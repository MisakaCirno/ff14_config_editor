using System.Windows;
using System.ComponentModel;

namespace UIMarkerEditor;

public partial class StartupLoadingWindow : Window
{
    private readonly Func<Window, bool> confirmCancellation;
    private bool allowClose;
    private bool cancellationRequested;

    public StartupLoadingWindow()
        : this(static owner => AppMessageBox.Show(
            owner,
            "工具仍在启动。是否取消启动并退出？",
            "取消启动",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes)
    {
    }

    internal StartupLoadingWindow(Func<Window, bool> confirmCancellation)
    {
        this.confirmCancellation = confirmCancellation ?? throw new ArgumentNullException(nameof(confirmCancellation));
        InitializeComponent();
    }

    public event EventHandler? CancellationRequested;

    internal bool IsCancellationRequested => cancellationRequested;

    public void SetStatus(string status)
    {
        Status_TextBlock.Text = status;
    }

    internal void CloseAfterStartup()
    {
        allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            if (cancellationRequested)
            {
                SetStatus("正在取消启动，请稍候...");
                return;
            }

            if (!confirmCancellation(this))
            {
                SetStatus("启动仍在进行，请等待主窗口打开后再关闭工具。");
                return;
            }

            cancellationRequested = true;
            SetStatus("正在取消启动，请稍候...");
            CancellationRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }
}
