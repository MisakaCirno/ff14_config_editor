using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace UIMarkerEditor.Controls;

public partial class HelpAboutControl
{
    private const string Repository = "https://github.com/MisakaCirno/ff14_config_editor";
    private const string BilibiliName = "@御琪幽然";
    private const string Bilibili = "https://space.bilibili.com/2908365";
    private const string QqGroupNumber = "1075777023";
    private const string QqGroup = "http://qm.qq.com/cgi-bin/qm/qr?_wv=1027&k=_Y7Glbc9stUFXNyiTKKYuBGBiusMrtY8&authKey=WfD6QlORkZPLuCqHxb0G7HWIxi3jZSJ10Ss4%2FWYvQb3hdp9IyJi8CuZ7R1BU4H%2BV&noverify=0&group_code=1075777023";
    private const string ReleaseDownloadPage = "https://misakacirno.lanzout.com/b00l2jg3ve";
    private const string ReleaseDownloadExtractCode = "9gud";
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
        AddReleaseDownloadPanel();

        AddSectionTitle("友情链接");
        AddInfoRow("呆萌", DieMoe, DieMoe);
        AddInfoRow("Souma", Souma, Souma);

        AddSectionTitle("第三方使用");
        AddInfoRow("服务器列表页面", ExternalLinks.ServerListPage, ExternalLinks.ServerListPage);
        AddInfoRow("服务器状态 API", ExternalLinks.ServerStatusApi, ExternalLinks.ServerStatusApi);
        AddInfoRow("地图数据来源", "ffxiv-datamining-cn ContentFinderCondition.csv", ExternalLinks.MapDataOnlineReferenceCsv);
        AddInfoRow("地图数据来源", "Diemoe MatchaData", ExternalLinks.MapDataDiemoeInstance);
        AddInfoRow("地图数据读取", "本地 FFXIV 客户端 sqpack");
        AddInfoRow("标点分享页面", "Souma", Souma);
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

