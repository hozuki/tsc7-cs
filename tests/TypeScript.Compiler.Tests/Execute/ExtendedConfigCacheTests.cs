// ported from: internal/execute/tsc/extendedconfigcache_test.go
//
// 与 Go 的偏离（都是机械替换，不改变断言强度）：
// 1) Go 的 `tsc.ExtendedConfigCache{}` → C# `new TypeScript.Execute.Tsc.ExtendedConfigCache()`（用别名
//    `TscExtendedConfigCache` 消歧：`using static TypeScript.TsOptions.TsOptions;` 也会把同名接口
//    `TsOptions.ExtendedConfigCache` 带进来）。
// 2) Go 的 `&tsc_test.testParseConfigHost{fs, cwd}` → 本文件内的同名假主机 `TestParseConfigHost : ParseConfigHost`
//    （`FS()` + `GetCurrentDirectory()`，与 Go 的两个方法逐条对应）。
// 3) `vfstest.FromMap(map[string]any{…}, false)` → `FromMap(new Dictionary<string, string> { … }, false)`
//    （Go 的 `any` 值全是字符串；C# 侧直接选 `Dictionary<string, string>` 重载，语义等价且不用装箱）。
// 4) `tsoptions.GetParsedCommandLineOfConfigFile(configFileName, nil, nil, host, cache)` → 同一个包级函数的 C#
//    版本 `GetParsedCommandLineOfConfigFile(configFileName, null, null, host, cache)`，返回
//    `(ParsedCommandLine? CommandLine, Diagnostic[] Errors)`。
// 5) Go 的 `t.Run(名称, …)` 子测试 → 独立 `[Fact]`，方法名 = `<GoTest>_<子测试名驼峰>`（同
//    tests/TypeScript.Compiler.Tests/Vfs/VfsTestTests.cs 的约定，Go 的 t.Run 名本身是带空格的短句）。
// 6) Go 的 `t.Fatal`/`t.Error` → xunit 的 `Assert`（`if cmd == nil { t.Fatal }` → `Assert.NotNull(cmd)`；
//    `if len(cmd.Errors) == 0 { t.Fatal }` → `Assert.NotEmpty`；圆环诊断的 18000 检查逐条对应）。
// 7) Go 的 `t.Parallel()` 由 xunit 调度，不逐字搬。
//
// 命名空间：用 `TypeScript.Tests.Execute`（镜像 Execute 目录；先例 tests/TypeScript.Compiler.Tests/Printer/UtilitiesTests.cs 的
// `TypeScript.Tests.Printer`）。本切片的验收命令是 `--filter "FullyQualifiedName~Execute"`，而目录名不进 FQN，
// 只有命名空间带 "Execute" 才会被它选中。
// 未移植：无（extendedconfigcache_test.go 的全部 2 个测试函数、3 个子测试都已覆盖；success/非圆环用例在该
// Go 文件里本来就不存在）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using Xunit;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Vfs.VfsTest.VfsTest;
using TscExtendedConfigCache = TypeScript.Execute.Tsc.ExtendedConfigCache;

namespace TypeScript.Tests.Execute;

public class ExtendedConfigCacheTests
{
    /// <summary>Go: <c>type testParseConfigHost struct</c>（extendedconfigcache_test.go:13-18）。</summary>
    private sealed class TestParseConfigHost : ParseConfigHost
    {
        internal FS _fs = null!;
        internal string _cwd = "";

        public FS FS() => _fs;

        public string GetCurrentDirectory() => _cwd;
    }

