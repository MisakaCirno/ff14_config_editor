namespace FF14LogParser;

public sealed record FF14LogSearchMatch(FF14LogRecord Record, FF14LogSearchFields MatchedFields)
{
    public string FilePath => Record.FilePath;

    public int EntryIndex => Record.EntryIndex;

    public FF14LogEntry Entry => Record.Entry;

    public DateTimeOffset Timestamp => Record.Timestamp;

    public int Kind => Record.Kind;

    public string Sender => Record.Sender;

    public string Body => Record.Body;
}
