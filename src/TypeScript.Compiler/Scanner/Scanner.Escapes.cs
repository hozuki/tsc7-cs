// ported from: internal/scanner/scanner.go:1585-1942（scanString / scanTemplateAndSetTokenValue / scanEscapeSequence /
//               scanUnicodeEscape / scanLowSurrogateEscape / peekUnicodeEscape）+ internal/stringutil/util.go 里
//               scanner 需要的码点/代理项解码助手（EncodeJSStringRune / DecodeJSStringRune 的 C# 等价物）
//
// 与 Go 的偏离（逐条）：
//
// 1) **哨兵机制不移植**（docs/architecture.md §3.1）：Go 的 `stringutil.EncodeJSStringRune(ch)` 把孤立代理项编成
//    3 字节的 CESU-8/WTF-8 哨兵（因为 Go 的 string 是 UTF-8 字节序列，孤立代理项在 UTF-8 里非法、会被换成
//    U+FFFD）。C# 的 string 就是 UTF-16 代码单元序列，孤立代理项本来就是合法值，所以：
//      * `EncodeJSStringRune(cp)` → `CodePointToString(cp)`（代理项 → 单个 char；其余码点 → ConvertFromUtf32）；
//      * `stringutil.SurrogatePairToCodePoint(hi, lo)` → `SurrogatePairToCodePoint`；
//      * `stringutil.DecodeJSStringRune(s)` → `DecodeJSStringRune(s)`，返回的 Size 是**UTF-16 代码单元数**
//        （Go 返回字节数；调用点只用 `len(s) == size` 判断"整串是不是一个完整码点"，语义等价，见
//        Regexp.cs 的 scanClassRanges）。
// 2) **utf8.DecodeRuneInString → DecodeRuneAt**：C# 的"在字符串上读一个码点"必须在 UTF-16 代码单元上下标进行。
//    语义逐条对应 Go：有效代理对 → (码点, 2)；孤立代理项 → (它自己的值, 1)（TS 语义，§3.1）；末尾 → (U+FFFD, 0)
//    （Go 的 DecodeRuneInString("") 返回 (RuneError, 0)）。注意 Go 对"非法 UTF-8"返回 RuneError，而 C# 的
//    string 不存在非法字节，因此 U+FFFD 只来自源文本里的字面量。
// 3) `strconv.ParseInt(s, 16, 32)`：Go 在越界时返回 MaxInt32（并带 ErrRange），C# 侧 `ParseHexInt32` 手写同样的
//    钳制语义（避免异常开销）。`strconv.ParseInt(octal, 8, …)` 同理见 Scanner.Numbers.cs。
// 4) Go 用 `strings.Builder` 收集字符串片段；C# 用 `StringBuilder`，并尽量用
//    `StringBuilder.Append(string, start, count)` 追加视图（不额外分配子串）。
// 5) Go 的 `fmt.Sprintf("\\x%02x", code)` → C# 内插字符串 `$"\\x{code:x2}"`。
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

public sealed partial class Scanner
{
    // ───────────────────────────── 码点/代理项助手（Go internal/stringutil/util.go） ─────────────────────────────

    /// <summary>
    /// Go: <c>utf8.DecodeRuneInString(text[index:])</c>。在 UTF-16 代码单元串上读一个码点：
    /// 有效代理对 → (码点, 2)；孤立代理项 → (它自己的值, 1)；index 越界（EOF）→ (U+FFFD, 0)。
    /// </summary>
    internal static int DecodeRuneAt(string text, int index, out int size)
    {
        if (index >= text.Length)
        {
            size = 0;
            return 0xFFFD; // Go: utf8.RuneError（DecodeRuneInString("") 的返回值）
        }

        var ch = text[index];
        if (char.IsHighSurrogate(ch) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            size = 2;
            return char.ConvertToUtf32(ch, text[index + 1]);
        }

        size = 1;
        return ch;
    }

    /// <summary>
    /// Go: <c>utf8.DecodeLastRuneInString(text[:end])</c>。读 `end` 之前的最后一个码点（`end` 不含）。
    /// </summary>
    internal static (int Rune, int Size) DecodeLastRuneAt(string text, int end)
    {
        if (end <= 0)
        {
            return (0xFFFD, 0);
        }

        var ch = text[end - 1];
        if (char.IsLowSurrogate(ch) && end >= 2 && char.IsHighSurrogate(text[end - 2]))
        {
            return (char.ConvertToUtf32(text[end - 2], ch), 2);
        }

        return (ch, 1);
    }

