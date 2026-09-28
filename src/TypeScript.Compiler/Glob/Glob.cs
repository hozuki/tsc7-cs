// ported from: internal/glob/glob.go
//
// 映射约定（docs/architecture.md §2/§3）：
// 1) Go 的包名 `glob` 与本包唯一的导出类型 `Glob` 同名，所以**不能**照 §2 再建一个 `public static partial class Glob`
//    来放包级函数（会在同一个命名空间里撞名）。这里把包级函数放成 `Glob` 类型上的 static 成员（`Glob.TryParse`/
//    `Glob.Split`/`Glob.MatchElements`），调用点仍是 `Glob.Parse(...)` 的形状。**偏离**，见报告。
// 2) Go 的 `func Parse(pattern string) (*Glob, error)`：唯一的调用点（tsoptions）就是 `if parsed, err := glob.Parse(...); err == nil`，
//    属于"预期内失败" → §3.2 落成 `bool TryParse(string, out Glob?)`（api-parity 的 `Try${last}` 规则覆盖）。
//    Go 里三种错误（errBadRange/errInvalidUTF8/`errors.New("** may only be adjacent to '/'")`/unmatched '{'）
//    调用方只看 err 是否为 nil，故 C# 不构造异常对象，统一返回 false（附注：诊断文案不在本包）。
// 3) Go 的 `element` 接口（`fmt.Stringer`）→ `internal interface IElement`，各元素类型照 Go 的名字（slash/literal/
//    star/anyChar/starStar/group/charRange）落成 `SlashElement`/…；`String()` → `Text()`（C# 的 ToString 留给调试时
//    与 Go 输出对齐的那份文本，见 `Glob.ToString()`）。元素的相等比较照 Go：`e == (slash{})` → `e is SlashElement`。
// 4) Go 的 `Glob.String() string` 是 fmt.Stringer → 覆写 `ToString()`（§3.2）。
// 5) 下标：Go 的 glob 按**字节**切分（`input[1:]`、`strings.IndexByte`），C# 的 string 是 UTF-16 序列，这里按**代码单元**
//    切分（与 Go 对 ASCII 模式的逐位行为一致）；`readRangeRune`/`charRange` 在 Go 里解码 rune，C# 用 `Rune.DecodeFromUtf16`
//    （§3.1：码点判定用 int，不出现孤代理哨兵）。`anyChar` 在 Go 里消耗 1 个字节、C# 消耗 1 个代码单元（ASCII 场景等价）。
// 6) 热路径用 `ReadOnlySpan<char>`/`ReadOnlySpan<IElement>` 做零拷贝切分（§3.8）；因为 C# 的元组不能装 ref struct，
//    Go 的 `split(input) (first, rest string)` → `static void Split(span, out span first, out span rest)`。
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace TypeScript.Glob;

/// <summary>
/// A Glob is an LSP-compliant glob pattern, as defined by the spec:
/// https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/#documentFilter
/// </summary>
public sealed class Glob
{
    private readonly IElement[] _elems; // pattern elements

    private Glob(IElement[] elems) => _elems = elems;

    /// <summary>
    /// Parse builds a Glob for the given pattern, returning false if the pattern is invalid.
    /// （Go: <c>func Parse(pattern string) (*Glob, error)</c>）
    /// </summary>
    public static bool TryParse(string pattern, out Glob? glob)
    {
        var ok = TryParse(pattern, false, out glob, out _);
        return ok;
    }

