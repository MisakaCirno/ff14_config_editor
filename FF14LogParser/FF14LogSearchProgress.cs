namespace FF14LogParser;

public sealed record FF14LogSearchProgress(
    int ScannedFiles,
    int ScannedEntries,
    int MatchedEntries,
    string? CurrentFilePath);
