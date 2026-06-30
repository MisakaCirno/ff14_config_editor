using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public partial class UserMapDataEditorDialog : Window
{
    private static readonly Encoding CsvEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly string filePath;
    private readonly ObservableCollection<UserMapDataRow> rows = [];
    private readonly bool isReadOnly;

    public UserMapDataEditorDialog(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("用户地图数据文件路径不能为空。", nameof(filePath));
        }

        this.filePath = filePath;
        InitializeComponent();
        FilePath_TextBox.Text = filePath;
        MapDataRows_DataGrid.ItemsSource = rows;
        LoadRowsFromFile();
    }

    public UserMapDataEditorDialog(
        string title,
        string description,
        IReadOnlyDictionary<ushort, string> mapNames,
        string sourceText)
    {
        ArgumentNullException.ThrowIfNull(mapNames);

        filePath = string.Empty;
        isReadOnly = true;
        InitializeComponent();
        Title = title;
        Description_TextBlock.Text = description;
        SourceLabel_TextBlock.Text = "来源";
        FilePath_TextBox.Text = string.IsNullOrWhiteSpace(sourceText) ? "当前地图快照" : sourceText;
        Add_Button.Visibility = Visibility.Collapsed;
        DeleteSelected_Button.Visibility = Visibility.Collapsed;
        Save_Button.Visibility = Visibility.Collapsed;
        Save_Button.IsDefault = false;
        Close_Button.Content = "关闭";
        Close_Button.IsDefault = true;
        MapDataRows_DataGrid.IsReadOnly = true;
        MapDataRows_DataGrid.ItemsSource = rows;
        LoadRows(mapNames);
    }

    private void LoadRowsFromFile()
    {
        rows.Clear();
        if (!File.Exists(filePath))
        {
            return;
        }

        string csv;
        try
        {
            csv = File.ReadAllText(filePath, CsvEncoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            AppMessageBox.Show(this, $"读取用户地图数据失败：{ex.Message}", "读取失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadRows(MapDataTableCsv.ParseSimpleMapDataCsv(csv));
    }

    private void LoadRows(IReadOnlyDictionary<ushort, string> mapNames)
    {
        rows.Clear();
        foreach (KeyValuePair<ushort, string> pair in mapNames.OrderBy(pair => pair.Key))
        {
            rows.Add(new UserMapDataRow(pair.Key.ToString(CultureInfo.InvariantCulture), pair.Value));
        }
    }

    private void Add_Button_Click(object sender, RoutedEventArgs e)
    {
        if (isReadOnly) return;

        UserMapDataRow row = new();
        rows.Add(row);
        MapDataRows_DataGrid.SelectedItem = row;
        MapDataRows_DataGrid.ScrollIntoView(row);
        MapDataRows_DataGrid.BeginEdit();
    }

    private void DeleteSelected_Button_Click(object sender, RoutedEventArgs e)
    {
        if (isReadOnly) return;

        List<UserMapDataRow> selectedRows = MapDataRows_DataGrid.SelectedItems
            .OfType<UserMapDataRow>()
            .ToList();
        foreach (UserMapDataRow row in selectedRows)
        {
            rows.Remove(row);
        }
    }

    private void Save_Button_Click(object sender, RoutedEventArgs e)
    {
        if (isReadOnly) return;

        if (!TryBuildMapData(out Dictionary<ushort, string> mapNames))
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SafeFileWriter.WriteAllText(filePath, MapDataTableCsv.Serialize(mapNames), CsvEncoding);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or AppDataStoreException)
        {
            AppMessageBox.Show(this, $"保存用户地图数据失败：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private bool TryBuildMapData(out Dictionary<ushort, string> mapNames)
    {
        mapNames = [];
        int rowNumber = 0;
        foreach (UserMapDataRow row in rows)
        {
            rowNumber++;
            string mapIdText = row.MapId.Trim();
            string name = row.Name.Trim();
            if (string.IsNullOrWhiteSpace(mapIdText) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!ushort.TryParse(mapIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort mapId) ||
                mapId == MapData.EmptyRegionId)
            {
                AppMessageBox.Show(this, $"第 {rowNumber} 行的地图 ID 无效。请输入 1 到 {ushort.MaxValue} 之间的整数。", "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                AppMessageBox.Show(this, $"第 {rowNumber} 行缺少地图名称。", "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!mapNames.TryAdd(mapId, name))
            {
                AppMessageBox.Show(this, $"地图 ID {mapId} 重复。", "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        if (mapNames.Count == 0)
        {
            AppMessageBox.Show(this, "请至少填写一条地图数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private sealed class UserMapDataRow
    {
        public UserMapDataRow()
        {
        }

        public UserMapDataRow(string mapId, string name)
        {
            MapId = mapId;
            Name = name;
        }

        public string MapId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }
}
