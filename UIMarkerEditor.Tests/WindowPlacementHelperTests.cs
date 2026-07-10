using System.Windows;

namespace UIMarkerEditor.Tests;

public sealed class WindowPlacementHelperTests
{
    [Fact]
    public void ConstrainToWorkArea_WhenBoundsFit_KeepsBounds()
    {
        Rect bounds = new(120, 80, 900, 600);
        Rect workArea = new(0, 0, 1920, 1040);

        Rect result = WindowPlacementHelper.ConstrainToWorkArea(bounds, workArea, 800, 500);

        Assert.Equal(bounds, result);
    }

    [Fact]
    public void ConstrainToWorkArea_WhenBoundsAreOffScreen_MovesEntireWindowIntoWorkArea()
    {
        Rect bounds = new(-700, -500, 900, 600);
        Rect workArea = new(0, 0, 1280, 680);

        Rect result = WindowPlacementHelper.ConstrainToWorkArea(bounds, workArea, 800, 500);

        Assert.Equal(new Rect(0, 0, 900, 600), result);
    }

    [Fact]
    public void ConstrainToWorkArea_WhenBoundsExceedWorkArea_UsesAvailableSize()
    {
        Rect bounds = new(2000, 1200, 1800, 1200);
        Rect workArea = new(100, 50, 1000, 700);

        Rect result = WindowPlacementHelper.ConstrainToWorkArea(bounds, workArea, 900, 600);

        Assert.Equal(workArea, result);
    }
}
