namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void RefreshCharacterList()
        {
            CharacterProfiles_Control.RefreshCharacterList();
        }

        private async Task SyncServerListIfNeededAsync()
        {
            await CharacterProfiles_Control.SyncServerListIfNeededAsync();
            ToolSettings_Control.RefreshOnlineDataStatus();
        }

        private bool ConfirmSaveOrDiscardCharacterChanges()
        {
            return CharacterProfiles_Control.ConfirmSaveOrDiscardCharacterChanges();
        }
    }
}
