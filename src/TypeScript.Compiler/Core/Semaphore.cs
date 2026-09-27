// ported from: internal/core/semaphore.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `context.Context` → CoreBasics 移植的 `Context`（Core/Context.cs）：Go 的 `<-ctx.Done()` 在这里是
//    `ctx.CancellationToken`（`Context.Background` 对应 CancellationToken.None，即 Go 里永远不触发的
//    nil Done channel）。
//
// 2) Go 的 `Acquire() (release func())` → `Action Acquire()`；`TryAcquire(ctx) (release func(), acquired bool)`
//    → `(Action Release, bool Acquired) TryAcquire(Context ctx)`（元组对应 Go 的多返回值，同 Collections 里
//    `SyncMap.Load` 的 `(V Value, bool Ok)` 约定）。与 Go 一致：`Acquired == false` 时返回的 Release 是
//    **空操作**（Go 返回 `func() {}`）。
//
// 3) Go 的 `ch chan struct{}`（容量 maxConcurrency 的带缓冲 channel：发送=获取、接收=释放）→
//    `SemaphoreSlim(maxConcurrency, maxConcurrency)`（Wait=发送、Release=接收；`TryAcquire` 用
//    `Wait(CancellationToken)`，取消时抛 OperationCanceledException —— 捕获后返回 false，等价于 Go 的
//    `case <-ctx.Done(): return func(){}, false`）。
//    差异：Go 的 release 多调用一次会**阻塞在接收上**（channel 已空），C# 的 `SemaphoreSlim.Release`
//    会抛 SemaphoreFullException（超过初始计数）。TSGO 的调用点都是配对使用，不受影响。
//
// 4) 零值：Go 的 `UnlimitedSemaphore` 是零尺寸结构体，`UnlimitedSemaphore{}` 可用 → C# 无零尺寸类型，
//    用 `new UnlimitedSemaphore()`。Go 文件里的 `var _ Semaphore = ...` 是编译期断言，C# 里由
//    `: Semaphore` 声明本身承担，不再保留。
//
// 5) `panic("maxConcurrency must be positive")` → `Debug.Fail(...)`（抛 DebugFailureException），
//    见 docs/architecture.md §3.2。
namespace TypeScript.Core;

/// <summary>Go 的 <c>Semaphore</c>：并发额度。</summary>
public interface Semaphore
{
    Action Acquire();

    (Action Release, bool Acquired) TryAcquire(Context ctx);
}

/// <summary>Go 的 <c>UnlimitedSemaphore</c>：不限制并发（Acquire 总是立即成功）。</summary>
public sealed class UnlimitedSemaphore : Semaphore
{
    private static readonly Action _noop = static () => { };

    public Action Acquire()
    {
        return _noop;
    }

    public (Action Release, bool Acquired) TryAcquire(Context ctx)
    {
        return (_noop, true);
    }
}

/// <summary>Go 的 <c>LimitedSemaphore</c>：容量 maxConcurrency 的信号量。</summary>
public sealed class LimitedSemaphore : Semaphore
{
    private static readonly Action _noop = static () => { };

    // Go: `ch chan struct{}`
    private readonly SemaphoreSlim _ch;
    // Go: `release func()`
    private readonly Action _release;

    internal LimitedSemaphore(int maxConcurrency)
    {
        if (maxConcurrency <= 0)
        {
            Debug.Fail("maxConcurrency must be positive");
        }

        _ch = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _release = () => _ch.Release();
    }

    public Action Acquire()
    {
        _ch.Wait();
        return _release;
    }

    public (Action Release, bool Acquired) TryAcquire(Context ctx)
    {
        // Go: select { case s.ch <- struct{}{}: return s.release, true; case <-ctx.Done(): return func(){}, false }
        try
        {
            _ch.Wait(ctx.CancellationToken);
            return (_release, true);
        }
        catch (OperationCanceledException)
        {
            return (_noop, false);
        }
    }
}

public static partial class Core
{
    public static LimitedSemaphore NewLimitedSemaphore(int maxConcurrency)
    {
        return new LimitedSemaphore(maxConcurrency);
    }
}
