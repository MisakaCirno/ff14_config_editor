namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void LoadSettingsIntoUi()
        {
            ToolSettings_Control.LoadSettingsIntoUi();
        }

        private void RefreshServerListConsumers()
        {
            CharacterProfiles_Control.RefreshServerPicker();
            RefreshCharacterList();
        }

        private void RefreshMapDataConsumers()
        {
            UpdateDataVersionText();
            WayMarkEditor_Control.RefreshMapDataDisplay();
            RefreshBackupList();
        }
    }
}
