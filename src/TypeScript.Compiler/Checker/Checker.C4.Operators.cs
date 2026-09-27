// ported from: internal/checker/checker.go:12487-13081
//   （checkAssertion / checkAssertionDeferred / checkBinaryExpression / checkBinaryLikeExpression /
//    checkDestructuringAssignment / checkObjectLiteralAssignment / checkObjectLiteralDestructuringPropertyAssignment /
//    checkArrayLiteralAssignment / checkArrayLiteralDestructuringElementAssignment / checkReferenceAssignment /
//    reportOperatorError(+Unless) / getBaseTypesIfUnrelated / checkAssignmentOperator / bothAreBigIntLike /
//    getSuggestedBooleanOperator / checkArithmeticOperandType / checkForDisallowedESSymbolOperand / checkNaNEquality /
//    isGlobalNaN / isTypeEqualityComparableTo / checkTruthinessOfType）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `func(left *Type, right *Type) bool` 参数 → `Func<Type, Type, bool>?`；Go 传 `nil` → `null`。实例方法当函数值
//    传递时一律写 lambda（`(l, r) => BothAreBigIntLike(l, r)`），不依赖方法组转换。
// 2) `panic("Unhandled case in checkBinaryLikeExpression")` → `Debug.Fail(...)` + `throw new UnreachableException()`
//    （`Debug.Fail` 无 `[DoesNotReturn]`）。
// 3) `rhsEval.Value.(jsnum.Number)` → `rhsEval.Value is Number numValue`（Go 的 type assert 两值形式 → C# 模式匹配）。
// 4) `core.NewTextRange(pos, end)` → `new TextRange(pos, end)`（`TextRange` 的构造器是 internal，同程序集可见）。
// 5) Go 的 `var resultType *Type`（可能保持 nil）→ `Type? resultType = null`；确定赋值的分支保留 `Type resultType`
//    （照 Go 的 nil 判定逐行对应）。
// 6) `c.evaluate` 是 Checker 的字段（`evaluator.Evaluator` 委托，Go checker.go:624）→ C# `_evaluate(expr, location)`。
// 7) `LanguageFeatureMinimumTarget.ObjectSpreadRest`（types.go 的包级 var）→ 同一个类的静态成员（见报告）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Evaluator;
using TypeScript.JsNum;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) checkAssertion(node *ast.Node, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckAssertion(Node node, CheckMode checkMode)
    {
        if (node.Kind == Kind.TypeAssertionExpression)
        {
            var file = GetSourceFileOfNode(node);
            if (file is not null && FileExtensionIsOneOf(file.FileName(), [ExtensionMts, ExtensionCts]))
            {
                GrammarErrorOnNode(node, This_syntax_is_reserved_in_files_with_the_mts_or_cts_extension_Use_an_as_expression_instead);
            }

            if (ShouldCheckErasableSyntax(node))
            {
                AddDiagnostic(NewDiagnostic(
                    GetSourceFileOfNode(node),
                    new TextRange(SkipTrivia(GetSourceFileOfNode(node)!.Text(), node.Pos()), node.Expression()!.Pos()),
                    This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled));
            }
        }

        var typeNode = node.Type();
        var exprType = CheckExpressionEx(node.Expression()!, checkMode);

        // Always check the type node so its identifiers are resolved. resolveName knows not
        // to resolve (or report an error for) the `const` in a `const` assertion, so this is
        // safe even for `x as const` and keeps diagnostics stable regardless of traversal order.
        CheckSourceElement(typeNode);
        if (IsConstTypeReference(typeNode!))
        {
            if (!IsValidConstAssertionArgument(node.Expression()!))
            {
                Error(node.Expression()!, A_const_assertion_can_only_be_applied_to_references_to_enum_members_or_string_number_boolean_array_or_object_literals);
            }

            return GetRegularTypeOfLiteralType(exprType);
        }

        var links = _assertionLinks.Get(node);
        links._exprType = exprType;
        CheckNodeDeferred(node);
        return GetTypeFromTypeNodeWorker(typeNode!);
    }

    /// <summary>Go: <c>func (c *Checker) checkAssertionDeferred(node *ast.Node)</c>。</summary>
    internal void CheckAssertionDeferred(Node node)
    {
        var typeNode = node.Type();
        var exprType = GetRegularTypeOfObjectLiteral(GetBaseTypeOfLiteralTypeWorker(_assertionLinks.Get(node)._exprType!));
        var targetType = GetTypeFromTypeNodeWorker(typeNode!);
        if (!IsErrorType(targetType))
        {
            var widenedType = GetWidenedTypeWorker(exprType);
            if (!IsTypeComparableTo(targetType, widenedType))
            {
                var errNode = node;
                if ((typeNode!.Flags & NodeFlags.Reparsed) != 0)
                {
                    errNode = typeNode;
                }

                CheckTypeComparableTo(exprType, targetType, errNode, Conversion_of_type_0_to_type_1_may_be_a_mistake_because_neither_type_sufficiently_overlaps_with_the_other_If_this_was_intentional_convert_the_expression_to_unknown_first);
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) checkBinaryExpression(node *ast.Node, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckBinaryExpression(Node node, CheckMode checkMode)
    {
        var binary = node.AsBinaryExpression()!;
        return CheckBinaryLikeExpression(binary.Left!, binary.OperatorToken!, binary.Right!, checkMode, node);
    }

    /// <summary>Go: <c>func (c *Checker) checkBinaryLikeExpression(left, operatorToken, right *ast.Node, checkMode CheckMode, errorNode *ast.Node) *Type</c>。</summary>
    internal Type CheckBinaryLikeExpression(Node left, Node operatorToken, Node right, CheckMode checkMode, Node? errorNode)
    {
        var @operator = operatorToken.Kind;
        if (@operator == Kind.EqualsToken && (left.Kind == Kind.ObjectLiteralExpression || left.Kind == Kind.ArrayLiteralExpression))
        {
            return CheckDestructuringAssignment(left, CheckExpressionEx(right, checkMode), checkMode, right.Kind == Kind.ThisKeyword);
        }

        var leftType = CheckExpressionEx(left, checkMode);
        var rightType = CheckExpressionEx(right, checkMode);
        if (IsLogicalOrCoalescingBinaryOperator(@operator))
        {
            var parent = left.Parent!.Parent!;
            while (IsParenthesizedExpression(parent) || IsLogicalOrCoalescingBinaryExpression(parent))
            {
                parent = parent.Parent!;
            }

            if (@operator == Kind.AmpersandAmpersandToken || IsIfStatement(parent))
            {
                Node? body = null;
                if (IsIfStatement(parent))
                {
                    body = parent.AsIfStatement()!.ThenStatement;
                }

                CheckTestingKnownTruthyCallableOrAwaitableOrEnumMemberType(left, leftType, body);
            }

            if (IsLogicalBinaryOperator(@operator))
            {
                CheckTruthinessOfType(leftType, left);
            }
        }

        switch (@operator)
        {
            case Kind.AsteriskToken:
            case Kind.AsteriskAsteriskToken:
            case Kind.AsteriskEqualsToken:
            case Kind.AsteriskAsteriskEqualsToken:
            case Kind.SlashToken:
            case Kind.SlashEqualsToken:
            case Kind.PercentToken:
            case Kind.PercentEqualsToken:
            case Kind.MinusToken:
            case Kind.MinusEqualsToken:
            case Kind.LessThanLessThanToken:
            case Kind.LessThanLessThanEqualsToken:
            case Kind.GreaterThanGreaterThanToken:
            case Kind.GreaterThanGreaterThanEqualsToken:
            case Kind.GreaterThanGreaterThanGreaterThanToken:
            case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
            case Kind.BarToken:
            case Kind.BarEqualsToken:
            case Kind.CaretToken:
            case Kind.CaretEqualsToken:
            case Kind.AmpersandToken:
            case Kind.AmpersandEqualsToken:
                if (leftType == _silentNeverType || rightType == _silentNeverType)
                {
                    return _silentNeverType!;
                }

                leftType = CheckNonNullType(leftType, left);
                rightType = CheckNonNullType(rightType, right);

                // if a user tries to apply a bitwise operator to 2 boolean operands
                // try and return them a helpful suggestion
                if ((leftType._flags & TypeFlags.BooleanLike) != 0 && (rightType._flags & TypeFlags.BooleanLike) != 0)
                {
                    var suggestedOperator = GetSuggestedBooleanOperator(@operator);
                    if (suggestedOperator != Kind.Unknown)
                    {
                        Error(operatorToken, The_0_operator_is_not_allowed_for_boolean_types_Consider_using_1_instead, TokenToString(operatorToken.Kind), TokenToString(suggestedOperator));
                        return _numberType!;
                    }
                }

                // otherwise just check each operand separately and report errors as normal
                var leftOk = CheckArithmeticOperandType(left, leftType, The_left_hand_side_of_an_arithmetic_operation_must_be_of_type_any_number_bigint_or_an_enum_type, true /*isAwaitValid*/);
                var rightOk = CheckArithmeticOperandType(right, rightType, The_right_hand_side_of_an_arithmetic_operation_must_be_of_type_any_number_bigint_or_an_enum_type, true /*isAwaitValid*/);
                Type resultType;
                // If both are any or unknown, allow operation; assume it will resolve to number
                if ((IsTypeAssignableToKind(leftType, TypeFlags.AnyOrUnknown) && IsTypeAssignableToKind(rightType, TypeFlags.AnyOrUnknown))
                    || (!MaybeTypeOfKind(leftType, TypeFlags.BigIntLike) && !MaybeTypeOfKind(rightType, TypeFlags.BigIntLike)))
                {
                    resultType = _numberType!;
                }
                else if (BothAreBigIntLike(leftType, rightType))
                {
                    switch (@operator)
                    {
                        case Kind.GreaterThanGreaterThanGreaterThanToken:
                        case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
                            ReportOperatorError(leftType, @operator, rightType, errorNode, null);
                            break;
                        case Kind.AsteriskAsteriskToken:
                        case Kind.AsteriskAsteriskEqualsToken:
                            if (_languageVersion < ScriptTarget.ES2016)
                            {
                                Error(errorNode, Exponentiation_cannot_be_performed_on_bigint_values_unless_the_target_option_is_set_to_es2016_or_later);
                            }

                            break;
                    }

                    resultType = _bigintType!;
                }
                else
                {
                    ReportOperatorError(leftType, @operator, rightType, errorNode, BothAreBigIntLike);
                    resultType = _errorType!;
                }

                if (leftOk && rightOk)
                {
                    CheckAssignmentOperator(left, @operator, right, leftType, resultType);
                    switch (@operator)
                    {
                        case Kind.LessThanLessThanToken:
                        case Kind.LessThanLessThanEqualsToken:
                        case Kind.GreaterThanGreaterThanToken:
                        case Kind.GreaterThanGreaterThanEqualsToken:
                        case Kind.GreaterThanGreaterThanGreaterThanToken:
                        case Kind.GreaterThanGreaterThanGreaterThanEqualsToken:
                            var rhsEval = _evaluate(right, right);
                            if (rhsEval.Value is Number numValue && numValue.Abs() >= 32)
                            {
                                // Elevate from suggestion to error within an enum member
                                ErrorOrSuggestion(IsEnumMember(WalkUpParenthesizedExpressions(right.Parent!.Parent!)!), errorNode, This_operation_can_be_simplified_This_shift_is_identical_to_0_1_2, GetTextOfNode(left), TokenToString(@operator), numValue.Remainder(32));
                            }

                            break;
                    }
                }

                return resultType;
            case Kind.PlusToken:
            case Kind.PlusEqualsToken:
                if (leftType == _silentNeverType || rightType == _silentNeverType)
                {
                    return _silentNeverType!;
                }

                if (!IsTypeAssignableToKind(leftType, TypeFlags.StringLike) && !IsTypeAssignableToKind(rightType, TypeFlags.StringLike))
                {
                    leftType = CheckNonNullType(leftType, left);
                    rightType = CheckNonNullType(rightType, right);
                }

                Type? plusResultType = null;
                if (IsTypeAssignableToKindEx(leftType, TypeFlags.NumberLike, true /*strict*/) && IsTypeAssignableToKindEx(rightType, TypeFlags.NumberLike, true /*strict*/))
                {
                    // Operands of an enum type are treated as having the primitive type Number.
                    // If both operands are of the Number primitive type, the result is of the Number primitive type.
                    plusResultType = _numberType;
                }
                else if (IsTypeAssignableToKindEx(leftType, TypeFlags.BigIntLike, true /*strict*/) && IsTypeAssignableToKindEx(rightType, TypeFlags.BigIntLike, true /*strict*/))
                {
                    // If both operands are of the BigInt primitive type, the result is of the BigInt primitive type.
                    plusResultType = _bigintType;
                }
                else if (IsTypeAssignableToKindEx(leftType, TypeFlags.StringLike, true /*strict*/) || IsTypeAssignableToKindEx(rightType, TypeFlags.StringLike, true /*strict*/))
                {
                    // If one or both operands are of the String primitive type, the result is of the String primitive type.
                    plusResultType = _stringType;
                }
                else if (IsTypeAny(leftType) || IsTypeAny(rightType))
                {
                    // Otherwise, the result is of type Any.
                    // NOTE: unknown type here denotes error type. Old compiler treated this case as any type so do we.
                    if (IsErrorType(leftType) || IsErrorType(rightType))
                    {
                        plusResultType = _errorType;
                    }
                    else
                    {
                        plusResultType = _anyType;
                    }
                }

                // Symbols are not allowed at all in arithmetic expressions
                if (plusResultType is not null && !CheckForDisallowedESSymbolOperand(left, right, leftType, rightType, @operator))
                {
                    return plusResultType;
                }

                if (plusResultType is null)
                {
                    // Types that have a reasonably good chance of being a valid operand type.
                    // If both types have an awaited type of one of these, we'll assume the user
                    // might be missing an await without doing an exhaustive check that inserting
                    // await(s) will actually be a completely valid binary expression.
                    var closeEnoughKind = TypeFlags.NumberLike | TypeFlags.BigIntLike | TypeFlags.StringLike | TypeFlags.AnyOrUnknown;
                    ReportOperatorError(leftType, @operator, rightType, errorNode, (l, r) =>
                        IsTypeAssignableToKind(l, closeEnoughKind) && IsTypeAssignableToKind(r, closeEnoughKind));
                    return _anyType!;
                }

                if (@operator == Kind.PlusEqualsToken)
                {
                    CheckAssignmentOperator(left, @operator, right, leftType, plusResultType);
                }

                return plusResultType;
            case Kind.LessThanToken:
            case Kind.GreaterThanToken:
            case Kind.LessThanEqualsToken:
            case Kind.GreaterThanEqualsToken:
                if (CheckForDisallowedESSymbolOperand(left, right, leftType, rightType, @operator))
                {
                    leftType = GetBaseTypeOfLiteralTypeForComparison(CheckNonNullType(leftType, left));
                    rightType = GetBaseTypeOfLiteralTypeForComparison(CheckNonNullType(rightType, right));
                    ReportOperatorErrorUnless(leftType, @operator, rightType, errorNode, (l, r) =>
                    {
                        if (IsTypeAny(l) || IsTypeAny(r))
                        {
                            return true;
                        }

                        var leftAssignableToNumber = IsTypeAssignableToWorker(l, _numberOrBigIntType!);
                        var rightAssignableToNumber = IsTypeAssignableToWorker(r, _numberOrBigIntType!);
                        return (leftAssignableToNumber && rightAssignableToNumber)
                            || (!leftAssignableToNumber && !rightAssignableToNumber && AreTypesComparable(l, r));
                    });
                }

                return _booleanType!;
            case Kind.EqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
            case Kind.EqualsEqualsEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
                // We suppress errors in CheckMode.TypeOnly (meaning the invocation came from getTypeOfExpression). During
                // control flow analysis it is possible for operands to temporarily have narrower types, and those narrower
                // types may cause the operands to not be comparable. We don't want such errors reported (see #46475).
                if ((checkMode & CheckMode.TypeOnly) == 0)
                {
                    if ((IsLiteralExpressionOfObject(left) || IsLiteralExpressionOfObject(right))
                        &&
                        // only report for === and !== in JS, not == or !=
                        (!IsInJSFile(left) || (@operator == Kind.EqualsEqualsEqualsToken || @operator == Kind.ExclamationEqualsEqualsToken)))
                    {
                        var eqType = @operator == Kind.EqualsEqualsToken || @operator == Kind.EqualsEqualsEqualsToken;
                        Error(errorNode, This_condition_will_always_return_0_since_JavaScript_compares_objects_by_reference_not_value, IfElse(eqType, "false", "true"));
                    }

                    CheckNaNEquality(errorNode, @operator, left, right);
                    ReportOperatorErrorUnless(leftType, @operator, rightType, errorNode, (l, r) =>
                        IsTypeEqualityComparableTo(l, r) || IsTypeEqualityComparableTo(r, l));
                }

                return _booleanType!;
            case Kind.InstanceOfKeyword:
                return CheckInstanceOfExpression(left, right, leftType, rightType, checkMode);
            case Kind.InKeyword:
                return CheckInExpression(left, right, leftType, rightType);
            case Kind.AmpersandAmpersandToken:
            case Kind.AmpersandAmpersandEqualsToken:
                var andResultType = leftType;
                if (HasTypeFacts(leftType, TypeFacts.Truthy))
                {
                    var t = leftType;
                    if (!_strictNullChecks)
                    {
                        t = GetBaseTypeOfLiteralTypeWorker(rightType);
                    }

                    andResultType = GetUnionTypeWorker([ExtractDefinitelyFalsyTypes(t), rightType]);
                }

                if (@operator == Kind.AmpersandAmpersandEqualsToken)
                {
                    CheckAssignmentOperator(left, @operator, right, leftType, rightType);
                }

                return andResultType;
            case Kind.BarBarToken:
            case Kind.BarBarEqualsToken:
                var orResultType = leftType;
                if (HasTypeFacts(leftType, TypeFacts.Falsy))
                {
                    orResultType = GetUnionTypeExWorker([GetNonNullableType(RemoveDefinitelyFalsyTypes(leftType)), rightType], UnionReduction.Subtype, null, null);
                }

                if (@operator == Kind.BarBarEqualsToken)
                {
                    CheckAssignmentOperator(left, @operator, right, leftType, rightType);
                }

                return orResultType;
            case Kind.QuestionQuestionToken:
            case Kind.QuestionQuestionEqualsToken:
                if (@operator == Kind.QuestionQuestionToken)
                {
                    CheckNullishCoalesceOperands(left, right);
                }

                var nullishResultType = leftType;
                if (HasTypeFacts(leftType, TypeFacts.EQUndefinedOrNull))
                {
                    nullishResultType = GetUnionTypeExWorker([GetNonNullableType(leftType), rightType], UnionReduction.Subtype, null, null);
                }

                if (@operator == Kind.QuestionQuestionEqualsToken)
                {
                    CheckAssignmentOperator(left, @operator, right, leftType, rightType);
                }

                return nullishResultType;
            case Kind.EqualsToken:
                CheckAssignmentOperator(left, @operator, right, leftType, rightType);
                return rightType;
            case Kind.CommaToken:
                if (!_compilerOptions.AllowUnreachableCode.IsTrue() && IsSideEffectFree(left) && !IsIndirectCall(left.Parent!))
                {
                    var sf = GetSourceFileOfNode(left)!;
                    var start = SkipTrivia(sf.Text(), left.Pos());
                    var isInDiag2657 = Some(sf.Diagnostics(), d =>
                    {
                        if (d.Code() != JSX_expressions_must_have_one_parent_element.Code())
                        {
                            return false;
                        }

                        return d.Loc().Contains(start);
                    });
                    if (!isInDiag2657)
                    {
                        Error(left, Left_side_of_comma_operator_is_unused_and_has_no_side_effects);
                    }
                }

                return rightType;
        }

        Debug.Fail("Unhandled case in checkBinaryLikeExpression");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func (c *Checker) checkDestructuringAssignment(node *ast.Node, sourceType *Type, checkMode CheckMode, rightIsThis bool) *Type</c>。</summary>
    internal Type CheckDestructuringAssignment(Node node, Type sourceType, CheckMode checkMode, bool rightIsThis)
    {
        Node target;
        if (IsShorthandPropertyAssignment(node))
        {
            var initializer = node.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer;
            if (initializer is not null)
            {
                // In strict null checking mode, if a default value of a non-undefined type is specified, remove
                // undefined from the final type.
                if (_strictNullChecks && !HasTypeFacts(CheckExpression(initializer), TypeFacts.IsUndefined))
                {
                    sourceType = GetTypeWithFacts(sourceType, TypeFacts.NEUndefined);
                }

                CheckBinaryLikeExpression(node.Name()!, node.AsShorthandPropertyAssignment()!.EqualsToken!, initializer, checkMode, null);
            }

            target = node.Name()!;
        }
        else
        {
            target = node;
        }

        if (IsBinaryExpression(target) && target.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken)
        {
            CheckBinaryExpression(target, checkMode);
            target = target.AsBinaryExpression()!.Left!;

            // A default value is specified, so remove undefined from the final type.
            if (_strictNullChecks)
            {
                sourceType = GetTypeWithFacts(sourceType, TypeFacts.NEUndefined);
            }
        }

        if (IsObjectLiteralExpression(target!))
        {
            return CheckObjectLiteralAssignment(target, sourceType, rightIsThis);
        }

        if (IsArrayLiteralExpression(target))
        {
            return CheckArrayLiteralAssignment(target, sourceType, checkMode);
        }

        return CheckReferenceAssignment(target, sourceType, checkMode);
    }

    /// <summary>Go: <c>func (c *Checker) checkObjectLiteralAssignment(node *ast.Node, sourceType *Type, rightIsThis bool) *Type</c>。</summary>
    internal Type CheckObjectLiteralAssignment(Node node, Type sourceType, bool rightIsThis)
    {
        var properties = node.PropertyList()!;
        if (_strictNullChecks && (properties.Nodes?.Length ?? 0) == 0)
        {
            return CheckNonNullType(sourceType, node);
        }

        for (int i = 0; i < (properties.Nodes?.Length ?? 0); i++)
        {
            CheckObjectLiteralDestructuringPropertyAssignment(node, sourceType, i, properties, rightIsThis);
        }

        return sourceType;
    }

    // Note: If property cannot be a SpreadAssignment, then allProperties does not need to be provided
    /// <summary>Go: <c>func (c *Checker) checkObjectLiteralDestructuringPropertyAssignment(node *ast.Node, objectLiteralType *Type, propertyIndex int, allProperties *ast.NodeList, rightIsThis bool) *Type</c>。</summary>
    internal Type? CheckObjectLiteralDestructuringPropertyAssignment(Node node, Type objectLiteralType, int propertyIndex, NodeList? allProperties, bool rightIsThis)
    {
        var properties = node.Properties()!;
        var property = properties[propertyIndex];
        if (IsPropertyAssignment(property) || IsShorthandPropertyAssignment(property))
        {
            var name = property.Name();

            if (name is not null && IsPrivateIdentifier(name))
            {
                GrammarErrorOnNode(name, Private_identifiers_cannot_be_used_in_destructuring_patterns);
            }

            var exprType = GetLiteralTypeFromPropertyName(name!);
            if (IsTypeUsableAsPropertyNameWorker(exprType))
            {
                var text = GetPropertyNameFromTypeWorker(exprType);
                var prop = GetPropertyOfTypeWorker(objectLiteralType, text);
                if (prop is not null)
                {
                    MarkPropertyAsReferenced(prop, property, rightIsThis);
                    CheckPropertyAccessibility(property, false /*isSuper*/, true /*writing*/, objectLiteralType, prop);
                }
            }

            var elementType = GetIndexedAccessTypeEx(objectLiteralType, exprType, AccessFlags.ExpressionPosition | IfElse(HasDefaultValue(property), AccessFlags.AllowMissing, default), name, null);
            var t = GetFlowTypeOfDestructuring(property, elementType);
            var expr = property;
            if (IsPropertyAssignment(property))
            {
                expr = property.Initializer()!;
            }

            return CheckDestructuringAssignment(expr, t, CheckMode.Normal, false);
        }

        if (IsSpreadAssignment(property))
        {
            if (propertyIndex < properties.Length - 1)
            {
                Error(property, A_rest_element_must_be_last_in_a_destructuring_pattern);
                return null;
            }

            if (_languageVersion < LanguageFeatureMinimumTarget.ObjectSpreadRest)
            {
                CheckExternalEmitHelpers(property, ExternalEmitHelpers.Rest);
            }

            List<Node>? nonRestNames = null;
            if (allProperties is not null)
            {
                foreach (var otherProperty in allProperties.Nodes ?? [])
                {
                    if (!IsSpreadAssignment(otherProperty))
                    {
                        (nonRestNames ??= []).Add(otherProperty.Name()!);
                    }
                }
            }

            var t = GetRestType(objectLiteralType, nonRestNames?.ToArray()!, objectLiteralType._symbol!);
            CheckGrammarForDisallowedTrailingComma(allProperties, A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
            return CheckDestructuringAssignment(property.Expression()!, t, CheckMode.Normal, false);
        }

        Error(property, Property_assignment_expected);
        return null;
    }

    /// <summary>Go: <c>func (c *Checker) checkArrayLiteralAssignment(node *ast.Node, sourceType *Type, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckArrayLiteralAssignment(Node node, Type sourceType, CheckMode checkMode)
    {
        var elements = node.Elements() ?? [];

        // This elementType will be used if the specific property corresponding to this index is not
        // present (aka the tuple element property). This call also checks that the parentType is in
        // fact an iterable or array (depending on target language).
        var possiblyOutOfBoundsType = OrElse(CheckIteratedTypeOrElementType(IterationUse.Destructuring | IterationUse.PossiblyOutOfBounds, sourceType, _undefinedType!, node), _errorType)!;
        var inBoundsType = _compilerOptions.NoUncheckedIndexedAccess == Tristate.TSTrue ? null : possiblyOutOfBoundsType;
        for (int i = 0; i < elements.Length; i++)
        {
            var t = possiblyOutOfBoundsType;
            if (elements[i].Kind == Kind.SpreadElement)
            {
                inBoundsType ??= OrElse(CheckIteratedTypeOrElementType(IterationUse.Destructuring, sourceType, _undefinedType!, node), _errorType);
                t = inBoundsType;
            }

            CheckArrayLiteralDestructuringElementAssignment(node, sourceType, i, t, checkMode);
        }

        return sourceType;
    }

    /// <summary>Go: <c>func (c *Checker) checkArrayLiteralDestructuringElementAssignment(node *ast.Node, sourceType *Type, elementIndex int, elementType *Type, checkMode CheckMode) *Type</c>。</summary>
    internal Type? CheckArrayLiteralDestructuringElementAssignment(Node node, Type sourceType, int elementIndex, Type elementType, CheckMode checkMode)
    {
        var elements = node.ElementList()!;
        var element = elements.Nodes![elementIndex];
        if (!IsOmittedExpression(element))
        {
            if (!IsSpreadElement(element))
            {
                var indexType = GetNumberLiteralType(new Number(elementIndex));
                if (IsArrayLikeTypeWorker(sourceType))
                {
                    // We create a synthetic expression so that getIndexedAccessType doesn't get confused
                    // when the element is a SyntaxKind.ElementAccessExpression.
                    var accessFlags = AccessFlags.ExpressionPosition | IfElse(HasDefaultValue(element), AccessFlags.AllowMissing, default);
                    var accessedElementType = OrElse(GetIndexedAccessTypeOrUndefined(sourceType, indexType, accessFlags, CreateSyntheticExpression(element, indexType, false, null), null), _errorType)!;
                    var assignedType = accessedElementType;
                    if (HasDefaultValue(element))
                    {
                        assignedType = GetTypeWithFacts(accessedElementType, TypeFacts.NEUndefined);
                    }

                    var t = GetFlowTypeOfDestructuring(element, assignedType);
                    return CheckDestructuringAssignment(element, t, checkMode, false);
                }

                return CheckDestructuringAssignment(element, elementType, checkMode, false);
            }

            if (elementIndex < elements.Nodes.Length - 1)
            {
                Error(element, A_rest_element_must_be_last_in_a_destructuring_pattern);
            }
            else
            {
                var restExpression = element.Expression()!;
                if (IsBinaryExpression(restExpression) && restExpression.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken)
                {
                    Error(restExpression.AsBinaryExpression()!.OperatorToken, A_rest_element_cannot_have_an_initializer);
                }
                else
                {
                    CheckGrammarForDisallowedTrailingComma(elements, A_rest_parameter_or_binding_pattern_may_not_have_a_trailing_comma);
                    Type t;
                    if (EveryType(sourceType, IsTupleType))
                    {
                        t = MapType(sourceType, t2 => SliceTupleType(t2, elementIndex, 0));
                    }
                    else
                    {
                        t = CreateArrayType(elementType);
                    }

                    return CheckDestructuringAssignment(restExpression, t, checkMode, false);
                }
            }
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) checkReferenceAssignment(target *ast.Node, sourceType *Type, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckReferenceAssignment(Node target, Type sourceType, CheckMode checkMode)
    {
        var targetType = CheckExpressionEx(target, checkMode);
        var message = IfElse(
            IsSpreadAssignment(target.Parent!),
            The_target_of_an_object_rest_assignment_must_be_a_variable_or_a_property_access,
            The_left_hand_side_of_an_assignment_expression_must_be_a_variable_or_a_property_access);
        var optionalMessage = IfElse(
            IsSpreadAssignment(target.Parent!),
            The_target_of_an_object_rest_assignment_may_not_be_an_optional_property_access,
            The_left_hand_side_of_an_assignment_expression_may_not_be_an_optional_property_access);
        if (CheckReferenceExpression(target, message, optionalMessage))
        {
            CheckTypeAssignableToAndOptionallyElaborate(sourceType, targetType, target, target, null, null);
        }

        return sourceType;
    }

    /// <summary>Go: <c>func (c *Checker) reportOperatorError(leftType *Type, operator ast.Kind, rightType *Type, errorNode *ast.Node, isRelated func(left, right *Type) bool)</c>。</summary>
    internal void ReportOperatorError(Type leftType, Kind @operator, Type rightType, Node? errorNode, Func<Type, Type, bool>? isRelated)
    {
        var wouldWorkWithAwait = false;
        if (isRelated is not null)
        {
            var awaitedLeftType = GetAwaitedTypeNoAlias(leftType);
            var awaitedRightType = GetAwaitedTypeNoAlias(rightType);
            wouldWorkWithAwait = !(awaitedLeftType == leftType && awaitedRightType == rightType)
                && awaitedLeftType is not null
                && awaitedRightType is not null
                && isRelated(awaitedLeftType, awaitedRightType);
        }

        var effectiveLeft = leftType;
        var effectiveRight = rightType;
        if (!wouldWorkWithAwait && isRelated is not null)
        {
            (effectiveLeft, effectiveRight) = GetBaseTypesIfUnrelated(leftType, rightType, isRelated);
        }

        var (leftStr, rightStr) = GetTypeNamesForErrorDisplay(effectiveLeft, effectiveRight);
        switch (@operator)
        {
            case Kind.EqualsEqualsEqualsToken:
            case Kind.EqualsEqualsToken:
            case Kind.ExclamationEqualsEqualsToken:
            case Kind.ExclamationEqualsToken:
                ErrorAndMaybeSuggestAwait(errorNode, wouldWorkWithAwait, This_comparison_appears_to_be_unintentional_because_the_types_0_and_1_have_no_overlap, leftStr, rightStr);
                break;
            default:
                ErrorAndMaybeSuggestAwait(errorNode, wouldWorkWithAwait, Operator_0_cannot_be_applied_to_types_1_and_2, TokenToString(@operator), leftStr, rightStr);
                break;
        }
    }

    /// <summary>Go: <c>func (c *Checker) reportOperatorErrorUnless(leftType *Type, operator ast.Kind, rightType *Type, errorNode *ast.Node, typesAreCompatible func(left, right *Type) bool)</c>。</summary>
    internal void ReportOperatorErrorUnless(Type leftType, Kind @operator, Type rightType, Node? errorNode, Func<Type, Type, bool> typesAreCompatible)
    {
        if (!typesAreCompatible(leftType, rightType))
        {
            ReportOperatorError(leftType, @operator, rightType, errorNode, typesAreCompatible);
        }
    }

    /// <summary>Go: <c>func (c *Checker) getBaseTypesIfUnrelated(leftType, rightType *Type, isRelated func(left, right *Type) bool) (*Type, *Type)</c>。</summary>
    internal (Type Left, Type Right) GetBaseTypesIfUnrelated(Type leftType, Type rightType, Func<Type, Type, bool> isRelated)
    {
        var effectiveLeft = leftType;
        var effectiveRight = rightType;
        var leftBase = GetBaseTypeOfLiteralTypeWorker(leftType);
        var rightBase = GetBaseTypeOfLiteralTypeWorker(rightType);
        if (!isRelated(leftBase, rightBase))
        {
            effectiveLeft = leftBase;
            effectiveRight = rightBase;
        }

        return (effectiveLeft, effectiveRight);
    }

    /// <summary>Go: <c>func (c *Checker) checkAssignmentOperator(left *ast.Node, operator ast.Kind, right *ast.Node, leftType *Type, rightType *Type)</c>。</summary>
    internal void CheckAssignmentOperator(Node left, Kind @operator, Node right, Type leftType, Type rightType)
    {
        if (IsAssignmentOperator(@operator))
        {
            // We ignore assignments of undefined to CommonJS exports when there are multiple assignment declarations
            if (IsDeclarationNode(left.Parent!) && GetAssignmentDeclarationKind(left.Parent!) == JSDeclarationKind.ExportsProperty)
            {
                var symbol = _symbolNodeLinks.Get(left)._resolvedSymbol;
                // Go: `len(symbol.Declarations) > 1`（nil 切片长度为 0）
                if (symbol is not null && (symbol.Declarations?.Length ?? 0) > 1 && (rightType._flags & TypeFlags.Undefined) != 0)
                {
                    return;
                }
            }

            // getters can be a subtype of setters, so to check for assignability we use the setter's type instead
            if (IsCompoundAssignment(@operator) && IsPropertyAccessExpression(left))
            {
                leftType = CheckPropertyAccessExpression(left, CheckMode.Normal, true /*writeOnly*/);
            }

            if (CheckReferenceExpression(left, The_left_hand_side_of_an_assignment_expression_must_be_a_variable_or_a_property_access, The_left_hand_side_of_an_assignment_expression_may_not_be_an_optional_property_access))
            {
                Message? headMessage = null;
                if (_exactOptionalPropertyTypes && IsPropertyAccessExpression(left) && MaybeTypeOfKind(rightType, TypeFlags.Undefined))
                {
                    var target = GetTypeOfPropertyOfTypeWorker(GetTypeOfExpression(left.Expression()!), left.Name()!.Text());
                    if (IsExactOptionalPropertyMismatch(rightType, target))
                    {
                        headMessage = Type_0_is_not_assignable_to_type_1_with_exactOptionalPropertyTypes_Colon_true_Consider_adding_undefined_to_the_type_of_the_target;
                    }
                }

                // to avoid cascading errors check assignability only if 'isReference' check succeeded and no errors were reported
                CheckTypeAssignableToAndOptionallyElaborate(rightType, leftType, left, right, headMessage, null);
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) bothAreBigIntLike(left, right *Type) bool</c>。</summary>
    internal bool BothAreBigIntLike(Type left, Type right)
    {
        return IsTypeAssignableToKind(left, TypeFlags.BigIntLike) && IsTypeAssignableToKind(right, TypeFlags.BigIntLike);
    }

    /// <summary>Go: <c>func (c *Checker) getSuggestedBooleanOperator(operator ast.Kind) ast.Kind</c>。</summary>
    internal Kind GetSuggestedBooleanOperator(Kind @operator)
    {
        switch (@operator)
        {
            case Kind.BarToken:
            case Kind.BarEqualsToken:
                return Kind.BarBarToken;
            case Kind.CaretToken:
            case Kind.CaretEqualsToken:
                return Kind.ExclamationEqualsEqualsToken;
            case Kind.AmpersandToken:
            case Kind.AmpersandEqualsToken:
                return Kind.AmpersandAmpersandToken;
        }

        return Kind.Unknown;
    }

    /// <summary>Go: <c>func (c *Checker) checkArithmeticOperandType(operand *ast.Node, t *Type, diagnostic *diagnostics.Message, isAwaitValid bool) bool</c>。</summary>
    internal bool CheckArithmeticOperandType(Node operand, Type t, Message diagnostic, bool isAwaitValid)
    {
        if (!IsTypeAssignableToWorker(t, _numberOrBigIntType!))
        {
            Type? awaitedType = null;
            if (isAwaitValid)
            {
                awaitedType = GetAwaitedTypeOfPromise(t);
            }

            ErrorAndMaybeSuggestAwait(operand, awaitedType is not null && IsTypeAssignableToWorker(awaitedType, _numberOrBigIntType!), diagnostic);
            return false;
        }

        return true;
    }

    // Return true if there was no error, false if there was an error.
    /// <summary>Go: <c>func (c *Checker) checkForDisallowedESSymbolOperand(left, right *ast.Node, leftType, rightType *Type, operator ast.Kind) bool</c>。</summary>
    internal bool CheckForDisallowedESSymbolOperand(Node left, Node right, Type leftType, Type rightType, Kind @operator)
    {
        Node? offendingSymbolOperand = null;
        if (MaybeTypeOfKindConsideringBaseConstraint(leftType, TypeFlags.ESSymbolLike))
        {
            offendingSymbolOperand = left;
        }
        else if (MaybeTypeOfKindConsideringBaseConstraint(rightType, TypeFlags.ESSymbolLike))
        {
            offendingSymbolOperand = right;
        }

        if (offendingSymbolOperand is not null)
        {
            Error(offendingSymbolOperand, The_0_operator_cannot_be_applied_to_type_symbol, TokenToString(@operator));
            return false;
        }

        return true;
    }

    /// <summary>Go: <c>func (c *Checker) checkNaNEquality(errorNode *ast.Node, operator ast.Kind, left, right *ast.Expression)</c>。</summary>
    internal void CheckNaNEquality(Node? errorNode, Kind @operator, Node left, Node right)
    {
        var isLeftNaN = IsGlobalNaN(SkipParentheses(left));
        var isRightNaN = IsGlobalNaN(SkipParentheses(right));
        if (isLeftNaN || isRightNaN)
        {
            var err = Error(errorNode, This_condition_will_always_return_0, TokenToString(IfElse(@operator == Kind.EqualsEqualsEqualsToken || @operator == Kind.EqualsEqualsToken, Kind.FalseKeyword, Kind.TrueKeyword)));
            if (isLeftNaN && isRightNaN)
            {
                return;
            }

            var operatorString = "";
            if (@operator == Kind.ExclamationEqualsEqualsToken || @operator == Kind.ExclamationEqualsToken)
            {
                operatorString = TokenToString(Kind.ExclamationToken);
            }

            var location = left;
            if (isLeftNaN)
            {
                location = right;
            }

            var expression = SkipParentheses(location)!;
            var entityName = "...";
            if (IsEntityNameExpression(expression))
            {
                entityName = EntityNameToString(expression);
            }

            var suggestion = operatorString + "Number.isNaN(" + entityName + ")";
            err.AddRelatedInfo(CreateDiagnosticForNode(location, Did_you_mean_0, suggestion));
        }
    }

    /// <summary>Go: <c>func (c *Checker) isGlobalNaN(expr *ast.Expression) bool</c>。</summary>
    internal bool IsGlobalNaN(Node? expr)
    {
        if (expr is not null && IsIdentifier(expr) && expr.Text() == "NaN")
        {
            var globalNaNSymbol = _getGlobalNaNSymbolOrNil();
            return globalNaNSymbol is not null && globalNaNSymbol == GetResolvedSymbolWorker(expr);
        }

        return false;
    }

    /// <summary>Go: <c>func (c *Checker) isTypeEqualityComparableTo(source, target *Type) bool</c>。</summary>
    internal bool IsTypeEqualityComparableTo(Type source, Type target)
    {
        return (target._flags & TypeFlags.Nullable) != 0 || IsTypeComparableTo(source, target);
    }

    /// <summary>Go: <c>func (c *Checker) checkTruthinessOfType(t *Type, node *ast.Node) *Type</c>。</summary>
    internal Type CheckTruthinessOfType(Type t, Node node)
    {
        if ((t._flags & TypeFlags.Void) != 0)
        {
            Error(node, An_expression_of_type_void_cannot_be_tested_for_truthiness);
            return t;
        }

        var semantics = GetSyntacticTruthySemantics(node);
        if (semantics != PredicateSemantics.Sometimes)
        {
            Error(node, IfElse(
                semantics == PredicateSemantics.Always,
                This_kind_of_expression_is_always_truthy,
                This_kind_of_expression_is_always_falsy));
        }

        return t;
    }
}
