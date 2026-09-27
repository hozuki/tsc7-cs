// ported from: internal/checker/services.go:1-1152
//
// 本文件是 `internal/checker/services.go` 的 1:1 移植：对外的语言服务查询（`GetTypeAtPosition` 家族、
// 符号/导出查询）+ 只在 checker 内部使用的辅助函数。
//
// Go→C# 语义改写（逐条）：
//  1) Worker 后缀（见 docs/checker-plan.md §5.2、`artifacts/checker-method-collisions.md`）：Go 里同时存在导出
//     `GetX`/未导出 `getX` 时，**导出者保持 Go 名**（对外 API），**未导出者加 `Worker`**。本文件中的三对都由本
//     切片处理：`getSymbolsInScope` → `GetSymbolsInScopeWorker`、`isValidPropertyAccess` → `IsValidPropertyAccessWorker`、
//     `getTypeArgumentConstraint` → `GetTypeArgumentConstraintWorker`。跨文件的未导出实现同样按清单加 `Worker`
//     （`GetExportsOfModuleWorker`、`GetUnionTypeWorker`、`GetSignaturesOfTypeWorker`、`GetPropertyOfTypeWorker` …）。
//  2) Go 包级函数 → 同一个 `partial class Checker` 的 `static` 成员：`GetResolvedSignatureForSignatureHelp`
//     （导出 → `public static`）、`IsExportSpecifierAlias`/`GetPossibleSymbolReferenceNodes`/
//     `GetPossibleSymbolReferencePositions`/`IsKnownGenericTypeName`（未导出 → `private static`）。
//     包级 `knownGenericTypeNames` 是 `map[string]struct{}` 的"集合"用法 → `private static readonly HashSet<string>`。
//  3) 字段：Go 未导出字段 → `_camelCase`（`c.symbolNodeLinks` → `_symbolNodeLinks`、`c.errorType` → `_errorType`…）。
//  4) 泛型：Go 的 `runWithInferenceBlockedFromSourceNode[T any](node, fn)` → C# 泛型方法
//     `T RunWithInferenceBlockedFromSourceNode<T>(Node node, Func<T> fn)`（对 `runWithoutResolvedSignatureCaching` 同理）。
//  5) nil 切片 → `[]`（Go 的 `len(nil)==0` 与空数组等价，docs §3.2）：`getImmediateRootSymbols`/`getUninstantiatedSignatures`/
//     `GetPropertySymbolsFromContextualType` 等返回 `T[]`（空数组）而不是 null。
//  6) `map[K]V` 的读取：Go 用 `v, ok := m[k]` 的地方 → C# `TryGetValue`/`ContainsKey`（`SymbolTable` 索引器读缺失键会抛）。
//  7) `panic(...)` → `throw new DebugFailureException(...)`（Node.cs 同款；`Debug.Fail` 只是它的包装，throw 让控制流被编译器接受）。
//  8) `Type` 的字段访问 `t.flags`/`t.objectFlags`/`t.symbol`/`t.alias` → C# 的内部字段 `t._flags`/`t._objectFlags`/`t._symbol`/`t._alias`
//     （Go 里 `t.Symbol()`/`t.Alias()` 只是这两个字段的访问器，逐字对应；用字段读避免与类型名同名的遮蔽问题）。
//  9) `core.Some/Every/Filter/MapNonNil/Deduplicate/FirstOrNil/IfElse/OrElse` → `Core.*`（同名，`T[]?` + `Func<T,U>`）。
// 10) `strings.Index(text[container.Pos():], symbolName)`（Go 的**字节**下标 + 相对偏移的怪写法）→ 逐字保留：
//     `text.AsSpan(container.Pos()).IndexOf(symbolName.AsSpan())` 返回相对下标，后续按下标访问 `text` 与 Go 完全一致
//     （仅当 symbolName 含非 ASCII 时与 Go 的字节语义不同，见 docs/architecture.md §3.1）。
// 11) `scanner.IsIdentifierPart(rune(text[i]))` → `Scanner.IsIdentifierPart(text[i])`（C# 的 char = UTF-16 代码单元 → int 参数）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;

public sealed partial class Checker
{
    public Symbol[] GetSymbolsInScope(Node location, SymbolFlags meaning) => GetSymbolsInScopeWorker(location, meaning);

