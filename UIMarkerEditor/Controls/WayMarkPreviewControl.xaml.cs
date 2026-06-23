using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls;

public partial class WayMarkPreviewControl : UserControl
{
    private const double MinimumGridSpacingPixels = 36;
    private const double ScaleBarTargetCanvasRatio = 0.28;
    private const double ScaleBarMaxCanvasRatio = 0.42;
    private const double OverlayEdgePadding = 4;
    private const double ScaleBarTickHeight = 10;
    private const double DirectionAxisLength = 26;
    private const double DirectionRightLabelWidth = 28;
    private const double DirectionTopLabelHeight = 18;

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
        if (sender is FrameworkElement container &&
            container.ActualWidth > 0 &&
            container.ActualHeight > 0)
        {
            Preview_Canvas.Width = container.ActualWidth;
            Preview_Canvas.Height = container.ActualHeight;
            UpdatePreview();
        }
    }

    private void Scale_Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        Preview_Canvas.Children.Clear();
        if (currentWayMark == null)
        {
            return;
        }

        List<(string Name, WayMarkPoint Point)> points = [];
        if (currentWayMark.AEnabled) points.Add(("A", currentWayMark.A));
        if (currentWayMark.BEnabled) points.Add(("B", currentWayMark.B));
        if (currentWayMark.CEnabled) points.Add(("C", currentWayMark.C));
        if (currentWayMark.DEnabled) points.Add(("D", currentWayMark.D));
        if (currentWayMark.OneEnabled) points.Add(("1", currentWayMark.One));
        if (currentWayMark.TwoEnabled) points.Add(("2", currentWayMark.Two));
        if (currentWayMark.ThreeEnabled) points.Add(("3", currentWayMark.Three));
        if (currentWayMark.FourEnabled) points.Add(("4", currentWayMark.Four));

        if (points.Count == 0)
        {
            return;
        }

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

        double canvasWidth = Preview_Canvas.ActualWidth;
        double canvasHeight = Preview_Canvas.ActualHeight;
        if (double.IsNaN(canvasWidth) ||
            double.IsNaN(canvasHeight) ||
            canvasWidth <= 0 ||
            canvasHeight <= 0)
        {
            return;
        }

        double scaleRatio = Scale_Slider != null ? Scale_Slider.Value : 0.1;
        if (scaleRatio >= 1.0) scaleRatio = 0.99;
        double markerSize = Math.Min(canvasWidth, canvasHeight) * scaleRatio;

        float maxContentDim = Math.Max(width, height);
        float requiredPadding = (float)((maxContentDim * scaleRatio) / (2 * (1 - scaleRatio)));

        paddingX = Math.Max(paddingX, requiredPadding);
        paddingZ = Math.Max(paddingZ, requiredPadding);

        double displayMinX = minX - paddingX;
        double displayMaxX = maxX + paddingX;
        double displayMinZ = minZ - paddingZ;
        double displayMaxZ = maxZ + paddingZ;
        double displayWidth = displayMaxX - displayMinX;
        double displayHeight = displayMaxZ - displayMinZ;

        ExpandDisplayRangeToCanvasAspect(
            canvasWidth,
            canvasHeight,
            ref displayMinX,
            ref displayMaxX,
            ref displayMinZ,
            ref displayMaxZ,
            ref displayWidth,
            ref displayHeight);

        double scale = Math.Min(canvasWidth / displayWidth, canvasHeight / displayHeight);
        double contentWidthPx = displayWidth * scale;
        double contentHeightPx = displayHeight * scale;
        double offsetX = (canvasWidth - contentWidthPx) / 2;
        double offsetY = (canvasHeight - contentHeightPx) / 2;

        DrawPreviewGuides(
            canvasWidth,
            canvasHeight,
            displayMinX,
            displayMaxX,
            displayMinZ,
            displayMaxZ,
            scale,
            offsetX,
            offsetY);
        foreach ((string Name, WayMarkPoint Point) point in points)
        {
            Image img = new();
            string imgName = point.Name.ToLower(CultureInfo.InvariantCulture);

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
            Panel.SetZIndex(bgShape, 5);
            Preview_Canvas.Children.Add(bgShape);

            Canvas.SetLeft(img, left);
            Canvas.SetTop(img, top);
            Panel.SetZIndex(img, 6);
            Preview_Canvas.Children.Add(img);
        }
    }

    private void DrawPreviewGuides(
        double canvasWidth,
        double canvasHeight,
        double displayMinX,
        double displayMaxX,
        double displayMinZ,
        double displayMaxZ,
        double scale,
        double offsetX,
        double offsetY)
    {
        double displayWidth = displayMaxX - displayMinX;
        double displayHeight = displayMaxZ - displayMinZ;
        double contentWidthPx = displayWidth * scale;
        double contentHeightPx = displayHeight * scale;
        double contentRight = offsetX + contentWidthPx;
        double contentBottom = offsetY + contentHeightPx;
        double gridStep = GetNiceStep(MinimumGridSpacingPixels / scale);

        Rectangle contentBackground = new()
        {
            Width = contentWidthPx,
            Height = contentHeightPx,
            Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Stroke = new SolidColorBrush(Color.FromArgb(135, 111, 91, 48)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(contentBackground, offsetX);
        Canvas.SetTop(contentBackground, offsetY);
        Preview_Canvas.Children.Add(contentBackground);

        Brush gridBrush = new SolidColorBrush(Color.FromArgb(95, 255, 255, 255));
        Brush axisBrush = new SolidColorBrush(Color.FromArgb(175, 53, 92, 115));

        for (double x = Math.Ceiling(displayMinX / gridStep) * gridStep; x <= displayMaxX; x += gridStep)
        {
            double canvasX = ToCanvasX(x, displayMinX, scale, offsetX);
            Preview_Canvas.Children.Add(CreatePreviewLine(canvasX, offsetY, canvasX, contentBottom, gridBrush, 0.8));
        }

        for (double z = Math.Ceiling(displayMinZ / gridStep) * gridStep; z <= displayMaxZ; z += gridStep)
        {
            double canvasY = ToCanvasY(z, displayMinZ, scale, offsetY);
            Preview_Canvas.Children.Add(CreatePreviewLine(offsetX, canvasY, contentRight, canvasY, gridBrush, 0.8));
        }

        if (displayMinX <= 0 && displayMaxX >= 0)
        {
            double axisX = ToCanvasX(0, displayMinX, scale, offsetX);
            Preview_Canvas.Children.Add(CreatePreviewLine(axisX, offsetY, axisX, contentBottom, axisBrush, 1.4));
            AddPreviewOverlayText("X 0", axisX + 4, offsetY + 4, axisBrush, new SolidColorBrush(Color.FromArgb(214, 255, 255, 255)));
        }

        if (displayMinZ <= 0 && displayMaxZ >= 0)
        {
            double axisY = ToCanvasY(0, displayMinZ, scale, offsetY);
            Preview_Canvas.Children.Add(CreatePreviewLine(offsetX, axisY, contentRight, axisY, axisBrush, 1.4));
            AddPreviewOverlayText("Z 0", offsetX + 4, axisY + 4, axisBrush, new SolidColorBrush(Color.FromArgb(214, 255, 255, 255)));
        }

        DrawPreviewScaleBar(canvasWidth, canvasHeight, scale);
        DrawPreviewDirectionIndicator(canvasWidth);
    }

    private void DrawPreviewScaleBar(double canvasWidth, double canvasHeight, double scale)
    {
        double displayedUnits = canvasWidth / scale;
        double scaleUnits = GetNiceValueAtOrBelow(displayedUnits * ScaleBarTargetCanvasRatio);
        double scaleLength = scaleUnits * scale;
        if (scaleLength > canvasWidth * ScaleBarMaxCanvasRatio)
        {
            scaleUnits = GetNiceValueAtOrBelow(displayedUnits * (ScaleBarMaxCanvasRatio * 0.8));
            scaleLength = scaleUnits * scale;
        }

        double left = OverlayEdgePadding;
        double baseline = canvasHeight - OverlayEdgePadding - (ScaleBarTickHeight / 2);
        Brush barBrush = new SolidColorBrush(Color.FromRgb(53, 92, 115));
        Brush labelBackground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        Preview_Canvas.Children.Add(CreatePreviewLine(left, baseline, left + scaleLength, baseline, barBrush, 2));
        Preview_Canvas.Children.Add(CreatePreviewLine(left, baseline - (ScaleBarTickHeight / 2), left, baseline + (ScaleBarTickHeight / 2), barBrush, 2));
        Preview_Canvas.Children.Add(CreatePreviewLine(left + scaleLength, baseline - (ScaleBarTickHeight / 2), left + scaleLength, baseline + (ScaleBarTickHeight / 2), barBrush, 2));
        AddPreviewOverlayText($"{FormatPreviewNumber(scaleUnits)} 米", left, baseline - 24, barBrush, labelBackground);
    }

    private void DrawPreviewDirectionIndicator(double canvasSize)
    {
        double groupLeft = canvasSize - OverlayEdgePadding - DirectionAxisLength - DirectionRightLabelWidth - 3;
        double groupTop = OverlayEdgePadding;
        double originX = groupLeft + 7;
        double originY = groupTop + DirectionTopLabelHeight + DirectionAxisLength;
        Brush brush = new SolidColorBrush(Color.FromRgb(53, 92, 115));
        Brush labelBackground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));

        Preview_Canvas.Children.Add(CreatePreviewLine(originX, originY, originX + DirectionAxisLength, originY, brush, 1.8));
        Preview_Canvas.Children.Add(CreatePreviewLine(originX + DirectionAxisLength, originY, originX + DirectionAxisLength - 7, originY - 4, brush, 1.8));
        Preview_Canvas.Children.Add(CreatePreviewLine(originX + DirectionAxisLength, originY, originX + DirectionAxisLength - 7, originY + 4, brush, 1.8));
        Preview_Canvas.Children.Add(CreatePreviewLine(originX, originY, originX, originY - DirectionAxisLength, brush, 1.8));
        Preview_Canvas.Children.Add(CreatePreviewLine(originX, originY - DirectionAxisLength, originX - 4, originY - DirectionAxisLength + 7, brush, 1.8));
        Preview_Canvas.Children.Add(CreatePreviewLine(originX, originY - DirectionAxisLength, originX + 4, originY - DirectionAxisLength + 7, brush, 1.8));

        AddPreviewOverlayText("X+", originX + DirectionAxisLength + 3, originY - 9, brush, labelBackground, DirectionRightLabelWidth);
        AddPreviewOverlayText("Z-", groupLeft, groupTop, brush, labelBackground, DirectionRightLabelWidth);
    }

    private void AddPreviewOverlayText(string text, double left, double top, Brush foreground, Brush background, double? width = null)
    {
        Border label = new()
        {
            Background = background,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center
            }
        };

        if (width.HasValue)
        {
            label.Width = width.Value;
        }

        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        Panel.SetZIndex(label, 4);
        Preview_Canvas.Children.Add(label);
    }


    private static double ToCanvasX(double worldX, double displayMinX, double scale, double offsetX)
    {
        return ((worldX - displayMinX) * scale) + offsetX;
    }

    private static double ToCanvasY(double worldZ, double displayMinZ, double scale, double offsetY)
    {
        return ((worldZ - displayMinZ) * scale) + offsetY;
    }

    private static Line CreatePreviewLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        return new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness,
            SnapsToDevicePixels = true
        };
    }

    private static double GetNiceStep(double rawStep)
    {
        if (!double.IsFinite(rawStep) || rawStep <= 0)
        {
            return 1;
        }

        double exponent = Math.Floor(Math.Log10(rawStep));
        double magnitude = Math.Pow(10, exponent);
        double normalized = rawStep / magnitude;
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private static double GetNiceValueAtOrBelow(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return 1;
        }

        double exponent = Math.Floor(Math.Log10(value));
        double magnitude = Math.Pow(10, exponent);
        double normalized = value / magnitude;
        double nice = normalized >= 5 ? 5 : normalized >= 2 ? 2 : 1;
        return nice * magnitude;
    }

    private static void ExpandDisplayRangeToCanvasAspect(
        double canvasWidth,
        double canvasHeight,
        ref double displayMinX,
        ref double displayMaxX,
        ref double displayMinZ,
        ref double displayMaxZ,
        ref double displayWidth,
        ref double displayHeight)
    {
        double canvasAspect = canvasWidth / canvasHeight;
        double displayAspect = displayWidth / displayHeight;

        if (!double.IsFinite(canvasAspect) ||
            !double.IsFinite(displayAspect) ||
            canvasAspect <= 0 ||
            displayAspect <= 0)
        {
            return;
        }

        if (displayAspect > canvasAspect)
        {
            double targetHeight = displayWidth / canvasAspect;
            double extraHeight = targetHeight - displayHeight;
            displayMinZ -= extraHeight / 2;
            displayMaxZ += extraHeight / 2;
            displayHeight = targetHeight;
        }
        else
        {
            double targetWidth = displayHeight * canvasAspect;
            double extraWidth = targetWidth - displayWidth;
            displayMinX -= extraWidth / 2;
            displayMaxX += extraWidth / 2;
            displayWidth = targetWidth;
        }
    }

    private static string FormatPreviewNumber(double value)
    {
        double absValue = Math.Abs(value);
        string format = absValue >= 100 ? "0" : absValue >= 10 ? "0.#" : "0.##";
        return value.ToString(format, CultureInfo.CurrentCulture);
    }
}
