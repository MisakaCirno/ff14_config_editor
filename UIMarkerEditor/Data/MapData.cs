namespace UIMarkerEditor;

public class MapData
{
    public const ushort EmptyRegionId = 0;
    public const string EmptyRegionName = "空";
    private const string UnknownRegionName = "未知地点";
    private static Dictionary<ushort, string> indexToName = [];

    public static string GetName(ushort index)
    {
        if (index == EmptyRegionId) return EmptyRegionName;

        return indexToName.TryGetValue(index, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : UnknownRegionName;
    }

    public static Dictionary<ushort, MapData> GetMapDataDisplayDict()
    {
        Dictionary<ushort, MapData> result = new()
        {
            [EmptyRegionId] = new MapData(EmptyRegionId, EmptyRegionName)
        };
        foreach (KeyValuePair<ushort, string> kvp in indexToName)
        {
            if (kvp.Key == EmptyRegionId) continue;

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
        Name = index == EmptyRegionId
            ? EmptyRegionName
            : string.IsNullOrWhiteSpace(name) ? UnknownRegionName : name;
        DisplayName = $"{Name}({index})";
    }

    public ushort Index { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
}
