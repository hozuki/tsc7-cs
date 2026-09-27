// ported from: internal/project/logging/logcollector.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `LogCollector`（导出接口）= `fmt.Stringer` + `Logger`。`fmt.Stringer` 的方法名在 C# 里保留为
//    `String()`（与 Printer/TextWriter.cs、Locale/Locale.cs 的既有做法一致），同时覆写 `ToString()`
//    以便内插字符串/string.Format 拿到同样的文本。
//
// 2) Go 的 `logCollector` **内嵌** `logger` 并另存一个 `builder *strings.Builder`（两者指向同一个
//    strings.Builder：日志写进它，`String()` 读它）。C# 的内嵌 → 继承 `LoggerImpl`；`strings.Builder`
//    的角色由 `StringWriter` 一个对象承担（StringWriter 内部就是 StringBuilder，不需要两份引用）。
//
// 3) `NewTestLogger()` 的固定时间前缀：Go 是 `formatTime(time.Unix(1349085672, 0))`（**本地时区**渲染）。
//    C# 用 `DateTimeOffset.FromUnixTimeSeconds(1349085672).ToLocalTime().DateTime`，逐字对应同一时刻的
//    本地渲染，因此同一台机器上 TSGO 与 C# 侧的测试日志时间戳文本相同。
//
// 4) 包级函数 `NewTestLogger` 落在 `Logging` 静态类里（Logger.cs 已声明 `public static partial class Logging`）。
namespace TypeScript.Project.Logging;

/// <summary>Go 的 <c>LogCollector</c>：可把已写出的日志整段取回的 Logger（测试/基线用）。</summary>
public interface LogCollector : Logger
{
    /// <summary>Go 的 <c>fmt.Stringer.String()</c>：返回收集到的全部文本。</summary>
    string String();
}

/// <summary>Go 的未导出 <c>logCollector</c>：把日志收进内存缓冲区的 Logger。</summary>
public sealed class LogCollectorImpl : LoggerImpl, LogCollector
{
    private readonly StringWriter _builder;

    public LogCollectorImpl(StringWriter builder)
        : base(builder, static () => Logging.FormatTime(Logging.TestLogTime))
    {
        _builder = builder;
    }

    public string String()
    {
        return _builder.ToString();
    }

    public override string ToString()
    {
        return String();
    }
}

public static partial class Logging
{
    /// <summary>Go: <c>time.Unix(1349085672, 0)</c> 在本地时区的渲染（测试日志的固定前缀）。</summary>
    internal static readonly DateTime TestLogTime = DateTimeOffset.FromUnixTimeSeconds(1349085672).ToLocalTime().DateTime;

    /// <summary>Go: <c>NewTestLogger() LogCollector</c>：时间前缀固定，便于确定性测试。</summary>
    public static LogCollector NewTestLogger()
    {
        return new LogCollectorImpl(new StringWriter());
    }
}
