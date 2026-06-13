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
    private readonly ObservableCollection<MapData> regionOptions = [];
    private ICollectionView? regionOptionsView;
    private string regionFilterText = string.Empty;
    private bool suppressRegionTextChanged;
    private bool isSelectingRegionFromPopup;
    private bool isClearingRegionText;
    private readonly Dictionary<TextBox, CoordinateEditContext> coordinateEditContexts = [];
    private readonly Dictionary<TextBox, string> coordinateAcceptedTexts = [];
    private readonly HashSet<TextBox> coordinateTextChangeGuards = [];
    private ToolTip? activeCoordinateInputTip;
    private TextBox? activeCoordinateInputTipTarget;
    private System.Windows.Threading.DispatcherTimer? activeCoordinateInputTipTimer;
    private List<WayMark>? wayMarks;
    private const int MinRawCoordinate = int.MinValue;
    private const int MaxRawCoordinate = int.MaxValue;
    private const int CoordinateScale = 1000;
    private const int MaxCoordinateTextLength = 12;
    private const string CoordinateInputTip =
        "坐标格式：\n-2147483.648 到 2147483.647的数字，最多 3 位小数。\n不可输入其他字符。";

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly List<string> PointShape =
    [
        "圆形八方",
        "方形八方",
    ];

    private readonly List<string> PointOrder =
    [
        "A1B2C3D4",
        "A2B3C4D1",
    ];

    private enum CoordinateAxis
    {
        X,
        Y,
        Z
    }

    private readonly record struct CoordinateEditContext(WayMarkPoint Point, CoordinateAxis Axis);

    public WayMarkEditorControl()
    {
        InitializeComponent();
        Edit1_Grid.IsEnabled = false;
        Edit2_Grid.IsEnabled = false;
        RegisterCoordinateTextBoxPasteHandlers();
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), true);
        RefreshRegionOptions();
        UpdateMoveButtonState();

        PointShape_ComboBox.ItemsSource = PointShape;
        PointShape_ComboBox.SelectedIndex = 0;

        PointOrder_ComboBox.ItemsSource = PointOrder;
        PointOrder_ComboBox.SelectedIndex = 0;
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
        RefreshRegionOptions(wayMarks?.Select(mark => mark.RegionID));
        WayMark_ListBox.Items.Refresh();
        if (currentWayMark != null)
        {
            SetRegionSearchText(currentWayMark.RegionID);
        }
    }

    public void SetWayMarks(List<WayMark> markerSection)
    {
        wayMarks = markerSection;
        RefreshRegionOptions(markerSection.Select(mark => mark.RegionID));
        WayMark_ListBox.ItemsSource = markerSection;
        UpdateMoveButtonState();
    }

    public void ClearWayMarks()
    {
        wayMarks = null;
        currentWayMark = null;
        WayMark_ListBox.ItemsSource = null;
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
