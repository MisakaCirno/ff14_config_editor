using System.Diagnostics;

namespace UIMarkerEditor.Tests;

public sealed class DirectoryOpenHelperTests
{
    [Fact]
    public void StartDirectoryShell_WhenShellReturnsNoProcess_TreatsLaunchAsSuccessful()
    {
        ProcessStartInfo? capturedStartInfo = null;

        DirectoryOpenHelper.StartDirectoryShell(
            @"C:\Test Directory",
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return null;
            });

        Assert.NotNull(capturedStartInfo);
        Assert.Equal(@"C:\Test Directory", capturedStartInfo.FileName);
        Assert.True(capturedStartInfo.UseShellExecute);
    }
}
