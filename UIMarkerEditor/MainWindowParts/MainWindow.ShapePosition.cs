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

namespace UIMarkerEditor
{
    public partial class MainWindow
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

            for (int i = 0; i < pointOrder.Length; i++)
            {
                SetPointPosition(currentWayMark, pointOrder[i], positions[i]);
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
        }

        private static bool TryReadDouble(TextBox textBox, string displayName, out double value)
        {
            string text = textBox.Text.Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            MessageBox.Show($"{displayName} 需要填写数字。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private static void SetPointPosition(WayMark wayMark, string pointName, GamePosition position)
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

            point.X = ToRawCoordinate(position.X);
            point.Y = ToRawCoordinate(position.Y);
            point.Z = ToRawCoordinate(position.Z);
        }

        private static int ToRawCoordinate(double value)
        {
            return (int)Math.Round(value * 1000, MidpointRounding.AwayFromZero);
        }

    }
}