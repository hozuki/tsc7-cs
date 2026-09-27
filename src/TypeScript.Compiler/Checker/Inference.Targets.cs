// ported from: internal/checker/inference.go:448-698
// （inferToMultipleTypes / getSingleTypeVariableFromIntersectionTypes / inferToMultipleTypesWithPriority /
//   inferToConditionalType / inferToTemplateLiteralType / inferFromGenericMappedTypes）
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `for i, target := range types`（`inferToTemplateLiteralType` 里内层 `target` 遮蔽了参数 `target`）→ C# 不允许
//    局部变量遮蔽参数（CS0136），内层改名为 `targetType`；同理内层 `var source *Type` → `sourceType`（Go 里它同样遮蔽
//    参数 `source`，闭包 `choose` 里的 `return source` 指的也是这个内层变量）。
// 2) `core.IfElse(n.contravariant, InferencePriorityContravariantConditional, 0)` → `IfElse(n._contravariant,
//    InferencePriority.ContravariantConditional, InferencePriority.None)`（Go 的无类型常量 0 → 枚举的 None）。
// 3) `c.inferTypesFromTemplateLiteralType`/`isTypeMatchedByTemplateLiteralType` 是 relater.go 的实例方法，
//    方法值在 C# 里是委托参数 → 直接传（`(_compareTypesAssignable` 是 Checker 的 `TypeComparer` 字段）。
// 4) 包级函数 `getSingleTypeVariableFromIntersectionTypes` → 同一个 `partial class Checker` 的 `internal static` 成员。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;

public sealed partial class Checker
{
    internal void InferToMultipleTypes(InferenceState n, Type source, Type[] targets, TypeFlags targetFlags)
    {
        var typeVariableCount = 0;
        if ((targetFlags & TypeFlags.Union) != 0)
        {
            Type? nakedTypeVariable = null;
            Type[] sources;
            if ((source._flags & TypeFlags.Union) != 0)
            {
                sources = source.Types();
            }
            else
            {
                sources = [source];
            }
            var matched = new bool[sources.Length];
            var inferenceCircularity = false;
            // First infer to types that are not naked type variables. For each source type we
            // track whether inferences were made from that particular type to some target with
            // equal priority (i.e. of equal quality) to what we would infer for a naked type
            // parameter.
            foreach (var t in targets)
            {
                if (GetInferenceInfoForType(n, t) != null)
                {
                    nakedTypeVariable = t;
                    typeVariableCount++;
                }
                else
                {
                    for (var i = 0; i < sources.Length; i++)
                    {
                        var saveInferencePriority = n._inferencePriority;
                        n._inferencePriority = InferencePriority.MaxValue;
                        InferFromTypes(n, sources[i], t);
                        if (n._inferencePriority == n._priority)
                        {
                            matched[i] = true;
                        }
                        inferenceCircularity = inferenceCircularity || n._inferencePriority == InferencePriority.Circularity;
                        n._inferencePriority = n._inferencePriority < saveInferencePriority ? n._inferencePriority : saveInferencePriority;
                    }
                }
            }
            if (typeVariableCount == 0)
            {
                // If every target is an intersection of types containing a single naked type variable,
                // make a lower priority inference to that type variable. This handles inferring from
                // 'A | B' to 'T & (X | Y)' where we want to infer 'A | B' for T.
                var intersectionTypeVariable = GetSingleTypeVariableFromIntersectionTypes(n, targets);
                if (intersectionTypeVariable != null)
                {
                    InferWithPriority(n, source, intersectionTypeVariable, InferencePriority.NakedTypeVariable);
                }
                return;
            }
            // If the target has a single naked type variable and no inference circularities were
            // encountered above (meaning we explored the types fully), create a union of the source
            // types from which no inferences have been made so far and infer from that union to the
            // naked type variable.
            if (typeVariableCount == 1 && !inferenceCircularity)
            {
                List<Type> unmatched = [];
                for (var i = 0; i < sources.Length; i++)
                {
                    if (!matched[i])
                    {
                        unmatched.Add(sources[i]);
                    }
                }
                if (unmatched.Count != 0)
                {
                    InferFromTypes(n, GetUnionTypeWorker(unmatched.ToArray()), nakedTypeVariable!);
                    return;
                }
            }
        }
        else
        {
            // We infer from types that are not naked type variables first so that inferences we
            // make from nested naked type variables and given slightly higher priority by virtue
            // of being first in the candidates array.
            foreach (var t in targets)
            {
                if (GetInferenceInfoForType(n, t) != null)
                {
                    typeVariableCount++;
                }
                else
                {
                    InferFromTypes(n, source, t);
                }
            }
        }
        // Inferences directly to naked type variables are given lower priority as they are
        // less specific. For example, when inferring from Promise<string> to T | Promise<T>,
        // we want to infer string for T, not Promise<string> | string. For intersection types
        // we only infer to single naked type variables.
        if (((targetFlags & TypeFlags.Intersection) != 0 && typeVariableCount == 1) || ((targetFlags & TypeFlags.Intersection) == 0 && typeVariableCount > 0))
        {
            foreach (var t in targets)
            {
                if (GetInferenceInfoForType(n, t) != null)
                {
                    InferWithPriority(n, source, t, InferencePriority.NakedTypeVariable);
                }
            }
        }
    }

