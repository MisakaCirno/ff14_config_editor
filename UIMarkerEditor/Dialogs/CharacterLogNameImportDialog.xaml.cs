using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace UIMarkerEditor;

public partial class CharacterLogNameImportDialog : Window
{
    private readonly ObservableCollection<CharacterLogNameImportItem> importItems;

    public CharacterLogNameImportDialog(
        IEnumerable<CharacterLogNameImportItem> importItems,
        int unchangedCount,
        int errorCount)
    {
        InitializeComponent();

        this.importItems = new ObservableCollection<CharacterLogNameImportItem>(importItems);
        ImportItems_DataGrid.ItemsSource = this.importItems;
        Summary_TextBlock.Text = BuildSummaryText(this.importItems, unchangedCount, errorCount);
    }

    public IReadOnlyList<CharacterLogNameImportItem> SelectedItems
        => importItems.Where(static item => item.IsSelected).ToArray();

    private static string BuildSummaryText(
        IReadOnlyCollection<CharacterLogNameImportItem> importItems,
        int unchangedCount,
        int errorCount)
    {
        int conflictCount = importItems.Count(static item => item.IsConflict);
        int safeCount = importItems.Count - conflictCount;
        return
            $"从客户端日志中找到 {importItems.Count} 个可处理的昵称；" +
            $"{safeCount} 个可直接应用，{conflictCount} 个与现有角色名冲突，{unchangedCount} 个已一致。" +
            (errorCount > 0 ? $" 另有 {errorCount} 个日志文件或目录读取失败，已跳过。" : string.Empty);
    }

    private void SelectSafeItems_Button_Click(object sender, RoutedEventArgs e)
    {
        foreach (CharacterLogNameImportItem item in importItems)
        {
            item.IsSelected = !item.IsConflict;
        }

        ImportItems_DataGrid.Items.Refresh();
    }

    private void ClearSelection_Button_Click(object sender, RoutedEventArgs e)
    {
        foreach (CharacterLogNameImportItem item in importItems)
        {
            item.IsSelected = false;
        }

        ImportItems_DataGrid.Items.Refresh();
    }

    private void Ok_Button_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItems.Count == 0)
        {
            AppMessageBox.Show(this, "请至少选择一个要应用的日志昵称。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }
}

public sealed class CharacterLogNameImportItem
{
    public required string UserID { get; init; }

    public required string CurrentCharacterName { get; init; }

    public required string LogCharacterName { get; init; }

    public required string JobName { get; init; }

    public required ClientLogCharacterNameSource Source { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string LogFilePath { get; init; }

    public required int EntryIndex { get; init; }

    public required bool HasExistingProfile { get; init; }

    public required bool IsConflict { get; init; }

    public bool IsSelected { get; set; }

    public string CurrentCharacterNameDisplay
        => string.IsNullOrWhiteSpace(CurrentCharacterName) ? "（空）" : CurrentCharacterName;

    public string JobNameDisplay
        => string.IsNullOrWhiteSpace(JobName) ? "-" : JobName;

    public string SourceText
        => Source switch
        {
            ClientLogCharacterNameSource.JobChange => "职业切换",
            ClientLogCharacterNameSource.ChatSender => "聊天发送者",
            _ => "未知"
        };

    public string TimeDisplay
        => Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    public string StatusText
        => IsConflict
            ? "冲突"
            : HasExistingProfile
                ? "补全"
                : "新增";
}
