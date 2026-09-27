// ported from: internal/project/logging/logger.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `io.Writer` → `System.IO.TextWriter`（本仓既有约定，见 DiagnosticWriter/DiagnosticWriter.cs 文件头：
//    Go 的 `fmt.Fprint/Fprintf` → `TextWriter.Write`，调用方传 `StringWriter`）。
//    **换行一律写 `"\n"`，不用 `TextWriter.WriteLine`**：Go 的 `fmt.Fprintln/Fprintf` 恒写 '\n'，而
//    `WriteLine` 在 Windows 上写 `Environment.NewLine`（"\r\n"），会让日志文本与 TSGO 不一致。
//
// 2) **Go 的 nil 接收者**：Go 的 `logger` 是未导出结构体，`NewNopLogger()` 返回的是**带类型的 nil**
//    （`(*logger)(nil)`），所有方法都以 `if l == nil { return }` 兜底。C# 的实例方法不能在 null 引用上
//    分派，这个分支在此不可达（与 Collections/Set.cs 文件头第 3 条同一处理），因此改为一个真正的
//    **NopLogger** 实现：所有方法空操作、`IsVerbose()` 恒 false、`Verbose()` 恒 null、`SetVerbose` 空操作
//    —— 与 Go 在 nil 接收者上的可观察行为逐条一致（Go 的 nil logger 调用 SetVerbose(true) 后
//    IsVerbose() 仍是 false，因为它先 return 了）。
//
// 3) `msg ...any` → `params object?[] msg`（本仓约定，如 Ast/Diagnostic.cs 的 NewDiagnostic）。
//    `fmt.Sprint(msg...)` 的语义（"两个相邻操作数都不是 string 时插一个空格"、nil → "<nil>"）在这里
//    用私有 `Sprint` 复刻（与 Debug/Debug.cs 的同名私有工具逐字一致）。
//
// 4) `Logf(format string, args ...any)` 内部的 `fmt.Sprintf(format, args...)` → C# 的
//    `string.Format(CultureInfo.InvariantCulture, format, args)`：**格式串是调用点负责改写的**
//    （Go 的 `%v`/`%s`/`%d` → C# 的 `{0}`/`{1}`，或者调用点直接用内插字符串拼好再交给 Log/Logf，
//    本仓其余地方就是这么做的，见 Checker/Checker.C5.Destructuring.cs 文件头第 15 条）。
//
// 5) `prefix func() string` → `Func<string>`；`formatTime(time.Time)` → `FormatTime(DateTime)`，
//    照 Go 的 `t.Format("15:04:05.000")` 落成不变文化的 `"HH:mm:ss.fff"`（毫秒 3 位、24 小时制）。
//    调用点（NewLogger）用 `DateTime.Now` 对应 Go 的 `time.Now()`。
//
// 6) 包级函数（NewLogger/NewNopLogger/formatTime）落在同名静态类 `Logging` 里（architecture.md §2），
//    调用点 `using static TypeScript.Project.Logging.Logging;`。
//    注意 `Logging` 既是命名空间名又是类名，所以**外部**只能靠 `using static` 的裸名调用
//    （直接写 `Logging.NewLogger` 会命中命名空间 → CS0118），这是 architecture.md §2 已记录的坑。
using System.Globalization;
using System.Text;

namespace TypeScript.Project.Logging;

/// <summary>Go 的 <c>Logger</c>：会话/项目系统的日志接口。</summary>
public interface Logger
{
    /// <summary>Go: <c>Error(msg ...any)</c>（当前实现与 Log 相同）。</summary>
    void Error(params object?[] msg);

    /// <summary>Go: <c>Errorf(format string, args ...any)</c>（当前实现与 Logf 相同）。</summary>
    void Errorf(string format, params object?[] args);

    /// <summary>Go: <c>Warn(msg ...any)</c>（当前实现与 Log 相同）。</summary>
    void Warn(params object?[] msg);

    /// <summary>Go: <c>Warnf(format string, args ...any)</c>（当前实现与 Logf 相同）。</summary>
    void Warnf(string format, params object?[] args);

    /// <summary>Go: <c>Info(msg ...any)</c>（当前实现与 Log 相同）。</summary>
    void Info(params object?[] msg);

    /// <summary>Go: <c>Infof(format string, args ...any)</c>（当前实现与 Logf 相同）。</summary>
    void Infof(string format, params object?[] args);

    /// <summary>Go: <c>Log(msg ...any)</c>：带前缀写一行。</summary>
    void Log(params object?[] msg);

    /// <summary>Go: <c>Logf(format string, args ...any)</c>：带前缀写一行格式化文本。</summary>
    void Logf(string format, params object?[] args);

    /// <summary>Go: <c>Verbose() Logger</c>：verbose 打开时返回自己，否则返回 nil。</summary>
    Logger? Verbose();

