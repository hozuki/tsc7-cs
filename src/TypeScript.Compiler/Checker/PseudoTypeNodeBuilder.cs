// ported from: internal/checker/pseudotypenodebuilder.go（整文件，1-765）
//
// 与 Go 的差异（逐条）：
// 1) 包级函数（`isStructuralPseudoType`）→ `partial class Checker` 的 static 成员（§5.5）；
//    Go 的函数内递归闭包 `appendTypeNode` → C# 局部函数。
// 2) Go 未导出字段 → `_camelCase`（§5.5）；同名方法冲突用 Worker 名（`getSignatureFromDeclaration`→
//    `GetSignatureFromDeclarationWorker`、`getPropertiesOfType`→`GetPropertiesOfTypeWorker`、
//    `getPropertyOfType`→`GetPropertyOfTypeWorker`、`getTypeOfSymbol`→`GetTypeOfSymbolWorker`、
//    `getTypeArguments`→`GetTypeArgumentsWorker`、`getReturnTypeOfSignature`→`GetReturnTypeOfSignatureWorker`、
//    `getTypePredicateOfSignature`→`GetTypePredicateOfSignatureWorker`、`getContextualType`→`GetContextualTypeWorker`、
//    `getWidenedType`→`GetWidenedTypeWorker`、`getUnionType`→`GetUnionTypeWorker`、`getTypeFromTypeNode`→
//    `GetTypeFromTypeNodeWorker`）。
// 3) `pseudochecker.*` → `TypeScript.PseudoChecker` 的类型/枚举（`PseudoTypeKind.Xxx`、`PseudoChecker.IsInConstContext`、
//    `PseudoChecker.CouldAlreadyReferToUndefinedType`）；`pseudochecker.PseudoTypeUndefined` → `PseudoChecker.PseudoTypeUndefined`。
// 4) `debug.Assert`/`debug.AssertNever`/`debug.Fail` → `Debug.Assert`/`Debug.AssertNever`/`Debug.Fail`（+ 显式 `return`，
//    C# 编译器不会把 `Debug.Fail` 当作 no-return）。
// 5) `printer.EFSingleLine` → `EmitFlags.SingleLine`；`printer.EFNoAsciiEscaping` → `EmitFlags.NoAsciiEscaping`。
// 6) Go 的 `t.(*Type)` 断言失败 panic ↔ C# 硬转换；`pseudochecker` 的 `As*` 是硬转换（见 PseudoChecker/Type.cs 文件头）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.PseudoChecker;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Checker.Checker;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;

public sealed partial class NodeBuilderImpl
{
    /// <summary>
    /// pseudoTypeToNodeWithCheckerFallback is like pseudoTypeToNode but when the top-level pseudo type
    /// is PseudoTypeInferred, it reports any error nodes and then serializes from the checker's type.
    /// This avoids incorrect type output when PseudoTypeInferred would derive the type from the
    /// original declaration expression in an instantiated context.
    /// </summary>
    internal Node? PseudoTypeToNodeWithCheckerFallback(PseudoType t, Type? checkerType)
    {
        if (t.Kind == PseudoTypeKind.Inferred)
        {
            if (!_ctx!._suppressReportInferenceFallback)
            {
                var errorNodes = t.AsPseudoTypeInferred().ErrorNodes;
                if (errorNodes is not null && errorNodes.Length > 0)
                {
                    foreach (var n in errorNodes)
                    {
                        _ctx!._tracker.ReportInferenceFallback(n);
                    }
                }
                else
                {
                    _ctx!._tracker.ReportInferenceFallback(t.AsPseudoTypeInferred().Expression!);
                }
            }

            var oldSuppress = _ctx!._suppressReportInferenceFallback;
            _ctx!._suppressReportInferenceFallback = true;
            var result = TypeToTypeNode(checkerType);
            _ctx!._suppressReportInferenceFallback = oldSuppress;
            return result;
        }
        else if (t.Kind == PseudoTypeKind.Direct)
        {
            var existing = t.AsPseudoTypeDirect().TypeNode;
            if (!CanReuseExistingJSTypeNode(existing, checkerType))
            {
                if (!_ctx!._suppressReportInferenceFallback)
                {
                    _ctx!._tracker.ReportInferenceFallback(existing!);
                }

                var oldSuppress = _ctx!._suppressReportInferenceFallback;
                _ctx!._suppressReportInferenceFallback = true;
                var result = TypeToTypeNode(checkerType);
                _ctx!._suppressReportInferenceFallback = oldSuppress;
                return result;
            }
        }

        return PseudoTypeToNode(t);
    }