    internal Symbol[] GetSymbolsInScopeWorker(Node location, SymbolFlags meaning)
    {
        if ((location.Flags & NodeFlags.InWithStatement) != 0)
        {
            // We cannot answer semantic questions within a with block, do not proceed any further
            return [];
        }

        var symbols = new SymbolTable();
        var isStaticSymbol = false;

        // Copy the given symbol into symbol tables if the symbol has the given meaning
        // and it doesn't already exists in the symbol table.
        void copySymbol(Symbol symbol, SymbolFlags meaning)
        {
            if ((symbol.CombinedLocalAndExportSymbolFlags() & meaning) != 0)
            {
                var id = symbol.Name;
                // We will copy all symbol regardless of its reserved name because
                // symbolsToArray will check whether the key is a reserved name and
                // it will not copy symbol with reserved name to the array
                if (!symbols.ContainsKey(id))
                {
                    symbols[id] = symbol;
                }
            }
        }

        void copySymbols(SymbolTable? source, SymbolFlags meaning)
        {
            if (meaning != 0)
            {
                // Go 的 `for _, symbol := range source`：nil map 迭代 0 次（C# 里 SymbolTable 为 null 同理）。
                if (source is not null)
                {
                    foreach (var symbol in source.Values)
                    {
                        copySymbol(symbol, meaning);
                    }
                }
            }
        }

        void copyLocallyVisibleExportSymbols(SymbolTable? source, SymbolFlags meaning)
        {
            if (meaning != 0)
            {
                if (source is not null)
                {
                    foreach (var symbol in source.Values)
                    {
                        // Similar condition as in `resolveNameHelper`
                        if (GetDeclarationOfKind(symbol, Kind.ExportSpecifier) is null &&
                            GetDeclarationOfKind(symbol, Kind.NamespaceExport) is null &&
                            symbol.Name != InternalSymbolNameDefault)
                        {
                            copySymbol(symbol, meaning);
                        }
                    }
                }
            }
        }

        void populateSymbols()
        {
            Node? current = location;
            Node? lastLocation = null;
            while (current is not null)
            {
                if (IsModuleDeclaration(current) && current.AsModuleDeclaration()!.Attributes is not null &&
                    ReferenceEquals(lastLocation, current.AsModuleDeclaration()!.Attributes))
                {
                    // Module declaration is not in scope inside its attributes.
                    lastLocation = current;
                    current = current.Parent;
                    continue;
                }

                if (CanHaveLocals(current) && current.Locals() is not null && !IsGlobalSourceFile(current))
                {
                    copySymbols(current.Locals(), meaning);
                }

                switch (current.Kind)
                {
                    case Kind.SourceFile:
                        if (!IsExternalModule(current.AsSourceFile()))
                        {
                            break;
                        }
                        goto case Kind.ModuleDeclaration;

                    case Kind.ModuleDeclaration:
                        copyLocallyVisibleExportSymbols(
                            GetSymbolOfDeclaration(current)!.Exports,
                            meaning & SymbolFlags.ModuleMember);
                        break;

                    case Kind.EnumDeclaration:
                        copySymbols(
                            GetSymbolOfDeclaration(current)!.Exports,
                            meaning & SymbolFlags.EnumMember);
                        break;

                    case Kind.ClassExpression:
                        {
                            var className = current.AsClassExpression()!.Name();
                            if (className is not null)
                            {
                                copySymbol(current.Symbol!, meaning);
                            }
                            // this fall-through is necessary because we would like to handle
                            // type parameter inside class expression similar to how we handle it in classDeclaration and interface Declaration.
                            goto case Kind.ClassDeclaration;
                        }

                    case Kind.ClassDeclaration:
                    case Kind.InterfaceDeclaration:
                        // If we didn't come from static member of class or interface,
                        // add the type parameters into the symbol table
                        // (type parameters of classDeclaration/classExpression and interface are in member property of the symbol.
                        // Note: that the memberFlags come from previous iteration.
                        if (!isStaticSymbol)
                        {
                            copySymbols(
                                GetMembersOfSymbol(GetSymbolOfDeclaration(current)!),
                                meaning & SymbolFlags.Type);
                        }
                        break;

                    case Kind.FunctionExpression:
                        {
                            var funcName = current.Name();
                            if (funcName is not null)
                            {
                                copySymbol(current.Symbol!, meaning);
                            }
                            break;
                        }
                }

                if (IntroducesArgumentsExoticObject(current))
                {
                    copySymbol(_argumentsSymbol!, meaning);
                }

                isStaticSymbol = IsStatic(current);
                lastLocation = current;
                current = current.Parent;
            }

            copySymbols(_globals, meaning);
        }

        populateSymbols();

        symbols.Remove(InternalSymbolNameThis); // Not a symbol, a keyword
        return SymbolsToArray(symbols);
    }

    public Symbol[] GetExportsOfModule(Symbol symbol) => SymbolsToArray(GetExportsOfModuleWorker(symbol));

    public void ForEachExportAndPropertyOfModule(Symbol moduleSymbol, Action<Symbol, string> cb)
    {
        foreach (var entry in GetExportsOfModuleWorker(moduleSymbol))
        {
            if (!IsReservedMemberName(entry.Key))
            {
                cb(entry.Value, entry.Key);
            }
        }

        var exportEquals = ResolveExternalModuleSymbolWorker(moduleSymbol, false /*dontResolveAlias*/);
        if (ReferenceEquals(exportEquals, moduleSymbol))
        {
            return;
        }

        var typeOfSymbol = GetTypeOfSymbolWorker(exportEquals!);
        if (!ShouldTreatPropertiesOfExternalModuleAsExports(typeOfSymbol))
        {
            return;
        }

        // forEachPropertyOfType
        var reducedType = GetReducedApparentType(typeOfSymbol);
        if ((reducedType._flags & TypeFlags.StructuredType) == 0)
        {
            return;
        }
        foreach (var entry in ResolveStructuredTypeMembers(reducedType)._members!)
        {
            if (IsNamedMember(entry.Value, entry.Key))
            {
                cb(entry.Value, entry.Key);
            }
        }
    }

    public bool IsValidPropertyAccess(Node node, string propertyName) => IsValidPropertyAccessWorker(node, propertyName);

    internal bool IsValidPropertyAccessWorker(Node node, string propertyName)
    {
        switch (node.Kind)
        {
            case Kind.PropertyAccessExpression:
                return IsValidPropertyAccessWithType(
                    node,
                    node.Expression()!.Kind == Kind.SuperKeyword,
                    propertyName,
                    GetWidenedTypeWorker(CheckExpression(node.Expression()!)));
            case Kind.QualifiedName:
                return IsValidPropertyAccessWithType(
                    node,
                    false /*isSuper*/,
                    propertyName,
                    GetWidenedTypeWorker(CheckExpression(node.AsQualifiedName()!.Left!)));
            case Kind.ImportType:
                return IsValidPropertyAccessWithType(node, false /*isSuper*/, propertyName, GetTypeFromTypeNodeWorker(node));
        }
        throw new DebugFailureException("Unexpected node kind in isValidPropertyAccess: " + node.Kind.String());
    }

    internal bool IsValidPropertyAccessWithType(Node node, bool isSuper, string propertyName, Type t)
    {
        // Short-circuiting for improved performance.
        if (IsTypeAny(t))
        {
            return true;
        }

        var prop = GetPropertyOfTypeWorker(t, propertyName);
        return prop is not null && IsPropertyAccessibleWorker(node, isSuper, false /*isWrite*/, t, prop);
    }

    // Checks if an existing property access is valid for completions purposes.
    // node: a property access-like node where we want to check if we can access a property.
    // This node does not need to be an access of the property we are checking.
    // e.g. in completions, this node will often be an incomplete property access node, as in `foo.`.
    // Besides providing a location (i.e. scope) used to check property accessibility, we use this node for
    // computing whether this is a `super` property access.
    // type: the type whose property we are checking.
    // property: the accessed property's symbol.
    public bool IsValidPropertyAccessForCompletions(Node node, Type t, Symbol property) =>
        IsPropertyAccessibleWorker(
            node,
            node.Kind == Kind.PropertyAccessExpression && node.Expression()!.Kind == Kind.SuperKeyword,
            false, /*isWrite*/
            t,
            property);

    // Previously we validated the 'this' type of methods but this adversely affected performance. See #31377 for more context.

    public Symbol[] GetAllPossiblePropertiesOfTypes(Type[] types)
    {
        var unionType = GetUnionTypeWorker(types);
        if ((unionType._flags & TypeFlags.Union) == 0)
        {
            return GetAugmentedPropertiesOfType(unionType);
        }

        var props = new SymbolTable();
        foreach (var memberType in types)
        {
            var augmentedProps = GetAugmentedPropertiesOfType(memberType);
            foreach (var p in augmentedProps)
            {
                if (!props.ContainsKey(p.Name))
                {
                    var prop = CreateUnionOrIntersectionProperty(unionType, p.Name, false /*skipObjectFunctionPropertyAugment*/);
                    // May be undefined if the property is private
                    if (prop is not null)
                    {
                        props[p.Name] = prop;
                    }
                }
            }
        }

        var result = new Symbol[props.Count];
        var i = 0;
        foreach (var value in props.Values)
        {
            result[i++] = value;
        }
        return result;
    }

