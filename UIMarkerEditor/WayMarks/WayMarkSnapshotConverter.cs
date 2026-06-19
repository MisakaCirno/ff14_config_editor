using FF14ConfigEditor.UISave;

namespace UIMarkerEditor;

internal static class WayMarkSnapshotConverter
{
    public static WayMarkSnapshot CreateSnapshot(WayMark wayMark)
    {
        ArgumentNullException.ThrowIfNull(wayMark);

        return new WayMarkSnapshot
        {
            RegionID = wayMark.RegionID,
            EnableFlag = wayMark.enableFlag,
            Unknown = wayMark.unknown,
            Timestamp = wayMark.timestamp,
            A = CreatePointSnapshot(wayMark.A),
            B = CreatePointSnapshot(wayMark.B),
            C = CreatePointSnapshot(wayMark.C),
            D = CreatePointSnapshot(wayMark.D),
            One = CreatePointSnapshot(wayMark.One),
            Two = CreatePointSnapshot(wayMark.Two),
            Three = CreatePointSnapshot(wayMark.Three),
            Four = CreatePointSnapshot(wayMark.Four)
        };
    }

    public static WayMarkSnapshot Clone(WayMarkSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return new WayMarkSnapshot();
        }

        return new WayMarkSnapshot
        {
            RegionID = snapshot.RegionID,
            EnableFlag = snapshot.EnableFlag,
            Unknown = snapshot.Unknown,
            Timestamp = snapshot.Timestamp,
            A = ClonePoint(snapshot.A),
            B = ClonePoint(snapshot.B),
            C = ClonePoint(snapshot.C),
            D = ClonePoint(snapshot.D),
            One = ClonePoint(snapshot.One),
            Two = ClonePoint(snapshot.Two),
            Three = ClonePoint(snapshot.Three),
            Four = ClonePoint(snapshot.Four)
        };
    }

    public static WayMarkFavorite CloneFavorite(WayMarkFavorite favorite)
    {
        ArgumentNullException.ThrowIfNull(favorite);

        return new WayMarkFavorite
        {
            Id = favorite.Id,
            CommentName = favorite.CommentName,
            CreatedAt = favorite.CreatedAt,
            UpdatedAt = favorite.UpdatedAt,
            Marker = Clone(favorite.Marker)
        };
    }

    public static WayMark CreateWayMark(WayMarkSnapshot snapshot)
    {
        WayMark wayMark = new();
        ApplyToWayMark(wayMark, snapshot, updateTimestamp: false);
        return wayMark;
    }

    public static void ApplyToWayMark(WayMark wayMark, WayMarkSnapshot snapshot, bool updateTimestamp)
    {
        ArgumentNullException.ThrowIfNull(wayMark);
        WayMarkSnapshot copy = Clone(snapshot);

        wayMark.RegionID = copy.RegionID;
        wayMark.unknown = copy.Unknown;
        wayMark.timestamp = updateTimestamp
            ? (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            : copy.Timestamp;

        ApplyPoint(wayMark.A, copy.A);
        ApplyPoint(wayMark.B, copy.B);
        ApplyPoint(wayMark.C, copy.C);
        ApplyPoint(wayMark.D, copy.D);
        ApplyPoint(wayMark.One, copy.One);
        ApplyPoint(wayMark.Two, copy.Two);
        ApplyPoint(wayMark.Three, copy.Three);
        ApplyPoint(wayMark.Four, copy.Four);

        wayMark.AEnabled = IsEnabled(copy.EnableFlag, 0x01);
        wayMark.BEnabled = IsEnabled(copy.EnableFlag, 0x02);
        wayMark.CEnabled = IsEnabled(copy.EnableFlag, 0x04);
        wayMark.DEnabled = IsEnabled(copy.EnableFlag, 0x08);
        wayMark.OneEnabled = IsEnabled(copy.EnableFlag, 0x10);
        wayMark.TwoEnabled = IsEnabled(copy.EnableFlag, 0x20);
        wayMark.ThreeEnabled = IsEnabled(copy.EnableFlag, 0x40);
        wayMark.FourEnabled = IsEnabled(copy.EnableFlag, 0x80);
    }

    public static int CountEnabledPoints(WayMarkSnapshot? snapshot)
    {
        if (snapshot == null) return 0;

        int count = 0;
        byte flag = snapshot.EnableFlag;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((flag & (1 << bit)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static WayMarkPointSnapshot CreatePointSnapshot(WayMarkPoint point)
    {
        return new WayMarkPointSnapshot
        {
            X = point.X,
            Y = point.Y,
            Z = point.Z
        };
    }

    private static WayMarkPointSnapshot ClonePoint(WayMarkPointSnapshot? point)
    {
        if (point == null)
        {
            return new WayMarkPointSnapshot();
        }

        return new WayMarkPointSnapshot
        {
            X = point.X,
            Y = point.Y,
            Z = point.Z
        };
    }

    private static void ApplyPoint(WayMarkPoint target, WayMarkPointSnapshot source)
    {
        target.X = source.X;
        target.Y = source.Y;
        target.Z = source.Z;
    }

    private static bool IsEnabled(byte enableFlag, byte mask)
    {
        return (enableFlag & mask) != 0;
    }
}
