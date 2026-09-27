// ported from: internal/checker/emitresolver.go:950-1323
//
// `EmitResolver` 的后半部分（与 `EmitResolver.cs` 同为 `public sealed partial class EmitResolver`）：
// declaration emit 的节点构造投影（`CreateX` 族）+ `GetTypeReferenceSerializationKind` 等。
// 分片依据见 docs/architecture.md §2（Go 1323 行 > 1200，按逻辑段拆）。
//
// Go→C# 语义改写（逐条）：
//  1) `NewNodeBuilder(r.checker, emitContext)`（nodebuilder.go:279 的包级导出函数）→ 经
//     `using static TypeScript.Checker.Checker;` 按裸名调用；返回值用 `var`，避免依赖 NodeBuilder 分片的类型名。
//  2) Go 的 `switch value := t.AsLiteralType().value.(type)` → C# 的 `switch (value)` + 类型模式；
//     `value.String()[1:]` → `n.String()[1..]`；末尾 `panic(...)` → `throw new DebugFailureException(...)`。
//  3) `core.IfElse(isStatic, []*ast.Node{…}, nil)` → 显式 `Node[]? mods = isStatic ? [ … ] : null;`
//     （C# 的集合表达式需要目标类型，泛型推断下写不出 Go 那种形状）；`append` → `List<Node>` / 数组拼接。
//  4) `t.flags`/`t.symbol`/`info.declaration`/`info.components`/`info.isReadonly` 等 Go 未导出字段 → `t._flags`/`t._symbol`/
//     `info._declaration`/`info._components`/`info._isReadonly`。
//  5) `node == nil` 检查（`CreateLiteralConstValue`/`GetPropertiesOfContainerFunction`/`IsThisPropertyAssignmentDeclarationRedundant`）
//     在 Go 里就是运行期分支，保留 `is null` 判断（接口签名是不可空 `Node`）。
//  6) Go 的 `for i, infoList := range [][]*IndexInfo{staticInfos, instanceInfos}` → C# 的 `IndexInfo[][]` + 索引循环。
//  7) `maps.Values(...)` 取 `SymbolTable` 的值 → 手动收集到 `Symbol[]`。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.JsNum;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Checker.Checker;

public sealed partial class EmitResolver
{
    // TODO: the emit resolver being responsible for some amount of node construction is a very leaky abstraction,
    // and requires giving it access to a lot of context it's otherwise not required to have, which also further complicates the API
    // and likely reduces performance. There's probably some refactoring that could be done here to simplify this.

    public Node CreateReturnTypeOfSignatureDeclaration(EmitContext emitContext, Node signatureDeclaration, Node? enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker)
    {
        var original = emitContext.ParseNode(signatureDeclaration);
        if (original is null)
        {
            return emitContext.Factory.NewKeywordTypeNode(Kind.AnyKeyword);
        }

        lock (_checkerMu)
        {
            var requestNodeBuilder = NewNodeBuilder(_checker, emitContext); // TODO: cache per-context
            return requestNodeBuilder.SerializeReturnTypeForSignature(original, enclosingDeclaration, flags, internalFlags, tracker)!;
        }
    }

    public Node[] CreateTypeParametersOfSignatureDeclaration(EmitContext emitContext, Node signatureDeclaration, Node? enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker)
    {
        var original = emitContext.ParseNode(signatureDeclaration);
        if (original is null)
        {
            return [];
        }

        lock (_checkerMu)
        {
            var requestNodeBuilder = NewNodeBuilder(_checker, emitContext); // TODO: cache per-context
            return requestNodeBuilder.SerializeTypeParametersForSignature(original, enclosingDeclaration, flags, internalFlags, tracker)!;
        }
    }

    public Node CreateTypeOfDeclaration(EmitContext emitContext, Node declaration, Node? enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker)
    {
        var original = emitContext.ParseNode(declaration);
        if (original is null)
        {
            return emitContext.Factory.NewKeywordTypeNode(Kind.AnyKeyword);
        }

        lock (_checkerMu)
        {
            var requestNodeBuilder = NewNodeBuilder(_checker, emitContext); // TODO: cache per-context
            // // Get type of the symbol if this is the valid symbol otherwise get type at location
            var symbol = _checker.GetSymbolOfDeclaration(declaration);
            return requestNodeBuilder.SerializeTypeForDeclaration(declaration, symbol, enclosingDeclaration, flags | Flags.MultilineObjectLiterals, internalFlags, tracker)!;
        }
    }

