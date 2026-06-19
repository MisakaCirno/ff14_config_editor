using System.Windows;
using FF14ConfigEditor;

namespace UIMarkerEditor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

    private async Task StartApplicationAsync()
    {
        AppDataStore appDataStore = new();
        appDataStore.Initialize();

        MapDataLoadResult mapDataLoadResult = await appDataStore.EnsureMapDataAvailableAsync();
        ServerListLoadResult serverListLoadResult = await appDataStore.EnsureServerListAvailableAsync();
        if (!mapDataLoadResult.Success || !serverListLoadResult.Success)
        {
            AppMessageBox.Show(
                BuildRequiredOnlineDataFailureMessage(mapDataLoadResult, serverListLoadResult),
                "在线数据加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if ((mapDataLoadResult.UsedCache || serverListLoadResult.UsedCache) &&
            AppMessageBox.Show(
                BuildCacheModeConfirmMessage(mapDataLoadResult, serverListLoadResult),
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
                $"地图数据已更新到版本：{versionText}",
                "地图数据更新完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        MainWindow mainWindow = new(appDataStore);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static string BuildStartupFailureMessage(Exception exception)
    {
        if (exception is AppDataStoreException)
        {
            return $"工具本地数据初始化失败，无法继续启动。{Environment.NewLine}{Environment.NewLine}{exception.Message}";
        }

        return $"工具启动失败，无法继续运行。{Environment.NewLine}{Environment.NewLine}原因：{exception.Message}";
    }

    private static string BuildRequiredOnlineDataFailureMessage(
        MapDataLoadResult mapDataLoadResult,
        ServerListLoadResult serverListLoadResult)
    {
        List<string> failedItems = [];
        if (!mapDataLoadResult.Success)
        {
            failedItems.Add("地图数据");
        }

        if (!serverListLoadResult.Success)
        {
            failedItems.Add("服务器列表");
        }

        return
            $"无法获取必要的在线数据：{string.Join("、", failedItems)}。\n\n" +
            "本地也没有可用缓存，工具无法启动。请检查网络连接后重新打开工具。";
    }

    private static string BuildCacheModeConfirmMessage(
        MapDataLoadResult mapDataLoadResult,
        ServerListLoadResult serverListLoadResult)
    {
        List<string> cachedItems = [];
        if (mapDataLoadResult.UsedCache)
        {
            cachedItems.Add("地图数据");
        }

        if (serverListLoadResult.UsedCache)
        {
            cachedItems.Add("服务器列表");
        }

        return
            $"在线检查失败，但已找到本地缓存：{string.Join("、", cachedItems)}。\n\n" +
            "是否使用本地缓存启动？";
    }
}