    internal static Type? GetSingleTypeVariableFromIntersectionTypes(InferenceState n, Type[] types)
    {
        Type? typeVariable = null;
        foreach (var t in types)
        {
            if ((t._flags & TypeFlags.Intersection) == 0)
            {
                return null;
            }
            var v = Find(t.Types(), u => GetInferenceInfoForType(n, u) != null);
            if (v == null || (typeVariable != null && v != typeVariable))
            {
                return null;
            }
            typeVariable = v;
        }
        return typeVariable;
    }

    internal void InferToMultipleTypesWithPriority(InferenceState n, Type source, Type[] targets, TypeFlags targetFlags, InferencePriority newPriority)
    {
        var savePriority = n._priority;
        n._priority |= newPriority;
        InferToMultipleTypes(n, source, targets, targetFlags);
        n._priority = savePriority;
    }

    internal void InferToConditionalType(InferenceState n, Type source, Type target)
    {
        if ((source._flags & TypeFlags.Conditional) != 0)
        {
            InferFromTypes(n, GetNonDistributedTypeParameter(source.AsConditionalType()!._checkType!), target.AsConditionalType()!._checkType!);
            InferFromTypes(n, GetNonDistributedTypeParameter(source.AsConditionalType()!._extendsType!), target.AsConditionalType()!._extendsType!);
            InferFromTypes(n, GetNonDistributedTypeParameter(GetTrueTypeFromConditionalType(source)), GetTrueTypeFromConditionalType(target));
            InferFromTypes(n, GetNonDistributedTypeParameter(GetFalseTypeFromConditionalType(source)), GetFalseTypeFromConditionalType(target));
        }
        else
        {
            Type[] targetTypes = [GetTrueTypeFromConditionalType(target), GetFalseTypeFromConditionalType(target)];
            InferToMultipleTypesWithPriority(n, source, targetTypes, target._flags, IfElse(n._contravariant, InferencePriority.ContravariantConditional, InferencePriority.None));
        }
    }

