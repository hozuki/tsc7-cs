// ported from: internal/project/extendedconfigcache.go:1-51
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `type ExtendedConfigCache = OwnerCache[tspath.Path, *ExtendedConfigCacheEntry, ExtendedConfigParseArgs]`
//    是**类型别名** → C# 没有类型别名，落成派生类 `internal sealed class ExtendedConfigCache :
//    OwnerCache<Path, ExtendedConfigCacheEntry, ExtendedConfigParseArgs>`（方法集就是基类那六个）。
//    注意：`internal/execute/tsc/extendedconfigcache.go` 的同名类型已由别的切片移植成
//    `TypeScript.Execute.Tsc.ExtendedConfigCache`，两者靠命名空间区分；本类型**不**实现
//    `TsOptions.ExtendedConfigCache` 接口（接口由 configFileRegistryBuilder 实现，Go 侧同样如此）。
// 2) Go 的嵌入 `*tsoptions.ExtendedConfigCacheEntry` → C# 的 `TsOptions.ExtendedConfigCacheEntry` 是 sealed，
//    改为**组合字段** `_extendedConfigCacheEntry`（Go 的嵌入字段名 `ExtendedConfigCacheEntry` 与本类型**同名**，
//    C# 禁止成员与封闭类型同名（CS0542）⇒ 加下划线前缀；调用点 configfileregistrybuilder.go:811 的
//    `.ExtendedConfigCacheEntry` 在 C# 里写 `._extendedConfigCacheEntry`）。
//    Go 通过提升还暴露了被嵌入值的 `ExtendedFileNames()/Errors/ExtendedResult`，C# 无法提升 ⇒ 本类补一个
//    `ExtendedFileNames()` 转发（hash() 要用），其余成员经 `_extendedConfigCacheEntry` 字段显式访问。
// 3) `tsoptions.ParseExtendedConfig`/`ParseConfigHost`/`ExtendedConfigCache` 是静态类 `TsOptions` 的**嵌套**成员 →
//    用别名 `TSOpts = TypeScript.TsOptions.TsOptions` 限定（先例 Execute/Tsc/ExtendedConfigCache.cs 第 3 条；
//    直接 `using static` 会让 `ExtendedConfigCache` 这个名字在本文件里自指）。
// 4) `xxh3.Uint128` → `System.UInt128`（Xxh3.cs 文件头第 1 条）。
// 5) **流式哈希未移植**：Go 的 `hasher := xxh3.New(); hasher.WriteString(...)`（多段）→ C# 侧先把各段拼成一个
//    字符串再 `Xxh3.HashString128`（Xxh3.cs 文件头第 2 条已说明 XXH3 的流式与一次性喂完整个输入结果逐位相同；
//    HashString128 按 UTF-8 编码，与 Go 的 string 字节序列一致）。nil 的 `args.FS.GetFile(fileName)`（文件不存在）
//    对应 Go 的 `return xxh3.Uint128{}` → `return default`。
// 6) `NewExtendedConfigCache()` 是包级构造函数 → 按 M8Project 广播 #2 挂成
//    `ExtendedConfigCache.NewExtendedConfigCache()`；构造函数本身私有（Go 无导出构造函数）。
// 7) `entry.Hash == xxh3.Uint128{}` → `entry.Hash == default`（UInt128 的零值比较）。
namespace TypeScript.Project;

using System;
using System.Text;
using static TypeScript.Xxh3.Xxh3;
using Path = TypeScript.TSPath.Path;
using TSOpts = TypeScript.TsOptions.TsOptions;

/// <summary>Go: <c>type ExtendedConfigParseArgs struct</c>（extendedconfigcache.go:9-16）。</summary>
public sealed class ExtendedConfigParseArgs
{
    public string FileName = "";
    public string Content = "";
    public FileSource? FS;
    public Path[]? ResolutionStack;
    public TSOpts.ParseConfigHost? Host;
    public TSOpts.ExtendedConfigCache? Cache;
}

