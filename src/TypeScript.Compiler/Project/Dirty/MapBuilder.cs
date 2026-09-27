// ported from: internal/project/dirty/mapbuilder.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `NewMapBuilder[K,VBase,VBuilder](base, toBuilder, build)` → 包级函数
//    `Dirty.NewMapBuilder`（architecture.md §2）。三个类型参数照抄；`func(VBase) VBuilder` → `Func<VBase, VBuilder>`。
//    注意 `MapBuilder` **不在** `Cloneable` 约束下（Go 也只有 comparable/any）：Builder 持有的是"未提交的
//    可变构建态"，不参与 Clone 语义。
//
// 2) Go 的 `map[K]VBase`/`map[K]VBuilder` → `Dictionary<K,VBase?>`/`Dictionary<K,VBuilder?>`，base 可为 nil
//    （`NewMapBuilder(registry.specifierCache, …)` 首次就是 nil）；`deleted` 在 Go 里是**惰性**创建的
//    （`Delete` 里 `if mb.deleted == nil`），C# 侧构造时就 `new` 出来（语义等价：Set/Delete/Has 都按
//    "集合为空"处理，无需区分 nil 与空）。
//
// 3) `Build()` 在无改动时返回 base 本身（Go 原样；此时可能是 null），与 `Map.Finalize` 的别名语义一致。
//
// 4) `Delete` 会清掉同名脏值（Go 原样）；`Set` 会取消同名删除（Go 原样）。
namespace TypeScript.Project.Dirty;

/// <summary>
/// Go 的 <c>MapBuilder[K,VBase,VBuilder]</c>：在 base map 之上收集"待提交"的构建态，
/// <c>Build()</c> 时用 <c>build</c> 把每个脏值物化成 VBase。
/// </summary>
public sealed class MapBuilder<K, VBase, VBuilder> where K : notnull
{
    private readonly Dictionary<K, VBase>? _base;
    private Dictionary<K, VBuilder> _dirty;
    private Dictionary<K, byte> _deleted;

    private readonly Func<VBase, VBuilder> _toBuilder;
    private readonly Func<VBuilder, VBase> _build;

    public MapBuilder(
        Dictionary<K, VBase>? @base,
        Func<VBase, VBuilder> toBuilder,
        Func<VBuilder, VBase> build)
    {
        _base = @base;
        _dirty = new Dictionary<K, VBuilder>();
        _deleted = new Dictionary<K, byte>();
        _toBuilder = toBuilder;
        _build = build;
    }

    public void Set(K key, VBuilder value)
    {
        _dirty[key] = value;
        _deleted.Remove(key);
    }

    public void Delete(K key)
    {
        _deleted[key] = 0;
        _dirty.Remove(key);
    }

    public void Clear()
    {
        _dirty = new Dictionary<K, VBuilder>();
        _deleted = new Dictionary<K, byte>(_base?.Count ?? 0);
        if (_base != null)
        {
            foreach (var key in _base.Keys)
            {
                _deleted[key] = 0;
            }
        }
    }

    public bool Has(K key)
    {
        if (_deleted.ContainsKey(key))
        {
            return false;
        }

        if (_dirty.ContainsKey(key))
        {
            return true;
        }

        return _base != null && _base.ContainsKey(key);
    }

    /// <summary>Go: <c>Build() map[K]VBase</c>。无脏值时返回 base 本身（可能是 null）。</summary>
    public Dictionary<K, VBase>? Build()
    {
        if (_dirty.Count == 0 && _deleted.Count == 0)
        {
            return _base;
        }

        var result = _base == null ? new Dictionary<K, VBase>() : new Dictionary<K, VBase>(_base);
        foreach (var key in _deleted.Keys)
        {
            result.Remove(key);
        }

        foreach (var pair in _dirty)
        {
            result[pair.Key] = _build(pair.Value);
        }

        return result;
    }
}

public static partial class Dirty
{
    /// <summary>Go: <c>NewMapBuilder[K,VBase,VBuilder](base, toBuilder, build) *MapBuilder[K,VBase,VBuilder]</c>。</summary>
    public static MapBuilder<K, VBase, VBuilder> NewMapBuilder<K, VBase, VBuilder>(
        Dictionary<K, VBase>? @base,
        Func<VBase, VBuilder> toBuilder,
        Func<VBuilder, VBase> build)
        where K : notnull
    {
        return new MapBuilder<K, VBase, VBuilder>(@base, toBuilder, build);
    }
}