    internal void InferToTemplateLiteralType(InferenceState n, Type source, TemplateLiteralType target)
    {
        var matches = InferTypesFromTemplateLiteralType(source, target, _compareTypesAssignable);
        var types = target._types!;
        // When the target template literal contains only placeholders (meaning that inference is intended to extract
        // single characters and remainder strings) and inference fails to produce matches, we want to infer 'never' for
        // each placeholder such that instantiation with the inferred value(s) produces 'never', a type for which an
        // assignment check will fail. If we make no inferences, we'll likely end up with the constraint 'string' which,
        // upon instantiation, would collapse all the placeholders to just 'string', and an assignment check might
        // succeed. That would be a pointless and confusing outcome.
        if (matches.Length != 0 || Every(target._texts!, s => s == ""))
        {
            for (var i = 0; i < types.Length; i++)
            {
                var targetType = types[i];
                Type sourceType;
                if (matches.Length != 0)
                {
                    sourceType = matches[i];
                }
                else
                {
                    sourceType = _neverType;
                }
                // If we are inferring from a string literal type to a type variable whose constraint includes one of the
                // allowed template literal placeholder types, infer from a literal type corresponding to the constraint.
                if ((sourceType._flags & TypeFlags.StringLiteral) != 0 && (targetType._flags & TypeFlags.TypeVariable) != 0)
                {
                    var inferenceContext = GetInferenceInfoForType(n, targetType);
                    if (inferenceContext != null)
                    {
                        var constraint = GetBaseConstraintOfTypeWorker(inferenceContext._typeParameter!);
                        if (constraint != null && !IsTypeAny(constraint))
                        {
                            var allTypeFlags = TypeFlags.None;
                            foreach (var t in constraint.Distributed())
                            {
                                allTypeFlags |= t._flags;
                            }
                            // If the constraint contains `string`, we don't need to look for a more preferred type
                            if ((allTypeFlags & TypeFlags.String) == 0)
                            {
                                var str = GetStringLiteralValue(sourceType);
                                // If the type contains `number` or a number literal and the string isn't a valid number, exclude numbers
                                if ((allTypeFlags & TypeFlags.NumberLike) != 0 && !IsValidNumberString(str, true /*roundTripOnly*/))
                                {
                                    allTypeFlags &= ~TypeFlags.NumberLike;
                                }
                                // If the type contains `bigint` or a bigint literal and the string isn't a valid bigint, exclude bigints
                                if ((allTypeFlags & TypeFlags.BigIntLike) != 0 && !IsValidBigIntString(str, true /*roundTripOnly*/))
                                {
                                    allTypeFlags &= ~TypeFlags.BigIntLike;
                                }

                                Type Choose(Type left, Type right)
                                {
                                    if ((right._flags & allTypeFlags) == 0)
                                    {
                                        return left;
                                    }
                                    else if ((left._flags & TypeFlags.String) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.String) != 0)
                                    {
                                        return sourceType;
                                    }
                                    else if ((left._flags & TypeFlags.TemplateLiteral) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.TemplateLiteral) != 0 && IsTypeMatchedByTemplateLiteralType(sourceType, right.AsTemplateLiteralType()!, _compareTypesAssignable))
                                    {
                                        return sourceType;
                                    }
                                    else if ((left._flags & TypeFlags.StringMapping) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.StringMapping) != 0 && str == ApplyStringMapping(right._symbol, str))
                                    {
                                        return sourceType;
                                    }
                                    else if ((left._flags & TypeFlags.StringLiteral) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.StringLiteral) != 0 && GetStringLiteralValue(right) == str)
                                    {
                                        return right;
                                    }
                                    else if ((left._flags & TypeFlags.Number) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.Number) != 0)
                                    {
                                        return GetNumberLiteralType(FromString(str));
                                    }
                                    else if ((left._flags & TypeFlags.Enum) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.Enum) != 0)
                                    {
                                        return GetNumberLiteralType(FromString(str));
                                    }
                                    else if ((left._flags & TypeFlags.NumberLiteral) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.NumberLiteral) != 0 && GetNumberLiteralValue(right) == FromString(str))
                                    {
                                        return right;
                                    }
                                    else if ((left._flags & TypeFlags.BigInt) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.BigInt) != 0)
                                    {
                                        return ParseBigIntLiteralType(str);
                                    }
                                    else if ((left._flags & TypeFlags.BigIntLiteral) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.BigIntLiteral) != 0 && PseudoBigIntToString(GetBigIntLiteralValue(right)) == str)
                                    {
                                        return right;
                                    }
                                    else if ((left._flags & TypeFlags.Boolean) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.Boolean) != 0)
                                    {
                                        if (str == "true")
                                        {
                                            return _trueType;
                                        }
                                        else if (str == "false")
                                        {
                                            return _falseType;
                                        }
                                        else
                                        {
                                            return _booleanType;
                                        }
                                    }
                                    else if ((left._flags & TypeFlags.BooleanLiteral) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.BooleanLiteral) != 0 && IfElse(GetBooleanLiteralValue(right), "true", "false") == str)
                                    {
                                        return right;
                                    }
                                    else if ((left._flags & TypeFlags.Undefined) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.Undefined) != 0 && right.AsIntrinsicType()!._intrinsicName == str)
                                    {
                                        return right;
                                    }
                                    else if ((left._flags & TypeFlags.Null) != 0)
                                    {
                                        return left;
                                    }
                                    else if ((right._flags & TypeFlags.Null) != 0 && right.AsIntrinsicType()!._intrinsicName == str)
                                    {
                                        return right;
                                    }
                                    else
                                    {
                                        return left;
                                    }
                                }

                                var matchingType = _neverType;
                                foreach (var t in constraint.Distributed())
                                {
                                    matchingType = Choose(matchingType, t);
                                }
                                if ((matchingType._flags & TypeFlags.Never) == 0)
                                {
                                    InferFromTypes(n, matchingType, targetType);
                                    continue;
                                }
                            }
                        }
                    }
                }
                InferFromTypes(n, sourceType, targetType);
            }
        }
    }

    internal void InferFromGenericMappedTypes(InferenceState n, Type source, Type target)
    {
        // The source and target types are generic types { [P in S]: X } and { [P in T]: Y }, so we infer
        // from S to T and from X to Y.
        InferFromTypes(n, GetConstraintTypeFromMappedType(source), GetConstraintTypeFromMappedType(target));
        InferFromTypes(n, GetTemplateTypeFromMappedType(source), GetTemplateTypeFromMappedType(target));
        var sourceNameType = GetNameTypeFromMappedType(source);
        var targetNameType = GetNameTypeFromMappedType(target);
        if (sourceNameType != null && targetNameType != null)
        {
            InferFromTypes(n, sourceNameType, targetNameType);
        }
    }
}
