// ported from: internal/format/indent.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.1/§3.2/§3.8）：
// - 包级函数 → 静态类 `Format`；Go 未导出函数 → internal + PascalCase。
// - `(a, b int)` 多返回值 → C# 元组 `(int Line, int Character)`（§3.2）。
// - `utf8.DecodeRuneInString(text[pos:])` → `Scan.DecodeRuneAt(text, pos, out var size)`（UTF-16 代码单元下标；
//   返回的 size 是**代码单元数**，所以 Go 的 `pos += size` / `current -= size` 逐字对应，§3.1）。
// - `iter.Seq[ast.CommentRange]` → `IEnumerable<CommentRange>`（nil Seq → null，`Core.ConcatenateSeq` 跳过 null）。
// - `iter.Seq` 的 `for commentRange := range commentRanges` 需要 break → C# 的 `foreach` + `return`（Go 用 return 退出）。
// - `core.FindIndex(slice, func(n) bool { return n == x })` 的指针相等 → `Array.IndexOf`（Node 是 class，引用相等）。
// - `slices.Contains(parent.Arguments(), child)` → `Array.IndexOf(parent.Arguments() ?? [], child) >= 0`
//   （不用 LINQ，§3.8）。
// - `node.Loc.ContainedBy(...)`（Node.Loc 是字段）→ 逐字保留。
// - `panic`/`debug.Assert` → `Debug.Fail`/`Debug.Assert`。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.LsUtil.LsUtil;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

internal enum NextTokenKind
{
    Unknown = 0,
    OpenBrace = 1,
    CloseBrace = 2,
}

public static partial class Format
{
    public static int GetIndentationForNode(Node n, TextRange? ignoreActualIndentationRange, SourceFile sourceFile, FormatCodeSettings options)
    {
        var (startline, startpos) = Scan.GetECMALineAndByteOffsetOfPosition(sourceFile, Scan.GetTokenPosOfNode(n, sourceFile, false));
        return GetIndentationForNodeWorker(n, startline, startpos, ignoreActualIndentationRange /*indentationDelta*/, 0, sourceFile /*isNextChild*/, false, options);
    }

    /// <summary>
    /// GetIndentation computes the expected indentation for a position in a source file.
    /// This is the Go port of SmartIndenter.getIndentation from TypeScript.
    /// </summary>
    public static int GetIndentation(int position, SourceFile sourceFile, FormatCodeSettings options, bool assumeNewLineBeforeCloseBrace)
    {
        if (position > sourceFile.Text().Length)
        {
            return options.BaseIndentSize; // past EOF
        }

        // no indentation when the indent style is set to none,
        // so we can return fast
        if (options.IndentStyle == IndentStyle.None)
        {
            return 0;
        }

        var precedingToken = FindPrecedingTokenEx(sourceFile, position, null /*startNode*/, true /*excludeJSDoc*/);

        var enclosingCommentRange = GetRangeOfEnclosingComment(sourceFile, position, precedingToken);
        if (enclosingCommentRange is not null && enclosingCommentRange.Kind == Kind.MultiLineCommentTrivia)
        {
            return GetCommentIndent(sourceFile, position, options, enclosingCommentRange);
        }

        if (precedingToken is null)
        {
            return options.BaseIndentSize;
        }

        // no indentation in string/regex/template literals
        if (Format.IsStringOrRegularExpressionOrTemplateLiteral(precedingToken.Kind))
        {
            var tokenStart = Scan.GetTokenPosOfNode(precedingToken, sourceFile, false);
            if (tokenStart <= position && position < precedingToken.End())
            {
                return 0;
            }
        }

        var lineAtPosition = Scan.GetECMALineOfPosition(sourceFile, position);

        // indentation is first non-whitespace character in a previous line
        // for block indentation, we should look for a line which contains something that's not
        // whitespace.
        var currentToken = GetTokenAtPosition(sourceFile, position);
        // For object literals, we want indentation to work just like with blocks.
        // If the `{` starts in any position (even in the middle of a line), then
        // the following indentation should treat `{` as the start of that line (including leading whitespace).
        // ```
        //     const a: { x: undefined, y: undefined } = {}       // leading 4 whitespaces and { starts in the middle of line
        // ->
        //     const a: { x: undefined, y: undefined } = {
        //         x: undefined,
        //         y: undefined,
        //     }
        // ---------------------
        //     const a: {x : undefined, y: undefined } =
        //      {}
        // ->
        //     const a: { x: undefined, y: undefined } =
        //      {                                                  // leading 5 whitespaces and { starts at 6 column
        //          x: undefined,
        //          y: undefined,
        //      }
        // ```
        var isObjectLiteral = currentToken.Kind == Kind.OpenBraceToken && currentToken.Parent is not null && currentToken.Parent.Kind == Kind.ObjectLiteralExpression;
        if (options.IndentStyle == IndentStyle.Block || isObjectLiteral)
        {
            return GetBlockIndent(sourceFile, position, options);
        }

        if (precedingToken.Kind == Kind.CommaToken && precedingToken.Parent is not null && precedingToken.Parent.Kind != Kind.BinaryExpression)
        {
            // previous token is comma that separates items in list - find the previous item and try to derive indentation from it
            var actualIndentation = GetActualIndentationForListItemBeforeComma(precedingToken, sourceFile, options);
            if (actualIndentation != -1)
            {
                return actualIndentation;
            }
        }

        var containerList = GetListByPosition(position, precedingToken.Parent, sourceFile);
        // use list position if the preceding token is before any list items
        if (containerList is not null && !precedingToken.Loc.ContainedBy(containerList.Loc))
        {
            var useTheSameBaseIndentation = currentToken.Parent is not null && (currentToken.Parent.Kind == Kind.FunctionExpression || currentToken.Parent.Kind == Kind.ArrowFunction);
            var indentSize = 0;
            if (!useTheSameBaseIndentation)
            {
                indentSize = options.IndentSize;
            }

            var res = GetActualIndentationForListStartLine(containerList, sourceFile, options);
            if (res == -1)
            {
                return indentSize;
            }

            return res + indentSize;
        }

        return GetSmartIndent(sourceFile, position, precedingToken, lineAtPosition, assumeNewLineBeforeCloseBrace, options);
    }

