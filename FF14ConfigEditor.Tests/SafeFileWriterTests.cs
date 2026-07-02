using System.Text;
using FF14ConfigEditor;

namespace FF14ConfigEditor.Tests;

public sealed class SafeFileWriterTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "FF14ConfigEditor.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllText_CreatesParentDirectoryAndWritesUtf8WithoutBom()
    {
        string targetPath = Path.Combine(testDirectory, "nested", "config.json");
        string contents = "{\"name\":\"配置\"}";

        SafeFileWriter.WriteAllText(targetPath, contents);

        Assert.True(File.Exists(targetPath));
        Assert.Equal(Encoding.UTF8.GetBytes(contents), File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void WriteAllBytes_ReplacesExistingFile()
    {
        string targetPath = Path.Combine(testDirectory, "UISAVE.DAT");
        byte[] oldContents = [0x01, 0x02, 0x03];
        byte[] newContents = [0xFE, 0xDC, 0xBA, 0x98];

        SafeFileWriter.WriteAllBytes(targetPath, oldContents);
        SafeFileWriter.WriteAllBytes(targetPath, newContents);

        Assert.Equal(newContents, File.ReadAllBytes(targetPath));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public void WriteAllBytes_WhenTargetIsLocked_ThrowsAndLeavesTargetUnchanged()
    {
        string targetPath = Path.Combine(testDirectory, "UISAVE.DAT");
        byte[] oldContents = [0x01, 0x02, 0x03];
        byte[] newContents = [0xFE, 0xDC, 0xBA, 0x98];

        Directory.CreateDirectory(testDirectory);
        File.WriteAllBytes(targetPath, oldContents);

        using (new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            AssertFileAccessFailure(() => SafeFileWriter.WriteAllBytes(targetPath, newContents));
        }

        Assert.Equal(oldContents, File.ReadAllBytes(targetPath));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public void Copy_ReplacesTargetWithSourceContents()
    {
        string sourcePath = Path.Combine(testDirectory, "source.dat");
        string targetPath = Path.Combine(testDirectory, "target", "UISAVE.DAT");
        byte[] sourceContents = [0x10, 0x20, 0x30];

        Directory.CreateDirectory(testDirectory);
        File.WriteAllBytes(sourcePath, sourceContents);
        SafeFileWriter.WriteAllBytes(targetPath, [0xAA]);

        SafeFileWriter.Copy(sourcePath, targetPath);

        Assert.Equal(sourceContents, File.ReadAllBytes(targetPath));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public void Copy_WhenTargetIsLocked_ThrowsAndLeavesTargetUnchanged()
    {
        string sourcePath = Path.Combine(testDirectory, "source.dat");
        string targetPath = Path.Combine(testDirectory, "target", "UISAVE.DAT");
        byte[] sourceContents = [0x10, 0x20, 0x30];
        byte[] oldContents = [0xAA, 0xBB, 0xCC];

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(sourcePath, sourceContents);
        File.WriteAllBytes(targetPath, oldContents);

        using (new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            AssertFileAccessFailure(() => SafeFileWriter.Copy(sourcePath, targetPath));
        }

        Assert.Equal(oldContents, File.ReadAllBytes(targetPath));
        AssertNoTemporaryFiles();
    }

    [Fact]
    public void Copy_WhenSourceDoesNotExist_ThrowsAndLeavesTargetUnchanged()
    {
        string sourcePath = Path.Combine(testDirectory, "missing.dat");
        string targetPath = Path.Combine(testDirectory, "target", "UISAVE.DAT");
        byte[] oldContents = [0xAA, 0xBB, 0xCC];

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllBytes(targetPath, oldContents);

        Assert.Throws<FileNotFoundException>(() => SafeFileWriter.Copy(sourcePath, targetPath));

        Assert.Equal(oldContents, File.ReadAllBytes(targetPath));
        AssertNoTemporaryFiles();
    }

    private void AssertNoTemporaryFiles()
    {
        if (!Directory.Exists(testDirectory))
        {
            return;
        }

        Assert.Empty(Directory.EnumerateFiles(testDirectory, "*.tmp", SearchOption.AllDirectories));
    }

    private static void AssertFileAccessFailure(Action action)
    {
        Exception exception = Assert.ThrowsAny<Exception>(action);
        Assert.True(
            exception is IOException or UnauthorizedAccessException,
            $"Expected an IO or access failure, but got {exception.GetType().FullName}: {exception.Message}");
    }
}
