using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

public class WayMarkTests
{
    [Fact]
    public void SettingRegionID_UpdatesValueAndRaisesRegionNotification()
    {
        WayMark mark = new();
        List<string> changedProperties = [];
        mark.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName ?? string.Empty);

        mark.RegionID = 1234;

        Assert.Equal((ushort)1234, mark.RegionID);
        Assert.Equal([nameof(WayMark.RegionID)], changedProperties);
    }

    [Fact]
    public void SettingSameRegionID_DoesNotRaiseNotification()
    {
        WayMark mark = new()
        {
            RegionID = 1234
        };
        int notificationCount = 0;
        mark.PropertyChanged += (_, _) => notificationCount++;

        mark.RegionID = 1234;

        Assert.Equal(0, notificationCount);
    }
}
