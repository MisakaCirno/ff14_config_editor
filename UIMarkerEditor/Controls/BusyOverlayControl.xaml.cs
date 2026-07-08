using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UIMarkerEditor.Controls;

public partial class BusyOverlayControl : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(BusyOverlayControl),
        new PropertyMetadata("正在处理..."));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(string),
        typeof(BusyOverlayControl),
        new PropertyMetadata("请稍候。"));

    public BusyOverlayControl()
    {
        InitializeComponent();
        Visibility = Visibility.Collapsed;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public void Show(string title, string message)
    {
        Title = title;
        Message = message;
        Visibility = Visibility.Visible;
        Keyboard.ClearFocus();
        Focus();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
    }
}
