using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class WayMarkDocumentTitleFormatterTests
{
    [Fact]
    public void BuildTitle_WhenNoFileLoaded_ReturnsDefaultTitle()
    {
        string title = WayMarkDocumentTitleFormatter.BuildTitle(
            "FF14 标点预设编辑工具",
            string.Empty,
            hasUnsavedChanges: true);

        Assert.Equal("FF14 标点预设编辑工具", title);
    }

    [Fact]
    public void BuildTitle_WhenFileLoadedAndClean_ShowsFileName()
    {
        string title = WayMarkDocumentTitleFormatter.BuildTitle(
            "FF14 标点预设编辑工具",
            @"C:\FFXIV_CHR1234\UISAVE.DAT",
            hasUnsavedChanges: false);

        Assert.Equal("UISAVE.DAT - FF14 标点预设编辑工具", title);
    }

    [Fact]
    public void BuildTitle_WhenFileLoadedAndDirty_ShowsUnsavedMarker()
    {
        string title = WayMarkDocumentTitleFormatter.BuildTitle(
            "FF14 标点预设编辑工具",
            @"C:\FFXIV_CHR1234\UISAVE.DAT",
            hasUnsavedChanges: true);

        Assert.Equal("* UISAVE.DAT（未保存） - FF14 标点预设编辑工具", title);
    }
}
