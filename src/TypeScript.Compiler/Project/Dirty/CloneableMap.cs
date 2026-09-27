// ported from: internal/project/dirty/cloneablemap.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `type CloneableMap[K comparable, V any] map[K]V` 是**命名 map 类型**；C# 没有这种构造，
//    用 `CloneableMap<K,V> : Dictionary<K,V>` 表达：`make(dirty.CloneableMap[K,V])` → `new CloneableMap<K,V>()`、
//    `len(m)` → `m.Count`、下标读写/`range` → 下标读写/`foreach` 都与 Go 逐句对应
//    （调用点见 snapshotfs.go:96/144/224-243）。它承载 `dirty.SyncMap`/`dirty.Map` 的 V，所以必须
//    实现 `Cloneable<CloneableMap<K,V>>`（Go 侧靠 `Clone() CloneableMap[K,V]` 满足 `V Cloneable[V]`）。
//
// 2) `maps.Clone(m)` → `new CloneableMap<K,V>(m)`（`Dictionary<K,V>(IDictionary)` 拷贝构造，浅拷贝，与
//    `maps.Clone` 一致：只复制表本身，不复制值）。
//
// 3) 额外的两个构造（capacity / IDictionary）是 C# 侧的落地细节：Go 的 `make(map[K]V, hint)` 与
//    `maps.Clone` 都要有对应物，故一并提供；公开 API 上它们不增加"新语义"。
namespace TypeScript.Project.Dirty;

/// <summary>Go 的 <c>CloneableMap[K,V]</c>：可克隆的普通 map（脏追踪里当作值用）。</summary>
public sealed class CloneableMap<K, V> : Dictionary<K, V>, Cloneable<CloneableMap<K, V>> where K : notnull
{
    public CloneableMap()
    {
    }

    public CloneableMap(int capacity)
        : base(capacity)
    {
    }

    public CloneableMap(IDictionary<K, V> other)
        : base(other)
    {
    }

    public CloneableMap<K, V> Clone()
    {
        return new CloneableMap<K, V>(this);
    }
}
