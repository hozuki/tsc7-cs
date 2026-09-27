// ported from: internal/project/checkerpool.go:1-533（全部）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出结构体 `checkerPool` → C# `internal sealed class CheckerPool`（M8Project 仲裁的定名；与
//    `TypeScript.Compiler.CheckerPool` 接口同名但不同命名空间 —— 本文件里接口一律写全限定名）。
//    字段（Go 未导出 → `_camelCase`）：`opts`→`_opts`、`program`→`_program`、`mu`→`_mu`、`discarded`→`_discarded`、
//    `checkers`→`_checkers`、`heldBy`→`_heldBy`、`fileAssociations`→`_fileAssociations`、
//    `requestAssociations`→`_requestAssociations`、`lastReleased`→`_lastReleased`、`cleanupTimer`→`_cleanupTimer`、
//    `persistentChecker`→`_persistentChecker`、`persistentHeld`→`_persistentHeld`、`diagSem`/`querySem`/`persistentSem`
//    →`_diagSem`/`_querySem`/`_persistentSem`、`log`→`_log`、`globalDiagAccumulated`→`_globalDiagAccumulated`、
//    `globalDiagChanged`→`_globalDiagChanged`、`globalDiagCheckerCount`→`_globalDiagCheckerCount`。
//    `checkerHeldAnonymous` → `private const string _checkerHeldAnonymous`。
//    Go 导出方法保持 public（`GetChecker`/`GetGlobalDiagnostics`/`TakeNewGlobalDiagnostics`/`Discard`）；
//    未导出方法 `cleanupIdleCheckers` → `internal`（测试直接调用它，与 Go 测试同包等价）。
// 2) **`CheckerPoolOptions` 在 Go 里是值类型**（`newCheckerPool` 对形参的钳制不会影响调用方）→ C# 的
//    `CheckerPoolOptions` 是可变 class，故构造函数入口先显式复制一份再钳制，复刻 Go 的值语义
//    （否则 `SessionOptions.CheckerPoolOptions` 会被池就地改写）。`IdleTimeout time.Duration` → `TimeSpan`。
// 3) `sync.Mutex` → **`System.Threading.Lock`**（仓内先例 Compiler/CheckerPool.cs）。`p.mu.Lock(); defer p.mu.Unlock()`
//    落成 `lock (_mu) { … }`；但 `tryReacquireForRequest` 里有**跨信号量等待的解锁/重加锁**（Go 明确先 Unlock 再
//    `sem <- struct{}{}`，以免持锁阻塞导致死锁），那一段用显式 `Enter()`/`Exit()` 逐点对应 Go 的 Unlock 位置。
// 4) `chan struct{}`（容量 N 的带缓冲 channel：发送=获取、接收=释放）→ **`SemaphoreSlim(capacity, capacity)`**
//    （仓库既定映射，见 Core/Semaphore.cs 文件头第 3 条：`sem <- struct{}{}` → `Wait()`、`<-sem` → `Release()`）。
//    差异：Go 的 release 多调一次会阻塞，SemaphoreSlim 会抛 SemaphoreFullException —— 本池的 release 都是
//    `OnceFunc` 包装的（每次获取恰好释放一次），不受影响。
// 5) `sync.OnceFunc(f)` → **`TypeScript.Compiler.Compiler.OnceFunc(f)`**（已落地，语义同 Go：第一次调用执行，
//    之后什么都不做）。
// 6) `time.Time` → `DateTimeOffset`：`_lastReleased` 用 `DateTimeOffset?`，`null` 精确对应 Go 的 `time.Time{}`
//    零值 + `IsZero()`；`time.Now()` → `DateTimeOffset.UtcNow`。`time.AfterFunc(delay, f)` →
//    `new Timer(callback, null, delay, Timeout.InfiniteTimeSpan)`；`timer.Reset(delay)` →
//    `timer.Change(delay, Timeout.InfiniteTimeSpan)`；`timer.Stop()` → `Change(Timeout.InfiniteTimeSpan,
//    Timeout.InfiniteTimeSpan)`（Go 的 Stop 不保证回调不在途，Change 同样如此；**不**用 `Timer.Dispose()`，
//    因为池可能在回调自身里停表）。日志里的 `idle` 文本用 `TimeSpan.ToString()`（Go 的 `%v` 是 "5s"）。
// 7) `map[*ast.SourceFile]int` 是**指针键** → `Dictionary<SourceFile,int>(ReferenceEqualityComparer.Instance)`
//    （先例 Compiler/CheckerPool.cs）。`delete` 遍历改写 → 先收集键再删（C# 不允许遍历中改 Dictionary）。
//    切片 `make([]T, n)` 的零值：`string` 是 `""`（Go）⇒ C# `new string[n]` 是 null，必须 `Array.Fill(…, "")`。
// 8) `ctx.Done() == nil` → `!ctx.CancellationToken.CanBeCanceled`（`Context.Background` 的 token 是
//    `CancellationToken.None`，与 Go 的 nil Done channel 一致，见 Core/Context.cs 文件头）。
//    `context.AfterFunc(ctx, f)` → `ctx.CancellationToken.Register(f)`：Go 在**新 goroutine** 里跑回调，
//    C# 在取消线程上**同步**跑（若注册时已取消则立即执行）。回调只取 `_mu` 删一个键，两种时序的终态相同。
// 9) `compiler.SortAndDeduplicateDiagnostics(append(accumulated, globals...))` →
//    `Compiler.SortAndDeduplicateDiagnostics([.. _globalDiagAccumulated, .. globals])`（包级函数落在与包同名的静态类）；
//    `slices.Clone(...)` → 新数组/新 List `[.. …]`。`globalDiagAccumulated []*ast.Diagnostic` → `List<Diagnostic>`
//    （Go 切片只做 append/len/Clone → C# List 最贴近；对外返回 `Diagnostic[]` 复刻 Clone 的"新切片"）。
// 10) `panic("checkerpool: no available query slot despite holding semaphore token")` → `Debug.Fail(...)`（抛
//     DebugFailureException，docs §3.2）；`debug.Assert(...)` → `Debug.Assert(...)`。Debug.Fail 不抛时其后补
//     一个不可达 return 以满足编译器的控制流分析。
// 11) `log func(msg string)` 可为 nil（Go 的函数值零值）→ 构造函数里 `log ?? (static _ => { })` 兜底（与 Go 的
//     `if pool.log == nil` 等价）。所有 `fmt.Sprintf` 调用点改成 C# 内插字符串（`%d/%s` → `{index}`/`{HoldTag(…)}`）。
// 12) `checker.NewChecker(p.program, nil)` → `TypeScript.Checker.Checker.NewChecker(_program, null)`（返回
//     `(Checker, Lock)` 二元组，Go 的 `c, _ :=` → `(c, _) =`）。`TypeScript.Checker` 同时是命名空间与类型名，
//     本文件用 `using Checker = TypeScript.Checker.Checker;` 别名（否则裸 `Checker` 命中命名空间）。
// 13) `noop()`（本包包级函数）→ `private static void Noop()`，作为 `Action` 传给返回元组（Go 直接把函数值
//     `noop` 当 `func()` 用）。
// 14) Go 的 `for i := range p.checkers`（下标遍历）→ C# `for (var i = 0; i < _checkers.Length; i++)`。
namespace TypeScript.Project;

