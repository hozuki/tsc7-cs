// ported from: internal/checker/checker.go:13082-13348
//   （PredicateSemantics（type + const） / getSyntacticTruthySemantics / checkNullishCoalesceOperands /
//    checkNullishCoalesceOperandLeft / getSyntacticNullishnessSemantics / isSideEffectFree / isIndirectCall /
//    checkInstanceOfExpression / checkInExpression / hasEmptyObjectIntersection /
//    getExactOptionalUnassignableProperties / isExactOptionalPropertyMismatch / checkReferenceExpression）
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `type PredicateSemantics uint32` + const → `[Flags] public enum PredicateSemantics : uint`
//    （成员名去 Go 常量名的 `PredicateSemantics` 前缀、数值与 Go 一致；docs/architecture.md §3.3）。
//    它是 `[Flags]`，因为 Go 用 `|` 合并（`getSyntacticTruthySemantics` 的 ConditionalExpression 分支）。
// 2) `getExactOptionalUnassignableProperties` 返回 `[]*ast.Symbol`（可能 nil）→ `Symbol[]?`。
// 3) `core.Filter(c.getPropertiesOfType(target), func(targetProp *ast.Symbol) bool {...})` 的 lambda 捕获 `this`
//    与 `source`，只能写 lambda（不是方法组）。
// 4) `someType(t, func(t *Type) bool {...})` 的 lambda 捕获 `this` → lambda（`SomeType(t, t2 => ...)`）。
// 5) Go 的 `node = ast.SkipOuterExpressions(node, ast.OEKAll)`（返回可能为 nil）→ 每处都用 `!` 表达 Go 的
//    非 nil 不变量（Go 里随后的 `node.Kind` 会解引用）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

/// <summary>Go: <c>type PredicateSemantics uint32</c>（成员名去 Go 常量名的类型前缀，数值一致）。</summary>
[Flags]
public enum PredicateSemantics : uint
{
    /// <summary>Go: <c>PredicateSemanticsNone</c>。</summary>
    None = 0,

    /// <summary>Go: <c>PredicateSemanticsAlways</c>。</summary>
    Always = 1 << 0,

    /// <summary>Go: <c>PredicateSemanticsNever</c>。</summary>
    Never = 1 << 1,

    /// <summary>Go: <c>PredicateSemanticsSometimes</c>（= Always | Never）。</summary>
    Sometimes = Always | Never,
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getSyntacticTruthySemantics(node *ast.Node) PredicateSemantics</c>。</summary>
    internal PredicateSemantics GetSyntacticTruthySemantics(Node? node)
    {
        node = SkipOuterExpressions(node, OuterExpressionKinds.All)!;
        switch (node.Kind)
        {
            case Kind.NumericLiteral:
                // Allow `while(0)` or `while(1)`
                if (node.Text() == "0" || node.Text() == "1")
                {
                    return PredicateSemantics.Sometimes;
                }

                return PredicateSemantics.Always;
            case Kind.ArrayLiteralExpression:
            case Kind.ArrowFunction:
            case Kind.BigIntLiteral:
            case Kind.ClassExpression:
            case Kind.FunctionExpression:
            case Kind.JsxElement:
            case Kind.JsxSelfClosingElement:
            case Kind.ObjectLiteralExpression:
            case Kind.RegularExpressionLiteral:
                return PredicateSemantics.Always;
            case Kind.VoidExpression:
            case Kind.NullKeyword:
                return PredicateSemantics.Never;
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.StringLiteral:
                if (node.Text() != "")
                {
                    return PredicateSemantics.Always;
                }

                return PredicateSemantics.Never;
            case Kind.ConditionalExpression:
                return GetSyntacticTruthySemantics(node.AsConditionalExpression()!.WhenTrue)
                    | GetSyntacticTruthySemantics(node.AsConditionalExpression()!.WhenFalse);
            case Kind.Identifier:
                if (GetResolvedSymbolWorker(node) == _undefinedSymbol)
                {
                    return PredicateSemantics.Never;
                }

                break;
        }

        return PredicateSemantics.Sometimes;
    }

