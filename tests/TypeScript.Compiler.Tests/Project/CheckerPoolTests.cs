// ported from: internal/project/checkerpool_test.go:1-1283
//
// 说明（与 Go 测试的差异，逐条）：
// 1) Go 的 `setupCheckerPoolSession` 用 `Session`（新建会话 → `snapshot.ProjectCollection.ConfiguredProject("/src/tsconfig.json")`
//    → `project.checkerPool`）拿到一个**真实 Program** 与池。本切片不依赖会话层（会话由 Main/ProjHub 的切片落地），
//    所以这里直接构造等价环境：`Compiler.NewCompilerHost` + `TsOptions.ParseCommandLine(["--noLib", "/src/index.ts"])`
//    + `Compiler.NewProgram`（与 Go 的 tsconfig `{"compilerOptions":{"noLib":true}}` + `src/index.ts` 等价），
//    池用 `CheckerPool.NewCheckerPool(...)` 直接建（Go 的 `newTestCheckerPool`）。每个用例自带独立的 program。
// 2) Go 用 `testing/synctest` 提供**虚拟时间**（`synctest.Sleep`/`synctest.Wait`）。C# 没有对应物 ⇒ 时间相关用例改成
//    **真时间 + 轮询到期限**：等待"清理发生"的用例用很短的 IdleTimeout（50/100ms）轮询（最长 10s）；
//    断言"还没有发生"的用例用很长的 IdleTimeout 并立即断言；`synctest.Wait()`（等 goroutine/定时器跑到静止）
//    在 C# 里等同于轮询条件。步进式用例（StaggeredIdleCleanup）用 5s 超时 + 1s 间隔，保留原用例的语义。
// 3) `assert.Assert(t, a == b)`（Go 里 checker 是**指针**）→ `Assert.Same`；`assert.Equal` → `Assert.Equal(expected, actual)`。
// 4) Go 测试直接读未导出字段（`pool.checkers`/`pool.opts`/`pool.fileAssociations`/`pool.requestAssociations`/
//    `pool.cleanupTimer`/`pool.persistentChecker`/`pool.mu`）→ C# 靠 InternalsVisibleTo 读同样的 internal 字段
//    （`_checkers`/`_opts`/`_fileAssociations`/`_requestAssociations`/`_cleanupTimer`/`_persistentChecker`），
//    `pool.mu.Lock()` 包住读的做法在 C# 里用 `lock (pool._mu)`。
// 5) Go 的 `context.WithCancel` + `core.WithRequestID`/`WithCheckerLifetime` → `CancellationTokenSource` +
//    `Core.WithCancellation(ctx, cts.Token)` + `WithRequestID`/`WithCheckerLifetime`（Core/Context.cs 的既定映射）。
//    `ctx.Done() == nil` → `!ctx.CancellationToken.CanBeCanceled`。
// 6) `c.GetDiagnostics(canceledCtx, sourceFile)` 用来把 checker 置为 canceled（Go 的 `WasCanceled` 语义），
//    C# 侧同名同参（`Checker.GetDiagnostics(Context, SourceFile)`）。
// 7) Go 的 goroutine + `atomic.Bool` 阻塞断言 → `Thread` + `ManualResetEventSlim`：先断言"仍未完成"（阻塞中的线程
//    不可能先完成，所以不会出现假失败），释放后 Join 带超时。
// 8) **using 放在 file-scoped namespace 之后**：`using Checker = TypeScript.Checker.Checker;` 只有放在
//    `namespace TypeScript.Tests;` 体内才压得过外层命名空间成员 `TypeScript.Checker`（否则 `Checker` 解析成命名空间，
//    仓内先例：Xxh3.cs / Tracing.cs 的文件头说明）。
namespace TypeScript.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Project;
using Xunit;
using static TypeScript.Bundled.Bundled;
using static TypeScript.Core.Core;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Vfs.VfsTest.VfsTest;
using Checker = TypeScript.Checker.Checker;
using ProjectCheckerPool = TypeScript.Project.CheckerPool;

public class ProjectCheckerPoolTests
{
    private const string _testFileName = "/src/index.ts";
    private const string _testContent = "export const x: number = 1;";

    /// <summary>Go: <c>setupCheckerPoolSession</c> + <c>ls.GetProgram()</c> 的等价物（见文件头第 1 条）。</summary>
    private static Program NewTestProgram()
    {
        var fs = WrapFS(FromMap(new Dictionary<string, string> { [_testFileName] = _testContent }, false));
        var host = TypeScript.Compiler.Compiler.NewCompilerHost("/", fs, LibPath(), null, null, null);
        var commandLine = ParseCommandLine(["--noLib", _testFileName], host);
        return TypeScript.Compiler.Compiler.NewProgram(new ProgramOptions
        {
            Host = host,
            Config = commandLine,
            SingleThreaded = Tristate.TSTrue,
            TypingsLocation = "/home/src/Library/Caches/typescript",
        });
    }

