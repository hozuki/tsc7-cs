// ported from: internal/transformers/tstransforms/importelision.go:1-153
//
// 与 Go 的差异（语言特性；其余见 docs/architecture.md §2/§3）：
// 1) Go 的嵌入 `transformers.Transformer` → C# 的基类继承；`tx.visit` 方法组直接传给
//    `NewTransformer`（C# 的具名委托 `NodeVisitorVisit`）。
// 2) `NewImportElisionTransformer` 是 Go 的包级函数；C# 里保留为同类上的 `public static` 工厂
//    （先例：`Printer.NewPrinter`，见 Printer/Printer.cs:172）。
// 3) Go 的 `panic(...)` → `Debug.Fail(...)`（docs/architecture.md §3.2）。这里用 throw，便于
//    编译器接受后续控制流（Debug.Fail 不是 [DoesNotReturn]，见 Debug/Debug.cs）。
// 4) Go 的 `null` 指针比较 → C# 的 `is null`；`emitResolver` 是接口 `printer.EmitResolver` → `EmitResolver?`。
// 5) `len(elements.Nodes) == 0` → `elements.Nodes is null || elements.Nodes.Length == 0`
//    （C# 的 `NodeList.Nodes` 是可空数组，Go 的 nil 切片 len 也是 0）。
// 6) `core.IfElse(cond, n.Name(), nil)` → 显式 `Node?` 局部变量（C# 对 `null` 字面量的类型推断
//    在 `IfElse<T>` 上不可靠）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;

namespace TypeScript.Transformers.TsTransforms;

/// <summary>Go: <c>type ImportElisionTransformer struct</c>。</summary>
public sealed class ImportElisionTransformer : Transformer
{
    // Go 未导出字段 → `_camelCase`（docs/architecture.md §2）。
    private readonly CompilerOptions _compilerOptions;
    private readonly EmitResolver? _emitResolver;
    private SourceFile? _currentSourceFile;

    private ImportElisionTransformer(CompilerOptions compilerOptions, EmitResolver? emitResolver)
    {
        _compilerOptions = compilerOptions;
        _emitResolver = emitResolver;
    }

    /// <summary>Go: <c>func NewImportElisionTransformer(opt *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    public static Transformer? NewImportElisionTransformer(TransformOptions opt)
    {
        var compilerOptions = opt.CompilerOptions!;
        var emitContext = opt.Context;
        if (compilerOptions.VerbatimModuleSyntax.IsTrue())
        {
            throw new DebugFailureException("ImportElisionTransformer should not be used with VerbatimModuleSyntax");
        }

        var tx = new ImportElisionTransformer(compilerOptions, opt.EmitResolver);
        return tx.NewTransformer(tx.Visit, emitContext);
    }

