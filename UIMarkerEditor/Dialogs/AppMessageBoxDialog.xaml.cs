using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UIMarkerEditor;

public partial class AppMessageBoxDialog : Window
{
    private MessageBoxResult result;

    public MessageBoxResult Result => result;

    public AppMessageBoxDialog(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
    {
        InitializeComponent();
        Title = caption;
        Message_TextBlock.Text = messageBoxText;
        result = GetDefaultResult(button);
        ConfigureIcon(icon);
        ConfigureButtons(button);
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
