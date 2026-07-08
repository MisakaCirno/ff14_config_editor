using System.Windows.Controls;
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
}
