using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private bool isWayMarkFileLoading;

        private void OpenWayMarkFile()
        {
            if (isWayMarkFileLoading)
            {
                return;
            }

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

            string? initialDirectory = WayMarkOpenDirectoryResolver.Resolve(
                appDataStore.Settings.WayMarkOpenDirectoryMode,
                appDataStore.Settings.WayMarkCustomDirectory,
                appDataStore.Settings.GameInstallDirectory);
            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                openFileDialog.InitialDirectory = initialDirectory;
            }

            if (DialogOwnerHelper.ShowCommonDialog(openFileDialog, this) == true)
            {
                string filePath = openFileDialog.FileName;

                // 强校验：必须是 UISAVE.dat（忽略大小写）
                if (!string.Equals(System.IO.Path.GetFileName(filePath), "UISAVE.dat", StringComparison.OrdinalIgnoreCase))
                {
                    AppMessageBox.Show("只能选择名为 UISAVE.dat 的文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!ConfirmSaveOrDiscardWayMarkChanges())
                {
                    return;
                }

                LoadConfigFileWithOverlay(filePath);
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

        private void CloseWayMarkFileCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            CloseWayMarkFile();
        }

        private void CurrentWayMarkFileCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = HasLoadedWayMarkFile() && !isWayMarkFileLoading;
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
                if (!ConfirmSaveOrDiscardWayMarkChanges())
                {
                    return;
                }

                LoadConfigFileWithOverlay(currentFilePath);
            }
            else
            {
                AppMessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private bool SaveWayMarkFile(bool showSuccessMessage = true, bool allowMissingFileRecreate = false)
        {
            if (isWayMarkFileLoading)
            {
                return false;
            }

            if (!TryCommitPendingWayMarkEdits())
            {
                return false;
            }

            // 保存修改后的UISAVE.DAT文件
            if (configUISave != null)
            {
                CurrentFileSaveDecision saveDecision = ResolveCurrentFileSaveDecision(allowMissingFileRecreate);
                if (saveDecision == CurrentFileSaveDecision.Cancel)
                {
                    return false;
                }

                bool recreateMissingFile = saveDecision == CurrentFileSaveDecision.RecreateMissingFile;
                if (!recreateMissingFile && appDataStore.Settings.AutoBackupBeforeSave)
                {
                    if (appDataStore.IsTrustedGameCharacterSaveFile(configUISave.FilePath))
                    {
                        try
                        {
                            appDataStore.CreateBackup(
                                configUISave.FilePath,
                                creationTrigger: BackupCreationTriggers.BeforeSave);
                            RefreshBackupList();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error(AppLogCategory.IO, $"保存前自动备份失败：{configUISave.FilePath}", ex);
                            AppMessageBox.Show($"保存前自动备份失败，已取消保存。\n{ex.Message}", "备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
                            return false;
                        }
                    }
                }

                // 在这里将修改后的数据写回UISAVE.DAT文件
                try
                {
                    configUISave.Save();
                    RefreshLoadedFileSnapshot();
                    if (recreateMissingFile)
                    {
                        CreateBackupAfterDeletedFileRecreate(configUISave.FilePath);
                    }
                }
                catch (UISaveFormatException ex)
                {
                    AppLogger.Error(AppLogCategory.UISaveFormat, $"保存 UISAVE.DAT 前结构校验失败：{configUISave.FilePath}", ex);
                    AppMessageBox.Show(this, $"UISAVE.DAT 结构校验失败，已取消保存，原文件未写入。\n\n文件：{configUISave.FilePath}\n\n诊断信息：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                catch (Exception ex)
                {
                    AppLogger.Error(AppLogCategory.IO, $"保存 UISAVE.DAT 失败：{configUISave.FilePath}", ex);
                    AppMessageBox.Show(this, $"保存 UISAVE.DAT 失败，原文件未确认写入完成。\n\n文件：{configUISave.FilePath}\n\n原因：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                SetWayMarkDirty(false);
                if (showSuccessMessage)
                {
                    ToastService.ShowSuccess("文件已保存。");
                }

                return true;
            }
            else
            {
                AppMessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
        }

        private bool CloseWayMarkFile(bool showSuccessMessage = true)
        {
            if (isWayMarkFileLoading)
            {
                return false;
            }

            if (!HasLoadedWayMarkFile())
            {
                AppMessageBox.Show("请先加载一个UISAVE.DAT文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            if (!TryPrepareCloseWayMarkFileChanges(out bool shouldSave))
            {
                return false;
            }

            if (shouldSave && !SaveWayMarkFile(showSuccessMessage: false))
            {
                return false;
            }

            CloseCurrentWayMarkFile();
            if (showSuccessMessage)
            {
                ToastService.ShowSuccess("当前文件已关闭。");
            }

            return true;
        }

        private void CloseCurrentWayMarkFile()
        {
            string closedFilePath = currentFilePath;
            StopCurrentFileChangeMonitor();

            try
            {
                suppressWayMarkDirtyTracking = true;
                WayMarkEditor_Control.ClearWayMarks();
                wayMarkChangeTracker.Clear();
            }
            finally
            {
                suppressWayMarkDirtyTracking = false;
            }

            currentFilePath = string.Empty;
            configUISave = null;
            SetWayMarkDirty(false);
            ResetCurrentFileStatus();
            UpdateWindowTitle();
            CommandManager.InvalidateRequerySuggested();

            if (!string.IsNullOrWhiteSpace(closedFilePath))
            {
                AppLogger.Info(AppLogCategory.UI, $"已关闭当前 UISAVE.DAT：{closedFilePath}");
            }
        }

        private void CreateBackupAfterDeletedFileRecreate(string filePath)
        {
            try
            {
                appDataStore.CreateBackup(
                    filePath,
                    creationTrigger: BackupCreationTriggers.AfterDeletedFileRecreate);
                RefreshBackupList();
            }
            catch (Exception ex)
            {
                AppLogger.Warning(AppLogCategory.IO, $"删除后重建备份失败：{filePath}", ex);
                AppMessageBox.Show(
                    this,
                    $"已成功保存 UISAVE.DAT，但删除后重建备份失败。\n\n文件：{filePath}\n\n原因：{ex.Message}",
                    "重建后备份失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CreateAutomaticBackupAfterLoad(string filePath)
        {
            if (!appDataStore.Settings.AutoBackupAfterLoad)
            {
                return;
            }

            if (!appDataStore.IsTrustedGameCharacterSaveFile(filePath))
            {
                return;
            }

            try
            {
                appDataStore.CreateBackup(
                    filePath,
                    creationTrigger: BackupCreationTriggers.AfterLoad);
                RefreshBackupList();
            }
            catch (Exception ex)
            {
                AppLogger.Warning(AppLogCategory.IO, $"读取后自动备份失败：{filePath}", ex);
                AppMessageBox.Show(this, $"已成功读取 UISAVE.DAT，但读取后自动备份失败。\n\n文件：{filePath}\n\n原因：{ex.Message}", "自动备份失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void LoadConfigFileWithOverlay(string filePath)
        {
            try
            {
                await LoadConfigFileWithOverlayAsync(filePath);
            }
            catch (Exception ex)
            {
                AppLogger.Error(AppLogCategory.IO, $"加载 UISAVE.DAT 时更新界面状态失败：{filePath}", ex);
                AppMessageBox.Show(this, $"加载 UISAVE.DAT 时更新界面状态失败。\n\n文件：{filePath}\n\n原因：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> LoadConfigFileWithOverlayAsync(string filePath)
        {
            if (isWayMarkFileLoading)
            {
                return false;
            }

            isWayMarkFileLoading = true;
            CommandManager.InvalidateRequerySuggested();

            try
            {
                WayMarkEditor_Control.SetLoadingOverlayVisible(true);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                return LoadConfigFile(filePath);
            }
            finally
            {
                WayMarkEditor_Control.SetLoadingOverlayVisible(false);
                isWayMarkFileLoading = false;
                CommandManager.InvalidateRequerySuggested();
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
                AppMessageBox.Show(this, $"这个 UISAVE.DAT 的结构与当前工具已知格式不一致，已取消加载，当前文件保持不变。\n\n文件：{filePath}\n\n诊断信息：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error(AppLogCategory.IO, $"加载 UISAVE.DAT 失败：{filePath}", ex);
                AppMessageBox.Show(this, $"无法加载 UISAVE.DAT 文件。\n\n文件：{filePath}\n\n原因：{ex.Message}", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }

            SectionFMARKER? markerSection = loadedConfig.Marks;
            if (markerSection == null)
            {
                AppMessageBox.Show(this, "无法在这个 UISAVE.DAT 中找到可编辑的 FMARKER 标点数据，当前已加载文件保持不变。", "加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
                CommandManager.InvalidateRequerySuggested();
                return false;
            }

            List<WayMark> wayMarks = markerSection.WayMarks;
            PreparedCurrentFileChangeMonitor? preparedMonitor = null;
            try
            {
                preparedMonitor = PrepareCurrentFileChangeMonitor(filePath);

                try
                {
                    ApplyLoadedWayMarksToEditor(wayMarks);
                }
                catch
                {
                    RestoreWayMarkEditorAfterLoadFailure();
                    throw;
                }

                currentFilePath = filePath;
                configUISave = loadedConfig;
                CommitCurrentFileChangeMonitor(preparedMonitor);
                preparedMonitor = null;
                TryRegisterLoadedCharacter(loadedConfig, filePath);
                UpdateCurrentFileStatus(filePath);
                appDataStore.AddRecentFile(filePath);
                RefreshRecentFileMenu();
                SetWayMarkDirty(false);
                UpdateWindowTitle();
                TryRecordGameInstallDirectoryFromLoadedSaveFile(filePath);
                CreateAutomaticBackupAfterLoad(filePath);

                AppLogger.Info(AppLogCategory.UI, $"已读取 UISAVE.DAT：{filePath}，可编辑标点槽位 {wayMarks.Count} 个。");
            }
            finally
            {
                DisposePreparedCurrentFileChangeMonitor(preparedMonitor);
            }

            CommandManager.InvalidateRequerySuggested();
            return true;
        }

        private void ApplyLoadedWayMarksToEditor(List<WayMark> wayMarks)
        {
            try
            {
                suppressWayMarkDirtyTracking = true;
                WayMarkEditor_Control.SetWayMarks(wayMarks);
                TrackWayMarkChanges(wayMarks);
            }
            finally
            {
                suppressWayMarkDirtyTracking = false;
            }
        }

        private void RestoreWayMarkEditorAfterLoadFailure()
        {
            try
            {
                suppressWayMarkDirtyTracking = true;
                if (configUISave?.Marks?.WayMarks is List<WayMark> currentWayMarks)
                {
                    WayMarkEditor_Control.SetWayMarks(currentWayMarks);
                    return;
                }

                WayMarkEditor_Control.ClearWayMarks();
            }
            catch (Exception ex)
            {
                AppLogger.Warning(AppLogCategory.UI, "加载 UISAVE.DAT 失败后恢复旧标点界面失败", ex);
            }
            finally
            {
                suppressWayMarkDirtyTracking = false;
            }
        }

        private void TryRegisterLoadedCharacter(ConfigUISave loadedConfig, string filePath)
        {
            try
            {
                RegisterLoadedCharacter(loadedConfig, filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AppDataStoreException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                AppLogger.Warning(AppLogCategory.IO, $"自动登记已加载角色失败：{filePath}", ex);
            }
        }

        private void TryRecordGameInstallDirectoryFromLoadedSaveFile(string filePath)
        {
            try
            {
                GameInstallDirectoryUpdateResult result = appDataStore.SetGameInstallDirectoryFromLoadedSaveFile(filePath);
                if (result is not (GameInstallDirectoryUpdateResult.Updated or GameInstallDirectoryUpdateResult.Relocated))
                {
                    return;
                }

                ToolSettings_Control.RefreshGameInstallDirectoryFromSettings();
                StartLocalCharacterScan();
                if (result == GameInstallDirectoryUpdateResult.Relocated)
                {
                    AppMessageBox.Show(
                        this,
                        "检测到游戏位置移动，已重新获取游戏位置。",
                        "游戏位置已更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                ToastService.ShowSuccess("已根据当前文件记录游戏安装目录。");
            }
            catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException or IOException or UnauthorizedAccessException)
            {
                AppLogger.Warning(AppLogCategory.IO, $"根据 UISAVE.DAT 路径记录游戏安装目录失败：{filePath}", ex);
            }
        }

    }
}
