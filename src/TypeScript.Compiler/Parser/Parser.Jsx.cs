// ported from: internal/parser/parser.go:5054-6854（JSX 属性/闭合片段、一元与左侧表达式、成员/调用表达式、
//               模板表达式、主表达式、标识符创建、节点收尾与列表辅助、扫描判定（isStartOf*/scan*MemberStart）、
//               上下文标志、pragma 提取与 processPragmasIntoFields、JS 语法检查）
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.1/§3.3/§3.8）：
// 1) 包级函数（`isReservedWord`/`attachFileToDiagnostics`/`getCommentPragmas`/`extractPragmas`/`match`/
//    `skipBlanks`/`skipNonBlanks`/`skipTo`/`lineEndPos`/`extractName`/`extractQuotedString`）落成同一个
//    `partial class Parser` 上的 `internal static` 方法（一个 Go package = 一个 C# 类型，见 §2）。
// 2) `ast.`/`core.`/`scanner.`/`diagnostics.` 的包级函数与常量用 `using static` 后按 Go 的裸名调用：本文件在
//    `TypeScript.Parser` 命名空间里，写限定名（`Ast.IsDecorator`）会先解析到同级的 `TypeScript.Ast`
//    命名空间，而 C# 只对命名空间做成员查找、不落到成员。
// 3) Go 的 `func(p *Parser) *ast.Node` 回调 → `Func<Parser, Node?>`；调用点写 `static p => p.Foo()`（不捕获
//    任何变量，无闭包分配）。
// 4) Go 的 `(T, bool)` → `bool TryX(..., out T)`（`extractQuotedString` → `TryExtractQuotedString`）。
// 5) Go 的 `switch` + `fallthrough` → C# 无 fallthrough：合并 case 标签 + 组内前置 `if` 判定，逐条展开（语义相同）。
// 6) `panic(...)` → `throw new DebugFailureException(...)`；Go 的 nil 解引用 → `!`（C# 抛 NRE，等价）。
// 7) 位置一律 UTF-16 代码单元（§3.1）：`lineEndPos` 按代码单元推进（Go 按字节推进，两者停止在同一个换行字符上，
//    返回的下标各自对应自己那套单位）；`match`/`skipTo` 用 span 而非 `Substring`/`text[pos:]` 拷贝（§3.8）。
// 8) `ast.Pragma`/`ast.PragmaArgument`/`ast.CommentRange` 在 C# 里是 class 且没有 Go 的内嵌提升：Go 的
//    `commentRange.Pos()` → `commentRange.Range.Pos()`；Go 的 `pragma.CommentRange`（内嵌结构体拷贝）→ 显式
//    拷 `Range`/`Kind`/`HasTrailingNewLine` 三个字段。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Parser;

public sealed partial class Parser
{
    // ported from: internal/parser/parser.go:5054-5057
    private Node ParseJsxAttributes()
    {
        var pos = NodePos();
        return FinishNode(_factory.NewJsxAttributes(ParseList(ParsingContext.JsxAttributes, static p => p.ParseJsxAttribute())), pos);
    }

    // ported from: internal/parser/parser.go:5059-5065
    private Node ParseJsxAttribute()
    {
        if (_token == Kind.OpenBraceToken)
        {
            return ParseJsxSpreadAttribute();
        }
        var pos = NodePos();
        return FinishNode(_factory.NewJsxAttribute(ParseJsxAttributeName(), ParseJsxAttributeValue()), pos);
    }

    // ported from: internal/parser/parser.go:5067-5074
    private Node ParseJsxSpreadAttribute()
    {
        var pos = NodePos();
        ParseExpected(Kind.OpenBraceToken);
        ParseExpected(Kind.DotDotDotToken);
        var expression = ParseExpression();
        ParseExpected(Kind.CloseBraceToken);
        return FinishNode(_factory.NewJsxSpreadAttribute(expression), pos);
    }

