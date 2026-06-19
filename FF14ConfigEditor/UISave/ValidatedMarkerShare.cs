namespace FF14ConfigEditor.UISave
{
    public readonly record struct ValidatedMarkerShare(
        ushort RegionID,
        ValidatedMarkerSharePoint A,
        ValidatedMarkerSharePoint B,
        ValidatedMarkerSharePoint C,
        ValidatedMarkerSharePoint D,
        ValidatedMarkerSharePoint One,
        ValidatedMarkerSharePoint Two,
        ValidatedMarkerSharePoint Three,
        ValidatedMarkerSharePoint Four);

    public readonly record struct ValidatedMarkerSharePoint(int RawX, int RawY, int RawZ, bool Active);
}
