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
    public void AutoDetectGameCharacterRootDirectory_UsesDetectedCharacterRootDirectory()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(Path.Combine(gameConfigRoot, "FFXIV_CHR0123456789ABCDEF"));

        string? directory = WayMarkOpenDirectoryResolver.AutoDetectGameCharacterRootDirectory([gameConfigRoot]);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void AutoDetectGameCharacterRootDirectory_DoesNotChooseASpecificCharacterDirectory()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        string oldCharacterDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHR0000000000000001");
        string newCharacterDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHR0000000000000002");
        Directory.CreateDirectory(oldCharacterDirectory);
        Directory.CreateDirectory(newCharacterDirectory);

        string? directory = WayMarkOpenDirectoryResolver.AutoDetectGameCharacterRootDirectory([gameConfigRoot]);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
        Assert.NotEqual(Path.GetFullPath(oldCharacterDirectory), directory);
        Assert.NotEqual(Path.GetFullPath(newCharacterDirectory), directory);
    }

    [Fact]
    public void Resolve_GameCharacterMode_UsesSavedDirectory()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(gameConfigRoot);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            gameConfigRoot,
            []);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void Resolve_GameCharacterMode_FallsBackToLastOpenedDirectoryWhenSavedDirectoryMissing()
    {
        string lastOpenedDirectory = Path.Combine(testDirectory, "ManualPick");
        Directory.CreateDirectory(lastOpenedDirectory);
        string recentFile = Path.Combine(lastOpenedDirectory, "UISAVE.DAT");
        File.WriteAllText(recentFile, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            string.Empty,
            [recentFile]);

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
        Directory.CreateDirectory(gameConfigRoot);
        string lastOpenedDirectory = Path.Combine(testDirectory, "LastOpened");
        Directory.CreateDirectory(lastOpenedDirectory);
        string recentFile = Path.Combine(lastOpenedDirectory, "UISAVE.DAT");
        File.WriteAllText(recentFile, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.LastOpenedPath,
            gameConfigRoot,
            [recentFile]);

        Assert.Equal(Path.GetFullPath(lastOpenedDirectory), directory);
    }
}
