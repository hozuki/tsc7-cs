// ported from: internal/checker/nodebuilderimpl.go:3299-3714（typeToTypeNode 主入口、字面量/访问表达式构造、限定名类型参数）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `func (t *TypeAlias) ToTypeReferenceNode(b *NodeBuilderImpl) *ast.Node`（3635）挂在 `TypeAlias` 的
//    `partial` 上（ChkTypes 已把 `TypeAlias` 声明为 `public sealed partial class`）。
// 2) Go 未导出字段 → `_camelCase`（§5.5）；同名方法冲突用 Worker 名（`getReducedType`→`GetReducedTypeWorker`、
//    `getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`getGlobalTypeAliasSymbol`→`GetGlobalTypeAliasSymbol`、
//    `isReadonlySymbol`→`IsReadonlySymbolWorker`）。
// 3) 函数值字段：`c.getGlobalIterableType` 等（checker.go 的 func 字段）→ `_getGlobalIterableType()` 等。
// 4) Go 的 `panic("Should be unreachable.")` → `Debug.Fail(...)` + `return null;`（Debug.Fail 是普通方法，
//    C# 编译器不会把它当 no-return；生成物同款写法见 docs/checker-plan.md §5.4）。
// 5) `core.IfElse`/`core.MapIndex`/`core.Some`/`core.Filter` → 同名 static；`printer.EFXxx` → `EmitFlags.Xxx`。
// 6) `t.AsLiteralType().value.(T)` → `(T)t.AsLiteralType()!._value!`（Go 的断言 panic ↔ C# 硬转换抛异常）。
namespace TypeScript.Checker;

using System;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.JsNum;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using static TypeScript.Scanner.Scanner;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

