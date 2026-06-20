using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace UIMarkerEditor;

internal static class WayMarkOpenDirectoryResolver
{
    private const string CharacterFolderPrefix = "FFXIV_CHR";
    private const string GameConfigFolderName = "FINAL FANTASY XIV - A Realm Reborn";
    private const string WayMarkFileName = "UISAVE.DAT";

    public static string? Resolve(WayMarkOpenDirectoryMode mode, IEnumerable<string> recentFiles)
    {
        return Resolve(mode, recentFiles, CreateDefaultCandidateRoots());
    }

    internal static string? Resolve(
        WayMarkOpenDirectoryMode mode,
        IEnumerable<string> recentFiles,
        IEnumerable<string> candidateRoots)
    {
        if (mode == WayMarkOpenDirectoryMode.GameCharacterDirectory)
        {
            string? gameCharacterRootDirectory = FindGameCharacterRootDirectory(recentFiles, candidateRoots);
            if (!string.IsNullOrWhiteSpace(gameCharacterRootDirectory))
            {
                return gameCharacterRootDirectory;
            }
        }

        return FindLastOpenedDirectory(recentFiles);
    }

    private static IEnumerable<string> CreateDefaultCandidateRoots()
    {
        return EnumerateRegistryInstallCandidateRoots()
            .Concat(EnumerateLikelyInstallCandidateRoots())
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
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static IEnumerable<string> EnumerateLikelyInstallCandidateRoots()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!IsUsableDrive(drive))
            {
                continue;
            }

            string root = drive.RootDirectory.FullName;
            foreach (string installDirectory in EnumerateLikelyInstallDirectories(root))
            {
                foreach (string candidateRoot in CreateCandidateRootsFromInstallPath(installDirectory))
                {
                    yield return candidateRoot;
                }
            }
        }
    }

    private static bool IsUsableDrive(DriveInfo drive)
    {
        try
        {
            return drive.IsReady &&
                (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateLikelyInstallDirectories(string driveRoot)
    {
        string[] directInstallNames =
        [
            "最终幻想XIV",
            "最终幻想14",
            "FINAL FANTASY XIV",
            "FINAL FANTASY XIV - A Realm Reborn",
            "FFXIV"
        ];
        foreach (string name in directInstallNames)
        {
            yield return Path.Combine(driveRoot, name);
        }

        string[] likelyParents =
        [
            "Software",
            "Games",
            "Game",
            "Program Files",
            "Program Files (x86)",
            "SquareEnix",
            "WeGameApps",
            "WeGame",
            "腾讯游戏",
            "上海数龙科技有限公司"
        ];
        foreach (string parentName in likelyParents)
        {
            string parentDirectory = Path.Combine(driveRoot, parentName);
            foreach (string childDirectory in EnumerateDirectoriesSafely(parentDirectory))
            {
                yield return childDirectory;
            }
        }
    }

    internal static IEnumerable<string> CreateCandidateRootsFromInstallPath(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            yield break;
        }

        string trimmedPath = installPath.Trim();
        yield return CombineGameConfigDirectory(trimmedPath);
        yield return CombineGameConfigDirectory(Path.Combine(trimmedPath, "game"));

        if (string.Equals(Path.GetFileName(trimmedPath), "game", StringComparison.OrdinalIgnoreCase))
        {
            yield return CombineGameConfigDirectory(trimmedPath);
        }
    }

    private static string CombineGameConfigDirectory(string gameDirectory)
    {
        return string.IsNullOrWhiteSpace(gameDirectory)
            ? string.Empty
            : Path.Combine(gameDirectory, "My Games", GameConfigFolderName);
    }

    private static string? FindGameCharacterRootDirectory(IEnumerable<string> recentFiles, IEnumerable<string> candidateRoots)
    {
        foreach (string candidateRoot in candidateRoots)
        {
            if (IsGameCharacterRootDirectory(candidateRoot))
            {
                return Path.GetFullPath(candidateRoot);
            }
        }

        foreach (string recentFile in recentFiles)
        {
            if (TryGetGameCharacterRootFromWayMarkFile(recentFile, out string? rootDirectory))
            {
                return rootDirectory;
            }
        }

        return null;
    }

    private static bool IsGameCharacterRootDirectory(string rootDirectory)
    {
        try
        {
            return Directory.Exists(rootDirectory) &&
                Directory.EnumerateDirectories(rootDirectory, CharacterFolderPrefix + "*").Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryGetGameCharacterRootFromWayMarkFile(string filePath, out string? rootDirectory)
    {
        rootDirectory = null;
        if (string.IsNullOrWhiteSpace(filePath) ||
            !string.Equals(Path.GetFileName(filePath), WayMarkFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        DirectoryInfo? characterDirectory;
        try
        {
            characterDirectory = Directory.GetParent(filePath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        if (characterDirectory == null ||
            !characterDirectory.Name.StartsWith(CharacterFolderPrefix, StringComparison.OrdinalIgnoreCase) ||
            characterDirectory.Parent == null ||
            !Directory.Exists(characterDirectory.Parent.FullName))
        {
            return false;
        }

        rootDirectory = Path.GetFullPath(characterDirectory.Parent.FullName);
        return true;
    }

    private static string? FindLastOpenedDirectory(IEnumerable<string> recentFiles)
    {
        foreach (string recentFile in recentFiles)
        {
            string? directory;
            try
            {
                directory = Path.GetDirectoryName(recentFile);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            return Path.GetFullPath(directory);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateDirectories(directory).ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }
}
