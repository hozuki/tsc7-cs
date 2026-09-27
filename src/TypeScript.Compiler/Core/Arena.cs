// ported from: internal/core/arena.go
//
// 与 Go 的偏离（逐条说明；除注明外都是"C# 语言特性没有对应物"导致的等价改写）：
//
// 1) Go 的 `Arena[T]` 是**结构体**、四个方法全是**指针接收者**（`func (a *Arena[T]) New()`）；Go 侧永远
//    通过 `*Arena[T]`（NodeFactory 的字段）原地修改 bump 状态。C# 用 `sealed class` 表达同一件事：
//    结构体在 readonly 字段上调用非 readonly 方法会**隐式拷贝接收者**，bump 状态会被静默丢弃（C# 的
//    已知陷阱）；引用类型与 Go 的指针接收者语义一致，也保证 `data`（当前块）的替换对所有调用方可见。
//    零值：Go 的零值 `Arena[T]{}` 可直接用 → C# 用 `new Arena<T>()`（字段默认 null/0，等价）。
//
// 2) `*T`（指向 arena 内部元素的指针）→ `ref T`（`data[index]` 的托管内部引用）。.NET 的 GC 把托管内部
//    byref 登记进栈/寄存器根表，并在压缩搬移时更新它，所以即使整块数组被 GC 移动，`ref` 仍指向同一元素
//    —— 与 Go "指向对象内部的指针让整个对象存活、地址稳定" 的效果一致。
//    **`ref` 不能存进字段**（C# 语言规则）：要长期持有元素只能让 T 是引用类型，或用 `(T[] block, int index)`
//    句柄（Go 里没有这种类型，本文件不提供）。
//
// 3) **`[]T`（切片）→ `T[]`（不是 `Span<T>`）**：Go 的 `NewSlice/NewSlice1/Clone` 的返回值会被调用点
//    **存进节点/结构体字段**（`NodeList.Nodes`、`singleDeclarations`、JSDoc 的 comments/quotes…），而
//    `Span<T>` 与 `ref` 一样不能存进字段，所以返回 `T[]` 以保住调用点形状。
//    代价：C# 的数组不是"从 slab 里切出来的一段"（数组没有 len < cap 的视图），所以这三个方法**退化成
//    普通分配**（`new T[size]`），只有 `ref T New()` 保留 slab/bump 语义。可观察语义仍与 Go 一致：Go 的
//    三下标切片 `data[i:j:j]` 也是"长度 = 容量 = size 的一段独立区域"（写入不会串到 arena 的其它槽位），
//    只是内存来自 arena 的块；而 Go 对"请求大于下一个块大小"的请求本来就走单独分配。
//    于是 `NewSlice` 里"能塞进块就从块里切、否则先扩容再切"的两种分支不再有意义（每次都等价于 Go 的
//    "单独分配"分支），`slices.Grow` 的"容量向上取整到 size class"也不再出现；`nextArenaSize` 仍被
//    `New()` 用来决定下一块的大小，`data`/`length` 现在**只服务 `New()`**。
//    * 将来的性能优化点：Go 里 `p.nodeSliceArena.NewSlice(len(ranges))[:0]` + append 是"预占容量再写"
//      （保留 cap，append 直接写进 arena 的块），C# 的 `T[]`/`Span<T>` 都表达不了 —— 这些调用点要改成
//      `List<T>` 或"数组 + 长度计数器"；真要恢复 slab 语义只能引入句柄类型。
//
// 4) Go 的 `data []T` 同时编码 len 与 cap；C# 数组没有 "len < cap" 这个状态，所以拆成
//    `T[] data`（cap = `data.Length`）+ `int length`（= Go 的 `len(a.data)`）。
//    Go 的 `slices.Grow[[]T](nil, nextSize)` 分配的是**长度 0、容量 nextSize 的新底层数组**，旧块被
//    丢弃（仅因仍被先前返回的元素引用而存活——这正是地址稳定的来源）→ C# 对应
//    `data = new T[nextSize]; length = 0;`（**不拷贝**旧元素，与 Go 完全一致）。
//    Go 的 growslice 会把容量**向上对齐到 size class**（实际 cap 可能 > nextSize），C# 无法复刻运行时
//    分配器的对齐，故按精确的 nextSize 分配：只影响内部扩容时机，外部不可观察。
//
// 5) `NewSlice(0)` / `Clone(空切片)` 在 Go 里返回 **nil 切片** → C# 返回**空数组** `[]`（docs/architecture.md
//    §3.2：语义等同时用 `[]`）。Go 的 nil 与非 nil 空切片只能用 `== nil` 区分，本项目调用点都当"空"用、不区分。
//
// 6) `Clone(t []T)` 的入参：Go 的 `[]T` 可以是任意切片 → C# 用 `ReadOnlySpan<T>`（`T[]` 会隐式转换），
//    这样既能传数组、也能传别的切片来源。
//
// 7) `nextArenaSize(size int)` → `internal static Core.NextArenaSize`（Math.Max/Math.Min 保持 Go 的两行）。
//
// **Ast 侧结论（已与父 agent 确认）**：AST 节点走 **class**（`Token : NodeBase` …），**NodeFactory 不使用
// Arena**（`f.tokenArena.New()` 的位置就是 `new Token()`）。原因：Go 的 `data := arena.New(); …; return
// f.newNode(kind, data)` 把 `*T` 长期存进节点，而 C# 的 `ref T` / `Span<T>` 都存不进字段；"struct 节点 +
// 句柄类型" 是 Go 里没有的结构性偏离，不做。本类因此只服务"需要结构体紧凑存放 + 稳定槽位引用"的场景
// （LinkStore/PagedLinkStore 已定为 `where V : class, new()`，也不用本类）。
namespace TypeScript.Core;

