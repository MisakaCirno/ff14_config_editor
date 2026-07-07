using System;
using System.Collections.Generic;
using System.Globalization;

namespace FF14ConfigEditor.UISave
{
    public static class MarkerShareConverter
    {
        private const int MinRawCoordinate = int.MinValue;
        private const int MaxRawCoordinate = int.MaxValue;
        private const int CoordinateScale = 1000;

        public static bool TryCreateValidatedImport(
            MarkerShare? markerShare,
            IReadOnlySet<ushort> knownMapIds,
            out ValidatedMarkerShare importedMarker,
            out string errorMessage)
        {
            return TryCreateValidatedImport(
                markerShare,
                knownMapIds,
                allowUnknownMapId: false,
                preservedUnknownMapIds: null,
                out importedMarker,
                out errorMessage);
        }

        public static bool TryCreateValidatedImport(
            MarkerShare? markerShare,
            IReadOnlySet<ushort> knownMapIds,
            bool allowUnknownMapId,
            IReadOnlySet<ushort>? preservedUnknownMapIds,
            out ValidatedMarkerShare importedMarker,
            out string errorMessage)
        {
            ArgumentNullException.ThrowIfNull(knownMapIds);

            importedMarker = default;
            errorMessage = string.Empty;

            if (markerShare is null)
            {
                errorMessage = "缺少标点分享数据。";
                return false;
            }

            if (!markerShare.MapID.HasValue)
            {
                errorMessage = "缺少地图 ID。";
                return false;
            }

            int mapID = markerShare.MapID.Value;
            if (mapID < ushort.MinValue || mapID > ushort.MaxValue)
            {
                errorMessage = $"地图 ID 超出可保存范围：{mapID}。";
                return false;
            }

            if (mapID == 0 && !allowUnknownMapId)
            {
                errorMessage = "地图 ID 不能为 0。";
                return false;
            }

            ushort regionId = (ushort)mapID;
            if (allowUnknownMapId)
            {
                return TryCreateValidatedImportPoints(
                    markerShare,
                    regionId,
                    out importedMarker,
                    out errorMessage);
            }

            if (preservedUnknownMapIds?.Contains(regionId) == true)
            {
                return TryCreateValidatedImportPoints(
                    markerShare,
                    regionId,
                    out importedMarker,
                    out errorMessage);
            }

            if (knownMapIds.Count == 0)
            {
                errorMessage = "当前地图数据未加载，无法校验地图 ID。请先检查地图数据，或开启“允许未知地图 ID”后自行确认地图 ID。";
                return false;
            }

            if (!knownMapIds.Contains(regionId))
            {
                errorMessage = $"地图 ID 不存在于当前地图数据：{mapID}。如确认该 ID 有效，可开启“允许未知地图 ID”后导入。";
                return false;
            }

            return TryCreateValidatedImportPoints(
                markerShare,
                regionId,
                out importedMarker,
                out errorMessage);
        }

        private static bool TryCreateValidatedImportPoints(
            MarkerShare markerShare,
            ushort regionId,
            out ValidatedMarkerShare importedMarker,
            out string errorMessage)
        {
            importedMarker = default;

            if (!TryCreateImportedPoint("A", markerShare.A, out ValidatedMarkerSharePoint a, out errorMessage) ||
                !TryCreateImportedPoint("B", markerShare.B, out ValidatedMarkerSharePoint b, out errorMessage) ||
                !TryCreateImportedPoint("C", markerShare.C, out ValidatedMarkerSharePoint c, out errorMessage) ||
                !TryCreateImportedPoint("D", markerShare.D, out ValidatedMarkerSharePoint d, out errorMessage) ||
                !TryCreateImportedPoint("1", markerShare.One, out ValidatedMarkerSharePoint one, out errorMessage) ||
                !TryCreateImportedPoint("2", markerShare.Two, out ValidatedMarkerSharePoint two, out errorMessage) ||
                !TryCreateImportedPoint("3", markerShare.Three, out ValidatedMarkerSharePoint three, out errorMessage) ||
                !TryCreateImportedPoint("4", markerShare.Four, out ValidatedMarkerSharePoint four, out errorMessage))
            {
                return false;
            }

            importedMarker = new ValidatedMarkerShare(
                regionId,
                a,
                b,
                c,
                d,
                one,
                two,
                three,
                four);
            return true;
        }

