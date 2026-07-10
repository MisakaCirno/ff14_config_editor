using System.IO;
using System.Windows.Controls;

namespace UIMarkerEditor.Tests;

public sealed class UserMapDataEditorDialogTests
{
    [Fact]
    public void Constructor_WhenUserCsvHasIssues_KeepsRowsAndMarksIssues()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.UserMapDataEditorDialogTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            string filePath = Path.Combine(testDirectory, "mapdata_user.csv");
            File.WriteAllText(
                filePath,
                "ID,Name\r\n" +
                "bad,坏 ID\r\n" +
                "321,第一项\r\n" +
                "321,重复项\r\n" +
                "322,名称,带逗号\r\n");

            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                UserMapDataEditorDialog dialog = new(filePath);
                DataGrid dataGrid = Assert.IsType<DataGrid>(dialog.FindName("MapDataRows_DataGrid"));
                List<UserMapDataEditorRow> rows = dataGrid.ItemsSource
                    .Cast<UserMapDataEditorRow>()
                    .ToList();

                Assert.Equal(4, rows.Count);
                Assert.Equal("bad", rows[0].MapId);
                Assert.True(rows[0].HasError);
                Assert.True(rows[1].HasError);
                Assert.True(rows[2].HasError);
                Assert.Equal("名称,带逗号", rows[3].Name);
                Assert.True(rows[3].HasWarning);

                dialog.Close();
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_WhenUserCsvHasUnclosedQuote_MarksProblemRow()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.UserMapDataEditorDialogTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            string filePath = Path.Combine(testDirectory, "mapdata_user.csv");
            File.WriteAllText(
                filePath,
                "ID,Name\r\n" +
                "321,\"未闭合\r\n" +
                "322,下一行\r\n");

            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                UserMapDataEditorDialog dialog = new(filePath);
                DataGrid dataGrid = Assert.IsType<DataGrid>(dialog.FindName("MapDataRows_DataGrid"));
                List<UserMapDataEditorRow> rows = dataGrid.ItemsSource
                    .Cast<UserMapDataEditorRow>()
                    .ToList();

                UserMapDataEditorRow row = Assert.Single(rows);
                Assert.Equal("321", row.MapId);
                Assert.True(row.HasError);
                Assert.Contains("引号没有闭合", row.IssueText, StringComparison.Ordinal);

                dialog.Close();
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Close_WhenRowsChanged_RequiresDiscardConfirmation()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.UserMapDataEditorDialogTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            string filePath = Path.Combine(testDirectory, "mapdata_user.csv");
            File.WriteAllText(filePath, "321,原名称\r\n");

            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                bool allowDiscard = false;
                int confirmationCount = 0;
                UserMapDataEditorDialog dialog = new(filePath, _ =>
                {
                    confirmationCount++;
                    return allowDiscard;
                });
                dialog.Show();
                try
                {
                    DataGrid dataGrid = Assert.IsType<DataGrid>(dialog.FindName("MapDataRows_DataGrid"));
                    UserMapDataEditorRow row = Assert.Single(dataGrid.ItemsSource.Cast<UserMapDataEditorRow>());
                    row.Name = "修改后的名称";

                    dialog.Close();

                    Assert.True(dialog.IsVisible);
                    Assert.Equal(1, confirmationCount);

                    allowDiscard = true;
                    dialog.Close();

                    Assert.False(dialog.IsVisible);
                    Assert.Equal(2, confirmationCount);
                }
                finally
                {
                    allowDiscard = true;
                    if (dialog.IsVisible)
                    {
                        dialog.Close();
                    }
                }
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Close_WhenRowsUnchanged_DoesNotRequestDiscardConfirmation()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.UserMapDataEditorDialogTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            string filePath = Path.Combine(testDirectory, "mapdata_user.csv");
            File.WriteAllText(filePath, "321,原名称\r\n");

            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                int confirmationCount = 0;
                UserMapDataEditorDialog dialog = new(filePath, _ =>
                {
                    confirmationCount++;
                    return false;
                });
                dialog.Show();
                try
                {
                    dialog.Close();

                    Assert.False(dialog.IsVisible);
                    Assert.Equal(0, confirmationCount);
                }
                finally
                {
                    if (dialog.IsVisible)
                    {
                        dialog.Close();
                    }
                }
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}
