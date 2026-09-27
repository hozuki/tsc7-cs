// ported from: internal/project/dirty/syncmap.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **Go 的内嵌**：`SyncMapEntry` 内嵌未导出的 `mapEntry`。C# 无内嵌，且基类可见性不能低于派生类
//    （CS0060），故字段 key/original/value/dirty/delete 与访问器 Key/Original/Value/Dirty 直接内联在
//    `SyncMapEntry` 里（与 Map.cs 的 `MapEntry` 同一处理，见那里文件头第 1 条）。
//
// 2) `sync.Mutex` → `System.Threading.Lock`（本仓统一约定，见 Checker/Checker.Struct.cs 与
//    Core/Compiler.cs 的注释）；`e.mu.Lock(); defer e.mu.Unlock()` → `lock (e._mu) { … }`。
//    **语义差异（唯一一处）**：C# 的 `lock`（Monitor）是**可重入**的，Go 的 `sync.Mutex` 不可重入。
//    Go 侧因为每个条目只会被"自己是脏表里的那一个"线程进入临界区，实际不会自我重入；C# 侧即使重入也只是
//    更宽松（不会死锁），可观察行为一致。
//
// 3) `collections.SyncMap[K, *SyncMapEntry[K,V]]` → `TypeScript.Collections.SyncMap<K, SyncMapEntry<K,V>>`
//    （已移植，加载/存储是弱一致语义，与 Go 的 sync.Map 一致）。注意它的 `Load`/`LoadOrStore` 返回
//    `(V? Value, bool Ok/Loaded)`：本文件存进去的都是非 null 入口，故取出时用 `!`。
//
// 4) `SyncMap.Load`/`LoadOrStore` 返回的 ok/loaded 照 Go 的形状落成元组第二个分量，条目可能为 null。
//
// 5) Go 的 `FinalizationHooks[K,V]` 三个回调字段 → C# 的 `Action<K,V>?` / `Action<K,V,V>?` / `Action<K,V>?`
//    （导出字段保持 public + PascalCase 原名）。
//
// 6) Go 的 `lockedEntry[K,V]`（未导出）→ `LockedEntry<K,V>`（未导出类型按 C# 习惯命名，不必与 Go 同名，
//    architecture.md §3.2）。
//
// 7) `SyncMap.finalize`（未导出）→ `FinalizeCore(hooks)`（C# 方法名一律 PascalCase，除非公开 API 另有要求）。
//    Go 里 `ensureCloned` 闭包捕获 result/changed 两个变量，C# 用**局部函数**原样保留（局部函数可以改写被
//    捕获的局部变量）。
namespace TypeScript.Project.Dirty;

/// <summary>
/// Go 的 <c>lockedEntry[K,V]</c>：<see cref="SyncMapEntry{K,V}.Locked"/> 交给回调的视图，
/// 所有操作都会转发回持有锁的那个条目（`e.e.changeLocked` 等 —— 不再重复加锁）。
/// </summary>
internal sealed class LockedEntry<K, V> : Value<V> where K : notnull where V : Cloneable<V>
{
    private readonly SyncMapEntry<K, V> _e;

    internal LockedEntry(SyncMapEntry<K, V> e)
    {
        _e = e;
    }

    public V Value()
    {
        return _e.ValueLocked();
    }

    public V Original()
    {
        return _e.Original();
    }

    public bool Dirty()
    {
        return _e._dirty;
    }

    public void Change(Action<V> apply)
    {
        _e.ChangeLocked(apply);
    }

    public bool ChangeIf(Func<V, bool> cond, Action<V> apply)
    {
        if (cond(_e.ValueLocked()))
        {
            _e.ChangeLocked(apply);
            return true;
        }

        return false;
    }

    public void Delete()
    {
        _e.DeleteLocked();
    }

    public void Locked(Action<Value<V>> fn)
    {
        fn(this);
    }
}

/// <summary>Go 的 <c>SyncMapEntry[K,V]</c>：<see cref="SyncMap{K,V}"/> 的并发安全条目。</summary>
public sealed class SyncMapEntry<K, V> : Value<V> where K : notnull where V : Cloneable<V>
{
    private readonly SyncMap<K, V> _m;
    internal readonly Lock _mu = new();
    internal readonly K _key;
    internal V? _original;
    internal V _value;
    internal bool _dirty;
    internal bool _delete;

    /// <summary>
    /// Go: <c>proxyFor</c>。当一个条目在"抢占脏条目"的竞争中失败时设置：两个 goroutine 各自持有
    /// 一个可能改写同一底层值的条目，所有改写都被路由到真正进入脏表的那个条目上。
    /// （Go 的测试是同 package 直接读这个未导出字段；C# 侧靠 Properties/AssemblyInfo.cs 的
    /// InternalsVisibleTo 保留同样的可见性。）
    /// </summary>
    internal SyncMapEntry<K, V>? _proxyFor;

