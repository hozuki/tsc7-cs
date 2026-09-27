// ported from: internal/stringutil/util.go
//
// 偏离 Go 的地方（docs/architecture.md §3.1、§3.4）：
//
// 1) 以下符号**不移植**：EncodeJSStringRune / DecodeJSStringRune / CombineSurrogatePairs，以及
//    surrogateUTF8Lead、surrogateUTF8LeadBits、utf8ContMarker、utf8ContMax、utf8ContMask、
//    surrogateUTF8Byte1Min/Max 这些哨兵常量。原因：Go 的 string 是 UTF-8 字节序列，孤立代理项
//    （U+D800–U+DFFF）在 UTF-8 里不合法、会被替换成 U+FFFD，所以 TSGO 必须把它编成 3 字节
//    CESU-8/WTF-8 哨兵字节才能无损保存，再在需要的地方解码回来、拼接后再合并成对。C# 的 string
//    本身就是 UTF-16 代码单元序列，与 JavaScript 字符串同构，孤立代理项天生是合法值，因此
//    需要保留孤立代理项的地方直接 Append(char) 即可（见 JsCase.cs）。
//
// 2) 判定函数的参数类型用 int 而不是 System.Text.Rune：Go 的 rune 是 int32，值域包含代理项、
//    astral 码点和负值；C# 的 char 只有 16 位（astral 码点会被静默截断），System.Text.Rune 无法
//    表示代理项（new Rune(0xD800) 会抛 ArgumentOutOfRangeException），两者都不是 rune 的等价
//    参数类型。int 是 rune 的精确对应物，而且 char 能隐式转 int（char 实参不需要强制转换）。
//
// 3) getByteOrderMarkLength / RemoveByteOrderMark / AddUTF8ByteOrderMark 在 Go 里是字节级实现
//    （识别 EF BB BF / FE FF / FF FE 三种 BOM）；C# 的 Text 是 UTF-16 字符序列，三种 BOM 在解码后
//    都归一到同一个字符 U+FEFF，所以这里按 char 级 U+FEFF 判断（见各函数注释）。
//
// 4) 新增了一个私有辅助函数 DecodeRuneAt（Go 直接用标准库 utf8.DecodeRuneInString）：它是"在 UTF-16
//    代码单元串上读一个码点"的最小封装，内部用 Rune.DecodeFromUtf16，语义与 Go 的 DecodeRuneInString
//    逐条对应（非法/孤立代理项 → U+FFFD、消耗 1；末尾 → size 0）。Compare.cs 里有对应的 span 重载。
using System.Text;
using System.Text.RegularExpressions;

namespace TypeScript.StringUtil;

public static partial class StringUtil
{
    public static bool IsWhiteSpaceLike(int ch)
    {
        return IsWhiteSpaceSingleLine(ch) || IsLineBreak(ch);
    }

    public static bool IsWhiteSpaceSingleLine(int ch)
    {
        // Note: nextLine is in the Zs space, and should be considered to be a whitespace.
        // It is explicitly not a line-break as it isn't in the exact set specified by EcmaScript.
        switch (ch)
        {
            case ' ':    // space
            case '\t':   // tab
            case '\v':   // verticalTab
            case '\f':   // formFeed
            case 0x0085: // nextLine
            case 0x00A0: // nonBreakingSpace
            case 0x1680: // ogham
            case 0x2000: // enQuad
            case 0x2001: // emQuad
            case 0x2002: // enSpace
            case 0x2003: // emSpace
            case 0x2004: // threePerEmSpace
            case 0x2005: // fourPerEmSpace
            case 0x2006: // sixPerEmSpace
            case 0x2007: // figureSpace
            case 0x2008: // punctuationEmSpace
            case 0x2009: // thinSpace
            case 0x200A: // hairSpace
            case 0x200B: // zeroWidthSpace
            case 0x202F: // narrowNoBreakSpace
            case 0x205F: // mathematicalSpace
            case 0x3000: // ideographicSpace
            case 0xFEFF: // byteOrderMark
                return true;
        }

        return false;
    }

