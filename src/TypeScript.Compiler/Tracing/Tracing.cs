// ported from: internal/tracing/tracing.go:1-531（Tracer/TracedType 接口、TraceRecord/traceEvent、
//   常量、Phase、Tracing 会话（StartTracing/timestamp/Instant/Push/线程 ID/NewTypeTracer/StopTracing）、
//   typeTracer（RecordType/DumpTypes））
//             + internal/tracing/tracing.go:534-763 在 Tracing.Types.cs（TypeDescriptor/Location/LineAndChar
//               及其 JSON 与 getLocation）
//
// 与 Go 的差异（逐条）：
// 1) **文件系统**：Go 的 `vfs.FS` → C# 的 `TypeScript.Vfs.FS`（Vfs 切片已落地）。Go 里 `(T, error)` 的返回在
//    C# 侧是抛异常（`WriteFile`/`AppendFile` 失败抛 IOException 家族，见 Vfs.cs 文件头）。
// 2) Go 的 `type Phase string` + `PhaseParse = "parse"` 常量组：C# 没有字符串枚举，按 docs/architecture.md §3.3
//    的"包级常量放静态类"落成 `public static class Phase`（`Phase.Parse` 等），参数类型直接是 `string`。
// 3) Go 的 `atomic.Bool` → `volatile bool`（Load/Store 语义一致）；`sync.Mutex` → `object _mu` + `lock`（与
//    Ast/SourceFile.cs 同款）。Go 的"nil 接收者安全"方法（Instant/Push）在 C# 里不可分派（§3.2），
//    调用点写 `tr?.Instant(...)`；checker 的 Tracer 适配器自己判空。
// 4) `xxh3`（github.com/zeebo/xxh3）是 Go 侧的外部依赖，不是 TSGO 的 API：按 XXH3 规范在 Xxh3/Xxh3.cs 里复刻
//    （baseline 里钉住了文件线程 ID，见该文件头）。64 位与 128 位入口现在是同一个 `TypeScript.Xxh3.Xxh3`。
// 5) `json.MarshalWrite(buf, v, opts)` 写进 `strings.Builder`：C# 的 `MarshalWrite` 只接受 `Stream`，所以这里
//    走 `Marshal(v, opts)` → `Encoding.UTF8.GetString` 后 append（字节相同）。
// 6) 结构体的 JSON 字段映射（Go 靠 reflect + tag）在 C# 侧必须显式实现 `IJsonMarshaler`（docs/json.md §2）：
//    TraceRecord（legend.json）、TypeDescriptor/Location/LineAndChar（types_N.json）、TraceEvent（trace.json，
//    另实现 `IJsonUnmarshaler` 供测试解析）。字段顺序与 `omitzero` 语义逐条照 Go 的 tag。
// 7) 未导出类型（traceEvent/traceThreadKind/traceThreadKey/typeTracer）按 C# 习惯命名：前三个 → TraceEvent/
//    TraceThreadKind/TraceThreadKey（internal，字段仍是 Go 的导出名 —— 它们在 Go 里虽然大写但类型本身不导出，
//    C# 的 internal 已表达同样的可见性）；typeTracer → TypeTracer。
namespace TypeScript.Tracing;

using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using System.Text;
using TypeScript.Ast;
using JsonDecoder = TypeScript.Json.Decoder;
using JsonEncoder = TypeScript.Json.Encoder;
using TypeScript.Json;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Ast.Ast;
using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;

/// <summary>An interface for recording types during type checking. Each checker should have its own Tracer instance to avoid sharing types between checkers.</summary>
public interface Tracer
{
    /// <summary>Records a type for later dumping.</summary>
    void RecordType(TracedType t);

    /// <summary>Writes all recorded types to disk.</summary>
    void DumpTypes();
}

/// <summary>
/// An interface that represents a type that can be traced. This allows the tracing package to work with types from
/// the checker package without creating a circular dependency.
/// </summary>
public interface TracedType
{
    uint Id();

    string[] FormatFlags();

    bool IsConditional();

    Symbol? Symbol();

    Symbol? AliasSymbol();

    TracedType[] AliasTypeArguments();

    // Type-specific data accessors
    string IntrinsicName();

    TracedType[] UnionTypes();

    TracedType[] IntersectionTypes();

    TracedType? IndexType();

    TracedType? IndexedAccessObjectType();

    TracedType? IndexedAccessIndexType();

    TracedType? ConditionalCheckType();

    TracedType? ConditionalExtendsType();

    TracedType? ConditionalTrueType();

