using FF14ConfigEditor.UISave;

namespace FF14ConfigEditor.Tests;

public sealed class ConfigUISaveBinaryTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "FF14ConfigEditor.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_FileFormatVersionIsTruncated_ThrowsFormatException()
    {
        string path = WriteFile([0x01, 0x02, 0x03]);

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(0, ex.Offset);
        Assert.Equal(8, ex.ExpectedLength);
        Assert.Equal(3, ex.RemainingLength);
    }

    [Fact]
    public void Load_EncryptedLengthIsTruncated_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithPartialEncryptedLength([0x01, 0x02]));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(8, ex.Offset);
        Assert.Equal(4, ex.ExpectedLength);
        Assert.Equal(2, ex.RemainingLength);
    }

    [Fact]
    public void Load_FileUnknownHeaderIsTruncated_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithPartialFileUnknown(0, [0xAA, 0xBB]));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(12, ex.Offset);
        Assert.Equal(4, ex.ExpectedLength);
        Assert.Equal(2, ex.RemainingLength);
    }

    [Fact]
    public void Load_NegativeEncryptedLength_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithDeclaredEncryptedLength(-1));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(8, ex.Offset);
        Assert.Equal(0, ex.ExpectedLength);
    }

    [Fact]
    public void Load_EncryptedLengthExceedsRemaining_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithDeclaredEncryptedLength(10, [0x01, 0x02, 0x03]));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(16, ex.Offset);
        Assert.Equal(10, ex.ExpectedLength);
        Assert.Equal(3, ex.RemainingLength);
    }

    [Fact]
    public void Load_NegativeSectionLength_ThrowsFormatException()
    {
        byte[] section = UISaveTestData.BuildSectionPrefixWithLength(1, -1);
        string path = WritePayloadFile(UISaveTestData.BuildPayload(section));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(1, ex.SectionIndex);
        Assert.Equal(0, ex.ExpectedLength);
    }

    [Fact]
    public void Load_SectionLengthExceedsPayload_ThrowsFormatException()
    {
        byte[] section = UISaveTestData.BuildSectionWithDeclaredLength(1, 5, [0xAA, 0xBB, 0xCC]);
        string path = WritePayloadFile(UISaveTestData.BuildPayload(section));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(1, ex.SectionIndex);
        Assert.Equal(9, ex.ExpectedLength);
        Assert.Equal(3, ex.RemainingLength);
    }

    [Fact]
    public void Load_MissingSectionEndFlag_ThrowsFormatException()
    {
        byte[] section = UISaveTestData.BuildSectionWithDeclaredLength(1, 0, []);
        string path = WritePayloadFile(UISaveTestData.BuildPayload(section));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(1, ex.SectionIndex);
        Assert.Equal(4, ex.ExpectedLength);
        Assert.Equal(0, ex.RemainingLength);
    }

    [Fact]
    public void Load_WhenReloadFails_DoesNotReplaceExistingState()
    {
        string path = WriteFile(UISaveTestData.BuildFile(
            UISaveTestData.BuildPayload(UISaveTestData.BuildSection(1, [0xAA, 0xBB])),
            [0xF1, 0xF2, 0xF3]));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        string originalUserId = config.UserIDHex;
        int originalSectionCount = config.Sections.Count;
        byte[] originalSectionData = config.Sections[0].data.ToArray();

        File.WriteAllBytes(path, UISaveTestData.BuildFileWithDeclaredEncryptedLength(-1));

        Assert.Throws<UISaveFormatException>(config.Load);
        Assert.Equal(originalUserId, config.UserIDHex);
        Assert.Equal(originalSectionCount, config.Sections.Count);
        Assert.Equal(originalSectionData, config.Sections[0].data);

        config.Save();
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Load_WhenPayloadParsingFails_DoesNotReplaceExistingState()
    {
        byte[] originalPayload = UISaveTestData.BuildPayloadWithTail(
            [0xEF],
            UISaveTestData.BuildSection(1, [0xAA, 0xBB]),
            UISaveTestData.BuildSection(2, [0xCC, 0xDD]));
        string path = WriteFile(UISaveTestData.BuildFile(originalPayload, [0xF1, 0xF2, 0xF3]));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        ConfigStateSnapshot originalState = ConfigStateSnapshot.Capture(config);

        byte[] brokenPayload = UISaveTestData.BuildPayload(
            [0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10],
            [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE],
            UISaveTestData.BuildSectionWithDeclaredLength(3, 8, [0x11, 0x22]));
        File.WriteAllBytes(path, UISaveTestData.BuildFile(
            brokenPayload,
            [0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x02, 0x00],
            [0x51, 0x52, 0x53, 0x54],
            [0x61, 0x62]));

        Assert.Throws<UISaveFormatException>(config.Load);
        originalState.AssertMatches(config);

        config.Save();
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ParseEncryptedPart_WhenPayloadParsingFails_DoesNotReplacePayloadState()
    {
        byte[] originalPayload = UISaveTestData.BuildPayloadWithTail(
            [0xEF],
            UISaveTestData.BuildSection(1, [0xAA, 0xBB]));
        string path = WriteFile(UISaveTestData.BuildFile(originalPayload, [0xF1, 0xF2]));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        ConfigStateSnapshot originalState = ConfigStateSnapshot.Capture(config);
        byte[] brokenPayload = UISaveTestData.BuildPayload(
            [0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10],
            [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE],
            UISaveTestData.BuildSectionWithDeclaredLength(3, 8, [0x11, 0x22]));

        Assert.Throws<UISaveFormatException>(() => config.ParseEncryptedPart(brokenPayload));
        originalState.AssertMatches(config);

        config.Save();
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_InvalidSectionLength_ThrowsAndLeavesFileUnchanged()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        config.Sections[0].length++;

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_NullSection_ThrowsAndLeavesFileUnchanged()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        config.Sections.Add(null!);

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_NullSectionData_ThrowsAndLeavesFileUnchanged()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        config.Sections[0].data = null!;

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_NullFileTail_ThrowsAndLeavesFileUnchanged()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        UISaveTestData.SetConfigByteArrayField(config, "fileTailRaw", null);

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_NullPayloadTail_ThrowsAndLeavesFileUnchanged()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        UISaveTestData.SetConfigByteArrayField(config, "payloadTailRaw", null);

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_PayloadTail_RoundTripsTailBytes()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayloadWithTail(
            [0xEF],
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);

        config.Save();

        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_UnknownSectionIndex_RoundTripsSectionBytes()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(99, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);

        config.Save();

        Assert.IsType<UISaveSection>(config.Sections[0]);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(0, "LETTER.DAT")]
    [InlineData(1, "RETTASK.DAT")]
    [InlineData(2, "FLAGS.DAT")]
    [InlineData(3, "RCFAV.DAT")]
    [InlineData(4, "UIDATA.DAT")]
    [InlineData(5, "TLPH.DAT")]
    [InlineData(6, "ITCC.DAT")]
    [InlineData(7, "PVPSET.DAT")]
    [InlineData(8, "EMTH.DAT")]
    [InlineData(9, "MNONLST.DAT")]
    [InlineData(10, "MUNTLST.DAT")]
    [InlineData(11, "EMJ.DAT")]
    [InlineData(12, "AOZNOTE.DAT")]
    [InlineData(13, "CWLS.DAT")]
    [InlineData(14, "ARCHVLST.DAT")]
    [InlineData(15, "GRPPOS.DAT")]
    [InlineData(16, "CRAFT.DAT")]
    [InlineData(17, "FMARKER.DAT")]
    [InlineData(18, "MYCNOT.DAT")]
    [InlineData(19, "ORNMLST.DAT")]
    [InlineData(20, "MYCITEM.DAT")]
    [InlineData(21, "GRPSTAMP.DAT")]
    [InlineData(22, "RTNR.DAT")]
    [InlineData(23, "BANNER.DAT")]
    [InlineData(24, "ADVNOTE.DAT")]
    [InlineData(25, "AKTKNOT.DAT")]
    [InlineData(26, "VVDNOTE.DAT")]
    [InlineData(27, "VVDACT.DAT")]
    [InlineData(28, "TOFU.DAT")]
    [InlineData(29, "FISHING.DAT")]
    [InlineData(30, "ACTION.DAT")]
    [InlineData(31, "TFILTER.DAT")]
    [InlineData(32, "READYC.DAT")]
    [InlineData(33, "PTRLST.DAT")]
    [InlineData(34, "CATSBM.DAT")]
    [InlineData(35, "DESCRI.DAT")]
    [InlineData(36, "MJICWSP.DAT")]
    [InlineData(37, "PERFORM.DAT")]
    [InlineData(38, "MKDSJOB.DAT")]
    [InlineData(39, "MKDLORE.DAT")]
    [InlineData(40, "MKDSJN.DAT")]
    [InlineData(41, "QPNL.DAT")]
    [InlineData(42, "GLASSES.DAT")]
    [InlineData(43, "XBMNOTE.DAT")]
    [InlineData(44, "XBM.DAT")]
    public void TryGetSectionName_KnownSectionIndex_ReturnsName(int sectionIndex, string expectedName)
    {
        bool result = ConfigUISave.TryGetSectionName(sectionIndex, out string sectionName);

        Assert.True(result);
        Assert.Equal(expectedName, sectionName);
    }

    [Fact]
    public void TryGetSectionName_UnknownSectionIndex_ReturnsFalse()
    {
        bool result = ConfigUISave.TryGetSectionName(99, out string sectionName);

        Assert.False(result);
        Assert.Equal(string.Empty, sectionName);
    }

    [Fact]
    public void Save_NonZeroSectionEndFlag_RoundTripsEndFlagBytes()
    {
        byte[] endFlag = [0xDE, 0xAD, 0xBE, 0xEF];
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB], UISaveTestData.SectionUnknown1(), UISaveTestData.SectionUnknown2(), endFlag)));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);

        config.Save();

        Assert.Equal(endFlag, config.Sections[0].endFlag);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_InvalidFMarkerTailLength_ThrowsAndLeavesFileUnchanged()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(17, markerData)));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        SectionFMARKER section = Assert.IsType<SectionFMARKER>(config.Sections[0]);
        UISaveTestData.SetMarkerTail(section, [0xAA]);

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_InvalidFMarkerHeaderLength_ThrowsAndLeavesFileUnchanged()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(17, markerData)));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        SectionFMARKER section = Assert.IsType<SectionFMARKER>(config.Sections[0]);
        UISaveTestData.SetMarkerHeader(section, [0xAA]);

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_NullFMarkerWayMark_ThrowsAndLeavesFileUnchanged()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(17, markerData)));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        SectionFMARKER section = Assert.IsType<SectionFMARKER>(config.Sections[0]);
        section.WayMarks.Add(null!);

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_NullFMarkerPoint_ThrowsAndLeavesFileUnchanged()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(17, markerData)));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        SectionFMARKER section = Assert.IsType<SectionFMARKER>(config.Sections[0]);
        section.WayMarks[0].A = null!;

        Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_FMarkerWayMarkChange_RegeneratesSectionLength()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(17, markerData)));
        ConfigUISave config = new(path);
        SectionFMARKER section = Assert.IsType<SectionFMARKER>(config.Sections[0]);
        section.WayMarks.Add(new WayMark());
        section.data = [0xAA];
        section.length = section.data.Length;

        config.Save();

        int expectedMarkerLength = SectionFMARKER.MarkerHeaderByteLength
            + SectionFMARKER.WayMarkByteLength * 2
            + SectionFMARKER.MarkerTailByteLength;
        Assert.Equal(expectedMarkerLength, section.length);
        Assert.Equal(expectedMarkerLength, section.data.Length);

        ConfigUISave reloaded = new(path);
        SectionFMARKER reloadedSection = Assert.IsType<SectionFMARKER>(reloaded.Sections[0]);
        Assert.Equal(2, reloadedSection.WayMarks.Count);
    }

    [Fact]
    public void Save_FileTail_RoundTripsTailBytes()
    {
        string path = WriteFile(UISaveTestData.BuildFile(
            UISaveTestData.BuildPayload(UISaveTestData.BuildSection(1, [0xAA, 0xBB])),
            [0x00, 0x00, 0xAA, 0x55, 0x10]));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);

        config.Save();

        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_FileWithoutTail_DoesNotAppendTailBytes()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);

        config.Save();

        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
    }

    private string WritePayloadFile(byte[] decryptedPayload)
    {
        return WriteFile(UISaveTestData.BuildFile(decryptedPayload));
    }

    private string WriteFile(byte[] contents)
    {
        Directory.CreateDirectory(testDirectory);
        string path = Path.Combine(testDirectory, "UISAVE.DAT");
        File.WriteAllBytes(path, contents);
        return path;
    }
}

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
    }

    [Fact]
    public void Constructor_DataWithoutMarkerTail_ThrowsFormatException()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1);

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(
            () => UISaveTestData.BuildFMarkerSection(markerData));

        Assert.Equal(17, ex.SectionIndex);
    }

    [Fact]
    public void Constructor_MarkerTailLengthIsNotFour_ThrowsFormatException()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, [0xEE]);

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(
            () => UISaveTestData.BuildFMarkerSection(markerData));

        Assert.Equal(17, ex.SectionIndex);
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

