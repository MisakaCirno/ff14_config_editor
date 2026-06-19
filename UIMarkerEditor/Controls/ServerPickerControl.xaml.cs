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
        UpdateButtonText();
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

        if (string.IsNullOrWhiteSpace(dataCenter) && string.IsNullOrWhiteSpace(world))
        {
            ServerArea_ListBox.SelectedItem = null;
            ServerWorld_ListBox.ItemsSource = null;
            ServerWorld_ListBox.SelectedItem = null;
            UpdateButtonText();
            suppressSelectionChanged = false;
            return;
        }

        ServerGroup? selectedGroup = serverGroups.FirstOrDefault(group =>
            string.Equals(group.DataCenter, dataCenter, StringComparison.OrdinalIgnoreCase));
        selectedGroup ??= serverGroups.FirstOrDefault(group =>
            group.Worlds.Any(candidateWorld => string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase)));

        ServerArea_ListBox.SelectedItem = selectedGroup;
        ServerWorld_ListBox.ItemsSource = selectedGroup?.Worlds;
        ServerWorld_ListBox.SelectedItem = selectedGroup?.Worlds.FirstOrDefault(candidateWorld =>
            string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase));

        if (selectedGroup == null || ServerWorld_ListBox.SelectedItem == null)
        {
            SelectedDataCenter = string.Empty;
            SelectedWorld = string.Empty;
        }

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
        bool hasSelectedWorld = !string.IsNullOrWhiteSpace(SelectedWorld);
        ServerPicker_TextBlock.Text = hasSelectedWorld
            ? $"{SelectedDataCenter} / {SelectedWorld}"
            : "请选择服务器";
        ClearServer_Button.IsEnabled = hasSelectedWorld;
    }

    private void ServerPicker_Button_Click(object sender, RoutedEventArgs e)
    {
        if (ServerArea_ListBox.SelectedItem == null)
        {
            ServerArea_ListBox.SelectedItem = serverGroups.FirstOrDefault();
        }

        ServerPicker_Popup.IsOpen = true;
    }

    private void ClearServer_Button_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedDataCenter) && string.IsNullOrWhiteSpace(SelectedWorld))
        {
            return;
        }

        SelectServer(string.Empty, string.Empty);
        ServerPicker_Popup.IsOpen = false;
        SelectedServerChanged?.Invoke(this, EventArgs.Empty);
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
