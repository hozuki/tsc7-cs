// ported from: internal/execute/incremental/referencemap.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `sync.Once`（`referenceBy sync.Once`）→ 一把锁 + 惰性字段（Once 的 happens-before 由锁复刻）。
// 2) `iter.Seq[tspath.Path]` → `IEnumerable<Path>`；Go 的"没有该项就返回空序列"→ 返回空数组。
// 3) Go 未导出类型 `referenceMap` → `internal sealed class ReferenceMap`（跨类访问：snapshot/各 handler）。
// 4) Go 的 `maps.Keys(set.Keys())` → 直接返回 `Set.Keys()`（C# 的 `Set.Keys()` 就是内部 HashSet）。
namespace TypeScript.Execute.Incremental;

using TypeScript.Collections;
using Path = TypeScript.TSPath.Path;

internal sealed class ReferenceMap
{
    private readonly SyncMap<Path, Set<Path>> _references = new();
    private Dictionary<Path, Set<Path>>? _referencedBy;
    private readonly object _referencedByLock = new();

    internal void StoreReferences(Path path, Set<Path> refs) => _references.Store(path, refs);

    internal (Set<Path>? References, bool Ok) GetReferences(Path path) => _references.Load(path);

    internal Path[] GetPathsWithReferences() => [.. _references.Keys()];

    internal IEnumerable<Path> GetReferencedBy(Path path)
    {
        var referencedBy = GetReferencedByMap();
        if (referencedBy.TryGetValue(path, out var refs))
        {
            return refs.Keys();
        }
        return [];
    }

    private Dictionary<Path, Set<Path>> GetReferencedByMap()
    {
        lock (_referencedByLock)
        {
            if (_referencedBy is not null)
            {
                return _referencedBy;
            }
            var referencedBy = new Dictionary<Path, Set<Path>>();
            _references.Range((key, value) =>
            {
                foreach (var @ref in value!.Keys())
                {
                    if (!referencedBy.TryGetValue(@ref, out var set))
                    {
                        set = new Set<Path>();
                        referencedBy[@ref] = set;
                    }
                    set.Add(key);
                }
                return true;
            });
            _referencedBy = referencedBy;
            return referencedBy;
        }
    }
}
