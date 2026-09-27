// ported from: internal/checker/symbolaccessibility.go
//
// 与 Go 的差异（逐条；其余与 Go 逐行对应）：
// 1) `symbolTableID`（Go 的 `type symbolTableID uint64`，本文件的未导出类型）→ C# 的 `SymbolTableId` 只读结构体
//    （Ast 的 `NodeId`/`SymbolId` 同款：`Value` 字段 + 双向隐式转换 + IEquatable，便于做 `Dictionary` 键；
//    Checker 的 `_symbolTableAliasCache` 字段类型已经是 `Dictionary<SymbolTableId, Symbol[]>`）。
//    Go 的 `stKind*` 常量 → `static readonly` 字段（C# 的 `const` 不能用于结构体类型；数值与 Go 逐位一致，
//    包括 `stKindMask = (iota-1) << stKindShift` 的 **4 << 61**）。
// 2) Go 的 `accessibleSymbolChainContext`（值传递）→ C# 的 `AccessibleSymbolChainContext` 类：Go 只在
//    `canQualifySymbol` 里新建一个并共享 `visitedSymbolTablesMap`（引用类型），所以引用语义与 Go 的值语义等价。
// 3) Go 的 `symbolTableIDFrom*` 包级函数 → 同一个 `partial class Checker` 的 `internal static` 成员
//    （docs/checker-plan.md §5.2）；同理 `hasNonGlobalAugmentationExternalModuleSymbol`/`getQualifiedLeftMeaning`/
//    `hasExternalModuleSymbol`/`isUMDExportSymbol`/`isNamespaceReexportDeclaration`/
//    `isPropertyOrMethodDeclarationSymbol`。
// 4) `slices.SortStableFunc(candidateChains, c.compareSymbolChains)` → C# 的 `List.Sort`/`Array.Sort` 不稳定，
//    这里手写插入排序保持 Go 的稳定性（候选链数量极小）。
// 5) `slices.Contains(candidates, sym)` → `List<T>.Contains`（都是引用相等）；`core.Some/MapNonNil/FirstNonNil/IfElse`
//    → `using static TypeScript.Core.Core` 后的裸名；`core.IfElse(a, x, nil)` → 三元 `a ? x : null`。
// 6) Go 的 `Dictionary`（`ast.SymbolTable`、map）读缺失键返回零值 → C# 用 `TryGetValue`/`GetValueOrDefault`
//    （索引器会抛异常）。Go 的 `len(x) > 0`（x 可能是 nil 切片）→ C# 的 `x is { Length: > 0 }` /
//    `x?.Length ?? 0`。
// 7) 同名方法冲突（artifacts/checker-method-collisions.md）：未导出的 `getAccessibleSymbolChain` →
//    `GetAccessibleSymbolChainWorker`（导出的 `GetAccessibleSymbolChain` 保留原名）；`compareSymbolChainsWorker`
//    在 Go 里本来就带 Worker 后缀 → `CompareSymbolChainsWorker`。
using System;
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Checker.Checker;
using static TypeScript.Core.Core;

namespace TypeScript.Checker;

