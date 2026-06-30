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

    public static List<List<string>> ReadRecords(string csv)
    {
        List<List<string>> records = [];
        List<string> currentRecord = [];
        StringBuilder currentField = new();
        bool inQuotes = false;

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
                    inQuotes = !inQuotes;
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
        AddRecordIfNotEmpty(records, currentRecord);
        return records;
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

    private static void AddRecordIfNotEmpty(List<List<string>> records, List<string> record)
    {
        if (record.Count == 0) return;
        if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0])) return;

        records.Add(record);
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
}
