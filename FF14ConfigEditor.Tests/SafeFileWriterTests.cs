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
        Assert.Empty(Directory.EnumerateFiles(testDirectory, "*.tmp", SearchOption.AllDirectories));
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
    }
}
