using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FF14ConfigEditor;

namespace UIMarkerEditor.Controls;

public partial class CharacterProfilesControl : UserControl
{
    private readonly ObservableCollection<CharacterProfile> characterEntries = [];
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private Action refreshBackupList = () => { };
    private Action refreshLocalCharacterSelectionAvailability = () => { };
    private CharacterProfile? loadedCharacterProfile;
    private string selectedCharacterDataCenter = string.Empty;
    private string selectedCharacterWorld = string.Empty;
    private bool isCharacterDetailDirty;
    private bool isCharacterOperationBusy;
    private bool hasPendingExternalCharacterRefresh;
    private bool isHandlingCharacterSelectionChange;
    private bool suppressCharacterSelectionChanged;
    private bool suppressCharacterChangeTracking;
    private Task<ServerListLoadResult?>? serverListSyncTask;

    public CharacterProfilesControl()
    {
        InitializeComponent();
        Character_DataGrid.ItemsSource = characterEntries;
        UpdateCharacterDetailVisibility(showDetail: false);
    }

    public void Initialize(
        AppDataStore appDataStore,
        Window ownerWindow,
        Action refreshBackupList,
        Action refreshLocalCharacterSelectionAvailability)
    {
        this.appDataStore = appDataStore;
        this.ownerWindow = ownerWindow;
        this.refreshBackupList = refreshBackupList;
        this.refreshLocalCharacterSelectionAvailability = refreshLocalCharacterSelectionAvailability;
        RefreshServerPicker();
    }

    public void RefreshCharacterList()
    {
        if (isCharacterOperationBusy) return;

        if (!ConfirmSaveOrDiscardCharacterChanges())
        {
            return;
        }

        string? selectedUserID = (Character_DataGrid.SelectedItem as CharacterProfile)?.UserID;
        ReloadCharacterList(selectedUserID);
    }

    public bool TryRefreshCharacterListFromExternalChange()
    {
        if (isCharacterOperationBusy || isCharacterDetailDirty)
        {
            hasPendingExternalCharacterRefresh = true;
            return false;
        }

        ReloadCharacterList(GetPreferredRefreshUserID());
        return true;
    }

    public void ApplyLayoutSettings(WindowLayoutSettings layout)
    {
        double listRatio = ClampRatio(layout.CharacterListRatio);
        CharacterList_Column.Width = new GridLength(listRatio, GridUnitType.Star);
        CharacterDetail_Column.Width = new GridLength(1 - listRatio, GridUnitType.Star);
    }

    public void CaptureLayoutSettings(WindowLayoutSettings layout)
    {
        double totalWidth = CharacterList_Column.ActualWidth + CharacterDetail_Column.ActualWidth;
        if (totalWidth <= 1) return;

        layout.CharacterListRatio = CharacterList_Column.ActualWidth / totalWidth;
    }

