namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void LoadSettingsIntoUi()
        {
            ToolSettings_Control.LoadSettingsIntoUi();
            RefreshAppearanceSettings();
        }

        private void RefreshAppearanceSettings()
        {
            WayMarkEditor_Control.ApplyAppearanceSettings(appDataStore.Settings);
            WayMarkFavorites_Control.ApplySettings(appDataStore.Settings);
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
            WayMarkFavorites_Control.RefreshMapDataDisplay();
            RefreshBackupList();
        }
    }
}
