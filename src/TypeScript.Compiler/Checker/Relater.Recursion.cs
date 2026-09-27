// ported from: internal/checker/relater.go:2619-3211
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (r *Relater) x()` → `Relater` 类的实例方法（`isRelatedToSimple` … `resetMaybeStack`）；
//    `func (c *Checker) x()` → `Checker` 类的 internal 实例方法（`getTypeOfPropertyInTypes`/`getTypeOfPropertyInType`/
//    `isTypeSubsetOf`/`isTypeSubsetOfUnion`）；包级函数 `shouldCheckAsExcessProperty`/`isIgnoredJsxProperty` →
//    同一个 `Checker` 类的 internal static 成员（合同 §1.2）。`Relater` 里对 Checker 静态成员的裸名调用靠文件头的
//    `using static TypeScript.Checker.Checker;`（先例：Checker/Tracer.cs）。
// 2) `getRelationKey` 返回 `(CacheHashKey, bool)` → `Checker.TryGetRelationKey(..., out CacheHashKey key)` 返回
//    bool constrained（Checker/Checker.C5.CacheKeys.cs:474）；`id, constrained := getRelationKey(...)` →
//    `var constrained = TryGetRelationKey(..., out var id)`。
// 3) `append` + 切片截断 → `List<T>.Add`/`RemoveAt`/`RemoveRange`（Relater 的 maybeKeys/sourceStack/targetStack 是 List<T>）。
// 4) `r.relation.get(id)` 缺失键返回零值 → `Relation.Get`（内部 TryGetValue，见 Relater.Types.cs）。
// 5) 函数值：`core.IfElse(reportErrors, r.reportError, nil)` → `reportErrors ? _reportErrorDelegate : null`；
//    `r.isRelatedToSimple`（作值传递）→ `_isRelatedToSimpleDelegate`（合同 §4：缓存委托，热路径零额外分配）。
// 6) `slices.Contains(arr, x)` → `Array.IndexOf(arr, x) >= 0`；`core.SameMap`/`core.Same` → `SameMap`/`Same`（using static Core.Core）。
// 7) `map[string]any` → `Dictionary<string, object?>`；`tracing.PhaseCheckTypes` → `Phase.CheckTypes`（Tracing/Tracing.cs:267）。
// 8) `defer tr.Push(...)()` 只覆盖紧随其后的那一次 `structuredTypeRelatedTo` 调用 → 用局部 `popFn` 接住、在该调用之后
//    立即调用（函数只有这一个 defer，且它的作用域内只有一个调用，语义等价）；`if tr := r.c.tracer; tr != nil` →
//    `if (_c._tracer is { } tr)`。
// 9) `panic("No errorNode in hasExcessProperties")` → `Debug.Fail(...)`（`TypeScript.Debug`，抛 DebugFailureException；
//    本文件不用 `using System.Diagnostics;`）。
// 10) Go 的 `switch { case a: ... case b: ... }` → if/else 链（两个 case 条件互斥，语义等价）。
// 11) `checkTypes []*Type` / `strippedTypes []*Type`（Go 的 nil 切片）→ `Type[]?` + null 判断；`target.Types()` 在
//     C# 返回 `Type[]`（非 List），故 `len(x)` → `x.Length`。
// 12) `ast.IsJsxOpeningLikeElement(r.errorNode.Parent)` 的实参用 `!`：Go 的形参是 `*ast.Node`（非 nil），
//     C# 侧 `IsJsxOpeningLikeElement(Node node)` 非可空，而 `Node.Parent` 是 `Node?`。
// 13) `eachTypeRelatedToType`：Go 在 if 块内外各声明一个 `related`（Go 允许，C# 会 CS0136）→ if 块内那个改名
//     `relatedToCorresponding`，块外的仍叫 `related`。
// 14) `source.AsLiteralType().regularType/freshType` 在 C# 是 `Type?` → 局部 `alternateForm` 声明为 `Type?`，
//     并逐字保留 Go 的 `!= nil` 判定。
// 15) `shouldCheckAsExcessProperty(prop, source.symbol)` 的 `container` 形参在 C# 写成非可空 `Symbol`
//     （调用点传 `source._symbol!`）：与 Go 一致，container 为 nil 且 prop.ValueDeclaration != nil 时 Go panic、C# NRE。
// 16) `getTypeOfPropertyInTypes`/`getTypeOfPropertyInType` 的返回类型按本片任务书写成 `Type?`（Go 的 `*Type` 可空），
//     因此 `GetTypeOfPropertyInTypes(...)` 的调用点补 `!`。
namespace TypeScript.Checker;

// Go 的 `Diagnostics`/`Ast`/`Core`/`Phase` 这些裸名必须靠「usings 放在 file-scoped namespace **之后**」才能解析到
// **类/静态类**而不是外层命名空间（TypeScript.Diagnostics / TypeScript.Ast / …）：导入名会成为本命名空间声明空间的成员，
// 优先于外层命名空间里的同名命名空间成员。Checker 包内已落地的文件（如 Checker.C2.IndexConstraints.cs）都是这个布局。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Checker.Checker;
using static TypeScript.Core.Core;

/// <summary>
/// Go: <c>type Relater struct</c>（relater.go:2580）中本分片负责的方法（relater.go:2619-3211）。
/// 本类型是独立类（接收者 `r *Relater`），由 Relater.Types.cs / Relater.Report.cs / Relater.Recursion.cs 等
/// 多个 partial 文件拼成。
/// </summary>
public sealed partial class Relater
{
    /// <summary>Go: <c>func (r *Relater) isRelatedToSimple(source *Type, target *Type) Ternary</c>（relater.go:2621）。</summary>
    internal Ternary IsRelatedToSimple(Type source, Type target)
    {
        return IsRelatedToEx(source, target, RecursionFlags.Both, false /*reportErrors*/, null /*headMessage*/, IntersectionState.None);
    }

    /// <summary>Go: <c>func (r *Relater) isRelatedToWorker(source *Type, target *Type, reportErrors bool) Ternary</c>（relater.go:2625）。</summary>
    internal Ternary IsRelatedToWorker(Type source, Type target, bool reportErrors)
    {
        return IsRelatedToEx(source, target, RecursionFlags.Both, reportErrors, null, IntersectionState.None);
    }

    /// <summary>Go: <c>func (r *Relater) isRelatedTo(source *Type, target *Type, recursionFlags RecursionFlags, reportErrors bool) Ternary</c>（relater.go:2629）。</summary>
    internal Ternary IsRelatedTo(Type source, Type target, RecursionFlags recursionFlags, bool reportErrors)
    {
        return IsRelatedToEx(source, target, recursionFlags, reportErrors, null, IntersectionState.None);
    }

    /// <summary>Go: <c>func (r *Relater) isRelatedToEx(originalSource, originalTarget *Type, recursionFlags RecursionFlags, reportErrors bool, headMessage *diagnostics.Message, intersectionState IntersectionState) Ternary</c>（relater.go:2633）。</summary>
    internal Ternary IsRelatedToEx(Type originalSource, Type originalTarget, RecursionFlags recursionFlags, bool reportErrors, Message? headMessage, IntersectionState intersectionState)
    {
        if (originalSource == originalTarget)
        {
            return Ternary.True;
        }
        // Before normalization: if `source` is type an object type, and `target` is primitive,
        // skip all the checks we don't need and just return `isSimpleTypeRelatedTo` result
        if ((originalSource._flags & TypeFlags.Object) != 0 && (originalTarget._flags & TypeFlags.Primitive) != 0)
        {
            if (_relation == _c._comparableRelation && (originalTarget._flags & TypeFlags.Never) == 0 && _c.IsSimpleTypeRelatedTo(originalTarget, originalSource, _relation, null) ||
                _c.IsSimpleTypeRelatedTo(originalSource, originalTarget, _relation, reportErrors ? _reportErrorDelegate : null))
            {
                return Ternary.True;
            }
            if (reportErrors)
            {
                ReportErrorResults(originalSource, originalTarget, originalSource, originalTarget, headMessage);
            }
            return Ternary.False;
        }
        // Normalize the source and target types: Turn fresh literal types into regular literal types,
        // turn deferred type references into regular type references, simplify indexed access and
        // conditional types, and resolve substitution types to either the substitution (on the source
        // side) or the type variable (on the target side).
        var source = _c.GetNormalizedType(originalSource, false /*writing*/);
        var target = _c.GetNormalizedType(originalTarget, true /*writing*/);
        if (source == target)
        {
            return Ternary.True;
        }
        if (_relation == _c._identityRelation)
        {
            if (source._flags != target._flags)
            {
                return Ternary.False;
            }
            if ((source._flags & TypeFlags.Singleton) != 0)
            {
                return Ternary.True;
            }
            TraceUnionsOrIntersectionsTooLarge(source, target);
            return RecursiveTypeRelatedTo(source, target, false /*reportErrors*/, IntersectionState.None, recursionFlags);
        }
        // We fastpath comparing a type parameter to exactly its constraint, as this is _super_ common,
        // and otherwise, for type parameters in large unions, causes us to need to compare the union to itself,
        // as we break down the _target_ union first, _then_ get the source constraint - so for every
        // member of the target, we attempt to find a match in the source. This avoids that in cases where
        // the target is exactly the constraint.
        if ((source._flags & TypeFlags.TypeParameter) != 0 && _c.GetConstraintOfType(source) == target)
        {
            return Ternary.True;
        }
        // See if we're relating a definitely non-nullable type to a union that includes null and/or undefined
        // plus a single non-nullable type. If so, remove null and/or undefined from the target type.
        if ((source._flags & TypeFlags.DefinitelyNonNullable) != 0 && (target._flags & TypeFlags.Union) != 0)
        {
            var types = target.Types();
            Type? candidate = null;
            if (types.Length == 2 && (types[0]._flags & TypeFlags.Nullable) != 0)
            {
                candidate = types[1];
            }
            else if (types.Length == 3 && (types[0]._flags & TypeFlags.Nullable) != 0 && (types[1]._flags & TypeFlags.Nullable) != 0)
            {
                candidate = types[2];
            }
            if (candidate != null && (candidate._flags & TypeFlags.Nullable) == 0)
            {
                target = _c.GetNormalizedType(candidate /*writing*/, true);
                if (source == target)
                {
                    return Ternary.True;
                }
            }
        }
        if (_relation == _c._comparableRelation && (target._flags & TypeFlags.Never) == 0 && _c.IsSimpleTypeRelatedTo(target, source, _relation, null) ||
            _c.IsSimpleTypeRelatedTo(source, target, _relation, reportErrors ? _reportErrorDelegate : null))
        {
            return Ternary.True;
        }
        if ((source._flags & TypeFlags.StructuredOrInstantiable) != 0 || (target._flags & TypeFlags.StructuredOrInstantiable) != 0)
        {
            var isPerformingExcessPropertyChecks = (intersectionState & IntersectionState.Target) == 0 && IsObjectLiteralType(source) && (source._objectFlags & ObjectFlags.FreshLiteral) != 0;
            if (isPerformingExcessPropertyChecks)
            {
                if (HasExcessProperties(source, target, reportErrors))
                {
                    if (reportErrors)
                    {
                        ReportRelationError(headMessage, source, originalTarget._alias != null ? originalTarget : target);
                    }
                    return Ternary.False;
                }
            }
            var isPerformingCommonPropertyChecks = (_relation != _c._comparableRelation || IsUnitType(source)) &&
                (intersectionState & IntersectionState.Target) == 0 &&
                (source._flags & (TypeFlags.Primitive | TypeFlags.Object | TypeFlags.Intersection)) != 0 && source != _c._globalObjectType &&
                (target._flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0 && _c.IsWeakType(target) && ((_c.GetPropertiesOfTypeWorker(source)?.Length ?? 0) > 0 || _c.TypeHasCallOrConstructSignaturesWorker(source));
            var isComparingJsxAttributes = (source._objectFlags & ObjectFlags.JsxAttributes) != 0;
            if (isPerformingCommonPropertyChecks && !_c.HasCommonProperties(source, target, isComparingJsxAttributes))
            {
                if (reportErrors)
                {
                    var sourceString = _c.TypeToString(originalSource._alias != null ? originalSource : source);
                    var targetString = _c.TypeToString(originalTarget._alias != null ? originalTarget : target);
                    var calls = _c.GetSignaturesOfTypeWorker(source, SignatureKind.Call);
                    var constructs = _c.GetSignaturesOfTypeWorker(source, SignatureKind.Construct);
                    if (calls?.Length > 0 && IsRelatedTo(_c.GetReturnTypeOfSignatureWorker(calls![0]), target, RecursionFlags.Source, false /*reportErrors*/) != Ternary.False ||
                        constructs?.Length > 0 && IsRelatedTo(_c.GetReturnTypeOfSignatureWorker(constructs![0]), target, RecursionFlags.Source, false /*reportErrors*/) != Ternary.False)
                    {
                        ReportError(Diagnostics.Value_of_type_0_has_no_properties_in_common_with_type_1_Did_you_mean_to_call_it, sourceString, targetString);
                    }
                    else
                    {
                        ReportError(Diagnostics.Type_0_has_no_properties_in_common_with_type_1, sourceString, targetString);
                    }
                }
                return Ternary.False;
            }
            TraceUnionsOrIntersectionsTooLarge(source, target);
            var skipCaching = (source._flags & TypeFlags.Union) != 0 && source.Types().Length < 4 && (target._flags & TypeFlags.Union) == 0 ||
                (target._flags & TypeFlags.Union) != 0 && target.Types().Length < 4 && (source._flags & TypeFlags.StructuredOrInstantiable) == 0;
            Ternary result;
            if (skipCaching)
            {
                result = UnionOrIntersectionRelatedTo(source, target, reportErrors, intersectionState);
            }
            else
            {
                result = RecursiveTypeRelatedTo(source, target, reportErrors, intersectionState, recursionFlags);
            }
            if (result != Ternary.False)
            {
                return result;
            }
        }
        if (reportErrors)
        {
            ReportErrorResults(originalSource, originalTarget, source, target, headMessage);
        }
        return Ternary.False;
    }

    /// <summary>Go: <c>func (r *Relater) hasExcessProperties(source *Type, target *Type, reportErrors bool) bool</c>（relater.go:2747）。</summary>
    internal bool HasExcessProperties(Type source, Type target, bool reportErrors)
    {
        if (!IsExcessPropertyCheckTarget(target) || !_c._noImplicitAny && (target._objectFlags & ObjectFlags.JSLiteral) != 0)
        {
            // Disable excess property checks on JS literals to simulate having an implicit "index signature" - but only outside of noImplicitAny
            return false;
        }
        var isComparingJsxAttributes = (source._objectFlags & ObjectFlags.JsxAttributes) != 0;
        if ((_relation == _c._assignableRelation || _relation == _c._comparableRelation) && (_c.IsTypeSubsetOf(_c._globalObjectType, target) || (!isComparingJsxAttributes && _c.IsEmptyObjectType(target))))
        {
            return false;
        }
        Type? reducedTarget = target;
        Type[]? checkTypes = null;
        if ((target._flags & TypeFlags.Union) != 0)
        {
            reducedTarget = _c.FindMatchingDiscriminantType(source, target, _isRelatedToSimpleDelegate);
            if (reducedTarget == null)
            {
                reducedTarget = _c.FilterPrimitivesIfContainsNonPrimitive(target);
            }
            checkTypes = reducedTarget.Distributed();
        }
        foreach (var prop in _c.GetPropertiesOfTypeWorker(source) ?? [])
        {
            if (ShouldCheckAsExcessProperty(prop, source._symbol!) && !IsIgnoredJsxProperty(source, prop))
            {
                if (!_c.IsKnownProperty(reducedTarget!, prop.Name, isComparingJsxAttributes))
                {
                    if (reportErrors)
                    {
                        // Report error in terms of object types in the target as those are the only ones
                        // we check in isKnownProperty.
                        var errorTarget = _c.FilterType(reducedTarget!, IsExcessPropertyCheckTarget);
                        // We know *exactly* where things went wrong when comparing the types.
                        // Use this property as the error node as this will be more helpful in
                        // reasoning about what went wrong.
                        if (_errorNode == null)
                        {
                            Debug.Fail("No errorNode in hasExcessProperties");
                        }
                        if (IsJsxAttributes(_errorNode!) || IsJsxOpeningLikeElement(_errorNode!) || IsJsxOpeningLikeElement(_errorNode!.Parent!))
                        {
                            // JsxAttributes has an object-literal flag and undergo same type-assignablity check as normal object-literal.
                            // However, using an object-literal error message will be very confusing to the users so we give different a message.
                            if (prop.ValueDeclaration != null && IsJsxAttribute(prop.ValueDeclaration) && GetSourceFileOfNode(_errorNode!) == GetSourceFileOfNode(prop.ValueDeclaration.Name()))
                            {
                                // Note that extraneous children (as in `<NoChild>extra</NoChild>`) don't pass this check,
                                // since `children` is a Kind.PropertySignature instead of a Kind.JsxAttribute.
                                _errorNode = prop.ValueDeclaration.Name();
                            }
                            var propName = _c.SymbolToStringWorker(prop);
                            var suggestionSymbol = _c.GetSuggestedSymbolForNonexistentJSXAttribute(propName, errorTarget);
                            if (suggestionSymbol != null)
                            {
                                ReportError(Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2, propName, _c.TypeToString(errorTarget), _c.SymbolToStringWorker(suggestionSymbol));
                            }
                            else
                            {
                                ReportError(Diagnostics.Property_0_does_not_exist_on_type_1, propName, _c.TypeToString(errorTarget));
                            }
                        }
                        else
                        {
                            // use the property's value declaration if the property is assigned inside the literal itself
                            Node? objectLiteralDeclaration = null;
                            if (source._symbol != null)
                            {
                                objectLiteralDeclaration = FirstOrNil(source._symbol.Declarations);
                            }
                            var suggestion = "";
                            if (prop.ValueDeclaration != null && IsObjectLiteralElement(prop.ValueDeclaration) &&
                                FindAncestor(prop.ValueDeclaration, d => d == objectLiteralDeclaration) != null &&
                                GetSourceFileOfNode(objectLiteralDeclaration) == GetSourceFileOfNode(_errorNode))
                            {
                                var name = prop.ValueDeclaration.Name();
                                _errorNode = name;
                                if (IsIdentifier(name!))
                                {
                                    suggestion = _c.GetSuggestionForNonexistentProperty(name!.Text(), errorTarget);
                                }
                            }
                            if (suggestion != "")
                            {
                                ReportError(Diagnostics.Object_literal_may_only_specify_known_properties_but_0_does_not_exist_in_type_1_Did_you_mean_to_write_2, _c.SymbolToStringWorker(prop), _c.TypeToString(errorTarget), suggestion);
                            }
                            else
                            {
                                ReportError(Diagnostics.Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1, _c.SymbolToStringWorker(prop), _c.TypeToString(errorTarget));
                            }
                        }
                    }
                    return true;
                }
                if (checkTypes != null && IsRelatedTo(_c.GetTypeOfSymbolWorker(prop), _c.GetTypeOfPropertyInTypes(checkTypes, prop.Name)!, RecursionFlags.Both, reportErrors) == Ternary.False)
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Types_of_property_0_are_incompatible, _c.SymbolToStringWorker(prop));
                    }
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Go: <c>func (r *Relater) unionOrIntersectionRelatedTo(source, target *Type, reportErrors bool, intersectionState IntersectionState) Ternary</c>（relater.go:2882）。</summary>
    internal Ternary UnionOrIntersectionRelatedTo(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
    {
        // Note that these checks are specifically ordered to produce correct results. In particular,
        // we need to deconstruct unions before intersections (because unions are always at the top),
        // and we need to handle "each" relations before "some" relations for the same kind of type.
        if ((source._flags & TypeFlags.Union) != 0)
        {
            if ((target._flags & TypeFlags.Union) != 0)
            {
                // Intersections of union types are normalized into unions of intersection types, and such normalized
                // unions can get very large and expensive to relate. The following fast path checks if the source union
                // originated in an intersection. If so, and if that intersection contains the target type, then we know
                // the result to be true (for any two types A and B, A & B is related to both A and B).
                var sourceOrigin = source.AsUnionType()!._origin;
                if (sourceOrigin != null && (sourceOrigin._flags & TypeFlags.Intersection) != 0 && target._alias != null && Array.IndexOf(sourceOrigin.Types(), target) >= 0)
                {
                    return Ternary.True;
                }
                // Similarly, in unions of unions the we preserve the original list of unions. This original list is often
                // much shorter than the normalized result, so we scan it in the following fast path.
                var targetOrigin = target.AsUnionType()!._origin;
                if (targetOrigin != null && (targetOrigin._flags & TypeFlags.Union) != 0 && source._alias != null && Array.IndexOf(targetOrigin.Types(), source) >= 0)
                {
                    return Ternary.True;
                }
            }
            if (_relation == _c._comparableRelation)
            {
                return SomeTypeRelatedToType(source, target, reportErrors && (source._flags & TypeFlags.Primitive) == 0, intersectionState);
            }
            return EachTypeRelatedToType(source, target, reportErrors && (source._flags & TypeFlags.Primitive) == 0, intersectionState);
        }
        if ((target._flags & TypeFlags.Union) != 0)
        {
            return TypeRelatedToSomeType(_c.GetRegularTypeOfObjectLiteral(source), target, reportErrors && (source._flags & TypeFlags.Primitive) == 0 && (target._flags & TypeFlags.Primitive) == 0, intersectionState);
        }
        if ((target._flags & TypeFlags.Intersection) != 0)
        {
            return TypeRelatedToEachType(source, target, reportErrors, IntersectionState.Target);
        }
        // Source is an intersection. For the comparable relation, if the target is a primitive type we hoist the
        // constraints of all non-primitive types in the source into a new intersection. We do this because the
        // intersection may further constrain the constraints of the non-primitive types. For example, given a type
        // parameter 'T extends 1 | 2', the intersection 'T & 1' should be reduced to '1' such that it doesn't
        // appear to be comparable to '2'.
        if (_relation == _c._comparableRelation && (target._flags & TypeFlags.Primitive) != 0)
        {
            var constraints = SameMap(source.Types(), t =>
            {
                if ((t._flags & TypeFlags.Instantiable) != 0)
                {
                    var constraint = _c.GetBaseConstraintOfTypeWorker(t);
                    if (constraint != null)
                    {
                        return constraint;
                    }
                    return _c._unknownType;
                }
                return t;
            });
            if (!Same(constraints, source.Types()))
            {
                source = _c.GetIntersectionType(constraints);
                if ((source._flags & TypeFlags.Never) != 0)
                {
                    return Ternary.False;
                }
                if ((source._flags & TypeFlags.Intersection) == 0)
                {
                    var result = IsRelatedTo(source, target, RecursionFlags.Source, false /*reportErrors*/);
                    if (result != Ternary.False)
                    {
                        return result;
                    }
                    return IsRelatedTo(target, source, RecursionFlags.Source, false /*reportErrors*/);
                }
            }
        }
        // Check to see if any constituents of the intersection are immediately related to the target.
        // Don't report errors though. Elaborating on whether a source constituent is related to the target is
        // not actually useful and leads to some confusing error messages. Instead, we rely on the caller
        // checking whether the full intersection viewed as an object is related to the target.
        return SomeTypeRelatedToType(source, target, false /*reportErrors*/, IntersectionState.Source);
    }

    /// <summary>Go: <c>func (r *Relater) someTypeRelatedToType(source, target *Type, reportErrors bool, intersectionState IntersectionState) Ternary</c>（relater.go:2951）。</summary>
    internal Ternary SomeTypeRelatedToType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
    {
        var sourceTypes = source.Types();
        if ((source._flags & TypeFlags.Union) != 0 && ContainsType(sourceTypes, target))
        {
            return Ternary.True;
        }
        for (var i = 0; i < sourceTypes.Length; i++)
        {
            var t = sourceTypes[i];
            var related = IsRelatedToEx(t, target, RecursionFlags.Source, reportErrors && i == sourceTypes.Length - 1, null /*headMessage*/, intersectionState);
            if (related != Ternary.False)
            {
                return related;
            }
        }
        return Ternary.False;
    }

    /// <summary>Go: <c>func (r *Relater) eachTypeRelatedToType(source, target *Type, reportErrors bool, intersectionState IntersectionState) Ternary</c>（relater.go:2965）。</summary>
    internal Ternary EachTypeRelatedToType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
    {
        var result = Ternary.True;
        var sourceTypes = source.Types();
        // We strip `undefined` from the target if the `source` trivially doesn't contain it for our correspondence-checking fastpath
        // since `undefined` is frequently added by optionality and would otherwise spoil a potentially useful correspondence
        var strippedTarget = GetUndefinedStrippedTargetIfNeeded(source, target);
        Type[]? strippedTypes = null;
        if ((strippedTarget._flags & TypeFlags.Union) != 0)
        {
            strippedTypes = strippedTarget.Types();
        }
        for (var i = 0; i < sourceTypes.Length; i++)
        {
            var sourceType = sourceTypes[i];
            if ((strippedTarget._flags & TypeFlags.Union) != 0 && sourceTypes.Length >= strippedTypes!.Length && sourceTypes.Length % strippedTypes.Length == 0)
            {
                // many unions are mappings of one another; in such cases, simply comparing members at the same index can shortcut the comparison
                // such unions will have identical lengths, and their corresponding elements will match up. Another common scenario is where a large
                // union has a union of objects intersected with it. In such cases, if the input was, eg `("a" | "b" | "c") & (string | boolean | {} | {whatever})`,
                // the result will have the structure `"a" | "b" | "c" | "a" & {} | "b" & {} | "c" & {} | "a" & {whatever} | "b" & {whatever} | "c" & {whatever}`
                // - the resulting union has a length which is a multiple of the original union, and the elements correspond modulo the length of the original union
                var relatedToCorresponding = IsRelatedToEx(sourceType, strippedTypes[i % strippedTypes.Length], RecursionFlags.Both, false /*reportErrors*/, null /*headMessage*/, intersectionState);
                if (relatedToCorresponding != Ternary.False)
                {
                    result &= relatedToCorresponding;
                    continue;
                }
            }
            var related = IsRelatedToEx(sourceType, target, RecursionFlags.Source, reportErrors, null /*headMessage*/, intersectionState);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        return result;
    }

    /// <summary>Go: <c>func (r *Relater) getUndefinedStrippedTargetIfNeeded(source *Type, target *Type) *Type</c>（relater.go:2997）。</summary>
    internal Type GetUndefinedStrippedTargetIfNeeded(Type source, Type target)
    {
        if ((source._flags & TypeFlags.Union) != 0 && (target._flags & TypeFlags.Union) != 0 && (source.Types()[0]._flags & TypeFlags.Undefined) == 0 && (target.Types()[0]._flags & TypeFlags.Undefined) != 0)
        {
            return _c.ExtractTypesOfKind(target, ~TypeFlags.Undefined);
        }
        return target;
    }

    /// <summary>Go: <c>func (r *Relater) typeRelatedToSomeType(source, target *Type, reportErrors bool, intersectionState IntersectionState) Ternary</c>（relater.go:3004）。</summary>
    internal Ternary TypeRelatedToSomeType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
    {
        var targetTypes = target.Types();
        if ((target._flags & TypeFlags.Union) != 0)
        {
            if (ContainsType(targetTypes, source))
            {
                return Ternary.True;
            }
            if (_relation != _c._comparableRelation && (target._objectFlags & ObjectFlags.PrimitiveUnion) != 0 && (source._flags & TypeFlags.EnumLiteral) == 0 &&
                ((source._flags & (TypeFlags.StringLiteral | TypeFlags.BooleanLiteral | TypeFlags.BigIntLiteral)) != 0 ||
                    (_relation == _c._subtypeRelation || _relation == _c._strictSubtypeRelation) && (source._flags & TypeFlags.NumberLiteral) != 0))
            {
                // When relating a literal type to a union of primitive types, we know the relation is false unless
                // the union contains the base primitive type or the literal type in one of its fresh/regular forms.
                // We exclude numeric literals for non-subtype relations because numeric literals are assignable to
                // numeric enum literals with the same value. Similarly, we exclude enum literal types because
                // identically named enum types are related (see isEnumTypeRelatedTo). We exclude the comparable
                // relation in entirety because it needs to be checked in both directions.
                Type? alternateForm;
                if (source == source.AsLiteralType()!._regularType)
                {
                    alternateForm = source.AsLiteralType()!._freshType;
                }
                else
                {
                    alternateForm = source.AsLiteralType()!._regularType;
                }
                Type? primitive = null;
                if ((source._flags & TypeFlags.StringLiteral) != 0)
                {
                    primitive = _c._stringType;
                }
                else if ((source._flags & TypeFlags.NumberLiteral) != 0)
                {
                    primitive = _c._numberType;
                }
                else if ((source._flags & TypeFlags.BigIntLiteral) != 0)
                {
                    primitive = _c._bigintType;
                }
                if (primitive != null && ContainsType(targetTypes, primitive) || alternateForm != null && ContainsType(targetTypes, alternateForm))
                {
                    return Ternary.True;
                }
                return Ternary.False;
            }
            var match = _c.GetMatchingUnionConstituentForType(target, source);
            if (match != null)
            {
                var related = IsRelatedToEx(source, match, RecursionFlags.Target, false /*reportErrors*/, null /*headMessage*/, intersectionState);
                if (related != Ternary.False)
                {
                    return related;
                }
            }
        }
        foreach (var t in targetTypes)
        {
            var related = IsRelatedToEx(source, t, RecursionFlags.Target, false /*reportErrors*/, null /*headMessage*/, intersectionState);
            if (related != Ternary.False)
            {
                return related;
            }
        }
        if (reportErrors)
        {
            // Elaborate only if we can find a best matching type in the target union
            var bestMatchingType = _c.GetBestMatchingType(source, target, _isRelatedToSimpleDelegate);
            if (bestMatchingType != null)
            {
                IsRelatedToEx(source, bestMatchingType, RecursionFlags.Target, true /*reportErrors*/, null /*headMessage*/, intersectionState);
            }
        }
        return Ternary.False;
    }

    /// <summary>Go: <c>func (r *Relater) typeRelatedToEachType(source, target *Type, reportErrors bool, intersectionState IntersectionState) Ternary</c>（relater.go:3063）。</summary>
    internal Ternary TypeRelatedToEachType(Type source, Type target, bool reportErrors, IntersectionState intersectionState)
    {
        var result = Ternary.True;
        var targetTypes = target.Types();
        foreach (var targetType in targetTypes)
        {
            var related = IsRelatedToEx(source, targetType, RecursionFlags.Target, reportErrors /*headMessage*/, null, intersectionState);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        return result;
    }

    /// <summary>Go: <c>func (r *Relater) eachTypeRelatedToSomeType(source *Type, target *Type) Ternary</c>（relater.go:3076）。</summary>
    internal Ternary EachTypeRelatedToSomeType(Type source, Type target)
    {
        var result = Ternary.True;
        var sourceTypes = source.Types();
        foreach (var sourceType in sourceTypes)
        {
            var related = TypeRelatedToSomeType(sourceType, target, false /*reportErrors*/, IntersectionState.None);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        return result;
    }

    /// <summary>
    /// Go: <c>func (r *Relater) recursiveTypeRelatedTo(source, target *Type, reportErrors bool, intersectionState IntersectionState, recursionFlags RecursionFlags) Ternary</c>
    /// （relater.go:3094）。Determine if possibly recursive types are related. First, check if the result is already
    /// available in the global cache. Second, check if we have already started a comparison of the given two types in
    /// which case we assume the result to be true. Third, check if both types are part of deeply nested chains of
    /// generic type instantiations and if so assume the types are equal and infinitely expanding. Fourth, if we have
    /// reached a depth of 100 nested comparisons, assume we have runaway recursion and issue an error. Otherwise,
    /// actually compare the structure of the two types.
    /// </summary>
    internal Ternary RecursiveTypeRelatedTo(Type source, Type target, bool reportErrors, IntersectionState intersectionState, RecursionFlags recursionFlags)
    {
        if (_overflow)
        {
            // Note that stack depth overflows can cause _any_ relation involving structured types to become false, so it is
            // important to have well-defined behavior even in cases that shouldn't normally occur.
            return Ternary.False;
        }
        var constrained = TryGetRelationKey(source, target, intersectionState, _relation == _c._identityRelation, false /*ignoreConstraints*/, out var id);
        var entry = _relation.Get(id);
        if (entry != RelationComparisonResult.None)
        {
            if (reportErrors && (entry & RelationComparisonResult.Failed) != 0 && (entry & RelationComparisonResult.Overflow) == 0)
            {
                // We are elaborating errors and the cached result is a failure not due to a comparison overflow,
                // so we will do the comparison again to generate an error message.
            }
            else
            {
                _c._reliabilityFlags |= entry & (RelationComparisonResult.ReportsUnmeasurable | RelationComparisonResult.ReportsUnreliable);
                if (reportErrors && (entry & RelationComparisonResult.Overflow) != 0)
                {
                    ReportError(Diagnostics.Excessive_complexity_comparing_types_0_and_1, _c.TypeToString(source), _c.TypeToString(target));
                }
                if ((entry & RelationComparisonResult.Succeeded) != 0)
                {
                    return Ternary.True;
                }
                return Ternary.False;
            }
        }
        if (_relationCount <= 0)
        {
            _overflow = true;
            return Ternary.False;
        }
        // If source and target are already being compared, consider them related with assumptions
        if (_maybeKeysSet.Has(id))
        {
            return Ternary.Maybe;
        }
        // A constrained key indicates that we have type references that reference constrained
        // type parameters. For such keys we also check against the key we would have gotten if all type parameters
        // were unconstrained.
        if (constrained)
        {
            TryGetRelationKey(source, target, intersectionState, _relation == _c._identityRelation, true /*ignoreConstraints*/, out var broadestEquivalentId);
            if (_maybeKeysSet.Has(broadestEquivalentId))
            {
                return Ternary.Maybe;
            }
        }
        if (_sourceStack.Count == 100 || _targetStack.Count == 100)
        {
            // We stop relating if we reach 100 levels of nesting. This is a backstop to catch infinite recursion
            // that wasn't caught by isDeeplyNestedType. It will also stop relating types that truly are over 100
            // levels deep, but those are exceedingly rare.
            return Ternary.Maybe;
        }
        var maybeStart = _maybeKeys.Count;
        _maybeKeys.Add(id);
        _maybeKeysSet.Add(id);
        var saveExpandingFlags = _expandingFlags;
        if ((recursionFlags & RecursionFlags.Source) != 0)
        {
            _sourceStack.Add(source);
            if ((_expandingFlags & ExpandingFlags.Source) == 0 && _c.IsDeeplyNestedType(source, _sourceStack, 3))
            {
                _expandingFlags |= ExpandingFlags.Source;
            }
        }
        if ((recursionFlags & RecursionFlags.Target) != 0)
        {
            _targetStack.Add(target);
            if ((_expandingFlags & ExpandingFlags.Target) == 0 && _c.IsDeeplyNestedType(target, _targetStack, 3))
            {
                _expandingFlags |= ExpandingFlags.Target;
            }
        }
        var saveReliabilityFlags = _c._reliabilityFlags;
        _c._reliabilityFlags = RelationComparisonResult.None;
        Ternary result;
        if (_expandingFlags == ExpandingFlags.Both)
        {
            if (_c._tracer is { } tr)
            {
                tr.Instant(Phase.CheckTypes, "recursiveTypeRelatedTo_DepthLimit", new Dictionary<string, object?>
                {
                    ["sourceId"] = source._id.Value,
                    ["targetId"] = target._id.Value,
                    ["depth"] = _sourceStack.Count,
                    ["targetDepth"] = _targetStack.Count,
                });
            }
            result = Ternary.Maybe;
        }
        else
        {
            Action? popFn = null;
            if (_c._tracer is { } tr)
            {
                popFn = tr.Push(Phase.CheckTypes, "structuredTypeRelatedTo", new Dictionary<string, object?>
                {
                    ["sourceId"] = source._id.Value,
                    ["targetId"] = target._id.Value,
                }, false);
            }
            result = StructuredTypeRelatedTo(source, target, reportErrors, intersectionState);
            // Go: defer tr.Push(...)()——该 defer 只覆盖上面这一次 structuredTypeRelatedTo 调用。
            popFn?.Invoke();
        }
        var propagatingVarianceFlags = _c._reliabilityFlags;
        _c._reliabilityFlags |= saveReliabilityFlags;
        if ((recursionFlags & RecursionFlags.Source) != 0)
        {
            _sourceStack.RemoveAt(_sourceStack.Count - 1);
        }
        if ((recursionFlags & RecursionFlags.Target) != 0)
        {
            _targetStack.RemoveAt(_targetStack.Count - 1);
        }
        _expandingFlags = saveExpandingFlags;
        if (result != Ternary.False)
        {
            if (result == Ternary.True || (_sourceStack.Count == 0 && _targetStack.Count == 0))
            {
                if (result == Ternary.True || result == Ternary.Maybe)
                {
                    // If result is definitely true, record all maybe keys as having succeeded. Also, record Ternary.Maybe
                    // results as having succeeded once we reach depth 0, but never record Ternary.Unknown results.
                    ResetMaybeStack(maybeStart, propagatingVarianceFlags, true);
                }
                else
                {
                    ResetMaybeStack(maybeStart, propagatingVarianceFlags, false);
                }
            }
            // Note: it's intentional that we don't reset in the else case;
            // we leave them on the stack such that when we hit depth zero
            // above, we can report all of them as successful.
        }
        else
        {
            // A false result goes straight into global cache (when something is false under
            // assumptions it will also be false without assumptions)
            _relation.Set(id, RelationComparisonResult.Failed | propagatingVarianceFlags);
            _relationCount--;
            ResetMaybeStack(maybeStart, propagatingVarianceFlags, false);
        }
        return result;
    }

    /// <summary>Go: <c>func (r *Relater) resetMaybeStack(maybeStart int, propagatingVarianceFlags RelationComparisonResult, markAllAsSucceeded bool)</c>（relater.go:3201）。</summary>
    internal void ResetMaybeStack(int maybeStart, RelationComparisonResult propagatingVarianceFlags, bool markAllAsSucceeded)
    {
        for (var i = maybeStart; i < _maybeKeys.Count; i++)
        {
            _maybeKeysSet.Delete(_maybeKeys[i]);
            if (markAllAsSucceeded)
            {
                _relation.Set(_maybeKeys[i], RelationComparisonResult.Succeeded | propagatingVarianceFlags);
                _relationCount--;
            }
        }
        _maybeKeys.RemoveRange(maybeStart, _maybeKeys.Count - maybeStart);
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getTypeOfPropertyInTypes(types []*Type, name string) *Type</c>（relater.go:2829）。</summary>
    internal Type? GetTypeOfPropertyInTypes(Type[] types, string name)
    {
        var propTypes = new List<Type>();
        foreach (var t in types)
        {
            propTypes.Add(GetTypeOfPropertyInType(t, name)!);
        }
        return GetUnionTypeWorker(propTypes.ToArray());
    }

    /// <summary>Go: <c>func (c *Checker) getTypeOfPropertyInType(t *Type, name string) *Type</c>（relater.go:2837）。</summary>
    internal Type? GetTypeOfPropertyInType(Type t, string name)
    {
        t = GetApparentTypeWorker(t);
        Symbol? prop;
        if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            prop = GetPropertyOfUnionOrIntersectionType(t, name, false);
        }
        else
        {
            prop = GetPropertyOfObjectType(t, name);
        }
        if (prop != null)
        {
            return GetTypeOfSymbolWorker(prop);
        }
        var indexInfo = GetApplicableIndexInfoForName(t, name);
        if (indexInfo != null)
        {
            return indexInfo._valueType;
        }
        return _undefinedType;
    }

    /// <summary>Go: <c>func shouldCheckAsExcessProperty(prop *ast.Symbol, container *ast.Symbol) bool</c>（relater.go:2855，包级函数）。</summary>
    internal static bool ShouldCheckAsExcessProperty(Symbol prop, Symbol container)
    {
        return prop.ValueDeclaration != null && container.ValueDeclaration != null && prop.ValueDeclaration.Parent == container.ValueDeclaration;
    }

    /// <summary>Go: <c>func isIgnoredJsxProperty(source *Type, sourceProp *ast.Symbol) bool</c>（relater.go:2859，包级函数）。</summary>
    internal static bool IsIgnoredJsxProperty(Type source, Symbol sourceProp)
    {
        return (source._objectFlags & ObjectFlags.JsxAttributes) != 0 && IsHyphenatedJsxName(sourceProp.Name);
    }

    /// <summary>Go: <c>func (c *Checker) isTypeSubsetOf(source *Type, target *Type) bool</c>（relater.go:2863）。</summary>
    internal bool IsTypeSubsetOf(Type source, Type target)
    {
        return source == target || (source._flags & TypeFlags.Never) != 0 || (target._flags & TypeFlags.Union) != 0 && IsTypeSubsetOfUnion(source, target);
    }

    /// <summary>Go: <c>func (c *Checker) isTypeSubsetOfUnion(source *Type, target *Type) bool</c>（relater.go:2867）。</summary>
    internal bool IsTypeSubsetOfUnion(Type source, Type target)
    {
        if ((source._flags & TypeFlags.Union) != 0)
        {
            foreach (var t in source.Types())
            {
                if (!ContainsType(target.Types(), t))
                {
                    return false;
                }
            }
            return true;
        }
        if ((source._flags & TypeFlags.EnumLike) != 0 && GetBaseTypeOfEnumLikeType(source) == target)
        {
            return true;
        }
        return ContainsType(target.Types(), source);
    }
}
