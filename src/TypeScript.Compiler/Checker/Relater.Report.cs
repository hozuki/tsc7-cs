// ported from: internal/checker/relater.go:4473-5046（signaturesRelatedTo / constructorVisibilitiesAreCompatible /
//   signatureRelatedTo / signaturesIdenticalTo / indexSignaturesRelatedTo / typeRelatedToIndexInfo /
//   isObjectTypeWithInferableIndex / membersRelatedToIndexInfo / indexInfoRelatedTo / indexSignaturesIdenticalTo /
//   reportErrorResults / reportRelationError / reportError / addToDottedName / getChainMessage / chainArgsMatch /
//   getPropertyNameArg / isConversionOrInterfaceImplementationMessage / chainDepth / isTypeDerivedFrom /
//   isDistributionDependent / traceUnionsOrIntersectionsTooLarge）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `signaturesRelatedTo` 的 `outer:` 标签 + `continue outer` → C# 用 `matched` 标志 + `continue`
//    （C# 有带标签的 goto，但这里用标志更直白；语义逐字等价：匹配到任一 source 签名后恢复错误状态并进入下一个 target）。
// 2) `signatureRelatedTo` 里的局部闭包 `isRelatedToWorker := func(...)`（捕获 `intersectionState`）→ C# lambda
//    （Go 同样每次分配一个函数值；Relater 上缓存的 `_isRelatedToWorkerDelegate` 不能捕获本次调用的
//    `intersectionState`，故此处保留闭包）。
// 3) Go 的 `switch r.getChainMessage(i) { case diagnostics.A, diagnostics.B: ... }` → C# `if`/`else if` 链：
//    `Diagnostics.A` 是 `static readonly Message` 字段（不是编译期常量），C# 的 `case` 标签不能用
//    （docs/checker-plan §5.4 的 switch 改写）。分支互斥，语义等价。
// 4) Go 的 `args ...any` 参数切片在 C# 里是 `params object?[]`，`args[0] = arg` 只是改本地数组，
//    与 Go 的 variadic 切片语义一致（`reportError` 里就是这样改写消息参数的）。
// 5) `chainArgsMatch` 里 `a != r.errorChain.args[i]` 是 Go 的**接口值比较**（动态类型 + 值）→ C# 用
//    `!Equals(a, x)`（string 按值、引用类型按引用、装箱值类型按值，与 Go 的 `==` 一致）；`a != nil` 用 `a != null`。
// 6) `*[]*ast.Diagnostic` / `[]*ast.Diagnostic` 的 `r.relatedInfo` → C# `Diagnostic[]`（追加用集合表达式
//    `[.. _relatedInfo, x]`；Go 的 append 语义保留）。
// 7) 字符串前缀判断用 `StringComparison.Ordinal`（Go 的 `strings.HasPrefix` 是字节序数比较，C# 的
//    `StartsWith(string)` 默认跟随当前区域，必须显式 Ordinal，见 docs/architecture.md §3.4）。
namespace TypeScript.Checker;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
// Go 的包级函数（无接收者）在 C# 里是 `partial class Checker` 的 static 成员：在 `Relater` 里按 Go 的裸名调用
// （`visibilityToString`/`newDiagnosticForNode`/`isLiteralType`/`isIgnoredJsxProperty`/…），靠 using static 引入。
using static TypeScript.Checker.Checker;

