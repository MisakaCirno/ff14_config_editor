using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace UIMarkerEditor;

internal static class WayMarkOpenDirectoryResolver
{
    private const string GameConfigFolderName = "FINAL FANTASY XIV - A Realm Reborn";

    public static string? Resolve(
        WayMarkOpenDirectoryMode mode,
        string customDirectory)
    {
        if (mode == WayMarkOpenDirectoryMode.CustomDirectory &&
            TryNormalizeExistingDirectory(customDirectory, out string? directory))
        {
            return directory;
        }

        return null;
    }

    public static string? AutoDetectGameCharacterRootDirectory()
    {
        return AutoDetectGameCharacterRootDirectory(CreateDefaultCandidateRoots());
    }

    internal static string? AutoDetectGameCharacterRootDirectory(IEnumerable<string> candidateRoots)
    {
        foreach (string candidateRoot in candidateRoots)
        {
            if (TryNormalizeDirectoryPath(candidateRoot, out string? directory))
            {
                return directory;
            }
        }

        return null;
    }

    private static IEnumerable<string> CreateDefaultCandidateRoots()
    {
        return EnumerateRegistryInstallCandidateRoots()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateRegistryInstallCandidateRoots()
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
                        foreach (string candidateRoot in CreateCandidateRootsFromInstallPath(installPath))
                        {
                            yield return candidateRoot;
                        }
                    }
                }
            }
        }
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
        return !string.IsNullOrWhiteSpace(displayName) &&
            (displayName.Contains("FINAL FANTASY XIV", StringComparison.OrdinalIgnoreCase) ||
             displayName.Contains("最终幻想XIV", StringComparison.OrdinalIgnoreCase) ||
             displayName.Contains("最终幻想14", StringComparison.OrdinalIgnoreCase));
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
                yield return path;
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

        try
        {
            return Path.HasExtension(trimmed)
                ? Path.GetDirectoryName(trimmed) ?? string.Empty
                : trimmed;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    internal static IEnumerable<string> CreateCandidateRootsFromInstallPath(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            yield break;
        }

        string trimmedPath = installPath.Trim();
        if (IsGameDirectory(trimmedPath))
        {
            yield return CombineGameConfigDirectory(trimmedPath);
            yield break;
        }

        yield return CombineGameConfigDirectory(Path.Combine(trimmedPath, "game"));
        yield return CombineGameConfigDirectory(trimmedPath);
    }

    private static bool IsGameDirectory(string path)
    {
        try
        {
            string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetFileName(trimmedPath), "game", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string CombineGameConfigDirectory(string gameDirectory)
    {
        return string.IsNullOrWhiteSpace(gameDirectory)
            ? string.Empty
            : Path.Combine(gameDirectory, "My Games", GameConfigFolderName);
    }

    private static bool TryNormalizeDirectoryPath(string directory, out string? normalizedDirectory)
    {
        normalizedDirectory = null;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            normalizedDirectory = Path.GetFullPath(directory.Trim());
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryNormalizeExistingDirectory(string directory, out string? normalizedDirectory)
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
}
