namespace UIMarkerEditor.Tests;

public sealed class StartupRecentFileSelectorTests
{
    [Fact]
    public void SelectFirstExisting_WhenRecentFilesEmpty_ReturnsNoRecentFiles()
    {
        StartupRecentFileSelection selection =
            StartupRecentFileSelector.SelectFirstExisting([], _ => true);

        Assert.False(selection.HasRecentFiles);
        Assert.False(selection.HasExistingFile);
        Assert.False(selection.SkippedMissingFiles);
        Assert.Equal(string.Empty, selection.FilePath);
    }

    [Fact]
    public void SelectFirstExisting_WhenFirstFileExists_SelectsFirstFile()
    {
        StartupRecentFileSelection selection =
            StartupRecentFileSelector.SelectFirstExisting(
                ["first.dat", "second.dat"],
                filePath => filePath == "first.dat");

        Assert.True(selection.HasRecentFiles);
        Assert.True(selection.HasExistingFile);
        Assert.False(selection.SkippedMissingFiles);
        Assert.Equal("first.dat", selection.FilePath);
    }

    [Fact]
    public void SelectFirstExisting_WhenEarlierFilesAreMissing_SelectsNextExistingFile()
    {
        StartupRecentFileSelection selection =
            StartupRecentFileSelector.SelectFirstExisting(
                ["missing-a.dat", "missing-b.dat", "existing.dat"],
                filePath => filePath == "existing.dat");

        Assert.True(selection.HasRecentFiles);
        Assert.True(selection.HasExistingFile);
        Assert.True(selection.SkippedMissingFiles);
        Assert.Equal("existing.dat", selection.FilePath);
    }

    [Fact]
    public void SelectFirstExisting_WhenAllRecentFilesAreMissing_ReturnsNoExistingFile()
    {
        StartupRecentFileSelection selection =
            StartupRecentFileSelector.SelectFirstExisting(
                ["missing-a.dat", "missing-b.dat"],
                _ => false);

        Assert.True(selection.HasRecentFiles);
        Assert.False(selection.HasExistingFile);
        Assert.True(selection.SkippedMissingFiles);
        Assert.Equal(string.Empty, selection.FilePath);
    }
}
