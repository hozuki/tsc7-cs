// 非 Go 源文件：harness 的**进度与单用例超时看门狗**（集成观测用；不影响被移植的编译器语义）。
//
// 为什么需要：一次全量 harness 要跑上万用例，其中长尾用例（例如以整个编译器自身为输入的 API sample）
// 可能耗时数十秒；而我们的编译器没有可取消的检查点（Go 也没有），所以**无法安全地"中断"某个正在跑用例**。
// 因此这里的超时语义与 Go 的 `go test -timeout` 一致：**超时即中止整轮运行**，打印是哪个用例，并以专属
// 退出码退出（而不是静默挂着）。这样长时间跑批不会因为单个用例卡死而无人察觉。
//
// 看门狗实现选择的理由（2026-09 修 fourslash 偶发死锁）：早期版本用 `System.Threading.Timer`，回调跑在
// 线程池上——而"卡死"这一类故障里线程池饥饿正是嫌疑之一（回调可能被无限推迟，看门狗形同虚设）。现在改为
// **一条专用的后台看门狗线程**（显式 `Thread` + `Monitor.Wait`，不依赖线程池）。超时后依次做三件事：
//   ① 先把卡住的用例名写到 stderr（最关键的信息，先写先 flush）；
//   ② 打印"谁在等什么"（<see cref="Wait"/> 登记的等待点：LSP 请求应答、队列取数、消息读取……）与
//      <see cref="RegisterThread"/> 登记线程的存活状态（死掉的泵线程 = 等待点永远不会有人唤醒它）；
//   ③ `Environment.Exit(3)`。
// ②有兜底：诊断在另一条线程上跑，看门狗只等它 10 秒，等不到也照常退出——看门狗自己绝不能变成新的挂死点。
// 注：不 dump 别的线程的托管栈——`System.Diagnostics.StackTrace(Thread, bool)` 只存在于 .NET Framework，
// 在 .NET（Core）里没有；反射式的 `GetMethod()` 还会触发 IL2026（trim/AOT 不安全）。等待点登记是它的替代。
//
// 复核口径：`--case-timeout <秒>`（0 或缺省 = 不启用，保持与 Go harness 相同的行为）。
namespace TypeScript.TestRunner;

using System.Collections.Concurrent;
using System.Globalization;

/// <summary>harness 运行期的进度输出与单用例超时看门狗。</summary>
public static class HarnessProgress
{
    /// <summary>看门狗超时时的退出码（与"有用例失败"的 1、"用法错误"的 2 区分开，便于脚本识别）。</summary>
    private const int _timeoutExitCode = 3;

    /// <summary>诊断线程的兜底等待上限：诊断卡住也必须让进程退出（否则"超时"本身也变成挂死）。</summary>
    private static readonly TimeSpan _diagnosticDeadline = TimeSpan.FromSeconds(10);

    private static readonly object _mu = new();

    /// <summary>登记用于超时诊断的线程（见 <see cref="RegisterThread"/>）。</summary>
    private static readonly System.Collections.Generic.List<(Thread Thread, string Label)> _known = [];

    /// <summary>诊断事件日志（见 <see cref="Note"/>）：环形缓冲，超时时打印最后若干条。</summary>
    private const int _maxNotes = 64;

    private static readonly System.Collections.Generic.Queue<(long AtMs, string Message)> _notes = new();

    /// <summary>当前的等待点：线程 id → (描述, 开始时刻, 线程名, 序号)。见 <see cref="Wait"/>。</summary>
    private static readonly ConcurrentDictionary<int, (string Description, long SinceMs, string ThreadName, long Seq)> _waits = new();

    private static long _waitSeq;

    private static int _timeoutSeconds;
    private static int _totalCases;
    private static int _index;

    /// <summary>用例代次：<see cref="BeginCase"/>/<see cref="EndCase"/> 各 +1。看门狗据此判断"睡觉期间
    /// 世界变了没有"（比比较用例名字符串更可靠：同一个用例名连跑两次也不会误判）。</summary>
    private static int _generation;
    private static readonly System.Diagnostics.Stopwatch _sinceStart = System.Diagnostics.Stopwatch.StartNew();
    private static string _currentSuite = "";
    private static string _currentCase = "";
    private static long _currentStartedMs;
    private static Thread? _watchdog;
    private static Thread? _diagnosticThread;

    /// <summary>由 harness 入口在解析完参数后调用一次。<paramref name="timeoutSeconds"/> 为 0 表示不启用超时。</summary>
    public static void Configure(int timeoutSeconds, int totalCases)
    {
        _timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 0;
        _totalCases = totalCases;
        _index = 0;
        if (_timeoutSeconds > 0 && _watchdog is null)
        {
            _watchdog = new Thread(WatchdogLoop, 4 * 1024 * 1024)
            {
                IsBackground = true,
                Name = "tsgo-harness-watchdog",
            };
            _watchdog.Start();
        }
    }

