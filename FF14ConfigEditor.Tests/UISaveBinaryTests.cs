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
    public void ParseMarker_ReparseClearsPreviousTail()
    {
        byte[] firstMarkerData = UISaveTestData.BuildMarkerData(1, UISaveTestData.MarkerTail());
        SectionFMARKER section = UISaveTestData.BuildFMarkerSection(firstMarkerData);
        Assert.Equal(4, section.MarkerTailLength);

        byte[] secondMarkerData = UISaveTestData.BuildMarkerData(1);
        section.data = secondMarkerData;
        section.length = secondMarkerData.Length;
        section.ParseMarker();

        Assert.Equal(0, section.MarkerTailLength);
        Assert.Single(section.WayMarks);
    }

    [Fact]
    public void Constructor_DataShorterThanMarkerHeader_ThrowsFormatException()
    {
        byte[] markerData = new byte[SectionFMARKER.MarkerHeaderByteLength - 1];

        UISaveFormatException ex = Assert.Throws<UISaveFormatException>(
            () => UISaveTestData.BuildFMarkerSection(markerData));

        Assert.Equal(17, ex.SectionIndex);
        Assert.Equal(SectionFMARKER.MarkerHeaderByteLength, ex.ExpectedLength);
        Assert.Equal(markerData.Length, ex.RemainingLength);
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
