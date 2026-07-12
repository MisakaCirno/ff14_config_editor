using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class StartupLocalCharacterScanPolicyTests
{
    [Fact]
    public void ShouldRun_WhenModeIsEveryStartup_ReturnsTrueEvenAfterCompleted()
    {
        AppSettings settings = new()
        {
            StartupLocalCharacterScanMode = StartupLocalCharacterScanMode.EveryStartup,
            StartupLocalCharacterScanCompleted = true
        };

        Assert.True(StartupLocalCharacterScanPolicy.ShouldRun(settings));
        Assert.False(StartupLocalCharacterScanPolicy.ShouldMarkCompleted(settings, scanCompleted: true));
    }

    [Fact]
    public void ShouldRun_WhenModeIsFirstInitializationOnly_StopsAfterCompleted()
    {
        AppSettings settings = new()
        {
            StartupLocalCharacterScanMode = StartupLocalCharacterScanMode.FirstInitializationOnly
        };

        Assert.True(StartupLocalCharacterScanPolicy.ShouldRun(settings));
        Assert.False(StartupLocalCharacterScanPolicy.ShouldMarkCompleted(settings, scanCompleted: false));
        Assert.True(StartupLocalCharacterScanPolicy.ShouldMarkCompleted(settings, scanCompleted: true));

        settings.StartupLocalCharacterScanCompleted = true;

        Assert.False(StartupLocalCharacterScanPolicy.ShouldRun(settings));
        Assert.False(StartupLocalCharacterScanPolicy.ShouldMarkCompleted(settings, scanCompleted: true));
    }
}
