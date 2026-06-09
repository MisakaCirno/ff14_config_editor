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
    public async Task<MapDataLoadResult> EnsureMapDataAvailableAsync()
    {
        try
        {
            using HttpClient httpClient = new()
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            string remoteVersionContent = await GetUtf8StringAsync(httpClient, MapDataVersionUrl);
            string remoteVersion = ParseMapDataVersion(remoteVersionContent);
            string localVersion = ReadMapDataVersion();
            if (File.Exists(MapDataInstanceFilePath) &&
                !string.IsNullOrWhiteSpace(remoteVersion) &&
                string.Equals(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase))
            {
                if (!LoadMapDataCache()) return new MapDataLoadResult(false, false, remoteVersion);

                MapDataVersion = remoteVersion;
                return new MapDataLoadResult(true, false, remoteVersion);
            }

            string instanceJson = await GetUtf8StringAsync(httpClient, MapDataInstanceUrl);
            Dictionary<ushort, string> mapNames = ParseMapNamesFromInstanceJson(instanceJson);
            if (mapNames.Count == 0) return new MapDataLoadResult(false, false, remoteVersion);

            WriteText(MapDataInstanceFilePath, instanceJson);
            WriteText(MapDataVersionFilePath, string.IsNullOrWhiteSpace(remoteVersion) ? remoteVersionContent : remoteVersion);
            MapData.ApplyMapNames(mapNames);
            MapDataVersion = remoteVersion;
            return new MapDataLoadResult(true, true, remoteVersion);
        }
        catch
        {
            return new MapDataLoadResult(false, false, string.Empty);
        }
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
        return true;
    }

    private string ReadMapDataVersion()
    {
        string? versionContent = ReadText(MapDataVersionFilePath);
        return string.IsNullOrWhiteSpace(versionContent) ? string.Empty : ParseMapDataVersion(versionContent);
    }

    private static async Task<string> GetUtf8StringAsync(HttpClient httpClient, string url)
    {
        byte[] bytes = await httpClient.GetByteArrayAsync(url);
        return Encoding.UTF8.GetString(bytes);
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