    TracedType? ConditionalFalseType();

    TracedType? SubstitutionBaseType();

    TracedType? SubstitutionConstraintType();

    TracedType? ReferenceTarget();

    TracedType[] ReferenceTypeArguments();

    Node? ReferenceNode();

    TracedType? ReverseMappedSourceType();

    TracedType? ReverseMappedMappedType();

    TracedType? ReverseMappedConstraintType();

    TracedType? EvolvingArrayElementType();

    TracedType? EvolvingArrayFinalType();

    bool IsTuple();

    Node? Pattern();

    object? RecursionIdentity();

    /// <summary>Display is an optional string representation of the type.</summary>
    string Display();
}

/// <summary>Represents metadata about a single trace file.</summary>
public sealed class TraceRecord : IJsonMarshaler
{
    public string ConfigFilePath = "";
    public string TracePath = "";
    public string TypesPath = "";
    public int CheckerID;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        if (ConfigFilePath.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("configFilePath"));
            MarshalEncode(encoder, ConfigFilePath);
        }

        if (TracePath.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("tracePath"));
            MarshalEncode(encoder, TracePath);
        }

        if (TypesPath.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("typesPath"));
            MarshalEncode(encoder, TypesPath);
        }

        encoder.WriteToken(JsonToken.String("checkerId"));
        MarshalEncode(encoder, CheckerID);
        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go 的 <c>type traceEvent struct</c>（internal：Go 里类型未导出，字段是 Go 侧的导出名）。</summary>
internal sealed class TraceEvent : IJsonMarshaler, IJsonUnmarshaler
{
    public int PID;
    public int TID;
    public string PH = "";
    public string Cat = "";
    public double TS;
    public string Name = "";
    public string S = ""; // scope, only set for instant events ("g" = global)
    public double? Dur;
    public Dictionary<string, object?>? Args;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);

        WriteName(encoder, "pid");
        MarshalEncode(encoder, PID);
        WriteName(encoder, "tid");
        MarshalEncode(encoder, TID);
        WriteName(encoder, "ph");
        MarshalEncode(encoder, PH);
        WriteName(encoder, "cat");
        MarshalEncode(encoder, Cat);
        WriteName(encoder, "ts");
        MarshalEncode(encoder, TS);

        if (Name.Length != 0)
        {
            WriteName(encoder, "name");
            MarshalEncode(encoder, Name);
        }

        if (S.Length != 0)
        {
            WriteName(encoder, "s");
            MarshalEncode(encoder, S);
        }

        if (Dur is not null)
        {
            WriteName(encoder, "dur");
            MarshalEncode(encoder, Dur.Value);
        }

        if (Args is not null)
        {
            WriteName(encoder, "args");
            MarshalEncode(encoder, Args);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("expected object");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadToken().StringValue;
            switch (name)
            {
                case "pid":
                    PID = decoder.Decode<int>();
                    break;
                case "tid":
                    TID = decoder.Decode<int>();
                    break;
                case "ph":
                    PH = decoder.Decode<string>() ?? "";
                    break;
                case "cat":
                    Cat = decoder.Decode<string>() ?? "";
                    break;
                case "ts":
                    TS = decoder.Decode<double>();
                    break;
                case "name":
                    Name = decoder.Decode<string>() ?? "";
                    break;
                case "s":
                    S = decoder.Decode<string>() ?? "";
                    break;
                case "dur":
                    Dur = decoder.DecodeNew<double?>();
                    break;
                case "args":
                    Args = decoder.DecodeNew<Dictionary<string, object?>>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken(); // }
    }

    private static void WriteName(JsonEncoder encoder, string name) => encoder.WriteToken(JsonToken.String(name));
}

/// <summary>Go 的 <c>type Phase string</c> + `PhaseParse = "parse"` 常量组（见文件头第 2 条）。</summary>
public static class Phase
{
    public const string Parse = "parse";
    public const string Program = "program";
    public const string Bind = "bind";
    public const string Check = "check";
    public const string CheckTypes = "checkTypes";
    public const string Emit = "emit";
    public const string Session = "session";
}

/// <summary>Go 的 <c>type traceThreadKind string</c>。</summary>
internal static class TraceThreadKind
{
    internal const string Checker = "checker";
    internal const string File = "file";
}

