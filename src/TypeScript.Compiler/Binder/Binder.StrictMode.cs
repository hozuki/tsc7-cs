// ported from: internal/binder/binder.go:1306-1475（严格模式检查：checkContextualIdentifier / checkPrivateIdentifier /
//   getStrictModeIdentifierMessage / isUseStrictPrologueDirective / FindUseStrictPrologue / checkStrictModeFunctionName /
//   getStrictModeBlockScopeFunctionDeclarationMessage / checkStrictModeBinaryExpression / checkStrictModeCatchClause /
//   checkStrictModeDeleteExpression / checkStrictModePostfixUnaryExpression / checkStrictModePrefixUnaryExpression /
//   checkStrictModeWithStatement / checkStrictModeLabeledStatement / isEvalOrArgumentsIdentifier /
//   checkStrictModeEvalOrArguments / getStrictModeEvalOrArgumentsMessage）
//
// 与 Go 的偏离（C# 语言特性，逐条依据 docs/architecture.md §2/§3 与 docs/ast.md；其余与 Go 逐行对应）：
// 1) `diagnostics.`/`ast.`/`scanner.` 的包级函数按裸名调用（`using static`）。本文件在 `TypeScript.Binder` 下，
//    裸写 `Ast.Xxx`/`Scanner.Xxx` 会先解析到外层命名空间 `TypeScript.Ast`/`TypeScript.Scanner`
//    （docs/architecture.md §2 的"外层命名空间先被找到"规则；Scanner/Utilities.cs 的文件头有同样说明）。
// 2) Go 的 `*diagnostics.Message` → `Message`（三条 `getStrictMode*Message` 的所有返回路径都非 nil，故不加 `?`）。
// 3) `node.Flags&ast.NodeFlagsXxx == 0` → `(node.Flags & NodeFlags.Xxx) == 0`：C# 的 `&` 优先级低于 `==`，必须加括号。
// 4) Go 里可能为 nil 的节点指针 → `Node?`；Go 在调用点直接解引用的地方（`node.Expression()`、
//    `clause.VariableDeclaration`、`expr.Expression`）按 Go 的既有不变量用 `!`（docs/architecture.md §3.5：
//    `!` 只用在已证明非空处；这里的依据是 kind 判定 + parser 保证，与 Parser 分片的写法一致）。
// 5) 包级未导出函数 `isUseStrictPrologueDirective`/`isEvalOrArgumentsIdentifier` → `private static`
//    `IsUseStrictPrologueDirective`/`IsEvalOrArgumentsIdentifier`（契约 §1.2 的命名规则；注意 Go 的函数名带
//    `Directive` 后缀，契约里举例写作 `isUseStrictPrologue`，同一个函数）；导出的 `FindUseStrictPrologue` →
//    `public static`，签名照契约 §1.2（`SourceFile` + `Node[]`）。
// 6) `(b *Binder)` 的未导出方法 → `private` 实例方法 + PascalCase（`checkContextualIdentifier` →
//    `CheckContextualIdentifier`）；分片外的调用点（W2 的 bindChildren 等）按同样的名字调用。
// 7) `getStrictModeBlockScopeFunctionDeclarationMessage` 在 Go 里是**死代码**（binder.go 无调用点；TS 侧的
//    `checkStrictModeFunctionDeclaration` 没有移植过来，见 tsc/testdata/fixtures/compiler/binder.ts:2681/2706）。
//    本文件照样 1:1 保留（先例：Binder.cs 的 `isSignedNumericLiteral` 偏差说明第 10 条）。
namespace TypeScript.Binder;

using TypeScript.Ast;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

