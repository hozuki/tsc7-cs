// C# 侧新增：float → JSON 数字文本，等价于 Go json/v2 对浮点的编码（docs/json.md §1.2）。
//
// Go 侧是 strconv.AppendFloat 的最短往返表示，但“用 'f' 还是 'e'”由 json 决定：
//   abs != 0 && (abs < 1e-6 || abs >= 1e21)  → 指数形式（小写 e、带正负号、指数不补零）
//   否则                                      → 十进制形式
// 实测：-2.109808898695963e16 → -21098088986959630、1e21 → 1e+21、1e-7 → 1e-7、5e-324 → 5e-324、
//       2.2250738585072014e-308 原样、520.2 原样、0 → 0、1e20 → 100000000000000000000。
//
// .NET 的 "R" 也是“最短且可往返”，但表示形式不同（大写 E、指数至少两位、切换点在别处），
// 所以这里只从它那里取数字串，再按 Go 的规则重排。取数字串的位置来自 TSGO 自己的测试向量
//（internal/jsnum/ryu_test.go 的期望值就是 json.Marshal(float64) 的输出）。
using System.Globalization;

namespace TypeScript.Json;

internal static class FloatFormat
{
    private const string _hexDigits = "0123456789abcdef";

    // Go 的 strconv.FormatFloat(f, 'f'|'e', -1, 64) + json 的 'f'/'e' 选择。
    internal static string Format(double value)
    {
        if (!double.IsFinite(value))
        {
            // Go 的 json/v2 对 NaN/±Inf 返回错误（不是 null）；调用方必须先分派（见 internal/jsnum/jsnum.go）。
            throw new JsonException($"json: unsupported value: {FormatNonFinite(value)}");
        }

        return Reformat(value.ToString("R", CultureInfo.InvariantCulture));
    }

    // Go 侧 float32 用位宽 32 的最短表示（strconv.FormatFloat(f, fmt, -1, 32)）。
    internal static string Format(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new JsonException($"json: unsupported value: {FormatNonFinite(value)}");
        }

        return Reformat(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static string FormatNonFinite(double value)
    {
        return double.IsNaN(value) ? "NaN" : value > 0 ? "+Inf" : "-Inf";
    }

    // 把“最短可往返”的十进制文本重排成 Go 的形状。
    // text 的形式是 C# "R" 的输出：[-]digits[.digits][E±digits]，比如 "520.2"、"-21098088986959630"、"1E+21"。
    private static string Reformat(string text)
    {
        Span<char> digits = stackalloc char[32];
        var digitCount = 0;
        var pointPosition = 0; // 小数点前的数字个数（尾数部分）
        var seenPoint = false;
        var exponent = 0;
        var negative = text[0] == '-';
        var index = negative ? 1 : 0;

        for (; index < text.Length; index++)
        {
            var c = text[index];
            if (c == '.')
            {
                seenPoint = true;
            }
            else if (c is 'E' or 'e')
            {
                exponent = int.Parse(text.AsSpan(index + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                break;
            }
            else
            {
                digits[digitCount++] = c;
                if (!seenPoint)
                {
                    pointPosition++;
                }
            }
        }

        // 去掉前导零（"0.000001" 的 digits 是 "0000001"、pointPosition 是 1），
        // 第一个有效数字的位权就是 exp10。
        var first = 0;
        while (first < digitCount && digits[first] == '0')
        {
            first++;
        }

        if (first == digitCount)
        {
            // ±0（Go：0.0 → "0"、-0.0 → "-0"）。
            return negative ? "-0" : "0";
        }

        var exp10 = pointPosition - first - 1 + exponent;

        // 去掉末尾零（"R" 不会产生，防御性处理；十进制形式里这些零由补零逻辑重新产生）。
        while (digitCount > first + 1 && digits[digitCount - 1] == '0')
        {
            digitCount--;
        }

        var significant = digits.Slice(first, digitCount - first);
        Span<char> buffer = stackalloc char[32];
        var length = 0;

        if (negative)
        {
            buffer[length++] = '-';
        }

        if (exp10 < -6 || exp10 >= 21)
        {
            // 指数形式：d1[.dN]e±NN（小写 e、指数不留前导零）
            buffer[length++] = significant[0];
            if (significant.Length > 1)
            {
                buffer[length++] = '.';
                significant[1..].CopyTo(buffer[length..]);
                length += significant.Length - 1;
            }

            buffer[length++] = 'e';
            buffer[length++] = exp10 >= 0 ? '+' : '-';
            length += FormatExponent(exp10 >= 0 ? exp10 : -exp10, buffer[length..]);
        }
        else
        {
            // 十进制形式：把小数点按 exp10 放到 digits 里
            var integerDigits = exp10 + 1;
            if (integerDigits <= 0)
            {
                buffer[length++] = '0';
                buffer[length++] = '.';
                for (var i = 0; i < -integerDigits; i++)
                {
                    buffer[length++] = '0';
                }

                significant.CopyTo(buffer[length..]);
                length += significant.Length;
            }
            else if (integerDigits >= significant.Length)
            {
                significant.CopyTo(buffer[length..]);
                length += significant.Length;
                for (var i = 0; i < integerDigits - significant.Length; i++)
                {
                    buffer[length++] = '0';
                }
            }
            else
            {
                significant[..integerDigits].CopyTo(buffer[length..]);
                length += integerDigits;
                buffer[length++] = '.';
                significant[integerDigits..].CopyTo(buffer[length..]);
                length += significant.Length - integerDigits;
            }
        }

        return new string(buffer[..length]);
    }

    private static int FormatExponent(int exponent, Span<char> destination)
    {
        Span<char> reverse = stackalloc char[4];
        var count = 0;
        do
        {
            reverse[count++] = _hexDigits[exponent % 10];
            exponent /= 10;
        }
        while (exponent > 0);

        for (var i = 0; i < count; i++)
        {
            destination[i] = reverse[count - 1 - i];
        }

        return count;
    }
}
