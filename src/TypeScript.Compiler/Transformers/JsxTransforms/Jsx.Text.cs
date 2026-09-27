// ported from: internal/transformers/jsxtransforms/jsx.go:777-1209
//   （JSX 文本的空格整理与 HTML 实体解码、visitJsxExpression、实体表）
//
// 与 Go 的差异（C# 语言特性；另见 `Jsx.cs` 文件头）：
// 1) Go 按 **UTF-8 字节**下标扫描（`utf8.DecodeRuneInString` + 字节切片）；C# 的 string 是 UTF-16 代码单元，
//    所以按 **char 下标 + 码点**扫描（docs/architecture.md §3.1）：一个码点占 1 或 2 个 char，
//    `i + size - 1` 就是该码点最后一个代码单元的偏移，切片仍然是完整码点（与 Go 的字节切片同语义）。
// 2) `addLineOfJsxText`/`decodeEntities` 收 `ReadOnlySpan<char>`：Go 传的是零拷贝子串（§3.8），
//    C# 用 span 切片，只有 `StringBuilder.Append(span)` 时才复制。
// 3) Go 的 `result.WriteString(stringutil.EncodeJSStringRune(decoded))`：C# **不做哨兵编码**（§3.1）——
//    BMP（含孤立代理项）直接 `Append((char)cp)`、补充平面用 `char.ConvertFromUtf32`、
//    超出 Unicode 范围（Go 的 `string(rune)` 会给 U+FFFD）补 `'\uFFFD'`。见 `AppendCodePoint`。
// 4) Go 的 `strconv.ParseInt(entity, base, 32)` → 逐位累加 + 显式溢出检查（见 `TryDecodeEntity`），
//    语义与 Go 的 ErrRange 一致：任何超出 int32 范围的结果都返回 false（调用点原样输出实体文本）。
// 5) Go 的 `var entities = map[string]rune` → `Dictionary<string, int>`（rune → int，§3.1）；
//    查找走 `GetAlternateLookup<ReadOnlySpan<char>>`，避免每次 `entity.ToString()` 分配。
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Transformers.JsxTransforms;

public sealed partial class JSXTransformer
{
    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxText(text *ast.JsxText) *ast.Node</c>（jsx.go:777-783）。</summary>
    private Node? VisitJsxText(JsxText text)
    {
        var fixedText = JsxTransforms.FixupWhitespaceAndDecodeEntities(text.Text);
        if (fixedText.Length == 0)
        {
            return null;
        }

        return Factory().NewStringLiteral(fixedText, TokenFlags.None);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxExpression(expression *ast.JsxExpression) *ast.Node</c>（jsx.go:852-860）。</summary>
    private Node? VisitJsxExpression(JsxExpression expression)
    {
        var e = Visitor().VisitNode(expression.Expression);
        if (expression.DotDotDotToken is not null)
        {
            return Factory().NewSpreadElement(e);
        }

        return e;
    }
}

public static partial class JsxTransforms
{
    /// <summary>Go: <c>func addLineOfJsxText(b *strings.Builder, trimmedLine string, isInitial bool)</c>（jsx.go:785-793）。</summary>
    private static void AddLineOfJsxText(StringBuilder b, ReadOnlySpan<char> trimmedLine, bool isInitial)
    {
        // We do not escape the string here as that is handled by the printer
        // when it emits the literal. We do, however, need to decode JSX entities.
        var decoded = DecodeEntities(trimmedLine);
        if (!isInitial)
        {
            b.Append(' ');
        }

        b.Append(decoded);
    }

