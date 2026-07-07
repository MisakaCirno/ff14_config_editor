using System.Buffers.Binary;

namespace FF14LogParser;

public static class FF14LogFileParser
{
    private const int HeaderLength = 8;
    private const int EntryHeaderLength = 8;
    private const int MaxEntryLength = 4 * 1024 * 1024;
    private const int MaxOffsetTableLength = 4 * 1024 * 1024;
    private const int FileBufferSize = 4096;

    public static IReadOnlyList<FF14LogEntry> ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return EnumerateFile(path).ToArray();
    }

    public static IEnumerable<FF14LogEntry> EnumerateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return EnumerateFileRecords(path).Select(static record => record.Entry);
    }

    public static IEnumerable<FF14LogRecord> EnumerateFileRecords(string path, bool newestFirst = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return EnumerateFileRecordIterator(path, newestFirst);
    }

    public static IReadOnlyList<FF14LogEntry> ReadLast(string path, int count)
        => ReadLastRecords(path, count).Select(static record => record.Entry).ToArray();

    public static IReadOnlyList<FF14LogRecord> ReadLastRecords(string path, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return Array.Empty<FF14LogRecord>();
        }

        using var stream = OpenReadOnly(path, FileOptions.RandomAccess);
        var index = ReadFileIndex(stream, path);
        var startIndex = Math.Max(0, index.EntryCount - count);
        var entries = new List<FF14LogRecord>(index.EntryCount - startIndex);
        for (var i = startIndex; i < index.EntryCount; i++)
        {
            entries.Add(ReadEntry(stream, index, i));
        }

        return entries;
    }

    public static IReadOnlyList<FF14LogEntry> Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return Parse(bytes.AsSpan());
    }

    public static IReadOnlyList<FF14LogEntry> Parse(ReadOnlySpan<byte> bytes)
    {
        var index = ReadFileIndex(bytes, filePath: null);
        var entries = new List<FF14LogEntry>(index.EntryCount);

        for (var i = 0; i < index.EntryCount; i++)
        {
            var (start, stop) = GetEntryRange(index, i);
            var entryLengthLong = stop - start;
            ValidateEntryLength(entryLengthLong, start, i, index.FilePath);
            var entryLength = checked((int)entryLengthLong);
            entries.Add(ParseEntry(bytes.Slice(checked((int)start), entryLength), start, i, index.FilePath));
        }

        return entries;
    }

    private static IEnumerable<FF14LogRecord> EnumerateFileRecordIterator(string path, bool newestFirst)
    {
        using var stream = OpenReadOnly(path, newestFirst ? FileOptions.RandomAccess : FileOptions.SequentialScan);
        var index = ReadFileIndex(stream, path);

        var startIndex = newestFirst ? index.EntryCount - 1 : 0;
        var stopBefore = newestFirst ? -1 : index.EntryCount;
        var step = newestFirst ? -1 : 1;
        for (var i = startIndex; i != stopBefore; i += step)
        {
            yield return ReadEntry(stream, index, i);
        }
    }

    private static FileStream OpenReadOnly(string path, FileOptions options)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileBufferSize,
            options);

    private static FF14LogFileIndex ReadFileIndex(FileStream stream, string filePath)
    {
        var fileLength = stream.Length;
        var header = ReadExactly(stream, HeaderLength, 0, filePath, entryIndex: null);
        var shape = ReadFileShape(header, fileLength, filePath);
        var offsetTable = ReadExactly(
            stream,
            checked((int)shape.OffsetTableLength),
            HeaderLength,
            filePath,
            entryIndex: null);
        var offsets = ReadOffsetTable(offsetTable, shape.EntryCount, shape.BodyBase, fileLength, filePath);

        return new FF14LogFileIndex(shape.EntryCount, shape.BodyBase, offsets, fileLength, filePath);
    }

    private static FF14LogFileIndex ReadFileIndex(ReadOnlySpan<byte> bytes, string? filePath)
    {
        EnsureAvailable(bytes, 0, HeaderLength, null, filePath, "日志文件头不足 8 字节。");
        var fileLength = bytes.Length;
        var shape = ReadFileShape(bytes[..HeaderLength], fileLength, filePath);
        var offsetTable = bytes.Slice(HeaderLength, checked((int)shape.OffsetTableLength));
        var offsets = ReadOffsetTable(offsetTable, shape.EntryCount, shape.BodyBase, fileLength, filePath);

        return new FF14LogFileIndex(shape.EntryCount, shape.BodyBase, offsets, fileLength, filePath);
    }

    private static FF14LogFileShape ReadFileShape(
        ReadOnlySpan<byte> header,
        long fileLength,
        string? filePath)
    {
        var begin = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        if (end < begin)
        {
            throw new FF14LogParseException(
                $"日志文件头非法：end({end}) 小于 begin({begin})。",
                offset: 4,
                expectedLength: null,
                remainingLength: checked((int)Math.Min(Math.Max(0, fileLength - 4), int.MaxValue)),
                filePath: filePath);
        }

        var entryCountLong = (long)end - begin;
        if (entryCountLong > int.MaxValue)
        {
            throw new FF14LogParseException(
                $"日志条目数量过大：{entryCountLong}。",
                offset: 0,
                expectedLength: null,
                remainingLength: checked((int)Math.Min(fileLength, int.MaxValue)),
                filePath: filePath);
        }

        var entryCount = (int)entryCountLong;
        var offsetTableLength = (long)entryCount * sizeof(uint);
        if (offsetTableLength > MaxOffsetTableLength)
        {
            throw new FF14LogParseException(
                $"日志 offset table 过大：{offsetTableLength} 字节，超过 offset table 上限 {MaxOffsetTableLength} 字节（4 MiB）。",
                offset: HeaderLength,
                expectedLength: MaxOffsetTableLength,
                remainingLength: checked((int)Math.Min(Math.Max(0, fileLength - HeaderLength), int.MaxValue)),
                filePath: filePath);
        }

        var bodyBaseLong = HeaderLength + offsetTableLength;
        if (bodyBaseLong > fileLength)
        {
            throw new FF14LogParseException(
                "日志 offset table 超出文件长度。",
                offset: HeaderLength,
                expectedLength: checked((int)Math.Min(offsetTableLength, int.MaxValue)),
                remainingLength: checked((int)Math.Min(Math.Max(0, fileLength - HeaderLength), int.MaxValue)),
                filePath: filePath);
        }

        return new FF14LogFileShape(entryCount, bodyBaseLong, offsetTableLength);
    }

    private static uint[] ReadOffsetTable(
        ReadOnlySpan<byte> offsetTable,
        int entryCount,
        long bodyBase,
        long fileLength,
        string? filePath)
    {
        var offsets = new uint[entryCount + 1];
        for (var i = 0; i < entryCount; i++)
        {
            var offsetPosition = HeaderLength + (i * sizeof(uint));
            var currentOffset = BinaryPrimitives.ReadUInt32LittleEndian(offsetTable.Slice(i * sizeof(uint), sizeof(uint)));
            if (currentOffset < offsets[i])
            {
                throw new FF14LogParseException(
                    $"日志 offset table 非法：第 {i} 项小于上一项。",
                    offset: offsetPosition,
                    expectedLength: null,
                    remainingLength: checked((int)Math.Min(Math.Max(0, fileLength - offsetPosition), int.MaxValue)),
                    filePath: filePath);
            }

            var absoluteStop = (long)bodyBase + currentOffset;
            if (absoluteStop > fileLength)
            {
                throw new FF14LogParseException(
                    $"日志 offset table 非法：第 {i} 项指向文件末尾之外。",
                    offset: offsetPosition,
                    expectedLength: null,
                    remainingLength: checked((int)Math.Min(Math.Max(0, fileLength - bodyBase), int.MaxValue)),
                    filePath: filePath);
            }

            offsets[i + 1] = currentOffset;
        }

        return offsets;
    }

    private static FF14LogRecord ReadEntry(FileStream stream, FF14LogFileIndex index, int entryIndex)
    {
        var (start, stop) = GetEntryRange(index, entryIndex);
        var entryLengthLong = stop - start;
        ValidateEntryLength(entryLengthLong, start, entryIndex, index.FilePath);
        var entryLength = checked((int)entryLengthLong);
        var entry = ReadExactly(stream, entryLength, start, index.FilePath!, entryIndex);
        return new FF14LogRecord(
            index.FilePath!,
            entryIndex,
            ParseEntry(entry, start, entryIndex, index.FilePath));
    }

    private static (long Start, long Stop) GetEntryRange(FF14LogFileIndex index, int entryIndex)
        => (
            index.BodyBase + index.Offsets[entryIndex],
            index.BodyBase + index.Offsets[entryIndex + 1]);

    private static void ValidateEntryLength(long entryLength, long offset, int entryIndex, string? filePath)
    {
        if (entryLength < EntryHeaderLength)
        {
            throw new FF14LogParseException(
                $"第 {entryIndex} 条日志长度不足 8 字节。",
                offset: offset,
                expectedLength: EntryHeaderLength,
                remainingLength: checked((int)Math.Max(0, entryLength)),
                entryIndex: entryIndex,
                filePath: filePath);
        }

        if (entryLength > MaxEntryLength)
        {
            throw new FF14LogParseException(
                $"第 {entryIndex} 条日志长度过大：{entryLength} 字节，超过单条日志 entry 上限 {MaxEntryLength} 字节（4 MiB）。",
                offset: offset,
                expectedLength: MaxEntryLength,
                remainingLength: checked((int)Math.Min(entryLength, int.MaxValue)),
                entryIndex: entryIndex,
                filePath: filePath);
        }
    }

    private static FF14LogEntry ParseEntry(ReadOnlySpan<byte> entry, long absoluteOffset, int entryIndex, string? filePath)
    {
        try
        {
            var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(entry[..4]);
            var meta = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4, 4));
            var text = FF14SeString.ExtractPlainText(entry[EntryHeaderLength..], absoluteOffset + EntryHeaderLength);
            SplitSenderAndBody(text, out var sender, out var body);

            return new FF14LogEntry(timestamp, meta, sender, body);
        }
        catch (FF14LogParseException ex)
        {
            throw ex.WithContext(entryIndex: entryIndex, filePath: filePath);
        }
    }

    private static void SplitSenderAndBody(string text, out string sender, out string body)
    {
        if (string.IsNullOrEmpty(text))
        {
            sender = string.Empty;
            body = string.Empty;
            return;
        }

        var delimiter = text[0];
        var secondDelimiter = text.IndexOf(delimiter, 1);
        if (secondDelimiter < 0)
        {
            sender = string.Empty;
            body = CleanText(text);
            return;
        }

        sender = CleanText(text[1..secondDelimiter]);
        body = CleanText(text[(secondDelimiter + 1)..]);
    }

    private static string CleanText(string value)
        => value
            .Replace("\u001F", string.Empty, StringComparison.Ordinal)
            .Replace("\uFFFC", string.Empty, StringComparison.Ordinal);

    private static void EnsureAvailable(
        ReadOnlySpan<byte> bytes,
        int offset,
        int expectedLength,
        int? entryIndex,
        string? filePath,
        string message)
    {
        if (offset < 0 || expectedLength < 0 || offset > bytes.Length || bytes.Length - offset < expectedLength)
        {
            throw new FF14LogParseException(
                message,
                offset,
                expectedLength,
                Math.Max(0, bytes.Length - offset),
                entryIndex,
                filePath);
        }
    }

    private static byte[] ReadExactly(
        FileStream stream,
        int length,
        long offset,
        string filePath,
        int? entryIndex)
    {
        var buffer = new byte[length];
        stream.Seek(offset, SeekOrigin.Begin);

        var totalRead = 0;
        while (totalRead < length)
        {
            var read = stream.Read(buffer, totalRead, length - totalRead);
            if (read == 0)
            {
                throw new FF14LogParseException(
                    "日志文件读取中途到达末尾。",
                    offset + totalRead,
                    expectedLength: length - totalRead,
                    remainingLength: 0,
                    entryIndex: entryIndex,
                    filePath: filePath);
            }

            totalRead += read;
        }

        return buffer;
    }

    private sealed record FF14LogFileIndex(
        int EntryCount,
        long BodyBase,
        uint[] Offsets,
        long FileLength,
        string? FilePath);

    private sealed record FF14LogFileShape(int EntryCount, long BodyBase, long OffsetTableLength);
}
