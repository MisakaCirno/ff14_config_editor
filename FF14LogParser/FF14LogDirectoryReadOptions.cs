namespace FF14LogParser;

public sealed record FF14LogDirectoryReadOptions
{
    public int? MaxEntries { get; init; }

    public bool NewestFirst { get; init; }
}
