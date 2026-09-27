// ported from: internal/printer/utilities.go
//
// 偏离 Go 的地方：
// 1) Go 的 `*ast.SourceFile` 参数（可为 nil）→ C# `SourceFile?`（docs §3.5）。函数内部按 Go 直接解引用
//    （Go 传 nil 会 panic，C# 会 NRE，语义一致）。
// 2) Go 的 `interface{ End() int }`（结构化接口）→ C# 没有结构类型：`TryGetEnd(object?, out int)` 用类型
//    模式匹配复刻 Go 的 type switch（`*Node`/`*NodeList`（含 `*ModifierList`）/`*TextRange` 指针为 nil → false，
//    `core.TextRange` 值恒有效，未知类型 → panic）。`greatestEnd` 的变参 `...interface{ End() int }` →
//    `params object?[]`（与 Go 的接口变参同为装箱语义；AOT 安全，无反射），另加 `GreatestEnd(int, TextRange)`
//    重载避免最常见实参的装箱。
// 3) `getLiteralTextFlags` → internal enum `GetLiteralTextFlags`；`QuoteChar` → public enum（Go 的导出函数
//    `EscapeString` 的签名里出现它，C# 的 public 方法不能有 internal 参数类型）。
// 4) `escapeStringWorker` 里的 `stringutil.DecodeJSStringRune`（Go 的"孤立代理项哨兵"解码）在 C# 里不存在
//    （docs §3.1）：C# 的 string 本身就是 UTF-16，孤立代理项是合法值，所以逐 `char`/码点扫描。
// 5) `IsRecognizedTripleSlashComment`：Go 把注释文本切片（零拷贝）后交给 `match*` 系列；C# 的 `match*` 私有
//    助手接收 `ReadOnlySpan<char>`（docs §3.8），主函数做一次 `AsSpan(...)` 视图，不 Substring。
// 6) `makeIdentifierFromModuleName`：Go 逐**字节**扫描（非 ASCII 字节每个都算一个分隔符 → 每个 UTF-8 字节产出一个
//    `_`）；C# 逐代码单元扫描并显式按"该码点的 UTF-8 字节数"补 `_`，与 Go 的输出逐字符一致。
// 7) `newLineCharacterCache` → internal `LineCharacterCache` + `NewLineCharacterCache(Source)`；`core.UTF16Len(x)`
//    在 C# 里退化成"代码单元数"（docs §3.1），所以内部计数直接是下标差，不再调 UTF16Len。
// 8) Go 的 `panic(...)` → `Debug.Fail(...)`（docs §3.2）。
using System.Globalization;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.SourceMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Printer;

/// <summary>Go: <c>type getLiteralTextFlags int</c>。</summary>
[Flags]
internal enum GetLiteralTextFlags
{
    None = 0,
    NeverAsciiEscape = 1 << 0,
    JsxAttributeEscape = 1 << 1,
    TerminateUnterminatedLiterals = 1 << 2,
    AllowNumericSeparator = 1 << 3,
}

/// <summary>Go: <c>type QuoteChar rune</c>（数值 = Go 的 rune 常量值）。</summary>
public enum QuoteChar
{
    SingleQuote = '\'' /* 39 */,
    DoubleQuote = '"' /* 34 */,
    Backtick = '`' /* 96 */,
}

public sealed partial class Printer
{
    private static readonly Dictionary<int, string> _jsxEscapedCharsMap = new()
    {
        ['"'] = "&quot;",
        ['\''] = "&apos;",
    };

    private static readonly Dictionary<int, string> _escapedCharsMap = new()
    {
        ['\t'] = "\\t",
        ['\v'] = "\\v",
        ['\f'] = "\\f",
        ['\b'] = "\\b",
        ['\r'] = "\\r",
        ['\n'] = "\\n",
        ['\\'] = "\\\\",
        ['"'] = "\\\"",
        ['\''] = "\\'",
        ['`'] = "\\`",
        ['$'] = "\\$",           // when quoteChar == '`'
        ['\u2028'] = "\\u2028",  // lineSeparator
        ['\u2029'] = "\\u2029",  // paragraphSeparator
        ['\u0085'] = "\\u0085",  // nextLine
    };

    private static void EncodeJsxCharacterEntity(StringBuilder b, int charCode)
    {
        var hexCharCode = charCode.ToString("X", CultureInfo.InvariantCulture);
        b.Append("&#x");
        b.Append(hexCharCode);
        b.Append(';');
    }

    private static void EncodeUtf16EscapeSequence(StringBuilder b, int charCode)
    {
        var hexCharCode = charCode.ToString("X", CultureInfo.InvariantCulture);
        b.Append("\\u");
        for (var i = hexCharCode.Length; i < 4; i++)
        {
            b.Append('0');
        }
        b.Append(hexCharCode);
    }

    /// <summary>Go: <c>strings.Builder.Grow(n)</c>（预留 n 个字符的容量）。</summary>
    private static void Grow(StringBuilder b, int n) => b.EnsureCapacity(b.Length + n);

