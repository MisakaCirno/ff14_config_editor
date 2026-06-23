using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class MarkerShapePosCalculatorTests
{
    [Fact]
    public void Diamond_ReturnsRotatedSquarePointsFromNorthClockwise()
    {
        GamePosition center = new(100, 5, 200);

        List<GamePosition> positions = MarkerShapePosCalculator.Diamond(center, 20);

        Assert.Collection(
            positions,
            point => AssertPosition(point, 100, 5, 180),
            point => AssertPosition(point, 110, 5, 190),
            point => AssertPosition(point, 120, 5, 200),
            point => AssertPosition(point, 110, 5, 210),
            point => AssertPosition(point, 100, 5, 220),
            point => AssertPosition(point, 90, 5, 210),
            point => AssertPosition(point, 80, 5, 200),
            point => AssertPosition(point, 90, 5, 190));
    }

    [Fact]
    public void Shapes_WithZeroDistance_ReturnCenterPoints()
    {
        GamePosition center = new(100, 5, 200);

        AssertAllAtCenter(MarkerShapePosCalculator.Circle(center, 0), center);
        AssertAllAtCenter(MarkerShapePosCalculator.Square(center, 0), center);
        AssertAllAtCenter(MarkerShapePosCalculator.Diamond(center, 0), center);
    }

    [Fact]
    public void Shapes_WithNegativeDistance_ReturnCenterPoints()
    {
        GamePosition center = new(100, 5, 200);

        AssertAllAtCenter(MarkerShapePosCalculator.Circle(center, -20), center);
        AssertAllAtCenter(MarkerShapePosCalculator.Square(center, -20), center);
        AssertAllAtCenter(MarkerShapePosCalculator.Diamond(center, -20), center);
    }

    private static void AssertAllAtCenter(IEnumerable<GamePosition> positions, GamePosition center)
    {
        List<GamePosition> positionList = positions.ToList();
        Assert.Equal(8, positionList.Count);

        foreach (GamePosition position in positionList)
        {
            AssertPosition(position, center.X, center.Y, center.Z);
        }
    }

    private static void AssertPosition(GamePosition actual, double expectedX, double expectedY, double expectedZ)
    {
        Assert.Equal(expectedX, actual.X, precision: 6);
        Assert.Equal(expectedY, actual.Y, precision: 6);
        Assert.Equal(expectedZ, actual.Z, precision: 6);
    }
}