    /// <summary>Go: <c>func (c *Checker) checkNullishCoalesceOperands(left, right *ast.Node)</c>。</summary>
    internal void CheckNullishCoalesceOperands(Node left, Node right)
    {
        if (IsBinaryExpression(left.Parent!.Parent!))
        {
            var grandparentLeft = left.Parent!.Parent!.AsBinaryExpression()!.Left;
            var grandparentOperatorToken = left.Parent!.Parent!.AsBinaryExpression()!.OperatorToken!;
            if (IsBinaryExpression(grandparentLeft!) && grandparentOperatorToken.Kind == Kind.BarBarToken)
            {
                GrammarErrorOnNode(grandparentLeft, X_0_and_1_operations_cannot_be_mixed_without_parentheses, TokenToString(Kind.QuestionQuestionToken), TokenToString(grandparentOperatorToken.Kind));
            }
        }
        else if (IsBinaryExpression(left))
        {
            var operatorToken = left.AsBinaryExpression()!.OperatorToken!;
            if (operatorToken.Kind == Kind.BarBarToken || operatorToken.Kind == Kind.AmpersandAmpersandToken)
            {
                GrammarErrorOnNode(left, X_0_and_1_operations_cannot_be_mixed_without_parentheses, TokenToString(operatorToken.Kind), TokenToString(Kind.QuestionQuestionToken));
            }
        }
        else if (IsBinaryExpression(right))
        {
            var operatorToken = right.AsBinaryExpression()!.OperatorToken!;
            if (operatorToken.Kind == Kind.AmpersandAmpersandToken)
            {
                GrammarErrorOnNode(right, X_0_and_1_operations_cannot_be_mixed_without_parentheses, TokenToString(Kind.QuestionQuestionToken), TokenToString(operatorToken.Kind));
            }
        }

        CheckNullishCoalesceOperandLeft(left);
    }

