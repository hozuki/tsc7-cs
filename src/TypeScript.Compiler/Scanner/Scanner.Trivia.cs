// ported from: internal/scanner/scanner.go:2284-2524（couldStartTrivia / SkipTriviaOptions / SkipTrivia / SkipTriviaEx /
//               冲突标记 / shebang / GetScannerForSourceFile / ScanTokenAtPosition / GetRangeOfTokenAtPosition）
//             + internal/scanner/scanner.go:2525-2654（GetTokenPosOfNode / getErrorRangeForArrowFunction /
//               findOriginatingJSDocSatisfiesTag / GetErrorRangeForNode）
//             + internal/scanner/scanner.go:2655-2798（ComputeLineOfPosition / GetECMALine* / GetECMAPositionOfLine* /
//               ComputePositionOfLineAnd*）
//             + internal/scanner/scanner.go:2799-2917（GetLeadingCommentRanges / GetTrailingCommentRanges / iterateCommentRanges）
//
// 与 Go 的偏离（逐条）：
// - `SkipTriviaOptions` 在 Go 里是结构体、按 `*SkipTriviaOptions` 传递（nil 表示默认值）→ C# class（引用类型，
//   null 语义一致）。
// - `utf8.DecodeRuneInString` / `utf8.DecodeLastRuneInString` → `DecodeRuneAt` / `DecodeLastRuneAt`（UTF-16 代码单元下标）。
// - **行映射里的"字节偏移"退化成代码单元偏移**：Go 的 `GetECMALineAndByteOffsetOfPosition` 返回 UTF-8 字节偏移、
//   `GetECMALineAndUTF16CharacterOfPosition` 返回 UTF-16 偏移（要用 core.UTF16Len 换算）。C# 的位置本来就是
//   UTF-16 代码单元下标，两者都等于 `pos - lineStarts[line]`（docs §3.1 的同一条理由）。函数照 Go 保留，语义一致。
// - `panic(fmt.Sprintf(...))` → `Debug.Fail($"…")`；`debug.Assert` → `Debug.Assert`。
// - `iter.Seq[ast.CommentRange]` → C# `IEnumerable<CommentRange>`（`yield return`）；Go 的 `break scan`（带标签的
//   break，从 switch 里跳出循环）在 C# 里用局部标志 `done` + `break`（从 switch 出到循环条件）表达。
// - `f.NewCommentRange(kind, pos, end, hasTrailingNewLine)`：**Ast 侧尚未移植 NodeFactory.NewCommentRange**
//   （缺失清单见交付报告），这里就地构造等价的 CommentRange（字段与 Go 的工厂逐字段一致）。
// - `node.JSDoc(sourceFile)`/`EagerJSDoc` 在 C# 里可能返回 null（Go 的 nil 切片）；`len(...) > 0` 写成
//   `is { Length: > 0 }`。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using TypeScript.Diagnostics;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

/// <summary>Go: <c>type SkipTriviaOptions struct</c>（三个可选开关；C# 用 class 表达 Go 的指针 + nil 默认值）。</summary>
public sealed class SkipTriviaOptions
{
    public bool StopAfterLineBreak;
    public bool StopAtComments;
    public bool InJSDoc;
}

public sealed partial class Scanner
{
    private const int _mergeConflictMarkerLength = 7; // len("<<<<<<<")
    private const int _maxAsciiCharacter = 127;

    // Go 里"按字节"的比较（`rune(text[i])` 取到的其实是 UTF-8 的一个字节）：只有 LF/CR 会命中。
    private static bool IsLineBreakByte(char ch) => ch == '\n' || ch == '\r';

    internal static bool CouldStartTrivia(string text, int pos)
    {
        // Keep in sync with skipTrivia
        var ch = text[pos];
        switch (ch)
        {
            // Characters that could start normal trivia
            case '\r':
            case '\n':
            case '\t':
            case '\v':
            case '\f':
            case ' ':
            case '/':
            // Characters that could start conflict marker trivia
            case '<':
            case '|':
            case '=':
            case '>':
                return true;
            case '#':
                // Only if its the beginning can we have #! trivia
                return pos == 0;
            default:
                return ch > _maxAsciiCharacter;
        }
    }

