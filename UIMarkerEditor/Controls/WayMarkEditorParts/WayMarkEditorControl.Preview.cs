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
        private WayMark? currentWayMark = null;

        private void WayMark_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WayMark_ListBox.SelectedItem is WayMark selectedMark)
            {
                currentWayMark = selectedMark;
                WayMarkEditPanel_Control.SetWayMark(currentWayMark, GetLoadedRegionIds());
                WayMarkPreview_Control.SetWayMark(currentWayMark);
            }
            else
            {
                currentWayMark = null;
                WayMarkEditPanel_Control.SetWayMark(null);
                WayMarkPreview_Control.SetWayMark(null);
            }

            UpdateMoveButtonState();
        }

        private void UpdatePreview()
        {
            WayMarkPreview_Control.RefreshPreview();
        }

        private void ShareWebsite_Button_Click(object sender, RoutedEventArgs e)
        {
            // 打开网站：https://souma.diemoe.net/ff14-overlay-vue/#/zoneMacro?OVERLAY_WS=ws://127.0.0.1:10501/ws&lang=zhCn
            string url = "https://souma.diemoe.net/ff14-overlay-vue/#/zoneMacro?OVERLAY_WS=ws://127.0.0.1:10501/ws&lang=zhCn";

            try
            {
                using Process? _ = Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
