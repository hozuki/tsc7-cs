// ported from: internal/ipc/timing.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的未导出类型（serverRequestTiming/serverTimingTotals/serverTimingInfo/timingCollector）→ C# 的
//    `internal` 类型（architecture.md §2：未导出类型按 C# 习惯命名，字段仍是 `_camelCase`；测试靠
//    Properties/AssemblyInfo.cs 的 InternalsVisibleTo 保持 Go 同 package 的可见性）。
// 2) Go 的 `time.Duration` → C# 的 `TimeSpan`（Core/WatchOptions.cs 的先例）。**精度偏离**：Go 的
//    time.Duration 是 1ns 粒度，TimeSpan 是 100ns 粒度，所以 durationToMillis 在 100ns 以下会丢精度
//    （timing_test.go 里 500ns→0.0005 仍精确，1234ns→0.001234 在 C# 侧只能到 0.0012；见测试文件头）。
// 3) Go 的 `time.Now().UnixMilli()` → `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`。
// 4) 这些类型在 Go 里靠 reflect 自动映射 JSON；C# 无反射（docs/json.md §4）→ 三个类型各自实现
//    `IJsonMarshaler`（只有写侧：Go 侧也没有读这些结构的代码，客户端要读得自己定义形状）。
// 5) Go 的 `sync.Mutex` → C# 的 `lock`；Go 的包级函数 `serverTimingSnapshot`/`disabledServerTimingInfo`/
//    `durationToMillis` → 静态类 `Ipc` 的 internal 静态方法（architecture.md §2）。
// 6) Go 的 `serverTimingSnapshot(nil)`（nil 接收者安全）→ C# 侧调用点写 `c?.Snapshot()`，封装在本文件
//    的 `Ipc.ServerTimingSnapshot` 里（§3.2）。
using TypeScript.Json;

using static TypeScript.Json.Json;

namespace TypeScript.Ipc;

/// <summary>
/// Go 的 <c>ipc</c> 包级常量/函数（见 Conn.cs 的类注释；本分片对应 timing.go）。
/// </summary>
public static partial class Ipc
{
    /// <summary>Go: <c>MethodGetServerTiming</c>。</summary>
    public const string MethodGetServerTiming = "getServerTiming";

    /// <summary>Go: <c>MethodResetServerTiming</c>。</summary>
    public const string MethodResetServerTiming = "resetServerTiming";

    /// <summary>Go: <c>func serverTimingSnapshot(c *timingCollector) serverTimingInfo</c>（c 为 nil 即未开启采集）。</summary>
    internal static ServerTimingInfo ServerTimingSnapshot(TimingCollector? c) =>
        c is null ? DisabledServerTimingInfo() : c.Snapshot();

    /// <summary>Go: <c>func disabledServerTimingInfo() serverTimingInfo</c>。</summary>
    internal static ServerTimingInfo DisabledServerTimingInfo() =>
        // Go: `RecentRequests: []serverRequestTiming{}`（非 nil 空切片）→ C# 的空数组。
        new() { _enabled = false, _recentRequests = [] };

    /// <summary>Go: <c>func durationToMillis(d time.Duration) float64</c>（负数钳到 0）。</summary>
    internal static double DurationToMillis(TimeSpan d) => d < TimeSpan.Zero ? 0 : d.TotalMilliseconds;
}

/// <summary>Go: <c>type serverRequestTiming struct</c>（单次请求的处理耗时样本）。</summary>
internal sealed class ServerRequestTiming : IJsonMarshaler
{
    /// <summary>Go: <c>Method string</c>（被处理的 API 方法名）。</summary>
    internal string _method = "";

    /// <summary>Go: <c>ProcessingTimeMs float64</c>（墙钟处理耗时，毫秒）。</summary>
    internal double _processingTimeMs;

    /// <summary>Go: <c>Timestamp int64</c>（请求完成时的 Unix 毫秒时间戳）。</summary>
    internal long _timestamp;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("method"));
        MarshalEncode(encoder, _method);
        encoder.WriteToken(JsonToken.String("processingTimeMs"));
        MarshalEncode(encoder, _processingTimeMs);
        encoder.WriteToken(JsonToken.String("timestamp"));
        MarshalEncode(encoder, _timestamp);
        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type serverTimingTotals struct</c>（所有请求的累计值）。</summary>
internal sealed class ServerTimingTotals : IJsonMarshaler
{
    /// <summary>Go: <c>RequestCount uint64</c>（被计量的请求总数）。</summary>
    internal ulong _requestCount;

