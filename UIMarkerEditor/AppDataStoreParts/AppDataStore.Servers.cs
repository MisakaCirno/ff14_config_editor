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
    private static readonly TimeSpan ServerListSyncTimeout = TimeSpan.FromSeconds(20);
    private const int ServerApiMaxResponseBytes = 2 * 1024 * 1024;
    private const int ServerPageMaxResponseBytes = 4 * 1024 * 1024;
    private const int ServerPageResourceMaxResponseBytes = 2 * 1024 * 1024;
    private const int ServerPageMaxResourceCount = 8;
    private const int ServerPageMaxCombinedCharacters = 8 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> ServerStatusRequestHeaders =
        new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FFXIVConfigEditor",
            ["Accept"] = "application/json, text/plain, */*",
            ["Accept-Language"] = "zh-CN,zh;q=0.9",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Referer"] = ExternalLinks.ServerListReferer,
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
            ? syncResult with { Success = true, UsedCache = true, CacheAvailable = true }
            : syncResult;
    }

    public async Task<ServerListLoadResult> RefreshServerListAsync()
    {
        return await SyncServerListAsync();
    }

    public async Task<bool> TrySyncServerListAsync()
    {
        ServerListLoadResult result = await RefreshServerListAsync();
        return result.Success;
    }

    private async Task<ServerListLoadResult> SyncServerListAsync()
    {
        DateTime successfulSyncTime = DateTime.Now;
        string currentStage = "检查服务器列表";
        using CancellationTokenSource syncTimeoutSource = new(ServerListSyncTimeout);
        try
        {
            string apiJson = await GetServerStatusApiJsonAsync(syncTimeoutSource.Token);
            currentStage = "解析服务器列表";
            List<ServerGroup> groups = ParseServerGroups(apiJson);

            if (groups.Count == 0)
            {
                currentStage = "下载服务器列表页面";
                string html = await networkClient.GetStringAsync(
                    ExternalLinks.ServerListPage,
                    ServerListRequestTimeout,
                    ServerPageMaxResponseBytes,
                    cancellationToken: syncTimeoutSource.Token);
                StringBuilder combinedPageText = new(html, ServerPageMaxCombinedCharacters);
                foreach (Uri resourceUri in ExtractServerPageResourceUris(html, new Uri(ExternalLinks.ServerListPage)))
                {
                    try
                    {
                        string resourceText = await networkClient.GetStringAsync(
                            resourceUri.ToString(),
                            ServerListRequestTimeout,
                            ServerPageResourceMaxResponseBytes,
                            cancellationToken: syncTimeoutSource.Token);
                        if (combinedPageText.Length + resourceText.Length + 1 > ServerPageMaxCombinedCharacters)
                        {
                            break;
                        }

                        combinedPageText.Append('\n');
                        combinedPageText.Append(resourceText);
                    }
                    catch (OperationCanceledException) when (syncTimeoutSource.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        // 部分 CDN 资源可能缺失或被网络拦截，继续解析已经取得的内容。
                    }
                }

                currentStage = "解析服务器列表";
                groups = ParseServerGroups(combinedPageText.ToString());
            }

            if (groups.Count == 0)
            {
                return CreateServerListSyncFailureResult(
                    currentStage,
                    "未能从服务器列表响应中解析到可用服务器。");
            }

            if (HasValidServerListCache() && AreServerGroupsEqual(ServerList.Groups, groups))
            {
                ServerListCache checkedServerList = new()
                {
                    SourceUrl = ExternalLinks.ServerStatusApi,
                    LastUpdated = ServerList.LastUpdated,
                    LastSuccessfulSyncAt = successfulSyncTime,
                    Groups = CloneServerGroups(ServerList.Groups)
                };
                if (!TrySaveServerList(checkedServerList, out ServerListLoadResult? blockedResult))
                {
                    return blockedResult!;
                }

                ServerList = checkedServerList;
                return new ServerListLoadResult(true, false, CacheAvailable: true);
            }

            ServerListCache nextServerList = new()
            {
                SourceUrl = ExternalLinks.ServerStatusApi,
                LastUpdated = successfulSyncTime,
                LastSuccessfulSyncAt = successfulSyncTime,
                Groups = groups
            };
            if (!TrySaveServerList(nextServerList, out ServerListLoadResult? nextBlockedResult))
            {
                return nextBlockedResult!;
            }

            ServerList = nextServerList;
            return new ServerListLoadResult(true, true, CacheAvailable: true);
        }
        catch (Exception ex)
        {
            return CreateServerListSyncFailureResult(currentStage, FormatDataSyncFailureReason(ex));
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

    private bool TrySaveServerList(
        ServerListCache serverList,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out ServerListLoadResult? blockedResult)
    {
        blockedResult = null;
        if (!TryExecuteDataDirectoryManagedWrite(() => SaveServerList(serverList)))
        {
            blockedResult = CreateServerListSyncFailureResult(
                "保存服务器列表",
                "工具数据目录正在迁移，本次服务器列表同步结果已跳过。请在迁移完成后重新检查服务器列表。");
            return false;
        }

        return true;
    }

    private ServerListLoadResult CreateServerListSyncFailureResult(string failureStage, string failureReason)
    {
        bool cacheAvailable = HasValidServerListCache();
        return new ServerListLoadResult(
            false,
            false,
            CacheAvailable: cacheAvailable,
            FailureStage: string.IsNullOrWhiteSpace(failureStage) ? "检查服务器列表" : failureStage,
            FailureReason: string.IsNullOrWhiteSpace(failureReason) ? "未知原因。" : failureReason);
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

    private static bool AreServerGroupsEqual(IReadOnlyList<ServerGroup> first, IReadOnlyList<ServerGroup> second)
    {
        if (first.Count != second.Count) return false;

        for (int groupIndex = 0; groupIndex < first.Count; groupIndex++)
        {
            ServerGroup firstGroup = first[groupIndex];
            ServerGroup secondGroup = second[groupIndex];
            if (!string.Equals(firstGroup.DataCenter, secondGroup.DataCenter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!firstGroup.Worlds.SequenceEqual(secondGroup.Worlds, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static List<ServerGroup> CloneServerGroups(IEnumerable<ServerGroup> groups)
    {
        return [.. groups.Select(group => new ServerGroup
        {
            DataCenter = group.DataCenter,
            Worlds = [.. group.Worlds]
        })];
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

            string candidateUrl = url.StartsWith("//", StringComparison.Ordinal)
                ? $"{baseUri.Scheme}:{url}"
                : url;
            if (!Uri.TryCreate(baseUri, candidateUrl, out Uri? resourceUri) ||
                !IsAllowedServerPageResourceUri(resourceUri))
            {
                continue;
            }

            if (!resourceUris.Any(existing => existing.Equals(resourceUri)))
            {
                resourceUris.Add(resourceUri);
                if (resourceUris.Count >= ServerPageMaxResourceCount)
                {
                    break;
                }
            }
        }

        return resourceUris;
    }

    private static bool IsAllowedServerPageResourceUri(Uri resourceUri)
    {
        return string.Equals(resourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(resourceUri.Host, "sdo.com", StringComparison.OrdinalIgnoreCase) ||
             resourceUri.Host.EndsWith(".sdo.com", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> GetServerStatusApiJsonAsync(CancellationToken cancellationToken)
    {
        return await networkClient.GetStringAsync(
            ExternalLinks.ServerStatusApi,
            ServerListRequestTimeout,
            ServerApiMaxResponseBytes,
            ServerStatusRequestHeaders,
            cancellationToken);
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
