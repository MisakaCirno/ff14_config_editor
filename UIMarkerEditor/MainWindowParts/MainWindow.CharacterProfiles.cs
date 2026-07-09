namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void RefreshCharacterList()
        {
            CharacterProfiles_Control.RefreshCharacterList();
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
            if (e.OriginalSource != MainTab_Control || !CharacterProfiles_TabItem.IsSelected)
            {
                return;
            }

            await SyncServerListIfNeededAsync();
        }

        private bool ConfirmSaveOrDiscardCharacterChanges()
        {
            return CharacterProfiles_Control.ConfirmSaveOrDiscardCharacterChanges();
        }
    }
}
