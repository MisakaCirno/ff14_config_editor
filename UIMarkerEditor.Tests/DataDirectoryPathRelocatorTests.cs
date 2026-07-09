using System.IO;

namespace UIMarkerEditor.Tests;

public sealed class DataDirectoryPathRelocatorTests
{
    [Fact]
    public void TryRelocatePath_WhenFileIsUnderSourceDirectory_ReturnsTargetPath()
    {
        string sourceDirectory = Path.Combine("C:\\", "OldData");
        string targetDirectory = Path.Combine("D:\\", "NewData");
        string filePath = Path.Combine(sourceDirectory, "backups", "backup-1", "UISAVE.DAT");

        bool relocated = DataDirectoryPathRelocator.TryRelocatePath(
            filePath,
            sourceDirectory,
            targetDirectory,
            out string relocatedPath);

        Assert.True(relocated);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(targetDirectory, "backups", "backup-1", "UISAVE.DAT")),
            relocatedPath);
    }

    [Fact]
    public void TryRelocatePath_WhenFileIsOutsideSourceDirectory_ReturnsFalse()
    {
        bool relocated = DataDirectoryPathRelocator.TryRelocatePath(
            Path.Combine("C:\\", "OtherData", "UISAVE.DAT"),
            Path.Combine("C:\\", "OldData"),
            Path.Combine("D:\\", "NewData"),
            out string relocatedPath);

        Assert.False(relocated);
        Assert.Equal(string.Empty, relocatedPath);
    }

    [Fact]
    public void TryRelocatePath_WhenFileIsUnderSiblingDirectory_ReturnsFalse()
    {
        bool relocated = DataDirectoryPathRelocator.TryRelocatePath(
            Path.Combine("C:\\", "OldDataSibling", "UISAVE.DAT"),
            Path.Combine("C:\\", "OldData"),
            Path.Combine("D:\\", "NewData"),
            out string relocatedPath);

        Assert.False(relocated);
        Assert.Equal(string.Empty, relocatedPath);
    }
}
