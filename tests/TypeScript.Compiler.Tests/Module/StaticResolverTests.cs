// ported from: internal/module/staticresolver_test.go
//
// 与 Go 测试的偏离：Go 用 `vfstest.FromMap`；这里用本目录的 `MapFileSystem`（见 MemoryFileSystem.cs 文件头）。
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.Vfs.VfsTest;
using Xunit;
using static TypeScript.Module.Module;

namespace TypeScript.Tests;

public class ModuleStaticResolverTests
{
    [Fact]
    public void TestStaticResolver()
    {
        var fs = VfsTest.FromMap(new Dictionary<string, string>
        {
            ["/repo/node_modules/fallback/package.json"] = """{"name":"fallback","types":"index.d.ts"}""",
            ["/repo/node_modules/fallback/index.d.ts"] = "export {};",
        }, true);
        var fallback = NewResolver(new ResolverOptions
        {
            Host = new ResolutionHostStub(fs, "/repo"),
            CompilerOptions = new CompilerOptions
            {
                Module = ModuleKind.ESNext,
                ModuleResolution = ModuleResolutionKind.Bundler,
            },
        });
        ModuleKind? esm = ModuleKind.ESNext;
        var resolutions = StaticResolutions.NewStaticResolutions(
            [
                new StaticResolutionEntry { ModuleName = "provided", Result = new ResolvedModule { ResolvedFileName = "/global.d.ts" } },
                new StaticResolutionEntry { ModuleName = "provided", ContainingDirectory = "/repo/src", Result = new ResolvedModule { ResolvedFileName = "/directory.d.ts" } },
                new StaticResolutionEntry { ModuleName = "provided", ResolutionMode = esm, Result = new ResolvedModule { ResolvedFileName = "/esm.d.ts" } },
                new StaticResolutionEntry { ModuleName = "provided", ContainingDirectory = "/repo/src", ResolutionMode = esm, Result = new ResolvedModule { ResolvedFileName = "/directory-esm.d.ts" } },
                new StaticResolutionEntry { ModuleName = "unresolved" },
            ],
            true,
            "/repo",
            true);
        var resolver = StaticResolver.NewStaticResolver(fallback, resolutions);

        (string Name, string ContainingFile, ModuleKind Mode, string ResolvedFileName)[] tests =
        [
            ("provided", "/repo/src/index.ts", ModuleKind.ESNext, "/directory-esm.d.ts"),
            ("provided", "/repo/src/index.ts", ModuleKind.CommonJS, "/directory.d.ts"),
            ("provided", "/repo/other/index.ts", ModuleKind.ESNext, "/esm.d.ts"),
            ("provided", "/repo/other/index.ts", ModuleKind.CommonJS, "/global.d.ts"),
            ("fallback", "/repo/src/index.ts", ModuleKind.ESNext, "/repo/node_modules/fallback/index.d.ts"),
            ("unresolved", "/repo/src/index.ts", ModuleKind.ESNext, ""),
        ];

        foreach (var test in tests)
        {
            var (result, _) = resolver.ResolveModuleName(test.Name, test.ContainingFile, test.Mode, null);
            if (test.ResolvedFileName == "")
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
                Assert.Equal(test.ResolvedFileName, result.ResolvedFileName);
            }
        }
    }
}
