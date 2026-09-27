// ported from: internal/module/cache.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// - Go 的 `type ModeAwareCache[T any] map[ModeAwareCacheKey]T` 是 map 类型 → C# 用 `Dictionary` 派生类
//   （Go 的 `make(module.ModeAwareCache[T])`、`cache[k]`、`len(cache)`、`range` 在 C# 里分别是
//   `new ModeAwareCache<T>()`、`cache[k]`、`cache.Count`、`foreach`，调用点形状最接近）。
// - Go 的 `collections.SyncMap` → `TypeScript.Collections.SyncMap`；`Load` 返回 `(V?, bool)`。
// - `parsedPatternsCache` 的键是 `*OrderedMap`（Go 的指针 = 引用同一性）；C# 的 `OrderedMap` 是 class 且
//   没有覆写 `Equals`/`GetHashCode`，`SyncMap` 因此同样是引用同一性键（与 Go 一致）。
// - Go 的 `caches` 结构体里其余字段是零值（nil map）；C# 的对应类型是 class，必须构造
//   （`ModuleResolutionCache`/`TypeRefDirectiveResolutionCache`/`ParsedPatternsCache` 在声明处就建好）。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.PackageJson;

namespace TypeScript.Module;

/// <summary>Go 的 <c>ModeAwareCache[T]</c>（map[ModeAwareCacheKey]T）。</summary>
public sealed class ModeAwareCache<T> : Dictionary<ModeAwareCacheKey, T>;

/// <summary>Go 的 <c>moduleResolutionCacheKey</c>。</summary>
internal struct ModuleResolutionCacheKey(string containingDirectory, string moduleName, ModuleKind resolutionMode, string redirectConfigName)
{
    public string ContainingDirectory = containingDirectory;
    public string ModuleName = moduleName;
    public ModuleKind ResolutionMode = resolutionMode;
    public string RedirectConfigName = redirectConfigName;
}

/// <summary>Go 的 <c>moduleResolutionCache</c>。</summary>
internal sealed class ModuleResolutionCache
{
    private readonly SyncMap<ModuleResolutionCacheKey, ResolvedModule> _cache = new();

    public (ResolvedModule? Value, bool Ok) Get(ModuleResolutionCacheKey key)
    {
        return _cache.Load(key);
    }

    public void Set(ModuleResolutionCacheKey key, ResolvedModule value)
    {
        _cache.LoadOrStore(key, value);
    }
}

/// <summary>Go 的 <c>typeRefDirectiveResolutionCacheKey</c>。</summary>
internal struct TypeRefDirectiveResolutionCacheKey(
    string containingDirectory,
    string typeReferenceName,
    ModuleKind resolutionMode,
    string redirectConfigName,
    bool fromInferredTypesContainingFile)
{
    public string ContainingDirectory = containingDirectory;
    public string TypeReferenceName = typeReferenceName;
    public ModuleKind ResolutionMode = resolutionMode;
    public string RedirectConfigName = redirectConfigName;
    public bool FromInferredTypesContainingFile = fromInferredTypesContainingFile;
}

/// <summary>Go 的 <c>typeRefDirectiveResolutionCache</c>。</summary>
internal sealed class TypeRefDirectiveResolutionCache
{
    private readonly SyncMap<TypeRefDirectiveResolutionCacheKey, ResolvedTypeReferenceDirective> _cache = new();

    public (ResolvedTypeReferenceDirective? Value, bool Ok) Get(TypeRefDirectiveResolutionCacheKey key)
    {
        return _cache.Load(key);
    }

    public void Set(TypeRefDirectiveResolutionCacheKey key, ResolvedTypeReferenceDirective value)
    {
        _cache.Store(key, value);
    }
}

/// <summary>Go 的 <c>parsedPatternsCache</c>。</summary>
internal sealed class ParsedPatternsCache
{
    private readonly SyncMap<OrderedMap<string, string[]>, ParsedPatterns> _cache = new();

    public ParsedPatterns Get(OrderedMap<string, string[]> pathMappings)
    {
        var (patterns, ok) = _cache.Load(pathMappings);
        if (!ok)
        {
            (patterns, _) = _cache.LoadOrStore(pathMappings, Module.TryParsePatterns(pathMappings));
        }

        // 缓存里只会存 TryParsePatterns 的返回值（恒非 null），所以这里的兜底只是为了满足可空性检查。
        return patterns ?? Module.TryParsePatterns(pathMappings);
    }
}

/// <summary>Go 的 <c>caches</c>（未导出；被 <see cref="DefaultResolver"/> 内嵌）。</summary>
internal sealed class Caches(InfoCache packageJsonInfoCache)
{
    internal readonly InfoCache _packageJsonInfoCache = packageJsonInfoCache;

    internal readonly ModuleResolutionCache _moduleResolutionCache = new();

    internal readonly TypeRefDirectiveResolutionCache _typeRefDirectiveResolutionCache = new();

    // Cached representations for `CompilerOptions.paths`, keyed by the path mappings themselves.
    // This does not handle other path patterns such as `typesVersions`.
    internal readonly ParsedPatternsCache _parsedPatternsForPaths = new();
}

public static partial class Module
{
    /// <summary>Go 的 <c>packagejson.NewInfoCache(...)</c>（跨 package 调用，这里只是让调用点保持 Go 的形状）。</summary>
    internal static InfoCache NewInfoCache(string currentDirectory, bool useCaseSensitiveFileNames)
    {
        return TypeScript.PackageJson.PackageJson.NewInfoCache(currentDirectory, useCaseSensitiveFileNames);
    }

    internal static Caches NewCaches(string currentDirectory, bool useCaseSensitiveFileNames, CompilerOptions options)
    {
        return new Caches(NewInfoCache(currentDirectory, useCaseSensitiveFileNames));
    }

    internal static string GetRedirectConfigName(ResolvedProjectReference? redirect)
    {
        if (redirect is null)
        {
            return "";
        }

        return redirect.ConfigName();
    }
}
