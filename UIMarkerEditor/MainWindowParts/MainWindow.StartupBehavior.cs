using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private bool startupWayMarkActionScheduled;

        private void ScheduleStartupWayMarkAction()
        {
            if (startupWayMarkActionScheduled) return;

            startupWayMarkActionScheduled = true;
            Dispatcher.BeginInvoke(new System.Action(RunStartupWayMarkAction));
        }

        private void RunStartupWayMarkAction()
        {
            switch (appDataStore.Settings.StartupWayMarkAction)
            {
                case StartupWayMarkAction.LoadMostRecentFile:
                    LoadMostRecentWayMarkFileOnStartup();
                    break;
                case StartupWayMarkAction.OpenFileDialog:
                    OpenWayMarkFile();
                    break;
                case StartupWayMarkAction.None:
                default:
                    break;
            }
        }

        private void LoadMostRecentWayMarkFileOnStartup()
        {
            List<string> recentFiles = appDataStore.GetRecentFiles();
            if (recentFiles.Count == 0) return;

            string filePath = recentFiles[0];
            if (!File.Exists(filePath))
            {
                MessageBox.Show(
                    this,
                    $"最近一次打开的标点文件已经不存在，已跳过启动自动加载。\n\n文件：{filePath}",
                    "启动自动加载",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshRecentFileMenu();
                return;
            }

            LoadConfigFile(filePath);
        }
    }
}
