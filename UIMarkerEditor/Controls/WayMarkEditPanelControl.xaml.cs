using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls;

public partial class WayMarkEditPanelControl : UserControl, INotifyPropertyChanged
{
    private const int MinRawCoordinate = WayMarkCoordinateConverter.MinRawCoordinate;
    private const int MaxRawCoordinate = WayMarkCoordinateConverter.MaxRawCoordinate;
    private const int CoordinateScale = WayMarkCoordinateConverter.CoordinateScale;
    private const int MaxCoordinateTextLength = 12;
    private const string CoordinateInputTip =
        "坐标格式：\n-2147483.648 到 2147483.647的数字，最多 3 位小数。\n不可输入其他字符。";

    private readonly ObservableCollection<MapData> regionOptions = [];
    private readonly Dictionary<TextBox, CoordinateEditContext> coordinateEditContexts = [];
    private readonly Dictionary<TextBox, string> coordinateAcceptedTexts = [];
    private readonly HashSet<TextBox> coordinateTextChangeGuards = [];
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly List<string> PointShape = ["圆形八方", "方形八方"];
    private readonly List<string> PointOrder = ["A1B2C3D4", "A2B3C4D1"];

    private ICollectionView? regionOptionsView;
    private string regionFilterText = string.Empty;
    private bool suppressRegionTextChanged;
    private bool isSelectingRegionFromPopup;
    private bool isClearingRegionText;
    private bool suppressWayMarkChanged;
    private bool showClipboardButtons = true;
    private WayMark? currentWayMark;
    private ToolTip? activeCoordinateInputTip;
    private TextBox? activeCoordinateInputTipTarget;
    private System.Windows.Threading.DispatcherTimer? activeCoordinateInputTipTimer;

    public WayMarkEditPanelControl()
    {
        InitializeComponent();
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(Window_PreviewMouseDown), true);
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), true);
        RegisterCoordinateTextBoxPasteHandlers();
        RefreshRegionOptions();
        UpdateEnabledState();

        PointShape_ComboBox.ItemsSource = PointShape;
        PointShape_ComboBox.SelectedIndex = 0;

        PointOrder_ComboBox.ItemsSource = PointOrder;
        PointOrder_ComboBox.SelectedIndex = 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? WayMarkChanged;

    public WayMark? CurrentWayMark
    {
        get => currentWayMark;
        private set
        {
            if (ReferenceEquals(currentWayMark, value)) return;
            UnsubscribeWayMark(currentWayMark);
            currentWayMark = value;
            SubscribeWayMark(currentWayMark);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentWayMark)));
            UpdateEnabledState();
        }
    }

    public bool ShowClipboardButtons
    {
        get => showClipboardButtons;
        set
        {
            if (showClipboardButtons == value) return;
            showClipboardButtons = value;
            UpdateClipboardButtonsVisibility();
        }
    }

    public void SetWayMark(WayMark? wayMark, IEnumerable<ushort>? extraRegionIds = null)
    {
        suppressWayMarkChanged = true;
        try
        {
            CurrentWayMark = wayMark;
            RefreshRegionOptions(extraRegionIds);
            if (currentWayMark != null)
            {
                SetRegionSearchText(currentWayMark.RegionID);
            }
            else
            {
                RegionSearch_TextBox.Clear();
                regionFilterText = string.Empty;
                regionOptionsView?.Refresh();
            }
        }
        finally
        {
            suppressWayMarkChanged = false;
        }
    }

    public void RefreshMapDataDisplay(IEnumerable<ushort>? extraRegionIds = null)
    {
        RefreshRegionOptions(extraRegionIds);
        if (currentWayMark != null)
        {
            SetRegionSearchText(currentWayMark.RegionID);
        }
    }

    private void UpdateEnabledState()
    {
        EditRoot_Grid.IsEnabled = currentWayMark != null;
        UpdateClipboardButtonsVisibility();
    }

    private void UpdateClipboardButtonsVisibility()
    {
        Visibility visibility = showClipboardButtons ? Visibility.Visible : Visibility.Collapsed;
        ClipboardButtons_Grid.Visibility = visibility;
        ClipboardButtons_Row.Height = showClipboardButtons ? GridLength.Auto : new GridLength(0);
    }

    private void SubscribeWayMark(WayMark? wayMark)
    {
        if (wayMark == null) return;

        wayMark.PropertyChanged += WayMark_PropertyChanged;
        wayMark.A.PropertyChanged += WayMarkPoint_PropertyChanged;
        wayMark.B.PropertyChanged += WayMarkPoint_PropertyChanged;
        wayMark.C.PropertyChanged += WayMarkPoint_PropertyChanged;
        wayMark.D.PropertyChanged += WayMarkPoint_PropertyChanged;
        wayMark.One.PropertyChanged += WayMarkPoint_PropertyChanged;
        wayMark.Two.PropertyChanged += WayMarkPoint_PropertyChanged;
        wayMark.Three.PropertyChanged += WayMarkPoint_PropertyChanged;
        wayMark.Four.PropertyChanged += WayMarkPoint_PropertyChanged;
    }

    private void UnsubscribeWayMark(WayMark? wayMark)
    {
        if (wayMark == null) return;

        wayMark.PropertyChanged -= WayMark_PropertyChanged;
        wayMark.A.PropertyChanged -= WayMarkPoint_PropertyChanged;
        wayMark.B.PropertyChanged -= WayMarkPoint_PropertyChanged;
        wayMark.C.PropertyChanged -= WayMarkPoint_PropertyChanged;
        wayMark.D.PropertyChanged -= WayMarkPoint_PropertyChanged;
        wayMark.One.PropertyChanged -= WayMarkPoint_PropertyChanged;
        wayMark.Two.PropertyChanged -= WayMarkPoint_PropertyChanged;
        wayMark.Three.PropertyChanged -= WayMarkPoint_PropertyChanged;
        wayMark.Four.PropertyChanged -= WayMarkPoint_PropertyChanged;
    }

    private void WayMark_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyWayMarksChanged();
    }

    private void WayMarkPoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyWayMarksChanged();
    }

    private void NotifyWayMarksChanged()
    {
        if (suppressWayMarkChanged) return;

        if (currentWayMark != null)
        {
            currentWayMark.timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        WayMarkChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePreview()
    {
    }

    private static IEnumerable<ushort> GetLoadedRegionIds()
    {
        return [];
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
    private enum CoordinateAxis
    {
        X,
        Y,
        Z
    }

    private readonly record struct CoordinateEditContext(WayMarkPoint Point, CoordinateAxis Axis);
}