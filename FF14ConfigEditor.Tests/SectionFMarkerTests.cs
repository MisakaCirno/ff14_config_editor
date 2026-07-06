using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

public class SectionFMarkerTests
{
    [Fact]
    public void ToRawBytes_RoundTripsMarkerData()
    {
        byte[] unknown1 = UISaveTestData.SectionUnknown1();
        byte[] unknown2 = UISaveTestData.SectionUnknown2();
        byte[] endFlag = UISaveTestData.SectionEndFlag();
        byte[] markerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        SectionFMARKER section = new(17, unknown1, markerData.Length, unknown2, markerData, endFlag);

        byte[] rawBytes = section.ToRawBytes();

        Assert.Equal(UISaveTestData.BuildSection(17, markerData, unknown1, unknown2, endFlag), rawBytes);
    }

    [Fact]
    public void ParseMarker_ReparseSameData_DoesNotDuplicateWayMarks()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(2, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(markerData);

        section.ParseMarker();

        Assert.Equal(2, section.WayMarks.Count);
        Assert.Equal(UISaveTestData.BuildSection(17, markerData), section.ToRawBytes());
    }

    [Fact]
    public void ParseMarker_ReparseReplacesPreviousTail()
    {
        byte[] firstMarkerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(firstMarkerData);
        Assert.Equal(4, section.MarkerTailLength);

        byte[] secondMarkerData = UISaveTestData.BuildMarkerData(1, [0xDE, 0xAD, 0xBE, 0xEF]);
        section.data = secondMarkerData;
        section.length = secondMarkerData.Length;
        section.ParseMarker();

        Assert.Equal(4, section.MarkerTailLength);
        Assert.Single(section.WayMarks);
        Assert.Equal(UISaveTestData.BuildSection(17, secondMarkerData), section.ToRawBytes());
    }

    [Fact]
    public void ParseMarker_WhenReparseFails_DoesNotReplaceExistingMarkerState()
    {
        byte[] firstMarkerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(firstMarkerData);
        section.data = UISaveTestData.BuildMarkerData(1);
        section.length = section.data.Length;

        Assert.Throws<UISaveFormatException>(section.ParseMarker);

        Assert.Equal(4, section.MarkerTailLength);
        Assert.Single(section.WayMarks);
        Assert.Equal(UISaveTestData.BuildSection(17, firstMarkerData), section.ToRawBytes());
    }

