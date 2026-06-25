namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void RefreshCharacterList()
        {
            CharacterProfiles_Control.RefreshCharacterList();
        }

        private async Task SyncServerListIfNeededAsync(bool showFailureMessage = false)
        {
            await CharacterProfiles_Control.SyncServerListIfNeededAsync(showFailureMessage);
            ToolSettings_Control.RefreshOnlineDataStatus();
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
