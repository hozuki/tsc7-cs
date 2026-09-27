// ported from: internal/project/parsecache.go:1-96
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `ParseCacheKey` **内嵌** `ast.SourceFileParseOptions`（提升字段：`key.FileName`/`key.Path` 直接可用）。
//    C# 没有字段提升，故内嵌落成组合字段 `SourceFileParseOptions`（public），Go 侧 `key.FileName` 在 C# 里是
//    `key.SourceFileParseOptions.FileName`。Go 测试里的字面量 `ContentMappedParseCacheKey{FileName: …}` 因此要写成
//    `new ContentMappedParseCacheKey { SourceFileParseOptions = new SourceFileParseOptions { FileName = … } }`
//    （测试里已按此改写，见 tests/TypeScript.Compiler.Tests/Project/*Tests.cs 的报告）。
// 2) Go 的这两个 key 是**值类型**（用作 map 键 ⇒ 按字段结构相等）。C# 的 `SourceFileParseOptions` 是可变 class
//    （引用相等），所以两个 key 类型手动实现 `IEquatable<T>` + `Equals`/`GetHashCode`，逐字段复刻 Go 的结构比较：
//    FileName、Path（`readonly record struct` 已有值相等）、ExternalModuleIndicatorOptions.JSX/.Force、
//    ScriptKind、Hash（`System.UInt128`）。这是**语义必需的偏离**（否则缓存永远不命中）。
// 2b) 同源问题：Go 构造 key 时写的是 `SourceFileParseOptions` 的**结构体副本**（值语义），C# 存的是可变 class 引用 ⇒
//    所有构造点都用 `ContentMapper.CloneParseOptions(...)`（Transform.cs:165 已有的内部函数）显式复制，
//    复刻 Go 的值语义；否则调用方后来改写 options 会让 key 的哈希/相等性漂移，字典直接损坏。
//    同一理由也适用于 `NewParseCache` 的解析函数：Go 的 `parser.ParseSourceFile(key.SourceFileParseOptions, …)`
//    传的是副本，而 C# 的 `SourceFile._parseOptions` 直接存引用（Ast/SourceFile.cs:985）⇒ 那里再复制一次，
//    使 SourceFile 拥有自己的副本（否则 key 与 SourceFile 共享一个可变对象，`file.ParseOptions()` 就地改动会
//    连带改到字典 key）。回归用例：`ParseCacheDoesNotAliasKeyParseOptions`。
// 3) `xxh3.Uint128` → `System.UInt128`（Xxh3.cs 文件头第 1 条的既定映射；`Ast.SourceFile.Hash` 同）。
// 4) `xxh3.Hash128(buf)` → `Xxh3.Hash128(ReadOnlySpan<byte>)`；`binary.LittleEndian.PutUint64` →
//    `BinaryPrimitives.WriteUInt64LittleEndian`（低 64 位 = Lo 在前，与 Go 的 `Uint128{Lo, Hi}` 一致）。
//    `append(buf, diagnosticLocale.String()...)` → `Encoding.UTF8.GetBytes(locale.String())` 追加（Go 的 string 是
//    UTF-8 字节序列，见 docs §3.1：本仓内部字符串是 UTF-16，但这里按 UTF-8 编码后哈希与 Go 逐字节一致）。
// 5) `parseCacheKeyForFile` 等**包级函数** → 按 M8Project 广播 #2 挂成"它构造/返回的类型"的静态成员：
//    `ParseCacheKey.ParseCacheKeyForFile`、`ParseCacheKey.ParseCacheKeyForDuplicate`、
//    `ContentMappedParseCacheKey.ContentMappedParseCacheKeyForFile/…ForDuplicate`、
//    `ParseCacheKey.NewParseCacheKey`（Go 的 `NewParseCacheKey` 返回的是 key，故挂 key 类型）、
//    `ContentMappedParseCacheKey.NewContentMappedParseCacheKey`（Go 的 `contentMappedParseCacheKey`；**改名**：C# 禁止
//    成员与封闭类型同名（CS0542），故按 `NewParseCacheKey` 的样式加 `New` 前缀）、
//    `ParseCache.NewParseCache`、`ContentMappedParseCache.NewContentMappedParseCache`。
// 6) Go 的 `type ParseCache = RefCountCache[ParseCacheKey, *ast.SourceFile, FileHandle]` 是**类型别名** →
//    C# 没有类型别名，落成派生类 `public sealed class ParseCache : RefCountCache<ParseCacheKey, SourceFile, FileHandle>`
//    （行为完全一致：方法集就是基类那五个）。`ContentMappedParseCache` 在 Go 里是"包装 + 内嵌" → C# 同样是派生类。
// 7) `struct{}`（`ContentMappedParseCache` 的 AcquireArgs 实参类型）→ C# 泛型实参用 `object?`（恒传 null；
//    该缓存只用 `AcquireOrError`，AcquireArgs 从不被读取）。`NewRefCountCache` 的解析函数里 Go 直接 panic
//    （必须走 AcquireOrError）→ `Debug.Fail(...)`。
// 8) `parser.ParseSourceFile`/`binder.BindSourceFile`/`core.EnsureScriptKindFromFileName` 是包级函数 →
//    `using static TypeScript.Parser.Parser;` / `using static TypeScript.Binder.Binder;` / `using static TypeScript.Core.Core;` 后的裸名。
// 9) `locale.Locale` 与命名空间 `TypeScript.Locale` 同名 → `using Loc = TypeScript.Locale.Locale;`（Locale.cs 文件头既定做法）。
namespace TypeScript.Project;