/// <summary>Go 的 <c>type traceThreadKey struct</c>（Go 里可比较的结构体 → C# 的值相等结构体，供 Dictionary 作键）。</summary>
internal readonly struct TraceThreadKey : IEquatable<TraceThreadKey>
{
    private readonly string _kind;
    private readonly string _text;
    private readonly int _index;
    private readonly bool _hasIndex;

    internal TraceThreadKey(string kind, string text, int index, bool hasIndex)
    {
        _kind = kind;
        _text = text;
        _index = index;
        _hasIndex = hasIndex;
    }

    internal static TraceThreadKey Checker(int checkerIndex) =>
        new(TraceThreadKind.Checker, "", checkerIndex, true);

    internal static TraceThreadKey File(string path) =>
        new(TraceThreadKind.File, path, 0, false);

    internal string Kind => _kind;

    internal string Text => _text;

    internal int Index => _index;

    internal bool HasIndex => _hasIndex;

    public bool Equals(TraceThreadKey other) =>
        _kind == other._kind && _text == other._text && _index == other._index && _hasIndex == other._hasIndex;

    public override bool Equals(object? obj) => obj is TraceThreadKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_kind, _text, _index, _hasIndex);
}

/// <summary>Manages the overall tracing session including all checkers.</summary>
public sealed partial class Tracing
{
    // sampleInterval matches TypeScript's 10ms sampling interval.
    // Events with separateBeginAndEnd=false are only recorded if their duration crosses a 10ms sampling boundary.
    private const double _sampleIntervalMicros = 10 * 1000;

    private const string _traceFileName = "trace.json";

    private const int _mainThreadID = 1;
    private const int _firstSyntheticThreadID = 2;
    private const int _firstFileThreadID = 1_000_000;
    private const ulong _fileThreadIDHashRange = 1_000_000_000;

    private static readonly string[] _traceThreadArgKeys =
        ["path", "fileName", "containingFileName", "jsFilePath", "declarationFilePath"];

    // flushThreshold is the size at which buffered trace content is flushed to disk via AppendFile. Keeps peak memory
    // bounded for long-running compilations while avoiding a syscall per event.
    private const int _flushThreshold = 256 * 1024;

    private readonly FS _fs;
    private readonly string _traceDir;
    private readonly string _tracePath;
    private readonly string _configFilePath;
    private readonly List<TraceRecord> _legend = [];
    private readonly List<TypeTracer> _tracers = [];
    private readonly StringBuilder _traceContent = new();
    private volatile bool _traceStarted;
    private readonly Dictionary<TraceThreadKey, int> _threadIDs = [];
    private readonly Dictionary<int, TraceThreadKey> _threadKeys = [];
    private double _metadataTS;
    private readonly bool _deterministic; // when true, use monotonic counter instead of real time
    private ulong _timestampCounter; // only used in deterministic mode
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly object _mu = new();

    // flushErr holds the first error encountered while appending the trace buffer to disk. Once set, subsequent flushes
    // become no-ops and the error is surfaced from StopTracing so that transient I/O failures (disk full, permission
    // denied, etc.) don't crash the compiler.
    private Exception? _flushErr;

    private Tracing(FS fs, string traceDir, string configFilePath, bool deterministic)
    {
        _fs = fs;
        _traceDir = traceDir;
        _tracePath = CombinePaths(traceDir, _traceFileName);
        _configFilePath = configFilePath;
        _deterministic = deterministic;
    }

    /// <summary>
    /// Creates a new tracing session. When deterministic is true, timestamps use a monotonic counter instead of real
    /// wall-clock time, producing stable output for test baselines.
    /// </summary>
    public static Tracing StartTracing(FS fs, string traceDir, string configFilePath, bool deterministic)
    {
        var tr = new Tracing(fs, traceDir, configFilePath, deterministic);
        tr._traceStarted = true;

        // Write the trace file header with metadata events
        tr._traceContent.Append("[\n");

        // Write metadata events (matching TypeScript's format)
        var metaTs = tr.Timestamp();
        tr._metadataTS = metaTs;
        tr.WriteEvent(new TraceEvent { PID = 1, TID = _mainThreadID, PH = "M", Cat = "__metadata", TS = metaTs, Name = "process_name", Args = new Dictionary<string, object?> { ["name"] = "tsgo" } });
        tr._traceContent.Append(",\n");
        tr.WriteEvent(new TraceEvent { PID = 1, TID = _mainThreadID, PH = "M", Cat = "__metadata", TS = metaTs, Name = "thread_name", Args = new Dictionary<string, object?> { ["name"] = "Main" } });
        tr._traceContent.Append(",\n");
        tr.WriteEvent(new TraceEvent { PID = 1, TID = _mainThreadID, PH = "M", Cat = "disabled-by-default-devtools.timeline", TS = metaTs, Name = "TracingStartedInBrowser" });

        // Truncate any existing trace file with the header so subsequent AppendFile calls extend a clean file.
        try
        {
            tr._fs.WriteFile(tr._tracePath, tr._traceContent.ToString());
        }
        catch (Exception ex)
        {
            throw new IOException($"failed to write trace file header: {ex.Message}", ex);
        }

        tr._traceContent.Clear();

        return tr;
    }

