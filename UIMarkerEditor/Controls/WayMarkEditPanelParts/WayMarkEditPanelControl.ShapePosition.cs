using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls
{
    public partial class WayMarkEditPanelControl
    {
        private void SetShapePos_Button_Click(object sender, RoutedEventArgs e)
        {
            if (currentWayMark == null)
            {
                MessageBox.Show("请先选择一个要设置的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryReadDouble(ShapeDistance_TextBox, "与中心点间距", out double distance) ||
                !TryReadDouble(ShapeCenterX_TextBox, "中心点 X", out double centerX) ||
                !TryReadDouble(ShapeCenterY_TextBox, "中心点 Y", out double centerY) ||
                !TryReadDouble(ShapeCenterZ_TextBox, "中心点 Z", out double centerZ))
            {
                return;
            }

            if (distance <= 0)
            {
                MessageBox.Show("与中心点间距必须大于 0。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GamePosition centerPos = new(centerX, centerY, centerZ);
            List<GamePosition> positions = PointShape_ComboBox.SelectedItem?.ToString() switch
            {
                "方形八方" => MarkerShapePosCalculator.Square(centerPos, distance),
                _ => MarkerShapePosCalculator.Circle(centerPos, distance)
            };

            string order = PointOrder_ComboBox.SelectedItem?.ToString() ?? "A1B2C3D4";
            string[] pointOrder = order switch
            {
                "A2B3C4D1" => ["A", "2", "B", "3", "C", "4", "D", "1"],
                _ => ["A", "1", "B", "2", "C", "3", "D", "4"]
            };

            List<(string PointName, RawGamePosition Position)> rawPositions = [];
            for (int i = 0; i < pointOrder.Length; i++)
            {
                if (!TryCreateRawGamePosition(positions[i], out RawGamePosition rawPosition))
                {
                    MessageBox.Show("生成的坐标超出可保存范围，请检查中心点和距离。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                rawPositions.Add((pointOrder[i], rawPosition));
            }

            foreach ((string pointName, RawGamePosition rawPosition) in rawPositions)
            {
                SetPointPosition(currentWayMark, pointName, rawPosition);
            }

            currentWayMark.AEnabled = true;
            currentWayMark.BEnabled = true;
            currentWayMark.CEnabled = true;
            currentWayMark.DEnabled = true;
            currentWayMark.OneEnabled = true;
            currentWayMark.TwoEnabled = true;
            currentWayMark.ThreeEnabled = true;
            currentWayMark.FourEnabled = true;
            currentWayMark.timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            UpdatePreview();
            NotifyWayMarksChanged();
        }

        private static bool TryReadDouble(TextBox textBox, string displayName, out double value)
        {
            string text = textBox.Text.Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                if (!double.IsFinite(value))
                {
                    MessageBox.Show($"{displayName} 需要填写有限数字。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                return true;
            }

            MessageBox.Show($"{displayName} 需要填写数字。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private static bool TryCreateRawGamePosition(GamePosition position, out RawGamePosition rawPosition)
        {
            rawPosition = default;
            if (!WayMarkCoordinateConverter.TryRoundToRawCoordinate(position.X, out int rawX) ||
                !WayMarkCoordinateConverter.TryRoundToRawCoordinate(position.Y, out int rawY) ||
                !WayMarkCoordinateConverter.TryRoundToRawCoordinate(position.Z, out int rawZ))
            {
                return false;
            }

            rawPosition = new RawGamePosition(rawX, rawY, rawZ);
            return true;
        }

        private static void SetPointPosition(WayMark wayMark, string pointName, RawGamePosition position)
        {
            WayMarkPoint point = pointName switch
            {
                "A" => wayMark.A,
                "B" => wayMark.B,
                "C" => wayMark.C,
                "D" => wayMark.D,
                "1" => wayMark.One,
                "2" => wayMark.Two,
                "3" => wayMark.Three,
                "4" => wayMark.Four,
                _ => throw new ArgumentOutOfRangeException(nameof(pointName), pointName, "未知标点名称")
            };

            point.X = position.X;
            point.Y = position.Y;
            point.Z = position.Z;
        }

        private readonly record struct RawGamePosition(int X, int Y, int Z);

    }
}
