using System.IO;

namespace UIMarkerEditor;

internal static class DataDirectoryPathRelocator
{
    public static bool TryRelocatePath(
        string filePath,
        string sourceDataDirectory,
        string targetDataDirectory,
        out string relocatedPath)
    {
        relocatedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath) ||
            string.IsNullOrWhiteSpace(sourceDataDirectory) ||
            string.IsNullOrWhiteSpace(targetDataDirectory))
        {
            return false;
        }

        try
        {
            string sourceFullPath = Path.GetFullPath(sourceDataDirectory);
            string targetFullPath = Path.GetFullPath(targetDataDirectory);
            string fileFullPath = Path.GetFullPath(filePath);
            string relativePath = Path.GetRelativePath(sourceFullPath, fileFullPath);
            if (string.IsNullOrWhiteSpace(relativePath) ||
                relativePath == "." ||
                relativePath == ".." ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relativePath))
            {
                return false;
            }

            relocatedPath = Path.GetFullPath(Path.Combine(targetFullPath, relativePath));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            relocatedPath = string.Empty;
            return false;
        }
    }
}