    /// <summary>Go: <c>func fixupWhitespaceAndDecodeEntities(text string) string</c>（jsx.go:810-848）。</summary>
    /// <remarks>
    /// JSX trims whitespace at the end and beginning of lines, except that the
    /// start/end of a tag is considered a start/end of a line only if that line is
    /// on the same line as the closing tag. See examples in
    /// tests/cases/conformance/jsx/tsxReactEmitWhitespace.tsx
    /// See also https://www.w3.org/TR/html4/struct/text.html#h-9.1 and https://www.w3.org/TR/CSS2/text.html#white-space-model
    ///
    /// An equivalent algorithm would be:
    /// <list type="bullet">
    /// <item>If there is only one line, return it.</item>
    /// <item>If there is only whitespace (but multiple lines), return `undefined`.</item>
    /// <item>Split the text into lines.</item>
    /// <item>'trimRight' the first line, 'trimLeft' the last line, 'trim' middle lines.</item>
    /// <item>Decode entities on each line (individually).</item>
    /// <item>Remove empty lines and join the rest with " ".</item>
    /// </list>
    /// </remarks>
    internal static string FixupWhitespaceAndDecodeEntities(string text)
    {
        var acc = new StringBuilder();
        var initial = true;
        // First non-whitespace character on this line.
        var firstNonWhitespace = 0;
        // End position of the last non-whitespace character on this line.
        var lastNonWhitespaceEnd = -1;
        // These initial values are special because the first line is:
        // firstNonWhitespace = 0 to indicate that we want leading whitespace,
        // but lastNonWhitespaceEnd = -1 as a special flag to indicate that we *don't* include the line if it's all whitespace.
        for (var i = 0; i < text.Length;)
        {
            var c = (int)text[i];
            var size = 1;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                c = char.ConvertToUtf32(text[i], text[i + 1]);
                size = 2;
            }

            if (IsLineBreak(c))
            {
                // If we've seen any non-whitespace characters on this line, add the 'trim' of the line.
                // (lastNonWhitespaceEnd == -1 is a special flag to detect whether the first line is all whitespace.)
                if (firstNonWhitespace != -1 && lastNonWhitespaceEnd != -1)
                {
                    AddLineOfJsxText(acc, text.AsSpan(firstNonWhitespace, lastNonWhitespaceEnd + 1 - firstNonWhitespace), initial);
                    initial = false;
                }

                // Reset firstNonWhitespace for the next line.
                // Don't bother to reset lastNonWhitespaceEnd because we ignore it if firstNonWhitespace = -1.
                firstNonWhitespace = -1;
            }
            else if (!IsWhiteSpaceSingleLine(c))
            {
                lastNonWhitespaceEnd = i + size - 1; // Store the end position of the character
                if (firstNonWhitespace == -1)
                {
                    firstNonWhitespace = i;
                }
            }

            i += size;
        }

        if (firstNonWhitespace != -1)
        {
            // Last line had a non-whitespace character. Emit the 'trimLeft', meaning keep trailing whitespace.
            AddLineOfJsxText(acc, text.AsSpan(firstNonWhitespace), initial);
        }

        return acc.ToString();
    }

    /// <summary>Go: <c>func decodeEntities(text string) string</c>（jsx.go:864-912）。</summary>
    /// <remarks>
    /// Replace entities like `&amp;nbsp;`, `&amp;#123;`, and `&amp;#xDEADBEEF;` with the characters they encode.
    /// See https://en.wikipedia.org/wiki/List_of_XML_and_HTML_character_entity_references
    /// </remarks>
    internal static string DecodeEntities(ReadOnlySpan<char> text)
    {
        var i = text.IndexOf('&');
        if (i < 0)
        {
            return text.ToString();
        }

        var result = new StringBuilder(text.Length);
        while (true)
        {
            result.Append(text[..i]);
            text = text[i..];

            var semi = text.IndexOf(';');
            if (semi < 0)
            {
                break;
            }

            // Skip past any intervening '&' characters between the current '&'
            // and the ';'. Each such '&' is not part of a valid entity, so emit
            // it (and any text before the next '&') as literals.
            while (true)
            {
                var nextAmp = text[1..semi].IndexOf('&');
                if (nextAmp < 0)
                {
                    break;
                }

                result.Append(text[..(nextAmp + 1)]);
                text = text[(nextAmp + 1)..];
                semi -= nextAmp + 1;
            }

            var entity = text[1..semi];
            if (TryDecodeEntity(entity, out var decoded))
            {
                // Use the JS-string encoder so lone surrogates (e.g. "&#xD800;")
                // are preserved rather than being lost to U+FFFD by WriteRune.
                AppendCodePoint(result, decoded);
            }
            else
            {
                result.Append(text[..(semi + 1)]);
            }

            text = text[(semi + 1)..];

            i = text.IndexOf('&');
            if (i < 0)
            {
                break;
            }
        }

        result.Append(text);
        return result.ToString();
    }

