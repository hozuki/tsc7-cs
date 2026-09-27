// ported from: internal/parser/parser.go:3534-5053（函数体/修饰符解析、类型字面量/元组/模板类型、表达式与运算符
//               优先级、箭头函数、JSX 解析）
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.1/§3.3/§3.8）：
// 1) 包级函数（Go 的 `shouldConsumeBinaryOperator`/`typeHasArrowFunctionBlockingParseError`）→ 同一个
//    `partial class Parser` 上的 `private static` 方法（一个 Go package = 一个 C# 类型，见 §2）。
// 2) `ast.`/`core.`/`scanner.`/`diagnostics.` 的包级函数与常量用 `using static` 后按 Go 的裸名调用：本文件在
//    `TypeScript.Parser` 命名空间里，写限定名（`Ast.IsBinaryExpression`）会先解析到同级的 `TypeScript.Ast`
//    命名空间，而 C# 只对命名空间做成员查找、不落到成员。
// 3) Go 的 `func(p *Parser) *ast.Node` 回调 → `Func<Parser, Node?>`；调用点写 `static p => p.Foo()`（不捕获
//    任何变量，无闭包分配）。
// 4) Go 的 `p.lookAhead(func(p *Parser) bool { … })` 闭包 → `LookAhead(static p => { … })`。
// 5) `p.parsingContexts |= 1 << PCJsxChildren` → `_parsingContexts |= (ParsingContexts)(1 << (int)ParsingContext.JsxChildren);`
//    （W1 的 `ParsingContexts` 是 readonly struct 包装 int，`ParsingContext` 的成员去掉 `PC` 前缀）。
// 6) Go 的 `append(nodes[:len-1], newLast)`（JSX 错误恢复）→ 新数组 + `Array.Copy`（Go 是就地改写，长度不变）。
// 7) `panic(...)` → `throw new DebugFailureException(...)`；Go 的 nil 解引用 → `!`（C# 抛 NRE，等价）。
//    少数 W5 侧的 helper（`ParseLeftHandSideExpressionOrHigher`/`ParseSimpleUnaryExpression`）在 C# 里标了
//    `Node?`（比 Go 的 `*ast.Expression` 保守）：Go 在这些点从不返回 nil（返回 nil 时 Go 也是 nil 解引用），
//    所以这里用 `!` 保持与 Go 逐行一致的语义，不用 `?? 兜底`。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Parser;

internal sealed partial class Parser
{
    // ported from: internal/parser/parser.go:3534-3547
    private Node? ParseFunctionBlockOrSemicolon(ParseFlags flags, Message? diagnosticMessage)
    {
        if (_token != Kind.OpenBraceToken)
        {
            if ((flags & ParseFlags.Type) != 0)
            {
                ParseTypeMemberSemicolon();
                return null;
            }
            if (CanParseSemicolon())
            {
                ParseSemicolon();
                return null;
            }
        }
        return ParseFunctionBlock(flags, diagnosticMessage);
    }

    // ported from: internal/parser/parser.go:3548-3561
    private Node ParseFunctionBlock(ParseFlags flags, Message? diagnosticMessage)
    {
        var saveContextFlags = _contextFlags;
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;
        SetContextFlags(NodeFlags.YieldContext, (flags & ParseFlags.Yield) != 0);
        SetContextFlags(NodeFlags.AwaitContext, (flags & ParseFlags.Await) != 0);
        // We may be in a [Decorator] context when parsing a function expression or
        // arrow function. The body of the function is not in [Decorator] context.
        SetContextFlags(NodeFlags.DecoratorContext, false);
        var block = ParseBlock((flags & ParseFlags.IgnoreMissingOpenBrace) != 0, diagnosticMessage);
        _contextFlags = saveContextFlags;
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;
        return block;
    }

    // ported from: internal/parser/parser.go:3562-3565
    private bool IsIndexSignature()
    {
        return _token == Kind.OpenBracketToken && LookAhead(static p => p.NextIsUnambiguouslyIndexSignature());
    }

    // ported from: internal/parser/parser.go:3566-3614
    private bool NextIsUnambiguouslyIndexSignature()
    {
        // The only allowed sequence is:
        //
        //   [id:
        //
        // However, for error recovery, we also check the following cases:
        //
        //   [...
        //   [id,
        //   [id?,
        //   [id?:
        //   [id?]
        //   [public id
        //   [private id
        //   [protected id
        //   []
        //
        NextToken();
        if (_token == Kind.DotDotDotToken || _token == Kind.CloseBracketToken)
        {
            return true;
        }
        if (IsModifierKind(_token))
        {
            NextToken();
            if (IsIdentifier())
            {
                return true;
            }
        }
        else if (!IsIdentifier())
        {
            return false;
        }
        else
        {
            // Skip the identifier
            NextToken();
        }
        // A colon signifies a well formed indexer
        // A comma should be a badly formed indexer because comma expressions are not allowed
        // in computed properties.
        if (_token == Kind.ColonToken || _token == Kind.CommaToken)
        {
            return true;
        }
        // Question mark could be an indexer with an optional property,
        // or it could be a conditional expression in a computed property.
        if (_token != Kind.QuestionToken)
        {
            return false;
        }
        // If any of the following tokens are after the question mark, it cannot
        // be a conditional expression, so treat it as an indexer.
        NextToken();
        return _token == Kind.ColonToken || _token == Kind.CommaToken || _token == Kind.CloseBracketToken;
    }