    // ported from: internal/parser/parser.go:5076-5085
    private Node ParseJsxAttributeName()
    {
        var pos = NodePos();
        ScanJsxIdentifier();
        var attrName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(Kind.ColonToken))
        {
            ScanJsxIdentifier();
            return FinishNode(_factory.NewJsxNamespacedName(attrName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }
        return attrName;
    }

    // ported from: internal/parser/parser.go:5087-5101
    private Node? ParseJsxAttributeValue()
    {
        if (_token == Kind.EqualsToken)
        {
            if (ScanJsxAttributeValue() == Kind.StringLiteral)
            {
                return ParseLiteralExpression();
            }
            if (_token == Kind.OpenBraceToken)
            {
                return ParseJsxExpression( /*inExpressionContext*/ true);
            }
            if (_token == Kind.LessThanToken)
            {
                return ParseJsxElementOrSelfClosingElementOrFragment(true /*inExpressionContext*/, -1, null, false);
            }
            ParseErrorAtCurrentToken(X_or_JSX_element_expected);
        }
        return null;
    }

    // ported from: internal/parser/parser.go:5103-5115
    private Node ParseJsxClosingFragment(bool inExpressionContext)
    {
        var pos = NodePos();
        ParseExpected(Kind.LessThanSlashToken);
        if (ParseExpectedWithDiagnostic(Kind.GreaterThanToken, Expected_corresponding_closing_tag_for_JSX_fragment, false /*shouldAdvance*/))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext)
            {
                NextToken();
            }
            else
            {
                ScanJsxText();
            }
        }
        return FinishNode(_factory.NewJsxClosingFragment(), pos);
    }

    // ported from: internal/parser/parser.go:5117-5145
    private Node? ParseSimpleUnaryExpression()
    {
        switch (_token)
        {
            case Kind.PlusToken:
            case Kind.MinusToken:
            case Kind.TildeToken:
            case Kind.ExclamationToken:
                return ParsePrefixUnaryExpression();
            case Kind.DeleteKeyword:
                return ParseDeleteExpression();
            case Kind.TypeOfKeyword:
                return ParseTypeOfExpression();
            case Kind.VoidKeyword:
                return ParseVoidExpression();
            case Kind.LessThanToken:
                // Just like in parseUpdateExpression, we need to avoid parsing type assertions when
                // in JSX and we see an expression like "+ <foo> bar".
                if (_languageVariant == LanguageVariant.JSX)
                {
                    return ParseJsxElementOrSelfClosingElementOrFragment(true /*inExpressionContext*/, -1 /*topInvalidNodePosition*/, null /*openingTag*/, true /*mustBeUnary*/);
                }
                // // This is modified UnaryExpression grammar in TypeScript
                // //  UnaryExpression (modified):
                // //      < type > UnaryExpression
                return ParseTypeAssertion();
            case Kind.AwaitKeyword:
                if (IsAwaitExpression())
                {
                    return ParseAwaitExpression();
                }
                // Go: fallthrough
                return ParseUpdateExpression();
            default:
                return ParseUpdateExpression();
        }
    }

    // ported from: internal/parser/parser.go:5147-5152
    private Node ParsePrefixUnaryExpression()
    {
        var pos = NodePos();
        var @operator = _token;
        NextToken();
        return FinishNode(_factory.NewPrefixUnaryExpression(@operator, ParseSimpleUnaryExpression()), pos);
    }

    // ported from: internal/parser/parser.go:5154-5158
    private Node ParseDeleteExpression()
    {
        var pos = NodePos();
        NextToken();
        return FinishNode(_factory.NewDeleteExpression(ParseSimpleUnaryExpression()), pos);
    }

    // ported from: internal/parser/parser.go:5160-5164
    private Node ParseTypeOfExpression()
    {
        var pos = NodePos();
        NextToken();
        return FinishNode(_factory.NewTypeOfExpression(ParseSimpleUnaryExpression()), pos);
    }

    // ported from: internal/parser/parser.go:5166-5170
    private Node ParseVoidExpression()
    {
        var pos = NodePos();
        NextToken();
        return FinishNode(_factory.NewVoidExpression(ParseSimpleUnaryExpression()), pos);
    }

    // ported from: internal/parser/parser.go:5172-5181
    private bool IsAwaitExpression()
    {
        if (_token == Kind.AwaitKeyword)
        {
            if (InAwaitContext())
            {
                return true;
            }
            // here we are using similar heuristics as 'isYieldExpression'
            return LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine());
        }
        return false;
    }

    // ported from: internal/parser/parser.go:5183-5187
    private Node ParseAwaitExpression()
    {
        var pos = NodePos();
        NextToken();
        return FinishNode(_factory.NewAwaitExpression(ParseSimpleUnaryExpression()), pos);
    }

    // ported from: internal/parser/parser.go:5189-5197
    private Node ParseTypeAssertion()
    {
        Debug.Assert(_languageVariant != LanguageVariant.JSX, "Type assertions should never be parsed in JSX; they should be parsed as comparisons or JSX elements/fragments.");
        var pos = NodePos();
        ParseExpected(Kind.LessThanToken);
        var typeNode = ParseType();
        ParseExpected(Kind.GreaterThanToken);
        var expression = ParseSimpleUnaryExpression();
        return FinishNode(_factory.NewTypeAssertion(typeNode, expression), pos);
    }

    // ported from: internal/parser/parser.go:5199-5266
    private Node? ParseLeftHandSideExpressionOrHigher()
    {
        // Original Ecma:
        // LeftHandSideExpression: See 11.2
        //      NewExpression
        //      CallExpression
        //
        // Our simplification:
        //
        // LeftHandSideExpression: See 11.2
        //      MemberExpression
        //      CallExpression
        //
        // See comment in parseMemberExpressionOrHigher on how we replaced NewExpression with
        // MemberExpression to make our lives easier.
        //
        // to best understand the below code, it's important to see how CallExpression expands
        // out into its own productions:
        //
        // CallExpression:
        //      MemberExpression Arguments
        //      CallExpression Arguments
        //      CallExpression[Expression]
        //      CallExpression.IdentifierName
        //      import (AssignmentExpression)
        //      super Arguments
        //      super.IdentifierName
        //
        // Because of the recursion in these calls, we need to bottom out first. There are three
        // bottom out states we can run into: 1) We see 'super' which must start either of
        // the last two CallExpression productions. 2) We see 'import' which must start import call.
        // 3)we have a MemberExpression which either completes the LeftHandSideExpression,
        // or starts the beginning of the first four CallExpression productions.
        var pos = NodePos();
        Node? expression;
        if (_token == Kind.ImportKeyword)
        {
            if (LookAhead(static p => p.NextTokenIsOpenParenOrLessThan()))
            {
                // We don't want to eagerly consume all import keyword as import call expression so we look ahead to find "("
                // For example:
                //      var foo3 = require("subfolder
                //      import * as foo1 from "module-from-node
                // We want this import to be a statement rather than import call expression
                _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                expression = ParseKeywordExpression();
            }
            else if (LookAhead(static p => p.NextTokenIsDot()))
            {
                // This is an 'import.*' metaproperty (i.e. 'import.meta')
                NextToken(); // advance past the 'import'
                NextToken(); // advance past the dot
                expression = FinishNode(_factory.NewMetaProperty(Kind.ImportKeyword, ParseIdentifierName()), pos);
                if (expression.Text() == "defer")
                {
                    if (_token == Kind.OpenParenToken || _token == Kind.LessThanToken)
                    {
                        _sourceFlags |= NodeFlags.PossiblyContainsDynamicImport;
                    }
                }
                else
                {
                    _sourceFlags |= NodeFlags.PossiblyContainsImportMeta;
                }
            }
            else
            {
                expression = ParseMemberExpressionOrHigher();
            }
        }
        else if (_token == Kind.SuperKeyword)
        {
            expression = ParseSuperExpression();
        }
        else
        {
            expression = ParseMemberExpressionOrHigher();
        }
        // Now, we *may* be complete.  However, we might have consumed the start of a
        // CallExpression or OptionalExpression.  As such, we need to consume the rest
        // of it here to be complete.
        return ParseCallExpressionRest(pos, expression);
    }

    // ported from: internal/parser/parser.go:5268-5270
    private bool NextTokenIsDot()
    {
        return NextToken() == Kind.DotToken;
    }

    // ported from: internal/parser/parser.go:5272-5293
    private Node? ParseSuperExpression()
    {
        var pos = NodePos();
        var expression = ParseKeywordExpression();
        if (_token == Kind.LessThanToken)
        {
            var startPos = NodePos();
            var typeArguments = TryParseTypeArgumentsInExpression();
            if (typeArguments is not null)
            {
                ParseErrorAt(startPos, NodePos(), X_super_may_not_use_type_arguments);
                if (!IsTemplateStartOfTaggedTemplate())
                {
                    expression = FinishNode(_factory.NewExpressionWithTypeArguments(expression, typeArguments), pos);
                }
            }
        }
        if (_token == Kind.OpenParenToken || _token == Kind.DotToken || _token == Kind.OpenBracketToken)
        {
            return expression;
        }
        // If we have seen "super" it must be followed by '(' or '.'.
        // If it wasn't then just try to parse out a '.' and report an error.
        ParseErrorAtCurrentToken(X_super_must_be_followed_by_an_argument_list_or_member_access);
        // private names will never work with `super` (`super.#foo`), but that's a semantic error, not syntactic
        return FinishNode(_factory.NewPropertyAccessExpression(expression, null /*questionDotToken*/, ParseRightSideOfDot(true /*allowIdentifierNames*/, true /*allowPrivateIdentifiers*/, true /*allowUnicodeEscapeSequenceInIdentifierName*/), NodeFlags.None), pos);
    }

    // ported from: internal/parser/parser.go:5295-5297
    private bool IsTemplateStartOfTaggedTemplate()
    {
        return _token == Kind.NoSubstitutionTemplateLiteral || _token == Kind.TemplateHead;
    }

    // ported from: internal/parser/parser.go:5299-5325
    private NodeList? TryParseTypeArgumentsInExpression()
    {
        // TypeArguments must not be parsed in JavaScript files to avoid ambiguity with binary operators.
        // Check the cheap preconditions before saving the parser state: unless the current token is `<`
        // (or `<<`, which reScanLessThanToken would split), there is nothing to speculatively parse and
        // the mark/rewind would be a no-op.
        if ((_contextFlags & NodeFlags.JavaScriptFile) != 0 || (_token != Kind.LessThanToken && _token != Kind.LessThanLessThanToken))
        {
            return null;
        }
        var state = Mark();
        if (ReScanLessThanToken() == Kind.LessThanToken)
        {
            NextToken();
            var typeArguments = ParseDelimitedList(ParsingContext.TypeArguments, static p => p.ParseType());
            // If it doesn't have the closing `>` then it's definitely not an type argument list.
            if (ReScanGreaterThanToken() == Kind.GreaterThanToken)
            {
                NextToken();
                // We successfully parsed a type argument list. The next token determines whether we want to
                // treat it as such. If the type argument list is followed by `(` or a template literal, as in
                // `f<number>(42)`, we favor the type argument interpretation even though JavaScript would view
                // it as a relational expression.
                if (CanFollowTypeArgumentsInExpression())
                {
                    return typeArguments;
                }
            }
        }
        Rewind(state);
        return null;
    }

    // ported from: internal/parser/parser.go:5327-5344
    private bool CanFollowTypeArgumentsInExpression()
    {
        switch (_token)
        {
            // These tokens can follow a type argument list in a call expression:
            // foo<x>(
            // foo<T> `...`
            // foo<T> `...${100}...`
            case Kind.OpenParenToken:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateHead:
                return true;
            // A type argument list followed by `<` never makes sense, and a type argument list followed
            // by `>` is ambiguous with a (re-scanned) `>>` operator, so we disqualify both. Also, in
            // this context, `+` and `-` are unary operators, not binary operators.
            case Kind.LessThanToken:
            case Kind.GreaterThanToken:
            case Kind.PlusToken:
            case Kind.MinusToken:
                return false;
        }
        // We favor the type argument list interpretation when it is immediately followed by
        // a line break, a binary operator, or something that can't start an expression.
        return HasPrecedingLineBreak() || IsBinaryOperator() || !IsStartOfExpression();
    }

    // ported from: internal/parser/parser.go:5346-5397
    private Node ParseMemberExpressionOrHigher()
    {
        // Note: to make our lives simpler, we decompose the NewExpression productions and
        // place ObjectCreationExpression and FunctionExpression into PrimaryExpression.
        // like so:
        //
        //   PrimaryExpression : See 11.1
        //      this
        //      Identifier
        //      Literal
        //      ArrayLiteral
        //      ObjectLiteral
        //      (Expression)
        //      FunctionExpression
        //      new MemberExpression Arguments?
        //
        //   MemberExpression : See 11.2
        //      PrimaryExpression
        //      MemberExpression[Expression]
        //      MemberExpression.IdentifierName
        //
        //   CallExpression : See 11.2
        //      MemberExpression
        //      CallExpression Arguments
        //      CallExpression[Expression]
        //      CallExpression.IdentifierName
        //
        // Technically this is ambiguous.  i.e. CallExpression defines:
        //
        //   CallExpression:
        //      CallExpression Arguments
        //
        // If you see: "new Foo()"
        //
        // Then that could be treated as a single ObjectCreationExpression, or it could be
        // treated as the invocation of "new Foo".  We disambiguate that in code (to match
        // the original grammar) by making sure that if we see an ObjectCreationExpression
        // we always consume arguments if they are there. So we treat "new Foo()" as an
        // object creation only, and not at all as an invocation.  Another way to think
        // about this is that for every "new" that we see, we will consume an argument list if
        // it is there as part of the *associated* object creation node.  Any additional
        // argument lists we see, will become invocation expressions.
        //
        // Because there are no other places in the grammar now that refer to FunctionExpression
        // or ObjectCreationExpression, it is safe to push down into the PrimaryExpression
        // production.
        //
        // Because CallExpression and MemberExpression are left recursive, we need to bottom out
        // of the recursion immediately.  So we parse out a primary expression to start with.
        var pos = NodePos();
        var expression = ParsePrimaryExpression();
        return ParseMemberExpressionRest(pos, expression, true /*allowOptionalChain*/);
    }

    // ported from: internal/parser/parser.go:5399-5443
    private Node ParseMemberExpressionRest(int pos, Node? expression, bool allowOptionalChain)
    {
        while (true)
        {
            Node? questionDotToken = null;
            bool isPropertyAccess;
            if (allowOptionalChain && IsStartOfOptionalPropertyOrElementAccessChain())
            {
                questionDotToken = ParseExpectedToken(Kind.QuestionDotToken);
                isPropertyAccess = TokenIsIdentifierOrKeyword(_token);
            }
            else
            {
                isPropertyAccess = ParseOptional(Kind.DotToken);
            }
            if (isPropertyAccess)
            {
                expression = ParsePropertyAccessExpressionRest(pos, expression, questionDotToken);
                continue;
            }
            // when in the [Decorator] context, we do not parse ElementAccess as it could be part of a ComputedPropertyName
            if ((questionDotToken is not null || !InDecoratorContext()) && ParseOptional(Kind.OpenBracketToken))
            {
                expression = ParseElementAccessExpressionRest(pos, expression, questionDotToken);
                continue;
            }
            if (IsTemplateStartOfTaggedTemplate())
            {
                // Absorb type arguments into TemplateExpression when preceding expression is ExpressionWithTypeArguments
                if (questionDotToken is null && IsExpressionWithTypeArguments(expression!))
                {
                    var original = expression!.AsExpressionWithTypeArguments()!;
                    expression = ParseTaggedTemplateRest(pos, original.Expression, questionDotToken, original.TypeArguments);
                    UnparseExpressionWithTypeArguments(original.Expression, original.TypeArguments, expression);
                }
                else
                {
                    expression = ParseTaggedTemplateRest(pos, expression, questionDotToken, null /*typeArguments*/);
                }
                continue;
            }
            if (questionDotToken is null)
            {
                if (_token == Kind.ExclamationToken && !HasPrecedingLineBreak())
                {
                    NextToken();
                    expression = CheckJSSyntax(FinishNode(_factory.NewNonNullExpression(expression, NodeFlags.None), pos));
                    continue;
                }
                var typeArguments = TryParseTypeArgumentsInExpression();
                if (typeArguments is not null)
                {
                    expression = FinishNode(_factory.NewExpressionWithTypeArguments(expression, typeArguments), pos);
                    continue;
                }
            }
            return expression!;
        }
    }

    // ported from: internal/parser/parser.go:5445-5447
    private bool IsStartOfOptionalPropertyOrElementAccessChain()
    {
        return _token == Kind.QuestionDotToken && LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate());
    }

    // ported from: internal/parser/parser.go:5449-5452
    private bool NextTokenIsIdentifierOrKeywordOrOpenBracketOrTemplate()
    {
        NextToken();
        return TokenIsIdentifierOrKeyword(_token) || _token == Kind.OpenBracketToken || IsTemplateStartOfTaggedTemplate();
    }

    // ported from: internal/parser/parser.go:5454-5469
    private Node ParsePropertyAccessExpressionRest(int pos, Node? expression, Node? questionDotToken)
    {
        var name = ParseRightSideOfDot(true /*allowIdentifierNames*/, true /*allowPrivateIdentifiers*/, true /*allowUnicodeEscapeSequenceInIdentifierName*/);
        var isOptionalChain = questionDotToken is not null || TryReparseOptionalChain(expression!);
        var propertyAccess = _factory.NewPropertyAccessExpression(expression, questionDotToken, name, IfElse(isOptionalChain, NodeFlags.OptionalChain, NodeFlags.None));
        if (isOptionalChain && IsPrivateIdentifier(name))
        {
            ParseErrorAtRange(SkipRangeTrivia(name.Loc), An_optional_chain_cannot_contain_private_identifiers);
        }
        if (IsExpressionWithTypeArguments(expression!))
        {
            var typeArguments = expression!.TypeArgumentList();
            if (typeArguments is not null)
            {
                var loc = NewTextRange(typeArguments.Pos() - 1, SkipTrivia(_sourceText, typeArguments.End()) + 1);
                ParseErrorAtRange(loc, An_instantiation_expression_cannot_be_followed_by_a_property_access);
            }
        }
        return FinishNode(propertyAccess, pos);
    }

    // ported from: internal/parser/parser.go:5471-5491
    private bool TryReparseOptionalChain(Node node)
    {
        if ((node.Flags & NodeFlags.OptionalChain) != 0)
        {
            return true;
        }
        // check for an optional chain in a non-null expression
        if (IsNonNullExpression(node))
        {
            var expr = node.Expression()!;
            while (IsNonNullExpression(expr) && (expr.Flags & NodeFlags.OptionalChain) == 0)
            {
                expr = expr.Expression()!;
            }
            if ((expr.Flags & NodeFlags.OptionalChain) != 0)
            {
                // this is part of an optional chain. Walk down from `node` to `expression` and set the flag.
                var n = node;
                while (IsNonNullExpression(n))
                {
                    n.Flags |= NodeFlags.OptionalChain;
                    n = n.Expression()!;
                }
                return true;
            }
        }
        return false;
    }

    // ported from: internal/parser/parser.go:5493-5503
    private Node ParseElementAccessExpressionRest(int pos, Node? expression, Node? questionDotToken)
    {
        var argumentExpression = CreateMissingIdentifier();
        if (_token == Kind.CloseBracketToken)
        {
            ParseErrorAt(NodePos(), NodePos(), An_element_access_expression_should_take_an_argument);
        }
        else
        {
            argumentExpression = ParseExpressionAllowIn();
        }
        ParseExpected(Kind.CloseBracketToken);
        var isOptionalChain = questionDotToken is not null || TryReparseOptionalChain(expression!);
        return FinishNode(_factory.NewElementAccessExpression(expression, questionDotToken, argumentExpression, IfElse(isOptionalChain, NodeFlags.OptionalChain, NodeFlags.None)), pos);
    }

    // ported from: internal/parser/parser.go:5505-5539
    private Node ParseCallExpressionRest(int pos, Node? expression)
    {
        while (true)
        {
            expression = ParseMemberExpressionRest(pos, expression /*allowOptionalChain*/, true);
            NodeList? typeArguments = null;
            var questionDotToken = ParseOptionalToken(Kind.QuestionDotToken);
            if (questionDotToken is not null)
            {
                typeArguments = TryParseTypeArgumentsInExpression();
                if (IsTemplateStartOfTaggedTemplate())
                {
                    expression = ParseTaggedTemplateRest(pos, expression, questionDotToken, typeArguments);
                    continue;
                }
            }
            if (typeArguments is not null || _token == Kind.OpenParenToken)
            {
                // Absorb type arguments into CallExpression when preceding expression is ExpressionWithTypeArguments
                if (questionDotToken is null && expression!.Kind == Kind.ExpressionWithTypeArguments)
                {
                    typeArguments = expression!.TypeArgumentList();
                    expression = expression!.AsExpressionWithTypeArguments()!.Expression;
                }
                var inner = expression;
                var argumentList = ParseArgumentList();
                var isOptionalChain = questionDotToken is not null || TryReparseOptionalChain(expression!);
                expression = CheckJSSyntax(FinishNode(_factory.NewCallExpression(expression, questionDotToken, typeArguments, argumentList, IfElse(isOptionalChain, NodeFlags.OptionalChain, NodeFlags.None)), pos));
                UnparseExpressionWithTypeArguments(inner, typeArguments, expression);
                continue;
            }
            if (questionDotToken is not null)
            {
                // We parsed `?.` but then failed to parse anything, so report a missing identifier here.
                ParseErrorAtCurrentToken(Identifier_expected);
                var name = CreateMissingIdentifier();
                expression = FinishNode(_factory.NewPropertyAccessExpression(expression, questionDotToken, name, NodeFlags.OptionalChain), pos);
            }
            break;
        }
        return expression!;
    }

    // ported from: internal/parser/parser.go:5541-5546
    private NodeList? ParseArgumentList()
    {
        ParseExpected(Kind.OpenParenToken);
        var result = ParseDelimitedList(ParsingContext.ArgumentExpressions, static p => p.ParseArgumentExpression());
        ParseExpected(Kind.CloseParenToken);
        return result;
    }

    // ported from: internal/parser/parser.go:5548-5550
    private Node? ParseArgumentExpression()
    {
        return DoInContext(NodeFlags.DisallowInContext | NodeFlags.DecoratorContext, false, static p => p.ParseArgumentOrArrayLiteralElement());
    }

    // ported from: internal/parser/parser.go:5552-5560
    private Node? ParseArgumentOrArrayLiteralElement()
    {
        switch (_token)
        {
            case Kind.DotDotDotToken:
                return ParseSpreadElement();
            case Kind.CommaToken:
                return FinishNode(_factory.NewOmittedExpression(), NodePos());
        }
        return ParseAssignmentExpressionOrHigher();
    }

    // ported from: internal/parser/parser.go:5562-5567
    private Node ParseSpreadElement()
    {
        var pos = NodePos();
        ParseExpected(Kind.DotDotDotToken);
        var expression = ParseAssignmentExpressionOrHigher();
        return FinishNode(_factory.NewSpreadElement(expression), pos);
    }

    // ported from: internal/parser/parser.go:5569-5579
    private Node ParseTaggedTemplateRest(int pos, Node? tag, Node? questionDotToken, NodeList? typeArguments)
    {
        Node template;
        if (_token == Kind.NoSubstitutionTemplateLiteral)
        {
            ReScanTemplateToken(true /*isTaggedTemplate*/);
            template = ParseLiteralExpression();
        }
        else
        {
            template = ParseTemplateExpression(true /*isTaggedTemplate*/);
        }
        var isOptionalChain = questionDotToken is not null || (tag!.Flags & NodeFlags.OptionalChain) != 0;
        return CheckJSSyntax(FinishNode(_factory.NewTaggedTemplateExpression(tag, questionDotToken, typeArguments, template, IfElse(isOptionalChain, NodeFlags.OptionalChain, NodeFlags.None)), pos));
    }

    // ported from: internal/parser/parser.go:5581-5584
    private Node ParseTemplateExpression(bool isTaggedTemplate)
    {
        var pos = NodePos();
        return FinishNode(_factory.NewTemplateExpression(ParseTemplateHead(isTaggedTemplate), ParseTemplateSpans(isTaggedTemplate)), pos);
    }

    // ported from: internal/parser/parser.go:5586-5597
    private NodeList ParseTemplateSpans(bool isTaggedTemplate)
    {
        var pos = NodePos();
        var list = new List<Node>();
        while (true)
        {
            var span = ParseTemplateSpan(isTaggedTemplate);
            list.Add(span);
            if (span.AsTemplateSpan()!.Literal!.Kind != Kind.TemplateMiddle)
            {
                break;
            }
        }
        return NewNodeList(NewTextRange(pos, NodePos()), list.ToArray());
    }

    // ported from: internal/parser/parser.go:5599-5604
    private Node ParseTemplateSpan(bool isTaggedTemplate)
    {
        var pos = NodePos();
        var expression = ParseExpressionAllowIn();
        var literal = ParseLiteralOfTemplateSpan(isTaggedTemplate);
        return FinishNode(_factory.NewTemplateSpan(expression, literal), pos);
    }

    // ported from: internal/parser/parser.go:5606-5649
    private Node? ParsePrimaryExpression()
    {
        switch (_token)
        {
            case Kind.NoSubstitutionTemplateLiteral:
                if ((_scanner.TokenFlags & TokenFlags.IsInvalid) != 0)
                {
                    ReScanTemplateToken(false /*isTaggedTemplate*/);
                }
                // Go: fallthrough
                return ParseLiteralExpression();
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.StringLiteral:
                return ParseLiteralExpression();
            case Kind.ThisKeyword:
            case Kind.SuperKeyword:
            case Kind.NullKeyword:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
                return ParseKeywordExpression();
            case Kind.OpenParenToken:
                return ParseParenthesizedExpression();
            case Kind.OpenBracketToken:
                return ParseArrayLiteralExpression();
            case Kind.OpenBraceToken:
                return ParseObjectLiteralExpression();
            case Kind.AsyncKeyword:
                // Async arrow functions are parsed earlier in parseAssignmentExpressionOrHigher.
                // If we encounter `async [no LineTerminator here] function` then this is an async
                // function; otherwise, its an identifier.
                if (!LookAhead(static p => p.NextTokenIsFunctionKeywordOnSameLine()))
                {
                    break;
                }
                return ParseFunctionExpression();
            case Kind.AtToken:
                return ParseDecoratedExpression();
            case Kind.ClassKeyword:
                return ParseClassExpression();
            case Kind.FunctionKeyword:
                return ParseFunctionExpression();
            case Kind.NewKeyword:
                return ParseNewExpressionOrNewDotTarget();
            case Kind.SlashToken:
            case Kind.SlashEqualsToken:
                if (ReScanSlashToken() == Kind.RegularExpressionLiteral)
                {
                    return ParseLiteralExpression();
                }
                break;
            case Kind.TemplateHead:
                return ParseTemplateExpression(false /*isTaggedTemplate*/);
            case Kind.PrivateIdentifier:
                return ParsePrivateIdentifier();
        }
        return ParseIdentifierWithDiagnostic(Expression_expected, null);
    }

    // ported from: internal/parser/parser.go:5651-5660
    private Node? ParseParenthesizedExpression()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        ParseExpected(Kind.OpenParenToken);
        var expression = ParseExpressionAllowIn();
        ParseExpected(Kind.CloseParenToken);
        var result = FinishNode(_factory.NewParenthesizedExpression(expression), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    // ported from: internal/parser/parser.go:5662-5670
    private Node? ParseArrayLiteralExpression()
    {
        var pos = NodePos();
        var openBracketPosition = _scanner.TokenStart();
        var openBracketParsed = ParseExpected(Kind.OpenBracketToken);
        var multiLine = HasPrecedingLineBreak();
        var elements = ParseDelimitedList(ParsingContext.ArrayLiteralMembers, static p => p.ParseArgumentOrArrayLiteralElement());
        ParseExpectedMatchingBrackets(Kind.OpenBracketToken, Kind.CloseBracketToken, openBracketParsed, openBracketPosition);
        return FinishNode(_factory.NewArrayLiteralExpression(elements, multiLine), pos);
    }

    // ported from: internal/parser/parser.go:5672-5680
    private Node? ParseObjectLiteralExpression()
    {
        var pos = NodePos();
        var openBracePosition = _scanner.TokenStart();
        var openBraceParsed = ParseExpected(Kind.OpenBraceToken);
        var multiLine = HasPrecedingLineBreak();
        var properties = ParseDelimitedList(ParsingContext.ObjectLiteralMembers, static p => p.ParseObjectLiteralElement());
        ParseExpectedMatchingBrackets(Kind.OpenBraceToken, Kind.CloseBraceToken, openBraceParsed, openBracePosition);
        return FinishNode(_factory.NewObjectLiteralExpression(properties, multiLine), pos);
    }

    // ported from: internal/parser/parser.go:5682-5732
    private Node? ParseObjectLiteralElement()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        if (ParseOptional(Kind.DotDotDotToken))
        {
            var expression = ParseAssignmentExpressionOrHigher();
            var result = FinishNode(_factory.NewSpreadAssignment(expression), pos);
            WithJSDoc(result, jsdoc);
            return result;
        }
        var modifiers = ParseModifiersEx(true /*allowDecorators*/, false /*permitConstAsModifier*/, false /*stopOnStartOfClassStaticBlock*/);
        if (ParseContextualModifier(Kind.GetKeyword))
        {
            return ParseAccessorDeclaration(pos, jsdoc, modifiers, Kind.GetAccessor, ParseFlags.None);
        }
        if (ParseContextualModifier(Kind.SetKeyword))
        {
            return ParseAccessorDeclaration(pos, jsdoc, modifiers, Kind.SetAccessor, ParseFlags.None);
        }
        var asteriskToken = ParseOptionalToken(Kind.AsteriskToken);
        var tokenIsIdentifier = IsIdentifier();
        var name = ParsePropertyName();
        // Disallowing of optional property assignments and definite assignment assertion happens in the grammar checker.
        var postfixToken = ParseOptionalToken(Kind.QuestionToken);
        // Decorators, Modifiers, questionToken, and exclamationToken are not supported by property assignments and are reported in the grammar checker
        if (postfixToken is null)
        {
            postfixToken = ParseOptionalToken(Kind.ExclamationToken);
        }
        if (asteriskToken is not null || _token == Kind.OpenParenToken || _token == Kind.LessThanToken)
        {
            return ParseMethodDeclaration(pos, jsdoc, modifiers, asteriskToken, name, postfixToken, null /*diagnosticMessage*/);
        }
        // check if it is short-hand property assignment or normal property assignment
        // NOTE: if token is EqualsToken it is interpreted as CoverInitializedName production
        // CoverInitializedName[Yield] :
        //     IdentifierReference[?Yield] Initializer[In, ?Yield]
        // this is necessary because ObjectLiteral productions are also used to cover grammar for ObjectAssignmentPattern
        Node node;
        var isShorthandPropertyAssignment = tokenIsIdentifier && _token != Kind.ColonToken;
        if (isShorthandPropertyAssignment)
        {
            var equalsToken = ParseOptionalToken(Kind.EqualsToken);
            Node? initializer = null;
            if (equalsToken is not null)
            {
                initializer = DoInContext(NodeFlags.DisallowInContext, false, static p => p.ParseAssignmentExpressionOrHigher());
            }
            node = _factory.NewShorthandPropertyAssignment(modifiers, name, postfixToken, null /*typeNode*/, equalsToken, initializer);
        }
        else
        {
            ParseExpected(Kind.ColonToken);
            var initializer = DoInContext(NodeFlags.DisallowInContext, false, static p => p.ParseAssignmentExpressionOrHigher());
            node = _factory.NewPropertyAssignment(modifiers, name, postfixToken, null /*typeNode*/, initializer);
        }
        FinishNode(node, pos);
        WithJSDoc(node, jsdoc);
        return node;
    }

    // ported from: internal/parser/parser.go:5734-5771
    private Node? ParseFunctionExpression()
    {
        // GeneratorExpression:
        //      function* BindingIdentifier [Yield][opt](FormalParameters[Yield]){ GeneratorBody }
        //
        // FunctionExpression:
        //      function BindingIdentifier[opt](FormalParameters){ FunctionBody }
        var saveContexFlags = _contextFlags;
        SetContextFlags(NodeFlags.DecoratorContext, false);
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var modifiers = ParseModifiers();
        ParseExpected(Kind.FunctionKeyword);
        var asteriskToken = ParseOptionalToken(Kind.AsteriskToken);
        var isGenerator = asteriskToken is not null;
        var isAsync = ModifierListHasAsync(modifiers);
        var signatureFlags = (isGenerator ? ParseFlags.Yield : ParseFlags.None) | (isAsync ? ParseFlags.Await : ParseFlags.None);
        Node? name;
        if (isGenerator && isAsync)
        {
            name = DoInContext(NodeFlags.YieldContext | NodeFlags.AwaitContext, true, static p => p.ParseOptionalBindingIdentifier());
        }
        else if (isGenerator)
        {
            name = DoInContext(NodeFlags.YieldContext, true, static p => p.ParseOptionalBindingIdentifier());
        }
        else if (isAsync)
        {
            name = DoInContext(NodeFlags.AwaitContext, true, static p => p.ParseOptionalBindingIdentifier());
        }
        else
        {
            name = ParseOptionalBindingIdentifier();
        }
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(signatureFlags);
        var returnType = ParseReturnType(Kind.ColonToken, false /*isType*/);
        var body = ParseFunctionBlock(signatureFlags, null /*diagnosticMessage*/);
        _contextFlags = saveContexFlags;
        var result = _factory.NewFunctionExpression(modifiers, asteriskToken, name, typeParameters, parameters, returnType, null /*fullSignature*/, body);
        FinishNode(result, pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    // ported from: internal/parser/parser.go:5773-5778
    private Node? ParseOptionalBindingIdentifier()
    {
        if (IsBindingIdentifier())
        {
            return ParseBindingIdentifier();
        }
        return null;
    }

    // ported from: internal/parser/parser.go:5780-5789
    private Node? ParseDecoratedExpression()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var modifiers = ParseModifiersEx(true /*allowDecorators*/, false /*permitConstAsModifier*/, false /*stopOnStartOfClassStaticBlock*/);
        if (_token == Kind.ClassKeyword)
        {
            return ParseClassDeclarationOrExpression(pos, jsdoc, modifiers, Kind.ClassExpression);
        }
        ParseErrorAt(NodePos(), NodePos(), Expression_expected);
        return FinishNode(_factory.NewMissingDeclaration(modifiers), pos);
    }

    // ported from: internal/parser/parser.go:5791-5801
    private void UnparseExpressionWithTypeArguments(Node? expression, NodeList? typeArguments, Node? result)
    {
        // force overwrite the `.Parent` of the expression and type arguments to erase the fact that they may have originally been parsed as an ExpressionWithTypeArguments and be parented to such
        if (expression is not null)
        {
            expression.Parent = result;
        }
        if (typeArguments is not null)
        {
            foreach (var a in typeArguments.Nodes ?? [])
            {
                a.Parent = result;
            }
        }
    }

    // ported from: internal/parser/parser.go:5803-5828
    private Node ParseNewExpressionOrNewDotTarget()
    {
        var pos = NodePos();
        ParseExpected(Kind.NewKeyword);
        if (ParseOptional(Kind.DotToken))
        {
            var name = ParseIdentifierName();
            return FinishNode(_factory.NewMetaProperty(Kind.NewKeyword, name), pos);
        }
        var expressionPos = NodePos();
        var expression = ParseMemberExpressionRest(expressionPos, ParsePrimaryExpression(), false /*allowOptionalChain*/);
        NodeList? typeArguments = null;
        // Absorb type arguments into NewExpression when preceding expression is ExpressionWithTypeArguments
        if (expression.Kind == Kind.ExpressionWithTypeArguments)
        {
            typeArguments = expression.TypeArgumentList();
            expression = expression.AsExpressionWithTypeArguments()!.Expression!;
        }
        if (_token == Kind.QuestionDotToken)
        {
            ParseErrorAtCurrentToken(Invalid_optional_chain_from_new_expression_Did_you_mean_to_call_0, GetTextOfNodeFromSourceText(_sourceText, expression, false /*includeTrivia*/));
        }
        NodeList? argumentList = null;
        if (_token == Kind.OpenParenToken)
        {
            argumentList = ParseArgumentList();
        }
        var result = CheckJSSyntax(FinishNode(_factory.NewNewExpression(expression, typeArguments, argumentList), pos));
        UnparseExpressionWithTypeArguments(expression, typeArguments, result);
        return result;
    }

    // ported from: internal/parser/parser.go:5830-5835
    private Node ParseKeywordExpression()
    {
        var pos = NodePos();
        var result = _factory.NewKeywordExpression(_token);
        NextToken();
        return FinishNode(result, pos);
    }

    // ported from: internal/parser/parser.go:5837-5858
    private Node ParseLiteralExpression()
    {
        var pos = NodePos();
        var text = _scanner.TokenValue();
        var tokenFlags = _scanner.TokenFlags;
        Node result;
        switch (_token)
        {
            case Kind.StringLiteral:
                result = _factory.NewStringLiteral(text, tokenFlags);
                break;
            case Kind.NumericLiteral:
                result = _factory.NewNumericLiteral(text, tokenFlags);
                break;
            case Kind.BigIntLiteral:
                result = _factory.NewBigIntLiteral(text, tokenFlags);
                break;
            case Kind.RegularExpressionLiteral:
                result = _factory.NewRegularExpressionLiteral(text, tokenFlags);
                break;
            case Kind.NoSubstitutionTemplateLiteral:
                result = _factory.NewNoSubstitutionTemplateLiteral(text, tokenFlags);
                break;
            default:
                throw new DebugFailureException("Unhandled case in parseLiteralExpression");
        }
        NextToken();
        return FinishNode(result, pos);
    }

    // ported from: internal/parser/parser.go:5860-5865
    private Node ParseIdentifierNameErrorOnUnicodeEscapeSequence()
    {
        if (_scanner.HasUnicodeEscape() || _scanner.HasExtendedUnicodeEscape())
        {
            ParseErrorAtCurrentToken(Unicode_escape_sequence_cannot_appear_here);
        }
        return CreateIdentifier(TokenIsIdentifierOrKeyword(_token));
    }

    // ported from: internal/parser/parser.go:5867-5869
    private Node ParseBindingIdentifier()
    {
        return ParseBindingIdentifierWithDiagnostic(null);
    }

    // ported from: internal/parser/parser.go:5871-5876
    private Node ParseBindingIdentifierWithDiagnostic(Message? privateIdentifierDiagnosticMessage)
    {
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        var id = CreateIdentifierWithDiagnostic(IsBindingIdentifier(), null /*diagnosticMessage*/, privateIdentifierDiagnosticMessage);
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return id;
    }

    // ported from: internal/parser/parser.go:5878-5880
    private Node ParseIdentifierName()
    {
        return ParseIdentifierNameWithDiagnostic(null);
    }

    // ported from: internal/parser/parser.go:5882-5884
    private Node ParseIdentifierNameWithDiagnostic(Message? diagnosticMessage)
    {
        return CreateIdentifierWithDiagnostic(TokenIsIdentifierOrKeyword(_token), diagnosticMessage, null);
    }

    // ported from: internal/parser/parser.go:5886-5888
    private Node ParseIdentifier()
    {
        return ParseIdentifierWithDiagnostic(null, null);
    }

    // ported from: internal/parser/parser.go:5890-5892
    private Node ParseIdentifierWithDiagnostic(Message? diagnosticMessage, Message? privateIdentifierDiagnosticMessage)
    {
        return CreateIdentifierWithDiagnostic(IsIdentifier(), diagnosticMessage, privateIdentifierDiagnosticMessage);
    }

    // ported from: internal/parser/parser.go:5894-5896
    private Node CreateIdentifier(bool isIdentifier)
    {
        return CreateIdentifierWithDiagnostic(isIdentifier, null, null);
    }

    // ported from: internal/parser/parser.go:5898-5943
    private Node CreateIdentifierWithDiagnostic(bool isIdentifier, Message? diagnosticMessage, Message? privateIdentifierDiagnosticMessage)
    {
        if (isIdentifier)
        {
            int pos;
            if (_scanner.HasPrecedingJSDocLeadingAsterisks())
            {
                pos = _scanner.TokenStart();
            }
            else
            {
                pos = NodePos();
            }
            var text = _scanner.TokenValue();
            NextTokenWithoutCheck();
            return FinishNode(NewIdentifier(text), pos);
        }
        if (_token == Kind.PrivateIdentifier)
        {
            if (privateIdentifierDiagnosticMessage is not null)
            {
                ParseErrorAtCurrentToken(privateIdentifierDiagnosticMessage);
            }
            else
            {
                ParseErrorAtCurrentToken(Private_identifiers_are_not_allowed_outside_class_bodies);
            }
            return CreateIdentifier(true /*isIdentifier*/);
        }
        // Only for end of file because the error gets reported incorrectly on embedded script tags.
        var reportAtCurrentPosition = _token == Kind.EndOfFile;
        if (diagnosticMessage is not null)
        {
            if (reportAtCurrentPosition)
            {
                var pos = _scanner.TokenFullStart();
                ParseErrorAt(pos, pos, diagnosticMessage);
            }
            else
            {
                ParseErrorAtCurrentToken(diagnosticMessage);
            }
        }
        else if (IsReservedWord(_token))
        {
            if (reportAtCurrentPosition)
            {
                var pos = _scanner.TokenFullStart();
                ParseErrorAt(pos, pos, Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, _scanner.TokenText());
            }
            else
            {
                ParseErrorAtCurrentToken(Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, _scanner.TokenText());
            }
        }
        else
        {
            if (reportAtCurrentPosition)
            {
                var pos = _scanner.TokenFullStart();
                ParseErrorAt(pos, pos, Identifier_expected);
            }
            else
            {
                ParseErrorAtCurrentToken(Identifier_expected);
            }
        }
        return CreateMissingIdentifier();
    }

    // ported from: internal/parser/parser.go:5945-5949
    private NodeList NewNodeList(TextRange loc, Node[] nodes)
    {
        var list = _factory.NewNodeList(nodes);
        list.Loc = loc;
        return list;
    }

    // ported from: internal/parser/parser.go:5951-5955
    private ModifierList NewModifierList(TextRange loc, Node[] nodes)
    {
        var list = _factory.NewModifierList(nodes);
        list.Loc = loc;
        return list;
    }

    // ported from: internal/parser/parser.go:5957-5959
    private Node FinishNode(Node node, int pos)
    {
        return FinishNodeWithEnd(node, pos, NodePos());
    }

    // ported from: internal/parser/parser.go:5961-5970
    private Node FinishNodeWithEnd(Node node, int pos, int end)
    {
        node.Loc = NewTextRange(pos, end);
        node.Flags |= _contextFlags;
        if (_hasParseError)
        {
            node.Flags |= NodeFlags.ThisNodeHasError;
            _hasParseError = false;
        }
        OverrideParentInImmediateChildren(node);
        return node;
    }

    // ported from: internal/parser/parser.go:5972-5976
    private void OverrideParentInImmediateChildren(Node node)
    {
        _currentParent = node;
        node.ForEachChild(_setParentFromContext);
        _currentParent = null;
    }

    // ported from: internal/parser/parser.go:5978-5980
    private bool NextTokenIsSlash()
    {
        return NextToken() == Kind.SlashToken;
    }

    // ported from: internal/parser/parser.go:5982-6008
    private bool ScanTypeMemberStart()
    {
        // Return true if we have the start of a signature member
        if (_token == Kind.OpenParenToken || _token == Kind.LessThanToken || _token == Kind.GetKeyword || _token == Kind.SetKeyword)
        {
            return true;
        }
        var idToken = false;
        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier
        while (IsModifierKind(_token))
        {
            idToken = true;
            NextToken();
        }
        // Index signatures and computed property names are type members
        if (_token == Kind.OpenBracketToken)
        {
            return true;
        }
        // Try to get the first property-like token following all modifiers
        if (IsLiteralPropertyName())
        {
            idToken = true;
            NextToken();
        }
        // If we were able to get any potential identifier, check that it is
        // the start of a member declaration
        if (idToken)
        {
            return _token == Kind.OpenParenToken || _token == Kind.LessThanToken || _token == Kind.QuestionToken || _token == Kind.ColonToken || _token == Kind.CommaToken || CanParseSemicolon();
        }
        return false;
    }

    // ported from: internal/parser/parser.go:6010-6067
    private bool ScanClassMemberStart()
    {
        var idToken = Kind.Unknown;
        if (_token == Kind.AtToken)
        {
            return true;
        }
        // Eat up all modifiers, but hold on to the last one in case it is actually an identifier.
        while (IsModifierKind(_token))
        {
            idToken = _token;
            // If the idToken is a class modifier (protected, private, public, and static), it is
            // certain that we are starting to parse class member. This allows better error recovery
            // Example:
            //      public foo() ...     // true
            //      public @dec blah ... // true; we will then report an error later
            //      export public ...    // true; we will then report an error later
            if (IsClassMemberModifier(idToken))
            {
                return true;
            }
            NextToken();
        }
        if (_token == Kind.AsteriskToken)
        {
            return true;
        }
        // Try to get the first property-like token following all modifiers.
        // This can either be an identifier or the 'get' or 'set' keywords.
        if (IsLiteralPropertyName())
        {
            idToken = _token;
            NextToken();
        }
        // Index signatures and computed properties are class members; we can parse.
        if (_token == Kind.OpenBracketToken)
        {
            return true;
        }
        // If we were able to get any potential identifier...
        if (idToken != Kind.Unknown)
        {
            // If we have a non-keyword identifier, or if we have an accessor, then it's safe to parse.
            if (!IsKeyword(idToken) || idToken == Kind.SetKeyword || idToken == Kind.GetKeyword)
            {
                return true;
            }
            // If it *is* a keyword, but not an accessor, check a little farther along
            // to see if it should actually be parsed as a class member.
            switch (_token)
            {
                case Kind.OpenParenToken: // Method declaration
                case Kind.LessThanToken: // Generic Method declaration
                case Kind.ExclamationToken: // Non-null assertion on property name
                case Kind.ColonToken: // Type Annotation for declaration
                case Kind.EqualsToken: // Initializer for declaration
                case Kind.QuestionToken: // Not valid, but permitted so that it gets caught later on.
                    return true;
            }
            // Covers
            //  - Semicolons     (declaration termination)
            //  - Closing braces (end-of-class, must be declaration)
            //  - End-of-files   (not valid, but permitted so that it gets caught later on)
            //  - Line-breaks    (enabling *automatic semicolon insertion*)
            return CanParseSemicolon();
        }
        return false;
    }

    // ported from: internal/parser/parser.go:6069-6073
    private bool CanParseSemicolon()
    {
        // If there's a real semicolon, then we can always parse it out.
        // We can parse out an optional semicolon in ASI cases in the following cases.
        return _token == Kind.SemicolonToken || _token == Kind.CloseBraceToken || _token == Kind.EndOfFile || HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:6075-6084
    private bool TryParseSemicolon()
    {
        if (!CanParseSemicolon())
        {
            return false;
        }
        if (_token == Kind.SemicolonToken)
        {
            // consume the semicolon if it was explicitly provided.
            NextToken();
        }
        return true;
    }

    // ported from: internal/parser/parser.go:6086-6088
    private bool ParseSemicolon()
    {
        return TryParseSemicolon() || ParseExpected(Kind.SemicolonToken);
    }

    // ported from: internal/parser/parser.go:6090-6092
    private bool IsLiteralPropertyName()
    {
        return TokenIsIdentifierOrKeyword(_token) || _token == Kind.StringLiteral || _token == Kind.NumericLiteral || _token == Kind.BigIntLiteral;
    }

    // ported from: internal/parser/parser.go:6094-6121
    private bool IsStartOfStatement()
    {
        switch (_token)
        {
            // 'catch' and 'finally' do not actually indicate that the code is part of a statement,
            // however, we say they are here so that we may gracefully parse them and error later.
            case Kind.AtToken:
            case Kind.SemicolonToken:
            case Kind.OpenBraceToken:
            case Kind.VarKeyword:
            case Kind.LetKeyword:
            case Kind.UsingKeyword:
            case Kind.FunctionKeyword:
            case Kind.ClassKeyword:
            case Kind.EnumKeyword:
            case Kind.IfKeyword:
            case Kind.DoKeyword:
            case Kind.WhileKeyword:
            case Kind.ForKeyword:
            case Kind.ContinueKeyword:
            case Kind.BreakKeyword:
            case Kind.ReturnKeyword:
            case Kind.WithKeyword:
            case Kind.SwitchKeyword:
            case Kind.ThrowKeyword:
            case Kind.TryKeyword:
            case Kind.DebuggerKeyword:
            case Kind.CatchKeyword:
            case Kind.FinallyKeyword:
                return true;
            case Kind.ImportKeyword:
                return IsStartOfDeclaration() || IsNextTokenOpenParenOrLessThanOrDot();
            case Kind.ConstKeyword:
            case Kind.ExportKeyword:
                return IsStartOfDeclaration();
            case Kind.AsyncKeyword:
            case Kind.DeclareKeyword:
            case Kind.InterfaceKeyword:
            case Kind.ModuleKeyword:
            case Kind.NamespaceKeyword:
            case Kind.TypeKeyword:
            case Kind.GlobalKeyword:
            case Kind.DeferKeyword:
                // When these don't start a declaration, they're an identifier in an expression statement
                return true;
            case Kind.AccessorKeyword:
            case Kind.PublicKeyword:
            case Kind.PrivateKeyword:
            case Kind.ProtectedKeyword:
            case Kind.StaticKeyword:
            case Kind.ReadonlyKeyword:
                // When these don't start a declaration, they may be the start of a class member if an identifier
                // immediately follows. Otherwise they're an identifier in an expression statement.
                return IsStartOfDeclaration() || !LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOnSameLine());

            default:
                return IsStartOfExpression();
        }
    }

    // ported from: internal/parser/parser.go:6123-6125
    private bool IsStartOfDeclaration()
    {
        return LookAhead(static p => p.ScanStartOfDeclaration());
    }

    // ported from: internal/parser/parser.go:6127-6199
    private bool ScanStartOfDeclaration()
    {
        while (true)
        {
            switch (_token)
            {
                case Kind.VarKeyword:
                case Kind.LetKeyword:
                case Kind.ConstKeyword:
                case Kind.FunctionKeyword:
                case Kind.ClassKeyword:
                case Kind.EnumKeyword:
                    return true;
                case Kind.UsingKeyword:
                    return IsUsingDeclaration();
                case Kind.AwaitKeyword:
                    return IsAwaitUsingDeclaration();
                // 'declare', 'module', 'namespace', 'interface'* and 'type' are all legal JavaScript identifiers;
                // however, an identifier cannot be followed by another identifier on the same line. This is what we
                // count on to parse out the respective declarations. For instance, we exploit this to say that
                //
                //    namespace n
                //
                // can be none other than the beginning of a namespace declaration, but need to respect that JavaScript sees
                //
                //    namespace
                //    n
                //
                // as the identifier 'namespace' on one line followed by the identifier 'n' on another.
                // We need to look one token ahead to see if it permissible to try parsing a declaration.
                //
                // *Note*: 'interface' is actually a strict mode reserved word. So while
                //
                //   "use strict"
                //   interface
                //   I {}
                //
                // could be legal, it would add complexity for very little gain.
                case Kind.InterfaceKeyword:
                case Kind.TypeKeyword:
                case Kind.DeferKeyword:
                    return NextTokenIsIdentifierOnSameLine();
                case Kind.ModuleKeyword:
                case Kind.NamespaceKeyword:
                    return NextTokenIsIdentifierOrStringLiteralOnSameLine();
                case Kind.AbstractKeyword:
                case Kind.AccessorKeyword:
                case Kind.AsyncKeyword:
                case Kind.DeclareKeyword:
                case Kind.PrivateKeyword:
                case Kind.ProtectedKeyword:
                case Kind.PublicKeyword:
                case Kind.ReadonlyKeyword:
                    var previousToken = _token;
                    NextToken();
                    // ASI takes effect for this modifier.
                    if (HasPrecedingLineBreak())
                    {
                        return false;
                    }
                    if (previousToken == Kind.DeclareKeyword && _token == Kind.TypeKeyword)
                    {
                        // If we see 'declare type', then commit to parsing a type alias. parseTypeAliasDeclaration will
                        // report Line_break_not_permitted_here if needed.
                        return true;
                    }
                    continue;
                case Kind.GlobalKeyword:
                    NextToken();
                    return _token == Kind.OpenBraceToken || _token == Kind.Identifier || _token == Kind.ExportKeyword;
                case Kind.ImportKeyword:
                    NextToken();
                    return _token == Kind.DeferKeyword || _token == Kind.StringLiteral || _token == Kind.AsteriskToken || _token == Kind.OpenBraceToken || TokenIsIdentifierOrKeyword(_token);
                case Kind.ExportKeyword:
                    NextToken();
                    if (_token == Kind.EqualsToken || _token == Kind.AsteriskToken || _token == Kind.OpenBraceToken ||
                        _token == Kind.DefaultKeyword || _token == Kind.AsKeyword || _token == Kind.AtToken)
                    {
                        return true;
                    }
                    if (_token == Kind.TypeKeyword)
                    {
                        NextToken();
                        return _token == Kind.AsteriskToken || _token == Kind.OpenBraceToken || IsIdentifier() && !HasPrecedingLineBreak();
                    }
                    continue;
                case Kind.StaticKeyword:
                    NextToken();
                    continue;
            }
            return false;
        }
    }

    // ported from: internal/parser/parser.go:6201-6222
    private bool IsStartOfExpression()
    {
        if (IsStartOfLeftHandSideExpression())
        {
            return true;
        }
        switch (_token)
        {
            case Kind.PlusToken:
            case Kind.MinusToken:
            case Kind.TildeToken:
            case Kind.ExclamationToken:
            case Kind.DeleteKeyword:
            case Kind.TypeOfKeyword:
            case Kind.VoidKeyword:
            case Kind.PlusPlusToken:
            case Kind.MinusMinusToken:
            case Kind.LessThanToken:
            case Kind.AwaitKeyword:
            case Kind.YieldKeyword:
            case Kind.PrivateIdentifier:
            case Kind.AtToken:
                // Yield/await always starts an expression.  Either it is an identifier (in which case
                // it is definitely an expression).  Or it's a keyword (either because we're in
                // a generator or async function, or in strict mode (or both)) and it started a yield or await expression.
                return true;
        }
        // Error tolerance.  If we see the start of some binary operator, we consider
        // that the start of an expression.  That way we'll parse out a missing identifier,
        // give a good message about an identifier being missing, and then consume the
        // rest of the binary expression.
        if (IsBinaryOperator())
        {
            return true;
        }
        return IsIdentifier();
    }

    // ported from: internal/parser/parser.go:6224-6235
    private bool IsStartOfLeftHandSideExpression()
    {
        switch (_token)
        {
            case Kind.ThisKeyword:
            case Kind.SuperKeyword:
            case Kind.NullKeyword:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateHead:
            case Kind.OpenParenToken:
            case Kind.OpenBracketToken:
            case Kind.OpenBraceToken:
            case Kind.FunctionKeyword:
            case Kind.ClassKeyword:
            case Kind.NewKeyword:
            case Kind.SlashToken:
            case Kind.SlashEqualsToken:
            case Kind.Identifier:
                return true;
            case Kind.ImportKeyword:
                return IsNextTokenOpenParenOrLessThanOrDot();
        }
        return IsIdentifier();
    }

    // ported from: internal/parser/parser.go:6237-6258
    private bool IsStartOfType(bool inStartOfParameter)
    {
        switch (_token)
        {
            case Kind.AnyKeyword:
            case Kind.UnknownKeyword:
            case Kind.StringKeyword:
            case Kind.NumberKeyword:
            case Kind.BigIntKeyword:
            case Kind.BooleanKeyword:
            case Kind.ReadonlyKeyword:
            case Kind.SymbolKeyword:
            case Kind.UniqueKeyword:
            case Kind.VoidKeyword:
            case Kind.UndefinedKeyword:
            case Kind.NullKeyword:
            case Kind.ThisKeyword:
            case Kind.TypeOfKeyword:
            case Kind.NeverKeyword:
            case Kind.OpenBraceToken:
            case Kind.OpenBracketToken:
            case Kind.LessThanToken:
            case Kind.BarToken:
            case Kind.AmpersandToken:
            case Kind.NewKeyword:
            case Kind.StringLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.ObjectKeyword:
            case Kind.AsteriskToken:
            case Kind.QuestionToken:
            case Kind.ExclamationToken:
            case Kind.DotDotDotToken:
            case Kind.InferKeyword:
            case Kind.ImportKeyword:
            case Kind.AssertsKeyword:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateHead:
                return true;
            case Kind.FunctionKeyword:
                return !inStartOfParameter;
            case Kind.MinusToken:
                return !inStartOfParameter && LookAhead(static p => p.NextTokenIsNumericOrBigIntLiteral());
            case Kind.OpenParenToken:
                // Only consider '(' the start of a type if followed by ')', '...', an identifier, a modifier,
                // or something that starts a type. We don't want to consider things like '(1)' a type.
                return !inStartOfParameter && LookAhead(static p => p.NextIsParenthesizedOrFunctionType());
        }
        return IsIdentifier();
    }

    // ported from: internal/parser/parser.go:6260-6263
    private bool NextTokenIsNumericOrBigIntLiteral()
    {
        NextToken();
        return _token == Kind.NumericLiteral || _token == Kind.BigIntLiteral;
    }

    // ported from: internal/parser/parser.go:6265-6268
    private bool NextIsParenthesizedOrFunctionType()
    {
        NextToken();
        return _token == Kind.CloseParenToken || IsStartOfParameter(false /*isJSDocParameter*/) || IsStartOfType(false /*inStartOfParameter*/);
    }

    // ported from: internal/parser/parser.go:6270-6276
    private bool IsStartOfParameter(bool isJSDocParameter)
    {
        return _token == Kind.DotDotDotToken ||
            IsBindingIdentifierOrPrivateIdentifierOrPattern() ||
            IsModifierKind(_token) ||
            _token == Kind.AtToken ||
            IsStartOfType(!isJSDocParameter /*inStartOfParameter*/);
    }

    // ported from: internal/parser/parser.go:6278-6280
    private bool IsBindingIdentifierOrPrivateIdentifierOrPattern()
    {
        return _token == Kind.OpenBraceToken || _token == Kind.OpenBracketToken || _token == Kind.PrivateIdentifier || IsBindingIdentifier();
    }

    // ported from: internal/parser/parser.go:6282-6284
    private bool IsNextTokenOpenParenOrLessThanOrDot()
    {
        return LookAhead(static p => p.NextTokenIsOpenParenOrLessThanOrDot());
    }

    // ported from: internal/parser/parser.go:6286-6292
    private bool NextTokenIsOpenParenOrLessThanOrDot()
    {
        switch (NextToken())
        {
            case Kind.OpenParenToken:
            case Kind.LessThanToken:
            case Kind.DotToken:
                return true;
        }
        return false;
    }

    // ported from: internal/parser/parser.go:6294-6297
    private bool NextTokenIsIdentifierOnSameLine()
    {
        NextToken();
        return IsIdentifier() && !HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:6299-6303
    private bool NextTokenIsIdentifierOrStringLiteralOnSameLine()
    {
        NextToken();
        return (IsIdentifier() || _token == Kind.StringLiteral) && !HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:6305-6317
    // Ignore strict mode flag because we will report an error in type checker instead.
    private bool IsIdentifier()
    {
        if (_token == Kind.Identifier)
        {
            return true;
        }
        // If we have a 'yield' keyword, and we're in the [yield] context, then 'yield' is
        // considered a keyword and is not an identifier.
        // If we have a 'await' keyword, and we're in the [Await] context, then 'await' is
        // considered a keyword and is not an identifier.
        if (_token == Kind.YieldKeyword && InYieldContext() || _token == Kind.AwaitKeyword && InAwaitContext())
        {
            return false;
        }
        return _token > Kind.LastReservedWord;
    }

    // ported from: internal/parser/parser.go:6319-6322
    private bool IsBindingIdentifier()
    {
        // `let await`/`let yield` in [Yield] or [Await] are allowed here and disallowed in the binder.
        return _token == Kind.Identifier || _token > Kind.LastReservedWord;
    }

    // ported from: internal/parser/parser.go:6324-6326
    private bool IsImportAttributeName()
    {
        return TokenIsIdentifierOrKeyword(_token) || _token == Kind.StringLiteral;
    }

    // ported from: internal/parser/parser.go:6328-6333
    private bool IsBinaryOperator()
    {
        if (InDisallowInContext() && _token == Kind.InKeyword)
        {
            return false;
        }
        return GetBinaryOperatorPrecedence(_token) != OperatorPrecedenceInvalid;
    }

    // ported from: internal/parser/parser.go:6335-6337
    private bool IsValidHeritageClauseObjectLiteral()
    {
        return LookAhead(static p => p.NextIsValidHeritageClauseObjectLiteral());
    }

    // ported from: internal/parser/parser.go:6339-6352
    private bool NextIsValidHeritageClauseObjectLiteral()
    {
        if (NextToken() == Kind.CloseBraceToken)
        {
            // if we see "extends {}" then only treat the {} as what we're extending (and not
            // the class body) if we have:
            //
            //      extends {} {
            //      extends {},
            //      extends {} extends
            //      extends {} implements
            var next = NextToken();
            return next == Kind.CommaToken || next == Kind.OpenBraceToken || next == Kind.ExtendsKeyword || next == Kind.ImplementsKeyword;
        }
        return true;
    }

    // ported from: internal/parser/parser.go:6354-6356
    private bool IsHeritageClause()
    {
        return _token == Kind.ExtendsKeyword || _token == Kind.ImplementsKeyword;
    }

    // ported from: internal/parser/parser.go:6358-6360
    private bool IsHeritageClauseExtendsOrImplementsKeyword()
    {
        return IsHeritageClause() && LookAhead(static p => p.NextIsStartOfExpression());
    }

    // ported from: internal/parser/parser.go:6362-6365
    private bool NextIsStartOfExpression()
    {
        NextToken();
        return IsStartOfExpression();
    }

    // ported from: internal/parser/parser.go:6367-6374
    private bool IsUsingDeclaration()
    {
        // 'using' always starts a lexical declaration if followed by an identifier. We also eagerly parse
        // |ObjectBindingPattern| so that we can report a grammar error during check. We don't parse out
        // |ArrayBindingPattern| since it potentially conflicts with element access (i.e., `using[x]`).
        return LookAhead(static p => p.NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine( /*disallowOf*/ false));
    }

    // ported from: internal/parser/parser.go:6376-6379
    private bool NextTokenIsEqualsOrSemicolonOrColonToken()
    {
        NextToken();
        return _token == Kind.EqualsToken || _token == Kind.SemicolonToken || _token == Kind.ColonToken;
    }

    // ported from: internal/parser/parser.go:6381-6387
    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine(bool disallowOf)
    {
        NextToken();
        if (disallowOf && _token == Kind.OfKeyword)
        {
            return LookAhead(static p => p.NextTokenIsEqualsOrSemicolonOrColonToken());
        }
        return (IsBindingIdentifier() || _token == Kind.OpenBraceToken) && !HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:6389-6391
    private bool NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLineDisallowOf()
    {
        return NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine( /*disallowOf*/ true);
    }

    // ported from: internal/parser/parser.go:6393-6395
    private bool IsAwaitUsingDeclaration()
    {
        return LookAhead(static p => p.NextIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine());
    }

    // ported from: internal/parser/parser.go:6397-6399
    private bool NextIsUsingKeywordThenBindingIdentifierOrStartOfObjectDestructuringOnSameLine()
    {
        return NextToken() == Kind.UsingKeyword && NextTokenIsBindingIdentifierOrStartOfDestructuringOnSameLine( /*disallowOf*/ false);
    }

    // ported from: internal/parser/parser.go:6401-6403
    private bool NextTokenIsTokenStringLiteral()
    {
        return NextToken() == Kind.StringLiteral;
    }

    // ported from: internal/parser/parser.go:6405-6411
    private void SetContextFlags(NodeFlags flags, bool value)
    {
        if (value)
        {
            _contextFlags |= flags;
        }
        else
        {
            _contextFlags &= ~flags;
        }
    }

    // ported from: internal/parser/parser.go:6413-6419
    private T DoInContext<T>(NodeFlags flags, bool value, Func<Parser, T> f)
    {
        var saveContextFlags = _contextFlags;
        SetContextFlags(flags, value);
        var result = f(this);
        _contextFlags = saveContextFlags;
        return result;
    }

    // ported from: internal/parser/parser.go:6421-6423
    private bool InYieldContext()
    {
        return (_contextFlags & NodeFlags.YieldContext) != 0;
    }

    // ported from: internal/parser/parser.go:6425-6427
    private bool InDisallowInContext()
    {
        return (_contextFlags & NodeFlags.DisallowInContext) != 0;
    }

    // ported from: internal/parser/parser.go:6429-6431
    private bool InDisallowConditionalTypesContext()
    {
        return (_contextFlags & NodeFlags.DisallowConditionalTypesContext) != 0;
    }

    // ported from: internal/parser/parser.go:6433-6435
    private bool InDecoratorContext()
    {
        return (_contextFlags & NodeFlags.DecoratorContext) != 0;
    }

    // ported from: internal/parser/parser.go:6437-6439
    private bool InAwaitContext()
    {
        return (_contextFlags & NodeFlags.AwaitContext) != 0;
    }

    // ported from: internal/parser/parser.go:6441-6443
    private TextRange SkipRangeTrivia(TextRange textRange)
    {
        return NewTextRange(SkipTrivia(_sourceText, textRange.Pos()), textRange.End());
    }

    // ported from: internal/parser/parser.go:6445-6447
    internal static bool IsReservedWord(Kind token)
    {
        return Kind.FirstReservedWord <= token && token <= Kind.LastReservedWord;
    }

    // ported from: internal/parser/parser.go:6449-6457
    internal static Diagnostic[] AttachFileToDiagnostics(Diagnostic[] diagnostics, SourceFile file)
    {
        foreach (var d in diagnostics)
        {
            d.SetFile(file);
            foreach (var r in d.RelatedInformation() ?? [])
            {
                r.SetFile(file);
            }
        }
        return diagnostics;
    }

    // ported from: internal/parser/parser.go:6459-6465
    internal static Pragma[] GetCommentPragmas(NodeFactory f, string sourceText)
    {
        var pragmas = new List<Pragma>();
        foreach (var commentRange in GetLeadingCommentRanges(f, sourceText, 0))
        {
            // Go: `comment := sourceText[commentRange.Pos():commentRange.End()]`（零拷贝切片）→ span（§3.8）
            var comment = sourceText.AsSpan(commentRange.Range.Pos(), commentRange.Range.End() - commentRange.Range.Pos());
            pragmas.AddRange(ExtractPragmas(commentRange, comment));
        }
        return pragmas.ToArray();
    }

    // ported from: internal/parser/parser.go:6467-6569
    private static Pragma[] ExtractPragmas(CommentRange commentRange, ReadOnlySpan<char> text)
    {
        if (commentRange.Kind == Kind.SingleLineCommentTrivia)
        {
            var pos = 2;
            var tripleSlash = Match(text, pos, "/");
            if (tripleSlash)
            {
                pos++;
            }
            pos = SkipBlanks(text, pos);
            if (tripleSlash && Match(text, pos, "<"))
            {
                var tagName = ExtractName(text, pos + 1);
                if (tagName != "reference")
                {
                    return [];
                }
                pos += 10;
                var args = new Dictionary<string, PragmaArgument>();
                while (true)
                {
                    pos = SkipBlanks(text, pos);
                    if (Match(text, pos, "/>"))
                    {
                        break;
                    }
                    var argName = ExtractName(text, pos);
                    if (argName == "")
                    {
                        break;
                    }
                    pos = SkipBlanks(text, pos + argName.Length);
                    if (!Match(text, pos, "="))
                    {
                        break;
                    }
                    pos = SkipBlanks(text, pos + 1);
                    if (!TryExtractQuotedString(text, pos, out var value))
                    {
                        break;
                    }
                    args[argName] = new PragmaArgument
                    {
                        Name = argName,
                        Value = value,
                        Range = NewTextRange(commentRange.Range.Pos() + pos + 1, commentRange.Range.Pos() + pos + 1 + value.Length),
                    };
                    pos += value.Length + 2;
                }
                return
                [
                    new Pragma
                    {
                        // Go: `CommentRange: commentRange`（内嵌结构体拷贝）—— C# 没有内嵌，逐字段拷
                        Range = commentRange.Range,
                        Kind = commentRange.Kind,
                        HasTrailingNewLine = commentRange.HasTrailingNewLine,
                        Name = "reference",
                        Args = args,
                    },
                ];
            }
            if (Match(text, pos, "@"))
            {
                pos++;
                var pragmaName = ExtractName(text, pos);
                if (!(pragmaName == "ts-check" || pragmaName == "ts-nocheck"))
                {
                    return [];
                }
                return
                [
                    new Pragma
                    {
                        Range = commentRange.Range,
                        Kind = commentRange.Kind,
                        HasTrailingNewLine = commentRange.HasTrailingNewLine,
                        Name = pragmaName,
                    },
                ];
            }
        }
        if (commentRange.Kind == Kind.MultiLineCommentTrivia)
        {
            // Go: `text = strings.TrimSuffix(text, "*/")`
            if (text.EndsWith("*/", StringComparison.Ordinal))
            {
                text = text[..^2];
            }
            var pos = 2;
            var pragmas = new List<Pragma>();
            while (true)
            {
                pos = SkipTo(text, pos, "@");
                if (pos < 0)
                {
                    break;
                }
                // Mirrors the /@(\S+)(\s+(?:\S.*)?)?$/gm pragma regex used by TypeScript: the '@'
                // must be immediately followed by a non-whitespace pragma name, and the remainder
                // of the line is consumed as that pragma's arguments. As a consequence, only the
                // first '@'-token on a line is considered, so an unrelated '@token' earlier on the
                // line (e.g. an email address) prevents a later '@jsx' on the same line from being
                // treated as a pragma.
                var namePos = pos + 1;
                var nameEnd = SkipNonBlanks(text, namePos);
                if (nameEnd == namePos)
                {
                    pos++;
                    continue;
                }
                var lineEnd = LineEndPos(text, pos);
                var pragmaName = ToLowerJS(text[namePos..nameEnd].ToString());
                if (pragmaName == "jsx" || pragmaName == "jsxfrag" || pragmaName == "jsximportsource" || pragmaName == "jsxruntime")
                {
                    var start = SkipBlanks(text, nameEnd);
                    var argEnd = SkipNonBlanks(text, start);
                    if (argEnd != start)
                    {
                        var args = new Dictionary<string, PragmaArgument>(1)
                        {
                            ["factory"] = new PragmaArgument
                            {
                                Name = "factory",
                                Value = text[start..argEnd].ToString(),
                                Range = NewTextRange(commentRange.Range.Pos() + start, commentRange.Range.Pos() + argEnd),
                            },
                        };
                        pragmas.Add(new Pragma
                        {
                            Range = commentRange.Range,
                            Kind = commentRange.Kind,
                            HasTrailingNewLine = commentRange.HasTrailingNewLine,
                            Name = pragmaName,
                            Args = args,
                        });
                    }
                }
                pos = lineEnd;
            }
            return pragmas.ToArray();
        }
        return [];
    }

    // ported from: internal/parser/parser.go:6571-6573
    private static bool Match(ReadOnlySpan<char> text, int pos, string s)
    {
        return text[pos..].StartsWith(s, StringComparison.Ordinal);
    }

    // ported from: internal/parser/parser.go:6575-6580
    private static int SkipBlanks(ReadOnlySpan<char> text, int pos)
    {
        while (pos < text.Length && (text[pos] == ' ' || text[pos] == '\t'))
        {
            pos++;
        }
        return pos;
    }

    // ported from: internal/parser/parser.go:6582-6587
    private static int SkipNonBlanks(ReadOnlySpan<char> text, int pos)
    {
        while (pos < text.Length && (text[pos] != ' ' && text[pos] != '\t' && text[pos] != '\r' && text[pos] != '\n'))
        {
            pos++;
        }
        return pos;
    }

    // ported from: internal/parser/parser.go:6589-6598
    private static int SkipTo(ReadOnlySpan<char> text, int pos, string s)
    {
        if (pos >= text.Length)
        {
            return -1;
        }
        var i = text[pos..].IndexOf(s, StringComparison.Ordinal);
        if (i < 0)
        {
            return -1;
        }
        return pos + i;
    }

    // ported from: internal/parser/parser.go:6600-6609
    private static int LineEndPos(ReadOnlySpan<char> text, int pos)
    {
        while (pos < text.Length)
        {
            var ch = text[pos];
            if (IsLineBreak(ch))
            {
                return pos;
            }
            // Go 的 `utf8.DecodeRuneInString` 按字节推进；C# 按 UTF-16 代码单元推进，
            // 两者都停在同一个换行字符上，返回值各自对应自己那套单位（§3.1）。
            pos++;
        }
        return text.Length;
    }

    // ported from: internal/parser/parser.go:6611-6617
    private static string ExtractName(ReadOnlySpan<char> text, int pos)
    {
        var start = pos;
        while (pos < text.Length && (text[pos] >= 'A' && text[pos] <= 'Z' || text[pos] >= 'a' && text[pos] <= 'z' || text[pos] == '-'))
        {
            pos++;
        }
        return ToLowerJS(text[start..pos].ToString());
    }

    // ported from: internal/parser/parser.go:6619-6636（Go 的 `(string, bool)` → `bool TryX(..., out string)`）
    private static bool TryExtractQuotedString(ReadOnlySpan<char> text, int pos, out string result)
    {
        result = "";
        if (pos == text.Length)
        {
            return false;
        }
        var quote = text[pos];
        if (quote != '\'' && quote != '"')
        {
            return false;
        }
        pos++;
        var start = pos;
        while (pos < text.Length && text[pos] != quote)
        {
            pos++;
        }
        if (pos == text.Length)
        {
            return false;
        }
        result = text[start..pos].ToString();
        return true;
    }

    // ported from: internal/parser/parser.go:6638-6696
    private void ProcessPragmasIntoFields(SourceFile context)
    {
        context.CheckJsDirective = null;
        context.ReferencedFiles = null;
        context.TypeReferenceDirectives = null;
        context.LibReferenceDirectives = null;
        // context.AmdDependencies = nil
        // Go 是 `append(context.X, …)`（从 nil 起步）：结束时若为空则仍是 nil，这里逐字段保持同样的“空 = null”。
        var referencedFiles = new List<FileReference>();
        var typeReferenceDirectives = new List<FileReference>();
        var libReferenceDirectives = new List<FileReference>();
        foreach (var pragma in context.Pragmas ?? [])
        {
            switch (pragma.Name)
            {
                case "reference":
                    {
                        var args = pragma.Args;
                        var typesOk = args?.ContainsKey("types") ?? false;
                        var libOk = args?.ContainsKey("lib") ?? false;
                        var pathOk = args?.ContainsKey("path") ?? false;
                        var resolutionModeOk = args?.ContainsKey("resolution-mode") ?? false;
                        var preserveOk = args?.ContainsKey("preserve") ?? false;
                        var noDefaultLibOk = args?.ContainsKey("no-default-lib") ?? false;
                        if (noDefaultLibOk && args!["no-default-lib"].Value == "true")
                        {
                            // Ignored.
                        }
                        else if (typesOk)
                        {
                            var types = args!["types"];
                            var parsed = default(ModuleKind);
                            if (resolutionModeOk)
                            {
                                var resolutionMode = args!["resolution-mode"];
                                parsed = ParseResolutionMode(resolutionMode.Value, resolutionMode.Range.Pos(), resolutionMode.Range.End());
                            }
                            typeReferenceDirectives.Add(new FileReference
                            {
                                Range = types.Range,
                                FileName = types.Value,
                                ResolutionMode = parsed,
                                Preserve = preserveOk && args!["preserve"].Value == "true",
                            });
                        }
                        else if (libOk)
                        {
                            var lib = args!["lib"];
                            libReferenceDirectives.Add(new FileReference
                            {
                                Range = lib.Range,
                                FileName = lib.Value,
                                Preserve = preserveOk && args!["preserve"].Value == "true",
                            });
                        }
                        else if (pathOk)
                        {
                            var path = args!["path"];
                            referencedFiles.Add(new FileReference
                            {
                                Range = path.Range,
                                FileName = path.Value,
                                Preserve = preserveOk && args!["preserve"].Value == "true",
                            });
                        }
                        else
                        {
                            ParseErrorAtRange(pragma.Range, Invalid_reference_directive_syntax);
                        }
                        break;
                    }
                case "ts-check":
                case "ts-nocheck":
                    // _last_ of either nocheck or check in a file is the "winner"
                    // Go 的 `context.CheckJsDirective.Range.Pos()`：Go 里 CommentRange 内嵌 core.TextRange（Pos 被提升），
                    // C# 的 CommentRange 把 TextRange 存成字段 `Range` → 多一层 `.Range`。
                    if (context.CheckJsDirective is null || pragma.Range.Pos() > context.CheckJsDirective.Range.Range.Pos())
                    {
                        context.CheckJsDirective = new CheckJsDirective
                        {
                            Enabled = pragma.Name == "ts-check",
                            Range = new CommentRange
                            {
                                Range = pragma.Range,
                                Kind = pragma.Kind,
                                HasTrailingNewLine = pragma.HasTrailingNewLine,
                            },
                        };
                    }
                    break;
                case "jsx":
                case "jsxfrag":
                case "jsximportsource":
                case "jsxruntime":
                    // Nothing to do here
                    break;
                default:
                    throw new DebugFailureException("Unhandled pragma kind: " + pragma.Name);
            }
        }
        context.ReferencedFiles = referencedFiles.Count != 0 ? referencedFiles.ToArray() : null;
        context.TypeReferenceDirectives = typeReferenceDirectives.Count != 0 ? typeReferenceDirectives.ToArray() : null;
        context.LibReferenceDirectives = libReferenceDirectives.Count != 0 ? libReferenceDirectives.ToArray() : null;
    }

    // ported from: internal/parser/parser.go:6698-6709
    private ModuleKind ParseResolutionMode(string mode, int pos, int end)
    {
        if (mode == "import")
        {
            return ModuleKind.ESNext;
        }
        if (mode == "require")
        {
            return ModuleKind.CommonJS;
        }
        ParseErrorAt(pos, end, X_resolution_mode_should_be_either_require_or_import);
        // Go: 命名返回值 resolutionKind 的零值（= core.ModuleKindNone）
        return default;
    }

    // ported from: internal/parser/parser.go:6711-6713
    private void JsErrorAtRange(TextRange loc, Message message, params object?[] args)
    {
        _jsDiagnostics.Add(NewDiagnostic(null, NewTextRange(SkipTrivia(_sourceText, loc.Pos()), loc.End()), message, args));
    }

    // ported from: internal/parser/parser.go:6715-6767
    private void CheckJSDecoratorSyntax(Node node)
    {
        var modifiers = node.ModifierNodes();
        if (modifiers is null || modifiers.Length == 0)
        {
            return;
        }

        if (CanHaveIllegalDecorators(node))
        {
            foreach (var modifier in modifiers)
            {
                if (IsDecorator(modifier))
                {
                    JsErrorAtRange(modifier.Loc, Decorators_are_not_valid_here);
                    break;
                }
            }
        }
        else if (CanHaveDecorators(node))
        {
            var decoratorIndex = FindIndex(modifiers, static m => IsDecorator(m));
            if (decoratorIndex >= 0)
            {
                if (IsClassDeclaration(node))
                {
                    var exportIndex = FindIndex(modifiers, static m => IsExportModifier(m));
                    if (exportIndex >= 0)
                    {
                        var defaultIndex = FindIndex(modifiers, static m => m.Kind == Kind.DefaultKeyword);
                        if (decoratorIndex > exportIndex && defaultIndex >= 0 && decoratorIndex < defaultIndex)
                        {
                            // Decorator between `export` and `default`
                            JsErrorAtRange(modifiers[decoratorIndex].Loc, Decorators_are_not_valid_here);
                        }
                        else if (decoratorIndex < exportIndex)
                        {
                            // Find a trailing decorator after the export keyword
                            var trailingDecoratorIndex = -1;
                            for (var i = exportIndex; i < modifiers.Length; i++)
                            {
                                if (IsDecorator(modifiers[i]))
                                {
                                    trailingDecoratorIndex = i;
                                    break;
                                }
                            }
                            if (trailingDecoratorIndex >= 0)
                            {
                                var diag = NewDiagnostic(
                                    null,
                                    NewTextRange(SkipTrivia(_sourceText, modifiers[trailingDecoratorIndex].Loc.Pos()), modifiers[trailingDecoratorIndex].Loc.End()),
                                    Decorators_may_not_appear_after_export_or_export_default_if_they_also_appear_before_export);
                                diag.AddRelatedInfo(NewDiagnostic(
                                    null,
                                    NewTextRange(SkipTrivia(_sourceText, modifiers[decoratorIndex].Loc.Pos()), modifiers[decoratorIndex].Loc.End()),
                                    Decorator_used_before_export_here));
                                _jsDiagnostics.Add(diag);
                            }
                        }
                    }
                }
            }
        }
    }

    // ported from: internal/parser/parser.go:6769-6854
    private Node CheckJSSyntax(Node node)
    {
        if ((node.Flags & NodeFlags.JavaScriptFile) == 0 || (node.Flags & (NodeFlags.JSDoc | NodeFlags.Reparsed)) != 0)
        {
            return node;
        }
        switch (node.Kind)
        {
            // Go: `case Parameter, PropertyDeclaration, MethodDeclaration:` 有 fallthrough 落到下面的 case，
            // C# 无 fallthrough → 合并 case 标签，组内用 `node.Kind is …` 还原“只对前三个生效”的那段语句体。
            case Kind.Parameter:
            case Kind.PropertyDeclaration:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionExpression:
            case Kind.FunctionDeclaration:
            case Kind.ArrowFunction:
            case Kind.VariableDeclaration:
            case Kind.IndexSignature:
                if (node.Kind is Kind.Parameter or Kind.PropertyDeclaration or Kind.MethodDeclaration)
                {
                    var token = node.QuestionToken();
                    if (token is not null && (token.Flags & NodeFlags.Reparsed) == 0 && IsQuestionToken(token))
                    {
                        JsErrorAtRange(token.Loc, The_0_modifier_can_only_be_used_in_TypeScript_files, "?");
                    }
                }
                if (IsFunctionLike(node) && node.Body() is null)
                {
                    JsErrorAtRange(node.Loc, Signature_declarations_can_only_be_used_in_TypeScript_files);
                }
                else if (node.Type() is { } t && (t.Flags & NodeFlags.Reparsed) == 0)
                {
                    JsErrorAtRange(t.Loc, Type_annotations_can_only_be_used_in_TypeScript_files);
                }
                break;
            case Kind.ImportDeclaration:
                {
                    var clause = node.ImportClause();
                    if (clause is not null && clause.IsTypeOnly())
                    {
                        JsErrorAtRange(node.Loc, X_0_declarations_can_only_be_used_in_TypeScript_files, "import type");
                    }
                    break;
                }
            case Kind.ExportDeclaration:
                if (node.IsTypeOnly())
                {
                    JsErrorAtRange(node.Loc, X_0_declarations_can_only_be_used_in_TypeScript_files, "export type");
                }
                break;
            case Kind.ImportSpecifier:
                if (node.IsTypeOnly())
                {
                    JsErrorAtRange(node.Loc, X_0_declarations_can_only_be_used_in_TypeScript_files, "import...type");
                }
                break;
            case Kind.ExportSpecifier:
                if (node.IsTypeOnly())
                {
                    JsErrorAtRange(node.Loc, X_0_declarations_can_only_be_used_in_TypeScript_files, "export...type");
                }
                break;
            case Kind.ImportEqualsDeclaration:
                JsErrorAtRange(node.Loc, X_import_can_only_be_used_in_TypeScript_files);
                break;
            case Kind.ExportAssignment:
                if (node.AsExportAssignment()!.IsExportEquals)
                {
                    JsErrorAtRange(node.Loc, X_export_can_only_be_used_in_TypeScript_files);
                }
                break;
            case Kind.HeritageClause:
                if (node.AsHeritageClause()!.Token == Kind.ImplementsKeyword)
                {
                    JsErrorAtRange(node.Loc, X_implements_clauses_can_only_be_used_in_TypeScript_files);
                }
                break;
            case Kind.InterfaceDeclaration:
                JsErrorAtRange(node.Name()!.Loc, X_0_declarations_can_only_be_used_in_TypeScript_files, "interface");
                break;
            case Kind.ModuleDeclaration:
                JsErrorAtRange(node.Name()!.Loc, X_0_declarations_can_only_be_used_in_TypeScript_files, TokenToString(node.AsModuleDeclaration()!.Keyword));
                break;
            case Kind.TypeAliasDeclaration:
                JsErrorAtRange(node.Name()!.Loc, Type_aliases_can_only_be_used_in_TypeScript_files);
                break;
            case Kind.EnumDeclaration:
                JsErrorAtRange(node.Name()!.Loc, X_0_declarations_can_only_be_used_in_TypeScript_files, "enum");
                break;
            case Kind.NonNullExpression:
                JsErrorAtRange(node.Loc, Non_null_assertions_can_only_be_used_in_TypeScript_files);
                break;
            case Kind.AsExpression:
                JsErrorAtRange(node.Type()!.Loc, Type_assertion_expressions_can_only_be_used_in_TypeScript_files);
                break;
            case Kind.SatisfiesExpression:
                JsErrorAtRange(node.Type()!.Loc, Type_satisfaction_expressions_can_only_be_used_in_TypeScript_files);
                break;
        }
        // Check decorator placement in JS files
        CheckJSDecoratorSyntax(node);
        // Check absence of type parameters, type arguments and non-JavaScript modifiers
        // Go 的 `case 类/函数…:` 里检查完类型参数后 fallthrough 到 `case VariableStatement, PropertyDeclaration:`
        // 的修饰符循环；C# 无 fallthrough → 用一个布尔量记录“第一组命中”，再决定是否进入同一个循环。
        var isClassOrFunctionLike = node.Kind is Kind.ClassDeclaration or Kind.ClassExpression or Kind.MethodDeclaration or Kind.Constructor
            or Kind.GetAccessor or Kind.SetAccessor or Kind.FunctionExpression or Kind.FunctionDeclaration or Kind.ArrowFunction;
        if (isClassOrFunctionLike)
        {
            var list = node.TypeParameterList();
            if (list is not null && Some(list.Nodes, static n => (n.Flags & NodeFlags.Reparsed) == 0))
            {
                JsErrorAtRange(list.Loc, Type_parameter_declarations_can_only_be_used_in_TypeScript_files);
            }
        }
        if (isClassOrFunctionLike || node.Kind is Kind.VariableStatement or Kind.PropertyDeclaration)
        {
            foreach (var modifier in node.ModifierNodes() ?? [])
            {
                if ((modifier.Flags & NodeFlags.Reparsed) == 0 && modifier.Kind != Kind.Decorator && (ModifierToFlag(modifier.Kind) & ModifierFlags.JavaScript) == 0)
                {
                    JsErrorAtRange(modifier.Loc, The_0_modifier_can_only_be_used_in_TypeScript_files, TokenToString(modifier.Kind));
                }
            }
        }
        else if (node.Kind == Kind.Parameter)
        {
            if (Some(node.ModifierNodes(), static n => IsModifier(n)))
            {
                JsErrorAtRange(node.Modifiers()!.Loc, Parameter_modifiers_can_only_be_used_in_TypeScript_files);
            }
        }
        else if (node.Kind is Kind.CallExpression or Kind.NewExpression or Kind.ExpressionWithTypeArguments or Kind.JsxSelfClosingElement
            or Kind.JsxOpeningElement or Kind.TaggedTemplateExpression)
        {
            var list = node.TypeArgumentList();
            if (list is not null && Some(list.Nodes, static n => (n.Flags & NodeFlags.Reparsed) == 0))
            {
                JsErrorAtRange(list.Loc, Type_arguments_can_only_be_used_in_TypeScript_files);
            }
        }
        return node;
    }
}
