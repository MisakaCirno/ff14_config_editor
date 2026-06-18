using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UIMarkerEditor.Controls;

public partial class ToolSettingsControl : UserControl
{
    private static readonly TimeSpan ManualRefreshCooldown = TimeSpan.FromMinutes(5);
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private bool isUpdatingNavigationFromScroll;
    private Action refreshBackupList = () => { };
    private Action refreshCharacterList = () => { };
    private Action refreshServerListConsumers = () => { };
    private Action refreshMapDataConsumers = () => { };
    private Action refreshAppearance = () => { };

    public ToolSettingsControl()
    {
        InitializeComponent();
        SettingsNavigation_ListBox.SelectedIndex = 0;
    }

    public void Initialize(
        AppDataStore appDataStore,
        Window ownerWindow,
        Action refreshBackupList,
        Action refreshCharacterList,
        Action refreshServerListConsumers,
        Action refreshMapDataConsumers,
        Action refreshAppearance)
    {
        this.appDataStore = appDataStore;
        this.ownerWindow = ownerWindow;
        this.refreshBackupList = refreshBackupList;
        this.refreshCharacterList = refreshCharacterList;
        this.refreshServerListConsumers = refreshServerListConsumers;
        this.refreshMapDataConsumers = refreshMapDataConsumers;
        this.refreshAppearance = refreshAppearance;
    }

    public void LoadSettingsIntoUi()
    {
        if (appDataStore == null) return;

        DataDirectory_TextBox.Text = appDataStore.DataDirectory;
        UpdateCurrentLogFilePathText();
        MaxBackupCount_TextBox.Text = appDataStore.Settings.MaxBackupCount.ToString(CultureInfo.InvariantCulture);
        MaxBackupDays_TextBox.Text = appDataStore.Settings.MaxBackupDays.ToString(CultureInfo.InvariantCulture);
        MaxLogFileSizeMb_TextBox.Text = appDataStore.Settings.MaxLogFileSizeMb.ToString(CultureInfo.InvariantCulture);
        MaxLogFileCount_TextBox.Text = appDataStore.Settings.MaxLogFileCount.ToString(CultureInfo.InvariantCulture);
        AutoBackup_CheckBox.IsChecked = appDataStore.Settings.AutoBackupBeforeSave;
        WayMarkLabelDisplayMode_SegmentedSwitch.IsLeftSelected = appDataStore.Settings.UseWayMarkImageLabels;
        LimitBackupCount_CheckBox.IsChecked = appDataStore.Settings.LimitBackupCount;
        LimitBackupDays_CheckBox.IsChecked = appDataStore.Settings.LimitBackupDays;
        ApplyStartupWayMarkActionToUi(appDataStore.Settings.StartupWayMarkAction);
        UpdateBackupLimitInputState();
        RefreshStatusFields();
    }

    public void RefreshOnlineDataStatus()
    {
        RefreshStatusFields();
    }

