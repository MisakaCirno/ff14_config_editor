namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void RefreshCharacterList()
        {
            CharacterProfiles_Control.RefreshCharacterList();
        }

        private Task SyncServerListIfNeededAsync()
        {
            return CharacterProfiles_Control.SyncServerListIfNeededAsync();
        }

        private bool ConfirmSaveOrDiscardCharacterChanges()
        {
            return CharacterProfiles_Control.ConfirmSaveOrDiscardCharacterChanges();
        }
    }
}