    public bool IsUnknownSymbol(Symbol? symbol) => ReferenceEquals(symbol, _unknownSymbol);

    public bool IsUndefinedSymbol(Symbol? symbol) => ReferenceEquals(symbol, _undefinedSymbol);

    public bool IsArgumentsSymbol(Symbol? symbol) => ReferenceEquals(symbol, _argumentsSymbol);

    // Originally from services.ts
    public Type? GetNonOptionalType(Type t) => RemoveOptionalTypeMarker(t);

    public Type? GetStringIndexType(Type t) => GetIndexTypeOfTypeWorker(t, _stringType);

    public Type? GetNumberIndexType(Type t) => GetIndexTypeOfTypeWorker(t, _numberType);

    public Type? GetElementTypeOfArrayType(Type t) => GetElementTypeOfArrayTypeWorker(t);

    public Signature[] GetCallSignatures(Type t) => GetSignaturesOfTypeWorker(t, SignatureKind.Call) ?? [];

    public Signature[] GetConstructSignatures(Type t) => GetSignaturesOfTypeWorker(t, SignatureKind.Construct) ?? [];

    public Symbol[] GetApparentProperties(Type t) => GetAugmentedPropertiesOfType(t);

    internal Symbol[] GetAugmentedPropertiesOfType(Type t)
    {
        t = GetApparentTypeWorker(t);
        var propsByName = CreateSymbolTable(GetPropertiesOfTypeWorker(t));
        Type? functionType = null;
        if ((GetSignaturesOfTypeWorker(t, SignatureKind.Call) ?? []).Length > 0)
        {
            functionType = _globalCallableFunctionType;
        }
        else if ((GetSignaturesOfTypeWorker(t, SignatureKind.Construct) ?? []).Length > 0)
        {
            functionType = _globalNewableFunctionType;
        }

        propsByName ??= new SymbolTable();
        if (functionType is not null)
        {
            foreach (var p in GetPropertiesOfTypeWorker(functionType) ?? [])
            {
                if (!propsByName.ContainsKey(p.Name))
                {
                    propsByName[p.Name] = p;
                }
            }
        }
        return GetNamedMembers(propsByName, null);
    }

    public Symbol? TryGetMemberInModuleExportsAndProperties(string memberName, Symbol moduleSymbol)
    {
        var symbol = TryGetMemberInModuleExports(memberName, moduleSymbol);
        if (symbol is not null)
        {
            return symbol;
        }

        var exportEquals = ResolveExternalModuleSymbolWorker(moduleSymbol, false /*dontResolveAlias*/);
        if (ReferenceEquals(exportEquals, moduleSymbol))
        {
            return null;
        }

        var t = GetTypeOfSymbolWorker(exportEquals!);
        if (ShouldTreatPropertiesOfExternalModuleAsExports(t))
        {
            return GetPropertyOfTypeWorker(t, memberName);
        }
        return null;
    }

    public Symbol? TryGetMemberInModuleExports(string memberName, Symbol moduleSymbol)
    {
        var symbolTable = GetExportsOfModuleWorker(moduleSymbol);
        return symbolTable.TryGetValue(memberName, out var symbol) ? symbol : null;
    }

    internal bool ShouldTreatPropertiesOfExternalModuleAsExports(Type resolvedExternalModuleType) =>
        (resolvedExternalModuleType._flags & TypeFlags.Primitive) == 0 ||
        (resolvedExternalModuleType._objectFlags & ObjectFlags.Class) != 0 ||
        // `isArrayOrTupleLikeType` is too expensive to use in this auto-imports hot path.
        IsArrayTypeWorker(resolvedExternalModuleType) ||
        IsTupleTypeWorker(resolvedExternalModuleType);

    public Type? GetContextualType(Node node, ContextFlags contextFlags)
    {
        if ((contextFlags & ContextFlags.IgnoreNodeInferences) != 0)
        {
            return RunWithInferenceBlockedFromSourceNode(node, () => GetContextualTypeWorker(node, contextFlags));
        }
        return GetContextualTypeWorker(node, contextFlags);
    }

    internal T RunWithInferenceBlockedFromSourceNode<T>(Node node, Func<T> fn)
    {
        var containingCall = FindAncestor(node, n => IsCallLikeExpression(n));
        if (containingCall is not null)
        {
            var toMarkSkip = node;
            while (true)
            {
                _skipDirectInferenceNodes.Add(toMarkSkip);
                toMarkSkip = toMarkSkip.Parent;
                if (toMarkSkip is null || ReferenceEquals(toMarkSkip, containingCall))
                {
                    break;
                }
            }
        }

        _isInferencePartiallyBlocked = true;
        var result = RunWithoutResolvedSignatureCaching(node, fn);
        _isInferencePartiallyBlocked = false;
        _skipDirectInferenceNodes.Clear();
        return result;
    }

    public static (Signature? Signature, Signature[] Candidates) GetResolvedSignatureForSignatureHelp(Node node, int argumentCount, Checker c) =>
        c.RunWithoutResolvedSignatureCaching(node, () => c.GetResolvedSignatureWorkerImpl(node, CheckMode.IsForSignatureHelp, argumentCount));

    internal T RunWithoutResolvedSignatureCaching<T>(Node node, Func<T> fn)
    {
        var ancestorNode = FindAncestor(node, n => IsCallLikeOrFunctionLikeExpression(n));
        if (ancestorNode is not null)
        {
            var cachedResolvedSignatures = new Dictionary<SignatureLinks, Signature?>();
            var cachedTypes = new Dictionary<ValueSymbolLinks, Type?>();
            while (ancestorNode is not null)
            {
                var signatureLinks = _signatureLinks.Get(ancestorNode);
                cachedResolvedSignatures[signatureLinks] = signatureLinks._resolvedSignature;
                signatureLinks._resolvedSignature = null;
                if (IsFunctionExpressionOrArrowFunction(ancestorNode))
                {
                    var symbolLinks = _valueSymbolLinks.Get(GetSymbolOfDeclaration(ancestorNode)!);
                    var resolvedType = symbolLinks._resolvedType;
                    cachedTypes[symbolLinks] = resolvedType;
                    symbolLinks._resolvedType = null;
                }
                ancestorNode = FindAncestor(ancestorNode.Parent, n => IsCallLikeOrFunctionLikeExpression(n));
            }
            var result = fn();
            foreach (var entry in cachedResolvedSignatures)
            {
                entry.Key._resolvedSignature = entry.Value;
            }
            foreach (var entry in cachedTypes)
            {
                entry.Key._resolvedType = entry.Value;
            }
            return result;
        }
        return fn();
    }

