using System.Windows;
using System.Windows.Controls;

namespace UIMarkerEditor.Controls;

public partial class ServerPickerControl : UserControl
{
    private readonly List<ServerGroup> serverGroups = [];
    private bool suppressSelectionChanged;
    private bool selectedServerAvailable = true;

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
        try
        {
            SelectedDataCenter = dataCenter;
            SelectedWorld = world;
            selectedServerAvailable = true;

            if (string.IsNullOrWhiteSpace(dataCenter) && string.IsNullOrWhiteSpace(world))
            {
                ServerArea_ListBox.SelectedItem = null;
                ServerWorld_ListBox.ItemsSource = null;
                ServerWorld_ListBox.SelectedItem = null;
                UpdateButtonText();
                return;
            }

            ServerGroup? selectedGroup = serverGroups.FirstOrDefault(group =>
                string.Equals(group.DataCenter, dataCenter, StringComparison.OrdinalIgnoreCase));
            selectedGroup ??= serverGroups.FirstOrDefault(group =>
                group.Worlds.Any(candidateWorld => string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase)));

            string? selectedWorld = selectedGroup?.Worlds.FirstOrDefault(candidateWorld =>
                string.Equals(candidateWorld, world, StringComparison.OrdinalIgnoreCase));
            ServerArea_ListBox.SelectedItem = selectedGroup;
            ServerWorld_ListBox.ItemsSource = selectedGroup?.Worlds;
            ServerWorld_ListBox.SelectedItem = selectedWorld;

            if (selectedGroup == null || selectedWorld == null)
            {
                selectedServerAvailable = false;
            }
            else
            {
                SelectedDataCenter = selectedGroup.DataCenter;
                SelectedWorld = selectedWorld;
            }

            UpdateButtonText();
        }
        finally
        {
            suppressSelectionChanged = false;
        }
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
        string selectedServerText = string.Join(" / ", new[] { SelectedDataCenter, SelectedWorld }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        ServerPicker_TextBlock.Text = hasSelectedWorld
            ? selectedServerAvailable
                ? selectedServerText
                : $"已保存：{selectedServerText}（当前列表不可用）"
            : "请选择服务器";
        ServerPicker_TextBlock.ToolTip = hasSelectedWorld && !selectedServerAvailable
            ? "当前服务器列表中找不到这个已保存的服务器，保存时会保留原值。"
            : null;
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