internal sealed record ConfigStateSnapshot(
    string UserIDHex,
    string UserIDRawBytesHex,
    byte[] FileFormatVersionRaw,
    byte[] FileUnknownRaw,
    byte[] FileTailRaw,
    byte[] PayloadUnknownRaw,
    byte[] UserIDRaw,
    byte[] PayloadTailRaw,
    SectionStateSnapshot[] Sections)
{
    public static ConfigStateSnapshot Capture(ConfigUISave config)
    {
        return new ConfigStateSnapshot(
            config.UserIDHex,
            config.UserIDRawBytesHex,
            UISaveTestData.GetConfigByteArrayField(config, "fileFormatVersionRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "fileUnknownRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "fileTailRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "payloadUnknownRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "userIDRaw"),
            UISaveTestData.GetConfigByteArrayField(config, "payloadTailRaw"),
            config.Sections.Select(SectionStateSnapshot.Capture).ToArray());
    }

    public void AssertMatches(ConfigUISave config)
    {
        ConfigStateSnapshot actual = Capture(config);
        Assert.Equal(UserIDHex, actual.UserIDHex);
        Assert.Equal(UserIDRawBytesHex, actual.UserIDRawBytesHex);
        Assert.Equal(FileFormatVersionRaw, actual.FileFormatVersionRaw);
        Assert.Equal(FileUnknownRaw, actual.FileUnknownRaw);
        Assert.Equal(FileTailRaw, actual.FileTailRaw);
        Assert.Equal(PayloadUnknownRaw, actual.PayloadUnknownRaw);
        Assert.Equal(UserIDRaw, actual.UserIDRaw);
        Assert.Equal(PayloadTailRaw, actual.PayloadTailRaw);
        Assert.Equal(Sections.Length, actual.Sections.Length);
        for (int i = 0; i < Sections.Length; i++)
        {
            Sections[i].AssertMatches(actual.Sections[i]);
        }
    }
}