    /// <summary>Maps a pseudochecker's pseudotypes into ast nodes and reports any inference fallback errors the pseudotype structure implies</summary>
    internal Node? PseudoTypeToNode(PseudoType t)
    {
        Debug.Assert(t is not null, "Attempted to serialize nil pseudotype");
        switch (t!.Kind)
        {
            case PseudoTypeKind.Direct:
                return ReuseTypeNode(t.AsPseudoTypeDirect().TypeNode!);
            case PseudoTypeKind.Inferred:
            {
                var inferred = t.AsPseudoTypeInferred();
                var node = inferred.Expression!;
                var errorNodes = inferred.ErrorNodes;
                if (errorNodes is not null && errorNodes.Length > 0)
                {
                    foreach (var n in errorNodes)
                    {
                        _ctx!._tracker.ReportInferenceFallback(n);
                    }
                }
                else if (IsEntityNameExpression(node) && IsDeclaration(node.Parent!))
                {
                    _ctx!._tracker.ReportInferenceFallback(node.Parent!);
                }
                else
                {
                    _ctx!._tracker.ReportInferenceFallback(node);
                }

                if (inferred.IsSignatureReturn)
                {
                    return SerializeReturnTypeForSignature(_ch.GetSignatureFromDeclarationWorker(node), false);
                }

                // use symbol type from parent declaration to automatically handle expression type widening without duplicating logic
                if (IsReturnStatement(node.Parent!))
                {
                    var enclosing = GetContainingFunction(node);
                    if (IsAccessor(enclosing!))
                    {
                        return SerializeTypeForDeclaration(enclosing, null, null, false);
                    }

                    return SerializeReturnTypeForSignature(_ch.GetSignatureFromDeclarationWorker(enclosing!), false);
                }

                if (IsArrowFunction(node.Parent!) && node.Parent!.AsArrowFunction()!.Body == node)
                {
                    return SerializeReturnTypeForSignature(_ch.GetSignatureFromDeclarationWorker(node.Parent!), false);
                }

                if (IsDeclaration(node.Parent!))
                {
                    return SerializeTypeForDeclaration(node.Parent, null, null, false);
                }

                // This might be effectively unreachable. If it's not, it may need more widening rules to mirror checker behavior for whatever expressions are serialized here
                var ty = _ch.GetTypeOfExpression(node);
                return TypeToTypeNode(ty);
            }

            case PseudoTypeKind.NoResult:
            {
                var node = t.AsPseudoTypeNoResult().Declaration;
                _ctx!._tracker.ReportInferenceFallback(node!);
                if (IsFunctionLike(node!) && !IsAccessor(node!))
                {
                    return SerializeReturnTypeForSignature(_ch.GetSignatureFromDeclarationWorker(node!), false);
                }

                return SerializeTypeForDeclaration(node, null, null, false);
            }

            case PseudoTypeKind.MaybeConstLocation:
            {
                var d = t.AsPseudoTypeMaybeConstLocation();
                // see checkExpressionWithContextualType for general literal widening rules which need to be emulated here, plus
                // checkTemplateLiteralExpression for template literal widening rules if the pseudochecker ever supports literalized templates
                var isInConstContext = _ch.IsConstContext(d.Node!);
                if (!isInConstContext && global::TypeScript.PseudoChecker.PseudoChecker.IsInConstContext(d.Node!))
                {
                    // Only consult the contextual type if the pseudochecker's syntactic check also puts us in a const context.
                    // getContextualType returns post-inference results at node-printing time which may not have existed
                    // during initial checking (e.g. when the contextual type depends on inference), causing incorrect
                    // literal type preservation.
                    var contextualType = _ch.GetContextualTypeWorker(d.Node!, ContextFlags.None);
                    var t2 = PseudoTypeToType(d.ConstType!);
                    if (t2 is not null && _ch.IsLiteralOfContextualType(t2, _ch.InstantiateContextualType(contextualType, d.Node!, ContextFlags.None)))
                    {
                        isInConstContext = true;
                    }
                }

                if (isInConstContext)
                {
                    return PseudoTypeToNode(d.ConstType!);
                }
                else
                {
                    return PseudoTypeToNode(d.RegularType!);
                }
            }

            case PseudoTypeKind.Union:
            {
                var res = new List<Node>();
                var hasElidedType = false;
                var hasUndefined = false;
                // Go: range over a nil slice is a no-op.
                var members = t.AsPseudoTypeUnion().Types ?? [];

                void AppendTypeNode(Node node)
                {
                    if (IsUnionTypeNode(node))
                    {
                        foreach (var n in node.AsUnionTypeNode()!.Types!.Nodes ?? [])
                        {
                            AppendTypeNode(n);
                        }

                        return;
                    }

                    if (node.Kind == Kind.UndefinedKeyword)
                    {
                        if (hasUndefined)
                        {
                            return;
                        }

                        hasUndefined = true;
                    }

                    res.Add(node);
                }

                foreach (var m in members)
                {
                    if (!_ch._strictNullChecks)
                    {
                        if (m.Kind == PseudoTypeKind.Undefined || m.Kind == PseudoTypeKind.Null)
                        {
                            hasElidedType = true;
                            continue;
                        }
                    }

                    AppendTypeNode(PseudoTypeToNode(m)!);
                }

                if (res.Count == 1)
                {
                    return res[0];
                }

                if (res.Count == 0)
                {
                    if (hasElidedType)
                    {
                        return _f.NewKeywordTypeNode(Kind.AnyKeyword);
                    }

                    return _f.NewKeywordTypeNode(Kind.NeverKeyword);
                }

                return _f.NewUnionTypeNode(_f.NewNodeList(res.ToArray()));
            }

            case PseudoTypeKind.Undefined:
                if (!_ch._strictNullChecks)
                {
                    return _f.NewKeywordTypeNode(Kind.AnyKeyword);
                }

                return _f.NewKeywordTypeNode(Kind.UndefinedKeyword);
            case PseudoTypeKind.Null:
                if (!_ch._strictNullChecks)
                {
                    return _f.NewKeywordTypeNode(Kind.AnyKeyword);
                }

                return _f.NewLiteralTypeNode(_f.NewKeywordExpression(Kind.NullKeyword));
            case PseudoTypeKind.Any:
                return _f.NewKeywordTypeNode(Kind.AnyKeyword);
            case PseudoTypeKind.String:
                return _f.NewKeywordTypeNode(Kind.StringKeyword);
            case PseudoTypeKind.Number:
                return _f.NewKeywordTypeNode(Kind.NumberKeyword);
            case PseudoTypeKind.BigInt:
                return _f.NewKeywordTypeNode(Kind.BigIntKeyword);
            case PseudoTypeKind.Boolean:
                return _f.NewKeywordTypeNode(Kind.BooleanKeyword);
            case PseudoTypeKind.False:
                return _f.NewLiteralTypeNode(_f.NewKeywordExpression(Kind.FalseKeyword));
            case PseudoTypeKind.True:
                return _f.NewLiteralTypeNode(_f.NewKeywordExpression(Kind.TrueKeyword));
            case PseudoTypeKind.SingleCallSignature:
            {
                var d = t.AsPseudoTypeSingleCallSignature();
                var signature = _ch.GetSignatureFromDeclarationWorker(d.Signature!);
                var expandedParams = _ch.GetExpandedParametersWorker(signature, true /*skipUnionExpanding*/)[0];
                var cleanup = EnterNewScope(d.Signature, expandedParams, signature._typeParameters, signature._parameters, signature._mapper);
                try
                {
                    NodeList? typeParams = null;
                    if (d.TypeParameters is not null && d.TypeParameters.Length > 0)
                    {
                        var res = new List<Node>(d.TypeParameters.Length);
                        foreach (var tp in d.TypeParameters)
                        {
                            res.Add(ReuseNode(tp.AsNode())!);
                        }

                        typeParams = _f.NewNodeList(res.ToArray());
                    }

                    var @params = PseudoParametersToNodeList(d.Parameters!);
                    var returnType = PseudoTypeToNode(d.ReturnType!);
                    return _f.NewFunctionTypeNode(typeParams, @params, returnType);
                }
                finally
                {
                    cleanup();
                }
            }

            case PseudoTypeKind.Tuple:
            {
                var res = new List<Node>();
                // Go: range over a nil slice is a no-op.
                var elements = t.AsPseudoTypeTuple().Elements ?? [];
                foreach (var e in elements)
                {
                    res.Add(PseudoTypeToNode(e)!);
                }

                // pseudo-tuples are implicitly `readonly` since they originate from `as const` contexts
                // but strada *sometimes* fails to add the `readonly` modifier to the generated node.
                var result = _f.NewTupleTypeNode(_f.NewNodeList(res.ToArray()));
                _e.AddEmitFlags(result, EmitFlags.SingleLine);
                return _f.NewTypeOperatorNode(Kind.ReadonlyKeyword, result);
            }

            case PseudoTypeKind.ObjectLiteral:
            {
                // Go: len/range over a nil slice is valid.
                var elements = t.AsPseudoTypeObjectLiteral().Elements ?? [];
                if (elements.Length == 0)
                {
                    var result = _f.NewTypeLiteralNode(_f.NewNodeList([]));
                    _e.AddEmitFlags(result, EmitFlags.SingleLine);
                    return result;
                }

                // NOTE: using the checker's `isConstContext` instead of the pseudochecker's `isInConstContext`
                // results in different results here. The checker one is more "correct" but means we'll mark
                // objects in parameter positions contextually typed by const type parameters as readonly -
                // something a true syntactic ID emitter couldn't possibly know (since the signature could
                // be from across files). This can't *really* happen in any cases ID doesn't already error on, though.
                // Just something to keep in mind if the ID checker keeps growing.
                var isConst = _ch.IsConstContext(elements[0].Name!.Parent!.Parent!);
                var newElements = new List<Node>(elements.Length);

                // Member types are serialized within an object type literal, so set the
                // corresponding flag to mirror createTypeNodeFromObjectType. This ensures
                // inaccessible `this` references inside the members are reported (TS2527).
                var restoreObjectLiteralFlags = SaveRestoreFlags();
                _ctx!._flags |= NodeBuilderFlags.InObjectTypeLiteral;

                try
                {
                    foreach (var e in elements)
                    {
                        ModifierList? modifiers = null;
                        if (isConst || (e.Kind == PseudoObjectElementKind.PropertyAssignment && e.AsPseudoPropertyAssignment().Readonly))
                        {
                            modifiers = _f.NewModifierList([_f.NewModifier(Kind.ReadonlyKeyword)]);
                        }

                        Action? cleanup = null;
                        if (e.Kind != PseudoObjectElementKind.PropertyAssignment)
                        {
                            var signature = _ch.GetSignatureFromDeclarationWorker(e.Signature()!);
                            var expandedParams = _ch.GetExpandedParametersWorker(signature, true /*skipUnionExpanding*/)[0];
                            cleanup = EnterNewScope(e.Signature(), expandedParams, signature._typeParameters, signature._parameters, signature._mapper);
                        }

                        Node? newProp = null;
                        switch (e.Kind)
                        {
                            case PseudoObjectElementKind.Method:
                            {
                                var d = e.AsPseudoObjectMethod();
                                NodeList? typeParams = null;
                                if (d.TypeParameters is not null && d.TypeParameters.Length > 0)
                                {
                                    var res = new List<Node>(d.TypeParameters.Length);
                                    foreach (var tp in d.TypeParameters)
                                    {
                                        res.Add(ReuseNode(tp.AsNode())!);
                                    }

                                    typeParams = _f.NewNodeList(res.ToArray());
                                }

                                if (isConst)
                                {
                                    newProp = _f.NewPropertySignatureDeclaration(
                                        modifiers,
                                        ReuseName(e.Name!, false /*isMethod*/),
                                        null,
                                        _f.NewFunctionTypeNode(
                                            typeParams,
                                            PseudoParametersToNodeList(d.Parameters!),
                                            PseudoTypeToNode(d.ReturnType!)),
                                        null);
                                    break;
                                }

                                newProp = _f.NewMethodSignatureDeclaration(
                                    modifiers,
                                    ReuseName(e.Name!, true /*isMethod*/),
                                    null,
                                    typeParams,
                                    PseudoParametersToNodeList(d.Parameters!),
                                    PseudoTypeToNode(d.ReturnType!));
                                break;
                            }

                            case PseudoObjectElementKind.PropertyAssignment:
                            {
                                var d = e.AsPseudoPropertyAssignment();
                                newProp = _f.NewPropertySignatureDeclaration(
                                    modifiers,
                                    ReuseName(e.Name!, false /*isMethod*/),
                                    null,
                                    PseudoTypeToNode(d.Type!),
                                    null);
                                break;
                            }

                            case PseudoObjectElementKind.SetAccessor:
                            {
                                var d = e.AsPseudoSetAccessor();
                                newProp = _f.NewSetAccessorDeclaration(
                                    null,
                                    ReuseName(e.Name!, false /*isMethod*/),
                                    null,
                                    _f.NewNodeList([PseudoParameterToNode(d.Parameter!)]),
                                    null,
                                    null,
                                    null);
                                break;
                            }

                            case PseudoObjectElementKind.GetAccessor:
                            {
                                var d = e.AsPseudoGetAccessor();
                                newProp = _f.NewGetAccessorDeclaration(
                                    null,
                                    ReuseName(e.Name!, false /*isMethod*/),
                                    null,
                                    null,
                                    PseudoTypeToNode(d.Type!),
                                    null,
                                    null);
                                break;
                            }
                        }

                        if (_ctx!._enclosingFile == GetSourceFileOfNode(e.Name!))
                        {
                            _e.SetCommentRange(newProp!, e.Name!.Parent!.Loc);
                        }

                        newElements.Add(newProp!);
                        cleanup?.Invoke();
                    }
                }
                finally
                {
                    restoreObjectLiteralFlags();
                }

                var result2 = _f.NewTypeLiteralNode(_f.NewNodeList(newElements.ToArray()));
                if ((_ctx!._flags & NodeBuilderFlags.MultilineObjectLiterals) == 0)
                {
                    _e.AddEmitFlags(result2, EmitFlags.SingleLine);
                }

                return result2;
            }

            case PseudoTypeKind.StringLiteral:
            case PseudoTypeKind.NumericLiteral:
            case PseudoTypeKind.BigIntLiteral:
            {
                var source = t.AsPseudoTypeLiteral().Node;
                return _f.NewLiteralTypeNode(ReuseNode(source!)!);
            }

            default:
                Debug.AssertNever(t.Kind, "Unhandled pseudotype kind in pseudotype node construction");
                return null;
        }
    }

