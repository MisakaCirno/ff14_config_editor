using System.Windows;

namespace UIMarkerEditor;

public partial class MapDataSourceStartupDialog : Window
{
    public MapDataTableMode SelectedTableMode { get; private set; } = MapDataTableMode.Automatic;
    public MapDataSource SelectedSource { get; private set; } = MapDataSource.OnlineReference;
    public MapDataOnlineSourceKind SelectedOnlineSource { get; private set; } = MapDataOnlineSourceKind.ContentFinderConditionCsv;

    public MapDataSourceStartupDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowPlacementHelper.ConstrainToCurrentWorkArea(this);
    }

    private void GitHubOnlineReference_Button_Click(object sender, RoutedEventArgs e)
    {
        SelectOnlineSource(MapDataOnlineSourceKind.ContentFinderConditionCsv);
    }

    private void DiemoeOnlineReference_Button_Click(object sender, RoutedEventArgs e)
    {
        SelectOnlineSource(MapDataOnlineSourceKind.DiemoeMatcha);
    }

    private void LocalGame_Button_Click(object sender, RoutedEventArgs e)
    {
        if (AppMessageBox.Show(
            this,
            "使用本地游戏数据后，工具会读取 FFXIV 安装目录下的 game\\sqpack 数据文件，用于解析地图名称和地图 ID 列表。此操作不会修改游戏文件，但属于稍有敏感的本地游戏资源文件读取行为。\n\n是否继续？",
            "确认读取游戏文件",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        SelectAutomaticSource(MapDataSource.LocalGame);
    }

    private void ManualCsv_Button_Click(object sender, RoutedEventArgs e)
    {
        SelectedTableMode = MapDataTableMode.Manual;
        DialogResult = true;
    }

    private void SelectAutomaticSource(MapDataSource source)
    {
        SelectedTableMode = MapDataTableMode.Automatic;
        SelectedSource = source;
        DialogResult = true;
    }

    private void SelectOnlineSource(MapDataOnlineSourceKind onlineSource)
    {
        SelectedOnlineSource = onlineSource;
        SelectAutomaticSource(MapDataSource.OnlineReference);
    }
}
