using System.IO;
using System.Text;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    private const string LocalSqpackMapDataSource = "local-sqpack";
    private static readonly Encoding MapDataCsvEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly TimeSpan MapDataRequestTimeout = TimeSpan.FromSeconds(20);

    private sealed record MapDataOnlineSourceDefinition(
        MapDataOnlineSourceKind Kind,
        string DisplayName,
        string DisplayDetail,
        string ContentUrl,
        string VersionUrl = "");

    public async Task<MapDataLoadResult> EnsureMapDataAvailableAsync()
    {
        return await LoadMapDataAsync();
    }

    public async Task<MapDataLoadResult> ForceRefreshMapDataAsync()
    {
        return await LoadMapDataAsync();
    }

    private async Task<MapDataLoadResult> LoadMapDataAsync()
    {
        if (Settings.MapDataTableMode == MapDataTableMode.Manual)
        {
            return await Task.Run(LoadMapDataFromUserCsv);
        }

        if (Settings.MapDataSource == MapDataSource.LocalGame)
        {
            return await Task.Run(LoadMapDataFromLocalGame);
        }

        return await LoadMapDataFromOnlineReferenceAsync();
    }

    private MapDataLoadResult LoadMapDataFromUserCsv()
    {
        const string currentStage = "读取手动地图数据";
        try
        {
            if (!File.Exists(UserMapDataFilePath))
            {
                Directory.CreateDirectory(CacheDirectory);
                SafeFileWriter.WriteAllText(
                    UserMapDataFilePath,
                    MapDataTableCsv.Serialize(new Dictionary<ushort, string>()),
                    MapDataCsvEncoding);
                ClearMapDataCacheState();
                return CreateMapDataFailureResult(
                    "user-csv",
                    currentStage,
                    $"手动地图数据文件不存在，已创建空白模板。请填写后重新读取：{UserMapDataFilePath}");
            }

            string csv = File.ReadAllText(UserMapDataFilePath, MapDataCsvEncoding);
            Dictionary<ushort, string> mapNames = MapDataTableCsv.ParseSimpleMapDataCsv(csv);
            if (mapNames.Count == 0)
            {
                ClearMapDataCacheState();
                return CreateMapDataFailureResult(
                    "user-csv",
                    currentStage,
                    $"手动地图数据文件为空或格式不受支持。请填写至少一行 ID 和名称后重新读取：{UserMapDataFilePath}");
            }

            string version = MapDataSourceParsers.CreateContentHashVersion("user", csv);
            MapDataCache userTable = CreateMapDataCache(
                version,
                mapNames,
                UserMapDataFilePath,
                "user-csv");
            userTable.LastUpdated = File.GetLastWriteTime(UserMapDataFilePath);
            userTable.LastSuccessfulSyncAt = DateTime.Now;
            bool updated = !IsCurrentMapDataTableSameAs(userTable);
            ApplyMapDataTable(userTable);
            return new MapDataLoadResult(
                true,
                updated,
                userTable.Version,
                CacheAvailable: true,
                SourcePath: userTable.SourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or AppDataStoreException)
        {
            ClearMapDataCacheState();
            return CreateMapDataFailureResult("user-csv", currentStage, FormatDataSyncFailureReason(ex));
        }
    }

    private async Task<MapDataLoadResult> LoadMapDataFromOnlineReferenceAsync()
    {
        DateTime successfulSyncTime = DateTime.Now;
        List<string> sourceFailures = [];
        MapDataOnlineSourceDefinition source = CreateMapDataOnlineSourceDefinition(Settings.MapDataOnlineSource);
        string cacheSource = CreateOnlineReferenceMapDataSource(source.Kind);

        MapDataLoadResult? result = source.Kind switch
        {
            MapDataOnlineSourceKind.DiemoeMatcha => await TryLoadDiemoeMapDataAsync(source, successfulSyncTime, sourceFailures),
            _ => await TryLoadContentFinderConditionMapDataAsync(source, successfulSyncTime, sourceFailures)
        };
        if (result != null)
        {
            return result;
        }

        return LoadMapDataCacheFallback(
            cacheSource,
            string.Empty,
            "获取在线地图参考数据",
            FormatOnlineMapDataSourceFailures(sourceFailures));
    }

    private async Task<MapDataLoadResult?> TryLoadContentFinderConditionMapDataAsync(
        MapDataOnlineSourceDefinition source,
        DateTime successfulSyncTime,
        List<string> sourceFailures)
    {
        string sourceUrl = source.ContentUrl;
        string csv;
        try
        {
            csv = await networkClient.GetStringAsync(sourceUrl, MapDataRequestTimeout);
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{sourceUrl}）：下载失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }

        Dictionary<ushort, string> mapNames;
        try
        {
            mapNames = MapDataSourceParsers.ParseContentFinderConditionMapNames(csv);
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{sourceUrl}）：解析失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }

        if (mapNames.Count == 0)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{sourceUrl}）：解析失败，在线地图参考数据为空或格式不受支持。");
            return null;
        }

        string version = MapDataSourceParsers.CreateContentHashVersion("online", csv);
        return ApplyOnlineMapDataSnapshot(version, mapNames, sourceUrl, successfulSyncTime);
    }

    private async Task<MapDataLoadResult?> TryLoadDiemoeMapDataAsync(
        MapDataOnlineSourceDefinition source,
        DateTime successfulSyncTime,
        List<string> sourceFailures)
    {
        string versionContent;
        try
        {
            versionContent = await networkClient.GetStringAsync(source.VersionUrl, MapDataRequestTimeout);
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.VersionUrl}）：版本下载失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }

        string instanceJson;
        try
        {
            instanceJson = await networkClient.GetStringAsync(source.ContentUrl, MapDataRequestTimeout);
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.ContentUrl}）：数据下载失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }

        Dictionary<ushort, string> mapNames;
        try
        {
            mapNames = MapDataSourceParsers.ParseDiemoeMapNamesFromInstanceJson(instanceJson);
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.ContentUrl}）：数据解析失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }

        if (mapNames.Count == 0)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.ContentUrl}）：数据解析失败，在线地图参考数据为空或格式不受支持。");
            return null;
        }

        string version = MapDataSourceParsers.ParseDiemoeVersion(versionContent);
        if (string.IsNullOrWhiteSpace(version))
        {
            version = MapDataSourceParsers.CreateContentHashVersion("diemoe", instanceJson);
        }

        return ApplyOnlineMapDataSnapshot(version, mapNames, source.ContentUrl, successfulSyncTime);
    }

    private MapDataLoadResult ApplyOnlineMapDataSnapshot(
        string version,
        IReadOnlyDictionary<ushort, string> mapNames,
        string sourcePath,
        DateTime successfulSyncTime)
    {
        string cacheSource = CreateOnlineReferenceMapDataSource(Settings.MapDataOnlineSource);
        MapDataCache nextCache = CreateMapDataCache(
            version,
            mapNames,
            sourcePath,
            cacheSource);
        nextCache.LastSuccessfulSyncAt = successfulSyncTime;
        if (TryReadMapDataCache(cacheSource, out MapDataCache cachedMapData) &&
            IsSameMapDataSnapshot(cachedMapData, nextCache))
        {
            cachedMapData.SourcePath = sourcePath;
            TryUpdateMapDataSuccessfulSyncTime(cachedMapData, successfulSyncTime);
            if (!ApplyMapDataCache(cachedMapData))
            {
                return CreateMapDataFailureResult(
                    version,
                    "读取在线地图数据缓存",
                    "在线地图数据缓存为空或格式不受支持。");
            }

            return new MapDataLoadResult(
                true,
                false,
                cachedMapData.Version,
                CacheAvailable: true,
                SourcePath: MapDataSourcePath);
        }

        WriteMapDataCache(nextCache);
        ApplyMapDataCache(nextCache);
        return new MapDataLoadResult(
            true,
            true,
            nextCache.Version,
            CacheAvailable: true,
            SourcePath: nextCache.SourcePath);
    }

    private MapDataLoadResult LoadMapDataFromLocalGame()
    {
        DateTime successfulSyncTime = DateTime.Now;
        string currentStage = "定位本地游戏数据";
        try
        {
            string gameInstallDirectory = ResolveMapDataGameInstallDirectory();
            currentStage = "解析本地游戏地图数据";
            MapDataSnapshot snapshot = localGameMapDataProvider.LoadFromGameInstallDirectory(gameInstallDirectory);
            if (snapshot.MapNames.Count == 0)
            {
                return LoadMapDataCacheFallback(
                    LocalSqpackMapDataSource,
                    string.Empty,
                    currentStage,
                    "本地游戏地图数据为空或格式不受支持。");
            }

            currentStage = "读取本地地图数据缓存";
            MapDataCache nextCache = CreateMapDataCache(
                snapshot.Version,
                snapshot.MapNames,
                snapshot.SourcePath,
                LocalSqpackMapDataSource);
            nextCache.LastSuccessfulSyncAt = successfulSyncTime;
            if (TryReadMapDataCache(LocalSqpackMapDataSource, out MapDataCache cachedMapData) &&
                IsSameMapDataSnapshot(cachedMapData, nextCache))
            {
                TryUpdateMapDataSuccessfulSyncTime(cachedMapData, successfulSyncTime);
                if (!ApplyMapDataCache(cachedMapData))
                {
                    return CreateMapDataFailureResult(
                        snapshot.Version,
                        currentStage,
                        "本地地图数据缓存为空或格式不受支持。");
                }

                return new MapDataLoadResult(
                    true,
                    false,
                    cachedMapData.Version,
                    CacheAvailable: true,
                    SourcePath: MapDataSourcePath);
            }

            currentStage = "保存本地地图数据缓存";
            WriteMapDataCache(nextCache);
            currentStage = "应用本地地图数据";
            ApplyMapDataCache(nextCache);
            return new MapDataLoadResult(
                true,
                true,
                nextCache.Version,
                CacheAvailable: true,
                SourcePath: nextCache.SourcePath);
        }
        catch (Exception ex)
        {
            string failureReason = FormatDataSyncFailureReason(ex);
            return LoadMapDataCacheFallback(LocalSqpackMapDataSource, string.Empty, currentStage, failureReason);
        }
    }

    private string ResolveMapDataGameInstallDirectory()
    {
        if (WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            Settings.GameInstallDirectory,
            out string? configuredGameInstallDirectory))
        {
            return configuredGameInstallDirectory;
        }

        string? detectedGameInstallDirectory = gameInstallDirectoryDetector();
        if (WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            detectedGameInstallDirectory,
            out string? normalizedDetectedGameInstallDirectory))
        {
            return normalizedDetectedGameInstallDirectory;
        }

        throw new InvalidOperationException("未找到有效的游戏安装目录。请在设置中填写游戏安装目录，或先启动游戏后重新检查。");
    }

    private MapDataLoadResult LoadMapDataCacheFallback(
        string expectedSource,
        string version,
        string failureStage = "",
        string failureReason = "")
    {
        if (!LoadMapDataCache(expectedSource))
        {
            return CreateMapDataFailureResult(version, failureStage, failureReason);
        }

        string cacheVersion = string.IsNullOrWhiteSpace(MapDataVersion) ? version : MapDataVersion;
        return new MapDataLoadResult(
            true,
            false,
            cacheVersion,
            UsedCache: true,
            CacheAvailable: true,
            FailureStage: failureStage,
            FailureReason: failureReason,
            SourcePath: MapDataSourcePath);
    }

    private static MapDataLoadResult CreateMapDataFailureResult(
        string version,
        string failureStage,
        string failureReason)
    {
        return new MapDataLoadResult(
            false,
            false,
            version,
            FailureStage: string.IsNullOrWhiteSpace(failureStage) ? "加载地图名称数据" : failureStage,
            FailureReason: string.IsNullOrWhiteSpace(failureReason) ? "未知原因。" : failureReason);
    }

    private bool LoadMapDataCache(string expectedSource)
    {
        if (!TryReadMapDataCache(expectedSource, out MapDataCache cache) || !ApplyMapDataCache(cache))
        {
            ClearMapDataCacheState();
            return false;
        }

        return true;
    }

    private void LoadMapDataCacheForCurrentSource()
    {
        if (Settings.MapDataTableMode == MapDataTableMode.Manual)
        {
            _ = LoadMapDataFromUserCsv();
            return;
        }

        LoadMapDataCache(GetCurrentMapDataCacheSource());
    }

    private void ClearMapDataCacheState()
    {
        MapData.Clear();
        MapDataVersion = string.Empty;
        MapDataSourcePath = string.Empty;
        MapDataLastUpdated = DateTime.MinValue;
        MapDataLastSuccessfulSyncAt = DateTime.MinValue;
    }

    private bool ApplyMapDataCache(MapDataCache cache)
    {
        if (!IsExpectedMapDataCacheSource(cache, GetCurrentMapDataCacheSource()))
        {
            return false;
        }

        if (cache.MapNames.Count == 0)
        {
            return false;
        }

        ApplyMapDataTable(cache);
        return true;
    }

    private void ApplyMapDataTable(MapDataCache cache)
    {
        MapData.ApplyMapNames(cache.MapNames);
        MapDataVersion = cache.Version;
        MapDataSourcePath = cache.SourcePath;
        MapDataLastUpdated = cache.LastUpdated;
        MapDataLastSuccessfulSyncAt = cache.LastSuccessfulSyncAt;
    }

    private bool TryReadMapDataCache(string expectedSource, out MapDataCache cache)
    {
        JsonFileReadResult<MapDataCache> metadataResult = ReadJsonFile<MapDataCache>(MapDataCacheMetadataFilePath);
        if (metadataResult.Status == JsonFileReadStatus.Missing && !File.Exists(MapDataCacheFilePath))
        {
            cache = new MapDataCache();
            return false;
        }

        if (metadataResult.Status == JsonFileReadStatus.Invalid)
        {
            AddJsonReadWarning(
                MapDataCacheMetadataFilePath,
                "地图缓存元数据无法读取，已按无缓存处理。",
                metadataResult.Error);
            cache = new MapDataCache();
            return false;
        }

        cache = metadataResult.Value ?? new MapDataCache();
        cache.Source ??= string.Empty;
        cache.SourcePath ??= string.Empty;
        if (metadataResult.Status == JsonFileReadStatus.Missing)
        {
            cache.Source = expectedSource;
            cache.SourcePath = MapDataCacheFilePath;
            cache.Version = "legacy-csv";
            cache.LastUpdated = File.GetLastWriteTime(MapDataCacheFilePath);
        }

        if (!IsExpectedMapDataCacheSource(cache, expectedSource))
        {
            cache = new MapDataCache();
            return false;
        }

        if (!TryReadMapDataCacheCsv(out Dictionary<ushort, string> mapNames))
        {
            cache = new MapDataCache();
            return false;
        }

        cache.MapNames = mapNames;
        return true;
    }

    private void WriteMapDataCache(MapDataCache cache)
    {
        try
        {
            string csv = MapDataTableCsv.Serialize(cache.MapNames);
            SafeFileWriter.WriteAllText(MapDataCacheFilePath, csv, MapDataCsvEncoding);
            WriteJson(MapDataCacheMetadataFilePath, cache);
        }
        catch (Exception ex)
        {
            throw new AppDataStoreException("写入地图数据缓存", MapDataCacheFilePath, ex);
        }
    }

    private void TryUpdateMapDataSuccessfulSyncTime(MapDataCache cache, DateTime successfulSyncTime)
    {
        cache.LastSuccessfulSyncAt = successfulSyncTime;
        MapDataLastSuccessfulSyncAt = successfulSyncTime;
        try
        {
            WriteMapDataCache(cache);
        }
        catch (AppDataStoreException ex)
        {
            AppLogger.Warning(AppLogCategory.IO, "保存地图数据成功检查时间失败", ex);
        }
    }

    private static MapDataCache CreateMapDataCache(
        string version,
        IReadOnlyDictionary<ushort, string> mapNames,
        string sourcePath,
        string source)
    {
        return new MapDataCache
        {
            Version = version,
            Source = source,
            SourcePath = sourcePath,
            LastUpdated = DateTime.Now,
            MapNames = mapNames
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Trim())
        };
    }

    private bool TryReadMapDataCacheCsv(out Dictionary<ushort, string> mapNames)
    {
        mapNames = [];
        if (!File.Exists(MapDataCacheFilePath))
        {
            return false;
        }

        try
        {
            string csv = File.ReadAllText(MapDataCacheFilePath, MapDataCsvEncoding);
            mapNames = MapDataTableCsv.ParseSimpleMapDataCsv(csv);
            if (mapNames.Count == 0)
            {
                AddDataLoadWarning(
                    $"mapdata-csv-empty:{Path.GetFullPath(MapDataCacheFilePath)}",
                    $"地图缓存 CSV 为空或格式不受支持，已按无缓存处理。{Environment.NewLine}文件：{MapDataCacheFilePath}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AddDataLoadWarning(
                $"mapdata-csv-invalid:{Path.GetFullPath(MapDataCacheFilePath)}",
                $"地图缓存 CSV 无法读取，已按无缓存处理。{Environment.NewLine}文件：{MapDataCacheFilePath}{Environment.NewLine}原因：{ex.Message}");
            return false;
        }
    }

    private static bool IsSameMapDataSnapshot(MapDataCache left, MapDataCache right)
    {
        if (!string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (left.MapNames.Count != right.MapNames.Count)
        {
            return false;
        }

        foreach (KeyValuePair<ushort, string> pair in right.MapNames)
        {
            if (!left.MapNames.TryGetValue(pair.Key, out string? leftName) ||
                !string.Equals(leftName, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsCurrentMapDataTableSameAs(MapDataCache next)
    {
        if (!string.Equals(MapDataVersion, next.Version, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IReadOnlySet<ushort> currentMapIds = MapData.GetKnownMapIds();
        if (currentMapIds.Count != next.MapNames.Count)
        {
            return false;
        }

        foreach (KeyValuePair<ushort, string> pair in next.MapNames)
        {
            if (!currentMapIds.Contains(pair.Key) ||
                !string.Equals(MapData.GetName(pair.Key), pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatOnlineMapDataSourceFailures(IReadOnlyList<string> sourceFailures)
    {
        return sourceFailures.Count == 0
            ? "没有配置可用的在线地图参考数据来源。"
            : string.Join(Environment.NewLine, sourceFailures);
    }

    private static MapDataOnlineSourceDefinition CreateMapDataOnlineSourceDefinition(MapDataOnlineSourceKind kind)
    {
        return kind == MapDataOnlineSourceKind.DiemoeMatcha
            ? new MapDataOnlineSourceDefinition(
                kind,
                "diemoe MatchaData",
                "固定组合：data.version + instance.json",
                ExternalLinks.MapDataDiemoeInstance,
                ExternalLinks.MapDataDiemoeVersion)
            : new MapDataOnlineSourceDefinition(
                MapDataOnlineSourceKind.ContentFinderConditionCsv,
                "GitHub ffxiv-datamining-cn",
                "ContentFinderCondition.csv",
                ExternalLinks.MapDataOnlineReferenceCsv);
    }

    private static string FormatMapDataOnlineSourceName(MapDataOnlineSourceDefinition source)
    {
        return source.DisplayName;
    }

    private string GetCurrentMapDataCacheSource()
    {
        return Settings.MapDataSource == MapDataSource.LocalGame
            ? LocalSqpackMapDataSource
            : CreateOnlineReferenceMapDataSource(Settings.MapDataOnlineSource);
    }

    private static string CreateOnlineReferenceMapDataSource(MapDataOnlineSourceKind source)
    {
        return source == MapDataOnlineSourceKind.DiemoeMatcha
            ? "online-reference:diemoe"
            : "online-reference:github";
    }

    private static bool IsExpectedMapDataCacheSource(MapDataCache cache, string expectedSource)
    {
        return string.Equals(cache.Source, expectedSource, StringComparison.OrdinalIgnoreCase);
    }
}
