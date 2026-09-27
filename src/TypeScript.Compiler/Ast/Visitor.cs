// ported from: internal/ast/ast.go（Visitor / visit / visitNodes / visitNodeList / visitModifiers）
//              + internal/ast/visitor.go（NodeVisitor / NodeVisitorHooks / visitEachChild 的实现）
//
// 与 Go 的差异（C# 语言特性）：
// - Go 的 `type Visitor func(*Node) bool` → C# `delegate bool Visitor(Node node)`。
// - Go 内联函数类型（钩子）→ C# 具名委托（`NodeVisitorVisitNode` 等），因为 C# 的字段类型必须是具名类型。
// - Go 的未导出方法名与公开方法名只差首字母（`v.visitNode` vs `v.VisitNode`）；C# 方法名一律 PascalCase，
//   两者会撞名，所以**会走 hooks 的那些包装方法**统一加 `WithHooks` 后缀：
//     `v.visitNode(x)` → `v.VisitNodeWithHooks(x)`、`v.visitNodes(l)` → `v.VisitNodesWithHooks(l)`、
//     `v.visitModifiers(l)` → `v.VisitModifiersWithHooks(l)`、`v.visitEmbeddedStatement(s)` →
//     `v.VisitEmbeddedStatementWithHooks(s)`、`v.visitIterationBody(s)` → `v.VisitIterationBodyWithHooks(s)`、
//     `v.visitFunctionBody(b)` → `v.VisitFunctionBodyWithHooks(b)`、`v.visitToken(t)` → `v.VisitTokenWithHooks(t)`、
//     `v.visitParameters(l)` → `v.VisitParametersWithHooks(l)`、
//     `v.visitTopLevelStatements(l)` → `v.VisitTopLevelStatementsWithHooks(l)`。
//   生成的 `VisitEachChild` 调用的是这些包装方法（与 Go 生成物一致）。
// - `liftToBlock` 是 Go 的未导出方法 → C# 的 `LiftToBlock`（internal）。
// - Go 里的 `panic(...)` → `throw new DebugFailureException(...)`。
namespace TypeScript.Ast;

/// <summary>Go 的 <c>type Visitor func(*Node) bool</c>（返回 true 表示停止遍历）。</summary>
public delegate bool Visitor(Node node);

/// <summary>Go 的 <c>NodeVisitor.Visit</c> 字段类型：<c>func(node *Node) *Node</c>。</summary>
public delegate Node? NodeVisitorVisit(Node node);

public delegate Node? NodeVisitorVisitNode(Node node, NodeVisitor v);

public delegate Node? NodeVisitorVisitToken(Node node, NodeVisitor v);

public delegate NodeList? NodeVisitorVisitNodes(NodeList nodes, NodeVisitor v);

public delegate ModifierList? NodeVisitorVisitModifiers(ModifierList nodes, NodeVisitor v);

public delegate Node? NodeVisitorVisitEmbeddedStatement(Node node, NodeVisitor v);

public delegate Node? NodeVisitorVisitIterationBody(Node node, NodeVisitor v);

public delegate NodeList? NodeVisitorVisitParameters(NodeList nodes, NodeVisitor v);

public delegate Node? NodeVisitorVisitFunctionBody(Node node, NodeVisitor v);

public delegate NodeList? NodeVisitorVisitTopLevelStatements(NodeList nodes, NodeVisitor v);

/// <summary>Go 的 <c>type NodeVisitorHooks struct</c>（拦截 NodeVisitor 的默认行为）。</summary>
public sealed class NodeVisitorHooks
{
    /// <summary>Overrides visiting a Node. Only invoked by the VisitEachChild method on a given Node subtype.</summary>
    public NodeVisitorVisitNode? VisitNode;

    /// <summary>Overrides visiting a Token. Only invoked by the VisitEachChild method on a given Node subtype.</summary>
    public NodeVisitorVisitToken? VisitToken;

