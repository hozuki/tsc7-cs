// ported from: internal/bundled/bundled_test.go
//
// 移植映射：
//   * Go 的 TestTestingLibPath / TestEmbeddedLibs 逐条对应（os.Stat → Directory.Exists/File.Exists；
//     slices.Sort → Array.Sort(…, StringComparer.Ordinal)）。
//   * 额外的断言是本移植特有的（Go 侧"目录内容 vs 嵌入内容"由 go:embed 在编译期绑定，C# 侧是
//     "生成器逐字节复制 + csproj 的 EmbeddedResource"两步，需要断言这两步没跑偏）：
//       - LibNames 与源码树里的 Bundled/libs（TestingLibPath）逐名一致（数量 + 顺序）；
//       - 108 个 lib 的 LoadLibBytes 与源码树里同名文件逐字节一致；
//       - 代表 lib（lib.d.ts / lib.es5.d.ts / lib.dom.d.ts）与 TSGO 检出里的同名文件逐字节一致
//         （TSGO_ROOT 语料路径，缺失则跳过——写法与 Diagnostics/ParserCorpus 的探测一致）。
//   * Go 的 t.Parallel() → xunit 的每条 [Fact]/[Theory] 各自并行。
using System.Security.Cryptography;
using TypeScript;
using TypeScript.Bundled;
using TypeScript.Vfs.OsVfs;
using Xunit;

using static TypeScript.Bundled.Bundled;

namespace TypeScript.Tests;

public class BundledTests
{
    // 与 docs/architecture.md §5 一致：TSGO 仓库根目录由 TSGO_ROOT 指定，缺省探测 D:\Source\GitHub\TypeScript-Go。
    private static readonly string _tsgoRoot =
        Environment.GetEnvironmentVariable("TSGO_ROOT") is { Length: > 0 } root ? root : @"D:\Source\GitHub\TypeScript-Go";

    /// <summary>Go 的 TestTestingLibPath。</summary>
    [Fact]
    public void TestingLibPathExists()
    {
        var p = TestingLibPath();

        Assert.True(Directory.Exists(p), $"TestingLibPath() = {p}");
        Assert.True(File.Exists(Path.Combine(p, "lib.d.ts")), p);
    }

    /// <summary>Go 的 TestEmbeddedLibs（Go 用 assert.DeepEqual 同时锁住数量与内容）。</summary>
    [Fact]
    public void EmbeddedLibsMatchLibNames()
    {
        var fs = WrapFS(Osvfs.FS());

        var files = fs.GetAccessibleEntries(LibPath()).Files;
        Array.Sort(files, StringComparer.Ordinal);

        Assert.Equal(108, LibNames.Length);
        Assert.Equal(LibNames, files);
    }

    /// <summary>LibNames 与源码树里的 bundled libs 目录逐名一致（数量 + 内容 + 顺序）。</summary>
    [Fact]
    public void LibNamesMatchSourceLibsDirectory()
    {
        var names = Directory.GetFiles(TestingLibPath(), "*.d.ts").Select(Path.GetFileName).OfType<string>().ToArray();
        Array.Sort(names, StringComparer.Ordinal);

        Assert.Equal(LibNames, names);
    }

    /// <summary>108 个嵌入资源都能读出来，且与源码树里的同名文件逐字节一致（资源名表/LogicalName 的护栏）。</summary>
    [Fact]
    public void AllLibsMatchSourceLibsBytes()
    {
        var dir = TestingLibPath();

        foreach (var name in LibNames)
        {
            var path = Path.Combine(dir, name);
            var expected = File.ReadAllBytes(path);
            var actual = LoadLibBytes(name);

            Assert.True(
                expected.AsSpan().SequenceEqual(actual),
                $"{name}: 嵌入资源 {actual.Length} 字节，源文件 {expected.Length} 字节（{path}）");
        }
    }

    /// <summary>代表 lib 与 TSGO 检出里的同名文件逐字节一致（无 TSGO 检出时跳过）。</summary>
    [Theory]
    [InlineData("lib.d.ts")]
    [InlineData("lib.es5.d.ts")]
    [InlineData("lib.dom.d.ts")]
    public void RepresentativeLibsMatchTsgoFiles(string name)
    {
        var tsgoLibsDir = Path.Combine(_tsgoRoot, "tsc", "internal", "bundled", "libs");
        if (!Directory.Exists(tsgoLibsDir))
        {
            return; // 需要 TSGO 检出（TSGO_ROOT），没有就跳过。
        }

        var tsgoPath = Path.Combine(tsgoLibsDir, name);
        var expected = File.ReadAllBytes(tsgoPath);
        var actual = LoadLibBytes(name);

        Assert.True(
            expected.AsSpan().SequenceEqual(actual),
            $"{name}: 我们 {Sha256(actual)}，TSGO {Sha256(expected)}");

        // 文本 API（LoadLib）也要与文件文本一致。
        Assert.True(TryLoadLib(name, out var contents));
        Assert.Equal(File.ReadAllText(tsgoPath), contents);
    }