    /// <summary>
    /// Returns the current timestamp in microseconds. In deterministic mode it returns a monotonically increasing
    /// counter; otherwise it returns the real elapsed wall-clock time since tracing started, matching TypeScript's
    /// 1000 * timestamp() (microseconds).
    /// </summary>
    private double Timestamp()
    {
        if (_deterministic)
        {
            _timestampCounter++;
            return _timestampCounter;
        }

        return _stopwatch.Elapsed.TotalMicroseconds;
    }

    private static void WriteEventTo(StringBuilder buf, TraceEvent @event)
    {
        // Go: json.MarshalWrite(buf, event, json.Deterministic(true))
        buf.Append(Encoding.UTF8.GetString(Marshal(@event, Deterministic(true))));
    }

    private void WriteEvent(TraceEvent @event) => WriteEventTo(_traceContent, @event);

    /// <summary>
    /// Appends the buffered trace content to disk if it has grown past the flush threshold. Caller must hold <c>_mu</c>.
    /// If a previous flush failed, or this flush fails, the error is recorded in <c>_flushErr</c> and subsequent writes
    /// become no-ops; the error is surfaced from StopTracing.
    /// </summary>
    private void MaybeFlushLocked()
    {
        if (_flushErr is not null)
        {
            _traceContent.Clear();
            return;
        }

        if (_traceContent.Length < _flushThreshold)
        {
            return;
        }

        try
        {
            _fs.AppendFile(_tracePath, _traceContent.ToString());
        }
        catch (Exception ex)
        {
            _flushErr = new IOException($"failed to flush trace file: {ex.Message}", ex);
        }

        _traceContent.Clear();
    }

    /// <summary>
    /// Records an instant event in the trace. Go 的 nil 接收者安全在 C# 里不可分派（§3.2）：调用点写 <c>tr?.Instant(...)</c>。
    /// </summary>
    public void Instant(string phase, string name, Dictionary<string, object?>? args)
    {
        if (!_traceStarted)
        {
            return;
        }

        lock (_mu)
        {
            // Re-check under the lock: StopTracing may have run between the load above and acquiring the lock. Once
            // stopped, further writes would land in a buffer that has already been flushed and the closing "]" written.
            if (!_traceStarted)
            {
                return;
            }

            var ts = Timestamp();
            var tid = ThreadIDLocked(args);
            _traceContent.Append(",\n");
            WriteEvent(new TraceEvent { PID = 1, TID = tid, PH = "I", Cat = phase, TS = ts, Name = name, S = "g", Args = args });
            MaybeFlushLocked();
        }
    }

