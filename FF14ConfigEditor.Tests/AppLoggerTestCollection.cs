using Xunit;

namespace FF14ConfigEditor.Tests;

// AppLogger 是进程级 static 全局状态，LogFilePath、MinimumLevel 等字段被多个测试类共享。
// xUnit 默认按测试类并行执行，触碰同一组静态状态的测试类必须串行，否则会出现：
// 一个类改 LogFilePath/MinimumLevel 时，另一个类的 AppLogger 调用读到中间态，
// 导致日志写进对方临时目录、或 MinimumLevel 被覆盖后读不到自己写的 debug 日志，CI 偶发失败。
// 把所有触碰 AppLogger 静态状态的测试类归入同一 collection，强制串行执行。
[CollectionDefinition("AppLogger")]
public sealed class AppLoggerTestCollection
{
}
