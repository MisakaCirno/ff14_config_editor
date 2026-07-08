namespace UIMarkerEditor;

internal static class UserMapDataRepairPrompt
{
    public static string BuildMessage(MapDataLoadResult result)
    {
        return
            $"用户填写地图数据存在问题，需要修复后才能作为地图数据来源使用。{Environment.NewLine}{Environment.NewLine}" +
            $"当前状态：{FormatCurrentState(result)}{Environment.NewLine}{Environment.NewLine}" +
            $"原因：{FormatFailure(result)}{Environment.NewLine}{Environment.NewLine}" +
            "是否现在打开编辑器修复？";
    }

    private static string FormatCurrentState(MapDataLoadResult result)
    {
        if (result.Success && result.UsedCache)
        {
            return "没有应用这个异常文件，已暂时继续使用同来源的本地缓存快照。";
        }

        if (result.Success)
        {
            return "没有应用这个异常文件，仍在使用当前已加载的地图数据。";
        }

        return "没有应用这个异常文件，当前没有可用地图数据快照，区域选择和导入校验会受限。";
    }

    private static string FormatFailure(MapDataLoadResult result)
    {
        string reason = string.IsNullOrWhiteSpace(result.FailureReason)
            ? "未知原因。"
            : result.FailureReason;
        return string.IsNullOrWhiteSpace(result.FailureStage)
            ? reason
            : $"{result.FailureStage}失败：{reason}";
    }
}
