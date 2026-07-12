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
    bool CacheAvailable = false,
    string FailureStage = "",
    string FailureReason = "",
    string SourcePath = "",
    bool RequiresUserMapDataRepair = false);

public sealed record ServerListLoadResult(
    bool Success,
    bool Updated,
    bool UsedCache = false,
    bool CacheAvailable = false,
    string FailureStage = "",
    string FailureReason = "");

public sealed class DataDirectoryMigrationResult
{
    public bool CleanupCompleted { get; set; } = true;
    public bool AutomaticRetryAttempted { get; set; }
    public bool OldDirectoryRetained { get; set; }
    public int MigratedFileCount { get; set; }
    public string SourceDirectory { get; set; } = string.Empty;
    public string TargetDirectory { get; set; } = string.Empty;
    public string MigrationStateFilePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public List<string> PendingItems { get; set; } = [];
}

public sealed class DataDirectoryMigrationProgress
{
    public string StageName { get; set; } = string.Empty;
    public string CurrentOperation { get; set; } = string.Empty;
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; } = 1;

    public double Percent => TotalSteps <= 0
        ? 0
        : Math.Clamp((double)CompletedSteps / TotalSteps * 100, 0, 100);
}
public sealed class MapDataCache
{
    public string Version { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public DateTime LastSuccessfulSyncAt { get; set; } = DateTime.MinValue;

    [JsonIgnore]
    public Dictionary<ushort, string> MapNames { get; set; } = [];
}

public sealed class WayMarkFavoritesData
{
    public int Version { get; set; } = 1;
    public List<WayMarkFavorite> Favorites { get; set; } = [];
}

public sealed class WayMarkFavorite
{
    public string Id { get; set; } = string.Empty;
    public string CommentName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public WayMarkSnapshot Marker { get; set; } = new();

    [JsonIgnore]
    public ushort RegionID => Marker.RegionID;

    [JsonIgnore]
    public string RegionDisplayName => MapData.GetDisplayName(RegionID);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(CommentName)
        ? RegionDisplayName
        : CommentName;

    [JsonIgnore]
    public int EnabledPointCount => WayMarkSnapshotConverter.CountEnabledPoints(Marker);

    [JsonIgnore]
    public string Summary => $"{RegionDisplayName}，启用 {EnabledPointCount} 个标点";
}

public sealed class WayMarkSnapshot
{
    public ushort RegionID { get; set; }
    public byte EnableFlag { get; set; }
    public byte Unknown { get; set; }
    public int Timestamp { get; set; }
    public WayMarkPointSnapshot A { get; set; } = new();
    public WayMarkPointSnapshot B { get; set; } = new();
    public WayMarkPointSnapshot C { get; set; } = new();
    public WayMarkPointSnapshot D { get; set; } = new();
    public WayMarkPointSnapshot One { get; set; } = new();
    public WayMarkPointSnapshot Two { get; set; } = new();
    public WayMarkPointSnapshot Three { get; set; } = new();
    public WayMarkPointSnapshot Four { get; set; } = new();
}

public sealed class WayMarkPointSnapshot
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public enum StartupWayMarkAction
{
    None = 0,
    LoadMostRecentFile = 1,
    OpenFileDialog = 2
}

public enum StartupLocalCharacterScanMode
{
    EveryStartup = 0,
    FirstInitializationOnly = 1
}

public enum WayMarkFavoriteSaveMode
{
    Manual = 0,
    Auto = 1
}

public enum WayMarkOpenDirectoryMode
{
    Default = 0,
    GameCharacterDirectory = 1,
    CustomDirectory = 2
}

public enum MapDataMode
{
    Manual = 0,
    GameData = 1
}

public enum MapDataTableMode
{
    Automatic = 0,
    Manual = 1
}

public enum MapDataSource
{
    OnlineReference = 0,
    LocalGame = 1
}

public enum MapDataOnlineSourceKind
{
    ContentFinderConditionCsv = 0,
    DiemoeMatcha = 1
}

public enum UnknownMapIdPolicy
{
    RejectUnknown = 0,
    AllowUnknown = 1
}

public enum GameInstallDirectoryUpdateResult
{
    NotFound = 0,
    Unchanged = 1,
    Updated = 2,
    Relocated = 3
}

public sealed class AppSettings
{
    public const int DefaultMaxBackupCount = 100;
    public const int DefaultMaxBackupCountPerUser = 20;
    public const int DefaultMaxBackupDays = 90;
    public const int MinBackupCount = 1;
    public const int MaxBackupCountLimit = 10000;
    public const int MinBackupDays = 1;
    public const int MaxBackupDaysLimit = 3650;
    public const int DefaultMaxLogFileSizeMb = 5;
    public const int DefaultMaxLogFileCount = 5;
    public const int MinLogFileSizeMb = 1;
    public const int MaxLogFileSizeMbLimit = 100;
    public const int MinLogFileCount = 1;
    public const int MaxLogFileCountLimit = 20;

