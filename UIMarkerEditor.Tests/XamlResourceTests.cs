using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                Assert.Equal(880d, window.MinWidth);
                Assert.Equal(460d, window.MinHeight);
                AssertMainWindowCloseFileCommand(window);
                AssertMainWindowMapDataOperationOverlayCanShowAndHide(window);
                AssertBackupRestoreOverlayCanShowAndHide(window);
                AssertResponsiveSplitPaneMinimums(window);
                window.Close();
                AssertMapDataSourceStartupDialogSupportsSmallWorkAreas();
                AssertStartupLoadingWindowCannotClosePrematurely();
                AssertAppMessageBoxLongTextUsesScrollLimit();
                AssertCurrentFileMissingDialogCanInitialize();
                AssertDataDirectoryMigrationReportDialogCanInitialize(testDirectory);
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        });

        Assert.Null(exception);
    }

    private static void AssertMapDataSourceStartupDialogSupportsSmallWorkAreas()
    {
        MapDataSourceStartupDialog dialog = new();
        try
        {
            dialog.Show();
            ScrollViewer scrollViewer = Assert.IsType<ScrollViewer>(dialog.FindName("SourceOptions_ScrollViewer"));

            Assert.Equal(ResizeMode.CanResize, dialog.ResizeMode);
            Assert.Equal(760d, dialog.MinWidth);
            Assert.Equal(420d, dialog.MinHeight);
            Assert.Equal(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);
            Assert.True(dialog.Width <= SystemParameters.WorkArea.Width);
            Assert.True(dialog.Height <= SystemParameters.WorkArea.Height);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void AssertResponsiveSplitPaneMinimums(MainWindow window)
    {
        WayMarkEditorControl wayMarkEditor = Assert.IsType<WayMarkEditorControl>(window.FindName("WayMarkEditor_Control"));
        Assert.Equal(190d, Assert.IsType<ColumnDefinition>(wayMarkEditor.FindName("WayMarkList_Column")).MinWidth);
        Assert.Equal(280d, Assert.IsType<ColumnDefinition>(wayMarkEditor.FindName("WayMarkEditor_Column")).MinWidth);
        Assert.Equal(220d, Assert.IsType<ColumnDefinition>(wayMarkEditor.FindName("WayMarkPreview_Column")).MinWidth);

        CharacterProfilesControl characterProfiles = Assert.IsType<CharacterProfilesControl>(window.FindName("CharacterProfiles_Control"));
        Assert.Equal(340d, Assert.IsType<ColumnDefinition>(characterProfiles.FindName("CharacterList_Column")).MinWidth);
        Assert.Equal(280d, Assert.IsType<ColumnDefinition>(characterProfiles.FindName("CharacterDetail_Column")).MinWidth);
    }

    private static void AssertCurrentFileMissingDialogCanInitialize()
    {
        const string filePath = "C:\\game\\FFXIV_CHR0123456789ABCDEF\\UISAVE.DAT";
        CurrentFileMissingDialog dialog = new(filePath);
        try
        {
            TextBox filePathTextBox = Assert.IsType<TextBox>(dialog.FindName("FilePath_TextBox"));

            Assert.Equal(CurrentFileMissingDialogResult.ContinueEditing, dialog.Result);
            Assert.Equal(filePath, filePathTextBox.Text);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void AssertStartupLoadingWindowCannotClosePrematurely()
    {
        StartupLoadingWindow window = new(_ => false);
        window.Show();
        try
        {
            window.Close();

            Assert.True(window.IsVisible);
            Assert.False(window.IsCancellationRequested);
            TextBlock statusTextBlock = Assert.IsType<TextBlock>(window.FindName("Status_TextBlock"));
            Assert.Contains("启动仍在进行", statusTextBlock.Text);
        }
        finally
        {
            window.CloseAfterStartup();
        }

        Assert.False(window.IsVisible);

        bool cancellationRequested = false;
        StartupLoadingWindow cancellingWindow = new(_ => true);
        cancellingWindow.CancellationRequested += (_, _) => cancellationRequested = true;
        cancellingWindow.Show();
        try
        {
            cancellingWindow.Close();

            Assert.True(cancellingWindow.IsVisible);
            Assert.True(cancellingWindow.IsCancellationRequested);
            Assert.True(cancellationRequested);
        }
        finally
        {
            cancellingWindow.CloseAfterStartup();
        }
    }

    private static void AssertAppMessageBoxLongTextUsesScrollLimit()
    {
        string message = string.Join(Environment.NewLine, Enumerable.Repeat("这是一行较长的诊断信息。", 100));
        AppMessageBoxDialog dialog = new(
            message,
            "长消息测试",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        try
        {
            ScrollViewer scrollViewer = Assert.IsType<ScrollViewer>(dialog.FindName("Message_ScrollViewer"));
            TextBlock messageTextBlock = Assert.IsType<TextBlock>(dialog.FindName("Message_TextBlock"));

            Assert.Equal(360d, scrollViewer.MaxHeight);
            Assert.Equal(message, messageTextBlock.Text);
        }
        finally
        {
            dialog.Close();
        }
    }

    private static void AssertMainWindowCloseFileCommand(MainWindow window)
    {
        Assert.Contains(
            window.CommandBindings.Cast<CommandBinding>(),
            binding => ReferenceEquals(binding.Command, MainWindow.CloseWayMarkFileCommand));

        MenuItem fileMenuItem = Assert.IsType<MenuItem>(window.FindName("File_MenuItem"));
        Assert.Contains(
            fileMenuItem.Items.OfType<MenuItem>(),
            menuItem => string.Equals(menuItem.Header as string, "关闭当前文件", StringComparison.Ordinal) &&
                        ReferenceEquals(menuItem.Command, MainWindow.CloseWayMarkFileCommand));
    }

    [Fact]
    public void BusyOverlayControl_CanShowAndHideWithStatusText()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            BusyOverlayControl control = new();

            Assert.Equal(Visibility.Collapsed, control.Visibility);
            Assert.False(control.IsBusy);

            control.Show("正在测试...", "请等待测试完成。");
            control.Measure(new Size(420, 240));
            control.Arrange(new Rect(0, 0, 420, 240));
            control.UpdateLayout();

            TextBlock titleTextBlock = Assert.IsType<TextBlock>(control.FindName("Title_TextBlock"));
            TextBlock messageTextBlock = Assert.IsType<TextBlock>(control.FindName("Message_TextBlock"));
            Assert.Equal(Visibility.Visible, control.Visibility);
            Assert.True(control.IsBusy);
            Assert.Equal("正在测试...", titleTextBlock.Text);
            Assert.Equal("请等待测试完成。", messageTextBlock.Text);

            control.Hide();

            Assert.Equal(Visibility.Collapsed, control.Visibility);
            Assert.False(control.IsBusy);
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
        Assert.False(window.IsBlockingOperationInProgress());

        overlay.Show("正在测试地图数据...", "请等待测试完成。");
        overlay.Measure(new Size(420, 240));
        overlay.Arrange(new Rect(0, 0, 420, 240));
        overlay.UpdateLayout();

        Assert.Equal(Visibility.Visible, overlay.Visibility);
        Assert.True(window.IsBlockingOperationInProgress());
        Assert.False(MainWindow.OpenWayMarkFileCommand.CanExecute(null, window));

        overlay.Hide();

        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        Assert.False(window.IsBlockingOperationInProgress());
        Assert.True(MainWindow.OpenWayMarkFileCommand.CanExecute(null, window));
    }

    private static void AssertBackupRestoreOverlayCanShowAndHide(MainWindow window)
    {
        BackupRestoreControl backupRestoreControl = Assert.IsType<BackupRestoreControl>(
            window.FindName("BackupRestore_Control"));
        BusyOverlayControl overlay = Assert.IsType<BusyOverlayControl>(
            backupRestoreControl.FindName("BackupBusyOverlay_Control"));

        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        Assert.False(backupRestoreControl.IsOperationBusy);

        overlay.Show("正在测试备份操作...", "请等待测试完成。");
        overlay.Measure(new Size(420, 240));
        overlay.Arrange(new Rect(0, 0, 420, 240));
        overlay.UpdateLayout();

        Assert.Equal(Visibility.Visible, overlay.Visibility);
        Assert.True(backupRestoreControl.IsOperationBusy);
        Assert.True(window.IsBlockingOperationInProgress());

        overlay.Hide();

        Assert.Equal(Visibility.Collapsed, overlay.Visibility);
        Assert.False(backupRestoreControl.IsOperationBusy);
        Assert.False(window.IsBlockingOperationInProgress());
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
