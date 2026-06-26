using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FF14LogParser;
using WinForms = System.Windows.Forms;

namespace FF14LogViewer;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LogRow> rows = [];
    private readonly ObservableCollection<string> errors = [];
    private CancellationTokenSource? operationCancellation;

    public MainWindow()
    {
        InitializeComponent();
        LogDataGrid.ItemsSource = rows;
        ErrorListBox.ItemsSource = errors;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择 FFXIV 角色 log 目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(DirectoryTextBox.Text) ? DirectoryTextBox.Text : string.Empty
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            DirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void LoadRecentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectory(out var directory)
            || !TryReadPositiveInt(RecentCountTextBox.Text, "最近条数", out var count)
            || !TryReadOptionalKind(out var kind))
        {
            return;
        }

        var options = CreateOptions(query: string.Empty, maxResults: count, kind) with
        {
            Direction = FF14LogSearchDirection.NewestFirst
        };
        await RunSearchAsync(directory, options);
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectory(out var directory) || !TryCreateSearchOptions(QueryTextBox.Text, out var options))
        {
            return;
        }

        await RunSearchAsync(directory, options);
    }

    private async void ShowAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectory(out var directory) || !TryCreateSearchOptions(string.Empty, out var options))
        {
            return;
        }

        await RunSearchAsync(directory, options);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => operationCancellation?.Cancel();

    private async Task RunSearchAsync(string directory, FF14LogSearchOptions options)
    {
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource();
        var cancellationToken = operationCancellation.Token;
        var capturedErrors = new List<FF14LogSearchError>();

        rows.Clear();
        errors.Clear();
        SetBusy(true);
        StatusTextBlock.Text = "正在读取...";

        var progress = new Progress<FF14LogSearchProgress>(progressInfo =>
        {
            var currentFile = string.IsNullOrEmpty(progressInfo.CurrentFilePath)
                ? string.Empty
                : $"，{Path.GetFileName(progressInfo.CurrentFilePath)}";
            StatusTextBlock.Text =
                $"已扫描 {progressInfo.ScannedFiles} 个文件、{progressInfo.ScannedEntries} 条，匹配 {progressInfo.MatchedEntries} 条{currentFile}";
        });

        try
        {
            var matches = await Task.Run(
                () => FF14LogSearcher.SearchDirectory(
                    directory,
                    options,
                    cancellationToken,
                    progress,
                    capturedErrors),
                cancellationToken);

            foreach (var row in matches.Select(LogRow.FromMatch))
            {
                rows.Add(row);
            }

            foreach (var error in capturedErrors)
            {
                errors.Add($"{Path.GetFileName(error.FilePath)}: {error.Message}");
            }

            StatusTextBlock.Text = $"完成，显示 {rows.Count} 条，错误 {errors.Count} 个";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "已取消";
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            StatusTextBlock.Text = "读取失败";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private FF14LogSearchOptions CreateOptions(string query, int? maxResults, int? kind)
    {
        var fields = FF14LogSearchFields.None;
        if (SearchSenderCheckBox.IsChecked == true)
        {
            fields |= FF14LogSearchFields.Sender;
        }

        if (SearchBodyCheckBox.IsChecked == true)
        {
            fields |= FF14LogSearchFields.Body;
        }

        return new FF14LogSearchOptions
        {
            Query = query,
            Fields = fields,
            Direction = NewestFirstCheckBox.IsChecked == true
                ? FF14LogSearchDirection.NewestFirst
                : FF14LogSearchDirection.OldestFirst,
            CaseSensitive = CaseSensitiveCheckBox.IsChecked == true,
            UseRegex = RegexCheckBox.IsChecked == true,
            Kind = kind,
            MaxResults = maxResults,
            ContinueOnError = true,
            ProgressInterval = 250
        };
    }

    private bool TryCreateSearchOptions(string query, out FF14LogSearchOptions options)
    {
        options = new FF14LogSearchOptions();
        if (!TryReadOptionalPositiveInt(MaxResultsTextBox.Text, "最多结果", out var maxResults))
        {
            return false;
        }

        if (!TryReadOptionalKind(out var kind))
        {
            return false;
        }

        options = CreateOptions(query, maxResults, kind);
        if (options.Fields == FF14LogSearchFields.None && !string.IsNullOrEmpty(query))
        {
            ShowInputError("至少选择一个搜索字段。");
            return false;
        }

        return true;
    }

    private bool TryGetDirectory(out string directory)
    {
        directory = DirectoryTextBox.Text.Trim();
        if (Directory.Exists(directory))
        {
            return true;
        }

        ShowInputError("日志目录不存在。");
        return false;
    }

    private bool TryReadPositiveInt(string value, string name, out int result)
    {
        if (int.TryParse(value.Trim(), out result) && result > 0)
        {
            return true;
        }

        ShowInputError($"{name}必须是大于 0 的整数。");
        return false;
    }

    private bool TryReadOptionalPositiveInt(string value, string name, out int? result)
    {
        result = null;
        var trimmed = value.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (int.TryParse(trimmed, out var parsed) && parsed > 0)
        {
            result = parsed;
            return true;
        }

        ShowInputError($"{name}必须为空或大于 0 的整数。");
        return false;
    }

    private bool TryReadOptionalKind(out int? result)
    {
        result = null;
        var trimmed = KindTextBox.Text.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return true;
        }

        if (int.TryParse(trimmed, out var parsed) && parsed is >= 0 and <= 127)
        {
            result = parsed;
            return true;
        }

        ShowInputError("Kind 必须为空或 0 到 127 的整数。");
        return false;
    }

    private void ShowInputError(string message)
    {
        errors.Clear();
        errors.Add(message);
        StatusTextBlock.Text = message;
    }

    private void SetBusy(bool isBusy)
    {
        BrowseButton.IsEnabled = !isBusy;
        LoadRecentButton.IsEnabled = !isBusy;
        SearchButton.IsEnabled = !isBusy;
        ShowAllButton.IsEnabled = !isBusy;
        CancelButton.IsEnabled = isBusy;
        ProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        ProgressBar.IsIndeterminate = isBusy;
    }

    private sealed class LogRow
    {
        public required string LocalTime { get; init; }

        public required int Kind { get; init; }

        public required string Sender { get; init; }

        public required string Body { get; init; }

        public required string FileName { get; init; }

        public required int EntryIndex { get; init; }

        public required string FilePath { get; init; }

        public static LogRow FromMatch(FF14LogSearchMatch match)
            => new()
            {
                LocalTime = match.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                Kind = match.Kind,
                Sender = match.Sender,
                Body = match.Body,
                FileName = Path.GetFileName(match.FilePath),
                EntryIndex = match.EntryIndex,
                FilePath = match.FilePath
            };
    }
}
