using System.Windows;
using FF14ConfigEditor;

namespace UIMarkerEditor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? singleInstanceService;
    private bool activateMainWindowWhenReady;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        singleInstanceService = SingleInstanceService.Create();
        if (!singleInstanceService.IsFirstInstance)
        {
            if (!SingleInstanceService.NotifyFirstInstance())
            {
                AppMessageBox.Show(
                    "FF14 标点预设编辑工具已经在运行，请使用已打开的窗口。",
                    "工具已在运行",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        singleInstanceService.StartActivationListener(
            RequestMainWindowActivation,
            ex => AppLogger.Warning(AppLogCategory.IO, "处理二次启动唤起请求失败", ex));

        try
        {
            await StartApplicationAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Error(AppLogCategory.IO, "工具启动失败", ex);
            AppMessageBox.Show(
                BuildStartupFailureMessage(ex),
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        singleInstanceService?.Dispose();
        singleInstanceService = null;
        base.OnExit(e);
    }

    private async Task StartApplicationAsync()
    {
        AppDataStore appDataStore = new();
        appDataStore.Initialize();

        if (!EnsureMapDataTableSelected(appDataStore))
        {
            Shutdown();
            return;
        }

        MapDataLoadResult mapDataLoadResult = await appDataStore.EnsureMapDataAvailableAsync();
        if (!mapDataLoadResult.Success)
        {
            appDataStore.AddDataLoadWarning(
                "map-data-unavailable",
                BuildMapDataUnavailableWarningMessage(mapDataLoadResult));
        }
        else if (mapDataLoadResult.UsedCache)
        {
            appDataStore.AddDataLoadWarning(
                "map-data-cache-fallback",
                BuildMapDataCacheFallbackWarningMessage(mapDataLoadResult));
        }

        if (mapDataLoadResult.Updated)
        {
            string versionText = string.IsNullOrWhiteSpace(mapDataLoadResult.Version)
                ? "未知版本"
                : mapDataLoadResult.Version;
            AppMessageBox.Show(
                $"地图数据已更新并重新加载到版本：{versionText}",
                "地图数据更新完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        MainWindow mainWindow = new(appDataStore);
        MainWindow = mainWindow;
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        if (activateMainWindowWhenReady)
        {
            activateMainWindowWhenReady = false;
            ActivateWindow(mainWindow);
        }
    }

    private static bool EnsureMapDataTableSelected(AppDataStore appDataStore)
    {
        if (appDataStore.Settings.MapDataTableModeInitialized)
        {
            return true;
        }

        MapDataSourceStartupDialog dialog = new();
        bool? dialogResult = dialog.ShowDialog();
        if (dialogResult != true)
        {
            return false;
        }

        AppSettings settings = appDataStore.CreateSettingsSnapshot();
        settings.MapDataTableMode = dialog.SelectedTableMode;
        settings.MapDataTableModeInitialized = true;
        if (dialog.SelectedTableMode == MapDataTableMode.Automatic)
        {
            settings.MapDataSource = dialog.SelectedSource;
            settings.MapDataSourceInitialized = true;
        }

        settings.UnknownMapIdPolicy = UnknownMapIdPolicy.RejectUnknown;
        appDataStore.SaveSettings(settings);
        return true;
    }

    private void RequestMainWindowActivation()
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow == null)
            {
                activateMainWindowWhenReady = true;
                return;
            }

            ActivateWindow(MainWindow);
        });
    }

    private static void ActivateWindow(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private static string BuildStartupFailureMessage(Exception exception)
    {
        if (exception is AppDataStoreException)
        {
            return $"工具本地数据初始化失败，无法继续启动。{Environment.NewLine}{Environment.NewLine}{exception.Message}";
        }

        return $"工具启动失败，无法继续运行。{Environment.NewLine}{Environment.NewLine}原因：{exception.Message}";
    }

    private static string BuildMapDataUnavailableWarningMessage(MapDataLoadResult result)
    {
        return
            "无法加载地图数据，工具已继续启动。\n\n" +
            $"原因：{BuildMapDataFailureReasonText(result)}\n\n" +
            "当前没有可用地图区域快照，区域选择和剪贴板导入校验会受限。可在设置中重新读取地图数据，或开启“允许未知地图 ID”后自行确认地图 ID。";
    }

    private static string BuildMapDataCacheFallbackWarningMessage(MapDataLoadResult result)
    {
        return
            "无法读取当前来源的地图数据，工具已使用缓存快照继续启动。\n\n" +
            $"原因：{BuildMapDataFailureReasonText(result)}\n\n" +
            $"区域列表可能不是最新，未覆盖的地图名称可能显示为“{MapData.UnavailableRegionName}”。";
    }

    private static string BuildMapDataFailureReasonText(MapDataLoadResult result)
    {
        string reason = string.IsNullOrWhiteSpace(result.FailureReason)
            ? "未知原因。"
            : result.FailureReason;
        return string.IsNullOrWhiteSpace(result.FailureStage)
            ? reason
            : $"{result.FailureStage}失败：{reason}";
    }
}