    public static bool IsLineBreak(int ch)
    {
        // ES5 7.3:
        // The ECMAScript line terminator characters are listed in Table 3.
        //     Table 3: Line Terminator Characters
        //     Code Unit Value     Name                    Formal Name
        //     \u000A              Line Feed               <LF>
        //     \u000D              Carriage Return         <CR>
        //     \u2028              Line separator          <LS>
        //     \u2029              Paragraph separator     <PS>
        // Only the characters in Table 3 are treated as line terminators. Other new line or line
        // breaking characters are treated as white space but not as line terminators.
        switch (ch)
        {
            case '\n':   // lineFeed
            case '\r':   // carriageReturn
            case 0x2028: // lineSeparator
            case 0x2029: // paragraphSeparator
                return true;
        }

        return false;
    }

    public static bool IsDigit(int ch)
    {
        return ch >= '0' && ch <= '9';
    }

    public static bool IsOctalDigit(int ch)
    {
        return ch >= '0' && ch <= '7';
    }

    public static bool IsHexDigit(int ch)
    {
        return ch >= '0' && ch <= '9' || ch >= 'A' && ch <= 'F' || ch >= 'a' && ch <= 'f';
    }

    public static bool IsASCIILetter(int ch)
    {
        return ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z';
    }

    public static string[] SplitLines(string text)
    {
        // Go: make([]string, 0, strings.Count(text, "\n")+1) // preallocate
        // Go 这行用 strings.Count 预算容量；C# 侧就地数一遍 \n（不引入额外的辅助 API）。
        var newLineCount = 0;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                newLineCount++;
            }
        }

        var lines = new List<string>(newLineCount + 1);
        var start = 0;
        var pos = 0;
        // Go 按 UTF-8 字节下标走；\r 和 \n 都是单字节/单代码单元，所以这里按 UTF-16 代码单元
        // 下标走的结果与 Go 完全一致（切出来的子串内容也一致；Go 是零拷贝视图，C# 的子串必然分配）。
        while (pos < text.Length)
        {
            switch (text[pos])
            {
                case '\r':
                    if (pos + 1 < text.Length && text[pos + 1] == '\n')
                    {
                        lines.Add(text[start..pos]);
                        pos += 2;
                        start = pos;
                        continue;
                    }

                    goto case '\n';
                case '\n':
                    lines.Add(text[start..pos]);
                    pos++;
                    start = pos;
                    continue;
            }

            pos++;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines.ToArray();
    }

    public static int GuessIndentation(string[] lines)
    {
        const int maxSmiX86 = 0x3fff_ffff;
        var indentation = maxSmiX86;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var i = 0;
            while (i < line.Length && i < indentation)
            {
                var ch = DecodeRuneAt(line, i, out var size);
                if (!IsWhiteSpaceLike(ch.Value))
                {
                    break;
                }

                i += size;
            }

            if (i < indentation)
            {
                indentation = i;
            }

            if (indentation == 0)
            {
                return 0;
            }
        }

        if (indentation == maxSmiX86)
        {
            return 0;
        }

        return indentation;
    }

    // https://tc39.es/ecma262/multipage/global-object.html#sec-encodeuri-uri
    public static string EncodeURI(string s)
    {
        // Go 直接按字节遍历 s 的 UTF-8 编码；C# 的 string 是 UTF-16，所以先取 UTF-8 字节再逐字节处理。
        // （偏离：孤立代理项会被 Encoding.UTF8 替换成 U+FFFD 的 3 字节 EF BF BD；Go 侧的孤立代理项
        //   以 CESU-8 哨兵字节存在，会被逐个百分号编码。本函数只用于 source map 路径名，不会出现它们。）
        var bytes = Encoding.UTF8.GetBytes(s);
        var builder = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (!ShouldEscapeForEncodeURI(b))
            {
                builder.Append((char)b);
                continue;
            }

            builder.Append('%');
            builder.Append(_upperhex[b >> 4]);
            builder.Append(_upperhex[b & 0x0f]);
        }

        return builder.ToString();
    }

    private const string _upperhex = "0123456789ABCDEF";

    private static bool ShouldEscapeForEncodeURI(byte b)
    {
        switch (b)
        {
            case >= (byte)'A' and <= (byte)'Z':
                return false;
            case >= (byte)'a' and <= (byte)'z':
                return false;
            case >= (byte)'0' and <= (byte)'9':
                return false;
        }

        switch (b)
        {
            case (byte)';':
            case (byte)'/':
            case (byte)'?':
            case (byte)':':
            case (byte)'@':
            case (byte)'&':
            case (byte)'=':
            case (byte)'+':
            case (byte)'$':
            case (byte)',':
            case (byte)'#':
            case (byte)'-':
            case (byte)'_':
            case (byte)'.':
            case (byte)'!':
            case (byte)'~':
            case (byte)'*':
            case (byte)'\'':
            case (byte)'(':
            case (byte)')':
                return false;
            default:
                return true;
        }
    }

    // Go 的 getByteOrderMarkLength 在字节层面识别 UTF-8（EF BB BF）与 UTF-16BE/LE（FE FF / FF FE）的 BOM，
    // 返回 2/3 字节的“按字节长度”。C# 侧字符串已经是 UTF-16 字符序列，三种 BOM 解码后都归一到同一个
    // 字符 U+FEFF（BOM 只剩 1 个代码单元），所以这里返回 0 或 1。
    private static int GetByteOrderMarkLength(string text)
    {
        if (text.Length >= 1 && text[0] == 0xFEFF)
        {
            return 1;
        }

        return 0;
    }

    public static string RemoveByteOrderMark(string text)
    {
        var length = GetByteOrderMarkLength(text);
        if (length > 0)
        {
            return text[length..];
        }

        return text;
    }

    // 名字保持与 Go 一致（Go 里加的是 UTF-8 的 EF BB BF），但 C# 侧加的是字符 U+FEFF：
    // 编码成 UTF-8 写文件时它会变成同样的 EF BB BF 三字节。
    public static string AddUTF8ByteOrderMark(string text)
    {
        if (GetByteOrderMarkLength(text) == 0)
        {
            return "\uFEFF" + text;
        }

        return text;
    }

    public static string StripQuotes(string name)
    {
        if (name.Length < 2)
        {
            return name;
        }

        // Go 用 utf8.DecodeRuneInString / DecodeLastRuneInString 取首末码点；引号都是 ASCII，首末码点是
        // 引号时必然是单个代码单元，所以这里按代码单元取。astral 码点与孤立代理项都不可能是引号，
        // 与 Go 的行为一致。
        var firstChar = name[0];
        var lastChar = name[^1];
        if (firstChar == lastChar && (firstChar == '\'' || firstChar == '"' || firstChar == '`'))
        {
            return name[1..(name.Length - 1)];
        }

        return name;
    }

    private static readonly Regex _matchSlashSomething = new(@"\\.", RegexOptions.CultureInvariant);

    // Go: func matchSlashReplacer(in string) string —— 取匹配串去掉第一个字符（反斜杠）之后的部分。
    // C# 的 Regex.Replace 只接受 MatchEvaluator（参数是 Match），所以这里多一步 match.Value。
    private static string MatchSlashReplacer(Match match)
    {
        return match.Value[1..];
    }

    public static string UnquoteString(string str)
    {
        // strconv.Unquote is insufficient as that only handles a single character inside single quotes, as those are character literals in go
        var inner = StripQuotes(str);
        // In strada we do str.replace(/\\./g, s => s.substring(1)) - which is to say, replace all backslash-something with just something
        // That's replicated here faithfully, but it seems wrong! This should probably be an actual unquote operation?
        return _matchSlashSomething.Replace(inner, MatchSlashReplacer);
    }

    public static string LowerFirstChar(string str)
    {
        var ch = DecodeRuneAt(str, 0, out var size);
        if (size > 0)
        {
            // Go: string(unicode.ToLower(char)) —— simple 映射，对应 Rune.ToLowerInvariant（docs §3.4）。
            // （偏离：首字符是孤立代理项时 Go 的 utf8.DecodeRuneInString 会给出 U+FFFD 加余下的哨兵字节，
            //   这里给出 U+FFFD 加余下的代码单元；该情形不会出现在标识符上。）
            return Rune.ToLowerInvariant(ch).ToString() + str[size..];
        }

        return str;
    }

    public static string TruncateByRunes(string str, int maxLength)
    {
        // Go 这里是 `len(str) < maxLength`（UTF-8 字节长度）的快速返回；C# 用 UTF-16 代码单元长度。
        // 结果一致：代码单元数 < maxLength 时码点数也必然 < maxLength，直接返回原串。
        if (str.Length < maxLength)
        {
            return str;
        }

        if (maxLength <= 0)
        {
            return "";
        }

        var runeCount = 0;
        var i = 0;
        while (i < str.Length)
        {
            runeCount++;
            if (runeCount > maxLength)
            {
                return str[..i];
            }

            _ = DecodeRuneAt(str, i, out var size);
            i += size;
        }

        return str;
    }

    // SurrogateLowStart is the boundary between the high and low halves of the
    // UTF-16 surrogate range. unicode/utf16 only exposes IsSurrogate for the
    // whole range, so this split point is defined here to distinguish the two.
    public const int SurrogateLowStart = 0xDC00;

    public static bool IsHighSurrogate(int ch)
    {
        return IsSurrogate(ch) && ch < SurrogateLowStart;
    }

    public static bool IsLowSurrogate(int ch)
    {
        return IsSurrogate(ch) && ch >= SurrogateLowStart;
    }

    /// <summary>Go: <c>utf16.IsSurrogate</c> —— U+D800–U+DFFF。</summary>
    public static bool IsSurrogate(int ch)
    {
        return ch >= 0xD800 && ch <= 0xDFFF;
    }

    /// <summary>
    /// Go: <c>utf16.DecodeRune</c>。不是合法的代理对时返回 U+FFFD（Go 也是返回替换字符，不 panic），
    /// 所以这里不直接转调会抛异常的 <c>char.ConvertToUtf32</c>。
    /// </summary>
    public static int SurrogatePairToCodePoint(int high, int low)
    {
        if (IsHighSurrogate(high) && IsLowSurrogate(low))
        {
            // 必须写全括号：C# 里 `+` 的优先级高于 `|`，而 Go 里两者同级且左结合。
            return (((high - 0xD800) << 10) | (low - 0xDC00)) + 0x10000;
        }

        return 0xFFFD;
    }

    /// <summary>
    /// Go: <c>utf16.EncodeRune</c>。不需要编码（码点 &lt; U+10000）或超出 U+10FFFF 时返回
    /// (U+FFFD, U+FFFD)，与 Go 一致。
    /// </summary>
    public static (int high, int low) CodePointToSurrogatePair(int ch)
    {
        if (ch < 0x10000 || ch > 0x10FFFF)
        {
            return (0xFFFD, 0xFFFD);
        }

        ch -= 0x10000;
        return (0xD800 + ((ch >> 10) & 0x3FF), 0xDC00 + (ch & 0x3FF));
    }

    // Go 用 utf8.DecodeRuneInString 读下一个码点：非法或不完整序列返回 U+FFFD 且只消耗 1 个单元，
    // 读到字符串末尾返回 (U+FFFD, 0)。C# 的等价物是 Rune.DecodeFromUtf16：孤立代理项给出
    // NeedMoreData、charsConsumed == 1、value == U+FFFD，空 span 给出 (U+FFFD, 0)，合法代理对给出
    // Done、charsConsumed == 2 —— 与 Go 的替换语义逐条对应，且不会抛异常。
    // 注意：需要"原样保留孤立代理项"的地方（JsCase.cs）不能用这个函数，那里要按代码单元读。
    private static Rune DecodeRuneAt(string str, int index, out int size)
    {
        _ = Rune.DecodeFromUtf16(str.AsSpan(index), out var value, out size);
        return value;
    }
}
