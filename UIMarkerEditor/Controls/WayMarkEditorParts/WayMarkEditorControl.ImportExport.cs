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

                // Update currentMark
                currentMark.RegionID = (ushort)markerShare.MapID;
                RefreshRegionOptions(GetLoadedRegionIds());
                SetRegionSearchText(currentMark.RegionID);

                static void UpdatePoint(WayMarkPoint point, MarkerSharePoint sharePoint, Action<bool> setEnabled)
                {
                    point.FloatX = (float)sharePoint.X;
                    point.FloatY = (float)sharePoint.Y;
                    point.FloatZ = (float)sharePoint.Z;
                    setEnabled(sharePoint.Active);
                }

                UpdatePoint(currentMark.A, markerShare.A, val => currentMark.AEnabled = val);
                UpdatePoint(currentMark.B, markerShare.B, val => currentMark.BEnabled = val);
                UpdatePoint(currentMark.C, markerShare.C, val => currentMark.CEnabled = val);
                UpdatePoint(currentMark.D, markerShare.D, val => currentMark.DEnabled = val);
                UpdatePoint(currentMark.One, markerShare.One, val => currentMark.OneEnabled = val);
                UpdatePoint(currentMark.Two, markerShare.Two, val => currentMark.TwoEnabled = val);
                UpdatePoint(currentMark.Three, markerShare.Three, val => currentMark.ThreeEnabled = val);
                UpdatePoint(currentMark.Four, markerShare.Four, val => currentMark.FourEnabled = val);

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
