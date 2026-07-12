using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand OpenWayMarkFileCommand = new(
            "读取标点文件",
            nameof(OpenWayMarkFileCommand),
            typeof(MainWindow),
            [new KeyGesture(Key.O, ModifierKeys.Control)]);

        public static readonly RoutedUICommand ReloadWayMarkFileCommand = new(
            "重新加载文件",
            nameof(ReloadWayMarkFileCommand),
            typeof(MainWindow),
            [new KeyGesture(Key.R, ModifierKeys.Control)]);

        public static readonly RoutedUICommand SaveWayMarkFileCommand = new(
            "保存标点文件",
            nameof(SaveWayMarkFileCommand),
            typeof(MainWindow),
            [new KeyGesture(Key.S, ModifierKeys.Control)]);

        public static readonly RoutedUICommand CloseWayMarkFileCommand = new(
            "关闭当前文件",
            nameof(CloseWayMarkFileCommand),
            typeof(MainWindow),
            [new KeyGesture(Key.W, ModifierKeys.Control)]);

        private const string DefaultWindowTitle = "FF14 标点预设编辑工具";
        private static readonly IReadOnlyDictionary<string, string> DataCenterAbbreviations =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["豆豆柴"] = "狗",
                ["莫古力"] = "猪",
                ["陆行鸟"] = "鸟",
                ["猫小胖"] = "猫"
            };

        private string currentFilePath = string.Empty;

        private ConfigUISave? configUISave = null;

        private readonly AppDataStore appDataStore;
        private readonly ObservableCollection<ToastNotification> toastNotifications = [];
        private MapDataLoadResult? pendingUserMapDataRepairPrompt;
        private bool isRestoringMainTabSelection;
        private string? dataDirectoryMigrationPausedCurrentFilePath;

        public MainWindow(AppDataStore appDataStore, MapDataLoadResult? startupMapDataLoadResult = null)
        {
            this.appDataStore = appDataStore;
            pendingUserMapDataRepairPrompt = startupMapDataLoadResult?.RequiresUserMapDataRepair == true
                ? startupMapDataLoadResult
                : null;
            wayMarkChangeTracker = new(WayMarkModel_PropertyChanged);
            InitializeComponent();
            ToastItems_Control.ItemsSource = toastNotifications;
            ToastService.RegisterSuccessHandler(ShowSuccessToast);
            WayMarkEditor_Control.Initialize(appDataStore, this, () => WayMarkFavorites_Control.RefreshFavorites());
            WayMarkFavorites_Control.Initialize(appDataStore, this);
            AddDeveloperToolsTab();
            InitializeCurrentFileChangeMonitor();
            WayMarkEditor_Control.WayMarksChanged += (_, _) => MarkWayMarkDirty();
            WayMarkEditor_Control.SelectLocalCharacterRequested += (_, _) => OpenLocalGameCharacterPicker();
            WayMarkEditor_Control.RecentFileRequested += (_, e) => OpenRecentWayMarkFile(e.FilePath);
            Title = DefaultWindowTitle;
            ApplySavedLayoutSettings();
            UpdateMaximizeRestoreButton();
            RefreshRecentFileMenu();
            RefreshMapDataSourceMenu();
        }

        protected override void OnClosed(EventArgs e)
        {
            ToastService.UnregisterSuccessHandler(ShowSuccessToast);
            base.OnClosed(e);
        }

        private void ShowSuccessToast(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ShowSuccessToast(message));
                return;
            }

            ToastNotification notification = new(message);
            toastNotifications.Insert(0, notification);
            while (toastNotifications.Count > 4)
            {
                toastNotifications.RemoveAt(toastNotifications.Count - 1);
            }

            DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromSeconds(2.6)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                toastNotifications.Remove(notification);
            };
            timer.Start();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDataVersionText();
            CharacterProfiles_Control.Initialize(
                appDataStore,
                this,
                RefreshBackupList,
                RefreshLocalCharacterSelectionAvailability,
                RefreshRecentFileMenu);
            BackupRestore_Control.Initialize(
                appDataStore,
                this,
                () => currentFilePath,
                GetCurrentFileBackupUserID,
                TryLoadConfigFileWithOverlayAsync,
                ConfirmSaveOrDiscardWayMarkChanges,
                ConfirmSaveOrDiscardCharacterChanges,
                () => SyncServerListIfNeededAsync(showFailureMessage: false),
                RefreshCharacterList);
            ToolSettings_Control.Initialize(
                appDataStore,
                this,
                RefreshBackupList,
                RefreshCharacterList,
                PrepareDataDirectoryMigration,
                FinishDataDirectoryMigration,
                RefreshServerListConsumers,
                RefreshMapDataConsumers,
                RefreshAppearanceSettings,
                StartLocalCharacterScan,
                ChangeMapDataSelectionAsync,
                ChangeMapDataOnlineSourceAsync,
                OpenUserMapDataEditorAsync,
                ShowMapDataOperationOverlay,
                HideMapDataOperationOverlay);
            LoadSettingsIntoUi();
            RefreshBackupList();
            RefreshCharacterList();
            ShowDataLoadWarnings();
            ShowMigrationReports();
            _ = CompleteMainWindowStartupAsync();
        }

        private async Task CompleteMainWindowStartupAsync()
        {
            try
            {
                await PromptForPendingUserMapDataRepairAsync();
                if (!IsLoaded)
                {
                    return;
                }

                ScheduleStartupWayMarkAction();
                RefreshLocalCharacterSelectionAvailability();
                _ = AutoDetectGameInstallDirectoryAndScanLocalCharactersAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Error(AppLogCategory.UI, "完成主窗口启动操作失败", ex);
                if (IsLoaded)
                {
                    AppMessageBox.Show(
                        this,
                        $"完成主窗口启动操作失败：{ex.Message}",
                        "启动操作失败",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private async Task PromptForPendingUserMapDataRepairAsync()
        {
            MapDataLoadResult? result = pendingUserMapDataRepairPrompt;
            pendingUserMapDataRepairPrompt = null;
            if (result == null)
            {
                return;
            }

            await PromptToRepairUserMapDataAsync(result);
        }

        private async Task AutoDetectGameInstallDirectoryAndScanLocalCharactersAsync()
        {
            if (!StartupLocalCharacterScanPolicy.ShouldRun(appDataStore.Settings))
            {
                return;
            }

            bool scanCompleted = false;
            try
            {
                GameInstallDirectoryUpdateResult result = await appDataStore.AutoDetectGameInstallDirectoryAsync();
                if (result == GameInstallDirectoryUpdateResult.NotFound)
                {
                    PromptForMissingGameInstallDirectory();
                    return;
                }

                if (result is GameInstallDirectoryUpdateResult.Updated or GameInstallDirectoryUpdateResult.Relocated)
                {
                    ToolSettings_Control.RefreshGameInstallDirectoryFromSettings();
                }

                if (result == GameInstallDirectoryUpdateResult.Relocated)
                {
                    AppMessageBox.Show(
                        this,
                        "检测到游戏位置移动，已重新获取游戏位置。",
                        "游戏位置已更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                scanCompleted = await ScanLocalCharactersAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warning(AppLogCategory.IO, "自动检测游戏安装目录失败", ex);
            }
            finally
            {
                MarkStartupLocalCharacterScanCompletedIfNeeded(scanCompleted);
            }
        }

        private void PromptForMissingGameInstallDirectory()
        {
            if (!appDataStore.Settings.ShowGameInstallDirectoryDetectionWarning)
            {
                return;
            }

            AppMessageBoxCheckBoxResult prompt = AppMessageBox.ShowWithCheckBox(
                this,
                "未能自动获取最终幻想 XIV 游戏安装目录。手动打开、编辑和保存标点文件不受影响；启动本地角色扫描和批量识别角色名、直接选择角色、角色活跃时间和基于游戏目录的自动备份暂不可用。\n\n打开真实角色目录中的 UISAVE.DAT 后，工具会再次尝试获取路径；也可以前往工具设置手动选择。是否现在前往工具设置？",
                "未获取游戏安装目录",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                "不再在启动时提示");

            if (prompt.IsChecked)
            {
                AppSettings settings = appDataStore.CreateSettingsSnapshot();
                settings.ShowGameInstallDirectoryDetectionWarning = false;
                try
                {
                    appDataStore.SaveSettings(settings);
                    ToolSettings_Control.LoadSettingsIntoUi();
                }
                catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException or IOException or UnauthorizedAccessException)
                {
                    AppLogger.Warning(AppLogCategory.IO, "保存游戏安装目录检测提示设置失败", ex);
                }
            }

            if (prompt.Result == MessageBoxResult.Yes)
            {
                MainTab_Control.SelectedItem = ToolSettings_TabItem;
                Dispatcher.BeginInvoke(ToolSettings_Control.FocusGameInstallDirectoryInput);
            }
        }

        private void MarkStartupLocalCharacterScanCompletedIfNeeded(bool scanCompleted)
        {
            if (!StartupLocalCharacterScanPolicy.ShouldMarkCompleted(appDataStore.Settings, scanCompleted))
            {
                return;
            }

            AppSettings settings = appDataStore.CreateSettingsSnapshot();
            settings.StartupLocalCharacterScanCompleted = true;
            try
            {
                appDataStore.SaveSettings(settings);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException or IOException or UnauthorizedAccessException)
            {
                AppLogger.Warning(AppLogCategory.IO, "保存启动本地角色扫描完成状态失败", ex);
            }
        }
    }
}