    // Go: TestExtendedConfigCacheExtendsCircularity/"self-referencing extends"（extendedconfigcache_test.go:25-45）。
    [Fact]
    public void TestExtendedConfigCacheExtendsCircularity_SelfReferencingExtends()
    {
        // Regression test: a tsconfig extends cycle should produce an error,
        // not a deadlock when using the tsc ExtendedConfigCache.
        var files = new Dictionary<string, string>
        {
            ["/project/tsconfig.json"] = """{"extends": "./base.json"}""",
            ["/project/base.json"] = """{"extends": "./base.json"}""",
            ["/project/main.ts"] = "// Hello World!",
        };

        var fs = FromMap(files, false /*useCaseSensitiveFileNames*/);
        var host = new TestParseConfigHost { _fs = fs, _cwd = "/project" };
        var cache = new TscExtendedConfigCache();

        var (cmd, _) = GetParsedCommandLineOfConfigFile("/project/tsconfig.json", null, null, host, cache);
        Assert.NotNull(cmd);
        AssertHasCircularityDiagnostic(cmd!);
    }

    // Go: TestExtendedConfigCacheExtendsCircularity/"mutual extends cycle"（extendedconfigcache_test.go:47-68）。
    [Fact]
    public void TestExtendedConfigCacheExtendsCircularity_MutualExtendsCycle()
    {
        // Two config files that extend each other.
        var files = new Dictionary<string, string>
        {
            ["/project/tsconfig.json"] = """{"extends": "./other.json"}""",
            ["/project/other.json"] = """{"extends": "./tsconfig.json"}""",
            ["/project/main.ts"] = "// Hello World!",
        };

        var fs = FromMap(files, false /*useCaseSensitiveFileNames*/);
        var host = new TestParseConfigHost { _fs = fs, _cwd = "/project" };
        var cache = new TscExtendedConfigCache();

        var (cmd, _) = GetParsedCommandLineOfConfigFile("/project/tsconfig.json", null, null, host, cache);
        Assert.NotNull(cmd);
        AssertHasCircularityDiagnostic(cmd!);
    }

    // Go: TestExtendedConfigCacheExtendsCircularity/"case-insensitive self-referencing extends"（extendedconfigcache_test.go:70-93）。
    [Fact]
    public void TestExtendedConfigCacheExtendsCircularity_CaseInsensitiveSelfReferencingExtends()
    {
        // On a case-insensitive FS, ./Base.json and ./base.json resolve to the same
        // cache entry. The cycle check must use canonical paths to avoid deadlock.
        var files = new Dictionary<string, string>
        {
            ["/project/tsconfig.json"] = """{"extends": "./Base.json"}""",
            ["/project/base.json"] = """{"extends": "./base.json"}""",
            ["/project/main.ts"] = "// Hello World!",
        };

        var fs = FromMap(files, false /*useCaseSensitiveFileNames*/);
        var host = new TestParseConfigHost { _fs = fs, _cwd = "/project" };
        var cache = new TscExtendedConfigCache();

        var (cmd, _) = GetParsedCommandLineOfConfigFile("/project/tsconfig.json", null, null, host, cache);
        Assert.NotNull(cmd);
        AssertHasCircularityDiagnostic(cmd!);
    }

    // Go: TestExtendedConfigCacheNullExtendsDoesNotPanic（extendedconfigcache_test.go:95-112）。
    [Fact]
    public void TestExtendedConfigCacheNullExtendsDoesNotPanic()
    {
        var files = new Dictionary<string, string>
        {
            ["/project/tsconfig.json"] = """{"extends": null}""",
            ["/project/main.ts"] = "// Hello World!",
        };

        var fs = FromMap(files, false /*useCaseSensitiveFileNames*/);
        var host = new TestParseConfigHost { _fs = fs, _cwd = "/project" };
        var cache = new TscExtendedConfigCache();

        var (cmd, _) = GetParsedCommandLineOfConfigFile("/project/tsconfig.json", null, null, host, cache);
        Assert.NotNull(cmd);
        Assert.NotEmpty(cmd!.Errors);
    }

    /// <summary>Go: <c>assertHasCircularityDiagnostic(t, cmd)</c>（extendedconfigcache_test.go:114-123）。</summary>
    private static void AssertHasCircularityDiagnostic(ParsedCommandLine cmd)
    {
        foreach (var d in cmd.Errors)
        {
            if (d is not null && d.Code() == 18000)
            {
                return;
            }
        }
        Assert.Fail("expected circularity diagnostic (code 18000), but none was found");
    }
}
