using System.Windows;
using System.Windows.Controls;

namespace UIMarkerEditor.Controls;

public partial class HelpAboutControl
{
    private void BuildUsageContent()
    {
        UsageInstructions_StackPanel.Children.Clear();

        AddUsageSection(
            "工具原理：",
            "本工具直接读写玩家的标点配置文件 UISAVE.DAT，以达到修改标点的效果。",
            "对游戏客户端无任何注入、修改操作。当然，通过技术手段得知文件读取的算法，其实也算是不那么敏感的【敏感操作】，请您使用前知悉。");

        AddUsageSection(
            "注意事项：",
            "由于是通过非官方的方式读写配置文件，有几率（尤其是在游戏更新后）会【损坏配置文件】。",
            "尽管本工具目前已经集成了自动备份系统，但仍然建议各位提前【手动备份自己的配置文件】，以防意外情况发生。");

        AddUsageSection(
            "标点信息位置：",
            "存储标点信息的文件名为【UISAVE.DAT】，位于游戏安装路径下的：",
            "【最终幻想XIV\\game\\My Games\\FINAL FANTASY XIV - A Realm Reborn\\FFXIV_CHRxxxxxxxxxxxxxxxx】文件夹中。");

        AddUsageSection(
            "如何找到自己角色对应的存档：",
            "需要注意的时，若你的电脑曾登陆过多个账号，则你的目录下会有多个名字类似于【FFXIV_CHRxxxxxxxxxxxxxxxx】的文件夹。",
            "在文件夹名称中，CHR 后面跟着的就是你的角色ID了。",
            "区分哪个是自己想要的账号的方式是，先登录该账号，随便操作一下然后退出登录。点进每个文件夹看所有 .DAT 文件的最后修改日期。里面文件的修改日期与你退出登录的时间吻合的，就是你刚才登陆的那个账号。",
            "通过这种方式，即可辨别出每个账号对应的角色ID是什么，从而找出自己想编辑的那个文件。");
    }

    private void AddUsageSection(string title, params string[] bodyLines)
    {
        Border sectionBorder = new()
        {
            Style = (Style)FindResource("UsageSectionContainerStyle")
        };

        Grid sectionGrid = new();
        sectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        sectionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border accentBorder = new()
        {
            Style = (Style)FindResource("UsageSectionAccentStyle")
        };
        Grid.SetColumn(accentBorder, 0);
        sectionGrid.Children.Add(accentBorder);

        StackPanel contentStackPanel = new()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(contentStackPanel, 2);
        sectionGrid.Children.Add(contentStackPanel);

        TextBlock titleTextBlock = new()
        {
            Text = title,
            Style = (Style)FindResource("UsageSectionTitleStyle")
        };
        contentStackPanel.Children.Add(titleTextBlock);

        if (bodyLines.Length > 0)
        {
            TextBlock bodyTextBlock = new()
            {
                Text = string.Join(Environment.NewLine, bodyLines),
                Style = (Style)FindResource("UsageSectionBodyStyle")
            };
            contentStackPanel.Children.Add(bodyTextBlock);
        }

        sectionBorder.Child = sectionGrid;
        UsageInstructions_StackPanel.Children.Add(sectionBorder);
    }
}