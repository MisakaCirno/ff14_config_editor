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

    public async Task<ServerListLoadResult> EnsureServerListAvailableAsync()
    {
        if (HasValidServerListCache() && !ShouldSyncServerList())
        {
            return new ServerListLoadResult(true, false, CacheAvailable: true);
        }

        ServerListLoadResult syncResult = await SyncServerListAsync(saveFailureAttempt: HasValidServerListCache());
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
        ServerListLoadResult result = await SyncServerListAsync(saveFailureAttempt: HasValidServerListCache());
        return result.Success;
    }

    private async Task<ServerListLoadResult> SyncServerListAsync(bool saveFailureAttempt)
    {
        DateTime syncAttemptTime = DateTime.Now;
        try
        {
            using HttpClient httpClient = new()
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            string apiJson = await GetServerStatusApiJsonAsync(httpClient);
            List<ServerGroup> groups = ParseServerGroups(apiJson);

            if (groups.Count == 0)
            {
                string html = await httpClient.GetStringAsync(ServerListSourceUrl);
                string combinedPageText = html;
                foreach (Uri resourceUri in ExtractServerPageResourceUris(html, new Uri(ServerListSourceUrl)))
                {
                    try
                    {
                        combinedPageText += "\n" + await httpClient.GetStringAsync(resourceUri);
                    }
                    catch
                    {
                        // Some CDN assets can be optional or region-blocked; keep parsing the resources we did fetch.
                    }
                }

                groups = ParseServerGroups(combinedPageText);
            }

            if (groups.Count == 0)
            {
                return HandleServerListSyncFailure(syncAttemptTime, saveFailureAttempt);
            }

            ServerList = new ServerListCache
            {
                SourceUrl = ServerStatusApiUrl,
                LastUpdated = syncAttemptTime,
                LastSyncAttempt = syncAttemptTime,
                Groups = groups
            };
            SaveServerList();
            return new ServerListLoadResult(true, true, CacheAvailable: true);
        }
        catch
        {
            return HandleServerListSyncFailure(syncAttemptTime, saveFailureAttempt);
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
        WriteJson(ServersFilePath, ServerList);
    }

    private ServerListLoadResult HandleServerListSyncFailure(DateTime syncAttemptTime, bool saveFailureAttempt)
    {
        bool cacheAvailable = HasValidServerListCache();
        if (saveFailureAttempt && cacheAvailable)
        {
            ServerList.LastSyncAttempt = syncAttemptTime;
            SaveServerList();
        }

        return new ServerListLoadResult(false, false, CacheAvailable: cacheAvailable);
    }

    private bool HasValidServerListCache()
    {
        return IsValidServerListCache(ServerList);
    }

    private bool ShouldSyncServerList()
    {
        DateTime lastServerSyncCheck = ServerList.LastUpdated > ServerList.LastSyncAttempt
            ? ServerList.LastUpdated
            : ServerList.LastSyncAttempt;
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

    private static async Task<string> GetServerStatusApiJsonAsync(HttpClient httpClient)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, ServerStatusApiUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FFXIVConfigEditor");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        request.Headers.TryAddWithoutValidation("Referer", "https://ff.web.sdo.com/");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
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
