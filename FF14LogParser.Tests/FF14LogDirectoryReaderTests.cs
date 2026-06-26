using System.Buffers.Binary;
using System.Text;

namespace FF14LogParser.Tests;

public sealed class FF14LogDirectoryReaderTests
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [Fact]
    public void EnumerateDirectory_ReadsLogFilesByNameAndHonorsLimit()
    {
        var directory = CreateTempDirectory();
        try
        {
            var newer = WriteLogFile(directory, "00000002.log", Entry("C"));
            var older = WriteLogFile(directory, "00000001.log", Entry("A"), Entry("B"));
            SetLastWriteTimes(older, newer);
            File.WriteAllText(Path.Combine(directory, "ignored.txt"), "not a log");

            var entries = FF14LogDirectoryReader
                .EnumerateDirectory(directory, new FF14LogDirectoryReadOptions { MaxEntries = 2 })
                .Select(static entry => entry.Body)
                .ToArray();

            Assert.Equal(["A", "B"], entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadLast_ReadsAcrossFilesAndReturnsChronologicalTail()
    {
        var directory = CreateTempDirectory();
        try
        {
            var older = WriteLogFile(directory, "00000001.log", Entry("A"), Entry("B"));
            var newer = WriteLogFile(directory, "00000002.log", Entry("C"), Entry("D"), Entry("E"));
            SetLastWriteTimes(older, newer);

            var entries = FF14LogDirectoryReader.ReadLast(directory, 4)
                .Select(static entry => entry.Body)
                .ToArray();

            Assert.Equal(["B", "C", "D", "E"], entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadLast_StopsAfterNewestFilesHaveEnoughEntries()
    {
        var directory = CreateTempDirectory();
        try
        {
            var older = Path.Combine(directory, "00000001.log");
            File.WriteAllBytes(older, [0x01]);
            var newer = WriteLogFile(directory, "00000002.log", Entry("A"), Entry("B"));
            SetLastWriteTimes(older, newer);

            var entry = Assert.Single(FF14LogDirectoryReader.ReadLast(directory, 1));

            Assert.Equal("B", entry.Body);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EnumerateDirectory_RejectsNegativeLimit()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => FF14LogDirectoryReader.EnumerateDirectory(
                "logs",
                new FF14LogDirectoryReadOptions { MaxEntries = -1 }));

        Assert.Equal("options", exception.ParamName);
    }

    private static LogSource Entry(string body)
        => new(1_700_000_000, 0x2Au, Utf8.GetBytes($"\u001FTester\u001F{body}"));

    private static string WriteLogFile(string directory, string fileName, params LogSource[] entries)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, BuildLogFile(entries));
        return path;
    }

    private static void SetLastWriteTimes(string olderPath, string newerPath)
    {
        var baseTime = new DateTime(2026, 6, 26, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(olderPath, baseTime);
        File.SetLastWriteTimeUtc(newerPath, baseTime.AddMinutes(1));
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

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FF14LogParserTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed record LogSource(uint Timestamp, uint Meta, byte[] Payload);
}
