using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
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
            LastSyncAttempt = DateTime.MinValue,
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

    private AppDataStore CreateStore()
    {
        return new AppDataStore(testDirectory);
    }
}
