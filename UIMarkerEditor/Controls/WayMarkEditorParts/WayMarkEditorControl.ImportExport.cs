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
    public partial class WayMarkEditorControl
    {
        private void Import_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WayMark_ListBox.SelectedItem is not WayMark currentMark)
                {
                    MessageBox.Show("请先选择一个要导入到的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string json = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(json))
                {
                    MessageBox.Show("剪贴板内容为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MarkerShare? markerShare = JsonSerializer.Deserialize<MarkerShare>(json);
                if (markerShare == null)
                {
                    MessageBox.Show("无法解析剪贴板中的JSON数据。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!TryCreateValidatedImport(markerShare, out ImportedMarker importedMarker, out string validationError))
                {
                    MessageBox.Show(validationError, "导入数据无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Update currentMark only after all imported data has been validated.
                currentMark.RegionID = importedMarker.RegionID;
                RefreshRegionOptions(GetLoadedRegionIds());
                SetRegionSearchText(currentMark.RegionID);

                static void UpdatePoint(WayMarkPoint point, ImportedMarkerPoint importedPoint, Action<bool> setEnabled)
                {
                    point.X = importedPoint.RawX;
                    point.Y = importedPoint.RawY;
                    point.Z = importedPoint.RawZ;
                    setEnabled(importedPoint.Active);
                }

                UpdatePoint(currentMark.A, importedMarker.A, val => currentMark.AEnabled = val);
                UpdatePoint(currentMark.B, importedMarker.B, val => currentMark.BEnabled = val);
                UpdatePoint(currentMark.C, importedMarker.C, val => currentMark.CEnabled = val);
                UpdatePoint(currentMark.D, importedMarker.D, val => currentMark.DEnabled = val);
                UpdatePoint(currentMark.One, importedMarker.One, val => currentMark.OneEnabled = val);
                UpdatePoint(currentMark.Two, importedMarker.Two, val => currentMark.TwoEnabled = val);
                UpdatePoint(currentMark.Three, importedMarker.Three, val => currentMark.ThreeEnabled = val);
                UpdatePoint(currentMark.Four, importedMarker.Four, val => currentMark.FourEnabled = val);

                // Update timestamp
                currentMark.timestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

                // 强制更新UI（如果需要）
                // 属性变更应该会自动通知UI

                MessageBox.Show("导入成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool TryCreateValidatedImport(MarkerShare markerShare, out ImportedMarker importedMarker, out string errorMessage)
        {
            importedMarker = default;
            errorMessage = string.Empty;

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

            if (!TryCreateImportedPoint("A", markerShare.A, out ImportedMarkerPoint a, out errorMessage) ||
                !TryCreateImportedPoint("B", markerShare.B, out ImportedMarkerPoint b, out errorMessage) ||
                !TryCreateImportedPoint("C", markerShare.C, out ImportedMarkerPoint c, out errorMessage) ||
                !TryCreateImportedPoint("D", markerShare.D, out ImportedMarkerPoint d, out errorMessage) ||
                !TryCreateImportedPoint("1", markerShare.One, out ImportedMarkerPoint one, out errorMessage) ||
                !TryCreateImportedPoint("2", markerShare.Two, out ImportedMarkerPoint two, out errorMessage) ||
                !TryCreateImportedPoint("3", markerShare.Three, out ImportedMarkerPoint three, out errorMessage) ||
                !TryCreateImportedPoint("4", markerShare.Four, out ImportedMarkerPoint four, out errorMessage))
            {
                return false;
            }

            importedMarker = new ImportedMarker(
                (ushort)mapID,
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

        private static bool TryCreateImportedPoint(
            string pointName,
            MarkerSharePoint? sharePoint,
            out ImportedMarkerPoint importedPoint,
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

            importedPoint = new ImportedMarkerPoint(rawX, rawY, rawZ, sharePoint.Active);
            return true;
        }

        private static bool TryConvertImportedCoordinate(
            string pointName,
            string axisName,
            double value,
            out int rawCoordinate,
            out string errorMessage)
        {
            rawCoordinate = 0;
            errorMessage = string.Empty;
            if (!double.IsFinite(value))
            {
                errorMessage = $"{pointName} 点 {axisName} 坐标不是有效数字。";
                return false;
            }

            decimal decimalValue;
            try
            {
                decimalValue = (decimal)value;
            }
            catch (OverflowException)
            {
                errorMessage = $"{pointName} 点 {axisName} 坐标超出可保存范围：{value}。";
                return false;
            }

            decimal rawValue = decimalValue * CoordinateScale;
            if (rawValue < MinRawCoordinate || rawValue > MaxRawCoordinate)
            {
                errorMessage = $"{pointName} 点 {axisName} 坐标超出可保存范围：{value}。";
                return false;
            }

            rawCoordinate = (int)rawValue;
            return true;
        }

        private readonly record struct ImportedMarker(
            ushort RegionID,
            ImportedMarkerPoint A,
            ImportedMarkerPoint B,
            ImportedMarkerPoint C,
            ImportedMarkerPoint D,
            ImportedMarkerPoint One,
            ImportedMarkerPoint Two,
            ImportedMarkerPoint Three,
            ImportedMarkerPoint Four);

        private readonly record struct ImportedMarkerPoint(int RawX, int RawY, int RawZ, bool Active);

        private void Export_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (WayMark_ListBox.SelectedItem is not WayMark currentMark)
                {
                    MessageBox.Show("请先选择一个要导出的标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MarkerShare markerShare = new()
                {
                    MapID = currentMark.RegionID,
                    Name = MapData.GetName(currentMark.RegionID)
                };

                static MarkerSharePoint CreatePoint(WayMarkPoint point, bool active)
                {
                    return new MarkerSharePoint
                    {
                        X = double.Parse(FormatCoordinate(point.FloatX)),
                        Y = double.Parse(FormatCoordinate(point.FloatY)),
                        Z = double.Parse(FormatCoordinate(point.FloatZ)),
                        Active = active
                    };
                }

                markerShare.A = CreatePoint(currentMark.A, currentMark.AEnabled);
                markerShare.B = CreatePoint(currentMark.B, currentMark.BEnabled);
                markerShare.C = CreatePoint(currentMark.C, currentMark.CEnabled);
                markerShare.D = CreatePoint(currentMark.D, currentMark.DEnabled);
                markerShare.One = CreatePoint(currentMark.One, currentMark.OneEnabled);
                markerShare.Two = CreatePoint(currentMark.Two, currentMark.TwoEnabled);
                markerShare.Three = CreatePoint(currentMark.Three, currentMark.ThreeEnabled);
                markerShare.Four = CreatePoint(currentMark.Four, currentMark.FourEnabled);

                string json = JsonSerializer.Serialize(markerShare, jsonOptions);

                // 兜底方案，如果没法直接复制成功，弹出一个窗口让用户复制
                try
                {
                    Clipboard.SetText(json);
                    MessageBox.Show("导出成功！\nJSON数据已复制到剪贴板。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch
                {
                    // 弹出窗口
                    Window copyWindow = new()
                    {
                        Title = "复制标点数据",
                        Width = 400,
                        Height = 300,
                        Content = new TextBox
                        {
                            Text = json,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                        },
                        // 设置窗口所有者和启动位置，确保弹出窗口在主窗口中央
                        Owner = Window.GetWindow(this),
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    };

                    copyWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败!\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
