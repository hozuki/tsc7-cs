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
        lock (_mu)
        {
            while (_items.Count == 0)
            {
                if (ctx.CancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ctx.CancellationToken);
                }

                // Go 的 `select { case <-q.ready: …; case <-ctx.Done(): … }`：等"有数据"或"取消"。
                // 已取消时 Register 会同步调用回调（可重入地抢到同一把锁），因此下面的重查是必需的。
                using var registration = ctx.CancellationToken.Register(
                    static state => { lock (state!) { Monitor.PulseAll(state); } },
                    _mu);
                if (_items.Count > 0)
                {
                    break;
                }

                Monitor.Wait(_mu);
            }

            return _items.Dequeue();
        }
    }
}

/// <summary>Go 的 <c>internal/lsp</c> 包级函数（本文件只有 dynamic_queue.go 的那一个）。</summary>
public static partial class Lsp
{
    /// <summary>Go: <c>func newDynamicQueue[T any]() *dynamicQueue[T]</c>（dynamic_queue.go:29-35）。</summary>
    internal static DynamicQueue<T> NewDynamicQueue<T>() => new();
}