    // ported from: internal/parser/parser.go:3615-3623
    private Node ParseIndexSignatureDeclaration(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var parameters = ParseBracketedList(ParsingContext.Parameters, static p => p.ParseParameter()!, Kind.OpenBracketToken, Kind.CloseBracketToken);
        var typeNode = ParseTypeAnnotation();
        ParseTypeMemberSemicolon();
        var result = FinishNode(_factory.NewIndexSignatureDeclaration(modifiers, parameters, typeNode), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    // ported from: internal/parser/parser.go:3624-3650
    private Node ParsePropertyOrMethodSignature(int pos, JsdocScannerInfo jsdoc, ModifierList? modifiers)
    {
        var name = ParsePropertyName();
        var questionToken = ParseOptionalToken(Kind.QuestionToken);
        Node result;
        if (_token == Kind.OpenParenToken || _token == Kind.LessThanToken)
        {
            // Method signatures don't exist in expression contexts.  So they have neither
            // [Yield] nor [Await]
            var typeParameters = ParseTypeParameters();
            var parameters = ParseParameters(ParseFlags.Type);
            var returnType = ParseReturnType(Kind.ColonToken /*isType*/, true);
            result = _factory.NewMethodSignatureDeclaration(modifiers, name, questionToken, typeParameters, parameters, returnType);
        }
        else
        {
            var typeNode = ParseTypeAnnotation();
            // Although type literal properties cannot not have initializers, we attempt
            // to parse an initializer so we can report in the checker that an interface
            // property or type literal property cannot have an initializer.
            Node? initializer = null;
            if (_token == Kind.EqualsToken)
            {
                initializer = ParseInitializer();
            }
            result = _factory.NewPropertySignatureDeclaration(modifiers, name, questionToken, typeNode, initializer);
        }
        ParseTypeMemberSemicolon();
        WithJSDoc(FinishNode(result, pos), jsdoc);
        return result;
    }

    // ported from: internal/parser/parser.go:3651-3656
    private Node ParseTypeLiteral()
    {
        var pos = NodePos();
        var result = FinishNode(_factory.NewTypeLiteralNode(ParseObjectTypeMembers()), pos);
        return result;
    }

    // ported from: internal/parser/parser.go:3657-3665
    private NodeList ParseObjectTypeMembers()
    {
        if (ParseExpected(Kind.OpenBraceToken))
        {
            var members = ParseList(ParsingContext.TypeMembers, static p => p.ParseTypeMember());
            ParseExpected(Kind.CloseBraceToken);
            return members;
        }
        return CreateMissingList();
    }

    // ported from: internal/parser/parser.go:3666-3670
    private Node ParseTupleType()
    {
        var pos = NodePos();
        return FinishNode(_factory.NewTupleTypeNode(ParseBracketedList(ParsingContext.TupleElementTypes, static p => p.ParseTupleElementNameOrTupleElementType(), Kind.OpenBracketToken, Kind.CloseBracketToken)), pos);
    }

    // ported from: internal/parser/parser.go:3671-3686
    private Node ParseTupleElementNameOrTupleElementType()
    {
        if (LookAhead(static p => p.ScanStartOfNamedTupleElement()))
        {
            var pos = NodePos();
            var jsdoc = JsdocScannerInfo;
            var dotDotDotToken = ParseOptionalToken(Kind.DotDotDotToken);
            var name = ParseIdentifierName();
            var questionToken = ParseOptionalToken(Kind.QuestionToken);
            ParseExpected(Kind.ColonToken);
            var typeNode = ParseTupleElementType();
            var result = FinishNode(_factory.NewNamedTupleMember(dotDotDotToken, name, questionToken, typeNode), pos);
            WithJSDoc(result, jsdoc);
            return result;
        }
        return ParseTupleElementType();
    }

    // ported from: internal/parser/parser.go:3687-3693
    private bool ScanStartOfNamedTupleElement()
    {
        if (_token == Kind.DotDotDotToken)
        {
            return TokenIsIdentifierOrKeyword(NextToken()) && NextTokenIsColonOrQuestionColon();
        }
        return TokenIsIdentifierOrKeyword(_token) && NextTokenIsColonOrQuestionColon();
    }

    // ported from: internal/parser/parser.go:3694-3697
    private bool NextTokenIsColonOrQuestionColon()
    {
        return NextToken() == Kind.ColonToken || _token == Kind.QuestionToken && NextToken() == Kind.ColonToken;
    }

    // ported from: internal/parser/parser.go:3698-3713
    private Node ParseTupleElementType()
    {
        var pos = NodePos();
        if (ParseOptional(Kind.DotDotDotToken))
        {
            return FinishNode(_factory.NewRestTypeNode(ParseType()), pos);
        }
        var typeNode = ParseType();
        if (IsJSDocNullableType(typeNode) && typeNode.Pos() == typeNode.Type()!.Pos())
        {
            var node = _factory.NewOptionalTypeNode(typeNode.Type());
            node.Flags = typeNode.Flags;
            node.Loc = typeNode.Loc;
            typeNode.Type()!.Parent = node;
            return node;
        }
        return typeNode;
    }

    // ported from: internal/parser/parser.go:3714-3721
    private Node ParseParenthesizedType()
    {
        var pos = NodePos();
        ParseExpected(Kind.OpenParenToken);
        var typeNode = ParseType();
        ParseExpected(Kind.CloseParenToken);
        return FinishNode(_factory.NewParenthesizedTypeNode(typeNode), pos);
    }

    // ported from: internal/parser/parser.go:3722-3737
    private Node ParseAssertsTypePredicate()
    {
        var pos = NodePos();
        var assertsModifier = ParseExpectedToken(Kind.AssertsKeyword);
        Node? parameterName;
        if (_token == Kind.ThisKeyword)
        {
            parameterName = ParseThisTypeNode();
        }
        else
        {
            parameterName = ParseIdentifier();
        }
        Node? typeNode = null;
        if (ParseOptional(Kind.IsKeyword))
        {
            typeNode = ParseType();
        }
        return FinishNode(_factory.NewTypePredicateNode(assertsModifier, parameterName, typeNode), pos);
    }

    // ported from: internal/parser/parser.go:3738-3742
    private Node ParseTemplateType()
    {
        var pos = NodePos();
        return FinishNode(_factory.NewTemplateLiteralTypeNode(ParseTemplateHead(false /*isTaggedTemplate*/), ParseTemplateTypeSpans()), pos);
    }

    // ported from: internal/parser/parser.go:3743-3752
    private Node ParseTemplateHead(bool isTaggedTemplate)
    {
        if (!isTaggedTemplate && (_scanner.TokenFlags & TokenFlags.IsInvalid) != 0)
        {
            ReScanTemplateToken(false /*isTaggedTemplate*/);
        }
        var pos = NodePos();
        var result = _factory.NewTemplateHead(_scanner.TokenValue(), GetTemplateLiteralRawText(2 /*endLength*/), _scanner.TokenFlags);
        NextToken();
        return FinishNode(result, pos);
    }

    // ported from: internal/parser/parser.go:3753-3760
    private string GetTemplateLiteralRawText(int endLength)
    {
        var tokenText = _scanner.TokenText();
        if ((_scanner.TokenFlags & TokenFlags.Unterminated) != 0)
        {
            endLength = 0;
        }
        return tokenText[1..^endLength];
    }

    // ported from: internal/parser/parser.go:3761-3773
    private NodeList ParseTemplateTypeSpans()
    {
        var pos = NodePos();
        var list = new List<Node>();
        while (true)
        {
            var span = ParseTemplateTypeSpan();
            list.Add(span);
            if (span.AsTemplateLiteralTypeSpan()!.Literal!.Kind != Kind.TemplateMiddle)
            {
                break;
            }
        }
        return NewNodeList(NewTextRange(pos, NodePos()), list.ToArray());
    }

    // ported from: internal/parser/parser.go:3774-3778
    private Node ParseTemplateTypeSpan()
    {
        var pos = NodePos();
        return FinishNode(_factory.NewTemplateLiteralTypeSpan(ParseType(), ParseLiteralOfTemplateSpan(false /*isTaggedTemplate*/)), pos);
    }

    // ported from: internal/parser/parser.go:3779-3787
    private Node ParseLiteralOfTemplateSpan(bool isTaggedTemplate)
    {
        if (_token == Kind.CloseBraceToken)
        {
            ReScanTemplateToken(isTaggedTemplate);
            return ParseTemplateMiddleOrTail();
        }
        ParseErrorAtCurrentToken(X_0_expected, TokenToString(Kind.CloseBraceToken));
        return FinishNode(_factory.NewTemplateTail("", "", TokenFlags.None), NodePos());
    }

    // ported from: internal/parser/parser.go:3788-3799
    private Node ParseTemplateMiddleOrTail()
    {
        var pos = NodePos();
        Node result;
        if (_token == Kind.TemplateMiddle)
        {
            result = _factory.NewTemplateMiddle(_scanner.TokenValue(), GetTemplateLiteralRawText(2 /*endLength*/), _scanner.TokenFlags);
        }
        else
        {
            result = _factory.NewTemplateTail(_scanner.TokenValue(), GetTemplateLiteralRawText(1 /*endLength*/), _scanner.TokenFlags);
        }
        NextToken();
        return FinishNode(result, pos);
    }
    // ported from: internal/parser/parser.go:3800-3821
    private Node? ParseFunctionOrConstructorTypeToError(bool isInUnionType, Func<Parser, Node?> parseConstituentType)
    {
        // the function type and constructor type shorthand notation
        // are not allowed directly in unions and intersections, but we'll
        // try to parse them gracefully and issue a helpful message.
        if (IsStartOfFunctionTypeOrConstructorType())
        {
            var typeNode = ParseFunctionOrConstructorType();
            Message diagnostic;
            if (typeNode.Kind == Kind.FunctionType)
            {
                diagnostic = IfElse(isInUnionType,
                    Function_type_notation_must_be_parenthesized_when_used_in_a_union_type,
                    Function_type_notation_must_be_parenthesized_when_used_in_an_intersection_type);
            }
            else
            {
                diagnostic = IfElse(isInUnionType,
                    Constructor_type_notation_must_be_parenthesized_when_used_in_a_union_type,
                    Constructor_type_notation_must_be_parenthesized_when_used_in_an_intersection_type);
            }
            ParseErrorAtRange(typeNode.Loc, diagnostic);
            return typeNode;
        }
        return parseConstituentType(this);
    }

    // ported from: internal/parser/parser.go:3822-3828
    private bool IsStartOfFunctionTypeOrConstructorType()
    {
        return _token == Kind.LessThanToken ||
            _token == Kind.OpenParenToken && LookAhead(static p => p.NextIsUnambiguouslyStartOfFunctionType()) ||
            _token == Kind.NewKeyword ||
            _token == Kind.AbstractKeyword && LookAhead(static p => p.NextTokenIsNewKeyword());
    }

    // ported from: internal/parser/parser.go:3829-3848
    private Node ParseFunctionOrConstructorType()
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var modifiers = ParseModifiersForConstructorType();
        var isConstructorType = ParseOptional(Kind.NewKeyword);
        Debug.Assert(modifiers == null || isConstructorType, "Per isStartOfFunctionOrConstructorType, a function type cannot have modifiers.");
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters(ParseFlags.Type);
        var returnType = ParseReturnType(Kind.EqualsGreaterThanToken, false /*isType*/);
        Node result;
        if (isConstructorType)
        {
            result = _factory.NewConstructorTypeNode(modifiers, typeParameters, parameters, returnType);
        }
        else
        {
            result = _factory.NewFunctionTypeNode(typeParameters, parameters, returnType);
        }
        FinishNode(result, pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    // ported from: internal/parser/parser.go:3849-3859
    private ModifierList? ParseModifiersForConstructorType()
    {
        if (_token == Kind.AbstractKeyword)
        {
            var pos = NodePos();
            var modifier = _factory.NewModifier(_token);
            NextToken();
            FinishNode(modifier, pos);
            return NewModifierList(modifier.Loc, _nodeSliceArena.NewSlice1(modifier));
        }
        return null;
    }

    // ported from: internal/parser/parser.go:3860-3863
    private bool NextTokenIsNewKeyword()
    {
        return NextToken() == Kind.NewKeyword;
    }

    // ported from: internal/parser/parser.go:3864-3888
    private bool NextIsUnambiguouslyStartOfFunctionType()
    {
        NextToken();
        if (_token == Kind.CloseParenToken || _token == Kind.DotDotDotToken)
        {
            // ( )
            // ( ...
            return true;
        }
        if (SkipParameterStart())
        {
            // We successfully skipped modifiers (if any) and an identifier or binding pattern,
            // now see if we have something that indicates a parameter declaration
            if (_token == Kind.ColonToken || _token == Kind.CommaToken || _token == Kind.QuestionToken || _token == Kind.EqualsToken)
            {
                // ( xxx :
                // ( xxx ,
                // ( xxx ?
                // ( xxx =
                return true;
            }
            if (_token == Kind.CloseParenToken && NextToken() == Kind.EqualsGreaterThanToken)
            {
                // ( xxx ) =>
                return true;
            }
        }
        return false;
    }

    // ported from: internal/parser/parser.go:3889-3907
    private bool SkipParameterStart()
    {
        if (IsModifierKind(_token))
        {
            // Skip modifiers
            ParseModifiers();
        }
        ParseOptional(Kind.DotDotDotToken);
        if (IsIdentifier() || _token == Kind.ThisKeyword)
        {
            NextToken();
            return true;
        }
        if (_token == Kind.OpenBracketToken || _token == Kind.OpenBraceToken)
        {
            // Return true if we can parse an array or object binding pattern with no errors
            var previousErrorCount = _diagnostics.Count;
            ParseIdentifierOrPattern();
            return previousErrorCount == _diagnostics.Count;
        }
        return false;
    }

    // ported from: internal/parser/parser.go:3908-3911
    private ModifierList? ParseModifiers()
    {
        return ParseModifiersEx(false, false, false);
    }

    // ported from: internal/parser/parser.go:3912-3951
    private ModifierList? ParseModifiersEx(bool allowDecorators, bool permitConstAsModifier, bool stopOnStartOfClassStaticBlock)
    {
        var hasLeadingModifier = false;
        var hasTrailingDecorator = false;
        var hasTrailingModifier = false;
        var hasStaticModifier = false;
        // Decorators should be contiguous in a list of modifiers but can potentially appear in two places (i.e., `[...leadingDecorators, ...leadingModifiers, ...trailingDecorators, ...trailingModifiers]`).
        // The leading modifiers *should* only contain `export` and `default` when trailingDecorators are present, but we'll handle errors for any other leading modifiers in the checker.
        // It is illegal to have both leadingDecorators and trailingDecorators, but we will report that as a grammar check in the checker.
        // parse leading decorators
        var pos = NodePos();
        var list = new List<Node>(16);
        while (true)
        {
            if (allowDecorators && _token == Kind.AtToken && !hasTrailingModifier)
            {
                var decorator = ParseDecorator();
                list.Add(decorator);
                if (hasLeadingModifier)
                {
                    hasTrailingDecorator = true;
                }
            }
            else
            {
                var modifier = TryParseModifier(hasStaticModifier, permitConstAsModifier, stopOnStartOfClassStaticBlock);
                if (modifier == null)
                {
                    break;
                }
                if (modifier.Kind == Kind.StaticKeyword)
                {
                    hasStaticModifier = true;
                }
                list.Add(modifier);
                if (hasTrailingDecorator)
                {
                    hasTrailingModifier = true;
                }
                else
                {
                    hasLeadingModifier = true;
                }
            }
        }
        if (list.Count != 0)
        {
            return NewModifierList(NewTextRange(pos, NodePos()), _nodeSliceArena.Clone(CollectionsMarshal.AsSpan(list)));
        }
        return null;
    }

    // ported from: internal/parser/parser.go:3952-3958
    private Node ParseDecorator()
    {
        var pos = NodePos();
        ParseExpected(Kind.AtToken);
        var expression = DoInContext(NodeFlags.DecoratorContext, true, static p => p.ParseDecoratorExpression());
        return FinishNode(_factory.NewDecorator(expression), pos);
    }

    // ported from: internal/parser/parser.go:3959-3971
    private Node ParseDecoratorExpression()
    {
        if (InAwaitContext() && _token == Kind.AwaitKeyword)
        {
            // `@await` is disallowed in an [Await] context, but can cause parsing to go off the rails
            // This simply parses the missing identifier and moves on.
            var pos = NodePos();
            var awaitExpression = ParseIdentifierWithDiagnostic(Expression_expected, null);
            NextToken();
            var memberExpression = ParseMemberExpressionRest(pos, awaitExpression /*allowOptionalChain*/, true);
            return ParseCallExpressionRest(pos, memberExpression);
        }
        return ParseLeftHandSideExpressionOrHigher()!;
    }

    // ported from: internal/parser/parser.go:3972-3994
    private Node? TryParseModifier(bool hasSeenStaticModifier, bool permitConstAsModifier, bool stopOnStartOfClassStaticBlock)
    {
        var pos = NodePos();
        var kind = _token;
        if (_token == Kind.ConstKeyword && permitConstAsModifier)
        {
            // We need to ensure that any subsequent modifiers appear on the same line
            // so that when 'const' is a standalone declaration, we don't issue an error.
            if (!LookAhead(static p => p.NextTokenIsOnSameLineAndCanFollowModifier()))
            {
                return null;
            }
            else
            {
                NextToken();
            }
        }
        else if (stopOnStartOfClassStaticBlock && _token == Kind.StaticKeyword && LookAhead(static p => p.NextTokenIsOpenBrace()))
        {
            return null;
        }
        else if (hasSeenStaticModifier && _token == Kind.StaticKeyword)
        {
            return null;
        }
        else
        {
            if (!ParseAnyContextualModifier())
            {
                return null;
            }
        }
        return FinishNode(_factory.NewModifier(kind), pos);
    }

    // ported from: internal/parser/parser.go:3995-4003
    private bool ParseContextualModifier(Kind t)
    {
        var state = Mark();
        if (_token == t && NextTokenCanFollowModifier())
        {
            return true;
        }
        Rewind(state);
        return false;
    }

    // ported from: internal/parser/parser.go:4004-4012
    private bool ParseAnyContextualModifier()
    {
        var state = Mark();
        if (IsModifierKind(_token) && NextTokenCanFollowModifier())
        {
            return true;
        }
        Rewind(state);
        return false;
    }

    // ported from: internal/parser/parser.go:4013-4039
    private bool NextTokenCanFollowModifier()
    {
        switch (_token)
        {
            case Kind.ConstKeyword:
                // 'const' is only a modifier if followed by 'enum'.
                return NextToken() == Kind.EnumKeyword;
            case Kind.ExportKeyword:
                NextToken();
                if (_token == Kind.DefaultKeyword)
                {
                    return LookAhead(static p => p.NextTokenCanFollowDefaultKeyword());
                }
                if (_token == Kind.TypeKeyword)
                {
                    return LookAhead(static p => p.NextTokenCanFollowExportModifier());
                }
                return CanFollowExportModifier();
            case Kind.DefaultKeyword:
                return NextTokenCanFollowDefaultKeyword();
            case Kind.StaticKeyword:
                NextToken();
                return CanFollowModifier();
            case Kind.GetKeyword:
            case Kind.SetKeyword:
                NextToken();
                return CanFollowGetOrSetKeyword();
            default:
                return NextTokenIsOnSameLineAndCanFollowModifier();
        }
    }

    // ported from: internal/parser/parser.go:4040-4051
    private bool NextTokenCanFollowDefaultKeyword()
    {
        switch (NextToken())
        {
            case Kind.ClassKeyword:
            case Kind.FunctionKeyword:
            case Kind.InterfaceKeyword:
            case Kind.AtToken:
                return true;
            case Kind.AbstractKeyword:
                return LookAhead(static p => p.NextTokenIsClassKeywordOnSameLine());
            case Kind.AsyncKeyword:
                return LookAhead(static p => p.NextTokenIsFunctionKeywordOnSameLine());
        }
        return false;
    }

    // ported from: internal/parser/parser.go:4052-4055
    private bool NextTokenIsIdentifierOrKeyword()
    {
        return TokenIsIdentifierOrKeyword(NextToken());
    }

    // ported from: internal/parser/parser.go:4056-4059
    private bool NextTokenIsIdentifierOrKeywordOrGreaterThan()
    {
        return TokenIsIdentifierOrKeywordOrGreaterThan(NextToken());
    }

    // ported from: internal/parser/parser.go:4060-4063
    private bool NextTokenIsIdentifierOrKeywordOnSameLine()
    {
        return NextTokenIsIdentifierOrKeyword() && !HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:4064-4067
    private bool NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine()
    {
        return (NextTokenIsIdentifierOrKeyword() || _token == Kind.NumericLiteral || _token == Kind.BigIntLiteral || _token == Kind.StringLiteral) && !HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:4068-4071
    private bool NextTokenIsClassKeywordOnSameLine()
    {
        return NextToken() == Kind.ClassKeyword && !HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:4072-4075
    private bool NextTokenIsFunctionKeywordOnSameLine()
    {
        return NextToken() == Kind.FunctionKeyword && !HasPrecedingLineBreak();
    }

    // ported from: internal/parser/parser.go:4076-4080
    private bool NextTokenCanFollowExportModifier()
    {
        NextToken();
        return CanFollowExportModifier();
    }

    // ported from: internal/parser/parser.go:4081-4084
    private bool CanFollowExportModifier()
    {
        return _token == Kind.AtToken || _token != Kind.AsteriskToken && _token != Kind.AsKeyword && _token != Kind.OpenBraceToken && CanFollowModifier();
    }

    // ported from: internal/parser/parser.go:4085-4088
    private bool CanFollowModifier()
    {
        return _token == Kind.OpenBracketToken || _token == Kind.OpenBraceToken || _token == Kind.AsteriskToken || _token == Kind.DotDotDotToken || IsLiteralPropertyName();
    }

    // ported from: internal/parser/parser.go:4089-4092
    private bool CanFollowGetOrSetKeyword()
    {
        return _token == Kind.OpenBracketToken || IsLiteralPropertyName();
    }

    // ported from: internal/parser/parser.go:4093-4100
    private bool NextTokenIsOnSameLineAndCanFollowModifier()
    {
        NextToken();
        if (HasPrecedingLineBreak())
        {
            return false;
        }
        return CanFollowModifier();
    }

    // ported from: internal/parser/parser.go:4101-4104
    private bool NextTokenIsOpenBrace()
    {
        return NextToken() == Kind.OpenBraceToken;
    }

    // ported from: internal/parser/parser.go:4105-4125
    private Node ParseExpression()
    {
        // Expression[in]:
        //      AssignmentExpression[in]
        //      Expression[in] , AssignmentExpression[in]

        // clear the decorator context when parsing Expression, as it should be unambiguous when parsing a decorator
        var saveContextFlags = _contextFlags;
        _contextFlags &= ~NodeFlags.DecoratorContext;
        var pos = NodePos();
        var expr = ParseAssignmentExpressionOrHigher();
        while (true)
        {
            var operatorToken = ParseOptionalToken(Kind.CommaToken);
            if (operatorToken == null)
            {
                break;
            }
            expr = MakeBinaryExpression(expr, operatorToken, ParseAssignmentExpressionOrHigher(), pos);
        }
        _contextFlags = saveContextFlags;
        return expr;
    }

    // ported from: internal/parser/parser.go:4126-4129
    private Node ParseExpressionAllowIn()
    {
        return DoInContext(NodeFlags.DisallowInContext, false, static p => p.ParseExpression());
    }

    // ported from: internal/parser/parser.go:4130-4133
    private Node ParseAssignmentExpressionOrHigher()
    {
        return ParseAssignmentExpressionOrHigherWorker(true /*allowReturnTypeInArrowFunction*/);
    }

    // ported from: internal/parser/parser.go:4134-4202
    private Node ParseAssignmentExpressionOrHigherWorker(bool allowReturnTypeInArrowFunction)
    {
        //  AssignmentExpression[in,yield]:
        //      1) ConditionalExpression[?in,?yield]
        //      2) LeftHandSideExpression = AssignmentExpression[?in,?yield]
        //      3) LeftHandSideExpression AssignmentOperator AssignmentExpression[?in,?yield]
        //      4) ArrowFunctionExpression[?in,?yield]
        //      5) AsyncArrowFunctionExpression[in,yield,await]
        //      6) [+Yield] YieldExpression[?In]
        //
        // Note: for ease of implementation we treat productions '2' and '3' as the same thing.
        // (i.e. they're both BinaryExpressions with an assignment operator in it).
        // First, do the simple check if we have a YieldExpression (production '6').
        if (IsYieldExpression())
        {
            return ParseYieldExpression();
        }
        // Then, check if we have an arrow function (production '4' and '5') that starts with a parenthesized
        // parameter list or is an async arrow function.
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        // Production (1) of AsyncArrowFunctionExpression is parsed in "tryParseAsyncSimpleArrowFunctionExpression".
        // And production (2) is parsed in "tryParseParenthesizedArrowFunctionExpression".
        //
        // If we do successfully parse arrow-function, we must *not* recurse for productions 1, 2 or 3. An ArrowFunction is
        // not a LeftHandSideExpression, nor does it start a ConditionalExpression.  So we are done
        // with AssignmentExpression if we see one.
        var arrowExpression = TryParseParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction);
        if (arrowExpression != null)
        {
            return arrowExpression;
        }
        arrowExpression = TryParseAsyncSimpleArrowFunctionExpression(allowReturnTypeInArrowFunction);
        if (arrowExpression != null)
        {
            return arrowExpression;
        }
        // Now try to see if we're in production '1', '2' or '3'.  A conditional expression can
        // start with a LogicalOrExpression, while the assignment productions can only start with
        // LeftHandSideExpressions.
        //
        // So, first, we try to just parse out a BinaryExpression.  If we get something that is a
        // LeftHandSide or higher, then we can try to parse out the assignment expression part.
        // Otherwise, we try to parse out the conditional expression bit.  We want to allow any
        // binary expression here, so we pass in the 'lowest' precedence here so that it matches
        // and consumes anything.
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var expr = ParseBinaryExpressionOrHigher(OperatorPrecedenceLowest);
        // To avoid a look-ahead, we did not handle the case of an arrow function with a single un-parenthesized
        // parameter ('x => ...') above. We handle it here by checking if the parsed expression was a single
        // identifier and the current token is an arrow.
        if (expr.Kind == Kind.Identifier && _token == Kind.EqualsGreaterThanToken)
        {
            return ParseSimpleArrowFunctionExpression(pos, expr, allowReturnTypeInArrowFunction, jsdoc, null /*asyncModifier*/);
        }
        // Now see if we might be in cases '2' or '3'.
        // If the expression was a LHS expression, and we have an assignment operator, then
        // we're in '2' or '3'. Consume the assignment and return.
        //
        // Note: we call reScanGreaterToken so that we get an appropriately merged token
        // for cases like `> > =` becoming `>>=`
        if (IsLeftHandSideExpression(expr) && IsAssignmentOperator(ReScanGreaterThanToken()))
        {
            return MakeBinaryExpression(expr, ParseTokenNode(), ParseAssignmentExpressionOrHigherWorker(allowReturnTypeInArrowFunction), pos);
        }
        // It wasn't an assignment or a lambda.  This is a conditional expression:
        return ParseConditionalExpressionRest(expr, pos, allowReturnTypeInArrowFunction);
    }
    // ported from: internal/parser/parser.go:4203-4229
    private bool IsYieldExpression()
    {
        if (_token == Kind.YieldKeyword)
        {
            // If we have a 'yield' keyword, and this is a context where yield expressions are
            // allowed, then definitely parse out a yield expression.
            if (InYieldContext())
            {
                return true;
            }

            // We're in a context where 'yield expr' is not allowed.  However, if we can
            // definitely tell that the user was trying to parse a 'yield expr' and not
            // just a normal expr that start with a 'yield' identifier, then parse out
            // a 'yield expr'.  We can then report an error later that they are only
            // allowed in generator expressions.
            //
            // for example, if we see 'yield(foo)', then we'll have to treat that as an
            // invocation expression of something called 'yield'.  However, if we have
            // 'yield foo' then that is not legal as a normal expression, so we can
            // definitely recognize this as a yield expression.
            //
            // for now we just check if the next token is an identifier.  More heuristics
            // can be added here later as necessary.  We just need to make sure that we
            // don't accidentally consume something legal.
            return LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrLiteralOnSameLine());
        }
        return false;
    }

    // ported from: internal/parser/parser.go:4230-4247
    private Node ParseYieldExpression()
    {
        var pos = NodePos();
        // YieldExpression[In] :
        //      yield
        //      yield [no LineTerminator here] [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        //      yield [no LineTerminator here] * [Lexical goal InputElementRegExp]AssignmentExpression[?In, Yield]
        NextToken();
        Node result;
        if (!HasPrecedingLineBreak() && (_token == Kind.AsteriskToken || IsStartOfExpression()))
        {
            result = _factory.NewYieldExpression(ParseOptionalToken(Kind.AsteriskToken), ParseAssignmentExpressionOrHigher());
        }
        else
        {
            // if the next token is not on the same line as yield.  or we don't have an '*' or
            // the start of an expression, then this is just a simple "yield" expression.
            result = _factory.NewYieldExpression(null /*asteriskToken*/, null /*expression*/);
        }
        return FinishNode(result, pos);
    }

    // ported from: internal/parser/parser.go:4248-4264
    private Tristate IsParenthesizedArrowFunctionExpression()
    {
        if (_token == Kind.OpenParenToken || _token == Kind.LessThanToken || _token == Kind.AsyncKeyword)
        {
            var state = Mark();
            var result = NextIsParenthesizedArrowFunctionExpression();
            Rewind(state);
            return result;
        }
        if (_token == Kind.EqualsGreaterThanToken)
        {
            // ERROR RECOVERY TWEAK:
            // If we see a standalone => try to parse it as an arrow function expression as that's
            // likely what the user intended to write.
            return Tristate.TSTrue;
        }
        // Definitely not a parenthesized arrow function.
        return Tristate.TSFalse;
    }

    // ported from: internal/parser/parser.go:4265-4372
    private Tristate NextIsParenthesizedArrowFunctionExpression()
    {
        if (_token == Kind.AsyncKeyword)
        {
            NextToken();
            if (HasPrecedingLineBreak())
            {
                return Tristate.TSFalse;
            }
            if (_token != Kind.OpenParenToken && _token != Kind.LessThanToken)
            {
                return Tristate.TSFalse;
            }
        }
        var first = _token;
        var second = NextToken();
        if (first == Kind.OpenParenToken)
        {
            if (second == Kind.CloseParenToken)
            {
                // Simple cases: "() =>", "(): ", and "() {".
                // This is an arrow function with no parameters.
                // The last one is not actually an arrow function,
                // but this is probably what the user intended.
                var third = NextToken();
                switch (third)
                {
                    case Kind.EqualsGreaterThanToken:
                    case Kind.ColonToken:
                    case Kind.OpenBraceToken:
                        return Tristate.TSTrue;
                }
                return Tristate.TSFalse;
            }
            // If encounter "([" or "({", this could be the start of a binding pattern.
            // Examples:
            //      ([ x ]) => { }
            //      ({ x }) => { }
            //      ([ x ])
            //      ({ x })
            if (second == Kind.OpenBracketToken || second == Kind.OpenBraceToken)
            {
                return Tristate.TSUnknown;
            }
            // Simple case: "(..."
            // This is an arrow function with a rest parameter.
            if (second == Kind.DotDotDotToken)
            {
                return Tristate.TSTrue;
            }
            // Check for "(xxx yyy", where xxx is a modifier and yyy is an identifier. This
            // isn't actually allowed, but we want to treat it as a lambda so we can provide
            // a good error message.
            if (IsModifierKind(second) && second != Kind.AsyncKeyword && LookAhead(static p => p.NextTokenIsIdentifier()))
            {
                if (NextToken() == Kind.AsKeyword)
                {
                    // https://github.com/microsoft/TypeScript/issues/44466
                    return Tristate.TSFalse;
                }
                return Tristate.TSTrue;
            }
            // If we had "(" followed by something that's not an identifier,
            // then this definitely doesn't look like a lambda.  "this" is not
            // valid, but we want to parse it and then give a semantic error.
            if (!IsIdentifier() && second != Kind.ThisKeyword)
            {
                return Tristate.TSFalse;
            }
            switch (NextToken())
            {
                case Kind.ColonToken:
                    // If we have something like "(a:", then we must have a
                    // type-annotated parameter in an arrow function expression.
                    return Tristate.TSTrue;
                case Kind.QuestionToken:
                    NextToken();
                    // If we have "(a?:" or "(a?," or "(a?=" or "(a?)" then it is definitely a lambda.
                    if (_token == Kind.ColonToken || _token == Kind.CommaToken || _token == Kind.EqualsToken || _token == Kind.CloseParenToken)
                    {
                        return Tristate.TSTrue;
                    }
                    // Otherwise it is definitely not a lambda.
                    return Tristate.TSFalse;
                case Kind.CommaToken:
                case Kind.EqualsToken:
                case Kind.CloseParenToken:
                    // If we have "(a," or "(a=" or "(a)" this *could* be an arrow function
                    return Tristate.TSUnknown;
            }
            // It is definitely not an arrow function
            return Tristate.TSFalse;
        }
        else
        {
            Debug.Assert(first == Kind.LessThanToken);
            // If we have "<" not followed by an identifier,
            // then this definitely is not an arrow function.
            if (!IsIdentifier() && _token != Kind.ConstKeyword)
            {
                return Tristate.TSFalse;
            }
            // JSX overrides
            if (_languageVariant == LanguageVariant.JSX)
            {
                var isArrowFunctionInJsx = LookAhead(static p =>
                {
                    p.ParseOptional(Kind.ConstKeyword);
                    var third = p.NextToken();
                    if (third == Kind.ExtendsKeyword)
                    {
                        var fourth = p.NextToken();
                        switch (fourth)
                        {
                            case Kind.EqualsToken:
                            case Kind.GreaterThanToken:
                            case Kind.SlashToken:
                                return false;
                        }
                        return true;
                    }
                    else if (third == Kind.CommaToken || third == Kind.EqualsToken)
                    {
                        return true;
                    }
                    return false;
                });
                if (isArrowFunctionInJsx)
                {
                    return Tristate.TSTrue;
                }
                return Tristate.TSFalse;
            }
            // This *could* be a parenthesized arrow function.
            return Tristate.TSUnknown;
        }
    }

    // ported from: internal/parser/parser.go:4373-4393
    private Node? TryParseParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        var tristate = IsParenthesizedArrowFunctionExpression();
        if (tristate == Tristate.TSFalse)
        {
            // It's definitely not a parenthesized arrow function expression.
            return null;
        }
        // If we definitely have an arrow function, then we can just parse one, not requiring a
        // following => or { token. Otherwise, we *might* have an arrow function.  Try to parse
        // it out, but don't allow any ambiguity, and return 'undefined' if this could be an
        // expression instead.
        if (tristate == Tristate.TSTrue)
        {
            return ParseParenthesizedArrowFunctionExpression(true /*allowAmbiguity*/, true /*allowReturnTypeInArrowFunction*/);
        }
        var state = Mark();
        var result = ParsePossibleParenthesizedArrowFunctionExpression(allowReturnTypeInArrowFunction);
        if (result == null)
        {
            Rewind(state);
        }
        return result;
    }

    // ported from: internal/parser/parser.go:4394-4491
    private Node? ParseParenthesizedArrowFunctionExpression(bool allowAmbiguity, bool allowReturnTypeInArrowFunction)
    {
        var pos = NodePos();
        var jsdoc = JsdocScannerInfo;
        var modifiers = ParseModifiersForArrowFunction();
        var isAsync = ModifierListHasAsync(modifiers);
        var signatureFlags = IfElse(isAsync, ParseFlags.Await, ParseFlags.None);
        // Arrow functions are never generators.
        //
        // If we're speculatively parsing a signature for a parenthesized arrow function, then
        // we have to have a complete parameter list.  Otherwise we might see something like
        // a => (b => c)
        // And think that "(b =>" was actually a parenthesized arrow function with a missing
        // close paren.
        var typeParameters = ParseTypeParameters();
        NodeList? parameters;
        if (!ParseExpected(Kind.OpenParenToken))
        {
            if (!allowAmbiguity)
            {
                return null;
            }
            parameters = CreateMissingList();
        }
        else
        {
            if (!allowAmbiguity)
            {
                var maybeParameters = ParseParametersWorker(signatureFlags, allowAmbiguity);
                if (maybeParameters == null)
                {
                    return null;
                }
                parameters = maybeParameters;
            }
            else
            {
                parameters = ParseParametersWorker(signatureFlags, allowAmbiguity);
            }
            if (!ParseExpected(Kind.CloseParenToken) && !allowAmbiguity)
            {
                return null;
            }
        }
        var hasReturnColon = _token == Kind.ColonToken;
        var returnType = ParseReturnType(Kind.ColonToken /*isType*/, false);
        if (returnType != null && !allowAmbiguity && TypeHasArrowFunctionBlockingParseError(returnType))
        {
            return null;
        }
        // Parsing a signature isn't enough.
        // Parenthesized arrow signatures often look like other valid expressions.
        // For instance:
        //  - "(x = 10)" is an assignment expression parsed as a signature with a default parameter value.
        //  - "(x,y)" is a comma expression parsed as a signature with two parameters.
        //  - "a ? (b): c" will have "(b):" parsed as a signature with a return type annotation.
        //  - "a ? (b): function() {}" will too, since function() is a valid JSDoc function type.
        //  - "a ? (b): (function() {})" as well, but inside of a parenthesized type with an arbitrary amount of nesting.
        //
        // So we need just a bit of lookahead to ensure that it can only be a signature.
        var unwrappedType = returnType;
        while (unwrappedType != null && unwrappedType.Kind == Kind.ParenthesizedType)
        {
            unwrappedType = unwrappedType.Type(); // Skip parens if need be
        }
        if (!allowAmbiguity && _token != Kind.EqualsGreaterThanToken && _token != Kind.OpenBraceToken)
        {
            // Returning undefined here will cause our caller to rewind to where we started from.
            return null;
        }
        // If we have an arrow, then try to parse the body. Even if not, try to parse if we
        // have an opening brace, just in case we're in an error state.
        var lastToken = _token;
        var equalsGreaterThanToken = ParseExpectedToken(Kind.EqualsGreaterThanToken);
        Node body;
        if (lastToken == Kind.EqualsGreaterThanToken || lastToken == Kind.OpenBraceToken)
        {
            body = ParseArrowFunctionExpressionBody(isAsync, allowReturnTypeInArrowFunction);
        }
        else
        {
            body = ParseIdentifier();
        }
        // Given:
        //     x ? y => ({ y }) : z => ({ z })
        // We try to parse the body of the first arrow function by looking at:
        //     ({ y }) : z => ({ z })
        // This is a valid arrow function with "z" as the return type.
        //
        // But, if we're in the true side of a conditional expression, this colon
        // terminates the expression, so we cannot allow a return type if we aren't
        // certain whether or not the preceding text was parsed as a parameter list.
        //
        // For example,
        //     a() ? (b: number, c?: string): void => d() : e
        // is determined by isParenthesizedArrowFunctionExpression to unambiguously
        // be an arrow expression, so we allow a return type.
        if (!allowReturnTypeInArrowFunction && hasReturnColon)
        {
            // However, if the arrow function we were able to parse is followed by another colon
            // as in:
            //     a ? (x): string => x : null
            // Then allow the arrow function, and treat the second colon as terminating
            // the conditional expression. It's okay to do this because this code would
            // be a syntax error in JavaScript (as the second colon shouldn't be there).
            if (_token != Kind.ColonToken)
            {
                return null;
            }
        }
        var result = FinishNode(_factory.NewArrowFunction(modifiers, typeParameters, parameters, returnType, null /*fullSignature*/, equalsGreaterThanToken, body), pos);
        WithJSDoc(result, jsdoc);
        CheckJSSyntax(result);
        return result;
    }

    // ported from: internal/parser/parser.go:4492-4502
    private ModifierList? ParseModifiersForArrowFunction()
    {
        if (_token == Kind.AsyncKeyword)
        {
            var pos = NodePos();
            NextToken();
            var modifier = FinishNode(_factory.NewModifier(Kind.AsyncKeyword), pos);
            return NewModifierList(modifier.Loc, _nodeSliceArena.NewSlice1(modifier));
        }
        return null;
    }

    // ported from: internal/parser/parser.go:4503-4514（Go 的包级函数 → Parser 上的 private static）
    // If true, we should abort parsing an error function.
    private static bool TypeHasArrowFunctionBlockingParseError(Node node)
    {
        switch (node.Kind)
        {
            case Kind.TypeReference:
                return NodeIsMissing(node.AsTypeReferenceNode()!.TypeName);
            case Kind.FunctionType:
            case Kind.ConstructorType:
                return IsMissingNodeList(node.FunctionLikeData()!.Parameters) || TypeHasArrowFunctionBlockingParseError(node.Type()!);
            case Kind.ParenthesizedType:
                return TypeHasArrowFunctionBlockingParseError(node.Type()!);
        }
        return false;
    }

    // ported from: internal/parser/parser.go:4515-4543
    private Node ParseArrowFunctionExpressionBody(bool isAsync, bool allowReturnTypeInArrowFunction)
    {
        if (_token == Kind.OpenBraceToken)
        {
            return ParseFunctionBlock(IfElse(isAsync, ParseFlags.Await, ParseFlags.None), null /*diagnosticMessage*/);
        }
        if (_token != Kind.SemicolonToken && _token != Kind.FunctionKeyword && _token != Kind.ClassKeyword && IsStartOfStatement() && !IsStartOfExpressionStatement())
        {
            // Check if we got a plain statement (i.e. no expression-statements, no function/class expressions/declarations)
            //
            // Here we try to recover from a potential error situation in the case where the
            // user meant to supply a block. For example, if the user wrote:
            //
            //  a =>
            //      let v = 0;
            //  }
            //
            // they may be missing an open brace.  Check to see if that's the case so we can
            // try to recover better.  If we don't do this, then the next close curly we see may end
            // up preemptively closing the containing construct.
            //
            // Note: even when 'IgnoreMissingOpenBrace' is passed, parseBody will still error.
            return ParseFunctionBlock(ParseFlags.IgnoreMissingOpenBrace | IfElse(isAsync, ParseFlags.Await, ParseFlags.None), null /*diagnosticMessage*/);
        }
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.AwaitContext, isAsync);
        SetContextFlags(NodeFlags.YieldContext, false);
        var node = ParseAssignmentExpressionOrHigherWorker(allowReturnTypeInArrowFunction);
        _contextFlags = saveContextFlags;
        return node;
    }

    // ported from: internal/parser/parser.go:4544-4548
    private bool IsStartOfExpressionStatement()
    {
        // As per the grammar, none of '{' or 'function' or 'class' can start an expression statement.
        return _token != Kind.OpenBraceToken && _token != Kind.FunctionKeyword && _token != Kind.ClassKeyword && _token != Kind.AtToken && IsStartOfExpression();
    }

    // ported from: internal/parser/parser.go:4549-4560
    private Node? ParsePossibleParenthesizedArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        var tokenPos = _scanner.TokenStart();
        if (_notParenthesizedArrow.Has(tokenPos))
        {
            return null;
        }
        var result = ParseParenthesizedArrowFunctionExpression(false /*allowAmbiguity*/, allowReturnTypeInArrowFunction);
        if (result == null)
        {
            _notParenthesizedArrow.Add(tokenPos);
        }
        return result;
    }

    // ported from: internal/parser/parser.go:4561-4572
    private Node? TryParseAsyncSimpleArrowFunctionExpression(bool allowReturnTypeInArrowFunction)
    {
        // We do a check here so that we won't be doing unnecessarily call to "lookAhead"
        if (_token == Kind.AsyncKeyword && LookAhead(static p => p.NextIsUnParenthesizedAsyncArrowFunction()))
        {
            var pos = NodePos();
            var jsdoc = JsdocScannerInfo;
            var asyncModifier = ParseModifiersForArrowFunction();
            var expr = ParseBinaryExpressionOrHigher(OperatorPrecedenceLowest);
            return ParseSimpleArrowFunctionExpression(pos, expr, allowReturnTypeInArrowFunction, jsdoc, asyncModifier);
        }
        return null;
    }

    // ported from: internal/parser/parser.go:4573-4593
    private bool NextIsUnParenthesizedAsyncArrowFunction()
    {
        // AsyncArrowFunctionExpression:
        //      1) async[no LineTerminator here]AsyncArrowBindingIdentifier[?Yield][no LineTerminator here]=>AsyncConciseBody[?In]
        //      2) CoverCallExpressionAndAsyncArrowHead[?Yield, ?Await][no LineTerminator here]=>AsyncConciseBody[?In]
        if (_token == Kind.AsyncKeyword)
        {
            NextToken();
            // If the "async" is followed by "=>" token then it is not a beginning of an async arrow-function
            // but instead a simple arrow-function which will be parsed inside "parseAssignmentExpressionOrHigher"
            if (HasPrecedingLineBreak() || _token == Kind.EqualsGreaterThanToken)
            {
                return false;
            }
            // Check for un-parenthesized AsyncArrowFunction
            if (!IsIdentifier())
            {
                return false;
            }
            NextTokenWithoutCheck();
            return !HasPrecedingLineBreak() && _token == Kind.EqualsGreaterThanToken;
        }
        return false;
    }

    // ported from: internal/parser/parser.go:4594-4604
    private Node ParseSimpleArrowFunctionExpression(int pos, Node identifier, bool allowReturnTypeInArrowFunction, JsdocScannerInfo jsdoc, ModifierList? asyncModifier)
    {
        Debug.Assert(_token == Kind.EqualsGreaterThanToken, "parseSimpleArrowFunctionExpression should only have been called if we had a =>");
        var parameter = FinishNode(_factory.NewParameterDeclaration(null /*modifiers*/, null /*dotDotDotToken*/, identifier, null /*questionToken*/, null /*typeNode*/, null /*initializer*/), identifier.Pos());
        var parameters = NewNodeList(parameter.Loc, new[] { parameter });
        var equalsGreaterThanToken = ParseExpectedToken(Kind.EqualsGreaterThanToken);
        var body = ParseArrowFunctionExpressionBody(asyncModifier != null /*isAsync*/, allowReturnTypeInArrowFunction);
        var result = FinishNode(_factory.NewArrowFunction(asyncModifier, null /*typeParameters*/, parameters, null /*returnType*/, null /*fullSignature*/, equalsGreaterThanToken, body), pos);
        WithJSDoc(result, jsdoc);
        return result;
    }

    // ported from: internal/parser/parser.go:4605-4626
    private Node ParseConditionalExpressionRest(Node leftOperand, int pos, bool allowReturnTypeInArrowFunction)
    {
        // Note: we are passed in an expression which was produced from parseBinaryExpressionOrHigher.
        var questionToken = ParseOptionalToken(Kind.QuestionToken);
        if (questionToken == null)
        {
            return leftOperand;
        }
        // Note: we explicitly 'allowIn' in the whenTrue part of the condition expression, and
        // we do not that for the 'whenFalse' part.
        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.DisallowInContext, false);
        var trueExpression = ParseAssignmentExpressionOrHigherWorker(false /*allowReturnTypeInArrowFunction*/);
        _contextFlags = saveContextFlags;
        var colonToken = ParseExpectedToken(Kind.ColonToken);
        Node falseExpression;
        if (NodeIsPresent(colonToken))
        {
            falseExpression = ParseAssignmentExpressionOrHigherWorker(allowReturnTypeInArrowFunction);
        }
        else
        {
            falseExpression = CreateMissingIdentifier();
        }
        return FinishNode(_factory.NewConditionalExpression(leftOperand, questionToken, trueExpression, colonToken, falseExpression), pos);
    }

