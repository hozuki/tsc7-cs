// ported from: internal/printer/printer.go:4254-4521（JSX + 子句）
//
// 与 Go 的偏离（逐条）：
// 1) Go 的联合别名（`JsxChild`/`JsxAttributeLike`/`JsxTagNameExpression`/`JsxAttributeName`/`JsxAttributeValue`/
//    `HeritageClauseElement`/`CaseOrDefaultClauseNode`/`Statement` 都 = `Node`；`TypeArgumentList` = `NodeList`）
//    在 C# 里就是 `Node`/`NodeList`，故这些方法按别名书写签名；`CaseOrDefaultClause`/`HeritageClause`/
//    `KeywordExpression` 等是 Go 的具体结构体，C# 用生成的同名类。
// 2) Go 的方法值 `p.emitList((*Printer).emitJsxChild, …)` → C# 方法组 + `NodeEmitter` 委托（在 Printer.Lists.cs 声明）。
// 3) Go 里由字段契约/类型断言保证非 nil 的 `Node?` 字段（`node.TagName`/`node.Attributes`/`node.Expression`/
//    `node.Name()`）在 C# 调用点加 `!`（nil 时抛 NRE，与 Go 解引用 nil 的 panic 对应）。
// 4) `len(node.Attributes.Properties()) > 0` / `len(node.Statements.Nodes) == 1`（Go 的 nil 切片与空切片同义）→
//    C# 分别用 `?.Length ?? 0` 与列表模式 `{ Length: 1 }` 显式判空（docs §3.2）。
// 5) Go 的 `format &^= LFMultiLine | LFIndented` → C# 的 `format &= ~(…)`。
// 6) `panic(fmt.Sprintf("unhandled …: %v", node.Kind))` → `Debug.Fail($"unhandled …: {node.Kind.String()}")`
//    （Go 的 `default` 分支本就位于各 switch 末尾，C# 保持最后）。
// 7) `emitToken`/`emitTokenNode`/`writeTokenText`/`emitList`/`emitExpression`/`emitStatement`/`emitTypeArguments`/
//    `emitIdentifierName`/`emitIdentifierReference`/`emitKeywordExpression`/`emitPropertyAccessExpression`/
//    `emitStringLiteral`/`emitTypeReference`/`emitExpressionWithTypeArguments`/`enterNode`/`exitNode` 由同
//    namespace 的兄弟分片提供，此处按契约名调用。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    internal void EmitJsxElement(JsxElement node)
    {
        var state = EnterNode(node);
        EmitJsxOpeningElement(node.OpeningElement!.AsJsxOpeningElement()!);
        EmitList(EmitJsxChild, node, node.Children, ListFormat.JsxElementOrFragmentChildren);
        EmitJsxClosingElement(node.ClosingElement!.AsJsxClosingElement()!);
        ExitNode(node, state);
    }

    internal void EmitJsxSelfClosingElement(JsxSelfClosingElement node)
    {
        var state = EnterNode(node);
        WritePunctuation("<");
        EmitJsxTagName(node.TagName!);
        EmitTypeArguments(node, node.TypeArguments);
        WriteSpace();
        EmitJsxAttributes(node.Attributes!.AsJsxAttributes()!);
        WritePunctuation("/>");
        ExitNode(node, state);
    }

    internal void EmitJsxFragment(JsxFragment node)
    {
        var state = EnterNode(node);
        EmitJsxOpeningFragment(node.OpeningFragment!.AsJsxOpeningFragment()!);
        EmitList(EmitJsxChild, node, node.Children, ListFormat.JsxElementOrFragmentChildren);
        EmitJsxClosingFragment(node.ClosingFragment!.AsJsxClosingFragment()!);
        ExitNode(node, state);
    }

    internal void EmitJsxOpeningElement(JsxOpeningElement node)
    {
        var state = EnterNode(node);
        WritePunctuation("<");
        var indented = WriteLineSeparatorsAndIndentBefore(node.TagName!, node);
        EmitJsxTagName(node.TagName!);
        EmitTypeArguments(node, node.TypeArguments);
        if ((node.Attributes!.Properties()?.Length ?? 0) > 0)
        {
            WriteSpace();
        }
        EmitJsxAttributes(node.Attributes!.AsJsxAttributes()!);
        WriteLineSeparatorsAfter(node.Attributes!, node);
        DecreaseIndentIf(indented);
        WritePunctuation(">");
        ExitNode(node, state);
    }

    internal void EmitJsxClosingElement(JsxClosingElement node)
    {
        var state = EnterNode(node);
        WritePunctuation("</");
        EmitJsxTagName(node.TagName!);
        WritePunctuation(">");
        ExitNode(node, state);
    }

    internal void EmitJsxOpeningFragment(JsxOpeningFragment node)
    {
        var state = EnterNode(node);
        WritePunctuation("<");
        WritePunctuation(">");
        ExitNode(node, state);
    }

    internal void EmitJsxClosingFragment(JsxClosingFragment node)
    {
        var state = EnterNode(node);
        WritePunctuation("</");
        WritePunctuation(">");
        ExitNode(node, state);
    }

    internal void EmitJsxText(JsxText node)
    {
        var state = EnterNode(node);
        // TODO(rbuckton): Should this be using `getLiteralTextOfNode` instead?
        WriteLiteral(node.Text);
        ExitNode(node, state);
    }

    internal void EmitJsxAttributes(JsxAttributes node)
    {
        var state = EnterNode(node);
        EmitList(EmitJsxAttributeLike, node, node.Properties, ListFormat.JsxElementAttributes);
        ExitNode(node, state);
    }

    internal void EmitJsxAttribute(JsxAttribute node)
    {
        var state = EnterNode(node);
        EmitJsxAttributeName(node.Name()!);
        if (node.Initializer is not null)
        {
            WritePunctuation("=");
            EmitJsxAttributeValue(node.Initializer);
        }
        ExitNode(node, state);
    }

    internal void EmitJsxSpreadAttribute(JsxSpreadAttribute node)
    {
        var state = EnterNode(node);
        WritePunctuation("{...");
        EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        WritePunctuation("}");
        ExitNode(node, state);
    }

    internal void EmitJsxAttributeLike(Node node)
    {
        switch (node.Kind)
        {
            case Kind.JsxAttribute:
                EmitJsxAttribute(node.AsJsxAttribute()!);
                break;
            case Kind.JsxSpreadAttribute:
                EmitJsxSpreadAttribute(node.AsJsxSpreadAttribute()!);
                break;
            default:
                Debug.Fail($"unhandled JsxAttributeLike: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitJsxExpression(JsxExpression node)
    {
        var state = EnterNode(node);
        // preserve empty expressions if they contain comments!
        if (node.Expression is not null ||
            !_commentsDisabled && !NodeIsSynthesized(node) && HasCommentsAtPosition(node.Pos()))
        {
            var indented = _currentSourceFile is not null && !NodeIsSynthesized(node) &&
                GetLinesBetweenPositions(_currentSourceFile, node.Pos(), node.End()) != 0;
            IncreaseIndentIf(indented);
            var end = EmitToken(Kind.OpenBraceToken, node.Pos(), WriteKind.Punctuation, node);
            EmitTokenNode(node.DotDotDotToken);
            if (node.Expression is not null)
            {
                EmitExpression(node.Expression, OperatorPrecedenceDisallowComma);
            }
            EmitToken(Kind.CloseBraceToken, GreatestEnd(end, node.Expression, node.DotDotDotToken), WriteKind.Punctuation, node);
            DecreaseIndentIf(indented);
        }
        ExitNode(node, state);
    }

    internal void EmitJsxNamespacedName(JsxNamespacedName node)
    {
        var state = EnterNode(node);
        EmitIdentifierName(node.Namespace!.AsIdentifier()!);
        WritePunctuation(":");
        EmitIdentifierName(node.Name()!.AsIdentifier()!);
        ExitNode(node, state);
    }

    internal void EmitJsxChild(Node node)
    {
        switch (node.Kind)
        {
            case Kind.JsxText:
                EmitJsxText(node.AsJsxText()!);
                break;
            case Kind.JsxExpression:
                EmitJsxExpression(node.AsJsxExpression()!);
                break;
            case Kind.JsxElement:
                EmitJsxElement(node.AsJsxElement()!);
                break;
            case Kind.JsxSelfClosingElement:
                EmitJsxSelfClosingElement(node.AsJsxSelfClosingElement()!);
                break;
            case Kind.JsxFragment:
                EmitJsxFragment(node.AsJsxFragment()!);
                break;
            default:
                Debug.Fail($"unhandled JsxChild: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitJsxTagName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierReference(node.AsIdentifier()!);
                break;
            case Kind.ThisKeyword:
                EmitKeywordExpression(node.AsKeywordExpression()!);
                break;
            case Kind.JsxNamespacedName:
                EmitJsxNamespacedName(node.AsJsxNamespacedName()!);
                break;
            case Kind.PropertyAccessExpression:
                EmitPropertyAccessExpression(node.AsPropertyAccessExpression()!);
                break;
            default:
                Debug.Fail($"unhandled JsxTagName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitJsxAttributeName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierName(node.AsIdentifier()!);
                break;
            case Kind.JsxNamespacedName:
                EmitJsxNamespacedName(node.AsJsxNamespacedName()!);
                break;
            default:
                Debug.Fail($"unhandled JsxAttributeName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitJsxAttributeValue(Node node)
    {
        switch (node.Kind)
        {
            case Kind.StringLiteral:
                EmitStringLiteral(node.AsStringLiteral()!);
                break;
            case Kind.JsxExpression:
                EmitJsxExpression(node.AsJsxExpression()!);
                break;
            case Kind.JsxElement:
                EmitJsxElement(node.AsJsxElement()!);
                break;
            case Kind.JsxSelfClosingElement:
                EmitJsxSelfClosingElement(node.AsJsxSelfClosingElement()!);
                break;
            case Kind.JsxFragment:
                EmitJsxFragment(node.AsJsxFragment()!);
                break;
            default:
                EmitExpression(node, OperatorPrecedenceLowest);
                break;
        }
    }

    //
    // Clauses
    //

    internal void EmitCaseOrDefaultClauseStatements(CaseOrDefaultClause node, int colonPos)
    {
        var statements = node.Statements!.Nodes;
        var emitAsSingleStatement = statements is { Length: 1 } &&
            // treat synthesized nodes as located on the same line for emit purposes
            (_currentSourceFile is null ||
                NodeIsSynthesized(node) ||
                NodeIsSynthesized(statements[0]) ||
                RangeStartPositionsAreOnSameLine(node.Loc, statements[0].Loc, _currentSourceFile));

        var format = ListFormat.CaseOrDefaultClauseStatements;
        if (emitAsSingleStatement)
        {
            // When emitting as a single statement, use writeToken (no comments) for the colon
            // to avoid duplicating trailing comments that will be picked up by the statement list.
            WriteTokenText(Kind.ColonToken, WriteKind.Punctuation, colonPos);
            WriteSpace();
            format &= ~(ListFormat.MultiLine | ListFormat.Indented);
        }
        else
        {
            EmitToken(Kind.ColonToken, colonPos, WriteKind.Punctuation, node);
        }

        EmitList(EmitStatement, node, node.Statements, format);
    }

    internal void EmitCaseClause(CaseOrDefaultClause node)
    {
        var state = EnterNode(node);
        EmitToken(Kind.CaseKeyword, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        EmitExpression(node.Expression!, OperatorPrecedenceLowest);
        EmitCaseOrDefaultClauseStatements(node, node.Expression!.End());
        ExitNode(node, state);
    }

    internal void EmitDefaultClause(CaseOrDefaultClause node)
    {
        var state = EnterNode(node);
        var pos = EmitToken(Kind.DefaultKeyword, node.Pos(), WriteKind.Keyword, node);
        EmitCaseOrDefaultClauseStatements(node, pos);
        ExitNode(node, state);
    }

    internal void EmitCaseOrDefaultClauseNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.CaseClause:
                EmitCaseClause(node.AsCaseOrDefaultClause()!);
                break;
            case Kind.DefaultClause:
                EmitDefaultClause(node.AsCaseOrDefaultClause()!);
                break;
            default:
                Debug.Fail($"unhandled CaseOrDefaultClause: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitHeritageClause(HeritageClause node)
    {
        var state = EnterNode(node);
        WriteSpace();
        EmitToken(node.Token, node.Pos(), WriteKind.Keyword, node);
        WriteSpace();
        EmitList(EmitHeritageClauseElement, node, node.Types, ListFormat.HeritageClauseTypes);
        ExitNode(node, state);
    }

    internal void EmitHeritageClauseElement(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ExpressionWithTypeArguments:
                EmitExpressionWithTypeArguments(node.AsExpressionWithTypeArguments()!);
                break;
            case Kind.TypeReference:
                EmitTypeReference(node.AsTypeReferenceNode()!);
                break;
            default:
                Debug.Fail($"unhandled HeritageClauseElement: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitHeritageClauseNode(Node node)
    {
        EmitHeritageClause(node.AsHeritageClause()!);
    }
}
