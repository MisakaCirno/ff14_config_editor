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
        try
        {
            string remoteVersionContent = await networkClient.GetStringAsync(MapDataVersionUrl, MapDataRequestTimeout);
            string remoteVersion = ParseMapDataVersion(remoteVersionContent);
            string localVersion = ReadMapDataVersion();
            if (!forceRefresh &&
                File.Exists(MapDataInstanceFilePath) &&
                !string.IsNullOrWhiteSpace(remoteVersion) &&
                string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase))
            {
                if (!LoadMapDataCache())
                {
                    return new MapDataLoadResult(false, false, remoteVersion);
                }

                MapDataVersion = remoteVersion;
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

            WriteText(MapDataInstanceFilePath, instanceJson);
            WriteText(MapDataVersionFilePath, string.IsNullOrWhiteSpace(remoteVersion) ? remoteVersionContent : remoteVersion);
            MapData.ApplyMapNames(mapNames);
            MapDataVersion = remoteVersion;
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
        string? instanceJson = ReadText(MapDataInstanceFilePath);
        if (string.IsNullOrWhiteSpace(instanceJson))
        {
            MapData.Clear();
            return false;
        }

        Dictionary<ushort, string> mapNames = ParseMapNamesFromInstanceJson(instanceJson);
        if (mapNames.Count == 0)
        {
            MapData.Clear();
            return false;
        }

        MapData.ApplyMapNames(mapNames);
        MapDataVersion = ReadMapDataVersion();
        return true;
    }

    private string ReadMapDataVersion()
    {
        string? versionContent = ReadText(MapDataVersionFilePath);
        return string.IsNullOrWhiteSpace(versionContent) ? string.Empty : ParseMapDataVersion(versionContent);
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
