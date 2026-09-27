// ported from: internal/testutil/baseline/testmain.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `func Track() func()` → C# 的 `public static Action Track()`（返回清理委托）。
// 2) Go 的 `runtime.Callers(2, pcs)` + `CallersFrames` 用来"把整条调用栈的文件名哈希成一个 per-package 文件名"。
//    C# 没有对应 API，这里用 `System.Diagnostics.StackTrace` + `GetFileName()`（同样只取文件路径入哈希）。
//    差异只在**开启 tracking 时**的文件名取值上（`TSGO_BASELINE_TRACKING_DIR` 非空才启用），且 tracking 文件只是
//    "这次跑过哪些 baseline" 的清单，不参与 baseline 内容比较。禁用 `using System.Diagnostics;`（仓库规定），
//    故全部写全限定名。
// 3) `fnv.New64a()` 是标准 FNV-1a 64 位，见下方 Fnv1a64 实现（与哈希 map 无关，仅用于文件名）。
// 4) Go 的 `t.Error`（`testing.TB`）→ `IHarnessTest.Error`。

namespace TypeScript.TestUtil.Baseline;

using System.Text;
using TypeScript.Collections;
using static TypeScript.TSPath.TSPath;

public static partial class Baseline
{
    /// <summary>Go: <c>var recordedBaselines collections.SyncSet[string]</c>（本次跑写过的 baseline 相对路径集合）。</summary>
    private static readonly SyncSet<string> _recordedBaselines = new();

    /// <summary>Go: <c>var trackingInitialized bool</c>。</summary>
    private static bool _trackingInitialized;

    /// <summary>Go: <c>var trackingDir = os.Getenv("TSGO_BASELINE_TRACKING_DIR")</c>（非空即启用 tracking）。</summary>
    private static readonly string _trackingDir = Environment.GetEnvironmentVariable("TSGO_BASELINE_TRACKING_DIR") ?? "";

    /// <summary>
    /// Go: <c>func Track() func()</c>。用法与 Go 相同（在 TestMain 里 <c>defer baseline.Track()()</c>）：
    /// <c>var cleanup = Baseline.Track(); try { ... } finally { cleanup(); }</c>。
    /// </summary>
    public static Action Track()
    {
        _trackingInitialized = true;

        if (_trackingDir.Length == 0)
        {
            return () => { };
        }

        // Go 在这里（而不是在延迟执行的清理函数里）取调用栈，因为清理函数跑的时候
        // 调用者信息已经不在栈上了。C# 同理：这里立刻抓栈。
        var trackingPath = CombinePaths(_trackingDir, $"{CallStackHash():x16}.txt");

        return () =>
        {
            // After tests complete, write the recorded baselines
            WriteRecordedBaselines(trackingPath);
        };
    }

    /// <summary>Go: <c>func recordBaseline(t testing.TB, relativePath string)</c>（相对 baselines/reference 的路径）。</summary>
    private static void RecordBaseline(IHarnessTest t, string relativePath)
    {
        if (_trackingDir.Length != 0)
        {
            if (!_trackingInitialized)
            {
                t.Error(
                    "baseline: package uses baselines but TestMain did not call baseline.Track(). " +
                    "Please add a TestMain function with: defer baseline.Track()()");
                return;
            }

            _recordedBaselines.Add(relativePath);
        }
    }

    /// <summary>Go: <c>func writeRecordedBaselines(trackingPath string)</c>。</summary>
    private static void WriteRecordedBaselines(string trackingPath)
    {
        if (_recordedBaselines.Size() == 0)
        {
            return;
        }

        var error = DoWriteRecordedBaselines(trackingPath);
        if (error is not null)
        {
            // Go: fmt.Fprintf(os.Stderr, "baseline: failed to write tracking file %s: %v\n", trackingPath, err); os.Exit(1)
            Console.Error.WriteLine($"baseline: failed to write tracking file {trackingPath}: {error.Message}");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Go: <c>func doWriteRecordedBaselines(trackingPath string) error</c>。
    /// C# 侧把 error 表达成返回值（null = 成功），因为调用点要把它变成 <c>os.Exit(1)</c>。
    /// </summary>
    private static Exception? DoWriteRecordedBaselines(string trackingPath)
    {
        try
        {
            var builder = new StringBuilder();
            foreach (var baseline in _recordedBaselines.Keys())
            {
                builder.Append(baseline).Append('\n');
            }

            File.WriteAllText(trackingPath, builder.ToString(), _utf8NoBom);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ex;
        }
    }

    /// <summary>Go: 调用栈里每个 frame 的文件路径喂给 FNV-1a 64，得到 per-package 的 tracking 文件名。</summary>
    private static ulong CallStackHash()
    {
        var hash = _fnv1a64OffsetBasis;
        var stackTrace = new System.Diagnostics.StackTrace(fNeedFileInfo: true);
        for (var i = 0; i < stackTrace.FrameCount; i++)
        {
            var frame = stackTrace.GetFrame(i);
            var fileName = frame?.GetFileName() ?? "";
            foreach (var b in Encoding.UTF8.GetBytes(fileName))
            {
                hash ^= b;
                hash *= _fnv1a64Prime;
            }
        }

        return hash;
    }

    private const ulong _fnv1a64OffsetBasis = 14695981039346656037;
    private const ulong _fnv1a64Prime = 1099511628211;
}
