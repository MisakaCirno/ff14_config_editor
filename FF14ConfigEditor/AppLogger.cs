using System;
using System.IO;
using System.Text;

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
        private const long MaxLogFileBytes = 2 * 1024 * 1024;

        public static bool LogToConsole { get; set; }
        public static bool LogToDebug { get; set; } = true;
        public static AppLogLevel MinimumLevel { get; set; } = AppLogLevel.Debug;
        public static string? LogFilePath { get; private set; }

        public static void SetLogFilePath(string? filePath)
        {
            lock (SyncRoot)
            {
                LogFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
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

                RotateLogIfNeeded(LogFilePath);
                File.AppendAllText(LogFilePath, logLine + Environment.NewLine, LogEncoding);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"日志写入失败：{ex.Message}");
            }
        }

        private static void RotateLogIfNeeded(string logFilePath)
        {
            if (!File.Exists(logFilePath))
            {
                return;
            }

            FileInfo fileInfo = new(logFilePath);
            if (fileInfo.Length <= MaxLogFileBytes)
            {
                return;
            }

            string rotatedPath = logFilePath + ".1";
            if (File.Exists(rotatedPath))
            {
                File.Delete(rotatedPath);
            }

            File.Move(logFilePath, rotatedPath);
        }
    }
}
