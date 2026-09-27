// ported from: internal/core/workgroup.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `sync.WaitGroup` → 本文件的 `WaitGroup`（Add/Done/Wait，Monitor 手写计数）。Go 里 WaitGroup 是标准库
//    类型，C# 没有等价物：**不能用 `CountdownEvent`** —— 它一旦处于 signaled 状态（包括 initialCount == 0
//    的初始状态）就不允许再 `AddCount`（实测抛 InvalidOperationException "The event is already signaled and
//    cannot be incremented."），而 Go 的 WaitGroup 允许在计数归零前继续 Add（`Queue` 可能在 `RunAndWait`
//    等待期间被调用）。手写版本与 Go 语义一致（计数归零时唤醒 waiter），且不引入 IDisposable（Go 没有 Close）。
//
// 2) `wg.Go(...)`（起 goroutine）→ `Task.Run(...)`（起线程池任务）。每个队列项一个 Task，与"每项一个
//    goroutine"一一对应。**不用 Parallel.For**（它的调度分片与异常语义都和 Go 不同）。
//
// 3) `atomic.Bool` → `volatile bool`（Load/Store 的 acquire/release 语义足够，Go 的 atomic.Bool 也是
//    sequentially consistent，但这里没有依赖更强顺序的代码）。
//
// 4) `sync.Mutex` + `[]func()` → `lock` + `List<Action>`；`pop()` 里 `w.fns[end] = nil; w.fns = w.fns[:end]`
//    用 `List.RemoveAt(end)`（它同样会把腾出的槽位清成 default，即丢掉委托引用）。
//
// 5) **异常语义**：Go 里 queued 函数 panic 会直接终止进程（parallel 版是在别的 goroutine 里崩）。C# 的
//    `Task.Run` 把异常捕获进 Task，而没人 await 它 → 默认**静默丢弃**（.NET Core 不触发
//    UnobservedTaskException 崩溃），这是比 Go 更糟的行为。为了不静默丢失内部错误，这里记录**第一个**
//    异常并在 `RunAndWait` 返回前用 ExceptionDispatchInfo 重新抛出：与单线程版（同步执行、异常自然冒出）
//    行为一致，且"错误不会被吞掉"这条 Go 的不变量得以保留。调用点若要区分"层末尾冒泡"与"当场崩进程"，
//    只能靠这条注释。
//
// 6) errgroup：`errgroup.Group` 没有独立移植类型（Go 侧只是库类型，不属于 core 的 API），它的两个机制
//    （等待全部完成 + 首个 error）直接内联进 `ThrottleGroup`（见下）。
using System.Runtime.ExceptionServices;

namespace TypeScript.Core;

/// <summary>
/// Go 的 <c>sync.WaitGroup</c> 的最小 C# 对应物（见文件头第 1 条：不能用 CountdownEvent）。
/// </summary>
internal sealed class WaitGroup
{
    private readonly object mu = new();
    private int count;

    internal void Add(int delta)
    {
        lock (mu)
        {
            count += delta;
        }
    }

    internal void Done()
    {
        lock (mu)
        {
            count--;
            if (count == 0)
            {
                Monitor.PulseAll(mu);
            }
        }
    }

    internal void Wait()
    {
        lock (mu)
        {
            while (count > 0)
            {
                Monitor.Wait(mu);
            }
        }
    }
}

public interface WorkGroup
{
    // Queue queues a function to run. It may be invoked immediately, or deferred until RunAndWait.
    // It is not safe to call Queue after RunAndWait has returned.
    void Queue(Action fn);

    // RunAndWait runs all queued functions, blocking until they have all completed.
    void RunAndWait();
}

internal sealed class ParallelWorkGroup : WorkGroup
{
    // Go: `done atomic.Bool`
    private volatile bool done;
    // Go: `wg sync.WaitGroup`
    private readonly WaitGroup wg = new();
    // Go 里 panic 会崩进程；见文件头第 5 条。
    private Exception? firstError;

    public void Queue(Action fn)
    {
        if (done)
        {
            Debug.Fail("Queue called after RunAndWait returned");
        }

        wg.Add(1);
        Task.Run(() =>
        {
            try
            {
                fn();
            }
            catch (Exception e)
            {
                Interlocked.CompareExchange(ref firstError, e, null);
            }
            finally
            {
                wg.Done();
            }
        });
    }