    // ported from: internal/parser/parser.go:4627-4634
    private Node ParseBinaryExpressionOrHigher(OperatorPrecedence precedence)
    {
        var pos = NodePos();
        var leftOperand = ParseUnaryExpressionOrHigher();
        return ParseBinaryExpressionRest(precedence, leftOperand, pos);
    }

    // ported from: internal/parser/parser.go:4635-4641（Go 的包级函数 → Parser 上的 private static）
    // shouldConsumeBinaryOperator reports whether an operator binds before the operator represented by currentPrecedence.
    // At equal precedence, only the right-associative exponentiation operator binds first.
    private static bool ShouldConsumeBinaryOperator(Kind @operator, OperatorPrecedence operatorPrecedence, OperatorPrecedence currentPrecedence)
    {
        if (operatorPrecedence > currentPrecedence)
        {
            return true;
        }
        return operatorPrecedence == currentPrecedence && @operator == Kind.AsteriskAsteriskToken;
    }
    // ported from: internal/parser/parser.go:4642-4712
    private Node ParseBinaryExpressionRest(OperatorPrecedence precedence, Node leftOperand, int pos)
    {
        var lastOperand = leftOperand;
        while (true)
        {
            // We either have a binary operator here, or we're finished.  We call
            // reScanGreaterToken so that we merge token sequences like > and = into >=
            var @operator = ReScanGreaterThanToken();
            var newPrecedence = GetBinaryOperatorPrecedence(@operator);
            // Check the precedence to see if we should "take" this operator
            // - For left associative operator (all operator but **), consume the operator,
            //   recursively call the function below, and parse binaryExpression as a rightOperand
            //   of the caller if the new precedence of the operator is greater then or equal to the current precedence.
            //   For example:
            //      a - b - c;
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a * b - c
            //            ^token; leftOperand = b. Return b to the caller as a rightOperand
            //      a - b * c;
            //            ^token; leftOperand = b. Return b * c to the caller as a rightOperand
            // - For right associative operator (**), consume the operator, recursively call the function
            //   and parse binaryExpression as a rightOperand of the caller if the new precedence of
            //   the operator is strictly grater than the current precedence
            //   For example:
            //      a ** b ** c;
            //             ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a - b ** c;
            //            ^^token; leftOperand = b. Return b ** c to the caller as a rightOperand
            //      a ** b - c
            //             ^token; leftOperand = b. Return b to the caller as a rightOperand
            if (!ShouldConsumeBinaryOperator(@operator, newPrecedence, precedence))
            {
                break;
            }
            if (@operator == Kind.InKeyword && InDisallowInContext())
            {
                break;
            }
            if (@operator == Kind.AsKeyword || @operator == Kind.SatisfiesKeyword)
            {
                // Make sure we *do* perform ASI for constructs like this:
                //    var x = foo
                //    as (Bar)
                // This should be parsed as an initialized variable, followed
                // by a function call to 'as' with the argument 'Bar'
                if (HasPrecedingLineBreak())
                {
                    break;
                }
                else
                {
                    NextToken();
                    // When we have 'a ## b as SomeType $$ c' or 'a ## b satisfies SomeType $$ c', where ## and $$
                    // are binary operators, we want to stop parsing when $$ would bind before ## after erasing the
                    // assertion. See https://github.com/microsoft/TypeScript/issues/63527.
                    var lastPrecedence = OperatorPrecedenceHighest;
                    if (IsBinaryExpression(lastOperand))
                    {
                        lastPrecedence = GetBinaryOperatorPrecedence(lastOperand.AsBinaryExpression()!.OperatorToken!.Kind);
                    }
                    if (@operator == Kind.SatisfiesKeyword)
                    {
                        leftOperand = MakeSatisfiesExpression(leftOperand, ParseType());
                    }
                    else
                    {
                        leftOperand = MakeAsExpression(leftOperand, ParseType());
                    }
                    // Stop if the next operator would bind before the last operator when the assertion is erased.
                    var nextOperator = ReScanGreaterThanToken();
                    var nextPrecedence = GetBinaryOperatorPrecedence(nextOperator);
                    if (ShouldConsumeBinaryOperator(nextOperator, nextPrecedence, lastPrecedence))
                    {
                        break;
                    }
                }
            }
            else
            {
                leftOperand = MakeBinaryExpression(leftOperand, ParseTokenNode(), ParseBinaryExpressionOrHigher(newPrecedence), pos);
                lastOperand = leftOperand;
            }
        }
        return leftOperand;
    }

