// ported from: internal/core/binarysearch.go
//
// 与 Go 的差异（依据 docs/architecture.md §3.2 与 TSPath/Path.cs 文件头定下的项目约定）：
// - Go 的 `(T, bool)` 统一映射成 `bool TryX(..., out T)`，所以 BinarySearchUniqueFunc 变成
//   TryBinarySearchUniqueFunc：Go 的 `i, ok := core.BinarySearchUniqueFunc(x, cmp)` 对应
//   `var ok = TryBinarySearchUniqueFunc(x, cmp, out var i)`。返回的下标语义不变（找不到时是插入点）。
// - Go 的 `S ~[]E`（任意切片类型）在 C# 里统一成 `E[]`。
namespace TypeScript.Core;

public static partial class Core
{
    // BinarySearchUniqueFunc works like [slices.BinarySearchFunc], but avoids extra
    // invocations of the comparison function by assuming that only one element
    // in the slice could match the target. Also, unlike [slices.BinarySearchFunc],
    // the comparison function is passed the current index of the element being
    // compared, instead of the target element.
    public static bool TryBinarySearchUniqueFunc<E>(E[] x, Func<int, E, int> cmp, out int index)
    {
        var n = x.Length;
        if (n == 0)
        {
            index = 0;
            return false;
        }

        var low = 0;
        var high = n - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var value = cmp(middle, x[middle]);
            if (value < 0)
            {
                low = middle + 1;
            }
            else if (value > 0)
            {
                high = middle - 1;
            }
            else
            {
                index = middle;
                return true;
            }
        }

        index = low;
        return false;
    }
}