    /// <summary>每个用例开始前调用（记录当前用例名并唤醒看门狗）。</summary>
    public static void BeginCase(string suite, string caseName)
    {
        lock (_mu)
        {
            _currentSuite = suite;
            _currentCase = caseName;
            _currentStartedMs = _sinceStart.ElapsedMilliseconds;
            _generation++;
            Monitor.PulseAll(_mu);
        }
    }

    /// <summary>每个用例结束后调用（停看门狗并打印一行进度；含已用时与粗略 ETA）。</summary>
    public static void EndCase(string suite, string caseName, double elapsedMs)
    {
        string line;
        lock (_mu)
        {
            _currentSuite = "";
            _currentCase = "";
            _generation++;
            _index++;
            var index = _index;
            var total = _totalCases;
            var elapsed = _sinceStart.Elapsed;
            var eta = total > 0 && index > 0
                ? TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / index * (total - index))
                : TimeSpan.Zero;
            var etaText = total > 0
                ? $", {index}/{total}, elapsed {Format(elapsed)}, eta {Format(eta)}"
                : $", #{index}, elapsed {Format(elapsed)}";
            line = $"[{suite}] {caseName}: {elapsedMs.ToString("N0", CultureInfo.InvariantCulture)} ms{etaText}";
            Monitor.PulseAll(_mu);
        }

