// ported from: internal/parser/parser.go:1063-2141（语句与声明：parseStatement/parseDeclaration/控制流语句/变量声明/
//   绑定模式/类与接口声明骨架/类成员解析）
//
// 与 Go 的差异（其余逐行对应）：
// - 位置一律是 UTF-16 代码单元（docs/architecture.md §3.1）；Go 里 `*ast.Statement`/`*ast.Expression`/`*ast.TypeNode`/
//   `*ast.ForInitializer`/`*ast.HeritageClauseElement` 这类**别名**（`= Node`）在 C# 里就是 `Node`（docs/ast.md §2），
//   所以返回值/参数写 `Node`；Go 里可能为 nil 的指针写 `Node?`。
// - Go 的 `panic(...)` → `Debug.Fail(...)`（抛 DebugFailureException）；因为它没有 `[DoesNotReturn]`，其后补
//   `throw new UnreachableException()` 满足控制流（与 Core/CompilerOptions.cs 的既有写法一致）。
// - Go 的 `&^=` → `&= ~`；Go 里 `&` 的优先级高于 `==`/`&&`，C# 相反，`x&y == 0` 这类写法都显式加了括号。
// - `core.IfElse(a, x, y)` → 三元表达式，因此 `?:` 参与 `|` 运算时也补了括号（C# 的 `?:` 优先级低于 `|`）。
// - 非公开类型按 C# 命名习惯：Go 的 `jsdocScannerInfo` → `JsdocScannerInfo`（由 Parser.cs 声明），
//   其位标志成员 `jsdocScannerInfoHasJSDoc` → `JsdocScannerInfo.HasJSDoc`（docs/architecture.md §3.3）。
// - `ast.IsIdentifier(node)` 的两处调用写成全名 `TypeScript.Ast.Ast.IsIdentifier(...)`：本类有同名零参方法
//   `IsIdentifier()`，C# 简单名查找会先命中本类成员而不落到 `using static` 导入的 Ast 成员上。
// - Go 的方法表达式 `(*Parser).parseStatement` 在 C# 里没有对应物（实例方法组不能转成 `Func<Parser, T>`），
//   凡是把实例方法当回调传给 ParseList/ParseDelimitedList/DoInContext/LookAhead 的地方一律写 `p => p.M()`。
// - `ParseLeftHandSideExpressionOrHigher()` 在 W5 的签名是 `Node?`（Go 里实际不会返回 nil），
//   按 Go 的既有不变量在其调用点用 `!`。
namespace TypeScript.Parser;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

public sealed partial class Parser
{
    private Node ParseStatement()
    {
        switch (_token)
        {
            case Kind.SemicolonToken:
                return ParseEmptyStatement();
            case Kind.OpenBraceToken:
                return ParseBlock(false /*ignoreMissingOpenBrace*/, null);
            case Kind.VarKeyword:
                return ParseVariableStatement(NodePos(), JsdocScannerInfo, null /*modifiers*/);
            case Kind.LetKeyword:
                if (IsLetDeclaration())
                {
                    return ParseVariableStatement(NodePos(), JsdocScannerInfo, null /*modifiers*/);
                }
                break;
            case Kind.AwaitKeyword:
                if (IsAwaitUsingDeclaration())
                {
                    return ParseVariableStatement(NodePos(), JsdocScannerInfo, null /*modifiers*/);
                }
                break;
            case Kind.UsingKeyword:
                if (IsUsingDeclaration())
                {
                    return ParseVariableStatement(NodePos(), JsdocScannerInfo, null /*modifiers*/);
                }
                break;
            case Kind.FunctionKeyword:
                return ParseFunctionDeclaration(NodePos(), JsdocScannerInfo, null /*modifiers*/);
            case Kind.ClassKeyword:
                return ParseClassDeclaration(NodePos(), JsdocScannerInfo, null /*modifiers*/);
            case Kind.IfKeyword:
                return ParseIfStatement();
            case Kind.DoKeyword:
                return ParseDoStatement();
            case Kind.WhileKeyword:
                return ParseWhileStatement();
            case Kind.ForKeyword:
                return ParseForOrForInOrForOfStatement();
            case Kind.ContinueKeyword:
                return ParseContinueStatement();
            case Kind.BreakKeyword:
                return ParseBreakStatement();
            case Kind.ReturnKeyword:
                return ParseReturnStatement();
            case Kind.WithKeyword:
                return ParseWithStatement();
            case Kind.SwitchKeyword:
                return ParseSwitchStatement();
            case Kind.ThrowKeyword:
                return ParseThrowStatement();
            case Kind.TryKeyword:
            case Kind.CatchKeyword:
            case Kind.FinallyKeyword:
                return ParseTryStatement();
            case Kind.DebuggerKeyword:
                return ParseDebuggerStatement();
            case Kind.AtToken:
                return ParseDeclaration();
            case Kind.AsyncKeyword:
            case Kind.InterfaceKeyword:
            case Kind.TypeKeyword:
            case Kind.ModuleKeyword:
            case Kind.NamespaceKeyword:
            case Kind.DeclareKeyword:
            case Kind.ConstKeyword:
            case Kind.EnumKeyword:
            case Kind.ExportKeyword:
            case Kind.ImportKeyword:
            case Kind.PrivateKeyword:
            case Kind.ProtectedKeyword:
            case Kind.PublicKeyword:
            case Kind.AbstractKeyword:
            case Kind.AccessorKeyword:
            case Kind.StaticKeyword:
            case Kind.ReadonlyKeyword:
            case Kind.GlobalKeyword:
                if (IsStartOfDeclaration())
                {
                    return ParseDeclaration();
                }
                break;
        }
        return ParseExpressionOrLabeledStatement();
    }

