namespace UIMarkerEditor;

public sealed record UnknownMapIdPolicyChangeConfirmation(bool RequiresConfirmation)
{
    public const string Title = "确认允许未知地图 ID";
    public const string DoNotShowAgainText = "不再提示";
    public const string Message =
        "允许未知地图 ID 后，工具会接受输入或导入当前地图列表中不存在的地图 ID，只校验数值仍在 DAT 可保存范围内。\n\n" +
        "错误 ID 可能导致标点在游戏内不可用或出现异常表现。请只在确认 ID 有效、或当前地图数据确实滞后时开启。";

    public static UnknownMapIdPolicyChangeConfirmation Evaluate(
        UnknownMapIdPolicy currentPolicy,
        UnknownMapIdPolicy nextPolicy,
        bool showAllowUnknownWarning)
    {
        return new UnknownMapIdPolicyChangeConfirmation(
            currentPolicy == UnknownMapIdPolicy.RejectUnknown &&
            nextPolicy == UnknownMapIdPolicy.AllowUnknown &&
            showAllowUnknownWarning);
    }

    public static bool ShouldDisableFutureConfirmation(bool confirmed, bool doNotShowAgainChecked)
    {
        return confirmed && doNotShowAgainChecked;
    }
}