using System;
using System.Collections.Generic;
using System.Threading;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using static TypeScript.Compiler.Compiler;
using static TypeScript.Core.Core;
using Checker = TypeScript.Checker.Checker;

/// <summary>Go: <c>type CheckerPoolOptions struct</c>（checkerpool.go:22-30）。</summary>
public sealed class CheckerPoolOptions
{
    /// <summary>Go: <c>MaxCheckers int</c>：每项目的 checker 槽总数（1 个专用诊断 checker + N-1 个查询 checker），最小 2；0 用默认值 4。</summary>
    public int MaxCheckers;

    /// <summary>Go: <c>IdleTimeout time.Duration</c>：空闲 checker 在被销毁前保留多久；0 用默认值 30s。</summary>
    public TimeSpan IdleTimeout = TimeSpan.Zero;
}

/// <summary>
/// Go: <c>type checkerPool struct</c>（checkerpool.go:41-81）。为一个项目管理三类类型检查器：
/// 诊断（下标 0，单个，空闲回收）、临时（下标 1+，LSP 操作的一次性查询 checker，空闲超时后回收）、
/// API（单个，为类型/符号句柄的引用相等提供稳定实例身份，**永不**空闲回收）。
/// </summary>
internal sealed class CheckerPool : TypeScript.Compiler.CheckerPool
{
    // Go: `checkerHeldAnonymous`（checkerpool.go:16）：heldBy 里代表"被没有请求 ID 的调用方持有"的哨兵。
    private const string _checkerHeldAnonymous = "<anonymous>";

