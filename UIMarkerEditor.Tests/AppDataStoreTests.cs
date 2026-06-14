using System;
using System.Collections.Generic;
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
        Assert.Contains("\"123\"", File.ReadAllText(store.MapDataInstanceFilePath));
        Assert.Equal("20260614", File.ReadAllText(store.MapDataVersionFilePath));
    }

    [Fact]
    public async Task EnsureMapDataAvailableAsync_WhenNetworkFailsAndCacheExists_UsesCache()
    {
        string instanceJson = CreateMapInstanceJson(456, "缓存副本");
        WriteMapDataCache(instanceJson, "build_version=cache-version");
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
    }

    [Fact]
    public async Task ForceRefreshMapDataAsync_WhenNetworkFails_DoesNotUseCache()
    {
        WriteMapDataCache(CreateMapInstanceJson(789, "缓存副本"), "build_version=cache-version");
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
        Assert.Contains("测试服务器", File.ReadAllText(store.ServersFilePath));
        Assert.Contains(networkClient.Requests, request =>
            request.Url == ServerStatusApiUrl &&
            request.Headers.ContainsKey("X-Requested-With"));
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
            LastSyncAttempt = originalAttempt,
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
        Assert.True(store.ServerList.LastSyncAttempt > originalAttempt);
        Assert.Equal("缓存大区", store.ServerList.Groups[0].DataCenter);

        string savedCacheText = File.ReadAllText(store.ServersFilePath);
        ServerListCache savedCache = JsonSerializer.Deserialize<ServerListCache>(savedCacheText)!;
        Assert.True(savedCache.LastSyncAttempt > originalAttempt);
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
    public async Task TrySyncServerListAsync_WhenNetworkFailsAndCacheExists_ReturnsFalseAndRecordsAttempt()
    {
        DateTime originalAttempt = DateTime.MinValue;
        WriteServerCache(new ServerListCache
        {
            SourceUrl = "测试缓存",
            LastUpdated = DateTime.Now,
            LastSyncAttempt = originalAttempt,
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
        Assert.True(store.ServerList.LastSyncAttempt > originalAttempt);
        Assert.Equal("缓存大区", store.ServerList.Groups[0].DataCenter);
    }

    private AppDataStore CreateStore()
    {
        return new AppDataStore(testDirectory);
    }

    private AppDataStore CreateStore(IAppDataNetworkClient networkClient)
    {
        return new AppDataStore(testDirectory, networkClient);
    }

    private void WriteMapDataCache(string instanceJson, string versionContent)
    {
        string dataDirectory = Path.Combine(testDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "instance.json"), instanceJson);
        File.WriteAllText(Path.Combine(dataDirectory, "mapdata.version"), versionContent);
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
