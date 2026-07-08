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
}