    internal static int GetCommentIndent(SourceFile sourceFile, int position, FormatCodeSettings options, CommentRange enclosingCommentRange)
    {
        var previousLine = Scan.GetECMALineOfPosition(sourceFile, position) - 1;
        var commentStartLine = Scan.GetECMALineOfPosition(sourceFile, enclosingCommentRange.Range.Pos());

        Debug.Assert(commentStartLine >= 0, "commentStartLine >= 0");

        if (previousLine <= commentStartLine)
        {
            var lineStarts = Scan.GetECMALineStarts(sourceFile);
            return FindFirstNonWhitespaceColumn(lineStarts[commentStartLine], position, sourceFile, options);
        }

        var lineStarts2 = Scan.GetECMALineStarts(sourceFile);
        var startPositionOfLine = lineStarts2[previousLine];
        var (character, column) = FindFirstNonWhitespaceCharacterAndColumn(startPositionOfLine, position, sourceFile, options);

        if (column == 0)
        {
            return column;
        }

        var firstNonWhitespaceCharacterCode = sourceFile.Text()[startPositionOfLine + character];
        if (firstNonWhitespaceCharacterCode == '*')
        {
            return column - 1;
        }

        return column;
    }

    internal static IEnumerable<CommentRange>? GetLeadingCommentRangesOfNode(Node node, SourceFile file)
    {
        if (node.Kind == Kind.JsxText)
        {
            return null;
        }

        return Scan.GetLeadingCommentRanges(new NodeFactory(), file.Text(), node.Pos());
    }

    internal static CommentRange? GetRangeOfEnclosingComment(
        SourceFile sourceFile,
        int position,
        Node? precedingToken)
    {
        var tokenAtPosition = GetTokenAtPosition(sourceFile, position);
        var jsdoc = FindAncestor(tokenAtPosition, n => n.IsJSDoc());
        if (jsdoc is not null)
        {
            tokenAtPosition = jsdoc.Parent!;
        }

        var tokenStart = GetStartOfNode(tokenAtPosition, sourceFile, false /*includeJSDoc*/);
        if (tokenStart <= position && position < tokenAtPosition.End())
        {
            return null;
        }

        // Between two consecutive tokens, all comments are either trailing on the former
        // or leading on the latter (and none are in both lists).
        IEnumerable<CommentRange>? trailingRangesOfPreviousToken = null;
        if (precedingToken is not null)
        {
            trailingRangesOfPreviousToken = Scan.GetTrailingCommentRanges(new NodeFactory(), sourceFile.Text(), precedingToken.End());
        }

        var leadingRangesOfNextToken = GetLeadingCommentRangesOfNode(tokenAtPosition, sourceFile);
        var commentRanges = ConcatenateSeq(trailingRangesOfPreviousToken, leadingRangesOfNextToken);
        foreach (var commentRange in commentRanges)
        {
            if (commentRange.Range.ContainsExclusive(position) ||
                (position == commentRange.Range.End() &&
                    (commentRange.Kind == Kind.SingleLineCommentTrivia || position == sourceFile.Text().Length)))
            {
                return commentRange;
            }
        }

        return null;
    }

