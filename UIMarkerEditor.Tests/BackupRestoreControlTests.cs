using System.IO;
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
}