    public Node? CreateLiteralConstValue(EmitContext emitContext, Node node, SymbolTracker tracker)
    {
        node = emitContext.ParseNode(node)!;
        Type t;
        lock (_checkerMu)
        {
            t = _checker.GetTypeOfSymbolWorker(_checker.GetSymbolOfDeclaration(node)!);
        }
        if (t is null)
        {
            return null; // TODO: How!? Maybe this should be a panic. All symbols should have a type.
        }

        Node? enumResult = null;
        if ((t._flags & TypeFlags.EnumLike) != 0)
        {
            lock (_checkerMu)
            {
                var requestNodeBuilder = NewNodeBuilder(_checker, emitContext); // TODO: cache per-context
                enumResult = requestNodeBuilder.SymbolToExpression(t._symbol!, SymbolFlags.Value, node, Flags.None, InternalFlags.None, tracker);
            }
            // What about regularTrueType/regularFalseType - since those aren't fresh, we never make initializers from them
            // TODO: handle those if this function is ever used for more than initializers in declaration emit
        }
        else if (ReferenceEquals(t, _checker._trueType))
        {
            enumResult = emitContext.Factory.NewKeywordExpression(Kind.TrueKeyword);
        }
        else if (ReferenceEquals(t, _checker._falseType))
        {
            enumResult = emitContext.Factory.NewKeywordExpression(Kind.FalseKeyword);
        }
        if (enumResult is not null)
        {
            return enumResult;
        }
        if ((t._flags & TypeFlags.Literal) == 0)
        {
            return null; // non-literal type
        }
        switch (t.AsLiteralType()!._value)
        {
            case string value:
                return emitContext.Factory.NewStringLiteral(value, TokenFlags.None);
            case Number value:
                if (value.IsInf())
                {
                    if (value > 0)
                    {
                        return emitContext.Factory.NewIdentifier("Infinity");
                    }
                    return emitContext.Factory.NewPrefixUnaryExpression(Kind.MinusToken, emitContext.Factory.NewIdentifier("Infinity"));
                }
                if (value.IsNaN())
                {
                    return emitContext.Factory.NewIdentifier("NaN");
                }
                if (value.Abs() != value)
                {
                    // negative
                    return emitContext.Factory.NewPrefixUnaryExpression(
                        Kind.MinusToken,
                        emitContext.Factory.NewNumericLiteral(value.String()[1..], TokenFlags.None));
                }
                return emitContext.Factory.NewNumericLiteral(value.String(), TokenFlags.None);
            case PseudoBigInt value:
                return emitContext.Factory.NewBigIntLiteral(PseudoBigIntToString(value) + "n", TokenFlags.None);
            case bool value:
                var kind = Kind.FalseKeyword;
                if (value)
                {
                    kind = Kind.TrueKeyword;
                }
                return emitContext.Factory.NewKeywordExpression(kind);
        }
        throw new DebugFailureException("unhandled literal const value kind");
    }

    public Node CreateTypeOfExpression(EmitContext emitContext, Node expression, Node? enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker)
    {
        expression = emitContext.ParseNode(expression)!;
        if (expression is null)
        {
            return emitContext.Factory.NewKeywordTypeNode(Kind.AnyKeyword);
        }

        lock (_checkerMu)
        {
            var requestNodeBuilder = NewNodeBuilder(_checker, emitContext); // TODO: cache per-context
            return requestNodeBuilder.SerializeTypeForExpression(expression, enclosingDeclaration, flags | Flags.MultilineObjectLiterals, internalFlags, tracker)!;
        }
    }

