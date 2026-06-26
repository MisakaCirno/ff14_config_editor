using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    public BackupMetadata CreateBackup(
        string sourceFilePath,
        bool cleanupAfterCreate = true,
        string creationTrigger = "")
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("找不到要备份的 UISAVE.DAT 文件。", sourceFilePath);
        }

        DateTime backupTime = DateTime.Now;
        string backupTimeId = backupTime.ToString("yyyyMMdd_HHmmss_fff");
        string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        string stagingDirectory = Path.Combine(BackupsDirectory, $".creating_{backupTimeId}_{uniqueSuffix}");
        string stagingBackupFilePath = Path.Combine(stagingDirectory, BackupDataFileName);

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            SafeFileWriter.Copy(sourceFilePath, stagingBackupFilePath);

            ConfigUISave backupConfig = new(stagingBackupFilePath);
            BackupMetadata metadata = CreateMetadata(
                sourceFilePath,
                stagingBackupFilePath,
                backupConfig,
                backupTime,
                uniqueSuffix,
                creationTrigger);
            WriteJson(Path.Combine(stagingDirectory, MetadataFileName), metadata);

            string backupDirectory = Path.Combine(BackupsDirectory, metadata.Id);
            Directory.Move(stagingDirectory, backupDirectory);

            metadata.BackupDirectory = backupDirectory;
            metadata.BackupFilePath = Path.Combine(backupDirectory, BackupDataFileName);

            if (cleanupAfterCreate)
            {
                CleanupBackups(metadata.BackupDirectory);
            }

            return metadata;
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            throw;
        }
    }

    public List<BackupMetadata> LoadBackups()
    {
        EnsureDataDirectory();
        if (!Directory.Exists(BackupsDirectory)) return [];

        List<BackupMetadata> backups = [];
        foreach (string metadataPath in Directory.EnumerateFiles(BackupsDirectory, MetadataFileName, SearchOption.AllDirectories))
        {
            JsonFileReadResult<BackupMetadata> metadataResult = ReadJsonFile<BackupMetadata>(metadataPath);
            if (metadataResult.Status == JsonFileReadStatus.Invalid)
            {
                AddJsonReadWarning(
                    metadataPath,
                    "备份元数据无法读取，已跳过这条备份记录。",
                    metadataResult.Error);
                continue;
            }

            BackupMetadata? metadata = metadataResult.Value;
            if (metadata == null) continue;
            metadata.MarkerSnapshots ??= [];

            metadata.BackupDirectory = Path.GetDirectoryName(metadataPath) ?? string.Empty;
            metadata.BackupFilePath = Path.Combine(metadata.BackupDirectory, BackupDataFileName);
            backups.Add(metadata);
        }

        return [.. backups.OrderByDescending(b => b.BackupTime)];
    }

    public void DeleteBackup(BackupMetadata backup)
    {
        if (!string.IsNullOrWhiteSpace(backup.BackupDirectory) && Directory.Exists(backup.BackupDirectory))
        {
            Directory.Delete(backup.BackupDirectory, recursive: true);
        }
    }

    public void RestoreBackup(BackupMetadata backup, string targetFilePath)
    {
        if (!File.Exists(backup.BackupFilePath))
        {
            throw new FileNotFoundException("找不到备份文件。", backup.BackupFilePath);
        }

        string? targetDirectory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        SafeFileWriter.Copy(backup.BackupFilePath, targetFilePath);
    }

    public void CleanupBackups(params string[] preservedBackupDirectories)
    {
        List<BackupMetadata> backups = LoadBackups();
        HashSet<string> deleteDirectories = [];
        HashSet<string> preservedDirectories = [.. preservedBackupDirectories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(NormalizeDirectoryPath)];

        if (Settings.LimitBackupDays && Settings.MaxBackupDays > 0)
        {
            DateTime cutoff = DateTime.Now.AddDays(-Settings.MaxBackupDays);
            foreach (BackupMetadata backup in backups.Where(b =>
                b.BackupTime < cutoff &&
                !IsPreservedBackup(b, preservedDirectories)))
            {
                AddDeleteDirectory(deleteDirectories, backup.BackupDirectory);
            }
        }

        if (Settings.LimitBackupCount && Settings.MaxBackupCount > 0)
        {
            foreach (BackupMetadata backup in GetBackupsExceedingCountLimit(
                backups,
                Settings.MaxBackupCount,
                preservedDirectories))
            {
                AddDeleteDirectory(deleteDirectories, backup.BackupDirectory);
            }
        }

        if (Settings.LimitBackupCountPerUser && Settings.MaxBackupCountPerUser > 0)
        {
            foreach (IGrouping<string, BackupMetadata> backupsByUser in backups
                .Where(b => !string.IsNullOrWhiteSpace(b.EffectiveUserID))
                .GroupBy(b => b.EffectiveUserID.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                foreach (BackupMetadata backup in GetBackupsExceedingCountLimit(
                    backupsByUser,
                    Settings.MaxBackupCountPerUser,
                    preservedDirectories))
                {
                    AddDeleteDirectory(deleteDirectories, backup.BackupDirectory);
                }
            }
        }

        foreach (string directory in deleteDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public bool IsTrustedGameCharacterSaveFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFileName(filePath), BackupDataFileName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(GetUserIDFromCharacterFolder(filePath)) &&
                IsInTrustedGameCharacterRootDirectory(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static IEnumerable<BackupMetadata> GetBackupsExceedingCountLimit(
        IEnumerable<BackupMetadata> backups,
        int maxBackupCount,
        HashSet<string> preservedDirectories)
    {
        return backups
            .OrderByDescending(b => b.BackupTime)
            .ThenByDescending(b => b.Id, StringComparer.OrdinalIgnoreCase)
            .Skip(maxBackupCount)
            .Where(b => !IsPreservedBackup(b, preservedDirectories));
    }

    private static bool IsPreservedBackup(BackupMetadata backup, HashSet<string> preservedDirectories)
    {
        return !string.IsNullOrWhiteSpace(backup.BackupDirectory) &&
            preservedDirectories.Contains(NormalizeDirectoryPath(backup.BackupDirectory));
    }

    private static void AddDeleteDirectory(HashSet<string> deleteDirectories, string directory)
    {
        if (!string.IsNullOrWhiteSpace(directory))
        {
            deleteDirectories.Add(directory);
        }
    }

    public static string? GetUserIDFromCharacterFolder(string filePath)
    {
        string? folderPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folderPath)) return null;

        return GameCharacterDirectoryName.TryGetUserIDFromDirectoryPath(folderPath, out string? userID)
            ? userID
            : null;
    }

    private BackupMetadata CreateMetadata(
        string sourceFilePath,
        string backedUpFilePath,
        ConfigUISave backupConfig,
        DateTime backupTime,
        string uniqueSuffix,
        string creationTrigger)
    {
        string backupTimeId = backupTime.ToString("yyyyMMdd_HHmmss_fff");
        string folderUserID = GetUserIDFromCharacterFolder(sourceFilePath) ?? string.Empty;
        string fileUserID = backupConfig.UserIDHex;
        bool useFolderUserID = IsTrustedGameCharacterSaveFile(sourceFilePath);
        string userIDForName = useFolderUserID ? folderUserID : fileUserID;

        return new BackupMetadata
        {
            Id = $"{backupTimeId}_{SanitizeFileName(userIDForName, "UNKNOWN")}_{uniqueSuffix}",
            BackupTime = backupTime,
            OriginalFilePath = sourceFilePath,
            OriginalDirectory = Path.GetDirectoryName(sourceFilePath) ?? string.Empty,
            FolderUserID = folderUserID,
            FileUserID = fileUserID,
            UseFolderUserIDAsEffectiveUserID = useFolderUserID,
            CreationTrigger = creationTrigger ?? string.Empty,
            SourceFileSize = new FileInfo(backedUpFilePath).Length,
            SourceFileSha256 = ComputeSha256(backedUpFilePath),
            MarkerSnapshots = CreateMarkerSnapshots(backupConfig.Marks)
        };
    }

    private bool IsInTrustedGameCharacterRootDirectory(string filePath)
    {
        string? characterDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(characterDirectory))
        {
            return false;
        }

        string? rootDirectory = Path.GetDirectoryName(characterDirectory);
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return false;
        }

        string normalizedRootDirectory = NormalizeDirectoryPath(rootDirectory);
        return EnumerateTrustedGameCharacterRootDirectories()
            .Any(directory => string.Equals(directory, normalizedRootDirectory, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<string> EnumerateTrustedGameCharacterRootDirectories()
    {
        if (WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            Settings.GameInstallDirectory,
            out string? configuredRootDirectory) &&
            !string.IsNullOrWhiteSpace(configuredRootDirectory))
        {
            yield return NormalizeDirectoryPath(configuredRootDirectory);
        }

    }

    private static List<BackupMarkerSnapshot> CreateMarkerSnapshots(SectionFMARKER? marks)
    {
        if (marks == null) return [];

        List<BackupMarkerSnapshot> snapshots = [];
        for (int index = 0; index < marks.WayMarks.Count; index++)
        {
            WayMark mark = marks.WayMarks[index];
            if (mark.RegionID == 0) continue;

            snapshots.Add(new BackupMarkerSnapshot
            {
                SlotIndex = index + 1,
                RegionID = mark.RegionID,
                RegionName = MapData.GetName(mark.RegionID),
                SlotCount = 1,
                EnabledPointCount = CountEnabledPoints(mark)
            });
        }

        return snapshots;
    }

    private static int CountEnabledPoints(WayMark mark)
    {
        int count = 0;
        if (mark.AEnabled) count++;
        if (mark.BEnabled) count++;
        if (mark.CEnabled) count++;
        if (mark.DEnabled) count++;
        if (mark.OneEnabled) count++;
        if (mark.TwoEnabled) count++;
        if (mark.ThreeEnabled) count++;
        if (mark.FourEnabled) count++;
        return count;
    }

    private static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string SanitizeFileName(string value, string fallback)
    {
        string sanitized = string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string NormalizeDirectoryPath(string directory)
    {
        return Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

}
