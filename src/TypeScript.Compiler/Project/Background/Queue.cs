// ported from: internal/project/background/queue.go:1-52（整文件）
//
// 与 Go 的偏离（逐条）：
//
// 1) **并发原语替换（本文件唯一一处）**：Go 的 Queue 是 `sync.WaitGroup` + `wg.Go(fn)`（Go 1.25 起等价于
//    `wg.Add(1) go fn()`），即**每个任务一个 goroutine、任务之间并发执行**——它不是单 worker 串行队列。
//    C# 侧每个任务起一个专用 `Thread`（`IsBackground = true`），栈大小 64MB（与 Execute/Build/Orchestrator.cs
//    的 `_stackSize`、tests/TypeScript.Harness 的常量一致）：Go 的 goroutine 栈可增长到 GB 级，而 C# 线程池
//    线程栈固定 1MB，而后台任务会跑 project 创建/checker 的深递归，用线程池有爆栈风险。可观察语义保持不变：
//    `Enqueue` 登记后立刻返回、任务并发执行、`Wait` 等到计数归零、`Close` 只拒绝新任务。
//
// 2) `sync.WaitGroup` 的计数 → `int _active` + `ManualResetEventSlim _idle`（计数归零时 Set、入队时 Reset）。
//    Go 的注释明确"Wait 期间不阻止新任务入队"，所以 `Wait` 用"读计数 + 等事件"的循环，不把 `Enqueue` 锁住
//    （`_active` 的增减与事件状态在同一把 `Lock` 下更新，故不存在"看到 0 与看到 Set 不一致"的漏唤醒）。
//
// 3) `sync.RWMutex` → `System.Threading.Lock`（本仓统一约定）：`closed` 的读写都用它串行化。Go 里读侧用
//    RLock 只是允许并发读，这里读侧只读一个 bool，串行化不改变任何可观察结果。
//
// 4) `ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`（`context.Context` → `TypeScript.Core.Context`，
//    见 Core/Context.cs 文件头：Go 的 Done()/Err() ↔ CancellationToken）。两处检查的位置与 Go 完全一致：
//    早退（计数增加之前，不入队）与任务体内再查一次。
//
// 5) **异常语义**：Go 里 queued 函数 panic 会终止进程（没人 recover）；本仓既有做法是"记录第一个异常、
//    join 后重抛"（Execute/Build/Orchestrator.cs 的 rangeTasks 注释、Core.WorkGroup）。这里照办：任务抛出的
//    第一个异常被捕获，`Wait()` 返回前用 `ExceptionDispatchInfo` 重抛（**每次** Wait 都重抛，不会丢），
//    计数器照常递减。偏离点：若从不调用 `Wait()`，异常不会立刻终止进程（Go 会），这是本仓的既定偏差。
//
// 6) 包级 `func NewQueue() *Queue` → `Queue.NewQueue()` 静态工厂（M8Project 统一约定：包级函数挂到它构造的
//    类型上）；构造函数保持 private（Go 也只能经 NewQueue 构造）。
//
// 7) 线程启动失败（`Thread.Start()` 抛）时回退计数后再重抛：Go 里 goroutine 创建失败同样是进程级崩溃，
//    但 C# 侧若留下未递减的计数会让 `Wait()` 永久挂起（比 Go 更糟），故显式回退。
using System.Runtime.ExceptionServices;
using TypeScript.Core;

namespace TypeScript.Project.Background;

/// <summary>Go 的 <c>type Queue struct</c>（queue.go:9-13）：后台任务队列。</summary>
public sealed class Queue
{
    // 见文件头 1：与 Execute/Build/Orchestrator.cs 的 `_stackSize` 取同一个值。
    private const int _stackSize = 64 * 1024 * 1024;

    // Go: `wg sync.WaitGroup` / `mu sync.RWMutex` / `closed bool`。
    private readonly Lock _mu = new();
    private readonly ManualResetEventSlim _idle = new(initialState: true);
    private bool _closed;
    private int _active;
    private ExceptionDispatchInfo? _firstError;

    private Queue()
    {
    }

    /// <summary>Go: <c>func NewQueue() *Queue</c>（queue.go:15-18）。</summary>
    public static Queue NewQueue() => new();

    /// <summary>Go: <c>func (q *Queue) Enqueue(ctx context.Context, fn func(context.Context))</c>（queue.go:20-40）。</summary>
    public void Enqueue(Context ctx, Action<Context> fn)
    {
        lock (_mu)
        {
            if (_closed)
            {
                return;
            }
        }

        // Go: `if ctx.Err() != nil { return }`——在计数增加之前早退（不入队）。
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            return;
        }

        Thread thread;
        lock (_mu)
        {
            // Go 的 `wg.Go` 是"Add(1) 后立刻返回"，C# 里登记（计数 + 事件复位）必须原子完成，
            // 否则 Wait 可能在任务登记之前就看到计数 0。
            _active++;
            _idle.Reset();
            thread = new Thread(() => RunTask(ctx, fn), _stackSize) { IsBackground = true, Name = "background-queue" };
        }

        try
        {
            thread.Start();
        }
        catch
        {
            // 见文件头 7：回退计数，避免 Wait 永久挂起。
            Complete(null);
            throw;
        }
    }

    /// <summary>
    /// Go: <c>func (q *Queue) Wait()</c>（queue.go:42-45）：等所有已登记任务完成。
    /// 与 Go 一样，它不阻止等待期间继续 Enqueue（那些新任务不保证被等到）。
    /// </summary>
    public void Wait()
    {
        while (Volatile.Read(ref _active) != 0)
        {
            _idle.Wait();
        }

        // 见文件头 5：本仓既有的"join 后重抛第一个异常"。
        Volatile.Read(ref _firstError)?.Throw();
    }

    /// <summary>Go: <c>func (q *Queue) Close()</c>（queue.go:47-51）：只拒绝新任务，不取消已在跑的任务。</summary>
    public void Close()
    {
        lock (_mu)
        {
            _closed = true;
        }
    }

    // Go: `q.wg.Go(func() { if ctx.Err() != nil { return }; fn(ctx) })` 的闭包体。
    private void RunTask(Context ctx, Action<Context> fn)
    {
        ExceptionDispatchInfo? error = null;
        try
        {
            // 执行前再查一次上下文（Go 的闭包里同样再查一次）。
            if (!ctx.CancellationToken.IsCancellationRequested)
            {
                fn(ctx);
            }
        }
        catch (Exception ex)
        {
            error = ExceptionDispatchInfo.Capture(ex);
        }

        Complete(error);
    }

    // Go: `wg.Done()`（计数递减）；异常用"第一个胜出"的语义记录（Interlocked 兼顾内存屏障，
    // 因为 Wait() 是不持锁读的）。
    private void Complete(ExceptionDispatchInfo? error)
    {
        lock (_mu)
        {
            if (error is not null)
            {
                Interlocked.CompareExchange(ref _firstError, error, null);
            }

            _active--;
            if (_active == 0)
            {
                _idle.Set();
            }
        }
    }
}
