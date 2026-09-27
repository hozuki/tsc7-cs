// ported from: internal/printer/printer.go:2439-3360（JSDoc 类型包装 + 表达式）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的联合别名（`Expression`/`Statement`/`ConciseBody`/`BlockOrExpression`/`TemplateSpanNode`/
//    `MemberName`/`IdentifierNode`/`TokenNode`/`KeywordExpression` 参数里的 `*ast.Node` 家族）
//    → C# 的 `Node` / 具体生成类（`JSDocNonNullableType`、`TemplateSpan`、`KeywordExpression`…），
//    与 dispatcher（Printer.Emit.cs 的 `Write`）传进来的实参类型一致。
// 2) Go 的命名多返回值 `getBinaryExpressionPrecedence(node) (leftPrec, rightPrec OperatorPrecedence)`
//    → C# 元组 `(OperatorPrecedence LeftPrec, OperatorPrecedence RightPrec)`：调用点
//    `var (leftPrec, rightPrec) = GetBinaryExpressionPrecedence(node);` 与 Go 的
//    `leftPrec, rightPrec := p.getBinaryExpressionPrecedence(node)` 逐字对应（票面要求二选一，取元组）。
//    `getLiteralKindOfBinaryPlusOperand` 仍返回单个 `Kind`（Go 的 `ast.Kind`）。
// 3) Go 的无标签 `switch { case ast.IsXxx(node): … }`（emitConciseBody）→ C# 的 `if`/`else if` 链。
// 4) Go 的局部 `type entry struct { node *ast.PartiallyEmittedExpression; state printerState }` +
//    `core.Stack[entry]`（emitPartiallyEmittedExpression）→ C# 的
//    `Stack<(PartiallyEmittedExpression Node, PrinterState State)>`；`stack.Len()` → `stack.Count`。
// 5) `slices.ContainsFunc(p.emitContext.GetSyntheticLeadingComments(node), p.syntheticCommentWillEmitNewLine)`
//    → 显式 `foreach` + `SyntheticCommentWillEmitNewLine`（同分片禁 LINQ，语义相同：任一命中即真）。
// 6) `strings.Contains(text, x)` → `text.Contains(x)`（String.Contains(string) 默认 ordinal，与本路径上的 ASCII 标记一致）。
// 7) Go 里由类型断言/字段契约保证非 nil 的 `Node?`（`node.Expression`/`node.Type`/`node.Name()`/`node.Body`…）
//    在 C# 调用点加 `!`（nil 时抛 NRE，与 Go 解引用 nil 的 panic 对应）；
//    Go 自身做了 `if node == nil { return }` 的兄弟分片函数（emitTokenNode/emitTypeNode/emitMemberName/
//    emitIdentifierNameNode/generateNameIfNeeded/emitFunctionBodyNode…）按其 C# 契约传可空实参，不加 `!`。
// 8) `panic(fmt.Sprintf("unhandled precedence: %v", precedence))` → `Debug.Fail($"unhandled precedence: {(int)precedence}")`
//    （C# 的 OperatorPrecedence 没有 stringer，按票面规则对整型用 `(int)`；`%v` 打在 Kind 上时用 `node.Kind.String()`）。
// 9) Go 的 `default:` 在 switch 里的位置随意，C# 统一放最后（emitTemplateLiteral、emitExpression、
//    getBinaryExpressionPrecedence、emitConciseBody 四处）。
// 10) `CommentRange`（Ast/AstTypes.cs）是引用类型，字段 `Range`/`Kind`/`HasTrailingNewLine`：
//     Go 的 `comment.Pos()` → `comment.Range.Pos()`。
// 11) JSDoc 分片函数按票面归属写在本文件（printer.go:2439 起），由 TypeShard 的 `emitTypeNode` 调用；
//     TypeShard 只保留调用点（Printer.Types.cs 底部有同款说明），全仓这 5 个成员只此一处定义。
// 12) 本文件不 `using TypeScript.Core;`：该命名空间里新落的 `Stack<T>` 会与 `System.Collections.Generic.Stack<T>`
//     对裸 `Stack<…>` 产生 CS0104（emitPartiallyEmittedExpression 的展开栈）；本文件只用到 `Core.Core` 的
//     静态成员（IfElse/NewTextRange），走 `using static TypeScript.Core.Core;` 即可。
using System.Collections.Generic;
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    //
    // JSDoc types
    //

    internal void EmitJSDocAllType(Node node)
    {
        EmitKeywordNode(node);
    }

    internal void EmitJSDocNonNullableType(JSDocNonNullableType node)
    {
        var state = EnterNode(node);
        WritePunctuation("!");
        EmitTypeNode(node.Type!, TypePrecedence.NonArray);
        ExitNode(node, state);
    }

    internal void EmitJSDocNullableType(JSDocNullableType node)
    {
        var state = EnterNode(node);
        WritePunctuation("?");
        EmitTypeNode(node.Type!, TypePrecedence.NonArray);
        ExitNode(node, state);
    }

    internal void EmitJSDocOptionalType(JSDocOptionalType node)
    {
        var state = EnterNode(node);
        EmitTypeNode(node.Type!, TypePrecedence.JSDoc);
        WritePunctuation("=");
        ExitNode(node, state);
    }

    internal void EmitJSDocVariadicType(JSDocVariadicType node)
    {
        var state = EnterNode(node);
        WritePunctuation("...");
        EmitTypeNode(node.Type!, TypePrecedence.JSDoc);
        ExitNode(node, state);
    }

    //
    // Expressions
    //

    internal void EmitKeywordExpression(KeywordExpression node)
    {
        EmitKeywordNode(node);
    }

    internal void EmitArrayLiteralExpressionElement(Node node)
    {
        EmitExpression(node, OperatorPrecedence.Spread);
    }

    internal void EmitArrayLiteralExpression(ArrayLiteralExpression node)
    {
        var state = EnterNode(node);
        EmitList(EmitArrayLiteralExpressionElement, node, node.Elements, ListFormat.ArrayLiteralExpressionElements | IfElse(node.MultiLine, ListFormat.PreferNewLine, ListFormat.None));
        ExitNode(node, state);
    }

    internal void EmitObjectLiteralExpression(ObjectLiteralExpression node)
    {
        var state = EnterNode(node);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        GenerateAllMemberNames(node.Properties);
        EmitList(EmitObjectLiteralElement, node, node.Properties, ListFormat.ObjectLiteralExpressionProperties |
            IfElse(node.MultiLine, ListFormat.PreferNewLine, ListFormat.None) |
            IfElse(ShouldAllowTrailingComma(node, node.Properties), ListFormat.AllowTrailingComma, ListFormat.None));
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    // 1..toString is a valid property access, emit a dot after the literal
    // Also emit a dot if expression is a integer const enum value - it will appear in generated code as numeric literal
    internal bool MayNeedDotDotForPropertyAccess(Node expression)
    {
        expression = SkipPartiallyEmittedExpressions(expression)!;
        if (IsNumericLiteral(expression))
        {
            // check if numeric literal is a decimal literal that was originally written with a dot
            var text = GetLiteralTextOfNode(expression /*sourceFile*/, null, GetLiteralTextFlags.NeverAsciiEscape);
            // If the number will be printed verbatim and it doesn't already contain a dot or an exponent indicator, add one
            // if the expression doesn't have any comments that will be emitted.
            return (expression.AsNumericLiteral()!.TokenFlags & TokenFlags.WithSpecifier) == 0 &&
                !text.Contains(TokenToString(Kind.DotToken)) &&
                !text.Contains("E") &&
                !text.Contains("e");
        }
        return false;
    }

    internal void EmitPropertyAccessExpression(PropertyAccessExpression node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Expression!, IsOptionalChain(node) ? OperatorPrecedence.OptionalChain : OperatorPrecedence.Member);
        var token = node.QuestionDotToken;
        if (token is null)
        {
            token = _emitContext.Factory.NewToken(Kind.DotToken);
            token.Loc = NewTextRange(node.Expression!.End(), node.Name()!.Pos());
            _emitContext.AddEmitFlags(token, EmitFlags.NoSourceMap);
        }
        var linesBeforeDot = GetLinesBetweenNodes(node, node.Expression!, token);
        WriteLineRepeat(linesBeforeDot);
        IncreaseIndentIf(linesBeforeDot > 0);
        var shouldEmitDotDot = token.Kind != Kind.QuestionDotToken &&
            MayNeedDotDotForPropertyAccess(node.Expression!) &&
            !_writer.HasTrailingComment() &&
            !_writer.HasTrailingWhitespace();
        if (shouldEmitDotDot)
        {
            WritePunctuation(".");
        }
        if (node.QuestionDotToken is not null)
        {
            EmitTokenNode(token);
        }
        else
        {
            EmitToken(Kind.DotToken, node.Expression!.End(), WriteKind.Punctuation, node);
        }
        var linesAfterDot = GetLinesBetweenNodes(node, token, node.Name()!);
        WriteLineRepeat(linesAfterDot);
        IncreaseIndentIf(linesAfterDot > 0);
        EmitMemberName(node.Name());
        DecreaseIndentIf(linesAfterDot > 0);
        DecreaseIndentIf(linesBeforeDot > 0);
        ExitNode(node, state);
    }

    internal void EmitElementAccessExpression(ElementAccessExpression node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Expression!, IsOptionalChain(node) ? OperatorPrecedence.OptionalChain : OperatorPrecedence.Member);
        EmitTokenNode(node.QuestionDotToken);
        EmitToken(Kind.OpenBracketToken, GreatestEnd(-1, node.Expression, node.QuestionDotToken), WriteKind.Punctuation, node);
        EmitExpression(node.ArgumentExpression!, OperatorPrecedence.Comma);
        EmitToken(Kind.CloseBracketToken, node.ArgumentExpression!.End(), WriteKind.Punctuation, node);
        ExitNode(node, state);
    }

    internal void EmitArgument(Node node)
    {
        EmitExpression(node, OperatorPrecedence.Spread);
    }

    internal void EmitCallee(Node callee, Node parentNode)
    {
        if (ShouldEmitIndirectCall(parentNode))
        {
            WritePunctuation("(");
            WriteLiteral("0");
            WritePunctuation(",");
            WriteSpace();
            EmitExpression(callee, OperatorPrecedence.Comma);
            WritePunctuation(")");
        }
        else if (parentNode.Kind == Kind.CallExpression && IsNewExpressionWithoutArguments(SkipPartiallyEmittedExpressions(callee)!))
        {
            // Parenthesize `new C` inside of a CallExpression so it is treated as `(new C)()` and not `new C()`
            EmitExpression(callee, OperatorPrecedence.Parentheses);
        }
        else
        {
            EmitExpression(callee, IsOptionalChain(parentNode) ? OperatorPrecedence.OptionalChain : OperatorPrecedence.Member);
        }
    }

    internal void EmitCallExpression(CallExpression node)
    {
        var state = EnterNode(node);
        EmitCallee(node.Expression!, node);
        EmitTokenNode(node.QuestionDotToken);
        EmitTypeArguments(node, node.TypeArguments);
        EmitList(EmitArgument, node, node.Arguments, ListFormat.CallExpressionArguments);
        ExitNode(node, state);
    }

    internal void EmitNewExpression(NewExpression node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.NewKeyword, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        if (SkipPartiallyEmittedExpressions(node.Expression)!.Kind == Kind.CallExpression)
        {
            // Parenthesize `C()` inside of a NewExpression so it is treated as `new (C())` and not `new C()`
            EmitExpression(node.Expression!, OperatorPrecedence.Parentheses);
        }
        else
        {
            EmitExpression(node.Expression!, OperatorPrecedence.Member);
        }
        EmitTypeArguments(node, node.TypeArguments);
        EmitList(EmitArgument, node, node.Arguments, ListFormat.NewExpressionArguments);
        ExitNode(node, state);
    }

    internal void EmitTemplateLiteral(Node node)
    {
        switch (node.Kind)
        {
            case Kind.NoSubstitutionTemplateLiteral:
                EmitNoSubstitutionTemplateLiteral(node.AsNoSubstitutionTemplateLiteral()!);
                break;
            case Kind.TemplateExpression:
                EmitTemplateExpression(node.AsTemplateExpression()!);
                break;
            default:
                Debug.Fail($"unhandled TemplateLiteral: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        var state = EnterNode(node);
        EmitCallee(node.Tag!, node);
        EmitTypeArguments(node, node.TypeArguments);
        WriteSpace();
        EmitTemplateLiteral(node.Template!);
        ExitNode(node, state);
    }

    internal void EmitTypeAssertionExpression(TypeAssertion node)
    {
        var state = EnterNode(node);
        WritePunctuation("<");
        EmitTypeNodeOutsideExtends(node.Type!);
        WritePunctuation(">");
        EmitExpression(node.Expression!, OperatorPrecedence.Update);
        ExitNode(node, state);
    }

    internal void EmitParenthesizedExpression(ParenthesizedExpression node)
    {
        var state = EnterNode(node);
        var openParenPos = EmitToken(Kind.OpenParenToken, node.Pos(), WriteKind.Punctuation, node);
        var indented = WriteLineSeparatorsAndIndentBefore(node.Expression!, node);
        EmitExpression(node.Expression!, OperatorPrecedence.Comma);
        WriteLineSeparatorsAfter(node.Expression!, node);
        DecreaseIndentIf(indented);
        var closeParenPos = openParenPos;
        if (node.Expression is not null)
        {
            closeParenPos = node.Expression.End();
        }
        EmitToken(Kind.CloseParenToken, closeParenPos, WriteKind.Punctuation, node);
        ExitNode(node, state);
    }

    internal void EmitFunctionExpression(FunctionExpression node)
    {
        var state = EnterNode(node);
        GenerateNameIfNeeded(node.Name());
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        WriteKeyword("function");
        EmitTokenNode(node.AsteriskToken);
        WriteSpace();
        EmitIdentifierNameNode(node.Name());
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitSignature(node);
        EmitFunctionBodyNode(node.Body);
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitConciseBody(Node node)
    {
        if (IsBlock(node))
        {
            EmitFunctionBody(node.AsBlock()!);
        }
        else if (IsObjectLiteralExpression(GetLeftmostExpression(node, false /*stopAtCallExpressions*/)))
        {
            // Wrap in ParenthesizedExpression to ensure parens are emitted after any leading
            // PartiallyEmittedExpression comments, matching TypeScript's factory-time wrapping
            // via parenthesizeConciseBodyOfArrowFunction.
            var paren = _emitContext.Factory.NewParenthesizedExpression(node);
            paren.Loc = node.Loc;
            EmitExpression(paren, OperatorPrecedenceLowest);
        }
        else if (IsExpression(node))
        {
            EmitExpression(node, OperatorPrecedence.Yield);
        }
        else
        {
            Debug.Fail($"unexpected ConciseBody: {node.Kind.String()}");
        }
    }

    internal void EmitArrowFunction(ArrowFunction node)
    {
        var state = EnterNode(node);
        EmitModifierList(node, node.Modifiers(), false /*allowDecorators*/);
        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);
        PushNameGenerationScope(node);
        EmitTypeParameters(node, node.TypeParameters);
        EmitParametersForArrow(node, node.Parameters);
        EmitTypeAnnotation(node.Type);
        WriteSpace();
        EmitTokenNode(node.EqualsGreaterThanToken);
        WriteSpace();
        EmitConciseBody(node.Body!);
        PopNameGenerationScope(node);
        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitDeleteExpression(DeleteExpression node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.DeleteKeyword, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(node.Expression!, OperatorPrecedence.Unary);
        ExitNode(node, state);
    }

    internal void EmitTypeOfExpression(TypeOfExpression node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.TypeOfKeyword, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(node.Expression!, OperatorPrecedence.Unary);
        ExitNode(node, state);
    }

    internal void EmitVoidExpression(VoidExpression node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.VoidKeyword, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(node.Expression!, OperatorPrecedence.Unary);
        ExitNode(node, state);
    }

    internal void EmitAwaitExpression(AwaitExpression node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.AwaitKeyword, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(node.Expression!, OperatorPrecedence.Unary);
        ExitNode(node, state);
    }

    internal void EmitPrefixUnaryExpression(PrefixUnaryExpression node)
    {
        var state = EnterNode(node);
        var @operator = node.Operator;
        var operand = node.Operand!;
        EmitToken(@operator, node.Pos(), WriteKind.Operator, node);

        // In some cases, we need to emit a space between the operator and the operand. One obvious case
        // is when the operator is an identifier, like delete or typeof. We also need to do this for plus
        // and minus expressions in certain cases. Specifically, consider the following two cases (parens
        // are just for clarity of exposition, and not part of the source code):
        //
        //  (+(+1))
        //  (+(++1))
        //
        // We need to emit a space in both cases. In the first case, the absence of a space will make
        // the resulting expression a prefix increment operation. And in the second, it will make the resulting
        // expression a prefix increment whose operand is a plus expression - (++(+x))
        // The same is true of minus of course.
        if (operand.Kind == Kind.PrefixUnaryExpression)
        {
            var inner = operand.AsPrefixUnaryExpression()!.Operator;
            if ((@operator == Kind.PlusToken && (inner == Kind.PlusToken || inner == Kind.PlusPlusToken)) ||
                (@operator == Kind.MinusToken && (inner == Kind.MinusToken || inner == Kind.MinusMinusToken)))
            {
                WriteSpace();
            }
        }

        EmitExpression(node.Operand!, OperatorPrecedence.Unary);
        ExitNode(node, state);
    }

    internal void EmitPostfixUnaryExpression(PostfixUnaryExpression node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Operand!, OperatorPrecedence.LeftHandSide);
        EmitToken(node.Operator, node.Operand!.End(), WriteKind.Operator, node);
        ExitNode(node, state);
    }

    // This function determines whether an expression consists of a homogeneous set of
    // literal expressions or binary plus expressions that all share the same literal kind.
    // It is used to determine whether the right-hand operand of a binary plus expression can be
    // emitted without parentheses.
    internal Kind GetLiteralKindOfBinaryPlusOperand(Node node)
    {
        node = SkipPartiallyEmittedExpressions(node)!;

        if (IsLiteralKind(node.Kind))
        {
            return node.Kind;
        }

        if (node.Kind == Kind.BinaryExpression)
        {
            var n = node.AsBinaryExpression()!;
            if (n.OperatorToken!.Kind == Kind.PlusToken)
            {
                // !!! Determine if caching this is worthwhile over recomputing
                ////if n.cachedLiteralKind != KindUnknown {
                ////	return n.cachedLiteralKind;
                ////}

                var leftKind = GetLiteralKindOfBinaryPlusOperand(n.Left!);
                var literalKind = Kind.Unknown;
                if (IsLiteralKind(leftKind) && leftKind == GetLiteralKindOfBinaryPlusOperand(n.Right!))
                {
                    literalKind = leftKind;
                }

                ////n.cachedLiteralKind = literalKind;
                return literalKind;
            }
        }

        return Kind.Unknown;
    }

    internal (OperatorPrecedence LeftPrec, OperatorPrecedence RightPrec) GetBinaryExpressionPrecedence(BinaryExpression node)
    {
        var precedence = GetExpressionPrecedence(node);
        var leftPrec = precedence;
        var rightPrec = precedence;
        switch (precedence)
        {
            case OperatorPrecedence.Comma:
                // No need to parenthesize the right operand when the binary operator and
                // operand are both ,:
                //  x,(a,b)     => x,a,b
                break;
            case OperatorPrecedence.Assignment:
                // assignment is right-associative
                leftPrec = OperatorPrecedence.Conditional;
                rightPrec = OperatorPrecedence.Yield;
                break;
            case OperatorPrecedence.LogicalOR:
                rightPrec = OperatorPrecedence.LogicalAND;
                break;
            case OperatorPrecedence.LogicalAND:
                rightPrec = OperatorPrecedence.BitwiseOR;
                break;
            case OperatorPrecedence.BitwiseOR:
                // No need to parenthesize the right operand when the binary operator and
                // operand are both | due to the associative property of mathematics:
                //  x|(a|b)     => x|a|b
                break;
            case OperatorPrecedence.BitwiseXOR:
                // No need to parenthesize the right operand when the binary operator and
                // operand are both ^ due to the associative property of mathematics:
                //  x^(a^b)     => x^a^b
                break;
            case OperatorPrecedence.BitwiseAND:
                // No need to parenthesize the right operand when the binary operator and
                // operand are both & due to the associative property of mathematics:
                //  x&(a&b)     => x&a&b
                break;
            case OperatorPrecedence.Equality:
                rightPrec = OperatorPrecedence.Relational;
                break;
            case OperatorPrecedence.Relational:
                rightPrec = OperatorPrecedence.Shift;
                break;
            case OperatorPrecedence.Shift:
                rightPrec = OperatorPrecedence.Additive;
                break;
            case OperatorPrecedence.Additive:
                if (node.OperatorToken!.Kind == Kind.PlusToken && IsBinaryOperation(node.Right!, Kind.PlusToken))
                {
                    var leftKind = GetLiteralKindOfBinaryPlusOperand(node.Left!);
                    if (IsLiteralKind(leftKind) && leftKind == GetLiteralKindOfBinaryPlusOperand(node.Right!))
                    {
                        // No need to parenthesize the right operand when the binary operator
                        // is plus (+) if both the left and right operands consist solely of either
                        // literals of the same kind or binary plus (+) expressions for literals of
                        // the same kind (recursively).
                        //  "a"+(1+2)       => "a"+(1+2)
                        //  "a"+("b"+"c")   => "a"+"b"+"c"
                        break;
                    }
                }
                rightPrec = OperatorPrecedence.Multiplicative;
                break;
            case OperatorPrecedence.Multiplicative:
                if (node.OperatorToken!.Kind == Kind.AsteriskToken && IsBinaryOperation(node.Right!, Kind.AsteriskToken))
                {
                    // No need to parenthesize the right operand when the binary operator and
                    // operand are both * due to the associative property of mathematics:
                    //  x*(a*b)     => x*a*b
                    break;
                }
                rightPrec = OperatorPrecedence.Exponentiation;
                break;
            case OperatorPrecedence.Exponentiation:
                // exponentiation is right-associative
                leftPrec = OperatorPrecedence.Update;
                break;
            default:
                Debug.Fail($"unhandled precedence: {(int)precedence}");
                break;
        }
        return (leftPrec, rightPrec);
    }

    internal void EmitBinaryExpression(BinaryExpression node)
    {
        var (leftPrec, rightPrec) = GetBinaryExpressionPrecedence(node);
        var emittedLeft = SkipPartiallyEmittedExpressions(node.Left);
        if (NodeIsSynthesized(emittedLeft) && emittedLeft!.Kind == Kind.BinaryExpression && MixingBinaryOperatorsRequiresParentheses(node.OperatorToken!.Kind, emittedLeft.AsBinaryExpression()!.OperatorToken!.Kind))
        {
            leftPrec = OperatorPrecedenceHighest;
        }
        var emittedRight = SkipPartiallyEmittedExpressions(node.Right);
        if (NodeIsSynthesized(emittedRight) && emittedRight!.Kind == Kind.BinaryExpression && MixingBinaryOperatorsRequiresParentheses(node.OperatorToken!.Kind, emittedRight.AsBinaryExpression()!.OperatorToken!.Kind))
        {
            rightPrec = OperatorPrecedenceHighest;
        }
        var state = EnterNode(node);
        EmitExpression(node.Left!, leftPrec);
        var linesBeforeOperator = GetLinesBetweenNodes(node, node.Left!, node.OperatorToken!);
        var linesAfterOperator = GetLinesBetweenNodes(node, node.OperatorToken!, node.Right!);
        WriteLinesAndIndent(linesBeforeOperator, node.OperatorToken!.Kind != Kind.CommaToken /*writeSpaceIfNotIndenting*/);
        EmitTokenNodeEx(node.OperatorToken!, TokenEmitFlags.NoSourceMaps);
        WriteLinesAndIndent(linesAfterOperator, true /*writeSpaceIfNotIndenting*/); // Binary operators should have a space before the comment starts
        EmitExpression(node.Right!, rightPrec);
        DecreaseIndentIf(linesAfterOperator > 0);
        DecreaseIndentIf(linesBeforeOperator > 0);
        ExitNode(node, state);
    }

    internal void EmitShortCircuitExpression(Node node)
    {
        if (IsBinaryOperation(SkipPartiallyEmittedExpressions(node)!, Kind.QuestionQuestionToken))
        {
            EmitExpression(node, OperatorPrecedenceCoalesce);
        }
        else
        {
            EmitExpression(node, OperatorPrecedence.LogicalOR);
        }
    }

    internal void EmitConditionalExpression(ConditionalExpression node)
    {
        var state = EnterNode(node);
        var linesBeforeQuestion = GetLinesBetweenNodes(node, node.Condition!, node.QuestionToken!);
        var linesAfterQuestion = GetLinesBetweenNodes(node, node.QuestionToken!, node.WhenTrue!);
        var linesBeforeColon = GetLinesBetweenNodes(node, node.WhenTrue!, node.ColonToken!);
        var linesAfterColon = GetLinesBetweenNodes(node, node.ColonToken!, node.WhenFalse!);
        EmitShortCircuitExpression(node.Condition!);
        WriteLinesAndIndent(linesBeforeQuestion /*writeSpaceIfNotIndenting*/, true);
        EmitPunctuationNode(node.QuestionToken);
        WriteLinesAndIndent(linesAfterQuestion /*writeSpaceIfNotIndenting*/, true);
        EmitExpression(node.WhenTrue!, OperatorPrecedence.Yield);
        DecreaseIndentIf(linesAfterQuestion > 0);
        DecreaseIndentIf(linesBeforeQuestion > 0);
        WriteLinesAndIndent(linesBeforeColon /*writeSpaceIfNotIndenting*/, true);
        EmitPunctuationNode(node.ColonToken);
        WriteLinesAndIndent(linesAfterColon /*writeSpaceIfNotIndenting*/, true);
        EmitExpression(node.WhenFalse!, OperatorPrecedence.Yield);
        DecreaseIndentIf(linesAfterColon > 0);
        DecreaseIndentIf(linesBeforeColon > 0);
        ExitNode(node, state);
    }

    internal void EmitTemplateExpression(TemplateExpression node)
    {
        var state = EnterNode(node);
        EmitTemplateHead(node.Head!.AsTemplateHead()!);
        EmitList(EmitTemplateSpanNode, node, node.TemplateSpans, ListFormat.TemplateExpressionSpans);
        ExitNode(node, state);
    }

    internal void EmitYieldExpression(YieldExpression node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.YieldKeyword, node.Pos(), WriteKind.Keyword, node);
        EmitPunctuationNode(node.AsteriskToken);
        if (node.Expression is not null)
        {
            WriteSpace();
            EmitExpressionNoASI(node.Expression, OperatorPrecedenceDisallowComma);
        }
        ExitNode(node, state);
    }

    internal void EmitSpreadElement(SpreadElement node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.DotDotDotToken, node.Pos(), WriteKind.Punctuation, node);
        EmitExpression(node.Expression!, OperatorPrecedenceDisallowComma);
        ExitNode(node, state);
    }

    internal void EmitClassExpression(ClassExpression node)
    {
        var state = EnterNode(node);
        GenerateNameIfNeeded(node.Name());

        var pos = EmitModifierList(node, node.Modifiers(), true /*allowDecorators*/);
        EmitToken(Kind.ClassKeyword, pos, WriteKind.Keyword, node);

        if (node.Name() is not null)
        {
            WriteSpace();
            EmitIdentifierName(node.Name()!.AsIdentifier()!);
        }

        var indented = ShouldEmitIndented(node);
        IncreaseIndentIf(indented);

        EmitTypeParameters(node, node.TypeParameters);
        EmitList(EmitHeritageClauseNode, node, node.HeritageClauses, ListFormat.ClassHeritageClauses);
        WriteSpace();
        WritePunctuation("{");
        PushNameGenerationScope(node);
        GenerateAllMemberNames(node.Members);
        EmitList(EmitClassElement, node, node.Members, ListFormat.ClassMembers);
        PopNameGenerationScope(node);
        WritePunctuation("}");

        DecreaseIndentIf(indented);
        ExitNode(node, state);
    }

    internal void EmitOmittedExpression(Node node)
    {
        ExitNode(node, EnterNode(node));
    }

    internal void EmitExpressionWithTypeArguments(ExpressionWithTypeArguments node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Expression!, OperatorPrecedence.Member);
        EmitTypeArguments(node, node.TypeArguments);
        ExitNode(node, state);
    }

    internal void EmitAsExpression(AsExpression node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Expression!, OperatorPrecedence.Relational);
        WriteSpace();
        WriteKeyword("as");
        WriteSpace();
        EmitTypeNodeOutsideExtends(node.Type!);
        ExitNode(node, state);
    }

    internal void EmitSatisfiesExpression(SatisfiesExpression node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Expression!, OperatorPrecedence.Relational);
        WriteSpace();
        WriteKeyword("satisfies");
        WriteSpace();
        EmitTypeNodeOutsideExtends(node.Type!);
        ExitNode(node, state);
    }

    internal void EmitNonNullExpression(NonNullExpression node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Expression!, OperatorPrecedence.Member);
        WriteOperator("!");
        ExitNode(node, state);
    }

    internal void EmitMetaProperty(MetaProperty node)
    {
        var state = EnterNode(node);
        EmitToken(node.KeywordToken, node.Pos(), WriteKind.Punctuation, node);
        WritePunctuation(".");
        EmitIdentifierName(node.Name()!.AsIdentifier()!);
        ExitNode(node, state);
    }

    internal void EmitPartiallyEmittedExpression(PartiallyEmittedExpression node)
    {
        // avoid reprinting parens for nested partially emitted expressions
        var stack = new Stack<(PartiallyEmittedExpression Node, PrinterState State)>();
        while (true)
        {
            var state = EnterNode(node);
            var emitFlags = _emitContext.EmitFlags(node);
            if ((emitFlags & EmitFlags.NoLeadingComments) == 0 && node.Pos() != node.Expression!.Pos())
            {
                EmitTrailingCommentsOfPosition(node.Expression.Pos(), false /*prefixSpace*/, false /*forceNoNewline*/);
            }
            stack.Push((node, state));
            if (!IsPartiallyEmittedExpression(node.Expression!))
            {
                break;
            }
            node = node.Expression!.AsPartiallyEmittedExpression()!;
        }

        EmitExpression(node.Expression!, OperatorPrecedenceLowest);

        // unwind stack
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            var emitFlags = _emitContext.EmitFlags(node);
            if ((emitFlags & EmitFlags.NoTrailingComments) == 0 && node.End() != node.Expression!.End())
            {
                EmitLeadingCommentsOfPosition(node.Expression.End());
            }
            ExitNode(node, entry.State);
            node = entry.Node;
        }
    }

    internal bool CommentWillEmitNewLine(CommentRange comment)
    {
        return comment.Kind == Kind.SingleLineCommentTrivia || comment.HasTrailingNewLine;
    }

    internal bool SyntheticCommentWillEmitNewLine(SynthesizedComment comment)
    {
        return comment.Kind == Kind.SingleLineCommentTrivia || comment.HasTrailingNewLine;
    }

    internal bool WillEmitLeadingNewLine(Node node)
    {
        if (_currentSourceFile is null)
        {
            return false;
        }
        var hasLeadingCommentRanges = false;
        var hasNewLineComment = false;
        foreach (var comment in GetLeadingCommentRanges(_emitContext.Factory.AsNodeFactory(), _currentSourceFile.Text(), node.Pos()))
        {
            hasLeadingCommentRanges = true;
            if (CommentWillEmitNewLine(comment))
            {
                hasNewLineComment = true;
            }
        }
        if (hasLeadingCommentRanges)
        {
            var parseNode = _emitContext.ParseNode(node);
            if (parseNode is not null && IsParenthesizedExpression(parseNode.Parent!))
            {
                return true;
            }
        }
        if (hasNewLineComment)
        {
            return true;
        }
        var syntheticComments = _emitContext.GetSyntheticLeadingComments(node);
        if (syntheticComments is not null)
        {
            foreach (var comment in syntheticComments)
            {
                if (SyntheticCommentWillEmitNewLine(comment))
                {
                    return true;
                }
            }
        }
        if (IsPartiallyEmittedExpression(node))
        {
            var pee = node.AsPartiallyEmittedExpression()!;
            if (node.Pos() != pee.Expression!.Pos())
            {
                foreach (var comment in GetTrailingCommentRanges(_emitContext.Factory.AsNodeFactory(), _currentSourceFile.Text(), pee.Expression.Pos()))
                {
                    if (CommentWillEmitNewLine(comment))
                    {
                        return true;
                    }
                }
            }
            return WillEmitLeadingNewLine(pee.Expression);
        }
        return false;
    }

    // parenthesizeExpressionForNoAsi wraps an expression in parens if we would emit a leading comment
    // that would introduce a line separator between the node and its parent.
    internal Node ParenthesizeExpressionForNoAsi(Node node)
    {
        if (!_commentsDisabled)
        {
            switch (node.Kind)
            {
                case Kind.PartiallyEmittedExpression:
                    if (WillEmitLeadingNewLine(node))
                    {
                        var pee = node.AsPartiallyEmittedExpression()!;
                        var parseNode = _emitContext.ParseNode(node);
                        if (parseNode is not null && IsParenthesizedExpression(parseNode))
                        {
                            // If the original node was a parenthesized expression, restore it to preserve comment and source map emit
                            var parens = _emitContext.Factory.NewParenthesizedExpression(pee.Expression);
                            _emitContext.SetOriginal(parens, node);
                            parens.Loc = parseNode.Loc;
                            return parens;
                        }
                        return _emitContext.Factory.NewParenthesizedExpression(node);
                    }
                    var pee2 = node.AsPartiallyEmittedExpression()!;
                    return _emitContext.Factory.UpdatePartiallyEmittedExpression(
                        pee2,
                        ParenthesizeExpressionForNoAsi(pee2.Expression!));
                case Kind.PropertyAccessExpression:
                    var pae = node.AsPropertyAccessExpression()!;
                    return _emitContext.Factory.UpdatePropertyAccessExpression(
                        pae,
                        ParenthesizeExpressionForNoAsi(pae.Expression!),
                        pae.QuestionDotToken,
                        pae.Name(),
                        pae.Flags);
                case Kind.ElementAccessExpression:
                    var eae = node.AsElementAccessExpression()!;
                    return _emitContext.Factory.UpdateElementAccessExpression(
                        eae,
                        ParenthesizeExpressionForNoAsi(eae.Expression!),
                        eae.QuestionDotToken,
                        eae.ArgumentExpression,
                        eae.Flags);
                case Kind.CallExpression:
                    var ce = node.AsCallExpression()!;
                    return _emitContext.Factory.UpdateCallExpression(
                        ce,
                        ParenthesizeExpressionForNoAsi(ce.Expression!),
                        ce.QuestionDotToken,
                        ce.TypeArguments,
                        ce.Arguments,
                        ce.Flags);
                case Kind.TaggedTemplateExpression:
                    var tte = node.AsTaggedTemplateExpression()!;
                    return _emitContext.Factory.UpdateTaggedTemplateExpression(
                        tte,
                        ParenthesizeExpressionForNoAsi(tte.Tag!),
                        tte.QuestionDotToken,
                        tte.TypeArguments,
                        tte.Template,
                        tte.Flags);
                case Kind.PostfixUnaryExpression:
                    var pue = node.AsPostfixUnaryExpression()!;
                    return _emitContext.Factory.UpdatePostfixUnaryExpression(
                        pue,
                        ParenthesizeExpressionForNoAsi(pue.Operand!),
                        pue.Operator);
                case Kind.BinaryExpression:
                    var be = node.AsBinaryExpression()!;
                    return _emitContext.Factory.UpdateBinaryExpression(
                        be,
                        be.Modifiers(),
                        ParenthesizeExpressionForNoAsi(be.Left!),
                        be.Type,
                        be.OperatorToken,
                        be.Right);
                case Kind.ConditionalExpression:
                    var ce2 = node.AsConditionalExpression()!;
                    return _emitContext.Factory.UpdateConditionalExpression(
                        ce2,
                        ParenthesizeExpressionForNoAsi(ce2.Condition!),
                        ce2.QuestionToken,
                        ce2.WhenTrue,
                        ce2.ColonToken,
                        ce2.WhenFalse);
                case Kind.AsExpression:
                    var ae = node.AsAsExpression()!;
                    return _emitContext.Factory.UpdateAsExpression(
                        ae,
                        ParenthesizeExpressionForNoAsi(ae.Expression!),
                        ae.Type);
                case Kind.SatisfiesExpression:
                    var se = node.AsSatisfiesExpression()!;
                    return _emitContext.Factory.UpdateSatisfiesExpression(
                        se,
                        ParenthesizeExpressionForNoAsi(se.Expression!),
                        se.Type);
                case Kind.NonNullExpression:
                    var nne = node.AsNonNullExpression()!;
                    return _emitContext.Factory.UpdateNonNullExpression(
                        nne,
                        ParenthesizeExpressionForNoAsi(nne.Expression!),
                        nne.Flags);
            }
        }
        return node;
    }

    internal void EmitExpressionNoASI(Node node, OperatorPrecedence precedence)
    {
        node = ParenthesizeExpressionForNoAsi(node);
        EmitExpression(node, precedence);
    }

    internal void EmitExpression(Node node, OperatorPrecedence precedence)
    {
        var parens = GetExpressionPrecedence(SkipPartiallyEmittedExpressions(node)) < precedence;
        if (parens)
        {
            WritePunctuation("(");
        }

        switch (node.Kind)
        {
            // Keywords
            case Kind.TrueKeyword or Kind.FalseKeyword or Kind.NullKeyword:
                EmitTokenNode(node);
                break;
            case Kind.ThisKeyword or Kind.SuperKeyword or Kind.ImportKeyword:
                EmitKeywordExpression(node.AsKeywordExpression()!);
                break;

            // Literals
            case Kind.NumericLiteral:
                EmitNumericLiteral(node.AsNumericLiteral()!);
                break;
            case Kind.BigIntLiteral:
                EmitBigIntLiteral(node.AsBigIntLiteral()!);
                break;
            case Kind.StringLiteral:
                EmitStringLiteral(node.AsStringLiteral()!);
                break;
            case Kind.RegularExpressionLiteral:
                EmitRegularExpressionLiteral(node.AsRegularExpressionLiteral()!);
                break;
            case Kind.NoSubstitutionTemplateLiteral:
                EmitNoSubstitutionTemplateLiteral(node.AsNoSubstitutionTemplateLiteral()!);
                break;

            // Identifiers
            case Kind.Identifier:
                EmitIdentifierReference(node.AsIdentifier()!);
                break;
            case Kind.PrivateIdentifier:
                EmitPrivateIdentifier(node.AsPrivateIdentifier()!);
                break;

            // Expressions
            case Kind.ArrayLiteralExpression:
                EmitArrayLiteralExpression(node.AsArrayLiteralExpression()!);
                break;
            case Kind.ObjectLiteralExpression:
                EmitObjectLiteralExpression(node.AsObjectLiteralExpression()!);
                break;
            case Kind.PropertyAccessExpression:
                EmitPropertyAccessExpression(node.AsPropertyAccessExpression()!);
                break;
            case Kind.ElementAccessExpression:
                EmitElementAccessExpression(node.AsElementAccessExpression()!);
                break;
            case Kind.CallExpression:
                EmitCallExpression(node.AsCallExpression()!);
                break;
            case Kind.NewExpression:
                EmitNewExpression(node.AsNewExpression()!);
                break;
            case Kind.TaggedTemplateExpression:
                EmitTaggedTemplateExpression(node.AsTaggedTemplateExpression()!);
                break;
            case Kind.TypeAssertionExpression:
                EmitTypeAssertionExpression(node.AsTypeAssertion()!);
                break;
            case Kind.ParenthesizedExpression:
                EmitParenthesizedExpression(node.AsParenthesizedExpression()!);
                break;
            case Kind.FunctionExpression:
                EmitFunctionExpression(node.AsFunctionExpression()!);
                break;
            case Kind.ArrowFunction:
                EmitArrowFunction(node.AsArrowFunction()!);
                break;
            case Kind.DeleteExpression:
                EmitDeleteExpression(node.AsDeleteExpression()!);
                break;
            case Kind.TypeOfExpression:
                EmitTypeOfExpression(node.AsTypeOfExpression()!);
                break;
            case Kind.VoidExpression:
                EmitVoidExpression(node.AsVoidExpression()!);
                break;
            case Kind.AwaitExpression:
                EmitAwaitExpression(node.AsAwaitExpression()!);
                break;
            case Kind.PrefixUnaryExpression:
                EmitPrefixUnaryExpression(node.AsPrefixUnaryExpression()!);
                break;
            case Kind.PostfixUnaryExpression:
                EmitPostfixUnaryExpression(node.AsPostfixUnaryExpression()!);
                break;
            case Kind.BinaryExpression:
                EmitBinaryExpression(node.AsBinaryExpression()!);
                break;
            case Kind.ConditionalExpression:
                EmitConditionalExpression(node.AsConditionalExpression()!);
                break;
            case Kind.TemplateExpression:
                EmitTemplateExpression(node.AsTemplateExpression()!);
                break;
            case Kind.YieldExpression:
                EmitYieldExpression(node.AsYieldExpression()!);
                break;
            case Kind.SpreadElement:
                EmitSpreadElement(node.AsSpreadElement()!);
                break;
            case Kind.ClassExpression:
                EmitClassExpression(node.AsClassExpression()!);
                break;
            case Kind.OmittedExpression:
                EmitOmittedExpression(node);
                break;
            case Kind.AsExpression:
                EmitAsExpression(node.AsAsExpression()!);
                break;
            case Kind.NonNullExpression:
                EmitNonNullExpression(node.AsNonNullExpression()!);
                break;
            case Kind.ExpressionWithTypeArguments:
                EmitExpressionWithTypeArguments(node.AsExpressionWithTypeArguments()!);
                break;
            case Kind.SatisfiesExpression:
                EmitSatisfiesExpression(node.AsSatisfiesExpression()!);
                break;
            case Kind.MetaProperty:
                EmitMetaProperty(node.AsMetaProperty()!);
                break;
            case Kind.SyntheticExpression:
                Debug.Fail("SyntheticExpression should never be printed.");
                break;
            case Kind.MissingDeclaration:
                // Missing declarations do not emit an expression.
                break;

            // JSX
            case Kind.JsxElement:
                EmitJsxElement(node.AsJsxElement()!);
                break;
            case Kind.JsxSelfClosingElement:
                EmitJsxSelfClosingElement(node.AsJsxSelfClosingElement()!);
                break;
            case Kind.JsxFragment:
                EmitJsxFragment(node.AsJsxFragment()!);
                break;

            // Synthesized list
            case Kind.SyntaxList:
                Debug.Fail("SyntaxList should not be printed");
                break;

            // Transformation nodes
            case Kind.NotEmittedStatement:
                return;
            case Kind.PartiallyEmittedExpression:
                EmitPartiallyEmittedExpression(node.AsPartiallyEmittedExpression()!);
                break;
            case Kind.SyntheticReferenceExpression:
                Debug.Fail("SyntheticReferenceExpression should not be printed");
                break;

            default:
                Debug.Fail($"unexpected Expression: {node.Kind.String()}");
                break;
        }

        if (parens)
        {
            WritePunctuation(")");
        }
    }

    //
    // Misc
    //

    internal void EmitTemplateSpan(TemplateSpan node)
    {
        var state = EnterNode(node);
        EmitExpression(node.Expression!, OperatorPrecedence.Comma);
        EmitTemplateMiddleTail(node.Literal!);
        ExitNode(node, state);
    }

    internal void EmitTemplateSpanNode(Node node)
    {
        EmitTemplateSpan(node.AsTemplateSpan()!);
    }
}