    private Node ParseDeclaration()
    {
        // `parseListElement` attempted to get the reused node at this position,
        // but the ambient context flag was not yet set, so the node appeared
        // not reusable in that context.
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var modifiers = ParseModifiersEx(/*allowDecorators*/ true, false /*permitConstAsModifier*/, false /*stopOnStartOfClassStaticBlock*/);
        var isAmbient = modifiers is not null && Some(modifiers.Nodes, IsDeclareModifier);
        if (isAmbient)
        {
            // !!! incremental parsing
            // node := p.tryReuseAmbientDeclaration(pos)
            // if node {
            // 	return node
            // }
            foreach (var m in modifiers!.Nodes!)
            {
                m.Flags |= NodeFlags.Ambient;
            }
            var saveContextFlags = _contextFlags;
            SetContextFlags(NodeFlags.Ambient, true);
            var result = ParseDeclarationWorker(pos, jsdoc, modifiers);
            _contextFlags = saveContextFlags;
            return result;
        }
        else
        {
            return ParseDeclarationWorker(pos, jsdoc, modifiers);
        }
    }

    private Node ParseDeclarationWorker(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        switch (_token)
        {
            case Kind.VarKeyword:
            case Kind.LetKeyword:
            case Kind.ConstKeyword:
            case Kind.UsingKeyword:
                return ParseVariableStatement(pos, jsdoc, modifiers);
            case Kind.AwaitKeyword:
                if (IsAwaitUsingDeclaration())
                {
                    return ParseVariableStatement(pos, jsdoc, modifiers);
                }
                break;
            case Kind.FunctionKeyword:
                return ParseFunctionDeclaration(pos, jsdoc, modifiers);
            case Kind.ClassKeyword:
                return ParseClassDeclaration(pos, jsdoc, modifiers);
            case Kind.InterfaceKeyword:
                return ParseInterfaceDeclaration(pos, jsdoc, modifiers);
            case Kind.TypeKeyword:
                return ParseTypeAliasDeclaration(pos, jsdoc, modifiers);
            case Kind.EnumKeyword:
                return ParseEnumDeclaration(pos, jsdoc, modifiers);
            case Kind.GlobalKeyword:
            case Kind.ModuleKeyword:
            case Kind.NamespaceKeyword:
                return ParseModuleDeclaration(pos, jsdoc, modifiers);
            case Kind.ImportKeyword:
                return ParseImportDeclarationOrImportEqualsDeclaration(pos, jsdoc, modifiers);
            case Kind.ExportKeyword:
                NextToken();
                switch (_token)
                {
                    case Kind.DefaultKeyword:
                    case Kind.EqualsToken:
                        return ParseExportAssignment(pos, jsdoc, modifiers);
                    case Kind.AsKeyword:
                        return ParseNamespaceExportDeclaration(pos, jsdoc, modifiers);
                    default:
                        return ParseExportDeclaration(pos, jsdoc, modifiers);
                }
        }
        if (modifiers is not null)
        {
            // We reached this point because we encountered decorators and/or modifiers and assumed a declaration
            // would follow. For recovery and error reporting purposes, return an incomplete declaration.
            ParseErrorAt(NodePos(), NodePos(), Declaration_expected);
            return FinishNode(_factory.NewMissingDeclaration(modifiers), pos);
        }
        Debug.Fail("Unhandled case in parseDeclarationWorker");
        throw new System.Diagnostics.UnreachableException();
    }

    private static bool IsDeclareModifier(Node modifier) => modifier.Kind == Kind.DeclareKeyword;

    private bool IsLetDeclaration()
    {
        // In ES6 'let' always starts a lexical declaration if followed by an identifier or {
        // or [.
        return LookAhead(p => p.NextTokenIsBindingIdentifierOrStartOfDestructuring());
    }

    private bool NextTokenIsBindingIdentifierOrStartOfDestructuring()
    {
        NextToken();
        return IsBindingIdentifier() || _token == Kind.OpenBraceToken || _token == Kind.OpenBracketToken;
    }