    /// <summary>Go: <c>func decodeEntity(entity string) (rune, bool)</c>（jsx.go:914-953）→ <c>TryX</c>（docs §3.2）。</summary>
    private static bool TryDecodeEntity(ReadOnlySpan<char> entity, out int decoded)
    {
        decoded = 0;
        if (entity.Length == 0)
        {
            return false;
        }

        if (entity[0] == '#')
        {
            entity = entity[1..];
            if (entity.Length == 0)
            {
                return false;
            }

            var radix = 10;
            if (entity[0] == 'x')
            {
                radix = 16;
                entity = entity[1..];
            }

            if (entity.Length == 0)
            {
                return false;
            }

            foreach (var c in entity)
            {
                if (radix == 16 && !IsHexDigit(c))
                {
                    return false;
                }

                if (radix == 10 && !IsDigit(c))
                {
                    return false;
                }
            }

            // Go: strconv.ParseInt(entity, radix, 32)（`(T, error)` → 失败即 false，§3.2）
            var value = 0UL;
            foreach (var c in entity)
            {
                var digit = (ulong)(radix == 16 ? HexDigitValue(c) : c - '0');
                if (value > (ulong.MaxValue - digit) / (ulong)radix)
                {
                    return false; // 64 位溢出
                }

                value = (value * (ulong)radix) + digit;
            }

            if (value > int.MaxValue)
            {
                return false; // Go 的 ErrRange（int32 越界）
            }

            decoded = (int)value;
            return true;
        }

        return _entitiesLookup.TryGetValue(entity, out decoded);
    }

    /// <summary>十六进制数字字符 → 数值（调用点已用 <c>IsHexDigit</c> 校验，不会走到 else 的非法分支）。</summary>
    private static int HexDigitValue(char c)
    {
        return c <= '9' ? c - '0' : c <= 'F' ? c - 'A' + 10 : c - 'a' + 10;
    }

    /// <summary>
    /// Go 的 <c>stringutil.EncodeJSStringRune(ch rune)</c> 的 C# 对应（docs/architecture.md §3.1：不移植哨兵机制）：
    /// 代理项直接用 <c>char</c> 承载（C# 的 string 与 JS 字符串同构），补充平面用代理对，
    /// 超出 Unicode 范围的码点与 Go 的 <c>string(rune)</c> 一样落成 U+FFFD。
    /// </summary>
    private static void AppendCodePoint(StringBuilder result, int codePoint)
    {
        if (codePoint <= 0xFFFF)
        {
            result.Append((char)codePoint);
        }
        else if (codePoint <= 0x10FFFF)
        {
            result.Append(char.ConvertFromUtf32(codePoint));
        }
        else
        {
            result.Append('\uFFFD');
        }
    }