    /// <summary>
    /// Go: <c>stringutil.EncodeJSStringRune(codePoint)</c> 的 C# 等价物（不移植哨兵编码，见文件头第 1 条）：
    /// 代理项值 → 单个代码单元；其余码点 → 一或两个代码单元。
    /// </summary>
    internal static string CodePointToString(int codePoint)
    {
        if (codePoint >= 0xD800 && codePoint <= 0xDFFF)
        {
            return ((char)codePoint).ToString();
        }

        return char.ConvertFromUtf32(codePoint);
    }

    /// <summary>Go: <c>stringutil.SurrogatePairToCodePoint(high, low)</c>（= utf16.DecodeRune）。</summary>
    internal static int SurrogatePairToCodePoint(int high, int low)
    {
        return char.ConvertToUtf32((char)high, (char)low);
    }

    /// <summary>
    /// Go: <c>stringutil.DecodeJSStringRune(s) (rune, int)</c>。Size 是**消耗的 UTF-16 代码单元数**（Go 是字节数）；
    /// 调用点用它判断"整串恰好是一个码点"（`s.Length == size`）。
    /// </summary>
    internal static (int Value, int Size) DecodeJSStringRune(string s)
    {
        var value = DecodeRuneAt(s, 0, out var size);
        return (value, size);
    }

    // Go: fmt.Sprintf("\\x%02x", code) —— 八进制转义的诊断参数（`\xNN` 十六进制展示）。
    private static string FormatEscapeByte(int code) => $"\\x{code:x2}";

    // Go: strconv.ParseInt(text[start:end], 16, 32)（越界 → MaxInt32，不抛异常）。
    private static int ParseHexInt32(string text, int start, int end)
    {
        long value = 0;
        for (var i = start; i < end; i++)
        {
            value = (value * 16) + HexValue(text[i]);
            if (value > int.MaxValue)
            {
                return int.MaxValue; // Go: ParseInt 的 ErrRange 分支返回 MaxInt32
            }
        }

        return (int)value;
    }

    // Go: strconv.ParseInt(digits, 16, 32)（越界 → MaxInt32）。
    private static int ParseHexInt32(string digits) => ParseHexInt32(digits, 0, digits.Length);

    private static int HexValue(char ch)
    {
        if (ch >= '0' && ch <= '9')
        {
            return ch - '0';
        }

        if (ch >= 'A' && ch <= 'F')
        {
            return ch - 'A' + 10;
        }

        return ch - 'a' + 10; // ch 已由 IsHexDigit 保证是十六进制数字
    }

    // Go: strconv.ParseInt(text[start:end], 8, 32)（`\0`-`\7` 转义最多 3 位八进制数字，不会越界）。
    private static int ParseOctalInt32(string text, int start, int end)
    {
        var value = 0;
        for (var i = start; i < end; i++)
        {
            value = (value * 8) + (text[i] - '0');
        }

        return value;
    }

    // ───────────────────────────── 字符串 / 模板字面量 ─────────────────────────────

    private string ScanString(bool jsxAttributeString)
    {
        var quote = Char();
        if (quote == '\'')
        {
            _tokenFlags |= TokenFlags.SingleQuote;
        }

        _pos++;

        // Fast path for simple strings without escape sequences.
        // Go: strLen := strings.IndexByte(s.text[s.pos:], byte(quote))（用 span 查，命中时只分配一次返回值）
        var index = _text.IndexOf((char)quote, _pos);
        var strLen = index < 0 ? -1 : index - _pos;
        if (strLen == 0)
        {
            _pos++;
            return "";
        }

        if (strLen > 0)
        {
            var str = _text.AsSpan(_pos, strLen);
            if (jsxAttributeString ||
                str.IndexOf('\\') < 0 && str.IndexOf('\r') < 0 && str.IndexOf('\n') < 0)
            {
                _pos += strLen + 1;
                return str.ToString();
            }
        }

        var sb = new StringBuilder();
        var start = _pos;
        while (true)
        {
            var ch = Char();
            if (ch < 0)
            {
                sb.Append(_text, start, _pos - start);
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Unterminated_string_literal);
                break;
            }

            if (ch == quote)
            {
                sb.Append(_text, start, _pos - start);
                _pos++;
                break;
            }

            if (ch == '\\' && !jsxAttributeString)
            {
                sb.Append(_text, start, _pos - start);
                sb.Append(ScanEscapeSequence(EscapeSequenceScanningFlags.String | EscapeSequenceScanningFlags.ReportErrors));
                start = _pos;
                continue;
            }

            if ((ch == '\n' || ch == '\r') && !jsxAttributeString)
            {
                sb.Append(_text, start, _pos - start);
                _tokenFlags |= TokenFlags.Unterminated;
                Error(Unterminated_string_literal);
                break;
            }

            _pos++;
        }