        Console.WriteLine(line);
    }

    /// <summary>登记一条"超时时值得看它是否还活着"的线程（harness 用例线程、LSP 泵线程……）。
    /// 非 Go 源：纯粹为了超时诊断，不影响任何语义。</summary>
    public static void RegisterThread(Thread thread)
    {
        if (_timeoutSeconds <= 0)
        {
            return;
        }

        lock (_mu)
        {
            foreach (var (known, _) in _known)
            {
                if (ReferenceEquals(known, thread))
                {
                    return;
                }
            }

            // 标签带上"登记时正在跑哪个用例"，超时 dump 才能把线程归属到用例（多条会话的线程会同时活着）。
            _known.Add((thread, _currentCase));
        }
    }

    /// <summary>记一条诊断事件（环形缓冲，只在超时时打印）。非 Go 源：纯粹为了诊断。</summary>
    public static void Note(string message)
    {
        if (_timeoutSeconds <= 0)
        {
            return;
        }

        lock (_mu)
        {
            if (_notes.Count >= _maxNotes)
            {
                _notes.Dequeue();
            }

            _notes.Enqueue((_sinceStart.ElapsedMilliseconds, message));
        }
    }

    /// <summary>
    /// 登记"当前线程正在等什么"，直到返回值被 dispose；用于超时时回答"卡在哪个等待点"。
    /// 非 Go 源：纯粹为了超时诊断，不影响任何语义（<paramref name="description"/> 只在超时时被读取）。
    /// </summary>
    public static WaitScope Wait(string description)
    {
        if (_timeoutSeconds <= 0)
        {
            return default;
        }

        var id = Environment.CurrentManagedThreadId;
        var seq = Interlocked.Increment(ref _waitSeq);
        _waits[id] = (description, _sinceStart.ElapsedMilliseconds, Thread.CurrentThread.Name ?? "", seq);
        return new WaitScope(id, seq);
    }

    /// <summary>见 <see cref="Wait"/>。</summary>
    public readonly struct WaitScope : IDisposable
    {
        private readonly int _threadId;
        private readonly long _seq;

        internal WaitScope(int threadId, long seq)
        {
            _threadId = threadId;
            _seq = seq;
        }

        public void Dispose()
        {
            // 只清掉"自己登记的那一条"：同一线程上嵌套的等待点（外层还没结束时内层先 dispose）
            // 不会把外层的记录误删，超时诊断才不会"看不到正在等什么"。
            if (_threadId != 0 && _waits.TryGetValue(_threadId, out var current) && current.Seq == _seq)
            {
                _waits.TryRemove(_threadId, out _);
            }
        }
    }

    /// <summary>
    /// 看门狗主循环：等待"某个用例正在跑"（<see cref="BeginCase"/> 唤醒），再睡到它的截止时间。
    /// 若一觉睡满且代次未变 ⇒ 同一个用例仍在跑 ⇒ 超时。
    /// </summary>
    private static void WatchdogLoop()
    {
        while (true)
        {
            string suite;
            string caseName;
            long startedMs;
            lock (_mu)
            {
                while (_currentCase.Length == 0)
                {
                    Monitor.Wait(_mu);
                }

                var generation = _generation;
                suite = _currentSuite;
                caseName = _currentCase;
                startedMs = _currentStartedMs;

                // false = 睡满超时（没被 Pulse 唤醒）；true = 用例结束/换新用例，重新评估。
                if (Monitor.Wait(_mu, TimeSpan.FromSeconds(_timeoutSeconds)))
                {
                    continue;
                }

                // 睡满后仍持有锁，且期间没有任何 Pulse ⇒ 代次必然还是同一个；再确认一次以防万一。
                if (_generation == generation && _currentCase.Length != 0)
                {
                    // 落到下面处理超时。
                }
                else
                {
                    continue;
                }
            }

            OnTimeout(suite, caseName, startedMs);
        }
    }

    private static void OnTimeout(string suite, string caseName, long startedMs)
    {
        Console.Out.Flush();
        Console.Error.WriteLine(
            $"TIMEOUT: case '{caseName}' (suite '{suite}') exceeded {_timeoutSeconds}s " +
            $"(started at {startedMs} ms into the run); aborting the whole run " +
            $"(退出码 {_timeoutExitCode})——与 `go test -timeout` 同样的语义：不可安全中断单个用例，故中止整轮。");
        Console.Error.Flush();

        var diagnostic = _diagnosticThread;
        if (diagnostic is null || !diagnostic.IsAlive)
        {
            diagnostic = new Thread(DumpWaitSites, 4 * 1024 * 1024)
            {
                IsBackground = true,
                Name = "tsgo-harness-timeout-dump",
            };
            _diagnosticThread = diagnostic;
            diagnostic.Start();
        }

        diagnostic.Join(_diagnosticDeadline);
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit(_timeoutExitCode);
    }

    /// <summary>超时诊断：谁还活着、谁在等什么、会话里发生了什么（不可能拿到别的线程的托管栈，见文件头）。</summary>
    private static void DumpWaitSites()
    {
        try
        {
            (Thread Thread, string Label)[] threads;
            (long AtMs, string Message)[] notes;
            lock (_mu)
            {
                threads = [.. _known];
                notes = [.. _notes];
            }

            var now = _sinceStart.ElapsedMilliseconds;
            Console.Error.WriteLine($"---- timeout diagnostics ({threads.Length} registered thread(s)) ----");

            // 只详列"还活着"的线程 + 已登记等待点的线程（死线程单独计数）——死/活本身就是关键证据：
            // 例如 writeLoop 线程死了而 readLoop 还活着，就是"写循环自行退出导致会话卡住"。
            var dead = 0;
            foreach (var (t, label) in threads)
            {
                var alive = t.IsAlive;
                var hasWait = _waits.TryGetValue(t.ManagedThreadId, out var wait);
                if (!alive && !hasWait)
                {
                    dead++;
                    continue;
                }

                var waitText = hasWait
                    ? $", waiting {Format(TimeSpan.FromMilliseconds(now - wait.SinceMs))} for: {wait.Description}"
                    : "";
                Console.Error.WriteLine(
                    $"  thread #{t.ManagedThreadId} '{t.Name ?? "<unnamed>"}' case='{label}' alive={alive} " +
                    $"state={t.ThreadState}{waitText}");
            }

            Console.Error.WriteLine($"  (+{dead} registered thread(s) already exited)");

            // 不在登记表里的等待点（例如线程池线程）也一并列出，避免漏掉真正的卡点。
            foreach (var (id, wait) in _waits.OrderBy(kv => kv.Value.SinceMs))
            {
                if (threads.Any(t => t.Thread.ManagedThreadId == id))
                {
                    continue;
                }

                Console.Error.WriteLine(
                    $"  thread #{id} '{wait.ThreadName}' (unregistered), waiting " +
                    $"{Format(TimeSpan.FromMilliseconds(now - wait.SinceMs))} for: {wait.Description}");
            }

            if (notes.Length > 0)
            {
                Console.Error.WriteLine($"---- diagnostic log (last {notes.Length} note(s)) ----");
                foreach (var (atMs, message) in notes)
                {
                    Console.Error.WriteLine($"  [+{Format(TimeSpan.FromMilliseconds(atMs))}] {message}");
                }
            }

            Console.Error.WriteLine("---- end timeout diagnostics ----");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"<timeout diagnostics failed: {e.GetType().Name}: {e.Message}>");
        }
        finally
        {
            Console.Error.Flush();
        }
    }

    private static string Format(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h{ts.Minutes}m{ts.Seconds}s"
            : ts.TotalMinutes >= 1
                ? $"{ts.Minutes}m{ts.Seconds}s"
                : $"{ts.TotalSeconds.ToString("N1", CultureInfo.InvariantCulture)}s";
}
