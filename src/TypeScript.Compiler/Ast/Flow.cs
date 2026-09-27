// ported from: internal/ast/flow.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.2/§3.3）：
// - `type FlowFlags uint32` + 包级常量 → `enum FlowFlags : uint`：成员名 = Go 常量名去掉 `FlowFlags`
//   前缀（`FlowFlagsBranchLabel` → `BranchLabel`、`FlowFlagsLabel` → `Label`…），数值逐条与 Go 一致。
// - Go 的 `type FlowLabel = FlowNode` 是类型别名，C# 没有类型别名：凡出现 FlowLabel 处一律写
//   `FlowNode`（不生成别名类型）。
// - Go 的 `FlowSwitchClauseData`/`FlowReduceLabelData` 内嵌 `NodeBase`（合成 AST 节点），C# 里就是
//   `: Node` 的派生类；`NewFlowSwitchClauseData`/`NewFlowReduceLabelData` 是包级函数，放 `Ast` 静态类。
//   Go 的 `newNode(KindUnknown, node, NodeFactoryHooks{})` → `Ast.NewNode(Kind.Unknown, node, default)`
//   （`NodeFactoryHooks{}` 的零值就是 C# 的 `default`）。
// - Go 的 int32 字段（ClauseStart/ClauseEnd）在 C# 里是 `int`（语义相同，见 docs/architecture.md §3.2）。
// - FlowNode/FlowList 的身份语义与 Go 的指针一致：引用相等，不覆写 Equals/GetHashCode。
namespace TypeScript.Ast;

[Flags]
public enum FlowFlags : uint
{
    Unreachable = 1u << 0,    // Unreachable code
    Start = 1u << 1,          // Start of flow graph
    BranchLabel = 1u << 2,    // Non-looping junction
    LoopLabel = 1u << 3,      // Looping junction
    Assignment = 1u << 4,     // Assignment
    TrueCondition = 1u << 5,  // Condition known to be true
    FalseCondition = 1u << 6, // Condition known to be false
    SwitchClause = 1u << 7,   // Switch statement clause
    ArrayMutation = 1u << 8,  // Potential array mutation
    Call = 1u << 9,           // Potential assertion call
    ReduceLabel = 1u << 10,   // Temporarily reduce antecedents of label
    Referenced = 1u << 11,    // Referenced as antecedent once
    Shared = 1u << 12,        // Referenced as antecedent more than once
    Label = BranchLabel | LoopLabel,
    Condition = TrueCondition | FalseCondition,
}

/// <summary>Go 的 <c>type FlowNode struct</c>（Go 的 <c>type FlowLabel = FlowNode</c> 是别名，C# 里直接用本类型）。</summary>
public sealed class FlowNode
{
    public FlowFlags Flags;
    public Node? Node;            // Associated AST node
    public FlowNode? Antecedent;  // Antecedent for all but FlowLabel
    public FlowList? Antecedents; // Linked list of antecedents for FlowLabel
}

/// <summary>Go 的 <c>type FlowList struct</c>：FlowNode 前驱的链表节点。</summary>
public sealed class FlowList
{
    public FlowNode? Flow;
    public FlowList? Next;
}

/// <summary>Go 的 <c>type FlowSwitchClauseData struct</c>（FlowFlagsSwitchClause 的合成 AST 节点）。</summary>
public sealed class FlowSwitchClauseData : Node
{
    public Node? SwitchStatement;
    public int ClauseStart; // Start index of case/default clause range
    public int ClauseEnd;   // End index of case/default clause range

    public bool IsEmpty() => ClauseStart == ClauseEnd;
}

/// <summary>Go 的 <c>type FlowReduceLabelData struct</c>（FlowFlagsReduceLabel 的合成 AST 节点）。</summary>
public sealed class FlowReduceLabelData : Node
{
    public FlowNode? Target;      // Target label
    public FlowList? Antecedents; // Temporary antecedent list
}

public static partial class Ast
{
    internal static Node NewFlowSwitchClauseData(Node? switchStatement, int clauseStart, int clauseEnd)
    {
        var node = new FlowSwitchClauseData
        {
            SwitchStatement = switchStatement,
            ClauseStart = clauseStart,
            ClauseEnd = clauseEnd,
        };
        return NewNode(Kind.Unknown, node, default);
    }

    internal static Node NewFlowReduceLabelData(FlowNode? target, FlowList? antecedents)
    {
        var node = new FlowReduceLabelData
        {
            Target = target,
            Antecedents = antecedents,
        };
        return NewNode(Kind.Unknown, node, default);
    }
}
