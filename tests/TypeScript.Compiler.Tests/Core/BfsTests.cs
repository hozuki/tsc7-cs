// ported from: internal/core/bfs_test.go
//
// 与 Go 测试的差异（逐条说明）：
//
// - Go 的 `t.Run("...", ...)` 子测试（含嵌套两层）→ 这里一个叶子子测试一个 [Fact]，方法名 = 子测试描述。
//   Go 的 `t.Parallel()` 在 xunit 里由"不同测试类并行"承担；这些用例各自构造图与 visited 集合，互不影响。
// - `assert.Equal(t, x, y, msg)` / `assert.Assert(t, cond, msg)` → `Assert.Equal` / `Assert.True`（保留 msg）。
// - `assert.DeepEqual(t, result.Path, []string{...})` → `Assert.Equal(new[] {...}, result.Path)`（xunit 对
//   IEnumerable 逐元素、按顺序比较）。Go 的 `map[string][]string` + `children := func(node) []string{...}`
//   → `Dictionary<string, string[]>` + `children = node => graph[node]`（Go 的 map 缺键返回 nil，这里的图
//   每个节点都显式列了键，取值语义一致）。
// - `assert.Assert(t, result.Path == nil, "...")` → `Assert.Null(result.Path)`。
// - `sort.Strings(visitedNodes)` → `visitedNodes.Sort(StringComparer.Ordinal)`（Go 的 sort.Strings 是字节序）。
// - `collections.SyncSet[string]` 的零值 `var visited collections.SyncSet[string]` → `new SyncSet<string>()`。
// - Go 在每个子测试里各建一份 `basicGraph`；这里把它提成静态字段供 3 个用例复用（内容与 Go 完全一致）。
// - Go 的 `assert.Equal(t, x, y, msg)` 对 bool 的 3 参形式在 xunit 里会解析到"自定义比较器"重载，
//   故布尔断言改用 `Assert.True/False(cond, msg)`（保留 Go 的消息文本）。
// - 末尾的 `..._ResultIsDeterministicAcrossRuns` 是**本移植新增**（Go 侧没有）：见该用例上的注释。
using TypeScript.Collections;
using TypeScript.Core;
using Xunit;
using static TypeScript.Core.Core;

namespace TypeScript.Tests;

public class CoreBfsTests
{
    // Test basic functionality with a simple DAG
    // Graph: A -> B, A -> C, B -> D, C -> D
    private static readonly Dictionary<string, string[]> basicGraph = new()
    {
        ["A"] = ["B", "C"],
        ["B"] = ["D"],
        ["C"] = ["D"],
        ["D"] = [],
    };

    [Fact]
    public void TestBreadthFirstSearchParallel_FindSpecificNode()
    {
        var result = BreadthFirstSearchParallel("A", node => basicGraph[node], node => (node == "D", true));
        Assert.True(result.Stopped, "Expected search to stop at D");
        Assert.Equal(new[] { "D", "B", "A" }, result.Path);
    }

    [Fact]
    public void TestBreadthFirstSearchParallel_VisitAllNodes()
    {
        var mu = new object();
        var visitedNodes = new List<string>();
        var result = BreadthFirstSearchParallel("A", node => basicGraph[node], node =>
        {
            lock (mu)
            {
                visitedNodes.Add(node);
            }

            return (false, false); // Never stop early
        });

        // Should return nil since we never return true
        Assert.False(result.Stopped, "Expected search to not stop early");
        Assert.Null(result.Path); // Expected nil path when visit function never returns true

        // Should visit all nodes exactly once
        visitedNodes.Sort(StringComparer.Ordinal);
        Assert.Equal(new[] { "A", "B", "C", "D" }, visitedNodes);
    }

