using System.Windows;
using System.Windows.Controls;
using FF14ConfigEditor.UISave;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class WayMarkEditorControlTests
{
    [Fact]
    public void ImportSnapshotToSelectedWayMark_WhenRejectUnknownMapId_BlocksUnknownSnapshot()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            ushort unknownRegionId = FindUnknownRegionId();
            WayMark targetMark = new()
            {
                RegionID = MapData.EmptyRegionId
            };
            WayMarkEditorControl control = CreateControl(
                [targetMark],
                UnknownMapIdPolicy.RejectUnknown,
                selectedIndex: 0);
            WayMarkSnapshot snapshot = new()
            {
                RegionID = unknownRegionId
            };

            Assert.False(control.ImportSnapshotToSelectedWayMark(snapshot));
            Assert.Equal(MapData.EmptyRegionId, targetMark.RegionID);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void ImportSnapshotToSelectedWayMark_WhenRejectUnknownMapId_AllowsLoadedUnknownSnapshot()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            ushort unknownRegionId = FindUnknownRegionId();
            WayMark existingUnknownMark = new()
            {
                RegionID = unknownRegionId
            };
            WayMark targetMark = new()
            {
                RegionID = MapData.EmptyRegionId
            };
            WayMarkEditorControl control = CreateControl(
                [existingUnknownMark, targetMark],
                UnknownMapIdPolicy.RejectUnknown,
                selectedIndex: 1);
            WayMarkSnapshot snapshot = new()
            {
                RegionID = unknownRegionId
            };

            Assert.True(control.ImportSnapshotToSelectedWayMark(snapshot));
            Assert.Equal(unknownRegionId, targetMark.RegionID);
        });

        Assert.Null(exception);
    }

    private static WayMarkEditorControl CreateControl(
        List<WayMark> wayMarks,
        UnknownMapIdPolicy unknownMapIdPolicy,
        int selectedIndex)
    {
        WpfTestHost.EnsureApplicationResources();
        WayMarkEditorControl control = new();
        control.ApplyAppearanceSettings(new AppSettings
        {
            UnknownMapIdPolicy = unknownMapIdPolicy
        });
        control.SetWayMarks(wayMarks);
        control.Measure(new Size(900, 600));
        control.Arrange(new Rect(0, 0, 900, 600));
        control.UpdateLayout();

        ListBox listBox = Assert.IsType<ListBox>(control.FindName("WayMark_ListBox"));
        listBox.SelectedIndex = selectedIndex;
        control.UpdateLayout();
        return control;
    }

    private static ushort FindUnknownRegionId()
    {
        IReadOnlySet<ushort> knownMapIds = MapData.GetKnownMapIds();
        for (int regionId = ushort.MaxValue; regionId > MapData.EmptyRegionId; regionId--)
        {
            ushort candidate = (ushort)regionId;
            if (!knownMapIds.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No unknown region id is available.");
    }
}
