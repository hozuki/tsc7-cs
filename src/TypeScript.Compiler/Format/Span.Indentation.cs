// ported from: internal/format/span.go:1042-1261（consumeTokenAndAdvanceScanner 的后半 / dynamicIndenter /
//            getFirstNonDecoratorTokenOfNode / getDynamicIndentation）
//            —— 与 Span.cs 同属 `partial class Format`，按逻辑段落切分（docs §2 的大文件拆分）。
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.2）：
// - 未导出类型 `dynamicIndenter` → `DynamicIndenter`（class，与 Go 的 `*dynamicIndenter` 语义一致）；
//   未导出字段 → `_camelCase`。
// - `core.Find(node.ModifierNodes()[core.FindIndex(...):], ast.IsModifier)`（切片 + 查找）→ C# 没有 `Find` 的
//   偏移重载，落成等价的 `for` 循环（从 decorator 下标开始找第一个 modifier）。
// - `ast.KindEnumDeclaration` 分支：Go 源码在 `case ast.KindEnumDeclaration` 里**返回的是节点 kind 本身**
//   （`return ast.KindEnumDeclaration`，看着是笔误，应是 `KindEnumKeyword`）。这里逐字保留 Go 的行为；
//   该分支只在 `HasDecorators(node)` 为真时可达，而 enum 声明不可能带装饰器，所以不可观察。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

internal sealed partial class FormatSpanWorker
{
    internal void ConsumeTokenAndAdvanceScanner(TokenInfo currentTokenInfo, Node parent, DynamicIndenter dynamicIndenation, Node container, bool isListEndToken)
    {
        // assert(currentTokenInfo.token.Loc.ContainedBy(parent.Loc)) // !!!
        var lastTriviaWasNewLine = _formattingScanner!.LastTrailingTriviaWasNewLine();
        var indentToken = false;

        if (currentTokenInfo._leadingTrivia.Length > 0)
        {
            ProcessTrivia(currentTokenInfo._leadingTrivia, parent, _childContextNode!, dynamicIndenation);
        }

        var lineAction = LineAction.None;
        var isTokenInRange = currentTokenInfo._token.Loc.ContainedBy(_originalRange);

        var (tokenStartLine, tokenStartChar) = Scan.GetECMALineAndByteOffsetOfPosition(_sourceFile, currentTokenInfo._token.Loc.Pos());

        if (isTokenInRange)
        {
            var rangeHasError = _rangeContainsError(currentTokenInfo._token.Loc);
            // save previousRange since processRange will overwrite this value with current one
            var savePreviousRange = _previousRange;
            lineAction = ProcessRange(currentTokenInfo._token, tokenStartLine, tokenStartChar, parent, _childContextNode!, dynamicIndenation);
            // do not indent comments\token if token range overlaps with some error
            if (!rangeHasError)
            {
                if (lineAction == LineAction.None)
                {
                    // indent token only if end line of previous range does not match start line of the token
                    if (savePreviousRange != Format.NewTextRangeWithKind(0, 0, 0))
                    {
                        var prevEndLine = Scan.GetECMALineOfPosition(_sourceFile, savePreviousRange.Loc.End());
                        indentToken = lastTriviaWasNewLine && tokenStartLine != prevEndLine;
                    }
                    else
                    {
                        // When there's no previous range (first token), TS sets prevEndLine to undefined.
                        // tokenStart.line !== undefined is always true in JS, so indentToken = lastTriviaWasNewLine.
                        indentToken = lastTriviaWasNewLine;
                    }
                }
                else
                {
                    indentToken = lineAction == LineAction.LineAdded;
                }
            }
        }

        if (currentTokenInfo._trailingTrivia.Length > 0)
        {
            _previousRangeTriviaEnd = LastOrNil(currentTokenInfo._trailingTrivia).Loc.End();
            // If any trailing comment trivia extends past the original range, it won't be
            // processed by processTrivia (which skips comments not contained by originalRange).
            // Cap previousRangeTriviaEnd before such comments so the trailing edit contiguity
            // check in execute() won't pair across unprocessed comment content.
            foreach (var trivia in currentTokenInfo._trailingTrivia)
            {
                if (Format.IsComment(trivia.Kind) && !trivia.Loc.ContainedBy(_originalRange))
                {
                    _previousRangeTriviaEnd = trivia.Loc.Pos();
                    break;
                }
            }

            ProcessTrivia(currentTokenInfo._trailingTrivia, parent, _childContextNode!, dynamicIndenation);
        }

        if (indentToken)
        {
            var tokenIndentation = -1;
            if (isTokenInRange && !_rangeContainsError(currentTokenInfo._token.Loc))
            {
                tokenIndentation = dynamicIndenation.GetIndentationForToken(tokenStartLine, currentTokenInfo._token.Kind, container, isListEndToken);
            }

            var indentNextTokenOrTrivia = true;
            if (currentTokenInfo._leadingTrivia.Length > 0)
            {
                var commentIndentation = dynamicIndenation.GetIndentationForComment(currentTokenInfo._token.Kind, tokenIndentation, container);
                indentNextTokenOrTrivia = IndentTriviaItems(currentTokenInfo._leadingTrivia, commentIndentation, indentNextTokenOrTrivia, item =>
                {
                    InsertIndentation(item.Loc.Pos(), commentIndentation, false);
                });
            }

            // indent token only if is it is in target range and does not overlap with any error ranges
            if (tokenIndentation != -1 && indentNextTokenOrTrivia)
            {
                InsertIndentation(currentTokenInfo._token.Loc.Pos(), tokenIndentation, lineAction == LineAction.LineAdded);

                _lastIndentedLine = tokenStartLine;
                _indentationOnLastIndentedLine = tokenIndentation;
            }
        }

        _formattingScanner.Advance();

        _childContextNode = parent;
    }

