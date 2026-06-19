namespace UIMarkerEditor;

internal static class WayMarkRegionSort
{
    public static int GetZeroLastBucket(ushort regionId)
    {
        return regionId == 0 ? 1 : 0;
    }
}
