using System;
using System.IO;
using System.Text;
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
    public void AutoDetectGameCharacterRootDirectory_UsesExistingRegistryCandidate()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(gameConfigRoot);

        string? directory = WayMarkOpenDirectoryResolver.AutoDetectGameCharacterRootDirectory([gameConfigRoot]);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void AutoDetectGameCharacterRootDirectory_SkipsEmptyCandidates()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(gameConfigRoot);

        string? directory = WayMarkOpenDirectoryResolver.AutoDetectGameCharacterRootDirectory([string.Empty, "  ", gameConfigRoot]);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void AutoDetectGameCharacterRootDirectory_SkipsMissingCandidates()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");

        string? directory = WayMarkOpenDirectoryResolver.AutoDetectGameCharacterRootDirectory([gameConfigRoot]);

        Assert.Null(directory);
    }

    [Fact]
    public void AutoDetectGameCharacterRootDirectory_RepairsUtf8DecodedAsGbkCandidate()
    {
        string gameConfigRoot = Path.Combine(
            testDirectory,
            "Software",
            "\u6700\u7EC8\u5E7B\u60F3XIV",
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(gameConfigRoot);
        string garbledRoot = CreateUtf8DecodedAsGbk(gameConfigRoot);

        string? directory = WayMarkOpenDirectoryResolver.AutoDetectGameCharacterRootDirectory([garbledRoot]);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void Resolve_CustomDirectoryMode_UsesSavedDirectory()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(gameConfigRoot);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.CustomDirectory,
            gameConfigRoot);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void Resolve_CustomDirectoryMode_ReturnsNullWhenSavedDirectoryMissing()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.CustomDirectory,
            gameConfigRoot);

        Assert.Null(directory);
    }

    [Fact]
    public void Resolve_DefaultMode_ReturnsNullSoDialogUsesPersistedState()
    {
        string gameConfigRoot = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(gameConfigRoot);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.Default,
            gameConfigRoot);

        Assert.Null(directory);
    }

    [Fact]
    public void CreateCandidateRootsFromInstallPath_PrefersGameMyGamesDirectory()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string expectedRoot = Path.Combine(installDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");

        string[] candidateRoots = WayMarkOpenDirectoryResolver
            .CreateCandidateRootsFromInstallPath(installDirectory)
            .ToArray();

        Assert.Equal(expectedRoot, candidateRoots[0]);
        Assert.Contains(Path.Combine(installDirectory, "My Games", "FINAL FANTASY XIV - A Realm Reborn"), candidateRoots);
    }

    [Fact]
    public void CreateCandidateRootsFromInstallPath_WhenInstallPathIsGameDirectory_UsesItDirectly()
    {
        string gameDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV", "game");
        string expectedRoot = Path.Combine(gameDirectory, "My Games", "FINAL FANTASY XIV - A Realm Reborn");

        string[] candidateRoots = WayMarkOpenDirectoryResolver
            .CreateCandidateRootsFromInstallPath(gameDirectory)
            .ToArray();

        Assert.Equal(new[] { expectedRoot }, candidateRoots);
    }

    private static string CreateUtf8DecodedAsGbk(string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936).GetString(Encoding.UTF8.GetBytes(value));
    }
}
