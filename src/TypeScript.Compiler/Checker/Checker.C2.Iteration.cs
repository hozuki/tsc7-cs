// ported from: internal/checker/checker.go:6263-6903（checkIteratedTypeOrElementType、getIteratedTypeOrElementType、
//   getIterationTypeOfGeneratorFunctionReturnType、getIterationTypesOfGeneratorFunctionReturnType、getIterationTypeOfIterable、
//   getIterationTypesOfIterable(+Worker/Fast)、IterationTypesResolver.getResolvedIterationTypes、isReferenceToType、
//   isReferenceToSomeType、getBuiltinIteratorReturnType、IterationTypes.hasTypes、IterationTypes.getType、combineIterationTypes、
//   getIterationTypeUnion、getAsyncFromSyncIterationTypes、getIterationTypesOfIterableSlow、getIterationTypesOfIterator(+Worker/Fast/Slow)、
//   getIterationTypesOfMethod、getIterationTypesOfIteratorResult、isYieldIteratorResult、isReturnIteratorResult、isIteratorResult、
//   reportTypeNotIterableError、getIterationDiagnosticDetails、包级 isES2015OrLaterIterable）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `IterationTypes`（checker.go:508）与 `IterationTypesResolver`（:522）由 C1 的 `Checker.Keys.cs` 声明为
//    `public sealed class`，本文件只在**末尾**追加同名的 `public sealed partial class` 分片，承载 Go 里属于本窗口的
//    三个方法（`IterationTypesResolver.getResolvedIterationTypes`、`IterationTypes.hasTypes`/`getType`）。
//    Keys.cs 已用 `public sealed partial class IterationTypesResolver` 声明（Checker.Keys.cs:1090），本文件的分片与之合并。
// 2) 诊断收集参数 `diagnosticOutput *[]*ast.Diagnostic` → **`ref Diagnostic[]?`**（与已落地的 relater 一致：
//    `Relater.Api.cs:431 ReportDiagnostic(Diagnostic?, ref Diagnostic[]?)`、`:339 CheckTypeAssignableToEx(..., out Diagnostic[]?)`）：
//    Go 的 nil 指针（"不收集"）→ 数组引用为 null；Go 的 `append(*diagnosticOutput, x)` 由 `ReportDiagnostic` 承担。
//    调用点 `&diags` → `ref diags`；Go 传字面 `nil` 的调用点 → 先声明局部 `Diagnostic[]? diags = null` 再 `ref`。
// 3) 切片操作：Go 的 `make([]T, 0, n)` + `append` → `List<T>`；`slices.Contains` → `Array.IndexOf(...) >= 0`
//    （先例 Checker.C7.Evaluate.cs:9）；`core.Filter/Map/MapNonNil` → 同名 `Core.*`（`T[]?` + `Func<T,U>`；本文件用
//    Go 的方法值处写方法组或 lambda）；`filterType(t, c.isYieldIteratorResult)` → `FilterType(t, IsYieldIteratorResult)`。
// 4) Go 的 map 读缺失键返回零值 → `TryGetValue`（`_iterationTypesCache`）；可空字段（`_syncIterationTypesResolver` 等）
//    调用点写 `!`（初始化后必非空）。
// 5) resolver / Checker 的**函数类型字段**在 C# 是委托字段：`r.getGlobalIteratorType()` → `r._getGlobalIteratorType!()`、
//    `r.resolveIterationType(...)` → `r._resolveIterationType!(...)`、`c.getGlobalIterableType()` → `_getGlobalIterableType()`、
//    `c.iterationTypesCache` → `_iterationTypesCache`（docs/checker-plan.md §5.5 + Checker.Keys.cs/Checker.Struct.cs 现状）。
// 6) `getIterationDiagnosticDetails` 的 Go `(*diagnostics.Message, bool)` 返回 → `bool TryGetIterationDiagnosticDetails(…, out Message)`
//    （§5.4 的 Try 约定）；`IterationTypes.getType` 的 `panic("Unhandled case …")` → `Debug.Fail(...)` +
//    `throw new System.Diagnostics.UnreachableException()`（Roslyn 不知道 Debug.Fail 不返回，先例 Checker.C3.Calls.cs:16）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal Type CheckIteratedTypeOrElementType(IterationUse use, Type inputType, Type sentType, Node? errorNode)
    {
        if (IsTypeAny(inputType))
        {
            return inputType;
        }
        var t = GetIteratedTypeOrElementType(use, inputType, sentType, errorNode, true /*checkAssignability*/);
        if (t != null)
        {
            return t;
        }
        return _anyType;
    }

    internal Type? GetIteratedTypeOrElementType(IterationUse use, Type inputType, Type sentType, Node? errorNode, bool checkAssignability)
    {
        var allowAsyncIterables = (use & IterationUse.AllowsAsyncIterablesFlag) != 0;
        if (inputType == _neverType)
        {
            if (errorNode != null)
            {
                ReportTypeNotIterableError(errorNode, inputType, allowAsyncIterables);
            }
            return null;
        }
        var iterableExists = _getGlobalIterableType() != _emptyGenericType;
        var possibleOutOfBounds = _compilerOptions.NoUncheckedIndexedAccess == Tristate.TSTrue && (use & IterationUse.PossiblyOutOfBounds) != 0;
        if (iterableExists || allowAsyncIterables)
        {
            var iterationTypes = GetIterationTypesOfIterable(inputType, use, IfElse(iterableExists, errorNode, null));
            if (checkAssignability)
            {
                if (iterationTypes._nextType != null)
                {
                    Message? diagnostic = null;
                    if ((use & IterationUse.ForOfFlag) != 0)
                    {
                        diagnostic = Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_for_of_will_always_send_0;
                    }
                    else if ((use & IterationUse.SpreadFlag) != 0)
                    {
                        diagnostic = Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_array_spread_will_always_send_0;
                    }
                    else if ((use & IterationUse.DestructuringFlag) != 0)
                    {
                        diagnostic = Diagnostics.Cannot_iterate_value_because_the_next_method_of_its_iterator_expects_type_1_but_array_destructuring_will_always_send_0;
                    }
                    else if ((use & IterationUse.YieldStarFlag) != 0)
                    {
                        diagnostic = Diagnostics.Cannot_delegate_iteration_to_value_because_the_next_method_of_its_iterator_expects_type_1_but_the_containing_generator_will_always_send_0;
                    }
                    if (diagnostic != null)
                    {
                        CheckTypeAssignableTo(sentType, iterationTypes._nextType!, errorNode, diagnostic);
                    }
                }
            }
            if (iterationTypes._yieldType != null || iterableExists)
            {
                if (iterationTypes._yieldType == null)
                {
                    return null;
                }
                if (possibleOutOfBounds)
                {
                    return IncludeUndefinedInIndexSignature(iterationTypes._yieldType!);
                }
                return iterationTypes._yieldType;
            }
        }
        var arrayType = inputType;
        var hasStringConstituent = false;
        // If strings are permitted, remove any string-like constituents from the array type.
        // This allows us to find other non-string element types from an array unioned with
        // a string.
        if ((use & IterationUse.AllowsStringInputFlag) != 0)
        {
            if ((arrayType._flags & TypeFlags.Union) != 0)
            {
                // After we remove all types that are StringLike, we will know if there was a string constituent
                // based on whether the result of filter is a new array.
                var arrayTypes = inputType.Types();
                var filteredTypes = Filter(arrayTypes, t => (t._flags & TypeFlags.StringLike) == 0);
                if (!Same(filteredTypes, arrayTypes))
                {
                    arrayType = GetUnionTypeExWorker(filteredTypes, UnionReduction.Subtype, null, null);
                }
            }
            else if ((arrayType._flags & TypeFlags.StringLike) != 0)
            {
                arrayType = _neverType;
            }
            hasStringConstituent = arrayType != inputType;
            if (hasStringConstituent)
            {
                // Now that we've removed all the StringLike types, if no constituents remain, then the entire
                // arrayOrStringType was a string.
                if ((arrayType._flags & TypeFlags.Never) != 0)
                {
                    if (possibleOutOfBounds)
                    {
                        return IncludeUndefinedInIndexSignature(_stringType);
                    }
                    return _stringType;
                }
            }
        }
        if (!IsArrayLikeTypeWorker(arrayType))
        {
            if (errorNode != null)
            {
                // Which error we report depends on whether we allow strings or if there was a
                // string constituent. For example, if the input type is number | string, we
                // want to say that number is not an array type. But if the input was just
                // number and string input is allowed, we want to say that number is not an
                // array type or a string type.
                var allowsStrings = (use & IterationUse.AllowsStringInputFlag) != 0 && !hasStringConstituent;
                var maybeMissingAwait = TryGetIterationDiagnosticDetails(use, inputType, allowsStrings, out var defaultDiagnostic);
                ErrorAndMaybeSuggestAwait(errorNode, maybeMissingAwait && GetAwaitedTypeOfPromise(arrayType) != null, defaultDiagnostic, TypeToString(arrayType));
            }
            if (hasStringConstituent)
            {
                if (possibleOutOfBounds)
                {
                    return IncludeUndefinedInIndexSignature(_stringType);
                }
                return _stringType;
            }
            return null;
        }
        var arrayElementType = GetIndexTypeOfTypeWorker(arrayType, _numberType);
        if (hasStringConstituent && arrayElementType != null)
        {
            // This is just an optimization for the case where arrayOrStringType is string | string[]
            if ((arrayElementType._flags & TypeFlags.StringLike) != 0 && _compilerOptions.NoUncheckedIndexedAccess != Tristate.TSTrue)
            {
                return _stringType;
            }
            if (possibleOutOfBounds)
            {
                return GetUnionTypeExWorker([arrayElementType, _stringType, _undefinedType], UnionReduction.Subtype, null, null);
            }
            return GetUnionTypeExWorker([arrayElementType, _stringType], UnionReduction.Subtype, null, null);
        }
        if ((use & IterationUse.PossiblyOutOfBounds) != 0)
        {
            // Go: includeUndefinedInIndexSignature(arrayElementType)。Go 侧同样直接解引用（getIndexTypeOfType 返回 nil
            // 时 Go 会 panic），这里 `!` 只表达同一假设。
            return IncludeUndefinedInIndexSignature(arrayElementType!);
        }
        return arrayElementType;
    }

    // Gets the requested "iteration type" from a type that is either `Iterable`-like, `Iterator`-like,
    // `IterableIterator`-like, or `Generator`-like (for a non-async generator); or `AsyncIterable`-like,
    // `AsyncIterator`-like, `AsyncIterableIterator`-like, or `AsyncGenerator`-like (for an async generator).
    internal Type? GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind typeKind, Type returnType, bool isAsyncGenerator)
    {
        if (IsTypeAny(returnType))
        {
            return null;
        }
        var iterationTypes = GetIterationTypesOfGeneratorFunctionReturnType(returnType, isAsyncGenerator);
        return iterationTypes.GetType(typeKind);
    }

    internal IterationTypes GetIterationTypesOfGeneratorFunctionReturnType(Type t, bool isAsyncGenerator)
    {
        if (IsTypeAny(t))
        {
            return new IterationTypes(_anyType, _anyType, _anyType);
        }
        var use = IfElse(isAsyncGenerator, IterationUse.AsyncGeneratorReturnType, IterationUse.GeneratorReturnType);
        var resolver = IfElse(isAsyncGenerator, _asyncIterationTypesResolver!, _syncIterationTypesResolver!);
        var result = GetIterationTypesOfIterable(t, use, null /*errorNode*/);
        if (result.HasTypes())
        {
            return result;
        }
        // Go: c.getIterationTypesOfIterator(t, resolver, nil /*errorNode*/, nil /*diagnosticOutput*/)。
        // Go 的 nil 指针在 C# 侧用"值 == null 的 `ref` 变量"表示：ReportDiagnostic 见到 null 就直接 AddDiagnostic，
        // 与 Go 的 `diagnosticOutput == nil` 分支一致。
        Diagnostic[]? diagnosticOutput = null;
        return GetIterationTypesOfIterator(t, resolver, null /*errorNode*/, ref diagnosticOutput);
    }

    // Gets the requested "iteration type" from an `Iterable`-like or `AsyncIterable`-like type.
    internal Type? GetIterationTypeOfIterable(IterationUse use, IterationTypeKind typeKind, Type inputType, Node? errorNode)
    {
        if (IsTypeAny(inputType))
        {
            return null;
        }
        var iterationTypes = GetIterationTypesOfIterable(inputType, use, errorNode);
        return iterationTypes.GetType(typeKind);
    }

    // Gets the *yield*, *return*, and *next* types from an `Iterable`-like or `AsyncIterable`-like type.
    //
    // At every level that involves analyzing return types of signatures, we union the return types of all the signatures.
    //
    // Another thing to note is that at any step of this process, we could run into a dead end,
    // meaning either the property is missing, or we run into the anyType. If either of these things
    // happens, we return a default `IterationTypes{}` to signal that we could not find the iteration type.
    // If a property is missing, and the previous step did not result in `any`, then we also give an error
    // if the caller requested it. Then the caller can decide what to do in the case where there is no
    // iterated type.
    //
    // For a **for-of** statement, `yield*` (in a normal generator), spread, array
    // destructuring, or normal generator we will only ever look for a `[Symbol.iterator]()`
    // method.
    //
    // For an async generator we will only ever look at the `[Symbol.asyncIterator]()` method.
    //
    // For a **for-await-of** statement or a `yield*` in an async generator we will look for
    // the `[Symbol.asyncIterator]()` method first, and then the `[Symbol.iterator]()` method.
    internal IterationTypes GetIterationTypesOfIterable(Type t, IterationUse use, Node? errorNode)
    {
        t = GetReducedTypeWorker(t);
        if (IsTypeAny(t))
        {
            return new IterationTypes(_anyType, _anyType, _anyType);
        }
        var key = new IterationTypesKey(t._id, use & IterationUse.CacheFlags);
        // If we are reporting errors and encounter a cached `noIterationTypes`, we should ignore the cached value and continue as if nothing was cached.
        // In addition, we should not cache any new results for this call.
        var noCache = false;
        if (_iterationTypesCache.TryGetValue(key, out var cached))
        {
            if (errorNode == null || cached.HasTypes())
            {
                return cached;
            }
            noCache = true;
        }
        var result = GetIterationTypesOfIterableWorker(t, use, errorNode, noCache);
        if (!noCache)
        {
            _iterationTypesCache[key] = result;
        }
        return result;
    }

    internal IterationTypes GetIterationTypesOfIterableWorker(Type t, IterationUse use, Node? errorNode, bool noCache)
    {
        if ((t._flags & TypeFlags.Union) != 0)
        {
            var allIterationTypes = new List<IterationTypes>(t.Types().Length);
            foreach (var constituent in t.Types())
            {
                var iterationTypes = GetIterationTypesOfIterableWorker(constituent, use, null, noCache);
                if (!iterationTypes.HasTypes())
                {
                    if (errorNode != null)
                    {
                        AddDeferredDiagnostic(() =>
                        {
                            ReportTypeNotIterableError(errorNode, t, (use & IterationUse.AllowsAsyncIterablesFlag) != 0);
                        });
                    }
                    return new IterationTypes();
                }
                allIterationTypes.Add(iterationTypes);
            }
            return CombineIterationTypes(allIterationTypes.ToArray());
        }
        // Go: var diags []*ast.Diagnostic（nil slice）+ &diags。C# 侧用**非 null 的空数组**：relater 的
        // ReportDiagnostic 见到非 null 就 append（否则立刻 AddDiagnostic），与 Go 的"指针非 nil"分支一一对应；
        // 收集到的诊断随后要么上报，要么作为相关附加信息。
        Diagnostic[]? diags = [];
        if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
        {
            var iterationTypes = GetIterationTypesOfIterableFast(t, _asyncIterationTypesResolver!);
            if (iterationTypes.HasTypes())
            {
                if ((use & IterationUse.ForOfFlag) != 0)
                {
                    return GetAsyncFromSyncIterationTypes(iterationTypes, errorNode);
                }
                return iterationTypes;
            }
            iterationTypes = GetIterationTypesOfIterableSlow(t, _asyncIterationTypesResolver!, errorNode, ref diags);
            if (iterationTypes.HasTypes())
            {
                if (diags is { Length: > 0 })
                {
                    foreach (var d in diags)
                    {
                        AddDiagnostic(d);
                    }
                }
                return iterationTypes;
            }
        }
        if ((use & IterationUse.AllowsSyncIterablesFlag) != 0)
        {
            var iterationTypes = GetIterationTypesOfIterableFast(t, _syncIterationTypesResolver!);
            if (iterationTypes.HasTypes())
            {
                if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
                {
                    return GetAsyncFromSyncIterationTypes(iterationTypes, errorNode);
                }
                return iterationTypes;
            }
            iterationTypes = GetIterationTypesOfIterableSlow(t, _syncIterationTypesResolver!, errorNode, ref diags);
            if (iterationTypes.HasTypes())
            {
                if (diags is { Length: > 0 })
                {
                    foreach (var d in diags)
                    {
                        AddDiagnostic(d);
                    }
                }
                if ((use & IterationUse.AllowsAsyncIterablesFlag) != 0)
                {
                    return GetAsyncFromSyncIterationTypes(iterationTypes, errorNode);
                }
                return iterationTypes;
            }
        }
        if (errorNode != null)
        {
            // We defer the diagnostic because TypeToString may attempt to resolve symbols that are already being
            // resolved, possibly causing circularities.
            AddDeferredDiagnostic(() =>
            {
                var diagnostic = ReportTypeNotIterableError(errorNode, t, (use & IterationUse.AllowsAsyncIterablesFlag) != 0);
                foreach (var d in diags ?? [])
                {
                    diagnostic.AddRelatedInfo(d);
                }
            });
        }
        return new IterationTypes();
    }

    internal IterationTypes GetIterationTypesOfIterableFast(Type t, IterationTypesResolver r)
    {
        // As an optimization, if the type is an instantiation of the following global type, then
        // just grab its related type arguments:
        // - `Iterable<T, TReturn, TNext>` or `AsyncIterable<T, TReturn, TNext>`
        // - `IteratorObject<T, TReturn, TNext>` or `AsyncIteratorObject<T, TReturn, TNext>`
        // - `IterableIterator<T, TReturn, TNext>` or `AsyncIterableIterator<T, TReturn, TNext>`
        // - `Generator<T, TReturn, TNext>` or `AsyncGenerator<T, TReturn, TNext>`
        if (IsReferenceToType(t, r._getGlobalIterableType!()) ||
            IsReferenceToType(t, r._getGlobalIteratorObjectType!()) ||
            IsReferenceToType(t, r._getGlobalIterableIteratorType!()) ||
            IsReferenceToType(t, r._getGlobalGeneratorType!()))
        {
            var typeArguments = GetTypeArgumentsWorker(t);
            return r.GetResolvedIterationTypes(typeArguments[0], typeArguments[1], typeArguments[2]);
        }
        // As an optimization, if the type is an instantiation of one of the following global types, then
        // just grab the related type argument:
        // - `ArrayIterator<T>`
        // - `MapIterator<T>`
        // - `SetIterator<T>`
        // - `StringIterator<T>`
        // - `ReadableStreamAsyncIterator<T>`
        if (IsReferenceToSomeType(t, r._getGlobalBuiltinIteratorTypes!()))
        {
            return r.GetResolvedIterationTypes(GetTypeArgumentsWorker(t)[0], GetBuiltinIteratorReturnType(), _unknownType);
        }
        return new IterationTypes();
    }

    internal bool IsReferenceToType(Type? t, Type target)
    {
        return t != null && (t._objectFlags & ObjectFlags.Reference) != 0 && t.Target() == target;
    }

    internal bool IsReferenceToSomeType(Type? t, Type[] targets)
    {
        return t != null && (t._objectFlags & ObjectFlags.Reference) != 0 && Array.IndexOf(targets, t.Target()!) >= 0;
    }

    internal Type GetBuiltinIteratorReturnType()
    {
        return IfElse(_strictBuiltinIteratorReturn, _undefinedType, _anyType);
    }

    internal IterationTypes CombineIterationTypes(IterationTypes[] iterationTypes)
    {
        return new IterationTypes(
            GetIterationTypeUnion(iterationTypes, t => t._yieldType),
            GetIterationTypeUnion(iterationTypes, t => t._returnType),
            GetIterationTypeUnion(iterationTypes, t => t._nextType));
    }

    internal Type? GetIterationTypeUnion(IterationTypes[] iterationTypes, Func<IterationTypes, Type?> f)
    {
        // Go: core.MapNonNil(iterationTypes, f)。lambda 内 `f(t)!` 只为让 U 推断成 Type（返回 Type[] 而非 Type?[]）；
        // 运行时仍是 MapNonNil 自己按 null 过滤，语义与 Go 一致。
        var types = MapNonNil(iterationTypes, t => f(t)!);
        if (types.Length == 0)
        {
            return null;
        }
        return GetUnionTypeWorker(types);
    }

    internal IterationTypes GetAsyncFromSyncIterationTypes(IterationTypes iterationTypes, Node? errorNode)
    {
        if (!iterationTypes.HasTypes() ||
            (iterationTypes._yieldType == _anyType && iterationTypes._returnType == _anyType && iterationTypes._nextType == _anyType))
        {
            return iterationTypes;
        }
        // if we're requesting diagnostics, report errors for a missing `Awaited<T>`.
        if (errorNode != null)
        {
            _getGlobalAwaitedSymbol();
        }
        return new IterationTypes(
            OrElse<Type?>(GetAwaitedTypeEx(iterationTypes._yieldType!, errorNode, null), _anyType),
            OrElse<Type?>(GetAwaitedTypeEx(iterationTypes._returnType!, errorNode, null), _anyType),
            iterationTypes._nextType);
    }

    // Gets the *yield*, *return*, and *next* types of an `Iterable`-like or `AsyncIterable`-like
    // type from its members.
    //
    // If we successfully found the *yield*, *return*, and *next* types, an `IterationTypes` with non-nil
    // members is returned. Otherwise, a default `IterationTypes{}` is returned.
    //
    // NOTE: You probably don't want to call this directly and should be calling
    // `getIterationTypesOfIterable` instead.
    internal IterationTypes GetIterationTypesOfIterableSlow(Type t, IterationTypesResolver r, Node? errorNode, ref Diagnostic[]? diagnosticOutput)
    {
        var method = GetPropertyOfTypeWorker(t, GetPropertyNameForKnownSymbolName(r._iteratorSymbolName));
        if (method != null && (method.Flags & SymbolFlags.Optional) == 0)
        {
            var methodType = GetTypeOfSymbolWorker(method);
            if (IsTypeAny(methodType))
            {
                return new IterationTypes(_anyType, _anyType, _anyType);
            }
            // Go: c.getSignaturesOfType(...) 的 nil slice 与空 slice 在这里等价（只看长度/遍历）。
            var allSignatures = GetSignaturesOfTypeWorker(methodType, SignatureKind.Call) ?? [];
            var validSignatures = Filter(allSignatures, sig => GetMinArgumentCount(sig) == 0);
            if (validSignatures.Length != 0)
            {
                var iteratorType = GetIntersectionType(Map(validSignatures, sig => GetReturnTypeOfSignatureWorker(sig)));
                return GetIterationTypesOfIteratorWorker(iteratorType, r, errorNode, ref diagnosticOutput);
            }
            if (errorNode != null && allSignatures.Length != 0)
            {
                // Go: c.checkTypeAssignableToEx(..., diagnosticOutput) 是**追加**写 *diagnosticOutput；C# 的
                // CheckTypeAssignableToEx 用 out（会覆盖），故这里手工合并，保持 Go 的追加语义。
                CheckTypeAssignableToEx(t, r._getGlobalIterableTypeChecked!(), errorNode, null, out var assignabilityDiagnostics);
                if (assignabilityDiagnostics is { Length: > 0 })
                {
                    diagnosticOutput = [.. diagnosticOutput ?? [], .. assignabilityDiagnostics];
                }
            }
        }
        return new IterationTypes();
    }

    // Gets the *yield*, *return*, and *next* types from an `Iterator`-like or `AsyncIterator`-like type.
    //
    // If we successfully found the *yield*, *return*, and *next* types, an `IterationTypes` with non-nil
    // members is returned. Otherwise, a default `IterationTypes{}` is returned.
    internal IterationTypes GetIterationTypesOfIterator(Type t, IterationTypesResolver r, Node? errorNode, ref Diagnostic[]? diagnosticOutput)
    {
        return GetIterationTypesOfIteratorWorker(t, r, errorNode, ref diagnosticOutput);
    }

    // Gets the *yield*, *return*, and *next* types from an `Iterator`-like or `AsyncIterator`-like type.
    //
    // If we successfully found the *yield*, *return*, and *next* types, an `IterationTypes` with non-nil
    // members is returned. Otherwise, a default `IterationTypes{}` is returned.
    //
    // NOTE: You probably don't want to call this directly and should be calling `getIterationTypesOfIterator` instead.
    internal IterationTypes GetIterationTypesOfIteratorWorker(Type t, IterationTypesResolver r, Node? errorNode, ref Diagnostic[]? diagnosticOutput)
    {
        if (IsTypeAny(t))
        {
            return new IterationTypes(_anyType, _anyType, _anyType);
        }
        var iterationTypes = GetIterationTypesOfIteratorFast(t, r);
        if (iterationTypes.HasTypes())
        {
            return iterationTypes;
        }
        return GetIterationTypesOfIteratorSlow(t, r, errorNode, ref diagnosticOutput);
    }

    internal IterationTypes GetIterationTypesOfIteratorFast(Type t, IterationTypesResolver r)
    {
        // As an optimization, if the type is an instantiation of the following global type, then
        // just grab its related type arguments:
        // - `Iterable<T, TReturn, TNext>` or `AsyncIterable<T, TReturn, TNext>`
        // - `IteratorObject<T, TReturn, TNext>` or `AsyncIteratorObject<T, TReturn, TNext>`
        // - `IterableIterator<T, TReturn, TNext>` or `AsyncIterableIterator<T, TReturn, TNext>`
        // - `Generator<T, TReturn, TNext>` or `AsyncGenerator<T, TReturn, TNext>`
        if (IsReferenceToType(t, r._getGlobalIteratorType!()) ||
            IsReferenceToType(t, r._getGlobalIteratorObjectType!()) ||
            IsReferenceToType(t, r._getGlobalIterableIteratorType!()) ||
            IsReferenceToType(t, r._getGlobalGeneratorType!()))
        {
            var typeArguments = GetTypeArgumentsWorker(t);
            return r.GetResolvedIterationTypes(typeArguments[0], typeArguments[1], typeArguments[2]);
        }
        // As an optimization, if the type is an instantiation of one of the following global types, then
        // just grab the related type argument:
        // - `ArrayIterator<T>`
        // - `MapIterator<T>`
        // - `SetIterator<T>`
        // - `StringIterator<T>`
        // - `ReadableStreamAsyncIterator<T>`
        if (IsReferenceToSomeType(t, r._getGlobalBuiltinIteratorTypes!()))
        {
            return r.GetResolvedIterationTypes(GetTypeArgumentsWorker(t)[0], GetBuiltinIteratorReturnType(), _unknownType);
        }
        return new IterationTypes();
    }

    internal IterationTypes GetIterationTypesOfIteratorSlow(Type t, IterationTypesResolver r, Node? errorNode, ref Diagnostic[]? diagnosticOutput)
    {
        return CombineIterationTypes([
            GetIterationTypesOfMethod(t, r, "next", errorNode, ref diagnosticOutput),
            GetIterationTypesOfMethod(t, r, "return", errorNode, ref diagnosticOutput),
            GetIterationTypesOfMethod(t, r, "throw", errorNode, ref diagnosticOutput),
        ]);
    }

    internal IterationTypes GetIterationTypesOfMethod(Type t, IterationTypesResolver resolver, string methodName, Node? errorNode, ref Diagnostic[]? diagnosticOutput)
    {
        var method = GetPropertyOfTypeWorker(t, methodName);
        // Ignore 'return' or 'throw' if they are missing.
        if (method == null && methodName != "next")
        {
            return new IterationTypes();
        }
        Type? methodType = null;
        if (method != null && !(methodName == "next" && (method.Flags & SymbolFlags.Optional) != 0))
        {
            if (methodName == "next")
            {
                methodType = GetTypeOfSymbolWorker(method);
            }
            else
            {
                methodType = GetTypeWithFacts(GetTypeOfSymbolWorker(method), TypeFacts.NEUndefinedOrNull);
            }
        }
        if (IsTypeAny(methodType))
        {
            return new IterationTypes(_anyType, _anyType, _anyType);
        }
        // Both async and non-async iterators *must* have a `next` method.
        // Go: methodSignatures 只在 methodType != nil 时取；nil slice 与空 slice 在下文只比较长度，故用 `?? []`。
        Signature[] methodSignatures = methodType != null ? GetSignaturesOfTypeWorker(methodType, SignatureKind.Call) ?? [] : [];
        if (methodSignatures.Length == 0)
        {
            if (errorNode != null)
            {
                var diagnostic = IfElse(methodName == "next", resolver._mustHaveANextMethodDiagnostic!, resolver._mustBeAMethodDiagnostic!);
                ReportDiagnostic(NewDiagnosticForNode(errorNode, diagnostic, methodName), ref diagnosticOutput);
            }
            return new IterationTypes();
        }
        // If the method signature comes exclusively from the global iterator or generator type,
        // create iteration types from its type arguments like `getIterationTypesOfIteratorFast`
        // does (so as to remove `undefined` from the next and return types). We arrive here when
        // a contextual type for a generator was not a direct reference to one of those global types,
        // but looking up `methodType` referred to one of them (and nothing else). E.g., in
        // `interface SpecialIterator extends Iterator<number> {}`, `SpecialIterator` is not a
        // reference to `Iterator`, but its `next` member derives exclusively from `Iterator`.
        if (methodSignatures.Length == 1 && methodType!._symbol != null)
        {
            var globalGeneratorType = resolver._getGlobalGeneratorType!();
            var globalIteratorType = resolver._getGlobalIteratorType!();
            var isGeneratorMethod = globalGeneratorType._symbol != null && globalGeneratorType._symbol.Members?.GetValueOrDefault(methodName) == methodType._symbol;
            var isIteratorMethod = !isGeneratorMethod && globalIteratorType._symbol != null && globalIteratorType._symbol.Members?.GetValueOrDefault(methodName) == methodType._symbol;
            if (isGeneratorMethod || isIteratorMethod)
            {
                var typeParameters = IfElse(isGeneratorMethod, globalGeneratorType, globalIteratorType).AsInterfaceType()!.TypeParameters();
                var mapper = methodType.Mapper();
                Type? nextType = null;
                if (methodName == "next")
                {
                    nextType = GetMappedType(typeParameters[2], mapper!);
                }
                return new IterationTypes(GetMappedType(typeParameters[0], mapper!), GetMappedType(typeParameters[1], mapper!), nextType);
            }
        }
        // Extract the first parameter and return type of each signature.
        List<Type>? methodParameterTypes = null;
        List<Type>? methodReturnTypes = null;
        foreach (var signature in methodSignatures)
        {
            if (methodName != "throw" && signature._parameters is { Length: > 0 })
            {
                methodParameterTypes ??= [];
                methodParameterTypes.Add(GetTypeAtPositionWorker(signature, 0));
            }
            methodReturnTypes ??= [];
            methodReturnTypes.Add(GetReturnTypeOfSignatureWorker(signature));
        }
        // Resolve the *next* or *return* type from the first parameter of a `next()` or
        // `return()` method, respectively.
        List<Type>? returnTypes = null;
        Type? nextType2 = null;
        if (methodName != "throw")
        {
            Type methodParameterType;
            if (methodParameterTypes != null)
            {
                methodParameterType = GetUnionTypeWorker(methodParameterTypes.ToArray());
            }
            else
            {
                methodParameterType = _unknownType;
            }
            if (methodName == "next")
            {
                // The value of `next(value)` is *not* awaited by async generators
                nextType2 = methodParameterType;
            }
            else if (methodName == "return")
            {
                // The value of `return(value)` *is* awaited by async generators
                var resolvedMethodParameterType = OrElse(resolver._resolveIterationType!(methodParameterType, errorNode), _anyType)!;
                returnTypes ??= [];
                returnTypes.Add(resolvedMethodParameterType);
            }
        }
        // Resolve the *yield* and *return* types from the return type of the method (i.e. `IteratorResult`)
        Type? yieldType;
        Type methodReturnType;
        if (methodReturnTypes != null)
        {
            methodReturnType = GetIntersectionType(methodReturnTypes.ToArray());
        }
        else
        {
            methodReturnType = _neverType;
        }
        var resolvedMethodReturnType = OrElse(resolver._resolveIterationType!(methodReturnType, errorNode), _anyType)!;
        var iterationTypes = GetIterationTypesOfIteratorResult(resolvedMethodReturnType);
        if (!iterationTypes.HasTypes())
        {
            if (errorNode != null)
            {
                ReportDiagnostic(NewDiagnosticForNode(errorNode, resolver._mustHaveAValueDiagnostic!, methodName), ref diagnosticOutput);
            }
            yieldType = _anyType;
            returnTypes ??= [];
            returnTypes.Add(_anyType);
        }
        else
        {
            yieldType = iterationTypes._yieldType;
            returnTypes ??= [];
            returnTypes.Add(iterationTypes._returnType!);
        }
        return new IterationTypes(yieldType, GetUnionTypeWorker(returnTypes!.ToArray()), nextType2);
    }

    // Gets the *yield* and *return* types of an `IteratorResult`-like type.
    //
    // If we are unable to determine a *yield* or a *return* type, `noIterationTypes` is
    // returned to indicate to the caller that it should handle the error. Otherwise, an
    // `IterationTypes` record is returned.
    internal IterationTypes GetIterationTypesOfIteratorResult(Type t)
    {
        if (IsTypeAny(t))
        {
            return new IterationTypes(_anyType, _anyType, _anyType);
        }
        // As an optimization, if the type is an instantiation of one of the global `IteratorYieldResult<T>`
        // or `IteratorReturnResult<TReturn>` types, then just grab its type argument.
        if (IsReferenceToType(t, _getGlobalIteratorYieldResultType()))
        {
            return new IterationTypes(GetTypeArgumentsWorker(t)[0], null, null);
        }
        if (IsReferenceToType(t, _getGlobalIteratorReturnResultType()))
        {
            return new IterationTypes(null, GetTypeArgumentsWorker(t)[0], null);
        }
        // Choose any constituents that can produce the requested iteration type.
        var yieldIteratorResult = FilterType(t, IsYieldIteratorResult);
        Type? yieldType = null;
        if (yieldIteratorResult != _neverType)
        {
            yieldType = GetTypeOfPropertyOfTypeWorker(yieldIteratorResult, "value" /* as __String */);
        }
        var returnIteratorResult = FilterType(t, IsReturnIteratorResult);
        Type? returnType = null;
        if (returnIteratorResult != _neverType)
        {
            returnType = GetTypeOfPropertyOfTypeWorker(returnIteratorResult, "value" /* as __String */);
        }
        if (yieldType == null && returnType == null)
        {
            return new IterationTypes();
        }
        // From https://tc39.github.io/ecma262/#sec-iteratorresult-interface
        // > ... If the iterator does not have a return value, `value` is `undefined`. In that case, the
        // > `value` property may be absent from the conforming object if it does not inherit an explicit
        // > `value` property.
        return new IterationTypes(yieldType, OrElse<Type?>(returnType, _voidType), null);
    }

    internal bool IsYieldIteratorResult(Type t)
    {
        return IsIteratorResult(t, IterationTypeKind.Yield);
    }

    internal bool IsReturnIteratorResult(Type t)
    {
        return IsIteratorResult(t, IterationTypeKind.Return);
    }

    internal bool IsIteratorResult(Type t, IterationTypeKind kind)
    {
        // From https://tc39.github.io/ecma262/#sec-iteratorresult-interface:
        // > [done] is the result status of an iterator `next` method call. If the end of the iterator was reached `done` is `true`.
        // > If the end was not reached `done` is `false` and a value is available.
        // > If a `done` property (either own or inherited) does not exist, it is consider to have the value `false`.
        var doneType = OrElse<Type?>(GetTypeOfPropertyOfTypeWorker(t, "done"), _falseType);
        return IsTypeAssignableToWorker(IfElse(kind == IterationTypeKind.Yield, _falseType, _trueType), doneType!);
    }

    internal Diagnostic ReportTypeNotIterableError(Node errorNode, Type t, bool allowAsyncIterables)
    {
        Message message;
        if (allowAsyncIterables)
        {
            message = Diagnostics.Type_0_must_have_a_Symbol_asyncIterator_method_that_returns_an_async_iterator;
        }
        else
        {
            message = Diagnostics.Type_0_must_have_a_Symbol_iterator_method_that_returns_an_iterator;
        }
        var suggestAwait = GetAwaitedTypeOfPromise(t) != null || (!allowAsyncIterables &&
            IsForOfStatement(errorNode.Parent!) &&
            errorNode.Parent!.Expression() == errorNode &&
            _getGlobalAsyncIterableType() != _emptyGenericType &&
            IsTypeAssignableToWorker(t, CreateTypeFromGenericGlobalType(_getGlobalAsyncIterableType(), [_anyType, _anyType, _anyType])));
        return ErrorAndMaybeSuggestAwait(errorNode, suggestAwait, message, TypeToString(t));
    }

    internal bool TryGetIterationDiagnosticDetails(IterationUse use, Type inputType, bool allowsStrings, out Message message)
    {
        var yieldType = GetIterationTypeOfIterable(use, IterationTypeKind.Yield, inputType, null /*errorNode*/);
        if (yieldType != null)
        {
            message = Diagnostics.Type_0_can_only_be_iterated_through_when_using_the_downlevelIteration_flag_or_with_a_target_of_es2015_or_higher;
            return false;
        }
        if (inputType._symbol != null && IsES2015OrLaterIterable(inputType._symbol.Name))
        {
            message = Diagnostics.Type_0_can_only_be_iterated_through_when_using_the_downlevelIteration_flag_or_with_a_target_of_es2015_or_higher;
            return true;
        }
        if (allowsStrings)
        {
            message = Diagnostics.Type_0_is_not_an_array_type_or_a_string_type;
            return true;
        }
        message = Diagnostics.Type_0_is_not_an_array_type;
        return true;
    }

    internal static bool IsES2015OrLaterIterable(string n)
    {
        switch (n)
        {
            case "Float32Array":
            case "Float64Array":
            case "Int16Array":
            case "Int32Array":
            case "Int8Array":
            case "NodeList":
            case "Uint16Array":
            case "Uint32Array":
            case "Uint8Array":
            case "Uint8ClampedArray":
                return true;
        }
        return false;
    }
}

