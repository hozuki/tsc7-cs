// ported from: internal/checker/inference.go:1004-1250
// （inferTypeForHomomorphicMappedType / createReverseMappedType / isPartiallyInferableType /
//   inferReverseMappedType / inferReverseMappedTypeWorker / resolveReverseMappedTypeMembers /
//   getTypeOfReverseMappedSymbol / getLimitedConstraint / replaceIndexedAccess / typesDefinitelyUnrelated /
//   tupleTypesDefinitelyUnrelated / isTupleTypeStructureMatching / isTypeOrBaseIdenticalTo /
//   isTypeCloselyMatchedBy / createEmptyObjectTypeFromStringLiteral）
//
// 与 Go 的偏离（逐条）：
// 1) `ReverseMappedTypeKey{sourceId: …, targetId: …, constraintId: …}`（Go 的带字段名结构体字面量）→
//    C# 对象初始化器（字段名按裁决加 `_` 前缀）。
// 2) `map[ReverseMappedTypeKey]*Type` 的读：`if cached, ok := m[k]; ok` → `TryGetValue`（`Dictionary` 索引器读缺键抛异常）；
//    `core.OrElse(cached, c.unknownType)`（cached 可能是 nil）→ `cached ?? _unknownType`。
// 3) `append` + `[:len-1]`（`reverseMappedSourceStack`/`reverseMappedTargetStack`）→ `List<Type>` 的 `Add`/`RemoveAt`；
//    这两个 stack 同时传给 relater.go 的 `isDeeplyNestedType(t, stack []*Type, n)` → C# 参数类型 `List<Type>`。
// 4) `core.Map(getElementTypes(source), func(t *Type) *Type { … })`（元素可能为 nil，Go 随后 `core.Every` 校验）→
//    C# 里显式写 `Map<Type, Type>(…, t => …!)`：`Map<Type, Type?>` 会得到 `Type?[]`，不能直接喂
//    `createTupleTypeEx([]*Type, …)`（CS8619）。
// 5) `ast.SymbolTable` 是 `Dictionary<string, Symbol>`；`c.newSymbolEx(...)`/`c.newSymbol(...)`/`setStructuredTypeMembers`
//    的 Go nil 实参 → C# `null`（这些参数在 C# 侧是可空的：`Symbol?`/`Signature[]?`/`IndexInfo[]?`）。
// 6) `core.IfElse(cond, 0, ast.SymbolFlagsOptional)`/`(…, ast.CheckFlagsReadonly, 0)` → 枚举的 `None` 成员
//    （Go 的无类型常量 0）。
// 7) 两个 Go 参数被遮蔽的地方（`Filter(origin.Types(), func(t *Type) …)` 遮蔽参数 `t`；
//    `for _, t := range t.Distributed()` 遮蔽参数 `t`）→ 内层改用 `u`/`literalType`（C# 禁止遮蔽参数，CS0136）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    // Infer a suitable input type for a homomorphic mapped type { [P in keyof T]: X }. We construct
    // an object type with the same set of properties as the source type, where the type of each
    // property is computed by inferring from the source property type to X for the type
    // variable T[P] (i.e. we treat the type T[P] as the type variable we're inferring for).
    internal Type? InferTypeForHomomorphicMappedType(Type source, Type target, Type constraint)
    {
        var key = new ReverseMappedTypeKey(source._id, target._id, constraint._id);
        if (_reverseHomomorphicMappedCache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }
        var t = CreateReverseMappedType(source, target, constraint);
        _reverseHomomorphicMappedCache[key] = t!;
        return t;
    }

    internal Type? CreateReverseMappedType(Type source, Type target, Type constraint)
    {
        // We consider a source type reverse mappable if it has a string index signature or if
        // it has one or more properties and is of a partially inferable type.
        if (!(GetIndexInfoOfTypeWorker(source, _stringType) != null || (GetPropertiesOfTypeWorker(source)?.Length ?? 0) != 0 && IsPartiallyInferableType(source)))
        {
            return null;
        }
        // For arrays and tuples we infer new arrays and tuples where the reverse mapping has been
        // applied to the element type(s).
        if (IsArrayTypeWorker(source))
        {
            var elementType = InferReverseMappedType(GetTypeArgumentsWorker(source)[0], target, constraint);
            if (elementType == null)
            {
                return null;
            }
            return CreateArrayTypeEx(elementType, IsReadonlyArrayType(source));
        }
        if (IsTupleType(source))
        {
            var elementTypes = Map<Type, Type>(GetElementTypes(source), t => InferReverseMappedType(t, target, constraint)!);
            if (!Every(elementTypes, t => t != null))
            {
                return null;
            }
            var elementInfos = source.TargetTupleType()!._elementInfos;
            if ((GetMappedTypeModifiers(target) & MappedTypeModifiers.IncludeOptional) != 0)
            {
                elementInfos = SameMap(elementInfos, info =>
                {
                    if ((info._flags & ElementFlags.Optional) != 0)
                    {
                        return new TupleElementInfo { _flags = ElementFlags.Required, _labeledDeclaration = info._labeledDeclaration };
                    }
                    return info;
                });
            }
            return CreateTupleTypeEx(elementTypes, elementInfos!, source.TargetTupleType()!._readonly);
        }
        // For all other object types we infer a new object type where the reverse mapping has been
        // applied to the type of each property.
        var reversed = NewObjectType(ObjectFlags.ReverseMapped | ObjectFlags.Anonymous, null /*symbol*/);
        reversed.AsReverseMappedType()!._source = source;
        reversed.AsReverseMappedType()!._mappedType = target;
        reversed.AsReverseMappedType()!._constraintType = constraint;
        return reversed;
    }

    // We consider a type to be partially inferable if it isn't marked non-inferable or if it is
    // an object literal type with at least one property of an inferable type. For example, an object
    // literal { a: 123, b: x => true } is marked non-inferable because it contains a context sensitive
    // arrow function, but is considered partially inferable because property 'a' has an inferable type.
    internal bool IsPartiallyInferableType(Type t)
    {
        return (t._objectFlags & ObjectFlags.NonInferrableType) == 0
            || IsObjectLiteralType(t) && Some(GetPropertiesOfTypeWorker(t), prop => IsPartiallyInferableType(GetTypeOfSymbolWorker(prop)))
            || IsTupleType(t) && Some(GetElementTypes(t), IsPartiallyInferableType);
    }

    internal Type? InferReverseMappedType(Type source, Type target, Type constraint)
    {
        var key = new ReverseMappedTypeKey(source._id, target._id, constraint._id);
        if (_reverseMappedCache.TryGetValue(key, out var cached))
        {
            return cached ?? _unknownType;
        }
        _reverseMappedSourceStack.Add(source);
        _reverseMappedTargetStack.Add(target);
        var saveExpandingFlags = _reverseExpandingFlags;
        if (IsDeeplyNestedType(source, _reverseMappedSourceStack, 2))
        {
            _reverseExpandingFlags |= ExpandingFlags.Source;
        }
        if (IsDeeplyNestedType(target, _reverseMappedTargetStack, 2))
        {
            _reverseExpandingFlags |= ExpandingFlags.Target;
        }
        Type? t = null;
        if (_reverseExpandingFlags != ExpandingFlags.Both)
        {
            t = InferReverseMappedTypeWorker(source, target, constraint);
        }
        _reverseMappedSourceStack.RemoveAt(_reverseMappedSourceStack.Count - 1);
        _reverseMappedTargetStack.RemoveAt(_reverseMappedTargetStack.Count - 1);
        _reverseExpandingFlags = saveExpandingFlags;
        _reverseMappedCache[key] = t!;
        return t;
    }

    internal Type InferReverseMappedTypeWorker(Type source, Type target, Type constraint)
    {
        var typeParameter = GetIndexedAccessType(constraint.AsIndexType()!._target!, GetTypeParameterFromMappedType(target));
        var templateType = GetTemplateTypeFromMappedType(target);
        var inference = NewInferenceInfo(typeParameter);
        InferTypes([inference], source, templateType, InferencePriority.None, false);
        return GetWidenedTypeWorker(GetTypeFromInference(inference) ?? _unknownType);
    }

    internal void ResolveReverseMappedTypeMembers(Type t)
    {
        var r = t.AsReverseMappedType()!;
        var indexInfo = GetIndexInfoOfTypeWorker(r._source!, _stringType);
        var modifiers = GetMappedTypeModifiers(r._mappedType!);
        var readonlyMask = (modifiers & MappedTypeModifiers.IncludeReadonly) == 0;
        var optionalMask = IfElse((modifiers & MappedTypeModifiers.IncludeOptional) != 0, SymbolFlags.None, SymbolFlags.Optional);
        IndexInfo[]? indexInfos = null;
        if (indexInfo != null)
        {
            indexInfos = [NewIndexInfo(_stringType, InferReverseMappedType(indexInfo._valueType!, r._mappedType!, r._constraintType!) ?? _unknownType, readonlyMask && indexInfo._isReadonly, null, null)];
        }
        var members = new SymbolTable();
        var limitedConstraint = GetLimitedConstraint(t);
        foreach (var prop in GetPropertiesOfTypeWorker(r._source!) ?? [])
        {
            // In case of a reverse mapped type with an intersection constraint, if we were able to
            // extract the filtering type literals we skip those properties that are not assignable to them,
            // because the extra properties wouldn't get through the application of the mapped type anyway
            if (limitedConstraint != null)
            {
                var propertyNameType = GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique, false);
                if (!IsTypeAssignableToWorker(propertyNameType, limitedConstraint))
                {
                    continue;
                }
            }
            var checkFlags = CheckFlags.ReverseMapped | IfElse(readonlyMask && IsReadonlySymbolWorker(prop), CheckFlags.Readonly, CheckFlags.None);
            var inferredProp = NewSymbolEx(SymbolFlags.Property | prop.Flags & optionalMask, prop.Name, checkFlags);
            inferredProp.Declarations = prop.Declarations;
            _valueSymbolLinks.Get(inferredProp)._nameType = _valueSymbolLinks.Get(prop)._nameType;
            var links = ReverseMappedSymbolLinks.Get(inferredProp);
            links._propertyType = GetTypeOfSymbolWorker(prop);
            var constraintTarget = r._constraintType!.AsIndexType()!._target!;
            if ((constraintTarget._flags & TypeFlags.IndexedAccess) != 0 && (constraintTarget.AsIndexedAccessType()!._objectType!._flags & TypeFlags.TypeParameter) != 0 && (constraintTarget.AsIndexedAccessType()!._indexType!._flags & TypeFlags.TypeParameter) != 0)
            {
                // A reverse mapping of `{[K in keyof T[K_1]]: T[K_1]}` is the same as that of `{[K in keyof T]: T}`, since all we care about is
                // inferring to the "type parameter" (or indexed access) shared by the constraint and template. So, to reduce the number of
                // type identities produced, we simplify such indexed access occurrences
                var newTypeParam = constraintTarget.AsIndexedAccessType()!._objectType!;
                var newMappedType = ReplaceIndexedAccess(r._mappedType!, constraintTarget, newTypeParam);
                links._mappedType = newMappedType;
                links._constraintType = GetIndexType(newTypeParam);
            }
            else
            {
                links._mappedType = r._mappedType;
                links._constraintType = r._constraintType;
            }
            members[prop.Name] = inferredProp;
        }
        SetStructuredTypeMembers(t, members, null, null, indexInfos);
    }

    internal Type GetTypeOfReverseMappedSymbol(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            var reverseLinks = ReverseMappedSymbolLinks.Get(symbol);
            links._resolvedType = InferReverseMappedType(reverseLinks._propertyType!, reverseLinks._mappedType!, reverseLinks._constraintType!) ?? _unknownType;
        }
        return links._resolvedType!;
    }

    // If the original mapped type had an intersection constraint we extract its components,
    // and we make an attempt to do so even if the intersection has been reduced to a union.
    // This entire process allows us to possibly retrieve the filtering type literals.
    // e.g. { [K in keyof U & ("a" | "b") ] } -> "a" | "b"
    internal Type? GetLimitedConstraint(Type t)
    {
        var constraint = GetConstraintTypeFromMappedType(t.AsReverseMappedType()!._mappedType!);
        if (!((constraint._flags & TypeFlags.Union) != 0 || (constraint._flags & TypeFlags.Intersection) != 0))
        {
            return null;
        }
        Type? origin = constraint;
        if ((constraint._flags & TypeFlags.Union) != 0)
        {
            origin = constraint.AsUnionType()!._origin;
        }
        if (origin == null || (origin._flags & TypeFlags.Intersection) == 0)
        {
            return null;
        }
        var constraintType = t.AsReverseMappedType()!._constraintType!;
        var limitedConstraint = GetIntersectionType(Filter(origin.Types(), u => u != constraintType));
        if (limitedConstraint != _neverType)
        {
            return limitedConstraint;
        }
        return null;
    }

    internal Type ReplaceIndexedAccess(Type instantiable, Type t, Type replacement)
    {
        // map type.indexType to 0
        // map type.objectType to `[TReplacement]`
        // thus making the indexed access `[TReplacement][0]` or `TReplacement`
        return InstantiateType(instantiable, NewTypeMapper([t.AsIndexedAccessType()!._indexType!, t.AsIndexedAccessType()!._objectType!], [GetNumberLiteralType(0), CreateTupleType([replacement])]));
    }

    internal bool TypesDefinitelyUnrelated(Type source, Type target)
    {
        // Two tuple types with incompatible arities are definitely unrelated.
        // Two object types that each have a property that is unmatched in the other are definitely unrelated.
        if (IsTupleType(source) && IsTupleType(target))
        {
            return TupleTypesDefinitelyUnrelated(source, target);
        }
        return GetUnmatchedProperty(source, target, false /*requireOptionalProperties*/, true /*matchDiscriminantProperties*/) != null
            && GetUnmatchedProperty(target, source, false /*requireOptionalProperties*/, false /*matchDiscriminantProperties*/) != null;
    }

    internal static bool TupleTypesDefinitelyUnrelated(Type source, Type target)
    {
        var s = source.TargetTupleType()!;
        var t = target.TargetTupleType()!;
        return (t._combinedFlags & ElementFlags.Variadic) == 0 && t._minLength > s._minLength
            || (t._combinedFlags & ElementFlags.Variable) == 0 && ((s._combinedFlags & ElementFlags.Variable) != 0 || t._fixedLength < s._fixedLength);
    }

    internal bool IsTupleTypeStructureMatching(Type t1, Type t2)
    {
        if (GetTypeReferenceArity(t1) != GetTypeReferenceArity(t2))
        {
            return false;
        }
        var elementInfos1 = t1.TargetTupleType()!._elementInfos!;
        var elementInfos2 = t2.TargetTupleType()!._elementInfos!;
        for (var i = 0; i < elementInfos1.Length; i++)
        {
            var e = elementInfos1[i];
            if ((e._flags & ElementFlags.Variable) != (elementInfos2[i]._flags & ElementFlags.Variable))
            {
                return false;
            }
        }
        return true;
    }

    internal bool IsTypeOrBaseIdenticalTo(Type s, Type t)
    {
        if (t == _missingType)
        {
            return s == t;
        }
        return IsTypeIdenticalTo(s, t)
            || (t._flags & TypeFlags.String) != 0 && (s._flags & TypeFlags.StringLiteral) != 0
            || (t._flags & TypeFlags.Number) != 0 && (s._flags & TypeFlags.NumberLiteral) != 0;
    }

    internal bool IsTypeCloselyMatchedBy(Type s, Type t)
    {
        return (s._flags & TypeFlags.Object) != 0 && (t._flags & TypeFlags.Object) != 0 && s._symbol != null && s._symbol == t._symbol
            || s._alias != null && t._alias != null && (s._alias._typeArguments?.Length ?? 0) != 0 && s._alias._symbol == t._alias._symbol;
    }

    // Create an object with properties named in the string literal type. Every property has type `any`.
    internal Type CreateEmptyObjectTypeFromStringLiteral(Type t)
    {
        var members = new SymbolTable();
        foreach (var literalType in t.Distributed() ?? [])
        {
            if ((literalType._flags & TypeFlags.StringLiteral) == 0)
            {
                continue;
            }
            var name = GetStringLiteralValue(literalType);
            var literalProp = NewSymbol(SymbolFlags.Property, name);
            _valueSymbolLinks.Get(literalProp)._resolvedType = _anyType;
            if (literalType._symbol != null)
            {
                literalProp.Declarations = literalType._symbol.Declarations;
                literalProp.ValueDeclaration = literalType._symbol.ValueDeclaration;
            }
            members[name] = literalProp;
        }
        IndexInfo[]? indexInfos = null;
        if ((t._flags & TypeFlags.String) != 0)
        {
            indexInfos = [NewIndexInfo(_stringType, _emptyObjectType, false /*isReadonly*/, null, null)];
        }
        return NewAnonymousType(null, members, null, null, indexInfos);
    }
}
