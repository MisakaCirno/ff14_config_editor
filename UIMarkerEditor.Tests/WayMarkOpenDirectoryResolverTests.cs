using System;
using System.IO;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class WayMarkOpenDirectoryResolverTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "UIMarkerEditor.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_GameCharacterMode_UsesDetectedCharacterDirectory()
    {
        string gameRoot = Path.Combine(testDirectory, "FINAL FANTASY XIV - A Realm Reborn");
        string characterDirectory = Path.Combine(gameRoot, "FFXIV_CHR0123456789ABCDEF");
        Directory.CreateDirectory(characterDirectory);
        File.WriteAllText(Path.Combine(characterDirectory, "UISAVE.DAT"), string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            [],
            [gameRoot]);

        Assert.Equal(Path.GetFullPath(characterDirectory), directory);
    }

    [Fact]
    public void Resolve_GameCharacterMode_UsesNewestDetectedCharacterDirectory()
    {
        string gameRoot = Path.Combine(testDirectory, "FINAL FANTASY XIV - A Realm Reborn");
        string oldCharacterDirectory = Path.Combine(gameRoot, "FFXIV_CHR0000000000000001");
        string newCharacterDirectory = Path.Combine(gameRoot, "FFXIV_CHR0000000000000002");
        Directory.CreateDirectory(oldCharacterDirectory);
        Directory.CreateDirectory(newCharacterDirectory);
        string oldFilePath = Path.Combine(oldCharacterDirectory, "UISAVE.DAT");
        string newFilePath = Path.Combine(newCharacterDirectory, "UISAVE.DAT");
        File.WriteAllText(oldFilePath, string.Empty);
        File.WriteAllText(newFilePath, string.Empty);
        File.SetLastWriteTimeUtc(oldFilePath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newFilePath, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            [],
            [gameRoot]);

        Assert.Equal(Path.GetFullPath(newCharacterDirectory), directory);
    }

    [Fact]
    public void Resolve_GameCharacterMode_CanInferDirectoryFromRecentWayMarkFile()
    {
        string gameRoot = Path.Combine(testDirectory, "GameConfig");
        string characterDirectory = Path.Combine(gameRoot, "FFXIV_CHR0123456789ABCDEF");
        Directory.CreateDirectory(characterDirectory);
        string recentFile = Path.Combine(characterDirectory, "UISAVE.DAT");
        File.WriteAllText(recentFile, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            [recentFile],
            []);

        Assert.Equal(Path.GetFullPath(characterDirectory), directory);
    }

    [Fact]
    public void Resolve_GameCharacterMode_FallsBackToLastOpenedDirectory()
    {
        string lastOpenedDirectory = Path.Combine(testDirectory, "ManualPick");
        Directory.CreateDirectory(lastOpenedDirectory);
        string recentFile = Path.Combine(lastOpenedDirectory, "UISAVE.DAT");
        File.WriteAllText(recentFile, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            [recentFile],
            []);

        Assert.Equal(Path.GetFullPath(lastOpenedDirectory), directory);
    }

    [Fact]
    public void Resolve_LastOpenedMode_PrefersRecentFileDirectory()
    {
        string gameRoot = Path.Combine(testDirectory, "FINAL FANTASY XIV - A Realm Reborn");
        string characterDirectory = Path.Combine(gameRoot, "FFXIV_CHR0123456789ABCDEF");
        Directory.CreateDirectory(characterDirectory);
        File.WriteAllText(Path.Combine(characterDirectory, "UISAVE.DAT"), string.Empty);
        string lastOpenedDirectory = Path.Combine(testDirectory, "LastOpened");
        Directory.CreateDirectory(lastOpenedDirectory);
        string recentFile = Path.Combine(lastOpenedDirectory, "UISAVE.DAT");
        File.WriteAllText(recentFile, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.LastOpenedPath,
            [recentFile],
            [gameRoot]);

        Assert.Equal(Path.GetFullPath(lastOpenedDirectory), directory);
    }
}