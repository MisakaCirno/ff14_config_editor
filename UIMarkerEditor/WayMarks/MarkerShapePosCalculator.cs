using System;
using System.Collections.Generic;

namespace UIMarkerEditor
{
    public static class MarkerShapePosCalculator
    {
        private static double NormalizeDistance(double distance)
        {
            return Math.Max(0, distance);
        }

        private static double GetLegFromHypotenuse(double hypotenuse)
        {
            return NormalizeDistance(hypotenuse) / Math.Sqrt(2);
        }

        public static List<GamePosition> Circle(GamePosition centerPos, double r)
        {
            r = NormalizeDistance(r);
            // 直角边长度
            double leg = GetLegFromHypotenuse(r);

            // 平面八个方向，从正北开始顺时针排列；北为 Z-，东为 X+。
            // 北
            GamePosition n = new(centerPos.X, centerPos.Y, centerPos.Z - r);
            // 东北
            GamePosition ne = new(centerPos.X + leg, centerPos.Y, centerPos.Z - leg);
            // 东
            GamePosition e = new(centerPos.X + r, centerPos.Y, centerPos.Z);
            // 东南
            GamePosition se = new(centerPos.X + leg, centerPos.Y, centerPos.Z + leg);
            // 南
            GamePosition s = new(centerPos.X, centerPos.Y, centerPos.Z + r);
            // 西南
            GamePosition sw = new(centerPos.X - leg, centerPos.Y, centerPos.Z + leg);
            // 西
            GamePosition w = new(centerPos.X - r, centerPos.Y, centerPos.Z);
            // 西北
            GamePosition nw = new(centerPos.X - leg, centerPos.Y, centerPos.Z - leg);

            List<GamePosition> result = new()
            {
                n,
                ne,
                e,
                se,
                s,
                sw,
                w,
                nw
            };
            return result;
        }

        public static List<GamePosition> Square(GamePosition centerPos, double distance)
        {
            distance = NormalizeDistance(distance);
            // 平面八个方向，从正北开始顺时针排列；北为 Z-，东为 X+。
            // 北
            GamePosition n = new(centerPos.X, centerPos.Y, centerPos.Z - distance);
            // 东北
            GamePosition ne = new(centerPos.X + distance, centerPos.Y, centerPos.Z - distance);
            // 东
            GamePosition e = new(centerPos.X + distance, centerPos.Y, centerPos.Z);
            // 东南
            GamePosition se = new(centerPos.X + distance, centerPos.Y, centerPos.Z + distance);
            // 南
            GamePosition s = new(centerPos.X, centerPos.Y, centerPos.Z + distance);
            // 西南
            GamePosition sw = new(centerPos.X - distance, centerPos.Y, centerPos.Z + distance);
            // 西
            GamePosition w = new(centerPos.X - distance, centerPos.Y, centerPos.Z);
            // 西北
            GamePosition nw = new(centerPos.X - distance, centerPos.Y, centerPos.Z - distance);

            List<GamePosition> result = new()
            {
                n,
                ne,
                e,
                se,
                s,
                sw,
                w,
                nw
            };
            return result;
        }

        public static List<GamePosition> Diamond(GamePosition centerPos, double distance)
        {
            distance = NormalizeDistance(distance);
            double halfDistance = distance / 2;

            // 平面八个方向，从正北开始顺时针排列；北为 Z-，东为 X+。
            // 北
            GamePosition n = new(centerPos.X, centerPos.Y, centerPos.Z - distance);
            // 东北
            GamePosition ne = new(centerPos.X + halfDistance, centerPos.Y, centerPos.Z - halfDistance);
            // 东
            GamePosition e = new(centerPos.X + distance, centerPos.Y, centerPos.Z);
            // 东南
            GamePosition se = new(centerPos.X + halfDistance, centerPos.Y, centerPos.Z + halfDistance);
            // 南
            GamePosition s = new(centerPos.X, centerPos.Y, centerPos.Z + distance);
            // 西南
            GamePosition sw = new(centerPos.X - halfDistance, centerPos.Y, centerPos.Z + halfDistance);
            // 西
            GamePosition w = new(centerPos.X - distance, centerPos.Y, centerPos.Z);
            // 西北
            GamePosition nw = new(centerPos.X - halfDistance, centerPos.Y, centerPos.Z - halfDistance);

            List<GamePosition> result = new()
            {
                n,
                ne,
                e,
                se,
                s,
                sw,
                w,
                nw
            };
            return result;
        }
    }
}
