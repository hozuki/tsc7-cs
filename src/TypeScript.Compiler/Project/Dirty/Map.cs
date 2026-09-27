// ported from: internal/project/dirty/map.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **Go 的内嵌**：Go 的 `MapEntry[K,V]` 内嵌未导出的 `mapEntry[K,V]`（字段 key/original/value/dirty/delete
//    与方法 Key/Original/Value/Dirty 因此被提升）。C# 没有内嵌，两条可选路径：
//      (a) 让 `MapEntry`/`SyncMapEntry` 继承同一个基类 —— 但 C# 规定**基类不能比派生类更不可见**（CS0060），
//          而 `mapEntry` 在 Go 里是未导出的，基类只能写成 public，等于凭空给公开 API 加类型；
//      (b) 把字段与访问器直接内联进两个派生类 —— 公开 API 与 Go 逐一对应。
//    本移植取 (b)：`MapEntry` 与 `SyncMapEntry` 各自持有 key/original/value/dirty/delete 与
//    Key/Original/Value/Dirty。**重复的只是 5 个字段 + 4 个 3 行访问器**，换来的是公开 API 不多不少。
//
// 2) Go 的 `map[K]V` → `Dictionary<K,V>`；`nil` map → `null`（见下条）。字典的遍历顺序与 Go 的 map 一样
//    不做保证（`Range` 的顺序在 Go 里本来也是随机的）。
//
// 3) `NewMap(base map[K]V)` 的 base 可以是 nil（首次构建时调用点传的就是 nil，例如
//    projectcollectionbuilder.go 的 `dirty.NewMap(oldProjectCollection.configFileNames)`）。
//    逐分支保留：`_base == null` 时 Get/Range 视为空表、Finalize 返回 `(null, false)`（Go 返回 nil map）。
//
// 4) `Map.Get` 返回 `(*MapEntry[K,V], bool)`：**照 Go 的形状**落成 `(MapEntry<K,V>? Entry, bool Ok)`
//    （与 Collections.Cow.Get / OrderedMap.Get 的既有约定一致，architecture.md §3.2）；`Ok == false` 时
//    Entry 恒为 null。注意 Go 的 Get 每次都在 base 命中时**新建**一个未登记的条目（调用它不会给 map 加脏），
//    这里同样每次新建。
//
// 5) Go 的三处 `panic(...)` → `Debug.Fail(...)`（抛 DebugFailureException，docs/architecture.md §3.2）：
//    "tried to change a deleted entry"、"tried to change a non-existent entry"、"tried to delete a non-existent entry"。
//
// 6) `Map.Range(fn func(*MapEntry[K,V]) bool)` → `Range(Func<MapEntry<K,V>, bool> fn)`；回调返回 false 即停止
//    （Go 的 `break`）。Go 先遍历 dirty 再遍历 base，且跳过 dirty 里已被删除的条目；逐条照搬。
namespace TypeScript.Project.Dirty;

/// <summary>Go 的 <c>MapEntry[K,V]</c>：<see cref="Map{K,V}"/> 的条目视图（见文件头第 1 条）。</summary>
public sealed class MapEntry<K, V> : Value<V> where K : notnull where V : Cloneable<V>
{
    private readonly Map<K, V> _m;
    private readonly K _key;
    private V _original;
    internal V _value;
    internal bool _dirty;
    internal bool _delete;

    internal MapEntry(Map<K, V> m, K key, V? original, V value, bool dirty)
    {
        _m = m;
        _key = key;
        _original = original!;
        _value = value;
        _dirty = dirty;
    }

    public K Key()
    {
        return _key;
    }

    /// <summary>Go: <c>Original()</c>。<c>Map.Add</c> 造的条目没有 original（Go 是零值），此时为 null。</summary>
    public V Original()
    {
        return _original;
    }

    public V Value()
    {
        if (_delete)
        {
            return default!;
        }

        return _value;
    }

    public bool Dirty()
    {
        return _dirty;
    }

    public void Change(Action<V> apply)
    {
        if (_delete)
        {
            Debug.Fail("tried to change a deleted entry");
        }

        if (!_dirty)
        {
            _value = _value.Clone();
            _dirty = true;
            _m._dirty[_key] = this;
        }

        apply(_value);
    }

    /// <summary>Go: <c>Replace(newValue V)</c>：不 Clone，直接换掉（调用方给的就是自己的新值）。</summary>
    public void Replace(V newValue)
    {
        if (_delete)
        {
            Debug.Fail("tried to change a deleted entry");
        }

        if (!_dirty)
        {
            _dirty = true;
            _m._dirty[_key] = this;
        }

        _value = newValue;
    }