    public Symbol? SkipAlias(Symbol symbol) =>
        (symbol.Flags & SymbolFlags.Alias) != 0 ? GetAliasedSymbol(symbol) : symbol;

    public Symbol[] GetRootSymbols(Symbol symbol)
    {
        var roots = GetImmediateRootSymbols(symbol);
        if (roots.Length == 0)
        {
            return [symbol];
        }
        var result = new List<Symbol>();
        foreach (var root in roots)
        {
            result.AddRange(GetRootSymbols(root));
        }
        return result.ToArray();
    }

    public Symbol? GetMappedTypeSymbolOfProperty(Symbol symbol)
    {
        var valueLinks = _valueSymbolLinks.TryGet(symbol);
        if (valueLinks is not null)
        {
            return valueLinks._containingType!._symbol;
        }
        return null;
    }

    internal Symbol[] GetImmediateRootSymbols(Symbol symbol)
    {
        if ((symbol.CheckFlags & CheckFlags.Synthetic) != 0)
        {
            return Core.MapNonNil(
                _valueSymbolLinks.Get(symbol)._containingType!.Types(),
                t => GetPropertyOfTypeWorker(t, symbol.Name)!);
        }
        if ((symbol.Flags & SymbolFlags.Transient) != 0)
        {
            if (_spreadLinks.Has(symbol))
            {
                var leftSpread = _spreadLinks.Get(symbol)._leftSpread;
                var rightSpread = _spreadLinks.Get(symbol)._rightSpread;
                if (leftSpread is not null)
                {
                    return [leftSpread, rightSpread!];
                }
            }
            if (_mappedSymbolLinks.Has(symbol))
            {
                var syntheticOrigin = _mappedSymbolLinks.Get(symbol)._syntheticOrigin;
                if (syntheticOrigin is not null)
                {
                    return [syntheticOrigin];
                }
            }
            var target = TryGetTarget(symbol);
            if (target is not null)
            {
                return [target];
            }
        }
        return [];
    }

    internal Symbol? TryGetTarget(Symbol symbol)
    {
        Symbol? target = null;
        var next = symbol;
        while (true)
        {
            if (_valueSymbolLinks.Has(next))
            {
                next = _valueSymbolLinks.Get(next)._target!;
            }
            else if (_exportTypeLinks.Has(next))
            {
                next = _exportTypeLinks.Get(next)._target!;
            }
            else
            {
                next = null!;
            }
            if (next is null)
            {
                break;
            }
            target = next;
        }
        return target;
    }

    public Symbol GetExportSymbolOfSymbol(Symbol symbol) =>
        GetMergedSymbolWorker(Core.IfElse(symbol.ExportSymbol is not null, symbol.ExportSymbol, symbol))!;

    public Symbol? GetExportSpecifierLocalTargetSymbol(Node node)
    {
        // node should be ExportSpecifier | Identifier
        switch (node.Kind)
        {
            case Kind.ExportSpecifier:
                if (node.Parent!.Parent!.ModuleSpecifier() is not null)
                {
                    return GetExternalModuleMember(node.Parent.Parent, node, false /*dontResolveAlias*/);
                }
                var name = node.PropertyNameOrName()!;
                if (name.Kind == Kind.StringLiteral)
                {
                    // Skip for invalid syntax like this: export { "x" }
                    return null;
                }
                return ResolveEntityName(name, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, true /*ignoreErrors*/, false, null);
            case Kind.Identifier:
                return ResolveEntityName(node, SymbolFlags.Value | SymbolFlags.Type | SymbolFlags.Namespace | SymbolFlags.Alias, true /*ignoreErrors*/, false, null);
        }
        throw new DebugFailureException("Unhandled case in getExportSpecifierLocalTargetSymbol, node should be ExportSpecifier | Identifier");
    }

    public Symbol? GetShorthandAssignmentValueSymbol(Node? location)
    {
        if (location is not null && location.Kind == Kind.ShorthandPropertyAssignment)
        {
            return ResolveEntityName(location.Name()!, SymbolFlags.Value | SymbolFlags.Alias, true /*ignoreErrors*/, false, null);
        }
        return null;
    }

    /// <summary>
    /// Get symbols that represent parameter-property-declaration as parameter and as property declaration
    /// </summary>
    /// <param name="parameter">a parameterDeclaration node</param>
    /// <param name="parameterName">a name of the parameter to get the symbols for.</param>
    /// <returns>a tuple of two symbols</returns>
    public (Symbol ParameterSymbol, Symbol PropertySymbol) GetSymbolsOfParameterPropertyDeclaration(Node parameter /*ParameterPropertyDeclaration*/, string parameterName)
    {
        var constructorDeclaration = parameter.Parent!;
        var classDeclaration = parameter.Parent!.Parent!;

        var parameterSymbol = GetSymbol(constructorDeclaration.Locals()!, parameterName, SymbolFlags.Value);
        var propertySymbol = GetSymbol(GetMembersOfSymbol(classDeclaration.Symbol!), parameterName, SymbolFlags.Value);

        if (parameterSymbol is not null && propertySymbol is not null)
        {
            return (parameterSymbol, propertySymbol);
        }

        throw new DebugFailureException("There should exist two symbols, one as property declaration and one as parameter declaration");
    }

    // IsDeclarationUsed checks if an import declaration identifier is used in the source file.
    // This is primarily used for organizing imports to determine which imports can be removed.
    public bool IsDeclarationUsed(
        SourceFile sourceFile,
        Identifier identifier,
        bool jsxElementsPresent,
        bool jsxModeNeedsExplicitImport)
    {
        if (jsxElementsPresent && jsxModeNeedsExplicitImport)
        {
            var jsxNamespace = GetJsxNamespaceWorker(sourceFile.AsNode());
            var jsxFragmentFactory = GetJsxFragmentFactory(sourceFile.AsNode());
            var identifierText = identifier.Text;
            if (identifierText == jsxNamespace)
            {
                return true;
            }
            if (jsxFragmentFactory != "" && identifierText == jsxFragmentFactory)
            {
                return true;
            }
        }

        var symbol = GetSymbolAtLocation(identifier.AsNode());
        if (symbol is null)
        {
            return true;
        }

        return IsSymbolReferencedInFile(sourceFile, identifier, symbol);
    }

