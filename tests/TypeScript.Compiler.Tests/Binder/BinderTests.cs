// 来源说明：Go 的 internal/binder/binder_test.go 只有 BenchmarkBind（依赖未移植的
// internal/testutil/fixtures 与 internal/vfs/osvfs；Go 侧没有任何 Test* 用例），因此**没有可搬运的 xunit
// 用例**——搬运清单里这条按"无 Test 函数"处理。本文件改为 binder 的**可观察行为冒烟测试**：
// parse → `Binder.BindSourceFile` → 断言 checker（M5）依赖的 binder 契约（符号表归属、SymbolFlags、
// valueDeclaration、ExportSymbol 链接、bind 诊断、流图赋值、BindOnce 幂等）。
//
// 调用约定：`Binder` 既是命名空间又是类名，从其它命名空间裸写 `Binder.X` 会先解析到**命名空间**
// （docs/architecture.md §2 的 Debug 坑），所以包级函数用 `using static TypeScript.Binder.Binder;` 引入。
namespace TypeScript.Tests;

using TypeScript.Ast;
using TypeScript.Core;
using Xunit;
using static TypeScript.Ast.Ast;
using static TypeScript.Binder.Binder;
using static TypeScript.Parser.Parser;
using Path = TypeScript.TSPath.Path;

public class BinderTests
{
    private static SourceFile ParseFile(string text)
    {
        const string fileName = "/file.ts";
        var opts = new SourceFileParseOptions { FileName = fileName, Path = new Path(fileName) };
        return ParseSourceFile(opts, text, ScriptKind.TS);
    }

    private static SourceFile Bind(string text)
    {
        var file = ParseFile(text);
        BindSourceFile(file);
        return file;
    }

    [Fact]
    public void BindsTopLevelDeclarationsIntoLocals()
    {
        var file = Bind("""
            var x = 1;
            let y = 2;
            const z = 3;
            function f() {}
            class C { m() {} }
            interface I {}
            enum E { A }
            namespace N { export var nz = 1; }
            """);

        var locals = file.Locals!;
        Assert.True((locals["x"].Flags & SymbolFlags.FunctionScopedVariable) != 0);
        Assert.True((locals["y"].Flags & SymbolFlags.BlockScopedVariable) != 0);
        Assert.True((locals["z"].Flags & SymbolFlags.BlockScopedVariable) != 0);
        Assert.True((locals["f"].Flags & SymbolFlags.Function) != 0);
        Assert.True((locals["C"].Flags & SymbolFlags.Class) != 0);
        Assert.True((locals["I"].Flags & SymbolFlags.Interface) != 0);
        Assert.True((locals["E"].Flags & SymbolFlags.RegularEnum) != 0);
        Assert.True((locals["N"].Flags & SymbolFlags.ValueModule) != 0);

        // valueDeclaration 指向声明节点本身
        Assert.Equal(Kind.VariableDeclaration, locals["x"].ValueDeclaration!.Kind);
        Assert.Equal(Kind.FunctionDeclaration, locals["f"].ValueDeclaration!.Kind);

        // 类成员进 members、枚举成员进 exports、namespace 的导出进 exports
        Assert.True((GetMembers(locals["C"])["m"].Flags & SymbolFlags.Method) != 0);
        Assert.True((GetExports(locals["E"])["A"].Flags & SymbolFlags.EnumMember) != 0);

        // Go 的 declareModuleMember 给导出成员两个符号：局部符号带 ExportValue、导出符号带真实 flags，
        // 两者用 ExportSymbol 互链（binder.go:378-418）
        var moduleDecl = file.Statements!.Nodes![7].AsModuleDeclaration()!;
        var exportedNz = GetExports(locals["N"])["nz"];
        Assert.True((moduleDecl.Locals!["nz"].Flags & SymbolFlags.ExportValue) != 0);
        Assert.True((exportedNz.Flags & SymbolFlags.FunctionScopedVariable) != 0);
        Assert.Same(exportedNz, moduleDecl.Locals!["nz"].ExportSymbol);
    }

    [Fact]
    public void BindsModuleExportsAndLinksLocalToExportSymbol()
    {
        var file = Bind("export class C {} export function f() {}");

        Assert.True(IsExternalModule(file));
        var exports = GetExports(file.Symbol);
        Assert.True(exports.ContainsKey("C"));
        Assert.True(exports.ContainsKey("f"));

        var local = file.Locals!["C"];
        Assert.True((local.Flags & SymbolFlags.ExportValue) != 0);
        Assert.Same(exports["C"], local.ExportSymbol);
        Assert.Same(file.Symbol, exports["C"].Parent);
    }

    [Fact]
    public void ReportsRedeclarationOfBlockScopedVariable()
    {
        var file = Bind("let a = 1; let a = 2;");

        // Go 的 declareSymbolEx 会对「新声明」+「每条已存在的冲突声明」各报一次
        // （diagnostics.Cannot_redeclare_block_scoped_variable_0，code 2451）。
        var diagnostics = file.BindDiagnostics();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, d => Assert.Equal(2451, d.Code()));
        Assert.All(diagnostics, d => Assert.Same(file, d.File()));
    }

    [Fact]
    public void MergesFunctionScopedVariablesWithoutDiagnostics()
    {
        var file = Bind("var a = 1; var a = 2;");

        Assert.Empty(file.BindDiagnostics());
        Assert.Equal(2, file.Locals!["a"].Declarations!.Length);
    }

    [Fact]
    public void AssignsFlowNodesWhileBinding()
    {
        var file = Bind("var x = 1; x;");

        // bind() 里 `case KindIdentifier: node.AsIdentifier().FlowNode = b.currentFlow`（binder.go）
        var statement = file.Statements!.Nodes![1];
        var expression = statement.Expression()!.AsIdentifier()!;
        Assert.NotNull(expression.FlowNode);
    }

    [Fact]
    public void BindingTwiceIsANoOp()
    {
        var file = Bind("var x = 1;");
        var symbolCount = file.SymbolCount;
        var symbol = file.Locals!["x"];

        BindSourceFile(file);

        Assert.True(file.IsBound());
        Assert.Equal(symbolCount, file.SymbolCount);
        Assert.Same(symbol, file.Locals!["x"]);
    }
}
