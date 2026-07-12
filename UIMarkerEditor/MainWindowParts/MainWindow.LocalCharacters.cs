using System.IO;
using System.Windows;
using FF14ConfigEditor;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private async Task<bool> ScanLocalCharactersAsync()
        {
            try
            {
                LocalGameCharacterScanPreparation preparation = await Task.Run(appDataStore.PrepareLocalGameCharacterScan);
                if (string.IsNullOrWhiteSpace(preparation.GameCharacterRootDirectory))
                {
                    return false;
                }

                LocalGameCharacterScanResult result = appDataStore.ApplyLocalGameCharacterScan(preparation);
                foreach (ClientLogCharacterNameScanError error in result.Errors)
                {
                    AppLogger.Warning(AppLogCategory.IO, $"启动本地角色扫描跳过：{error.Path}；{error.Message}");
                }

                if (result.SkippedBecauseGameInstallDirectoryChanged ||
                    result.SkippedBecauseCharactersChanged)
                {
                    return false;
                }

                if (result.Changed)
                {
                    RefreshCharacterListFromExternalChange();
                    RefreshBackupList();
                }

                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
            {
                AppLogger.Warning(AppLogCategory.IO, "启动本地角色扫描失败", ex);
                return false;
            }
            finally
            {
                RefreshLocalCharacterSelectionAvailability();
            }
        }

        private void StartLocalCharacterScan()
        {
            _ = ScanLocalCharactersAsync();
        }

        private void RefreshLocalCharacterSelectionAvailability()
        {
            bool canSelectLocalCharacter = appDataStore.GetAvailableLocalGameCharacters().Count > 0;
            WayMarkEditor_Control.SetLocalCharacterSelectionAvailable(
                canSelectLocalCharacter);
            OpenLocalCharacter_MenuItem.Visibility = canSelectLocalCharacter
                ? Visibility.Visible
                : Visibility.Collapsed;
            OpenLocalCharacter_MenuItem.IsEnabled = canSelectLocalCharacter && !isWayMarkFileLoading;
        }

        private void OpenLocalCharacter_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenLocalGameCharacterPicker();
        }

        private void OpenLocalGameCharacterPicker()
        {
            if (IsBlockingOperationInProgress())
            {
                return;
            }

            IReadOnlyList<LocalGameCharacter> localCharacters = appDataStore.GetAvailableLocalGameCharacters();
            if (localCharacters.Count == 0)
            {
                RefreshLocalCharacterSelectionAvailability();
                AppMessageBox.Show(
                    this,
                    "当前没有可直接打开的本地角色。请确认游戏安装目录正确，并且角色目录下存在 UISAVE.DAT。若是首次使用，建议先启动一次游戏并进入角色后再尝试使用本工具。",
                    "选择游戏角色",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            LocalGameCharacterPickerDialog dialog = new(localCharacters);
            DialogOwnerHelper.ConfigureOwnedDialog(dialog, this);
            if (dialog.ShowDialog() != true || dialog.SelectedCharacter == null)
            {
                return;
            }

            string saveFilePath = dialog.SelectedCharacter.SaveFilePath;
            if (!File.Exists(saveFilePath))
            {
                RefreshLocalCharacterSelectionAvailability();
                AppMessageBox.Show(
                    this,
                    $"选择的角色文件已经不存在。\n\n文件：{saveFilePath}",
                    "选择游戏角色",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!ConfirmSaveOrDiscardWayMarkChanges())
            {
                return;
            }

            LoadConfigFileWithOverlay(saveFilePath);
        }
    }
}
