using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UIMarkerEditor;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class XamlResourceTests
{
    [Fact]
    public void MainWindow_CanInitializeWithThemeResources()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            AssertButtonPadding();
            AssertColoredButtonForegroundPassesIntoTemplate();

            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "UIMarkerEditor.XamlTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                MainWindow window = new(new AppDataStore(testDirectory));
                AssertMainWindowMapDataOperationOverlayCanShowAndHide(window);
                window.Close();
                AssertDataDirectoryMigrationReportDialogCanInitialize(testDirectory);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void BusyOverlayControl_CanShowAndHideWithStatusText()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            BusyOverlayControl control = new();

            Assert.Equal(Visibility.Collapsed, control.Visibility);

            control.Show("正在测试...", "请等待测试完成。");
            control.Measure(new Size(420, 240));
            control.Arrange(new Rect(0, 0, 420, 240));
            control.UpdateLayout();

            TextBlock titleTextBlock = Assert.IsType<TextBlock>(control.FindName("Title_TextBlock"));
            TextBlock messageTextBlock = Assert.IsType<TextBlock>(control.FindName("Message_TextBlock"));
            Assert.Equal(Visibility.Visible, control.Visibility);
            Assert.Equal("正在测试...", titleTextBlock.Text);
            Assert.Equal("请等待测试完成。", messageTextBlock.Text);

            control.Hide();

            Assert.Equal(Visibility.Collapsed, control.Visibility);
        });

        Assert.Null(exception);
    }

    private static void AssertDataDirectoryMigrationReportDialogCanInitialize(string testDirectory)
    {
        DataDirectoryMigrationReportDialog progressDialog = new();
        progressDialog.Close();

        DataDirectoryMigrationReportDialog preparationDialog = new(
            Path.Combine(testDirectory, "old-data"),
            Path.Combine(testDirectory, "new-data"));
        preparationDialog.Close();

        DataDirectoryMigrationReportDialog reportDialog = new(new DataDirectoryMigrationResult
        {
            CleanupCompleted = true,
            MigratedFileCount = 3,
            SourceDirectory = Path.Combine(testDirectory, "old-data"),
            TargetDirectory = Path.Combine(testDirectory, "new-data"),
            MigrationStateFilePath = Path.Combine(testDirectory, "migration-state.json")
        });
        reportDialog.Close();
    }

    private static void AssertMainWindowMapDataOperationOverlayCanShowAndHide(MainWindow window)
    {
        BusyOverlayControl overlay = Assert.IsType<BusyOverlayControl>(
            window.FindName("MapDataOperationOverlay_Control"));

        Assert.Equal(Visibility.Collapsed, overlay.Visibility);

        overlay.Show("正在测试地图数据...", "请等待测试完成。");
        overlay.Measure(new Size(420, 240));
        overlay.Arrange(new Rect(0, 0, 420, 240));
        overlay.UpdateLayout();

        Assert.Equal(Visibility.Visible, overlay.Visibility);

        overlay.Hide();

        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
    }

    private static void AssertColoredButtonForegroundPassesIntoTemplate()
    {
        (string styleKey, string foregroundKey)[] buttonStyles =
        [
            ("PrimaryButtonStyle", "AppPrimaryButtonForegroundBrush"),
            ("SuccessButtonStyle", "AppSuccessButtonForegroundBrush"),
            ("WarningButtonStyle", "AppWarningButtonForegroundBrush"),
            ("DangerButtonStyle", "AppDangerButtonForegroundBrush"),
            ("SecondaryButtonStyle", "AppSecondaryButtonForegroundBrush"),
            ("InfoButtonStyle", "AppInfoButtonForegroundBrush"),
            ("LightButtonStyle", "AppLightButtonForegroundBrush"),
            ("DarkButtonStyle", "AppDarkButtonForegroundBrush"),
            ("OrangeButtonStyle", "AppOrangeButtonForegroundBrush"),
            ("PurpleButtonStyle", "AppPurpleButtonForegroundBrush"),
            ("TealButtonStyle", "AppTealButtonForegroundBrush"),
            ("PinkButtonStyle", "AppPinkButtonForegroundBrush")
        ];

        foreach ((string styleKey, string foregroundKey) in buttonStyles)
        {
            AssertButtonTextForeground(styleKey, foregroundKey);
        }
    }

    private static void AssertButtonPadding()
    {
        Thickness expectedPadding = new(20, 5, 20, 5);
        Button defaultButton = new()
        {
            Content = "Default",
            Style = (Style)Application.Current.FindResource(typeof(Button))
        };

        Assert.Equal(expectedPadding, defaultButton.Padding);

        foreach (string styleKey in GetColoredButtonStyleKeys())
        {
            Button button = new()
            {
                Content = "Check",
                Style = (Style)Application.Current.FindResource(styleKey)
            };

            Assert.Equal(expectedPadding, button.Padding);
        }
    }

    private static void AssertButtonTextForeground(string buttonStyleKey, string foregroundBrushKey)
    {
        Button button = new()
        {
            Content = "Check",
            Style = (Style)Application.Current.FindResource(buttonStyleKey)
        };

        button.ApplyTemplate();
        button.Measure(new Size(120, 32));
        button.Arrange(new Rect(0, 0, 120, 32));
        button.UpdateLayout();

        SolidColorBrush expectedBrush = Assert.IsType<SolidColorBrush>(
            Application.Current.FindResource(foregroundBrushKey));
        SolidColorBrush buttonBrush = Assert.IsType<SolidColorBrush>(button.Foreground);
        Brush? actualTextBrush = FindVisualChild<AccessText>(button)?.Foreground
            ?? FindVisualChild<TextBlock>(button)?.Foreground;
        SolidColorBrush actualBrush = Assert.IsType<SolidColorBrush>(actualTextBrush);

        Assert.Equal(expectedBrush.Color, buttonBrush.Color);
        Assert.Equal(expectedBrush.Color, actualBrush.Color);
    }

    private static string[] GetColoredButtonStyleKeys()
    {
        return
        [
            "PrimaryButtonStyle",
            "SuccessButtonStyle",
            "WarningButtonStyle",
            "DangerButtonStyle",
            "SecondaryButtonStyle",
            "InfoButtonStyle",
            "LightButtonStyle",
            "DarkButtonStyle",
            "OrangeButtonStyle",
            "PurpleButtonStyle",
            "TealButtonStyle",
            "PinkButtonStyle"
        ];
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
