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
            Title = DefaultWindowTitle;
            UpdateMaximizeRestoreButton();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDataVersionText();
            CharacterProfiles_Control.Initialize(appDataStore, this, RefreshBackupList);
            BackupRestore_Control.Initialize(
                appDataStore,
                this,
                () => currentFilePath,
                LoadConfigFile,
                ConfirmSaveOrDiscardCharacterChanges,
                RefreshCharacterList);
            ToolSettings_Control.Initialize(appDataStore, this, RefreshBackupList, RefreshCharacterList);
            LoadSettingsIntoUi();
            RefreshBackupList();
            RefreshCharacterList();
            _ = SyncServerListIfNeededAsync();
        }

        private void OpenWayMarkFile()
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

        private void OpenWayMarkFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            OpenWayMarkFile();
        }

        private void ReloadWayMarkFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ReloadWayMarkFile();
        }

        private void SaveWayMarkFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SaveWayMarkFile();
        }

        private void CurrentWayMarkFileCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = HasLoadedWayMarkFile();
            e.Handled = true;
        }

        private bool HasLoadedWayMarkFile()
        {
            return !string.IsNullOrWhiteSpace(currentFilePath) && configUISave != null;
        }

        private void ReloadWayMarkFile()
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

        private void SaveWayMarkFile()
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
                UpdateCurrentFileStatus(filePath);
                List<WayMark> markerSection = configUISave.Marks.WayMarks;

                WayMarkEditor_Control.SetWayMarks(markerSection);

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
                currentFilePath = string.Empty;
                configUISave = null;
                ResetCurrentFileStatus();
                WayMarkEditor_Control.ClearWayMarks();
                CommandManager.InvalidateRequerySuggested();
                return;
            }

            CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateCurrentFileStatus(string filePath)
        {
            string fullPath = System.IO.Path.GetFullPath(filePath);
            string displayText = fullPath + CreateCharacterStatusSuffix(filePath);
            CurrentFileStatus_TextBlock.Text = displayText;
            CurrentFileStatus_TextBlock.ToolTip = displayText;
        }

        private void ResetCurrentFileStatus()
        {
            const string displayText = "未加载 UISAVE 文件";
            CurrentFileStatus_TextBlock.Text = displayText;
            CurrentFileStatus_TextBlock.ToolTip = displayText;
        }

        private string CreateCharacterStatusSuffix(string filePath)
        {
            string? folderUserID = AppDataStore.GetUserIDFromCharacterFolder(filePath);
            if (string.IsNullOrWhiteSpace(folderUserID)) return string.Empty;

            CharacterProfile? profile = appDataStore.Characters.FirstOrDefault(character =>
                string.Equals(character.UserID, folderUserID, StringComparison.OrdinalIgnoreCase));
            if (profile == null) return string.Empty;

            string characterName = profile.CharacterName.Trim();
            if (string.IsNullOrWhiteSpace(characterName)) return string.Empty;

            string dataCenter = profile.DataCenter.Trim();
            string world = profile.World.Trim();
            DataCenterAbbreviations.TryGetValue(dataCenter, out string? abbreviation);
            string dataCenterDisplay = abbreviation ?? dataCenter;
            string serverFirstChar = string.IsNullOrWhiteSpace(world) ? string.Empty : world[..1];
            string serverDisplay = string.Join("-", new[] { dataCenterDisplay, serverFirstChar }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            return string.IsNullOrWhiteSpace(serverDisplay)
                ? $" - {characterName}"
                : $" - {characterName}（{serverDisplay}）";
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

        private void MinimizeWindow_Button_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeRestoreWindow_Button_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowMaximizeRestore();
        }

        private void CloseWindow_Button_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeRestoreButton();
        }

        private void ToggleWindowMaximizeRestore()
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
        }

        private void UpdateMaximizeRestoreButton()
        {
            if (!IsInitialized) return;

            bool isMaximized = WindowState == WindowState.Maximized;
            MaximizeIcon_Rectangle.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
            RestoreIcon_Path.Visibility = isMaximized ? Visibility.Visible : Visibility.Collapsed;
            MaximizeRestoreWindow_Button.ToolTip = isMaximized ? "还原" : "最大化";
        }
    }
}
