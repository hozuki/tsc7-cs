// ported from: internal/project/watch_test.go（28 行）+ watchtimeout_test.go（149 行里可确定复刻的不变量）
//
// 与 Go 测试的差异（逐条）：
//
// - Go 的 `assert.DeepEqual(t, got, want)`（got 在前）→ xunit `Assert.Equal(expected, actual)`（expected 在前），
//   期望值写成 `new[] { … }`（xunit 的重载要先把 T 定下来，集合表达式在这里没有目标类型）。
// - Go 的 `var w *WatchedFiles[int]; result := w.Clone(42)` 靠**nil 接收者**返回 nil；C# 实例方法不能在 null 上
//   分派，故写成 `w?.Clone(42)` 并断言 null（先例 Collections/Set.cs 文件头第 3 条记录的移植约定）。
// - `watchtimeout_test.go` 的 TestUpdateWatchTimeoutAndRollback 是 `testing/synctest` 假时钟 + 完整 Session +
//   bundled 内嵌文件的**集成**测试，验的是 session.go 的 updateWatch（watchRequestTimeout=1s 超时 →
//   `Release` 回滚 → `MarkPending` → 下次快照更新重试）——Session 属 ProjHub 切片，本文件不复刻它；
//   这里复刻其中**属于 watch.go 的确定性不变量**：watcher 身份（`Watchers().WatcherID`）与 glob 一一对应
//   （glob 未变 ⇒ 克隆出的新 watcher 身份相同，重试能复用同一个 WatcherID；glob 变了 ⇒ 身份变化），
//   以及 `WatchRegistry` 的引用计数/回滚/pending 语义。
using System.Linq;
using TypeScript.Collections;
using TypeScript.LsProto;
using TypeScript.Project;
using Xunit;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class ProjectWatchTests
{
    private const WatchKind _allWatchKinds = WatchKind.Create | WatchKind.Change | WatchKind.Delete;

    /// <summary>Go 的 <c>TestGetPathComponentsForWatching</c>（watch_test.go:10-22）。</summary>
    [Fact]
    public void TestGetPathComponentsForWatching()
    {
        Assert.Equal(new[] { "/", "project" }, Watch.GetPathComponentsForWatching("/project", ""));
        Assert.Equal(new[] { "C:/", "project" }, Watch.GetPathComponentsForWatching("C:\\project", ""));
        Assert.Equal(new[] { "//server/share", "project", "tsconfig.json" }, Watch.GetPathComponentsForWatching("//server/share/project/tsconfig.json", ""));
        Assert.Equal(new[] { "//server/share", "project", "tsconfig.json" }, Watch.GetPathComponentsForWatching("\\\\server\\share\\project\\tsconfig.json", ""));
        Assert.Equal(new[] { "C:/Users" }, Watch.GetPathComponentsForWatching("C:\\Users", ""));
        Assert.Equal(new[] { "C:/Users/andrew", "project" }, Watch.GetPathComponentsForWatching("C:\\Users\\andrew\\project", ""));
        Assert.Equal(new[] { "/home" }, Watch.GetPathComponentsForWatching("/home", ""));
        Assert.Equal(new[] { "/home/andrew", "project" }, Watch.GetPathComponentsForWatching("/home/andrew/project", ""));
    }

    /// <summary>Go 的 <c>TestNilWatchedFilesClone</c>（watch_test.go:24-27）。</summary>
    [Fact]
    public void TestNilWatchedFilesClone()
    {
        WatchedFiles<int>? w = null;
        var result = w?.Clone(42);
        Assert.Null(result);
    }

    /// <summary>
    /// watch.go:50-84：`Acquire` 在引用计数 0→1 时返回 true（需要向 client 注册），`Release` 在最后一个引用
    /// 消失时删表并交回当初的 id（需要向 client 注销）。同一个 pattern+kind 的不同 watcher 实例算同一个键。
    /// </summary>
    [Fact]
    public void TestWatchRegistryRefCounts()
    {
        var registry = WatchRegistry.NewWatchRegistry();
        var watcher = Watch.NewRecursiveDirectoryWatcher("/a", _allWatchKinds, useRelativePattern: false);

        Assert.True(registry.Acquire(watcher, new WatcherID("w.0")));
        Assert.False(registry.Acquire(watcher, new WatcherID("w.1")));

        Assert.Equal((WatcherID.Empty, false), registry.Release(watcher));
        Assert.Equal((new WatcherID("w.0"), true), registry.Release(watcher));

        // 已经删表：再 Release 既不改计数也不返回 id（对应 Go 的 nil entry 分支）。
        Assert.Equal((WatcherID.Empty, false), registry.Release(watcher));

        // 不同 kind 是不同的键；同 pattern+kind 的不同实例仍是同一个键（各自 0→1 都会返回 true）。
        Assert.True(registry.Acquire(Watch.NewRecursiveDirectoryWatcher("/a", _allWatchKinds, useRelativePattern: false), new WatcherID("w.2")));
        Assert.False(registry.Acquire(watcher, new WatcherID("w.3")));
        Assert.True(registry.Acquire(Watch.NewRecursiveDirectoryWatcher("/a", WatchKind.Change, useRelativePattern: false), new WatcherID("w.4")));
    }

    /// <summary>watch.go:86-105：注册失败后的 pending 集合（session.go 用它在下次快照更新时重试）。</summary>
    [Fact]
    public void TestWatchRegistryPending()
    {
        var registry = WatchRegistry.NewWatchRegistry();
        var id = new WatcherID("program files watcher 1");

        Assert.False(registry.IsPending(id));
        registry.MarkPending(id);
        Assert.True(registry.IsPending(id));
        registry.ClearPending(id);
        Assert.False(registry.IsPending(id));
    }

    /// <summary>
    /// watchtimeout_test.go 里"超时回滚后重试，重试用的 WatcherID 与首次尝试一致"这条不变量中属于 watch.go 的部分：
    /// watcher 身份只随 glob 集合变化（Go 的 `Clone` 不复制 `id`，保持零值 0；`ComputeWatchers` 只在 glob 变化时
    /// 才分配新 id）。
    /// </summary>
    [Fact]
    public void TestWatchedFilesIdentityTracksGlobs()
    {
        var compute = Watch.CreateResolutionLookupGlobMapper(
            workspaceDirectory: "/home/projects/ts/p1",
            libDirectory: "/home/projects/ts/p1/lib",
            currentDirectory: "/home/projects/ts/p1",
            useCaseSensitiveFileNames: true);

        var initial = WatchedFiles<SyncSet<Path>>.NewWatchedFiles("program files for /home/projects/ts/p1/tsconfig.json", _allWatchKinds, false, compute)
            .Clone(NewPathSet("/home/projects/ts/p1/src/index.ts"));

        // 首次计算：glob 从空变成非空 ⇒ 分配新 id。
        var firstId = initial.ID();
        var watchers = initial.Watchers();
        Assert.Equal(new[] { "/home/projects/ts/p1/**/*" }, watchers.WorkspaceWatchers.Select(w => w.GlobPattern.Pattern!).ToArray());
        Assert.Equal(watchers.WatcherID, firstId);
        Assert.StartsWith("program files for /home/projects/ts/p1/tsconfig.json watcher ", firstId.Value);

        // 同一个 watcher 反复取身份不变（Once 语义）。
        Assert.Equal(firstId, initial.ID());

        // 克隆 + 同一 glob 集合（重试场景）：Go 的 Clone 不复制 id ⇒ 两个克隆身份相同，且都不同于首次的 id。
        var retried1 = initial.Clone(NewPathSet("/home/projects/ts/p1/src/index.ts"));
        var retried2 = initial.Clone(NewPathSet("/home/projects/ts/p1/src/other.ts"));
        Assert.NotEqual(firstId, retried1.ID());
        Assert.Equal(retried1.ID(), retried2.ID());

        // glob 集合变化（工作区外的新文件）⇒ 身份变化，session.go 据此重新注册 watcher。
        var changed = initial.Clone(NewPathSet("/external/pkg/index.d.ts"));
        Assert.NotEqual(retried1.ID(), changed.ID());
        Assert.Empty(changed.Watchers().WorkspaceWatchers);
        Assert.Equal(new[] { "/external/pkg/**/*" }, changed.Watchers().OutsideWorkspaceWatchers.Select(w => Watch.FileSystemWatcherGlobString(w)).ToArray());
    }

    /// <summary>watch.go:298-378：把已解析的文件路径折成"工作区内的 glob + 工作区外的目录"。</summary>
    [Fact]
    public void TestResolutionLookupGlobMapper()
    {
        var mapper = Watch.CreateResolutionLookupGlobMapper(
            workspaceDirectory: "/home/projects/ts/p1",
            libDirectory: "/home/projects/ts/p1/lib",
            currentDirectory: "/home/projects/ts/p1",
            useCaseSensitiveFileNames: true);

        var result = mapper(NewPathSet(
            "/home/projects/ts/p1/src/index.ts",                  // 工作区内
            "/home/projects/node_modules/@types/node/index.d.ts", // 工作区外、命中 /node_modules/
            "/external/pkg/index.d.ts"));                         // 其它工作区外文件

        Assert.Equal(2, result._patternsInsideWorkspace!.Length);
        Assert.Contains("/home/projects/ts/p1/**/*", result._patternsInsideWorkspace!);
        Assert.Contains("/home/projects/node_modules/**/*", result._patternsInsideWorkspace!);
        Assert.Equal(new[] { "/external/pkg" }, result._directoriesOutsideWorkspace!);
        Assert.Empty(result._ignored ?? []);

        // Go 的 `if data != nil` 分支：nil 输入不产生任何 glob/目录。
        var empty = mapper(null);
        Assert.Empty(empty._patternsInsideWorkspace ?? []);
        Assert.Empty(empty._directoriesOutsideWorkspace ?? []);
    }

    /// <summary>watch.go:380-422：typings 安装位置 / 工作区 / 其它外部目录三分。</summary>
    [Fact]
    public void TestGetTypingsLocationsGlobs()
    {
        var result = Watch.GetTypingsLocationsGlobs(
            new[]
            {
                "/home/projects/ts/p1/node_modules/@types/node/index.d.ts", // typingsLocation 下
                "/home/projects/ts/p1/src/globals.d.ts",                    // workspace 下
                "/other/typings/b/index.d.ts",                              // 其它外部目录
            },
            typingsLocation: "/home/projects/ts/p1/node_modules/@types",
            workspaceDirectory: "/home/projects/ts/p1",
            currentDirectory: "/home/projects/ts/p1",
            useCaseSensitiveFileNames: true);

        Assert.Equal(2, result._patternsInsideWorkspace!.Length);
        Assert.Contains("/home/projects/ts/p1/**/*", result._patternsInsideWorkspace!);
        Assert.Contains("/home/projects/ts/p1/node_modules/@types/**/*", result._patternsInsideWorkspace!);
        Assert.Equal(new[] { "/other/typings/b" }, result._directoriesOutsideWorkspace!);
        Assert.Empty(result._ignored ?? []);
    }

    /// <summary>Go: <c>func NewWatchedFilesForPaths</c>（watch.go:186-209）：工作区内保留精确文件路径，
    /// 工作区外折成"目录级"外部 watcher。</summary>
    [Fact]
    public void TestNewWatchedFilesForPaths()
    {
        var watcher = WatchedFiles<string[]>.NewWatchedFilesForPaths(
            name: "content mapper configuration files for /home/projects/ts/p1/tsconfig.json",
            watchKind: _allWatchKinds,
            hasRelativePatternCapability: false,
            workspaceDirectory: "/home/projects/ts/p1",
            currentDirectory: "/home/projects/ts/p1",
            useCaseSensitiveFileNames: true).Clone(new[]
            {
                "/home/projects/ts/p1/foo.vue",
                "/external/pkg/bar.svelte",
            });

        var watchers = watcher.Watchers();
        Assert.Equal(new[] { "/home/projects/ts/p1/foo.vue" }, watchers.WorkspaceWatchers.Select(w => w.GlobPattern.Pattern!).ToArray());
        Assert.Equal(new[] { "/external/pkg/**/*" }, watchers.OutsideWorkspaceWatchers.Select(w => Watch.FileSystemWatcherGlobString(w)).ToArray());

        // 还没 Clone 过时 `input` 是 nil：Go 的 `range nil 切片` 是零次迭代 ⇒ 没有 watcher（不抛 NRE）。
        var untouched = WatchedFiles<string[]>.NewWatchedFilesForPaths(
            name: "content mapper configuration files for /home/projects/ts/p1/tsconfig.json",
            watchKind: _allWatchKinds,
            hasRelativePatternCapability: false,
            workspaceDirectory: "/home/projects/ts/p1",
            currentDirectory: "/home/projects/ts/p1",
            useCaseSensitiveFileNames: true).Watchers();
        Assert.Empty(untouched.WorkspaceWatchers);
        Assert.Empty(untouched.OutsideWorkspaceWatchers);
    }

    /// <summary>watch.go:380-422 的 nil 入参（Go 的 <c>range nil 切片</c> 是零次迭代）。</summary>
    [Fact]
    public void TestGetTypingsLocationsGlobsWithNilFiles()
    {
        var result = Watch.GetTypingsLocationsGlobs(
            typingsFiles: null,
            typingsLocation: "/home/projects/ts/p1/node_modules/@types",
            workspaceDirectory: "/home/projects/ts/p1",
            currentDirectory: "/home/projects/ts/p1",
            useCaseSensitiveFileNames: true);

        Assert.Empty(result._patternsInsideWorkspace ?? []);
        Assert.Empty(result._directoriesOutsideWorkspace ?? []);
    }

    /// <summary>
    /// Project.cs 的 `WatchedFiles&lt;PatternsAndIgnored&gt;.NewWatchedFiles(…, Core.Identity)`：`input` 还是 Go 的零值
    /// （没 Clone 过）⇒ 没有任何 glob；C# 侧 `PatternsAndIgnored` 是结构体，`default` 就是 Go 的零值，
    /// 不会因为 null 解引用炸掉（见 Watch.cs 文件头 19）。
    /// </summary>
    [Fact]
    public void TestTypingsWatchWithoutClone()
    {
        var watch = WatchedFiles<PatternsAndIgnored>.NewWatchedFiles(
            "typings installer files",
            _allWatchKinds,
            hasRelativePatternCapability: false,
            Core.Core.Identity);

        var watchers = watch.Watchers();
        Assert.Empty(watchers.WorkspaceWatchers);
        Assert.Empty(watchers.OutsideWorkspaceWatchers);
        Assert.Empty(watchers.IgnoredPaths);
        Assert.StartsWith("typings installer files watcher ", watchers.WatcherID.Value);
        Assert.Equal(watchers.WatcherID, watch.ID());
    }

    private static SyncSet<Path> NewPathSet(params string[] paths)
    {
        var set = new SyncSet<Path>();
        foreach (var path in paths)
        {
            set.Add(new Path(path));
        }

        return set;
    }
}
