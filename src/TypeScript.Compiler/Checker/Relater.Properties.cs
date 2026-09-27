// ported from: internal/checker/relater.go:4004-4472（mappedTypeRelatedTo / typeRelatedToDiscriminatedType /
//   propertiesRelatedTo / propertyRelatedTo / isPropertySymbolTypeRelated / reportUnmatchedProperty /
//   tryElaborateArrayLikeErrors / tryElaborateErrorsForPrimitivesAndObjects / propertiesIdenticalTo）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `var excludedProperties collections.Set[string]`（零值 = nil map，Add 时惰性分配）→ C# `new Set<string>()`
//    （C# 的 Set<T> 恒有 M；该 Set 只在 `len(sourcePropertiesFiltered) == 0` 提前返回之后被 Add，至少 Add 一次，
//    观测行为等价）。
// 2) `typeRelatedToDiscriminatedType` 的 `outer:` 标签 + `continue outer` → C# 用 `skip` 标志 + `break` + `continue`。
// 3) 4092 行的闭包 `func(*ast.Symbol) *Type { return combination[i] }` → C# 局部函数（捕获 `combination`/`i`），
//    传给 `PropertyRelatedTo` 的 `Func<Symbol, Type>` 形参；4291 行的 `r.c.getNonMissingTypeOfSymbol` 是方法组。
// 4) `r.relatedInfo = append(r.relatedInfo, x)` → `_relatedInfo = [.. _relatedInfo, x]`（Go 的 append 语义；
//    `_relatedInfo` 在 Relater.Types.cs 里是 `Diagnostic[]`）。
// 5) `strconv.Itoa(int)` → `int.ToString(CultureInfo.InvariantCulture)`。
// 6) `strings.Join(core.Map(props, r.c.symbolToString), ", ")` → `string.Join(", ", Map(props, x => _c.SymbolToString(x)))`
//    （`Core.Map` 的返回类型必须由函数值推断，方法组不能用于类型推断）。
// 7) Go 的 `switch { case ...: }` → C# `if`/`else if` 链（顺序敏感，逐字保留；各 case 体都以 return 结束）。
// 8) `if tr := r.c.tracer; tr != nil` → `var tr = _c._tracer; if (tr != null)`（Go 的 nil 接收者不可分派）。
// 9) `r.c.TypeToString(t)` / `r.c.SymbolToString(s)` / `r.c.symbolToString(s)` → `_c.TypeToString(t)` / `_c.SymbolToString(s)`
//    （Go 的导出 `TypeToString`/`SymbolToString` 就是 `typeToString(t, nil)`/`symbolToString(s)` 的转发，行为等价；
//    用公开形式而非 `TypeToStringWorker`/`SymbolToStringWorker`，是因为 C# 侧那两个是 2 参形态
//    `TypeToStringWorker(Type, Node?)`，与 Go 调用点的 1 参形态不匹配）。
// 10) `nil slice` 的 `matchingTypes`（只有 `core.AppendIfUnique` 追加）→ `Type[] matchingTypes = []` +
//     `matchingTypes = AppendIfUnique(matchingTypes, t)`（与 Go 的 append 返回新切片逐字对应）。
// 11) **usings 放在 `namespace TypeScript.Checker;` 之后**（合同 §0 写的是"usings 再 namespace"，但那样是错的）：
//     本文件要对 `Diagnostics.Xxx` / `Ast.IsPrivateIdentifier` / `Binder.GetSymbolNameForPrivateIdentifier` 做**限定名**
//     访问，而 `TypeScript.Diagnostics` / `TypeScript.Ast` / `TypeScript.Binder` 都是外层命名空间 `TypeScript` 的成员，
//     放在 namespace 之前的 usings 属于编译单元作用域，名字解析会在"外层命名空间链"这一步就先命中**命名空间**，
//     `using` 导入的**类型**（同名静态类）永远轮不到 → CS0234（csc 实测：'BeforeNs.cs' 报 2 处 CS0234；
//     同名别名 `using Diagnostics = TypeScript.Diagnostics.Diagnostics;` 放在 namespace 之前**同样无效**；
//     只有"usings 在 namespace 之后"或"使用点全限定"才通过）。这也是 Checker 包既有文件的统一写法。
namespace TypeScript.Checker;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
// Go 的包级函数（无接收者）在 C# 里是 `partial class Checker` 的 static 成员：在 `Relater` 里按 Go 的裸名调用
// （`isTupleType`→`IsTupleTypeWorker`、`getStartElementCount`→`GetStartElementCount`、`countTypes`→`CountTypes`、
// `getMappedTypeModifiers`→`GetMappedTypeModifiers`、`isNumericLiteralName`→`IsNumericLiteralName`、
// `isObjectLiteralType`→`IsObjectLiteralType`、`excludeProperties`→`ExcludeProperties`、
// `getDeclarationModifierFlagsFromSymbol`→`GetDeclarationModifierFlagsFromSymbol`、
// `createDiagnosticForNode`→`CreateDiagnosticForNode`），靠 using static 引入。
using static TypeScript.Checker.Checker;