    private Node? Visit(Node node)
    {
        if (IsSourceFile(node) && _emitResolver is not null)
        {
            _emitResolver.MarkLinkedReferencesRecursively(EmitContext().MostOriginal(node)!.AsSourceFile()!);
        }

        switch (node.Kind)
        {
            case Kind.ImportEqualsDeclaration:
                if (IsExternalModuleImportEqualsDeclaration(node))
                {
                    if (!ShouldEmitAliasDeclaration(node))
                    {
                        return null;
                    }
                }
                else
                {
                    if (!ShouldEmitImportEqualsDeclaration(node.AsImportEqualsDeclaration()!))
                    {
                        return null;
                    }
                }

                return Visitor().VisitEachChild(node);

            case Kind.ImportDeclaration:
            {
                var n = node.AsImportDeclaration()!;
                // Do not elide a side-effect only import declaration.
                //  import "foo";
                if (n.ImportClause is not null)
                {
                    var importClause = Visitor().VisitNode(n.ImportClause);
                    if (importClause is null)
                    {
                        return null;
                    }

                    return Factory().UpdateImportDeclaration(n, n.Modifiers(), importClause, n.ModuleSpecifier, Visitor().VisitNode(n.Attributes));
                }

                return Visitor().VisitEachChild(node);
            }

            case Kind.ImportClause:
            {
                var n = node.AsImportClause()!;
                Node? name = ShouldEmitAliasDeclaration(node) ? n.Name() : null;
                var namedBindings = Visitor().VisitNode(n.NamedBindings);
                if (name is null && namedBindings is null)
                {
                    // all import bindings were elided
                    return null;
                }

                return Factory().UpdateImportClause(n, n.PhaseModifier, name, namedBindings);
            }

            case Kind.NamespaceImport:
                if (!ShouldEmitAliasDeclaration(node))
                {
                    // elide unused imports
                    return null;
                }

                return node;

            case Kind.NamedImports:
            {
                var n = node.AsNamedImports()!;
                var elements = Visitor().VisitNodes(n.Elements)!;
                if (elements.Nodes is null || elements.Nodes.Length == 0)
                {
                    // all import specifiers were elided
                    return null;
                }

                return Factory().UpdateNamedImports(n, elements);
            }

            case Kind.ImportSpecifier:
                if (!ShouldEmitAliasDeclaration(node))
                {
                    // elide type-only or unused imports
                    return null;
                }

                return node;

            case Kind.ExportAssignment:
                if (!_compilerOptions.VerbatimModuleSyntax.IsTrue() && !IsValueAliasDeclaration(node))
                {
                    // elide unused import
                    return null;
                }

                return Visitor().VisitEachChild(node);

            case Kind.ExportDeclaration:
            {
                var n = node.AsExportDeclaration()!;
                Node? exportClause = null;
                if (n.ExportClause is not null)
                {
                    exportClause = Visitor().VisitNode(n.ExportClause);
                    if (exportClause is null)
                    {
                        // all export bindings were elided
                        return null;
                    }
                }

                return Factory().UpdateExportDeclaration(n, null /*modifiers*/, false /*isTypeOnly*/, exportClause, Visitor().VisitNode(n.ModuleSpecifier), Visitor().VisitNode(n.Attributes));
            }

            case Kind.NamedExports:
            {
                var n = node.AsNamedExports()!;
                var elements = Visitor().VisitNodes(n.Elements)!;
                if (elements.Nodes is null || elements.Nodes.Length == 0)
                {
                    // all export specifiers were elided
                    return null;
                }

                return Factory().UpdateNamedExports(n, elements);
            }

            case Kind.ExportSpecifier:
                if (!IsValueAliasDeclaration(node))
                {
                    // elide unused export
                    return null;
                }

                return node;

            case Kind.SourceFile:
            {
                var savedCurrentSourceFile = _currentSourceFile;
                _currentSourceFile = node.AsSourceFile();
                node = Visitor().VisitEachChild(node)!;
                _currentSourceFile = savedCurrentSourceFile;
                return node;
            }

            case Kind.ModuleDeclaration:
            case Kind.ModuleBlock:
                return Visitor().VisitEachChild(node);

            default:
                return node;
        }
    }

    private bool ShouldEmitAliasDeclaration(Node node)
    {
        return IsInJSFile(node) || IsReferencedAliasDeclaration(node);
    }

    private bool ShouldEmitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        // preserve old compiler's behavior: emit import declaration (even if we do not consider them referenced) when
        // - current file is not external module
        // - import declaration is top level and target is value imported by entity name
        return ShouldEmitAliasDeclaration(node) || (!IsExternalModule(_currentSourceFile) && IsTopLevelValueImportEqualsWithEntityName(node));
    }

    private bool IsReferencedAliasDeclaration(Node node)
    {
        // Go 把 ParseNode 的结果赋回局部参数再判 nil；C# 里必须保留 `Node?` 静态类型，
        // 否则 `parsed is null` 会被编译器当成恒假（Go 的 `node == nil` 分支是活的）。
        var parsed = EmitContext().ParseNode(node);
        return parsed is null || _emitResolver!.IsReferencedAliasDeclaration(parsed);
    }

    private bool IsValueAliasDeclaration(Node node)
    {
        var parsed = EmitContext().ParseNode(node);
        return parsed is null || _emitResolver!.IsValueAliasDeclaration(parsed);
    }

    private bool IsTopLevelValueImportEqualsWithEntityName(Node node)
    {
        var parsed = EmitContext().ParseNode(node);
        return parsed is not null && _emitResolver!.IsTopLevelValueImportEqualsWithEntityName(parsed);
    }
}
