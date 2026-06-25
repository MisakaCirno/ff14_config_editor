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
    private Func<string, IProgress<DataDirectoryMigrationProgress>, Task<DataDirectoryMigrationResult>>? migrationOperation;
    private DataDirectoryMigrationResult? migrationResult;
    private Exception? migrationError;
    private string currentDataDirectory = string.Empty;
    private bool allowClose = true;

    public DataDirectoryMigrationReportDialog()
    {
        InitializeComponent();
        ShowProgressMode();
    }

    public DataDirectoryMigrationReportDialog(string currentDataDirectory, string targetDataDirectory)
    {
        InitializeComponent();
        this.currentDataDirectory = currentDataDirectory ?? string.Empty;
        ShowPreparationMode(this.currentDataDirectory, targetDataDirectory ?? string.Empty);
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

    public DataDirectoryMigrationResult? RunMigration(
        Func<string, IProgress<DataDirectoryMigrationProgress>, Task<DataDirectoryMigrationResult>> operation)
    {
        migrationOperation = operation ?? throw new ArgumentNullException(nameof(operation));
        migrationResult = null;
        migrationError = null;
        ShowDialog();
        if (migrationError != null)
        {
            ExceptionDispatchInfo.Capture(migrationError).Throw();
        }

        return migrationResult;
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

    private void ShowPreparationMode(string sourceDirectory, string targetDirectory)
    {
        Title = "工具数据目录迁移";
        Summary_TextBlock.Text = "确认工具数据目录迁移";
        Detail_TextBlock.Text = "请确认新的目录和迁移内容。点击“开始迁移”后，工具才会复制、校验并切换数据目录。";
        currentDataDirectory = NormalizeOptionalDirectory(sourceDirectory);
        CurrentDataDirectory_TextBox.Text = currentDataDirectory;
        TargetDataDirectory_TextBox.Text = NormalizeOptionalDirectory(targetDirectory);
        Preparation_Panel.Visibility = Visibility.Visible;
        Progress_Panel.Visibility = Visibility.Collapsed;
        ReportDetails_Grid.Visibility = Visibility.Collapsed;
        PendingItems_GroupBox.Visibility = Visibility.Collapsed;
        Buttons_Panel.Visibility = Visibility.Visible;
        StartMigration_Button.Visibility = Visibility.Visible;
        OpenSourceDirectory_Button.Visibility = Visibility.Collapsed;
        OpenTargetDirectory_Button.Visibility = Visibility.Collapsed;
        OpenStateDirectory_Button.Visibility = Visibility.Collapsed;
        CopyReport_Button.Visibility = Visibility.Collapsed;
        StartMigration_Button.IsDefault = true;
        Close_Button.IsDefault = false;
        Close_Button.Content = "取消";
    }

    private void ShowProgressMode()
    {
        Title = "工具数据目录迁移";
        Summary_TextBlock.Text = "正在迁移工具数据目录";
        Detail_TextBlock.Text = "工具正在复制、校验并清理旧目录中的受管数据。";
        Preparation_Panel.Visibility = Visibility.Collapsed;
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
        Title = "工具数据目录迁移";
        Preparation_Panel.Visibility = Visibility.Collapsed;
        Progress_Panel.Visibility = Visibility.Collapsed;
        ReportDetails_Grid.Visibility = Visibility.Visible;
        PendingItems_GroupBox.Visibility = Visibility.Visible;
        Buttons_Panel.Visibility = Visibility.Visible;
        StartMigration_Button.Visibility = Visibility.Collapsed;
        OpenSourceDirectory_Button.Visibility = Visibility.Visible;
        OpenTargetDirectory_Button.Visibility = Visibility.Visible;
        CopyReport_Button.Visibility = Visibility.Visible;
        StartMigration_Button.IsDefault = false;
        Close_Button.IsDefault = true;
        Close_Button.Content = "关闭";
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
        Title = "工具数据目录迁移";
        Summary_TextBlock.Text = "工具数据目录迁移失败";
        Detail_TextBlock.Text = $"迁移未完成，工具已保留原数据目录。原因：{exception.Message}";
        Preparation_Panel.Visibility = Visibility.Collapsed;
        Progress_Panel.Visibility = Visibility.Collapsed;
        ReportDetails_Grid.Visibility = Visibility.Collapsed;
        PendingItems_GroupBox.Visibility = Visibility.Collapsed;
        Buttons_Panel.Visibility = Visibility.Visible;
        StartMigration_Button.Visibility = Visibility.Collapsed;
        OpenSourceDirectory_Button.Visibility = Visibility.Collapsed;
        OpenTargetDirectory_Button.Visibility = Visibility.Collapsed;
        OpenStateDirectory_Button.Visibility = Visibility.Collapsed;
        CopyReport_Button.Visibility = Visibility.Collapsed;
        StartMigration_Button.IsDefault = false;
        Close_Button.IsDefault = true;
        Close_Button.Content = "关闭";
    }

    private void BrowseTargetDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        string targetDirectory = TargetDataDirectory_TextBox.Text.Trim();
        string initialDirectory = Directory.Exists(targetDirectory)
            ? targetDirectory
            : Directory.Exists(currentDataDirectory)
                ? currentDataDirectory
                : string.Empty;

        while (true)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new()
            {
                Title = "选择新的工具数据目录",
                InitialDirectory = initialDirectory
            };

            if (DialogOwnerHelper.ShowCommonDialog(dialog, this) != true)
            {
                return;
            }

            if (TryValidateTargetDirectory(
                currentDataDirectory,
                dialog.FolderName,
                out string targetFullPath,
                out string errorMessage))
            {
                TargetDataDirectory_TextBox.Text = targetFullPath;
                return;
            }

            AppMessageBox.Show(
                this,
                $"{errorMessage}{Environment.NewLine}{Environment.NewLine}请重新选择一个目录。",
                "工具数据目录迁移",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            if (Directory.Exists(dialog.FolderName))
            {
                initialDirectory = dialog.FolderName;
            }
        }
    }

    private async void StartMigration_Button_Click(object sender, RoutedEventArgs e)
    {
        if (migrationOperation == null) return;

        if (!TryValidateTargetDirectory(
            currentDataDirectory,
            TargetDataDirectory_TextBox.Text.Trim(),
            out string targetFullPath,
            out string errorMessage))
        {
            AppMessageBox.Show(this, errorMessage, "工具数据目录迁移", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        TargetDataDirectory_TextBox.Text = targetFullPath;

        allowClose = false;
        try
        {
            ShowProgressMode();
            Progress<DataDirectoryMigrationProgress> progress = new(UpdateProgress);
            migrationResult = await migrationOperation(targetFullPath, progress);
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

    internal static bool TryValidateTargetDirectory(
        string currentDataDirectory,
        string targetDirectory,
        out string targetFullPath,
        out string errorMessage)
    {
        targetFullPath = string.Empty;
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            errorMessage = "新的目录不能为空。";
            return false;
        }

        string currentFullPath;
        try
        {
            currentFullPath = NormalizeDataDirectoryPath(currentDataDirectory);
            targetFullPath = NormalizeDataDirectoryPath(targetDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errorMessage = $"新的目录路径无效：{ex.Message}";
            return false;
        }

        if (IsSameDirectory(currentFullPath, targetFullPath))
        {
            errorMessage = "新的目录与当前数据目录相同，无需迁移。";
            return false;
        }

        if (IsRootDataDirectory(targetFullPath))
        {
            errorMessage = "新的目录不能是磁盘根目录或共享根目录。";
            return false;
        }

        if (IsSubdirectoryOf(targetFullPath, currentFullPath))
        {
            errorMessage = "新的目录不能位于当前数据目录内部。";
            return false;
        }

        if (IsSubdirectoryOf(currentFullPath, targetFullPath))
        {
            errorMessage = "新的目录不能包含当前数据目录。";
            return false;
        }

        try
        {
            if (Directory.Exists(targetFullPath) &&
                Directory.EnumerateFileSystemEntries(targetFullPath).Any())
            {
                errorMessage = "新的目录必须为空。";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            errorMessage = $"无法检查新的目录：{ex.Message}";
            return false;
        }

        return true;
    }

    private string GetMigrationStateDirectory()
    {
        return result == null
            ? string.Empty
            : Path.GetDirectoryName(result.MigrationStateFilePath) ?? string.Empty;
    }

    private static bool IsSameDirectory(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            string normalizedLeft = NormalizeDataDirectoryPath(left);
            string normalizedRight = NormalizeDataDirectoryPath(right);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsRootDataDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string? root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrWhiteSpace(root) &&
            string.Equals(
                NormalizeDataDirectoryPath(fullPath),
                NormalizeDataDirectoryPath(root),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSubdirectoryOf(string candidateDirectory, string parentDirectory)
    {
        string candidateFullPath = NormalizeDataDirectoryPath(candidateDirectory);
        string parentFullPath = NormalizeDataDirectoryPath(parentDirectory);
        return candidateFullPath.StartsWith(parentFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOptionalDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        try
        {
            return NormalizeDataDirectoryPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return directory.Trim();
        }
    }

    private static string NormalizeDataDirectoryPath(string directory)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
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
