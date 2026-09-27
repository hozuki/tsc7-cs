// ported from: internal/checker/checker.go:7575-8145（表达式检查入口与 super/private identifier 检查：
//   checkNonNullExpression、checkNonNullType、checkNonNullTypeWithReporter、checkNonNullNonVoidType、
//   reportObjectPossiblyNullOrUndefinedError、checkExpressionWithContextualType、getContextNode、
//   checkExpressionCached(+Ex)、getContextFreeTypeOfExpression、checkExpression(+Ex)、checkConstEnumAccess、
//   instantiateTypeWithSingleGenericCallSignature、getOuterInferenceTypeParameters、getUniqueTypeParameters、
//   包级 hasTypeParameterByName、getUniqueTypeParameterName、checkExpressionWorker、
//   checkPrivateIdentifierExpression、getSymbolForPrivateIdentifierExpression、checkSuperExpression、
//   isInConstructorArgumentInitializer）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `InferenceContext._inferences` 在 C# 侧是 `List<InferenceInfo>`（Checker.Keys.cs:800）。推导辅助函数的分工：
//    `InferTypes`/`HasOverlappingInferences` 收 `IReadOnlyList<InferenceInfo>`（数组/List 都直接传），
//    `MergeInferences` 收 `List<InferenceInfo>`（会执行 `target[i] = source[i]`，与 Go 的共享底层数组写回等价）；
//    Core 的 `Every/Map`（`T[]?`）→ 用 collection expression `[.. context._inferences]` 取快照数组
//    （元素是引用，读语义与 Go 的切片一致）。
// 2) Go 的 `reportError func(c *Checker, node *ast.Node, facts TypeFacts)`（无返回值）→ C# `Action<Checker, Node, TypeFacts>`；
//    `(*Checker).reportObjectPossiblyNullOrUndefinedError`（Go 的方法表达式）→ `(c, n, facts) => c.ReportObjectPossiblyNullOrUndefinedError(n, facts)`。
// 3) `defer tr.Push(phase, name, args, false)()`（Go 在函数返回时执行）→ C# `try/finally` + `Action?`：
//    进入时取 pop 闭包，`finally` 里调用。
// 4) Go 的 `if c.flowTypeCache == nil { c.flowTypeCache = make(...) }` / `c.flowLoopStack = nil` 落到 C# 的非空
//    容器字段：`_flowTypeCache` 用 `new()`、`_flowLoopStack` 用 `[]`（Go 的 nil 与空容器在本处语义相同）。
//    `inferenceContext.intraExpressionInferenceSites = nil` 同理 → `Clear()`（字段是 `List<...>`）。
// 5) `x.Symbol()`（Go 方法）→ C# **属性** `x.Symbol`（Ast/Node.cs:408，名字与类型名相同的访问器是属性）。
// 6) `panic(...)` → `Debug.Fail(...)`；Go 的 `switch { case ... }`（无 tag）→ C# `if/else if`；`goto case` 不出现。
//    包级函数 `hasTypeParameterByName`/`getUniqueTypeParameterName` → 同一个 partial class 的 `internal static` 成员。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;

public sealed partial class Checker
{
    internal Type CheckNonNullExpression(Node node)
    {
        return CheckNonNullType(CheckExpression(node), node);
    }

    internal Type CheckNonNullType(Type t, Node node)
    {
        // Go: `(*Checker).reportObjectPossiblyNullOrUndefinedError`（方法表达式）
        return CheckNonNullTypeWithReporter(t, node, (c, n, facts) => c.ReportObjectPossiblyNullOrUndefinedError(n, facts));
    }

