// ported from: internal/ast/subtreefacts.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.3）：
// - `type SubtreeFacts uint32` + 包级常量 → `enum SubtreeFacts : uint`：成员名 = Go 常量名去掉 `SubtreeFacts`
//   与 `Subtree` 前缀（`SubtreeContainsTypeScript` → `ContainsTypeScript`、`SubtreeFactsComputed` → `Computed`、
//   `SubtreeExclusionsNode` → `ExclusionsNode`…），数值逐条与 Go 一致（`1 << iota` 序列展开成 `1u << n`）。
// - Go 的 unary `^X`（按位取反）在 C# 里是 `~X`。
// - 包级函数（propagate*）放 `Ast` 静态类：Go 里它们未导出（只在本包内使用），C# 侧对应 `internal static`。
// - Go 的 nil 指针参数（`*Node`/`*NodeList`/`*ModifierList`）→ C# 可空引用；Go 的 nil slice（`children.Nodes`）
//   在 C# 里是 `null`（同上：range 不迭代 ↔ 跳过）。
// - Go 的 `core.IfElse(cond, a, b)` 就地写作 C# 条件表达式（避免为了一个 IfElse 把整个 Core 静态类
//   `using static` 进来——`Ast` 的生成成员名可能与之相撞）。
namespace TypeScript.Ast;

[Flags]
public enum SubtreeFacts : uint
{
    // Facts
    // - Flags used to indicate that a node or subtree contains syntax relevant to a specific transform

    ContainsTypeScript = 1u << 0,
    ContainsJsx = 1u << 1,
    ContainsESDecorators = 1u << 2,
    ContainsUsing = 1u << 3,
    ContainsClassStaticBlocks = 1u << 4,
    ContainsESClassFields = 1u << 5,
    ContainsLogicalAssignments = 1u << 6,
    ContainsNullishCoalescing = 1u << 7,
    ContainsOptionalChaining = 1u << 8,
    ContainsMissingCatchClauseVariable = 1u << 9,
    ContainsESObjectRestOrSpread = 1u << 10, // subtree has a `...` somewhere inside it, never cleared
    ContainsForAwaitOrAsyncGenerator = 1u << 11,
    ContainsAnyAwait = 1u << 12,
    ContainsExponentiationOperator = 1u << 13,

    // Markers
    // - Flags used to indicate that a node or subtree contains a particular kind of syntax.

    ContainsLexicalThis = 1u << 14,
    ContainsLexicalSuper = 1u << 15,
    ContainsRestOrSpread = 1u << 16,       // marker on any `...` - cleared on binding pattern exit
    ContainsObjectRestOrSpread = 1u << 17, // marker on any `{...x}` - cleared on most scope exits
    ContainsAwait = 1u << 18,
    ContainsDynamicImport = 1u << 19,
    ContainsClassFields = 1u << 20,
    ContainsDecorators = 1u << 21,
    ContainsIdentifier = 1u << 22,
    ContainsPrivateIdentifierInExpression = 1u << 23,
    ContainsInvalidTemplateEscape = 1u << 24,

    Computed = 1u << 25, // NOTE: This should always be last
    None = 0,

    // Aliases (unused, for documentation purposes only - correspond to combinations in transformers/estransforms/definitions.go)

    ContainsESNext = ContainsESDecorators | ContainsUsing,
    ContainsES2022 = ContainsClassStaticBlocks | ContainsESClassFields,
    ContainsES2021 = ContainsLogicalAssignments,
    ContainsES2020 = ContainsNullishCoalescing | ContainsOptionalChaining,
    ContainsES2019 = ContainsMissingCatchClauseVariable,
    ContainsES2018 = ContainsESObjectRestOrSpread | ContainsForAwaitOrAsyncGenerator | ContainsInvalidTemplateEscape,
    ContainsES2017 = ContainsAnyAwait,
    ContainsES2016 = ContainsExponentiationOperator,

    // Scope Exclusions
    // - Bitmasks that exclude flags from propagating out of a specific context
    //   into the subtree flags of their container.

