// ported from: internal/jsnum/pseudobigint.go
//
// 偏离 Go 的地方：
//
// 1) Go 的 `PseudoBigInt` 是含 `string` 字段的可比较结构体；C# 的 `default(PseudoBigInt)` 里 string 字段是
//    null，而 Go 的零值是 ""。所以字段用 private + `Base10Value` 属性把 null 归一成 ""：`default` 与
//    `PseudoBigInt{}` 语义一致（Base10Value == ""、Sign() == 0、ToString() == "0"）。
// 2) 因为结构体含引用字段，Equals/GetHashCode 手写（value 语义），避免 ValueType.Equals 走反射
//    （docs/architecture.md §3.6 的 AOT 约束）；语义与 Go 的 `==`（逐字段比较）一致。
// 3) Go 的 `String()` stringer → `ToString()` 覆写，另给 `String(this PseudoBigInt)` 扩展方法保留
//    Go 的调用点写法（见 JsNum.cs 里的约定）。
// 4) Go 的 `new(big.Int).SetString(stringValue, 0)` → TryParseBigIntBase0（String.cs，同一实现，
//    支持 0b/0o/0x 前缀与 Go 的下划线规则）；失败时 Go `panic` → `Debug.Fail`。
using System.Numerics;

namespace TypeScript.JsNum;

/// <summary>
/// PseudoBigInt represents a JS-like bigint. The zero state of the struct represents the value 0.
/// </summary>
public readonly struct PseudoBigInt : IEquatable<PseudoBigInt>
{
    // true if the value is a non-zero negative number.
    public readonly bool Negative;

    // The absolute value in base 10 with no leading zeros. The value zero is represented as an empty string.
    // Go 里是导出字段；C# 用属性归一 null → ""（见文件头第 1 条）。
    private readonly string? _base10Value;

    internal PseudoBigInt(bool negative, string? base10Value)
    {
        Negative = negative;
        this._base10Value = base10Value;
    }

    public string Base10Value => _base10Value ?? "";

    public override string ToString()
    {
        if (Base10Value.Length == 0)
        {
            return "0";
        }

        if (Negative)
        {
            return "-" + Base10Value;
        }

        return Base10Value;
    }

    public int Sign()
    {
        if (Base10Value.Length == 0)
        {
            return 0;
        }

        if (Negative)
        {
            return -1;
        }

        return 1;
    }

    public int Compare(PseudoBigInt other)
    {
        if (Sign() != other.Sign())
        {
            // Go: cmp.Compare(value.Sign(), other.Sign())
            return Sign() < other.Sign() ? -1 : 1;
        }

        var c = Base10Value.Length == other.Base10Value.Length ? 0 : Base10Value.Length < other.Base10Value.Length ? -1 : 1;
        if (c == 0)
        {
            // Go: strings.Compare —— 这里两侧都只有 ASCII 数字，序数比较的符号与 Go 的字节比较一致。
            c = Math.Sign(string.CompareOrdinal(Base10Value, other.Base10Value));
        }

        if (Negative)
        {
            c = -c;
        }

        return c;
    }

    // Go 的 `==` / map key 语义（逐字段比较）。
    public bool Equals(PseudoBigInt other) => Negative == other.Negative && Base10Value == other.Base10Value;

    public override bool Equals(object? obj) => obj is PseudoBigInt other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Negative, Base10Value);

    public static bool operator ==(PseudoBigInt left, PseudoBigInt right) => left.Equals(right);

    public static bool operator !=(PseudoBigInt left, PseudoBigInt right) => !left.Equals(right);
}

public static partial class JsNum
{
    public static PseudoBigInt NewPseudoBigInt(string value, bool negative)
    {
        value = value.TrimStart('0');
        return new PseudoBigInt(negative && value.Length != 0, value);
    }

    // Go: `String() string` stringer → ToString()（见文件头第 3 条）。
    public static string String(this PseudoBigInt value)
    {
        return value.ToString();
    }

    public static PseudoBigInt ParseValidBigInt(string text)
    {
        var negative = text.StartsWith('-');
        if (negative)
        {
            text = text[1..];
        }

        return NewPseudoBigInt(ParsePseudoBigInt(text), negative);
    }

    public static string ParsePseudoBigInt(string stringValue)
    {
        // Go: strings.TrimSuffix(stringValue, "n")
        if (stringValue.EndsWith('n'))
        {
            stringValue = stringValue[..^1];
        }

        var b1 = '\0';
        if (stringValue.Length > 1)
        {
            b1 = stringValue[1];
        }

        switch (b1)
        {
            case 'b':
            case 'B':
            case 'o':
            case 'O':
            case 'x':
            case 'X':
                // Not decimal.
                break;
            default:
                stringValue = stringValue.TrimStart('0');
                if (stringValue.Length == 0)
                {
                    return "0";
                }

                return stringValue;
        }

        if (!TryParseBigIntBase0(stringValue, out var bi))
        {
            // Go: panic(fmt.Sprintf("Failed to parse big int: %q", stringValue))
            // `%q` 的引号与转义对纯数字串与 C# 的字面量插值一致。
            Debug.Fail($"Failed to parse big int: \"{stringValue}\"");
        }

        return bi.ToString(); // Go: bi.String()
    }
}
