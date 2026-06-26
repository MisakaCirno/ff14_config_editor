using System.IO;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    internal IReadOnlyList<LocalGameCharacter> GetAvailableLocalGameCharacters()
    {
        if (!WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            Settings.GameInstallDirectory,
            out string? gameCharacterRootDirectory))
        {
            return [];
        }

        Dictionary<string, CharacterProfile> profilesByUserID = Characters
            .Where(static character => !string.IsNullOrWhiteSpace(character.UserID))
            .GroupBy(static character => character.UserID.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        List<LocalGameCharacter> characters = [];
        foreach (LocalGameCharacterFile characterFile in EnumerateLocalGameCharacterFiles(gameCharacterRootDirectory))
        {
            if (!profilesByUserID.TryGetValue(characterFile.UserID, out CharacterProfile? profile))
            {
                continue;
            }

            characters.Add(CreateLocalGameCharacter(characterFile, profile));
        }

        return [.. characters
            .OrderBy(static character => string.IsNullOrWhiteSpace(character.CharacterName) ? 1 : 0)
            .ThenBy(static character => character.CharacterName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static character => character.UserID, StringComparer.OrdinalIgnoreCase)];
    }

    internal LocalGameCharacterScanPreparation PrepareLocalGameCharacterScan()
    {
        if (!WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            Settings.GameInstallDirectory,
            out string? gameCharacterRootDirectory))
        {
            return new LocalGameCharacterScanPreparation(string.Empty, [], []);
        }

        List<LocalGameCharacterFile> characterFiles = [.. EnumerateLocalGameCharacterFiles(gameCharacterRootDirectory)];
        List<ClientLogCharacterNameScanError> errors = [];
        List<LocalGameCharacterScanItem> items = [];
        foreach (LocalGameCharacterFile characterFile in characterFiles)
        {
            ClientLogCharacterNameMatch? logNameMatch = ClientLogCharacterNameResolver.FindLatestFromCharacterDirectory(
                characterFile.CharacterDirectory,
                errors);
            items.Add(new LocalGameCharacterScanItem(
                characterFile.UserID,
                characterFile.CharacterDirectory,
                characterFile.SaveFilePath,
                characterFile.SaveFileLastWriteTime,
                logNameMatch?.CharacterName ?? string.Empty));
        }

        return new LocalGameCharacterScanPreparation(gameCharacterRootDirectory, items, errors);
    }

    internal LocalGameCharacterScanResult ApplyLocalGameCharacterScan(LocalGameCharacterScanPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (!IsCurrentLocalGameCharacterRoot(preparation.GameCharacterRootDirectory))
        {
            return new LocalGameCharacterScanResult(
                preparation.Items.Count,
                0,
                0,
                0,
                preparation.Errors,
                SkippedBecauseGameInstallDirectoryChanged: true);
        }

        List<CharacterProfile> snapshot = CloneCharacterProfilesForLocalScan();
        int createdProfileCount = 0;
        int importedCharacterNameCount = 0;
        int unchangedProfileCount = 0;

        try
        {
            foreach (LocalGameCharacterScanItem item in preparation.Items)
            {
                CharacterProfile? existingProfile = Characters.FirstOrDefault(character =>
                    string.Equals(character.UserID, item.UserID, StringComparison.OrdinalIgnoreCase));
                bool isNewProfile = existingProfile == null;
                CharacterProfile profile = existingProfile ?? GetOrCreateCharacter(item.UserID);
                if (isNewProfile)
                {
                    createdProfileCount++;
                }

                if (!string.IsNullOrWhiteSpace(profile.CharacterName))
                {
                    if (!isNewProfile)
                    {
                        unchangedProfileCount++;
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.CharacterNameFromLog))
                {
                    if (!isNewProfile)
                    {
                        unchangedProfileCount++;
                    }

                    continue;
                }

                profile.CharacterName = item.CharacterNameFromLog;
                profile.UpdatedAt = DateTime.Now;
                importedCharacterNameCount++;
            }

            if (createdProfileCount > 0 || importedCharacterNameCount > 0)
            {
                SaveCharacters();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            RestoreCharacterProfilesForLocalScan(snapshot);
            throw;
        }

        return new LocalGameCharacterScanResult(
            preparation.Items.Count,
            createdProfileCount,
            importedCharacterNameCount,
            unchangedProfileCount,
            preparation.Errors);
    }

    internal LocalGameCharacterScanResult ScanLocalGameCharacters()
    {
        return ApplyLocalGameCharacterScan(PrepareLocalGameCharacterScan());
    }

    private static IEnumerable<LocalGameCharacterFile> EnumerateLocalGameCharacterFiles(string gameCharacterRootDirectory)
    {
        string[] characterDirectories;
        try
        {
            characterDirectories = Directory.EnumerateDirectories(
                gameCharacterRootDirectory,
                "FFXIV_CHR*",
                SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            yield break;
        }

        foreach (string characterDirectory in characterDirectories.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!GameCharacterDirectoryName.TryGetUserIDFromDirectoryPath(characterDirectory, out string? userID))
            {
                continue;
            }

            string saveFilePath = Path.Combine(characterDirectory, BackupDataFileName);
            if (!File.Exists(saveFilePath))
            {
                continue;
            }

            DateTime saveFileLastWriteTime;
            string normalizedCharacterDirectory;
            try
            {
                normalizedCharacterDirectory = Path.GetFullPath(characterDirectory);
                saveFilePath = Path.GetFullPath(saveFilePath);
                saveFileLastWriteTime = File.GetLastWriteTime(saveFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            yield return new LocalGameCharacterFile(
                userID,
                normalizedCharacterDirectory,
                saveFilePath,
                saveFileLastWriteTime);
        }
    }

    private static LocalGameCharacter CreateLocalGameCharacter(
        LocalGameCharacterFile characterFile,
        CharacterProfile profile)
    {
        return new LocalGameCharacter
        {
            UserID = characterFile.UserID,
            CharacterName = profile.CharacterName,
            DataCenter = profile.DataCenter,
            World = profile.World,
            CharacterDirectory = characterFile.CharacterDirectory,
            SaveFilePath = characterFile.SaveFilePath,
            SaveFileLastWriteTime = characterFile.SaveFileLastWriteTime
        };
    }

    private List<CharacterProfile> CloneCharacterProfilesForLocalScan()
    {
        return [.. Characters.Select(static profile => new CharacterProfile
        {
            UserID = profile.UserID,
            CharacterName = profile.CharacterName,
            DataCenter = profile.DataCenter,
            World = profile.World,
            Note = profile.Note,
            UpdatedAt = profile.UpdatedAt
        })];
    }

    private bool IsCurrentLocalGameCharacterRoot(string gameCharacterRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameCharacterRootDirectory))
        {
            return true;
        }

        if (!WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            Settings.GameInstallDirectory,
            out string? currentGameCharacterRootDirectory))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(gameCharacterRootDirectory),
                Path.GetFullPath(currentGameCharacterRootDirectory),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void RestoreCharacterProfilesForLocalScan(IEnumerable<CharacterProfile> snapshot)
    {
        Characters.Clear();
        foreach (CharacterProfile profile in snapshot)
        {
            Characters.Add(profile);
        }
    }

    private sealed record LocalGameCharacterFile(
        string UserID,
        string CharacterDirectory,
        string SaveFilePath,
        DateTime SaveFileLastWriteTime);
}