public sealed partial class NodeBuilderImpl
{
    internal Node? TypeToTypeNode(Type? t)
    {
        var inTypeAlias = _ctx!._flags & NodeBuilderFlags.InTypeAlias;
        _ctx!._flags &= ~NodeBuilderFlags.InTypeAlias;

        if (t is null)
        {
            if ((_ctx!._flags & NodeBuilderFlags.AllowEmptyUnionOrIntersection) == 0)
            {
                _ctx!._encounteredError = true;
                return null;
                // TODO: GH#18217
            }

            _ctx!._approximateLength += 3;
            return _f.NewKeywordTypeNode(Kind.AnyKeyword);
        }

        t = GetNonDistributedTypeParameter(t);

        // Push type onto typeStack for expansion depth tracking
        var pushedType = false;
        var aliasDepthIncreased = false;
        if (_ctx!._maxExpansionDepth >= 0)
        {
            _ctx!._typeStack.Add(t);
            pushedType = true;
        }

        try
        {
            if ((_ctx!._flags & NodeBuilderFlags.NoTypeReduction) == 0)
            {
                t = _ch.GetReducedTypeWorker(t);
            }

            if ((t._flags & TypeFlags.Any) != 0)
            {
                if (t._alias is not null)
                {
                    return t._alias.ToTypeReferenceNode(this);
                }

                if (t == _ch._unresolvedType)
                {
                    return _e.AddSyntheticLeadingComment(_f.NewKeywordTypeNode(Kind.AnyKeyword), Kind.MultiLineCommentTrivia, "unresolved", false /*hasTrailingNewLine*/);
                }

                _ctx!._approximateLength += 3;
                return _f.NewKeywordTypeNode(IfElse(t == _ch._intrinsicMarkerType, Kind.IntrinsicKeyword, Kind.AnyKeyword));
            }

            if ((t._flags & TypeFlags.Unknown) != 0)
            {
                return _f.NewKeywordTypeNode(Kind.UnknownKeyword);
            }

            if ((t._flags & TypeFlags.String) != 0)
            {
                _ctx!._approximateLength += 6;
                return _f.NewKeywordTypeNode(Kind.StringKeyword);
            }

            if ((t._flags & TypeFlags.Number) != 0)
            {
                _ctx!._approximateLength += 6;
                return _f.NewKeywordTypeNode(Kind.NumberKeyword);
            }

            if ((t._flags & TypeFlags.BigInt) != 0)
            {
                _ctx!._approximateLength += 6;
                return _f.NewKeywordTypeNode(Kind.BigIntKeyword);
            }

            if ((t._flags & TypeFlags.Boolean) != 0 && t._alias is null)
            {
                _ctx!._approximateLength += 7;
                return _f.NewKeywordTypeNode(Kind.BooleanKeyword);
            }

            var expandingEnum = false;
            if ((t._flags & TypeFlags.EnumLike) != 0)
            {
                if ((t._symbol!.Flags & SymbolFlags.EnumMember) != 0)
                {
                    var parentSymbol = _ch.GetParentOfSymbol(t._symbol);
                    var parentName = SymbolToTypeNode(parentSymbol!, SymbolFlags.Type, null);
                    if (_ch.GetDeclaredTypeOfSymbolWorker(parentSymbol!) == t)
                    {
                        return parentName;
                    }

                    var memberName = SymbolName(t._symbol);
                    if (IsIdentifierText(memberName, LanguageVariant.Standard))
                    {
                        return AppendReferenceToType(parentName! /* as TypeReference | ImportTypeNode */, _f.NewTypeReferenceNode(_f.NewIdentifier(memberName), null /*typeArguments*/));
                    }

                    if (IsImportTypeNode(parentName!))
                    {
                        parentName!.AsImportTypeNode()!.IsTypeOf = true;
                        // mutably update, node is freshly manufactured anyhow
                        return _f.NewIndexedAccessTypeNode(parentName, _f.NewLiteralTypeNode(NewStringLiteral(memberName)));
                    }
                    else if (IsTypeReferenceNode(parentName!))
                    {
                        return _f.NewIndexedAccessTypeNode(_f.NewTypeQueryNode(parentName!.AsTypeReferenceNode()!.TypeName, null), _f.NewLiteralTypeNode(NewStringLiteral(memberName)));
                    }
                    else
                    {
                        Debug.Fail("Unhandled type node kind returned from `symbolToTypeNode`.");
                        return null;
                    }
                }

                if ((t._flags & TypeFlags.Union) == 0 || !ShouldExpandType(t, false /*isAlias*/))
                {
                    return SymbolToTypeNode(t._symbol, SymbolFlags.Type, null);
                }

                expandingEnum = true;
            }

            if ((t._flags & TypeFlags.StringLiteral) != 0)
            {
                var text = (string)t.AsLiteralType()!._value!;
                _ctx!._approximateLength += text.Length + 2;
                var lit = NewStringLiteral(text);
                _e.AddEmitFlags(lit, EmitFlags.NoAsciiEscaping);
                return _f.NewLiteralTypeNode(lit);
            }

            if ((t._flags & TypeFlags.NumberLiteral) != 0)
            {
                var value = (Number)t.AsLiteralType()!._value!;
                _ctx!._approximateLength += value.String().Length;
                if (value < 0)
                {
                    return _f.NewLiteralTypeNode(_f.NewPrefixUnaryExpression(Kind.MinusToken, _f.NewNumericLiteral(value.String()[1..], TokenFlags.None)));
                }
                else
                {
                    return _f.NewLiteralTypeNode(_f.NewNumericLiteral(value.String(), TokenFlags.None));
                }
            }

            if ((t._flags & TypeFlags.BigIntLiteral) != 0)
            {
                _ctx!._approximateLength += PseudoBigIntToString(GetBigIntLiteralValue(t)).Length + 1;
                return _f.NewLiteralTypeNode(_f.NewBigIntLiteral(PseudoBigIntToString(GetBigIntLiteralValue(t)) + "n", TokenFlags.None));
            }

            if ((t._flags & TypeFlags.BooleanLiteral) != 0)
            {
                if ((bool)t.AsLiteralType()!._value!)
                {
                    _ctx!._approximateLength += 4;
                    return _f.NewLiteralTypeNode(_f.NewKeywordExpression(Kind.TrueKeyword));
                }
                else
                {
                    _ctx!._approximateLength += 5;
                    return _f.NewLiteralTypeNode(_f.NewKeywordExpression(Kind.FalseKeyword));
                }
            }

            if ((t._flags & TypeFlags.UniqueESSymbol) != 0)
            {
                if ((_ctx!._flags & NodeBuilderFlags.AllowUniqueESSymbolType) == 0)
                {
                    if (_ch.IsValueSymbolAccessible(t._symbol!, _ctx!._enclosingDeclaration))
                    {
                        _ctx!._approximateLength += 6;
                        return SymbolToTypeNode(t._symbol!, SymbolFlags.Value, null);
                    }

                    _ctx!._tracker.ReportInaccessibleUniqueSymbolError();
                }

                _ctx!._approximateLength += 13;
                return _f.NewTypeOperatorNode(Kind.UniqueKeyword, _f.NewKeywordTypeNode(Kind.SymbolKeyword));
            }

            if ((t._flags & TypeFlags.Void) != 0)
            {
                _ctx!._approximateLength += 4;
                return _f.NewKeywordTypeNode(Kind.VoidKeyword);
            }

            if ((t._flags & TypeFlags.Undefined) != 0)
            {
                _ctx!._approximateLength += 9;
                return _f.NewKeywordTypeNode(Kind.UndefinedKeyword);
            }

            if ((t._flags & TypeFlags.Null) != 0)
            {
                _ctx!._approximateLength += 4;
                return _f.NewLiteralTypeNode(_f.NewKeywordExpression(Kind.NullKeyword));
            }

            if ((t._flags & TypeFlags.Never) != 0)
            {
                _ctx!._approximateLength += 5;
                return _f.NewKeywordTypeNode(Kind.NeverKeyword);
            }

            if ((t._flags & TypeFlags.ESSymbol) != 0)
            {
                _ctx!._approximateLength += 6;
                return _f.NewKeywordTypeNode(Kind.SymbolKeyword);
            }

            if ((t._flags & TypeFlags.NonPrimitive) != 0)
            {
                _ctx!._approximateLength += 6;
                return _f.NewKeywordTypeNode(Kind.ObjectKeyword);
            }

            if (IsThisTypeParameter(t))
            {
                if ((_ctx!._flags & NodeBuilderFlags.InObjectTypeLiteral) != 0)
                {
                    if (!_ctx!._encounteredError && (_ctx!._flags & NodeBuilderFlags.AllowThisInObjectLiteral) == 0)
                    {
                        _ctx!._encounteredError = true;
                    }

                    _ctx!._tracker.ReportInaccessibleThisError();
                }

                _ctx!._approximateLength += 4;
                return _f.NewThisTypeNode();
            }

            if (inTypeAlias == 0 && t._alias is not null && ((_ctx!._flags & NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope) != 0 || _ch.IsTypeSymbolAccessible(t._alias.Symbol()!, _ctx!._enclosingDeclaration)))
            {
                // If we should expand this type alias, skip the alias and fall through to expand the underlying type
                if (!ShouldExpandType(t, true /*isAlias*/))
                {
                    var sym = t._alias.Symbol()!;
                    var typeArgumentNodes0 = MapToTypeNodes(t._alias.TypeArguments() ?? [], false /*isBareList*/);
                    if (IsReservedMemberName(sym.Name) && (sym.Flags & SymbolFlags.Class) == 0)
                    {
                        return _f.NewTypeReferenceNode(_f.NewIdentifier(""), typeArgumentNodes0);
                    }

                    if (typeArgumentNodes0 is not null && (typeArgumentNodes0.Nodes?.Length ?? 0) == 1 && sym == _ch._globalArrayType!._symbol)
                    {
                        return _f.NewArrayTypeNode(typeArgumentNodes0.Nodes![0]);
                    }

                    return SymbolToTypeNode(sym, SymbolFlags.Type, typeArgumentNodes0);
                }

                // Expanding: increment depth and process the underlying type
                _ctx!._depth++;
                aliasDepthIncreased = true;
            }

            {
                var objectFlags = t._objectFlags;

                if ((objectFlags & ObjectFlags.Reference) != 0)
                {
                    Debug.Assert((t.Flags() & TypeFlags.Object) != 0);
                    // When expanding, expand type references to their structural form
                    if (ShouldExpandType(t, false /*isAlias*/))
                    {
                        _ctx!._depth++;
                        var result = CreateAnonymousTypeNodeEx(t, true /*forceClassExpansion*/, true /*forceExpansion*/);
                        _ctx!._depth--;
                        return result;
                    }

                    if (t.AsTypeReference()!._node is not null)
                    {
                        return VisitAndTransformType(t, static (b, t2) => b.TypeReferenceToTypeNode(t2));
                    }
                    else
                    {
                        return TypeReferenceToTypeNode(t);
                    }
                }

                if ((t._flags & TypeFlags.TypeParameter) != 0 || (objectFlags & ObjectFlags.ClassOrInterface) != 0)
                {
                    // When expanding class or interface types, show their structural form
                    if ((objectFlags & ObjectFlags.ClassOrInterface) != 0 && ShouldExpandType(t, false /*isAlias*/))
                    {
                        _ctx!._depth++;
                        var result = CreateAnonymousTypeNodeEx(t, true /*forceClassExpansion*/, true /*forceExpansion*/);
                        _ctx!._depth--;
                        return result;
                    }

                    if ((t._flags & TypeFlags.TypeParameter) != 0 && Array.IndexOf(_ctx!._inferTypeParameters, t) >= 0)
                    {
                        _ctx!._approximateLength += SymbolName(t._symbol).Length + 6;
                        Node? constraintNode = null;
                        var constraint = _ch.GetConstraintOfTypeParameterWorker(t);
                        if (constraint is not null)
                        {
                            // If the infer type has a constraint that is not the same as the constraint
                            // we would have normally inferred based on b, we emit the constraint
                            // using `infer T extends ?`. We omit inferred constraints from type references
                            // as they may be elided.
                            var inferredConstraint = _ch.GetInferredTypeParameterConstraint(t, true /*omitTypeReferences*/);
                            if (!(inferredConstraint is not null && _ch.IsTypeIdenticalTo(constraint, inferredConstraint)))
                            {
                                _ctx!._approximateLength += 9;
                                constraintNode = TypeToTypeNode(constraint);
                            }
                        }

                        return _f.NewInferTypeNode(TypeParameterToDeclarationWithConstraint(t, constraintNode));
                    }

                    if ((_ctx!._flags & NodeBuilderFlags.GenerateNamesForShadowedTypeParams) != 0 && (t._flags & TypeFlags.TypeParameter) != 0)
                    {
                        var name = TypeParameterToName(t);
                        _ctx!._approximateLength += name.Text.Length;
                        return _f.NewTypeReferenceNode(NewIdentifier(name.Text, t._symbol), null /*typeArguments*/);
                    }

                    // Ignore constraint/default when creating a usage (as opposed to declaration) of a type parameter.
                    if (t._symbol is not null)
                    {
                        return SymbolToTypeNode(t._symbol, SymbolFlags.Type, null);
                    }

                    string name2;
                    if ((t == _ch._markerSuperTypeForCheck || t == _ch._markerSubTypeForCheck) && _ch._varianceTypeParameter is not null && _ch._varianceTypeParameter._symbol is not null)
                    {
                        name2 = (t == _ch._markerSubTypeForCheck ? "sub-" : "super-") + SymbolName(_ch._varianceTypeParameter._symbol);
                    }
                    else
                    {
                        name2 = "?";
                    }

                    return _f.NewTypeReferenceNode(NewIdentifier(name2, null /*symbol*/), null /*typeArguments*/);
                }

                if ((t._flags & TypeFlags.Union) != 0 && t.AsUnionType()!._origin is not null)
                {
                    t = t.AsUnionType()!._origin!;
                }

                if ((t._flags & (TypeFlags.Union | TypeFlags.Intersection)) != 0)
                {
                    Type[] types;
                    if ((t._flags & TypeFlags.Union) != 0)
                    {
                        types = _ch.FormatUnionTypes(t.AsUnionType()!._types!, expandingEnum);
                    }
                    else
                    {
                        types = t.AsIntersectionType()!._types!;
                    }

                    if (types.Length == 1)
                    {
                        return TypeToTypeNode(types[0]);
                    }

                    var typeNodes = MapToTypeNodes(types, true /*isBareList*/);
                    if (typeNodes is not null && (typeNodes.Nodes?.Length ?? 0) > 0)
                    {
                        if ((t._flags & TypeFlags.Union) != 0)
                        {
                            return _f.NewUnionTypeNode(typeNodes);
                        }
                        else
                        {
                            return _f.NewIntersectionTypeNode(typeNodes);
                        }
                    }
                    else
                    {
                        if (!_ctx!._encounteredError && (_ctx!._flags & NodeBuilderFlags.AllowEmptyUnionOrIntersection) == 0)
                        {
                            _ctx!._encounteredError = true;
                        }

                        return null;
                        // TODO: GH#18217
                    }
                }

                if ((objectFlags & (ObjectFlags.Anonymous | ObjectFlags.Mapped)) != 0)
                {
                    Debug.Assert((t.Flags() & TypeFlags.Object) != 0);
                    // The type is an object literal type.
                    return CreateAnonymousTypeNode(t);
                }

                if ((t._flags & TypeFlags.Index) != 0)
                {
                    var indexedType = t.Target();
                    _ctx!._approximateLength += 6;
                    var indexTypeNode = TypeToTypeNode(indexedType);
                    return _f.NewTypeOperatorNode(Kind.KeyOfKeyword, indexTypeNode);
                }

                if ((t._flags & TypeFlags.TemplateLiteral) != 0)
                {
                    var texts = t.AsTemplateLiteralType()!._texts!;
                    var types = t.AsTemplateLiteralType()!._types!;
                    var templateHead = _f.NewTemplateHead(texts[0], "", TokenFlags.None);
                    _e.AddEmitFlags(templateHead, EmitFlags.NoAsciiEscaping);
                    var templateSpans = _f.NewNodeList(MapIndex(types, (t2, i) =>
                    {
                        Node res; // Go: TemplateMiddleOrTail = Node（TemplateMiddle | TemplateTail）
                        if (i < types.Length - 1)
                        {
                            res = _f.NewTemplateMiddle(texts[i + 1], "", TokenFlags.None);
                        }
                        else
                        {
                            res = _f.NewTemplateTail(texts[i + 1], "", TokenFlags.None);
                        }

                        _e.AddEmitFlags(res, EmitFlags.NoAsciiEscaping);
                        return _f.NewTemplateLiteralTypeSpan(TypeToTypeNode(t2)!, res);
                    }));
                    _ctx!._approximateLength += 2;
                    return _f.NewTemplateLiteralTypeNode(templateHead, templateSpans);
                }

                if ((t._flags & TypeFlags.StringMapping) != 0)
                {
                    var typeNode = TypeToTypeNode(t.Target());
                    return SymbolToTypeNode(t.AsStringMappingType()!._symbol!, SymbolFlags.Type, _f.NewNodeList([typeNode!]));
                }

                if ((t._flags & TypeFlags.IndexedAccess) != 0)
                {
                    var objectTypeNode = TypeToTypeNode(t.AsIndexedAccessType()!._objectType);
                    var indexTypeNode2 = TypeToTypeNode(t.AsIndexedAccessType()!._indexType);
                    _ctx!._approximateLength += 2;
                    return _f.NewIndexedAccessTypeNode(objectTypeNode, indexTypeNode2);
                }

                if ((t._flags & TypeFlags.Conditional) != 0)
                {
                    return VisitAndTransformType(t, static (b, t2) => b.ConditionalTypeToTypeNode(t2));
                }

                if ((t._flags & TypeFlags.Substitution) != 0)
                {
                    var typeNode = TypeToTypeNode(t.AsSubstitutionType()!._baseType);
                    if (!_ch.IsNoInferType(t))
                    {
                        return typeNode;
                    }

                    var noInferSymbol = _ch.GetGlobalTypeAliasSymbol("NoInfer", 1, false);
                    if (noInferSymbol is not null)
                    {
                        return SymbolToTypeNode(noInferSymbol, SymbolFlags.Type, _f.NewNodeList([typeNode!]));
                    }
                    else
                    {
                        return typeNode;
                    }
                }

                Debug.Fail("Should be unreachable.");
                return null;
            }
        }
        finally
        {
            if (aliasDepthIncreased)
            {
                _ctx!._depth--;
            }

            if (pushedType)
            {
                _ctx!._typeStack.RemoveAt(_ctx!._typeStack.Count - 1);
            }
        }
    }

