// ported from: internal/lsp/progress.go:1-218（整文件）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **并发模型**：Go 的 `projectLoadingProgress` 是一个常驻 goroutine + `ch chan progressEvent`（容量 64），
//    `start`/`finish` 用 `select { case ch <- ev: case <-done: }` 投递（满则等，关闭则丢弃）；`run` 用
//    `select { case ev := <-ch: case <-delayC(): case <-reporter.done(): }` 三路等待。
//    C# 侧落成"一把锁 + 一个容量 64 的 `Queue<progressEvent>` + 条件变量 + 一个常驻 `Thread`"：
//      * `Enqueue` 对应 `select { case ch <- ev: case <-done: }`（满则 `Monitor.Wait` 25ms 重试，
//        done 已取消则丢弃）。**偏离**：Go 的 select 在"有空间"与"done"同时就绪时是随机挑，这里固定优先 done；
//        两者都只是"关闭时丢事件"，无可观察差异。
//      * `run` 循环顶部的 `if (_delaySignal) { … }` 对应 `case <-delayC():`（见第 2 条）；
//        `Monitor.Wait(_gate)` 的三路唤醒（新事件 / delay 定时器 / done 取消回调）覆盖剩下两个 case。
//    常驻线程用 64 MiB 栈（与 `Project/Background/Queue.cs`、`Execute/Build/Orchestrator.cs` 同一个常量：
//    Go 的 goroutine 栈可增长，.NET 线程栈固定）。
//
// 2) Go 的 `delay *time.Timer` + `delayC()`/`stopDelay()` 用 `time.Timer.C`（**一次性**：取走值后该 channel
//    永远不会再就绪）表达"延迟窗口只触发一次"。C# 侧用 `System.Threading.Timer` + 一个 `_delaySignal` 标志，
//    并且**在消费掉信号后立刻把 `_delayTimer` 置 null**——这正是 Go 里"channel 已 drain"的状态
//    （`delayC()` 之后永远阻塞），从而 `run` 后续的等待时间变成无限。`stopDelay()` = `Timer.Dispose()` + 置 null。
//
// 3) `collections.OrderedMap[string, int]` → `Collections.OrderedMap<string, int>`（已移植）；
//    `core.FirstOrNilSeq(loading.Keys())` → `Core.FirstOrNilSeq(loading.Keys())`。
//
// 4) `msg.Localize(r.server.locale, args...)` → `msg.Localize(_server.GetLocale(), args)`
//    （Go 直接读 `server.locale` 字段、不加 localeMu；C# 走既有的 `Server.GetLocale()`，多一把读锁，
//    可观察结果相同）。
//
// 5) `time.Duration` → `System.TimeSpan`；`delay <= 0` → `ProgressDelay <= TimeSpan.Zero`。
//
// 6) Go 的三个未导出类型（`progressEvent` / `progressReporter` / `serverProgressReporter`）→ C# 的
//    `ProgressEvent` / `IProgressReporter` / `ServerProgressReporter`（internal；Go 接口名在 C# 侧不加 I 前缀
//    是本仓对"导出"接口的约定，这里是包内接口、且与本文件已有的 `IProcessExitState` 先例一致，故加 I）。
//    两个包级构造函数 `newProjectLoadingProgress` / `newProjectLoadingProgressFromReporter` →
//    静态类 `Lsp` 的 `internal static NewProjectLoadingProgress...`。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.LsProto;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using Message = TypeScript.Diagnostics.Message;

namespace TypeScript.Lsp;

/// <summary>Go: <c>type progressEvent struct</c>（progress.go:11-15）。</summary>
internal sealed class ProgressEvent
{
    /// <summary>Go: <c>message *diagnostics.Message</c>。</summary>
    internal readonly Message _message;

    /// <summary>Go: <c>args []any</c>。</summary>
    internal readonly object?[] _args;

