// ported from: internal/checker/inference.go:1434-1685
// （getCovariantInference / getContravariantInference / unionObjectAndArrayLiteralCandidates / hasPrimitiveConstraint /
//   isTypeParameterAtTopLevel / isTypeParameterAtTopLevelInReturnType / getTypeFromInference / getInferenceInfoForType /
//   getCommonSupertype / getSingleCommonSupertype / findLeftmostType / getCommonSubtype / getCombinedTypeFlags /
//   literalTypesWithSameBaseType / isFromInferenceBlockedSource / isSkipDirectInferenceNode / newInferenceInfo /
//   cloneInferenceInfo / clearCachedInferences / hasInferenceCandidates / hasInferenceCandidatesOrDefault /
//   hasTypeParameterDefault / hasOverlappingInferences / mergeInferences）
//
// 与 Go 的偏离（逐条）：
// 1) 包级函数（`getInferenceInfoForType`/`newInferenceInfo`/`cloneInferenceInfo`/`clearCachedInferences`/
//    `hasInferenceCandidates`/`hasInferenceCandidatesOrDefault`/`hasTypeParameterDefault`/`hasOverlappingInferences`）
//    → 同一个 `partial class Checker` 的 `internal static` 成员（`NewInferenceInfo`/`HasOverlappingInferences`
//    被 checker.go:7813/7827 的其它片调用，必须 internal）。
//    其中包级 `hasTypeParameterDefault` 与 checker.go:22307 的**实例方法** `hasTypeParameterDefault` 同名（C6 已落地
//    `HasTypeParameterDefault`）→ 按 docs/checker-plan §5.2 的 Worker 规则，包级这个改名 `HasTypeParameterDefaultWorker`
//    （artifacts/checker-method-collisions.md；inference.go 里只有这一处包级/实例同名）。
// 2) `findLeftmostType`/`getSingleCommonSupertype`/`getCommonSubtype` 在 Go 里对**空**输入会返回 nil（调用方保证非空）→
//    C# 返回 `Type?`，调用点按 Go 的非空不变量加 `!`。
// 3) `core.SameMap`、`core.Same` 的"未改变则返回同一底层数组"语义在 C# `Core.SameMap`/`Core.Same` 里已 1:1 复刻，
//    故 `Same(primaryTypes, types)`（Go 的"同一底层数组"判定）在 C# 里同样成立。
// 4) `slices.Clone(xs)` → 集合表达式 `[.. xs]`；`for i := range a` → 索引循环；`List<T>.Count` 取代 `len(slice)`。
// 5) Go 里被遮蔽的形参（`core.Some(t.Types(), func(t *Type) …)` 遮蔽参数 `t`）→ 内层改名 `u`（C# 禁止遮蔽参数，CS0136）。
// 6) `InferenceInfo._candidates`/`_contraCandidates` 是 `List<Type>`（Checker.Keys.cs:816/817）→ `Clear()`/`Add()`/
//    `Contains()`/`Count`；喂给 `GetUnionTypeExWorker`(Type[])/`GetIntersectionType`(Type[]) 时用 `ToArray()`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type GetCovariantInference(InferenceInfo inference, Signature signature)
    {
        // Extract all object and array literal types and replace them with a single widened and normalized type.
        var candidates = UnionObjectAndArrayLiteralCandidates(inference._candidates.ToArray());
        // We widen inferred literal types if
        // all inferences were made to top-level occurrences of the type parameter, and
        // the type parameter has no constraint or its constraint includes no primitive or literal types, and
        // the type parameter was fixed during inference or does not occur at top-level in the return type.
        var primitiveConstraint = HasPrimitiveConstraint(inference._typeParameter!) || IsConstTypeVariable(inference._typeParameter, 0);
        var widenLiteralTypes = !primitiveConstraint && inference._topLevel && (inference._isFixed || !IsTypeParameterAtTopLevelInReturnType(signature, inference._typeParameter!));
        Type[] baseCandidates;
        if (primitiveConstraint)
        {
            baseCandidates = SameMap(candidates, t => GetRegularTypeOfLiteralType(t));
        }
        else if (widenLiteralTypes)
        {
            baseCandidates = SameMap(candidates, t => GetWidenedLiteralTypeWorker(t));
        }
        else
        {
            baseCandidates = candidates;
        }
        // If all inferences were made from a position that implies a combined result, infer a union type.
        // Otherwise, infer a common supertype.
        Type unwidenedType;
        if ((inference._priority & InferencePriority.PriorityImpliesCombination) != 0)
        {
            unwidenedType = GetUnionTypeExWorker(baseCandidates, UnionReduction.Subtype, null, null);
        }
        else
        {
            unwidenedType = GetCommonSupertype(baseCandidates);
        }
        return GetWidenedTypeWorker(unwidenedType);
    }

    internal Type GetContravariantInference(InferenceInfo inference)
    {
        if ((inference._priority & InferencePriority.PriorityImpliesCombination) != 0)
        {
            return GetIntersectionType(inference._contraCandidates.ToArray());
        }
        return GetCommonSubtype(inference._contraCandidates.ToArray())!;
    }

    internal Type[] UnionObjectAndArrayLiteralCandidates(Type[] candidates)
    {
        if (candidates.Length > 1)
        {
            var objectLiterals = Filter(candidates, t => IsObjectOrArrayLiteralType(t));
            if (objectLiterals.Length != 0)
            {
                var literalsType = GetUnionTypeExWorker(objectLiterals, UnionReduction.Subtype, null, null);
                var nonLiteralTypes = Filter(candidates, t => !IsObjectOrArrayLiteralType(t));
                return Concatenate(nonLiteralTypes, [literalsType]);
            }
        }
        return candidates;
    }

    internal bool HasPrimitiveConstraint(Type t)
    {
        var constraint = GetConstraintOfTypeParameterWorker(t);
        if (constraint != null)
        {
            if ((constraint._flags & TypeFlags.Conditional) != 0)
            {
                constraint = GetDefaultConstraintOfConditionalType(constraint);
            }
            return MaybeTypeOfKind(constraint, TypeFlags.Primitive | TypeFlags.Index | TypeFlags.TemplateLiteral | TypeFlags.StringMapping);
        }
        return false;
    }

    internal bool IsTypeParameterAtTopLevel(Type t, Type tp, int depth)
    {
        return t == tp
            || (t._flags & TypeFlags.UnionOrIntersection) != 0 && Some(t.Types(), u => IsTypeParameterAtTopLevel(u, tp, depth))
            || depth < 3 && (t._flags & TypeFlags.Conditional) != 0
                && (IsTypeParameterAtTopLevel(GetTrueTypeFromConditionalType(t), tp, depth + 1)
                    || IsTypeParameterAtTopLevel(GetFalseTypeFromConditionalType(t), tp, depth + 1));
    }

    internal bool IsTypeParameterAtTopLevelInReturnType(Signature signature, Type typeParameter)
    {
        var typePredicate = GetTypePredicateOfSignatureWorker(signature);
        if (typePredicate != null)
        {
            return typePredicate._t != null && IsTypeParameterAtTopLevel(typePredicate._t, typeParameter, 0);
        }
        return IsTypeParameterAtTopLevel(GetReturnTypeOfSignatureWorker(signature), typeParameter, 0);
    }

    internal Type? GetTypeFromInference(InferenceInfo inference)
    {
        if (inference._candidates.Count != 0)
        {
            return GetUnionTypeExWorker(inference._candidates.ToArray(), UnionReduction.Subtype, null, null);
        }
        if (inference._contraCandidates.Count != 0)
        {
            return GetIntersectionType(inference._contraCandidates.ToArray());
        }
        return null;
    }

    internal static InferenceInfo? GetInferenceInfoForType(InferenceState n, Type t)
    {
        if ((t._flags & TypeFlags.TypeVariable) != 0)
        {
            t = GetNonDistributedTypeParameter(t);
            foreach (var inference in n._inferences)
            {
                if (t == inference._typeParameter)
                {
                    return inference;
                }
            }
        }
        return null;
    }

    internal Type GetCommonSupertype(Type[] types)
    {
        if (types.Length == 1)
        {
            return types[0];
        }
        // Remove nullable types from each of the candidates.
        var primaryTypes = types;
        if (_strictNullChecks)
        {
            primaryTypes = SameMap(types, t => FilterType(t, u => (u._flags & TypeFlags.Nullable) == 0));
        }
        // When the candidate types are all literal types with the same base type, return a union
        // of those literal types. Otherwise, return the leftmost type for which no type to the
        // right is a supertype.
        Type supertype;
        if (LiteralTypesWithSameBaseType(primaryTypes))
        {
            supertype = GetUnionTypeWorker(primaryTypes);
        }
        else
        {
            supertype = GetSingleCommonSupertype(primaryTypes)!;
        }
        // Add any nullable types that occurred in the candidates back to the result.
        if (Same(primaryTypes, types))
        {
            return supertype;
        }
        return GetNullableType(supertype, GetCombinedTypeFlags(types) & TypeFlags.Nullable);
    }

    internal Type? GetSingleCommonSupertype(Type[] types)
    {
        // First, find the leftmost type for which no type to the right is a strict supertype, and if that
        // type is a strict supertype of all other candidates, return it. Otherwise, return the leftmost type
        // for which no type to the right is a (regular) supertype.
        var candidate = FindLeftmostType(types, (c, s, t) => c.IsTypeStrictSubtypeOf(s, t));
        if (Every(types, t => t == candidate || IsTypeStrictSubtypeOf(t, candidate!)))
        {
            return candidate;
        }
        return FindLeftmostType(types, (c, s, t) => c.IsTypeSubtypeOf(s, t));
    }

    internal Type? FindLeftmostType(Type[] types, Func<Checker, Type, Type, bool> f)
    {
        Type? candidate = null;
        foreach (var t in types)
        {
            if (candidate == null || f(this, candidate, t))
            {
                candidate = t;
            }
        }
        return candidate;
    }

    // Return the leftmost type for which no type to the right is a subtype.
    internal Type? GetCommonSubtype(Type[] types)
    {
        Type? subtype = null;
        foreach (var t in types)
        {
            if (subtype == null || IsTypeSubtypeOf(t, subtype))
            {
                subtype = t;
            }
        }
        return subtype;
    }

    internal TypeFlags GetCombinedTypeFlags(Type[] types)
    {
        var flags = TypeFlags.None;
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.Union) != 0)
            {
                flags |= GetCombinedTypeFlags(t.Types());
            }
            else
            {
                flags |= t._flags;
            }
        }
        return flags;
    }

    internal bool LiteralTypesWithSameBaseType(Type[] types)
    {
        Type? commonBaseType = null;
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.Never) == 0)
            {
                var baseType = GetBaseTypeOfLiteralTypeWorker(t);
                if (commonBaseType == null)
                {
                    commonBaseType = baseType;
                }
                if (baseType == t || baseType != commonBaseType)
                {
                    return false;
                }
            }
        }
        return true;
    }

    internal bool IsFromInferenceBlockedSource(Type t)
    {
        return t._symbol != null && Some(t._symbol.Declarations, n => IsSkipDirectInferenceNode(n));
    }

    internal bool IsSkipDirectInferenceNode(Node node)
    {
        return _skipDirectInferenceNodes.Has(node);
    }

    internal static InferenceInfo NewInferenceInfo(Type? typeParameter)
    {
        return new InferenceInfo { _typeParameter = typeParameter, _priority = InferencePriority.MaxValue, _topLevel = true, _impliedArity = -1 };
    }

    internal static InferenceInfo CloneInferenceInfo(InferenceInfo info)
    {
        return new InferenceInfo
        {
            _typeParameter = info._typeParameter,
            _candidates = [.. info._candidates],
            _contraCandidates = [.. info._contraCandidates],
            _inferredType = info._inferredType,
            _priority = info._priority,
            _topLevel = info._topLevel,
            _isFixed = info._isFixed,
            _impliedArity = info._impliedArity,
        };
    }

    internal static void ClearCachedInferences(IReadOnlyList<InferenceInfo> inferences)
    {
        for (var i = 0; i < inferences.Count; i++)
        {
            var inference = inferences[i];
            if (!inference._isFixed)
            {
                inference._inferredType = null;
            }
        }
    }

    internal static bool HasInferenceCandidates(InferenceInfo info)
    {
        return info._candidates.Count != 0 || info._contraCandidates.Count != 0;
    }

    internal static bool HasInferenceCandidatesOrDefault(InferenceInfo info)
    {
        return HasInferenceCandidates(info) || HasTypeParameterDefaultWorker(info._typeParameter!);
    }

    // 包级 `hasTypeParameterDefault`（inference.go:1659）；与 checker.go:22307 的实例方法同名 → 按 Worker 规则改名。
    internal static bool HasTypeParameterDefaultWorker(Type tp)
    {
        if (tp._symbol != null)
        {
            foreach (var d in tp._symbol.Declarations ?? [])
            {
                if (IsTypeParameterDeclaration(d) && d.AsTypeParameterDeclaration()!.DefaultType != null)
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal static bool HasOverlappingInferences(IReadOnlyList<InferenceInfo> a, IReadOnlyList<InferenceInfo> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            if (HasInferenceCandidates(a[i]) && HasInferenceCandidates(b[i]))
            {
                return true;
            }
        }
        return false;
    }

    internal void MergeInferences(List<InferenceInfo> target, IReadOnlyList<InferenceInfo> source)
    {
        for (var i = 0; i < target.Count; i++)
        {
            if (!HasInferenceCandidates(target[i]) && HasInferenceCandidates(source[i]))
            {
                target[i] = source[i];
            }
        }
    }
}
