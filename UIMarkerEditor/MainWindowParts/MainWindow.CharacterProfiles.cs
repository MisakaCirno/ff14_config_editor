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
    public partial class MainWindow
    {
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

    }
}