    private static double ClampRatio(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0.15, 0.85) : 0.4;
    }

    public async Task<ServerListLoadResult?> SyncServerListIfNeededAsync(bool showFailureMessage)
    {
        if (appDataStore == null) return null;

        Task<ServerListLoadResult?> syncTask = serverListSyncTask ??= SyncServerListIfNeededCoreAsync();
        ServerListLoadResult? result;
        try
        {
            result = await syncTask;
        }
        finally
        {
            if (ReferenceEquals(serverListSyncTask, syncTask))
            {
                serverListSyncTask = null;
            }
        }

        if (showFailureMessage && result?.Success == false && appDataStore.ServerList.Groups.Count == 0)
        {
            ShowServerListUnavailableMessage();
        }

        return result;
    }

    private async Task<ServerListLoadResult?> SyncServerListIfNeededCoreAsync()
    {
        if (appDataStore == null) return null;

        ServerListLoadResult result = await appDataStore.EnsureServerListAvailableAsync();
        RefreshServerPicker();
        return result;
    }

    public bool ConfirmSaveOrDiscardCharacterChanges()
    {
        if (isCharacterOperationBusy)
        {
            AppMessageBox.Show(
                ownerWindow,
                "角色备注正在处理中，请稍候完成后再继续操作。",
                "角色备注处理中",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (appDataStore == null || !isCharacterDetailDirty) return true;

        MessageBoxResult result = AppMessageBox.Show(
            ownerWindow,
            "当前角色备注有未保存的修改。\n\n选择“是”保存修改，选择“否”放弃修改，选择“取消”继续编辑。",
            "未保存的角色备注",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.No)
        {
            DiscardCharacterDetailChanges();
            TryApplyPendingExternalCharacterRefresh();
            return true;
        }

        if (!TrySaveCharacterProfile(showSuccessMessage: false, out string savedUserID)) return false;

        RefreshCharacterListAfterCharacterChange(savedUserID);
        refreshBackupList();
        return true;
    }

    public bool TryPrepareCloseChanges(out bool shouldSave)
    {
        shouldSave = false;
        if (isCharacterOperationBusy)
        {
            AppMessageBox.Show(
                ownerWindow,
                "角色备注正在处理中，请稍候完成后再关闭工具。",
                "角色备注处理中",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (appDataStore == null || !isCharacterDetailDirty) return true;

        MessageBoxResult result = AppMessageBox.Show(
            ownerWindow,
            "当前角色备注有未保存的修改。\n\n选择“是”在关闭前保存，选择“否”继续关闭并放弃这些修改，选择“取消”返回编辑。\n\n如果后续关闭被取消，当前修改会保留。",
            "未保存的角色备注",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        shouldSave = result == MessageBoxResult.Yes;
        return true;
    }

    public bool SavePreparedCloseChanges()
    {
        if (!TrySaveCharacterProfile(showSuccessMessage: false, out string savedUserID)) return false;

        RefreshCharacterListAfterCharacterChange(savedUserID);
        refreshBackupList();
        return true;
    }

    private void DiscardCharacterDetailChanges()
    {
        if (loadedCharacterProfile == null)
        {
            ClearCharacterDetailFields();
            UpdateCharacterDetailVisibility(showDetail: false);
            return;
        }

        CharacterProfile? currentProfile = FindCharacterProfile(loadedCharacterProfile.UserID);
        if (currentProfile == null)
        {
            ClearCharacterDetailFields();
            UpdateCharacterDetailVisibility(showDetail: false);
            return;
        }

        LoadCharacterProfileIntoDetail(currentProfile);
    }

    public void RefreshServerPicker()
    {
        if (appDataStore == null) return;

        suppressCharacterChangeTracking = true;
        CharacterServerPicker_Control.SetServerGroups(appDataStore.ServerList.Groups);
        SelectServer(selectedCharacterDataCenter, selectedCharacterWorld);
        suppressCharacterChangeTracking = false;
    }

    private (string DataCenter, string World)? GetSelectedServer()
    {
        return CharacterServerPicker_Control.GetSelectedServer();
    }

    private void SelectServer(string dataCenter, string world)
    {
        selectedCharacterDataCenter = dataCenter;
        selectedCharacterWorld = world;
        CharacterServerPicker_Control.SelectServer(dataCenter, world);
    }

    private void CharacterServerPicker_Control_SelectedServerChanged(object sender, EventArgs e)
    {
        (string DataCenter, string World)? selectedServer = CharacterServerPicker_Control.GetSelectedServer();
        selectedCharacterDataCenter = selectedServer?.DataCenter ?? string.Empty;
        selectedCharacterWorld = selectedServer?.World ?? string.Empty;
        MarkCharacterDetailDirty();
    }

    private void RefreshCharacters_Button_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSaveOrDiscardCharacterChanges()) return;

        ClearCharacterDetailFields();
        ReloadCharacterList(null);
    }

    private async void Character_DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCharacterSelectionChanged) return;

        isHandlingCharacterSelectionChange = true;
        try
        {
            CharacterProfile? selectedProfile = Character_DataGrid.SelectedItem as CharacterProfile;
            CharacterProfile? previousProfile = e.RemovedItems.OfType<CharacterProfile>().FirstOrDefault();
            if (isCharacterDetailDirty && !ConfirmSaveOrDiscardCharacterChanges())
            {
                RestoreCharacterSelectionWithoutReload(previousProfile);
                return;
            }

            LoadCharacterProfileIntoDetail(selectedProfile);
            if (selectedProfile != null)
            {
                await SyncServerListIfNeededAsync(showFailureMessage: false);
            }
        }
        finally
        {
            isHandlingCharacterSelectionChange = false;
        }

        TryApplyPendingExternalCharacterRefresh(GetPreferredRefreshUserID());
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
        UpdateCharacterActionStates();
    }

    private async void NewCharacter_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null || isCharacterOperationBusy || !ConfirmSaveOrDiscardCharacterChanges()) return;

        ServerListLoadResult? serverListResult = null;
        ShowCharacterBusyOverlay("正在同步服务器列表...", "正在准备角色备注窗口，请稍候。");
        try
        {
            serverListResult = await SyncServerListIfNeededAsync(showFailureMessage: false);
        }
        finally
        {
            HideCharacterBusyOverlay();
        }

        if (serverListResult?.Success == false && appDataStore.ServerList.Groups.Count == 0)
        {
            ShowServerListUnavailableMessage();
        }

        if (!ConfirmSaveOrDiscardCharacterChanges()) return;

        BackupCharacterProfileDialog dialog = new(appDataStore.ServerList.Groups);
        DialogOwnerHelper.ConfigureOwnedDialog(dialog, ownerWindow ?? Window.GetWindow(this));
        if (dialog.ShowDialog() != true) return;

        if (!TryCreateCharacterProfile(dialog, out string createdUserID)) return;

        ReloadCharacterList(createdUserID);
        refreshBackupList();
    }

    private async void ScanClientLogs_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null || isCharacterOperationBusy || !ConfirmSaveOrDiscardCharacterChanges()) return;

        if (!WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            appDataStore.Settings.GameInstallDirectory,
            out _))
        {
            AppMessageBox.Show(
                ownerWindow,
                "无法定位游戏角色目录。请确认游戏安装目录正确；若是首次使用，建议先启动一次游戏后再尝试使用本工具。",
                "获取所有本地角色",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        LocalGameCharacterScanPreparation? preparation = null;
        Exception? scanFailure = null;
        ShowCharacterBusyOverlay("正在获取本地角色...", "正在读取角色目录和客户端日志，请稍候。");
        try
        {
            preparation = await Task.Run(appDataStore.PrepareLocalGameCharacterScan);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            scanFailure = ex;
        }
        finally
        {
            HideCharacterBusyOverlay();
        }

        if (scanFailure != null || preparation == null)
        {
            AppMessageBox.Show(ownerWindow, $"获取所有本地角色失败：{scanFailure?.Message ?? "未知错误"}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ConfirmSaveOrDiscardCharacterChanges()) return;

        LocalGameCharacterScanResult? result = null;
        Exception? applyFailure = null;
        ShowCharacterBusyOverlay("正在更新角色备注...", "正在保存本地角色备注，请稍候。");
        try
        {
            result = appDataStore.ApplyLocalGameCharacterScan(preparation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            applyFailure = ex;
        }
        finally
        {
            HideCharacterBusyOverlay();
        }

        if (applyFailure != null || result == null)
        {
            AppMessageBox.Show(ownerWindow, $"获取所有本地角色失败：{applyFailure?.Message ?? "未知错误"}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (ClientLogCharacterNameScanError error in result.Errors)
        {
            AppLogger.Warning(AppLogCategory.IO, $"客户端日志昵称扫描跳过：{error.Path}；{error.Message}");
        }

        if (result.SkippedBecauseGameInstallDirectoryChanged)
        {
            refreshLocalCharacterSelectionAvailability();
            AppMessageBox.Show(
                ownerWindow,
                "游戏安装目录已变化，本次本地角色扫描结果已跳过。请重新获取所有本地角色。",
                "获取所有本地角色",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (result.SkippedBecauseCharactersChanged)
        {
            refreshLocalCharacterSelectionAvailability();
            AppMessageBox.Show(
                ownerWindow,
                "角色备注已在扫描期间变化，本次本地角色扫描结果已跳过。请重新获取所有本地角色。",
                "获取所有本地角色",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ReloadCharacterList(null);
        refreshBackupList();
        refreshLocalCharacterSelectionAvailability();
        if (result.LocalCharacterCount == 0)
        {
            AppMessageBox.Show(
                ownerWindow,
                "没有找到包含 UISAVE.DAT 的本地角色目录。建议先启动一次游戏并进入角色后再尝试使用本工具。",
                "获取所有本地角色",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string message = $"已找到 {result.LocalCharacterCount} 个本地角色。";
        if (result.CreatedProfileCount > 0)
        {
            message += $" 新增 {result.CreatedProfileCount} 条角色备注。";
        }
        if (result.ImportedCharacterNameCount > 0)
        {
            message += $" 补全 {result.ImportedCharacterNameCount} 个角色名。";
        }
        if (!result.Changed)
        {
            message += " 当前角色备注无需更新。";
        }
        if (result.Errors.Count > 0)
        {
            message += $" 另有 {result.Errors.Count} 个日志文件或目录读取失败，已跳过。";
        }

        ToastService.ShowSuccess(message);
    }

    private bool TryCreateCharacterProfile(BackupCharacterProfileDialog dialog, out string createdUserID)
    {
        createdUserID = string.Empty;
        if (appDataStore == null) return false;

        string userID = dialog.UserID;
        createdUserID = userID;
        if (!IsValidUserID(userID))
        {
            AppMessageBox.Show(ownerWindow, "User ID 必须是 16 位十六进制字符。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (appDataStore.Characters.Any(character => string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase)))
        {
            AppMessageBox.Show(ownerWindow, "这个 User ID 已经有角色备注。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (string.IsNullOrWhiteSpace(dialog.CharacterName))
        {
            AppMessageBox.Show(ownerWindow, "角色名不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        CharacterProfile profile = appDataStore.GetOrCreateCharacter(userID);
        profile.CharacterName = dialog.CharacterName;
        profile.DataCenter = dialog.DataCenter;
        profile.World = dialog.World;
        profile.Note = dialog.Note;
        profile.UpdatedAt = DateTime.Now;

        try
        {
            appDataStore.SaveCharacters();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            appDataStore.Characters.Remove(profile);
            AppMessageBox.Show(ownerWindow, $"保存角色备注失败：{ex.Message}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        isCharacterDetailDirty = false;
        ToastService.ShowSuccess("角色备注已创建。");
        return true;
    }

    private void ShowServerListUnavailableMessage()
    {
        AppMessageBox.Show(
            ownerWindow ?? Window.GetWindow(this),
            "服务器列表同步失败，当前没有可用的服务器列表。你仍然可以编辑角色名和备注，稍后可在工具设置中手动检查服务器列表。",
            "服务器列表不可用",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ClearCharacterDetailFields()
    {
        suppressCharacterChangeTracking = true;
        loadedCharacterProfile = null;
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
        UpdateCharacterActionStates();
    }

    private void UpdateCharacterActionStates()
    {
        bool canUseActions = !isCharacterOperationBusy;
        bool hasSelection = canUseActions && Character_DataGrid.SelectedItem is CharacterProfile;
        bool hasLoadedProfile = canUseActions && loadedCharacterProfile != null;
        DeleteCharacter_Button.IsEnabled = CharacterDetail_ScrollViewer.Visibility == Visibility.Visible && hasLoadedProfile;
        DeleteCharacterList_Button.IsEnabled = hasSelection;
        DeleteCharacter_MenuItem.IsEnabled = hasSelection;
    }

    private void SaveCharacter_Button_Click(object sender, RoutedEventArgs e)
    {
        if (isCharacterOperationBusy) return;

        if (!TrySaveCharacterProfile(showSuccessMessage: true, out string savedUserID)) return;

        ReloadCharacterList(savedUserID);
        refreshBackupList();
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

    private bool TrySaveCharacterProfile(bool showSuccessMessage, out string savedUserID)
    {
        savedUserID = string.Empty;
        if (appDataStore == null || isCharacterOperationBusy) return false;

        if (loadedCharacterProfile is not CharacterProfile draftProfile)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个要修改的角色备注。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string userID = draftProfile.UserID.Trim().ToUpperInvariant();
        savedUserID = userID;
        if (!IsValidUserID(userID))
        {
            AppMessageBox.Show(ownerWindow, "当前角色备注的 User ID 无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        CharacterProfile? profile = FindCharacterProfile(userID);
        if (profile == null)
        {
            AppMessageBox.Show(ownerWindow, "当前角色备注已经不在列表中。请刷新角色备注后再继续编辑。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        string characterName = CharacterName_TextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(characterName))
        {
            AppMessageBox.Show(ownerWindow, "角色名不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        (string DataCenter, string World)? selectedServer = GetSelectedServer();

        string previousCharacterName = profile.CharacterName;
        string previousDataCenter = profile.DataCenter;
        string previousWorld = profile.World;
        string previousNote = profile.Note;
        DateTime previousUpdatedAt = profile.UpdatedAt;

        profile.CharacterName = characterName;
        profile.DataCenter = selectedServer?.DataCenter ?? string.Empty;
        profile.World = selectedServer?.World ?? string.Empty;
        profile.Note = CharacterNote_TextBox.Text.Trim();
        profile.UpdatedAt = DateTime.Now;
        try
        {
            appDataStore.SaveCharacters();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            profile.CharacterName = previousCharacterName;
            profile.DataCenter = previousDataCenter;
            profile.World = previousWorld;
            profile.Note = previousNote;
            profile.UpdatedAt = previousUpdatedAt;

            AppMessageBox.Show(ownerWindow, $"保存角色备注失败：{ex.Message}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        isCharacterDetailDirty = false;
        loadedCharacterProfile = CloneCharacterProfile(profile);

        if (showSuccessMessage)
        {
            ToastService.ShowSuccess("角色备注已保存。");
        }

        return true;
    }

    private void ReloadCharacterList(string? preferredUserID)
    {
        if (appDataStore == null) return;

        hasPendingExternalCharacterRefresh = false;
        suppressCharacterSelectionChanged = true;
        try
        {
            characterEntries.Clear();
            foreach (CharacterProfile profile in appDataStore.Characters.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                characterEntries.Add(profile);
            }

            Character_DataGrid.SelectedItem = string.IsNullOrWhiteSpace(preferredUserID)
                ? null
                : characterEntries.FirstOrDefault(character =>
                    string.Equals(character.UserID, preferredUserID, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            suppressCharacterSelectionChanged = false;
        }

        LoadCharacterProfileIntoDetail(Character_DataGrid.SelectedItem as CharacterProfile);
    }

    private void RestoreCharacterSelectionWithoutReload(CharacterProfile? profile)
    {
        suppressCharacterSelectionChanged = true;
        try
        {
            Character_DataGrid.SelectedItem = profile;
        }
        finally
        {
            suppressCharacterSelectionChanged = false;
        }
    }

    private void LoadCharacterProfileIntoDetail(CharacterProfile? profile)
    {
        suppressCharacterChangeTracking = true;
        loadedCharacterProfile = profile == null ? null : CloneCharacterProfile(profile);
        if (profile == null)
        {
            ClearCharacterDetailFields();
            UpdateCharacterDetailVisibility(showDetail: false);
            suppressCharacterChangeTracking = false;
            isCharacterDetailDirty = false;
            return;
        }

        CharacterUserID_TextBox.Text = profile.UserID;
        CharacterName_TextBox.Text = profile.CharacterName;
        SelectServer(profile.DataCenter, profile.World);
        CharacterNote_TextBox.Text = profile.Note;
        UpdateCharacterDetailVisibility(showDetail: true);
        suppressCharacterChangeTracking = false;
        isCharacterDetailDirty = false;
    }

    private void RefreshCharacterListAfterCharacterChange(string? preferredUserID)
    {
        if (TryApplyPendingExternalCharacterRefresh(preferredUserID))
        {
            return;
        }

        Character_DataGrid.Items.Refresh();
    }

    private bool TryApplyPendingExternalCharacterRefresh(string? preferredUserID = null)
    {
        if (!hasPendingExternalCharacterRefresh ||
            isCharacterOperationBusy ||
            isCharacterDetailDirty ||
            isHandlingCharacterSelectionChange)
        {
            return false;
        }

        ReloadCharacterList(preferredUserID ?? GetPreferredRefreshUserID());
        return true;
    }

    private string? GetPreferredRefreshUserID()
    {
        return (Character_DataGrid.SelectedItem as CharacterProfile)?.UserID ??
            loadedCharacterProfile?.UserID;
    }

    private CharacterProfile? FindCharacterProfile(string userID)
    {
        return appDataStore?.Characters.FirstOrDefault(character =>
            string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
    }

    private static CharacterProfile CloneCharacterProfile(CharacterProfile profile)
    {
        return new CharacterProfile
        {
            UserID = profile.UserID,
            CharacterName = profile.CharacterName,
            DataCenter = profile.DataCenter,
            World = profile.World,
            Note = profile.Note,
            UpdatedAt = profile.UpdatedAt
        };
    }

    private void DeleteCharacter_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null || isCharacterOperationBusy || Character_DataGrid.SelectedItem is not CharacterProfile profile) return;
        if (AppMessageBox.Show(ownerWindow, "确定要删除这个角色备注吗？备份文件不会被删除。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        int removedIndex = appDataStore.Characters.IndexOf(profile);
        appDataStore.Characters.Remove(profile);
        try
        {
            appDataStore.SaveCharacters();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            if (removedIndex >= 0)
            {
                appDataStore.Characters.Insert(removedIndex, profile);
            }

            AppMessageBox.Show(ownerWindow, $"删除角色备注失败：{ex.Message}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ClearCharacterDetailFields();
        ReloadCharacterList(null);
        refreshBackupList();
    }

    private void ShowCharacterBusyOverlay(string title, string message)
    {
        isCharacterOperationBusy = true;
        CharacterList_GroupBox.IsEnabled = false;
        CharacterGridSplitter.IsEnabled = false;
        CharacterDetail_GroupBox.IsEnabled = false;
        if (Character_DataGrid.ContextMenu != null)
        {
            Character_DataGrid.ContextMenu.IsOpen = false;
        }

        CharacterBusyOverlay_Control.Show(title, message);
        UpdateCharacterActionStates();
    }

    private void HideCharacterBusyOverlay()
    {
        CharacterBusyOverlay_Control.Hide();
        CharacterList_GroupBox.IsEnabled = true;
        CharacterGridSplitter.IsEnabled = true;
        CharacterDetail_GroupBox.IsEnabled = true;
        isCharacterOperationBusy = false;
        UpdateCharacterActionStates();
    }

    private static bool IsValidUserID(string userID)
    {
        return userID.Length == 16 && userID.All(Uri.IsHexDigit);
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
}
