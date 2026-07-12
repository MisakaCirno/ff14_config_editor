using System.IO;
using UIMarkerEditor;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class BackupRestoreControlTests
{
    [Fact]
    public void IsSameFilePath_WhenPathTextDiffersButFullPathMatches_ReturnsTrue()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.BackupRestoreControlTests",
            Guid.NewGuid().ToString("N"));
        string nestedDirectory = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        try
        {
            string currentFilePath = Path.Combine(nestedDirectory, "UISAVE.DAT");
            string equivalentPath = Path.Combine(nestedDirectory, "..", "nested", "UISAVE.DAT");

            Assert.True(BackupRestoreControl.IsSameFilePath(currentFilePath, equivalentPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IsSameFilePath_WhenPathIsInvalid_ReturnsFalse()
    {
        Assert.False(BackupRestoreControl.IsSameFilePath("C:\\valid\\UISAVE.DAT", "\0"));
    }

    [Fact]
    public void BuildRestoreWarning_WhenOriginalTargetExists_WarnsAboutOverwrite()
    {
        BackupMetadata backup = CreateBackupMetadata();

        string warning = BackupRestoreControl.BuildRestoreWarning(
            backup,
            "C:\\game\\FFXIV_CHR1234\\UISAVE.DAT",
            targetExists: true,
            willCreateSafetyBackup: false);

        Assert.Contains("覆盖目标文件", warning);
        Assert.Contains("当前不会创建还原前安全备份", warning);
        Assert.Contains("覆盖后目标文件当前状态将无法从工具备份中恢复", warning);
    }

    [Fact]
    public void BuildRestoreWarning_WhenOriginalTargetIsMissing_WarnsAboutCreate()
    {
        BackupMetadata backup = CreateBackupMetadata();

        string warning = BackupRestoreControl.BuildRestoreWarning(
            backup,
            "C:\\game\\FFXIV_CHR1234\\UISAVE.DAT",
            targetExists: false,
            willCreateSafetyBackup: false);

        Assert.Contains("创建目标文件", warning);
        Assert.Contains("目标文件当前不存在", warning);
        Assert.DoesNotContain("覆盖目标文件", warning);
        Assert.DoesNotContain("覆盖后目标文件当前状态将无法从工具备份中恢复", warning);
    }

    [Fact]
    public void MatchesCurrentFileUserID_UsesEffectiveUserIDCaseInsensitively()
    {
        BackupMetadata folderBasedBackup = new()
        {
            FolderUserID = "AAAABBBBCCCCDDDD",
            FileUserID = "1111222233334444",
            UseFolderUserIDAsEffectiveUserID = true
        };
        BackupMetadata fileBasedBackup = new()
        {
            FolderUserID = "AAAABBBBCCCCDDDD",
            FileUserID = "1111222233334444",
            UseFolderUserIDAsEffectiveUserID = false
        };

        Assert.True(BackupRestoreControl.MatchesCurrentFileUserID(
            folderBasedBackup,
            "aaaabbbbccccdddd"));
        Assert.True(BackupRestoreControl.MatchesCurrentFileUserID(
            fileBasedBackup,
            "1111222233334444"));
        Assert.False(BackupRestoreControl.MatchesCurrentFileUserID(
            folderBasedBackup,
            "1111222233334444"));
        Assert.False(BackupRestoreControl.MatchesCurrentFileUserID(
            folderBasedBackup,
            string.Empty));
    }

    private static BackupMetadata CreateBackupMetadata()
    {
        return new BackupMetadata
        {
            BackupTime = new DateTime(2026, 7, 9, 12, 34, 56),
            CharacterDisplayName = "测试角色",
            FolderUserID = "FFXIV_CHR1234",
            FileUserID = "FFXIV_CHR1234",
            BackupFilePath = "C:\\tool-data\\backups\\backup\\UISAVE.DAT"
        };
    }
}
