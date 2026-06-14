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
                ConfirmSaveOrDiscardCharacterChanges,
                RefreshCharacterList);
            ToolSettings_Control.Initialize(
                appDataStore,
                this,
                RefreshBackupList,
                RefreshCharacterList,
                RefreshServerListConsumers,
                RefreshMapDataConsumers);
            LoadSettingsIntoUi();
            RefreshBackupList();
            RefreshCharacterList();
            ShowDataLoadWarnings();
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

                LoadConfigFile(filePath);
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

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void File_MenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
            RefreshRecentFileMenu();
        }

        private void RecentFile_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string filePath }) return;

            if (!File.Exists(filePath))
            {
                MessageBox.Show(this, "这个最近文件已经不存在。", "最近打开", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshRecentFileMenu();
                return;
            }

            LoadConfigFile(filePath);
        }

        private void ClearRecentFiles_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            appDataStore.ClearRecentFiles();
            RefreshRecentFileMenu();
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
                        AppLogger.Error(AppLogCategory.IO, $"保存前自动备份失败：{configUISave.FilePath}", ex);
                        MessageBox.Show($"保存前自动备份失败，已取消保存。\n{ex.Message}", "备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 在这里将修改后的数据写回UISAVE.DAT文件
                try
                {
                    configUISave.Save();
                }
                catch (UISaveFormatException ex)
                {
                    AppLogger.Error(AppLogCategory.UISaveFormat, $"保存 UISAVE.DAT 前结构校验失败：{configUISave.FilePath}", ex);
                    MessageBox.Show(this, $"UISAVE.DAT 结构校验失败，已取消保存，原文件未写入。\n\n文件：{configUISave.FilePath}\n\n诊断信息：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Error(AppLogCategory.IO, $"保存 UISAVE.DAT 失败：{configUISave.FilePath}", ex);
                    MessageBox.Show(this, $"保存 UISAVE.DAT 失败，原文件未确认写入完成。\n\n文件：{configUISave.FilePath}\n\n原因：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                MessageBox.Show("文件已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool LoadConfigFile(string filePath)
        {
            // 使用 ConfigUISave 类加载文件
            ConfigUISave loadedConfig;
            try
            {
                loadedConfig = new(filePath);
            }
            catch (UISaveFormatException ex)
            {
                AppLogger.Error(AppLogCategory.UISaveFormat, $"加载 UISAVE.DAT 格式失败：{filePath}", ex);
                MessageBox.Show(this, $"这个 UISAVE.DAT 的结构与当前工具已知格式不一致，已取消加载，当前文件保持不变。\n\n文件：{filePath}\n\n诊断信息：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error(AppLogCategory.IO, $"加载 UISAVE.DAT 失败：{filePath}", ex);
                MessageBox.Show(this, $"无法加载 UISAVE.DAT 文件。\n\n文件：{filePath}\n\n原因：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }

            SectionFMARKER? markerSection = loadedConfig.Marks;
            if (markerSection != null)
            {
                currentFilePath = filePath;
                configUISave = loadedConfig;
                RegisterLoadedCharacter(loadedConfig, filePath);
                UpdateCurrentFileStatus(filePath);
                appDataStore.AddRecentFile(filePath);
                RefreshRecentFileMenu();
                List<WayMark> wayMarks = markerSection.WayMarks;

                WayMarkEditor_Control.SetWayMarks(wayMarks);

                // 输出所有的enableFlag和regionID以供调试
                foreach (WayMark mark in wayMarks)
                {
                    // enableFlag 再用二进制显示
                    AppLogger.Debug(AppLogCategory.UI, $"RegionID: {mark.RegionID} -> EnableFlag: {mark.enableFlag} ({Convert.ToString(mark.enableFlag, 2).PadLeft(8, '0')})");
                }
            }
            else
            {
                MessageBox.Show(this, "无法在这个 UISAVE.DAT 中找到可编辑的 FMARKER 标点数据，当前已加载文件保持不变。", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }

            CommandManager.InvalidateRequerySuggested();
            return true;
        }

        private void UpdateCurrentFileStatus(string filePath)
        {
            string fullPath = System.IO.Path.GetFullPath(filePath);
            string displayText = BuildFileDisplayText(fullPath);
            CurrentFileStatus_TextBlock.Text = displayText;
            CurrentFileStatus_TextBlock.ToolTip = displayText;
        }

        private void ResetCurrentFileStatus()
        {
            const string displayText = "未加载 UISAVE 文件";
            CurrentFileStatus_TextBlock.Text = displayText;
            CurrentFileStatus_TextBlock.ToolTip = displayText;
        }

        private void RefreshRecentFileMenu()
        {
            RecentFiles_MenuItem.Items.Clear();
            Style subMenuItemStyle = (Style)FindResource("TitleBarSubMenuItemStyle");
            List<string> recentFiles = appDataStore.GetRecentFiles();

            RecentFiles_MenuItem.IsEnabled = true;
            if (recentFiles.Count == 0)
            {
                RecentFiles_MenuItem.Items.Add(new MenuItem
                {
                    Header = "暂无最近文件",
                    IsEnabled = false,
                    Style = subMenuItemStyle
                });
                return;
            }

            foreach (string filePath in recentFiles)
            {
                bool exists = File.Exists(filePath);
                RecentFiles_MenuItem.Items.Add(new MenuItem
                {
                    Header = BuildRecentFileDisplayName(filePath, exists),
                    ToolTip = exists ? filePath : $"{filePath}\n文件不存在",
                    Tag = filePath,
                    IsEnabled = exists,
                    Style = subMenuItemStyle
                });

                if (RecentFiles_MenuItem.Items[^1] is MenuItem item)
                {
                    item.Click += RecentFile_MenuItem_Click;
                }
            }

            RecentFiles_MenuItem.Items.Add(new Separator());
            RecentFiles_MenuItem.Items.Add(new MenuItem
            {
                Header = "清空最近记录",
                Style = subMenuItemStyle
            });

            if (RecentFiles_MenuItem.Items[^1] is MenuItem clearItem)
            {
                clearItem.Click += ClearRecentFiles_MenuItem_Click;
            }
        }

        private string BuildRecentFileDisplayName(string filePath, bool fileExists)
        {
            string displayName = BuildFileDisplayText(filePath);

            return fileExists ? displayName : $"{displayName}（文件不存在）";
        }

        private string BuildFileDisplayText(string filePath)
        {
            string fullPath = System.IO.Path.GetFullPath(filePath);
            string? folderUserID = AppDataStore.GetUserIDFromCharacterFolder(fullPath);
            if (string.IsNullOrWhiteSpace(folderUserID)) return fullPath;

            string characterName = BuildCharacterCompactName(folderUserID);
            return string.Equals(characterName, folderUserID, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : $"{characterName} - {fullPath}";
        }

        private string BuildCharacterCompactName(string userID)
        {
            CharacterProfile? profile = appDataStore.Characters.FirstOrDefault(character =>
                string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
            if (profile == null || string.IsNullOrWhiteSpace(profile.CharacterName)) return userID;

            string dataCenter = profile.DataCenter.Trim();
            string world = profile.World.Trim();
            DataCenterAbbreviations.TryGetValue(dataCenter, out string? abbreviation);
            string dataCenterDisplay = abbreviation ?? dataCenter;
            string serverFirstChar = string.IsNullOrWhiteSpace(world) ? string.Empty : world[..1];
            string serverDisplay = string.Join("-", new[] { dataCenterDisplay, serverFirstChar }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            return string.IsNullOrWhiteSpace(serverDisplay)
                ? profile.CharacterName
                : $"{profile.CharacterName}（{serverDisplay}）";
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