    // ported from: internal/parser/parser.go:4713-4716
    private Node MakeSatisfiesExpression(Node expression, Node? typeNode)
    {
        return CheckJSSyntax(FinishNode(_factory.NewSatisfiesExpression(expression, typeNode), expression.Pos()));
    }

    // ported from: internal/parser/parser.go:4717-4720
    private Node MakeAsExpression(Node left, Node? right)
    {
        return CheckJSSyntax(FinishNode(_factory.NewAsExpression(left, right), left.Pos()));
    }

    // ported from: internal/parser/parser.go:4721-4724
    private Node MakeBinaryExpression(Node left, Node operatorToken, Node right, int pos)
    {
        return FinishNode(_factory.NewBinaryExpression(null /*modifiers*/, left, null /*typeNode*/, operatorToken, right), pos);
    }
    // ported from: internal/parser/parser.go:4725-4763
    private Node ParseUnaryExpressionOrHigher()
    {
        // ES7 UpdateExpression:
        //      1) LeftHandSideExpression[?Yield]
        //      2) LeftHandSideExpression[?Yield][no LineTerminator here]++
        //      3) LeftHandSideExpression[?Yield][no LineTerminator here]--
        //      4) ++UnaryExpression[?Yield]
        //      5) --UnaryExpression[?Yield]
        if (IsUpdateExpression())
        {
            var pos = NodePos();
            var updateExpression = ParseUpdateExpression();
            if (_token == Kind.AsteriskAsteriskToken)
            {
                return ParseBinaryExpressionRest(GetBinaryOperatorPrecedence(_token), updateExpression, pos);
            }
            return updateExpression;
        }
        // ES7 UnaryExpression:
        //      1) UpdateExpression[?yield]
        //      2) delete UpdateExpression[?yield]
        //      3) void UpdateExpression[?yield]
        //      4) typeof UpdateExpression[?yield]
        //      5) + UpdateExpression[?yield]
        //      6) - UpdateExpression[?yield]
        //      7) ~ UpdateExpression[?yield]
        //      8) ! UpdateExpression[?yield]
        var unaryOperator = _token;
        var simpleUnaryExpression = ParseSimpleUnaryExpression()!;
        if (_token == Kind.AsteriskAsteriskToken)
        {
            var pos = SkipTrivia(_sourceText, simpleUnaryExpression.Pos());
            var end = simpleUnaryExpression.End();
            if (simpleUnaryExpression.Kind == Kind.TypeAssertionExpression)
            {
                ParseErrorAt(pos, end, A_type_assertion_expression_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses);
            }
            else
            {
                Debug.Assert(IsKeywordOrPunctuation(unaryOperator));
                ParseErrorAt(pos, end, An_unary_expression_with_the_0_operator_is_not_allowed_in_the_left_hand_side_of_an_exponentiation_expression_Consider_enclosing_the_expression_in_parentheses, TokenToString(unaryOperator));
            }
        }
        return simpleUnaryExpression;
    }

