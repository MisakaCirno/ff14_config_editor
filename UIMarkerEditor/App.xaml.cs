using System.Windows;
using System.Windows.Threading;
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

        StartupLoadingWindow? loadingWindow = null;
        try
        {
            StartupLoadingWindow startupLoadingWindow = ShowStartupLoadingWindow(BuildMapDataLoadingStatus(appDataStore));
            loadingWindow = startupLoadingWindow;
            await YieldForStartupLoadingWindowAsync();

            MapDataLoadResult mapDataLoadResult = await appDataStore.EnsureMapDataAvailableAsync();
            if (ShouldAddStartupMapDataLoadWarning(mapDataLoadResult))
            {
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
            }

            if (mapDataLoadResult.Updated)
            {
                string versionText = string.IsNullOrWhiteSpace(mapDataLoadResult.Version)
                    ? "未知版本"
                    : mapDataLoadResult.Version;
                AppMessageBox.Show(
                    startupLoadingWindow,
                    $"地图数据已更新并重新加载到版本：{versionText}",
                    "地图数据更新完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            startupLoadingWindow.SetStatus("正在加载主窗口...");
            await YieldForStartupLoadingWindowAsync();

            MainWindow mainWindow = new(appDataStore, mapDataLoadResult);
            MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            CloseStartupLoadingWindow(startupLoadingWindow);
            loadingWindow = null;

            if (activateMainWindowWhenReady)
            {
                activateMainWindowWhenReady = false;
                ActivateWindow(mainWindow);
            }
        }
        finally
        {
            CloseStartupLoadingWindow(loadingWindow);
        }
    }

    private static StartupLoadingWindow ShowStartupLoadingWindow(string status)
    {
        StartupLoadingWindow window = new();
        window.SetStatus(status);
        window.Show();
        return window;
    }

    private static void CloseStartupLoadingWindow(StartupLoadingWindow? window)
    {
        if (window == null)
        {
            return;
        }

        try
        {
            window.CloseAfterStartup();
        }
        catch (InvalidOperationException)
        {
            // 启动异常路径中窗口可能已经被 WPF 关闭，忽略即可。
        }
    }

    private static Task YieldForStartupLoadingWindowAsync()
    {
        return Current.Dispatcher.InvokeAsync(
            static () => { },
            DispatcherPriority.Background).Task;
    }

    private static string BuildMapDataLoadingStatus(AppDataStore appDataStore)
    {
        if (appDataStore.Settings.MapDataTableMode == MapDataTableMode.Manual)
        {
            return "正在读取用户维护的地图数据...";
        }

        return appDataStore.Settings.MapDataSource == MapDataSource.LocalGame
            ? "正在读取本地游戏地图数据..."
            : "正在检查在线地图数据...";
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
            if (dialog.SelectedSource == MapDataSource.OnlineReference)
            {
                settings.MapDataOnlineSource = dialog.SelectedOnlineSource;
            }
        }

        settings.UnknownMapIdPolicy = UnknownMapIdPolicy.RejectUnknown;
        appDataStore.SaveSettings(settings);
        return true;
    }

    private void RequestMainWindowActivation()
    {
        Dispatcher.Invoke(() =>
        {
            Window? activationTarget = SelectActivationTarget(MainWindow, Current.Windows);
            if (activationTarget == null)
            {
                activateMainWindowWhenReady = true;
                return;
            }

            ActivateWindow(activationTarget);
        });
    }

    internal static Window? SelectActivationTarget(Window? mainWindow, System.Collections.IEnumerable windows)
    {
        if (mainWindow != null)
        {
            return mainWindow;
        }

        Window? fallbackWindow = null;
        foreach (object? item in windows)
        {
            if (item is not Window window || !window.IsVisible)
            {
                continue;
            }

            if (window.IsActive)
            {
                return window;
            }

            fallbackWindow ??= window;
        }

        return fallbackWindow;
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

    internal static bool ShouldAddStartupMapDataLoadWarning(MapDataLoadResult result)
    {
        return !result.RequiresUserMapDataRepair &&
            (!result.Success || result.UsedCache);
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
