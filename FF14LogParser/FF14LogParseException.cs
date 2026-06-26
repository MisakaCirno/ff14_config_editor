namespace FF14LogParser;

public sealed class FF14LogParseException : FormatException
{
    public FF14LogParseException(
        string message,
        long offset,
        int? expectedLength = null,
        int? remainingLength = null,
        int? entryIndex = null,
        string? filePath = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Offset = offset;
        ExpectedLength = expectedLength;
        RemainingLength = remainingLength;
        EntryIndex = entryIndex;
        FilePath = filePath;
    }

    public long Offset { get; }

    public int? ExpectedLength { get; }

    public int? RemainingLength { get; }

    public int? EntryIndex { get; }

    public string? FilePath { get; }

    internal FF14LogParseException WithEntryIndex(int entryIndex)
        => WithContext(entryIndex: entryIndex);

    internal FF14LogParseException WithContext(int? entryIndex = null, string? filePath = null)
        => new(
            Message,
            Offset,
            ExpectedLength,
            RemainingLength,
            entryIndex ?? EntryIndex,
            filePath ?? FilePath,
            InnerException);
}