    public void RunAndWait()
    {
        try
        {
            wg.Wait();
        }
        finally
        {
            done = true;
        }

        var error = Volatile.Read(ref firstError);
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}

internal sealed class SingleThreadedWorkGroup : WorkGroup
{
    // Go: `done atomic.Bool`
    private volatile bool done;
    // Go: `fnsMu sync.Mutex`
    private readonly object fnsMu = new();
    // Go: `fns []func()`
    private readonly List<Action> fns = new();

    public void Queue(Action fn)
    {
        if (done)
        {
            Debug.Fail("Queue called after RunAndWait returned");
        }

        lock (fnsMu)
        {
            fns.Add(fn);
        }
    }

    public void RunAndWait()
    {
        try
        {
            while (true)
            {
                var fn = Pop();
                if (fn is null)
                {
                    return;
                }

                fn();
            }
        }
        finally
        {
            done = true;
        }
    }

    private Action? Pop()
    {
        lock (fnsMu)
        {
            if (fns.Count == 0)
            {
                return null;
            }

            var end = fns.Count - 1;
            var fn = fns[end];
            fns.RemoveAt(end); // Go: w.fns[end] = nil; w.fns = w.fns[:end]（RemoveAt 同样清空腾出的槽位）
            return fn;
        }
    }
}

/// <summary>ThrottleGroup is like errgroup.Group but with global concurrency limiting via a semaphore.</summary>
public sealed class ThrottleGroup
{
    // Go: `semaphore chan struct{}`（带缓冲 channel 当信号量用）→ SemaphoreSlim；
    // 调用点的 `make(chan struct{}, n)` 对应 `new SemaphoreSlim(n, n)`。
    private readonly SemaphoreSlim semaphore;
    // Go: `group *errgroup.Group` 的等待机制（WaitGroup；见文件头第 1 条）
    private readonly WaitGroup group = new();
    // Go: errgroup.Group 的 `errOnce sync.Once` + 保存的**第一个** error
    private Exception? firstError;

    // Go 里 `g, _ := errgroup.WithContext(ctx)` **丢弃**了派生 context（没有任何地方使用它），
    // 所以这里的 ctx 参数也不使用；保留参数只为与 Go 的 API 形状一致。
    internal ThrottleGroup(Context ctx, SemaphoreSlim semaphore)
    {
        this.semaphore = semaphore;
    }

    /// <summary>
    /// Go 的 <c>Go(fn func() error)</c>：起一个 goroutine，先取信号量额度，运行 fn，结束时归还。
    /// 错误路径从"返回 error"改为"抛异常"（docs §3.2），故这里是 <see cref="Action"/>。
    /// </summary>
    public void Go(Action fn)
    {
        group.Add(1);
        Task.Run(() =>
        {
            try
            {
                // Acquire semaphore slot - this will block until a slot is available
                semaphore.Wait();
                try
                {
                    fn();
                }
                catch (Exception e)
                {
                    // Go: errgroup 只保留第一个 error（errOnce），其余丢弃
                    Interlocked.CompareExchange(ref firstError, e, null);
                }
                finally
                {
                    // Release semaphore slot when done
                    semaphore.Release();
                }
            }
            finally
            {
                group.Done();
            }
        });
    }

    /// <summary>Wait waits for all goroutines to complete and returns the first error encountered, if any.</summary>
    public void Wait()
    {
        group.Wait();

        var error = Volatile.Read(ref firstError);
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}

public static partial class Core
{
    public static WorkGroup NewWorkGroup(bool singleThreaded)
    {
        if (singleThreaded)
        {
            return new SingleThreadedWorkGroup();
        }

        return new ParallelWorkGroup();
    }

    /// <summary>NewThrottleGroup creates a new ThrottleGroup with the given context and semaphore for concurrency limiting.</summary>
    public static ThrottleGroup NewThrottleGroup(Context ctx, SemaphoreSlim semaphore)
    {
        return new ThrottleGroup(ctx, semaphore);
    }
}
