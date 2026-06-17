using System.Windows.Controls;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void AddDeveloperToolsTab()
        {
#if DEBUG
            MainTab_Control.Items.Add(new TabItem
            {
                Header = "开发工具",
                Content = new DeveloperToolsControl()
            });
#endif
        }
    }
}