    /// <summary>Go: <c>newTestCheckerPool(program, opts)</c>。</summary>
    private static ProjectCheckerPool NewTestCheckerPool(Program program, int maxCheckers = 4, int idleMilliseconds = 30_000)
    {
        return ProjectCheckerPool.NewCheckerPool(
            new CheckerPoolOptions { MaxCheckers = maxCheckers, IdleTimeout = TimeSpan.FromMilliseconds(idleMilliseconds) },
            program,
            static _ => { });
    }

    private static Context RequestContext(string requestId, CheckerLifetime lifetime, CancellationToken token = default)
    {
        var ctx = WithRequestID(WithCancellation(Context.Background, token), requestId);
        return WithCheckerLifetime(ctx, lifetime);
    }

    private static Context ApiContext()
    {
        return WithCheckerLifetime(Context.Background, CheckerLifetime.API);
    }

    /// <summary>Go: <c>synctest.Wait()</c> / 等"某个条件成立"（见文件头第 2 条）。</summary>
    private static void WaitUntil(Func<bool> condition, string what, int timeoutMilliseconds = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(5);
        }

        Assert.True(condition(), $"timed out waiting for: {what}");
    }

    private static int IndexOfChecker(ProjectCheckerPool pool, Checker checker, int start = 1)
    {
        for (var i = start; i < pool._checkers.Length; i++)
        {
            if (pool._checkers[i] == checker)
            {
                return i;
            }
        }

        return -1;
    }

    // ── 路由 ──────────────────────────────────────────────────────────────────────

    /// <summary>Go: <c>TestCheckerPoolDiagnosticsRouting</c>（checkerpool_test.go:53-65）。</summary>
    [Fact]
    public void DiagnosticsRouting()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        var ctx = RequestContext("diag-req-1", CheckerLifetime.Diagnostics);

        var (checker, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(checker);
        Assert.Same(pool._checkers[0], checker);
        release();
    }

    /// <summary>Go: <c>TestCheckerPoolQueryRouting</c>（checkerpool_test.go:67-80）。</summary>
    [Fact]
    public void QueryRouting()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        var ctx = RequestContext("query-req-1", CheckerLifetime.Temporary);

        var (checker, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(checker);
        Assert.NotSame(pool._checkers[0], checker);
        release();
    }

    /// <summary>Go: <c>TestCheckerPoolRequestAffinity</c>（checkerpool_test.go:82-107）。</summary>
    [Fact]
    public void RequestAffinity()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        using var cts = new CancellationTokenSource();
        var ctx = RequestContext("req-affinity", CheckerLifetime.Temporary, cts.Token);

        var (c1, release1) = pool.GetChecker(ctx, null);

        // 同一请求 ID 在仍被持有时再取：同一个 checker（noop release）。
        var (c2, release2) = pool.GetChecker(ctx, null);
        release2();
        release1();
        Assert.Same(c1, c2);

        // 释放后同一请求仍应拿到同一个 checker（跨释放的亲和性）。
        var (c3, release3) = pool.GetChecker(ctx, null);
        release3();
        Assert.Same(c1, c3);
    }

    /// <summary>Go: <c>TestCheckerPoolIdleCleanup</c>（checkerpool_test.go:109-166；时间语义见文件头第 2 条）。</summary>
    [Fact]
    public void IdleCleanup()
    {
        var pool = NewTestCheckerPool(NewTestProgram(), idleMilliseconds: 50);

        var diagCtx = RequestContext("diag-cleanup", CheckerLifetime.Diagnostics);
        var (c1, release1) = pool.GetChecker(diagCtx, null);
        Assert.NotNull(c1);
        release1();

        var queryCtx = RequestContext("query-cleanup", CheckerLifetime.Temporary);
        var (c2, release2) = pool.GetChecker(queryCtx, null);
        Assert.NotNull(c2);
        release2();

        var queryIdx = IndexOfChecker(pool, c2);
        Assert.True(queryIdx > 0);
        Assert.NotNull(pool._checkers[0]);

        // 超过空闲超时后两者都应被销毁。
        WaitUntil(() => pool._checkers[0] is null, "diagnostics checker disposed");
        WaitUntil(() => pool._checkers[queryIdx] is null, "query checker disposed");
    }

    /// <summary>Go: <c>TestCheckerPoolFileAssociationCleanup</c>（checkerpool_test.go:168-208）。</summary>
    [Fact]
    public void FileAssociationCleanup()
    {
        var program = NewTestProgram();
        var sourceFile = program.GetSourceFile(_testFileName);
        Assert.NotNull(sourceFile);

        var pool = NewTestCheckerPool(program, idleMilliseconds: 50);
        var ctx = RequestContext("file-assoc-req", CheckerLifetime.Temporary);
        var (checker, release) = pool.GetChecker(ctx, sourceFile);
        Assert.NotNull(checker);
        release();

        Assert.True(pool._fileAssociations.ContainsKey(sourceFile));

        WaitUntil(() => !pool._fileAssociations.ContainsKey(sourceFile), "file association cleared");
    }

    /// <summary>Go: <c>TestCheckerPoolMinCheckers</c>（checkerpool_test.go:210-218）。</summary>
    [Fact]
    public void MinCheckersIsClampedToTwo()
    {
        var options = new CheckerPoolOptions { MaxCheckers = 1, IdleTimeout = TimeSpan.FromSeconds(10) };
        var pool = NewTestCheckerPool(NewTestProgram(), maxCheckers: options.MaxCheckers);
        Assert.Equal(2, pool._opts.MaxCheckers);
        Assert.Equal(2, pool._checkers.Length);
    }

    /// <summary>Go: <c>TestCheckerPoolDefaultIdleTimeout</c>（checkerpool_test.go:220-226）。</summary>
    [Fact]
    public void DefaultIdleTimeoutIsThirtySeconds()
    {
        var pool = ProjectCheckerPool.NewCheckerPool(
            new CheckerPoolOptions { MaxCheckers = 4 },
            NewTestProgram(),
            static _ => { });
        Assert.Equal(TimeSpan.FromSeconds(30), pool._opts.IdleTimeout);
    }

    /// <summary>Go: <c>TestCheckerPoolDefaultMaxCheckers</c>（checkerpool_test.go:1073-1081）。</summary>
    [Fact]
    public void DefaultMaxCheckersIsFour()
    {
        var pool = ProjectCheckerPool.NewCheckerPool(
            new CheckerPoolOptions { MaxCheckers = 0, IdleTimeout = TimeSpan.FromSeconds(10) },
            NewTestProgram(),
            static _ => { });
        Assert.Equal(4, pool._opts.MaxCheckers);
        Assert.Equal(4, pool._checkers.Length);
        Assert.Equal(3, pool._querySem.CurrentCount); // Go: cap(pool.querySem) == MaxCheckers-1
    }

    // ── 争用 ──────────────────────────────────────────────────────────────────────

    /// <summary>Go: <c>TestCheckerPoolQueryContention</c>（checkerpool_test.go:228-271）。</summary>
    [Fact]
    public void QueryContention()
    {
        var pool = NewTestCheckerPool(NewTestProgram(), maxCheckers: 2);

        var ctx1 = RequestContext("query-hold", CheckerLifetime.Temporary);
        var (c1, release1) = pool.GetChecker(ctx1, null);
        Assert.NotNull(c1);

        var ctx2 = RequestContext("query-wait", CheckerLifetime.Temporary);
        var c2 = (Checker?)null;
        var release2 = (Action?)null;
        var got = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            (c2, release2) = pool.GetChecker(ctx2, null);
            got.Set();
        });
        thread.Start();

        // 只有 1 个查询槽：第二个请求应被阻塞（Go 的 synctest.Wait + !c2Got）。
        Assert.False(got.Wait(TimeSpan.FromMilliseconds(200)), "second query should be blocked while first holds the checker");

        release1();
        Assert.True(got.Wait(TimeSpan.FromSeconds(10)), "second query should have acquired the checker after release");
        thread.Join();
        Assert.NotNull(c2);
        release2!();
    }

    /// <summary>Go: <c>TestCheckerPoolDiagnosticsContention</c>（checkerpool_test.go:273-320）。</summary>
    [Fact]
    public void DiagnosticsContention()
    {
        var pool = NewTestCheckerPool(NewTestProgram(), maxCheckers: 2);

        var ctx1 = RequestContext("diag-hold", CheckerLifetime.Diagnostics);
        var (c1, release1) = pool.GetChecker(ctx1, null);
        Assert.NotNull(c1);

        var ctx2 = RequestContext("diag-wait", CheckerLifetime.Diagnostics);
        var got = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var (_, release2) = pool.GetChecker(ctx2, null);
            got.Set();
            release2();
        });
        thread.Start();
        Assert.False(got.Wait(TimeSpan.FromMilliseconds(200)), "second diagnostics request should be blocked");

        // 查询请求**不应**被阻塞（不同的槽）。
        var ctx3 = RequestContext("query-concurrent", CheckerLifetime.Temporary);
        var (c3, release3) = pool.GetChecker(ctx3, null);
        Assert.NotNull(c3);
        Assert.NotSame(c3, c1);
        release3();

        release1();
        Assert.True(got.Wait(TimeSpan.FromSeconds(10)), "second diagnostics request should have acquired the checker after release");
        thread.Join();
    }

    /// <summary>Go: <c>TestCheckerPoolMultipleConcurrentQueryCheckers</c>（checkerpool_test.go:910-966）。</summary>
    [Fact]
    public void MultipleConcurrentQueryCheckers()
    {
        var pool = NewTestCheckerPool(NewTestProgram(), maxCheckers: 4);

        var (c1, release1) = pool.GetChecker(RequestContext("multi-q-1", CheckerLifetime.Temporary), null);
        var (c2, release2) = pool.GetChecker(RequestContext("multi-q-2", CheckerLifetime.Temporary), null);
        var (c3, release3) = pool.GetChecker(RequestContext("multi-q-3", CheckerLifetime.Temporary), null);
        Assert.NotNull(c1);
        Assert.NotNull(c2);
        Assert.NotNull(c3);
        Assert.NotSame(c1, c2);
        Assert.NotSame(c1, c3);
        Assert.NotSame(c2, c3);
        Assert.NotSame(pool._checkers[0], c1);
        Assert.NotSame(pool._checkers[0], c2);
        Assert.NotSame(pool._checkers[0], c3);

        // 第 4 个查询请求会阻塞（3 个槽都被占用）。
        var got = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var (_, release4) = pool.GetChecker(RequestContext("multi-q-4", CheckerLifetime.Temporary), null);
            got.Set();
            release4();
        });
        thread.Start();
        Assert.False(got.Wait(TimeSpan.FromMilliseconds(200)), "4th query should block when all 3 query slots are held");

        release1();
        Assert.True(got.Wait(TimeSpan.FromSeconds(10)), "4th query should unblock after one slot is released");
        thread.Join();
        release2();
        release3();
    }

    /// <summary>Go: <c>TestCheckerPoolCrossReleaseAffinityWithContention</c>（checkerpool_test.go:453-500）。</summary>
    [Fact]
    public void CrossReleaseAffinityWithContention()
    {
        var pool = NewTestCheckerPool(NewTestProgram(), maxCheckers: 2);
        using var cts = new CancellationTokenSource();

        var ctxA = RequestContext("req-A", CheckerLifetime.Temporary, cts.Token);
        var (cA, releaseA) = pool.GetChecker(ctxA, null);
        Assert.NotNull(cA);
        releaseA();

        // 请求 B 在 A 释放期间拿走唯一的查询槽。
        var ctxB = RequestContext("req-B", CheckerLifetime.Temporary);
        var (cB, releaseB) = pool.GetChecker(ctxB, null);
        Assert.NotNull(cB);

        // 请求 A 重新获取 —— B 持有槽时应当阻塞。
        var cA2 = (Checker?)null;
        var got = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var (checker, release) = pool.GetChecker(ctxA, null);
            cA2 = checker;
            got.Set();
            release();
        });
        thread.Start();
        Assert.False(got.Wait(TimeSpan.FromMilliseconds(200)), "request A should block while B holds the slot");

        releaseB();
        Assert.True(got.Wait(TimeSpan.FromSeconds(10)), "request A should unblock after B releases");
        thread.Join();
        Assert.Same(cA, cA2);
    }

    // ── 取消与关联清理 ─────────────────────────────────────────────────────────────

    /// <summary>Go: <c>TestCheckerPoolCanceledCheckerDisposal</c>（checkerpool_test.go:304-341）。</summary>
    [Fact]
    public void CanceledCheckerDisposal()
    {
        var program = NewTestProgram();
        var sourceFile = program.GetSourceFile(_testFileName)!;
        var pool = NewTestCheckerPool(program);

        var ctx = RequestContext("cancel-test", CheckerLifetime.Temporary);
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        c.GetDiagnostics(WithCancellation(Context.Background, cts.Token), sourceFile);
        Assert.True(c.WasCanceled());

        // 释放已取消的 checker 会销毁它。
        release();

        // 下一个请求应拿到新的 checker。
        var ctx2 = RequestContext("after-cancel", CheckerLifetime.Temporary);
        var (c2, release2) = pool.GetChecker(ctx2, null);
        Assert.NotSame(c, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolRequestAssociationCleanupOnDisposal</c>（checkerpool_test.go:343-380）。</summary>
    [Fact]
    public void RequestAssociationCleanupOnDisposal()
    {
        var program = NewTestProgram();
        var sourceFile = program.GetSourceFile(_testFileName)!;
        var pool = NewTestCheckerPool(program);

        using var requestCts = new CancellationTokenSource();
        var ctx = RequestContext("assoc-cleanup-req", CheckerLifetime.Temporary, requestCts.Token);
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        c.GetDiagnostics(WithCancellation(Context.Background, cts.Token), sourceFile);
        Assert.True(c.WasCanceled());

        release();

        Assert.False(pool._requestAssociations.ContainsKey("assoc-cleanup-req"));
    }

    /// <summary>Go: <c>TestCheckerPoolRequestAssociationCleanupOnContextDone</c>（checkerpool_test.go:382-420）。</summary>
    [Fact]
    public void RequestAssociationCleanupOnContextDone()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        using var requestCts = new CancellationTokenSource();
        var ctx = RequestContext("ctx-cleanup-req", CheckerLifetime.Temporary, requestCts.Token);

        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);
        release();

        // 释放后关联仍在。
        Assert.True(pool._requestAssociations.ContainsKey("ctx-cleanup-req"));

        // 取消请求 context → 关联被清理。
        requestCts.Cancel();
        WaitUntil(() => !pool._requestAssociations.ContainsKey("ctx-cleanup-req"), "request association cleaned up on cancel");
    }

    /// <summary>Go: <c>TestCheckerPoolNonCancelableContextNoAffinity</c>（checkerpool_test.go:1196-1226）。</summary>
    [Fact]
    public void NonCancelableContextRegistersNoAffinity()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        // 带 request ID 但永远不可取消的 context（ctx.Done() == nil）不得登记请求关联。
        var ctx = WithCheckerLifetime(WithRequestID(Context.Background, "uncancelable-req"), CheckerLifetime.Temporary);
        Assert.False(ctx.CancellationToken.CanBeCanceled);

        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);
        release();

        Assert.Empty(pool._requestAssociations);
    }

    /// <summary>Go: <c>TestCheckerPoolAPICheckerDisposedOnCancel</c>（checkerpool_test.go:1151-1194）。</summary>
    [Fact]
    public void ApiCheckerDisposedOnCancel()
    {
        var program = NewTestProgram();
        var sourceFile = program.GetSourceFile(_testFileName)!;
        var pool = NewTestCheckerPool(program);

        var ctx = ApiContext();
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        c.GetDiagnostics(WithCancellation(Context.Background, cts.Token), sourceFile);
        Assert.True(c.WasCanceled());

        release();

        Assert.Null(pool._persistentChecker);

        // 下一次 API 获取拿到全新的可用 checker，而不是 panic。
        var (c2, release2) = pool.GetChecker(ctx, null);
        Assert.NotSame(c, c2);
        release2();
    }

    // ── 空闲重建 / 稳定性 ──────────────────────────────────────────────────────────

    /// <summary>Go: <c>TestCheckerPoolDiagnosticsRecreatedAfterIdleDisposal</c>（checkerpool_test.go:422-451）。</summary>
    [Fact]
    public void DiagnosticsRecreatedAfterIdleDisposal()
    {
        var pool = NewTestCheckerPool(NewTestProgram(), idleMilliseconds: 50);

        var (c1, release1) = pool.GetChecker(RequestContext("diag-recreate-1", CheckerLifetime.Diagnostics), null);
        Assert.NotNull(c1);
        release1();

        WaitUntil(() => pool._checkers[0] is null, "diagnostics checker disposed");

        var (c2, release2) = pool.GetChecker(RequestContext("diag-recreate-2", CheckerLifetime.Diagnostics), null);
        Assert.NotNull(c2);
        Assert.NotSame(c1, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolDiagnosticsCheckerStableIdentity</c>（checkerpool_test.go:730-758）。</summary>
    [Fact]
    public void DiagnosticsCheckerStableIdentity()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var (c1, release1) = pool.GetChecker(RequestContext("diag-stable-1", CheckerLifetime.Diagnostics), null);
        Assert.NotNull(c1);
        release1();

        var (c2, release2) = pool.GetChecker(RequestContext("diag-stable-2", CheckerLifetime.Diagnostics), null);
        Assert.Same(c1, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolDiagnosticsCrossReleaseAffinity</c>（checkerpool_test.go:579-605）。</summary>
    [Fact]
    public void DiagnosticsCrossReleaseAffinity()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        using var cts = new CancellationTokenSource();
        var ctx = RequestContext("diag-affinity", CheckerLifetime.Diagnostics, cts.Token);

        var (c1, release1) = pool.GetChecker(ctx, null);
        Assert.NotNull(c1);
        Assert.Same(pool._checkers[0], c1);
        release1();

        var (c2, release2) = pool.GetChecker(ctx, null);
        Assert.Same(c1, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolLifetimeMismatchIgnoresAssociation</c>（checkerpool_test.go:502-545）。</summary>
    [Fact]
    public void LifetimeMismatchIgnoresAssociation()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        using var cts = new CancellationTokenSource();

        // 先用诊断生命周期、同一 request ID 取诊断 checker。
        var diagCtx = RequestContext("mixed", CheckerLifetime.Diagnostics, cts.Token);
        var (diagChecker, releaseDiag) = pool.GetChecker(diagCtx, null);
        Assert.NotNull(diagChecker);
        Assert.Same(pool._checkers[0], diagChecker);
        releaseDiag();

        // 再用同一 request ID 但查询用途：旧关联指向下标 0，必须被拒绝。
        var queryCtx = RequestContext("mixed", CheckerLifetime.Temporary, cts.Token);
        var (queryChecker, releaseQuery) = pool.GetChecker(queryCtx, null);
        Assert.NotNull(queryChecker);
        Assert.NotSame(diagChecker, queryChecker);
        Assert.NotSame(pool._checkers[0], queryChecker);
        releaseQuery();
    }

    /// <summary>Go: <c>TestCheckerPoolFileAffinity</c>（checkerpool_test.go:880-908）。</summary>
    [Fact]
    public void FileAffinity()
    {
        var program = NewTestProgram();
        var sourceFile = program.GetSourceFile(_testFileName)!;
        var pool = NewTestCheckerPool(program);

        var (c1, release1) = pool.GetChecker(RequestContext("file-aff-1", CheckerLifetime.Temporary), sourceFile);
        Assert.NotNull(c1);
        release1();

        var (c2, release2) = pool.GetChecker(RequestContext("file-aff-2", CheckerLifetime.Temporary), sourceFile);
        Assert.NotNull(c2);
        Assert.Same(c1, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolNoRequestID</c>（checkerpool_test.go:547-563）。</summary>
    [Fact]
    public void NoRequestIdStillWorks()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var (c1, release1) = pool.GetChecker(Context.Background, null);
        Assert.NotNull(c1);
        release1();

        var (c2, release2) = pool.GetChecker(Context.Background, null);
        Assert.NotNull(c2);
        release2();

        // 没有请求 ID 就没有亲和性保证 —— 只验证不崩。
    }

    /// <summary>Go: <c>TestCheckerPoolDoubleReleaseSafe</c>（checkerpool_test.go:1040-1059）。</summary>
    [Fact]
    public void DoubleReleaseIsSafe()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var ctx = RequestContext("double-release", CheckerLifetime.Temporary);
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);

        release();
        release(); // sync.OnceFunc ⇒ 第二次是空操作。

        var ctx2 = RequestContext("after-double", CheckerLifetime.Temporary);
        var (c2, release2) = pool.GetChecker(ctx2, null);
        Assert.NotNull(c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolStaggeredIdleCleanup</c>（checkerpool_test.go:1083-1149；时间语义见文件头第 2 条）。</summary>
    [Fact]
    public void StaggeredIdleCleanup()
    {
        var pool = NewTestCheckerPool(NewTestProgram(), idleMilliseconds: 5_000);

        var (cA, releaseA) = pool.GetChecker(RequestContext("stagger-A", CheckerLifetime.Temporary), null);
        Assert.NotNull(cA);
        var (cB, releaseB) = pool.GetChecker(RequestContext("stagger-B", CheckerLifetime.Temporary), null);
        Assert.NotNull(cB);
        Assert.NotSame(cA, cB);
        var idxA = IndexOfChecker(pool, cA);
        var idxB = IndexOfChecker(pool, cB);
        Assert.True(idxA > 0);
        Assert.True(idxB > 0);

        // A 先释放（定时器设为 t=5s）。
        releaseA();
        Thread.Sleep(1_000);
        // 1 秒后释放 B（定时器重置为 t=6s）。
        releaseB();

        // 定时器未触发前两者都在（A 空闲 1s < 5s）。
        Assert.NotNull(pool._checkers[idxA]);
        Assert.NotNull(pool._checkers[idxB]);

        // 超过两个截止时刻后，两者都应被销毁（A 空闲 ≥ 6s、B 空闲 ≥ 5s）。
        WaitUntil(() => pool._checkers[idxA] is null && pool._checkers[idxB] is null, "both checkers disposed", 20_000);
    }

    // ── Discard ───────────────────────────────────────────────────────────────────

    /// <summary>Go: <c>TestCheckerPoolDiscardKeepsIdleCheckers</c>（checkerpool_test.go:607-660）。</summary>
    [Fact]
    public void DiscardKeepsIdleCheckers()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var (c1, release1) = pool.GetChecker(RequestContext("obs-diag", CheckerLifetime.Diagnostics), null);
        Assert.NotNull(c1);
        release1();

        var (c2, release2) = pool.GetChecker(RequestContext("obs-query", CheckerLifetime.Temporary), null);
        Assert.NotNull(c2);
        release2();

        Assert.Same(c1, pool._checkers[0]);

        pool.Discard();

        Assert.Same(c1, pool._checkers[0]);
        Assert.True(IndexOfChecker(pool, c2) > 0, "query checker should survive Discard");
        Assert.Null(pool._cleanupTimer);

        // 丢弃后即使等很久也不销毁（没有定时器在跑）—— 长空闲超时下立即断言。
        Thread.Sleep(200);
        Assert.Same(c1, pool._checkers[0]);
    }

    /// <summary>Go: <c>TestCheckerPoolDiscardHeldCheckerSurvivesRelease</c>（checkerpool_test.go:662-706）。</summary>
    [Fact]
    public void DiscardHeldCheckerSurvivesRelease()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var ctx = RequestContext("held-obs", CheckerLifetime.Temporary);
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);
        var heldIndex = IndexOfChecker(pool, c);
        Assert.True(heldIndex > 0);

        pool.Discard();
        Assert.Same(c, pool._checkers[heldIndex]);

        release();
        Assert.Same(c, pool._checkers[heldIndex]);

        Thread.Sleep(200);
        Assert.Same(c, pool._checkers[heldIndex]);
    }

    /// <summary>Go: <c>TestCheckerPoolDiscardStillFunctional</c>（checkerpool_test.go:708-748）。</summary>
    [Fact]
    public void DiscardStillFunctional()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        pool.Discard();

        var ctx = RequestContext("post-obs", CheckerLifetime.Temporary);
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);
        var idx = IndexOfChecker(pool, c);
        Assert.True(idx > 0);

        release();
        Assert.Same(c, pool._checkers[idx]);

        var ctx2 = RequestContext("post-obs-2", CheckerLifetime.Temporary);
        var (c2, release2) = pool.GetChecker(ctx2, null);
        Assert.Same(c, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolDiscardIdempotent</c>（checkerpool_test.go:1003-1038）。</summary>
    [Fact]
    public void DiscardIsIdempotent()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var ctx = RequestContext("idem-q", CheckerLifetime.Temporary);
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);
        release();

        pool.Discard();
        Assert.Contains(pool._checkers, checker => checker is not null);

        // 第二次 Discard 是空操作（不 panic、不破坏状态）。
        pool.Discard();

        var (c2, release2) = pool.GetChecker(RequestContext("post-idem", CheckerLifetime.Temporary), null);
        Assert.NotNull(c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolCleanupAfterDiscardIsNoop</c>（checkerpool_test.go:1228-1264）。</summary>
    [Fact]
    public void CleanupAfterDiscardIsNoop()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var ctx = RequestContext("discard-cleanup", CheckerLifetime.Temporary);
        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);
        release();

        pool.Discard();

        // 模拟 Stop() 之后定时器回调仍然跑到（Stop 不保证回调不跑）：必须是空操作，且不得重新排定时器。
        pool.CleanupIdleCheckers();

        Assert.Null(pool._cleanupTimer);
        Assert.Contains(pool._checkers, checker => checker is not null);
    }

    /// <summary>Go: <c>TestCheckerPoolDiagnosticsCheckerSurvivesDiscard</c>（checkerpool_test.go:760-795）。</summary>
    [Fact]
    public void DiagnosticsCheckerSurvivesDiscard()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var (c, release) = pool.GetChecker(RequestContext("diag-discard", CheckerLifetime.Diagnostics), null);
        Assert.NotNull(c);
        release();

        pool.Discard();
        Assert.Same(c, pool._checkers[0]);

        var (c2, release2) = pool.GetChecker(RequestContext("diag-discard-2", CheckerLifetime.Diagnostics), null);
        Assert.Same(c, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolDiagnosticsCheckerIndependentFromQuery</c>（checkerpool_test.go:797-827）。</summary>
    [Fact]
    public void DiagnosticsCheckerIndependentFromQuery()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var (diagChecker, diagRelease) = pool.GetChecker(RequestContext("diag-indep", CheckerLifetime.Diagnostics), null);
        var (queryChecker, queryRelease) = pool.GetChecker(RequestContext("query-indep", CheckerLifetime.Temporary), null);
        Assert.NotSame(diagChecker, queryChecker);
        diagRelease();
        queryRelease();
    }

    // ── API checker ───────────────────────────────────────────────────────────────

    /// <summary>Go: <c>TestCheckerPoolAPICheckerStableIdentity</c>（checkerpool_test.go:829-856）。</summary>
    [Fact]
    public void ApiCheckerStableIdentity()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        var ctx = ApiContext();

        var (c1, release1) = pool.GetChecker(ctx, null);
        Assert.NotNull(c1);
        release1();

        var (c2, release2) = pool.GetChecker(ctx, null);
        Assert.Same(c1, c2);
        release2();

        // API checker 不参与空闲清理（长空闲超时下立即复查身份）。
        Assert.Same(c1, pool._persistentChecker);
        var (c3, release3) = pool.GetChecker(ctx, null);
        Assert.Same(c1, c3);
        release3();
    }

    /// <summary>Go: <c>TestCheckerPoolAPICheckerSurvivesDiscard</c>（checkerpool_test.go:858-878）。</summary>
    [Fact]
    public void ApiCheckerSurvivesDiscard()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        var ctx = ApiContext();

        var (c, release) = pool.GetChecker(ctx, null);
        Assert.NotNull(c);
        release();

        pool.Discard();
        Assert.Same(c, pool._persistentChecker);

        var (c2, release2) = pool.GetChecker(ctx, null);
        Assert.Same(c, c2);
        release2();
    }

    /// <summary>Go: <c>TestCheckerPoolAllThreeIndependent</c>（checkerpool_test.go:880 test 前的 930? 见 checkerpool_test.go:930-956 的 AllThreeIndependent）。</summary>
    [Fact]
    public void AllThreeCategoriesAreIndependent()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        var (diagChecker, diagRelease) = pool.GetChecker(RequestContext("ded-req", CheckerLifetime.Diagnostics), null);
        var (tempChecker, tempRelease) = pool.GetChecker(RequestContext("tmp-req", CheckerLifetime.Temporary), null);
        var (apiChecker, apiRelease) = pool.GetChecker(ApiContext(), null);

        Assert.NotSame(diagChecker, tempChecker);
        Assert.NotSame(diagChecker, apiChecker);
        Assert.NotSame(tempChecker, apiChecker);

        diagRelease();
        tempRelease();
        apiRelease();
    }

    // ── 全局诊断 ───────────────────────────────────────────────────────────────────

    /// <summary>Go: <c>TestCheckerPoolGetGlobalDiagnosticsEmpty</c>（checkerpool_test.go:964-973）。</summary>
    [Fact]
    public void GetGlobalDiagnosticsEmptyInitially()
    {
        var pool = NewTestCheckerPool(NewTestProgram());
        Assert.Empty(pool.GetGlobalDiagnostics());
    }

    /// <summary>Go: <c>TestCheckerPoolTakeNewGlobalDiagnostics</c>（checkerpool_test.go:975-1001）。</summary>
    [Fact]
    public void TakeNewGlobalDiagnostics()
    {
        var pool = NewTestCheckerPool(NewTestProgram());

        Assert.False(pool.TakeNewGlobalDiagnostics());

        var ctx = RequestContext("global-diag-req", CheckerLifetime.Diagnostics);
        var sourceFile = pool._program.GetSourceFile(_testFileName);
        Assert.NotNull(sourceFile);
        var (c, release) = pool.GetChecker(ctx, sourceFile);
        Assert.NotNull(c);
        c.GetDiagnostics(ctx, sourceFile);
        release();

        // 诊断 checker 应发布缺失 lib 的全局诊断（program 是 noLib）。
        Assert.True(pool.TakeNewGlobalDiagnostics());
        // 取走之后标志被重置。
        Assert.False(pool.TakeNewGlobalDiagnostics());

        // 同一个 checker 状态未变时再次释放不应再置标志。
        var ctx2 = RequestContext("global-diag-req-2", CheckerLifetime.Diagnostics);
        var (c2, release2) = pool.GetChecker(ctx2, sourceFile);
        Assert.NotNull(c2);
        c2.GetDiagnostics(ctx2, sourceFile);
        release2();

        Assert.False(pool.TakeNewGlobalDiagnostics());
    }
}