    internal NodeList PseudoParametersToNodeList(PseudoParameter[]? params_)
    {
        // Go: len/range over a nil slice is valid; null is treated as an empty parameter list.
        params_ ??= [];
        var res = new List<Node>(params_.Length);
        foreach (var p in params_)
        {
            res.Add(PseudoParameterToNode(p));
        }

        return _f.NewNodeList(res.ToArray());
    }

    internal Node PseudoParameterToNode(PseudoParameter p)
    {
        Node? dotDotDot = null;
        Node? questionMark = null;
        if (p.Rest)
        {
            dotDotDot = _f.NewToken(Kind.DotDotDotToken);
        }

        if (p.Optional)
        {
            questionMark = _f.NewToken(Kind.QuestionToken);
        }

        var parameter = _f.NewParameterDeclaration(
            null,
            dotDotDot,
            // matches strada behavior of always reserializing param names from scratch
            ParameterToParameterDeclarationName(p.Name!.Parent!.Symbol!, p.Name.Parent),
            questionMark,
            PseudoTypeToNode(p.Type!),
            null);
        var original = p.Name.Parent;
        if (IsParameterDeclaration(original!))
        {
            SetCommentRange(parameter, original);
        }

        return parameter;
    }

    /// <summary>
    /// see `typeNodeIsEquivalentToType` in strada, but applied more broadly here, so is setup to handle more equivalences - strada only used it via
    /// the `canReuseTypeNodeAnnotation` host hook and not the `canReuseTypeNode` hook, which meant locations using the later were reliant on
    /// over-invalidation by the ID inference engine to not emit incorrect types.
    /// </summary>
    internal bool PseudoTypeEquivalentToType(PseudoType t, Type? type_, bool isOptionalAnnotated, bool reportErrors)
    {
        // if type_ resolves to an error, we charitably assume equality, since we might be in a single-file checking mode
        if (type_ is not null && _ch.IsErrorType(type_))
        {
            return true;
        }

        // If we can easily operate on just types, we should
        var typeFromPseudo = PseudoTypeToType(t); // note: cannot convert complex types like objects, which must be validated separately
        if (typeFromPseudo == type_)
        {
            return true;
        }

        var undefinedStripped = type_;
        if (isOptionalAnnotated)
        {
            undefinedStripped = _ch.GetTypeWithFacts(type_!, TypeFacts.NEUndefined);
        }

        if (typeFromPseudo is not null && type_ is not null)
        {
            if (isOptionalAnnotated)
            {
                if (undefinedStripped == typeFromPseudo)
                {
                    return true;
                }

                if ((typeFromPseudo._flags & TypeFlags.Union) != 0 && (undefinedStripped!._flags & TypeFlags.Union) != 0)
                {
                    // does union comparison in general, since the unions may not be `==` identical due to aliasing and the like
                    if (_ch.CompareTypesIdentical(typeFromPseudo, undefinedStripped) == Ternary.True)
                    {
                        return true;
                    }
                }
            }

            // handles freshness mismatches (e.g., fresh true vs regular true in as const)
            if (_ch.GetRegularTypeOfLiteralType(typeFromPseudo) == _ch.GetRegularTypeOfLiteralType(type_))
            {
                return true;
            }

            if ((typeFromPseudo._flags & TypeFlags.Union) != 0 && (type_._flags & TypeFlags.Union) != 0)
            {
                // handles union comparison in general, since unions may not be `==` identical due to aliasing
                if (_ch.CompareTypesIdentical(typeFromPseudo, type_) == Ternary.True)
                {
                    return true;
                }
            }
        }

        // otherwise, fallback to actual pseudo/type cross-comparisons
        switch (t.Kind)
        {
            case PseudoTypeKind.Inferred:
            {
                // PseudoTypeInferred with error nodes identifies specific problematic children.
                // Report fine-grained errors on them, then return false so the parent falls back
                // to checker-based serialization (avoiding issues like reusing raw JSON string
                // literal property names from the pseudochecker's AST).
                var errorNodes = t.AsPseudoTypeInferred().ErrorNodes;
                if (errorNodes is not null && errorNodes.Length > 0)
                {
                    if (reportErrors)
                    {
                        foreach (var n in errorNodes)
                        {
                            _ctx!._tracker.ReportInferenceFallback(n);
                        }
                    }

                    return false;
                }

                if (reportErrors)
                {
                    _ctx!._tracker.ReportInferenceFallback(t.AsPseudoTypeInferred().Expression!);
                }

                return false;
            }

            case PseudoTypeKind.ObjectLiteral:
            {
                var pt = t.AsPseudoTypeObjectLiteral();
                if (type_ is null)
                {
                    return false;
                }

                var targetProps = _ch.GetPropertiesOfTypeWorker(undefinedStripped!) ?? [];
                // Count total declarations across all target prop symbols to handle getter/setter pairs,
                // which are two elements in pt.Elements but only one symbol in targetProps.
                var targetDeclCount = 0;
                foreach (var prop in targetProps)
                {
                    targetDeclCount += prop.Declarations?.Length ?? 0;
                }

                // Go: len/range over a nil slice is valid.
                var elements = pt.Elements ?? [];
                if (elements.Length != targetDeclCount)
                {
                    return false;
                }

                foreach (var e in elements)
                {
                    Symbol? targetProp = null;
                    var elemSymbol = e.Name!.Parent!.Symbol;
                    if (elemSymbol is not null)
                    {
                        targetProp = _ch.GetPropertyOfTypeWorker(undefinedStripped!, elemSymbol.Name);
                    }

                    if (targetProp is null)
                    {
                        // Name lookup failed or returned no result; search target properties
                        // for one whose declaration name node matches the one we have
                        foreach (var prop in targetProps)
                        {
                            if (prop.ValueDeclaration is not null && prop.ValueDeclaration.Name() == e.Name)
                            {
                                targetProp = prop;
                                break;
                            }
                        }

                        if (targetProp is null)
                        {
                            if (reportErrors)
                            {
                                _ctx!._tracker.ReportInferenceFallback(e.Name.Parent!);
                            }

                            return false;
                        }
                    }

                    var targetIsOptional = (targetProp.Flags & SymbolFlags.Optional) != 0;
                    if (e.Optional != targetIsOptional)
                    {
                        if (reportErrors)
                        {
                            _ctx!._tracker.ReportInferenceFallback(e.Name.Parent!);
                        }

                        return false;
                    }

                    var propType = _ch.GetTypeOfSymbolWorker(targetProp);
                    propType = _ch.RemoveMissingType(propType, targetIsOptional);
                    switch (e.Kind)
                    {
                        case PseudoObjectElementKind.PropertyAssignment:
                        {
                            var d = e.AsPseudoPropertyAssignment();
                            if (!PseudoTypeEquivalentToType(d.Type!, propType, e.Optional, false))
                            {
                                if (reportErrors)
                                {
                                    if (d.Type!.Kind == PseudoTypeKind.Inferred && (d.Type.AsPseudoTypeInferred().ErrorNodes?.Length ?? 0) > 0)
                                    {
                                        // Re-report the fine-grained error nodes; the recursive call used reportErrors=false
                                        foreach (var n in d.Type.AsPseudoTypeInferred().ErrorNodes!)
                                        {
                                            _ctx!._tracker.ReportInferenceFallback(n);
                                        }
                                    }
                                    else if (!IsStructuralPseudoType(d.Type))
                                    {
                                        _ctx!._tracker.ReportInferenceFallback(e.Name.Parent!);
                                    }
                                }

                                return false;
                            }

                            break;
                        }

                        case PseudoObjectElementKind.Method:
                        {
                            var d = e.AsPseudoObjectMethod();
                            var targetSig = _ch.GetSingleCallSignature(propType);
                            if (targetSig is null)
                            {
                                // Target property type doesn't have a single call signature; can't validate
                                continue;
                            }

                            var paramEq = PseudoParametersEquivalentToParameters(d.Parameters!, targetSig, reportErrors, e.Name.Parent!);
                            if (!paramEq)
                            {
                                return false;
                            }

                            var targetPredicate = _ch.GetTypePredicateOfSignatureWorker(targetSig);
                            if (targetPredicate is not null)
                            {
                                if (!PseudoReturnTypeMatchesPredicate(d.ReturnType!, targetPredicate))
                                {
                                    if (reportErrors)
                                    {
                                        _ctx!._tracker.ReportInferenceFallback(e.Name.Parent!);
                                    }

                                    return false;
                                }
                            }
                            else if (!PseudoTypeEquivalentToType(d.ReturnType!, _ch.GetReturnTypeOfSignatureWorker(targetSig), false, false))
                            {
                                if (reportErrors)
                                {
                                    _ctx!._tracker.ReportInferenceFallback(e.Name.Parent!);
                                }

                                return false;
                            }

                            break;
                        }

                        case PseudoObjectElementKind.GetAccessor:
                        {
                            var d = e.AsPseudoGetAccessor();
                            if (!PseudoTypeEquivalentToType(d.Type!, propType, false, false))
                            {
                                if (reportErrors)
                                {
                                    _ctx!._tracker.ReportInferenceFallback(e.Name.Parent!);
                                }

                                return false;
                            }

                            break;
                        }

                        case PseudoObjectElementKind.SetAccessor:
                        {
                            var d = e.AsPseudoSetAccessor();
                            var writeType = _ch.GetWriteTypeOfSymbol(targetProp);
                            if (!PseudoTypeEquivalentToType(d.Parameter!.Type!, writeType, false, false))
                            {
                                if (reportErrors)
                                {
                                    _ctx!._tracker.ReportInferenceFallback(e.Name.Parent!);
                                }

                                return false;
                            }

                            break;
                        }
                    }
                }

                return true;
            }

            case PseudoTypeKind.Tuple:
            {
                var pt = t.AsPseudoTypeTuple();
                if (undefinedStripped is null || !IsTupleType(undefinedStripped))
                {
                    return false;
                }

                var tupleTarget = undefinedStripped.TargetTupleType();
                // Pseudo-tuples come from `as const` array literals, so they only ever have required elements.
                // If the target tuple has optional, rest, or variadic elements, the structures can't match.
                if ((tupleTarget!._combinedFlags & ElementFlags.NonRequired) != 0)
                {
                    return false;
                }

                var elementTypes = _ch.GetTypeArgumentsWorker(undefinedStripped);
                // Go: len/range over a nil slice is valid.
                var elements = pt.Elements ?? [];
                if (elements.Length != elementTypes.Length)
                {
                    return false;
                }

                for (var i = 0; i < elements.Length; i++)
                {
                    if (!PseudoTypeEquivalentToType(elements[i], elementTypes[i], false, reportErrors))
                    {
                        return false;
                    }
                }

                return true;
            }

            case PseudoTypeKind.SingleCallSignature:
            {
                var targetSig = _ch.GetSingleCallSignature(undefinedStripped!);
                if (targetSig is null)
                {
                    return false;
                }

                var pt = t.AsPseudoTypeSingleCallSignature();
                if ((targetSig._typeParameters?.Length ?? 0) != (pt.TypeParameters?.Length ?? 0))
                {
                    if (reportErrors)
                    {
                        _ctx!._tracker.ReportInferenceFallback(pt.Signature!);
                    }

                    return false;
                }

                var paramEq = PseudoParametersEquivalentToParameters(pt.Parameters!, targetSig, reportErrors, pt.Signature!);
                if (!paramEq)
                {
                    return false;
                }

                var targetPredicate = _ch.GetTypePredicateOfSignatureWorker(targetSig);
                if (targetPredicate is not null)
                {
                    if (!PseudoReturnTypeMatchesPredicate(pt.ReturnType!, targetPredicate))
                    {
                        if (reportErrors)
                        {
                            _ctx!._tracker.ReportInferenceFallback(pt.Signature!);
                        }

                        return false;
                    }
                }
                else if (!PseudoTypeEquivalentToType(pt.ReturnType!, _ch.GetReturnTypeOfSignatureWorker(targetSig), false, reportErrors))
                {
                    // error reported within the return type
                    return false;
                }

                return true;
            }

            case PseudoTypeKind.NoResult:
                if (reportErrors)
                {
                    _ctx!._tracker.ReportInferenceFallback(t.AsPseudoTypeNoResult().Declaration!);
                }

                return false;
            default:
                return false;
        }
    }

