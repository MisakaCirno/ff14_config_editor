namespace FF14LogParser;

public sealed record FF14LogRecord(string FilePath, int EntryIndex, FF14LogEntry Entry)
{
    public uint TimestampUnixSeconds => Entry.TimestampUnixSeconds;

    public DateTimeOffset Timestamp => Entry.Timestamp;

    public uint Meta => Entry.Meta;

    public int Kind => Entry.Kind;

    public string Sender => Entry.Sender;

    public string Body => Entry.Body;
}