using System;
using System.Buffers.Binary;
using System.Text;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using static TypeScript.Binder.Binder;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Core.Core;
using static TypeScript.Parser.Parser;
using static TypeScript.Xxh3.Xxh3;
using Loc = TypeScript.Locale.Locale;

/// <summary>Go: <c>type ParseCacheKey struct</c>（parsecache.go:16-20）。</summary>
public sealed class ParseCacheKey : IEquatable<ParseCacheKey>
{
    /// <summary>Go 的内嵌字段 <c>ast.SourceFileParseOptions</c>（见文件头第 1 条）。</summary>
    public SourceFileParseOptions SourceFileParseOptions = new();

    /// <summary>Go: <c>ScriptKind core.ScriptKind</c>。</summary>
    public ScriptKind ScriptKind;

    /// <summary>Go: <c>Hash xxh3.Uint128</c>。</summary>
    public UInt128 Hash;

    /// <summary>Go: <c>func NewParseCacheKey(options ast.SourceFileParseOptions, hash xxh3.Uint128, scriptKind core.ScriptKind) ParseCacheKey</c>（parsecache.go:22-33）。</summary>
    public static ParseCacheKey NewParseCacheKey(SourceFileParseOptions options, UInt128 hash, ScriptKind scriptKind)
    {
        if (scriptKind == ScriptKind.Unknown)
        {
            scriptKind = EnsureScriptKindFromFileName(options.FileName);
        }

        return new ParseCacheKey
        {
            // Go 收下的是结构体**副本**（值语义）；C# 的 SourceFileParseOptions 是可变 class，必须显式复制，
            // 否则 key 的相等性/哈希会随原对象被改写而漂移（见文件头第 2/2b 条）。
            SourceFileParseOptions = CloneParseOptions(options),
            Hash = hash,
            ScriptKind = scriptKind,
        };
    }

    /// <summary>Go: <c>func parseCacheKeyForFile(file *ast.SourceFile) ParseCacheKey</c>（parsecache.go:50-52）。</summary>
    public static ParseCacheKey ParseCacheKeyForFile(SourceFile file)
    {
        return NewParseCacheKey(file.ParseOptions(), file.Hash, file.ScriptKind);
    }

