// ported from: internal/execute/watchmanager/watchmanager_test.go
//
// 与 Go 的偏离（都是机械替换，不改变断言强度）：
// 1) Go 的包级 `NewDirWatchSet`/`tspath.ComparePathsOptions` → C# 静态类 `WatchManager` 上的静态方法
//    （`using static TypeScript.Execute.WatchManager.WatchManager;`，见 WatchManager.cs 文件头偏离 1）与
//    `TypeScript.TSPath.ComparePathsOptions`：`tspath.ComparePathsOptions{UseCaseSensitiveFileNames: x,
//    CurrentDirectory: y}` → `new ComparePathsOptions(useCaseSensitiveFileNames: x, currentDirectory: y)`。
//    命名空间 `TypeScript.Execute.WatchManager` 与类型 `WatchManager` 同名，故 `using` 必须写在 file-scoped
//    namespace **之内**（WatchManager.cs 文件头偏离 1 的消费方注意事项）。
// 2) Go 的 `gotest.tools/assert` → xunit 的 `Assert`：`assert.Equal(t, got, want, "Covered(%q)", dir)` 在 xunit
//    里没有带格式串的重载，落成 `Assert.True(got == want, message)`（xunit2004 禁止用 Assert.Equal 比 bool），
//    消息保留 Go 的标签；`assert.Equal(t, len(x), n, msg)` → `Assert.Equal(n, x.Count)`（长度为 1 的那处
//    按 xUnit2013 落成 `Assert.Single(x)`，语义仍是"恰好一个元素"）；
//    `assert.Assert(t, cond, msg)` / `assert.Assert(t, !cond, msg)` → `Assert.True(cond, msg)`/`Assert.False(cond, msg)`。
// 3) Go 的 `dirs := set.Dirs()` 返回 `map[string]bool` → C# `Dictionary<string, bool>`：`len(dirs)` → `dirs.Count`，
//    `_, original := dirs[k]` → `dirs.ContainsKey(k)`（Go 丢了 value，只看键是否存在）。
// 4) Go 的 `t.Parallel()` 由 xunit 调度，不逐字搬。
//
// 命名空间：用 `TypeScript.Tests.Execute`（镜像 Execute 目录；先例 tests/TypeScript.Compiler.Tests/Printer/UtilitiesTests.cs 的
// `TypeScript.Tests.Printer`）。本切片的验收命令是 `--filter "FullyQualifiedName~Execute"`，而目录名不进 FQN，
// 只有命名空间带 "Execute" 才会被它选中。
// 未移植：无（本文件覆盖 watchmanager_test.go 的全部 7 个测试）。
using TypeScript.TSPath;
using Xunit;

namespace TypeScript.Tests.Execute;

using TypeScript.Execute.WatchManager;
using static TypeScript.Execute.WatchManager.WatchManager;

public class WatchManagerTests
{
    // Go: caseSensitiveOpts = tspath.ComparePathsOptions{UseCaseSensitiveFileNames: true, CurrentDirectory: "/repo"}
    private static readonly ComparePathsOptions _caseSensitiveOpts = new(useCaseSensitiveFileNames: true, currentDirectory: "/repo");

    // Go: caseInsensitiveOpts = tspath.ComparePathsOptions{UseCaseSensitiveFileNames: false, CurrentDirectory: "/repo"}
    private static readonly ComparePathsOptions _caseInsensitiveOpts = new(useCaseSensitiveFileNames: false, currentDirectory: "/repo");

    // Go: TestDirWatchSetCoverage（watchmanager_test.go:21-46）。
    [Fact]
    public void TestDirWatchSetCoverage()
    {
        var set = NewDirWatchSet(_caseSensitiveOpts);
        set.Set("/repo/src", true);             // recursive
        set.Set("/repo/config", false);         // non-recursive
        set.Set("/repo/node_modules/a", false); // non-recursive

        (string Dir, bool Want)[] tests =
        [
            ("/repo/src", true),             // exact recursive
            ("/repo/src/nested", true),      // descendant of recursive
            ("/repo/src/nested/deep", true), // deep descendant of recursive
            ("/repo/config", true),          // exact non-recursive
            ("/repo/config/nested", false),  // descendant of non-recursive: NOT covered
            ("/repo/node_modules/a", true),  // exact non-recursive
            ("/repo/node_modules/b", false), // sibling, absent
            ("/repo", false),                // ancestor of watched dirs: NOT covered
            ("/other", false),               // unrelated
        ];
        foreach (var tt in tests)
        {
            var got = set.Covered(tt.Dir);
            Assert.True(got == tt.Want, $"Covered(\"{tt.Dir}\") = {got}, want {tt.Want}");
        }
    }

