// ported from: internal/project/dirty/box.go
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 `Box[T Cloneable[T]]` 是导出结构体，C# 是 `Box<T> : Value<T>`（见 Interfaces.cs 第 1 条：
//    Go 的结构性接口在 C# 里要显式声明）。Go 的 `NewBox[T](original T) *Box[T]` → 包级函数
//    `Dirty.NewBox<T>`（architecture.md §2「包级函数」），调用点 `using static TypeScript.Project.Dirty.Dirty;`。
//
// 2) Go 的 `Value()` 在已删除（Delete 之后）时返回 `var zero T` → C# `default!`（引用类型为 null、
//    值类型为 0，与 Go 的零值一致；见 architecture.md 类型映射）。
//
// 3) Go 允许在 nil 接收者上调用这些方法吗？不允许（Box 的方法都会解引用 b），所以 C# 无需保留 nil 分支。
namespace TypeScript.Project.Dirty;

/// <summary>
/// Go 的 <c>Box[T]</c>：单个值的脏追踪盒子（比对整份快照时只替换变化的那一个值）。
/// </summary>
public sealed class Box<T> : Value<T> where T : Cloneable<T>
{
    private T _original;
    private T _value;
    private bool _dirty;
    private bool _delete;

    public Box(T original)
    {
        _original = original;
        _value = original;
    }

    public T Value()
    {
        if (_delete)
        {
            return default!;
        }

        return _value;
    }

    public T Original()
    {
        return _original;
    }

    public bool Dirty()
    {
        return _dirty;
    }

    /// <summary>Go: <c>Set(value T)</c>。直接替换（不再 Clone —— 调用方给的就是自己的新值）。</summary>
    public void Set(T value)
    {
        _value = value;
        _delete = false;
        _dirty = true;
    }

    public void Change(Action<T> apply)
    {
        if (!_dirty)
        {
            _value = _value.Clone();
            _dirty = true;
        }

        apply(_value);
    }

    public bool ChangeIf(Func<T, bool> cond, Action<T> apply)
    {
        if (cond(_value))
        {
            Change(apply);
            return true;
        }

        return false;
    }

    public void Delete()
    {
        _delete = true;
    }

    public void Locked(Action<Value<T>> fn)
    {
        fn(this);
    }

    /// <summary>Go: <c>Finalize() (T, bool)</c>。返回 (当前值, 是否脏/被删除)。</summary>
    public (T Value, bool Dirty) Finalize()
    {
        return (Value(), _dirty || _delete);
    }
}

public static partial class Dirty
{
    /// <summary>Go: <c>NewBox[T](original T) *Box[T]</c>。</summary>
    public static Box<T> NewBox<T>(T original) where T : Cloneable<T>
    {
        return new Box<T>(original);
    }
}
