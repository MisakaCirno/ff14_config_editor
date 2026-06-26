using System.Text.RegularExpressions;

namespace FF14LogParser;

public static class FF14LogSearcher
{
    public static IReadOnlyList<FF14LogSearchMatch> SearchDirectory(
        string directoryPath,
        FF14LogSearchOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<FF14LogSearchProgress>? progress = null,
        ICollection<FF14LogSearchError>? errors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var normalizedOptions = ValidateOptions(options ?? new FF14LogSearchOptions());
        return SearchDirectoryIterator(directoryPath, normalizedOptions, cancellationToken, progress, errors).ToArray();
    }

    public static FF14LogSearchMatch? FindFirst(
        string directoryPath,
        FF14LogSearchOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<FF14LogSearchProgress>? progress = null,
        ICollection<FF14LogSearchError>? errors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var normalizedOptions = ValidateOptions((options ?? new FF14LogSearchOptions()) with { MaxResults = 1 });
        return SearchDirectoryIterator(directoryPath, normalizedOptions, cancellationToken, progress, errors).FirstOrDefault();
    }

    public static IEnumerable<FF14LogSearchMatch> EnumerateDirectory(
        string directoryPath,
        FF14LogSearchOptions? options = null,
        CancellationToken cancellationToken = default,
        IProgress<FF14LogSearchProgress>? progress = null,
        ICollection<FF14LogSearchError>? errors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var normalizedOptions = ValidateOptions(options ?? new FF14LogSearchOptions());
        return SearchDirectoryIterator(directoryPath, normalizedOptions, cancellationToken, progress, errors);
    }

    private static IEnumerable<FF14LogSearchMatch> SearchDirectoryIterator(
        string directoryPath,
        FF14LogSearchOptions options,
        CancellationToken cancellationToken,
        IProgress<FF14LogSearchProgress>? progress,
        ICollection<FF14LogSearchError>? errors)
    {
        var matcher = CreateMatcher(options);
        var newestFirst = options.Direction == FF14LogSearchDirection.NewestFirst;
        var scannedFiles = 0;
        var scannedEntries = 0;
        var matchedEntries = 0;
        if (options.MaxResults == 0)
        {
            progress?.Report(new FF14LogSearchProgress(scannedFiles, scannedEntries, matchedEntries, null));
            yield break;
        }

        foreach (var filePath in FF14LogDirectoryReader.EnumerateLogFiles(directoryPath, newestFirst))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedFiles++;
            progress?.Report(new FF14LogSearchProgress(scannedFiles, scannedEntries, matchedEntries, filePath));

            IEnumerator<FF14LogRecord>? enumerator = null;
            try
            {
                enumerator = FF14LogFileParser.EnumerateFileRecords(filePath, newestFirst).GetEnumerator();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    FF14LogRecord record;
                    try
                    {
                        if (!enumerator.MoveNext())
                        {
                            break;
                        }

                        record = enumerator.Current;
                    }
                    catch (Exception ex) when (options.ContinueOnError)
                    {
                        errors?.Add(new FF14LogSearchError(filePath, ex.Message, ex));
                        break;
                    }

                    scannedEntries++;
                    if (scannedEntries % options.ProgressInterval == 0)
                    {
                        progress?.Report(new FF14LogSearchProgress(scannedFiles, scannedEntries, matchedEntries, filePath));
                    }

                    if (!PassesMetadataFilters(record, options))
                    {
                        continue;
                    }

                    var matchedFields = GetMatchedFields(record, options, matcher);
                    if (matchedFields == FF14LogSearchFields.None && !string.IsNullOrEmpty(options.Query))
                    {
                        continue;
                    }

                    matchedEntries++;
                    yield return new FF14LogSearchMatch(record, matchedFields);

                    if (options.MaxResults is not null && matchedEntries >= options.MaxResults.Value)
                    {
                        progress?.Report(new FF14LogSearchProgress(scannedFiles, scannedEntries, matchedEntries, filePath));
                        yield break;
                    }
                }
            }
            finally
            {
                enumerator?.Dispose();
            }
        }

        progress?.Report(new FF14LogSearchProgress(scannedFiles, scannedEntries, matchedEntries, null));
    }

    private static FF14LogSearchOptions ValidateOptions(FF14LogSearchOptions options)
    {
        if (options.Fields == FF14LogSearchFields.None && !string.IsNullOrEmpty(options.Query))
        {
            throw new ArgumentException("搜索字段不能为空。", nameof(options));
        }

        if (options.MaxResults < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxResults, "最大结果数不能小于 0。");
        }

        if (options.ProgressInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ProgressInterval, "进度间隔必须大于 0。");
        }

        if (options.StartTime > options.EndTime)
        {
            throw new ArgumentException("开始时间不能晚于结束时间。", nameof(options));
        }

        return options;
    }

    private static Func<string, bool> CreateMatcher(FF14LogSearchOptions options)
    {
        if (string.IsNullOrEmpty(options.Query))
        {
            return static _ => true;
        }

        if (options.UseRegex)
        {
            var regexOptions = RegexOptions.CultureInvariant;
            if (!options.CaseSensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            var regex = new Regex(options.Query, regexOptions, TimeSpan.FromSeconds(2));
            return value => regex.IsMatch(value);
        }

        var comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return value => value.Contains(options.Query, comparison);
    }

    private static bool PassesMetadataFilters(FF14LogRecord record, FF14LogSearchOptions options)
    {
        if (options.Kind is not null && record.Kind != options.Kind.Value)
        {
            return false;
        }

        if (options.StartTime is not null && record.Timestamp < options.StartTime.Value)
        {
            return false;
        }

        if (options.EndTime is not null && record.Timestamp > options.EndTime.Value)
        {
            return false;
        }

        return true;
    }

    private static FF14LogSearchFields GetMatchedFields(
        FF14LogRecord record,
        FF14LogSearchOptions options,
        Func<string, bool> matcher)
    {
        if (string.IsNullOrEmpty(options.Query))
        {
            return options.Fields;
        }

        var matchedFields = FF14LogSearchFields.None;
        if (options.Fields.HasFlag(FF14LogSearchFields.Sender) && matcher(record.Sender))
        {
            matchedFields |= FF14LogSearchFields.Sender;
        }

        if (options.Fields.HasFlag(FF14LogSearchFields.Body) && matcher(record.Body))
        {
            matchedFields |= FF14LogSearchFields.Body;
        }

        return matchedFields;
    }
}
