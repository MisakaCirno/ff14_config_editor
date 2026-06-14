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

        decimal roundedRawValue = decimal.Round(rawValue, 0, MidpointRounding.AwayFromZero);
        rawCoordinate = (int)roundedRawValue;
        return true;
    }
}
