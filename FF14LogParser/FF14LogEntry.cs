namespace FF14LogParser;

public sealed record FF14LogEntry(
    uint TimestampUnixSeconds,
    uint Meta,
    string Sender,
    string Body)
{
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeSeconds(TimestampUnixSeconds);

    public int Kind => (int)(Meta & 0x7Fu);
}