    /// <summary>Overrides visiting a NodeList. Only invoked by the VisitEachChild method on a given Node subtype.</summary>
    public NodeVisitorVisitNodes? VisitNodes;

    /// <summary>Overrides visiting a ModifierList. Only invoked by the VisitEachChild method on a given Node subtype.</summary>
    public NodeVisitorVisitModifiers? VisitModifiers;

    /// <summary>
    /// Overrides visiting a Node when it is the embedded statement body of an iteration statement,
    /// `if` statement, or `with` statement.
    /// </summary>
    public NodeVisitorVisitEmbeddedStatement? VisitEmbeddedStatement;

    /// <summary>Overrides visiting a Node when it is the embedded statement body of an iteration statement.</summary>
    public NodeVisitorVisitIterationBody? VisitIterationBody;

    /// <summary>Overrides visiting a ParameterList.</summary>
    public NodeVisitorVisitParameters? VisitParameters;

    /// <summary>Overrides visiting a function body.</summary>
    public NodeVisitorVisitFunctionBody? VisitFunctionBody;

    /// <summary>Overrides visiting a variable environment.</summary>
    public NodeVisitorVisitTopLevelStatements? VisitTopLevelStatements;
}

public sealed class NodeVisitor
{
    /// <summary>Required. The callback used to visit a node.</summary>
    public NodeVisitorVisit? Visit;

    /// <summary>Required. The NodeFactory used to produce new nodes when passed to VisitEachChild.</summary>
    public NodeFactory Factory;

    /// <summary>Hooks to be invoked when visiting a node.</summary>
    public NodeVisitorHooks Hooks = new();

    public NodeVisitor(NodeVisitorVisit? visit, NodeFactory? factory = null, NodeVisitorHooks? hooks = null)
    {
        // Go: func NewNodeVisitor(visit, factory, hooks) —— factory 为 nil 时给一个零值工厂。
        Visit = visit;
        Factory = factory ?? new NodeFactory();
        Hooks = hooks ?? new NodeVisitorHooks();
    }

    public SourceFile VisitSourceFile(SourceFile node) => (SourceFile)VisitNode(node)!;

    /// <summary>
    /// Visits a Node, possibly returning a new Node in its place.
    /// <list type="bullet">
    /// <item>If the input node is null, then the output is null.</item>
    /// <item>If Visit is null, then the output is the input.</item>
    /// <item>If Visit returns null, then the output is null.</item>
    /// <item>If Visit returns a SyntaxList Node, then the output is the only child of the SyntaxList Node.</item>
    /// </list>
    /// </summary>
    public Node? VisitNode(Node? node)
    {
        if (node is null || Visit is null)
        {
            return node;
        }

        var visited = Visit(node);
        if (visited is not null && visited.Kind == Kind.SyntaxList)
        {
            var nodes = visited.AsSyntaxList()!.Children;
            if (nodes is null || nodes.Length != 1)
            {
                throw new DebugFailureException("Expected only a single node to be written to output");
            }

            visited = nodes[0];
            if (visited is not null && visited.Kind == Kind.SyntaxList)
            {
                throw new DebugFailureException("The result of visiting and lifting a Node may not be SyntaxList");
            }
        }

        return visited;
    }

    /// <summary>
    /// Visits an embedded Statement (i.e. the single statement body of a loop, `if..else` branch, etc.),
    /// possibly returning a new Statement in its place.
    /// </summary>
    public Node? VisitEmbeddedStatement(Node? node)
    {
        if (node is null || Visit is null)
        {
            return node;
        }

        var visited = Visit(node);
        if (visited is null)
        {
            return null;
        }

        return LiftToBlock(visited);
    }

