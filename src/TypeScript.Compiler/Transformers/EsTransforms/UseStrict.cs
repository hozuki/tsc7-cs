// ported from: internal/transformers/estransforms/usestrict.go（50 行，全部：NewUseStrictTransformer、useStrictTransformer +
//               visit、visitSourceFile）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type useStrictTransformer struct { transformers.Transformer; compilerOptions *core.CompilerOptions;
//    getEmitModuleFormatOfFile func(file ast.HasFileName) core.ModuleKind }`（嵌入）→ C# 的
//    `internal sealed class UseStrictTransformer : Transformer` + 两个只读字段；Go 的结构体字面量构造
//    → `internal` 构造函数（字段是 readonly，只能由本类的构造函数赋值）。
// 2) `GetEmitModuleFormatOfFile` 是 Go 的函数类型字段 → C# 用 `Func<HasFileName, ModuleKind>`
//    （TransformOptions.GetEmitModuleFormatOfFile 的委托字段类型，见 Transformers/Transformer.cs 契约）。
// 3) Go 的 `NewUseStrictTransformer`（**导出**）→ `public static partial class EsTransforms` 的 `public static` 成员
//    （docs/architecture.md §2：Go 包级函数进与 package 同名的静态类；导出名原样保留）。
// 4) `return tx.Factory().UpdateSourceFile(node, statementList, node.EndOfFileToken).AsSourceFile().AsNode()`：
//    Go 末尾两次转换（`AsSourceFile()`/`AsNode()`）在 C# 里是恒等（同一个 Node 对象）→ 直接返回。
// 5) `node.Statements.Nodes`（Go 的 nil 切片）→ C# `?? []`，避免把 Go 的"nil 长度 0"变成 NRE。
using System;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;

namespace TypeScript.Transformers.EsTransforms;

internal sealed class UseStrictTransformer : Transformer
{
    private readonly CompilerOptions _compilerOptions;
    private readonly Func<HasFileName, ModuleKind> _getEmitModuleFormatOfFile;

    internal UseStrictTransformer(CompilerOptions compilerOptions, Func<HasFileName, ModuleKind> getEmitModuleFormatOfFile)
    {
        _compilerOptions = compilerOptions;
        _getEmitModuleFormatOfFile = getEmitModuleFormatOfFile;
    }

    /// <summary>Go: <c>func (tx *useStrictTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if (node.Kind != Kind.SourceFile)
        {
            return node;
        }

        return VisitSourceFile(node.AsSourceFile()!);
    }

    /// <summary>Go: <c>func (tx *useStrictTransformer) visitSourceFile(node *ast.SourceFile) *ast.Node</c>。</summary>
    private Node VisitSourceFile(SourceFile node)
    {
        if (node.ScriptKind == ScriptKind.JSON)
        {
            return node;
        }

        var isExternalModule = IsExternalModule(node);
        var moduleKind = _compilerOptions.GetEmitModuleKind();
        var format = _getEmitModuleFormatOfFile(node);

        // ESM is always strict. If the file is ESM, and CJS emit
        // has not been requested, then skip adding "use strict".
        if (isExternalModule && moduleKind >= ModuleKind.ES2015 &&
            (moduleKind == ModuleKind.Preserve || format >= ModuleKind.ES2015))
        {
            return node;
        }

        var statements = Factory().EnsureUseStrict(node.Statements!.Nodes ?? []);
        var statementList = Factory().NewNodeList(statements);
        statementList.Loc = node.Statements!.Loc;
        return Factory().UpdateSourceFile(node, statementList, node.EndOfFileToken);
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>func NewUseStrictTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewUseStrictTransformer(TransformOptions opts)
    {
        var tx = new UseStrictTransformer(opts.CompilerOptions!, opts.GetEmitModuleFormatOfFile!);
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