    internal static int GetBlockIndent(SourceFile sourceFile, int position, FormatCodeSettings options)
    {
        // move backwards until we find a line with a non-whitespace character,
        // then find the first non-whitespace character for that line.
        var current = position;
        while (current > 0)
        {
            var ch = Scan.DecodeRuneAt(sourceFile.Text(), current, out var size);
            if (!StringUtil.StringUtil.IsWhiteSpaceLike(ch))
            {
                break;
            }

            current -= size;
        }

        var lineStart = GetLineStartPositionForPosition(current, sourceFile);
        return FindFirstNonWhitespaceColumn(lineStart, current, sourceFile, options);
    }

    internal static int GetActualIndentationForListItemBeforeComma(Node commaToken, SourceFile sourceFile, FormatCodeSettings options)
    {
        // previous token is comma that separates items in list - find the previous item and try to derive indentation from it
        if (commaToken.Parent is null)
        {
            return -1;
        }

        var containingList = GetContainingList(commaToken, sourceFile);
        if (containingList is null)
        {
            return -1;
        }

        var commaIndex = FindIndex(containingList.Nodes, n => ReferenceEquals(n, commaToken));
        if (commaIndex > 0)
        {
            return DeriveActualIndentationFromList(containingList, commaIndex - 1, sourceFile, options);
        }

        return -1;
    }

    internal static NextTokenKind NextTokenIsCurlyBraceOnSameLineAsCursor(Node precedingToken, Node current, int lineAtPosition, SourceFile sourceFile)
    {
        var nextToken = FindNextToken(precedingToken, current, sourceFile);
        if (nextToken is null)
        {
            return NextTokenKind.Unknown;
        }

        if (nextToken.Kind == Kind.OpenBraceToken)
        {
            // open braces are always indented at the parent level
            return NextTokenKind.OpenBrace;
        }
        else if (nextToken.Kind == Kind.CloseBraceToken)
        {
            // close braces are indented at the parent level if they are located on the same line with cursor
            var nextTokenStartLine = GetStartLineForNode(nextToken, sourceFile);
            if (lineAtPosition == nextTokenStartLine)
            {
                return NextTokenKind.CloseBrace;
            }

            return NextTokenKind.Unknown;
        }

        return NextTokenKind.Unknown;
    }

    internal static int GetSmartIndent(SourceFile sourceFile, int position, Node precedingToken, int lineAtPosition, bool assumeNewLineBeforeCloseBrace, FormatCodeSettings options)
    {
        // try to find node that can contribute to indentation and includes 'position' starting from 'precedingToken'
        // if such node is found - compute initial indentation for 'position' inside this node
        Node? previous = null;
        var current = precedingToken;

        while (current is not null)
        {
            if (PositionBelongsToNode(current, position, sourceFile) && ShouldIndentChildNode(options, current, previous, sourceFile, true))
            {
                var (currentStartLine, currentStartChar) = GetStartLineAndCharacterForNode(current, sourceFile);
                var ntk = NextTokenIsCurlyBraceOnSameLineAsCursor(precedingToken, current, lineAtPosition, sourceFile);
                var indentationDelta = 0;
                if (ntk != NextTokenKind.Unknown)
                {
                    // handle cases when codefix is about to be inserted before the close brace
                    if (assumeNewLineBeforeCloseBrace && ntk == NextTokenKind.CloseBrace)
                    {
                        indentationDelta = options.IndentSize;
                    }

                    // else 0
                }
                else
                {
                    if (lineAtPosition != currentStartLine)
                    {
                        indentationDelta = options.IndentSize;
                    }
                }

                return GetIndentationForNodeWorker(current, currentStartLine, currentStartChar, null, indentationDelta, sourceFile, true, options);
            }

            // check if current node is a list item - if yes, take indentation from it
            // do not consider parent-child line sharing yet:
            // function foo(a
            //    | preceding node 'a' does share line with its parent but indentation is expected
            var actualIndentation = GetActualIndentationForListItem(current, sourceFile, options, true /*listIndentsChild*/);
            if (actualIndentation != -1)
            {
                return actualIndentation;
            }

            previous = current;
            current = current.Parent;
        }

        // no parent was found - return the base indentation of the SourceFile
        return options.BaseIndentSize;
    }

