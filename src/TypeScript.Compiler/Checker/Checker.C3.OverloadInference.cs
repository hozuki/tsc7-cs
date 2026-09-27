// ported from: internal/checker/checker.go:9012-9024（CallState）+ 9580-9838（inferTypeArguments、
//   getCandidateForOverloadFailure、pickLongestCandidateSignature、getLongestCandidateIndex、
//   getTypeArgumentsFromNodes、inferSignatureInstantiationForOverloadFailure、
//   createUnionOfSignaturesForOverloadFailure、createCombinedSymbolFromTypes、createCombinedSymbolForOverloadFailure、
//   getRestTypeOfSignature（→ GetRestTypeOfSignatureWorker）、tryGetRestTypeOfSignature）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `type CallState struct` → 命名空间级 `public sealed class CallState`（Go 是包级导出类型，docs/architecture.md §2）。
//    Go 里到处传 `*CallState` 并就地改字段，C# 用引用类型；`localState := *s` 的浅拷贝 → `ShallowClone()`
//    （`MemberwiseClone`，docs/architecture.md §3.2）。
// 2) `[]*ast.Node`/`[]*Signature` 零值（nil，len 0）→ 显式 `[]` 初始化（Go 的 nil 切片与空切片在 len/range 上等价；
//    Go 里靠 `== nil` 区分的两处（`candidatesForArgumentError == nil`）在 C# 里是 null 检查，见 ChooseOverload）。
// 3) Worker 改名（artifacts/checker-method-collisions.md）：`getRestTypeOfSignature` → `GetRestTypeOfSignatureWorker`、
//    `getContextualType` → `GetContextualTypeWorker`、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getIndexTypeOfType` → `GetIndexTypeOfTypeWorker`、`getDefaultFromTypeParameter` →
//    `GetDefaultFromTypeParameterWorker`、`getConstraintOfTypeParameter` → `GetConstraintOfTypeParameterWorker`、
//    `getTypeFromTypeNode` → `GetTypeFromTypeNodeWorker`、`getUnionTypeEx` → `GetUnionTypeExWorker`。
// 4) `core.MapNonNil`/`core.Map`/`core.Some` 的 Go 方法值参数 → lambda（命中 Worker 改名的写改后名字）。
// 5) `minAndMax(candidates, getNonRestParameterCount)`（Go 泛型函数）→ `MinAndMax(...)`，返回具名元组，用解构接收。
// 6) `for i := range maxNonRestParam`（Go 1.22 的整数 range）→ `for (var i = 0; i < maxNonRestParam; i++)`。
// 7) Go 的 `typeArgumentNodes[:len(typeParameters)]` → `Array.Resize`（Go 的切片视图在 C# 里要复制；这里只用于读取）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type[] InferTypeArguments(Node node, Signature signature, Node[] args, CheckMode checkMode, InferenceContext context)
    {
        if (IsJsxOpeningLikeElement(node))
        {
            return InferJsxTypeArguments(node, signature, checkMode, context);
        }
        // If a contextual type is available, infer from that type to the return type of the call expression. For
        // example, given a 'function wrap<T, U>(cb: (x: T) => U): (x: T) => U' and a call expression
        // 'let f: (x: string) => number = wrap(s => s.length)', we infer from the declared type of 'f' to the
        // return type of 'wrap'.
        if (!IsDecorator(node) && !IsBinaryExpression(node))
        {
            var skipBindingPatterns = Every(signature._typeParameters, p => GetDefaultFromTypeParameterWorker(p) != null);
            var contextualType = GetContextualTypeWorker(node, IfElse(skipBindingPatterns, ContextFlags.SkipBindingPatterns, ContextFlags.None));
            if (contextualType != null)
            {
                var inferenceTargetType = GetReturnTypeOfSignatureWorker(signature);
                if (_couldContainTypeVariables(inferenceTargetType))
                {
                    var outerContext = GetInferenceContext(node);
                    var isFromBindingPattern = !skipBindingPatterns && GetContextualTypeWorker(node, ContextFlags.SkipBindingPatterns) != contextualType;
                    // A return type inference from a binding pattern can be used in instantiating the contextual
                    // type of an argument later in inference, but cannot stand on its own as the final return type.
                    // It is incorporated into `context.returnMapper` which is used in `instantiateContextualType`,
                    // but doesn't need to go into `context.inferences`. This allows a an array binding pattern to
                    // produce a tuple for `T` in
                    //   declare function f<T>(cb: () => T): T;
                    //   const [e1, e2, e3] = f(() => [1, "hi", true]);
                    // but does not produce any inference for `T` in
                    //   declare function f<T>(): T;
                    //   const [e1, e2, e3] = f();
                    if (!isFromBindingPattern)
                    {
                        // We clone the inference context to avoid disturbing a resolution in progress for an
                        // outer call expression. Effectively we just want a snapshot of whatever has been
                        // inferred for any outer call expression so far.
                        var outerMapper = GetMapperFromContext(CloneInferenceContext(outerContext, InferenceFlags.NoDefault));
                        var instantiatedType = InstantiateType(contextualType, outerMapper);
                        // If the contextual type is a generic function type with a single call signature, we
                        // instantiate the type with its own type parameters and type arguments. This ensures that
                        // the type parameters are not erased to type any during type inference such that they can
                        // be inferred as actual types from the contextual type. For example:
                        //   declare function arrayMap<T, U>(f: (x: T) => U): (a: T[]) => U[];
                        //   const boxElements: <A>(a: A[]) => { value: A }[] = arrayMap(value => ({ value }));
                        // Above, the type of the 'value' parameter is inferred to be 'A'.
                        var contextualSignature = GetSingleCallSignature(instantiatedType);
                        Type inferenceSourceType;
                        if (contextualSignature != null && contextualSignature.TypeParameters().Length != 0)
                        {
                            inferenceSourceType = GetOrCreateTypeFromSignature(GetSignatureInstantiationWithoutFillingInTypeArguments(contextualSignature, contextualSignature._typeParameters));
                        }
                        else
                        {
                            inferenceSourceType = instantiatedType;
                        }
                        // Inferences made from return types have lower priority than all other inferences.
                        InferTypes(context._inferences, inferenceSourceType, inferenceTargetType, InferencePriority.ReturnType, false);
                    }
                    // Create a type mapper for instantiating generic contextual types using the inferences made
                    // from the return type. We need a separate inference pass here because (a) instantiation of
                    // the source type uses the outer context's return mapper (which excludes inferences made from
                    // outer arguments), and (b) we don't want any further inferences going into this context.
                    // We use `createOuterReturnMapper` to ensure that all occurrences of outer type parameters are
                    // replaced with inferences produced from the outer return type or preceding outer arguments.
                    // This protects against circular inferences, i.e. avoiding situations where inferences reference
                    // type parameters for which the inferences are being made.
                    var returnContext = NewInferenceContext(signature._typeParameters, signature, context._flags, null);
                    TypeMapper? outerReturnMapper = null;
                    if (outerContext != null)
                    {
                        outerReturnMapper = CreateOuterReturnMapper(outerContext);
                    }
                    var returnSourceType = InstantiateType(contextualType, outerReturnMapper);
                    InferTypes(returnContext._inferences, returnSourceType, inferenceTargetType, InferencePriority.None, false);
                    if (Some<InferenceInfo>([.. returnContext._inferences], HasInferenceCandidates))
                    {
                        context._returnMapper = GetMapperFromContext(CloneInferredPartOfContext(returnContext));
                    }
                    else
                    {
                        context._returnMapper = null;
                    }
                }
            }
        }
        var restType = GetNonArrayRestType(signature);
        var argCount = args.Length;
        if (restType != null)
        {
            argCount = Math.Min(GetParameterCount(signature) - 1, argCount);
        }
        if (restType != null && (restType._flags & TypeFlags.TypeParameter) != 0)
        {
            var info = Find<InferenceInfo>([.. context._inferences], i => i._typeParameter == restType);
            if (info != null)
            {
                // Go 的 `core.FindIndex(args[argCount:], isSpreadArgument)` 是切片视图；这里用手写循环避免分配。
                var hasSpreadArgument = false;
                for (var i = argCount; i < args.Length; i++)
                {
                    if (IsSpreadArgument(args[i]))
                    {
                        hasSpreadArgument = true;
                        break;
                    }
                }
                if (!hasSpreadArgument)
                {
                    info._impliedArity = args.Length - argCount;
                }
            }
        }
        var thisType = GetThisTypeOfSignature(signature);
        if (thisType != null && _couldContainTypeVariables(thisType))
        {
            var thisArgumentNode = GetThisArgumentOfCall(node);
            InferTypes(context._inferences, GetThisArgumentType(thisArgumentNode), thisType, InferencePriority.None, false);
        }
        for (var i = 0; i < argCount; i++)
        {
            var arg = args[i];
            if (arg.Kind != Kind.OmittedExpression)
            {
                var paramType = GetTypeAtPositionWorker(signature, i);
                if (_couldContainTypeVariables(paramType))
                {
                    var argType = CheckExpressionWithContextualType(arg, paramType, context, checkMode);
                    InferTypes(context._inferences, argType, paramType, InferencePriority.None, false);
                }
            }
        }
        if (restType != null && _couldContainTypeVariables(restType))
        {
            var spreadType = GetSpreadArgumentType(args, argCount, args.Length, restType, context, checkMode);
            InferTypes(context._inferences, spreadType, restType, InferencePriority.None, false);
        }
        return GetInferredTypes(context);
    }

    // No signature was applicable. We have already reported the errors for the invalid signature.
    internal Signature GetCandidateForOverloadFailure(Node node, Signature[] candidates, Node[] args, bool hasCandidatesOutArray, CheckMode checkMode)
    {
        // Else should not have called this.
        CheckNodeDeferred(node);
        // Normally we will combine overloads. Skip this if they have type parameters since that's hard to combine.
        // Don't do this if there is a `candidatesOutArray`,
        // because then we want the chosen best candidate to be one of the overloads, not a combination.
        if (hasCandidatesOutArray || candidates.Length == 1 || Some(candidates, s => s.TypeParameters().Length != 0))
        {
            return PickLongestCandidateSignature(node, candidates, args, checkMode);
        }
        return CreateUnionOfSignaturesForOverloadFailure(candidates);
    }

    internal Signature PickLongestCandidateSignature(Node node, Signature[] candidates, Node[] args, CheckMode checkMode)
    {
        // Pick the longest signature. This way we can get a contextual type for cases like:
        //     declare function f(a: { xa: number; xb: number; }, b: number);
        //     f({ |
        // Also, use explicitly-supplied type arguments if they are provided, so we can get a contextual signature in cases like:
        //     declare function f<T>(k: keyof T);
        //     f<Foo>("
        var argCount = args.Length;
        if (_apparentArgumentCount != null)
        {
            argCount = _apparentArgumentCount.Value;
        }
        var bestIndex = GetLongestCandidateIndex(candidates, argCount);
        var candidate = candidates[bestIndex];
        var typeParameters = candidate.TypeParameters();
        if (typeParameters.Length == 0)
        {
            return candidate;
        }
        Node[]? typeArgumentNodes = null;
        if (CallLikeExpressionMayHaveTypeArguments(node))
        {
            typeArgumentNodes = node.TypeArguments();
        }
        Signature instantiated;
        if (typeArgumentNodes != null && typeArgumentNodes.Length != 0)
        {
            instantiated = CreateSignatureInstantiation(candidate, GetTypeArgumentsFromNodes(typeArgumentNodes, typeParameters));
        }
        else
        {
            instantiated = InferSignatureInstantiationForOverloadFailure(node, typeParameters, candidate, args, checkMode);
        }
        candidates[bestIndex] = instantiated;
        return instantiated;
    }

    internal int GetLongestCandidateIndex(Signature[] candidates, int argsCount)
    {
        var maxParamsIndex = -1;
        var maxParams = -1;
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            var paramCount = GetParameterCount(candidate);
            if (HasEffectiveRestParameterWorker(candidate) || paramCount >= argsCount)
            {
                return i;
            }
            if (paramCount > maxParams)
            {
                maxParams = paramCount;
                maxParamsIndex = i;
            }
        }
        return maxParamsIndex;
    }

    internal Type[] GetTypeArgumentsFromNodes(Node[] typeArgumentNodes, Type[] typeParameters)
    {
        if (typeArgumentNodes.Length > typeParameters.Length)
        {
            Array.Resize(ref typeArgumentNodes, typeParameters.Length);
        }
        var typeArguments = new List<Type>(Map(typeArgumentNodes, n => GetTypeFromTypeNodeWorker(n)));
        while (typeArguments.Count < typeParameters.Length)
        {
            var t = GetDefaultFromTypeParameterWorker(typeParameters[typeArguments.Count]);
            if (t == null)
            {
                t = GetConstraintOfTypeParameterWorker(typeParameters[typeArguments.Count]);
                if (t == null)
                {
                    t = _unknownType;
                }
            }
            typeArguments.Add(t!);
        }
        return typeArguments.ToArray();
    }

    internal Signature InferSignatureInstantiationForOverloadFailure(Node node, Type[] typeParameters, Signature candidate, Node[] args, CheckMode checkMode)
    {
        var inferenceContext = NewInferenceContext(typeParameters, candidate, IfElse(IsInJSFile(node), InferenceFlags.AnyDefault, InferenceFlags.None), null);
        var typeArgumentTypes = InferTypeArguments(node, candidate, args, checkMode | CheckMode.SkipContextSensitive | CheckMode.SkipGenericFunctions, inferenceContext);
        return CreateSignatureInstantiation(candidate, typeArgumentTypes);
    }

    internal Signature CreateUnionOfSignaturesForOverloadFailure(Signature[] candidates)
    {
        Symbol[] thisParameters = MapNonNil(candidates, s => s._thisParameter!);
        Symbol? thisParameter = null;
        if (thisParameters.Length != 0)
        {
            thisParameter = CreateCombinedSymbolFromTypes(thisParameters, Map(thisParameters, p => GetTypeOfParameter(p)));
        }
        var (minArgumentCount, maxNonRestParam) = MinAndMax(candidates, GetNonRestParameterCount);
        var parameters = new List<Symbol>(maxNonRestParam);
        for (var i = 0; i < maxNonRestParam; i++)
        {
            Symbol[] symbols = MapNonNil(candidates, s =>
            {
                if (SignatureHasRestParameter(s))
                {
                    if (i < s.Parameters().Length - 1)
                    {
                        return s.Parameters()[i];
                    }
                    return LastOrNil(s._parameters)!;
                }
                if (i < s.Parameters().Length)
                {
                    return s.Parameters()[i];
                }
                return null!;
            });
            parameters.Add(CreateCombinedSymbolFromTypes(symbols, MapNonNil(candidates, s => TryGetTypeAtPosition(s, i)!)));
        }
        Symbol[] restParameterSymbols = MapNonNil(candidates, s => SignatureHasRestParameter(s) ? LastOrNil(s._parameters)! : null!);
        var flags = SignatureFlags.IsSignatureCandidateForOverloadFailure;
        if (restParameterSymbols.Length != 0)
        {
            var t = CreateArrayType(GetUnionTypeExWorker(MapNonNil(candidates, s => TryGetRestTypeOfSignature(s)!), UnionReduction.Subtype, null, null));
            parameters.Add(CreateCombinedSymbolForOverloadFailure(restParameterSymbols, t));
            flags |= SignatureFlags.HasRestParameter;
        }
        if (Some(candidates, SignatureHasLiteralTypes))
        {
            flags |= SignatureFlags.HasLiteralTypes;
        }
        return NewSignature(flags, candidates[0]._declaration!, null, thisParameter, parameters.ToArray(), GetIntersectionType(Map(candidates, s => GetReturnTypeOfSignatureWorker(s))), null, minArgumentCount);
    }

    internal Symbol CreateCombinedSymbolFromTypes(Symbol[] sources, Type[] types)
    {
        return CreateCombinedSymbolForOverloadFailure(sources, GetUnionTypeExWorker(types, UnionReduction.Subtype, null, null));
    }

    internal Symbol CreateCombinedSymbolForOverloadFailure(Symbol[] sources, Type t)
    {
        // This function is currently only used for erroneous overloads, so it's good enough to just use the first source.
        return CreateSymbolWithType(FirstOrNil(sources)!, t);
    }

    internal Type GetRestTypeOfSignatureWorker(Signature signature)
    {
        return OrElse(TryGetRestTypeOfSignature(signature), _anyType)!;
    }

    internal Type? TryGetRestTypeOfSignature(Signature signature)
    {
        if (!SignatureHasRestParameter(signature))
        {
            return null;
        }
        var restType = GetTypeOfSymbolWorker(signature.Parameters()[^1]);
        if (IsTupleType(restType))
        {
            restType = GetRestTypeOfTupleType(restType);
            if (restType == null)
            {
                return null;
            }
        }
        return GetIndexTypeOfTypeWorker(restType, _numberType!);
    }
}

/// <summary>Go: <c>type CallState struct</c>（checker.go:9012；Go 里到处传 <c>*CallState</c> 并就地改字段 → C# 引用类型）。</summary>
public sealed class CallState
{
    internal Node? _node;
    internal Node[]? _typeArguments;
    internal Node[] _args = [];
    internal Signature[] _candidates = [];
    internal CheckMode _argCheckMode;
    internal bool _isSingleNonGenericCandidate;
    internal bool _signatureHelpTrailingComma;
    internal bool _recursiveResolution;
    internal Signature[]? _candidatesForArgumentError;
    internal Signature? _candidateForArgumentArityError;
    internal Signature? _candidateForTypeArgumentError;

    /// <summary>Go: <c>localState := *s</c>（结构体浅拷贝）→ <c>MemberwiseClone()</c>。</summary>
    internal CallState ShallowClone() => (CallState)MemberwiseClone();
}
