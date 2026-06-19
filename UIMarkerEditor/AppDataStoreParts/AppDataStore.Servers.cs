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
    private static readonly TimeSpan ServerListAutoSyncInterval = TimeSpan.FromDays(7);
    private static readonly TimeSpan ServerListRequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly IReadOnlyDictionary<string, string> ServerStatusRequestHeaders =
        new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FFXIVConfigEditor",
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "zh-CN,zh;q=0.9",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Referer"] = "https://ff.web.sdo.com/",
            ["Pragma"] = "no-cache",
            ["Cache-Control"] = "no-cache"
        };

    public async Task<ServerListLoadResult> EnsureServerListAvailableAsync()
    {
        if (HasValidServerListCache() && !ShouldSyncServerList())
        {
            return new ServerListLoadResult(true, false, CacheAvailable: true);
        }

        ServerListLoadResult syncResult = await SyncServerListAsync();
        if (syncResult.Success)
        {
            return syncResult;
        }

        return HasValidServerListCache()
            ? new ServerListLoadResult(true, false, UsedCache: true, CacheAvailable: true)
            : syncResult;
    }

    public async Task<bool> TrySyncServerListAsync()
    {
        ServerListLoadResult result = await SyncServerListAsync();
        return result.Success;
    }

    private async Task<ServerListLoadResult> SyncServerListAsync()
    {
        DateTime successfulSyncTime = DateTime.Now;
        try
        {
            string apiJson = await GetServerStatusApiJsonAsync();
            List<ServerGroup> groups = ParseServerGroups(apiJson);

            if (groups.Count == 0)
            {
                string html = await networkClient.GetStringAsync(ServerListSourceUrl, ServerListRequestTimeout);
                string combinedPageText = html;
                foreach (Uri resourceUri in ExtractServerPageResourceUris(html, new Uri(ServerListSourceUrl)))
                {
                    try
                    {
                        combinedPageText += "\n" + await networkClient.GetStringAsync(
                            resourceUri.ToString(),
                            ServerListRequestTimeout);
                    }
                    catch
                    {
                        // 部分 CDN 资源可能缺失或被网络拦截，继续解析已经取得的内容。
                    }
                }

                groups = ParseServerGroups(combinedPageText);
            }

            if (groups.Count == 0)
            {
                return CreateServerListSyncFailureResult();
            }

            ServerListCache nextServerList = new()
            {
                SourceUrl = ServerStatusApiUrl,
                LastUpdated = successfulSyncTime,
                LastSuccessfulSyncAt = successfulSyncTime,
                Groups = groups
            };
            SaveServerList(nextServerList);
            ServerList = nextServerList;
            return new ServerListLoadResult(true, true, CacheAvailable: true);
        }
        catch
        {
            return CreateServerListSyncFailureResult();
        }
    }

    private void LoadServerList()
    {
        JsonFileReadResult<ServerListCache> serverListResult = ReadJsonFile<ServerListCache>(ServersFilePath);
        if (serverListResult.Status == JsonFileReadStatus.Invalid)
        {
            AddJsonReadWarning(
                ServersFilePath,
                "服务器列表缓存无法读取，已按无缓存处理。",
                serverListResult.Error);
        }

        ServerListCache? cachedServerList = serverListResult.Status == JsonFileReadStatus.Success
            ? serverListResult.Value
            : null;
        if (cachedServerList != null)
        {
            cachedServerList.Groups ??= [];
        }

        ServerList = IsValidServerListCache(cachedServerList)
            ? cachedServerList!
            : new ServerListCache();
    }

    private void SaveServerList()
    {
        SaveServerList(ServerList);
    }

    private void SaveServerList(ServerListCache serverList)
    {
        WriteJson(ServersFilePath, serverList);
    }

    private ServerListLoadResult CreateServerListSyncFailureResult()
    {
        bool cacheAvailable = HasValidServerListCache();
        return new ServerListLoadResult(false, false, CacheAvailable: cacheAvailable);
    }

    private bool HasValidServerListCache()
    {
        return IsValidServerListCache(ServerList);
    }

    private bool ShouldSyncServerList()
    {
        DateTime lastServerSyncCheck = ServerList.LastUpdated > ServerList.LastSuccessfulSyncAt
            ? ServerList.LastUpdated
            : ServerList.LastSuccessfulSyncAt;
        return lastServerSyncCheck == DateTime.MinValue ||
            DateTime.Now - lastServerSyncCheck >= ServerListAutoSyncInterval;
    }

    private static bool IsValidServerListCache(ServerListCache? serverList)
    {
        return serverList?.Groups?.Count > 0 && serverList.LastUpdated > DateTime.MinValue;
    }

    private static List<Uri> ExtractServerPageResourceUris(string html, Uri baseUri)
    {
        List<Uri> resourceUris = [];
        foreach (Match match in Regex.Matches(html, "(?:src|href)=[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase))
        {
            string url = match.Groups["url"].Value;
            string urlWithoutQuery = url.Split('?', '#')[0];
            if (!urlWithoutQuery.EndsWith(".js", StringComparison.OrdinalIgnoreCase) &&
                !urlWithoutQuery.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Uri resourceUri = url.StartsWith("//", StringComparison.Ordinal)
                ? new Uri($"{baseUri.Scheme}:{url}")
                : new Uri(baseUri, url);

            if (!resourceUris.Any(existing => existing.Equals(resourceUri)))
            {
                resourceUris.Add(resourceUri);
            }
        }

        return resourceUris;
    }

    private async Task<string> GetServerStatusApiJsonAsync()
    {
        return await networkClient.GetStringAsync(
            ServerStatusApiUrl,
            ServerListRequestTimeout,
            ServerStatusRequestHeaders);
    }

    private static List<ServerGroup> ParseServerGroups(string html)
    {
        List<ServerGroup> apiGroups = ParseServerGroupsFromApiJson(html);
        if (apiGroups.Count > 0)
        {
            return apiGroups;
        }

        List<ServerGroup> builtinGroups = ServerListCache.CreateBuiltin().Groups;
        Dictionary<string, int> dataCenterPositions = [];
        foreach (ServerGroup group in builtinGroups)
        {
            int position = html.IndexOf(group.DataCenter, StringComparison.OrdinalIgnoreCase);
            if (position >= 0)
            {
                dataCenterPositions[group.DataCenter] = position;
            }
        }

        if (dataCenterPositions.Count == 0)
        {
            return ParseServerGroupsByKnownWorlds(html, builtinGroups);
        }

        List<ServerGroup> groups = [];
        foreach (ServerGroup builtinGroup in builtinGroups)
        {
            if (!dataCenterPositions.TryGetValue(builtinGroup.DataCenter, out int dataCenterPosition)) continue;

            int nextDataCenterPosition = dataCenterPositions
                .Where(pair => pair.Value > dataCenterPosition)
                .Select(pair => pair.Value)
                .DefaultIfEmpty(html.Length)
                .Min();
            string segment = html[dataCenterPosition..nextDataCenterPosition];

            List<string> worlds = [];
            foreach (string world in builtinGroup.Worlds)
            {
                if (Regex.IsMatch(segment, Regex.Escape(world), RegexOptions.IgnoreCase))
                {
                    worlds.Add(world);
                }
            }

            if (worlds.Count > 0)
            {
                groups.Add(new ServerGroup
                {
                    DataCenter = builtinGroup.DataCenter,
                    Worlds = worlds
                });
            }
        }

        return groups;
    }

    private static List<ServerGroup> ParseServerGroupsFromApiJson(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("IsSuccess", out JsonElement isSuccessElement) ||
                isSuccessElement.ValueKind != JsonValueKind.True ||
                !document.RootElement.TryGetProperty("Data", out JsonElement dataElement) ||
                dataElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            List<ServerGroup> groups = [];
            foreach (JsonElement areaElement in dataElement.EnumerateArray())
            {
                if (!areaElement.TryGetProperty("AreaName", out JsonElement areaNameElement)) continue;
                string? areaName = areaNameElement.GetString();
                if (string.IsNullOrWhiteSpace(areaName)) continue;
                if (!areaElement.TryGetProperty("Group", out JsonElement worldGroupElement) ||
                    worldGroupElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                List<string> worlds = [];
                foreach (JsonElement worldElement in worldGroupElement.EnumerateArray())
                {
                    if (!worldElement.TryGetProperty("name", out JsonElement worldNameElement)) continue;
                    string? worldName = worldNameElement.GetString();
                    if (!string.IsNullOrWhiteSpace(worldName))
                    {
                        worlds.Add(worldName);
                    }
                }

                if (worlds.Count > 0)
                {
                    groups.Add(new ServerGroup
                    {
                        DataCenter = areaName,
                        Worlds = worlds
                    });
                }
            }

            return groups;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<ServerGroup> ParseServerGroupsByKnownWorlds(string text, List<ServerGroup> builtinGroups)
    {
        List<ServerGroup> groups = [];
        foreach (ServerGroup builtinGroup in builtinGroups)
        {
            List<string> worlds = [.. builtinGroup.Worlds.Where(world =>
                Regex.IsMatch(text, Regex.Escape(world), RegexOptions.IgnoreCase))];
            if (worlds.Count == 0) continue;

            groups.Add(new ServerGroup
            {
                DataCenter = builtinGroup.DataCenter,
                Worlds = worlds
            });
        }

        return groups;
    }

}
