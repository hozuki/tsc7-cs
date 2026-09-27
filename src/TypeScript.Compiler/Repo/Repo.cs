// ported from: internal/repo/repo.go
//
// 与 Go 的差异（逐条）：
// 1) Go 用 `runtime.Caller(0)` 拿到**源文件**路径再向上找 `go.mod`，从而定位仓库根。C# 没有"当前源文件"的概念，
//    所以改成环境变量 `TSGO_ROOT`（默认 `D:\Source\GitHub\TypeScript-Go`，与 docs/harness.md、artifacts/ 下各探针
//    的默认值一致）：
//      * `<TSGO_ROOT>/go.mod` 存在          → 根 = `<TSGO_ROOT>`（TSGO 把 go.mod 放在仓库根的情形）
//      * `<TSGO_ROOT>/tsc/go.mod` 存在      → 根 = `<TSGO_ROOT>/tsc`（本机检出的实际布局，等价于 Go 的 runtime.Caller 结果）
//      * 都不存在                            → 照 Go 的 panic 语义 `Debug.Fail`（不会静默给出错误路径）
// 2) Go 的 `sync.OnceValue` → `Lazy<T>`（语义相同：只算一次，并发安全）。
// 3) Go 的 `filepath.Join` → `TypeScript.TSPath.TSPath.CombinePaths`（同一套路径规范化，harness 其余部分也用这个）。

namespace TypeScript.Repo;

using static TypeScript.TSPath.TSPath;

/// <summary>Go: <c>package repo</c>（仓库根 / testdata 路径）。</summary>
public static class Repo
{
    /// <summary>Go: <c>var rootPath = sync.OnceValue(...)</c>。</summary>
    private static readonly Lazy<string> _rootPath = new(FindRootPath);

    /// <summary>Go: <c>var testDataPath = sync.OnceValue(...)</c>。</summary>
    private static readonly Lazy<string> _testDataPath = new(() => CombinePaths(RootPath(), "testdata"));

    /// <summary>Go: <c>func RootPath() string</c>（含 <c>go.mod</c> 的目录，本机即 TSGO 检出的 <c>tsc/</c>）。</summary>
    public static string RootPath() => _rootPath.Value;

    /// <summary>Go: <c>func TestDataPath() string</c>（<c>&lt;root&gt;/testdata</c>，baseline 就在这下面的 <c>baselines/</c>）。</summary>
    public static string TestDataPath() => _testDataPath.Value;

    /// <summary>Go: <c>rootPath</c> 的 <c>sync.OnceValue</c> 函数体（见文件头第 1 条）。</summary>
    private static string FindRootPath()
    {
        var tsgoRoot = Environment.GetEnvironmentVariable("TSGO_ROOT");
        if (string.IsNullOrEmpty(tsgoRoot))
        {
            tsgoRoot = @"D:\Source\GitHub\TypeScript-Go";
        }

        tsgoRoot = GetNormalizedAbsolutePath(tsgoRoot, CurrentDirectory());

        if (File.Exists(CombinePaths(tsgoRoot, "go.mod")))
        {
            return tsgoRoot;
        }

        var nested = CombinePaths(tsgoRoot, "tsc");
        if (File.Exists(CombinePaths(nested, "go.mod")))
        {
            return nested;
        }

        // Go 在这个分支是 `panic("could not find go.mod above " + filename)`。
        Debug.Fail($"could not find go.mod in '{tsgoRoot}' or '{nested}' (set TSGO_ROOT to the TypeScript-Go checkout)");
        return tsgoRoot;
    }

    /// <summary>Go: <c>os.Getwd()</c> 的替身（<c>GetNormalizedAbsolutePath</c> 需要它把相对路径绝对化）。</summary>
    private static string CurrentDirectory() => Directory.GetCurrentDirectory();
}