    internal static int GetIndentationForNodeWorker(
        Node current,
        int currentStartLine,
        int currentStartCharacter,
        TextRange? ignoreActualIndentationRange,
        int indentationDelta,
        SourceFile sourceFile,
        bool isNextChild,
        FormatCodeSettings options)
    {
        var parent = current.Parent;

        // Walk up the tree and collect indentation for parent-child node pairs. Indentation is not added if
        // * parent and child nodes start on the same line, or
        // * parent is an IfStatement and child starts on the same line as an 'else clause'.
        while (parent is not null)
        {
            var useActualIndentation = true;
            if (ignoreActualIndentationRange is not null)
            {
                var start = Scan.GetTokenPosOfNode(current, sourceFile, false);
                useActualIndentation = start < ignoreActualIndentationRange.Value.Pos() || start > ignoreActualIndentationRange.Value.End();
            }

            var (containingListOrParentStartLine, containingListOrParentStartCharacter) = GetContainingListOrParentStart(parent, current, sourceFile);
            var parentAndChildShareLine = containingListOrParentStartLine == currentStartLine ||
                ChildStartsOnTheSameLineWithElseInIfStatement(parent, current, currentStartLine, sourceFile);

            if (useActualIndentation)
            {
                // check if current node is a list item - if yes, take indentation from it
                Node? firstListChild = null;
                var containerList = GetContainingList(current, sourceFile);
                if (containerList is not null)
                {
                    firstListChild = FirstOrNil(containerList.Nodes);
                }

                // A list indents its children if the children begin on a later line than the list itself:
                //
                // f1(               L0 - List start
                //   {               L1 - First child start: indented, along with all other children
                //     prop: 0
                //   },
                //   {
                //     prop: 1
                //   }
                // )
                //
                // f2({             L0 - List start and first child start: children are not indented.
                //   prop: 0             Object properties are indented only one level, because the list
                // }, {                  itself contributes nothing.
                //   prop: 1        L3 - The indentation of the second object literal is best understood by
                // })                    looking at the relationship between the list and *first* list item.
                var listIndentsChild = false;
                if (firstListChild is not null)
                {
                    var listLine = GetStartLineForNode(firstListChild, sourceFile);
                    listIndentsChild = listLine > containingListOrParentStartLine;
                }

                var actualIndentation = GetActualIndentationForListItem(current, sourceFile, options, listIndentsChild);
                if (actualIndentation != -1)
                {
                    return actualIndentation + indentationDelta;
                }

                // try to fetch actual indentation for current node from source text
                actualIndentation = GetActualIndentationForNode(current, parent, currentStartLine, currentStartCharacter, parentAndChildShareLine, sourceFile, options);
                if (actualIndentation != -1)
                {
                    return actualIndentation + indentationDelta;
                }
            }

            // increase indentation if parent node wants its content to be indented and parent and child nodes don't start on the same line
            if (ShouldIndentChildNode(options, parent, current, sourceFile, isNextChild) && !parentAndChildShareLine)
            {
                indentationDelta += options.IndentSize;
            }

            // In our AST, a call argument's `parent` is the call-expression, not the argument list.
            // We would like to increase indentation based on the relationship between an argument and its argument-list,
            // so we spoof the starting position of the (parent) call-expression to match the (non-parent) argument-list.
            // But, the spoofed start-value could then cause a problem when comparing the start position of the call-expression
            // to *its* parent (in the case of an iife, an expression statement), adding an extra level of indentation.
            //
            // Instead, when at an argument, we unspoof the starting position of the enclosing call expression
            // *after* applying indentation for the argument.

            var useTrueStart = IsArgumentAndStartLineOverlapsExpressionBeingCalled(parent, current, currentStartLine, sourceFile);

            current = parent;
            parent = current.Parent;

            if (useTrueStart)
            {
                (currentStartLine, currentStartCharacter) = Scan.GetECMALineAndByteOffsetOfPosition(sourceFile, Scan.GetTokenPosOfNode(current, sourceFile, false));
            }
            else
            {
                currentStartLine = containingListOrParentStartLine;
                currentStartCharacter = containingListOrParentStartCharacter;
            }
        }

        return indentationDelta + options.BaseIndentSize;
    }

    /// <summary>Function returns -1 if actual indentation for node should not be used (i.e because node is nested expression).</summary>
    internal static int GetActualIndentationForNode(Node? current, Node parent, int cuurentLine, int currentChar, bool parentAndChildShareLine, SourceFile sourceFile, FormatCodeSettings options)
    {
        // actual indentation is used for statements\declarations if one of cases below is true:
        // - parent is SourceFile - by default immediate children of SourceFile are not indented except when user indents them manually
        // - parent and child are not on the same line
        var useActualIndentation = (IsDeclaration(current!) || IsStatementButNotDeclaration(current!)) && (parent.Kind == Kind.SourceFile || !parentAndChildShareLine);

        if (!useActualIndentation)
        {
            return -1;
        }

        return FindColumnForFirstNonWhitespaceCharacterInLine(cuurentLine, currentChar, sourceFile, options);
    }

