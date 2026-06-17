using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    private static readonly TimeSpan MapDataRequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<MapDataLoadResult> EnsureMapDataAvailableAsync()
    {
        return await LoadMapDataAsync(forceRefresh: false, allowCacheFallback: true);
    }

    public async Task<MapDataLoadResult> ForceRefreshMapDataAsync()
    {
        return await LoadMapDataAsync(forceRefresh: true, allowCacheFallback: false);
    }

    private async Task<MapDataLoadResult> LoadMapDataAsync(bool forceRefresh, bool allowCacheFallback)
    {
        DateTime successfulSyncTime = DateTime.Now;
        try
        {
            string remoteVersionContent = await networkClient.GetStringAsync(MapDataVersionUrl, MapDataRequestTimeout);
            string remoteVersion = ParseMapDataVersion(remoteVersionContent);
            if (!forceRefresh &&
                !string.IsNullOrWhiteSpace(remoteVersion) &&
                TryReadMapDataCache(out MapDataCache cachedMapData) &&
                string.Equals(remoteVersion, cachedMapData.Version, StringComparison.OrdinalIgnoreCase))
            {
                TryUpdateMapDataSuccessfulSyncTime(cachedMapData, successfulSyncTime);
                if (!ApplyMapDataCache(cachedMapData))
                {
                    return new MapDataLoadResult(false, false, remoteVersion);
                }

                return new MapDataLoadResult(true, false, remoteVersion, CacheAvailable: true);
            }

            string instanceJson = await networkClient.GetStringAsync(MapDataInstanceUrl, MapDataRequestTimeout);
            Dictionary<ushort, string> mapNames = ParseMapNamesFromInstanceJson(instanceJson);
            if (mapNames.Count == 0)
            {
                return allowCacheFallback
                    ? LoadMapDataCacheFallback(remoteVersion)
                    : new MapDataLoadResult(false, false, remoteVersion);
            }

            MapDataCache cache = CreateMapDataCache(remoteVersion, mapNames);
            cache.LastSuccessfulSyncAt = successfulSyncTime;
            WriteMapDataCache(cache);
            ApplyMapDataCache(cache);
            return new MapDataLoadResult(true, true, remoteVersion, CacheAvailable: true);
        }
        catch
        {
            return allowCacheFallback
                ? LoadMapDataCacheFallback(string.Empty)
                : new MapDataLoadResult(false, false, string.Empty);
        }
    }

    private MapDataLoadResult LoadMapDataCacheFallback(string version)
    {
        if (!LoadMapDataCache())
        {
            return new MapDataLoadResult(false, false, version);
        }

        string cacheVersion = string.IsNullOrWhiteSpace(MapDataVersion) ? version : MapDataVersion;
        return new MapDataLoadResult(true, false, cacheVersion, UsedCache: true, CacheAvailable: true);
    }

    private bool LoadMapDataCache()
    {
        if (!TryReadMapDataCache(out MapDataCache cache) || !ApplyMapDataCache(cache))
        {
            MapData.Clear();
            MapDataVersion = string.Empty;
            MapDataLastUpdated = DateTime.MinValue;
            MapDataLastSuccessfulSyncAt = DateTime.MinValue;
            return false;
        }

        return true;
    }

    private bool ApplyMapDataCache(MapDataCache cache)
    {
        Dictionary<ushort, string> mapNames = ParseMapNamesFromCache(cache);
        if (mapNames.Count == 0)
        {
            return false;
        }

        MapData.ApplyMapNames(mapNames);
        MapDataVersion = cache.Version;
        MapDataLastUpdated = cache.LastUpdated;
        MapDataLastSuccessfulSyncAt = cache.LastSuccessfulSyncAt;
        return true;
    }

    private bool TryReadMapDataCache(out MapDataCache cache)
    {
        JsonFileReadResult<MapDataCache> cacheResult = ReadJsonFile<MapDataCache>(MapDataCacheFilePath);
        if (cacheResult.Status == JsonFileReadStatus.Success && cacheResult.Value != null)
        {
            cache = cacheResult.Value;
            cache.Instances ??= [];
            return true;
        }

        if (cacheResult.Status == JsonFileReadStatus.Invalid)
        {
            AddJsonReadWarning(
                MapDataCacheFilePath,
                "地图缓存无法读取，已按无缓存处理。",
                cacheResult.Error);
        }

        cache = new MapDataCache();
        return false;
    }

    private void WriteMapDataCache(MapDataCache cache)
    {
        WriteJson(MapDataCacheFilePath, cache);
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

    private static MapDataCache CreateMapDataCache(string version, IReadOnlyDictionary<ushort, string> mapNames)
    {
        return new MapDataCache
        {
            Version = version,
            LastUpdated = DateTime.Now,
            Instances = mapNames
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .OrderBy(pair => pair.Key)
                .ToDictionary(pair => pair.Key.ToString(), pair => pair.Value)
        };
    }

    private static Dictionary<ushort, string> ParseMapNamesFromCache(MapDataCache cache)
    {
        Dictionary<ushort, string> mapNames = [];
        foreach (KeyValuePair<string, string> pair in cache.Instances)
        {
            if (!ushort.TryParse(pair.Key, out ushort mapId)) continue;
            if (string.IsNullOrWhiteSpace(pair.Value)) continue;

            mapNames[mapId] = pair.Value;
        }

        return mapNames;
    }

    private static string ParseMapDataVersion(string versionContent)
    {
        foreach (string line in versionContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0) continue;

            string key = line[..equalsIndex].Trim();
            if (!string.Equals(key, "build_version", StringComparison.OrdinalIgnoreCase)) continue;

            return line[(equalsIndex + 1)..].Trim();
        }

        return versionContent.Trim();
    }

    private static Dictionary<ushort, string> ParseMapNamesFromInstanceJson(string json)
    {
        Dictionary<ushort, string> mapNames = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return mapNames;

            foreach (JsonProperty instanceProperty in document.RootElement.EnumerateObject())
            {
                if (!ushort.TryParse(instanceProperty.Name, out ushort mapId)) continue;
                if (!TryGetChineseInstanceName(instanceProperty.Value, out string mapName)) continue;

                mapNames[mapId] = mapName;
            }
        }
        catch (JsonException)
        {
            return [];
        }

        return mapNames;
    }

    private static bool TryGetChineseInstanceName(JsonElement instanceElement, out string mapName)
    {
        mapName = string.Empty;
        if (instanceElement.ValueKind != JsonValueKind.Object ||
            !instanceElement.TryGetProperty("name", out JsonElement nameElement))
        {
            return false;
        }

        string? parsedName = null;
        if (nameElement.ValueKind == JsonValueKind.Object &&
            nameElement.TryGetProperty("chs", out JsonElement chsElement))
        {
            parsedName = chsElement.GetString();
        }
        else if (nameElement.ValueKind == JsonValueKind.String)
        {
            parsedName = nameElement.GetString();
        }

        mapName = parsedName?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(mapName);
    }

}