    /// <summary>Go: <c>func parseCacheKeyForDuplicate(file *compiler.DuplicateSourceFile) ParseCacheKey</c>（parsecache.go:58-60）。</summary>
    public static ParseCacheKey ParseCacheKeyForDuplicate(DuplicateSourceFile file)
    {
        return NewParseCacheKey(file.ParseOptions, file.Hash, file.ScriptKind);
    }

    /// <summary>Go 的结构体相等（见文件头第 2 条）。</summary>
    public bool Equals(ParseCacheKey? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return SourceFileParseOptionsEqual(SourceFileParseOptions, other.SourceFileParseOptions)
            && ScriptKind == other.ScriptKind
            && Hash == other.Hash;
    }

    public override bool Equals(object? obj) => Equals(obj as ParseCacheKey);

    public override int GetHashCode()
    {
        var options = SourceFileParseOptions;
        return HashCode.Combine(
            options.FileName,
            options.Path,
            options.ExternalModuleIndicatorOptions.JSX,
            options.ExternalModuleIndicatorOptions.Force,
            ScriptKind,
            Hash);
    }

    /// <summary>Go 的 `SourceFileParseOptions` 结构体比较（它只含值字段：FileName/Path/ExternalModuleIndicatorOptions）。</summary>
    internal static bool SourceFileParseOptionsEqual(SourceFileParseOptions left, SourceFileParseOptions right)
    {
        return left.FileName == right.FileName
            && left.Path == right.Path
            && left.ExternalModuleIndicatorOptions.JSX == right.ExternalModuleIndicatorOptions.JSX
            && left.ExternalModuleIndicatorOptions.Force == right.ExternalModuleIndicatorOptions.Force;
    }
}

/// <summary>
/// Go: <c>type ContentMappedParseCacheKey struct</c>（parsecache.go:36-40）。标识一个映射输入的完整输出包；
/// Hash 把原始内容、mapper 变换 identity 与诊断 locale 一起折叠进去。
/// </summary>
public sealed class ContentMappedParseCacheKey : IEquatable<ContentMappedParseCacheKey>
{
    /// <summary>Go 的内嵌字段 <c>ast.SourceFileParseOptions</c>（见文件头第 1 条）。</summary>
    public SourceFileParseOptions SourceFileParseOptions = new();

    /// <summary>Go: <c>Hash xxh3.Uint128</c>。</summary>
    public UInt128 Hash;

    /// <summary>Go: <c>func contentMappedParseCacheKey(options ast.SourceFileParseOptions, rawHash, transformIdentity xxh3.Uint128, diagnosticLocale locale.Locale) ContentMappedParseCacheKey</c>（parsecache.go:42-48）。
    /// 名字偏离：Go 的原名与本类型同名，C# 禁止成员与封闭类型同名（CS0542）⇒ 按 `NewParseCacheKey` 的样式改叫 <c>NewContentMappedParseCacheKey</c>。</summary>
    public static ContentMappedParseCacheKey NewContentMappedParseCacheKey(SourceFileParseOptions options, UInt128 rawHash, UInt128 transformIdentity, Loc diagnosticLocale)
    {
        var localeText = diagnosticLocale.String();
        var buf = new byte[32 + Encoding.UTF8.GetByteCount(localeText)];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, (ulong)rawHash);              // Go: rawHash.Lo
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8), (ulong)(rawHash >> 64));   // Go: rawHash.Hi
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(16), (ulong)transformIdentity);       // Go: transformIdentity.Lo
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(24), (ulong)(transformIdentity >> 64)); // Go: transformIdentity.Hi
        Encoding.UTF8.GetBytes(localeText, buf.AsSpan(32));
        return new ContentMappedParseCacheKey
        {
            SourceFileParseOptions = CloneParseOptions(options),
            Hash = Hash128(buf),
        };
    }

    /// <summary>Go: <c>func contentMappedParseCacheKeyForFile(file *ast.SourceFile) ContentMappedParseCacheKey</c>（parsecache.go:54-56）。</summary>
    public static ContentMappedParseCacheKey ContentMappedParseCacheKeyForFile(SourceFile file)
    {
        return new ContentMappedParseCacheKey
        {
            SourceFileParseOptions = CloneParseOptions(file.ContentMapperParseOptions()),
            Hash = file.Hash,
        };
    }

    /// <summary>Go: <c>func contentMappedParseCacheKeyForDuplicate(file *compiler.DuplicateSourceFile) ContentMappedParseCacheKey</c>（parsecache.go:62-64）。</summary>
    public static ContentMappedParseCacheKey ContentMappedParseCacheKeyForDuplicate(DuplicateSourceFile file)
    {
        return new ContentMappedParseCacheKey
        {
            SourceFileParseOptions = CloneParseOptions(file.ContentMapperParseOptions),
            Hash = file.Hash,
        };
    }

    /// <summary>Go 的结构体相等（见文件头第 2 条）。</summary>
    public bool Equals(ContentMappedParseCacheKey? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return ParseCacheKey.SourceFileParseOptionsEqual(SourceFileParseOptions, other.SourceFileParseOptions)
            && Hash == other.Hash;
    }

    public override bool Equals(object? obj) => Equals(obj as ContentMappedParseCacheKey);

    public override int GetHashCode()
    {
        var options = SourceFileParseOptions;
        return HashCode.Combine(
            options.FileName,
            options.Path,
            options.ExternalModuleIndicatorOptions.JSX,
            options.ExternalModuleIndicatorOptions.Force,
            Hash);
    }
}

