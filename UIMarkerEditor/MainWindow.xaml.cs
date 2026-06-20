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

        public MainWindow(AppDataStore appDataStore)
        {
            this.appDataStore = appDataStore;
            InitializeComponent();
            ToastItems_Control.ItemsSource = toastNotifications;
            ToastService.RegisterSuccessHandler(ShowSuccessToast);
            WayMarkEditor_Control.Initialize(appDataStore, this, () => WayMarkFavorites_Control.RefreshFavorites());
            WayMarkFavorites_Control.Initialize(appDataStore, this);
            AddDeveloperToolsTab();
            InitializeCurrentFileChangeMonitor();
            WayMarkEditor_Control.WayMarksChanged += (_, _) => MarkWayMarkDirty();
            Title = DefaultWindowTitle;
            ApplySavedLayoutSettings();
            UpdateMaximizeRestoreButton();
            RefreshRecentFileMenu();
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
            CharacterProfiles_Control.Initialize(appDataStore, this, RefreshBackupList);
            BackupRestore_Control.Initialize(
                appDataStore,
                this,
                () => currentFilePath,
                filePath => LoadConfigFileWithOverlay(filePath),
                ConfirmSaveOrDiscardWayMarkChanges,
                ConfirmSaveOrDiscardCharacterChanges,
                RefreshCharacterList);
            ToolSettings_Control.Initialize(
                appDataStore,
                this,
                RefreshBackupList,
                RefreshCharacterList,
                RefreshServerListConsumers,
                RefreshMapDataConsumers,
                RefreshAppearanceSettings);
            LoadSettingsIntoUi();
            RefreshBackupList();
            RefreshCharacterList();
            ShowDataLoadWarnings();
            ScheduleStartupWayMarkAction();
        }

    }
}
