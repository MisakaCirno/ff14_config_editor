namespace FF14ConfigEditor
{
    /// <summary>
    /// 旧调试入口的兼容层，新代码优先使用 AppLogger。
    /// </summary>
    public static class DebugHelper
    {
        public static bool LogToConsole
        {
            get => AppLogger.LogToConsole;
            set => AppLogger.LogToConsole = value;
        }

        public static bool LogToDebug
        {
            get => AppLogger.LogToDebug;
            set => AppLogger.LogToDebug = value;
        }

        public static void Log(string message)
        {
            AppLogger.Debug(AppLogCategory.General, message);
        }

        public static void LogWarning(string message)
        {
            AppLogger.Warning(AppLogCategory.UISaveWarning, message);
        }

        public static void LogFormatError(string message, Exception? exception = null)
        {
            AppLogger.Error(AppLogCategory.UISaveFormat, message, exception);
        }

        public static void LogUnknownPreserved(string message)
        {
            AppLogger.Info(AppLogCategory.UISaveUnknownPreserved, message);
        }
    }
}
