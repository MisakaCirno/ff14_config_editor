namespace FF14LogParser;

public sealed record FF14LogSearchError(string FilePath, string Message, Exception Exception);