        return sb.ToString();
    }

    private Kind ScanTemplateAndSetTokenValue(bool shouldEmitInvalidEscapeError)
    {
        var startedWithBacktick = Char() == '`';
        _pos++;
        var start = _pos;
        // Go: parts := make([]string, 0, 4) + strings.Join(parts, "")；C# 用 StringBuilder 直接累积（等价、少一次拼接）。
        var parts = new StringBuilder();
        Kind token;
        while (true)
        {
            ScanASCIIWhile(default(TemplateCharPredicate));
            var ch = Char();
            if (ch < 0 || ch == '`')
            {
                parts.Append(_text, start, _pos - start);
                if (ch == '`')
                {
                    _pos++;
                }
                else
                {
                    _tokenFlags |= TokenFlags.Unterminated;
                    Error(Unterminated_template_literal);
                }

                token = IfElse(startedWithBacktick, Kind.NoSubstitutionTemplateLiteral, Kind.TemplateTail);
                break;
            }

            if (ch == '$' && CharAt(1) == '{')
            {
                parts.Append(_text, start, _pos - start);
                _pos += 2;
                token = IfElse(startedWithBacktick, Kind.TemplateHead, Kind.TemplateMiddle);
                break;
            }

            if (ch == '\\')
            {
                parts.Append(_text, start, _pos - start);
                parts.Append(ScanEscapeSequence(EscapeSequenceScanningFlags.String | IfElse(shouldEmitInvalidEscapeError, EscapeSequenceScanningFlags.ReportErrors, EscapeSequenceScanningFlags.None)));
                start = _pos;
                continue;
            }

            // Speculated ECMAScript 6 Spec 11.8.6.1:
            // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for Template Values
            if (ch == '\r')
            {
                parts.Append(_text, start, _pos - start);
                _pos++;
                if (Char() == '\n')
                {
                    _pos++;
                }

                parts.Append('\n');
                start = _pos;
                continue;
            }

            _pos++;
        }

        _tokenValue = parts.ToString();
        return token;
    }

    // ───────────────────────────── 转义序列 ─────────────────────────────

    internal string ScanEscapeSequence(EscapeSequenceScanningFlags flags)
    {
        var start = _pos;
        _pos++;
        var ch = Char();
        if (ch < 0)
        {
            Error(Unexpected_end_of_text);
            return "";
        }

        _pos++;
        switch (ch)
        {
            case '0':
                // Although '0' preceding any digit is treated as LegacyOctalEscapeSequence,
                // '\08' should separately be interpreted as '\0' + '8'.
                if (!IsDigit(Char()))
                {
                    return "\0";
                }

                // '\01', '\011'
                goto case '1';
            case '1':
            case '2':
            case '3':
                // '\1', '\17', '\177'
                if (IsOctalDigit(Char()))
                {
                    _pos++;
                }

                // '\17', '\177'
                goto case '4';
            case '4':
            case '5':
            case '6':
            case '7':
                // '\4', '\47' but not '\477'
                if (IsOctalDigit(Char()))
                {
                    _pos++;
                }

                // '\47'
                _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    var code = ParseOctalInt32(_text, start + 1, _pos);
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AtomEscape) == 0 && ch != '0')
                    {
                        ErrorAt(Octal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class_If_this_was_intended_as_an_escape_sequence_use_the_syntax_0_instead, start, _pos - start, FormatEscapeByte(code));
                    }
                    else
                    {
                        ErrorAt(Octal_escape_sequences_are_not_allowed_Use_the_syntax_0, start, _pos - start, FormatEscapeByte(code));
                    }

                    return CodePointToString(code);
                }

                return _text[start.._pos];
            case '8':
            case '9':
                // the invalid '\8' and '\9'
                _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                {
                    if ((flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AtomEscape) == 0)
                    {
                        ErrorAt(Decimal_escape_sequences_and_backreferences_are_not_allowed_in_a_character_class, start, _pos - start);
                    }
                    else
                    {
                        ErrorAt(Escape_sequence_0_is_not_allowed, start, _pos - start, _text[start.._pos]);
                    }

                    return CodePointToString(ch);
                }

                return _text[start.._pos];
            case 'b':
                return "\b";
            case 't':
                return "\t";
            case 'n':
                return "\n";
            case 'v':
                return "\v";
            case 'f':
                return "\f";
            case 'r':
                return "\r";
            case '\'':
                return "'";
            case '"':
                return "\"";
            case 'u':
                {
                    // '\uDDDD' and '\u{DDDDDD}'
                    var extended = Char() == '{';
                    _pos -= 2;
                    var codePoint = ScanUnicodeEscape((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0);
                    if (extended)
                    {
                        if ((flags & EscapeSequenceScanningFlags.AllowExtendedUnicodeEscape) == 0)
                        {
                            _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                            if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                            {
                                ErrorAt(Unicode_escape_sequences_are_only_available_when_the_Unicode_u_flag_or_the_Unicode_Sets_v_flag_is_set, start, _pos - start);
                            }
                        }

                        if (codePoint < 0)
                        {
                            return _text[start.._pos];
                        }

                        // In string literals, a high surrogate \u{...} followed by a low
                        // surrogate escape forms a single code point, exactly as adjacent
                        // UTF-16 code units would in a JavaScript string.
                        if ((flags & EscapeSequenceScanningFlags.RegularExpression) == 0 && IsHighSurrogate(codePoint))
                        {
                            var (combined, ok) = ScanLowSurrogateEscape(codePoint);
                            if (ok)
                            {
                                return CodePointToString(combined);
                            }
                        }

                        return CodePointToString(codePoint);
                    }

                    if (codePoint < 0)
                    {
                        return _text[start.._pos];
                    }
                    else if (IsHighSurrogate(codePoint))
                    {
                        if ((flags & EscapeSequenceScanningFlags.RegularExpression) == 0)
                        {
                            // Combine \uHigh followed by any low surrogate escape (\uLow or
                            // \u{Low}) into a single code point in string literals, matching
                            // how adjacent UTF-16 code units pair in a JavaScript string.
                            var (combined, ok) = ScanLowSurrogateEscape(codePoint);
                            if (ok)
                            {
                                return CodePointToString(combined);
                            }
                        }
                        else if ((flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0 &&
                            Char() == '\\' && CharAt(1) == 'u' && CharAt(2) != '{')
                        {
                            // In regex AnyUnicodeMode, combine \uHigh\uLow so scanClassRanges
                            // can compare the pair numerically. In non-unicode regex mode they
                            // are separate atoms, and extended \u{...} escapes never combine.
                            var savedPos = _pos;
                            var nextCodePoint = ScanUnicodeEscape((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0);
                            if (IsLowSurrogate(nextCodePoint))
                            {
                                return CodePointToString(SurrogatePairToCodePoint(codePoint, nextCodePoint));
                            }

                            _pos = savedPos;
                        }
                    }

                    // Lone surrogate: C# 的 string 直接存这个代码单元（Go 用 CESU-8 哨兵字符串，见文件头第 1 条）。
                    return CodePointToString(codePoint);
                }

            case 'x':
                // '\xDD'
                for (; _pos < start + 4; _pos++)
                {
                    if (!IsHexDigit(Char()))
                    {
                        _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                        if ((flags & EscapeSequenceScanningFlags.ReportInvalidEscapeErrors) != 0)
                        {
                            Error(Hexadecimal_digit_expected);
                        }

                        return _text[start.._pos];
                    }
                }

                _tokenFlags |= TokenFlags.HexEscape;
                return CodePointToString(ParseHexInt32(_text, start + 2, _pos));
            case '\r':
                // when encountering a LineContinuation (i.e. a backslash and a line terminator sequence),
                // the line terminator is interpreted to be "the empty code unit sequence".
                if (Char() == '\n')
                {
                    _pos++;
                }

                goto case '\n';
            case '\n':
                return "";
            default:
                // ch was read as a single code unit; for a non-BMP code point (surrogate pair),
                // we need to decode the full code point and advance past both code units.
                if (ch >= 0x80)
                {
                    _pos--; // back up past the single-code-unit advance
                    ch = DecodeRuneAt(_text, _pos, out var size);
                    _pos += size;
                }

                // LineContinuation: a backslash followed by a line terminator is "the empty code unit sequence".
                if (ch == 0x2028 || ch == 0x2029)
                {
                    return "";
                }

                if ((flags & EscapeSequenceScanningFlags.AnyUnicodeMode) != 0 ||
                    (flags & EscapeSequenceScanningFlags.RegularExpression) != 0 && (flags & EscapeSequenceScanningFlags.AnnexB) == 0 && IsIdentifierPart(ch))
                {
                    ErrorAt(This_character_cannot_be_escaped_in_a_regular_expression, start, _pos - start);
                }

                return CodePointToString(ch);
        }
    }

    // Known to be at \u
    private int ScanUnicodeEscape(bool shouldEmitInvalidEscapeError)
    {
        _pos += 2;
        var start = _pos;
        var extended = Char() == '{';
        string hexDigits;
        if (extended)
        {
            _pos++;
            hexDigits = ScanHexDigits(1, true, false);
        }
        else
        {
            _tokenFlags |= TokenFlags.UnicodeEscape;
            hexDigits = ScanHexDigits(4, false, false);
        }

        if (hexDigits == "")
        {
            _tokenFlags |= TokenFlags.ContainsInvalidEscape;
            if (shouldEmitInvalidEscapeError)
            {
                Error(Hexadecimal_digit_expected);
            }

            return -1;
        }

        var hexValue = ParseHexInt32(hexDigits);
        if (extended)
        {
            var isInvalidExtendedEscape = false;
            if (hexValue > 0x10FFFF)
            {
                if (shouldEmitInvalidEscapeError)
                {
                    ErrorAt(An_extended_Unicode_escape_value_must_be_between_0x0_and_0x10FFFF_inclusive, start + 1, _pos - start - 1);
                }

                isInvalidExtendedEscape = true;
            }

            if (_pos >= _end)
            {
                if (shouldEmitInvalidEscapeError)
                {
                    Error(Unexpected_end_of_text);
                }

                isInvalidExtendedEscape = true;
            }
            else if (Char() == '}')
            {
                _pos++;
            }
            else
            {
                if (shouldEmitInvalidEscapeError)
                {
                    Error(Unterminated_Unicode_escape_sequence);
                }

                isInvalidExtendedEscape = true;
            }

            if (isInvalidExtendedEscape)
            {
                _tokenFlags |= TokenFlags.ContainsInvalidEscape;
                return -1;
            }

            _tokenFlags |= TokenFlags.ExtendedUnicodeEscape;
        }

        return hexValue;
    }

    // scanLowSurrogateEscape attempts to consume a low-surrogate Unicode escape
    // (either '\uLow' or '\u{Low}') immediately following an already-scanned high
    // surrogate and combine them into a single supplementary code point. This
    // mirrors how adjacent UTF-16 code units form a surrogate pair in a JavaScript
    // string, regardless of which escape syntax produced each half. On success it
    // returns the combined code point and true; otherwise it restores the scanner
    // position and returns false.
    private (int Value, bool Ok) ScanLowSurrogateEscape(int high)
    {
        if (Char() != '\\' || CharAt(1) != 'u')
        {
            return (0, false);
        }

        var savedPos = _pos;
        var savedTokenFlags = _tokenFlags;
        // Speculatively scan the escape with diagnostics suppressed: if it isn't a
        // low surrogate we rewind below, and the caller re-scans the same escape and
        // reports any error then, so reporting here would duplicate diagnostics.
        var low = ScanUnicodeEscape(false);
        if (IsLowSurrogate(low))
        {
            return (SurrogatePairToCodePoint(high, low), true);
        }

        _pos = savedPos;
        _tokenFlags = savedTokenFlags;
        return (0, false);
    }

    // Current character is known to be a backslash. Check for Unicode escape of the form '\uXXXX'
    // or '\u{XXXXXX}' and return code point value if valid Unicode escape is found. Otherwise return -1.
    private int PeekUnicodeEscape()
    {
        if (CharAt(1) == 'u')
        {
            var savePos = _pos;
            var saveTokenFlags = _tokenFlags;
            var codePoint = ScanUnicodeEscape(false);
            _pos = savePos;
            _tokenFlags = saveTokenFlags;
            return codePoint;
        }

        return -1;
    }
}
