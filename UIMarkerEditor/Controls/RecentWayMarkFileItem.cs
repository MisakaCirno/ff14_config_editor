namespace UIMarkerEditor.Controls;

public sealed record RecentWayMarkFileItem(
    string FilePath,
    string DisplayName,
    string UserID,
    string Note,
    string ToolTip,
    bool Exists);

public sealed class RecentWayMarkFileRequestedEventArgs(string filePath) : EventArgs
{
    public string FilePath { get; } = filePath;
}