    // IsSymbolReferencedInFile checks if a symbol is referenced in the source file (besides its definition).
    // This is used as a quick check for whether a symbol is used at all in a file.
    public bool IsSymbolReferencedInFile(
        SourceFile sourceFile,
        Identifier definition,
        Symbol symbol)
    {
        var identifierText = definition.Text;
        foreach (var token in GetPossibleSymbolReferenceNodes(sourceFile, identifierText, sourceFile.AsNode()))
        {
            if (!IsIdentifier(token))
            {
                continue;
            }
            var id = token.AsIdentifier()!;
            if (ReferenceEquals(id, definition) || id.Text != identifierText)
            {
                continue;
            }
            var refSymbol = GetSymbolAtLocation(token);
            if (ReferenceEquals(refSymbol, symbol))
            {
                return true;
            }
            if (token.Parent is not null && token.Parent.Kind == Kind.ShorthandPropertyAssignment)
            {
                var shorthandSymbol = GetShorthandAssignmentValueSymbol(token.Parent);
                if (ReferenceEquals(shorthandSymbol, symbol))
                {
                    return true;
                }
            }
            if (token.Parent is not null && IsExportSpecifier(token.Parent))
            {
                var localSymbol = GetLocalSymbolForExportSpecifier(token.AsIdentifier()!, refSymbol, token.Parent.AsExportSpecifier()!);
                if (ReferenceEquals(localSymbol, symbol))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // GetReferencesToSymbolInFile returns all identifier nodes in the file that reference the given symbol.
    public Node[] GetReferencesToSymbolInFile(
        SourceFile sourceFile,
        Symbol symbol)
    {
        var identifierText = symbol.Name;
        var result = new List<Node>();
        foreach (var token in GetPossibleSymbolReferenceNodes(sourceFile, identifierText, sourceFile.AsNode()))
        {
            if (!IsIdentifier(token))
            {
                continue;
            }
            var id = token.AsIdentifier()!;
            if (id.Text != identifierText)
            {
                continue;
            }
            var refSymbol = GetSymbolAtLocation(token);
            if (ReferenceEquals(refSymbol, symbol))
            {
                result.Add(token);
                continue;
            }
            if (token.Parent is not null && token.Parent.Kind == Kind.ShorthandPropertyAssignment)
            {
                var shorthandSymbol = GetShorthandAssignmentValueSymbol(token.Parent);
                if (ReferenceEquals(shorthandSymbol, symbol))
                {
                    result.Add(token);
                    continue;
                }
            }
            if (token.Parent is not null && IsExportSpecifier(token.Parent))
            {
                var localSymbol = GetLocalSymbolForExportSpecifier(token.AsIdentifier()!, refSymbol, token.Parent.AsExportSpecifier()!);
                if (ReferenceEquals(localSymbol, symbol))
                {
                    result.Add(token);
                    continue;
                }
            }
        }
        return result.ToArray();
    }

    internal Symbol? GetLocalSymbolForExportSpecifier(Identifier referenceLocation, Symbol? referenceSymbol, ExportSpecifier exportSpecifier)
    {
        if (IsExportSpecifierAlias(referenceLocation, exportSpecifier))
        {
            var symbol = GetExportSpecifierLocalTargetSymbol(exportSpecifier.AsNode());
            if (symbol is not null)
            {
                return symbol;
            }
        }
        return referenceSymbol;
    }

    private static bool IsExportSpecifierAlias(Identifier referenceLocation, ExportSpecifier exportSpecifier)
    {
        Debug.Assert(
            ReferenceEquals(exportSpecifier.PropertyName, referenceLocation.AsNode()) ||
            ReferenceEquals(exportSpecifier.Name(), referenceLocation.AsNode()),
            "referenceLocation is not export specifier name or property name");
        var propertyName = exportSpecifier.PropertyName;
        if (propertyName is not null)
        {
            // Given `export { foo as bar } [from "someModule"]`: It's an alias at `foo`, but at `bar` it's a new symbol.
            return ReferenceEquals(propertyName, referenceLocation.AsNode());
        }
        else
        {
            // `export { foo } from "foo"` is a re-export.
            // `export { foo };` is not a re-export, it creates an alias for the local variable `foo`.
            return exportSpecifier.Parent!.Parent!.ModuleSpecifier() is null;
        }
    }

    private static Node[] GetPossibleSymbolReferenceNodes(SourceFile sourceFile, string symbolName, Node? container) =>
        Core.MapNonNil<int, Node>(
            GetPossibleSymbolReferencePositions(sourceFile, symbolName, container),
            pos =>
            {
                var referenceLocation = AstNav.GetTouchingPropertyName(sourceFile, pos);
                return !ReferenceEquals(referenceLocation, sourceFile.AsNode()) ? referenceLocation : null!;
            });

    private static int[] GetPossibleSymbolReferencePositions(SourceFile sourceFile, string symbolName, Node? container)
    {
        var positions = new List<int>();

        // TODO: Cache symbol existence for files to save text search
        // Also, need to make this work for unicode escapes.

        // Be resilient in the face of a symbol with no name or zero length name
        if (symbolName == "")
        {
            return [];
        }

        var text = sourceFile.Text();
        var sourceLength = text.Length;
        var symbolNameLength = symbolName.Length;

        container ??= sourceFile.AsNode();

        var position = text.AsSpan(container.Pos()).IndexOf(symbolName.AsSpan());
        var endPos = container.End();
        while (position >= 0 && position < endPos)
        {
            // We found a match.  Make sure it's not part of a larger word (i.e. the char
            // before and after it have to be a non-identifier char).
            var endPosition = position + symbolNameLength;

            if ((position == 0 || !Scanner.IsIdentifierPart(text[position - 1])) &&
                (endPosition == sourceLength || !Scanner.IsIdentifierPart(text[endPosition])))
            {
                // Found a real match.  Keep searching.
                positions.Add(position);
            }
            var startIndex = position + symbolNameLength + 1;
            if (startIndex > text.Length)
            {
                break;
            }
            var foundIndex = text.AsSpan(startIndex).IndexOf(symbolName.AsSpan());
            if (foundIndex != -1)
            {
                position = startIndex + foundIndex;
            }
            else
            {
                break;
            }
        }

        return positions.ToArray();
    }

    public Type? GetTypeArgumentConstraint(Node node)
    {
        if (!IsTypeNode(node))
        {
            return null;
        }
        return GetTypeArgumentConstraintWorker(node);
    }

    // getUninstantiatedSignatures gets generic signatures from the function's/constructor's type.
    internal Signature[] GetUninstantiatedSignatures(Node node)
    {
        switch (node.Kind)
        {
            case Kind.CallExpression:
            case Kind.Decorator:
                return GetSignaturesOfTypeWorker(GetTypeOfExpression(node.Expression()!), SignatureKind.Call) ?? [];
            case Kind.NewExpression:
                return GetSignaturesOfTypeWorker(GetTypeOfExpression(node.Expression()!), SignatureKind.Construct) ?? [];
            case Kind.JsxSelfClosingElement:
            case Kind.JsxOpeningElement:
                if (IsJsxIntrinsicTagName(node.TagName()!))
                {
                    return [];
                }
                return GetSignaturesOfTypeWorker(GetTypeOfExpression(node.TagName()!), SignatureKind.Call) ?? [];
            case Kind.TaggedTemplateExpression:
                return GetSignaturesOfTypeWorker(GetTypeOfExpression(node.AsTaggedTemplateExpression()!.Tag!), SignatureKind.Call) ?? [];
            case Kind.BinaryExpression:
            case Kind.JsxOpeningFragment:
                return [];
        }
        return [];
    }

    internal Type GetTypeParameterConstraintForPositionAcrossSignatures(Signature[] signatures, int position)
    {
        var relevantConstraints = new List<Type>();
        foreach (var signature in signatures)
        {
            if (position >= signature._typeParameters!.Length)
            {
                continue;
            }
            var relevantTypeParameter = signature._typeParameters[position];
            var relevantConstraint = GetConstraintOfTypeParameterWorker(relevantTypeParameter);
            if (relevantConstraint is not null)
            {
                relevantConstraints.Add(relevantConstraint);
            }
        }
        return GetUnionTypeWorker(relevantConstraints.ToArray());
    }

    // Go 的 `getTypeArgumentConstraint`（未导出）→ `GetTypeArgumentConstraintWorker`（导出的包装器见上）。
    internal Type? GetTypeArgumentConstraintWorker(Node node)
    {
        var typeArgumentPosition = -1;
        if (HasTypeArguments(node.Parent))
        {
            var typeArgs = node.Parent!.TypeArguments();
            if (typeArgs is not null)
            {
                for (var i = 0; i < typeArgs.Length; i++)
                {
                    if (ReferenceEquals(typeArgs[i], node))
                    {
                        typeArgumentPosition = i;
                        break;
                    }
                }
            }
        }

        if (typeArgumentPosition >= 0)
        {
            // The node could be a type argument of a call, a `new` expression, a decorator, an
            // instantiation expression, or a generic type instantiation.

            if (IsCallLikeExpression(node.Parent))
            {
                return GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetUninstantiatedSignatures(node.Parent!),
                    typeArgumentPosition);
            }

            if (IsDecorator(node.Parent!.Parent!))
            {
                return GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetUninstantiatedSignatures(node.Parent.Parent!),
                    typeArgumentPosition);
            }

            if (IsExpressionWithTypeArguments(node.Parent) && IsExpressionStatement(node.Parent!.Parent!))
            {
                var uninstantiatedType = CheckExpression(node.Parent.Expression()!);

                var callConstraint = GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetSignaturesOfTypeWorker(uninstantiatedType, SignatureKind.Call) ?? [],
                    typeArgumentPosition);
                var constructConstraint = GetTypeParameterConstraintForPositionAcrossSignatures(
                    GetSignaturesOfTypeWorker(uninstantiatedType, SignatureKind.Construct) ?? [],
                    typeArgumentPosition);

                // An instantiation expression instantiates both call and construct signatures, so
                // if both exist type arguments must be assignable to both constraints.
                if ((constructConstraint._flags & TypeFlags.Never) != 0)
                {
                    return callConstraint;
                }
                if ((callConstraint._flags & TypeFlags.Never) != 0)
                {
                    return constructConstraint;
                }
                return GetIntersectionType([callConstraint, constructConstraint]);
            }

            if (IsTypeReferenceType(node.Parent))
            {
                var typeParameters = GetTypeParametersForTypeReferenceOrImport(node.Parent!) ?? [];
                if (typeParameters.Length == 0)
                {
                    return null;
                }
                if (typeArgumentPosition >= typeParameters.Length)
                {
                    return null;
                }
                var relevantTypeParameter = typeParameters[typeArgumentPosition];
                var constraint = GetConstraintOfTypeParameterWorker(relevantTypeParameter);
                if (constraint is not null)
                {
                    return InstantiateType(
                        constraint,
                        NewTypeMapper(typeParameters, GetEffectiveTypeArguments(node.Parent!, typeParameters)));
                }
            }
        }
        return null;
    }

