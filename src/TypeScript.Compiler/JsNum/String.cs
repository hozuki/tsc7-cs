// ported from: internal/jsnum/string.go
//
// 与 Go 不一致的地方（除 JsNum.cs 文件头列的 Number 形状差异外）：
//
// 1) Go 的 `func (n Number) String() string` 是 stringer，C# 侧是 `Number.ToString()` 覆写
//    （docs/architecture.md §3.2），调用点仍可写 `n.String()`（JsNum.cs 的扩展方法）。
// 2) Go 的 `for _, r := range s` / `strings.TrimFunc` 按 UTF-8 码点走；C# 的 string 是 UTF-16 代码
//    单元序列，这里用 `s.EnumerateRunes()`（= Go 的 range 语义：非法序列给出 U+FFFD）和
//    Rune.DecodeFromUtf16/DecodeLastFromUtf16（StringUtil 里已有同款注释）。jsnum 里涉及的全是 ASCII
//    （isNumberRune 只放行 ASCII），所以两者对任何能走到这里的输入都一致。
// 3) `strconv.ParseInt(s, base, 64)` → `Convert.ToInt64(s, base)`（基 2/8/16）/`long.TryParse`
//    （基 10）；Go 在**溢出**时返回 ErrRange 并落到 big.Int 分支，C# 用 OverflowException/TryParse
//    返回 false 复刻同一个回退。
// 4) `new(big.Int).SetString(s, 0)` → BigInteger，但 .NET 的 BigInteger 不认 0b/0o 前缀、不认 Go 的
//    下划线分隔符，所以新增了 TryParseBigIntBase0（下划线规则逐条照抄 Go 的 math/big underscoreOK）。
// 5) `bi.Float64()` → `(double)bigInteger`；Go 在超出 float64 范围时返回 ±Inf，.NET 抛
//    OverflowException 时按同一语义转换（见 BigIntegerToFloat64）。
using System.Globalization;
using System.Numerics;
using System.Text;
using static TypeScript.Json.Json;
using static TypeScript.JsNum.JsNum;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.JsNum;

public readonly partial struct Number
{
    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-tostring
    public override string ToString()
    {
        if (IsNaN())
        {
            return "NaN";
        }

        if (IsInf())
        {
            if (Value < 0)
            {
                return "-Infinity";
            }

            return "Infinity";
        }

        // Fast path: for safe integers, directly convert to string.
        if (MinSafeInteger <= this && this <= MaxSafeInteger)
        {
            // Go: i := int64(n); float64(i) == float64(n)
            var i = (long)Value;
            if ((double)i == Value)
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }
        }

        // Otherwise, the Go json package handles this correctly.
        // Go: b, _ := json.Marshal(float64(n)); return string(b)
        return Encoding.UTF8.GetString(Marshal(Value));
    }
}

public static partial class JsNum
{
    // https://tc39.es/ecma262/2024/multipage/abstract-operations.html#sec-stringtonumber
    public static Number FromString(string s)
    {
        // Implementing StringToNumber exactly as written in the spec involves
        // writing a parser, along with the conversion of the parsed AST into the
        // actual value.
        //
        // We've already implemented a number parser in the scanner, but we can't
        // import it here. We also do not have the conversion implemented since we
        // previously just wrote `+literal` and let the runtime handle it.
        //
        // The strategy below is to instead break the number apart and fix it up
        // such that Go's own parsing functionality can handle it. This won't be
        // the fastest method, but it saves us from writing the full parser and
        // conversion logic.

        s = TrimWhitespace(s);

        switch (s)
        {
            case "":
                return (Number)0;
            case "Infinity":
            case "+Infinity":
                return Inf(1);
            case "-Infinity":
                return Inf(-1);
        }

        foreach (var r in s.EnumerateRunes())
        {
            if (!IsNumberRune(r.Value))
            {
                return NaN();
            }
        }

        if (TryParseInt(s, out var n))
        {
            return n;
        }

        // Cut this off first so we can ensure -0 is returned as -0.
        var negative = s.StartsWith('-');
        if (!negative)
        {
            s = s.StartsWith('+') ? s[1..] : s;
        }
        else
        {
            s = s[1..];
        }

        // Go: first, _ := utf8.DecodeRuneInString(s)（空串给出 RuneError）——空串不是数字也不是 '.'。
        var first = s.Length == 0 ? -1 : s[0];
        if (!IsDigit(first) && first != '.')
        {
            return NaN();
        }

        var f = ParseFloatString(s);
        if (double.IsNaN(f))
        {
            return NaN();
        }

        var sign = negative ? -1.0 : 1.0;
        return (Number)Math.CopySign(f, sign);
    }