public sealed partial class Relater
{
    internal Ternary MappedTypeRelatedTo(Type source, Type target, bool reportErrors)
    {
        var modifiersRelated =
            _relation == _c._comparableRelation ||
            _relation == _c._identityRelation && GetMappedTypeModifiers(source) == GetMappedTypeModifiers(target) ||
            _relation != _c._identityRelation && _c.GetCombinedMappedTypeOptionality(source) <= _c.GetCombinedMappedTypeOptionality(target);
        if (modifiersRelated)
        {
            var targetConstraint = _c.GetConstraintTypeFromMappedType(target);
            var sourceConstraint = _c.InstantiateType(
                _c.GetConstraintTypeFromMappedType(source),
                Core.IfElse(_c.GetCombinedMappedTypeOptionality(source) < 0, _c._reportUnmeasurableMapper, _c._reportUnreliableMapper));
            var result = IsRelatedTo(targetConstraint, sourceConstraint, RecursionFlags.Both, reportErrors);
            if (result != Ternary.False)
            {
                var mapper = new SimpleTypeMapper(_c.GetTypeParameterFromMappedType(source), _c.GetTypeParameterFromMappedType(target));
                if (_c.InstantiateType(_c.GetNameTypeFromMappedType(source)!, mapper) == _c.InstantiateType(_c.GetNameTypeFromMappedType(target)!, mapper))
                {
                    return result & IsRelatedTo(
                        _c.InstantiateType(_c.GetTemplateTypeFromMappedType(source), mapper),
                        _c.GetTemplateTypeFromMappedType(target),
                        RecursionFlags.Both,
                        reportErrors);
                }
            }
        }
        return Ternary.False;
    }

