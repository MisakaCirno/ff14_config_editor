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
        }

        private void RefreshCharacterListFromExternalChange()
        {
            CharacterProfiles_Control.TryRefreshCharacterListFromExternalChange();
            RefreshLocalCharacterSelectionAvailability();
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

        private void FinishDataDirectoryMigration(
            string currentDataDirectory,
            string targetDataDirectory,
            DataDirectoryMigrationResult? result)
        {
            string? pausedFilePath = dataDirectoryMigrationPausedCurrentFilePath;
            dataDirectoryMigrationPausedCurrentFilePath = null;

            if (result != null)
            {
                WayMarkFavorites_Control.RefreshFavorites();
                StartLocalCharacterScan();
                if (TryReloadRelocatedCurrentFile(pausedFilePath ?? currentFilePath, currentDataDirectory, targetDataDirectory))
                {
                    return;
                }
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

        private bool TryReloadRelocatedCurrentFile(
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

            LoadConfigFileWithOverlay(relocatedFilePath);
            return true;
        }
    }
}