    public Node[] CreateLateBoundIndexSignatures(EmitContext emitContext, Node container, Node? enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker)
    {
        container = emitContext.ParseNode(container)!;
        lock (_checkerMu)
        {
            var sym = container.Symbol!;
            var staticInfos = _checker.GetIndexInfosOfTypeWorker(_checker.GetTypeOfSymbolWorker(sym));
            var instanceIndexSymbol = _checker.GetIndexSymbol(sym);
            IndexInfo[] instanceInfos = [];
            if (instanceIndexSymbol is not null)
            {
                var members = _checker.GetMembersOfSymbol(sym);
                var siblingSymbols = new Symbol[members.Count];
                var si = 0;
                foreach (var value in members.Values)
                {
                    siblingSymbols[si++] = value;
                }
                instanceInfos = _checker.GetIndexInfosOfIndexSymbol(instanceIndexSymbol, siblingSymbols);
            }

            var requestNodeBuilder = NewNodeBuilder(_checker, emitContext); // TODO: cache per-context

            var result = new List<Node>();
            var infoLists = new IndexInfo[][] { staticInfos ?? [], instanceInfos };
            for (var i = 0; i < infoLists.Length; i++)
            {
                var infoList = infoLists[i];
                var isStatic = i == 0;
                if (infoList.Length == 0)
                {
                    continue;
                }
                foreach (var info in infoList)
                {
                    if (info._declaration is not null)
                    {
                        continue;
                    }
                    if (ReferenceEquals(info, _checker._anyBaseTypeIndexInfo))
                    {
                        continue; // inherited, but looks like a late-bound signature because it has no declarations
                    }
                    if (info._components is { Length: > 0 })
                    {
                        // !!! TODO: Complete late-bound index info support - getObjectLiteralIndexInfo does not yet add late bound components to index signatures
                        var allComponentComputedNamesSerializable = enclosingDeclaration is not null && Core.Every(info._components, c =>
                            c.Name() is not null &&
                            IsComputedPropertyName(c.Name()!) &&
                            IsEntityNameExpression(c.Name()!.Expression()!) &&
                            IsEntityNameVisibleWorker(c.Name()!.Expression()!, enclosingDeclaration, false).Accessibility == SymbolAccessibility.Accessible);
                        if (allComponentComputedNamesSerializable)
                        {
                            foreach (var c in info._components)
                            {
                                if (_checker.HasLateBindableName(c))
                                {
                                    // skip late bound props that contribute to the index signature - they'll be preserved via other means
                                    continue;
                                }

                                var firstIdentifier = GetFirstIdentifier(c.Name()!.Expression()!)!;
                                var name = _checker._resolveName(firstIdentifier, firstIdentifier.Text(), SymbolFlags.Value | SymbolFlags.ExportValue, null /*nameNotFoundMessage*/, true /*isUse*/, false /*excludeGlobals*/);
                                if (name is not null)
                                {
                                    tracker.TrackSymbol(name, enclosingDeclaration!, SymbolFlags.Value);
                                }

                                Node[]? mods = isStatic ? [emitContext.Factory.NewModifier(Kind.StaticKeyword)] : null;
                                if (info._isReadonly)
                                {
                                    var readonlyModifier = emitContext.Factory.NewModifier(Kind.ReadonlyKeyword);
                                    mods = mods is null ? [readonlyModifier] : [.. mods, readonlyModifier];
                                }

                                var decl = emitContext.Factory.NewPropertyDeclaration(
                                    mods is not null ? emitContext.Factory.NewModifierList(mods) : null,
                                    c.Name(),
                                    c.QuestionToken(),
                                    requestNodeBuilder.TypeToTypeNode(_checker.GetTypeOfSymbolWorker(c.Symbol!), enclosingDeclaration, flags, internalFlags, tracker),
                                    null);
                                result.Add(decl);
                            }
                            continue;
                        }
                    }
                    var node = requestNodeBuilder.IndexInfoToIndexSignatureDeclaration(info, enclosingDeclaration, flags, internalFlags, tracker);
                    if (node is not null && isStatic)
                    {
                        var modNodes = new List<Node> { emitContext.Factory.NewModifier(Kind.StaticKeyword) };
                        var existingModifiers = node.ModifierNodes();
                        if (existingModifiers is not null)
                        {
                            modNodes.AddRange(existingModifiers);
                        }
                        var mods = emitContext.Factory.NewModifierList(modNodes.ToArray());
                        node = emitContext.Factory.UpdateIndexSignatureDeclaration(
                            node.AsIndexSignatureDeclaration()!,
                            mods,
                            node.ParameterList(),
                            node.Type());
                    }
                    if (node is not null)
                    {
                        result.Add(node);
                    }
                }
            }
            return result.ToArray();
        }
    }

    public ModifierFlags GetEffectiveDeclarationFlags(Node node, ModifierFlags flags)
    {
        // node = emitContext.ParseNode(node)
        lock (_checkerMu)
        {
            return _checker.GetEffectiveDeclarationFlags(node, flags);
        }
    }

    public object? GetConstantValue(Node node)
    {
        // node = emitContext.ParseNode(node)
        lock (_checkerMu)
        {
            return _checker.GetConstantValue(node);
        }
    }