    // ported from: internal/parser/parser.go:4764-4773
    private bool IsUpdateExpression()
    {
        switch (_token)
        {
            case Kind.PlusToken:
            case Kind.MinusToken:
            case Kind.TildeToken:
            case Kind.ExclamationToken:
            case Kind.DeleteKeyword:
            case Kind.TypeOfKeyword:
            case Kind.VoidKeyword:
            case Kind.AwaitKeyword:
                return false;
            case Kind.LessThanToken:
                return _languageVariant == LanguageVariant.JSX;
        }
        return true;
    }

    // ported from: internal/parser/parser.go:4774-4792
    private Node ParseUpdateExpression()
    {
        var pos = NodePos();
        if (_token == Kind.PlusPlusToken || _token == Kind.MinusMinusToken)
        {
            var @operator = _token;
            NextToken();
            return FinishNode(_factory.NewPrefixUnaryExpression(@operator, ParseLeftHandSideExpressionOrHigher()), pos);
        }
        else if (_languageVariant == LanguageVariant.JSX && _token == Kind.LessThanToken && LookAhead(static p => p.NextTokenIsIdentifierOrKeywordOrGreaterThan()))
        {
            // JSXElement is part of primaryExpression
            return ParseJsxElementOrSelfClosingElementOrFragment(true /*inExpressionContext*/, -1 /*topInvalidNodePosition*/, null /*openingTag*/, false /*mustBeUnary*/);
        }
        var expression = ParseLeftHandSideExpressionOrHigher()!;
        if ((_token == Kind.PlusPlusToken || _token == Kind.MinusMinusToken) && !HasPrecedingLineBreak())
        {
            var @operator = _token;
            NextToken();
            return FinishNode(_factory.NewPostfixUnaryExpression(expression, @operator), pos);
        }
        return expression;
    }

