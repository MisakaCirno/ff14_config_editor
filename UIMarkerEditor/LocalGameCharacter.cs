namespace UIMarkerEditor;

internal sealed class LocalGameCharacter
{
    public string UserID { get; init; } = string.Empty;
    public string CharacterName { get; init; } = string.Empty;
    public string DataCenter { get; init; } = string.Empty;
    public string World { get; init; } = string.Empty;
    public string CharacterDirectory { get; init; } = string.Empty;
    public string SaveFilePath { get; init; } = string.Empty;
    public DateTime SaveFileLastWriteTime { get; init; }

    public string ServerDisplayName => string.Join(" / ", new[] { DataCenter, World }
        .Where(static part => !string.IsNullOrWhiteSpace(part)));

    public string SaveFileLastWriteTimeDisplay => SaveFileLastWriteTime == DateTime.MinValue
        ? string.Empty
        : SaveFileLastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
}

internal sealed record LocalGameCharacterScanResult(
    int LocalCharacterCount,
    int CreatedProfileCount,
    int ImportedCharacterNameCount,
    int UnchangedProfileCount,
    IReadOnlyList<ClientLogCharacterNameScanError> Errors,
    bool SkippedBecauseGameInstallDirectoryChanged = false)
{
    public bool Changed => !SkippedBecauseGameInstallDirectoryChanged &&
        (CreatedProfileCount > 0 || ImportedCharacterNameCount > 0);
}

internal sealed record LocalGameCharacterScanPreparation(
    string GameCharacterRootDirectory,
    IReadOnlyList<LocalGameCharacterScanItem> Items,
    IReadOnlyList<ClientLogCharacterNameScanError> Errors);

internal sealed record LocalGameCharacterScanItem(
    string UserID,
    string CharacterDirectory,
    string SaveFilePath,
    DateTime SaveFileLastWriteTime,
    string CharacterNameFromLog);
