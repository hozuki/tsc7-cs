// ported from: internal/core/bfs.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 并行：Go 的 `sync.WaitGroup` + "每个 job 一个 goroutine" → C# 的 `Task.Run` + `Task.WaitAll`
//    （每个 job 一个 Task，一一对应；WaitGroup 的 Add/Done 由"Task 数组 + WaitAll"承担）。
//    不用 `Parallel.For`（它的分片调度与异常语义和 Go 不同，见任务约束）。层与层之间仍然是串行的
//    （Go 也是：processLevel 内部并行、层间顺序执行）。
//
// 2) `atomic.Int64` → `long` + `Volatile`/`Interlocked`；Go 的 `updateMin(a *atomic.Int64, candidate int64)`
//    的指针接收改成 `ref long`（C# 里"可被多个 Task 共享的原子变量"只有闭包提升的局部变量或字段两种，
//    Go 侧三个原子变量是 processLevel 的局部变量，这里同样用（被 lambda 捕获而提升的）局部变量）。
//
// 3) `*breadthFirstSearchJob[N]` → `BreadthFirstSearchJob<N>`（C# 引用类型即 Go 的指针）；
//    map 的类型 `collections.OrderedMap[K, *job]` → `OrderedMap<K, BreadthFirstSearchJob<N>>`。
//
// 4) `visit func(node N) (isResult bool, stop bool)` → `Func<N, (bool IsResult, bool Stop)>`（元组对应
//    Go 的多返回值）；`neighbors func(N) []N` → `Func<N, N[]>`；`getKey func(N) K` → `Func<N, K>`。
//
// 5) `N comparable` / `K comparable` → `where N : notnull` / `where K : notnull`（OrderedMap/SyncSet 的
//    哈希容器要求键非 null，同 Collections 的文件头）。
//
// 6) `make([][]*job, n)`（槽位可能是 nil 切片）→ `BreadthFirstSearchJob<N>[]?[]`（槽位可能是 null 数组）；
//    Go 里 nil 切片 `for range` 迭代 0 次，C# 需要显式 null 判断。
//
// 7) Go 的 `var path []N` 是 **nil 切片**（只有 append 过才非 nil），`BreadthFirstSearchResult.Path` 因此
//    可能是 nil（bfs_test.go 就断言了 `result.Path == nil`）→ C# 用 `N[]?`，`CreatePath(nil)` 显式返回
//    null（函数里加了这一条早返回）。
//
// 8) 异常语义：Go 里 job goroutine 内的 panic 直接终止进程；C# 里 Task 内抛出的异常由 `Task.WaitAll`
//    在层栅栏处包成 AggregateException 抛出（"层结束时冒泡"而不是"当场崩进程"）。本文件不做进一步包装：
//    调用点若要拿到 DebugFailureException 原样冒泡，需自行捕获 AggregateException 取 InnerException。
using TypeScript.Collections;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;

namespace TypeScript.Core;

public struct BreadthFirstSearchResult<N>
{
    public bool Stopped;
    public N[]? Path;
}

internal sealed class BreadthFirstSearchJob<N>
{
    internal readonly N node;
    internal readonly BreadthFirstSearchJob<N>? parent;

    internal BreadthFirstSearchJob(N node, BreadthFirstSearchJob<N>? parent)
    {
        this.node = node;
        this.parent = parent;
    }
}

public sealed class BreadthFirstSearchLevel<K, N> where K : notnull
{
    // Go: `jobs *collections.OrderedMap[K, *breadthFirstSearchJob[N]]`
    internal readonly OrderedMap<K, BreadthFirstSearchJob<N>> jobs;

    internal BreadthFirstSearchLevel(OrderedMap<K, BreadthFirstSearchJob<N>> jobs)
    {
        this.jobs = jobs;
    }

    public bool Has(K key)
    {
        return jobs.Has(key);
    }

    public void Delete(K key)
    {
        jobs.Delete(key);
    }