    /// <summary>
    /// Visits a NodeList, possibly returning a new NodeList in its place.
    /// <list type="bullet">
    /// <item>If the input NodeList is null, the output is null.</item>
    /// <item>If Visit is null, then the output is the input.</item>
    /// <item>If Visit returns null, the visited Node will be absent in the output.</item>
    /// <item>If Visit returns a different Node than the input, a new NodeList will be generated and returned.</item>
    /// <item>If Visit returns a SyntaxList Node, then the children of that node will be merged into the output.</item>
    /// <item>If a new NodeList is returned for any reason, it will have the same Loc as the input NodeList.</item>
    /// </list>
    /// </summary>
    public NodeList? VisitNodes(NodeList? nodes)
    {
        if (nodes is null || Visit is null)
        {
            return nodes;
        }

        var (result, changed) = VisitSlice(nodes.Nodes);
        if (changed)
        {
            var list = Factory.NewNodeList(result);
            list.Loc = nodes.Loc;
            return list;
        }

        return nodes;
    }

    /// <summary>Visits a ModifierList, possibly returning a new ModifierList in its place（语义同 VisitNodes）。</summary>
    public ModifierList? VisitModifiers(ModifierList? nodes)
    {
        if (nodes is null || Visit is null)
        {
            return nodes;
        }

        var (result, changed) = VisitSlice(nodes.Nodes);
        if (changed)
        {
            var list = Factory.NewModifierList(result);
            list.Loc = nodes.Loc;
            return list;
        }

        return nodes;
    }

    /// <summary>
    /// Visits a slice of Nodes, returning the resulting slice and a value indicating whether the slice was changed.
    /// <list type="bullet">
    /// <item>If the input slice is null, the output is null.</item>
    /// <item>If Visit is null, then the output is the input.</item>
    /// <item>If Visit returns null, the visited Node will be absent in the output.</item>
    /// <item>If Visit returns a different Node than the input, a new slice will be generated and returned.</item>
    /// <item>If Visit returns a SyntaxList Node, then the children of that node will be merged into the output.</item>
    /// </list>
    /// </summary>
    public (Node[]? Result, bool Changed) VisitSlice(Node[]? nodes)
    {
        if (nodes is null || Visit is null)
        {
            return (nodes, false);
        }

        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i]!;
            if (Visit is null)
            {
                break;
            }

