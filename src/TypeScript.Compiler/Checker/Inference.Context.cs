// ported from: internal/checker/inference.go:1251-1432
// （newInferenceContext / cloneInferenceContext / cloneInferredPartOfContext / newInferenceContextWorker /
//   addIntraExpressionInferenceSite / inferFromIntraExpressionSites / getInferredType / getInferredTypes /
//   getMapperFromContext / createOuterReturnMapper）
//
// 与 Go 的偏离（逐条）：
// 1) `newInferenceContext(typeParameters, signature, flags, compareTypes)` 的 `signature`/`compareTypes` 在 Go 里可为 nil
//    （checker.go:9766 传 nil）→ C# 可空参数（`Signature?`/`TypeComparer?`）；`compareTypes == nil` → `??=`。
// 2) Go 的 `n.inferences []*InferenceInfo` 在 C# 侧（Checker.Keys.cs:800）是 `List<InferenceInfo>` → `newInferenceContextWorker`
//    收 `List<InferenceInfo>`；`core.Map(xs, f)` → `List<T>.ConvertAll`，`core.Filter(xs, f)` → `List<T>.FindAll`，
//    `core.Some/Every` → `List<T>.Exists/TrueForAll`（`Core.*` 的签名是 `T[]?`，List 传不进去）。
// 3) `cloneInferenceContext(n, extraFlags)`（Go 的 `n == nil` 分支，调用方也允许传 nil）→
//    `CloneInferenceContext(InferenceContext? n, …)` 返回 `InferenceContext?`。
// 4) `n.intraExpressionInferenceSites = append(...)`/`= nil`（Go slice）→ `List<IntraExpressionInferenceSite>` 的
//    `Add`/`Clear`；`IntraExpressionInferenceSite` 是 sealed class（有 2 参构造器）→ 直接用构造器。
// 5) `InferenceContext`/`InferenceInfo`/`IntraExpressionInferenceSite` 属 checker.go（C1）分片，字段按 `_camelCase` 引用；
//    `InferenceInfo._typeParameter` 是可空字段（Go 的 `*Type`），非空处补 `!`。
// 6) `n.compareTypes(...)`（`TypeComparer?` 字段，非空不变量）→ `n._compareTypes!(...)`；`c.getContextualType` 命中
//    Worker 改名（`GetContextualTypeWorker`，artifacts/checker-method-collisions.md）。
// 7) `new Type[n.inferences.Length]`（Go `make([]*Type, n)`）→ `new Type[n._inferences.Count]`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal InferenceContext NewInferenceContext(Type[]? typeParameters, Signature? signature, InferenceFlags flags, TypeComparer? compareTypes)
    {
        if (compareTypes == null)
        {
            compareTypes = _compareTypesAssignable;
        }
        // Go: core.Map(typeParameters, newInferenceInfo) —— 逐元素建 List（`Core.Map` 返回数组，喂不进 List 字段）。
        var inferences = new List<InferenceInfo>(typeParameters?.Length ?? 0);
        if (typeParameters != null)
        {
            foreach (var typeParameter in typeParameters)
            {
                inferences.Add(NewInferenceInfo(typeParameter));
            }
        }
        return NewInferenceContextWorker(inferences, signature, flags, compareTypes);
    }

    internal InferenceContext? CloneInferenceContext(InferenceContext? n, InferenceFlags extraFlags)
    {
        if (n == null)
        {
            return null;
        }
        return NewInferenceContextWorker(n._inferences.ConvertAll(i => CloneInferenceInfo(i)), n._signature, n._flags | extraFlags, n._compareTypes!);
    }

    internal InferenceContext? CloneInferredPartOfContext(InferenceContext n)
    {
        var inferences = n._inferences.FindAll(i => HasInferenceCandidates(i));
        if (inferences.Count == 0)
        {
            return null;
        }
        return NewInferenceContextWorker(inferences.ConvertAll(i => CloneInferenceInfo(i)), n._signature, n._flags, n._compareTypes!);
    }

    internal InferenceContext NewInferenceContextWorker(List<InferenceInfo> inferences, Signature? signature, InferenceFlags flags, TypeComparer compareTypes)
    {
        var n = new InferenceContext
        {
            _inferences = inferences,
            _signature = signature,
            _flags = flags,
            _compareTypes = compareTypes,
        };
        n._mapper = NewInferenceTypeMapper(n, true /*fixing*/);
        n._nonFixingMapper = NewInferenceTypeMapper(n, false /*fixing*/);
        return n;
    }

    internal void AddIntraExpressionInferenceSite(InferenceContext n, Node node, Type t)
    {
        n._intraExpressionInferenceSites.Add(new IntraExpressionInferenceSite(node, t));
    }

    // We collect intra-expression inference sites within object and array literals to handle cases where
    // inferred types flow between context sensitive element expressions. For example:
    //
    //	declare function foo<T>(arg: [(n: number) => T, (x: T) => void]): void;
    //	foo([_a => 0, n => n.toFixed()]);
    //
    // Above, both arrow functions in the tuple argument are context sensitive, thus both are omitted from the
    // pass that collects inferences from the non-context sensitive parts of the arguments. In the subsequent
    // pass where nothing is omitted, we need to commit to an inference for T in order to contextually type the
    // parameter in the second arrow function, but we want to first infer from the return type of the first
    // arrow function. This happens automatically when the arrow functions are discrete arguments (because we
    // infer from each argument before processing the next), but when the arrow functions are elements of an
    // object or array literal, we need to perform intra-expression inferences early.
    internal void InferFromIntraExpressionSites(InferenceContext n)
    {
        foreach (var site in n._intraExpressionInferenceSites)
        {
            Type? contextualType;
            if (IsMethodDeclaration(site._node!))
            {
                contextualType = GetContextualTypeForObjectLiteralMethod(site._node!, ContextFlags.NoConstraints);
            }
            else
            {
                contextualType = GetContextualTypeWorker(site._node!, ContextFlags.NoConstraints);
            }
            if (contextualType != null)
            {
                InferTypes(n._inferences, site._t!, contextualType, InferencePriority.None, false);
            }
        }
        n._intraExpressionInferenceSites.Clear();
    }

    internal Type GetInferredType(InferenceContext n, int index)
    {
        var inference = n._inferences[index];
        if (inference._inferredType == null)
        {
            if (inference._typeParameter == _errorType)
            {
                return inference._typeParameter!;
            }
            Type? inferredType = null;
            Type? fallbackType = null;
            if (n._signature != null)
            {
                Type? inferredCovariantType = null;
                if (inference._candidates.Count != 0)
                {
                    inferredCovariantType = GetCovariantInference(inference, n._signature);
                }
                Type? inferredContravariantType = null;
                if (inference._contraCandidates.Count != 0)
                {
                    inferredContravariantType = GetContravariantInference(inference);
                }
                if (inferredCovariantType != null || inferredContravariantType != null)
                {
                    // If we have both co- and contra-variant inferences, we prefer the co-variant inference if it is not 'never',
                    // all co-variant inferences are assignable to it (i.e. it isn't one of a conflicting set of candidates), it is
                    // assignable to some contra-variant inference, and no other type parameter is constrained to this type parameter
                    // and has inferences that would conflict. Otherwise, we prefer the contra-variant inference.
                    // Similarly ignore co-variant `any` inference when both are available as almost everything is assignable to it
                    // and it would spoil the overall inference.
                    var preferCovariantType = inferredCovariantType != null && (inferredContravariantType == null
                        || (inferredCovariantType._flags & (TypeFlags.Never | TypeFlags.Any)) == 0
                            && inference._contraCandidates.Exists(t => IsTypeAssignableToWorker(inferredCovariantType, t))
                            && n._inferences.TrueForAll(other => (other != inference && GetConstraintOfTypeParameterWorker(other._typeParameter!) != inference._typeParameter)
                                || other._candidates.TrueForAll(t => IsTypeAssignableToWorker(t, inferredCovariantType))));
                    if (preferCovariantType)
                    {
                        inferredType = inferredCovariantType;
                        fallbackType = inferredContravariantType;
                    }
                    else
                    {
                        inferredType = inferredContravariantType;
                        fallbackType = inferredCovariantType;
                    }
                }
                else if ((n._flags & InferenceFlags.NoDefault) != 0)
                {
                    // We use silentNeverType as the wildcard that signals no inferences.
                    inferredType = _silentNeverType;
                }
                else
                {
                    // Infer either the default or the empty object type when no inferences were
                    // made. It is important to remember that in this case, inference still
                    // succeeds, meaning there is no error for not having inference candidates. An
                    // inference error only occurs when there are *conflicting* candidates, i.e.
                    // candidates with no common supertype.
                    var defaultType = GetDefaultFromTypeParameterWorker(inference._typeParameter!);
                    if (defaultType != null)
                    {
                        // Instantiate the default type. Any forward reference to a type
                        // parameter should be instantiated to the empty object type.
                        inferredType = InstantiateType(defaultType, MergeTypeMappers(NewBackreferenceMapper(n, index), n._nonFixingMapper!));
                    }
                }
            }
            else
            {
                inferredType = GetTypeFromInference(inference);
            }
            inference._inferredType = inferredType;
            if (inference._inferredType == null)
            {
                inference._inferredType = IfElse((n._flags & InferenceFlags.AnyDefault) != 0, _anyType, _unknownType);
            }
            var constraint = GetConstraintOfTypeParameterWorker(inference._typeParameter!);
            if (constraint != null)
            {
                var instantiatedConstraint = InstantiateType(constraint, n._nonFixingMapper!);
                if (inferredType != null && (n._flags & InferenceFlags.NoConstraintChecks) == 0)
                {
                    var constraintWithThis = GetTypeWithThisArgument(instantiatedConstraint, inferredType, false);
                    if (n._compareTypes!(inferredType, constraintWithThis, false) == Ternary.False)
                    {
                        Type? filteredByConstraint = null;
                        if (inference._priority == InferencePriority.ReturnType)
                        {
                            // If we have a pure return type inference, we may succeed by removing constituents of the inferred type
                            // that aren't assignable to the constraint type (pure return type inferences are speculation anyway).
                            filteredByConstraint = MapType(inferredType, t => IfElse(n._compareTypes!(t, constraintWithThis, false) != Ternary.False, t, _neverType));
                        }
                        inferredType = IfElse(filteredByConstraint != null && (filteredByConstraint._flags & TypeFlags.Never) == 0, filteredByConstraint, null);
                    }
                }
                if (inferredType == null)
                {
                    // If the fallback type satisfies the constraint, we pick it. Otherwise, we pick the constraint.
                    inferredType = IfElse(fallbackType != null && n._compareTypes!(fallbackType, GetTypeWithThisArgument(instantiatedConstraint, fallbackType, false), false) != Ternary.False, fallbackType, instantiatedConstraint);
                }
                inference._inferredType = inferredType;
            }
            ClearActiveMapperCaches();
        }
        return inference._inferredType!;
    }

    internal Type[] GetInferredTypes(InferenceContext n)
    {
        var result = new Type[n._inferences.Count];
        for (var i = 0; i < n._inferences.Count; i++)
        {
            result[i] = GetInferredType(n, i);
        }
        return result;
    }

    internal TypeMapper? GetMapperFromContext(InferenceContext? n)
    {
        if (n == null)
        {
            return null;
        }
        return n._mapper;
    }

    // Return a type mapper that combines the context's return mapper with a mapper that erases any additional type parameters
    // to their inferences at the time of creation.
    internal TypeMapper CreateOuterReturnMapper(InferenceContext context)
    {
        if (context._outerReturnMapper == null)
        {
            var mapper = CloneInferenceContext(context, InferenceFlags.None)!._mapper!;
            if (context._returnMapper != null)
            {
                // Go: newMergedTypeMapper(context.returnMapper, mapper)（C# 侧没有该工厂，直接用 MergedTypeMapper 的构造器）。
                mapper = new MergedTypeMapper(context._returnMapper, mapper);
            }
            context._outerReturnMapper = mapper;
        }
        return context._outerReturnMapper;
    }
}
