// ported from: internal/checker/inference.go:699-1003
// （inferFromObjectTypes / inferFromProperties / inferFromSignatures / inferFromSignature /
//   applyToParameterTypes / applyToReturnTypes / inferFromIndexTypes / inferToMappedType）
//
// 与 Go 的偏离（逐条）：
// 1) `c.applyToParameterTypes(s, t, func(s, t *Type) { … })`（Go 的闭包回调）→ `Action<Type, Type>` 参数；调用点
//    （含 checker.go:7815/19824 的其它片）同样传 lambda。
// 2) Go 的 `core.OrElse(c.inferToMappedType(...), result)` 会**先求值左侧再选值**（OrElse 是普通函数）；C# 里写成
//    `result = InferToMappedType(...) || result;`（左操作数先求值，短路发生在求值之后，语义一致；不要反写成
//    `result || InferToMappedType(...)`，那会短路掉副作用）。
// 3) `c.patternForType[source] != nil`（Go 的 map 读缺键返回 nil）→ `_patternForType.ContainsKey(source)`
//    （`Dictionary` 索引器会抛 KeyNotFoundException；该 map 从不写入 nil 值）。
// 4) `for i := range N` → `for (var i = 0; i < N; i++)`；`min`/`max` → `Math.Min`/`Math.Max`。
// 5) `c.getPropertiesOfType(source)` 等命中 `Worker` 改名（artifacts/checker-method-collisions.md）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal void InferFromObjectTypes(InferenceState n, Type source, Type target)
    {
        if ((source._objectFlags & ObjectFlags.Reference) != 0 && (target._objectFlags & ObjectFlags.Reference) != 0 && (source.Target() == target.Target() || IsArrayTypeWorker(source) && IsArrayTypeWorker(target)))
        {
            // If source and target are references to the same generic type, infer from type arguments
            InferFromTypeArguments(n, GetTypeArgumentsWorker(source), GetTypeArgumentsWorker(target), GetVariances(source.Target()!));
            return;
        }
        if (IsGenericMappedType(source) && IsGenericMappedType(target))
        {
            InferFromGenericMappedTypes(n, source, target);
        }
        if ((target._objectFlags & ObjectFlags.Mapped) != 0 && target.AsMappedType()!._declaration!.NameType == null)
        {
            var constraintType = GetConstraintTypeFromMappedType(target);
            if (InferToMappedType(n, source, target, constraintType))
            {
                return;
            }
        }
        // Infer from the members of source and target only if the two types are possibly related
        if (TypesDefinitelyUnrelated(source, target))
        {
            return;
        }
        if (IsArrayOrTupleType(source))
        {
            if (IsTupleType(target))
            {
                var sourceArity = GetTypeReferenceArity(source);
                var targetArity = GetTypeReferenceArity(target);
                var elementTypes = GetTypeArgumentsWorker(target);
                var elementInfos = target.TargetTupleType()!._elementInfos!;
                // When source and target are tuple types with the same structure (fixed, variadic, and rest are matched
                // to the same kind in each position), simply infer between the element types.
                if (IsTupleType(source) && IsTupleTypeStructureMatching(source, target))
                {
                    for (var i = 0; i < targetArity; i++)
                    {
                        InferFromTypes(n, GetTypeArgumentsWorker(source)[i], elementTypes[i]);
                    }
                    return;
                }
                var startLength = 0;
                var endLength = 0;
                if (IsTupleType(source))
                {
                    startLength = Math.Min(source.TargetTupleType()!._fixedLength, target.TargetTupleType()!._fixedLength);
                    if ((target.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0)
                    {
                        endLength = Math.Min(GetEndElementCount(source.TargetTupleType()!, ElementFlags.Fixed), GetEndElementCount(target.TargetTupleType()!, ElementFlags.Fixed));
                    }
                }
                // Infer between starting fixed elements.
                for (var i = 0; i < startLength; i++)
                {
                    InferFromTypes(n, GetTypeArgumentsWorker(source)[i], elementTypes[i]);
                }
                if (!IsTupleType(source) || (sourceArity - startLength - endLength == 1 && (source.TargetTupleType()!._elementInfos![startLength]._flags & ElementFlags.Rest) != 0))
                {
                    // Single rest element remains in source, infer from that to every element in target
                    var restType = GetTypeArgumentsWorker(source)[startLength];
                    for (var i = startLength; i < targetArity - endLength; i++)
                    {
                        var t = restType;
                        if ((elementInfos[i]._flags & ElementFlags.Variadic) != 0)
                        {
                            t = CreateArrayType(t);
                        }
                        InferFromTypes(n, t, elementTypes[i]);
                    }
                }
                else
                {
                    var middleLength = targetArity - startLength - endLength;
                    if (middleLength == 2)
                    {
                        if ((elementInfos[startLength]._flags & elementInfos[startLength + 1]._flags & ElementFlags.Variadic) != 0)
                        {
                            // Middle of target is [...T, ...U] and source is tuple type
                            var targetInfo = GetInferenceInfoForType(n, elementTypes[startLength]);
                            if (targetInfo != null && targetInfo._impliedArity >= 0)
                            {
                                // Infer slices from source based on implied arity of T.
                                InferFromTypes(n, SliceTupleType(source, startLength, endLength + sourceArity - targetInfo._impliedArity), elementTypes[startLength]);
                                InferFromTypes(n, SliceTupleType(source, startLength + targetInfo._impliedArity, endLength), elementTypes[startLength + 1]);
                            }
                        }
                        else if ((elementInfos[startLength]._flags & ElementFlags.Variadic) != 0 && (elementInfos[startLength + 1]._flags & ElementFlags.Rest) != 0)
                        {
                            // Middle of target is [...T, ...rest] and source is tuple type
                            // if T is constrained by a fixed-size tuple we might be able to use its arity to infer T
                            var info = GetInferenceInfoForType(n, elementTypes[startLength]);
                            if (info != null)
                            {
                                var constraint = GetBaseConstraintOfTypeWorker(info._typeParameter!);
                                if (constraint != null && IsTupleType(constraint) && (constraint.TargetTupleType()!._combinedFlags & ElementFlags.Variable) == 0)
                                {
                                    var impliedArity = constraint.TargetTupleType()!._fixedLength;
                                    InferFromTypes(n, SliceTupleType(source, startLength, sourceArity - (startLength + impliedArity)), elementTypes[startLength]);
                                    var restType2 = GetElementTypeOfSliceOfTupleType(source, startLength + impliedArity, endLength, false, false);
                                    if (restType2 != null)
                                    {
                                        InferFromTypes(n, restType2, elementTypes[startLength + 1]);
                                    }
                                }
                            }
                        }
                        else if ((elementInfos[startLength]._flags & ElementFlags.Rest) != 0 && (elementInfos[startLength + 1]._flags & ElementFlags.Variadic) != 0)
                        {
                            // Middle of target is [...rest, ...T] and source is tuple type
                            // if T is constrained by a fixed-size tuple we might be able to use its arity to infer T
                            var info = GetInferenceInfoForType(n, elementTypes[startLength + 1]);
                            if (info != null)
                            {
                                var constraint = GetBaseConstraintOfTypeWorker(info._typeParameter!);
                                if (constraint != null && IsTupleType(constraint) && (constraint.TargetTupleType()!._combinedFlags & ElementFlags.Variable) == 0)
                                {
                                    var impliedArity = constraint.TargetTupleType()!._fixedLength;
                                    var endIndex = sourceArity - GetEndElementCount(target.TargetTupleType()!, ElementFlags.Fixed);
                                    var startIndex = endIndex - impliedArity;
                                    if (startIndex >= startLength)
                                    {
                                        var trailingSlice = CreateTupleTypeEx(GetTypeArgumentsWorker(source)[startIndex..endIndex], source.TargetTupleType()!._elementInfos![startIndex..endIndex], false /*readonly*/);
                                        var restType3 = GetElementTypeOfSliceOfTupleType(source, startLength, endLength + impliedArity, false, false);
                                        if (restType3 != null)
                                        {
                                            InferFromTypes(n, restType3, elementTypes[startLength]);
                                        }
                                        InferFromTypes(n, trailingSlice, elementTypes[startLength + 1]);
                                    }
                                }
                            }
                        }
                    }
                    else if (middleLength == 1 && (elementInfos[startLength]._flags & ElementFlags.Variadic) != 0)
                    {
                        // Middle of target is exactly one variadic element. Infer the slice between the fixed parts in the source.
                        // If target ends in optional element(s), make a lower priority a speculative inference.
                        var priority = IfElse((elementInfos[targetArity - 1]._flags & ElementFlags.Optional) != 0, InferencePriority.SpeculativeTuple, InferencePriority.None);
                        var sourceSlice = SliceTupleType(source, startLength, endLength);
                        InferWithPriority(n, sourceSlice, elementTypes[startLength], priority);
                    }
                    else if (middleLength == 1 && (elementInfos[startLength]._flags & ElementFlags.Rest) != 0)
                    {
                        // Middle of target is exactly one rest element. If middle of source is not empty, infer union of middle element types.
                        var restType4 = GetElementTypeOfSliceOfTupleType(source, startLength, endLength, false, false);
                        if (restType4 != null)
                        {
                            InferFromTypes(n, restType4, elementTypes[startLength]);
                        }
                    }
                }
                // Infer between ending fixed elements
                for (var i = 0; i < endLength; i++)
                {
                    InferFromTypes(n, GetTypeArgumentsWorker(source)[sourceArity - i - 1], elementTypes[targetArity - i - 1]);
                }
                return;
            }
            if (IsArrayTypeWorker(target))
            {
                InferFromIndexTypes(n, source, target);
                return;
            }
        }
        InferFromProperties(n, source, target);
        InferFromSignatures(n, source, target, SignatureKind.Call);
        InferFromSignatures(n, source, target, SignatureKind.Construct);
        InferFromIndexTypes(n, source, target);
    }

    internal void InferFromProperties(InferenceState n, Type source, Type target)
    {
        var properties = GetPropertiesOfObjectType(target) ?? [];
        foreach (var targetProp in properties)
        {
            var sourceProp = GetPropertyOfTypeWorker(source, targetProp.Name);
            if (sourceProp != null && !Some(sourceProp.Declarations, IsSkipDirectInferenceNode))
            {
                InferFromTypes(n, RemoveMissingType(GetTypeOfSymbolWorker(sourceProp), (sourceProp.Flags & SymbolFlags.Optional) != 0), RemoveMissingType(GetTypeOfSymbolWorker(targetProp), (targetProp.Flags & SymbolFlags.Optional) != 0));
            }
        }
    }

    internal void InferFromSignatures(InferenceState n, Type source, Type target, SignatureKind kind)
    {
        var sourceSignatures = GetSignaturesOfTypeWorker(source, kind) ?? [];
        var sourceLen = sourceSignatures.Length;
        if (sourceLen > 0)
        {
            // We match source and target signatures from the bottom up, and if the source has fewer signatures
            // than the target, we infer from the first source signature to the excess target signatures.
            var targetSignatures = GetSignaturesOfTypeWorker(target, kind) ?? [];
            var targetLen = targetSignatures.Length;
            for (var i = 0; i < targetLen; i++)
            {
                var sourceIndex = Math.Max(sourceLen - targetLen + i, 0);
                InferFromSignature(n, GetBaseSignature(sourceSignatures[sourceIndex]), GetErasedSignature(targetSignatures[i]));
            }
        }
    }

    internal void InferFromSignature(InferenceState n, Signature source, Signature target)
    {
        if ((source._flags & SignatureFlags.IsNonInferrable) == 0)
        {
            var saveBivariant = n._bivariant;
            var kind = Kind.Unknown;
            if (target._declaration != null)
            {
                kind = target._declaration.Kind;
            }
            // Once we descend into a bivariant signature we remain bivariant for all nested inferences
            n._bivariant = n._bivariant || kind == Kind.MethodDeclaration || kind == Kind.MethodSignature || kind == Kind.Constructor;
            ApplyToParameterTypes(source, target, (s, t) => InferFromContravariantTypesIfStrictFunctionTypes(n, s, t));
            n._bivariant = saveBivariant;
        }
        ApplyToReturnTypes(source, target, (s, t) => InferFromTypes(n, s, t));
    }

    internal void ApplyToParameterTypes(Signature source, Signature target, Action<Type, Type> callback)
    {
        var sourceCount = GetParameterCount(source);
        var targetCount = GetParameterCount(target);
        var sourceRestType = GetEffectiveRestType(source);
        var targetRestType = GetEffectiveRestType(target);
        var targetNonRestCount = targetCount;
        if (targetRestType != null)
        {
            targetNonRestCount--;
        }
        var paramCount = targetNonRestCount;
        if (sourceRestType == null)
        {
            paramCount = Math.Min(sourceCount, targetNonRestCount);
        }
        var sourceThisType = GetThisTypeOfSignature(source);
        if (sourceThisType != null)
        {
            var targetThisType = GetThisTypeOfSignature(target);
            if (targetThisType != null)
            {
                callback(sourceThisType, targetThisType);
            }
        }
        for (var i = 0; i < paramCount; i++)
        {
            callback(GetTypeAtPositionWorker(source, i)!, GetTypeAtPositionWorker(target, i)!);
        }
        if (targetRestType != null)
        {
            callback(GetRestTypeAtPosition(source, paramCount, IsConstTypeVariable(targetRestType, 0) && !SomeType(targetRestType, t => IsMutableArrayLikeType(t)) /*readonly*/), targetRestType);
        }
    }

    internal void ApplyToReturnTypes(Signature source, Signature target, Action<Type, Type> callback)
    {
        var targetTypePredicate = GetTypePredicateOfSignatureWorker(target);
        if (targetTypePredicate != null)
        {
            var sourceTypePredicate = GetTypePredicateOfSignatureWorker(source);
            if (sourceTypePredicate != null && TypePredicateKindsMatch(sourceTypePredicate, targetTypePredicate) && sourceTypePredicate._t != null && targetTypePredicate._t != null)
            {
                callback(sourceTypePredicate._t, targetTypePredicate._t);
                return;
            }
        }
        var targetReturnType = GetReturnTypeOfSignatureWorker(target);
        if (_couldContainTypeVariables(targetReturnType))
        {
            callback(GetReturnTypeOfSignatureWorker(source), targetReturnType);
        }
    }

    internal void InferFromIndexTypes(InferenceState n, Type source, Type target)
    {
        // Inferences across mapped type index signatures are pretty much the same a inferences to homomorphic variables
        var priority = InferencePriority.None;
        if ((source._objectFlags & target._objectFlags & ObjectFlags.Mapped) != 0)
        {
            priority = InferencePriority.HomomorphicMappedType;
        }
        var indexInfos = GetIndexInfosOfTypeWorker(target) ?? [];
        if (IsObjectTypeWithInferableIndex(source))
        {
            foreach (var targetInfo in indexInfos)
            {
                List<Type> propTypes = [];
                foreach (var prop in GetPropertiesOfTypeWorker(source) ?? [])
                {
                    if (IsApplicableIndexType(GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique, false), targetInfo._keyType!))
                    {
                        var propType = GetTypeOfSymbolWorker(prop);
                        if ((prop.Flags & SymbolFlags.Optional) != 0)
                        {
                            propType = RemoveMissingOrUndefinedTypeWorker(propType);
                        }
                        propTypes.Add(propType);
                    }
                }
                foreach (var info in GetIndexInfosOfTypeWorker(source) ?? [])
                {
                    if (IsApplicableIndexType(info._keyType!, targetInfo._keyType!))
                    {
                        propTypes.Add(info._valueType!);
                    }
                }
                if (propTypes.Count != 0)
                {
                    InferWithPriority(n, GetUnionTypeWorker(propTypes.ToArray()), targetInfo._valueType!, priority);
                }
            }
        }
        foreach (var targetInfo in indexInfos)
        {
            var sourceInfo = GetApplicableIndexInfo(source, targetInfo._keyType!);
            if (sourceInfo != null)
            {
                InferWithPriority(n, sourceInfo._valueType!, targetInfo._valueType!, priority);
            }
        }
    }

    internal bool InferToMappedType(InferenceState n, Type source, Type target, Type constraintType)
    {
        if ((constraintType._flags & TypeFlags.Union) != 0 || (constraintType._flags & TypeFlags.Intersection) != 0)
        {
            var result = false;
            foreach (var t in constraintType.Types())
            {
                result = InferToMappedType(n, source, target, t) || result;
            }
            return result;
        }
        if ((constraintType._flags & TypeFlags.Index) != 0)
        {
            // We're inferring from some source type S to a homomorphic mapped type { [P in keyof T]: X },
            // where T is a type variable. Use inferTypeForHomomorphicMappedType to infer a suitable source
            // type and then make a secondary inference from that type to T. We make a secondary inference
            // such that direct inferences to T get priority over inferences to Partial<T>, for example.
            var inference = GetInferenceInfoForType(n, constraintType.AsIndexType()!._target!);
            if (inference != null && !inference._isFixed && !IsFromInferenceBlockedSource(source))
            {
                var inferredType = InferTypeForHomomorphicMappedType(source, target, constraintType);
                if (inferredType != null)
                {
                    // We assign a lower priority to inferences made from types containing non-inferrable
                    // types because we may only have a partial result (i.e. we may have failed to make
                    // reverse inferences for some properties).
                    InferWithPriority(n, inferredType, inference._typeParameter!, IfElse((source._objectFlags & ObjectFlags.NonInferrableType) != 0, InferencePriority.PartialHomomorphicMappedType, InferencePriority.HomomorphicMappedType));
                }
            }
            return true;
        }
        if ((constraintType._flags & TypeFlags.TypeParameter) != 0)
        {
            // We're inferring from some source type S to a mapped type { [P in K]: X }, where K is a type
            // parameter. First infer from 'keyof S' to K.
            InferWithPriority(n, GetIndexTypeEx(source, IfElse(_patternForType.ContainsKey(source), IndexFlags.NoIndexSignatures, IndexFlags.None)), constraintType, InferencePriority.MappedTypeConstraint);
            // If K is constrained to a type C, also infer to C. Thus, for a mapped type { [P in K]: X },
            // where K extends keyof T, we make the same inferences as for a homomorphic mapped type
            // { [P in keyof T]: X }. This enables us to make meaningful inferences when the target is a
            // Pick<T, K>.
            var extendedConstraint = GetConstraintOfType(constraintType);
            if (extendedConstraint != null && InferToMappedType(n, source, target, extendedConstraint))
            {
                return true;
            }
            // If no inferences can be made to K's constraint, infer from a union of the property types
            // in the source to the template type X.
            var propTypes = Map(GetPropertiesOfTypeWorker(source), prop => GetTypeOfSymbolWorker(prop));
            var indexTypes = Map(GetIndexInfosOfTypeWorker(source), info => info != _enumNumberIndexInfo ? info._valueType! : _neverType);
            InferFromTypes(n, GetUnionTypeWorker(Concatenate(propTypes, indexTypes)), GetTemplateTypeFromMappedType(target));
            return true;
        }
        return false;
    }
}
