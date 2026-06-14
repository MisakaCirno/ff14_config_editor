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
    public BackupMetadata CreateBackup(string sourceFilePath, bool cleanupAfterCreate = true)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("找不到要备份的 UISAVE.DAT 文件。", sourceFilePath);
        }

        ConfigUISave sourceConfig = new(sourceFilePath);
        BackupMetadata metadata = CreateMetadata(sourceFilePath, sourceConfig);
        string backupDirectory = Path.Combine(BackupsDirectory, metadata.Id);
        string backupFilePath = Path.Combine(backupDirectory, BackupDataFileName);

        Directory.CreateDirectory(backupDirectory);
        SafeFileWriter.Copy(sourceFilePath, backupFilePath);
        metadata.BackupDirectory = backupDirectory;
        metadata.BackupFilePath = backupFilePath;
        WriteJson(Path.Combine(backupDirectory, MetadataFileName), metadata);

        if (cleanupAfterCreate)
        {
            CleanupBackups();
        }

        return metadata;
    }

    public List<BackupMetadata> LoadBackups()
    {
        EnsureDataDirectory();
        if (!Directory.Exists(BackupsDirectory)) return [];

        List<BackupMetadata> backups = [];
        foreach (string metadataPath in Directory.EnumerateFiles(BackupsDirectory, MetadataFileName, SearchOption.AllDirectories))
        {
            BackupMetadata? metadata = ReadJson<BackupMetadata>(metadataPath);
            if (metadata == null) continue;

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
                !preservedDirectories.Contains(NormalizeDirectoryPath(b.BackupDirectory))))
            {
                deleteDirectories.Add(backup.BackupDirectory);
            }
        }

        if (Settings.LimitBackupCount && Settings.MaxBackupCount > 0)
        {
            foreach (BackupMetadata backup in backups
                .Where(b => !preservedDirectories.Contains(NormalizeDirectoryPath(b.BackupDirectory)))
                .OrderByDescending(b => b.BackupTime)
                .Skip(Settings.MaxBackupCount))
            {
                deleteDirectories.Add(backup.BackupDirectory);
            }
        }

        foreach (string directory in deleteDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static string? GetUserIDFromCharacterFolder(string filePath)
    {
        string? folderPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folderPath)) return null;

        string folderName = new DirectoryInfo(folderPath).Name;
        const string prefix = "FFXIV_CHR";
        return folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? folderName[prefix.Length..].ToUpperInvariant()
            : null;
    }

    private BackupMetadata CreateMetadata(string sourceFilePath, ConfigUISave sourceConfig)
    {
        string backupTimeId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        string folderUserID = GetUserIDFromCharacterFolder(sourceFilePath) ?? string.Empty;
        string fileUserID = sourceConfig.UserIDHex;
        string userIDForName = !string.IsNullOrWhiteSpace(fileUserID) ? fileUserID : folderUserID;

        return new BackupMetadata
        {
            Id = $"{backupTimeId}_{SanitizeFileName(userIDForName, "UNKNOWN")}_{uniqueSuffix}",
            BackupTime = DateTime.Now,
            OriginalFilePath = sourceFilePath,
            OriginalDirectory = Path.GetDirectoryName(sourceFilePath) ?? string.Empty,
            FolderUserID = folderUserID,
            FileUserID = fileUserID,
            SourceFileSize = new FileInfo(sourceFilePath).Length,
            SourceFileSha256 = ComputeSha256(sourceFilePath),
            MarkerSnapshots = CreateMarkerSnapshots(sourceConfig.Marks)
        };
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