    internal static bool IsArgumentAndStartLineOverlapsExpressionBeingCalled(Node parent, Node child, int childStartLine, SourceFile sourceFile)
    {
        if (!(IsCallExpression(parent) && Array.IndexOf(parent.Arguments() ?? [], child) >= 0))
        {
            return false;
        }

        var expressionOfCallExpressionEnd = parent.Expression()!.End();
        var expressionOfCallExpressionEndLine = Scan.GetECMALineOfPosition(sourceFile, expressionOfCallExpressionEnd);
        return expressionOfCallExpressionEndLine == childStartLine;
    }

    internal static int GetActualIndentationForListItem(Node node, SourceFile sourceFile, FormatCodeSettings options, bool listIndentsChild)
    {
        if (node.Parent is not null && node.Parent.Kind == Kind.VariableDeclarationList)
        {
            // VariableDeclarationList has no wrapping tokens
            return -1;
        }

        var containingList = GetContainingList(node, sourceFile);
        if (containingList is not null)
        {
            var index = FindIndex(containingList.Nodes, e => ReferenceEquals(e, node));
            if (index != -1)
            {
                var result = DeriveActualIndentationFromList(containingList, index, sourceFile, options);
                if (result != -1)
                {
                    return result;
                }
            }

            var delta = 0;
            if (listIndentsChild)
            {
                delta = options.IndentSize;
            }

            var res = GetActualIndentationForListStartLine(containingList, sourceFile, options);
            if (res == -1)
            {
                return delta;
            }

            return res + delta;
        }

        return -1;
    }

    internal static int GetActualIndentationForListStartLine(NodeList? list, SourceFile sourceFile, FormatCodeSettings options)
    {
        if (list is null)
        {
            return -1;
        }

        var (line, char_) = Scan.GetECMALineAndByteOffsetOfPosition(sourceFile, list.Loc.Pos());
        return FindColumnForFirstNonWhitespaceCharacterInLine(line, char_, sourceFile, options);
    }

    internal static int DeriveActualIndentationFromList(NodeList list, int index, SourceFile sourceFile, FormatCodeSettings options)
    {
        // Go: debug.Assert(list != nil && index >= 0 && index < len(list.Nodes))
        // `list != nil` 那半句在 C# 里是死代码（参数是非空引用类型，§3.2），删掉后也避免 Debug.Assert 的
        // 条件把 list 的 null 状态"污染"成 maybe-null（C# 的 `x is not null` 只在其为真的分支里收窄，
        // 语句之后仍是 true/false 两支的合并状态）。剩下的界检查与 Go 逐字对应。
        Debug.Assert(index >= 0 && index < (list.Nodes?.Length ?? 0));

        var listNodes = list.Nodes!;
        var node = listNodes[index]!;

        // walk toward the start of the list starting from current node and check if the line is the same for all items.
        // if end line for item [i - 1] differs from the start line for item [i] - find column of the first non-whitespace character on the line of item [i]

        var (line, char_) = GetStartLineAndCharacterForNode(node, sourceFile);

        for (var i = index; i >= 0; i--)
        {
            if (list.Nodes![i]!.Kind == Kind.CommaToken)
            {
                continue;
            }

            // skip list items that ends on the same line with the current list element
            var prevEndLine = Scan.GetECMALineOfPosition(sourceFile, list.Nodes![i]!.End());
            if (prevEndLine != line)
            {
                return FindColumnForFirstNonWhitespaceCharacterInLine(line, char_, sourceFile, options);
            }

            (line, char_) = GetStartLineAndCharacterForNode(list.Nodes![i]!, sourceFile);
        }

        return -1;
    }

    internal static int FindColumnForFirstNonWhitespaceCharacterInLine(int line, int char_, SourceFile sourceFile, FormatCodeSettings options)
    {
        var lineStart = Scan.GetECMAPositionOfLineAndByteOffset(sourceFile, line, 0);
        return FindFirstNonWhitespaceColumn(lineStart, lineStart + char_, sourceFile, options);
    }

    public static int FindFirstNonWhitespaceColumn(int startPos, int endPos, SourceFile sourceFile, FormatCodeSettings options)
    {
        var (_, col) = FindFirstNonWhitespaceCharacterAndColumn(startPos, endPos, sourceFile, options);
        return col;
    }

    /// <summary>
    /// Character is the actual index of the character since the beginning of the line.
    /// Column - position of the character after expanding tabs to spaces.
    /// "0\t2$"
    /// value of 'character' for '$' is 3
    /// value of 'column' for '$' is 6 (assuming that tab size is 4)
    /// </summary>
    internal static (int Character, int Column) FindFirstNonWhitespaceCharacterAndColumn(int startPos, int endPos, SourceFile sourceFile, FormatCodeSettings options)
    {
        var column = 0;
        var text = sourceFile.Text();
        var pos = startPos;
        while (pos < endPos)
        {
            var ch = Scan.DecodeRuneAt(text, pos, out var size);
            if (!StringUtil.StringUtil.IsWhiteSpaceSingleLine(ch))
            {
                break;
            }

            if (ch == '\t')
            {
                if (options.TabSize > 0)
                {
                    column += options.TabSize + (column % options.TabSize);
                }
            }
            else
            {
                column++;
            }

            pos += size;
        }

        return (pos - startPos, column);
    }