    /// <summary>Go: <c>finish bool</c>。</summary>
    internal readonly bool _finish;

    internal ProgressEvent(Message message, object?[] args, bool finish)
    {
        _message = message;
        _args = args;
        _finish = finish;
    }
}

/// <summary>
/// Go: <c>type progressReporter interface</c>（progress.go:17-27）：把
/// <c>projectLoadingProgress</c> 需要的 LSP 传输操作抽象出来，好在没有完整 Server 的情况下测进度逻辑。
/// </summary>
internal interface IProgressReporter
{
    /// <summary>Go: <c>done() &lt;-chan struct{}</c>：服务器关闭时触发。</summary>
    CancellationToken Done { get; }

    /// <summary>Go: <c>localize(msg *diagnostics.Message, args ...any) string</c>。</summary>
    string Localize(Message msg, params object?[] args);

    /// <summary>Go: <c>createWorkDoneProgress(token string)</c>：请客户端创建进度 token。</summary>
    void CreateWorkDoneProgress(string token);

    /// <summary>Go: <c>sendProgress(token string, value lsproto.WorkDoneProgressBeginOrReportOrEnd)</c>：
    /// 发一条 <c>$/progress</c> 通知。</summary>
    void SendProgress(string token, WorkDoneProgressBeginOrReportOrEnd value);
}

/// <summary>Go: <c>type serverProgressReporter struct</c>（progress.go:29-31）：把 <c>*Server</c> 适配成上面的接口。</summary>
internal sealed class ServerProgressReporter(Server server) : IProgressReporter
{
    private readonly Server _server = server;

    public CancellationToken Done => _server._backgroundCtx.CancellationToken;

    public string Localize(Message msg, params object?[] args) => msg.Localize(_server.GetLocale(), args);

    public void CreateWorkDoneProgress(string token)
    {
        _ = _server.SendClientRequestFireAndForget(WindowWorkDoneProgressCreateInfo, new WorkDoneProgressCreateParams
        {
            Token = new IntegerOrString { String = token },
        });
    }

    public void SendProgress(string token, WorkDoneProgressBeginOrReportOrEnd value)
    {
        _ = _server.SendNotification(ProgressInfo, new ProgressParams
        {
            Token = new IntegerOrString { String = token },
            Value = value,
        });
    }
}

/// <summary>
/// Go: <c>type projectLoadingProgress struct</c>（progress.go:52-56）：管理长耗时操作的 LSP WorkDoneProgress 指示器。
///
/// 单个常驻线程处理 start/finish 事件、维护一张引用计数的活动操作表，并按序发送进度消息。
/// 为避免快速操作导致的闪烁：从**第一个** start 事件算起，直到 `delay` 过去才显示指示器；如果所有操作都在此
/// 之前结束，就完全不会显示进度 UI。
///
/// `start`/`finish` 在内置缓冲（64 个事件）满时可能阻塞，但会在服务器背景上下文被取消时放弃。
/// </summary>
internal sealed class ProjectLoadingProgress
{
    /// <summary>Go: <c>make(chan progressEvent, 64)</c>。</summary>
    private const int _bufferSize = 64;

    /// <summary>见文件头第 1 条：与 Project/Background/Queue.cs 同一个常量。</summary>
    private const int _stackSize = 64 * 1024 * 1024;

    private readonly IProgressReporter _reporter;
    private readonly TimeSpan _delay;

    /// <summary>Go 的 <c>ch chan progressEvent</c> + 它的互斥/条件变量（见文件头第 1 条）。</summary>
    private readonly object _gate = new();
    private readonly Queue<ProgressEvent> _events = new();

    /// <summary>Go 的 <c>delay *time.Timer</c>；null = 没有待触发的延迟窗口（见文件头第 2 条）。</summary>
    private Timer? _delayTimer;

    /// <summary>Go 的 <c>delay.C</c> 里那个还没被取走的值；见文件头第 2 条。</summary>
    private bool _delaySignal;

