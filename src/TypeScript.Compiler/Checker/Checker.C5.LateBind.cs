// ported from: internal/checker/checker.go:16322-16717
//   （lateBindMember / lateBindIndexSignature / isNotReplacableByMethod / addDeclarationToLateBoundSymbol /
//     getMembersOfSymbol / getExportsOfModule(+Worker) / ExportCollision / extendExportSymbols /
//     ResolveAlias / resolveAlias / resolveIndirectionAlias / tryResolveAlias / resolveAliasWithDeprecationCheck /
//     getSymbolFlags(+Ex) / getDeclarationOfAliasSymbol）
//
// 与 Go 的偏离（逐条）：
// 1) Go 的包级函数与 `(c *Checker)` 方法同处一个 `public sealed partial class Checker`（docs/checker-plan.md §5.2），
//    故 `isNotReplacableByMethod` 落成 `internal static IsNotReplacableByMethod`。
// 2) Worker 后缀（§5.2 同名规则 + artifacts/checker-method-collisions.md）：
//    `getExportsOfModule` → `GetExportsOfModuleWorker`（导出的 `GetExportsOfModule` 在 services.go:132）;
//    `getSymbolFlags` → `GetSymbolFlagsWorker`（导出在 exports.go:300）;
//    `resolveAlias` → `ResolveAliasWorker`（导出的 `ResolveAlias` 就在本窗口）。
//    Go 的 `getExportsOfModuleWorker`（名字已带 Worker）落成 `GetExportsOfModuleWorkerImpl`，避免与前者重名。
// 3) `ResolveAlias` 的 Go 返回 `(*ast.Symbol, bool)` → `bool ResolveAlias(Symbol? symbol, out Symbol? resolved)`（§5.4 的 Try 约定）。
// 4) Go 的 `map` 读缺失键返回 nil → C# 用 `GetValueOrDefault`；写/增用索引器。
// 5) Go 对 **nil map** 的 `for range`/`delete` 都是合法空操作 → C# 的 `foreach`/`Dictionary.Remove` 在 null 上会抛异常，
//    故 `ExtendExportSymbols` 对 null `source` 提前返回、`GetExportsOfModuleWorkerImpl` 末尾对 null
//    `typeOnlyExportStarMap` 判空（语义与 Go 一致：nil map 上没有可删除的键）。
// 6) `ExportCollisionTable` 是 Go 的类型别名 `map[string]*ExportCollision` → 不产生 C# 类型（§5.4 类型别名折叠），
//    签名里直接写 `Dictionary<string, ExportCollision>`。
// 7) Go 的 `append(slice, x)` → C# 集合表达式 `[.. slice ?? [], x]`（与 Binder/*.cs 同款）。
// 8) `debug.Assert` → `Debug.Assert`；Go 的 `panic` → `Debug.Fail`（§5.4；本文件禁止 `using System.Diagnostics;`）。
// 9) `c.resolveSymbolEx(<可能为 nil 的符号>, false)`：Go 会把 nil 直接交给 `ast.IsNonLocalAlias`（其 C# 参数是 `Symbol?`），
//    所以调用点写 `Exports?.GetValueOrDefault(...)`，与 Go 的 nil 传参一致。
// 10) Go 的 `for _, x := range m` 遍历顺序依赖 Go map 的随机顺序；本文件两处遍历（`extendExportSymbols` 的 `source`、
//     `getExportsOfModuleWorker` 的 `lookupTable`/`originalModule.Exports`）在 Go 里也不保证顺序，故 C# 的
//     `Dictionary` 遍历顺序差异不影响语义。
// 11) 注意（Go 允许、C# 不允许的调用约定）：`extendExportSymbols` 的 `source` 与 `target` 若是**同一个** Dictionary 实例，
//    Go 允许边 range 边写而 C# 会抛 `InvalidOperationException`；本文件的两个调用点传入的都是互相独立的新表
//    （`symbols` 是 `symbol.Exports` 的克隆、`nestedSymbols` 是新表），与 Go 的调用点一致。

namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Binder.Binder;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    /// <summary>
    /// lateBindMember: Late-bind a member name, if it isn't already bound.
    /// </summary>
    /// <param name="parent">Parent symbol of the member</param>
    /// <param name="earlySymbols">Symbol table containing early-bound members of the parent</param>
    /// <param name="lateSymbols">Symbol table containing late-bound members of the parent</param>
    /// <param name="decl">The member to bind.</param>
    internal Symbol LateBindMember(Symbol parent, SymbolTable earlySymbols, SymbolTable lateSymbols, Node decl)
    {
        Debug.Assert(decl.Symbol is not null, "The member is expected to have a symbol.");
        var links = _symbolNodeLinks.Get(decl);
        if (links._resolvedSymbol is null)
        {
            // In the event we attempt to resolve the late-bound name of this member recursively,
            // fall back to the early-bound name of this member.
            links._resolvedSymbol = decl.Symbol;
            Node declName;
            if (IsBinaryExpression(decl))
            {
                declName = decl.AsBinaryExpression()!.Left!;
            }
            else
            {
                declName = decl.Name()!;
            }

            Type t;
            if (IsElementAccessExpression(declName))
            {
                t = CheckExpressionCached(declName.AsElementAccessExpression()!.ArgumentExpression!);
            }
            else
            {
                t = CheckComputedPropertyName(declName);
            }

            if (IsTypeUsableAsPropertyName(t))
            {
                var memberName = GetPropertyNameFromType(t);
                var symbolFlags = decl.Symbol!.Flags;
                // Get or add a late-bound symbol for the member. This allows us to merge late-bound accessor declarations.
                var lateSymbol = lateSymbols.GetValueOrDefault(memberName);
                if (lateSymbol is null)
                {
                    lateSymbol = NewSymbolEx(SymbolFlags.None, memberName, CheckFlags.Late);
                    lateSymbols[memberName] = lateSymbol;
                }

                // Report an error if there's a symbol declaration with the same name and conflicting flags.
                var earlySymbol = earlySymbols.GetValueOrDefault(memberName);
                if ((lateSymbol.Flags & GetExcludedSymbolFlags(symbolFlags)) != 0)
                {
                    // If we have an existing early-bound member, combine its declarations so that we can
                    // report an error at each declaration.
                    Node[] declarations;
                    if (earlySymbol is not null)
                    {
                        declarations = Concatenate(earlySymbol.Declarations, lateSymbol.Declarations);
                    }
                    else
                    {
                        declarations = lateSymbol.Declarations ?? [];
                    }

                    var name = memberName;
                    if ((t._flags & TypeFlags.UniqueESSymbol) != 0)
                    {
                        name = DeclarationNameToString(declName);
                    }

                    foreach (var d in declarations)
                    {
                        Error(OrElse(GetNameOfDeclaration(d), d), Diagnostics.Duplicate_identifier_0, name);
                    }

                    Error(OrElse(declName, decl), Diagnostics.Duplicate_identifier_0, name);

                    if ((lateSymbol.Flags & SymbolFlags.Accessor) != 0 && (lateSymbol.Flags & SymbolFlags.Accessor) != (symbolFlags & SymbolFlags.Accessor))
                    {
                        lateSymbol.Flags |= SymbolFlags.Accessor;
                    }

                    lateSymbol = NewSymbolEx(SymbolFlags.None, memberName, CheckFlags.Late);
                }

                _valueSymbolLinks.Get(lateSymbol)._nameType = t;
                AddDeclarationToLateBoundSymbol(lateSymbol, decl, symbolFlags);
                if (lateSymbol.Parent is null)
                {
                    lateSymbol.Parent = parent;
                }

                links._resolvedSymbol = lateSymbol;
            }
        }

        return links._resolvedSymbol!;
    }

    internal void LateBindIndexSignature(Symbol parent, SymbolTable earlySymbols, SymbolTable lateSymbols, Node decl)
    {
        // First, late bind the index symbol itself, if needed
        var indexSymbol = lateSymbols.GetValueOrDefault(InternalSymbolNameIndex);
        if (indexSymbol is null)
        {
            var early = earlySymbols.GetValueOrDefault(InternalSymbolNameIndex);
            if (early is null)
            {
                indexSymbol = NewSymbolEx(SymbolFlags.None, InternalSymbolNameIndex, CheckFlags.Late);
            }
            else
            {
                indexSymbol = CloneSymbol(early);
                indexSymbol.CheckFlags |= CheckFlags.Late;
            }

            lateSymbols[InternalSymbolNameIndex] = indexSymbol;
        }

        // Then just add the computed name as a late bound declaration
        // (note: unlike `addDeclarationToLateBoundSymbol` we do not set up a `.lateSymbol` on `decl`'s links,
        // since that would point at an index symbol and not a single property symbol, like most consumers would expect)
        if (indexSymbol.Declarations is null || indexSymbol.Declarations.Length == 0 || (decl.Symbol!.Flags & SymbolFlags.ReplaceableByMethod) == 0)
        {
            indexSymbol.Declarations = [.. (indexSymbol.Declarations ?? []), decl];
        }
    }

    internal static bool IsNotReplacableByMethod(Node decl)
    {
        return (decl.Symbol!.Flags & SymbolFlags.ReplaceableByMethod) == 0;
    }

    /// <summary>
    /// Adds a declaration to a late-bound dynamic member. This performs the same function for
    /// late-bound members that `addDeclarationToSymbol` in binder.ts performs for early-bound
    /// members.
    /// </summary>
    internal void AddDeclarationToLateBoundSymbol(Symbol symbol, Node member, SymbolFlags symbolFlags)
    {
        Debug.Assert((symbol.CheckFlags & CheckFlags.Late) != 0, "Expected a late-bound symbol.");
        _lateBoundLinks.Get(member.Symbol!)._lateSymbol = symbol;
        if (symbol.Declarations is null || symbol.Declarations.Length == 0 || (member.Symbol!.Flags & SymbolFlags.ReplaceableByMethod) == 0)
        {
            symbol.Flags |= symbolFlags;
            symbol.Declarations = [.. (symbol.Declarations ?? []), member];
        }
        else if ((symbol.Flags & SymbolFlags.ReplaceableByMethod) != 0 && (member.Symbol!.Flags & SymbolFlags.Method) != 0)
        {
            // Remove all replacable-by-method members, along with their flags.
            symbol.Declarations = [.. Filter(symbol.Declarations, IsNotReplacableByMethod), member];
            var oldFlags = symbol.Flags;
            symbol.Flags = SymbolFlags.None;
            foreach (var d in symbol.Declarations)
            {
                symbol.Flags |= d.Symbol!.Flags;
            }

            if ((oldFlags & SymbolFlags.Accessor) != 0)
            {
                symbol.Flags |= SymbolFlags.Accessor;
            }
        }

        if ((symbolFlags & SymbolFlags.Value) != 0)
        {
            SetValueDeclaration(symbol, member);
        }
    }

    /// <summary>
    /// Gets a SymbolTable containing both the early- and late-bound members of a symbol.
    ///
    /// For a description of late-binding, see `lateBindMember`.
    /// </summary>
    /// <remarks>
    /// Go 返回 `symbol.Members`（可能是 nil map，Go 侧调用点靠 `len`/`range` 把 nil 当空表）。
    /// C# 侧的调用点（Checker.C8.Aliases.cs、Services.cs、EmitResolver、本片其它文件）都按**非空**使用
    /// （`.Count`、直接传给收 `SymbolTable` 的参数），所以这里对 nil 返回一个**新建的空表**：
    /// 读语义与 Go 的 nil map 完全一致（Count/Len 为 0、遍历 0 次），且不会像共享空表那样让写操作污染别人。
    /// </remarks>
    internal SymbolTable GetMembersOfSymbol(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.LateBindingContainer) != 0)
        {
            return GetResolvedMembersOrExportsOfSymbol(symbol, MembersOrExportsResolutionKind.ResolvedMembers);
        }

        return symbol.Members ?? new SymbolTable();
    }

    internal SymbolTable GetExportsOfModuleWorker(Symbol moduleSymbol)
    {
        var links = _moduleSymbolLinks.Get(moduleSymbol);
        if (links._resolvedExports is null)
        {
            var (exports, typeOnlyExportStarMap) = GetExportsOfModuleWorkerImpl(moduleSymbol);
            links._resolvedExports = exports;
            links._typeOnlyExportStarMap = typeOnlyExportStarMap;
        }

        return links._resolvedExports;
    }

    internal (SymbolTable Exports, Dictionary<string, Node>? TypeOnlyExportStarMap) GetExportsOfModuleWorkerImpl(Symbol moduleSymbol)
    {
        var visitedSymbols = new List<Symbol>();
        var nonTypeOnlyNames = new Set<string>(moduleSymbol.Exports?.Count ?? 0);
        Dictionary<string, Node>? typeOnlyExportStarMap = null;

        // The ES6 spec permits export * declarations in a module to circularly reference the module itself. For example,
        // module 'a' can 'export * from "b"' and 'b' can 'export * from "a"' without error.
        SymbolTable? Visit(Symbol? symbol, Node? exportStar, bool isTypeOnly)
        {
            if (!isTypeOnly && symbol is not null)
            {
                // Add non-type-only names before checking if we've visited this module,
                // because we might have visited it via an 'export type *', and visiting
                // again with 'export *' will override the type-onlyness of its exports.
                foreach (var name in symbol.Exports!.Keys)
                {
                    nonTypeOnlyNames.Add(name);
                }
            }

            if (symbol is null || symbol.Exports is null || visitedSymbols.Contains(symbol))
            {
                return null;
            }

            visitedSymbols.Add(symbol);
            var symbols = new SymbolTable();
            foreach (var entry in symbol.Exports)
            {
                symbols[entry.Key] = entry.Value;
            }

            // All export * declarations are collected in an __export symbol by the binder
            var exportStars = symbol.Exports.GetValueOrDefault(InternalSymbolNameExportStar);
            if (exportStars is not null)
            {
                var nestedSymbols = new SymbolTable();
                var lookupTable = new Dictionary<string, ExportCollision>();
                foreach (var node in exportStars.Declarations!)
                {
                    var resolvedModule = ResolveExternalModuleNameWorker(node, node.ModuleSpecifier()!, false /*ignoreErrors*/, GetTypeFromImportAttributes(GetImportAttributes(node)!));
                    var exportedSymbols = Visit(resolvedModule, node, isTypeOnly || node.IsTypeOnly());
                    ExtendExportSymbols(nestedSymbols, exportedSymbols, lookupTable, node);
                }

                foreach (var pair in lookupTable)
                {
                    var id = pair.Key;
                    var s = pair.Value;
                    // It's not an error if the file with multiple `export *`s with duplicate names exports a member with that name itself
                    if (id == InternalSymbolNameExportEquals || (s._exportsWithDuplicate?.Length ?? 0) == 0 || symbols.ContainsKey(id))
                    {
                        continue;
                    }

                    foreach (var node in s._exportsWithDuplicate!)
                    {
                        AddDiagnostic(CreateDiagnosticForNode(node, Diagnostics.Module_0_has_already_exported_a_member_named_1_Consider_explicitly_re_exporting_to_resolve_the_ambiguity, s._specifierText, id));
                    }
                }

                ExtendExportSymbols(symbols, nestedSymbols, null, null);
            }

            if (exportStar is not null && exportStar.IsTypeOnly())
            {
                typeOnlyExportStarMap ??= new Dictionary<string, Node>();
                foreach (var name in symbols.Keys)
                {
                    typeOnlyExportStarMap[name] = exportStar;
                }
            }

            return symbols;
        }

        Symbol? originalModule = null;
        if (ResolveSymbolEx(moduleSymbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals), false /*dontResolveAlias*/) is not null)
        {
            originalModule = moduleSymbol;
        }

        // A module defined by an 'export=' consists of one export that needs to be resolved
        // Go 把结果赋回参数 `moduleSymbol`（其后只用于 `visit`）；C# 里 `resolveExternalModuleSymbol` 的返回是可空语义，
        // 因此用局部量承载，避免把 `Symbol?` 赋给非空参数（CS8600），语义与 Go 完全一致（visit 接受 nil）。
        var resolvedModuleSymbol = ResolveExternalModuleSymbolWorker(moduleSymbol, false /*dontResolveAlias*/);
        var exports2 = Visit(resolvedModuleSymbol, null, false);
        if (exports2 is null)
        {
            exports2 = new SymbolTable();
        }

        // A CommonJS module defined by an 'export=' might also export typedefs, stored on the original module
        if (originalModule is not null && (originalModule.Exports?.Count ?? 0) > 1)
        {
            foreach (var symbol in originalModule.Exports!.Values)
            {
                if (symbol.Name == InternalSymbolNameExportEquals || symbol.Name == InternalSymbolNameExportStar)
                {
                    continue;
                }

                var flags = GetSymbolFlagsWorker(symbol);
                if ((flags & (SymbolFlags.Type | SymbolFlags.Namespace)) != 0 &&
                    (flags & SymbolFlags.Value) == 0 &&
                    !exports2.ContainsKey(symbol.Name))
                {
                    exports2[symbol.Name] = symbol;
                }
            }
        }

        if (typeOnlyExportStarMap is not null)
        {
            foreach (var name in nonTypeOnlyNames.Keys())
            {
                typeOnlyExportStarMap.Remove(name);
            }
        }

        return (exports2, typeOnlyExportStarMap);
    }

    /// <summary>
    /// Extends one symbol table with another while collecting information on name collisions for error message generation into the `lookupTable` argument
    /// Not passing `lookupTable` and `exportNode` disables this collection, and just extends the tables
    /// </summary>
    internal void ExtendExportSymbols(SymbolTable target, SymbolTable? source, Dictionary<string, ExportCollision>? lookupTable, Node? exportNode)
    {
        if (source is null)
        {
            // Go: `for range <nil map>` 是空操作。
            return;
        }

        foreach (var pair in source)
        {
            var id = pair.Key;
            var sourceSymbol = pair.Value;
            if (id == InternalSymbolNameDefault)
            {
                continue;
            }

            var targetSymbol = target.GetValueOrDefault(id);
            if (targetSymbol is null)
            {
                target[id] = sourceSymbol;
                if (lookupTable is not null && exportNode is not null)
                {
                    lookupTable[id] = new ExportCollision { _specifierText = GetTextOfNode(exportNode.ModuleSpecifier()!) };
                }
            }
            else if (lookupTable is not null && exportNode is not null && !ReferenceEquals(ResolveSymbol(targetSymbol), ResolveSymbol(sourceSymbol)))
            {
                var s = lookupTable[id];
                s._exportsWithDuplicate = [.. (s._exportsWithDuplicate ?? []), exportNode];
            }
        }
    }

    public bool ResolveAlias(Symbol? symbol, out Symbol? resolved)
    {
        if (symbol is null)
        {
            resolved = null;
            return false;
        }

        resolved = ResolveAliasWorker(symbol);
        return !ReferenceEquals(resolved, _unknownSymbol);
    }

    /// <summary>
    /// Resolve an alias symbol to the first target symbol in the resolution chain that includes some other
    /// meaning. Pure aliases are eagerly resolved and any type-only markers are back-propagated to the original
    /// symbol. The function panics if the argument is not a symbol with an alias meaning.
    /// </summary>
    internal Symbol ResolveAliasWorker(Symbol symbol)
    {
        if ((symbol.Flags & SymbolFlags.Alias) == 0)
        {
            Debug.Fail("Should only get alias here");
        }

        var links = _aliasSymbolLinks.Get(symbol);
        if (links._aliasTarget is null)
        {
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.AliasTarget))
            {
                return _unknownSymbol!;
            }

            var node = GetDeclarationOfAliasSymbol(symbol);
            if (node is null)
            {
                Debug.Fail("Unexpected nil in resolveAlias for symbol: " + SymbolToStringWorker(symbol));
            }

            var target = GetTargetOfAliasDeclaration(node!);
            if (IsNonLocalAlias(target, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace))
            {
                // When the target is a pure alias, we transitively resolve and propagate any typeOnlyDeclaration
                target = ResolveIndirectionAlias(symbol, target!);
            }

            links._aliasTarget = OrElse(target, _unknownSymbol);
            if (!PopTypeResolution())
            {
                Error(node, Diagnostics.Circular_definition_of_import_alias_0, SymbolToStringWorker(symbol));
                links._aliasTarget = _unknownSymbol;
            }
        }

        return links._aliasTarget!;
    }

    internal Symbol ResolveIndirectionAlias(Symbol source, Symbol target)
    {
        // Go 的 resolveAlias 对非 nil 输入恒返回非 nil（最差是 unknownSymbol），getMergedSymbol 对非 nil 输入也恒返回非 nil（checker.go:14581）
        var result = GetMergedSymbolWorker(ResolveAliasWorker(target))!;
        var targetLinks = _aliasSymbolLinks.Get(target);
        if (targetLinks._typeOnlyDeclaration is not null)
        {
            var sourceLinks = _aliasSymbolLinks.Get(source);
            if (sourceLinks._typeOnlyDeclaration is null)
            {
                sourceLinks._typeOnlyDeclaration = targetLinks._typeOnlyDeclaration;
            }
        }

        return result;
    }

    internal Symbol? TryResolveAlias(Symbol symbol)
    {
        var links = _aliasSymbolLinks.Get(symbol);
        if (links._aliasTarget is not null || FindResolutionCycleStartIndex(symbol, TypeSystemPropertyName.AliasTarget) < 0)
        {
            return ResolveAliasWorker(symbol);
        }

        return null;
    }

    internal Symbol ResolveAliasWithDeprecationCheck(Symbol symbol, Node location)
    {
        if ((symbol.Flags & SymbolFlags.Alias) == 0 || IsDeprecatedSymbol(symbol) || GetDeclarationOfAliasSymbol(symbol) is null)
        {
            return symbol;
        }

        var targetSymbol = ResolveAliasWorker(symbol);
        if (ReferenceEquals(targetSymbol, _unknownSymbol))
        {
            return targetSymbol;
        }

        while ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            var target = GetImmediateAliasedSymbolWorker(symbol);
            if (target is not null)
            {
                if (ReferenceEquals(target, targetSymbol))
                {
                    break;
                }

                if (target.Declarations is not null && target.Declarations.Length != 0)
                {
                    if (IsDeprecatedSymbol(target))
                    {
                        AddDeprecatedSuggestion(location, target.Declarations, target.Name);
                        break;
                    }
                    else
                    {
                        if (ReferenceEquals(symbol, targetSymbol))
                        {
                            break;
                        }

                        symbol = target;
                    }
                }
            }
            else
            {
                break;
            }
        }

        return targetSymbol;
    }

    /// <summary>
    /// Gets combined flags of a `symbol` and all alias targets it resolves to. `resolveAlias`
    /// is typically recursive over chains of aliases, but stops mid-chain if an alias is merged
    /// with another exported symbol, e.g.
    /// <code>
    /// // a.ts
    /// export const a = 0;
    /// // b.ts
    /// export { a } from "./a";
    /// export type a = number;
    /// // c.ts
    /// import { a } from "./b";
    /// </code>
    /// Calling `resolveAlias` on the `a` in c.ts would stop at the merged symbol exported
    /// from b.ts, even though there is still more alias to resolve. Consequently, if we were
    /// trying to determine if the `a` in c.ts has a value meaning, looking at the flags on
    /// the local symbol and on the symbol returned by `resolveAlias` is not enough.
    /// </summary>
    /// <returns>`SymbolFlags.All` if `symbol` is an alias that ultimately resolves to `unknown`;
    /// combined flags of all alias targets otherwise.</returns>
    internal SymbolFlags GetSymbolFlagsWorker(Symbol symbol)
    {
        return GetSymbolFlagsEx(symbol, false /*excludeTypeOnlyMeanings*/, false /*excludeLocalMeanings*/);
    }

    internal SymbolFlags GetSymbolFlagsEx(Symbol symbol, bool excludeTypeOnlyMeanings, bool excludeLocalMeanings)
    {
        Set<Symbol>? seenSymbols = null;
        var flags = SymbolFlags.None;
        if (!excludeLocalMeanings)
        {
            flags = symbol.Flags;
        }

        while ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            if (excludeTypeOnlyMeanings && GetTypeOnlyAliasDeclarationWorker(symbol) is not null)
            {
                break;
            }

            var target = GetExportSymbolOfValueSymbolIfExported(ResolveAliasWorker(symbol));
            if (ReferenceEquals(target, _unknownSymbol))
            {
                return SymbolFlags.All;
            }

            if ((target!.Flags & SymbolFlags.Alias) != 0)
            {
                // Optimization - try to avoid creating or adding to `seenSymbols` if possible
                if (ReferenceEquals(target, symbol) || (seenSymbols?.Has(target) ?? false))
                {
                    break;
                }

                if (seenSymbols is null || seenSymbols.Len() == 0)
                {
                    seenSymbols ??= new Set<Symbol>();
                    seenSymbols.Add(symbol);
                }

                seenSymbols.Add(target);
            }

            flags |= target.Flags;
            symbol = target;
        }

        return flags;
    }

    internal Node? GetDeclarationOfAliasSymbol(Symbol symbol)
    {
        return FindLast(symbol.Declarations, IsAliasSymbolDeclaration);
    }
}

/// <summary>Go: <c>type ExportCollision struct</c>（checker.go:16458；Go 里类型名导出、字段未导出）。</summary>
public sealed class ExportCollision
{
    internal string _specifierText = "";
    internal Node[]? _exportsWithDuplicate;
}
