// ported from: github.com/peter-evans/patience@v0.3.0/lcs.go
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `[][2]int`（每个元素是定长 2 的数组，回溯时按 `x[0]`/`x[1]` 取值、也会被 `x[0] = ...` 改写）
//    → `List<int[]>`，每个元素是长度 2 的 int[]；保持 `pair[0]`/`pair[1]` 的取值与改写写法。
//    （不是 `List<List<int>>`：Go 的 `[2]int` 是定长数组，对应 C# 的数组而不是 List。）
// 2) Go 的二维表 `make([][]int, len(a)+1)` + 每行 `make([]int, len(b)+1)` → jagged `int[][]`，
//    零值同样是 0，行宽同样是 len(b)+1。
// 3) Go 的 `make([][2]int, 0, lcs[i][j])` → `new List<int[]>(capacity)`。
// 4) 包级 `max` 是 Go 1.21 之前的自定义实现（lcs.go 里定义）→ private `Max`，一行不改。
namespace TypeScript.Patience;

public static partial class Patience
{
    /// <summary>
    /// Go: <c>func LCS(a, b []string) [][2]int</c> —— 两个字符串切片的最长公共子序列，
    /// 返回 LCS 中元素在两边的下标对。
    /// </summary>
    public static List<int[]> LCS(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        // Initialize the LCS table.
        var lcs = new int[a.Count + 1][];
        for (var i = 0; i <= a.Count; i++)
        {
            lcs[i] = new int[b.Count + 1];
        }

        // Populate the LCS table.
        for (var i = 1; i < lcs.Length; i++)
        {
            for (var j = 1; j < lcs[i].Length; j++)
            {
                lcs[i][j] = a[i - 1] == b[j - 1]
                    ? lcs[i - 1][j - 1] + 1
                    : Max(lcs[i - 1][j], lcs[i][j - 1]);
            }
        }

        // Backtrack to find the LCS.
        var x = a.Count;
        var y = b.Count;
        var s = new List<int[]>(lcs[x][y]);
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1])
            {
                s.Add([x - 1, y - 1]);
                x--;
                y--;
            }
            else if (lcs[x - 1][y] > lcs[x][y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        // Reverse the backtracked LCS.
        s.Reverse();

        return s;
    }

    /// <summary>Go: <c>func max(a, b int) int</c>。</summary>
    private static int Max(int a, int b)
    {
        return a > b ? a : b;
    }
}
