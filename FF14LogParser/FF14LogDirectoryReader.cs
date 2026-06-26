namespace FF14LogParser;

public static class FF14LogDirectoryReader
{
    public static IEnumerable<FF14LogEntry> EnumerateDirectory(
        string directoryPath,
        FF14LogDirectoryReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ValidateOptions(options);

        return EnumerateDirectoryRecords(directoryPath, options).Select(static record => record.Entry);
    }

    public static IEnumerable<FF14LogRecord> EnumerateDirectoryRecords(
        string directoryPath,
        FF14LogDirectoryReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ValidateOptions(options);

        return EnumerateDirectoryRecordIterator(directoryPath, options ?? new FF14LogDirectoryReadOptions());
    }

    public static IReadOnlyList<FF14LogEntry> ReadLast(string directoryPath, int count)
        => ReadLastRecords(directoryPath, count).Select(static record => record.Entry).ToArray();

    public static IReadOnlyList<FF14LogRecord> ReadLastRecords(string directoryPath, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return Array.Empty<FF14LogRecord>();
        }

        var remaining = count;
        var chunks = new Stack<IReadOnlyList<FF14LogRecord>>();
        foreach (var filePath in EnumerateLogFiles(directoryPath, newestFirst: true))
        {
            var entries = FF14LogFileParser.ReadLastRecords(filePath, remaining);
            if (entries.Count == 0)
            {
                continue;
            }

            chunks.Push(entries);
            remaining -= entries.Count;
            if (remaining == 0)
            {
                break;
            }
        }

        var result = new List<FF14LogRecord>(count - remaining);
        foreach (var chunk in chunks)
        {
            result.AddRange(chunk);
        }

        return result;
    }

    private static IEnumerable<FF14LogRecord> EnumerateDirectoryRecordIterator(
        string directoryPath,
        FF14LogDirectoryReadOptions options)
    {
        var yielded = 0;
        foreach (var filePath in EnumerateLogFiles(directoryPath, options.NewestFirst))
        {
            foreach (var entry in FF14LogFileParser.EnumerateFileRecords(filePath, options.NewestFirst))
            {
                yield return entry;
                yielded++;
                if (options.MaxEntries is not null && yielded >= options.MaxEntries.Value)
                {
                    yield break;
                }
            }
        }
    }

    internal static IEnumerable<string> EnumerateLogFiles(string directoryPath, bool newestFirst)
    {
        var files = Directory
            .EnumerateFiles(directoryPath, "*.log", SearchOption.TopDirectoryOnly)
            .Select(static path => new FileInfo(path));

        return newestFirst
            ? files
                .OrderByDescending(static file => file.LastWriteTimeUtc)
                .ThenByDescending(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static file => file.FullName)
            : files
                .OrderBy(static file => file.LastWriteTimeUtc)
                .ThenBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static file => file.FullName);
    }

    private static void ValidateOptions(FF14LogDirectoryReadOptions? options)
    {
        if (options?.MaxEntries < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxEntries,
                "最大读取条目数不能小于 0。");
        }
    }
}