    public static int SkipTrivia(string text, int pos) => SkipTriviaEx(text, pos, null);

    public static int SkipTriviaEx(string text, int pos, SkipTriviaOptions? options)
    {
        if (PositionIsSynthesized(pos))
        {
            return pos;
        }

        options ??= new SkipTriviaOptions();

        var textLen = text.Length;
        var canConsumeStar = false;

        // Keep in sync with couldStartTrivia
        while (true)
        {
            if (pos >= textLen)
            {
                return pos;
            }

            var ch = DecodeRuneAt(text, pos, out var size);
            switch (ch)
            {
                case '\r':
                    if (pos + 1 < textLen && text[pos + 1] == '\n')
                    {
                        pos++;
                    }

                    goto case '\n';
                case '\n':
                    pos++;
                    if (options.StopAfterLineBreak)
                    {
                        return pos;
                    }

                    canConsumeStar = options.InJSDoc;
                    continue;
                case '\t':
                case '\v':
                case '\f':
                case ' ':
                    pos++;
                    continue;
                case '/':
                    if (options.StopAtComments)
                    {
                        break;
                    }

                    if (pos + 1 < textLen)
                    {
                        if (text[pos + 1] == '/')
                        {
                            pos += 2;
                            while (pos < textLen)
                            {
                                var c = DecodeRuneAt(text, pos, out var s);
                                if (IsLineBreak(c))
                                {
                                    break;
                                }

                                pos += s;
                            }

                            canConsumeStar = false;
                            continue;
                        }

                        if (text[pos + 1] == '*')
                        {
                            pos += 2;
                            while (pos < textLen)
                            {
                                if (text[pos] == '*' && pos + 1 < textLen && text[pos + 1] == '/')
                                {
                                    pos += 2;
                                    break;
                                }

                                _ = DecodeRuneAt(text, pos, out var s);
                                pos += s;
                            }

                            canConsumeStar = false;
                            continue;
                        }
                    }

                    break;
                case '<':
                case '|':
                case '=':
                case '>':
                    if (IsConflictMarkerTrivia(text, pos))
                    {
                        pos = ScanConflictMarkerTrivia(text, pos, null);
                        canConsumeStar = false;
                        continue;
                    }

                    break;
                case '#':
                    if (pos == 0 && IsShebangTrivia(text, pos))
                    {
                        pos = ScanShebangTrivia(text, pos);
                        canConsumeStar = false;
                        continue;
                    }

                    break;
                case '*':
                    if (canConsumeStar)
                    {
                        pos++;
                        canConsumeStar = false;
                        continue;
                    }

                    break;
                default:
                    if (ch > _maxAsciiCharacter && IsWhiteSpaceLike(ch))
                    {
                        pos += size;
                        continue;
                    }

                    break;
            }

            return pos;
        }
    }

    /// <summary>
    /// All conflict markers consist of the same character repeated seven times. If it is
    /// a &lt;&lt;&lt;&lt;&lt;&lt;&lt; or &gt;&gt;&gt;&gt;&gt;&gt;&gt; marker then it is also followed by a space.
    /// </summary>
    internal static bool IsConflictMarkerTrivia(string text, int pos)
    {
        if (pos < 0)
        {
            Debug.Fail("pos < 0");
        }

        // Fast reject: a conflict marker is the same code unit repeated seven times. If the
        // second code unit differs (the overwhelmingly common case for `<`, `>`, `=`, `|`
        // tokens), it cannot be a marker, so skip the line-start check entirely.
        if (pos + 1 >= text.Length || text[pos + 1] != text[pos])
        {
            return false;
        }

        // Conflict markers must be at the start of a line.
        // Go: `stringutil.IsLineBreak(rune(text[pos-1]))` —— 这里的 text[pos-1] 在 Go 里是**字节**，
        // 所以只有 0x0A/0x0D 算换行（U+2028/U+2029 是多字节字符，Go 侧看的是它的尾字节）。
        // C# 的 char 直接是代码单元，用 rune 版的 IsLineBreak 会把 LS/PS 也算进来 —— 为了与 Go 逐字对应，
        // 这里显式只认 LF/CR（下面的 fallback 分支照 Go 用 DecodeLastRuneInString，那个是 rune 级的）。
        var atLineStart = pos == 0 || IsLineBreakByte(text[pos - 1]);
        if (!atLineStart && pos >= 2)
        {
            var (prev, _) = DecodeLastRuneAt(text, pos - 2);
            atLineStart = IsLineBreak(prev);
        }

        if (atLineStart)
        {
            var ch = text[pos];

            if (pos + _mergeConflictMarkerLength < text.Length)
            {
                for (var i = 0; i < _mergeConflictMarkerLength; i++)
                {
                    if (text[pos + i] != ch)
                    {
                        return false;
                    }
                }

                return ch == '=' || text[pos + _mergeConflictMarkerLength] == ' ';
            }
        }

        return false;
    }

