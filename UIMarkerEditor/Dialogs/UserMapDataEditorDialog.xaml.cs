using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public partial class UserMapDataEditorDialog : Window
{
    private static readonly Encoding CsvEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly string filePath;
    private readonly ObservableCollection<UserMapDataEditorRow> rows = [];
    private readonly bool isReadOnly;
    private readonly Func<Window, bool> confirmDiscardChanges;
    private IReadOnlyList<UserMapDataEditorRowSnapshot> initialRows = [];
    private bool allowClose;

    public UserMapDataEditorDialog(string filePath)
        : this(filePath, ConfirmDiscardChanges)
    {
    }

    internal UserMapDataEditorDialog(string filePath, Func<Window, bool> confirmDiscardChanges)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("用户地图数据文件路径不能为空。", nameof(filePath));
        }

        this.filePath = filePath;
        this.confirmDiscardChanges = confirmDiscardChanges ?? throw new ArgumentNullException(nameof(confirmDiscardChanges));
        InitializeComponent();
        FilePath_TextBox.Text = filePath;
        MapDataRows_DataGrid.ItemsSource = rows;
        LoadRowsFromFile();
        initialRows = CaptureRows();
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
        confirmDiscardChanges = _ => true;
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
        initialRows = CaptureRows();
    }

    private void LoadRowsFromFile()
    {
        ClearRows();
        if (!File.Exists(filePath))
        {
            RefreshRowsValidation();
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
            RefreshRowsValidation();
            return;
        }

        LoadDiagnosticRows(MapDataTableCsv.DiagnoseSimpleMapDataCsv(csv));
    }

    private void LoadRows(IReadOnlyDictionary<ushort, string> mapNames)
    {
        ClearRows();
        foreach (KeyValuePair<ushort, string> pair in mapNames.OrderBy(pair => pair.Key))
        {
            AddRow(new UserMapDataEditorRow(pair.Key.ToString(CultureInfo.InvariantCulture), pair.Value));
        }

        RefreshRowsValidation();
    }

    private void LoadDiagnosticRows(MapDataTableCsvDiagnosticResult diagnosticResult)
    {
        ClearRows();
        foreach (MapDataTableCsvRow row in diagnosticResult.Rows)
        {
            AddRow(new UserMapDataEditorRow(row.MapIdText, row.Name));
        }

        ApplyIssues(diagnosticResult.Issues);
        UpdateValidationStatus();
    }

    private void Add_Button_Click(object sender, RoutedEventArgs e)
    {
        if (isReadOnly) return;

        UserMapDataEditorRow row = new();
        AddRow(row);
        MapDataRows_DataGrid.SelectedItem = row;
        MapDataRows_DataGrid.ScrollIntoView(row);
        MapDataRows_DataGrid.BeginEdit();
    }

    private void DeleteSelected_Button_Click(object sender, RoutedEventArgs e)
    {
        if (isReadOnly) return;

        List<UserMapDataEditorRow> selectedRows = MapDataRows_DataGrid.SelectedItems
            .OfType<UserMapDataEditorRow>()
            .ToList();
        foreach (UserMapDataEditorRow row in selectedRows)
        {
            row.PropertyChanged -= UserMapDataRow_PropertyChanged;
            rows.Remove(row);
        }

        RefreshRowsValidation();
    }

    private void Save_Button_Click(object sender, RoutedEventArgs e)
    {
        if (isReadOnly) return;

        MapDataRows_DataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        MapDataRows_DataGrid.CommitEdit(DataGridEditingUnit.Row, true);
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

        allowClose = true;
        DialogResult = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!isReadOnly && !allowClose && HasUnsavedChanges() && !confirmDiscardChanges(this))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private IReadOnlyList<UserMapDataEditorRowSnapshot> CaptureRows()
    {
        return [.. rows.Select(static row => new UserMapDataEditorRowSnapshot(row.MapId, row.Name))];
    }

    private bool HasUnsavedChanges()
    {
        return !initialRows.SequenceEqual(CaptureRows());
    }

    private static bool ConfirmDiscardChanges(Window owner)
    {
        return AppMessageBox.Show(
            owner,
            "用户地图数据还有未保存的修改。是否放弃这些修改并关闭编辑器？",
            "放弃未保存的修改",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private bool TryBuildMapData(out Dictionary<ushort, string> mapNames)
    {
        MapDataTableCsvDiagnosticResult diagnosticResult = RefreshRowsValidation();
        mapNames = new Dictionary<ushort, string>(diagnosticResult.MapNames);

        if (diagnosticResult.HasErrors)
        {
            SelectFirstInvalidRow();
            AppMessageBox.Show(this, BuildValidationErrorMessage(diagnosticResult), "格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (mapNames.Count == 0)
        {
            AppMessageBox.Show(this, "请至少填写一条地图数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void AddRow(UserMapDataEditorRow row)
    {
        row.PropertyChanged += UserMapDataRow_PropertyChanged;
        rows.Add(row);
    }

    private void ClearRows()
    {
        foreach (UserMapDataEditorRow row in rows)
        {
            row.PropertyChanged -= UserMapDataRow_PropertyChanged;
        }

        rows.Clear();
    }

    private void UserMapDataRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UserMapDataEditorRow.MapId) or nameof(UserMapDataEditorRow.Name))
        {
            RefreshRowsValidation();
        }
    }

    private MapDataTableCsvDiagnosticResult RefreshRowsValidation()
    {
        MapDataTableCsvDiagnosticResult diagnosticResult = MapDataTableCsv.DiagnoseSimpleMapDataRows(rows.Select((row, index) =>
            new MapDataTableCsvRow(index + 1, row.MapId, row.Name)));
        ApplyIssues(diagnosticResult.Issues);
        UpdateValidationStatus();
        return diagnosticResult;
    }

    private void ApplyIssues(IReadOnlyList<MapDataTableCsvIssue> issues)
    {
        foreach (UserMapDataEditorRow row in rows)
        {
            row.SetIssues([]);
        }

        foreach (IGrouping<int, MapDataTableCsvIssue> group in issues.GroupBy(static issue => issue.RowNumber))
        {
            int rowIndex = group.Key - 1;
            if (rowIndex < 0 || rowIndex >= rows.Count)
            {
                continue;
            }

            rows[rowIndex].SetIssues([.. group]);
        }
    }

    private void UpdateValidationStatus()
    {
        int errorCount = rows.Count(static row => row.HasError);
        int warningCount = rows.Count(static row => row.HasWarning);
        if (errorCount > 0)
        {
            ValidationStatus_TextBlock.Text = $"发现 {errorCount} 行需要修复。修复前不会保存文件。";
            ValidationStatus_TextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (warningCount > 0)
        {
            ValidationStatus_TextBlock.Text = $"发现 {warningCount} 行可自动规范化的问题，保存后会写成合法 CSV。";
            ValidationStatus_TextBlock.Visibility = Visibility.Visible;
            return;
        }

        ValidationStatus_TextBlock.Text = string.Empty;
        ValidationStatus_TextBlock.Visibility = Visibility.Collapsed;
    }

    private void SelectFirstInvalidRow()
    {
        UserMapDataEditorRow? firstInvalidRow = rows.FirstOrDefault(static row => row.HasError);
        if (firstInvalidRow == null)
        {
            return;
        }

        MapDataRows_DataGrid.SelectedItem = firstInvalidRow;
        MapDataRows_DataGrid.ScrollIntoView(firstInvalidRow);
    }

    private static string BuildValidationErrorMessage(MapDataTableCsvDiagnosticResult diagnosticResult)
    {
        IEnumerable<string> messages = diagnosticResult.Issues
            .Where(static issue => issue.Severity == MapDataTableCsvIssueSeverity.Error)
            .OrderBy(static issue => issue.RowNumber)
            .Take(5)
            .Select(static issue => $"第 {issue.RowNumber} 行：{issue.Message}");
        string message = string.Join(Environment.NewLine, messages);
        int extraCount = diagnosticResult.Issues.Count(static issue => issue.Severity == MapDataTableCsvIssueSeverity.Error) - 5;
        if (extraCount > 0)
        {
            message += $"{Environment.NewLine}另有 {extraCount} 个问题。";
        }

        return message;
    }
}

internal sealed record UserMapDataEditorRowSnapshot(string MapId, string Name);

internal sealed class UserMapDataEditorRow : INotifyPropertyChanged
{
    private string mapId = string.Empty;
    private string name = string.Empty;
    private IReadOnlyList<MapDataTableCsvIssue> issues = [];

    public UserMapDataEditorRow()
    {
    }

    public UserMapDataEditorRow(string mapId, string name)
    {
        this.mapId = mapId;
        this.name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MapId
    {
        get => mapId;
        set
        {
            if (mapId == value) return;
            mapId = value;
            OnPropertyChanged(nameof(MapId));
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (name == value) return;
            name = value;
            OnPropertyChanged(nameof(Name));
        }
    }

    public string IssueText => string.Join(Environment.NewLine, issues.Select(static issue => issue.Message));

    public bool HasError => issues.Any(static issue => issue.Severity == MapDataTableCsvIssueSeverity.Error);

    public bool HasWarning => !HasError && issues.Any(static issue => issue.Severity == MapDataTableCsvIssueSeverity.Warning);

    public void SetIssues(IReadOnlyList<MapDataTableCsvIssue> nextIssues)
    {
        issues = nextIssues;
        OnPropertyChanged(nameof(IssueText));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasWarning));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
