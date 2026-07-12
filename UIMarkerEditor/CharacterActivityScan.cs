namespace UIMarkerEditor;

internal enum CharacterActivityScanState
{
    Available,
    NoLocalRecord,
    ReadFailed
}

internal sealed record CharacterActivityScanItem(
    string UserID,
    string DisplayName);

internal sealed record CharacterActivityScanPreparation(
    string GameCharacterRootDirectory,
    IReadOnlyList<CharacterActivityScanItem> Items);

internal sealed record CharacterActivityScanEntry(
    string UserID,
    DateTime? LastActiveAtUtc,
    CharacterActivityScanState State,
    string ErrorMessage = "");

internal sealed record CharacterActivityScanProgress(
    int CompletedCount,
    int TotalCount,
    string DisplayName)
{
    public double Percent => TotalCount <= 0
        ? 100
        : Math.Clamp(CompletedCount * 100d / TotalCount, 0, 100);
}

internal sealed record CharacterActivityScanResult(
    string GameCharacterRootDirectory,
    IReadOnlyList<CharacterActivityScanEntry> Entries,
    DateTime CompletedAtUtc)
{
    public int ReadFailureCount => Entries.Count(static entry =>
        entry.State == CharacterActivityScanState.ReadFailed);
}
