// ported from: internal/checker/checker.go:20339-20952
// （getReturnTypeOfSignature … addOptionalTypeMarker：返回类型推导、promise/generator 返回类型、
//   隐式 any 放宽报错、类型谓词推导、签名/符号表/indexInfo 的实例化入口）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (c *Checker) x()` → 实例方法（PascalCase / 未导出为 internal）；Go 的**包级函数**
//    （无接收者）→ 同一个 `partial class Checker` 的 `internal static`/`public static` 成员
//    （Parser/Binder/Printer 先例，docs/architecture.md §2）。类声明全仓统一 `public sealed partial class Checker`。
// 2) 命名：Go 未导出**字段** → `_camelCase`（`sig.resolvedReturnType` → `sig._resolvedReturnType`、
//    `t.flags` → `t._flags`、`c.errorType` → `_errorType`）；Go 导出字段 → 原名 PascalCase；
//    Go 未导出**方法** → `internal` + PascalCase；枚举成员名 = Go 常量名去类型前缀。
// 3) Go 的多返回值：`(T, bool)` 之外的两处多值返回用 C# 元组（见下），保持与 Go 调用点逐字对应。
//    `checkAndAggregateReturnExpressionTypes` → `(Type[]?, bool)` 元组（bool 是 "never-returning"，
//    不是"查找成功"，所以不用 `TryX(..., out T)`）。
// 4) `core.X`/`ast.X`/`diagnostics.X`/`scanner.X` 一律 `using static` + 裸名（与 Go 的包前缀一一对应）。
// 5) `slices.Index(node.Parameters(), parameter)` → `Array.IndexOf(node.Parameters()!, parameter)`（找不到返回 -1，一致）。
// 6) `c.isEmptyObjectType` 是 Go 的方法值 → C# 的 lambda（实例方法组在部分场景不能直接转委托）。
// 7) `[]*Type{a, b}` → `new Type[] { a, b }`。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。本命名空间作用域内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type GetReturnTypeOfSignatureWorker(Signature sig)
    {
        if (sig._resolvedReturnType != null)
        {
            return sig._resolvedReturnType;
        }
        if (!PushTypeResolution(sig, TypeSystemPropertyName.ResolvedReturnType))
        {
            return _errorType!;
        }
        Type? t;
        if (sig._target != null)
        {
            t = InstantiateType(GetReturnTypeOfSignatureWorker(sig._target), sig._mapper);
        }
        else if (sig._composite != null)
        {
            t = InstantiateType(GetUnionOrIntersectionType(Core.Map(sig._composite._signatures, s => GetReturnTypeOfSignatureWorker(s)), sig._composite._isUnion, UnionReduction.Subtype), sig._mapper);
        }
        else
        {
            t = GetReturnTypeFromAnnotation(sig._declaration!);
            if (t == null)
            {
                if (!NodeIsMissing(sig._declaration!.Body()))
                {
                    t = GetReturnTypeFromBody(sig._declaration, CheckMode.Normal);
                }
                else
                {
                    t = _anyType!;
                }
            }
        }
        if ((sig._flags & SignatureFlags.IsInnerCallChain) != 0)
        {
            t = AddOptionalTypeMarker(t!);
        }
        else if ((sig._flags & SignatureFlags.IsOuterCallChain) != 0)
        {
            t = GetOptionalType(t!, false /*isProperty*/);
        }
        if (!PopTypeResolution())
        {
            if (sig._declaration != null)
            {
                var typeNode = sig._declaration.Type();
                if (typeNode != null)
                {
                    Error(typeNode, Return_type_annotation_circularly_references_itself);
                }
                else if (_noImplicitAny)
                {
                    var name = GetNameOfDeclaration(sig._declaration);
                    if (name != null)
                    {
                        Error(name, X_0_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions, DeclarationNameToString(name));
                    }
                    else
                    {
                        Error(sig._declaration, Function_implicitly_has_return_type_any_because_it_does_not_have_a_return_type_annotation_and_is_referenced_directly_or_indirectly_in_one_of_its_return_expressions);
                    }
                }
            }
            t = _anyType!;
        }
        sig._resolvedReturnType ??= t!;
        return sig._resolvedReturnType!;
    }

    internal Type GetNonCircularReturnTypeOfSignature(Signature sig)
    {
        if (IsResolvingReturnTypeOfSignature(sig))
        {
            return _anyType!;
        }
        return GetReturnTypeOfSignatureWorker(sig);
    }

    internal Type? GetReturnTypeFromAnnotation(Node declaration)
    {
        if (IsConstructorDeclaration(declaration))
        {
            return GetDeclaredTypeOfClassOrInterface(GetMergedSymbolWorker(declaration.Parent!.Symbol!)!);
        }
        var returnType = declaration.Type();
        if (returnType != null)
        {
            return GetTypeFromTypeNodeWorker(returnType);
        }
        if (IsGetAccessorDeclaration(declaration) && HasBindableName(declaration))
        {
            return GetAnnotatedAccessorType(GetDeclarationOfKind(GetSymbolOfDeclaration(declaration), Kind.SetAccessor)!);
        }
        return GetReturnTypeOfFullSignature(declaration);
    }

    internal Signature? GetSignatureOfFullSignatureType(Node node)
    {
        var fullSignature = node.FunctionLikeData()?.FullSignature;
        if (IsInJSFile(node) && (IsFunctionDeclaration(node) || IsMethodDeclaration(node) || IsFunctionExpressionOrArrowFunction(node)) && fullSignature != null)
        {
            return GetSingleCallSignature(GetTypeFromTypeNodeWorker(fullSignature));
        }
        return null;
    }

    internal Type? GetParameterTypeOfFullSignature(Node node, Node parameter)
    {
        var signature = GetSignatureOfFullSignatureType(node);
        if (signature != null)
        {
            var pos = Array.IndexOf(node.Parameters()!, parameter);
            if (parameter.AsParameterDeclaration()!.DotDotDotToken != null)
            {
                return GetRestTypeAtPosition(signature, pos, false /*readonly*/);
            }
            else
            {
                return GetTypeAtPositionWorker(signature, pos);
            }
        }
        return null;
    }

    internal Type? GetReturnTypeOfFullSignature(Node node)
    {
        var signature = GetSignatureOfFullSignatureType(node);
        if (signature != null)
        {
            return GetReturnTypeOfSignatureWorker(signature);
        }
        return null;
    }

    internal Type? GetAnnotatedAccessorType(Node accessor)
    {
        var node = GetAnnotatedAccessorTypeNode(accessor);
        if (node != null)
        {
            return GetTypeFromTypeNodeWorker(node);
        }
        return null;
    }

    internal Node? GetAnnotatedAccessorTypeNode(Node? accessor)
    {
        if (accessor != null)
        {
            switch (accessor.Kind)
            {
                case Kind.GetAccessor:
                case Kind.PropertyDeclaration:
                    return accessor.Type();
                case Kind.SetAccessor:
                    return GetEffectiveSetAccessorTypeAnnotationNode(accessor);
            }
        }
        return null;
    }

    internal static Node? GetEffectiveSetAccessorTypeAnnotationNode(Node node)
    {
        var param = GetSetAccessorValueParameter(node);
        if (param != null)
        {
            return param.Type();
        }
        return null;
    }

    internal Type GetReturnTypeFromBody(Node fn, CheckMode checkMode)
    {
        var body = fn.Body();
        if (body == null)
        {
            return _errorType!;
        }
        var functionFlags = GetFunctionFlags(fn);
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        var isGenerator = (functionFlags & FunctionFlags.Generator) != 0;
        Type? returnType = null;
        Type? yieldType = null;
        Type? nextType = null;
        Type? fallbackReturnType = _voidType;
        if (!IsBlock(body))
        {
            returnType = CheckExpressionCachedEx(body, checkMode & ~CheckMode.SkipGenericFunctions);
            if (IsConstContext(body))
            {
                returnType = GetRegularTypeOfLiteralType(returnType!);
            }
            if (isAsync)
            {
                // From within an async function you can return either a non-promise value or a promise. Any
                // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
                // return type of the body should be unwrapped to its awaited type, which we will wrap in
                // the native Promise<T> type later in this function.
                returnType = UnwrapAwaitedType(CheckAwaitedType(returnType, false /*withAlias*/, fn /*errorNode*/, The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member));
            }
        }
        else if (isGenerator)
        {
            var (returnTypes, isNeverReturning) = CheckAndAggregateReturnExpressionTypes(fn, checkMode);
            if (isNeverReturning)
            {
                fallbackReturnType = _neverType;
            }
            else if ((returnTypes?.Length ?? 0) != 0)
            {
                returnType = GetUnionTypeExWorker(returnTypes!, UnionReduction.Subtype, null, null);
            }
            var (yieldTypes, nextTypes) = CheckAndAggregateYieldOperandTypes(fn, checkMode);
            if ((yieldTypes?.Length ?? 0) != 0)
            {
                yieldType = GetUnionTypeExWorker(yieldTypes!, UnionReduction.Subtype, null, null);
            }
            if ((nextTypes?.Length ?? 0) != 0)
            {
                nextType = GetIntersectionType(nextTypes!);
            }
        }
        else
        {
            var (types, isNeverReturning) = CheckAndAggregateReturnExpressionTypes(fn, checkMode);
            if (isNeverReturning)
            {
                // For an async function, the return type will not be never, but rather a Promise for never.
                if ((functionFlags & FunctionFlags.Async) != 0)
                {
                    return CreatePromiseReturnType(fn, _neverType!);
                }
                // Normal function
                return _neverType!;
            }
            if ((types?.Length ?? 0) == 0)
            {
                // For an async function, the return type will not be void/undefined, but rather a Promise for void/undefined.
                var contextualReturnType = GetContextualReturnType(fn, ContextFlags.None);
                Type returnType0;
                if (contextualReturnType != null && SomeType(Core.OrElse(UnwrapReturnType(contextualReturnType, functionFlags), _voidType), t => (t._flags & TypeFlags.Undefined) != 0))
                {
                    returnType0 = _undefinedType!;
                }
                else
                {
                    returnType0 = _voidType!;
                }
                if ((functionFlags & FunctionFlags.Async) != 0)
                {
                    return CreatePromiseReturnType(fn, returnType0);
                }
                // Normal function
                return returnType0;
            }
            // Return a union of the return expression types.
            returnType = GetUnionTypeExWorker(types!, UnionReduction.Subtype, null, null);
        }
        if (returnType != null || yieldType != null || nextType != null)
        {
            if (yieldType != null)
            {
                ReportErrorsFromWidening(fn, yieldType, WideningKind.GeneratorYield);
            }
            if (returnType != null)
            {
                ReportErrorsFromWidening(fn, returnType, WideningKind.FunctionReturn);
            }
            if (nextType != null)
            {
                ReportErrorsFromWidening(fn, nextType, WideningKind.GeneratorNext);
            }
            if (returnType != null && IsUnitType(returnType) || yieldType != null && IsUnitType(yieldType) || nextType != null && IsUnitType(nextType))
            {
                var contextualSignature = GetContextualSignatureForFunctionLikeDeclaration(fn);
                Type? contextualType = null;
                if (contextualSignature == null)
                {
                    // No contextual type
                }
                else if (contextualSignature == GetSignatureFromDeclarationWorker(fn))
                {
                    if (!isGenerator)
                    {
                        contextualType = returnType;
                    }
                }
                else
                {
                    contextualType = InstantiateContextualType(GetReturnTypeOfSignatureWorker(contextualSignature), fn, ContextFlags.None);
                }
                if (isGenerator)
                {
                    yieldType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(yieldType, contextualType, IterationTypeKind.Yield, isAsync);
                    returnType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(returnType, contextualType, IterationTypeKind.Return, isAsync);
                    nextType = GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(nextType, contextualType, IterationTypeKind.Next, isAsync);
                }
                else
                {
                    returnType = GetWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded(returnType, contextualType, isAsync);
                }
            }
            if (yieldType != null)
            {
                yieldType = GetWidenedTypeWorker(yieldType);
            }
            if (returnType != null)
            {
                returnType = GetWidenedTypeWorker(returnType);
            }
            if (nextType != null)
            {
                nextType = GetWidenedTypeWorker(nextType);
            }
        }
        returnType ??= fallbackReturnType;
        if (isGenerator)
        {
            yieldType ??= _neverType;
            if (nextType == null)
            {
                nextType = GetContextualIterationType(IterationTypeKind.Next, fn);
                if (nextType == null)
                {
                    nextType = _unknownType!;
                }
            }
            return CreateGeneratorType(yieldType!, returnType!, nextType, isAsync);
        }
        // From within an async function you can return either a non-promise value or a promise. Any
        // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
        // return type of the body is awaited type of the body, wrapped in a native Promise<T> type.
        if (isAsync)
        {
            return CreatePromiseType(returnType!);
        }
        return returnType!;
    }

    /// <summary>
    /// Returns the aggregated list of return types, plus a bool indicating a never-returning function.
    /// Go: <c>func (c *Checker) checkAndAggregateReturnExpressionTypes(fn *ast.Node, checkMode CheckMode) ([]*Type, bool)</c>。
    /// 偏离：Go 的 `([]*Type, bool)` → C# 元组（bool 是 "never-returning"，不是查询成败）。
    /// </summary>
    internal (Type[]? Types, bool IsNeverReturning) CheckAndAggregateReturnExpressionTypes(Node fn, CheckMode checkMode)
    {
        var functionFlags = GetFunctionFlags(fn);
        Type[]? aggregatedTypes = null;
        var hasReturnWithNoExpression = FunctionHasImplicitReturn(fn);
        var hasReturnOfTypeNever = false;
        ForEachReturnStatement(fn.Body(), returnStatement =>
        {
            var expr = returnStatement.Expression();
            if (expr == null)
            {
                hasReturnWithNoExpression = true;
                return false;
            }
            expr = SkipParentheses(expr)!;
            // Bare calls to this same function don't contribute to inference
            // and `return await` is also safe to unwrap here
            if ((functionFlags & FunctionFlags.Async) != 0 && IsAwaitExpression(expr!))
            {
                expr = SkipParentheses(expr!.Expression()!);
            }
            if (IsCallExpression(expr!) && IsIdentifier(expr!.Expression()!) && CheckExpressionCached(expr.Expression()!)._symbol == GetMergedSymbolWorker(fn.Symbol!) &&
                (!IsFunctionExpressionOrArrowFunction(fn.Symbol!.ValueDeclaration!) || IsConstantReference(expr.Expression()!)))
            {
                hasReturnOfTypeNever = true;
                return false;
            }
            var t = CheckExpressionCachedEx(expr!, checkMode & ~CheckMode.SkipGenericFunctions);
            if ((functionFlags & FunctionFlags.Async) != 0)
            {
                // From within an async function you can return either a non-promise value or a promise. Any
                // Promise/A+ compatible implementation will always assimilate any foreign promise, so the
                // return type of the body should be unwrapped to its awaited type, which should be wrapped in
                // the native Promise<T> type by the caller.
                t = UnwrapAwaitedType(CheckAwaitedType(t, false /*withAlias*/, fn, The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member));
            }
            if ((t._flags & TypeFlags.Never) != 0)
            {
                hasReturnOfTypeNever = true;
            }
            if (IsConstContext(expr!))
            {
                t = GetRegularTypeOfLiteralType(t);
            }
            aggregatedTypes = Core.AppendIfUnique(aggregatedTypes, t);
            return false;
        });
        if ((aggregatedTypes?.Length ?? 0) == 0 && !hasReturnWithNoExpression && (hasReturnOfTypeNever || MayReturnNever(fn)))
        {
            return (null, true);
        }
        if (_strictNullChecks && (aggregatedTypes?.Length ?? 0) != 0 && hasReturnWithNoExpression)
        {
            aggregatedTypes = Core.AppendIfUnique(aggregatedTypes, _undefinedType!);
        }
        return (aggregatedTypes, false);
    }

    internal bool FunctionHasImplicitReturn(Node fn)
    {
        var endFlowNode = fn.BodyData()?.EndFlowNode;
        return endFlowNode != null && IsReachableFlowNode(endFlowNode);
    }

    internal static bool MayReturnNever(Node fn)
    {
        switch (fn.Kind)
        {
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                return true;
            case Kind.MethodDeclaration:
                return IsObjectLiteralExpression(fn.Parent!);
        }
        return false;
    }

    internal (Type[]? YieldTypes, Type[]? NextTypes) CheckAndAggregateYieldOperandTypes(Node fn, CheckMode checkMode)
    {
        var isAsync = (GetFunctionFlags(fn) & FunctionFlags.Async) != 0;
        Type[]? yieldTypes = null;
        Type[]? nextTypes = null;
        ForEachYieldExpression(fn.Body()!, yieldExpr =>
        {
            var yieldExprType = _undefinedWideningType!;
            if (yieldExpr.Expression() != null)
            {
                yieldExprType = CheckExpressionEx(yieldExpr.Expression()!, checkMode & ~CheckMode.SkipGenericFunctions);
            }
            if (yieldExpr.Expression() != null && IsConstContext(yieldExpr.Expression()!))
            {
                yieldExprType = GetRegularTypeOfLiteralType(yieldExprType);
            }
            yieldTypes = Core.AppendIfUnique(yieldTypes, GetYieldedTypeOfYieldExpression(yieldExpr, yieldExprType, _anyType!, isAsync));
            Type? nextType;
            if (yieldExpr.AsYieldExpression()!.AsteriskToken != null)
            {
                var iterationTypes = GetIterationTypesOfIterable(yieldExprType, Core.IfElse(isAsync, IterationUse.AsyncYieldStar, IterationUse.YieldStar), yieldExpr.Expression());
                nextType = iterationTypes._nextType;
            }
            else
            {
                nextType = GetContextualTypeWorker(yieldExpr, ContextFlags.None);
            }
            if (nextType != null)
            {
                nextTypes = Core.AppendIfUnique(nextTypes, nextType);
            }
            return false;
        });
        return (yieldTypes, nextTypes);
    }

    internal Type CreatePromiseType(Type promisedType)
    {
        // creates a `Promise<T>` type where `T` is the promisedType argument
        var globalPromiseType = _getGlobalPromiseTypeChecked();
        if (globalPromiseType != _emptyGenericType)
        {
            // if the promised type is itself a promise, get the underlying type; otherwise, fallback to the promised type
            // Unwrap an `Awaited<T>` to `T` to improve inference.
            promisedType = Core.OrElse(GetAwaitedTypeNoAlias(UnwrapAwaitedType(promisedType)), _unknownType)!;
            return CreateTypeReference(globalPromiseType, new Type[] { promisedType });
        }
        return _unknownType!;
    }

    internal Type CreatePromiseLikeType(Type promisedType)
    {
        // creates a `PromiseLike<T>` type where `T` is the promisedType argument
        var globalPromiseLikeType = _getGlobalPromiseLikeType();
        if (globalPromiseLikeType != _emptyGenericType)
        {
            // if the promised type is itself a promise, get the underlying type; otherwise, fallback to the promised type
            // Unwrap an `Awaited<T>` to `T` to improve inference.
            promisedType = Core.OrElse(GetAwaitedTypeNoAlias(UnwrapAwaitedType(promisedType)), _unknownType)!;
            return CreateTypeReference(globalPromiseLikeType, new Type[] { promisedType });
        }
        return _unknownType!;
    }

    internal Type CreatePromiseReturnType(Node fn, Type promisedType)
    {
        var promiseType = CreatePromiseType(promisedType);
        if (promiseType == _unknownType)
        {
            Error(fn, Core.IfElse(IsImportCall(fn),
                A_dynamic_import_call_returns_a_Promise_Make_sure_you_have_a_declaration_for_Promise_or_include_ES2015_in_your_lib_option,
                An_async_function_or_method_must_return_a_Promise_Make_sure_you_have_a_declaration_for_Promise_or_include_ES2015_in_your_lib_option));
            return _errorType!;
        }
        if (_getGlobalPromiseConstructorSymbol() == null)
        {
            Error(fn, Core.IfElse(IsImportCall(fn),
                A_dynamic_import_call_in_ES5_requires_the_Promise_constructor_Make_sure_you_have_a_declaration_for_the_Promise_constructor_or_include_ES2015_in_your_lib_option,
                An_async_function_or_method_in_ES5_requires_the_Promise_constructor_Make_sure_you_have_a_declaration_for_the_Promise_constructor_or_include_ES2015_in_your_lib_option));
        }
        return promiseType;
    }

    internal Type UnwrapReturnType(Type returnType, FunctionFlags functionFlags)
    {
        var isGenerator = (functionFlags & FunctionFlags.Generator) != 0;
        var isAsync = (functionFlags & FunctionFlags.Async) != 0;
        if (isGenerator)
        {
            var returnIterationType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, isAsync);
            if (returnIterationType == null)
            {
                return _errorType!;
            }
            if (isAsync)
            {
                return GetAwaitedTypeNoAlias(UnwrapAwaitedType(returnIterationType))!;
            }
            return returnIterationType;
        }
        if (isAsync)
        {
            return Core.OrElse(GetAwaitedTypeNoAlias(returnType), _errorType)!;
        }
        return returnType;
    }

    internal Type? GetWidenedLiteralLikeTypeForContextualReturnTypeIfNeeded(Type? t, Type? contextualSignatureReturnType, bool isAsync)
    {
        if (t != null && IsUnitType(t))
        {
            Type? contextualType;
            if (contextualSignatureReturnType == null)
            {
                // No contextual type
                contextualType = null;
            }
            else if (isAsync)
            {
                contextualType = GetPromisedTypeOfPromise(contextualSignatureReturnType);
            }
            else
            {
                contextualType = contextualSignatureReturnType;
            }
            t = GetWidenedLiteralLikeTypeForContextualType(t, contextualType);
        }
        return t;
    }

    internal Type? GetWidenedLiteralLikeTypeForContextualIterationTypeIfNeeded(Type? t, Type? contextualSignatureReturnType, IterationTypeKind kind, bool isAsyncGenerator)
    {
        if (t != null && IsUnitType(t))
        {
            Type? contextualType = null;
            if (contextualSignatureReturnType != null)
            {
                contextualType = GetIterationTypeOfGeneratorFunctionReturnType(kind, contextualSignatureReturnType, isAsyncGenerator);
            }
            t = GetWidenedLiteralLikeTypeForContextualType(t, contextualType);
        }
        return t;
    }

    internal Type CreateGeneratorType(Type yieldType, Type returnType, Type nextType, bool isAsyncGenerator)
    {
        var resolver = Core.IfElse(isAsyncGenerator, _asyncIterationTypesResolver, _syncIterationTypesResolver)!;
        var globalGeneratorType = resolver._getGlobalGeneratorType!();
        yieldType = Core.OrElse(resolver._resolveIterationType!(yieldType, null /*errorNode*/), _unknownType)!;
        returnType = Core.OrElse(resolver._resolveIterationType!(returnType, null /*errorNode*/), _unknownType)!;
        if (globalGeneratorType == _emptyGenericType)
        {
            // Fall back to the global IterableIterator type.
            var globalIterableIteratorType = resolver._getGlobalIterableIteratorType!();
            if (globalIterableIteratorType != _emptyGenericType)
            {
                return CreateTypeFromGenericGlobalType(globalIterableIteratorType, new Type[] { yieldType, returnType, nextType });
            }
            // The global Generator type doesn't exist, so report an error
            resolver._getGlobalIterableIteratorTypeChecked!();
            return _emptyObjectType!;
        }
        return CreateTypeFromGenericGlobalType(globalGeneratorType, new Type[] { yieldType, returnType, nextType });
    }

    internal void ReportErrorsFromWidening(Node declaration, Type t, WideningKind wideningKind)
    {
        if (_noImplicitAny && (t._objectFlags & ObjectFlags.ContainsWideningType) != 0)
        {
            if (wideningKind == WideningKind.Normal || IsFunctionLikeDeclaration(declaration) && ShouldReportErrorsFromWideningWithContextualSignature(declaration, wideningKind))
            {
                // Report implicit any error within type if possible, otherwise report error on declaration
                if (!ReportWideningErrorsInType(t))
                {
                    ReportImplicitAny(declaration, t, wideningKind);
                }
            }
        }
    }

    internal bool ShouldReportErrorsFromWideningWithContextualSignature(Node declaration, WideningKind wideningKind)
    {
        var signature = GetContextualSignatureForFunctionLikeDeclaration(declaration);
        if (signature == null)
        {
            return true;
        }
        var returnType = GetReturnTypeOfSignatureWorker(signature);
        var flags = GetFunctionFlags(declaration);
        switch (wideningKind)
        {
            case WideningKind.FunctionReturn:
                if ((flags & FunctionFlags.Generator) != 0)
                {
                    returnType = Core.OrElse(GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Return, returnType, (flags & FunctionFlags.Async) != 0), returnType)!;
                }
                else if ((flags & FunctionFlags.Async) != 0)
                {
                    returnType = Core.OrElse(GetAwaitedTypeNoAlias(returnType), returnType)!;
                }
                return IsGenericType(returnType);
            case WideningKind.GeneratorYield:
                var yieldType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Yield, returnType, (flags & FunctionFlags.Async) != 0);
                return yieldType != null && IsGenericType(yieldType);
            case WideningKind.GeneratorNext:
                var nextType = GetIterationTypeOfGeneratorFunctionReturnType(IterationTypeKind.Next, returnType, (flags & FunctionFlags.Async) != 0);
                return nextType != null && IsGenericType(nextType);
        }
        return false;
    }

    // Reports implicit any errors that occur as a result of widening 'null' and 'undefined'
    // to 'any'. A call to reportWideningErrorsInType is normally accompanied by a call to
    // getWidenedType. But in some cases getWidenedType is called without reporting errors
    // (type argument inference is an example).
    //
    // The return value indicates whether an error was in fact reported. The particular circumstances
    // are on a best effort basis. Currently, if the null or undefined that causes widening is inside
    // an object literal property (arbitrarily deeply), this function reports an error. If no error is
    // reported, reportImplicitAnyError is a suitable fallback to report a general error.
    internal bool ReportWideningErrorsInType(Type t)
    {
        var errorReported = false;
        if ((t._objectFlags & ObjectFlags.ContainsWideningType) != 0)
        {
            if ((t._flags & TypeFlags.Union) != 0)
            {
                if (Core.Some(t.Types(), s => IsEmptyObjectType(s)))
                {
                    errorReported = true;
                }
                else
                {
                    foreach (var s in t.Types())
                    {
                        errorReported = errorReported || ReportWideningErrorsInType(s);
                    }
                }
            }
            else if (IsArrayOrTupleType(t))
            {
                foreach (var s in GetTypeArgumentsWorker(t))
                {
                    errorReported = errorReported || ReportWideningErrorsInType(s);
                }
            }
            else if (IsObjectLiteralType(t))
            {
                foreach (var p in GetPropertiesOfObjectType(t)!)
                {
                    var s = GetTypeOfSymbolWorker(p);
                    if ((s._objectFlags & ObjectFlags.ContainsWideningType) != 0)
                    {
                        errorReported = ReportWideningErrorsInType(s);
                        if (!errorReported)
                        {
                            // we need to account for property types coming from object literal type normalization in unions
                            var valueDeclaration = Core.Find(p.Declarations, d =>
                            {
                                var valueDeclaration = d.Symbol!.ValueDeclaration;
                                return valueDeclaration != null && valueDeclaration.Parent == t._symbol!.ValueDeclaration;
                            });
                            if (valueDeclaration != null)
                            {
                                Error(valueDeclaration, Object_literal_s_property_0_implicitly_has_an_1_type, SymbolToStringWorker(p), TypeToString(GetWidenedTypeWorker(s)));
                                errorReported = true;
                            }
                        }
                    }
                }
            }
        }
        return errorReported;
    }

    internal TypePredicate? GetTypePredicateFromBody(Node fn)
    {
        switch (fn.Kind)
        {
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return null;
        }
        var functionFlags = GetFunctionFlags(fn);
        if (functionFlags != FunctionFlags.Normal)
        {
            return null;
        }
        // Only attempt to infer a type predicate if there's exactly one return.
        Node? singleReturn = null;
        var body = fn.Body();
        if (body != null && !IsBlock(body))
        {
            // arrow function
            singleReturn = body;
        }
        else
        {
            var bailedEarly = ForEachReturnStatement(body, returnStatement =>
            {
                if (singleReturn != null || returnStatement.Expression() == null)
                {
                    return true;
                }
                singleReturn = returnStatement.Expression();
                return false;
            });
            if (bailedEarly || singleReturn == null || FunctionHasImplicitReturn(fn))
            {
                return null;
            }
        }
        return CheckIfExpressionRefinesAnyParameter(fn, singleReturn!);
    }

    internal TypePredicate? CheckIfExpressionRefinesAnyParameter(Node fn, Node expr)
    {
        expr = SkipParentheses(expr)!;
        var returnType = CheckExpressionCached(expr);
        if ((returnType._flags & TypeFlags.Boolean) == 0)
        {
            return null;
        }
        var parameters = fn.Parameters()!;
        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var initType = GetTypeOfSymbolWorker(param.Symbol!);
            if (initType == null || (initType._flags & TypeFlags.Boolean) != 0 || !IsIdentifier(param.Name()!) || IsSymbolAssigned(param.Symbol!) || IsRestParameter(param))
            {
                // Refining "x: boolean" to "x is true" or "x is false" isn't useful.
                continue;
            }
            var trueType = CheckIfExpressionRefinesParameter(fn, expr, param, initType);
            if (trueType != null)
            {
                return NewTypePredicate(TypePredicateKind.Identifier, param.Name()!.Text(), (int)i, trueType);
            }
        }
        return null;
    }

    internal Type? CheckIfExpressionRefinesParameter(Node fn, Node expr, Node param, Type initType)
    {
        var antecedent = GetFlowNodeOfNode(expr);
        if (antecedent == null && IsReturnStatement(expr.Parent!))
        {
            antecedent = GetFlowNodeOfNode(expr.Parent!);
        }
        if (antecedent == null)
        {
            antecedent = new FlowNode { Flags = FlowFlags.Start };
        }
        var trueCondition = new FlowNode { Flags = FlowFlags.TrueCondition, Node = expr, Antecedent = antecedent };
        var trueType = GetFlowTypeOfReferenceEx(param.Name()!, initType, initType, fn, trueCondition);
        if (trueType == initType)
        {
            return null;
        }
        // "x is T" means that x is T if and only if it returns true. If it returns false then x is not T.
        // This means that if the function is called with an argument of type trueType, there can't be anything left in the `else` branch. It must reduce to `never`.
        var falseCondition = new FlowNode { Flags = FlowFlags.FalseCondition, Node = expr, Antecedent = antecedent };
        var falseSubtype = GetReducedTypeWorker(GetFlowTypeOfReferenceEx(param.Name()!, initType, trueType, fn, falseCondition));
        if ((falseSubtype._flags & TypeFlags.Never) != 0)
        {
            return trueType;
        }
        return null;
    }

    internal Type AddOptionalTypeMarker(Type t)
    {
        if (_strictNullChecks)
        {
            return GetUnionTypeWorker(new Type[] { t, _optionalType! });
        }
        return t;
    }
}
