// ported from: internal/checker/relater.go:1199-2048
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 包级接口 `Discriminator`（relater.go:1199，成员 len/name/matches）→ 命名空间级 `public interface Discriminator`
//    （成员按 C# 规则 PascalCase：`Len()`/`Name(int)`/`Matches(int, Type)`）。实现类 `TypeDiscriminator`（relater.go:1030）
//    在 Relater.Api.cs，`ObjectLiteralDiscriminator`（checker.go:31117）在 Checker.C8.DecoratorsAndDiscriminants.cs。
// 2) 包级函数 `isNonPrimitiveType` → 同一个 `partial class Checker` 的 `internal static` 成员（无接收者）。
// 3) Go 的 `defer popFn()`（getVariancesWorker 的 tracing）→ 在函数唯一出口前显式执行等价逻辑：该函数体内只有末尾一个
//    `return`，Go 的 defer 语义等价于在该处执行；`traceArgs`/`popFn` 用局部变量接住后判空调用。
// 4) Go 的 `var related Ternary`（零值 = TernaryFalse = 0）→ C# `var related = Ternary.False;`（C# 未赋值局部不可读，
//    且这正是 Go 代码显式依赖的零值语义）。
// 5) nil 切片口径：`signature.parameters` / `signature.typeParameters` 在 C# 是可空数组 → `?.Length ?? 0`（len(nil) == 0）；
//    `createTupleType(nil)` / `createTupleTypeEx(nil, nil, readonly)` → `[]`（Go 侧只取 len 与 range，语义等价）；
//    `typeAliasLinks.Get(symbol).typeParameters`（Type[]?）→ `?? []`；`c.varianceStack = nil` → `_varianceStack = []`
//    （新建空 List；**不能**用 Clear()：Go 赋 nil 后 append 落在新底层数组上，saveVarianceStack 的内容不受影响）。
// 6) 名字冲突（artifacts/checker-method-collisions.md）：`getTypeAtPosition` → `GetTypeAtPositionWorker`、
//    `hasEffectiveRestParameter` → `HasEffectiveRestParameterWorker`；调用的跨片成员同样按清单用改名后的名字。
// 7) `strconv.Itoa(n)` → `n.ToString(CultureInfo.InvariantCulture)`（`using System.Globalization;`）；
//    `jsnum.Number(i)` → `new Number(i)`（`using TypeScript.JsNum;`）。
// 8) Go 的 `switch { case cond: … }`（getVariancesWorker 的 modifiers 分派、compareTypePredicateRelatedTo 的 source.t 分派）
//    → `if`/`else if` 链（分支互斥，判断顺序逐字保留）。
// 9) `c.TypeToString(t)`（Go 导出包装器，printer.go:43）→ `TypeToString(t)`（Checker/Printer.cs 的 1 参 public 成员）；
//    `c.typeToString(t, decl)` → `TypeToStringWorker(t, decl)`（2 参）；`c.typeToStringEx(t, nil, flags, nil)` →
//    `TypeToStringExWorker(t, null, flags, null)`；`c.signatureToString(sig)` → `SignatureToString(sig)`（无 Worker 版）；
//    `c.typePredicateToString(p)` → `TypePredicateToStringWorker(p)`。
// 10) `newSimpleTypeMapper(source, target)`（mapper.go 包级函数）→ `new SimpleTypeMapper(source, target)`
//     （Mapper.cs 已把该函数落成 internal 构造函数）。
// 11) usings 放在 file-scoped `namespace TypeScript.Checker;` **之后**（Checker 包既有写法）。实测（net11.0 Roslyn 探针
//     0 警告 0 错误）：usings 在 namespace 之前时裸名 `Diagnostics.X` 会先命中外层命名空间链里的**命名空间**
//     `TypeScript.Diagnostics` → CS0234，且 `using Diagnostics = TypeScript.Diagnostics.Diagnostics;` 别名**无效**；
//     只有 namespace 声明体内的 using 才参与简单名解析（本文件全部 `Diagnostics.X` 依赖此布局）。
namespace TypeScript.Checker;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

/// <summary>Go: <c>type Discriminator interface</c>（relater.go:1199）。</summary>
public interface Discriminator
{
    /// <summary>Number of discriminant properties.</summary>
    int Len();

    /// <summary>Property name of index-th discriminator.</summary>
    string Name(int index);

    /// <summary>True if index-th discriminator matches the given type.</summary>
    bool Matches(int index, Type t);
}

public sealed partial class Checker
{
    internal Type DiscriminateTypeByDiscriminableItems(Type target, Discriminator discriminator)
    {
        var types = target.Types();
        var include = new Ternary[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            var t = types[i];
            if ((t._flags & TypeFlags.Primitive) == 0 && (GetReducedTypeWorker(t)._flags & TypeFlags.Never) == 0)
            {
                include[i] = Ternary.True;
            }
        }
        for (var n = 0; n < discriminator.Len(); n++)
        {
            // If the remaining target types include at least one with a matching discriminant, eliminate those that
            // have non-matching discriminants. This ensures that we ignore erroneous discriminators and gradually
            // refine the target set without eliminating every constituent (which would lead to `never`).
            var matched = false;
            for (var i = 0; i < types.Length; i++)
            {
                if (include[i] != Ternary.False)
                {
                    var targetType = GetTypeOfPropertyOrIndexSignatureOfType(types[i], discriminator.Name(n));
                    if (targetType != null)
                    {
                        if (discriminator.Matches(n, targetType))
                        {
                            matched = true;
                        }
                        else
                        {
                            include[i] = Ternary.Maybe;
                        }
                    }
                }
            }
            // Turn each Ternary.Maybe into Ternary.False if there was a match. Otherwise, revert to Ternary.True.
            for (var i = 0; i < types.Length; i++)
            {
                if (include[i] == Ternary.Maybe)
                {
                    if (matched)
                    {
                        include[i] = Ternary.False;
                    }
                    else
                    {
                        include[i] = Ternary.True;
                    }
                }
            }
        }
        if (Array.IndexOf(include, Ternary.False) >= 0)
        {
            var filteredTypes = new List<Type>();
            for (var i = 0; i < types.Length; i++)
            {
                if (include[i] == Ternary.True)
                {
                    filteredTypes.Add(types[i]);
                }
            }
            var filtered = GetUnionTypeExWorker(filteredTypes.ToArray(), UnionReduction.None, null, null);
            if ((filtered._flags & TypeFlags.Never) == 0)
            {
                return filtered;
            }
        }
        return target;
    }