/// <summary>
/// Go: <c>type ParseCache = RefCountCache[ParseCacheKey, *ast.SourceFile, FileHandle]</c>（parsecache.go:66）。
/// 见文件头第 6 条：C# 用派生类代替类型别名，方法集与 <see cref="RefCountCache{K,V,H}"/> 一致。
/// </summary>
public sealed class ParseCache : RefCountCache<ParseCacheKey, SourceFile, FileHandle>
{
    private ParseCache(RefCountCacheOptions options)
        : base(options, static (key, fh) =>
        {
            // Go: `parser.ParseSourceFile(key.SourceFileParseOptions, …)` —— Go 的参数是**结构体副本**，而 C# 的
            // `SourceFile` 会把 opts 引用直接存进 `_parseOptions`（Ast/SourceFile.cs:985），所以这里必须显式复制：
            // 否则 key 与它解析出的 SourceFile 共享同一个可变 options 对象（Go 里是两份独立副本），
            // 一旦有人就地改 `file.ParseOptions()` 就会改到字典 key 的哈希/相等性（缓存静默失效或损坏）。
            var file = ParseSourceFile(CloneParseOptions(key.SourceFileParseOptions), fh.Content(), key.ScriptKind);
            file.Hash = fh.Hash();
            BindSourceFile(file);
            return file;
        })
    {
    }

    /// <summary>Go: <c>func NewParseCache(options RefCountCacheOptions) *ParseCache</c>（parsecache.go:68-78）。</summary>
    public static ParseCache NewParseCache(RefCountCacheOptions options)
    {
        return new ParseCache(options);
    }
}

/// <summary>
/// Go: <c>type ContentMappedParseCache struct</c>（parsecache.go:80-84）。一个引用同时拥有规范文件与全部补充文件
/// （作为一整个 bundle）；调用方只对规范文件 ref/deref。
/// </summary>
public sealed class ContentMappedParseCache : RefCountCache<ContentMappedParseCacheKey, SourceFiles, object?>
{
    private ContentMappedParseCache(RefCountCacheOptions options)
        : base(options, static (_, _) =>
        {
            // Go: panic("content-mapped source files must be produced with AcquireOrError")
            Debug.Fail("content-mapped source files must be produced with AcquireOrError");
            return null!;
        })
    {
    }

    /// <summary>Go: <c>func NewContentMappedParseCache(options RefCountCacheOptions) *ContentMappedParseCache</c>（parsecache.go:86-90）。</summary>
    public static ContentMappedParseCache NewContentMappedParseCache(RefCountCacheOptions options)
    {
        return new ContentMappedParseCache(options);
    }
}
