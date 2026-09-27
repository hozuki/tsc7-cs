// ported from: github.com/peter-evans/patience@v0.3.0/patience.go
//
// 这是 TSGO 的 `tsc/go.mod` 里唯一的第三方 diff 依赖（github.com/peter-evans/patience v0.3.0），
// 调用点在 tsc/internal/testutil/baseline/baseline.go 的 DiffText：
//
//	patience.Diff(stringutil.SplitLines(expected), stringutil.SplitLines(actual))
//	patience.UnifiedDiffTextWithOptions(lines, patience.UnifiedDiffOptions{...})
//
// Go 的 package → 同名 C# 命名空间 TypeScript.Patience；包级函数放进与包同名的
// `public static partial class Patience`，包级类型（enum/struct）放在命名空间层。
//
// 与 Go 的差异（逐条）：
// 1) `type DiffType int8` → `enum DiffType : int`，数值与 Go 逐字相同（Delete=-1/Insert=1/Equal=0）。
// 2) Go 的 `DiffLine` 是 struct（字段导出）→ C# `sealed class` + `public readonly` 字段，
//    字段名保持 Text/Type；Go 的 struct 字面量 `DiffLine{l, t}` → 构造函数 `new DiffLine(text, type)`。
// 3) `[]DiffLine` → `DiffLine[]`。Go 用 nil 表达"没有任何 diff 行"（`Diff` 在两个入参都为空时返回 nil，
//    而且只有这一种情况：其余分支至少产出一行），C# 侧保持 `DiffLine[]?` 并只在同一条件下返回 null；
//    下游（DiffText*/makeHunks）按 Go 的 `len(nil) == 0` 语义把 null 当空处理；反过来 `Diff` 的两个入参
//    也接受 null（= Go 的 nil 切片），在入口 `??= []`。
// 4) 切片语法 `a[i:]` / `a[i:j]` **不**实例化成新数组：Go 的切片只是别名（零拷贝），
//    C# 的数组 range 拷贝会让递归每层都拷一次（O(n·深度)）。这里改成把 (list, start, end) 传给
//    private 递归核心 DiffCore，逐处对应：`len(a)` → `aEnd - aStart`，`a[i]` → `a[aStart + i]`，
//    `a[i:]` → `(a, aStart + i, aEnd)`，`a[:i]` → `(a, aStart, aStart + i)`。
// 5) `uniqueElements` 返回的 `[]string{}`/`[]int{}` 在 Go 里是**非 nil 空切片**，C# 用空 List（不是 null）。
// 6) `a, b []string` → `IReadOnlyList<string>`（string[]/List<string> 都能传）。
// 7) 未导出函数的可见性：toDiffLines → private `ToDiffLines`；uniqueElements → `public UniqueElements`
//    （Go 的同 package 单测直接调它，探针要照搬，故不降成 private）；typeSymbol/min/max 由 Format/Unified/Lcs 各自持有。
namespace TypeScript.Patience;

/// <summary>Go 的 <c>type DiffType int8</c>（数值与 Go 一致）。</summary>
public enum DiffType : int
{
    /// <summary>Represents a diff delete operation.</summary>
    Delete = -1,

    /// <summary>Represents a diff insert operation.</summary>
    Insert = 1,

    /// <summary>Represents no diff.</summary>
    Equal = 0,
}

/// <summary>Go 的 <c>type DiffLine struct { Text string; Type DiffType }</c>。</summary>
public sealed class DiffLine(string text, DiffType type)
{
    public readonly string Text = text;

    public readonly DiffType Type = type;
}

public static partial class Patience
{
    /// <summary>
    /// Go: <c>func toDiffLines(a []string, t DiffType) []DiffLine</c>。
    /// Go 的 `a[:i]`/`a[i:]` 对应调用点的 (start, end)。
    /// </summary>
    private static List<DiffLine> ToDiffLines(IReadOnlyList<string> a, int start, int end, DiffType t)
    {
        var diffs = new List<DiffLine>(end - start);
        for (var i = start; i < end; i++)
        {
            diffs.Add(new DiffLine(a[i], t));
        }

        return diffs;
    }

    /// <summary>
    /// Go: <c>func uniqueElements(a []string) ([]string, []int)</c>。
    /// 返回原切片中只出现一次的元素，以及它们各自的下标；两个结果都是非 nil 的（可能为空）。
    /// </summary>
    public static (List<string> Elements, List<int> Indices) UniqueElements(IReadOnlyList<string> a)
    {
        return UniqueElements(a, 0, a.Count);
    }

