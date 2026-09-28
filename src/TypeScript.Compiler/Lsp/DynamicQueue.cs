// ported from: internal/lsp/dynamic_queue.go:1-94（整文件）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `dynamicQueue[T]` 借两个容量 1 的 channel（`idle`/`ready`）在调用者之间**传递**那个
//    `dynamicQueueState[T]`：state 要么在某个 channel 里，要么被某个线程持有。可观察语义是：
//      * `Put` 在拿到 state 后追加一项，放回 `ready`；
//      * `Get` 只从 `ready` 拿，取出队首；取空后放回 `idle`，否则放回 `ready`。
//    ⇒ 用不变式重述：「`ready` 里有 state ⟺ 队列非空」，`Put` 永不因"队列满"而阻塞（只会短暂地等别的
//    线程交还 state），`Get` 阻塞到非空或 ctx 取消。这就是一个**无界阻塞 FIFO 队列 + 取消**，所以 C# 侧
//    用 `lock` + `Monitor.Wait/PulseAll` 直接实现这套不变式（Go 的 channel 只是实现手段，不是契约）。
//    不用 `BlockingCollection`：它的 `CompleteAdding`/`TryTake` 语义与这里"Get 阻塞在 ctx"不同，
//    也没有 `(T, error)` 的取消返回路径。
//
// 2) `(T, error)` / `error` → C# 直接返回 `T` / `void`，取消时抛 `OperationCanceledException`
//    （docs/architecture.md §3.2：Go 的 error → C# 异常）。Go 里 `Put`/`Get` 的 error **只**可能是
//    `ctx.Err()`，所以异常类型选 `OperationCanceledException` 而不是本仓的哨兵异常。调用点把
//    `if err != nil { … }` 写成 `catch (OperationCanceledException)`。
//
// 3) `case <-ctx.Done():` → `ctx.CancellationToken`（`context.Context` → `TypeScript.Core.Context`，
//    见 Core/Context.cs 文件头）。等待期间用 `CancellationToken.Register` 唤起被阻塞的等待者，
//    而不是轮询超时：与 Go 的"取消立刻唤醒"一致。
//
// 4) `newDynamicQueue[T]()` 是包级泛型函数 → 静态类 `Lsp` 的 `internal static NewDynamicQueue<T>()`
//    （Go 包级函数 → 同名静态类；`internal` 因为它返回 internal 类型）。
//
// 5) Go 的 `state.items[0] = zero` + `state.items = state.items[1:]` 是"清掉引用后前移"；C# 的
//    `Queue<T>.Dequeue()` 同样会把腾出的槽位清成 `default`，语义一致。
//
// 6) **`Get` 里 Register/Dispose 取消回调必须在锁外做（本文件修过的一处死锁）**。Go 的
//    `select { case q.ready: …; case ctx.Done(): … }` 是 channel 操作，取消只是"关闭 Done channel"，
//    任何线程都不会因为取消而阻塞在别的锁上。C# 侧的等价物是 `CancellationToken.Register` + `Monitor`，
//    而 **`CancellationTokenSource.Cancel()` 是在取消线程上同步执行回调的**：回调要拿 `_mu` 来唤醒等待者，
//    所以如果"等待者"是在持有 `_mu` 的情况下 Register/Dispose，就会与取消线程互相等待：
//      取消线程：Cancel() → 执行回调 → 等 `_mu`
//      等待者  ：持有 `_mu` → Register/Dispose → CTS 内部等"回调批次跑完"
//    实测（fourslash 偶发死锁，`dotnet-stack report` 抓到两边栈）正是这条环。因此 `Get` 的结构改成
//    "锁外 Register → 锁内复查 → Monitor.Wait"，且 `_mu` 内不做任何 CTS 操作。`Put` 不涉及 Register，
//    不受影响。
using TypeScript.Core;

namespace TypeScript.Lsp;

/// <summary>
/// Go: <c>type dynamicQueue[T any] struct</c>（dynamic_queue.go:19-22）：一个**可以取消的阻塞 FIFO 队列**。
///
/// 灵感来自 Brian C. Mills 的 "Rethinking Classical Concurrency Patterns"：
/// Go 版本是"状态机，每个状态是一个 channel（idle/ready），任何时刻只有一个调用者持有 state 结构"。
/// C# 侧按上面文件头第 1 条把它落成等价的锁 + 条件变量实现。
/// </summary>
internal sealed class DynamicQueue<T>
{
    /// <summary>Go 的 <c>idle chan *dynamicQueueState[T]</c> / <c>ready chan *dynamicQueueState[T]</c>。</summary>
    private readonly object _mu = new();

    /// <summary>Go 的 <c>dynamicQueueState.items</c>（state 只在锁内被持有）。</summary>
    private readonly Queue<T> _items = new();

    /// <summary>Go: <c>func newDynamicQueue[T any]() *dynamicQueue[T]</c>（dynamic_queue.go:29-35）。</summary>
    internal DynamicQueue()
    {
    }

    /// <summary>
    /// Go: <c>func (q *dynamicQueue[T]) Put(ctx context.Context, item T) error</c>（dynamic_queue.go:37-48）。
    /// </summary>
    internal void Put(Context ctx, T item)
    {
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ctx.CancellationToken);
        }

        lock (_mu)
        {
            _items.Enqueue(item);
            Monitor.PulseAll(_mu);
        }
    }

    /// <summary>
    /// Go: <c>func (q *dynamicQueue[T]) Get(ctx context.Context) (T, error)</c>（dynamic_queue.go:50-71）。
    /// </summary>
    internal T Get(Context ctx)
    {
        var token = ctx.CancellationToken;
        while (true)
        {
            if (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }

            // **必须在锁外** Register / Dispose（见文件头第 6 条：锁内做这件事会与"取消线程正在跑回调"
            // 形成循环等待，实测会把整个 harness 挂死）。
            using var registration = token.Register(_pulseAll, _mu);

            lock (_mu)
            {
                if (_items.Count > 0)
                {
                    return _items.Dequeue();
                }

                // Register 之前/期间就已被取消时，回调已经跑过（不会再有 Pulse），必须在这里再查一次。
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }

                // Go 的 `select { case <-q.ready: …; case <-ctx.Done(): … }`：等"有数据"或"取消"。
                // `Monitor.Wait` 会原子地释放 `_mu`，所以"重查 + Wait"之间不会有丢失的 Pulse（Put/回调都要 `_mu`）。
                Monitor.Wait(_mu);
            }
        }
    }

    /// <summary>取消回调：唤醒所有等待者（要拿 `_mu`，所以调用方绝不能持有 `_mu` 时 Register，见文件头第 6 条）。</summary>
    private static readonly Action<object?> _pulseAll =
        static state => { lock (state!) { Monitor.PulseAll(state); } };
}

/// <summary>Go 的 <c>internal/lsp</c> 包级函数（本文件只有 dynamic_queue.go 的那一个）。</summary>
public static partial class Lsp
{
    /// <summary>Go: <c>func newDynamicQueue[T any]() *dynamicQueue[T]</c>（dynamic_queue.go:29-35）。</summary>
    internal static DynamicQueue<T> NewDynamicQueue<T>() => new();
}
