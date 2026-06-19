using System.Windows.Input;

namespace UIMarkerEditor.Controls;

public partial class WayMarkEditorControl
{
    private void WayMarkEditorControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        HandleWayMarkClipboardShortcut(e);
    }
}