    internal static int ScanConflictMarkerTrivia(string text, int pos, ErrorCallback? reportError)
    {
        reportError?.Invoke(Merge_conflict_marker_encountered, pos, _mergeConflictMarkerLength);

        var ch = DecodeRuneAt(text, pos, out var size);
        var length = text.Length;

        if (ch == '<' || ch == '>')
        {
            while (pos < length && !IsLineBreak(ch))
            {
                pos += size;
                ch = DecodeRuneAt(text, pos, out size);
            }
        }
        else
        {
            if (ch != '|' && ch != '=')
            {
                Debug.Fail("Assertion failed: ch must be either '|' or '='");
            }

            // Consume everything from the start of a ||||||| or ======= marker to the start
            // of the next ======= or >>>>>>> marker.
            while (pos < length)
            {
                var currentChar = text[pos];
                if ((currentChar == '=' || currentChar == '>') && currentChar != ch && IsConflictMarkerTrivia(text, pos))
                {
                    break;
                }

                pos++;
            }
        }

        return pos;
    }

    internal static bool IsShebangTrivia(string text, int pos)
    {
        if (text.Length < 2)
        {
            return false;
        }

        if (pos != 0)
        {
            Debug.Fail("Shebangs check must only be done at the start of the file");
        }

        return text[0] == '#' && text[1] == '!';
    }

    internal static int ScanShebangTrivia(string text, int pos)
    {
        pos += 2;
        while (pos < text.Length)
        {
            var ch = DecodeRuneAt(text, pos, out var size);
            if (IsLineBreak(ch))
            {
                break;
            }

            pos += size;
        }

        return pos;
    }

    public static string GetShebang(string text)
    {
        if (!IsShebangTrivia(text, 0))
        {
            return "";
        }

        var end = ScanShebangTrivia(text, 0);
        return text[..end];
    }

    public static Scanner GetScannerForSourceFile(SourceFile sourceFile, int pos)
    {
        var s = NewScanner();
        s._text = sourceFile.Text();
        s._pos = pos;
        s._end = s._text.Length;
        s._languageVariant = sourceFile.LanguageVariant;
        s.Scan();
        return s;
    }

    public static Kind ScanTokenAtPosition(SourceFile sourceFile, int pos)
    {
        var s = GetScannerForSourceFile(sourceFile, pos);
        return s._token;
    }

    public static TextRange GetRangeOfTokenAtPosition(SourceFile sourceFile, int pos)
    {
        var s = GetScannerForSourceFile(sourceFile, pos);
        return new TextRange(s._tokenStart, s._pos);
    }

    public static int GetTokenPosOfNode(Node node, SourceFile sourceFile, bool includeJSDoc)
    {
        // With nodes that have no width (i.e. 'Missing' nodes), we actually *don't*
        // want to skip trivia because this will launch us forward to the next token.
        if (NodeIsMissing(node))
        {
            return node.Pos();
        }

        if (IsJSDocNode(node) || node.Kind == Kind.JsxText)
        {
            // JsxText cannot actually contain comments, even though the scanner will think it sees comments
            return SkipTriviaEx(sourceFile.Text(), node.Pos(), new SkipTriviaOptions { StopAtComments = true });
        }

        var jsDoc = node.JSDoc(sourceFile);
        if (includeJSDoc && jsDoc is { Length: > 0 })
        {
            return GetTokenPosOfNode(jsDoc[0], sourceFile, false /*includeJSDoc*/);
        }

        return SkipTriviaEx(sourceFile.Text(), node.Pos(), new SkipTriviaOptions { InJSDoc = (node.Flags & NodeFlags.JSDoc) != 0 });
    }

