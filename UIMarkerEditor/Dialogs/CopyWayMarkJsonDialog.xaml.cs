using System.Windows;

namespace UIMarkerEditor;

public partial class CopyWayMarkJsonDialog : Window
{
    public CopyWayMarkJsonDialog(string json)
    {
        InitializeComponent();
        Json_TextBox.Text = json;
        Loaded += (_, _) =>
        {
            Json_TextBox.Focus();
            Json_TextBox.SelectAll();
        };
    }
}
