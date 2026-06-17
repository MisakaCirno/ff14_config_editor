using System.Text.Json.Serialization;

namespace UIMarkerEditor;

public sealed class BootstrapSettings
{
    public string DataDirectory { get; set; } = string.Empty;
}

public sealed record MapDataLoadResult(
    bool Success,
    bool Updated,
    string Version,
    bool UsedCache = false,
    bool CacheAvailable = false);

public sealed record ServerListLoadResult(
    bool Success,
    bool Updated,
    bool UsedCache = false,
    bool CacheAvailable = false);

public sealed class MapDataCache
{
    public string Version { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public DateTime LastSyncAttempt { get; set; } = DateTime.MinValue;
    public Dictionary<string, string> Instances { get; set; } = [];
}

public enum StartupWayMarkAction
{
    None = 0,
    LoadMostRecentFile = 1,
    OpenFileDialog = 2
}

public sealed class AppSettings
{
    public int MaxBackupCount { get; set; } = 100;
    public int MaxBackupDays { get; set; } = 90;
    public bool LimitBackupCount { get; set; } = true;
    public bool LimitBackupDays { get; set; } = true;
    public bool AutoBackupBeforeSave { get; set; } = true;
    public bool UseWayMarkImageLabels { get; set; } = true;
    public StartupWayMarkAction StartupWayMarkAction { get; set; } = StartupWayMarkAction.None;
    public DateTime LastMapDataManualRefreshAttempt { get; set; } = DateTime.MinValue;
    public WindowLayoutSettings WindowLayout { get; set; } = new();
    public List<string> RecentFiles { get; set; } = [];
}

public sealed class WindowLayoutSettings
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string WindowState { get; set; } = nameof(System.Windows.WindowState.Normal);
    public double WayMarkListRatio { get; set; } = 1d / 3d;
    public double WayMarkEditorRatio { get; set; } = 1d / 3d;
    public double WayMarkPreviewRatio { get; set; } = 1d / 3d;
    public double BackupListRatio { get; set; } = 0.4;
    public double CharacterListRatio { get; set; } = 0.4;
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
