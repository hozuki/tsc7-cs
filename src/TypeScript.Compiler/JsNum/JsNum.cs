// ported from: internal/jsnum/jsnum.go
//
// 偏离 Go 的地方（docs/architecture.md §2、§3）：
//
// 1) Go 的 `type Number float64` 在 C# 里是 `readonly struct Number`（包一层 double）。Go 的 Number 是
//    数值类型，可以直接参与 `+ - * / < ==` 和 `len`/`switch` 等运算；C# 的结构体要保住这些调用点，
//    就必须提供对应的运算符重载（见下方 operators 区）。转换按任务约定做成 explicit：Go 的
//    `jsnum.Number(x)` ↔ C# 的 `(Number)x`，Go 的 `float64(n)` ↔ C# 的 `(double)n`——与 Go 一样，
//    隐式转换只发生在无类型常量上（`Number` 字段初始化为 `(Number)1.5` 需要显式写出）。
//    因此调用点里 `Number` 与非 Number 数值直接比较/运算的地方要补显式转换（Go 里靠无类型常量隐式转换）。
// 2) Go 的 typed const `MaxSafeInteger Number = 1<<53 - 1` 在 C# 里没有对应物（Number 是结构体，
//    不能 const），改用 `static readonly`。
// 3) Go 的 `String()` stringer → C# 的 `ToString()` 覆写（见 String.cs），另在类 JsNum 里提供
//    `String(this Number)` 扩展方法，让调用点保留 Go 的 `n.String()` 写法（与 Core 枚举同一约定）。
// 4) `func (x Number) toInt32() int32` 的第一步 `smi := int32(x); float64(smi) == x` 依赖
//    "float64 → int32 越界时是平台相关值"这个 Go 实现细节（amd64/arm64 上都饱和成 MinInt32，于是
//    比较必然为 false）。C# 里越界转换是未定义行为，所以这里先做同样的区间判定（判定的结果与
//    Go 的 fast path 完全一致：只有当 x 恰好是 int32 可表示整数时才会命中），再做转换。
using System.Numerics;
using static TypeScript.JsNum.JsNum;

namespace TypeScript.JsNum;

public static partial class JsNum
{
    // Go: const ( MaxSafeInteger Number = 1<<53 - 1; MinSafeInteger Number = -MaxSafeInteger )
    public static readonly Number MaxSafeInteger = (Number)9007199254740991.0;
    public static readonly Number MinSafeInteger = -MaxSafeInteger;

    // Go: var negativeZero = Number(math.Copysign(0, -1))
    //
    // Go 里它只被同 package 的测试用到；C# 侧测试在另一个程序集，靠 InternalsVisibleTo 访问
    // （Properties/AssemblyInfo.cs），所以这里是 internal 而不是 private。
    internal static readonly Number negativeZero = (Number)Math.CopySign(0, -1);

    public static Number NaN()
    {
        return (Number)double.NaN;
    }

    // Go: math.Inf(sign)：sign >= 0 → +Inf，否则 -Inf（C# 没有 math.Inf 对应物）。
    public static Number Inf(int sign)
    {
        return sign >= 0 ? (Number)double.PositiveInfinity : (Number)double.NegativeInfinity;
    }

    internal static bool IsNonFinite(double x)
    {
        // This is equivalent to checking `math.IsNaN(x) || math.IsInf(x, 0)` in one operation.
        const long mask = 0x7FF0000000000000;
        return (BitConverter.DoubleToInt64Bits(x) & mask) == mask;
    }

    // Go: `String() string` stringer → `ToString()` 覆写（String.cs）。
    // 这里保留 Go 的调用点写法 `n.String()`；对合法值它等价于 `n.ToString()`。
    public static string String(this Number n)
    {
        return n.ToString();
    }
}