    public void Range(Func<N, bool> f)
    {
        foreach (var job in jobs.Values())
        {
            // OrderedMap.Values() 的元素类型是 V?（Go 零值语义的注解）；本文件构造的 jobs 里每个值都是
            // `new BreadthFirstSearchJob<N>(...)`（BreadthFirstSearchLevel 的构造器是 internal，只有本文件用），
            // 永不为 null，故这里的非空断言在同一文件内是可证的。
            if (!f(job!.node))
            {
                return;
            }
        }
    }
}

public struct BreadthFirstSearchOptions<K, N> where K : notnull
{
    // Visited is a set of nodes that have already been visited.
    // If nil, a new set will be created.
    public SyncSet<K>? Visited;

    // PreprocessLevel is a function that, if provided, will be called
    // before each level, giving the caller an opportunity to remove nodes.
    public Action<BreadthFirstSearchLevel<K, N>>? PreprocessLevel;
}

public static partial class Core
{
    /// <summary>
    /// BreadthFirstSearchParallel performs a breadth-first search on a graph
    /// starting from the given node. It processes nodes in parallel and returns the path
    /// from the first node that satisfies the `visit` function back to the start node.
    /// </summary>
    public static BreadthFirstSearchResult<N> BreadthFirstSearchParallel<N>(
        N start,
        Func<N, N[]> neighbors,
        Func<N, (bool IsResult, bool Stop)> visit) where N : notnull
    {
        return BreadthFirstSearchParallelEx(start, neighbors, visit, default, Identity);
    }