    internal Ternary TypeRelatedToDiscriminatedType(Type source, Type target)
    {
        // 1. Generate the combinations of discriminant properties & types 'source' can satisfy.
        //    a. If the number of combinations is above a set limit, the comparison is too complex.
        // 2. Filter 'target' to the subset of types whose discriminants exist in the matrix.
        //    a. If 'target' does not satisfy all discriminants in the matrix, 'source' is not related.
        // 3. For each type in the filtered 'target', determine if all non-discriminant properties of
        //    'target' are related to a property in 'source'.
        //
        // NOTE: See ~/tests/cases/conformance/types/typeRelationships/assignmentCompatibility/assignmentCompatWithDiscriminatedUnion.ts
        //       for examples.
        var sourceProperties = _c.GetPropertiesOfTypeWorker(source) ?? [];
        var sourcePropertiesFiltered = _c.FindDiscriminantProperties(sourceProperties, target);
        if (sourcePropertiesFiltered.Length == 0)
        {
            return Ternary.False;
        }
        // Though we could compute the number of combinations as we generate
        // the matrix, this would incur additional memory overhead due to
        // array allocations. To reduce this overhead, we first compute
        // the number of combinations to ensure we will not surpass our
        // fixed limit before incurring the cost of any allocations:
        var numCombinations = 1;
        foreach (var sourceProperty in sourcePropertiesFiltered)
        {
            numCombinations *= CountTypes(_c.GetNonMissingTypeOfSymbolWorker(sourceProperty));
            if (numCombinations > 25)
            {
                var tr = _c._tracer;
                if (tr != null)
                {
                    tr.Instant(Phase.CheckTypes, "typeRelatedToDiscriminatedType_DepthLimit", new Dictionary<string, object?>
                    {
                        ["sourceId"] = source._id.Value,
                        ["targetId"] = target._id.Value,
                        ["numCombinations"] = numCombinations,
                    });
                }
                return Ternary.False;
            }
            if (numCombinations == 0)
            {
                return Ternary.False;
            }
        }
        // Compute the set of types for each discriminant property.
        var sourceDiscriminantTypes = new Type[sourcePropertiesFiltered.Length][];
        var excludedProperties = new Set<string>();
        for (var i = 0; i < sourcePropertiesFiltered.Length; i++)
        {
            var sourceProperty = sourcePropertiesFiltered[i];
            var sourcePropertyType = _c.GetNonMissingTypeOfSymbolWorker(sourceProperty);
            sourceDiscriminantTypes[i] = sourcePropertyType.Distributed();
            excludedProperties.Add(sourceProperty.Name);
        }
        // Build the cartesian product
        var discriminantCombinations = new Type[numCombinations][];
        for (var i = 0; i < numCombinations; i++)
        {
            var combination = new Type[sourceDiscriminantTypes.Length];
            var n = i;
            for (var j = sourceDiscriminantTypes.Length - 1; j >= 0; j--)
            {
                var sourceTypes = sourceDiscriminantTypes[j];
                var length = sourceTypes.Length;
                combination[j] = sourceTypes[n % length];
                n = n / length;
            }
            discriminantCombinations[i] = combination;
        }
        // Match each combination of the cartesian product of discriminant properties to one or more
        // constituents of 'target'. If any combination does not have a match then 'source' is not relatable.
        Type[] matchingTypes = [];
        foreach (var combination in discriminantCombinations)
        {
            var hasMatch = false;
            foreach (var t in target.Types())
            {
                var skip = false;
                for (var i = 0; i < sourcePropertiesFiltered.Length; i++)
                {
                    var sourceProperty = sourcePropertiesFiltered[i];
                    var targetProperty = _c.GetPropertyOfTypeWorker(t, sourceProperty.Name);
                    if (targetProperty == null)
                    {
                        skip = true;
                        break;
                    }
                    if (sourceProperty == targetProperty)
                    {
                        continue;
                    }
                    // We compare the source property to the target in the context of a single discriminant type.
                    Type GetTypeOfSourceProperty(Symbol _) => combination[i];
                    var related = PropertyRelatedTo(
                        source,
                        target,
                        sourceProperty,
                        targetProperty,
                        GetTypeOfSourceProperty,
                        false /*reportErrors*/,
                        IntersectionState.None,
                        _c._strictNullChecks || _relation == _c._comparableRelation /*skipOptional*/);
                    // If the target property could not be found, or if the properties were not related,
                    // then this constituent is not a match.
                    if (related == Ternary.False)
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip)
                {
                    continue;
                }
                matchingTypes = Core.AppendIfUnique(matchingTypes, t);
                hasMatch = true;
            }
            if (!hasMatch)
            {
                // We failed to match any type for this combination.
                return Ternary.False;
            }
        }
        // Compare the remaining non-discriminant properties of each match.
        var result = Ternary.True;
        foreach (var t in matchingTypes)
        {
            result &= PropertiesRelatedTo(source, t, false /*reportErrors*/, excludedProperties, false /*optionalsOnly*/, IntersectionState.None);
            if (result != Ternary.False)
            {
                result &= SignaturesRelatedTo(source, t, SignatureKind.Call, false /*reportErrors*/, IntersectionState.None);
                if (result != Ternary.False)
                {
                    result &= SignaturesRelatedTo(source, t, SignatureKind.Construct, false /*reportErrors*/, IntersectionState.None);
                    if (result != Ternary.False && !(IsTupleTypeWorker(source) && IsTupleTypeWorker(t)))
                    {
                        // Comparing numeric index types when both `source` and `type` are tuples is unnecessary as the
                        // element types should be sufficiently covered by `propertiesRelatedTo`. It also causes problems
                        // with index type assignability as the types for the excluded discriminants are still included
                        // in the index type.
                        result &= IndexSignaturesRelatedTo(source, t, false /*sourceIsPrimitive*/, false /*reportErrors*/, IntersectionState.None);
                    }
                }
            }
            if (result == Ternary.False)
            {
                return result;
            }
        }
        return result;
    }

