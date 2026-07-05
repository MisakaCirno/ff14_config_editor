using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void File_MenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
            RefreshLocalCharacterSelectionAvailability();
            RefreshRecentFileMenu();
        }

        private void RecentFile_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: string filePath }) return;

            OpenRecentWayMarkFile(filePath);
        }

        private void OpenRecentWayMarkFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                AppMessageBox.Show(this, "这个最近文件已经不存在。", "最近打开", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshRecentFileMenu();
                return;
            }

            if (!ConfirmSaveOrDiscardWayMarkChanges())
            {
                return;
            }

            LoadConfigFileWithOverlay(filePath);
        }

        private void ClearRecentFiles_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            appDataStore.ClearRecentFiles();
            RefreshRecentFileMenu();
        }

        private void RefreshRecentFileMenu()
        {
            RecentFiles_MenuItem.Items.Clear();
            Style subMenuItemStyle = (Style)FindResource("TitleBarSubMenuItemStyle");
            List<string> recentFiles = appDataStore.GetRecentFiles();
            RefreshRecentFileOverlay(recentFiles);

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

        private void RefreshRecentFileOverlay(IReadOnlyList<string> recentFiles)
        {
            List<RecentWayMarkFileItem> overlayItems = [];
            foreach (string filePath in recentFiles)
            {
                bool exists = File.Exists(filePath);
                string userID = AppDataStore.GetUserIDFromCharacterFolder(filePath) ?? "未知 User ID";
                CharacterProfile? profile = appDataStore.Characters.FirstOrDefault(character =>
                    string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));

                overlayItems.Add(new RecentWayMarkFileItem(
                    filePath,
                    userID,
                    profile?.Note.Trim() ?? string.Empty,
                    exists ? filePath : $"{filePath}\n文件不存在",
                    exists));
            }

            WayMarkEditor_Control.SetRecentFiles(overlayItems);
        }
    }
}
