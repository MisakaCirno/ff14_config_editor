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
        private void Import_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (currentWayMark is not WayMark currentMark)
                {
                    AppMessageBox.Show("请先选择一个要导入到的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string json = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(json))
                {
                    AppMessageBox.Show("剪贴板内容为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MarkerShare? markerShare = JsonSerializer.Deserialize<MarkerShare>(json);
                if (markerShare == null)
                {
                    AppMessageBox.Show("无法解析剪贴板中的JSON数据。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!MarkerShareConverter.TryCreateValidatedImport(
                    markerShare,
                    MapData.GetKnownMapIds(),
                    out ValidatedMarkerShare importedMarker,
                    out string validationError))
                {
                    AppMessageBox.Show(validationError, "导入数据无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 所有导入数据校验通过后，再修改当前标点槽位。
                currentMark.RegionID = importedMarker.RegionID;
                RefreshRegionOptions([currentMark.RegionID]);
                SetRegionSearchText(currentMark.RegionID);

                static void UpdatePoint(WayMarkPoint point, ValidatedMarkerSharePoint importedPoint, Action<bool> setEnabled)
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

                // 更新时间戳。
                currentMark.timestamp = (int)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                NotifyWayMarksChanged();

                // 强制更新UI（如果需要）
                // 属性变更应该会自动通知UI

                AppMessageBox.Show("导入成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"导入失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Export_Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (currentWayMark is not WayMark currentMark)
                {
                    AppMessageBox.Show("请先选择一个要导出的标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                MarkerShare markerShare = MarkerShareConverter.CreateShare(currentMark, MapData.GetName);
                string json = JsonSerializer.Serialize(markerShare, jsonOptions);

                // 兜底方案，如果没法直接复制成功，弹出一个窗口让用户复制
                try
                {
                    Clipboard.SetText(json);
                    AppMessageBox.Show("导出成功！\nJSON数据已复制到剪贴板。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
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
                AppMessageBox.Show($"导出失败!\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
