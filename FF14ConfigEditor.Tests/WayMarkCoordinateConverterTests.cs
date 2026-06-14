using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

public class WayMarkCoordinateConverterTests
{
    [Theory]
    [InlineData(1.2345, 1235)]
    [InlineData(-1.2345, -1235)]
    [InlineData(0, 0)]
    public void TryRoundToRawCoordinate_ValidCoordinate_ReturnsRoundedRawValue(double value, int expectedRawCoordinate)
    {
        bool result = WayMarkCoordinateConverter.TryRoundToRawCoordinate(value, out int rawCoordinate);

        Assert.True(result);
        Assert.Equal(expectedRawCoordinate, rawCoordinate);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void TryRoundToRawCoordinate_InvalidCoordinate_ReturnsFalse(double value)
    {
        bool result = WayMarkCoordinateConverter.TryRoundToRawCoordinate(value, out int rawCoordinate);

        Assert.False(result);
        Assert.Equal(0, rawCoordinate);
    }

    [Theory]
    [InlineData(2147483.647, int.MaxValue)]
    [InlineData(-2147483.648, int.MinValue)]
    public void TryRoundToRawCoordinate_RawBoundary_ReturnsTrue(double value, int expectedRawCoordinate)
    {
        bool result = WayMarkCoordinateConverter.TryRoundToRawCoordinate(value, out int rawCoordinate);

        Assert.True(result);
        Assert.Equal(expectedRawCoordinate, rawCoordinate);
    }

    [Theory]
    [InlineData(2147483.648)]
    [InlineData(2147483.6474)]
    [InlineData(-2147483.649)]
    [InlineData(-2147483.6484)]
    public void TryRoundToRawCoordinate_OutsideRawBoundary_ReturnsFalse(double value)
    {
        bool result = WayMarkCoordinateConverter.TryRoundToRawCoordinate(value, out int rawCoordinate);

        Assert.False(result);
        Assert.Equal(0, rawCoordinate);
    }
}
