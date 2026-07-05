using System.Windows;

namespace UIMarkerEditor;

public partial class StartupLoadingWindow : Window
{
    public StartupLoadingWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status)
    {
        Status_TextBlock.Text = status;
    }
}
