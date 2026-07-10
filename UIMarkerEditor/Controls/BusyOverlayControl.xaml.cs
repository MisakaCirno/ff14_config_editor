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
        PreviewKeyDown += BusyOverlayControl_PreviewKeyDown;
    }

    public bool IsBusy { get; private set; }

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
        IsBusy = true;
        Visibility = Visibility.Visible;
        Keyboard.ClearFocus();
        Focus();
        CommandManager.InvalidateRequerySuggested();
    }

    public void Hide()
    {
        IsBusy = false;
        Visibility = Visibility.Collapsed;
        CommandManager.InvalidateRequerySuggested();
    }

    private void BusyOverlayControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey == Key.F4)
        {
            return;
        }

        e.Handled = true;
    }
}