    /// <summary>Go 的 IsBundled / LibPath（embed.go）：scheme 判定与 bundled libs 目录路径。</summary>
    [Fact]
    public void IsBundledRecognizesScheme()
    {
        Assert.Equal("bundled:///libs", LibPath());

        Assert.True(IsBundled("bundled:///"));
        Assert.True(IsBundled("bundled:///libs/lib.d.ts"));

        Assert.False(IsBundled("/lib.d.ts"));
        Assert.False(IsBundled("C:/lib.d.ts"));
        Assert.False(IsBundled(""));
    }

    /// <summary>Go 的 wrappedFS（embed.go）：bundled:/// 路径全部由嵌入资源回答。</summary>
    [Fact]
    public void WrappedFsServesBundledPaths()
    {
        var fs = WrapFS(Osvfs.FS());
        const string libPath = "bundled:///libs/lib.es5.d.ts";

        Assert.True(fs.FileExists(libPath));
        Assert.False(fs.FileExists("bundled:///libs/lib.nope.d.ts"));

        Assert.True(fs.DirectoryExists("bundled:///libs"));
        Assert.False(fs.DirectoryExists("bundled:///"));

        var root = fs.GetAccessibleEntries("bundled:///");
        Assert.Equal(["libs"], root.Directories);
        Assert.Empty(root.Files);

        Assert.True(fs.TryReadFile(libPath, out var contents));
        Assert.Equal(LoadLib("lib.es5.d.ts"), contents);
        Assert.False(fs.TryReadFile("bundled:///libs/lib.nope.d.ts", out _));

        var info = fs.Stat(libPath);
        Assert.NotNull(info);
        Assert.Equal("lib.es5.d.ts", info.Name);
        Assert.Equal(LoadLibBytes("lib.es5.d.ts").Length, info.Size);
        Assert.False(info.IsDirectory);
        Assert.Equal(libPath, info.FullName);

        var dirInfo = fs.Stat("bundled:///");
        Assert.NotNull(dirInfo);
        Assert.True(dirInfo.IsDirectory);
        Assert.Equal("", dirInfo.Name);

        Assert.Null(fs.Stat("bundled:///libs/lib.nope.d.ts"));
        Assert.Equal(libPath, fs.Realpath(libPath));

        // Go 的三个 panic（写/删/改时间）在 C# 里是 Debug.Fail → DebugFailureException。
        Assert.Throws<DebugFailureException>(() => fs.WriteFile(libPath, "x"));
        Assert.Throws<DebugFailureException>(() => fs.AppendFile(libPath, "x"));
        Assert.Throws<DebugFailureException>(() => fs.Remove(libPath));
        Assert.Throws<DebugFailureException>(() => fs.Chtimes(libPath, default, default));
    }

    /// <summary>非 bundled 路径透传给被包装的 FS（Go 的 wrappedFS 对每个方法都有该分支）。</summary>
    [Fact]
    public void WrappedFsPassesThroughOtherPaths()
    {
        var fs = WrapFS(Osvfs.FS());
        // TestingLibPath() 已是 NormalizeSlashes 后的路径，这里也用 '/' 拼（osvfs 的 Realpath 返回正斜杠）。
        var sourceLib = TestingLibPath() + "/lib.d.ts";

        Assert.False(IsBundled(sourceLib));
        Assert.True(fs.FileExists(sourceLib));
        Assert.True(fs.TryReadFile(sourceLib, out var contents));
        Assert.Equal(File.ReadAllText(sourceLib), contents);
        Assert.True(fs.DirectoryExists(TestingLibPath()));
        Assert.Equal(sourceLib, fs.Realpath(sourceLib));
        Assert.False(fs.DirectoryExists("bundled:///nonexistent"));
    }

    /// <summary>缺失的 lib 名：TryLoadLib 返回 false，LoadLib 抛（Go 的表查不到就没有值）。</summary>
    [Fact]
    public void MissingLibIsRejected()
    {
        Assert.False(TryLoadLib("lib.nope.d.ts", out var contents));
        Assert.Equal("", contents);
        Assert.Throws<DebugFailureException>(() => LoadLib("lib.nope.d.ts"));
        Assert.Throws<DebugFailureException>(() => LoadLibBytes("lib.nope.d.ts"));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
