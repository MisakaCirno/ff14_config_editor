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
            if (isRestoringWayMarkSelection)
            {
                UpdateMoveButtonState();
                return;
            }

            WayMark? selectedMark = WayMark_ListBox.SelectedItem as WayMark;
            WayMark? previousMark = currentWayMark;
            if (!ReferenceEquals(previousMark, selectedMark) &&
                previousMark != null &&
                !TryCommitPendingWayMarkEdits())
            {
                RestoreWayMarkSelection(previousMark);
                return;
            }

            ApplySelectedWayMark(selectedMark);
            UpdateMoveButtonState();
        }

        private void ApplySelectedWayMark(WayMark? selectedMark)
        {
            currentWayMark = selectedMark;
            WayMarkEditPanel_Control.SetWayMark(currentWayMark, GetLoadedRegionIds());
            WayMarkPreview_Control.SetWayMark(currentWayMark);
        }

        private void RestoreWayMarkSelection(WayMark previousMark)
        {
            isRestoringWayMarkSelection = true;
            try
            {
                WayMark_ListBox.SelectedItem = previousMark;
            }
            finally
            {
                isRestoringWayMarkSelection = false;
            }

            UpdateMoveButtonState();
        }

        private void UpdatePreview()
        {
            WayMarkPreview_Control.RefreshPreview();
        }

        private void ShareWebsite_Button_Click(object sender, RoutedEventArgs e)
        {
            string url = ExternalLinks.WayMarkSharePage;

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
                AppMessageBox.Show($"无法打开链接：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