    private readonly Thread _thread;

    internal ProjectLoadingProgress(IProgressReporter reporter, TimeSpan delay)
    {
        _reporter = reporter;
        _delay = delay;
        _thread = new Thread(Run, _stackSize) { IsBackground = true, Name = "tsgo-project-progress" };
        // Go 的 `case <-p.reporter.done():` 在 run 的 select 里；C# 侧用取消回调唤醒等待中的 run。
        reporter.Done.Register(static state => { var p = (ProjectLoadingProgress)state!; lock (p._gate) { Monitor.PulseAll(p._gate); } }, this);
    }

    internal void StartThread() => _thread.Start();

    /// <summary>Go: <c>func (p *projectLoadingProgress) start(message *diagnostics.Message, args ...any)</c>（progress.go:72-79）。</summary>
    internal void Start(Message message, params object?[] args) =>
        Enqueue(new ProgressEvent(message, args, finish: false));

    /// <summary>Go: <c>func (p *projectLoadingProgress) finish(message *diagnostics.Message, args ...any)</c>（progress.go:81-88）。</summary>
    internal void Finish(Message message, params object?[] args) =>
        Enqueue(new ProgressEvent(message, args, finish: true));

    /// <summary>Go 的 <c>select { case p.ch &lt;- ev: case &lt;-p.reporter.done(): }</c>（见文件头第 1 条）。</summary>
    private void Enqueue(ProgressEvent ev)
    {
        lock (_gate)
        {
            while (true)
            {
                if (_reporter.Done.IsCancellationRequested)
                {
                    // 服务器正在关闭；丢弃该事件。
                    return;
                }

                if (_events.Count < _bufferSize)
                {
                    _events.Enqueue(ev);
                    Monitor.PulseAll(_gate);
                    return;
                }

                Monitor.Wait(_gate, 25);
            }
        }
    }

    /// <summary>
    /// Go: <c>func (p *projectLoadingProgress) run()</c>（progress.go:90-160）：常驻 goroutine，处理所有进度事件。
    /// 它独占全部可变状态，不需要外部同步。
    /// </summary>
    private void Run()
    {
        var loading = new OrderedMap<string, int>();
        var token = ""; // 当前 token；空 = 无进度
        var tokenID = 0;
        var begun = false; // 当前 token 是否已经发过 "begin"

        var delayFired = false; // 延迟定时器触发之后为 true

        lock (_gate)
        {
            while (true)
            {
                if (_reporter.Done.IsCancellationRequested)
                {
                    StopDelay();
                    return;
                }

                // Go 的 `case <-delayC():`（见文件头第 2 条）。
                if (_delayTimer is not null && _delaySignal)
                {
                    // 取走 delay.C 里的那个值；此后该 "channel" 永远不会再就绪。
                    _delayTimer.Dispose();
                    _delayTimer = null;
                    _delaySignal = false;

                    delayFired = true;
                    if (token != "" && loading.Size() > 0)
                    {
                        _reporter.CreateWorkDoneProgress(token);
                        var first = FirstOrNilSeq(loading.Keys()) ?? "";
                        begun = BeginOrReport(token, first, begun);
                    }

                    continue;
                }

                // Go 的 `case ev := <-p.ch:` —— 没有待处理事件时等（新事件 / delay 触发 / done 都会 PulseAll）。
                if (_events.Count == 0)
                {
                    Monitor.Wait(_gate);
                    continue;
                }

                var ev = _events.Dequeue();
                Monitor.PulseAll(_gate);

                var text = _reporter.Localize(ev._message, ev._args);
                if (!ev._finish)
                {
                    var count = loading.GetOrZero(text);
                    loading.Set(text, count + 1);
                    if (token == "")
                    {
                        tokenID++;
                        token = $"tsgo-loading-{tokenID}";
                        begun = false;
                        if (_delay <= TimeSpan.Zero)
                        {
                            delayFired = true;
                            _reporter.CreateWorkDoneProgress(token);
                        }
                        else
                        {
                            delayFired = false;
                            ArmDelay();
                        }
                    }

                    if (delayFired)
                    {
                        begun = BeginOrReport(token, text, begun);
                    }
                }
                else
                {
                    var count = loading.GetOrZero(text);
                    if (count <= 1)
                    {
                        loading.Delete(text);
                    }
                    else
                    {
                        loading.Set(text, count - 1);
                    }

                    if (token == "")
                    {
                        continue;
                    }

                    if (loading.Size() == 0)
                    {
                        if (begun)
                        {
                            _reporter.SendProgress(token, new WorkDoneProgressBeginOrReportOrEnd
                            {
                                End = new WorkDoneProgressEnd(),
                            });
                        }

                        StopDelay();
                        token = "";
                    }
                    else if (delayFired)
                    {
                        var first = FirstOrNilSeq(loading.Keys()) ?? "";
                        _reporter.SendProgress(token, new WorkDoneProgressBeginOrReportOrEnd
                        {
                            Report = new WorkDoneProgressReport
                            {
                                Message = first,
                            },
                        });
                    }
                }
            }
        }
    }

