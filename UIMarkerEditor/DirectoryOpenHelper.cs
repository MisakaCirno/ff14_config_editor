using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace UIMarkerEditor;

internal static class DirectoryOpenHelper
{
    public static bool OpenExistingDirectory(
        Window? owner,
        string directory,
        string caption,
        string emptyMessage = "目录为空。",
        string missingMessage = "目录不存在。")
    {
        return OpenDirectory(
            owner,
            directory,
            caption,
            createIfMissing: false,
            emptyMessage,
            missingMessage);
    }

    public static bool OpenOrCreateDirectory(
        Window? owner,
        string directory,
        string caption,
        string emptyMessage = "目录为空。")
    {
        return OpenDirectory(
            owner,
            directory,
            caption,
            createIfMissing: true,
            emptyMessage,
            missingMessage: "目录不存在。");
    }

    private static bool OpenDirectory(
        Window? owner,
        string directory,
        string caption,
        bool createIfMissing,
        string emptyMessage,
        string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            AppMessageBox.Show(owner, emptyMessage, caption, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        string fullDirectory;
        try
        {
            fullDirectory = Path.GetFullPath(directory.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            AppMessageBox.Show(owner, $"目录路径无效：{ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!createIfMissing && !Directory.Exists(fullDirectory))
        {
            AppMessageBox.Show(owner, missingMessage, caption, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        try
        {
            if (createIfMissing)
            {
                Directory.CreateDirectory(fullDirectory);
            }

            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = fullDirectory,
                UseShellExecute = true
            });
            if (process == null)
            {
                AppMessageBox.Show(owner, $"{caption}失败：系统没有返回打开目录进程。", caption, MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        catch (Exception ex) when (IsDirectoryOpenException(ex))
        {
            AppMessageBox.Show(owner, $"{caption}失败：{ex.Message}", caption, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private static bool IsDirectoryOpenException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or PathTooLongException or Win32Exception or InvalidOperationException;
    }
}