    /// <summary>
    /// Starts a trace event block on the shared trace buffer. Safe to call from multiple threads; Go 的 nil 接收者安全
    /// 在 C# 里不可分派（§3.2），调用点写 <c>tr?.Push(...)</c>（并在返回的 Action 上判空）。
    /// <para>
    /// When separateBeginAndEnd is true, a "B" (begin) event is written immediately and the returned function writes a
    /// matching "E" (end) event. This is used for events that must always appear in the trace (e.g. checkSourceFile,
    /// createProgram, emit).
    /// </para>
    /// <para>
    /// When separateBeginAndEnd is false (the default in TypeScript), the event is only recorded if its duration
    /// crosses a 10ms sampling boundary, matching TypeScript's behavior of sampling short-lived events to avoid trace
    /// bloat.
    /// </para>
    /// Returns a function that should be called (typically deferred) to end the event. Each returned function is
    /// self-contained and does not depend on a shared stack, making it safe for concurrent use across goroutines.
    /// </summary>
    public Action Push(string phase, string name, Dictionary<string, object?>? args, bool separateBeginAndEnd)
    {
        if (!_traceStarted)
        {
            return static () => { };
        }

        if (separateBeginAndEnd)
        {
            int tid;
            lock (_mu)
            {
                if (!_traceStarted)
                {
                    return static () => { };
                }

                var ts = Timestamp();
                tid = ThreadIDLocked(args);
                _traceContent.Append(",\n");
                WriteEvent(new TraceEvent { PID = 1, TID = tid, PH = "B", Cat = phase, TS = ts, Name = name, Args = args });
                MaybeFlushLocked();
            }

            return () =>
            {
                lock (_mu)
                {
                    if (!_traceStarted)
                    {
                        return;
                    }

                    var endTs = Timestamp();
                    _traceContent.Append(",\n");
                    WriteEvent(new TraceEvent { PID = 1, TID = tid, PH = "E", Cat = phase, TS = endTs, Name = name, Args = args });
                    MaybeFlushLocked();
                }
            };
        }

        // Sampled event: only record if duration crosses a sampling boundary.
        // In deterministic mode, sampled events are skipped entirely to avoid flaky baselines,
        // so avoid the cost of cloning args / capturing the start time.
        if (_deterministic)
        {
            return static () => { };
        }

        var startMicros = _stopwatch.Elapsed.TotalMicroseconds;
        args = args is null ? null : new Dictionary<string, object?>(args);
        return () =>
        {
            var dur = _stopwatch.Elapsed.TotalMicroseconds - startMicros;
            var intervalMicros = _sampleIntervalMicros;
            if (intervalMicros - (startMicros % intervalMicros) > dur)
            {
                return;
            }

            lock (_mu)
            {
                if (!_traceStarted)
                {
                    return;
                }

                var tid = ThreadIDLocked(args);
                _traceContent.Append(",\n");
                WriteEvent(new TraceEvent { PID = 1, TID = tid, PH = "X", Cat = phase, TS = startMicros, Name = name, Dur = dur, Args = args });
                MaybeFlushLocked();
            }
        };
    }

    private int ThreadIDLocked(Dictionary<string, object?>? args)
    {
        if (!TryTraceThreadKeyFromArgs(args, out var key))
        {
            return _mainThreadID;
        }

        if (_threadIDs.TryGetValue(key, out var existingTid))
        {
            return existingTid;
        }

        var tid = DefaultThreadID(key);
        while (true)
        {
            if (!_threadKeys.TryGetValue(tid, out var existingKey) || existingKey.Equals(key))
            {
                break;
            }

            tid++;
        }

        _threadIDs[key] = tid;
        _threadKeys[tid] = key;
        WriteThreadNameEventLocked(tid, DisplayName(key));
        return tid;
    }

    private void WriteThreadNameEventLocked(int tid, string name)
    {
        _traceContent.Append(",\n");
        WriteEvent(new TraceEvent { PID = 1, TID = tid, PH = "M", Cat = "__metadata", TS = _metadataTS, Name = "thread_name", Args = new Dictionary<string, object?> { ["name"] = name } });
    }

    private static bool TryTraceThreadKeyFromArgs(Dictionary<string, object?>? args, out TraceThreadKey key)
    {
        if (args is null || args.Count == 0)
        {
            key = default;
            return false;
        }

        if (args.TryGetValue("checkerId", out var checkerIdValue) && checkerIdValue is int checkerId)
        {
            key = TraceThreadKey.Checker(checkerId);
            return true;
        }

        foreach (var argKey in _traceThreadArgKeys)
        {
            if (args.TryGetValue(argKey, out var value) && value is string path && path.Length != 0)
            {
                key = TraceThreadKey.File(path);
                return true;
            }
        }

        key = default;
        return false;
    }

    private static int DefaultThreadID(TraceThreadKey key)
    {
        if (key.Kind == TraceThreadKind.Checker && key.HasIndex && key.Index >= 0)
        {
            return _firstSyntheticThreadID + key.Index;
        }

        return StableTraceThreadID(key);
    }

    private static string DisplayName(TraceThreadKey key)
    {
        if (key.HasIndex)
        {
            return key.Kind + ":" + key.Index.ToString(CultureInfo.InvariantCulture);
        }

        return key.Kind + ":" + key.Text;
    }

    private static int StableTraceThreadID(TraceThreadKey key)
    {
        // Go: hash := xxh3.New(); hash.WriteString(kind); hash.WriteString(":"); hash.WriteString(text|index)
        var keyText = new StringBuilder();
        keyText.Append(key.Kind);
        keyText.Append(':');
        keyText.Append(key.HasIndex ? key.Index.ToString(CultureInfo.InvariantCulture) : key.Text);
        var hash = Xxh3.Xxh3.Hash64(Encoding.UTF8.GetBytes(keyText.ToString()));
        return _firstFileThreadID + (int)(hash % _fileThreadIDHashRange);
    }

