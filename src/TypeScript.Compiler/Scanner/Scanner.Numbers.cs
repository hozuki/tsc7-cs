// ported from: internal/scanner/scanner.go:1943-2212（scanNumber / scanNumberFragment / scanDigits / scanHexDigits /
//               scanBinaryOrOctalDigits / scanBigIntSuffix / scanInvalidCharacter）
//
// 与 Go 的偏离（逐条）：
// - 三个缓存 `map[string]string` → `Dictionary<string, string>`（序号比较器）。Go 里 key 是字节串**视图**（切
//   字符串不分配），C# 里 key 必须是 string：`scanHexDigits` 本来就要把 digits 当字符串返回，所以缓存查找
//   直接用那个字符串（不额外造 key），只有 miss 时多一次 `Replace`/`ToLowerJS` 的分配。
// - `strconv.ParseInt(digits, 8, 64)`（越界 → MaxInt64 + ErrRange，Go 调用点忽略错误）→ 手写 `ParseOctalInt64`
//   做同样的钳制；`strconv.FormatInt(v, 8|10)` → `FormatOctal`（手写，C# 没有内置八进制格式化）与
//   `ToString(CultureInfo.InvariantCulture)`（Go 的数字转字符串与区域无关）。
// - `strings.ReplaceAll(digits, "_", "")` → `string.Replace`；`strings.ToLower` → `ToLowerJS`（docs §3.4 的映射）。
// - `jsnum.FromString(v).String()` → `FromString(v).String()`（`using static TypeScript.JsNum.JsNum`）。
// - Go 里 `if tokenValue == s.tokenValue { tokenValue = s.tokenValue }` 是恒等式（照抄以保持逐行对应）。
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.JsNum.JsNum;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

public sealed partial class Scanner
{
    private Kind ScanNumber()
    {
        var start = _pos;
        string fixedPart;
        if (Char() == '0')
        {
            _pos++;
            if (Char() == '_')
            {
                _tokenFlags |= TokenFlags.ContainsSeparator | TokenFlags.ContainsInvalidSeparator;
                ErrorAt(Numeric_separators_are_not_allowed_here, _pos, 1);
                _pos = start;
                fixedPart = ScanNumberFragment();
            }
            else
            {
                var (digits, isOctal) = ScanDigits();
                if (digits == "")
                {
                    fixedPart = "0";
                }
                else if (!isOctal)
                {
                    _tokenFlags |= TokenFlags.ContainsLeadingZero;
                    fixedPart = digits;
                }
                else
                {
                    var val = ParseOctalInt64(digits);
                    _tokenValue = val.ToString(CultureInfo.InvariantCulture);
                    _tokenFlags |= TokenFlags.Octal;
                    var withMinus = _token == Kind.MinusToken;
                    var literal = IfElse(withMinus, "-", "") + "0o" + FormatOctal(val);
                    if (withMinus)
                    {
                        start--;
                    }

                    ErrorAt(Octal_literals_are_not_allowed_Use_the_syntax_0, start, _pos - start, literal);
                    return Kind.NumericLiteral;
                }
            }
        }
        else
        {
            fixedPart = ScanNumberFragment();
        }

        var fixedPartEnd = _pos;
        var fractionalPart = "";
        var exponentPreamble = "";
        var exponentPart = "";
        if (Char() == '.')
        {
            _pos++;
            fractionalPart = ScanNumberFragment();
        }

        var end = _pos;
        if (Char() == 'E' || Char() == 'e')
        {
            _pos++;
            _tokenFlags |= TokenFlags.Scientific;
            if (Char() == '+' || Char() == '-')
            {
                _pos++;
            }

            var startNumericPart = _pos;
            exponentPart = ScanNumberFragment();
            if (exponentPart == "")
            {
                Error(Digit_expected);
            }
            else
            {
                exponentPreamble = _text[end..startNumericPart];
                end = _pos;
            }
        }

        if ((_tokenFlags & TokenFlags.ContainsSeparator) != 0)
        {
            _tokenValue = fixedPart;
            if (fractionalPart != "")
            {
                _tokenValue += "." + fractionalPart;
            }

            if (exponentPart != "")
            {
                _tokenValue += exponentPreamble + exponentPart;
            }
        }
        else
        {
            _tokenValue = _text[start..end];
        }

        if ((_tokenFlags & TokenFlags.ContainsLeadingZero) != 0)
        {
            ErrorAt(Decimals_with_leading_zeros_are_not_allowed, start, _pos - start);
            _tokenValue = FromString(_tokenValue).String();
            return Kind.NumericLiteral;
        }

        Kind result;
        if (fixedPartEnd == _pos)
        {
            result = ScanBigIntSuffix();
        }
        else
        {
            _tokenValue = FromString(_tokenValue).String();
            result = Kind.NumericLiteral;
        }

        var (ch, _) = CharAndSize();
        if (IsIdentifierStart(ch))
        {
            var idStart = _pos;
            var id = ScanIdentifierParts(IdentifierVariant.Standard);
            if (result != Kind.BigIntLiteral && id.Length == 1 && _text[idStart] == 'n')
            {
                if ((_tokenFlags & TokenFlags.Scientific) != 0)
                {
                    ErrorAt(A_bigint_literal_cannot_use_exponential_notation, start, _pos - start);
                    return result;
                }

                if (fixedPartEnd < idStart)
                {
                    ErrorAt(A_bigint_literal_must_be_an_integer, start, _pos - start);
                    return result;
                }
            }

            ErrorAt(An_identifier_or_keyword_cannot_immediately_follow_a_numeric_literal, idStart, _pos - idStart);
            _pos = idStart;
        }

        return result;
    }

