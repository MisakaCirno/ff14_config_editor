namespace UIMarkerEditor.Controls;

public partial class WayMarkEditorControl
{
    public void ApplyAppearanceSettings(AppSettings settings)
    {
        unknownMapIdPolicy = settings.UnknownMapIdPolicy;
        WayMarkEditPanel_Control.ApplyAppearanceSettings(settings);
    }
}