    public bool IsTypeInvalidDueToUnionDiscriminant(Type contextualType, Node obj)
    {
        var properties = obj.Properties();
        return Core.Some(properties, property =>
        {
            Type? nameType = null;
            var propertyName = property.Name();
            if (propertyName is not null)
            {
                if (IsJsxNamespacedName(propertyName))
                {
                    nameType = GetStringLiteralType(propertyName.Text());
                }
                else
                {
                    nameType = GetLiteralTypeFromPropertyName(propertyName);
                }
            }
            var name = "";
            if (nameType is not null && IsTypeUsableAsPropertyNameWorker(nameType))
            {
                name = GetPropertyNameFromTypeWorker(nameType);
            }
            Type? expected = null;
            if (name != "")
            {
                expected = GetTypeOfPropertyOfTypeWorker(contextualType, name);
            }
            return expected is not null && IsLiteralType(expected) && !IsTypeAssignableToWorker(GetTypeOfNode(property), expected);
        });
    }

    // Unlike `getExportsOfModule`, this includes properties of an `export =` value.
    public Symbol[] GetExportsAndPropertiesOfModule(Symbol moduleSymbol)
    {
        var exports = GetExportsOfModuleAsArray(moduleSymbol);
        var exportEquals = ResolveExternalModuleSymbolWorker(moduleSymbol, false /*dontResolveAlias*/);
        if (!ReferenceEquals(exportEquals, moduleSymbol))
        {
            var t = GetTypeOfSymbolWorker(exportEquals!);
            if (ShouldTreatPropertiesOfExternalModuleAsExports(t))
            {
                var props = GetPropertiesOfTypeWorker(t) ?? [];
                var combined = new Symbol[exports.Length + props.Length];
                exports.CopyTo(combined, 0);
                props.CopyTo(combined, exports.Length);
                exports = combined;
            }
        }
        return exports;
    }

