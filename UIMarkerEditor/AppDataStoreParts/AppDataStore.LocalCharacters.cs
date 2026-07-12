using System.IO;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    internal CharacterActivityScanPreparation PrepareCharacterActivityScan()
    {
        string gameCharacterRootDirectory = WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            Settings.GameInstallDirectory,
            out string? resolvedDirectory)
            ? resolvedDirectory
            : string.Empty;
        return new CharacterActivityScanPreparation(
            gameCharacterRootDirectory,
            [.. Characters.Select(static character => new CharacterActivityScanItem(
                character.UserID,
                character.DisplayName))]);
    }

    internal static CharacterActivityScanResult ScanCharacterActivity(
        CharacterActivityScanPreparation preparation,
        IProgress<CharacterActivityScanProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        List<CharacterActivityScanEntry> entries = [];
        int totalCount = preparation.Items.Count;
        for (int index = 0; index < totalCount; index++)
        {
            CharacterActivityScanItem item = preparation.Items[index];
            entries.Add(ScanCharacterActivityItem(preparation.GameCharacterRootDirectory, item));
            progress?.Report(new CharacterActivityScanProgress(index + 1, totalCount, item.DisplayName));
        }

        return new CharacterActivityScanResult(
            preparation.GameCharacterRootDirectory,
            entries,
            DateTime.UtcNow);
    }

    internal bool TryApplyCharacterActivityScan(CharacterActivityScanResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!IsCurrentCharacterActivityScanRoot(result.GameCharacterRootDirectory))
        {
            return false;
        }

        Dictionary<string, CharacterActivityScanEntry> entriesByUserID = result.Entries
            .GroupBy(static entry => entry.UserID, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (CharacterProfile profile in Characters)
        {
            if (!entriesByUserID.TryGetValue(profile.UserID, out CharacterActivityScanEntry? entry))
            {
                continue;
            }

            profile.LastActiveAtUtc = entry.LastActiveAtUtc;
            profile.LastActiveTimeDisplay = entry.State switch
            {
                CharacterActivityScanState.Available when entry.LastActiveAtUtc.HasValue =>
                    entry.LastActiveAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                CharacterActivityScanState.ReadFailed => "读取失败",
                _ => "无本地记录"
            };
        }

        return true;
    }

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
        int preparationCharactersRevision = GetCharactersRevision();
        if (!WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            Settings.GameInstallDirectory,
            out string? gameCharacterRootDirectory))
        {
            return new LocalGameCharacterScanPreparation(string.Empty, [], [], preparationCharactersRevision);
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

        return new LocalGameCharacterScanPreparation(
            gameCharacterRootDirectory,
            items,
            errors,
            preparationCharactersRevision);
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

        if (preparation.CharactersRevision != GetCharactersRevision())
        {
            return new LocalGameCharacterScanResult(
                preparation.Items.Count,
                0,
                0,
                0,
                preparation.Errors,
                SkippedBecauseCharactersChanged: true);
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

    private static CharacterActivityScanEntry ScanCharacterActivityItem(
        string gameCharacterRootDirectory,
        CharacterActivityScanItem item)
    {
        if (string.IsNullOrWhiteSpace(gameCharacterRootDirectory) ||
            string.IsNullOrWhiteSpace(item.UserID))
        {
            return new CharacterActivityScanEntry(
                item.UserID,
                null,
                CharacterActivityScanState.NoLocalRecord);
        }

        string characterDirectory;
        try
        {
            string normalizedUserID = item.UserID.Trim().ToUpperInvariant();
            if (normalizedUserID.Length != 16 || !normalizedUserID.All(Uri.IsHexDigit))
            {
                return new CharacterActivityScanEntry(
                    item.UserID,
                    null,
                    CharacterActivityScanState.NoLocalRecord);
            }

            characterDirectory = Path.Combine(
                gameCharacterRootDirectory,
                $"FFXIV_CHR{normalizedUserID}");

            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            DateTime? latestWriteTimeUtc = null;
            foreach (string filePath in Directory.EnumerateFiles(characterDirectory, "*", options))
            {
                string extension = Path.GetExtension(filePath);
                if (!string.Equals(extension, ".dat", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileInfo fileInfo = new(filePath);
                fileInfo.Refresh();
                if (!fileInfo.Exists)
                {
                    throw new FileNotFoundException("扫描期间文件已不存在。", filePath);
                }

                if (!latestWriteTimeUtc.HasValue || fileInfo.LastWriteTimeUtc > latestWriteTimeUtc.Value)
                {
                    latestWriteTimeUtc = fileInfo.LastWriteTimeUtc;
                }
            }

            return new CharacterActivityScanEntry(
                item.UserID,
                latestWriteTimeUtc,
                latestWriteTimeUtc.HasValue
                    ? CharacterActivityScanState.Available
                    : CharacterActivityScanState.NoLocalRecord);
        }
        catch (DirectoryNotFoundException)
        {
            return new CharacterActivityScanEntry(
                item.UserID,
                null,
                CharacterActivityScanState.NoLocalRecord);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new CharacterActivityScanEntry(
                item.UserID,
                null,
                CharacterActivityScanState.ReadFailed,
                ex.Message);
        }
    }

    private bool IsCurrentCharacterActivityScanRoot(string scannedRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(scannedRootDirectory))
        {
            return !WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
                Settings.GameInstallDirectory,
                out _);
        }

        return IsCurrentLocalGameCharacterRoot(scannedRootDirectory);
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
            UpdatedAt = profile.UpdatedAt,
            LastActiveAtUtc = profile.LastActiveAtUtc,
            LastActiveTimeDisplay = profile.LastActiveTimeDisplay
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
