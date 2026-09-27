// ported from: internal/project/extendedconfigcache_test.go:1-323
//
// 说明（与 Go 测试的差异，逐条）：
// 1) Go 的 `TestExtendedConfigCacheOwnership`（extendedconfigcache_test.go:71-302）整体依赖 `Session`/`Snapshot`/
//    language service（跨切片集成，由 M8Project/M8Project.ProjHub 的会话切片落地），本切片不移植；这里移植它的
//    **确定性内核**：
//      - "ExtendedSourceFiles can contain same path twice (case-insensitive)"（extendedconfigcache_test.go:129-171）—— 不依赖 Session，
//        原样移植为 `ExtendedSourceFilesCanContainSamePathTwiceCaseInsensitive`。
//      - 该文件的核心不变量（"每个 ExtendedSourceFile 在缓存里对每个引用它的 owner 恰好记一次，owner 清零即释放"）
//        落成针对 `ExtendedConfigCache`（= OwnerCache）的确定性用例。
// 2) Go 的 `assert.Equal(t, len(diags), 0)` → `Assert.Empty(diags)`；`assert.NilError` → 无（C# 侧已改成抛异常）。
// 3) Go 的 `h := &testParseConfigHost{fs: fs, cwd: "/"}` → 本文件的 `TestParseConfigHost`（实现
//    `TsOptions.ParseConfigHost`：FS() + GetCurrentDirectory()）。
// 4) Go 的 `cmd.ExtendedSourceFiles()` 是 `*tsoptions.ParsedCommandLine` 的方法，C# 同名（先例 fileloader.go 的移植）。
// 5) Go 的 `tsoptions.GetParsedCommandLineOfConfigFile` 返回 `(*ParsedCommandLine, []*ast.Diagnostic)` →
//    C# `(ParsedCommandLine? CommandLine, Diagnostic[] Errors)`（TsConfigParsing.FileNames.cs:1218）。
// 6) `ExtendedConfigParseArgs`/`ExtendedConfigCacheEntry` 是 project 包的**未导出**类型 → 测试靠
//    InternalsVisibleTo("TypeScript.Compiler.Tests") 访问（同 dirty 测试读 `_proxyFor` 的做法）。
namespace TypeScript.Tests;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Project;
using TypeScript.Vfs;
using Xunit;
using static TypeScript.Bundled.Bundled;
using static TypeScript.Core.Core;
using static TypeScript.Vfs.VfsTest.VfsTest;
using FS = TypeScript.Vfs.FS;
using Path = TypeScript.TSPath.Path;
using TSOpts = TypeScript.TsOptions.TsOptions;

public class ProjectExtendedConfigCacheTests
{
    private sealed class TestParseConfigHost(FS fs, string cwd) : TSOpts.ParseConfigHost
    {
        public FS FS() => fs;

        public string GetCurrentDirectory() => cwd;
    }

    /// <summary>Go 测试里 overlay/vfstest 的 FileHandle 替身（本文件只用到 Content/Hash）。</summary>
    private sealed class TestFileHandle(string fileName, string content) : FileHandle
    {
        public string Content() => content;

        public UInt128 Hash() => TypeScript.Xxh3.Xxh3.HashString128(content);

        public string FileName() => fileName;

        public int Version() => 1;

        public bool MatchesDiskText() => true;

        public bool IsOverlay() => false;

        public TypeScript.LsConv.LSPLineMap? LSPLineMap() => null;

        public TypeScript.SourceMap.ECMALineInfo? ECMALineInfo() => null;

        public ScriptKind Kind() => EnsureScriptKindFromFileName(fileName);
    }

    /// <summary>`FileSource` 替身：直接读底层 FS（`ExtendedConfigCache.hash` 只用到 GetFile）。</summary>
    private sealed class TestFileSource(FS fs) : FileSource
    {
        public FS FS() => fs;

        public FileHandle? GetFile(string fileName) => GetFileByPath(fileName, (Path)fileName);

        public FileHandle? GetFileByPath(string fileName, Path path)
        {
            return fs.TryReadFile(fileName, out var content) ? new TestFileHandle(fileName, content) : null;
        }

        public bool FileExists(string fileName, Path path) => fs.FileExists(fileName);

        public Entries GetAccessibleEntries(string path) => fs.GetAccessibleEntries(path);
    }

    private static (FS Fs, TestFileSource Source, TestParseConfigHost Host) Setup(Dictionary<string, string> files, bool caseSensitive)
    {
        var fs = WrapFS(FromMap(files, caseSensitive));
        return (fs, new TestFileSource(fs), new TestParseConfigHost(fs, "/"));
    }

    /// <summary>
    /// Go: <c>TestExtendedConfigCacheOwnership/"ExtendedSourceFiles can contain same path twice (case-insensitive)"</c>
    /// （extendedconfigcache_test.go:129-171）。这个用例刻意绕过项目系统的 ExtendedConfigCache，
    /// 直接观察大小写不敏感 FS 上"同一文件以不同大小写被 extends 两次"时 ExtendedSourceFiles 的样子。
    /// </summary>
    [Fact]
    public void ExtendedSourceFilesCanContainSamePathTwiceCaseInsensitive()
    {
        var files = new Dictionary<string, string>
        {
            ["/project/tsconfig.json"] = """
                {
                    "extends": ["./Shared.json", "./shared.json"]
                }
                """,
            ["/project/shared.json"] = """
                {
                    "compilerOptions": {"strict": true}
                }
                """,
        };

        var (fs, _, _) = Setup(files, caseSensitive: false);
        var h = new TestParseConfigHost(fs, "/");
        var (cmd, diags) = TSOpts.GetParsedCommandLineOfConfigFile("/project/tsconfig.json", null, null, h, null);

        Assert.Empty(diags);
        Assert.NotNull(cmd);

        var extended = cmd!.ExtendedSourceFiles()!;
        Assert.Equal(2, extended.Length);
        Assert.Equal("/project/Shared.json", extended[0]);
        Assert.Equal("/project/shared.json", extended[1]);
    }

