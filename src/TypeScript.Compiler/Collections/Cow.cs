// ported from: internal/collections/cow.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 CopyOnWriteMap 是**结构体**（零值可用：m == nil, owned == false），所有方法都是指针接收者，
//    EnterScope 靠 `saved := *c; c.owned = false; return func() { *c = saved }` 实现作用域进出。
//    C# 用**类**（引用语义）：EnterScope 返回的 Action 闭包捕获进入作用域时的 (m, owned) 快照，
//    恢复时写回同一个实例 —— 与 Go 通过指针改结构体字段的效果一致。相应地 Go 的零值
//    `var c collections.CopyOnWriteMap[K,V]` 在 C# 里写成 `new CopyOnWriteMap<K,V>()`（字段声明处
//    直接用 `= new()`）。差异点：若某个 Go 调用方**按值拷贝**过这个结构体（例如拷贝了包含它的宿主
//    结构体），Go 的拷贝是"m 别名共享 + owned 各自独立"，而 C# 复制字段只是复制同一个对象引用；
//    TSGO 里所有 COW 字段都是通过指针访问（checker/nodebuilderimpl.go:91-94 +
//    nodebuilderscopes.go:23-26），不触发这条差异 —— 见 "unverified"。
//
// 2) Go 的零值 m 是 nil map；C# 在构造函数里建成空字典并把 owned 初始化为 true。理由：Go 的
//    (m == nil, owned == false) 含义是"我没有自己的存储"（首次写时 make），C# 的"空字典 + owned=true"
//    含义是"我拥有一份空的存储"，对外可观察行为相同（读是空、首次写无需克隆）。决定共享/克隆的是
//    EnterScope 把 owned 置 false —— 与 Go 一致。
//
// 3) ensureOwned：Go 是 `if owned return; if m == nil { m = make() } else { m = maps.Clone(m) }; owned = true`。
//    C# 里 m 恒非空，只剩克隆一条路径：即使 m 是空字典也会做一次空拷贝（多一次空 Dictionary 分配，
//    换来读写路径不需要判空；Go 只在 m 恰好为 nil 时才省掉这次拷贝）。
//
// 4) 值语义：Get 返回 `(V?, bool)`（对应 Go 的 `v, ok := c.m[k]`：键不存在时 Go 是 V 的零值、C# 是 null）。
//
// 5) nil 接收者：Go 的 COW 方法也没有 nil 接收者分支（nil 接收者会在 ensureOwned 写字段时 panic）；
//    C# 的实例方法不能在 null 引用上分派，语义一致，不需要额外分支。
//
// 6) 泛型约束：内部用 Dictionary，要求 K 满足 notnull（CS8714），故加 `where K : notnull`
//    （对应 Go 的 `K comparable`）。

namespace TypeScript.Collections;

/// <summary>
/// Go 的 <c>CopyOnWriteMap[K,V]</c>：推迟克隆，直到第一次写入才把继承来的存储复制一份；
/// 支持嵌套作用域（作用域内读父级的条目，首次写时透明克隆，父级视图不被修改）。
/// </summary>
public sealed class CopyOnWriteMap<K, V> where K : notnull
{
    private Dictionary<K, V> _m;
    private bool _owned;

    /// <summary>Go 的零值 <c>CopyOnWriteMap[K,V]{}</c>；C# 里对应 <c>new CopyOnWriteMap&lt;K,V&gt;()</c>。</summary>
    public CopyOnWriteMap()
    {
        _m = new Dictionary<K, V>();
        _owned = true;
    }

    /// <summary>Get returns the value for k and whether it was present.</summary>
    public (V? Value, bool Ok) Get(K k)
    {
        if (_m.TryGetValue(k, out var value))
        {
            return (value, true);
        }

        return (default, false);
    }

    /// <summary>Has reports whether k is in the map.</summary>
    public bool Has(K k)
    {
        return _m.ContainsKey(k);
    }

    /// <summary>Set assigns v to k, cloning the inherited backing map first if necessary.</summary>
    public void Set(K k, V v)
    {
        EnsureOwned();
        _m[k] = v;
    }

    private void EnsureOwned()
    {
        if (_owned)
        {
            return;
        }

        // Go: if c.m == nil { c.m = make(map[K]V) } else { c.m = maps.Clone(c.m) }（见文件头第 3 条）
        _m = new Dictionary<K, V>(_m);
        _owned = true;
    }

    /// <summary>
    /// EnterScope returns a function that restores this map to its current state.
    /// While the scope is active, the map shares its current backing storage with the parent scope:
    /// reads see the inherited entries, and the first mutation transparently clones the storage so the
    /// parent's view is not modified.
    /// （Go 的 <c>EnterScope() func()</c> → C# 的 <c>Action</c>，保持 Go 的形状，不改成 IDisposable）
    /// </summary>
    public Action EnterScope()
    {
        // Go: saved := *c; c.owned = false; return func() { *c = saved }
        var savedMap = _m;
        var savedOwned = _owned;
        _owned = false;
        return () =>
        {
            _m = savedMap;
            _owned = savedOwned;
        };
    }
}

/// <summary>Go 的 <c>CopyOnWriteSet[K]</c>：建立在 CopyOnWriteMap 之上（值为 <c>struct{}</c>）。</summary>
public sealed class CopyOnWriteSet<K> where K : notnull
{
    // Go: `m CopyOnWriteMap[K, struct{}]`（C# 没有零尺寸类型，用 byte 占位）
    private readonly CopyOnWriteMap<K, byte> _m = new();

    /// <summary>Has reports whether k is in the set.</summary>
    public bool Has(K k)
    {
        return _m.Has(k);
    }

    /// <summary>Set adds k to the set, cloning the inherited backing map first if necessary.</summary>
    public void Add(K k)
    {
        _m.Set(k, 0);
    }

    /// <summary>EnterScope returns a function that restores this set to its current state.</summary>
    public Action EnterScope()
    {
        return _m.EnterScope();
    }
}
