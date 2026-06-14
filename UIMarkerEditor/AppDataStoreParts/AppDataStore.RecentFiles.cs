using System.IO;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    private const int MaxRecentFileCount = 10;

    public void AddRecentFile(string filePath)
    {
        if (!TryGetFullPath(filePath, out string fullPath)) return;

        List<string> recentFiles = NormalizeRecentFiles(Settings.RecentFiles)
            .Where(path => !string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase))
            .Prepend(fullPath)
            .Take(MaxRecentFileCount)
            .ToList();

        Settings.RecentFiles = recentFiles;
        TrySaveSettings(Settings);
    }

    public List<string> GetRecentFiles()
    {
        return NormalizeRecentFiles(Settings.RecentFiles)
            .Take(MaxRecentFileCount)
            .ToList();
    }

    public void ClearRecentFiles()
    {
        if (Settings.RecentFiles.Count == 0) return;

        Settings.RecentFiles.Clear();
        TrySaveSettings(Settings);
    }

    private bool TrySaveSettings(AppSettings settings)
    {
        try
        {
            SaveSettings(settings);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            AppLogger.Warning(AppLogCategory.IO, "保存最近文件列表失败", ex);
            return false;
        }
        catch (AppDataStoreException ex)
        {
            AppLogger.Warning(AppLogCategory.IO, "保存最近文件列表失败", ex);
            return false;
        }
    }

    private static IEnumerable<string> NormalizeRecentFiles(IEnumerable<string> filePaths)
    {
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string filePath in filePaths)
        {
            if (!TryGetFullPath(filePath, out string fullPath) || !seenPaths.Add(fullPath))
            {
                continue;
            }

            yield return fullPath;
        }
    }

    private static bool TryGetFullPath(string filePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath)) return false;

        try
        {
            fullPath = Path.GetFullPath(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
