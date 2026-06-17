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
    private string selectedCharacterDataCenter = string.Empty;
    private string selectedCharacterWorld = string.Empty;
    private bool isCreatingCharacterProfile;
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
        if (appDataStore == null) return;

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

        MessageBoxResult result = MessageBox.Show(
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
        if (selectedServer == null)
        {
            return;
        }

        selectedCharacterDataCenter = selectedServer.Value.DataCenter;
        selectedCharacterWorld = selectedServer.Value.World;
        MarkCharacterDetailDirty();
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

        string userID = CharacterUserID_TextBox.Text.Trim().ToUpperInvariant();
        savedUserID = userID;
        if (string.IsNullOrWhiteSpace(userID))
        {
            MessageBox.Show(ownerWindow, "User ID 不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!IsValidUserID(userID))
        {
            MessageBox.Show(ownerWindow, "User ID 必须是 16 位十六进制字符。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        (string DataCenter, string World)? selectedServer = GetSelectedServer();
        if (selectedServer == null)
        {
            MessageBox.Show(ownerWindow, "请选择角色所在服务器。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        bool isNewProfile = !appDataStore.Characters.Any(character =>
            string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
        CharacterProfile profile = appDataStore.GetOrCreateCharacter(userID);
        string previousCharacterName = profile.CharacterName;
        string previousDataCenter = profile.DataCenter;
        string previousWorld = profile.World;
        string previousNote = profile.Note;
        DateTime previousUpdatedAt = profile.UpdatedAt;

        profile.CharacterName = CharacterName_TextBox.Text.Trim();
        profile.DataCenter = selectedServer.Value.DataCenter;
        profile.World = selectedServer.Value.World;
        profile.Note = CharacterNote_TextBox.Text.Trim();
        profile.UpdatedAt = DateTime.Now;
        try
        {
            appDataStore.SaveCharacters();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            if (isNewProfile)
            {
                appDataStore.Characters.Remove(profile);
            }
            else
            {
                profile.CharacterName = previousCharacterName;
                profile.DataCenter = previousDataCenter;
                profile.World = previousWorld;
                profile.Note = previousNote;
                profile.UpdatedAt = previousUpdatedAt;
            }

            MessageBox.Show(ownerWindow, $"保存角色备注失败：{ex.Message}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!characterEntries.Any(character => string.Equals(character.UserID, profile.UserID, StringComparison.OrdinalIgnoreCase)))
        {
            characterEntries.Add(profile);
        }

        isCreatingCharacterProfile = false;
        isCharacterDetailDirty = false;
        CharacterUserID_TextBox.IsReadOnly = true;

        if (showSuccessMessage)
        {
            MessageBox.Show(ownerWindow, "角色备注已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
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
        if (appDataStore == null || Character_DataGrid.SelectedItem is not CharacterProfile profile) return;
        if (MessageBox.Show(ownerWindow, "确定要删除这个角色备注吗？备份文件不会被删除。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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

            MessageBox.Show(ownerWindow, $"删除角色备注失败：{ex.Message}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        isCreatingCharacterProfile = false;
        SetCharacterSelection(null);
        ClearCharacterDetailFields();
        UpdateCharacterDetailVisibility(showDetail: false);
        RefreshCharacterList();
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
