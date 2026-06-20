using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
            string? gameCharacterDirectory = FindGameCharacterDirectory(recentFiles, candidateRoots);
            if (!string.IsNullOrWhiteSpace(gameCharacterDirectory))
            {
                return gameCharacterDirectory;
            }
        }

        return FindLastOpenedDirectory(recentFiles);
    }

    private static IEnumerable<string> CreateDefaultCandidateRoots()
    {
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfilePath))
        {
            userProfilePath = Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty;
        }

        string[] candidates =
        [
            CombineGameConfigDirectory(documentsPath),
            CombineGameConfigDirectory(Path.Combine(userProfilePath, "Documents")),
            CombineGameConfigDirectory(Path.Combine(userProfilePath, "OneDrive", "Documents")),
            CombineGameConfigDirectory(Path.Combine(userProfilePath, "OneDrive", "文档"))
        ];

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string CombineGameConfigDirectory(string baseDirectory)
    {
        return string.IsNullOrWhiteSpace(baseDirectory)
            ? string.Empty
            : Path.Combine(baseDirectory, "My Games", GameConfigFolderName);
    }

    private static string? FindGameCharacterDirectory(IEnumerable<string> recentFiles, IEnumerable<string> candidateRoots)
    {
        foreach (string candidateRoot in candidateRoots)
        {
            if (TryFindCharacterDirectoryFromRoot(candidateRoot, out string? characterDirectory))
            {
                return characterDirectory;
            }
        }

        foreach (string recentFile in recentFiles)
        {
            if (TryGetCharacterDirectoryFromWayMarkFile(recentFile, out string? characterDirectory))
            {
                return characterDirectory;
            }
        }

        return null;
    }

    private static bool TryFindCharacterDirectoryFromRoot(string rootDirectory, out string? characterDirectory)
    {
        characterDirectory = null;
        try
        {
            if (!Directory.Exists(rootDirectory))
            {
                return false;
            }

            var candidates = new List<(string Directory, DateTime UpdatedAt)>();
            foreach (string directory in Directory.EnumerateDirectories(rootDirectory, CharacterFolderPrefix + "*"))
            {
                string wayMarkFilePath = Path.Combine(directory, WayMarkFileName);
                if (!File.Exists(wayMarkFilePath))
                {
                    continue;
                }

                candidates.Add((directory, GetFileLastWriteTimeUtc(wayMarkFilePath)));
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            characterDirectory = Path.GetFullPath(candidates
                .OrderByDescending(candidate => candidate.UpdatedAt)
                .First()
                .Directory);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static DateTime GetFileLastWriteTimeUtc(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return DateTime.MinValue;
        }
    }

    private static bool TryGetCharacterDirectoryFromWayMarkFile(string filePath, out string? characterDirectoryPath)
    {
        characterDirectoryPath = null;
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
            !Directory.Exists(characterDirectory.FullName))
        {
            return false;
        }

        characterDirectoryPath = Path.GetFullPath(characterDirectory.FullName);
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
}