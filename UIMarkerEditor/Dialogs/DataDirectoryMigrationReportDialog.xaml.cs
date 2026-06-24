using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;

namespace UIMarkerEditor;

public partial class DataDirectoryMigrationReportDialog : Window
{
    private DataDirectoryMigrationResult? result;
    private bool allowClose = true;

    public DataDirectoryMigrationReportDialog()
    {
        InitializeComponent();
        ShowProgressMode();
    }

    public DataDirectoryMigrationReportDialog(DataDirectoryMigrationResult result)
    {
        InitializeComponent();
        LoadReport(result ?? throw new ArgumentNullException(nameof(result)));
    }

    public DataDirectoryMigrationResult RunMigration(
        Func<IProgress<DataDirectoryMigrationProgress>, Task<DataDirectoryMigrationResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        DataDirectoryMigrationResult? migrationResult = null;
        Exception? migrationError = null;
        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            allowClose = false;
            try
            {
                Progress<DataDirectoryMigrationProgress> progress = new(UpdateProgress);
                migrationResult = await operation(progress);
                allowClose = true;
                LoadReport(migrationResult);
            }
            catch (Exception ex)
            {
                migrationError = ex;
                allowClose = true;
                LoadFailure(ex);
            }
        }

        Loaded += OnLoaded;
        ShowDialog();
        if (migrationError != null)
        {
            ExceptionDispatchInfo.Capture(migrationError).Throw();
        }

        return migrationResult ?? throw new InvalidOperationException("迁移未完成。");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void ShowProgressMode()
    {
        Title = "正在迁移工具数据目录";
        Summary_TextBlock.Text = "正在迁移工具数据目录";
        Detail_TextBlock.Text = "工具正在复制、校验并清理旧目录中的受管数据。";
        Progress_Panel.Visibility = Visibility.Visible;
        ReportDetails_Grid.Visibility = Visibility.Collapsed;
        PendingItems_GroupBox.Visibility = Visibility.Collapsed;
        Buttons_Panel.Visibility = Visibility.Collapsed;
        UpdateProgress(new DataDirectoryMigrationProgress
        {
            StageName = "准备迁移",
            CurrentOperation = "准备迁移工具数据目录。",
            CompletedSteps = 0,
            TotalSteps = 1
        });
    }

    private void UpdateProgress(DataDirectoryMigrationProgress progress)
    {
        ProgressStage_TextBlock.Text = string.IsNullOrWhiteSpace(progress.StageName)
            ? "正在迁移"
            : progress.StageName;
        ProgressOperation_TextBlock.Text = progress.CurrentOperation;
        Migration_ProgressBar.Value = progress.Percent;
        ProgressPercent_TextBlock.Text = $"{progress.Percent:0}%";
    }

    private void LoadReport(DataDirectoryMigrationResult result)
    {
        this.result = result;
        Title = "工具数据目录迁移结果";
        Progress_Panel.Visibility = Visibility.Collapsed;
        ReportDetails_Grid.Visibility = Visibility.Visible;
        PendingItems_GroupBox.Visibility = Visibility.Visible;
        Buttons_Panel.Visibility = Visibility.Visible;
        OpenSourceDirectory_Button.Visibility = Visibility.Visible;
        OpenTargetDirectory_Button.Visibility = Visibility.Visible;
        CopyReport_Button.Visibility = Visibility.Visible;
        bool showMigrationState = ShouldShowMigrationState(result);
        Visibility migrationStateVisibility = showMigrationState
            ? Visibility.Visible
            : Visibility.Collapsed;
        MigrationStateFilePath_Label.Visibility = migrationStateVisibility;
        MigrationStateFilePath_TextBox.Visibility = migrationStateVisibility;
        OpenStateDirectory_Button.Visibility = migrationStateVisibility;

        Summary_TextBlock.Text = result.CleanupCompleted
            ? result.OldDirectoryRetained
                ? $"工具数据目录迁移已完成，共迁移 {result.MigratedFileCount} 个文件；旧目录仍保留非本工具管理的内容。"
                : result.AutomaticRetryAttempted
                    ? $"工具已自动恢复并完成上次数据目录迁移，共迁移 {result.MigratedFileCount} 个文件。"
                    : $"工具数据目录迁移已完成，共迁移 {result.MigratedFileCount} 个文件。"
            : result.AutomaticRetryAttempted
                ? $"工具已自动重试清理旧目录，但仍有受管文件未完全清理。已迁移 {result.MigratedFileCount} 个文件。"
                : $"工具数据已迁移到新目录，共迁移 {result.MigratedFileCount} 个文件，但旧目录中仍有受管文件未完全清理。";

        Detail_TextBlock.Text = result.CleanupCompleted
            ? result.OldDirectoryRetained
                ? "迁移状态文件已清理，当前工具数据目录可以正常使用。旧目录中的非本工具管理内容不会由工具处理，请确认后手动处理。"
                : "迁移状态文件已清理，当前工具数据目录可以正常使用。"
            : result.AutomaticRetryAttempted
                ? "新目录已经是当前工具数据目录。工具仍会在下次启动时根据迁移状态文件尝试清理未完成的受管文件。"
                : "新目录已经是当前工具数据目录。工具会在下次启动时根据迁移状态文件再次尝试清理未完成的受管文件。";

        SourceDirectory_TextBox.Text = result.SourceDirectory;
        TargetDirectory_TextBox.Text = result.TargetDirectory;
        MigrationStateFilePath_TextBox.Text = result.MigrationStateFilePath;
        Error_TextBlock.Text = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? string.Empty
            : $"原因：{result.ErrorMessage}";
        PendingItems_ListBox.ItemsSource = result.PendingItems.Count == 0
            ? ["无"]
            : result.PendingItems;

        OpenSourceDirectory_Button.IsEnabled = Directory.Exists(result.SourceDirectory);
        OpenTargetDirectory_Button.IsEnabled = Directory.Exists(result.TargetDirectory);
        OpenStateDirectory_Button.IsEnabled = showMigrationState && Directory.Exists(GetMigrationStateDirectory());
    }

