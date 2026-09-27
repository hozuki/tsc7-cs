// ported from: internal/project/refcountcache_test.go:1-571
//
// 说明（与 Go 测试的差异，逐条）：
// 1) Go 的 `assert.NilError` → 无（C# 侧 `(T, error)` 已改成抛异常，失败路径直接用 `Assert.Throws`）。
//    `assert.Assert(t, cond)` → `Assert.True(cond)`；`assert.Equal(t, want, got)` → `Assert.Equal(want, got)`
//    （xunit 是 (expected, actual)）；`assert.DeepEqual` → `Assert.Equal`（两侧都实现了值相等，见 ParseCache.cs 文件头第 2 条）。
//    `assert.Assert(t, a == b)`（Go 的指针相等）→ `Assert.Same(a, b)`。
// 2) Go 测试直接用结构体字面量 + 内嵌/提升字段：`ContentMappedParseCacheKey{FileName: "/component.vue", Path: …}`
//    → C# 必须写 `new ContentMappedParseCacheKey { SourceFileParseOptions = new SourceFileParseOptions { … } }`
//    （C# 没有内嵌字段提升；见 parsecache.go 的 C# 文件头第 1 条）。
// 3) Go 的 `newOverlay(fileName, content, version, kind)`（overlayfs.go 的未导出构造函数，A 组的切片）在 C# 里换成
//    本文件的 `TestFileHandle` 替身（只实现 ParseCache 需要的 FileContent/FileHandle 成员）。
// 4) `TestRefCountingCaches`（refcountcache_test.go:87 起）整段依赖 `Session`/`Snapshot`/language service（跨切片集成），
//    本切片不移植；它的**确定性内核**（缓存条目引用计数从 1 起、Deref 归零即删、produce 失败不缓存、DisableDeletion、
//    Ref 缺条目报错）在这里逐条落成独立的 [Fact]（Go 侧那些断言分散在同名子测试里，行号见各用例注释）。
namespace TypeScript.Tests;

using System;
using System.Text;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Project;
using Xunit;
using static TypeScript.Parser.Parser;
using static TypeScript.Xxh3.Xxh3;
using Path = TypeScript.TSPath.Path;

public class ProjectRefCountCacheTests
{
    /// <summary>Go 测试里的 <c>newOverlay(...)</c> 替身（只实现 ParseCache 用到的成员）。</summary>
    private sealed class TestFileHandle(string fileName, string content, ScriptKind kind) : FileHandle
    {
        public string Content() => content;

        public UInt128 Hash() => HashString128(content);

        public string FileName() => fileName;

        public int Version() => 1;

        public bool MatchesDiskText() => true;

        public bool IsOverlay() => true;

        public TypeScript.LsConv.LSPLineMap? LSPLineMap() => null;

        public TypeScript.SourceMap.ECMALineInfo? ECMALineInfo() => null;

        public ScriptKind Kind() => kind;
    }

    /// <summary>
    /// 回归：Go 的 `ParseSourceFile(key.SourceFileParseOptions, …)` 传的是**结构体副本**，而 C# 的
    /// `SourceFile._parseOptions` 存引用（Ast/SourceFile.cs:985）。若解析时不复制，key 会与 SourceFile 共享同一个
    /// 可变 options 对象：就地改 `file.ParseOptions()` 会让字典 key 的哈希/相等性漂移，缓存静默损坏
    /// （`Has` 失效、`Ref`/`Deref` 找不到条目）。
    /// </summary>
    [Fact]
    public void ParseCacheDoesNotAliasKeyParseOptions()
    {
        const string fileName = "/alias.ts";
        var fileHandle = new TestFileHandle(fileName, "export const x = 1;", ScriptKind.TS);
        var key = ParseCacheKey.NewParseCacheKey(
            new SourceFileParseOptions { FileName = fileName, Path = (Path)fileName },
            fileHandle.Hash(),
            fileHandle.Kind());
        var cache = ParseCache.NewParseCache(new RefCountCacheOptions());

        var file = cache.Acquire(key, fileHandle);
        try
        {
            file.ParseOptions().FileName = "/mutated.ts";
            Assert.Equal(fileName, key.SourceFileParseOptions.FileName);
            Assert.True(cache.Has(key));
            Assert.Equal(1, cache._entries.Load(key).Value!._refCount);
        }
        finally
        {
            cache.Deref(key);
        }
    }

