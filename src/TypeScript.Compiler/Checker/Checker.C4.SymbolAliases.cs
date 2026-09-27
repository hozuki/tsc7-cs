// ported from: internal/checker/checker.go:14569-14900
//   （cloneSymbol / getMergedSymbol / getParentOfSymbol / recordMergedSymbol / getSymbolIfSameReference /
//    getExportSymbolOfValueSymbolIfExported / getSymbolOfDeclaration / getSymbolOfNode / getLateBoundSymbol /
//    resolveSymbol / resolveSymbolEx / getTargetOfImportEqualsDeclaration / resolveExternalModuleTypeByLiteral /
//    getSymbolOfPartOfRightHandSideOfImportEquals / checkAndReportErrorForResolvingImportAliasToTypeOnlySymbol /
//    getTypeOnlyDeclarationOfEntityName / getTargetOfImportClause / getTargetOfModuleDefault / reportNonDefaultExport /
//    resolveExportByName / getTargetOfNamespaceImport / getTargetOfNamespaceExport / getTargetOfImportSpecifier）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `maps.Clone(symbol.Members)`（Go 的 maps.Clone：nil → nil）→ `symbol.Members is null ? null : new SymbolTable(symbol.Members)`
//    （`SymbolTable : Dictionary<string, Symbol>`，用拷贝构造；nil 语义与 Go 一致）。
// 2) `c.mergedSymbols[symbol]`（Go map 读零值 nil）→ `GetValueOrDefault(symbol)`；写仍是索引器。
// 3) `c.resolveAlias(symbol)` → `ResolveAliasWorker(symbol)`（未导出者改名，`artifacts/checker-method-collisions.md`；
//    导出的 `ResolveAlias(Symbol?, out Symbol?)` 在 C5 的 Checker.C5.LateBind.cs）。
// 4) 包级函数 → 同一个类的 static 成员：`createDiagnosticForNode` → `CreateDiagnosticForNode`（本片 Checker.C4.Symbols.cs）。
// 5) `debug.Assert(...)` → `Debug.Assert(...)`。
// 6) `ast.SymbolTable` 的索引读（Go 零值 nil）→ `GetValueOrDefault`。
// 7) 未导出成员命中改名的照写：`getMergedSymbol` → `GetMergedSymbolWorker`（本文件定义）、
//    `getTypeOnlyAliasDeclaration` → `GetTypeOnlyAliasDeclarationWorker`、`resolveExternalModuleName` →
//    `ResolveExternalModuleNameWorker`、`resolveExternalModuleSymbol` → `ResolveExternalModuleSymbolWorker`、
//    `getTypeOfSymbol` → `GetTypeOfSymbolWorker`、`symbolToString` → `SymbolToStringWorker`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) cloneSymbol(symbol *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol CloneSymbol(Symbol symbol)
    {
        var result = NewSymbol(symbol.Flags, symbol.Name);

        // Force reallocation if anything is ever appended to declarations
        result.Declarations = symbol.Declarations![0..symbol.Declarations!.Length];
        result.Parent = symbol.Parent;
        result.ValueDeclaration = symbol.ValueDeclaration;
        result.Members = symbol.Members is null ? null : CloneSymbolTable(symbol.Members);
        result.Exports = symbol.Exports is null ? null : CloneSymbolTable(symbol.Exports);
        RecordMergedSymbol(result, symbol);
        return result;
    }

    /// <summary>Go: <c>func (c *Checker) getMergedSymbol(symbol *ast.Symbol) *ast.Symbol</c>（C# 名加 Worker 后缀，见文件头 7）。</summary>
    internal Symbol? GetMergedSymbolWorker(Symbol? symbol)
    {
        if (symbol is not null)
        {
            var merged = _mergedSymbols.GetValueOrDefault(symbol);
            if (merged is not null)
            {
                return merged;
            }
        }

        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) getParentOfSymbol(symbol *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol? GetParentOfSymbol(Symbol symbol)
    {
        if (symbol.Parent is not null)
        {
            return GetMergedSymbolWorker(GetLateBoundSymbol(symbol.Parent));
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) recordMergedSymbol(target, source *ast.Symbol)</c>。</summary>
    internal void RecordMergedSymbol(Symbol target, Symbol source)
    {
        _mergedSymbols[source] = target;
    }

    /// <summary>Go: <c>func (c *Checker) getSymbolIfSameReference(s1, s2 *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol? GetSymbolIfSameReference(Symbol s1, Symbol s2)
    {
        if (GetMergedSymbolWorker(ResolveSymbol(GetMergedSymbolWorker(s1)!)) == GetMergedSymbolWorker(ResolveSymbol(GetMergedSymbolWorker(s2)!)))
        {
            return s1;
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getExportSymbolOfValueSymbolIfExported(symbol *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol? GetExportSymbolOfValueSymbolIfExported(Symbol? symbol)
    {
        if (symbol is not null && (symbol.Flags & SymbolFlags.ExportValue) != 0 && symbol.ExportSymbol is not null)
        {
            symbol = symbol.ExportSymbol;
        }

        return GetMergedSymbolWorker(symbol);
    }

    /// <summary>Go: <c>func (c *Checker) getSymbolOfDeclaration(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetSymbolOfDeclaration(Node node)
    {
        var symbol = node.Symbol;
        if (symbol is not null)
        {
            return GetMergedSymbolWorker(GetLateBoundSymbol(symbol));
        }

        return null;
    }

    // Get the merged symbol for a node. If you know the node is a `Declaration`, it is more type safe to
    // use use `getSymbolOfDeclaration` instead.
    /// <summary>Go: <c>func (c *Checker) getSymbolOfNode(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetSymbolOfNode(Node node)
    {
        var data = node.DeclarationData();
        if (data is not null && data.Symbol is not null)
        {
            return GetMergedSymbolWorker(GetLateBoundSymbol(data.Symbol));
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getLateBoundSymbol(symbol *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol GetLateBoundSymbol(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.ClassMember) == 0 || symbol.Name != InternalSymbolNameComputed)
        {
            return symbol;
        }

        var links = _lateBoundLinks.Get(symbol);
        if (links._lateSymbol is null && Some(symbol.Declarations, HasLateBindableName))
        {
            // force late binding of members/exports. This will set the late-bound symbol
            var parent = GetMergedSymbolWorker(symbol.Parent)!;
            if (Some(symbol.Declarations, HasStaticModifier))
            {
                GetExportsOfSymbol(parent);
            }
            else
            {
                GetMembersOfSymbol(parent);
            }
        }

        links._lateSymbol ??= symbol;
        return links._lateSymbol!;
    }

    /// <summary>Go: <c>func (c *Checker) resolveSymbol(symbol *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol? ResolveSymbol(Symbol? symbol)
    {
        return ResolveSymbolEx(symbol, false /*dontResolveAlias*/);
    }

    /// <summary>Go: <c>func (c *Checker) resolveSymbolEx(symbol *ast.Symbol, dontResolveAlias bool) *ast.Symbol</c>。</summary>
    internal Symbol? ResolveSymbolEx(Symbol? symbol, bool dontResolveAlias)
    {
        if (!dontResolveAlias && IsNonLocalAlias(symbol, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace))
        {
            return ResolveAliasWorker(symbol!);
        }

        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfImportEqualsDeclaration(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfImportEqualsDeclaration(Node node)
    {
        // Node is ImportEqualsDeclaration | VariableDeclaration
        if (IsVariableDeclaration(node) || node.AsImportEqualsDeclaration()!.ModuleReference!.Kind == Kind.ExternalModuleReference)
        {
            var moduleReference = GetExternalModuleRequireArgument(node);
            moduleReference ??= GetExternalModuleImportEqualsDeclarationExpression(node);
            var immediate = ResolveExternalModuleNameWorker(node, moduleReference!, false /*ignoreErrors*/, null /*importAttributesType*/);
            var resolved = ResolveExternalModuleSymbolWorker(immediate, true /*dontResolveAlias*/);
            if (resolved is not null && ModuleKind.Node20 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
            {
                var moduleExports = GetExportOfModule(resolved, InternalSymbolNameModuleExports, node, true /*dontResolveAlias*/);
                if (moduleExports is not null)
                {
                    return moduleExports;
                }
            }

            MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
            return resolved;
        }

        var resolvedRef = GetSymbolOfPartOfRightHandSideOfImportEquals(node.AsImportEqualsDeclaration()!.ModuleReference!);
        CheckAndReportErrorForResolvingImportAliasToTypeOnlySymbol(node, resolvedRef);
        return resolvedRef;
    }

    /// <summary>Go: <c>func (c *Checker) resolveExternalModuleTypeByLiteral(name *ast.Node) *Type</c>。</summary>
    internal Type ResolveExternalModuleTypeByLiteral(Node name)
    {
        var moduleSym = ResolveExternalModuleNameWorker(name, name, false /*ignoreErrors*/, null /*importAttributesType*/);
        if (moduleSym is not null)
        {
            var resolvedModuleSymbol = ResolveExternalModuleSymbolWorker(moduleSym, false /*dontResolveAlias*/);
            if (resolvedModuleSymbol is not null)
            {
                return GetTypeOfSymbolWorker(resolvedModuleSymbol);
            }
        }

        return _anyType!;
    }

    // This function is only for imports with entity names
    /// <summary>Go: <c>func (c *Checker) getSymbolOfPartOfRightHandSideOfImportEquals(entityName *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetSymbolOfPartOfRightHandSideOfImportEquals(Node entityName)
    {
        // There are three things we might try to look for. In the following examples,
        // the search term is enclosed in |...|:
        //
        //     import a = |b|; // Namespace
        //     import a = |b.c|; // Value, type, namespace
        //     import a = |b.c|.d; // Namespace
        if (entityName.Kind == Kind.Identifier && IsRightSideOfQualifiedNameOrPropertyAccess(entityName))
        {
            entityName = entityName.Parent!; // QualifiedName
        }

        // Check for case 1 and 3 in the above example
        if (entityName.Kind == Kind.Identifier || entityName.Parent!.Kind == Kind.QualifiedName)
        {
            return ResolveEntityName(entityName, SymbolFlags.Namespace, false /*ignoreErrors*/, true /*dontResolveAlias*/, null /*location*/);
        }

        // Case 2 in above example
        // entityName.kind could be a QualifiedName or a Missing identifier
        Debug.Assert(entityName.Parent!.Kind == Kind.ImportEqualsDeclaration);
        return ResolveEntityName(entityName, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, false /*ignoreErrors*/, true /*dontResolveAlias*/, null /*location*/);
    }

    /// <summary>Go: <c>func (c *Checker) checkAndReportErrorForResolvingImportAliasToTypeOnlySymbol(node *ast.Node, resolved *ast.Symbol)</c>。</summary>
    internal void CheckAndReportErrorForResolvingImportAliasToTypeOnlySymbol(Node node, Symbol? resolved)
    {
        var decl = node.AsImportEqualsDeclaration()!;
        var name = decl.ModuleReference;
        while (true)
        {
            var typeOnlyDeclaration = GetTypeOnlyDeclarationOfEntityName(name!);
            if (typeOnlyDeclaration is not null)
            {
                var isExport = NodeKindIs(typeOnlyDeclaration, Kind.ExportSpecifier, Kind.ExportDeclaration);
                var message = IfElse(
                    isExport,
                    An_import_alias_cannot_reference_a_declaration_that_was_exported_using_export_type,
                    An_import_alias_cannot_reference_a_declaration_that_was_imported_using_import_type);
                var relatedMessage = IfElse(
                    isExport,
                    X_0_was_exported_here,
                    X_0_was_imported_here);

                // TODO: how to get name for export *?
                var exportName = "*";
                if (!IsExportDeclaration(typeOnlyDeclaration))
                {
                    exportName = typeOnlyDeclaration.Name()!.Text();
                }

                Error(decl.ModuleReference, message).AddRelatedInfo(CreateDiagnosticForNode(typeOnlyDeclaration, relatedMessage, exportName));
                break;
            }

            if (IsIdentifier(name!))
            {
                break;
            }

            name = name!.AsQualifiedName()!.Left;
        }
    }

    /// <summary>Go: <c>func (c *Checker) getTypeOnlyDeclarationOfEntityName(name *ast.Node) *ast.Node</c>。</summary>
    internal Node? GetTypeOnlyDeclarationOfEntityName(Node name)
    {
        var symbol = ResolveEntityName(name, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, true /*ignoreErrors*/, true /*dontResolveAlias*/, null /*location*/);
        if (symbol is not null)
        {
            return GetTypeOnlyAliasDeclarationWorker(symbol);
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfImportClause(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfImportClause(Node node)
    {
        var moduleSymbol = ResolveExternalModuleNameWorker(node, GetModuleSpecifierFromNode(node.Parent!)!, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(node.Parent!)));
        if (moduleSymbol is not null)
        {
            return GetTargetOfModuleDefault(moduleSymbol, node, true /*dontResolveAlias*/);
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfModuleDefault(moduleSymbol *ast.Symbol, node *ast.Node, dontResolveAlias bool) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfModuleDefault(Symbol moduleSymbol, Node node, bool dontResolveAlias)
    {
        var file = Find(moduleSymbol.Declarations, IsSourceFile);
        var specifier = GetModuleSpecifierForImportOrExport(node);
        Symbol? exportDefaultSymbol = null;
        Symbol? exportModuleDotExportsSymbol = null;
        if (IsShorthandAmbientModuleSymbol(moduleSymbol))
        {
            // !!! exportDefaultSymbol = moduleSymbol
            // Does nothing?
        }
        else if (file is not null && specifier is not null
            && ModuleKind.Node20 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext
            && GetEmitSyntaxForModuleSpecifierExpression(specifier) == ModuleKind.CommonJS
            && _program.GetImpliedNodeFormatForEmit(file.AsSourceFile()!) == ModuleKind.ESNext)
        {
            exportModuleDotExportsSymbol = ResolveExportByName(moduleSymbol, InternalSymbolNameModuleExports, node, dontResolveAlias);
        }

        if (exportModuleDotExportsSymbol is not null)
        {
            // We have a transpiled default import where the `require` resolves to an ES module with a `module.exports` named
            // export. With `esModuleInterop` (always enabled), this will work:
            //
            // const dep_1 = __importDefault(require("./dep.mjs")); // wraps like { default: require("./dep.mjs") }
            // dep_1.default; // require("./dep.mjs") -> the `module.exports` export value
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
            return exportModuleDotExportsSymbol;
        }
        else
        {
            exportDefaultSymbol = ResolveExportByName(moduleSymbol, InternalSymbolNameDefault, node, dontResolveAlias);
        }

        if (specifier is null)
        {
            return exportDefaultSymbol;
        }

        // node is ImportClause | ImportSpecifier | ExportSpecifier
        Node? attributes = null;
        if (IsImportClause(node))
        {
            attributes = GetImportAttributes(node.Parent!);
        }
        else if (IsImportSpecifier(node))
        {
            attributes = GetImportAttributes(node.Parent!.Parent!.Parent!);
        }
        else if (IsExportSpecifier(node))
        {
            attributes = GetImportAttributes(node.Parent!.Parent!);
        }

        var hasDefaultOnly = IsOnlyImportableAsDefault(specifier, moduleSymbol, GetTypeFromImportAttributes(attributes));
        var hasSyntheticDefault = CanHaveSyntheticDefault(file, moduleSymbol, dontResolveAlias, specifier);
        if (exportDefaultSymbol is null && !hasSyntheticDefault && !hasDefaultOnly)
        {
            if (IsImportClause(node))
            {
                ReportNonDefaultExport(moduleSymbol, node);
            }
            else
            {
                Node? name;
                if (IsImportOrExportSpecifier(node))
                {
                    name = node.PropertyNameOrName();
                }
                else
                {
                    name = node.Name();
                }

                ErrorNoModuleMemberSymbol(moduleSymbol, moduleSymbol, node, name!);
            }
        }
        else if (hasSyntheticDefault || hasDefaultOnly)
        {
            // per emit behavior, a synthetic default overrides a "real" .default member if `__esModule` is not present
            var resolved = ResolveExternalModuleSymbolWorker(moduleSymbol, dontResolveAlias);
            resolved ??= ResolveSymbolEx(moduleSymbol, dontResolveAlias);
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
            return resolved;
        }

        MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
        return exportDefaultSymbol;
    }

    /// <summary>Go: <c>func (c *Checker) reportNonDefaultExport(moduleSymbol *ast.Symbol, node *ast.Node)</c>。</summary>
    internal void ReportNonDefaultExport(Symbol moduleSymbol, Node node)
    {
        if (moduleSymbol.Exports is not null && moduleSymbol.Exports.GetValueOrDefault(node.Symbol!.Name) is not null)
        {
            Error(node, Module_0_has_no_default_export_Did_you_mean_to_use_import_1_from_0_instead, SymbolToStringWorker(moduleSymbol), SymbolToStringWorker(node.Symbol!));
        }
        else
        {
            var diagnostic = Error(node.Name(), Module_0_has_no_default_export, SymbolToStringWorker(moduleSymbol));
            Symbol? exportStar = null;
            if (moduleSymbol.Exports is not null)
            {
                exportStar = moduleSymbol.Exports.GetValueOrDefault(InternalSymbolNameExportStar);
            }

            if (exportStar is not null)
            {
                var defaultExport = Find(exportStar.Declarations, decl =>
                {
                    if (!(IsExportDeclaration(decl) && decl.ModuleSpecifier() is not null))
                    {
                        return false;
                    }

                    var resolvedExternalModuleName = ResolveExternalModuleNameWorker(decl, decl.ModuleSpecifier()!, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(decl)));
                    return resolvedExternalModuleName is not null && resolvedExternalModuleName.Exports?.GetValueOrDefault(InternalSymbolNameDefault) is not null;
                });
                if (defaultExport is not null)
                {
                    diagnostic.AddRelatedInfo(CreateDiagnosticForNode(defaultExport, X_export_Asterisk_does_not_re_export_a_default));
                }
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) resolveExportByName(moduleSymbol *ast.Symbol, name string, sourceNode *ast.Node, dontResolveAlias bool) *ast.Symbol</c>。</summary>
    internal Symbol? ResolveExportByName(Symbol moduleSymbol, string name, Node? sourceNode, bool dontResolveAlias)
    {
        var exportValue = moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals);
        Symbol? exportSymbol;
        if (exportValue is not null)
        {
            exportSymbol = GetPropertyOfTypeEx(GetTypeOfSymbolWorker(exportValue), name, true /*skipObjectFunctionPropertyAugment*/, false /*includeTypeOnlyMembers*/);
        }
        else
        {
            exportSymbol = moduleSymbol.Exports?.GetValueOrDefault(name);
        }

        var resolved = ResolveSymbolEx(exportSymbol, dontResolveAlias);
        MarkSymbolOfAliasDeclarationIfTypeOnly(sourceNode, null);
        return resolved;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfNamespaceImport(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfNamespaceImport(Node node)
    {
        var moduleSpecifier = GetModuleSpecifierForImportOrExport(node);
        var immediate = ResolveExternalModuleNameWorker(node, moduleSpecifier!, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(node.Parent!.Parent!)));
        var resolved = ResolveESModuleSymbol(immediate, node, moduleSpecifier);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
        return resolved;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfNamespaceExport(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfNamespaceExport(Node node)
    {
        var moduleSpecifier = GetModuleSpecifierForImportOrExport(node);
        if (moduleSpecifier is not null)
        {
            var immediate = ResolveExternalModuleNameWorker(node, moduleSpecifier, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(node.Parent!)));
            var resolved = ResolveESModuleSymbol(immediate, node, moduleSpecifier);
            MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
            return resolved;
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getTargetOfImportSpecifier(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfImportSpecifier(Node node)
    {
        var name = node.PropertyNameOrName();
        if (IsImportSpecifier(node) && ModuleExportNameIsDefault(name!))
        {
            var specifier = GetModuleSpecifierForImportOrExport(node);
            if (specifier is not null)
            {
                var moduleSymbol = ResolveExternalModuleNameWorker(node, specifier, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(node.Parent!.Parent!.Parent!)));
                if (moduleSymbol is not null)
                {
                    return GetTargetOfModuleDefault(moduleSymbol, node, true /*dontResolveAlias*/);
                }
            }
        }

        var root = node.Parent!.Parent!.Parent!; // ImportDeclaration
        if (IsBindingElement(node))
        {
            root = GetRootDeclaration(node);
        }

        var resolved = GetExternalModuleMember(root!, node, true /*dontResolveAlias*/);
        MarkSymbolOfAliasDeclarationIfTypeOnly(node, null);
        return resolved;
    }
}