    private static TextRange GetErrorRangeForArrowFunction(SourceFile sourceFile, Node node)
    {
        var pos = SkipTrivia(sourceFile.Text(), node.Pos());
        var body = node.Body();
        if (body is not null && body.Kind == Kind.Block)
        {
            var startLine = GetECMALineOfPosition(sourceFile, body.Pos());
            var endLine = GetECMALineOfPosition(sourceFile, body.End());
            if (startLine < endLine)
            {
                // The arrow function spans multiple lines, make the error span be the first line, inclusive.
                return new TextRange(pos, GetECMAEndLinePosition(sourceFile, startLine) + 1);
            }
        }

        return new TextRange(pos, node.End());
    }

    private static Node? FindOriginatingJSDocSatisfiesTag(SourceFile sourceFile, Node node)
    {
        var targetType = node.AsSatisfiesExpression()!.Type!;
        if ((targetType.Flags & NodeFlags.Reparsed) == 0)
        {
            return null;
        }

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if ((current.Flags & NodeFlags.HasJSDoc) == 0)
            {
                continue;
            }

            Node? firstSatisfiesTag = null;
            var jsDocs = current.EagerJSDoc(sourceFile);
            if (jsDocs is not null)
            {
                foreach (var jsDoc in jsDocs)
                {
                    var tags = jsDoc.AsJSDoc()!.Tags;
                    if (tags?.Nodes is null)
                    {
                        continue;
                    }

                    foreach (var tag in tags.Nodes)
                    {
                        if (!IsJSDocSatisfiesTag(tag))
                        {
                            continue;
                        }

                        firstSatisfiesTag ??= tag;
                        var typeExpr = tag.AsJSDocSatisfiesTag()!.TypeExpression;
                        if (typeExpr is not null)
                        {
                            var t = typeExpr.Type();
                            if (t is not null && t.Loc.Equals(targetType.Loc))
                            {
                                return tag;
                            }
                        }
                    }
                }
            }

            return firstSatisfiesTag;
        }

