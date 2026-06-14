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
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(
            UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail()));
        while (section.WayMarks.Count < 31)
        {
            section.WayMarks.Add(new WayMark());
        }

        byte[] rawBytes = section.ToRawBytes();

        Assert.Equal(31, section.WayMarks.Count);
        Assert.Equal(
            SectionFMARKER.MarkerHeaderByteLength + SectionFMARKER.WayMarkByteLength * 31 + section.MarkerTailLength,
            section.length);
        Assert.Equal(UISaveTestData.BuildSection(17, section.data), rawBytes);
    }
}
