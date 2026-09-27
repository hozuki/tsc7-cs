// ported from: internal/checker/checker.go:24428-24970（求值 + 类型节点 → 类型 + 条件类型解析）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 把**实例方法当函数值**传给 `core.Map`/`core.Filter`/`core.Some`（`core.Map(node.Elements(), c.getTypeFromTypeNode)`
//    → C# 一律写成 lambda `Map(node.Elements(), t => GetTypeFromTypeNodeWorker(t))`；命中 Worker 改名的成员用改后名字。
// 2) `c.` 前缀的 Go 未导出**字段** → `_camelCase`（`c.typeNodeLinks` → `_typeNodeLinks`、`c.cachedTypes` → `_cachedTypes`）。
//    `c.evaluate` 是 Checker 的**委托字段**（Go checker.go:624）→ C# `_evaluate(expr, location)`（Checker.Struct.cs）。
// 3) `evaluator.NewResult(...)` → `NewResult(...)`；类型 `evaluator.Result` → `Result`（Evaluator.cs）。
// 4) `slices.Index(types, c.emptyTypeLiteralType)` → `Array.IndexOf(types, _emptyTypeLiteralType)`（找不到返回 -1，一致）。
// 5) Go 的 `var extraTypes []*Type` + `append` + `if extraTypes != nil` → `List<Type>? extraTypes`（null = Go 的 nil slice）。
// 6) `panic("Unhandled case in evaluateEntity")` → `throw new DebugFailureException(...)`（Evaluator.cs 的同款先例）。
// 7) Go 的多返回值 `(*ConditionalRoot, *TypeMapper)` → C# 具名元组 `(ConditionalRoot?, TypeMapper?)`；调用点解构后用 `!= null` 判空。
// 8) `root.instantiations = make(map[CacheHashKey]*Type)` → `new Dictionary<CacheHashKey, Type>()`。
// 9) `jsnum.Number(0)` → `(Number)0`；`jsnum.FromString` → `FromString`（`using static TypeScript.JsNum.JsNum;`）。
// 10) Go map 读缺失键返回零值 → `TryGetValue`（`rootSymbol.Exports[name]` 用 `is { } && TryGetValue`）。
// 11) `getArrayOrTupleTargetType` 的局部变量 Go 叫 `readonly`（C# 关键字）→ 改名 `isReadonly`（唯一处非单字面照抄）。
// 12) usings 一律写在 file-scoped `namespace TypeScript.Checker;` **之后**（Checker 包既有布局）：这样
//     `using TypeScript.Diagnostics;` 导入的**类型** `Diagnostics` 成为本命名空间声明的成员，优先于外层命名空间
//     `TypeScript.Diagnostics`，裸写 `Diagnostics.X` 无歧义（本文件 3 处诊断消息都这么写）。
//     注：本人另用最小复刻探针（临时工程，已删；`namespace TypeScript.Diagnostics` 里放 `Message` + 静态类 `Diagnostics`，
//     消费文件在 `namespace TypeScript.Checker`）实测四种布局——usings 在前/在后 × 裸名/`using Diagnostics = …` 别名——
//     **四种均编译通过**；故此处采用新布局只是与全包保持一致，不代表旧布局必然 CS0234。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Evaluator;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Evaluator.EvaluatorFunctions;
using static TypeScript.JsNum.JsNum;

