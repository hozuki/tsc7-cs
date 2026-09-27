// ported from: internal/printer/printer.go:3361-4253（语句与声明）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的类型别名（Statement / Expression / TypeNode / ModuleName / ModuleExportName / ImportAttributeName /
//    NamedImportBindings / NamedExportBindings / ModuleReference / ForInitializer / ClassElement / TypeElement /
//    DeclarationName / BindingName）在 C# 里就是 `Node`（docs/ast.md §2）；具体节点类（Block / IfStatement / …）
//    保留 Go 里的具体类型（与 Printer.Emit.cs 的派发签名一致）。
// 2) `panic(fmt.Sprintf(...))` → `Debug.Fail($"...")`；`%v` 打在 Kind 上 → `node.Kind.String()`。Go 里位置随意的
//    `default:` 在 C# 里一律放最后（分支顺序不影响语义）。
// 3) Go 的 `switch { case <cond>: }`（switch-true，无 fallthrough）→ if / else-if 链；多值 `case A, B:` → `case A or B:`。
// 4) Go 的方法值 `(*Printer).emitStatement` → 方法组 `EmitStatement`（委托 `NodeEmitter`，声明在 Printer.Lists.cs）。
// 5) 常量名映射：`EFNoLeadingComments` → `EmitFlags.NoLeadingComments`；`tefIndentLeadingComments` →
//    `TokenEmitFlags.IndentLeadingComments`；`tefNone` → `TokenEmitFlags.None`；`commentSeparatorAfter` →
//    `CommentSeparator.After`；`LFXxx` → `ListFormat.Xxx`。
// 6) `ast.OperatorPrecedenceComma` → `OperatorPrecedenceLowest`（Ast 的 const），`ast.OperatorPrecedenceDisallowComma` →
//    `OperatorPrecedenceDisallowComma`（const），`ast.OperatorPrecedenceParentheses` → `OperatorPrecedence.Parentheses`，
//    `ast.OperatorPrecedenceAssignment` → `OperatorPrecedence.Assignment`，`ast.TypePrecedenceNonArray` → `TypePrecedence.NonArray`。
// 7) `greatestEnd(end, nodes...)` → `GreatestEnd(end, nodes)`（`params object?[]` 重载，见 Utilities.cs；传 nil 即
//    Go 的 nil 指针分支，判定后跳过）。
// 8) `p.emitContext.SnippetElement(node)` 返回 `SnippetElement?` → `is { } snippetElement` 绑定出非空值后按值传给
//    `EmitSnippetNode`（该方法的第二个参数是 `SnippetElement`，非可空）。
// 9) Go 里不判空就解引用的地方照 Go 写 `x!`（nil 时 C# 抛 NRE，等价于 Go 的 panic）；Go 里判空的分支则不加 `!`。
// 10) `emitTryStatement` 里 Go 两次调用 `core.Coalesce(node.CatchClause, node.TryBlock)`，C# 版提出局部变量 `prevNode`
//     （`Coalesce` 的返回类型是可空的，局部变量 + `!` 比两处 `!` 更贴近 Go 的"必非空"推断）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    internal void EmitSemicolonClassElement(SemicolonClassElement node)
    {
        var state = EnterNode(node.AsNode());
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    //
    // Statements
    //

    internal bool IsEmptyBlock(Node block, NodeList statements)
    {
        return (statements.Nodes is null || statements.Nodes.Length == 0) &&
            (_currentSourceFile is null || RangeEndIsOnSameLineAsRangeStart(block.Loc, block.Loc, _currentSourceFile));
    }

    internal void EmitBlock(Block node)
    {
        var state = EnterNode(node.AsNode());
        GenerateNames(node.AsNode());
        EmitToken(Kind.OpenBraceToken, node.Pos(), WriteKind.Punctuation, node.AsNode());

        var format = IfElse(!node.MultiLine && IsEmptyBlock(node.AsNode(), node.Statements!) || ShouldEmitOnSingleLine(node.AsNode()),
            ListFormat.SingleLineBlockStatements,
            ListFormat.MultiLineBlockStatements);
        EmitList(EmitStatement, node.AsNode(), node.Statements, format);

        EmitTokenEx(Kind.CloseBraceToken, node.Statements!.End(), WriteKind.Punctuation, node.AsNode(), IfElse((format & ListFormat.MultiLine) != 0, TokenEmitFlags.IndentLeadingComments, TokenEmitFlags.None));
        ExitNode(node.AsNode(), state);
    }

    internal void EmitVariableStatement(VariableStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        EmitVariableDeclarationList(node.DeclarationList!.AsVariableDeclarationList()!);
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitEmptyStatement(EmptyStatement node, bool isEmbeddedStatement)
    {
        var state = EnterNode(node.AsNode());

        // While most trailing semicolons are possibly insignificant, an embedded "empty"
        // statement is significant and cannot be elided by a trailing-semicolon-omitting writer.
        if (isEmbeddedStatement)
        {
            WritePunctuation(";");
        }
        else
        {
            WriteTrailingSemicolon();
        }
        ExitNode(node.AsNode(), state);
    }

    internal void EmitExpressionStatement(ExpressionStatement node)
    {
        var state = EnterNode(node.AsNode());

        if (_currentSourceFile is not null && _currentSourceFile.ScriptKind == ScriptKind.JSON)
        {
            // !!! In strada, this was handled by an undefined parenthesizerRule, so this is a hack.
            EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        }
        else if (IsImmediatelyInvokedFunctionExpressionOrArrowFunction(node.Expression!))
        {
            // For IIFEs, parenthesize just the callee (not the whole call), matching TypeScript's
            // parenthesizeExpressionOfExpressionStatement which wraps the function/arrow in parens:
            //   (function() { })()  -- not (function() { }())
            EmitIIFEWithParenthesizedCallee(node.Expression!);
        }
        else
        {
            switch (GetLeftmostExpression(node.Expression!, false /*stopAtCallExpression*/).Kind)
            {
                case Kind.FunctionExpression or Kind.ObjectLiteralExpression:
                    EmitExpression(node.Expression!, OperatorPrecedence.Parentheses);
                    break;
                default:
                    EmitExpression(node.Expression!, OperatorPrecedenceLowest);
                    break;
            }
        }

        // Emit semicolon in non json files
        // or if json file that created synthesized expression(eg.define expression statement when --out and amd code generation)
        if (_currentSourceFile is null ||
            _currentSourceFile.ScriptKind != ScriptKind.JSON ||
            NodeIsSynthesized(node.Expression!))
        {
            WriteTrailingSemicolon();
        }

        ExitNode(node.AsNode(), state);
    }

    // EmitIIFEWithParenthesizedCallee emits a call expression that is an IIFE,
    // wrapping just the callee in parens rather than the entire call expression.
    // This matches TypeScript's parenthesizeExpressionOfExpressionStatement behavior:
    //
    //	(function() { })()   -- parens around callee only
    //
    // instead of:
    //
    //	(function() { }())   -- parens around entire call
    internal void EmitIIFEWithParenthesizedCallee(Node node)
    {
        // Walk through PartiallyEmittedExpression wrappers to find the call
        var call = SkipPartiallyEmittedExpressions(node)!.AsCallExpression()!;
        var state = EnterNode(call.AsNode());
        // Emit the callee wrapped in parens
        WritePunctuation("(");
        EmitExpression(call.Expression!, OperatorPrecedenceLowest);
        WritePunctuation(")");
        EmitTokenNode(call.QuestionDotToken);
        EmitTypeArguments(call.AsNode(), call.TypeArguments);
        EmitList(EmitArgument, call.AsNode(), call.Arguments, ListFormat.CallExpressionArguments);
        ExitNode(call.AsNode(), state);
    }

    internal void EmitIfStatement(IfStatement node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.IfKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitToken(Kind.OpenParenToken, pos, WriteKind.Punctuation, node.AsNode());
        EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        EmitToken(Kind.CloseParenToken, node.Expression!.End(), WriteKind.Punctuation, node.AsNode());
        EmitEmbeddedStatement(node.AsNode(), node.ThenStatement!);
        if (node.ElseStatement is not null)
        {
            WriteLineOrSpace(node.AsNode(), node.ThenStatement!, node.ElseStatement);
            EmitToken(Kind.ElseKeyword, node.ThenStatement!.End(), WriteKind.Keyword, node.AsNode());
            if (node.ElseStatement.Kind == Kind.IfStatement)
            {
                WriteSpace();
                EmitIfStatement(node.ElseStatement.AsIfStatement()!);
            }
            else
            {
                EmitEmbeddedStatement(node.AsNode(), node.ElseStatement);
            }
        }
        ExitNode(node.AsNode(), state);
    }

    internal void EmitWhileClause(Node node, Node expression, int startPos)
    {
        var pos = EmitToken(Kind.WhileKeyword, startPos, WriteKind.Keyword, node);
        WriteSpace();
        EmitToken(Kind.OpenParenToken, pos, WriteKind.Punctuation, node);
        EmitExpression(expression, OperatorPrecedenceLowest);
        EmitToken(Kind.CloseParenToken, expression.End(), WriteKind.Punctuation, node);
    }

    internal void EmitDoStatement(DoStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.DoKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        EmitEmbeddedStatement(node.AsNode(), node.Statement!);
        if (IsBlock(node.Statement!) && !Options.PreserveSourceNewlines)
        {
            WriteSpace();
        }
        else
        {
            WriteLineOrSpace(node.AsNode(), node.Statement!, node.Expression!);
        }

        EmitWhileClause(node.AsNode(), node.Expression!, node.Statement!.End());
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitWhileStatement(WhileStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitWhileClause(node.AsNode(), node.Expression!, node.Pos());
        EmitEmbeddedStatement(node.AsNode(), node.Statement!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitForInitializer(Node node)
    {
        if (node.Kind == Kind.VariableDeclarationList)
        {
            EmitVariableDeclarationList(node.AsVariableDeclarationList()!);
        }
        else
        {
            EmitExpression(node, OperatorPrecedenceLowest);
        }
    }

    internal void EmitForStatement(ForStatement node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.ForKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        pos = EmitToken(Kind.OpenParenToken, pos, WriteKind.Punctuation, node.AsNode());
        if (node.Initializer is not null)
        {
            EmitForInitializer(node.Initializer);
            pos = node.Initializer.End();
        }
        pos = EmitToken(Kind.SemicolonToken, pos, WriteKind.Punctuation, node.AsNode());
        if (node.Condition is not null)
        {
            WriteSpace();
            EmitExpression(node.Condition, OperatorPrecedenceLowest);
            pos = node.Condition.End();
        }
        pos = EmitToken(Kind.SemicolonToken, pos, WriteKind.Punctuation, node.AsNode());
        if (node.Incrementor is not null)
        {
            WriteSpace();
            EmitExpression(node.Incrementor, OperatorPrecedenceLowest);
            pos = node.Incrementor.End();
        }
        EmitToken(Kind.CloseParenToken, pos, WriteKind.Punctuation, node.AsNode());
        EmitEmbeddedStatement(node.AsNode(), node.Statement!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitForInStatement(ForInOrOfStatement node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.ForKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitToken(Kind.OpenParenToken, pos, WriteKind.Punctuation, node.AsNode());
        EmitForInitializer(node.Initializer!);
        WriteSpace();
        EmitToken(Kind.InKeyword, node.Initializer!.End(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        EmitToken(Kind.CloseParenToken, node.Expression!.End(), WriteKind.Punctuation, node.AsNode());
        EmitEmbeddedStatement(node.AsNode(), node.Statement!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitForOfStatement(ForInOrOfStatement node)
    {
        var state = EnterNode(node.AsNode());
        var openParenPos = EmitToken(Kind.ForKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        if (node.AwaitModifier is not null)
        {
            EmitKeywordNode(node.AwaitModifier);
            WriteSpace();
        }
        EmitToken(Kind.OpenParenToken, openParenPos, WriteKind.Punctuation, node.AsNode());
        EmitForInitializer(node.Initializer!);
        WriteSpace();
        EmitToken(Kind.OfKeyword, node.Initializer!.End(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        EmitToken(Kind.CloseParenToken, node.Expression!.End(), WriteKind.Punctuation, node.AsNode());
        EmitEmbeddedStatement(node.AsNode(), node.Statement!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitContinueStatement(ContinueStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.ContinueKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        if (node.Label is not null)
        {
            WriteSpace();
            EmitLabelIdentifier(node.Label.AsIdentifier()!);
        }
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitBreakStatement(BreakStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.BreakKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        if (node.Label is not null)
        {
            WriteSpace();
            EmitLabelIdentifier(node.Label.AsIdentifier()!);
        }
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitReturnStatement(ReturnStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.ReturnKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        if (node.Expression is not null)
        {
            WriteSpace();
            EmitExpressionNoASI(node.Expression, OperatorPrecedenceLowest);
        }
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitWithStatement(WithStatement node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.WithKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitToken(Kind.OpenParenToken, pos, WriteKind.Punctuation, node.AsNode());
        EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        EmitToken(Kind.CloseParenToken, node.Expression!.End(), WriteKind.Punctuation, node.AsNode());
        EmitEmbeddedStatement(node.AsNode(), node.Statement!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitSwitchStatement(SwitchStatement node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.SwitchKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitToken(Kind.OpenParenToken, pos, WriteKind.Punctuation, node.AsNode());
        EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        EmitToken(Kind.CloseParenToken, node.Expression!.End(), WriteKind.Punctuation, node.AsNode());
        WriteSpace();
        EmitCaseBlock(node.CaseBlock!.AsCaseBlock()!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitLabeledStatement(LabeledStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitLabelIdentifier(node.Label!.AsIdentifier()!);
        EmitToken(Kind.ColonToken, node.Label!.End(), WriteKind.Punctuation, node.AsNode());

        // TODO: use emitEmbeddedStatement rather than writeSpace/emitStatement here after Strada migration as it is
        //       more consistent with similar emit elsewhere. writeSpace/emitStatement is used here to reduce spurious
        //       diffs when testing the Strada migration.
        ////p.emitEmbeddedStatement(node.AsNode(), node.Statement)

        WriteSpace();
        EmitStatement(node.Statement!);

        ExitNode(node.AsNode(), state);
    }

    internal void EmitThrowStatement(ThrowStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.ThrowKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitExpressionNoASI(node.Expression!, OperatorPrecedenceLowest);
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitTryStatement(TryStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.TryKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitBlock(node.TryBlock!.AsBlock()!);
        if (node.CatchClause is not null)
        {
            WriteLineOrSpace(node.AsNode(), node.TryBlock!, node.CatchClause);
            EmitCatchClause(node.CatchClause.AsCatchClause()!);
        }
        if (node.FinallyBlock is not null)
        {
            // Go 在这里两次调用 core.Coalesce(node.CatchClause, node.TryBlock)；C# 提出局部变量（文件头第 10 条）。
            var prevNode = Coalesce(node.CatchClause, node.TryBlock!)!;
            WriteLineOrSpace(node.AsNode(), prevNode, node.FinallyBlock);
            EmitToken(Kind.FinallyKeyword, prevNode.End(), WriteKind.Keyword, node.AsNode());
            WriteSpace();
            EmitBlock(node.FinallyBlock.AsBlock()!);
        }
        ExitNode(node.AsNode(), state);
    }

    internal void EmitDebuggerStatement(DebuggerStatement node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.DebuggerKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNotEmittedStatement(NotEmittedStatement node)
    {
        ExitNode(node.AsNode(), EnterNode(node.AsNode()));
    }

    internal void EmitNotEmittedTypeElement(NotEmittedTypeElement node)
    {
        ExitNode(node.AsNode(), EnterNode(node.AsNode()));
    }

    //
    // Declarations
    //

    internal void EmitVariableDeclaration(VariableDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitBindingName(node.Name());
        EmitPunctuationNode(node.ExclamationToken);
        EmitTypeAnnotation(node.Type);
        EmitInitializer(node.Initializer, GreatestEnd(node.Name()!.End(), node.Type, _emitContext.GetTypeNode(node.Name()!)), node.AsNode());
        ExitNode(node.AsNode(), state);
    }

    internal void EmitVariableDeclarationNode(Node node)
    {
        EmitVariableDeclaration(node.AsVariableDeclaration()!);
    }

    internal void EmitVariableDeclarationList(VariableDeclarationList node)
    {
        var state = EnterNode(node.AsNode());
        if (IsVarLet(node.AsNode()))
        {
            WriteKeyword("let");
        }
        else if (IsVarConst(node.AsNode()))
        {
            WriteKeyword("const");
        }
        else if (IsVarUsing(node.AsNode()))
        {
            WriteKeyword("using");
        }
        else if (IsVarAwaitUsing(node.AsNode()))
        {
            WriteKeyword("await");
            WriteSpace();
            WriteKeyword("using");
        }
        else
        {
            WriteKeyword("var");
        }
        WriteSpace();
        EmitList(EmitVariableDeclarationNode, node.AsNode(), node.Declarations, ListFormat.VariableDeclarationList);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitFunctionDeclaration(FunctionDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        GenerateNameIfNeeded(node.Name());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        WriteKeyword("function");
        EmitTokenNode(node.AsteriskToken);
        WriteSpace();
        if (node.Name() is { } name)
        {
            EmitIdentifierName(name.AsIdentifier()!);
        }
        var indented = ShouldEmitIndented(node.AsNode());
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node.AsNode());
        EmitSignature(node.AsNode());
        EmitFunctionBodyNode(node.Body);
        PopNameGenerationScope(node.AsNode());
        DecreaseIndentIf(indented);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitClassDeclaration(ClassDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        GenerateNameIfNeeded(node.Name());
        var pos = EmitModifierList(node.AsNode(), node.Modifiers(), true /*allowDecorators*/);
        EmitToken(Kind.ClassKeyword, pos, WriteKind.Keyword, node.AsNode());
        if (node.Name() is { } name)
        {
            WriteSpace();
            EmitIdentifierName(name.AsIdentifier()!);
        }
        var indented = ShouldEmitIndented(node.AsNode());
        IncreaseIndentIf(indented);
        EmitTypeParameters(node.AsNode(), node.TypeParameters);
        EmitList(EmitHeritageClauseNode, node.AsNode(), node.HeritageClauses, ListFormat.ClassHeritageClauses);
        WriteSpace();
        WritePunctuation("{");
        PushNameGenerationScope(node.AsNode());
        GenerateAllMemberNames(node.Members);
        EmitList(EmitClassElement, node.AsNode(), node.Members, ListFormat.ClassMembers);
        PopNameGenerationScope(node.AsNode());
        WritePunctuation("}");
        DecreaseIndentIf(indented);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitInterfaceDeclaration(InterfaceDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        WriteKeyword("interface");
        WriteSpace();
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        EmitTypeParameters(node.AsNode(), node.TypeParameters);
        EmitList(EmitHeritageClauseNode, node.AsNode(), node.HeritageClauses, ListFormat.HeritageClauses);
        WriteSpace();
        WritePunctuation("{");
        PushNameGenerationScope(node.AsNode());
        GenerateAllMemberNames(node.Members);
        EmitList(EmitTypeElement, node.AsNode(), node.Members, ListFormat.InterfaceMembers);
        PopNameGenerationScope(node.AsNode());
        WritePunctuation("}");
        ExitNode(node.AsNode(), state);
    }

    internal void EmitTypeAliasDeclaration(TypeAliasDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        WriteKeyword("type");
        WriteSpace();
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        EmitTypeParameters(node.AsNode(), node.TypeParameters);
        WriteSpace();
        WritePunctuation("=");
        WriteSpace();
        EmitTypeNodeOutsideExtends(node.Type!);
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitEnumDeclaration(EnumDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        WriteKeyword("enum");
        WriteSpace();
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        WriteSpace();
        WritePunctuation("{");
        EmitList(EmitEnumMemberNode, node.AsNode(), node.Members, ListFormat.EnumMembers);
        WritePunctuation("}");
        ExitNode(node.AsNode(), state);
    }

    internal void EmitModuleDeclaration(ModuleDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        if (node.Keyword != Kind.GlobalKeyword)
        {
            WriteKeyword(IfElse(node.Keyword == Kind.NamespaceKeyword, "namespace", "module"));
            WriteSpace();
        }
        EmitModuleName(node.Name());
        var body = node.Body;
        while (body is not null && IsModuleDeclaration(body))
        {
            var module = body.AsModuleDeclaration()!;
            WritePunctuation(".");
            EmitNestedModuleName(module.Name());
            body = module.Body;
        }
        if (node.Attributes is not null)
        {
            WriteSpace();
            WriteKeyword("with");
            WriteSpace();
            EmitTypeNode(node.Attributes, TypePrecedence.NonArray);
        }
        if (body is null)
        {
            WriteTrailingSemicolon();
        }
        else
        {
            WriteSpace();
            EmitModuleBlock(body.AsModuleBlock()!);
        }
        ExitNode(node.AsNode(), state);
    }

    internal void EmitModuleBlock(ModuleBlock node)
    {
        var state = EnterNode(node.AsNode());
        GenerateNames(node.AsNode());
        EmitToken(Kind.OpenBraceToken, node.Pos(), WriteKind.Punctuation, node.AsNode());
        var format = IfElse(IsEmptyBlock(node.AsNode(), node.Statements!) || ShouldEmitOnSingleLine(node.AsNode()),
            ListFormat.SingleLineBlockStatements,
            ListFormat.MultiLineBlockStatements);
        EmitList(EmitStatement, node.AsNode(), node.Statements, format);
        EmitTokenEx(Kind.CloseBraceToken, node.Statements!.End(), WriteKind.Punctuation, node.AsNode(), IfElse((format & ListFormat.MultiLine) != 0, TokenEmitFlags.IndentLeadingComments, TokenEmitFlags.None));
        ExitNode(node.AsNode(), state);
    }

    internal void EmitCaseBlock(CaseBlock node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(Kind.OpenBraceToken, node.Pos(), WriteKind.Punctuation, node.AsNode());
        EmitList(EmitCaseOrDefaultClauseNode, node.AsNode(), node.Clauses, ListFormat.CaseBlockClauses);
        EmitTokenEx(Kind.CloseBraceToken, node.Clauses!.End(), WriteKind.Punctuation, node.AsNode(), TokenEmitFlags.IndentLeadingComments);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitImportEqualsDeclaration(ImportEqualsDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        var pos = EmitToken(Kind.ImportKeyword, GreatestEnd(node.Pos(), node.Modifiers()), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        if (node.IsTypeOnly)
        {
            EmitToken(Kind.TypeKeyword, pos, WriteKind.Keyword, node.AsNode());
            WriteSpace();
        }
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        WriteSpace();
        EmitToken(Kind.EqualsToken, node.Name()!.End(), WriteKind.Punctuation, node.AsNode());
        WriteSpace();
        EmitModuleReference(node.ModuleReference!);
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitModuleReference(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierReference(node.AsIdentifier()!);
                break;
            case Kind.QualifiedName:
                EmitQualifiedName(node.AsQualifiedName()!);
                break;
            case Kind.ExternalModuleReference:
                EmitExternalModuleReference(node.AsExternalModuleReference()!);
                break;
            default:
                Debug.Fail($"unhandled ModuleReference: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitImportDeclaration(ImportDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        EmitToken(Kind.ImportKeyword, GreatestEnd(node.Pos(), node.Modifiers()), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        if (node.ImportClause is not null)
        {
            EmitImportClause(node.ImportClause.AsImportClause()!);
            WriteSpace();
            EmitToken(Kind.FromKeyword, node.ImportClause.End(), WriteKind.Keyword, node.AsNode());
            WriteSpace();
        }
        EmitExpression(node.ModuleSpecifier!, OperatorPrecedenceLowest);
        if (node.Attributes is not null)
        {
            WriteSpace();
            EmitImportAttributes(node.Attributes.AsImportAttributes()!);
        }
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitImportClause(ImportClause node)
    {
        var state = EnterNode(node.AsNode());
        if (node.PhaseModifier != Kind.Unknown)
        {
            EmitToken(node.PhaseModifier, node.Pos(), WriteKind.Keyword, node.AsNode());
            WriteSpace();
        }
        if (node.Name() is { } name)
        {
            EmitBindingIdentifier(name.AsIdentifier()!);
            if (node.NamedBindings is not null)
            {
                EmitToken(Kind.CommaToken, name.End(), WriteKind.Punctuation, node.AsNode());
                WriteSpace();
            }
        }
        EmitNamedImportBindings(node.NamedBindings);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNamespaceImport(NamespaceImport node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.AsteriskToken, node.Pos(), WriteKind.Punctuation, node.AsNode());
        WriteSpace();
        EmitToken(Kind.AsKeyword, pos, WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNamedImports(NamedImports node)
    {
        var state = EnterNode(node.AsNode());
        WritePunctuation("{");
        EmitList(EmitImportSpecifierNode, node.AsNode(), node.Elements, ListFormat.NamedImportsOrExportsElements);
        WritePunctuation("}");
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNamedImportBindings(Node? node)
    {
        if (node is null)
        {
            return;
        }
        switch (node.Kind)
        {
            case Kind.NamespaceImport:
                EmitNamespaceImport(node.AsNamespaceImport()!);
                break;
            case Kind.NamedImports:
                EmitNamedImports(node.AsNamedImports()!);
                break;
            default:
                Debug.Fail($"unhandled NamedImportBindings: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitImportSpecifier(ImportSpecifier node)
    {
        var state = EnterNode(node.AsNode());
        if (node.IsTypeOnly)
        {
            WriteKeyword("type");
            WriteSpace();
        }
        if (node.PropertyName is not null)
        {
            EmitModuleExportName(node.PropertyName);
            WriteSpace();
            EmitToken(Kind.AsKeyword, node.PropertyName.End(), WriteKind.Keyword, node.AsNode());
            WriteSpace();
        }
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitImportSpecifierNode(Node node)
    {
        EmitImportSpecifier(node.AsImportSpecifier()!);
    }

    internal void EmitExportAssignment(ExportAssignment node)
    {
        var state = EnterNode(node.AsNode());
        var nextPos = EmitToken(Kind.ExportKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        if (node.IsExportEquals)
        {
            EmitToken(Kind.EqualsToken, nextPos, WriteKind.Operator, node.AsNode());
        }
        else
        {
            EmitToken(Kind.DefaultKeyword, nextPos, WriteKind.Keyword, node.AsNode());
        }
        WriteSpace();
        if (node.IsExportEquals)
        {
            EmitExpression(node.Expression!, OperatorPrecedence.Assignment);
        }
        else
        {
            // parenthesize `class` and `function` expressions so as not to conflict with exported `class` and `function` declarations
            var expr = GetLeftmostExpression(node.Expression!, false /*stopAtCallExpressions*/);
            if (IsClassExpression(expr) || IsFunctionExpression(expr))
            {
                EmitExpression(node.Expression!, OperatorPrecedence.Parentheses);
            }
            else
            {
                EmitExpression(node.Expression!, OperatorPrecedence.Assignment);
            }
        }
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitExportDeclaration(ExportDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        EmitModifierList(node.AsNode(), node.Modifiers(), false /*allowDecorators*/);
        var pos = EmitToken(Kind.ExportKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        if (node.IsTypeOnly)
        {
            pos = EmitToken(Kind.TypeKeyword, pos, WriteKind.Keyword, node.AsNode());
            WriteSpace();
        }
        if (node.ExportClause is not null)
        {
            EmitNamedExportBindings(node.ExportClause);
        }
        else
        {
            pos = EmitToken(Kind.AsteriskToken, pos, WriteKind.Punctuation, node.AsNode());
        }
        if (node.ModuleSpecifier is not null)
        {
            WriteSpace();
            EmitToken(Kind.FromKeyword, GreatestEnd(pos, node.ExportClause), WriteKind.Keyword, node.AsNode());
            WriteSpace();
            EmitExpression(node.ModuleSpecifier, OperatorPrecedenceLowest);
        }
        if (node.Attributes is not null)
        {
            WriteSpace();
            EmitImportAttributes(node.Attributes.AsImportAttributes()!);
        }
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitImportAttributes(ImportAttributes node)
    {
        var state = EnterNode(node.AsNode());
        EmitToken(node.Token, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitList(EmitImportAttributeNode, node.AsNode(), node.Attributes, ListFormat.ImportAttributes);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitImportAttribute(ImportAttribute node)
    {
        var state = EnterNode(node.AsNode());
        EmitImportAttributeName(node.Name()!);
        WritePunctuation(":");
        WriteSpace();
        var value = node.Value!;
        if ((_emitContext.EmitFlags(node.Value!) & EmitFlags.NoLeadingComments) == 0)
        {
            var commentRange = _emitContext.CommentRange(value);
            EmitTrailingComments(commentRange.Pos(), CommentSeparator.After);
        }
        EmitExpression(value, OperatorPrecedenceDisallowComma);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitImportAttributeNode(Node node)
    {
        EmitImportAttribute(node.AsImportAttribute()!);
    }

    internal void EmitNamespaceExportDeclaration(NamespaceExportDeclaration node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.ExportKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();
        pos = EmitToken(Kind.AsKeyword, pos, WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitToken(Kind.NamespaceKeyword, pos, WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitBindingIdentifier(node.Name()!.AsIdentifier()!);
        WriteTrailingSemicolon();
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNamespaceExport(NamespaceExport node)
    {
        var state = EnterNode(node.AsNode());
        var pos = EmitToken(Kind.AsteriskToken, node.Pos(), WriteKind.Punctuation, node.AsNode());
        WriteSpace();
        EmitToken(Kind.AsKeyword, pos, WriteKind.Keyword, node.AsNode());
        WriteSpace();
        EmitModuleExportName(node.Name());
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNamedExports(NamedExports node)
    {
        var state = EnterNode(node.AsNode());
        WritePunctuation("{");
        EmitList(EmitExportSpecifierNode, node.AsNode(), node.Elements, ListFormat.NamedImportsOrExportsElements);
        WritePunctuation("}");
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNamedExportBindings(Node node)
    {
        switch (node.Kind)
        {
            case Kind.NamespaceExport:
                EmitNamespaceExport(node.AsNamespaceExport()!);
                break;
            case Kind.NamedExports:
                EmitNamedExports(node.AsNamedExports()!);
                break;
            default:
                Debug.Fail($"unhandled NamedExportBindings: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitExportSpecifier(ExportSpecifier node)
    {
        var state = EnterNode(node.AsNode());
        if (node.IsTypeOnly)
        {
            WriteKeyword("type");
            WriteSpace();
        }
        if (node.PropertyName is not null)
        {
            EmitModuleExportName(node.PropertyName);
            WriteSpace();
            EmitToken(Kind.AsKeyword, node.PropertyName.End(), WriteKind.Keyword, node.AsNode());
            WriteSpace();
        }
        EmitModuleExportName(node.Name());
        ExitNode(node.AsNode(), state);
    }

    internal void EmitExportSpecifierNode(Node node)
    {
        EmitExportSpecifier(node.AsExportSpecifier()!);
    }

    internal void EmitEmbeddedStatement(Node parentNode, Node node)
    {
        if (IsBlock(node) ||
            ShouldEmitOnSingleLine(parentNode) ||
            Options.PreserveSourceNewlines && GetLeadingLineTerminatorCount(parentNode, node, ListFormat.None) == 0)
        {
            WriteSpace();
            EmitStatement(node);
        }
        else
        {
            WriteLine();
            IncreaseIndent();
            if (node.Kind == Kind.EmptyStatement)
            {
                EmitEmptyStatement(node.AsEmptyStatement()!, true /*isEmbeddedStatement*/);
            }
            else
            {
                EmitStatement(node);
            }
            DecreaseIndent();
        }
    }

    internal void EmitStatement(Node node)
    {
        if (_emitContext.SnippetElement(node) is { } snippetElement)
        {
            EmitSnippetNode(node, snippetElement);
            return;
        }

        switch (node.Kind)
        {
            // Statements
            case Kind.Block:
                EmitBlock(node.AsBlock()!);
                break;
            case Kind.EmptyStatement:
                EmitEmptyStatement(node.AsEmptyStatement()!, false /*isEmbeddedStatement*/);
                break;
            case Kind.VariableStatement:
                EmitVariableStatement(node.AsVariableStatement()!);
                break;
            case Kind.ExpressionStatement:
                EmitExpressionStatement(node.AsExpressionStatement()!);
                break;
            case Kind.IfStatement:
                EmitIfStatement(node.AsIfStatement()!);
                break;
            case Kind.DoStatement:
                EmitDoStatement(node.AsDoStatement()!);
                break;
            case Kind.WhileStatement:
                EmitWhileStatement(node.AsWhileStatement()!);
                break;
            case Kind.ForStatement:
                EmitForStatement(node.AsForStatement()!);
                break;
            case Kind.ForInStatement:
                EmitForInStatement(node.AsForInOrOfStatement()!);
                break;
            case Kind.ForOfStatement:
                EmitForOfStatement(node.AsForInOrOfStatement()!);
                break;
            case Kind.ContinueStatement:
                EmitContinueStatement(node.AsContinueStatement()!);
                break;
            case Kind.BreakStatement:
                EmitBreakStatement(node.AsBreakStatement()!);
                break;
            case Kind.ReturnStatement:
                EmitReturnStatement(node.AsReturnStatement()!);
                break;
            case Kind.WithStatement:
                EmitWithStatement(node.AsWithStatement()!);
                break;
            case Kind.SwitchStatement:
                EmitSwitchStatement(node.AsSwitchStatement()!);
                break;
            case Kind.LabeledStatement:
                EmitLabeledStatement(node.AsLabeledStatement()!);
                break;
            case Kind.ThrowStatement:
                EmitThrowStatement(node.AsThrowStatement()!);
                break;
            case Kind.TryStatement:
                EmitTryStatement(node.AsTryStatement()!);
                break;
            case Kind.DebuggerStatement:
                EmitDebuggerStatement(node.AsDebuggerStatement()!);
                break;
            case Kind.NotEmittedStatement:
                EmitNotEmittedStatement(node.AsNotEmittedStatement()!);
                break;

            // Declaration Statements
            case Kind.FunctionDeclaration:
                EmitFunctionDeclaration(node.AsFunctionDeclaration()!);
                break;
            case Kind.ClassDeclaration:
                EmitClassDeclaration(node.AsClassDeclaration()!);
                break;
            case Kind.InterfaceDeclaration:
                EmitInterfaceDeclaration(node.AsInterfaceDeclaration()!);
                break;
            case Kind.TypeAliasDeclaration or Kind.JSTypeAliasDeclaration:
                EmitTypeAliasDeclaration(node.AsTypeAliasDeclaration()!);
                break;
            case Kind.EnumDeclaration:
                EmitEnumDeclaration(node.AsEnumDeclaration()!);
                break;
            case Kind.ModuleDeclaration:
                EmitModuleDeclaration(node.AsModuleDeclaration()!);
                break;
            case Kind.MissingDeclaration:
                // Missing declarations do not emit a statement.
                break;

            // Import/Export Statements
            case Kind.NamespaceExportDeclaration:
                EmitNamespaceExportDeclaration(node.AsNamespaceExportDeclaration()!);
                break;
            case Kind.ImportEqualsDeclaration:
                EmitImportEqualsDeclaration(node.AsImportEqualsDeclaration()!);
                break;
            case Kind.ImportDeclaration:
                EmitImportDeclaration(node.AsImportDeclaration()!);
                break;
            case Kind.ExportAssignment:
                EmitExportAssignment(node.AsExportAssignment()!);
                break;
            case Kind.ExportDeclaration:
                EmitExportDeclaration(node.AsExportDeclaration()!);
                break;

            default:
                Debug.Fail($"unhandled statement: {node.Kind.String()}");
                break;
        }
    }

    //
    // Module references
    //

    internal void EmitExternalModuleReference(ExternalModuleReference node)
    {
        var state = EnterNode(node.AsNode());
        WriteKeyword("require");
        WritePunctuation("(");
        EmitExpression(node.Expression!, OperatorPrecedenceDisallowComma);
        WritePunctuation(")");
        ExitNode(node.AsNode(), state);
    }
}