/// <summary>Go: checker.go:6572/6576 的 `func (iterationTypes *IterationTypes) hasTypes()/getType(...)`。</summary>
public sealed partial class IterationTypes
{
    internal bool HasTypes()
    {
        return _yieldType != null || _returnType != null || _nextType != null;
    }

    internal Type? GetType(IterationTypeKind typeKind)
    {
        switch (typeKind)
        {
            case IterationTypeKind.Yield:
                return _yieldType;
            case IterationTypeKind.Return:
                return _returnType;
            case IterationTypeKind.Next:
                return _nextType;
        }
        Debug.Fail("Unhandled case in getType(IterationTypeKind)");
        throw new System.Diagnostics.UnreachableException();
    }
}

/// <summary>Go: checker.go:6552 的 `func (r *IterationTypesResolver) getResolvedIterationTypes(...)`。</summary>
public sealed partial class IterationTypesResolver
{
    internal IterationTypes GetResolvedIterationTypes(Type? yieldType, Type? returnType, Type? nextType)
    {
        // Go: core.OrElse(r.resolveIterationType(yieldType, nil), yieldType)。`_resolveIterationType` 的第一参声明为
        // 非空（Checker.Keys.cs），与 Go 侧"调用方恒传 typeArguments[i]（非 nil）"的假设一致，故写 `!`。
        return new IterationTypes(
            OrElse<Type?>(_resolveIterationType!(yieldType!, null /*errorNode*/), yieldType),
            OrElse<Type?>(_resolveIterationType!(returnType!, null /*errorNode*/), returnType),
            nextType);
    }
}
