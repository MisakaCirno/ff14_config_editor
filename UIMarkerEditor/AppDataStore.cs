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

public sealed class AppDataStore
{
    private const string AppFolderName = "FFXIVConfigEditor";
    private const string BootstrapFileName = "bootstrap.json";
    private const string SettingsFileName = "config.json";
    private const string CharactersFileName = "characters.json";
    private const string ServersFileName = "servers.json";
    private const string MapDataVersionFileName = "mapdata.version";
    private const string MapDataInstanceFileName = "instance.json";
    private const string MetadataFileName = "metadata.json";
    private const string BackupDataFileName = "UISAVE.DAT";
    private const string ServerListSourceUrl = "https://ff.web.sdo.com/web8/index.html#/servers";
    private const string ServerStatusApiUrl = "https://ff14act.web.sdo.com/api/serverStatus/getServerStatus";
    private const string MapDataVersionUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/data.version";
    private const string MapDataInstanceUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/instance.json";

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string BootstrapDirectory { get; }
    public string BootstrapFilePath => Path.Combine(BootstrapDirectory, BootstrapFileName);
    public string DefaultDataDirectory => Path.Combine(BootstrapDirectory, "Data");
    public string DataDirectory { get; private set; }
    public string BackupsDirectory => Path.Combine(DataDirectory, "backups");
    public string SettingsFilePath => Path.Combine(DataDirectory, SettingsFileName);
    public string CharactersFilePath => Path.Combine(DataDirectory, CharactersFileName);
    public string ServersFilePath => Path.Combine(DataDirectory, ServersFileName);
    public string MapDataVersionFilePath => Path.Combine(DataDirectory, MapDataVersionFileName);
    public string MapDataInstanceFilePath => Path.Combine(DataDirectory, MapDataInstanceFileName);

    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<CharacterProfile> Characters { get; } = [];
    public ServerListCache ServerList { get; private set; } = ServerListCache.CreateBuiltin();
    public string MapDataVersion { get; private set; } = string.Empty;

