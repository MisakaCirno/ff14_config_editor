namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void RefreshBackupList()
        {
            BackupRestore_Control.RefreshBackupList(allowDuringOperation: true);
        }

        private string GetCurrentFileBackupUserID()
        {
            return configUISave == null || string.IsNullOrWhiteSpace(currentFilePath)
                ? string.Empty
                : appDataStore.ResolveEffectiveBackupUserID(currentFilePath, configUISave.UserIDHex);
        }
    }
}
