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
}
