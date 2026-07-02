using System;
using System.Diagnostics;
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
    public void AutoDetectGameInstallDirectory_UsesExistingGameExecutableCandidate()
    {
        string gameInstallDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);

        string? detectedGameInstallDirectory = WayMarkOpenDirectoryResolver.AutoDetectGameInstallDirectory([gameExecutablePath]);

        Assert.Equal(Path.GetFullPath(gameInstallDirectory), detectedGameInstallDirectory);
    }

    [Fact]
    public void AutoDetectGameInstallDirectory_SkipsEmptyAndMissingCandidates()
    {
        string gameInstallDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameDirectory = Path.Combine(gameInstallDirectory, "game");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "ffxiv_dx11.exe"), string.Empty);

        string? detectedGameInstallDirectory = WayMarkOpenDirectoryResolver.AutoDetectGameInstallDirectory(
            [string.Empty, "  ", Path.Combine(testDirectory, "missing", "ffxiv_dx11.exe"), gameDirectory]);

        Assert.Equal(Path.GetFullPath(gameInstallDirectory), detectedGameInstallDirectory);
    }

    [Fact]
    public void AutoDetectGameInstallDirectory_ReturnsNullWhenCandidatesMissing()
    {
        string gameExecutablePath = Path.Combine(testDirectory, "game", "ffxiv_dx11.exe");

        string? detectedGameInstallDirectory = WayMarkOpenDirectoryResolver.AutoDetectGameInstallDirectory([gameExecutablePath]);

        Assert.Null(detectedGameInstallDirectory);
    }

    [Fact]
    public void AutoDetectGameInstallDirectory_RepairsUtf8DecodedAsGbkCandidate()
    {
        string gameInstallDirectory = Path.Combine(
            testDirectory,
            "Software",
            "\u6700\u7EC8\u5E7B\u60F3XIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        string garbledGameExecutablePath = CreateUtf8DecodedAsGbk(gameExecutablePath);

        string? detectedGameInstallDirectory = WayMarkOpenDirectoryResolver.AutoDetectGameInstallDirectory([garbledGameExecutablePath]);

        Assert.Equal(Path.GetFullPath(gameInstallDirectory), detectedGameInstallDirectory);
    }

    [Fact]
    public void TryGetProcessExecutablePath_ReturnsRunningProcessPath()
    {
        using Process process = Process.GetCurrentProcess();

        bool resolved = WayMarkOpenDirectoryResolver.TryGetProcessExecutablePath(
            process,
            out string? executablePath);

        Assert.True(resolved);
        Assert.True(File.Exists(executablePath));
    }

    [Fact]
    public void Resolve_GameCharacterDirectoryMode_UsesGameInstallDirectory()
    {
        string gameInstallDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        string gameConfigRoot = Path.Combine(gameInstallDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        Directory.CreateDirectory(gameConfigRoot);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            Path.Combine(testDirectory, "custom"),
            gameInstallDirectory);

        Assert.Equal(Path.GetFullPath(gameConfigRoot), directory);
    }

    [Fact]
    public void Resolve_GameCharacterDirectoryMode_ReturnsNullWhenGameCharacterRootMissing()
    {
        string gameInstallDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);

        string? directory = WayMarkOpenDirectoryResolver.Resolve(
            WayMarkOpenDirectoryMode.GameCharacterDirectory,
            Path.Combine(testDirectory, "custom"),
            gameInstallDirectory);

        Assert.Null(directory);
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
    public void TryNormalizeGameInstallDirectory_WhenGivenGameExecutable_ReturnsInstallRoot()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameExecutablePath = Path.Combine(installDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);

        bool normalized = WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            gameExecutablePath,
            out string? normalizedInstallDirectory);

        Assert.True(normalized);
        Assert.Equal(Path.GetFullPath(installDirectory), normalizedInstallDirectory);
    }

    [Fact]
    public void TryNormalizeGameInstallDirectory_WhenGivenGameDirectory_ReturnsInstallRoot()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameDirectory = Path.Combine(installDirectory, "game");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "ffxiv_dx11.exe"), string.Empty);

        bool normalized = WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            gameDirectory,
            out string? normalizedInstallDirectory);

        Assert.True(normalized);
        Assert.Equal(Path.GetFullPath(installDirectory), normalizedInstallDirectory);
    }

    [Fact]
    public void TryInferGameInstallDirectoryFromSaveFile_WhenFileIsUnderGameCharacterDirectory_ReturnsInstallRoot()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameExecutablePath = Path.Combine(installDirectory, "game", "ffxiv_dx11.exe");
        string saveFilePath = Path.Combine(
            installDirectory,
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn",
            "FFXIV_CHR0011223344556677",
            "UISAVE.DAT");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        File.WriteAllText(saveFilePath, string.Empty);

        bool inferred = WayMarkOpenDirectoryResolver.TryInferGameInstallDirectoryFromSaveFile(
            saveFilePath,
            out string? inferredInstallDirectory);

        Assert.True(inferred);
        Assert.Equal(Path.GetFullPath(installDirectory), inferredInstallDirectory);
    }

    [Fact]
    public void TryInferGameInstallDirectoryFromSaveFile_WhenCharacterDirectoryHasSuffix_ReturnsFalse()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "鏈€缁堝够鎯砐IV");
        string gameExecutablePath = Path.Combine(installDirectory, "game", "ffxiv_dx11.exe");
        string saveFilePath = Path.Combine(
            installDirectory,
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn",
            "FFXIV_CHR0011223344556677_Manual",
            "UISAVE.DAT");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        File.WriteAllText(saveFilePath, string.Empty);

        bool inferred = WayMarkOpenDirectoryResolver.TryInferGameInstallDirectoryFromSaveFile(
            saveFilePath,
            out string? inferredInstallDirectory);

        Assert.False(inferred);
        Assert.Null(inferredInstallDirectory);
    }

    [Fact]
    public void TryInferGameInstallDirectoryFromSaveFile_WhenFileIsNotUnderGameCharacterDirectory_ReturnsFalse()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameExecutablePath = Path.Combine(installDirectory, "game", "ffxiv_dx11.exe");
        string saveFilePath = Path.Combine(testDirectory, "ManualFiles", "UISAVE.DAT");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        File.WriteAllText(saveFilePath, string.Empty);

        bool inferred = WayMarkOpenDirectoryResolver.TryInferGameInstallDirectoryFromSaveFile(
            saveFilePath,
            out string? inferredInstallDirectory);

        Assert.False(inferred);
        Assert.Null(inferredInstallDirectory);
    }

    [Fact]
    public void CreateCandidateGameInstallDirectoriesFromInstallPath_WhenInstallPathIsRoot_ReturnsRoot()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");

        string[] candidateGameInstallDirectories = WayMarkOpenDirectoryResolver
            .CreateCandidateGameInstallDirectoriesFromInstallPath(installDirectory)
            .ToArray();

        Assert.Equal(new[] { installDirectory }, candidateGameInstallDirectories);
    }

    [Fact]
    public void CreateCandidateGameInstallDirectoriesFromInstallPath_WhenInstallPathIsGameExe_UsesInstallRootFirst()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameDirectory = Path.Combine(installDirectory, "game");
        string gameExePath = Path.Combine(gameDirectory, "ffxiv_dx11.exe");

        string[] candidateGameInstallDirectories = WayMarkOpenDirectoryResolver
            .CreateCandidateGameInstallDirectoriesFromInstallPath(gameExePath)
            .ToArray();

        Assert.Equal(installDirectory, candidateGameInstallDirectories[0]);
        Assert.Equal(gameDirectory, candidateGameInstallDirectories[1]);
    }

    [Fact]
    public void CreateCandidateGameInstallDirectoriesFromInstallPath_WhenInstallPathIsBootDirectory_UsesInstallRootFirst()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string bootDirectory = Path.Combine(installDirectory, "boot");

        string[] candidateGameInstallDirectories = WayMarkOpenDirectoryResolver
            .CreateCandidateGameInstallDirectoriesFromInstallPath(bootDirectory)
            .ToArray();

        Assert.Equal(installDirectory, candidateGameInstallDirectories[0]);
        Assert.Equal(bootDirectory, candidateGameInstallDirectories[1]);
    }

    [Fact]
    public void TryResolveGameExecutablePath_ReturnsExistingExecutableFromInstallRoot()
    {
        string installDirectory = Path.Combine(testDirectory, "Software", "最终幻想XIV");
        string gameExecutablePath = Path.Combine(installDirectory, "game", "ffxiv.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);

        bool resolved = WayMarkOpenDirectoryResolver.TryResolveGameExecutablePath(
            installDirectory,
            out string? resolvedGameExecutablePath);

        Assert.True(resolved);
        Assert.Equal(Path.GetFullPath(gameExecutablePath), resolvedGameExecutablePath);
    }

    private static string CreateUtf8DecodedAsGbk(string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936).GetString(Encoding.UTF8.GetBytes(value));
    }
}