/// <summary>
/// Go: <c>type symbolTableID uint64</c>。symbolTableID uniquely identifies a symbol table by encoding its source.
/// The high 3 bits encode the kind, and the remaining bits encode the NodeId or SymbolId of the source.
/// </summary>
internal readonly struct SymbolTableId : IEquatable<SymbolTableId>
{
    internal readonly ulong Value;

    internal SymbolTableId(ulong value)
    {
        Value = value;
    }

    public static implicit operator ulong(SymbolTableId id) => id.Value;

    public static implicit operator SymbolTableId(ulong value) => new(value);

    public static SymbolTableId operator |(SymbolTableId a, SymbolTableId b) => new(a.Value | b.Value);

    public static SymbolTableId operator &(SymbolTableId a, SymbolTableId b) => new(a.Value & b.Value);

    public bool Equals(SymbolTableId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SymbolTableId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Go: <c>type accessibleSymbolChainContext struct</c>。</summary>
internal sealed class AccessibleSymbolChainContext
{
    internal Symbol? _symbol;
    internal Node? _enclosingDeclaration;
    internal SymbolFlags _meaning;
    internal bool _useOnlyExternalAliasing;
    internal Dictionary<SymbolId, HashSet<SymbolTableId>> _visitedSymbolTablesMap = new();
}

/// <summary>Go 的 <c>func(symbolTable ast.SymbolTable, tableId symbolTableID, ignoreQualification bool, isLocalNameLookup bool, scopeNode *ast.Node) bool</c>。</summary>
internal delegate bool SymbolTableInScopeCallback(SymbolTable? symbolTable, SymbolTableId tableId, bool ignoreQualification, bool isLocalNameLookup, Node? scopeNode);

public sealed partial class Checker
{
    private const int _stKindShift = 61;

    private static readonly SymbolTableId _stKindLocals = new(0UL);
    private static readonly SymbolTableId _stKindExports = new(1UL << _stKindShift);
    private static readonly SymbolTableId _stKindMembers = new(2UL << _stKindShift);
    private static readonly SymbolTableId _stKindGlobals = new(3UL << _stKindShift);
    private static readonly SymbolTableId _stKindResolvedExports = new(4UL << _stKindShift);

    /// <summary>stKindMask extracts the kind bits from a symbolTableID（Go 的 <c>(iota - 1) &lt;&lt; stKindShift</c>，即 4 &lt;&lt; 61，逐位保留）。</summary>
    private static readonly SymbolTableId _stKindMask = new(4UL << _stKindShift);

    private static SymbolTableId SymbolTableIdFromLocals(Node node) => _stKindLocals | new SymbolTableId(GetNodeId(node));

    private static SymbolTableId SymbolTableIdFromExports(Symbol sym) => _stKindExports | new SymbolTableId(GetSymbolId(sym));

    /// <summary>
    /// symbolTableIDFromResolvedExports returns an ID for resolved/derived export tables
    /// (e.g. from getExportsOfSymbol/getExportsOfModule which may include export * resolution
    /// and late-bound members). This is distinct from symbolTableIDFromExports to prevent
    /// cache collisions with raw sym.Exports tables passed by someSymbolTableInScope.
    /// </summary>
    private static SymbolTableId SymbolTableIdFromResolvedExports(Symbol sym) => _stKindResolvedExports | new SymbolTableId(GetSymbolId(sym));

    private static SymbolTableId SymbolTableIdFromMembers(Symbol sym) => _stKindMembers | new SymbolTableId(GetSymbolId(sym));

    private static SymbolTableId SymbolTableIdFromGlobals() => _stKindGlobals;

    public bool IsTypeSymbolAccessible(Symbol typeSymbol, Node? enclosingDeclaration)
    {
        var access = IsSymbolAccessibleWorker(typeSymbol, enclosingDeclaration, SymbolFlags.Type /*shouldComputeAliasesToMakeVisible*/, false /*allowModules*/, true);
        return access.Accessibility == SymbolAccessibility.Accessible;
    }

    public bool IsValueSymbolAccessible(Symbol symbol, Node? enclosingDeclaration)
    {
        var access = IsSymbolAccessibleWorker(symbol, enclosingDeclaration, SymbolFlags.Value /*shouldComputeAliasesToMakeVisible*/, false /*allowModules*/, true);
        return access.Accessibility == SymbolAccessibility.Accessible;
    }

    public bool IsSymbolAccessibleByFlags(Symbol symbol, Node? enclosingDeclaration, SymbolFlags flags)
    {
        var access = IsSymbolAccessibleWorker(symbol, enclosingDeclaration, flags /*shouldComputeAliasesToMakeVisible*/, false /*allowModules*/, false); // TODO: Strada bug? Why is this allowModules: false?
        return access.Accessibility == SymbolAccessibility.Accessible;
    }

    public SymbolAccessibilityResult? IsAnySymbolAccessible(Symbol[]? symbols, Node? enclosingDeclaration, Symbol initialSymbol, SymbolFlags meaning, bool shouldComputeAliasesToMakeVisible, bool allowModules)
    {
        if (symbols is null || symbols.Length == 0)
        {
            return null;
        }

        Symbol? hadAccessibleChain = null;
        var earlyModuleBail = false;
        foreach (var symbol in symbols)
        {
            // Symbol is accessible if it by itself is accessible
            var accessibleSymbolChain = GetAccessibleSymbolChainWorker(symbol, enclosingDeclaration, meaning /*useOnlyExternalAliasing*/, false);
            if (accessibleSymbolChain is { Length: > 0 })
            {
                hadAccessibleChain = symbol;
                // TODO: going through emit resolver here is weird. Relayer these APIs.
                var hasAccessibleDeclarations = GetEmitResolver().HasVisibleDeclarations(accessibleSymbolChain[0]!, shouldComputeAliasesToMakeVisible);
                if (hasAccessibleDeclarations is not null)
                {
                    return hasAccessibleDeclarations;
                }
            }

            if (allowModules)
            {
                if (Some(symbol.Declarations, HasNonGlobalAugmentationExternalModuleSymbol))
                {
                    if (shouldComputeAliasesToMakeVisible)
                    {
                        earlyModuleBail = true;
                        // Generally speaking, we want to use the aliases that already exist to refer to a module, if present
                        // In order to do so, we need to find those aliases in order to retain them in declaration emit; so
                        // if we are in declaration emit, we cannot use the fast path for module visibility until we've exhausted
                        // all other visibility options (in order to capture the possible aliases used to reference the module)
                        continue;
                    }

                    // Any meaning of a module symbol is always accessible via an `import` type
                    return new SymbolAccessibilityResult
                    {
                        Accessibility = SymbolAccessibility.Accessible,
                    };
                }
            }

            // If we haven't got the accessible symbol, it doesn't mean the symbol is actually inaccessible.
            // It could be a qualified symbol and hence verify the path
            // e.g.:
            // module m {
            //     export class c {
            //     }
            // }
            // const x: typeof m.c
            // In the above example when we start with checking if typeof m.c symbol is accessible,
            // we are going to see if c can be accessed in scope directly.
            // But it can't, hence the accessible is going to be undefined, but that doesn't mean m.c is inaccessible
            // It is accessible if the parent m is accessible because then m.c can be accessed through qualification

            var containers = GetContainersOfSymbol(symbol, enclosingDeclaration, meaning);
            var nextMeaning = meaning;
            if (ReferenceEquals(initialSymbol, symbol))
            {
                nextMeaning = GetQualifiedLeftMeaning(meaning);
            }

            var parentResult = IsAnySymbolAccessible(containers, enclosingDeclaration, initialSymbol, nextMeaning, shouldComputeAliasesToMakeVisible, allowModules);
            if (parentResult is not null)
            {
                return parentResult;
            }
        }

        if (earlyModuleBail)
        {
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.Accessible,
            };
        }

        if (hadAccessibleChain is not null)
        {
            var moduleName = "";
            if (!ReferenceEquals(hadAccessibleChain, initialSymbol))
            {
                moduleName = SymbolToStringExWorker(hadAccessibleChain, enclosingDeclaration, SymbolFlags.Namespace, SymbolFormatFlags.AllowAnyNodeKind);
            }

            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotAccessible,
                ErrorSymbolName = SymbolToStringExWorker(initialSymbol, enclosingDeclaration, meaning, SymbolFormatFlags.AllowAnyNodeKind),
                ErrorModuleName = moduleName,
            };
        }

        return null;
    }

    internal static bool HasNonGlobalAugmentationExternalModuleSymbol(Node declaration) =>
        IsModuleWithStringLiteralName(declaration) || (declaration.Kind == Kind.SourceFile && IsExternalOrCommonJSModule(declaration.AsSourceFile()!));

    internal static SymbolFlags GetQualifiedLeftMeaning(SymbolFlags rightMeaning)
    {
        // If we are looking in value space, the parent meaning is value, other wise it is namespace
        return rightMeaning == SymbolFlags.Value ? SymbolFlags.Value : SymbolFlags.Namespace;
    }

    private Symbol[] GetWithAlternativeContainers(Symbol container, Symbol symbol, Node? enclosingDeclaration, SymbolFlags meaning)
    {
        var additionalContainers = MapNonNil(container.Declarations, d => GetFileSymbolIfFileSymbolExportEqualsContainer(d, container)!);
        Symbol[]? reexportContainers = null;
        if (enclosingDeclaration is not null)
        {
            reexportContainers = GetAlternativeContainingModules(symbol, enclosingDeclaration);
        }

        var objectLiteralContainer = GetVariableDeclarationOfObjectLiteral(container, meaning);
        var leftMeaning = GetQualifiedLeftMeaning(meaning);
        if (enclosingDeclaration is not null &&
            (container.Flags & leftMeaning) != 0 &&
            GetAccessibleSymbolChainWorker(container, enclosingDeclaration, SymbolFlags.Namespace /*useOnlyExternalAliasing*/, false) is { Length: > 0 })
        {
            // This order expresses a preference for the real container if it is in scope
            var res = new List<Symbol> { container };
            res.AddRange(additionalContainers);
            if (reexportContainers is not null)
            {
                res.AddRange(reexportContainers);
            }

            if (objectLiteralContainer is not null)
            {
                res.Add(objectLiteralContainer);
            }

            return res.ToArray();
        }

        // we potentially have a symbol which is a member of the instance side of something - look for a variable in scope with the container's type
        // which may be acting like a namespace (eg, `Symbol` acts like a namespace when looking up `Symbol.toStringTag`)
        List<Symbol>? variableMatches = null;
        if (meaning == SymbolFlags.Value &&
            (container.Flags & leftMeaning) == 0 &&
            (container.Flags & SymbolFlags.Type) != 0 &&
            (GetDeclaredTypeOfSymbolWorker(container)._flags & TypeFlags.Object) != 0)
        {
            SomeSymbolTableInScope(enclosingDeclaration, (t, _, _, _, _) =>
            {
                var found = false;
                if (t is not null)
                {
                    foreach (var s in t.Values)
                    {
                        if ((s.Flags & leftMeaning) != 0 && ReferenceEquals(GetTypeOfSymbolWorker(s), GetDeclaredTypeOfSymbolWorker(container)))
                        {
                            (variableMatches ??= new List<Symbol>()).Add(s);
                            found = true;
                        }
                    }
                }

                return found;
            });
            if (variableMatches is not null)
            {
                SortSymbols(variableMatches.ToArray());
            }
        }

        var result = new List<Symbol>();
        if (variableMatches is not null)
        {
            result.AddRange(variableMatches);
        }

        result.AddRange(additionalContainers);
        result.Add(container);
        if (objectLiteralContainer is not null)
        {
            result.Add(objectLiteralContainer);
        }

        if (reexportContainers is not null)
        {
            result.AddRange(reexportContainers);
        }

        return result.ToArray();
    }

    private Symbol[]? GetAlternativeContainingModules(Symbol symbol, Node? enclosingDeclaration)
    {
        if (enclosingDeclaration is null)
        {
            return null;
        }

        var containingFile = GetSourceFileOfNode(enclosingDeclaration)!;
        var id = GetNodeId(containingFile.AsNode());
        var links = _symbolContainerLinks.Get(symbol);
        links._extendedContainersByFile ??= new Dictionary<NodeId, Symbol[]>();
        if (links._extendedContainersByFile.TryGetValue(id, out var existing) && existing is not null)
        {
            return existing;
        }

        List<Symbol>? results = null;
        if (containingFile.Imports().Length > 0)
        {
            // Try to make an import using an import already in the enclosing file, if possible
            foreach (var importRef in containingFile.Imports())
            {
                if (NodeIsSynthesized(importRef))
                {
                    // Synthetic names can't be resolved by `resolveExternalModuleName` - they'll cause a debug assert if they error
                    continue;
                }

                var resolvedModule = ResolveExternalModuleNameWorker(enclosingDeclaration, importRef /*ignoreErrors*/, true, GetImportAttributesTypeForModuleSpecifier(importRef));
                if (resolvedModule is null)
                {
                    continue;
                }

                var reference = GetAliasForSymbolInContainer(resolvedModule, symbol);
                if (reference is null)
                {
                    continue;
                }

                (results ??= new List<Symbol>()).Add(resolvedModule);
            }

            if (results is not null && results.Count > 0)
            {
                var arr = results.ToArray();
                links._extendedContainersByFile[id] = arr;
                return arr;
            }
        }

        if (links._extendedContainers is not null)
        {
            return links._extendedContainers;
        }

        // No results from files already being imported by this file - expand search (expensive, but not location-specific, so cached)
        var otherFiles = _program.SourceFiles();
        foreach (var file in otherFiles)
        {
            if (!IsExternalModule(file))
            {
                continue;
            }

            var sym = GetSymbolOfDeclaration(file.AsNode());
            if (sym is null)
            {
                continue;
            }

            var reference = GetAliasForSymbolInContainer(sym, symbol);
            if (reference is null)
            {
                continue;
            }

            (results ??= new List<Symbol>()).Add(sym);
        }

        var allResults = results?.ToArray() ?? Array.Empty<Symbol>();
        links._extendedContainers = allResults;
        return allResults;
    }

    private Symbol? GetVariableDeclarationOfObjectLiteral(Symbol symbol, SymbolFlags meaning)
    {
        // If we're trying to reference some object literal in, eg `var a = { x: 1 }`, the symbol for the literal, `__object`, is distinct
        // from the symbol of the declaration it is being assigned to. Since we can use the declaration to refer to the literal, however,
        // we'd like to make that connection here - potentially causing us to paint the declaration's visibility, and therefore the literal.
        if ((meaning & SymbolFlags.Value) == 0)
        {
            return null;
        }

        if (symbol.Declarations is null || symbol.Declarations.Length == 0)
        {
            return null;
        }

        var firstDecl = symbol.Declarations[0];
        if (firstDecl.Parent is null)
        {
            return null;
        }

        if (!IsVariableDeclaration(firstDecl.Parent))
        {
            return null;
        }

        if ((IsObjectLiteralExpression(firstDecl) && ReferenceEquals(firstDecl, firstDecl.Parent.Initializer())) || (IsTypeLiteralNode(firstDecl) && ReferenceEquals(firstDecl, firstDecl.Parent.Type())))
        {
            return GetSymbolOfDeclaration(firstDecl.Parent);
        }

        return null;
    }

    private static bool HasExternalModuleSymbol(Node declaration) =>
        IsAmbientModule(declaration) || (declaration.Kind == Kind.SourceFile && IsExternalOrCommonJSModule(declaration.AsSourceFile()!));

    private Symbol? GetExternalModuleContainer(Node declaration)
    {
        var node = FindAncestor(declaration, HasExternalModuleSymbol);
        if (node is null)
        {
            return null;
        }

        return GetSymbolOfDeclaration(node);
    }

    internal Symbol? GetFileSymbolIfFileSymbolExportEqualsContainer(Node d, Symbol container)
    {
        var fileSymbol = GetExternalModuleContainer(d);
        if (fileSymbol?.Exports is null)
        {
            return null;
        }

        if (!fileSymbol.Exports.TryGetValue(InternalSymbolNameExportEquals, out var exported) || exported is null)
        {
            return null;
        }

        if (GetSymbolIfSameReference(exported, container) is not null)
        {
            return fileSymbol;
        }

        return null;
    }

    /// <summary>
    /// Attempts to find the symbol corresponding to the container a symbol is in - usually this
    /// is just its' `.parent`, but for locals, this value is `undefined`
    /// </summary>
    internal Symbol[]? GetContainersOfSymbol(Symbol symbol, Node? enclosingDeclaration, SymbolFlags meaning)
    {
        var container = GetParentOfSymbol(symbol);
        // Type parameters end up in the `members` lists but are not externally visible
        if (container is not null && (symbol.Flags & SymbolFlags.TypeParameter) == 0)
        {
            return GetWithAlternativeContainers(container, symbol, enclosingDeclaration, meaning);
        }

        List<Symbol>? candidates = null;
        foreach (var d in symbol.Declarations ?? [])
        {
            if (!IsAmbientModule(d) && d.Parent is not null)
            {
                // direct children of a module
                if (HasNonGlobalAugmentationExternalModuleSymbol(d.Parent))
                {
                    var sym = GetSymbolOfDeclaration(d.Parent);
                    if (sym is not null && !(candidates?.Contains(sym) ?? false))
                    {
                        (candidates ??= new List<Symbol>()).Add(sym);
                    }

                    continue;
                }

                // export ='d member of an ambient module
                if (IsModuleBlock(d.Parent) && d.Parent.Parent is not null && ReferenceEquals(ResolveExternalModuleSymbolWorker(GetSymbolOfDeclaration(d.Parent.Parent), false), symbol))
                {
                    var sym = GetSymbolOfDeclaration(d.Parent.Parent);
                    if (sym is not null && !(candidates?.Contains(sym) ?? false))
                    {
                        (candidates ??= new List<Symbol>()).Add(sym);
                    }

                    continue;
                }
            }

            var binaryParent = d.Parent;
            if (IsClassExpression(d) && IsBinaryExpression(binaryParent!) && binaryParent!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken && IsAccessExpression(binaryParent!.AsBinaryExpression()!.Left!) && IsEntityNameExpression(binaryParent!.AsBinaryExpression()!.Left!.Expression()!))
            {
                if (IsModuleExportsAccessExpression(binaryParent!.AsBinaryExpression()!.Left) || IsExportsIdentifier(binaryParent!.AsBinaryExpression()!.Left!.Expression()!))
                {
                    var sym = GetSymbolOfDeclaration(GetSourceFileOfNode(d)!.AsNode());
                    if (sym is not null && !(candidates?.Contains(sym) ?? false))
                    {
                        (candidates ??= new List<Symbol>()).Add(sym);
                    }

                    continue;
                }

                CheckExpressionCached(d.Parent!.AsBinaryExpression()!.Left!.Expression()!);
                var resolved = _symbolNodeLinks.Get(d.Parent!.AsBinaryExpression()!.Left!.Expression()!)._resolvedSymbol;
                if (resolved is not null && !(candidates?.Contains(resolved) ?? false))
                {
                    (candidates ??= new List<Symbol>()).Add(resolved);
                }

                continue;
            }
        }

        if (candidates is null || candidates.Count == 0)
        {
            return null;
        }

        List<Symbol>? bestContainers = null;
        List<Symbol>? alternativeContainers = null;
        foreach (var containerCandidate in candidates)
        {
            if (GetAliasForSymbolInContainer(containerCandidate, symbol) is null)
            {
                continue;
            }

            var allAlts = GetWithAlternativeContainers(containerCandidate, symbol, enclosingDeclaration, meaning);
            if (allAlts.Length == 0)
            {
                continue;
            }

            (bestContainers ??= new List<Symbol>()).Add(allAlts[0]);
            if (allAlts.Length > 1)
            {
                (alternativeContainers ??= new List<Symbol>()).AddRange(allAlts.AsSpan(1).ToArray());
            }
        }

        var result = new List<Symbol>();
        if (bestContainers is not null)
        {
            result.AddRange(bestContainers);
        }

        if (alternativeContainers is not null)
        {
            result.AddRange(alternativeContainers);
        }

        return result.ToArray();
    }

    internal Symbol? GetAliasForSymbolInContainer(Symbol container, Symbol symbol)
    {
        if (ReferenceEquals(container, GetParentOfSymbol(symbol)))
        {
            // fast path, `symbol` is either already the alias or isn't aliased
            return symbol;
        }

        // Check if container is a thing with an `export=` which points directly at `symbol`, and if so, return
        // the container itself as the alias for the symbol
        if (container.Exports is not null)
        {
            if (container.Exports.TryGetValue(InternalSymbolNameExportEquals, out var exportEquals) && exportEquals is not null && GetSymbolIfSameReference(exportEquals, symbol) is not null)
            {
                return container;
            }
        }

        var exports = GetExportsOfSymbol(container);
        if (exports is not null)
        {
            if (exports.TryGetValue(symbol.Name, out var quick) && quick is not null && GetSymbolIfSameReference(quick, symbol) is not null)
            {
                return quick;
            }
        }

        List<Symbol>? candidates = null;
        if (exports is not null)
        {
            foreach (var exported in exports.Values)
            {
                if (GetSymbolIfSameReference(exported, symbol) is not null)
                {
                    (candidates ??= new List<Symbol>()).Add(exported);
                }
            }
        }

        if (candidates is not null && candidates.Count > 0)
        {
            var arr = candidates.ToArray();
            SortSymbols(arr); // _must_ sort exports for stable results - symbol table is randomly iterated
            return arr[0];
        }

        return null;
    }

    internal Symbol[]? GetAccessibleSymbolChainWorker(
        Symbol symbol,
        Node? enclosingDeclaration,
        SymbolFlags meaning,
        bool useOnlyExternalAliasing)
    {
        return GetAccessibleSymbolChainEx(new AccessibleSymbolChainContext
        {
            _symbol = symbol,
            _enclosingDeclaration = enclosingDeclaration,
            _meaning = meaning,
            _useOnlyExternalAliasing = useOnlyExternalAliasing,
            _visitedSymbolTablesMap = new Dictionary<SymbolId, HashSet<SymbolTableId>>(),
        });
    }

    public Symbol[]? GetAccessibleSymbolChain(
        Symbol symbol,
        Node? enclosingDeclaration,
        SymbolFlags meaning,
        bool useOnlyExternalAliasing)
    {
        return GetAccessibleSymbolChainWorker(symbol, enclosingDeclaration, meaning, useOnlyExternalAliasing);
    }

    private Symbol[]? GetAccessibleSymbolChainEx(AccessibleSymbolChainContext ctx)
    {
        if (ctx._symbol is null)
        {
            return null;
        }

        if (IsPropertyOrMethodDeclarationSymbol(ctx._symbol))
        {
            return null;
        }

        // Go from enclosingDeclaration to the first scope we check, so the cache is keyed off the scope and thus shared more
        Node? firstRelevantLocation = null;
        SomeSymbolTableInScope(ctx._enclosingDeclaration, (_, _, _, _, node) =>
        {
            firstRelevantLocation = node;
            return true;
        });
        var links = _symbolContainerLinks.Get(ctx._symbol);
        var linkKey = new AccessibleChainCacheKey(ctx._useOnlyExternalAliasing, firstRelevantLocation, ctx._meaning);
        links._accessibleChainCache ??= new Dictionary<AccessibleChainCacheKey, Symbol[]>();
        if (links._accessibleChainCache.TryGetValue(linkKey, out var cached))
        {
            return cached;
        }

        Symbol[]? result = null;

        SomeSymbolTableInScope(ctx._enclosingDeclaration, (t, tableId, ignoreQualification, isLocalNameLookup, _) =>
        {
            var res = GetAccessibleSymbolChainFromSymbolTable(ctx, t, tableId, ignoreQualification, isLocalNameLookup);
            if (res is { Length: > 0 })
            {
                result = res;
                return true;
            }

            return false;
        });
        links._accessibleChainCache[linkKey] = result ?? Array.Empty<Symbol>();
        return result;
    }

    /// <summary>
    /// <paramref name="ignoreQualification"/> Set when a symbol is being looked for through the exports of another symbol (meaning we have a route to qualify it already)
    /// </summary>
    private Symbol[]? GetAccessibleSymbolChainFromSymbolTable(AccessibleSymbolChainContext ctx, SymbolTable? t, SymbolTableId tableId, bool ignoreQualification, bool isLocalNameLookup)
    {
        var symId = GetSymbolId(ctx._symbol!);
        if (!ctx._visitedSymbolTablesMap.TryGetValue(symId, out var visitedSymbolTables))
        {
            visitedSymbolTables = new HashSet<SymbolTableId>();
            ctx._visitedSymbolTablesMap[symId] = visitedSymbolTables;
        }

        if (!visitedSymbolTables.Add(tableId))
        {
            return null;
        }

        var res = TrySymbolTable(ctx, t, tableId, ignoreQualification, isLocalNameLookup);

        visitedSymbolTables.Remove(tableId);
        return res;
    }

    /// <summary>
    /// getSymbolTableAliases returns only the alias symbols from a symbol table,
    /// caching the result by tableId to avoid repeated iteration over large tables.
    /// Members tables are skipped entirely since someSymbolTableInScope filters them
    /// to SymbolFlagsType &amp; ^SymbolFlagsAssignment, which never includes aliases.
    /// </summary>
    private Symbol[]? GetSymbolTableAliases(SymbolTable? symbols, SymbolTableId tableId)
    {
        var kind = tableId & _stKindMask;
        // Members tables never contain alias symbols; skip entirely.
        if (kind == _stKindMembers)
        {
            return null;
        }

        // Cache globals and exports tables (which are large and revisited often).
        // Locals tables are small and per-scope, so they are filtered but not cached.
        if (kind == _stKindGlobals || kind == _stKindExports || kind == _stKindResolvedExports)
        {
            if (_symbolTableAliasCache is not null && _symbolTableAliasCache.TryGetValue(tableId, out var aliases))
            {
                return aliases;
            }
        }

        List<Symbol>? result = null;
        if (symbols is not null)
        {
            foreach (var sym in symbols.Values)
            {
                if ((sym.Flags & SymbolFlags.Alias) != 0)
                {
                    (result ??= new List<Symbol>()).Add(sym);
                }
            }
        }

        var arr = result?.ToArray();
        if (kind == _stKindGlobals || kind == _stKindExports || kind == _stKindResolvedExports)
        {
            _symbolTableAliasCache ??= new Dictionary<SymbolTableId, Symbol[]>();
            _symbolTableAliasCache[tableId] = arr ?? Array.Empty<Symbol>();
        }

        return arr;
    }

    private Symbol[]? TrySymbolTable(
        AccessibleSymbolChainContext ctx,
        SymbolTable? symbols,
        SymbolTableId tableId,
        bool ignoreQualification,
        bool isLocalNameLookup)
    {
        var isGlobals = tableId == _stKindGlobals;
        // If symbol is directly available by its name in the symbol table
        Symbol? res = null;
        var ok = symbols is not null && symbols.TryGetValue(ctx._symbol!.Name, out res);
        if (ok && res is not null && IsAccessible(ctx, res /*resolvedAliasSymbol*/, null, ignoreQualification))
        {
            return new Symbol[] { ctx._symbol! };
        }

        List<Symbol[]>? candidateChains = null;

        // Check for ExportSymbol by direct name lookup rather than discovering it during
        // the alias iteration below (where it would never match, since only alias-flagged
        // symbols are iterated).
        if (ok && res is not null && res.ExportSymbol is not null)
        {
            if (IsAccessible(ctx, GetMergedSymbolWorker(res.ExportSymbol)!, null, ignoreQualification))
            {
                (candidateChains ??= new List<Symbol[]>()).Add(new Symbol[] { ctx._symbol! });
            }
        }

        // Iterate only alias symbols from the table (cached per tableId).
        // This avoids iterating thousands of non-alias symbols in large tables like globals.
        foreach (var symbolFromSymbolTable in GetSymbolTableAliases(symbols, tableId) ?? [])
        {
            // for every non-default, non-export= alias symbol in scope, check if it refers to or can chain to the target symbol
            if (symbolFromSymbolTable.Name != InternalSymbolNameExportEquals &&
                symbolFromSymbolTable.Name != InternalSymbolNameDefault &&
                !(IsUMDExportSymbol(symbolFromSymbolTable) && ctx._enclosingDeclaration is not null && IsExternalModule(GetSourceFileOfNode(ctx._enclosingDeclaration)!)) &&
                // If `!useOnlyExternalAliasing`, we can use any type of alias to get the name
                (!ctx._useOnlyExternalAliasing || Some(symbolFromSymbolTable.Declarations, IsExternalModuleImportEqualsDeclaration)) &&
                // If we're looking up a local name to reference directly, omit namespace reexports, otherwise when we're trawling through an export list to make a dotted name, we can keep it
                ((isLocalNameLookup && !Some(symbolFromSymbolTable.Declarations, IsNamespaceReexportDeclaration)) || !isLocalNameLookup) &&
                // While exports are generally considered to be in scope, export-specifier declared symbols are _not_
                // See similar comment in `resolveName` for details
                (ignoreQualification || (GetDeclarationsOfKind(symbolFromSymbolTable, Kind.ExportSpecifier)?.Length ?? 0) == 0))
            {
                var resolvedImportedSymbol = ResolveAliasWorker(symbolFromSymbolTable);
                var candidate = GetCandidateListForSymbol(ctx, symbolFromSymbolTable, resolvedImportedSymbol, ignoreQualification);
                if (candidate is { Length: > 0 })
                {
                    (candidateChains ??= new List<Symbol[]>()).Add(candidate);
                }
            }
        }

        if (candidateChains is not null && candidateChains.Count > 0)
        {
            // pick first, shortest
            // Go: slices.SortStableFunc(candidateChains, c.compareSymbolChains) —— C# 的 Array.Sort/List.Sort 不稳定，
            // 这里手写插入排序保持 Go 的稳定性（候选链数量极小）。
            for (var i = 1; i < candidateChains.Count; i++)
            {
                var chain = candidateChains[i];
                var j = i - 1;
                while (j >= 0 && _compareSymbolChains(chain, candidateChains[j]) < 0)
                {
                    candidateChains[j + 1] = candidateChains[j];
                    j--;
                }

                candidateChains[j + 1] = chain;
            }

            return candidateChains[0];
        }

        // If there's no result and we're looking at the global symbol table, treat `globalThis` like an alias and try to lookup thru that
        if (isGlobals)
        {
            return GetCandidateListForSymbol(ctx, _globalThisSymbol, _globalThisSymbol, ignoreQualification);
        }

        return null;
    }

    private int CompareSymbolChainsWorker(Symbol[] a, Symbol[] b)
    {
        var chainLen = a.Length - b.Length;
        if (chainLen != 0)
        {
            return chainLen;
        }

        var idx = 0;
        while (idx < a.Length)
        {
            var comparison = _compareSymbols(a[idx], b[idx]);
            if (comparison != 0)
            {
                return comparison;
            }

            idx++;
        }

        return 0;
    }

    private static bool IsUMDExportSymbol(Symbol? symbol) =>
        symbol is not null && symbol.Declarations is { Length: > 0 } && symbol.Declarations[0] is not null && IsNamespaceExportDeclaration(symbol.Declarations[0]);

    private static bool IsNamespaceReexportDeclaration(Node node) =>
        IsNamespaceExport(node) && node.Parent!.ModuleSpecifier() is not null;

    private Symbol[]? GetCandidateListForSymbol(
        AccessibleSymbolChainContext ctx,
        Symbol symbolFromSymbolTable,
        Symbol? resolvedImportedSymbol,
        bool ignoreQualification)
    {
        if (IsAccessible(ctx, symbolFromSymbolTable, resolvedImportedSymbol, ignoreQualification))
        {
            return new Symbol[] { symbolFromSymbolTable };
        }

        // Look in the exported members, if we can find accessibleSymbolChain, symbol is accessible using this chain
        // but only if the symbolFromSymbolTable can be qualified
        var candidateTable = GetExportsOfSymbol(resolvedImportedSymbol!);
        if (candidateTable is null)
        {
            return null;
        }

        var candidateTableId = SymbolTableIdFromResolvedExports(resolvedImportedSymbol!);
        var accessibleSymbolsFromExports = GetAccessibleSymbolChainFromSymbolTable(ctx, candidateTable, candidateTableId /*ignoreQualification*/, true, false);
        if (accessibleSymbolsFromExports is not { Length: > 0 })
        {
            return null;
        }

        if (!CanQualifySymbol(ctx, symbolFromSymbolTable, GetQualifiedLeftMeaning(ctx._meaning)))
        {
            return null;
        }

        var result = new Symbol[1 + accessibleSymbolsFromExports.Length];
        result[0] = symbolFromSymbolTable;
        accessibleSymbolsFromExports.CopyTo(result, 1);
        return result;
    }

    private bool IsAccessible(
        AccessibleSymbolChainContext ctx,
        Symbol symbolFromSymbolTable,
        Symbol? resolvedAliasSymbol,
        bool ignoreQualification)
    {
        var likeSymbols = false;
        if (ReferenceEquals(ctx._symbol, resolvedAliasSymbol))
        {
            likeSymbols = true;
        }

        if (ReferenceEquals(ctx._symbol, symbolFromSymbolTable))
        {
            likeSymbols = true;
        }

        var symbol = GetMergedSymbolWorker(ctx._symbol);
        if (ReferenceEquals(symbol, GetMergedSymbolWorker(resolvedAliasSymbol)))
        {
            likeSymbols = true;
        }

        if (ReferenceEquals(symbol, GetMergedSymbolWorker(symbolFromSymbolTable)))
        {
            likeSymbols = true;
        }

        if (!likeSymbols)
        {
            return false;
        }

        // if the symbolFromSymbolTable is not external module (it could be if it was determined as ambient external module and would be in globals table)
        // and if symbolFromSymbolTable or alias resolution matches the symbol,
        // check the symbol can be qualified, it is only then this symbol is accessible
        return !Some(symbolFromSymbolTable.Declarations, HasNonGlobalAugmentationExternalModuleSymbol) &&
            (ignoreQualification || CanQualifySymbol(ctx, GetMergedSymbolWorker(symbolFromSymbolTable)!, ctx._meaning));
    }

    private bool CanQualifySymbol(
        AccessibleSymbolChainContext ctx,
        Symbol symbolFromSymbolTable,
        SymbolFlags meaning)
    {
        // If the symbol is equivalent and doesn't need further qualification, this symbol is accessible
        return !NeedsQualification(symbolFromSymbolTable, ctx._enclosingDeclaration, meaning) ||
            // If symbol needs qualification, make sure that parent is accessible, if it is then this symbol is accessible too
            GetAccessibleSymbolChainEx(new AccessibleSymbolChainContext
            {
                _symbol = symbolFromSymbolTable.Parent,
                _enclosingDeclaration = ctx._enclosingDeclaration,
                _meaning = GetQualifiedLeftMeaning(meaning),
                _useOnlyExternalAliasing = ctx._useOnlyExternalAliasing,
                _visitedSymbolTablesMap = ctx._visitedSymbolTablesMap,
            }) is { Length: > 0 };
    }

    internal bool NeedsQualification(Symbol symbol, Node? enclosingDeclaration, SymbolFlags meaning)
    {
        var qualify = false;
        SomeSymbolTableInScope(enclosingDeclaration, (symbolTable, _, _, _, _) =>
        {
            // If symbol of this name is not available in the symbol table we are ok
            if (symbolTable is null || !symbolTable.TryGetValue(symbol.Name, out var res) || res is null)
            {
                return false;
            }

            var symbolFromSymbolTable = GetMergedSymbolWorker(res);
            if (symbolFromSymbolTable is null)
            {
                // Continue to the next symbol table
                return false;
            }

            // If the symbol with this name is present it should refer to the symbol
            if (ReferenceEquals(symbolFromSymbolTable, symbol))
            {
                // No need to qualify
                return true;
            }

            // Qualify if the symbol from symbol table has same meaning as expected
            var shouldResolveAlias = (symbolFromSymbolTable.Flags & SymbolFlags.Alias) != 0 && GetDeclarationOfKind(symbolFromSymbolTable, Kind.ExportSpecifier) is null;
            if (shouldResolveAlias)
            {
                symbolFromSymbolTable = ResolveAliasWorker(symbolFromSymbolTable);
            }

            var flags = symbolFromSymbolTable!.Flags;
            if (shouldResolveAlias)
            {
                flags = GetSymbolFlagsWorker(symbolFromSymbolTable);
            }

            if ((flags & meaning) != 0)
            {
                qualify = true;
                return true;
            }

            // Continue to the next symbol table
            return false;
        });

        return qualify;
    }

    private static bool IsPropertyOrMethodDeclarationSymbol(Symbol symbol)
    {
        if (symbol.Declarations is { Length: > 0 })
        {
            foreach (var declaration in symbol.Declarations)
            {
                switch (declaration.Kind)
                {
                    case Kind.PropertyDeclaration:
                    case Kind.MethodDeclaration:
                    case Kind.GetAccessor:
                    case Kind.SetAccessor:
                        continue;
                    default:
                        return false;
                }
            }

            return true;
        }

        return false;
    }

    private bool SomeSymbolTableInScope(Node? enclosingDeclaration, SymbolTableInScopeCallback callback)
    {
        for (var location = enclosingDeclaration; location is not null; location = location.Parent)
        {
            // Locals of a source file are not in scope (because they get merged into the global symbol table)
            if (CanHaveLocals(location) && location.Locals() is not null && !IsGlobalSourceFile(location))
            {
                if (callback(location.Locals(), SymbolTableIdFromLocals(location.AsNode()), false, true, location))
                {
                    return true;
                }
            }

            switch (location.Kind)
            {
                case Kind.SourceFile:
                case Kind.ModuleDeclaration:
                    if (IsSourceFile(location) && !IsExternalOrCommonJSModule(location.AsSourceFile()!))
                    {
                        break;
                    }

                    var moduleSym = GetSymbolOfDeclaration(GetReparsedNodeForNode(location)!);
                    if (callback(moduleSym!.Exports, SymbolTableIdFromExports(moduleSym), false, true, location))
                    {
                        return true;
                    }

                    break;
                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                case Kind.InterfaceDeclaration:
                    // Type parameters are bound into `members` lists so they can merge across declarations
                    // This is troublesome, since in all other respects, they behave like locals :cries:
                    // TODO: the below is shared with similar code in `resolveName` - in fact, rephrasing all this symbol
                    // lookup logic in terms of `resolveName` would be nice
                    // The below is used to lookup type parameters within a class or interface, as they are added to the class/interface locals
                    // These can never be latebound, so the symbol's raw members are sufficient. `getMembersOfNode` cannot be used, as it would
                    // trigger resolving late-bound names, which we may already be in the process of doing while we're here!
                    SymbolTable? table = null;
                    var sym = GetSymbolOfDeclaration(location);
                    // TODO: Should this filtered table be cached in some way?
                    foreach (var pair in sym!.Members ?? [])
                    {
                        if ((pair.Value.Flags & (SymbolFlags.Type & ~SymbolFlags.Assignment)) != 0)
                        {
                            table ??= new SymbolTable();
                            table[pair.Key] = pair.Value;
                        }
                    }

                    if (table is not null && callback(table, SymbolTableIdFromMembers(sym), false, false, location))
                    {
                        return true;
                    }

                    // Class expression names (e.g., `B` in `class B {}`) are not stored in any
                    // scope table — the binder uses bindAnonymousDeclaration. Expose the name
                    // binding here so getAccessibleSymbolChain can resolve self-references.
                    // This mirrors the special casing of class expression names in
                    // (*NameResolver).Resolve; if class names are ever bound differently
                    // (e.g., via class-local type aliases), both sites should be updated.
                    if (IsClassExpression(location) && location.AsClassExpression()!.Name() is not null)
                    {
                        var nameTable = GetClassExpressionNameTable(location);
                        if (nameTable is not null && callback(nameTable, SymbolTableIdFromLocals(location.AsNode()), false, true, location))
                        {
                            return true;
                        }
                    }

                    break;
            }
        }

        return callback(_globals, SymbolTableIdFromGlobals(), false, true, null);
    }

    /// <summary>
    /// getClassExpressionNameTable returns a cached symbol table containing the class
    /// expression's name binding. Class expression names are bound via
    /// bindAnonymousDeclaration and aren't stored in any container's locals, so this
    /// synthesized table lets someSymbolTableInScope expose them during accessibility checks.
    /// </summary>
    private SymbolTable? GetClassExpressionNameTable(Node location)
    {
        var nodeId = GetNodeId(location);
        if (_classExpressionNameTables is not null && _classExpressionNameTables.TryGetValue(nodeId, out var existing))
        {
            return existing;
        }

        var classSymbol = GetSymbolOfDeclaration(location);
        var nameText = location.AsClassExpression()!.Name()!.Text();
        if (nameText.Length == 0 || classSymbol is null)
        {
            return null;
        }

        var table = new SymbolTable { [nameText] = classSymbol };
        _classExpressionNameTables ??= new Dictionary<NodeId, SymbolTable>();
        _classExpressionNameTables[nodeId] = table;
        return table;
    }

    /// <summary>
    /// Check if the given symbol in given enclosing declaration is accessible and mark all associated alias to be visible if requested
    /// </summary>
    /// <param name="symbol">a Symbol to check if accessible</param>
    /// <param name="enclosingDeclaration">a Node containing reference to the symbol</param>
    /// <param name="meaning">a SymbolFlags to check if such meaning of the symbol is accessible</param>
    /// <param name="shouldComputeAliasesToMakeVisible">a boolean value to indicate whether to return aliases to be mark visible in case the symbol is accessible</param>
    public SymbolAccessibilityResult IsSymbolAccessible(Symbol symbol, Node? enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasesToMakeVisible)
    {
        return IsSymbolAccessibleWorker(symbol, enclosingDeclaration, meaning, shouldComputeAliasesToMakeVisible, true /*allowModules*/);
    }

    internal SymbolAccessibilityResult IsSymbolAccessibleWorker(Symbol? symbol, Node? enclosingDeclaration, SymbolFlags meaning, bool shouldComputeAliasesToMakeVisible, bool allowModules)
    {
        if (symbol is not null && enclosingDeclaration is not null)
        {
            var result = IsAnySymbolAccessible(new Symbol[] { symbol }, enclosingDeclaration, symbol, meaning, shouldComputeAliasesToMakeVisible, allowModules);
            if (result is not null)
            {
                return result;
            }

            // This could be a symbol that is not exported in the external module
            // or it could be a symbol from different external module that is not aliased and hence cannot be named
            var symbolExternalModule = FirstNonNil(symbol.Declarations, d => GetExternalModuleContainer(d));
            if (symbolExternalModule is not null)
            {
                var enclosingExternalModule = GetExternalModuleContainer(enclosingDeclaration);
                if (!ReferenceEquals(symbolExternalModule, enclosingExternalModule))
                {
                    // name from different external module that is not visible
                    return new SymbolAccessibilityResult
                    {
                        Accessibility = SymbolAccessibility.CannotBeNamed,
                        ErrorSymbolName = SymbolToStringExWorker(symbol, enclosingDeclaration, meaning, SymbolFormatFlags.AllowAnyNodeKind),
                        ErrorModuleName = SymbolToStringWorker(symbolExternalModule),
                        ErrorNode = IsInJSFile(enclosingDeclaration) ? enclosingDeclaration : null,
                    };
                }
            }

            // Just a local name that is not accessible
            return new SymbolAccessibilityResult
            {
                Accessibility = SymbolAccessibility.NotAccessible,
                ErrorSymbolName = SymbolToStringExWorker(symbol, enclosingDeclaration, meaning, SymbolFormatFlags.AllowAnyNodeKind),
            };
        }

        return new SymbolAccessibilityResult
        {
            Accessibility = SymbolAccessibility.Accessible,
        };
    }
}