    internal CheckerPoolOptions _opts = null!;
    internal Program _program = null!;

    private readonly Lock _mu = new();

    // Go: `discarded`：本池的 program 已被替换。池仍然完全可用，但停止空闲清理定时器，
    // 使查询 checker 直到池被 GC 才销毁。
    internal bool _discarded;

    // Go: `checkers[0]` 是诊断 checker，`checkers[1:]` 是临时查询 checker；全部参与空闲清理。
    internal Checker?[] _checkers = null!;

    // Go: `heldBy[i]` 是持有 checker i 的 requestID、checkerHeldAnonymous，或空串（未被持有）。
    internal string[] _heldBy = null!;

    // Go: `file → query checker index (1+)`。
    internal Dictionary<SourceFile, int> _fileAssociations = null!;

    // Go: `requestID → checker index`。
    internal Dictionary<string, int> _requestAssociations = null!;

    // Go: `lastReleased`：每个 checker 上次被释放的时间（`null` = Go 的 `time.Time{}` 零值）。
    internal DateTimeOffset?[] _lastReleased = null!;

    // Go: `cleanupTimer`：每次释放 checker 时重置；触发时销毁空闲 checker。
    internal Timer? _cleanupTimer;

    // Go: `persistentChecker` 是 API checker，永不空闲清理（为 API 客户端提供稳定实例身份）。
    internal Checker? _persistentChecker;
    internal bool _persistentHeld;

    internal SemaphoreSlim _diagSem = null!;
    internal SemaphoreSlim _querySem = null!;
    internal SemaphoreSlim _persistentSem = null!;

    internal Action<string> _log = null!;
    internal List<Diagnostic> _globalDiagAccumulated = [];
    internal bool _globalDiagChanged;

    /// <summary>Go: <c>globalDiagCheckerCount []int</c>：每个 checker 上次见到的全局诊断条数。</summary>
    internal int[] _globalDiagCheckerCount = null!;