    public TypeReferenceSerializationKind GetTypeReferenceSerializationKind(Node? typeName, Node? location)
    {
        // typeName = emitContext.ParseNode(typeName)
        // location = emitContext.ParseNode(location)
        lock (_checkerMu)
        {
            if (typeName is null || location is null)
            {
                return TypeReferenceSerializationKind.Unknown;
            }

            // Resolve the symbol as a value to ensure the type can be reached at runtime during emit.
            var isTypeOnly = false;
            if (IsQualifiedName(typeName))
            {
                var rootValueSymbol = _checker.ResolveEntityName(GetFirstIdentifier(typeName)!, SymbolFlags.Value, true, true, location);

                if (rootValueSymbol is not null && rootValueSymbol.Declarations is { Length: > 0 })
                {
                    isTypeOnly = Core.Every(rootValueSymbol.Declarations, n => IsTypeOnlyImportOrExportDeclaration(n));
                }
            }
            var valueSymbol = _checker.ResolveEntityName(typeName, SymbolFlags.Value, true, true, location);
            var resolvedValueSymbol = valueSymbol;
            if (valueSymbol is not null && (valueSymbol.Flags & SymbolFlags.Alias) != 0)
            {
                resolvedValueSymbol = _checker.ResolveAliasWorker(valueSymbol);
            }

            isTypeOnly = isTypeOnly || (valueSymbol is not null && _checker.GetTypeOnlyAliasDeclarationEx(valueSymbol, SymbolFlags.Value) is not null);

            // Resolve the symbol as a type so that we can provide a more useful hint for the type serializer.
            var typeSymbol = _checker.ResolveEntityName(typeName, SymbolFlags.Type, true, true, location);
            var resolvedTypeSymbol = typeSymbol;
            if (typeSymbol is not null && (typeSymbol.Flags & SymbolFlags.Alias) != 0)
            {
                resolvedTypeSymbol = _checker.ResolveAliasWorker(typeSymbol);
            }
            // In case the value symbol can't be resolved (e.g. because of missing declarations), use type symbol for reachability check.
            isTypeOnly = isTypeOnly || (typeSymbol is not null && _checker.GetTypeOnlyAliasDeclarationEx(typeSymbol, SymbolFlags.Type) is not null);

            if (resolvedValueSymbol is not null && ReferenceEquals(resolvedValueSymbol, resolvedTypeSymbol))
            {
                var globalPromiseSymbol = _checker._getGlobalPromiseConstructorSymbol();
                if (globalPromiseSymbol is not null && ReferenceEquals(resolvedValueSymbol, globalPromiseSymbol))
                {
                    return TypeReferenceSerializationKind.Promise;
                }

                var constructorType = _checker.GetTypeOfSymbolWorker(resolvedValueSymbol);
                if (constructorType is not null && _checker.IsConstructorType(constructorType))
                {
                    if (isTypeOnly)
                    {
                        return TypeReferenceSerializationKind.TypeWithCallSignature;
                    }
                    return TypeReferenceSerializationKind.TypeWithConstructSignatureAndValue;
                }
            }

            // We might not be able to resolve type symbol so use unknown type in that case (eg error case)
            if (resolvedTypeSymbol is null)
            {
                if (isTypeOnly)
                {
                    return TypeReferenceSerializationKind.ObjectType;
                }
                return TypeReferenceSerializationKind.Unknown;
            }

            var type_ = _checker.GetDeclaredTypeOfSymbolWorker(resolvedTypeSymbol);
            if (_checker.IsErrorType(type_))
            {
                if (isTypeOnly)
                {
                    return TypeReferenceSerializationKind.ObjectType;
                }
                return TypeReferenceSerializationKind.Unknown;
            }

            if ((type_._flags & TypeFlags.AnyOrUnknown) != 0)
            {
                return TypeReferenceSerializationKind.ObjectType;
            }
            else if (_checker.IsTypeAssignableToKind(type_, TypeFlags.Void | TypeFlags.Nullable | TypeFlags.Never))
            {
                return TypeReferenceSerializationKind.VoidNullableOrNeverType;
            }
            else if (_checker.IsTypeAssignableToKind(type_, TypeFlags.BooleanLike))
            {
                return TypeReferenceSerializationKind.BooleanType;
            }
            else if (_checker.IsTypeAssignableToKind(type_, TypeFlags.NumberLike))
            {
                return TypeReferenceSerializationKind.NumberLikeType;
            }
            else if (_checker.IsTypeAssignableToKind(type_, TypeFlags.BigIntLike))
            {
                return TypeReferenceSerializationKind.BigIntLikeType;
            }
            else if (_checker.IsTypeAssignableToKind(type_, TypeFlags.StringLike))
            {
                return TypeReferenceSerializationKind.StringLikeType;
            }
            else if (IsTupleTypeWorker(type_))
            {
                return TypeReferenceSerializationKind.ArrayLikeType;
            }
            else if (_checker.IsTypeAssignableToKind(type_, TypeFlags.ESSymbolLike))
            {
                return TypeReferenceSerializationKind.ESSymbolType;
            }
            else if (_checker.IsFunctionType(type_))
            {
                return TypeReferenceSerializationKind.TypeWithCallSignature;
            }
            else if (_checker.IsArrayTypeWorker(type_))
            {
                return TypeReferenceSerializationKind.ArrayLikeType;
            }
            else
            {
                return TypeReferenceSerializationKind.ObjectType;
            }
        }
    }

