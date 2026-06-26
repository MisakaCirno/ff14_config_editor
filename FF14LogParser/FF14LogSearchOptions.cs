namespace FF14LogParser;

public sealed record FF14LogSearchOptions
{
    public string Query { get; init; } = string.Empty;

    public FF14LogSearchFields Fields { get; init; } = FF14LogSearchFields.SenderAndBody;

    public FF14LogSearchDirection Direction { get; init; }

    public bool CaseSensitive { get; init; }

    public bool UseRegex { get; init; }

    public int? Kind { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public int? MaxResults { get; init; }

    public bool ContinueOnError { get; init; } = true;

    public int ProgressInterval { get; init; } = 500;
}