    /// <summary>Go: <c>TestContentMappedParseCacheBundleLifetime</c>（refcountcache_test.go:24-51）。</summary>
    [Fact]
    public void ContentMappedParseCacheBundleLifetime()
    {
        var cache = ContentMappedParseCache.NewContentMappedParseCache(new RefCountCacheOptions());
        var key = new ContentMappedParseCacheKey
        {
            SourceFileParseOptions = new SourceFileParseOptions { FileName = "/component.vue", Path = (Path)"/component.vue" },
        };
        var canonical = new SourceFile();
        var supplemental = new SourceFile();
        var produced = new SourceFiles { Canonical = canonical, Supplemental = [supplemental] };

        // 缓存把整份 transform 结果当成**一个**值，所以复用会保留每个文件的身份。
        var acquired = cache.AcquireOrError(key, () => produced);
        Assert.Same(canonical, acquired.Canonical);
        Assert.Same(supplemental, acquired.Supplemental[0]);
        var reused = cache.AcquireOrError(key, () => throw new InvalidOperationException("cached bundle should be reused"));
        Assert.Same(canonical, reused.Canonical);
        Assert.Same(supplemental, reused.Supplemental[0]);

        // 规范文件与补充文件共享 bundle 的引用计数，最后一次释放后一起消失。
        cache.Deref(key);
        Assert.True(cache.Has(key));
        cache.Deref(key);
        Assert.False(cache.Has(key));
    }