    // ported from: internal/parser/parser.go:4793-4872
    private Node ParseJsxElementOrSelfClosingElementOrFragment(bool inExpressionContext, int topInvalidNodePosition, Node? openingTag, bool mustBeUnary)
    {
        var pos = NodePos();
        var opening = ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(inExpressionContext);
        Node result;
        switch (opening.Kind)
        {
            case Kind.JsxOpeningElement:
                var children = ParseJsxChildren(opening);
                Node? closingElement;
                var lastChild = LastOrNil(children.Nodes);
                if (lastChild != null && lastChild.Kind == Kind.JsxElement &&
                    !TagNamesAreEquivalent(lastChild.AsJsxElement()!.OpeningElement!.TagName(), lastChild.AsJsxElement()!.ClosingElement!.TagName()) &&
                    TagNamesAreEquivalent(opening.TagName(), lastChild.AsJsxElement()!.ClosingElement!.TagName()))
                {
                    // when an unclosed JsxOpeningElement incorrectly parses its parent's JsxClosingElement,
                    // restructure (<div>(...<span>...</div>)) --> (<div>(...<span>...</>)</div>)
                    // (no need to error; the parent will error)
                    var lastChildElement = lastChild.AsJsxElement()!;
                    var end = lastChildElement.Children!.End();
                    var missingIdentifier = FinishNodeWithEnd(NewIdentifier(""), end, end);
                    var newClosingElement = FinishNodeWithEnd(_factory.NewJsxClosingElement(missingIdentifier), end, end);
                    var newLast = FinishNodeWithEnd(
                        _factory.NewJsxElement(lastChildElement.OpeningElement, lastChildElement.Children, newClosingElement),
                        lastChildElement.OpeningElement!.Pos(),
                        end);
                    // force reset parent pointers from discarded parse result
                    if (lastChildElement.OpeningElement != null)
                    {
                        lastChildElement.OpeningElement.Parent = newLast;
                    }
                    if (lastChildElement.Children != null)
                    {
                        foreach (var c in lastChildElement.Children.Nodes!)
                        {
                            c.Parent = newLast;
                        }
                    }
                    newClosingElement.Parent = newLast;
                    // Go: append(children.Nodes[0:len(children.Nodes)-1], newLast)（长度不变 → 同长度新数组）
                    var oldNodes = children.Nodes!;
                    var newNodes = new Node[oldNodes.Length];
                    Array.Copy(oldNodes, newNodes, oldNodes.Length - 1);
                    newNodes[^1] = newLast;
                    children = NewNodeList(NewTextRange(children.Pos(), newLast.End()), newNodes);
                    closingElement = lastChildElement.ClosingElement;
                }
                else
                {
                    closingElement = ParseJsxClosingElement(opening, inExpressionContext);
                    if (!TagNamesAreEquivalent(opening.TagName(), closingElement.TagName()))
                    {
                        if (openingTag != null && IsJsxOpeningElement(openingTag) && TagNamesAreEquivalent(closingElement.TagName(), openingTag.TagName()))
                        {
                            // opening incorrectly matched with its parent's closing -- put error on opening
                            ParseErrorAtRange(opening.TagName()!.Loc, JSX_element_0_has_no_corresponding_closing_tag, GetTextOfNodeFromSourceText(_sourceText, opening.TagName()!, false /*includeTrivia*/));
                        }
                        else
                        {
                            // other opening/closing mismatches -- put error on closing
                            ParseErrorAtRange(closingElement.TagName()!.Loc, Expected_corresponding_JSX_closing_tag_for_0, GetTextOfNodeFromSourceText(_sourceText, opening.TagName()!, false /*includeTrivia*/));
                        }
                    }
                }
                result = FinishNode(_factory.NewJsxElement(opening, children, closingElement), pos);
                closingElement!.Parent = result; // force reset parent pointers from possibly discarded parse result
                break;
            case Kind.JsxOpeningFragment:
                result = FinishNode(_factory.NewJsxFragment(opening, ParseJsxChildren(opening), ParseJsxClosingFragment(inExpressionContext)), pos);
                break;
            case Kind.JsxSelfClosingElement:
                // Nothing else to do for self-closing elements
                result = opening;
                break;
            default:
                throw new DebugFailureException("Unhandled case in parseJsxElementOrSelfClosingElementOrFragment");
        }
        // If the user writes the invalid code '<div></div><div></div>' in an expression context (i.e. not wrapped in
        // an enclosing tag), we'll naively try to parse   ^ this as a 'less than' operator and the remainder of the tag
        // as garbage, which will cause the formatter to badly mangle the JSX. Perform a speculative parse of a JSX
        // element if we see a < token so that we can wrap it in a synthetic binary expression so the formatter
        // does less damage and we can report a better error.
        // Since JSX elements are invalid < operands anyway, this lookahead parse will only occur in error scenarios
        // of one sort or another.
        // If we are in a unary context, we can't do this recovery; the binary expression we return here is not
        // a valid UnaryExpression and will cause problems later.
        if (!mustBeUnary && inExpressionContext && _token == Kind.LessThanToken)
        {
            var topBadPos = topInvalidNodePosition;
            if (topBadPos < 0)
            {
                topBadPos = result.Pos();
            }
            var invalidElement = ParseJsxElementOrSelfClosingElementOrFragment( /*inExpressionContext*/ true, topBadPos, null, false);
            var operatorToken = _factory.NewToken(Kind.CommaToken);
            operatorToken.Loc = NewTextRange(invalidElement.Pos(), invalidElement.Pos());
            ParseErrorAt(SkipTrivia(_sourceText, topBadPos), invalidElement.End(), JSX_expressions_must_have_one_parent_element);
            result = FinishNode(_factory.NewBinaryExpression(null /*modifiers*/, result, null /*typeNode*/, operatorToken, invalidElement), pos);
        }
        return result;
    }