    internal Node NewStringLiteral(string text)
    {
        return NewStringLiteralEx(text, false /*isSingleQuote*/);
    }

    internal Node NewStringLiteralEx(string text, bool isSingleQuote)
    {
        var flags = TokenFlags.None;
        if (isSingleQuote || (_ctx!._flags & NodeBuilderFlags.UseSingleQuotesForStringLiteralType) != 0)
        {
            flags |= TokenFlags.SingleQuote;
        }

        var node = _f.NewStringLiteral(text, flags);
        return node;
    }

    // Direct serialization core functions for types, type aliases, and symbols

    internal Node NewIdentifier(string text, Symbol? symbol)
    {
        var id = _f.NewIdentifier(text);
        if (symbol is not null)
        {
            _idToSymbol[id] = symbol;
        }

        return id;
    }

    internal Node? CreateAccessExpression(Node node)
    {
        if (IsQualifiedName(node))
        {
            return _f.NewPropertyAccessExpression(CreateAccessExpression(node.AsQualifiedName()!.Left!)!, null /*questionDotToken*/, _f.DeepCloneNode(node.AsQualifiedName()!.Right!), NodeFlags.None);
        }
        else if (IsIdentifier(node) || IsPropertyAccessExpression(node) || IsExpressionWithTypeArguments(node))
        {
            return _f.DeepCloneNode(node);
        }
        else
        {
            Debug.Fail("unexpected access node kind: " + node.Kind.String());
            return null;
        }
    }

