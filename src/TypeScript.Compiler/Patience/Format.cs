// ported from: github.com/peter-evans/patience@v0.3.0/format.go
//
// 调用点：tsc/internal/testutil/baseline/baseline.go 的 DiffText 只用
// UnifiedDiffTextWithOptions（其余导出函数是同一个包的公开面，一并 1:1 移植）。
//
// 与 Go 的差异（逐条）：
// 1) `typeSymbol` 的 `panic("unknown DiffType")` → `Debug.Fail`（TypeScript.Debug，见 docs/architecture.md §3.2），
//    其后补一条 `throw` 是为了让 switch 的每个分支都有返回路径（Debug.Fail 没标 [DoesNotReturn]）。
// 2) `diffs []DiffLine` 可能是 Go 的 nil（`Diff` 的空结果），C# 参数是 `DiffLine[]?` 并在入口 `??= []`
//    把 null 当空处理——保留 Go 的 `len(nil) == 0` 语义。
// 3) `DiffText` 里 `s := make([]string, len(diffs))` 是**定长**切片，`continue` 跳过的位置留在原地
//    （值仍是零值 ""），最后 Join("\n") 会把它当作一个空行。C# 用 `new string[len]`（元素初值 null，
//    string.Join 同样当空串）而不是 List.Add：绝不能把跳过的位置从结果里去掉。
// 4) `fmt.Sprintf("--- %s", x)` → 插值字符串；`len(opts.X) > 0` → `opts.X.Length > 0`。
// 5) Go 的 `UnifiedDiffOptions` 零值语义（缺省 Precontext/Postcontext=0、两个 header 为 ""）→ C# 类用
//    字段默认值表达，调用点用对象初始化器（Go 的 struct 字面量）。
namespace TypeScript.Patience;

/// <summary>Go 的 <c>type UnifiedDiffOptions struct</c>（零值同 Go：int 字段 0、header 为空串）。</summary>
public sealed class UnifiedDiffOptions
{
    /// <summary>Precontext is the number of lines of context before each change in a hunk.</summary>
    public int Precontext;

    /// <summary>Postcontext is the number of lines of context after each change in a hunk.</summary>
    public int Postcontext;

    /// <summary>SrcHeader is the header for the source file.</summary>
    public string SrcHeader = "";

    /// <summary>DstHeader is the header for the destination file.</summary>
    public string DstHeader = "";
}

public static partial class Patience
{
    /// <summary>Go: <c>func typeSymbol(t DiffType) string</c>。</summary>
    private static string TypeSymbol(DiffType t)
    {
        switch (t)
        {
            case DiffType.Equal:
                return " ";
            case DiffType.Insert:
                return "+";
            case DiffType.Delete:
                return "-";
            default:
                Debug.Fail("unknown DiffType");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    /// <summary>
    /// Go: <c>func DiffText(diffs []DiffLine) string</c> —— 源+目标两侧的全文
    /// （所有相等行、插入行、删除行）。
    /// </summary>
    public static string DiffText(DiffLine[]? diffs)
    {
        diffs ??= [];
        var s = new string[diffs.Length];
        for (var i = 0; i < diffs.Length; i++)
        {
            var l = diffs[i];
            if (l.Text.Length == 0 && l.Type == DiffType.Equal)
            {
                continue;
            }

            s[i] = TypeSymbol(l.Type) + l.Text;
        }

        return string.Join("\n", s);
    }

    /// <summary>Go: <c>func DiffTextA(diffs []DiffLine) string</c> —— 源侧全文（相等行与删除行）。</summary>
    public static string DiffTextA(DiffLine[]? diffs)
    {
        diffs ??= [];
        var s = new List<string>();
        foreach (var l in diffs)
        {
            if (l.Type == DiffType.Insert)
            {
                continue;
            }

            s.Add(l.Type == DiffType.Equal && l.Text.Length == 0 ? "" : TypeSymbol(l.Type) + l.Text);
        }

        return string.Join("\n", s);
    }

    /// <summary>Go: <c>func DiffTextB(diffs []DiffLine) string</c> —— 目标侧全文（相等行与插入行）。</summary>
    public static string DiffTextB(DiffLine[]? diffs)
    {
        diffs ??= [];
        var s = new List<string>();
        foreach (var l in diffs)
        {
            if (l.Type == DiffType.Delete)
            {
                continue;
            }

            s.Add(l.Type == DiffType.Equal && l.Text.Length == 0 ? "" : TypeSymbol(l.Type) + l.Text);
        }

        return string.Join("\n", s);
    }

    /// <summary>Go: <c>func UnifiedDiffTextWithOptions(diffs []DiffLine, opts UnifiedDiffOptions) string</c>。</summary>
    public static string UnifiedDiffTextWithOptions(DiffLine[]? diffs, UnifiedDiffOptions opts)
    {
        // Go 里 makeHunks 可以返回 nil，`range nil` 是空迭代（不是 NRE），这里显式还原这个语义。
        var hunks = MakeHunks(diffs, opts.Precontext, opts.Postcontext) ?? [];
        var s = new List<string>();
        if (opts.SrcHeader.Length > 0)
        {
            s.Add($"--- {opts.SrcHeader}");
        }

        if (opts.DstHeader.Length > 0)
        {
            s.Add($"+++ {opts.DstHeader}");
        }

        foreach (var h in hunks)
        {
            s.Add($"@@ -{h.SrcStart},{h.SrcLines} +{h.DstStart},{h.DstLines} @@");
            foreach (var l in h.Diffs)
            {
                if (l.Type == DiffType.Equal && l.Text.Length == 0)
                {
                    s.Add("");
                }
                else
                {
                    s.Add(TypeSymbol(l.Type) + l.Text);
                }
            }
        }

        return string.Join("\n", s);
    }

    /// <summary>Go: <c>func UnifiedDiffText(diffs []DiffLine) string</c> —— 上下文 3 行的 unidiff。</summary>
    public static string UnifiedDiffText(DiffLine[]? diffs)
    {
        return UnifiedDiffTextWithOptions(diffs, new UnifiedDiffOptions { Precontext = 3, Postcontext = 3 });
    }
}
