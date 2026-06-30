using System.ComponentModel;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor;

internal sealed class WayMarkChangeTracker
{
    private readonly PropertyChangedEventHandler propertyChangedHandler;
    private readonly List<WayMark> trackedWayMarks = [];

    public WayMarkChangeTracker(PropertyChangedEventHandler propertyChangedHandler)
    {
        this.propertyChangedHandler = propertyChangedHandler;
    }

    public void Track(IEnumerable<WayMark> wayMarks)
    {
        ArgumentNullException.ThrowIfNull(wayMarks);

        List<WayMark> nextTrackedWayMarks = [];
        try
        {
            foreach (WayMark wayMark in wayMarks)
            {
                SubscribeWayMark(wayMark);
                nextTrackedWayMarks.Add(wayMark);
            }
        }
        catch
        {
            UnsubscribeWayMarks(nextTrackedWayMarks);
            throw;
        }

        Clear();
        trackedWayMarks.AddRange(nextTrackedWayMarks);
    }

    public void Clear()
    {
        UnsubscribeWayMarks(trackedWayMarks);
        trackedWayMarks.Clear();
    }

    private void SubscribeWayMark(WayMark wayMark)
    {
        wayMark.PropertyChanged += propertyChangedHandler;
        List<WayMarkPoint> subscribedPoints = [];
        try
        {
            SubscribeWayMarkPoint(wayMark.A, subscribedPoints);
            SubscribeWayMarkPoint(wayMark.B, subscribedPoints);
            SubscribeWayMarkPoint(wayMark.C, subscribedPoints);
            SubscribeWayMarkPoint(wayMark.D, subscribedPoints);
            SubscribeWayMarkPoint(wayMark.One, subscribedPoints);
            SubscribeWayMarkPoint(wayMark.Two, subscribedPoints);
            SubscribeWayMarkPoint(wayMark.Three, subscribedPoints);
            SubscribeWayMarkPoint(wayMark.Four, subscribedPoints);
        }
        catch
        {
            foreach (WayMarkPoint point in subscribedPoints)
            {
                point.PropertyChanged -= propertyChangedHandler;
            }

            wayMark.PropertyChanged -= propertyChangedHandler;
            throw;
        }
    }

    private void SubscribeWayMarkPoint(WayMarkPoint point, ICollection<WayMarkPoint> subscribedPoints)
    {
        point.PropertyChanged += propertyChangedHandler;
        subscribedPoints.Add(point);
    }

    private void UnsubscribeWayMarks(IEnumerable<WayMark> wayMarks)
    {
        foreach (WayMark wayMark in wayMarks)
        {
            wayMark.PropertyChanged -= propertyChangedHandler;
            UnsubscribeWayMarkPoints(wayMark);
        }
    }

    private void UnsubscribeWayMarkPoints(WayMark wayMark)
    {
        wayMark.A.PropertyChanged -= propertyChangedHandler;
        wayMark.B.PropertyChanged -= propertyChangedHandler;
        wayMark.C.PropertyChanged -= propertyChangedHandler;
        wayMark.D.PropertyChanged -= propertyChangedHandler;
        wayMark.One.PropertyChanged -= propertyChangedHandler;
        wayMark.Two.PropertyChanged -= propertyChangedHandler;
        wayMark.Three.PropertyChanged -= propertyChangedHandler;
        wayMark.Four.PropertyChanged -= propertyChangedHandler;
    }
}
