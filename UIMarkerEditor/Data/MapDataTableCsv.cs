using System.Globalization;
using System.Text;

namespace UIMarkerEditor;

internal static class MapDataTableCsv
{
    public static string Serialize(IReadOnlyDictionary<ushort, string> mapNames)
    {
        StringBuilder builder = new();
        builder.Append("ID,Name\r\n");
        foreach (KeyValuePair<ushort, string> pair in mapNames.OrderBy(pair => pair.Key))
        {
            if (pair.Key == MapData.EmptyRegionId || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            AppendCsvField(builder, pair.Key.ToString(CultureInfo.InvariantCulture));
            builder.Append(',');
            AppendCsvField(builder, pair.Value.Trim());
            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    public static Dictionary<ushort, string> ParseSimpleMapDataCsv(string csv)
    {
        List<List<string>> records = ReadRecords(csv);
        Dictionary<ushort, string> mapNames = [];
        for (int i = HasSimpleMapDataHeader(records) ? 1 : 0; i < records.Count; i++)
        {
            List<string> fields = records[i];
            if (fields.Count < 2)
            {
                continue;
            }

            string mapIdText = fields[0].Trim().TrimStart('\uFEFF');
            if (!ushort.TryParse(mapIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort mapId) ||
                mapId == MapData.EmptyRegionId)
            {
                continue;
            }

            string name = fields[1].Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            mapNames.TryAdd(mapId, name);
        }

        return mapNames;
    }

    public static MapDataTableCsvDiagnosticResult DiagnoseSimpleMapDataCsv(string csv)
    {
        CsvReadResult readResult = ReadRecordsWithDiagnostics(csv);
        List<List<string>> records = readResult.Records;
        HashSet<int> issueRecordIndexes = [.. readResult.Issues.Select(static issue => issue.RecordIndex)];
        bool hasHeader = HasSimpleMapDataHeader(records) && !issueRecordIndexes.Contains(0);
        List<MapDataTableCsvRow> rows = [];
        Dictionary<int, int> recordIndexToRowNumber = [];
        int visibleRowNumber = 0;
        for (int i = hasHeader ? 1 : 0; i < records.Count; i++)
        {
            List<string> fields = records[i];
            string mapIdText = fields.Count > 0
                ? NormalizeHeaderName(fields[0])
                : string.Empty;
            string name = fields.Count > 1
                ? string.Join(",", fields.Skip(1))
                : string.Empty;
            if (string.IsNullOrWhiteSpace(mapIdText) && string.IsNullOrWhiteSpace(name))
            {
                if (!issueRecordIndexes.Contains(i))
                {
                    continue;
                }
            }

            visibleRowNumber++;
            recordIndexToRowNumber[i] = visibleRowNumber;
            rows.Add(new MapDataTableCsvRow(
                visibleRowNumber,
                mapIdText,
                name,
                HasExtraColumns: fields.Count > 2));
        }

        MapDataTableCsvDiagnosticResult rowDiagnosticResult = DiagnoseSimpleMapDataRows(rows);
        List<MapDataTableCsvIssue> issues = [.. rowDiagnosticResult.Issues];
        foreach (CsvReadIssue issue in readResult.Issues)
        {
            int rowNumber = recordIndexToRowNumber.TryGetValue(issue.RecordIndex, out int mappedRowNumber)
                ? mappedRowNumber
                : Math.Max(1, visibleRowNumber);
            issues.Add(new MapDataTableCsvIssue(
                rowNumber,
                MapDataTableCsvIssueSeverity.Error,
                issue.Message));
        }

        IReadOnlyDictionary<ushort, string> mapNames = issues.Any(static issue => issue.Severity == MapDataTableCsvIssueSeverity.Error)
            ? new Dictionary<ushort, string>()
            : rowDiagnosticResult.MapNames;
        return new MapDataTableCsvDiagnosticResult(rows, issues, mapNames);
    }

    public static MapDataTableCsvDiagnosticResult DiagnoseSimpleMapDataRows(IEnumerable<MapDataTableCsvRow> rows)
    {
        List<MapDataTableCsvRow> normalizedRows = [.. rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.MapIdText) || !string.IsNullOrWhiteSpace(row.Name))];
        List<MapDataTableCsvIssue> issues = [];
        List<(MapDataTableCsvRow Row, ushort MapId, string Name)> validRows = [];

        foreach (MapDataTableCsvRow row in normalizedRows)
        {
            if (row.HasExtraColumns)
            {
                issues.Add(new MapDataTableCsvIssue(
                    row.RowNumber,
                    MapDataTableCsvIssueSeverity.Warning,
                    "这一行有多余列，已临时合并到名称；保存后会写成合法 CSV。"));
            }

            string mapIdText = row.MapIdText.Trim().TrimStart('\uFEFF');
            if (!ushort.TryParse(mapIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort mapId) ||
                mapId == MapData.EmptyRegionId)
            {
                issues.Add(new MapDataTableCsvIssue(
                    row.RowNumber,
                    MapDataTableCsvIssueSeverity.Error,
                    $"地图 ID 无效。请输入 1 到 {ushort.MaxValue} 之间的整数。"));
                continue;
            }

            string name = row.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add(new MapDataTableCsvIssue(
                    row.RowNumber,
                    MapDataTableCsvIssueSeverity.Error,
                    "缺少地图名称。"));
                continue;
            }

            validRows.Add((row, mapId, name));
        }

        foreach (IGrouping<ushort, (MapDataTableCsvRow Row, ushort MapId, string Name)> duplicateGroup in
            validRows.GroupBy(static item => item.MapId).Where(static group => group.Count() > 1))
        {
            foreach ((MapDataTableCsvRow row, ushort _, string _) in duplicateGroup)
            {
                issues.Add(new MapDataTableCsvIssue(
                    row.RowNumber,
                    MapDataTableCsvIssueSeverity.Error,
                    $"地图 ID {duplicateGroup.Key} 重复。"));
            }
        }

        Dictionary<ushort, string> mapNames = [];
        if (!issues.Any(static issue => issue.Severity == MapDataTableCsvIssueSeverity.Error))
        {
            foreach ((_, ushort mapId, string name) in validRows.OrderBy(static item => item.MapId))
            {
                mapNames[mapId] = name;
            }
        }

        return new MapDataTableCsvDiagnosticResult(normalizedRows, issues, mapNames);
    }

