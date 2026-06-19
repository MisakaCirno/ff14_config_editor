using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UIMarkerEditor.Controls
{
    public partial class WayMarkEditPanelControl
    {
        public void ApplyAppearanceSettings(AppSettings settings)
        {
            bool useImageLabels = settings.UseWayMarkImageLabels;
            SetWayMarkCheckBoxContent(A_CheckBox, "A点", "Assets/Image/s_a.png", useImageLabels);
            SetWayMarkCheckBoxContent(B_CheckBox, "B点", "Assets/Image/s_b.png", useImageLabels);
            SetWayMarkCheckBoxContent(C_CheckBox, "C点", "Assets/Image/s_c.png", useImageLabels);
            SetWayMarkCheckBoxContent(D_CheckBox, "D点", "Assets/Image/s_d.png", useImageLabels);
            SetWayMarkCheckBoxContent(One_CheckBox, "1点", "Assets/Image/s_1.png", useImageLabels);
            SetWayMarkCheckBoxContent(Two_CheckBox, "2点", "Assets/Image/s_2.png", useImageLabels);
            SetWayMarkCheckBoxContent(Three_CheckBox, "3点", "Assets/Image/s_3.png", useImageLabels);
            SetWayMarkCheckBoxContent(Four_CheckBox, "4点", "Assets/Image/s_4.png", useImageLabels);
        }

        private static void SetWayMarkCheckBoxContent(
            CheckBox checkBox,
            string textLabel,
            string imagePath,
            bool useImageLabel)
        {
            checkBox.ToolTip = textLabel;
            AutomationProperties.SetName(checkBox, textLabel);
            checkBox.Content = useImageLabel
                ? CreateWayMarkImage(imagePath, textLabel)
                : textLabel;
        }

        private static FrameworkElement CreateWayMarkImage(string imagePath, string toolTip)
        {
            Image image = new()
            {
                Width = 28,
                Height = 24,
                ToolTip = toolTip,
                Source = new BitmapImage(new Uri($"pack://application:,,,/UIMarkerEditor;component/{imagePath}", UriKind.Absolute)),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
    }
}