    internal Symbol[] GetExportsOfModuleAsArray(Symbol moduleSymbol) => SymbolsToArray(GetExportsOfModuleWorker(moduleSymbol));

    // Returns all the properties of the Jsx.IntrinsicElements interface.
    public Symbol[] GetJsxIntrinsicTagNamesAt(Node location)
    {
        var intrinsics = GetJsxType(JsxNames.IntrinsicElements, location);
        if (intrinsics is null)
        {
            return [];
        }
        return GetPropertiesOfTypeWorker(intrinsics) ?? [];
    }

    public Type? GetContextualTypeForJsxAttribute(Node attribute /*JsxAttributeLike*/) =>
        GetContextualTypeForJsxAttributeWorker(attribute, ContextFlags.None);

    public object? GetConstantValue(Node node)
    {
        if (node.Kind == Kind.EnumMember)
        {
            return GetEnumMemberValue(node).Value;
        }

        if (_symbolNodeLinks.Get(node)._resolvedSymbol is null)
        {
            CheckExpressionCached(node); // ensure cached resolved symbol is set
        }
        var symbol = _symbolNodeLinks.Get(node)._resolvedSymbol;
        if (symbol is null && IsEntityNameExpression(node))
        {
            symbol = ResolveEntityName(
                node,
                SymbolFlags.Value,
                true,  /*ignoreErrors*/
                false, /*dontResolveAlias*/
                null); /*location*/
        }
        if (symbol is not null && (symbol.Flags & SymbolFlags.EnumMember) != 0)
        {
            // inline property\index accesses only for const enums
            var member = symbol.ValueDeclaration;
            if (IsEnumConst(member!.Parent!))
            {
                return GetEnumMemberValue(member).Value;
            }
        }

        return null;
    }

    // Go 的 `getResolvedSignatureWorker`（services.go:899）→ `GetResolvedSignatureWorkerImpl`
    // （`getResolvedSignature` 的 C# 名是 `GetResolvedSignatureWorker`，见 artifacts/checker-method-collisions.md 的
    // 「Worker 二次冲突」规则：Go 自带的 `getXWorker` → `GetXWorkerImpl`）。
    internal (Signature? Signature, Signature[] Candidates) GetResolvedSignatureWorkerImpl(Node node, CheckMode checkMode, int argumentCount)
    {
        var parsedNode = TypeScript.Printer.Printer.NewEmitContext().ParseNode(node);
        _apparentArgumentCount = argumentCount;
        Signature? res = null;
        Signature[]? candidatesOutArray = [];
        if (parsedNode is not null)
        {
            res = GetResolvedSignatureWorker(parsedNode, ref candidatesOutArray, checkMode);
        }
        _apparentArgumentCount = null;
        return (res, candidatesOutArray ?? []);
    }

    public Signature[] GetCandidateSignaturesForStringLiteralCompletions(Node call /*CallLikeExpression*/, Node editingArgument)
    {
        // first, get candidates when inference is blocked from the source node.
        var candidates = RunWithInferenceBlockedFromSourceNode(editingArgument, () =>
        {
            var (_, blockedInferenceCandidates) = GetResolvedSignatureWorkerImpl(call, CheckMode.Normal, 0);
            return blockedInferenceCandidates;
        });
        var candidatesSet = TypeScript.Collections.Collections.NewSetFromItems(candidates);

        // next, get candidates where the source node is considered for inference.
        var otherCandidates = RunWithoutResolvedSignatureCaching(editingArgument, () =>
        {
            var (_, inferenceCandidates) = GetResolvedSignatureWorkerImpl(call, CheckMode.Normal, 0);
            return inferenceCandidates;
        });

        var result = new List<Signature>(candidates);
        foreach (var candidate in otherCandidates)
        {
            if (candidatesSet.Has(candidate))
            {
                continue;
            }
            result.Add(candidate);
        }

        return result.ToArray();
    }

    // GetTypeAtPosition returns the type of a parameter at a given index in a signature.
    public Type? GetTypeAtPosition(Signature s, int pos) => GetTypeAtPositionWorker(s, pos);

    public Type GetTypeParameterAtPosition(Signature s, int pos)
    {
        var t = GetTypeAtPositionWorker(s, pos);
        if (t.IsIndex() && IsThisTypeParameter(t.AsIndexType()!._target!))
        {
            var constraint = GetBaseConstraintOfTypeWorker(t.AsIndexType()!._target!);
            if (constraint is not null)
            {
                return GetIndexType(constraint);
            }
        }
        return t;
    }

    // GetContextualTypeForArrayLiteralAtPosition returns the contextual type for an element at the given position
    // in an array with the given contextual type.
    public Type? GetContextualTypeForArrayLiteralAtPosition(Type? contextualArrayType, Node arrayLiteral, int position)
    {
        if (contextualArrayType is null)
        {
            return null;
        }
        var firstSpreadIndex = -1;
        var lastSpreadIndex = -1;
        var elementIndex = 0;
        var elements = arrayLiteral.Elements();
        if (elements is not null)
        {
            for (var i = 0; i < elements.Length; i++)
            {
                var elem = elements[i];
                if (elem.Pos() < position)
                {
                    elementIndex++;
                }
                if (IsSpreadElement(elem))
                {
                    if (firstSpreadIndex == -1)
                    {
                        firstSpreadIndex = i;
                    }
                    lastSpreadIndex = i;
                }
            }
        }
        // The array may be incomplete, so we don't know its final length.
        return GetContextualTypeForElementExpression(
            contextualArrayType,
            elementIndex,
            -1, /*length*/
            firstSpreadIndex,
            lastSpreadIndex);
    }

    private static readonly HashSet<string> _knownGenericTypeNames = new()
    {
        "Array",
        "ArrayLike",
        "ReadonlyArray",
        "Promise",
        "PromiseLike",
        "Iterable",
        "IterableIterator",
        "AsyncIterable",
        "Set",
        "WeakSet",
        "ReadonlySet",
        "Map",
        "WeakMap",
        "ReadonlyMap",
        "Partial",
        "Required",
        "Readonly",
        "Pick",
        "Omit",
        "NonNullable",
    };

    private static bool IsKnownGenericTypeName(string name) => _knownGenericTypeNames.Contains(name);