    internal Type FilterPrimitivesIfContainsNonPrimitive(Type unionType)
    {
        if (MaybeTypeOfKind(unionType, TypeFlags.NonPrimitive))
        {
            var result = FilterType(unionType, IsNonPrimitiveType);
            if ((result._flags & TypeFlags.Never) == 0)
            {
                return result;
            }
        }
        return unionType;
    }

    internal static bool IsNonPrimitiveType(Type t)
    {
        return (t._flags & TypeFlags.Primitive) == 0;
    }

    internal (string Left, string Right) GetTypeNamesForErrorDisplay(Type left, Type right)
    {
        string leftStr;
        if (SymbolValueDeclarationIsContextSensitive(left._symbol))
        {
            leftStr = TypeToStringWorker(left, left._symbol!.ValueDeclaration);
        }
        else
        {
            leftStr = TypeToString(left);
        }
        string rightStr;
        if (SymbolValueDeclarationIsContextSensitive(right._symbol))
        {
            rightStr = TypeToStringWorker(right, right._symbol!.ValueDeclaration);
        }
        else
        {
            rightStr = TypeToString(right);
        }
        if (leftStr == rightStr)
        {
            leftStr = GetTypeNameForErrorDisplay(left);
            rightStr = GetTypeNameForErrorDisplay(right);
        }
        return (leftStr, rightStr);
    }

    internal string GetTypeNameForErrorDisplay(Type t)
    {
        return TypeToStringExWorker(t, null /*enclosingDeclaration*/, TypeFormatFlags.UseFullyQualifiedType, null);
    }

    internal bool SymbolValueDeclarationIsContextSensitive(Symbol? symbol)
    {
        return symbol != null && symbol.ValueDeclaration != null && IsExpression(symbol.ValueDeclaration) && !IsContextSensitiveWorker(symbol.ValueDeclaration);
    }

