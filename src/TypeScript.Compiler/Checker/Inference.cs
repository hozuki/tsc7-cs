// ported from: internal/checker/inference.go:1-446
// （InferenceKey / InferenceState + 状态池（getInferenceState / putInferenceState）/ inferTypes /
//   inferFromTypes / inferFromTypeArguments / inferWithPriority / inferFromContravariantTypesWithPriority /
//   inferFromContravariantTypes / inferFromContravariantTypesIfStrictFunctionTypes / invokeOnce /
//   inferFromMatchingTypes / compareTypesAndDepth / getTypeDepth / getTypeListDepth）
//
// 与 Go 的偏离（逐条；其余地方与 Go 逐行对应）：
//
// 1) 类形状（docs/architecture.md §2 的 Parser/Binder 先例）：Go 的 `func (c *Checker) x()` → 唯一的
//    `public sealed partial class Checker` 的实例方法（Go 未导出 → `internal`）；Go 的**包级函数**（无接收者）
//    → 同一个类的 `internal static` 成员。`NewInferenceInfo`/`HasOverlappingInferences` 必须 `internal`
//    可跨片（checker.go:7813/7827 调用它们）。
// 2) Go 的 `type InferenceKey struct { s; t TypeId }` 是 map 键（值语义）→ `internal readonly struct` +
//    `IEquatable`（`TypeId` 无 `==` 运算符，见 Ast/Ids.cs 的先例，故用 `Equals`）。
// 3) `n.visited map[InferenceKey]InferencePriority`（Go 惰性 `make`、`clear(n.visited)`）→
//    `Dictionary<InferenceKey, InferencePriority>?`（null = Go 的 nil map）+ `?.Clear()`。
// 4) Go 的 slice `append`/`[:len-1]` 截断（`sourceStack`/`targetStack`）→ `List<Type>` 的 `Add`/`RemoveAt`；
//    `putInferenceState` 里 `n.inferences[:0]` 只截断**本状态自己**的 slice 头（不影响调用方持有的
//    `InferenceContext.inferences`），且下次 `InferTypes` 必定整体覆盖该字段 → 直接置空数组。
// 5) `*n = InferenceState{...}`（Go 的整结构体覆写）→ 逐字段复位（其余字段归零）。
// 6) `min(n.inferencePriority, x)`（Go 内建，`InferencePriority` 是 int32）→ 就地三元表达式（枚举不能直接喂
//    `Math.Min`）。
// 7) `slices.Contains(ss, x)` → `Array.IndexOf(ss, x) >= 0`（`Type` 是 class、按引用相等，与 Go 的 `*Type` 比较
//    一致）；`slices.SortFunc(xs, cmp)` → `Array.Sort(xs, (a, b) => …)`（两者都是不稳定排序）。
// 8) `(*Checker).isX` 方法表达式（`func(c *Checker, …)`）→ `Func<Checker, …>`/`Action<…>` + 调用点 lambda
//    （Go 的方法表达式每次都显式传接收者，lambdas 等价）。
// 9) `isDeeplyNestedType(t, stack, n)`（relater.go:766）的参数是 `List<Type>`：Go 侧 `r.sourceStack`/
//    `n.sourceStack` 都要 append + `[:len-1]` 截断，只有 `List<T>` 能做（§3.2 的 `append` → `List<T>`）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

/// <summary>Go: <c>type InferenceKey struct { s TypeId; t TypeId }</c>（用作 map 键 → 值语义）。</summary>
internal readonly struct InferenceKey : IEquatable<InferenceKey>
{
    internal readonly TypeId _s;
    internal readonly TypeId _t;

    internal InferenceKey(TypeId s, TypeId t)
    {
        _s = s;
        _t = t;
    }

    public bool Equals(InferenceKey other) => _s.Equals(other._s) && _t.Equals(other._t);

    public override bool Equals(object? obj) => obj is InferenceKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_s, _t);
}

/// <summary>Go: <c>type InferenceState struct</c>（推断状态；由 <c>Checker.freeinferenceState</c> 池复用）。</summary>
internal sealed class InferenceState
{
    internal IReadOnlyList<InferenceInfo> _inferences = [];
    internal Type? _originalSource;
    internal Type? _originalTarget;
    internal InferencePriority _priority;
    internal InferencePriority _inferencePriority;
    internal bool _contravariant;
    internal bool _bivariant;
    internal ExpandingFlags _expandingFlags;
    internal Type? _propagationType;
    internal Dictionary<InferenceKey, InferencePriority>? _visited;
    internal List<Type> _sourceStack = [];
    internal List<Type> _targetStack = [];
    internal InferenceState? _next;
}

