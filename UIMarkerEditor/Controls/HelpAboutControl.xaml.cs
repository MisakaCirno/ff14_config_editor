using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace UIMarkerEditor.Controls;

public partial class HelpAboutControl : UserControl
{
    private const string DieMoe = "https://act.diemoe.net";
    private const string Souma = "https://souma.diemoe.net";
    private const string LucideLicense = "https://lucide.dev/license";
    private const string FfxivMaterialUsageLicense = "https://support.na.square-enix.com/rule.php?id=5382&tag=authc";

    private int aboutGridRowIndex;

    public HelpAboutControl()
    {
        InitializeComponent();
        BuildAboutContent();

        UsageInstructions_TextBlock.Text =
            "本工具的原理为直接读取玩家的配置文件，并对其进行修改、保存。" + Environment.NewLine +
            "对游戏客户端无任何注入、修改操作。当然，通过技术手段得知文件读取的算法，其实也算是不那么敏感的【敏感操作】，请您使用前知悉。" + Environment.NewLine +
            Environment.NewLine +
            "存储标点信息的文件名为 UISAVE.DAT，位于游戏安装路径下的：【最终幻想XIV\\game\\My Games\\FINAL FANTASY XIV - A Realm Reborn\\FFXIV_CHRxxxxxxxxxxxxxxxx】文件夹中。" + Environment.NewLine +
            Environment.NewLine +
            "需要注意的时，若你的电脑曾登陆过多个账号，则你的目录下会有多个名字类似于【FFXIV_CHRxxxxxxxxxxxxxxxx】的文件夹。在文件夹名称中，CHR 后面跟着的就是你的角色ID了。" + Environment.NewLine +
            "区分哪个是自己想要的账号的方式是，先登录该账号，随便操作一下然后退出登录。点进每个文件夹看所有 .DAT 文件的最后修改日期。里面文件的修改日期与你退出登录的时间吻合的，就是你刚才登陆的那个账号。" + Environment.NewLine +
            "通过这种方式，即可辨别出每个账号对应的角色ID是什么，从而找出自己想编辑的那个文件。";
    }

    private void BuildAboutContent()
    {
        AboutContent_Grid.Children.Clear();
        AboutContent_Grid.RowDefinitions.Clear();
        aboutGridRowIndex = 0;

        AddSectionTitle("软件信息");
        AddInfoRow("软件名称", "FF14 标点预设编辑工具");
        AddInfoRow("版本号", GetVersionText());
        AddInfoRow("发布日期", "2026年6月19日");
        AddInfoRow("Git 仓库", ExternalLinks.Repository, ExternalLinks.Repository);
        AddInfoRow("Bilibili", ExternalLinks.BilibiliName, ExternalLinks.Bilibili);
        AddInfoRow("QQ交流群", ExternalLinks.QqGroupNumber, ExternalLinks.QqGroup);

        AddSectionTitle("友情链接");
        AddInfoRow("呆萌", DieMoe, DieMoe);
        AddInfoRow("Souma", Souma, Souma);

        AddSectionTitle("第三方使用");
        AddInfoRow("服务器列表页面", ExternalLinks.ServerListPage, ExternalLinks.ServerListPage);
        AddInfoRow("服务器状态 API", ExternalLinks.ServerStatusApi, ExternalLinks.ServerStatusApi);
        AddInfoRow("地图数据版本", ExternalLinks.MapDataVersion, ExternalLinks.MapDataVersion);
        AddInfoRow("地图数据内容", ExternalLinks.MapDataInstance, ExternalLinks.MapDataInstance);
        AddInfoRow("界面图标", "Lucide Icons（ISC License，部分源自 Feather / MIT License）", LucideLicense);
        AddInfoRow("图片素材", "FINAL FANTASY XIV 游戏内素材，© SQUARE ENIX CO., LTD. All Rights Reserved.", FfxivMaterialUsageLicense);
        AddTextLine("FINAL FANTASY XIV 及相关素材版权、商标归 Square Enix Holdings Co., Ltd. / Square Enix Co., Ltd. 所有。本工具为非官方工具，与 Square Enix 无从属或授权关系。");

        AddSectionTitle("参考项目");
        AddInfoRow("解析算法", ExternalLinks.UisaveReader, ExternalLinks.UisaveReader);
        AddInfoRow("解析算法", ExternalLinks.FfxivUisaveParser, ExternalLinks.FfxivUisaveParser);
        AddInfoRow("文件结构", ExternalLinks.FfxivClientStructsUiSavePackModule, ExternalLinks.FfxivClientStructsUiSavePackModule);
        AddInfoRow("文件结构", ExternalLinks.HaselDebugUiModuleTab, ExternalLinks.HaselDebugUiModuleTab);
        AddInfoRow("地图数据", ExternalLinks.FfxivDataminingCnContentFinderCondition, ExternalLinks.FfxivDataminingCnContentFinderCondition);
    }

    private void AddSectionTitle(string title)
    {
        TextBlock titleTextBlock = new()
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, aboutGridRowIndex == 0 ? 0 : 12, 0, 6)
        };

        AddGridElement(titleTextBlock, 0, 2);
    }

    private void AddInfoRow(string label, string text, string? url = null)
    {
        AddRowDefinition();
        int rowIndex = aboutGridRowIndex++;

        TextBlock labelTextBlock = CreateLabelTextBlock(label);
        Grid.SetRow(labelTextBlock, rowIndex);
        Grid.SetColumn(labelTextBlock, 0);
        AboutContent_Grid.Children.Add(labelTextBlock);

        TextBlock valueTextBlock = CreateValueTextBlock();
        if (string.IsNullOrWhiteSpace(url))
        {
            valueTextBlock.Text = text;
        }
        else
        {
            valueTextBlock.Inlines.Add(CreateHyperlink(text, url));
        }

        Grid.SetRow(valueTextBlock, rowIndex);
        Grid.SetColumn(valueTextBlock, 1);
        AboutContent_Grid.Children.Add(valueTextBlock);
    }

    private void AddTextLine(string text)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            Style = (Style)FindResource("AboutValueTextBlockStyle")
        };

        AddGridElement(textBlock, 0, 2);
    }

    private void AddGridElement(UIElement element, int column, int columnSpan)
    {
        AddRowDefinition();
        Grid.SetRow(element, aboutGridRowIndex++);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
        AboutContent_Grid.Children.Add(element);
    }

    private void AddRowDefinition()
    {
        AboutContent_Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }

    private TextBlock CreateLabelTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            Style = (Style)FindResource("AboutLabelTextBlockStyle")
        };
    }

    private TextBlock CreateValueTextBlock()
    {
        return new TextBlock
        {
            Style = (Style)FindResource("AboutValueTextBlockStyle")
        };
    }

    private Hyperlink CreateHyperlink(string text, string url)
    {
        Hyperlink hyperlink = new(new Run(text))
        {
            NavigateUri = new Uri(url, UriKind.Absolute)
        };
        hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
        return hyperlink;
    }

    private static string GetVersionText()
    {
        Assembly assembly = typeof(HelpAboutControl).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "未知";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), $"无法打开链接：{ex.Message}", "打开链接失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        e.Handled = true;
    }
}
