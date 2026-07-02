using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

public class WayMarkPointTests
{
    [Fact]
    public void SettingRawCoordinate_UpdatesFloatCoordinateAndRaisesNotifications()
    {
        WayMarkPoint point = new();
        List<string> changedProperties = [];
        point.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName ?? string.Empty);

        point.X = 1234;

        Assert.Equal(1.234f, point.FloatX);
        Assert.Contains(nameof(WayMarkPoint.X), changedProperties);
        Assert.Contains(nameof(WayMarkPoint.FloatX), changedProperties);
    }

    [Fact]
    public void SettingFloatCoordinate_UpdatesRawCoordinateAndRaisesNotifications()
    {
        WayMarkPoint point = new();
        List<string> changedProperties = [];
        point.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName ?? string.Empty);

        point.FloatY = -12.345f;

        Assert.Equal(-12345, point.Y);
        Assert.Equal([nameof(WayMarkPoint.Y), nameof(WayMarkPoint.FloatY)], changedProperties);
    }

    [Fact]
    public void SettingFloatCoordinate_WhenRoundedRawCoordinateIsUnchanged_DoesNotRaiseNotification()
    {
        WayMarkPoint point = new()
        {
            X = 1235
        };
        int notificationCount = 0;
        point.PropertyChanged += (_, _) => notificationCount++;

        point.FloatX = 1.2345f;

        Assert.Equal(1235, point.X);
        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void SettingFloatCoordinate_UsesCoordinateConverterRounding()
    {
        WayMarkPoint point = new();

        point.FloatX = 1.2345f;

        Assert.Equal(1235, point.X);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void SettingInvalidFloatCoordinate_RejectsValueAndLeavesRawCoordinateUnchanged(float invalidValue)
    {
        WayMarkPoint point = new()
        {
            Z = 42
        };
        int notificationCount = 0;
        point.PropertyChanged += (_, _) => notificationCount++;

        Assert.Throws<ArgumentOutOfRangeException>(() => point.FloatZ = invalidValue);

        Assert.Equal(42, point.Z);
        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public void SettingSameRawCoordinate_DoesNotRaiseNotification()
    {
        WayMarkPoint point = new()
        {
            Z = 1000
        };
        int notificationCount = 0;
        point.PropertyChanged += (_, _) => notificationCount++;

        point.Z = 1000;

        Assert.Equal(0, notificationCount);
    }
}