    private Node ParseBlock(bool ignoreMissingOpenBrace, Message? diagnosticMessage)
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var openBracePosition = _scanner.TokenStart();
        var openBraceParsed = ParseExpectedWithDiagnostic(Kind.OpenBraceToken, diagnosticMessage, true /*shouldAdvance*/);
        var multiline = false;
        if (openBraceParsed || ignoreMissingOpenBrace)
        {
            multiline = HasPrecedingLineBreak();
            var statements = ParseList(ParsingContext.BlockStatements, p => p.ParseStatement());
            ParseExpectedMatchingBrackets(Kind.OpenBraceToken, Kind.CloseBraceToken, openBraceParsed, openBracePosition);
            var result = FinishNode(_factory.NewBlock(statements, multiline), pos);
            WithJSDoc(result, jsdoc);
            if (_token == Kind.EqualsToken)
            {
                ParseErrorAtCurrentToken(Declaration_or_statement_expected_This_follows_a_block_of_statements_so_if_you_intended_to_write_a_destructuring_assignment_you_might_need_to_wrap_the_whole_assignment_in_parentheses);
                NextToken();
            }
            return result;
        }
        var missingBlock = FinishNode(_factory.NewBlock(CreateMissingList(), multiline), pos);
        WithJSDoc(missingBlock, jsdoc);
        return missingBlock;
    }

    private Node ParseEmptyStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.SemicolonToken);
        var result = FinishNode(_factory.NewEmptyStatement(), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseIfStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.IfKeyword);
        var openParenPosition = _scanner.TokenStart();
        var openParenParsed = ParseExpected(Kind.OpenParenToken);
        var expression = ParseExpressionAllowIn();
        ParseExpectedMatchingBrackets(Kind.OpenParenToken, Kind.CloseParenToken, openParenParsed, openParenPosition);
        var thenStatement = ParseStatement();
        Node? elseStatement = null;
        if (ParseOptional(Kind.ElseKeyword))
        {
            elseStatement = ParseStatement();
        }
        var result = FinishNode(_factory.NewIfStatement(expression, thenStatement, elseStatement), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseDoStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.DoKeyword);
        var statement = ParseStatement();
        ParseExpected(Kind.WhileKeyword);
        var openParenPosition = _scanner.TokenStart();
        var openParenParsed = ParseExpected(Kind.OpenParenToken);
        var expression = ParseExpressionAllowIn();
        ParseExpectedMatchingBrackets(Kind.OpenParenToken, Kind.CloseParenToken, openParenParsed, openParenPosition);
        // From: https://mail.mozilla.org/pipermail/es-discuss/2011-August/016188.html
        // 157 min --- All allen at wirfs-brock.com CONF --- "do{;}while(false)false" prohibited in
        // spec but allowed in consensus reality. Approved -- this is the de-facto standard whereby
        //  do;while(0)x will have a semicolon inserted before x.
        ParseOptional(Kind.SemicolonToken);
        var result = FinishNode(_factory.NewDoStatement(statement, expression), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseWhileStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.WhileKeyword);
        var openParenPosition = _scanner.TokenStart();
        var openParenParsed = ParseExpected(Kind.OpenParenToken);
        var expression = ParseExpressionAllowIn();
        ParseExpectedMatchingBrackets(Kind.OpenParenToken, Kind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = ParseStatement();
        var result = FinishNode(_factory.NewWhileStatement(expression, statement), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseForOrForInOrForOfStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.ForKeyword);
        var awaitToken = ParseOptionalToken(Kind.AwaitKeyword);
        ParseExpected(Kind.OpenParenToken);
        Node? initializer = null;
        if (_token != Kind.SemicolonToken)
        {
            if (_token == Kind.VarKeyword || _token == Kind.LetKeyword || _token == Kind.ConstKeyword ||
                (_token == Kind.UsingKeyword && LookAhead(p => p.NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf())) ||
                // this one is meant to allow of
                (_token == Kind.AwaitKeyword && LookAhead(p => p.NextIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine())))
            {
                initializer = ParseVariableDeclarationList(true /*inForStatementInitializer*/);
            }
            else
            {
                initializer = DoInContext(NodeFlags.DisallowInContext, true, p => p.ParseExpression());
            }
        }
        Node result;
        if ((awaitToken is not null && ParseExpected(Kind.OfKeyword)) || (awaitToken is null && ParseOptional(Kind.OfKeyword)))
        {
            var expression = DoInContext(NodeFlags.DisallowInContext, false, p => p.ParseAssignmentExpressionOrHigher());
            ParseExpected(Kind.CloseParenToken);
            result = _factory.NewForInOrOfStatement(Kind.ForOfStatement, awaitToken, initializer, expression, ParseStatement());
        }
        else if (ParseOptional(Kind.InKeyword))
        {
            var expression = ParseExpressionAllowIn();
            ParseExpected(Kind.CloseParenToken);
            result = _factory.NewForInOrOfStatement(Kind.ForInStatement, null /*awaitToken*/, initializer, expression, ParseStatement());
        }
        else
        {
            ParseExpected(Kind.SemicolonToken);
            Node? condition = null;
            if (_token != Kind.SemicolonToken && _token != Kind.CloseParenToken)
            {
                condition = ParseExpressionAllowIn();
            }
            ParseExpected(Kind.SemicolonToken);
            Node? incrementor = null;
            if (_token != Kind.CloseParenToken)
            {
                incrementor = ParseExpressionAllowIn();
            }
            ParseExpected(Kind.CloseParenToken);
            result = _factory.NewForStatement(initializer, condition, incrementor, ParseStatement());
        }
        FinishNode(result, pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseBreakStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.BreakKeyword);
        var label = ParseIdentifierUnlessAtSemicolon();
        ParseSemicolon();
        var result = FinishNode(_factory.NewBreakStatement(label), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseContinueStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.ContinueKeyword);
        var label = ParseIdentifierUnlessAtSemicolon();
        ParseSemicolon();
        var result = FinishNode(_factory.NewContinueStatement(label), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node? ParseIdentifierUnlessAtSemicolon()
    {
        if (!CanParseSemicolon())
        {
            return ParseIdentifier();
        }
        return null;
    }

    private Node ParseReturnStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.ReturnKeyword);
        Node? expression = null;
        if (!CanParseSemicolon())
        {
            expression = ParseExpressionAllowIn();
        }
        ParseSemicolon();
        var result = FinishNode(_factory.NewReturnStatement(expression), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseWithStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.WithKeyword);
        var openParenPosition = _scanner.TokenStart();
        var openParenParsed = ParseExpected(Kind.OpenParenToken);
        var expression = ParseExpressionAllowIn();
        ParseExpectedMatchingBrackets(Kind.OpenParenToken, Kind.CloseParenToken, openParenParsed, openParenPosition);
        var statement = DoInContext(NodeFlags.InWithStatement, true, p => p.ParseStatement());
        var result = FinishNode(_factory.NewWithStatement(expression, statement), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseCaseClause()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.CaseKeyword);
        var expression = ParseExpressionAllowIn();
        ParseExpected(Kind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, p => p.ParseStatement());
        var result = FinishNode(_factory.NewCaseOrDefaultClause(Kind.CaseClause, expression, statements), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseDefaultClause()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.DefaultKeyword);
        ParseExpected(Kind.ColonToken);
        var statements = ParseList(ParsingContext.SwitchClauseStatements, p => p.ParseStatement());
        var result = FinishNode(_factory.NewCaseOrDefaultClause(Kind.DefaultClause, null /*expression*/, statements), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseCaseOrDefaultClause()
    {
        if (_token == Kind.CaseKeyword)
        {
            return ParseCaseClause();
        }
        return ParseDefaultClause();
    }

    private Node ParseCaseBlock()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.OpenBraceToken);
        var clauses = ParseList(ParsingContext.SwitchClauses, p => p.ParseCaseOrDefaultClause());
        ParseExpected(Kind.CloseBraceToken);
        var result = FinishNode(_factory.NewCaseBlock(clauses), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseSwitchStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.SwitchKeyword);
        ParseExpected(Kind.OpenParenToken);
        var expression = ParseExpressionAllowIn();
        ParseExpected(Kind.CloseParenToken);
        var caseBlock = ParseCaseBlock();
        var result = FinishNode(_factory.NewSwitchStatement(expression, caseBlock), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseThrowStatement()
    {
        // ThrowStatement[Yield] :
        //      throw [no LineTerminator here]Expression[In, ?Yield];
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.ThrowKeyword);
        // Because of automatic semicolon insertion, we need to report error if this
        // throw could be terminated with a semicolon.  Note: we can't call 'parseExpression'
        // directly as that might consume an expression on the following line.
        // Instead, we create a "missing" identifier, but don't report an error. The actual error
        // will be reported in the grammar walker.
        Node? expression;
        if (!HasPrecedingLineBreak())
        {
            expression = ParseExpressionAllowIn();
        }
        else
        {
            expression = CreateMissingIdentifier();
        }
        if (!TryParseSemicolon())
        {
            ParseErrorForMissingSemicolonAfter(expression);
        }
        var result = FinishNode(_factory.NewThrowStatement(expression), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    // TODO: Review for error recovery
    private Node ParseTryStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.TryKeyword);
        var tryBlock = ParseBlock(false /*ignoreMissingOpenBrace*/, null);
        Node? catchClause = null;
        if (_token == Kind.CatchKeyword)
        {
            catchClause = ParseCatchClause();
        }
        // If we don't have a catch clause, then we must have a finally clause.  Try to parse
        // one out no matter what.
        Node? finallyBlock = null;
        if (catchClause is null || _token == Kind.FinallyKeyword)
        {
            ParseExpectedWithDiagnostic(Kind.FinallyKeyword, X_catch_or_finally_expected, true /*shouldAdvance*/);
            finallyBlock = ParseBlock(false /*ignoreMissingOpenBrace*/, null);
        }
        var result = FinishNode(_factory.NewTryStatement(tryBlock, catchClause, finallyBlock), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseCatchClause()
    {
        var pos = NodePos();
        ParseExpected(Kind.CatchKeyword);
        Node? variableDeclaration = null;
        if (ParseOptional(Kind.OpenParenToken))
        {
            variableDeclaration = ParseVariableDeclaration();
            ParseExpected(Kind.CloseParenToken);
        }
        var block = ParseBlock(false /*ignoreMissingOpenBrace*/, null);
        var result = FinishNode(_factory.NewCatchClause(variableDeclaration, block), pos);
        return result;
    }

    private Node ParseDebuggerStatement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.DebuggerKeyword);
        ParseSemicolon();
        var result = FinishNode(_factory.NewDebuggerStatement(), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseExpressionOrLabeledStatement()
    {
        // Avoiding having to do the lookahead for a labeled statement by just trying to parse
        // out an expression, seeing if it is identifier and then seeing if it is followed by
        // a colon.
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var hasParen = _token == Kind.OpenParenToken;
        var expression = ParseExpression();

        if (expression.Kind == Kind.Identifier && ParseOptional(Kind.ColonToken))
        {
            var labeledResult = FinishNode(_factory.NewLabeledStatement(expression, ParseStatement()), pos);
            WithJSDoc(labeledResult, jsdoc);
            return labeledResult;
        }

        if (!TryParseSemicolon())
        {
            ParseErrorForMissingSemicolonAfter(expression);
        }
        var result = FinishNode(_factory.NewExpressionStatement(expression), pos);
        if (hasParen)
        {
            jsdoc &= ~JsdocScannerInfo.HasJSDoc;
        }
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseVariableStatement(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var declarationList = ParseVariableDeclarationList(false /*inForStatementInitializer*/);
        ParseSemicolon();
        var result = FinishNode(_factory.NewVariableStatement(modifiers, declarationList), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    private Node ParseVariableDeclarationList(bool inForStatementInitializer)
    {
        var pos = NodePos();
        var flags = NodeFlags.None;
        switch (_token)
        {
            case Kind.VarKeyword:
                flags = NodeFlags.None;
                break;
            case Kind.LetKeyword:
                flags = NodeFlags.Let;
                break;
            case Kind.ConstKeyword:
                flags = NodeFlags.Const;
                break;
            case Kind.UsingKeyword:
                flags = NodeFlags.Using;
                break;
            case Kind.AwaitKeyword:
                if (!IsAwaitUsingDeclaration())
                {
                    break;
                }
                flags = NodeFlags.AwaitUsing;
                NextToken();
                break;
            default:
                Debug.Fail("Unhandled case in parseVariableDeclarationList");
                throw new System.Diagnostics.UnreachableException();
        }
        NextToken();
        // The user may have written the following:
        //
        //    for (let of X) { }
        //
        // In this case, we want to parse an empty declaration list, and then parse 'of'
        // as a keyword. The reason this is not automatic is that 'of' is a valid identifier.
        // So we need to look ahead to determine if 'of' should be treated as a keyword in
        // this context.
        // The checker will then give an error that there is an empty declaration list.
        NodeList? declarations;
        if (_token == Kind.OfKeyword && LookAhead(p => p.NextIsIdentifierAndCloseParen()))
        {
            declarations = CreateMissingList();
        }
        else
        {
            var saveContextFlags = _contextFlags;
            SetContextFlags(NodeFlags.DisallowInContext, inForStatementInitializer);
            Func<Parser, Node> parseVariableDeclaration = inForStatementInitializer
                ? p => p.ParseVariableDeclaration()
                : p => p.ParseVariableDeclarationAllowExclamation();
            declarations = ParseDelimitedList(ParsingContext.VariableDeclarations, parseVariableDeclaration);
            _contextFlags = saveContextFlags;
        }
        var result = FinishNode(_factory.NewVariableDeclarationList(declarations, flags), pos);
        return result;
    }

    private bool NextIsIdentifierAndCloseParen()
    {
        return NextTokenIsIdentifier() && NextToken() == Kind.CloseParenToken;
    }

    private bool NextTokenIsIdentifier()
    {
        NextToken();
        return IsIdentifier();
    }

    private Node ParseVariableDeclaration()
    {
        return ParseVariableDeclarationWorker(false /*allowExclamation*/);
    }

    private Node ParseVariableDeclarationAllowExclamation()
    {
        return ParseVariableDeclarationWorker(true /*allowExclamation*/);
    }

    private Node ParseVariableDeclarationWorker(bool allowExclamation)
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var name = ParseIdentifierOrPatternWithDiagnostic(Private_identifiers_are_not_allowed_in_variable_declarations);
        Node? exclamationToken = null;
        if (allowExclamation && name.Kind == Kind.Identifier && _token == Kind.ExclamationToken && !HasPrecedingLineBreak())
        {
            exclamationToken = ParseTokenNode();
        }
        var typeNode = ParseTypeAnnotation();
        Node? initializer = null;
        if (_token != Kind.InKeyword && _token != Kind.OfKeyword)
        {
            initializer = ParseInitializer();
        }
        var result = FinishNode(_factory.NewVariableDeclaration(name, exclamationToken, typeNode, initializer), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    private Node ParseIdentifierOrPattern()
    {
        return ParseIdentifierOrPatternWithDiagnostic(null);
    }

    private Node ParseIdentifierOrPatternWithDiagnostic(Message? privateIdentifierDiagnosticMessage)
    {
        if (_token == Kind.OpenBracketToken)
        {
            return ParseArrayBindingPattern();
        }
        if (_token == Kind.OpenBraceToken)
        {
            return ParseObjectBindingPattern();
        }
        return ParseBindingIdentifierWithDiagnostic(privateIdentifierDiagnosticMessage);
    }

    private Node ParseArrayBindingPattern()
    {
        var pos = NodePos();
        ParseExpected(Kind.OpenBracketToken);
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.DisallowInContext, false);
        var elements = ParseDelimitedList(ParsingContext.ArrayBindingElements, p => p.ParseArrayBindingElement());
        _contextFlags = saveContextFlags;
        ParseExpected(Kind.CloseBracketToken);
        return FinishNode(_factory.NewBindingPattern(Kind.ArrayBindingPattern, elements), pos);
    }

    private Node ParseArrayBindingElement()
    {
        var pos = NodePos();
        Node? dotDotDotToken = null;
        Node? name = null;
        Node? initializer = null;
        if (_token != Kind.CommaToken)
        {
            // These are all nil for a missing element
            dotDotDotToken = ParseOptionalToken(Kind.DotDotDotToken);
            name = ParseIdentifierOrPattern();
            initializer = ParseInitializer();
        }
        return FinishNode(_factory.NewBindingElement(dotDotDotToken, null /*propertyName*/, name, initializer), pos);
    }

    private Node ParseObjectBindingPattern()
    {
        var pos = NodePos();
        ParseExpected(Kind.OpenBraceToken);
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.DisallowInContext, false);
        var elements = ParseDelimitedList(ParsingContext.ObjectBindingElements, p => p.ParseObjectBindingElement());
        _contextFlags = saveContextFlags;
        ParseExpected(Kind.CloseBraceToken);
        return FinishNode(_factory.NewBindingPattern(Kind.ObjectBindingPattern, elements), pos);
    }

    private Node ParseObjectBindingElement()
    {
        var pos = NodePos();
        var dotDotDotToken = ParseOptionalToken(Kind.DotDotDotToken);
        var tokenIsIdentifier = IsBindingIdentifier();
        var propertyName = ParsePropertyName();
        Node? name;
        if (tokenIsIdentifier && _token != Kind.ColonToken)
        {
            name = propertyName;
            propertyName = null;
        }
        else
        {
            ParseExpected(Kind.ColonToken);
            name = ParseIdentifierOrPattern();
        }
        var initializer = ParseInitializer();
        return FinishNode(_factory.NewBindingElement(dotDotDotToken, propertyName, name, initializer), pos);
    }

    private Node? ParseInitializer()
    {
        if (ParseOptional(Kind.EqualsToken))
        {
            return ParseAssignmentExpressionOrHigher();
        }
        return null;
    }

    private Node? ParseTypeAnnotation()
    {
        if (ParseOptional(Kind.ColonToken))
        {
            return ParseType();
        }
        return null;
    }

    private Node ParseFunctionDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        ParseExpected(Kind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken(Kind.AsteriskToken);
        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        Node? name = null;
        if (modifiers is null || (modifiers.ModifierFlags & ModifierFlags.Default) == 0 || IsBindingIdentifier())
        {
            name = ParseBindingIdentifier();
        }
        var signatureFlags = (asteriskToken is not null ? ParseFlags.Yield : ParseFlags.None) | (modifiers is not null && (modifiers.ModifierFlags & ModifierFlags.Async) != 0 ? ParseFlags.Await : ParseFlags.None);
        var typeParameters = ParseTypeParameters();
        var saveContextFlags = _contextFlags;
        if (modifiers is not null && (modifiers.ModifierFlags & ModifierFlags.Export) != 0)
        {
            SetContextFlags(NodeFlags.AwaitContext, true);
        }
        var parameters = ParseParameters(signatureFlags);
        var returnType = ParseReturnType(Kind.ColonToken, false /*isType*/);
        var body = ParseFunctionBlockOrSemicolon(signatureFlags, X_or_expected);
        _contextFlags = saveContextFlags;
        var result = FinishNode(_factory.NewFunctionDeclaration(modifiers, asteriskToken, name, typeParameters, parameters, returnType, null /*fullSignature*/, body), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    private Node ParseClassDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        return ParseClassDeclarationOrExpression(pos, jsdoc, modifiers, Kind.ClassDeclaration);
    }

    private Node ParseClassExpression()
    {
        return ParseClassDeclarationOrExpression(NodePos(), JsdocScannerInfo, null /*modifiers*/, Kind.ClassExpression);
    }

    private Node ParseClassDeclarationOrExpression(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers, Kind kind)
    {
        var saveContextFlags = _contextFlags;
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        ParseExpected(Kind.ClassKeyword);
        // We don't parse the name here in await context, instead we will report a grammar error in the checker.
        var name = ParseNameOfClassDeclarationOrExpression();
        var typeParameters = ParseTypeParameters();
        if (modifiers is not null &&
            (_parsingContexts & (ParsingContexts)(1 << (int)ParsingContext.SourceElements)) != ParsingContexts.None &&
            (_parsingContexts & (ParsingContexts)((1 << (int)ParsingContext.BlockStatements) | (1 << (int)ParsingContext.SwitchClauseStatements))) == ParsingContexts.None &&
            Some(modifiers.Nodes, IsExportModifier))
        {
            SetContextFlags(NodeFlags.AwaitContext, true /*value*/);
        }
        var heritageClauses = ParseHeritageClauses(false /*isInterface*/);
        NodeList members;
        if (ParseExpected(Kind.OpenBraceToken))
        {
            // ClassTail[Yield,Await] : (Modified) See 14.5
            //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }
            members = ParseList(ParsingContext.ClassMembers, p => p.ParseClassElement());
            ParseExpected(Kind.CloseBraceToken);
        }
        else
        {
            members = CreateMissingList();
        }
        _contextFlags = saveContextFlags;
        Node result;
        if (modifiers is not null && (Ast.ModifiersToFlags(modifiers.Nodes ?? []) & ModifierFlags.Ambient) != 0)
        {
            _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        }
        if (kind == Kind.ClassDeclaration)
        {
            result = _factory.NewClassDeclaration(modifiers, name, typeParameters, heritageClauses, members);
        }
        else
        {
            result = _factory.NewClassExpression(modifiers, name, typeParameters, heritageClauses, members);
        }
        FinishNode(result, pos);
        WithJSDoc(result, jsdoc);
        if ((result.Flags & NodeFlags.JavaScriptFile) != 0)
        {
            CheckJSSyntax(result);
            if (heritageClauses is not null)
            {
                foreach (var clause in heritageClauses.Nodes!)
                {
                    var heritageClause = clause.AsHeritageClause()!;
                    if (heritageClause.Token == Kind.ExtendsKeyword)
                    {
                        foreach (var expr in heritageClause.Types!.Nodes!)
                        {
                            CheckJSSyntax(expr);
                        }
                    }
                }
            }
        }
        return result;
    }

    private Node? ParseNameOfClassDeclarationOrExpression()
    {
        // implements is a future reserved word so
        // 'class implements' might mean either
        // - class expression with omitted name, 'implements' starts heritage clause
        // - class with name 'implements'
        // 'isImplementsClause' helps to disambiguate between these two cases
        if (IsBindingIdentifier() && !IsImplementsClause())
        {
            var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
            var id = CreateIdentifier(IsBindingIdentifier());
            _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
            return id;
        }
        return null;
    }

    private bool IsImplementsClause()
    {
        return _token == Kind.ImplementsKeyword && LookAhead(p => p.NextTokenIsIdentifierOrKeyword());
    }

    private static bool IsExportModifier(Node modifier) => modifier.Kind == Kind.ExportKeyword;

    private static bool IsAsyncModifier(Node modifier) => modifier.Kind == Kind.AsyncKeyword;

    private NodeList? ParseHeritageClauses(bool isInterface)
    {
        // ClassTail[Yield,Await] : (Modified) See 14.5
        //      ClassHeritage[?Yield,?Await]opt { ClassBody[?Yield,?Await]opt }
        if (IsHeritageClause())
        {
            return ParseList(ParsingContext.HeritageClauses, p => p.ParseHeritageClause(isInterface));
        }
        return null;
    }

    private Node ParseHeritageClause(bool isInterface)
    {
        var pos = NodePos();
        var kind = _token;
        NextToken();
        Func<Parser, Node> parseElement = p => p.ParseExpressionWithTypeArguments();
        if (IsTypeHeritageClause(isInterface, kind))
        {
            parseElement = p => p.ParseTypeHeritageClauseElement();
        }
        var types = ParseDelimitedList(ParsingContext.HeritageClauseElement, parseElement);
        return CheckJSSyntax(FinishNode(_factory.NewHeritageClause(kind, types), pos));
    }

    private static bool IsTypeHeritageClause(bool isInterface, Kind token)
    {
        return (isInterface && token == Kind.ExtendsKeyword) ||
            (!isInterface && token == Kind.ImplementsKeyword);
    }

    private Node ParseTypeHeritageClauseElement()
    {
        var pos = NodePos();
        var expressionWithTypeArguments = ParseExpressionWithTypeArguments().AsExpressionWithTypeArguments()!;
        if (!IsValidHeritageTypeReferenceExpression(expressionWithTypeArguments.Expression!))
        {
            return expressionWithTypeArguments.AsNode();
        }
        var typeName = ConvertEntityNameExpressionToEntityName(expressionWithTypeArguments.Expression!);
        return FinishNode(_factory.NewTypeReferenceNode(typeName, expressionWithTypeArguments.TypeArguments), pos);
    }

    private static bool IsValidHeritageTypeReferenceExpression(Node node)
    {
        if (TypeScript.Ast.Ast.IsIdentifier(node))
        {
            return NodeIsPresent(node);
        }
        return IsPropertyAccessExpression(node) &&
            !IsOptionalChain(node) &&
            NodeIsPresent(node.Name()) &&
            IsValidHeritageTypeReferenceExpression(node.Expression()!);
    }

    private Node ConvertEntityNameExpressionToEntityName(Node node)
    {
        if (TypeScript.Ast.Ast.IsIdentifier(node))
        {
            return node;
        }
        var propertyAccess = node.AsPropertyAccessExpression()!;
        var result = _factory.NewQualifiedName(
            ConvertEntityNameExpressionToEntityName(propertyAccess.Expression!),
            propertyAccess.Name()
        );
        return FinishNodeWithEnd(result, node.Pos(), node.End());
    }

    private Node ParseExpressionWithTypeArguments()
    {
        var pos = NodePos();
        var expression = ParseLeftHandSideExpressionOrHigher()!;
        if (IsExpressionWithTypeArguments(expression))
        {
            return expression;
        }
        var typeArguments = ParseTypeArguments();
        return FinishNode(_factory.NewExpressionWithTypeArguments(expression, typeArguments), pos);
    }

    private Node ParseClassElement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        if (_token == Kind.SemicolonToken)
        {
            NextToken();
            var semicolonResult = FinishNode(_factory.NewSemicolonClassElement(), pos);
            WithJSDoc(semicolonResult, jsdoc);
            return semicolonResult;
        }
        var modifiers = ParseModifiersEx(true /*allowDecorators*/, true /*permitConstAsModifier*/, true /*stopOnStartOfClassStaticBlock*/);
        if (_token == Kind.StaticKeyword && LookAhead(p => p.NextTokenIsOpenBrace()))
        {
            return ParseClassStaticBlockDeclaration(pos, jsdoc, modifiers);
        }
        if (ParseContextualModifier(Kind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, jsdoc, modifiers, Kind.GetAccessor, ParseFlags.None);
        }
        if (ParseContextualModifier(Kind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, jsdoc, modifiers, Kind.SetAccessor, ParseFlags.None);
        }
        if (_token == Kind.ConstructorKeyword || _token == Kind.StringLiteral)
        {
            var constructorDeclaration = TryParseConstructorDeclaration(pos, jsdoc, modifiers);
            if (constructorDeclaration is not null)
            {
                return constructorDeclaration;
            }
        }
        if (IsIndexSignature())
        {
            return CheckJSSyntax(ParseIndexSignatureDeclaration(pos, jsdoc, modifiers));
        }
        // It is very important that we check this *after* checking indexers because
        // the [ token can start an index signature or a computed property name
        if (TokenIsIdentifierOrKeyword(_token) || _token == Kind.StringLiteral || _token == Kind.NumericLiteral || _token == Kind.BigIntLiteral || _token == Kind.AsteriskToken || _token == Kind.OpenBracketToken)
        {
            var isAmbient = modifiers is not null && Some(modifiers.Nodes, IsDeclareModifier);
            if (isAmbient)
            {
                foreach (var m in modifiers!.Nodes!)
                {
                    m.Flags |= NodeFlags.Ambient;
                }
                var saveContextFlags = _contextFlags;
                SetContextFlags(NodeFlags.Ambient, true);
                var ambientResult = ParsePropertyOrMethodDeclaration(pos, jsdoc, modifiers);
                _contextFlags = saveContextFlags;
                return ambientResult;
            }
            else
            {
                return ParsePropertyOrMethodDeclaration(pos, jsdoc, modifiers);
            }
        }
        if (modifiers is not null)
        {
            // treat this as a property declaration with a missing name.
            ParseErrorAt(NodePos(), NodePos(), Declaration_expected);
            var name = CreateMissingIdentifier();
            return ParsePropertyDeclaration(pos, jsdoc, modifiers, name, null /*questionToken*/);
        }
        // 'isClassMemberStart' should have hinted not to attempt parsing.
        Debug.Fail("Should not have attempted to parse class member declaration.");
        throw new System.Diagnostics.UnreachableException();
    }

    private Node ParseClassStaticBlockDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        ParseExpectedToken(Kind.StaticKeyword);
        var body = ParseClassStaticBlockBody();
        var result = FinishNode(_factory.NewClassStaticBlockDeclaration(modifiers, body), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    private Node ParseClassStaticBlockBody()
    {
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.YieldContext, false);
        SetContextFlags(NodeFlags.AwaitContext, true);
        var body = ParseBlock(false /*ignoreMissingOpenBrace*/, null /*diagnosticMessage*/);
        _contextFlags = saveContextFlags;
        return body;
    }

    private Node? TryParseConstructorDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var state = Mark();
        if (_token == Kind.ConstructorKeyword || (_token == Kind.StringLiteral && _scanner.TokenValue() == "constructor" && LookAhead(p => p.NextTokenIsOpenParen())))
        {
            NextToken();
            var typeParameters = ParseTypeParameters();
            var parameters = ParseParameters(ParseFlags.None);
            var returnType = ParseReturnType(Kind.ColonToken, false /*isType*/);
            var body = ParseFunctionBlockOrSemicolon(ParseFlags.None, X_or_expected);
            var result = FinishNode(_factory.NewConstructorDeclaration(modifiers, typeParameters, parameters, returnType, null /*fullSignature*/, body), pos);
            WithJSDoc(result, jsdoc);
            CheckJSSyntax(result);
            return result;
        }
        Rewind(state);
        return null;
    }

    private bool NextTokenIsOpenParen()
    {
        return NextToken() == Kind.OpenParenToken;
    }

    private Node ParsePropertyOrMethodDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var asteriskToken = ParseOptionalToken(Kind.AsteriskToken);
        var name = ParsePropertyName();
        // Note: this is not legal as per the grammar.  But we allow it in the parser and
        // report an error in the grammar checker.
        var questionToken = ParseOptionalToken(Kind.QuestionToken);
        if (asteriskToken is not null || _token == Kind.OpenParenToken || _token == Kind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, jsdoc, modifiers, asteriskToken, name, questionToken, X_or_expected);
        }
        return ParsePropertyDeclaration(pos, jsdoc, modifiers, name, questionToken);
    }

    private Node ParseMethodDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers, Node? asteriskToken, Node? name, Node? questionToken, Message? diagnosticMessage)
    {
        var signatureFlags = (asteriskToken is not null ? ParseFlags.Yield : ParseFlags.None) | (ModifierListHasAsync(modifiers) ? ParseFlags.Await : ParseFlags.None);
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(signatureFlags);
        var typeNode = ParseReturnType(Kind.ColonToken, false /*isType*/);
        var body = ParseFunctionBlockOrSemicolon(signatureFlags, diagnosticMessage);
        var result = FinishNode(_factory.NewMethodDeclaration(modifiers, asteriskToken, name, questionToken, typeParameters, parameters, typeNode, null /*fullSignature*/, body), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    private static bool ModifierListHasAsync(ModifierList? modifiers)
    {
        return modifiers is not null && Some(modifiers.Nodes, IsAsyncModifier);
    }

    private Node ParsePropertyDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers, Node? name, Node? questionToken)
    {
        var postfixToken = questionToken;
        if (postfixToken is null && !HasPrecedingLineBreak())
        {
            postfixToken = ParseOptionalToken(Kind.ExclamationToken);
        }
        var typeNode = ParseTypeAnnotation();
        var initializer = DoInContext(NodeFlags.YieldContext | NodeFlags.AwaitContext | NodeFlags.DisallowInContext, false, p => p.ParseInitializer());
        ParseSemicolonAfterPropertyName(name, typeNode, initializer);
        var result = FinishNode(_factory.NewPropertyDeclaration(modifiers, name, postfixToken, typeNode, initializer), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    private void ParseSemicolonAfterPropertyName(Node? name, Node? typeNode, Node? initializer)
    {
        if (_token == Kind.AtToken && !HasPrecedingLineBreak())
        {
            ParseErrorAtCurrentToken(Decorators_must_precede_the_name_and_all_keywords_of_property_declarations);
            return;
        }
        if (_token == Kind.OpenParenToken)
        {
            ParseErrorAtCurrentToken(Cannot_start_a_function_call_in_a_type_annotation);
            NextToken();
            return;
        }
        if (typeNode is not null && !CanParseSemicolon())
        {
            if (initializer is not null)
            {
                ParseErrorAtCurrentToken(X_0_expected, TokenToString(Kind.SemicolonToken));
            }
            else
            {
                ParseErrorAtCurrentToken(Expected_for_property_initializer);
            }
            return;
        }
        if (TryParseSemicolon())
        {
            return;
        }
        if (initializer is not null)
        {
            ParseErrorAtCurrentToken(X_0_expected, TokenToString(Kind.SemicolonToken));
            return;
        }
        ParseErrorForMissingSemicolonAfter(name!);
    }

    private void ParseErrorForMissingSemicolonAfter(Node node)
    {
        // Tagged template literals are sometimes used in places where only simple strings are allowed, i.e.:
        //   module `M1` {
        //   ^^^^^^^^^^^ This block is parsed as a template literal like module`M1`.
        if (node.Kind == Kind.TaggedTemplateExpression)
        {
            ParseErrorAtRange(SkipRangeTrivia(node.AsTaggedTemplateExpression()!.Template!.Loc), Module_declaration_names_may_only_use_or_quoted_strings);
            return;
        }
        // Otherwise, if this isn't a well-known keyword-like identifier, give the generic fallback message.
        var expressionText = "";
        if (node.Kind == Kind.Identifier)
        {
            expressionText = node.Text();
        }
        if (expressionText.Length == 0)
        {
            ParseErrorAtCurrentToken(X_0_expected, TokenToString(Kind.SemicolonToken));
            return;
        }
        var pos = SkipTrivia(_sourceText, node.Pos());
        // Some known keywords are likely signs of syntax being used improperly.
        switch (expressionText)
        {
            case "const":
            case "let":
            case "var":
                ParseErrorAt(pos, node.End(), Variable_declaration_not_allowed_at_this_location);
                return;
            case "declare":
                // If a declared node failed to parse, it would have emitted a diagnostic already.
                return;
            case "interface":
                ParseErrorForInvalidName(Interface_name_cannot_be_0, Interface_must_be_given_a_name, Kind.OpenBraceToken);
                return;
            case "is":
                ParseErrorAt(pos, _scanner.TokenStart(), A_type_predicate_is_only_allowed_in_return_type_position_for_functions_and_methods);
                return;
            case "module":
            case "namespace":
                ParseErrorForInvalidName(Namespace_name_cannot_be_0, Namespace_must_be_given_a_name, Kind.OpenBraceToken);
                return;
            case "type":
                ParseErrorForInvalidName(Type_alias_name_cannot_be_0, Type_alias_must_be_given_a_name, Kind.EqualsToken);
                return;
        }
        // The user alternatively might have misspelled or forgotten to add a space after a common keyword.
        var suggestion = GetSpellingSuggestionForStrings(expressionText, _viableKeywordSuggestions);
        if (suggestion.Length == 0)
        {
            suggestion = GetSpaceSuggestion(expressionText);
        }
        if (suggestion.Length != 0)
        {
            ParseErrorAt(pos, node.End(), Unknown_keyword_or_identifier_Did_you_mean_0, suggestion);
            return;
        }
        // Unknown tokens are handled with their own errors in the scanner
        if (_token == Kind.Unknown)
        {
            return;
        }
        // Otherwise, we know this some kind of unknown word, not just a missing expected semicolon.
        ParseErrorAt(pos, node.End(), Unexpected_keyword_or_identifier);
    }

    private static string GetSpaceSuggestion(string expressionText)
    {
        foreach (var keyword in _viableKeywordSuggestions)
        {
            // Go 的 len(expressionText)/[len(keyword):] 是字节下标；TS 原文是 UTF-16 长度，
            // C# 用 Length/范围下标与之同构（docs/architecture.md §3.1）。
            if (expressionText.Length > keyword.Length + 2 && HasPrefix(expressionText, keyword, true /*caseSensitive*/))
            {
                return keyword + " " + expressionText[keyword.Length..];
            }
        }
        return "";
    }

    private void ParseErrorForInvalidName(Message nameDiagnostic, Message blankDiagnostic, Kind tokenIfBlankName)
    {
        if (_token == tokenIfBlankName)
        {
            ParseErrorAtCurrentToken(blankDiagnostic);
        }
        else
        {
            ParseErrorAtCurrentToken(nameDiagnostic, _scanner.TokenValue());
        }
    }

    private Node ParseInterfaceDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        ParseExpected(Kind.InterfaceKeyword);
        var name = ParseIdentifier();
        var typeParameters = ParseTypeParameters();
        var heritageClauses = ParseHeritageClauses(true /*isInterface*/);
        var members = ParseObjectTypeMembers();
        var result = FinishNode(_factory.NewInterfaceDeclaration(modifiers, name, typeParameters, heritageClauses, members), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }
}