    /// <summary>Go: <c>IsVerbose() bool</c>。</summary>
    bool IsVerbose();

    /// <summary>Go: <c>SetVerbose(verbose bool)</c>。</summary>
    void SetVerbose(bool verbose);
}

/// <summary>Go 的未导出 <c>logger</c> 结构体（带前缀、带锁的写行日志器）。</summary>
public class LoggerImpl : Logger
{
    private readonly Lock _mu = new();
    private bool _verbose;
    private readonly TextWriter _writer;
    private readonly Func<string> _prefix;

    public LoggerImpl(TextWriter writer, Func<string> prefix)
    {
        _writer = writer;
        _prefix = prefix;
    }

    public void Log(params object?[] msg)
    {
        lock (_mu)
        {
            // Go: fmt.Fprintln(w, prefix, fmt.Sprint(msg...)) —— Fprintln 恒写 '\n'
            //（不能用 TextWriter.WriteLine：在 Windows 上它会写 Environment.NewLine = "\r\n"，
            // 日志文本就会与 TSGO 的基线不一致）。
            _writer.Write(_prefix() + Sprint(msg) + "\n");
        }
    }

    public void Logf(string format, params object?[] args)
    {
        lock (_mu)
        {
            // Go: fmt.Fprintf(w, "%s %s\n", l.prefix(), fmt.Sprintf(format, args...))
            _writer.Write(_prefix() + " " + string.Format(CultureInfo.InvariantCulture, format, args) + "\n");
        }
    }

    public Logger? Verbose()
    {
        lock (_mu)
        {
            if (!_verbose)
            {
                return null;
            }

            return this;
        }
    }

    public bool IsVerbose()
    {
        lock (_mu)
        {
            return _verbose;
        }
    }

    public void SetVerbose(bool verbose)
    {
        lock (_mu)
        {
            _verbose = verbose;
        }
    }

    public void Error(params object?[] msg)
    {
        Log(msg);
    }

    public void Errorf(string format, params object?[] args)
    {
        Logf(format, args);
    }

    public void Warn(params object?[] msg)
    {
        Log(msg);
    }

    public void Warnf(string format, params object?[] args)
    {
        Logf(format, args);
    }

    public void Info(params object?[] msg)
    {
        Log(msg);
    }

    public void Infof(string format, params object?[] args)
    {
        Logf(format, args);
    }

    /// <summary>复刻 fmt.Sprint 的默认格式（含"两个操作数都不是 string 时插入空格"的规则）。</summary>
    internal static string Sprint(object?[] parts)
    {
        if (parts.Length == 1)
        {
            return Format(parts[0]);
        }

        var builder = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0 && parts[i - 1] is not string && parts[i] is not string)
            {
                builder.Append(' ');
            }

            builder.Append(Format(parts[i]));
        }

        return builder.ToString();
    }

    private static string Format(object? value)
    {
        return value switch
        {
            null => "<nil>",
            string s => s,
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? "<nil>",
        };
    }
}

/// <summary>
/// Go 的 <c>NewNopLogger()</c> 返回的"带类型 nil"（见文件头第 2 条）：C# 侧落成一个真正的空实现，
/// 可观察行为与 Go 的 nil 接收者一致。
/// </summary>
public sealed class NopLogger : Logger
{
    public static readonly NopLogger Instance = new();

    private NopLogger()
    {
    }

    public void Error(params object?[] msg)
    {
    }

    public void Errorf(string format, params object?[] args)
    {
    }

    public void Warn(params object?[] msg)
    {
    }

    public void Warnf(string format, params object?[] args)
    {
    }

    public void Info(params object?[] msg)
    {
    }

    public void Infof(string format, params object?[] args)
    {
    }

    public void Log(params object?[] msg)
    {
    }

    public void Logf(string format, params object?[] args)
    {
    }

    public Logger? Verbose()
    {
        return null;
    }

    public bool IsVerbose()
    {
        return false;
    }

    public void SetVerbose(bool verbose)
    {
    }
}

public static partial class Logging
{
    /// <summary>Go: <c>NewLogger(output io.Writer) Logger</c>：时间前缀取当前本地时间。</summary>
    public static Logger NewLogger(TextWriter output)
    {
        return new LoggerImpl(output, static () => FormatTime(DateTime.Now));
    }

    /// <summary>Go: <c>NewNopLogger() Logger</c>（见文件头第 2 条）。</summary>
    public static Logger NewNopLogger()
    {
        return NopLogger.Instance;
    }

    /// <summary>Go: <c>formatTime(t time.Time) string</c> = <c>t.Format("15:04:05.000")</c> 外加方括号。</summary>
    internal static string FormatTime(DateTime t)
    {
        return "[" + t.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "]";
    }
}