    internal Node? CreateExpressionWithTypeArguments(Node? expr, NodeList? typeArguments)
    {
        if (typeArguments is null || (typeArguments.Nodes?.Length ?? 0) == 0)
        {
            return expr;
        }

        return _f.NewExpressionWithTypeArguments(expr, typeArguments);
    }

    internal NodeList? LookupInstantiatedTypeArgumentNodes(Symbol[] chain, int index)
    {
        if (ShouldWriteTypeParametersInQualifiedName(chain, index))
        {
            var symbol = chain[index];
            var nextSymbol = chain[index + 1];
            if ((nextSymbol.CheckFlags & CheckFlags.Instantiated) == 0)
            {
                return null;
            }

            var targetSymbol = symbol;
            if ((symbol.Flags & SymbolFlags.Alias) != 0 && !_ch.CanGetTypeParametersOfClassOrInterface(symbol))
            {
                targetSymbol = _ch.ResolveAliasWorker(symbol)!;
            }

            if (!_ch.CanGetTypeParametersOfClassOrInterface(targetSymbol))
            {
                return null;
            }

            var @params = GetTypeParametersOfClassOrInterface(targetSymbol);
            var targetMapper = _ch._valueSymbolLinks.Get(nextSymbol)._mapper;
            if (targetMapper is not null)
            {
                @params = Map(@params, targetMapper.Map);
            }

            return MapToTypeNodes(@params, false /*isBareList*/);
        }

        return null;
    }

