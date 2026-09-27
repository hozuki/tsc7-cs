// ported from: internal/modulespecifiers/compare.go（13 行，整文件）
//
// 与 Go 的偏离：
// 1) `strings.HasPrefix(path, "./")` → `path.StartsWith("./", StringComparison.Ordinal)`：Go 的 HasPrefix 是
//    纯字节前缀比较，.NET 的 `StartsWith(string)` 默认是区域相关的，必须显式 Ordinal（与 StringUtil.HasPrefix
//    的实现同一语义，这里不引入该 helper 是因为调用点只有这一处）。
// 2) `strings.Count(path[initial:], "/")` 在 Go 里是零拷贝的子串扫描 → C# 用 `path.AsSpan(initial)` 逐个
//    代码单元计数（§3.8；"/" 是单字节，Go 的 Count 就是数出现次数）。
using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>func CountPathComponents(path string) int</c>。</summary>
    public static int CountPathComponents(string path)
    {
        var initial = 0;
        if (path.StartsWith("./", StringComparison.Ordinal))
        {
            initial = 2;
        }

        var count = 0;
        foreach (var c in path.AsSpan(initial))
        {
            if (c == '/')
            {
                count++;
            }
        }

        return count;
    }
}
