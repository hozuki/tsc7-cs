// ported from: internal/checker/checker.go:16053-16321
//   （getTargetOfAliasDeclaration / resolveEntityName / resolveQualifiedName / tryGetQualifiedNameAsValue /
//    getSuggestedSymbolForNonexistentModule / getFullyQualifiedName / getExportsOfSymbol /
//    getResolvedMembersOrExportsOfSymbol）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `panic("Unhandled case in getTargetOfAliasDeclaration: " + node.Kind.String())` / `panic("Unknown entity name kind")`
//    → `Debug.Fail(...)` + `throw new UnreachableException()`（`Debug.Fail` 无 `[DoesNotReturn]`）。
// 2) 未导出成员命中 `artifacts/checker-method-collisions.md` 的改名照写：`getExportsOfModule` →
//    `GetExportsOfModuleWorker`、`getExportsOfModuleWorker` → **`GetExportsOfModuleWorkerImpl`**（Worker 二次冲突；
//    ChkC5 的签名是 `(SymbolTable Exports, Dictionary<string, Node>? TypeOnlyExportStarMap)`）、
//    `getFullyQualifiedName` → `GetFullyQualifiedNameWorker`（本文件定义）、`symbolToString` → `SymbolToStringWorker`、
//    `symbolToStringEx` → `SymbolToStringExWorker`、`resolveExternalModuleName` → `ResolveExternalModuleNameWorker`、
//    `resolveExternalModuleSymbol` → `ResolveExternalModuleSymbolWorker`、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getPropertyOfType` → `GetPropertyOfTypeWorker`。
// 3) **`combineSymbolTables` 的 nil 折叠**：Go 的 `SymbolTable` 是 map，nil 与空表在所有映射操作上等价；C# 的
//    `SymbolTable` 是 class，null 不能用。故 `CombineSymbolTables` 在结果会是 nil 时返回**空表**（并在
//    `GetResolvedMembersOrExportsOfSymbol` 里缓存之）：对外可观察行为与 Go 一致（所有下游都是映射读/遍历），
//    副作用是"结果为空时不再重复解析"（Go 因为缓存 nil 会重算），这只影响性能。
// 4) `maps.Values(c.getExportsOfModule(targetModule))` → `GetExportsOfModuleWorker(targetModule).Values`
//    （`SymbolTable : Dictionary<string, Symbol>` → `IEnumerable<Symbol>`，对上 `GetSpellingSuggestionForName` 的
//    `IEnumerable<Symbol>` 参数）。
// 5) `c.resolveAlias(x)` → `ResolveAliasWorker(x)`（ChkC5 定义，返回非空 `Symbol`）。
// 6) `MembersOrExportsResolutionKindResolvedExports` → `MembersOrExportsResolutionKind.ResolvedExports`
//    （types.go 的 `MembersAndExportsLinks` 有 `this[MembersOrExportsResolutionKind]` 索引器）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getTargetOfAliasDeclaration(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? GetTargetOfAliasDeclaration(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        switch (node.Kind)
        {
            case Kind.ImportEqualsDeclaration:
            case Kind.VariableDeclaration:
                return GetTargetOfImportEqualsDeclaration(node);
            case Kind.ImportClause:
                return GetTargetOfImportClause(node);
            case Kind.NamespaceImport:
                return GetTargetOfNamespaceImport(node);
            case Kind.NamespaceExport:
                return GetTargetOfNamespaceExport(node);
            case Kind.ImportSpecifier:
            case Kind.BindingElement:
                return GetTargetOfImportSpecifier(node);
            case Kind.ExportSpecifier:
                return GetTargetOfExportSpecifier(node, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, true /*dontRecursivelyResolve*/);
            case Kind.ExportAssignment:
                return GetTargetOfExportAssignment(node);
            case Kind.BinaryExpression:
                return GetTargetOfBinaryExpression(node);
            case Kind.NamespaceExportDeclaration:
                return GetTargetOfNamespaceExportDeclaration(node);
            case Kind.ShorthandPropertyAssignment:
                return ResolveEntityName(node.AsShorthandPropertyAssignment()!.Name()!, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace, true /*ignoreErrors*/, true /*dontRecursivelyResolve*/, null /*location*/);
            case Kind.PropertyAssignment:
                return GetTargetOfAliasLikeExpression(node.Initializer()!);
            case Kind.ElementAccessExpression:
            case Kind.PropertyAccessExpression:
                return GetTargetOfAccessExpression(node);
        }

        Debug.Fail("Unhandled case in getTargetOfAliasDeclaration: " + node.Kind.String());
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Resolves a qualified name and any involved aliases.</summary>
    internal Symbol? ResolveEntityName(Node name, SymbolFlags meaning, bool ignoreErrors, bool dontResolveAlias, Node? location)
    {
        if (NodeIsMissing(name))
        {
            return null;
        }

        Symbol? symbol;
        switch (name.Kind)
        {
            case Kind.Identifier:
                Message? message = null;
                if (!ignoreErrors)
                {
                    if (meaning == SymbolFlags.Namespace || NodeIsSynthesized(name))
                    {
                        message = Cannot_find_namespace_0;
                    }
                    else
                    {
                        message = GetCannotFindNameDiagnosticForName(GetFirstIdentifier(name)!);
                    }
                }

                var resolveLocation = location;
                resolveLocation ??= name;
                if (meaning == SymbolFlags.Namespace)
                {
                    symbol = GetMergedSymbolWorker(_resolveName(resolveLocation, name.Text(), meaning, null, true /*isUse*/, false /*excludeGlobals*/));
                    if (symbol is null)
                    {
                        var alias = GetMergedSymbolWorker(_resolveName(resolveLocation, name.Text(), SymbolFlags.Alias, null, true /*isUse*/, false /*excludeGlobals*/));
                        if (alias is not null && alias.Name == InternalSymbolNameExportEquals)
                        {
                            // resolve typedefs exported from commonjs, stored on the module symbol
                            symbol = alias.Parent;
                        }
                    }

                    if (symbol is null && message is not null)
                    {
                        _resolveName(resolveLocation, name.Text(), meaning, message, true /*isUse*/, false /*excludeGlobals*/);
                    }
                }
                else
                {
                    symbol = GetMergedSymbolWorker(_resolveName(resolveLocation, name.Text(), meaning, message, true /*isUse*/, false /*excludeGlobals*/));
                }

                break;
            case Kind.QualifiedName:
                var qualified = name.AsQualifiedName()!;
                symbol = ResolveQualifiedName(name, qualified.Left!, qualified.Right!, meaning, ignoreErrors, location);
                break;
            case Kind.PropertyAccessExpression:
                var access = name.AsPropertyAccessExpression()!;
                symbol = ResolveQualifiedName(name, access.Expression!, access.Name()!, meaning, ignoreErrors, location);
                break;
            default:
                Debug.Fail("Unknown entity name kind");
                throw new System.Diagnostics.UnreachableException();
        }

        if (symbol is not null && symbol != _unknownSymbol)
        {
            if (!NodeIsSynthesized(name) && IsEntityName(name)
                && ((symbol.Flags & SymbolFlags.Alias) != 0 || (name.Parent is not null && name.Parent.Kind == Kind.ExportAssignment)))
            {
                MarkSymbolOfAliasDeclarationIfTypeOnly(GetAliasDeclarationFromName(name), null);
            }

            // We know a symbol with the given meaning exists along the alias chain, so resolve until we find it.
            while ((symbol.Flags & meaning) == 0 && !dontResolveAlias && (symbol.Flags & SymbolFlags.Alias) != 0)
            {
                symbol = ResolveAliasWorker(symbol);
            }
        }

        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) resolveQualifiedName(name, left, right *ast.Node, meaning ast.SymbolFlags, ignoreErrors bool, location *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? ResolveQualifiedName(Node name, Node left, Node right, SymbolFlags meaning, bool ignoreErrors, Node? location)
    {
        var @namespace = ResolveEntityName(left, SymbolFlags.Namespace, ignoreErrors, false /*dontResolveAlias*/, location);
        if (@namespace is null || NodeIsMissing(right))
        {
            return null;
        }

        if (@namespace == _unknownSymbol)
        {
            return @namespace;
        }

        if (@namespace.ValueDeclaration is not null
            && IsInJSFile(@namespace.ValueDeclaration)
            && _compilerOptions.GetModuleResolutionKind() != ModuleResolutionKind.Bundler
            && IsVariableDeclaration(@namespace.ValueDeclaration)
            && @namespace.ValueDeclaration.Initializer() is not null
            && IsCommonJSRequire(@namespace.ValueDeclaration.Initializer()!))
        {
            var moduleName = @namespace.ValueDeclaration.Initializer()!.Arguments()![0];
            var moduleSym = ResolveExternalModuleNameWorker(moduleName, moduleName, false /*ignoreErrors*/, null);
            if (moduleSym is not null)
            {
                var resolvedModuleSymbol = ResolveExternalModuleSymbolWorker(moduleSym, false /*dontResolveAlias*/);
                if (resolvedModuleSymbol is not null)
                {
                    @namespace = resolvedModuleSymbol;
                }
            }
        }

        var text = right.Text();
        var symbol = GetMergedSymbolWorker(GetSymbol(GetExportsOfSymbol(@namespace), text, meaning));
        if (symbol is null && (@namespace.Flags & SymbolFlags.Alias) != 0)
        {
            // `namespace` can be resolved further if there was a symbol merge with a re-export
            symbol = GetMergedSymbolWorker(GetSymbol(GetExportsOfSymbol(ResolveAliasWorker(@namespace)), text, meaning));
        }

        if (symbol is null)
        {
            if (!ignoreErrors)
            {
                var namespaceName = GetFullyQualifiedNameWorker(@namespace, null /*containingLocation*/);
                var declarationName = DeclarationNameToString(right);
                var suggestionForNonexistentModule = GetSuggestedSymbolForNonexistentModule(right, @namespace);
                if (suggestionForNonexistentModule is not null)
                {
                    Error(right, X_0_has_no_exported_member_named_1_Did_you_mean_2, namespaceName, declarationName, SymbolToStringWorker(suggestionForNonexistentModule));
                    return null;
                }

                Node? containingQualifiedName = null;
                if (IsQualifiedName(name))
                {
                    containingQualifiedName = GetContainingQualifiedNameNode(name);
                }

                var canSuggestTypeof = _globalObjectType is not null
                    && (meaning & SymbolFlags.Type) != 0
                    && containingQualifiedName is not null
                    && !IsTypeOfExpression(containingQualifiedName.Parent!)
                    && TryGetQualifiedNameAsValue(containingQualifiedName) is not null;
                if (canSuggestTypeof)
                {
                    Error(containingQualifiedName, X_0_refers_to_a_value_but_is_being_used_as_a_type_here_Did_you_mean_typeof_0, EntityNameToString(containingQualifiedName!));
                    return null;
                }

                if ((meaning & SymbolFlags.Namespace) != 0)
                {
                    if (IsQualifiedName(name.Parent!))
                    {
                        var exportedTypeSymbol = GetMergedSymbolWorker(GetSymbol(GetExportsOfSymbol(@namespace), text, SymbolFlags.Type));
                        if (exportedTypeSymbol is not null)
                        {
                            var outerQualified = name.Parent!.AsQualifiedName()!;
                            Error(outerQualified.Right, Cannot_access_0_1_because_0_is_a_type_but_not_a_namespace_Did_you_mean_to_retrieve_the_type_of_the_property_1_in_0_with_0_1, SymbolToStringWorker(exportedTypeSymbol), outerQualified.Right!.Text());
                            return null;
                        }
                    }
                }

                Error(right, Namespace_0_has_no_exported_member_1, namespaceName, declarationName);
            }
        }

        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) tryGetQualifiedNameAsValue(node *ast.Node) *ast.Symbol</c>。</summary>
    internal Symbol? TryGetQualifiedNameAsValue(Node node)
    {
        var id = GetFirstIdentifier(node)!;
        var symbol = _resolveName(id, id.Text(), SymbolFlags.Value, null /*nameNotFoundMessage*/, true /*isUse*/, false /*excludeGlobals*/);
        if (symbol is null)
        {
            return null;
        }

        var n = id;
        while (IsQualifiedName(n.Parent!))
        {
            var t = GetTypeOfSymbolWorker(symbol);
            symbol = GetPropertyOfTypeWorker(t, n.Parent!.AsQualifiedName()!.Right!.Text());
            if (symbol is null)
            {
                return null;
            }

            n = n.Parent!;
        }

        return symbol;
    }

    /// <summary>Go: <c>func (c *Checker) getSuggestedSymbolForNonexistentModule(name *ast.Node, targetModule *ast.Symbol) *ast.Symbol</c>。</summary>
    internal Symbol? GetSuggestedSymbolForNonexistentModule(Node name, Symbol targetModule)
    {
        return GetSpellingSuggestionForName(name.Text(), GetExportsOfModuleWorker(targetModule).Values, SymbolFlags.ModuleMember);
    }

    /// <summary>Go: <c>func (c *Checker) getFullyQualifiedName(symbol *ast.Symbol, containingLocation *ast.Node) string</c>（C# 名加 Worker 后缀，见文件头 2）。</summary>
    internal string GetFullyQualifiedNameWorker(Symbol symbol, Node? containingLocation)
    {
        if (symbol.Parent is not null)
        {
            return GetFullyQualifiedNameWorker(symbol.Parent, containingLocation) + "." + SymbolToStringWorker(symbol);
        }

        return SymbolToStringExWorker(symbol, containingLocation, SymbolFlags.All, SymbolFormatFlags.DoNotIncludeSymbolChain | SymbolFormatFlags.AllowAnyNodeKind);
    }

    /// <summary>Go: <c>func (c *Checker) getExportsOfSymbol(symbol *ast.Symbol) ast.SymbolTable</c>。</summary>
    internal SymbolTable GetExportsOfSymbol(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.LateBindingContainer) != 0)
        {
            return GetResolvedMembersOrExportsOfSymbol(symbol, MembersOrExportsResolutionKind.ResolvedExports);
        }

        if ((symbol.Flags & SymbolFlags.Module) != 0)
        {
            return GetExportsOfModuleWorker(symbol);
        }

        return symbol.Exports ?? new SymbolTable();
    }

    /// <summary>Go: <c>func (c *Checker) getResolvedMembersOrExportsOfSymbol(symbol *ast.Symbol, resolutionKind MembersOrExportsResolutionKind) ast.SymbolTable</c>。</summary>
    internal SymbolTable GetResolvedMembersOrExportsOfSymbol(Symbol symbol, MembersOrExportsResolutionKind resolutionKind)
    {
        var links = _membersAndExportsLinks.Get(symbol);
        if (links[resolutionKind] is null)
        {
            var isStatic = resolutionKind == MembersOrExportsResolutionKind.ResolvedExports;
            var earlySymbols = symbol.Exports;
            if (!isStatic)
            {
                earlySymbols = symbol.Members;
            }
            else if ((symbol.Flags & SymbolFlags.Module) != 0)
            {
                (earlySymbols, _) = GetExportsOfModuleWorkerImpl(symbol);
            }

            links[resolutionKind] = earlySymbols;

            // Go 的 `earlySymbols` 可能是 nil map（读等价于空表）；C# 的 SymbolTable 是 class（null 不能用作映射）
            // → 传给 lateBind* 时折叠成空表（只读用途，语义一致）。
            var early = earlySymbols ?? new SymbolTable();

            // fill in any as-yet-unresolved late-bound members.
            SymbolTable? lateSymbols = null;
            // Go: `for _, decl := range symbol.Declarations`（checker.go:16261，nil 切片不迭代）
            foreach (var decl in symbol.Declarations ?? [])
            {
                foreach (var member in GetMembersOfDeclaration(decl) ?? [])
                {
                    if (isStatic == HasStaticModifier(member))
                    {
                        if (HasLateBindableName(member))
                        {
                            lateSymbols ??= new SymbolTable();
                            LateBindMember(symbol, early, lateSymbols, member);
                        }
                        else if (HasLateBindableIndexSignature(member))
                        {
                            lateSymbols ??= new SymbolTable();
                            LateBindIndexSignature(symbol, early, lateSymbols, member); // as LateBoundDeclaration | LateBoundBinaryExpressionDeclaration
                        }
                    }
                }
            }

            if (isStatic)
            {
                var assignmentSymbol = symbol.Exports?.GetValueOrDefault(InternalSymbolNameAssignmentDeclaration);
                if (assignmentSymbol is not null)
                {
                    // Go: `for _, member := range assignmentSymbol.Declarations`（checker.go:16281）
                    foreach (var member in assignmentSymbol.Declarations ?? [])
                    {
                        if (HasLateBindableName(member))
                        {
                            lateSymbols ??= new SymbolTable();
                            LateBindMember(symbol, early, lateSymbols, member);
                        }
                    }
                }
            }

            links[resolutionKind] = CombineSymbolTables(earlySymbols, lateSymbols);
        }

        return links[resolutionKind]!;
    }
}
