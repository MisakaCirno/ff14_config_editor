using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UIMarkerEditor;

internal static class MapDataSourceParsers
{
    public static Dictionary<ushort, string> ParseContentFinderConditionMapNames(string csv)
    {
        List<List<string>> records = MapDataTableCsv.ReadRecords(csv);
        if (records.Count == 0)
        {
            return [];
        }

        if (!TryFindContentFinderConditionColumns(records, out int headerIndex, out int mapIdIndex, out int nameIndex))
        {
            return [];
        }

        Dictionary<ushort, string> mapNames = [];
        for (int i = headerIndex + 1; i < records.Count; i++)
        {
            List<string> fields = records[i];
            if (fields.Count <= Math.Max(nameIndex, mapIdIndex))
            {
                continue;
            }

            string name = fields[nameIndex].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!ushort.TryParse(fields[mapIdIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort mapId) ||
                mapId == MapData.EmptyRegionId)
            {
                continue;
            }

            mapNames.TryAdd(mapId, name);
        }

        return mapNames;
    }

    public static string ParseDiemoeVersion(string versionContent)
    {
        foreach (string line in versionContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            if (string.Equals(key, "build_version", StringComparison.OrdinalIgnoreCase))
            {
                return line[(separatorIndex + 1)..].Trim();
            }
        }

        return versionContent.Trim();
    }

    public static Dictionary<ushort, string> ParseDiemoeMapNamesFromInstanceJson(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        Dictionary<ushort, string> mapNames = [];
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!ushort.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort mapId) ||
                mapId == MapData.EmptyRegionId)
            {
                continue;
            }

            if (TryGetChineseInstanceName(property.Value, out string mapName))
            {
                mapNames.TryAdd(mapId, mapName);
            }
        }

        return mapNames;
    }

    public static string CreateContentHashVersion(string prefix, string content)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return $"{prefix}-{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static bool TryFindContentFinderConditionColumns(
        IReadOnlyList<List<string>> records,
        out int headerIndex,
        out int mapIdIndex,
        out int nameIndex)
    {
        for (int i = 0; i < records.Count; i++)
        {
            mapIdIndex = FindCsvColumnIndex(records[i], "#");
            nameIndex = FindCsvColumnIndex(records[i], "Name");
            if (mapIdIndex >= 0 && nameIndex >= 0)
            {
                headerIndex = i;
                return true;
            }
        }

        headerIndex = -1;
        mapIdIndex = -1;
        nameIndex = -1;
        return false;
    }

    private static int FindCsvColumnIndex(IReadOnlyList<string> headers, string name)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            if (string.Equals(MapDataTableCsv.NormalizeHeaderName(headers[i]), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetChineseInstanceName(JsonElement instanceElement, out string mapName)
    {
        mapName = string.Empty;
        if (instanceElement.ValueKind != JsonValueKind.Object ||
            !instanceElement.TryGetProperty("name", out JsonElement nameElement))
        {
            return false;
        }

        if (nameElement.ValueKind == JsonValueKind.Object &&
            nameElement.TryGetProperty("chs", out JsonElement chineseNameElement))
        {
            mapName = chineseNameElement.GetString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(mapName);
        }

        if (nameElement.ValueKind == JsonValueKind.String)
        {
            mapName = nameElement.GetString()?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(mapName);
        }

        return false;
    }
}
