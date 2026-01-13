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
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private string currentFilePath = string.Empty;

        private ConfigUISave? configUISave = null;

        private readonly JsonSerializerOptions jsonOptions = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Edit1_Grid.IsEnabled = false;
            Edit2_Grid.IsEnabled = false;
        }

        private void Load_Button_Click(object sender, RoutedEventArgs e)
        {
            // 打开文件对话框，只允许选择 UISAVE.dat
            Microsoft.Win32.OpenFileDialog openFileDialog = new()
            {
                Title = "选择 UISAVE.dat",
                Filter = "UISAVE.dat 文件 (UISAVE.dat)|UISAVE.dat",
                FilterIndex = 1,
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;

                // 强校验：必须是 UISAVE.dat（忽略大小写）
                if (!string.Equals(System.IO.Path.GetFileName(filePath), "UISAVE.dat", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("只能选择名为 UISAVE.dat 的文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                currentFilePath = filePath;
                LoadConfigFile(currentFilePath);
            }
        }
        private void Reload_Button_Click(object sender, RoutedEventArgs e)
        {
            // 重新加载标点列表
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                LoadConfigFile(currentFilePath);
            }
            else
            {
                MessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Save_Button_Click(object sender, RoutedEventArgs e)
        {
            // 保存修改后的UISAVE.DAT文件
            if (configUISave != null)
            {
                // 在这里将修改后的数据写回UISAVE.DAT文件
                configUISave.Save();
                MessageBox.Show("文件已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LoadConfigFile(string filePath)
        {
            // 使用 ConfigUISave 类加载文件
            configUISave = new(filePath);

            if (configUISave != null && configUISave.Marks != null)
            {
                List<WayMark> markerSection = configUISave.Marks.WayMarks;

                WayMark_ListBox.ItemsSource = markerSection;

                // 输出所有的enableFlag和regionID以供调试
                foreach (WayMark mark in markerSection)
                {
                    // enableFlag 再用二进制显示
                    Debug.WriteLine($"RegionID: {mark.RegionID} -> EnableFlag: {mark.enableFlag} ({Convert.ToString(mark.enableFlag, 2).PadLeft(8, '0')})");
                }
            }
            else
            {
                MessageBox.Show("无法加载UISAVE.DAT文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

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
                        X = point.FloatX,
                        Y = point.FloatY,
                        Z = point.FloatZ,
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

                Clipboard.SetText(json);

                MessageBox.Show("导出成功！\nJSON数据已复制到剪贴板。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败!\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private WayMark? currentWayMark = null;

        private void WayMark_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (currentWayMark != null)
            {
                currentWayMark.PropertyChanged -= OnWayMarkPropertyChanged;
                UnsubscribeWayMarkPoints(currentWayMark);
            }

            if (WayMark_ListBox.SelectedItem is WayMark selectedMark)
            {
                currentWayMark = selectedMark;
                currentWayMark.PropertyChanged += OnWayMarkPropertyChanged;
                SubscribeWayMarkPoints(currentWayMark);
                UpdatePreview();

                Edit1_Grid.IsEnabled = true;
                Edit2_Grid.IsEnabled = true;
            }
            else
            {
                currentWayMark = null;
                Preview_Canvas.Children.Clear();

                Edit1_Grid.IsEnabled = false;
                Edit2_Grid.IsEnabled = false;
            }
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

            // Collect active points
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

            // Calculate BBOX
            float minX = points.Min(p => p.Point.FloatX);
            float maxX = points.Max(p => p.Point.FloatX);
            float minZ = points.Min(p => p.Point.FloatZ);
            float maxZ = points.Max(p => p.Point.FloatZ);

            float width = maxX - minX;
            float height = maxZ - minZ;

            // Check if all points are at the same spot or width/height is 0
            if (width < 1) width = 10;
            if (height < 1) height = 10;

            // Add padding (Requirement 3)
            float paddingX = width * 0.1f;
            float paddingZ = height * 0.1f;

            if (paddingX < 1) paddingX = 1;
            if (paddingZ < 1) paddingZ = 1;

            float displayMinX = minX - paddingX;
            float displayMaxX = maxX + paddingX;
            float displayMinZ = minZ - paddingZ;
            float displayMaxZ = maxZ + paddingZ;

            float displayWidth = displayMaxX - displayMinX;
            float displayHeight = displayMaxZ - displayMinZ;

            double canvasSize = Preview_Canvas.Width;
            if (double.IsNaN(canvasSize) || canvasSize <= 0) return;

            // Scale: pixels per game-unit. Fit display rect into square canvas.
            float maxDim = Math.Max(displayWidth, displayHeight);
            double scale = canvasSize / maxDim;

            // Image size based on slider
            double scaleRatio = Scale_Slider != null ? Scale_Slider.Value : 0.1;
            double markerSize = canvasSize * scaleRatio;

            // Recalculate padding based on marker size to prevent clipping
            // We need padding (in game units) such that: padding * scale >= markerSize / 2
            // Since padding affects scale, this is an iterative process or solvable equation.
            // Simplified approach: Calculate scale without padding first to estimate, or just ensure display rect is large enough.

            // Formula: minPadding = (maxContentDim * scaleRatio) / (2 * (1 - scaleRatio))
            // Ensure denominator is not 0
            if (scaleRatio >= 1.0) scaleRatio = 0.99;

            float maxContentDim = Math.Max(width, height);
            float requiredPadding = (float)((maxContentDim * scaleRatio) / (2 * (1 - scaleRatio)));

            // Apply minimum padding (10%) or required padding, whichever is larger
            paddingX = Math.Max(paddingX, requiredPadding);
            paddingZ = Math.Max(paddingZ, requiredPadding);

            displayMinX = minX - paddingX;
            displayMaxX = maxX + paddingX;
            displayMinZ = minZ - paddingZ;
            displayMaxZ = maxZ + paddingZ;

            displayWidth = displayMaxX - displayMinX;
            displayHeight = displayMaxZ - displayMinZ;

            // Recalculate scale
            maxDim = Math.Max(displayWidth, displayHeight);
            scale = canvasSize / maxDim;

            // Recalculate markerSize (pixels) based on new scale ratio (relative to canvas, so it stays same)
            // markerSize = canvasSize * scaleRatio; (Already set)

            double contentWidthPx = displayWidth * scale;
            double contentHeightPx = displayHeight * scale;

            // Center content in canvas
            double offsetX = (canvasSize - contentWidthPx) / 2;
            double offsetY = (canvasSize - contentHeightPx) / 2;

            foreach ((string Name, WayMarkPoint Point) p in points)
            {
                Image img = new();
                string imgName = p.Name.ToLower();

                try
                {
                    img.Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/Image/s_{imgName}.png"));
                }
                catch
                {
                }

                img.Width = markerSize;
                img.Height = markerSize;

                double relativeX = p.Point.FloatX - displayMinX;
                double relativeZ = p.Point.FloatZ - displayMinZ;

                double left = relativeX * scale + offsetX - (markerSize / 2);

                // Z grows down
                double top = relativeZ * scale + offsetY - (markerSize / 2);

                Shape bgShape;
                // Check if name starts with digit (1-4) or letter (A-D)
                if (!string.IsNullOrEmpty(p.Name) && char.IsDigit(p.Name[0]))
                {
                    bgShape = new Rectangle(); // 1-4: Square background
                }
                else
                {
                    bgShape = new Ellipse(); // A-D: Circle background
                }

                bgShape.Width = markerSize;
                bgShape.Height = markerSize;
                // Semi-transparent black background
                bgShape.Fill = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));

                Canvas.SetLeft(bgShape, left);
                Canvas.SetTop(bgShape, top);
                Preview_Canvas.Children.Add(bgShape);

                Canvas.SetLeft(img, left);
                Canvas.SetTop(img, top);
                Preview_Canvas.Children.Add(img);
            }
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