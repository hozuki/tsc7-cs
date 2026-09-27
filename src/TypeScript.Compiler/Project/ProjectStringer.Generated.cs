// ported from: internal/project/project_stringer_generated.go:1-26
//
// Go 的 stringer 生成物（`stringer -type=Kind -trimprefix=Kind`）：`Kind.String()`。
// C# 侧按仓内既有做法落成**扩展方法 + 表驱动**（architecture.md §3.2：Go 的 `String()` 方法 → C# 扩展方法，
// 先例 `Core/CompilerOptions.cs` 的 `ModuleResolutionKind.String()`）。生成物里的 `_Kind_name`/`_Kind_index`
// 两个表照抄，`_()` 里那组"常量值变了就编译报错"的断言用 C# 的 `Debug.Assert` 等价表达（Go 靠数组下标越界
// 在编译期发现，C# 没有这个技巧）。
//
// 与 Go 的偏离：
// 1) `Kind.String()` → `KindExtensions.String(this Kind)`（扩展方法，名字照 Go 保留）。
// 2) 越界时的 `"Kind(" + strconv.FormatInt(...) + ")"` → `$"Kind({(int)value})"`（不变文化整数格式，等价）。
// 3) `_()` 的编译期断言 → 静态构造里的 `Debug.Assert`（同样的意图：常量值一旦改动就立刻炸）。
using System.Globalization;

namespace TypeScript.Project;

public static class KindExtensions
{
    private const string KindName = "InferredConfiguredSynthetic";

    private static readonly byte[] KindIndex = [0, 8, 18, 27];

    static KindExtensions()
    {
        // Go: func _() { var x [1]struct{}; _ = x[KindInferred-0]; _ = x[KindConfigured-1]; _ = x[KindSynthetic-2] }
        Debug.Assert(KindIndex.Length == 4, "Kind stringer table is stale; re-run the stringer");
        Debug.Assert((int)Kind.KindInferred == 0, "Kind stringer table is stale; re-run the stringer");
        Debug.Assert((int)Kind.KindConfigured == 1, "Kind stringer table is stale; re-run the stringer");
        Debug.Assert((int)Kind.KindSynthetic == 2, "Kind stringer table is stale; re-run the stringer");
    }

    /// <summary>Go: <c>func (i Kind) String() string</c>（project_stringer_generated.go:20-26）。</summary>
    public static string String(this Kind value)
    {
        var idx = (int)value;
        if (idx < 0 || idx >= KindIndex.Length - 1)
        {
            return "Kind(" + idx.ToString(CultureInfo.InvariantCulture) + ")";
        }

        return KindName[KindIndex[idx]..KindIndex[idx + 1]];
    }
}
