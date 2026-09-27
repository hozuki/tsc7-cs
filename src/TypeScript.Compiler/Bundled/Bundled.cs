// ported from: internal/bundled/bundled.go
//
// 构建标签偏离：Go 用 embed.go（//go:build !noembed）/ noembed.go（//go:build noembed）两个文件二选一，
// 靠 `-tags noembed` 切换"内容编译进内存"还是"从可执行文件目录读盘"。C# 没有构建标签，而本移植的
// 108 个 .d.ts 始终作为 EmbeddedResource 随程序集发布（含 AOT/单文件发布），所以只移植 embed.go 的分支：
// Embedded 恒为 true、LibPath 是 "bundled:///libs"、IsBundled 走前缀判定。noembed.go 的
// "可执行文件目录里必须有 lib.d.ts，否则 panic" 分支不移植（把 108 个 .d.ts 复制到产物目录在本移植里没有场景）。
//
// TestingLibPath 的定位偏离：Go 用 runtime.Caller(0) 取 bundled.go 所在目录，C# 用 [CallerFilePath]
// （编译期写入调用点的源文件路径，语义相同）取 Bundled.cs 所在目录，再拼 "libs" —— 即本移植源码树里的
// src/TypeScript.Compiler/Bundled/libs（由 tools/TypeScript.CodeGen 从 TSGO 逐字节同步过来）。
// Go 的 testing.Testing() 断言（非测试期调用即 panic）在 C# 里没有对应物，不移植。
namespace TypeScript.Bundled;

using System.Runtime.CompilerServices;
using TypeScript.Vfs;

using static TypeScript.TSPath.TSPath;

/// <summary>Go 的 <c>bundled</c> 包级函数所在的静态类（embed.go 的部分见 Embed.cs，生成的部分见
/// Libs.Generated.cs / Embed.Generated.cs）。</summary>
public static partial class Bundled
{
    /// <summary>Go: <c>Embedded</c>（= embed.go 的 <c>embedded</c>）：bundled 文件由嵌入资源提供。见文件头。</summary>
    public const bool Embedded = true;

    /// <summary>Go: <c>WrapFS</c>：把 <c>bundled:///…</c> 路径重定向到嵌入资源，其余路径透传。</summary>
    public static FS WrapFS(FS fs) => WrapFSImpl(fs);

    /// <summary>Go: <c>LibPath()</c>：bundled libs 目录的路径（<c>bundled:///libs</c>，只能用 <see cref="WrapFS"/> 访问）。</summary>
    public static string LibPath() => LibPathImpl();

    /// <summary>Go: <c>TestingLibPath()</c>：源码树里 bundled libs 目录的路径，只在有源码可用的测试里有效。</summary>
    public static string TestingLibPath() => _testingLibPath.Value;

    // Go: sync.OnceValue —— Lazy 的默认模式（ExecutionAndPublication）同样是"只算一次 + 并发安全"。

    /// <summary>Go: <c>bundledSourceDir</c>：本文件所在目录（Go 是 bundled.go 所在目录）。</summary>
    private static readonly Lazy<string> _bundledSourceDir = new(() => GetBundledSourceDir());

    /// <summary>Go: <c>testingLibPath</c>：<c>NormalizeSlashes(filepath.Join(bundledSourceDir, "libs"))</c>。</summary>
    private static readonly Lazy<string> _testingLibPath = new(
        () => NormalizeSlashes(CombinePaths(_bundledSourceDir.Value, "libs")));

    private static string GetBundledSourceDir([CallerFilePath] string filename = "")
    {
        var dir = Path.GetDirectoryName(filename);
        if (string.IsNullOrEmpty(dir))
        {
            // Go: panic("bundled: could not get current filename")
            throw new DebugFailureException("bundled: could not get current filename");
        }

        return dir;
    }
}