internal sealed record SectionStateSnapshot(
    Type SectionType,
    short Index,
    byte[] Unknown1,
    int Length,
    byte[] Unknown2,
    byte[] Data,
    byte[] EndFlag)
{
    public static SectionStateSnapshot Capture(UISaveSection section)
    {
        return new SectionStateSnapshot(
            section.GetType(),
            section.index,
            section.unknown1.ToArray(),
            section.length,
            section.unknown2.ToArray(),
            section.data.ToArray(),
            section.endFlag.ToArray());
    }

    public void AssertMatches(SectionStateSnapshot actual)
    {
        Assert.Equal(SectionType, actual.SectionType);
        Assert.Equal(Index, actual.Index);
        Assert.Equal(Unknown1, actual.Unknown1);
        Assert.Equal(Length, actual.Length);
        Assert.Equal(Unknown2, actual.Unknown2);
        Assert.Equal(Data, actual.Data);
        Assert.Equal(EndFlag, actual.EndFlag);
    }
}

internal static class UISaveTestData
{
    private static readonly byte[] FileFormatVersion = [0x55, 0x49, 0x53, 0x41, 0x56, 0x45, 0x01, 0x00];
    private static readonly byte[] FileUnknown = [0x10, 0x20, 0x30, 0x40];
    private static readonly byte[] PayloadUnknown = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
    private static readonly byte[] UserId = [0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11];