    // ported from: internal/parser/parser.go:4873-4895
    private NodeList ParseJsxChildren(Node openingTag)
    {
        var pos = NodePos();
        var saveParsingContexts = _parsingContexts;
        _parsingContexts |= (ParsingContexts)(1 << (int)ParsingContext.JsxChildren);
        var list = new List<Node>();
        while (true)
        {
            var currentToken = _scanner.ReScanJsxToken(true /*allowMultilineJsxText*/);
            var child = ParseJsxChild(openingTag, currentToken);
            if (child == null)
            {
                break;
            }
            list.Add(child);
            if (IsJsxOpeningElement(openingTag) && child.Kind == Kind.JsxElement &&
                !TagNamesAreEquivalent(child.AsJsxElement()!.OpeningElement!.TagName(), child.AsJsxElement()!.ClosingElement!.TagName()) &&
                TagNamesAreEquivalent(openingTag.TagName(), child.AsJsxElement()!.ClosingElement!.TagName()))
            {
                // stop after parsing a mismatched child like <div>...(<span></div>) in order to reattach the </div> higher
                break;
            }
        }
        _parsingContexts = saveParsingContexts;
        return NewNodeList(NewTextRange(pos, NodePos()), list.ToArray());
    }

    // ported from: internal/parser/parser.go:4896-4923
    private Node? ParseJsxChild(Node openingTag, Kind token)
    {
        switch (token)
        {
            case Kind.EndOfFile:
                // If we hit EOF, issue the error at the tag that lacks the closing element
                // rather than at the end of the file (which is useless)
                if (IsJsxOpeningFragment(openingTag))
                {
                    ParseErrorAtRange(openingTag.Loc, JSX_fragment_has_no_corresponding_closing_tag);
                }
                else
                {
                    // We want the error span to cover only 'Foo.Bar' in < Foo.Bar >
                    // or to cover only 'Foo' in < Foo >
                    var tag = openingTag.TagName()!;
                    var start = Math.Min(SkipTrivia(_sourceText, tag.Pos()), tag.End());
                    ParseErrorAt(start, tag.End(), JSX_element_0_has_no_corresponding_closing_tag, GetTextOfNodeFromSourceText(_sourceText, openingTag.TagName()!, false /*includeTrivia*/));
                }
                return null;
            case Kind.LessThanSlashToken:
            case Kind.ConflictMarkerTrivia:
                return null;
            case Kind.JsxText:
            case Kind.JsxTextAllWhiteSpaces:
                return ParseJsxText();
            case Kind.OpenBraceToken:
                return ParseJsxExpression(false /*inExpressionContext*/);
            case Kind.LessThanToken:
                return ParseJsxElementOrSelfClosingElementOrFragment(false /*inExpressionContext*/, -1 /*topInvalidNodePosition*/, openingTag, false);
        }
        throw new DebugFailureException("Unhandled case in parseJsxChild");
    }

