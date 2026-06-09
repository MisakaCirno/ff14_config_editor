using System.Diagnostics;
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
        private string currentFilePath = string.Empty;

        private ConfigUISave? configUISave = null;

        private Point dragStartPoint;
        private WayMark? draggedWayMark;
        private int currentDropTargetIndex = -1;
        private readonly ObservableCollection<MapData> regionOptions = [];
        private ICollectionView? regionOptionsView;
        private string regionFilterText = string.Empty;
        private bool suppressRegionTextChanged = false;
        private bool isSelectingRegionFromPopup = false;
        private bool isClearingRegionText = false;
        private readonly Dictionary<TextBox, CoordinateEditContext> coordinateEditContexts = [];
        private readonly Dictionary<TextBox, string> coordinateAcceptedTexts = [];
        private readonly HashSet<TextBox> coordinateTextChangeGuards = [];
        private ToolTip? activeCoordinateInputTip;
        private TextBox? activeCoordinateInputTipTarget;
        private System.Windows.Threading.DispatcherTimer? activeCoordinateInputTipTimer;
        private readonly AppDataStore appDataStore;
        private readonly ObservableCollection<CharacterProfile> characterEntries = [];
        private string selectedCharacterDataCenter = string.Empty;
        private string selectedCharacterWorld = string.Empty;
        private bool isCreatingCharacterProfile = false;
        private bool isCharacterDetailDirty = false;
        private bool suppressCharacterSelectionChanged = false;
        private bool suppressCharacterChangeTracking = false;

        private const int MinRawCoordinate = int.MinValue;
        private const int MaxRawCoordinate = int.MaxValue;
        private const int CoordinateScale = 1000;
        private const int MaxCoordinateTextLength = 12;
        private const string CoordinateInputTip =
            "坐标格式：\n-2147483.648 到 2147483.647的数字，最多 3 位小数。\n不可输入其他字符。";

        private readonly JsonSerializerOptions jsonOptions = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 给ComboBox用的ItemsSource
        private readonly List<string> PointShape =
        [
            "圆形八方",
            "方形八方",
        ];

        private readonly List<string> PointOrder =
        [
            "A1B2C3D4",
            "A2B3C4D1",
        ];

        private enum CoordinateAxis
        {
            X,
            Y,
            Z
        }

        private readonly record struct CoordinateEditContext(WayMarkPoint Point, CoordinateAxis Axis);

        public MainWindow(AppDataStore appDataStore)
        {
            this.appDataStore = appDataStore;
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Edit1_Grid.IsEnabled = false;
            Edit2_Grid.IsEnabled = false;
            RegisterCoordinateTextBoxPasteHandlers();
            AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);
            AddHandler(PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), true);
            UpdateDataVersionText();
            UpdateMoveButtonState();
            RefreshRegionOptions();

            PointShape_ComboBox.ItemsSource = PointShape;
            PointShape_ComboBox.SelectedIndex = 0;

            PointOrder_ComboBox.ItemsSource = PointOrder;
            PointOrder_ComboBox.SelectedIndex = 0;

            Character_DataGrid.ItemsSource = characterEntries;
            BackupRestore_Control.Initialize(
                appDataStore,
                this,
                () => currentFilePath,
                LoadConfigFile,
                ConfirmSaveOrDiscardCharacterChanges,
                RefreshCharacterList);
            LoadSettingsIntoUi();
            RefreshServerPicker();
            RefreshBackupList();
            RefreshCharacterList();
            _ = SyncServerListIfNeededAsync();
        }

        private void Load_Button_Click(object sender, RoutedEventArgs e)
        {
            // 打开文件对话框，只允许选择 UISAVE.dat
            Microsoft.Win32.OpenFileDialog openFileDialog = new()
            {
                Title = "选择 UISAVE.dat",
                Filter = "UISAVE.dat 文件 (UISAVE.dat)|UISAVE.dat",
                FilterIndex = 1,
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                // 强校验：必须是 UISAVE.dat（忽略大小写）
                if (!string.Equals(System.IO.Path.GetFileName(filePath), "UISAVE.dat", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("只能选择名为 UISAVE.dat 的文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                currentFilePath = filePath;
                LoadConfigFile(currentFilePath);
            }
        }
        private void Reload_Button_Click(object sender, RoutedEventArgs e)
        {
            // 重新加载标点列表
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                LoadConfigFile(currentFilePath);
            }
            else
            {
                MessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            // 保存修改后的UISAVE.DAT文件
            if (configUISave != null)
            {
                if (appDataStore.Settings.AutoBackupBeforeSave)
                {
                    try
                    {
                        appDataStore.CreateBackup(configUISave.FilePath);
                        RefreshBackupList();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"保存前自动备份失败，已取消保存。\n{ex.Message}", "备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 在这里将修改后的数据写回UISAVE.DAT文件
                configUISave.Save();
                MessageBox.Show("文件已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LoadConfigFile(string filePath)
        {
            // 使用 ConfigUISave 类加载文件
            configUISave = new(filePath);

            if (configUISave != null && configUISave.Marks != null)
            {
                RegisterLoadedCharacter(configUISave, filePath);
                List<WayMark> markerSection = configUISave.Marks.WayMarks;

                RefreshRegionOptions(markerSection.Select(mark => mark.RegionID));
                WayMark_ListBox.ItemsSource = markerSection;
                UpdateMoveButtonState();

                // 输出所有的enableFlag和regionID以供调试
                foreach (WayMark mark in markerSection)
                {
                    // enableFlag 再用二进制显示
                    Debug.WriteLine($"RegionID: {mark.RegionID} -> EnableFlag: {mark.enableFlag} ({Convert.ToString(mark.enableFlag, 2).PadLeft(8, '0')})");
                }
            }
            else
            {
                MessageBox.Show("无法加载UISAVE.DAT文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateMoveButtonState();
                return;
            }
        }

        private void RegisterLoadedCharacter(ConfigUISave loadedConfig, string filePath)
        {
            string userID = !string.IsNullOrWhiteSpace(loadedConfig.UserIDHex)
                ? loadedConfig.UserIDHex
                : AppDataStore.GetUserIDFromCharacterFolder(filePath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userID)) return;

            appDataStore.GetOrCreateCharacter(userID);
            RefreshCharacterList();
        }
    }
}
