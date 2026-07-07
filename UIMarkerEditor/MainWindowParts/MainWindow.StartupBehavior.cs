using System.Collections.Generic;
using System.IO;
using System.Windows;
using FF14ConfigEditor;

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
            StartupRecentFileSelection selection = StartupRecentFileSelector.SelectFirstExisting(recentFiles, File.Exists);
            if (!selection.HasRecentFiles) return;

            if (!selection.HasExistingFile)
            {
                AppMessageBox.Show(
                    this,
                    "最近打开的标点文件都已经不存在，已跳过启动自动加载。",
                    "启动自动加载",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshRecentFileMenu();
                return;
            }

            if (selection.SkippedMissingFiles)
            {
                AppLogger.Info(AppLogCategory.IO, $"启动自动加载跳过已不存在的最近文件，改用：{selection.FilePath}");
            }

            LoadConfigFileWithOverlay(selection.FilePath);
        }
    }
}
