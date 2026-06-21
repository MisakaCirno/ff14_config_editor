using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        Assert.True(File.Exists(store.SettingsFilePath));
        Assert.True(File.Exists(store.CharactersFilePath));
        Assert.True(Directory.Exists(store.BackupsDirectory));
        Assert.False(store.Settings.AutoBackupAfterLoad);
        Assert.False(store.Settings.WayMarkGameCharacterRootDirectoryAutoDetectAttempted);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(string.Empty, store.Settings.WayMarkGameCharacterRootDirectory);
    }

    [Fact]
    public void Initialize_WhenSettingsJsonInvalid_DoesNotOverwriteCorruptedFileAndBlocksSave()
    {
        string settingsPath = Path.Combine(testDirectory, "Data", "config.json");
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
        string charactersPath = Path.Combine(testDirectory, "Data", "characters.json");
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
    }

    [Fact]
    public async Task AutoDetectWayMarkGameCharacterRootDirectoryAsync_WhenDetected_PersistsDirectoryAndKeepsDefaultMode()
    {
        string detectedDirectory = Path.Combine(testDirectory, "Game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        Directory.CreateDirectory(detectedDirectory);
        int detectCount = 0;
        AppDataStore store = CreateStore(() =>
        {
            detectCount++;
            return detectedDirectory;
        });
        store.Initialize();

        bool updatedDirectory = await store.AutoDetectWayMarkGameCharacterRootDirectoryAsync();

        Assert.True(updatedDirectory);
        Assert.Equal(1, detectCount);
        Assert.True(store.Settings.WayMarkGameCharacterRootDirectoryAutoDetectAttempted);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(Path.GetFullPath(detectedDirectory), store.Settings.WayMarkGameCharacterRootDirectory);

        AppDataStore reloadedStore = CreateStore(() =>
        {
            detectCount++;
            return Path.Combine(testDirectory, "Other");
        });
        reloadedStore.Initialize();
        bool reloadedUpdatedDirectory = await reloadedStore.AutoDetectWayMarkGameCharacterRootDirectoryAsync();

        Assert.False(reloadedUpdatedDirectory);
        Assert.Equal(1, detectCount);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(Path.GetFullPath(detectedDirectory), reloadedStore.Settings.WayMarkGameCharacterRootDirectory);
    }

    [Fact]
    public async Task AutoDetectWayMarkGameCharacterRootDirectoryAsync_WhenNotDetected_PersistsAttemptAndDoesNotRetry()
    {
        int detectCount = 0;
        AppDataStore store = CreateStore(() =>
        {
            detectCount++;
            return null;
        });
        store.Initialize();

        bool updatedDirectory = await store.AutoDetectWayMarkGameCharacterRootDirectoryAsync();

        Assert.False(updatedDirectory);
        Assert.Equal(1, detectCount);
        Assert.True(store.Settings.WayMarkGameCharacterRootDirectoryAutoDetectAttempted);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, store.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(string.Empty, store.Settings.WayMarkGameCharacterRootDirectory);

        AppDataStore reloadedStore = CreateStore(() =>
        {
            detectCount++;
            return Path.Combine(testDirectory, "Game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        });
        reloadedStore.Initialize();
        bool reloadedUpdatedDirectory = await reloadedStore.AutoDetectWayMarkGameCharacterRootDirectoryAsync();

        Assert.False(reloadedUpdatedDirectory);
        Assert.Equal(1, detectCount);
        Assert.Equal(WayMarkOpenDirectoryMode.Default, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(string.Empty, reloadedStore.Settings.WayMarkGameCharacterRootDirectory);
    }

    [Fact]
    public async Task AutoDetectWayMarkGameCharacterRootDirectoryAsync_DoesNotOverwriteExistingDirectoryOrMode()
    {
        string manualDirectory = Path.Combine(testDirectory, "Manual", "FINAL FANTASY XIV - A Realm Reborn");
        string detectedDirectory = Path.Combine(testDirectory, "Game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        AppDataStore store = CreateStore(() => detectedDirectory);
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.GameCharacterDirectory,
            WayMarkGameCharacterRootDirectory = manualDirectory,
            WayMarkGameCharacterRootDirectoryAutoDetectAttempted = false
        });

        bool updatedDirectory = await store.AutoDetectWayMarkGameCharacterRootDirectoryAsync();

        Assert.False(updatedDirectory);
        Assert.True(store.Settings.WayMarkGameCharacterRootDirectoryAutoDetectAttempted);
        Assert.Equal(WayMarkOpenDirectoryMode.GameCharacterDirectory, store.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(manualDirectory, store.Settings.WayMarkGameCharacterRootDirectory);
    }

    [Fact]
    public void SaveSettings_PersistsStartupAndAppearanceSettings()
    {
        AppDataStore store = CreateStore();
        store.Initialize();

        store.SaveSettings(new AppSettings
        {
            UseWayMarkImageLabels = false,
            StartupWayMarkAction = StartupWayMarkAction.LoadMostRecentFile,
            WayMarkFavoriteSaveMode = WayMarkFavoriteSaveMode.Auto,
            WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.Default,
            WayMarkGameCharacterRootDirectory = Path.Combine(testDirectory, "Game", "My Games", "FINAL FANTASY XIV - A Realm Reborn"),
            WayMarkGameCharacterRootDirectoryAutoDetectAttempted = true,
            AutoBackupAfterLoad = true,
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
        Assert.Equal(WayMarkOpenDirectoryMode.Default, reloadedStore.Settings.WayMarkOpenDirectoryMode);
        Assert.Equal(Path.GetFullPath(Path.Combine(testDirectory, "Game", "My Games", "FINAL FANTASY XIV - A Realm Reborn")), reloadedStore.Settings.WayMarkGameCharacterRootDirectory);
        Assert.True(reloadedStore.Settings.WayMarkGameCharacterRootDirectoryAutoDetectAttempted);
        Assert.True(reloadedStore.Settings.AutoBackupAfterLoad);
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
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataVersionUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        store.SaveSettings(new AppSettings
        {
            MaxBackupCount = 37,
            RecentFiles = [Path.Combine(testDirectory, "old.dat")]
        });
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
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "config.json"), "{ 损坏的 JSON");
        File.Delete(store.BootstrapFilePath);
        Directory.CreateDirectory(store.BootstrapFilePath);

        Assert.Throws<AppDataStoreException>(() =>
            store.ChangeDataDirectory(targetDirectory, migrateExistingData: false));

        Assert.Equal(oldDataDirectory, store.DataDirectory);
        Assert.Equal(oldSettingsFilePath, store.SettingsFilePath);
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
    public void ChangeDataDirectory_WhenMigratingData_LoadsMigratedMapCache()
    {
        DateTime mapSuccessfulSyncAt = new(2026, 6, 18, 10, 20, 0);
        AppDataStore store = CreateStore();
        store.Initialize();
        WriteMapDataCache(901, "迁移地图", "migrated-map-version", mapSuccessfulSyncAt);
        string targetDirectory = Path.Combine(testDirectory, "MigratedData");

        store.ChangeDataDirectory(targetDirectory, migrateExistingData: true);

        Assert.Equal(Path.GetFullPath(targetDirectory), store.DataDirectory);
        Assert.True(File.Exists(Path.Combine(targetDirectory, "mapdata.json")));
        Assert.Equal("migrated-map-version", store.MapDataVersion);
        Assert.Equal(mapSuccessfulSyncAt, store.MapDataLastSuccessfulSyncAt);
        Assert.Equal("迁移地图", MapData.GetName(901));
    }

    [Fact]
    public async Task ChangeDataDirectory_WhenNotMigratingData_ClearsMapCacheState()
    {
        DateTime mapSuccessfulSyncAt = new(2026, 6, 18, 10, 40, 0);
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataVersionUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();
        WriteMapDataCache(902, "旧目录地图", "old-directory-map-version", mapSuccessfulSyncAt);
        MapDataLoadResult mapDataResult = await store.EnsureMapDataAvailableAsync();
        Assert.True(mapDataResult.Success);
        Assert.Equal("旧目录地图", MapData.GetName(902));

        string targetDirectory = Path.Combine(testDirectory, "EmptyData");
        store.ChangeDataDirectory(targetDirectory, migrateExistingData: false);

        Assert.Equal(Path.GetFullPath(targetDirectory), store.DataDirectory);
        Assert.Equal(string.Empty, store.MapDataVersion);
        Assert.Equal(DateTime.MinValue, store.MapDataLastUpdated);
        Assert.Equal(DateTime.MinValue, store.MapDataLastSuccessfulSyncAt);
        Assert.False(MapData.HasData);
        Assert.False(File.Exists(Path.Combine(targetDirectory, "mapdata.json")));
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

        BackupMetadata backup = store.CreateBackup(sourceFilePath, cleanupAfterCreate: false);

        Assert.True(File.Exists(backup.BackupFilePath));
        byte[] backupBytes = File.ReadAllBytes(backup.BackupFilePath);
        Assert.Equal(sourceBytes, backupBytes);
        Assert.Equal(backupBytes.Length, backup.SourceFileSize);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(backupBytes)), backup.SourceFileSha256);
        BackupMarkerSnapshot snapshot = Assert.Single(backup.MarkerSnapshots);
        Assert.Equal(123, snapshot.RegionID);
        Assert.Equal(1, snapshot.SlotIndex);
        Assert.Equal(8, snapshot.EnabledPointCount);
    }

    [Fact]
    public void AddRecentFile_WhenSettingsJsonInvalid_DoesNotThrowOrOverwriteCorruptedFile()
    {
        string settingsPath = Path.Combine(testDirectory, "Data", "config.json");
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
        string serversPath = Path.Combine(testDirectory, "Data", "servers.json");
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
    public async Task EnsureMapDataAvailableAsync_WhenNetworkSucceeds_WritesCacheAndAppliesMapData()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataVersionUrl, "build_version=20260614");
        networkClient.AddResponse(MapDataInstanceUrl, CreateMapInstanceJson(123, "测试副本"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.True(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("20260614", result.Version);
        Assert.Equal("测试副本", MapData.GetName(123));
        string cacheText = File.ReadAllText(store.MapDataCacheFilePath);
        MapDataCache cache = JsonSerializer.Deserialize<MapDataCache>(cacheText)!;
        Assert.Equal("20260614", cache.Version);
        Assert.Equal("测试副本", cache.Instances["123"]);
        Assert.True(cache.LastUpdated > DateTime.MinValue);
        Assert.True(cache.LastSuccessfulSyncAt > DateTime.MinValue);
        Assert.True(store.MapDataLastUpdated > DateTime.MinValue);
        Assert.True(store.MapDataLastSuccessfulSyncAt > DateTime.MinValue);
        Assert.False(File.Exists(Path.Combine(store.DataDirectory, "instance.json")));
        Assert.False(File.Exists(Path.Combine(store.DataDirectory, "mapdata.version")));
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenNetworkFailsAndCacheExists_UsesCache()
    {
        DateTime successfulSyncAt = new(2026, 6, 18, 9, 0, 0);
        WriteMapDataCache(456, "缓存副本", "cache-version", successfulSyncAt);
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataVersionUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.True(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("cache-version", result.Version);
        Assert.Equal("缓存副本", MapData.GetName(456));
        Assert.Equal(successfulSyncAt, store.MapDataLastSuccessfulSyncAt);

        string savedCacheText = File.ReadAllText(store.MapDataCacheFilePath);
        MapDataCache savedCache = JsonSerializer.Deserialize<MapDataCache>(savedCacheText)!;
        Assert.Equal(successfulSyncAt, savedCache.LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenRemoteVersionMatchesCache_UsesCacheWithoutDownloadingInstances()
    {
        WriteMapDataCache(567, "同版本缓存副本", "same-version");
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataVersionUrl, "build_version=same-version");
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("same-version", result.Version);
        Assert.Equal("同版本缓存副本", MapData.GetName(567));
        Assert.DoesNotContain(networkClient.Requests, request => request.Url == MapDataInstanceUrl);
        string savedCacheText = File.ReadAllText(store.MapDataCacheFilePath);
        MapDataCache savedCache = JsonSerializer.Deserialize<MapDataCache>(savedCacheText)!;
        Assert.True(savedCache.LastSuccessfulSyncAt > DateTime.MinValue);
        Assert.True(store.MapDataLastSuccessfulSyncAt > DateTime.MinValue);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenRemoteVersionMatchesCache_ReturnsLatestWithoutDownloadingInstances()
    {
        DateTime originalSuccessfulSyncAt = new(2026, 6, 18, 9, 30, 0);
        WriteMapDataCache(568, "同版本手动缓存副本", "same-version", originalSuccessfulSyncAt);
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataVersionUrl, "build_version=same-version");
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.ForceRefreshMapDataAsync();

        Assert.True(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.True(result.CacheAvailable);
        Assert.Equal("same-version", result.Version);
        Assert.Equal("同版本手动缓存副本", MapData.GetName(568));
        Assert.DoesNotContain(networkClient.Requests, request => request.Url == MapDataInstanceUrl);
        Assert.True(store.MapDataLastSuccessfulSyncAt > originalSuccessfulSyncAt);

        string savedCacheText = File.ReadAllText(store.MapDataCacheFilePath);
        MapDataCache savedCache = JsonSerializer.Deserialize<MapDataCache>(savedCacheText)!;
        Assert.True(savedCache.LastSuccessfulSyncAt > originalSuccessfulSyncAt);
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenNetworkFailsAndNoCache_ReturnsFailure()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataVersionUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.EnsureMapDataAvailableAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.False(result.CacheAvailable);
        Assert.False(MapData.HasData);
        Assert.Equal(DateTime.MinValue, store.MapDataLastSuccessfulSyncAt);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenInstanceJsonHasNoMaps_DoesNotRecordSuccessfulSyncTime()
    {
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddResponse(MapDataVersionUrl, "build_version=empty-instance");
        networkClient.AddResponse(MapDataInstanceUrl, "{}");
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.ForceRefreshMapDataAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.False(result.CacheAvailable);
        Assert.Equal(DateTime.MinValue, store.MapDataLastSuccessfulSyncAt);
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenNetworkFails_DoesNotUseCache()
    {
        WriteMapDataCache(789, "缓存副本", "cache-version");
        FakeAppDataNetworkClient networkClient = new();
        networkClient.AddException(MapDataVersionUrl, new InvalidOperationException("模拟网络失败"));
        AppDataStore store = CreateStore(networkClient);
        store.Initialize();

        MapDataLoadResult result = await store.ForceRefreshMapDataAsync();

        Assert.False(result.Success);
        Assert.False(result.Updated);
        Assert.False(result.UsedCache);
        Assert.False(result.CacheAvailable);
        Assert.False(MapData.HasData);
        Assert.Equal(DateTime.MinValue, store.MapDataLastSuccessfulSyncAt);
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
        string favoritesPath = Path.Combine(testDirectory, "Data", "waymark-favorites.json");
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

    private AppDataStore CreateStore()
    {
        return CreateStore(() => null);
    }

    private AppDataStore CreateStore(Func<string?> wayMarkGameCharacterRootDirectoryDetector)
    {
        return new AppDataStore(testDirectory, wayMarkGameCharacterRootDirectoryDetector);
    }

    private AppDataStore CreateStore(IAppDataNetworkClient networkClient)
    {
        return new AppDataStore(testDirectory, networkClient, () => null);
    }

    private void WriteMapDataCache(
        ushort mapId,
        string mapName,
        string version,
        DateTime? lastSuccessfulSyncAt = null)
    {
        string dataDirectory = Path.Combine(testDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        MapDataCache cache = new()
        {
            Version = version,
            LastUpdated = DateTime.Now,
            LastSuccessfulSyncAt = lastSuccessfulSyncAt ?? DateTime.MinValue,
            Instances = new Dictionary<string, string>
            {
                [mapId.ToString()] = mapName
            }
        };
        File.WriteAllText(Path.Combine(dataDirectory, "mapdata.json"), JsonSerializer.Serialize(cache));
    }

    private void WriteServerCache(ServerListCache cache)
    {
        string dataDirectory = Path.Combine(testDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "servers.json"), JsonSerializer.Serialize(cache));
    }

    private static string CreateMapInstanceJson(ushort mapId, string mapName)
    {
        return $$"""
            {
              "{{mapId}}": {
                "name": {
                  "chs": "{{mapName}}"
                }
              }
            }
            """;
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

    private const string MapDataVersionUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/data.version";
    private const string MapDataInstanceUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/instance.json";
    private const string ServerStatusApiUrl = "https://ff14act.web.sdo.com/api/serverStatus/getServerStatus";
    private const string ServerListSourceUrl = "https://ff.web.sdo.com/web8/index.html#/servers";

    private sealed class FakeAppDataNetworkClient : IAppDataNetworkClient
    {
        private readonly Dictionary<string, Queue<Func<string>>> responses = [];

        public List<FakeNetworkRequest> Requests { get; } = [];

        public void AddResponse(string url, string response)
        {
            Add(url, () => response);
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

            if (!responses.TryGetValue(url, out Queue<Func<string>>? queue) || queue.Count == 0)
            {
                throw new InvalidOperationException($"未配置测试网络响应：{url}");
            }

            return Task.FromResult(queue.Dequeue().Invoke());
        }

        private void Add(string url, Func<string> responseFactory)
        {
            if (!responses.TryGetValue(url, out Queue<Func<string>>? queue))
            {
                queue = new Queue<Func<string>>();
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
