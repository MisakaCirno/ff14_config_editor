using System.Globalization;
using System.Text.Json;
using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

public class MarkerShareConverterTests
{
    private static readonly IReadOnlySet<ushort> KnownMapIds = new HashSet<ushort> { 123 };

    [Fact]
    public void TryCreateValidatedImport_ValidShare_ReturnsRawCoordinates()
    {
        MarkerShare share = CreateValidShare();
        share.Name = "导入时不信任这个名字";

        bool result = MarkerShareConverter.TryCreateValidatedImport(
            share,
            KnownMapIds,
            out ValidatedMarkerShare importedMarker,
            out string errorMessage);

        Assert.True(result);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal((ushort)123, importedMarker.RegionID);
        Assert.Equal(1234, importedMarker.A.RawX);
        Assert.Equal(-2345, importedMarker.A.RawY);
        Assert.Equal(0, importedMarker.A.RawZ);
        Assert.True(importedMarker.A.Active);
    }

    [Fact]
    public void TryCreateValidatedImport_AllPointsInactive_Succeeds()
    {
        MarkerShare share = CreateValidShare();
        foreach (MarkerSharePoint point in GetAllPoints(share))
        {
            point.Active = false;
        }

        bool result = MarkerShareConverter.TryCreateValidatedImport(
            share,
            KnownMapIds,
            out ValidatedMarkerShare importedMarker,
            out string errorMessage);

        Assert.True(result);
        Assert.Equal(string.Empty, errorMessage);
        Assert.False(importedMarker.A.Active);
        Assert.False(importedMarker.Four.Active);
    }

