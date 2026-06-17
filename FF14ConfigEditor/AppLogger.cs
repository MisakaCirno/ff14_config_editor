using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FF14ConfigEditor
{
    public enum AppLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public enum AppLogCategory
    {
        General,
        UISaveFormat,
        UISaveUnknownPreserved,
        UISaveWarning,
        UI,
        IO
    }

    /// <summary>
    /// 项目内轻量日志入口，用于区分调试信息、格式错误、警告和保留的未知结构。
    /// </summary>
    public static class AppLogger
    {
        private static readonly object SyncRoot = new();
        private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private const long DefaultMaxLogFileBytes = 5 * 1024 * 1024;
        private const int DefaultMaxLogFileCount = 5;
        private static long maxLogFileBytes = DefaultMaxLogFileBytes;
        private static int maxLogFileCount = DefaultMaxLogFileCount;

        public static bool LogToConsole { get; set; }
        public static bool LogToDebug { get; set; } = true;
        public static AppLogLevel MinimumLevel { get; set; } = AppLogLevel.Debug;
        public static string? LogFilePath { get; private set; }
        public static long MaxLogFileBytes
        {
            get
            {
                lock (SyncRoot)
                {
                    return maxLogFileBytes;
                }
            }
        }

        public static int MaxLogFileCount
        {
            get
            {
                lock (SyncRoot)
                {
                    return maxLogFileCount;
                }
            }
        }

        public static void SetLogFilePath(string? filePath)
        {
            lock (SyncRoot)
            {
                LogFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
            }
        }

        public static void ConfigureFileLogging(string? filePath, long maxFileBytes, int maxFileCount)
        {
            lock (SyncRoot)
            {
                LogFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
                maxLogFileBytes = Math.Max(1, maxFileBytes);
                maxLogFileCount = Math.Max(1, maxFileCount);
            }
        }

        public static int ClearLogFiles()
        {
            lock (SyncRoot)
            {
                if (string.IsNullOrWhiteSpace(LogFilePath))
                {
                    return 0;
                }

                return DeleteManagedLogFiles(LogFilePath);
            }
        }

        public static void Debug(AppLogCategory category, string message)
        {
            Log(AppLogLevel.Debug, category, message);
        }

        public static void Info(AppLogCategory category, string message)
        {
            Log(AppLogLevel.Info, category, message);
        }

        public static void Warning(AppLogCategory category, string message, Exception? exception = null)
        {
            Log(AppLogLevel.Warning, category, message, exception);
        }

        public static void Error(AppLogCategory category, string message, Exception? exception = null)
        {
            Log(AppLogLevel.Error, category, message, exception);
        }

        public static void Log(
            AppLogLevel level,
            AppLogCategory category,
            string message,
            Exception? exception = null)
        {
            if (level < MinimumLevel)
            {
                return;
            }

            string logLine = FormatLine(level, category, message, exception);

            lock (SyncRoot)
            {
                if (LogToConsole)
                {
                    Console.WriteLine(logLine);
                }

                if (LogToDebug)
                {
                    System.Diagnostics.Debug.WriteLine(logLine);
                }

                WriteFileLog(logLine);
            }
        }

        private static string FormatLine(
            AppLogLevel level,
            AppLogCategory category,
            string message,
            Exception? exception)
        {
            string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [{category}] {message}";
            if (exception == null)
            {
                return line;
            }

            return $"{line} | {exception.GetType().Name}: {exception.Message}";
        }

        private static void WriteFileLog(string logLine)
        {
            if (string.IsNullOrWhiteSpace(LogFilePath))
            {
                return;
            }

            try
            {
                string? directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string logText = logLine + Environment.NewLine;
                RotateLogIfNeeded(LogFilePath, LogEncoding.GetByteCount(logText));
                File.AppendAllText(LogFilePath, logText, LogEncoding);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"日志写入失败：{ex.Message}");
            }
        }

        private static void RotateLogIfNeeded(string logFilePath, int incomingBytes)
        {
            if (!File.Exists(logFilePath))
            {
                return;
            }

            FileInfo fileInfo = new(logFilePath);
            if (fileInfo.Length == 0 || fileInfo.Length + incomingBytes <= maxLogFileBytes)
            {
                return;
            }

            int archiveCount = maxLogFileCount - 1;
            if (archiveCount <= 0)
            {
                File.Delete(logFilePath);
                PruneArchiveLogFiles(logFilePath, archiveCount);
                return;
            }

            DateTimeOffset rotatedAt = DateTimeOffset.Now;
            string archivePath = CreateTimestampedArchiveLogFilePath(logFilePath, rotatedAt);
            File.Move(logFilePath, archivePath);
            File.SetLastWriteTimeUtc(archivePath, rotatedAt.UtcDateTime);
            PruneArchiveLogFiles(logFilePath, archiveCount);
        }

        private static string CreateTimestampedArchiveLogFilePath(string logFilePath, DateTimeOffset rotatedAt)
        {
            string? directory = Path.GetDirectoryName(logFilePath);
            string fileName = Path.GetFileNameWithoutExtension(logFilePath);
            string extension = Path.GetExtension(logFilePath);
            string timestamp = rotatedAt.ToString("yyyyMMdd_HHmmss_fff");
            string archiveFileName = $"{fileName}_{timestamp}{extension}";
            string archivePath = string.IsNullOrWhiteSpace(directory)
                ? archiveFileName
                : Path.Combine(directory, archiveFileName);
            if (!File.Exists(archivePath))
            {
                return archivePath;
            }

            for (int index = 2; ; index++)
            {
                string retryFileName = $"{fileName}_{timestamp}_{index}{extension}";
                string retryPath = string.IsNullOrWhiteSpace(directory)
                    ? retryFileName
                    : Path.Combine(directory, retryFileName);
                if (!File.Exists(retryPath))
                {
                    return retryPath;
                }
            }
        }

        private static void PruneArchiveLogFiles(string logFilePath, int archiveCount)
        {
            foreach (FileInfo archiveFile in EnumerateArchiveLogFiles(logFilePath)
                         .Select(path => new FileInfo(path))
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                         .Skip(Math.Max(0, archiveCount)))
            {
                archiveFile.Delete();
            }
        }

        private static int DeleteManagedLogFiles(string logFilePath)
        {
            int deletedCount = 0;
            foreach (string filePath in EnumerateManagedLogFiles(logFilePath))
            {
                if (!File.Exists(filePath))
                {
                    continue;
                }

                File.Delete(filePath);
                deletedCount++;
            }

            return deletedCount;
        }

        private static IEnumerable<string> EnumerateManagedLogFiles(string logFilePath)
        {
            if (File.Exists(logFilePath))
            {
                yield return logFilePath;
            }

            foreach (string archivePath in EnumerateArchiveLogFiles(logFilePath))
            {
                yield return archivePath;
            }
        }

        private static IEnumerable<string> EnumerateArchiveLogFiles(string logFilePath)
        {
            string? directory = Path.GetDirectoryName(logFilePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                yield break;
            }

            string fileName = Path.GetFileNameWithoutExtension(logFilePath);
            string extension = Path.GetExtension(logFilePath);
            string timestampedPattern = $"^{Regex.Escape(fileName)}_\\d{{8}}_\\d{{6}}_\\d{{3}}(?:_\\d+)?{Regex.Escape(extension)}$";

            foreach (string archivePath in Directory.EnumerateFiles(directory))
            {
                string archiveFileName = Path.GetFileName(archivePath);
                if (Regex.IsMatch(archiveFileName, timestampedPattern, RegexOptions.CultureInvariant))
                {
                    yield return archivePath;
                }
            }
        }
    }
}
