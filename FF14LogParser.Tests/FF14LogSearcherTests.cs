using System.Buffers.Binary;
using System.Text;

namespace FF14LogParser.Tests;

public sealed class FF14LogSearcherTests
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [Fact]
    public void SearchDirectory_FindsBodyTextAndReturnsSourceMetadata()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = WriteLogFile(directory, "00000001.log", Entry("Alice", "第一条"), Entry("Bob", "目标消息"));

            var match = Assert.Single(FF14LogSearcher.SearchDirectory(
                directory,
                new FF14LogSearchOptions { Query = "目标", Fields = FF14LogSearchFields.Body }));

            Assert.Equal(path, match.FilePath);
            Assert.Equal(1, match.EntryIndex);
            Assert.Equal("Bob", match.Sender);
            Assert.Equal("目标消息", match.Body);
            Assert.Equal(FF14LogSearchFields.Body, match.MatchedFields);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SearchDirectory_EmptyQueryListsAllMessagesWithFilters()
    {
        var directory = CreateTempDirectory();
        try
        {
            WriteLogFile(
                directory,
                "00000001.log",
                Entry("Alice", "A", kind: 10, timestamp: 100),
                Entry("Bob", "B", kind: 11, timestamp: 200),
                Entry("Cecil", "C", kind: 11, timestamp: 300));

            var matches = FF14LogSearcher.SearchDirectory(
                directory,
                new FF14LogSearchOptions
                {
                    Query = string.Empty,
                    Kind = 11,
                    StartTime = DateTimeOffset.FromUnixTimeSeconds(150),
                    EndTime = DateTimeOffset.FromUnixTimeSeconds(250)
                });

            var match = Assert.Single(matches);
            Assert.Equal("B", match.Body);
            Assert.Equal(FF14LogSearchFields.SenderAndBody, match.MatchedFields);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FindFirst_NewestFirstStopsAfterFirstMatch()
    {
        var directory = CreateTempDirectory();
        try
        {
            var older = Path.Combine(directory, "00000001.log");
            File.WriteAllBytes(older, [0x01]);
            var newer = WriteLogFile(directory, "00000002.log", Entry("Alice", "新消息"));
            SetLastWriteTimes(older, newer);

            var errors = new List<FF14LogSearchError>();
            var match = FF14LogSearcher.FindFirst(
                directory,
                new FF14LogSearchOptions
                {
                    Query = "新消息",
                    Direction = FF14LogSearchDirection.NewestFirst,
                    ContinueOnError = false
                },
                errors: errors);

            Assert.NotNull(match);
            Assert.Equal("新消息", match.Body);
            Assert.Empty(errors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SearchDirectory_CollectsErrorsAndContinuesWhenRequested()
    {
        var directory = CreateTempDirectory();
        try
        {
            var older = Path.Combine(directory, "00000001.log");
            File.WriteAllBytes(older, [0x01]);
            var newer = WriteLogFile(directory, "00000002.log", Entry("Alice", "可用消息"));
            SetLastWriteTimes(older, newer);

            var errors = new List<FF14LogSearchError>();
            var match = Assert.Single(FF14LogSearcher.SearchDirectory(
                directory,
                new FF14LogSearchOptions { Query = "可用", ContinueOnError = true },
                errors: errors));

            Assert.Equal("可用消息", match.Body);
            var error = Assert.Single(errors);
            Assert.Equal(older, error.FilePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SearchDirectory_SupportsRegexAndCaseSensitivity()
    {
        var directory = CreateTempDirectory();
        try
        {
            WriteLogFile(directory, "00000001.log", Entry("Alice", "Alpha-42"), Entry("Bob", "alpha-43"));

            var matches = FF14LogSearcher.SearchDirectory(
                directory,
                new FF14LogSearchOptions
                {
                    Query = "^Alpha-\\d+$",
                    UseRegex = true,
                    CaseSensitive = true,
                    Fields = FF14LogSearchFields.Body
                });

            var match = Assert.Single(matches);
            Assert.Equal("Alpha-42", match.Body);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SearchDirectory_ReturnsNoMatchesWhenMaxResultsIsZero()
    {
        var directory = CreateTempDirectory();
        try
        {
            WriteLogFile(directory, "00000001.log", Entry("Alice", "A"));

            var matches = FF14LogSearcher.SearchDirectory(
                directory,
                new FF14LogSearchOptions { Query = string.Empty, MaxResults = 0 });

            Assert.Empty(matches);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SearchDirectory_ReportsProgress()
    {
        var directory = CreateTempDirectory();
        try
        {
            WriteLogFile(directory, "00000001.log", Entry("Alice", "A"), Entry("Bob", "B"));
            var reports = new List<FF14LogSearchProgress>();
            var progress = new TestProgress<FF14LogSearchProgress>(reports.Add);

            _ = FF14LogSearcher.SearchDirectory(
                directory,
                new FF14LogSearchOptions { Query = "B", ProgressInterval = 1 },
                progress: progress);

            Assert.Contains(reports, static report => report.ScannedEntries >= 2 && report.MatchedEntries == 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static LogSource Entry(string sender, string body, int kind = 42, uint timestamp = 1_700_000_000)
        => new(timestamp, (uint)kind, Utf8.GetBytes($"\u001F{sender}\u001F{body}"));

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

    private sealed class TestProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value)
            => report(value);
    }
}
