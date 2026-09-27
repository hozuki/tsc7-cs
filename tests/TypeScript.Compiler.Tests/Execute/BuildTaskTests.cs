// ported from: internal/execute/build/buildtask_contentmapper_test.go
//
// 与 Go 的偏离（都是机械替换，不改变断言强度）：
// 1) Go 的包级函数 `isContentMapperSupplementalBuildInfoPath` 在 C# 是 internal 静态类 `BuildTask` 上的
//    internal 静态方法（BuildTask.cs 文件头偏离 8；`BuildTask` 本身是 internal，测试程序集有
//    InternalsVisibleTo）→ 用 `using static TypeScript.Execute.Build.BuildTask;` 后与 Go 的包级调用同形。
// 2) Go 的 `iter.Seq[tspath.Path]` → `IEnumerable<Path>`：测试里的 `slices.Values(roots)`（把切片变成 seq）
//    在 C# 直接传数组本身（数组就是 IEnumerable<Path>）。
// 3) `tspath.Path("/src/app.vue")` → `new Path("/src/app.vue")`（record struct 的主构造函数）。
// 4) Go 的 `gotest.tools/assert` → xunit 的 `Assert.True`/`Assert.False`（Go 的 `assert.Assert(t, x)` 就是
//    bool 断言，逐条对应）。
// 5) Go 的 `t.Parallel()` 由 xunit 调度，不逐字搬。
//
// 命名空间：用 `TypeScript.Tests.Execute`（镜像 Execute 目录；先例 tests/TypeScript.Compiler.Tests/Printer/UtilitiesTests.cs 的
// `TypeScript.Tests.Printer`）。本切片的验收命令是 `--filter "FullyQualifiedName~Execute"`，而目录名不进 FQN，
// 只有命名空间带 "Execute" 才会被它选中。
// 未移植：无（buildtask_contentmapper_test.go 只有这一个测试）。
using Xunit;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests.Execute;

using static TypeScript.Execute.Build.BuildTask;

public class BuildTaskTests
{
    // Go: TestIsContentMapperSupplementalBuildInfoPath（buildtask_contentmapper_test.go:11-21）。
    [Fact]
    public void TestIsContentMapperSupplementalBuildInfoPath()
    {
        Path[] roots = [new Path("/src/app.vue"), new Path("/src/index.ts")];

        Assert.True(IsContentMapperSupplementalBuildInfoPath(new Path("/src/app.vue.0.ts"), roots));
        Assert.True(IsContentMapperSupplementalBuildInfoPath(new Path("/src/app.vue.12.mts"), roots));
        Assert.False(IsContentMapperSupplementalBuildInfoPath(new Path("/src/app.vue.ts"), roots));
        Assert.False(IsContentMapperSupplementalBuildInfoPath(new Path("/src/app.vue.0.txt"), roots));
        Assert.False(IsContentMapperSupplementalBuildInfoPath(new Path("/src/other.vue.0.ts"), roots));
    }
}
