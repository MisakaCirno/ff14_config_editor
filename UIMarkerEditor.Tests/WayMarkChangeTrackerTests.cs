using FF14ConfigEditor.UISave;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public class WayMarkChangeTrackerTests
{
    [Fact]
    public void Track_WhenNewSubscriptionFails_KeepsOldTrackingAndCleansNewTracking()
    {
        int changedCount = 0;
        WayMark oldWayMark = new();
        WayMark brokenWayMark = new()
        {
            A = null!
        };
        WayMarkChangeTracker tracker = new((_, _) => changedCount++);
        tracker.Track([oldWayMark]);

        Assert.Throws<NullReferenceException>(() => tracker.Track([brokenWayMark]));

        oldWayMark.RegionID = 1;
        int countAfterOldChange = changedCount;
        Assert.True(countAfterOldChange > 0);

        brokenWayMark.RegionID = 1;
        Assert.Equal(countAfterOldChange, changedCount);
    }

    [Fact]
    public void Track_WhenNewSubscriptionSucceeds_ReplacesOldTracking()
    {
        int changedCount = 0;
        WayMark oldWayMark = new();
        WayMark newWayMark = new();
        WayMarkChangeTracker tracker = new((_, _) => changedCount++);

        tracker.Track([oldWayMark]);
        tracker.Track([newWayMark]);

        oldWayMark.RegionID = 1;
        Assert.Equal(0, changedCount);

        newWayMark.RegionID = 1;
        Assert.True(changedCount > 0);
    }

    [Fact]
    public void Clear_UnsubscribesTrackedWayMarks()
    {
        int changedCount = 0;
        WayMark wayMark = new();
        WayMarkChangeTracker tracker = new((_, _) => changedCount++);

        tracker.Track([wayMark]);
        tracker.Clear();

        wayMark.RegionID = 1;
        Assert.Equal(0, changedCount);
    }
}