public sealed partial class Relater
{
    internal Ternary SignaturesRelatedTo(Type source, Type target, SignatureKind kind, bool reportErrors, IntersectionState intersectionState)
    {
        if (_relation == _c._identityRelation)
        {
            return SignaturesIdenticalTo(source, target, kind);
        }
        // With respect to signatures, the anyFunctionType wildcard is a subtype of every other function type.
        if (source == _c._anyFunctionType)
        {
            return Ternary.True;
        }
        if (target == _c._anyFunctionType)
        {
            return Ternary.False;
        }
        var sourceSignatures = _c.GetSignaturesOfTypeWorker(source, kind) ?? [];
        var targetSignatures = _c.GetSignaturesOfTypeWorker(target, kind) ?? [];
        if (kind == SignatureKind.Construct && sourceSignatures.Length != 0 && targetSignatures.Length != 0)
        {
            var sourceIsAbstract = (sourceSignatures[0]._flags & SignatureFlags.Abstract) != 0;
            var targetIsAbstract = (targetSignatures[0]._flags & SignatureFlags.Abstract) != 0;
            if (sourceIsAbstract && !targetIsAbstract)
            {
                // An abstract constructor type is not assignable to a non-abstract constructor type
                // as it would otherwise be possible to new an abstract class. Note that the assignability
                // check we perform for an extends clause excludes construct signatures from the target,
                // so this check never proceeds.
                if (reportErrors)
                {
                    ReportError(Diagnostics.Cannot_assign_an_abstract_constructor_type_to_a_non_abstract_constructor_type);
                }
                return Ternary.False;
            }
            if (!ConstructorVisibilitiesAreCompatible(sourceSignatures[0], targetSignatures[0], reportErrors))
            {
                return Ternary.False;
            }
        }
        var result = Ternary.True;
        if ((source._objectFlags & ObjectFlags.Instantiated) != 0 && (target._objectFlags & ObjectFlags.Instantiated) != 0 && source._symbol == target._symbol ||
            (source._objectFlags & ObjectFlags.Reference) != 0 && (target._objectFlags & ObjectFlags.Reference) != 0 && source.Target() == target.Target())
        {
            // We have instantiations of the same anonymous type (which typically will be the type of a
            // method). Simply do a pairwise comparison of the signatures in the two signature lists instead
            // of the much more expensive N * M comparison matrix we explore below. We erase type parameters
            // as they are known to always be the same.
            for (var i = 0; i < targetSignatures.Length; i++)
            {
                var related = SignatureRelatedTo(sourceSignatures[i], targetSignatures[i], true /*erase*/, reportErrors, intersectionState);
                if (related == Ternary.False)
                {
                    return Ternary.False;
                }
                result &= related;
            }
        }
        else if (sourceSignatures.Length == 1 && targetSignatures.Length == 1)
        {
            // For simple functions (functions with a single signature) we only erase type parameters for
            // the comparable relation. Otherwise, if the source signature is generic, we instantiate it
            // in the context of the target signature before checking the relationship. Ideally we'd do
            // this regardless of the number of signatures, but the potential costs are prohibitive due
            // to the quadratic nature of the logic below.
            var eraseGenerics = _relation == _c._comparableRelation;
            result = SignatureRelatedTo(sourceSignatures[0], targetSignatures[0], eraseGenerics, reportErrors, intersectionState);
        }
        else
        {
            foreach (var t in targetSignatures)
            {
                var saveErrorState = GetErrorState();
                // Only elaborate errors from the first failure
                var shouldElaborateErrors = reportErrors;
                var matched = false;
                foreach (var s in sourceSignatures)
                {
                    var related = SignatureRelatedTo(s, t, true /*erase*/, shouldElaborateErrors, intersectionState);
                    if (related != Ternary.False)
                    {
                        result &= related;
                        matched = true;
                        break;
                    }
                    shouldElaborateErrors = false;
                }
                if (matched)
                {
                    RestoreErrorState(saveErrorState);
                    continue;
                }
                if (shouldElaborateErrors)
                {
                    ReportError(Diagnostics.Type_0_provides_no_match_for_the_signature_1, _c.TypeToString(source), _c.SignatureToString(t));
                }
                return Ternary.False;
            }
        }
        return result;
    }

    internal bool ConstructorVisibilitiesAreCompatible(Signature sourceSignature, Signature targetSignature, bool reportErrors)
    {
        if (sourceSignature._declaration == null || targetSignature._declaration == null)
        {
            return true;
        }
        var sourceAccessibility = sourceSignature._declaration.ModifierFlags & ModifierFlags.NonPublicAccessibilityModifier;
        var targetAccessibility = targetSignature._declaration.ModifierFlags & ModifierFlags.NonPublicAccessibilityModifier;
        // A public, protected and private signature is assignable to a private signature.
        if (targetAccessibility == ModifierFlags.Private)
        {
            return true;
        }
        // A public and protected signature is assignable to a protected signature.
        if (targetAccessibility == ModifierFlags.Protected && sourceAccessibility != ModifierFlags.Private)
        {
            return true;
        }
        // Only a public signature is assignable to public signature.
        if (targetAccessibility != ModifierFlags.Protected && sourceAccessibility == 0)
        {
            return true;
        }
        if (reportErrors)
        {
            ReportError(Diagnostics.Cannot_assign_a_0_constructor_type_to_a_1_constructor_type, VisibilityToString(sourceAccessibility), VisibilityToString(targetAccessibility));
        }
        return false;
    }

