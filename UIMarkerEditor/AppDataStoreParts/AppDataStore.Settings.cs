using System;

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
        NormalizeSettings(nextSettings);
        EnsureDataDirectory();
        WriteJson(SettingsFilePath, nextSettings);
        Settings = nextSettings;
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

        NormalizeSettings(Settings);
    }

    private static void NormalizeSettings(AppSettings settings)
    {
        settings.WindowLayout ??= new WindowLayoutSettings();
        settings.RecentFiles ??= [];
        if (!Enum.IsDefined(settings.StartupWayMarkAction))
        {
            settings.StartupWayMarkAction = StartupWayMarkAction.None;
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
            UseWayMarkImageLabels = settings.UseWayMarkImageLabels,
            StartupWayMarkAction = settings.StartupWayMarkAction,
            LastMapDataManualRefreshAttempt = settings.LastMapDataManualRefreshAttempt,
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
            BackupListRatio = layout.BackupListRatio,
            CharacterListRatio = layout.CharacterListRatio
        };
    }
}
