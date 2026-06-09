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
        private readonly ObservableCollection<BackupMetadata> backupEntries = [];
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

            Backup_DataGrid.ItemsSource = backupEntries;
            Character_DataGrid.ItemsSource = characterEntries;
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

        private void RefreshBackupList()
        {
            backupEntries.Clear();
            foreach (BackupMetadata backup in appDataStore.LoadBackups())
            {
                FillBackupDisplayFields(backup);
                backupEntries.Add(backup);
            }

            UpdateBackupDetail(null);
        }

        private void RefreshCharacterList()
        {
            characterEntries.Clear();
            foreach (CharacterProfile profile in appDataStore.Characters.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                characterEntries.Add(profile);
            }

            if (Character_DataGrid.SelectedItem == null && !isCreatingCharacterProfile)
            {
                UpdateCharacterDetailVisibility(showDetail: false);
            }
        }

        private void RefreshServerPicker()
        {
            ServerArea_ListBox.ItemsSource = appDataStore.ServerList.Groups;
            UpdateServerPickerButtonText();
            suppressCharacterChangeTracking = true;
            SelectServer(selectedCharacterDataCenter, selectedCharacterWorld);
            suppressCharacterChangeTracking = false;
        }

        private async Task SyncServerListIfNeededAsync()
        {
            DateTime lastServerSyncCheck = appDataStore.ServerList.LastUpdated > appDataStore.ServerList.LastSyncAttempt
                ? appDataStore.ServerList.LastUpdated
                : appDataStore.ServerList.LastSyncAttempt;
            if (DateTime.Now - lastServerSyncCheck < TimeSpan.FromDays(7)) return;

            if (await appDataStore.TrySyncServerListAsync())
            {
                RefreshServerPicker();
            }
        }

        private void UpdateDataVersionText()
        {
            string versionText = string.IsNullOrWhiteSpace(appDataStore.MapDataVersion)
                ? "未知"
                : appDataStore.MapDataVersion;
            DataVersion_TextBlock.Text = $"当前版本：{versionText}";
        }

        private (string DataCenter, string World)? GetSelectedServer()
        {
            return string.IsNullOrWhiteSpace(selectedCharacterWorld)
                ? null
                : (selectedCharacterDataCenter, selectedCharacterWorld);
        }

        private void SelectServer(string dataCenter, string world)
        {
            selectedCharacterDataCenter = dataCenter;
            selectedCharacterWorld = world;

            ServerGroup? selectedGroup = appDataStore.ServerList.Groups.FirstOrDefault(group =>
                string.Equals(group.DataCenter, dataCenter, StringComparison.OrdinalIgnoreCase));
            selectedGroup ??= appDataStore.ServerList.Groups.FirstOrDefault(group =>
                group.Worlds.Any(candidateWorld => string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase)));
            selectedGroup ??= appDataStore.ServerList.Groups.FirstOrDefault();

            ServerArea_ListBox.SelectedItem = selectedGroup;
            ServerWorld_ListBox.ItemsSource = selectedGroup?.Worlds;
            ServerWorld_ListBox.SelectedItem = selectedGroup?.Worlds.FirstOrDefault(candidateWorld =>
                string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase));

            UpdateServerPickerButtonText();
        }

        private void UpdateServerPickerButtonText()
        {
            ServerPicker_TextBlock.Text = string.IsNullOrWhiteSpace(selectedCharacterWorld)
                ? "请选择服务器"
                : $"{selectedCharacterDataCenter} / {selectedCharacterWorld}";
        }

        private void ServerPicker_Button_Click(object sender, RoutedEventArgs e)
        {
            ServerPicker_Popup.IsOpen = true;
        }

        private void ServerArea_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerArea_ListBox.SelectedItem is ServerGroup group)
            {
                ServerWorld_ListBox.ItemsSource = group.Worlds;
            }
        }

        private void ServerWorld_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerArea_ListBox.SelectedItem is not ServerGroup group ||
                ServerWorld_ListBox.SelectedItem is not string world)
            {
                return;
            }

            selectedCharacterDataCenter = group.DataCenter;
            selectedCharacterWorld = world;
            UpdateServerPickerButtonText();
            ServerPicker_Popup.IsOpen = false;
            MarkCharacterDetailDirty();
        }

        private void LoadSettingsIntoUi()
        {
            DataDirectory_TextBox.Text = appDataStore.DataDirectory;
            MaxBackupCount_TextBox.Text = appDataStore.Settings.MaxBackupCount.ToString(CultureInfo.InvariantCulture);
            MaxBackupDays_TextBox.Text = appDataStore.Settings.MaxBackupDays.ToString(CultureInfo.InvariantCulture);
            AutoBackup_CheckBox.IsChecked = appDataStore.Settings.AutoBackupBeforeSave;
        }

        private void FillBackupDisplayFields(BackupMetadata backup)
        {
            CharacterProfile? profile = appDataStore.Characters.FirstOrDefault(character =>
                string.Equals(character.UserID, backup.EffectiveUserID, StringComparison.OrdinalIgnoreCase));

            backup.CharacterDisplayName = profile?.DisplayName ?? DisplayOptionalText(backup.EffectiveUserID);
            backup.CharacterNameDisplay = profile != null && !string.IsNullOrWhiteSpace(profile.CharacterName)
                ? profile.CharacterName
                : DisplayOptionalText(backup.EffectiveUserID);
            backup.ServerDisplayName = profile == null
                ? "无"
                : DisplayOptionalText(string.Join(" / ", new[] { profile.DataCenter, profile.World }
                    .Where(part => !string.IsNullOrWhiteSpace(part))));
        }

        private bool HasCharacterProfile(string userID)
        {
            return appDataStore.Characters.Any(character =>
                string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase) &&
                HasCharacterRemark(character));
        }

        private static bool HasCharacterRemark(CharacterProfile profile)
        {
            return !string.IsNullOrWhiteSpace(profile.CharacterName) ||
                !string.IsNullOrWhiteSpace(profile.DataCenter) ||
                !string.IsNullOrWhiteSpace(profile.World) ||
                !string.IsNullOrWhiteSpace(profile.Note);
        }

        private void Backup_DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBackupDetail(Backup_DataGrid.SelectedItem as BackupMetadata);
        }

        private void Backup_DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is DataGridRow row)
            {
                row.IsSelected = true;
                Backup_DataGrid.SelectedItem = row.Item;
                return;
            }

            Backup_DataGrid.SelectedItem = null;
        }

        private void Backup_ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            BackupMetadata? backup = Backup_DataGrid.SelectedItem as BackupMetadata;
            bool hasBackup = backup != null;
            bool hasBackupDirectory = backup != null && Directory.Exists(backup.BackupDirectory);
            bool hasValidUserID = backup != null && IsValidUserID(backup.EffectiveUserID);
            bool alreadyHasCharacterProfile = hasValidUserID && HasCharacterProfile(backup!.EffectiveUserID);
            bool canCreateCharacter = hasValidUserID && !alreadyHasCharacterProfile;

            RestoreBackup_MenuItem.IsEnabled = hasBackup;
            RestoreBackupAs_MenuItem.IsEnabled = hasBackup;
            DeleteBackup_MenuItem.IsEnabled = hasBackup;
            OpenBackupDirectory_MenuItem.IsEnabled = hasBackupDirectory;
            CreateCharacterFromBackup_MenuItem.IsEnabled = canCreateCharacter;
            CreateCharacterFromBackup_MenuItem.Header = backup == null
                ? "为此备份创建角色备注..."
                : alreadyHasCharacterProfile
                    ? "已有角色备注"
                    : hasValidUserID
                        ? "为此备份创建角色备注..."
                        : "无法创建角色备注";
        }

        private void CreateCharacterFromBackup_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmSaveOrDiscardCharacterChanges()) return;

            if (Backup_DataGrid.SelectedItem is not BackupMetadata backup)
            {
                MessageBox.Show("请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string userID = backup.EffectiveUserID;
            if (!IsValidUserID(userID))
            {
                MessageBox.Show("这个备份没有可用于创建角色备注的 16 位 User ID。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CharacterProfile? existingProfile = appDataStore.Characters.FirstOrDefault(character =>
                string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
            if (existingProfile != null && HasCharacterRemark(existingProfile))
            {
                MessageBox.Show("这个备份对应的角色已经有备注。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BackupCharacterProfileDialog dialog = new(userID, appDataStore.ServerList.Groups, existingProfile)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true) return;

            CharacterProfile profile = appDataStore.GetOrCreateCharacter(userID);
            profile.CharacterName = dialog.CharacterName;
            profile.DataCenter = dialog.DataCenter;
            profile.World = dialog.World;
            profile.Note = dialog.Note;
            profile.UpdatedAt = DateTime.Now;
            appDataStore.SaveCharacters();

            string selectedBackupId = backup.Id;
            RefreshCharacterList();
            RefreshBackupList();
            Backup_DataGrid.SelectedItem = backupEntries.FirstOrDefault(entry => entry.Id == selectedBackupId);
            MessageBox.Show("角色备注已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateBackupDetail(BackupMetadata? backup)
        {
            UpdateBackupActionButtons(backup);
            if (backup == null)
            {
                ClearBackupDetailFields();
                BackupSnapshot_TextBox.Text = string.Empty;
                BackupEmpty_Panel.Visibility = Visibility.Visible;
                BackupDetail_ScrollViewer.Visibility = Visibility.Collapsed;
                return;
            }

            BackupEmpty_Panel.Visibility = Visibility.Collapsed;
            BackupDetail_ScrollViewer.Visibility = Visibility.Visible;
            BackupDetail_BackupTime_TextBox.Text = backup.BackupTime.ToString("yyyy-MM-dd HH:mm:ss");
            BackupDetail_Character_TextBox.Text = backup.CharacterDisplayName;
            BackupDetail_OriginalPath_TextBox.Text = backup.OriginalFilePath;
            BackupDetail_FolderUserID_TextBox.Text = DisplayOptionalText(backup.FolderUserID);
            BackupDetail_FileUserID_TextBox.Text = DisplayOptionalText(backup.FileUserID);
            BackupDetail_SourceFileSize_TextBox.Text = $"{backup.SourceFileSize:N0} 字节";
            BackupDetail_SourceSha256_TextBox.Text = backup.SourceFileSha256;
            BackupDetail_BackupFile_TextBox.Text = backup.BackupFilePath;
            BackupSnapshot_TextBox.Text = backup.MarkerSnapshots.Count == 0
                ? "无"
                : string.Join(Environment.NewLine, backup.MarkerSnapshots.Select(snapshot => snapshot.DisplayText));
        }

        private void UpdateBackupActionButtons(BackupMetadata? backup)
        {
            bool hasBackup = backup != null;
            RestoreBackup_Button.IsEnabled = hasBackup;
            RestoreBackupAs_Button.IsEnabled = hasBackup;
            DeleteBackup_Button.IsEnabled = hasBackup;
            OpenBackupDirectory_Button.IsEnabled = backup != null && Directory.Exists(backup.BackupDirectory);
        }

        private void ClearBackupDetailFields()
        {
            BackupDetail_BackupTime_TextBox.Text = string.Empty;
            BackupDetail_Character_TextBox.Text = string.Empty;
            BackupDetail_OriginalPath_TextBox.Text = string.Empty;
            BackupDetail_FolderUserID_TextBox.Text = string.Empty;
            BackupDetail_FileUserID_TextBox.Text = string.Empty;
            BackupDetail_SourceFileSize_TextBox.Text = string.Empty;
            BackupDetail_SourceSha256_TextBox.Text = string.Empty;
            BackupDetail_BackupFile_TextBox.Text = string.Empty;
            BackupSnapshot_TextBox.Text = string.Empty;
        }

        private void RefreshBackups_Button_Click(object sender, RoutedEventArgs e)
        {
            RefreshBackupList();
        }

        private void RestoreBackup_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Backup_DataGrid.SelectedItem is not BackupMetadata backup)
            {
                MessageBox.Show("请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string warning = BuildRestoreWarning(backup, backup.OriginalFilePath);
            if (MessageBox.Show(warning, "确认还原备份", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                BackupMetadata? safetyBackup = null;
                if (File.Exists(backup.OriginalFilePath))
                {
                    safetyBackup = appDataStore.CreateBackup(backup.OriginalFilePath, cleanupAfterCreate: false);
                }

                appDataStore.RestoreBackup(backup, backup.OriginalFilePath);
                appDataStore.CleanupBackups(backup.BackupDirectory, safetyBackup?.BackupDirectory ?? string.Empty);
                RefreshBackupList();
                if (string.Equals(currentFilePath, backup.OriginalFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    LoadConfigFile(currentFilePath);
                }

                MessageBox.Show("备份已还原到原文件路径。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"还原失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestoreBackupAs_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Backup_DataGrid.SelectedItem is not BackupMetadata backup)
            {
                MessageBox.Show("请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Microsoft.Win32.SaveFileDialog saveFileDialog = new()
            {
                Title = "还原 UISAVE.DAT 到...",
                FileName = "UISAVE.DAT",
                Filter = "UISAVE.dat 文件 (UISAVE.dat)|UISAVE.dat|所有文件 (*.*)|*.*",
                InitialDirectory = Directory.Exists(backup.OriginalDirectory) ? backup.OriginalDirectory : null
            };

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                appDataStore.RestoreBackup(backup, saveFileDialog.FileName);
                MessageBox.Show("备份已还原到指定位置。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"还原失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteBackup_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Backup_DataGrid.SelectedItem is not BackupMetadata backup)
            {
                MessageBox.Show("请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show("确定要删除这个备份吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            appDataStore.DeleteBackup(backup);
            RefreshBackupList();
        }

        private void OpenBackupDirectory_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Backup_DataGrid.SelectedItem is BackupMetadata backup && Directory.Exists(backup.BackupDirectory))
            {
                OpenDirectory(backup.BackupDirectory);
            }
        }

        private void RefreshCharacters_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmSaveOrDiscardCharacterChanges()) return;

            isCreatingCharacterProfile = false;
            ClearCharacterDetailFields();
            RefreshCharacterList();
        }

        private void Character_DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (suppressCharacterSelectionChanged) return;

            CharacterProfile? selectedProfile = Character_DataGrid.SelectedItem as CharacterProfile;
            CharacterProfile? previousProfile = e.RemovedItems.OfType<CharacterProfile>().FirstOrDefault();
            if (isCharacterDetailDirty && !ConfirmSaveOrDiscardCharacterChanges())
            {
                SetCharacterSelection(previousProfile);
                return;
            }

            LoadCharacterProfileIntoDetail(selectedProfile);
        }

        private void Character_DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is DataGridRow row)
            {
                row.IsSelected = true;
                Character_DataGrid.SelectedItem = row.Item;
                return;
            }

            if (!ConfirmSaveOrDiscardCharacterChanges())
            {
                e.Handled = true;
                return;
            }

            Character_DataGrid.SelectedItem = null;
        }

        private void Character_ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            DeleteCharacter_MenuItem.IsEnabled = Character_DataGrid.SelectedItem is CharacterProfile;
        }

        private void NewCharacter_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmSaveOrDiscardCharacterChanges()) return;

            isCreatingCharacterProfile = true;
            SetCharacterSelection(null);
            ClearCharacterDetailFields();
            CharacterUserID_TextBox.IsReadOnly = false;
            UpdateCharacterDetailVisibility(showDetail: true);
            isCharacterDetailDirty = false;
            CharacterUserID_TextBox.Focus();
        }

        private void ClearCharacterDetailFields()
        {
            suppressCharacterChangeTracking = true;
            CharacterUserID_TextBox.Text = string.Empty;
            CharacterName_TextBox.Text = string.Empty;
            CharacterNote_TextBox.Text = string.Empty;
            SelectServer(string.Empty, string.Empty);
            suppressCharacterChangeTracking = false;
            isCharacterDetailDirty = false;
        }

        private void UpdateCharacterDetailVisibility(bool showDetail)
        {
            CharacterEmpty_Panel.Visibility = showDetail ? Visibility.Collapsed : Visibility.Visible;
            CharacterDetail_ScrollViewer.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
            DeleteCharacter_Button.IsEnabled = showDetail && !isCreatingCharacterProfile && Character_DataGrid.SelectedItem != null;
        }

        private void SaveCharacter_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!TrySaveCharacterProfile(showSuccessMessage: true, out string savedUserID)) return;

            RefreshCharacterListAndSelect(savedUserID);
            RefreshBackupList();
        }

        private void CharacterDetail_TextChanged(object sender, TextChangedEventArgs e)
        {
            MarkCharacterDetailDirty();
        }

        private void MarkCharacterDetailDirty()
        {
            if (suppressCharacterChangeTracking || CharacterDetail_ScrollViewer.Visibility != Visibility.Visible) return;

            isCharacterDetailDirty = true;
        }

        private bool ConfirmSaveOrDiscardCharacterChanges()
        {
            if (!isCharacterDetailDirty) return true;

            MessageBoxResult result = MessageBox.Show(
                "当前角色备注有未保存的修改。\n\n选择“是”保存修改，选择“否”放弃修改，选择“取消”继续编辑。",
                "未保存的角色备注",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.No)
            {
                isCharacterDetailDirty = false;
                return true;
            }

            if (!TrySaveCharacterProfile(showSuccessMessage: false, out string _)) return false;

            Character_DataGrid.Items.Refresh();
            RefreshBackupList();
            return true;
        }

        private bool TrySaveCharacterProfile(bool showSuccessMessage, out string savedUserID)
        {
            string userID = CharacterUserID_TextBox.Text.Trim().ToUpperInvariant();
            savedUserID = userID;
            if (string.IsNullOrWhiteSpace(userID))
            {
                MessageBox.Show("User ID 不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (!IsValidUserID(userID))
            {
                MessageBox.Show("User ID 必须是 16 位十六进制字符。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            (string DataCenter, string World)? selectedServer = GetSelectedServer();
            if (selectedServer == null)
            {
                MessageBox.Show("请选择角色所在服务器。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            CharacterProfile profile = appDataStore.GetOrCreateCharacter(userID);
            profile.CharacterName = CharacterName_TextBox.Text.Trim();
            profile.DataCenter = selectedServer.Value.DataCenter;
            profile.World = selectedServer.Value.World;
            profile.Note = CharacterNote_TextBox.Text.Trim();
            profile.UpdatedAt = DateTime.Now;
            appDataStore.SaveCharacters();
            if (!characterEntries.Any(character => string.Equals(character.UserID, profile.UserID, StringComparison.OrdinalIgnoreCase)))
            {
                characterEntries.Add(profile);
            }

            isCreatingCharacterProfile = false;
            isCharacterDetailDirty = false;
            CharacterUserID_TextBox.IsReadOnly = true;

            if (showSuccessMessage)
            {
                MessageBox.Show("角色备注已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return true;
        }

        private void RefreshCharacterListAndSelect(string userID)
        {
            suppressCharacterSelectionChanged = true;
            RefreshCharacterList();
            Character_DataGrid.SelectedItem = characterEntries.FirstOrDefault(character =>
                string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
            suppressCharacterSelectionChanged = false;
            LoadCharacterProfileIntoDetail(Character_DataGrid.SelectedItem as CharacterProfile);
        }

        private void SetCharacterSelection(CharacterProfile? profile)
        {
            suppressCharacterSelectionChanged = true;
            Character_DataGrid.SelectedItem = profile;
            suppressCharacterSelectionChanged = false;
            LoadCharacterProfileIntoDetail(profile);
        }

        private void LoadCharacterProfileIntoDetail(CharacterProfile? profile)
        {
            suppressCharacterChangeTracking = true;
            if (profile == null)
            {
                if (!isCreatingCharacterProfile)
                {
                    ClearCharacterDetailFields();
                    CharacterUserID_TextBox.IsReadOnly = false;
                    UpdateCharacterDetailVisibility(showDetail: false);
                }

                suppressCharacterChangeTracking = false;
                isCharacterDetailDirty = false;
                return;
            }

            isCreatingCharacterProfile = false;
            CharacterUserID_TextBox.IsReadOnly = true;
            CharacterUserID_TextBox.Text = profile.UserID;
            CharacterName_TextBox.Text = profile.CharacterName;
            SelectServer(profile.DataCenter, profile.World);
            CharacterNote_TextBox.Text = profile.Note;
            UpdateCharacterDetailVisibility(showDetail: true);
            suppressCharacterChangeTracking = false;
            isCharacterDetailDirty = false;
        }

        private void DeleteCharacter_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Character_DataGrid.SelectedItem is not CharacterProfile profile) return;
            if (MessageBox.Show("确定要删除这个角色备注吗？备份文件不会被删除。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            appDataStore.Characters.Remove(profile);
            appDataStore.SaveCharacters();
            isCreatingCharacterProfile = false;
            SetCharacterSelection(null);
            ClearCharacterDetailFields();
            UpdateCharacterDetailVisibility(showDetail: false);
            RefreshCharacterList();
            RefreshBackupList();
        }

        private void BrowseDataDirectory_Button_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new()
            {
                Title = "选择数据目录",
                InitialDirectory = Directory.Exists(DataDirectory_TextBox.Text) ? DataDirectory_TextBox.Text : appDataStore.DataDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                DataDirectory_TextBox.Text = dialog.FolderName;
            }
        }

        private void SaveSettings_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadPositiveInt(MaxBackupCount_TextBox, "最多保留备份数量", out int maxBackupCount) ||
                !TryReadPositiveInt(MaxBackupDays_TextBox, "最多保留天数", out int maxBackupDays))
            {
                return;
            }

            try
            {
                string requestedDataDirectory = DataDirectory_TextBox.Text.Trim();
                if (!string.Equals(requestedDataDirectory, appDataStore.DataDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBoxResult migrateResult = MessageBox.Show(
                        "是否将现有配置、角色备注和备份迁移到新目录？\n选择“否”将只切换目录，不复制旧数据。",
                        "迁移数据目录",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    if (migrateResult == MessageBoxResult.Cancel) return;

                    appDataStore.ChangeDataDirectory(requestedDataDirectory, migrateResult == MessageBoxResult.Yes);
                }

                appDataStore.SaveSettings(new AppSettings
                {
                    MaxBackupCount = maxBackupCount,
                    MaxBackupDays = maxBackupDays,
                    AutoBackupBeforeSave = AutoBackup_CheckBox.IsChecked == true
                });
                appDataStore.CleanupBackups();
                LoadSettingsIntoUi();
                RefreshBackupList();
                RefreshCharacterList();
                MessageBox.Show("设置已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetDataDirectory_Button_Click(object sender, RoutedEventArgs e)
        {
            DataDirectory_TextBox.Text = appDataStore.DefaultDataDirectory;
        }

        private void OpenDataDirectory_Button_Click(object sender, RoutedEventArgs e)
        {
            OpenDirectory(appDataStore.DataDirectory);
        }

        private static string BuildRestoreWarning(BackupMetadata backup, string targetFilePath)
        {
            string? targetFolderUserID = AppDataStore.GetUserIDFromCharacterFolder(targetFilePath);
            string userIDWarning = !string.IsNullOrWhiteSpace(targetFolderUserID) &&
                !string.IsNullOrWhiteSpace(backup.EffectiveUserID) &&
                !string.Equals(targetFolderUserID, backup.EffectiveUserID, StringComparison.OrdinalIgnoreCase)
                    ? $"\n\n警告：目标目录 User ID 为 {targetFolderUserID}，备份文件 User ID 为 {backup.EffectiveUserID}。"
                    : string.Empty;

            return
                "将把下面的备份文件还原到原文件路径，并覆盖目标文件。\n\n" +
                $"备份时间：{backup.BackupTime:yyyy-MM-dd HH:mm:ss}\n" +
                $"角色备注：{DisplayOptionalText(backup.CharacterDisplayName)}\n" +
                $"目录 User ID：{DisplayOptionalText(backup.FolderUserID)}\n" +
                $"文件 User ID：{DisplayOptionalText(backup.FileUserID)}\n\n" +
                $"原文件路径：\n{targetFilePath}\n\n" +
                $"备份文件路径：\n{backup.BackupFilePath}\n\n" +
                $"覆盖前会自动备份当前目标文件。确定继续吗？{userIDWarning}";
        }

        private static string DisplayOptionalText(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "无" : text;
        }

        private static bool IsValidUserID(string userID)
        {
            return userID.Length == 16 && userID.All(Uri.IsHexDigit);
        }

        private static bool TryReadPositiveInt(TextBox textBox, string displayName, out int value)
        {
            if (int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return true;
            }

            MessageBox.Show($"{displayName} 必须是大于 0 的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
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

        private void RefreshRegionOptions(IEnumerable<ushort>? extraRegionIds = null)
        {
            Dictionary<ushort, MapData> options = MapData.GetMapDataDisplayDict();
            if (extraRegionIds != null)
            {
                foreach (ushort regionId in extraRegionIds)
                {
                    if (!options.ContainsKey(regionId))
                    {
                        options[regionId] = new MapData(regionId, MapData.GetName(regionId));
                    }
                }
            }

            regionOptions.Clear();
            foreach (MapData option in options.Values.OrderBy(option => option.Index))
            {
                regionOptions.Add(option);
            }

            regionOptionsView ??= CollectionViewSource.GetDefaultView(regionOptions);
            regionOptionsView.Filter = FilterRegionOption;
            regionOptionsView.Refresh();
            RegionOptions_ListBox.ItemsSource = regionOptionsView;
        }

        private bool FilterRegionOption(object item)
        {
            if (item is not MapData mapData) return false;
            if (string.IsNullOrWhiteSpace(regionFilterText)) return true;

            string[] keywords = regionFilterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string keyword in keywords)
            {
                if (!mapData.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    !mapData.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    !mapData.Index.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void RegionSearch_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressRegionTextChanged) return;

            regionFilterText = RegionSearch_TextBox.Text.Trim();
            regionOptionsView?.Refresh();
            OpenRegionPopup();
        }

        private void RegionSearch_TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            PrepareRegionPopup();
            OpenRegionPopupAfterFocus();
        }

        private void RegionSearch_TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PrepareRegionPopup();
            OpenRegionPopupAfterFocus();
        }

        private void RegionSearch_TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            PrepareRegionPopup();
            OpenRegionPopupAfterFocus();
        }

        private void PrepareRegionPopup()
        {
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            RegionClear_Button.Visibility = Visibility.Visible;
        }

        private void OpenRegionPopupAfterFocus()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (RegionSearch_TextBox.IsKeyboardFocusWithin)
                {
                    RegionSearch_Popup.IsOpen = true;
                }
            });
        }

        private void RegionSearch_TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (isClearingRegionText) return;
            if (isSelectingRegionFromPopup) return;
            if (IsFocusInRegionOptions(e.NewFocus as DependencyObject)) return;

            CommitRegionSearchText();
            RegionClear_Button.Visibility = Visibility.Collapsed;
        }

        private void RegionSearch_TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitRegionSearchText();
                RegionSearch_Popup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (currentWayMark != null)
                {
                    SetRegionSearchText(currentWayMark.RegionID);
                }

                regionFilterText = string.Empty;
                regionOptionsView?.Refresh();
                RegionSearch_Popup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                OpenRegionPopup();
                RegionOptions_ListBox.Focus();
                if (RegionOptions_ListBox.SelectedIndex < 0 && RegionOptions_ListBox.Items.Count > 0)
                {
                    RegionOptions_ListBox.SelectedIndex = 0;
                }
                e.Handled = true;
            }
        }

        private void RegionDropDown_Button_Click(object sender, RoutedEventArgs e)
        {
            if (RegionSearch_Popup.IsOpen)
            {
                RegionSearch_Popup.IsOpen = false;
                return;
            }

            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            RegionSearch_TextBox.Focus();
            OpenRegionPopupAfterFocus();
        }

        private void RegionClear_Button_Click(object sender, RoutedEventArgs e)
        {
            RegionSearch_TextBox.Clear();
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            RegionSearch_TextBox.Focus();
            OpenRegionPopupAfterFocus();
            Dispatcher.BeginInvoke(() => isClearingRegionText = false);
        }

        private void RegionClear_Button_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isClearingRegionText = true;
        }

        private void RegionClear_Button_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Dispatcher.BeginInvoke(() => isClearingRegionText = false);
        }

        private void RegionOptions_ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isSelectingRegionFromPopup = true;
        }

        private void RegionOptions_ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is MapData selectedRegion)
            {
                RegionOptions_ListBox.SelectedItem = selectedRegion;
                CommitSelectedRegionOption(selectedRegion);
                e.Handled = true;
                return;
            }

            CommitSelectedRegionOption();
        }

        private void RegionOptions_ListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitSelectedRegionOption();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                RegionSearch_Popup.IsOpen = false;
                RegionSearch_TextBox.Focus();
                e.Handled = true;
            }
        }

        private bool IsFocusInRegionOptions(DependencyObject? focusedElement)
        {
            while (focusedElement != null)
            {
                if (focusedElement == RegionOptions_ListBox)
                {
                    return true;
                }

                focusedElement = VisualTreeHelper.GetParent(focusedElement);
            }

            return false;
        }

        private void RegionSearch_Popup_Closed(object sender, EventArgs e)
        {
            if (isClearingRegionText) return;
            if (isSelectingRegionFromPopup)
            {
                isSelectingRegionFromPopup = false;
                return;
            }

            CommitRegionSearchText();
        }

        private void OpenRegionPopup()
        {
            if (!RegionSearch_TextBox.IsKeyboardFocusWithin) return;

            RegionSearch_Popup.IsOpen = true;
        }

        private void CommitSelectedRegionOption()
        {
            if (RegionOptions_ListBox.SelectedItem is not MapData selectedRegion) return;
            CommitSelectedRegionOption(selectedRegion);
        }

        private void CommitSelectedRegionOption(MapData selectedRegion)
        {
            if (currentWayMark == null) return;

            currentWayMark.RegionID = selectedRegion.Index;
            SetRegionSearchText(selectedRegion.Index);
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            RegionSearch_Popup.IsOpen = false;
            isSelectingRegionFromPopup = false;
        }

        private void CommitRegionSearchText()
        {
            if (currentWayMark == null) return;

            ushort regionId = ResolveRegionIdFromText(RegionSearch_TextBox.Text);
            EnsureRegionOption(regionId);

            currentWayMark.RegionID = regionId;
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            SetRegionSearchText(regionId);
        }

        private ushort ResolveRegionIdFromText(string text)
        {
            string trimmedText = text.Trim();
            if (string.IsNullOrEmpty(trimmedText)) return 0;

            MapData? exactMatch = regionOptions.FirstOrDefault(option =>
                string.Equals(option.DisplayName, trimmedText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.Name, trimmedText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(option.Index.ToString(CultureInfo.InvariantCulture), trimmedText, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch.Index;
            }

            int leftParen = trimmedText.LastIndexOf('(');
            int rightParen = trimmedText.LastIndexOf(')');
            if (leftParen >= 0 && rightParen > leftParen &&
                ushort.TryParse(trimmedText[(leftParen + 1)..rightParen], out ushort parsedFromDisplayText))
            {
                return parsedFromDisplayText;
            }

            return ushort.TryParse(trimmedText, out ushort parsedRegionId) ? parsedRegionId : (ushort)0;
        }

        private void EnsureRegionOption(ushort regionId)
        {
            if (regionOptions.Any(option => option.Index == regionId)) return;

            regionOptions.Add(new MapData(regionId, MapData.GetName(regionId)));
            SortRegionOptions();
        }

        private void SortRegionOptions()
        {
            List<MapData> sortedOptions = [.. regionOptions.OrderBy(option => option.Index)];
            regionOptions.Clear();
            foreach (MapData option in sortedOptions)
            {
                regionOptions.Add(option);
            }
        }

        private void SetRegionSearchText(ushort regionId)
        {
            MapData? selectedMap = regionOptions.FirstOrDefault(option => option.Index == regionId);
            if (selectedMap == null)
            {
                EnsureRegionOption(regionId);
                selectedMap = regionOptions.FirstOrDefault(option => option.Index == regionId);
            }

            suppressRegionTextChanged = true;
            RegionSearch_TextBox.Text = selectedMap?.DisplayName ?? $"{MapData.GetName(regionId)}({regionId})";
            RegionSearch_TextBox.CaretIndex = RegionSearch_TextBox.Text.Length;
            suppressRegionTextChanged = false;
        }

        private void Coordinate_TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox && TryGetCoordinateEditContext(textBox, out CoordinateEditContext context))
            {
                coordinateEditContexts[textBox] = context;
                coordinateAcceptedTexts[textBox] = textBox.Text;
                textBox.ToolTip ??= CoordinateInputTip;
            }
        }

        private void Coordinate_TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (!coordinateEditContexts.TryGetValue(textBox, out CoordinateEditContext context) &&
                !TryGetCoordinateEditContext(textBox, out context))
            {
                return;
            }

            coordinateEditContexts.Remove(textBox);
            CommitOrRevertCoordinateText(textBox, context);
            coordinateAcceptedTexts.Remove(textBox);
        }

        private void Coordinate_TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox textBox) return;

            if (!coordinateEditContexts.TryGetValue(textBox, out CoordinateEditContext context) &&
                !TryGetCoordinateEditContext(textBox, out context))
            {
                return;
            }

            e.Handled = true;
            CommitOrRevertCoordinateText(textBox, context);
            textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void Coordinate_TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                e.Handled = !CanApplyCoordinateText(textBox, e.Text);
                if (e.Handled)
                {
                    ShowInvalidCoordinateFeedback(textBox);
                }
            }
        }

        private void Coordinate_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (coordinateTextChangeGuards.Contains(textBox)) return;
            if (!textBox.IsKeyboardFocusWithin) return;

            if (IsCoordinateEditingText(textBox.Text))
            {
                coordinateAcceptedTexts[textBox] = textBox.Text;
                return;
            }

            string fallbackText = coordinateAcceptedTexts.GetValueOrDefault(textBox, string.Empty);
            coordinateTextChangeGuards.Add(textBox);
            textBox.Text = fallbackText;
            textBox.CaretIndex = textBox.Text.Length;
            coordinateTextChangeGuards.Remove(textBox);

            ShowInvalidCoordinateFeedback(textBox);
        }

        private void Coordinate_TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                ShowInvalidCoordinateFeedback(textBox);
                return;
            }

            string pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!CanApplyCoordinateText(textBox, pastedText))
            {
                e.CancelCommand();
                ShowInvalidCoordinateFeedback(textBox);
            }
        }

        private void CommitOrRevertCoordinateText(TextBox textBox, CoordinateEditContext context)
        {
            if (TryParseCoordinateText(textBox.Text, out int rawCoordinate))
            {
                SetCoordinateValue(context.Point, context.Axis, rawCoordinate);
                CloseCoordinateInputTipFor(textBox);
            }
            else
            {
                ShowInvalidCoordinateFeedback(textBox);
            }

            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            textBox.CaretIndex = textBox.Text.Length;
        }

        private void RegisterCoordinateTextBoxPasteHandlers()
        {
            foreach (TextBox textBox in FindVisualChildren<TextBox>(Edit2_Grid))
            {
                DataObject.AddPastingHandler(textBox, Coordinate_TextBox_Pasting);
            }
        }

        private static bool CanApplyCoordinateText(TextBox textBox, string inputText)
        {
            string candidateText = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                .Insert(textBox.SelectionStart, inputText);

            return IsCoordinateEditingText(candidateText);
        }

        private static bool IsCoordinateEditingText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (text.Length > MaxCoordinateTextLength) return false;

            bool hasDecimalPoint = false;
            int decimalDigitCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (char.IsDigit(character))
                {
                    if (hasDecimalPoint)
                    {
                        decimalDigitCount++;
                        if (decimalDigitCount > 3) return false;
                    }

                    continue;
                }

                if (character == '.')
                {
                    if (hasDecimalPoint) return false;
                    hasDecimalPoint = true;
                    continue;
                }

                if (character == '-' && i == 0) continue;

                return false;
            }

            return !IsCompleteCoordinateText(text) || IsCoordinateTextInRange(text);
        }

        private bool TryGetCoordinateEditContext(TextBox textBox, out CoordinateEditContext context)
        {
            context = default;
            if ((currentWayMark ?? WayMark_ListBox.SelectedItem as WayMark) is not WayMark wayMark) return false;

            string[] nameParts = textBox.Name.Split('_');
            if (nameParts.Length < 3) return false;

            WayMarkPoint point = nameParts[0] switch
            {
                "A" => wayMark.A,
                "B" => wayMark.B,
                "C" => wayMark.C,
                "D" => wayMark.D,
                "One" => wayMark.One,
                "Two" => wayMark.Two,
                "Three" => wayMark.Three,
                "Four" => wayMark.Four,
                _ => null!
            };
            if (point == null) return false;

            CoordinateAxis axis = nameParts[1] switch
            {
                "X" => CoordinateAxis.X,
                "Y" => CoordinateAxis.Y,
                "Z" => CoordinateAxis.Z,
                _ => (CoordinateAxis)(-1)
            };
            if (!Enum.IsDefined(axis)) return false;

            context = new CoordinateEditContext(point, axis);
            return true;
        }

        private static bool IsCompleteCoordinateText(string text)
        {
            string trimmedText = text.Trim();
            return trimmedText is not ("" or "-" or "." or "-.");
        }

        private static bool IsCoordinateTextInRange(string text)
        {
            return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) &&
                value >= (decimal)MinRawCoordinate / CoordinateScale &&
                value <= (decimal)MaxRawCoordinate / CoordinateScale;
        }

        private static bool TryParseCoordinateText(string text, out int rawCoordinate)
        {
            rawCoordinate = 0;
            string trimmedText = text.Trim();
            if (!IsCompleteCoordinateText(trimmedText) || !IsCoordinateEditingText(trimmedText))
            {
                return false;
            }

            if (decimal.TryParse(trimmedText, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
            {
                decimal rawValue = value * CoordinateScale;
                if (rawValue < MinRawCoordinate || rawValue > MaxRawCoordinate)
                {
                    return false;
                }

                rawCoordinate = (int)rawValue;
                return true;
            }

            return false;
        }

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (activeCoordinateInputTipTarget == null) return;

            TextBox? clickedTextBox = FindVisualParent<TextBox>(e.OriginalSource as DependencyObject);
            if (clickedTextBox != activeCoordinateInputTipTarget)
            {
                CloseActiveCoordinateInputTip();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (activeCoordinateInputTipTarget?.IsKeyboardFocusWithin == false)
            {
                CloseActiveCoordinateInputTip();
            }
        }

        private void ShowInvalidCoordinateFeedback(TextBox textBox)
        {
            FlashInvalidCoordinateInput(textBox);
            ShowCoordinateInputTip(textBox);
        }

        private static void FlashInvalidCoordinateInput(TextBox textBox)
        {
            textBox.Background = new SolidColorBrush(Color.FromRgb(255, 225, 225));
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 60, 60));

            System.Windows.Threading.DispatcherTimer timer = new()
            {
                Interval = TimeSpan.FromMilliseconds(220)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                textBox.ClearValue(Control.BackgroundProperty);
                textBox.ClearValue(Control.BorderBrushProperty);
            };
            timer.Start();
        }

        private void ShowCoordinateInputTip(TextBox textBox)
        {
            CloseActiveCoordinateInputTip();

            ToolTip toolTip = textBox.ToolTip as ToolTip ?? new ToolTip
            {
                Content = CoordinateInputTip
            };
            toolTip.Content = CoordinateInputTip;
            toolTip.PlacementTarget = textBox;
            textBox.ToolTip = toolTip;
            toolTip.IsOpen = true;

            activeCoordinateInputTip = toolTip;
            activeCoordinateInputTipTarget = textBox;
            activeCoordinateInputTipTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            activeCoordinateInputTipTimer.Tick += (_, _) =>
            {
                CloseActiveCoordinateInputTip();
            };
            activeCoordinateInputTipTimer.Start();
        }

        private void CloseActiveCoordinateInputTip()
        {
            activeCoordinateInputTipTimer?.Stop();
            activeCoordinateInputTipTimer = null;

            if (activeCoordinateInputTip != null)
            {
                activeCoordinateInputTip.IsOpen = false;
            }

            activeCoordinateInputTip = null;
            activeCoordinateInputTipTarget = null;
        }

        private void CloseCoordinateInputTipFor(TextBox textBox)
        {
            if (activeCoordinateInputTipTarget == textBox)
            {
                CloseActiveCoordinateInputTip();
            }
        }

        private static void SetCoordinateValue(WayMarkPoint point, CoordinateAxis axis, int rawCoordinate)
        {
            switch (axis)
            {
                case CoordinateAxis.X:
                    point.X = rawCoordinate;
                    break;
                case CoordinateAxis.Y:
                    point.Y = rawCoordinate;
                    break;
                case CoordinateAxis.Z:
                    point.Z = rawCoordinate;
                    break;
            }
        }

        private void MoveUp_Button_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedWayMark(-1);
        }

        private void MoveDown_Button_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedWayMark(1);
        }

        private void MoveSelectedWayMark(int offset)
        {
            if (WayMark_ListBox.SelectedItem is not WayMark selectedMark)
            {
                MessageBox.Show("请先选择一个要移动的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<WayMark>? wayMarks = GetWayMarks();
            if (wayMarks == null) return;

            int currentIndex = wayMarks.IndexOf(selectedMark);
            int targetIndex = currentIndex + offset;
            MoveWayMark(currentIndex, targetIndex);
        }

        private void MoveWayMark(int sourceIndex, int targetIndex)
        {
            List<WayMark>? wayMarks = GetWayMarks();
            if (wayMarks == null) return;
            if (sourceIndex < 0 || sourceIndex >= wayMarks.Count) return;
            if (targetIndex < 0 || targetIndex >= wayMarks.Count) return;
            if (sourceIndex == targetIndex) return;

            WayMark movedMark = wayMarks[sourceIndex];
            wayMarks.RemoveAt(sourceIndex);
            wayMarks.Insert(targetIndex, movedMark);

            WayMark_ListBox.Items.Refresh();
            WayMark_ListBox.SelectedItem = movedMark;
            WayMark_ListBox.ScrollIntoView(movedMark);
            UpdateMoveButtonState();
        }

        private List<WayMark>? GetWayMarks()
        {
            return configUISave?.Marks?.WayMarks;
        }

        private IEnumerable<ushort> GetLoadedRegionIds()
        {
            return GetWayMarks()?.Select(mark => mark.RegionID) ?? [];
        }

        private void UpdateMoveButtonState()
        {
            int selectedIndex = WayMark_ListBox.SelectedIndex;
            int itemCount = WayMark_ListBox.Items.Count;

            MoveUp_Button.IsEnabled = selectedIndex > 0;
            MoveDown_Button.IsEnabled = selectedIndex >= 0 && selectedIndex < itemCount - 1;
        }

        private void WayMark_ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragStartPoint = e.GetPosition(null);
            draggedWayMark = null;

            ListBoxItem? item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.DataContext is WayMark wayMark)
            {
                draggedWayMark = wayMark;
                WayMark_ListBox.SelectedItem = wayMark;
            }
        }

        private void WayMark_ListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (draggedWayMark is not WayMark draggedMark) return;

            Point currentPosition = e.GetPosition(null);
            Vector diff = dragStartPoint - currentPosition;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            ShowDragPreview(draggedMark, e.GetPosition(WayMark_ListBox));
            DragDrop.DoDragDrop(WayMark_ListBox, draggedMark, DragDropEffects.Move);
            HideDragVisuals();
            draggedWayMark = null;
        }

        private void WayMark_ListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(WayMark)) is not WayMark draggedMark)
            {
                e.Effects = DragDropEffects.None;
                HideDragVisuals();
                return;
            }

            List<WayMark>? wayMarks = GetWayMarks();
            if (wayMarks == null)
            {
                e.Effects = DragDropEffects.None;
                HideDragVisuals();
                return;
            }

            Point position = e.GetPosition(WayMark_ListBox);
            currentDropTargetIndex = GetVisualDropTargetIndex(e.OriginalSource as DependencyObject, position, wayMarks.Count);
            UpdateDropIndicator(currentDropTargetIndex);
            ShowDragPreview(draggedMark, position);

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void WayMark_ListBox_DragLeave(object sender, DragEventArgs e)
        {
            HideDragVisuals();
        }

        private void WayMark_ListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(WayMark)) is not WayMark draggedMark) return;

            List<WayMark>? wayMarks = GetWayMarks();
            if (wayMarks == null) return;

            int sourceIndex = wayMarks.IndexOf(draggedMark);
            int targetIndex = currentDropTargetIndex >= 0
                ? currentDropTargetIndex
                : GetVisualDropTargetIndex(e.OriginalSource as DependencyObject, e.GetPosition(WayMark_ListBox), wayMarks.Count);
            if (sourceIndex < 0 || targetIndex < 0) return;

            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            MoveWayMark(sourceIndex, targetIndex);
            HideDragVisuals();
        }

        private int GetVisualDropTargetIndex(DependencyObject? source, Point position, int itemCount)
        {
            ListBoxItem? targetItem = FindVisualParent<ListBoxItem>(source);
            if (targetItem?.DataContext is WayMark targetMark)
            {
                int targetIndex = WayMark_ListBox.Items.IndexOf(targetMark);
                Point itemPosition = position;
                if (source != null)
                {
                    itemPosition = Mouse.GetPosition(targetItem);
                }

                return itemPosition.Y > targetItem.ActualHeight / 2 ? targetIndex + 1 : targetIndex;
            }

            if (position.Y <= 0)
            {
                return 0;
            }

            return itemCount;
        }

        private void UpdateDropIndicator(int insertionIndex)
        {
            double y = GetDropIndicatorY(insertionIndex);
            if (double.IsNaN(y))
            {
                DropIndicator_Line.Visibility = Visibility.Collapsed;
                return;
            }

            DropIndicator_Line.Width = Math.Max(0, WayMark_ListBox.ActualWidth - 8);
            Canvas.SetLeft(DropIndicator_Line, 4);
            Canvas.SetTop(DropIndicator_Line, y);
            DropIndicator_Line.Visibility = Visibility.Visible;
        }

        private double GetDropIndicatorY(int insertionIndex)
        {
            int itemCount = WayMark_ListBox.Items.Count;
            if (itemCount == 0) return 2;

            int containerIndex = Math.Clamp(insertionIndex, 0, itemCount - 1);
            if (WayMark_ListBox.ItemContainerGenerator.ContainerFromIndex(containerIndex) is not ListBoxItem item)
            {
                return double.NaN;
            }

            Point itemTop = item.TranslatePoint(new Point(0, 0), DragOverlay_Canvas);
            return insertionIndex >= itemCount
                ? itemTop.Y + item.ActualHeight - 1
                : itemTop.Y - 1;
        }

        private void ShowDragPreview(WayMark wayMark, Point position)
        {
            DragPreview_TextBlock.Text = $"{MapData.GetName(wayMark.RegionID)}({wayMark.RegionID})";
            DragPreview_Border.Width = Math.Max(0, WayMark_ListBox.ActualWidth - 8);
            DragPreview_Border.Visibility = Visibility.Visible;

            Canvas.SetLeft(DragPreview_Border, 4);
            Canvas.SetTop(DragPreview_Border, Math.Min(position.Y + 12, Math.Max(0, WayMark_ListBox.ActualHeight - 36)));
        }

        private void HideDragVisuals()
        {
            currentDropTargetIndex = -1;
            DropIndicator_Line.Visibility = Visibility.Collapsed;
            DragPreview_Border.Visibility = Visibility.Collapsed;
        }

        private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T matched)
                {
                    return matched;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject source) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(source, i);
                if (child is T matched)
                {
                    yield return matched;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

        private void Import_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WayMark_ListBox.SelectedItem is not WayMark currentMark)
                {
                    MessageBox.Show("请先选择一个要导入到的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string json = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(json))
                {
                    MessageBox.Show("剪贴板内容为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MarkerShare? markerShare = JsonSerializer.Deserialize<MarkerShare>(json);
                if (markerShare == null)
                {
                    MessageBox.Show("无法解析剪贴板中的JSON数据。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Update currentMark
                currentMark.RegionID = (ushort)markerShare.MapID;
                RefreshRegionOptions(GetLoadedRegionIds());
                SetRegionSearchText(currentMark.RegionID);

                static void UpdatePoint(WayMarkPoint point, MarkerSharePoint sharePoint, Action<bool> setEnabled)
                {
                    point.FloatX = (float)sharePoint.X;
                    point.FloatY = (float)sharePoint.Y;
                    point.FloatZ = (float)sharePoint.Z;
                    setEnabled(sharePoint.Active);
                }

                UpdatePoint(currentMark.A, markerShare.A, val => currentMark.AEnabled = val);
                UpdatePoint(currentMark.B, markerShare.B, val => currentMark.BEnabled = val);
                UpdatePoint(currentMark.C, markerShare.C, val => currentMark.CEnabled = val);
                UpdatePoint(currentMark.D, markerShare.D, val => currentMark.DEnabled = val);
                UpdatePoint(currentMark.One, markerShare.One, val => currentMark.OneEnabled = val);
                UpdatePoint(currentMark.Two, markerShare.Two, val => currentMark.TwoEnabled = val);
                UpdatePoint(currentMark.Three, markerShare.Three, val => currentMark.ThreeEnabled = val);
                UpdatePoint(currentMark.Four, markerShare.Four, val => currentMark.FourEnabled = val);

                // Update timestamp
                currentMark.timestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

                // 强制更新UI（如果需要）
                // 属性变更应该会自动通知UI

                MessageBox.Show("导入成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WayMark_ListBox.SelectedItem is not WayMark currentMark)
                {
                    MessageBox.Show("请先选择一个要导出的标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MarkerShare markerShare = new()
                {
                    MapID = currentMark.RegionID,
                    Name = MapData.GetName(currentMark.RegionID)
                };

                static MarkerSharePoint CreatePoint(WayMarkPoint point, bool active)
                {
                    return new MarkerSharePoint
                    {
                        X = double.Parse(FormatCoordinate(point.FloatX)),
                        Y = double.Parse(FormatCoordinate(point.FloatY)),
                        Z = double.Parse(FormatCoordinate(point.FloatZ)),
                        Active = active
                    };
                }

                markerShare.A = CreatePoint(currentMark.A, currentMark.AEnabled);
                markerShare.B = CreatePoint(currentMark.B, currentMark.BEnabled);
                markerShare.C = CreatePoint(currentMark.C, currentMark.CEnabled);
                markerShare.D = CreatePoint(currentMark.D, currentMark.DEnabled);
                markerShare.One = CreatePoint(currentMark.One, currentMark.OneEnabled);
                markerShare.Two = CreatePoint(currentMark.Two, currentMark.TwoEnabled);
                markerShare.Three = CreatePoint(currentMark.Three, currentMark.ThreeEnabled);
                markerShare.Four = CreatePoint(currentMark.Four, currentMark.FourEnabled);

                string json = JsonSerializer.Serialize(markerShare, jsonOptions);

                // 兜底方案，如果没法直接复制成功，弹出一个窗口让用户复制
                try
                {
                    Clipboard.SetText(json);
                    MessageBox.Show("导出成功！\nJSON数据已复制到剪贴板。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch
                {
                    // 弹出窗口
                    Window copyWindow = new()
                    {
                        Title = "复制标点数据",
                        Width = 400,
                        Height = 300,
                        Content = new TextBox
                        {
                            Text = json,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                        },
                        // 设置窗口所有者和启动位置，确保弹出窗口在主窗口中央
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    copyWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败!\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private WayMark? currentWayMark = null;

        private void WayMark_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (currentWayMark != null)
            {
                currentWayMark.PropertyChanged -= OnWayMarkPropertyChanged;
                UnsubscribeWayMarkPoints(currentWayMark);
            }

            if (WayMark_ListBox.SelectedItem is WayMark selectedMark)
            {
                currentWayMark = selectedMark;
                currentWayMark.PropertyChanged += OnWayMarkPropertyChanged;
                SubscribeWayMarkPoints(currentWayMark);
                SetRegionSearchText(currentWayMark.RegionID);
                UpdatePreview();

                Edit1_Grid.IsEnabled = true;
                Edit2_Grid.IsEnabled = true;
            }
            else
            {
                currentWayMark = null;
                Preview_Canvas.Children.Clear();

                Edit1_Grid.IsEnabled = false;
                Edit2_Grid.IsEnabled = false;
            }

            UpdateMoveButtonState();
        }

        private void SubscribeWayMarkPoints(WayMark mark)
        {
            mark.A.PropertyChanged += OnPointPropertyChanged;
            mark.B.PropertyChanged += OnPointPropertyChanged;
            mark.C.PropertyChanged += OnPointPropertyChanged;
            mark.D.PropertyChanged += OnPointPropertyChanged;
            mark.One.PropertyChanged += OnPointPropertyChanged;
            mark.Two.PropertyChanged += OnPointPropertyChanged;
            mark.Three.PropertyChanged += OnPointPropertyChanged;
            mark.Four.PropertyChanged += OnPointPropertyChanged;
        }

        private void UnsubscribeWayMarkPoints(WayMark mark)
        {
            mark.A.PropertyChanged -= OnPointPropertyChanged;
            mark.B.PropertyChanged -= OnPointPropertyChanged;
            mark.C.PropertyChanged -= OnPointPropertyChanged;
            mark.D.PropertyChanged -= OnPointPropertyChanged;
            mark.One.PropertyChanged -= OnPointPropertyChanged;
            mark.Two.PropertyChanged -= OnPointPropertyChanged;
            mark.Three.PropertyChanged -= OnPointPropertyChanged;
            mark.Four.PropertyChanged -= OnPointPropertyChanged;
        }

        private void OnWayMarkPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void Preview_Container_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is FrameworkElement container)
            {
                double size = Math.Min(container.ActualWidth, container.ActualHeight);
                if (size > 0)
                {
                    Preview_Canvas.Width = size;
                    Preview_Canvas.Height = size;
                    UpdatePreview();
                }
            }
        }

        private void Scale_Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            Preview_Canvas.Children.Clear();
            if (currentWayMark == null) return;

            // Collect active points
            List<(string Name, WayMarkPoint Point)> points = [];
            if (currentWayMark.AEnabled) points.Add(("A", currentWayMark.A));
            if (currentWayMark.BEnabled) points.Add(("B", currentWayMark.B));
            if (currentWayMark.CEnabled) points.Add(("C", currentWayMark.C));
            if (currentWayMark.DEnabled) points.Add(("D", currentWayMark.D));
            if (currentWayMark.OneEnabled) points.Add(("1", currentWayMark.One));
            if (currentWayMark.TwoEnabled) points.Add(("2", currentWayMark.Two));
            if (currentWayMark.ThreeEnabled) points.Add(("3", currentWayMark.Three));
            if (currentWayMark.FourEnabled) points.Add(("4", currentWayMark.Four));

            if (points.Count == 0) return;

            // Calculate BBOX
            float minX = points.Min(p => p.Point.FloatX);
            float maxX = points.Max(p => p.Point.FloatX);
            float minZ = points.Min(p => p.Point.FloatZ);
            float maxZ = points.Max(p => p.Point.FloatZ);

            float width = maxX - minX;
            float height = maxZ - minZ;

            // Check if all points are at the same spot or width/height is 0
            if (width < 1) width = 10;
            if (height < 1) height = 10;

            // Add padding (Requirement 3)
            float paddingX = width * 0.1f;
            float paddingZ = height * 0.1f;

            if (paddingX < 1) paddingX = 1;
            if (paddingZ < 1) paddingZ = 1;

            float displayMinX = minX - paddingX;
            float displayMaxX = maxX + paddingX;
            float displayMinZ = minZ - paddingZ;
            float displayMaxZ = maxZ + paddingZ;

            float displayWidth = displayMaxX - displayMinX;
            float displayHeight = displayMaxZ - displayMinZ;

            double canvasSize = Preview_Canvas.Width;
            if (double.IsNaN(canvasSize) || canvasSize <= 0) return;

            // Scale: pixels per game-unit. Fit display rect into square canvas.
            float maxDim = Math.Max(displayWidth, displayHeight);
            double scale = canvasSize / maxDim;

            // Image size based on slider
            double scaleRatio = Scale_Slider != null ? Scale_Slider.Value : 0.1;
            double markerSize = canvasSize * scaleRatio;

            // Recalculate padding based on marker size to prevent clipping
            // We need padding (in game units) such that: padding * scale >= markerSize / 2
            // Since padding affects scale, this is an iterative process or solvable equation.
            // Simplified approach: Calculate scale without padding first to estimate, or just ensure display rect is large enough.

            // Formula: minPadding = (maxContentDim * scaleRatio) / (2 * (1 - scaleRatio))
            // Ensure denominator is not 0
            if (scaleRatio >= 1.0) scaleRatio = 0.99;

            float maxContentDim = Math.Max(width, height);
            float requiredPadding = (float)((maxContentDim * scaleRatio) / (2 * (1 - scaleRatio)));

            // Apply minimum padding (10%) or required padding, whichever is larger
            paddingX = Math.Max(paddingX, requiredPadding);
            paddingZ = Math.Max(paddingZ, requiredPadding);

            displayMinX = minX - paddingX;
            displayMaxX = maxX + paddingX;
            displayMinZ = minZ - paddingZ;
            displayMaxZ = maxZ + paddingZ;

            displayWidth = displayMaxX - displayMinX;
            displayHeight = displayMaxZ - displayMinZ;

            // Recalculate scale
            maxDim = Math.Max(displayWidth, displayHeight);
            scale = canvasSize / maxDim;

            // Recalculate markerSize (pixels) based on new scale ratio (relative to canvas, so it stays same)
            // markerSize = canvasSize * scaleRatio; (Already set)

            double contentWidthPx = displayWidth * scale;
            double contentHeightPx = displayHeight * scale;

            // Center content in canvas
            double offsetX = (canvasSize - contentWidthPx) / 2;
            double offsetY = (canvasSize - contentHeightPx) / 2;

            foreach ((string Name, WayMarkPoint Point) p in points)
            {
                Image img = new();
                string imgName = p.Name.ToLower();

                try
                {
                    img.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Image/s_{imgName}.png"));
                }
                catch
                {
                }

                img.Width = markerSize;
                img.Height = markerSize;

                double relativeX = p.Point.FloatX - displayMinX;
                double relativeZ = p.Point.FloatZ - displayMinZ;

                double left = relativeX * scale + offsetX - (markerSize / 2);

                // Z grows down
                double top = relativeZ * scale + offsetY - (markerSize / 2);

                Shape bgShape;
                // Check if name starts with digit (1-4) or letter (A-D)
                if (!string.IsNullOrEmpty(p.Name) && char.IsDigit(p.Name[0]))
                {
                    bgShape = new Rectangle(); // 1-4: Square background
                }
                else
                {
                    bgShape = new Ellipse(); // A-D: Circle background
                }

                bgShape.Width = markerSize;
                bgShape.Height = markerSize;
                // Semi-transparent black background
                bgShape.Fill = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

                Canvas.SetLeft(bgShape, left);
                Canvas.SetTop(bgShape, top);
                Preview_Canvas.Children.Add(bgShape);

                Canvas.SetLeft(img, left);
                Canvas.SetTop(img, top);
                Preview_Canvas.Children.Add(img);
            }
        }

        private void ShareWebsite_Button_Click(object sender, RoutedEventArgs e)
        {
            // 打开网站：https://souma.diemoe.net/ff14-overlay-vue/#/zoneMacro?OVERLAY_WS=ws://127.0.0.1:10501/ws&lang=zhCn
            string url = "https://souma.diemoe.net/ff14-overlay-vue/#/zoneMacro?OVERLAY_WS=ws://127.0.0.1:10501/ws&lang=zhCn";

            try
            {
                using Process? _ = Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FormatCoordinate(float value)
        {
            // 四舍五入保留最多四位小数
            return Math.Round(value, 4).ToString("F4");
        }

        private void SetShapePos_Button_Click(object sender, RoutedEventArgs e)
        {
            if (currentWayMark == null)
            {
                MessageBox.Show("请先选择一个要设置的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryReadDouble(ShapeDistance_TextBox, "与中心点间距", out double distance) ||
                !TryReadDouble(ShapeCenterX_TextBox, "中心点 X", out double centerX) ||
                !TryReadDouble(ShapeCenterY_TextBox, "中心点 Y", out double centerY) ||
                !TryReadDouble(ShapeCenterZ_TextBox, "中心点 Z", out double centerZ))
            {
                return;
            }

            if (distance <= 0)
            {
                MessageBox.Show("与中心点间距必须大于 0。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GamePosition centerPos = new(centerX, centerY, centerZ);
            List<GamePosition> positions = PointShape_ComboBox.SelectedItem?.ToString() switch
            {
                "方形八方" => MarkerShapePosCalculator.Square(centerPos, distance),
                _ => MarkerShapePosCalculator.Circle(centerPos, distance)
            };

            string order = PointOrder_ComboBox.SelectedItem?.ToString() ?? "A1B2C3D4";
            string[] pointOrder = order switch
            {
                "A2B3C4D1" => ["A", "2", "B", "3", "C", "4", "D", "1"],
                _ => ["A", "1", "B", "2", "C", "3", "D", "4"]
            };

            for (int i = 0; i < pointOrder.Length; i++)
            {
                SetPointPosition(currentWayMark, pointOrder[i], positions[i]);
            }

            currentWayMark.AEnabled = true;
            currentWayMark.BEnabled = true;
            currentWayMark.CEnabled = true;
            currentWayMark.DEnabled = true;
            currentWayMark.OneEnabled = true;
            currentWayMark.TwoEnabled = true;
            currentWayMark.ThreeEnabled = true;
            currentWayMark.FourEnabled = true;
            currentWayMark.timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            UpdatePreview();
        }

        private static bool TryReadDouble(TextBox textBox, string displayName, out double value)
        {
            string text = textBox.Text.Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            MessageBox.Show($"{displayName} 需要填写数字。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private static void SetPointPosition(WayMark wayMark, string pointName, GamePosition position)
        {
            WayMarkPoint point = pointName switch
            {
                "A" => wayMark.A,
                "B" => wayMark.B,
                "C" => wayMark.C,
                "D" => wayMark.D,
                "1" => wayMark.One,
                "2" => wayMark.Two,
                "3" => wayMark.Three,
                "4" => wayMark.Four,
                _ => throw new ArgumentOutOfRangeException(nameof(pointName), pointName, "未知标点名称")
            };

            point.X = ToRawCoordinate(position.X);
            point.Y = ToRawCoordinate(position.Y);
            point.Z = ToRawCoordinate(position.Z);
        }

        private static int ToRawCoordinate(double value)
        {
            return (int)Math.Round(value * 1000, MidpointRounding.AwayFromZero);
        }
    }
}
