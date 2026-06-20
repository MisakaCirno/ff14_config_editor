using System.Windows.Controls;

namespace UIMarkerEditor.Controls;

public partial class HelpAboutControl : UserControl
{
    public HelpAboutControl()
    {
        InitializeComponent();
        BuildUsageContent();
        BuildAboutContent();
    }
}