    // Go: TestDirWatchSetCaseSensitive（watchmanager_test.go:50-62）。
    [Fact]
    public void TestDirWatchSetCaseSensitive()
    {
        var set = NewDirWatchSet(_caseSensitiveOpts);
        set.Set("/repo/node_modules/a", false);
        set.Set("/repo/Src", true);

        Assert.True(set.Covered("/repo/node_modules/a"));
        Assert.False(set.Covered("/repo/node_modules/A"), "case-sensitive FS must not cover differently-cased dir");
        Assert.True(set.Covered("/repo/Src/nested"), "recursive descendant with matching case is covered");
        Assert.False(set.Covered("/repo/src/nested"), "case-sensitive FS must not cover differently-cased descendant");
    }

    // Go: TestDirWatchSetCaseInsensitive（watchmanager_test.go:66-76）。
    [Fact]
    public void TestDirWatchSetCaseInsensitive()
    {
        var set = NewDirWatchSet(_caseInsensitiveOpts);
        set.Set("/repo/node_modules/a", false);
        set.Set("/repo/Src", true);

        Assert.True(set.Covered("/repo/node_modules/A"), "exact match should be case-insensitive");
        Assert.True(set.Covered("/REPO/NODE_MODULES/a"), "exact match should be case-insensitive across components");
        Assert.True(set.Covered("/repo/src/nested/deep"), "recursive containment should be case-insensitive");
    }

    // Go: TestDirWatchSetCanonicalDedup（watchmanager_test.go:81-95）。
    [Fact]
    public void TestDirWatchSetCanonicalDedup()
    {
        var insensitive = NewDirWatchSet(_caseInsensitiveOpts);
        insensitive.Set("/repo/Node_Modules/PkgName", false);
        insensitive.Set("/repo/node_modules/pkgname", false); // same dir, different casing

        var dirs = insensitive.Dirs();
        // Go: assert.Equal(t, len(dirs), 1, "differently-cased dirs must collapse to one entry")
        // xunit 的等价写法（xUnit2013 要求集合长度用 Assert.Single 而不是 Assert.Equal）：恰好一个元素。
        Assert.Single(dirs);
        Assert.True(dirs.ContainsKey("/repo/Node_Modules/PkgName"), "Dirs must retain the original spelling used for registration");

        var sensitive = NewDirWatchSet(_caseSensitiveOpts);
        sensitive.Set("/repo/Node_Modules/PkgName", false);
        sensitive.Set("/repo/node_modules/pkgname", false); // distinct dirs when case-sensitive
        // Go: assert.Equal(t, len(sensitive.Dirs()), 2, "case-sensitive FS keeps differently-cased dirs distinct")
        Assert.Equal(2, sensitive.Dirs().Count);
    }

    // Go: TestDirWatchSetUpgradeToRecursive（watchmanager_test.go:99-112）。
    [Fact]
    public void TestDirWatchSetUpgradeToRecursive()
    {
        var set = NewDirWatchSet(_caseSensitiveOpts);
        set.Set("/repo/src", false);
        Assert.True(set.Covered("/repo/src"));
        Assert.False(set.Covered("/repo/src/nested"), "descendant not covered while non-recursive");

        set.Set("/repo/src", true);
        Assert.True(set.Covered("/repo/src/nested"), "descendant covered after upgrade to recursive");
        Assert.True(set.Dirs()["/repo/src"]);
    }

    // Go: TestDirWatchSetNeverDowngrades（watchmanager_test.go:116-126）。
    [Fact]
    public void TestDirWatchSetNeverDowngrades()
    {
        var set = NewDirWatchSet(_caseSensitiveOpts);
        set.Set("/repo/src", true);
        set.Set("/repo/src", false);

        Assert.True(set.Dirs()["/repo/src"]);
        Assert.True(set.Covered("/repo/src/nested"), "recursive coverage retained after non-recursive Set");
    }

    // Go: TestDirWatchSetDirs（watchmanager_test.go:130-140）。
    [Fact]
    public void TestDirWatchSetDirs()
    {
        var set = NewDirWatchSet(_caseSensitiveOpts);
        set.Set("/repo/a", false);
        set.Set("/repo/b", true);
        set.Set("/repo/a", false); // duplicate non-recursive add is idempotent

        var dirs = set.Dirs();
        Assert.Equal(2, dirs.Count);
        Assert.False(dirs["/repo/a"]);
        Assert.True(dirs["/repo/b"]);
    }
}
