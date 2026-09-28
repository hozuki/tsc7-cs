// ported from: internal/checker/relater.go:3224-4003
// （structuredTypeRelatedTo / isSourceIntersectionNeedingExtraCheck / structuredTypeRelatedToWorker / typeArgumentsRelatedTo）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (r *Relater) x()` → `public sealed partial class Relater` 的 `internal` PascalCase 方法
//    （本范围 4 个声明的接收者全是 *Relater，不是 *Checker）。Go 的**包级函数**在 Relater 里按 Go 裸名调用：
//    `someType`→`SomeType`、`everyType`→`EveryType`、`isObjectLiteralType`→`IsObjectLiteralType`、
//    `isTupleType`→`IsTupleType`、`isMutableTupleType`→`IsMutableTupleType`、
//    `isSingleElementGenericTupleType`→`IsSingleElementGenericTupleType`、`getMappedTypeModifiers`→`GetMappedTypeModifiers`、
//    `isPartialMappedType`→`IsPartialMappedType`（都是 `Checker` 的 static 成员，靠 `using static` 引入）；
//    `chainDepth`→`ChainDepth` 是 `Relater` 的 static 成员（Relater.Report.cs:696），同样裸名调用。
// 2) Go 的两处 `switch { case ...: }` → C# `if`/`else if` 链：Go 的 case 按顺序**首个匹配**即执行并跳出，
//    if/else if 链语义相同；所有判断顺序逐条保留（顺序敏感）。
// 3) Go 的局部变量遮蔽（C# 不允许嵌套作用域里重名声明）：`result := r.eachTypeRelatedToSomeType(...)` 与
//    `result := r.typeRelatedToDiscriminatedType(...)` 两处遮蔽外层 `result` → 改名 `res`（Go 里被遮蔽的外层
//    result 在这两个分支中不再被读取，且分支随即 return，行为一致）；内层 `templateType :=
//    r.c.getTemplateTypeFromMappedType(target)` 遮蔽外层同名变量 → 直接复用外层变量（两次调用返回
//    MappedType.templateType 的同一个缓存值，见 checker.go:23056，可观察行为一致）。
// 4) 闭包 `relateVariances` 的形参 `intersectionState` 在 Go 里遮蔽外层同名形参；C# 局部函数形参不能遮蔽外层形参
//    → 改名 `innerIntersectionState`（所有调用点传入的就是外层 intersectionState，值完全相同）。
// 5) 函数值参数（合同 §4）：`r.isRelatedToWorker` → 缓存字段 `_isRelatedToWorkerDelegate`（热路径零分配）；
//    Go 的内联闭包 → C# lambda/局部函数（非捕获 lambda 由 Roslyn 缓存，无额外分配）。
// 6) 字段：`r.relation`→`_relation`、`r.errorChain`→`_errorChain`、`r.c`→`_c`（Go 未导出字段一律 `_camelCase`）；
//    `source.flags`→`source._flags`、`source.objectFlags`→`source._objectFlags`、`source.alias`→`source._alias`；
//    `source.AsX()` → `source.AsX()!`（C# 返回可空，Go 是直接断言）；`Type.Symbol()` 是**方法**（与 `Node.Symbol` 属性区分）。
// 7) `min(...)`→`Math.Min`、`slices.Equal`→`Core.Same`、`core.Some/IfElse/OrElse`→`Core.Some/IfElse/OrElse`；
//    `len(x)`→`x.Length`/`x.Count`，Go 的 nil 切片长度视为 0 → `(x?.Length ?? 0)`/`Core.Some(null)==false`。
namespace TypeScript.Checker;

// usings 放在 file-scoped namespace **之后**（Types.cs / Checker.Keys.cs / Checker.C3.Expressions.cs 的既有布局）：
// 这样 `using TypeScript.Core;` 等导入的类型成为本命名空间的成员，优先于外层命名空间里的同名**命名空间**
// （`TypeScript.Core` / `TypeScript.Diagnostics` / `TypeScript.Ast` 都是命名空间），否则裸写 `Core.Some(...)`
// 会解析成命名空间 `TypeScript.Core` → CS0234。本文件用到的 `Core.Some/IfElse/OrElse/Same` 依赖此布局。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Checker.Checker;