public sealed partial class Binder
{
    // The binder visits every node in the syntax tree so it is a convenient place to perform a single localized
    // check for reserved words used as identifiers in strict mode code, as well as `yield` or `await` in
    // [Yield] or [Await] contexts, respectively.
    private void CheckContextualIdentifier(Node node)
    {
        // Report error only if there are no parse errors in file
        if (_file.Diagnostics().Length == 0 && (node.Flags & NodeFlags.Ambient) == 0 && (node.Flags & NodeFlags.JSDoc) == 0 && !IsIdentifierName(node))
        {
            // strict mode identifiers
            var originalKeywordKind = GetIdentifierToken(node.Text());
            if (originalKeywordKind == Kind.Identifier)
            {
                return;
            }
            if (originalKeywordKind >= Kind.FirstFutureReservedWord && originalKeywordKind <= Kind.LastFutureReservedWord)
            {
                ErrorOnNode(node, GetStrictModeIdentifierMessage(node), DeclarationNameToString(node));
            }
            else if (originalKeywordKind == Kind.AwaitKeyword)
            {
                if (IsExternalModule(_file) && IsInTopLevelContext(node))
                {
                    ErrorOnNode(node, Identifier_expected_0_is_a_reserved_word_at_the_top_level_of_a_module, DeclarationNameToString(node));
                }
                else if ((node.Flags & NodeFlags.AwaitContext) != 0)
                {
                    ErrorOnNode(node, Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, DeclarationNameToString(node));
                }
            }
            else if (originalKeywordKind == Kind.YieldKeyword && (node.Flags & NodeFlags.YieldContext) != 0)
            {
                ErrorOnNode(node, Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, DeclarationNameToString(node));
            }
        }
    }

    private void CheckPrivateIdentifier(Node node)
    {
        if (node.Text() == "#constructor")
        {
            // Report error only if there are no parse errors in file
            if (_file.Diagnostics().Length == 0)
            {
                ErrorOnNode(node, X_constructor_is_a_reserved_word, DeclarationNameToString(node));
            }
        }
    }

    private Message GetStrictModeIdentifierMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in
        // strict mode.
        if (GetContainingClass(node) is not null)
        {
            return Identifier_expected_0_is_a_reserved_word_in_strict_mode_Class_definitions_are_automatically_in_strict_mode;
        }
        if (_file.ExternalModuleIndicator is not null)
        {
            return Identifier_expected_0_is_a_reserved_word_in_strict_mode_Modules_are_automatically_in_strict_mode;
        }
        return Identifier_expected_0_is_a_reserved_word_in_strict_mode;
    }

    // Should be called only on prologue directives (IsPrologueDirective(node) should be true)
    private static bool IsUseStrictPrologueDirective(SourceFile sourceFile, Node node)
    {
        var nodeText = GetSourceTextOfNodeFromSourceFile(sourceFile, node.Expression()!, false /*includeTrivia*/);
        // Note: the node text must be exactly "use strict" or 'use strict'.  It is not ok for the
        // string to contain unicode escapes (as per ES5).
        return nodeText == "\"use strict\"" || nodeText == "'use strict'";
    }

    public static Node? FindUseStrictPrologue(SourceFile sourceFile, Node[] statements)
    {
        for (var i = 0; i < statements.Length; i++)
        {
            var statement = statements[i];
            if (IsPrologueDirective(statement))
            {
                if (IsUseStrictPrologueDirective(sourceFile, statement))
                {
                    return statement;
                }
            }
            else
            {
                return null;
            }
        }

        return null;
    }

    private void CheckStrictModeFunctionName(Node node)
    {
        if ((node.Flags & NodeFlags.Ambient) == 0)
        {
            // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a strict mode FunctionDeclaration or FunctionExpression (13.1))
            CheckStrictModeEvalOrArguments(node, node.Name());
        }
    }

    private Message GetStrictModeBlockScopeFunctionDeclarationMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in strict mode.
        if (GetContainingClass(node) is not null)
        {
            return Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5_Class_definitions_are_automatically_in_strict_mode;
        }
        if (_file.ExternalModuleIndicator is not null)
        {
            return Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5_Modules_are_automatically_in_strict_mode;
        }
        return Function_declarations_are_not_allowed_inside_blocks_in_strict_mode_when_targeting_ES5;
    }

    private void CheckStrictModeBinaryExpression(Node node)
    {
        var expr = node.AsBinaryExpression()!;
        if (IsLeftHandSideExpression(expr.Left) && IsAssignmentOperator(expr.OperatorToken!.Kind))
        {
            // ECMA 262 (Annex C) The identifier eval or arguments may not appear as the LeftHandSideExpression of an
            // Assignment operator(11.13) or of a PostfixExpression(11.3)
            CheckStrictModeEvalOrArguments(node, expr.Left);
        }
    }

    private void CheckStrictModeCatchClause(Node node)
    {
        // It is a SyntaxError if a TryStatement with a Catch occurs within strict code and the Identifier of the
        // Catch production is eval or arguments
        var clause = node.AsCatchClause()!;
        if (clause.VariableDeclaration is not null)
        {
            CheckStrictModeEvalOrArguments(node, clause.VariableDeclaration.AsVariableDeclaration()!.Name());
        }
    }

    private void CheckStrictModeDeleteExpression(Node node)
    {
        // Grammar checking
        var expr = node.AsDeleteExpression()!;
        if (expr.Expression!.Kind == Kind.Identifier)
        {
            // When a delete operator occurs within strict mode code, a SyntaxError is thrown if its
            // UnaryExpression is a direct reference to a variable, function argument, or function name
            ErrorOnNode(expr.Expression!, X_delete_cannot_be_called_on_an_identifier_in_strict_mode);
        }
    }

    private void CheckStrictModePostfixUnaryExpression(Node node)
    {
        // Grammar checking
        // The identifier eval or arguments may not appear as the LeftHandSideExpression of an
        // Assignment operator(11.13) or of a PostfixExpression(11.3) or as the UnaryExpression
        // operated upon by a Prefix Increment(11.4.4) or a Prefix Decrement(11.4.5) operator.
        CheckStrictModeEvalOrArguments(node, node.AsPostfixUnaryExpression()!.Operand);
    }

    private void CheckStrictModePrefixUnaryExpression(Node node)
    {
        // Grammar checking
        var expr = node.AsPrefixUnaryExpression()!;
        if (expr.Operator == Kind.PlusPlusToken || expr.Operator == Kind.MinusMinusToken)
        {
            CheckStrictModeEvalOrArguments(node, expr.Operand);
        }
    }

    private void CheckStrictModeWithStatement(Node node)
    {
        // Grammar checking for withStatement
        ErrorOnFirstToken(node, X_with_statements_are_not_allowed_in_strict_mode);
    }

    private void CheckStrictModeLabeledStatement(Node node)
    {
        // Grammar checking for labeledStatement
        var data = node.AsLabeledStatement()!;
        if (IsDeclarationStatement(data.Statement) || IsVariableStatement(data.Statement!))
        {
            ErrorOnFirstToken(data.Label!, A_label_is_not_allowed_here);
        }
    }

    private static bool IsEvalOrArgumentsIdentifier(Node node)
    {
        if (IsIdentifier(node))
        {
            var text = node.Text();
            return text == "eval" || text == "arguments";
        }
        return false;
    }

    private void CheckStrictModeEvalOrArguments(Node contextNode, Node? name)
    {
        if (name is not null && IsEvalOrArgumentsIdentifier(name))
        {
            // We check first if the name is inside class declaration or class expression; if so give explicit message
            // otherwise report generic error message.
            ErrorOnNode(name, GetStrictModeEvalOrArgumentsMessage(contextNode), name.Text());
        }
    }

    private Message GetStrictModeEvalOrArgumentsMessage(Node node)
    {
        // Provide specialized messages to help the user understand why we think they're in strict mode
        if (GetContainingClass(node) is not null)
        {
            return Code_contained_in_a_class_is_evaluated_in_JavaScript_s_strict_mode_which_does_not_allow_this_use_of_0_For_more_information_see_https_Colon_Slash_Slashdeveloper_mozilla_org_Slashen_US_Slashdocs_SlashWeb_SlashJavaScript_SlashReference_SlashStrict_mode;
        }
        if (_file.ExternalModuleIndicator is not null)
        {
            return Invalid_use_of_0_Modules_are_automatically_in_strict_mode;
        }
        return Invalid_use_of_0_in_strict_mode;
    }
}