    private void SettingsNavigation_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingNavigationFromScroll) return;
        if (SettingsNavigation_ListBox.SelectedItem is not ListBoxItem { Tag: string sectionName }) return;
        if (FindName(sectionName) is not FrameworkElement section) return;

        section.BringIntoView();
    }

    private void SettingsContent_ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateNavigationSelectionFromScroll();
    }

    private void UpdateNavigationSelectionFromScroll()
    {
        ListBoxItem? activeItem = null;
        foreach (ListBoxItem item in SettingsNavigation_ListBox.Items.OfType<ListBoxItem>())
        {
            if (item.Tag is not string sectionName) continue;
            if (FindName(sectionName) is not FrameworkElement section) continue;

            double sectionTop = section.TransformToAncestor(SettingsContent_ScrollViewer).Transform(new Point(0, 0)).Y;
            if (sectionTop <= 24)
            {
                activeItem = item;
                continue;
            }

            activeItem ??= item;
            break;
        }

        if (activeItem == null || ReferenceEquals(activeItem, SettingsNavigation_ListBox.SelectedItem)) return;

        isUpdatingNavigationFromScroll = true;
        try
        {
            SettingsNavigation_ListBox.SelectedItem = activeItem;
        }
        finally
        {
            isUpdatingNavigationFromScroll = false;
        }
    }

    private void BrowseDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        Microsoft.Win32.OpenFolderDialog dialog = new()
        {
            Title = "选择配置目录",
            InitialDirectory = Directory.Exists(DataDirectory_TextBox.Text) ? DataDirectory_TextBox.Text : appDataStore.DataDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            DataDirectory_TextBox.Text = dialog.FolderName;
        }
    }

    private void BackupLimit_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateBackupLimitInputState();
    }

    private void AutoBackup_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateBackupLimitInputState();
    }

    private void SaveSettings_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        bool autoBackupBeforeSave = AutoBackup_CheckBox.IsChecked == true;
        bool limitBackupCount = LimitBackupCount_CheckBox.IsChecked == true;
        bool limitBackupDays = LimitBackupDays_CheckBox.IsChecked == true;
        int maxBackupCount = appDataStore.Settings.MaxBackupCount;
        int maxBackupDays = appDataStore.Settings.MaxBackupDays;
        int maxLogFileSizeMb = appDataStore.Settings.MaxLogFileSizeMb;
        int maxLogFileCount = appDataStore.Settings.MaxLogFileCount;
        if (!TryReadIntInRange(
                MaxBackupCount_TextBox,
                "最多保留备份数量",
                AppSettings.MinBackupCount,
                AppSettings.MaxBackupCountLimit,
                out maxBackupCount) ||
            !TryReadIntInRange(
                MaxBackupDays_TextBox,
                "最多保留备份天数",
                AppSettings.MinBackupDays,
                AppSettings.MaxBackupDaysLimit,
                out maxBackupDays) ||
            !TryReadIntInRange(
                MaxLogFileSizeMb_TextBox,
                "日志文件大小",
                AppSettings.MinLogFileSizeMb,
                AppSettings.MaxLogFileSizeMbLimit,
                out maxLogFileSizeMb) ||
            !TryReadIntInRange(
                MaxLogFileCount_TextBox,
                "日志文件最多保存数量",
                AppSettings.MinLogFileCount,
                AppSettings.MaxLogFileCountLimit,
                out maxLogFileCount))
        {
            return;
        }

        try
        {
            string requestedDataDirectory = DataDirectory_TextBox.Text.Trim();
            if (!string.Equals(requestedDataDirectory, appDataStore.DataDirectory, StringComparison.OrdinalIgnoreCase))
            {
                MessageBoxResult migrateResult = MessageBox.Show(
                    ownerWindow,
                    "是否将现有配置、角色备注和备份迁移到新目录？\n选择“否”将只切换目录，不复制旧数据。",
                    "迁移配置目录",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (migrateResult == MessageBoxResult.Cancel) return;

                appDataStore.ChangeDataDirectory(requestedDataDirectory, migrateResult == MessageBoxResult.Yes);
            }

            appDataStore.SaveSettings(new AppSettings
            {
                MaxBackupCount = maxBackupCount,
                MaxBackupDays = maxBackupDays,
                LimitBackupCount = limitBackupCount,
                LimitBackupDays = limitBackupDays,
                AutoBackupBeforeSave = autoBackupBeforeSave,
                MaxLogFileSizeMb = maxLogFileSizeMb,
                MaxLogFileCount = maxLogFileCount,
                UseWayMarkImageLabels = WayMarkLabelDisplayMode_SegmentedSwitch.IsLeftSelected,
                StartupWayMarkAction = ReadStartupWayMarkActionFromUi(),
                LastMapDataManualRefreshAttempt = appDataStore.Settings.LastMapDataManualRefreshAttempt,
                LastServerListManualRefreshAttempt = appDataStore.Settings.LastServerListManualRefreshAttempt,
                WindowLayout = appDataStore.Settings.WindowLayout,
                RecentFiles = [.. appDataStore.Settings.RecentFiles]
            });
            if (autoBackupBeforeSave)
            {
                appDataStore.CleanupBackups();
            }

            LoadSettingsIntoUi();
            refreshAppearance();
            refreshBackupList();
            refreshCharacterList();
            MessageBox.Show(ownerWindow, "设置已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ownerWindow, $"保存设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        DataDirectory_TextBox.Text = appDataStore.DefaultDataDirectory;
    }

    private void OpenDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        string directory = DataDirectory_TextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            MessageBox.Show(ownerWindow, "请先填写配置目录。", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Directory.Exists(directory))
        {
            MessageBox.Show(ownerWindow, "当前配置目录不存在，请先选择一个已有目录或保存设置后再打开。", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            OpenDirectory(directory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ownerWindow, $"打开当前目录失败：{ex.Message}", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ArchiveCurrentLog_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        MessageBoxResult result = MessageBox.Show(
            ownerWindow,
            "确定要归档当前正在写入的日志文件，并切换到新的日志文件吗？",
            "归档当前日志",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            string? archivePath = appDataStore.ArchiveCurrentLogFile();
            UpdateCurrentLogFilePathText();
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                MessageBox.Show(ownerWindow, "当前日志文件尚未生成。", "归档当前日志", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBox.Show(ownerWindow, $"当前日志已归档到：\n{archivePath}", "归档当前日志", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ownerWindow, $"归档当前日志失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearCurrentLog_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        MessageBoxResult result = MessageBox.Show(
            ownerWindow,
            "确定要清理当前正在写入的日志文件吗？历史日志不会被删除。",
            "清理当前日志",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            int deletedCount = appDataStore.ClearCurrentLogFile();
            UpdateCurrentLogFilePathText();
            MessageBox.Show(ownerWindow, $"已清理 {deletedCount} 个当前日志文件。", "清理当前日志", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ownerWindow, $"清理当前日志失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearAllLogs_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        MessageBoxResult result = MessageBox.Show(
            ownerWindow,
            "确定要清理当前日志和所有历史日志吗？",
            "清理所有日志",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            int deletedCount = appDataStore.ClearLogFiles();
            UpdateCurrentLogFilePathText();
            MessageBox.Show(ownerWindow, $"已清理 {deletedCount} 个日志文件。", "清理所有日志", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ownerWindow, $"清理所有日志失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenLogDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        OpenDirectory(appDataStore.LogDirectory);
    }

    private void UpdateCurrentLogFilePathText()
    {
        if (appDataStore == null) return;

        CurrentLogFilePath_TextBox.Text = appDataStore.LogFilePath;
        CurrentLogFilePath_TextBox.ToolTip = appDataStore.LogFilePath;
    }

    private async void RefreshMapData_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;
        if (!CanStartManualRefresh(appDataStore.Settings.LastMapDataManualRefreshAttempt, out TimeSpan waitTime))
        {
            ShowRefreshCooldownMessage("地图数据", waitTime);
            return;
        }

        SetManualRefreshButtonsEnabled(false);
        DateTime attemptTime = DateTime.Now;
        try
        {
            SaveSettingsPreservingEditableValues(settings =>
            {
                settings.LastMapDataManualRefreshAttempt = attemptTime;
            });

            MapDataLoadResult result = await appDataStore.ForceRefreshMapDataAsync();
            RefreshStatusFields();
            if (!result.Success)
            {
                MessageBox.Show(ownerWindow, "地图数据检查更新失败，请稍后再试。", "数据同步", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            refreshMapDataConsumers();
            string versionText = string.IsNullOrWhiteSpace(result.Version) ? "未知版本" : result.Version;
            MessageBox.Show(ownerWindow, $"地图数据已更新到：{versionText}", "数据同步", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            SetManualRefreshButtonsEnabled(true);
            RefreshStatusFields();
        }
    }

    private async void RefreshServerList_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;
        if (!CanStartManualRefresh(appDataStore.Settings.LastServerListManualRefreshAttempt, out TimeSpan waitTime))
        {
            ShowRefreshCooldownMessage("服务器列表", waitTime);
            return;
        }

        SetManualRefreshButtonsEnabled(false);
        DateTime attemptTime = DateTime.Now;
        try
        {
            SaveSettingsPreservingEditableValues(settings =>
            {
                settings.LastServerListManualRefreshAttempt = attemptTime;
            });

            bool success = await appDataStore.TrySyncServerListAsync();
            RefreshStatusFields();
            if (!success)
            {
                MessageBox.Show(ownerWindow, "服务器列表检查更新失败，已继续使用本地缓存。", "数据同步", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            refreshServerListConsumers();
            MessageBox.Show(ownerWindow, "服务器列表已更新。", "数据同步", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            SetManualRefreshButtonsEnabled(true);
            RefreshStatusFields();
        }
    }

    private void RefreshStatusFields()
    {
        if (appDataStore == null) return;

        MapDataVersion_TextBox.Text = string.IsNullOrWhiteSpace(appDataStore.MapDataVersion)
            ? "未加载"
            : appDataStore.MapDataVersion;
        int mapCount = MapData.GetKnownMapIds().Count;
        MapDataSummary_TextBox.Text = mapCount > 0
            ? $"{mapCount} 张地图"
            : "未加载";
        MapDataUpdatedAt_TextBox.Text = FormatOptionalTime(appDataStore.MapDataLastUpdated, "尚未更新");
        MapDataCheckedAt_TextBox.Text = FormatOptionalTime(appDataStore.MapDataLastSuccessfulSyncAt, "尚未成功检查");
        MapDataVersionSource_TextBox.Text = appDataStore.MapDataVersionSourceUrl;
        MapDataContentSource_TextBox.Text = appDataStore.MapDataContentSourceUrl;

        int dataCenterCount = appDataStore.ServerList.Groups.Count;
        int worldCount = appDataStore.ServerList.Groups.Sum(group => group.Worlds.Count);
        ServerListStatus_TextBox.Text = $"{dataCenterCount} 个大区，{worldCount} 个服务器";
        ServerListUpdatedAt_TextBox.Text = FormatOptionalTime(appDataStore.ServerList.LastUpdated, "尚未更新");
        ServerListCheckedAt_TextBox.Text = FormatOptionalTime(appDataStore.ServerList.LastSuccessfulSyncAt, "尚未成功检查");
        ServerListSource_TextBox.Text = string.IsNullOrWhiteSpace(appDataStore.ServerList.SourceUrl)
            ? "未知"
            : appDataStore.ServerList.SourceUrl;
    }

    private void UpdateBackupLimitInputState()
    {
        bool autoBackupEnabled = AutoBackup_CheckBox.IsChecked == true;
        LimitBackupCount_CheckBox.IsEnabled = autoBackupEnabled;
        LimitBackupDays_CheckBox.IsEnabled = autoBackupEnabled;
        MaxBackupCount_TextBox.IsEnabled = autoBackupEnabled && LimitBackupCount_CheckBox.IsChecked == true;
        MaxBackupDays_TextBox.IsEnabled = autoBackupEnabled && LimitBackupDays_CheckBox.IsChecked == true;
    }

    private bool CanStartManualRefresh(DateTime lastAttempt, out TimeSpan waitTime)
    {
        waitTime = TimeSpan.Zero;
        if (lastAttempt == DateTime.MinValue) return true;

        TimeSpan elapsed = DateTime.Now - lastAttempt;
        if (elapsed >= ManualRefreshCooldown) return true;

        waitTime = ManualRefreshCooldown - elapsed;
        return false;
    }

    private void ShowRefreshCooldownMessage(string dataName, TimeSpan waitTime)
    {
        int waitSeconds = Math.Max(1, (int)Math.Ceiling(waitTime.TotalSeconds));
        MessageBox.Show(
            ownerWindow,
            $"{dataName}刚刚检查过，请约 {waitSeconds} 秒后再试。",
            "检查过于频繁",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SetManualRefreshButtonsEnabled(bool isEnabled)
    {
        RefreshMapData_Button.IsEnabled = isEnabled;
        RefreshServerList_Button.IsEnabled = isEnabled;
    }

    private void SaveSettingsPreservingEditableValues(Action<AppSettings> updateSettings)
    {
        if (appDataStore == null) return;

        AppSettings settings = new()
        {
            MaxBackupCount = appDataStore.Settings.MaxBackupCount,
            MaxBackupDays = appDataStore.Settings.MaxBackupDays,
            LimitBackupCount = appDataStore.Settings.LimitBackupCount,
            LimitBackupDays = appDataStore.Settings.LimitBackupDays,
            AutoBackupBeforeSave = appDataStore.Settings.AutoBackupBeforeSave,
            MaxLogFileSizeMb = appDataStore.Settings.MaxLogFileSizeMb,
            MaxLogFileCount = appDataStore.Settings.MaxLogFileCount,
            UseWayMarkImageLabels = appDataStore.Settings.UseWayMarkImageLabels,
            StartupWayMarkAction = appDataStore.Settings.StartupWayMarkAction,
            LastMapDataManualRefreshAttempt = appDataStore.Settings.LastMapDataManualRefreshAttempt,
            LastServerListManualRefreshAttempt = appDataStore.Settings.LastServerListManualRefreshAttempt,
            WindowLayout = appDataStore.Settings.WindowLayout,
            RecentFiles = [.. appDataStore.Settings.RecentFiles]
        };
        updateSettings(settings);
        try
        {
            appDataStore.SaveSettings(settings);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ownerWindow, $"无法保存检查记录：{ex.Message}", "设置保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (AppDataStoreException ex)
        {
            MessageBox.Show(ownerWindow, $"无法保存检查记录：{ex.Message}", "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyStartupWayMarkActionToUi(StartupWayMarkAction action)
    {
        StartupLoadRecentWayMarkFile_RadioButton.IsChecked = action == StartupWayMarkAction.LoadMostRecentFile;
        StartupOpenWayMarkFileDialog_RadioButton.IsChecked = action == StartupWayMarkAction.OpenFileDialog;
        StartupDoNothing_RadioButton.IsChecked = action == StartupWayMarkAction.None;
    }

    private StartupWayMarkAction ReadStartupWayMarkActionFromUi()
    {
        if (StartupLoadRecentWayMarkFile_RadioButton.IsChecked == true)
        {
            return StartupWayMarkAction.LoadMostRecentFile;
        }

        if (StartupOpenWayMarkFileDialog_RadioButton.IsChecked == true)
        {
            return StartupWayMarkAction.OpenFileDialog;
        }

        return StartupWayMarkAction.None;
    }

    private static string FormatOptionalTime(DateTime value, string emptyText)
    {
        return value == DateTime.MinValue
            ? emptyText
            : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    }

    private bool TryReadIntInRange(TextBox textBox, string displayName, int min, int max, out int value)
    {
        if (int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            value >= min &&
            value <= max)
        {
            return true;
        }

        MessageBox.Show(ownerWindow, $"{displayName} 必须是 {min} 到 {max} 之间的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigitsOnly(e.Text);
    }

    private void IntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text) ||
            e.DataObject.GetData(DataFormats.Text) is not string text ||
            !IsDigitsOnly(text))
        {
            e.CancelCommand();
        }
    }

    private static bool IsDigitsOnly(string text)
    {
        return !string.IsNullOrEmpty(text) && text.All(character => character is >= '0' and <= '9');
    }

    private static void OpenDirectory(string directory)
    {
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        using Process? _ = Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }
}