    internal SyncMapEntry(SyncMap<K, V> m, K key, V? original, V value, bool dirty, bool delete = false)
    {
        _m = m;
        _key = key;
        _original = original;
        _value = value;
        _dirty = dirty;
        _delete = delete;
    }

    public V Value()
    {
        lock (_mu)
        {
            if (_proxyFor != null)
            {
                return _proxyFor.Value();
            }

            return ValueLocked();
        }
    }

    /// <summary>
    /// Go: 由内嵌的 <c>mapEntry.Key()</c> 提升而来（与 <see cref="MapEntry{K,V}.Key"/> 对应）。
    /// </summary>
    public K Key()
    {
        return _key;
    }

    internal V ValueLocked()
    {
        if (_delete)
        {
            return default!;
        }

        return _value;
    }

    /// <summary>
    /// Go: 由内嵌的 <c>mapEntry.Original()</c> 提升而来 —— **不加锁**（与 Go 一致：直接读字段）。
    /// </summary>
    public V Original()
    {
        return _original!;
    }

    public bool Dirty()
    {
        lock (_mu)
        {
            if (_proxyFor != null)
            {
                return _proxyFor.Dirty();
            }

            return _dirty;
        }
    }

    public void Locked(Action<Value<V>> fn)
    {
        lock (_mu)
        {
            if (_proxyFor != null)
            {
                _proxyFor.Locked(fn);
                return;
            }

            fn(new LockedEntry<K, V>(this));
        }
    }

    public void Change(Action<V> apply)
    {
        lock (_mu)
        {
            if (_proxyFor != null)
            {
                _proxyFor.Change(apply);
                return;
            }

            ChangeLocked(apply);
        }
    }

    internal void ChangeLocked(Action<V> apply)
    {
        if (_dirty)
        {
            apply(_value);
            return;
        }

        var (entryMaybe, loaded) = _m._dirty.LoadOrStore(_key, this);
        var entry = entryMaybe!;
        if (loaded)
        {
            entry._mu.Enter();
        }

        try
        {
            if (!entry._dirty)
            {
                entry._value = entry._value.Clone();
                entry._dirty = true;
            }

            if (loaded)
            {
                _proxyFor = entry;
                _value = entry._value;
                _dirty = true;
                _delete = entry._delete;
            }

            apply(entry._value);
        }
        finally
        {
            if (loaded)
            {
                entry._mu.Exit();
            }
        }
    }

    public bool ChangeIf(Func<V, bool> cond, Action<V> apply)
    {
        lock (_mu)
        {
            if (_proxyFor != null)
            {
                return _proxyFor.ChangeIf(cond, apply);
            }

            if (cond(_value))
            {
                ChangeLocked(apply);
                return true;
            }

            return false;
        }
    }

    public void Delete()
    {
        lock (_mu)
        {
            if (_proxyFor != null)
            {
                _proxyFor.Delete();
                return;
            }

            if (_dirty)
            {
                _delete = true;
                return;
            }

            var (entryMaybe, loaded) = _m._dirty.LoadOrStore(_key, this);
            var entry = entryMaybe!;
            if (loaded)
            {
                lock (entry._mu)
                {
                    _delete = true;
                }
            }
            else
            {
                entry._delete = true;
            }
        }
    }

    internal void DeleteLocked()
    {
        if (_dirty)
        {
            _delete = true;
            return;
        }

        var (entryMaybe, loaded) = _m._dirty.LoadOrStore(_key, this);
        var entry = entryMaybe!;
        if (loaded)
        {
            lock (entry._mu)
            {
                _proxyFor = entry;
                _value = entry._value;
                _delete = true;
                _dirty = entry._dirty;
            }
        }

        entry._delete = true;
    }

    /// <summary>Go: <c>DeleteIf(cond func(V) bool)</c>（Go 里没有对应接口方法，只有这个方法）。</summary>
    public void DeleteIf(Func<V, bool> cond)
    {
        lock (_mu)
        {
            if (_proxyFor != null)
            {
                _proxyFor.DeleteIf(cond);
                return;
            }

            if (cond(_value))
            {
                DeleteLocked();
            }
        }
    }
}

/// <summary>Go 的 <c>SyncMap[K,V]</c>：并发安全的脏追踪 map（对 base 的写时复制）。</summary>
public sealed class SyncMap<K, V> where K : notnull where V : Cloneable<V>
{
    internal readonly Dictionary<K, V>? _base;
    internal readonly TypeScript.Collections.SyncMap<K, SyncMapEntry<K, V>> _dirty = new();

    public SyncMap(Dictionary<K, V>? @base)
    {
        _base = @base;
    }

    private V? BaseAt(K key)
    {
        if (_base == null)
        {
            return default;
        }

        return _base.TryGetValue(key, out var value) ? value : default;
    }

