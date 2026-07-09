namespace UIMarkerEditor.Tests;

public sealed class AppTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    public void ShouldAddStartupMapDataLoadWarning_SkipsRepairPromptResults(
        bool success,
        bool usedCache,
        bool requiresRepair,
        bool expected)
    {
        MapDataLoadResult result = new(
            success,
            Updated: false,
            Version: "test-version",
            UsedCache: usedCache,
            RequiresUserMapDataRepair: requiresRepair);

        Assert.Equal(expected, App.ShouldAddStartupMapDataLoadWarning(result));
    }

    [Fact]
    public void SelectActivationTarget_WhenMainWindowExists_ReturnsMainWindow()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            System.Windows.Window mainWindow = new();
            System.Windows.Window startupWindow = new();

            try
            {
                mainWindow.Show();
                startupWindow.Show();

                Assert.Same(
                    mainWindow,
                    App.SelectActivationTarget(mainWindow, new[] { startupWindow }));
            }
            finally
            {
                CloseWindow(startupWindow);
                CloseWindow(mainWindow);
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void SelectActivationTarget_WhenMainWindowMissing_ReturnsVisibleWindow()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            System.Windows.Window hiddenWindow = new();
            System.Windows.Window startupWindow = new();

            try
            {
                startupWindow.Show();

                Assert.Same(
                    startupWindow,
                    App.SelectActivationTarget(null, new[] { hiddenWindow, startupWindow }));
            }
            finally
            {
                CloseWindow(startupWindow);
                CloseWindow(hiddenWindow);
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void SelectActivationTarget_WhenNoVisibleWindow_ReturnsNull()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            System.Windows.Window hiddenWindow = new();

            try
            {
                Assert.Null(App.SelectActivationTarget(null, new[] { hiddenWindow }));
            }
            finally
            {
                CloseWindow(hiddenWindow);
            }
        });

        Assert.Null(exception);
    }

    private static void CloseWindow(System.Windows.Window window)
    {
        if (window.IsVisible)
        {
            window.Close();
        }
    }
}
