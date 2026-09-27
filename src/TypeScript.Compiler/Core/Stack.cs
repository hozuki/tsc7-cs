// ported from: internal/core/stack.go
//
// 与 Go 的差异（依据 docs/architecture.md §3.2）：
// - Go 的 `panic("stack is empty")` → Debug.Fail（抛 DebugFailureException），与 TSPath 的处理一致。
// - Go 的 `s.data = append(s.data, item)` / `s.data = s.data[:l-1]`（切片，容量保留）在 C# 里用
//   List<T> 表达：Add 是同样的摊销增长、RemoveAt(last) 同样把腾出的槽位置为 default（不保留已弹出
//   元素的引用，对应 Go 的 `s.data[l-1] = zero`）。逻辑长度 = Count，对应 Go 的 len(s.data)。
// - 类型名与 System.Collections.Generic.Stack<T> 冲突：本命名空间内部不受影响；外部代码要写
//   `using Stack = TypeScript.Core.Stack;` 别名或 `Core.Stack<T>`（同 TSPath.Path 的注意事项）。
namespace TypeScript.Core;

public sealed class Stack<T>
{
    private readonly List<T> data = [];

    public void Push(T item)
    {
        data.Add(item);
    }

    public T Pop()
    {
        var l = data.Count;
        if (l == 0)
        {
            Debug.Fail("stack is empty");
        }

        var item = data[l - 1];
        data.RemoveAt(l - 1);
        return item;
    }

    public T Peek()
    {
        var l = data.Count;
        if (l == 0)
        {
            Debug.Fail("stack is empty");
        }

        return data[l - 1];
    }

    public int Len()
    {
        return data.Count;
    }
}
