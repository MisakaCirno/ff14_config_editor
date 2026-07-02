using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace UIMarkerEditor;

internal static class WayMarkOpenDirectoryResolver
{
    private const string GameConfigFolderName = "FINAL FANTASY XIV - A Realm Reborn";
    private const string BootDirectoryName = "boot";
    private const string GameDirectoryName = "game";
    private const string MyGamesDirectoryName = "My Games";
    private const string SaveFileName = "UISAVE.DAT";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private static readonly string[] GameExecutableNames = ["ffxiv_dx11.exe", "ffxiv.exe"];
    private static readonly string[] GameProcessNames = ["ffxiv_dx11", "ffxiv"];

    public static string? Resolve(
        WayMarkOpenDirectoryMode mode,
        string customDirectory,
        string gameInstallDirectory)
    {
        if (mode == WayMarkOpenDirectoryMode.GameCharacterDirectory &&
            TryResolveGameCharacterRootDirectory(gameInstallDirectory, out string? gameCharacterRootDirectory))
        {
            return gameCharacterRootDirectory;
        }

        if (mode == WayMarkOpenDirectoryMode.CustomDirectory &&
            TryNormalizeExistingDirectory(customDirectory, out string? customDirectoryPath))
        {
            return customDirectoryPath;
        }

        return null;
    }

    public static string? Resolve(
        WayMarkOpenDirectoryMode mode,
        string customDirectory)
    {
        return Resolve(mode, customDirectory, string.Empty);
    }

    public static string? AutoDetectGameInstallDirectory()
    {
        return AutoDetectGameInstallDirectory(CreateDefaultGameInstallDirectoryCandidates());
    }

    internal static string? AutoDetectGameInstallDirectory(IEnumerable<string> candidatePaths)
    {
        foreach (string candidatePath in candidatePaths)
        {
            foreach (string candidatePathVariant in PathTextRepair.EnumerateCommonUtf8MojibakeVariants(candidatePath))
            {
                if (TryNormalizeGameInstallDirectory(candidatePathVariant, out string? gameInstallDirectory))
                {
                    return gameInstallDirectory;
                }
            }
        }

        return null;
    }

    public static string? DetectRunningGameInstallDirectory()
    {
        foreach (string processName in GameProcessNames)
        {
            foreach (Process process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (TryGetProcessExecutablePath(process, out string? executablePath) &&
                            TryNormalizeGameInstallDirectory(executablePath, out string? gameInstallDirectory))
                        {
                            return gameInstallDirectory;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                    }
                }
            }
        }

