// ported from: internal/tspath/ignoredpaths.go
namespace TypeScript.TSPath;

public static partial class TSPath
{
    // Go 的 unexported 包级变量 → private。
    private static readonly string[] ignoredPaths =
    [
        "/node_modules/.",
        "/.git",
        ".#",
    ];

    public static bool ContainsIgnoredPath(string path)
    {
        // Go 用 strings.Contains（字节级子串查找）；C# 用序数子串查找，结果一致。
        foreach (var pattern in ignoredPaths)
        {
            if (path.Contains(pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