    /// <summary>Go: <c>TotalProcessingTimeMs float64</c>（处理耗时总和，毫秒）。</summary>
    internal double _totalProcessingTimeMs;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("requestCount"));
        MarshalEncode(encoder, _requestCount);
        encoder.WriteToken(JsonToken.String("totalProcessingTimeMs"));
        MarshalEncode(encoder, _totalProcessingTimeMs);
        encoder.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type serverTimingInfo struct</c>（getServerTiming 响应体）。</summary>
internal sealed class ServerTimingInfo : IJsonMarshaler
{
    /// <summary>Go: <c>Enabled bool</c>（服务端是否开启了耗时采集）。</summary>
    internal bool _enabled;

    /// <summary>Go: <c>Totals serverTimingTotals</c>。</summary>
    internal ServerTimingTotals _totals = new();

    /// <summary>Go: <c>RecentRequests []serverRequestTiming</c>（由旧到新，最多 serverRecentRequestCapacity 条）。</summary>
    internal ServerRequestTiming[] _recentRequests = [];

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("enabled"));
        MarshalEncode(encoder, _enabled);
        encoder.WriteToken(JsonToken.String("totals"));
        _totals.MarshalJSONTo(encoder);
        encoder.WriteToken(JsonToken.String("recentRequests"));
        MarshalEncode(encoder, _recentRequests);
        encoder.WriteToken(EndObject);
    }
}

/// <summary>
/// Go: <c>type timingCollector struct</c>。把每次请求的处理耗时累加进总计 + 定长环形缓冲。
/// 可并发使用（异步连接的多个请求 goroutine 会同时 record）。
/// </summary>
internal sealed class TimingCollector
{
    /// <summary>Go: <c>serverRecentRequestCapacity</c>（环形缓冲保留的"最近请求"条数）。</summary>
    internal const int _serverRecentRequestCapacity = 5;

    /// <summary>Go: <c>mu sync.Mutex</c>。</summary>
    private readonly object _mu = new();

    /// <summary>Go: <c>totals serverTimingTotals</c>（累计值）。</summary>
    private readonly ServerTimingTotals _totals = new();

    /// <summary>Go: <c>ring []serverRequestTiming</c>（最多 serverRecentRequestCapacity 条；满了以后 head 指向最旧的一条）。</summary>
    private readonly List<ServerRequestTiming> _ring = new();

    /// <summary>Go: <c>head int</c>。</summary>
    private int _head;

    /// <summary>Go: <c>func (c *timingCollector) record(method string, d time.Duration)</c>。</summary>
    internal void Record(string method, TimeSpan d)
    {
        var processingMs = Ipc.DurationToMillis(d);

        lock (_mu)
        {
            _totals._requestCount++;
            _totals._totalProcessingTimeMs += processingMs;

            var entry = new ServerRequestTiming
            {
                _method = method,
                _processingTimeMs = processingMs,
                _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            if (_ring.Count < _serverRecentRequestCapacity)
            {
                _ring.Add(entry);
            }
            else
            {
                _ring[_head] = entry;
                _head = (_head + 1) % _serverRecentRequestCapacity;
            }
        }
    }

    /// <summary>Go: <c>func (c *timingCollector) snapshot() serverTimingInfo</c>（最近请求按由旧到新排序）。</summary>
    internal ServerTimingInfo Snapshot()
    {
        lock (_mu)
        {
            var recent = new ServerRequestTiming[_ring.Count];
            for (var i = 0; i < _ring.Count; i++)
            {
                recent[i] = _ring[(_head + i) % _ring.Count];
            }

            // Go 按值拷贝 totals（`Totals: c.totals`）：C# 里必须逐字段复制，不能共享可变实例。
            return new ServerTimingInfo
            {
                _enabled = true,
                _totals = new ServerTimingTotals
                {
                    _requestCount = _totals._requestCount,
                    _totalProcessingTimeMs = _totals._totalProcessingTimeMs,
                },
                _recentRequests = recent,
            };
        }
    }

    /// <summary>Go: <c>func (c *timingCollector) reset()</c>（清空累计值与历史，之后仍可继续使用）。</summary>
    internal void Reset()
    {
        lock (_mu)
        {
            _totals._requestCount = 0;
            _totals._totalProcessingTimeMs = 0;
            _ring.Clear();
            _head = 0;
        }
    }
}