    public (SyncMapEntry<K, V>? Entry, bool Ok) Load(K key)
    {
        var (entryMaybe, ok) = _dirty.Load(key);
        if (ok)
        {
            var entry = entryMaybe!;
            lock (entry._mu)
            {
                if (entry._delete)
                {
                    return (null, false);
                }

                return (entry, true);
            }
        }

        if (_base != null && _base.TryGetValue(key, out var val))
        {
            return (new SyncMapEntry<K, V>(this, key, val, val, dirty: false), true);
        }

        return (null, false);
    }

    public (SyncMapEntry<K, V>? Entry, bool Loaded) LoadOrStore(K key, V value)
    {
        // Check for existence in the base map first so the sync map access is atomic.
        if (_base != null && _base.TryGetValue(key, out var baseValue))
        {
            var (dirtyMaybe, hasDirty) = _dirty.Load(key);
            if (hasDirty)
            {
                var dirty = dirtyMaybe!;
                lock (dirty._mu)
                {
                    if (dirty._delete)
                    {
                        return (null, false);
                    }

                    return (dirty, true);
                }
            }

            return (new SyncMapEntry<K, V>(this, key, baseValue, baseValue, dirty: false), true);
        }

        var (entryMaybe, loaded) = _dirty.LoadOrStore(
            key,
            new SyncMapEntry<K, V>(this, key, original: default, value, dirty: true));
        var entry = entryMaybe!;
        if (loaded)
        {
            lock (entry._mu)
            {
                if (entry._delete)
                {
                    return (null, false);
                }
            }
        }

        return (entry, loaded);
    }

    public void Delete(K key)
    {
        var (entryMaybe, loaded) = _dirty.LoadOrStore(
            key,
            new SyncMapEntry<K, V>(this, key, BaseAt(key), default!, dirty: false, delete: true));
        if (loaded)
        {
            entryMaybe!.Delete();
        }
    }

    public void Range(Func<SyncMapEntry<K, V>, bool> fn)
    {
        var seenInDirty = new HashSet<K>();
        _dirty.Range((key, entryMaybe) =>
        {
            var entry = entryMaybe!;
            seenInDirty.Add(key);
            bool deleted;
            lock (entry._mu)
            {
                deleted = entry._delete;
            }

            if (!deleted && !fn(entry))
            {
                return false;
            }

            return true;
        });

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

            if (!fn(new SyncMapEntry<K, V>(this, pair.Key, pair.Value, pair.Value, dirty: false)))
            {
                break;
            }
        }
    }

    private (Dictionary<K, V>? Result, bool Changed) FinalizeCore(FinalizationHooks<K, V> hooks)
    {
        var changed = false;
        var result = _base;
        void EnsureCloned()
        {
            if (!changed)
            {
                result = _base == null ? new Dictionary<K, V>() : new Dictionary<K, V>(_base);
                changed = true;
            }
        }

        _dirty.Range((key, entryMaybe) =>
        {
            var entry = entryMaybe!;
            lock (entry._mu)
            {
                if (entry._delete)
                {
                    EnsureCloned();
                    hooks.OnDelete?.Invoke(key, entry._value);
                    result!.Remove(key);
                }
                else if (entry._dirty)
                {
                    EnsureCloned();
                    if (hooks.OnChange != null || hooks.OnAdd != null)
                    {
                        if (_base != null && _base.ContainsKey(key))
                        {
                            hooks.OnChange?.Invoke(key, entry._original!, entry._value);
                        }
                        else
                        {
                            hooks.OnAdd?.Invoke(key, entry._value);
                        }
                    }

                    result![key] = entry._value;
                }
            }

            return true;
        });
        return (result, changed);
    }

    public (Dictionary<K, V>? Result, bool Changed) Finalize()
    {
        return FinalizeCore(new FinalizationHooks<K, V>());
    }

    public (Dictionary<K, V>? Result, bool Changed) FinalizeWith(FinalizationHooks<K, V> hooks)
    {
        return FinalizeCore(hooks);
    }
}

/// <summary>Go 的 <c>FinalizationHooks[K,V]</c>：<see cref="SyncMap{K,V}.FinalizeWith"/> 的提交钩子。</summary>
public sealed class FinalizationHooks<K, V> where K : notnull
{
    public Action<K, V>? OnDelete;
    public Action<K, V, V>? OnChange;
    public Action<K, V>? OnAdd;
}

public static partial class Dirty
{
    /// <summary>Go: <c>NewSyncMap[K,V](base map[K]V) *SyncMap[K,V]</c>。</summary>
    public static SyncMap<K, V> NewSyncMap<K, V>(Dictionary<K, V>? @base) where K : notnull where V : Cloneable<V>
    {
        return new SyncMap<K, V>(@base);
    }
}
