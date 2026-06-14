namespace UIMarkerEditor;

public class MapData
{
    private static Dictionary<ushort, string> indexToName = [];

    public static string GetName(ushort index)
    {
        return indexToName.TryGetValue(index, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : "未知地点";
    }

    public static Dictionary<ushort, MapData> GetMapDataDisplayDict()
    {
        Dictionary<ushort, MapData> result = [];
        foreach (KeyValuePair<ushort, string> kvp in indexToName)
        {
            result[kvp.Key] = new MapData(kvp.Key, kvp.Value);
        }

        return result;
    }

    public static IReadOnlySet<ushort> GetKnownMapIds()
    {
        return indexToName.Keys.ToHashSet();
    }

    public static IEnumerable<MapData> CachedDisplayDictValues => GetMapDataDisplayDict().Values;

    public static bool HasData => indexToName.Count > 0;

    public static void ApplyMapNames(IReadOnlyDictionary<ushort, string> mapNames)
    {
        indexToName = mapNames
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public static void Clear()
    {
        indexToName = [];
    }

    public MapData(ushort index, string name)
    {
        Index = index;
        Name = string.IsNullOrWhiteSpace(name) ? "未知地点" : name;
        DisplayName = $"{Name}({index})";
    }

    public ushort Index { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
}