    internal bool PseudoParametersEquivalentToParameters(PseudoParameter[]? params_, Signature targetSig, bool reportErrors, Node nonParamErrorLocation)
    {
        // Go: nil slices have len 0, so `len(targetSig.parameters)`/`len(params)` are safe on nil.
        params_ ??= [];
        var targetParams = targetSig._parameters ?? [];
        if (targetSig._thisParameter is not null && params_.Length == 0)
        {
            if (reportErrors)
            {
                _ctx!._tracker.ReportInferenceFallback(nonParamErrorLocation); // missing `this` param
            }

            return false;
        }
        else if (targetSig._thisParameter is not null && IsThisIdentifier(params_[0].Name!))
        {
            var targetParam = targetSig._thisParameter;
            var paramType = _ch.GetTypeOfParameter(targetParam);
            if (!PseudoTypeEquivalentToType(params_[0].Type!, paramType, params_[0].Optional, false))
            {
                if (reportErrors)
                {
                    _ctx!._tracker.ReportInferenceFallback(params_[0].Name!.Parent!);
                }

                return false;
            }

            params_ = params_[1..];
        }
        else if (targetSig._thisParameter is not null)
        {
            if (reportErrors)
            {
                _ctx!._tracker.ReportInferenceFallback(nonParamErrorLocation);
            }

            return false;
        }

        if (targetParams.Length != params_.Length)
        {
            if (reportErrors)
            {
                _ctx!._tracker.ReportInferenceFallback(nonParamErrorLocation);
            }

            return false; // TODO: spread tuple params may mess with this check
        }

        for (var i = 0; i < params_.Length; i++)
        {
            var p = params_[i];
            var targetParam = targetParams[i];
            if (p.Optional != _ch.IsOptionalParameter(targetParam.ValueDeclaration!))
            {
                if (reportErrors)
                {
                    _ctx!._tracker.ReportInferenceFallback(p.Name!.Parent!);
                }

                return false;
            }

            var paramType = _ch.GetTypeOfParameter(targetParam);
            if (!PseudoTypeEquivalentToType(p.Type!, paramType, p.Optional, false))
            {
                if (reportErrors)
                {
                    _ctx!._tracker.ReportInferenceFallback(p.Name!.Parent!);
                }

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// pseudoReturnTypeMatchesPredicate checks if a pseudo return type (which should be a Direct type
    /// wrapping a TypePredicate) matches the given type predicate from the checker.
    /// </summary>
    internal bool PseudoReturnTypeMatchesPredicate(PseudoType rt, TypePredicate predicate)
    {
        if (rt.Kind != PseudoTypeKind.Direct)
        {
            return false;
        }

        var node = rt.AsPseudoTypeDirect().TypeNode!;
        if (!IsTypePredicateNode(node))
        {
            return false;
        }

        var tp = node.AsTypePredicateNode()!;
        // Check asserts modifier matches
        var isAsserts = tp.AssertsModifier is not null;
        var predicateIsAsserts = predicate._kind == TypePredicateKind.AssertsThis || predicate._kind == TypePredicateKind.AssertsIdentifier;
        if (isAsserts != predicateIsAsserts)
        {
            return false;
        }

        // Check this vs identifier matches
        var isThis = IsThisTypeNode(tp.ParameterName!);
        var predicateIsThis = predicate._kind == TypePredicateKind.This || predicate._kind == TypePredicateKind.AssertsThis;
        if (isThis != predicateIsThis)
        {
            return false;
        }

        // For identifier predicates, check parameter name matches
        if (!isThis)
        {
            if (tp.ParameterName!.Text() != predicate._parameterName)
            {
                return false;
            }
        }

        // Check the narrowed type, if any
        if (predicate._t is not null)
        {
            if (tp.Type is null)
            {
                return false;
            }

            var predicateTypeFromNode = _ch.GetTypeFromTypeNodeWorker(tp.Type);
            if (predicateTypeFromNode != predicate._t)
            {
                if (_ch.CompareTypesIdentical(predicateTypeFromNode, predicate._t) != Ternary.True)
                {
                    return false;
                }
            }
        }
        else if (tp.Type is not null)
        {
            return false;
        }

        return true;
    }

    internal Type? PseudoTypeToType(PseudoType t)
    {
        // !!! TODO: only literal types currently mapped because this is only used to determine if literal contextual typing need apply to the pseudotype
        // If this is used more broadly, the implementation needs to be filled out more to handle the structural pseudotypes - signatures, objects, tuples, etc
        Debug.Assert(t is not null, "Attempted to realize nil pseudotype");
        switch (t!.Kind)
        {
            case PseudoTypeKind.Direct:
                return _ch.GetTypeFromTypeNodeWorker(t.AsPseudoTypeDirect().TypeNode!);
            case PseudoTypeKind.Inferred:
            {
                var node = t.AsPseudoTypeInferred().Expression!;
                if (t.AsPseudoTypeInferred().IsSignatureReturn)
                {
                    return _ch.GetReturnTypeOfSignatureWorker(_ch.GetSignatureFromDeclarationWorker(node));
                }

                var ty = _ch.GetWidenedTypeWorker(_ch.GetRegularTypeOfExpression(node));
                return ty;
            }

            case PseudoTypeKind.NoResult:
                return null; // TODO: extract type selection logic from `serializeTypeForDeclaration`, not needed for current usecases but needed if completeness becomes required
            case PseudoTypeKind.MaybeConstLocation:
            {
                var d = t.AsPseudoTypeMaybeConstLocation();
                if (_ch.IsConstContext(d.Node!))
                {
                    return PseudoTypeToType(d.ConstType!);
                }

                return PseudoTypeToType(d.RegularType!);
            }

            case PseudoTypeKind.Union:
            {
                var res = new List<Type>();
                var hasElidedType = false;
                var members = t.AsPseudoTypeUnion().Types ?? [];
                foreach (var m in members)
                {
                    if (!_ch._strictNullChecks)
                    {
                        if (m.Kind == PseudoTypeKind.Undefined || m.Kind == PseudoTypeKind.Null)
                        {
                            hasElidedType = true;
                            continue;
                        }
                    }

                    var t2 = PseudoTypeToType(m);
                    if (t2 is null)
                    {
                        return null; // propagate failure
                    }

                    res.Add(t2);
                }

                if (res.Count == 1)
                {
                    return res[0];
                }

                if (res.Count == 0)
                {
                    if (hasElidedType)
                    {
                        return _ch._anyType;
                    }

                    return _ch._neverType;
                }

                return _ch.GetUnionTypeWorker(res.ToArray());
            }

            case PseudoTypeKind.Undefined:
                return _ch._undefinedWideningType;
            case PseudoTypeKind.Null:
                return _ch._nullWideningType;
            case PseudoTypeKind.Any:
                return _ch._anyType;
            case PseudoTypeKind.String:
                return _ch._stringType;
            case PseudoTypeKind.Number:
                return _ch._numberType;
            case PseudoTypeKind.BigInt:
                return _ch._bigintType;
            case PseudoTypeKind.Boolean:
                return _ch._booleanType;
            case PseudoTypeKind.False:
                return _ch._falseType;
            case PseudoTypeKind.True:
                return _ch._trueType;
            case PseudoTypeKind.StringLiteral:
            case PseudoTypeKind.NumericLiteral:
            case PseudoTypeKind.BigIntLiteral:
            {
                var source = t.AsPseudoTypeLiteral().Node!;
                return _ch.GetRegularTypeOfExpression(source); // big shortcut, uses cached expression types where possible
            }

            case PseudoTypeKind.ObjectLiteral:
            case PseudoTypeKind.SingleCallSignature:
            case PseudoTypeKind.Tuple:
                return null; // no simple mapping to a type, since these are structural types
            default:
                Debug.Fail("Unhandled pseudochecker.PseudoTypeKind in pseudoTypeToType");
                return null;
        }
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func isStructuralPseudoType(t *pseudochecker.PseudoType) bool</c>（pseudotypenodebuilder.go:700）。</summary>
    internal static bool IsStructuralPseudoType(PseudoType t)
    {
        switch (t.Kind)
        {
            case PseudoTypeKind.ObjectLiteral:
            case PseudoTypeKind.Tuple:
            case PseudoTypeKind.SingleCallSignature:
                return true;
            case PseudoTypeKind.MaybeConstLocation:
            {
                var d = t.AsPseudoTypeMaybeConstLocation();
                return IsStructuralPseudoType(d.ConstType!) || IsStructuralPseudoType(d.RegularType!);
            }
        }

        return false;
    }
}