/// <summary>Go: <c>type ExtendedConfigCacheEntry struct { *tsoptions.ExtendedConfigCacheEntry; Hash xxh3.Uint128 }</c>（extendedconfigcache.go:18-21）。</summary>
public sealed class ExtendedConfigCacheEntry
{
    /// <summary>Go 的嵌入字段 <c>*tsoptions.ExtendedConfigCacheEntry</c>（C# 用组合替继承，见文件头第 2 条）。
    /// 名字偏离：Go 的嵌入字段名与本类型同名，C# 禁止成员与封闭类型同名（CS0542）⇒ 改叫 <c>_extendedConfigCacheEntry</c>；
    /// 调用点（configfileregistrybuilder.go:811 的 `.ExtendedConfigCacheEntry`）在 C# 里写 `.<see cref="_extendedConfigCacheEntry"/>`。</summary>
    public TSOpts.ExtendedConfigCacheEntry? _extendedConfigCacheEntry;

    /// <summary>Go: <c>Hash xxh3.Uint128</c>。</summary>
    public UInt128 Hash;

    /// <summary>Go 的提升方法 <c>ExtendedFileNames()</c>（被嵌入值的同名方法）。</summary>
    public string[] ExtendedFileNames() => _extendedConfigCacheEntry?.ExtendedFileNames() ?? [];
}

/// <summary>
/// Go: <c>type ExtendedConfigCache = OwnerCache[tspath.Path, *ExtendedConfigCacheEntry, ExtendedConfigParseArgs]</c>
/// （extendedconfigcache.go:23）。见文件头第 1 条。
/// </summary>
public sealed class ExtendedConfigCache : OwnerCache<Path, ExtendedConfigCacheEntry, ExtendedConfigParseArgs>
{
    private ExtendedConfigCache()
        : base(Parse, IsExpired)
    {
    }

    /// <summary>Go: <c>func NewExtendedConfigCache() *ExtendedConfigCache</c>（extendedconfigcache.go:25-38）。</summary>
    public static ExtendedConfigCache NewExtendedConfigCache()
    {
        return new ExtendedConfigCache();
    }

    /// <summary>Go: <c>func(path tspath.Path, args ExtendedConfigParseArgs) *ExtendedConfigCacheEntry { … }</c>（NewOwnerCache 的第一个实参）。</summary>
    private static ExtendedConfigCacheEntry Parse(Path path, ExtendedConfigParseArgs args)
    {
        var result = new ExtendedConfigCacheEntry
        {
            _extendedConfigCacheEntry = TSOpts.ParseExtendedConfig(args.FileName, path, args.ResolutionStack, args.Host!, args.Cache),
        };
        result.Hash = Hash(result._extendedConfigCacheEntry, args);
        return result;
    }

    /// <summary>Go: <c>func(path tspath.Path, entry *ExtendedConfigCacheEntry, args ExtendedConfigParseArgs) bool { … }</c>（NewOwnerCache 的第二个实参）。</summary>
    private static bool IsExpired(Path path, ExtendedConfigCacheEntry entry, ExtendedConfigParseArgs args)
    {
        return entry.Hash == default || entry.Hash != Hash(entry._extendedConfigCacheEntry, args);
    }

    /// <summary>Go: <c>func hash(entry *tsoptions.ExtendedConfigCacheEntry, args ExtendedConfigParseArgs) xxh3.Uint128</c>（extendedconfigcache.go:40-50）。</summary>
    private static UInt128 Hash(TSOpts.ExtendedConfigCacheEntry? entry, ExtendedConfigParseArgs args)
    {
        // 见文件头第 5 条：Go 的流式 WriteString 在这里拼成一次哈希（结果逐位相同）。
        var writer = new StringBuilder(args.Content);
        foreach (var fileName in entry?.ExtendedFileNames() ?? [])
        {
            var fh = args.FS?.GetFile(fileName);
            if (fh is null)
            {
                return default;
            }

            writer.Append(fh.Content());
        }

        return HashString128(writer.ToString());
    }
}
