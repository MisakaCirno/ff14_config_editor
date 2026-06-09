using System.Windows;

namespace UIMarkerEditor;

public partial class BackupCharacterProfileDialog : Window
{
    public string CharacterName => CharacterName_TextBox.Text.Trim();
    public string DataCenter => ServerPicker_Control.SelectedDataCenter;
    public string World => ServerPicker_Control.SelectedWorld;
    public string Note => Note_TextBox.Text.Trim();

    public BackupCharacterProfileDialog(
        string userID,
        IEnumerable<ServerGroup> serverGroups,
        CharacterProfile? existingProfile)
    {
        InitializeComponent();

        UserID_TextBox.Text = userID;
        CharacterName_TextBox.Text = existingProfile?.CharacterName ?? string.Empty;
        Note_TextBox.Text = existingProfile?.Note ?? string.Empty;

        ServerPicker_Control.SetServerGroups(serverGroups);
        ServerPicker_Control.SelectServer(existingProfile?.DataCenter ?? string.Empty, existingProfile?.World ?? string.Empty);
    }

    private void Ok_Button_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(World))
        {
            MessageBox.Show(this, "请选择服务器。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
