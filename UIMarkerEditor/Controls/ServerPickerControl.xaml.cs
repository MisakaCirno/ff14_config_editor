using System.Windows;
using System.Windows.Controls;

namespace UIMarkerEditor.Controls;

public partial class ServerPickerControl : UserControl
{
    private readonly List<ServerGroup> serverGroups = [];
    private bool suppressSelectionChanged;

    public event EventHandler? SelectedServerChanged;

    public string SelectedDataCenter { get; private set; } = string.Empty;

    public string SelectedWorld { get; private set; } = string.Empty;

    public ServerPickerControl()
    {
        InitializeComponent();
    }

    public void SetServerGroups(IEnumerable<ServerGroup> groups)
    {
        serverGroups.Clear();
        serverGroups.AddRange(groups);
        ServerArea_ListBox.ItemsSource = serverGroups;
        SelectServer(SelectedDataCenter, SelectedWorld);
    }

    public void SelectServer(string dataCenter, string world)
    {
        suppressSelectionChanged = true;
        SelectedDataCenter = dataCenter;
        SelectedWorld = world;

        ServerGroup? selectedGroup = serverGroups.FirstOrDefault(group =>
            string.Equals(group.DataCenter, dataCenter, StringComparison.OrdinalIgnoreCase));
        selectedGroup ??= serverGroups.FirstOrDefault(group =>
            group.Worlds.Any(candidateWorld => string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase)));
        selectedGroup ??= serverGroups.FirstOrDefault();

        ServerArea_ListBox.SelectedItem = selectedGroup;
        ServerWorld_ListBox.ItemsSource = selectedGroup?.Worlds;
        ServerWorld_ListBox.SelectedItem = selectedGroup?.Worlds.FirstOrDefault(candidateWorld =>
            string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase));

        UpdateButtonText();
        suppressSelectionChanged = false;
    }

    public (string DataCenter, string World)? GetSelectedServer()
    {
        return string.IsNullOrWhiteSpace(SelectedWorld)
            ? null
            : (SelectedDataCenter, SelectedWorld);
    }

    private void UpdateButtonText()
    {
        ServerPicker_TextBlock.Text = string.IsNullOrWhiteSpace(SelectedWorld)
            ? "请选择服务器"
            : $"{SelectedDataCenter} / {SelectedWorld}";
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
        if (suppressSelectionChanged ||
            ServerArea_ListBox.SelectedItem is not ServerGroup group ||
            ServerWorld_ListBox.SelectedItem is not string world)
        {
            return;
        }

        SelectedDataCenter = group.DataCenter;
        SelectedWorld = world;
        UpdateButtonText();
        ServerPicker_Popup.IsOpen = false;
        SelectedServerChanged?.Invoke(this, EventArgs.Empty);
    }
}