    private void LoadFailure(Exception exception)
    {
        result = null;
        Title = "工具数据目录迁移失败";
        Summary_TextBlock.Text = "工具数据目录迁移失败";
        Detail_TextBlock.Text = $"迁移未完成，工具已保留原数据目录。原因：{exception.Message}";
        Progress_Panel.Visibility = Visibility.Collapsed;
        ReportDetails_Grid.Visibility = Visibility.Collapsed;
        PendingItems_GroupBox.Visibility = Visibility.Collapsed;
        Buttons_Panel.Visibility = Visibility.Visible;
        OpenSourceDirectory_Button.Visibility = Visibility.Collapsed;
        OpenTargetDirectory_Button.Visibility = Visibility.Collapsed;
        OpenStateDirectory_Button.Visibility = Visibility.Collapsed;
        CopyReport_Button.Visibility = Visibility.Collapsed;
    }

    private void OpenSourceDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (result == null) return;

        OpenExistingDirectory(result.SourceDirectory);
    }

    private void OpenTargetDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (result == null) return;

        OpenExistingDirectory(result.TargetDirectory);
    }

    private void OpenStateDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        OpenExistingDirectory(GetMigrationStateDirectory());
    }

    private void CopyReport_Button_Click(object sender, RoutedEventArgs e)
    {
        if (result == null) return;

        try
        {
            Clipboard.SetText(CreateReportText());
            ToastService.ShowSuccess("迁移信息已复制。");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(this, $"复制迁移信息失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string CreateReportText()
    {
        if (result == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        builder.AppendLine(Summary_TextBlock.Text);
        builder.AppendLine($"自动重试：{(result.AutomaticRetryAttempted ? "是" : "否")}");
        builder.AppendLine($"旧目录保留：{(result.OldDirectoryRetained ? "是" : "否")}");
        builder.AppendLine($"迁移文件数：{result.MigratedFileCount}");
        builder.AppendLine($"旧目录：{result.SourceDirectory}");
        builder.AppendLine($"新目录：{result.TargetDirectory}");
        if (ShouldShowMigrationState(result))
        {
            builder.AppendLine($"状态文件：{result.MigrationStateFilePath}");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            builder.AppendLine($"原因：{result.ErrorMessage}");
        }

        builder.AppendLine("尚未清理的项目：");
        if (result.PendingItems.Count == 0)
        {
            builder.AppendLine("无");
        }
        else
        {
            foreach (string item in result.PendingItems)
            {
                builder.AppendLine(item);
            }
        }

        return builder.ToString();
    }

    private static bool ShouldShowMigrationState(DataDirectoryMigrationResult result)
    {
        return !result.CleanupCompleted && !string.IsNullOrWhiteSpace(result.MigrationStateFilePath);
    }

    private string GetMigrationStateDirectory()
    {
        return result == null
            ? string.Empty
            : Path.GetDirectoryName(result.MigrationStateFilePath) ?? string.Empty;
    }

    private void OpenExistingDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            AppMessageBox.Show(this, "目录不存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        using Process? _ = Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }
}