    private static bool TryParse(string pattern, bool nested, out Glob? glob, out string rest)
    {
        var elems = new List<IElement>();
        var pos = 0;
        while (pos < pattern.Length)
        {
            switch (pattern[pos])
            {
                case '/':
                    pos++;
                    elems.Add(new SlashElement());
                    break;

                case '*':
                    if (pos + 1 < pattern.Length && pattern[pos + 1] == '*')
                    {
                        if ((elems.Count > 0 && elems[^1] is not SlashElement) || (pos + 2 < pattern.Length && pattern[pos + 2] != '/'))
                        {
                            glob = null;
                            rest = "";
                            return false;
                        }
                        pos += 2;
                        elems.Add(new StarStarElement());
                        break;
                    }
                    pos++;
                    elems.Add(new StarElement());
                    break;

                case '?':
                    pos++;
                    elems.Add(new AnyCharElement());
                    break;

                case '{':
                {
                    var group = new List<Glob>();
                    var start = pos;
                    while (pattern[start] != '}')
                    {
                        start++;
                        if (!TryParse(pattern[start..], true, out var groupG, out var pat))
                        {
                            glob = null;
                            rest = "";
                            return false;
                        }
                        if (pat.Length == 0)
                        {
                            // Go: errors.New("unmatched '{'")
                            glob = null;
                            rest = "";
                            return false;
                        }
                        // Go: pattern = pat
                        start = pattern.Length - pat.Length;
                        group.Add(groupG!);
                    }
                    pos = start + 1;
                    elems.Add(new GroupElement([.. group]));
                    break;
                }

                case '}':
                case ',':
                    if (nested)
                    {
                        glob = new Glob([.. elems]);
                        rest = pattern[pos..];
                        return true;
                    }
                    pos = ParseLiteral(elems, pattern, pos, false);
                    break;

                case '[':
                {
                    pos++;
                    if (pos == pattern.Length)
                    {
                        glob = null;
                        rest = "";
                        return false;
                    }
                    var negate = false;
                    if (pattern[pos] == '!')
                    {
                        pos++;
                        negate = true;
                    }
                    if (!TryReadRangeRune(pattern, pos, out var low, out var lowSize))
                    {
                        glob = null;
                        rest = "";
                        return false;
                    }
                    pos += lowSize;
                    if (pos == pattern.Length || pattern[pos] != '-')
                    {
                        glob = null;
                        rest = "";
                        return false;
                    }
                    pos++;
                    if (!TryReadRangeRune(pattern, pos, out var high, out var highSize))
                    {
                        glob = null;
                        rest = "";
                        return false;
                    }
                    pos += highSize;
                    if (pos == pattern.Length || pattern[pos] != ']')
                    {
                        glob = null;
                        rest = "";
                        return false;
                    }
                    pos++;
                    elems.Add(new CharRangeElement(negate, low, high));
                    break;
                }

                default:
                    pos = ParseLiteral(elems, pattern, pos, nested);
                    break;
            }
        }
        glob = new Glob([.. elems]);
        rest = "";
        return true;
    }

    // helper for decoding a rune in range elements, e.g. [a-z]
    private static bool TryReadRangeRune(string input, int pos, out int rune, out int size)
    {
        var status = Rune.DecodeFromUtf16(input.AsSpan(pos), out var r, out size);
        if (status == OperationStatus.Done)
        {
            rune = r.Value;
            return true;
        }
        // Go 的 readRangeRune：RuneError + sz==1 → errInvalidUTF8；sz==0（空输入）→ errBadRange；两者都让 Parse 失败。
        rune = 0xFFFD;
        return false;
    }

    private static int ParseLiteral(List<IElement> elems, string pattern, int pos, bool nested)
    {
        var specialChars = nested ? "*?{[/}," : "*?{[/";
        var end = pattern.AsSpan(pos).IndexOfAny(specialChars);
        if (end < 0)
        {
            end = pattern.Length;
        }
        else
        {
            end += pos;
        }
        elems.Add(new LiteralElement(pattern[pos..end]));
        return end;
    }

    public override string ToString()
    {
        var b = new StringBuilder();
        foreach (var e in _elems)
        {
            b.Append(e.Text());
        }
        return b.ToString();
    }

    /// <summary>Match reports whether the input string matches the glob pattern.</summary>
    public bool Match(string input) => MatchElements(_elems, input);