    public Type? GetFirstTypeArgumentFromKnownType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Reference) != 0 && t._symbol is not null && IsKnownGenericTypeName(t._symbol.Name))
        {
            var symbol = GetGlobalSymbolWorker(t._symbol.Name, SymbolFlags.Type, null);
            if (symbol is not null && ReferenceEquals(symbol, t.Target()!._symbol))
            {
                return Core.FirstOrNil(GetTypeArgumentsWorker(t));
            }
        }
        if (t._alias is not null && IsKnownGenericTypeName(t._alias._symbol!.Name))
        {
            var symbol = GetGlobalSymbolWorker(t._alias._symbol!.Name, SymbolFlags.Type, null);
            if (symbol is not null && ReferenceEquals(symbol, t._alias._symbol))
            {
                return Core.FirstOrNil(t._alias._typeArguments);
            }
        }
        return null;
    }

    // Gets all symbols for one property. Does not get symbols for every property.
    public Symbol[] GetPropertySymbolsFromContextualType(Node node, Type contextualType, bool unionSymbolOk)
    {
        var name = GetTextOfPropertyName(node.Name());
        if (name == "")
        {
            return [];
        }
        if ((contextualType._flags & TypeFlags.Union) == 0)
        {
            var symbol = GetPropertyOfTypeWorker(contextualType, name);
            if (symbol is not null)
            {
                return [symbol];
            }
            return [];
        }
        var filteredTypes = contextualType.Types();
        if (IsObjectLiteralExpression(node.Parent!) || IsJsxAttributes(node.Parent!))
        {
            filteredTypes = Core.Filter(filteredTypes, t => !IsTypeInvalidDueToUnionDiscriminant(t, node.Parent!));
        }
        var discriminatedPropertySymbols = Core.MapNonNil(filteredTypes, t => GetPropertyOfTypeWorker(t, name)!);
        if (unionSymbolOk && (discriminatedPropertySymbols.Length == 0 || discriminatedPropertySymbols.Length == contextualType.Types().Length))
        {
            var symbol = GetPropertyOfTypeWorker(contextualType, name);
            if (symbol is not null)
            {
                return [symbol];
            }
        }
        if (filteredTypes.Length == 0 && discriminatedPropertySymbols.Length == 0)
        {
            // Bad discriminant -- do again without discriminating
            return Core.MapNonNil(contextualType.Types(), t => GetPropertyOfTypeWorker(t, name)!);
        }
        // by eliminating duplicates we might even end up with a single symbol
        // that helps with displaying better quick infos on properties of union types
        return Core.Deduplicate(discriminatedPropertySymbols);
    }

    // Gets the property symbol corresponding to the property in destructuring assignment
    // 'property1' from
    //
    //     for ( { property1: a } of elems) {
    //     }
    //
    // 'property1' at location 'a' from:
    //     [a] = [ property1, property2 ]
    public Symbol? GetPropertySymbolOfDestructuringAssignment(Node location)
    {
        if (IsArrayLiteralOrObjectLiteralDestructuringPattern(location.Parent!.Parent!))
        {
            // Get the type of the object or array literal and then look for property of given name in the type
            var typeOfObjectLiteral = GetTypeOfAssignmentPattern(location.Parent!.Parent!);
            if (typeOfObjectLiteral is not null)
            {
                return GetPropertyOfTypeWorker(typeOfObjectLiteral, location.Text());
            }
        }
        return null;
    }

    // Gets the type of object literal or array literal of destructuring assignment.
    // { a } from
    //
    //     for ( { a } of elems) {
    //     }
    //
    // [ a ] from
    //     [a] = [ some array ...]
    internal Type? GetTypeOfAssignmentPattern(Node expr)
    {
        // If this is from "for of"
        //     for ( { a } of elems) {
        //     }
        if (IsForOfStatement(expr.Parent!))
        {
            var iteratedType = CheckRightHandSideOfForOf(expr.Parent!);
            return CheckDestructuringAssignment(expr, Core.OrElse(iteratedType, _errorType), CheckMode.Normal, false);
        }
        // If this is from "for" initializer
        //     for ({a } = elems[0];.....) { }
        if (IsBinaryExpression(expr.Parent!))
        {
            var iteratedType = GetTypeOfExpression(expr.Parent!.AsBinaryExpression()!.Right!);
            return CheckDestructuringAssignment(expr, Core.OrElse(iteratedType, _errorType), CheckMode.Normal, false);
        }
        // If this is from nested object binding pattern
        //     for ({ skills: { primary, secondary } } = multiRobot, i = 0; i < 1; i++) {
        if (IsPropertyAssignment(expr.Parent!))
        {
            var node = expr.Parent!.Parent!;
            var typeOfParentObjectLiteral = Core.OrElse(GetTypeOfAssignmentPattern(node), _errorType);
            var propertyIndex = Array.IndexOf(node.Properties() ?? [], expr.Parent);
            return CheckObjectLiteralDestructuringPropertyAssignment(node, typeOfParentObjectLiteral!, propertyIndex, null, false);
        }
        // Array literal assignment - array destructuring pattern
        var arrayExpr = expr.Parent!;
        //    [{ property1: p1, property2 }] = elems;
        var typeOfArrayLiteral = Core.OrElse(GetTypeOfAssignmentPattern(arrayExpr), _errorType);
        var elementType = Core.OrElse(
            CheckIteratedTypeOrElementType(IterationUse.Destructuring, typeOfArrayLiteral!, _undefinedType, arrayExpr),
            _errorType);
        return CheckArrayLiteralDestructuringElementAssignment(
            arrayExpr,
            typeOfArrayLiteral!,
            Array.IndexOf(arrayExpr.Elements() ?? [], expr),
            elementType,
            CheckMode.Normal);
    }

    public Signature? GetSignatureFromDeclaration(Node node) => GetSignatureFromDeclarationWorker(node);

    // IsLibSymbolForHoverVerbosity returns true if a symbol is declared in a lib file.
    public bool IsLibSymbolForHoverVerbosity(Symbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }
        if (symbol.Declarations is not null)
        {
            foreach (var decl in symbol.Declarations)
            {
                var sf = GetSourceFileOfNode(decl);
                if (sf is not null && _program.IsSourceFileDefaultLibrary(sf.Path()))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // IsLibTypeForHoverVerbosity returns true if a type is declared in a lib file.
    // Don't expand types like Array or Promise, instead treating them as opaque.
    public bool IsLibTypeForHoverVerbosity(Type t)
    {
        Symbol? symbol;
        if ((t._objectFlags & ObjectFlags.Reference) != 0)
        {
            symbol = t.Target()!._symbol;
        }
        else
        {
            symbol = t._symbol;
        }
        if (IsLibSymbolForHoverVerbosity(symbol))
        {
            return true;
        }
        return IsTupleTypeWorker(t);
    }
}