    internal static bool ChildStartsOnTheSameLineWithElseInIfStatement(Node parent, Node child, int childStartLine, SourceFile sourceFile)
    {
        if (parent.Kind == Kind.IfStatement && ReferenceEquals(parent.AsIfStatement()!.ElseStatement, child))
        {
            var elseKeyword = FindPrecedingToken(sourceFile, child.Pos());
            Debug.Assert(elseKeyword is not null);
            var elseKeywordStartLine = GetStartLineForNode(elseKeyword!, sourceFile);
            return elseKeywordStartLine == childStartLine;
        }

        return false;
    }

    internal static (int Line, int Character) GetStartLineAndCharacterForNode(Node n, SourceFile sourceFile)
    {
        return Scan.GetECMALineAndByteOffsetOfPosition(sourceFile, Scan.GetTokenPosOfNode(n, sourceFile, false));
    }

    internal static int GetStartLineForNode(Node n, SourceFile sourceFile)
    {
        return Scan.GetECMALineOfPosition(sourceFile, Scan.GetTokenPosOfNode(n, sourceFile, false));
    }

    public static NodeList? GetContainingList(Node node, SourceFile sourceFile)
    {
        if (node.Parent is null)
        {
            return null;
        }

        return GetListByRange(Scan.GetTokenPosOfNode(node, sourceFile, false), node.End(), node.Parent, sourceFile);
    }

    internal static NodeList? GetListByPosition(int pos, Node? node, SourceFile sourceFile)
    {
        if (node is null)
        {
            return null;
        }

        return GetListByRange(pos, pos, node, sourceFile);
    }

    internal static NodeList? GetListByRange(int start, int end, Node node, SourceFile sourceFile)
    {
        var r = NewTextRange(start, end);
        switch (node.Kind)
        {
            case Kind.TypeReference:
                return GetList(node.TypeArgumentList(), r, node, sourceFile);
            case Kind.ObjectLiteralExpression:
                return GetList(node.PropertyList(), r, node, sourceFile);
            case Kind.ArrayLiteralExpression:
                return GetList(node.ElementList(), r, node, sourceFile);
            case Kind.TypeLiteral:
                return GetList(node.MemberList(), r, node, sourceFile);
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.CallSignature:
            case Kind.Constructor:
            case Kind.ConstructorType:
            case Kind.ConstructSignature:
                var tpl = GetList(node.TypeParameterList(), r, node, sourceFile);
                if (tpl is not null)
                {
                    return tpl;
                }

                return GetList(node.ParameterList(), r, node, sourceFile);
            case Kind.GetAccessor:
                return GetList(node.ParameterList(), r, node, sourceFile);
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSDocTemplateTag:
                return GetList(node.TypeParameterList(), r, node, sourceFile);
            case Kind.NewExpression:
            case Kind.CallExpression:
                var l = GetList(node.TypeArgumentList(), r, node, sourceFile);
                if (l is not null)
                {
                    return l;
                }

                return GetList(node.ArgumentList(), r, node, sourceFile);
            case Kind.VariableDeclarationList:
                return GetList(node.AsVariableDeclarationList()!.Declarations, r, node, sourceFile);
            case Kind.ObjectBindingPattern:
            case Kind.ArrayBindingPattern:
            case Kind.NamedImports:
            case Kind.NamedExports:
                return GetList(node.ElementList(), r, node, sourceFile);
        }

        return null; // TODO: should this be a panic? It isn't in strada.
    }

    internal static NodeList? GetList(NodeList? list, TextRange r, Node node, SourceFile sourceFile)
    {
        if (list is null)
        {
            return null;
        }

        if (r.ContainedBy(GetVisualListRange(node, list.Loc, sourceFile)))
        {
            return list;
        }

        return null;
    }

    internal static TextRange GetVisualListRange(Node node, TextRange list, SourceFile sourceFile)
    {
        // In strada, this relied on the services .getChildren method, which manifested synthetic token nodes
        // _however_, the logic boils down to "find the child with the matching span and adjust its start to the
        // previous (possibly token) child's end and its end to the token start of the following element" - basically
        // expanding the range to encompass all the neighboring non-token trivia
        // Now, we perform that logic with the scanner instead
        var prior = FindPrecedingToken(sourceFile, list.Pos());
        int priorEnd;
        if (prior is null)
        {
            priorEnd = list.Pos();
        }
        else
        {
            priorEnd = prior.End();
        }

        // Find the token that starts at or after list.End() using the scanner
        var scan = Scan.GetScannerForSourceFile(sourceFile, list.End());
        int nextStart;
        if (scan.Token() == Kind.EndOfFile)
        {
            nextStart = list.End();
        }
        else
        {
            nextStart = scan.TokenStart();
        }

        return NewTextRange(priorEnd, nextStart);
    }