    public int MaxBackupCount { get; set; } = DefaultMaxBackupCount;
    public int MaxBackupCountPerUser { get; set; } = DefaultMaxBackupCountPerUser;
    public int MaxBackupDays { get; set; } = DefaultMaxBackupDays;
    public bool LimitBackupCount { get; set; } = true;
    public bool LimitBackupCountPerUser { get; set; } = true;
    public bool LimitBackupDays { get; set; } = true;
    public bool AutoBackupBeforeSave { get; set; } = true;
    public bool AutoBackupAfterLoad { get; set; } = false;
    public bool AutoBackupBeforeRestore { get; set; } = true;
    public int MaxLogFileSizeMb { get; set; } = DefaultMaxLogFileSizeMb;
    public int MaxLogFileCount { get; set; } = DefaultMaxLogFileCount;
    public bool UseWayMarkImageLabels { get; set; } = true;
    public StartupWayMarkAction StartupWayMarkAction { get; set; } = StartupWayMarkAction.None;
    public StartupLocalCharacterScanMode StartupLocalCharacterScanMode { get; set; } = StartupLocalCharacterScanMode.EveryStartup;
    public bool StartupLocalCharacterScanCompleted { get; set; }
    public WayMarkFavoriteSaveMode WayMarkFavoriteSaveMode { get; set; } = WayMarkFavoriteSaveMode.Manual;
    public MapDataTableMode MapDataTableMode { get; set; } = MapDataTableMode.Automatic;
    public bool MapDataTableModeInitialized { get; set; }
    public MapDataSource MapDataSource { get; set; } = MapDataSource.OnlineReference;
    public bool MapDataSourceInitialized { get; set; }
    public MapDataOnlineSourceKind MapDataOnlineSource { get; set; } = MapDataOnlineSourceKind.ContentFinderConditionCsv;
    public UnknownMapIdPolicy UnknownMapIdPolicy { get; set; } = UnknownMapIdPolicy.RejectUnknown;
    public bool ShowAllowUnknownMapIdPolicyWarning { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MapDataMode? MapDataMode { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MapDataModeInitialized { get; set; }
    public WayMarkOpenDirectoryMode WayMarkOpenDirectoryMode { get; set; } = WayMarkOpenDirectoryMode.Default;
    public bool WayMarkOpenDirectoryModeInitialized { get; set; }
    public string GameInstallDirectory { get; set; } = string.Empty;
    public string WayMarkCustomDirectory { get; set; } = string.Empty;
    public DateTime LastMapDataManualRefreshAttempt { get; set; } = DateTime.MinValue;
    public DateTime LastServerListManualRefreshAttempt { get; set; } = DateTime.MinValue;
    public WindowLayoutSettings WindowLayout { get; set; } = new();
    public List<string> RecentFiles { get; set; } = [];
}

public static class BackupCreationTriggers
{
    public const string BeforeSave = "BeforeSave";
    public const string AfterLoad = "AfterLoad";
    public const string BeforeRestore = "BeforeRestore";
    public const string AfterDeletedFileRecreate = "AfterDeletedFileRecreate";

    public static string ToDisplayText(string? creationTrigger)
    {
        return creationTrigger switch
        {
            BeforeSave => "保存前自动备份",
            AfterLoad => "读取后自动备份",
            BeforeRestore => "还原前安全备份",
            AfterDeletedFileRecreate => "已删除文件重建后备份",
            _ => "未记录"
        };
    }
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
    public double WayMarkFavoriteListRatio { get; set; } = 1d / 3d;
    public double WayMarkFavoriteEditorRatio { get; set; } = 1d / 3d;
    public double WayMarkFavoritePreviewRatio { get; set; } = 1d / 3d;
    public double WayMarkFavoritePickerLeft { get; set; }
    public double WayMarkFavoritePickerTop { get; set; }
    public double WayMarkFavoritePickerWidth { get; set; }
    public double WayMarkFavoritePickerHeight { get; set; }
    public double WayMarkFavoritePickerListRatio { get; set; } = 0.6;
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
    public DateTime? LastActiveAtUtc { get; set; }

    [JsonIgnore]
    public string LastActiveTimeDisplay { get; set; } = "尚未扫描";

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
    public DateTime LastSuccessfulSyncAt { get; set; } = DateTime.MinValue;
    public List<ServerGroup> Groups { get; set; } = [];

    public static ServerListCache CreateBuiltin()
    {
        return new ServerListCache
        {
            SourceUrl = "内置服务器列表",
            LastUpdated = DateTime.MinValue,
            LastSuccessfulSyncAt = DateTime.MinValue,
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
    public bool UseFolderUserIDAsEffectiveUserID { get; set; }
    public string CreationTrigger { get; set; } = string.Empty;
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
    public string EffectiveUserID => UseFolderUserIDAsEffectiveUserID && !string.IsNullOrWhiteSpace(FolderUserID)
        ? FolderUserID
        : FileUserID;

    [JsonIgnore]
    public string CreationTriggerDisplay => BackupCreationTriggers.ToDisplayText(CreationTrigger);

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