    public Symbol[] GetPropertiesOfContainerFunction(Node node)
    {
        // This is explicitly _not locked_ because it is only called via error reporters invoked via node builder calls
        // to the symbol tracker already within locked contexts.
        // _checkerMu.Lock()
        // defer _checkerMu.Unlock()
        if (node is null)
        {
            return [];
        }
        var s = _checker.GetSymbolOfDeclaration(node);
        if (s is null)
        {
            return [];
        }
        return _checker.GetPropertiesOfTypeWorker(_checker.GetTypeOfSymbolWorker(s)) ?? [];
    }

    public Node? TryJSTypeNodeToTypeNode(EmitContext emitContext, Node typeNode, Node? enclosingDeclaration, Flags flags, InternalFlags internalFlags, SymbolTracker tracker)
    {
        typeNode = emitContext.ParseNode(typeNode)!;
        lock (_checkerMu)
        {
            var requestNodeBuilder = NewNodeBuilder(_checker, emitContext); // TODO: cache per-context
            return requestNodeBuilder.TryJSTypeNodeToTypeNode(typeNode, enclosingDeclaration, flags, internalFlags, tracker);
        }
    }

    // IsThisPropertyAssignmentDeclarationRedundant reports whether a JS `this.<name> = ...` expando
    // assignment should be omitted from declaration emit because the member it would synthesize is
    // already provided by an `extends` base type. This mirrors the skip condition in the checker's
    // serializePropertySymbol: an inherited member is redundant when it is identical to the assigned
    // one (same readonly-ness, optionality and type). Inherited accessors and methods are always
    // treated as redundant here, since accessors merge oddly with value assignments (and run via the
    // accessor at runtime), and `this`-expando props carry the ReplaceableByMethod contract, so a
    // rebind such as `this.method = this.method.bind(this)` must not override the base method.
    //
    // Only `extends` base types are considered. Members coming from `implements` clauses are not
    // inherited, so the class must redeclare them, and they are always emitted.
    public bool IsThisPropertyAssignmentDeclarationRedundant(Node node)
    {
        if (node is null)
        {
            return false;
        }

        lock (_checkerMu)
        {
            var s = _checker.GetSymbolOfDeclaration(node);
            if (s is null || s.Parent is null)
            {
                return false;
            }
            var parentType = _checker.GetDeclaredTypeOfSymbolWorker(s.Parent);
            if (parentType is null)
            {
                return false;
            }
            foreach (var baseType in _checker.GetBaseTypesWorker(parentType) ?? [])
            {
                var baseProp = _checker.GetPropertyOfTypeWorker(baseType, s.Name);
                if (baseProp is null)
                {
                    continue;
                }
                if ((baseProp.Flags & (SymbolFlags.Accessor | SymbolFlags.Method | SymbolFlags.Function)) != 0)
                {
                    return true;
                }
                if (_checker.IsReadonlySymbolWorker(baseProp) == _checker.IsReadonlySymbolWorker(s) &&
                    (s.Flags & SymbolFlags.Optional) == (baseProp.Flags & SymbolFlags.Optional) &&
                    _checker.IsTypeIdenticalTo(_checker.GetTypeOfSymbolWorker(s), _checker.GetTypeOfSymbolWorker(baseProp)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
