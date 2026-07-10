using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using FF14ConfigEditor;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private static readonly TimeSpan CurrentFileChangeDebounceInterval = TimeSpan.FromMilliseconds(700);
        private static readonly TimeSpan CurrentFilePollingInterval = TimeSpan.FromSeconds(1);

        private readonly DispatcherTimer currentFileChangeDebounceTimer = new()
        {
            Interval = CurrentFileChangeDebounceInterval
        };

        private readonly DispatcherTimer currentFilePollingTimer = new()
        {
            Interval = CurrentFilePollingInterval
        };

        private FileSystemWatcher? currentFileWatcher;
        private CurrentFileSnapshot? loadedFileSnapshot;
        private CurrentFileSnapshot? promptedExternalFileSnapshot;
        private bool hasPromptedCurrentFileMissing;
        private bool isHandlingCurrentFileExternalChange;

        private void InitializeCurrentFileChangeMonitor()
        {
            currentFileChangeDebounceTimer.Tick += CurrentFileChangeDebounceTimer_Tick;
            currentFilePollingTimer.Tick += CurrentFilePollingTimer_Tick;
        }

        private void StartCurrentFileChangeMonitor(string filePath)
        {
            PreparedCurrentFileChangeMonitor? preparedMonitor = null;
            try
            {
                preparedMonitor = PrepareCurrentFileChangeMonitor(filePath);
                CommitCurrentFileChangeMonitor(preparedMonitor);
                preparedMonitor = null;
            }
            finally
            {
                DisposePreparedCurrentFileChangeMonitor(preparedMonitor);
            }
        }

        private PreparedCurrentFileChangeMonitor PrepareCurrentFileChangeMonitor(string filePath)
        {
            CurrentFileSnapshot? snapshot = TryCreateCurrentFileSnapshot(filePath);
            if (snapshot == null)
            {
                return new PreparedCurrentFileChangeMonitor(null, null);
            }

            string fullPath = snapshot.Metadata.FullPath;
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            {
                return new PreparedCurrentFileChangeMonitor(snapshot, null);
            }

            FileSystemWatcher watcher = new(directory, fileName)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime
            };
            watcher.Changed += CurrentFileWatcher_FileChanged;
            watcher.Created += CurrentFileWatcher_FileChanged;
            watcher.Deleted += CurrentFileWatcher_FileChanged;
            watcher.Renamed += CurrentFileWatcher_Renamed;
            watcher.Error += CurrentFileWatcher_Error;

            return new PreparedCurrentFileChangeMonitor(snapshot, watcher);
        }

        private void CommitCurrentFileChangeMonitor(PreparedCurrentFileChangeMonitor preparedMonitor)
        {
            StopCurrentFileChangeMonitor();

            loadedFileSnapshot = preparedMonitor.Snapshot;
            currentFileWatcher = preparedMonitor.Watcher;
            if (currentFileWatcher == null || loadedFileSnapshot == null)
            {
                return;
            }

            try
            {
                currentFileWatcher.EnableRaisingEvents = true;
                currentFilePollingTimer.Start();
                AppLogger.Info(AppLogCategory.IO, $"开始监听当前 UISAVE.DAT：{loadedFileSnapshot.Metadata.FullPath}，{FormatCurrentFileMetadata(loadedFileSnapshot.Metadata)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                AppLogger.Warning(AppLogCategory.IO, $"监听当前 UISAVE.DAT 文件变化失败：{loadedFileSnapshot.Metadata.FullPath}", ex);
                DisposeCurrentFileWatcher(currentFileWatcher);
                currentFileWatcher = null;
            }
        }

        private void StopCurrentFileChangeMonitor()
        {
            currentFileChangeDebounceTimer.Stop();
            currentFilePollingTimer.Stop();

            FileSystemWatcher? watcher = currentFileWatcher;
            currentFileWatcher = null;
            if (watcher != null)
            {
                DisposeCurrentFileWatcher(watcher);
            }

            loadedFileSnapshot = null;
            promptedExternalFileSnapshot = null;
            hasPromptedCurrentFileMissing = false;
        }

        private void DisposePreparedCurrentFileChangeMonitor(PreparedCurrentFileChangeMonitor? preparedMonitor)
        {
            if (preparedMonitor?.Watcher != null)
            {
                DisposeCurrentFileWatcher(preparedMonitor.Watcher);
            }
        }

        private void DisposeCurrentFileWatcher(FileSystemWatcher watcher)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= CurrentFileWatcher_FileChanged;
                watcher.Created -= CurrentFileWatcher_FileChanged;
                watcher.Deleted -= CurrentFileWatcher_FileChanged;
                watcher.Renamed -= CurrentFileWatcher_Renamed;
                watcher.Error -= CurrentFileWatcher_Error;
                watcher.Dispose();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
                AppLogger.Warning(AppLogCategory.IO, "释放当前 UISAVE.DAT 文件监听器失败", ex);
            }
        }

        private void RefreshLoadedFileSnapshot()
        {
            loadedFileSnapshot = TryCreateCurrentFileSnapshot(currentFilePath);
            promptedExternalFileSnapshot = null;
            hasPromptedCurrentFileMissing = false;
            ClearCurrentFileMissingStatusIfNeeded();
        }

        private void CurrentFileWatcher_FileChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsCurrentWayMarkFilePath(e.FullPath))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ScheduleCurrentFileChangeCheck));
        }

        private void CurrentFileWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            if (!IsCurrentWayMarkFilePath(e.FullPath) && !IsCurrentWayMarkFilePath(e.OldFullPath))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ScheduleCurrentFileChangeCheck));
        }

        private void CurrentFileWatcher_Error(object sender, ErrorEventArgs e)
        {
            AppLogger.Warning(AppLogCategory.IO, "监听当前 UISAVE.DAT 文件变化失败", e.GetException());
        }

        private void ScheduleCurrentFileChangeCheck()
        {
            currentFileChangeDebounceTimer.Stop();
            currentFileChangeDebounceTimer.Start();
        }

        private void CurrentFileChangeDebounceTimer_Tick(object? sender, EventArgs e)
        {
            currentFileChangeDebounceTimer.Stop();
            CheckCurrentFileExternalChange(showPrompt: true);
        }

        private void CurrentFilePollingTimer_Tick(object? sender, EventArgs e)
        {
            CheckCurrentFileExternalChange(showPrompt: true);
        }

        private CurrentFileSaveDecision ResolveCurrentFileSaveDecision(bool allowMissingFileRecreate)
        {
            CurrentFileExternalChangeState state = allowMissingFileRecreate
                ? GetCurrentFileExternalChangeState(out _)
                : CheckCurrentFileExternalChange(showPrompt: false);
            if (state == CurrentFileExternalChangeState.Unchanged)
            {
                return CurrentFileSaveDecision.Save;
            }

            if (state == CurrentFileExternalChangeState.Missing)
            {
                return allowMissingFileRecreate
                    ? CurrentFileSaveDecision.RecreateMissingFile
                    : PromptForMissingCurrentFileBeforeSave();
            }

            return AppMessageBox.Show(
                this,
                "当前 UISAVE.DAT 已被外部更新。\n\n继续保存会用本窗口内容覆盖磁盘上的外部更新，是否继续？",
                "确认覆盖外部更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes
                    ? CurrentFileSaveDecision.Save
                    : CurrentFileSaveDecision.Cancel;
        }

        private CurrentFileExternalChangeState CheckCurrentFileExternalChange(bool showPrompt)
        {
            if (isHandlingCurrentFileExternalChange || !HasLoadedWayMarkFile() || loadedFileSnapshot == null)
            {
                return CurrentFileExternalChangeState.Unchanged;
            }

            CurrentFileExternalChangeState state = GetCurrentFileExternalChangeState(out CurrentFileSnapshot? currentSnapshot);
            if (!showPrompt || state == CurrentFileExternalChangeState.Unchanged)
            {
                return state;
            }

            if (state == CurrentFileExternalChangeState.Missing)
            {
                if (hasPromptedCurrentFileMissing)
                {
                    return state;
                }

                PromptForCurrentFileExternalChange(state);
                return state;
            }
            else if (currentSnapshot != null)
            {
                if (currentSnapshot.Equals(promptedExternalFileSnapshot))
                {
                    return state;
                }

                if (PromptForCurrentFileExternalChange(state))
                {
                    promptedExternalFileSnapshot = currentSnapshot;
                }

                return state;
            }

            PromptForCurrentFileExternalChange(state);
            return state;
        }

        private CurrentFileExternalChangeState GetCurrentFileExternalChangeState(out CurrentFileSnapshot? currentSnapshot)
        {
            currentSnapshot = null;

            CurrentFileSnapshot? loadedSnapshot = loadedFileSnapshot;
            if (loadedSnapshot == null)
            {
                return CurrentFileExternalChangeState.Unchanged;
            }

            CurrentFileMetadata? currentMetadata = TryCreateCurrentFileMetadata(currentFilePath);
            if (currentMetadata == null)
            {
                return CurrentFileExternalChangeState.Missing;
            }

            if (currentMetadata.Equals(loadedSnapshot.Metadata))
            {
                ClearCurrentFileMissingStatusIfNeeded();
                promptedExternalFileSnapshot = null;
                hasPromptedCurrentFileMissing = false;
                return CurrentFileExternalChangeState.Unchanged;
            }

            AppLogger.Debug(AppLogCategory.IO, $"当前 UISAVE.DAT 元数据变化：原 {FormatCurrentFileMetadata(loadedSnapshot.Metadata)}；新 {FormatCurrentFileMetadata(currentMetadata)}");

            currentSnapshot = TryCreateCurrentFileSnapshot(currentMetadata);
            if (currentSnapshot == null)
            {
                return CurrentFileExternalChangeState.Missing;
            }

            ClearCurrentFileMissingStatusIfNeeded();

            if (string.Equals(currentSnapshot.Hash, loadedSnapshot.Hash, StringComparison.Ordinal))
            {
                AppLogger.Debug(AppLogCategory.IO, "当前 UISAVE.DAT 元数据已变化，但内容 Hash 未变化。");
                loadedFileSnapshot = currentSnapshot;
                promptedExternalFileSnapshot = null;
                hasPromptedCurrentFileMissing = false;
                return CurrentFileExternalChangeState.Unchanged;
            }

            AppLogger.Info(AppLogCategory.IO, "当前 UISAVE.DAT 内容已被外部更新。");
            hasPromptedCurrentFileMissing = false;
            return CurrentFileExternalChangeState.Updated;
        }

        private bool PromptForCurrentFileExternalChange(CurrentFileExternalChangeState state)
        {
            isHandlingCurrentFileExternalChange = true;
            try
            {
                if (state == CurrentFileExternalChangeState.Missing)
                {
                    HandleMissingCurrentFileDialogResult(
                        ShowCurrentFileMissingDialog());
                    return true;
                }

                bool hasInvalidPendingWayMarkEdits = !TryCommitPendingWayMarkEdits(showValidationMessage: false);
                string message = BuildCurrentFileUpdatedMessage(hasInvalidPendingWayMarkEdits);

                MessageBoxResult result = AppMessageBox.Show(
                    this,
                    message,
                    "当前文件已更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    LoadConfigFileWithOverlay(currentFilePath);
                }

                return true;
            }
            finally
            {
                isHandlingCurrentFileExternalChange = false;
            }
        }

        private CurrentFileSaveDecision PromptForMissingCurrentFileBeforeSave()
        {
            return ShowCurrentFileMissingDialog() switch
            {
                CurrentFileMissingDialogResult.SaveToOriginalPath => ResolveCurrentFileSaveDecision(allowMissingFileRecreate: true),
                CurrentFileMissingDialogResult.CloseCurrentFile => CloseMissingCurrentFileAndCancelSave(),
                _ => AcknowledgeMissingCurrentFileAndCancelSave()
            };
        }

        private CurrentFileSaveDecision CloseMissingCurrentFileAndCancelSave()
        {
            CloseCurrentWayMarkFile();
            return CurrentFileSaveDecision.Cancel;
        }

        private CurrentFileSaveDecision AcknowledgeMissingCurrentFileAndCancelSave()
        {
            AcknowledgeMissingCurrentFile();
            return CurrentFileSaveDecision.Cancel;
        }

        private void HandleMissingCurrentFileDialogResult(CurrentFileMissingDialogResult result)
        {
            switch (result)
            {
                case CurrentFileMissingDialogResult.SaveToOriginalPath:
                    if (!SaveWayMarkFile(showSuccessMessage: true, allowMissingFileRecreate: true))
                    {
                        AcknowledgeMissingCurrentFile();
                    }
                    break;
                case CurrentFileMissingDialogResult.CloseCurrentFile:
                    CloseCurrentWayMarkFile();
                    break;
                default:
                    AcknowledgeMissingCurrentFile();
                    break;
            }
        }

        private CurrentFileMissingDialogResult ShowCurrentFileMissingDialog()
        {
            bool wasHandlingCurrentFileExternalChange = isHandlingCurrentFileExternalChange;
            isHandlingCurrentFileExternalChange = true;
            try
            {
                CurrentFileMissingDialog dialog = new(currentFilePath);
                DialogOwnerHelper.ConfigureOwnedDialog(dialog, this);
                dialog.ShowDialog();
                return dialog.Result;
            }
            finally
            {
                isHandlingCurrentFileExternalChange = wasHandlingCurrentFileExternalChange;
            }
        }

        private void AcknowledgeMissingCurrentFile()
        {
            if (!HasLoadedWayMarkFile())
            {
                return;
            }

            hasPromptedCurrentFileMissing = true;
            UpdateCurrentFileMissingStatus(currentFilePath);
        }

        private string BuildCurrentFileUpdatedMessage(bool hasInvalidPendingWayMarkEdits)
        {
            if (hasInvalidPendingWayMarkEdits && isWayMarkDirty)
            {
                return "当前 UISAVE.DAT 已被外部更新，且本窗口有未保存的修改或未完成的坐标输入。\n\n选择“是”重新读取磁盘上的最新内容并放弃本窗口未保存修改和未完成输入，选择“否”继续保留当前编辑。";
            }

            if (hasInvalidPendingWayMarkEdits)
            {
                return "当前 UISAVE.DAT 已被外部更新，且当前坐标输入未完成或超出可保存范围。\n\n选择“是”重新读取磁盘上的最新内容并放弃未完成输入，选择“否”继续编辑当前输入。";
            }

            if (isWayMarkDirty)
            {
                return "当前 UISAVE.DAT 已被外部更新，且本窗口有未保存的修改。\n\n选择“是”重新读取磁盘上的最新内容并放弃本窗口未保存修改，选择“否”继续保留当前编辑。";
            }

            return "当前 UISAVE.DAT 已被外部更新。\n\n是否重新读取磁盘上的最新内容？";
        }

        private bool IsCurrentWayMarkFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(currentFilePath) || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(currentFilePath),
                    Path.GetFullPath(filePath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        private static CurrentFileSnapshot? TryCreateCurrentFileSnapshot(string filePath)
        {
            CurrentFileMetadata? metadata = TryCreateCurrentFileMetadata(filePath);
            return metadata == null ? null : TryCreateCurrentFileSnapshot(metadata);
        }

        private static CurrentFileMetadata? TryCreateCurrentFileMetadata(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                string fullPath = Path.GetFullPath(filePath);
                FileInfo fileInfo = new(fullPath);
                if (!fileInfo.Exists)
                {
                    return null;
                }

                return new CurrentFileMetadata(
                    fullPath,
                    fileInfo.Length,
                    fileInfo.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        private static CurrentFileSnapshot? TryCreateCurrentFileSnapshot(CurrentFileMetadata metadata)
        {
            try
            {
                using FileStream stream = new(
                    metadata.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                string hash = Convert.ToHexString(SHA256.HashData(stream));
                return new CurrentFileSnapshot(
                    metadata,
                    hash);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        private static string FormatCurrentFileMetadata(CurrentFileMetadata metadata)
        {
            return $"Length={metadata.Length}, LastWriteTimeUtc={metadata.LastWriteTimeUtc:O}";
        }

        private enum CurrentFileExternalChangeState
        {
            Unchanged,
            Updated,
            Missing
        }

        private enum CurrentFileSaveDecision
        {
            Save,
            RecreateMissingFile,
            Cancel
        }

        private sealed record CurrentFileMetadata(
            string FullPath,
            long Length,
            DateTime LastWriteTimeUtc);

        private sealed record CurrentFileSnapshot(
            CurrentFileMetadata Metadata,
            string Hash);

        private sealed record PreparedCurrentFileChangeMonitor(
            CurrentFileSnapshot? Snapshot,
            FileSystemWatcher? Watcher);
    }
}
