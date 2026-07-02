using System;
using System.IO;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class DataDirectoryMigrationTargetDirectoryValidatorTests : IDisposable
{
    private readonly string testRoot;
    private readonly string currentDirectory;

    public DataDirectoryMigrationTargetDirectoryValidatorTests()
    {
        testRoot = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.MigrationTargetValidatorTests",
            Guid.NewGuid().ToString("N"));
        currentDirectory = Path.Combine(testRoot, "current");
        Directory.CreateDirectory(currentDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void TryValidateTargetDirectory_AllowsExistingEmptyDirectory()
    {
        string targetDirectory = Path.Combine(testRoot, "empty-target");
        Directory.CreateDirectory(targetDirectory);

        bool isValid = DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
            currentDirectory,
            targetDirectory,
            out string targetFullPath,
            out string errorMessage);

        Assert.True(isValid);
        Assert.Equal(Normalize(targetDirectory), targetFullPath);
        Assert.Equal(string.Empty, errorMessage);
    }

    [Fact]
    public void TryValidateTargetDirectory_AllowsMissingDirectory()
    {
        string targetDirectory = Path.Combine(testRoot, "missing-target");

        bool isValid = DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
            currentDirectory,
            targetDirectory,
            out string targetFullPath,
            out string errorMessage);

        Assert.True(isValid);
        Assert.Equal(Normalize(targetDirectory), targetFullPath);
        Assert.Equal(string.Empty, errorMessage);
    }

    [Fact]
    public void TryValidateTargetDirectory_RejectsNonEmptyDirectory()
    {
        string targetDirectory = Path.Combine(testRoot, "non-empty-target");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "existing.txt"), "data");

        bool isValid = DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
            currentDirectory,
            targetDirectory,
            out _,
            out string errorMessage);

        Assert.False(isValid);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void TryValidateTargetDirectory_RejectsRootDirectory()
    {
        string rootDirectory = Path.GetPathRoot(Path.GetTempPath())!;

        bool isValid = DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
            currentDirectory,
            rootDirectory,
            out _,
            out string errorMessage);

        Assert.False(isValid);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void TryValidateTargetDirectory_RejectsTargetInsideCurrentDirectory()
    {
        string targetDirectory = Path.Combine(currentDirectory, "child-target");

        bool isValid = DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
            currentDirectory,
            targetDirectory,
            out _,
            out string errorMessage);

        Assert.False(isValid);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void TryValidateTargetDirectory_RejectsTargetContainingCurrentDirectory()
    {
        bool isValid = DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
            currentDirectory,
            testRoot,
            out _,
            out string errorMessage);

        Assert.False(isValid);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void TryValidateTargetDirectory_RejectsCurrentDirectory()
    {
        bool isValid = DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
            currentDirectory,
            currentDirectory,
            out _,
            out string errorMessage);

        Assert.False(isValid);
        Assert.NotEmpty(errorMessage);
    }

    private static string Normalize(string directory)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }
}
