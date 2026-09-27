// ported from: internal/checker/checker.go:15270-15392
//   （getModuleSpecifierForImportOrExport / getModuleSpecifierFromNode / markSymbolOfAliasDeclarationIfTypeOnly /
//    resolveExternalModuleName / getCannotResolveModuleNameErrorForSpecificModule / resolveExternalModuleNameWorker /
//    getExternalModuleFileFromDeclaration）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) 包级函数 → 同一类的 static 成员：`getModuleSpecifierFromNode` → `GetModuleSpecifierFromNode`。
// 2) `panic("Unhandled case in …: " + node.Kind.String())` → `Debug.Fail(...)` + `throw new UnreachableException()`
//    （`Debug.Fail` 无 `[DoesNotReturn]`；`Kind.String()` 是 Ast 的 stringer 扩展方法）。
// 3) **Worker 二次冲突**（Main 已裁定，见 `artifacts/checker-method-collisions.md` 追加项）：Go 里
//    `resolveExternalModuleName` 与 `resolveExternalModuleNameWorker` 同名族，C# 命名是
//    `ResolveExternalModuleNameWorker`（未导出门面）与 **`ResolveExternalModuleNameWorkerImpl`**（Go 自带的 Worker）。
//    因此 Go 的 `c.resolveExternalModuleName(...)` → `ResolveExternalModuleNameWorker(...)`；
//    Go 的 `c.resolveExternalModuleNameWorker(...)` → `ResolveExternalModuleNameWorkerImpl(...)`。
// 4) `core.IfElse(ignoreErrors, nil, errorMessage)` → `IfElse(ignoreErrors, null, errorMessage)`（`Message?`）。
// 5) `core.NodeCoreModules()[moduleName.Text()]`（Go map 取值为真；C# 是 `Dictionary<string, bool>` 字段，
//    索引器缺键会抛）→ `NodeCoreModules.GetValueOrDefault(moduleName.Text())`（缺键 false，语义与 Go 一致）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getModuleSpecifierForImportOrExport(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? GetModuleSpecifierForImportOrExport(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ImportClause:
                return GetModuleSpecifierFromNode(node.Parent!);
            case Kind.ImportEqualsDeclaration:
                if (IsExternalModuleReference(node.AsImportEqualsDeclaration()!.ModuleReference!))
                {
                    return node.AsImportEqualsDeclaration()!.ModuleReference!.Expression();
                }

                return null;
            case Kind.NamespaceImport:
                return GetModuleSpecifierFromNode(node.Parent!.Parent!);
            case Kind.ImportSpecifier:
                return GetModuleSpecifierFromNode(node.Parent!.Parent!.Parent!);
            case Kind.NamespaceExport:
                return GetModuleSpecifierFromNode(node.Parent!);
            case Kind.ExportSpecifier:
                return GetModuleSpecifierFromNode(node.Parent!.Parent!);
        }

        Debug.Fail("Unhandled case in getModuleSpecifierForImportOrExport");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go（包级）: <c>func getModuleSpecifierFromNode(node *ast.Node) *ast.Node</c>。</summary>
    internal static Node? GetModuleSpecifierFromNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ImportDeclaration:
            case Kind.JSImportDeclaration:
                return node.ModuleSpecifier();
            case Kind.ExportDeclaration:
                return node.ModuleSpecifier();
        }

        Debug.Fail("Unhandled case in getModuleSpecifierFromNode: " + node.Kind.String());
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>
    /// Marks a symbol as type-only if its declaration is syntactically type-only.
    /// If it is not itself marked type-only, but resolves to a type-only alias
    /// somewhere in its resolution chain, save a reference to the type-only alias declaration
    /// so the alias _not_ marked type-only can be identified as _transitively_ type-only.
    /// <para>
    /// This function is called on each alias declaration that could be type-only or resolve to
    /// another type-only alias during `resolveAlias`, so that later, when an alias is used in a
    /// JS-emitting expression, we can quickly determine if that symbol is effectively type-only
    /// and issue an error if so.
    /// </para>
    /// </summary>
    /// <param name="aliasDeclaration">The alias declaration not marked as type-only</param>
    /// <param name="exportStarDeclaration">
    /// The starred export declaration that the alias resolves through (Go 的 `exportStarDeclaration`）。
    /// </param>
    internal bool MarkSymbolOfAliasDeclarationIfTypeOnly(Node? aliasDeclaration, Node? exportStarDeclaration)
    {
        if (aliasDeclaration is null || !IsDeclarationNode(aliasDeclaration))
        {
            return false;
        }

        // If the declaration itself is type-only, mark it and return. No need to check what it resolves to.
        var sourceSymbol = GetSymbolOfDeclaration(aliasDeclaration);
        var links = _aliasSymbolLinks.Get(sourceSymbol!);
        if (links._typeOnlyDeclaration is null && IsTypeOnlyImportOrExportDeclaration(aliasDeclaration))
        {
            links._typeOnlyDeclaration = aliasDeclaration;
            return true;
        }

        if (links._typeOnlyDeclaration is null && exportStarDeclaration is not null)
        {
            links._typeOnlyDeclaration = exportStarDeclaration;
            return true;
        }

        return links._typeOnlyDeclaration is not null;
    }

    /// <summary>Go: <c>func (c *Checker) resolveExternalModuleName(location *ast.Node, moduleReferenceExpression *ast.Node, ignoreErrors bool, importAttributesType *Type) *ast.Symbol</c>。</summary>
    internal Symbol? ResolveExternalModuleNameWorker(Node location, Node moduleReferenceExpression, bool ignoreErrors, Type? importAttributesType)
    {
        var errorMessage = GetCannotResolveModuleNameErrorForSpecificModule(moduleReferenceExpression);
        errorMessage ??= Cannot_find_module_0_or_its_corresponding_type_declarations;
        ignoreErrors = ignoreErrors || _compilerOptions.NoCheck.IsTrue();
        return ResolveExternalModuleNameWorkerImpl(location, moduleReferenceExpression, IfElse(ignoreErrors, null, errorMessage), ignoreErrors, false /*isForAugmentation*/, importAttributesType);
    }

    /// <summary>Go: <c>func (c *Checker) getCannotResolveModuleNameErrorForSpecificModule(moduleName *ast.Node) *diagnostics.Message</c>。</summary>
    internal Message? GetCannotResolveModuleNameErrorForSpecificModule(Node moduleName)
    {
        if (IsStringLiteral(moduleName))
        {
            if (NodeCoreModules.GetValueOrDefault(moduleName.Text()))
            {
                if (_compilerOptions.UsesWildcardTypes())
                {
                    return Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode;
                }

                return Cannot_find_name_0_Do_you_need_to_install_type_definitions_for_node_Try_npm_i_save_dev_types_Slashnode_and_then_add_node_to_the_types_field_in_your_tsconfig;
            }
        }

        return null;
    }

    /// <summary>
    /// Go: <c>func (c *Checker) resolveExternalModuleNameWorker(location, moduleReferenceExpression *ast.Node, moduleNotFoundError *diagnostics.Message, ignoreErrors bool, isForAugmentation bool, importAttributesType *Type) *ast.Symbol</c>。
    /// C# 名加 `Impl` 后缀（Worker 二次冲突，见文件头 3）。
    /// </summary>
    internal Symbol? ResolveExternalModuleNameWorkerImpl(Node location, Node moduleReferenceExpression, Message? moduleNotFoundError, bool ignoreErrors, bool isForAugmentation, Type? importAttributesType)
    {
        if (IsStringLiteralLike(moduleReferenceExpression))
        {
            return ResolveExternalModule(location, moduleReferenceExpression.Text(), moduleNotFoundError, IfElse(!ignoreErrors, moduleReferenceExpression, null), isForAugmentation, importAttributesType);
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getExternalModuleFileFromDeclaration(declaration *ast.Node) *ast.SourceFile</c>。</summary>
    internal SourceFile? GetExternalModuleFileFromDeclaration(Node declaration)
    {
        Node? specifier;
        if (declaration.Kind == Kind.ModuleDeclaration)
        {
            if (IsStringLiteral(declaration.Name()!))
            {
                specifier = declaration.Name();
            }
            else
            {
                specifier = null;
            }
        }
        else
        {
            specifier = GetExternalModuleName(declaration);
        }

        Type? importAttributesType = null;
        if (HasImportAttributes(declaration))
        {
            importAttributesType = GetTypeFromImportAttributes(GetImportAttributes(declaration));
        }

        // TODO: GH#18217
        var moduleSymbol = ResolveExternalModuleNameWorkerImpl(specifier!, specifier!, null, false, false, importAttributesType);
        if (moduleSymbol is null)
        {
            return null;
        }

        var decl = GetDeclarationOfKind(moduleSymbol, Kind.SourceFile);
        if (decl is null)
        {
            return null;
        }

        return decl.AsSourceFile();
    }
}