    internal NodeList? LookupExpressionChainTypeArgumentNodes(Symbol[] chain, int index)
    {
        if (ShouldWriteTypeParametersInQualifiedName(chain, index))
        {
            var symbol = chain[index];
            var symbolId = GetSymbolId(symbol);
            if (_ctx!._typeParameterSymbolList.Has(symbolId))
            {
                return null;
            }

            _ctx!._typeParameterSymbolList.Add(symbolId);
            var typeArgumentNodes = LookupInstantiatedTypeArgumentNodes(chain, index);
            if (typeArgumentNodes is not null)
            {
                return typeArgumentNodes;
            }

            var typeParameterNodes = TypeParametersToTypeParameterDeclarations(symbol);
            if (typeParameterNodes.Length > 0)
            {
                return _f.NewNodeList(typeParameterNodes);
            }
        }

        return null;
    }

    internal bool ShouldWriteTypeParametersInQualifiedName(Symbol[] chain, int index)
    {
        return (_ctx!._flags & NodeBuilderFlags.WriteTypeParametersInQualifiedName) != 0 && index < chain.Length - 1;
    }
}

public sealed partial class TypeAlias
{
    /// <summary>Go: <c>func (t *TypeAlias) ToTypeReferenceNode(b *NodeBuilderImpl) *ast.Node</c>（nodebuilderimpl.go:3635）。</summary>
    public Node? ToTypeReferenceNode(NodeBuilderImpl b)
    {
        return b._f.NewTypeReferenceNode(b.SymbolToEntityNameNode(Symbol()!), b.MapToTypeNodes(TypeArguments() ?? [], false /*isBareList*/));
    }
}
