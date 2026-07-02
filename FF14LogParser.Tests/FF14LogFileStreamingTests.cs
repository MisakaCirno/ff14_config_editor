using System.Buffers.Binary;
using System.Text;

namespace FF14LogParser.Tests;

public sealed class FF14LogFileStreamingTests
{
    private const int EntryLengthLimit = 4 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [Fact]
    public void EnumerateFile_ReadsEntriesFromFile()
    {
        var path = CreateTempLogFile(Entry("A"), Entry("B"));
        try
        {
            var entries = FF14LogFileParser.EnumerateFile(path)
                .Select(static entry => entry.Body)
                .ToArray();

            Assert.Equal(["A", "B"], entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnumerateFileRecords_ReturnsSourceMetadata()
    {
        var path = CreateTempLogFile(Entry("A"), Entry("B"));
        try
        {
            var records = FF14LogFileParser.EnumerateFileRecords(path).ToArray();

            Assert.Equal(path, records[0].FilePath);
            Assert.Equal(0, records[0].EntryIndex);
            Assert.Equal(1, records[1].EntryIndex);
            Assert.Equal("B", records[1].Body);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnumerateFileRecords_CanReadNewestFirst()
    {
        var path = CreateTempLogFile(Entry("A"), Entry("B"));
        try
        {
            var records = FF14LogFileParser.EnumerateFileRecords(path, newestFirst: true).ToArray();

            Assert.Equal(["B", "A"], records.Select(static record => record.Body).ToArray());
            Assert.Equal([1, 0], records.Select(static record => record.EntryIndex).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLast_ReturnsTailEntriesInFileOrder()
    {
        var path = CreateTempLogFile(Entry("A"), Entry("B"), Entry("C"));
        try
        {
            var entries = FF14LogFileParser.ReadLast(path, 2)
                .Select(static entry => entry.Body)
                .ToArray();

            Assert.Equal(["B", "C"], entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLast_ReturnsAllEntriesWhenCountExceedsFileEntryCount()
    {
        var path = CreateTempLogFile(Entry("A"), Entry("B"));
        try
        {
            var entries = FF14LogFileParser.ReadLast(path, 10)
                .Select(static entry => entry.Body)
                .ToArray();

            Assert.Equal(["A", "B"], entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLast_ReturnsEmptyListForZeroCount()
    {
        var path = CreateTempLogFile(Entry("A"));
        try
        {
            Assert.Empty(FF14LogFileParser.ReadLast(path, 0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLast_RejectsNegativeCount()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => FF14LogFileParser.ReadLast("x.log", -1));

        Assert.Equal("count", exception.ParamName);
    }

    [Fact]
    public void ReadLast_RejectsOversizedEntryBeforeReadingBody()
    {
        const int bodyBase = 12;
        const int oversizedEntryLength = EntryLengthLimit + 1;
        var path = CreateSparseLogFileWithSingleEntryLength(oversizedEntryLength);
        try
        {
            var exception = Assert.Throws<FF14LogParseException>(() => FF14LogFileParser.ReadLast(path, 1));

            Assert.Equal(0, exception.EntryIndex);
            Assert.Equal(bodyBase, exception.Offset);
            Assert.Equal(EntryLengthLimit, exception.ExpectedLength);
            Assert.Equal(oversizedEntryLength, exception.RemainingLength);
            Assert.Contains("长度过大", exception.Message);
            Assert.Contains("4 MiB", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static LogSource Entry(string body)
        => new(1_700_000_000, 0x2Au, Utf8.GetBytes($"\u001FTester\u001F{body}"));

    private static string CreateTempLogFile(params LogSource[] entries)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FF14LogParserTests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.log");
        File.WriteAllBytes(path, BuildLogFile(entries));
        return path;
    }

    private static string CreateSparseLogFileWithSingleEntryLength(int entryLength)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FF14LogParserTests");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.log");

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        Span<byte> headerAndOffset = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(headerAndOffset[..4], 100);
        BinaryPrimitives.WriteUInt32LittleEndian(headerAndOffset.Slice(4, 4), 101);
        BinaryPrimitives.WriteUInt32LittleEndian(headerAndOffset.Slice(8, 4), checked((uint)entryLength));
        stream.Write(headerAndOffset);
        stream.SetLength(12L + entryLength);

        return path;
    }

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

    private sealed record LogSource(uint Timestamp, uint Meta, byte[] Payload);
}