    /// <summary>
    /// Based heavily on the abstract 'Quote'/'QuoteJSONString' operation from ECMA-262 (24.3.2.2),
    /// but augmented for a few select characters (e.g. lineSeparator, paragraphSeparator, nextLine)
    /// Note that this doesn't actually wrap the input in double quotes.
    /// </summary>
    private static void EscapeStringWorker(string s, QuoteChar quoteChar, GetLiteralTextFlags flags, StringBuilder b)
    {
        var pos = 0;
        var i = 0;
        while (i < s.Length)
        {
            // Go: stringutil.DecodeJSStringRune(s[i:])（C# 的 string 就是 UTF-16，见文件头第 4 条）。
            // Go 里 `ch == utf8.RuneError && size == 1`（残留的哨兵字节片段）那一支在 C# 不存在：C# 的 string
            // 只可能有合法的 UTF-16 代码单元（孤立代理项是合法值，由下面的 0xD800-0xDFFF 分支处理）。
            var size = 1;
            int ch = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                ch = char.ConvertToUtf32(s[i], s[i + 1]);
                size = 2;
            }

            var escape = false;
            if (ch >= 0xD800 && ch <= 0xDFFF)
            {
                escape = true;
            }

            // This consists of the first 19 unprintable ASCII characters, canonical escapes, lineSeparator,
            // paragraphSeparator, and nextLine. The latter three are just desirable to suppress new lines in
            // the language service. These characters should be escaped when printing, and if any characters are added,
            // `escapedCharsMap` and/or `jsx_escapedCharsMap` must be updated. Note that this *does not* include the
            // 'delete' character. There is no reason for this other than that JSON.stringify does not handle it either.
            // （Go 的 `switch ch` 逐 case 判定 → C# 的 if/else 链；顺序与 Go 的 case 值一一对应，互斥。）
            if (ch == (int)quoteChar || ch == '\u2028' || ch == '\u2029' || ch == '\u0085' || ch == '\r')
            {
                escape = true;
            }
            else if (ch == '\n')
            {
                if (quoteChar != QuoteChar.Backtick)
                {
                    // Template strings preserve simple LF newlines, still encode CRLF (or CR).
                    escape = true;
                }
            }
            else if (ch == '\\')
            {
                if ((flags & GetLiteralTextFlags.JsxAttributeEscape) == 0)
                {
                    escape = true;
                }
            }
            else if (ch == '$')
            {
                if (quoteChar == QuoteChar.Backtick && i + 1 < s.Length && s[i + 1] == '{')
                {
                    escape = true;
                }
            }
            else if (ch <= '\u001f' || ((flags & GetLiteralTextFlags.NeverAsciiEscape) == 0 && ch > '\u007f'))
            {
                escape = true;
            }

            if (escape)
            {
                if (pos < i)
                {
                    // Write string up to this point
                    b.Append(s, pos, i - pos);
                }

                if ((flags & GetLiteralTextFlags.JsxAttributeEscape) != 0)
                {
                    if (ch == 0)
                    {
                        b.Append("&#0;");
                    }
                    else if (_jsxEscapedCharsMap.TryGetValue(ch, out var match))
                    {
                        b.Append(match);
                    }
                    else
                    {
                        EncodeJsxCharacterEntity(b, ch);
                    }
                }
                else
                {
                    if (ch == '\r' && quoteChar == QuoteChar.Backtick && i + 1 < s.Length && s[i + 1] == '\n')
                    {
                        // Template strings preserve simple LF newlines, but still must escape CRLF. Left alone, the
                        // above cases for `\r` and `\n` would inadvertently escape CRLF as two independent characters.
                        size++;
                        b.Append("\\r\\n");
                    }
                    else if (ch > 0xffff)
                    {
                        // encode as surrogate pair
                        ch -= 0x10000;
                        EncodeUtf16EscapeSequence(b, ((ch & 0b11111111110000000000) >> 10) + 0xD800);
                        EncodeUtf16EscapeSequence(b, (ch & 0b00000000001111111111) + 0xDC00);
                    }
                    else if (ch >= 0xD800 && ch <= 0xDFFF)
                    {
                        EncodeUtf16EscapeSequence(b, ch);
                    }
                    else if (ch == 0)
                    {
                        if (i + 1 < s.Length && IsDigit(s[i + 1]))
                        {
                            // If the null character is followed by digits, print as a hex escape to prevent the result
                            // from parsing as an octal (which is forbidden in strict mode)
                            b.Append("\\x00");
                        }
                        else
                        {
                            // Otherwise, keep printing a literal \0 for the null character
                            b.Append("\\0");
                        }
                    }
                    else if (_escapedCharsMap.TryGetValue(ch, out var match))
                    {
                        b.Append(match);
                    }
                    else
                    {
                        EncodeUtf16EscapeSequence(b, ch);
                    }
                }
                pos = i + size;
            }

            i += size;
        }

