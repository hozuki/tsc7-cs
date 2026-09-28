// ported from: (Go 标准库) os/signal 的 NotifyContext，按 cmd/tsc 的三处用法落地
//              （main.go:29-30、lsp.go:48-49、api.go:74-75）
//
// 与 Go 的偏离（逐条给依据）：
//
// 1) Go 的 `ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)` 返回
//    (context.Context, context.CancelFunc) → C# 侧的 `SignalContext.NotifyContext()`（`Context` + `IDisposable`），
//    Go 的 `defer stop()` → `using`（stop 的语义是"恢复这些信号的默认处置"，`Dispose` 做的正是这件事）。
// 2) SIGINT（Ctrl+C）走 `Console.CancelKeyPress`：它是 .NET 里 Ctrl+C 的**唯一**跨平台入口，而且 .NET 的 Unix 实现
//    本身就是"在内部的 PosixSignalRegistration(SIGINT) 上再抬 CancelKeyPress 事件"（见 ConsolePal.Unix）。若我们
//    同时再注册一个 PosixSignalRegistration(SIGINT)，两个处理器会争 `PosixSignalContext.Cancel` 的最终值（谁最后写
//    谁生效）——结果是"第一次 Ctrl+C 就被默认处置干掉"这类不确定行为。所以本类**每个信号只用一种机制**：
//    SIGINT 用 CancelKeyPress，SIGTERM 用 PosixSignalRegistration（它没有控制台等价物；Windows 上 PosixSignalRegistration
//    对 SIGTERM 抛 PlatformNotSupportedException，而 Windows 进程本来也收不到外部 SIGTERM ⇒ 与 Go 同）。
// 3) 第一次收到信号：取消 Context（Go 的 `ctx.Done()` 触发，CLI 据此优雅收尾）并**拦截**默认处置（进程不死）；
//    第二次收到信号：不再拦截 → 走平台默认处置（立刻终止），与 Go 的 `stop()` 之后信号恢复默认处置的效果一致。
// 4) Ctrl+Break 不拦截：Go 只 Notify SIGINT/SIGTERM，SIGQUIT（Unix 的 Ctrl+\，Windows 的 Ctrl+Break 对应
//    CTRL_BREAK_EVENT）会走默认处置。
// 5) Go 的 `ctx.Done() <-chan struct{}` → `Context.CancellationToken`（Core/Context.cs 的文件头第 1 条）。
// 6) `sync` 保证：信号处理器可能在线程池线程上跑，`_cancelled`/`_disposed` 用一把锁保护（Go 的 signal 包内部同样是
//    互斥的）。
using global::System.Runtime.InteropServices;
using TypeScript.Core;

using static TypeScript.Core.Core;

namespace TypeScript.Tsc;

/// <summary>
/// Go: <c>func signal.NotifyContext(parent context.Context, signals ...os.Signal) (ctx context.Context, stop context.CancelFunc)</c>
/// 在 cmd/tsc 里的用法（SIGINT + SIGTERM）。
/// </summary>
internal sealed class SignalContext : IDisposable
{
    /// <summary>
    /// Go: NotifyContext 内部那个派生 ctx 的 cancel（<c>ctx.Done()</c> 的来源）。
    /// **不**在 Dispose 里释放：Go 的 <c>stop()</c> 也只取消不释放；而且看门狗线程/信号处理器可能正持有
    /// <c>CancellationToken</c>（释放会让它们的读取抛 ObjectDisposedException）。CLI 是进程级生命周期，无需回收。
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    private readonly ConsoleCancelEventHandler _onCancelKeyPress;
    private readonly PosixSignalRegistration? _sigterm;
    private readonly object _gate = new();
    private bool _cancelled;
    private bool _disposed;

    /// <summary>Go: <c>signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)</c>（main.go:29 等三处）。</summary>
    public static SignalContext NotifyContext() => new();

    /// <summary>Go: NotifyContext 返回的第一个值（派生后的 ctx）——Go 里就是一个实例，这里同样只派生一次。</summary>
    private readonly Context _context;

    private SignalContext()
    {
        _context = WithCancellation(Context.Background, _cts.Token);

        // Go: syscall.SIGINT（见文件头偏离 2）。
        _onCancelKeyPress = (_, e) =>
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlBreak)
            {
                return;   // 见文件头偏离 4
            }
            e.Cancel = HandleSignal();
        };
        Console.CancelKeyPress += _onCancelKeyPress;

        // Go: syscall.SIGTERM（见文件头偏离 2/3）。
        try
        {
            _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => ctx.Cancel = HandleSignal());
        }
        catch (PlatformNotSupportedException)
        {
            _sigterm = null;
        }
        catch (NotSupportedException)
        {
            _sigterm = null;
        }
    }

    /// <summary>Go: NotifyContext 返回的第一个值（派生后的 ctx）。</summary>
    public Context Context => _context;

    /// <summary>
    /// Go: NotifyContext 返回的 <c>stop</c>（<c>defer stop()</c>；lsp.go 的父进程看门狗也直接调它）。
    /// 两个作用与 Go 一致：取消派生 ctx + 恢复信号的默认处置（这里 = 摘掉处理器）。可重复调用（Go 的 CancelFunc 亦然）。
    /// </summary>
    internal void Stop()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }

        Console.CancelKeyPress -= _onCancelKeyPress;
        _sigterm?.Dispose();
        CancelQuietly();
    }

    /// <summary>Go: <c>defer stop()</c> → <c>using</c>（见文件头偏离 1）。</summary>
    public void Dispose() => Stop();

    /// <summary>
    /// 返回 true = 拦截默认处置（进程继续跑），false = 交给平台默认处置（终止）。
    /// 对应 Go 里"第一个信号被 Notify 吃掉、stop() 之后恢复默认"的行为（见文件头偏离 3）。
    /// </summary>
    private bool HandleSignal()
    {
        lock (_gate)
        {
            if (_cancelled)
            {
                return false;
            }
            _cancelled = true;
        }

        CancelQuietly();
        return true;
    }

    /// <summary>Go 的 <c>cancel()</c> 在已释放的 CTS 上仍是安全的 no-op（同 Lsp/Server.cs 的 CancelQuietly）。</summary>
    private void CancelQuietly()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
