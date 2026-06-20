using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace UIMarkerEditor.Controls;

public partial class ToolSettingsControl : UserControl
{
    private static readonly TimeSpan ManualRefreshCooldown = TimeSpan.FromMinutes(1);
    private const double NavigationActivationThreshold = 24;
    private const double ScrollBottomTolerance = 1;
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private bool isNavigatingFromNavigation;
    private bool isSelectingNavigationItem;
    private Action refreshBackupList = () => { };
    private Action refreshCharacterList = () => { };
    private Action refreshServerListConsumers = () => { };
    private Action refreshMapDataConsumers = () => { };
    private Action refreshAppearance = () => { };

    public ToolSettingsControl()
    {
        InitializeComponent();
        SelectNavigationItemWithoutNavigation(SettingsNavigation_ListBox.Items.OfType<ListBoxItem>().FirstOrDefault());
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
        AutoBackupBeforeSave_CheckBox.IsChecked = appDataStore.Settings.AutoBackupBeforeSave;
        AutoBackupAfterLoad_CheckBox.IsChecked = appDataStore.Settings.AutoBackupAfterLoad;
        WayMarkLabelDisplayMode_SegmentedSwitch.IsLeftSelected = appDataStore.Settings.UseWayMarkImageLabels;
        WayMarkFavoriteSaveMode_SegmentedSwitch.IsLeftSelected = appDataStore.Settings.WayMarkFavoriteSaveMode == WayMarkFavoriteSaveMode.Manual;
        LimitBackupCount_CheckBox.IsChecked = appDataStore.Settings.LimitBackupCount;
        LimitBackupDays_CheckBox.IsChecked = appDataStore.Settings.LimitBackupDays;
        ApplyStartupWayMarkActionToUi(appDataStore.Settings.StartupWayMarkAction);
        WayMarkGameCharacterRootDirectory_TextBox.Text = appDataStore.Settings.WayMarkGameCharacterRootDirectory;
        ApplyWayMarkOpenDirectoryModeToUi(appDataStore.Settings.WayMarkOpenDirectoryMode);
        UpdateBackupLimitInputState();
        RefreshStatusFields();
    }

    public void RefreshOnlineDataStatus()
    {
        RefreshStatusFields();
    }

