using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void OpenWayMarkFile()
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

                LoadConfigFile(filePath);
            }
        }

        private void OpenWayMarkFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            OpenWayMarkFile();
        }

        private void ReloadWayMarkFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            ReloadWayMarkFile();
        }

        private void SaveWayMarkFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SaveWayMarkFile();
        }

        private void CurrentWayMarkFileCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = HasLoadedWayMarkFile();
            e.Handled = true;
        }

        private bool HasLoadedWayMarkFile()
        {
            return !string.IsNullOrWhiteSpace(currentFilePath) && configUISave != null;
        }

        private void ReloadWayMarkFile()
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

        private void SaveWayMarkFile()
        {
            // 保存修改后的UISAVE.DAT文件
            if (configUISave != null)
            {
                if (appDataStore.Settings.AutoBackupBeforeSave)
                {
                    try
                    {
                        appDataStore.CreateBackup(configUISave.FilePath);
                        RefreshBackupList();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error(AppLogCategory.IO, $"保存前自动备份失败：{configUISave.FilePath}", ex);
                        MessageBox.Show($"保存前自动备份失败，已取消保存。\n{ex.Message}", "备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 在这里将修改后的数据写回UISAVE.DAT文件
                try
                {
                    configUISave.Save();
                }
                catch (UISaveFormatException ex)
                {
                    AppLogger.Error(AppLogCategory.UISaveFormat, $"保存 UISAVE.DAT 前结构校验失败：{configUISave.FilePath}", ex);
                    MessageBox.Show(this, $"UISAVE.DAT 结构校验失败，已取消保存，原文件未写入。\n\n文件：{configUISave.FilePath}\n\n诊断信息：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Error(AppLogCategory.IO, $"保存 UISAVE.DAT 失败：{configUISave.FilePath}", ex);
                    MessageBox.Show(this, $"保存 UISAVE.DAT 失败，原文件未确认写入完成。\n\n文件：{configUISave.FilePath}\n\n原因：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                MessageBox.Show("文件已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool LoadConfigFile(string filePath)
        {
            // 使用 ConfigUISave 类加载文件
            ConfigUISave loadedConfig;
            try
            {
                loadedConfig = new(filePath);
            }
            catch (UISaveFormatException ex)
            {
                AppLogger.Error(AppLogCategory.UISaveFormat, $"加载 UISAVE.DAT 格式失败：{filePath}", ex);
                MessageBox.Show(this, $"这个 UISAVE.DAT 的结构与当前工具已知格式不一致，已取消加载，当前文件保持不变。\n\n文件：{filePath}\n\n诊断信息：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error(AppLogCategory.IO, $"加载 UISAVE.DAT 失败：{filePath}", ex);
                MessageBox.Show(this, $"无法加载 UISAVE.DAT 文件。\n\n文件：{filePath}\n\n原因：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }

            SectionFMARKER? markerSection = loadedConfig.Marks;
            if (markerSection != null)
            {
                currentFilePath = filePath;
                configUISave = loadedConfig;
                RegisterLoadedCharacter(loadedConfig, filePath);
                UpdateCurrentFileStatus(filePath);
                appDataStore.AddRecentFile(filePath);
                RefreshRecentFileMenu();
                List<WayMark> wayMarks = markerSection.WayMarks;

                WayMarkEditor_Control.SetWayMarks(wayMarks);

                // 输出所有的enableFlag和regionID以供调试
                foreach (WayMark mark in wayMarks)
                {
                    // enableFlag 再用二进制显示
                    AppLogger.Debug(AppLogCategory.UI, $"RegionID: {mark.RegionID} -> EnableFlag: {mark.enableFlag} ({Convert.ToString(mark.enableFlag, 2).PadLeft(8, '0')})");
                }
            }
            else
            {
                MessageBox.Show(this, "无法在这个 UISAVE.DAT 中找到可编辑的 FMARKER 标点数据，当前已加载文件保持不变。", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }

            CommandManager.InvalidateRequerySuggested();
            return true;
        }

    }
}