    /// <summary>
    /// Creates a new tracer for a specific checker. The checkerIndex is used to create unique filenames for each
    /// checker's output.
    /// </summary>
    public Tracer NewTypeTracer(int checkerIndex)
    {
        lock (_mu)
        {
            var typesPath = CombinePaths(_traceDir, "types_" + checkerIndex.ToString(CultureInfo.InvariantCulture) + ".json");
            var tracer = new TypeTracer(_fs, checkerIndex, typesPath);
            _tracers.Add(tracer);
            _legend.Add(new TraceRecord
            {
                ConfigFilePath = _configFilePath,
                TracePath = _tracePath,
                TypesPath = typesPath,
                CheckerID = checkerIndex,
            });
            return tracer;
        }
    }

    /// <summary>Finalizes the tracing session and writes all output files.</summary>
    public void StopTracing()
    {
        // Dump types from all tracers BEFORE acquiring the lock, because
        // DumpTypes → buildTypeDescriptor → Display() → TypeToString can
        // re-enter the checker which calls Push/Pop (which need _mu).
        foreach (var tracer in _tracers)
        {
            try
            {
                tracer.DumpTypes();
            }
            catch (Exception ex)
            {
                throw new IOException($"failed to dump types for checker {tracer._checkerIndex}: {ex.Message}", ex);
            }
        }

        lock (_mu)
        {
            // Close the trace file(s)
            if (_traceStarted)
            {
                // Surface any buffered flush failure before attempting the final write.
                if (_flushErr is not null)
                {
                    _traceContent.Clear();
                    _traceStarted = false;
                    throw _flushErr;
                }

                // Flush any remaining buffered content and close the JSON array.
                try
                {
                    _fs.AppendFile(_tracePath, _traceContent.ToString() + "\n]\n");
                }
                catch (Exception ex)
                {
                    throw new IOException($"failed to write trace file: {ex.Message}", ex);
                }

                _traceContent.Clear();
                _traceStarted = false;
            }
        }

        // Sort legend entries by typesPath for deterministic output
        _legend.Sort(static (a, b) => string.CompareOrdinal(a.TypesPath, b.TypesPath));

        // Write the legend file
        var legendPath = CombinePaths(_traceDir, "legend.json");
        byte[] legendData;
        try
        {
            legendData = MarshalIndent(_legend, "", "  ");
        }
        catch (Exception ex)
        {
            throw new IOException($"failed to marshal legend file: {ex.Message}", ex);
        }

        try
        {
            _fs.WriteFile(legendPath, Encoding.UTF8.GetString(legendData));
        }
        catch (Exception ex)
        {
            throw new IOException($"failed to write legend file: {ex.Message}", ex);
        }
    }
}

/// <summary>Go 的 <c>type typeTracer struct</c>：每 checker 一个的 tracer 实现。</summary>
internal sealed partial class TypeTracer : Tracer
{
    private readonly FS _fs;
    internal readonly int _checkerIndex;
    private readonly string _typesPath;
    private readonly List<TracedType> _types = [];
    private readonly object _mu = new();

    internal TypeTracer(FS fs, int checkerIndex, string typesPath)
    {
        _fs = fs;
        _checkerIndex = checkerIndex;
        _typesPath = typesPath;
    }

    public void RecordType(TracedType typ)
    {
        lock (_mu)
        {
            _types.Add(typ);
        }
    }

    public void DumpTypes()
    {
        // Copy the types list under lock, then release so Display() calls during
        // buildTypeDescriptor don't deadlock when they create new types
        TracedType[] types;
        lock (_mu)
        {
            types = [.. _types];
        }

        if (types.Length == 0)
        {
            return;
        }

        var sb = new StringBuilder();

        // Write opening bracket (no newline so type ID matches line number)
        sb.Append('[');

        var recursionIdentityMap = new Dictionary<object, int>();

        for (var i = 0; i < types.Length; i++)
        {
            var descriptor = BuildTypeDescriptor(types[i], recursionIdentityMap);

            // Go: json.MarshalWrite(&sb, descriptor)
            sb.Append(Encoding.UTF8.GetString(Marshal(descriptor)));

            if (i < types.Length - 1)
            {
                sb.Append(",\n");
            }
        }

        sb.Append("]\n");

        try
        {
            _fs.WriteFile(_typesPath, sb.ToString());
        }
        catch (Exception ex)
        {
            throw new IOException($"failed to write types file: {ex.Message}", ex);
        }
    }
}
