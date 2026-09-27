// ported from: internal/checker/checker.go:4058-4318（语句/控制流检查：checkForOfStatement、checkBreakOrContinueStatement、
//   checkReturnStatement、checkReturnExpression、checkWithStatement、checkSwitchStatement、checkLabeledStatement、
//   checkThrowStatement、checkTryStatement、checkCatchClause、checkBindingElement、checkClassDeclaration）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `for name := range m`（map 键遍历）→ `foreach (var name in m.Keys)`；`m[k]` 读缺失键 → `TryGetValue`
//    （Go 的 map 读返回零值 nil，`Dictionary` 的索引器抛 KeyNotFoundException，docs/architecture.md §3.2）。
// 2) 节点数据字段在 C# 是生成类的 **public 字段**（`data.AwaitModifier`、`caseBlock.Clauses`），访问写法与 Go 相同。
// 3) `node.Members()`/`NodeList.Nodes` 是可空数组（Go 的 nil slice）→ 传给 `core.Some`/`core.Filter`（它们收
//    `T[]?`，与 Go 语义一致）；需要 `foreach` 时用 `!`（AST 不变量）或 `?? []`。
// 4) `panic`/`Debug.Fail` 规则见 docs/architecture.md §3.6；本文件不出现。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal void CheckForOfStatement(Node node)
    {
        var data = node.AsForInOrOfStatement()!;
        CheckGrammarForInOrForOfStatement(data);
        var container = GetContainingFunctionOrClassStaticBlock(node);
        if (data.AwaitModifier != null)
        {
            if (container != null && IsClassStaticBlockDeclaration(container))
            {
                GrammarErrorOnNode(data.AwaitModifier, Diagnostics.X_for_await_loops_cannot_be_used_inside_a_class_static_block);
            }
            else
            {
                var functionFlags = GetFunctionFlags(container);
                if ((functionFlags & (FunctionFlags.Invalid | FunctionFlags.Async)) == FunctionFlags.Async && _languageVersion < LanguageFeatureMinimumTarget.ForAwaitOf)
                {
                    // for..await..of in an async function or async generator function prior to ESNext requires the __asyncValues helper
                    CheckExternalEmitHelpers(node, ExternalEmitHelpers.ForAwaitOfIncludes);
                }
            }
        }

        // Check the LHS and RHS
        // If the LHS is a declaration, just check it as a variable declaration, which will in turn check the RHS
        // via checkRightHandSideOfForOf.
        // If the LHS is an expression, check the LHS, as a destructuring assignment or as a reference.
        // Then check that the RHS is assignable to it.
        if (IsVariableDeclarationList(data.Initializer!))
        {
            CheckVariableDeclarationList(data.Initializer!);
        }
        else
        {
            var varExpr = data.Initializer!;
            var iteratedType = CheckRightHandSideOfForOf(node);

            // There may be a destructuring assignment on the left side
            if (IsArrayLiteralExpression(varExpr) || IsObjectLiteralExpression(varExpr))
            {
                // iteratedType may be undefined. In this case, we still want to check the structure of
                // varExpr, in particular making sure it's a valid LeftHandSideExpression. But we'd like
                // to short circuit the type relation checking as much as possible, so we pass the unknownType.
                CheckDestructuringAssignment(varExpr, OrElse(iteratedType, _errorType), CheckMode.Normal, false);
            }
            else
            {
                var leftType = CheckExpression(varExpr);
                CheckReferenceExpression(varExpr, Diagnostics.The_left_hand_side_of_a_for_of_statement_must_be_a_variable_or_a_property_access, Diagnostics.The_left_hand_side_of_a_for_of_statement_may_not_be_an_optional_property_access);

                // iteratedType will be undefined if the rightType was missing properties/signatures
                // required to get its iteratedType (like [Symbol.iterator] or next). This may be
                // because we accessed properties from anyType, or it may have led to an error inside
                // getElementTypeOfIterable.
                if (iteratedType != null)
                {
                    CheckTypeAssignableToAndOptionallyElaborate(iteratedType, leftType, varExpr, data.Expression, null, null);
                }
            }
        }

        CheckSourceElement(data.Statement!);
        if (node.Locals() != null)
        {
            RegisterForUnusedIdentifiersCheck(node);
        }
    }

    internal void CheckBreakOrContinueStatement(Node node)
    {
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            CheckGrammarBreakOrContinueStatement(node);
        }
    }

    internal void CheckReturnStatement(Node node)
    {
        // Always check the return expression so its identifiers are resolved even when the
        // return statement is misplaced (grammar error), keeping diagnostics stable
        // regardless of traversal order.
        var exprNode = node.Expression();
        var exprType = _undefinedType;
        if (exprNode != null)
        {
            exprType = CheckExpressionCached(exprNode);
        }

        if (CheckGrammarStatementInAmbientContext(node))
        {
            return;
        }

        var container = GetContainingFunctionOrClassStaticBlock(node);
        if (container != null && IsClassStaticBlockDeclaration(container))
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_return_statement_cannot_be_used_inside_a_class_static_block);
            return;
        }

        if (container == null)
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_return_statement_can_only_be_used_within_a_function_body);
            return;
        }

        var signature = GetSignatureFromDeclarationWorker(container);
        var returnType = GetReturnTypeOfSignatureWorker(signature);
        var functionFlags = GetFunctionFlags(container);

        if (_strictNullChecks || exprNode != null || (returnType._flags & TypeFlags.Never) != 0)
        {
            if (IsSetAccessorDeclaration(container))
            {
                if (exprNode != null)
                {
                    Error(node, Diagnostics.Setters_cannot_return_a_value);
                }
            }
            else if (IsConstructorDeclaration(container))
            {
                if (exprNode != null && !CheckTypeAssignableToAndOptionallyElaborate(exprType, returnType, node, exprNode, null, null))
                {
                    Error(node, Diagnostics.Return_type_of_constructor_signature_must_be_assignable_to_the_instance_type_of_the_class);
                }
            }
            else if (GetReturnTypeFromAnnotation(container) != null)
            {
                var unwrappedReturnType = OrElse(UnwrapReturnType(returnType, functionFlags), returnType);
                CheckReturnExpression(container, unwrappedReturnType, node, node.Expression(), exprType, false);
            }
        }
        else if (!IsConstructorDeclaration(container) && _compilerOptions.NoImplicitReturns.IsTrue() && !IsUnwrappedReturnTypeUndefinedVoidOrAny(container, returnType))
        {
            // The function has a return type, but the return statement doesn't have an expression.
            Error(node, Diagnostics.Not_all_code_paths_return_a_value);
        }
    }

    // When checking an arrow expression such as `(x) => exp`, then `node` is the expression `exp`.
    // Otherwise, `node` is a return statement.
    internal void CheckReturnExpression(Node container, Type unwrappedReturnType, Node node, Node? expr, Type exprType, bool inConditionalExpression)
    {
        var unwrappedExprType = exprType;
        var functionFlags = GetFunctionFlags(container);
        if (expr != null)
        {
            var unwrappedExpr = SkipParentheses(expr)!;
            if (IsConditionalExpression(unwrappedExpr))
            {
                var whenTrue = unwrappedExpr.AsConditionalExpression()!.WhenTrue!;
                var whenFalse = unwrappedExpr.AsConditionalExpression()!.WhenFalse!;
                CheckReturnExpression(container, unwrappedReturnType, node, whenTrue, CheckExpression(whenTrue), true /*inConditionalExpression*/);
                CheckReturnExpression(container, unwrappedReturnType, node, whenFalse, CheckExpression(whenFalse), true /*inConditionalExpression*/);
                return;
            }
        }

        var inReturnStatement = node.Kind == Kind.ReturnStatement;
        if ((functionFlags & FunctionFlags.Async) != 0)
        {
            unwrappedExprType = CheckAwaitedType(exprType, false /*withAlias*/, node, Diagnostics.The_return_type_of_an_async_function_must_either_be_a_valid_promise_or_must_not_contain_a_callable_then_member);
        }

        var effectiveExpr = expr; // The effective expression for diagnostics purposes.
        if (expr != null)
        {
            effectiveExpr = GetEffectiveCheckNode(expr);
        }

        var errorNode = IfElse(inReturnStatement && !inConditionalExpression, node, effectiveExpr);
        CheckTypeAssignableToAndOptionallyElaborate(unwrappedExprType, unwrappedReturnType, errorNode, effectiveExpr, null, null);
    }

    internal void CheckWithStatement(Node node)
    {
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if ((node.Flags & NodeFlags.AwaitContext) != 0)
            {
                GrammarErrorOnFirstToken(node, Diagnostics.X_with_statements_are_not_allowed_in_an_async_function_block);
            }
        }

        CheckExpression(node.Expression()!);
        var sourceFile = GetSourceFileOfNode(node)!;
        if (!HasParseDiagnostics(sourceFile))
        {
            var start = SkipTrivia(sourceFile.Text(), node.Pos());
            var end = node.Statement()!.Pos();
            GrammarErrorAtPos(sourceFile.AsNode(), start, end - start, Diagnostics.The_with_statement_is_not_supported_All_symbols_in_a_with_block_will_have_type_any);
        }
    }

    internal void CheckSwitchStatement(Node node)
    {
        // Grammar checking
        CheckGrammarStatementInAmbientContext(node);

        Node? firstDefaultClause = null;
        var hasDuplicateDefaultClause = false;

        var expressionType = CheckExpression(node.Expression()!);
        var caseBlock = node.AsSwitchStatement()!.CaseBlock!;
        foreach (var clause in caseBlock.AsCaseBlock()!.Clauses!.Nodes ?? [])
        {
            // Grammar check for duplicate default clauses, skip if we already report duplicate default clause
            if (IsDefaultClause(clause) && !hasDuplicateDefaultClause)
            {
                if (firstDefaultClause == null)
                {
                    firstDefaultClause = clause;
                }
                else
                {
                    GrammarErrorOnNode(clause, Diagnostics.A_default_clause_cannot_appear_more_than_once_in_a_switch_statement);
                    hasDuplicateDefaultClause = true;
                }
            }

            if (IsCaseClause(clause))
            {
                var caseType = CheckExpression(clause.Expression()!);
                if (!IsTypeEqualityComparableTo(expressionType, caseType))
                {
                    // expressionType is not comparable to caseType, try the reversed check and report errors if it fails
                    CheckTypeComparableTo(caseType, expressionType, clause.Expression(), null /*headMessage*/);
                }
            }

            CheckSourceElements(clause.Statements() ?? []);

            if (_compilerOptions.NoFallthroughCasesInSwitch.IsTrue())
            {
                var flowNode = clause.AsCaseOrDefaultClause()!.FallthroughFlowNode;
                if (flowNode != null && IsReachableFlowNode(flowNode))
                {
                    Error(clause, Diagnostics.Fallthrough_case_in_switch);
                }
            }
        }

        if (caseBlock.Locals() != null)
        {
            RegisterForUnusedIdentifiersCheck(caseBlock);
        }
    }

    internal void CheckLabeledStatement(Node node)
    {
        var labeledStatement = node.AsLabeledStatement()!;
        var labelNode = labeledStatement.Label!;
        var labelText = labelNode.Text();
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            for (var current = node.Parent; current != null && !IsFunctionLike(current); current = current.Parent)
            {
                if (IsLabeledStatement(current) && current.Label()!.Text() == labelText)
                {
                    GrammarErrorOnNode(labelNode, Diagnostics.Duplicate_label_0, labelText);
                    break;
                }
            }
        }

        if ((labelNode.Flags & NodeFlags.Unreachable) != 0 && _compilerOptions.AllowUnusedLabels != Tristate.TSTrue)
        {
            ErrorOrSuggestion(_compilerOptions.AllowUnusedLabels == Tristate.TSFalse, labelNode, Diagnostics.Unused_label);
        }

        CheckSourceElement(labeledStatement.Statement!);
    }

    internal void CheckThrowStatement(Node node)
    {
        var throwExpr = node.Expression()!;
        if (!CheckGrammarStatementInAmbientContext(node))
        {
            if (IsIdentifier(throwExpr) && throwExpr.Text().Length == 0)
            {
                GrammarErrorAtPos(node, throwExpr.Pos(), 0 /*length*/, Diagnostics.Line_break_not_permitted_here);
            }
        }

        CheckExpression(throwExpr);
    }

    internal void CheckTryStatement(Node node)
    {
        CheckGrammarStatementInAmbientContext(node);
        var data = node.AsTryStatement()!;
        CheckBlock(data.TryBlock!);
        if (data.CatchClause != null)
        {
            CheckCatchClause(data.CatchClause);
        }

        if (data.FinallyBlock != null)
        {
            CheckBlock(data.FinallyBlock);
        }
    }

    internal void CheckCatchClause(Node node)
    {
        var declaration = node.AsCatchClause()!.VariableDeclaration;
        if (declaration != null)
        {
            CheckVariableLikeDeclaration(declaration);
            var typeNode = declaration.Type();
            if (typeNode != null)
            {
                var t = GetTypeFromTypeNodeWorker(typeNode);
                if (t != null && (t._flags & TypeFlags.AnyOrUnknown) == 0)
                {
                    GrammarErrorOnFirstToken(typeNode, Diagnostics.Catch_clause_variable_type_annotation_must_be_any_or_unknown_if_specified);
                }
            }
            else if (declaration.Initializer() != null)
            {
                GrammarErrorOnFirstToken(declaration.Initializer()!, Diagnostics.Catch_clause_variable_cannot_have_an_initializer);
            }
            else
            {
                var blockLocals = node.AsCatchClause()!.Block!.Locals();
                if (blockLocals != null)
                {
                    foreach (var caughtName in node.Locals()!.Keys)
                    {
                        if (blockLocals.TryGetValue(caughtName, out var blockLocal) && blockLocal != null && blockLocal.ValueDeclaration != null && (blockLocal.Flags & SymbolFlags.BlockScopedVariable) != 0)
                        {
                            GrammarErrorOnNode(blockLocal.ValueDeclaration, Diagnostics.Cannot_redeclare_identifier_0_in_catch_clause, caughtName);
                        }
                    }
                }
            }
        }

        CheckBlock(node.AsCatchClause()!.Block!);
    }

    internal void CheckBindingElement(Node node)
    {
        CheckGrammarBindingElement(node.AsBindingElement()!);
        CheckVariableLikeDeclaration(node);
    }

    internal void CheckClassDeclaration(Node node)
    {
        var firstDecorator = Find(node.ModifierNodes(), d => IsDecorator(d));
        if (_legacyDecorators && firstDecorator != null && Some(node.Members(), p => HasStaticModifier(p) && IsPrivateIdentifierClassElementDeclaration(p)))
        {
            GrammarErrorOnNode(firstDecorator, Diagnostics.Class_decorators_can_t_be_used_with_static_private_identifier_Consider_removing_the_experimental_decorator);
        }

        if (node.Name() == null && !HasSyntacticModifier(node, ModifierFlags.Default))
        {
            GrammarErrorOnFirstToken(node, Diagnostics.A_class_declaration_without_the_default_modifier_must_have_a_name);
        }

        CheckClassLikeDeclaration(node);
        CheckSourceElements(node.Members() ?? []);
        RegisterForUnusedIdentifiersCheck(node);
    }
}
