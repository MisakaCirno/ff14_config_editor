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
    private const string AppFolderName = "FFXIVConfigEditor";
    private const string BootstrapFileName = "bootstrap.json";
    private const string SettingsFileName = "config.json";
    private const string CharactersFileName = "characters.json";
    private const string ServersFileName = "servers.json";
    private const string MapDataCacheFileName = "mapdata.csv";
    private const string MapDataCacheMetadataFileName = "mapdata.meta.json";
    private const string UserMapDataFileName = "mapdata_user.csv";
    private const string WayMarkFavoritesFileName = "waymark-favorites.json";
    private const string MetadataFileName = "metadata.json";
    private const string BackupDataFileName = "UISAVE.DAT";
    private const string LogFileName = "app.log";
    private const string ConfigsFolderName = "configs";
    private const string BackupsFolderName = "backups";
    private const string CacheFolderName = "cache";
    private const string LogsFolderName = "logs";
    private const string MigrationStateFileName = "migration-state.json";

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly IAppDataNetworkClient networkClient;
    private readonly Func<string?> gameInstallDirectoryDetector;
    private readonly ILocalGameMapDataProvider localGameMapDataProvider;
    private readonly List<string> dataLoadWarnings = [];
    private readonly HashSet<string> dataLoadWarningKeys = [];
    private readonly List<DataDirectoryMigrationResult> migrationReports = [];
    private bool bootstrapFileInvalid;
    private bool settingsFileInvalid;
    private bool charactersFileInvalid;
    private bool wayMarkFavoritesFileInvalid;
    private bool migrationCleanupPending;

    public string BootstrapDirectory { get; }
    public string BootstrapFilePath => Path.Combine(BootstrapDirectory, BootstrapFileName);
    public string MigrationStateFilePath => Path.Combine(BootstrapDirectory, MigrationStateFileName);
    public string DefaultDataDirectory => Path.Combine(BootstrapDirectory, "Data");
    public string DataDirectory { get; private set; }
    public string ConfigsDirectory => Path.Combine(DataDirectory, ConfigsFolderName);
    public string BackupsDirectory => Path.Combine(DataDirectory, BackupsFolderName);
    public string CacheDirectory => Path.Combine(DataDirectory, CacheFolderName);
    public string SettingsFilePath => Path.Combine(ConfigsDirectory, SettingsFileName);
    public string CharactersFilePath => Path.Combine(ConfigsDirectory, CharactersFileName);
    public string ServersFilePath => Path.Combine(CacheDirectory, ServersFileName);
    public string MapDataCacheFilePath => Path.Combine(CacheDirectory, MapDataCacheFileName);
    public string MapDataCacheMetadataFilePath => Path.Combine(CacheDirectory, MapDataCacheMetadataFileName);
    public string UserMapDataFilePath => Path.Combine(CacheDirectory, UserMapDataFileName);
    public string WayMarkFavoritesFilePath => Path.Combine(ConfigsDirectory, WayMarkFavoritesFileName);
    public string LogDirectory => Path.Combine(DataDirectory, LogsFolderName);
    public string LogFilePath => Path.Combine(LogDirectory, LogFileName);

    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<CharacterProfile> Characters { get; } = [];
    public ObservableCollection<WayMarkFavorite> WayMarkFavorites { get; } = [];
    public ServerListCache ServerList { get; private set; } = new();
    public string MapDataVersion { get; private set; } = string.Empty;
    public string MapDataSourcePath { get; private set; } = string.Empty;
    public DateTime MapDataLastUpdated { get; private set; } = DateTime.MinValue;
    public DateTime MapDataLastSuccessfulSyncAt { get; private set; } = DateTime.MinValue;
    public string MapDataContentSourceText => string.IsNullOrWhiteSpace(MapDataSourcePath)
        ? GetDefaultMapDataContentSourceText()
        : MapDataSourcePath;

    public AppDataStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName))
    {
    }

    internal AppDataStore(string bootstrapDirectory)
        : this(bootstrapDirectory, new HttpAppDataNetworkClient())
    {
    }

    internal AppDataStore(string bootstrapDirectory, Func<string?> gameInstallDirectoryDetector)
        : this(bootstrapDirectory, new HttpAppDataNetworkClient(), gameInstallDirectoryDetector)
    {
    }

    internal AppDataStore(
        string bootstrapDirectory,
        IAppDataNetworkClient networkClient,
        Func<string?>? gameInstallDirectoryDetector = null,
        ILocalGameMapDataProvider? localGameMapDataProvider = null)
    {
        if (string.IsNullOrWhiteSpace(bootstrapDirectory))
        {
            throw new ArgumentException("启动配置目录不能为空。", nameof(bootstrapDirectory));
        }

        this.networkClient = networkClient ?? throw new ArgumentNullException(nameof(networkClient));
        this.gameInstallDirectoryDetector = gameInstallDirectoryDetector ?? WayMarkOpenDirectoryResolver.AutoDetectGameInstallDirectory;
        this.localGameMapDataProvider = localGameMapDataProvider ?? new LocalGameMapDataProvider();
        BootstrapDirectory = Path.GetFullPath(bootstrapDirectory);
        DataDirectory = DefaultDataDirectory;
    }

    public void Initialize()
    {
        try
        {
            Directory.CreateDirectory(BootstrapDirectory);
        }
        catch (Exception ex)
        {
            throw new AppDataStoreException("准备本地启动配置目录", BootstrapDirectory, ex);
        }

        JsonFileReadResult<BootstrapSettings> bootstrapResult = ReadJsonFile<BootstrapSettings>(BootstrapFilePath);
        BootstrapSettings? bootstrap = null;
        bootstrapFileInvalid = false;
        if (bootstrapResult.Status == JsonFileReadStatus.Success)
        {
            bootstrap = bootstrapResult.Value;
        }
        else if (bootstrapResult.Status == JsonFileReadStatus.Invalid)
        {
            bootstrapFileInvalid = true;
            AddJsonReadWarning(
                BootstrapFilePath,
                "启动配置无法读取，已改用默认数据目录。原文件已保留，请手动修复或删除后重启工具。",
                bootstrapResult.Error);
        }

        DataDirectory = !string.IsNullOrWhiteSpace(bootstrap?.DataDirectory)
            ? bootstrap.DataDirectory
            : DefaultDataDirectory;
        if (IsRootDataDirectory(DataDirectory))
        {
            AddDataLoadWarning(
                $"data-directory-root:{DataDirectory}",
                $"启动配置指向磁盘根目录或共享根目录，已改用默认数据目录。{Environment.NewLine}" +
                $"配置的数据目录：{DataDirectory}{Environment.NewLine}" +
                $"默认数据目录：{DefaultDataDirectory}");
            DataDirectory = DefaultDataDirectory;
        }

        RecoverInterruptedDataDirectoryMigration();

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
        EnsureSettingsFile();
        ConfigureLoggerIfMigrationCleanupAllows();
        LoadCharacters();
        LoadWayMarkFavorites();
        LoadServerList();
    }

    public List<string> ConsumeDataLoadWarnings()
    {
        List<string> warnings = [.. dataLoadWarnings];
        dataLoadWarnings.Clear();
        dataLoadWarningKeys.Clear();
        return warnings;
    }

    public List<DataDirectoryMigrationResult> ConsumeMigrationReports()
    {
        List<DataDirectoryMigrationResult> reports = [.. migrationReports];
        migrationReports.Clear();
        return reports;
    }

    private string GetDefaultMapDataContentSourceText()
    {
        if (Settings.MapDataTableMode == MapDataTableMode.Manual)
        {
            return UserMapDataFilePath;
        }

        return Settings.MapDataSource == MapDataSource.LocalGame
            ? "未定位到本地 sqpack"
            : FormatMapDataOnlineSourceSummary();
    }

    private string FormatMapDataOnlineSourceSummary()
    {
        return CreateMapDataOnlineSourceDefinition(Settings.MapDataOnlineSource).DisplayName;
    }
}