    internal static (int Line, int Character) GetContainingListOrParentStart(Node parent, Node child, SourceFile sourceFile)
    {
        var containingList = GetContainingList(child, sourceFile);
        int startPos;
        if (containingList is not null)
        {
            startPos = containingList.Loc.Pos();
        }
        else
        {
            startPos = Scan.GetTokenPosOfNode(parent, sourceFile, false);
        }

        return Scan.GetECMALineAndByteOffsetOfPosition(sourceFile, startPos);
    }

    internal static bool IsControlFlowEndingStatement(Kind kind, Kind parentKind)
    {
        switch (kind)
        {
            case Kind.ReturnStatement:
            case Kind.ThrowStatement:
            case Kind.ContinueStatement:
            case Kind.BreakStatement:
                return parentKind != Kind.Block;
            default:
                return false;
        }
    }

    /// <summary>
    /// True when the parent node should indent the given child by an explicit rule.
    /// </summary>
    /// <param name="isNextChildArg">If true, we are judging indent of a hypothetical child *after* this one, not the current child.</param>
    public static bool ShouldIndentChildNode(FormatCodeSettings settings, Node parent, Node? child, SourceFile sourceFile, params ReadOnlySpan<bool> isNextChildArg)
    {
        var isNextChild = false;
        if (isNextChildArg.Length > 0)
        {
            isNextChild = isNextChildArg[0];
        }

        return NodeWillIndentChild(settings, parent, child, sourceFile, false) && !(isNextChild && child is not null && IsControlFlowEndingStatement(child.Kind, parent.Kind));
    }

    public static bool NodeWillIndentChild(FormatCodeSettings settings, Node parent, Node? child, SourceFile? sourceFile, bool indentByDefault)
    {
        var childKind = Kind.Unknown;
        if (child is not null)
        {
            childKind = child.Kind;
        }

        switch (parent.Kind)
        {
            case Kind.ExpressionStatement:
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.ArrayLiteralExpression:
            case Kind.Block:
            case Kind.ModuleBlock:
            case Kind.ObjectLiteralExpression:
            case Kind.TypeLiteral:
            case Kind.MappedType:
            case Kind.TupleType:
            case Kind.ParenthesizedExpression:
            case Kind.PropertyAccessExpression:
            case Kind.CallExpression:
            case Kind.NewExpression:
            case Kind.VariableStatement:
            case Kind.ExportAssignment:
            case Kind.ReturnStatement:
            case Kind.ConditionalExpression:
            case Kind.ArrayBindingPattern:
            case Kind.ObjectBindingPattern:
            case Kind.JsxOpeningElement:
            case Kind.JsxOpeningFragment:
            case Kind.JsxSelfClosingElement:
            case Kind.JsxExpression:
            case Kind.MethodSignature:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.Parameter:
            case Kind.FunctionType:
            case Kind.ConstructorType:
            case Kind.ParenthesizedType:
            case Kind.TaggedTemplateExpression:
            case Kind.AwaitExpression:
            case Kind.NamedExports:
            case Kind.NamedImports:
            case Kind.ExportSpecifier:
            case Kind.ImportSpecifier:
            case Kind.PropertyDeclaration:
            case Kind.CaseClause:
            case Kind.DefaultClause:
                return true;
            case Kind.CaseBlock:
                return settings.IndentSwitchCase.IsTrueOrUnknown();
            case Kind.VariableDeclaration:
            case Kind.PropertyAssignment:
            case Kind.BinaryExpression:
                if (settings.IndentMultiLineObjectLiteralBeginningOnBlankLine.IsFalseOrUnknown() && sourceFile is not null && childKind == Kind.ObjectLiteralExpression)
                {
                    return RangeIsOnOneLine(child!.Loc, sourceFile);
                }

                if (parent.Kind == Kind.BinaryExpression && sourceFile is not null && childKind == Kind.JsxElement)
                {
                    var parentStartLine = Scan.GetECMALineOfPosition(sourceFile, Scan.SkipTrivia(sourceFile.Text(), parent.Pos()));
                    var childStartLine = Scan.GetECMALineOfPosition(sourceFile, Scan.SkipTrivia(sourceFile.Text(), child!.Pos()));
                    return parentStartLine != childStartLine;
                }

                if (parent.Kind != Kind.BinaryExpression)
                {
                    return true;
                }

                return indentByDefault;
            case Kind.DoStatement:
            case Kind.WhileStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.ForStatement:
            case Kind.IfStatement:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.MethodDeclaration:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return childKind != Kind.Block;
            case Kind.ArrowFunction:
                if (sourceFile is not null && childKind == Kind.ParenthesizedExpression)
                {
                    return RangeIsOnOneLine(child!.Loc, sourceFile);
                }

                return childKind != Kind.Block;
            case Kind.ExportDeclaration:
                return childKind != Kind.NamedExports;
            case Kind.ImportDeclaration:
                return childKind != Kind.ImportClause || (child!.AsImportClause()!.NamedBindings is not null && child.AsImportClause()!.NamedBindings!.Kind != Kind.NamedImports);
            case Kind.JsxElement:
                return childKind != Kind.JsxClosingElement;
            case Kind.JsxFragment:
                return childKind != Kind.JsxClosingFragment;
            case Kind.IntersectionType:
            case Kind.UnionType:
            case Kind.SatisfiesExpression:
                if (childKind == Kind.TypeLiteral || childKind == Kind.TupleType || childKind == Kind.MappedType)
                {
                    return false;
                }

                return indentByDefault;
            case Kind.TryStatement:
                if (childKind == Kind.Block)
                {
                    return false;
                }

                return indentByDefault;
        }

        // No explicit rule for given nodes so the result will follow the default value argument
        return indentByDefault;
    }