using static TypeScript.Core.Core;

/// <summary>Go 的 <c>Arena[T]</c>：bump allocator。</summary>
public sealed class Arena<T>
{
    // Go: `data []T`。C# 数组没有 len/cap 之分，故 cap = data.Length，len = length。
    // 切片方法（NewSlice/NewSlice1/Clone）退化成普通分配后，这两个字段只被 New() 使用。
    private T[]? _data;
    private int _length;

    /// <summary>
    /// Allocate a single element in the arena and return a pointer to the element. If the arena is at capacity,
    /// a new arena of the next size up is allocated.
    /// </summary>
    public ref T New()
    {
        var array = _data;
        if (array is null || _length == array.Length)
        {
            var nextSize = NextArenaSize(_length);
            // Use the same trick as slices.Concat; Grow rounds up to the next size class.
            array = new T[nextSize];
            _data = array;
            _length = 0;
        }

        var index = _length;
        _length = index + 1;
        return ref array[index];
    }

    /// <summary>
    /// Allocate a slice of the given size in the arena. If the requested size is beyond the capacity of the arena
    /// and an arena of the next size up still wouldn't fit the slice, make a separate memory allocation for the slice.
    /// Otherwise, grow the arena if necessary and allocate a slice out of it. The length and capacity of the resulting
    /// slice are equal to the given size.
    /// </summary>
    public T[] NewSlice(int size)
    {
        if (size == 0)
        {
            return []; // Go: nil 切片（本项目调用点不区分 nil 与非 nil 空切片）
        }

        // 退化（见文件头第 3 条）：Go 在这里从 arena 的块里切一段（必要时先扩容），C# 的数组无法成为
        // "块里的视图"，所以直接分配 —— 等价于 Go 里 size > nextSize 时走的那条单独分配分支。
        return new T[size];
    }

    public T[] NewSlice1(T t)
    {
        var slice = NewSlice(1);
        slice[0] = t;
        return slice;
    }

    public T[] Clone(ReadOnlySpan<T> t)
    {
        if (t.Length == 0)
        {
            return []; // Go: nil 切片
        }

        var slice = NewSlice(t.Length);
        t.CopyTo(slice);
        return slice;
    }
}

public static partial class Core
{
    internal static int NextArenaSize(int size)
    {
        // This compiles down branch-free.
        size = Math.Max(size, 1);
        size = Math.Min(size * 2, 256);
        return size;
    }
}