/// <summary>
/// Number represents a JS-like number.
/// <para>
/// All operations that can be performed directly on this type
/// (e.g., conversion, arithmetic, etc.) behave as they would in JavaScript,
/// but any other operation should use this type's methods,
/// not the "math" package and conversions.
/// </para>
/// </summary>
public readonly partial struct Number : IEquatable<Number>, IComparable<Number>
{
    public readonly double Value;

    public Number(double value)
    {
        Value = value;
    }

    // Go 的 `Number(x)` / `float64(n)` 转换（见文件头第 1 条）。
    public static explicit operator Number(double value) => new(value);

    public static explicit operator double(Number number) => number.Value;

    // Go 的 `Number` 底层是 float64，`+ - * / < <= > >= == !=` 都按 IEEE 754 直接作用在值上。
    public static Number operator +(Number left, Number right) => new(left.Value + right.Value);

    public static Number operator -(Number left, Number right) => new(left.Value - right.Value);

    public static Number operator *(Number left, Number right) => new(left.Value * right.Value);

    public static Number operator /(Number left, Number right) => new(left.Value / right.Value);

    public static Number operator -(Number value) => new(-value.Value);

    public static Number operator +(Number value) => value;

    public static bool operator ==(Number left, Number right) => left.Value == right.Value;

    public static bool operator !=(Number left, Number right) => left.Value != right.Value;

    public static bool operator <(Number left, Number right) => left.Value < right.Value;

    public static bool operator <=(Number left, Number right) => left.Value <= right.Value;

    public static bool operator >(Number left, Number right) => left.Value > right.Value;

    public static bool operator >=(Number left, Number right) => left.Value >= right.Value;

    // Go: `n == m` / map key 语义（+0 == -0，NaN != NaN）。
    public bool Equals(Number other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is Number other && Value == other.Value;

    public override int GetHashCode()
    {
        // +0 与 -0 相等，因此必须给出同一个哈希；NaN 与自身不相等，无此约束。
        var value = Value == 0 ? 0d : Value;
        return value.GetHashCode();
    }

    // Go 的 `cmp.Compare(a, b)` 对 Number 的语义（checker/utilities.go 用它比较数字字面量类型）：
    // 先判相等，再判大小，NaN 落在最后（a < b 为假 → 返回 1）。
    public int CompareTo(Number other)
    {
        if (Value == other.Value)
        {
            return 0;
        }

        return Value < other.Value ? -1 : 1;
    }

    public bool IsNaN()
    {
        return double.IsNaN(Value);
    }

    public bool IsInf()
    {
        return double.IsInfinity(Value);
    }

    // https://tc39.es/ecma262/2024/multipage/abstract-operations.html#sec-touint32
    internal uint ToUint32()
    {
        // The only difference between ToUint32 and ToInt32 is the interpretation of the bits.
        return (uint)ToInt32();
    }

    // https://tc39.es/ecma262/2024/multipage/abstract-operations.html#sec-toint32
    internal int ToInt32()
    {
        var x = Value;

        // Fast path: if the number is in the range (-2^31, 2^32), i.e. an SMI,
        // then we don't need to do any special mapping.
        //
        // Go 写的是 `if smi := int32(x); float64(smi) == x`；C# 的越界转换是未定义行为，所以
        // 先判区间（只有 int32 可表示的整数才可能命中，结论与 Go 相同），再转换。
        if (x >= -2147483648.0 && x <= 2147483647.0)
        {
            var smi = (int)x;
            if (smi == x)
            {
                return smi;
            }
        }

        // 2. If number is not finite or number is either +0𝔽 or -0𝔽, return +0𝔽.
        // Zero was covered by the test above.
        if (IsNonFinite(x))
        {
            return 0;
        }

        // Let int be truncate(ℝ(number)).
        x = Math.Truncate(x);
        // Let int32bit be int modulo 2**32.
        x = x % 4294967296.0; // Go: math.Mod(x, 1<<32)，C# 的 double % 就是 fmod。
        // If int32bit ≥ 2**31, return 𝔽(int32bit - 2**32); otherwise return 𝔽(int32bit).
        return unchecked((int)(long)x);
    }

    internal uint ToShiftCount()
    {
        return ToUint32() & 31;
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-signedRightShift
    public Number SignedRightShift(Number y)
    {
        return (Number)(ToInt32() >> (int)y.ToShiftCount());
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-unsignedRightShift
    public Number UnsignedRightShift(Number y)
    {
        return (Number)(ToUint32() >> (int)y.ToShiftCount());
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-leftShift
    public Number LeftShift(Number y)
    {
        return (Number)(ToInt32() << (int)y.ToShiftCount());
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-bitwiseNOT
    public Number BitwiseNOT()
    {
        return (Number)~ToInt32();
    }

    // The below are implemented by https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numberbitwiseop.

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-bitwiseOR
    public Number BitwiseOR(Number y)
    {
        return (Number)(ToInt32() | y.ToInt32());
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-bitwiseAND
    public Number BitwiseAND(Number y)
    {
        return (Number)(ToInt32() & y.ToInt32());
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-bitwiseXOR
    public Number BitwiseXOR(Number y)
    {
        return (Number)(ToInt32() ^ y.ToInt32());
    }

    // Go 的 `trunc` 未导出且在本 package 内没有调用点（Go 侧同样如此），保留 1:1 对应物。
    internal Number Trunc()
    {
        return (Number)Math.Truncate(Value);
    }

    public Number Floor()
    {
        return (Number)Math.Floor(Value);
    }

    public Number Abs()
    {
        return (Number)Math.Abs(Value);
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-remainder
    public Number Remainder(Number d)
    {
        if (IsNaN() || d.IsNaN())
        {
            return NaN();
        }

        if (IsInf())
        {
            return NaN();
        }

        if (d.IsInf())
        {
            return this;
        }

        if (d == (Number)0)
        {
            return NaN();
        }

        if (this == (Number)0)
        {
            return this;
        }

        // Go: math.Mod —— C# 的 double `%` 就是 IEEE 754 的 fmod（同一操作），不是 Math.IEEERemainder。
        return (Number)(Value % d.Value);
    }

    // https://tc39.es/ecma262/2024/multipage/ecmascript-data-types-and-values.html#sec-numeric-types-number-exponentiate
    public Number Exponentiate(Number exponent)
    {
        if ((this == (Number)1 || this == (Number)(-1)) && exponent.IsInf())
        {
            return NaN();
        }

        if (this == (Number)1 && exponent.IsNaN())
        {
            return NaN();
        }

        var b = Value;
        var e = exponent.Value;

        // For integer base ** integer exponent where the result exceeds 53 bits,
        // math.Pow can be off by multiple ULPs vs JS engines. Use exact big.Int
        // arithmetic and IEEE 754 round-to-nearest-even conversion instead.
        // The ES spec (§6.1.6.1.3) says exponentiate returns an
        // "implementation-approximated" value, so engines are allowed to differ.
        // This won't exactly match every engine (V8's fdlibm-compiled pow can
        // round halfway ties differently), but will always be within 1 ULP
        // (unit in the last place, i.e. the least significant bit of the result).
        if (b >= long.MinValue && b <= long.MaxValue && b == Math.Truncate(b) &&
            e >= 0 && e <= long.MaxValue && e == Math.Truncate(e) && !double.IsInfinity(e))
        {
            var magnitude = e * Math.Log2(Math.Abs(b));
            if (magnitude > 53 && magnitude <= Math.Log2(double.MaxValue))
            {
                // 能走到这里必有 |b| >= 2 且 e <= log2(MaxFloat64) ≈ 1024，所以 (int)e 不会溢出
                // （BigInteger.Pow 的指数是 int，Go 侧是 big.NewInt(int64(e))）。
                var ri = BigInteger.Pow(new BigInteger(b), (int)e);
                return (Number)(double)ri;
            }
        }

        return (Number)Math.Pow(b, e);
    }
}