    internal bool TypeCouldHaveTopLevelSingletonTypes(Type t)
    {
        // Okay, yes, 'boolean' is a union of 'true | false', but that's not useful
        // in error reporting scenarios. If you need to use this function but that detail matters,
        // feel free to add a flag.
        if ((t._flags & TypeFlags.Boolean) != 0)
        {
            return false;
        }
        if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            return Some(t.Types(), TypeCouldHaveTopLevelSingletonTypes);
        }
        if ((t._flags & TypeFlags.Instantiable) != 0)
        {
            var constraint = GetConstraintOfType(t);
            if (constraint != null && constraint != t)
            {
                return TypeCouldHaveTopLevelSingletonTypes(constraint);
            }
        }
        return IsUnitType(t) || (t._flags & TypeFlags.TemplateLiteral) != 0 || (t._flags & TypeFlags.StringMapping) != 0;
    }

    internal VarianceFlags[] GetVariances(Type t)
    {
        // Arrays and tuples are known to be covariant, no need to spend time computing this.
        if (t == _globalArrayType || t == _globalReadonlyArrayType || (t._objectFlags & ObjectFlags.Tuple) != 0)
        {
            return _arrayVariances;
        }
        return GetVariancesWorker(t._symbol!, t.AsInterfaceType()!.TypeParameters());
    }

    internal VarianceFlags[] GetAliasVariances(Symbol symbol)
    {
        return GetVariancesWorker(symbol, _typeAliasLinks.Get(symbol)._typeParameters ?? []);
    }

    // Return an array containing the variance of each type parameter. The variance is effectively
    // a digest of the type comparisons that occur for each type argument when instantiations of the
    // generic type are structurally compared. We infer the variance information by comparing
    // instantiations of the generic type for type arguments with known relations. The function
    // returns an empty slice when invoked recursively for the given generic type.
    internal VarianceFlags[] GetVariancesWorker(Symbol symbol, Type[] typeParameters)
    {
        var links = _varianceLinks.Get(symbol);
        if (links._variances == null)
        {
            Dictionary<string, object?>? traceArgs = null;
            Action? popFn = null;
            var tr = _tracer;
            if (tr != null)
            {
                traceArgs = new Dictionary<string, object?> { ["arity"] = typeParameters.Length, ["id"] = GetDeclaredTypeOfSymbolWorker(symbol)._id };
                popFn = tr.Push(Phase.CheckTypes, "getVariancesWorker", traceArgs, true);
            }
            var stackIndex = GetVarianceStackIndex(symbol);
            if (stackIndex < 0)
            {
                var saveResolutionStart = _resolutionStart;
                if (_varianceStack.Count == 0)
                {
                    _resolutionStart = _typeResolutions.Count;
                }
                _varianceStack.Add(new VarianceStackEntry(symbol, typeParameters));
                var variances = new VarianceFlags[typeParameters.Length];
                for (var i = 0; i < typeParameters.Length; i++)
                {
                    var tp = typeParameters[i];
                    var modifiers = GetTypeParameterModifiers(tp);
                    VarianceFlags variance;
                    if ((modifiers & ModifierFlags.Out) != 0)
                    {
                        if ((modifiers & ModifierFlags.In) != 0)
                        {
                            variance = VarianceFlags.Invariant;
                        }
                        else
                        {
                            variance = VarianceFlags.Covariant;
                        }
                    }
                    else if ((modifiers & ModifierFlags.In) != 0)
                    {
                        variance = VarianceFlags.Contravariant;
                    }
                    else
                    {
                        var saveReliabilityFlags = _reliabilityFlags;
                        _reliabilityFlags = 0;
                        // We first compare instantiations where the type parameter is replaced with
                        // marker types that have a known subtype relationship. From this we can infer
                        // invariance, covariance, contravariance or bivariance.
                        var typeWithSuper = CreateMarkerType(symbol, tp, _markerSuperType);
                        var typeWithSub = CreateMarkerType(symbol, tp, _markerSubType);
                        // Go 的 `core.IfElse(…, VarianceFlagsCovariant, 0)`：Go 的 `0` 即 `VarianceFlagsInvariant`（= 0）。
                        variance = IfElse(IsTypeAssignableToWorker(typeWithSub, typeWithSuper), VarianceFlags.Covariant, VarianceFlags.Invariant) |
                            IfElse(IsTypeAssignableToWorker(typeWithSuper, typeWithSub), VarianceFlags.Contravariant, VarianceFlags.Invariant);
                        // If the instantiations appear to be related bivariantly it may be because the
                        // type parameter is independent (i.e. it isn't witnessed anywhere in the generic
                        // type). To determine this we compare instantiations where the type parameter is
                        // replaced with marker types that are known to be unrelated.
                        if (variance == VarianceFlags.Bivariant && IsTypeAssignableToWorker(CreateMarkerType(symbol, tp, _markerOtherType), typeWithSuper))
                        {
                            variance = VarianceFlags.Independent;
                        }
                        if ((_reliabilityFlags & RelationComparisonResult.ReportsUnmeasurable) != 0)
                        {
                            variance |= VarianceFlags.Unmeasurable;
                        }
                        if ((_reliabilityFlags & RelationComparisonResult.ReportsUnreliable) != 0)
                        {
                            variance |= VarianceFlags.Unreliable;
                        }
                        _reliabilityFlags = saveReliabilityFlags;
                    }
                    // If variance computation was restarted due to a circularity we may have already
                    // computed variances for this generic type. If so, we exit early.
                    if ((links._variances?.Length ?? 0) != 0)
                    {
                        break;
                    }
                    variances[i] = variance;
                }
                // Store the results unless a restarted computation has already stored them.
                if ((links._variances?.Length ?? 0) == 0)
                {
                    links._variances = variances;
                }
                _varianceStack.RemoveAt(_varianceStack.Count - 1);
                if (_varianceStack.Count == 0)
                {
                    _resolutionStart = saveResolutionStart;
                }
            }
            else
            {
                // We've detected a circularity. Since we may compute different variances depending on where
                // we enter a circularity, we find the generic type with the "smallest" symbol in the circular
                // region of the variance stack and restart the computation from there if necessary. This
                // ensures stable results for circular generic types.
                var minIndex = stackIndex;
                for (var i = stackIndex + 1; i < _varianceStack.Count; i++)
                {
                    if (CompareSymbols(_varianceStack[i]._symbol!, _varianceStack[minIndex]._symbol!) < 0)
                    {
                        minIndex = i;
                    }
                }
                if (minIndex > stackIndex)
                {
                    var saveVarianceStack = _varianceStack;
                    _varianceStack = [];
                    GetVariancesWorker(saveVarianceStack[minIndex]._symbol!, saveVarianceStack[minIndex]._typeParameters!);
                    _varianceStack = saveVarianceStack;
                }
                // Store an empty slice to mark that we can't compute variances for this type. We treat type
                // parameters as co-variant in this case.
                if ((links._variances?.Length ?? 0) == 0)
                {
                    links._variances = [];
                }
            }
            // Go 的 `defer func() { …formatted…; popFn() }()`：本函数只有此处（末尾 `return`）一个出口。
            if (popFn != null)
            {
                var formatted = new string[links._variances?.Length ?? 0];
                for (var i = 0; i < formatted.Length; i++)
                {
                    formatted[i] = links._variances![i].String();
                }
                traceArgs!["variances"] = formatted;
                popFn();
            }
        }
        return links._variances!;
    }

    internal int GetVarianceStackIndex(Symbol symbol)
    {
        for (var i = 0; i < _varianceStack.Count; i++)
        {
            if (_varianceStack[i]._symbol == symbol)
            {
                return i;
            }
        }
        return -1;
    }

    internal Type CreateMarkerType(Symbol symbol, Type source, Type target)
    {
        var mapper = new SimpleTypeMapper(source, target);
        var t = GetDeclaredTypeOfSymbolWorker(symbol);
        if (IsErrorType(t))
        {
            return t;
        }
        Type result;
        if ((symbol.Flags & SymbolFlags.TypeAlias) != 0)
        {
            result = GetTypeAliasInstantiation(symbol, InstantiateTypes(_typeAliasLinks.Get(symbol)._typeParameters ?? [], mapper), null);
        }
        else
        {
            result = CreateTypeReference(t, InstantiateTypes(t.AsInterfaceType()!.TypeParameters(), mapper));
        }
        _markerTypes.Add(result);
        return result;
    }

    internal bool IsMarkerType(Type t)
    {
        return _markerTypes.Has(t);
    }

    internal ModifierFlags GetTypeParameterModifiers(Type tp)
    {
        var flags = ModifierFlags.None;
        if (tp._symbol != null)
        {
            foreach (var d in tp._symbol.Declarations ?? [])
            {
                flags |= d.ModifierFlags;
            }
        }
        return flags & (ModifierFlags.In | ModifierFlags.Out | ModifierFlags.Const);
    }

    // Return true if the given type reference has a 'void' type argument for a covariant type parameter.
    // See comment at call in recursiveTypeRelatedTo for when this case matters.
    internal bool HasCovariantVoidArgument(Type[] typeArguments, VarianceFlags[] variances)
    {
        for (var i = 0; i < variances.Length; i++)
        {
            if ((variances[i] & VarianceFlags.VarianceMask) == VarianceFlags.Covariant && (typeArguments[i]._flags & TypeFlags.Void) != 0)
            {
                return true;
            }
        }
        return false;
    }

    internal bool IsSignatureAssignableTo(Signature source, Signature target, bool ignoreReturnTypes)
    {
        return CompareSignaturesRelated(source, target, IfElse(ignoreReturnTypes, SignatureCheckMode.IgnoreReturnTypes, SignatureCheckMode.None), false /*reportErrors*/, null /*errorReporter*/, _compareTypesAssignable, null /*reportUnreliableMarkers*/) != Ternary.False;
    }

    internal Ternary CompareSignaturesRelated(Signature source, Signature target, SignatureCheckMode checkMode, bool reportErrors, ErrorReporter? errorReporter, TypeComparer compareTypes, TypeMapper? reportUnreliableMarkers)
    {
        if (source == target)
        {
            return Ternary.True;
        }
        if (!((checkMode & SignatureCheckMode.StrictTopSignature) != 0 && IsTopSignature(source)) && IsTopSignature(target))
        {
            return Ternary.True;
        }
        if ((checkMode & SignatureCheckMode.StrictTopSignature) != 0 && IsTopSignature(source) && !IsTopSignature(target))
        {
            return Ternary.False;
        }
        var targetCount = GetParameterCount(target);
        var sourceHasMoreParameters = false;
        if (!HasEffectiveRestParameterWorker(target))
        {
            if ((checkMode & SignatureCheckMode.StrictArity) != 0)
            {
                sourceHasMoreParameters = HasEffectiveRestParameterWorker(source) || GetParameterCount(source) > targetCount;
            }
            else
            {
                sourceHasMoreParameters = GetMinArgumentCount(source) > targetCount;
            }
        }
        if (sourceHasMoreParameters)
        {
            if (reportErrors && (checkMode & SignatureCheckMode.StrictArity) == 0)
            {
                // the second condition should be redundant, because there is no error reporting when comparing signatures by strict arity
                // since it is only done for subtype reduction
                errorReporter!(Diagnostics.Target_signature_provides_too_few_arguments_Expected_0_or_more_but_got_1, GetMinArgumentCount(source), targetCount);
            }
            return Ternary.False;
        }
        if ((source._typeParameters?.Length ?? 0) != 0 && !Same(source._typeParameters, target._typeParameters))
        {
            target = GetCanonicalSignature(target);
            source = InstantiateSignatureInContextOf(source, target /*inferenceContext*/, null, compareTypes);
        }
        var sourceCount = GetParameterCount(source);
        var sourceRestType = GetNonArrayRestType(source);
        var targetRestType = GetNonArrayRestType(target);
        if (sourceRestType != null || targetRestType != null)
        {
            // Go: relater.go:1526 —— 此分支内 `IfElse` 必返回非 nil（source 非空则取 source，否则由守卫保证 target 非空）。
            InstantiateType(IfElse(sourceRestType != null, sourceRestType, targetRestType)!, reportUnreliableMarkers);
        }
        var kind = Kind.Unknown;
        if (target._declaration != null)
        {
            kind = target._declaration.Kind;
        }
        var strictVariance = (checkMode & SignatureCheckMode.Callback) == 0 && _strictFunctionTypes && kind != Kind.MethodDeclaration && kind != Kind.MethodSignature && kind != Kind.Constructor;
        var result = Ternary.True;
        var sourceThisType = GetThisTypeOfSignature(source);
        if (sourceThisType != null && sourceThisType != _voidType)
        {
            var targetThisType = GetThisTypeOfSignature(target);
            if (targetThisType != null)
            {
                // void sources are assignable to anything.
                var related = Ternary.False;
                if (!strictVariance)
                {
                    related = compareTypes(sourceThisType, targetThisType, false /*reportErrors*/);
                }
                if (related == Ternary.False)
                {
                    related = compareTypes(targetThisType, sourceThisType, reportErrors);
                }
                if (related == Ternary.False)
                {
                    if (reportErrors)
                    {
                        errorReporter!(Diagnostics.The_this_types_of_each_signature_are_incompatible);
                    }
                    return Ternary.False;
                }
                result &= related;
            }
        }
        int paramCount;
        if (sourceRestType != null || targetRestType != null)
        {
            paramCount = Math.Min(sourceCount, targetCount);
        }
        else
        {
            paramCount = Math.Max(sourceCount, targetCount);
        }
        int restIndex;
        if (sourceRestType != null || targetRestType != null)
        {
            restIndex = paramCount - 1;
        }
        else
        {
            restIndex = -1;
        }
        for (var i = 0; i < paramCount; i++)
        {
            Type? sourceType;
            if (i == restIndex)
            {
                sourceType = GetRestOrAnyTypeAtPosition(source, i);
            }
            else
            {
                sourceType = TryGetTypeAtPosition(source, i);
            }
            Type? targetType;
            if (i == restIndex)
            {
                targetType = GetRestOrAnyTypeAtPosition(target, i);
            }
            else
            {
                targetType = TryGetTypeAtPosition(target, i);
            }
            if (sourceType != null && targetType != null && (sourceType != targetType || (checkMode & SignatureCheckMode.StrictArity) != 0))
            {
                // In order to ensure that any generic type Foo<T> is at least co-variant with respect to T no matter
                // how Foo uses T, we need to relate parameters bi-variantly (given that parameters are input positions,
                // they naturally relate only contra-variantly). However, if the source and target parameters both have
                // function types with a single call signature, we know we are relating two callback parameters. In
                // that case it is sufficient to only relate the parameters of the signatures co-variantly because,
                // similar to return values, callback parameters are output positions. This means that a Promise<T>,
                // where T is used only in callback parameter positions, will be co-variant (as opposed to bi-variant)
                // with respect to T.
                Signature? sourceSig = null;
                Signature? targetSig = null;
                if ((checkMode & SignatureCheckMode.Callback) == 0 && !IsInstantiatedGenericParameter(source, i))
                {
                    sourceSig = GetSingleCallSignature(GetNonNullableType(sourceType));
                }
                if ((checkMode & SignatureCheckMode.Callback) == 0 && !IsInstantiatedGenericParameter(target, i))
                {
                    targetSig = GetSingleCallSignature(GetNonNullableType(targetType));
                }
                var callbacks = sourceSig != null && targetSig != null && GetTypePredicateOfSignatureWorker(sourceSig) == null && GetTypePredicateOfSignatureWorker(targetSig) == null &&
                    GetTypeFacts(sourceType, TypeFacts.IsUndefinedOrNull) == GetTypeFacts(targetType, TypeFacts.IsUndefinedOrNull);
                var related = Ternary.False;
                if (callbacks)
                {
                    related = CompareSignaturesRelated(targetSig!, sourceSig!, (checkMode & SignatureCheckMode.StrictArity) | IfElse(strictVariance, SignatureCheckMode.StrictCallback, SignatureCheckMode.BivariantCallback), reportErrors, errorReporter, compareTypes, reportUnreliableMarkers);
                }
                else
                {
                    if ((checkMode & SignatureCheckMode.Callback) == 0 && !strictVariance)
                    {
                        related = compareTypes(sourceType, targetType, false /*reportErrors*/);
                    }
                    if (related == Ternary.False)
                    {
                        related = compareTypes(targetType, sourceType, reportErrors);
                    }
                }
                // With strict arity, (x: number | undefined) => void is a subtype of (x?: number | undefined) => void
                if (related != Ternary.False && (checkMode & SignatureCheckMode.StrictArity) != 0 && i >= GetMinArgumentCount(source) && i < GetMinArgumentCount(target) && compareTypes(sourceType, targetType, false /*reportErrors*/) != Ternary.False)
                {
                    related = Ternary.False;
                }
                if (related == Ternary.False)
                {
                    if (reportErrors)
                    {
                        errorReporter!(Diagnostics.Types_of_parameters_0_and_1_are_incompatible, GetParameterNameAtPosition(source, i), GetParameterNameAtPosition(target, i));
                    }
                    return Ternary.False;
                }
                result &= related;
            }
        }
        if ((checkMode & SignatureCheckMode.IgnoreReturnTypes) == 0)
        {
            // If a signature resolution is already in-flight, skip issuing a circularity error
            // here and just use the `any` type directly
            var targetReturnType = GetNonCircularReturnTypeOfSignature(target);
            if (targetReturnType == _voidType || targetReturnType == _anyType)
            {
                return result;
            }
            var sourceReturnType = GetNonCircularReturnTypeOfSignature(source);
            // The following block preserves behavior forbidding boolean returning functions from being assignable to type guard returning functions
            var targetTypePredicate = GetTypePredicateOfSignatureWorker(target);
            if (targetTypePredicate != null)
            {
                var sourceTypePredicate = GetTypePredicateOfSignatureWorker(source);
                if (sourceTypePredicate != null)
                {
                    result &= CompareTypePredicateRelatedTo(sourceTypePredicate, targetTypePredicate, reportErrors, errorReporter, compareTypes);
                }
                else if (targetTypePredicate._kind == TypePredicateKind.Identifier || targetTypePredicate._kind == TypePredicateKind.This)
                {
                    if (reportErrors)
                    {
                        errorReporter!(Diagnostics.Signature_0_must_be_a_type_predicate, SignatureToString(source));
                    }
                    return Ternary.False;
                }
            }
            else
            {
                // When relating callback signatures, we still need to relate return types bi-variantly as otherwise
                // the containing type wouldn't be co-variant. For example, interface Foo<T> { add(cb: () => T): void }
                // wouldn't be co-variant for T without this rule.
                var related = Ternary.False;
                if ((checkMode & SignatureCheckMode.BivariantCallback) != 0)
                {
                    related = compareTypes(targetReturnType, sourceReturnType, false /*reportErrors*/);
                }
                if (related == Ternary.False)
                {
                    related = compareTypes(sourceReturnType, targetReturnType, reportErrors);
                }
                result &= related;
                if (result == Ternary.False && reportErrors)
                {
                    // The errors reported here serve as markers that trigger error chain reduction in the (*Relater).reportError
                    // method. The markers are elided in the final diagnostic chain and never actually reported.
                    Message message;
                    if ((source._parameters?.Length ?? 0) == 0 && (target._parameters?.Length ?? 0) == 0)
                    {
                        message = IfElse((source._flags & SignatureFlags.Construct) != 0,
                            Diagnostics.Construct_signatures_with_no_arguments_have_incompatible_return_types_0_and_1,
                            Diagnostics.Call_signatures_with_no_arguments_have_incompatible_return_types_0_and_1);
                    }
                    else
                    {
                        message = IfElse((source._flags & SignatureFlags.Construct) != 0,
                            Diagnostics.Construct_signature_return_types_0_and_1_are_incompatible,
                            Diagnostics.Call_signature_return_types_0_and_1_are_incompatible);
                    }
                    errorReporter!(message, TypeToString(sourceReturnType), TypeToString(targetReturnType));
                }
            }
        }
        return result;
    }

    internal Ternary CompareTypePredicateRelatedTo(TypePredicate source, TypePredicate target, bool reportErrors, ErrorReporter? errorReporter, TypeComparer compareTypes)
    {
        if (source._kind != target._kind)
        {
            if (reportErrors)
            {
                errorReporter!(Diagnostics.A_this_based_type_guard_is_not_compatible_with_a_parameter_based_type_guard);
                errorReporter!(Diagnostics.Type_predicate_0_is_not_assignable_to_1, TypePredicateToStringWorker(source), TypePredicateToStringWorker(target));
            }
            return Ternary.False;
        }
        if (source._kind == TypePredicateKind.Identifier || source._kind == TypePredicateKind.AssertsIdentifier)
        {
            if (source._parameterIndex != target._parameterIndex)
            {
                if (reportErrors)
                {
                    errorReporter!(Diagnostics.Parameter_0_is_not_in_the_same_position_as_parameter_1, source._parameterName, target._parameterName);
                    errorReporter!(Diagnostics.Type_predicate_0_is_not_assignable_to_1, TypePredicateToStringWorker(source), TypePredicateToStringWorker(target));
                }
                return Ternary.False;
            }
        }
        Ternary related;
        if (source._t == target._t)
        {
            related = Ternary.True;
        }
        else if (source._t != null && target._t != null)
        {
            related = compareTypes(source._t, target._t, reportErrors);
        }
        else
        {
            related = Ternary.False;
        }
        if (related == Ternary.False && reportErrors)
        {
            errorReporter!(Diagnostics.Type_predicate_0_is_not_assignable_to_1, TypePredicateToStringWorker(source), TypePredicateToStringWorker(target));
        }
        return related;
    }

    // Returns true if `s` is `(...args: A) => R` where `A` is `any`, `any[]`, `never`, or `never[]`, and `R` is `any` or `unknown`.
    internal bool IsTopSignature(Signature s)
    {
        if ((s._typeParameters?.Length ?? 0) == 0 && (s._thisParameter == null || IsTypeAny(GetTypeOfParameter(s._thisParameter))) && (s._parameters?.Length ?? 0) == 1 && SignatureHasRestParameter(s))
        {
            var paramType = GetTypeOfParameter(s._parameters![0]);
            Type restType;
            if (IsArrayTypeWorker(paramType))
            {
                restType = GetTypeArgumentsWorker(paramType)[0];
            }
            else
            {
                restType = paramType;
            }
            return (restType._flags & (TypeFlags.Any | TypeFlags.Never)) != 0 && (GetReturnTypeOfSignatureWorker(s)._flags & TypeFlags.AnyOrUnknown) != 0;
        }
        return false;
    }

    // Return the number of parameters in a signature. The rest parameter, if present, counts as one
    // parameter. For example, the parameter count of (x: number, y: number, ...z: string[]) is 3 and
    // the parameter count of (x: number, ...args: [number, ...string[], boolean])) is also 3. In the
    // latter example, the effective rest type is [...string[], boolean].
    internal int GetParameterCount(Signature signature)
    {
        var length = signature._parameters?.Length ?? 0;
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbolWorker(signature._parameters![length - 1]);
            if (IsTupleTypeWorker(restType))
            {
                return length + restType.TargetTupleType()!._fixedLength - IfElse((restType.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0, 0, 1);
            }
        }
        return length;
    }

    internal int GetMinArgumentCount(Signature signature)
    {
        return GetMinArgumentCountEx(signature, MinArgumentCountFlags.None);
    }

    internal int GetMinArgumentCountEx(Signature signature, MinArgumentCountFlags flags)
    {
        var strongArityForUntypedJS = flags & MinArgumentCountFlags.StrongArityForUntypedJS;
        var voidIsNonOptional = flags & MinArgumentCountFlags.VoidIsNonOptional;
        if (voidIsNonOptional != 0 || signature._resolvedMinArgumentCount == -1)
        {
            var minArgumentCount = -1;
            if (SignatureHasRestParameter(signature))
            {
                var restType = GetTypeOfSymbolWorker(signature._parameters![^1]);
                if (IsTupleTypeWorker(restType))
                {
                    var firstOptionalIndex = FindIndex(restType.TargetTupleType()!._elementInfos, static info => (info._flags & ElementFlags.Required) == 0);
                    var requiredCount = firstOptionalIndex;
                    if (firstOptionalIndex < 0)
                    {
                        requiredCount = restType.TargetTupleType()!._fixedLength;
                    }
                    if (requiredCount > 0)
                    {
                        minArgumentCount = signature._parameters!.Length - 1 + requiredCount;
                    }
                }
            }
            if (minArgumentCount == -1)
            {
                if (strongArityForUntypedJS == 0 && (signature._flags & SignatureFlags.IsUntypedSignatureInJSFile) != 0)
                {
                    return 0;
                }
                minArgumentCount = signature._minArgumentCount;
            }
            if (voidIsNonOptional != 0)
            {
                return minArgumentCount;
            }
            for (var i = minArgumentCount - 1; i >= 0; i--)
            {
                var t = GetTypeAtPositionWorker(signature, i);
                if (!SomeType(t, static t2 => (t2._flags & TypeFlags.Void) != 0))
                {
                    break;
                }
                minArgumentCount = i;
            }
            signature._resolvedMinArgumentCount = minArgumentCount;
        }
        return signature._resolvedMinArgumentCount;
    }

    internal bool HasEffectiveRestParameterWorker(Signature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbolWorker(signature._parameters![^1]);
            return !IsTupleTypeWorker(restType) || (restType.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0;
        }
        return false;
    }

    internal Type GetTypeAtPositionWorker(Signature signature, int pos)
    {
        var t = TryGetTypeAtPosition(signature, pos);
        if (t != null)
        {
            return t;
        }
        return _anyType;
    }

    internal Type? TryGetTypeAtPosition(Signature signature, int pos)
    {
        var paramCount = (signature._parameters?.Length ?? 0) - IfElse(SignatureHasRestParameter(signature), 1, 0);
        if (pos < paramCount)
        {
            return GetTypeOfParameter(signature._parameters![pos]);
        }
        if (SignatureHasRestParameter(signature))
        {
            // We want to return the value undefined for an out of bounds parameter position,
            // so we need to check bounds here before calling getIndexedAccessType (which
            // otherwise would return the type 'undefined').
            var restType = GetTypeOfSymbolWorker(signature._parameters![paramCount]);
            var index = pos - paramCount;
            if (!IsTupleTypeWorker(restType) || (restType.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0 || index < restType.TargetTupleType()!._fixedLength)
            {
                return GetIndexedAccessType(restType, GetNumberLiteralType(new Number(index)));
            }
        }
        return null;
    }

    // Return the rest type at the given position, transforming `any[]` into just `any`. We do this because
    // in signatures we want `any[]` in a rest position to be compatible with anything, but `any[]` isn't
    // assignable to tuple types with required elements.
    internal Type? GetRestOrAnyTypeAtPosition(Signature source, int pos)
    {
        var restType = GetRestTypeAtPosition(source, pos, false);
        if (restType != null)
        {
            var elementType = GetElementTypeOfArrayTypeWorker(restType);
            if (elementType != null && IsTypeAny(elementType))
            {
                return _anyType;
            }
        }
        return restType;
    }

    internal Type GetRestTypeAtPosition(Signature source, int pos, bool @readonly)
    {
        var parameterCount = GetParameterCount(source);
        var minArgumentCount = GetMinArgumentCount(source);
        var restType = GetEffectiveRestType(source);
        if (restType != null && pos >= parameterCount - 1)
        {
            if (pos == parameterCount - 1)
            {
                return restType;
            }
            else
            {
                return CreateArrayType(GetIndexedAccessType(restType, _numberType));
            }
        }
        var length = parameterCount - pos;
        if (length <= 0)
        {
            return CreateTupleTypeEx([], [], @readonly);
        }
        var types = new Type[length];
        var infos = new TupleElementInfo[length];
        for (var i = 0; i < types.Length; i++)
        {
            ElementFlags flags;
            if (restType == null || i < types.Length - 1)
            {
                types[i] = GetTypeAtPositionWorker(source, i + pos);
                flags = IfElse(i + pos < minArgumentCount, ElementFlags.Required, ElementFlags.Optional);
            }
            else
            {
                types[i] = restType;
                flags = ElementFlags.Variadic;
            }
            infos[i] = new TupleElementInfo { _flags = flags, _labeledDeclaration = GetNameableDeclarationAtPosition(source, i + pos) };
        }
        return CreateTupleTypeEx(types, infos, @readonly);
    }

    internal Node? GetNameableDeclarationAtPosition(Signature signature, int pos)
    {
        var paramCount = (signature._parameters?.Length ?? 0) - IfElse(SignatureHasRestParameter(signature), 1, 0);
        if (pos < paramCount)
        {
            var decl = signature._parameters![pos].ValueDeclaration;
            if (decl != null && IsValidDeclarationForTupleLabel(decl))
            {
                return decl;
            }
            return null;
        }
        if (SignatureHasRestParameter(signature))
        {
            var restParameter = signature._parameters![paramCount];
            var restType = GetTypeOfSymbolWorker(restParameter);
            if (IsTupleTypeWorker(restType))
            {
                var elementInfos = restType.TargetTupleType()!._elementInfos!;
                var index = pos - paramCount;
                if (index < elementInfos.Length)
                {
                    return elementInfos[index]._labeledDeclaration;
                }
                return null;
            }
            if (restParameter.ValueDeclaration != null && IsValidDeclarationForTupleLabel(restParameter.ValueDeclaration))
            {
                return restParameter.ValueDeclaration;
            }
        }
        return null;
    }

    internal bool IsValidDeclarationForTupleLabel(Node d)
    {
        return IsNamedTupleMember(d) || IsParameterDeclaration(d) && d.Name() != null && IsIdentifier(d.Name()!);
    }

    internal Type? GetNonArrayRestType(Signature signature)
    {
        var restType = GetEffectiveRestType(signature);
        if (restType != null && !IsArrayTypeWorker(restType) && !IsTypeAny(restType))
        {
            return restType;
        }
        return null;
    }

    internal Type? GetEffectiveRestType(Signature signature)
    {
        if (SignatureHasRestParameter(signature))
        {
            var restType = GetTypeOfSymbolWorker(signature._parameters![^1]);
            if (!IsTupleTypeWorker(restType))
            {
                if (IsTypeAny(restType))
                {
                    return _anyArrayType;
                }
                return restType;
            }
            if ((restType.TargetTupleType()!._combinedFlags & ElementFlags.Variable) != 0)
            {
                return SliceTupleType(restType, restType.TargetTupleType()!._fixedLength, 0);
            }
        }
        return null;
    }

    internal Type SliceTupleType(Type t, int index, int endSkipCount)
    {
        var target = t.TargetTupleType()!;
        var endIndex = GetTypeReferenceArity(t) - Math.Max(endSkipCount, 0);
        if (index > target._fixedLength)
        {
            var restArrayType = GetRestArrayTypeOfTupleType(t);
            if (restArrayType != null)
            {
                return restArrayType;
            }
            return CreateTupleType([]);
        }
        if (index >= endIndex)
        {
            return CreateTupleType([]);
        }
        return CreateTupleTypeEx(GetTypeArgumentsWorker(t)[index..endIndex], target._elementInfos![index..endIndex], false /*readonly*/);
    }

    internal Type GetKnownKeysOfTupleType(Type t)
    {
        var fixedLength = t.TargetTupleType()!._fixedLength;
        var keys = new Type[fixedLength + 1];
        for (var i = 0; i < fixedLength; i++)
        {
            keys[i] = GetStringLiteralType(i.ToString(CultureInfo.InvariantCulture));
        }
        keys[fixedLength] = GetIndexType(IfElse(t.TargetTupleType()!._readonly, _globalReadonlyArrayType, _globalArrayType));
        return GetUnionTypeWorker(keys);
    }

    internal Type? GetRestArrayTypeOfTupleType(Type t)
    {
        var restType = GetRestTypeOfTupleType(t);
        if (restType != null)
        {
            return CreateArrayType(restType);
        }
        return null;
    }

    internal Type? GetThisTypeOfSignature(Signature signature)
    {
        if (signature._thisParameter != null)
        {
            return GetTypeOfSymbolWorker(signature._thisParameter);
        }
        return null;
    }

    internal bool IsInstantiatedGenericParameter(Signature signature, int pos)
    {
        if (signature._target == null)
        {
            return false;
        }
        var t = TryGetTypeAtPosition(signature._target, pos);
        return t != null && IsGenericType(t);
    }

    internal string GetParameterNameAtPosition(Signature signature, int pos)
    {
        var paramCount = (signature._parameters?.Length ?? 0) - IfElse(SignatureHasRestParameter(signature), 1, 0);
        if (pos < paramCount)
        {
            return signature._parameters![pos].Name;
        }
        var restParameter = signature._parameters![paramCount];
        var restType = GetTypeOfSymbolWorker(restParameter);
        if (IsTupleTypeWorker(restType))
        {
            var index = pos - paramCount;
            return GetTupleElementLabel(restType.TargetTupleType()!._elementInfos![index], restParameter, index);
        }
        return restParameter.Name;
    }

    internal string GetTupleElementLabel(TupleElementInfo elementInfo, Symbol? restSymbol, int index)
    {
        if (elementInfo._labeledDeclaration != null)
        {
            return elementInfo._labeledDeclaration.Name()!.Text();
        }
        if (restSymbol != null && restSymbol.ValueDeclaration != null && IsParameterDeclaration(restSymbol.ValueDeclaration))
        {
            return GetTupleElementLabelFromBindingElement(restSymbol.ValueDeclaration, index, elementInfo._flags);
        }
        string rootName;
        if (restSymbol != null)
        {
            rootName = restSymbol.Name;
        }
        else
        {
            rootName = "arg";
        }
        return rootName + "_" + index.ToString(CultureInfo.InvariantCulture);
    }

    internal string GetTupleElementLabelFromBindingElement(Node node, int index, ElementFlags elementFlags)
    {
        if (node.Name() != null)
        {
            switch (node.Name()!.Kind)
            {
                case Kind.Identifier:
                    {
                        var name = node.Name()!.Text();
                        if (HasDotDotDotToken(node))
                        {
                            // given
                            //   (...[x, y, ...z]: [number, number, ...number[]]) => ...
                            // this produces
                            //   (x: number, y: number, ...z: number[]) => ...
                            // which preserves rest elements of 'z'

                            // given
                            //   (...[x, y, ...z]: [number, number, ...[...number[], number]]) => ...
                            // this produces
                            //   (x: number, y: number, ...z: number[], z_1: number) => ...
                            // which preserves rest elements of z but gives distinct numbers to fixed elements of 'z'
                            if ((elementFlags & ElementFlags.Variable) != 0)
                            {
                                return name;
                            }
                            return name + "_" + index.ToString(CultureInfo.InvariantCulture);
                        }
                        // given
                        //   (...[x]: [number]) => ...
                        // this produces
                        //   (x: number) => ...
                        // which preserves fixed elements of 'x'

                        // given
                        //   (...[x]: ...number[]) => ...
                        // this produces
                        //   (x_0: number) => ...
                        // which numbers fixed elements of 'x' whose tuple element type is variable
                        if ((elementFlags & ElementFlags.Fixed) != 0)
                        {
                            return name;
                        }
                        return name + "_n";
                    }
                case Kind.ArrayBindingPattern:
                    {
                        if (HasDotDotDotToken(node))
                        {
                            var elements = node.Name()!.Elements() ?? [];
                            var lastElement = LastOrNil(elements);
                            var lastElementIsBindingElementRest = lastElement != null && IsBindingElement(lastElement) && HasDotDotDotToken(lastElement);
                            var elementCount = elements.Length - IfElse(lastElementIsBindingElementRest, 1, 0);
                            if (index < elementCount)
                            {
                                var element = elements[index];
                                if (IsBindingElement(element))
                                {
                                    return GetTupleElementLabelFromBindingElement(element, index, elementFlags);
                                }
                            }
                            else if (lastElementIsBindingElementRest)
                            {
                                return GetTupleElementLabelFromBindingElement(lastElement!, index - elementCount, elementFlags);
                            }
                        }
                        break;
                    }
            }
        }
        return "arg_" + index.ToString(CultureInfo.InvariantCulture);
    }
}
