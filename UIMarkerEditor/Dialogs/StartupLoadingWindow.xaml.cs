using System.Windows;
using System.ComponentModel;

namespace UIMarkerEditor;

public partial class StartupLoadingWindow : Window
{
    private bool allowClose;

    public StartupLoadingWindow()
    {
        InitializeComponent();
    }

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
            SetStatus("启动仍在进行，请等待主窗口打开后再关闭工具。");
            return;
        }

        base.OnClosing(e);
    }
}
