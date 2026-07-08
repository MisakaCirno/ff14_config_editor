using System.Windows;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class WayMarkFavoritePickerDialogTests
{
    [Fact]
    public void ApplyLayoutSettings_WhenSavedPositionIsVisible_RestoresPosition()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            WayMarkFavoritePickerDialog dialog = new([]);
            try
            {
                double left = SystemParameters.VirtualScreenLeft + 20;
                double top = SystemParameters.VirtualScreenTop + 20;
                WindowLayoutSettings layout = new()
                {
                    WayMarkFavoritePickerLeft = left,
                    WayMarkFavoritePickerTop = top,
                    WayMarkFavoritePickerWidth = 720,
                    WayMarkFavoritePickerHeight = 520,
                    WayMarkFavoritePickerListRatio = 0.7
                };

                dialog.ApplyLayoutSettings(layout);

                Assert.Equal(WindowStartupLocation.Manual, dialog.WindowStartupLocation);
                Assert.Equal(left, dialog.Left);
                Assert.Equal(top, dialog.Top);
                Assert.Equal(720, dialog.Width);
                Assert.Equal(520, dialog.Height);
            }
            finally
            {
                dialog.Close();
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void ApplyLayoutSettings_WhenSavedPositionIsOffScreen_KeepsCenteredStartupLocation()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            WayMarkFavoritePickerDialog dialog = new([]);
            try
            {
                WindowLayoutSettings layout = new()
                {
                    WayMarkFavoritePickerLeft = SystemParameters.VirtualScreenLeft - 10000,
                    WayMarkFavoritePickerTop = SystemParameters.VirtualScreenTop - 10000,
                    WayMarkFavoritePickerWidth = 720,
                    WayMarkFavoritePickerHeight = 520,
                    WayMarkFavoritePickerListRatio = 0.7
                };

                dialog.ApplyLayoutSettings(layout);

                Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
                Assert.Equal(720, dialog.Width);
                Assert.Equal(520, dialog.Height);
            }
            finally
            {
                dialog.Close();
            }
        });

        Assert.Null(exception);
    }
}
