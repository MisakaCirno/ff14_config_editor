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
    private enum JsonFileReadStatus
    {
        Missing,
        Success,
        Invalid
    }

    private sealed record JsonFileReadResult<T>(
        JsonFileReadStatus Status,
        T? Value = default,
        Exception? Error = null);

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

    public AppSettings Settings { get; private set; } = new();
    public ObservableCollection<CharacterProfile> Characters { get; } = [];
    public ServerListCache ServerList { get; private set; } = new();
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

    public void SaveSettings(AppSettings settings)
    {
        if (settingsFileInvalid)
        {
            throw new InvalidOperationException("config.json 本次启动读取失败。为避免覆盖损坏文件，请先备份、删除或修复该文件后重启工具。");
        }

        NormalizeSettings(settings);
        Settings = settings;
        EnsureDataDirectory();
        WriteJson(SettingsFilePath, Settings);
    }

    public void SaveCharacters()
    {
        if (charactersFileInvalid)
        {
            throw new InvalidOperationException("characters.json 本次启动读取失败。为避免覆盖损坏文件，请先备份、删除或修复该文件后重启工具。");
        }

        EnsureDataDirectory();
        WriteJson(CharactersFilePath, Characters.OrderBy(c => c.UserID, StringComparer.OrdinalIgnoreCase).ToList());
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
            ServerList = new ServerListCache();
        }

        DataDirectory = targetDirectory;
        EnsureDataDirectory();
        SaveBootstrap(allowOverwriteInvalid: true);
        LoadSettings();
        LoadCharacters();
        LoadServerList();
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
        if (!charactersFileInvalid)
        {
            SaveCharacters();
        }

        return profile;
    }

    public string GetCharacterDisplayName(string? userID)
    {
        if (string.IsNullOrWhiteSpace(userID)) return "未知角色";

        CharacterProfile? profile = Characters.FirstOrDefault(c =>
            string.Equals(c.UserID, userID, StringComparison.OrdinalIgnoreCase));
        return profile?.DisplayName ?? userID;
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

    }

    private void LoadSettings()
    {
        settingsFileInvalid = false;
        JsonFileReadResult<AppSettings> settingsResult = ReadJsonFile<AppSettings>(SettingsFilePath);
        if (settingsResult.Status == JsonFileReadStatus.Success && settingsResult.Value != null)
        {
            Settings = settingsResult.Value;
        }
        else
        {
            Settings = new AppSettings();
            if (settingsResult.Status == JsonFileReadStatus.Invalid)
            {
                settingsFileInvalid = true;
                AddJsonReadWarning(
                    SettingsFilePath,
                    "工具设置无法读取，已使用默认设置。为避免覆盖损坏文件，本次运行会阻止保存设置。",
                    settingsResult.Error);
            }
        }

        NormalizeSettings(Settings);
    }

    private void LoadCharacters()
    {
        Characters.Clear();
        charactersFileInvalid = false;
        JsonFileReadResult<List<CharacterProfile>> charactersResult = ReadJsonFile<List<CharacterProfile>>(CharactersFilePath);
        if (charactersResult.Status == JsonFileReadStatus.Invalid)
        {
            charactersFileInvalid = true;
            AddJsonReadWarning(
                CharactersFilePath,
                "角色备注无法读取，列表已留空。为避免覆盖损坏文件，本次运行会阻止保存角色备注。",
                charactersResult.Error);
            return;
        }

        foreach (CharacterProfile? profile in charactersResult.Value ?? [])
        {
            if (profile != null)
            {
                Characters.Add(profile);
            }
        }
    }

    private void SaveBootstrap(bool allowOverwriteInvalid = false)
    {
        if (bootstrapFileInvalid && !allowOverwriteInvalid)
        {
            return;
        }

        WriteJson(BootstrapFilePath, new BootstrapSettings { DataDirectory = DataDirectory });
        bootstrapFileInvalid = false;
    }

    private JsonFileReadResult<T> ReadJsonFile<T>(string path)
    {
        if (!File.Exists(path)) return new JsonFileReadResult<T>(JsonFileReadStatus.Missing);

        try
        {
            string json = File.ReadAllText(path);
            T? value = JsonSerializer.Deserialize<T>(json, jsonOptions);
            return value == null
                ? new JsonFileReadResult<T>(
                    JsonFileReadStatus.Invalid,
                    Error: new JsonException("JSON 内容为空或类型不匹配。"))
                : new JsonFileReadResult<T>(JsonFileReadStatus.Success, value);
        }
        catch (Exception ex)
        {
            return new JsonFileReadResult<T>(JsonFileReadStatus.Invalid, Error: ex);
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
        SafeFileWriter.WriteAllText(path, json);
    }

    private static void WriteText(string path, string value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SafeFileWriter.WriteAllText(path, value);
    }

    private void AddJsonReadWarning(string path, string message, Exception? error)
    {
        string warningKey = $"json:{Path.GetFullPath(path)}";
        if (!dataLoadWarningKeys.Add(warningKey)) return;

        string detail = error == null ? string.Empty : $"{Environment.NewLine}原因：{error.Message}";
        dataLoadWarnings.Add($"{message}{Environment.NewLine}文件：{path}{detail}");
    }

    private static void NormalizeSettings(AppSettings settings)
    {
        settings.WindowLayout ??= new WindowLayoutSettings();
        settings.RecentFiles ??= [];
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

            SafeFileWriter.Copy(file, targetFile);
        }
    }

}