    internal Ternary PropertiesRelatedTo(Type source, Type target, bool reportErrors, Set<string> excludedProperties, bool optionalsOnly, IntersectionState intersectionState)
    {
        if (_relation == _c._identityRelation)
        {
            return PropertiesIdenticalTo(source, target, excludedProperties);
        }
        var result = Ternary.True;
        if (IsTupleTypeWorker(target))
        {
            if (_c.IsArrayOrTupleType(source))
            {
                // Go: `if !target.TargetTupleType().readonly && (isReadonlyArrayType(source) || isTupleType(source) && source.TargetTupleType().readonly)`
                // —— 只有「目标可变 + 源只读」才不相关；C# 之前漏了 `!`，把 readonly → readonly 也判成 False。
                if (!target.TargetTupleType()!._readonly && (_c.IsReadonlyArrayType(source) || IsTupleTypeWorker(source) && source.TargetTupleType()!._readonly))
                {
                    return Ternary.False;
                }
                var sourceArity = _c.GetTypeReferenceArity(source);
                var targetArity = _c.GetTypeReferenceArity(target);
                bool sourceRest;
                if (IsTupleTypeWorker(source))
                {
                    sourceRest = (source.TargetTupleType()!._combinedFlags & ElementFlags.Rest) != 0;
                }
                else
                {
                    sourceRest = true;
                }
                var targetHasRestElement = (target.TargetTupleType()!._combinedFlags & ElementFlags.Rest) != 0;
                var targetHasVariableElement = (target.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0;
                int sourceMinLength;
                if (IsTupleTypeWorker(source))
                {
                    sourceMinLength = source.TargetTupleType()!._minLength;
                }
                else
                {
                    sourceMinLength = 0;
                }
                var targetMinLength = target.TargetTupleType()!._minLength;
                if (!sourceRest && sourceArity < targetMinLength)
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Source_has_0_element_s_but_target_requires_1, sourceArity, targetMinLength);
                    }
                    return Ternary.False;
                }
                if (!targetHasVariableElement && targetArity < sourceMinLength)
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Source_has_0_element_s_but_target_allows_only_1, sourceMinLength, targetArity);
                    }
                    return Ternary.False;
                }
                if (!targetHasVariableElement && (sourceRest || targetArity < sourceArity))
                {
                    if (reportErrors)
                    {
                        if (sourceMinLength < targetMinLength)
                        {
                            ReportError(Diagnostics.Target_requires_0_element_s_but_source_may_have_fewer, targetMinLength);
                        }
                        else
                        {
                            ReportError(Diagnostics.Target_allows_only_0_element_s_but_source_may_have_more, targetArity);
                        }
                    }
                    return Ternary.False;
                }
                var sourceTypeArguments = _c.GetTypeArgumentsWorker(source);
                var targetTypeArguments = _c.GetTypeArgumentsWorker(target);
                var targetStartCount = GetStartElementCount(target.TargetTupleType()!, ElementFlags.NonRest);
                var targetEndCount = GetEndElementCount(target.TargetTupleType()!, ElementFlags.NonRest);
                var canExcludeDiscriminants = excludedProperties.Len() != 0;
                for (var sourcePosition = 0; sourcePosition < sourceArity; sourcePosition++)
                {
                    ElementFlags sourceFlags;
                    if (IsTupleTypeWorker(source))
                    {
                        sourceFlags = source.TargetTupleType()!._elementInfos![sourcePosition]._flags;
                    }
                    else
                    {
                        sourceFlags = ElementFlags.Rest;
                    }
                    var sourcePositionFromEnd = sourceArity - 1 - sourcePosition;
                    int targetPosition;
                    if (targetHasRestElement && sourcePosition >= targetStartCount)
                    {
                        targetPosition = targetArity - 1 - Math.Min(sourcePositionFromEnd, targetEndCount);
                    }
                    else
                    {
                        if (sourcePosition >= targetArity)
                        {
                            if (reportErrors)
                            {
                                ReportError(Diagnostics.Target_allows_only_0_element_s_but_source_may_have_more, targetArity);
                            }
                            return Ternary.False;
                        }
                        targetPosition = sourcePosition;
                    }
                    var targetFlags = ElementFlags.None;
                    if (targetPosition >= 0)
                    {
                        targetFlags = target.TargetTupleType()!._elementInfos![targetPosition]._flags;
                    }
                    if ((targetFlags & ElementFlags.Variadic) != 0 && (sourceFlags & ElementFlags.Variadic) == 0)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Source_provides_no_match_for_variadic_element_at_position_0_in_target, targetPosition);
                        }
                        return Ternary.False;
                    }
                    if ((sourceFlags & ElementFlags.Variadic) != 0 && (targetFlags & ElementFlags.Variable) == 0)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Variadic_element_at_position_0_in_source_does_not_match_element_at_position_1_in_target, sourcePosition, targetPosition);
                        }
                        return Ternary.False;
                    }
                    if ((targetFlags & ElementFlags.Required) != 0 && (sourceFlags & ElementFlags.Required) == 0)
                    {
                        if (reportErrors)
                        {
                            ReportError(Diagnostics.Source_provides_no_match_for_required_element_at_position_0_in_target, targetPosition);
                        }
                        return Ternary.False;
                    }
                    // We can only exclude discriminant properties if we have not yet encountered a variable-length element.
                    if (canExcludeDiscriminants)
                    {
                        if ((sourceFlags & ElementFlags.Variable) != 0 || (targetFlags & ElementFlags.Variable) != 0)
                        {
                            canExcludeDiscriminants = false;
                        }
                        if (canExcludeDiscriminants && excludedProperties.Has(sourcePosition.ToString(CultureInfo.InvariantCulture)))
                        {
                            continue;
                        }
                    }
                    var sourceType = _c.RemoveMissingType(sourceTypeArguments[sourcePosition], (sourceFlags & targetFlags & ElementFlags.Optional) != 0);
                    var targetType = targetTypeArguments[targetPosition];
                    Type targetCheckType;
                    if ((sourceFlags & ElementFlags.Variadic) != 0 && (targetFlags & ElementFlags.Rest) != 0)
                    {
                        targetCheckType = _c.CreateArrayType(targetType);
                    }
                    else
                    {
                        targetCheckType = _c.RemoveMissingType(targetType, (targetFlags & ElementFlags.Optional) != 0);
                    }
                    var related = IsRelatedToEx(sourceType, targetCheckType, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
                    if (related == Ternary.False)
                    {
                        if (reportErrors && (targetArity > 1 || sourceArity > 1))
                        {
                            if (targetHasRestElement && sourcePosition >= targetStartCount && sourcePositionFromEnd >= targetEndCount && targetStartCount != sourceArity - targetEndCount - 1)
                            {
                                ReportError(Diagnostics.Type_at_positions_0_through_1_in_source_is_not_compatible_with_type_at_position_2_in_target, targetStartCount, sourceArity - targetEndCount - 1, targetPosition);
                            }
                            else
                            {
                                ReportError(Diagnostics.Type_at_position_0_in_source_is_not_compatible_with_type_at_position_1_in_target, sourcePosition, targetPosition);
                            }
                        }
                        return Ternary.False;
                    }
                    result &= related;
                }
                return result;
            }
            if ((target.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0)
            {
                return Ternary.False;
            }
        }
        var requireOptionalProperties = (_relation == _c._subtypeRelation || _relation == _c._strictSubtypeRelation) && !IsObjectLiteralType(source) && !_c.IsEmptyArrayLiteralType(source) && !IsTupleTypeWorker(source);
        var unmatchedProperty = _c.GetUnmatchedProperty(source, target, requireOptionalProperties, false /*matchDiscriminantProperties*/);
        if (unmatchedProperty != null)
        {
            if (reportErrors && _c.ShouldReportUnmatchedPropertyError(source, target))
            {
                ReportUnmatchedProperty(source, target, unmatchedProperty, requireOptionalProperties);
            }
            return Ternary.False;
        }
        if (IsObjectLiteralType(target))
        {
            foreach (var sourceProp in ExcludeProperties(_c.GetPropertiesOfTypeWorker(source) ?? [], excludedProperties))
            {
                if (_c.GetPropertyOfObjectType(target, sourceProp.Name) == null)
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Property_0_does_not_exist_on_type_1, _c.SymbolToString(sourceProp), _c.TypeToString(target));
                    }
                    return Ternary.False;
                }
            }
        }
        // We only call this for union target types when we're attempting to do excess property checking - in those cases, we want to get _all possible props_
        // from the target union, across all members
        var properties = _c.GetPropertiesOfTypeWorker(target) ?? [];
        var numericNamesOnly = IsTupleTypeWorker(source) && IsTupleTypeWorker(target);
        foreach (var targetProp in ExcludeProperties(properties, excludedProperties))
        {
            var name = targetProp.Name;
            if ((targetProp.Flags & SymbolFlags.Prototype) == 0 && (!numericNamesOnly || IsNumericLiteralName(name) || name == "length") && (!optionalsOnly || (targetProp.Flags & SymbolFlags.Optional) != 0))
            {
                var sourceProp = _c.GetPropertyOfTypeWorker(source, name);
                if (sourceProp != null && sourceProp != targetProp)
                {
                    var related = PropertyRelatedTo(source, target, sourceProp, targetProp, _c.GetNonMissingTypeOfSymbolWorker, reportErrors, intersectionState, _relation == _c._comparableRelation);
                    if (related == Ternary.False)
                    {
                        return Ternary.False;
                    }
                    result &= related;
                }
            }
        }
        return result;
    }

    internal Ternary PropertyRelatedTo(Type source, Type target, Symbol sourceProp, Symbol targetProp, Func<Symbol, Type> getTypeOfSourceProperty, bool reportErrors, IntersectionState intersectionState, bool skipOptional)
    {
        var sourcePropFlags = GetDeclarationModifierFlagsFromSymbol(sourceProp);
        var targetPropFlags = GetDeclarationModifierFlagsFromSymbol(targetProp);
        if ((sourcePropFlags & ModifierFlags.Private) != 0 || (targetPropFlags & ModifierFlags.Private) != 0)
        {
            if (sourceProp.ValueDeclaration != targetProp.ValueDeclaration)
            {
                if (reportErrors)
                {
                    if ((sourcePropFlags & ModifierFlags.Private) != 0 && (targetPropFlags & ModifierFlags.Private) != 0)
                    {
                        ReportError(Diagnostics.Types_have_separate_declarations_of_a_private_property_0, _c.SymbolToString(targetProp));
                    }
                    else
                    {
                        ReportError(
                            Diagnostics.Property_0_is_private_in_type_1_but_not_in_type_2,
                            _c.SymbolToString(targetProp),
                            _c.TypeToString(Core.IfElse((sourcePropFlags & ModifierFlags.Private) != 0, source, target)),
                            _c.TypeToString(Core.IfElse((sourcePropFlags & ModifierFlags.Private) != 0, target, source)));
                    }
                }
                return Ternary.False;
            }
        }
        else if ((targetPropFlags & ModifierFlags.Protected) != 0)
        {
            if (!_c.IsValidOverrideOf(sourceProp, targetProp))
            {
                if (reportErrors)
                {
                    var sourceType = Core.OrElse(_c.GetDeclaringClass(sourceProp), source);
                    var targetType = Core.OrElse(_c.GetDeclaringClass(targetProp), target);
                    ReportError(Diagnostics.Property_0_is_protected_but_type_1_is_not_a_class_derived_from_2, _c.SymbolToString(targetProp), _c.TypeToString(sourceType!), _c.TypeToString(targetType!));
                }
                return Ternary.False;
            }
        }
        else if ((sourcePropFlags & ModifierFlags.Protected) != 0)
        {
            if (reportErrors)
            {
                ReportError(Diagnostics.Property_0_is_protected_in_type_1_but_public_in_type_2, _c.SymbolToString(targetProp), _c.TypeToString(source), _c.TypeToString(target));
            }
            return Ternary.False;
        }
        // Ensure {readonly a: whatever} is not a subtype of {a: whatever},
        // while {a: whatever} is a subtype of {readonly a: whatever}.
        // This ensures the subtype relationship is ordered, and preventing declaration order
        // from deciding which type "wins" in union subtype reduction.
        // They're still assignable to one another, since `readonly` doesn't affect assignability.
        // This is only applied during the strictSubtypeRelation -- currently used in subtype reduction
        if (_relation == _c._strictSubtypeRelation && _c.IsReadonlySymbolWorker(sourceProp) && !_c.IsReadonlySymbolWorker(targetProp))
        {
            return Ternary.False;
        }
        // If the target comes from a partial union prop, allow `undefined` in the target type
        var related = IsPropertySymbolTypeRelated(sourceProp, targetProp, getTypeOfSourceProperty, reportErrors, intersectionState);
        if (related == Ternary.False)
        {
            if (reportErrors)
            {
                ReportError(Diagnostics.Types_of_property_0_are_incompatible, _c.SymbolToString(targetProp));
            }
            return Ternary.False;
        }
        // When checking for comparability, be more lenient with optional properties.
        if (!skipOptional && (sourceProp.Flags & SymbolFlags.Optional) != 0 && (targetProp.Flags & SymbolFlags.ClassMember) != 0 && (targetProp.Flags & SymbolFlags.Optional) == 0)
        {
            // TypeScript 1.0 spec (April 2014): 3.8.3
            // S is a subtype of a type T, and T is a supertype of S if ...
            // S' and T are object types and, for each member M in T..
            // M is a property and S' contains a property N where
            // if M is a required property, N is also a required property
            // (M - property in T)
            // (N - property in S)
            if (reportErrors)
            {
                ReportError(Diagnostics.Property_0_is_optional_in_type_1_but_required_in_type_2, _c.SymbolToString(targetProp), _c.TypeToString(source), _c.TypeToString(target));
            }
            return Ternary.False;
        }
        return related;
    }

    internal Ternary IsPropertySymbolTypeRelated(Symbol sourceProp, Symbol targetProp, Func<Symbol, Type> getTypeOfSourceProperty, bool reportErrors, IntersectionState intersectionState)
    {
        var targetIsOptional = _c._strictNullChecks && (targetProp.CheckFlags & CheckFlags.Partial) != 0;
        var effectiveTarget = _c.AddOptionalityEx(_c.GetNonMissingTypeOfSymbolWorker(targetProp), false /*isProperty*/, targetIsOptional);
        // source could resolve to `any` and that's not related to `unknown` target under strict subtype relation
        if ((effectiveTarget._flags & Core.IfElse(_relation == _c._strictSubtypeRelation, TypeFlags.Any, TypeFlags.AnyOrUnknown)) != 0)
        {
            return Ternary.True;
        }
        var effectiveSource = getTypeOfSourceProperty(sourceProp);
        return IsRelatedToEx(effectiveSource, effectiveTarget, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
    }

    internal void ReportUnmatchedProperty(Type source, Type target, Symbol unmatchedProperty, bool requireOptionalProperties)
    {
        // give specific error in case where private names have the same description
        if (unmatchedProperty.ValueDeclaration != null &&
            unmatchedProperty.ValueDeclaration.Name() != null &&
            Ast.IsPrivateIdentifier(unmatchedProperty.ValueDeclaration.Name()!) &&
            source._symbol != null &&
            (source._symbol.Flags & SymbolFlags.Class) != 0)
        {
            var privateIdentifierDescription = unmatchedProperty.ValueDeclaration.Name()!.Text();
            var symbolTableKey = Binder.GetSymbolNameForPrivateIdentifier(source._symbol!, privateIdentifierDescription);
            if (_c.GetPropertyOfTypeWorker(source, symbolTableKey) != null)
            {
                ReportError(
                    Diagnostics.Property_0_in_type_1_refers_to_a_different_member_that_cannot_be_accessed_from_within_type_2,
                    privateIdentifierDescription,
                    _c.SymbolToString(source._symbol!),
                    _c.SymbolToString(target._symbol!));
                return;
            }
        }
        var props = _c.GetUnmatchedProperties(source, target, requireOptionalProperties, false /*matchDiscriminantProperties*/);
        if (props.Length == 1)
        {
            var (sourceType, targetType) = _c.GetTypeNamesForErrorDisplay(source, target);
            var propName = _c.SymbolToString(unmatchedProperty);
            ReportError(Diagnostics.Property_0_is_missing_in_type_1_but_required_in_type_2, propName, sourceType, targetType);
            var declarations = unmatchedProperty.Declarations;
            if (declarations != null && declarations.Length != 0)
            {
                _relatedInfo = [.. _relatedInfo, CreateDiagnosticForNode(declarations[0], Diagnostics.X_0_is_declared_here, propName)];
            }
        }
        else if (TryElaborateArrayLikeErrors(source, target, false /*reportErrors*/))
        {
            var (sourceType, targetType) = _c.GetTypeNamesForErrorDisplay(source, target);
            if (props.Length > 5)
            {
                var propNames = string.Join(", ", Core.Map(props[..4], x => _c.SymbolToString(x)));
                ReportError(Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2_and_3_more, sourceType, targetType, propNames, props.Length - 4);
            }
            else
            {
                var propNames = string.Join(", ", Core.Map(props, x => _c.SymbolToString(x)));
                ReportError(Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2, sourceType, targetType, propNames);
            }
        }
    }

    internal bool TryElaborateArrayLikeErrors(Type source, Type target, bool reportErrors)
    {
        /**
         * The spec for elaboration is:
         * - If the source is a readonly tuple and the target is a mutable array or tuple, elaborate on mutability and skip property elaborations.
         * - If the source is a tuple then skip property elaborations if the target is an array or tuple.
         * - If the source is a readonly array and the target is a mutable array or tuple, elaborate on mutability and skip property elaborations.
         * - If the source an array then skip property elaborations if the target is a tuple.
         */
        if (IsTupleTypeWorker(source))
        {
            if (source.TargetTupleType()!._readonly && _c.IsMutableArrayOrTuple(target))
            {
                if (reportErrors)
                {
                    ReportError(Diagnostics.The_type_0_is_readonly_and_cannot_be_assigned_to_the_mutable_type_1, _c.TypeToString(source), _c.TypeToString(target));
                }
                return false;
            }
            return _c.IsArrayOrTupleType(target);
        }
        if (_c.IsReadonlyArrayType(source) && _c.IsMutableArrayOrTuple(target))
        {
            if (reportErrors)
            {
                ReportError(Diagnostics.The_type_0_is_readonly_and_cannot_be_assigned_to_the_mutable_type_1, _c.TypeToString(source), _c.TypeToString(target));
            }
            return false;
        }
        if (IsTupleTypeWorker(target))
        {
            return _c.IsArrayTypeWorker(source);
        }
        return true;
    }

    internal void TryElaborateErrorsForPrimitivesAndObjects(Type source, Type target)
    {
        if ((source == _c._globalStringType && target == _c._stringType) ||
            (source == _c._globalNumberType && target == _c._numberType) ||
            (source == _c._globalBooleanType && target == _c._booleanType) ||
            (source == _c._getGlobalESSymbolType() && target == _c._esSymbolType))
        {
            ReportError(Diagnostics.X_0_is_a_primitive_but_1_is_a_wrapper_object_Prefer_using_0_when_possible, _c.TypeToString(target), _c.TypeToString(source));
        }
    }

    internal Ternary PropertiesIdenticalTo(Type source, Type target, Set<string> excludedProperties)
    {
        if ((source._flags & TypeFlags.Object) == 0 || (target._flags & TypeFlags.Object) == 0)
        {
            return Ternary.False;
        }
        var sourceProperties = ExcludeProperties(_c.GetPropertiesOfObjectType(source) ?? [], excludedProperties);
        var targetProperties = ExcludeProperties(_c.GetPropertiesOfObjectType(target) ?? [], excludedProperties);
        if (sourceProperties.Length != targetProperties.Length)
        {
            return Ternary.False;
        }
        var result = Ternary.True;
        foreach (var sourceProp in sourceProperties)
        {
            var targetProp = _c.GetPropertyOfObjectType(target, sourceProp.Name);
            if (targetProp == null)
            {
                return Ternary.False;
            }
            var related = _c.CompareProperties(sourceProp, targetProp, _isRelatedToSimpleDelegate);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        return result;
    }
}
