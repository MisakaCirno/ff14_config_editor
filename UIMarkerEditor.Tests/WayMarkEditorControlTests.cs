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

    [Fact]
    public void SelectionChange_CommitsPendingCoordinateBeforeSwitchingWayMark()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WayMark firstMark = new();
            WayMark secondMark = new();
            secondMark.A.X = 2000;
            WayMarkEditorControl control = CreateControl(
                [firstMark, secondMark],
                UnknownMapIdPolicy.RejectUnknown,
                selectedIndex: 0);
            TextBox textBox = GetCoordinateTextBox(control, "A_X_TextBox");
            ListBox listBox = GetWayMarkListBox(control);

            textBox.Text = "123.456";
            listBox.SelectedIndex = 1;
            control.UpdateLayout();

            Assert.Equal(123456, firstMark.A.X);
            Assert.Same(secondMark, listBox.SelectedItem);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void ImportSnapshotToSelectedWayMark_WhenPendingCoordinateInvalid_DoesNotOverwriteTarget()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WayMark targetMark = new()
            {
                RegionID = MapData.EmptyRegionId
            };
            targetMark.A.X = 111;
            WayMarkEditorControl control = CreateControl(
                [targetMark],
                UnknownMapIdPolicy.RejectUnknown,
                selectedIndex: 0);
            TextBox textBox = GetCoordinateTextBox(control, "A_X_TextBox");
            WayMarkSnapshot snapshot = new()
            {
                RegionID = MapData.EmptyRegionId,
                A = new WayMarkPointSnapshot
                {
                    X = 999
                }
            };

            textBox.Text = "-";

            Assert.False(control.ImportSnapshotToSelectedWayMark(snapshot));
            Assert.Equal(111, targetMark.A.X);
            Assert.Equal("-", textBox.Text);
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

        ListBox listBox = GetWayMarkListBox(control);
        listBox.SelectedIndex = selectedIndex;
        control.UpdateLayout();
        InitializeCoordinateText(control);
        return control;
    }

    private static ListBox GetWayMarkListBox(WayMarkEditorControl control)
    {
        return Assert.IsType<ListBox>(control.FindName("WayMark_ListBox"));
    }

    private static TextBox GetCoordinateTextBox(WayMarkEditorControl control, string name)
    {
        WayMarkEditPanelControl editPanel = Assert.IsType<WayMarkEditPanelControl>(control.FindName("WayMarkEditPanel_Control"));
        return Assert.IsType<TextBox>(editPanel.FindName(name));
    }

    private static void InitializeCoordinateText(WayMarkEditorControl control)
    {
        foreach (string pointName in new[] { "A", "B", "C", "D", "One", "Two", "Three", "Four" })
        {
            foreach (string axisName in new[] { "X", "Y", "Z" })
            {
                GetCoordinateTextBox(control, $"{pointName}_{axisName}_TextBox").Text = "0";
            }
        }
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
