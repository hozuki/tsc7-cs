// ported from: internal/transformers/moduletransforms/impliedmodule.go
//
// Go → C# 形状说明（docs/architecture.md §2、docs/ast.md §2）：
// 1) Go 的嵌入 `transformers.Transformer` → C# 基类 `Transformer`（T1 提供）。
// 2) Go 的 `tx.visit` 方法 → C# 私有 `Visit(Node?)`，方法组直接传给 `NewTransformer`。
// 3) Go 的未使用字段 `resolver`（只被构造器赋值）声明为 `internal`，避免 CS0414 且与 Go 字段对齐。
// 4) Go 的包级导出处方函数 `NewImpliedModuleTransformer` → 与包同名的静态类 `ModuleTransforms` 的 static 成员（§2）。

using System;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;

namespace TypeScript.Transformers.ModuleTransforms;

/// <summary>Go: <c>type ImpliedModuleTransformer struct</c>（impliedmodule.go:11-19）。</summary>
internal sealed class ImpliedModuleTransformer : Transformer
{
    internal TransformOptions _opts = null!;

    /// <summary>Go: impliedmodule.go:13 的 `resolver`：Go 里只被构造器赋值、后续从未读取（已 grep 确认）→ internal 对齐且避免 CS0414。</summary>
    internal ReferenceResolver? _resolver;

    internal Func<HasFileName, ModuleKind>? _getEmitModuleFormatOfFile;
    private Transformer? _cjsTransformer;
    private Transformer? _esmTransformer;

    /// <summary>Go: <c>func (tx *ImpliedModuleTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        switch (node.Kind)
        {
            case Kind.SourceFile:
                node = VisitSourceFile(node.AsSourceFile()!)!;
                break;
        }
        return node;
    }

    /// <summary>Go: <c>func (tx *ImpliedModuleTransformer) visitSourceFile(node *ast.SourceFile) *ast.Node</c>。</summary>
    private Node VisitSourceFile(SourceFile node)
    {
        if (node.IsDeclarationFile)
        {
            return node;
        }

        var format = _getEmitModuleFormatOfFile!(node);

        Transformer transformer;
        if (format >= ModuleKind.ES2015)
        {
            _esmTransformer ??= ModuleTransforms.NewESModuleTransformer(_opts);
            transformer = _esmTransformer!;
        }
        else
        {
            _cjsTransformer ??= ModuleTransforms.NewCommonJSModuleTransformer(_opts);
            transformer = _cjsTransformer!;
        }

        return transformer.TransformSourceFile(node);
    }
}

/// <summary>Go: moduletransforms 包的包级导出处方函数。</summary>
public static partial class ModuleTransforms
{
    /// <summary>Go: <c>func NewImpliedModuleTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewImpliedModuleTransformer(TransformOptions opts)
    {
        var tx = new ImpliedModuleTransformer
        {
            _opts = opts,
            _resolver = opts.Resolver,
            _getEmitModuleFormatOfFile = opts.GetEmitModuleFormatOfFile,
        };
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