    /// <summary>Go: <c>var entities = map[string]rune</c>（jsx.go:955-1209；253 条，逐条与 Go 源一致）。</summary>
    private static readonly Dictionary<string, int> _entities = new(StringComparer.Ordinal)
    {
        { "quot", 0x0022 },
        { "amp", 0x0026 },
        { "apos", 0x0027 },
        { "lt", 0x003C },
        { "gt", 0x003E },
        { "nbsp", 0x00A0 },
        { "iexcl", 0x00A1 },
        { "cent", 0x00A2 },
        { "pound", 0x00A3 },
        { "curren", 0x00A4 },
        { "yen", 0x00A5 },
        { "brvbar", 0x00A6 },
        { "sect", 0x00A7 },
        { "uml", 0x00A8 },
        { "copy", 0x00A9 },
        { "ordf", 0x00AA },
        { "laquo", 0x00AB },
        { "not", 0x00AC },
        { "shy", 0x00AD },
        { "reg", 0x00AE },
        { "macr", 0x00AF },
        { "deg", 0x00B0 },
        { "plusmn", 0x00B1 },
        { "sup2", 0x00B2 },
        { "sup3", 0x00B3 },
        { "acute", 0x00B4 },
        { "micro", 0x00B5 },
        { "para", 0x00B6 },
        { "middot", 0x00B7 },
        { "cedil", 0x00B8 },
        { "sup1", 0x00B9 },
        { "ordm", 0x00BA },
        { "raquo", 0x00BB },
        { "frac14", 0x00BC },
        { "frac12", 0x00BD },
        { "frac34", 0x00BE },
        { "iquest", 0x00BF },
        { "Agrave", 0x00C0 },
        { "Aacute", 0x00C1 },
        { "Acirc", 0x00C2 },
        { "Atilde", 0x00C3 },
        { "Auml", 0x00C4 },
        { "Aring", 0x00C5 },
        { "AElig", 0x00C6 },
        { "Ccedil", 0x00C7 },
        { "Egrave", 0x00C8 },
        { "Eacute", 0x00C9 },
        { "Ecirc", 0x00CA },
        { "Euml", 0x00CB },
        { "Igrave", 0x00CC },
        { "Iacute", 0x00CD },
        { "Icirc", 0x00CE },
        { "Iuml", 0x00CF },
        { "ETH", 0x00D0 },
        { "Ntilde", 0x00D1 },
        { "Ograve", 0x00D2 },
        { "Oacute", 0x00D3 },
        { "Ocirc", 0x00D4 },
        { "Otilde", 0x00D5 },
        { "Ouml", 0x00D6 },
        { "times", 0x00D7 },
        { "Oslash", 0x00D8 },
        { "Ugrave", 0x00D9 },
        { "Uacute", 0x00DA },
        { "Ucirc", 0x00DB },
        { "Uuml", 0x00DC },
        { "Yacute", 0x00DD },
        { "THORN", 0x00DE },
        { "szlig", 0x00DF },
        { "agrave", 0x00E0 },
        { "aacute", 0x00E1 },
        { "acirc", 0x00E2 },
        { "atilde", 0x00E3 },
        { "auml", 0x00E4 },
        { "aring", 0x00E5 },
        { "aelig", 0x00E6 },
        { "ccedil", 0x00E7 },
        { "egrave", 0x00E8 },
        { "eacute", 0x00E9 },
        { "ecirc", 0x00EA },
        { "euml", 0x00EB },
        { "igrave", 0x00EC },
        { "iacute", 0x00ED },
        { "icirc", 0x00EE },
        { "iuml", 0x00EF },
        { "eth", 0x00F0 },
        { "ntilde", 0x00F1 },
        { "ograve", 0x00F2 },
        { "oacute", 0x00F3 },
        { "ocirc", 0x00F4 },
        { "otilde", 0x00F5 },
        { "ouml", 0x00F6 },
        { "divide", 0x00F7 },
        { "oslash", 0x00F8 },
        { "ugrave", 0x00F9 },
        { "uacute", 0x00FA },
        { "ucirc", 0x00FB },
        { "uuml", 0x00FC },
        { "yacute", 0x00FD },
        { "thorn", 0x00FE },
        { "yuml", 0x00FF },
        { "OElig", 0x0152 },
        { "oelig", 0x0153 },
        { "Scaron", 0x0160 },
        { "scaron", 0x0161 },
        { "Yuml", 0x0178 },
        { "fnof", 0x0192 },
        { "circ", 0x02C6 },
        { "tilde", 0x02DC },
        { "Alpha", 0x0391 },
        { "Beta", 0x0392 },
        { "Gamma", 0x0393 },
        { "Delta", 0x0394 },
        { "Epsilon", 0x0395 },
        { "Zeta", 0x0396 },
        { "Eta", 0x0397 },
        { "Theta", 0x0398 },
        { "Iota", 0x0399 },
        { "Kappa", 0x039A },
        { "Lambda", 0x039B },
        { "Mu", 0x039C },
        { "Nu", 0x039D },
        { "Xi", 0x039E },
        { "Omicron", 0x039F },
        { "Pi", 0x03A0 },
        { "Rho", 0x03A1 },
        { "Sigma", 0x03A3 },
        { "Tau", 0x03A4 },
        { "Upsilon", 0x03A5 },
        { "Phi", 0x03A6 },
        { "Chi", 0x03A7 },
        { "Psi", 0x03A8 },
        { "Omega", 0x03A9 },
        { "alpha", 0x03B1 },
        { "beta", 0x03B2 },
        { "gamma", 0x03B3 },
        { "delta", 0x03B4 },
        { "epsilon", 0x03B5 },
        { "zeta", 0x03B6 },
        { "eta", 0x03B7 },
        { "theta", 0x03B8 },
        { "iota", 0x03B9 },
        { "kappa", 0x03BA },
        { "lambda", 0x03BB },
        { "mu", 0x03BC },
        { "nu", 0x03BD },
        { "xi", 0x03BE },
        { "omicron", 0x03BF },
        { "pi", 0x03C0 },
        { "rho", 0x03C1 },
        { "sigmaf", 0x03C2 },
        { "sigma", 0x03C3 },
        { "tau", 0x03C4 },
        { "upsilon", 0x03C5 },
        { "phi", 0x03C6 },
        { "chi", 0x03C7 },
        { "psi", 0x03C8 },
        { "omega", 0x03C9 },
        { "thetasym", 0x03D1 },
        { "upsih", 0x03D2 },
        { "piv", 0x03D6 },
        { "ensp", 0x2002 },
        { "emsp", 0x2003 },
        { "thinsp", 0x2009 },
        { "zwnj", 0x200C },
        { "zwj", 0x200D },
        { "lrm", 0x200E },
        { "rlm", 0x200F },
        { "ndash", 0x2013 },
        { "mdash", 0x2014 },
        { "lsquo", 0x2018 },
        { "rsquo", 0x2019 },
        { "sbquo", 0x201A },
        { "ldquo", 0x201C },
        { "rdquo", 0x201D },
        { "bdquo", 0x201E },
        { "dagger", 0x2020 },
        { "Dagger", 0x2021 },
        { "bull", 0x2022 },
        { "hellip", 0x2026 },
        { "permil", 0x2030 },
        { "prime", 0x2032 },
        { "Prime", 0x2033 },
        { "lsaquo", 0x2039 },
        { "rsaquo", 0x203A },
        { "oline", 0x203E },
        { "frasl", 0x2044 },
        { "euro", 0x20AC },
        { "image", 0x2111 },
        { "weierp", 0x2118 },
        { "real", 0x211C },
        { "trade", 0x2122 },
        { "alefsym", 0x2135 },
        { "larr", 0x2190 },
        { "uarr", 0x2191 },
        { "rarr", 0x2192 },
        { "darr", 0x2193 },
        { "harr", 0x2194 },
        { "crarr", 0x21B5 },
        { "lArr", 0x21D0 },
        { "uArr", 0x21D1 },
        { "rArr", 0x21D2 },
        { "dArr", 0x21D3 },
        { "hArr", 0x21D4 },
        { "forall", 0x2200 },
        { "part", 0x2202 },
        { "exist", 0x2203 },
        { "empty", 0x2205 },
        { "nabla", 0x2207 },
        { "isin", 0x2208 },
        { "notin", 0x2209 },
        { "ni", 0x220B },
        { "prod", 0x220F },
        { "sum", 0x2211 },
        { "minus", 0x2212 },
        { "lowast", 0x2217 },
        { "radic", 0x221A },
        { "prop", 0x221D },
        { "infin", 0x221E },
        { "ang", 0x2220 },
        { "and", 0x2227 },
        { "or", 0x2228 },
        { "cap", 0x2229 },
        { "cup", 0x222A },
        { "int", 0x222B },
        { "there4", 0x2234 },
        { "sim", 0x223C },
        { "cong", 0x2245 },
        { "asymp", 0x2248 },
        { "ne", 0x2260 },
        { "equiv", 0x2261 },
        { "le", 0x2264 },
        { "ge", 0x2265 },
        { "sub", 0x2282 },
        { "sup", 0x2283 },
        { "nsub", 0x2284 },
        { "sube", 0x2286 },
        { "supe", 0x2287 },
        { "oplus", 0x2295 },
        { "otimes", 0x2297 },
        { "perp", 0x22A5 },
        { "sdot", 0x22C5 },
        { "lceil", 0x2308 },
        { "rceil", 0x2309 },
        { "lfloor", 0x230A },
        { "rfloor", 0x230B },
        { "lang", 0x2329 },
        { "rang", 0x232A },
        { "loz", 0x25CA },
        { "spades", 0x2660 },
        { "clubs", 0x2663 },
        { "hearts", 0x2665 },
        { "diams", 0x2666 },
    };

    /// <summary>Go 的 <c>entities[entity]</c>（string 切片查找）→ 无分配的 span 查找（net9+ 的 AlternateLookup）。</summary>
    private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _entitiesLookup =
        _entities.GetAlternateLookup<ReadOnlySpan<char>>();
}
