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
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "文件格式版本", "文件");
    }

    [Fact]
    public void Load_EncryptedLengthIsTruncated_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithPartialEncryptedLength([0x01, 0x02]));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(8, ex.Offset);
        Assert.Equal(4, ex.ExpectedLength);
        Assert.Equal(2, ex.RemainingLength);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "加密数据长度", "文件");
    }

    [Fact]
    public void Load_FileUnknownHeaderIsTruncated_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithPartialFileUnknown(0, [0xAA, 0xBB]));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(12, ex.Offset);
        Assert.Equal(4, ex.ExpectedLength);
        Assert.Equal(2, ex.RemainingLength);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "文件未知头部", "文件");
    }

    [Fact]
    public void Load_NegativeEncryptedLength_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithDeclaredEncryptedLength(-1));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(8, ex.Offset);
        Assert.Equal(0, ex.ExpectedLength);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "加密数据长度", "文件");
    }

    [Fact]
    public void Load_EncryptedLengthExceedsRemaining_ThrowsFormatException()
    {
        string path = WriteFile(UISaveTestData.BuildFileWithDeclaredEncryptedLength(10, [0x01, 0x02, 0x03]));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(16, ex.Offset);
        Assert.Equal(10, ex.ExpectedLength);
        Assert.Equal(3, ex.RemainingLength);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "加密数据", "文件");
    }

    [Fact]
    public void Load_NegativeSectionLength_ThrowsFormatException()
    {
        byte[] section = UISaveTestData.BuildSectionPrefixWithLength(1, -1);
        string path = WritePayloadFile(UISaveTestData.BuildPayload(section));

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(() => new ConfigUISave(path));

        Assert.Equal(1, ex.SectionIndex);
        Assert.Equal(0, ex.ExpectedLength);
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "段长度", "解密数据");
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
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "段数据和结束标记", "解密数据");
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
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "段数据和结束标记", "解密数据");
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

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "段长度");
    }

    [Fact]
    public void Save_NullSection_ThrowsAndLeavesFileUnchanged()
    {
        string path = WritePayloadFile(UISaveTestData.BuildPayload(
            UISaveTestData.BuildSection(1, [0xAA, 0xBB])));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        config.Sections.Add(null!);

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(config.Save);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "段列表第 1 项");
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
    public void Save_WhenLaterSectionFails_DoesNotCommitFMarkerRawState()
    {
        byte[] markerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        byte[] fMarkerSection = UISaveTestData.BuildSection(17, markerData);
        byte[] laterSection = UISaveTestData.BuildSection(1, [0xAA, 0xBB]);
        string path = WritePayloadFile(UISaveTestData.BuildPayload([fMarkerSection, laterSection]));
        byte[] originalFileBytes = File.ReadAllBytes(path);
        ConfigUISave config = new(path);
        SectionFMARKER section = Assert.IsType<SectionFMARKER>(config.Sections[0]);
        int originalFMarkerLength = section.length;
        byte[] originalFMarkerData = section.data.ToArray();
        section.WayMarks.Add(new WayMark());
        config.Sections[1].length++;

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(config.Save);

        Assert.Equal(originalFMarkerLength, section.length);
        Assert.Equal(originalFMarkerData, section.data);
        Assert.Equal(originalFileBytes, File.ReadAllBytes(path));
        UISaveFormatExceptionAssert.HasDiagnostic(ex, "段长度");
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
