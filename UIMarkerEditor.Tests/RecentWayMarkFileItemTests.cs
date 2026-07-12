using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class RecentWayMarkFileItemTests
{
    [Fact]
    public void Create_WithCharacterProfile_PrioritizesCharacterInformation()
    {
        const string userID = "0011223344556677";
        const string filePath = @"C:\Game\FFXIV_CHR0011223344556677\UISAVE.DAT";
        CharacterProfile profile = new()
        {
            UserID = userID,
            CharacterName = "测试角色",
            DataCenter = "陆行鸟",
            World = "红玉海",
            Note = "高难角色"
        };

        RecentWayMarkFileItem item = RecentWayMarkFileItem.Create(filePath, userID, profile, exists: true);

        Assert.Equal("测试角色  陆行鸟-红玉海  0011223344556677", item.DisplayText);
        Assert.Contains("角色：测试角色", item.ToolTip, StringComparison.Ordinal);
        Assert.Contains("服务器：陆行鸟-红玉海", item.ToolTip, StringComparison.Ordinal);
        Assert.Contains("角色 ID：0011223344556677", item.ToolTip, StringComparison.Ordinal);
        Assert.Contains("备注：高难角色", item.ToolTip, StringComparison.Ordinal);
        Assert.Contains($"文件：{filePath}", item.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithoutCharacterProfile_DisplaysOnlyUserID()
    {
        RecentWayMarkFileItem item = RecentWayMarkFileItem.Create(
            @"C:\Game\UISAVE.DAT",
            "0011223344556677",
            profile: null,
            exists: false);

        Assert.Equal("0011223344556677", item.DisplayText);
        Assert.Contains("状态：文件不存在", item.ToolTip, StringComparison.Ordinal);
    }
}
