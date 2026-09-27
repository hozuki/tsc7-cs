// ported from: internal/execute/tsc/extendedconfigcache.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的嵌入 `*tsoptions.ExtendedConfigCacheEntry` → C# 的 `TsOptions.ExtendedConfigCacheEntry` 是 sealed
//    （不能继承），故改为组合字段 `_entry`；包装类型 `extendedConfigCacheEntry` 是未导出类型，按 §2 命名成
//    internal `ExtendedConfigCacheSlot`（Go 名与本包已移植的 `TsOptions.ExtendedConfigCacheEntry` 撞名）。
// 2) `sync.Mutex` + `defer entry.mu.Unlock()` → `Monitor.Enter/Exit` + `try/finally`：
//    `loadOrStoreNewLockedEntry` 的契约（返回**已加锁**的 entry，且新 entry 先锁再存入 map）与 Go 逐字一致，
//    用 `lock` 语句反而表达不了"辅助方法返回已持锁对象"。
// 3) `tsoptions.ParseExtendedConfig`/`tsoptions.ExtendedConfigCache`/`tsoptions.ExtendedConfigCacheEntry`/
//    `tsoptions.ParseConfigHost` 在 C# 里是静态类 `TsOptions` 的**嵌套**成员（`using static` 会把嵌套类型也
//    导入，但那样本类的名字 `ExtendedConfigCache` 会与接口同名产生自引用），故用别名 `TSOpts` 限定。
// 4) Go 的 `var _ tsoptions.ExtendedConfigCache = (*ExtendedConfigCache)(nil)`（编译期接口断言）→ C# 的
//    基类型列表 `: TSOpts.ExtendedConfigCache` 本身就是这个断言，不需要额外声明。
using global::System.Threading;
using TypeScript.Collections;
using TSOpts = TypeScript.TsOptions.TsOptions;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Execute.Tsc;

/// <summary>
/// Go: <c>type ExtendedConfigCache struct</c>（extendedconfigcache.go:15-17）。<br/>
/// tsoptions.ExtendedConfigCache 的最小实现：并发安全，但缓存条目永久保留 —— 不适用于配置会在多次编译之间
/// 变化的长时间运行进程（Go 的注释原话）。
/// </summary>
public sealed class ExtendedConfigCache : TSOpts.ExtendedConfigCache
{
    private readonly SyncMap<Path, ExtendedConfigCacheSlot> _m = new();

    /// <summary>Go: <c>func (e *ExtendedConfigCache) GetExtendedConfig(fileName string, path tspath.Path, resolutionStack []tspath.Path, host tsoptions.ParseConfigHost) *tsoptions.ExtendedConfigCacheEntry</c>（extendedconfigcache.go:26-33）。</summary>
    public TSOpts.ExtendedConfigCacheEntry? GetExtendedConfig(string fileName, Path path, Path[]? resolutionStack, TSOpts.ParseConfigHost host)
    {
        var (entry, loaded) = LoadOrStoreNewLockedEntry(path);
        try
        {
            if (!loaded)
            {
                entry._entry = TSOpts.ParseExtendedConfig(fileName, path, resolutionStack, host, this);
            }
            return entry._entry;
        }
        finally
        {
            Monitor.Exit(entry._mu);
        }
    }

    /// <summary>Go: <c>func (c *ExtendedConfigCache) loadOrStoreNewLockedEntry(path tspath.Path) (*extendedConfigCacheEntry, bool)</c>（extendedconfigcache.go:36-45）：加载已有条目或新建一个；返回条目的互斥量处于**已加锁**状态。</summary>
    private (ExtendedConfigCacheSlot Entry, bool Loaded) LoadOrStoreNewLockedEntry(Path path)
    {
        var entry = new ExtendedConfigCacheSlot();
        Monitor.Enter(entry._mu);
        var (existing, loaded) = _m.LoadOrStore(path, entry);
        if (loaded)
        {
            Monitor.Enter(existing!._mu);
            return (existing!, true);
        }
        return (entry, false);
    }
}

/// <summary>Go: <c>type extendedConfigCacheEntry struct { *tsoptions.ExtendedConfigCacheEntry; mu sync.Mutex }</c>（extendedconfigcache.go:19-22）。</summary>
internal sealed class ExtendedConfigCacheSlot
{
    /// <summary>Go 的嵌入字段 <c>*tsoptions.ExtendedConfigCacheEntry</c>（C# 里 sealed 类型用组合替代继承）。</summary>
    internal TSOpts.ExtendedConfigCacheEntry? _entry;

    /// <summary>Go: <c>mu sync.Mutex</c>。</summary>
    internal readonly object _mu = new();
}
