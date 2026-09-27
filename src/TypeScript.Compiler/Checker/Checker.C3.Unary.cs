// ported from: internal/checker/checker.go:10812-11149（一元/其它表达式检查：checkTypeOfExpression、
//   checkNonNullAssertion、checkNonNullChain、checkExpressionWithTypeArguments、getInstantiationExpressionType、
//   checkSatisfiesExpression、checkMetaProperty、checkNewTargetMetaProperty、checkImportMetaProperty、
//   checkMetaPropertyKeyword、checkDeleteExpression、checkDeleteExpressionMustBeOptional、checkVoidExpression、
//   checkAwaitExpression、checkPrefixUnaryExpression、checkPostfixUnaryExpression、getUnaryResultType、
//   checkConditionalExpression、checkTruthinessExpression、checkSpreadExpression）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) **func 字段**（Go 的 `Checker` 结构体里是委托字段，不是方法）→ `_camelCase` 字段名：
//    `c.getGlobalImportMetaType` → `_getGlobalImportMetaType()`（artifacts 里 ChkC7 的同款提示）。
// 2) Go 的相互递归闭包（`var getInstantiatedType func(*Type) *Type` 里再声明 `getInstantiatedTypePart`）→ C# 的
//    **本地函数**（`GetInstantiatedType` 内嵌 `GetInstantiatedTypePart`）；两者互相递归、闭包捕获外层局部变量
//    （`hasSomeApplicableSignature`/`nonApplicableType`/`node`），语义与 Go 的闭包一致。
// 3) `jsnum.FromString` → `FromString`（using static TypeScript.JsNum.JsNum）；Go 的 `+jsnum.FromString(x)`
//    （一元加，把无类型常量转成 jsnum.Number 的恒等运算）在 C# 里省略 —— `FromString` 已经返回 `Number`。
// 4) Worker 改名（artifacts/checker-method-collisions.md）：`getBaseConstraintOfType` → `GetBaseConstraintOfTypeWorker`、
//    `getBaseTypeOfLiteralType` → `GetBaseTypeOfLiteralTypeWorker`、`getUnionType` → `GetUnionTypeWorker`、
//    `getUnionTypeEx` → `GetUnionTypeExWorker`、`getSignaturesOfType` → `GetSignaturesOfTypeWorker`。
// 5) `debug.Assert(…)` → `Debug.Assert(…)`；`panic(...)` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`。
// 6) `ast.InternalSymbolNameInstantiationExpression` 在 C# 里是 `Ast` 类里的 `public const string` →
//    按 `using static TypeScript.Ast.Ast;` 的裸名 `InternalSymbolNameInstantiationExpression` 调用。
// 7) `resolved.members`/`resolved.indexInfos`（Go 未导出字段）→ `resolved._members`/`resolved._indexInfos`（§5.5）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal Type CheckTypeOfExpression(Node node)
    {
        CheckExpression(node.Expression()!);
        return _typeofType;
    }

    internal Type CheckNonNullAssertion(Node node)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            // checkNonNullChain checks the same operand expression (node.Expression()),
            // so the child is still visited on this branch.
            return CheckNonNullChain(node); //nolint:customlint // checkNonNullChain checks the operand
        }
        return GetNonNullableType(CheckExpression(node.Expression()!));
    }

    internal Type CheckNonNullChain(Node node)
    {
        var leftType = CheckExpression(node.Expression()!);
        var nonOptionalType = GetOptionalExpressionType(leftType, node.Expression()!);
        return PropagateOptionalTypeMarker(GetNonNullableType(nonOptionalType), node, nonOptionalType != leftType);
    }

    internal Type CheckExpressionWithTypeArguments(Node node)
    {
        CheckGrammarExpressionWithTypeArguments(node);
        CheckSourceElements(node.TypeArguments() ?? []);
        if (IsExpressionWithTypeArguments(node))
        {
            var parent = WalkUpParenthesizedExpressions(node.Parent);
            if (IsBinaryExpression(parent!) && parent!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.InstanceOfKeyword && IsNodeDescendantOf(node, parent.AsBinaryExpression()!.Right!))
            {
                Error(node, Diagnostics.The_right_hand_side_of_an_instanceof_expression_must_not_be_an_instantiation_expression);
            }
        }
        Type exprType;
        if (IsExpressionWithTypeArguments(node))
        {
            exprType = CheckExpression(node.Expression()!);
        }
        else
        {
            var exprName = node.AsTypeQueryNode()!.ExprName!;
            if (IsThisIdentifier(exprName))
            {
                exprType = CheckThisExpression(node.AsTypeQueryNode()!.ExprName!);
            }
            else
            {
                exprType = CheckExpression(node.AsTypeQueryNode()!.ExprName!);
            }
        }
        return GetInstantiationExpressionType(exprType, node);
    }

    internal Type GetInstantiationExpressionType(Type exprType, Node node)
    {
        var typeArguments = node.TypeArgumentList();
        if (exprType == _silentNeverType || IsErrorType(exprType) || typeArguments == null)
        {
            return exprType;
        }
        var key = new InstantiationExpressionKey { _nodeId = GetNodeId(node), _typeId = exprType._id };
        if (_instantiationExpressionTypes.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }
        var hasSomeApplicableSignature = false;
        Type? nonApplicableType = null;

        Signature[] GetInstantiatedSignatures(Signature[] signatures)
        {
            var applicableSignatures = Filter(signatures, sig => sig.TypeParameters().Length != 0 && HasCorrectTypeArgumentArity(sig, typeArguments.Nodes ?? []));
            return SameMap(applicableSignatures, sig =>
            {
                var typeArgumentTypes = CheckTypeArguments(sig, typeArguments.Nodes, true /*reportErrors*/, null);
                if (typeArgumentTypes != null)
                {
                    return GetSignatureInstantiation(sig, typeArgumentTypes, IsInJSFile(sig._declaration), null);
                }
                return sig;
            });
        }

        Type GetInstantiatedType(Type t)
        {
            var hasSignatures = false;
            var hasApplicableSignature = false;

            Type GetInstantiatedTypePart(Type t2)
            {
                if ((t2._flags & TypeFlags.Object) != 0)
                {
                    var resolved = ResolveStructuredTypeMembers(t2);
                    var callSignatures = GetInstantiatedSignatures(resolved.CallSignatures());
                    var constructSignatures = GetInstantiatedSignatures(resolved.ConstructSignatures());
                    hasSignatures = hasSignatures || resolved.CallSignatures().Length != 0 || resolved.ConstructSignatures().Length != 0;
                    hasApplicableSignature = hasApplicableSignature || callSignatures.Length != 0 || constructSignatures.Length != 0;
                    if (!Same(callSignatures, resolved.CallSignatures()) || !Same(constructSignatures, resolved.ConstructSignatures()))
                    {
                        var symbol = NewSymbol(SymbolFlags.None, InternalSymbolNameInstantiationExpression);
                        Debug.Assert(t2._symbol != null, "Instantiation expression source type must have a symbol");
                        symbol.Declarations = t2._symbol!.Declarations;
                        var result = NewObjectType(ObjectFlags.Anonymous | ObjectFlags.InstantiationExpressionType, symbol);
                        SetStructuredTypeMembers(result, resolved._members, callSignatures, constructSignatures, resolved._indexInfos);
                        result.AsInstantiationExpressionType()!._node = node;
                        return result;
                    }
                }
                else if ((t2._flags & TypeFlags.InstantiableNonPrimitive) != 0)
                {
                    var constraint = GetBaseConstraintOfTypeWorker(t2);
                    if (constraint != null)
                    {
                        var instantiated = GetInstantiatedTypePart(constraint);
                        if (instantiated != constraint)
                        {
                            return instantiated;
                        }
                    }
                }
                else if ((t2._flags & TypeFlags.Union) != 0)
                {
                    return MapType(t2, x => GetInstantiatedType(x));
                }
                else if ((t2._flags & TypeFlags.Intersection) != 0)
                {
                    return GetIntersectionType(SameMap(t2.AsIntersectionType()!._types!, x => GetInstantiatedTypePart(x)));
                }
                return t2;
            }

            var result = GetInstantiatedTypePart(t);
            hasSomeApplicableSignature = hasSomeApplicableSignature || hasApplicableSignature;
            if (hasSignatures && !hasApplicableSignature)
            {
                nonApplicableType ??= t;
            }
            return result;
        }

        var instantiationResult = GetInstantiatedType(exprType);
        _instantiationExpressionTypes[key] = instantiationResult;
        Type? errorType;
        if (hasSomeApplicableSignature)
        {
            errorType = nonApplicableType;
        }
        else
        {
            errorType = exprType;
        }
        if (errorType != null)
        {
            var sourceFile = GetSourceFileOfNode(node)!;
            var loc = NewTextRange(SkipTrivia(sourceFile.Text(), typeArguments.Pos()), typeArguments.End());
            AddDiagnostic(NewDiagnostic(sourceFile, loc, Diagnostics.Type_0_has_no_signatures_for_which_the_type_argument_list_is_applicable, TypeToString(errorType)));
        }
        return instantiationResult;
    }

    internal Type CheckSatisfiesExpression(Node node)
    {
        var typeNode = node.Type()!;
        CheckSourceElement(typeNode);
        var exprType = CheckExpression(node.Expression()!);
        var targetType = GetTypeFromTypeNodeWorker(typeNode);
        if (IsErrorType(targetType))
        {
            return targetType;
        }
        CheckTypeAssignableToAndOptionallyElaborate(exprType, targetType, node, node.Expression(), Diagnostics.Type_0_does_not_satisfy_the_expected_type_1, null);
        return exprType;
    }

    internal Type CheckMetaProperty(Node node)
    {
        CheckGrammarMetaProperty(node.AsMetaProperty()!);
        switch (node.AsMetaProperty()!.KeywordToken)
        {
            case Kind.NewKeyword:
                return CheckNewTargetMetaProperty(node);
            case Kind.ImportKeyword:
                if (node.Name()!.Text() == "defer")
                {
                    Debug.Assert(!IsCallExpression(node.Parent!) || node.Parent!.Expression() != node, "Trying to get the type of `import.defer` in `import.defer(...)`");
                    return _errorType;
                }
                return CheckImportMetaProperty(node);
        }
        Debug.Fail("Unhandled case in checkMetaProperty");
        throw new System.Diagnostics.UnreachableException();
    }

    internal Type CheckNewTargetMetaProperty(Node node)
    {
        var container = GetNewTargetContainer(node);
        if (container == null)
        {
            Error(node, Diagnostics.Meta_property_0_is_only_allowed_in_the_body_of_a_function_declaration_function_expression_or_constructor, "new.target");
            return _errorType;
        }
        if (IsConstructorDeclaration(container))
        {
            var symbol = GetSymbolOfDeclaration(container.Parent!)!;
            return GetTypeOfSymbolWorker(symbol);
        }
        var containerSymbol = GetSymbolOfDeclaration(container)!;
        return GetTypeOfSymbolWorker(containerSymbol);
    }

    internal Type CheckImportMetaProperty(Node node)
    {
        if (ModuleKind.Node16 <= _moduleKind && _moduleKind <= ModuleKind.NodeNext)
        {
            var sourceFileMetaData = _program.GetSourceFileMetaData(GetSourceFileOfNode(node)!.Path());
            if (sourceFileMetaData.ImpliedNodeFormat != ModuleKind.ESNext)
            {
                Error(node, Diagnostics.The_import_meta_meta_property_is_not_allowed_in_files_which_will_build_into_CommonJS_output);
            }
        }
        else if (_moduleKind < ModuleKind.ES2020 && _moduleKind != ModuleKind.System)
        {
            Error(node, Diagnostics.The_import_meta_meta_property_is_only_allowed_when_the_module_option_is_es2020_es2022_esnext_system_node16_node18_node20_or_nodenext);
        }
        var file = GetSourceFileOfNode(node)!;
        Debug.Assert((file.Flags & NodeFlags.PossiblyContainsImportMeta) != 0, "Containing file is missing import meta node flag.");
        if (node.Name()!.Text() == "meta")
        {
            return _getGlobalImportMetaType();
        }
        return _errorType;
    }

    internal Type CheckMetaPropertyKeyword(Node node)
    {
        // !!! This is effectively a helper for GetSymbolAtLocation and GetTypeAtLocation
        return _errorType;
    }

    internal Type CheckDeleteExpression(Node node)
    {
        CheckExpression(node.Expression()!);
        var expr = SkipParentheses(node.Expression());
        if (!IsAccessExpression(expr!))
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_must_be_a_property_reference);
            return _booleanType;
        }
        if (IsPropertyAccessExpression(expr!) && IsPrivateIdentifier(expr!.Name()!))
        {
            Error(expr, Diagnostics.The_operand_of_a_delete_operator_cannot_be_a_private_identifier);
        }
        var symbol = GetExportSymbolOfValueSymbolIfExported(GetResolvedSymbolOrNil(expr!));
        if (symbol != null)
        {
            if (IsReadonlySymbolWorker(symbol))
            {
                Error(expr, Diagnostics.The_operand_of_a_delete_operator_cannot_be_a_read_only_property);
            }
            else
            {
                CheckDeleteExpressionMustBeOptional(expr!, symbol);
            }
        }
        return _booleanType;
    }

    internal void CheckDeleteExpressionMustBeOptional(Node expr, Symbol symbol)
    {
        var t = GetTypeOfSymbolWorker(symbol);
        if (_strictNullChecks && (t._flags & (TypeFlags.AnyOrUnknown | TypeFlags.Never)) == 0)
        {
            bool isOptional;
            if (_exactOptionalPropertyTypes)
            {
                isOptional = (symbol.Flags & SymbolFlags.Optional) != 0;
            }
            else
            {
                isOptional = HasTypeFacts(t, TypeFacts.IsUndefined);
            }
            if (!isOptional)
            {
                Error(expr, Diagnostics.The_operand_of_a_delete_operator_must_be_optional);
            }
        }
    }

    internal Type CheckVoidExpression(Node node)
    {
        CheckNodeDeferred(node);
        return _undefinedWideningType;
    }

    internal Type CheckAwaitExpression(Node node)
    {
        CheckGrammarAwaitOrAwaitUsing(node);
        var operandType = CheckExpression(node.Expression()!);
        var awaitedType = CheckAwaitedType(operandType, true /*withAlias*/, node, Diagnostics.Type_of_await_operand_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
        if (awaitedType == operandType && !IsErrorType(awaitedType) && (operandType._flags & TypeFlags.AnyOrUnknown) == 0)
        {
            AddErrorOrSuggestion(false, CreateDiagnosticForNode(node, Diagnostics.X_await_has_no_effect_on_the_type_of_this_expression));
        }
        return awaitedType;
    }

    internal Type CheckPrefixUnaryExpression(Node node)
    {
        var expr = node.AsPrefixUnaryExpression()!;
        var operandType = CheckExpression(expr.Operand!);
        if (operandType == _silentNeverType)
        {
            return _silentNeverType;
        }
        switch (expr.Operand!.Kind)
        {
            case Kind.NumericLiteral:
                if (expr.Operator == Kind.MinusToken)
                {
                    return GetFreshTypeOfLiteralType(GetNumberLiteralType(-FromString(expr.Operand!.Text())));
                }
                if (expr.Operator == Kind.PlusToken)
                {
                    return GetFreshTypeOfLiteralType(GetNumberLiteralType(FromString(expr.Operand!.Text())));
                }
                break;
            case Kind.BigIntLiteral:
                if (expr.Operator == Kind.MinusToken)
                {
                    return GetFreshTypeOfLiteralType(GetBigIntLiteralType(NewPseudoBigInt(ParsePseudoBigInt(expr.Operand!.Text()), true /*negative*/)));
                }
                break;
        }
        switch (expr.Operator)
        {
            case Kind.PlusToken:
            case Kind.MinusToken:
            case Kind.TildeToken:
                CheckNonNullType(operandType, expr.Operand!);
                if (MaybeTypeOfKindConsideringBaseConstraint(operandType, TypeFlags.ESSymbolLike))
                {
                    Error(expr.Operand!, Diagnostics.The_0_operator_cannot_be_applied_to_type_symbol, TokenToString(expr.Operator));
                }
                if (expr.Operator == Kind.PlusToken)
                {
                    if (MaybeTypeOfKindConsideringBaseConstraint(operandType, TypeFlags.BigIntLike))
                    {
                        Error(expr.Operand!, Diagnostics.Operator_0_cannot_be_applied_to_type_1, TokenToString(expr.Operator), TypeToString(GetBaseTypeOfLiteralTypeWorker(operandType)));
                    }
                    return _numberType;
                }
                return GetUnaryResultType(operandType);
            case Kind.ExclamationToken:
                CheckTruthinessOfType(operandType, expr.Operand!);
                var facts = GetTypeFacts(operandType, TypeFacts.Truthy | TypeFacts.Falsy);
                if (facts == TypeFacts.Truthy)
                {
                    return _falseType;
                }
                if (facts == TypeFacts.Falsy)
                {
                    return _trueType;
                }
                return _booleanType;
            case Kind.PlusPlusToken:
            case Kind.MinusMinusToken:
                var ok = CheckArithmeticOperandType(expr.Operand!, CheckNonNullType(operandType, expr.Operand!), Diagnostics.An_arithmetic_operand_must_be_of_type_any_number_bigint_or_an_enum_type, false);
                if (ok)
                {
                    // run check only if former checks succeeded to avoid reporting cascading errors
                    CheckReferenceExpression(expr.Operand!, Diagnostics.The_operand_of_an_increment_or_decrement_operator_must_be_a_variable_or_a_property_access, Diagnostics.The_operand_of_an_increment_or_decrement_operator_may_not_be_an_optional_property_access);
                }
                return GetUnaryResultType(operandType);
        }
        return _errorType;
    }

    internal Type CheckPostfixUnaryExpression(Node node)
    {
        var expr = node.AsPostfixUnaryExpression()!;
        var operandType = CheckExpression(expr.Operand!);
        if (operandType == _silentNeverType)
        {
            return _silentNeverType;
        }
        var ok = CheckArithmeticOperandType(expr.Operand!, CheckNonNullType(operandType, expr.Operand!), Diagnostics.An_arithmetic_operand_must_be_of_type_any_number_bigint_or_an_enum_type, false);
        if (ok)
        {
            // run check only if former checks succeeded to avoid reporting cascading errors
            CheckReferenceExpression(expr.Operand!, Diagnostics.The_operand_of_an_increment_or_decrement_operator_must_be_a_variable_or_a_property_access, Diagnostics.The_operand_of_an_increment_or_decrement_operator_may_not_be_an_optional_property_access);
        }
        return GetUnaryResultType(operandType);
    }

    internal Type GetUnaryResultType(Type operandType)
    {
        if (MaybeTypeOfKind(operandType, TypeFlags.BigIntLike))
        {
            if (IsTypeAssignableToKind(operandType, TypeFlags.AnyOrUnknown) || MaybeTypeOfKind(operandType, TypeFlags.NumberLike))
            {
                return _numberOrBigIntType;
            }
            return _bigintType;
        }
        // If it's not a bigint type, implicit coercion will result in a number
        return _numberType;
    }

    internal Type CheckConditionalExpression(Node node, CheckMode checkMode)
    {
        var cond = node.AsConditionalExpression()!;
        var t = CheckTruthinessExpression(cond.Condition!, checkMode);
        CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(cond.Condition!, t, cond.WhenTrue!);
        var type1 = CheckExpressionEx(cond.WhenTrue!, checkMode);
        var type2 = CheckExpressionEx(cond.WhenFalse!, checkMode);
        return GetUnionTypeExWorker([type1, type2], UnionReduction.Subtype, null, null);
    }

    internal Type CheckTruthinessExpression(Node node, CheckMode checkMode)
    {
        return CheckTruthinessOfType(CheckExpressionEx(node, checkMode), node);
    }

    internal Type CheckSpreadExpression(Node node, CheckMode checkMode)
    {
        var arrayOrIterableType = CheckExpressionEx(node.Expression()!, checkMode);
        return CheckIteratedTypeOrElementType(IterationUse.Spread, arrayOrIterableType, _undefinedType, node.Expression());
    }
}
