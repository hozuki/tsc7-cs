// ported from: internal/project/logging/logtree.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **Go 的 nil 接收者**：LogTree 的每个方法都有 `if c == nil { return }`（`c *LogTree` 可以是 nil）。
//    C# 的实例方法不能在 null 引用上分派，这些分支不可达，故不保留（与 Collections/Set.cs 文件头第 3 条
//    同一处理）；需要"可选日志器"的调用点写 `logger?.Log(...)`。
//
// 2) `sync.Mutex` → `System.Threading.Lock`；`sync/atomic.Int32` 的 count/stringLength → `int` +
//    `Interlocked.Add/Increment` 与 `Volatile.Read`（与 Checker/CheckerPool.cs 等的既有做法一致）。
//    这两个字段只在根上使用（Go 的注释也是这样写的），子树的这两个字段保持 0。
//
// 3) `len(log.message)` 在 Go 里是**字节数**（UTF-8），C# 的 `string.Length` 是 UTF-16 码元数。这个值只用于
//    `String()` 里 StringBuilder 的容量预估（`builder.Grow`），不影响输出，故按 C# 语义取 `Length`
//    （对 ASCII 日志文本两者相同）。
//
// 4) `strings.Builder` → `System.Text.StringBuilder`；`builder.Grow(n)` → `new StringBuilder(capacity)`。
//
// 5) `panic("can only call String on root LogTree")` → `Debug.Fail(...)` + 抛出诚实的不可达异常
//    （Debug.Fail 没有 [DoesNotReturn]，见 Binder/Binder.Declarations.cs 的同一处理）。
//
// 6) 导出方法名照抄（`String()`/`Log`/`Logf`/`Embed`/`Fork`/`IsVerbose`/`SetVerbose`/`Verbose`），
//    未导出的 `logEntry` → `LogEntry`、`writeLogsRecursive` → `WriteLogsRecursive`
//    （未导出类型/方法按 C# 习惯命名，architecture.md §3.2）。`String()` 之外同时覆写 `ToString()`
//    （Locale/Locale.cs 的先例），这样内插字符串也能拿到同样的文本。
//
// 7) 包级函数 `NewLogTree` 落在同名静态类 `Logging` 里（Logger.cs 已声明 `public static partial class Logging`）。
using System.Text;

namespace TypeScript.Project.Logging;

/// <summary>Go 的未导出 <c>logEntry</c>：一条日志（可挂一棵子树）。</summary>
internal sealed class LogEntry
{
    /// <summary>Go: <c>var seq atomic.Uint64</c> 的全局序号（Go 只写不读，这里同样保留）。</summary>
    private static long _seq;

    internal long Seq;
    internal DateTime Time;
    internal string Message;
    internal LogTree? Child;

    internal LogEntry(LogTree? child, string message)
    {
        Seq = Interlocked.Increment(ref _seq);
        Time = DateTime.Now;
        Message = message;
        Child = child;
    }
}

/// <summary>Go 的 <c>LogTree</c>：可 Fork/Embed 的日志树，`String()` 输出整棵树的文本。</summary>
public sealed class LogTree : LogCollector
{
    private readonly string _name;
    private readonly Lock _mu = new();
    private readonly List<LogEntry> _logs = new();
    private readonly LogTree _root;
    private readonly int _level;
    private bool _verbose;

    // Only set on root
    private int _count;
    private int _stringLength;

    private LogTree(string name, LogTree? root, int level, bool verbose)
    {
        _name = name;
        // Go: NewLogTree 里 `lc.root = lc`；Fork 里 `root: c.root`。
        _root = root ?? this;
        _level = level;
        _verbose = verbose;
    }

    /// <summary>Go: <c>NewLogTree(name string) *LogTree</c>（自身即根）。</summary>
    internal static LogTree CreateRoot(string name)
    {
        return new LogTree(name, root: null, level: 0, verbose: false);
    }

    private void Add(LogEntry log)
    {
        // indent + header + message + newline
        Interlocked.Add(ref _root._stringLength, _level + 15 + log.Message.Length + 1);
        Interlocked.Increment(ref _root._count);
        lock (_mu)
        {
            _logs.Add(log);
        }
    }

    public void Log(params object?[] message)
    {
        var log = new LogEntry(child: null, LoggerImpl.Sprint(message));
        Add(log);
    }

    public void Logf(string format, params object?[] args)
    {
        var log = new LogEntry(child: null, string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
        Add(log);
    }

    public bool IsVerbose()
    {
        return _verbose;
    }

    public void SetVerbose(bool verbose)
    {
        _verbose = verbose;
    }

    public Logger? Verbose()
    {
        if (!_verbose)
        {
            return null;
        }

        return this;
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

    public void Embed(LogTree logs)
    {
        var count = Volatile.Read(ref logs._count);
        Interlocked.Add(ref _root._stringLength, Volatile.Read(ref logs._stringLength) + (count * _level));
        Interlocked.Add(ref _root._count, count);
        var log = new LogEntry(logs, logs._name);
        Add(log);
    }

    public LogTree Fork(string message)
    {
        var child = new LogTree(name: string.Empty, _root, _level + 1, _verbose);
        var log = new LogEntry(child, message);
        Add(log);
        return child;
    }

    public string String()
    {
        if (!ReferenceEquals(_root, this))
        {
            Debug.Fail("can only call String on root LogTree");
            throw new System.Diagnostics.UnreachableException();
        }

        var builder = new StringBuilder(Volatile.Read(ref _stringLength) + ("======== " + _name + " ========\n").Length);
        builder.Append("======== ").Append(_name).Append(" ========\n");
        WriteLogsRecursive(builder, string.Empty);
        return builder.ToString();
    }

    public override string ToString()
    {
        return String();
    }

    private void WriteLogsRecursive(StringBuilder builder, string indent)
    {
        foreach (var log in _logs)
        {
            builder.Append(indent);
            builder.Append(Logging.FormatTime(log.Time));
            builder.Append(' ');
            builder.Append(log.Message);
            builder.Append('\n');
            log.Child?.WriteLogsRecursive(builder, indent + "\t");
        }
    }
}

public static partial class Logging
{
    /// <summary>Go: <c>NewLogTree(name string) *LogTree</c>。</summary>
    public static LogTree NewLogTree(string name)
    {
        return LogTree.CreateRoot(name);
    }
}
