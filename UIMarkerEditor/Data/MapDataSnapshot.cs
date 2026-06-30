namespace UIMarkerEditor;

internal sealed record MapDataSnapshot(
    string Version,
    string SourcePath,
    IReadOnlyDictionary<ushort, string> MapNames);
