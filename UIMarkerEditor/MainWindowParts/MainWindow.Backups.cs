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

    }
}