    public bool ChangeIf(Func<V, bool> cond, Action<V> apply)
    {
        if (cond(Value()))
        {
            Change(apply);
            return true;
        }

        return false;
    }

    public void Delete()
    {
        if (!_dirty)
        {
            _m._dirty[_key] = this;
        }

        _delete = true;
    }

    public void Locked(Action<Value<V>> fn)
    {
        fn(this);
    }
}

/// <summary>Go 的 <c>Map[K,V]</c>：带脏追踪的 map（对 base 的写时复制）。</summary>
public sealed class Map<K, V> where K : notnull where V : Cloneable<V>
{
    internal Dictionary<K, V>? _base;
    internal Dictionary<K, MapEntry<K, V>> _dirty;

    public Map(Dictionary<K, V>? @base)
    {
        _base = @base;
        _dirty = new Dictionary<K, MapEntry<K, V>>();
    }

    public (MapEntry<K, V>? Entry, bool Ok) Get(K key)
    {
        if (_dirty.TryGetValue(key, out var dirtyEntry))
        {
            if (dirtyEntry._delete)
            {
                return (null, false);
            }

            return (dirtyEntry, true);
        }

        if (_base == null || !_base.TryGetValue(key, out var value))
        {
            return (null, false);
        }

        return (new MapEntry<K, V>(this, key, value, value, dirty: false), true);
    }

    /// <summary>
    /// Go: <c>Add(key K, value V)</c>：不查 base，直接当作脏条目写入（value 必须是**新鲜**的可变值 ——
    /// 新条目本身就处于脏状态，后续 Change 不会先 Clone）。要改可能已存在于 base 的条目，用 Change。
    /// </summary>
    public void Add(K key, V value)
    {
        _dirty[key] = new MapEntry<K, V>(this, key, original: default, value, dirty: true);
    }

    public void Change(K key, Action<V> apply)
    {
        var (entry, ok) = Get(key);
        if (ok)
        {
            entry!.Change(apply);
            return;
        }

        Debug.Fail("tried to change a non-existent entry");
    }

    public bool TryDelete(K key)
    {
        var (entry, ok) = Get(key);
        if (ok)
        {
            entry!.Delete();
            return true;
        }

        return false;
    }

    public void Delete(K key)
    {
        if (!TryDelete(key))
        {
            Debug.Fail("tried to delete a non-existent entry");
        }
    }

    public void Range(Func<MapEntry<K, V>, bool> fn)
    {
        var seenInDirty = new HashSet<K>();
        foreach (var entry in _dirty.Values)
        {
            seenInDirty.Add(entry.Key());
            if (!entry._delete && !fn(entry))
            {
                break;
            }
        }

        if (_base == null)
        {
            return;
        }

        foreach (var pair in _base)
        {
            if (seenInDirty.Contains(pair.Key))
            {
                continue; // already processed in dirty entries
            }

            if (!fn(new MapEntry<K, V>(this, pair.Key, pair.Value, pair.Value, dirty: false)))
            {
                break;
            }
        }
    }

    public void Clear()
    {
        _dirty = new Dictionary<K, MapEntry<K, V>>();
        _base = new Dictionary<K, V>();
    }

    /// <summary>Go: <c>Finalize()(map[K]V, bool)</c>。无改动时返回 base 本身（可能是 null）与 false。</summary>
    public (Dictionary<K, V>? Result, bool Changed) Finalize()
    {
        if (_dirty.Count == 0)
        {
            return (_base, false); // no changes, return base map
        }

        Dictionary<K, V> result;
        if (_base == null)
        {
            result = new Dictionary<K, V>(_dirty.Count);
        }
        else
        {
            result = new Dictionary<K, V>(_base);
        }

        foreach (var pair in _dirty)
        {
            if (pair.Value._delete)
            {
                result.Remove(pair.Key);
            }
            else
            {
                result[pair.Key] = pair.Value._value;
            }
        }

        return (result, true);
    }
}

public static partial class Dirty
{
    /// <summary>Go: <c>NewMap[K,V](base map[K]V) *Map[K,V]</c>。</summary>
    public static Map<K, V> NewMap<K, V>(Dictionary<K, V>? @base) where K : notnull where V : Cloneable<V>
    {
        return new Map<K, V>(@base);
    }
}