        return null;
    }

    internal static bool TryGetProcessExecutablePath(
        Process process,
        [NotNullWhen(true)] out string? executablePath)
    {
        executablePath = null;
        try
        {
            return TryQueryFullProcessImageName(process.Id, out executablePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryQueryFullProcessImageName(
        int processId,
        [NotNullWhen(true)] out string? executablePath)
    {
        executablePath = null;
        IntPtr processHandle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            int capacity = 32768;
            StringBuilder buffer = new(capacity);
            if (!QueryFullProcessImageName(processHandle, 0, buffer, ref capacity))
            {
                return false;
            }

            executablePath = buffer.ToString();
            return !string.IsNullOrWhiteSpace(executablePath);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    internal static bool TryNormalizeGameInstallDirectory(
        string? path,
        [NotNullWhen(true)] out string? normalizedInstallDirectory)
    {
        normalizedInstallDirectory = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string repairedPath = PathTextRepair.RepairCommonUtf8Mojibake(path.Trim());
            string fullPath = Path.GetFullPath(repairedPath);
            string? directory = File.Exists(fullPath)
                ? Path.GetDirectoryName(fullPath)
                : Directory.Exists(fullPath)
                    ? fullPath
                    : null;
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            foreach (string installDirectoryCandidate in EnumerateCandidateGameInstallDirectoriesFromDirectory(directory))
            {
                if (TryValidateGameInstallDirectory(installDirectoryCandidate, out normalizedInstallDirectory))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
        }

        return false;
    }

    internal static bool TryResolveGameDirectory(
        string? gameInstallDirectory,
        [NotNullWhen(true)] out string? gameDirectory)
    {
        gameDirectory = null;
        if (!TryNormalizeGameInstallDirectory(gameInstallDirectory, out string? normalizedInstallDirectory))
        {
            return false;
        }

        string candidateGameDirectory = Path.Combine(normalizedInstallDirectory, GameDirectoryName);
        if (!Directory.Exists(candidateGameDirectory))
        {
            return false;
        }

        gameDirectory = Path.GetFullPath(candidateGameDirectory);
        return true;
    }

    internal static bool TryResolveGameExecutablePath(
        string? gameInstallDirectory,
        [NotNullWhen(true)] out string? gameExecutablePath)
    {
        gameExecutablePath = null;
        if (!TryResolveGameDirectory(gameInstallDirectory, out string? gameDirectory))
        {
            return false;
        }

        foreach (string executableName in GameExecutableNames)
        {
            string candidateExecutablePath = Path.Combine(gameDirectory, executableName);
            if (File.Exists(candidateExecutablePath))
            {
                gameExecutablePath = Path.GetFullPath(candidateExecutablePath);
                return true;
            }
        }

        return false;
    }

    internal static bool TryResolveGameCharacterRootDirectory(
        string? gameInstallDirectory,
        [NotNullWhen(true)] out string? gameCharacterRootDirectory)
    {
        gameCharacterRootDirectory = null;
        if (!TryResolveGameDirectory(gameInstallDirectory, out string? gameDirectory))
        {
            return false;
        }

        string candidateRoot = Path.Combine(gameDirectory, "My Games", GameConfigFolderName);
        foreach (string candidateRootVariant in PathTextRepair.EnumerateCommonUtf8MojibakeVariants(candidateRoot))
        {
            if (TryNormalizeExistingDirectory(candidateRootVariant, out string? directory))
            {
                gameCharacterRootDirectory = directory;
                return true;
            }
        }

        return false;
    }

    internal static bool TryInferGameInstallDirectoryFromSaveFile(
        string? filePath,
        [NotNullWhen(true)] out string? normalizedInstallDirectory)
    {
        normalizedInstallDirectory = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            string repairedPath = PathTextRepair.RepairCommonUtf8Mojibake(filePath.Trim());
            string fullPath = Path.GetFullPath(repairedPath);
            if (!string.Equals(Path.GetFileName(fullPath), SaveFileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(fullPath))
            {
                return false;
            }

            string? characterDirectory = Path.GetDirectoryName(fullPath);
            if (!GameCharacterDirectoryName.TryGetUserIDFromDirectoryPath(characterDirectory, out _))
            {
                return false;
            }

            string? gameConfigDirectory = Path.GetDirectoryName(characterDirectory);
            if (!HasDirectoryName(gameConfigDirectory, GameConfigFolderName))
            {
                return false;
            }

            string? myGamesDirectory = Path.GetDirectoryName(gameConfigDirectory);
            if (!HasDirectoryName(myGamesDirectory, MyGamesDirectoryName))
            {
                return false;
            }

            string? gameDirectory = Path.GetDirectoryName(myGamesDirectory);
            if (!IsGameDirectory(gameDirectory ?? string.Empty))
            {
                return false;
            }

            string? installDirectory = Path.GetDirectoryName(gameDirectory);
            return !string.IsNullOrWhiteSpace(installDirectory) &&
                TryValidateGameInstallDirectory(installDirectory, out normalizedInstallDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    internal static IEnumerable<string> CreateCandidateGameInstallDirectoriesFromInstallPath(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            yield break;
        }

        string trimmedPath = installPath.Trim();
        string? directory = trimmedPath;
        try
        {
            if (Path.HasExtension(trimmedPath))
            {
                directory = Path.GetDirectoryName(trimmedPath);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            yield break;
        }

        foreach (string candidateDirectory in EnumerateCandidateGameInstallDirectoriesFromDirectory(directory))
        {
            yield return candidateDirectory;
        }
    }

    internal static bool IsGameCharacterRootDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            string trimmedPath = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetFileName(trimmedPath), GameConfigFolderName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IEnumerable<string> CreateDefaultGameInstallDirectoryCandidates()
    {
        return EnumerateRegistryInstallCandidatePaths()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateRegistryInstallCandidatePaths()
    {
        const string uninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (RegistryHive hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                using RegistryKey? baseKey = TryOpenBaseKey(hive, view);
                using RegistryKey? uninstallKey = baseKey?.OpenSubKey(uninstallSubKey);
                if (uninstallKey == null)
                {
                    continue;
                }

                foreach (string subKeyName in EnumerateRegistrySubKeyNames(uninstallKey))
                {
                    using RegistryKey? appKey = TryOpenSubKey(uninstallKey, subKeyName);
                    if (appKey == null || !IsFinalFantasyXivDisplayName(ReadRegistryString(appKey, "DisplayName")))
                    {
                        continue;
                    }

                    foreach (string installPath in EnumerateRegistryInstallPaths(appKey))
                    {
                        yield return installPath;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateRegistryInstallPaths(RegistryKey appKey)
    {
        foreach (string valueName in new[] { "InstallLocation", "InstallSource", "DisplayIcon", "UninstallString" })
        {
            string? value = ReadRegistryString(appKey, valueName);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string path = ExtractPathFromRegistryValue(value);
            if (!string.IsNullOrWhiteSpace(path))
            {
                foreach (string pathVariant in PathTextRepair.EnumerateCommonUtf8MojibakeVariants(path))
                {
                    yield return pathVariant;
                }
            }
        }
    }

    private static string ExtractPathFromRegistryValue(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed[0] == '"')
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                trimmed = trimmed[1..endQuote];
            }
        }
        else
        {
            int exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                trimmed = trimmed[..(exeIndex + 4)];
            }
        }

        return trimmed;
    }

    private static RegistryKey? TryOpenBaseKey(RegistryHive hive, RegistryView view)
    {
        try
        {
            return RegistryKey.OpenBaseKey(hive, view);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static RegistryKey? TryOpenSubKey(RegistryKey key, string name)
    {
        try
        {
            return key.OpenSubKey(name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateRegistrySubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static string? ReadRegistryString(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsFinalFantasyXivDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        foreach (string displayNameVariant in PathTextRepair.EnumerateCommonUtf8MojibakeVariants(displayName))
        {
            if (displayNameVariant.Contains("FINAL FANTASY XIV", StringComparison.OrdinalIgnoreCase) ||
                displayNameVariant.Contains("最终幻想XIV", StringComparison.OrdinalIgnoreCase) ||
                displayNameVariant.Contains("最终幻想14", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidateGameInstallDirectoriesFromDirectory(string directory)
    {
        string trimmedDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsGameDirectory(trimmedDirectory) || IsBootDirectory(trimmedDirectory))
        {
            string? parentDirectory = TryGetDirectoryName(trimmedDirectory);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                yield return parentDirectory;
            }
        }

        yield return trimmedDirectory;
    }

    private static bool TryValidateGameInstallDirectory(
        string directory,
        [NotNullWhen(true)] out string? normalizedDirectory)
    {
        normalizedDirectory = null;
        try
        {
            string fullDirectory = Path.GetFullPath(directory.Trim());
            if (!Directory.Exists(fullDirectory))
            {
                return false;
            }

            string gameDirectory = Path.Combine(fullDirectory, GameDirectoryName);
            if (!Directory.Exists(gameDirectory) || !GameExecutableNames.Any(name => File.Exists(Path.Combine(gameDirectory, name))))
            {
                return false;
            }

            normalizedDirectory = fullDirectory;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsGameDirectory(string path)
    {
        try
        {
            string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetFileName(trimmedPath), GameDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool HasDirectoryName(string? path, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetFileName(trimmedPath), expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsBootDirectory(string path)
    {
        try
        {
            string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetFileName(trimmedPath), BootDirectoryName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? TryGetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool TryNormalizeExistingDirectory(
        string directory,
        [NotNullWhen(true)] out string? normalizedDirectory)
    {
        normalizedDirectory = null;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            normalizedDirectory = Path.GetFullPath(directory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        int flags,
        StringBuilder executablePath,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