public sealed partial class Checker
{
    internal InferenceState GetInferenceState()
    {
        var n = _freeinferenceState;
        if (n == null)
        {
            n = new InferenceState();
        }
        _freeinferenceState = n._next;
        return n;
    }

    internal void PutInferenceState(InferenceState n)
    {
        n._visited?.Clear();
        // Go: `*n = InferenceState{inferences: n.inferences[:0], visited: n.visited,
        //                          sourceStack: n.sourceStack[:0], targetStack: n.targetStack[:0], next: c.freeinferenceState}`
        // —— 整结构体覆写，其余字段归零。
        n._inferences = [];
        n._originalSource = null;
        n._originalTarget = null;
        n._priority = InferencePriority.None;
        n._inferencePriority = InferencePriority.None;
        n._contravariant = false;
        n._bivariant = false;
        n._expandingFlags = ExpandingFlags.None;
        n._propagationType = null;
        n._sourceStack.Clear();
        n._targetStack.Clear();
        n._next = _freeinferenceState;
        _freeinferenceState = n;
    }

    internal void InferTypes(IReadOnlyList<InferenceInfo> inferences, Type originalSource, Type originalTarget, InferencePriority priority, bool contravariant)
    {
        var n = GetInferenceState();
        n._inferences = inferences;
        n._originalSource = originalSource;
        n._originalTarget = originalTarget;
        n._priority = priority;
        n._inferencePriority = InferencePriority.MaxValue;
        n._contravariant = contravariant;
        InferFromTypes(n, originalSource, originalTarget);
        PutInferenceState(n);
    }

