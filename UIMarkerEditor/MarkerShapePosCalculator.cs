using System;
using System.Collections.Generic;
using System.Linq;

namespace UIMarkerEditor
{
    public class GamePosition
    {
        public double X, Y, Z;

        public GamePosition()
        {
            
        }

        public GamePosition(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    public static class MarkerShapePosCalculator
    {
        private static double GetLegFromHypotenuse(double hypotenuse)
        {
            if (hypotenuse <= 0)
                throw new ArgumentOutOfRangeException(nameof(hypotenuse), "斜边长度必须为正数");
            return hypotenuse / Math.Sqrt(2);
        }

        public static List<GamePosition> Circle(GamePosition centerPos, double r)
        {
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
    }
}