    /// <summary>Go: <c>func (c *Checker) checkNullishCoalesceOperandLeft(left *ast.Node)</c>。</summary>
    internal void CheckNullishCoalesceOperandLeft(Node left)
    {
        var leftTarget = SkipOuterExpressions(left, OuterExpressionKinds.All)!;
        var nullishSemantics = GetSyntacticNullishnessSemantics(leftTarget);
        if (nullishSemantics != PredicateSemantics.Sometimes)
        {
            if (nullishSemantics == PredicateSemantics.Always)
            {
                Error(leftTarget, This_expression_is_always_nullish);
            }
            else
            {
                Error(leftTarget, Right_operand_of_is_unreachable_because_the_left_operand_is_never_nullish);
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) getSyntacticNullishnessSemantics(node *ast.Node) PredicateSemantics</c>。</summary>
    internal PredicateSemantics GetSyntacticNullishnessSemantics(Node? node)
    {
        node = SkipOuterExpressions(node, OuterExpressionKinds.All)!;
        switch (node.Kind)
        {
            case Kind.AwaitExpression:
            case Kind.CallExpression:
            case Kind.TaggedTemplateExpression:
            case Kind.ElementAccessExpression:
            case Kind.MetaProperty:
            case Kind.NewExpression:
            case Kind.PropertyAccessExpression:
            case Kind.YieldExpression:
            case Kind.ThisKeyword:
                return PredicateSemantics.Sometimes;
            case Kind.BinaryExpression:
                // List of operators that can produce null/undefined:
                // || ||= && &&= ?? ??=
                switch (node.AsBinaryExpression()!.OperatorToken!.Kind)
                {
                    case Kind.BarBarToken:
                    case Kind.BarBarEqualsToken:
                    case Kind.AmpersandAmpersandToken:
                    case Kind.AmpersandAmpersandEqualsToken:
                        return PredicateSemantics.Sometimes;
                    // For these operator kinds, the right operand is effectively controlling
                    case Kind.CommaToken:
                    case Kind.EqualsToken:
                        return GetSyntacticNullishnessSemantics(node.AsBinaryExpression()!.Right);
                    // For nullish coalescing: result is the left operand when left is non-null,
                    // or the right operand when left is null/undefined. The nullishness of the
                    // result combines both paths: the left's non-null path contributes Never,
                    // and when left can be null, the right's semantics are also included.
                    case Kind.QuestionQuestionToken:
                    case Kind.QuestionQuestionEqualsToken:
                        var leftSemantics = GetSyntacticNullishnessSemantics(node.AsBinaryExpression()!.Left);
                        // The non-null path (left is non-null): left branch taken, result has Never bit
                        var result = leftSemantics & PredicateSemantics.Never;
                        // The null path (left is null/undefined): right branch taken, result inherits right's semantics
                        if ((leftSemantics & PredicateSemantics.Always) != 0)
                        {
                            result |= GetSyntacticNullishnessSemantics(node.AsBinaryExpression()!.Right);
                        }

                        return result;
                }

                return PredicateSemantics.Never;
            case Kind.ConditionalExpression:
                return GetSyntacticNullishnessSemantics(node.AsConditionalExpression()!.WhenTrue)
                    | GetSyntacticNullishnessSemantics(node.AsConditionalExpression()!.WhenFalse);
            case Kind.NullKeyword:
                return PredicateSemantics.Always;
            case Kind.Identifier:
                if (GetResolvedSymbolWorker(node) == _undefinedSymbol)
                {
                    return PredicateSemantics.Always;
                }

                return PredicateSemantics.Sometimes;
        }

        return PredicateSemantics.Never;
    }

    /// <summary>
    /// This is a *shallow* check: An expression is side-effect-free if the
    /// evaluation of the expression *itself* cannot produce side effects.
    /// For example, x++ / 3 is side-effect free because the / operator
    /// does not have side effects.
    /// The intent is to "smell test" an expression for correctness in positions where
    /// its value is discarded (e.g. the left side of the comma operator).
    /// </summary>
    internal bool IsSideEffectFree(Node node)
    {
        node = SkipParentheses(node)!;
        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.StringLiteral:
            case Kind.RegularExpressionLiteral:
            case Kind.TaggedTemplateExpression:
            case Kind.TemplateExpression:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NullKeyword:
            case Kind.UndefinedKeyword:
            case Kind.FunctionExpression:
            case Kind.ClassExpression:
            case Kind.ArrowFunction:
            case Kind.ArrayLiteralExpression:
            case Kind.ObjectLiteralExpression:
            case Kind.TypeOfExpression:
            case Kind.NonNullExpression:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxElement:
                return true;
            case Kind.ConditionalExpression:
                return IsSideEffectFree(node.AsConditionalExpression()!.WhenTrue!)
                    && IsSideEffectFree(node.AsConditionalExpression()!.WhenFalse!);
            case Kind.BinaryExpression:
                if (IsAssignmentOperator(node.AsBinaryExpression()!.OperatorToken!.Kind))
                {
                    return false;
                }

                return IsSideEffectFree(node.AsBinaryExpression()!.Left!) && IsSideEffectFree(node.AsBinaryExpression()!.Right!);
            case Kind.PrefixUnaryExpression:
                // Unary operators ~, !, +, and - have no side effects.
                // The rest do.
                switch (node.AsPrefixUnaryExpression()!.Operator)
                {
                    case Kind.ExclamationToken:
                    case Kind.PlusToken:
                    case Kind.MinusToken:
                    case Kind.TildeToken:
                        return true;
                }

                break;
        }

        return false;
    }

    // Return true for "indirect calls", (i.e. `(0, x.f)(...)` or `(0, eval)(...)`), which prevents passing `this`.
    /// <summary>Go: <c>func (c *Checker) isIndirectCall(node *ast.Node) bool</c>。</summary>
    internal bool IsIndirectCall(Node node)
    {
        var left = node.AsBinaryExpression()!.Left;
        var right = node.AsBinaryExpression()!.Right;
        return IsParenthesizedExpression(node.Parent!) && IsNumericLiteral(left!) && left!.Text() == "0"
            && ((IsCallExpression(node.Parent!.Parent!) && node.Parent.Parent!.Expression() == node.Parent)
                || IsTaggedTemplateExpression(node.Parent!.Parent!))
            && (IsAccessExpression(right) || (IsIdentifier(right!) && right!.Text() == "eval"));
    }

    /// <summary>Go: <c>func (c *Checker) checkInstanceOfExpression(left, right *ast.Expression, leftType, rightType *Type, checkMode CheckMode) *Type</c>。</summary>
    internal Type CheckInstanceOfExpression(Node left, Node right, Type leftType, Type rightType, CheckMode checkMode)
    {
        if (leftType == _silentNeverType || rightType == _silentNeverType)
        {
            return _silentNeverType!;
        }

        // TypeScript 1.0 spec (April 2014): 4.15.4
        // The instanceof operator requires the left operand to be of type Any, an object type, or a type parameter type,
        // and the right operand to be of type Any, a subtype of the 'Function' interface type, or have a call or construct signature.
        // The result is always of the Boolean primitive type.
        // NOTE: do not raise error if leftType is unknown as related error was already reported
        if (!IsTypeAny(leftType) && AllTypesAssignableToKind(leftType, TypeFlags.Primitive))
        {
            Error(left, The_left_hand_side_of_an_instanceof_expression_must_be_of_type_any_an_object_type_or_a_type_parameter);
        }

        var signature = GetResolvedSignatureWorker(left.Parent!, null /*candidatesOutArray*/, checkMode);
        if (signature == _resolvingSignature)
        {
            // CheckMode.SkipGenericFunctions is enabled and this is a call to a generic function that
            // returns a function type. We defer checking and return silentNeverType.
            return _silentNeverType!;
        }

        // If rightType has a `[Symbol.hasInstance]` method that is not `(value: unknown) => boolean`, we
        // must check the expression as if it were a call to `right[Symbol.hasInstance](left)`. The call to
        // `getResolvedSignature`, below, will check that leftType is assignable to the type of the first
        // parameter.
        var returnType = GetReturnTypeOfSignatureWorker(signature);

        // We also verify that the return type of the `[Symbol.hasInstance]` method is assignable to
        // `boolean`. According to the spec, the runtime will actually perform `ToBoolean` on the result,
        // but this is more type-safe.
        CheckTypeAssignableTo(returnType, _booleanType!, right, An_object_s_Symbol_hasInstance_method_must_return_a_boolean_value_for_it_to_be_used_on_the_right_hand_side_of_an_instanceof_expression);
        return _booleanType!;
    }

    /// <summary>Go: <c>func (c *Checker) checkInExpression(left, right *ast.Expression, leftType, rightType *Type) *Type</c>。</summary>
    internal Type CheckInExpression(Node left, Node right, Type leftType, Type rightType)
    {
        if (leftType == _silentNeverType || rightType == _silentNeverType)
        {
            return _silentNeverType!;
        }

        if (IsPrivateIdentifier(left))
        {
            if (_languageVersion < LanguageFeatureMinimumTarget.PrivateNamesAndClassStaticBlocks
                || _languageVersion < LanguageFeatureMinimumTarget.ClassAndClassElementDecorators
                || !_compilerOptions.GetUseDefineForClassFields())
            {
                CheckExternalEmitHelpers(left, ExternalEmitHelpers.ClassPrivateFieldIn);
            }

            // Unlike in 'checkPrivateIdentifierExpression' we now have access to the RHS type
            // which provides us with the opportunity to emit more detailed errors
            if (_symbolNodeLinks.Get(left)._resolvedSymbol is null && GetContainingClass(left) is not null)
            {
                var isUncheckedJS = IsUncheckedJSSuggestion(left, rightType._symbol, true /*excludeClasses*/);
                ReportNonexistentProperty(left, rightType, isUncheckedJS);
            }
        }
        else
        {
            // The type of the left operand must be assignable to string, number, or symbol.
            CheckTypeAssignableTo(CheckNonNullType(leftType, left), _stringNumberSymbolType!, left, null);
        }

        // The type of the right operand must be assignable to 'object'.
        if (CheckTypeAssignableTo(CheckNonNullType(rightType, right), _nonPrimitiveType!, right, null))
        {
            // The {} type is assignable to the object type, yet {} might represent a primitive type. Here we
            // detect and error on {} that results from narrowing the unknown type, as well as intersections
            // that include {} (we know that the other types in such intersections are assignable to object
            // since we already checked for that).
            if (HasEmptyObjectIntersection(rightType))
            {
                Error(right, Type_0_may_represent_a_primitive_value_which_is_not_permitted_as_the_right_operand_of_the_in_operator, TypeToString(rightType));
            }
        }

        // The result is always of the Boolean primitive type.
        return _booleanType!;
    }

    /// <summary>Go: <c>func (c *Checker) hasEmptyObjectIntersection(t *Type) bool</c>。</summary>
    internal bool HasEmptyObjectIntersection(Type t)
    {
        return SomeType(t, t2 => t2 == _unknownEmptyObjectType
            || ((t2._flags & TypeFlags.Intersection) != 0 && IsEmptyAnonymousObjectType(GetBaseConstraintOrType(t2))));
    }

    /// <summary>Go: <c>func (c *Checker) getExactOptionalUnassignableProperties(source, target *Type) []*ast.Symbol</c>。</summary>
    internal Symbol[]? GetExactOptionalUnassignableProperties(Type source, Type target)
    {
        if (IsTupleType(source) && IsTupleType(target))
        {
            return null;
        }

        return Filter(GetPropertiesOfTypeWorker(target), targetProp =>
            IsExactOptionalPropertyMismatch(GetTypeOfPropertyOfTypeWorker(source, targetProp.Name), GetTypeOfSymbolWorker(targetProp)));
    }

    /// <summary>Go: <c>func (c *Checker) isExactOptionalPropertyMismatch(source, target *Type) bool</c>。</summary>
    internal bool IsExactOptionalPropertyMismatch(Type? source, Type? target)
    {
        return source is not null && target is not null
            && MaybeTypeOfKind(source, TypeFlags.Undefined)
            && _containsMissingType(target);
    }

    /// <summary>Go: <c>func (c *Checker) checkReferenceExpression(expr *ast.Node, invalidReferenceMessage, invalidOptionalChainMessage *diagnostics.Message) bool</c>。</summary>
    internal bool CheckReferenceExpression(Node expr, Message invalidReferenceMessage, Message invalidOptionalChainMessage)
    {
        // References are combinations of identifiers, parentheses, and property accesses.
        var node = SkipOuterExpressions(expr, OuterExpressionKinds.Assertions | OuterExpressionKinds.Parentheses)!;
        if (node.Kind != Kind.Identifier && !IsAccessExpression(node))
        {
            Error(expr, invalidReferenceMessage);
            return false;
        }

        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            Error(expr, invalidOptionalChainMessage);
            return false;
        }

        return true;
    }
}