    internal static bool MatchElements(ReadOnlySpan<IElement> elems, ReadOnlySpan<char> input)
    {
        while (elems.Length > 0)
        {
            var elem = elems[0];
            elems = elems[1..];
            switch (elem)
            {
                case SlashElement:
                    if (input.Length == 0 || input[0] != '/')
                    {
                        return false;
                    }
                    // Go: `for input[0] == '/' { input = input[1:] }`（全为 '/' 时 Go 会越界 panic；C# 用长度守卫，行为更安全）
                    while (input.Length > 0 && input[0] == '/')
                    {
                        input = input[1..];
                    }
                    break;

                case StarStarElement:
                    // Special cases:
                    //  - **/a matches "a"
                    //  - **/ matches everything
                    //
                    // Note that if ** is followed by anything, it must be '/' (this is
                    // enforced by Parse).
                    if (elems.Length > 0)
                    {
                        elems = elems[1..];
                    }

                    // A trailing ** matches anything.
                    if (elems.Length == 0)
                    {
                        return true;
                    }

                    // Backtracking: advance pattern segments until the remaining pattern
                    // elements match.
                    while (input.Length != 0)
                    {
                        if (MatchElements(elems, input))
                        {
                            return true;
                        }
                        Split(input, out _, out var rest);
                        input = rest;
                    }
                    return false;

                case LiteralElement literal:
                    if (!input.StartsWith(literal.Value))
                    {
                        return false;
                    }
                    input = input[literal.Value.Length..];
                    break;

                case StarElement:
                {
                    Split(input, out var segInput, out var starRest);
                    input = starRest;

                    var elemEnd = elems.Length;
                    for (var i = 0; i < elems.Length; i++)
                    {
                        if (elems[i] is SlashElement)
                        {
                            elemEnd = i;
                            break;
                        }
                    }
                    var segElems = elems[..elemEnd];
                    elems = elems[elemEnd..];

                    // A trailing * matches the entire segment.
                    if (segElems.Length == 0)
                    {
                        break;
                    }

                    // Backtracking: advance characters until remaining subpattern elements
                    // match.
                    var matched = false;
                    for (var i = 0; i < segInput.Length; i++)
                    {
                        if (MatchElements(segElems, segInput[i..]))
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched)
                    {
                        return false;
                    }
                    break;
                }

                case AnyCharElement:
                    if (input.Length == 0 || input[0] == '/')
                    {
                        return false;
                    }
                    input = input[1..];
                    break;

                case GroupElement group:
                {
                    // Append remaining pattern elements to each group member looking for a
                    // match.
                    foreach (var m in group.Alternatives)
                    {
                        var branch = new IElement[m._elems.Length + elems.Length];
                        m._elems.CopyTo(branch, 0);
                        elems.CopyTo(branch.AsSpan(m._elems.Length));
                        if (MatchElements(branch, input))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                case CharRangeElement range:
                {
                    if (input.Length == 0 || input[0] == '/')
                    {
                        return false;
                    }
                    var status = Rune.DecodeFromUtf16(input, out var r, out var size);
                    var c = status == OperationStatus.Done ? r.Value : 0xFFFD;
                    if (c < range.Low || c > range.High)
                    {
                        return false;
                    }
                    input = input[size..];
                    break;
                }

                default:
                    throw new DebugFailureException($"segment type {elem.GetType()} not implemented");
            }
        }

        return input.Length == 0;
    }

    /// <summary>
    /// split returns the portion before and after the first slash
    /// (or sequence of consecutive slashes). If there is no slash
    /// it returns (input, "").
    /// </summary>
    internal static void Split(ReadOnlySpan<char> input, out ReadOnlySpan<char> first, out ReadOnlySpan<char> rest)
    {
        var i = input.IndexOf('/');
        if (i < 0)
        {
            first = input;
            rest = default;
            return;
        }
        first = input[..i];
        // Go 的 `for j := i; j < len(input); j++ { if input[j] != '/' { ... } }`：找第一个非 '/' 的位置。
        // 用内置的 `IndexOfAnyExcept`（对 char 有向量化实现）取代手写循环。
        var j = input[i..].IndexOfAnyExcept('/');
        rest = j < 0 ? default : input[(i + j)..];
    }
}

/// <summary>element holds a glob pattern element, as defined below（Go: <c>type element fmt.Stringer</c>）。</summary>
internal interface IElement
{
    string Text(); // Go 的 fmt.Stringer
}

internal sealed class SlashElement : IElement
{
    public string Text() => "/";
}

internal sealed class LiteralElement(string value) : IElement
{
    public readonly string Value = value;

    public string Text() => Value;
}

internal sealed class StarElement : IElement
{
    public string Text() => "*";
}

internal sealed class AnyCharElement : IElement
{
    public string Text() => "?";
}

internal sealed class StarStarElement : IElement
{
    public string Text() => "**";
}

/// <summary>Go: <c>group []*Glob</c>（<c>{foo, bar, ...}</c> grouping）。</summary>
internal sealed class GroupElement(Glob[] alternatives) : IElement
{
    public readonly Glob[] Alternatives = alternatives;

    public string Text()
    {
        var parts = new string[Alternatives.Length];
        for (var i = 0; i < Alternatives.Length; i++)
        {
            parts[i] = Alternatives[i].ToString();
        }
        return "{" + string.Join(",", parts) + "}";
    }
}

/// <summary>Go: <c>charRange struct { negate bool; low, high rune }</c>（rune → C# 的 int 码点，§3.1）。</summary>
internal sealed class CharRangeElement(bool negate, int low, int high) : IElement
{
    public readonly bool Negate = negate;
    public readonly int Low = low;
    public readonly int High = high;

    // Go 的 charRange.String() 不打印 negate（`"[" + string(r.low) + "-" + string(r.high) + "]"`）
    public string Text() => "[" + RuneToString(Low) + "-" + RuneToString(High) + "]";

    private static string RuneToString(int rune) => char.ConvertFromUtf32(rune);
}
