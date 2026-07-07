using System.Windows;
using System.Windows.Controls;
using FF14ConfigEditor.UISave;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class WayMarkEditPanelControlTests
{
    [Fact]
    public void CommitPendingEdits_WhenCoordinateTextIsValid_UpdatesWayMark()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WayMark wayMark = new();
            WayMarkEditPanelControl control = CreateControl(wayMark);
            TextBox textBox = GetCoordinateTextBox(control, "A_X_TextBox");
            int changedCount = 0;
            control.WayMarkChanged += (_, _) => changedCount++;

            textBox.Text = "123.456";

            Assert.True(control.CommitPendingEdits());
            Assert.Equal(123456, wayMark.A.X);
            Assert.True(changedCount > 0);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void CommitPendingEdits_WhenCoordinateTextIsIncomplete_ReturnsFalseWithoutUpdatingWayMark()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WayMark wayMark = new();
            WayMarkEditPanelControl control = CreateControl(wayMark);
            TextBox textBox = GetCoordinateTextBox(control, "A_X_TextBox");

            textBox.Text = "-";

            Assert.False(control.CommitPendingEdits());
            Assert.Equal(0, wayMark.A.X);
            Assert.Equal("-", textBox.Text);
        });

        Assert.Null(exception);
    }

    private static WayMarkEditPanelControl CreateControl(WayMark wayMark)
    {
        WpfTestHost.EnsureApplicationResources();
        WayMarkEditPanelControl control = new();
        control.SetWayMark(wayMark);
        control.Measure(new Size(800, 600));
        control.Arrange(new Rect(0, 0, 800, 600));
        control.UpdateLayout();
        InitializeCoordinateText(control);
        return control;
    }

    private static void InitializeCoordinateText(WayMarkEditPanelControl control)
    {
        foreach (string pointName in new[] { "A", "B", "C", "D", "One", "Two", "Three", "Four" })
        {
            foreach (string axisName in new[] { "X", "Y", "Z" })
            {
                GetCoordinateTextBox(control, $"{pointName}_{axisName}_TextBox").Text = "0";
            }
        }
    }

    private static TextBox GetCoordinateTextBox(WayMarkEditPanelControl control, string name)
    {
        return Assert.IsType<TextBox>(control.FindName(name));
    }
}
