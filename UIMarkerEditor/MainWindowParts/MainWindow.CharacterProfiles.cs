using System.IO;
using System.Windows.Controls;
using FF14ConfigEditor;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void RefreshCharacterList()
        {
            CharacterProfiles_Control.RefreshCharacterList();
            RefreshLocalCharacterSelectionAvailability();
            RefreshRecentFileMenu();
        }

        private void RefreshCharacterListFromExternalChange()
        {
            CharacterProfiles_Control.TryRefreshCharacterListFromExternalChange();
            RefreshLocalCharacterSelectionAvailability();
            RefreshRecentFileMenu();
        }

        private async Task<ServerListLoadResult?> SyncServerListIfNeededAsync(bool showFailureMessage = false)
        {
            ServerListLoadResult? result = await CharacterProfiles_Control.SyncServerListIfNeededAsync(showFailureMessage);
            ToolSettings_Control.RefreshOnlineDataStatus();
            return result;
        }

        private async void MainTab_Control_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != MainTab_Control || isRestoringMainTabSelection)
            {
                return;
            }

            TabItem? previousTab = e.RemovedItems.OfType<TabItem>().FirstOrDefault();
            if (previousTab != null && !ConfirmLeaveMainTab(previousTab))
            {
                isRestoringMainTabSelection = true;
                try
                {
                    MainTab_Control.SelectedItem = previousTab;
                }
                finally
                {
                    isRestoringMainTabSelection = false;
                }

                return;
            }

            if (CharacterProfiles_TabItem.IsSelected)
            {
                await CharacterProfiles_Control.RefreshCharacterActivityAsync();
                await SyncServerListIfNeededAsync();
            }
        }

        private bool ConfirmSaveOrDiscardCharacterChanges()
        {
            return CharacterProfiles_Control.ConfirmSaveOrDiscardCharacterChanges();
        }

        private bool ConfirmLeaveMainTab(TabItem previousTab)
        {
            if (ReferenceEquals(previousTab, CharacterProfiles_TabItem))
            {
                return ConfirmSaveOrDiscardCharacterChanges();
            }

            if (ReferenceEquals(previousTab, WayMarkFavorites_TabItem))
            {
                return WayMarkFavorites_Control.ConfirmSaveOrDiscardChanges();
            }

            if (ReferenceEquals(previousTab, ToolSettings_TabItem))
            {
                return ToolSettings_Control.CommitPendingSettingsEdits();
            }

            return true;
        }

        private bool PrepareDataDirectoryMigration(string currentDataDirectory, string targetDataDirectory)
        {
            if (!ToolSettings_Control.CommitPendingSettingsEdits(scanLocalCharactersAfterGameInstallDirectorySave: false))
            {
                return false;
            }

            if (IsCurrentWayMarkFileUnderDataDirectory(currentDataDirectory) &&
                !ConfirmSaveOrDiscardWayMarkChanges())
            {
                return false;
            }

            if (!WayMarkFavorites_Control.ConfirmSaveOrDiscardChanges())
            {
                return false;
            }

            if (!ConfirmSaveOrDiscardCharacterChanges())
            {
                return false;
            }

            PauseCurrentFileMonitorForDataDirectoryMigration(currentDataDirectory);
            return true;
        }

        private async Task FinishDataDirectoryMigration(
            string currentDataDirectory,
            string targetDataDirectory,
            DataDirectoryMigrationResult? result)
        {
            string? pausedFilePath = dataDirectoryMigrationPausedCurrentFilePath;
            dataDirectoryMigrationPausedCurrentFilePath = null;

            if (result != null && !string.IsNullOrWhiteSpace(pausedFilePath))
            {
                if (await TryReloadRelocatedCurrentFileAsync(pausedFilePath, currentDataDirectory, targetDataDirectory))
                {
                    WayMarkFavorites_Control.RefreshFavorites();
                    StartLocalCharacterScan();
                    return;
                }

                if (HasLoadedWayMarkFile())
                {
                    CloseCurrentWayMarkFile();
                    AppMessageBox.Show(
                        this,
                        "工具数据目录迁移已完成，但当前标点文件无法从新位置重新加载。为避免继续显示失效的文件状态，已关闭当前文件。",
                        "当前文件已关闭",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }

                WayMarkFavorites_Control.RefreshFavorites();
                StartLocalCharacterScan();
                return;
            }

            if (result != null)
            {
                WayMarkFavorites_Control.RefreshFavorites();
                StartLocalCharacterScan();
            }

            if (HasLoadedWayMarkFile())
            {
                StartCurrentFileChangeMonitor(currentFilePath);
            }
        }

        private bool IsCurrentWayMarkFileUnderDataDirectory(string dataDirectory)
        {
            return HasLoadedWayMarkFile() &&
                DataDirectoryPathRelocator.TryRelocatePath(
                    currentFilePath,
                    dataDirectory,
                    dataDirectory,
                    out _);
        }

        private void PauseCurrentFileMonitorForDataDirectoryMigration(string dataDirectory)
        {
            if (!IsCurrentWayMarkFileUnderDataDirectory(dataDirectory))
            {
                return;
            }

            dataDirectoryMigrationPausedCurrentFilePath = currentFilePath;
            StopCurrentFileChangeMonitor();
        }

        private async Task<bool> TryReloadRelocatedCurrentFileAsync(
            string? sourceFilePath,
            string currentDataDirectory,
            string targetDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) ||
                !DataDirectoryPathRelocator.TryRelocatePath(
                    sourceFilePath,
                    currentDataDirectory,
                    targetDataDirectory,
                    out string relocatedFilePath))
            {
                return false;
            }

            if (!File.Exists(relocatedFilePath))
            {
                AppLogger.Warning(
                    AppLogCategory.IO,
                    $"数据目录迁移后未找到当前 UISAVE.DAT 的新位置：{relocatedFilePath}");
                return false;
            }

            return await TryLoadConfigFileWithOverlayAsync(relocatedFilePath);
        }
    }
}
