// ported from: internal/ast/ids.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.2）：
// - Go 的 `type NodeId uint64` / `type SymbolId uint64` 是"命名整数类型"，在 C# 里落成只读结构体包装
//   （与 Core.TextPos 的做法一致）：Go 的 `NodeId(x)` ↔ `new NodeId(x)`，`uint64(id)` ↔ `(ulong)id`。
//   两个方向的隐式转换都定义，于是 Go 里 `uint64(ast.GetNodeId(node))` 仍写作 `(ulong)nodeId`，
//   把 `ulong` 直接塞进 `NodeId`/`SymbolId` 字段或参数的移植点也不必逐个强制转换。
// - 与 TextPos 同理，**不要**给它们定义 == / != 运算符：一旦有到 ulong 的隐式转换，`id == 0` / `id1 == id2`
//   就会同时匹配"用户定义的 ==(NodeId, NodeId)"与"内建 ulong == ulong"，产生二义性（CS0034）。
//   比较与排序全部经隐式转换落到内建 ulong 运算符，与 Go 的 uint64 语义一致。
// - Go 里两者常做 map 的 key（`map[NodeId]X`、`map[SymbolId]*Type`），所以两个结构体实现
//   IEquatable/GetHashCode；另外实现 IComparable<T>（Go 的 uint64 可排序，`Comparer<T>.Default` 需要它）。
using System.Globalization;

namespace TypeScript.Ast;

/// <summary>Go 的 <c>type NodeId uint64</c>：节点的唯一标识（由 <c>GetNodeId</c> 惰性分配）。</summary>
public readonly struct NodeId : IEquatable<NodeId>, IComparable<NodeId>
{
    public readonly ulong Value;

    public NodeId(ulong value)
    {
        Value = value;
    }

    public static implicit operator ulong(NodeId id) => id.Value;

    public static implicit operator NodeId(ulong value) => new(value);

    public bool Equals(NodeId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is NodeId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(NodeId other) => Value.CompareTo(other.Value);

    // Go 里 NodeId 就是 uint64：`strconv.FormatUint(uint64(id), 10)` 与 `%d` 打印数字本身，
    // 所以这里不能留下结构体默认的格式。
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Go 的 <c>type SymbolId uint64</c>：符号的唯一标识（由 <c>GetSymbolId</c> 惰性分配）。</summary>
public readonly struct SymbolId : IEquatable<SymbolId>, IComparable<SymbolId>
{
    public readonly ulong Value;

    public SymbolId(ulong value)
    {
        Value = value;
    }

    public static implicit operator ulong(SymbolId id) => id.Value;

    public static implicit operator SymbolId(ulong value) => new(value);

    public bool Equals(SymbolId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SymbolId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(SymbolId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
