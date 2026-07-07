namespace FF14ConfigEditor.UISave;

public static class WayMarkCoordinateConverter
{
    public const int MinRawCoordinate = int.MinValue;
    public const int MaxRawCoordinate = int.MaxValue;
    public const int CoordinateScale = 1000;

    public static bool TryRoundToRawCoordinate(double value, out int rawCoordinate)
    {
        rawCoordinate = 0;
        if (!double.IsFinite(value)) return false;

        decimal rawValue;
        try
        {
            rawValue = (decimal)value * CoordinateScale;
        }
        catch (OverflowException)
        {
            return false;
        }

        if (rawValue < MinRawCoordinate || rawValue > MaxRawCoordinate)
        {
            return false;
        }

        // 对象模型允许 UI 输入被取整到 raw 坐标；分享码导入会拒绝超过 3 位小数，避免静默改写外部数据。
        decimal roundedRawValue = decimal.Round(rawValue, 0, MidpointRounding.AwayFromZero);
        rawCoordinate = (int)roundedRawValue;
        return true;
    }
}
