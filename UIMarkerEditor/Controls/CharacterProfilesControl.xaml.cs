using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UIMarkerEditor.Controls;

public partial class CharacterProfilesControl : UserControl
{
    private readonly ObservableCollection<CharacterProfile> characterEntries = [];
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private Action refreshBackupList = () => { };
    private CharacterProfile? loadedCharacterProfile;
    private string selectedCharacterDataCenter = string.Empty;
    private string selectedCharacterWorld = string.Empty;
    private bool isCharacterDetailDirty;
    private bool suppressCharacterSelectionChanged;
    private bool suppressCharacterChangeTracking;

    public CharacterProfilesControl()
    {
        InitializeComponent();
        Character_DataGrid.ItemsSource = characterEntries;
        UpdateCharacterDetailVisibility(showDetail: false);
    }

    public void Initialize(AppDataStore appDataStore, Window ownerWindow, Action refreshBackupList)
    {
        this.appDataStore = appDataStore;
        this.ownerWindow = ownerWindow;
        this.refreshBackupList = refreshBackupList;
        RefreshServerPicker();
    }

    public void RefreshCharacterList()
    {
        string? selectedUserID = (Character_DataGrid.SelectedItem as CharacterProfile)?.UserID;
        ReloadCharacterList(selectedUserID);
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

    public async Task SyncServerListIfNeededAsync()
    {
        if (appDataStore == null) return;

        DateTime lastServerSyncCheck = appDataStore.ServerList.LastUpdated > appDataStore.ServerList.LastSuccessfulSyncAt
            ? appDataStore.ServerList.LastUpdated
            : appDataStore.ServerList.LastSuccessfulSyncAt;
        if (DateTime.Now - lastServerSyncCheck < TimeSpan.FromDays(7)) return;

        if (await appDataStore.TrySyncServerListAsync())
        {
            RefreshServerPicker();
        }
    }

    public bool ConfirmSaveOrDiscardCharacterChanges()
    {
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
            isCharacterDetailDirty = false;
            return true;
        }

        if (!TrySaveCharacterProfile(showSuccessMessage: false, out string _)) return false;

        Character_DataGrid.Items.Refresh();
        refreshBackupList();
        return true;
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
        UpdateCharacterActionStates();
    }

    private void NewCharacter_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null || !ConfirmSaveOrDiscardCharacterChanges()) return;

        BackupCharacterProfileDialog dialog = new(appDataStore.ServerList.Groups);
        DialogOwnerHelper.ConfigureOwnedDialog(dialog, ownerWindow ?? Window.GetWindow(this));
        if (dialog.ShowDialog() != true) return;

        if (!TryCreateCharacterProfile(dialog, out string createdUserID)) return;

        ReloadCharacterList(createdUserID);
        refreshBackupList();
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
        AppMessageBox.Show(ownerWindow, "角色备注已创建。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
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
        UpdateCharacterActionStates();
    }

    private void UpdateCharacterActionStates()
    {
        bool hasSelection = Character_DataGrid.SelectedItem is CharacterProfile;
        bool hasLoadedProfile = loadedCharacterProfile != null;
        DeleteCharacter_Button.IsEnabled = CharacterDetail_ScrollViewer.Visibility == Visibility.Visible && hasLoadedProfile;
        DeleteCharacterList_Button.IsEnabled = hasSelection;
        DeleteCharacter_MenuItem.IsEnabled = hasSelection;
    }

    private void SaveCharacter_Button_Click(object sender, RoutedEventArgs e)
    {
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
        if (appDataStore == null) return false;

        if (loadedCharacterProfile is not CharacterProfile profile)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个要修改的角色备注。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string userID = profile.UserID.Trim().ToUpperInvariant();
        savedUserID = userID;
        if (!IsValidUserID(userID))
        {
            AppMessageBox.Show(ownerWindow, "当前角色备注的 User ID 无效。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (showSuccessMessage)
        {
            AppMessageBox.Show(ownerWindow, "角色备注已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        return true;
    }

    private void ReloadCharacterList(string? preferredUserID)
    {
        if (appDataStore == null) return;

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
        loadedCharacterProfile = profile;
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

    private void DeleteCharacter_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null || Character_DataGrid.SelectedItem is not CharacterProfile profile) return;
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
