using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UIMarkerEditor;

internal static class MapDataSourceParsers
{
    public static GitHubFileCommitInfo? ParseGitHubLatestFileCommitInfo(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        using JsonElement.ArrayEnumerator commitEnumerator = document.RootElement.EnumerateArray();
        if (!commitEnumerator.MoveNext())
        {
            return null;
        }

        JsonElement commitElement = commitEnumerator.Current;
        if (!TryGetStringProperty(commitElement, "sha", out string sha) ||
            !IsGitHubCommitSha(sha))
        {
            return null;
        }

        string message = string.Empty;
        DateTimeOffset? commitDate = null;
        if (commitElement.TryGetProperty("commit", out JsonElement commitDetails) &&
            commitDetails.ValueKind == JsonValueKind.Object)
        {
            if (TryGetStringProperty(commitDetails, "message", out string commitMessage))
            {
                message = commitMessage;
            }

            if (TryGetGitHubCommitDate(commitDetails, out DateTimeOffset parsedCommitDate))
            {
                commitDate = parsedCommitDate;
            }
        }

        return new GitHubFileCommitInfo(sha, CreateGitHubCommitVersion(sha, message, commitDate));
    }

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
                return NormalizeDiemoeBuildVersion(line[(separatorIndex + 1)..].Trim());
            }
        }

        return NormalizeDiemoeBuildVersion(versionContent.Trim());
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

    public static string CreateContentHashVersion(string content)
    {
        return $"hash-{CreateContentHash(content)}";
    }

    public static string CreateTimestampedContentHashVersion(DateTime timestamp, string content)
    {
        return $"{FormatSnapshotTimestamp(timestamp)}-{CreateContentHash(content)}";
    }

    public static string FormatSnapshotTimestamp(DateTime timestamp)
    {
        return timestamp.ToString("yyyy.MM.dd.HHmmss", CultureInfo.InvariantCulture);
    }

    private static string NormalizeDiemoeBuildVersion(string version)
    {
        if (version.Length >= 8 &&
            version.Take(8).All(char.IsDigit))
        {
            string normalizedVersion = $"{version[..4]}.{version[4..6]}.{version[6..8]}";
            if (version.Length > 8)
            {
                normalizedVersion += version[8..];
            }

            return normalizedVersion;
        }

        return version;
    }

    private static string CreateContentHash(string content)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string CreateGitHubCommitVersion(string sha, string message, DateTimeOffset? commitDate)
    {
        string shortSha = sha[..Math.Min(7, sha.Length)].ToLowerInvariant();
        string upstreamVersion = ExtractGitHubUpstreamVersion(message);
        if (!string.IsNullOrWhiteSpace(upstreamVersion))
        {
            return upstreamVersion;
        }

        return commitDate.HasValue
            ? $"{FormatSnapshotTimestamp(commitDate.Value.UtcDateTime)}-{shortSha}"
            : shortSha;
    }

    private static string ExtractGitHubUpstreamVersion(string message)
    {
        string firstLine = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? string.Empty;
        const string versionPrefix = "[ver ";
        if (!firstLine.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        int versionEndIndex = firstLine.IndexOf(']', versionPrefix.Length);
        return versionEndIndex <= versionPrefix.Length
            ? string.Empty
            : firstLine[versionPrefix.Length..versionEndIndex].Trim();
    }

    private static bool IsGitHubCommitSha(string sha)
    {
        if (sha.Length < 7)
        {
            return false;
        }

        foreach (char character in sha)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetGitHubCommitDate(JsonElement commitDetails, out DateTimeOffset commitDate)
    {
        commitDate = default;
        foreach (string propertyName in new[] { "committer", "author" })
        {
            if (!commitDetails.TryGetProperty(propertyName, out JsonElement identityElement) ||
                identityElement.ValueKind != JsonValueKind.Object ||
                !TryGetStringProperty(identityElement, "date", out string dateText))
            {
                continue;
            }

            if (DateTimeOffset.TryParse(
                dateText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out commitDate))
            {
                return true;
            }
        }

        return false;
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

internal sealed record GitHubFileCommitInfo(string Sha, string Version);
