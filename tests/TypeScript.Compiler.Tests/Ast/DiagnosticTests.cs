// ported from: internal/ast/diagnostic_test.go
//
// 与 Go 的差异：
// - Go 的 `var collection DiagnosticsCollection`（值语义 + sync.Mutex）→ C# `new DiagnosticsCollection()`（引用类型）。
// - Go 的 `&SourceFile{parseOptions: …}` → C# 的对象初始化器写 internal 字段 `_parseOptions`（测试程序集有
//   InternalsVisibleTo，见 Properties/AssemblyInfo.cs）。
// - `core.TextRange{}` → `default`；`tspath.Path("/src/file.ts")` → `new Path("/src/file.ts")`。
// - Go 用 `t.Fatalf`/`gotest.tools/assert` → xunit 的 Assert（比较语义逐条对应，包括指针相等 → ReferenceEquals）。
// - Go 的 t.Parallel() 在 xunit 里由框架调度（不从 Go 逐字搬）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using Path = TypeScript.TSPath.Path;
using Xunit;

using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

namespace TypeScript.Tests;

public class DiagnosticTests
{
    [Fact]
    public void DiagnosticsCollectionDeduplicatesExactDiagnosticsOnAdd()
    {
        var collection = new DiagnosticsCollection();
        var first = NewCompilerDiagnostic(Cannot_find_name_0, "x")
            .AddRelatedInfo(NewCompilerDiagnostic(X_0_is_declared_here, "first"));
        var second = NewCompilerDiagnostic(Cannot_find_name_0, "x")
            .AddRelatedInfo(NewCompilerDiagnostic(X_0_is_declared_here, "first"));
        var different = NewCompilerDiagnostic(Cannot_find_name_0, "x")
            .AddRelatedInfo(NewCompilerDiagnostic(X_0_is_declared_here, "second"));

        Assert.Same(first, collection.Add(first));

        var canonical = collection.Add(second);
        Assert.Same(first, canonical);

        Assert.Same(different, collection.Add(different));

        canonical.AddRelatedInfo(NewCompilerDiagnostic(X_0_is_declared_here, "third"));
        var collected = collection.GetGlobalDiagnostics();
        Assert.Equal(2, collected.Length);
        Assert.Equal(2, (first.RelatedInformation() ?? []).Length);
    }

    [Fact]
    public void DiagnosticsCollectionPreservesDistinctAdHocMessages()
    {
        var collection = new DiagnosticsCollection();
        var first = NewCompilerDiagnostic(NewAdHocMessage("first"));
        var second = NewCompilerDiagnostic(NewAdHocMessage("second"));

        collection.Add(first);
        collection.Add(second);
        var collected = collection.GetGlobalDiagnostics();
        Assert.Equal(2, collected.Length);
    }

    [Fact]
    public void DiagnosticsCollectionGetsDiagnosticsForEquivalentSourceFile()
    {
        var path = new Path("/src/file.ts");
        var diagnosticFile = new SourceFile
        {
            _parseOptions = new SourceFileParseOptions { FileName = path.Value, Path = path },
        };
        var requestedFile = new SourceFile
        {
            _parseOptions = new SourceFileParseOptions { FileName = path.Value, Path = path },
        };
        var diagnostic = NewDiagnostic(diagnosticFile, default, Cannot_find_name_0, "x");

        var collection = new DiagnosticsCollection();
        collection.Add(diagnostic);

        var collected = collection.GetDiagnosticsForFile(requestedFile);
        Assert.Single(collected);
        Assert.Same(diagnostic, collected[0]);
    }

    [Fact]
    public void ExternalDiagnosticIdentity()
    {
        var file = new SourceFile
        {
            _parseOptions = new SourceFileParseOptions { FileName = "/src/file.vue", Path = new Path("/src/file.vue") },
        };
        var loc = NewTextRange(1, 2);
        var first = NewExternalDiagnostic(file, loc, "mapper-a", Category.Error, 0, "first");
        Diagnostic[] diagnostics =
        [
            first,
            NewExternalDiagnostic(file, loc, "mapper-a", Category.Error, 0, "second"),
            NewExternalDiagnostic(file, loc, "mapper-b", Category.Error, 0, "first"),
            NewExternalDiagnostic(file, loc, "mapper-a", Category.Warning, 0, "first"),
        ];

        var collection = new DiagnosticsCollection();
        foreach (var diagnostic in diagnostics)
        {
            Assert.True(!EqualDiagnosticsNoRelatedInfo(first, diagnostic) || ReferenceEquals(diagnostic, first));
            Assert.True(CompareDiagnostics(first, diagnostic) != 0 || ReferenceEquals(diagnostic, first));
            collection.Add(diagnostic);
        }

        Assert.Equal(diagnostics.Length, collection.GetDiagnostics().Length);
    }
}
