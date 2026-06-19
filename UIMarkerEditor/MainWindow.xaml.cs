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

        public MainWindow(AppDataStore appDataStore)
        {
            this.appDataStore = appDataStore;
            InitializeComponent();
            WayMarkEditor_Control.Initialize(appDataStore, this, () => WayMarkFavorites_Control.RefreshFavorites());
            WayMarkFavorites_Control.Initialize(appDataStore, this);
            AddDeveloperToolsTab();
            WayMarkEditor_Control.WayMarksChanged += (_, _) => MarkWayMarkDirty();
            Title = DefaultWindowTitle;
            ApplySavedLayoutSettings();
            UpdateMaximizeRestoreButton();
            RefreshRecentFileMenu();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDataVersionText();
            CharacterProfiles_Control.Initialize(appDataStore, this, RefreshBackupList);
            BackupRestore_Control.Initialize(
                appDataStore,
                this,
                () => currentFilePath,
                filePath => LoadConfigFile(filePath),
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