    public static byte[] SectionUnknown1()
    {
        return [0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6];
    }

    public static byte[] SectionUnknown2()
    {
        return [0xB1, 0xB2, 0xB3, 0xB4];
    }

    public static byte[] SectionEndFlag()
    {
        return [0xE1, 0xE2, 0xE3, 0xE4];
    }

    public static byte[] MarkerTail()
    {
        return [0x00, 0x00, 0x00, 0x00];
    }

    public static void SetMarkerTail(SectionFMARKER section, byte[] markerTail)
    {
        SetPrivateField(section, "_markerTail", markerTail);
    }

    public static void SetMarkerHeader(SectionFMARKER section, byte[] markerHeader)
    {
        SetPrivateField(section, "_markerHeader", markerHeader);
    }

    public static void SetConfigByteArrayField(ConfigUISave config, string fieldName, byte[]? value)
    {
        SetPrivateField(config, fieldName, value);
    }

    public static byte[] GetConfigByteArrayField(ConfigUISave config, string fieldName)
    {
        if (GetPrivateField(config, fieldName) is not byte[] value)
        {
            throw new InvalidOperationException($"字段 {fieldName} 不是 byte[]。");
        }

        return value.ToArray();
    }

    private static object? GetPrivateField(object target, string fieldName)
    {
        return GetPrivateFieldInfo(target, fieldName).GetValue(target);
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        GetPrivateFieldInfo(target, fieldName).SetValue(target, value);
    }

