using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls;

public partial class WayMarkPreviewControl : UserControl
{
    private WayMark? currentWayMark;

    public WayMarkPreviewControl()
    {
        InitializeComponent();
    }

    public void SetWayMark(WayMark? wayMark)
    {
        if (currentWayMark != null)
        {
            currentWayMark.PropertyChanged -= OnWayMarkPropertyChanged;
            UnsubscribeWayMarkPoints(currentWayMark);
        }

        currentWayMark = wayMark;

        if (currentWayMark != null)
        {
            currentWayMark.PropertyChanged += OnWayMarkPropertyChanged;
            SubscribeWayMarkPoints(currentWayMark);
        }

        UpdatePreview();
    }

    public void RefreshPreview()
    {
        UpdatePreview();
    }

    private void SubscribeWayMarkPoints(WayMark mark)
    {
        mark.A.PropertyChanged += OnPointPropertyChanged;
        mark.B.PropertyChanged += OnPointPropertyChanged;
        mark.C.PropertyChanged += OnPointPropertyChanged;
        mark.D.PropertyChanged += OnPointPropertyChanged;
        mark.One.PropertyChanged += OnPointPropertyChanged;
        mark.Two.PropertyChanged += OnPointPropertyChanged;
        mark.Three.PropertyChanged += OnPointPropertyChanged;
        mark.Four.PropertyChanged += OnPointPropertyChanged;
    }

    private void UnsubscribeWayMarkPoints(WayMark mark)
    {
        mark.A.PropertyChanged -= OnPointPropertyChanged;
        mark.B.PropertyChanged -= OnPointPropertyChanged;
        mark.C.PropertyChanged -= OnPointPropertyChanged;
        mark.D.PropertyChanged -= OnPointPropertyChanged;
        mark.One.PropertyChanged -= OnPointPropertyChanged;
        mark.Two.PropertyChanged -= OnPointPropertyChanged;
        mark.Three.PropertyChanged -= OnPointPropertyChanged;
        mark.Four.PropertyChanged -= OnPointPropertyChanged;
    }

    private void OnWayMarkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void OnPointPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void Preview_Container_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement container)
        {
            double size = Math.Min(container.ActualWidth, container.ActualHeight);
            if (size > 0)
            {
                Preview_Canvas.Width = size;
                Preview_Canvas.Height = size;
                UpdatePreview();
            }
        }
    }

    private void Scale_Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        Preview_Canvas.Children.Clear();
        if (currentWayMark == null) return;

        List<(string Name, WayMarkPoint Point)> points = [];
        if (currentWayMark.AEnabled) points.Add(("A", currentWayMark.A));
        if (currentWayMark.BEnabled) points.Add(("B", currentWayMark.B));
        if (currentWayMark.CEnabled) points.Add(("C", currentWayMark.C));
        if (currentWayMark.DEnabled) points.Add(("D", currentWayMark.D));
        if (currentWayMark.OneEnabled) points.Add(("1", currentWayMark.One));
        if (currentWayMark.TwoEnabled) points.Add(("2", currentWayMark.Two));
        if (currentWayMark.ThreeEnabled) points.Add(("3", currentWayMark.Three));
        if (currentWayMark.FourEnabled) points.Add(("4", currentWayMark.Four));

        if (points.Count == 0) return;

        float minX = points.Min(p => p.Point.FloatX);
        float maxX = points.Max(p => p.Point.FloatX);
        float minZ = points.Min(p => p.Point.FloatZ);
        float maxZ = points.Max(p => p.Point.FloatZ);

        float width = maxX - minX;
        float height = maxZ - minZ;

        if (width < 1) width = 10;
        if (height < 1) height = 10;

        float paddingX = Math.Max(width * 0.1f, 1);
        float paddingZ = Math.Max(height * 0.1f, 1);

        double canvasSize = Preview_Canvas.Width;
        if (double.IsNaN(canvasSize) || canvasSize <= 0) return;

        double scaleRatio = Scale_Slider != null ? Scale_Slider.Value : 0.1;
        double markerSize = canvasSize * scaleRatio;
        if (scaleRatio >= 1.0) scaleRatio = 0.99;

        float maxContentDim = Math.Max(width, height);
        float requiredPadding = (float)((maxContentDim * scaleRatio) / (2 * (1 - scaleRatio)));

        paddingX = Math.Max(paddingX, requiredPadding);
        paddingZ = Math.Max(paddingZ, requiredPadding);

        float displayMinX = minX - paddingX;
        float displayMaxX = maxX + paddingX;
        float displayMinZ = minZ - paddingZ;
        float displayMaxZ = maxZ + paddingZ;
        float displayWidth = displayMaxX - displayMinX;
        float displayHeight = displayMaxZ - displayMinZ;

        float maxDim = Math.Max(displayWidth, displayHeight);
        double scale = canvasSize / maxDim;
        double contentWidthPx = displayWidth * scale;
        double contentHeightPx = displayHeight * scale;
        double offsetX = (canvasSize - contentWidthPx) / 2;
        double offsetY = (canvasSize - contentHeightPx) / 2;

        foreach ((string Name, WayMarkPoint Point) point in points)
        {
            Image img = new();
            string imgName = point.Name.ToLower();

            try
            {
                img.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Image/s_{imgName}.png"));
            }
            catch
            {
            }

            img.Width = markerSize;
            img.Height = markerSize;

            double relativeX = point.Point.FloatX - displayMinX;
            double relativeZ = point.Point.FloatZ - displayMinZ;
            double left = relativeX * scale + offsetX - (markerSize / 2);
            double top = relativeZ * scale + offsetY - (markerSize / 2);

            Shape bgShape = !string.IsNullOrEmpty(point.Name) && char.IsDigit(point.Name[0])
                ? new Rectangle()
                : new Ellipse();

            bgShape.Width = markerSize;
            bgShape.Height = markerSize;
            bgShape.Fill = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

            Canvas.SetLeft(bgShape, left);
            Canvas.SetTop(bgShape, top);
            Preview_Canvas.Children.Add(bgShape);

            Canvas.SetLeft(img, left);
            Canvas.SetTop(img, top);
            Preview_Canvas.Children.Add(img);
        }
    }
}
