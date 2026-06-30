using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace UIMarkerEditor.Controls;

public partial class HelpAboutControl
{
    private const string Repository = "https://github.com/MisakaCirno/ff14_config_editor";
    private const string BilibiliName = "@御琪幽然";
    private const string Bilibili = "https://space.bilibili.com/2908365";
    private const string QqGroupNumber = "1075777023";
    private const string QqGroup = "http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=_Y7Glbc9stUFXNyiTKKYuBGBiusMrtY8&authKey=WfD6QlORkZPLuCqHxb0G7HWIxi3jZSJ10Ss4%2FWYvQb3hdp9IyJi8CuZ7R1BU4H%2BV&noverify=0&group_code=1075777023";
    private const string DieMoe = "https://act.diemoe.net";
    private const string Souma = "https://souma.diemoe.net";
    private const string Lumina = "https://github.com/NotAdam/Lumina";
    private const string LucideLicense = "https://lucide.dev/license";
    private const string FfxivMaterialUsageLicense = "https://support.na.square-enix.com/rule.php?id=5382&tag=authc";
    private const string UisaveReader = "https://github.com/PunishedPineapple/UISAVE_Reader";
    private const string FfxivUisaveParser = "https://github.com/Lujiang0111/FFxivUisaveParser";
    private const string FfxivClientStructsUiSavePackModule = "https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/UI/Misc/UiSavePackModule.cs";
    private const string HaselDebugUiModuleTab = "https://github.com/Haselnussbomber/HaselDebug/blob/main/HaselDebug/Tabs/Disabled/UIModuleTab.cs";
    private const string FfxivDataminingCnContentFinderCondition = "https://github.com/thewakingsands/ffxiv-datamining-cn/blob/master/ContentFinderCondition.csv";
    private const string FfxivLogViewer = "https://ffxivlog.orz.tools/";
    private const string FfxivLogParser = "https://github.com/ffxiv-cyou/ffxiv-log-parser";

    private int aboutGridRowIndex;

    private void BuildAboutContent()
    {
        AboutContent_Grid.Children.Clear();
        AboutContent_Grid.RowDefinitions.Clear();
        aboutGridRowIndex = 0;

        AddSectionTitle("软件信息");
        AddInfoRow("软件名称", "FF14 标点预设编辑工具");
        AddInfoRow("版本号", GetVersionText());
        AddInfoRow("发布日期", "2026年6月19日");
        AddInfoRow("Git 仓库", Repository, Repository);
        AddInfoRow("Bilibili", BilibiliName, Bilibili);
        AddInfoRow("QQ交流群", QqGroupNumber, QqGroup);

        AddSectionTitle("友情链接");
        AddInfoRow("呆萌", DieMoe, DieMoe);
        AddInfoRow("Souma", Souma, Souma);

        AddSectionTitle("第三方使用");
        AddInfoRow("服务器列表页面", ExternalLinks.ServerListPage, ExternalLinks.ServerListPage);
        AddInfoRow("服务器状态 API", ExternalLinks.ServerStatusApi, ExternalLinks.ServerStatusApi);
        AddInfoRow("地图数据读取", "本地 FFXIV 客户端 sqpack");
        AddInfoRow("游戏数据读取库", "Lumina", Lumina);
        AddInfoRow("界面图标", "Lucide Icons（ISC License，部分源自 Feather / MIT License）", LucideLicense);
        AddInfoRow("图片素材", "FINAL FANTASY XIV 游戏内素材，© SQUARE ENIX CO., LTD. All Rights Reserved.", FfxivMaterialUsageLicense);
        AddTextLine("FINAL FANTASY XIV 及相关素材版权、商标归 Square Enix Holdings Co., Ltd. / Square Enix Co., Ltd. 所有。本工具为非官方工具，与 Square Enix 无从属或授权关系。");

        AddSectionTitle("参考项目");
        AddInfoRow("解析算法", UisaveReader, UisaveReader);
        AddInfoRow("解析算法", FfxivUisaveParser, FfxivUisaveParser);
        AddInfoRow("文件结构", FfxivClientStructsUiSavePackModule, FfxivClientStructsUiSavePackModule);
        AddInfoRow("文件结构", HaselDebugUiModuleTab, HaselDebugUiModuleTab);
        AddInfoRow("地图数据", FfxivDataminingCnContentFinderCondition, FfxivDataminingCnContentFinderCondition);
        AddInfoRow("日志解析", FfxivLogViewer, FfxivLogViewer);
        AddInfoRow("日志解析", FfxivLogParser, FfxivLogParser);
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