    public AppDataStore()
    {
        BootstrapDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);
        DataDirectory = DefaultDataDirectory;
    }

    public void Initialize()
    {
        Directory.CreateDirectory(BootstrapDirectory);

        BootstrapSettings? bootstrap = ReadJson<BootstrapSettings>(BootstrapFilePath);
        DataDirectory = !string.IsNullOrWhiteSpace(bootstrap?.DataDirectory)
            ? bootstrap.DataDirectory
            : DefaultDataDirectory;

        try
        {
            EnsureDataDirectory();
        }
        catch when (!string.Equals(DataDirectory, DefaultDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            DataDirectory = DefaultDataDirectory;
            EnsureDataDirectory();
        }

        SaveBootstrap();
        LoadSettings();
        LoadCharacters();
        LoadServerList();
    }

    public void SaveSettings(AppSettings settings)
    {
        Settings = settings;
        EnsureDataDirectory();
        WriteJson(SettingsFilePath, Settings);
    }

    public void SaveCharacters()
    {
        EnsureDataDirectory();
        WriteJson(CharactersFilePath, Characters.OrderBy(c => c.UserID, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public async Task<bool> TrySyncServerListAsync()
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
                ServerList.LastSyncAttempt = syncAttemptTime;
                SaveServerList();
                return false;
            }

            ServerList = new ServerListCache
            {
                SourceUrl = ServerStatusApiUrl,
                LastUpdated = syncAttemptTime,
                LastSyncAttempt = syncAttemptTime,
                Groups = groups
            };
            SaveServerList();
            return true;
        }
        catch
        {
            ServerList.LastSyncAttempt = syncAttemptTime;
            SaveServerList();
            return false;
        }
    }

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

    public void ChangeDataDirectory(string newDataDirectory, bool migrateExistingData)
    {
        if (string.IsNullOrWhiteSpace(newDataDirectory))
        {
            throw new InvalidOperationException("数据目录不能为空。");
        }

        string oldDataDirectory = DataDirectory;
        string targetDirectory = Path.GetFullPath(newDataDirectory);
        Directory.CreateDirectory(targetDirectory);
        VerifyDirectoryWritable(targetDirectory);

        if (migrateExistingData && Directory.Exists(oldDataDirectory) &&
            !string.Equals(oldDataDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
        {
            string oldFullPath = Path.GetFullPath(oldDataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string targetFullPath = targetDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (targetFullPath.StartsWith(oldFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("新数据目录不能位于旧数据目录内部，请选择其它位置后再迁移。");
            }

            CopyDirectory(oldDataDirectory, targetDirectory);
        }

        if (!migrateExistingData)
        {
            Settings = new AppSettings();
            Characters.Clear();
            ServerList = ServerListCache.CreateBuiltin();
        }

        DataDirectory = targetDirectory;
        EnsureDataDirectory();
        SaveBootstrap();
        LoadSettings();
        LoadCharacters();
        LoadServerList();
    }

    public BackupMetadata CreateBackup(string sourceFilePath, bool cleanupAfterCreate = true)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("找不到要备份的 UISAVE.DAT 文件。", sourceFilePath);
        }

        ConfigUISave sourceConfig = new(sourceFilePath);
        BackupMetadata metadata = CreateMetadata(sourceFilePath, sourceConfig);
        string backupDirectory = Path.Combine(BackupsDirectory, metadata.Id);
        string backupFilePath = Path.Combine(backupDirectory, BackupDataFileName);

        Directory.CreateDirectory(backupDirectory);
        File.Copy(sourceFilePath, backupFilePath, overwrite: true);
        metadata.BackupDirectory = backupDirectory;
        metadata.BackupFilePath = backupFilePath;
        WriteJson(Path.Combine(backupDirectory, MetadataFileName), metadata);

        if (cleanupAfterCreate)
        {
            CleanupBackups();
        }

        return metadata;
    }

    public List<BackupMetadata> LoadBackups()
    {
        EnsureDataDirectory();
        if (!Directory.Exists(BackupsDirectory)) return [];

        List<BackupMetadata> backups = [];
        foreach (string metadataPath in Directory.EnumerateFiles(BackupsDirectory, MetadataFileName, SearchOption.AllDirectories))
        {
            BackupMetadata? metadata = ReadJson<BackupMetadata>(metadataPath);
            if (metadata == null) continue;

            metadata.BackupDirectory = Path.GetDirectoryName(metadataPath) ?? string.Empty;
            metadata.BackupFilePath = Path.Combine(metadata.BackupDirectory, BackupDataFileName);
            backups.Add(metadata);
        }

        return [.. backups.OrderByDescending(b => b.BackupTime)];
    }

    public void DeleteBackup(BackupMetadata backup)
    {
        if (!string.IsNullOrWhiteSpace(backup.BackupDirectory) && Directory.Exists(backup.BackupDirectory))
        {
            Directory.Delete(backup.BackupDirectory, recursive: true);
        }
    }

    public void RestoreBackup(BackupMetadata backup, string targetFilePath)
    {
        if (!File.Exists(backup.BackupFilePath))
        {
            throw new FileNotFoundException("找不到备份文件。", backup.BackupFilePath);
        }

        string? targetDirectory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.Copy(backup.BackupFilePath, targetFilePath, overwrite: true);
    }

    public void CleanupBackups(params string[] preservedBackupDirectories)
    {
        List<BackupMetadata> backups = LoadBackups();
        HashSet<string> deleteDirectories = [];
        HashSet<string> preservedDirectories = [.. preservedBackupDirectories
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(NormalizeDirectoryPath)];

        if (Settings.MaxBackupDays > 0)
        {
            DateTime cutoff = DateTime.Now.AddDays(-Settings.MaxBackupDays);
            foreach (BackupMetadata backup in backups.Where(b =>
                b.BackupTime < cutoff &&
                !preservedDirectories.Contains(NormalizeDirectoryPath(b.BackupDirectory))))
            {
                deleteDirectories.Add(backup.BackupDirectory);
            }
        }

        if (Settings.MaxBackupCount > 0)
        {
            foreach (BackupMetadata backup in backups
                .Where(b => !preservedDirectories.Contains(NormalizeDirectoryPath(b.BackupDirectory)))
                .OrderByDescending(b => b.BackupTime)
                .Skip(Settings.MaxBackupCount))
            {
                deleteDirectories.Add(backup.BackupDirectory);
            }
        }

        foreach (string directory in deleteDirectories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public CharacterProfile GetOrCreateCharacter(string? userID)
    {
        string normalizedUserID = string.IsNullOrWhiteSpace(userID) ? "UNKNOWN" : userID.ToUpperInvariant();
        CharacterProfile? existing = Characters.FirstOrDefault(c =>
            string.Equals(c.UserID, normalizedUserID, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        CharacterProfile profile = new()
        {
            UserID = normalizedUserID,
            UpdatedAt = DateTime.Now
        };
        Characters.Add(profile);
        SaveCharacters();
        return profile;
    }

    public string GetCharacterDisplayName(string? userID)
    {
        if (string.IsNullOrWhiteSpace(userID)) return "未知角色";

        CharacterProfile? profile = Characters.FirstOrDefault(c =>
            string.Equals(c.UserID, userID, StringComparison.OrdinalIgnoreCase));
        return profile?.DisplayName ?? userID;
    }

    public static string? GetUserIDFromCharacterFolder(string filePath)
    {
        string? folderPath = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folderPath)) return null;

        string folderName = new DirectoryInfo(folderPath).Name;
        const string prefix = "FFXIV_CHR";
        return folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? folderName[prefix.Length..].ToUpperInvariant()
            : null;
    }

    private BackupMetadata CreateMetadata(string sourceFilePath, ConfigUISave sourceConfig)
    {
        string backupTimeId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        string folderUserID = GetUserIDFromCharacterFolder(sourceFilePath) ?? string.Empty;
        string fileUserID = sourceConfig.UserIDHex;
        string userIDForName = !string.IsNullOrWhiteSpace(fileUserID) ? fileUserID : folderUserID;

        return new BackupMetadata
        {
            Id = $"{backupTimeId}_{SanitizeFileName(userIDForName, "UNKNOWN")}_{uniqueSuffix}",
            BackupTime = DateTime.Now,
            OriginalFilePath = sourceFilePath,
            OriginalDirectory = Path.GetDirectoryName(sourceFilePath) ?? string.Empty,
            FolderUserID = folderUserID,
            FileUserID = fileUserID,
            SourceFileSize = new FileInfo(sourceFilePath).Length,
            SourceFileSha256 = ComputeSha256(sourceFilePath),
            MarkerSnapshots = CreateMarkerSnapshots(sourceConfig.Marks)
        };
    }

    private static List<BackupMarkerSnapshot> CreateMarkerSnapshots(SectionFMARKER? marks)
    {
        if (marks == null) return [];

        List<BackupMarkerSnapshot> snapshots = [];
        for (int index = 0; index < marks.WayMarks.Count; index++)
        {
            WayMark mark = marks.WayMarks[index];
            if (mark.RegionID == 0) continue;

            snapshots.Add(new BackupMarkerSnapshot
            {
                SlotIndex = index + 1,
                RegionID = mark.RegionID,
                RegionName = MapData.GetName(mark.RegionID),
                SlotCount = 1,
                EnabledPointCount = CountEnabledPoints(mark)
            });
        }

        return snapshots;
    }

    private static int CountEnabledPoints(WayMark mark)
    {
        int count = 0;
        if (mark.AEnabled) count++;
        if (mark.BEnabled) count++;
        if (mark.CEnabled) count++;
        if (mark.DEnabled) count++;
        if (mark.OneEnabled) count++;
        if (mark.TwoEnabled) count++;
        if (mark.ThreeEnabled) count++;
        if (mark.FourEnabled) count++;
        return count;
    }

    private void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        if (!File.Exists(SettingsFilePath))
        {
            WriteJson(SettingsFilePath, Settings);
        }

        if (!File.Exists(CharactersFilePath))
        {
            WriteJson(CharactersFilePath, new List<CharacterProfile>());
        }

        if (!File.Exists(ServersFilePath))
        {
            SaveServerList();
        }
    }

    private void LoadSettings()
    {
        Settings = ReadJson<AppSettings>(SettingsFilePath) ?? new AppSettings();
    }

    private void LoadCharacters()
    {
        Characters.Clear();
        foreach (CharacterProfile profile in ReadJson<List<CharacterProfile>>(CharactersFilePath) ?? [])
        {
            Characters.Add(profile);
        }
    }

    private void LoadServerList()
    {
        ServerListCache? cachedServerList = ReadJson<ServerListCache>(ServersFilePath);
        ServerList = cachedServerList?.Groups.Count > 0 ? cachedServerList : ServerListCache.CreateBuiltin();
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

    private void SaveServerList()
    {
        WriteJson(ServersFilePath, ServerList);
    }

    private void SaveBootstrap()
    {
        WriteJson(BootstrapFilePath, new BootstrapSettings { DataDirectory = DataDirectory });
    }

    private string ReadMapDataVersion()
    {
        string? versionContent = ReadText(MapDataVersionFilePath);
        return string.IsNullOrWhiteSpace(versionContent) ? string.Empty : ParseMapDataVersion(versionContent);
    }

    private T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, jsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string? ReadText(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private void WriteJson<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(value, jsonOptions);
        File.WriteAllText(path, json);
    }

    private static void WriteText(string path, string value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, value);
    }

    private static async Task<string> GetUtf8StringAsync(HttpClient httpClient, string url)
    {
        byte[] bytes = await httpClient.GetByteArrayAsync(url);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void VerifyDirectoryWritable(string directory)
    {
        string testFile = Path.Combine(directory, $".write_test_{Guid.NewGuid():N}");
        File.WriteAllText(testFile, string.Empty);
        File.Delete(testFile);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase));
        }

        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string targetFile = file.Replace(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase);
            string? targetFileDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetFileDirectory))
            {
                Directory.CreateDirectory(targetFileDirectory);
            }

            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static string SanitizeFileName(string value, string fallback)
    {
        string sanitized = string.Join("_", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string NormalizeDirectoryPath(string directory)
    {
        return Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

public sealed class BootstrapSettings
{
    public string DataDirectory { get; set; } = string.Empty;
}

public sealed record MapDataLoadResult(bool Success, bool Updated, string Version);

public sealed class AppSettings
{
    public int MaxBackupCount { get; set; } = 100;
    public int MaxBackupDays { get; set; } = 90;
    public bool AutoBackupBeforeSave { get; set; } = true;
}

public sealed class CharacterProfile
{
    public string UserID { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(CharacterName) ? UserID : CharacterName;
            string world = string.Join(" / ", new[] { DataCenter, World }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.IsNullOrWhiteSpace(world) ? name : $"{name} ({world})";
        }
    }
}

public sealed class ServerListCache
{
    public string SourceUrl { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public DateTime LastSyncAttempt { get; set; } = DateTime.MinValue;
    public List<ServerGroup> Groups { get; set; } = [];

    public static ServerListCache CreateBuiltin()
    {
        return new ServerListCache
        {
            SourceUrl = "内置服务器列表",
            LastUpdated = DateTime.MinValue,
            LastSyncAttempt = DateTime.MinValue,
            Groups =
            [
                new ServerGroup
                {
                    DataCenter = "陆行鸟",
                    Worlds = ["拉诺西亚", "幻影群岛", "神意之地", "萌芽池", "红玉海", "宇宙和音", "沃仙曦染", "晨曦王座"]
                },
                new ServerGroup
                {
                    DataCenter = "莫古力",
                    Worlds = ["潮风亭", "神拳痕", "白银乡", "白金幻象", "旅人栈桥", "拂晓之间", "龙巢神殿", "梦羽宝境"]
                },
                new ServerGroup
                {
                    DataCenter = "猫小胖",
                    Worlds = ["紫水栈桥", "延夏", "静语庄园", "摩杜纳", "海猫茶屋", "柔风海湾", "琥珀原"]
                },
                new ServerGroup
                {
                    DataCenter = "豆豆柴",
                    Worlds = ["水晶塔", "银泪湖", "太阳海岸", "伊修加德", "红茶川"]
                }
            ]
        };
    }
}

public sealed class ServerGroup
{
    public string DataCenter { get; set; } = string.Empty;
    public List<string> Worlds { get; set; } = [];
}

public sealed class BackupMetadata
{
    public string Id { get; set; } = string.Empty;
    public DateTime BackupTime { get; set; }
    public string OriginalFilePath { get; set; } = string.Empty;
    public string OriginalDirectory { get; set; } = string.Empty;
    public string FolderUserID { get; set; } = string.Empty;
    public string FileUserID { get; set; } = string.Empty;
    public long SourceFileSize { get; set; }
    public string SourceFileSha256 { get; set; } = string.Empty;
    public List<BackupMarkerSnapshot> MarkerSnapshots { get; set; } = [];

    [JsonIgnore]
    public string BackupDirectory { get; set; } = string.Empty;

    [JsonIgnore]
    public string BackupFilePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayTitle => $"{BackupTime:yyyy-MM-dd HH:mm:ss}  {CharacterDisplayName}";

    [JsonIgnore]
    public string CharacterDisplayName { get; set; } = string.Empty;

    [JsonIgnore]
    public string CharacterNameDisplay { get; set; } = string.Empty;

    [JsonIgnore]
    public string ServerDisplayName { get; set; } = string.Empty;

    [JsonIgnore]
    public string EffectiveUserID => !string.IsNullOrWhiteSpace(FileUserID) ? FileUserID : FolderUserID;

    [JsonIgnore]
    public string Summary => $"{MarkerSnapshots.Count} 条标点记录";
}

public sealed class BackupMarkerSnapshot
{
    public int SlotIndex { get; set; }
    public ushort RegionID { get; set; }
    public string RegionName { get; set; } = string.Empty;
    public int SlotCount { get; set; }
    public int EnabledPointCount { get; set; }

    [JsonIgnore]
    public string DisplayText => SlotIndex > 0
        ? $"第 {SlotIndex} 项：{RegionName}({RegionID})，启用 {EnabledPointCount} 个标点"
        : $"{RegionName}({RegionID}) - {SlotCount} 个槽位，启用 {EnabledPointCount} 个标点";
}