    /// <summary>
    /// BreadthFirstSearchParallelEx is an extension of BreadthFirstSearchParallel that allows
    /// the caller to pass a pre-seeded set of already-visited nodes and a preprocessing function
    /// that can be used to remove nodes from each level before parallel processing.
    /// </summary>
    public static BreadthFirstSearchResult<N> BreadthFirstSearchParallelEx<K, N>(
        N start,
        Func<N, N[]> neighbors,
        Func<N, (bool IsResult, bool Stop)> visit,
        BreadthFirstSearchOptions<K, N> options,
        Func<N, K> getKey) where K : notnull
    {
        var visited = options.Visited;
        if (visited is null)
        {
            visited = new SyncSet<K>();
        }

        // Go 的局部类型 `type result struct {...}`：C# 没有局部类型，下面用元组表达同名字段
        // （Stop/Job/Next 与 Go 的 stop/job/next 一一对应）。
        BreadthFirstSearchJob<N>? fallback = null;

        // processLevel processes each node at the current level in parallel.
        // It produces either a list of jobs to be processed in the next level,
        // or a result if the visit function returns true for any node.
        // （Go 的 `index` 参数在函数体内未被使用，这里保持一致，仅为逐行对应。）
        (bool Stop, BreadthFirstSearchJob<N>? Job, OrderedMap<K, BreadthFirstSearchJob<N>>? Next) ProcessLevel(
            int index,
            OrderedMap<K, BreadthFirstSearchJob<N>> jobs)
        {
            var lowestFallback = 0L;
            var lowestGoal = 0L;
            var nextJobCount = 0L;
            Volatile.Write(ref lowestGoal, long.MaxValue);
            Volatile.Write(ref lowestFallback, long.MaxValue);
            if (options.PreprocessLevel is not null)
            {
                options.PreprocessLevel(new BreadthFirstSearchLevel<K, N>(jobs));
            }

            // Go: `next := make([][]*breadthFirstSearchJob[N], jobs.Size())`（槽位可为 nil 切片）
            BreadthFirstSearchJob<N>[]?[] next = new BreadthFirstSearchJob<N>[jobs.Size()][];
            var tasks = new Task[jobs.Size()];
            var i = 0;
            foreach (var job in jobs.Values())
            {
                // 同 Range：本文件构造的 jobs 中每个值都非 null。
                var j = job!;
                var jobIndex = i;
                tasks[i] = Task.Run(() =>
                {
                    if (jobIndex >= Volatile.Read(ref lowestGoal))
                    {
                        return; // Stop processing if we already found a lower result
                    }

                    // If we have already visited this node, skip it.
                    if (!visited.AddIfAbsent(getKey(j.node)))
                    {
                        // Note that if we are here, we already visited this node at a
                        // previous *level*, which means `visit` must have returned false,
                        // so we don't need to update our result indices. This holds true
                        // because we deduplicated jobs before queuing the level.
                        return;
                    }

                    var (isResult, stop) = visit(j.node);
                    if (isResult)
                    {
                        // We found a result, so we will stop at this level, but an
                        // earlier job may still find a true result at a lower index.
                        if (stop)
                        {
                            UpdateMin(ref lowestGoal, jobIndex);
                            return;
                        }

                        if (fallback is null)
                        {
                            UpdateMin(ref lowestFallback, jobIndex);
                        }
                    }

                    if (jobIndex >= Volatile.Read(ref lowestGoal))
                    {
                        // If `visit` is expensive, it's likely that by the time we get here,
                        // a different job has already found a lower index result, so we
                        // don't even need to collect the next jobs.
                        return;
                    }

                    // Add the next level jobs
                    var neighborNodes = neighbors(j.node);
                    if (neighborNodes.Length > 0)
                    {
                        Interlocked.Add(ref nextJobCount, neighborNodes.Length);
                        next[jobIndex] = Map(neighborNodes, child => new BreadthFirstSearchJob<N>(child, j));
                    }
                });
                i++;
            }

            Task.WaitAll(tasks);

            if (Volatile.Read(ref lowestGoal) != long.MaxValue)
            {
                // If we found a result, return it immediately.
                var (_, job, _) = jobs.EntryAt((int)Volatile.Read(ref lowestGoal));
                return (true, job, null);
            }

            if (fallback is null)
            {
                var lowestFallbackIndex = Volatile.Read(ref lowestFallback);
                if (lowestFallbackIndex != long.MaxValue)
                {
                    var (_, job, _) = jobs.EntryAt((int)lowestFallbackIndex);
                    fallback = job;
                }
            }

            var nextJobs = NewOrderedMapWithSizeHint<K, BreadthFirstSearchJob<N>>((int)Volatile.Read(ref nextJobCount));
            foreach (var jobsAtSlot in next)
            {
                // Go 的 nil 切片 range 迭代 0 次
                if (jobsAtSlot is null)
                {
                    continue;
                }

                foreach (var j in jobsAtSlot)
                {
                    if (!nextJobs.Has(getKey(j.node)))
                    {
                        // Deduplicate synchronously to avoid messy locks and spawning
                        // unnecessary goroutines.
                        nextJobs.Set(getKey(j.node), j);
                    }
                }
            }

            return (false, null, nextJobs);
        }

        // Go 的 `var path []N` 是 nil 切片（只在 append 后非 nil）→ 这里 job 为 null 时返回 null。
        static N[]? CreatePath(BreadthFirstSearchJob<N>? job)
        {
            if (job is null)
            {
                return null;
            }

            var path = new List<N>();
            while (job is not null)
            {
                path.Add(job.node);
                job = job.parent;
            }

            return path.ToArray();
        }

        var levelIndex = 0;
        var level = NewOrderedMapFromList(new MapEntry<K, BreadthFirstSearchJob<N>>[]
        {
            new(getKey(start), new BreadthFirstSearchJob<N>(start, null)),
        });
        while (level.Size() > 0)
        {
            var result = ProcessLevel(levelIndex, level);
            if (result.Stop)
            {
                return new BreadthFirstSearchResult<N> { Stopped = true, Path = CreatePath(result.Job) };
            }

            if (result.Job is not null && fallback is null)
            {
                fallback = result.Job;
            }

            // ProcessLevel 只在 stop 分支返回 Next == null（上面已经 return），非 stop 分支的 Next 一定是
            // 它构造出来的 nextJobs —— 同一方法内可证。
            level = result.Next!;
            levelIndex++;
        }

        return new BreadthFirstSearchResult<N> { Stopped = false, Path = CreatePath(fallback) };
    }

    // updateMin updates the atomic integer `a` to the candidate value if it is less than the current value.
    internal static bool UpdateMin(ref long a, long candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref a);
            if (current < candidate)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref a, candidate, current) == current)
            {
                return true;
            }
        }
    }
}
