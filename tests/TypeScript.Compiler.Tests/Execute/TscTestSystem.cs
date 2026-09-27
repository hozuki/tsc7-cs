// 测试局部替身：Go 的 `internal/execute/tsctests.NewTscSystem(files, useCaseSensitiveFileNames, cwd)`。
//
// 作用域：**只**服务 Execute/BuildGraphAndCleanTests.cs（internal/execute/build/graph_test.go 与
// clean_test.go 的移植）。完整的 tsctests 移植是另一个切片，本文件**不**移植它 —— Go 的 `TestSys` 还带
// baselines / watch backend / content mapper spawner / CommandLineTesting 等本切片完全用不到的成员。
//
// 与 Go 的偏离（都是机械替换，不改变断言强度）：
// 1) Go 的 `tsctests.FileMap`（`map[string]any`）→ `Dictionary<string, string>`：这两个 Go 测试的文件值全是
//    字符串，直接选 `vfstest.FromMap` 的 string 重载（语义等价，不用装箱）。
// 2) Go 的 `TestClock`（每调用一次 `Now()` 前进一秒，让虚拟 FS 的 mtime 确定性）→ 同形的私有 `TestClock`。
//    Go 的 `NewTscSystem` 用 `FromMapWithClock`，本切片用 `FromMap`：graph 测试只生成图、clean 测试只做
//    `FileExists`/`Remove`，都不读 mtime，故不引入 clock 版 FS（Go 里 clock 的唯一消费者是 MapFS 的时间戳）。
// 3) Go 的 `TestSys.currentWrite`（`NewTscSystem` 里是 nil，写它会 panic）→ 这里的 `_output`（`StringWriter`）。
//    Go 的 clean 测试为此必须覆盖 `Writer`/`ErrorWriter`；C# 侧逐条保留该覆盖（见 CleanTestSystem）。
// 4) Go 的 `Spawn` 走 contentmappertest 的进程内假 mapper → 本切片不涉及 content mapper，按 §3.2 抛
//    `NotSupportedException`（同 tests/.../Execute/TscEmitTests.cs 的 `TimingTestSystem.Spawn`）。
// 5) `GetEnvironmentVariable(name) (string, bool)` → `TryGetEnvironmentVariable(name, out value)`（§3.2）；
//    Go 的 `TestSys` 在默认（nil map）配置下恒返回 false，`GetWidthOfTerminal` 因此恒 0。
// 6) `time.Time`/`time.Duration` → `DateTime`/`TimeSpan`（统一 UTC，同 VfsTest 的 Clock）。
// 7) Go 的 `DirExists` 等 `testFs` 成员 → `VfsTest.FromMap` 返回的 `FS`（同一个移植，无需额外包装）。
using System.Runtime.ExceptionServices;
using TypeScript.Vfs;
using TSys = TypeScript.Execute.Tsc.System;

namespace TypeScript.Tests;

using static TypeScript.Vfs.VfsTest.VfsTest;

/// <summary>Go: <c>type TestClock struct</c>（tsctests/sys.go:69-84）——每次 <c>Now()</c> 前进一秒。</summary>
public sealed class TestClock
{
    private readonly object _mu = new();
    private readonly DateTime _start = DateTime.UtcNow;
    private DateTime _now;

    public DateTime Now()
    {
        lock (_mu)
        {
            if (_now == default)
            {
                _now = _start;
            }
            _now = _now.AddSeconds(1); // Simulate some time passing
            return _now;
        }
    }

    public TimeSpan SinceStart() => Now() - _start;
}

/// <summary>Go: <c>tsctests.NewTscSystem(files, useCaseSensitiveFileNames, cwd) *TestSys</c>（tsctests/sys.go:93-103）。</summary>
public class TscTestSystem : TSys
{
    // docs/architecture.md §3.9 / tests/TypeScript.Harness/HarnessTest.cs:48
    private const int _stackSize = 64 * 1024 * 1024;

    internal readonly FS _fs;
    internal readonly string _cwd;
    internal readonly TestClock _clock = new();
    internal readonly Dictionary<string, string> _env = new();

    /// <summary>Go: <c>TestSys.currentWrite</c>（见文件头偏离 3）。</summary>
    internal readonly StringWriter _output = new();

    public TscTestSystem(Dictionary<string, string> files, bool useCaseSensitiveFileNames, string cwd)
    {
        _fs = FromMap(files, useCaseSensitiveFileNames);
        _cwd = cwd;
    }

    public virtual TextWriter Writer() => _output;

    public virtual TextWriter ErrorWriter() => _output;

    public FS FS() => _fs;

    /// <summary>Go: <c>TestSys.DefaultLibraryPath()</c>：<c>NewTscSystem</c> 不设置它（零值 ""）。</summary>
    public string DefaultLibraryPath() => "";

    public string GetCurrentDirectory() => _cwd;

    /// <summary>Go: <c>TestSys.outputIsTTY</c>（<c>NewTscSystem</c> 置 true）。</summary>
    public bool WriteOutputIsTTY() => true;

    public int GetWidthOfTerminal()
    {
        if (TryGetEnvironmentVariable("TS_TEST_TERMINAL_WIDTH", out var width) && width != "")
        {
            return int.Parse(width);
        }
        return 0;
    }

    public bool TryGetEnvironmentVariable(string name, out string value) => _env.TryGetValue(name, out value!);

    public DateTime Now() => _clock.Now();

    public TimeSpan SinceStart() => _clock.SinceStart();

    public Stream Spawn(string[] command, string dir, Stream stderr) =>
        throw new NotSupportedException("TscTestSystem does not implement Spawn (no content mapper in these tests)");

    /// <summary>docs/architecture.md §3.9：递归很深的入口点必须在大栈线程上跑（同 HarnessTest.cs:163-179）。</summary>
    public static void RunOnBigStackThread(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        }, _stackSize);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }
}