    ExclusionsNode = Computed,
    ExclusionsEraseable = ~ContainsTypeScript,
    ExclusionsOuterExpression = ExclusionsNode,
    ExclusionsPropertyAccess = ExclusionsNode,
    ExclusionsElementAccess = ExclusionsNode,
    ExclusionsArrowFunction = ExclusionsNode | ContainsAwait | ContainsObjectRestOrSpread,
    ExclusionsFunction = ExclusionsNode | ContainsLexicalThis | ContainsLexicalSuper | ContainsAwait | ContainsObjectRestOrSpread,
    ExclusionsConstructor = ExclusionsNode | ContainsLexicalThis | ContainsLexicalSuper | ContainsAwait | ContainsObjectRestOrSpread,
    ExclusionsMethod = ExclusionsNode | ContainsLexicalThis | ContainsLexicalSuper | ContainsAwait | ContainsObjectRestOrSpread,
    ExclusionsAccessor = ExclusionsNode | ContainsLexicalThis | ContainsLexicalSuper | ContainsAwait | ContainsObjectRestOrSpread,
    ExclusionsProperty = ExclusionsNode | ContainsLexicalThis | ContainsLexicalSuper,
    ExclusionsClass = ExclusionsNode,
    ExclusionsModule = ExclusionsNode | ContainsLexicalThis | ContainsLexicalSuper,
    ExclusionsObjectLiteral = ExclusionsNode | ContainsObjectRestOrSpread,
    ExclusionsArrayLiteral = ExclusionsNode,
    ExclusionsCall = ExclusionsNode,
    ExclusionsNew = ExclusionsNode,
    ExclusionsVariableDeclarationList = ExclusionsNode | ContainsObjectRestOrSpread,
    ExclusionsParameter = ExclusionsNode,
    ExclusionsCatchClause = ExclusionsNode | ContainsObjectRestOrSpread,
    ExclusionsBindingPattern = ExclusionsNode | ContainsRestOrSpread,

    // Masks
    // - Additional bitmasks

    ContainsLexicalThisOrSuper = ContainsLexicalThis | ContainsLexicalSuper,
}

public static partial class Ast
{
    internal static SubtreeFacts PropagateEraseableSyntaxListSubtreeFacts(NodeList? children)
    {
        // Go: core.IfElse(children != nil, SubtreeContainsTypeScript, SubtreeFactsNone)
        return children is not null ? SubtreeFacts.ContainsTypeScript : SubtreeFacts.None;
    }

    internal static SubtreeFacts PropagateEraseableSyntaxSubtreeFacts(Node? child)
    {
        return child is not null ? SubtreeFacts.ContainsTypeScript : SubtreeFacts.None;
    }

    internal static SubtreeFacts PropagateObjectBindingElementSubtreeFacts(Node? child)
    {
        var facts = PropagateSubtreeFacts(child);
        if ((facts & SubtreeFacts.ContainsRestOrSpread) != 0)
        {
            facts &= ~SubtreeFacts.ContainsRestOrSpread;
            facts |= SubtreeFacts.ContainsObjectRestOrSpread | SubtreeFacts.ContainsESObjectRestOrSpread;
        }

        return facts;
    }

    internal static SubtreeFacts PropagateBindingElementSubtreeFacts(Node? child)
    {
        return PropagateSubtreeFacts(child) & ~SubtreeFacts.ContainsRestOrSpread;
    }

    internal static SubtreeFacts PropagateSubtreeFacts(Node? child)
    {
        if (child is null)
        {
            return SubtreeFacts.None;
        }

        return child.PropagateSubtreeFacts();
    }

    internal static SubtreeFacts PropagateNodeListSubtreeFacts(NodeList? children, Func<Node, SubtreeFacts> propagate)
    {
        if (children is null)
        {
            return SubtreeFacts.None;
        }

        var facts = SubtreeFacts.None;
        var nodes = children.Nodes;
        if (nodes is not null)
        {
            foreach (var child in nodes)
            {
                facts |= propagate(child);
            }
        }

        return facts;
    }

    internal static SubtreeFacts PropagateModifierListSubtreeFacts(ModifierList? children)
    {
        if (children is null)
        {
            return SubtreeFacts.None;
        }

        // Go: propagateNodeListSubtreeFacts(&children.NodeList, propagateSubtreeFacts)
        // C# 里 ModifierList 就是 NodeList 的子类，直接传 children。
        return PropagateNodeListSubtreeFacts(children, PropagateSubtreeFacts);
    }
}