    /// <summary>
    /// Go 的 <c>uniqueElements</c> 作用在 <c>a[start:end]</c> 上的版本（见文件头第 4 条）。
    /// 关键：Go 传进来的就是**子切片本身**，所以它返回的下标相对子切片开头；这里传的是 (整表, start, end)，
    /// 因此下标要减去 start 才能和 Go 逐字对齐（于是调用点写 `a[aStart + i]` 而不是 `a[i]`）。
    /// </summary>
    private static (List<string> Elements, List<int> Indices) UniqueElements(IReadOnlyList<string> a, int start, int end)
    {
        var m = new Dictionary<string, int>();
        for (var i = start; i < end; i++)
        {
            m[a[i]] = m.GetValueOrDefault(a[i]) + 1;
        }

        var elements = new List<string>();
        var indices = new List<int>();
        for (var i = start; i < end; i++)
        {
            var e = a[i];
            if (m[e] == 1)
            {
                elements.Add(e);
                indices.Add(i - start);
            }
        }

        return (elements, indices);
    }

    /// <summary>
    /// Go: <c>func Diff(a, b []string) []DiffLine</c> —— 两个字符串切片的 patience diff。
    /// 入参都为空（含 Go 的 nil）时返回 null（Go 的 nil），其余情况返回非空数组。
    /// </summary>
    public static DiffLine[]? Diff(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        // Go 的 nil 切片 `len(nil) == 0`，`Diff(nil, nil)` 因此返回 nil；这里显式还原。
        a ??= [];
        b ??= [];
        var diffs = DiffCore(a, 0, a.Count, b, 0, b.Count);
        return diffs.Count == 0 ? null : diffs.ToArray();
    }

    /// <summary>
    /// Go 的 <c>Diff(a, b)</c>，但把 `a`/`b` 的窗口显式写成 (start, end) 以免切片拷贝（文件头第 4 条）。
    /// 空 List 对应 Go 的 nil 返回值（`append(x, nil...)` 与 append 空切片等价）。
    /// </summary>
    private static List<DiffLine> DiffCore(
        IReadOnlyList<string> a,
        int aStart,
        int aEnd,
        IReadOnlyList<string> b,
        int bStart,
        int bEnd)
    {
        if (aStart == aEnd && bStart == bEnd)
        {
            return [];
        }

        if (aStart == aEnd)
        {
            return ToDiffLines(b, bStart, bEnd, DiffType.Insert);
        }

        if (bStart == bEnd)
        {
            return ToDiffLines(a, aStart, aEnd, DiffType.Delete);
        }

        // Find equal elements at the head of slices a and b.
        var i = 0;
        while (i < aEnd - aStart && i < bEnd - bStart && a[aStart + i] == b[bStart + i])
        {
            i++;
        }

        if (i > 0)
        {
            var head = ToDiffLines(a, aStart, aStart + i, DiffType.Equal);
            head.AddRange(DiffCore(a, aStart + i, aEnd, b, bStart + i, bEnd));
            return head;
        }

        // Find equal elements at the tail of slices a and b.
        var j = 0;
        while (j < aEnd - aStart && j < bEnd - bStart && a[aEnd - 1 - j] == b[bEnd - 1 - j])
        {
            j++;
        }

        if (j > 0)
        {
            var tail = DiffCore(a, aStart, aEnd - j, b, bStart, bEnd - j);
            tail.AddRange(ToDiffLines(a, aEnd - j, aEnd, DiffType.Equal));
            return tail;
        }

        // Find the longest common subsequence of unique elements in a and b.
        var (ua, idxa) = UniqueElements(a, aStart, aEnd);
        var (ub, idxb) = UniqueElements(b, bStart, bEnd);
        var lcs = LCS(ua, ub);

        // If the LCS is empty, the diff is all deletions and insertions.
        if (lcs.Count == 0)
        {
            var all = ToDiffLines(a, aStart, aEnd, DiffType.Delete);
            all.AddRange(ToDiffLines(b, bStart, bEnd, DiffType.Insert));
            return all;
        }

        // Lookup the original indices of slices a and b.
        foreach (var x in lcs)
        {
            x[0] = idxa[x[0]];
            x[1] = idxb[x[1]];
        }

        var diffs = new List<DiffLine>();
        var ga = 0;
        var gb = 0;
        foreach (var ip in lcs)
        {
            // Diff the gaps between the lcs elements.
            diffs.AddRange(DiffCore(a, aStart + ga, aStart + ip[0], b, bStart + gb, bStart + ip[1]));
            // Append the LCS elements to the diff.
            diffs.Add(new DiffLine(a[aStart + ip[0]], DiffType.Equal));
            ga = ip[0] + 1;
            gb = ip[1] + 1;
        }

        // Diff the remaining elements of a and b after the final LCS element.
        diffs.AddRange(DiffCore(a, aStart + ga, aEnd, b, bStart + gb, bEnd));

        return diffs;
    }
}