    private static (ExtendedConfigCache Cache, Path Path, ExtendedConfigParseArgs Args) SetupCache(
        Dictionary<string, string> files,
        string fileName)
    {
        var (fs, source, host) = Setup(files, caseSensitive: true);
        var cache = ExtendedConfigCache.NewExtendedConfigCache();
        var args = new ExtendedConfigParseArgs
        {
            FileName = fileName,
            Content = files[fileName],
            FS = source,
            ResolutionStack = null,
            Host = host,
            Cache = null,
        };
        return (cache, (Path)fileName, args);
    }

    /// <summary>
    /// Go: <c>TestExtendedConfigCacheOwnership</c> 的核心不变量（extendedconfigcache_test.go:94-105 的 `ownerCount`/
    /// `assertNoEntry`，以及 176-213 的"同一 owner 只记一次"）：owner 记录在条目上，最后一个 owner 释放时条目消失。
    /// </summary>
    [Fact]
    public void ExtendedConfigCacheTracksOwnersAndReleasesEntries()
    {
        var files = new Dictionary<string, string>
        {
            ["/project/tsconfig.base.json"] = """
                {
                    "extends": "./tsconfig.common.json",
                    "compilerOptions": {"strict": true}
                }
                """,
            ["/project/tsconfig.common.json"] = """
                {
                    "compilerOptions": {"target": "ES2020"}
                }
                """,
        };
        var (cache, path, args) = SetupCache(files, "/project/tsconfig.base.json");
        Assert.False(cache.Has(path));

        // 解析出整条 extends 链（base → common）。
        var first = cache.LoadAndAcquire(path, 1, args);
        Assert.True(cache.Has(path));
        Assert.Contains("/project/tsconfig.common.json", first.ExtendedFileNames());
        Assert.Equal(1, OwnerCount(cache, path));

        // 同一个 owner 再获取一次：仍是同一条目、owner 集合不增长（Go 的 map[uint64]struct{} 语义）。
        Assert.Same(first, cache.LoadAndAcquire(path, 1, args));
        Assert.Equal(1, OwnerCount(cache, path));

        // 第二个 owner（另一个 snapshot）加入：条目保留、owner 变 2。
        Assert.Same(first, cache.LoadAndAcquire(path, 2, args));
        Assert.Equal(2, OwnerCount(cache, path));

        // 只释放一个 owner 时条目仍在。
        cache.Release(path, 1);
        Assert.True(cache.Has(path));
        Assert.Equal(1, OwnerCount(cache, path));

        // 最后一个 owner 释放后条目消失（Go 的 `assertNoEntry`）。
        cache.Release(path, 2);
        Assert.False(cache.Has(path));
    }

    /// <summary>
    /// Go: <c>extendedconfigcache.go:25-38</c> 的 isExpired 闭包：内容未变时复用同一条目，内容变化必须**重新解析**
    /// （返回新对象，因为 parse 每次都新建 ExtendedConfigCacheEntry）。
    /// </summary>
    [Fact]
    public void ExtendedConfigCacheReparsesWhenContentChanges()
    {
        var files = new Dictionary<string, string>
        {
            ["/project/tsconfig.base.json"] = """
                {
                    "compilerOptions": {"strict": true}
                }
                """,
        };
        var (cache, path, args) = SetupCache(files, "/project/tsconfig.base.json");
        var first = cache.LoadAndAcquire(path, 1, args);
        Assert.Same(first, cache.LoadAndAcquire(path, 1, args));

        // 同一路径、内容不同 → hash 不同 → isExpired 为真 → 重新解析。
        var changedFiles = new Dictionary<string, string>
        {
            ["/project/tsconfig.base.json"] = """
                {
                    "compilerOptions": {"strict": true, "target": "ES2020"}
                }
                """,
        };
        var (_, source, host) = Setup(changedFiles, caseSensitive: true);
        var changedArgs = new ExtendedConfigParseArgs
        {
            FileName = "/project/tsconfig.base.json",
            Content = changedFiles["/project/tsconfig.base.json"],
            FS = source,
            ResolutionStack = null,
            Host = host,
            Cache = null,
        };
        var second = cache.LoadAndAcquire(path, 1, changedArgs);
        Assert.NotSame(first, second);
    }

    /// <summary>Go: <c>OwnerCache.Release</c>（ownercache.go:79-92）：缺失条目是空操作。</summary>
    [Fact]
    public void ExtendedConfigCacheReleaseMissingEntryIsNoop()
    {
        var (_, path, _) = SetupCache(
            new Dictionary<string, string> { ["/project/tsconfig.base.json"] = "{}" },
            "/project/tsconfig.base.json");
        var cache = ExtendedConfigCache.NewExtendedConfigCache();
        cache.Release(path, 7);
        Assert.False(cache.Has(path));
    }

    private static int OwnerCount(ExtendedConfigCache cache, Path path)
    {
        var (entry, ok) = cache._entries.Load(path);
        Assert.True(ok);
        return entry!._owners.Count;
    }
}