    // See signatureAssignableTo, compareSignaturesIdentical
    internal Ternary SignatureRelatedTo(Signature source, Signature target, bool erase, bool reportErrors, IntersectionState intersectionState)
    {
        var checkMode = SignatureCheckMode.None;
        if (_relation == _c._subtypeRelation)
        {
            checkMode = SignatureCheckMode.StrictTopSignature;
        }
        else if (_relation == _c._strictSubtypeRelation)
        {
            checkMode = SignatureCheckMode.StrictTopSignature | SignatureCheckMode.StrictArity;
        }
        if (erase)
        {
            source = _c.GetErasedSignature(source);
            target = _c.GetErasedSignature(target);
        }
        TypeComparer isRelatedToWorker = (sourceType, targetType, reportErrorsArg) =>
            IsRelatedToEx(sourceType, targetType, RecursionFlags.Both, reportErrorsArg, null /*headMessage*/, intersectionState);
        return _c.CompareSignaturesRelated(source, target, checkMode, reportErrors, _reportErrorDelegate, isRelatedToWorker, _c._reportUnreliableMapper);
    }

    internal Ternary SignaturesIdenticalTo(Type source, Type target, SignatureKind kind)
    {
        var sourceSignatures = _c.GetSignaturesOfTypeWorker(source, kind) ?? [];
        var targetSignatures = _c.GetSignaturesOfTypeWorker(target, kind) ?? [];
        if (sourceSignatures.Length != targetSignatures.Length)
        {
            return Ternary.False;
        }
        var result = Ternary.True;
        for (var i = 0; i < sourceSignatures.Length; i++)
        {
            var related = _c.CompareSignaturesIdentical(sourceSignatures[i], targetSignatures[i], false /*partialMatch*/, false /*ignoreThisTypes*/, false /*ignoreReturnTypes*/, _isRelatedToSimpleDelegate);
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        return result;
    }

    internal Ternary IndexSignaturesRelatedTo(Type source, Type target, bool sourceIsPrimitive, bool reportErrors, IntersectionState intersectionState)
    {
        if (_relation == _c._identityRelation)
        {
            return IndexSignaturesIdenticalTo(source, target);
        }
        var indexInfos = _c.GetIndexInfosOfTypeWorker(target) ?? [];
        var targetHasStringIndex = Core.Some(indexInfos, info => info._keyType == _c._stringType);
        var result = Ternary.True;
        foreach (var targetInfo in indexInfos)
        {
            Ternary related;
            if (_relation != _c._strictSubtypeRelation && !sourceIsPrimitive && targetHasStringIndex && (targetInfo._valueType!._flags & TypeFlags.Any) != 0)
            {
                related = Ternary.True;
            }
            else if (_c.IsGenericMappedType(source) && targetHasStringIndex)
            {
                related = IsRelatedTo(_c.GetTemplateTypeFromMappedType(source), targetInfo._valueType!, RecursionFlags.Both, reportErrors);
            }
            else
            {
                related = TypeRelatedToIndexInfo(source, targetInfo, reportErrors, intersectionState);
            }
            if (related == Ternary.False)
            {
                return Ternary.False;
            }
            result &= related;
        }
        return result;
    }

    internal Ternary TypeRelatedToIndexInfo(Type source, IndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
    {
        var sourceInfo = _c.GetApplicableIndexInfo(source, targetInfo._keyType!);
        if (sourceInfo != null)
        {
            return IndexInfoRelatedTo(sourceInfo, targetInfo, reportErrors, intersectionState);
        }
        // Intersection constituents are never considered to have an inferred index signature. Also, in the strict subtype relation,
        // only fresh object literals are considered to have inferred index signatures. This ensures { [x: string]: xxx } <: {} but
        // not vice-versa. Without this rule, those types would be mutual strict subtypes.
        if ((intersectionState & IntersectionState.Source) == 0 && (_relation != _c._strictSubtypeRelation || (source._objectFlags & ObjectFlags.FreshLiteral) != 0) && _c.IsObjectTypeWithInferableIndex(source))
        {
            return MembersRelatedToIndexInfo(source, targetInfo, reportErrors, intersectionState);
        }
        if (reportErrors)
        {
            ReportError(Diagnostics.Index_signature_for_type_0_is_missing_in_type_1, _c.TypeToString(targetInfo._keyType!), _c.TypeToString(source));
        }
        return Ternary.False;
    }

    internal Ternary MembersRelatedToIndexInfo(Type source, IndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
    {
        var result = Ternary.True;
        var keyType = targetInfo._keyType!;
        Symbol[] props;
        if ((source._flags & TypeFlags.Intersection) != 0)
        {
            props = _c.GetPropertiesOfUnionOrIntersectionType(source) ?? [];
        }
        else
        {
            props = _c.GetPropertiesOfObjectType(source) ?? [];
        }
        foreach (var prop in props)
        {
            // Skip over ignored JSX and symbol-named members
            if (IsIgnoredJsxProperty(source, prop))
            {
                continue;
            }
            if (_c.IsApplicableIndexType(_c.GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique, false), keyType))
            {
                var propType = _c.GetNonMissingTypeOfSymbolWorker(prop);
                Type t;
                if (_c._exactOptionalPropertyTypes || (propType._flags & TypeFlags.Undefined) != 0 || keyType == _c._numberType || (prop.Flags & SymbolFlags.Optional) == 0)
                {
                    t = propType;
                }
                else
                {
                    t = _c.GetTypeWithFacts(propType, TypeFacts.NEUndefined);
                }
                var related = IsRelatedToEx(t, targetInfo._valueType!, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
                if (related == Ternary.False)
                {
                    if (reportErrors)
                    {
                        ReportError(Diagnostics.Property_0_is_incompatible_with_index_signature, _c.SymbolToStringWorker(prop));
                    }
                    return Ternary.False;
                }
                result &= related;
            }
        }
        foreach (var info in _c.GetIndexInfosOfTypeWorker(source) ?? [])
        {
            if (_c.IsApplicableIndexType(info._keyType!, keyType))
            {
                var related = IndexInfoRelatedTo(info, targetInfo, reportErrors, intersectionState);
                if (!(related != 0))
                {
                    return Ternary.False;
                }
                result &= related;
            }
        }
        return result;
    }

    internal Ternary IndexInfoRelatedTo(IndexInfo sourceInfo, IndexInfo targetInfo, bool reportErrors, IntersectionState intersectionState)
    {
        var related = IsRelatedToEx(sourceInfo._valueType!, targetInfo._valueType!, RecursionFlags.Both, reportErrors, null /*headMessage*/, intersectionState);
        if (related == Ternary.False && reportErrors)
        {
            if (sourceInfo._keyType == targetInfo._keyType)
            {
                ReportError(Diagnostics.X_0_index_signatures_are_incompatible, _c.TypeToString(sourceInfo._keyType!));
            }
            else
            {
                ReportError(Diagnostics.X_0_and_1_index_signatures_are_incompatible, _c.TypeToString(sourceInfo._keyType!), _c.TypeToString(targetInfo._keyType!));
            }
        }
        return related;
    }

    internal Ternary IndexSignaturesIdenticalTo(Type source, Type target)
    {
        var sourceInfos = _c.GetIndexInfosOfTypeWorker(source) ?? [];
        var targetInfos = _c.GetIndexInfosOfTypeWorker(target) ?? [];
        if (sourceInfos.Length != targetInfos.Length)
        {
            return Ternary.False;
        }
        foreach (var targetInfo in targetInfos)
        {
            var sourceInfo = _c.GetIndexInfoOfTypeWorker(source, targetInfo._keyType!);
            if (!(sourceInfo != null && IsRelatedTo(sourceInfo._valueType!, targetInfo._valueType!, RecursionFlags.Both, false) != Ternary.False && sourceInfo._isReadonly == targetInfo._isReadonly))
            {
                return Ternary.False;
            }
        }
        return Ternary.True;
    }

    internal void ReportErrorResults(Type originalSource, Type originalTarget, Type source, Type target, Message? headMessage)
    {
        var sourceHasBase = _c.GetSingleBaseForNonAugmentingSubtype(originalSource) != null;
        var targetHasBase = _c.GetSingleBaseForNonAugmentingSubtype(originalTarget) != null;
        if (originalSource._alias != null || sourceHasBase)
        {
            source = originalSource;
        }
        if (originalTarget._alias != null || targetHasBase)
        {
            target = originalTarget;
        }
        if ((source._flags & TypeFlags.Object) != 0 && (target._flags & TypeFlags.Object) != 0)
        {
            TryElaborateArrayLikeErrors(source, target, true /*reportErrors*/);
        }
        if ((source._flags & TypeFlags.Object) != 0 && (target._flags & TypeFlags.Primitive) != 0)
        {
            TryElaborateErrorsForPrimitivesAndObjects(source, target);
        }
        else if (source._symbol != null && (source._flags & TypeFlags.Object) != 0 && _c._globalObjectType == source)
        {
            ReportError(Diagnostics.The_Object_type_is_assignable_to_very_few_other_types_Did_you_mean_to_use_the_any_type_instead);
        }
        else if ((source._objectFlags & ObjectFlags.JsxAttributes) != 0 && (target._flags & TypeFlags.Intersection) != 0)
        {
            var targetTypes = target.Types();
            var intrinsicAttributes = _c.GetJsxType(JsxNames.IntrinsicAttributes, _errorNode);
            var intrinsicClassAttributes = _c.GetJsxType(JsxNames.IntrinsicClassAttributes, _errorNode);
            if (!_c.IsErrorType(intrinsicAttributes) && !_c.IsErrorType(intrinsicClassAttributes) && (Array.IndexOf(targetTypes, intrinsicAttributes) >= 0 || Array.IndexOf(targetTypes, intrinsicClassAttributes) >= 0))
            {
                return;
            }
        }
        else if ((originalTarget._flags & TypeFlags.Intersection) != 0 && (originalTarget._objectFlags & ObjectFlags.IsNeverIntersection) != 0)
        {
            var message = Diagnostics.The_intersection_0_was_reduced_to_never_because_property_1_has_conflicting_types_in_some_constituents;
            var prop = Core.Find(_c.GetPropertiesOfUnionOrIntersectionType(originalTarget), _c.IsDiscriminantWithNeverType);
            if (prop == null)
            {
                message = Diagnostics.The_intersection_0_was_reduced_to_never_because_property_1_exists_in_multiple_constituents_and_is_private_in_some;
                prop = Core.Find(_c.GetPropertiesOfUnionOrIntersectionType(originalTarget), IsConflictingPrivateProperty);
            }
            if (prop != null)
            {
                ReportError(message, _c.TypeToStringExWorker(originalTarget, null /*enclosingDeclaration*/, TypeFormatFlags.NoTypeReduction, null), _c.SymbolToStringWorker(prop));
            }
        }
        ReportRelationError(headMessage, source, target);
        if ((source._flags & TypeFlags.TypeParameter) != 0 && source._symbol != null && source._symbol.Declarations!.Length != 0 && _c.GetConstraintOfType(source) == null)
        {
            var syntheticParam = _c.CloneTypeParameter(source);
            syntheticParam.AsTypeParameter()!._constraint = _c.InstantiateType(target, new SimpleTypeMapper(source, syntheticParam));
            if (_c.HasNonCircularBaseConstraint(syntheticParam))
            {
                var targetConstraintString = _c.TypeToString(target);
                _relatedInfo = [.. _relatedInfo, NewDiagnosticForNode(source._symbol.Declarations[0], Diagnostics.This_type_parameter_might_need_an_extends_0_constraint, targetConstraintString)];
            }
        }
    }

    internal void ReportRelationError(Message? message, Type source, Type target)
    {
        var (sourceType, targetType) = _c.GetTypeNamesForErrorDisplay(source, target);
        var generalizedSource = source;
        var generalizedSourceType = sourceType;
        // Don't generalize on 'never' - we really want the original type
        // to be displayed for use-cases like 'assertNever'.
        if ((target._flags & TypeFlags.Never) == 0 && IsLiteralType(source) && !_c.TypeCouldHaveTopLevelSingletonTypes(target))
        {
            generalizedSource = _c.GetBaseTypeOfLiteralTypeWorker(source);
            generalizedSourceType = _c.GetTypeNameForErrorDisplay(generalizedSource);
        }
        // If `target` is of indexed access type (and `source` it is not), we use the object type of `target` for better error reporting
        TypeFlags targetFlags;
        if ((target._flags & TypeFlags.IndexedAccess) != 0 && (source._flags & TypeFlags.IndexedAccess) == 0)
        {
            targetFlags = target.AsIndexedAccessType()!._objectType!._flags;
        }
        else
        {
            targetFlags = target._flags;
        }
        if ((targetFlags & TypeFlags.TypeParameter) != 0 && target != _c._markerSuperTypeForCheck && target != _c._markerSubTypeForCheck)
        {
            var constraint = _c.GetBaseConstraintOfTypeWorker(target);
            if (IsDistributedTypeParameter(target) && _c.IsTypeAssignableToWorker(generalizedSource, target.AsTypeParameter()!._constraint!))
            {
                ReportError(Diagnostics.X_0_is_only_assignable_to_the_non_distributed_1_but_1_has_been_distributed_here, generalizedSourceType, targetType);
            }
            else if (constraint != null && _c.IsTypeAssignableToWorker(generalizedSource, constraint))
            {
                ReportError(Diagnostics.X_0_is_assignable_to_the_constraint_of_type_1_but_1_could_be_instantiated_with_a_different_subtype_of_constraint_2, generalizedSourceType, targetType, _c.TypeToString(constraint));
            }
            else if (constraint != null && _c.IsTypeAssignableToWorker(source, constraint))
            {
                ReportError(Diagnostics.X_0_is_assignable_to_the_constraint_of_type_1_but_1_could_be_instantiated_with_a_different_subtype_of_constraint_2, sourceType, targetType, _c.TypeToString(constraint));
            }
            else
            {
                _errorChain = null; // Only report this error once
                ReportError(Diagnostics.X_0_could_be_instantiated_with_an_arbitrary_type_which_could_be_unrelated_to_1, targetType, generalizedSourceType);
            }
        }
        if (message == null)
        {
            if (_relation == _c._comparableRelation)
            {
                message = Diagnostics.Type_0_is_not_comparable_to_type_1;
            }
            else if (sourceType == targetType)
            {
                message = Diagnostics.Type_0_is_not_assignable_to_type_1_Two_different_types_with_this_name_exist_but_they_are_unrelated;
            }
            else if (_c._exactOptionalPropertyTypes && (_c.GetExactOptionalUnassignableProperties(source, target) ?? []).Length != 0)
            {
                message = Diagnostics.Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_types_of_the_target_s_properties;
            }
            else
            {
                if ((source._flags & TypeFlags.StringLiteral) != 0 && (target._flags & TypeFlags.Union) != 0)
                {
                    var suggestedType = _c.GetSuggestedTypeForNonexistentStringLiteralType(source, target);
                    if (suggestedType != null)
                    {
                        ReportError(Diagnostics.Type_0_is_not_assignable_to_type_1_Did_you_mean_2, generalizedSourceType, targetType, _c.TypeToString(suggestedType));
                        return;
                    }
                }
                message = Diagnostics.Type_0_is_not_assignable_to_type_1;
            }
        }
        else if (message == Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1 && _c._exactOptionalPropertyTypes && (_c.GetExactOptionalUnassignableProperties(source, target) ?? []).Length > 0)
        {
            message = Diagnostics.Argument_of_type_0_is_not_assignable_to_parameter_of_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_types_of_the_target_s_properties;
        }
        var chainMessage = GetChainMessage(0);
        // Suppress if next message is an excess property error
        if (chainMessage == Diagnostics.Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1 ||
            chainMessage == Diagnostics.Object_literal_may_only_specify_known_properties_but_0_does_not_exist_in_type_1_Did_you_mean_to_write_2)
        {
            return;
        }
        // Suppress if next message is an excessive complexity/stack depth message for source and target or a readonly
        // vs. mutable error for source and target
        if (chainMessage == Diagnostics.Excessive_complexity_comparing_types_0_and_1 ||
            chainMessage == Diagnostics.The_type_0_is_readonly_and_cannot_be_assigned_to_the_mutable_type_1)
        {
            if (ChainArgsMatch(generalizedSourceType, targetType))
            {
                return;
            }
        }
        // Suppress if next message is a missing property message for source and target and we're not
        // reporting on conversion or interface implementation
        if (chainMessage == Diagnostics.Property_0_is_missing_in_type_1_but_required_in_type_2)
        {
            if (!IsConversionOrInterfaceImplementationMessage(message) && ChainArgsMatch(null, generalizedSourceType, targetType))
            {
                return;
            }
        }
        if (chainMessage == Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2_and_3_more ||
            chainMessage == Diagnostics.Type_0_is_missing_the_following_properties_from_type_1_Colon_2)
        {
            if (!IsConversionOrInterfaceImplementationMessage(message) && ChainArgsMatch(generalizedSourceType, targetType))
            {
                return;
            }
        }
        ReportError(message, generalizedSourceType, targetType);
    }

    internal void ReportError(Message message, params object?[] args)
    {
        if (message == Diagnostics.Types_of_property_0_are_incompatible)
        {
            // Suppress if next message is an excess property error
            var chainMessage0 = GetChainMessage(0);
            if (chainMessage0 == Diagnostics.Object_literal_may_only_specify_known_properties_and_0_does_not_exist_in_type_1 ||
                chainMessage0 == Diagnostics.Object_literal_may_only_specify_known_properties_but_0_does_not_exist_in_type_1_Did_you_mean_to_write_2)
            {
                return;
            }
            // Transform a property incompatibility message for property 'x' followed by some elaboration message
            // followed by a signature return type incompatibility message into a single return type incompatibility
            // message for 'x()' or 'x(...)'
            var arg = "";
            var chainMessage1 = GetChainMessage(1);
            if (chainMessage1 == Diagnostics.Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1)
            {
                arg = GetPropertyNameArg(args[0]) + "()";
            }
            else if (chainMessage1 == Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1)
            {
                arg = "new " + GetPropertyNameArg(args[0]) + "()";
            }
            else if (chainMessage1 == Diagnostics.Call_signature_return_types_0_and_1_are_incompatible)
            {
                arg = GetPropertyNameArg(args[0]) + "(...)";
            }
            else if (chainMessage1 == Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible)
            {
                arg = "new " + GetPropertyNameArg(args[0]) + "(...)";
            }
            if (arg != "")
            {
                message = Diagnostics.The_types_returned_by_0_are_incompatible_between_these_types;
                args[0] = arg;
                _errorChain = _errorChain!._next!._next;
            }
            // Transform a property incompatibility message for property 'x' followed by some elaboration message
            // followed by a property incompatibility message for property 'y' into a single property incompatibility
            // message for 'x.y'
            chainMessage1 = GetChainMessage(1);
            if (chainMessage1 == Diagnostics.Types_of_property_0_are_incompatible ||
                chainMessage1 == Diagnostics.The_types_of_0_are_incompatible_between_these_types ||
                chainMessage1 == Diagnostics.The_types_returned_by_0_are_incompatible_between_these_types)
            {
                var head = GetPropertyNameArg(args[0]);
                var tail = GetPropertyNameArg(_errorChain!._next!._args[0]);
                var dottedArg = AddToDottedName(head, tail);
                _errorChain = _errorChain._next!._next;
                if (message == Diagnostics.Types_of_property_0_are_incompatible)
                {
                    message = Diagnostics.The_types_of_0_are_incompatible_between_these_types;
                }
                ReportError(message, dottedArg);
                return;
            }
        }
        _errorChain = new ErrorChain { _next = _errorChain, _message = message, _args = args };
    }

    internal static string AddToDottedName(string head, string tail)
    {
        if (head.StartsWith("new ", StringComparison.Ordinal))
        {
            head = "(" + head + ")";
        }
        var pos = 0;
        while (true)
        {
            if (tail.AsSpan(pos).StartsWith("("))
            {
                pos++;
            }
            else if (tail.AsSpan(pos).StartsWith("new "))
            {
                pos += 4;
            }
            else
            {
                break;
            }
        }
        var prefix = tail[..pos];
        var suffix = tail[pos..];
        if (suffix.StartsWith("[", StringComparison.Ordinal))
        {
            return prefix + head + suffix;
        }
        return prefix + head + "." + suffix;
    }

    internal Message? GetChainMessage(int index)
    {
        var e = _errorChain;
        while (true)
        {
            if (e == null)
            {
                return null;
            }
            if (index == 0)
            {
                return e._message;
            }
            e = e._next;
            index--;
        }
    }

    // Return true if the arguments of the first entry on the error chain match the
    // given arguments (where nil acts as a wildcard).
    internal bool ChainArgsMatch(params object?[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a != null && !Equals(a, _errorChain!._args[i]))
            {
                return false;
            }
        }
        return true;
    }

    internal static string GetPropertyNameArg(object? arg)
    {
        var s = (string)arg!;
        if (s.Length != 0 && (s[0] == '"' || s[0] == '\'' || s[0] == '`'))
        {
            return "[" + s + "]";
        }
        return s;
    }

    internal static bool IsConversionOrInterfaceImplementationMessage(Message? message)
    {
        return message == Diagnostics.Class_0_incorrectly_implements_interface_1 ||
            message == Diagnostics.Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass ||
            message == Diagnostics.Conversion_of_type_0_to_type_1_may_be_a_mistake_because_neither_type_sufficiently_overlaps_with_the_other_If_this_was_intentional_convert_the_expression_to_unknown_first ||
            message == Diagnostics.Its_instance_type_0_is_not_a_valid_JSX_element ||
            message == Diagnostics.Its_return_type_0_is_not_a_valid_JSX_element ||
            message == Diagnostics.Its_element_type_0_is_not_a_valid_JSX_element;
    }

    internal static int ChainDepth(ErrorChain? chain)
    {
        var depth = 0;
        while (chain != null)
        {
            depth++;
            chain = chain._next;
        }
        return depth;
    }

    internal void TraceUnionsOrIntersectionsTooLarge(Type source, Type target)
    {
        var tr = _c._tracer;
        if (tr == null)
        {
            return;
        }
        if ((source._flags & TypeFlags.UnionOrIntersection) != 0 && (target._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            if ((source._objectFlags & target._objectFlags & ObjectFlags.PrimitiveUnion) != 0)
            {
                // There's a fast path for comparing primitive unions
                return;
            }
            var sourceSize = source.Types().Length;
            var targetSize = target.Types().Length;
            if (sourceSize * targetSize > 1_000_000)
            {
                tr.Instant(Phase.CheckTypes, "traceUnionsOrIntersectionsTooLarge_DepthLimit", new Dictionary<string, object?>
                {
                    ["sourceId"] = source._id,
                    ["sourceSize"] = sourceSize,
                    ["targetId"] = target._id,
                    ["targetSize"] = targetSize,
                });
            }
        }
    }
}

public sealed partial class Checker
{
    // Return true if the type was inferred from
    //   - an object literal, object type literal, enum type, or a value module and has no call or construct signatures, or
    //   - a JS expando object literal or a rest type, or
    //   - a reverse mapped type with a source for which one of the above is true.
    internal bool IsObjectTypeWithInferableIndex(Type t)
    {
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return Core.Every(t.Types(), IsObjectTypeWithInferableIndex);
        }
        return t._symbol != null &&
            (t._symbol.Flags & (SymbolFlags.ObjectLiteral | SymbolFlags.TypeLiteral | SymbolFlags.Enum | SymbolFlags.ValueModule)) != 0 &&
            (t._symbol.Flags & SymbolFlags.Class) == 0 && !TypeHasCallOrConstructSignaturesWorker(t) ||
            (t._objectFlags & (ObjectFlags.JSLiteral | ObjectFlags.ObjectRestType)) != 0 ||
            (t._objectFlags & ObjectFlags.ReverseMapped) != 0 && IsObjectTypeWithInferableIndex(t.AsReverseMappedType()!._source!);
    }

    /// <summary>
    /// An object type S is considered to be derived from an object type T if
    /// S is a union type and every constituent of S is derived from T,
    /// T is a union type and S is derived from at least one constituent of T, or
    /// S is an intersection type and some constituent of S is derived from T, or
    /// S is a type variable with a base constraint that is derived from T, or
    /// T is {} and S is an object-like type (ensuring {} is less derived than Object), or
    /// T is one of the global types Object and Function and S is a subtype of T, or
    /// T occurs directly or indirectly in an 'extends' clause of S.
    /// Note that this check ignores type parameters and only considers the
    /// inheritance hierarchy.
    /// </summary>
    internal bool IsTypeDerivedFrom(Type source, Type target)
    {
        if ((source._flags & TypeFlags.Union) != 0)
        {
            return Core.Every(source.AsUnionType()!._types, t => IsTypeDerivedFrom(t, target));
        }
        if ((target._flags & TypeFlags.Union) != 0)
        {
            return Core.Some(target.AsUnionType()!._types, t => IsTypeDerivedFrom(source, t));
        }
        if ((source._flags & TypeFlags.Intersection) != 0)
        {
            return Core.Some(source.AsIntersectionType()!._types, t => IsTypeDerivedFrom(t, target));
        }
        if ((source._flags & TypeFlags.InstantiableNonPrimitive) != 0)
        {
            var constraint = GetBaseConstraintOfTypeWorker(source);
            if (constraint == null)
            {
                constraint = _unknownType;
            }
            return IsTypeDerivedFrom(constraint, target);
        }
        if (IsEmptyAnonymousObjectType(target))
        {
            return (source._flags & (TypeFlags.Object | TypeFlags.NonPrimitive)) != 0;
        }
        if (target == _globalObjectType)
        {
            return (source._flags & (TypeFlags.Object | TypeFlags.NonPrimitive)) != 0 && !IsEmptyAnonymousObjectType(source);
        }
        if (target == _globalFunctionType)
        {
            return (source._flags & TypeFlags.Object) != 0 && IsFunctionObjectType(source);
        }
        return HasBaseType(source, GetTargetType(target)) || (IsArrayTypeWorker(target) && !IsReadonlyArrayType(target) && IsTypeDerivedFrom(source, _globalReadonlyArrayType));
    }

    internal bool IsDistributionDependent(ConditionalRoot root)
    {
        return root._isDistributive && (IsTypeParameterPossiblyReferenced(root._checkType!, root._node!.TrueType!) || IsTypeParameterPossiblyReferenced(root._checkType!, root._node.FalseType!));
    }
}
