// ported from: internal/stringutil/identifier_parts_generated.go, internal/stringutil/js_case_generated.go
//
// 本文件是**手写**的（生成器不产出它）：Go 标准库 unicode.RangeTable / unicode.Is / unicode.is16 / unicode.is32
// 的等价物，供生成器产出的 IdentifierParts.Generated.cs 与 SpecialCasing.Generated.cs 使用。
// Go 侧这些表是 `*unicode.RangeTable`，由 stdlib 的 unicode.Is 查表；C# 没有对应类型，所以在这里定义
// Range16 / Range32 / UnicodeRangeTable，并把 unicode.Is 的两条查找分支照搬过来（见 Contains）。
namespace TypeScript.StringUtil;

/// <summary>Go 标准库 <c>unicode.Range16</c> 的等价物：BMP 上步长为 <c>Stride</c> 的等差数列 [Lo, Hi]。</summary>
public readonly struct Range16(ushort lo, ushort hi, ushort stride)
{
    public readonly ushort Lo = lo;
    public readonly ushort Hi = hi;
    public readonly ushort Stride = stride;
}

/// <summary>Go 标准库 <c>unicode.Range32</c> 的等价物：astral 平面上的等差数列 [Lo, Hi]。</summary>
public readonly struct Range32(uint lo, uint hi, uint stride)
{
    public readonly uint Lo = lo;
    public readonly uint Hi = hi;
    public readonly uint Stride = stride;
}

/// <summary>Go 标准库 <c>unicode.RangeTable</c> 的等价物（R16 + R32 + LatinOffset）。</summary>
public sealed class UnicodeRangeTable(Range16[] r16, Range32[] r32, int latinOffset)
{
    public readonly Range16[] R16 = r16;

    public readonly Range32[] R32 = r32;

    /// <summary>
    /// Go 的 <c>RangeTable.LatinOffset</c>：R16 里开头若干个 <c>Hi &lt;= U+00FF</c> 的项数。
    /// <c>unicode.Is</c> 不使用它（Go 的 IsGraphic / IsPrint 才用），这里保留只是为了与 Go 的类型逐字段对应、
    /// 让生成数据与 Go 的 <c>*_generated.go</c> 一比一。
    /// </summary>
    public readonly int LatinOffset = latinOffset;

    // Go 标准库 unicode 包里的两个常量（unicode/tables.go）：R16 项数不超过它、或码点在 Latin-1 内时，
    // is16 改走线性扫描。两条分支结果完全相同，这里照搬只是为了与 Go 逐行对应。
    private const int _linearMax = 18;

    private const int _maxLatin1 = 0xFF;

    /// <summary>Go 标准库 <c>unicode.Is(rangeTab, r)</c> 的等价物。</summary>
    public bool Contains(int codePoint)
    {
        var r16 = R16;
        // Go 用 uint32 比较，以便正确处理负 rune（负数转 uint32 后必然大于任何 Hi）。
        if (r16.Length > 0 && (uint)codePoint <= r16[r16.Length - 1].Hi)
        {
            return Contains16(r16, codePoint);
        }

        var r32 = R32;
        if (r32.Length > 0 && codePoint >= (int)r32[0].Lo)
        {
            return Contains32(r32, codePoint);
        }

        return false;
    }

    private static bool Contains16(Range16[] ranges, int r)
    {
        if (ranges.Length <= _linearMax || r <= _maxLatin1)
        {
            foreach (var range in ranges)
            {
                if (r < range.Lo)
                {
                    return false;
                }

                if (r <= range.Hi)
                {
                    return range.Stride == 1 || (r - range.Lo) % range.Stride == 0;
                }
            }

            return false;
        }

        // binary search over ranges
        var lo = 0;
        var hi = ranges.Length;
        while (lo < hi)
        {
            var m = (int)((uint)(lo + hi) >> 1);
            var range = ranges[m];
            if (range.Lo <= r && r <= range.Hi)
            {
                return range.Stride == 1 || (r - range.Lo) % range.Stride == 0;
            }

            if (r < range.Lo)
            {
                hi = m;
            }
            else
            {
                lo = m + 1;
            }
        }

        return false;
    }

    private static bool Contains32(Range32[] ranges, int r)
    {
        var u = (uint)r;
        if (ranges.Length <= _linearMax)
        {
            foreach (var range in ranges)
            {
                if (u < range.Lo)
                {
                    return false;
                }

                if (u <= range.Hi)
                {
                    return range.Stride == 1 || (u - range.Lo) % range.Stride == 0;
                }
            }

            return false;
        }

        // binary search over ranges
        var lo = 0;
        var hi = ranges.Length;
        while (lo < hi)
        {
            var m = (int)((uint)(lo + hi) >> 1);
            var range = ranges[m];
            if (range.Lo <= u && u <= range.Hi)
            {
                return range.Stride == 1 || (u - range.Lo) % range.Stride == 0;
            }

            if (u < range.Lo)
            {
                hi = m;
            }
            else
            {
                lo = m + 1;
            }
        }

        return false;
    }
}
