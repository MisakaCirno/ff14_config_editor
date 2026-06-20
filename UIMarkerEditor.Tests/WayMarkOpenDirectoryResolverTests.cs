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
    public void Resolve_GameCharacterMode_UsesDetectedCharacterRootDirectory()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(Path.Combine(gameConfigRoot, "FFXIV_CHR0123456789ABCDEF"));

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            [],
            [gameConfigRoot]);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void Resolve_GameCharacterMode_DoesNotChooseASpecificCharacterDirectory()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        string oldCharacterDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHR0000000000000001");
        string newCharacterDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHR0000000000000002");
        Directory.CreateDirectory(oldCharacterDirectory);
        Directory.CreateDirectory(newCharacterDirectory);
        File.WriteAllText(Path.Combine(oldCharacterDirectory, "UISAVE.DAT"), string.Empty);
        File.WriteAllText(Path.Combine(newCharacterDirectory, "UISAVE.DAT"), string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            [],
            [gameConfigRoot]);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
        Assert.NotEqual(Path.GetFullPath(oldCharacterDirectory), directory);
        Assert.NotEqual(Path.GetFullPath(newCharacterDirectory), directory);
    }

    [Fact]
    public void Resolve_GameCharacterMode_CanInferRootDirectoryFromRecentWayMarkFile()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        string characterDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHR0123456789ABCDEF");
        Directory.CreateDirectory(characterDirectory);
        string recentFile = Path.Combine(characterDirectory, "UISAVE.DAT");
        File.WriteAllText(recentFile, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            [recentFile],
            []);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
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
    public void CreateCandidateRootsFromInstallPath_IncludesGameMyGamesDirectory()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string expectedRoot = Path.Combine(installDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");

        string[] candidateRoots = WayMarkOpenDirectoryResolver
            .CreateCandidateRootsFromInstallPath(installDirectory)
            .ToArray();

        Assert.Contains(expectedRoot, candidateRoots);
    }

    [Fact]
    public void Resolve_LastOpenedMode_PrefersRecentFileDirectory()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        string characterDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHR0123456789ABCDEF");
        Directory.CreateDirectory(characterDirectory);
        File.WriteAllText(Path.Combine(characterDirectory, "UISAVE.DAT"), string.Empty);
        string lastOpenedDirectory = Path.Combine(testDirectory, "LastOpened");
        Directory.CreateDirectory(lastOpenedDirectory);
        string recentFile = Path.Combine(lastOpenedDirectory, "UISAVE.DAT");
        File.WriteAllText(recentFile, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.LastOpenedPath,
            [recentFile],
            [gameConfigRoot]);

        Assert.Equal(Path.GetFullPath(lastOpenedDirectory), directory);
    }
}
