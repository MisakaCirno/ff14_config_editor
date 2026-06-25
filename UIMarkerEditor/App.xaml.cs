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

        MapDataLoadResult mapDataLoadResult = await appDataStore.EnsureMapDataAvailableAsync();
        if (!mapDataLoadResult.Success)
        {
            AppMessageBox.Show(
                BuildRequiredMapDataFailureMessage(),
                "在线数据加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (mapDataLoadResult.UsedCache &&
            AppMessageBox.Show(
                BuildMapDataCacheModeConfirmMessage(),
                "使用本地缓存启动",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            Shutdown();
            return;
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

    private static string BuildRequiredMapDataFailureMessage()
    {
        return
            "无法获取必要的在线数据：地图数据。\n\n" +
            "本地也没有可用缓存，工具无法启动。请检查网络连接后重新打开工具。";
    }

    private static string BuildMapDataCacheModeConfirmMessage()
    {
        return
            "在线检查失败，但已找到本地地图数据缓存。\n\n" +
            "是否使用本地缓存启动？";
    }
}