        return null;
    }

    public static TextRange GetErrorRangeForNode(SourceFile sourceFile, Node node)
    {
        var errorNode = node;
        switch (node.Kind)
        {
            case Kind.SourceFile:
                {
                    var pos = SkipTrivia(sourceFile.Text(), 0);
                    if (pos == sourceFile.Text().Length)
                    {
                        return new TextRange(0, 0);
                    }

                    return GetRangeOfTokenAtPosition(sourceFile, pos);
                }

            // This list is a work in progress. Add missing node kinds to improve their error spans
            case Kind.FunctionDeclaration:
            case Kind.MethodDeclaration:
                if ((node.Flags & NodeFlags.Reparsed) != 0)
                {
                    errorNode = node;
                    break;
                }

                goto case Kind.VariableDeclaration;
            case Kind.VariableDeclaration:
            case Kind.BindingElement:
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.ModuleDeclaration:
            case Kind.EnumDeclaration:
            case Kind.EnumMember:
            case Kind.FunctionExpression:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.NamespaceImport:
                errorNode = GetNameOfDeclaration(node);
                break;
            case Kind.ClassExpression:
                errorNode = node.Name();
                break;

            case Kind.ArrowFunction:
                return GetErrorRangeForArrowFunction(sourceFile, node);
            case Kind.CaseClause:
            case Kind.DefaultClause:
                {
                    var start = SkipTrivia(sourceFile.Text(), node.Pos());
                    var end = node.End();
                    var statements = node.Statements();
                    if (statements is { Length: > 0 })
                    {
                        end = statements[0].Pos();
                    }

                    return new TextRange(start, end);
                }

            case Kind.ReturnStatement:
            case Kind.YieldExpression:
                {
                    var pos = SkipTrivia(sourceFile.Text(), node.Pos());
                    return GetRangeOfTokenAtPosition(sourceFile, pos);
                }

            case Kind.SatisfiesExpression:
                {
                    var jsDocSatisfiesTag = FindOriginatingJSDocSatisfiesTag(sourceFile, node);
                    if (jsDocSatisfiesTag is not null)
                    {
                        var tagPos = SkipTrivia(sourceFile.Text(), jsDocSatisfiesTag.TagName()!.Pos());
                        return GetRangeOfTokenAtPosition(sourceFile, tagPos);
                    }

                    var exprPos = SkipTrivia(sourceFile.Text(), node.AsSatisfiesExpression()!.Expression!.End());
                    return GetRangeOfTokenAtPosition(sourceFile, exprPos);
                }

            case Kind.Constructor:
                if ((node.Flags & NodeFlags.Reparsed) != 0)
                {
                    errorNode = node;
                    break;
                }

                {
                    var scanner = GetScannerForSourceFile(sourceFile, node.Pos());
                    var start = scanner.TokenStart();
                    while (scanner.Token() != Kind.ConstructorKeyword && scanner.Token() != Kind.StringLiteral && scanner.Token() != Kind.EndOfFile)
                    {
                        scanner.Scan();
                    }

                    return new TextRange(start, scanner.TokenEnd());
                }
        }

        if (errorNode is null)
        {
            // If we don't have a better node, then just set the error on the first token of
            // construct.
            return GetRangeOfTokenAtPosition(sourceFile, node.Pos());
        }

        var errorNodePos = errorNode.Pos();
        if (!NodeIsMissing(errorNode) && !IsJsxText(errorNode))
        {
            errorNodePos = SkipTrivia(sourceFile.Text(), errorNodePos);
        }

        return new TextRange(errorNodePos, errorNode.End());
    }

    public static int ComputeLineOfPosition(TextPos[] lineStarts, int pos)
    {
        var low = 0;
        var high = lineStarts.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var value = (int)lineStarts[middle];
            if (value < pos)
            {
                low = middle + 1;
            }
            else if (value > pos)
            {
                high = middle - 1;
            }
            else
            {
                return middle;
            }
        }

        return low - 1;
    }

    public static TextPos[] GetECMALineStarts(SourceFileLike sourceFile) => sourceFile.ECMALineMap();

    public static int GetECMALineOfPosition(SourceFileLike sourceFile, int pos)
    {
        var lineMap = GetECMALineStarts(sourceFile);
        return ComputeLineOfPosition(lineMap, pos);
    }

    // GetECMALineAndUTF16CharacterOfPosition returns the 0-based line number and the
    // UTF-16 code unit offset from the start of that line for the given position.
    // Uses ECMAScript line separators (LF, CR, CRLF, LS, PS).
    public static (int Line, UTF16Offset Character) GetECMALineAndUTF16CharacterOfPosition(SourceFileLike sourceFile, int pos)
    {
        var lineMap = GetECMALineStarts(sourceFile);
        var line = ComputeLineOfPosition(lineMap, pos);
        // Go: core.UTF16Len(sourceFile.Text()[lineMap[line]:pos]) —— C# 的位置本来就是 UTF-16 代码单元下标，
        // 所以 UTF16Len 退化成"两个代码单元下标之差"（文件头第 3 条）。
        var character = new UTF16Offset(pos - (int)lineMap[line]);
        return (line, character);
    }

    // GetECMALineAndByteOffsetOfPosition returns the 0-based line number and the
    // offset from the start of that line for the given position.
    // Uses ECMAScript line separators (LF, CR, CRLF, LS, PS).
    // NOTE: Go 这里返回的是 UTF-8 字节偏移；C# 的位置是 UTF-16 代码单元下标，两者在 C# 侧同一个值（文件头第 3 条）。
    public static (int Line, int ByteOffset) GetECMALineAndByteOffsetOfPosition(SourceFileLike sourceFile, int pos)
    {
        var lineMap = GetECMALineStarts(sourceFile);
        var line = ComputeLineOfPosition(lineMap, pos);
        return (line, pos - (int)lineMap[line]);
    }

    public static int GetECMAEndLinePosition(SourceFile sourceFile, int line)
    {
        var pos = (int)GetECMALineStarts(sourceFile)[line];
        while (true)
        {
            var ch = DecodeRuneAt(sourceFile.Text(), pos, out var size);
            if (size == 0 || IsLineBreak(ch))
            {
                return pos - 1;
            }

            pos += size;
        }
    }

    // GetECMAPositionOfLineAndUTF16Character converts a 0-based line number and UTF-16
    // code unit character offset back to an absolute position in the source text.
    // Uses ECMAScript line separators.
    public static int GetECMAPositionOfLineAndUTF16Character(SourceFileLike sourceFile, int line, UTF16Offset character)
    {
        var lineStarts = GetECMALineStarts(sourceFile);
        return ComputePositionOfLineAndUTF16Character(lineStarts, line, character, sourceFile.Text(), false);
    }

    // GetECMAPositionOfLineAndByteOffset converts a 0-based line number and offset
    // from line start back to an absolute position in the source text.
    // Uses ECMAScript line separators.
    public static int GetECMAPositionOfLineAndByteOffset(SourceFileLike sourceFile, int line, int byteOffset)
    {
        return ComputePositionOfLineAndByteOffset(GetECMALineStarts(sourceFile), line, byteOffset);
    }

    // ComputePositionOfLineAndByteOffset computes a position from a line and
    // offset from the line start. This is a simple addition with validation.
    public static int ComputePositionOfLineAndByteOffset(TextPos[] lineStarts, int line, int byteOffset)
    {
        if (line < 0 || line >= lineStarts.Length)
        {
            Debug.Fail($"Bad line number. Line: {line}, lineStarts.length: {lineStarts.Length}.");
        }

        return (int)lineStarts[line] + byteOffset;
    }

    // ComputePositionOfLineAndUTF16Character converts a line and UTF-16 character offset
    // back to a position. The character parameter is measured in UTF-16 code units.
    // When allowEdits is true, out-of-range values are clamped instead of panicking.
    public static int ComputePositionOfLineAndUTF16Character(TextPos[] lineStarts, int line, UTF16Offset character, string text, bool allowEdits)
    {
        if (line < 0 || line >= lineStarts.Length)
        {
            if (allowEdits)
            {
                // Clamp line to nearest allowable value
                if (line < 0)
                {
                    line = 0;
                }
                else if (line >= lineStarts.Length)
                {
                    line = lineStarts.Length - 1;
                }
            }
            else
            {
                Debug.Fail($"Bad line number. Line: {line}, lineStarts.length: {lineStarts.Length}.");
            }
        }

        var lineStart = (int)lineStarts[line];
        var characterCount = (int)character;

        if (characterCount > 0)
        {
            // UTF-16 character offset: scan from line start counting UTF-16 code units.
            var lineEnd = text.Length;
            if (line + 1 < lineStarts.Length)
            {
                lineEnd = (int)lineStarts[line + 1];
            }

            var utf16Count = 0;
            var pos = lineStart;
            while (pos < lineEnd)
            {
                if (utf16Count >= characterCount)
                {
                    break;
                }

                _ = DecodeRuneAt(text, pos, out var size);
                // Go: utf16Count += utf16.RuneLen(r) —— C# 里一个码点消耗的代码单元数就是它的 UTF-16 长度。
                utf16Count += size;
                pos += size;
            }

            if (!allowEdits)
            {
                if (pos == lineEnd && utf16Count < characterCount)
                {
                    Debug.Fail($"Bad UTF-16 character offset. Line: {line}, character: {characterCount}.");
                }

                Debug.Assert(pos <= text.Length);
                return pos;
            }

            if (pos > text.Length)
            {
                return text.Length;
            }

            return pos;
        }

        // Character is 0: line start position.
        var res = lineStart;

        if (allowEdits)
        {
            if (res > text.Length)
            {
                return text.Length;
            }

            return res;
        }

        Debug.Assert(res <= text.Length); // Allow single character overflow for trailing newline
        return res;
    }

    public static IEnumerable<CommentRange> GetLeadingCommentRanges(NodeFactory f, string text, int pos)
    {
        return IterateCommentRanges(f, text, pos, false);
    }

    public static IEnumerable<CommentRange> GetTrailingCommentRanges(NodeFactory f, string text, int pos)
    {
        return IterateCommentRanges(f, text, pos, true);
    }

    /*
    Returns an iterator over each comment range following the provided position.
    Single-line comment ranges include the leading double-slash characters but not the ending
    line break. Multi-line comment ranges include the leading slash-asterisk and trailing
    asterisk-slash characters.
    */
    private static IEnumerable<CommentRange> IterateCommentRanges(NodeFactory f, string text, int pos, bool trailing)
    {
        var pendingPos = 0;
        var pendingEnd = 0;
        var pendingKind = Kind.Unknown;
        var pendingHasTrailingNewLine = false;
        var hasPendingCommentRange = false;
        var collecting = trailing;
        if (pos == 0)
        {
            collecting = true;
            if (IsShebangTrivia(text, pos))
            {
                pos = ScanShebangTrivia(text, pos);
            }
        }

        var done = false;
        while (!done && pos >= 0 && pos < text.Length)
        {
            var ch = DecodeRuneAt(text, pos, out var size);
            switch (ch)
            {
                case '\r':
                    if (pos + 1 < text.Length && text[pos + 1] == '\n')
                    {
                        pos++;
                    }

                    goto case '\n';
                case '\n':
                    pos++;
                    if (trailing)
                    {
                        done = true;
                        break;
                    }

                    collecting = true;
                    if (hasPendingCommentRange)
                    {
                        pendingHasTrailingNewLine = true;
                    }

                    continue;
                case '\t':
                case '\v':
                case '\f':
                case ' ':
                    pos++;
                    continue;
                case '/':
                    var nextChar = '\0';
                    if (pos + 1 < text.Length)
                    {
                        nextChar = text[pos + 1];
                    }

                    var hasTrailingNewLine = false;
                    if (nextChar == '/' || nextChar == '*')
                    {
                        Kind kind;
                        if (nextChar == '/')
                        {
                            kind = Kind.SingleLineCommentTrivia;
                        }
                        else
                        {
                            kind = Kind.MultiLineCommentTrivia;
                        }

                        var startPos = pos;
                        pos += 2;
                        if (nextChar == '/')
                        {
                            while (pos < text.Length)
                            {
                                var c = DecodeRuneAt(text, pos, out var s);
                                if (IsLineBreak(c))
                                {
                                    hasTrailingNewLine = true;
                                    break;
                                }

                                pos += s;
                            }
                        }
                        else
                        {
                            var i = text.IndexOf("*/", pos, StringComparison.Ordinal);
                            if (i >= 0)
                            {
                                pos = i + 2;
                            }
                            else
                            {
                                pos = text.Length;
                            }
                        }

                        if (collecting)
                        {
                            if (hasPendingCommentRange)
                            {
                                yield return NewCommentRange(f, pendingKind, pendingPos, pendingEnd, pendingHasTrailingNewLine);
                            }

                            pendingPos = startPos;
                            pendingEnd = pos;
                            pendingKind = kind;
                            pendingHasTrailingNewLine = hasTrailingNewLine;
                            hasPendingCommentRange = true;
                        }

                        continue;
                    }

                    done = true;
                    break;
                default:
                    if (ch > _maxAsciiCharacter && IsWhiteSpaceLike(ch))
                    {
                        if (hasPendingCommentRange && IsLineBreak(ch))
                        {
                            pendingHasTrailingNewLine = true;
                        }

                        pos += size;
                        continue;
                    }

                    done = true;
                    break;
            }
        }

        if (hasPendingCommentRange)
        {
            yield return NewCommentRange(f, pendingKind, pendingPos, pendingEnd, pendingHasTrailingNewLine);
        }
    }

    // Go: (*ast.NodeFactory).NewCommentRange —— **Ast 侧尚未移植该工厂**（见文件头与交付报告的缺失清单）。
    // 这里就地构造与 Go 工厂逐字段等价的 CommentRange。
    private static CommentRange NewCommentRange(NodeFactory f, Kind kind, int pos, int end, bool hasTrailingNewLine)
    {
        _ = f; // Go 的工厂挂在 NodeFactory 上（将来的 ast 里若补上 NewCommentRange，这里应改成 f.NewCommentRange(...)）。
        return new CommentRange
        {
            Range = new TextRange(pos, end),
            Kind = kind,
            HasTrailingNewLine = hasTrailingNewLine,
        };
    }
}