    internal void InferFromTypes(InferenceState n, Type source, Type target)
    {
        if (!_couldContainTypeVariables(target) || IsNoInferType(target))
        {
            return;
        }
        if (source == _wildcardType || source == _blockedStringType)
        {
            // We are inferring from an 'any' type. We want to infer this type for every type parameter
            // referenced in the target type, so we record it as the propagation type and infer from the
            // target to itself. Then, as we find candidates we substitute the propagation type.
            var savePropagationType = n._propagationType;
            n._propagationType = source;
            InferFromTypes(n, target, target);
            n._propagationType = savePropagationType;
            return;
        }
        if (source._alias != null && target._alias != null && source._alias._symbol == target._alias._symbol)
        {
            if ((source._alias._typeArguments?.Length ?? 0) != 0 || (target._alias._typeArguments?.Length ?? 0) != 0)
            {
                // Source and target are types originating in the same generic type alias declaration.
                // Simply infer from source type arguments to target type arguments, with defaults applied.
                var typeParams = _typeAliasLinks.Get(source._alias._symbol!)._typeParameters ?? [];
                var minParams = GetMinTypeArgumentCountWorker(typeParams);
                var nodeIsInJsFile = IsInJSFile(source._alias._symbol!.ValueDeclaration);
                var sourceTypes = FillMissingTypeArgumentsWorker(source._alias._typeArguments, typeParams, minParams, nodeIsInJsFile);
                var targetTypes = FillMissingTypeArgumentsWorker(target._alias._typeArguments, typeParams, minParams, nodeIsInJsFile);
                InferFromTypeArguments(n, sourceTypes ?? [], targetTypes ?? [], GetAliasVariances(source._alias._symbol!));
            }
            // And if there weren't any type arguments, there's no reason to run inference as the types must be the same.
            return;
        }
        if (source == target && (source._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            // When source and target are the same union or intersection type, just relate each constituent
            // type to itself.
            foreach (var t in source.Types())
            {
                InferFromTypes(n, t, t);
            }
            return;
        }
        if ((target._flags & TypeFlags.Union) != 0)
        {
            Type[] sourceTypes;
            if ((source._flags & TypeFlags.Union) != 0)
            {
                sourceTypes = source.Types();
            }
            else
            {
                sourceTypes = [source];
            }
            // First, infer between identically matching source and target constituents and remove the
            // matching types.
            var (tempSources, tempTargets) = InferFromMatchingTypes(n, sourceTypes, target.Types(), (c, s, t) => c.IsTypeOrBaseIdenticalTo(s, t), false /*sort*/);
            // Next, infer between closely matching source and target constituents and remove
            // the matching types. Types closely match when they are instantiations of the same
            // object type or instantiations of the same type alias.
            var (sources, targets) = InferFromMatchingTypes(n, tempSources, tempTargets, (c, s, t) => c.IsTypeCloselyMatchedBy(s, t), true /*sort*/);
            if (targets.Length == 0)
            {
                return;
            }
            target = GetUnionTypeWorker(targets);
            if (sources.Length == 0)
            {
                // All source constituents have been matched and there is nothing further to infer from.
                // However, simply making no inferences is undesirable because it could ultimately mean
                // inferring a type parameter constraint. Instead, make a lower priority inference from
                // the full source to whatever remains in the target. For example, when inferring from
                // string to 'string | T', make a lower priority inference of string for T.
                InferWithPriority(n, source, target, InferencePriority.NakedTypeVariable);
                return;
            }
            source = GetUnionTypeWorker(sources);
        }
        else if ((target._flags & TypeFlags.Intersection) != 0 && !Every(target.Types(), t => IsNonGenericObjectType(t)))
        {
            // We reduce intersection types unless they're simple combinations of object types. For example,
            // when inferring from 'string[] & { extra: any }' to 'string[] & T' we want to remove string[] and
            // infer { extra: any } for T. But when inferring to 'string[] & Iterable<T>' we want to keep the
            // string[] on the source side and infer string for T.
            if ((source._flags & TypeFlags.Union) == 0)
            {
                Type[] sourceTypes;
                if ((source._flags & TypeFlags.Intersection) != 0)
                {
                    sourceTypes = source.Types();
                }
                else
                {
                    sourceTypes = [source];
                }
                // Infer between identically matching source and target constituents and remove the matching types.
                var (sources, targets) = InferFromMatchingTypes(n, sourceTypes, target.Types(), (c, s, t) => c.IsTypeIdenticalTo(s, t), false /*sort*/);
                if (sources.Length == 0 || targets.Length == 0)
                {
                    return;
                }
                source = GetIntersectionType(sources);
                target = GetIntersectionType(targets);
            }
        }
        if ((target._flags & (TypeFlags.IndexedAccess | TypeFlags.Substitution)) != 0)
        {
            if (IsNoInferType(target))
            {
                return;
            }
            target = GetActualTypeVariable(target);
        }
        if ((target._flags & TypeFlags.TypeVariable) != 0)
        {
            // Skip inference if the source is "blocked", which is used by the language service to
            // prevent inference on nodes currently being edited.
            if (IsFromInferenceBlockedSource(source))
            {
                return;
            }
            var inference = GetInferenceInfoForType(n, target);
            if (inference != null)
            {
                // If target is a type parameter, make an inference, unless the source type contains
                // a "non-inferrable" type. Types with this flag set are markers used to prevent inference.
                //
                // For example:
                //     - anyFunctionType is a wildcard type that's used to avoid contextually typing functions;
                //       it's internal, so should not be exposed to the user by adding it as a candidate.
                //     - autoType (and autoArrayType) is a special "any" used in control flow; like anyFunctionType,
                //       it's internal and should not be observable.
                //     - silentNeverType is returned by getInferredType when instantiating a generic function for
                //       inference (and a type variable has no mapping).
                //
                // This flag is infectious; if we produce Box<never> (where never is silentNeverType), Box<never> is
                // also non-inferrable.
                //
                // As a special case, also ignore nonInferrableAnyType, which is a special form of the any type
                // used as a stand-in for binding elements when they are being inferred.
                if ((source._objectFlags & ObjectFlags.NonInferrableType) != 0 || source == _nonInferrableAnyType)
                {
                    return;
                }
                if (!inference._isFixed)
                {
                    var candidate = OrElse(n._propagationType, source)!;
                    if (candidate == _blockedStringType)
                    {
                        return;
                    }
                    if (n._priority < inference._priority)
                    {
                        inference._candidates.Clear();
                        inference._contraCandidates.Clear();
                        inference._topLevel = true;
                        inference._priority = n._priority;
                    }
                    if (n._priority == inference._priority)
                    {
                        // We make contravariant inferences only if we are in a pure contravariant position,
                        // i.e. only if we have not descended into a bivariant position.
                        if (n._contravariant && !n._bivariant)
                        {
                            if (!inference._contraCandidates.Contains(candidate))
                            {
                                inference._contraCandidates.Add(candidate);
                                ClearCachedInferences(n._inferences);
                            }
                        }
                        else if (!inference._candidates.Contains(candidate))
                        {
                            inference._candidates.Add(candidate);
                            ClearCachedInferences(n._inferences);
                        }
                    }
                    if ((n._priority & InferencePriority.ReturnType) == 0 && (target._flags & TypeFlags.TypeParameter) != 0 && inference._topLevel && !IsTypeParameterAtTopLevel(n._originalTarget!, target, 0))
                    {
                        inference._topLevel = false;
                        ClearCachedInferences(n._inferences);
                    }
                }
                n._inferencePriority = n._inferencePriority < n._priority ? n._inferencePriority : n._priority;
                return;
            }
            // Infer to the simplified version of an indexed access, if possible, to (hopefully) expose more bare type parameters to the inference engine
            var simplified = GetSimplifiedType(target, false /*writing*/);
            if (simplified != target)
            {
                InferFromTypes(n, source, simplified);
            }
            else if ((target._flags & TypeFlags.IndexedAccess) != 0)
            {
                var indexType = GetSimplifiedType(target.AsIndexedAccessType()!._indexType!, false /*writing*/);
                // Generally simplifications of instantiable indexes are avoided to keep relationship checking correct, however if our target is an access, we can consider
                // that key of that access to be "instantiated", since we're looking to find the infernce goal in any way we can.
                if ((indexType._flags & TypeFlags.Instantiable) != 0)
                {
                    var distributed = DistributeIndexOverObjectType(GetSimplifiedType(target.AsIndexedAccessType()!._objectType!, false /*writing*/), indexType, false /*writing*/);
                    if (distributed != null && distributed != target)
                    {
                        InferFromTypes(n, source, distributed);
                    }
                }
            }
        }
        if ((source._objectFlags & ObjectFlags.Reference) != 0 && (target._objectFlags & ObjectFlags.Reference) != 0 && (source.AsTypeReference()!._target == target.AsTypeReference()!._target || IsArrayTypeWorker(source) && IsArrayTypeWorker(target)) && !(source.AsTypeReference()!._node != null && target.AsTypeReference()!._node != null))
        {
            // If source and target are references to the same generic type, infer from type arguments
            InferFromTypeArguments(n, GetTypeArgumentsWorker(source), GetTypeArgumentsWorker(target), GetVariances(source.AsTypeReference()!._target!));
        }
        else if ((source._flags & TypeFlags.Index) != 0 && (target._flags & TypeFlags.Index) != 0)
        {
            InferFromContravariantTypes(n, source.AsIndexType()!._target!, target.AsIndexType()!._target!);
        }
        else if ((IsLiteralType(source) || (source._flags & TypeFlags.String) != 0) && (target._flags & TypeFlags.Index) != 0)
        {
            var empty = CreateEmptyObjectTypeFromStringLiteral(source);
            InferFromContravariantTypesWithPriority(n, empty, target.AsIndexType()!._target!, InferencePriority.LiteralKeyof);
        }
        else if ((source._flags & TypeFlags.IndexedAccess) != 0 && (target._flags & TypeFlags.IndexedAccess) != 0)
        {
            InferFromTypes(n, source.AsIndexedAccessType()!._objectType!, target.AsIndexedAccessType()!._objectType!);
            InferFromTypes(n, source.AsIndexedAccessType()!._indexType!, target.AsIndexedAccessType()!._indexType!);
        }
        else if ((source._flags & TypeFlags.StringMapping) != 0 && (target._flags & TypeFlags.StringMapping) != 0)
        {
            if (source._symbol == target._symbol)
            {
                InferFromTypes(n, source.AsStringMappingType()!._target!, target.AsStringMappingType()!._target!);
            }
        }
        else if ((source._flags & TypeFlags.Substitution) != 0)
        {
            InferFromTypes(n, source.AsSubstitutionType()!._baseType!, target);
            // Make substitute inference at a lower priority
            InferWithPriority(n, GetSubstitutionIntersection(source), target, InferencePriority.SubstituteSource);
        }
        else if ((target._flags & TypeFlags.Conditional) != 0)
        {
            InvokeOnce(n, source, target, (c, nn, s, t) => c.InferToConditionalType(nn, s, t));
        }
        else if ((target._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            InferToMultipleTypes(n, source, target.Types(), target._flags);
        }
        else if ((source._flags & TypeFlags.Union) != 0)
        {
            // Source is a union or intersection type, infer from each constituent type
            foreach (var sourceType in source.Types())
            {
                InferFromTypes(n, sourceType, target);
            }
        }
        else if ((target._flags & TypeFlags.TemplateLiteral) != 0)
        {
            InferToTemplateLiteralType(n, source, target.AsTemplateLiteralType()!);
        }
        else
        {
            source = GetReducedTypeWorker(source);
            if (IsGenericMappedType(source) && IsGenericMappedType(target))
            {
                InvokeOnce(n, source, target, (c, nn, s, t) => c.InferFromGenericMappedTypes(nn, s, t));
            }
            if (!((n._priority & InferencePriority.NoConstraints) != 0 && (source._flags & (TypeFlags.Intersection | TypeFlags.Instantiable)) != 0))
            {
                var apparentSource = GetApparentTypeWorker(source);
                // getApparentType can return _any_ type, since an indexed access or conditional may simplify to any other type.
                // If that occurs and it doesn't simplify to an object or intersection, we'll need to restart `inferFromTypes`
                // with the simplified source.
                if (apparentSource != source && (apparentSource._flags & (TypeFlags.Object | TypeFlags.Intersection)) == 0)
                {
                    InferFromTypes(n, apparentSource, target);
                    return;
                }
                source = apparentSource;
            }
            if ((source._flags & (TypeFlags.Object | TypeFlags.Intersection)) != 0)
            {
                InvokeOnce(n, source, target, (c, nn, s, t) => c.InferFromObjectTypes(nn, s, t));
            }
        }
    }

    internal void InferFromTypeArguments(InferenceState n, Type[] sourceTypes, Type[] targetTypes, VarianceFlags[] variances)
    {
        for (var i = 0; i < Math.Min(sourceTypes.Length, targetTypes.Length); i++)
        {
            if (i < variances.Length && (variances[i] & VarianceFlags.VarianceMask) == VarianceFlags.Contravariant)
            {
                InferFromContravariantTypes(n, sourceTypes[i], targetTypes[i]);
            }
            else
            {
                InferFromTypes(n, sourceTypes[i], targetTypes[i]);
            }
        }
    }

    internal void InferWithPriority(InferenceState n, Type source, Type target, InferencePriority newPriority)
    {
        var savePriority = n._priority;
        n._priority |= newPriority;
        InferFromTypes(n, source, target);
        n._priority = savePriority;
    }

    internal void InferFromContravariantTypesWithPriority(InferenceState n, Type source, Type target, InferencePriority newPriority)
    {
        var savePriority = n._priority;
        n._priority |= newPriority;
        InferFromContravariantTypes(n, source, target);
        n._priority = savePriority;
    }

    internal void InferFromContravariantTypes(InferenceState n, Type source, Type target)
    {
        n._contravariant = !n._contravariant;
        InferFromTypes(n, source, target);
        n._contravariant = !n._contravariant;
    }

    internal void InferFromContravariantTypesIfStrictFunctionTypes(InferenceState n, Type source, Type target)
    {
        if (_strictFunctionTypes || (n._priority & InferencePriority.AlwaysStrict) != 0)
        {
            InferFromContravariantTypes(n, source, target);
        }
        else
        {
            InferFromTypes(n, source, target);
        }
    }

    // Ensure an inference action is performed only once for the given source and target types.
    // This includes two things:
    // Avoiding inferring between the same pair of source and target types,
    // and avoiding circularly inferring between source and target types.
    // For an example of the last, consider if we are inferring between source type
    // `type Deep<T> = { next: Deep<Deep<T>> }` and target type `type Loop<U> = { next: Loop<U> }`.
    // We would then infer between the types of the `next` property: `Deep<Deep<T>>` = `{ next: Deep<Deep<Deep<T>>> }` and `Loop<U>` = `{ next: Loop<U> }`.
    // We will then infer again between the types of the `next` property:
    // `Deep<Deep<Deep<T>>>` and `Loop<U>`, and so on, such that we would be forever inferring
    // between instantiations of the same types `Deep` and `Loop`.
    // In particular, we would be inferring from increasingly deep instantiations of `Deep` to `Loop`,
    // such that we would go on inferring forever, even though we would never infer
    // between the same pair of types.
    internal void InvokeOnce(InferenceState n, Type source, Type target, Action<Checker, InferenceState, Type, Type> action)
    {
        var key = new InferenceKey(source._id, target._id);
        if (n._visited != null && n._visited.TryGetValue(key, out var status))
        {
            n._inferencePriority = n._inferencePriority < status ? n._inferencePriority : status;
            return;
        }
        if (n._visited == null)
        {
            n._visited = [];
        }
        n._visited[key] = InferencePriority.Circularity;
        var saveInferencePriority = n._inferencePriority;
        n._inferencePriority = InferencePriority.MaxValue;
        // We stop inferring and report a circularity if we encounter duplicate recursion identities on both
        // the source side and the target side.
        var saveExpandingFlags = n._expandingFlags;
        n._sourceStack.Add(source);
        n._targetStack.Add(target);
        if (IsDeeplyNestedType(source, n._sourceStack, 2))
        {
            n._expandingFlags |= ExpandingFlags.Source;
        }
        if (IsDeeplyNestedType(target, n._targetStack, 2))
        {
            n._expandingFlags |= ExpandingFlags.Target;
        }
        if (n._expandingFlags != ExpandingFlags.Both)
        {
            action(this, n, source, target);
        }
        else
        {
            n._inferencePriority = InferencePriority.Circularity;
        }
        n._targetStack.RemoveAt(n._targetStack.Count - 1);
        n._sourceStack.RemoveAt(n._sourceStack.Count - 1);
        n._expandingFlags = saveExpandingFlags;
        n._visited[key] = n._inferencePriority;
        n._inferencePriority = n._inferencePriority < saveInferencePriority ? n._inferencePriority : saveInferencePriority;
    }

    internal (Type[] sources, Type[] targets) InferFromMatchingTypes(InferenceState n, Type[] sources, Type[] targets, Func<Checker, Type, Type, bool> matches, bool sort)
    {
        Type[] matchedSources = [];
        Type[] matchedTargets = [];
        foreach (var t in targets)
        {
            foreach (var s in sources)
            {
                if (matches(this, s, t))
                {
                    if (!sort)
                    {
                        InferFromTypes(n, s, t);
                    }
                    matchedSources = AppendIfUnique(matchedSources, s);
                    matchedTargets = AppendIfUnique(matchedTargets, t);
                }
            }
        }
        if (sort)
        {
            // Sort target types by decreasing depth of generic instantiations. Intuitively, a successful
            // inference from a type argument with deeper nesting is of higher quality because we've stripped
            // away more layers of type instantiations that otherwise might skew the results. For example,
            // when inferring from string[] | string[][] to T[] | T[][], the inference of string we make from
            // relating string[][] to T[][] is of higher quality than the inference of string[] we make relating
            // string[][] to T[].
            Array.Sort(matchedTargets, (a, b) => CompareTypesAndDepth(a, b));
            foreach (var t in matchedTargets)
            {
                foreach (var s in matchedSources)
                {
                    if (matches(this, s, t))
                    {
                        InferFromTypes(n, s, t);
                    }
                }
            }
        }
        if (matchedSources.Length != 0)
        {
            sources = Filter(sources, t => Array.IndexOf(matchedSources, t) < 0);
        }
        if (matchedTargets.Length != 0)
        {
            targets = Filter(targets, t => Array.IndexOf(matchedTargets, t) < 0);
        }
        return (sources, targets);
    }

    /// <summary>Compare two types first by depth and then by the regular type ordering.</summary>
    internal static int CompareTypesAndDepth(Type t1, Type t2)
    {
        var d1 = GetTypeDepth(t1, 3);
        var d2 = GetTypeDepth(t2, 3);
        if (d1 != d2)
        {
            return d2 - d1; // Largest depth sorts first
        }
        return CompareTypes(t1, t2);
    }

    /// <summary>Return the depth of the given type up to the given maximum depth. For generic aliased types
    /// and type references, the depth is one plus the largest type argument depth. For union and
    /// intersection types, the depth is the largest constituent type depth. For all other types,
    /// the depth is zero. The maximum depth limits infinite recursion of circular types.</summary>
    internal static int GetTypeDepth(Type t, int maxDepth)
    {
        if (maxDepth != 0)
        {
            if (t._alias != null && (t._alias._typeArguments?.Length ?? 0) != 0)
            {
                return GetTypeListDepth(t._alias._typeArguments ?? [], maxDepth - 1) + 1;
            }
            if ((t._objectFlags & ObjectFlags.Reference) != 0)
            {
                var typeArguments = t._checker.GetTypeArgumentsWorker(t);
                if (typeArguments.Length != 0)
                {
                    return GetTypeListDepth(typeArguments, maxDepth - 1) + 1;
                }
            }
            if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
            {
                return GetTypeListDepth(t.Types(), maxDepth);
            }
        }
        return 0;
    }

    internal static int GetTypeListDepth(Type[] types, int maxDepth)
    {
        var depth = 0;
        foreach (var t in types)
        {
            depth = Math.Max(depth, GetTypeDepth(t, maxDepth));
        }
        return depth;
    }
}
