// ported from: internal/project/background/queue_test.go（91 行）
//
// 与 Go 测试的差异（逐条）：
//
// - Go 的 4 个 `t.Run` 子测试 → 4 个 [Fact]（与 tests/…/Project/LoggingTests.cs 的移植先例一致）。
// - Go 的 `defer q.Close()` → try/finally（C# 没有 defer）；Go 的 `t.Parallel()` 不需要（xunit 默认并行跑类）。
// - `atomic.Int64` → `Interlocked.Increment(ref counter)`；`sync.Mutex` 保护的 append → `lock (executed)`。
// - `q := background.NewQueue()` → `Queue.NewQueue()`（包级构造函数改成静态工厂，见 Queue.cs 文件头 6）。
// - 额外一条 `TestQueueSkipsCancelledTask`：queue.go:28-29 的 `if ctx.Err() != nil { return }`（入队前早退）
//   在 Go 测试里没有覆盖，这里补一条确定性用例；任务体内那次 `ctx.Err()` 复检发生在"入队到线程启动之间被取消"
//   的竞态窗口里，无法确定性地触发，故不单测。
using System.Collections.Generic;
using System.Threading;
using TypeScript.Core;
using TypeScript.Project.Background;
using Xunit;

namespace TypeScript.Tests;

public class ProjectBackgroundQueueTests
{
    /// <summary>Go: <c>TestQueue/BasicEnqueue</c>（queue_test.go:16-27）。</summary>
    [Fact]
    public void TestQueueBasicEnqueue()
    {
        var queue = Queue.NewQueue();
        try
        {
            var executed = false;
            queue.Enqueue(Context.Background, _ => executed = true);

            queue.Wait();

            Assert.True(executed);
        }
        finally
        {
            queue.Close();
        }
    }

    /// <summary>Go: <c>TestQueue/MultipleTasksExecution</c>（queue_test.go:29-46）。</summary>
    [Fact]
    public void TestQueueMultipleTasksExecution()
    {
        var queue = Queue.NewQueue();
        try
        {
            var counter = 0;
            const int numTasks = 10;

            for (var i = 0; i < numTasks; i++)
            {
                queue.Enqueue(Context.Background, _ => Interlocked.Increment(ref counter));
            }

            queue.Wait();

            Assert.Equal(numTasks, counter);
        }
        finally
        {
            queue.Close();
        }
    }

    /// <summary>Go: <c>TestQueue/NestedEnqueue</c>（queue_test.go:48-73）。</summary>
    [Fact]
    public void TestQueueNestedEnqueue()
    {
        var queue = Queue.NewQueue();
        try
        {
            var executed = new List<string>();

            queue.Enqueue(Context.Background, ctx =>
            {
                lock (executed)
                {
                    executed.Add("parent");
                }

                queue.Enqueue(ctx, _ =>
                {
                    lock (executed)
                    {
                        executed.Add("child");
                    }
                });
            });

            queue.Wait();

            lock (executed)
            {
                Assert.Equal(2, executed.Count);
            }
        }
        finally
        {
            queue.Close();
        }
    }

    /// <summary>Go: <c>TestQueue/ClosedQueueRejectsNewTasks</c>（queue_test.go:75-90）。</summary>
    [Fact]
    public void TestQueueClosedQueueRejectsNewTasks()
    {
        var queue = Queue.NewQueue();
        queue.Close();

        var executed = false;
        queue.Enqueue(Context.Background, _ => executed = true);

        queue.Wait();

        Assert.False(executed, "Task should not execute after queue is closed");
    }

    /// <summary>queue.go:28-29 的 `if ctx.Err() != nil { return }`（见文件头最后一条）。</summary>
    [Fact]
    public void TestQueueSkipsCancelledTask()
    {
        var queue = Queue.NewQueue();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var cancelled = Core.Core.WithCancellation(Context.Background, cts.Token);

            var executed = false;
            queue.Enqueue(cancelled, _ => executed = true);

            queue.Wait();

            Assert.False(executed, "Task should not execute when the context is already cancelled");
        }
        finally
        {
            queue.Close();
        }
    }

    /// <summary>
    /// Queue.cs 文件头 5 的偏离：Go 里任务 panic 直接终止进程，C# 侧按本仓既有做法（Core.WorkGroup /
    /// Orchestrator）记录第一个异常并在 `Wait()` 时重抛。计数照常递减（不会让 Wait 永久挂起），
    /// 且异常会一直保留到后续的每次 Wait（不丢异常）。
    /// </summary>
    [Fact]
    public void TestQueueRethrowsTaskFailureOnWait()
    {
        var queue = Queue.NewQueue();
        try
        {
            var otherRan = false;
            queue.Enqueue(Context.Background, _ => throw new InvalidOperationException("boom"));
            queue.Enqueue(Context.Background, _ => otherRan = true);

            var error = Assert.Throws<InvalidOperationException>(queue.Wait);
            Assert.Equal("boom", error.Message);
            Assert.True(otherRan, "Wait should have waited for every task before rethrowing");

            Assert.Throws<InvalidOperationException>(queue.Wait);
        }
        finally
        {
            queue.Close();
        }
    }
}
