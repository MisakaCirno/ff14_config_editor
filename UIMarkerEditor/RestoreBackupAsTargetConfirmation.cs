using System.IO;

namespace UIMarkerEditor;

internal sealed class RestoreBackupAsTargetConfirmation
{
    private const string ExpectedSaveFileName = "UISAVE.DAT";

    private RestoreBackupAsTargetConfirmation(
        bool isExpectedSaveFileName,
        bool targetExists,
        string targetFullPath)
    {
        IsExpectedSaveFileName = isExpectedSaveFileName;
        TargetExists = targetExists;
        TargetFullPath = targetFullPath;
        Message = BuildMessage();
    }

    public bool IsExpectedSaveFileName { get; }

    public bool TargetExists { get; }

    public string TargetFullPath { get; }

    public bool RequiresConfirmation => !IsExpectedSaveFileName || TargetExists;

    public string Message { get; }

    public static RestoreBackupAsTargetConfirmation Evaluate(string targetFilePath, bool targetExists)
    {
        string targetFileName = Path.GetFileName(targetFilePath);
        bool isExpectedSaveFileName = string.Equals(
            targetFileName,
            ExpectedSaveFileName,
            StringComparison.OrdinalIgnoreCase);

        return new RestoreBackupAsTargetConfirmation(
            isExpectedSaveFileName,
            targetExists,
            GetDisplayFullPath(targetFilePath));
    }

    private string BuildMessage()
    {
        if (!RequiresConfirmation)
        {
            return string.Empty;
        }

        string headline = (!IsExpectedSaveFileName, TargetExists) switch
        {
            (true, true) => "你选择的目标文件名不是 UISAVE.DAT，并且目标文件已存在。",
            (true, false) => "你选择的目标文件名不是 UISAVE.DAT。",
            (false, true) => "目标文件已存在。",
            _ => string.Empty
        };

        string action = TargetExists
            ? "确认后将把备份写入此位置，并覆盖此文件。"
            : "确认后将把备份写入此位置。";

        return
            $"{headline}\n\n" +
            $"目标路径：\n{TargetFullPath}\n\n" +
            $"{action}\n" +
            "请确认这确实是你想覆盖/写入的位置。\n\n" +
            "确定继续吗？";
    }

    private static string GetDisplayFullPath(string targetFilePath)
    {
        try
        {
            return Path.GetFullPath(targetFilePath);
        }
        catch
        {
            return targetFilePath;
        }
    }
}