public sealed partial class Relater
{
    internal Ternary StructuredTypeRelatedTo(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
    {
        var saveErrorState = GetErrorState();
        var result = StructuredTypeRelatedToWorker(source, target, reportErrors, intersectionState);
        if (_relation != _c._identityRelation)
        {
            // The combined constraint of an intersection type is the intersection of the constraints of
            // the constituents. When an intersection type contains instantiable types with union type
            // constraints, there are situations where we need to examine the combined constraint. One is
            // when the target is a union type. Another is when the intersection contains types belonging
            // to one of the disjoint domains. For example, given type variables T and U, each with the
            // constraint 'string | number', the combined constraint of 'T & U' is 'string | number' and
            // we need to check this constraint against a union on the target side. Also, given a type
            // variable V constrained to 'string | number', 'V & number' has a combined constraint of
            // 'string & number | number & number' which reduces to just 'number'.
            // This also handles type parameters, as a type parameter with a union constraint compared against a union
            // needs to have its constraint hoisted into an intersection with said type parameter, this way
            // the type param can be compared with itself in the target (with the influence of its constraint to match other parts)
            // For example, if `T extends 1 | 2` and `U extends 2 | 3` and we compare `T & U` to `T & U & (1 | 2 | 3)`
            if (result == Ternary.False && ((source._flags & TypeFlags.Intersection) != 0 || (source._flags & TypeFlags.TypeParameter) != 0 && (target._flags & TypeFlags.Union) != 0))
            {
                Type[] sourceTypes;
                if ((source._flags & TypeFlags.Intersection) != 0)
                {
                    sourceTypes = source.Types();
                }
                else
                {
                    sourceTypes = new Type[] { source };
                }
                var constraint = _c.GetEffectiveConstraintOfIntersection(sourceTypes, (target._flags & TypeFlags.Union) != 0);
                if (constraint != null && EveryType(constraint, x => x != source))
                {
                    // TODO: Stack errors so we get a pyramid for the "normal" comparison above, _and_ a second for this
                    result = IsRelatedToEx(constraint, target, RecursionFlags.Source, false /*reportErrors*/, null /*headMessage*/, intersectionState);
                }
            }
            if (result != Ternary.False && (intersectionState & IntersectionState.Target) == 0 && (target._flags & TypeFlags.Intersection) != 0 && !_c.IsGenericObjectType(target) && (source._flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
            {
                // When the target is an intersection we need an extra property check in order to detect nested excess
                // properties and nested weak types. The following are motivating examples that all should be errors, but
                // aren't without this extra property check:
                //
                //   let obj: { a: { x: string } } & { c: number } = { a: { x: 'hello', y: 2 }, c: 5 };  // Nested excess property
                //
                //   declare let wrong: { a: { y: string } };
                //   let weak: { a?: { x?: number } } & { c?: string } = wrong;  // Nested weak object type
                result &= PropertiesRelatedTo(source, target, reportErrors, new Set<string>() /*excludedProperties*/, false /*optionalsOnly*/, IntersectionState.None);
                if (result != Ternary.False && IsObjectLiteralType(source) && (source._objectFlags & ObjectFlags.FreshLiteral) != 0)
                {
                    result &= IndexSignaturesRelatedTo(source, target, false /*sourceIsPrimitive*/, reportErrors, IntersectionState.None);
                }
            }
            else if (result != Ternary.False && _c.IsNonGenericObjectType(target) && !_c.IsArrayOrTupleType(target) && IsSourceIntersectionNeedingExtraCheck(source, target))
            {
                // When the source is an intersection we need an extra check of any optional properties in the target to
                // detect possible mismatched property types. For example:
                //
                //   function foo<T extends object>(x: { a?: string }, y: T & { a: boolean }) {
                //     x = y;  // Mismatched property in source intersection
                //   }
                result &= PropertiesRelatedTo(source, target, reportErrors, new Set<string>() /*excludedProperties*/, true /*optionalsOnly*/, intersectionState);
            }
        }
        if (result != Ternary.False)
        {
            RestoreErrorState(saveErrorState);
        }
        return result;
    }

    internal bool IsSourceIntersectionNeedingExtraCheck(Type source, Type target)
    {
        return (source._flags & TypeFlags.Intersection) != 0 && (_c.GetApparentTypeWorker(source)._flags & TypeFlags.StructuredType) != 0 &&
            !Core.Some(source.Types(), x => x == target || (x._objectFlags & ObjectFlags.NonInferrableType) != 0);
    }

    internal Ternary StructuredTypeRelatedToWorker(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
    {
        var result = Ternary.False;
        var varianceCheckFailed = false;
        ErrorChain? originalErrorChain = null;
        var saveErrorState = GetErrorState();

        // Go: 闭包参数名为 `intersectionState`（遮蔽外层同名形参）；C# 局部函数形参不能与外层形参同名 → 改名。
        (Ternary Result, bool Ok) RelateVariances(Type[] sourceTypeArguments, Type[] targetTypeArguments, VarianceFlags[] variances, IntersectionState innerIntersectionState)
        {
            if ((result = TypeArgumentsRelatedTo(sourceTypeArguments, targetTypeArguments, variances, reportErrors, innerIntersectionState)) != Ternary.False)
            {
                return (result, true);
            }
            if (Core.Some(variances, v => (v & VarianceFlags.AllowsStructuralFallback) != 0))
            {
                // If some type parameter was `Unmeasurable` or `Unreliable`, and we couldn't pass by assuming it was identical, then we
                // have to allow a structural fallback check
                // We elide the variance-based error elaborations, since those might not be too helpful, since we'll potentially
                // be assuming identity of the type parameter.
                originalErrorChain = null;
                RestoreErrorState(saveErrorState);
                return (Ternary.False, false);
            }
            var allowStructuralFallback = _c.HasCovariantVoidArgument(targetTypeArguments, variances);
            varianceCheckFailed = !allowStructuralFallback;
            // The type arguments did not relate appropriately, but it may be because we have no variance
            // information (in which case typeArgumentsRelatedTo defaulted to covariance for all type
            // arguments). It might also be the case that the target type has a 'void' type argument for
            // a covariant type parameter that is only used in return positions within the generic type
            // (in which case any type argument is permitted on the source side). In those cases we proceed
            // with a structural comparison. Otherwise, we know for certain the instantiations aren't
            // related and we can return here.
            if (variances.Length != 0 && !allowStructuralFallback)
            {
                // In some cases generic types that are covariant in regular type checking mode become
                // invariant in --strictFunctionTypes mode because one or more type parameters are used in
                // both co- and contravariant positions. In order to make it easier to diagnose *why* such
                // types are invariant, if any of the type parameters are invariant we reset the reported
                // errors and instead force a structural comparison (which will include elaborations that
                // reveal the reason).
                // We can switch on `reportErrors` here, since varianceCheckFailed guarantees we return `False`,
                // we can return `False` early here to skip calculating the structural error message we don't need.
                if (varianceCheckFailed && !(reportErrors && Core.Some(variances, v => (v & VarianceFlags.VarianceMask) == VarianceFlags.Invariant)))
                {
                    return (Ternary.False, true);
                }
                // We remember the original error information so we can restore it in case the structural
                // comparison unexpectedly succeeds. This can happen when the structural comparison result
                // is a Ternary.Maybe for example caused by the recursion depth limiter.
                originalErrorChain = _errorChain;
                RestoreErrorState(saveErrorState);
            }
            return (Ternary.False, false);
        }

        if (_relation == _c._identityRelation)
        {
            // We've already checked that source.flags and target.flags are identical
            if ((source._flags & TypeFlags.UnionOrIntersection) != 0)
            {
                // Go 此处 `result :=` 遮蔽外层 result（新局部变量）→ C# 不允许 → 改名 `res`。
                var res = EachTypeRelatedToSomeType(source, target);
                if (res != Ternary.False)
                {
                    res &= EachTypeRelatedToSomeType(target, source);
                }
                return res;
            }
            else if ((source._flags & TypeFlags.Index) != 0)
            {
                return IsRelatedTo(source.Target()!, target.Target()!, RecursionFlags.Both, false /*reportErrors*/);
            }
            else if ((source._flags & TypeFlags.IndexedAccess) != 0)
            {
                result = IsRelatedTo(source.AsIndexedAccessType()!._objectType!, target.AsIndexedAccessType()!._objectType!, RecursionFlags.Both, false /*reportErrors*/);
                if (result != Ternary.False)
                {
                    result &= IsRelatedTo(source.AsIndexedAccessType()!._indexType!, target.AsIndexedAccessType()!._indexType!, RecursionFlags.Both, false /*reportErrors*/);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            else if ((source._flags & TypeFlags.Conditional) != 0)
            {
                if (source.AsConditionalType()!._root!._isDistributive == target.AsConditionalType()!._root!._isDistributive)
                {
                    result = IsRelatedTo(source.AsConditionalType()!._root!._checkType!, target.AsConditionalType()!._root!._checkType!, RecursionFlags.Both, false /*reportErrors*/);
                    if (result != Ternary.False)
                    {
                        result &= IsRelatedTo(source.AsConditionalType()!._root!._extendsType!, target.AsConditionalType()!._root!._extendsType!, RecursionFlags.Both, false /*reportErrors*/);
                        if (result != Ternary.False)
                        {
                            result &= IsRelatedTo(_c.GetTrueTypeFromConditionalType(source), _c.GetTrueTypeFromConditionalType(target), RecursionFlags.Both, false /*reportErrors*/);
                            if (result != Ternary.False)
                            {
                                result &= IsRelatedTo(_c.GetFalseTypeFromConditionalType(source), _c.GetFalseTypeFromConditionalType(target), RecursionFlags.Both, false /*reportErrors*/);
                                if (result != Ternary.False)
                                {
                                    return result;
                                }
                            }
                        }
                    }
                }
            }
            else if ((source._flags & TypeFlags.Substitution) != 0)
            {
                result = IsRelatedTo(source.AsSubstitutionType()!._baseType!, target.AsSubstitutionType()!._baseType!, RecursionFlags.Both, false /*reportErrors*/);
                if (result != Ternary.False)
                {
                    result &= IsRelatedTo(source.AsSubstitutionType()!._constraint!, target.AsSubstitutionType()!._constraint!, RecursionFlags.Both, false /*reportErrors*/);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            else if ((source._flags & TypeFlags.TemplateLiteral) != 0)
            {
                if (Core.SameElements(source.AsTemplateLiteralType()!._texts, target.AsTemplateLiteralType()!._texts))
                {
                    result = Ternary.True;
                    for (var i = 0; i < source.AsTemplateLiteralType()!._types!.Length; i++)
                    {
                        var sourceType = source.AsTemplateLiteralType()!._types![i];
                        var targetType = target.AsTemplateLiteralType()!._types![i];
                        result &= IsRelatedTo(sourceType, targetType, RecursionFlags.Both, false /*reportErrors*/);
                        if (result == Ternary.False)
                        {
                            return result;
                        }
                    }
                    return result;
                }
            }
            else if ((source._flags & TypeFlags.StringMapping) != 0)
            {
                if (source.AsStringMappingType()!.Symbol() == target.AsStringMappingType()!.Symbol())
                {
                    return IsRelatedTo(source.AsStringMappingType()!._target!, target.AsStringMappingType()!._target!, RecursionFlags.Both, false /*reportErrors*/);
                }
            }
            if ((source._flags & TypeFlags.Object) == 0)
            {
                return Ternary.False;
            }
        }
        else if ((source._flags & TypeFlags.UnionOrIntersection) != 0 || (target._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            result = UnionOrIntersectionRelatedTo(source, target, reportErrors, intersectionState);
            if (result != Ternary.False)
            {
                return result;
            }
            // The ordered decomposition above doesn't handle all cases. Specifically, we also need to handle:
            // Source is instantiable (e.g. source has union or intersection constraint).
            // Source is an object, target is a union (e.g. { a, b: boolean } <=> { a, b: true } | { a, b: false }).
            // Source is an intersection, target is an object (e.g. { a } & { b } <=> { a, b }).
            // Source is an intersection, target is a union (e.g. { a } & { b: boolean } <=> { a, b: true } | { a, b: false }).
            // Source is an intersection, target instantiable (e.g. string & { tag } <=> T["a"] constrained to string & { tag }).
            if (!((source._flags & TypeFlags.Instantiable) != 0 ||
                (source._flags & TypeFlags.Object) != 0 && (target._flags & TypeFlags.Union) != 0 ||
                (source._flags & TypeFlags.Intersection) != 0 && (target._flags & (TypeFlags.Object | TypeFlags.Union | TypeFlags.Instantiable)) != 0))
            {
                return Ternary.False;
            }
        }

        // We limit alias variance probing to only object and conditional types since their alias behavior
        // is more predictable than other, interned types, which may or may not have an alias depending on
        // the order in which things were checked.
        if ((source._flags & (TypeFlags.Object | TypeFlags.Conditional)) != 0 && source._alias != null && (source._alias._typeArguments?.Length ?? 0) != 0 &&
            target._alias != null && source._alias._symbol == target._alias._symbol && !(_c.IsMarkerType(source) || _c.IsMarkerType(target)))
        {
            var variances = _c.GetAliasVariances(source._alias._symbol!);
            if (variances.Length == 0)
            {
                return Ternary.Unknown;
            }
            var parameters = _c._typeAliasLinks.Get(source._alias._symbol!)._typeParameters!;
            var minParams = _c.GetMinTypeArgumentCountWorker(parameters);
            var nodeIsInJsFile = IsInJSFile(source._alias._symbol!.ValueDeclaration);
            var sourceTypes = _c.FillMissingTypeArgumentsWorker(source._alias._typeArguments, parameters, minParams, nodeIsInJsFile)!;
            var targetTypes = _c.FillMissingTypeArgumentsWorker(target._alias._typeArguments, parameters, minParams, nodeIsInJsFile)!;
            var (varianceResult, ok) = RelateVariances(sourceTypes, targetTypes, variances, intersectionState);
            if (ok)
            {
                return varianceResult;
            }
        }

        // For a generic type T and a type U that is assignable to T, [...U] is assignable to T, U is assignable to readonly [...T],
        // and U is assignable to [...T] when U is constrained to a mutable array or tuple type.
        if (IsSingleElementGenericTupleType(source) && !source.TargetTupleType()!._readonly)
        {
            result = IsRelatedTo(_c.GetTypeArgumentsWorker(source)[0], target, RecursionFlags.Source, false /*reportErrors*/);
            if (result != Ternary.False)
            {
                return result;
            }
        }
        if (IsSingleElementGenericTupleType(target) && (target.TargetTupleType()!._readonly || _c.IsMutableArrayOrTuple(_c.GetBaseConstraintOrType(source))))
        {
            result = IsRelatedTo(source, _c.GetTypeArgumentsWorker(target)[0], RecursionFlags.Target, false /*reportErrors*/);
            if (result != Ternary.False)
            {
                return result;
            }
        }

        if ((target._flags & TypeFlags.TypeParameter) != 0)
        {
            // A source type { [P in Q]: X } is related to a target type T if keyof T is related to Q and X is related to T[Q].
            if ((source._objectFlags & ObjectFlags.Mapped) != 0 && source.AsMappedType()!._declaration!.NameType == null && IsRelatedTo(_c.GetIndexType(target), _c.GetConstraintTypeFromMappedType(source), RecursionFlags.Both, false) != Ternary.False)
            {
                if ((GetMappedTypeModifiers(source) & MappedTypeModifiers.IncludeOptional) == 0)
                {
                    var templateType = _c.GetTemplateTypeFromMappedType(source);
                    var indexedAccessType = _c.GetIndexedAccessType(target, _c.GetTypeParameterFromMappedType(source));
                    result = IsRelatedTo(templateType, indexedAccessType, RecursionFlags.Both, reportErrors);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            if (_relation == _c._comparableRelation && (source._flags & TypeFlags.TypeParameter) != 0)
            {
                // This is a carve-out in comparability to essentially forbid comparing a type parameter with another type parameter
                // unless one extends the other. (Remember: comparability is mostly bidirectional!)
                var constraint = _c.GetConstraintOfTypeParameterWorker(source);
                if (constraint != null && SomeType(constraint, x => (x._flags & TypeFlags.TypeParameter) != 0))
                {
                    return IsRelatedTo(constraint, target, RecursionFlags.Source, false /*reportErrors*/);
                }
                return Ternary.False;
            }
        }
        else if ((target._flags & TypeFlags.IndexedAccess) != 0)
        {
            if ((source._flags & TypeFlags.IndexedAccess) != 0)
            {
                // Relate components directly before falling back to constraint relationships
                // A type S[K] is related to a type T[J] if S is related to T and K is related to J.
                result = IsRelatedTo(source.AsIndexedAccessType()!._objectType!, target.AsIndexedAccessType()!._objectType!, RecursionFlags.Both, reportErrors);
                if (result != Ternary.False)
                {
                    result &= IsRelatedTo(source.AsIndexedAccessType()!._indexType!, target.AsIndexedAccessType()!._indexType!, RecursionFlags.Both, reportErrors);
                }
                if (result != Ternary.False)
                {
                    return result;
                }
                if (reportErrors)
                {
                    originalErrorChain = _errorChain;
                }
            }
            // A type S is related to a type T[K] if S is related to C, where C is the base
            // constraint of T[K] for writing.
            if (_relation == _c._assignableRelation || _relation == _c._comparableRelation)
            {
                var objectType = target.AsIndexedAccessType()!._objectType!;
                var indexType = target.AsIndexedAccessType()!._indexType!;
                var baseObjectType = _c.GetBaseConstraintOrType(objectType);
                var baseIndexType = _c.GetBaseConstraintOrType(indexType);
                if (!_c.IsGenericObjectType(baseObjectType) && !_c.IsGenericIndexType(baseIndexType))
                {
                    var accessFlags = AccessFlags.Writing | Core.IfElse(baseObjectType != objectType, AccessFlags.NoIndexSignatures, (AccessFlags)0);
                    var constraint = _c.GetIndexedAccessTypeOrUndefined(baseObjectType, baseIndexType, accessFlags, null, null);
                    if (constraint != null)
                    {
                        if (reportErrors && originalErrorChain != null)
                        {
                            // create a new chain for the constraint error
                            RestoreErrorState(saveErrorState);
                        }
                        result = IsRelatedToEx(source, constraint, RecursionFlags.Target, reportErrors, null /*headMessage*/, intersectionState);
                        if (result != Ternary.False)
                        {
                            return result;
                        }
                        // prefer the shorter chain of the constraint comparison chain, and the direct comparison chain
                        if (reportErrors && originalErrorChain != null && _errorChain != null)
                        {
                            if (ChainDepth(originalErrorChain) <= ChainDepth(_errorChain))
                            {
                                _errorChain = originalErrorChain;
                            }
                        }
                    }
                }
            }
            if (reportErrors)
            {
                originalErrorChain = null;
            }
        }
        else if ((target._flags & TypeFlags.Index) != 0)
        {
            var targetType = target.AsIndexType()!._target!;
            // A keyof S is related to a keyof T if T is related to S.
            if ((source._flags & TypeFlags.Index) != 0)
            {
                result = IsRelatedTo(targetType, source.AsIndexType()!._target!, RecursionFlags.Both, false /*reportErrors*/);
                if (result != Ternary.False)
                {
                    return result;
                }
            }
            if (IsTupleType(targetType))
            {
                // An index type can have a tuple type target when the tuple type contains variadic elements.
                // Check if the source is related to the known keys of the tuple type.
                result = IsRelatedTo(source, _c.GetKnownKeysOfTupleType(targetType), RecursionFlags.Target, reportErrors);
                if (result != Ternary.False)
                {
                    return result;
                }
            }
            else
            {
                // A type S is assignable to keyof T if S is assignable to keyof C, where C is the
                // simplified form of T or, if T doesn't simplify, the constraint of T.
                var constraint = _c.GetSimplifiedTypeOrConstraint(targetType);
                if (constraint != null)
                {
                    // We require Ternary.True here such that circular constraints don't cause
                    // false positives. For example, given 'T extends { [K in keyof T]: string }',
                    // 'keyof T' has itself as its constraint and produces a Ternary.Maybe when
                    // related to other types.
                    if (IsRelatedTo(source, _c.GetIndexTypeEx(constraint, target.AsIndexType()!._indexFlags | IndexFlags.NoReducibleCheck), RecursionFlags.Target, reportErrors) == Ternary.True)
                    {
                        return Ternary.True;
                    }
                }
                else if (_c.IsGenericMappedType(targetType))
                {
                    // generic mapped types that don't simplify or have a constraint still have a very simple set of keys we can compare against
                    // - their nameType or constraintType.
                    // In many ways, this comparison is a deferred version of what `getIndexTypeForMappedType` does to actually resolve the keys for _non_-generic types
                    var nameType = _c.GetNameTypeFromMappedType(targetType);
                    var constraintType = _c.GetConstraintTypeFromMappedType(targetType);
                    Type targetKeys;
                    if (nameType != null && _c.IsMappedTypeWithKeyofConstraintDeclaration(targetType))
                    {
                        // we need to get the apparent mappings and union them with the generic mappings, since some properties may be
                        // missing from the `constraintType` which will otherwise be mapped in the object
                        var mappedKeys = _c.GetApparentMappedTypeKeys(nameType, targetType);
                        // We still need to include the non-apparent (and thus still generic) keys in the target side of the comparison (in case they're in the source side)
                        targetKeys = _c.GetUnionTypeWorker(new Type[] { mappedKeys, nameType });
                    }
                    else if (nameType != null)
                    {
                        targetKeys = nameType;
                    }
                    else
                    {
                        targetKeys = constraintType;
                    }
                    if (IsRelatedTo(source, targetKeys, RecursionFlags.Target, reportErrors) == Ternary.True)
                    {
                        return Ternary.True;
                    }
                }
            }
        }
        else if ((target._flags & TypeFlags.Conditional) != 0)
        {
            // If we reach 10 levels of nesting for the same conditional type, assume it is an infinitely expanding recursive
            // conditional type and bail out with a Ternary.Maybe result.
            if (_c.IsDeeplyNestedType(target, _targetStack, 10))
            {
                return Ternary.Maybe;
            }
            var c = target.AsConditionalType()!;
            // We check for a relationship to a conditional type target only when the conditional type has no
            // 'infer' positions, is not distributive or is distributive but doesn't reference the check type
            // parameter in either of the result types, and the source isn't an instantiation of the same
            // conditional type (as happens when computing variance).
            if (c._root!._inferTypeParameters == null && !_c.IsDistributionDependent(c._root!) && !((source._flags & TypeFlags.Conditional) != 0 && source.AsConditionalType()!._root == c._root))
            {
                // Check if the conditional is always true or always false but still deferred for distribution purposes.
                var skipTrue = !_c.IsTypeAssignableToWorker(_c.GetPermissiveInstantiation(c._root!._checkType!), _c.GetPermissiveInstantiation(c._root!._extendsType!));
                var skipFalse = !skipTrue && _c.IsTypeAssignableToWorker(_c.GetRestrictiveInstantiation(c._root!._checkType!), _c.GetRestrictiveInstantiation(c._root!._extendsType!));
                // TODO: Find a nice way to include potential conditional type breakdowns in error output, if they seem good (they usually don't)
                if (skipTrue)
                {
                    result = Ternary.True;
                }
                else
                {
                    result = IsRelatedToEx(source, _c.GetTrueTypeFromConditionalType(target), RecursionFlags.Target, false /*reportErrors*/, null /*headMessage*/, intersectionState);
                }
                if (result != Ternary.False)
                {
                    if (skipFalse)
                    {
                        result &= Ternary.True;
                    }
                    else
                    {
                        result &= IsRelatedToEx(source, _c.GetFalseTypeFromConditionalType(target), RecursionFlags.Target, false /*reportErrors*/, null /*headMessage*/, intersectionState);
                    }
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
        }
        else if ((target._flags & TypeFlags.TemplateLiteral) != 0)
        {
            if ((source._flags & TypeFlags.TemplateLiteral) != 0)
            {
                if (_relation == _c._comparableRelation)
                {
                    if (_c.TemplateLiteralTypesDefinitelyUnrelated(source.AsTemplateLiteralType()!, target.AsTemplateLiteralType()!))
                    {
                        return Ternary.False;
                    }
                    return Ternary.True;
                }
                // Report unreliable variance for type variables referenced in template literal type placeholders.
                // For example, `foo-${number}` is related to `foo-${string}` even though number isn't related to string.
                _c.InstantiateType(source, _c._reportUnreliableMapper);
            }
            if (_c.IsTypeMatchedByTemplateLiteralType(source, target.AsTemplateLiteralType()!, _isRelatedToWorkerDelegate))
            {
                return Ternary.True;
            }
        }
        else if ((target._flags & TypeFlags.StringMapping) != 0)
        {
            if ((source._flags & TypeFlags.StringMapping) == 0)
            {
                if (_c.IsMemberOfStringMapping(source, target))
                {
                    return Ternary.True;
                }
            }
        }
        else if (_c.IsGenericMappedType(target) && _relation != _c._identityRelation)
        {
            // Check if source type `S` is related to target type `{ [P in Q]: T }` or `{ [P in Q as R]: T}`.
            var keysRemapped = target.AsMappedType()!._declaration!.NameType != null;
            var templateType = _c.GetTemplateTypeFromMappedType(target);
            var modifiers = GetMappedTypeModifiers(target);
            if ((modifiers & MappedTypeModifiers.ExcludeOptional) == 0)
            {
                // If the mapped type has shape `{ [P in Q]: T[P] }`,
                // source `S` is related to target if `T` = `S`, i.e. `S` is related to `{ [P in Q]: S[P] }`.
                if (!keysRemapped && (templateType._flags & TypeFlags.IndexedAccess) != 0 && templateType.AsIndexedAccessType()!._objectType == source && templateType.AsIndexedAccessType()!._indexType == _c.GetTypeParameterFromMappedType(target))
                {
                    return Ternary.True;
                }
                if (!_c.IsGenericMappedType(source))
                {
                    // If target has shape `{ [P in Q as R]: T}`, then its keys have type `R`.
                    // If target has shape `{ [P in Q]: T }`, then its keys have type `Q`.
                    Type targetKeys;
                    if (keysRemapped)
                    {
                        targetKeys = _c.GetNameTypeFromMappedType(target)!;
                    }
                    else
                    {
                        targetKeys = _c.GetConstraintTypeFromMappedType(target);
                    }
                    // Type of the keys of source type `S`, i.e. `keyof S`.
                    var sourceKeys = _c.GetIndexTypeEx(source, IndexFlags.NoIndexSignatures);
                    var includeOptional = (modifiers & MappedTypeModifiers.IncludeOptional) != 0;
                    Type? filteredByApplicability = null;
                    if (includeOptional)
                    {
                        filteredByApplicability = _c.IntersectTypes(targetKeys, sourceKeys);
                    }
                    // A source type `S` is related to a target type `{ [P in Q]: T }` if `Q` is related to `keyof S` and `S[Q]` is related to `T`.
                    // A source type `S` is related to a target type `{ [P in Q as R]: T }` if `R` is related to `keyof S` and `S[R]` is related to `T.
                    // A source type `S` is related to a target type `{ [P in Q]?: T }` if some constituent `Q'` of `Q` is related to `keyof S` and `S[Q']` is related to `T`.
                    // A source type `S` is related to a target type `{ [P in Q as R]?: T }` if some constituent `R'` of `R` is related to `keyof S` and `S[R']` is related to `T`.
                    if (includeOptional && (filteredByApplicability!._flags & TypeFlags.Never) == 0 || !includeOptional && IsRelatedTo(targetKeys, sourceKeys, RecursionFlags.Both, false) != Ternary.False)
                    {
                        // Go 在内层用 `templateType := r.c.getTemplateTypeFromMappedType(target)` 遮蔽外层同名局部变量；
                        // 两次调用返回 MappedType.templateType 的同一个缓存值（checker.go:23056），故直接复用外层变量。
                        var typeParameter = _c.GetTypeParameterFromMappedType(target);
                        // Fastpath: When the template type has the form `Obj[P]` where `P` is the mapped type parameter, directly compare source `S` with `Obj`
                        // to avoid creating the (potentially very large) number of new intermediate types made by manufacturing `S[P]`.
                        var nonNullComponent = _c.ExtractTypesOfKind(templateType, ~TypeFlags.Nullable);
                        if (!keysRemapped && (nonNullComponent._flags & TypeFlags.IndexedAccess) != 0 && nonNullComponent.AsIndexedAccessType()!._indexType == typeParameter)
                        {
                            result = IsRelatedTo(source, nonNullComponent.AsIndexedAccessType()!._objectType!, RecursionFlags.Target, reportErrors);
                            if (result != Ternary.False)
                            {
                                return result;
                            }
                        }
                        else
                        {
                            // We need to compare the type of a property on the source type `S` to the type of the same property on the target type,
                            // so we need to construct an indexing type representing a property, and then use indexing type to index the source type for comparison.
                            // If the target type has shape `{ [P in Q]: T }`, then a property of the target has type `P`.
                            // If the target type has shape `{ [P in Q]?: T }`, then a property of the target has type `P`,
                            // but the property is optional, so we only want to compare properties `P` that are common between `keyof S` and `Q`.
                            // If the target type has shape `{ [P in Q as R]: T }`, then a property of the target has type `R`.
                            // If the target type has shape `{ [P in Q as R]?: T }`, then a property of the target has type `R`,
                            // but the property is optional, so we only want to compare properties `R` that are common between `keyof S` and `R`.
                            var indexingType = typeParameter;
                            if (keysRemapped)
                            {
                                indexingType = Core.OrElse(filteredByApplicability, targetKeys)!;
                            }
                            else if (filteredByApplicability != null)
                            {
                                indexingType = _c.GetIntersectionType(new Type[] { filteredByApplicability, typeParameter });
                            }
                            var indexedAccessType = _c.GetIndexedAccessType(source, indexingType);
                            // Compare `S[indexingType]` to `T`, where `T` is the type of a property of the target type.
                            result = IsRelatedTo(indexedAccessType, templateType, RecursionFlags.Both, reportErrors);
                            if (result != Ternary.False)
                            {
                                return result;
                            }
                        }
                    }
                    originalErrorChain = _errorChain;
                    RestoreErrorState(saveErrorState);
                }
            }
        }

        if ((source._flags & TypeFlags.TypeVariable) != 0)
        {
            // IndexedAccess comparisons are handled above in the `target.flags&TypeFlagsIndexedAccess` branch
            if ((source._flags & TypeFlags.IndexedAccess) == 0 || (target._flags & TypeFlags.IndexedAccess) == 0)
            {
                var constraint = _c.GetConstraintOfType(source);
                if (constraint == null)
                {
                    constraint = _c._unknownType;
                }
                // hi-speed no-this-instantiation check (less accurate, but avoids costly `this`-instantiation when the constraint will suffice), see #28231 for report on why this is needed
                result = IsRelatedToEx(constraint, target, RecursionFlags.Source, false /*reportErrors*/, null /*headMessage*/, intersectionState);
                if (result != Ternary.False)
                {
                    return result;
                }
                var constraintWithThis = _c.GetTypeWithThisArgument(constraint, source, false /*needApparentType*/);
                result = IsRelatedToEx(constraintWithThis, target, RecursionFlags.Source, reportErrors && constraint != _c._unknownType && (target._flags & source._flags & TypeFlags.TypeParameter) == 0, null /*headMessage*/, intersectionState);
                if (result != Ternary.False)
                {
                    return result;
                }
                if (_c.IsMappedTypeGenericIndexedAccess(source))
                {
                    // For an indexed access type { [P in K]: E}[X], above we have already explored an instantiation of E with X
                    // substituted for P. We also want to explore type { [P in K]: E }[C], where C is the constraint of X.
                    var indexConstraint = _c.GetConstraintOfType(source.AsIndexedAccessType()!._indexType!);
                    if (indexConstraint != null)
                    {
                        result = IsRelatedTo(_c.GetIndexedAccessType(source.AsIndexedAccessType()!._objectType!, indexConstraint), target, RecursionFlags.Source, reportErrors);
                        if (result != Ternary.False)
                        {
                            return result;
                        }
                    }
                }
            }
        }
        else if ((source._flags & TypeFlags.Index) != 0)
        {
            var isDeferredMappedIndex = _c.ShouldDeferIndexType(source.AsIndexType()!._target!, source.AsIndexType()!._indexFlags) && (source.AsIndexType()!._target!._objectFlags & ObjectFlags.Mapped) != 0;
            result = IsRelatedTo(_c._stringNumberSymbolType, target, RecursionFlags.Source, reportErrors && !isDeferredMappedIndex);
            if (result != Ternary.False)
            {
                return result;
            }
            if (isDeferredMappedIndex)
            {
                var mappedType = source.AsIndexType()!._target!;
                var nameType = _c.GetNameTypeFromMappedType(mappedType);
                // Unlike on the target side, on the source side we do *not* include the generic part of the `nameType`, since that comes from a
                // (potentially anonymous) mapped type local type parameter, so that'd never assign outside the mapped type body, but we still want to
                // allow assignments of index types of identical (or similar enough) mapped types.
                // eg, `keyof {[X in keyof A]: Obj[X]}` should be assignable to `keyof {[Y in keyof A]: Tup[Y]}` because both map over the same set of keys (`keyof A`).
                // Without this source-side breakdown, a `keyof {[X in keyof A]: Obj[X]}` style type won't be assignable to anything except itself, which is much too strict.
                Type sourceMappedKeys;
                if (nameType != null && _c.IsMappedTypeWithKeyofConstraintDeclaration(mappedType))
                {
                    sourceMappedKeys = _c.GetApparentMappedTypeKeys(nameType, mappedType);
                }
                else if (nameType != null)
                {
                    sourceMappedKeys = nameType;
                }
                else
                {
                    sourceMappedKeys = _c.GetConstraintTypeFromMappedType(mappedType);
                }
                result = IsRelatedTo(sourceMappedKeys, target, RecursionFlags.Source, reportErrors);
                if (result != Ternary.False)
                {
                    return result;
                }
            }
        }
        else if ((source._flags & TypeFlags.Conditional) != 0)
        {
            // If we reach 10 levels of nesting for the same conditional type, assume it is an infinitely expanding recursive
            // conditional type and bail out with a Ternary.Maybe result.
            if (_c.IsDeeplyNestedType(source, _sourceStack, 10))
            {
                return Ternary.Maybe;
            }
            if ((target._flags & TypeFlags.Conditional) != 0)
            {
                // Two conditional types 'T1 extends U1 ? X1 : Y1' and 'T2 extends U2 ? X2 : Y2' are related if
                // one of T1 and T2 is related to the other, U1 and U2 are identical types, X1 is related to X2,
                // and Y1 is related to Y2.
                var sourceParams = source.AsConditionalType()!._root!._inferTypeParameters;
                var sourceExtends = source.AsConditionalType()!._root!._extendsType!;
                TypeMapper? mapper = null;
                if (sourceParams.Length != 0)
                {
                    // If the source has infer type parameters, we instantiate them in the context of the target
                    var ctx = _c.NewInferenceContext(sourceParams, null /*signature*/, InferenceFlags.None, _isRelatedToWorkerDelegate);
                    _c.InferTypes(ctx._inferences, target.AsConditionalType()!._root!._extendsType!, sourceExtends, InferencePriority.NoConstraints | InferencePriority.AlwaysStrict, false);
                    sourceExtends = _c.InstantiateType(sourceExtends, ctx._mapper);
                    mapper = ctx._mapper;
                }
                if (_c.IsTypeIdenticalTo(sourceExtends, target.AsConditionalType()!._root!._extendsType!) && (IsRelatedTo(source.AsConditionalType()!._root!._checkType!, target.AsConditionalType()!._root!._checkType!, RecursionFlags.Both, false) != Ternary.False || IsRelatedTo(target.AsConditionalType()!._root!._checkType!, source.AsConditionalType()!._root!._checkType!, RecursionFlags.Both, false) != Ternary.False))
                {
                    result = IsRelatedTo(_c.InstantiateType(_c.GetTrueTypeFromConditionalType(source), mapper), _c.GetTrueTypeFromConditionalType(target), RecursionFlags.Both, reportErrors);
                    if (result != Ternary.False)
                    {
                        result &= IsRelatedTo(_c.GetFalseTypeFromConditionalType(source), _c.GetFalseTypeFromConditionalType(target), RecursionFlags.Both, reportErrors);
                    }
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
            // conditionals can be related to one another via normal constraint, as, eg, `A extends B ? O : never` should be assignable to `O`
            // when `O` is a conditional (`never` is trivially assignable to `O`, as is `O`!).
            var defaultConstraint = _c.GetDefaultConstraintOfConditionalType(source);
            if (defaultConstraint != null)
            {
                result = IsRelatedTo(defaultConstraint, target, RecursionFlags.Source, reportErrors);
                if (result != Ternary.False)
                {
                    return result;
                }
            }
            // conditionals aren't related to one another via distributive constraint as it is much too inaccurate and allows way
            // more assignments than are desirable (since it maps the source check type to its constraint, it loses information).
            if ((target._flags & TypeFlags.Conditional) == 0 && _c.HasNonCircularBaseConstraint(source))
            {
                var distributiveConstraint = _c.GetConstraintOfDistributiveConditionalType(source);
                if (distributiveConstraint != null)
                {
                    RestoreErrorState(saveErrorState);
                    result = IsRelatedTo(distributiveConstraint, target, RecursionFlags.Source, reportErrors);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
        }
        else if ((source._flags & TypeFlags.TemplateLiteral) != 0 && (target._flags & TypeFlags.Object) == 0)
        {
            if ((target._flags & TypeFlags.TemplateLiteral) == 0)
            {
                var constraint = _c.GetBaseConstraintOfTypeWorker(source);
                if (constraint != null && constraint != source)
                {
                    result = IsRelatedTo(constraint, target, RecursionFlags.Source, reportErrors);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
        }
        else if ((source._flags & TypeFlags.StringMapping) != 0)
        {
            if ((target._flags & TypeFlags.StringMapping) != 0)
            {
                if (source.AsStringMappingType()!._symbol != target.AsStringMappingType()!._symbol)
                {
                    return Ternary.False;
                }
                result = IsRelatedTo(source.AsStringMappingType()!._target!, target.AsStringMappingType()!._target!, RecursionFlags.Both, reportErrors);
                if (result != Ternary.False)
                {
                    return result;
                }
            }
            else
            {
                var constraint = _c.GetBaseConstraintOfTypeWorker(source);
                if (constraint != null)
                {
                    result = IsRelatedTo(constraint, target, RecursionFlags.Source, reportErrors);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
            }
        }
        else
        {
            // An empty object type is related to any mapped type that includes a '?' modifier.
            if (_relation != _c._subtypeRelation && _relation != _c._strictSubtypeRelation && IsPartialMappedType(target) && _c.IsEmptyObjectType(source))
            {
                return Ternary.True;
            }
            if (_c.IsGenericMappedType(target))
            {
                if (_c.IsGenericMappedType(source))
                {
                    result = MappedTypeRelatedTo(source, target, reportErrors);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                }
                return Ternary.False;
            }
            var sourceIsPrimitive = (source._flags & TypeFlags.Primitive) != 0;
            if (_relation != _c._identityRelation)
            {
                source = _c.GetApparentTypeWorker(source);
            }
            else if (_c.IsGenericMappedType(source))
            {
                return Ternary.False;
            }
            if ((source._objectFlags & ObjectFlags.Reference) != 0 && (target._objectFlags & ObjectFlags.Reference) != 0 && source.Target() == target.Target() && !IsTupleType(source) && !_c.IsMarkerType(source) && !_c.IsMarkerType(target))
            {
                // When strictNullChecks is disabled, the element type of the empty array literal is undefinedWideningType,
                // and an empty array literal wouldn't be assignable to a `never[]` without this check.
                if (_c.IsEmptyArrayLiteralType(source))
                {
                    return Ternary.True;
                }
                // We have type references to the same generic type, and the type references are not marker
                // type references (which are intended by be compared structurally). Obtain the variance
                // information for the type parameters and relate the type arguments accordingly.
                var variances = _c.GetVariances(source.Target()!);
                // We return Ternary.Maybe for a recursive invocation of getVariances (signaled by emptyArray). This
                // effectively means we measure variance only from type parameter occurrences that aren't nested in
                // recursive instantiations of the generic type.
                if (variances.Length == 0)
                {
                    return Ternary.Unknown;
                }
                var (varianceResult, ok) = RelateVariances(_c.GetTypeArgumentsWorker(source), _c.GetTypeArgumentsWorker(target), variances, intersectionState);
                if (ok)
                {
                    return varianceResult;
                }
            }
            else if (_c.IsArrayTypeWorker(target) && (_c.IsReadonlyArrayType(target) && EveryType(source, t => _c.IsArrayOrTupleType(t)) || EveryType(source, t => IsMutableTupleType(t))))
            {
                if (_relation != _c._identityRelation)
                {
                    return IsRelatedTo(_c.GetIndexTypeOfTypeEx(source, _c._numberType, _c._anyType)!, _c.GetIndexTypeOfTypeEx(target, _c._numberType, _c._anyType)!, RecursionFlags.Both, reportErrors);
                }
                // By flags alone, we know that the `target` is a readonly array while the source is a normal array or tuple
                // or `target` is an array and source is a tuple - in both cases the types cannot be identical, by construction
                return Ternary.False;
            }
            else if (_c.IsGenericTupleType(source) && IsTupleType(target) && !_c.IsGenericTupleType(target))
            {
                var constraint = _c.GetBaseConstraintOrType(source);
                if (constraint != source)
                {
                    return IsRelatedTo(constraint, target, RecursionFlags.Source, reportErrors);
                }
            }
            else if ((_relation == _c._subtypeRelation || _relation == _c._strictSubtypeRelation) && _c.IsEmptyObjectType(target) && (target._objectFlags & ObjectFlags.FreshLiteral) != 0 && !_c.IsEmptyObjectType(source))
            {
                return Ternary.False;
            }
            // Even if relationship doesn't hold for unions, intersections, or generic type references,
            // it may hold in a structural comparison.
            // In a check of the form X = A & B, we will have previously checked if A relates to X or B relates
            // to X. Failing both of those we want to check if the aggregation of A and B's members structurally
            // relates to X. Thus, we include intersection types on the source side here.
            if ((source._flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && (target._flags & TypeFlags.Object) != 0)
            {
                // Report structural errors only if we haven't reported any errors yet
                var reportStructuralErrors = reportErrors && _errorChain == saveErrorState._errorChain && !sourceIsPrimitive;
                result = PropertiesRelatedTo(source, target, reportStructuralErrors, new Set<string>() /*excludedProperties*/, false /*optionalsOnly*/, intersectionState);
                if (result != Ternary.False)
                {
                    result &= SignaturesRelatedTo(source, target, SignatureKind.Call, reportStructuralErrors, intersectionState);
                    if (result != Ternary.False)
                    {
                        result &= SignaturesRelatedTo(source, target, SignatureKind.Construct, reportStructuralErrors, intersectionState);
                        if (result != Ternary.False)
                        {
                            result &= IndexSignaturesRelatedTo(source, target, sourceIsPrimitive, reportStructuralErrors, intersectionState);
                        }
                    }
                }
                if (result != Ternary.False)
                {
                    if (!varianceCheckFailed)
                    {
                        return result;
                    }
                    if (originalErrorChain != null)
                    {
                        _errorChain = originalErrorChain;
                    }
                    else if (_errorChain == null)
                    {
                        _errorChain = saveErrorState._errorChain;
                    }
                    // Use variance error (there is no structural one) and return false
                }
            }
            // If S is an object type and T is a discriminated union, S may be related to T if
            // there exists a constituent of T for every combination of the discriminants of S
            // with respect to T. We do not report errors here, as we will use the existing
            // error result from checking each constituent of the union.
            if ((source._flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && (target._flags & TypeFlags.Union) != 0)
            {
                var objectOnlyTarget = _c.ExtractTypesOfKind(target, TypeFlags.Object | TypeFlags.Intersection | TypeFlags.Substitution);
                if ((objectOnlyTarget._flags & TypeFlags.Union) != 0)
                {
                    // Go 此处 `result :=` 遮蔽外层 result（新局部变量）→ C# 不允许 → 改名 `res`。
                    var res = TypeRelatedToDiscriminatedType(source, objectOnlyTarget);
                    if (res != Ternary.False)
                    {
                        return res;
                    }
                }
            }
        }
        return Ternary.False;
    }

    // A type [P in S]: X is related to a type [Q in T]: Y if T is related to S and X' is
    // related to Y, where X' is an instantiation of X in which P is replaced with Q. Notice
    // that S and T are contra-variant whereas X and Y are co-variant.
    internal Ternary TypeArgumentsRelatedTo(Type[] sources, Type[] targets, VarianceFlags[]? variances, bool reportErrors, IntersectionState intersectionState)
    {
        if (sources.Length != targets.Length && _relation == _c._identityRelation)
        {
            return Ternary.False;
        }
        var length = Math.Min(sources.Length, targets.Length);
        var result = Ternary.True;
        for (var i = 0; i < length; i++)
        {
            // When variance information isn't available we default to covariance. This happens
            // in the process of computing variance information for recursive types and when
            // comparing 'this' type arguments.
            var varianceFlags = VarianceFlags.Covariant;
            if (variances != null && i < variances.Length)
            {
                varianceFlags = variances[i];
            }
            var variance = varianceFlags & VarianceFlags.VarianceMask;
            // We ignore arguments for independent type parameters (because they're never witnessed).
            if (variance != VarianceFlags.Independent)
            {
                var s = sources[i];
                var t = targets[i];
                Ternary related;
                if ((varianceFlags & VarianceFlags.Unmeasurable) != 0)
                {
                    // Even an `Unmeasurable` variance works out without a structural check if the source and target are _identical_.
                    // We can't simply assume invariance, because `Unmeasurable` marks nonlinear relations, for example, a relation tainted by
                    // the `-?` modifier in a mapped type (where, no matter how the inputs are related, the outputs still might not be)
                    if (_relation == _c._identityRelation)
                    {
                        related = IsRelatedTo(s, t, RecursionFlags.Both, false /*reportErrors*/);
                    }
                    else
                    {
                        related = _c.CompareTypesIdentical(s, t);
                    }
                }
                else
                {
                    // Propagate unreliable variance flag in variance computations
                    if (_c._varianceStack.Count != 0 && (varianceFlags & VarianceFlags.Unreliable) != 0)
                    {
                        _c.InstantiateType(s, _c._reportUnreliableMapper);
                    }
                    if (variance == VarianceFlags.Covariant)
                    {
                        related = IsRelatedToEx(s, t, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
                    }
                    else if (variance == VarianceFlags.Contravariant)
                    {
                        related = IsRelatedToEx(t, s, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
                    }
                    else if (variance == VarianceFlags.Bivariant)
                    {
                        // In the bivariant case we first compare contravariantly without reporting
                        // errors. Then, if that doesn't succeed, we compare covariantly with error
                        // reporting. Thus, error elaboration will be based on the covariant check,
                        // which is generally easier to reason about.
                        related = IsRelatedTo(t, s, RecursionFlags.Both, false /*reportErrors*/);
                        if (related == Ternary.False)
                        {
                            related = IsRelatedToEx(s, t, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
                        }
                    }
                    else
                    {
                        // In the invariant case we first compare covariantly, and only when that
                        // succeeds do we proceed to compare contravariantly. Thus, error elaboration
                        // will typically be based on the covariant check.
                        related = IsRelatedToEx(s, t, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
                        if (related != Ternary.False)
                        {
                            related &= IsRelatedToEx(t, s, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
                        }
                    }
                }
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
        }
        return result;
    }
}