    /// <summary>Go: <c>func newCheckerPool(opts CheckerPoolOptions, program *compiler.Program, log func(msg string)) *checkerPool</c>（checkerpool.go:84-118）。</summary>
    internal static CheckerPool NewCheckerPool(CheckerPoolOptions opts, Program program, Action<string>? log)
    {
        // Go 收下的是结构体副本（见文件头第 2 条）：先复制，钳制不会外溢到调用方的 SessionOptions。
        opts = new CheckerPoolOptions { MaxCheckers = opts.MaxCheckers, IdleTimeout = opts.IdleTimeout };

        if (opts.MaxCheckers <= 0)
        {
            opts.MaxCheckers = 4;
        }
        else if (opts.MaxCheckers < 2)
        {
            opts.MaxCheckers = 2; // 至少 1 个诊断 + 1 个查询 checker
        }

        if (opts.IdleTimeout <= TimeSpan.Zero)
        {
            opts.IdleTimeout = TimeSpan.FromSeconds(30);
        }

        var querySlots = opts.MaxCheckers - 1;
        var heldBy = new string[opts.MaxCheckers];
        // Go: `make([]string, n)` 的元素是 ""（零值）；C# 的 `new string[n]` 是 null → 显式填 ""（见文件头第 7 条）。
        Array.Fill(heldBy, "");
        return new CheckerPool
        {
            _program = program,
            _opts = opts,
            _checkers = new Checker?[opts.MaxCheckers],
            _heldBy = heldBy,
            _fileAssociations = new Dictionary<SourceFile, int>(ReferenceEqualityComparer.Instance),
            _requestAssociations = [],
            _lastReleased = new DateTimeOffset?[opts.MaxCheckers],
            _diagSem = new SemaphoreSlim(1, 1),
            _querySem = new SemaphoreSlim(querySlots, querySlots),
            _persistentSem = new SemaphoreSlim(1, 1),
            // Go: `if pool.log == nil { pool.log = func(msg string) {} }`
            _log = log ?? (static _ => { }),
            _globalDiagCheckerCount = new int[opts.MaxCheckers],
        };
    }

    /// <summary>Go: <c>func holdTag(requestID string) string</c>（checkerpool.go:121-126）。</summary>
    private static string HoldTag(string requestID)
    {
        if (requestID == "")
        {
            return _checkerHeldAnonymous;
        }

        return requestID;
    }