    [Fact]
    public void TryCreateValidatedImport_MissingMapId_ReturnsError()
    {
        MarkerShare share = CreateValidShare(null);

        string errorMessage = ValidateError(share);

        Assert.Contains("缺少地图 ID", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_MapIdOutOfRange_ReturnsError()
    {
        MarkerShare share = CreateValidShare(ushort.MaxValue + 1);

        string errorMessage = ValidateError(share);

        Assert.Contains("超出可保存范围", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_MapIdZero_ReturnsError()
    {
        MarkerShare share = CreateValidShare(0);

        string errorMessage = ValidateError(share);

        Assert.Contains("不能为 0", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_UnknownMapId_ReturnsError()
    {
        MarkerShare share = CreateValidShare(456);

        string errorMessage = ValidateError(share);

        Assert.Contains("不存在于当前地图数据", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_MapDataNotLoaded_ReturnsError()
    {
        MarkerShare share = CreateValidShare();

        string errorMessage = ValidateError(share, new HashSet<ushort>());

        Assert.Contains("地图数据未加载", errorMessage);
        Assert.Contains("允许未知地图 ID", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_AllowUnknownMapId_AllowsUnknownMapId()
    {
        MarkerShare share = CreateValidShare(456);

        bool result = MarkerShareConverter.TryCreateValidatedImport(
            share,
            new HashSet<ushort>(),
            allowUnknownMapId: true,
            preservedUnknownMapIds: null,
            out ValidatedMarkerShare importedMarker,
            out string errorMessage);

        Assert.True(result);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal((ushort)456, importedMarker.RegionID);
    }

    [Fact]
    public void TryCreateValidatedImport_AllowUnknownMapId_AllowsZeroMapId()
    {
        MarkerShare share = CreateValidShare(0);

        bool result = MarkerShareConverter.TryCreateValidatedImport(
            share,
            new HashSet<ushort>(),
            allowUnknownMapId: true,
            preservedUnknownMapIds: null,
            out ValidatedMarkerShare importedMarker,
            out string errorMessage);

        Assert.True(result);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal((ushort)0, importedMarker.RegionID);
    }

    [Fact]
    public void TryCreateValidatedImport_RejectUnknownMapId_AllowsPreservedUnknownMapId()
    {
        MarkerShare share = CreateValidShare(456);

        bool result = MarkerShareConverter.TryCreateValidatedImport(
            share,
            new HashSet<ushort> { 123 },
            allowUnknownMapId: false,
            preservedUnknownMapIds: new HashSet<ushort> { 456 },
            out ValidatedMarkerShare importedMarker,
            out string errorMessage);

        Assert.True(result);
        Assert.Equal(string.Empty, errorMessage);
        Assert.Equal((ushort)456, importedMarker.RegionID);
    }

    [Fact]
    public void TryCreateValidatedImport_MissingPoint_ReturnsError()
    {
        MarkerShare share = CreateValidShare();
        share.A = null;

        string errorMessage = ValidateError(share);

        Assert.Contains("缺少 A 点数据", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_MissingCoordinate_ReturnsError()
    {
        MarkerShare share = CreateValidShare();
        share.A!.X = null;

        string errorMessage = ValidateError(share);

        Assert.Contains("缺少 A 点 X 坐标", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_MissingActive_ReturnsError()
    {
        MarkerShare share = CreateValidShare();
        share.A!.Active = null;

        string errorMessage = ValidateError(share);

        Assert.Contains("缺少 A 点启用状态", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_NonFiniteCoordinate_ReturnsError()
    {
        MarkerShare share = CreateValidShare();
        share.A!.X = double.NaN;

        string errorMessage = ValidateError(share);

        Assert.Contains("不是有效数字", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_RawCoordinateOutOfRange_ReturnsError()
    {
        MarkerShare share = CreateValidShare();
        share.A!.X = 2147483.648;

        string errorMessage = ValidateError(share);

        Assert.Contains("超出可保存范围", errorMessage);
    }

    [Fact]
    public void TryCreateValidatedImport_CoordinateWithMoreThanThreeDecimals_ReturnsError()
    {
        MarkerShare share = CreateValidShare();
        share.A!.X = 1.2345;

        string errorMessage = ValidateError(share);

        Assert.Contains("最多支持 3 位小数", errorMessage);
    }

    [Fact]
    public void CreateShare_SerializedJsonUsesInvariantNumbersUnderDifferentCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            WayMark wayMark = new()
            {
                RegionID = 123,
                A = new WayMarkPoint { X = 1234, Y = -2345, Z = 0 }
            };
            wayMark.AEnabled = true;

            MarkerShare share = MarkerShareConverter.CreateShare(wayMark, _ => "测试地图");
            string json = JsonSerializer.Serialize(share);

            Assert.Equal("测试地图", share.Name);
            Assert.Contains("\"MapID\":123", json);
            Assert.Contains("\"X\":1.234", json);
            Assert.Contains("\"Y\":-2.345", json);
            Assert.DoesNotContain("1,234", json);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    [Fact]
    public void CreateShare_NullWayMark_ThrowsArgumentNullException()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => MarkerShareConverter.CreateShare(null!));

        Assert.Equal("wayMark", ex.ParamName);
    }

    [Fact]
    public void CreateShare_NullFirstPoint_ThrowsArgumentNullException()
    {
        WayMark wayMark = new() { RegionID = 123 };
        wayMark.A = null!;

        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => MarkerShareConverter.CreateShare(wayMark));

        Assert.Equal(nameof(wayMark.A), ex.ParamName);
    }

    [Fact]
    public void CreateShare_NullNumberedPoint_ThrowsArgumentNullException()
    {
        // A-D 均非 null（默认值），应通过前四项校验，在 One 处抛出。
        WayMark wayMark = new() { RegionID = 123 };
        wayMark.One = null!;

        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => MarkerShareConverter.CreateShare(wayMark));

        Assert.Equal(nameof(wayMark.One), ex.ParamName);
    }

    private static string ValidateError(MarkerShare share, IReadOnlySet<ushort>? knownMapIds = null)
    {
        bool result = MarkerShareConverter.TryCreateValidatedImport(
            share,
            knownMapIds ?? KnownMapIds,
            out _,
            out string errorMessage);

        Assert.False(result);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
        return errorMessage;
    }

    private static MarkerShare CreateValidShare(int? mapID = 123)
    {
        return new MarkerShare
        {
            Name = "测试地图",
            MapID = mapID,
            A = CreatePoint(),
            B = CreatePoint(),
            C = CreatePoint(),
            D = CreatePoint(),
            One = CreatePoint(),
            Two = CreatePoint(),
            Three = CreatePoint(),
            Four = CreatePoint()
        };
    }

    private static MarkerSharePoint CreatePoint()
    {
        return new MarkerSharePoint
        {
            X = 1.234,
            Y = -2.345,
            Z = 0,
            Active = true
        };
    }

    private static IEnumerable<MarkerSharePoint> GetAllPoints(MarkerShare share)
    {
        return
        [
            share.A!,
            share.B!,
            share.C!,
            share.D!,
            share.One!,
            share.Two!,
            share.Three!,
            share.Four!
        ];
    }
}
