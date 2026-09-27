// ported from: internal/contentmapper/transform_test.go
//
// 与 Go 的差异：
// - Go 的 `(SourceFiles, error)` → C# 的 `ParseResult` 失败时抛异常（docs/architecture.md §3.2），
//   故 `assert.NilError(err)` 一律消失。
// - Go 的 `files.Canonical` 是非 nil 的 `*ast.SourceFile`；C# 的 `SourceFile? Canonical` 需先断言非空。
// - 指针相等（Go 的 `==`）→ xunit 的 `Assert.Same`。
// - `core.ScriptKindJS` 等 → `ScriptKind.JS` / `.JSX` / `.TS` / `.TSX` / `.JSON`。
using TypeScript.Ast;
using TypeScript.ContentMapper;
using TypeScript.Core;
using Xunit;
using static TypeScript.Ast.Ast;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.SpanMap.SpanMap;
// ImplicitUsings 带进 System.IO.Path，裸写 Path 是 CS0104 → 显式别名。
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class ContentMapperTransformTests
{
    [Fact]
    public void ParseResultSupplementalFileExtensions()
    {
        var mappings = New(null);
        var result = new Result
        {
            VirtualExtension = ".ts",
            Mappings = mappings,
            Supplemental =
            [
                new MappedResult { VirtualExtension = ".js", Mappings = mappings },
                new MappedResult { VirtualExtension = ".jsx", Mappings = mappings },
                new MappedResult { VirtualExtension = ".ts", Mappings = mappings },
                new MappedResult { VirtualExtension = ".tsx", Mappings = mappings },
                new MappedResult { VirtualExtension = ".mts", Mappings = mappings },
                new MappedResult { VirtualExtension = ".cts", Mappings = mappings },
                new MappedResult { VirtualExtension = ".json", Mappings = mappings },
            ],
        };
        var files = ParseResult(
            new SourceFileParseOptions { FileName = "/component.astro", Path = (Path)"/component.astro" },
            "",
            new Mapper { Extensions = [".astro"], Name = "mapper" },
            "transform-identity",
            result);

        Assert.NotNull(files.Canonical);
        var canonical = files.Canonical!;
        Assert.Equal("transform-identity", canonical.ContentMapperTransformIdentity());
        var canonicalSupplementals = canonical.SupplementalSourceFiles();
        Assert.Equal(canonicalSupplementals.Length, files.Supplemental.Length);

        (string fileName, ScriptKind scriptKind)[] expected =
        [
            ("/component.astro.0.js", ScriptKind.JS),
            ("/component.astro.1.jsx", ScriptKind.JSX),
            ("/component.astro.2.ts", ScriptKind.TS),
            ("/component.astro.3.tsx", ScriptKind.TSX),
            ("/component.astro.4.mts", ScriptKind.TS),
            ("/component.astro.5.cts", ScriptKind.TS),
            ("/component.astro.6.json", ScriptKind.JSON),
        ];
        Assert.Equal(expected.Length, files.Supplemental.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].fileName, files.Supplemental[i].FileName());
            Assert.Equal((Path)expected[i].fileName, files.Supplemental[i].Path());
            Assert.Equal(expected[i].scriptKind, files.Supplemental[i].ScriptKind);
            Assert.Equal("transform-identity", files.Supplemental[i].ContentMapperTransformIdentity());
            Assert.Same(files.Supplemental[i], canonicalSupplementals[i]);
            Assert.Same(canonical, files.Supplemental[i].CanonicalSourceFile());
        }
    }

    [Fact]
    public void ParseResultAllowsSupplementalModules()
    {
        var mappings = New(null);
        var files = ParseResult(
            new SourceFileParseOptions { FileName = "/component.astro", Path = (Path)"/component.astro" },
            "",
            new Mapper { Extensions = [".astro"], Name = "mapper" },
            "",
            new Result
            {
                Text = "export {};",
                VirtualExtension = ".ts",
                Mappings = mappings,
                Supplemental =
                [
                    new MappedResult
                    {
                        Text = "export const value = 1;",
                        VirtualExtension = ".mts",
                        Mappings = mappings,
                    },
                ],
            });

        Assert.True(IsExternalModule(files.Supplemental[0]));
    }

    [Fact]
    public void ParseResultDoesNotLeakCanonicalModuleForcingToSupplementals()
    {
        var mappings = New(null);
        var files = ParseResult(
            new SourceFileParseOptions { FileName = "/component.astro", Path = (Path)"/component.astro" },
            "",
            new Mapper { Name = "mapper" },
            "",
            new Result
            {
                Text = "const canonical = 1;",
                VirtualExtension = ".mts",
                Mappings = mappings,
                Supplemental =
                [
                    new MappedResult
                    {
                        Text = "const supplemental = 1;",
                        VirtualExtension = ".ts",
                        Mappings = mappings,
                    },
                ],
            });

        Assert.NotNull(files.Canonical);
        Assert.True(files.Canonical!.ParseOptions().ExternalModuleIndicatorOptions.Force);
        Assert.False(files.Supplemental[0].ParseOptions().ExternalModuleIndicatorOptions.Force);
    }
}
