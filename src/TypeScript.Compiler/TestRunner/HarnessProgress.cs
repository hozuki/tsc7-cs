// 非 Go 源文件：harness 的**进度与单用例超时看门狗**（集成观测用；不影响被移植的编译器语义）。
//
// 为什么需要：一次全量 harness 要跑上万用例，其中长尾用例（例如以整个编译器自身为输入的 API sample）
// 可能耗时数十秒；而我们的编译器没有可取消的检查点（Go 也没有），所以**无法安全地"中断"某个正在跑用例**。
// 因此这里的超时语义与 Go 的 `go test -timeout` 一致：**超时即中止整轮运行**，打印是哪个用例，并以专属
// 退出码退出（而不是静默挂着）。这样长时间跑批不会因为单个用例卡死而无人察觉。
//
// 复核口径：`--case-timeout <秒>`（0 或缺省 = 不启用，保持与 Go harness 相同的行为）。
namespace TypeScript.TestRunner;

using System.Globalization;

/// <summary>harness 运行期的进度输出与单用例超时看门狗。</summary>
public static class HarnessProgress
{
    private const int _timeoutExitCode = 3;

    private static int _timeoutSeconds;
    private static int _totalCases;
    private static int _index;
    private static readonly System.Diagnostics.Stopwatch _sinceStart = System.Diagnostics.Stopwatch.StartNew();
    private static System.Threading.Timer? _watchdog;
    private static string _currentSuite = "";
    private static string _currentCase = "";
    private static long _currentStartedMs;

    /// <summary>由 harness 入口在解析完参数后调用一次。<paramref name="timeoutSeconds"/> 为 0 表示不启用超时。</summary>
    public static void Configure(int timeoutSeconds, int totalCases)
    {
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 0;
        _totalCases = totalCases;
        _index = 0;
    }

    /// <summary>每个用例开始前调用（记录当前用例名并启动看门狗）。</summary>
    public static void BeginCase(string suite, string caseName)
    {
        _currentSuite = suite;
        _currentCase = caseName;
        _currentStartedMs = _sinceStart.ElapsedMilliseconds;
        if (_timeoutSeconds <= 0)
        {
            return;
        }

        _watchdog?.Dispose();
        _watchdog = new System.Threading.Timer(
            _ => OnTimeout(),
            null,
            TimeSpan.FromSeconds(_timeoutSeconds),
            System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>每个用例结束后调用（停看门狗并打印一行进度；含已用时与粗略 ETA）。</summary>
    public static void EndCase(string suite, string caseName, double elapsedMs)
    {
        _watchdog?.Dispose();
        _watchdog = null;
        _index++;
        var elapsed = _sinceStart.Elapsed;
        var eta = _totalCases > 0 && _index > 0
            ? TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / _index * (_totalCases - _index))
            : TimeSpan.Zero;
        var etaText = _totalCases > 0
            ? $", {_index}/{_totalCases}, elapsed {Format(elapsed)}, eta {Format(eta)}"
            : $", #{_index}, elapsed {Format(elapsed)}";
        Console.WriteLine(
            $"[{suite}] {caseName}: {elapsedMs.ToString("N0", CultureInfo.InvariantCulture)} ms{etaText}");
    }

    private static void OnTimeout()
    {
        Console.Out.Flush();
        Console.Error.WriteLine(
            $"TIMEOUT: case '{_currentCase}' (suite '{_currentSuite}') exceeded {_timeoutSeconds}s " +
            $"(started at {_currentStartedMs} ms into the run); aborting the whole run " +
            $"(退出码 {_timeoutExitCode})——与 `go test -timeout` 同样的语义：不可安全中断单个用例，故中止整轮。");
        Console.Error.Flush();
        Environment.Exit(_timeoutExitCode);
    }

    private static string Format(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h{ts.Minutes}m{ts.Seconds}s"
            : ts.TotalMinutes >= 1
                ? $"{ts.Minutes}m{ts.Seconds}s"
                : $"{ts.TotalSeconds.ToString("N1", CultureInfo.InvariantCulture)}s";
}
