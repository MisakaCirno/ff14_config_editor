using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace UIMarkerEditor.Controls;

public partial class OpenFileOverlayControl : UserControl
{
    public ObservableCollection<RecentWayMarkFileItem> RecentFiles { get; } = [];

    public event EventHandler? SelectLocalCharacterRequested;
    public event EventHandler<RecentWayMarkFileRequestedEventArgs>? RecentFileRequested;

    public OpenFileOverlayControl()
    {
        InitializeComponent();
        UpdateRecentFilesVisibility();
    }

    public void SetLocalCharacterSelectionAvailable(bool isAvailable)
    {
        OpenLocalCharacter_Button.Visibility = isAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public void SetRecentFiles(IEnumerable<RecentWayMarkFileItem> recentFiles)
    {
        RecentFiles.Clear();
        foreach (RecentWayMarkFileItem recentFile in recentFiles)
        {
            RecentFiles.Add(recentFile);
        }

        UpdateRecentFilesVisibility();
    }

    private void UpdateRecentFilesVisibility()
    {
        bool hasRecentFiles = RecentFiles.Count > 0;
        NoRecentFiles_TextBlock.Visibility = hasRecentFiles
            ? Visibility.Collapsed
            : Visibility.Visible;
        RecentFiles_ListBox.Visibility = hasRecentFiles
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OpenLocalCharacter_Button_Click(object sender, RoutedEventArgs e)
    {
        SelectLocalCharacterRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RecentFiles_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecentFiles_ListBox.SelectedItem is not RecentWayMarkFileItem recentFile)
        {
            return;
        }

        RecentFiles_ListBox.SelectedItem = null;
        RecentFileRequested?.Invoke(this, new RecentWayMarkFileRequestedEventArgs(recentFile.FilePath));
    }
}