    internal static bool IsStrWhiteSpace(int r)
    {
        // This is different than stringutil.IsWhiteSpaceLike.

        // https://tc39.es/ecma262/2024/multipage/ecmascript-language-lexical-grammar.html#prod-LineTerminator
        // https://tc39.es/ecma262/2024/multipage/ecmascript-language-lexical-grammar.html#prod-WhiteSpace

        switch (r)
        {
            // LineTerminator
            case '\n':
            case '\r':
            case 0x2028:
            case 0x2029:
                return true;
            // WhiteSpace
            case '\t':
            case '\v':
            case '\f':
            case 0xFEFF:
                return true;
        }

        // WhiteSpace
        // Go: unicode.Is(unicode.Zs, r) —— Zs 全是 BMP 字符，CharUnicodeInfo.GetUnicodeCategory(int)
        // 对孤立代理项给出 Surrogate（不是 SpaceSeparator），与 Go 的判定一致。
        return CharUnicodeInfo.GetUnicodeCategory(r) == UnicodeCategory.SpaceSeparator;
    }

    // Go: strings.TrimFunc(s, isStrWhiteSpace)；按码点从两端裁剪（等价于 Go 的 rune 语义）。
    internal static string TrimWhitespace(string s)
    {
        var start = 0;
        var end = s.Length;

        while (start < end)
        {
            _ = Rune.DecodeFromUtf16(s.AsSpan(start, end - start), out var rune, out var size);
            if (!IsStrWhiteSpace(rune.Value))
            {
                break;
            }

            start += size;
        }

        while (end > start)
        {
            _ = Rune.DecodeLastFromUtf16(s.AsSpan(start, end - start), out var rune, out var size);
            if (!IsStrWhiteSpace(rune.Value))
            {
                break;
            }

            end -= size;
        }

        return s[start..end];
    }

    // Go: var errUnknownPrefix = errors.New("unknown number prefix")
    //
    // Go 里声明后从未被使用（tryParseInt 的三个分支都不用它）；C# 侧保留同名对应物以免将来同步 Go 时
    // 丢失这个符号。error 值在 C# 里的对应物是消息字符串（这里没有任何抛出点），所以类型是 string。
    internal static readonly string errUnknownPrefix = "unknown number prefix";

    internal static bool TryParseInt(string s, out Number value)
    {
        var i = 0L;
        var failed = false;
        var hasIntResult = false;

        if (s.Length > 2)
        {
            var prefix = s[..2];
            var rest = s[2..];
            switch (prefix)
            {
                case "0b":
                case "0B":
                    if (!IsAllBinaryDigits(rest))
                    {
                        value = NaN();
                        return true;
                    }

                    hasIntResult = true;
                    failed = !TryParseIntRadix(rest, 2, out i);
                    break;
                case "0o":
                case "0O":
                    if (!IsAllOctalDigits(rest))
                    {
                        value = NaN();
                        return true;
                    }

                    hasIntResult = true;
                    failed = !TryParseIntRadix(rest, 8, out i);
                    break;
                case "0x":
                case "0X":
                    if (!IsAllHexDigits(rest))
                    {
                        value = NaN();
                        return true;
                    }

                    hasIntResult = true;
                    failed = !TryParseIntRadix(rest, 16, out i);
                    break;
            }
        }

        if (!hasIntResult)
        {
            // StringToNumber does not parse leading zeros as octal.
            s = TrimLeadingZeros(s);
            if (!IsAllDigits(s))
            {
                value = (Number)0;
                return false;
            }

            hasIntResult = true;
            failed = !long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out i);
        }

        if (hasIntResult && !failed)
        {
            value = (Number)i;
            return true;
        }

        // Using this to parse large integers.
        if (!TryParseBigIntBase0(s, out var bi))
        {
            value = NaN();
            return true;
        }