    /// <summary>Go: <c>TestContentMappedParseCacheKeyReconstruction</c>（refcountcache_test.go:53-79）。</summary>
    [Fact]
    public void ContentMappedParseCacheKeyReconstruction()
    {
        var acquireOptions = new SourceFileParseOptions { FileName = "/component.box", Path = (Path)"/component.box" };
        // Go: mappedOptions := acquireOptions; mappedOptions.ExternalModuleIndicatorOptions.Force = true（结构体复制）
        var mappedOptions = new SourceFileParseOptions
        {
            FileName = "/component.box",
            Path = (Path)"/component.box",
            ExternalModuleIndicatorOptions = new ExternalModuleIndicatorOptions { Force = true },
        };
        var hash = Hash128(Encoding.UTF8.GetBytes("cache key"));
        var file = ParseSourceFile(mappedOptions, "export {};", ScriptKind.TS);
        file.Hash = hash;
        file.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            ContentMapper = "mapper",
            ParseOptions = acquireOptions,
        });
        var expected = new ContentMappedParseCacheKey { SourceFileParseOptions = acquireOptions, Hash = hash };
        Assert.Equal(expected, ContentMappedParseCacheKey.ContentMappedParseCacheKeyForFile(file));

        var duplicate = new DuplicateSourceFile
        {
            ParseOptions = mappedOptions,
            ContentMapperParseOptions = acquireOptions,
            Hash = hash,
            ContentMapper = "mapper",
        };
        Assert.Equal(expected, ContentMappedParseCacheKey.ContentMappedParseCacheKeyForDuplicate(duplicate));
    }

    /// <summary>Go: <c>TestParseCacheBindsBeforePublishing</c>（refcountcache_test.go:81-98）。</summary>
    [Fact]
    public void ParseCacheBindsBeforePublishing()
    {
        const string fileName = "/index.js";
        var fileHandle = new TestFileHandle(fileName, "module.exports = 0;", ScriptKind.JS);
        var parseOptions = new SourceFileParseOptions { FileName = fileName, Path = (Path)fileName };
        var key = ParseCacheKey.NewParseCacheKey(parseOptions, fileHandle.Hash(), fileHandle.Kind());
        var cache = ParseCache.NewParseCache(new RefCountCacheOptions());

        var file = cache.Acquire(key, fileHandle);
        try
        {
            Assert.True(file.IsBound());
            Assert.NotNull(file.CommonJSModuleIndicator);
        }
        finally
        {
            cache.Deref(key);
        }
    }

    /// <summary>
    /// Go: <c>TestRefCountingCaches</c> 的 parseCache 子测试里"每个源文件恰好 1 个引用、Deref 到 0 即消失"
    /// 的确定性内核（refcountcache_test.go:120-150 等）；Session 部分见文件头第 4 条。
    /// </summary>
    [Fact]
    public void ParseCacheEntryRefCountStartsAtOneAndDerefRemoves()
    {
        const string fileName = "/index.ts";
        var fileHandle = new TestFileHandle(fileName, "export const x = 1;", ScriptKind.TS);
        var parseOptions = new SourceFileParseOptions { FileName = fileName, Path = (Path)fileName };
        var key = ParseCacheKey.NewParseCacheKey(parseOptions, fileHandle.Hash(), fileHandle.Kind());
        var cache = ParseCache.NewParseCache(new RefCountCacheOptions());

        var file = cache.Acquire(key, fileHandle);
        var (entry, ok) = cache._entries.Load(key);
        Assert.True(ok);
        Assert.Equal(1, entry!._refCount);
        Assert.Same(file, entry._value);

        cache.Deref(key);
        Assert.False(cache.Has(key));
    }

    /// <summary>Go: <c>refcountcache.go:112-136</c>（loadOrStoreNewLockedEntry 对已有条目自增）+ 测试里的 `mainEntry.refCount + 1`。</summary>
    [Fact]
    public void AcquireOnExistingEntryIncrementsRefCount()
    {
        var cache = RefCountCache<string, int, object?>.NewRefCountCache(new RefCountCacheOptions(), static (_, _) => 42);
        Assert.Equal(42, cache.Acquire("k", null));
        var (first, ok) = cache._entries.Load("k");
        Assert.True(ok);
        Assert.Equal(1, first!._refCount);

        Assert.Equal(42, cache.Acquire("k", null));
        Assert.Equal(2, first._refCount);

        cache.Deref("k");
        Assert.True(cache.Has("k"));
        cache.Deref("k");
        Assert.False(cache.Has("k"));
    }

    /// <summary>Go: <c>refcountcache.go:98-110</c>（Deref 对缺失键是空操作）。</summary>
    [Fact]
    public void DerefMissingEntryIsNoop()
    {
        var cache = RefCountCache<string, int, object?>.NewRefCountCache(new RefCountCacheOptions(), static (_, _) => 1);
        cache.Deref("missing");
        Assert.False(cache.Has("missing"));
    }

    /// <summary>Go: <c>refcountcache.go:80-96</c>（Ref 对缺失条目 panic → C# 抛 DebugFailureException）。</summary>
    [Fact]
    public void RefMissingEntryThrows()
    {
        var cache = RefCountCache<string, int, object?>.NewRefCountCache(new RefCountCacheOptions(), static (_, _) => 1);
        Assert.Throws<DebugFailureException>(() => cache.Ref("missing"));
    }

    /// <summary>Go: <c>RefCountCacheOptions{DisableDeletion: true}</c>（refcountcache.go:98-110 的 `!c.Options.DisableDeletion`）。</summary>
    [Fact]
    public void DisableDeletionKeepsEntries()
    {
        var cache = RefCountCache<string, int, object?>.NewRefCountCache(
            new RefCountCacheOptions { DisableDeletion = true },
            static (_, _) => 1);
        Assert.Equal(1, cache.Acquire("k", null));
        cache.Deref("k");
        Assert.True(cache.Has("k"));
        Assert.Equal(0, cache._entries.Load("k").Value!._refCount);
    }

    /// <summary>Go: <c>AcquireOrError</c>（refcountcache.go:59-78）：produce 失败不进缓存，ref 也不自增。</summary>
    [Fact]
    public void AcquireOrErrorDoesNotCacheFailures()
    {
        var cache = RefCountCache<string, int, object?>.NewRefCountCache(
            new RefCountCacheOptions(),
            static (_, _) => throw new InvalidOperationException("parse must not be used"));

        Assert.Throws<InvalidOperationException>(() => cache.AcquireOrError("x", static () => throw new InvalidOperationException("produce")));
        Assert.False(cache.Has("x"));

        // 失败后再成功：条目是新建的，引用计数为 1。
        Assert.Equal(5, cache.AcquireOrError("x", static () => 5));
        Assert.Equal(1, cache._entries.Load("x").Value!._refCount);
    }

    /// <summary>
    /// Go 的 map 键是**值类型**（结构相等）；C# 的 key 类手动实现 IEquatable/GetHashCode（ParseCache.cs 文件头第 2 条）。
    /// 这些断言在 Go 里隐含于"同 options 的 key 命中同一缓存"（refcountcache_test.go:120-150）。
    /// </summary>
    [Fact]
    public void ParseCacheKeyUsesStructuralEquality()
    {
        var hash = HashString128("cache key");
        var left = ParseCacheKey.NewParseCacheKey(new SourceFileParseOptions { FileName = "/a.ts", Path = (Path)"/a.ts" }, hash, ScriptKind.TS);
        var right = ParseCacheKey.NewParseCacheKey(new SourceFileParseOptions { FileName = "/a.ts", Path = (Path)"/a.ts" }, hash, ScriptKind.TS);
        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());

        // 外部改动原 options 不应影响 key（Go 是结构体复制；C# 复制于构造点）。
        var mutable = new SourceFileParseOptions { FileName = "/a.ts", Path = (Path)"/a.ts" };
        var snapshot = ParseCacheKey.NewParseCacheKey(mutable, hash, ScriptKind.TS);
        mutable.FileName = "/b.ts";
        Assert.Equal(left, snapshot);

        Assert.NotEqual(left, ParseCacheKey.NewParseCacheKey(new SourceFileParseOptions { FileName = "/a.ts", Path = (Path)"/a.ts" }, hash, ScriptKind.TSX));
        Assert.NotEqual(left, ParseCacheKey.NewParseCacheKey(new SourceFileParseOptions { FileName = "/b.ts", Path = (Path)"/b.ts" }, hash, ScriptKind.TS));

        // Go: NewParseCacheKey 在 ScriptKindUnknown 时按文件名推断（parsecache.go:22-33）。
        var inferred = ParseCacheKey.NewParseCacheKey(new SourceFileParseOptions { FileName = "/a.jsx", Path = (Path)"/a.jsx" }, hash, ScriptKind.Unknown);
        Assert.Equal(ScriptKind.JSX, inferred.ScriptKind);

        // ExternalModuleIndicatorOptions 参与结构相等（Go 的结构体比较含该字段）。
        var forced = ParseCacheKey.NewParseCacheKey(
            new SourceFileParseOptions
            {
                FileName = "/a.ts",
                Path = (Path)"/a.ts",
                ExternalModuleIndicatorOptions = new ExternalModuleIndicatorOptions { Force = true },
            },
            hash,
            ScriptKind.TS);
        Assert.NotEqual(left, forced);
    }

    /// <summary>Go: <c>ContentMappedParseCacheKey</c> 的 Hash 折叠了内容/mapper identity/locale（parsecache.go:42-48）。</summary>
    [Fact]
    public void ContentMappedParseCacheKeyFoldsHashAndLocale()
    {
        var options = new SourceFileParseOptions { FileName = "/component.box", Path = (Path)"/component.box" };
        var rawHash = HashString128("raw");
        var transformIdentity = HashString128("identity");
        var first = ContentMappedParseCacheKey.NewContentMappedParseCacheKey(options, rawHash, transformIdentity, TypeScript.Locale.Locale.Default);
        var same = ContentMappedParseCacheKey.NewContentMappedParseCacheKey(
            new SourceFileParseOptions { FileName = "/component.box", Path = (Path)"/component.box" },
            rawHash,
            transformIdentity,
            TypeScript.Locale.Locale.Default);
        Assert.Equal(first, same);

        var otherIdentity = ContentMappedParseCacheKey.NewContentMappedParseCacheKey(
            options, rawHash, HashString128("identity2"), TypeScript.Locale.Locale.Default);
        Assert.NotEqual(first, otherIdentity);

        var otherContent = ContentMappedParseCacheKey.NewContentMappedParseCacheKey(
            options, HashString128("raw2"), transformIdentity, TypeScript.Locale.Locale.Default);
        Assert.NotEqual(first, otherContent);
    }
}
