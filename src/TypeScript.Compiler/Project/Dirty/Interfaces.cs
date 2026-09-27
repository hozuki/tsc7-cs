// ported from: internal/project/dirty/interfaces.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `Cloneable[T any]` / `Value[T any]` 是**导出**接口，C# 保留原名（`Cloneable<T>` / `Value<T>`）。
//    Go 的接口是结构性（隐式）实现，C# 必须显式写 `: Value<V>` —— 见各实现类（Box/MapEntry/SyncMapEntry/
//    LockedEntry）的声明与文件头注释。
//
// 2) 回调类型逐条对应 Go 的函数类型（本仓既有约定，见 architecture.md §3.2）：
//    `func(T)` → `Action<T>`、`func(T) bool` → `Func<T, bool>`、`func(Value[T])` → `Action<Value<T>>`。
//
// 3) 关于 `Action<Value<T>>`：实现类（如 `MapEntry<K,V>`）里同时存在**方法** `Value()`（Go 的方法同名的
//    必然结果），C# 的 namespace-or-type-name 查找在类型位置只考虑类型参数与嵌套类型，**不**会被同名方法
//    遮蔽，所以这里直接写 `Value<T>` 即可（已在编译中验证）。

namespace TypeScript.Project.Dirty;

/// <summary>
/// Go 的 <c>Cloneable[T]</c>：可克隆的值（脏追踪的基本要求 —— 进入脏集合前先克隆，避免改动共享状态）。
/// </summary>
public interface Cloneable<T>
{
    T Clone();
}

/// <summary>
/// Go 的 <c>Value[T]</c>：脏值（Box / MapEntry / SyncMapEntry 共享的读改视图）。
/// </summary>
public interface Value<T>
{
    /// <summary>Go: <c>Value()</c>。条目已被删除时返回 T 的零值（Go 的 <c>var zero T</c> → C# <c>default!</c>）。</summary>
    T Value();

    /// <summary>Go: <c>Original()</c>。进入脏状态**之前**的值（未改动时等于 Value()）。</summary>
    T Original();

    /// <summary>Go: <c>Dirty()</c>。</summary>
    bool Dirty();

    /// <summary>Go: <c>Change(apply func(T))</c>。首次改动前先 Clone。</summary>
    void Change(Action<T> apply);

    /// <summary>Go: <c>ChangeIf(cond func(T) bool, apply func(T)) bool</c>。cond 为真才 Change，并返回是否改动。</summary>
    bool ChangeIf(Func<T, bool> cond, Action<T> apply);

    /// <summary>Go: <c>Delete()</c>。</summary>
    void Delete();

    /// <summary>Go: <c>Locked(fn func(Value[T]))</c>。持锁回调（Box/MapEntry 无锁，SyncMapEntry 持自身锁）。</summary>
    void Locked(Action<Value<T>> fn);
}
