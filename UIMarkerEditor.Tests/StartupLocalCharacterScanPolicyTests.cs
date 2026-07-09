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
        Assert.False(StartupLocalCharacterScanPolicy.ShouldMarkCompleted(settings));
    }

    [Fact]
    public void ShouldRun_WhenModeIsFirstInitializationOnly_StopsAfterCompleted()
    {
        AppSettings settings = new()
        {
            StartupLocalCharacterScanMode = StartupLocalCharacterScanMode.FirstInitializationOnly
        };

        Assert.True(StartupLocalCharacterScanPolicy.ShouldRun(settings));
        Assert.True(StartupLocalCharacterScanPolicy.ShouldMarkCompleted(settings));

        settings.StartupLocalCharacterScanCompleted = true;

        Assert.False(StartupLocalCharacterScanPolicy.ShouldRun(settings));
        Assert.False(StartupLocalCharacterScanPolicy.ShouldMarkCompleted(settings));
    }
}
