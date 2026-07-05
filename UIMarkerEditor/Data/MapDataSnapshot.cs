namespace UIMarkerEditor;

internal sealed record MapDataSnapshot(
    string Version,
    string SourcePath,
    string SourceFingerprint,
    IReadOnlyDictionary<ushort, string> MapNames);

internal sealed record MapDataSnapshotIdentity(
    string Version,
    string SourcePath,
    string SourceFingerprint);