    /// <summary>Go: <c>func (p *checkerPool) GetChecker(ctx context.Context, file *ast.SourceFile) (*checker.Checker, func())</c>（checkerpool.go:128-147）。</summary>
    public (Checker Checker, Action Done) GetChecker(Context ctx, SourceFile? file)
    {
        var lifetime = GetCheckerLifetime(ctx);
        var requestID = GetRequestID(ctx);

        // 请求亲和性通过 context 完成回调清理。若 context 永远不可取消（ctx.Done() == nil，例如
        // context.Background()），那个清理永远不会跑，requestAssociations 会无限增长，所以彻底禁用亲和性。
        if (!ctx.CancellationToken.CanBeCanceled)
        {
            requestID = "";
        }

        switch (lifetime)
        {
            case CheckerLifetime.Diagnostics:
                return GetDiagnosticsChecker(ctx, requestID);
            case CheckerLifetime.API:
                return GetPersistentChecker();
            default:
                return GetQueryChecker(ctx, requestID, file);
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) tryReacquireForRequest(requestID string, sem chan&lt;- struct{}, isDiag bool) (*checker.Checker, func(), bool)</c>
    /// （checkerpool.go:149-218）。若该请求已有 checker：仍被持有则直接返回，否则抢一个信号量槽重新获取。
    /// 返回 (checker, release, true) 表示请求已被服务；(null, null, false) 表示调用方要继续正常获取 ——
    /// 此时信号量槽**已经**被占用。**不得**在持有 p.mu 时调用。
    /// </summary>
    private (Checker? Checker, Action? Release, bool Ok) TryReacquireForRequest(string requestID, SemaphoreSlim sem, bool isDiag)
    {
        if (requestID == "")
        {
            sem.Wait();
            return (null, null, false);
        }

        _mu.Enter();

        if (!_requestAssociations.TryGetValue(requestID, out var index))
        {
            _mu.Exit();
            sem.Wait();
            return (null, null, false);
        }

        // 校验关联的下标属于期望的类别：下标 0 是诊断，1+ 是查询。
        if ((isDiag && index != 0) || (!isDiag && index == 0))
        {
            _requestAssociations.Remove(requestID);
            _mu.Exit();
            sem.Wait();
            return (null, null, false);
        }

        var c = _checkers[index];
        if (c is null)
        {
            _requestAssociations.Remove(requestID);
            _mu.Exit();
            sem.Wait();
            return (null, null, false);
        }

        var held = _heldBy[index];
        if (held == requestID)
        {
            // 同一请求，checker 仍被持有 —— 不抢槽直接返回。
            _mu.Exit();
            return (c, Noop, true);
        }

        if (held == "")
        {
            // 同一请求在释放后重新获取 —— 需要一个信号量槽（先解锁再抢槽，见文件头第 3 条）。
            _mu.Exit();
            sem.Wait();
            _mu.Enter();

            // 重新检查：等待槽期间 checker 可能已被销毁。
            if (_checkers[index] == c && _heldBy[index] == "")
            {
                _heldBy[index] = requestID;
                _mu.Exit();
                return (c, CreateRelease(requestID, index, c), true);
            }

            _mu.Exit();

            // 等待槽期间 checker 被替换/销毁。槽仍被占用；调用方会用它做正常获取。
            return (null, null, false);
        }

        // checker 被另一个请求持有 —— 照常抢槽。
        _mu.Exit();
        sem.Wait();
        return (null, null, false);
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) getDiagnosticsChecker(ctx context.Context, requestID string) (*checker.Checker, func())</c>
    /// （checkerpool.go:220-250）。返回专用的诊断 checker（下标 0），首次使用时创建；正被占用时阻塞在 diagSem 上。
    /// </summary>
    private (Checker Checker, Action Done) GetDiagnosticsChecker(Context ctx, string requestID)
    {
        const int diagIndex = 0;

        var (reacquired, reacquireRelease, ok) = TryReacquireForRequest(requestID, _diagSem, true);
        if (ok)
        {
            return (reacquired!, reacquireRelease!);
        }

        // 槽已被占用 —— 继续正常获取。
        lock (_mu)
        {
            if (_checkers[diagIndex] is null)
            {
                _log("checkerpool: Creating diagnostics checker");
                var (created, _) = Checker.NewChecker(_program, null);
                _checkers[diagIndex] = created;
            }

            var c = _checkers[diagIndex]!;
            _heldBy[diagIndex] = HoldTag(requestID);
            _log("checkerpool: Acquired diagnostics checker for request " + HoldTag(requestID));
            if (requestID != "")
            {
                if (!_requestAssociations.ContainsKey(requestID))
                {
                    _requestAssociations[requestID] = diagIndex;
                    RegisterRequestCleanup(ctx, requestID);
                }
            }

            return (c, CreateRelease(requestID, diagIndex, c));
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) getQueryChecker(ctx context.Context, requestID string, file *ast.SourceFile) (*checker.Checker, func())</c>
    /// （checkerpool.go:252-283）。从下标 1+ 取临时查询 checker：先用请求亲和性，再用文件亲和性，最后查找/创建。
    /// 所有查询槽都被占用时阻塞在 querySem 上。
    /// </summary>
    private (Checker Checker, Action Done) GetQueryChecker(Context ctx, string requestID, SourceFile? file)
    {
        var (reacquired, reacquireRelease, ok) = TryReacquireForRequest(requestID, _querySem, false);
        if (ok)
        {
            return (reacquired!, reacquireRelease!);
        }

        // 槽已被占用 —— 继续正常获取。
        lock (_mu)
        {
            // 先试文件亲和性。
            if (file is not null)
            {
                if (_fileAssociations.TryGetValue(file, out var affinityIndex) && affinityIndex > 0)
                {
                    var affinityChecker = _checkers[affinityIndex];
                    if (affinityChecker is not null && _heldBy[affinityIndex] == "")
                    {
                        _heldBy[affinityIndex] = HoldTag(requestID);
                        if (requestID != "")
                        {
                            if (!_requestAssociations.ContainsKey(requestID))
                            {
                                _requestAssociations[requestID] = affinityIndex;
                                RegisterRequestCleanup(ctx, requestID);
                            }
                        }

                        return (affinityChecker, CreateRelease(requestID, affinityIndex, affinityChecker));
                    }
                }
            }

            // 找任意可用查询 checker，没有就建一个。
            var (c, index) = FindOrCreateQueryCheckerLocked();
            _heldBy[index] = HoldTag(requestID);
            _log($"checkerpool: Acquired query checker {index} for request {HoldTag(requestID)}");
            if (requestID != "")
            {
                if (!_requestAssociations.ContainsKey(requestID))
                {
                    _requestAssociations[requestID] = index;
                    RegisterRequestCleanup(ctx, requestID);
                }
            }

            if (file is not null)
            {
                _fileAssociations[file] = index;
            }

            return (c, CreateRelease(requestID, index, c));
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) findOrCreateQueryCheckerLocked() (*checker.Checker, int)</c>（checkerpool.go:285-313）。
    /// 返回空闲的查询 checker，或在第一个空槽里新建一个。信号量保证至少有一个槽可用。**必须**在持有 p.mu 时调用。
    /// </summary>
    private (Checker Checker, int Index) FindOrCreateQueryCheckerLocked()
    {
        // 优先复用已有的空闲 checker。
        for (var i = 1; i < _checkers.Length; i++)
        {
            var c = _checkers[i];
            if (c is not null && _heldBy[i] == "")
            {
                return (c, i);
            }
        }

        // 在第一个空槽里创建。
        for (var i = 1; i < _checkers.Length; i++)
        {
            if (_checkers[i] is null)
            {
                _log($"checkerpool: Creating query checker {i}");
                var (created, _) = Checker.NewChecker(_program, null);
                _checkers[i] = created;
                return (created, i);
            }
        }

        // Go: panic("checkerpool: no available query slot despite holding semaphore token")
        Debug.Fail("checkerpool: no available query slot despite holding semaphore token");
        return (null!, 0);
    }

    /// <summary>Go: <c>func (p *checkerPool) getPersistentChecker() (*checker.Checker, func())</c>（checkerpool.go:315-343）。</summary>
    private (Checker Checker, Action Done) GetPersistentChecker()
    {
        _persistentSem.Wait();

        Checker c;
        lock (_mu)
        {
            if (_persistentChecker is null)
            {
                _log("checkerpool: Creating persistent checker");
                var (created, _) = Checker.NewChecker(_program, null);
                _persistentChecker = created;
            }

            c = _persistentChecker!;
            _persistentHeld = true;
        }

        return (c, OnceFunc(() =>
        {
            lock (_mu)
            {
                _persistentHeld = false;
                if (c.WasCanceled())
                {
                    // 已取消的 checker 复用时会 panic，所以丢弃它；下次 API 获取会新建一个持久 checker。
                    _log("checkerpool: Persistent checker was canceled, disposing");
                    if (_persistentChecker == c)
                    {
                        _persistentChecker = null;
                    }
                }
            }

            _persistentSem.Release();
        }));
    }

    /// <summary>Go: <c>func (p *checkerPool) createRelease(requestID string, index int, c *checker.Checker) func()</c>（checkerpool.go:345-385）。</summary>
    private Action CreateRelease(string requestID, int index, Checker c)
    {
        return OnceFunc(() =>
        {
            lock (_mu)
            {
                if (c.WasCanceled())
                {
                    // 已取消的 checker 必须销毁。
                    _log($"checkerpool: Checker {index} for request {HoldTag(requestID)} was canceled, disposing");
                    DisposeCheckerLocked(index, c);
                }
                else
                {
                    // 诊断 checker 释放时把它的全局诊断并入累计集合（查询 checker 在序列化类型时可能产生附带错误）。
                    if (index == 0)
                    {
                        MergeGlobalDiagnosticsFromCheckerLocked(index, c);
                    }

                    _heldBy[index] = "";
                    _lastReleased[index] = DateTimeOffset.UtcNow;
                    if (!_discarded)
                    {
                        ScheduleCleanupLocked();
                    }

                    // 若已 discarded，跳过清理调度 —— checker 一直存活到池被 GC，
                    // 这样 API 客户端可以继续解析类型/符号句柄。
                }
            }

            // 先解锁再释放信号量槽：若在持有 p.mu 时接收 channel，被唤醒的 goroutine 可能立刻去抢 p.mu，
            // 造成优先级反转或不必要的争用。
            if (index == 0)
            {
                _diagSem.Release();
            }
            else
            {
                _querySem.Release();
            }
        });
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) registerRequestCleanup(ctx context.Context, requestID string)</c>（checkerpool.go:387-397）。
    /// 请求 context 结束时删除请求关联，避免 requestAssociations 随已完成的请求 ID 无限增长。
    /// **必须**在持有 p.mu 时调用；清理是异步的（见文件头第 8 条）。
    /// </summary>
    private void RegisterRequestCleanup(Context ctx, string requestID)
    {
        _ = ctx.CancellationToken.Register(() =>
        {
            lock (_mu)
            {
                _requestAssociations.Remove(requestID);
            }
        });
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) scheduleCleanupLocked()</c>（checkerpool.go:399-429）。重置（或启动）清理定时器，
    /// 使它在当前所有空闲未持有的 checker 中最早的到期时刻触发。**必须**在持有 p.mu 时调用；**不得**在已 discarded 的池上调用。
    /// </summary>
    private void ScheduleCleanupLocked()
    {
        DateTimeOffset? earliestDeadline = null;
        for (var i = 0; i < _checkers.Length; i++)
        {
            if (_checkers[i] is null || _heldBy[i] != "" || _lastReleased[i] is null)
            {
                continue;
            }

            var deadline = _lastReleased[i]!.Value + _opts.IdleTimeout;
            if (earliestDeadline is null || deadline < earliestDeadline)
            {
                earliestDeadline = deadline;
            }
        }

        if (earliestDeadline is null)
        {
            // 没有空闲 checker 了 —— 有定时器就停掉。
            if (_cleanupTimer is not null)
            {
                _cleanupTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _cleanupTimer = null;
            }

            return;
        }

        var delay = earliestDeadline.Value - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromMilliseconds(1);
        }

        if (_cleanupTimer is not null)
        {
            _cleanupTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _cleanupTimer = new Timer(static state => ((CheckerPool)state!).CleanupIdleCheckers(), this, delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) cleanupIdleCheckers()</c>（checkerpool.go:431-461）。销毁空闲超过超时时间的 checker。
    /// API checker 是独立的，永不空闲清理。
    /// </summary>
    internal void CleanupIdleCheckers()
    {
        lock (_mu)
        {
            // Discard() 调 Stop() 时定时器回调可能已经在途（Stop 不保证回调不跑）。直接退出且不重排，
            // 这样已丢弃的池不会通过新定时器把自己保活。
            if (_discarded)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < _checkers.Length; i++)
            {
                var c = _checkers[i];
                if (c is null || _heldBy[i] != "")
                {
                    continue;
                }

                if (_lastReleased[i] is null)
                {
                    continue;
                }

                var idle = now - _lastReleased[i]!.Value;
                if (idle >= _opts.IdleTimeout)
                {
                    _log($"checkerpool: Disposing idle checker {i} (idle {idle})");
                    DisposeCheckerLocked(i, c);
                }
            }

            // 为其余"仍空闲但尚未到期"的 checker 重排。scheduleCleanupLocked 会 Reset 已有定时器
            // 而不是新建，避免定时器泄漏。
            ScheduleCleanupLocked();
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) disposeCheckerLocked(index int, c *checker.Checker)</c>（checkerpool.go:463-482）。
    /// 从池里移除 checker 并清掉引用它的全部关联（文件与请求）。**必须**在持有 p.mu 时调用。
    /// </summary>
    private void DisposeCheckerLocked(int index, Checker c)
    {
        Debug.Assert(_checkers[index] == c);
        _checkers[index] = null;
        _heldBy[index] = "";
        _globalDiagCheckerCount[index] = 0;
        _lastReleased[index] = null;

        // Go 的 `for file, idx := range map { delete(...) }` 是安全的；C# 遍历中改 Dictionary 会抛，
        // 故先收集键再删（见文件头第 7 条）。
        List<SourceFile>? filesToRemove = null;
        foreach (var (file, idx) in _fileAssociations)
        {
            if (idx == index)
            {
                (filesToRemove ??= []).Add(file);
            }
        }

        if (filesToRemove is not null)
        {
            foreach (var file in filesToRemove)
            {
                _fileAssociations.Remove(file);
            }
        }

        List<string>? requestsToRemove = null;
        foreach (var (request, idx) in _requestAssociations)
        {
            if (idx == index)
            {
                (requestsToRemove ??= []).Add(request);
            }
        }

        if (requestsToRemove is not null)
        {
            foreach (var request in requestsToRemove)
            {
                _requestAssociations.Remove(request);
            }
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) mergeGlobalDiagnosticsFromCheckerLocked(index int, c *checker.Checker)</c>（checkerpool.go:484-497）。
    /// 若该 checker 自上次查看以来产生了新的全局诊断，则并入累计集合。**必须**在持有 p.mu 时调用。
    /// </summary>
    private void MergeGlobalDiagnosticsFromCheckerLocked(int index, Checker c)
    {
        var globals = c.GetGlobalDiagnostics();
        if (globals.Length == _globalDiagCheckerCount[index])
        {
            return;
        }

        _globalDiagCheckerCount[index] = globals.Length;
        var before = _globalDiagAccumulated.Count;
        // Go: compiler.SortAndDeduplicateDiagnostics(append(accumulated, globals...))
        _globalDiagAccumulated =
            [.. TypeScript.Compiler.Compiler.SortAndDeduplicateDiagnostics([.. _globalDiagAccumulated, .. globals])];
        if (_globalDiagAccumulated.Count != before)
        {
            _globalDiagChanged = true;
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) GetGlobalDiagnostics() []*ast.Diagnostic</c>（checkerpool.go:499-505）。
    /// 返回本池生命周期内专用诊断 checker 各实例累计的全局诊断（Go 的 `slices.Clone` ⇒ 新数组）。
    /// </summary>
    public Diagnostic[] GetGlobalDiagnostics()
    {
        lock (_mu)
        {
            return [.. _globalDiagAccumulated];
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) TakeNewGlobalDiagnostics() bool</c>（checkerpool.go:507-515）。
    /// 报告自上次调用以来是否有新的全局诊断被累计，并重置标志。
    /// </summary>
    public bool TakeNewGlobalDiagnostics()
    {
        lock (_mu)
        {
            var changed = _globalDiagChanged;
            _globalDiagChanged = false;
            return changed;
        }
    }

    /// <summary>
    /// Go: <c>func (p *checkerPool) Discard()</c>（checkerpool.go:517-531）。表示本池的 program 已被替换。
    /// 池仍然可用，但停止空闲清理定时器，使 checker 直到池被 GC 才销毁；API checker 不受影响（它从不空闲清理）。
    /// </summary>
    public void Discard()
    {
        lock (_mu)
        {
            if (_discarded)
            {
                return; // 已丢弃
            }

            _log("checkerpool: Discarding pool, stopping idle cleanup");
            _discarded = true;
            if (_cleanupTimer is not null)
            {
                _cleanupTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _cleanupTimer = null;
            }
        }
    }

    /// <summary>Go: <c>func noop() {}</c>（checkerpool.go:533）：用作"什么都不做"的 release。</summary>
    private static void Noop()
    {
    }
}