        value = (Number)BigIntegerToFloat64(bi);
        return true;
    }

    // Go: strconv.ParseInt(s, base, 64)；返回 false 表示 ErrRange（溢出），调用方据此走 big.Int 分支。
    //
    // 不能用 Convert.ToInt64：它把基 2/8/16 的串当**无符号 64 位**解析后重新解释符号位
    //（实测 Convert.ToInt64("FFFFFFFFFFFFFFFF", 16) == -1、Convert.ToInt64("8000000000000000", 16)
    // == long.MinValue），而 Go 的 ParseInt 对 > MaxInt64 一律返回 ErrRange。这里先按 ulong 解析
    //（越界 → OverflowException，等于 Go 的 ErrRange），再判是否超过 MaxInt64。
    private static bool TryParseIntRadix(string s, int numberBase, out long value)
    {
        try
        {
            var result = Convert.ToUInt64(s, numberBase);
            if (result > long.MaxValue)
            {
                value = 0;
                return false;
            }

            value = (long)result;
            return true;
        }
        catch (OverflowException)
        {
            value = 0;
            return false;
        }
    }

    // Go: (*big.Int).Float64() —— 超出 float64 范围时返回 ±Inf，不 panic。
    // 实测 .NET 的 `(double)BigInteger` 在 net11.0 上同样返回 ±Inf（10^400 → +Inf）且是正确舍入
    // （10^308/5^210/10^200 的位模式与 Go 侧测试向量一致，见 artifacts/jsnum-sem）；catch 只是
    // 为"若某个目标框架改为抛 OverflowException"保底复刻 Go 的 ±Inf 语义。
    internal static double BigIntegerToFloat64(BigInteger value)
    {
        try
        {
            return (double)value;
        }
        catch (OverflowException)
        {
            return value.Sign < 0 ? double.NegativeInfinity : double.PositiveInfinity;
        }
    }

    internal static double ParseFloatString(string s)
    {
        var hasDot = false;
        var hasExp = false;

        // <a>
        // <a>.<b>
        // <a>.<b>e<c>
        // <a>e<c>
        string a, b, c, rest;

        var dotIndex = s.IndexOf('.');
        if (dotIndex >= 0)
        {
            // <a>.<b>
            // <a>.<b>e<c>
            a = s[..dotIndex];
            rest = s[(dotIndex + 1)..];
            hasDot = true;
            hasExp = CutAny(rest, "eE", out b, out c);
        }
        else
        {
            // <a>
            // <a>e<c>
            b = "";
            hasExp = CutAny(s, "eE", out a, out c);
        }

        var sb = new StringBuilder(a.Length + b.Length + c.Length + 3);

        if (a.Length == 0)
        {
            if (hasDot && b.Length == 0)
            {
                return double.NaN;
            }

            if (hasExp && c.Length == 0)
            {
                return double.NaN;
            }

            sb.Append('0');
        }
        else
        {
            a = TrimLeadingZeros(a);
            if (!IsAllDigits(a))
            {
                return double.NaN;
            }

            sb.Append(a);
        }

        if (hasDot)
        {
            sb.Append('.');
            if (b.Length == 0)
            {
                sb.Append('0');
            }
            else
            {
                b = TrimTrailingZeros(b);
                if (!IsAllDigits(b))
                {
                    return double.NaN;
                }

                sb.Append(b);
            }
        }

        if (hasExp)
        {
            sb.Append('e');

            var negative = c.StartsWith('-');
            if (negative)
            {
                c = c[1..];
                sb.Append('-');
            }
            else
            {
                c = c.StartsWith('+') ? c[1..] : c;
            }

            c = TrimLeadingZeros(c);
            if (!IsAllDigits(c))
            {
                return double.NaN;
            }

            sb.Append(c);
        }

        return StringToFloat64(sb.ToString());
    }

    // Go: strings.Cut(s, cutset) 的 "Any" 版本。调用点只用 "eE"，且走到这里时 s 必定是 ASCII
    //（FromString 已用 isNumberRune 过滤），所以直接按代码单元找第一个命中项、跳过 1 个单元即可
    //（Go 是 utf8.DecodeRuneInString 跳过一整个码点）。
    private static bool CutAny(string s, string cutset, out string before, out string after)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (cutset.Contains(s[i]))
            {
                before = s[..i];
                after = s[(i + 1)..];
                return true;
            }
        }

        before = s;
        after = "";
        return false;
    }

    internal static string TrimLeadingZeros(string s)
    {
        if (s.StartsWith('0'))
        {
            s = s.TrimStart('0');
            if (s.Length == 0)
            {
                return "0";
            }
        }

        return s;
    }

    internal static string TrimTrailingZeros(string s)
    {
        if (s.EndsWith('0'))
        {
            s = s.TrimEnd('0');
            if (s.Length == 0)
            {
                return "0";
            }
        }

        return s;
    }

    internal static double StringToFloat64(string s)
    {
        // Go: strconv.ParseFloat(s, 64)：语法错误 → NaN，ErrRange（溢出）→ 返回 f（±Inf）。
        // .NET 的 double.TryParse 语法错误返回 false、溢出返回 ±Inf（.NET Core 3.0+ 不再抛
        // OverflowException），与 Go 的分工逐条对应。
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
        {
            return f;
        }

        return double.NaN;
    }

    internal static bool IsAllDigits(string s)
    {
        foreach (var ch in s)
        {
            if (!IsDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsAllBinaryDigits(string s)
    {
        foreach (var ch in s)
        {
            if (ch != '0' && ch != '1')
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsAllOctalDigits(string s)
    {
        foreach (var ch in s)
        {
            if (!IsOctalDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsAllHexDigits(string s)
    {
        foreach (var ch in s)
        {
            if (!IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsNumberRune(int r)
    {
        if (IsDigit(r))
        {
            return true;
        }

        if ('a' <= r && r <= 'f')
        {
            return true;
        }

        if ('A' <= r && r <= 'F')
        {
            return true;
        }

        switch (r)
        {
            case '.':
            case '-':
            case '+':
            case 'x':
            case 'X':
            case 'o':
            case 'O':
                return true;
        }

        return false;
    }

    // C# 侧新增（Go 侧是 math/big 的 `new(big.Int).SetString(s, 0)`）：base 0 = 0b/0B、0o/0O、0x/0X
    // 前缀，其余按十进制（前导 0 视为八进制，与 Go 的 base 0 一致）；下划线规则照抄 Go 的
    // math/big underscoreOK（只能出现在数字之间；紧跟在进制前缀之后也算合法）。
    internal static bool TryParseBigIntBase0(string s, out BigInteger value)
    {
        value = default;
        if (!UnderscoreOk(s))
        {
            return false;
        }

        var i = 0;
        var negative = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            negative = s[i] == '-';
            i++;
        }

        var numberBase = 10;
        if (i < s.Length && s[i] == '0')
        {
            if (i + 1 >= s.Length)
            {
                // "0" 本身。
                i++;
                return true;
            }

            switch (char.ToLowerInvariant(s[i + 1]))
            {
                case 'b':
                    numberBase = 2;
                    i += 2;
                    break;
                case 'o':
                    numberBase = 8;
                    i += 2;
                    break;
                case 'x':
                    numberBase = 16;
                    i += 2;
                    break;
                default:
                    // Go 的 base 0：前导 0（且后面不是进制前缀）按八进制解析。
                    numberBase = 8;
                    break;
            }
        }

        var any = false;
        for (; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '_')
            {
                continue;
            }

            var digit = DigitValue(ch);
            if (digit < 0 || digit >= numberBase)
            {
                return false;
            }

            value = value * numberBase + digit;
            any = true;
        }

        if (!any)
        {
            value = default;
            return false;
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }

    private static int DigitValue(char ch)
    {
        if (ch >= '0' && ch <= '9')
        {
            return ch - '0';
        }

        if (ch >= 'a' && ch <= 'z')
        {
            return ch - 'a' + 10;
        }

        if (ch >= 'A' && ch <= 'Z')
        {
            return ch - 'A' + 10;
        }

        return -1;
    }

    // Go: math/big 的 underscoreOK —— 下划线只能出现在数字之间（进制前缀后算作数字）。
    private static bool UnderscoreOk(string s)
    {
        var saw = '^';
        var i = 0;
        if (s.Length >= 1 && (s[0] == '-' || s[0] == '+'))
        {
            s = s[1..];
        }

        var hex = false;
        if (s.Length >= 2 && s[0] == '0' && (char.ToLowerInvariant(s[1]) == 'b' || char.ToLowerInvariant(s[1]) == 'o' || char.ToLowerInvariant(s[1]) == 'x'))
        {
            i = 2;
            saw = '0'; // base prefix counts as a digit for "underscore as digit separator"
            hex = char.ToLowerInvariant(s[1]) == 'x';
        }

        for (; i < s.Length; i++)
        {
            // Digits are always okay.
            if (s[i] >= '0' && s[i] <= '9' || hex && char.ToLowerInvariant(s[i]) >= 'a' && char.ToLowerInvariant(s[i]) <= 'f')
            {
                saw = '0';
                continue;
            }

            // Underscore must follow digit.
            if (s[i] == '_')
            {
                if (saw != '0')
                {
                    return false;
                }

                saw = '_';
                continue;
            }

            // Underscore must also be followed by digit.
            if (saw == '_')
            {
                return false;
            }

            // Saw non-digit, non-underscore.
            saw = '!';
        }

        return saw != '_';
    }
}
