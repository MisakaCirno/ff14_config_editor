using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls
{
    public partial class WayMarkEditPanelControl
    {
        private const double ShapePreviewMarkerSize = 24;
        private const double ShapePreviewMinimumRadius = 30;
        private const double ShapePreviewRadiusDamping = 20;
        private const double ShapePreviewDistanceLabelWidth = 58;
        private const double ShapePreviewDistanceLabelHeight = 20;
        private const double ShapePreviewDistanceGuideNormalOpacity = 0.62;
        private const double ShapePreviewDistanceGuideHoverOpacity = 0.92;

        private enum ShapePreviewKind
        {
            Circle,
            Square,
            Diamond
        }

        private bool isSynchronizingShapeDistance;

        private void SetShapePos_Button_Click(object sender, RoutedEventArgs e)
        {
            if (currentWayMark == null)
            {
                AppMessageBox.Show("请先选择一个要设置的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryReadShapeParameters(true, out GamePosition centerPos, out double distance, out _))
            {
                return;
            }

            List<GamePosition> positions = CreateShapePositions(centerPos, distance);
            string[] pointOrder = GetSelectedPointOrder();

            List<(string PointName, RawGamePosition Position)> rawPositions = [];
            for (int i = 0; i < pointOrder.Length; i++)
            {
                if (!TryCreateRawGamePosition(positions[i], out RawGamePosition rawPosition))
                {
                    AppMessageBox.Show("生成的坐标超出可保存范围，请检查中心点和距离。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                rawPositions.Add((pointOrder[i], rawPosition));
            }

            foreach ((string pointName, RawGamePosition rawPosition) in rawPositions)
            {
                SetPointPosition(currentWayMark, pointName, rawPosition);
            }

            currentWayMark.AEnabled = true;
            currentWayMark.BEnabled = true;
            currentWayMark.CEnabled = true;
            currentWayMark.DEnabled = true;
            currentWayMark.OneEnabled = true;
            currentWayMark.TwoEnabled = true;
            currentWayMark.ThreeEnabled = true;
            currentWayMark.FourEnabled = true;
            currentWayMark.timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            UpdatePreview();
            NotifyWayMarksChanged();
        }

        private void RegisterShapePreviewInputHandlers()
        {
            PointShape_ComboBox.SelectionChanged += ShapePreviewInput_Changed;
            PointOrder_ComboBox.SelectionChanged += ShapePreviewInput_Changed;
            ShapeDistance_Slider.ValueChanged += ShapeDistance_Slider_ValueChanged;
            ShapeDistance_TextBox.TextChanged += ShapeDistance_TextBox_TextChanged;
            ShapeCenterX_TextBox.TextChanged += ShapePreviewInput_Changed;
            ShapeCenterY_TextBox.TextChanged += ShapePreviewInput_Changed;
            ShapeCenterZ_TextBox.TextChanged += ShapePreviewInput_Changed;
        }

        private void ShapePreviewInput_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        private void ShapeDistance_Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (isSynchronizingShapeDistance)
            {
                return;
            }

            isSynchronizingShapeDistance = true;
            try
            {
                ShapeDistance_TextBox.Text = FormatShapeDistance(e.NewValue);
            }
            finally
            {
                isSynchronizingShapeDistance = false;
            }

            UpdatePreview();
        }

        private void ShapeDistance_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isSynchronizingShapeDistance)
            {
                return;
            }

            if (TryParseFiniteDouble(ShapeDistance_TextBox.Text, out double distance))
            {
                double normalizedDistance = NormalizeShapeDistance(distance);
                isSynchronizingShapeDistance = true;
                try
                {
                    if (distance < 0)
                    {
                        ShapeDistance_TextBox.Text = FormatShapeDistance(normalizedDistance);
                        ShapeDistance_TextBox.CaretIndex = ShapeDistance_TextBox.Text.Length;
                    }

                    ShapeDistance_Slider.Value = Math.Clamp(
                        normalizedDistance,
                        ShapeDistance_Slider.Minimum,
                        ShapeDistance_Slider.Maximum);
                }
                finally
                {
                    isSynchronizingShapeDistance = false;
                }
            }

            UpdatePreview();
        }

        private void ShapePreview_Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            ShapePreview_Canvas.Children.Clear();

            double canvasWidth = ShapePreview_Canvas.ActualWidth;
            double canvasHeight = ShapePreview_Canvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                return;
            }

            DrawShapePreviewBackground(canvasWidth, canvasHeight);

            if (!TryReadShapeParameters(false, out GamePosition centerPos, out double distance, out _))
            {
                return;
            }

            List<GamePosition> positions = CreateShapePositions(centerPos, distance);
            string[] pointOrder = GetSelectedPointOrder();
            DrawShapePreviewShape(canvasWidth, canvasHeight, centerPos, distance, positions, pointOrder);
        }

        private bool TryReadShapeParameters(
            bool showValidationMessage,
            out GamePosition centerPos,
            out double distance,
            out string? validationMessage)
        {
            centerPos = new GamePosition();
            distance = 0;

            if (!TryReadDouble(ShapeDistance_TextBox, "与中心点间距", showValidationMessage, out distance, out validationMessage) ||
                !TryReadDouble(ShapeCenterX_TextBox, "中心点 X", showValidationMessage, out double centerX, out validationMessage) ||
                !TryReadDouble(ShapeCenterY_TextBox, "中心点 Y", showValidationMessage, out double centerY, out validationMessage) ||
                !TryReadDouble(ShapeCenterZ_TextBox, "中心点 Z", showValidationMessage, out double centerZ, out validationMessage))
            {
                return false;
            }

            distance = NormalizeShapeDistance(distance);
            centerPos = new GamePosition(centerX, centerY, centerZ);
            validationMessage = null;
            return true;
        }

        private List<GamePosition> CreateShapePositions(GamePosition centerPos, double distance)
        {
            return GetSelectedShapeKind() switch
            {
                ShapePreviewKind.Square => MarkerShapePosCalculator.Square(centerPos, distance),
                ShapePreviewKind.Diamond => MarkerShapePosCalculator.Diamond(centerPos, distance),
                _ => MarkerShapePosCalculator.Circle(centerPos, distance)
            };
        }

        private ShapePreviewKind GetSelectedShapeKind()
        {
            return PointShape_ComboBox.SelectedItem?.ToString() switch
            {
                "方形八方" => ShapePreviewKind.Square,
                "斜方八方" => ShapePreviewKind.Diamond,
                _ => ShapePreviewKind.Circle
            };
        }

        private string[] GetSelectedPointOrder()
        {
            string order = PointOrder_ComboBox.SelectedItem?.ToString() ?? "4A1";
            return order switch
            {
                "1A2" => ["A", "2", "B", "3", "C", "4", "D", "1"],
                _ => ["A", "1", "B", "2", "C", "3", "D", "4"]
            };
        }

        private static bool TryReadDouble(
            TextBox textBox,
            string displayName,
            bool showValidationMessage,
            out double value,
            out string? validationMessage)
        {
            value = 0;
            validationMessage = null;
            string text = textBox.Text.Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                if (!double.IsFinite(value))
                {
                    validationMessage = $"{displayName} 需要填写有限数字。";
                    if (showValidationMessage)
                    {
                        AppMessageBox.Show(validationMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    return false;
                }

                return true;
            }

            validationMessage = $"{displayName} 需要填写数字。";
            if (showValidationMessage)
            {
                AppMessageBox.Show(validationMessage, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }

        private void DrawShapePreviewShape(
            double canvasWidth,
            double canvasHeight,
            GamePosition centerPos,
            double distance,
            IReadOnlyList<GamePosition> positions,
            IReadOnlyList<string> pointOrder)
        {
            double centerX = canvasWidth / 2;
            double centerY = canvasHeight / 2;
            double radius = distance > 0
                ? CalculateShapePreviewRadius(distance, canvasWidth, canvasHeight)
                : 0;
            double scale = distance > 0 ? radius / distance : 0;

            if (distance > 0)
            {
                DrawShapePreviewOutline(centerX, centerY, radius, GetSelectedShapeKind());
                AddShapePreviewDistanceGuide(canvasWidth, canvasHeight, centerX, centerY, radius, distance);
            }

            AddShapePreviewCenter(centerX, centerY);

            for (int i = 0; i < Math.Min(positions.Count, pointOrder.Count); i++)
            {
                GamePosition position = positions[i];
                double x = centerX + ((position.X - centerPos.X) * scale);
                double y = centerY + ((position.Z - centerPos.Z) * scale);

                if (distance > 0)
                {
                    ShapePreview_Canvas.Children.Add(CreatePreviewLine(centerX, centerY, x, y, new SolidColorBrush(Color.FromArgb(80, 53, 92, 115)), 1));
                }

                AddShapePreviewMarker(pointOrder[i], x, y);
            }
        }

        private static double CalculateShapePreviewRadius(double distance, double canvasWidth, double canvasHeight)
        {
            double maxRadius = Math.Min(canvasWidth, canvasHeight) / 2 - (ShapePreviewMarkerSize / 2) - 10;
            if (maxRadius <= ShapePreviewMinimumRadius)
            {
                return Math.Max(8, maxRadius);
            }

            double normalizedDistance = distance / (distance + ShapePreviewRadiusDamping);
            return Math.Clamp(
                ShapePreviewMinimumRadius + (normalizedDistance * (maxRadius - ShapePreviewMinimumRadius)),
                ShapePreviewMinimumRadius,
                maxRadius);
        }

        private void DrawShapePreviewBackground(double canvasWidth, double canvasHeight)
        {
            Brush gridBrush = new SolidColorBrush(Color.FromRgb(226, 232, 238));
            Brush axisBrush = new SolidColorBrush(Color.FromRgb(184, 198, 211));
            double centerX = canvasWidth / 2;
            double centerY = canvasHeight / 2;

            for (int i = 1; i <= 3; i++)
            {
                double x = canvasWidth * i / 4;
                double y = canvasHeight * i / 4;
                ShapePreview_Canvas.Children.Add(CreatePreviewLine(x, 0, x, canvasHeight, gridBrush, 0.6));
                ShapePreview_Canvas.Children.Add(CreatePreviewLine(0, y, canvasWidth, y, gridBrush, 0.6));
            }

            ShapePreview_Canvas.Children.Add(CreatePreviewLine(centerX, 0, centerX, canvasHeight, axisBrush, 1));
            ShapePreview_Canvas.Children.Add(CreatePreviewLine(0, centerY, canvasWidth, centerY, axisBrush, 1));
        }

        private void DrawShapePreviewOutline(double centerX, double centerY, double radius, ShapePreviewKind shapeKind)
        {
            Brush outlineBrush = new SolidColorBrush(Color.FromRgb(53, 92, 115));
            DoubleCollection dashArray = new() { 4, 3 };

            if (shapeKind == ShapePreviewKind.Diamond)
            {
                Polygon diamondOutline = new()
                {
                    Points = new PointCollection
                    {
                        new(centerX, centerY - radius),
                        new(centerX + radius, centerY),
                        new(centerX, centerY + radius),
                        new(centerX - radius, centerY)
                    },
                    Stroke = outlineBrush,
                    StrokeThickness = 1.4,
                    StrokeDashArray = dashArray
                };
                ShapePreview_Canvas.Children.Add(diamondOutline);
                return;
            }

            Shape outline = shapeKind == ShapePreviewKind.Square ? new Rectangle() : new Ellipse();
            outline.Width = radius * 2;
            outline.Height = radius * 2;
            outline.Stroke = outlineBrush;
            outline.StrokeThickness = 1.4;
            outline.StrokeDashArray = dashArray;

            Canvas.SetLeft(outline, centerX - radius);
            Canvas.SetTop(outline, centerY - radius);
            ShapePreview_Canvas.Children.Add(outline);
        }

        private void AddShapePreviewDistanceGuide(
            double canvasWidth,
            double canvasHeight,
            double centerX,
            double centerY,
            double radius,
            double distance)
        {
            string distanceText = $"{FormatShapeDistance(distance)} 米";
            double endX = centerX;
            double endY = centerY - radius;
            Brush guideBrush = new SolidColorBrush(Color.FromArgb(215, 53, 92, 115));
            Line guideLine = CreatePreviewLine(centerX, centerY, endX, endY, guideBrush, 2);
            guideLine.StrokeDashArray = new DoubleCollection { 3, 2 };
            guideLine.Opacity = ShapePreviewDistanceGuideNormalOpacity;
            guideLine.ToolTip = distanceText;
            Panel.SetZIndex(guideLine, 1);
            ShapePreview_Canvas.Children.Add(guideLine);

            Border label = new()
            {
                Width = ShapePreviewDistanceLabelWidth,
                Height = ShapePreviewDistanceLabelHeight,
                Background = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                CornerRadius = new CornerRadius(3),
                Opacity = ShapePreviewDistanceGuideNormalOpacity,
                ToolTip = distanceText,
                Child = new TextBlock
                {
                    Text = distanceText,
                    Foreground = guideBrush,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            guideLine.MouseEnter += (_, _) => SetShapePreviewDistanceGuideOpacity(guideLine, label, ShapePreviewDistanceGuideHoverOpacity);
            guideLine.MouseLeave += (_, _) => SetShapePreviewDistanceGuideOpacity(guideLine, label, ShapePreviewDistanceGuideNormalOpacity);
            label.MouseEnter += (_, _) => SetShapePreviewDistanceGuideOpacity(guideLine, label, ShapePreviewDistanceGuideHoverOpacity);
            label.MouseLeave += (_, _) => SetShapePreviewDistanceGuideOpacity(guideLine, label, ShapePreviewDistanceGuideNormalOpacity);

            double labelLeft = Math.Clamp(
                centerX - (ShapePreviewDistanceLabelWidth / 2),
                4,
                Math.Max(4, canvasWidth - ShapePreviewDistanceLabelWidth - 4));
            double labelTop = Math.Clamp(
                centerY - (radius / 2) - (ShapePreviewDistanceLabelHeight / 2),
                4,
                Math.Max(4, canvasHeight - ShapePreviewDistanceLabelHeight - 4));
            Canvas.SetLeft(label, labelLeft);
            Canvas.SetTop(label, labelTop);
            Panel.SetZIndex(label, 1);
            ShapePreview_Canvas.Children.Add(label);
        }

        private static void SetShapePreviewDistanceGuideOpacity(UIElement guideLine, UIElement label, double opacity)
        {
            guideLine.Opacity = opacity;
            label.Opacity = opacity;
        }

        private void AddShapePreviewCenter(double centerX, double centerY)
        {
            Ellipse centerDot = new()
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(53, 92, 115)),
                Stroke = Brushes.White,
                StrokeThickness = 1
            };

            Canvas.SetLeft(centerDot, centerX - (centerDot.Width / 2));
            Canvas.SetTop(centerDot, centerY - (centerDot.Height / 2));
            Panel.SetZIndex(centerDot, 1);
            ShapePreview_Canvas.Children.Add(centerDot);
        }

        private void AddShapePreviewMarker(string name, double x, double y)
        {
            double left = x - (ShapePreviewMarkerSize / 2);
            double top = y - (ShapePreviewMarkerSize / 2);

            Shape background = !string.IsNullOrEmpty(name) && char.IsDigit(name[0])
                ? new Rectangle()
                : new Ellipse();
            background.Width = ShapePreviewMarkerSize;
            background.Height = ShapePreviewMarkerSize;
            background.Fill = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

            Canvas.SetLeft(background, left);
            Canvas.SetTop(background, top);
            Panel.SetZIndex(background, 2);
            ShapePreview_Canvas.Children.Add(background);

            Image icon = new()
            {
                Width = ShapePreviewMarkerSize,
                Height = ShapePreviewMarkerSize,
                Stretch = Stretch.Uniform
            };

            try
            {
                string imageName = name.ToLower(CultureInfo.InvariantCulture);
                icon.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Image/s_{imageName}.png"));
            }
            catch
            {
            }

            if (icon.Source == null)
            {
                AddShapePreviewFallbackMarker(name, left, top);
                return;
            }

            Canvas.SetLeft(icon, left);
            Canvas.SetTop(icon, top);
            Panel.SetZIndex(icon, 3);
            ShapePreview_Canvas.Children.Add(icon);
        }

        private void AddShapePreviewFallbackMarker(string name, double left, double top)
        {
            TextBlock fallback = new()
            {
                Width = ShapePreviewMarkerSize,
                Height = ShapePreviewMarkerSize,
                Text = name,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas.SetLeft(fallback, left);
            Canvas.SetTop(fallback, top + 4);
            Panel.SetZIndex(fallback, 3);
            ShapePreview_Canvas.Children.Add(fallback);
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

        private static bool TryParseFiniteDouble(string text, out double value)
        {
            value = 0;
            string trimmedText = text.Trim();
            if (double.TryParse(trimmedText, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                double.TryParse(trimmedText, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return double.IsFinite(value);
            }

            return false;
        }

        private static double NormalizeShapeDistance(double distance)
        {
            return Math.Max(0, distance);
        }

        private static string FormatShapeDistance(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        private static bool TryCreateRawGamePosition(GamePosition position, out RawGamePosition rawPosition)
        {
            rawPosition = default;
            if (!WayMarkCoordinateConverter.TryRoundToRawCoordinate(position.X, out int rawX) ||
                !WayMarkCoordinateConverter.TryRoundToRawCoordinate(position.Y, out int rawY) ||
                !WayMarkCoordinateConverter.TryRoundToRawCoordinate(position.Z, out int rawZ))
            {
                return false;
            }

            rawPosition = new RawGamePosition(rawX, rawY, rawZ);
            return true;
        }

        private static void SetPointPosition(WayMark wayMark, string pointName, RawGamePosition position)
        {
            WayMarkPoint point = pointName switch
            {
                "A" => wayMark.A,
                "B" => wayMark.B,
                "C" => wayMark.C,
                "D" => wayMark.D,
                "1" => wayMark.One,
                "2" => wayMark.Two,
                "3" => wayMark.Three,
                "4" => wayMark.Four,
                _ => throw new ArgumentOutOfRangeException(nameof(pointName), pointName, "未知标点名称")
            };

            point.X = position.X;
            point.Y = position.Y;
            point.Z = position.Z;
        }

        private readonly record struct RawGamePosition(int X, int Y, int Z);
    }
}
