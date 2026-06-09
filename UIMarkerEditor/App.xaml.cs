using System.Windows;

namespace UIMarkerEditor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDataStore appDataStore = new();
        appDataStore.Initialize();

        MapDataLoadResult mapDataLoadResult = await appDataStore.EnsureMapDataAvailableAsync();
        if (!mapDataLoadResult.Success)
        {
            MessageBox.Show(
                "地图数据获取失败，工具无法启动。\n请检查网络连接后重新打开工具。",
                "地图数据加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (mapDataLoadResult.Updated)
        {
            string versionText = string.IsNullOrWhiteSpace(mapDataLoadResult.Version)
                ? "未知版本"
                : mapDataLoadResult.Version;
            MessageBox.Show(
                $"地图数据已更新到版本：{versionText}",
                "地图数据更新完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        MainWindow mainWindow = new(appDataStore);
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