    internal Type CheckNonNullTypeWithReporter(Type t, Node node, Action<Checker, Node, TypeFacts> reportError)
    {
        if (_strictNullChecks && (t._flags & TypeFlags.Unknown) != 0)
        {
            if (IsEntityNameExpression(node))
            {
                var nodeText = EntityNameToString(node);
                if (nodeText.Length < 100)
                {
                    Error(node, Diagnostics.X_0_is_of_type_unknown, nodeText);
                    return _errorType;
                }
            }

            Error(node, Diagnostics.Object_is_of_type_unknown);
            return _errorType;
        }

        var facts = GetTypeFacts(t, TypeFacts.IsUndefinedOrNull);
        if ((facts & TypeFacts.IsUndefinedOrNull) != 0)
        {
            reportError(this, node, facts);
            var nonNullable = GetNonNullableType(t);
            if ((nonNullable._flags & (TypeFlags.Nullable | TypeFlags.Never)) != 0)
            {
                return _errorType;
            }

            return nonNullable;
        }

        return t;
    }

    internal Type CheckNonNullNonVoidType(Type t, Node node)
    {
        var nonNullType = CheckNonNullType(t, node);
        if ((nonNullType._flags & TypeFlags.Void) != 0)
        {
            if (IsEntityNameExpression(node))
            {
                var nodeText = EntityNameToString(node);
                if (IsIdentifier(node) && nodeText == "undefined")
                {
                    Error(node, Diagnostics.The_value_0_cannot_be_used_here, nodeText);
                    return nonNullType;
                }

                if (nodeText.Length < 100)
                {
                    Error(node, Diagnostics.X_0_is_possibly_undefined, nodeText);
                    return nonNullType;
                }
            }

            Error(node, Diagnostics.Object_is_possibly_undefined);
        }

        return nonNullType;
    }

    internal void ReportObjectPossiblyNullOrUndefinedError(Node node, TypeFacts facts)
    {
        var nodeText = "";
        if (IsEntityNameExpression(node))
        {
            nodeText = EntityNameToString(node);
        }

        if (node.Kind == Kind.NullKeyword)
        {
            Error(node, Diagnostics.The_value_0_cannot_be_used_here, "null");
            return;
        }

        if (nodeText != "" && nodeText.Length < 100)
        {
            if (IsIdentifier(node) && nodeText == "undefined")
            {
                Error(node, Diagnostics.The_value_0_cannot_be_used_here, "undefined");
                return;
            }

            Error(node, IfElse(
                (facts & TypeFacts.IsUndefined) != 0,
                IfElse(
                    (facts & TypeFacts.IsNull) != 0,
                    Diagnostics.X_0_is_possibly_null_or_undefined,
                    Diagnostics.X_0_is_possibly_undefined),
                Diagnostics.X_0_is_possibly_null), nodeText);
        }
        else
        {
            Error(node, IfElse(
                (facts & TypeFacts.IsUndefined) != 0,
                IfElse(
                    (facts & TypeFacts.IsNull) != 0,
                    Diagnostics.Object_is_possibly_null_or_undefined,
                    Diagnostics.Object_is_possibly_undefined),
                Diagnostics.Object_is_possibly_null));
        }
    }

    internal Type CheckExpressionWithContextualType(Node node, Type contextualType, InferenceContext? inferenceContext, CheckMode checkMode)
    {
        var contextNode = GetContextNode(node);
        PushContextualType(contextNode, contextualType, false /*isCache*/);
        PushInferenceContext(contextNode, inferenceContext!);
        var t = CheckExpressionEx(node, checkMode | CheckMode.Contextual | IfElse(inferenceContext != null, CheckMode.Inferential, CheckMode.Normal));
        // In CheckMode.Inferential we collect intra-expression inference sites to process before fixing any type
        // parameters. This information is no longer needed after the call to checkExpression.
        // Go: `inferenceContext.intraExpressionInferenceSites = nil`（C# 字段是 `List<...>` → `Clear()`）。
        if (inferenceContext != null && inferenceContext._intraExpressionInferenceSites.Count != 0)
        {
            inferenceContext._intraExpressionInferenceSites.Clear();
        }

        // We strip literal freshness when an appropriate contextual type is present such that contextually typed
        // literals always preserve their literal types (otherwise they might widen during type inference). An alternative
        // here would be to not mark contextually typed literals as fresh in the first place.
        if (MaybeTypeOfKind(t, TypeFlags.Literal) && IsLiteralOfContextualType(t, InstantiateContextualType(contextualType, node, ContextFlags.None)))
        {
            t = GetRegularTypeOfLiteralType(t);
        }

        PopInferenceContext();
        PopContextualType();
        return t;
    }

