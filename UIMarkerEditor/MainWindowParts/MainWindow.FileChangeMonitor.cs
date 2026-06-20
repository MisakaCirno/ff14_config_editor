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
            StopCurrentFileChangeMonitor();

            loadedFileSnapshot = TryCreateCurrentFileSnapshot(filePath);
            if (loadedFileSnapshot == null)
            {
                return;
            }

            string fullPath = loadedFileSnapshot.Metadata.FullPath;
            string? directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            currentFileWatcher = new FileSystemWatcher(directory, fileName)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime
            };
            currentFileWatcher.Changed += CurrentFileWatcher_FileChanged;
            currentFileWatcher.Created += CurrentFileWatcher_FileChanged;
            currentFileWatcher.Deleted += CurrentFileWatcher_FileChanged;
            currentFileWatcher.Renamed += CurrentFileWatcher_Renamed;
            currentFileWatcher.Error += CurrentFileWatcher_Error;
            currentFileWatcher.EnableRaisingEvents = true;
            currentFilePollingTimer.Start();
            AppLogger.Info(AppLogCategory.IO, $"开始监听当前 UISAVE.DAT：{fullPath}，{FormatCurrentFileMetadata(loadedFileSnapshot.Metadata)}");
        }

        private void StopCurrentFileChangeMonitor()
        {
            currentFileChangeDebounceTimer.Stop();
            currentFilePollingTimer.Stop();

            if (currentFileWatcher != null)
            {
                currentFileWatcher.EnableRaisingEvents = false;
                currentFileWatcher.Changed -= CurrentFileWatcher_FileChanged;
                currentFileWatcher.Created -= CurrentFileWatcher_FileChanged;
                currentFileWatcher.Deleted -= CurrentFileWatcher_FileChanged;
                currentFileWatcher.Renamed -= CurrentFileWatcher_Renamed;
                currentFileWatcher.Error -= CurrentFileWatcher_Error;
                currentFileWatcher.Dispose();
                currentFileWatcher = null;
            }

            loadedFileSnapshot = null;
            promptedExternalFileSnapshot = null;
            hasPromptedCurrentFileMissing = false;
        }

        private void RefreshLoadedFileSnapshot()
        {
            loadedFileSnapshot = TryCreateCurrentFileSnapshot(currentFilePath);
            promptedExternalFileSnapshot = null;
            hasPromptedCurrentFileMissing = false;
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

        private bool ConfirmOverwriteExternallyChangedWayMarkFile()
        {
            CurrentFileExternalChangeState state = CheckCurrentFileExternalChange(showPrompt: false);
            if (state == CurrentFileExternalChangeState.Unchanged)
            {
                return true;
            }

            string message = state == CurrentFileExternalChangeState.Missing
                ? "当前 UISAVE.DAT 已被外部删除或暂时无法读取。\n\n继续保存会尝试重新写入这个文件，是否继续？"
                : "当前 UISAVE.DAT 已被外部更新。\n\n继续保存会用本窗口内容覆盖磁盘上的外部更新，是否继续？";

            return AppMessageBox.Show(
                this,
                message,
                "确认覆盖外部更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
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

                hasPromptedCurrentFileMissing = true;
            }
            else if (currentSnapshot != null)
            {
                if (currentSnapshot.Equals(promptedExternalFileSnapshot))
                {
                    return state;
                }

                promptedExternalFileSnapshot = currentSnapshot;
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

        private void PromptForCurrentFileExternalChange(CurrentFileExternalChangeState state)
        {
            isHandlingCurrentFileExternalChange = true;
            try
            {
                if (state == CurrentFileExternalChangeState.Missing)
                {
                    AppMessageBox.Show(
                        this,
                        "当前 UISAVE.DAT 已被外部删除或暂时无法读取。\n\n请确认文件状态后再继续编辑或保存。",
                        "当前文件不可读取",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string message = isWayMarkDirty
                    ? "当前 UISAVE.DAT 已被外部更新，且本窗口有未保存的修改。\n\n选择“是”重新读取磁盘上的最新内容并放弃本窗口未保存修改，选择“否”继续保留当前编辑。"
                    : "当前 UISAVE.DAT 已被外部更新。\n\n是否重新读取磁盘上的最新内容？";

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
            }
            finally
            {
                isHandlingCurrentFileExternalChange = false;
            }
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

        private sealed record CurrentFileMetadata(
            string FullPath,
            long Length,
            DateTime LastWriteTimeUtc);

        private sealed record CurrentFileSnapshot(
            CurrentFileMetadata Metadata,
            string Hash);
    }
}