    /// <summary>
    /// A multiline conditional typically increases the indentation of its whenTrue and whenFalse children:
    /// <code>
    /// condition
    ///   ? whenTrue
    ///   : whenFalse;
    /// </code>
    /// <para>
    /// However, that indentation does not apply if the subexpressions themselves span multiple lines,
    /// applying their own indentation:
    /// </para>
    /// <code>
    /// (() => {
    ///   return complexCalculationForCondition();
    /// })() ? {
    ///
    ///   whenTrue: 'multiline object literal'
    /// } : (
    ///
    /// whenFalse('multiline parenthesized expression')
    ///
    /// );
    /// </code>
    /// <para>
    /// In these cases, we must discard the indentation increase that would otherwise be applied to the
    /// whenTrue and whenFalse children to avoid double-indenting their contents. To identify this scenario,
    /// we check for the whenTrue branch beginning on the line that the condition ends, and the whenFalse
    /// branch beginning on the line that the whenTrue branch ends.
    /// </para>
    /// </summary>
    internal static bool ChildIsUnindentedBranchOfConditionalExpression(Node parent, Node child, int childStartLine, SourceFile sourceFile)
    {
        if (parent.Kind == Kind.ConditionalExpression && (ReferenceEquals(child, parent.AsConditionalExpression()!.WhenTrue) || ReferenceEquals(child, parent.AsConditionalExpression()!.WhenFalse)))
        {
            var conditionEndLine = Scan.GetECMALineOfPosition(sourceFile, parent.AsConditionalExpression()!.Condition!.End());
            if (ReferenceEquals(child, parent.AsConditionalExpression()!.WhenTrue))
            {
                return childStartLine == conditionEndLine;
            }
            else
            {
                // On the whenFalse side, we have to look at the whenTrue side, because if that one was
                // indented, whenFalse must also be indented:
                //
                // const y = true
                //   ? 1 : (          L1: whenTrue indented because it's on a new line
                //     0              L2: indented two stops, one because whenTrue was indented
                //   );                   and one because of the parentheses spanning multiple lines
                var trueStartLine = GetStartLineForNode(parent.AsConditionalExpression()!.WhenTrue!, sourceFile);
                var trueEndLine = Scan.GetECMALineOfPosition(sourceFile, parent.AsConditionalExpression()!.WhenTrue!.End());
                return conditionEndLine == trueStartLine && trueEndLine == childStartLine;
            }
        }

        return false;
    }

    internal static bool ArgumentStartsOnSameLineAsPreviousArgument(Node parent, Node child, int childStartLine, SourceFile sourceFile)
    {
        if (IsCallExpression(parent) || IsNewExpression(parent))
        {
            var args = parent.Arguments();
            if (args is null || args.Length == 0)
            {
                return false;
            }

            var currentIndex = FindIndex(args, n => ReferenceEquals(n, child));
            if (currentIndex == -1)
            {
                // If it's not one of the arguments, don't look past this
                return false;
            }

            if (currentIndex == 0)
            {
                return false; // Can't look at previous node if first
            }

            var previousNode = args[currentIndex - 1]!;
            var lineOfPreviousNode = Scan.GetECMALineOfPosition(sourceFile, previousNode.End());
            if (childStartLine == lineOfPreviousNode)
            {
                return true;
            }
        }

        return false;
    }
}
