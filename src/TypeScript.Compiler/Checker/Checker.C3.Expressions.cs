// ported from: internal/checker/checker.go:8146-8493（表达式检查：模板表达式/正则字面量/数组字面量/限定名/
//   元素访问/for-in 数值属性名/索引访问索引类型/动态 import 调用 checkImportCallExpression；
//   createArrayLiteralType；包级函数 isSpreadIntoCallOrNew）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 包级函数 `isSpreadIntoCallOrNew` → 同一个 `public sealed partial class Checker` 里的 `internal static`
//    （docs/checker-plan.md §5.5；C# 没有自由函数）。
// 2) Go 未导出字段 → `_camelCase`（§5.5）：`c.nodeLinks`→`_nodeLinks`、`c.cachedTypes`→`_cachedTypes`、
//    `c.errorType`→`_errorType`、`t.flags`→`t._flags`、`t.objectFlags`→`t._objectFlags`、`t.id`→`t._id`。
// 3) 同名方法冲突（artifacts/checker-method-collisions.md）：`c.getPropertiesOfType`→`GetPropertiesOfTypeWorker`、
//    `c.getIndexInfoOfType`→`GetIndexInfoOfTypeWorker`、`c.getUnionTypeEx`→`GetUnionTypeExWorker`、
//    `c.isTypeAssignableTo`→`IsTypeAssignableToWorker`、`c.isArrayLikeType`→`IsArrayLikeTypeWorker`、
//    `c.getIndexTypeOfType`→`GetIndexTypeOfTypeWorker`、`c.getPropertyOfType`→`GetPropertyOfTypeWorker`、
//    `c.getTypeOfSymbol`→`GetTypeOfSymbolWorker`、`c.getPropertiesOfType`→`GetPropertiesOfTypeWorker`、
//    `c.resolveExternalModuleName`→`ResolveExternalModuleNameWorker`、`c.resolveExternalModuleSymbol`→
//    `ResolveExternalModuleSymbolWorker`、`c.getTypeOfPropertyOfType`→`GetTypeOfPropertyOfTypeWorker`、
//    `c.TypeToString`（Go 导出名，保留原名；未导出的 `typeToString(t, enclosingDeclaration)` 才是 `TypeToStringWorker`）。
// 4) `node.AsX()` 在 Go 里断言失败即 panic，C# 返回 null → 按 AST 不变量加 `!`（docs/ast.md §2）。
// 5) Go 的 nil 切片（`node.Elements()`/`node.Arguments()`）长度视为 0 → `?? []`。
// 6) Go 的 `for i, e := range elements` 用索引循环（要与 `elementTypes[i]`/`elementInfos[i]` 同步写）。
// 7) `core.IfElse`→`IfElse`、`core.OrElse`→`OrElse`、`core.Some`→`Some`、`core.Map`→`Map`；
//    `min`/`max` 内建 → `Math.Min`/`Math.Max`；`strconv.Itoa`→`.ToString()`（后续文件）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type CheckTemplateExpression(Node node)
    {
        var expr = node.AsTemplateExpression()!;
        var spans = expr.TemplateSpans?.Nodes ?? [];
        var length = spans.Length;
        var texts = new string[length + 1];
        var types = new Type?[length];
        texts[0] = expr.Head!.Text();
        for (var i = 0; i < length; i++)
        {
            var span = spans[i];
            var t = CheckExpression(span.Expression()!);
            if (MaybeTypeOfKindConsideringBaseConstraint(t, TypeFlags.ESSymbolLike))
            {
                Error(span.Expression(), Diagnostics.Implicit_conversion_of_a_symbol_to_a_string_will_fail_at_runtime_Consider_wrapping_this_expression_in_String);
            }
            texts[i + 1] = span.AsTemplateSpan()!.Literal!.Text();
            types[i] = IfElse(IsTypeAssignableToWorker(t, _templateConstraintType!), t, _stringType);
        }
        object? evaluated = null;
        if (!IsTaggedTemplateExpression(node.Parent!))
        {
            evaluated = _evaluate(node, node).Value;
        }
        if (evaluated != null)
        {
            return GetFreshTypeOfLiteralType(GetStringLiteralType((string)evaluated));
        }
        if (IsConstContext(node) || IsTemplateLiteralContext(node) || SomeType(OrElse(GetContextualTypeWorker(node, ContextFlags.None), _unknownType!)!, t => IsTemplateLiteralContextualType(t)))
        {
            return GetTemplateLiteralType(texts, types!);
        }
        return _stringType!;
    }

    internal bool IsTemplateLiteralContext(Node node)
    {
        var parent = node.Parent;
        return (IsParenthesizedExpression(parent!) && IsTemplateLiteralContext(parent!)) ||
            (IsElementAccessExpression(parent!) && parent!.AsElementAccessExpression()!.ArgumentExpression == node);
    }

    internal bool IsTemplateLiteralContextualType(Type t)
    {
        return (t._flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral)) != 0 ||
            (t._flags & TypeFlags.InstantiableNonPrimitive) != 0 && MaybeTypeOfKind(OrElse(GetBaseConstraintOfTypeWorker(t), _unknownType!)!, TypeFlags.StringLike);
    }

    internal Type CheckRegularExpressionLiteral(Node node)
    {
        var nodeLinks = _nodeLinks.Get(node);
        if ((nodeLinks._flags & NodeCheckFlags.TypeChecked) == 0)
        {
            nodeLinks._flags |= NodeCheckFlags.TypeChecked;
            CheckGrammarRegularExpressionLiteral(node.AsRegularExpressionLiteral()!);
        }
        return _globalRegExpType!;
    }

    internal Type CheckArrayLiteral(Node node, CheckMode checkMode)
    {
        var elements = node.Elements() ?? [];
        var elementTypes = new Type?[elements.Length];
        var elementInfos = new TupleElementInfo[elements.Length];
        PushCachedContextualType(node);
        var inDestructuringPattern = IsAssignmentTarget(node);
        var inConstContext = IsConstContext(node);
        var contextualType = GetApparentTypeOfContextualType(node, ContextFlags.None);
        var inTupleContext = IsSpreadIntoCallOrNew(node) || contextualType != null && SomeType(contextualType, t =>
            IsTupleLikeType(t) ||
            IsGenericMappedType(t) && t.AsMappedType()!._nameType == null && GetHomomorphicTypeVariable(OrElse(t.AsMappedType()!._target, t!)!) != null);
        var hasOmittedExpression = false;
        for (var i = 0; i < elements.Length; i++)
        {
            var e = elements[i];
            if (IsSpreadElement(e))
            {
                var spreadType = CheckExpressionEx(e.Expression()!, checkMode);
                if (IsArrayLikeTypeWorker(spreadType))
                {
                    elementTypes[i] = spreadType;
                    elementInfos[i] = new TupleElementInfo { _flags = ElementFlags.Variadic };
                }
                else if (inDestructuringPattern)
                {
                    // Given the following situation:
                    //    var c: {};
                    //    [...c] = ["", 0];
                    //
                    // c is represented in the tree as a spread element in an array literal.
                    // But c really functions as a rest element, and its purpose is to provide
                    // a contextual type for the right hand side of the assignment. Therefore,
                    // instead of calling checkExpression on "...c", which will give an error
                    // if c is not iterable/array-like, we need to act as if we are trying to
                    // get the contextual element type from it. So we do something similar to
                    // getContextualTypeForElementExpression, which will crucially not error
                    // if there is no index type / iterated type.
                    var restElementType = GetIndexTypeOfTypeWorker(spreadType, _numberType!);
                    if (restElementType == null)
                    {
                        restElementType = GetIteratedTypeOrElementType(IterationUse.Destructuring, spreadType, _undefinedType, null /*errorNode*/, false /*checkAssignability*/);
                        if (restElementType == null)
                        {
                            restElementType = _unknownType;
                        }
                    }
                    elementTypes[i] = restElementType;
                    elementInfos[i] = new TupleElementInfo { _flags = ElementFlags.Rest };
                }
                else
                {
                    elementTypes[i] = CheckIteratedTypeOrElementType(IterationUse.Spread, spreadType, _undefinedType!, e.Expression());
                    elementInfos[i] = new TupleElementInfo { _flags = ElementFlags.Rest };
                }
            }
            else if (_exactOptionalPropertyTypes && IsOmittedExpression(e))
            {
                hasOmittedExpression = true;
                elementTypes[i] = _undefinedOrMissingType;
                elementInfos[i] = new TupleElementInfo { _flags = ElementFlags.Optional };
            }
            else
            {
                var t = CheckExpressionForMutableLocation(e, checkMode);
                elementTypes[i] = AddOptionalityEx(t, true /*isProperty*/, hasOmittedExpression);
                elementInfos[i] = new TupleElementInfo { _flags = IfElse(hasOmittedExpression, ElementFlags.Optional, ElementFlags.Required) };
                if (inTupleContext && (checkMode & CheckMode.Inferential) != 0 && (checkMode & CheckMode.SkipContextSensitive) == 0 && IsContextSensitiveWorker(e))
                {
                    var inferenceContext = GetInferenceContext(node)!;
                    // In CheckMode.Inferential we should always have an inference context
                    AddIntraExpressionInferenceSite(inferenceContext, e, t);
                }
            }
        }
        PopContextualType();
        if (inDestructuringPattern)
        {
            return CreateTupleTypeEx(elementTypes!, elementInfos!, false);
        }
        if ((checkMode & CheckMode.ForceTuple) != 0 || inConstContext || inTupleContext)
        {
            return CreateArrayLiteralType(CreateTupleTypeEx(elementTypes!, elementInfos!, inConstContext && !(contextualType != null && SomeType(contextualType, t => IsMutableArrayLikeType(t))) /*readonly*/));
        }
        Type? elementType;
        if (elementTypes.Length != 0)
        {
            for (var i = 0; i < elementTypes.Length; i++)
            {
                if ((elementInfos[i]._flags & ElementFlags.Variadic) != 0)
                {
                    elementTypes[i] = OrElse(GetIndexedAccessTypeOrUndefined(elementTypes[i]!, _numberType!, AccessFlags.None, null, null), _anyType);
                }
            }
            elementType = GetUnionTypeExWorker(elementTypes!, UnionReduction.Subtype, null, null);
        }
        else
        {
            elementType = IfElse(_strictNullChecks, _implicitNeverType!, _undefinedWideningType!);
        }
        return CreateArrayLiteralType(CreateArrayTypeEx(elementType, inConstContext));
    }

    internal Type CreateArrayLiteralType(Type t)
    {
        if ((t._objectFlags & ObjectFlags.Reference) == 0)
        {
            return t;
        }
        var key = new CachedTypeKey { _kind = CachedTypeKind.ArrayLiteralType, _typeId = t._id };
        if (_cachedTypes.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var literalType = CloneTypeReference(t);
        literalType._objectFlags |= ObjectFlags.ArrayLiteral | ObjectFlags.ContainsObjectOrArrayLiteral;
        _cachedTypes[key] = literalType;
        return literalType;
    }

    internal static bool IsSpreadIntoCallOrNew(Node node)
    {
        var parent = WalkUpParenthesizedExpressions(node.Parent);
        return IsSpreadElement(parent!) && IsCallOrNewExpression(parent!.Parent!);
    }

    internal Type CheckQualifiedName(Node node, CheckMode checkMode)
    {
        var left = node.AsQualifiedName()!.Left;
        Type? leftType;
        if (IsPartOfTypeQuery(node) && IsThisIdentifier(left!))
        {
            leftType = CheckNonNullType(CheckThisExpression(left!), left!);
        }
        else
        {
            leftType = CheckNonNullExpression(left!);
        }
        return CheckPropertyAccessExpressionOrQualifiedName(node, left!, leftType, node.AsQualifiedName()!.Right!, checkMode, false);
    }

    internal Type CheckIndexedAccess(Node node, CheckMode checkMode)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            return CheckElementAccessChain(node, checkMode);
        }
        return CheckElementAccessExpression(node, CheckNonNullExpression(node.Expression()!), checkMode);
    }

    internal Type CheckElementAccessChain(Node node, CheckMode checkMode)
    {
        var exprType = CheckExpression(node.Expression()!);
        var nonOptionalType = GetOptionalExpressionType(exprType, node.Expression()!);
        return PropagateOptionalTypeMarker(CheckElementAccessExpression(node, CheckNonNullType(nonOptionalType, node.Expression()!), checkMode), node, nonOptionalType != exprType);
    }

    internal Type CheckElementAccessExpression(Node node, Type exprType, CheckMode checkMode)
    {
        var objectType = exprType;
        if (GetAssignmentTargetKind(node) != AssignmentKind.None || IsMethodAccessForCall(node))
        {
            objectType = GetWidenedTypeWorker(objectType);
        }
        var indexExpression = node.AsElementAccessExpression()!.ArgumentExpression!;
        var indexType = CheckExpression(indexExpression);
        if (IsErrorType(objectType) || objectType == _silentNeverType)
        {
            return objectType;
        }
        if (IsConstEnumObjectType(objectType) && !IsStringLiteralLike(indexExpression))
        {
            Error(indexExpression, Diagnostics.A_const_enum_member_can_only_be_accessed_using_a_string_literal);
            return _errorType!;
        }
        var effectiveIndexType = indexType;
        if (IsForInVariableForNumericPropertyNames(indexExpression))
        {
            effectiveIndexType = _numberType!;
        }
        var assignmentTargetKind = GetAssignmentTargetKind(node);
        AccessFlags accessFlags;
        if (assignmentTargetKind == AssignmentKind.None)
        {
            accessFlags = AccessFlags.ExpressionPosition;
        }
        else
        {
            accessFlags = AccessFlags.Writing |
                IfElse(assignmentTargetKind == AssignmentKind.Compound, AccessFlags.ExpressionPosition, (AccessFlags)0) |
                IfElse(IsGenericObjectType(objectType) && !IsThisTypeParameter(objectType), AccessFlags.NoIndexSignatures, (AccessFlags)0);
        }
        var indexedAccessType = OrElse(GetIndexedAccessTypeOrUndefined(objectType, effectiveIndexType, accessFlags, node, null), _errorType)!;
        return CheckIndexedAccessIndexType(GetFlowTypeOfAccessExpression(node, GetResolvedSymbolOrNil(node), indexedAccessType, indexExpression, checkMode), node);
    }

    // Return true if given node is an expression consisting of an identifier (possibly parenthesized)
    // that references a for-in variable for an object with numeric property names.
    internal bool IsForInVariableForNumericPropertyNames(Node expr)
    {
        var e = SkipParentheses(expr);
        if (IsIdentifier(e!))
        {
            var symbol = GetResolvedSymbolWorker(e!);
            if ((symbol.Flags & SymbolFlags.Variable) != 0)
            {
                var child = expr;
                var node = expr.Parent;
                while (node != null)
                {
                    if (IsForInStatement(node) && child == node.AsForInOrOfStatement()!.Statement && GetForInVariableSymbol(node) == symbol && HasNumericPropertyNames(GetTypeOfExpression(node.Expression()!)))
                    {
                        return true;
                    }
                    child = node;
                    node = node.Parent;
                }
            }
        }
        return false;
    }

    // Return the symbol of the for-in variable declared or referenced by the given for-in statement.
    internal Symbol? GetForInVariableSymbol(Node node)
    {
        var initializer = node.Initializer();
        if (IsVariableDeclarationList(initializer!))
        {
            var declarations = initializer!.AsVariableDeclarationList()!.Declarations?.Nodes ?? [];
            if (declarations.Length > 0)
            {
                var variable = declarations[0];
                if (variable != null && !IsBindingPattern(variable.Name()))
                {
                    return GetSymbolOfDeclaration(variable);
                }
            }
        }
        else if (IsIdentifier(initializer!))
        {
            return GetResolvedSymbolWorker(initializer!);
        }
        return null;
    }

    // Return true if the given type is considered to have numeric property names.
    internal bool HasNumericPropertyNames(Type t)
    {
        return (GetIndexInfosOfTypeWorker(t)?.Length ?? 0) == 1 && GetIndexInfoOfTypeWorker(t, _numberType!) != null;
    }

    internal Type CheckIndexedAccessIndexType(Type t, Node accessNode)
    {
        if ((t._flags & TypeFlags.IndexedAccess) == 0)
        {
            return t;
        }
        // Check if the index type is assignable to 'keyof T' for the object type.
        var objectType = t.AsIndexedAccessType()!._objectType!;
        var indexType = t.AsIndexedAccessType()!._indexType!;
        // skip index type deferral on remapping mapped types
        Type? objectIndexType;
        if (IsGenericMappedType(objectType) && GetMappedTypeNameTypeKind(objectType) == MappedTypeNameTypeKind.Remapping)
        {
            objectIndexType = GetIndexTypeForMappedType(objectType, IndexFlags.None);
        }
        else
        {
            objectIndexType = GetIndexTypeEx(objectType, IndexFlags.None);
        }
        var hasNumberIndexInfo = GetIndexInfoOfTypeWorker(objectType, _numberType!) != null;
        if (EveryType(indexType, t => IsTypeAssignableToWorker(t, objectIndexType!) || hasNumberIndexInfo && IsApplicableIndexType(t, _numberType!)))
        {
            if (accessNode.Kind == Kind.ElementAccessExpression && IsAssignmentTarget(accessNode) && (objectType._objectFlags & ObjectFlags.Mapped) != 0 && (GetMappedTypeModifiers(objectType) & MappedTypeModifiers.IncludeReadonly) != 0)
            {
                Error(accessNode, Diagnostics.Index_signature_in_type_0_only_permits_reading, TypeToString(objectType));
            }
            return t;
        }
        if (IsGenericObjectType(objectType))
        {
            var propertyName = GetPropertyNameFromIndex(indexType, accessNode);
            if (propertyName != InternalSymbolNameMissing)
            {
                var propertySymbol = GetConstituentProperty(objectType, propertyName);
                if (propertySymbol != null && (GetDeclarationModifierFlagsFromSymbol(propertySymbol) & ModifierFlags.NonPublicAccessibilityModifier) != 0)
                {
                    Error(accessNode, Diagnostics.Private_or_protected_member_0_cannot_be_accessed_on_a_type_parameter, propertyName);
                    return _errorType!;
                }
            }
        }
        Error(accessNode, Diagnostics.Type_0_cannot_be_used_to_index_type_1, TypeToString(indexType), TypeToString(objectType));
        return _errorType!;
    }

    internal Symbol? GetConstituentProperty(Type objectType, string propertyName)
    {
        foreach (var t in GetApparentTypeWorker(objectType).Distributed())
        {
            var prop = GetPropertyOfTypeWorker(t, propertyName);
            if (prop != null)
            {
                return prop;
            }
        }
        return null;
    }

    internal Type CheckImportCallExpression(Node node)
    {
        // Check grammar of dynamic import
        CheckGrammarImportCallExpression(node);
        var args = node.Arguments() ?? [];
        if (args.Length == 0)
        {
            // No call arguments exist, so there are no child expressions to check.
            return CreatePromiseReturnType(node, _anyType!); //nolint:customlint // no arguments to check
        }
        var specifier = args[0];
        var specifierType = CheckExpressionCached(specifier);
        Type? optionsType = null;
        if (args.Length > 1)
        {
            optionsType = CheckExpressionCached(args[1]);
        }
        // Even though multiple arguments is grammatically incorrect, type-check extra arguments for completion
        for (var i = 2; i < args.Length; i++)
        {
            CheckExpressionCached(args[i]);
        }
        if ((specifierType._flags & TypeFlags.Nullable) != 0 || !IsTypeAssignableToWorker(specifierType, _stringType!))
        {
            Error(specifier, Diagnostics.Dynamic_import_s_specifier_must_be_of_type_string_but_here_has_type_0, TypeToString(specifierType));
        }
        Type? importAttributesType = null;
        if (optionsType != null)
        {
            var importCallOptionsType = _getGlobalImportCallOptionsTypeChecked();
            if (importCallOptionsType != _emptyObjectType)
            {
                CheckTypeAssignableTo(optionsType, GetNullableType(importCallOptionsType, TypeFlags.Undefined), args[1], null);
            }
            if (IsObjectLiteralExpression(args[1]))
            {
                foreach (var prop in args[1].AsObjectLiteralExpression()!.Properties?.Nodes ?? [])
                {
                    if (IsPropertyAssignment(prop) && IsIdentifier(prop.Name()!) && prop.Name()!.Text() == "assert")
                    {
                        Error(prop.Name(), Diagnostics.Import_assertions_have_been_replaced_by_import_attributes_Use_with_instead_of_assert);
                        break;
                    }
                }
            }
            importAttributesType = GetTypeOfPropertyOfTypeWorker(optionsType, "with");
        }
        // resolveExternalModuleName will return undefined if the moduleReferenceExpression is not a string literal
        var moduleSymbol = ResolveExternalModuleNameWorker(node, specifier, false /*ignoreErrors*/, importAttributesType);
        if (moduleSymbol != null)
        {
            var esModuleSymbol = ResolveExternalModuleSymbolWorker(moduleSymbol, true /*dontResolveAlias*/);
            if (esModuleSymbol != null)
            {
                var syntheticType = GetTypeWithSyntheticDefaultOnly(GetTypeOfSymbolWorker(esModuleSymbol), esModuleSymbol, moduleSymbol, specifier, importAttributesType);
                if (syntheticType == null)
                {
                    syntheticType = GetTypeWithSyntheticDefaultImportType(GetTypeOfSymbolWorker(esModuleSymbol), esModuleSymbol, moduleSymbol, specifier);
                }
                return CreatePromiseReturnType(node, syntheticType!);
            }
        }
        return CreatePromiseReturnType(node, _anyType!);
    }
}