    private string ScanNumberFragment()
    {
        var start = _pos;
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var before = _pos;
            ScanASCIIWhile(default(DecimalDigitPredicate));
            if (_pos > before)
            {
                allowSeparator = true;
                isPreviousTokenSeparator = false;
            }

            var ch = Char();
            if (ch == '_')
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                    result.Append(_text, start, _pos - start);
                }
                else
                {
                    _tokenFlags |= TokenFlags.ContainsInvalidSeparator;
                    if (isPreviousTokenSeparator)
                    {
                        ErrorAt(Multiple_consecutive_numeric_separators_are_not_permitted, _pos, 1);
                    }
                    else
                    {
                        ErrorAt(Numeric_separators_are_not_allowed_here, _pos, 1);
                    }
                }

                _pos++;
                start = _pos;
                continue;
            }

            break;
        }

        if (isPreviousTokenSeparator)
        {
            _tokenFlags |= TokenFlags.ContainsInvalidSeparator;
            ErrorAt(Numeric_separators_are_not_allowed_here, _pos - 1, 1);
        }

        if (result.Length == 0)
        {
            return _text[start.._pos];
        }

        result.Append(_text, start, _pos - start);
        return result.ToString();
    }

    private (string Digits, bool IsOctal) ScanDigits()
    {
        var start = _pos;
        var isOctal = true;
        while (IsDigit(Char()))
        {
            if (!IsOctalDigit(Char()))
            {
                isOctal = false;
            }

            _pos++;
        }

        return (_text[start.._pos], isOctal);
    }

    private string ScanHexDigits(int minCount, bool scanAsManyAsPossible, bool canHaveSeparators)
    {
        var digitCount = 0;
        var start = _pos;
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        while (digitCount < minCount || scanAsManyAsPossible)
        {
            var ch = Char();
            if (IsHexDigit(ch))
            {
                allowSeparator = canHaveSeparators;
                isPreviousTokenSeparator = false;
                digitCount++;
            }
            else if (canHaveSeparators && ch == '_')
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    ErrorAt(Multiple_consecutive_numeric_separators_are_not_permitted, _pos, 1);
                }
                else
                {
                    ErrorAt(Numeric_separators_are_not_allowed_here, _pos, 1);
                }
            }
            else
            {
                break;
            }

            _pos++;
        }

        if (isPreviousTokenSeparator)
        {
            ErrorAt(Numeric_separators_are_not_allowed_here, _pos - 1, 1);
        }

        if (digitCount < minCount)
        {
            return "";
        }

        var digits = _text[start.._pos];
        if (_hexDigitCache.TryGetValue(digits, out var cached))
        {
            return cached;
        }

        var original = digits;
        if ((_tokenFlags & TokenFlags.ContainsSeparator) != 0)
        {
            digits = digits.Replace("_", "");
        }

        digits = ToLowerJS(digits); // standardize hex literals to lowercase
        _hexDigitCache[original] = digits;
        return digits;
    }

    private string ScanBinaryOrOctalDigits(int @base)
    {
        var sb = new System.Text.StringBuilder();
        var allowSeparator = false;
        var isPreviousTokenSeparator = false;
        while (true)
        {
            var ch = Char();
            if (IsDigit(ch) && ch - '0' < @base)
            {
                sb.Append((char)ch);
                allowSeparator = true;
                isPreviousTokenSeparator = false;
            }
            else if (ch == '_')
            {
                _tokenFlags |= TokenFlags.ContainsSeparator;
                if (allowSeparator)
                {
                    allowSeparator = false;
                    isPreviousTokenSeparator = true;
                }
                else if (isPreviousTokenSeparator)
                {
                    ErrorAt(Multiple_consecutive_numeric_separators_are_not_permitted, _pos, 1);
                }
                else
                {
                    ErrorAt(Numeric_separators_are_not_allowed_here, _pos, 1);
                }
            }
            else
            {
                break;
            }

            _pos++;
        }

        if (isPreviousTokenSeparator)
        {
            ErrorAt(Numeric_separators_are_not_allowed_here, _pos - 1, 1);
        }

        return sb.ToString();
    }

    private Kind ScanBigIntSuffix()
    {
        if (Char() == 'n')
        {
            _tokenValue += "n";
            if ((_tokenFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
            {
                _tokenValue = ParsePseudoBigInt(_tokenValue) + "n";
            }

            _pos++;
            return Kind.BigIntLiteral;
        }

        if (_numberCache.TryGetValue(_tokenValue, out var cached))
        {
            _tokenValue = cached;
        }
        else
        {
            var key = _tokenValue;
            var tokenValue = FromString(_tokenValue).String();
            // Go 里这一行是恒等式（tokenValue 必然等于 s.tokenValue，因为 FromString 的结果就是它自己）；
            // 照抄以保持与 Go 逐行对应。
            if (tokenValue == _tokenValue)
            {
                tokenValue = _tokenValue;
            }

            _numberCache[key] = tokenValue;
            _tokenValue = tokenValue;
        }

        return Kind.NumericLiteral;
    }

    private void ScanInvalidCharacter()
    {
        var (_, size) = CharAndSize();
        ErrorAt(Invalid_character, _pos, size);
        _pos += size;
        _token = Kind.Unknown;
    }

    // Go: strconv.ParseInt(digits, 8, 64)（越界时返回 MaxInt64 并带 ErrRange，调用点忽略错误）。
    private static long ParseOctalInt64(string digits)
    {
        long value = 0;
        foreach (var ch in digits)
        {
            var digit = ch - '0';
            if (value > (long.MaxValue - digit) / 8)
            {
                return long.MaxValue;
            }

            value = (value * 8) + digit;
        }

        return value;
    }

    // Go: strconv.FormatInt(value, 8)（value >= 0，这里只为 legacy octal 字面量构造诊断参数）。
    private static string FormatOctal(long value)
    {
        if (value == 0)
        {
            return "0";
        }

        var buffer = new char[22]; // long.MaxValue 的八进制表示最多 21 位
        var i = buffer.Length;
        while (value > 0)
        {
            buffer[--i] = (char)('0' + (int)(value % 8));
            value /= 8;
        }

        return new string(buffer, i, buffer.Length - i);
    }
}
