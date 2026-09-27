// ported from: internal/pprof/pprof.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) .NET 没有 `runtime/pprof`：`pprof.StartCPUProfile`/`pprof.StopCPUProfile`/`pprof.Lookup("allocs"|"heap").WriteTo`
//    （Go 的 pb.gz profile 导出）在 .NET 里没有对应物（.NET 侧的等价物是 EventPipe/dotnet-trace，API 与格式都不同，
//    本移植不引入）。因此 `ProfileSession` 是**显式 no-op 会话**：`BeginProfiling` 不建目录/文件、不启动采样，
//    `Stop()` 什么都不做（也不像 Go 那样打印 "CPU profile: <path>"/"Memory profile: <path>" —— 没有文件，
//    打印会是假信息）。调用点（tsc.go 的 `--pprofDir` 分支）因此可以逐字照抄 Go 的形状。
// 2) `os.MkdirAll`/`os.Create`/`os.Getpid`/`time.Now().UnixMilli()`/`fmt.Fprintf`：只在"真要写 profile 文件"时才
//    有意义 → no-op 会话里全部省略（不写死代码，也不伪造一个看起来像真 profiler 的路径）。
//    故 `ProfileSession` 的未导出字段（`cpuFilePath`/`memFilePath`/`cpuFile`/`logWriter`）不移植：它们的存在只为
//    写文件/打印路径服务。
// 3) `(T, error)` → 预期分支用 `bool TryX(..., out T)`（§3.2）。`CPUProfiler` 在 no-op 实现下只剩两条分支：
//    "CPU profiling already in progress" / "CPU profiling not in progress" → `TryStartCPUProfile`/`TryStopCPUProfile`；
//    Go 里其余的 error（MkdirAll/Create/StartCPUProfile 失败）不可能发生（不碰文件系统、没有 profiler 后端）。
// 4) `StopCPUProfile() (string, error)` 的路径：no-op 会话不产生文件 → `filePath` 恒为 `""`（调用方只能得到
//    "没有文件" 这一事实）。
// 5) `SaveHeapProfile`/`SaveAllocProfile`：.NET 没有堆/分配 profile 的导出通道（`GC.GetTotalMemory`/
//    `GC.GetTotalAllocatedBytes` 只是数字，不是 profile）→ `TrySaveHeapProfile`/`TrySaveAllocProfile` 恒返回 `false`
//    （= Go 的 error 路径，调用方据此上报"不支持"）。它们的消费者（`lsp/server.go`、`api/session.go`）不在本切片；
//    将来若引入 EventPipe 实现，只改这两个方法体，签名不变。
// 6) `RunGC`：`runtime.GC()` → `GC.Collect()`（直接对应物，同为阻塞式回收）。
// 7) Go 的 `internal/pprof` 是独立 package（map 到 `src/TypeScript.Compiler/Pprof/` + `namespace TypeScript.Pprof`），
//    但它的唯一消费者就在 execute 家族（`execute/tsc.go`，以及后续的 lsp/api）→ 按本切片要求落在
//    `Execute/Pprof.cs`（`namespace TypeScript.Execute`），包级函数落在静态类 `Pprof`（§2）。文件名/命名空间因此与
//    Go 的目录结构不同。
// 8) `io.Writer` → `System.IO.TextWriter`（`logWriter` 参数保留形状，见 2）。
// 9) `sync.Mutex` → `lock`（字段 `_mu`）。
using global::System.IO;

namespace TypeScript.Execute;

/// <summary>
/// Go: <c>type ProfileSession struct</c>（pprof.go:16-21）。
/// **no-op 会话**：.NET 没有 `runtime/pprof`，所以这个会话不启动/停止任何采样，也不写任何文件（见文件头偏离 1/2）。
/// </summary>
public sealed class ProfileSession
{
    /// <summary>
    /// Go: <c>func (p *ProfileSession) Stop()</c>（pprof.go:49-66）。no-op：Go 里的
    /// `pprof.StopCPUProfile()` + `os.File.Close` + `pprof.Lookup("allocs").WriteTo` + 两行 profile 路径日志全部没有
    /// .NET 对应物（见文件头偏离 1）。
    /// </summary>
    public void Stop()
    {
    }
}

public static partial class Pprof
{
    /// <summary>
    /// Go: <c>func BeginProfiling(profileDir string, logWriter io.Writer) *ProfileSession</c>（pprof.go:24-46）。
    /// Go 会建目录、按 pid 生成两个 profile 路径、创建 CPU profile 文件并启动采样；这些全部依赖 `runtime/pprof`，
    /// .NET 无对应物 → 返回 no-op 会话（连 `os.MkdirAll` 也省略：不产生文件却建空目录会让用户误以为 profile 已生成）。
    /// </summary>
    public static ProfileSession BeginProfiling(string profileDir, TextWriter logWriter) => new();
}

/// <summary>Go: <c>type CPUProfiler struct</c>（pprof.go:68-71）。按需 CPU profiling 的 no-op 版本（见文件头偏离 3）。</summary>
public sealed class CPUProfiler
{
    /// <summary>Go: <c>mu sync.Mutex</c>。</summary>
    private readonly object _mu = new();

    /// <summary>Go: <c>session *ProfileSession</c>（nil = 未在 profiling）。</summary>
    private ProfileSession? _session;

    /// <summary>
    /// Go: <c>func (c *CPUProfiler) StartCPUProfile(profileDir string) error</c>（pprof.go:74-97）。
    /// <c>false</c> = Go 的 "CPU profiling already in progress"（唯一可能出现的分支：不建目录、不启动采样）。
    /// </summary>
    public bool TryStartCPUProfile(string profileDir)
    {
        lock (_mu)
        {
            if (_session is not null)
            {
                return false;
            }
            _session = new ProfileSession();
            return true;
        }
    }

    /// <summary>
    /// Go: <c>func (c *CPUProfiler) StopCPUProfile() (string, error)</c>（pprof.go:100-112）。
    /// <c>false</c> = Go 的 "CPU profiling not in progress"；成功时 <paramref name="filePath"/> 恒为 `""`（no-op 会话
    /// 不产生 profile 文件，见文件头偏离 4）。
    /// </summary>
    public bool TryStopCPUProfile(out string filePath)
    {
        lock (_mu)
        {
            if (_session is null)
            {
                filePath = "";
                return false;
            }
            _session.Stop();
            _session = null;
            filePath = "";
            return true;
        }
    }
}

public static partial class Pprof
{
    /// <summary>
    /// Go: <c>func SaveHeapProfile(profileDir string) (string, error)</c>（pprof.go:115-136）。
    /// .NET 无堆 profile 导出通道（见文件头偏离 5）→ 恒 `false`（= Go 的 error 路径）。
    /// </summary>
    public static bool TrySaveHeapProfile(string profileDir, out string filePath)
    {
        filePath = "";
        return false;
    }

    /// <summary>
    /// Go: <c>func SaveAllocProfile(profileDir string) (string, error)</c>（pprof.go:139-161）。
    /// .NET 无分配 profile 导出通道（见文件头偏离 5）→ 恒 `false`（= Go 的 error 路径）。
    /// </summary>
    public static bool TrySaveAllocProfile(string profileDir, out string filePath)
    {
        filePath = "";
        return false;
    }

    /// <summary>Go: <c>func RunGC()</c>（pprof.go:164-166）：<c>runtime.GC()</c> → <c>GC.Collect()</c>。</summary>
    public static void RunGC() => GC.Collect();
}
