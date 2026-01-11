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

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

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

            if (configUISave != null)
            {
                List<WayMark> markerSection = configUISave.Marks.WayMarks;

                WayMark_ListBox.ItemsSource = markerSection;

                // 输出所有的enableFlag和regionID以供调试
                foreach (WayMark mark in markerSection)
                {
                    // enableFlag 再用二进制显示
                    Debug.WriteLine($"RegionID: {mark.regionID} -> EnableFlag: {mark.enableFlag} ({Convert.ToString(mark.enableFlag, 2).PadLeft(8, '0')})");
                }
            }
            else
            {
                MessageBox.Show("无法加载UISAVE.DAT文件。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

    }
}