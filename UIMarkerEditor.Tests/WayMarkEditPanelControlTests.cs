using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FF14ConfigEditor.UISave;
using UIMarkerEditor;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class WayMarkEditPanelControlTests
{
    [Fact]
    public void CommitPendingEdits_WhenCoordinateTextIsValid_UpdatesWayMark()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WayMark wayMark = new();
            WayMarkEditPanelControl control = CreateControl(wayMark);
            TextBox textBox = GetCoordinateTextBox(control, "A_X_TextBox");
            int changedCount = 0;
            control.WayMarkChanged += (_, _) => changedCount++;

            textBox.Text = "123.456";

            Assert.True(control.CommitPendingEdits());
            Assert.Equal(123456, wayMark.A.X);
            Assert.True(changedCount > 0);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void CommitPendingEdits_WhenCoordinateTextIsIncomplete_ReturnsFalseWithoutUpdatingWayMark()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WayMark wayMark = new();
            WayMarkEditPanelControl control = CreateControl(wayMark);
            TextBox textBox = GetCoordinateTextBox(control, "A_X_TextBox");

            textBox.Text = "-";

            Assert.False(control.CommitPendingEdits());
            Assert.Equal(0, wayMark.A.X);
            Assert.Equal("-", textBox.Text);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void FavoriteConfirmSaveOrDiscardChanges_WhenAutoSaveMode_CommitsPendingCoordinateBeforeSaving()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.FavoritePendingEditTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                AppDataStore store = new(testDirectory);
                store.Initialize();
                WayMark wayMark = new()
                {
                    RegionID = 123
                };
                store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(wayMark), "测试收藏");

                WayMarkFavoritesControl control = new();
                Window owner = new()
                {
                    Content = control
                };
                control.Initialize(store, owner);
                control.ApplySettings(new AppSettings
                {
                    WayMarkFavoriteSaveMode = WayMarkFavoriteSaveMode.Auto
                });
                owner.Show();
                control.UpdateLayout();

                TextBox textBox = FindVisualChildByName<TextBox>(control, "A_X_TextBox")
                    ?? throw new InvalidOperationException("A_X_TextBox not found.");
                textBox.Text = "123.456";

                Assert.True(control.ConfirmSaveOrDiscardChanges());
                Assert.Equal(123456, store.WayMarkFavorites[0].Marker.A.X);

                owner.Close();
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void FavoriteAutoSave_WhenCommittedEditCompletes_SavesFavoriteWithoutTimer()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.FavoriteAutoSaveTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                AppDataStore store = new(testDirectory);
                store.Initialize();
                WayMark wayMark = new()
                {
                    RegionID = 123
                };
                store.AddWayMarkFavorite(WayMarkSnapshotConverter.CreateSnapshot(wayMark), "测试收藏");

                WayMarkFavoritesControl control = new();
                Window owner = new()
                {
                    Content = control
                };
                control.Initialize(store, owner);
                control.ApplySettings(new AppSettings
                {
                    WayMarkFavoriteSaveMode = WayMarkFavoriteSaveMode.Auto
                });
                owner.Show();
                control.UpdateLayout();

                TextBox commentTextBox = FindVisualChildByName<TextBox>(control, "CommentName_TextBox")
                    ?? throw new InvalidOperationException("CommentName_TextBox not found.");
                commentTextBox.Text = "正在输入";
                DrainDispatcherOperations();

                Assert.Equal("测试收藏", store.WayMarkFavorites[0].CommentName);

                WayMarkEditPanelControl editPanel = FindVisualChildByName<WayMarkEditPanelControl>(control, "WayMarkEditPanel_Control")
                    ?? throw new InvalidOperationException("WayMarkEditPanel_Control not found.");
                TextBox coordinateTextBox = FindVisualChildByName<TextBox>(control, "A_X_TextBox")
                    ?? throw new InvalidOperationException("A_X_TextBox not found.");
                coordinateTextBox.Text = "123.456";

                Assert.True(editPanel.CommitPendingEdits());
                DrainDispatcherOperations();

                Assert.Equal("正在输入", store.WayMarkFavorites[0].CommentName);
                Assert.Equal(123456, store.WayMarkFavorites[0].Marker.A.X);

                owner.Close();
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static WayMarkEditPanelControl CreateControl(WayMark wayMark)
    {
        WpfTestHost.EnsureApplicationResources();
        WayMarkEditPanelControl control = new();
        control.SetWayMark(wayMark);
        control.Measure(new Size(800, 600));
        control.Arrange(new Rect(0, 0, 800, 600));
        control.UpdateLayout();
        InitializeCoordinateText(control);
        return control;
    }

    private static void InitializeCoordinateText(WayMarkEditPanelControl control)
    {
        foreach (string pointName in new[] { "A", "B", "C", "D", "One", "Two", "Three", "Four" })
        {
            foreach (string axisName in new[] { "X", "Y", "Z" })
            {
                GetCoordinateTextBox(control, $"{pointName}_{axisName}_TextBox").Text = "0";
            }
        }
    }

    private static TextBox GetCoordinateTextBox(WayMarkEditPanelControl control, string name)
    {
        return Assert.IsType<TextBox>(control.FindName(name));
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && typedChild.Name == name)
            {
                return typedChild;
            }

            T? descendant = FindVisualChildByName<T>(child, name);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static void DrainDispatcherOperations()
    {
        DispatcherFrame frame = new();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.Background);
        Dispatcher.PushFrame(frame);
    }
}
