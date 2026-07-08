namespace UIMarkerEditor.Tests;

public sealed class MapDataTableCsvTests
{
    [Fact]
    public void DiagnoseSimpleMapDataCsv_PreservesInvalidDuplicateAndExtraColumnRows()
    {
        string csv =
            "ID,Name\r\n" +
            "abc,坏 ID\r\n" +
            "2,\r\n" +
            "3,第一项\r\n" +
            "3,重复项\r\n" +
            "4,名称,带逗号\r\n";

        MapDataTableCsvDiagnosticResult result = MapDataTableCsv.DiagnoseSimpleMapDataCsv(csv);

        Assert.True(result.HasErrors);
        Assert.Empty(result.MapNames);
        Assert.Equal(["abc", "2", "3", "3", "4"], result.Rows.Select(row => row.MapIdText));
        Assert.Equal("名称,带逗号", result.Rows[4].Name);
        Assert.Contains(result.Issues, issue =>
            issue.RowNumber == 1 &&
            issue.Severity == MapDataTableCsvIssueSeverity.Error &&
            issue.Message.Contains("地图 ID 无效", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue =>
            issue.RowNumber == 2 &&
            issue.Severity == MapDataTableCsvIssueSeverity.Error &&
            issue.Message.Contains("缺少地图名称", StringComparison.Ordinal));
        Assert.Equal(2, result.Issues.Count(issue =>
            issue.Severity == MapDataTableCsvIssueSeverity.Error &&
            issue.Message.Contains("重复", StringComparison.Ordinal)));
        Assert.Contains(result.Issues, issue =>
            issue.RowNumber == 5 &&
            issue.Severity == MapDataTableCsvIssueSeverity.Warning &&
            issue.Message.Contains("多余列", StringComparison.Ordinal));
    }

    [Fact]
    public void DiagnoseSimpleMapDataCsv_WhenQuotedFieldIsNotClosed_ReturnsErrorAndNoMapNames()
    {
        string csv =
            "ID,Name\r\n" +
            "321,\"未闭合\r\n" +
            "322,下一行\r\n";

        MapDataTableCsvDiagnosticResult result = MapDataTableCsv.DiagnoseSimpleMapDataCsv(csv);

        Assert.True(result.HasErrors);
        Assert.Empty(result.MapNames);
        MapDataTableCsvIssue issue = Assert.Single(result.Issues.Where(issue =>
            issue.Severity == MapDataTableCsvIssueSeverity.Error &&
            issue.Message.Contains("引号没有闭合", StringComparison.Ordinal)));
        Assert.Equal(1, issue.RowNumber);
        Assert.Single(result.Rows);
        Assert.Equal("321", result.Rows[0].MapIdText);
    }

    [Fact]
    public void DiagnoseSimpleMapDataRows_WhenRowsAreValid_ReturnsMapNames()
    {
        MapDataTableCsvDiagnosticResult result = MapDataTableCsv.DiagnoseSimpleMapDataRows(
        [
            new MapDataTableCsvRow(1, "321", "地图一"),
            new MapDataTableCsvRow(2, "322", "地图二")
        ]);

        Assert.False(result.HasIssues);
        Assert.Equal("地图一", result.MapNames[321]);
        Assert.Equal("地图二", result.MapNames[322]);
    }
}
