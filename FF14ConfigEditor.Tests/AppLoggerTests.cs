using FF14ConfigEditor;

namespace FF14ConfigEditor.Tests;

public sealed class AppLoggerTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "FF14ConfigEditor.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly bool originalLogToConsole = AppLogger.LogToConsole;
    private readonly bool originalLogToDebug = AppLogger.LogToDebug;
    private readonly AppLogLevel originalMinimumLevel = AppLogger.MinimumLevel;
    private readonly string? originalLogFilePath = AppLogger.LogFilePath;

    public void Dispose()
    {
        AppLogger.LogToConsole = originalLogToConsole;
        AppLogger.LogToDebug = originalLogToDebug;
        AppLogger.MinimumLevel = originalMinimumLevel;
        AppLogger.SetLogFilePath(originalLogFilePath);

        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Log_WhenFilePathConfigured_WritesStructuredLine()
    {
        string logPath = Path.Combine(testDirectory, "logs", "app.log");
        AppLogger.LogToConsole = false;
        AppLogger.LogToDebug = false;
        AppLogger.MinimumLevel = AppLogLevel.Debug;
        AppLogger.SetLogFilePath(logPath);

        AppLogger.Warning(
            AppLogCategory.UISaveWarning,
            "测试警告",
            new InvalidOperationException("测试异常"));

        string logText = File.ReadAllText(logPath);
        Assert.Contains("[Warning]", logText);
        Assert.Contains("[UISaveWarning]", logText);
        Assert.Contains("测试警告", logText);
        Assert.Contains("InvalidOperationException: 测试异常", logText);
    }
}
