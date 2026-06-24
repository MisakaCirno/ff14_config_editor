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
        NormalizeSettingsReferences(nextSettings);
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
        settings.WayMarkCustomDirectory = NormalizeOptionalDirectoryPath(settings.WayMarkCustomDirectory);
        if (!Enum.IsDefined(settings.StartupWayMarkAction))
        {
            settings.StartupWayMarkAction = StartupWayMarkAction.None;
        }

        if (!Enum.IsDefined(settings.WayMarkFavoriteSaveMode))
        {
            settings.WayMarkFavoriteSaveMode = WayMarkFavoriteSaveMode.Manual;
        }

        if (!Enum.IsDefined(settings.WayMarkOpenDirectoryMode))
        {
            settings.WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.Default;
        }

        settings.MaxBackupCount = NormalizeIntRange(
            settings.MaxBackupCount,
            AppSettings.MinBackupCount,
            AppSettings.MaxBackupCountLimit,
            AppSettings.DefaultMaxBackupCount);
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
        settings.WayMarkCustomDirectory ??= string.Empty;
    }

    private static string NormalizeOptionalDirectoryPath(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(directory.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return directory.Trim();
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

    public async Task<bool> AutoFillWayMarkCustomDirectoryAsync()
    {
        if (Settings.WayMarkCustomDirectoryAutoFillAttempted)
        {
            return false;
        }

        string? detectedDirectory = await Task.Run(TryDetectWayMarkCustomDirectory);
        if (Settings.WayMarkCustomDirectoryAutoFillAttempted)
        {
            return false;
        }

        AppSettings settings = CloneSettings(Settings);
        settings.WayMarkCustomDirectoryAutoFillAttempted = true;
        bool updatedDirectory = false;
        if (!string.IsNullOrWhiteSpace(detectedDirectory) &&
            string.IsNullOrWhiteSpace(settings.WayMarkCustomDirectory))
        {
            settings.WayMarkCustomDirectory = detectedDirectory;
            updatedDirectory = true;
        }

        try
        {
            SaveSettings(settings);
            return updatedDirectory;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AddJsonReadWarning(
                SettingsFilePath,
                "自定义路径自动填充结果无法保存，本次启动将继续使用当前设置。",
                ex);
            return false;
        }
    }

    private string? TryDetectWayMarkCustomDirectory()
    {
        try
        {
            return wayMarkCustomDirectoryDetector();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ValidateSettingsForSave(AppSettings settings)
    {
        ValidateIntRange(settings.MaxBackupCount, "最多保留备份数量", AppSettings.MinBackupCount, AppSettings.MaxBackupCountLimit);
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

        if (!Enum.IsDefined(settings.WayMarkOpenDirectoryMode))
        {
            throw new InvalidOperationException("标点文件打开目录设置不是有效选项。");
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
            MaxBackupDays = settings.MaxBackupDays,
            LimitBackupCount = settings.LimitBackupCount,
            LimitBackupDays = settings.LimitBackupDays,
            AutoBackupBeforeSave = settings.AutoBackupBeforeSave,
            AutoBackupAfterLoad = settings.AutoBackupAfterLoad,
            MaxLogFileSizeMb = settings.MaxLogFileSizeMb,
            MaxLogFileCount = settings.MaxLogFileCount,
            UseWayMarkImageLabels = settings.UseWayMarkImageLabels,
            StartupWayMarkAction = settings.StartupWayMarkAction,
            WayMarkFavoriteSaveMode = settings.WayMarkFavoriteSaveMode,
            WayMarkOpenDirectoryMode = settings.WayMarkOpenDirectoryMode,
            WayMarkCustomDirectory = settings.WayMarkCustomDirectory,
            WayMarkCustomDirectoryAutoFillAttempted = settings.WayMarkCustomDirectoryAutoFillAttempted,
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