        public static MarkerShare CreateShare(WayMark wayMark, Func<ushort, string>? resolveMapName = null)
        {
            ArgumentNullException.ThrowIfNull(wayMark);
            ThrowIfPointNull(wayMark.A, nameof(wayMark.A));
            ThrowIfPointNull(wayMark.B, nameof(wayMark.B));
            ThrowIfPointNull(wayMark.C, nameof(wayMark.C));
            ThrowIfPointNull(wayMark.D, nameof(wayMark.D));
            ThrowIfPointNull(wayMark.One, nameof(wayMark.One));
            ThrowIfPointNull(wayMark.Two, nameof(wayMark.Two));
            ThrowIfPointNull(wayMark.Three, nameof(wayMark.Three));
            ThrowIfPointNull(wayMark.Four, nameof(wayMark.Four));

            return new MarkerShare
            {
                MapID = wayMark.RegionID,
                Name = resolveMapName?.Invoke(wayMark.RegionID) ?? string.Empty,
                A = CreatePoint(wayMark.A, wayMark.AEnabled),
                B = CreatePoint(wayMark.B, wayMark.BEnabled),
                C = CreatePoint(wayMark.C, wayMark.CEnabled),
                D = CreatePoint(wayMark.D, wayMark.DEnabled),
                One = CreatePoint(wayMark.One, wayMark.OneEnabled),
                Two = CreatePoint(wayMark.Two, wayMark.TwoEnabled),
                Three = CreatePoint(wayMark.Three, wayMark.ThreeEnabled),
                Four = CreatePoint(wayMark.Four, wayMark.FourEnabled)
            };
        }

        private static bool TryCreateImportedPoint(
            string pointName,
            MarkerSharePoint? sharePoint,
            out ValidatedMarkerSharePoint importedPoint,
            out string errorMessage)
        {
            importedPoint = default;
            errorMessage = string.Empty;
            if (sharePoint == null)
            {
                errorMessage = $"缺少 {pointName} 点数据。";
                return false;
            }

            if (!TryConvertImportedCoordinate(pointName, "X", sharePoint.X, out int rawX, out errorMessage) ||
                !TryConvertImportedCoordinate(pointName, "Y", sharePoint.Y, out int rawY, out errorMessage) ||
                !TryConvertImportedCoordinate(pointName, "Z", sharePoint.Z, out int rawZ, out errorMessage))
            {
                return false;
            }

            if (!sharePoint.Active.HasValue)
            {
                errorMessage = $"缺少 {pointName} 点启用状态。";
                return false;
            }

            importedPoint = new ValidatedMarkerSharePoint(rawX, rawY, rawZ, sharePoint.Active.Value);
            return true;
        }

        private static bool TryConvertImportedCoordinate(
            string pointName,
            string axisName,
            double? value,
            out int rawCoordinate,
            out string errorMessage)
        {
            rawCoordinate = 0;
            errorMessage = string.Empty;
            if (!value.HasValue)
            {
                errorMessage = $"缺少 {pointName} 点 {axisName} 坐标。";
                return false;
            }

            if (!double.IsFinite(value.Value))
            {
                errorMessage = $"{pointName} 点 {axisName} 坐标不是有效数字。";
                return false;
            }

            decimal decimalValue;
            try
            {
                decimalValue = (decimal)value.Value;
            }
            catch (OverflowException)
            {
                errorMessage = $"{pointName} 点 {axisName} 坐标超出可保存范围：{FormatCoordinateForMessage(value.Value)}。";
                return false;
            }

            decimal rawValue = decimalValue * CoordinateScale;
            if (rawValue < MinRawCoordinate || rawValue > MaxRawCoordinate)
            {
                errorMessage = $"{pointName} 点 {axisName} 坐标超出可保存范围：{FormatCoordinateForMessage(value.Value)}。";
                return false;
            }

            // 分享码导入要求坐标能按 3 位小数精确保存，不在这里做取整。
            if (rawValue != decimal.Truncate(rawValue))
            {
                errorMessage = $"{pointName} 点 {axisName} 坐标最多支持 3 位小数：{FormatCoordinateForMessage(value.Value)}。";
                return false;
            }

            rawCoordinate = (int)rawValue;
            return true;
        }

        private static MarkerSharePoint CreatePoint(WayMarkPoint point, bool active)
        {
            return new MarkerSharePoint
            {
                X = RawToShareCoordinate(point.X),
                Y = RawToShareCoordinate(point.Y),
                Z = RawToShareCoordinate(point.Z),
                Active = active
            };
        }

        private static void ThrowIfPointNull(WayMarkPoint? point, string pointName)
            => ArgumentNullException.ThrowIfNull(point, pointName);

        private static double RawToShareCoordinate(int rawCoordinate)
        {
            return rawCoordinate / (double)CoordinateScale;
        }

        private static string FormatCoordinateForMessage(double value)
        {
            return value.ToString("G15", CultureInfo.InvariantCulture);
        }
    }
}