    private static System.Reflection.FieldInfo GetPrivateFieldInfo(object target, string fieldName)
    {
        System.Reflection.FieldInfo? field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            throw new InvalidOperationException($"无法找到字段 {fieldName}。");
        }

        return field;
    }

    public static byte[] BuildFile(byte[] decryptedPayload, byte[]? fileTail = null)
    {
        byte[] encryptedPayload = Utils.EncryptData(decryptedPayload);
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(encryptedPayload.Length);
        writer.Write(FileUnknown);
        writer.Write(encryptedPayload);
        if (fileTail is { Length: > 0 })
        {
            writer.Write(fileTail);
        }

        return ms.ToArray();
    }

    public static byte[] BuildFile(
        byte[] decryptedPayload,
        byte[] fileFormatVersion,
        byte[] fileUnknown,
        byte[]? fileTail = null)
    {
        byte[] encryptedPayload = Utils.EncryptData(decryptedPayload);
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(fileFormatVersion);
        writer.Write(encryptedPayload.Length);
        writer.Write(fileUnknown);
        writer.Write(encryptedPayload);
        if (fileTail is { Length: > 0 })
        {
            writer.Write(fileTail);
        }

        return ms.ToArray();
    }

    public static byte[] BuildFileWithPartialEncryptedLength(byte[] partialEncryptedLength)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(partialEncryptedLength);

        return ms.ToArray();
    }

    public static byte[] BuildFileWithPartialFileUnknown(int declaredLength, byte[] partialFileUnknown)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(declaredLength);
        writer.Write(partialFileUnknown);

        return ms.ToArray();
    }

    public static byte[] BuildFileWithDeclaredEncryptedLength(int declaredLength, byte[] encryptedPayload)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(FileFormatVersion);
        writer.Write(declaredLength);
        writer.Write(FileUnknown);
        writer.Write(encryptedPayload);

        return ms.ToArray();
    }

    public static byte[] BuildFileWithDeclaredEncryptedLength(int declaredLength)
    {
        return BuildFileWithDeclaredEncryptedLength(declaredLength, []);
    }

    public static byte[] BuildPayload(params byte[][] sections)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(PayloadUnknown);
        writer.Write(UserId);
        foreach (byte[] section in sections)
        {
            writer.Write(section);
        }

        return ms.ToArray();
    }

    public static byte[] BuildPayload(byte[] payloadUnknown, byte[] userId, params byte[][] sections)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(payloadUnknown);
        writer.Write(userId);
        foreach (byte[] section in sections)
        {
            writer.Write(section);
        }

        return ms.ToArray();
    }

    public static byte[] BuildPayloadWithTail(byte[] payloadTail, params byte[][] sections)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(PayloadUnknown);
        writer.Write(UserId);
        foreach (byte[] section in sections)
        {
            writer.Write(section);
        }
        writer.Write(payloadTail);

        return ms.ToArray();
    }

    public static byte[] BuildSection(short index, byte[] data)
    {
        return BuildSection(index, data, SectionUnknown1(), SectionUnknown2(), SectionEndFlag());
    }

    public static byte[] BuildSection(
        short index,
        byte[] data,
        byte[] unknown1,
        byte[] unknown2,
        byte[] endFlag)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(index);
        writer.Write(unknown1);
        writer.Write(data.Length);
        writer.Write(unknown2);
        writer.Write(data);
        writer.Write(endFlag);

        return ms.ToArray();
    }

    public static byte[] BuildSectionPrefixWithLength(short index, int declaredLength)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(index);
        writer.Write(SectionUnknown1());
        writer.Write(declaredLength);

        return ms.ToArray();
    }

    public static byte[] BuildSectionWithDeclaredLength(short index, int declaredLength, byte[] data)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(index);
        writer.Write(SectionUnknown1());
        writer.Write(declaredLength);
        writer.Write(SectionUnknown2());
        writer.Write(data);

        return ms.ToArray();
    }

    public static byte[] BuildMarkerData(int wayMarkCount, byte[]? tail = null)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(Enumerable.Range(0, SectionFMARKER.MarkerHeaderByteLength).Select(value => (byte)value).ToArray());
        for (int i = 0; i < wayMarkCount; i++)
        {
            writer.Write(BuildWayMarkBytes((ushort)(100 + i), i));
        }

        if (tail is { Length: > 0 })
        {
            writer.Write(tail);
        }

        return ms.ToArray();
    }

    public static SectionFMARKER BuildFMarkerSection(byte[] markerData)
    {
        return new SectionFMARKER(
            17,
            SectionUnknown1(),
            markerData.Length,
            SectionUnknown2(),
            markerData,
            SectionEndFlag());
    }

    private static byte[] BuildWayMarkBytes(ushort regionId, int seed)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        for (int i = 0; i < 24; i++)
        {
            writer.Write(seed * 1000 + i + 1);
        }

        writer.Write((byte)0xFF);
        writer.Write((byte)0x12);
        writer.Write(regionId);
        writer.Write(123456 + seed);

        return ms.ToArray();
    }
}