            var visited = Visit(node);
            if (visited is null || !ReferenceEquals(visited, node))
            {
                var updated = new List<Node>(nodes.Length);
                for (var j = 0; j < i; j++)
                {
                    updated.Add(nodes[j]);
                }

                while (true)
                {
                    // finish prior loop
                    if (visited is not null)
                    {
                        if (visited.Kind == Kind.SyntaxList)
                        {
                            var children = visited.AsSyntaxList()!.Children;
                            updated.AddRange(children ?? []);
                        }
                        else
                        {
                            updated.Add(visited);
                        }
                    }

                    i++;

                    // loop over remaining elements
                    if (i >= nodes.Length)
                    {
                        break;
                    }

                    if (Visit is not null)
                    {
                        node = nodes[i]!;
                        visited = Visit(node);
                    }
                    else
                    {
                        for (var j = i; j < nodes.Length; j++)
                        {
                            updated.Add(nodes[j]);
                        }

                        break;
                    }
                }

                // Go 的 VisitSlice 在这里 slices.Clone + append 出一个新切片（同样要分配），返回类型就是数组，
                // 所以 ToArray() 不是多余拷贝（docs/architecture.md §3.8：不能把 span 当返回值持有）。
                return (updated.ToArray(), true);
            }
        }

        return (nodes, false);
    }

    /// <summary>Visits each child of a Node, possibly returning a new Node of the same kind in its place.</summary>
    public Node? VisitEachChild(Node? node)
    {
        if (node is null || Visit is null)
        {
            return node;
        }

        return node.VisitEachChild(this);
    }

    // ── 会走 hooks 的包装方法（Go 的 v.visitXxx；见文件头命名说明）──

    internal Node? VisitNodeWithHooks(Node? node)
    {
        var hook = Hooks.VisitNode;
        if (hook is not null)
        {
            return hook(node!, this);
        }

        return VisitNode(node);
    }

    internal Node? VisitEmbeddedStatementWithHooks(Node? node)
    {
        var hook = Hooks.VisitEmbeddedStatement;
        if (hook is not null)
        {
            return hook(node!, this);
        }

        var nodeHook = Hooks.VisitNode;
        if (nodeHook is not null)
        {
            return LiftToBlock(nodeHook(node!, this));
        }

        return VisitEmbeddedStatement(node);
    }

    internal Node? VisitIterationBodyWithHooks(Node? node)
    {
        var hook = Hooks.VisitIterationBody;
        if (hook is not null)
        {
            return hook(node!, this);
        }

        return VisitEmbeddedStatementWithHooks(node);
    }

    internal Node? VisitFunctionBodyWithHooks(Node? node)
    {
        var hook = Hooks.VisitFunctionBody;
        if (hook is not null)
        {
            return hook(node!, this);
        }

        return VisitNodeWithHooks(node);
    }

    internal Node? VisitTokenWithHooks(Node? node)
    {
        var hook = Hooks.VisitToken;
        if (hook is not null)
        {
            return hook(node!, this);
        }

        return VisitNode(node);
    }

    internal NodeList? VisitNodesWithHooks(NodeList? nodes)
    {
        var hook = Hooks.VisitNodes;
        if (hook is not null)
        {
            return hook(nodes!, this);
        }

        return VisitNodes(nodes);
    }

    internal ModifierList? VisitModifiersWithHooks(ModifierList? nodes)
    {
        var hook = Hooks.VisitModifiers;
        if (hook is not null)
        {
            return hook(nodes!, this);
        }

        return VisitModifiers(nodes);
    }

    internal NodeList? VisitParametersWithHooks(NodeList? nodes)
    {
        var hook = Hooks.VisitParameters;
        if (hook is not null)
        {
            return hook(nodes!, this);
        }

        return VisitNodesWithHooks(nodes);
    }

    internal NodeList? VisitTopLevelStatementsWithHooks(NodeList? nodes)
    {
        var hook = Hooks.VisitTopLevelStatements;
        if (hook is not null)
        {
            return hook(nodes!, this);
        }

        return VisitNodesWithHooks(nodes);
    }

    internal Node? LiftToBlock(Node? node)
    {
        Node[]? nodes;
        if (node is null)
        {
            nodes = [];
        }
        else if (node.Kind == Kind.SyntaxList)
        {
            nodes = node.AsSyntaxList()!.Children;
        }
        else
        {
            nodes = [node];
        }

        Node? result;
        if (nodes is not null && nodes.Length == 1)
        {
            result = nodes[0];
        }
        else
        {
            result = Factory.NewBlock(Factory.NewNodeList(nodes), true /* multiLine */);
        }

        if (result!.Kind == Kind.SyntaxList)
        {
            throw new DebugFailureException("The result of visiting and lifting a Node may not be SyntaxList");
        }

        return result;
    }
}

public static partial class Ast
{
    /// <summary>Go: <c>func visit(v Visitor, node *Node) bool</c>。</summary>
    internal static bool Visit(Visitor v, Node? node) => node is not null && v(node);

    /// <summary>Go: <c>func visitNodes(v Visitor, nodes []*Node) bool</c>（Span 视图，不复制）。</summary>
    internal static bool VisitNodes(Visitor v, ReadOnlySpan<Node?> nodes)
    {
        foreach (var node in nodes)
        {
            if (v(node!))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func visitNodeList(v Visitor, nodeList *NodeList) bool</c>。</summary>
    internal static bool VisitNodeList(Visitor v, NodeList? nodeList) =>
        nodeList is not null && VisitNodes(v, nodeList.Nodes.AsSpan());

    /// <summary>Go: <c>func visitModifiers(v Visitor, modifiers *ModifierList) bool</c>。</summary>
    internal static bool VisitModifiers(Visitor v, ModifierList? modifiers) =>
        modifiers is not null && VisitNodes(v, modifiers.Nodes.AsSpan());
}