        if (pos < i)
        {
            b.Append(s, pos, i - pos);
        }
    }

    public static string EscapeString(string s, QuoteChar quoteChar)
    {
        var b = new StringBuilder();
        Grow(b, s.Length + 2);
        EscapeStringWorker(s, quoteChar, GetLiteralTextFlags.NeverAsciiEscape, b);
        return b.ToString();
    }

    internal static string EscapeNonAsciiString(string s, QuoteChar quoteChar)
    {
        var b = new StringBuilder();
        Grow(b, s.Length + 2);
        EscapeStringWorker(s, quoteChar, GetLiteralTextFlags.None, b);
        return b.ToString();
    }

    internal static string EscapeJsxAttributeString(string s, QuoteChar quoteChar)
    {
        var b = new StringBuilder();
        Grow(b, s.Length + 2);
        EscapeStringWorker(s, quoteChar, GetLiteralTextFlags.JsxAttributeEscape | GetLiteralTextFlags.NeverAsciiEscape, b);
        return b.ToString();
    }

    private static bool CanUseOriginalText(Node node, GetLiteralTextFlags flags)
    {
        // A synthetic node has no original text, nor does a node without a parent as we would be unable to find the
        // containing SourceFile. We also cannot use the original text if the literal was unterminated and the caller
        // has requested proper termination of unterminated literals
        if (NodeIsSynthesized(node) || node.Parent is null || ((flags & GetLiteralTextFlags.TerminateUnterminatedLiterals) != 0 && IsUnterminatedLiteral(node)))
        {
            return false;
        }

        if (node.Kind == Kind.NumericLiteral)
        {
            var tokenFlags = node.AsNumericLiteral()!.TokenFlags;
            // For a numeric literal, we cannot use the original text if the original text was an invalid literal
            if ((tokenFlags & TokenFlags.IsInvalid) != 0)
            {
                return false;
            }
            // We also cannot use the original text if the literal contains numeric separators, but numeric separators
            // are not permitted
            if ((tokenFlags & TokenFlags.ContainsSeparator) != 0)
            {
                return (flags & GetLiteralTextFlags.AllowNumericSeparator) != 0;
            }
        }

        // Finally, we do not use the original text of a BigInt literal
        // TODO(rbuckton): The reason as to why we do not use the original text for bigints is not mentioned in the
        // original compiler source. It could be that this is no longer necessary, in which case bigint literals should
        // use the same code path as numeric literals, above
        return node.Kind != Kind.BigIntLiteral;
    }

    internal static string GetLiteralText(Node node, SourceFile? sourceFile, GetLiteralTextFlags flags)
    {
        // If we don't need to downlevel and we can reach the original source text using
        // the node's parent reference, then simply get the text as it was originally written.
        if (sourceFile is not null && CanUseOriginalText(node, flags))
        {
            return GetSourceTextOfNodeFromSourceFile(sourceFile, node, false /*includeTrivia*/);
        }

        // If we can't reach the original source text, use the canonical form if it's a number,
        // or a (possibly escaped) quoted form of the original text if it's string-like.
        switch (node.Kind)
        {
            case Kind.StringLiteral:
                {
                    var b = new StringBuilder();
                    QuoteChar quoteChar;
                    if ((node.AsStringLiteral()!.TokenFlags & TokenFlags.SingleQuote) != 0)
                    {
                        quoteChar = QuoteChar.SingleQuote;
                    }
                    else
                    {
                        quoteChar = QuoteChar.DoubleQuote;
                    }

                    var text = node.Text();

                    // Write leading quote character
                    Grow(b, text.Length + 2);
                    b.Append((char)quoteChar);

                    // Write text
                    EscapeStringWorker(text, quoteChar, flags, b);

                    // Write trailing quote character
                    b.Append((char)quoteChar);
                    return b.ToString();
                }

            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateHead:
            case Kind.TemplateMiddle:
            case Kind.TemplateTail:
                {
                    // If a NoSubstitutionTemplateLiteral appears to have a substitution in it, the original text
                    // had to include a backslash: `not \${a} substitution`.
                    var b = new StringBuilder();
                    var text = node.Text();
                    var rawText = node.TemplateLiteralLikeData()!.RawText;
                    var raw = rawText.Length > 0 || text.Length == 0;

                    int textLen;
                    if (raw)
                    {
                        textLen = rawText.Length;
                    }
                    else
                    {
                        textLen = text.Length;
                    }

                    // Write leading quote character
                    switch (node.Kind)
                    {
                        case Kind.NoSubstitutionTemplateLiteral:
                            Grow(b, 2 + textLen);
                            b.Append('`');
                            break;
                        case Kind.TemplateHead:
                            Grow(b, 3 + textLen);
                            b.Append('`');
                            break;
                        case Kind.TemplateMiddle:
                            Grow(b, 3 + textLen);
                            b.Append('}');
                            break;
                        case Kind.TemplateTail:
                            Grow(b, 2 + textLen);
                            b.Append('}');
                            break;
                    }

                    // Write text
                    if (rawText.Length > 0 || text.Length == 0)
                    {
                        // If rawText is set, it is expected to be valid.
                        b.Append(rawText);
                    }
                    else
                    {
                        EscapeStringWorker(text, QuoteChar.Backtick, flags, b);
                    }

                    // Write trailing quote character
                    switch (node.Kind)
                    {
                        case Kind.NoSubstitutionTemplateLiteral:
                            b.Append('`');
                            break;
                        case Kind.TemplateHead:
                            b.Append("${");
                            break;
                        case Kind.TemplateMiddle:
                            b.Append("${");
                            break;
                        case Kind.TemplateTail:
                            b.Append('`');
                            break;
                    }
                    return b.ToString();
                }

            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
                return node.Text();

            case Kind.RegularExpressionLiteral:
                if ((flags & GetLiteralTextFlags.TerminateUnterminatedLiterals) != 0 && IsUnterminatedLiteral(node))
                {
                    var b = new StringBuilder();
                    var text = node.Text();
                    if (text.Length > 0 && text[^1] == '\\')
                    {
                        Grow(b, 2 + text.Length);
                        b.Append(text);
                        b.Append(" /");
                    }
                    else
                    {
                        Grow(b, 1 + text.Length);
                        b.Append(text);
                        b.Append('/');
                    }
                    return b.ToString();
                }
                return node.Text();

            default:
                Debug.Fail("Unsupported LiteralLikeNode");
                return "";
        }
    }

    internal static bool IsNotPrologueDirective(Node node)
    {
        return !IsPrologueDirective(node);
    }

    public static bool RangeIsOnSingleLine(TextRange r, SourceFile? sourceFile)
    {
        return RangeStartIsOnSameLineAsRangeEnd(r, r, sourceFile);
    }

    public static bool RangeStartPositionsAreOnSameLine(TextRange range1, TextRange range2, SourceFile? sourceFile)
    {
        return PositionsAreOnSameLine(
            GetStartPositionOfRange(range1, sourceFile, false /*includeComments*/),
            GetStartPositionOfRange(range2, sourceFile, false /*includeComments*/),
            sourceFile);
    }

    internal static bool RangeEndPositionsAreOnSameLine(TextRange range1, TextRange range2, SourceFile? sourceFile)
    {
        return PositionsAreOnSameLine(range1.End(), range2.End(), sourceFile);
    }

    internal static bool RangeStartIsOnSameLineAsRangeEnd(TextRange range1, TextRange range2, SourceFile? sourceFile)
    {
        return PositionsAreOnSameLine(GetStartPositionOfRange(range1, sourceFile, false /*includeComments*/), range2.End(), sourceFile);
    }

    internal static bool RangeEndIsOnSameLineAsRangeStart(TextRange range1, TextRange range2, SourceFile? sourceFile)
    {
        return PositionsAreOnSameLine(range1.End(), GetStartPositionOfRange(range2, sourceFile, false /*includeComments*/), sourceFile);
    }

    private static int GetStartPositionOfRange(TextRange r, SourceFile? sourceFile, bool includeComments)
    {
        if (PositionIsSynthesized(r.Pos()))
        {
            return -1;
        }
        return SkipTriviaEx(sourceFile!.Text(), r.Pos(), new TypeScript.Scanner.SkipTriviaOptions { StopAtComments = includeComments });
    }

    public static bool PositionsAreOnSameLine(int pos1, int pos2, SourceFile? sourceFile)
    {
        return GetLinesBetweenPositions(sourceFile, pos1, pos2) == 0;
    }

    public static int GetLinesBetweenPositions(SourceFile? sourceFile, int pos1, int pos2)
    {
        if (pos1 == pos2)
        {
            return 0;
        }
        var lineStarts = GetECMALineStarts(sourceFile!);
        var lower = IfElse(pos1 < pos2, pos1, pos2);
        var isNegative = lower == pos2;
        var upper = IfElse(isNegative, pos1, pos2);
        var lowerLine = ComputeLineOfPosition(lineStarts, lower);
        var upperLine = lowerLine + ComputeLineOfPosition(lineStarts[lowerLine..], upper);
        if (isNegative)
        {
            return lowerLine - upperLine;
        }
        else
        {
            return upperLine - lowerLine;
        }
    }

    internal static int GetLinesBetweenRangeEndAndRangeStart(TextRange range1, TextRange range2, SourceFile? sourceFile, bool includeSecondRangeComments)
    {
        var range2Start = GetStartPositionOfRange(range2, sourceFile, includeSecondRangeComments);
        return GetLinesBetweenPositions(sourceFile, range1.End(), range2Start);
    }

    internal static int GetLinesBetweenPositionAndPrecedingNonWhitespaceCharacter(int pos, int stopPos, SourceFile? sourceFile, bool includeComments)
    {
        var startPos = SkipTriviaEx(sourceFile!.Text(), pos, new TypeScript.Scanner.SkipTriviaOptions { StopAtComments = includeComments });
        var prevPos = GetPreviousNonWhitespacePosition(startPos, stopPos, sourceFile);
        return GetLinesBetweenPositions(sourceFile, IfElse(prevPos >= 0, prevPos, stopPos), startPos);
    }

    internal static int GetLinesBetweenPositionAndNextNonWhitespaceCharacter(int pos, int stopPos, SourceFile? sourceFile, bool includeComments)
    {
        var nextPos = SkipTriviaEx(sourceFile!.Text(), pos, new TypeScript.Scanner.SkipTriviaOptions { StopAtComments = includeComments });
        return GetLinesBetweenPositions(sourceFile, pos, IfElse(stopPos < nextPos, stopPos, nextPos));
    }

    private static int GetPreviousNonWhitespacePosition(int pos, int stopPos, SourceFile? sourceFile)
    {
        var text = sourceFile!.Text();
        for (; pos >= stopPos; pos--)
        {
            if (!IsWhiteSpaceLike(text[pos]))
            {
                return pos;
            }
        }
        return -1;
    }

    internal static bool SiblingNodePositionsAreComparable(EmitContext emitContext, Node previousNode, Node nextNode)
    {
        if (nextNode.Pos() < previousNode.End())
        {
            return false;
        }

        previousNode = emitContext.MostOriginal(previousNode)!;
        nextNode = emitContext.MostOriginal(nextNode)!;
        var parent = previousNode.Parent;
        if (parent is null || !ReferenceEquals(parent, nextNode.Parent))
        {
            return false;
        }

        var parentNodeArray = GetContainingNodeArray(previousNode);
        if (parentNodeArray is not null)
        {
            var prevNodeIndex = Array.IndexOf(parentNodeArray.Nodes ?? [], previousNode);
            return prevNodeIndex >= 0 && Array.IndexOf(parentNodeArray.Nodes ?? [], nextNode) == prevNodeIndex + 1;
        }

        return false;
    }

    private static NodeList? GetContainingNodeArray(Node node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return null;
        }

        switch (node.Kind)
        {
            case Kind.TypeParameter:
                // Go: case ast.IsFunctionLike(parent) || ast.IsClassLike(parent) || ast.IsInterfaceDeclaration(parent) ||
                //     ast.IsTypeOrJSTypeAliasDeclaration(parent)
                if (IsFunctionLike(parent) || IsClassLike(parent) || IsInterfaceDeclaration(parent) || IsTypeOrJSTypeAliasDeclaration(parent))
                {
                    return parent.TypeParameterList();
                }
                if (IsInferTypeNode(parent))
                {
                    // infer type nodes have no associated type parameter list
                    return null;
                }
                // Go: default: panic(fmt.Sprintf("Unexpected TypeParameter parent: %#v", parent.Kind))
                Debug.Fail($"Unexpected TypeParameter parent: {parent.Kind}");
                return null;

            case Kind.Parameter:
                return node.Parent!.FunctionLikeData()!.Parameters;

            case Kind.TemplateLiteralTypeSpan:
                return node.Parent!.AsTemplateLiteralTypeNode()!.TemplateSpans;

            case Kind.TemplateSpan:
                return node.Parent!.AsTemplateExpression()!.TemplateSpans;

            case Kind.Decorator:
                if (CanHaveDecorators(node.Parent!))
                {
                    var modifiers = node.Parent!.Modifiers();
                    if (modifiers is not null)
                    {
                        return modifiers;
                    }
                }
                return null;

            case Kind.HeritageClause:
                if (IsClassLike(node.Parent!))
                {
                    return node.Parent!.ClassLikeData()!.HeritageClauses;
                }
                else
                {
                    return node.Parent!.AsInterfaceDeclaration()!.HeritageClauses;
                }
        }

        // TODO(rbuckton)
        // if ast.IsJSDocTag(node) {
        //     if ast.IsJSDocTypeLiteral(node.parent) {
        //         return nil
        //     }
        //     return node.parent.tags
        // }

        switch (parent.Kind)
        {
            case Kind.TypeLiteral:
            case Kind.InterfaceDeclaration:
                if (IsTypeElement(node))
                {
                    return parent.MemberList();
                }
                break;
            case Kind.UnionType:
                return parent.AsUnionTypeNode()!.Types;
            case Kind.IntersectionType:
                return parent.AsIntersectionTypeNode()!.Types;
            case Kind.ArrayLiteralExpression:
            case Kind.TupleType:
            case Kind.NamedImports:
            case Kind.NamedExports:
                return parent.ElementList();
            case Kind.ObjectLiteralExpression:
            case Kind.JsxAttributes:
                return parent.PropertyList();
            case Kind.CallExpression:
                {
                    var p = parent.AsCallExpression()!;
                    if (IsTypeNode(node))
                    {
                        return p.TypeArguments;
                    }
                    else if (!ReferenceEquals(node, p.Expression))
                    {
                        return p.Arguments;
                    }
                    break;
                }
            case Kind.NewExpression:
                {
                    var p = parent.AsNewExpression()!;
                    if (IsTypeNode(node))
                    {
                        return p.TypeArguments;
                    }
                    else if (!ReferenceEquals(node, p.Expression))
                    {
                        return p.Arguments;
                    }
                    break;
                }
            case Kind.JsxElement:
            case Kind.JsxFragment:
                if (IsJsxChild(node))
                {
                    return parent.Children();
                }
                break;
            case Kind.JsxOpeningElement:
            case Kind.JsxSelfClosingElement:
                if (IsTypeNode(node))
                {
                    return parent.TypeArgumentList();
                }
                break;
            case Kind.Block:
            case Kind.ModuleBlock:
            case Kind.CaseClause:
            case Kind.DefaultClause:
                return parent.StatementList();
            case Kind.CaseBlock:
                return parent.AsCaseBlock()!.Clauses;
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                if (IsClassElement(node))
                {
                    return parent.MemberList();
                }
                break;
            case Kind.EnumDeclaration:
                if (IsEnumMember(node))
                {
                    return parent.MemberList();
                }
                break;
            case Kind.SourceFile:
                if (IsStatement(node))
                {
                    return parent.StatementList();
                }
                break;
        }

        if (IsModifier(node))
        {
            var modifiers = parent.Modifiers();
            if (modifiers is not null)
            {
                return modifiers;
            }
        }

        return null;
    }

    private static bool CanHaveDecorators(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Parameter:
            case Kind.PropertyDeclaration:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.ClassExpression:
            case Kind.ClassDeclaration:
                return true;
        }
        return false;
    }

    internal static bool OriginalNodesHaveSameParent(EmitContext emitContext, Node nodeA, Node nodeB)
    {
        nodeA = emitContext.MostOriginal(nodeA)!;
        if (nodeA.Parent is not null)
        {
            // For performance, do not call `MostOriginal` for `nodeB` if `nodeA` doesn't even
            // have a parent node.
            nodeB = emitContext.MostOriginal(nodeB)!;
            return ReferenceEquals(nodeA.Parent, nodeB.Parent);
        }
        return false;
    }

    /// <summary>
    /// Go: <c>func tryGetEnd(node interface{ End() int }) (int, bool)</c>：结构化接口 → 显式类型模式匹配
    /// （文件头第 2 条）。Go 里 `*ast.ModifierList` 分支由 `*ast.NodeList` 分支覆盖（C# 里 ModifierList 派生自 NodeList）。
    /// </summary>
    internal static bool TryGetEnd(object? node, out int end)
    {
        // avoid using reflect (via core.IsNil) for common cases
        switch (node)
        {
            case null:
                end = 0;
                return false;
            case Node n:
                end = n.End();
                return true;
            case NodeList l:
                end = l.End();
                return true;
            case TextRange r:
                end = r.End();
                return true;
            default:
                Debug.Fail($"unhandled type: {node.GetType()}");
                end = 0;
                return false;
        }
    }

    internal static int GreatestEnd(int end, params object?[] nodes)
    {
        // Go: slices.Backward(nodes)
        for (var i = nodes.Length - 1; i >= 0; i--)
        {
            if (TryGetEnd(nodes[i], out var nodeEnd) && end < nodeEnd)
            {
                end = nodeEnd;
            }
        }
        return end;
    }

    /// <summary>Go: <c>greatestEnd(end, range)</c> 的 TextRange 重载（避免最常见实参的装箱）。</summary>
    internal static int GreatestEnd(int end, TextRange range)
    {
        var nodeEnd = range.End();
        return end < nodeEnd ? nodeEnd : end;
    }

    internal static Node SkipSynthesizedParentheses(Node node)
    {
        while (node.Kind == Kind.ParenthesizedExpression && NodeIsSynthesized(node))
        {
            node = node.Expression()!;
        }
        return node;
    }

    internal static bool IsNewExpressionWithoutArguments(Node node)
    {
        return node.Kind == Kind.NewExpression && node.ArgumentList() is null;
    }

    internal static bool IsBinaryOperation(Node node, Kind token)
    {
        node = SkipPartiallyEmittedExpressions(node)!;
        return node.Kind == Kind.BinaryExpression &&
            node.AsBinaryExpression()!.OperatorToken!.Kind == token;
    }

    internal static bool MixingBinaryOperatorsRequiresParentheses(Kind a, Kind b)
    {
        if (a == Kind.QuestionQuestionToken)
        {
            return b == Kind.AmpersandAmpersandToken || b == Kind.BarBarToken;
        }
        if (b == Kind.QuestionQuestionToken)
        {
            return a == Kind.AmpersandAmpersandToken || a == Kind.BarBarToken;
        }
        return false;
    }

    internal static bool IsImmediatelyInvokedFunctionExpressionOrArrowFunction(Node node)
    {
        node = SkipPartiallyEmittedExpressions(node)!;
        if (!IsCallExpression(node))
        {
            return false;
        }
        node = SkipPartiallyEmittedExpressions(node.Expression())!;
        return IsFunctionExpression(node) || IsArrowFunction(node);
    }

    internal static bool HasLeadingHash(string text)
    {
        return text.Length > 0 && text[0] == '#';
    }

    internal static string RemoveLeadingHash(string text)
    {
        if (HasLeadingHash(text))
        {
            return text[1..];
        }
        else
        {
            return text;
        }
    }

    internal static string EnsureLeadingHash(string text)
    {
        if (HasLeadingHash(text))
        {
            return text;
        }
        else
        {
            return "#" + text;
        }
    }

    public static string FormatGeneratedName(bool privateName, string prefix, string @base, string suffix)
    {
        var name = RemoveLeadingHash(prefix) + RemoveLeadingHash(@base) + RemoveLeadingHash(suffix);
        if (privateName)
        {
            return EnsureLeadingHash(name);
        }
        return name;
    }

    private static bool IsASCIIWordCharacter(int ch)
    {
        return IsASCIILetter(ch) || IsDigit(ch) || ch == '_';
    }

    /// <summary>该码点在 UTF-8 下的字节数（Go 逐字节扫描的等价展开，见文件头第 6 条）。</summary>
    private static int Utf8ByteLength(string text, int pos)
    {
        var ch = text[pos];
        if (ch < 0x80)
        {
            return 1;
        }
        if (char.IsHighSurrogate(ch) && pos + 1 < text.Length && char.IsLowSurrogate(text[pos + 1]))
        {
            return 4;
        }
        if (ch < 0x800)
        {
            return 2;
        }
        return 3;
    }

    internal static string MakeIdentifierFromModuleName(string moduleName)
    {
        moduleName = GetBaseFileName(moduleName);
        var builder = new StringBuilder();
        var start = 0;
        var pos = 0;
        while (pos < moduleName.Length)
        {
            var ch = moduleName[pos];
            var isAscii = ch < 0x80;
            if (pos == 0 && isAscii && IsDigit(ch))
            {
                builder.Append('_');
            }
            else if (!(isAscii && IsASCIIWordCharacter(ch)))
            {
                if (start < pos)
                {
                    builder.Append(moduleName, start, pos - start);
                }
                // Go 按字节扫描：一个多字节码点的每个字节都会产出一个 '_'
                var byteLength = Utf8ByteLength(moduleName, pos);
                for (var i = 0; i < byteLength; i++)
                {
                    builder.Append('_');
                }
                start = pos + byteLength;
                pos += byteLength;
                continue;
            }
            pos++;
        }
        if (start < pos)
        {
            builder.Append(moduleName, start, pos - start);
        }
        return builder.ToString();
    }

    internal static int FindSpanEndWithEmitContext<T>(EmitContext c, T[] array, Func<EmitContext, T, bool> test, int start)
    {
        var i = start;
        while (i < array.Length && test(c, array[i]))
        {
            i++;
        }
        return i;
    }

    internal static int FindSpanEnd<T>(T[] array, Func<T, bool> test, int start)
    {
        var i = start;
        while (i < array.Length && test(array[i]))
        {
            i++;
        }
        return i;
    }

    private static void SkipWhiteSpaceSingleLine(ReadOnlySpan<char> text, ref int pos)
    {
        while (pos < text.Length)
        {
            var ch = DecodeRune(text, pos, out var size);
            if (!IsWhiteSpaceSingleLine(ch))
            {
                break;
            }
            pos += size;
        }
    }

    /// <summary>Go: <c>utf8.DecodeRuneInString</c>（C# 的 string 是 UTF-16，见 docs §3.1）。</summary>
    private static int DecodeRune(ReadOnlySpan<char> text, int pos, out int size)
    {
        if ((uint)pos >= (uint)text.Length)
        {
            // Go: utf8.DecodeRuneInString("") == (utf8.RuneError, 0)（越界/空串不抛异常）
            size = 0;
            return 0xFFFD;
        }
        var ch = text[pos];
        if (char.IsHighSurrogate(ch) && pos + 1 < text.Length && char.IsLowSurrogate(text[pos + 1]))
        {
            size = 2;
            return char.ConvertToUtf32(ch, text[pos + 1]);
        }
        size = 1;
        return ch;
    }

    private static bool MatchWhiteSpaceSingleLine(ReadOnlySpan<char> text, ref int pos)
    {
        var startPos = pos;
        SkipWhiteSpaceSingleLine(text, ref pos);
        return pos != startPos;
    }

    private static bool MatchRune(ReadOnlySpan<char> text, ref int pos, int expected)
    {
        var ch = DecodeRune(text, pos, out var size);
        if (ch == expected)
        {
            pos += size;
            return true;
        }
        return false;
    }

    private static bool MatchString(ReadOnlySpan<char> text, ref int pos, ReadOnlySpan<char> expected)
    {
        var textPos = pos;
        var expectedPos = 0;
        while (expectedPos < expected.Length)
        {
            if (textPos >= text.Length)
            {
                return false;
            }

            var expectedRune = DecodeRune(expected, expectedPos, out var expectedSize);
            if (!MatchRune(text, ref textPos, expectedRune))
            {
                return false;
            }

            expectedPos += expectedSize;
        }

        pos = textPos;
        return true;
    }

    private static bool MatchQuotedString(ReadOnlySpan<char> text, ref int pos)
    {
        var textPos = pos;
        int quoteChar;
        if (MatchRune(text, ref textPos, '\''))
        {
            quoteChar = '\'';
        }
        else if (MatchRune(text, ref textPos, '"'))
        {
            quoteChar = '"';
        }
        else
        {
            return false;
        }
        while (textPos < text.Length)
        {
            var ch = DecodeRune(text, textPos, out var size);
            textPos += size;
            if (ch == quoteChar)
            {
                pos = textPos;
                return true;
            }
        }
        return false;
    }

    // /// <reference path="..." />
    // /// <reference types="..." />
    // /// <reference lib="..." />
    // /// <reference no-default-lib="..." />
    // /// <amd-dependency path="..." />
    // /// <amd-module />
    public static bool IsRecognizedTripleSlashComment(string text, CommentRange commentRange)
    {
        if (commentRange.Kind == Kind.SingleLineCommentTrivia &&
            commentRange.Range.Len() > 2 &&
            text[commentRange.Range.Pos() + 1] == '/' &&
            text[commentRange.Range.Pos() + 2] == '/')
        {
            // Go: text = text[commentRange.Pos()+3 : commentRange.End()] —— 零拷贝切片 → C# 用 span 视图
            var span = text.AsSpan(commentRange.Range.Pos() + 3, commentRange.Range.End() - (commentRange.Range.Pos() + 3));
            var pos = 0;
            SkipWhiteSpaceSingleLine(span, ref pos);
            if (!MatchRune(span, ref pos, '<'))
            {
                return false;
            }
            if (MatchString(span, ref pos, "reference"))
            {
                if (!MatchWhiteSpaceSingleLine(span, ref pos))
                {
                    return false;
                }
                if (!MatchString(span, ref pos, "path") &&
                    !MatchString(span, ref pos, "types") &&
                    !MatchString(span, ref pos, "lib") &&
                    !MatchString(span, ref pos, "no-default-lib"))
                {
                    return false;
                }
                SkipWhiteSpaceSingleLine(span, ref pos);
                if (!MatchRune(span, ref pos, '='))
                {
                    return false;
                }
                SkipWhiteSpaceSingleLine(span, ref pos);
                if (!MatchQuotedString(span, ref pos))
                {
                    return false;
                }
            }
            else if (MatchString(span, ref pos, "amd-dependency"))
            {
                if (!MatchWhiteSpaceSingleLine(span, ref pos))
                {
                    return false;
                }
                if (!MatchString(span, ref pos, "path"))
                {
                    return false;
                }
                SkipWhiteSpaceSingleLine(span, ref pos);
                if (!MatchRune(span, ref pos, '='))
                {
                    return false;
                }
                SkipWhiteSpaceSingleLine(span, ref pos);
                if (!MatchQuotedString(span, ref pos))
                {
                    return false;
                }
            }
            else if (MatchString(span, ref pos, "amd-module"))
            {
                SkipWhiteSpaceSingleLine(span, ref pos);
            }
            else
            {
                return false;
            }
            var index = span[pos..].IndexOf("/>");
            return index != -1;
        }

        return false;
    }

    internal static bool IsJSDocLikeText(string text, CommentRange comment)
    {
        return comment.Kind == Kind.MultiLineCommentTrivia &&
            comment.Range.Len() >= 5 &&
            text[comment.Range.Pos() + 2] == '*' &&
            text[comment.Range.Pos() + 3] != '/';
    }

    public static bool IsPinnedComment(string text, CommentRange comment)
    {
        return comment.Kind == Kind.MultiLineCommentTrivia &&
            comment.Range.Len() > 5 &&
            text[comment.Range.Pos() + 2] == '!';
    }

    internal static int CalculateIndent(string text, int pos, int end)
    {
        var currentLineIndent = 0;
        var indentSize = GetDefaultIndentSize();
        while (pos < end)
        {
            var ch = DecodeRune(text, pos, out var size);
            if (!IsWhiteSpaceSingleLine(ch))
            {
                break;
            }
            if (ch == '\t')
            {
                // Tabs = TabSize = indent size and go to next tabStop
                currentLineIndent += indentSize - (currentLineIndent % indentSize);
            }
            else
            {
                // Single space
                currentLineIndent++;
            }
            pos += size;
        }

        return currentLineIndent;
    }

    internal static LineCharacterCache NewLineCharacterCache(Source source)
    {
        return new LineCharacterCache(source.ECMALineMap(), source.Text());
    }
}

