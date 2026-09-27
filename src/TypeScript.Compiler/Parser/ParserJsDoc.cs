// ported from: internal/parser/jsdoc.go:1-1355（JSDoc 注释解析：注释文本/tag/@link/withJSDoc 入口；W6 分片，整文件）
//
// JSDoc 注释解析：`parseJSDocCommentWorker`（注释文本 + tag 列表）、每个 tag 的解析（`@param`/`@typedef`/
// `@template`/`@see`…）、`@link` 文本，以及 `withJSDoc`（语句/声明解析器调用的入口，惰性/急切解析的分流点）。
//
// 与 Go 的偏离（逐条；依据 docs/architecture.md §2/§3.1/§3.2/§3.8 与 W1 冻结的 Parser 字段契约）：
//
// 1) Go 的 `func init() { ast.SetParseJSDocForNode(parseJSDocForNode) }` → `[ModuleInitializer]`（jsdoc.go:13-15）。
//    C# 没有包级 init，且 parser 与 ast 同程序集，模块初始化器与 Go"导入 parser 包即完成注册"语义一致。
// 2) 包级类型 `jsdocState`/`propertyLikeParse` 在本文件命名空间级声明（成员名去前缀、数值照 Go）。
// 3) `ast.SetParseJSDocForNode` 的回调类型是 `Func<SourceFile, Node, Node[]?>`（Ast.cs:24），Go 的
//    `[]*ast.Node`（可为 nil）→ `Node[]?`。
// 4) Go 的 `defer putParser(p)` → `try/finally`（返回值先算完再 defer，语义一致）。
// 5) **切片复用的 scratch 空间**（W1 契约）：`_jsdocCommentsSpace`（List<string>）与
//    `_jsdocCommentRangesSpace`（List<CommentRange>）非空、用 `Clear()` 表达 Go 的 `x = x[:0]`；
//    `_jsdocTagCommentsSpace`/`_jsdocTagCommentsPartsSpace` 可空（Go 写 `p.x = nil` 防重入）。
//    注意 Go 是"字段拿 len=0 视图、局部变量仍看全量"，C# 的 List 无法这样共享，故本文件一律
//    **先用完局部内容、最后再 `Clear()` 写回字段**（返回的 NodeList 已在 Clear 前 Clone 出来）。
// 6) Go 的 `x = append(x, y)`（写回字段/局部切片）→ `List<T>.Add`；`p.nodeSliceArena.NewSlice(0)` + append
//    → `List<T>` + `ToArray()`（§3.2）。
// 7) Go 的 `p.diagnostics[0:n]` / `append(p.jsdocDiagnostics, p.diagnostics[n:]...)` → `List<Diagnostic>` 的
//    `RemoveRange` / 逐项 `Add`（§3.2）；`len(p.diagnostics)` → `_diagnostics.Count`。
// 8) Go 的 `(T, bool)` 返回值 → `bool TryX(..., out T)`：`parseJSDocLinkPrefix` → `TryParseJSDocLinkPrefix`、
//    `parseBracketNameInPropertyAndParamTag` → `TryParseBracketNameInPropertyAndParamTag`。
// 9) Go 的 `func(...)` 回调用 lambda（实例方法组不能转成 `Func<...>`，见 W2/Main 的约定）；不捕获变量的用
//    `static p => ...`（零闭包分配）。
// 10) docs §3.1：`len(s)`/下标一律是 UTF-16 代码单元。**两处已知字节↔代码单元差异**（都只在源文本含非 ASCII
//     时可能不同，且只影响缩进/空白裁剪的启发式）：
//     * `strings.LastIndex(sourceText[:start], "\n")` → `AsSpan(0, start).LastIndexOf('\n')`（零拷贝）；
//     * `for i, ch := range text` 按**码点**迭代 → C# 用 `DecodeRuneAt` 显式解码码点（在 ParserReparser.cs）。
// 11) `p.sourceText = p.sourceText[:end-2]`（Go 是零拷贝切片）→ C# 的 `string` 切片必然分配子串（§3.8 的
//     "string 无视图"），只在解析一个 JSDoc 注释期间存在，语义一致。
// 12) Go 的 `unicode.IsSpace` → `char.IsWhiteSpace`（实测 .NET 的空白集合与 Go 的 unicode.IsSpace 一致：
//     U+001C–U+001F 两边都不是空白）；`stringutil.IsWhiteSpaceLike` 直接用 StringUtil 的同名函数。
// 13) Go 的 nil 解引用（`x.Name().Text()`、`tag.TypeExpression().Type()` 等）→ C# 的 `!`：抛 NRE，与 Go 的
//     panic 等价（W4 文件头同款约定）。
// 14) 同名遮蔽：本类有实例方法 `IsIdentifier()`（Go `p.isIdentifier()`，W5 定义），它会遮蔽 `using static`
//     引入的 `Ast.IsIdentifier(Node)` —— 这类调用一律写全名 `TypeScript.Ast.Ast.IsIdentifier(...)`（docs §2
//     说明命名空间限定拿不到静态类成员，故用完整名而不是 `Ast.X`）。
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
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
    /// <summary>
    /// Go: <c>func init() { ast.SetParseJSDocForNode(parseJSDocForNode) }</c>（jsdoc.go:13-15）：注册
    /// TS/TSX 文件的懒 JSDoc 解析回调（SourceFile.ResolveJSDoc → Ast._parseJSDocForNodeCallback）。
    /// </summary>
    /// <remarks>
    /// CA2255 抑制的论证：Go 的 `init()` 语义是"**整个程序**里导入了 parser 包就注册"；本项目 parser 与 ast 同在
    /// 一个程序集，模块初始化器正是同一件事（模块加载即注册，NativeAOT 也保证在 Main 之前跑）。CA2255 的顾虑是
    /// "库的模块初始化器何时运行不确定/消费者可能永远不加载该库"——本程序集不会脱离自身被加载（SourceFile 的
    /// 懒 JSDoc 路径本就要求 parser 在场），故这里是安全的。
    /// </remarks>
    [SuppressMessage("Usage", "CA2255", Justification = "parser 与 ast 同程序集，模块初始化器等价于 Go 的 parser 包 init；见上方 remarks。")]
    [ModuleInitializer]
    internal static void InitializeJSDocParser()
    {
        SetParseJSDocForNode(ParseJSDocForNode);
    }

    /// <summary>
    /// parseJSDocForNode lazily parses JSDoc for a node in a TS file.
    /// Called on first access to Node.JSDoc() for non-JS source files.
    /// </summary>
    // ported from: internal/parser/jsdoc.go:17-35
    internal static Node[]? ParseJSDocForNode(SourceFile sourceFile, Node node)
    {
        var p = GetParser();
        try
        {
            p.InitializeState(sourceFile.ParseOptions(), sourceFile.Text(), sourceFile.ScriptKind);
            var ranges = GetJSDocCommentRanges(p._factory, null, node, sourceFile.Text());
            if (ranges.Count == 0)
            {
                return null;
            }

            var jsdoc = new List<Node>(ranges.Count);
            var pos = node.Pos();
            foreach (var comment in ranges)
            {
                var parsed = p.ParseJSDocComment(node, comment.Range.Pos(), comment.Range.End(), pos);
                if (parsed is not null)
                {
                    parsed.Parent = node;
                    jsdoc.Add(parsed);
                    pos = parsed.End();
                }
            }

            return jsdoc.ToArray();
        }
        finally
        {
            PutParser(p);
        }
    }

    // ported from: internal/parser/jsdoc.go:56-105
    //
    // Go 的返回值是 `[]*ast.Node`（可能为 nil）；W1 冻结的 C# 签名是 `Node[]`，且 56 个调用点全部忽略返回值
    // （parser.go），故 Go 里返回 nil 的分支在这里返回空数组（§3.2："语义等同时用 []"）。
    internal Node[] WithJSDoc(Node node, JsdocScannerInfo info)
    {
        if ((info & JsdocScannerInfo.HasJSDoc) == 0)
        {
            return [];
        }

        // For TS/TSX files, defer JSDoc parsing to first access, unless the comment
        // contains @see/@link (needed for unused-identifier checks).
        // @deprecated is detected via cheap text scan to set PossiblyContainsDeprecatedTag;
        // callers must confirm via JSDoc lookup.
        if (!IsJavaScript())
        {
            node.Flags |= NodeFlags.HasJSDoc;
            if ((info & JsdocScannerInfo.HasDeprecated) != 0)
            {
                node.Flags |= NodeFlags.PossiblyContainsDeprecatedTag;
            }

            if ((info & JsdocScannerInfo.HasSeeOrLink) == 0)
            {
                return [];
            }

            // Fall through to eager parse for @see/@link
        }

        var ranges = GetJSDocCommentRanges(_factory, _jsdocCommentRangesSpace, node, _sourceText);

        // Should only be called once per node
        _hasDeprecatedTag = false;
        var jsdoc = new List<Node>();
        var pos = node.Pos();
        foreach (var comment in ranges)
        {
            var parsed = ParseJSDocComment(node, comment.Range.Pos(), comment.Range.End(), pos);
            if (parsed is not null)
            {
                parsed.Parent = node;
                jsdoc.Add(parsed);
                pos = parsed.End();
            }
        }

        // Go: p.jsdocCommentRangesSpace = ranges[:0]（迭代结束后清空同一 List 以复用其容量）
        ranges.Clear();
        if (jsdoc.Count != 0)
        {
            if ((node.Flags & NodeFlags.HasJSDoc) == 0)
            {
                node.Flags |= NodeFlags.HasJSDoc;
            }

            if (_hasDeprecatedTag)
            {
                _hasDeprecatedTag = false;
                node.Flags |= NodeFlags.PossiblyContainsDeprecatedTag;
            }

            if (IsJavaScript())
            {
                ReparseTags(node, jsdoc);
            }

            var jsdocNodes = jsdoc.ToArray();
            _jsdocInfos.Add(new JSDocInfo(node, jsdocNodes));
            return jsdocNodes;
        }

        return [];
    }

    // ported from: internal/parser/jsdoc.go:107-123
    private Node ParseJSDocTypeExpression(bool mayOmitBraces)
    {
        var pos = NodePos();
        bool hasBrace;
        if (mayOmitBraces)
        {
            hasBrace = ParseOptional(Kind.OpenBraceToken);
        }
        else
        {
            hasBrace = ParseExpected(Kind.OpenBraceToken);
        }

        var saveContextFlags = _contextFlags;
        SetContextFlags(NodeFlags.JSDoc, true);
        var t = ParseJSDocType();
        _contextFlags = saveContextFlags;
        if (hasBrace)
        {
            ParseExpectedJSDoc(Kind.CloseBraceToken);
        }

        return FinishNode(_factory.NewJSDocTypeExpression(t), pos);
    }

    // ported from: internal/parser/jsdoc.go:125-135
    private Node ParseJSDocNameReference()
    {
        var pos = NodePos();
        var hasBrace = ParseOptional(Kind.OpenBraceToken);
        var entityName = ParseJSDocLinkName();
        if (hasBrace)
        {
            ParseExpectedJSDoc(Kind.CloseBraceToken);
        }

        _scanner.ResetPos(_scanner.TokenFullStart());
        NextTokenJSDoc();
        return FinishNode(_factory.NewJSDocNameReference(entityName), pos);
    }

    /// <summary>Pass end=-1 to parse the text to the end.</summary>
    // ported from: internal/parser/jsdoc.go:137-183
    private Node? ParseJSDocComment(Node parent, int start, int end, int fullStart)
    {
        if (end == -1)
        {
            end = _sourceText.Length;
        }

        // Check for /** (JSDoc opening part)
        if (!IsJSDocLikeText(_sourceText[start..]))
        {
            // Go 的注释：这里本该是 panic（parseSingleJSDocComment 尚未移植）；C# 保持与 Go 相同的"返回 nil"。
            return null;
        }

        var saveSourceText = _sourceText;
        var saveToken = _token;
        var saveContextFlags = _contextFlags;
        var saveParsingContexts = _parsingContexts;
        var saveScannerState = _scanner.Mark();
        var saveDiagnosticsLength = _diagnostics.Count;
        var saveHasParseError = _hasParseError;
        var saveHasAwaitIdentifier = _statementHasAwaitIdentifier;

        // initial indent is start+4 to account for leading `/** `
        // + 1 because \n is one character before the first character in the line and,
        // if there is no \n before start, -1 is one index before the first character in the string
        var initialIndent = start + 4 - (_sourceText.AsSpan(0, start).LastIndexOf('\n') + 1);
        // -2 for trailing `*/`
        _sourceText = _sourceText[..(end - 2)];
        _scanner.SetText(_sourceText);
        // +3 for leading `/**`
        _scanner.ResetPos(start + 3);
        SetContextFlags(NodeFlags.JSDoc, true);
        _parsingContexts |= (ParsingContexts)(1 << (int)ParsingContext.JSDocComment);

        var comment = ParseJSDocCommentWorker(start, end, fullStart, initialIndent);

        // move jsdoc diagnostics to jsdocDiagnostics -- for JS files only
        if ((_contextFlags & NodeFlags.JavaScriptFile) != 0)
        {
            for (var i = saveDiagnosticsLength; i < _diagnostics.Count; i++)
            {
                _jsdocDiagnostics.Add(_diagnostics[i]);
            }
        }

        _diagnostics.RemoveRange(saveDiagnosticsLength, _diagnostics.Count - saveDiagnosticsLength);

        _sourceText = saveSourceText;
        _scanner.SetText(_sourceText);
        _parsingContexts = saveParsingContexts;
        _contextFlags = saveContextFlags;
        _scanner.Rewind(saveScannerState);
        _token = saveToken;
        _hasParseError = saveHasParseError;
        _statementHasAwaitIdentifier = saveHasAwaitIdentifier;

        return comment;
    }

    /// <summary>
    /// @param offset - the offset in the containing file
    /// @param indent - the number of spaces to consider as the margin (applies to non-first lines only)
    /// </summary>
    // ported from: internal/parser/jsdoc.go:189-373
    private Node ParseJSDocCommentWorker(int start, int end, int fullStart, int indent)
    {
        // Initially we can parse out a tag.  We also have seen a starting asterisk.
        // This is so that /** * @type */ doesn't parse.
        var tags = new List<Node>();
        var tagsPos = -1;
        var tagsEnd = -1;
        var state = JSDocState.SawAsterisk;
        var backtickCount = 0;
        var inFencedCodeBlock = false;
        var commentParts = new List<Node>();
        var comments = _jsdocCommentsSpace;
        var commentsPos = -1;
        var linkEnd = start;
        var margin = -1;

        void PushComment(string text)
        {
            if (margin == -1)
            {
                margin = indent;
            }

            comments.Add(text);
            indent += text.Length;
        }

        NextTokenJSDoc();
        while (ParseOptionalJsdoc(Kind.WhitespaceTrivia))
        {
        }

        if (ParseOptionalJsdoc(Kind.NewLineTrivia))
        {
            state = JSDocState.BeginningOfLine;
            indent = 0;
        }

        var atEndOfFile = false;
        while (!atEndOfFile)
        {
            // Detect fenced code blocks by counting consecutive backtick tokens.
            // Three or more consecutive backticks toggle the fenced code block state.
            if (_token != Kind.BacktickToken && backtickCount > 0)
            {
                if (backtickCount >= 3)
                {
                    inFencedCodeBlock = !inFencedCodeBlock;
                }

                backtickCount = 0;
            }

            switch (_token)
            {
                case Kind.AtToken:
                    {
                        if (inFencedCodeBlock || !_scanner.CanFollowJSDocAt())
                        {
                            if (inFencedCodeBlock)
                            {
                                state = JSDocState.SavingBackticks;
                            }
                            else
                            {
                                state = JSDocState.SavingComments;
                            }

                            PushComment(_scanner.TokenText());
                            break;
                        }

                        RemoveTrailingWhitespace(comments);
                        if (commentsPos == -1)
                        {
                            commentsPos = NodePos();
                        }

                        var tag = ParseTag(tags, indent);
                        if (tagsPos == -1)
                        {
                            tagsPos = tag.Pos();
                        }

                        tags.Add(tag);
                        tagsEnd = tag.End();
                        // NOTE: According to usejsdoc.org, a tag goes to end of line, except the last tag.
                        // Real-world comments may break this rule, so "BeginningOfLine" will not be a real line beginning
                        // for malformed examples like `/** @param {string} x @returns {number} the length */`
                        state = JSDocState.BeginningOfLine;
                        margin = -1;
                        break;
                    }

                case Kind.NewLineTrivia:
                    comments.Add(_scanner.TokenText());
                    state = JSDocState.BeginningOfLine;
                    indent = 0;
                    break;

                case Kind.AsteriskToken:
                    {
                        var asterisk = _scanner.TokenText();
                        if (state == JSDocState.SawAsterisk)
                        {
                            // If we've already seen an asterisk, then we can no longer parse a tag on this line
                            state = JSDocState.SavingComments;
                            PushComment(asterisk);
                        }
                        else
                        {
                            if (state != JSDocState.BeginningOfLine)
                            {
                                throw new DebugFailureException("state must be BeginningOfLine");
                            }

                            // Ignore the first asterisk on a line
                            state = JSDocState.SawAsterisk;
                            indent += asterisk.Length;
                        }

                        break;
                    }

                case Kind.WhitespaceTrivia:
                    {
                        if (state == JSDocState.SavingComments || state == JSDocState.SavingBackticks)
                        {
                            throw new DebugFailureException("whitespace shouldn't come from the scanner while saving top-level comment text");
                        }

                        // only collect whitespace if we're already saving comments or have just crossed the comment indent margin
                        var whitespace = _scanner.TokenText();
                        if (margin > -1 && indent + whitespace.Length > margin)
                        {
                            var existingIndent = margin - indent;
                            if (existingIndent < 0)
                            {
                                existingIndent += whitespace.Length;
                            }

                            if (existingIndent < 0)
                            {
                                existingIndent = 0;
                            }

                            comments.Add(whitespace[existingIndent..]);
                        }

                        indent += whitespace.Length;
                        break;
                    }

                case Kind.EndOfFile:
                    atEndOfFile = true;
                    break;

                case Kind.JSDocCommentTextToken:
                    {
                        if (state != JSDocState.SavingBackticks)
                        {
                            if (inFencedCodeBlock)
                            {
                                state = JSDocState.SavingBackticks;
                            }
                            else
                            {
                                state = JSDocState.SavingComments;
                            }
                        }

                        PushComment(_scanner.TokenValue());
                        break;
                    }

                case Kind.BacktickToken:
                    backtickCount++;
                    if (state == JSDocState.SavingBackticks)
                    {
                        state = JSDocState.SavingComments;
                    }
                    else
                    {
                        state = JSDocState.SavingBackticks;
                    }

                    PushComment(_scanner.TokenText());
                    break;

                case Kind.OpenBraceToken:
                    {
                        if (inFencedCodeBlock)
                        {
                            state = JSDocState.SavingBackticks;
                            PushComment(_scanner.TokenText());
                            break;
                        }

                        state = JSDocState.SavingComments;
                        var commentEnd = _scanner.TokenFullStart();
                        var linkStart = _scanner.TokenEnd() - 1;
                        var link = ParseJSDocLink(linkStart);
                        if (link is not null)
                        {
                            if (linkEnd == start)
                            {
                                RemoveLeadingNewlines(comments);
                            }

                            var jsdocText = FinishNodeWithEnd(_factory.NewJSDocText(_stringSliceArena.Clone(CollectionsMarshal.AsSpan(comments))), linkEnd, commentEnd);
                            commentParts.Add(jsdocText);
                            commentParts.Add(link);
                            comments.Clear();
                            linkEnd = _scanner.TokenEnd();
                            break;
                        }

                        goto default;
                    }

                default:
                    {
                        // Anything else is doc comment text. We just save it. Because it
                        // wasn't a tag, we can no longer parse a tag on this line until we hit the next
                        // line break.
                        if (state != JSDocState.SavingBackticks)
                        {
                            if (inFencedCodeBlock)
                            {
                                state = JSDocState.SavingBackticks;
                            }
                            else
                            {
                                state = JSDocState.SavingComments;
                            }
                        }

                        PushComment(_scanner.TokenText());
                        break;
                    }
            }

            if (atEndOfFile)
            {
                // Go: `break loop` —— 直接跳出循环，不再推进 token
                break;
            }

            if (state == JSDocState.SavingComments || state == JSDocState.SavingBackticks)
            {
                NextJSDocCommentTextToken(state == JSDocState.SavingBackticks);
            }
            else
            {
                NextTokenJSDoc();
            }
        }

        // Go: p.jsdocCommentsSpace = comments[:0]（下面的末尾文本仍要读 comments，故 Clear 放到最后）
        if (commentsPos == -1)
        {
            commentsPos = _scanner.TokenFullStart();
        }

        if (comments.Count > 0)
        {
            // Go: strings.TrimRightFunc(comments[len(comments)-1], unicode.IsSpace)
            var lastComment = comments[^1];
            var lastCommentEnd = lastComment.Length;
            while (lastCommentEnd > 0 && char.IsWhiteSpace(lastComment[lastCommentEnd - 1]))
            {
                lastCommentEnd--;
            }

            comments[^1] = lastComment[..lastCommentEnd];
            var jsdocText = FinishNodeWithEnd(_factory.NewJSDocText(_stringSliceArena.Clone(CollectionsMarshal.AsSpan(comments))), linkEnd, commentsPos);
            commentParts.Add(jsdocText);
        }

        if (commentParts.Count > 0 && tags.Count > 0 && commentsPos == -1)
        {
            throw new DebugFailureException("having parsed tags implies that the end of the comment span should be set");
        }

        NodeList? tagsNodeList = null;
        if (tagsPos != -1)
        {
            tagsNodeList = NewNodeList(NewTextRange(tagsPos, tagsEnd), tags.ToArray());
        }

        var jsdocComment = _factory.NewJSDoc(NewNodeList(NewTextRange(start, commentsPos), commentParts.ToArray()), tagsNodeList);
        var result = FinishNodeWithEnd(jsdocComment, fullStart, end);

        // 复用 scratch 空间（Go: p.jsdocCommentsSpace = comments[:0]）
        comments.Clear();
        return result;
    }

    // ported from: internal/parser/jsdoc.go:375-382
    private static void RemoveLeadingNewlines(List<string> comments)
    {
        var i = 0;
        while (i < comments.Count && comments[i].Trim('\r', '\n').Length == 0)
        {
            i++;
        }

        if (i > 0)
        {
            comments.RemoveRange(0, i);
        }
    }

    // ported from: internal/parser/jsdoc.go:384-386
    private static string TrimEnd(string s)
    {
        var end = s.Length;
        while (end > 0 && IsWhiteSpaceLike(s[end - 1]))
        {
            end--;
        }

        return s[..end];
    }

    // ported from: internal/parser/jsdoc.go:388-400
    private static void RemoveTrailingWhitespace(List<string> comments)
    {
        var end = comments.Count;
        for (var i = comments.Count - 1; i >= 0; i--)
        {
            var trimmed = TrimEnd(comments[i]);
            if (trimmed.Length == 0)
            {
                end = i;
            }
            else
            {
                comments[i] = trimmed;
                break;
            }
        }

        if (end < comments.Count)
        {
            comments.RemoveRange(end, comments.Count - end);
        }
    }

    // ported from: internal/parser/jsdoc.go:402-415
    private bool IsNextNonwhitespaceTokenEndOfFile()
    {
        // We must use infinite lookahead, as there could be any number of newlines :(
        while (true)
        {
            NextTokenJSDoc();
            if (_token == Kind.EndOfFile)
            {
                return true;
            }

            if (!(_token == Kind.WhitespaceTrivia || _token == Kind.NewLineTrivia))
            {
                return false;
            }
        }
    }

    // ported from: internal/parser/jsdoc.go:417-427
    private void SkipWhitespace()
    {
        if (_token == Kind.WhitespaceTrivia || _token == Kind.NewLineTrivia)
        {
            if (LookAhead(static p => p.IsNextNonwhitespaceTokenEndOfFile()))
            {
                return;
                // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
            }
        }

        while (_token == Kind.WhitespaceTrivia || _token == Kind.NewLineTrivia)
        {
            NextTokenJSDoc();
        }
    }

    // ported from: internal/parser/jsdoc.go:429-463
    private string SkipWhitespaceOrAsterisk()
    {
        if (_token == Kind.WhitespaceTrivia || _token == Kind.NewLineTrivia)
        {
            if (LookAhead(static p => p.IsNextNonwhitespaceTokenEndOfFile()))
            {
                return "";
                // Don't skip whitespace prior to EoF (or end of comment) - that shouldn't be included in any node's range
            }
        }

        var precedingLineBreak = _scanner.HasPrecedingLineBreak();
        var seenLineBreak = false;
        var indents = new List<string>(4);
        while ((precedingLineBreak && _token == Kind.AsteriskToken) || _token == Kind.WhitespaceTrivia || _token == Kind.NewLineTrivia)
        {
            indents.Add(_scanner.TokenText());
            if (_token == Kind.NewLineTrivia)
            {
                precedingLineBreak = true;
                seenLineBreak = true;
                indents.Clear();
            }
            else if (_token == Kind.AsteriskToken)
            {
                precedingLineBreak = false;
            }

            NextTokenJSDoc();
        }

        if (seenLineBreak)
        {
            return string.Join("", indents);
        }
        else
        {
            return "";
        }
    }

    // ported from: internal/parser/jsdoc.go:465-545
    private Node ParseTag(List<Node>? tags, int margin)
    {
        if (_token != Kind.AtToken)
        {
            throw new DebugFailureException("should be called only at the start of a tag");
        }

        var start = _scanner.TokenStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName(Identifier_expected);
        var indentText = SkipWhitespaceOrAsterisk();

        Node? tag;
        switch (tagName.Text())
        {
            case "implements":
                tag = ParseImplementsTag(start, tagName, margin, indentText);
                break;
            case "augments":
            case "extends":
                tag = ParseAugmentsTag(start, tagName, margin, indentText);
                break;
            case "public":
                tag = ParseSimpleTag(start, (name, comments) => _factory.NewJSDocPublicTag(name, comments), tagName, margin, indentText);
                break;
            case "private":
                tag = ParseSimpleTag(start, (name, comments) => _factory.NewJSDocPrivateTag(name, comments), tagName, margin, indentText);
                break;
            case "protected":
                tag = ParseSimpleTag(start, (name, comments) => _factory.NewJSDocProtectedTag(name, comments), tagName, margin, indentText);
                break;
            case "readonly":
                tag = ParseSimpleTag(start, (name, comments) => _factory.NewJSDocReadonlyTag(name, comments), tagName, margin, indentText);
                break;
            case "override":
                tag = ParseSimpleTag(start, (name, comments) => _factory.NewJSDocOverrideTag(name, comments), tagName, margin, indentText);
                break;
            case "deprecated":
                _hasDeprecatedTag = true;
                tag = ParseSimpleTag(start, (name, comments) => _factory.NewJSDocDeprecatedTag(name, comments), tagName, margin, indentText);
                break;
            case "this":
                tag = ParseThisTag(start, tagName, margin, indentText);
                break;
            case "arg":
            case "argument":
            case "param":
                tag = ParseParameterOrPropertyTag(start, tagName, PropertyLikeParse.Parameter, margin);
                break;
            case "return":
            case "returns":
                tag = ParseReturnTag(tags, start, tagName, margin, indentText);
                break;
            case "template":
                tag = ParseTemplateTag(start, tagName, margin, indentText);
                break;
            case "type":
                tag = ParseTypeTag(tags, start, tagName, margin, indentText);
                break;
            case "typedef":
                tag = ParseTypedefTag(start, tagName, margin, indentText);
                break;
            case "callback":
                tag = ParseCallbackTag(start, tagName, margin, indentText);
                break;
            case "overload":
                tag = ParseOverloadTag(start, tagName, margin, indentText);
                break;
            case "satisfies":
                tag = ParseSatisfiesTag(start, tagName, margin, indentText);
                break;
            case "see":
                tag = ParseSeeTag(start, tagName, margin, indentText);
                break;
            case "exception":
            case "throws":
                tag = ParseThrowsTag(start, tagName, margin, indentText);
                break;
            case "import":
                tag = ParseImportTag(start, tagName, margin, indentText);
                break;
            default:
                tag = ParseUnknownTag(start, tagName, margin, indentText);
                break;
        }

        if (tag is null)
        {
            throw new DebugFailureException("tag should not be nil");
        }

        return tag;
    }

    // ported from: internal/parser/jsdoc.go:547-558
    private NodeList? ParseTrailingTagComments(int pos, int end, int margin, string indentText)
    {
        // some tags, like typedef and callback, have already parsed their comments earlier
        if (indentText.Length == 0)
        {
            margin += end - pos;
        }

        // Go 传的是 `*string`（nil 与空串语义不同：nil = 没有初始缩进），C# 用 null 表达 nil。
        string? initialMargin = null;
        if (margin < indentText.Length)
        {
            initialMargin = indentText[margin..];
        }

        return ParseTagComments(margin, initialMargin);
    }

    // ported from: internal/parser/jsdoc.go:560-714
    private NodeList? ParseTagComments(int indent, string? initialMargin)
    {
        var commentsPos = NodePos();
        var comments = _jsdocTagCommentsSpace ?? [];
        _jsdocTagCommentsSpace = null; // !!! can parseTagComments call itself?
        var parts = _jsdocTagCommentsPartsSpace ?? [];
        _jsdocTagCommentsPartsSpace = null;
        var linkEnd = -1;
        var state = JSDocState.BeginningOfLine;
        var backtickCount = 0;
        var inFencedCodeBlock = false;
        if (indent < 0)
        {
            throw new DebugFailureException("indent must be a natural number");
        }

        var margin = -1;

        void PushComment(string text)
        {
            if (margin == -1)
            {
                margin = indent;
            }

            comments.Add(text);
            indent += text.Length;
        }

        if (initialMargin is not null)
        {
            // jump straight to saving comments if there is some initial indentation
            if (initialMargin.Length != 0)
            {
                PushComment(initialMargin);
            }

            state = JSDocState.SawAsterisk;
        }

        var tok = _token;
        var done = false;
        while (!done)
        {
            // Detect fenced code blocks by counting consecutive backtick tokens.
            // Three or more consecutive backticks toggle the fenced code block state.
            if (tok != Kind.BacktickToken && backtickCount > 0)
            {
                if (backtickCount >= 3)
                {
                    inFencedCodeBlock = !inFencedCodeBlock;
                }

                backtickCount = 0;
            }

            switch (tok)
            {
                case Kind.NewLineTrivia:
                    state = JSDocState.BeginningOfLine;
                    // don't use PushComment here because we want to keep the margin unchanged
                    comments.Add(_scanner.TokenText());
                    indent = 0;
                    break;

                case Kind.AtToken:
                    {
                        if (!inFencedCodeBlock && _scanner.CanFollowJSDocAt())
                        {
                            _scanner.ResetPos(_scanner.TokenEnd() - 1);
                            done = true;
                            break;
                        }

                        if (inFencedCodeBlock)
                        {
                            state = JSDocState.SavingBackticks;
                        }
                        else
                        {
                            state = JSDocState.SavingComments;
                        }

                        PushComment(_scanner.TokenText());
                        break;
                    }

                case Kind.EndOfFile:
                    // Done
                    done = true;
                    break;

                case Kind.WhitespaceTrivia:
                    {
                        if (state == JSDocState.SavingComments || state == JSDocState.SavingBackticks)
                        {
                            throw new DebugFailureException("whitespace shouldn't come from the scanner while saving comment text");
                        }

                        var whitespace = _scanner.TokenText();
                        // if the whitespace crosses the margin, take only the whitespace that passes the margin
                        if (margin > -1 && indent + whitespace.Length > margin)
                        {
                            comments.Add(whitespace[Math.Max(margin - indent, 0)..]);
                            if (inFencedCodeBlock)
                            {
                                state = JSDocState.SavingBackticks;
                            }
                            else
                            {
                                state = JSDocState.SavingComments;
                            }
                        }

                        indent += whitespace.Length;
                        break;
                    }

                case Kind.OpenBraceToken:
                    {
                        if (inFencedCodeBlock)
                        {
                            state = JSDocState.SavingBackticks;
                            PushComment(_scanner.TokenText());
                            break;
                        }

                        state = JSDocState.SavingComments;
                        var commentEnd = _scanner.TokenFullStart();
                        var linkStart = _scanner.TokenEnd() - 1;
                        var link = ParseJSDocLink(linkStart);
                        if (link is not null)
                        {
                            var commentStart = linkEnd > -1 ? linkEnd : commentsPos;
                            var text = FinishNodeWithEnd(_factory.NewJSDocText(_stringSliceArena.Clone(CollectionsMarshal.AsSpan(comments))), commentStart, commentEnd);
                            parts.Add(text);
                            parts.Add(link);
                            comments.Clear();
                            linkEnd = _scanner.TokenEnd();
                        }
                        else
                        {
                            PushComment(_scanner.TokenText());
                        }

                        break;
                    }

                case Kind.BacktickToken:
                    backtickCount++;
                    if (state == JSDocState.SavingBackticks)
                    {
                        state = JSDocState.SavingComments;
                    }
                    else
                    {
                        state = JSDocState.SavingBackticks;
                    }

                    PushComment(_scanner.TokenText());
                    break;

                case Kind.JSDocCommentTextToken:
                    if (state != JSDocState.SavingBackticks)
                    {
                        if (inFencedCodeBlock)
                        {
                            state = JSDocState.SavingBackticks;
                        }
                        else
                        {
                            state = JSDocState.SavingComments;
                        }

                        // leading identifiers start recording as well
                    }

                    PushComment(_scanner.TokenValue());
                    break;

                case Kind.AsteriskToken:
                    if (state == JSDocState.BeginningOfLine)
                    {
                        // leading asterisks start recording on the *next* (non-whitespace) token
                        state = JSDocState.SawAsterisk;
                        indent += 1;
                        break;
                    }

                    // record the * as a comment
                    goto default;

                default:
                    {
                        if (state != JSDocState.SavingBackticks)
                        {
                            if (inFencedCodeBlock)
                            {
                                state = JSDocState.SavingBackticks;
                            }
                            else
                            {
                                state = JSDocState.SavingComments;
                            }

                            // leading identifiers start recording as well
                        }

                        PushComment(_scanner.TokenText());
                        break;
                    }
            }

            if (done)
            {
                // Go: `break loop` —— 不再推进 token
                break;
            }

            if (state == JSDocState.SavingComments || state == JSDocState.SavingBackticks)
            {
                tok = NextJSDocCommentTextToken(state == JSDocState.SavingBackticks);
            }
            else
            {
                tok = NextTokenJSDoc();
            }
        }

        RemoveLeadingNewlines(comments);
        RemoveTrailingWhitespace(comments);
        if (comments.Count > 0)
        {
            var commentStart = linkEnd > -1 ? linkEnd : commentsPos;
            var text = FinishNode(_factory.NewJSDocText(_stringSliceArena.Clone(CollectionsMarshal.AsSpan(comments))), commentStart);
            parts.Add(text);
        }

        // Go 的 `p.jsdocTagCommentsSpace = comments[:0]` / `p.jsdocTagCommentsPartsSpace = parts[:0]` 是"字段拿 len=0
        // 视图、局部变量仍看全量"；C# 先在上面把要用的内容都取出（Clone/构造 NodeList），最后再清空写回字段。
        var result = parts.Count > 0
            ? NewNodeList(NewTextRange(commentsPos, _scanner.TokenEnd()), _nodeSliceArena.Clone(CollectionsMarshal.AsSpan(parts)))
            : null;

        comments.Clear();
        parts.Clear();
        _jsdocTagCommentsSpace = comments;
        _jsdocTagCommentsPartsSpace = parts;
        return result;
    }

    // ported from: internal/parser/jsdoc.go:716-740
    private Node? ParseJSDocLink(int start)
    {
        var state = Mark();
        if (!TryParseJSDocLinkPrefix(out var linkType))
        {
            Rewind(state);
            return null;
        }

        NextTokenJSDoc();
        // start at token after link, then skip any whitespace
        SkipWhitespace();
        var name = ParseJSDocLinkName();
        var text = new List<string>();
        while (_token != Kind.CloseBraceToken && _token != Kind.NewLineTrivia && _token != Kind.EndOfFile)
        {
            text.Add(_scanner.TokenText());
            NextTokenJSDoc(); // Couldn't this be nextTokenCommentJSDoc?
        }

        Node create;
        switch (linkType)
        {
            case "link":
                create = _factory.NewJSDocLink(name, text.ToArray());
                break;
            case "linkcode":
                create = _factory.NewJSDocLinkCode(name, text.ToArray());
                break;
            default:
                create = _factory.NewJSDocLinkPlain(name, text.ToArray());
                break;
        }

        return FinishNodeWithEnd(create, start, _scanner.TokenEnd());
    }

    // ported from: internal/parser/jsdoc.go:742-763
    private Node? ParseJSDocLinkName()
    {
        if (TokenIsIdentifierOrKeyword(_token))
        {
            var pos = NodePos();
            var name = ParseIdentifierName();
            while (ParseOptional(Kind.DotToken))
            {
                Node right;
                if (_token == Kind.PrivateIdentifier)
                {
                    right = CreateMissingIdentifier();
                }
                else
                {
                    right = ParseIdentifierName();
                }

                name = FinishNode(_factory.NewQualifiedName(name, right), pos);
            }

            while (_token == Kind.PrivateIdentifier)
            {
                _scanner.ReScanHashToken();
                NextTokenJSDoc();
                name = FinishNode(_factory.NewQualifiedName(name, ParseIdentifier()), pos);
            }

            return name;
        }

        return null;
    }

    // ported from: internal/parser/jsdoc.go:765-775
    //
    // Go 的 `(string, bool)` → `bool TryX(..., out string)`（约定）；失败时 out 给 Go 的失败值 "NONE"。
    private bool TryParseJSDocLinkPrefix(out string kind)
    {
        SkipWhitespaceOrAsterisk();
        if (_token == Kind.OpenBraceToken && NextTokenJSDoc() == Kind.AtToken && TokenIsIdentifierOrKeyword(NextTokenJSDoc()))
        {
            var linkKind = _scanner.TokenValue();
            if (IsJSDocLinkTag(linkKind))
            {
                kind = linkKind;
                return true;
            }
        }

        kind = "NONE";
        return false;
    }

    // ported from: internal/parser/jsdoc.go:777-779
    private static bool IsJSDocLinkTag(string kind) => kind is "link" or "linkcode" or "linkplain";

    // ported from: internal/parser/jsdoc.go:781-783
    private Node ParseUnknownTag(int start, Node tagName, int indent, string indentText) =>
        FinishNode(_factory.NewJSDocUnknownTag(tagName, ParseTrailingTagComments(start, NodePos(), indent, indentText)), start);

    // ported from: internal/parser/jsdoc.go:785-792
    private Node? TryParseTypeExpression()
    {
        SkipWhitespaceOrAsterisk();
        if (_token == Kind.OpenBraceToken)
        {
            return ParseJSDocTypeExpression(false /*mayOmitBraces*/);
        }
        else
        {
            return null;
        }
    }

    // ported from: internal/parser/jsdoc.go:794-819
    //
    // Go 的 `(name *ast.EntityName, isBracketed bool)` → `bool TryX(..., out name)`（约定）。
    private bool TryParseBracketNameInPropertyAndParamTag(PropertyLikeParse target, out Node? name)
    {
        // Looking for something like '[foo]', 'foo', '[foo.bar]' or 'foo.bar'
        var isBracketed = ParseOptionalJsdoc(Kind.OpenBracketToken);
        if (isBracketed)
        {
            SkipWhitespace();
        }

        // a markdown-quoted name: `arg` is not legal jsdoc, but occurs in the wild
        var isBackquoted = ParseOptionalJsdoc(Kind.BacktickToken);
        name = ParseJSDocEntityName(IfElse(target == PropertyLikeParse.Parameter, (Message?)null, Identifier_expected));
        if (isBackquoted)
        {
            ParseExpectedTokenJSDoc(Kind.BacktickToken);
        }

        if (isBracketed)
        {
            SkipWhitespace();
            // May have an optional default, e.g. '[foo = 42]'
            if (ParseOptionalToken(Kind.EqualsToken) is not null)
            {
                ParseExpression();
            }

            ParseExpected(Kind.CloseBracketToken);
        }

        return isBracketed;
    }

    // ported from: internal/parser/jsdoc.go:821-834
    private static bool IsObjectOrObjectArrayTypeReference(Node node)
    {
        switch (node.Kind)
        {
            case Kind.ObjectKeyword:
                return true;
            case Kind.ArrayType:
                return IsObjectOrObjectArrayTypeReference(node.AsArrayTypeNode()!.ElementType!);
            default:
                if (IsTypeReferenceNode(node))
                {
                    var @ref = node.AsTypeReferenceNode()!;
                    return TypeScript.Ast.Ast.IsIdentifier(@ref.TypeName!) && @ref.TypeName!.Text() == "Object" && @ref.TypeArguments is null;
                }

                return false;
        }
    }

    // ported from: internal/parser/jsdoc.go:836-856
    private Node ParseParameterOrPropertyTag(int start, Node tagName, PropertyLikeParse target, int indent)
    {
        var typeExpression = TryParseTypeExpression();
        var isNameFirst = typeExpression is null;
        SkipWhitespaceOrAsterisk();

        var isBracketed = TryParseBracketNameInPropertyAndParamTag(target, out var name);
        var indentText = SkipWhitespaceOrAsterisk();

        if (isNameFirst && LookAhead(static p => !p.TryParseJSDocLinkPrefix(out _)))
        {
            typeExpression = TryParseTypeExpression();
        }

        var comment = ParseTrailingTagComments(start, NodePos(), indent, indentText);

        var nestedTypeLiteral = ParseNestedTypeLiteral(typeExpression, name, target, indent);
        if (nestedTypeLiteral is not null)
        {
            typeExpression = nestedTypeLiteral;
            isNameFirst = true;
        }

        /* JSDocPropertyTag | JSDocParameterTag */
        var kind = IfElse(target == PropertyLikeParse.Property, Kind.JSDocPropertyTag, Kind.JSDocParameterTag);
        var result = _factory.NewJSDocParameterOrPropertyTag(kind, tagName, name, isBracketed, typeExpression, isNameFirst, comment);
        return FinishNode(result, start);
    }

    // ported from: internal/parser/jsdoc.go:858-881
    private Node? ParseNestedTypeLiteral(Node? typeExpression, Node? name, PropertyLikeParse target, int indent)
    {
        if (typeExpression is not null && IsObjectOrObjectArrayTypeReference(typeExpression.Type()!))
        {
            var pos = NodePos();
            List<Node>? children = null;
            while (true)
            {
                var state = Mark();
                var child = ParseChildParameterOrPropertyTag(target, indent, name);
                if (child is null)
                {
                    Rewind(state);
                    break;
                }

                switch (child.Kind)
                {
                    case Kind.JSDocParameterTag:
                    case Kind.JSDocPropertyTag:
                        children ??= [];
                        children.Add(child);
                        break;
                    case Kind.JSDocTemplateTag:
                        ParseErrorAtRange(child.TagName()!.Loc, A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                        break;
                }
            }

            if (children is not null)
            {
                var literal = FinishNode(_factory.NewJSDocTypeLiteral(children.ToArray(), typeExpression.Type()!.Kind == Kind.ArrayType), pos);
                return FinishNode(_factory.NewJSDocTypeExpression(literal), pos);
            }
        }

        return null;
    }

    // ported from: internal/parser/jsdoc.go:883-891
    private Node ParseReturnTag(List<Node>? previousTags, int start, Node tagName, int indent, string indentText)
    {
        // Go: core.Some(previousTags, ast.IsJSDocReturnTag)；previousTags 是累积中的 List，用 List.Exists 表达（零分配）
        if (previousTags is not null && previousTags.Exists(tag => IsJSDocReturnTag(tag)))
        {
            ParseErrorAt(tagName.Pos(), _scanner.TokenStart(), X_0_tag_already_specified, tagName.Text());
        }

        var typeExpression = TryParseTypeExpression();
        return FinishNode(_factory.NewJSDocReturnTag(tagName, typeExpression, ParseTrailingTagComments(start, NodePos(), indent, indentText)), start);
    }

    /// <summary>pass indent=-1 to skip parsing trailing comments (as when a type tag is nested in a typedef)</summary>
    // ported from: internal/parser/jsdoc.go:893-904
    private Node ParseTypeTag(List<Node>? previousTags, int start, Node tagName, int indent, string indentText)
    {
        // Go: core.Some(previousTags, ast.IsJSDocTypeTag)；同上
        if (previousTags is not null && previousTags.Exists(tag => IsJSDocTypeTag(tag)))
        {
            ParseErrorAt(tagName.Pos(), _scanner.TokenStart(), X_0_tag_already_specified, tagName.Text());
        }

        var typeExpression = ParseJSDocTypeExpression(true);
        NodeList? comments = null;
        if (indent != -1)
        {
            comments = ParseTrailingTagComments(start, NodePos(), indent, indentText);
        }

        return FinishNode(_factory.NewJSDocTypeTag(tagName, typeExpression, comments), start);
    }

    // ported from: internal/parser/jsdoc.go:906-915
    private Node ParseSeeTag(int start, Node tagName, int indent, string indentText)
    {
        var hasNameReference = IsIdentifier() && !_sourceText.AsSpan(_scanner.TokenEnd()).StartsWith("://") ||
            _token == Kind.OpenBraceToken && LookAhead(static p => p.NextTokenIsIdentifierOrKeyword());
        Node? nameExpression = null;
        if (hasNameReference)
        {
            nameExpression = ParseJSDocNameReference();
        }

        var comments = ParseTrailingTagComments(start, NodePos(), indent, indentText);
        return FinishNode(_factory.NewJSDocSeeTag(tagName, nameExpression, comments), start);
    }

    // ported from: internal/parser/jsdoc.go:917-920
    private Node ParseImplementsTag(int start, Node tagName, int margin, string indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(_factory.NewJSDocImplementsTag(tagName, className, ParseTrailingTagComments(start, NodePos(), margin, indentText)), start);
    }

    // ported from: internal/parser/jsdoc.go:922-925
    private Node ParseAugmentsTag(int start, Node tagName, int margin, string indentText)
    {
        var className = ParseExpressionWithTypeArgumentsForAugments();
        return FinishNode(_factory.NewJSDocAugmentsTag(tagName, className, ParseTrailingTagComments(start, NodePos(), margin, indentText)), start);
    }

    // ported from: internal/parser/jsdoc.go:927-931
    private Node ParseSatisfiesTag(int start, Node tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(false);
        var comments = ParseTrailingTagComments(start, NodePos(), margin, indentText);
        return FinishNode(_factory.NewJSDocSatisfiesTag(tagName, typeExpression, comments), start);
    }

    // ported from: internal/parser/jsdoc.go:933-937
    private Node ParseThrowsTag(int start, Node tagName, int margin, string indentText)
    {
        var typeExpression = TryParseTypeExpression();
        var comment = ParseTrailingTagComments(start, NodePos(), margin, indentText);
        return FinishNode(_factory.NewJSDocThrowsTag(tagName, typeExpression, comment), start);
    }

    // ported from: internal/parser/jsdoc.go:939-951
    private Node ParseImportTag(int start, Node tagName, int margin, string indentText)
    {
        var afterImportTagPos = _scanner.TokenFullStart();

        Node? identifier = null;
        if (IsIdentifier())
        {
            identifier = ParseIdentifier();
        }

        var importClause = TryParseImportClause(identifier, afterImportTagPos, Kind.TypeKeyword, true /*skipJSDocLeadingAsterisks*/);
        var moduleSpecifier = ParseModuleSpecifier();
        var attributes = TryParseImportAttributes();

        var comments = ParseTrailingTagComments(start, NodePos(), margin, indentText);
        return FinishNode(_factory.NewJSDocImportTag(tagName, importClause, moduleSpecifier, attributes, comments), start);
    }

    // ported from: internal/parser/jsdoc.go:953-965
    private Node ParseExpressionWithTypeArgumentsForAugments()
    {
        var usedBrace = ParseOptional(Kind.OpenBraceToken);
        var pos = NodePos();
        var expression = ParsePropertyAccessEntityNameExpression();
        _scanner.SetSkipJSDocLeadingAsterisks(true);
        var typeArguments = ParseTypeArguments();
        _scanner.SetSkipJSDocLeadingAsterisks(false);
        var node = FinishNode(_factory.NewExpressionWithTypeArguments(expression, typeArguments), pos);
        if (usedBrace)
        {
            SkipWhitespace();
            ParseExpected(Kind.CloseBraceToken);
        }

        return node;
    }

    // ported from: internal/parser/jsdoc.go:967-975
    private Node ParsePropertyAccessEntityNameExpression()
    {
        var pos = NodePos();
        var node = ParseJSDocIdentifierName(Identifier_expected);
        while (ParseOptional(Kind.DotToken))
        {
            var name = ParseJSDocIdentifierName(Identifier_expected);
            node = FinishNode(_factory.NewPropertyAccessExpression(node, null, name, NodeFlags.None), pos);
        }

        return node;
    }

    // ported from: internal/parser/jsdoc.go:977-979
    private Node ParseSimpleTag(int start, Func<Node, NodeList?, Node> createTag, Node tagName, int margin, string indentText) =>
        FinishNode(createTag(tagName, ParseTrailingTagComments(start, NodePos(), margin, indentText)), start);

    // ported from: internal/parser/jsdoc.go:981-986
    private Node ParseThisTag(int start, Node tagName, int margin, string indentText)
    {
        var typeExpression = ParseJSDocTypeExpression(true);
        SkipWhitespace();
        var result = _factory.NewJSDocThisTag(tagName, typeExpression, ParseTrailingTagComments(start, NodePos(), margin, indentText));
        return FinishNode(result, start);
    }

    // ported from: internal/parser/jsdoc.go:988-1006
    private Node? ParseJSDocTypeNameWithNamespace(bool nested)
    {
        var start = _scanner.TokenStart();
        if (!TokenIsIdentifierOrKeyword(_token))
        {
            return null;
        }

        var typeNameOrNamespaceName = ParseJSDocIdentifierName(null);
        if (ParseOptionalJsdoc(Kind.DotToken))
        {
            var body = ParseJSDocTypeNameWithNamespace(true /*nested*/);
            var jsDocNamespaceNode = _factory.NewModuleDeclaration(
                null,                     /*modifiers*/
                Kind.NamespaceKeyword,    /*keyword*/
                typeNameOrNamespaceName,
                null, /*attributes*/
                body);
            if (nested)
            {
                jsDocNamespaceNode.Flags |= NodeFlags.NestedNamespace;
            }

            return FinishNode(jsDocNamespaceNode, start);
        }

        if (nested)
        {
            typeNameOrNamespaceName.Flags |= NodeFlags.IdentifierIsInJSDocNamespace;
        }

        return typeNameOrNamespaceName;
    }

    // ported from: internal/parser/jsdoc.go:1008-1107
    private Node ParseTypedefTag(int start, Node tagName, int indent, string indentText)
    {
        var typeExpression = TryParseTypeExpression();
        SkipWhitespaceOrAsterisk();
        var fullName = ParseJSDocTypeNameWithNamespace(false /*nested*/);
        if (fullName is null)
        {
            fullName = ParseJSDocIdentifierName(Identifier_expected);
        }

        SkipWhitespace();
        var comment = ParseTagComments(indent, null);

        var end = -1;
        var hasChildren = false;
        if (typeExpression is null || IsObjectOrObjectArrayTypeReference(typeExpression.Type()!))
        {
            JSDocTypeTag? childTypeTag = null;
            List<Node>? jsdocPropertyTags = null;
            while (true)
            {
                var state = Mark();
                var child = ParseChildPropertyTag(indent);
                if (child is null)
                {
                    Rewind(state);
                    break;
                }

                hasChildren = true;
                switch (child.Kind)
                {
                    case Kind.JSDocTemplateTag:
                        ParseErrorAtRange(child.TagName()!.Loc, A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
                        break;
                    case Kind.JSDocTypeTag:
                        if (childTypeTag is null)
                        {
                            childTypeTag = child.AsJSDocTypeTag();
                        }
                        else
                        {
                            var lastError = ParseErrorAtCurrentToken(A_JSDoc_typedef_comment_may_not_contain_multiple_type_tags);
                            if (lastError is not null)
                            {
                                var related = NewDiagnostic(null, NewTextRange(0, 0), The_tag_was_first_specified_here);
                                lastError.AddRelatedInfo(related);
                            }
                        }

                        break;
                    default:
                        jsdocPropertyTags ??= [];
                        jsdocPropertyTags.Add(child);
                        break;
                }
            }

            if (hasChildren)
            {
                var isArrayType = typeExpression is not null && typeExpression.Type()!.Kind == Kind.ArrayType;
                var jsdocTypeLiteral = _factory.NewJSDocTypeLiteral(jsdocPropertyTags?.ToArray(), isArrayType);
                if (childTypeTag?.TypeExpression is not null && !IsObjectOrObjectArrayTypeReference(childTypeTag.TypeExpression.Type()!))
                {
                    typeExpression = childTypeTag.TypeExpression;
                }
                else
                {
                    // !!! This differs from Strada but prevents a crash
                    var pos = start;
                    if (jsdocPropertyTags is { Count: > 0 })
                    {
                        pos = jsdocPropertyTags[0].Pos();
                    }

                    typeExpression = FinishNode(jsdocTypeLiteral, pos);
                }

                end = typeExpression.End();
            }
        }

        // Only include the characters between the name end and the next token if a comment was actually parsed out - otherwise it's just whitespace
        if (end == -1)
        {
            if (hasChildren && typeExpression is not null)
            {
                end = typeExpression.End();
            }
            else if (comment is not null)
            {
                end = NodePos();
            }
            else if (fullName is not null)
            {
                end = fullName.End();
            }
            else if (typeExpression is not null)
            {
                end = typeExpression.End();
            }
            else
            {
                end = tagName.End();
            }
        }

        if (comment is null)
        {
            comment = ParseTrailingTagComments(start, end, indent, indentText);
        }

        var typedefTag = FinishNodeWithEnd(_factory.NewJSDocTypedefTag(tagName, typeExpression, fullName, comment), start, end);
        if (typeExpression is not null)
        {
            typeExpression.Parent = typedefTag; // forcibly overwrite parent potentially set by inner type expression parse
        }

        return typedefTag;
    }

    // ported from: internal/parser/jsdoc.go:1109-1125
    private NodeList ParseCallbackTagParameters(int indent)
    {
        var pos = NodePos();
        var parameters = new List<Node>();
        while (true)
        {
            var state = Mark();
            var child = ParseChildParameterOrPropertyTag(PropertyLikeParse.CallbackParameter, indent, null);
            if (child is null)
            {
                Rewind(state);
                break;
            }

            if (child.Kind == Kind.JSDocTemplateTag)
            {
                ParseErrorAtRange(child.TagName()!.Loc, A_JSDoc_template_tag_may_not_follow_a_typedef_callback_or_overload_tag);
            }
            else
            {
                parameters.Add(child);
            }
        }

        return NewNodeList(NewTextRange(pos, NodePos()), parameters.ToArray());
    }

    // ported from: internal/parser/jsdoc.go:1127-1140
    private Node ParseJSDocSignature(int start, int indent)
    {
        var parameters = ParseCallbackTagParameters(indent);
        Node? returnTag = null;
        var state = Mark();
        if (ParseOptionalJsdoc(Kind.AtToken))
        {
            var tag = ParseTag(null, indent);
            if (tag.Kind == Kind.JSDocReturnTag)
            {
                returnTag = tag;
            }
        }

        if (returnTag is null)
        {
            Rewind(state);
        }

        return FinishNode(_factory.NewJSDocSignature(null, parameters, returnTag), start);
    }

    // ported from: internal/parser/jsdoc.go:1142-1159
    private Node ParseCallbackTag(int start, Node tagName, int indent, string indentText)
    {
        var fullName = ParseJSDocTypeNameWithNamespace(false /*nested*/);
        if (fullName is null)
        {
            fullName = ParseJSDocIdentifierName(Identifier_expected);
        }

        SkipWhitespace();
        var comment = ParseTagComments(indent, null);
        var typeExpression = ParseJSDocSignature(NodePos(), indent);
        if (comment is null)
        {
            comment = ParseTrailingTagComments(start, NodePos(), indent, indentText);
        }

        int end;
        if (comment is not null)
        {
            end = NodePos();
        }
        else
        {
            end = typeExpression.End();
        }

        return FinishNodeWithEnd(_factory.NewJSDocCallbackTag(tagName, typeExpression, fullName, comment), start, end);
    }

    // ported from: internal/parser/jsdoc.go:1161-1175
    private Node ParseOverloadTag(int start, Node tagName, int indent, string indentText)
    {
        SkipWhitespace();
        var comment = ParseTagComments(indent, null);
        var typeExpression = ParseJSDocSignature(start, indent);
        if (comment is null)
        {
            comment = ParseTrailingTagComments(start, NodePos(), indent, indentText);
        }

        int end;
        if (comment is not null)
        {
            end = NodePos();
        }
        else
        {
            end = typeExpression.End();
        }

        return FinishNodeWithEnd(_factory.NewJSDocOverloadTag(tagName, typeExpression, comment), start, end);
    }

    // ported from: internal/parser/jsdoc.go:1177-1187
    private static bool TextsEqual(Node a, Node b)
    {
        while (!TypeScript.Ast.Ast.IsIdentifier(a) || !TypeScript.Ast.Ast.IsIdentifier(b))
        {
            if (!TypeScript.Ast.Ast.IsIdentifier(a) && !TypeScript.Ast.Ast.IsIdentifier(b) &&
                a.AsQualifiedName()!.Right!.Text() == b.AsQualifiedName()!.Right!.Text())
            {
                a = a.AsQualifiedName()!.Left!;
                b = b.AsQualifiedName()!.Left!;
            }
            else
            {
                return false;
            }
        }

        return a.Text() == b.Text();
    }

    // ported from: internal/parser/jsdoc.go:1189-1191
    private Node? ParseChildPropertyTag(int indent) =>
        ParseChildParameterOrPropertyTag(PropertyLikeParse.Property, indent, null);

    // ported from: internal/parser/jsdoc.go:1193-1225
    private Node? ParseChildParameterOrPropertyTag(PropertyLikeParse target, int indent, Node? name)
    {
        var canParseTag = true;
        var seenAsterisk = false;
        while (true)
        {
            switch (NextTokenJSDoc())
            {
                case Kind.AtToken:
                    if (canParseTag && _scanner.CanFollowJSDocAt())
                    {
                        var child = TryParseChildTag(target, indent);
                        if (child is not null && name is not null &&
                            (child.Kind == Kind.JSDocParameterTag || child.Kind == Kind.JSDocPropertyTag) &&
                            (TypeScript.Ast.Ast.IsIdentifier(child.Name()!) || !TextsEqual(name, child.Name()!.AsQualifiedName()!.Left!)))
                        {
                            return null;
                        }

                        return child;
                    }

                    seenAsterisk = false;
                    break;
                case Kind.NewLineTrivia:
                    canParseTag = true;
                    seenAsterisk = false;
                    break;
                case Kind.AsteriskToken:
                    if (seenAsterisk)
                    {
                        canParseTag = false;
                    }

                    seenAsterisk = true;
                    break;
                case Kind.Identifier:
                    canParseTag = false;
                    break;
                case Kind.EndOfFile:
                    return null;
            }
        }
    }

    // ported from: internal/parser/jsdoc.go:1227-1256
    private Node? TryParseChildTag(PropertyLikeParse target, int indent)
    {
        if (_token != Kind.AtToken)
        {
            throw new DebugFailureException("should only be called when at @");
        }

        var start = _scanner.TokenFullStart();
        NextTokenJSDoc();

        var tagName = ParseJSDocIdentifierName(Identifier_expected);
        var indentText = SkipWhitespaceOrAsterisk();
        var t = (PropertyLikeParse)0;
        switch (tagName.Text())
        {
            case "type":
                if (target == PropertyLikeParse.Property)
                {
                    return ParseTypeTag(null, start, tagName, -1, "");
                }

                break;
            case "prop":
            case "property":
                t = PropertyLikeParse.Property;
                break;
            case "arg":
            case "argument":
            case "param":
                t = PropertyLikeParse.Parameter | PropertyLikeParse.CallbackParameter;
                break;
            case "template":
                return ParseTemplateTag(start, tagName, indent, indentText);
            case "this":
                return ParseThisTag(start, tagName, indent, indentText);
            default:
                return null;
        }

        if ((target & t) == 0)
        {
            return null;
        }

        return ParseParameterOrPropertyTag(start, tagName, target, indent);
    }

    // ported from: internal/parser/jsdoc.go:1258-1289
    private Node? ParseTemplateTagTypeParameter()
    {
        var typeParameterPos = NodePos();
        var isBracketed = ParseOptionalJsdoc(Kind.OpenBracketToken);
        if (isBracketed)
        {
            SkipWhitespace();
        }

        var modifiers = ParseModifiersEx(false, true /*permitConstAsModifier*/, false);
        var name = ParseJSDocIdentifierName(Unexpected_token_A_type_parameter_name_was_expected_without_curly_braces);
        Node? defaultType = null;
        if (isBracketed)
        {
            SkipWhitespace();
            ParseExpected(Kind.EqualsToken);
            var saveContextFlags = _contextFlags;
            SetContextFlags(NodeFlags.JSDoc, true);
            defaultType = ParseJSDocType();
            _contextFlags = saveContextFlags;
            ParseExpected(Kind.CloseBracketToken);
        }

        if (NodeIsMissing(name))
        {
            return null;
        }

        return FinishNode(_factory.NewTypeParameterDeclaration(modifiers, name, null /*constraint*/, null /*expression*/, defaultType), typeParameterPos);
    }

    // ported from: internal/parser/jsdoc.go:1291-1301
    private NodeList ParseTemplateTagTypeParameters()
    {
        var typeParameters = new NodeList();
        for (var ok = true; ok; ok = ParseOptionalJsdoc(Kind.CommaToken)) // do-while loop
        {
            SkipWhitespace();
            var node = ParseTemplateTagTypeParameter();
            if (node is not null)
            {
                typeParameters.Nodes = [.. typeParameters.Nodes ?? [], node];
            }

            SkipWhitespaceOrAsterisk();
        }

        return typeParameters;
    }

    // ported from: internal/parser/jsdoc.go:1303-1325
    private Node ParseTemplateTag(int start, Node tagName, int indent, string indentText)
    {
        // The template tag looks like one of the following:
        //   @template T,U,V
        //   @template {Constraint} T
        //
        // According to the [closure docs](https://github.com/google/closure-compiler/wiki/Generic-Types#multiple-bounded-template-types):
        //   > Multiple bounded generics cannot be declared on the same line. For the sake of clarity, if multiple templates share the same
        //   > type bound they must be declared on separate lines.
        //
        // Go 注释：TODO: Determine whether we should enforce this in the checker.
        // Go 注释：TODO: Consider moving the `constraint` to the first type parameter as we could then remove `getEffectiveConstraintOfTypeParameter`.
        // Go 注释：TODO: Consider only parsing a single type parameter if there is a constraint.
        Node? constraint = null;
        if (_token == Kind.OpenBraceToken)
        {
            constraint = ParseJSDocTypeExpression(false);
        }

        var typeParameters = ParseTemplateTagTypeParameters();
        var result = _factory.NewJSDocTemplateTag(tagName, constraint, typeParameters, ParseTrailingTagComments(start, NodePos(), indent, indentText));
        return FinishNode(result, start);
    }

    // ported from: internal/parser/jsdoc.go:1327-1333
    private bool ParseOptionalJsdoc(Kind t)
    {
        if (_token == t)
        {
            NextTokenJSDoc();
            return true;
        }

        return false;
    }

    // ported from: internal/parser/jsdoc.go:1335-1350
    private Node ParseJSDocEntityName(Message? diagnosticMessage)
    {
        var entity = ParseJSDocIdentifierName(diagnosticMessage);
        if (ParseOptional(Kind.OpenBracketToken))
        {
            ParseExpected(Kind.CloseBracketToken);
            // Note that y[] is accepted as an entity name, but the postfix brackets are not saved for checking.
            // Technically usejsdoc.org requires them for specifying a property of a type equivalent to Array<{ x: ...}>
            // but it's not worth it to enforce that restriction.
        }

        while (ParseOptional(Kind.DotToken))
        {
            var name = ParseJSDocIdentifierName(Identifier_expected);
            if (ParseOptional(Kind.OpenBracketToken))
            {
                ParseExpected(Kind.CloseBracketToken);
            }

            var pos = entity.Pos();
            entity = FinishNode(_factory.NewQualifiedName(entity, name), pos);
        }

        return entity;
    }

    // ported from: internal/parser/jsdoc.go:1352-1366
    private Node ParseJSDocIdentifierName(Message? diagnosticMessage)
    {
        if (!TokenIsIdentifierOrKeyword(_token))
        {
            if (diagnosticMessage is not null)
            {
                ParseErrorAtCurrentToken(diagnosticMessage);
            }
            else if (IsReservedWord(_token))
            {
                ParseErrorAtCurrentToken(Identifier_expected_0_is_a_reserved_word_that_cannot_be_used_here, _scanner.TokenText());
            }

            return FinishNode(NewIdentifier(""), NodePos());
        }

        var pos = _scanner.TokenStart();
        var end = _scanner.TokenEnd();
        var text = _scanner.TokenValue();
        NextTokenJSDoc();
        return FinishNodeWithEnd(NewIdentifier(text), pos, end);
    }
}

/// <summary>Go: <c>type jsdocState int32</c>（jsdoc.go:37-43）。</summary>
internal enum JSDocState : int
{
    BeginningOfLine = 0,
    SawAsterisk = 1,
    SavingComments = 2,
    SavingBackticks = 3,
}

/// <summary>Go: <c>type propertyLikeParse int32</c>（位标志；jsdoc.go:45-50）。</summary>
[Flags]
internal enum PropertyLikeParse : int
{
    Property = 1 << 0,
    Parameter = 1 << 1,
    CallbackParameter = 1 << 2,
}