    public static List<List<string>> ReadRecords(string csv)
    {
        return ReadRecordsWithDiagnostics(csv).Records;
    }

    private static CsvReadResult ReadRecordsWithDiagnostics(string csv)
    {
        List<List<string>> records = [];
        List<CsvReadIssue> issues = [];
        List<string> currentRecord = [];
        StringBuilder currentField = new();
        bool inQuotes = false;
        int openQuoteRecordIndex = -1;

        for (int i = 0; i < csv.Length; i++)
        {
            char ch = csv[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    if (!inQuotes)
                    {
                        openQuoteRecordIndex = records.Count;
                    }

                    inQuotes = !inQuotes;
                    if (!inQuotes)
                    {
                        openQuoteRecordIndex = -1;
                    }
                }

                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                currentRecord.Add(currentField.ToString());
                currentField.Clear();
                continue;
            }

            if ((ch == '\r' || ch == '\n') && !inQuotes)
            {
                currentRecord.Add(currentField.ToString());
                currentField.Clear();
                AddRecordIfNotEmpty(records, currentRecord);
                currentRecord = [];
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                continue;
            }

            currentField.Append(ch);
        }

        currentRecord.Add(currentField.ToString());
        AddRecordIfNotEmpty(records, currentRecord, force: inQuotes);
        if (inQuotes)
        {
            int recordIndex = openQuoteRecordIndex >= 0
                ? openQuoteRecordIndex
                : Math.Max(0, records.Count - 1);
            issues.Add(new CsvReadIssue(
                recordIndex,
                "CSV 引号没有闭合。请补上结尾双引号，或删除多余的开头双引号。"));
        }

        return new CsvReadResult(records, issues);
    }

    private static bool HasSimpleMapDataHeader(IReadOnlyList<List<string>> records)
    {
        return records.Count > 0 &&
            records[0].Count >= 2 &&
            string.Equals(NormalizeHeaderName(records[0][0]), "ID", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeHeaderName(records[0][1]), "Name", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeHeaderName(string header)
    {
        return header.Trim().TrimStart('\uFEFF');
    }

    private static bool AddRecordIfNotEmpty(List<List<string>> records, List<string> record, bool force = false)
    {
        if (!force)
        {
            if (record.Count == 0) return false;
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0])) return false;
        }

        records.Add(record);
        return true;
    }

    private static void AppendCsvField(StringBuilder builder, string value)
    {
        bool needsQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0;
        if (!needsQuotes)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        builder.Append('"');
    }

    private sealed record CsvReadResult(
        List<List<string>> Records,
        IReadOnlyList<CsvReadIssue> Issues);

    private sealed record CsvReadIssue(
        int RecordIndex,
        string Message);
}

internal sealed record MapDataTableCsvRow(
    int RowNumber,
    string MapIdText,
    string Name,
    bool HasExtraColumns = false);

internal sealed record MapDataTableCsvIssue(
    int RowNumber,
    MapDataTableCsvIssueSeverity Severity,
    string Message);

internal enum MapDataTableCsvIssueSeverity
{
    Warning,
    Error
}

internal sealed record MapDataTableCsvDiagnosticResult(
    IReadOnlyList<MapDataTableCsvRow> Rows,
    IReadOnlyList<MapDataTableCsvIssue> Issues,
    IReadOnlyDictionary<ushort, string> MapNames)
{
    public bool HasIssues => Issues.Count > 0;

    public bool HasErrors => Issues.Any(static issue => issue.Severity == MapDataTableCsvIssueSeverity.Error);
}