    private void SettingsNavigation_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isSelectingNavigationItem) return;
        if (SettingsNavigation_ListBox.SelectedItem is not ListBoxItem item) return;

        NavigateToSettingsSection(item);
    }

    private void SettingsNavigation_ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(SettingsNavigation_ListBox, source) is not ListBoxItem item) return;
        if (!ReferenceEquals(item, SettingsNavigation_ListBox.SelectedItem)) return;

        NavigateToSettingsSection(item);
    }

    private void SettingsContent_ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (isNavigatingFromNavigation) return;

        UpdateNavigationSelectionFromScroll();
    }

    private void NavigateToSettingsSection(ListBoxItem item)
    {
        if (item.Tag is not string sectionName) return;
        if (FindName(sectionName) is not FrameworkElement section) return;

        double sectionTop = section.TransformToAncestor(SettingsContent_ScrollViewer).Transform(new Point(0, 0)).Y;
        double targetOffset = SettingsContent_ScrollViewer.VerticalOffset + sectionTop;
        targetOffset = Math.Clamp(targetOffset, 0, SettingsContent_ScrollViewer.ScrollableHeight);

        isNavigatingFromNavigation = true;
        SettingsContent_ScrollViewer.ScrollToVerticalOffset(targetOffset);
        SelectNavigationItemWithoutNavigation(item);

        Dispatcher.BeginInvoke(
            new Action(() => isNavigatingFromNavigation = false),
            DispatcherPriority.Background);
    }

    private void UpdateNavigationSelectionFromScroll()
    {
        ListBoxItem? activeItem = GetActiveNavigationItemFromScroll();
        if (activeItem == null || ReferenceEquals(activeItem, SettingsNavigation_ListBox.SelectedItem)) return;

        SelectNavigationItemWithoutNavigation(activeItem);
    }

    private ListBoxItem? GetActiveNavigationItemFromScroll()
    {
        ListBoxItem[] navigationItems = [.. SettingsNavigation_ListBox.Items.OfType<ListBoxItem>()];
        if (SettingsContent_ScrollViewer.ScrollableHeight > 0 &&
            SettingsContent_ScrollViewer.VerticalOffset >= SettingsContent_ScrollViewer.ScrollableHeight - ScrollBottomTolerance)
        {
            return navigationItems.LastOrDefault(HasMatchingSettingsSection);
        }

        ListBoxItem? activeItem = null;
        foreach (ListBoxItem item in navigationItems)
        {
            if (item.Tag is not string sectionName) continue;
            if (FindName(sectionName) is not FrameworkElement section) continue;

            double sectionTop = section.TransformToAncestor(SettingsContent_ScrollViewer).Transform(new Point(0, 0)).Y;
            if (sectionTop <= NavigationActivationThreshold)
            {
                activeItem = item;
                continue;
            }

            activeItem ??= item;
            break;
        }

        return activeItem;
    }

    private bool HasMatchingSettingsSection(ListBoxItem item)
    {
        return item.Tag is string sectionName && FindName(sectionName) is FrameworkElement;
    }

    private void SelectNavigationItemWithoutNavigation(ListBoxItem? item)
    {
        if (item == null || ReferenceEquals(item, SettingsNavigation_ListBox.SelectedItem)) return;

        isSelectingNavigationItem = true;
        try
        {
            SettingsNavigation_ListBox.SelectedItem = item;
        }
        finally
        {
            isSelectingNavigationItem = false;
        }
    }

    private void BrowseDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        Microsoft.Win32.OpenFolderDialog dialog = new()
        {
            Title = "选择工具数据目录",
            InitialDirectory = Directory.Exists(DataDirectory_TextBox.Text) ? DataDirectory_TextBox.Text : appDataStore.DataDirectory
        };

        if (DialogOwnerHelper.ShowCommonDialog(dialog, ownerWindow ?? Window.GetWindow(this)) == true)
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

    private void OpenDirectoryMode_RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateWayMarkGameCharacterRootDirectoryInputState();
    }

    private void BrowseWayMarkGameCharacterRootDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        string initialDirectory = string.Empty;
        string currentDirectory = WayMarkGameCharacterRootDirectory_TextBox.Text.Trim();
        if (Directory.Exists(currentDirectory))
        {
            initialDirectory = currentDirectory;
        }
        else if (Directory.Exists(appDataStore.Settings.WayMarkGameCharacterRootDirectory))
        {
            initialDirectory = appDataStore.Settings.WayMarkGameCharacterRootDirectory;
        }

        Microsoft.Win32.OpenFolderDialog dialog = new()
        {
            Title = "选择游戏角色目录",
            InitialDirectory = initialDirectory
        };

        if (DialogOwnerHelper.ShowCommonDialog(dialog, ownerWindow ?? Window.GetWindow(this)) == true)
        {
            WayMarkGameCharacterRootDirectory_TextBox.Text = dialog.FolderName;
            OpenDirectoryGameCharacter_RadioButton.IsChecked = true;
        }
    }

    private void SaveSettings_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        bool autoBackupBeforeSave = AutoBackupBeforeSave_CheckBox.IsChecked == true;
        bool autoBackupAfterLoad = AutoBackupAfterLoad_CheckBox.IsChecked == true;
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
                MessageBoxResult migrateResult = AppMessageBox.Show(
                    ownerWindow,
                    "是否将现有工具设置、角色备注和备份迁移到新目录？\n选择“否”将只切换目录，不复制旧数据。",
                    "迁移工具数据目录",
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
                AutoBackupAfterLoad = autoBackupAfterLoad,
                MaxLogFileSizeMb = maxLogFileSizeMb,
                MaxLogFileCount = maxLogFileCount,
                UseWayMarkImageLabels = WayMarkLabelDisplayMode_SegmentedSwitch.IsLeftSelected,
                WayMarkFavoriteSaveMode = ReadWayMarkFavoriteSaveModeFromUi(),
                StartupWayMarkAction = ReadStartupWayMarkActionFromUi(),
                WayMarkOpenDirectoryMode = ReadWayMarkOpenDirectoryModeFromUi(),
                WayMarkGameCharacterRootDirectory = WayMarkGameCharacterRootDirectory_TextBox.Text.Trim(),
                WayMarkGameCharacterRootDirectoryAutoDetectAttempted = true,
                LastMapDataManualRefreshAttempt = appDataStore.Settings.LastMapDataManualRefreshAttempt,
                LastServerListManualRefreshAttempt = appDataStore.Settings.LastServerListManualRefreshAttempt,
                WindowLayout = appDataStore.Settings.WindowLayout,
                RecentFiles = [.. appDataStore.Settings.RecentFiles]
            });
            if (autoBackupBeforeSave || autoBackupAfterLoad)
            {
                appDataStore.CleanupBackups();
            }

            LoadSettingsIntoUi();
            refreshAppearance();
            refreshBackupList();
            refreshCharacterList();
            ToastService.ShowSuccess("设置已保存。");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"保存设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            AppMessageBox.Show(ownerWindow, "请先填写工具数据目录。", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Directory.Exists(directory))
        {
            AppMessageBox.Show(ownerWindow, "当前工具数据目录不存在，请先选择一个已有目录或保存设置后再打开。", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            OpenDirectory(directory);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"打开当前目录失败：{ex.Message}", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ArchiveCurrentLog_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        MessageBoxResult result = AppMessageBox.Show(
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
                AppMessageBox.Show(ownerWindow, "当前日志文件尚未生成。", "归档当前日志", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ToastService.ShowSuccess($"当前日志已归档到：{archivePath}");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"归档当前日志失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearCurrentLog_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        MessageBoxResult result = AppMessageBox.Show(
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
            ToastService.ShowSuccess($"已清理 {deletedCount} 个当前日志文件。");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"清理当前日志失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearAllLogs_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        MessageBoxResult result = AppMessageBox.Show(
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
            ToastService.ShowSuccess($"已清理 {deletedCount} 个日志文件。");
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"清理所有日志失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
        try
        {
            MapDataLoadResult result = await appDataStore.ForceRefreshMapDataAsync();
            RefreshStatusFields();
            if (!result.Success)
            {
                AppMessageBox.Show(ownerWindow, "地图数据检查更新失败，请稍后再试。", "数据同步", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettingsPreservingEditableValues(settings =>
            {
                settings.LastMapDataManualRefreshAttempt = DateTime.Now;
            });

            string versionText = string.IsNullOrWhiteSpace(result.Version) ? "未知版本" : result.Version;
            if (result.Updated)
            {
                refreshMapDataConsumers();
                ToastService.ShowSuccess($"地图数据已更新到：{versionText}");
                return;
            }

            ToastService.ShowSuccess($"地图数据目前已是最新。当前版本：{versionText}");
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
        try
        {
            ServerListLoadResult result = await appDataStore.RefreshServerListAsync();
            RefreshStatusFields();
            if (!result.Success)
            {
                AppMessageBox.Show(ownerWindow, "服务器列表检查更新失败，已继续使用本地缓存。", "数据同步", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettingsPreservingEditableValues(settings =>
            {
                settings.LastServerListManualRefreshAttempt = DateTime.Now;
            });

            if (result.Updated)
            {
                refreshServerListConsumers();
                ToastService.ShowSuccess("服务器列表已更新。");
                return;
            }

            ToastService.ShowSuccess("服务器列表目前已是最新。");
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
        bool autoBackupEnabled = AutoBackupBeforeSave_CheckBox.IsChecked == true || AutoBackupAfterLoad_CheckBox.IsChecked == true;
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
        AppMessageBox.Show(
            ownerWindow,
            $"为减轻服务器压力，{dataName} 1 分钟内只能检查一次。请约 {waitSeconds} 秒后再试。",
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
            AutoBackupAfterLoad = appDataStore.Settings.AutoBackupAfterLoad,
            MaxLogFileSizeMb = appDataStore.Settings.MaxLogFileSizeMb,
            MaxLogFileCount = appDataStore.Settings.MaxLogFileCount,
            UseWayMarkImageLabels = appDataStore.Settings.UseWayMarkImageLabels,
            WayMarkFavoriteSaveMode = appDataStore.Settings.WayMarkFavoriteSaveMode,
            StartupWayMarkAction = appDataStore.Settings.StartupWayMarkAction,
            WayMarkOpenDirectoryMode = appDataStore.Settings.WayMarkOpenDirectoryMode,
            WayMarkGameCharacterRootDirectory = appDataStore.Settings.WayMarkGameCharacterRootDirectory,
            WayMarkGameCharacterRootDirectoryAutoDetectAttempted = appDataStore.Settings.WayMarkGameCharacterRootDirectoryAutoDetectAttempted,
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
            AppMessageBox.Show(ownerWindow, $"无法保存检查记录：{ex.Message}", "设置保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (AppDataStoreException ex)
        {
            AppMessageBox.Show(ownerWindow, $"无法保存检查记录：{ex.Message}", "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private WayMarkFavoriteSaveMode ReadWayMarkFavoriteSaveModeFromUi()
    {
        return WayMarkFavoriteSaveMode_SegmentedSwitch.IsLeftSelected
            ? WayMarkFavoriteSaveMode.Manual
            : WayMarkFavoriteSaveMode.Auto;
    }

    private void ApplyWayMarkOpenDirectoryModeToUi(WayMarkOpenDirectoryMode mode)
    {
        OpenDirectoryGameCharacter_RadioButton.IsChecked = mode == WayMarkOpenDirectoryMode.GameCharacterDirectory;
        OpenDirectoryLastOpened_RadioButton.IsChecked = mode == WayMarkOpenDirectoryMode.LastOpenedPath;
        UpdateWayMarkGameCharacterRootDirectoryInputState();
    }

    private void UpdateWayMarkGameCharacterRootDirectoryInputState()
    {
        bool isGameCharacterMode = OpenDirectoryGameCharacter_RadioButton.IsChecked == true;
        WayMarkGameCharacterRootDirectory_TextBox.IsEnabled = isGameCharacterMode;
        BrowseWayMarkGameCharacterRootDirectory_Button.IsEnabled = isGameCharacterMode;
    }

    private WayMarkOpenDirectoryMode ReadWayMarkOpenDirectoryModeFromUi()
    {
        return OpenDirectoryLastOpened_RadioButton.IsChecked == true
            ? WayMarkOpenDirectoryMode.LastOpenedPath
            : WayMarkOpenDirectoryMode.GameCharacterDirectory;
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

        AppMessageBox.Show(ownerWindow, $"{displayName} 必须是 {min} 到 {max} 之间的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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
