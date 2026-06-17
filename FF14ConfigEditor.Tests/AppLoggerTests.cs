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
    private readonly long originalMaxLogFileBytes = AppLogger.MaxLogFileBytes;
    private readonly int originalMaxLogFileCount = AppLogger.MaxLogFileCount;

    public void Dispose()
    {
        AppLogger.LogToConsole = originalLogToConsole;
        AppLogger.LogToDebug = originalLogToDebug;
        AppLogger.MinimumLevel = originalMinimumLevel;
        AppLogger.ConfigureFileLogging(originalLogFilePath, originalMaxLogFileBytes, originalMaxLogFileCount);

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
        AppLogger.ConfigureFileLogging(logPath, 1024 * 1024, 3);

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

    [Fact]
    public void Log_WhenConfiguredSizeExceeded_RotatesAndKeepsConfiguredFileCount()
    {
        string logPath = Path.Combine(testDirectory, "logs", "app.log");
        AppLogger.LogToConsole = false;
        AppLogger.LogToDebug = false;
        AppLogger.MinimumLevel = AppLogLevel.Debug;
        AppLogger.ConfigureFileLogging(logPath, maxFileBytes: 240, maxFileCount: 3);

        for (int index = 0; index < 8; index++)
        {
            AppLogger.Info(AppLogCategory.General, $"测试轮转 {index} {new string('X', 100)}");
        }

        string logDirectory = Path.GetDirectoryName(logPath)!;
        string[] logFiles = Directory.GetFiles(logDirectory, "app.log*")
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        Assert.True(logFiles.Length <= 3);
        Assert.Contains("app.log", logFiles);
        Assert.Contains("app.log.1", logFiles);
        Assert.Contains("app.log.2", logFiles);
        Assert.DoesNotContain("app.log.3", logFiles);
    }
}