    // ported from: internal/parser/parser.go:4924-4930
    private Node ParseJsxText()
    {
        var pos = NodePos();
        var result = _factory.NewJsxText(_scanner.TokenValue(), _token == Kind.JsxTextAllWhiteSpaces);
        ScanJsxText();
        return FinishNode(result, pos);
    }

    // ported from: internal/parser/parser.go:4931-4954
    private Node? ParseJsxExpression(bool inExpressionContext)
    {
        var pos = NodePos();
        if (!ParseExpected(Kind.OpenBraceToken))
        {
            return null;
        }
        Node? dotDotDotToken = null;
        Node? expression = null;
        if (_token != Kind.CloseBraceToken)
        {
            if (!inExpressionContext)
            {
                dotDotDotToken = ParseOptionalToken(Kind.DotDotDotToken);
            }
            // Only an AssignmentExpression is valid here per the JSX spec,
            // but we can unambiguously parse a comma sequence and provide
            // a better error message in grammar checking.
            expression = ParseExpression();
        }
        if (inExpressionContext)
        {
            ParseExpected(Kind.CloseBraceToken);
        }
        else if (ParseExpectedWithoutAdvancing(Kind.CloseBraceToken))
        {
            ScanJsxText();
        }
        return FinishNode(_factory.NewJsxExpression(dotDotDotToken, expression), pos);
    }

    // ported from: internal/parser/parser.go:4955-4959
    private Kind ScanJsxText()
    {
        _token = _scanner.ScanJsxToken();
        return _token;
    }

    // ported from: internal/parser/parser.go:4960-4964
    private Kind ScanJsxIdentifier()
    {
        _token = _scanner.ScanJsxIdentifier();
        return _token;
    }

    // ported from: internal/parser/parser.go:4965-4969
    private Kind ScanJsxAttributeValue()
    {
        _token = _scanner.ScanJsxAttributeValue();
        return _token;
    }

    // ported from: internal/parser/parser.go:4970-4984
    private Node ParseJsxClosingElement(Node open, bool inExpressionContext)
    {
        var pos = NodePos();
        ParseExpected(Kind.LessThanSlashToken);
        var tagName = ParseJsxElementName();
        if (ParseExpectedWithDiagnostic(Kind.GreaterThanToken, null /*diagnosticMessage*/, false /*shouldAdvance*/))
        {
            // manually advance the scanner in order to look for jsx text inside jsx
            if (inExpressionContext || !TagNamesAreEquivalent(open.TagName(), tagName))
            {
                NextToken();
            }
            else
            {
                ScanJsxText();
            }
        }
        return FinishNode(_factory.NewJsxClosingElement(tagName), pos);
    }

    // ported from: internal/parser/parser.go:4985-5019
    private Node ParseJsxOpeningOrSelfClosingElementOrOpeningFragment(bool inExpressionContext)
    {
        var pos = NodePos();
        ParseExpected(Kind.LessThanToken);
        if (_token == Kind.GreaterThanToken)
        {
            // See below for explanation of scanJsxText
            ScanJsxText();
            return FinishNode(_factory.NewJsxOpeningFragment(), pos);
        }
        var tagName = ParseJsxElementName();
        NodeList? typeArguments = null;
        if ((_contextFlags & NodeFlags.JavaScriptFile) == 0)
        {
            typeArguments = ParseTypeArguments();
        }
        var attributes = ParseJsxAttributes();
        Node result;
        if (_token == Kind.GreaterThanToken)
        {
            // Closing tag, so scan the immediately-following text with the JSX scanning instead
            // of regular scanning to avoid treating illegal characters (e.g. '#') as immediate
            // scanning errors
            ScanJsxText();
            result = _factory.NewJsxOpeningElement(tagName, typeArguments, attributes);
        }
        else
        {
            ParseExpected(Kind.SlashToken);
            if (ParseExpectedWithoutAdvancing(Kind.GreaterThanToken))
            {
                if (inExpressionContext)
                {
                    NextToken();
                }
                else
                {
                    ScanJsxText();
                }
            }
            result = _factory.NewJsxSelfClosingElement(tagName, typeArguments, attributes);
        }
        return FinishNode(result, pos);
    }

    // ported from: internal/parser/parser.go:5020-5036
    private Node ParseJsxElementName()
    {
        var pos = NodePos();
        // JsxElement can have name in the form of
        //      propertyAccessExpression
        //      primaryExpression in the form of an identifier and "this" keyword
        // We can't just simply use parseLeftHandSideExpressionOrHigher because then we will start consider class,function etc as a keyword
        // We only want to consider "this" as a primaryExpression
        var initialExpression = ParseJsxTagName();
        if (IsJsxNamespacedName(initialExpression))
        {
            return initialExpression; // `a:b.c` is invalid syntax, don't even look for the `.` if we parse `a:b`, and let `parseAttribute` report "unexpected :" instead.
        }
        var expression = initialExpression;
        while (ParseOptional(Kind.DotToken))
        {
            expression = FinishNode(_factory.NewPropertyAccessExpression(expression, null, ParseRightSideOfDot(true /*allowIdentifierNames*/, false /*allowPrivateIdentifiers*/, false /*allowUnicodeEscapeSequenceInIdentifierName*/), NodeFlags.None), pos);
        }
        return expression;
    }

    // ported from: internal/parser/parser.go:5038-5053
    private Node ParseJsxTagName()
    {
        var pos = NodePos();
        ScanJsxIdentifier();
        var isThis = _token == Kind.ThisKeyword;
        var tagName = ParseIdentifierNameErrorOnUnicodeEscapeSequence();
        if (ParseOptional(Kind.ColonToken))
        {
            ScanJsxIdentifier();
            return FinishNode(_factory.NewJsxNamespacedName(tagName, ParseIdentifierNameErrorOnUnicodeEscapeSequence()), pos);
        }
        if (isThis)
        {
            var result = _factory.NewKeywordExpression(Kind.ThisKeyword);
            return FinishNode(result, pos);
        }
        return tagName;
    }
}
