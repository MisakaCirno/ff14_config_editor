using System.IO;
using System.Text;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    private const string LocalSqpackMapDataSource = "local-sqpack";
    private const string UserCsvMapDataSource = "user-csv";
    private const string MapDataSourceChangedDuringLoadReason = "地图数据来源已在刷新过程中变更，本次刷新结果已丢弃。";
    private const string DefaultUserMapDataCsv =
        "1,监狱废墟托托·拉克千狱\r\n" +
        "2,地下灵殿塔姆·塔拉墓园\r\n" +
        "3,封锁坑道铜铃铜山\r\n" +
        "4,天然要害沙斯塔夏溶洞\r\n" +
        "5,毒雾洞窟黄金谷\r\n" +
        "6,名门府邸静语庄园\r\n" +
        "7,魔兽领域日影地修炼所\r\n" +
        "8,休养胜地布雷福洛克斯野营地\r\n" +
        "9,古代遗迹喀恩埋没圣堂\r\n" +
        "10,神灵圣域放浪神古神殿\r\n";
    private static readonly Encoding MapDataCsvEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly TimeSpan MapDataRequestTimeout = TimeSpan.FromSeconds(20);
    private const int MapDataMetadataMaxResponseBytes = 1024 * 1024;
    private const int MapDataContentMaxResponseBytes = 16 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> GitHubApiHeaders = new Dictionary<string, string>
    {
        ["Accept"] = "application/vnd.github+json",
        ["User-Agent"] = "FFXIVConfigEditor",
        ["X-GitHub-Api-Version"] = "2022-11-28"
    };

    private sealed record MapDataOnlineSourceDefinition(
        MapDataOnlineSourceKind Kind,
        string DisplayName,
        string DisplayDetail,
        string ContentUrl,
        string VersionUrl = "");

    public async Task<MapDataLoadResult> EnsureMapDataAvailableAsync()
    {
        return await LoadMapDataAsync(forceRefresh: false, CancellationToken.None);
    }

    internal async Task<MapDataLoadResult> EnsureMapDataAvailableAsync(CancellationToken cancellationToken)
    {
        return await LoadMapDataAsync(forceRefresh: false, cancellationToken);
    }

    public async Task<MapDataLoadResult> ForceRefreshMapDataAsync()
    {
        return await LoadMapDataAsync(forceRefresh: true, CancellationToken.None);
    }

    private async Task<MapDataLoadResult> LoadMapDataAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (Settings.MapDataTableMode == MapDataTableMode.Manual)
        {
            return await Task.Run(() => LoadMapDataFromUserCsv(forceRefresh)).WaitAsync(cancellationToken);
        }

        if (Settings.MapDataSource == MapDataSource.LocalGame)
        {
            return await Task.Run(() => LoadMapDataFromLocalGame(forceRefresh)).WaitAsync(cancellationToken);
        }

        return await LoadMapDataFromOnlineReferenceAsync(forceRefresh, cancellationToken);
    }

    private MapDataLoadResult LoadMapDataFromUserCsv(bool forceRefresh)
    {
        const string currentStage = "读取手动地图数据";
        try
        {
            if (!File.Exists(UserMapDataFilePath))
            {
                ExecuteDataDirectoryManagedWrite(() =>
                {
                    Directory.CreateDirectory(CacheDirectory);
                    SafeFileWriter.WriteAllText(
                        UserMapDataFilePath,
                        DefaultUserMapDataCsv,
                        MapDataCsvEncoding);
                });
            }

            FileInfo userMapDataFileInfo = new(UserMapDataFilePath);
            DateTime userMapDataLastWriteTime = userMapDataFileInfo.LastWriteTime;
            string userMapDataFingerprint = CreateFileMetadataFingerprint(UserCsvMapDataSource, userMapDataFileInfo);
            string userMapDataVersionPrefix = $"{MapDataSourceParsers.FormatSnapshotTimestamp(userMapDataLastWriteTime)}-";
            string csv = File.ReadAllText(UserMapDataFilePath, MapDataCsvEncoding);
            MapDataTableCsvDiagnosticResult diagnosticResult = MapDataTableCsv.DiagnoseSimpleMapDataCsv(csv);
            if (diagnosticResult.HasIssues)
            {
                return LoadUserMapDataRepairFallback(
                    currentStage,
                    BuildUserMapDataDiagnosticFailureReason(diagnosticResult));
            }

            Dictionary<ushort, string> mapNames = new(diagnosticResult.MapNames);
            if (mapNames.Count == 0)
            {
                return LoadUserMapDataRepairFallback(
                    currentStage,
                    $"手动地图数据文件为空或格式不受支持。请填写至少一行 ID 和名称后重新读取：{UserMapDataFilePath}");
            }

            if (!forceRefresh && TryUseCachedMapDataSnapshot(
                UserCsvMapDataSource,
                cache =>
                    cache.Version.StartsWith(userMapDataVersionPrefix, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(cache.SourceFingerprint, userMapDataFingerprint, StringComparison.OrdinalIgnoreCase),
                UserMapDataFilePath,
                DateTime.Now,
                currentStage,
                "手动地图数据缓存为空或格式不受支持。",
                out MapDataLoadResult? cachedUserResult))
            {
                return cachedUserResult!;
            }

            string version = MapDataSourceParsers.CreateTimestampedContentHashVersion(userMapDataLastWriteTime, csv);
            MapDataCache userTable = CreateMapDataCache(
                version,
                mapNames,
                UserMapDataFilePath,
                UserCsvMapDataSource,
                userMapDataFingerprint);
            userTable.LastUpdated = userMapDataLastWriteTime;
            userTable.LastSuccessfulSyncAt = DateTime.Now;
            if (TryCreateMapDataSourceChangedResult(
                UserCsvMapDataSource,
                userTable.Version,
                "应用手动地图数据",
                out MapDataLoadResult? sourceChangedResult))
            {
                return sourceChangedResult!;
            }

            if (TryReadMapDataCache(UserCsvMapDataSource, out MapDataCache cachedUserTable) &&
                IsSameMapDataSnapshot(cachedUserTable, userTable))
            {
                cachedUserTable.SourcePath = UserMapDataFilePath;
                TryUpdateMapDataSuccessfulSyncTime(cachedUserTable, userTable.LastSuccessfulSyncAt);
                if (!ApplyMapDataCache(cachedUserTable))
                {
                    return CreateMapDataFailureResult(
                        version,
                        currentStage,
                        "手动地图数据缓存为空或格式不受支持。");
                }

                return new MapDataLoadResult(
                    true,
                    false,
                    cachedUserTable.Version,
                    CacheAvailable: true,
                    SourcePath: MapDataSourcePath);
            }

            if (IsSameMapDataContent(cachedUserTable, userTable))
            {
                WriteMapDataCache(userTable);
                if (!ApplyMapDataCache(userTable))
                {
                    return CreateMapDataFailureResult(
                        userTable.Version,
                        "应用手动地图数据",
                        MapDataSourceChangedDuringLoadReason);
                }

                return new MapDataLoadResult(
                    true,
                    false,
                    userTable.Version,
                    CacheAvailable: true,
                    SourcePath: userTable.SourcePath);
            }

            bool updated = !IsCurrentMapDataTableSameAs(userTable);
            WriteMapDataCache(userTable);
            if (!ApplyMapDataCache(userTable))
            {
                return CreateMapDataFailureResult(
                    userTable.Version,
                    "应用手动地图数据",
                    MapDataSourceChangedDuringLoadReason);
            }

            return new MapDataLoadResult(
                true,
                updated,
                userTable.Version,
                CacheAvailable: true,
                SourcePath: userTable.SourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or AppDataStoreException)
        {
            return LoadMapDataCacheFallback(
                UserCsvMapDataSource,
                string.Empty,
                currentStage,
                FormatDataSyncFailureReason(ex));
        }
    }

    private MapDataLoadResult LoadUserMapDataRepairFallback(string failureStage, string failureReason)
    {
        return LoadMapDataCacheFallback(
            UserCsvMapDataSource,
            string.Empty,
            failureStage,
            failureReason) with
        {
            SourcePath = UserMapDataFilePath,
            RequiresUserMapDataRepair = true
        };
    }

    private static string BuildUserMapDataDiagnosticFailureReason(MapDataTableCsvDiagnosticResult diagnosticResult)
    {
        IEnumerable<string> messages = diagnosticResult.Issues
            .OrderBy(static issue => issue.RowNumber)
            .ThenByDescending(static issue => issue.Severity)
            .Take(5)
            .Select(static issue => $"第 {issue.RowNumber} 行：{issue.Message}");
        string summary = string.Join("；", messages);
        if (diagnosticResult.Issues.Count > 5)
        {
            summary += $"；另有 {diagnosticResult.Issues.Count - 5} 个问题";
        }

        return $"手动地图数据文件存在需要修复的问题：{summary}";
    }

    private async Task<MapDataLoadResult> LoadMapDataFromOnlineReferenceAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        DateTime successfulSyncTime = DateTime.Now;
        List<string> sourceFailures = [];
        MapDataOnlineSourceDefinition source = CreateMapDataOnlineSourceDefinition(Settings.MapDataOnlineSource);
        string cacheSource = CreateOnlineReferenceMapDataSource(source.Kind);

        MapDataLoadResult? result = source.Kind switch
        {
            MapDataOnlineSourceKind.DiemoeMatcha => await TryLoadDiemoeMapDataAsync(
                source,
                forceRefresh,
                successfulSyncTime,
                sourceFailures,
                cancellationToken),
            _ => await TryLoadContentFinderConditionMapDataAsync(
                source,
                forceRefresh,
                successfulSyncTime,
                sourceFailures,
                cancellationToken)
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
        bool forceRefresh,
        DateTime successfulSyncTime,
        List<string> sourceFailures,
        CancellationToken cancellationToken)
    {
        GitHubFileCommitInfo? commitInfo = await TryLoadContentFinderConditionCommitInfoAsync(source, sourceFailures, cancellationToken);
        if (commitInfo != null)
        {
            string pinnedSourceUrl = ExternalLinks.CreateMapDataOnlineReferenceCsvUrl(commitInfo.Sha);
            string sourceFingerprint = CreateSourceFingerprint("github", commitInfo.Sha.ToLowerInvariant());
            if (!forceRefresh && TryUseCachedMapDataSnapshot(
                CreateOnlineReferenceMapDataSource(source.Kind),
                commitInfo.Version,
                pinnedSourceUrl,
                sourceFingerprint,
                successfulSyncTime,
                "读取在线地图数据缓存",
                "在线地图数据缓存为空或格式不受支持。",
                out MapDataLoadResult? cachedResult))
            {
                return cachedResult!;
            }

            MapDataLoadResult? pinnedResult = await TryLoadContentFinderConditionCsvSnapshotAsync(
                source,
                pinnedSourceUrl,
                commitInfo.Version,
                sourceFingerprint,
                successfulSyncTime,
                sourceFailures,
                cancellationToken);
            if (pinnedResult != null)
            {
                return pinnedResult;
            }
        }

        return await TryLoadContentFinderConditionCsvSnapshotAsync(
            source,
            source.ContentUrl,
            string.Empty,
            string.Empty,
            successfulSyncTime,
            sourceFailures,
            cancellationToken);
    }

    private async Task<GitHubFileCommitInfo?> TryLoadContentFinderConditionCommitInfoAsync(
        MapDataOnlineSourceDefinition source,
        List<string> sourceFailures,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.VersionUrl))
        {
            return null;
        }

        string commitJson;
        try
        {
            commitJson = await networkClient.GetStringAsync(
                source.VersionUrl,
                MapDataRequestTimeout,
                MapDataMetadataMaxResponseBytes,
                GitHubApiHeaders,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.VersionUrl}）：版本查询失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }

        try
        {
            GitHubFileCommitInfo? commitInfo = MapDataSourceParsers.ParseGitHubLatestFileCommitInfo(commitJson);
            if (commitInfo == null)
            {
                sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.VersionUrl}）：版本解析失败，GitHub 响应中没有可用的文件提交信息。");
            }

            return commitInfo;
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.VersionUrl}）：版本解析失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }
    }

    private async Task<MapDataLoadResult?> TryLoadContentFinderConditionCsvSnapshotAsync(
        MapDataOnlineSourceDefinition source,
        string sourceUrl,
        string version,
        string sourceFingerprint,
        DateTime successfulSyncTime,
        List<string> sourceFailures,
        CancellationToken cancellationToken)
    {
        string csv;
        try
        {
            csv = await networkClient.GetStringAsync(
                sourceUrl,
                MapDataRequestTimeout,
                MapDataContentMaxResponseBytes,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

        string snapshotVersion = string.IsNullOrWhiteSpace(version)
            ? MapDataSourceParsers.CreateContentHashVersion(csv)
            : version;
        return TryApplyOnlineMapDataSnapshot(
            source,
            CreateOnlineReferenceMapDataSource(source.Kind),
            snapshotVersion,
            string.IsNullOrWhiteSpace(sourceFingerprint) ? snapshotVersion : sourceFingerprint,
            mapNames,
            sourceUrl,
            successfulSyncTime);
    }

    private async Task<MapDataLoadResult?> TryLoadDiemoeMapDataAsync(
        MapDataOnlineSourceDefinition source,
        bool forceRefresh,
        DateTime successfulSyncTime,
        List<string> sourceFailures,
        CancellationToken cancellationToken)
    {
        string versionContent;
        try
        {
            versionContent = await networkClient.GetStringAsync(
                source.VersionUrl,
                MapDataRequestTimeout,
                MapDataMetadataMaxResponseBytes,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sourceFailures.Add($"{FormatMapDataOnlineSourceName(source)}（{source.VersionUrl}）：版本下载失败，{FormatDataSyncFailureReason(ex)}");
            return null;
        }

        string version = MapDataSourceParsers.ParseDiemoeVersion(versionContent);
        string sourceFingerprint = string.IsNullOrWhiteSpace(version)
            ? string.Empty
            : CreateSourceFingerprint("diemoe", version);
        if (!forceRefresh &&
            !string.IsNullOrWhiteSpace(version) &&
            TryUseCachedMapDataSnapshot(
                CreateOnlineReferenceMapDataSource(source.Kind),
                version,
                source.ContentUrl,
                sourceFingerprint,
                successfulSyncTime,
                "读取在线地图数据缓存",
                "在线地图数据缓存为空或格式不受支持。",
                out MapDataLoadResult? cachedResult))
        {
            return cachedResult!;
        }

        string instanceJson;
        try
        {
            instanceJson = await networkClient.GetStringAsync(
                source.ContentUrl,
                MapDataRequestTimeout,
                MapDataContentMaxResponseBytes,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

        if (string.IsNullOrWhiteSpace(version))
        {
            version = MapDataSourceParsers.CreateContentHashVersion(instanceJson);
            sourceFingerprint = version;
        }

        return TryApplyOnlineMapDataSnapshot(
            source,
            CreateOnlineReferenceMapDataSource(source.Kind),
            version,
            sourceFingerprint,
            mapNames,
            source.ContentUrl,
            successfulSyncTime);
    }

    private MapDataLoadResult? TryApplyOnlineMapDataSnapshot(
        MapDataOnlineSourceDefinition source,
        string cacheSource,
        string version,
        string sourceFingerprint,
        IReadOnlyDictionary<ushort, string> mapNames,
        string sourcePath,
        DateTime successfulSyncTime)
    {
        bool hasFallbackCache = TryReadMapDataCache(cacheSource, out MapDataCache fallbackCache);
        try
        {
            return ApplyOnlineMapDataSnapshot(
                cacheSource,
                version,
                sourceFingerprint,
                mapNames,
                sourcePath,
                successfulSyncTime);
        }
        catch (Exception ex) when (IsRecoverableMapDataSnapshotApplyException(ex))
        {
            string failureReason = $"{FormatMapDataOnlineSourceName(source)}（{sourcePath}）：应用失败，{FormatDataSyncFailureReason(ex)}";
            return CreateOnlineMapDataApplyFailureResult(cacheSource, version, failureReason, hasFallbackCache, fallbackCache);
        }
    }

    private MapDataLoadResult ApplyOnlineMapDataSnapshot(
        string cacheSource,
        string version,
        string sourceFingerprint,
        IReadOnlyDictionary<ushort, string> mapNames,
        string sourcePath,
        DateTime successfulSyncTime)
    {
        if (TryCreateMapDataSourceChangedResult(
            cacheSource,
            version,
            "应用在线地图数据",
            out MapDataLoadResult? sourceChangedResult))
        {
            return sourceChangedResult!;
        }

        MapDataCache nextCache = CreateMapDataCache(
            version,
            mapNames,
            sourcePath,
            cacheSource,
            sourceFingerprint);
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
                    "应用在线地图数据",
                    MapDataSourceChangedDuringLoadReason);
            }

            return new MapDataLoadResult(
                true,
                false,
                cachedMapData.Version,
                CacheAvailable: true,
                SourcePath: MapDataSourcePath);
        }

        if (IsSameMapDataContent(cachedMapData, nextCache))
        {
            WriteMapDataCache(nextCache);
            if (!ApplyMapDataCache(nextCache))
            {
                return CreateMapDataFailureResult(
                    nextCache.Version,
                    "应用在线地图数据",
                    MapDataSourceChangedDuringLoadReason);
            }

            return new MapDataLoadResult(
                true,
                false,
                nextCache.Version,
                CacheAvailable: true,
                SourcePath: nextCache.SourcePath);
        }

        WriteMapDataCache(nextCache);
        if (!ApplyMapDataCache(nextCache))
        {
            return CreateMapDataFailureResult(
                nextCache.Version,
                "应用在线地图数据",
                MapDataSourceChangedDuringLoadReason);
        }

        return new MapDataLoadResult(
            true,
            true,
            nextCache.Version,
            CacheAvailable: true,
            SourcePath: nextCache.SourcePath);
    }

    private MapDataLoadResult CreateOnlineMapDataApplyFailureResult(
        string cacheSource,
        string version,
        string failureReason,
        bool hasFallbackCache,
        MapDataCache fallbackCache)
    {
        const string failureStage = "应用在线地图数据";
        if (TryCreateMapDataSourceChangedResult(
            cacheSource,
            version,
            failureStage,
            out MapDataLoadResult? sourceChangedResult))
        {
            return sourceChangedResult!;
        }

        if (hasFallbackCache && ApplyMapDataCache(fallbackCache))
        {
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

        return CreateMapDataFailureResult(version, failureStage, failureReason);
    }

    private static bool IsRecoverableMapDataSnapshotApplyException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or AppDataStoreException;
    }

    private MapDataLoadResult LoadMapDataFromLocalGame(bool forceRefresh)
    {
        DateTime successfulSyncTime = DateTime.Now;
        string currentStage = "定位本地游戏数据";
        try
        {
            string gameInstallDirectory = ResolveMapDataGameInstallDirectory();
            currentStage = "检查本地游戏地图数据";
            MapDataSnapshotIdentity snapshotIdentity = localGameMapDataProvider.GetSnapshotIdentity(gameInstallDirectory);
            if (!forceRefresh &&
                !string.Equals(snapshotIdentity.Version, "unknown", StringComparison.OrdinalIgnoreCase) &&
                TryUseCachedMapDataSnapshot(
                    LocalSqpackMapDataSource,
                    snapshotIdentity.Version,
                    snapshotIdentity.SourcePath,
                    snapshotIdentity.SourceFingerprint,
                    successfulSyncTime,
                    "读取本地地图数据缓存",
                    "本地地图数据缓存为空或格式不受支持。",
                    out MapDataLoadResult? cachedResult))
            {
                return cachedResult!;
            }

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
                LocalSqpackMapDataSource,
                snapshot.SourceFingerprint);
            nextCache.LastSuccessfulSyncAt = successfulSyncTime;
            if (TryCreateMapDataSourceChangedResult(
                LocalSqpackMapDataSource,
                nextCache.Version,
                "应用本地地图数据",
                out MapDataLoadResult? sourceChangedResult))
            {
                return sourceChangedResult!;
            }

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

            if (IsSameMapDataContent(cachedMapData, nextCache))
            {
                WriteMapDataCache(nextCache);
                if (!ApplyMapDataCache(nextCache))
                {
                    return CreateMapDataFailureResult(
                        nextCache.Version,
                        "应用本地地图数据",
                        MapDataSourceChangedDuringLoadReason);
                }

                return new MapDataLoadResult(
                    true,
                    false,
                    nextCache.Version,
                    CacheAvailable: true,
                    SourcePath: nextCache.SourcePath);
            }

            currentStage = "保存本地地图数据缓存";
            WriteMapDataCache(nextCache);
            currentStage = "应用本地地图数据";
            if (!ApplyMapDataCache(nextCache))
            {
                return CreateMapDataFailureResult(
                    nextCache.Version,
                    currentStage,
                    MapDataSourceChangedDuringLoadReason);
            }

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
        if (TryCreateMapDataSourceChangedResult(
            expectedSource,
            version,
            failureStage,
            out MapDataLoadResult? sourceChangedResult))
        {
            return sourceChangedResult!;
        }

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

    private bool TryUseCachedMapDataSnapshot(
        string expectedSource,
        string version,
        string sourcePath,
        string sourceFingerprint,
        DateTime successfulSyncTime,
        string failureStage,
        string invalidCacheReason,
        out MapDataLoadResult? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return TryUseCachedMapDataSnapshot(
            expectedSource,
            cache =>
                string.Equals(cache.Version, version, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(sourceFingerprint) ||
                    string.Equals(cache.SourceFingerprint, sourceFingerprint, StringComparison.OrdinalIgnoreCase)),
            sourcePath,
            successfulSyncTime,
            failureStage,
            invalidCacheReason,
            out result);
    }

    private bool TryUseCachedMapDataSnapshot(
        string expectedSource,
        Func<MapDataCache, bool> matchesCache,
        string sourcePath,
        DateTime successfulSyncTime,
        string failureStage,
        string invalidCacheReason,
        out MapDataLoadResult? result)
    {
        result = null;
        if (!TryReadMapDataCache(expectedSource, out MapDataCache cache) ||
            !matchesCache(cache))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            cache.SourcePath = sourcePath;
        }

        if (TryCreateMapDataSourceChangedResult(
            expectedSource,
            cache.Version,
            failureStage,
            out MapDataLoadResult? sourceChangedResult))
        {
            result = sourceChangedResult;
            return true;
        }

        TryUpdateMapDataSuccessfulSyncTime(cache, successfulSyncTime);
        if (!ApplyMapDataCache(cache))
        {
            result = CreateMapDataFailureResult(cache.Version, failureStage, MapDataSourceChangedDuringLoadReason);
            return true;
        }

        result = new MapDataLoadResult(
            true,
            false,
            cache.Version,
            CacheAvailable: true,
            SourcePath: MapDataSourcePath);
        return true;
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
            _ = LoadMapDataFromUserCsv(forceRefresh: false);
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
        if (!IsCurrentMapDataCacheSource(cache.Source))
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
        cache.SourceFingerprint ??= string.Empty;
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
            ExecuteDataDirectoryManagedWrite(() =>
            {
                string csv = MapDataTableCsv.Serialize(cache.MapNames);
                SafeFileWriter.WriteAllText(MapDataCacheFilePath, csv, MapDataCsvEncoding);
                WriteJson(MapDataCacheMetadataFilePath, cache);
            });
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
        string source,
        string sourceFingerprint = "")
    {
        return new MapDataCache
        {
            Version = version,
            Source = source,
            SourcePath = sourcePath,
            SourceFingerprint = sourceFingerprint,
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

        if (!string.Equals(left.SourceFingerprint, right.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
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

    private static bool IsSameMapDataContent(MapDataCache left, MapDataCache right)
    {
        if (!string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase))
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

    private static string CreateFileMetadataFingerprint(string source, FileInfo fileInfo)
    {
        return FormattableString.Invariant($"{source}:{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}");
    }

    private static string CreateSourceFingerprint(string source, string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"{source}:{value.Trim()}";
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
                ExternalLinks.MapDataOnlineReferenceCsv,
                ExternalLinks.MapDataOnlineReferenceCommitApi);
    }

    private static string FormatMapDataOnlineSourceName(MapDataOnlineSourceDefinition source)
    {
        return source.DisplayName;
    }

    private string GetCurrentMapDataCacheSource()
    {
        if (Settings.MapDataTableMode == MapDataTableMode.Manual)
        {
            return UserCsvMapDataSource;
        }

        return Settings.MapDataSource == MapDataSource.LocalGame
            ? LocalSqpackMapDataSource
            : CreateOnlineReferenceMapDataSource(Settings.MapDataOnlineSource);
    }

    private bool TryCreateMapDataSourceChangedResult(
        string expectedSource,
        string version,
        string failureStage,
        out MapDataLoadResult? result)
    {
        result = null;
        if (IsCurrentMapDataCacheSource(expectedSource))
        {
            return false;
        }

        result = CreateMapDataFailureResult(
            version,
            failureStage,
            MapDataSourceChangedDuringLoadReason);
        return true;
    }

    private bool IsCurrentMapDataCacheSource(string expectedSource)
    {
        return string.Equals(GetCurrentMapDataCacheSource(), expectedSource, StringComparison.OrdinalIgnoreCase);
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
