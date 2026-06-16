using System.IO;

namespace UIMarkerEditor;

internal static class WayMarkDocumentTitleFormatter
{
    public static string BuildTitle(string defaultTitle, string currentFilePath, bool hasUnsavedChanges)
    {
        if (string.IsNullOrWhiteSpace(currentFilePath))
        {
            return defaultTitle;
        }

        string fileName = Path.GetFileName(currentFilePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = currentFilePath;
        }

        return hasUnsavedChanges
            ? $"* {fileName}（未保存） - {defaultTitle}"
            : $"{fileName} - {defaultTitle}";
    }
}