    [Fact]
    public void TestBreadthFirstSearchParallel_EarlyTermination()
    {
        // Test that nodes below the target level are not visited
        var graph = new Dictionary<string, string[]>
        {
            ["Root"] = ["L1A", "L1B"],
            ["L1A"] = ["L2A", "L2B"],
            ["L1B"] = ["L2C"],
            ["L2A"] = ["L3A"],
            ["L2B"] = [],
            ["L2C"] = [],
            ["L3A"] = [],
        };

        var visited = new SyncSet<string>();
        BreadthFirstSearchParallelEx(
            "Root",
            node => graph[node],
            node => (node == "L2B", true), // Stop at level 2
            new BreadthFirstSearchOptions<string, string>
            {
                Visited = visited,
            },
            Identity);

        Assert.True(visited.Has("Root"), "Expected to visit Root");
        Assert.True(visited.Has("L1A"), "Expected to visit L1A");
        Assert.True(visited.Has("L1B"), "Expected to visit L1B");
        Assert.True(visited.Has("L2A"), "Expected to visit L2A");
        Assert.True(visited.Has("L2B"), "Expected to visit L2B");
        // L2C is non-deterministic
        Assert.True(!visited.Has("L3A"), "Expected not to visit L3A");
    }

    [Fact]
    public void TestBreadthFirstSearchParallel_ReturnsFallbackWhenNoOtherResultFound()
    {
        // Test that fallback behavior works correctly
        var visited = new SyncSet<string>();
        var result = BreadthFirstSearchParallelEx(
            "A",
            node => basicGraph[node],
            node => (node == "A", false), // Record A as a fallback, but do not stop
            new BreadthFirstSearchOptions<string, string>
            {
                Visited = visited,
            },
            Identity);

        Assert.False(result.Stopped, "Expected search to not stop early");
        Assert.Equal(new[] { "A" }, result.Path);
        Assert.True(visited.Has("B"), "Expected to visit B");
        Assert.True(visited.Has("C"), "Expected to visit C");
        Assert.True(visited.Has("D"), "Expected to visit D");
    }

    [Fact]
    public void TestBreadthFirstSearchParallel_ReturnsAStopResultOverAFallback()
    {
        // Test that a stop result is preferred over a fallback
        var result = BreadthFirstSearchParallel("A", node => basicGraph[node], node =>
        {
            switch (node)
            {
                case "A":
                    return (true, false); // Record fallback
                case "D":
                    return (true, true); // Stop at D
                default:
                    return (false, false);
            }
        });

        Assert.True(result.Stopped, "Expected search to stop at D");
        Assert.Equal(new[] { "D", "B", "A" }, result.Path);
    }

    // **本移植新增**（Go 侧没有对应断言）：层序并行 + 最小索引合并 ⇒ 路径必须与线程调度无关。
    // 32 宽 × 4 层的图让每层真的并行跑；重复 200 次逐元素比较路径。Go 的实现里若把
    // "最小索引" 改成 "谁先到谁赢"（或在错误的层做去重），这条会立刻失败。
    [Fact]
    public void BreadthFirstSearchParallel_ResultIsDeterministicAcrossRuns()
    {
        var graph = new Dictionary<string, string[]>();
        for (var level = 0; level < 4; level++)
        {
            for (var i = 0; i < 32; i++)
            {
                graph[$"L{level}_{i}"] = level == 3
                    ? []
                    : [$"L{level + 1}_{i}", $"L{level + 1}_{(i + 1) % 32}"];
            }
        }

        graph["root"] = ["L0_0", "L0_16"];

        string[]? expected = null;
        for (var run = 0; run < 200; run++)
        {
            var result = BreadthFirstSearchParallel("root", node => graph[node], node => (node == "L3_2", true));
            Assert.True(result.Stopped);
            Assert.NotNull(result.Path);
            if (expected is null)
            {
                expected = result.Path;
            }
            else
            {
                Assert.Equal(expected, result.Path);
            }
        }

        Assert.Equal(5, expected!.Length); // root → L0_* → L1_* → L2_* → L3_2
        Assert.Equal("L3_2", expected![0]);
    }
}
