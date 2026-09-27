// ported from: internal/core/text.go
//
// 与 Go 的差异（依据 docs/architecture.md §3.1/§3.2）：
// - Go 的 string 是 UTF-8 字节序列，TextPos 的语义是**字节**下标；C# 的 string 是 UTF-16 代码单元序列
//   （与 JS 字符串同构），所以本文件里的 TextPos 一律是 UTF-16 代码单元下标（= TS 的 Pos 语义）。
//   Go 里"字节下标 → UTF-16 偏移"的换算（core.UTF16Len）在 C# 里因此退化成恒等，见 Core.cs。
// - Go 的 `type TextPos int32` 在 C# 里是只读结构体包装（Go 用 int32 是为了省内存，C# 用 int 承载，
//   语义相同）。隐式转换两个方向都定义，于是 Go 的 `int(pos)` 在 C# 里可以直接写成 `(int)pos`。
//   注意：**不能**给 TextPos 定义 == / != 运算符——它已经有到 int 的隐式转换，再定义会让 `pos == 0`
//   在"用户定义的 TextPos==TextPos"和"内建 int==int"之间产生二义性（CS0034）。`pos == 0` / `pos1 == pos2`
//   都已通过隐式转换解析到内建 int 比较，与 Go 的 int32 比较一致。
// - Go 结构体的 == 是字段级（TextRange{1,2} == TextRange{1,2} 为 true）；C# 结构体默认没有 ==，
//   所以 TextRange 补齐 ==/!=/Equals/GetHashCode（语义等价，且 xunit 的 Assert.Equal 直接可用）。
// - Go 的 unexported 字段（pos/end）只能由同包构造。C# 里 private 字段不能被同一个程序集里的
//   `Core` 静态类访问（C# 的可见性是类型级而不是包级），所以构造器用 internal，与 Collections.OrderedMap
//   的处理一致；字段仍保持 private。
using System.Globalization;

namespace TypeScript.Core;

/// <summary>Go 的 <c>type TextPos int32</c>：源文本里的一个位置。</summary>
/// <remarks>Go 里是 UTF-8 字节下标，C# 里是 UTF-16 代码单元下标（见文件头）。</remarks>
public readonly struct TextPos : IEquatable<TextPos>
{
    public readonly int Value;

    public TextPos(int value)
    {
        Value = value;
    }

    // Go 的 TextPos(x) 与 int(p) 都是显式转换；这里把两者都做成隐式，于是 Go 的 `int(pos)` 写作 `(int)pos`
    // 仍然合法，而 `pos` 需要当作 int 用的地方（下标、算术）也不必逐个强制转换。
    public static implicit operator int(TextPos pos) => pos.Value;

    public static implicit operator TextPos(int value) => new(value);

    public bool Equals(TextPos other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is TextPos other && Equals(other);

    public override int GetHashCode() => Value;

    // Go 里 TextPos 就是 int32，%d 打印数字本身，所以不能留下 record struct 的默认格式。
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Go 的 <c>type ECMALineStarts []TextPos</c>（ECMA 行首位置表）。</summary>
/// <remarks>
/// C# 不能给数组起别名，所以用只读结构体包装一个 <see cref="TextPos"/> 数组：它与 Go 的切片一样是
/// "数组引用 + 长度"的值语义，隐式转换两个方向都定义，因此 Go 的 `[]core.TextPos(x)` / 把结果赋给
/// `[]core.TextPos` 字段的写法都能原样保留。Go 的 nil 切片对应 C# 的空数组（见 Core.cs 文件头）。
/// </remarks>
public readonly struct ECMALineStarts(TextPos[] items)
{
    public readonly TextPos[] Items = items;

    public int Length => Items.Length;

    public TextPos this[int index] => Items[index];

    public static implicit operator TextPos[](ECMALineStarts value) => value.Items;

    public static implicit operator ECMALineStarts(TextPos[] items) => new(items);

    public IEnumerator<TextPos> GetEnumerator()
    {
        // foreach 用的是模式匹配的 GetEnumerator（不会经过隐式转换），所以这里要自己给出。
        foreach (var item in Items)
        {
            yield return item;
        }
    }
}

/// <summary>Go 的 <c>type TextRange struct { pos, end TextPos }</c>。</summary>
public readonly struct TextRange : IEquatable<TextRange>
{
    private readonly TextPos _pos;
    private readonly TextPos _end;

    // Go 的复合字面量 TextRange{pos: …, end: …} 只在 core 包内可用；C# 的 internal 是程序集可见，
    // 比 Go 的包可见性宽一点（同程序集的其它 package 也能构造），与 Collections.OrderedMap 一致。
    internal TextRange(int pos, int end)
    {
        this._pos = new TextPos(pos);
        this._end = new TextPos(end);
    }

    public int Pos()
    {
        return _pos.Value;
    }

    public int End()
    {
        return _end.Value;
    }

    public int Len()
    {
        return _end.Value - _pos.Value;
    }

    public bool IsValid()
    {
        return _pos.Value >= 0 || _end.Value >= 0;
    }

    public bool Contains(int pos)
    {
        return pos >= this._pos.Value && pos < _end.Value;
    }

    public bool ContainsInclusive(int pos)
    {
        return pos >= this._pos.Value && pos <= _end.Value;
    }

    public bool ContainsExclusive(int pos)
    {
        return this._pos.Value < pos && pos < _end.Value;
    }

    public TextRange WithPos(int pos)
    {
        return new TextRange(pos, _end.Value);
    }

    public TextRange WithEnd(int end)
    {
        return new TextRange(_pos.Value, end);
    }

    public bool ContainedBy(TextRange t2)
    {
        return t2._pos.Value <= _pos.Value && t2._end.Value >= _end.Value;
    }

    public bool Overlaps(TextRange t2)
    {
        // Go 的 max/min 内建在这里换成分支，避免 Math.Max/Min 的重载在 TextPos 上做隐式转换解析。
        var start = _pos.Value > t2._pos.Value ? _pos.Value : t2._pos.Value;
        var stop = _end.Value < t2._end.Value ? _end.Value : t2._end.Value;
        return start < stop;
    }

    // Similar to Overlaps, but treats touching ranges as intersecting.
    // For example, [0, 5) intersects [5, 10).
    public bool Intersects(TextRange t2)
    {
        var start = _pos.Value > t2._pos.Value ? _pos.Value : t2._pos.Value;
        var stop = _end.Value < t2._end.Value ? _end.Value : t2._end.Value;
        return start <= stop;
    }

    public bool Equals(TextRange other) => _pos.Value == other._pos.Value && _end.Value == other._end.Value;

    public override bool Equals(object? obj) => obj is TextRange other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_pos.Value, _end.Value);

    public static bool operator ==(TextRange left, TextRange right) => left.Equals(right);

    public static bool operator !=(TextRange left, TextRange right) => !left.Equals(right);
}

public static partial class Core
{
    public static TextRange NewTextRange(int pos, int end)
    {
        return new TextRange(pos, end);
    }

    public static TextRange UndefinedTextRange()
    {
        return new TextRange(-1, -1);
    }

    public static int CompareTextRanges(TextRange r1, TextRange r2)
    {
        var c = r1.Pos() - r2.Pos();
        if (c != 0)
        {
            return c;
        }

        return r1.End() - r2.End();
    }
}