    /// <summary>Go 的 <c>delay = time.NewTimer(p.delay)</c>（progress.go:114）；见文件头第 2 条。</summary>
    private void ArmDelay()
    {
        _delaySignal = false;
        _delayTimer?.Dispose();
        _delayTimer = new Timer(
            static state =>
            {
                var p = (ProjectLoadingProgress)state!;
                lock (p._gate)
                {
                    p._delaySignal = true;
                    Monitor.PulseAll(p._gate);
                }
            },
            this,
            _delay,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>Go 的 <c>stopDelay()</c>（progress.go:100-105）。</summary>
    private void StopDelay()
    {
        _delayTimer?.Dispose();
        _delayTimer = null;
        _delaySignal = false;
    }

    /// <summary>
    /// Go: <c>func (p *projectLoadingProgress) beginOrReport(token, text string, begun bool) bool</c>（progress.go:162-180）：
    /// 还没 begin 就发 WorkDoneProgressBegin，否则发 WorkDoneProgressReport。返回 true 表示 begun。
    /// </summary>
    private bool BeginOrReport(string token, string text, bool begun)
    {
        if (!begun)
        {
            var title = _reporter.Localize(TypeScript.Diagnostics.Diagnostics.Loading);
            _reporter.SendProgress(token, new WorkDoneProgressBeginOrReportOrEnd
            {
                Begin = new WorkDoneProgressBegin
                {
                    Title = title,
                    Message = text,
                },
            });
        }
        else
        {
            _reporter.SendProgress(token, new WorkDoneProgressBeginOrReportOrEnd
            {
                Report = new WorkDoneProgressReport
                {
                    Message = text,
                },
            });
        }

        return true;
    }
}

/// <summary>Go 的 <c>internal/lsp</c> 包级函数（本文件只有 progress.go 的两个）。</summary>
public static partial class Lsp
{
    /// <summary>Go: <c>func newProjectLoadingProgress(server *Server, delay time.Duration) *projectLoadingProgress</c>（progress.go:58-60）。</summary>
    internal static ProjectLoadingProgress NewProjectLoadingProgress(Server server, TimeSpan delay) =>
        NewProjectLoadingProgressFromReporter(new ServerProgressReporter(server), delay);

    /// <summary>Go: <c>func newProjectLoadingProgressFromReporter(reporter progressReporter, delay time.Duration) *projectLoadingProgress</c>（progress.go:62-70）。</summary>
    internal static ProjectLoadingProgress NewProjectLoadingProgressFromReporter(IProgressReporter reporter, TimeSpan delay)
    {
        var p = new ProjectLoadingProgress(reporter, delay);
        p.StartThread();
        return p;
    }
}