    internal DynamicIndenter GetDynamicIndentation(Node node, int nodeStartLine, int indentation, int delta)
    {
        return new DynamicIndenter
        {
            _node = node,
            _nodeStartLine = nodeStartLine,
            _indentation = indentation,
            _delta = delta,
            _options = _formattingContext!.Options,
            _sourceFile = _sourceFile,
        };
    }
}

internal sealed class DynamicIndenter
{
    internal Node _node = null!;
    internal int _nodeStartLine;
    internal int _indentation;
    internal int _delta;

    internal FormatCodeSettings _options;
    internal SourceFile _sourceFile = null!;

    internal int GetIndentationForComment(Kind kind, int tokenIndentation, Node container)
    {
        switch (kind)
        {
            // preceding comment to the token that closes the indentation scope inherits the indentation from the scope
            // ..  {
            //     // comment
            // }
            case Kind.CloseBraceToken:
            case Kind.CloseBracketToken:
            case Kind.CloseParenToken:
                return _indentation + GetDelta(container);
        }

        if (tokenIndentation != -1)
        {
            return tokenIndentation;
        }

        return _indentation;
    }

    // if list end token is LessThanToken '>' then its delta should be explicitly suppressed
    // so that LessThanToken as a binary operator can still be indented.
    // foo.then
    //
    //	<
    //	    number,
    //	    string,
    //	>();
    //
    // vs
    // var a = xValue
    //
    //	> yValue;
    internal int GetIndentationForToken(int line, Kind kind, Node container, bool suppressDelta)
    {
        if (!suppressDelta && ShouldAddDelta(line, kind, container))
        {
            return _indentation + GetDelta(container);
        }

        return _indentation;
    }

    internal int GetIndentation()
    {
        return _indentation;
    }

    internal int GetDelta(Node child)
    {
        // Delta value should be zero when the node explicitly prevents indentation of the child node
        if (Format.NodeWillIndentChild(_options, _node, child, _sourceFile, true))
        {
            return _delta;
        }

        return 0;
    }

    internal void RecomputeIndentation(bool lineAdded, Node parent)
    {
        if (Format.ShouldIndentChildNode(_options, parent, _node, _sourceFile))
        {
            if (lineAdded)
            {
                _indentation += _options.IndentSize; // !!! no nil check???
            }
            else
            {
                _indentation -= _options.IndentSize; // !!! no nil check???
            }

            if (Format.ShouldIndentChildNode(_options, _node, null, null!))
            {
                _delta = _options.IndentSize;
            }
            else
            {
                _delta = 0;
            }
        }
    }

    internal bool ShouldAddDelta(int line, Kind kind, Node container)
    {
        switch (kind)
        {
            // open and close brace, 'else' and 'while' (in do statement) tokens has indentation of the parent
            case Kind.OpenBraceToken:
            case Kind.CloseBraceToken:
            case Kind.CloseParenToken:
            case Kind.ElseKeyword:
            case Kind.WhileKeyword:
            case Kind.AtToken:
                return false;
            case Kind.SlashToken:
            case Kind.GreaterThanToken:
                switch (container.Kind)
                {
                    case Kind.JsxOpeningElement:
                    case Kind.JsxClosingElement:
                    case Kind.JsxSelfClosingElement:
                        return false;
                }

                break;
            case Kind.OpenBracketToken:
            case Kind.CloseBracketToken:
                if (container.Kind != Kind.MappedType)
                {
                    return false;
                }

                break;
        }

        // if token line equals to the line of containing node (this is a first token in the node) - use node indentation
        return _nodeStartLine != line &&
            // if this token is the first token following the list of decorators, we do not need to indent
            !(HasDecorators(_node) && kind == Format.GetFirstNonDecoratorTokenOfNode(_node));
    }
}

public static partial class Format
{
    internal static Kind GetFirstNonDecoratorTokenOfNode(Node node)
    {
        if (CanHaveModifiers(node))
        {
            // Go: core.Find(node.ModifierNodes()[core.FindIndex(node.ModifierNodes(), ast.IsDecorator):], ast.IsModifier)
            // —— C# 的 Core.Find 没有"偏移切片"的重载，这里用等价的循环（从 decorator 的下标开始找第一个 modifier）。
            var modifierNodes = node.ModifierNodes();
            var start = FindIndex(modifierNodes, n => IsDecorator(n));
            Node? modifier = null;
            for (var i = start; i < (modifierNodes?.Length ?? 0); i++)
            {
                if (IsModifier(modifierNodes![i]!))
                {
                    modifier = modifierNodes[i];
                    break;
                }
            }

            if (modifier is not null)
            {
                return modifier.Kind;
            }
        }

        switch (node.Kind)
        {
            case Kind.ClassDeclaration:
                return Kind.ClassKeyword;
            case Kind.InterfaceDeclaration:
                return Kind.InterfaceKeyword;
            case Kind.FunctionDeclaration:
                return Kind.FunctionKeyword;
            case Kind.EnumDeclaration:
                return Kind.EnumDeclaration; // Go: `return ast.KindEnumDeclaration`（见文件头）
            case Kind.GetAccessor:
                return Kind.GetKeyword;
            case Kind.SetAccessor:
                return Kind.SetKeyword;
            case Kind.MethodDeclaration:
                if (node.AsMethodDeclaration()!.AsteriskToken is not null)
                {
                    return Kind.AsteriskToken;
                }

                goto case Kind.PropertyDeclaration;
            case Kind.PropertyDeclaration:
            case Kind.Parameter:
                var name = GetNameOfDeclaration(node);
                if (name is not null)
                {
                    return name.Kind;
                }

                break;
        }

        return Kind.Unknown;
    }
}
