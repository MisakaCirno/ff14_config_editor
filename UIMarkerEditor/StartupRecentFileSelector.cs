namespace UIMarkerEditor;

internal sealed record StartupRecentFileSelection(
    bool HasRecentFiles,
    string FilePath,
    bool SkippedMissingFiles)
{
    public bool HasExistingFile => !string.IsNullOrWhiteSpace(FilePath);
}

internal static class StartupRecentFileSelector
{
    public static StartupRecentFileSelection SelectFirstExisting(
        IEnumerable<string> recentFiles,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(fileExists);

        bool hasRecentFiles = false;
        bool skippedMissingFiles = false;

        foreach (string filePath in recentFiles)
        {
            hasRecentFiles = true;
            if (!string.IsNullOrWhiteSpace(filePath) && fileExists(filePath))
            {
                return new StartupRecentFileSelection(
                    HasRecentFiles: true,
                    filePath,
                    skippedMissingFiles);
            }

            skippedMissingFiles = true;
        }

        return new StartupRecentFileSelection(
            hasRecentFiles,
            string.Empty,
            skippedMissingFiles);
    }
}
