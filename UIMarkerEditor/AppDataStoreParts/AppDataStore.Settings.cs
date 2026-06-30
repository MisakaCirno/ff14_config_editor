using System;
using System.IO;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    public void SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settingsFileInvalid)
        {
            throw new InvalidOperationException("config.json 本次启动读取失败。为避免覆盖损坏文件，请先备份、删除或修复该文件后重启工具。");
        }

        AppSettings nextSettings = CloneSettings(settings);
        NormalizeSettingsForSave(nextSettings);
        ValidateSettingsForSave(nextSettings);
        EnsureDataDirectory();
        WriteJson(SettingsFilePath, nextSettings);
        Settings = nextSettings;
        ConfigureLoggerIfMigrationCleanupAllows();
    }

    public AppSettings CreateSettingsSnapshot()
    {
        return CloneSettings(Settings);
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

        NormalizeSettingsForLoad(Settings);
    }

    private static void NormalizeSettingsForLoad(AppSettings settings)
    {
        NormalizeSettingsReferences(settings);
        settings.GameInstallDirectory = NormalizeOptionalPath(settings.GameInstallDirectory);
        if (WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            settings.GameInstallDirectory,
            out string? normalizedGameInstallDirectory))
        {
            settings.GameInstallDirectory = normalizedGameInstallDirectory;
        }

        settings.WayMarkCustomDirectory = NormalizeOptionalPath(settings.WayMarkCustomDirectory);
        if (!Enum.IsDefined(settings.StartupWayMarkAction))
        {
            settings.StartupWayMarkAction = StartupWayMarkAction.None;
        }

        if (!Enum.IsDefined(settings.WayMarkFavoriteSaveMode))
        {
            settings.WayMarkFavoriteSaveMode = WayMarkFavoriteSaveMode.Manual;
        }

        MigrateLegacyMapDataMode(settings);

        if (!Enum.IsDefined(settings.MapDataTableMode))
        {
            settings.MapDataTableMode = MapDataTableMode.Automatic;
        }

        if (!Enum.IsDefined(settings.MapDataSource))
        {
            settings.MapDataSource = MapDataSource.OnlineReference;
        }

        if (!Enum.IsDefined(settings.MapDataOnlineSource))
        {
            settings.MapDataOnlineSource = MapDataOnlineSourceKind.ContentFinderConditionCsv;
        }

        if (!Enum.IsDefined(settings.UnknownMapIdPolicy))
        {
            settings.UnknownMapIdPolicy = UnknownMapIdPolicy.RejectUnknown;
        }

        if (!Enum.IsDefined(settings.WayMarkOpenDirectoryMode))
        {
            settings.WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.Default;
        }

        NormalizeWayMarkOpenDirectoryModeInitialized(settings);
        NormalizeMapDataSourceInitialized(settings);
        NormalizeMapDataTableModeInitialized(settings);

        settings.MaxBackupCount = NormalizeIntRange(
            settings.MaxBackupCount,
            AppSettings.MinBackupCount,
            AppSettings.MaxBackupCountLimit,
            AppSettings.DefaultMaxBackupCount);
        settings.MaxBackupCountPerUser = NormalizeIntRange(
            settings.MaxBackupCountPerUser,
            AppSettings.MinBackupCount,
            AppSettings.MaxBackupCountLimit,
            AppSettings.DefaultMaxBackupCountPerUser);
        settings.MaxBackupDays = NormalizeIntRange(
            settings.MaxBackupDays,
            AppSettings.MinBackupDays,
            AppSettings.MaxBackupDaysLimit,
            AppSettings.DefaultMaxBackupDays);
        settings.MaxLogFileSizeMb = NormalizeIntRange(
            settings.MaxLogFileSizeMb,
            AppSettings.MinLogFileSizeMb,
            AppSettings.MaxLogFileSizeMbLimit,
            AppSettings.DefaultMaxLogFileSizeMb);
        settings.MaxLogFileCount = NormalizeIntRange(
            settings.MaxLogFileCount,
            AppSettings.MinLogFileCount,
            AppSettings.MaxLogFileCountLimit,
            AppSettings.DefaultMaxLogFileCount);
    }

    private static void NormalizeSettingsReferences(AppSettings settings)
    {
        settings.WindowLayout ??= new WindowLayoutSettings();
        settings.RecentFiles ??= [];
        settings.GameInstallDirectory ??= string.Empty;
        settings.WayMarkCustomDirectory ??= string.Empty;
    }

    private static void NormalizeSettingsForSave(AppSettings settings)
    {
        NormalizeSettingsReferences(settings);
        MigrateLegacyMapDataMode(settings);
        settings.GameInstallDirectory = NormalizeOptionalPath(settings.GameInstallDirectory);
        if (WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            settings.GameInstallDirectory,
            out string? normalizedGameInstallDirectory))
        {
            settings.GameInstallDirectory = normalizedGameInstallDirectory;
        }

        settings.WayMarkCustomDirectory = NormalizeOptionalPath(settings.WayMarkCustomDirectory);
        if (!Enum.IsDefined(settings.MapDataTableMode))
        {
            settings.MapDataTableMode = MapDataTableMode.Automatic;
        }

        if (!Enum.IsDefined(settings.MapDataSource))
        {
            settings.MapDataSource = MapDataSource.OnlineReference;
        }

        if (!Enum.IsDefined(settings.MapDataOnlineSource))
        {
            settings.MapDataOnlineSource = MapDataOnlineSourceKind.ContentFinderConditionCsv;
        }

        NormalizeWayMarkOpenDirectoryModeInitialized(settings);
        NormalizeMapDataSourceInitialized(settings);
        NormalizeMapDataTableModeInitialized(settings);
    }

    private static void MigrateLegacyMapDataMode(AppSettings settings)
    {
        if (settings.MapDataMode is not MapDataMode legacyMode || !Enum.IsDefined(legacyMode))
        {
            return;
        }

        if (legacyMode == MapDataMode.GameData)
        {
            settings.MapDataTableMode = MapDataTableMode.Automatic;
            settings.MapDataSource = MapDataSource.LocalGame;
            settings.UnknownMapIdPolicy = UnknownMapIdPolicy.RejectUnknown;
            settings.MapDataTableModeInitialized = true;
            settings.MapDataSourceInitialized = true;
        }
        else
        {
            settings.MapDataTableMode = MapDataTableMode.Manual;
            settings.UnknownMapIdPolicy = UnknownMapIdPolicy.AllowUnknown;
            settings.MapDataTableModeInitialized = settings.MapDataModeInitialized;
        }

        settings.MapDataMode = null;
        settings.MapDataModeInitialized = false;
    }

    private static void NormalizeMapDataTableModeInitialized(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.MapDataTableMode))
        {
            return;
        }

        if (settings.MapDataTableMode == MapDataTableMode.Manual)
        {
            settings.MapDataTableModeInitialized = true;
            return;
        }

        if (settings.MapDataSourceInitialized)
        {
            settings.MapDataTableModeInitialized = true;
        }
    }

    private static void NormalizeMapDataSourceInitialized(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.MapDataSource))
        {
            return;
        }

        if (settings.MapDataSource == MapDataSource.LocalGame)
        {
            settings.MapDataSourceInitialized = true;
        }
    }

    private static void NormalizeWayMarkOpenDirectoryModeInitialized(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.WayMarkOpenDirectoryMode))
        {
            return;
        }

        if (settings.WayMarkOpenDirectoryMode != WayMarkOpenDirectoryMode.Default)
        {
            settings.WayMarkOpenDirectoryModeInitialized = true;
        }
    }

    private static void InitializeWayMarkOpenDirectoryModeForGameInstallDirectory(
        AppSettings settings,
        bool force)
    {
        if (!Enum.IsDefined(settings.WayMarkOpenDirectoryMode))
        {
            settings.WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.Default;
        }

        if (!force && settings.WayMarkOpenDirectoryModeInitialized)
        {
            return;
        }

        settings.WayMarkOpenDirectoryModeInitialized = true;
        if (settings.WayMarkOpenDirectoryMode == WayMarkOpenDirectoryMode.CustomDirectory)
        {
            return;
        }

        if (WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            settings.GameInstallDirectory,
            out _))
        {
            settings.WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.GameCharacterDirectory;
        }
    }

    private static string NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(PathTextRepair.RepairCommonUtf8Mojibake(path.Trim()));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.Trim();
        }
    }

    private static int NormalizeIntRange(int value, int min, int max, int defaultValue)
    {
        if (value < min)
        {
            return defaultValue;
        }

        return Math.Min(value, max);
    }

    private void EnsureSettingsFile()
    {
        if (settingsFileInvalid || File.Exists(SettingsFilePath))
        {
            return;
        }

        try
        {
            SaveSettings(Settings);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AddJsonReadWarning(
                SettingsFilePath,
                "默认工具设置无法保存，本次启动将继续使用内存中的默认设置。",
                ex);
        }
    }

    public async Task<GameInstallDirectoryUpdateResult> AutoDetectGameInstallDirectoryAsync()
    {
        if (HasValidGameInstallDirectory())
        {
            return GameInstallDirectoryUpdateResult.Unchanged;
        }

        string? detectedGameInstallDirectory = await Task.Run(TryDetectGameInstallDirectory);
        if (HasValidGameInstallDirectory() ||
            string.IsNullOrWhiteSpace(detectedGameInstallDirectory))
        {
            return GameInstallDirectoryUpdateResult.NotFound;
        }

        try
        {
            return SetGameInstallDirectoryFromDetectedPath(detectedGameInstallDirectory);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AddJsonReadWarning(
                SettingsFilePath,
                "游戏安装目录自动检测结果无法保存，本次启动将继续使用当前设置。",
                ex);
            return GameInstallDirectoryUpdateResult.NotFound;
        }
    }

    public GameInstallDirectoryUpdateResult SetGameInstallDirectoryFromRunningGameProcess()
    {
        string? detectedGameInstallDirectory = WayMarkOpenDirectoryResolver.DetectRunningGameInstallDirectory();
        return SetGameInstallDirectoryFromDetectedPath(detectedGameInstallDirectory);
    }

    public GameInstallDirectoryUpdateResult SetGameInstallDirectoryFromLoadedSaveFile(string filePath)
    {
        if (HasValidGameInstallDirectory())
        {
            return GameInstallDirectoryUpdateResult.Unchanged;
        }

        return WayMarkOpenDirectoryResolver.TryInferGameInstallDirectoryFromSaveFile(
            filePath,
            out string? detectedGameInstallDirectory)
                ? SetGameInstallDirectoryFromDetectedPath(detectedGameInstallDirectory)
                : GameInstallDirectoryUpdateResult.NotFound;
    }

    internal GameInstallDirectoryUpdateResult SetGameInstallDirectoryFromDetectedPath(string? detectedGameInstallDirectory)
    {
        if (!WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            detectedGameInstallDirectory,
            out string? normalizedGameInstallDirectory))
        {
            return GameInstallDirectoryUpdateResult.NotFound;
        }

        bool hadConfiguredGameInstallDirectory = !string.IsNullOrWhiteSpace(Settings.GameInstallDirectory);
        bool hadValidGameInstallDirectory = HasValidGameInstallDirectory();
        if (hadValidGameInstallDirectory &&
            string.Equals(normalizedGameInstallDirectory, Settings.GameInstallDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return GameInstallDirectoryUpdateResult.Unchanged;
        }

        bool isRelocatedGameInstallDirectory = hadConfiguredGameInstallDirectory && !hadValidGameInstallDirectory;
        AppSettings settings = CloneSettings(Settings);
        settings.GameInstallDirectory = normalizedGameInstallDirectory;
        if (!hadConfiguredGameInstallDirectory || isRelocatedGameInstallDirectory)
        {
            InitializeWayMarkOpenDirectoryModeForGameInstallDirectory(
                settings,
                force: isRelocatedGameInstallDirectory);
        }

        SaveSettings(settings);
        return isRelocatedGameInstallDirectory
            ? GameInstallDirectoryUpdateResult.Relocated
            : GameInstallDirectoryUpdateResult.Updated;
    }

    private bool HasValidGameInstallDirectory()
    {
        return WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(
            Settings.GameInstallDirectory,
            out _);
    }

    private string? TryDetectGameInstallDirectory()
    {
        try
        {
            return gameInstallDirectoryDetector();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ValidateSettingsForSave(AppSettings settings)
    {
        ValidateIntRange(settings.MaxBackupCount, "最多保留备份数量", AppSettings.MinBackupCount, AppSettings.MaxBackupCountLimit);
        ValidateIntRange(settings.MaxBackupCountPerUser, "每个玩家最多保留备份数量", AppSettings.MinBackupCount, AppSettings.MaxBackupCountLimit);
        ValidateIntRange(settings.MaxBackupDays, "最多保留备份天数", AppSettings.MinBackupDays, AppSettings.MaxBackupDaysLimit);
        ValidateIntRange(settings.MaxLogFileSizeMb, "日志文件大小", AppSettings.MinLogFileSizeMb, AppSettings.MaxLogFileSizeMbLimit);
        ValidateIntRange(settings.MaxLogFileCount, "日志文件最多保存数量", AppSettings.MinLogFileCount, AppSettings.MaxLogFileCountLimit);
        if (!Enum.IsDefined(settings.StartupWayMarkAction))
        {
            throw new InvalidOperationException("启动行为设置不是有效选项。");
        }

        if (!Enum.IsDefined(settings.WayMarkFavoriteSaveMode))
        {
            throw new InvalidOperationException("标点收藏保存方式不是有效选项。");
        }

        if (!Enum.IsDefined(settings.MapDataSource))
        {
            throw new InvalidOperationException("地图数据来源不是有效选项。");
        }

        if (!Enum.IsDefined(settings.MapDataOnlineSource))
        {
            throw new InvalidOperationException("在线地图数据来源不是有效选项。");
        }

        if (!Enum.IsDefined(settings.MapDataTableMode))
        {
            throw new InvalidOperationException("地图数据读取方式不是有效选项。");
        }

        if (!Enum.IsDefined(settings.UnknownMapIdPolicy))
        {
            throw new InvalidOperationException("未知地图 ID 策略不是有效选项。");
        }

        if (!Enum.IsDefined(settings.WayMarkOpenDirectoryMode))
        {
            throw new InvalidOperationException("标点文件打开目录设置不是有效选项。");
        }

        if (!string.IsNullOrWhiteSpace(settings.GameInstallDirectory) &&
            !WayMarkOpenDirectoryResolver.TryNormalizeGameInstallDirectory(settings.GameInstallDirectory, out _))
        {
            throw new InvalidOperationException("游戏安装目录无效。请选择包含 game 文件夹和 ffxiv_dx11.exe 或 ffxiv.exe 的最终幻想 XIV 安装目录。");
        }
    }

    private static void ValidateIntRange(int value, string displayName, int min, int max)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{displayName} 必须是 {min} 到 {max} 之间的整数。");
        }
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        return new AppSettings
        {
            MaxBackupCount = settings.MaxBackupCount,
            MaxBackupCountPerUser = settings.MaxBackupCountPerUser,
            MaxBackupDays = settings.MaxBackupDays,
            LimitBackupCount = settings.LimitBackupCount,
            LimitBackupCountPerUser = settings.LimitBackupCountPerUser,
            LimitBackupDays = settings.LimitBackupDays,
            AutoBackupBeforeSave = settings.AutoBackupBeforeSave,
            AutoBackupAfterLoad = settings.AutoBackupAfterLoad,
            AutoBackupBeforeRestore = settings.AutoBackupBeforeRestore,
            MaxLogFileSizeMb = settings.MaxLogFileSizeMb,
            MaxLogFileCount = settings.MaxLogFileCount,
            UseWayMarkImageLabels = settings.UseWayMarkImageLabels,
            StartupWayMarkAction = settings.StartupWayMarkAction,
            WayMarkFavoriteSaveMode = settings.WayMarkFavoriteSaveMode,
            MapDataTableMode = settings.MapDataTableMode,
            MapDataTableModeInitialized = settings.MapDataTableModeInitialized,
            MapDataSource = settings.MapDataSource,
            MapDataSourceInitialized = settings.MapDataSourceInitialized,
            MapDataOnlineSource = settings.MapDataOnlineSource,
            UnknownMapIdPolicy = settings.UnknownMapIdPolicy,
            MapDataMode = settings.MapDataMode,
            MapDataModeInitialized = settings.MapDataModeInitialized,
            WayMarkOpenDirectoryMode = settings.WayMarkOpenDirectoryMode,
            WayMarkOpenDirectoryModeInitialized = settings.WayMarkOpenDirectoryModeInitialized,
            GameInstallDirectory = settings.GameInstallDirectory,
            WayMarkCustomDirectory = settings.WayMarkCustomDirectory,
            LastMapDataManualRefreshAttempt = settings.LastMapDataManualRefreshAttempt,
            LastServerListManualRefreshAttempt = settings.LastServerListManualRefreshAttempt,
            WindowLayout = CloneWindowLayout(settings.WindowLayout),
            RecentFiles = settings.RecentFiles == null ? [] : [.. settings.RecentFiles]
        };
    }

    private static WindowLayoutSettings CloneWindowLayout(WindowLayoutSettings? layout)
    {
        if (layout == null)
        {
            return new WindowLayoutSettings();
        }

        return new WindowLayoutSettings
        {
            Left = layout.Left,
            Top = layout.Top,
            Width = layout.Width,
            Height = layout.Height,
            WindowState = layout.WindowState,
            WayMarkListRatio = layout.WayMarkListRatio,
            WayMarkEditorRatio = layout.WayMarkEditorRatio,
            WayMarkPreviewRatio = layout.WayMarkPreviewRatio,
            WayMarkFavoriteListRatio = layout.WayMarkFavoriteListRatio,
            WayMarkFavoriteEditorRatio = layout.WayMarkFavoriteEditorRatio,
            WayMarkFavoritePreviewRatio = layout.WayMarkFavoritePreviewRatio,
            WayMarkFavoritePickerLeft = layout.WayMarkFavoritePickerLeft,
            WayMarkFavoritePickerTop = layout.WayMarkFavoritePickerTop,
            WayMarkFavoritePickerWidth = layout.WayMarkFavoritePickerWidth,
            WayMarkFavoritePickerHeight = layout.WayMarkFavoritePickerHeight,
            WayMarkFavoritePickerListRatio = layout.WayMarkFavoritePickerListRatio,
            BackupListRatio = layout.BackupListRatio,
            CharacterListRatio = layout.CharacterListRatio
        };
    }
}