    private void AddReleaseDownloadPanel()
    {
        Border panel = new()
        {
            Background = (Brush)FindResource("AppBackgroundBrush"),
            BorderBrush = (Brush)FindResource("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 4, 0, 10)
        };

        Grid panelGrid = new();
        panelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        panelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        panelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border accent = new()
        {
            Background = (Brush)FindResource("AppAccentBrush"),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetColumn(accent, 0);
        panelGrid.Children.Add(accent);

        StackPanel contentStackPanel = new()
        {
            Orientation = Orientation.Vertical
        };
        Grid.SetColumn(contentStackPanel, 2);

        contentStackPanel.Children.Add(new TextBlock
        {
            Text = "发布网盘",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AppAccentPressedBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 6)
        });

        contentStackPanel.Children.Add(new TextBlock
        {
            Text = "最新安装包会持续更新到蓝奏云发布页。",
            Foreground = (Brush)FindResource("AppMutedTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        contentStackPanel.Children.Add(CreateReleaseDownloadValueRow(
            ReleaseDownloadPage,
            "下载链接，可手动选择复制",
            "复制下载链接",
            CopyReleaseDownloadPage_Button_Click,
            new Thickness(-4, 0, 0, 2)));
        contentStackPanel.Children.Add(CreateReleaseDownloadValueRow(
            ReleaseDownloadExtractCode,
            "提取码，可手动选择复制",
            "复制提取码",
            CopyReleaseDownloadExtractCode_Button_Click,
            new Thickness(-4, 0, 0, 6)));
        contentStackPanel.Children.Add(CreateReleaseDownloadButtonPanel());
        panelGrid.Children.Add(contentStackPanel);
        panel.Child = panelGrid;

        AddGridElement(panel, 0, 2);
    }

    private Grid CreateReleaseDownloadValueRow(
        string value,
        string valueToolTip,
        string copyButtonToolTip,
        RoutedEventHandler copyButtonClick,
        Thickness margin)
    {
        Grid valueGrid = new()
        {
            Margin = margin
        };
        valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        TextBox valueTextBox = new()
        {
            Text = value,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            ToolTip = valueToolTip
        };
        valueTextBox.GotKeyboardFocus += ReleaseDownloadValue_TextBox_GotKeyboardFocus;
        valueTextBox.PreviewMouseLeftButtonDown += ReleaseDownloadValue_TextBox_PreviewMouseLeftButtonDown;
        Grid.SetColumn(valueTextBox, 0);
        valueGrid.Children.Add(valueTextBox);

        Button copyButton = CreateCopyIconButton(copyButtonToolTip);
        copyButton.Click += copyButtonClick;
        Grid.SetColumn(copyButton, 1);
        valueGrid.Children.Add(copyButton);

        return valueGrid;
    }

    private Button CreateCopyIconButton(string toolTip)
    {
        Path iconPath = new()
        {
            Data = (Geometry)FindResource("CopyIconGeometry"),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        iconPath.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });

        Canvas iconCanvas = new()
        {
            Width = 24,
            Height = 24
        };
        iconCanvas.Children.Add(iconPath);

        Viewbox iconViewbox = new()
        {
            Width = 16,
            Height = 16,
            Child = iconCanvas
        };

        Button copyButton = new()
        {
            Content = iconViewbox,
            Width = 34,
            MinWidth = 34,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("LightButtonStyle"),
            ToolTip = toolTip
        };
        AutomationProperties.SetName(copyButton, toolTip);
        return copyButton;
    }

    private WrapPanel CreateReleaseDownloadButtonPanel()
    {
        WrapPanel buttonPanel = new()
        {
            Margin = new Thickness(-4, 0, 0, 0)
        };

        Button openOnlyButton = new()
        {
            Content = "打开发布网盘",
            Style = (Style)FindResource("LightButtonStyle")
        };
        openOnlyButton.Click += OpenReleaseDownload_Button_Click;
        buttonPanel.Children.Add(openOnlyButton);

        Button openReleaseDownloadButton = new()
        {
            Content = "复制提取码并打开",
            Style = (Style)FindResource("PrimaryButtonStyle")
        };
        openReleaseDownloadButton.Click += ReleaseDownload_Button_Click;
        buttonPanel.Children.Add(openReleaseDownloadButton);

        return buttonPanel;
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

    private static void ReleaseDownloadValue_TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private static void ReleaseDownloadValue_TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            textBox.Focus();
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenExternalUri(e.Uri);
        e.Handled = true;
    }

    private void ReleaseDownload_Button_Click(object sender, RoutedEventArgs e)
    {
        bool extractCodeCopied = TryCopyReleaseDownloadExtractCode(out string copyErrorMessage);
        bool releasePageOpened = OpenExternalUri(new Uri(ReleaseDownloadPage, UriKind.Absolute));

        if (extractCodeCopied)
        {
            ToastService.ShowSuccess("提取码已复制。");
        }
        else
        {
            string message = releasePageOpened
                ? $"已打开发布网盘，但复制提取码失败：{copyErrorMessage}\n\n提取码：{ReleaseDownloadExtractCode}"
                : $"复制提取码失败：{copyErrorMessage}\n\n提取码：{ReleaseDownloadExtractCode}";
            AppMessageBox.Show(
                Window.GetWindow(this),
                message,
                "复制提取码失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenReleaseDownload_Button_Click(object sender, RoutedEventArgs e)
    {
        OpenExternalUri(new Uri(ReleaseDownloadPage, UriKind.Absolute));
    }

    private void CopyReleaseDownloadPage_Button_Click(object sender, RoutedEventArgs e)
    {
        if (TryCopyToClipboard(ReleaseDownloadPage, out string copyErrorMessage))
        {
            ToastService.ShowSuccess("网盘链接已复制。");
            return;
        }

        AppMessageBox.Show(
            Window.GetWindow(this),
            $"复制网盘链接失败：{copyErrorMessage}\n\n网盘链接：{ReleaseDownloadPage}",
            "复制网盘链接失败",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void CopyReleaseDownloadExtractCode_Button_Click(object sender, RoutedEventArgs e)
    {
        if (TryCopyReleaseDownloadExtractCode(out string copyErrorMessage))
        {
            ToastService.ShowSuccess("提取码已复制。");
            return;
        }

        AppMessageBox.Show(
            Window.GetWindow(this),
            $"复制提取码失败：{copyErrorMessage}\n\n提取码：{ReleaseDownloadExtractCode}",
            "复制提取码失败",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static bool TryCopyReleaseDownloadExtractCode(out string errorMessage)
    {
        return TryCopyToClipboard(ReleaseDownloadExtractCode, out errorMessage);
    }

    private static bool TryCopyToClipboard(string text, out string errorMessage)
    {
        try
        {
            Clipboard.SetText(text);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private bool OpenExternalUri(Uri uri)
    {
        try
        {
            using Process? _ = Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(Window.GetWindow(this), $"无法打开链接：{ex.Message}", "打开链接失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
