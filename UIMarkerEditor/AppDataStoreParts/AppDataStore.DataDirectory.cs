using System;
using System.Collections.Generic;
using System.IO;
using FF14ConfigEditor;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    private const int MigrationStateVersion = 1;
    private const string MigrationStagePreparing = "Preparing";
    private const string MigrationStageCopying = "Copying";
    private const string MigrationStageVerifying = "Verifying";
    private const string MigrationStageReadyToCommit = "ReadyToCommit";
    private const string MigrationStageCommitted = "Committed";
    private const string MigrationStageCleaningOldDirectory = "CleaningOldDirectory";
    private const string MigrationStageCompleted = "Completed";
    private const string MigrationStageFailed = "Failed";
    private const string DataDirectoryMigrationWriteBlockedMessage =
        "工具数据目录正在迁移，本次写入已取消。请在迁移完成后重试。";
    private static readonly string[] ManagedDataDirectoryNames =
    [
        ConfigsFolderName,
        BackupsFolderName,
        CacheFolderName,
        LogsFolderName
    ];

    public DataDirectoryMigrationResult ChangeDataDirectory(string newDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(newDataDirectory))
        {
            throw new InvalidOperationException("数据目录不能为空。");
        }

        string oldDataDirectory = Path.GetFullPath(DataDirectory);
        string targetDirectory = Path.GetFullPath(newDataDirectory);
        if (IsSameDirectory(oldDataDirectory, targetDirectory))
        {
            return new DataDirectoryMigrationResult
            {
                CleanupCompleted = true,
                MigratedFileCount = 0,
                SourceDirectory = oldDataDirectory,
                TargetDirectory = targetDirectory,
                MigrationStateFilePath = MigrationStateFilePath
            };
        }

        using IDisposable migrationWriteBlock = EnterDataDirectoryMigrationWriteBlock();
        AppDataStateSnapshot previousState = CreateAppDataStateSnapshot();
        DataDirectoryMigrationState? migrationState = null;
        try
        {
            AppLogger.SetLogFilePath(null);
            migrationState = CreateDataDirectoryMigrationState(oldDataDirectory, targetDirectory);
            SaveMigrationState(migrationState);
            CopyDataDirectoryFiles(migrationState);
            VerifyDataDirectoryFiles(migrationState);
            MarkMigrationState(migrationState, MigrationStageReadyToCommit, "复制和哈希校验已完成，准备切换工具数据目录。");

            DataDirectory = migrationState.TargetDataDirectory;
            EnsureDataDirectory();
            LoadSettings();
            LoadCharacters();
            LoadWayMarkFavorites();
            LoadServerList();
            LoadMapDataCacheForCurrentSource();
            MarkMigrationState(migrationState, MigrationStageCommitted, "工具数据目录已切换到新目录，准备清理旧目录。");
            SaveBootstrap(allowOverwriteInvalid: true);
        }
        catch (Exception ex)
        {
            TryMarkMigrationFailed(migrationState, ex);
            RestoreAppDataState(previousState);
            throw;
        }

        bool cleanupCompleted = CompleteDataDirectoryMigrationCleanup(migrationState);
        DataDirectoryMigrationResult result = CreateDataDirectoryMigrationResult(
            migrationState,
            cleanupCompleted,
            automaticRetryAttempted: false);
        if (cleanupCompleted)
        {
            ClearMigrationStateFile();
            ConfigureLoggerIfMigrationCleanupAllows();
        }
        else if (migrationCleanupPending)
        {
            AppLogger.SetLogFilePath(null);
        }
        else
        {
            ConfigureLoggerIfMigrationCleanupAllows();
        }

        return result;
    }

    public async Task<DataDirectoryMigrationResult> ChangeDataDirectoryAsync(
        string newDataDirectory,
        IProgress<DataDirectoryMigrationProgress>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(newDataDirectory))
        {
            throw new InvalidOperationException("数据目录不能为空。");
        }

        string oldDataDirectory = Path.GetFullPath(DataDirectory);
        string targetDirectory = Path.GetFullPath(newDataDirectory);
        if (IsSameDirectory(oldDataDirectory, targetDirectory))
        {
            ReportMigrationProgress(progress, "无需迁移", "新目录与当前目录一致。", 1, 1);
            return new DataDirectoryMigrationResult
            {
                CleanupCompleted = true,
                MigratedFileCount = 0,
                SourceDirectory = oldDataDirectory,
                TargetDirectory = targetDirectory,
                MigrationStateFilePath = MigrationStateFilePath
            };
        }

        using IDisposable migrationWriteBlock = EnterDataDirectoryMigrationWriteBlock();
        AppDataStateSnapshot previousState = CreateAppDataStateSnapshot();
        DataDirectoryMigrationState? migrationState = null;
        int completedSteps = 0;
        int totalSteps = 1;
        try
        {
            AppLogger.SetLogFilePath(null);
            ReportMigrationProgress(progress, "准备迁移", "扫描可迁移目录。", completedSteps, totalSteps);
            migrationState = CreateDataDirectoryMigrationState(oldDataDirectory, targetDirectory);
            totalSteps = GetMigrationProgressTotalSteps(migrationState);
            ReportMigrationProgress(progress, "准备迁移", migrationState.CurrentOperation, completedSteps, totalSteps);
            SaveMigrationState(migrationState);
            completedSteps = await CopyDataDirectoryFilesAsync(migrationState, progress, completedSteps, totalSteps);
            completedSteps = await VerifyDataDirectoryFilesAsync(migrationState, progress, completedSteps, totalSteps);
            MarkMigrationState(migrationState, MigrationStageReadyToCommit, "复制和哈希校验已完成，准备切换工具数据目录。");

            completedSteps++;
            ReportMigrationProgress(progress, "切换数据目录", "重新读取新目录中的工具数据。", completedSteps, totalSteps);
            await Task.Yield();

            DataDirectory = migrationState.TargetDataDirectory;
            EnsureDataDirectory();
            LoadSettings();
            LoadCharacters();
            LoadWayMarkFavorites();
            LoadServerList();
            LoadMapDataCacheForCurrentSource();
            MarkMigrationState(migrationState, MigrationStageCommitted, "工具数据目录已切换到新目录，准备清理旧目录。");
            SaveBootstrap(allowOverwriteInvalid: true);
        }
        catch (Exception ex)
        {
            TryMarkMigrationFailed(migrationState, ex);
            RestoreAppDataState(previousState);
            throw;
        }

        bool cleanupCompleted = await CompleteDataDirectoryMigrationCleanupAsync(migrationState, progress, completedSteps, totalSteps);
        DataDirectoryMigrationResult result = CreateDataDirectoryMigrationResult(
            migrationState,
            cleanupCompleted,
            automaticRetryAttempted: false);
        if (cleanupCompleted)
        {
            ClearMigrationStateFile();
            ConfigureLoggerIfMigrationCleanupAllows();
        }
        else if (migrationCleanupPending)
        {
            AppLogger.SetLogFilePath(null);
        }
        else
        {
            ConfigureLoggerIfMigrationCleanupAllows();
        }

        string completionMessage = result.CleanupCompleted
            ? result.OldDirectoryRetained
                ? "迁移完成，旧目录仍保留非本工具管理的内容。"
                : "迁移完成，旧目录已清理。"
            : "迁移完成，旧目录中仍有受管文件未清理。";
        ReportMigrationProgress(progress, "迁移完成", completionMessage, totalSteps, totalSteps);
        return result;
    }

    private IDisposable EnterDataDirectoryMigrationWriteBlock()
    {
        lock (dataDirectoryManagedFileWriteGate)
        {
            dataDirectoryMigrationWriteBlockCount++;
        }

        return new DataDirectoryMigrationWriteBlock(this);
    }

    private bool IsDataDirectoryMigrationWriteBlocked()
    {
        return System.Threading.Volatile.Read(ref dataDirectoryMigrationWriteBlockCount) > 0;
    }

    private void ExecuteDataDirectoryManagedWrite(Action writeAction)
    {
        lock (dataDirectoryManagedFileWriteGate)
        {
            if (IsDataDirectoryMigrationWriteBlocked())
            {
                throw new InvalidOperationException(DataDirectoryMigrationWriteBlockedMessage);
            }

            writeAction();
        }
    }

    private T ExecuteDataDirectoryManagedWrite<T>(Func<T> writeAction)
    {
        lock (dataDirectoryManagedFileWriteGate)
        {
            if (IsDataDirectoryMigrationWriteBlocked())
            {
                throw new InvalidOperationException(DataDirectoryMigrationWriteBlockedMessage);
            }

            return writeAction();
        }
    }

    private bool TryExecuteDataDirectoryManagedWrite(Action writeAction)
    {
        lock (dataDirectoryManagedFileWriteGate)
        {
            if (IsDataDirectoryMigrationWriteBlocked())
            {
                return false;
            }

            writeAction();
            return true;
        }
    }

    private void ExitDataDirectoryMigrationWriteBlock()
    {
        lock (dataDirectoryManagedFileWriteGate)
        {
            dataDirectoryMigrationWriteBlockCount = Math.Max(0, dataDirectoryMigrationWriteBlockCount - 1);
        }
    }

    private sealed class DataDirectoryMigrationWriteBlock(AppDataStore owner) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owner.ExitDataDirectoryMigrationWriteBlock();
        }
    }

    private void RecoverInterruptedDataDirectoryMigration()
    {
        JsonFileReadResult<DataDirectoryMigrationState> stateResult = ReadJsonFile<DataDirectoryMigrationState>(MigrationStateFilePath);
        if (stateResult.Status == JsonFileReadStatus.Missing)
        {
            return;
        }

        if (stateResult.Status == JsonFileReadStatus.Invalid || stateResult.Value == null)
        {
            AddDataLoadWarning(
                $"migration-state:{Path.GetFullPath(MigrationStateFilePath)}",
                $"检测到上次工具数据目录迁移状态文件无法读取，已继续使用当前启动配置指向的数据目录。{Environment.NewLine}" +
                $"文件：{MigrationStateFilePath}{Environment.NewLine}" +
                $"原因：{stateResult.Error?.Message}");
            return;
        }

        DataDirectoryMigrationState state = stateResult.Value;
        if (!IsMigrationStateValid(state))
        {
            AddDataLoadWarning(
                $"migration-state-invalid:{Path.GetFullPath(MigrationStateFilePath)}",
                $"检测到上次工具数据目录迁移状态不完整，已继续使用当前启动配置指向的数据目录。{Environment.NewLine}" +
                $"文件：{MigrationStateFilePath}");
            return;
        }

        AppDataStateSnapshot previousState = CreateAppDataStateSnapshot();
        try
        {
            switch (state.Stage)
            {
                case MigrationStagePreparing:
                case MigrationStageCopying:
                case MigrationStageVerifying:
                    RecoverPreCommitMigration(state);
                    break;
                case MigrationStageReadyToCommit:
                    RecoverReadyToCommitMigration(state);
                    break;
                case MigrationStageCommitted:
                case MigrationStageCleaningOldDirectory:
                    RecoverCommittedMigration(state);
                    break;
                case MigrationStageCompleted:
                    migrationReports.Add(CreateDataDirectoryMigrationResult(state, cleanupCompleted: true, automaticRetryAttempted: true));
                    ClearMigrationStateFile();
                    break;
                default:
                    AddDataLoadWarning(
                        $"migration-interrupted:{state.Id}",
                        $"检测到上次工具数据目录迁移在完成前中断，工具已继续使用当前启动配置指向的数据目录。{Environment.NewLine}" +
                        $"如需重新迁移，请重新选择一个空的数据目录。{Environment.NewLine}" +
                        $"迁移状态文件：{MigrationStateFilePath}{Environment.NewLine}" +
                        $"源目录：{state.SourceDataDirectory}{Environment.NewLine}" +
                        $"新目录：{state.TargetDataDirectory}{Environment.NewLine}" +
                        $"中断阶段：{state.Stage}");
                    break;
            }
        }
        catch (Exception ex)
        {
            TryMarkMigrationFailed(state, ex);
            RestoreAppDataState(previousState);
            AddDataLoadWarning(
                $"migration-recovery-failed:{state.Id}",
                $"自动恢复上次工具数据目录迁移失败，已继续使用当前启动配置指向的数据目录。{Environment.NewLine}" +
                $"迁移状态文件：{MigrationStateFilePath}{Environment.NewLine}" +
                $"原因：{ex.Message}");
        }
    }

    private void RecoverPreCommitMigration(DataDirectoryMigrationState state)
    {
        EnsureMigrationStateDirectoriesAllowed(state);
        Directory.CreateDirectory(state.TargetDataDirectory);
        DeleteMigrationTempFiles(state);
        EnsurePreCommitRecoveryTargetContainsOnlyMigrationArtifacts(state);
        CopyDataDirectoryFiles(state);
        VerifyDataDirectoryFiles(state);
        MarkMigrationState(state, MigrationStageReadyToCommit, "启动时检测到迁移在复制或校验阶段中断，已重新复制并完成校验。");
        RecoverReadyToCommitMigration(state);
    }

    private void RecoverReadyToCommitMigration(DataDirectoryMigrationState state)
    {
        EnsureMigrationStateDirectoriesAllowed(state);
        VerifyMigrationTargetFromState(state);
        if (Directory.Exists(state.SourceDataDirectory))
        {
            VerifyMigrationSourceFromState(state);
        }

        DataDirectory = state.TargetDataDirectory;
        MarkMigrationState(state, MigrationStageCommitted, "启动时检测到迁移已完成复制和校验，已切换到新目录。");
        SaveBootstrap(allowOverwriteInvalid: true);
        bool cleanupCompleted = CompleteDataDirectoryMigrationCleanup(state);
        if (cleanupCompleted)
        {
            migrationReports.Add(CreateDataDirectoryMigrationResult(state, cleanupCompleted, automaticRetryAttempted: true));
            ClearMigrationStateFile();
        }
        else
        {
            migrationReports.Add(CreateDataDirectoryMigrationResult(state, cleanupCompleted, automaticRetryAttempted: true));
        }
    }

    private void RecoverCommittedMigration(DataDirectoryMigrationState state)
    {
        EnsureMigrationStateDirectoriesAllowed(state);
        VerifyMigrationTargetFromState(state, includeDeletedSourceFiles: false);
        DataDirectory = state.TargetDataDirectory;
        MarkMigrationState(state, MigrationStageCommitted, "启动时检测到迁移已切换到新目录，继续清理旧目录。");
        SaveBootstrap(allowOverwriteInvalid: true);
        bool cleanupCompleted = CompleteDataDirectoryMigrationCleanup(state);
        if (cleanupCompleted)
        {
            migrationReports.Add(CreateDataDirectoryMigrationResult(state, cleanupCompleted, automaticRetryAttempted: true));
            ClearMigrationStateFile();
        }
        else
        {
            migrationReports.Add(CreateDataDirectoryMigrationResult(state, cleanupCompleted, automaticRetryAttempted: true));
        }
    }

    private void EnsureDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(ConfigsDirectory);
            Directory.CreateDirectory(BackupsDirectory);
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(LogDirectory);
            EnsureUserMapDataFileExistsCore();
            if (!File.Exists(SettingsFilePath))
            {
                WriteJson(SettingsFilePath, Settings);
            }

            if (!File.Exists(CharactersFilePath))
            {
                WriteJson(CharactersFilePath, new List<CharacterProfile>());
            }

            if (!File.Exists(WayMarkFavoritesFilePath))
            {
                WriteJson(WayMarkFavoritesFilePath, new WayMarkFavoritesData());
            }
        }
        catch (AppDataStoreException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppDataStoreException("准备本地数据目录", DataDirectory, ex);
        }
    }

    private void ConfigureLoggerIfMigrationCleanupAllows()
    {
        if (migrationCleanupPending)
        {
            AppLogger.SetLogFilePath(null);
            return;
        }

        ConfigureLogger();
    }

    private void ConfigureLogger()
    {
        AppLogger.ConfigureFileLogging(
            LogFilePath,
            (long)Settings.MaxLogFileSizeMb * 1024 * 1024,
            Settings.MaxLogFileCount);
        AppLogger.Info(
            AppLogCategory.General,
            $"日志已启用：{LogFilePath}；单个文件上限 {Settings.MaxLogFileSizeMb} MB，最多保留 {Settings.MaxLogFileCount} 个文件。");
    }

    public int ClearLogFiles()
    {
        try
        {
            return AppLogger.ClearLogFiles();
        }
        catch (Exception ex)
        {
            string logDirectory = Path.GetDirectoryName(LogFilePath) ?? LogFilePath;
            throw new AppDataStoreException("清理日志文件", logDirectory, ex);
        }
    }

    public int ClearCurrentLogFile()
    {
        try
        {
            return AppLogger.ClearCurrentLogFile();
        }
        catch (Exception ex)
        {
            throw new AppDataStoreException("清理当前日志文件", LogFilePath, ex);
        }
    }

    public string? ArchiveCurrentLogFile()
    {
        try
        {
            string? archivePath = AppLogger.ArchiveCurrentLogFile();
            if (!string.IsNullOrWhiteSpace(archivePath))
            {
                AppLogger.Info(AppLogCategory.General, $"已手动归档当前日志：{archivePath}");
            }

            return archivePath;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AppDataStoreException("归档当前日志文件", LogFilePath, ex);
        }
    }

    private void SaveBootstrap(bool allowOverwriteInvalid = false)
    {
        if (bootstrapFileInvalid && !allowOverwriteInvalid)
        {
            return;
        }

        WriteJson(BootstrapFilePath, new BootstrapSettings { DataDirectory = DataDirectory });
        bootstrapFileInvalid = false;
    }

    private static void VerifyDirectoryWritable(string directory)
    {
        string testFile = Path.Combine(directory, $".write_test_{Guid.NewGuid():N}");
        File.WriteAllText(testFile, string.Empty);
        File.Delete(testFile);
    }

    private static void EnsureMigrationStateDirectoriesAllowed(DataDirectoryMigrationState state)
    {
        state.SourceDataDirectory = NormalizeDataDirectoryPath(state.SourceDataDirectory);
        state.TargetDataDirectory = NormalizeDataDirectoryPath(state.TargetDataDirectory);
        EnsureDataDirectoryIsNotRoot(state.SourceDataDirectory, "迁移源数据目录");
        EnsureDataDirectoryIsNotRoot(state.TargetDataDirectory, "迁移目标数据目录");
        EnsureDataDirectoriesDoNotOverlap(
            state.SourceDataDirectory,
            state.TargetDataDirectory,
            "迁移源数据目录",
            "迁移目标数据目录");
    }

    private static void EnsureDataDirectoryIsNotRoot(string directory, string description)
    {
        if (IsRootDataDirectory(directory))
        {
            throw new InvalidOperationException($"{description}不能是磁盘根目录或共享根目录，请选择一个空的专用文件夹。");
        }
    }

    private static bool IsRootDataDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string normalizedPath = NormalizeDataDirectoryPath(fullPath);
        string? root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrWhiteSpace(root) &&
            string.Equals(normalizedPath, NormalizeDataDirectoryPath(root), StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureDataDirectoriesDoNotOverlap(
        string sourceDirectory,
        string targetDirectory,
        string sourceDescription,
        string targetDescription)
    {
        if (IsSameDirectory(sourceDirectory, targetDirectory))
        {
            throw new InvalidOperationException($"{sourceDescription}和{targetDescription}不能是同一个目录。");
        }

        if (IsSubdirectoryOf(targetDirectory, sourceDirectory))
        {
            throw new InvalidOperationException($"{targetDescription}不能位于{sourceDescription}内部，请选择其它位置后再迁移。");
        }

        if (IsSubdirectoryOf(sourceDirectory, targetDirectory))
        {
            throw new InvalidOperationException($"{sourceDescription}不能位于{targetDescription}内部，请选择其它位置后再迁移。");
        }
    }

    private static void DeleteMigrationTempFiles(DataDirectoryMigrationState state)
    {
        if (!Directory.Exists(state.TargetDataDirectory))
        {
            return;
        }

        foreach (string relativePath in EnumerateRelativeFiles(state.TargetDataDirectory))
        {
            if (!IsMigrationTempFile(state, relativePath))
            {
                continue;
            }

            File.Delete(Path.Combine(state.TargetDataDirectory, relativePath));
        }
    }

    private static void EnsurePreCommitRecoveryTargetContainsOnlyMigrationArtifacts(DataDirectoryMigrationState state)
    {
        if (!Directory.Exists(state.TargetDataDirectory))
        {
            return;
        }

        HashSet<string> expectedFiles = state.Files
            .Select(file => NormalizeRelativePath(file.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedDirectories = state.Directories
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string relativeFile in expectedFiles)
        {
            AddParentDirectories(expectedDirectories, relativeFile);
        }

        foreach (string relativeDirectory in EnumerateRelativeDirectories(state.TargetDataDirectory))
        {
            string normalizedDirectory = NormalizeRelativePath(relativeDirectory);
            if (!expectedDirectories.Contains(normalizedDirectory))
            {
                throw new InvalidOperationException($"恢复迁移时新目录包含未知目录，已停止恢复以避免覆盖数据：{relativeDirectory}");
            }
        }

        foreach (string relativePath in EnumerateRelativeFiles(state.TargetDataDirectory))
        {
            if (IsMigrationTempFile(state, relativePath))
            {
                continue;
            }

            string normalizedPath = NormalizeRelativePath(relativePath);
            if (!expectedFiles.Contains(normalizedPath))
            {
                throw new InvalidOperationException($"恢复迁移时新目录包含未知文件，已停止恢复以避免覆盖数据：{relativePath}");
            }

            string sourceFile = Path.Combine(state.SourceDataDirectory, relativePath);
            if (!File.Exists(sourceFile))
            {
                throw new FileNotFoundException("迁移源目录缺少状态文件记录的文件，已停止恢复。", sourceFile);
            }

            string targetFile = Path.Combine(state.TargetDataDirectory, relativePath);
            string sourceHash = ComputeSha256(sourceFile);
            string targetHash = ComputeSha256(targetFile);
            if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"恢复迁移时目标文件已存在且内容与源文件不一致，已停止恢复以避免覆盖数据：{relativePath}");
            }
        }
    }

    private static void AddParentDirectories(HashSet<string> directories, string relativePath)
    {
        string? directory = Path.GetDirectoryName(relativePath);
        while (!string.IsNullOrWhiteSpace(directory))
        {
            directories.Add(NormalizeRelativePath(directory));
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static bool IsMigrationTempFile(DataDirectoryMigrationState state, string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        if (!fileName.StartsWith('.') || !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relativeDirectory = Path.GetDirectoryName(relativePath) ?? string.Empty;
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            string expectedDirectory = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
            if (!string.Equals(relativeDirectory, expectedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string prefix = $".{Path.GetFileName(file.RelativePath)}.";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int tokenStart = prefix.Length;
            int tokenLength = fileName.Length - prefix.Length - ".tmp".Length;
            return tokenLength == 32 && fileName
                .Substring(tokenStart, tokenLength)
                .All(Uri.IsHexDigit);
        }

        return false;
    }

    private DataDirectoryMigrationState CreateDataDirectoryMigrationState(string sourceDirectory, string targetDirectory)
    {
        string sourceFullPath = NormalizeDataDirectoryPath(sourceDirectory);
        string targetFullPath = NormalizeDataDirectoryPath(targetDirectory);
        EnsureDataDirectoryIsNotRoot(sourceFullPath, "当前数据目录");
        EnsureDataDirectoryIsNotRoot(targetFullPath, "新数据目录");
        EnsureDataDirectoriesDoNotOverlap(sourceFullPath, targetFullPath, "当前数据目录", "新数据目录");

        if (!Directory.Exists(sourceFullPath))
        {
            throw new DirectoryNotFoundException($"当前数据目录不存在，无法迁移：{sourceFullPath}");
        }

        Directory.CreateDirectory(targetFullPath);
        VerifyDirectoryWritable(targetFullPath);
        if (Directory.EnumerateFileSystemEntries(targetFullPath).Any())
        {
            throw new InvalidOperationException("新数据目录必须为空。为避免覆盖已有数据，请选择一个空目录后再迁移。");
        }

        List<string> directories = EnumerateManagedRelativeDirectories(sourceFullPath);
        List<DataDirectoryMigrationFileState> files = EnumerateManagedRelativeFiles(sourceFullPath)
            .Select(relativePath =>
            {
                string sourceFilePath = Path.Combine(sourceFullPath, relativePath);
                return new DataDirectoryMigrationFileState
                {
                    RelativePath = relativePath,
                    Length = new FileInfo(sourceFilePath).Length
                };
            })
            .ToList();

        return new DataDirectoryMigrationState
        {
            Version = MigrationStateVersion,
            Id = Guid.NewGuid().ToString("N"),
            SourceDataDirectory = sourceFullPath,
            TargetDataDirectory = targetFullPath,
            Stage = MigrationStagePreparing,
            StartedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
            CurrentOperation = "准备迁移工具数据目录。",
            Directories = directories,
            Files = files
        };
    }

    private void CopyDataDirectoryFiles(DataDirectoryMigrationState state)
    {
        MarkMigrationState(state, MigrationStageCopying, "开始逐文件复制工具数据。");
        foreach (string relativeDirectory in state.Directories)
        {
            Directory.CreateDirectory(Path.Combine(state.TargetDataDirectory, relativeDirectory));
        }

        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            string? targetFileDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetFileDirectory))
            {
                Directory.CreateDirectory(targetFileDirectory);
            }

            state.CurrentOperation = $"复制文件：{file.RelativePath}";
            SaveMigrationState(state);
            SafeFileWriter.Copy(sourceFile, targetFile);
            file.Copied = true;
            state.CurrentOperation = $"文件复制完成：{file.RelativePath}";
            SaveMigrationState(state);
        }
    }

    private void VerifyDataDirectoryFiles(DataDirectoryMigrationState state)
    {
        MarkMigrationState(state, MigrationStageVerifying, "开始校验迁移文件 SHA-256。");
        EnsureSourceFileListMatchesState(state);
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            string sourceHash = ComputeSha256(sourceFile);
            string targetHash = ComputeSha256(targetFile);
            if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"迁移校验失败，文件哈希不一致：{file.RelativePath}");
            }

            file.Sha256 = sourceHash;
            file.Verified = true;
            state.CurrentOperation = $"哈希校验完成：{file.RelativePath}";
            SaveMigrationState(state);
        }

        VerifyMigrationSourceFromState(state);
        VerifyMigrationTargetFromState(state);
    }

    private void VerifyMigrationSourceFromState(DataDirectoryMigrationState state)
    {
        EnsureSourceFileListMatchesState(state);
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Sha256))
            {
                throw new InvalidOperationException($"迁移状态缺少文件哈希，无法验证源目录：{file.RelativePath}");
            }

            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            string sourceHash = ComputeSha256(sourceFile);
            if (!string.Equals(sourceHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"迁移期间源文件发生变化，已停止切换数据目录：{file.RelativePath}");
            }
        }
    }

    private void VerifyMigrationTargetFromState(DataDirectoryMigrationState state, bool includeDeletedSourceFiles = true)
    {
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            if (!includeDeletedSourceFiles && file.DeletedFromSource)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(file.Sha256))
            {
                throw new InvalidOperationException($"迁移状态缺少文件哈希，无法验证新目录：{file.RelativePath}");
            }

            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            if (!File.Exists(targetFile))
            {
                throw new FileNotFoundException("迁移新目录缺少已校验文件。", targetFile);
            }

            string targetHash = ComputeSha256(targetFile);
            if (!string.Equals(targetHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"迁移目标文件哈希不一致，已停止清理旧目录：{file.RelativePath}");
            }
        }
    }

    private bool CompleteDataDirectoryMigrationCleanup(DataDirectoryMigrationState state)
    {
        MarkMigrationState(state, MigrationStageCleaningOldDirectory, "开始清理旧数据目录中已校验的文件。");
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            if (file.DeletedFromSource)
            {
                continue;
            }

            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            try
            {
                if (!File.Exists(sourceFile))
                {
                    file.DeletedFromSource = true;
                    SaveMigrationState(state);
                    continue;
                }

                string targetHash = ComputeSha256(targetFile);
                if (!string.Equals(targetHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SetMigrationErrorMessage(state, $"目标文件哈希不一致，已跳过旧文件清理：{file.RelativePath}");
                    SaveMigrationState(state);
                    continue;
                }

                string sourceHash = ComputeSha256(sourceFile);
                if (!string.Equals(sourceHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SetMigrationErrorMessage(state, $"旧目录文件在迁移后发生变化，已保留该文件：{file.RelativePath}");
                    SaveMigrationState(state);
                    continue;
                }

                state.CurrentOperation = $"删除旧文件：{file.RelativePath}";
                SaveMigrationState(state);
                File.Delete(sourceFile);
                file.DeletedFromSource = true;
                state.CurrentOperation = $"旧文件已删除：{file.RelativePath}";
                SaveMigrationState(state);
            }
            catch (Exception ex)
            {
                SetMigrationErrorMessage(state, $"清理旧文件失败：{sourceFile}；原因：{ex.Message}");
                SaveMigrationState(state);
            }
        }

        foreach (string relativeDirectory in state.Directories.OrderByDescending(directory => directory.Length))
        {
            string sourceDirectory = Path.Combine(state.SourceDataDirectory, relativeDirectory);
            if (!TryDeleteDirectoryIfEmpty(sourceDirectory, out Exception? deleteError))
            {
                string reason = deleteError?.Message ?? "目录内仍有非本工具管理的内容或迁移后新增内容。";
                SetMigrationErrorMessage(state, $"清理旧目录失败：{sourceDirectory}；原因：{reason}");
                SaveMigrationState(state);
            }
        }

        if (!TryDeleteDirectoryIfEmpty(state.SourceDataDirectory, out Exception? rootDeleteError))
        {
            if (Directory.Exists(state.SourceDataDirectory))
            {
                SetMigrationErrorMessage(
                    state,
                    $"旧数据目录未能完全清理：{state.SourceDataDirectory}；原因：{rootDeleteError?.Message ?? "目录内仍有非本工具管理的内容或迁移后新增内容。"}");
            }
            SaveMigrationState(state);
        }

        bool managedCleanupCompleted = !HasPendingMigrationSourceFiles(state);
        if (managedCleanupCompleted)
        {
            migrationCleanupPending = false;
            bool oldDirectoryRetained = Directory.Exists(state.SourceDataDirectory);
            state.Stage = MigrationStageCompleted;
            state.CurrentOperation = oldDirectoryRetained
                ? "受管数据已清理完成，旧目录仍保留非本工具管理的内容。"
                : "旧数据目录已清理完成，迁移结束。";
            if (!oldDirectoryRetained)
            {
                state.ErrorMessage = string.Empty;
            }

            SaveMigrationState(state);
            return true;
        }

        migrationCleanupPending = true;
        return false;
    }

    private async Task<int> CopyDataDirectoryFilesAsync(
        DataDirectoryMigrationState state,
        IProgress<DataDirectoryMigrationProgress>? progress,
        int completedSteps,
        int totalSteps)
    {
        MarkMigrationState(state, MigrationStageCopying, "开始逐文件复制工具数据。");
        ReportMigrationProgress(progress, "复制文件", state.CurrentOperation, completedSteps, totalSteps);
        foreach (string relativeDirectory in state.Directories)
        {
            Directory.CreateDirectory(Path.Combine(state.TargetDataDirectory, relativeDirectory));
        }

        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            string? targetFileDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrWhiteSpace(targetFileDirectory))
            {
                Directory.CreateDirectory(targetFileDirectory);
            }

            state.CurrentOperation = $"复制文件：{file.RelativePath}";
            SaveMigrationState(state);
            ReportMigrationProgress(progress, "复制文件", state.CurrentOperation, completedSteps, totalSteps);
            await Task.Run(() => SafeFileWriter.Copy(sourceFile, targetFile));
            file.Copied = true;
            completedSteps++;
            state.CurrentOperation = $"文件复制完成：{file.RelativePath}";
            SaveMigrationState(state);
            ReportMigrationProgress(progress, "复制文件", state.CurrentOperation, completedSteps, totalSteps);
        }

        await Task.Yield();
        return completedSteps;
    }

    private async Task<int> VerifyDataDirectoryFilesAsync(
        DataDirectoryMigrationState state,
        IProgress<DataDirectoryMigrationProgress>? progress,
        int completedSteps,
        int totalSteps)
    {
        MarkMigrationState(state, MigrationStageVerifying, "开始校验迁移文件 SHA-256。");
        ReportMigrationProgress(progress, "校验文件", state.CurrentOperation, completedSteps, totalSteps);
        EnsureSourceFileListMatchesState(state);
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            ReportMigrationProgress(progress, "校验文件", $"计算哈希：{file.RelativePath}", completedSteps, totalSteps);
            var hashes = await Task.Run(() =>
            {
                string sourceHash = ComputeSha256(sourceFile);
                string targetHash = ComputeSha256(targetFile);
                return new { Source = sourceHash, Target = targetHash };
            });
            if (!string.Equals(hashes.Source, hashes.Target, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"迁移校验失败，文件哈希不一致：{file.RelativePath}");
            }

            file.Sha256 = hashes.Source;
            file.Verified = true;
            completedSteps++;
            state.CurrentOperation = $"哈希校验完成：{file.RelativePath}";
            SaveMigrationState(state);
            ReportMigrationProgress(progress, "校验文件", state.CurrentOperation, completedSteps, totalSteps);
        }

        completedSteps = await VerifyMigrationSourceFromStateAsync(state, progress, completedSteps, totalSteps);
        completedSteps = await VerifyMigrationTargetFromStateAsync(state, includeDeletedSourceFiles: true, progress, completedSteps, totalSteps);
        return completedSteps;
    }

    private async Task<int> VerifyMigrationSourceFromStateAsync(
        DataDirectoryMigrationState state,
        IProgress<DataDirectoryMigrationProgress>? progress,
        int completedSteps,
        int totalSteps)
    {
        EnsureSourceFileListMatchesState(state);
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Sha256))
            {
                throw new InvalidOperationException($"迁移状态缺少文件哈希，无法验证源目录：{file.RelativePath}");
            }

            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            ReportMigrationProgress(progress, "复核源目录", $"复核源文件：{file.RelativePath}", completedSteps, totalSteps);
            string sourceHash = await Task.Run(() => ComputeSha256(sourceFile));
            if (!string.Equals(sourceHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"迁移期间源文件发生变化，已停止切换数据目录：{file.RelativePath}");
            }

            completedSteps++;
            ReportMigrationProgress(progress, "复核源目录", $"源文件复核完成：{file.RelativePath}", completedSteps, totalSteps);
        }

        return completedSteps;
    }

    private async Task<int> VerifyMigrationTargetFromStateAsync(
        DataDirectoryMigrationState state,
        bool includeDeletedSourceFiles,
        IProgress<DataDirectoryMigrationProgress>? progress,
        int completedSteps,
        int totalSteps)
    {
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            if (!includeDeletedSourceFiles && file.DeletedFromSource)
            {
                completedSteps++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(file.Sha256))
            {
                throw new InvalidOperationException($"迁移状态缺少文件哈希，无法验证新目录：{file.RelativePath}");
            }

            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            if (!File.Exists(targetFile))
            {
                throw new FileNotFoundException("迁移新目录缺少已校验文件。", targetFile);
            }

            ReportMigrationProgress(progress, "复核新目录", $"复核新目录文件：{file.RelativePath}", completedSteps, totalSteps);
            string targetHash = await Task.Run(() => ComputeSha256(targetFile));
            if (!string.Equals(targetHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"迁移目标文件哈希不一致，已停止清理旧目录：{file.RelativePath}");
            }

            completedSteps++;
            ReportMigrationProgress(progress, "复核新目录", $"新目录文件复核完成：{file.RelativePath}", completedSteps, totalSteps);
        }

        return completedSteps;
    }

    private async Task<bool> CompleteDataDirectoryMigrationCleanupAsync(
        DataDirectoryMigrationState state,
        IProgress<DataDirectoryMigrationProgress>? progress,
        int completedSteps,
        int totalSteps)
    {
        MarkMigrationState(state, MigrationStageCleaningOldDirectory, "开始清理旧数据目录中已校验的文件。");
        ReportMigrationProgress(progress, "清理旧目录", state.CurrentOperation, completedSteps, totalSteps);
        foreach (DataDirectoryMigrationFileState file in state.Files)
        {
            if (file.DeletedFromSource)
            {
                completedSteps++;
                continue;
            }

            string sourceFile = Path.Combine(state.SourceDataDirectory, file.RelativePath);
            string targetFile = Path.Combine(state.TargetDataDirectory, file.RelativePath);
            try
            {
                if (!File.Exists(sourceFile))
                {
                    file.DeletedFromSource = true;
                    completedSteps++;
                    SaveMigrationState(state);
                    ReportMigrationProgress(progress, "清理旧目录", $"旧文件已不存在：{file.RelativePath}", completedSteps, totalSteps);
                    continue;
                }

                ReportMigrationProgress(progress, "清理旧目录", $"校验待清理文件：{file.RelativePath}", completedSteps, totalSteps);
                string targetHash = await Task.Run(() => ComputeSha256(targetFile));
                if (!string.Equals(targetHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SetMigrationErrorMessage(state, $"目标文件哈希不一致，已跳过旧文件清理：{file.RelativePath}");
                    completedSteps++;
                    SaveMigrationState(state);
                    ReportMigrationProgress(progress, "清理旧目录", $"已跳过旧文件：{file.RelativePath}", completedSteps, totalSteps);
                    continue;
                }

                string sourceHash = await Task.Run(() => ComputeSha256(sourceFile));
                if (!string.Equals(sourceHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SetMigrationErrorMessage(state, $"旧目录文件在迁移后发生变化，已保留该文件：{file.RelativePath}");
                    completedSteps++;
                    SaveMigrationState(state);
                    ReportMigrationProgress(progress, "清理旧目录", $"已保留变化文件：{file.RelativePath}", completedSteps, totalSteps);
                    continue;
                }

                state.CurrentOperation = $"删除旧文件：{file.RelativePath}";
                SaveMigrationState(state);
                ReportMigrationProgress(progress, "清理旧目录", state.CurrentOperation, completedSteps, totalSteps);
                await Task.Run(() => File.Delete(sourceFile));
                file.DeletedFromSource = true;
                completedSteps++;
                state.CurrentOperation = $"旧文件已删除：{file.RelativePath}";
                SaveMigrationState(state);
                ReportMigrationProgress(progress, "清理旧目录", state.CurrentOperation, completedSteps, totalSteps);
            }
            catch (Exception ex)
            {
                SetMigrationErrorMessage(state, $"清理旧文件失败：{sourceFile}；原因：{ex.Message}");
                completedSteps++;
                SaveMigrationState(state);
                ReportMigrationProgress(progress, "清理旧目录", $"清理旧文件失败：{file.RelativePath}", completedSteps, totalSteps);
            }
        }

        ReportMigrationProgress(progress, "清理旧目录", "清理旧目录结构。", Math.Min(completedSteps + 1, totalSteps), totalSteps);
        foreach (string relativeDirectory in state.Directories.OrderByDescending(directory => directory.Length))
        {
            string sourceDirectory = Path.Combine(state.SourceDataDirectory, relativeDirectory);
            if (!TryDeleteDirectoryIfEmpty(sourceDirectory, out Exception? deleteError))
            {
                string reason = deleteError?.Message ?? "目录内仍有非本工具管理的内容或迁移后新增内容。";
                SetMigrationErrorMessage(state, $"清理旧目录失败：{sourceDirectory}；原因：{reason}");
                SaveMigrationState(state);
            }
        }

        if (!TryDeleteDirectoryIfEmpty(state.SourceDataDirectory, out Exception? rootDeleteError))
        {
            if (Directory.Exists(state.SourceDataDirectory))
            {
                SetMigrationErrorMessage(
                    state,
                    $"旧数据目录未能完全清理：{state.SourceDataDirectory}；原因：{rootDeleteError?.Message ?? "目录内仍有非本工具管理的内容或迁移后新增内容。"}");
            }
            SaveMigrationState(state);
        }

        bool managedCleanupCompleted = !HasPendingMigrationSourceFiles(state);
        if (managedCleanupCompleted)
        {
            migrationCleanupPending = false;
            bool oldDirectoryRetained = Directory.Exists(state.SourceDataDirectory);
            state.Stage = MigrationStageCompleted;
            state.CurrentOperation = oldDirectoryRetained
                ? "受管数据已清理完成，旧目录仍保留非本工具管理的内容。"
                : "旧数据目录已清理完成，迁移结束。";
            if (!oldDirectoryRetained)
            {
                state.ErrorMessage = string.Empty;
            }

            SaveMigrationState(state);
            ReportMigrationProgress(progress, "清理旧目录", state.CurrentOperation, Math.Max(totalSteps - 1, completedSteps), totalSteps);
            return true;
        }

        migrationCleanupPending = true;
        ReportMigrationProgress(progress, "清理旧目录", "旧目录仍有受管文件未清理。", Math.Max(totalSteps - 1, completedSteps), totalSteps);
        return false;
    }

    private static int GetMigrationProgressTotalSteps(DataDirectoryMigrationState state)
    {
        return Math.Max(1, state.Files.Count * 5 + 3);
    }

    private static void ReportMigrationProgress(
        IProgress<DataDirectoryMigrationProgress>? progress,
        string stageName,
        string currentOperation,
        int completedSteps,
        int totalSteps)
    {
        if (progress == null)
        {
            return;
        }

        int safeTotalSteps = Math.Max(1, totalSteps);
        progress.Report(new DataDirectoryMigrationProgress
        {
            StageName = stageName,
            CurrentOperation = currentOperation,
            CompletedSteps = Math.Clamp(completedSteps, 0, safeTotalSteps),
            TotalSteps = safeTotalSteps
        });
    }

    private static bool HasPendingMigrationSourceFiles(DataDirectoryMigrationState state)
    {
        return state.Files.Any(file => !file.DeletedFromSource);
    }

    private static void SetMigrationErrorMessage(DataDirectoryMigrationState state, string message)
    {
        if (string.IsNullOrWhiteSpace(state.ErrorMessage))
        {
            state.ErrorMessage = message;
        }
    }

    private DataDirectoryMigrationResult CreateDataDirectoryMigrationResult(
        DataDirectoryMigrationState state,
        bool cleanupCompleted,
        bool automaticRetryAttempted)
    {
        bool oldDirectoryRetained = Directory.Exists(state.SourceDataDirectory);
        return new DataDirectoryMigrationResult
        {
            CleanupCompleted = cleanupCompleted,
            AutomaticRetryAttempted = automaticRetryAttempted,
            OldDirectoryRetained = oldDirectoryRetained,
            MigratedFileCount = state.Files.Count,
            SourceDirectory = state.SourceDataDirectory,
            TargetDirectory = state.TargetDataDirectory,
            MigrationStateFilePath = MigrationStateFilePath,
            ErrorMessage = cleanupCompleted && !oldDirectoryRetained ? string.Empty : state.ErrorMessage,
            PendingItems = cleanupCompleted && !oldDirectoryRetained ? [] : GetPendingMigrationCleanupItems(state)
        };
    }

    private static List<string> GetPendingMigrationCleanupItems(DataDirectoryMigrationState state)
    {
        List<string> pendingItems = state.Files
            .Where(file => !file.DeletedFromSource)
            .Select(file => $"受管文件未清理：{file.RelativePath}")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!Directory.Exists(state.SourceDataDirectory))
        {
            return pendingItems;
        }

        if (SourceDirectoryContainsUnmanagedOrNewContent(state))
        {
            pendingItems.Add("旧数据目录仍包含非本工具管理的内容或迁移后新增内容，请确认后手动处理。");
        }
        else if (pendingItems.Count == 0)
        {
            pendingItems.Add("旧数据目录仍存在但未发现待清理的受管文件，请确认后手动处理。");
        }

        return pendingItems;
    }

    private static bool SourceDirectoryContainsUnmanagedOrNewContent(DataDirectoryMigrationState state)
    {
        if (!Directory.Exists(state.SourceDataDirectory))
        {
            return false;
        }

        HashSet<string> expectedFiles = state.Files
            .Where(file => !file.DeletedFromSource)
            .Select(file => NormalizeRelativePath(file.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedDirectories = state.Directories
            .Select(NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string relativeFile in expectedFiles)
        {
            AddParentDirectories(expectedDirectories, relativeFile);
        }

        foreach (string relativePath in EnumerateRelativeFiles(state.SourceDataDirectory))
        {
            if (!expectedFiles.Contains(NormalizeRelativePath(relativePath)))
            {
                return true;
            }
        }

        foreach (string relativeDirectory in EnumerateRelativeDirectories(state.SourceDataDirectory))
        {
            if (!expectedDirectories.Contains(NormalizeRelativePath(relativeDirectory)))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureSourceFileListMatchesState(DataDirectoryMigrationState state)
    {
        List<string> currentFiles = EnumerateManagedRelativeFiles(state.SourceDataDirectory);
        List<string> expectedFiles = state.Files
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!currentFiles.SequenceEqual(expectedFiles, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("迁移期间源数据目录文件列表发生变化，已停止切换数据目录。");
        }
    }

    private void MarkMigrationState(DataDirectoryMigrationState state, string stage, string currentOperation)
    {
        state.Stage = stage;
        state.CurrentOperation = currentOperation;
        state.ErrorMessage = string.Empty;
        SaveMigrationState(state);
    }

    private void SaveMigrationState(DataDirectoryMigrationState state)
    {
        state.UpdatedAt = DateTime.Now;
        WriteJson(MigrationStateFilePath, state);
    }

    private void TryMarkMigrationFailed(DataDirectoryMigrationState? state, Exception exception)
    {
        if (state == null)
        {
            return;
        }

        try
        {
            state.Stage = MigrationStageFailed;
            state.ErrorMessage = exception.Message;
            state.CurrentOperation = "迁移失败，工具已保留原数据目录。";
            SaveMigrationState(state);
        }
        catch
        {
            // Failure recording is best-effort here; the original exception is more important.
        }
    }

    private void ClearMigrationStateFile()
    {
        try
        {
            if (File.Exists(MigrationStateFilePath))
            {
                File.Delete(MigrationStateFilePath);
            }
        }
        catch (Exception ex)
        {
            AddDataLoadWarning(
                $"migration-state-clear:{Path.GetFullPath(MigrationStateFilePath)}",
                $"工具数据目录迁移已完成，但迁移状态文件未能删除。{Environment.NewLine}" +
                $"文件：{MigrationStateFilePath}{Environment.NewLine}" +
                $"原因：{ex.Message}");
        }
    }

    private static bool IsMigrationStateValid(DataDirectoryMigrationState state)
    {
        return state.Version == MigrationStateVersion &&
            !string.IsNullOrWhiteSpace(state.Id) &&
            !string.IsNullOrWhiteSpace(state.SourceDataDirectory) &&
            !string.IsNullOrWhiteSpace(state.TargetDataDirectory) &&
            state.Files != null &&
            state.Directories != null &&
            state.Files.All(file => file != null && IsManagedRelativePath(file.RelativePath)) &&
            state.Directories.All(IsManagedRelativePath);
    }

    private static List<string> EnumerateManagedRelativeFiles(string rootDirectory)
    {
        List<string> files = [];
        foreach (string directoryName in ManagedDataDirectoryNames)
        {
            string directory = Path.Combine(rootDirectory, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(rootDirectory, file)));
        }

        return files
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> EnumerateManagedRelativeDirectories(string rootDirectory)
    {
        HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
        foreach (string directoryName in ManagedDataDirectoryNames)
        {
            string directory = Path.Combine(rootDirectory, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            directories.Add(directoryName);
            foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
            {
                directories.Add(Path.GetRelativePath(rootDirectory, childDirectory));
            }
        }

        return directories
            .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsManagedRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        string normalizedPath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string[] parts = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part is "." or ".."))
        {
            return false;
        }

        return ManagedDataDirectoryNames.Any(directoryName =>
            string.Equals(parts[0], directoryName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> EnumerateRelativeFiles(string rootDirectory)
    {
        return Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(rootDirectory, file))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> EnumerateRelativeDirectories(string rootDirectory)
    {
        return Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories)
            .Select(directory => Path.GetRelativePath(rootDirectory, directory))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static bool TryDeleteDirectoryIfEmpty(string directory, out Exception? error)
    {
        error = null;
        if (!Directory.Exists(directory))
        {
            return true;
        }

        try
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                return false;
            }

            Directory.Delete(directory);
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static string NormalizeDataDirectoryPath(string directory)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    private static bool IsSameDirectory(string left, string right)
    {
        return string.Equals(NormalizeDataDirectoryPath(left), NormalizeDataDirectoryPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSubdirectoryOf(string candidateDirectory, string parentDirectory)
    {
        string candidateFullPath = NormalizeDataDirectoryPath(candidateDirectory);
        string parentFullPath = NormalizeDataDirectoryPath(parentDirectory);
        return candidateFullPath.StartsWith(parentFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private AppDataStateSnapshot CreateAppDataStateSnapshot()
    {
        return new AppDataStateSnapshot(
            DataDirectory,
            CloneSettings(Settings),
            CloneCharacterProfiles(Characters),
            CloneWayMarkFavorites(WayMarkFavorites),
            CloneServerList(ServerList),
            MapDataVersion,
            MapDataSourcePath,
            MapDataLastUpdated,
            MapDataLastSuccessfulSyncAt,
            CreateMapNamesSnapshot(),
            bootstrapFileInvalid,
            settingsFileInvalid,
            charactersFileInvalid,
            wayMarkFavoritesFileInvalid,
            [.. dataLoadWarnings],
            new HashSet<string>(dataLoadWarningKeys));
    }

    private void RestoreAppDataState(AppDataStateSnapshot snapshot)
    {
        DataDirectory = snapshot.DataDirectory;
        Settings = CloneSettings(snapshot.Settings);
        Characters.Clear();
        foreach (CharacterProfile profile in snapshot.Characters)
        {
            Characters.Add(CloneCharacterProfile(profile));
        }

        WayMarkFavorites.Clear();
        foreach (WayMarkFavorite favorite in snapshot.WayMarkFavorites)
        {
            WayMarkFavorites.Add(WayMarkSnapshotConverter.CloneFavorite(favorite));
        }

        ServerList = CloneServerList(snapshot.ServerList);
        MapDataVersion = snapshot.MapDataVersion;
        MapDataSourcePath = snapshot.MapDataSourcePath;
        MapDataLastUpdated = snapshot.MapDataLastUpdated;
        MapDataLastSuccessfulSyncAt = snapshot.MapDataLastSuccessfulSyncAt;
        if (snapshot.MapNames.Count > 0)
        {
            MapData.ApplyMapNames(snapshot.MapNames);
        }
        else
        {
            MapData.Clear();
        }

        bootstrapFileInvalid = snapshot.BootstrapFileInvalid;
        settingsFileInvalid = snapshot.SettingsFileInvalid;
        charactersFileInvalid = snapshot.CharactersFileInvalid;
        wayMarkFavoritesFileInvalid = snapshot.WayMarkFavoritesFileInvalid;
        dataLoadWarnings.Clear();
        dataLoadWarnings.AddRange(snapshot.DataLoadWarnings);
        dataLoadWarningKeys.Clear();
        foreach (string warningKey in snapshot.DataLoadWarningKeys)
        {
            dataLoadWarningKeys.Add(warningKey);
        }

        ConfigureLoggerIfMigrationCleanupAllows();
    }

    private static List<CharacterProfile> CloneCharacterProfiles(IEnumerable<CharacterProfile> profiles)
    {
        List<CharacterProfile> result = [];
        foreach (CharacterProfile profile in profiles)
        {
            result.Add(CloneCharacterProfile(profile));
        }

        return result;
    }

    private static CharacterProfile CloneCharacterProfile(CharacterProfile profile)
    {
        return new CharacterProfile
        {
            UserID = profile.UserID,
            CharacterName = profile.CharacterName,
            DataCenter = profile.DataCenter,
            World = profile.World,
            Note = profile.Note,
            UpdatedAt = profile.UpdatedAt
        };
    }

    private static ServerListCache CloneServerList(ServerListCache serverList)
    {
        ServerListCache result = new()
        {
            SourceUrl = serverList.SourceUrl,
            LastUpdated = serverList.LastUpdated,
            LastSuccessfulSyncAt = serverList.LastSuccessfulSyncAt,
            Groups = []
        };

        if (serverList.Groups != null)
        {
            foreach (ServerGroup group in serverList.Groups)
            {
                result.Groups.Add(new ServerGroup
                {
                    DataCenter = group.DataCenter,
                    Worlds = group.Worlds == null ? [] : [.. group.Worlds]
                });
            }
        }

        return result;
    }

    private static Dictionary<ushort, string> CreateMapNamesSnapshot()
    {
        return MapData.GetKnownMapIds()
            .ToDictionary(mapId => mapId, MapData.GetName);
    }

    private sealed class DataDirectoryMigrationState
    {
        public int Version { get; set; }
        public string Id { get; set; } = string.Empty;
        public string SourceDataDirectory { get; set; } = string.Empty;
        public string TargetDataDirectory { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public List<string> Directories { get; set; } = [];
        public List<DataDirectoryMigrationFileState> Files { get; set; } = [];
    }

    private sealed class DataDirectoryMigrationFileState
    {
        public string RelativePath { get; set; } = string.Empty;
        public long Length { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public bool Copied { get; set; }
        public bool Verified { get; set; }
        public bool DeletedFromSource { get; set; }
    }

    private sealed record AppDataStateSnapshot(
        string DataDirectory,
        AppSettings Settings,
        List<CharacterProfile> Characters,
        List<WayMarkFavorite> WayMarkFavorites,
        ServerListCache ServerList,
        string MapDataVersion,
        string MapDataSourcePath,
        DateTime MapDataLastUpdated,
        DateTime MapDataLastSuccessfulSyncAt,
        Dictionary<ushort, string> MapNames,
        bool BootstrapFileInvalid,
        bool SettingsFileInvalid,
        bool CharactersFileInvalid,
        bool WayMarkFavoritesFileInvalid,
        List<string> DataLoadWarnings,
        HashSet<string> DataLoadWarningKeys);

}