    internal Node GetContextNode(Node node)
    {
        if (IsJsxAttributes(node) && !IsJsxSelfClosingElement(node.Parent!))
        {
            // Needs to be the root JsxElement, so it encompasses the attributes _and_ the children (which are essentially part of the attributes)
            return node.Parent!.Parent!;
        }

        return node;
    }

    internal Type CheckExpressionCached(Node node)
    {
        return CheckExpressionCachedEx(node, CheckMode.Normal);
    }

    internal Type CheckExpressionCachedEx(Node node, CheckMode checkMode)
    {
        if (checkMode != CheckMode.Normal)
        {
            return CheckExpressionEx(node, checkMode);
        }

        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            // When computing a type that we're going to cache, we need to ignore any ongoing control flow
            // analysis because variables may have transient types in indeterminable states. Moving flowLoopStart
            // to the top of the stack ensures all transient types are computed from a known point.
            var saveFlowLoopStack = _flowLoopStack;
            var saveFlowTypeCache = _flowTypeCache;
            _flowLoopStack = [];
            _flowTypeCache = new();
            links._resolvedType = CheckExpressionEx(node, checkMode);
            _flowTypeCache = saveFlowTypeCache;
            _flowLoopStack = saveFlowLoopStack;
        }

        return links._resolvedType;
    }

    // Returns the type of an expression. Unlike checkExpression, this function is simply concerned
    // with computing the type and may not fully check all contained sub-expressions for errors.
    // It is intended for uses where you know there is no contextual type,
    // and requesting the contextual type might cause a circularity or other bad behaviour.
    // It sets the contextual type of the node to any before calling getTypeOfExpression.
    internal Type GetContextFreeTypeOfExpression(Node node)
    {
        if (_contextFreeTypes.TryGetValue(node, out var cached))
        {
            return cached;
        }

        PushContextualType(node, _anyType, false /*isCache*/);
        var t = CheckExpressionEx(node, CheckMode.SkipContextSensitive);
        _contextFreeTypes[node] = t;
        PopContextualType();
        return t;
    }

    internal Type CheckExpression(Node node)
    {
        return CheckExpressionEx(node, CheckMode.Normal);
    }

    internal Type CheckExpressionEx(Node node, CheckMode checkMode)
    {
        // Go: `if tr := c.tracer; tr != nil { defer tr.Push(...)() }` → try/finally
        Action? traceEnd = null;
        if (_tracer != null)
        {
            traceEnd = _tracer.Push(Phase.Check, "checkExpression", new Dictionary<string, object?>
            {
                ["kind"] = node.Kind,
                ["pos"] = node.Pos(),
                ["end"] = node.End(),
                ["path"] = GetSourceFileOfNode(node)!.FileName(),
            }, false);
        }

        try
        {
            var saveCurrentNode = _currentNode;
            _currentNode = node;
            _instantiationCount = 0;
            var uninstantiatedType = CheckExpressionWorker(node, checkMode);
            var t = InstantiateTypeWithSingleGenericCallSignature(node, uninstantiatedType, checkMode);
            if (IsConstEnumObjectType(t))
            {
                CheckConstEnumAccess(node, t);
            }

            _currentNode = saveCurrentNode;
            return t;
        }
        finally
        {
            traceEnd?.Invoke();
        }
    }

    internal void CheckConstEnumAccess(Node node, Type t)
    {
        // enum object type for const enums are only permitted in:
        // - 'left' in property access
        // - 'object' in indexed access
        // - target in rhs of import statement
        var ok = (IsPropertyAccessExpression(node.Parent!) && node.Parent!.Expression() == node) ||
            (IsElementAccessExpression(node.Parent!) && node.Parent!.Expression() == node) ||
            (((IsIdentifier(node) || IsQualifiedName(node)) && IsInRightSideOfImportOrExportAssignment(node)) ||
                (IsTypeQueryNode(node.Parent!) && node.Parent!.AsTypeQueryNode()!.ExprName == node)) ||
            IsExportSpecifier(node.Parent!); // We allow reexporting const enums
        if (!ok)
        {
            Error(node, Diagnostics.X_const_enums_can_only_be_used_in_property_or_index_access_expressions_or_the_right_hand_side_of_an_import_declaration_or_export_assignment_or_type_query);
        }

        // --verbatimModuleSyntax only gets checked here when the enum usage does not
        // resolve to an import, because imports of ambient const enums get checked
        // separately in `checkAliasSymbol`.
        if (_compilerOptions.IsolatedModules.IsTrue() ||
            (_compilerOptions.VerbatimModuleSyntax.IsTrue() && ok && _resolveName(node, GetFirstIdentifier(node)!.Text(), SymbolFlags.Alias, null, false, true) == null))
        {
            Debug.Assert((t._symbol!.Flags & SymbolFlags.ConstEnum) != 0);
            var constEnumDeclaration = t._symbol.ValueDeclaration!;
            var redirect = _program.GetProjectReferenceFromOutputDts(GetSourceFileOfNode(constEnumDeclaration)!.Path());
            if ((constEnumDeclaration.Flags & NodeFlags.Ambient) != 0 && !IsValidTypeOnlyAliasUseSite(node) && (redirect == null || !redirect.Resolved!.CompilerOptions()!.ShouldPreserveConstEnums()))
            {
                Error(node, Diagnostics.Cannot_access_ambient_const_enums_when_0_is_enabled, GetIsolatedModulesLikeFlagName());
            }
        }
    }

    internal Type InstantiateTypeWithSingleGenericCallSignature(Node node, Type t, CheckMode checkMode)
    {
        if ((checkMode & (CheckMode.Inferential | CheckMode.SkipGenericFunctions)) == 0)
        {
            return t;
        }

        var callSignature = GetSingleSignature(t, SignatureKind.Call, true /*allowMembers*/);
        var constructSignature = GetSingleSignature(t, SignatureKind.Construct, true /*allowMembers*/);
        var signature = OrElse(callSignature, constructSignature);
        if (signature == null || signature.TypeParameters().Length == 0)
        {
            return t;
        }

        var contextualType = GetApparentTypeOfContextualType(node, ContextFlags.NoConstraints);
        if (contextualType == null)
        {
            return t;
        }

        var contextualSignature = GetSingleSignature(GetNonNullableType(contextualType), IfElse(callSignature != null, SignatureKind.Call, SignatureKind.Construct), false /*allowMembers*/);
        if (contextualSignature == null || contextualSignature.TypeParameters().Length != 0)
        {
            return t;
        }

        if ((checkMode & CheckMode.SkipGenericFunctions) != 0)
        {
            SkippedGenericFunction(node, checkMode);
            return _anyFunctionType;
        }

        var context = GetInferenceContext(node)!;
        // We have an expression that is an argument of a generic function for which we are performing
        // type argument inference. The expression is of a function type with a single generic call
        // signature and a contextual function type with a single non-generic call signature. Now check
        // if the outer function returns a function type with a single non-generic call signature and
        // if some of the outer function type parameters have no inferences so far. If so, we can
        // potentially add inferred type parameters to the outer function return type.
        Signature? returnSignature = null;
        if (context._signature != null)
        {
            var returnType = GetReturnTypeOfSignatureWorker(context._signature);
            if (returnType != null)
            {
                returnSignature = GetSingleCallOrConstructSignature(returnType);
            }
        }

        if (returnSignature != null && returnSignature.TypeParameters().Length == 0 && !Every<InferenceInfo>([.. context._inferences], HasInferenceCandidates))
        {
            // Instantiate the signature with its own type parameters as type arguments, possibly
            // renaming the type parameters to ensure they have unique names.
            var uniqueTypeParameters = GetUniqueTypeParameters(context, signature.TypeParameters());
            var instantiatedSignature = GetSignatureInstantiationWithoutFillingInTypeArguments(signature, uniqueTypeParameters);
            // Infer from the parameters of the instantiated signature to the parameters of the
            // contextual signature starting with an empty set of inference candidates.
            var inferences = Map<InferenceInfo, InferenceInfo>([.. context._inferences], info => NewInferenceInfo(info._typeParameter!));
            ApplyToParameterTypes(instantiatedSignature, contextualSignature, (source, target) =>
            {
                InferTypes(inferences, source, target, InferencePriority.None, true /*contravariant*/);
            });
            if (Some(inferences, HasInferenceCandidates))
            {
                // We have inference candidates, indicating that one or more type parameters are referenced
                // in the parameter types of the contextual signature. Now also infer from the return type.
                ApplyToReturnTypes(instantiatedSignature, contextualSignature, (source, target) =>
                {
                    InferTypes(inferences, source, target, InferencePriority.None, false);
                });
                // If the type parameters for which we produced candidates do not have any inferences yet,
                // we adopt the new inference candidates and add the type parameters of the expression type
                // to the set of inferred type parameters for the outer function return type.
                if (!HasOverlappingInferences(context._inferences, inferences))
                {
                    // Go: `c.mergeInferences(context.inferences, inferences)`——C# 的 `MergeInferences` 收
                    // `List<InferenceInfo>` 且执行 `target[i] = source[i]`，与 Go 写回同一底层数组等价。
                    MergeInferences(context._inferences, inferences);
                    context._inferredTypeParameters = [.. context._inferredTypeParameters, .. uniqueTypeParameters];
                    return GetOrCreateTypeFromSignature(instantiatedSignature);
                }
            }
        }

        // TODO: The signature may reference any outer inference contexts, but we map pop off and then apply new inference contexts,
        // and thus get different inferred types. That this is cached on the *first* such attempt is not currently an issue, since expression
        // types *also* get cached on the first pass. If we ever properly speculate, though, the cached "isolatedSignatureType" signature
        // field absolutely needs to be included in the list of speculative caches.
        return GetOrCreateTypeFromSignature(InstantiateSignatureInContextOf(signature, contextualSignature, context, null));
    }

    internal Type[] GetOuterInferenceTypeParameters()
    {
        var result = new List<Type>();
        foreach (var info in _inferenceContextInfos)
        {
            var context = info._context;
            if (context != null)
            {
                foreach (var inference in context._inferences)
                {
                    result.Add(inference._typeParameter!);
                }
            }
        }

        return result.ToArray();
    }

    internal Type[] GetUniqueTypeParameters(InferenceContext context, Type[] typeParameters)
    {
        List<Type>? oldTypeParameters = null;
        List<Type>? newTypeParameters = null;
        var result = new List<Type>(typeParameters.Length);
        foreach (var tp in typeParameters)
        {
            var name = tp._symbol!.Name;
            if (HasTypeParameterByName([.. context._inferredTypeParameters], name) || HasTypeParameterByName(result.ToArray(), name))
            {
                var newName = GetUniqueTypeParameterName([.. context._inferredTypeParameters, .. result], name);
                var symbol = NewSymbol(SymbolFlags.TypeParameter, newName);
                var newTypeParameter = NewTypeParameter(symbol);
                newTypeParameter.AsTypeParameter()!._target = tp;
                oldTypeParameters ??= [];
                newTypeParameters ??= [];
                oldTypeParameters.Add(tp);
                newTypeParameters.Add(newTypeParameter);
                result.Add(newTypeParameter);
            }
            else
            {
                result.Add(tp);
            }
        }

        if (newTypeParameters != null && newTypeParameters.Count != 0)
        {
            var mapper = NewTypeMapper(oldTypeParameters!.ToArray(), newTypeParameters.ToArray());
            foreach (var tp in newTypeParameters)
            {
                tp.AsTypeParameter()!._mapper = mapper;
            }
        }

        return result.ToArray();
    }

    // Go: 包级函数（无接收者）
    internal static bool HasTypeParameterByName(Type[] typeParameters, string name)
    {
        return Some(typeParameters, tp => tp._symbol!.Name == name);
    }

    // Go: 包级函数（无接收者）
    internal static string GetUniqueTypeParameterName(Type[] typeParameters, string baseName)
    {
        while (baseName.Length > 1 && baseName[^1] >= '0' && baseName[^1] <= '9')
        {
            baseName = baseName[..^1];
        }

        var index = 1;
        while (true)
        {
            var augmentedName = baseName + index.ToString();
            if (!HasTypeParameterByName(typeParameters, augmentedName))
            {
                return augmentedName;
            }

            index++;
        }
    }

    internal Type CheckExpressionWorker(Node node, CheckMode checkMode)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                return CheckIdentifier(node, checkMode);
            case Kind.PrivateIdentifier:
                return CheckPrivateIdentifierExpression(node);
            case Kind.ThisKeyword:
                return CheckThisExpression(node);
            case Kind.SuperKeyword:
                return CheckSuperExpression(node);
            case Kind.NullKeyword:
                return _nullWideningType;
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
                if (IsSkipDirectInferenceNode(node))
                {
                    return _blockedStringType;
                }

                return GetFreshTypeOfLiteralType(GetStringLiteralType(node.Text()));
            case Kind.NumericLiteral:
                CheckGrammarNumericLiteral(node.AsNumericLiteral()!);
                return GetFreshTypeOfLiteralType(GetNumberLiteralType(FromString(node.Text())));
            case Kind.BigIntLiteral:
                CheckGrammarBigIntLiteral(node.AsBigIntLiteral()!);
                return GetFreshTypeOfLiteralType(GetBigIntLiteralType(NewPseudoBigInt(ParsePseudoBigInt(node.Text()), false /*negative*/)));
            case Kind.TrueKeyword:
                return _trueType;
            case Kind.FalseKeyword:
                return _falseType;
            case Kind.TemplateExpression:
                return CheckTemplateExpression(node);
            case Kind.RegularExpressionLiteral:
                return CheckRegularExpressionLiteral(node);
            case Kind.ArrayLiteralExpression:
                return CheckArrayLiteral(node, checkMode);
            case Kind.ObjectLiteralExpression:
                return CheckObjectLiteral(node, checkMode);
            case Kind.PropertyAccessExpression:
                return CheckPropertyAccessExpression(node, checkMode, false /*writeOnly*/);
            case Kind.QualifiedName:
                return CheckQualifiedName(node, checkMode);
            case Kind.ElementAccessExpression:
                return CheckIndexedAccess(node, checkMode);
            case Kind.CallExpression:
                if (IsImportCall(node))
                {
                    return CheckImportCallExpression(node);
                }

                return CheckCallExpression(node, checkMode);
            case Kind.NewExpression:
                return CheckCallExpression(node, checkMode);
            case Kind.TaggedTemplateExpression:
                return CheckTaggedTemplateExpression(node);
            case Kind.ParenthesizedExpression:
                return CheckParenthesizedExpression(node, checkMode);
            case Kind.ClassExpression:
                return CheckClassExpression(node);
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                return CheckFunctionExpressionOrObjectLiteralMethod(node, checkMode);
            case Kind.TypeAssertionExpression:
            case Kind.AsExpression:
                return CheckAssertion(node, checkMode);
            case Kind.TypeOfExpression:
                return CheckTypeOfExpression(node);
            case Kind.NonNullExpression:
                return CheckNonNullAssertion(node);
            case Kind.ExpressionWithTypeArguments:
                return CheckExpressionWithTypeArguments(node);
            case Kind.SatisfiesExpression:
                return CheckSatisfiesExpression(node);
            case Kind.MetaProperty:
                return CheckMetaProperty(node);
            case Kind.DeleteExpression:
                return CheckDeleteExpression(node);
            case Kind.VoidExpression:
                return CheckVoidExpression(node);
            case Kind.AwaitExpression:
                return CheckAwaitExpression(node);
            case Kind.PrefixUnaryExpression:
                return CheckPrefixUnaryExpression(node);
            case Kind.PostfixUnaryExpression:
                return CheckPostfixUnaryExpression(node);
            case Kind.BinaryExpression:
                return CheckBinaryExpression(node, checkMode);
            case Kind.ConditionalExpression:
                return CheckConditionalExpression(node, checkMode);
            case Kind.SpreadElement:
                return CheckSpreadExpression(node, checkMode);
            case Kind.OmittedExpression:
                return _undefinedWideningType;
            case Kind.YieldExpression:
                return CheckYieldExpression(node);
            case Kind.SyntheticExpression:
                return CheckSyntheticExpression(node);
            case Kind.JsxExpression:
                return CheckJsxExpression(node, checkMode);
            case Kind.JsxElement:
                return CheckJsxElement(node, checkMode);
            case Kind.JsxSelfClosingElement:
                return CheckJsxSelfClosingElement(node, checkMode);
            case Kind.JsxFragment:
                return CheckJsxFragment(node);
            case Kind.JsxAttributes:
                return CheckJsxAttributes(node, checkMode);
            case Kind.JsxOpeningElement:
                // Go: panic("Should never directly check a JsxOpeningElement")
                Debug.Fail("Should never directly check a JsxOpeningElement");
                break;
        }

        return _errorType;
    }

    internal Type CheckPrivateIdentifierExpression(Node node)
    {
        CheckGrammarPrivateIdentifierExpression(node.AsPrivateIdentifier()!);
        var symbol = GetSymbolForPrivateIdentifierExpression(node);
        if (symbol != null)
        {
            MarkPropertyAsReferenced(symbol, null /*nodeForCheckWriteOnly*/, false /*isSelfTypeAccess*/);
        }

        return _anyType;
    }

    internal Symbol? GetSymbolForPrivateIdentifierExpression(Node node)
    {
        var links = _symbolNodeLinks.Get(node);
        if (links._resolvedSymbol == null)
        {
            links._resolvedSymbol = LookupSymbolForPrivateIdentifierDeclaration(node.Text(), node);
        }

        return links._resolvedSymbol;
    }

    internal Type CheckSuperExpression(Node node)
    {
        var isCallExpression = IsCallExpression(node.Parent!) && node.Parent!.Expression() == node;
        var immediateContainer = GetSuperContainer(node, true /*stopOnFunctions*/);
        var container = immediateContainer;

        // adjust the container reference in case if super is used inside arrow functions with arbitrarily deep nesting
        if (!isCallExpression)
        {
            while (container != null && IsArrowFunction(container))
            {
                container = GetSuperContainer(container, true /*stopOnFunctions*/);
            }
        }

        bool IsLegalUsageOfSuperExpression()
        {
            if (isCallExpression)
            {
                // TS 1.0 SPEC (April 2014): 4.8.1
                // Super calls are only permitted in constructors of derived classes
                return IsConstructorDeclaration(container!);
            }

            // TS 1.0 SPEC (April 2014)
            // 'super' property access is allowed
            // - In a constructor, instance member function, instance member accessor, or instance member variable initializer where this references a derived class instance
            // - In a static member function or static member accessor

            // topmost container must be something that is directly nested in the class declaration\object literal expression
            if (IsClassLike(container!.Parent!) || IsObjectLiteralExpression(container.Parent!))
            {
                if (IsStatic(container))
                {
                    return NodeKindIs(container, Kind.MethodDeclaration, Kind.MethodSignature, Kind.GetAccessor, Kind.SetAccessor, Kind.PropertyDeclaration, Kind.ClassStaticBlockDeclaration);
                }

                return NodeKindIs(container, Kind.MethodDeclaration, Kind.MethodSignature, Kind.GetAccessor, Kind.SetAccessor, Kind.PropertyDeclaration, Kind.PropertySignature, Kind.Constructor);
            }

            return false;
        }

        if (container == null || !IsLegalUsageOfSuperExpression())
        {
            // issue more specific error if super is used in computed property name
            // class A { foo() { return "1" }}
            // class B {
            //     [super.foo()]() {}
            // }
            var current = FindAncestorOrQuit(node, n =>
            {
                if (n == container)
                {
                    return FindAncestorResult.Quit;
                }

                if (IsComputedPropertyName(n))
                {
                    return FindAncestorResult.True;
                }

                return FindAncestorResult.False;
            });
            if (current != null && IsComputedPropertyName(current))
            {
                Error(node, Diagnostics.X_super_cannot_be_referenced_in_a_computed_property_name);
            }
            else if (isCallExpression)
            {
                Error(node, Diagnostics.Super_calls_are_not_permitted_outside_constructors_or_in_nested_functions_inside_constructors);
            }
            else if (container == null || container.Parent == null || !(IsClassLike(container.Parent) || IsObjectLiteralExpression(container.Parent)))
            {
                Error(node, Diagnostics.X_super_can_only_be_referenced_in_members_of_derived_classes_or_object_literal_expressions);
            }
            else
            {
                Error(node, Diagnostics.X_super_property_access_is_permitted_only_in_a_constructor_member_function_or_member_accessor_of_a_derived_class);
            }

            return _errorType;
        }

        if (!isCallExpression && IsConstructorDeclaration(immediateContainer!))
        {
            CheckThisBeforeSuper(node, container, Diagnostics.X_super_must_be_called_before_accessing_a_property_of_super_in_the_constructor_of_a_derived_class);
        }

        if (container.Parent!.Kind == Kind.ObjectLiteralExpression)
        {
            // for object literal assume that type of 'super' is 'any'
            return _anyType;
        }

        // at this point the only legal case for parent is ClassLikeDeclaration
        var classLikeDeclaration = container.Parent!;
        if (GetClassExtendsHeritageElement(classLikeDeclaration) == null)
        {
            Error(node, Diagnostics.X_super_can_only_be_referenced_in_a_derived_class);
            return _errorType;
        }

        if (ClassDeclarationExtendsNull(classLikeDeclaration))
        {
            if (isCallExpression)
            {
                return _errorType;
            }

            return _nullWideningType;
        }

        var classType = GetDeclaredTypeOfSymbolWorker(GetSymbolOfDeclaration(classLikeDeclaration)!);
        Type? baseClassType = null;
        if (classType != null)
        {
            baseClassType = FirstOrNil(GetBaseTypesWorker(classType));
        }

        if (baseClassType == null)
        {
            return _errorType;
        }

        if (IsConstructorDeclaration(container) && IsInConstructorArgumentInitializer(node, container))
        {
            // issue custom error message for super property access in constructor arguments (to be aligned with old compiler)
            Error(node, Diagnostics.X_super_cannot_be_referenced_in_constructor_arguments);
            return _errorType;
        }

        if (IsStatic(container) || isCallExpression)
        {
            if (!isCallExpression &&
                _languageVersion <= ScriptTarget.ES2021 &&
                (IsPropertyDeclaration(container) || IsClassStaticBlockDeclaration(container)))
            {
                // for `super.x` or `super[x]` in a static initializer, mark all enclosing
                // block scope containers so that we can report potential collisions with
                // `Reflect`.
                for (var current = GetEnclosingBlockScopeContainer(node.Parent!); current != null; current = GetEnclosingBlockScopeContainer(current))
                {
                    if (!IsSourceFile(current) || IsExternalOrCommonJSModule(current.AsSourceFile()!))
                    {
                        _nodeLinks.Get(current!)._flags |= NodeCheckFlags.ContainsSuperPropertyInStaticInitializer;
                    }
                }
            }

            return GetBaseConstructorTypeOfClassWorker(classType!);
        }

        return GetTypeWithThisArgument(baseClassType, classType!.AsInterfaceType()!._thisType, false);
    }

    internal bool IsInConstructorArgumentInitializer(Node node, Node constructorDecl)
    {
        return FindAncestorOrQuit(node, n =>
        {
            if (IsFunctionLikeDeclaration(n))
            {
                return FindAncestorResult.Quit;
            }

            if (IsParameterDeclaration(n) && n.Parent == constructorDecl)
            {
                return FindAncestorResult.True;
            }

            return FindAncestorResult.False;
        }) != null;
    }
}
