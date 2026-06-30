namespace UIMarkerEditor;

public class MapData
{
    public const ushort EmptyRegionId = 0;
    public const string EmptyRegionName = "空";
    public const string UnavailableRegionName = "暂无名称";
    private static Dictionary<ushort, string> indexToName = [];
    private static bool showMapNames;

    public static string GetName(ushort index)
    {
        if (index == EmptyRegionId) return EmptyRegionName;

        if (!showMapNames)
        {
            return FormatMapIdName(index);
        }

        return indexToName.TryGetValue(index, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : UnavailableRegionName;
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

    public static bool IsNameDisplayEnabled => showMapNames;

    public static void ApplyMapNames(IReadOnlyDictionary<ushort, string> mapNames)
    {
        indexToName = mapNames
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        showMapNames = true;
    }

    public static void Clear(bool hideNames = false)
    {
        indexToName = [];
        showMapNames = !hideNames;
    }

    public static void DisableNameDisplay()
    {
        Clear(hideNames: true);
    }

    public static string FormatMapIdName(ushort index)
    {
        return $"地图 ID {index}";
    }

    public static string GetDisplayName(ushort index)
    {
        if (index == EmptyRegionId) return $"{EmptyRegionName}({index})";

        return !showMapNames
            ? $"{index}"
            : $"{GetName(index)}({index})";
    }

    public MapData(ushort index, string name)
    {
        Index = index;
        Name = index == EmptyRegionId
            ? EmptyRegionName
            : string.IsNullOrWhiteSpace(name) ? UnavailableRegionName : name;
        DisplayName = GetDisplayName(index);
    }

    public ushort Index { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
}
