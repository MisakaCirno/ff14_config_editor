namespace UIMarkerEditor.Controls;

public sealed record RecentWayMarkFileItem(
    string FilePath,
    string DisplayText,
    string ToolTip,
    bool Exists)
{
    public static RecentWayMarkFileItem Create(
        string filePath,
        string userID,
        CharacterProfile? profile,
        bool exists)
    {
        string characterName = profile?.CharacterName.Trim() ?? string.Empty;
        string server = string.Join("-", new[]
        {
            profile?.DataCenter.Trim(),
            profile?.World.Trim()
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        string displayText = string.IsNullOrWhiteSpace(characterName)
            ? userID
            : string.Join("  ", new[] { characterName, server, userID }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        List<string> toolTipLines = [];
        if (!string.IsNullOrWhiteSpace(characterName))
        {
            toolTipLines.Add($"角色：{characterName}");
        }

        if (!string.IsNullOrWhiteSpace(server))
        {
            toolTipLines.Add($"服务器：{server}");
        }

        toolTipLines.Add($"角色 ID：{userID}");
        if (!string.IsNullOrWhiteSpace(profile?.Note))
        {
            toolTipLines.Add($"备注：{profile.Note.Trim()}");
        }

        toolTipLines.Add($"文件：{filePath}");
        if (!exists)
        {
            toolTipLines.Add("状态：文件不存在");
        }

        return new RecentWayMarkFileItem(
            filePath,
            displayText,
            string.Join(Environment.NewLine, toolTipLines),
            exists);
    }
}

public sealed class RecentWayMarkFileRequestedEventArgs(string filePath) : EventArgs
{
    public string FilePath { get; } = filePath;
}