/// <summary>
/// lineCharacterCache provides cached line/character lookups for a source file,
/// optimized for monotonically increasing positions (e.g., during source map emit).
///
/// When positions increase within the same line, only the delta between the last
/// position and the new position needs to be scanned for UTF-16 code unit counts,
/// turning what would be O(n²) into O(n) for long lines.
///
/// Character offsets are measured in UTF-16 code units per the source map specification.
/// </summary>
internal sealed class LineCharacterCache
{
    private readonly TextPos[] _lineMap;
    private readonly string _text;
    private int _cachedLine;
    private int _cachedPos;
    private UTF16Offset _cachedChar;
    private bool _hasCached;

    public LineCharacterCache(TextPos[] lineMap, string text)
    {
        _lineMap = lineMap;
        _text = text;
    }

    /// <summary>
    /// getLineAndCharacter returns the 0-based line number and UTF-16 code unit
    /// offset from the start of that line for the given byte position.
    /// </summary>
    public (int Line, UTF16Offset Character) GetLineAndCharacter(int pos)
    {
        var line = ComputeLineOfPosition(_lineMap, pos);
        var lineStart = (int)_lineMap[line];
        // When pos is beyond the source text (e.g., for error-recovery tokens like
        // missing closing braces), we can't slice past the text end. Compute the
        // UTF-16 length up to EOF and add the remaining byte offset arithmetically,
        // matching TypeScript's computeLineAndCharacterOfPosition which uses
        // arithmetic (position - lineStarts[lineNumber]) and handles this implicitly.
        var endPos = Math.Min(pos, _text.Length);
        UTF16Offset character;
        if (_hasCached && line == _cachedLine && endPos >= _cachedPos)
        {
            // Incremental: only count UTF-16 code units from the last cached position.
            // （Go: core.UTF16Len(c.text[c.cachedPos:endPos])；C# 里 UTF16Len 退化为代码单元下标差）
            character = new UTF16Offset(_cachedChar.Value + (endPos - _cachedPos));
        }
        else
        {
            // Full computation from line start.
            character = new UTF16Offset(endPos - lineStart);
        }
        var cachedChar = character;
        character = new UTF16Offset(character.Value + (pos - endPos));
        _cachedLine = line;
        _cachedPos = endPos;
        _cachedChar = cachedChar;
        _hasCached = true;
        return (line, character);
    }
}
