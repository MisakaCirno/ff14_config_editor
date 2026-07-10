using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class ServerPickerControlTests
{
    [Fact]
    public void SelectServer_WhenSavedServerIsMissingFromList_KeepsSavedSelection()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            ServerPickerControl control = new();
            control.SetServerGroups(
            [
                new ServerGroup
                {
                    DataCenter = "莫古力",
                    Worlds = ["潮风亭"]
                }
            ]);

            control.SelectServer("陆行鸟", "拉诺西亚");

            (string DataCenter, string World)? selectedServer = control.GetSelectedServer();
            Assert.NotNull(selectedServer);
            Assert.Equal("陆行鸟", selectedServer.Value.DataCenter);
            Assert.Equal("拉诺西亚", selectedServer.Value.World);

            TextBlock textBlock = Assert.IsType<TextBlock>(control.FindName("ServerPicker_TextBlock"));
            Assert.Contains("已保存：陆行鸟 / 拉诺西亚", textBlock.Text);
            Assert.Contains("当前列表不可用", textBlock.Text);

            Button clearButton = Assert.IsType<Button>(control.FindName("ClearServer_Button"));
            Assert.True(clearButton.IsEnabled);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void SetServerGroups_WhenSavedServerBecomesAvailable_UsesCurrentListSelection()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            ServerPickerControl control = new();
            control.SetServerGroups([]);
            control.SelectServer("陆行鸟", "拉诺西亚");

            control.SetServerGroups(
            [
                new ServerGroup
                {
                    DataCenter = "陆行鸟",
                    Worlds = ["拉诺西亚"]
                }
            ]);

            (string DataCenter, string World)? selectedServer = control.GetSelectedServer();
            Assert.NotNull(selectedServer);
            Assert.Equal("陆行鸟", selectedServer.Value.DataCenter);
            Assert.Equal("拉诺西亚", selectedServer.Value.World);

            TextBlock textBlock = Assert.IsType<TextBlock>(control.FindName("ServerPicker_TextBlock"));
            Assert.Equal("陆行鸟 / 拉诺西亚", textBlock.Text);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void KeyboardNavigation_SelectsWorldOnlyAfterEnter()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            ServerPickerControl control = new();
            control.SetServerGroups(
            [
                new ServerGroup
                {
                    DataCenter = "陆行鸟",
                    Worlds = ["拉诺西亚", "幻影群岛"]
                }
            ]);
            int selectionChangedCount = 0;
            control.SelectedServerChanged += (_, _) => selectionChangedCount++;
            Window window = new() { Content = control };
            window.Show();
            try
            {
                Button pickerButton = Assert.IsType<Button>(control.FindName("ServerPicker_Button"));
                Popup popup = Assert.IsType<Popup>(control.FindName("ServerPicker_Popup"));
                ListBox areaListBox = Assert.IsType<ListBox>(control.FindName("ServerArea_ListBox"));
                ListBox worldListBox = Assert.IsType<ListBox>(control.FindName("ServerWorld_ListBox"));

                pickerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();

                Assert.True(popup.IsOpen);
                Assert.True(areaListBox.IsKeyboardFocusWithin);
                RaisePreviewKeyDown(areaListBox, Key.Right);
                Assert.True(worldListBox.IsKeyboardFocusWithin);

                worldListBox.SelectedIndex = 1;
                Assert.True(popup.IsOpen);
                Assert.Equal(0, selectionChangedCount);

                RaisePreviewKeyDown(worldListBox, Key.Enter);

                Assert.False(popup.IsOpen);
                Assert.Equal(1, selectionChangedCount);
                Assert.Equal(("陆行鸟", "幻影群岛"), control.GetSelectedServer());
                Assert.True(pickerButton.IsKeyboardFocused);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void KeyboardNavigation_EscapeClosesPopupAndReturnsFocus()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            ServerPickerControl control = new();
            control.SetServerGroups(
            [
                new ServerGroup
                {
                    DataCenter = "陆行鸟",
                    Worlds = ["拉诺西亚"]
                }
            ]);
            Window window = new() { Content = control };
            window.Show();
            try
            {
                Button pickerButton = Assert.IsType<Button>(control.FindName("ServerPicker_Button"));
                Popup popup = Assert.IsType<Popup>(control.FindName("ServerPicker_Popup"));
                ListBox areaListBox = Assert.IsType<ListBox>(control.FindName("ServerArea_ListBox"));

                pickerButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                FlushDispatcher();
                RaisePreviewKeyDown(areaListBox, Key.Escape);

                Assert.False(popup.IsOpen);
                Assert.True(pickerButton.IsKeyboardFocused);
                Assert.Null(control.GetSelectedServer());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Null(exception);
    }

    private static void RaisePreviewKeyDown(UIElement target, Key key)
    {
        PresentationSource source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("测试控件尚未连接到 WPF 可视树。");
        KeyEventArgs eventArgs = new(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(eventArgs);
    }

    private static void FlushDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ApplicationIdle);
    }
}