    [Fact]
    public void Constructor_DataShorterThanMinimumMarkerData_ThrowsFormatException()
    {
        int minimumLength = SectionFMARKER.MarkerHeaderByteLength + SectionFMARKER.MarkerTailByteLength;
        byte[] markerData = new byte[minimumLength - 1];

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(
            () => UISaveTestData.BuildFMarkerSection(markerData));

        Assert.Equal(17, ex.SectionIndex);
        Assert.Equal(minimumLength, ex.ExpectedLength);
        Assert.Equal(markerData.Length, ex.RemainingLength);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "FMARKER 数据");
    }

    [Fact]
    public void Constructor_DataWithoutMarkerTail_ThrowsFormatException()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1);

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(
            () => UISaveTestData.BuildFMarkerSection(markerData));

        Assert.Equal(17, ex.SectionIndex);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "FMARKER 数据");
    }

    [Fact]
    public void Constructor_MarkerTailLengthIsNotFour_ThrowsFormatException()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, [0xEE]);

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(
            () => UISaveTestData.BuildFMarkerSection(markerData));

        Assert.Equal(17, ex.SectionIndex);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "FMARKER 数据");
    }

    [Fact]
    public void Constructor_NonZeroMarkerTail_RoundTripsMarkerData()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, [0x01, 0x02, 0x03, 0x04]);
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(markerData);

        byte[] rawBytes = section.ToRawBytes();

        Assert.Equal(4, section.MarkerTailLength);
        Assert.Equal(UISaveTestData.BuildSection(17, markerData), rawBytes);
    }

    [Fact]
    public void Constructor_FiveWayMarks_RoundTripsMarkerData()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(5, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(markerData);

        byte[] rawBytes = section.ToRawBytes();

        Assert.Equal(5, section.WayMarks.Count);
        Assert.Equal(UISaveTestData.BuildSection(17, markerData), rawBytes);
    }

    [Fact]
    public void Constructor_ThirtyWayMarks_RoundTripsMarkerData()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(30, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(markerData);

        byte[] rawBytes = section.ToRawBytes();

        Assert.Equal(30, section.WayMarks.Count);
        Assert.Equal(UISaveTestData.BuildSection(17, markerData), rawBytes);
    }

    [Fact]
    public void Constructor_MoreThanThirtyWayMarks_RoundTripsMarkerData()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(31, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(markerData);

        byte[] rawBytes = section.ToRawBytes();

        Assert.Equal(31, section.WayMarks.Count);
        Assert.Equal(UISaveTestData.BuildSection(17, markerData), rawBytes);
    }

    [Fact]
    public void ToRawBytes_MoreThanThirtyWayMarks_Succeeds()
    {
        byte[] oneMarkerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(oneMarkerData);
        while (section.WayMarks.Count < 31)
        {
            section.WayMarks.Add(new WayMark());
        }

        byte[] rawBytes = section.ToRawBytes();

        Assert.Equal(31, section.WayMarks.Count);
        // ToRawBytes 是纯读取，不应提交 length/data：length 仍为构造时的 1-mark 数据长度。
        Assert.Equal(oneMarkerData.Length, section.length);

        // 期望：header + 首个标点（来自 oneMarkerData）+ 30 个全零标点 + 尾部，套上 section 信封。
        byte[] headerAndFirstMark = oneMarkerData
            .Take(SectionFMARKER.MarkerHeaderByteLength + SectionFMARKER.WayMarkByteLength)
            .ToArray();
        byte[] expectedMarkerData = headerAndFirstMark
            .Concat(new byte[30 * SectionFMARKER.WayMarkByteLength])
            .Concat(UISaveTestData.MarkerTail())
            .ToArray();
        Assert.Equal(UISaveTestData.BuildSection(17, expectedMarkerData), rawBytes);
    }

    [Fact]
    public void ParseMarker_KnownRawBytes_PreservesWayMarkSemantics()
    {
        // 用交错 enableFlag=0x55 和连续递增坐标验证解析后语义字段对应正确，而不只是字节 round-trip。
        // 坐标递增可暴露点顺序/偏移错误；交错位可暴露 enableFlag 位映射错误。这类"读写对称但语义错"的 bug
        // 会让普通 round-trip 测试通过，但本测试会失败。
        byte enableFlag = 0x55;   // 0b01010101：位 0/2/4/6 = A/C/One/Three 启用
        ushort regionId = 1234;
        int timestamp = 1700000000;
        byte unknown = 0x12;
        byte[] markerData = UISaveTestData.BuildMarkerDataWithKnownWayMark(enableFlag, regionId, timestamp, unknown);
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(markerData);

        Assert.Single(section.WayMarks);
        WayMark mark = section.WayMarks[0];

        // 8 个点坐标连续递增 1..24，点顺序或偏移错误都会暴露。
        Assert.Equal(1, mark.A.X);
        Assert.Equal(2, mark.A.Y);
        Assert.Equal(3, mark.A.Z);
        Assert.Equal(4, mark.B.X);
        Assert.Equal(5, mark.B.Y);
        Assert.Equal(6, mark.B.Z);
        Assert.Equal(7, mark.C.X);
        Assert.Equal(8, mark.C.Y);
        Assert.Equal(9, mark.C.Z);
        Assert.Equal(10, mark.D.X);
        Assert.Equal(11, mark.D.Y);
        Assert.Equal(12, mark.D.Z);
        Assert.Equal(13, mark.One.X);
        Assert.Equal(14, mark.One.Y);
        Assert.Equal(15, mark.One.Z);
        Assert.Equal(16, mark.Two.X);
        Assert.Equal(17, mark.Two.Y);
        Assert.Equal(18, mark.Two.Z);
        Assert.Equal(19, mark.Three.X);
        Assert.Equal(20, mark.Three.Y);
        Assert.Equal(21, mark.Three.Z);
        Assert.Equal(22, mark.Four.X);
        Assert.Equal(23, mark.Four.Y);
        Assert.Equal(24, mark.Four.Z);

        // enableFlag=0x55：A/C/One/Three 启用，B/D/Two/Four 禁用。
        Assert.True(mark.AEnabled);
        Assert.False(mark.BEnabled);
        Assert.True(mark.CEnabled);
        Assert.False(mark.DEnabled);
        Assert.True(mark.OneEnabled);
        Assert.False(mark.TwoEnabled);
        Assert.True(mark.ThreeEnabled);
        Assert.False(mark.FourEnabled);

        Assert.Equal(enableFlag, mark.enableFlag);
        Assert.Equal(unknown, mark.unknown);
        Assert.Equal(regionId, mark.RegionID);
        Assert.Equal(timestamp, mark.timestamp);
    }
}
