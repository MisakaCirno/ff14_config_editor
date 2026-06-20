using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls;

public partial class WayMarkEditorControl : UserControl
{
    private Point dragStartPoint;
    private WayMark? draggedWayMark;
    private int currentDropTargetIndex = -1;
    private bool suppressWayMarkListDragUntilLeftButtonReleased;
    private bool isWayMarkContextMenuOpen;
    private bool isWatchingWayMarkListDragSuppressionRelease;
    private List<WayMark>? wayMarks;
    public event EventHandler? WayMarksChanged;

    public WayMarkEditorControl()
    {
        InitializeComponent();
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(WayMarkEditorControl_PreviewKeyDown), true);
        Unloaded += (_, _) => StopWatchingWayMarkListDragSuppressionRelease();
        WayMarkEditPanel_Control.WayMarkChanged += (_, _) =>
        {
            WayMark_ListBox.Items.Refresh();
            UpdatePreview();
            NotifyWayMarksChanged();
        };
        UpdateMoveButtonState();
    }

    public void SetLoadingOverlayVisible(bool isVisible)
    {
        LoadingOverlay_Grid.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        bool isContentEnabled = !isVisible;
        WayMark_ListBox.IsEnabled = isContentEnabled;
        ShareWebsite_Button.IsEnabled = isContentEnabled;
        MoveUp_Button.IsEnabled = isContentEnabled;
        MoveDown_Button.IsEnabled = isContentEnabled;
        WayMarkEditPanel_Control.IsEnabled = isContentEnabled;
        WayMarkPreview_Control.IsEnabled = isContentEnabled;

        if (isVisible)
        {
            if (WayMark_ListBox.ContextMenu != null)
            {
                WayMark_ListBox.ContextMenu.IsOpen = false;
            }

            Keyboard.ClearFocus();
            LoadingOverlay_Grid.Focus();
        }
        else
        {
            UpdateMoveButtonState();
        }
    }

    public void UpdateDataVersionText(string mapDataVersion)
    {
        string versionText = string.IsNullOrWhiteSpace(mapDataVersion)
            ? "未知"
            : mapDataVersion;
        DataVersion_TextBlock.Text = $"当前版本：{versionText}";
    }

    public void RefreshMapDataDisplay()
    {
        WayMarkEditPanel_Control.RefreshMapDataDisplay(wayMarks?.Select(mark => mark.RegionID));
        WayMark_ListBox.Items.Refresh();
    }

    public void SetWayMarks(List<WayMark> markerSection)
    {
        wayMarks = markerSection;
        WayMarkEditPanel_Control.RefreshMapDataDisplay(markerSection.Select(mark => mark.RegionID));
        WayMark_ListBox.ItemsSource = markerSection;
        UpdateMoveButtonState();
    }

    public void ClearWayMarks()
    {
        wayMarks = null;
        currentWayMark = null;
        WayMark_ListBox.ItemsSource = null;
        WayMarkEditPanel_Control.SetWayMark(null);
        WayMarkPreview_Control.SetWayMark(null);
        UpdateMoveButtonState();
    }

    public void ApplyLayoutSettings(WindowLayoutSettings layout)
    {
        double listRatio = ClampRatio(layout.WayMarkListRatio);
        double editorRatio = ClampRatio(layout.WayMarkEditorRatio);
        double previewRatio = ClampRatio(layout.WayMarkPreviewRatio);
        double total = listRatio + editorRatio + previewRatio;
        if (total <= 0) return;

        WayMarkList_Column.Width = new GridLength(listRatio / total, GridUnitType.Star);
        WayMarkEditor_Column.Width = new GridLength(editorRatio / total, GridUnitType.Star);
        WayMarkPreview_Column.Width = new GridLength(previewRatio / total, GridUnitType.Star);
    }

    public void CaptureLayoutSettings(WindowLayoutSettings layout)
    {
        double totalWidth = WayMarkList_Column.ActualWidth + WayMarkEditor_Column.ActualWidth + WayMarkPreview_Column.ActualWidth;
        if (totalWidth <= 1) return;

        layout.WayMarkListRatio = WayMarkList_Column.ActualWidth / totalWidth;
        layout.WayMarkEditorRatio = WayMarkEditor_Column.ActualWidth / totalWidth;
        layout.WayMarkPreviewRatio = WayMarkPreview_Column.ActualWidth / totalWidth;
    }

    private static double ClampRatio(double value)
    {
        return double.IsFinite(value) && value > 0.05 ? value : 0.05;
    }

    private void NotifyWayMarksChanged()
    {
        WayMarksChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T matched)
            {
                return matched;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject source) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(source, i);
            if (child is T matched)
            {
                yield return matched;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
