using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class AppDataStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "UIMarkerEditor.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        AppLogger.SetLogFilePath(null);

        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }

        MapData.Clear();
    }

    [Fact]
    public void Initialize_CreatesLocalDataFilesInTemporaryDirectory()
    {
        AppDataStore store = CreateStore();

        store.Initialize();

        Assert.Equal(Path.Combine(testDirectory, "Data"), store.DataDirectory);
        Assert.True(File.Exists(store.BootstrapFilePath));
        Assert.True(Directory.Exists(store.ConfigsDirectory));
        Assert.True(Directory.Exists(store.CacheDirectory));
        Assert.True(Directory.Exists(store.LogDirectory));
        Assert.True(File.Exists(store.SettingsFilePath));
        Assert.True(File.Exists(store.CharactersFilePath));
        Assert.True(Directory.Exists(store.BackupsDirectory));
        Assert.Equal(Path.Combine(testDirectory, "Data", "configs", "config.json"), store.SettingsFilePath);
        Assert.Equal(Path.Combine(testDirectory, "Data", "cache", "servers.json"), store.ServersFilePath);
        Assert.True(store.Settings.LimitBackupCountPerUser);
        Assert.Equal(AppSettings.DefaultMaxBackupCountPerUser, store.Settings.MaxBackupCountPerUser);
        Assert.False(store.Settings.AutoBackupAfterLoad);
        Assert.True(store.Settings.AutoBackupBeforeRestore);
        Assert.Equal(MapDataTableMode.Automatic, store.Settings.MapDataTableMode);
        Assert.False(store.Settings.MapDataTableModeInitialized);
        Assert.Equal(MapDataSource.OnlineReference, store.Settings.MapDataSource);
        Assert.False(store.Settings.MapDataSourceInitialized);
        Assert.Equal(MapDataOnlineSourceKind.ContentFinderConditionCsv, store.Settings.MapDataOnlineSource);
        Assert.Equal(UnknownMapIdPolicy.RejectUnknown, store.Settings.UnknownMapIdPolicy);
        Assert.True(store.Settings.ShowAllowUnknownMapIdPolicyWarning);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.False(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(string.Empty, store.Settings.GameInstallDirectory);
        Assert.Equal(string.Empty, store.Settings.WayMarkCustomDirectory);
    }

    [Fact]
    public void Initialize_WhenSettingsJsonInvalid_DoesNotOverwriteCorruptedFileAndBlocksSave()
    {
        string settingsPath = Path.Combine(testDirectory, "Data", "configs", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{ 损坏的 JSON");
        AppDataStore store = CreateStore();

        store.Initialize();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.SaveSettings(new AppSettings { MaxBackupCount = 7 }));
        Assert.Contains("config.json 本次启动读取失败", exception.Message);
        Assert.Equal("{ 损坏的 JSON", File.ReadAllText(settingsPath));
        Assert.Contains(store.ConsumeDataLoadWarnings(), warning => warning.Contains("工具设置无法读取"));
    }

    [Fact]
    public void Initialize_WhenCharactersJsonInvalid_DoesNotOverwriteCorruptedFileAndBlocksSave()
    {
        string charactersPath = Path.Combine(testDirectory, "Data", "configs", "characters.json");
        Directory.CreateDirectory(Path.GetDirectoryName(charactersPath)!);
        File.WriteAllText(charactersPath, "{ 损坏的 JSON");
        AppDataStore store = CreateStore();

        store.Initialize();
        store.GetOrCreateCharacter("abc");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(store.SaveCharacters);
        Assert.Contains("characters.json 本次启动读取失败", exception.Message);
        Assert.Equal("{ 损坏的 JSON", File.ReadAllText(charactersPath));
        Assert.Contains(store.ConsumeDataLoadWarnings(), warning => warning.Contains("角色备注无法读取"));
    }

    [Fact]
    public void Initialize_WhenCharactersJsonContainsNullFields_NormalizesCharacters()
    {
        string charactersPath = Path.Combine(testDirectory, "Data", "configs", "characters.json");
        Directory.CreateDirectory(Path.GetDirectoryName(charactersPath)!);
        File.WriteAllText(charactersPath, """
[
  {
    "UserID": " abc123 ",
    "CharacterName": null,
    "DataCenter": null,
    "World": null,
    "Note": null
  },
  null,
  {
    "UserID": null,
    "CharacterName": "  测试角色  ",
    "DataCenter": "  Elemental  ",
    "World": "  Aegis  ",
    "Note": "  备注  "
  }
]
""");
        AppDataStore store = CreateStore();

        store.Initialize();

        Assert.Equal(2, store.Characters.Count);
        Assert.Equal("ABC123", store.Characters[0].UserID);
        Assert.Equal(string.Empty, store.Characters[0].CharacterName);
        Assert.Equal(string.Empty, store.Characters[0].DataCenter);
        Assert.Equal(string.Empty, store.Characters[0].World);
        Assert.Equal(string.Empty, store.Characters[0].Note);
        Assert.NotEqual(default, store.Characters[0].UpdatedAt);
        Assert.Equal("UNKNOWN", store.Characters[1].UserID);
        Assert.Equal("测试角色", store.Characters[1].CharacterName);
        Assert.Equal("Elemental", store.Characters[1].DataCenter);
        Assert.Equal("Aegis", store.Characters[1].World);
        Assert.Equal("备注", store.Characters[1].Note);
        Assert.NotEqual(default, store.Characters[1].UpdatedAt);
    }

    [Fact]
    public void Initialize_WhenBootstrapDirectoryCannotBeCreated_ThrowsAppDataStoreException()
    {
        Directory.CreateDirectory(testDirectory);
        string bootstrapDirectoryPath = Path.Combine(testDirectory, "bootstrap-file");
        File.WriteAllText(bootstrapDirectoryPath, "不是目录");
        AppDataStore store = new(bootstrapDirectoryPath);

        AppDataStoreException exception = Assert.Throws<AppDataStoreException>(store.Initialize);

        Assert.Equal("准备本地启动配置目录", exception.Operation);
        Assert.Equal(Path.GetFullPath(bootstrapDirectoryPath), exception.Path);
    }

    [Fact]
    public void Initialize_WhenConfiguredDataDirectoryCannotBePrepared_FallsBackWithWarning()
    {
        Directory.CreateDirectory(testDirectory);
        string configuredDataDirectory = Path.Combine(testDirectory, "BrokenData");
        Directory.CreateDirectory(configuredDataDirectory);
        File.WriteAllText(Path.Combine(configuredDataDirectory, "configs"), "不是目录");
        File.WriteAllText(
            Path.Combine(testDirectory, "bootstrap.json"),
            JsonSerializer.Serialize(new BootstrapSettings { DataDirectory = configuredDataDirectory }));
        AppDataStore store = CreateStore();

        store.Initialize();

        Assert.Equal(Path.Combine(testDirectory, "Data"), store.DataDirectory);
        Assert.True(File.Exists(store.SettingsFilePath));
        Assert.Equal(
            store.DataDirectory,
            JsonSerializer.Deserialize<BootstrapSettings>(File.ReadAllText(store.BootstrapFilePath))!.DataDirectory);
        Assert.Contains(store.ConsumeDataLoadWarnings(), warning =>
            warning.Contains("启动配置指向的数据目录无法使用") &&
            warning.Contains(configuredDataDirectory) &&
            warning.Contains(store.DefaultDataDirectory));
    }

    [Fact]
    public void SaveSettings_WhenDataDirectoryCannotBePrepared_ThrowsAppDataStoreException()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        Directory.Delete(store.BackupsDirectory, recursive: true);
        File.WriteAllText(store.BackupsDirectory, "不是目录");

        AppDataStoreException exception = Assert.Throws<AppDataStoreException>(() =>
            store.SaveSettings(store.Settings));

        Assert.Equal("准备本地数据目录", exception.Operation);
        Assert.Equal(store.DataDirectory, exception.Path);
    }

    [Fact]
    public void SaveSettings_WhenDataDirectoryCannotBePrepared_DoesNotReplaceCurrentSettings()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            MaxBackupCount = 37,
            RecentFiles = [Path.Combine(testDirectory, "old.dat")]
        });
        Directory.Delete(store.BackupsDirectory, recursive: true);
        File.WriteAllText(store.BackupsDirectory, "不是目录");

        Assert.Throws<AppDataStoreException>(() =>
            store.SaveSettings(new AppSettings
            {
                MaxBackupCount = 7,
                RecentFiles = [Path.Combine(testDirectory, "new.dat")]
            }));

        Assert.Equal(37, store.Settings.MaxBackupCount);
        Assert.Single(store.Settings.RecentFiles);
        Assert.EndsWith("old.dat", store.Settings.RecentFiles[0]);
    }

    [Fact]
    public void SaveSettings_WhenNumericSettingsInvalid_ThrowsAndDoesNotReplaceCurrentSettings()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            MaxBackupCount = 37,
            MaxBackupCountPerUser = 9,
            MaxBackupDays = 120,
            MaxLogFileSizeMb = 13,
            MaxLogFileCount = 4
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.SaveSettings(new AppSettings
            {
                MaxBackupCount = 37,
                MaxBackupDays = 120,
                MaxLogFileSizeMb = AppSettings.MaxLogFileSizeMbLimit + 1,
                MaxLogFileCount = 4
            }));

        Assert.Contains("日志文件大小", exception.Message);
        Assert.Equal(13, store.Settings.MaxLogFileSizeMb);
        Assert.Equal(4, store.Settings.MaxLogFileCount);
        Assert.Equal(37, store.Settings.MaxBackupCount);
        Assert.Equal(9, store.Settings.MaxBackupCountPerUser);
    }

    [Fact]
    public void SaveSettings_WhenPerUserBackupCountInvalid_ThrowsAndDoesNotReplaceCurrentSettings()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            MaxBackupCountPerUser = 9
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.SaveSettings(new AppSettings
            {
                MaxBackupCountPerUser = AppSettings.MaxBackupCountLimit + 1
            }));

        Assert.Contains("每个玩家最多保留备份数量", exception.Message);
        Assert.Equal(9, store.Settings.MaxBackupCountPerUser);
    }

    [Fact]
    public async Task AutoDetectGameInstallDirectoryAsync_WhenCharacterDirectoryExists_SelectsGameCharacterDirectory()
    {
        string gameInstallDirectory = CreateGameInstallDirectory("FinalFantasyXIV");
        string detectedGameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        int detectCount = 0;
        AppDataStore store = CreateStore(() =>
        {
            detectCount++;
            return detectedGameExecutablePath;
        });
        store.Initialize();

        GameInstallDirectoryUpdateResult result = await store.AutoDetectGameInstallDirectoryAsync();

        Assert.Equal(GameInstallDirectoryUpdateResult.Updated, result);
        Assert.Equal(1, detectCount);
        Assert.Equal(WayMarkOpenDirectoryMode.GameCharacterDirectory, store.Settings.WayMarkOpenDirectoryMode);
        Assert.True(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);
        Assert.Equal(string.Empty, store.Settings.WayMarkCustomDirectory);

        AppDataStore reloadedStore = CreateStore(() =>
        {
            detectCount++;
            return Path.Combine(testDirectory, "Other", "ffxiv_dx11.exe");
        });
        reloadedStore.Initialize();
        GameInstallDirectoryUpdateResult reloadedResult = await reloadedStore.AutoDetectGameInstallDirectoryAsync();

        Assert.Equal(GameInstallDirectoryUpdateResult.Unchanged, reloadedResult);
        Assert.Equal(1, detectCount);
        Assert.Equal(WayMarkOpenDirectoryMode.GameCharacterDirectory, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.True(reloadedStore.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), reloadedStore.Settings.GameInstallDirectory);
    }

    [Fact]
    public async Task AutoDetectGameInstallDirectoryAsync_WhenNotDetected_DoesNotPersistAttemptAndAllowsRetry()
    {
        int detectCount = 0;
        AppDataStore store = CreateStore(() =>
        {
            detectCount++;
            return null;
        });
        store.Initialize();

        GameInstallDirectoryUpdateResult result = await store.AutoDetectGameInstallDirectoryAsync();

        Assert.Equal(GameInstallDirectoryUpdateResult.NotFound, result);
        Assert.Equal(1, detectCount);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(string.Empty, store.Settings.GameInstallDirectory);

        string gameInstallDirectory = Path.Combine(testDirectory, "FinalFantasyXIV");
        string detectedGameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(detectedGameExecutablePath)!);
        File.WriteAllText(detectedGameExecutablePath, string.Empty);

        AppDataStore reloadedStore = CreateStore(() =>
        {
            detectCount++;
            return detectedGameExecutablePath;
        });
        reloadedStore.Initialize();
        GameInstallDirectoryUpdateResult reloadedResult = await reloadedStore.AutoDetectGameInstallDirectoryAsync();

        Assert.Equal(GameInstallDirectoryUpdateResult.Updated, reloadedResult);
        Assert.Equal(2, detectCount);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.True(reloadedStore.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), reloadedStore.Settings.GameInstallDirectory);
    }

    [Fact]
    public async Task AutoDetectGameInstallDirectoryAsync_WhenExistingDirectoryIsInvalid_ReplacesItWithDetectedDirectory()
    {
        string invalidGameInstallDirectory = Path.Combine(testDirectory, "MissingGame");
        string settingsPath = Path.Combine(testDirectory, "Data", "configs", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(
            settingsPath,
            JsonSerializer.Serialize(new AppSettings
            {
                GameInstallDirectory = invalidGameInstallDirectory
            }));

        string detectedGameInstallDirectory = Path.Combine(testDirectory, "DetectedGame");
        string detectedGameExecutablePath = Path.Combine(detectedGameInstallDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(detectedGameExecutablePath)!);
        File.WriteAllText(detectedGameExecutablePath, string.Empty);
        AppDataStore store = CreateStore(() => detectedGameExecutablePath);
        store.Initialize();

        GameInstallDirectoryUpdateResult result = await store.AutoDetectGameInstallDirectoryAsync();

        Assert.Equal(GameInstallDirectoryUpdateResult.Relocated, result);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.True(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(detectedGameInstallDirectory), store.Settings.GameInstallDirectory);

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();
        Assert.Equal(Path.GetFullPath(detectedGameInstallDirectory), reloadedStore.Settings.GameInstallDirectory);
    }

    [Fact]
    public async Task AutoDetectGameInstallDirectoryAsync_DoesNotOverwriteExistingGameInstallDirectoryOrMode()
    {
        string manualGameInstallDirectory = Path.Combine(testDirectory, "Manual");
        string manualGameExecutablePath = Path.Combine(manualGameInstallDirectory, "game", "ffxiv_dx11.exe");
        string customDirectory = Path.Combine(testDirectory, "Manual", "OpenHere");
        Directory.CreateDirectory(Path.GetDirectoryName(manualGameExecutablePath)!);
        File.WriteAllText(manualGameExecutablePath, string.Empty);
        Directory.CreateDirectory(customDirectory);
        int detectCount = 0;
        AppDataStore store = CreateStore(() =>
        {
            detectCount++;
            return Path.Combine(testDirectory, "Game", "ffxiv_dx11.exe");
        });
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.CustomDirectory,
            GameInstallDirectory = manualGameInstallDirectory,
            WayMarkCustomDirectory = customDirectory
        });

        GameInstallDirectoryUpdateResult result = await store.AutoDetectGameInstallDirectoryAsync();

        Assert.Equal(GameInstallDirectoryUpdateResult.Unchanged, result);
        Assert.Equal(0, detectCount);
        Assert.Equal(WayMarkOpenDirectoryMode.CustomDirectory, store.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(Path.GetFullPath(manualGameInstallDirectory), store.Settings.GameInstallDirectory);
        Assert.Equal(Path.GetFullPath(customDirectory), store.Settings.WayMarkCustomDirectory);
    }

    [Fact]
    public void SetGameInstallDirectoryFromDetectedPath_WhenDefaultModeIsExplicit_KeepsDefaultMode()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.Default,
            WayMarkOpenDirectoryModeInitialized = true
        });
        string gameInstallDirectory = CreateGameInstallDirectory("FinalFantasyXIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");

        GameInstallDirectoryUpdateResult result = store.SetGameInstallDirectoryFromDetectedPath(gameExecutablePath);

        Assert.Equal(GameInstallDirectoryUpdateResult.Updated, result);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.True(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);
    }

    [Fact]
    public void SetGameInstallDirectoryFromDetectedPath_WhenDirectoryIsSame_ReturnsUnchanged()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string gameInstallDirectory = Path.Combine(testDirectory, "FinalFantasyXIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);

        GameInstallDirectoryUpdateResult firstResult = store.SetGameInstallDirectoryFromDetectedPath(gameExecutablePath);
        GameInstallDirectoryUpdateResult secondResult = store.SetGameInstallDirectoryFromDetectedPath(
            Path.GetDirectoryName(gameExecutablePath));

        Assert.Equal(GameInstallDirectoryUpdateResult.Updated, firstResult);
        Assert.Equal(GameInstallDirectoryUpdateResult.Unchanged, secondResult);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);
    }

    [Fact]
    public void SetGameInstallDirectoryFromDetectedPath_WhenDirectoryIsSameAndCharacterRootAppears_DoesNotSelectGameCharacterDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string gameInstallDirectory = Path.Combine(testDirectory, "FinalFantasyXIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        string gameCharacterRootDirectory = Path.Combine(
            gameInstallDirectory,
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        GameInstallDirectoryUpdateResult firstResult = store.SetGameInstallDirectoryFromDetectedPath(gameExecutablePath);
        Assert.Equal(GameInstallDirectoryUpdateResult.Updated, firstResult);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.True(store.Settings.WayMarkOpenDirectoryModeInitialized);

        Directory.CreateDirectory(gameCharacterRootDirectory);
        GameInstallDirectoryUpdateResult result = store.SetGameInstallDirectoryFromDetectedPath(gameExecutablePath);

        Assert.Equal(GameInstallDirectoryUpdateResult.Unchanged, result);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.True(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);
    }

    [Fact]
    public void SetGameInstallDirectoryFromLoadedSaveFile_WhenSettingsEmptyAndFileIsUnderGameDirectory_PersistsInstallDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string gameInstallDirectory = Path.Combine(testDirectory, "FinalFantasyXIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        string saveFilePath = Path.Combine(
            gameInstallDirectory,
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn",
            "FFXIV_CHR0011223344556677",
            "UISAVE.DAT");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        File.WriteAllText(saveFilePath, string.Empty);

        GameInstallDirectoryUpdateResult result = store.SetGameInstallDirectoryFromLoadedSaveFile(saveFilePath);

        Assert.Equal(GameInstallDirectoryUpdateResult.Updated, result);
        Assert.Equal(WayMarkOpenDirectoryMode.GameCharacterDirectory, store.Settings.WayMarkOpenDirectoryMode);
        Assert.True(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();
        Assert.Equal(WayMarkOpenDirectoryMode.GameCharacterDirectory, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.True(reloadedStore.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), reloadedStore.Settings.GameInstallDirectory);
    }

    [Fact]
    public void SetGameInstallDirectoryFromLoadedSaveFile_WhenExistingDirectoryIsInvalid_ReplacesItWithInferredDirectory()
    {
        string invalidGameInstallDirectory = Path.Combine(testDirectory, "MissingGame");
        string settingsPath = Path.Combine(testDirectory, "Data", "configs", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(
            settingsPath,
            JsonSerializer.Serialize(new AppSettings
            {
                GameInstallDirectory = invalidGameInstallDirectory
            }));

        AppDataStore store = CreateStore();
        store.Initialize();
        string gameInstallDirectory = Path.Combine(testDirectory, "FinalFantasyXIV");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        string saveFilePath = Path.Combine(
            gameInstallDirectory,
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn",
            "FFXIV_CHR0011223344556677",
            "UISAVE.DAT");
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        File.WriteAllText(saveFilePath, string.Empty);

        GameInstallDirectoryUpdateResult result = store.SetGameInstallDirectoryFromLoadedSaveFile(saveFilePath);

        Assert.Equal(GameInstallDirectoryUpdateResult.Relocated, result);
        Assert.Equal(WayMarkOpenDirectoryMode.GameCharacterDirectory, store.Settings.WayMarkOpenDirectoryMode);
        Assert.True(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);
    }

    [Fact]
    public void SetGameInstallDirectoryFromLoadedSaveFile_WhenSettingsAlreadyHasDirectory_DoesNotOverwrite()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string existingInstallDirectory = Path.Combine(testDirectory, "ExistingGame");
        string existingExecutablePath = Path.Combine(existingInstallDirectory, "game", "ffxiv_dx11.exe");
        string otherInstallDirectory = Path.Combine(testDirectory, "OtherGame");
        string otherExecutablePath = Path.Combine(otherInstallDirectory, "game", "ffxiv_dx11.exe");
        string saveFilePath = Path.Combine(
            otherInstallDirectory,
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn",
            "FFXIV_CHR0011223344556677",
            "UISAVE.DAT");
        Directory.CreateDirectory(Path.GetDirectoryName(existingExecutablePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(otherExecutablePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(saveFilePath)!);
        File.WriteAllText(existingExecutablePath, string.Empty);
        File.WriteAllText(otherExecutablePath, string.Empty);
        File.WriteAllText(saveFilePath, string.Empty);
        store.SaveSettings(new AppSettings
        {
            GameInstallDirectory = existingExecutablePath
        });

        GameInstallDirectoryUpdateResult result = store.SetGameInstallDirectoryFromLoadedSaveFile(saveFilePath);

        Assert.Equal(GameInstallDirectoryUpdateResult.Unchanged, result);
        Assert.Equal(Path.GetFullPath(existingInstallDirectory), store.Settings.GameInstallDirectory);
    }

    [Fact]
    public void SaveSettings_WhenGameInstallDirectoryInvalid_ThrowsAndDoesNotReplaceCurrentSettings()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string validGameInstallDirectory = Path.Combine(testDirectory, "ValidGame");
        string validGameExecutablePath = Path.Combine(validGameInstallDirectory, "game", "ffxiv_dx11.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(validGameExecutablePath)!);
        File.WriteAllText(validGameExecutablePath, string.Empty);
        store.SaveSettings(new AppSettings
        {
            MaxBackupCount = 37,
            GameInstallDirectory = validGameExecutablePath
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.SaveSettings(new AppSettings
            {
                MaxBackupCount = 7,
                GameInstallDirectory = Path.Combine(testDirectory, "MissingGame")
            }));

        Assert.Contains("游戏安装目录无效", exception.Message);
        Assert.Equal(37, store.Settings.MaxBackupCount);
        Assert.Equal(Path.GetFullPath(validGameInstallDirectory), store.Settings.GameInstallDirectory);
    }

    [Fact]
    public void SaveSettings_WhenCustomDirectoryModeDirectoryInvalid_ThrowsAndDoesNotReplaceCurrentSettings()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string validCustomDirectory = Path.Combine(testDirectory, "ValidCustom");
        Directory.CreateDirectory(validCustomDirectory);
        store.SaveSettings(new AppSettings
        {
            MaxBackupCount = 37,
            WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.CustomDirectory,
            WayMarkCustomDirectory = validCustomDirectory
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.SaveSettings(new AppSettings
            {
                MaxBackupCount = 7,
                WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.CustomDirectory,
                WayMarkCustomDirectory = Path.Combine(testDirectory, "MissingCustom")
            }));

        Assert.Contains("自定义目录无效", exception.Message);
        Assert.Equal(37, store.Settings.MaxBackupCount);
        Assert.Equal(WayMarkOpenDirectoryMode.CustomDirectory, store.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(Path.GetFullPath(validCustomDirectory), store.Settings.WayMarkCustomDirectory);
    }

    [Fact]
    public void Initialize_WhenCustomDirectoryModeDirectoryMissing_FallsBackToDefaultWithWarning()
    {
        AppDataStore store = CreateStore();
        string missingCustomDirectory = Path.Combine(testDirectory, "MissingCustom");
        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsFilePath)!);
        File.WriteAllText(
            store.SettingsFilePath,
            JsonSerializer.Serialize(new AppSettings
            {
                WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.CustomDirectory,
                WayMarkOpenDirectoryModeInitialized = true,
                WayMarkCustomDirectory = missingCustomDirectory
            }));

        store.Initialize();

        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.False(store.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(missingCustomDirectory), store.Settings.WayMarkCustomDirectory);
        Assert.Contains(store.ConsumeDataLoadWarnings(), warning =>
            warning.Contains("自定义目录无法使用") &&
            warning.Contains(missingCustomDirectory));
    }

    [Fact]
    public void SaveSettings_PersistsStartupAndAppearanceSettings()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string customDirectory = Path.Combine(
            testDirectory,
            "\u6700\u7EC8\u5E7B\u60F3XIV",
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn");
        string gameInstallDirectory = Path.Combine(
            testDirectory,
            "\u6700\u7EC8\u5E7B\u60F3XIV",
            "game",
            "ffxiv_dx11.exe");
        string gameInstallRootDirectory = Path.GetDirectoryName(Path.GetDirectoryName(gameInstallDirectory)!)!;
        Directory.CreateDirectory(Path.GetDirectoryName(gameInstallDirectory)!);
        File.WriteAllText(gameInstallDirectory, string.Empty);

        store.SaveSettings(new AppSettings
        {
            UseWayMarkImageLabels = false,
            StartupWayMarkAction = StartupWayMarkAction.LoadMostRecentFile,
            WayMarkFavoriteSaveMode = WayMarkFavoriteSaveMode.Auto,
            MapDataTableMode = MapDataTableMode.Automatic,
            MapDataTableModeInitialized = true,
            MapDataSource = MapDataSource.LocalGame,
            MapDataSourceInitialized = true,
            MapDataOnlineSource = MapDataOnlineSourceKind.DiemoeMatcha,
            UnknownMapIdPolicy = UnknownMapIdPolicy.AllowUnknown,
            ShowAllowUnknownMapIdPolicyWarning = false,
            WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.GameCharacterDirectory,
            GameInstallDirectory = gameInstallDirectory,
            WayMarkCustomDirectory = customDirectory,
            AutoBackupAfterLoad = true,
            AutoBackupBeforeRestore = false,
            LimitBackupCountPerUser = false,
            MaxBackupCountPerUser = 12,
            MaxLogFileSizeMb = 13,
            MaxLogFileCount = 4,
            LastServerListManualRefreshAttempt = new DateTime(2026, 6, 18, 8, 30, 0),
            WindowLayout = new WindowLayoutSettings
            {
                WayMarkFavoriteListRatio = 0.2,
                WayMarkFavoriteEditorRatio = 0.5,
                WayMarkFavoritePreviewRatio = 0.3,
                WayMarkFavoritePickerLeft = 11,
                WayMarkFavoritePickerTop = 22,
                WayMarkFavoritePickerWidth = 777,
                WayMarkFavoritePickerHeight = 555,
                WayMarkFavoritePickerListRatio = 0.65
            }
        });

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();
        Assert.False(reloadedStore.Settings.UseWayMarkImageLabels);
        Assert.Equal(StartupWayMarkAction.LoadMostRecentFile, reloadedStore.Settings.StartupWayMarkAction);
        Assert.Equal(WayMarkFavoriteSaveMode.Auto, reloadedStore.Settings.WayMarkFavoriteSaveMode);
        Assert.Equal(MapDataTableMode.Automatic, reloadedStore.Settings.MapDataTableMode);
        Assert.True(reloadedStore.Settings.MapDataTableModeInitialized);
        Assert.Equal(MapDataSource.LocalGame, reloadedStore.Settings.MapDataSource);
        Assert.True(reloadedStore.Settings.MapDataSourceInitialized);
        Assert.Equal(MapDataOnlineSourceKind.DiemoeMatcha, reloadedStore.Settings.MapDataOnlineSource);
        Assert.Equal(UnknownMapIdPolicy.AllowUnknown, reloadedStore.Settings.UnknownMapIdPolicy);
        Assert.False(reloadedStore.Settings.ShowAllowUnknownMapIdPolicyWarning);
        Assert.Equal(WayMarkOpenDirectoryMode.GameCharacterDirectory, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.True(reloadedStore.Settings.WayMarkOpenDirectoryModeInitialized);
        Assert.Equal(Path.GetFullPath(gameInstallRootDirectory), reloadedStore.Settings.GameInstallDirectory);
        Assert.Equal(Path.GetFullPath(customDirectory), reloadedStore.Settings.WayMarkCustomDirectory);
        Assert.True(reloadedStore.Settings.AutoBackupAfterLoad);
        Assert.False(reloadedStore.Settings.AutoBackupBeforeRestore);
        Assert.False(reloadedStore.Settings.LimitBackupCountPerUser);
        Assert.Equal(12, reloadedStore.Settings.MaxBackupCountPerUser);
        Assert.Equal(13, reloadedStore.Settings.MaxLogFileSizeMb);
        Assert.Equal(4, reloadedStore.Settings.MaxLogFileCount);
        Assert.Equal(new DateTime(2026, 6, 18, 8, 30, 0), reloadedStore.Settings.LastServerListManualRefreshAttempt);
        Assert.Equal(0.2, reloadedStore.Settings.WindowLayout.WayMarkFavoriteListRatio);
        Assert.Equal(0.5, reloadedStore.Settings.WindowLayout.WayMarkFavoriteEditorRatio);
        Assert.Equal(0.3, reloadedStore.Settings.WindowLayout.WayMarkFavoritePreviewRatio);
        Assert.Equal(11, reloadedStore.Settings.WindowLayout.WayMarkFavoritePickerLeft);
        Assert.Equal(22, reloadedStore.Settings.WindowLayout.WayMarkFavoritePickerTop);
        Assert.Equal(777, reloadedStore.Settings.WindowLayout.WayMarkFavoritePickerWidth);
        Assert.Equal(555, reloadedStore.Settings.WindowLayout.WayMarkFavoritePickerHeight);
        Assert.Equal(0.65, reloadedStore.Settings.WindowLayout.WayMarkFavoritePickerListRatio);
    }

    [Fact]
    public void Initialize_RepairsUtf8DecodedAsGbkConfiguredPaths()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string gameInstallDirectory = Path.Combine(
            testDirectory,
            "\u6700\u7EC8\u5E7B\u60F3XIV",
            "game",
            "ffxiv_dx11.exe");
        string gameInstallRootDirectory = Path.GetDirectoryName(Path.GetDirectoryName(gameInstallDirectory)!)!;
        string customDirectory = Path.Combine(
            testDirectory,
            "\u6700\u7EC8\u5E7B\u60F3XIV",
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn");
        string garbledDirectory = CreateUtf8DecodedAsGbk(customDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(gameInstallDirectory)!);
        File.WriteAllText(gameInstallDirectory, string.Empty);
        Directory.CreateDirectory(customDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsFilePath)!);
        File.WriteAllText(
            store.SettingsFilePath,
            JsonSerializer.Serialize(new AppSettings
            {
                GameInstallDirectory = CreateUtf8DecodedAsGbk(gameInstallDirectory),
                WayMarkCustomDirectory = garbledDirectory
            }));

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();

        Assert.Equal(Path.GetFullPath(gameInstallRootDirectory), reloadedStore.Settings.GameInstallDirectory);
        Assert.Equal(Path.GetFullPath(customDirectory), reloadedStore.Settings.WayMarkCustomDirectory);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.False(reloadedStore.Settings.WayMarkOpenDirectoryModeInitialized);
    }

    [Fact]
    public void AddRecentFile_WhenSettingsSaveFails_DoesNotMutateRecentFilesInMemory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string oldPath = Path.Combine(testDirectory, "old.dat");
        store.AddRecentFile(oldPath);
        Directory.Delete(store.BackupsDirectory, recursive: true);
        File.WriteAllText(store.BackupsDirectory, "不是目录");

        store.AddRecentFile(Path.Combine(testDirectory, "new.dat"));

        List<string> recentFiles = store.GetRecentFiles();
        Assert.Single(recentFiles);
        Assert.Equal(Path.GetFullPath(oldPath), recentFiles[0]);
    }

    [Fact]
    public async Task ChangeDataDirectory_WhenBootstrapWriteFails_RestoresPreviousState()
    {
        DateTime mapSuccessfulSyncAt = new(2026, 6, 18, 10, 0, 0);
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddException(new InvalidOperationException("模拟本地解析失败"));
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            MaxBackupCount = 37,
            RecentFiles = [Path.Combine(testDirectory, "old.dat")]
        });
        EnableMapDataLocalGameSource(store);
        CharacterProfile profile = store.GetOrCreateCharacter("abc");
        profile.CharacterName = "旧角色";
        store.SaveCharacters();
        WriteMapDataCache(908, "旧地图", "old-map-version", mapSuccessfulSyncAt);
        MapDataLoadResult mapDataResult = await store.EnsureMapDataAvailableAsync();
        Assert.True(mapDataResult.Success);
        Assert.Equal("旧地图", MapData.GetName(908));

        string oldDataDirectory = store.DataDirectory;
        string oldSettingsFilePath = store.SettingsFilePath;
        string targetDirectory = Path.Combine(testDirectory, "NewData");
        File.Delete(store.BootstrapFilePath);
        Directory.CreateDirectory(store.BootstrapFilePath);

        Assert.Throws<AppDataStoreException>(() => store.ChangeDataDirectory(targetDirectory));

        Assert.Equal(oldDataDirectory, store.DataDirectory);
        Assert.Equal(oldSettingsFilePath, store.SettingsFilePath);
        Assert.True(Directory.Exists(oldDataDirectory));
        Assert.True(File.Exists(store.MigrationStateFilePath));
        Assert.Equal(37, store.Settings.MaxBackupCount);
        Assert.Single(store.Settings.RecentFiles);
        Assert.EndsWith("old.dat", store.Settings.RecentFiles[0]);
        Assert.Contains(store.Characters, character =>
            character.UserID == "ABC" && character.CharacterName == "旧角色");
        Assert.Equal("old-map-version", store.MapDataVersion);
        Assert.Equal(mapSuccessfulSyncAt, store.MapDataLastSuccessfulSyncAt);
        Assert.Equal("旧地图", MapData.GetName(908));
        Assert.DoesNotContain(store.ConsumeDataLoadWarnings(), warning => warning.Contains("工具设置无法读取"));
    }

    [Fact]
    public void ChangeDataDirectory_WhenMigratingData_LoadsMigratedMapCacheAndCleansOldDirectory()
    {
        DateTime mapSuccessfulSyncAt = new(2026, 6, 18, 10, 20, 0);
        AppDataStore store = CreateStore();
        store.Initialize();
        EnableMapDataLocalGameSource(store);
        WriteMapDataCache(901, "迁移地图", "migrated-map-version", mapSuccessfulSyncAt);
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "MigratedData");

        DataDirectoryMigrationResult result = store.ChangeDataDirectory(targetDirectory);

        Assert.True(result.CleanupCompleted, $"{result.ErrorMessage} | {string.Join(", ", result.PendingItems)}");
        Assert.Equal(NormalizeDirectoryPathForTest(targetDirectory), NormalizeDirectoryPathForTest(store.DataDirectory));
        Assert.True(File.Exists(Path.Combine(targetDirectory, "cache", "mapdata.csv")));
        Assert.True(File.Exists(Path.Combine(targetDirectory, "cache", "mapdata.meta.json")));
        Assert.Equal(Directory.EnumerateFiles(targetDirectory, "*", SearchOption.AllDirectories).Count(), result.MigratedFileCount);
        Assert.False(Directory.Exists(oldDataDirectory));
        Assert.False(File.Exists(store.MigrationStateFilePath));
        Assert.Equal("migrated-map-version", store.MapDataVersion);
        Assert.Equal(mapSuccessfulSyncAt, store.MapDataLastSuccessfulSyncAt);
        Assert.Equal("迁移地图", MapData.GetName(901));
    }

    [Fact]
    public async Task ChangeDataDirectoryAsync_WhenMigratingData_ReportsProgressAndCleansOldDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings { MaxBackupCount = 41 });
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "AsyncMigratedData");
        RecordingMigrationProgress progress = new();

        DataDirectoryMigrationResult result = await store.ChangeDataDirectoryAsync(targetDirectory, progress);

        Assert.True(result.CleanupCompleted, $"{result.ErrorMessage} | {string.Join(", ", result.PendingItems)}");
        Assert.Equal(NormalizeDirectoryPathForTest(targetDirectory), NormalizeDirectoryPathForTest(store.DataDirectory));
        Assert.Equal(41, store.Settings.MaxBackupCount);
        Assert.False(Directory.Exists(oldDataDirectory));
        Assert.False(File.Exists(store.MigrationStateFilePath));
        Assert.Contains(progress.Events, item => item.StageName == "复制文件");
        Assert.Contains(progress.Events, item => item.StageName == "校验文件");
        Assert.Contains(progress.Events, item => item.StageName == "清理旧目录");
        Assert.Contains(progress.Events, item => item.StageName == "迁移完成" && item.Percent == 100);
    }
    [Fact]
    public void ChangeDataDirectory_WhenSourceContainsUnmanagedContent_LeavesItInOldDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string oldDataDirectory = store.DataDirectory;
        string unmanagedRootFile = Path.Combine(oldDataDirectory, "manual-note.txt");
        string unmanagedDirectory = Path.Combine(oldDataDirectory, "manual-folder");
        Directory.CreateDirectory(unmanagedDirectory);
        File.WriteAllText(unmanagedRootFile, "用户手动放入的文件");
        File.WriteAllText(Path.Combine(unmanagedDirectory, "manual-data.txt"), "用户手动放入的目录内容");
        string targetDirectory = Path.Combine(testDirectory, "ManagedOnlyData");

        DataDirectoryMigrationResult result = store.ChangeDataDirectory(targetDirectory);

        Assert.True(result.CleanupCompleted, $"{result.ErrorMessage} | {string.Join(", ", result.PendingItems)}");
        Assert.True(result.OldDirectoryRetained);
        Assert.True(File.Exists(Path.Combine(targetDirectory, "configs", "config.json")));
        Assert.False(File.Exists(Path.Combine(targetDirectory, "manual-note.txt")));
        Assert.False(Directory.Exists(Path.Combine(targetDirectory, "manual-folder")));
        Assert.True(File.Exists(unmanagedRootFile));
        Assert.True(File.Exists(Path.Combine(unmanagedDirectory, "manual-data.txt")));
        Assert.False(File.Exists(store.MigrationStateFilePath));
        Assert.Contains("非本工具管理", result.ErrorMessage);
        Assert.Contains(result.PendingItems, item => item.Contains("非本工具管理"));
    }

    [Fact]
    public void ChangeDataDirectory_WhenOnlyUnmanagedContentRemains_ClearsMigrationStateAndDoesNotRetry()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string oldDataDirectory = store.DataDirectory;
        File.WriteAllText(Path.Combine(oldDataDirectory, "manual-note.txt"), "用户手动放入的文件");
        string targetDirectory = Path.Combine(testDirectory, "ChangedTargetAfterCleanupData");
        DataDirectoryMigrationResult result = store.ChangeDataDirectory(targetDirectory);
        Assert.True(result.CleanupCompleted, $"{result.ErrorMessage} | {string.Join(", ", result.PendingItems)}");
        Assert.True(result.OldDirectoryRetained);
        Assert.False(File.Exists(store.MigrationStateFilePath));

        store.SaveSettings(new AppSettings { MaxBackupCount = 52 });

        AppDataStore recoveredStore = CreateStore();
        recoveredStore.Initialize();

        Assert.Equal(NormalizeDirectoryPathForTest(targetDirectory), NormalizeDirectoryPathForTest(recoveredStore.DataDirectory));
        Assert.Equal(52, recoveredStore.Settings.MaxBackupCount);
        Assert.DoesNotContain(recoveredStore.ConsumeDataLoadWarnings(), warning => warning.Contains("自动恢复上次工具数据目录迁移失败"));
        Assert.Empty(recoveredStore.ConsumeMigrationReports());
    }

    [Fact]
    public void ChangeDataDirectory_WhenTargetDirectoryNotEmpty_ThrowsAndKeepsCurrentDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "ExistingData");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "existing.txt"), "已有数据");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.ChangeDataDirectory(targetDirectory));

        Assert.Contains("必须为空", exception.Message);
        Assert.Equal(oldDataDirectory, store.DataDirectory);
        Assert.True(Directory.Exists(oldDataDirectory));
        Assert.False(File.Exists(store.MigrationStateFilePath));
    }

    [Fact]
    public void ChangeDataDirectory_WhenTargetIsRootDirectory_ThrowsAndKeepsCurrentDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string oldDataDirectory = store.DataDirectory;
        string rootDirectory = Path.GetPathRoot(testDirectory)!;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.ChangeDataDirectory(rootDirectory));

        Assert.Contains("根目录", exception.Message);
        Assert.Equal(oldDataDirectory, store.DataDirectory);
        Assert.False(File.Exists(store.MigrationStateFilePath));
    }

    [Fact]
    public void ChangeDataDirectory_WhenTargetIsSharedRootDirectory_ThrowsAndKeepsCurrentDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string oldDataDirectory = store.DataDirectory;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.ChangeDataDirectory("\\\\server\\share\\"));

        Assert.Contains("根目录", exception.Message);
        Assert.Equal(oldDataDirectory, store.DataDirectory);
        Assert.False(File.Exists(store.MigrationStateFilePath));
    }

    [Fact]
    public void Initialize_WhenMigrationReadyToCommit_RecoversAndCleansOldDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings { MaxBackupCount = 37 });
        CharacterProfile profile = store.GetOrCreateCharacter("abc");
        profile.CharacterName = "恢复角色";
        store.SaveCharacters();
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "RecoveredData");
        WriteReadyToCommitMigrationState(store.MigrationStateFilePath, oldDataDirectory, targetDirectory);

        AppDataStore recoveredStore = CreateStore();
        recoveredStore.Initialize();

        Assert.Equal(NormalizeDirectoryPathForTest(targetDirectory), NormalizeDirectoryPathForTest(recoveredStore.DataDirectory));
        Assert.Equal(37, recoveredStore.Settings.MaxBackupCount);
        Assert.Contains(recoveredStore.Characters, character =>
            character.UserID == "ABC" && character.CharacterName == "恢复角色");
        Assert.False(Directory.Exists(oldDataDirectory));
        Assert.False(File.Exists(recoveredStore.MigrationStateFilePath));
        DataDirectoryMigrationResult report = Assert.Single(recoveredStore.ConsumeMigrationReports());
        Assert.True(report.CleanupCompleted);
        Assert.True(report.AutomaticRetryAttempted);
        Assert.True(report.MigratedFileCount > 0);
    }

    [Fact]
    public void Initialize_WhenMigrationCopying_ResumesAndCleansOldDirectory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings { MaxBackupCount = 37 });
        CharacterProfile profile = store.GetOrCreateCharacter("abc");
        profile.CharacterName = "续迁角色";
        store.SaveCharacters();
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "ResumeCopyingData");
        WriteCopyingMigrationState(store.MigrationStateFilePath, oldDataDirectory, targetDirectory);

        AppDataStore recoveredStore = CreateStore();
        recoveredStore.Initialize();

        Assert.Equal(NormalizeDirectoryPathForTest(targetDirectory), NormalizeDirectoryPathForTest(recoveredStore.DataDirectory));
        Assert.Equal(37, recoveredStore.Settings.MaxBackupCount);
        Assert.Contains(recoveredStore.Characters, character =>
            character.UserID == "ABC" && character.CharacterName == "续迁角色");
        Assert.False(Directory.Exists(oldDataDirectory));
        Assert.False(File.Exists(recoveredStore.MigrationStateFilePath));
        DataDirectoryMigrationResult report = Assert.Single(recoveredStore.ConsumeMigrationReports());
        Assert.True(report.CleanupCompleted);
        Assert.True(report.AutomaticRetryAttempted);
        Assert.True(report.MigratedFileCount > 0);
    }

    [Fact]
    public void Initialize_WhenMigrationStateUsesSameSourceAndTarget_DoesNotDeleteData()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings { MaxBackupCount = 38 });
        string oldDataDirectory = store.DataDirectory;
        string oldSettingsFilePath = store.SettingsFilePath;
        WriteReadyToCommitMigrationState(store.MigrationStateFilePath, oldDataDirectory, oldDataDirectory);

        AppDataStore recoveredStore = CreateStore();
        recoveredStore.Initialize();

        Assert.Equal(NormalizeDirectoryPathForTest(oldDataDirectory), NormalizeDirectoryPathForTest(recoveredStore.DataDirectory));
        Assert.True(Directory.Exists(oldDataDirectory));
        Assert.True(File.Exists(oldSettingsFilePath));
        Assert.Equal(38, recoveredStore.Settings.MaxBackupCount);
        Assert.True(File.Exists(recoveredStore.MigrationStateFilePath));
        Assert.Empty(recoveredStore.ConsumeMigrationReports());
        Assert.Contains(recoveredStore.ConsumeDataLoadWarnings(), warning => warning.Contains("不能是同一个目录"));
    }

    [Fact]
    public void Initialize_WhenPreCommitRecoveryTargetContainsChangedFile_DoesNotOverwriteTarget()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings { MaxBackupCount = 39 });
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "ChangedRecoveryTargetData");
        WriteCopyingMigrationState(store.MigrationStateFilePath, oldDataDirectory, targetDirectory);
        string changedTargetFile = Path.Combine(targetDirectory, "configs", "config.json");
        string changedTargetContent = "{\"MaxBackupCount\":9999}";
        File.WriteAllText(changedTargetFile, changedTargetContent);
        string unknownTargetFile = Path.Combine(targetDirectory, "manual-note.txt");
        File.WriteAllText(unknownTargetFile, "用户放入目标目录的内容");

        AppDataStore recoveredStore = CreateStore();
        recoveredStore.Initialize();

        Assert.Equal(NormalizeDirectoryPathForTest(oldDataDirectory), NormalizeDirectoryPathForTest(recoveredStore.DataDirectory));
        Assert.True(Directory.Exists(oldDataDirectory));
        Assert.Equal(39, recoveredStore.Settings.MaxBackupCount);
        Assert.Equal(changedTargetContent, File.ReadAllText(changedTargetFile));
        Assert.True(File.Exists(unknownTargetFile));
        Assert.True(File.Exists(recoveredStore.MigrationStateFilePath));
        Assert.Empty(recoveredStore.ConsumeMigrationReports());
        Assert.Contains(recoveredStore.ConsumeDataLoadWarnings(), warning => warning.Contains("避免覆盖数据"));
    }

    [Fact]
    public void Initialize_WhenMigrationCleanupStillIncomplete_AddsMigrationReport()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings { MaxBackupCount = 37 });
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "CleanupPendingData");
        WriteCommittedMigrationState(store.MigrationStateFilePath, oldDataDirectory, targetDirectory);
        File.AppendAllText(store.SettingsFilePath, Environment.NewLine + "changed after migration state");
        File.WriteAllText(Path.Combine(oldDataDirectory, "manual-note.txt"), "用户手动放入的文件");

        AppDataStore recoveredStore = CreateStore();
        recoveredStore.Initialize();

        Assert.Equal(NormalizeDirectoryPathForTest(targetDirectory), NormalizeDirectoryPathForTest(recoveredStore.DataDirectory));
        Assert.True(Directory.Exists(oldDataDirectory));
        Assert.True(File.Exists(recoveredStore.MigrationStateFilePath));
        DataDirectoryMigrationResult report = Assert.Single(recoveredStore.ConsumeMigrationReports());
        Assert.False(report.CleanupCompleted);
        Assert.True(report.AutomaticRetryAttempted);
        Assert.Equal(NormalizeDirectoryPathForTest(oldDataDirectory), NormalizeDirectoryPathForTest(report.SourceDirectory));
        Assert.Equal(NormalizeDirectoryPathForTest(targetDirectory), NormalizeDirectoryPathForTest(report.TargetDirectory));
        Assert.Contains(report.PendingItems, item => item.EndsWith(Path.Combine("configs", "config.json"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.PendingItems, item => item.Contains("非本工具管理"));
        Assert.Contains("发生变化", report.ErrorMessage);
    }

    [Fact]
    public void RecoverInterruptedMigration_WhenBootstrapWriteFails_RestoresPreviousState()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string oldDataDirectory = store.DataDirectory;
        string targetDirectory = Path.Combine(testDirectory, "BootstrapWriteFailsData");
        WriteReadyToCommitMigrationState(store.MigrationStateFilePath, oldDataDirectory, targetDirectory);

        AppDataStore recoveredStore = CreateStore();
        using FileStream bootstrapLock = new(
            recoveredStore.BootstrapFilePath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        InvokeRecoverInterruptedDataDirectoryMigration(recoveredStore);

        Assert.Equal(NormalizeDirectoryPathForTest(oldDataDirectory), NormalizeDirectoryPathForTest(recoveredStore.DataDirectory));
        Assert.Contains(recoveredStore.ConsumeDataLoadWarnings(), warning => warning.Contains("自动恢复上次工具数据目录迁移失败"));
    }

    [Fact]
    public void GetOrCreateCharacter_DoesNotWriteCharactersFileUntilSaveCharacters()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string before = File.ReadAllText(store.CharactersFilePath);

        CharacterProfile profile = store.GetOrCreateCharacter("abc123");

        Assert.Equal("ABC123", profile.UserID);
        Assert.Equal(before, File.ReadAllText(store.CharactersFilePath));

        store.SaveCharacters();

        string after = File.ReadAllText(store.CharactersFilePath);
        Assert.Contains("ABC123", after);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void ScanLocalGameCharacters_CreatesProfilesForEveryLocalSaveFile()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string gameInstallDirectory = CreateGameInstallDirectory("LocalCharactersGame");
        CreateLocalCharacterSaveFile(gameInstallDirectory, "0011223344556677");
        CreateLocalCharacterSaveFile(gameInstallDirectory, "8899AABBCCDDEEFF");
        CreateLocalCharacterDirectory(gameInstallDirectory, "1122334455667788");
        CreateLocalCharacterSaveFile(gameInstallDirectory, "FFEEDDCCBBAA9988_Manual");
        store.SaveSettings(new AppSettings
        {
            GameInstallDirectory = gameInstallDirectory
        });

        LocalGameCharacterScanResult result = store.ScanLocalGameCharacters();

        Assert.Equal(2, result.LocalCharacterCount);
        Assert.Equal(2, result.CreatedProfileCount);
        Assert.Empty(result.Errors);
        Assert.Contains(store.Characters, character => character.UserID == "0011223344556677");
        Assert.Contains(store.Characters, character => character.UserID == "8899AABBCCDDEEFF");
        Assert.DoesNotContain(store.Characters, character => character.UserID == "1122334455667788");
        Assert.DoesNotContain(store.Characters, character => character.UserID.StartsWith("FFEEDDCCBBAA9988", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetAvailableLocalGameCharacters_ReturnsOnlyRecordedCharactersWithExistingSaveFiles()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string gameInstallDirectory = CreateGameInstallDirectory("AvailableCharactersGame");
        string firstSaveFile = CreateLocalCharacterSaveFile(gameInstallDirectory, "0011223344556677");
        CreateLocalCharacterSaveFile(gameInstallDirectory, "8899AABBCCDDEEFF");
        store.SaveSettings(new AppSettings
        {
            GameInstallDirectory = gameInstallDirectory
        });

        Assert.Empty(store.GetAvailableLocalGameCharacters());

        LocalGameCharacterScanPreparation preparation = store.PrepareLocalGameCharacterScan();
        LocalGameCharacterScanResult result = store.ApplyLocalGameCharacterScan(preparation);
        File.Delete(firstSaveFile);

        IReadOnlyList<LocalGameCharacter> availableCharacters = store.GetAvailableLocalGameCharacters();

        Assert.Equal(2, result.CreatedProfileCount);
        LocalGameCharacter character = Assert.Single(availableCharacters);
        Assert.Equal("8899AABBCCDDEEFF", character.UserID);
        Assert.Equal(string.Empty, character.CharacterName);
        Assert.True(File.Exists(character.SaveFilePath));
    }

    [Fact]
    public void ApplyLocalGameCharacterScan_WhenGameInstallDirectoryChanged_SkipsStaleResult()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string firstGameInstallDirectory = CreateGameInstallDirectory("FirstLocalCharactersGame");
        string secondGameInstallDirectory = CreateGameInstallDirectory("SecondLocalCharactersGame");
        CreateLocalCharacterSaveFile(firstGameInstallDirectory, "0011223344556677");
        store.SaveSettings(new AppSettings
        {
            GameInstallDirectory = firstGameInstallDirectory
        });
        LocalGameCharacterScanPreparation preparation = store.PrepareLocalGameCharacterScan();
        store.SaveSettings(new AppSettings
        {
            GameInstallDirectory = secondGameInstallDirectory
        });

        LocalGameCharacterScanResult result = store.ApplyLocalGameCharacterScan(preparation);

        Assert.True(result.SkippedBecauseGameInstallDirectoryChanged);
        Assert.False(result.Changed);
        Assert.Empty(store.Characters);
    }

    [Fact]
    public void AddRecentFile_DeduplicatesLimitsAndPersistsRecentFiles()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        for (int index = 0; index < 12; index++)
        {
            store.AddRecentFile(Path.Combine(testDirectory, $"UISAVE_{index}.DAT"));
        }

        string duplicatePath = Path.Combine(testDirectory, "UISAVE_5.DAT");
        store.AddRecentFile(duplicatePath);

        List<string> recentFiles = store.GetRecentFiles();
        Assert.Equal(10, recentFiles.Count);
        Assert.Equal(Path.GetFullPath(duplicatePath), recentFiles[0]);
        Assert.Equal(recentFiles.Count, recentFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();
        Assert.Equal(recentFiles, reloadedStore.GetRecentFiles());
    }

    [Fact]
    public void CreateBackup_GeneratesMetadataFromBackedUpFile()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string sourceDirectory = Path.Combine(testDirectory, "FFXIV_CHR0123456789ABCDEF");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "UISAVE.DAT");
        byte[] sourceBytes = CreateMinimalUISaveFile(regionId: 123);
        File.WriteAllBytes(sourceFilePath, sourceBytes);

        BackupMetadata backup = store.CreateBackup(
            sourceFilePath,
            cleanupAfterCreate: false,
            creationTrigger: BackupCreationTriggers.BeforeSave);

        Assert.True(File.Exists(backup.BackupFilePath));
        byte[] backupBytes = File.ReadAllBytes(backup.BackupFilePath);
        Assert.Equal(sourceBytes, backupBytes);
        Assert.Equal(backupBytes.Length, backup.SourceFileSize);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(backupBytes)), backup.SourceFileSha256);
        Assert.Equal(BackupCreationTriggers.BeforeSave, backup.CreationTrigger);
        Assert.Equal("保存前自动备份", backup.CreationTriggerDisplay);
        BackupMarkerSnapshot snapshot = Assert.Single(backup.MarkerSnapshots);
        Assert.Equal(123, snapshot.RegionID);
        Assert.Equal(1, snapshot.SlotIndex);
        Assert.Equal(8, snapshot.EnabledPointCount);
    }

    [Fact]
    public void CreateBackup_WhenSourceIsTrustedCharacterFolder_UsesFolderUserIdAsEffectiveUserId()
    {
        string gameInstallDirectory = Path.Combine(testDirectory, "GameInstall");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        AppDataStore store = CreateStore();
        store.Initialize();
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        store.SaveSettings(new AppSettings
        {
            GameInstallDirectory = gameExecutablePath
        });
        string gameConfigRoot = Path.Combine(gameInstallDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        string sourceDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHRAAAABBBBCCCCDDDD");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "UISAVE.DAT");
        File.WriteAllBytes(sourceFilePath, CreateMinimalUISaveFile(regionId: 123));

        BackupMetadata backup = store.CreateBackup(sourceFilePath, cleanupAfterCreate: false);

        Assert.True(store.IsTrustedGameCharacterSaveFile(sourceFilePath));
        Assert.Equal("AAAABBBBCCCCDDDD", backup.FolderUserID);
        Assert.Equal("0123456789ABCDEF", backup.FileUserID);
        Assert.True(backup.UseFolderUserIDAsEffectiveUserID);
        Assert.Equal("AAAABBBBCCCCDDDD", backup.EffectiveUserID);
        Assert.Contains("AAAABBBBCCCCDDDD", backup.Id);
    }

    [Fact]
    public void CreateBackup_WhenSourceCharacterFolderHasSuffix_UsesFileUserIdAsEffectiveUserId()
    {
        string gameInstallDirectory = Path.Combine(testDirectory, "GameInstall");
        string gameExecutablePath = Path.Combine(gameInstallDirectory, "game", "ffxiv_dx11.exe");
        AppDataStore store = CreateStore();
        store.Initialize();
        Directory.CreateDirectory(Path.GetDirectoryName(gameExecutablePath)!);
        File.WriteAllText(gameExecutablePath, string.Empty);
        store.SaveSettings(new AppSettings
        {
            GameInstallDirectory = gameExecutablePath
        });
        string gameConfigRoot = Path.Combine(gameInstallDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        string sourceDirectory = Path.Combine(gameConfigRoot, "FFXIV_CHRAAAABBBBCCCCDDDD_Manual");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "UISAVE.DAT");
        File.WriteAllBytes(sourceFilePath, CreateMinimalUISaveFile(regionId: 123));

        BackupMetadata backup = store.CreateBackup(sourceFilePath, cleanupAfterCreate: false);

        Assert.False(store.IsTrustedGameCharacterSaveFile(sourceFilePath));
        Assert.Null(AppDataStore.GetUserIDFromCharacterFolder(sourceFilePath));
        Assert.Equal(string.Empty, backup.FolderUserID);
        Assert.Equal("0123456789ABCDEF", backup.FileUserID);
        Assert.False(backup.UseFolderUserIDAsEffectiveUserID);
        Assert.Equal("0123456789ABCDEF", backup.EffectiveUserID);
        Assert.Contains("0123456789ABCDEF", backup.Id);
    }

    [Fact]
    public void CreateBackup_WhenSourceIsUntrustedCharacterFolder_UsesFileUserIdAsEffectiveUserId()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string sourceDirectory = Path.Combine(testDirectory, "Temporary", "FFXIV_CHRAAAABBBBCCCCDDDD");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "UISAVE.DAT");
        File.WriteAllBytes(sourceFilePath, CreateMinimalUISaveFile(regionId: 123));

        BackupMetadata backup = store.CreateBackup(sourceFilePath, cleanupAfterCreate: false);

        Assert.False(store.IsTrustedGameCharacterSaveFile(sourceFilePath));
        Assert.Equal("AAAABBBBCCCCDDDD", backup.FolderUserID);
        Assert.Equal("0123456789ABCDEF", backup.FileUserID);
        Assert.False(backup.UseFolderUserIDAsEffectiveUserID);
        Assert.Equal("0123456789ABCDEF", backup.EffectiveUserID);
        Assert.Contains("0123456789ABCDEF", backup.Id);
    }

    [Fact]
    public void CreateBackup_WhenSourceIsOutsideCharacterFolder_UsesFileUserIdAsEffectiveUserId()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        string sourceDirectory = Path.Combine(testDirectory, "ManualFiles");
        Directory.CreateDirectory(sourceDirectory);
        string sourceFilePath = Path.Combine(sourceDirectory, "UISAVE.DAT");
        File.WriteAllBytes(sourceFilePath, CreateMinimalUISaveFile(regionId: 123));

        BackupMetadata backup = store.CreateBackup(sourceFilePath, cleanupAfterCreate: false);

        Assert.False(store.IsTrustedGameCharacterSaveFile(sourceFilePath));
        Assert.Equal(string.Empty, backup.FolderUserID);
        Assert.Equal("0123456789ABCDEF", backup.FileUserID);
        Assert.False(backup.UseFolderUserIDAsEffectiveUserID);
        Assert.Equal("0123456789ABCDEF", backup.EffectiveUserID);
        Assert.Contains("0123456789ABCDEF", backup.Id);
    }

    [Fact]
    public void CleanupBackups_WhenPerUserLimitEnabled_DeletesOldestBackupsForEachUser()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            LimitBackupCount = false,
            LimitBackupDays = false,
            LimitBackupCountPerUser = true,
            MaxBackupCountPerUser = 2
        });
        DateTime baseTime = new(2026, 6, 25, 10, 0, 0);
        string userOldDirectory = WriteBackupMetadata(store, "user-old", folderUserId: "AAA111", fileUserId: "OTHER001", backupTime: baseTime);
        string userMiddleDirectory = WriteBackupMetadata(store, "user-middle", folderUserId: "AAA111", fileUserId: "OTHER002", backupTime: baseTime.AddMinutes(1));
        string userNewestDirectory = WriteBackupMetadata(store, "user-newest", folderUserId: "AAA111", fileUserId: "OTHER003", backupTime: baseTime.AddMinutes(2));
        string otherUserOldDirectory = WriteBackupMetadata(store, "other-old", "BBB222", baseTime);
        string otherUserNewDirectory = WriteBackupMetadata(store, "other-new", "BBB222", baseTime.AddMinutes(1));
        string unknownUserDirectory = WriteBackupMetadata(store, "unknown-old", string.Empty, baseTime);

        store.CleanupBackups(userNewestDirectory);

        Assert.False(Directory.Exists(userOldDirectory));
        Assert.True(Directory.Exists(userMiddleDirectory));
        Assert.True(Directory.Exists(userNewestDirectory));
        Assert.True(Directory.Exists(otherUserOldDirectory));
        Assert.True(Directory.Exists(otherUserNewDirectory));
        Assert.True(Directory.Exists(unknownUserDirectory));
        List<BackupMetadata> remainingBackups = store.LoadBackups();
        Assert.Equal(2, remainingBackups.Count(backup =>
            string.Equals(backup.EffectiveUserID, "AAA111", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, remainingBackups.Count(backup =>
            string.Equals(backup.EffectiveUserID, "BBB222", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(remainingBackups, backup => string.IsNullOrWhiteSpace(backup.EffectiveUserID));
    }

    [Fact]
    public void AddRecentFile_WhenSettingsJsonInvalid_DoesNotThrowOrOverwriteCorruptedFile()
    {
        string settingsPath = Path.Combine(testDirectory, "Data", "configs", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, "{ 损坏的 JSON");
        AppDataStore store = CreateStore();
        store.Initialize();

        Exception? exception = Record.Exception(() =>
            store.AddRecentFile(Path.Combine(testDirectory, "UISAVE.DAT")));

        Assert.Null(exception);
        Assert.Equal("{ 损坏的 JSON", File.ReadAllText(settingsPath));
    }

    [Fact]
    public async Task EnsureServerListAvailableAsync_WhenValidCacheLoaded_UsesCacheWithoutRefresh()
    {
        string serversPath = Path.Combine(testDirectory, "Data", "cache", "servers.json");
        Directory.CreateDirectory(Path.GetDirectoryName(serversPath)!);
        ServerListCache cache = new()
        {
            SourceUrl = "测试缓存",
            LastUpdated = DateTime.Now,
            LastSuccessfulSyncAt = DateTime.MinValue,
            Groups =
            [
                new ServerGroup
                {
                    DataCenter = "测试大区",
                    Worlds = ["测试服务器"]
                }
            ]
        };
        File.WriteAllText(serversPath, JsonSerializer.Serialize(cache));
        AppDataStore store = CreateStore();
        store.Initialize();

        ServerListLoadResult result = await store.EnsureServerListAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Single(store.ServerList.Groups);
        Assert.Equal("测试大区", store.ServerList.Groups[0].DataCenter);
    }

    [Fact]
    public void MapData_GetName_WhenNameMissing_ReturnsUnavailableName()
    {
        MapData.Clear();

        Assert.Equal("暂无名称", MapData.GetName(123));
    }

    [Fact]
    public void MapData_GetName_WhenNameDisplayDisabled_ReturnsMapIdName()
    {
        MapData.DisableNameDisplay();

        Assert.Equal("地图 ID 123", MapData.GetName(123));
        Assert.Equal("123", MapData.GetDisplayName(123));
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenOnlineReferenceSucceeds_WritesCacheAndAppliesMapData()
    {
        FakeAppDataNetworkClient networkClient = new();
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(GitHubMapDataCommitSha);
        networkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        networkClient.AddResponse(pinnedCsvUrl, CreateContentFinderConditionCsv(123, "在线副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("2026.06.10.0000.0000", result.Version);
        Assert.Equal(pinnedCsvUrl, result.SourcePath);
        Assert.Equal(2, networkClient.Requests.Count);
        Assert.Equal(MapDataOnlineReferenceCommitApiUrl, networkClient.Requests[0].Url);
        Assert.Equal("FFXIVConfigEditor", networkClient.Requests[0].Headers["User-Agent"]);
        Assert.Equal(pinnedCsvUrl, networkClient.Requests[1].Url);
        Assert.Equal("在线副本", MapData.GetName(123));
        Assert.True(MapData.IsNameDisplayEnabled);
        Assert.Contains((ushort)123, MapData.GetKnownMapIds());
        Assert.True(store.MapDataLastSuccessfulSyncAt > DateTime.MinValue);

        MapDataCache cache = ReadMapDataCacheMetadata(store);
        Dictionary<ushort, string> cachedMapNames = ReadMapDataCacheCsv(store);
        Assert.Equal("online-reference:github", cache.Source);
        Assert.Equal(pinnedCsvUrl, cache.SourcePath);
        Assert.Equal("在线副本", cachedMapNames[123]);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenOnlineCacheWriteFails_ReturnsFailureWithoutApplyingMapData()
    {
        FakeAppDataNetworkClient networkClient = new();
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(GitHubMapDataCommitSha);
        networkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        networkClient.AddResponse(pinnedCsvUrl, CreateContentFinderConditionCsv(123, "在线副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        Directory.CreateDirectory(store.MapDataCacheFilePath);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.False(result.Success);
        Assert.False(result.CacheAvailable);
        Assert.Equal("应用在线地图数据", result.FailureStage);
        Assert.Contains("应用失败", result.FailureReason);
        Assert.Contains("写入地图数据缓存", result.FailureReason);
        Assert.False(MapData.HasData);
        Assert.Equal(2, networkClient.Requests.Count);
        Assert.Contains(networkClient.Requests, request =>
            string.Equals(request.Url, MapDataOnlineReferenceCommitApiUrl, StringComparison.Ordinal));
        Assert.Contains(networkClient.Requests, request =>
            string.Equals(request.Url, pinnedCsvUrl, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenGitHubSnapshotMatchesCache_DoesNotReportUpdated()
    {
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(GitHubMapDataCommitSha);
        FakeAppDataNetworkClient firstNetworkClient = new();
        firstNetworkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        firstNetworkClient.AddResponse(pinnedCsvUrl, CreateContentFinderConditionCsv(123, "在线副本"));
        AppDataStore firstStore = CreateStore(firstNetworkClient);
        firstStore.Initialize();
        MapDataLoadResult firstResult = await firstStore.EnsureMapDataAvailableAsync();
        Assert.True(firstResult.Updated);

        MapData.Clear();
        FakeAppDataNetworkClient secondNetworkClient = new();
        secondNetworkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        AppDataStore secondStore = CreateStore(secondNetworkClient);
        secondStore.Initialize();

        MapDataLoadResult secondResult = await secondStore.EnsureMapDataAvailableAsync();

        Assert.True(secondResult.Success);
        Assert.False(secondResult.Updated);
        Assert.Equal("2026.06.10.0000.0000", secondResult.Version);
        Assert.Equal("在线副本", MapData.GetName(123));
        Assert.Single(secondNetworkClient.Requests);
        Assert.Equal(MapDataOnlineReferenceCommitApiUrl, secondNetworkClient.Requests[0].Url);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenGitHubSnapshotMatchesCache_DownloadsCsvAndDoesNotReportUpdated()
    {
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(GitHubMapDataCommitSha);
        FakeAppDataNetworkClient firstNetworkClient = new();
        firstNetworkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        firstNetworkClient.AddResponse(pinnedCsvUrl, CreateContentFinderConditionCsv(123, "在线副本"));
        AppDataStore firstStore = CreateStore(firstNetworkClient);
        firstStore.Initialize();
        MapDataLoadResult firstResult = await firstStore.EnsureMapDataAvailableAsync();
        Assert.True(firstResult.Updated);

        MapData.Clear();
        FakeAppDataNetworkClient secondNetworkClient = new();
        secondNetworkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        secondNetworkClient.AddResponse(pinnedCsvUrl, CreateContentFinderConditionCsv(123, "在线副本"));
        AppDataStore secondStore = CreateStore(secondNetworkClient);
        secondStore.Initialize();

        MapDataLoadResult secondResult = await secondStore.ForceRefreshMapDataAsync();

        Assert.True(secondResult.Success);
        Assert.False(secondResult.Updated);
        Assert.Equal("2026.06.10.0000.0000", secondResult.Version);
        Assert.Equal("在线副本", MapData.GetName(123));
        Assert.Equal(2, secondNetworkClient.Requests.Count);
        Assert.Equal(MapDataOnlineReferenceCommitApiUrl, secondNetworkClient.Requests[0].Url);
        Assert.Equal(pinnedCsvUrl, secondNetworkClient.Requests[1].Url);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenGitHubContentMatchesCacheButVersionChanges_DoesNotReportUpdated()
    {
        WriteMapDataCache(123, "在线副本", "hash-legacy", source: MapDataSource.OnlineReference);
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(GitHubMapDataCommitSha);
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        networkClient.AddResponse(pinnedCsvUrl, CreateContentFinderConditionCsv(123, "在线副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.Equal("2026.06.10.0000.0000", result.Version);
        Assert.Equal("在线副本", MapData.GetName(123));
        MapDataCache cache = ReadMapDataCacheMetadata(store);
        Assert.Equal("2026.06.10.0000.0000", cache.Version);
        Assert.Equal(pinnedCsvUrl, cache.SourcePath);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenGitHubCommitLookupFails_FallsBackToRawContentHashVersion()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataOnlineReferenceCommitApiUrl, new InvalidOperationException("GitHub API 不可用"));
        networkClient.AddResponse(MapDataOnlineReferenceCsvUrl, CreateContentFinderConditionCsv(124, "在线回退副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.StartsWith("hash-", result.Version);
        Assert.Equal(MapDataOnlineReferenceCsvUrl, result.SourcePath);
        Assert.Equal("在线回退副本", MapData.GetName(124));
        Assert.Equal(2, networkClient.Requests.Count);
        Assert.Equal(MapDataOnlineReferenceCommitApiUrl, networkClient.Requests[0].Url);
        Assert.Equal(MapDataOnlineReferenceCsvUrl, networkClient.Requests[1].Url);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenGitHubCommitMessageHasNoVersion_UsesCommitDateAndShortCommitSha()
    {
        const string commitSha = "ae635eceb241bce63b72530828c540ffe7d0c497";
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(commitSha);
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(commitSha, "Update ContentFinderCondition.csv", "2026-05-25T15:30:45Z"));
        networkClient.AddResponse(pinnedCsvUrl, CreateContentFinderConditionCsv(125, "短哈希副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.Equal("2026.05.25.153045-ae635ec", result.Version);
        Assert.Equal(pinnedCsvUrl, result.SourcePath);
        Assert.Equal("短哈希副本", MapData.GetName(125));
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenGitHubPinnedRawFails_FallsBackToRawContentHashVersion()
    {
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(GitHubMapDataCommitSha);
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        networkClient.AddException(pinnedCsvUrl, new InvalidOperationException("固定文件不可用"));
        networkClient.AddResponse(MapDataOnlineReferenceCsvUrl, CreateContentFinderConditionCsv(126, "raw 回退副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.StartsWith("hash-", result.Version);
        Assert.Equal(MapDataOnlineReferenceCsvUrl, result.SourcePath);
        Assert.Equal("raw 回退副本", MapData.GetName(126));
        Assert.Equal(3, networkClient.Requests.Count);
        Assert.Equal(MapDataOnlineReferenceCommitApiUrl, networkClient.Requests[0].Url);
        Assert.Equal(pinnedCsvUrl, networkClient.Requests[1].Url);
        Assert.Equal(MapDataOnlineReferenceCsvUrl, networkClient.Requests[2].Url);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenContentFinderConditionRowsShareTerritoryType_UsesRowIdAsMapId()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            MapDataOnlineReferenceCsvUrl,
            CreateContentFinderConditionCsv(
                (123, 900, "在线副本一"),
                (124, 900, "在线副本二")));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.Equal("在线副本一", MapData.GetName(123));
        Assert.Equal("在线副本二", MapData.GetName(124));
        Assert.Contains((ushort)123, MapData.GetKnownMapIds());
        Assert.Contains((ushort)124, MapData.GetKnownMapIds());
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenGitHubReferenceFails_DoesNotUseDiemoeFallback()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataOnlineReferenceCsvUrl, new InvalidOperationException("主来源不可用"));
        networkClient.AddResponse(MapDataDiemoeVersionUrl, "build_version=diemoe-version");
        networkClient.AddResponse(MapDataDiemoeInstanceUrl, CreateDiemoeMapInstanceJson(456, "备用副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.CacheAvailable);
        Assert.Contains("主来源不可用", result.FailureReason);
        Assert.Equal(2, networkClient.Requests.Count);
        Assert.Equal(MapDataOnlineReferenceCommitApiUrl, networkClient.Requests[0].Url);
        Assert.Equal(MapDataOnlineReferenceCsvUrl, networkClient.Requests[1].Url);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenDiemoeSelected_UsesDiemoeOnly()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataDiemoeVersionUrl, "build_version=20260603.001");
        networkClient.AddResponse(MapDataDiemoeInstanceUrl, CreateDiemoeMapInstanceJson(654, "优先备用副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataOnlineSource = MapDataOnlineSourceKind.DiemoeMatcha;
        store.SaveSettings(settings);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.Equal("2026.06.03.001", result.Version);
        Assert.Equal("优先备用副本", MapData.GetName(654));
        Assert.Equal(2, networkClient.Requests.Count);
        Assert.Equal(MapDataDiemoeVersionUrl, networkClient.Requests[0].Url);
        Assert.Equal(MapDataDiemoeInstanceUrl, networkClient.Requests[1].Url);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenDiemoeVersionMatchesCache_DoesNotDownloadInstanceJson()
    {
        WriteMapDataCache(
            654,
            "优先备用副本",
            "2026.06.03.001",
            sourceKey: "online-reference:diemoe",
            sourceFingerprint: "diemoe:2026.06.03.001");
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataDiemoeVersionUrl, "build_version=20260603.001");
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataOnlineSource = MapDataOnlineSourceKind.DiemoeMatcha;
        store.SaveSettings(settings);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.Equal("2026.06.03.001", result.Version);
        Assert.Equal("优先备用副本", MapData.GetName(654));
        Assert.Single(networkClient.Requests);
        Assert.Equal(MapDataDiemoeVersionUrl, networkClient.Requests[0].Url);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenDiemoeVersionMatchesCache_DownloadsInstanceJson()
    {
        WriteMapDataCache(
            654,
            "优先备用副本",
            "2026.06.03.001",
            sourceKey: "online-reference:diemoe",
            sourceFingerprint: "diemoe:2026.06.03.001");
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataDiemoeVersionUrl, "build_version=20260603.001");
        networkClient.AddResponse(MapDataDiemoeInstanceUrl, CreateDiemoeMapInstanceJson(654, "优先备用副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataOnlineSource = MapDataOnlineSourceKind.DiemoeMatcha;
        store.SaveSettings(settings);

        MapDataLoadResult result = await store.ForceRefreshMapDataAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.Equal("2026.06.03.001", result.Version);
        Assert.Equal("优先备用副本", MapData.GetName(654));
        Assert.Equal(2, networkClient.Requests.Count);
        Assert.Equal(MapDataDiemoeVersionUrl, networkClient.Requests[0].Url);
        Assert.Equal(MapDataDiemoeInstanceUrl, networkClient.Requests[1].Url);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenDiemoeSelectedAndGitHubCacheExists_DoesNotUseGitHubCache()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataDiemoeVersionUrl, new InvalidOperationException("diemoe 不可用"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        WriteMapDataCache(999, "GitHub 缓存", "github-cache", source: MapDataSource.OnlineReference);
        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataOnlineSource = MapDataOnlineSourceKind.DiemoeMatcha;
        store.SaveSettings(settings);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.False(result.Success);
        Assert.False(result.CacheAvailable);
        Assert.False(MapData.HasData);
        Assert.Single(networkClient.Requests);
        Assert.Equal(MapDataDiemoeVersionUrl, networkClient.Requests[0].Url);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenOnlineSourceChangesDuringDownload_DiscardsStaleSnapshot()
    {
        MapData.Clear();
        string pinnedCsvUrl = CreateMapDataPinnedCsvUrl(GitHubMapDataCommitSha);
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            MapDataOnlineReferenceCommitApiUrl,
            CreateGitHubCommitApiResponse(GitHubMapDataCommitSha, "[ver 2026.06.10.0000.0000]"));
        TaskCompletionSource<string> pinnedCsvResponse = networkClient.AddPendingResponse(pinnedCsvUrl);
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        Task<MapDataLoadResult> refreshTask = store.ForceRefreshMapDataAsync();
        Assert.Contains(networkClient.Requests, request =>
            string.Equals(request.Url, pinnedCsvUrl, StringComparison.Ordinal));

        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataOnlineSource = MapDataOnlineSourceKind.DiemoeMatcha;
        store.SaveSettings(settings);

        pinnedCsvResponse.SetResult(CreateContentFinderConditionCsv(123, "过期 GitHub 副本"));
        MapDataLoadResult result = await refreshTask;

        Assert.False(result.Success);
        Assert.Contains("地图数据来源已在刷新过程中变更", result.FailureReason);
        Assert.False(MapData.HasData);
        Assert.False(File.Exists(store.MapDataCacheFilePath));
        Assert.False(File.Exists(store.MapDataCacheMetadataFilePath));
    }

    [Fact]
    public void SaveSettings_WhenMapDataOnlineSourceInvalid_NormalizesToGitHub()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataOnlineSource = (MapDataOnlineSourceKind)999;

        store.SaveSettings(settings);

        Assert.Equal(MapDataOnlineSourceKind.ContentFinderConditionCsv, store.Settings.MapDataOnlineSource);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenGitHubReferenceHasUnsupportedStructure_DoesNotUseDiemoeFallback()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataOnlineReferenceCsvUrl, "id,title\n1,不是地图数据");
        networkClient.AddResponse(MapDataDiemoeVersionUrl, "build_version=diemoe-structure-version");
        networkClient.AddResponse(MapDataDiemoeInstanceUrl, CreateDiemoeMapInstanceJson(789, "结构兜底副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.CacheAvailable);
        Assert.Contains("格式不受支持", result.FailureReason);
        Assert.Equal(2, networkClient.Requests.Count);
        Assert.Equal(MapDataOnlineReferenceCommitApiUrl, networkClient.Requests[0].Url);
        Assert.Equal(MapDataOnlineReferenceCsvUrl, networkClient.Requests[1].Url);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenManualCsvExists_LoadsUserMapDataWithoutNetwork()
    {
        FakeAppDataNetworkClient networkClient = new();
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        EnableMapDataManualTable(store);
        WriteUserMapDataCsv(store, 321, "手动副本");

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Matches(@"^\d{4}\.\d{2}\.\d{2}\.\d{6}-[0-9a-f]{12}$", result.Version);
        Assert.Equal(store.UserMapDataFilePath, result.SourcePath);
        Assert.Empty(networkClient.Requests);
        Assert.Equal("手动副本", MapData.GetName(321));
        Assert.Equal(store.UserMapDataFilePath, store.MapDataContentSourceText);
        Assert.True(store.MapDataLastUpdated > DateTime.MinValue);
        Assert.True(store.MapDataLastSuccessfulSyncAt > DateTime.MinValue);
        MapDataCache cache = ReadMapDataCacheMetadata(store);
        Dictionary<ushort, string> cachedMapNames = ReadMapDataCacheCsv(store);
        Assert.Equal("user-csv", cache.Source);
        Assert.Equal(store.UserMapDataFilePath, cache.SourcePath);
        Assert.StartsWith("user-csv:", cache.SourceFingerprint, StringComparison.Ordinal);
        Assert.Equal("手动副本", cachedMapNames[321]);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenManualCsvSnapshotMatchesPreviousRead_DoesNotReportUpdated()
    {
        FakeAppDataNetworkClient firstNetworkClient = new();
        AppDataStore firstStore = CreateStore(firstNetworkClient);
        firstStore.Initialize();
        EnableMapDataManualTable(firstStore);
        WriteUserMapDataCsv(firstStore, 321, "手动副本");
        MapDataLoadResult firstResult = await firstStore.EnsureMapDataAvailableAsync();
        Assert.True(firstResult.Updated);

        MapData.Clear();
        FakeAppDataNetworkClient secondNetworkClient = new();
        AppDataStore secondStore = CreateStore(secondNetworkClient);
        secondStore.Initialize();
        EnableMapDataManualTable(secondStore);

        MapDataLoadResult secondResult = await secondStore.EnsureMapDataAvailableAsync();

        Assert.True(secondResult.Success);
        Assert.False(secondResult.Updated);
        Assert.Equal(firstResult.Version, secondResult.Version);
        Assert.Equal("手动副本", MapData.GetName(321));
        Assert.Empty(secondNetworkClient.Requests);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenManualCsvSizeChangesWithSameTimestamp_ReadsUserCsv()
    {
        FakeAppDataNetworkClient firstNetworkClient = new();
        AppDataStore firstStore = CreateStore(firstNetworkClient);
        firstStore.Initialize();
        EnableMapDataManualTable(firstStore);
        WriteUserMapDataCsv(firstStore, 321, "手动副本");
        MapDataLoadResult firstResult = await firstStore.EnsureMapDataAvailableAsync();
        Assert.True(firstResult.Updated);

        DateTime originalWriteTime = File.GetLastWriteTime(firstStore.UserMapDataFilePath);
        WriteUserMapDataCsv(firstStore, 322, "修改后的手动副本");
        File.SetLastWriteTime(firstStore.UserMapDataFilePath, originalWriteTime);

        MapData.Clear();
        FakeAppDataNetworkClient secondNetworkClient = new();
        AppDataStore secondStore = CreateStore(secondNetworkClient);
        secondStore.Initialize();
        EnableMapDataManualTable(secondStore);

        MapDataLoadResult secondResult = await secondStore.EnsureMapDataAvailableAsync();

        Assert.True(secondResult.Success);
        Assert.True(secondResult.Updated);
        Assert.NotEqual(firstResult.Version, secondResult.Version);
        Assert.Equal("修改后的手动副本", MapData.GetName(322));
        Assert.Empty(secondNetworkClient.Requests);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenManualCsvContentChangesWithSameFingerprint_ReadsUserCsv()
    {
        FakeAppDataNetworkClient firstNetworkClient = new();
        AppDataStore firstStore = CreateStore(firstNetworkClient);
        firstStore.Initialize();
        EnableMapDataManualTable(firstStore);
        WriteRawUserMapDataCsv(firstStore, "ID,Name\r\n321,Alpha\r\n");
        MapDataLoadResult firstResult = await firstStore.EnsureMapDataAvailableAsync();
        Assert.True(firstResult.Updated);

        DateTime originalWriteTime = File.GetLastWriteTime(firstStore.UserMapDataFilePath);
        WriteRawUserMapDataCsv(firstStore, "ID,Name\r\n322,Bravo\r\n");
        File.SetLastWriteTime(firstStore.UserMapDataFilePath, originalWriteTime);

        MapData.Clear();
        FakeAppDataNetworkClient secondNetworkClient = new();
        AppDataStore secondStore = CreateStore(secondNetworkClient);
        secondStore.Initialize();
        EnableMapDataManualTable(secondStore);

        MapDataLoadResult secondResult = await secondStore.ForceRefreshMapDataAsync();

        Assert.True(secondResult.Success);
        Assert.True(secondResult.Updated);
        Assert.NotEqual(firstResult.Version, secondResult.Version);
        Assert.Equal("Bravo", MapData.GetName(322));
        Assert.Empty(secondNetworkClient.Requests);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenManualCsvHasInvalidRows_RequiresRepairWithoutApplyingPartialRows()
    {
        FakeAppDataNetworkClient networkClient = new();
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        EnableMapDataManualTable(store);
        WriteRawUserMapDataCsv(store, "ID,Name\r\n321,有效行\r\nbad,坏 ID\r\n");
        MapData.Clear();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.False(result.Success);
        Assert.True(result.RequiresUserMapDataRepair);
        Assert.Equal(store.UserMapDataFilePath, result.SourcePath);
        Assert.Contains("需要修复", result.FailureReason);
        Assert.Contains("第 2 行", result.FailureReason);
        Assert.False(MapData.HasData);
        Assert.Empty(networkClient.Requests);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenManualCsvInvalidAndCacheExists_UsesCache()
    {
        DateTime successfulSyncAt = new(2026, 7, 6, 9, 0, 0);
        FakeAppDataNetworkClient networkClient = new();
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        EnableMapDataManualTable(store);
        WriteMapDataCache(
            321,
            "缓存手动副本",
            "manual-cache-version",
            successfulSyncAt,
            store.UserMapDataFilePath,
            sourceKey: "user-csv",
            sourceFingerprint: "user-csv:cached");
        WriteRawUserMapDataCsv(store, "ID,Name\r\n");

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.True(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.True(result.RequiresUserMapDataRepair);
        Assert.Equal(store.UserMapDataFilePath, result.SourcePath);
        Assert.Equal("读取手动地图数据", result.FailureStage);
        Assert.Contains("为空或格式不受支持", result.FailureReason);
        Assert.Equal("manual-cache-version", result.Version);
        Assert.Equal("缓存手动副本", MapData.GetName(321));
        Assert.Equal(successfulSyncAt, store.MapDataLastSuccessfulSyncAt);
        Assert.Empty(networkClient.Requests);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenManualCsvInvalidAndCacheFingerprintMatches_StillRequiresRepair()
    {
        DateTime successfulSyncAt = new(2026, 7, 6, 9, 0, 0);
        FakeAppDataNetworkClient networkClient = new();
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        EnableMapDataManualTable(store);
        WriteRawUserMapDataCsv(store, "ID,Name\r\n321,有效行\r\nbad,坏 ID\r\n");
        FileInfo userMapDataFileInfo = new(store.UserMapDataFilePath);
        string matchingVersion = $"{MapDataSourceParsers.FormatSnapshotTimestamp(userMapDataFileInfo.LastWriteTime)}-cached";
        string matchingFingerprint = FormattableString.Invariant(
            $"user-csv:{userMapDataFileInfo.LastWriteTimeUtc.Ticks}:{userMapDataFileInfo.Length}");
        WriteMapDataCache(
            321,
            "缓存手动副本",
            matchingVersion,
            successfulSyncAt,
            store.UserMapDataFilePath,
            sourceKey: "user-csv",
            sourceFingerprint: matchingFingerprint);
        MapData.Clear();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.True(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.True(result.RequiresUserMapDataRepair);
        Assert.Equal(store.UserMapDataFilePath, result.SourcePath);
        Assert.Contains("第 2 行", result.FailureReason);
        Assert.Equal(matchingVersion, result.Version);
        Assert.Equal("缓存手动副本", MapData.GetName(321));
        Assert.Equal(successfulSyncAt, store.MapDataLastSuccessfulSyncAt);
        Assert.Empty(networkClient.Requests);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenManualCsvMissing_CreatesDefaultCsvAndLoadsIt()
    {
        FakeAppDataNetworkClient networkClient = new();
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        EnableMapDataManualTable(store);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal(store.UserMapDataFilePath, result.SourcePath);
        Assert.True(File.Exists(store.UserMapDataFilePath));
        string csv = File.ReadAllText(store.UserMapDataFilePath);
        Assert.StartsWith("1,监狱废墟托托·拉克千狱\r\n", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("ID,Name", csv, StringComparison.Ordinal);
        Dictionary<ushort, string> userMapNames = ReadUserMapDataCsv(store);
        Assert.Equal("监狱废墟托托·拉克千狱", userMapNames[1]);
        Assert.Equal("神灵圣域放浪神古神殿", userMapNames[10]);
        Assert.Equal(10, userMapNames.Count);
        Assert.Equal("监狱废墟托托·拉克千狱", MapData.GetName(1));
        Assert.Equal("神灵圣域放浪神古神殿", MapData.GetName(10));
        Assert.Empty(networkClient.Requests);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenAutomaticSourceUpdates_DoesNotOverwriteUserCsv()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataOnlineReferenceCsvUrl, CreateContentFinderConditionCsv(123, "自动副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        WriteUserMapDataCsv(store, 654, "用户副本");

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.Equal("自动副本", MapData.GetName(123));
        Dictionary<ushort, string> userMapNames = ReadUserMapDataCsv(store);
        Assert.Equal("用户副本", userMapNames[654]);
        Assert.False(userMapNames.ContainsKey(123));
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenLocalGameSucceeds_WritesCacheAndAppliesMapData()
    {
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        string sourcePath = Path.Combine(gameInstallDirectory, "game", "sqpack");
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddSnapshot("local-version", sourcePath, 123, "测试副本");
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("local-version", result.Version);
        Assert.Equal(sourcePath, result.SourcePath);
        Assert.Equal("测试副本", MapData.GetName(123));
        MapDataCache cache = ReadMapDataCacheMetadata(store);
        Dictionary<ushort, string> cachedMapNames = ReadMapDataCacheCsv(store);
        Assert.Equal("local-version", cache.Version);
        Assert.Equal("local-sqpack", cache.Source);
        Assert.Equal(sourcePath, cache.SourcePath);
        Assert.StartsWith("sqpack-indexes:", cache.SourceFingerprint, StringComparison.Ordinal);
        Assert.Equal("测试副本", cachedMapNames[123]);
        Assert.True(cache.LastUpdated > DateTime.MinValue);
        Assert.True(cache.LastSuccessfulSyncAt > DateTime.MinValue);
        Assert.True(store.MapDataLastUpdated > DateTime.MinValue);
        Assert.True(store.MapDataLastSuccessfulSyncAt > DateTime.MinValue);
        Assert.False(File.Exists(Path.Combine(store.DataDirectory, "instance.json")));
        Assert.False(File.Exists(Path.Combine(store.DataDirectory, "mapdata.version")));
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenLocalGameFailsAndCacheExists_UsesCache()
    {
        DateTime successfulSyncAt = new(2026, 6, 18, 9, 0, 0);
        WriteMapDataCache(456, "缓存副本", "cache-version", successfulSyncAt);
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddException(new InvalidOperationException("模拟本地解析失败"));
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.True(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("解析本地游戏地图数据", result.FailureStage);
        Assert.Equal("模拟本地解析失败", result.FailureReason);
        Assert.Equal("cache-version", result.Version);
        Assert.Equal("缓存副本", MapData.GetName(456));
        Assert.Equal(successfulSyncAt, store.MapDataLastSuccessfulSyncAt);

        MapDataCache savedCache = ReadMapDataCacheMetadata(store);
        Assert.Equal(successfulSyncAt, savedCache.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenLocalSnapshotMatchesCache_UsesCacheWithoutRewritingCsv()
    {
        DateTime originalSuccessfulSyncAt = new(2026, 6, 18, 9, 15, 0);
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        string sourcePath = Path.Combine(gameInstallDirectory, "game", "sqpack");
        const string sourceFingerprint = "sqpack-indexes:same";
        WriteMapDataCache(567, "同版本缓存副本", "same-version", originalSuccessfulSyncAt, sourcePath, sourceFingerprint: sourceFingerprint);
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddIdentity("same-version", sourcePath, sourceFingerprint);
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("same-version", result.Version);
        Assert.Equal("同版本缓存副本", MapData.GetName(567));
        Assert.Single(mapDataProvider.IdentityRequests);
        Assert.Empty(mapDataProvider.Requests);
        MapDataCache savedCache = ReadMapDataCacheMetadata(store);
        Assert.True(savedCache.LastSuccessfulSyncAt > originalSuccessfulSyncAt);
        Assert.True(store.MapDataLastSuccessfulSyncAt > originalSuccessfulSyncAt);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenLocalSnapshotFingerprintChanges_ReadsLocalGame()
    {
        DateTime originalSuccessfulSyncAt = new(2026, 6, 18, 9, 20, 0);
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        string sourcePath = Path.Combine(gameInstallDirectory, "game", "sqpack");
        WriteMapDataCache(
            567,
            "旧本地缓存副本",
            "same-version",
            originalSuccessfulSyncAt,
            sourcePath,
            sourceFingerprint: "sqpack-indexes:old");
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddSnapshot("same-version", sourcePath, 568, "新本地副本", "sqpack-indexes:new");
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.Equal("same-version", result.Version);
        Assert.Equal("新本地副本", MapData.GetName(568));
        Assert.Single(mapDataProvider.IdentityRequests);
        Assert.Single(mapDataProvider.Requests);
        MapDataCache savedCache = ReadMapDataCacheMetadata(store);
        Assert.Equal("sqpack-indexes:new", savedCache.SourceFingerprint);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenLocalSnapshotMatchesCache_ReadsLocalGameAndDoesNotReportUpdated()
    {
        DateTime originalSuccessfulSyncAt = new(2026, 6, 18, 9, 30, 0);
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        string sourcePath = Path.Combine(gameInstallDirectory, "game", "sqpack");
        const string sourceFingerprint = "sqpack-indexes:same";
        WriteMapDataCache(568, "同版本手动缓存副本", "same-version", originalSuccessfulSyncAt, sourcePath, sourceFingerprint: sourceFingerprint);
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddSnapshot("same-version", sourcePath, 568, "同版本手动缓存副本", sourceFingerprint);
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.ForceRefreshMapDataAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("same-version", result.Version);
        Assert.Equal("同版本手动缓存副本", MapData.GetName(568));
        Assert.Single(mapDataProvider.IdentityRequests);
        Assert.Single(mapDataProvider.Requests);
        Assert.True(store.MapDataLastSuccessfulSyncAt > originalSuccessfulSyncAt);

        MapDataCache savedCache = ReadMapDataCacheMetadata(store);
        Assert.True(savedCache.LastSuccessfulSyncAt > originalSuccessfulSyncAt);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenLocalGameFailsAndNoCache_ReturnsFailure()
    {
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddException(new InvalidOperationException("模拟本地解析失败"));
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.False(result.CacheAvailable);
        Assert.Equal("解析本地游戏地图数据", result.FailureStage);
        Assert.Equal("模拟本地解析失败", result.FailureReason);
        Assert.False(MapData.HasData);
        Assert.Equal("暂无名称", MapData.GetName(123));
        Assert.Equal(DateTime.MinValue, store.MapDataLastSuccessfulSyncAt);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenLocalGameHasNoMaps_DoesNotRecordSuccessfulSyncTime()
    {
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddSnapshot("empty-local", Path.Combine(gameInstallDirectory, "game", "sqpack"), new Dictionary<ushort, string>());
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.ForceRefreshMapDataAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.False(result.CacheAvailable);
        Assert.Equal("解析本地游戏地图数据", result.FailureStage);
        Assert.Equal("本地游戏地图数据为空或格式不受支持。", result.FailureReason);
        Assert.Equal(DateTime.MinValue, store.MapDataLastSuccessfulSyncAt);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenLocalGameFailsAndCacheExists_UsesCache()
    {
        WriteMapDataCache(789, "缓存副本", "cache-version");
        string gameInstallDirectory = CreateGameInstallDirectory("Game");
        FakeLocalGameMapDataProvider mapDataProvider = new();
        mapDataProvider.AddException(new InvalidOperationException("模拟本地解析失败"));
        AppDataStore store = CreateStore(mapDataProvider, () => gameInstallDirectory);
        store.Initialize();
        EnableMapDataLocalGameSource(store);

        MapDataLoadResult result = await store.ForceRefreshMapDataAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.True(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("解析本地游戏地图数据", result.FailureStage);
        Assert.Equal("模拟本地解析失败", result.FailureReason);
        Assert.Equal("缓存副本", MapData.GetName(789));
    }

    [Fact]
    public async Task EnsureServerListAvailableAsync_WhenApiSucceeds_WritesServerCache()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            ServerStatusApiUrl,
            """
            {
              "IsSuccess": true,
              "Data": [
                {
                  "AreaName": "测试大区",
                  "Group": [
                    { "name": "测试服务器" }
                  ]
                }
              ]
            }
            """);
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        ServerListLoadResult result = await store.EnsureServerListAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Single(store.ServerList.Groups);
        Assert.Equal("测试大区", store.ServerList.Groups[0].DataCenter);
        Assert.True(store.ServerList.LastSuccessfulSyncAt > DateTime.MinValue);
        Assert.Contains("测试服务器", File.ReadAllText(store.ServersFilePath));
        Assert.Contains(networkClient.Requests, request =>
            request.Url == ServerStatusApiUrl &&
            request.Headers.ContainsKey("X-Requested-With"));
    }

    [Fact]
    public async Task RefreshServerListAsync_WhenRemoteGroupsMatchCache_ReturnsLatestWithoutUpdatingContent()
    {
        DateTime originalUpdatedAt = new(2026, 6, 18, 8, 0, 0);
        DateTime originalSuccessfulSyncAt = new(2026, 6, 18, 8, 30, 0);
        WriteServerCache(new ServerListCache
        {
            SourceUrl = "测试缓存",
            LastUpdated = originalUpdatedAt,
            LastSuccessfulSyncAt = originalSuccessfulSyncAt,
            Groups =
            [
                new ServerGroup
                {
                    DataCenter = "测试大区",
                    Worlds = ["测试服务器"]
                }
            ]
        });
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            ServerStatusApiUrl,
            """
            {
              "IsSuccess": true,
              "Data": [
                {
                  "AreaName": "测试大区",
                  "Group": [
                    { "name": "测试服务器" }
                  ]
                }
              ]
            }
            """);
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        ServerListLoadResult result = await store.RefreshServerListAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal(originalUpdatedAt, store.ServerList.LastUpdated);
        Assert.True(store.ServerList.LastSuccessfulSyncAt > originalSuccessfulSyncAt);
        Assert.Equal("测试大区", store.ServerList.Groups[0].DataCenter);
        Assert.Equal(["测试服务器"], store.ServerList.Groups[0].Worlds);

        string savedCacheText = File.ReadAllText(store.ServersFilePath);
        ServerListCache savedCache = JsonSerializer.Deserialize<ServerListCache>(savedCacheText)!;
        Assert.Equal(originalUpdatedAt, savedCache.LastUpdated);
        Assert.True(savedCache.LastSuccessfulSyncAt > originalSuccessfulSyncAt);
    }

    [Fact]
    public async Task EnsureServerListAvailableAsync_WhenApiHasNoGroups_ParsesServerPage()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(ServerStatusApiUrl, """{ "IsSuccess": false }""");
        networkClient.AddResponse(ServerListSourceUrl, "陆行鸟 拉诺西亚 幻影群岛");
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        ServerListLoadResult result = await store.EnsureServerListAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.Contains(store.ServerList.Groups, group =>
            group.DataCenter == "陆行鸟" &&
            group.Worlds.Contains("拉诺西亚"));
    }

    [Fact]
    public async Task EnsureServerListAvailableAsync_WhenNetworkFailsAndCacheExists_UsesCache()
    {
        DateTime originalAttempt = DateTime.MinValue;
        WriteServerCache(new ServerListCache
        {
            SourceUrl = "测试缓存",
            LastUpdated = DateTime.Now.AddDays(-8),
            LastSuccessfulSyncAt = originalAttempt,
            Groups =
            [
                new ServerGroup
                {
                    DataCenter = "缓存大区",
                    Worlds = ["缓存服务器"]
                }
            ]
        });
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(ServerStatusApiUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        ServerListLoadResult result = await store.EnsureServerListAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.True(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("检查服务器列表", result.FailureStage);
        Assert.Equal("模拟网络失败", result.FailureReason);
        Assert.Equal(originalAttempt, store.ServerList.LastSuccessfulSyncAt);
        Assert.Equal("缓存大区", store.ServerList.Groups[0].DataCenter);

        string savedCacheText = File.ReadAllText(store.ServersFilePath);
        ServerListCache savedCache = JsonSerializer.Deserialize<ServerListCache>(savedCacheText)!;
        Assert.Equal(originalAttempt, savedCache.LastSuccessfulSyncAt);
        Assert.Contains(savedCache.Groups, group => group.Worlds.Contains("缓存服务器"));
    }

    [Fact]
    public async Task EnsureServerListAvailableAsync_WhenNetworkFailsAndNoCache_ReturnsFailure()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(ServerStatusApiUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        ServerListLoadResult result = await store.EnsureServerListAvailableAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.False(result.CacheAvailable);
        Assert.Equal("检查服务器列表", result.FailureStage);
        Assert.Equal("模拟网络失败", result.FailureReason);
    }

    [Fact]
    public async Task TrySyncServerListAsync_WhenNetworkFailsAndCacheExists_ReturnsFalseAndKeepsSuccessfulSyncTime()
    {
        DateTime originalAttempt = DateTime.MinValue;
        WriteServerCache(new ServerListCache
        {
            SourceUrl = "测试缓存",
            LastUpdated = DateTime.Now,
            LastSuccessfulSyncAt = originalAttempt,
            Groups =
            [
                new ServerGroup
                {
                    DataCenter = "缓存大区",
                    Worlds = ["缓存服务器"]
                }
            ]
        });
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(ServerStatusApiUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        bool result = await store.TrySyncServerListAsync();

        Assert.False(result);
        Assert.Equal(originalAttempt, store.ServerList.LastSuccessfulSyncAt);
        Assert.Equal("缓存大区", store.ServerList.Groups[0].DataCenter);
    }

    [Fact]
    public async Task TrySyncServerListAsync_WhenCacheWriteFails_KeepsPreviousCacheInMemory()
    {
        WriteServerCache(new ServerListCache
        {
            SourceUrl = "测试缓存",
            LastUpdated = DateTime.Now,
            LastSuccessfulSyncAt = DateTime.MinValue,
            Groups =
            [
                new ServerGroup
                {
                    DataCenter = "缓存大区",
                    Worlds = ["缓存服务器"]
                }
            ]
        });
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(
            ServerStatusApiUrl,
            """
            {
              "IsSuccess": true,
              "Data": [
                {
                  "AreaName": "新大区",
                  "Group": [
                    { "name": "新服务器" }
                  ]
                }
              ]
            }
            """);
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        File.Delete(store.ServersFilePath);
        Directory.CreateDirectory(store.ServersFilePath);

        bool result = await store.TrySyncServerListAsync();

        Assert.False(result);
        Assert.Equal("缓存大区", store.ServerList.Groups[0].DataCenter);
        Assert.DoesNotContain(store.ServerList.Groups, group => group.DataCenter == "新大区");
    }

    [Fact]
    public void AddWayMarkFavorite_PersistsSnapshotAndDoesNotShareReferences()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        WayMark wayMark = CreateSampleWayMark(123);

        WayMarkFavorite favorite = store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(wayMark), "测试收藏");
        wayMark.RegionID = 456;
        wayMark.A.X = 999999;

        WayMarkFavorite savedFavorite = Assert.Single(store.WayMarkFavorites);
        Assert.Equal(favorite.Id, savedFavorite.Id);
        Assert.Equal("测试收藏", savedFavorite.CommentName);
        Assert.Equal((ushort)123, savedFavorite.RegionID);
        Assert.Equal(1000, savedFavorite.Marker.A.X);

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();
        WayMarkFavorite reloadedFavorite = Assert.Single(reloadedStore.WayMarkFavorites);
        Assert.Equal(favorite.Id, reloadedFavorite.Id);
        Assert.Equal("测试收藏", reloadedFavorite.CommentName);
        Assert.Equal((ushort)123, reloadedFavorite.RegionID);
        Assert.Equal(1000, reloadedFavorite.Marker.A.X);
    }

    [Fact]
    public void AddWayMarkFavorite_WhenFavoritesFileCannotBeWritten_DoesNotMutateFavoritesInMemory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(123)), "旧收藏");
        File.Delete(store.WayMarkFavoritesFilePath);
        Directory.CreateDirectory(store.WayMarkFavoritesFilePath);

        AppDataStoreException exception = Assert.Throws<AppDataStoreException>(() =>
            store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(456)), "新收藏"));

        Assert.Equal("写入本地 JSON 文件", exception.Operation);
        WayMarkFavorite favorite = Assert.Single(store.WayMarkFavorites);
        Assert.Equal("旧收藏", favorite.CommentName);
        Assert.Equal((ushort)123, favorite.RegionID);
    }

    [Fact]
    public void MoveWayMarkFavorite_PersistsOrder()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        WayMarkFavorite first = store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(101)), "第一项");
        WayMarkFavorite second = store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(102)), "第二项");
        WayMarkFavorite third = store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(103)), "第三项");

        bool moved = store.MoveWayMarkFavorite(first.Id, -1);

        Assert.True(moved);
        Assert.Equal([third.Id, first.Id, second.Id], store.WayMarkFavorites.Select(favorite => favorite.Id));

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();
        Assert.Equal([third.Id, first.Id, second.Id], reloadedStore.WayMarkFavorites.Select(favorite => favorite.Id));
    }

    [Fact]
    public void MoveWayMarkFavorite_WhenFavoritesFileCannotBeWritten_DoesNotMutateFavoritesInMemory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        WayMarkFavorite first = store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(101)), "第一项");
        WayMarkFavorite second = store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(102)), "第二项");
        string[] originalOrder = [.. store.WayMarkFavorites.Select(favorite => favorite.Id)];
        File.Delete(store.WayMarkFavoritesFilePath);
        Directory.CreateDirectory(store.WayMarkFavoritesFilePath);

        AppDataStoreException exception = Assert.Throws<AppDataStoreException>(() =>
            store.MoveWayMarkFavorite(first.Id, -1));

        Assert.Equal("写入本地 JSON 文件", exception.Operation);
        Assert.Equal(originalOrder, store.WayMarkFavorites.Select(favorite => favorite.Id));
        Assert.Equal(second.Id, store.WayMarkFavorites[0].Id);
        Assert.Equal(first.Id, store.WayMarkFavorites[1].Id);
    }

    [Fact]
    public void SortWayMarkFavoritesByRegion_PersistsOrder()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(300)), "300");
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(100)), "100");
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(200)), "200");
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(0)), "0");

        bool sortedAscending = store.SortWayMarkFavoritesByRegion(ascending: true);

        Assert.True(sortedAscending);
        Assert.Equal(new ushort[] { 100, 200, 300, 0 }, store.WayMarkFavorites.Select(favorite => favorite.RegionID));

        bool sortedDescending = store.SortWayMarkFavoritesByRegion(ascending: false);

        Assert.True(sortedDescending);
        Assert.Equal(new ushort[] { 300, 200, 100, 0 }, store.WayMarkFavorites.Select(favorite => favorite.RegionID));

        AppDataStore reloadedStore = CreateStore();
        reloadedStore.Initialize();
        Assert.Equal(new ushort[] { 300, 200, 100, 0 }, reloadedStore.WayMarkFavorites.Select(favorite => favorite.RegionID));
    }

    [Fact]
    public void SortWayMarkFavoritesByRegion_WhenFavoritesFileCannotBeWritten_DoesNotMutateFavoritesInMemory()
    {
        AppDataStore store = CreateStore();
        store.Initialize();
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(300)), "300");
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(100)), "100");
        store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(200)), "200");
        string[] originalOrder = [.. store.WayMarkFavorites.Select(favorite => favorite.Id)];
        ushort[] originalRegions = [.. store.WayMarkFavorites.Select(favorite => favorite.RegionID)];
        File.Delete(store.WayMarkFavoritesFilePath);
        Directory.CreateDirectory(store.WayMarkFavoritesFilePath);

        AppDataStoreException exception = Assert.Throws<AppDataStoreException>(() =>
            store.SortWayMarkFavoritesByRegion(ascending: true));

        Assert.Equal("\u5199\u5165\u672C\u5730 JSON \u6587\u4EF6", exception.Operation);
        Assert.Equal(originalOrder, store.WayMarkFavorites.Select(favorite => favorite.Id));
        Assert.Equal(originalRegions, store.WayMarkFavorites.Select(favorite => favorite.RegionID));
    }

    [Fact]
    public void Initialize_WhenWayMarkFavoritesJsonInvalid_DoesNotOverwriteCorruptedFileAndBlocksSave()
    {
        string favoritesPath = Path.Combine(testDirectory, "Data", "configs", "waymark-favorites.json");
        Directory.CreateDirectory(Path.GetDirectoryName(favoritesPath)!);
        File.WriteAllText(favoritesPath, "{ 损坏的 JSON");
        AppDataStore store = CreateStore();

        store.Initialize();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(CreateSampleWayMark(123)), "测试收藏"));
        Assert.Contains("waymark-favorites.json 本次启动读取失败", exception.Message);
        Assert.Equal("{ 损坏的 JSON", File.ReadAllText(favoritesPath));
        Assert.Empty(store.WayMarkFavorites);
        Assert.Contains(store.ConsumeDataLoadWarnings(), warning => warning.Contains("标点收藏无法读取"));
    }

    private sealed class RecordingMigrationProgress : IProgress<DataDirectoryMigrationProgress>
    {
        public List<DataDirectoryMigrationProgress> Events { get; } = [];

        public void Report(DataDirectoryMigrationProgress value)
        {
            Events.Add(value);
        }
    }
    private AppDataStore CreateStore()
    {
        return CreateStore(() => null);
    }

    private string CreateGameInstallDirectory(string directoryName)
    {
        string gameInstallDirectory = Path.Combine(testDirectory, directoryName);
        string gameDirectory = Path.Combine(gameInstallDirectory, "game");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "ffxiv_dx11.exe"), string.Empty);
        Directory.CreateDirectory(Path.Combine(gameDirectory, "My Games", "FINAL FANTASY XIV - A Realm Reborn"));
        return gameInstallDirectory;
    }

    private string CreateLocalCharacterDirectory(string gameInstallDirectory, string userID)
    {
        string characterDirectory = Path.Combine(
            gameInstallDirectory,
            "game",
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn",
            $"FFXIV_CHR{userID}");
        Directory.CreateDirectory(characterDirectory);
        return characterDirectory;
    }

    private string CreateLocalCharacterSaveFile(string gameInstallDirectory, string userID)
    {
        string characterDirectory = CreateLocalCharacterDirectory(gameInstallDirectory, userID);
        string saveFilePath = Path.Combine(characterDirectory, "UISAVE.DAT");
        File.WriteAllText(saveFilePath, string.Empty);
        return saveFilePath;
    }

    private AppDataStore CreateStore(Func<string?> gameInstallDirectoryDetector)
    {
        return new AppDataStore(testDirectory, gameInstallDirectoryDetector);
    }

    private AppDataStore CreateStore(
        ILocalGameMapDataProvider localGameMapDataProvider,
        Func<string?> gameInstallDirectoryDetector)
    {
        return new AppDataStore(
            testDirectory,
            new FakeAppDataNetworkClient(),
            gameInstallDirectoryDetector,
            localGameMapDataProvider);
    }

    private AppDataStore CreateStore(IAppDataNetworkClient networkClient)
    {
        return new AppDataStore(testDirectory, networkClient, () => null);
    }

    private static void EnableMapDataLocalGameSource(AppDataStore store)
    {
        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataTableMode = MapDataTableMode.Automatic;
        settings.MapDataTableModeInitialized = true;
        settings.MapDataSource = MapDataSource.LocalGame;
        settings.MapDataSourceInitialized = true;
        store.SaveSettings(settings);
    }

    private static void EnableMapDataManualTable(AppDataStore store)
    {
        AppSettings settings = store.CreateSettingsSnapshot();
        settings.MapDataTableMode = MapDataTableMode.Manual;
        settings.MapDataTableModeInitialized = true;
        store.SaveSettings(settings);
    }

    private static void WriteReadyToCommitMigrationState(string stateFilePath, string sourceDirectory, string targetDirectory)
    {
        WriteMigrationState(
            stateFilePath,
            sourceDirectory,
            targetDirectory,
            stage: "ReadyToCommit",
            copyTargetFiles: true,
            includeHashes: true);
    }

    private static void WriteCopyingMigrationState(string stateFilePath, string sourceDirectory, string targetDirectory)
    {
        WriteMigrationState(
            stateFilePath,
            sourceDirectory,
            targetDirectory,
            stage: "Copying",
            copyTargetFiles: false,
            includeHashes: false);
    }

    private static void WriteCommittedMigrationState(string stateFilePath, string sourceDirectory, string targetDirectory)
    {
        WriteMigrationState(
            stateFilePath,
            sourceDirectory,
            targetDirectory,
            stage: "Committed",
            copyTargetFiles: true,
            includeHashes: true);
    }

    private static void WriteMigrationState(
        string stateFilePath,
        string sourceDirectory,
        string targetDirectory,
        string stage,
        bool copyTargetFiles,
        bool includeHashes)
    {
        string sourceFullPath = NormalizeDirectoryPathForTest(sourceDirectory);
        string targetFullPath = NormalizeDirectoryPathForTest(targetDirectory);
        Directory.CreateDirectory(targetFullPath);

        List<string> directories = Directory.EnumerateDirectories(sourceFullPath, "*", SearchOption.AllDirectories)
            .Select(directory => Path.GetRelativePath(sourceFullPath, directory))
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (string relativeDirectory in directories)
        {
            Directory.CreateDirectory(Path.Combine(targetFullPath, relativeDirectory));
        }

        var files = Directory.EnumerateFiles(sourceFullPath, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(sourceFullPath, file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .Select(relativePath =>
            {
                string sourceFile = Path.Combine(sourceFullPath, relativePath);
                string targetFile = Path.Combine(targetFullPath, relativePath);
                string? targetFileDirectory = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetFileDirectory))
                {
                    Directory.CreateDirectory(targetFileDirectory);
                }

                if (copyTargetFiles)
                {
                    if (!string.Equals(
                        NormalizeDirectoryPathForTest(sourceFile),
                        NormalizeDirectoryPathForTest(targetFile),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(sourceFile, targetFile, overwrite: true);
                    }
                }

                return new
                {
                    RelativePath = relativePath,
                    Length = new FileInfo(sourceFile).Length,
                    Sha256 = includeHashes ? ComputeSha256ForTest(sourceFile) : string.Empty,
                    Copied = copyTargetFiles,
                    Verified = includeHashes,
                    DeletedFromSource = false
                };
            })
            .ToList();

        var state = new
        {
            Version = 1,
            Id = $"test-{stage.ToLowerInvariant()}",
            SourceDataDirectory = sourceFullPath,
            TargetDataDirectory = targetFullPath,
            Stage = stage,
            StartedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            CurrentOperation = "测试模拟迁移状态。",
            ErrorMessage = string.Empty,
            Directories = directories,
            Files = files
        };
        File.WriteAllText(stateFilePath, JsonSerializer.Serialize(state));
    }

    private static string NormalizeDirectoryPathForTest(string directory)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    private static string ComputeSha256ForTest(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void InvokeRecoverInterruptedDataDirectoryMigration(AppDataStore store)
    {
        typeof(AppDataStore)
            .GetMethod("RecoverInterruptedDataDirectoryMigration", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(store, null);
    }

    private static string WriteBackupMetadata(
        AppDataStore store,
        string id,
        string fileUserId,
        DateTime backupTime)
    {
        return WriteBackupMetadata(
            store,
            id,
            folderUserId: string.Empty,
            fileUserId,
            backupTime);
    }

    private static string WriteBackupMetadata(
        AppDataStore store,
        string id,
        string folderUserId,
        string fileUserId,
        DateTime backupTime)
    {
        string backupDirectory = Path.Combine(store.BackupsDirectory, id);
        Directory.CreateDirectory(backupDirectory);
        File.WriteAllText(
            Path.Combine(backupDirectory, "metadata.json"),
            JsonSerializer.Serialize(new BackupMetadata
            {
                Id = id,
                BackupTime = backupTime,
                FolderUserID = folderUserId,
                FileUserID = fileUserId,
                UseFolderUserIDAsEffectiveUserID = !string.IsNullOrWhiteSpace(folderUserId)
            }));
        return backupDirectory;
    }

    private void WriteMapDataCache(
        ushort mapId,
        string mapName,
        string version,
        DateTime? lastSuccessfulSyncAt = null,
        string sourcePath = "",
        MapDataSource source = MapDataSource.LocalGame,
        string sourceKey = "",
        string sourceFingerprint = "")
    {
        string dataDirectory = Path.Combine(testDirectory, "Data");
        Directory.CreateDirectory(Path.Combine(dataDirectory, "cache"));
        bool isLocalGameSource = source == MapDataSource.LocalGame;
        MapDataCache metadata = new()
        {
            Version = version,
            Source = string.IsNullOrWhiteSpace(sourceKey)
                ? isLocalGameSource ? "local-sqpack" : "online-reference:github"
                : sourceKey,
            SourcePath = string.IsNullOrWhiteSpace(sourcePath)
                ? isLocalGameSource ? string.Empty : MapDataOnlineReferenceCsvUrl
                : sourcePath,
            SourceFingerprint = sourceFingerprint,
            LastUpdated = DateTime.Now,
            LastSuccessfulSyncAt = lastSuccessfulSyncAt ?? DateTime.MinValue
        };
        string csv = MapDataTableCsv.Serialize(new Dictionary<ushort, string>
        {
            [mapId] = mapName
        });
        File.WriteAllText(Path.Combine(dataDirectory, "cache", "mapdata.csv"), csv);
        File.WriteAllText(Path.Combine(dataDirectory, "cache", "mapdata.meta.json"), JsonSerializer.Serialize(metadata));
    }

    private static MapDataCache ReadMapDataCacheMetadata(AppDataStore store)
    {
        string metadataText = File.ReadAllText(store.MapDataCacheMetadataFilePath);
        return JsonSerializer.Deserialize<MapDataCache>(metadataText)!;
    }

    private static Dictionary<ushort, string> ReadMapDataCacheCsv(AppDataStore store)
    {
        string csv = File.ReadAllText(store.MapDataCacheFilePath);
        return MapDataTableCsv.ParseSimpleMapDataCsv(csv);
    }

    private static void WriteUserMapDataCsv(AppDataStore store, ushort mapId, string mapName)
    {
        Directory.CreateDirectory(store.CacheDirectory);
        string csv = MapDataTableCsv.Serialize(new Dictionary<ushort, string>
        {
            [mapId] = mapName
        });
        File.WriteAllText(store.UserMapDataFilePath, csv);
    }

    private static void WriteRawUserMapDataCsv(AppDataStore store, string csv)
    {
        Directory.CreateDirectory(store.CacheDirectory);
        File.WriteAllText(store.UserMapDataFilePath, csv);
    }

    private static Dictionary<ushort, string> ReadUserMapDataCsv(AppDataStore store)
    {
        string csv = File.ReadAllText(store.UserMapDataFilePath);
        return MapDataTableCsv.ParseSimpleMapDataCsv(csv);
    }

    private void WriteServerCache(ServerListCache cache)
    {
        string dataDirectory = Path.Combine(testDirectory, "Data");
        Directory.CreateDirectory(Path.Combine(dataDirectory, "cache"));
        File.WriteAllText(Path.Combine(dataDirectory, "cache", "servers.json"), JsonSerializer.Serialize(cache));
    }

    private static WayMark CreateSampleWayMark(ushort regionId)
    {
        WayMark wayMark = new()
        {
            RegionID = regionId,
            timestamp = 123456,
            unknown = 7
        };
        wayMark.A.X = 1000;
        wayMark.A.Y = 2000;
        wayMark.A.Z = 3000;
        wayMark.B.X = 4000;
        wayMark.B.Y = 5000;
        wayMark.B.Z = 6000;
        wayMark.AEnabled = true;
        wayMark.BEnabled = true;
        return wayMark;
    }

    private static byte[] CreateMinimalUISaveFile(ushort regionId)
    {
        byte[] payload = BuildPayload(BuildFMarkerSection(regionId));
        byte[] encryptedPayload = payload.Select(value => (byte)(value ^ 0x31)).ToArray();

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 });
        writer.Write(encryptedPayload.Length);
        writer.Write(new byte[] { 0, 0, 0, 0 });
        writer.Write(encryptedPayload);
        return stream.ToArray();
    }

    private static byte[] BuildPayload(params byte[][] sections)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(new byte[] { 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17 });
        writer.Write(new byte[] { 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01 });
        foreach (byte[] section in sections)
        {
            writer.Write(section);
        }

        return stream.ToArray();
    }

    private static byte[] BuildFMarkerSection(ushort regionId)
    {
        byte[] markerData = BuildMarkerData(regionId);

        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write((short)17);
        writer.Write(new byte[] { 1, 2, 3, 4, 5, 6 });
        writer.Write(markerData.Length);
        writer.Write(new byte[] { 7, 8, 9, 10 });
        writer.Write(markerData);
        writer.Write(new byte[] { 0, 0, 0, 0 });
        return stream.ToArray();
    }

    private static byte[] BuildMarkerData(ushort regionId)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(new byte[16]);
        for (int pointIndex = 0; pointIndex < 8; pointIndex++)
        {
            writer.Write(1000 + pointIndex);
            writer.Write(2000 + pointIndex);
            writer.Write(3000 + pointIndex);
        }

        writer.Write((byte)0xFF);
        writer.Write((byte)0);
        writer.Write(regionId);
        writer.Write(123456);
        writer.Write(new byte[] { 0, 0, 0, 0 });
        return stream.ToArray();
    }

    private static string CreateUtf8DecodedAsGbk(string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936).GetString(Encoding.UTF8.GetBytes(value));
    }

    private static string CreateContentFinderConditionCsv(ushort rowId, string name)
    {
        return CreateContentFinderConditionCsv((rowId, rowId, name));
    }

    private static string CreateContentFinderConditionCsv(params (ushort RowId, ushort TerritoryType, string Name)[] rows)
    {
        List<string> lines =
        [
            "key,0,1,2,3,4,5",
            "#,ShortCode,TerritoryType,ContentLinkType,Content,Name,NameShort",
            "int32,str,TerritoryType,byte,Row,str,str"
        ];

        lines.AddRange(rows.Select(row => $"{row.RowId},\"test\",{row.TerritoryType},1,1,\"{row.Name}\",\"\""));
        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateDiemoeMapInstanceJson(ushort mapId, string name)
    {
        Dictionary<string, Dictionary<string, Dictionary<string, string>>> instances = new()
        {
            [mapId.ToString()] = new Dictionary<string, Dictionary<string, string>>
            {
                ["name"] = new Dictionary<string, string>
                {
                    ["chs"] = name
                }
            }
        };

        return JsonSerializer.Serialize(instances);
    }

    private static string CreateGitHubCommitApiResponse(
        string sha,
        string message,
        string commitDate = "2026-06-10T00:00:00Z")
    {
        return JsonSerializer.Serialize(new[]
        {
            new
            {
                sha,
                commit = new
                {
                    message,
                    committer = new
                    {
                        date = commitDate
                    }
                }
            }
        });
    }

    private static string CreateMapDataPinnedCsvUrl(string commitSha)
    {
        return $"https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/{commitSha}/ContentFinderCondition.csv";
    }

    private const string ServerStatusApiUrl = "https://ff14act.web.sdo.com/api/serverStatus/getServerStatus";
    private const string ServerListSourceUrl = "https://ff.web.sdo.com/web8/index.html#/servers";
    private const string GitHubMapDataCommitSha = "bdc3adfcecfa38772a26f98daaa9936d0dc40279";
    private const string MapDataOnlineReferenceCsvUrl = "https://raw.githubusercontent.com/thewakingsands/ffxiv-datamining-cn/master/ContentFinderCondition.csv";
    private const string MapDataOnlineReferenceCommitApiUrl = "https://api.github.com/repos/thewakingsands/ffxiv-datamining-cn/commits?path=ContentFinderCondition.csv&per_page=1";
    private const string MapDataDiemoeVersionUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/data.version";
    private const string MapDataDiemoeInstanceUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/instance.json";

    private sealed class FakeLocalGameMapDataProvider : ILocalGameMapDataProvider
    {
        private readonly Queue<FakeLocalGameMapDataResponse> responses = [];

        public List<string> Requests { get; } = [];
        public List<string> IdentityRequests { get; } = [];

        public void AddSnapshot(string version, string sourcePath, ushort mapId, string mapName, string sourceFingerprint = "")
        {
            AddSnapshot(version, sourcePath, new Dictionary<ushort, string>
            {
                [mapId] = mapName
            }, sourceFingerprint);
        }

        public void AddSnapshot(
            string version,
            string sourcePath,
            IReadOnlyDictionary<ushort, string> mapNames,
            string sourceFingerprint = "")
        {
            string fingerprint = CreateFakeLocalFingerprint(version, sourcePath, sourceFingerprint);
            responses.Enqueue(new FakeLocalGameMapDataResponse(
                new MapDataSnapshotIdentity(version, sourcePath, fingerprint),
                _ => new MapDataSnapshot(version, sourcePath, fingerprint, mapNames)));
        }

        public void AddIdentity(string version, string sourcePath, string sourceFingerprint = "")
        {
            string fingerprint = CreateFakeLocalFingerprint(version, sourcePath, sourceFingerprint);
            responses.Enqueue(new FakeLocalGameMapDataResponse(
                new MapDataSnapshotIdentity(version, sourcePath, fingerprint),
                _ => throw new InvalidOperationException("完整本地地图数据不应被读取。")));
        }

        public void AddException(Exception exception)
        {
            responses.Enqueue(new FakeLocalGameMapDataResponse(
                new MapDataSnapshotIdentity("probe-version", string.Empty, "sqpack-indexes:probe"),
                _ => throw exception));
        }

        public MapDataSnapshotIdentity GetSnapshotIdentity(string gameInstallDirectory)
        {
            IdentityRequests.Add(gameInstallDirectory);
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("没有配置本地地图数据响应。");
            }

            return responses.Peek().Identity;
        }

        public MapDataSnapshot LoadFromGameInstallDirectory(string gameInstallDirectory)
        {
            Requests.Add(gameInstallDirectory);
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("没有配置本地地图数据响应。");
            }

            return responses.Dequeue().Load(gameInstallDirectory);
        }

        private sealed record FakeLocalGameMapDataResponse(
            MapDataSnapshotIdentity Identity,
            Func<string, MapDataSnapshot> Load);

        private static string CreateFakeLocalFingerprint(string version, string sourcePath, string sourceFingerprint)
        {
            return string.IsNullOrWhiteSpace(sourceFingerprint)
                ? $"sqpack-indexes:{version}:{sourcePath}"
                : sourceFingerprint;
        }
    }

    private sealed class FakeAppDataNetworkClient : IAppDataNetworkClient
    {
        private readonly Dictionary<string, Queue<Func<Task<string>>>> responses = [];

        public List<FakeNetworkRequest> Requests { get; } = [];

        public void AddResponse(string url, string response)
        {
            Add(url, () => Task.FromResult(response));
        }

        public TaskCompletionSource<string> AddPendingResponse(string url)
        {
            TaskCompletionSource<string> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Add(url, () => response.Task);
            return response;
        }

        public void AddException(string url, Exception exception)
        {
            Add(url, () => throw exception);
        }

        public Task<string> GetStringAsync(
            string url,
            TimeSpan timeout,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Requests.Add(new FakeNetworkRequest(
                url,
                timeout,
                headers?.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

            if (!responses.TryGetValue(url, out Queue<Func<Task<string>>>? queue) || queue.Count == 0)
            {
                throw new InvalidOperationException($"未配置测试网络响应：{url}");
            }

            return queue.Dequeue().Invoke();
        }

        private void Add(string url, Func<Task<string>> responseFactory)
        {
            if (!responses.TryGetValue(url, out Queue<Func<Task<string>>>? queue))
            {
                queue = new Queue<Func<Task<string>>>();
                responses[url] = queue;
            }

            queue.Enqueue(responseFactory);
        }
    }

    private sealed record FakeNetworkRequest(
        string Url,
        TimeSpan Timeout,
        IReadOnlyDictionary<string, string> Headers);
}
