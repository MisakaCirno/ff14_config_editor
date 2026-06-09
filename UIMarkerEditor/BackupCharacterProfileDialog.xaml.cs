using System.Windows;
using System.Windows.Controls;

namespace UIMarkerEditor;

public partial class BackupCharacterProfileDialog : Window
{
    private readonly List<ServerGroup> serverGroups;
    private string selectedDataCenter = string.Empty;
    private string selectedWorld = string.Empty;

    public string CharacterName => CharacterName_TextBox.Text.Trim();
    public string DataCenter => selectedDataCenter;
    public string World => selectedWorld;
    public string Note => Note_TextBox.Text.Trim();

    public BackupCharacterProfileDialog(
        string userID,
        IEnumerable<ServerGroup> serverGroups,
        CharacterProfile? existingProfile)
    {
        InitializeComponent();

        this.serverGroups = [.. serverGroups];
        UserID_TextBox.Text = userID;
        CharacterName_TextBox.Text = existingProfile?.CharacterName ?? string.Empty;
        Note_TextBox.Text = existingProfile?.Note ?? string.Empty;

        ServerArea_ListBox.ItemsSource = this.serverGroups;
        SelectServer(existingProfile?.DataCenter ?? string.Empty, existingProfile?.World ?? string.Empty);
    }

    private void SelectServer(string dataCenter, string world)
    {
        selectedDataCenter = dataCenter;
        selectedWorld = world;

        ServerGroup? selectedGroup = serverGroups.FirstOrDefault(group =>
            string.Equals(group.DataCenter, dataCenter, StringComparison.OrdinalIgnoreCase));
        selectedGroup ??= serverGroups.FirstOrDefault(group =>
            group.Worlds.Any(candidateWorld => string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase)));
        selectedGroup ??= serverGroups.FirstOrDefault();

        ServerArea_ListBox.SelectedItem = selectedGroup;
        ServerWorld_ListBox.ItemsSource = selectedGroup?.Worlds;
        ServerWorld_ListBox.SelectedItem = selectedGroup?.Worlds.FirstOrDefault(candidateWorld =>
            string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase));
        UpdateServerPickerButtonText();
    }

    private void UpdateServerPickerButtonText()
    {
        ServerPicker_TextBlock.Text = string.IsNullOrWhiteSpace(selectedWorld)
            ? "请选择服务器"
            : $"{selectedDataCenter} / {selectedWorld}";
    }

    private void ServerPicker_Button_Click(object sender, RoutedEventArgs e)
    {
        ServerPicker_Popup.IsOpen = true;
    }

    private void ServerArea_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerArea_ListBox.SelectedItem is not ServerGroup group)
        {
            ServerWorld_ListBox.ItemsSource = null;
            return;
        }

        ServerWorld_ListBox.ItemsSource = group.Worlds;
    }

    private void ServerWorld_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServerArea_ListBox.SelectedItem is not ServerGroup group ||
            ServerWorld_ListBox.SelectedItem is not string world)
        {
            return;
        }

        selectedDataCenter = group.DataCenter;
        selectedWorld = world;
        UpdateServerPickerButtonText();
        ServerPicker_Popup.IsOpen = false;
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
