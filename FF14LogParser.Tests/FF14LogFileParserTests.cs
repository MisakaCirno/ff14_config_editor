using System.Buffers.Binary;
using System.Text;

namespace FF14LogParser.Tests;

public sealed class FF14LogFileParserTests
{
    private const int OffsetTableLengthLimit = 4 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [Fact]
    public void Parse_ReadsHeaderOffsetTableAndEntries()
    {
        var bytes = BuildLogFile(
            Entry(1_700_000_000, 0x8000_002Au, "\u001FAlice\u001F你好，世界"),
            Entry(1_700_000_060, 0x0000_0081u, "\u001F系统通知"));

        var entries = FF14LogFileParser.Parse(bytes);

        Assert.Equal(2, entries.Count);
        Assert.Equal((uint)1_700_000_000, entries[0].TimestampUnixSeconds);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), entries[0].Timestamp);
        Assert.Equal(0x8000_002Au, entries[0].Meta);
        Assert.Equal(42, entries[0].Kind);
        Assert.Equal("Alice", entries[0].Sender);
        Assert.Equal("你好，世界", entries[0].Body);

        Assert.Equal(1, entries[1].Kind);
        Assert.Equal(string.Empty, entries[1].Sender);
        Assert.Equal("系统通知", entries[1].Body);
    }

    [Fact]
    public void Parse_SkipsSeStringTokensBeforeSplittingSenderAndBody()
    {
        var payload = Concat(
            Utf8.GetBytes("\u001F"),
            TokenWithInclusiveLength(0x12, [0x4F]),
            Utf8.GetBytes("Alice\uFFFC"),
            Utf8.GetBytes("\u001FHello "),
            TokenWithExclusiveLength(0x13, [0xFE, 0xFF, 0xF3, 0xF3, 0xF3]),
            Utf8.GetBytes("世界"));

        var bytes = BuildLogFile(Entry(123, 0x2Au, payload));

        var entry = Assert.Single(FF14LogFileParser.Parse(bytes));
        Assert.Equal("Alice", entry.Sender);
        Assert.Equal("Hello 世界", entry.Body);
    }

    [Fact]
    public void Parse_RejectsDecreasingOffsets()
    {
        var bytes = new byte[8 + 8 + 16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 4);

        var exception = Assert.Throws<FF14LogParseException>(() => FF14LogFileParser.Parse(bytes));
        Assert.Equal(12, exception.Offset);
    }

    [Fact]
    public void Parse_RejectsEntryShorterThanHeader()
    {
        var bytes = new byte[8 + 4 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 10);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 11);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 4);

        var exception = Assert.Throws<FF14LogParseException>(() => FF14LogFileParser.Parse(bytes));
        Assert.Equal(0, exception.EntryIndex);
        Assert.Equal(8, exception.ExpectedLength);
        Assert.Equal(4, exception.RemainingLength);
    }

    [Fact]
    public void Parse_RejectsOversizedOffsetTable()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4, 4),
            100u + (uint)((OffsetTableLengthLimit / sizeof(uint)) + 1));

        var exception = Assert.Throws<FF14LogParseException>(() => FF14LogFileParser.Parse(bytes));

        Assert.Equal(8, exception.Offset);
        Assert.Equal(OffsetTableLengthLimit, exception.ExpectedLength);
        Assert.Contains("offset table 过大", exception.Message);
    }

    private static LogSource Entry(uint timestamp, uint meta, string text)
        => Entry(timestamp, meta, Utf8.GetBytes(text));

    private static LogSource Entry(uint timestamp, uint meta, byte[] payload)
        => new(timestamp, meta, payload);

    private static byte[] BuildLogFile(params LogSource[] entries)
    {
        var entryBytes = entries.Select(BuildEntry).ToArray();
        var bodyLength = entryBytes.Sum(static e => e.Length);
        var result = new byte[8 + (entries.Length * 4) + bodyLength];

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)(100 + entries.Length));

        var cumulativeOffset = 0;
        for (var i = 0; i < entryBytes.Length; i++)
        {
            cumulativeOffset += entryBytes[i].Length;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8 + (i * 4), 4), (uint)cumulativeOffset);
        }

        var bodyPosition = 8 + (entries.Length * 4);
        foreach (var entry in entryBytes)
        {
            entry.CopyTo(result.AsSpan(bodyPosition));
            bodyPosition += entry.Length;
        }

        return result;
    }

    private static byte[] BuildEntry(LogSource source)
    {
        var entry = new byte[8 + source.Payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(0, 4), source.Timestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4, 4), source.Meta);
        source.Payload.CopyTo(entry.AsSpan(8));
        return entry;
    }

    private static byte[] TokenWithInclusiveLength(byte tag, byte[] data)
    {
        var token = new byte[3 + data.Length + 1];
        token[0] = 0x02;
        token[1] = tag;
        token[2] = checked((byte)(data.Length + 2));
        data.CopyTo(token.AsSpan(3));
        token[^1] = 0x03;
        return token;
    }

    private static byte[] TokenWithExclusiveLength(byte tag, byte[] data)
    {
        var token = new byte[3 + data.Length + 1];
        token[0] = 0x02;
        token[1] = tag;
        token[2] = checked((byte)(data.Length + 1));
        data.CopyTo(token.AsSpan(3));
        token[^1] = 0x03;
        return token;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(static p => p.Length)];
        var position = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result.AsSpan(position));
            position += part.Length;
        }

        return result;
    }

    private sealed record LogSource(uint Timestamp, uint Meta, byte[] Payload);
}
