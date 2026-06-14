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
        Settings.WindowLayout ??= new WindowLayoutSettings();
    }

    private void LoadCharacters()
    {
        Characters.Clear();
        foreach (CharacterProfile profile in ReadJson<List<CharacterProfile>>(CharactersFilePath) ?? [])
        {
            Characters.Add(profile);
        }
    }

    private void SaveBootstrap()
    {
        WriteJson(BootstrapFilePath, new BootstrapSettings { DataDirectory = DataDirectory });
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
