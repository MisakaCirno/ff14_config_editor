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
    private const string MapDataVersionFileName = "mapdata.version";
    private const string MapDataInstanceFileName = "instance.json";
    private const string MetadataFileName = "metadata.json";
    private const string BackupDataFileName = "UISAVE.DAT";
    private const string LogFileName = "app.log";
    private const string ServerListSourceUrl = "https://ff.web.sdo.com/web8/index.html#/servers";
    private const string ServerStatusApiUrl = "https://ff14act.web.sdo.com/api/serverStatus/getServerStatus";
    private const string MapDataVersionUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/data.version";
    private const string MapDataInstanceUrl = "https://cdn.diemoe.net/files/ACT.DieMoe/Resources/MatchaData/instance.json";

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly List<string> dataLoadWarnings = [];
    private readonly HashSet<string> dataLoadWarningKeys = [];
    private bool bootstrapFileInvalid;
    private bool settingsFileInvalid;
    private bool charactersFileInvalid;

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
    public string LogFilePath => Path.Combine(DataDirectory, "logs", LogFileName);

    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<CharacterProfile> Characters { get; } = [];
    public ServerListCache ServerList { get; private set; } = new();
    public string MapDataVersion { get; private set; } = string.Empty;

    public AppDataStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName))
    {
    }

    internal AppDataStore(string bootstrapDirectory)
    {
        if (string.IsNullOrWhiteSpace(bootstrapDirectory))
        {
            throw new ArgumentException("启动配置目录不能为空。", nameof(bootstrapDirectory));
        }

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
        ConfigureLogger();
        LoadSettings();
        LoadCharacters();
        LoadServerList();
    }

    public List<string> ConsumeDataLoadWarnings()
    {
        List<string> warnings = [.. dataLoadWarnings];
        dataLoadWarnings.Clear();
        dataLoadWarningKeys.Clear();
        return warnings;
    }
}
