using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public partial class AppMessageBoxDialog : Window
{
    private const string ClipboardSeparator = "---------------------------";
    private MessageBoxResult result;

    public MessageBoxResult Result => result;

    public AppMessageBoxDialog(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        string? optionText = null,
        bool isOptionChecked = false)
    {
        InitializeComponent();
        Title = caption;
        Message_TextBlock.Text = messageBoxText;
        ConfigureOption(optionText, isOptionChecked);
        result = GetDefaultResult(button);
        ConfigureIcon(icon);
        ConfigureButtons(button);
        ConfigureCopyCommand();
    }

    public bool IsOptionChecked => Option_CheckBox.IsChecked == true;

    private void ConfigureOption(string? optionText, bool isOptionChecked)
    {
        if (string.IsNullOrWhiteSpace(optionText))
        {
            Option_CheckBox.Visibility = Visibility.Collapsed;
            return;
        }

        Option_CheckBox.Content = optionText;
        Option_CheckBox.IsChecked = isOptionChecked;
        Option_CheckBox.Visibility = Visibility.Visible;
    }

    private void ConfigureIcon(MessageBoxImage icon)
    {
        (string text, Brush brush) = icon switch
        {
            MessageBoxImage.Error => ("X", (Brush)FindResource("AppDangerButtonBrush")),
            MessageBoxImage.Warning => ("!", (Brush)FindResource("AppWarningButtonBrush")),
            MessageBoxImage.Question => ("?", (Brush)FindResource("AppPrimaryButtonBrush")),
            MessageBoxImage.Information => ("i", (Brush)FindResource("AppInfoButtonBrush")),
            _ => (string.Empty, Brushes.Transparent)
        };

        Icon_TextBlock.Text = text;
        Icon_TextBlock.Foreground = icon == MessageBoxImage.Warning || icon == MessageBoxImage.Information
            ? Brushes.Black
            : Brushes.White;
        Icon_Border.Background = brush;
        Icon_Border.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ConfigureButtons(MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OK:
                AddButton("确定", MessageBoxResult.OK, "PrimaryButtonStyle", isDefault: true, isCancel: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("确定", MessageBoxResult.OK, "PrimaryButtonStyle", isDefault: true, isCancel: false);
                AddButton("取消", MessageBoxResult.Cancel, null, isDefault: false, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("是", MessageBoxResult.Yes, "PrimaryButtonStyle", isDefault: true, isCancel: false);
                AddButton("否", MessageBoxResult.No, null, isDefault: false, isCancel: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("是", MessageBoxResult.Yes, "PrimaryButtonStyle", isDefault: true, isCancel: false);
                AddButton("否", MessageBoxResult.No, null, isDefault: false, isCancel: false);
                AddButton("取消", MessageBoxResult.Cancel, null, isDefault: false, isCancel: true);
                break;
        }
    }

    private void ConfigureCopyCommand()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, CopyCommand_Executed));
        InputBindings.Add(new KeyBinding(
            ApplicationCommands.Copy,
            new KeyGesture(Key.C, ModifierKeys.Control)));
        InputBindings.Add(new KeyBinding(
            ApplicationCommands.Copy,
            new KeyGesture(Key.Insert, ModifierKeys.Control)));
    }

    private void CopyCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(BuildClipboardText());
        }
        catch (ExternalException ex)
        {
            AppLogger.Warning(AppLogCategory.UI, "复制信息框内容失败", ex);
        }

        e.Handled = true;
    }

    internal string BuildClipboardText()
    {
        List<string> sections = [Title, Message_TextBlock.Text];
        if (Option_CheckBox.Visibility == Visibility.Visible)
        {
            string checkState = Option_CheckBox.IsChecked == true ? "x" : " ";
            sections.Add($"[{checkState}] {Option_CheckBox.Content}");
        }

        string buttonText = string.Join(
            "   ",
            Buttons_Panel.Children
                .OfType<Button>()
                .Select(button => button.Content?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (!string.IsNullOrWhiteSpace(buttonText))
        {
            sections.Add(buttonText);
        }

        return ClipboardSeparator + Environment.NewLine +
            string.Join(
                Environment.NewLine + ClipboardSeparator + Environment.NewLine,
                sections) +
            Environment.NewLine + ClipboardSeparator;
    }

    private void AddButton(string text, MessageBoxResult buttonResult, string? styleKey, bool isDefault, bool isCancel)
    {
        Button button = new()
        {
            Content = text,
            Width = 96,
            Height = 32,
            Margin = new Thickness(4, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
            Tag = buttonResult
        };
        if (!string.IsNullOrEmpty(styleKey))
        {
            button.Style = (Style)FindResource(styleKey);
        }

        button.Click += (_, _) =>
        {
            result = buttonResult;
            DialogResult = true;
        };
        Buttons_Panel.Children.Add(button);
    }

    private static MessageBoxResult GetDefaultResult(MessageBoxButton button)
    {
        return button switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.None
        };
    }
}
