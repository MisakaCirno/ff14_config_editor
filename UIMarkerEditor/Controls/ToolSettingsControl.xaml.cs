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
    private bool isLoadingSettingsIntoUi;
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

        SetSettingsUiSilently(() =>
        {
            DataDirectory_TextBox.Text = appDataStore.DataDirectory;
            BootstrapFilePath_TextBox.Text = appDataStore.BootstrapFilePath;
            MigrationStateFilePath_TextBox.Text = appDataStore.MigrationStateFilePath;
            Visibility migrationStateVisibility = File.Exists(appDataStore.MigrationStateFilePath)
                ? Visibility.Visible
                : Visibility.Collapsed;
            MigrationStateFilePath_Label.Visibility = migrationStateVisibility;
            MigrationStateFilePath_TextBox.Visibility = migrationStateVisibility;
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
            WayMarkCustomDirectory_TextBox.Text = appDataStore.Settings.WayMarkCustomDirectory;
            ApplyWayMarkOpenDirectoryModeToUi(appDataStore.Settings.WayMarkOpenDirectoryMode);
            UpdateBackupLimitInputState();
            RefreshStatusFields();
        });
    }

    public void RefreshOnlineDataStatus()
    {
        RefreshStatusFields();
    }

    public void RefreshWayMarkCustomDirectoryFromSettings()
    {
        if (appDataStore == null) return;
        if (string.IsNullOrWhiteSpace(appDataStore.Settings.WayMarkCustomDirectory)) return;
        if (WayMarkCustomDirectory_TextBox.IsKeyboardFocusWithin) return;
        if (!string.IsNullOrWhiteSpace(WayMarkCustomDirectory_TextBox.Text)) return;

        WayMarkCustomDirectory_TextBox.Text = appDataStore.Settings.WayMarkCustomDirectory;
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

        string initialDirectory = Directory.Exists(appDataStore.DataDirectory)
            ? appDataStore.DataDirectory
            : string.Empty;

        while (true)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new()
            {
                Title = "选择新的工具数据目录",
                InitialDirectory = initialDirectory
            };

            if (DialogOwnerHelper.ShowCommonDialog(dialog, ownerWindow ?? Window.GetWindow(this)) != true)
            {
                return;
            }

            if (DataDirectoryMigrationReportDialog.TryValidateTargetDirectory(
                appDataStore.DataDirectory,
                dialog.FolderName,
                out string targetFullPath,
                out string errorMessage))
            {
                RequestDataDirectoryChange(targetFullPath);
                return;
            }

            AppMessageBox.Show(
                ownerWindow,
                $"{errorMessage}\n\n请重新选择一个目录。",
                "迁移工具数据目录",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            if (Directory.Exists(dialog.FolderName))
            {
                initialDirectory = dialog.FolderName;
            }
        }
    }

    private void BackupLimit_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateBackupLimitInputState();
        if (isLoadingSettingsIntoUi) return;

        SaveSettingsMutation(
            settings =>
            {
                settings.LimitBackupCount = LimitBackupCount_CheckBox.IsChecked == true;
                settings.LimitBackupDays = LimitBackupDays_CheckBox.IsChecked == true;
            },
            "保存自动清理设置",
            CleanupBackupsIfEnabled);
    }

    private void AutoBackup_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateBackupLimitInputState();
        if (isLoadingSettingsIntoUi) return;

        SaveSettingsMutation(
            settings =>
            {
                settings.AutoBackupBeforeSave = AutoBackupBeforeSave_CheckBox.IsChecked == true;
                settings.AutoBackupAfterLoad = AutoBackupAfterLoad_CheckBox.IsChecked == true;
            },
            "保存自动备份设置",
            CleanupBackupsIfEnabled);
    }

    private void OpenDirectoryMode_RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        UpdateWayMarkCustomDirectoryInputState();
        if (isLoadingSettingsIntoUi) return;

        SaveSettingsMutation(
            settings =>
            {
                settings.WayMarkOpenDirectoryMode = ReadWayMarkOpenDirectoryModeFromUi();
            },
            "保存文件打开设置");
    }

    private void BrowseWayMarkCustomDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        string initialDirectory = string.Empty;
        string currentDirectory = WayMarkCustomDirectory_TextBox.Text.Trim();
        if (Directory.Exists(currentDirectory))
        {
            initialDirectory = currentDirectory;
        }
        else if (Directory.Exists(appDataStore.Settings.WayMarkCustomDirectory))
        {
            initialDirectory = appDataStore.Settings.WayMarkCustomDirectory;
        }

        Microsoft.Win32.OpenFolderDialog dialog = new()
        {
            Title = "选择自定义路径",
            InitialDirectory = initialDirectory
        };

        if (DialogOwnerHelper.ShowCommonDialog(dialog, ownerWindow ?? Window.GetWindow(this)) == true)
        {
            SetSettingsUiSilently(() =>
            {
                WayMarkCustomDirectory_TextBox.Text = dialog.FolderName;
                OpenDirectoryCustom_RadioButton.IsChecked = true;
                UpdateWayMarkCustomDirectoryInputState();
            });
            SaveSettingsMutation(
                settings =>
                {
                    settings.WayMarkOpenDirectoryMode = WayMarkOpenDirectoryMode.CustomDirectory;
                    settings.WayMarkCustomDirectory = dialog.FolderName.Trim();
                    settings.WayMarkCustomDirectoryAutoFillAttempted = true;
                },
                "保存自定义打开路径");
        }
    }

    private void WayMarkLabelDisplayMode_SegmentedSwitch_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (isLoadingSettingsIntoUi) return;

        SaveSettingsMutation(
            settings =>
            {
                settings.UseWayMarkImageLabels = WayMarkLabelDisplayMode_SegmentedSwitch.IsLeftSelected;
            },
            "保存标点显示形式",
            refreshAppearance);
    }

    private void WayMarkFavoriteSaveMode_SegmentedSwitch_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (isLoadingSettingsIntoUi) return;

        SaveSettingsMutation(
            settings =>
            {
                settings.WayMarkFavoriteSaveMode = ReadWayMarkFavoriteSaveModeFromUi();
            },
            "保存标点收藏设置",
            refreshAppearance);
    }

    private void StartupWayMarkAction_RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (isLoadingSettingsIntoUi) return;

        SaveSettingsMutation(
            settings =>
            {
                settings.StartupWayMarkAction = ReadStartupWayMarkActionFromUi();
            },
            "保存启动行为设置");
    }

    private void MaxBackupCount_TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitIntegerSetting(
            MaxBackupCount_TextBox,
            "最多保留备份数量",
            AppSettings.MinBackupCount,
            AppSettings.MaxBackupCountLimit,
            settings => settings.MaxBackupCount,
            (settings, value) => settings.MaxBackupCount = value,
            "保存备份数量设置",
            CleanupBackupsIfEnabled);
    }

    private void MaxBackupDays_TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitIntegerSetting(
            MaxBackupDays_TextBox,
            "最多保留备份天数",
            AppSettings.MinBackupDays,
            AppSettings.MaxBackupDaysLimit,
            settings => settings.MaxBackupDays,
            (settings, value) => settings.MaxBackupDays = value,
            "保存备份时间设置",
            CleanupBackupsIfEnabled);
    }

    private void MaxLogFileSizeMb_TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitIntegerSetting(
            MaxLogFileSizeMb_TextBox,
            "日志文件大小",
            AppSettings.MinLogFileSizeMb,
            AppSettings.MaxLogFileSizeMbLimit,
            settings => settings.MaxLogFileSizeMb,
            (settings, value) => settings.MaxLogFileSizeMb = value,
            "保存日志大小设置");
    }

    private void MaxLogFileCount_TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitIntegerSetting(
            MaxLogFileCount_TextBox,
            "日志文件最多保存数量",
            AppSettings.MinLogFileCount,
            AppSettings.MaxLogFileCountLimit,
            settings => settings.MaxLogFileCount,
            (settings, value) => settings.MaxLogFileCount = value,
            "保存日志数量设置");
    }

    private void IntegerSetting_TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
        {
            return;
        }

        e.Handled = true;
        bool committed =
            ReferenceEquals(textBox, MaxBackupCount_TextBox) && CommitIntegerSetting(
                MaxBackupCount_TextBox,
                "最多保留备份数量",
                AppSettings.MinBackupCount,
                AppSettings.MaxBackupCountLimit,
                settings => settings.MaxBackupCount,
                (settings, value) => settings.MaxBackupCount = value,
                "保存备份数量设置",
                CleanupBackupsIfEnabled) ||
            ReferenceEquals(textBox, MaxBackupDays_TextBox) && CommitIntegerSetting(
                MaxBackupDays_TextBox,
                "最多保留备份天数",
                AppSettings.MinBackupDays,
                AppSettings.MaxBackupDaysLimit,
                settings => settings.MaxBackupDays,
                (settings, value) => settings.MaxBackupDays = value,
                "保存备份时间设置",
                CleanupBackupsIfEnabled) ||
            ReferenceEquals(textBox, MaxLogFileSizeMb_TextBox) && CommitIntegerSetting(
                MaxLogFileSizeMb_TextBox,
                "日志文件大小",
                AppSettings.MinLogFileSizeMb,
                AppSettings.MaxLogFileSizeMbLimit,
                settings => settings.MaxLogFileSizeMb,
                (settings, value) => settings.MaxLogFileSizeMb = value,
                "保存日志大小设置") ||
            ReferenceEquals(textBox, MaxLogFileCount_TextBox) && CommitIntegerSetting(
                MaxLogFileCount_TextBox,
                "日志文件最多保存数量",
                AppSettings.MinLogFileCount,
                AppSettings.MaxLogFileCountLimit,
                settings => settings.MaxLogFileCount,
                (settings, value) => settings.MaxLogFileCount = value,
                "保存日志数量设置");

        if (committed)
        {
            Keyboard.ClearFocus();
        }
    }

    private void WayMarkCustomDirectory_TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitWayMarkCustomDirectory();
    }

    private void WayMarkCustomDirectory_TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (CommitWayMarkCustomDirectory())
        {
            Keyboard.ClearFocus();
        }
    }

    private void ShowMigrationReport(DataDirectoryMigrationResult result)
    {
        DataDirectoryMigrationReportDialog dialog = new(result);
        DialogOwnerHelper.ConfigureOwnedDialog(dialog, ownerWindow ?? Window.GetWindow(this));
        dialog.ShowDialog();
    }

    private void RequestDataDirectoryChange(string requestedDataDirectory)
    {
        if (appDataStore == null) return;

        requestedDataDirectory = requestedDataDirectory.Trim();
        if (string.IsNullOrWhiteSpace(requestedDataDirectory))
        {
            AppMessageBox.Show(ownerWindow, "数据目录不能为空。", "迁移工具数据目录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string requestedFullPath;
        string currentFullPath;
        try
        {
            requestedFullPath = Path.GetFullPath(requestedDataDirectory);
            currentFullPath = Path.GetFullPath(appDataStore.DataDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            AppMessageBox.Show(ownerWindow, $"数据目录路径无效：{ex.Message}", "迁移工具数据目录", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.Equals(requestedFullPath, currentFullPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadSettingsIntoUi();
            return;
        }

        bool migrationDialogCreated = false;
        try
        {
            DataDirectoryMigrationReportDialog migrationDialog = new(currentFullPath, requestedFullPath);
            migrationDialogCreated = true;
            DialogOwnerHelper.ConfigureOwnedDialog(migrationDialog, ownerWindow ?? Window.GetWindow(this));
            DataDirectoryMigrationResult? result = migrationDialog.RunMigration((targetDirectory, progress) =>
                appDataStore.ChangeDataDirectoryAsync(targetDirectory, progress));
            if (result == null)
            {
                LoadSettingsIntoUi();
                return;
            }

            LoadSettingsIntoUi();
            refreshAppearance();
            refreshBackupList();
            refreshCharacterList();
            refreshServerListConsumers();
            refreshMapDataConsumers();
        }
        catch (Exception ex)
        {
            LoadSettingsIntoUi();
            if (!migrationDialogCreated)
            {
                AppMessageBox.Show(ownerWindow, $"迁移工具数据目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ResetDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        RequestDataDirectoryChange(appDataStore.DefaultDataDirectory);
    }

    private void OpenDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        string directory = appDataStore.DataDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            AppMessageBox.Show(ownerWindow, "当前工具数据目录为空。", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!Directory.Exists(directory))
        {
            AppMessageBox.Show(ownerWindow, "当前工具数据目录不存在，请先迁移到一个可用目录。", "打开当前目录", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void SetSettingsUiSilently(Action updateUi)
    {
        bool previousValue = isLoadingSettingsIntoUi;
        isLoadingSettingsIntoUi = true;
        try
        {
            updateUi();
        }
        finally
        {
            isLoadingSettingsIntoUi = previousValue;
        }
    }

    private bool SaveSettingsMutation(
        Action<AppSettings> updateSettings,
        string actionName,
        Action? afterSave = null,
        bool restoreUiOnFailure = true)
    {
        if (appDataStore == null) return false;

        AppSettings settings = appDataStore.CreateSettingsSnapshot();
        updateSettings(settings);
        try
        {
            appDataStore.SaveSettings(settings);
            afterSave?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException or IOException or UnauthorizedAccessException)
        {
            if (restoreUiOnFailure)
            {
                LoadSettingsIntoUi();
                refreshAppearance();
            }

            AppMessageBox.Show(ownerWindow, $"{actionName}失败：{ex.Message}", "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private bool CommitIntegerSetting(
        TextBox textBox,
        string displayName,
        int min,
        int max,
        Func<AppSettings, int> readSetting,
        Action<AppSettings, int> writeSetting,
        string actionName,
        Action? afterSave = null)
    {
        if (appDataStore == null || isLoadingSettingsIntoUi) return false;

        int currentValue = readSetting(appDataStore.Settings);
        if (!TryReadIntInRange(textBox, displayName, min, max, out int value))
        {
            textBox.Text = currentValue.ToString(CultureInfo.InvariantCulture);
            return false;
        }

        if (value == currentValue)
        {
            textBox.Text = currentValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        bool saved = SaveSettingsMutation(
            settings =>
            {
                writeSetting(settings, value);
            },
            actionName,
            afterSave);
        textBox.Text = readSetting(appDataStore.Settings).ToString(CultureInfo.InvariantCulture);
        return saved;
    }

    private bool CommitWayMarkCustomDirectory()
    {
        if (appDataStore == null || isLoadingSettingsIntoUi) return false;

        string directory = WayMarkCustomDirectory_TextBox.Text.Trim();
        if (string.Equals(directory, appDataStore.Settings.WayMarkCustomDirectory, StringComparison.OrdinalIgnoreCase))
        {
            WayMarkCustomDirectory_TextBox.Text = appDataStore.Settings.WayMarkCustomDirectory;
            return true;
        }

        return SaveSettingsMutation(
            settings =>
            {
                settings.WayMarkCustomDirectory = directory;
                settings.WayMarkCustomDirectoryAutoFillAttempted = true;
            },
            "保存自定义打开路径");
    }

    private void CleanupBackupsIfEnabled()
    {
        if (appDataStore == null) return;
        if (!appDataStore.Settings.AutoBackupBeforeSave && !appDataStore.Settings.AutoBackupAfterLoad) return;

        appDataStore.CleanupBackups();
        refreshBackupList();
    }

    private void SaveSettingsPreservingEditableValues(Action<AppSettings> updateSettings)
    {
        SaveSettingsMutation(updateSettings, "保存检查记录", restoreUiOnFailure: false);
    }

    private WayMarkFavoriteSaveMode ReadWayMarkFavoriteSaveModeFromUi()
    {
        return WayMarkFavoriteSaveMode_SegmentedSwitch.IsLeftSelected
            ? WayMarkFavoriteSaveMode.Manual
            : WayMarkFavoriteSaveMode.Auto;
    }

    private void ApplyWayMarkOpenDirectoryModeToUi(WayMarkOpenDirectoryMode mode)
    {
        OpenDirectoryCustom_RadioButton.IsChecked = mode == WayMarkOpenDirectoryMode.CustomDirectory;
        OpenDirectoryDefault_RadioButton.IsChecked = mode == WayMarkOpenDirectoryMode.Default;
        UpdateWayMarkCustomDirectoryInputState();
    }

    private void UpdateWayMarkCustomDirectoryInputState()
    {
        bool isCustomDirectoryMode = OpenDirectoryCustom_RadioButton.IsChecked == true;
        WayMarkCustomDirectory_TextBox.IsEnabled = isCustomDirectoryMode;
        BrowseWayMarkCustomDirectory_Button.IsEnabled = isCustomDirectoryMode;
    }

    private WayMarkOpenDirectoryMode ReadWayMarkOpenDirectoryModeFromUi()
    {
        return OpenDirectoryDefault_RadioButton.IsChecked == true
            ? WayMarkOpenDirectoryMode.Default
            : WayMarkOpenDirectoryMode.CustomDirectory;
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