public sealed partial class Checker
{
    internal Result EvaluateEntity(Node expr, Node? location)
    {
        switch (expr.Kind)
        {
            case Kind.Identifier:
            case Kind.PropertyAccessExpression:
                {
                    var symbol = ResolveEntityName(expr, SymbolFlags.Value, true /*ignoreErrors*/, false, null);
                    if (symbol == null)
                    {
                        return NewResult(null, false, false, false);
                    }
                    if (expr.Kind == Kind.Identifier)
                    {
                        if (IsInfinityOrNaNString(expr.Text()) && symbol == GetGlobalSymbolWorker(expr.Text(), SymbolFlags.Value, null /*diagnostic*/))
                        {
                            // Technically we resolved a global lib file here, but the decision to treat this as numeric
                            // is more predicated on the fact that the single-file resolution *didn't* resolve to a
                            // different meaning of `Infinity` or `NaN`. Transpilers handle this no problem.
                            return NewResult(FromString(expr.Text()), false, false, false);
                        }
                    }
                    if ((symbol.Flags & SymbolFlags.EnumMember) != 0)
                    {
                        if (location != null)
                        {
                            return EvaluateEnumMember(expr, symbol, location);
                        }
                        return GetEnumMemberValue(symbol.ValueDeclaration!);
                    }
                    if (IsConstantVariable(symbol))
                    {
                        var declaration = symbol.ValueDeclaration;
                        if (declaration != null && IsVariableDeclaration(declaration) && declaration.Type() == null && declaration.Initializer() != null &&
                            (location == null || (declaration != location && IsBlockScopedNameDeclaredBeforeUse(declaration, location))))
                        {
                            var result = _evaluate(declaration.Initializer()!, declaration);
                            if (location != null && GetSourceFileOfNode(location) != GetSourceFileOfNode(declaration))
                            {
                                return NewResult(result.Value, false, true, true);
                            }
                            return NewResult(result.Value, result.IsSyntacticallyString, result.ResolvedOtherFiles, true /*hasExternalReferences*/);
                        }
                    }
                    return NewResult(null, false, false, false);
                }
            case Kind.ElementAccessExpression:
                {
                    var root = expr.Expression();
                    if (IsEntityNameExpression(root) && IsStringLiteralLike(expr.AsElementAccessExpression()!.ArgumentExpression))
                    {
                        var rootSymbol = ResolveEntityName(root!, SymbolFlags.Value, true /*ignoreErrors*/, false, null);
                        if (rootSymbol != null && (rootSymbol.Flags & SymbolFlags.Enum) != 0)
                        {
                            var name = expr.AsElementAccessExpression()!.ArgumentExpression!.Text();
                            Symbol? member = rootSymbol.Exports is { } exports && exports.TryGetValue(name, out var m) ? m : null;
                            if (member != null)
                            {
                                if (location != null)
                                {
                                    return EvaluateEnumMember(expr, member, location);
                                }
                                return GetEnumMemberValue(member.ValueDeclaration!);
                            }
                        }
                    }
                    return NewResult(null, false, false, false);
                }
        }
        throw new DebugFailureException("Unhandled case in evaluateEntity");
    }

    internal Result EvaluateEnumMember(Node expr, Symbol symbol, Node location)
    {
        var declaration = symbol.ValueDeclaration;
        if (declaration == null || declaration == location)
        {
            Error(expr, Diagnostics.Property_0_is_used_before_being_assigned, SymbolToStringWorker(symbol));
            return NewResult(null, false, false, false);
        }
        if (!IsBlockScopedNameDeclaredBeforeUse(declaration, location))
        {
            Error(expr, Diagnostics.A_member_initializer_in_a_enum_declaration_cannot_reference_members_declared_after_it_including_members_defined_in_other_enums);
            return NewResult((Number)0, false, false, false);
        }
        var value = GetEnumMemberValue(declaration);
        if (location.Parent != declaration.Parent)
        {
            return NewResult(value.Value, value.IsSyntacticallyString, value.ResolvedOtherFiles, true /*hasExternalReferences*/);
        }
        return value;
    }

    internal Type GetDeclaredTypeOfAlias(Symbol symbol)
    {
        var links = _declaredTypeLinks.Get(symbol);
        if (links._declaredType == null)
        {
            links._declaredType = GetDeclaredTypeOfSymbolWorker(ResolveAliasWorker(symbol));
        }
        return links._declaredType!;
    }

    internal Type GetTypeFromTypeQueryNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            // TypeScript 1.0 spec (April 2014): 3.6.3
            // The expression is processed as an identifier expression (section 4.3)
            // or property access expression(section 4.10),
            // the widened type(section 3.9) of which becomes the result.
            var t = CheckExpressionWithTypeArguments(node);
            links._resolvedType = GetRegularTypeOfLiteralType(GetWidenedTypeWorker(t));
        }
        return links._resolvedType!;
    }

    internal Type GetTypeFromArrayOrTupleTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var target = GetArrayOrTupleTargetType(node);
            if (target == _emptyGenericType)
            {
                links._resolvedType = _emptyObjectType;
            }
            else if (!(node.Kind == Kind.TupleType && Some(node.Elements(), e => IsVariadicTupleElement(e))) && IsDeferredTypeReferenceNode(node, false))
            {
                if (node.Kind == Kind.TupleType && (node.Elements()?.Length ?? 0) == 0)
                {
                    links._resolvedType = target;
                }
                else
                {
                    links._resolvedType = CreateDeferredTypeReference(target, node, null /*mapper*/, null /*alias*/);
                }
            }
            else
            {
                Type[] elementTypes;
                if (node.Kind == Kind.ArrayType)
                {
                    elementTypes = [GetTypeFromTypeNodeWorker(node.AsArrayTypeNode()!.ElementType!)];
                }
                else
                {
                    elementTypes = Map(node.Elements(), t => GetTypeFromTypeNodeWorker(t));
                }
                if ((target._objectFlags & ObjectFlags.Tuple) != 0)
                {
                    links._resolvedType = CreateNormalizedTupleTypeEx(target, elementTypes, ObjectFlags.FromTypeNode);
                }
                else
                {
                    links._resolvedType = CreateTypeReferenceEx(target, elementTypes, ObjectFlags.FromTypeNode);
                }
            }
        }
        return links._resolvedType!;
    }

    internal bool IsVariadicTupleElement(Node node)
    {
        return (GetTupleElementFlags(node) & ElementFlags.Variadic) != 0;
    }

    internal Type GetArrayOrTupleTargetType(Node node)
    {
        var isReadonly = IsReadonlyTypeOperator(node.Parent);
        var elementType = GetArrayElementTypeNode(node);
        if (elementType != null)
        {
            if (isReadonly)
            {
                return _globalReadonlyArrayType;
            }
            return _globalArrayType;
        }
        return GetTupleTargetType(Map(node.Elements(), e => GetTupleElementInfo(e)), isReadonly);
    }

    internal bool IsReadonlyTypeOperator(Node? node)
    {
        return IsTypeOperatorNode(node!) && node!.AsTypeOperatorNode()!.Operator == Kind.ReadonlyKeyword;
    }

    internal Type GetTypeFromNamedTupleTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            if (node.AsNamedTupleMember()!.DotDotDotToken != null)
            {
                links._resolvedType = GetTypeFromRestTypeNode(node);
            }
            else
            {
                links._resolvedType = AddOptionalityEx(GetTypeFromTypeNodeWorker(node.Type()!), true /*isProperty*/, node.QuestionToken() != null);
            }
        }
        return links._resolvedType!;
    }

    internal Type GetTypeFromRestTypeNode(Node node)
    {
        var typeNode = node.Type();
        var elementTypeNode = GetArrayElementTypeNode(typeNode!);
        if (elementTypeNode != null)
        {
            typeNode = elementTypeNode;
        }
        return GetTypeFromTypeNodeWorker(typeNode!);
    }

    internal Node? GetArrayElementTypeNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ParenthesizedType:
                return GetArrayElementTypeNode(node.Type()!);
            case Kind.TupleType:
                if ((node.Elements()?.Length ?? 0) == 1)
                {
                    node = node.Elements()![0];
                    if (node.Kind == Kind.RestType)
                    {
                        return GetArrayElementTypeNode(node.Type()!);
                    }
                    if (node.Kind == Kind.NamedTupleMember && node.AsNamedTupleMember()!.DotDotDotToken != null)
                    {
                        return GetArrayElementTypeNode(node.Type()!);
                    }
                }
                break;
            case Kind.ArrayType:
                return node.AsArrayTypeNode()!.ElementType;
        }
        return null;
    }

    internal Type GetTypeFromOptionalTypeNode(Node node)
    {
        return AddOptionalityEx(GetTypeFromTypeNodeWorker(node.Type()!), true /*isProperty*/, true /*isOptional*/);
    }

    internal Type GetTypeFromUnionTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var alias = GetAliasForTypeNode(node);
            links._resolvedType = GetUnionTypeExWorker(Map(node.AsUnionTypeNode()!.Types!.Nodes, t => GetTypeFromTypeNodeWorker(t)), UnionReduction.Literal, alias, null /*origin*/);
        }
        return links._resolvedType!;
    }

    internal Type GetTypeFromIntersectionTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var alias = GetAliasForTypeNode(node);
            var types = Map(node.AsIntersectionTypeNode()!.Types!.Nodes, t => GetTypeFromTypeNodeWorker(t));
            // We perform no supertype reduction for X & {} or {} & X, where X is one of string, number, bigint,
            // or a pattern literal template type. This enables union types like "a" | "b" | string & {} or
            // "aa" | "ab" | `a${string}` which preserve the literal types for purposes of statement completion.
            var noSupertypeReduction = false;
            if (types.Length == 2)
            {
                var emptyIndex = Array.IndexOf(types, _emptyTypeLiteralType);
                if (emptyIndex >= 0)
                {
                    var t = types[1 - emptyIndex];
                    noSupertypeReduction = (t._flags & (TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 || (t._flags & TypeFlags.TemplateLiteral) != 0 && IsPatternLiteralType(t);
                }
            }
            links._resolvedType = GetIntersectionTypeEx(types, IfElse(noSupertypeReduction, IntersectionFlags.NoSupertypeReduction, (IntersectionFlags)0), alias);
        }
        return links._resolvedType!;
    }

    internal Type GetTypeFromTemplateTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var spans = node.AsTemplateLiteralTypeNode()!.TemplateSpans!;
            var spanNodes = spans.Nodes ?? [];
            var texts = new string[spanNodes.Length + 1];
            var types = new Type[spanNodes.Length];
            texts[0] = node.AsTemplateLiteralTypeNode()!.Head!.Text();
            for (var i = 0; i < spanNodes.Length; i++)
            {
                var span = spanNodes[i];
                texts[i + 1] = span.AsTemplateLiteralTypeSpan()!.Literal!.Text();
                types[i] = GetTypeFromTypeNodeWorker(span.Type()!);
            }
            links._resolvedType = GetTemplateLiteralType(texts, types);
        }
        return links._resolvedType!;
    }

    internal Type GetTypeFromMappedTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var t = NewObjectType(ObjectFlags.Mapped, node.Symbol);
            t.AsMappedType()!._declaration = node.AsMappedTypeNode()!;
            t._alias = GetAliasForTypeNode(node);
            links._resolvedType = t;
            // Eagerly resolve the constraint type which forces an error if the constraint type circularly
            // references itself through one or more type aliases.
            GetConstraintTypeFromMappedType(t);
        }
        return links._resolvedType!;
    }

    internal Type GetTypeFromConditionalTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var checkType = GetTypeFromTypeNodeWorker(node.AsConditionalTypeNode()!.CheckType!);
            var alias = GetAliasForTypeNode(node);
            var allOuterTypeParameters = GetOuterTypeParameters(node, true /*includeThisTypes*/);
            Type[]? outerTypeParameters;
            if (alias != null && (alias._typeArguments?.Length ?? 0) != 0)
            {
                outerTypeParameters = allOuterTypeParameters;
            }
            else
            {
                outerTypeParameters = Filter(allOuterTypeParameters, tp => IsTypeParameterPossiblyReferenced(tp, node));
            }
            var root = new ConditionalRoot
            {
                _node = node.AsConditionalTypeNode()!,
                _checkType = checkType,
                _extendsType = GetTypeFromTypeNodeWorker(node.AsConditionalTypeNode()!.ExtendsType!),
                _isDistributive = (checkType._flags & TypeFlags.TypeParameter) != 0,
                _inferTypeParameters = GetInferTypeParameters(node) ?? [],
                _outerTypeParameters = outerTypeParameters,
                _instantiations = null,
                _alias = alias,
            };
            links._resolvedType = GetConditionalType(root, null /*mapper*/, false /*forConstraint*/, null);
            if (outerTypeParameters != null)
            {
                root._instantiations = new Dictionary<CacheHashKey, Type>();
                root._instantiations[GetConditionalTypeKey(outerTypeParameters, null /*alias*/, false /*forConstraint*/)] = links._resolvedType!;
            }
        }
        return links._resolvedType!;
    }

    internal Type GetConditionalType(ConditionalRoot root, TypeMapper? mapper, bool forConstraint, TypeAlias? alias)
    {
        Type? result = null;
        List<Type>? extraTypes = null;
        var tailCount = 0;
        // We loop here for an immediately nested conditional type in the false position, effectively treating
        // types of the form 'A extends B ? X : C extends D ? Y : E extends F ? Z : ...' as a single construct for
        // purposes of resolution. We also loop here when resolution of a conditional type ends in resolution of
        // another (or, through recursion, possibly the same) conditional type. In the potentially tail-recursive
        // cases we increment the tail recursion counter and stop after 1000 iterations.
        for (;;)
        {
            if (tailCount == 1000)
            {
                Error(_currentNode, Diagnostics.Type_instantiation_is_excessively_deep_and_possibly_infinite);
                return _errorType;
            }
            var checkType = InstantiateType(GetActualTypeVariable(root._checkType!), mapper);
            var extendsType = InstantiateType(root._extendsType!, mapper);
            if (checkType == _errorType || extendsType == _errorType)
            {
                return _errorType;
            }
            if (checkType == _wildcardType || extendsType == _wildcardType)
            {
                return _wildcardType;
            }
            var checkTypeNode = SkipTypeParentheses(root._node!.CheckType);
            var extendsTypeNode = SkipTypeParentheses(root._node!.ExtendsType);
            // When the check and extends types are simple tuple types of the same arity, we defer resolution of the
            // conditional type when any tuple elements are generic. This is such that non-distributable conditional
            // types can be written `[X] extends [Y] ? ...` and be deferred similarly to `X extends Y ? ...`.
            var checkTuples = IsSimpleTupleType(checkTypeNode!) && IsSimpleTupleType(extendsTypeNode!) && (checkTypeNode!.Elements()?.Length ?? 0) == (extendsTypeNode!.Elements()?.Length ?? 0);
            var checkTypeDeferred = IsDeferredType(checkType, checkTuples);
            TypeMapper? combinedMapper = null;
            if (root._inferTypeParameters.Length != 0)
            {
                // When we're looking at making an inference for an infer type, when we get its constraint, it'll automagically be
                // instantiated with the context, so it doesn't need the mapper for the inference context - however the constraint
                // may refer to another _root_, _uncloned_ `infer` type parameter [1], or to something mapped by `mapper` [2].
                // [1] Eg, if we have `Foo<T, U extends T>` and `Foo<number, infer B>` - `B` is constrained to `T`, which, in turn, has been instantiated
                // as `number`
                // Conversely, if we have `Foo<infer A, infer B>`, `B` is still constrained to `T` and `T` is instantiated as `A`
                // [2] Eg, if we have `Foo<T, U extends T>` and `Foo<Q, infer B>` where `Q` is mapped by `mapper` into `number` - `B` is constrained to `T`
                // which is in turn instantiated as `Q`, which is in turn instantiated as `number`.
                // So we need to:
                //    * combine `context.nonFixingMapper` with `mapper` so their constraints can be instantiated in the context of `mapper` (otherwise they'd only get inference context information)
                //    * incorporate all of the component mappers into the combined mapper for the true and false members
                // This means we have two mappers that need applying:
                //    * The original `mapper` used to create this conditional
                //    * The mapper that maps the infer type parameter to its inference result (`context.mapper`)
                var context = NewInferenceContext(root._inferTypeParameters, null /*signature*/, InferenceFlags.None, null);
                if (mapper != null)
                {
                    context._nonFixingMapper = CombineTypeMappers(context._nonFixingMapper, mapper);
                }
                if (!checkTypeDeferred)
                {
                    // We don't want inferences from constraints as they may cause us to eagerly resolve the
                    // conditional type instead of deferring resolution. Also, we always want strict function
                    // types rules (i.e. proper contravariance) for inferences.
                    InferTypes(context._inferences, checkType, extendsType, InferencePriority.NoConstraints | InferencePriority.AlwaysStrict, false);
                }
                // It's possible for 'infer T' type parameters to be given uninstantiated constraints when the
                // those type parameters are used in type references (see getInferredTypeParameterConstraint). For
                // that reason we need context.mapper to be first in the combined mapper. See #42636 for examples.
                if (mapper != null)
                {
                    combinedMapper = CombineTypeMappers(context._mapper, mapper);
                }
                else
                {
                    combinedMapper = context._mapper;
                }
            }
            // Instantiate the extends type including inferences for 'infer T' type parameters
            Type inferredExtendsType;
            if (combinedMapper != null)
            {
                inferredExtendsType = InstantiateType(root._extendsType!, combinedMapper);
            }
            else
            {
                inferredExtendsType = extendsType;
            }
            // We attempt to resolve the conditional type only when the check and extends types are non-generic
            if (!checkTypeDeferred && !IsDeferredType(inferredExtendsType, checkTuples))
            {
                // Return falseType for a definitely false extends check. We check an instantiations of the two
                // types with type parameters mapped to the wildcard type, the most permissive instantiations
                // possible (the wildcard type is assignable to and from all types). If those are not related,
                // then no instantiations will be and we can just return the false branch type.
                if ((inferredExtendsType._flags & TypeFlags.AnyOrUnknown) == 0 && ((checkType._flags & TypeFlags.Any) != 0 || !IsTypeAssignableToWorker(GetPermissiveInstantiation(checkType), GetPermissiveInstantiation(inferredExtendsType))))
                {
                    // Return union of trueType and falseType for 'any' since it matches anything. Furthermore, for a
                    // distributive conditional type applied to the constraint of a type variable, include trueType if
                    // there are possible values of the check type that are also possible values of the extends type.
                    // We use a reverse assignability check as it is less expensive than the comparable relationship
                    // and avoids false positives of a non-empty intersection check.
                    if ((checkType._flags & TypeFlags.Any) != 0 || forConstraint && (inferredExtendsType._flags & TypeFlags.Never) == 0 && SomeType(GetPermissiveInstantiation(inferredExtendsType), t => IsTypeAssignableToWorker(t, GetPermissiveInstantiation(checkType))))
                    {
                        (extraTypes ??= []).Add(InstantiateType(GetTypeFromTypeNodeWorker(root._node!.TrueType!), OrElse(combinedMapper, mapper)));
                    }
                    // If falseType is an immediately nested conditional type that isn't distributive or has an
                    // identical checkType, switch to that type and loop.
                    var falseType = GetTypeFromTypeNodeWorker(root._node!.FalseType!);
                    if ((falseType._flags & TypeFlags.Conditional) != 0)
                    {
                        var newRoot = falseType.AsConditionalType()!._root!;
                        if (newRoot._node!.Parent == root._node!.AsNode() && (!newRoot._isDistributive || newRoot._checkType == root._checkType))
                        {
                            root = newRoot;
                            continue;
                        }
                        var (tailRoot, tailMapper) = GetTailRecursionRoot(falseType, mapper);
                        if (tailRoot != null)
                        {
                            root = tailRoot;
                            mapper = tailMapper;
                            alias = null;
                            if (tailRoot._alias != null)
                            {
                                tailCount++;
                            }
                            continue;
                        }
                    }
                    result = InstantiateType(falseType, mapper);
                    break;
                }
                // Return trueType for a definitely true extends check. We check instantiations of the two
                // types with type parameters mapped to their restrictive form, i.e. a form of the type parameter
                // that has no constraint. This ensures that, for example, the type
                //   type Foo<T extends { x: any }> = T extends { x: string } ? string : number
                // doesn't immediately resolve to 'string' instead of being deferred.
                if ((inferredExtendsType._flags & TypeFlags.AnyOrUnknown) != 0 || IsTypeAssignableToWorker(GetRestrictiveInstantiation(checkType), GetRestrictiveInstantiation(inferredExtendsType)))
                {
                    var trueType = GetTypeFromTypeNodeWorker(root._node!.TrueType!);
                    var trueMapper = OrElse(combinedMapper, mapper);
                    var (tailRoot, tailMapper) = GetTailRecursionRoot(trueType, trueMapper);
                    if (tailRoot != null)
                    {
                        root = tailRoot;
                        mapper = tailMapper;
                        alias = null;
                        if (tailRoot._alias != null)
                        {
                            tailCount++;
                        }
                        continue;
                    }
                    result = InstantiateType(trueType, trueMapper);
                    break;
                }
            }
            // Return a deferred type for a check that is neither definitely true nor definitely false
            result = NewConditionalType(root, mapper, combinedMapper);
            if (alias != null)
            {
                result._alias = alias;
            }
            else
            {
                // Go（checker.go:24840）允许 mapper == nil：instantiateTypeAlias→InstantiateList→InstantiateType 全链路对 null 是 no-op
                // （InstantiateTypeWithAlias 首行 m == null 直接返回 t），故 `!` 只满足形参注解，运行时与 Go 一致。
                result._alias = InstantiateTypeAlias(root._alias!, mapper!);
            }
            break;
        }
        if (extraTypes != null)
        {
            extraTypes.Add(result!);
            return GetUnionTypeWorker(extraTypes.ToArray());
        }
        return result!;
    }

    // We tail-recurse for generic conditional types that (a) have not already been evaluated and cached, and
    // (b) are non distributive, have a check type that is unaffected by instantiation, or have a non-union check
    // type. Note that recursion is possible only through aliased conditional types, so we only increment the tail
    // recursion counter for those.
    internal (ConditionalRoot? newRoot, TypeMapper? newRootMapper) GetTailRecursionRoot(Type newType, TypeMapper? newMapper)
    {
        if ((newType._flags & TypeFlags.Conditional) != 0 && newMapper != null)
        {
            var newRoot = newType.AsConditionalType()!._root!;
            if (newRoot._outerTypeParameters!.Length != 0)
            {
                var typeParamMapper = CombineTypeMappers(newType.AsConditionalType()!._mapper, newMapper);
                var typeArguments = Map(newRoot._outerTypeParameters, t => typeParamMapper.Map(t));
                var newRootMapper = NewTypeMapper(newRoot._outerTypeParameters!, typeArguments);
                Type? newCheckType = null;
                if (newRoot._isDistributive)
                {
                    newCheckType = GetMappedType(newRoot._checkType!, newRootMapper);
                }
                if (newCheckType == null || newCheckType == newRoot._checkType || (newCheckType._flags & (TypeFlags.Union | TypeFlags.Never)) == 0)
                {
                    return (newRoot, newRootMapper);
                }
            }
        }
        return (null, null);
    }

    internal bool IsSimpleTupleType(Node node)
    {
        return IsTupleTypeNode(node) && (node.Elements()?.Length ?? 0) > 0 && !Some(node.Elements(), e => IsOptionalTypeNode(e) || IsRestTypeNode(e) || IsNamedTupleMember(e) && (e.QuestionToken() != null || e.AsNamedTupleMember()!.DotDotDotToken != null));
    }

    internal bool IsDeferredType(Type t, bool checkTuples)
    {
        return IsGenericType(t) || checkTuples && IsTupleType(t) && Some(GetElementTypes(t), e => IsGenericType(e));
    }

    internal Type GetPermissiveInstantiation(Type t)
    {
        if ((t._flags & (TypeFlags.Primitive | TypeFlags.AnyOrUnknown | TypeFlags.Never)) != 0)
        {
            return t;
        }
        var key = new CachedTypeKey(CachedTypeKind.PermissiveInstantiation, t._id);
        if (_cachedTypes.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var result = InstantiateType(t, _permissiveMapper);
        _cachedTypes[key] = result;
        return result;
    }

    internal Type GetRestrictiveInstantiation(Type t)
    {
        if ((t._flags & (TypeFlags.Primitive | TypeFlags.AnyOrUnknown | TypeFlags.Never)) != 0)
        {
            return t;
        }
        var key = new CachedTypeKey(CachedTypeKind.RestrictiveInstantiation, t._id);
        if (_cachedTypes.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var result = InstantiateType(t, _restrictiveMapper);
        _cachedTypes[key] = result;
        // We set the following so we don't attempt to set the restrictive instance of a restrictive instance
        // which is redundant - we'll produce new type identities, but all type params have already been mapped.
        // This also gives us a way to detect restrictive instances upon comparisons and _disable_ the "distributeive constraint"
        // assignability check for them, which is distinctly unsafe, as once you have a restrctive instance, all the type parameters
        // are constrained to `unknown` and produce tons of false positives/negatives!
        _cachedTypes[new CachedTypeKey(CachedTypeKind.RestrictiveInstantiation, result._id)] = result;
        return result;
    }

    internal Type GetRestrictiveTypeParameter(Type t)
    {
        if (t.AsTypeParameter()!._constraint == null && GetConstraintDeclaration(t) == null || t.AsTypeParameter()!._constraint == _noConstraintType)
        {
            return t;
        }
        var key = new CachedTypeKey(CachedTypeKind.RestrictiveTypeParameter, t._id);
        if (_cachedTypes.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var result = NewTypeParameter(t._symbol!);
        result.AsTypeParameter()!._constraint = _noConstraintType;
        _cachedTypes[key] = result;
        return result;
    }

    internal Type RestrictiveMapperWorker(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0)
        {
            return GetRestrictiveTypeParameter(t);
        }
        return t;
    }

    internal Type PermissiveMapperWorker(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0)
        {
            return _wildcardType;
        }
        return t;
    }

    internal Type GetTrueTypeFromConditionalType(Type t)
    {
        var d = t.AsConditionalType()!;
        if (d._resolvedTrueType == null)
        {
            d._resolvedTrueType = InstantiateType(GetTypeFromTypeNodeWorker(d._root!._node!.TrueType!), d._mapper);
        }
        return d._resolvedTrueType!;
    }

    internal Type GetFalseTypeFromConditionalType(Type t)
    {
        var d = t.AsConditionalType()!;
        if (d._resolvedFalseType == null)
        {
            d._resolvedFalseType = InstantiateType(GetTypeFromTypeNodeWorker(d._root!._node!.FalseType!), d._mapper);
        }
        return d._resolvedFalseType!;
    }

    internal Type GetInferredTrueTypeFromConditionalType(Type t)
    {
        var d = t.AsConditionalType()!;
        if (d._resolvedInferredTrueType == null)
        {
            if (d._combinedMapper != null)
            {
                d._resolvedInferredTrueType = InstantiateType(GetTypeFromTypeNodeWorker(d._root!._node!.TrueType!), d._combinedMapper);
            }
            else
            {
                d._resolvedInferredTrueType = GetTrueTypeFromConditionalType(t);
            }
        }
        return d._resolvedInferredTrueType!;
    }
}
