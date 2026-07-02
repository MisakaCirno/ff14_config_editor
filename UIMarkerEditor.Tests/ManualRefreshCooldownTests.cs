using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class ManualRefreshCooldownTests
{
    [Fact]
    public void ShouldRecordMapDataManualRefreshAttempt_WhenOnlineRefreshSucceedsWithoutCache_ReturnsTrue()
    {
        AppSettings settings = new()
        {
            MapDataTableMode = MapDataTableMode.Automatic,
            MapDataSource = MapDataSource.OnlineReference
        };
        MapDataLoadResult result = new(
            Success: true,
            Updated: false,
            Version: "2026-07-03");

        Assert.True(ToolSettingsControl.ShouldRecordMapDataManualRefreshAttempt(settings, result));
    }

    [Fact]
    public void ShouldRecordMapDataManualRefreshAttempt_WhenOnlineRefreshFails_ReturnsFalse()
    {
        AppSettings settings = new()
        {
            MapDataTableMode = MapDataTableMode.Automatic,
            MapDataSource = MapDataSource.OnlineReference
        };
        MapDataLoadResult result = new(
            Success: false,
            Updated: false,
            Version: "",
            FailureStage: "下载地图数据",
            FailureReason: "网络不可用");

        Assert.False(ToolSettingsControl.ShouldRecordMapDataManualRefreshAttempt(settings, result));
    }

    [Fact]
    public void ShouldRecordMapDataManualRefreshAttempt_WhenOnlineRefreshUsesCacheFallback_ReturnsFalse()
    {
        AppSettings settings = new()
        {
            MapDataTableMode = MapDataTableMode.Automatic,
            MapDataSource = MapDataSource.OnlineReference
        };
        MapDataLoadResult result = new(
            Success: true,
            Updated: false,
            Version: "cached",
            UsedCache: true,
            CacheAvailable: true,
            FailureStage: "下载地图数据",
            FailureReason: "网络不可用");

        Assert.False(ToolSettingsControl.ShouldRecordMapDataManualRefreshAttempt(settings, result));
    }

    [Fact]
    public void ShouldRecordMapDataManualRefreshAttempt_WhenRefreshIsNotOnlineReference_ReturnsFalse()
    {
        MapDataLoadResult result = new(
            Success: true,
            Updated: true,
            Version: "local");

        Assert.False(ToolSettingsControl.ShouldRecordMapDataManualRefreshAttempt(
            new AppSettings
            {
                MapDataTableMode = MapDataTableMode.Automatic,
                MapDataSource = MapDataSource.LocalGame
            },
            result));
        Assert.False(ToolSettingsControl.ShouldRecordMapDataManualRefreshAttempt(
            new AppSettings
            {
                MapDataTableMode = MapDataTableMode.Manual,
                MapDataSource = MapDataSource.OnlineReference
            },
            result));
    }

    [Fact]
    public void ShouldRecordServerListManualRefreshAttempt_RecordsOnlySuccessfulRefresh()
    {
        Assert.True(ToolSettingsControl.ShouldRecordServerListManualRefreshAttempt(new ServerListLoadResult(
            Success: true,
            Updated: false)));
        Assert.False(ToolSettingsControl.ShouldRecordServerListManualRefreshAttempt(new ServerListLoadResult(
            Success: false,
            Updated: false,
            FailureStage: "检查服务器列表",
            FailureReason: "网络不可用")));
    }
}
