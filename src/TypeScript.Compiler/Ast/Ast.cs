// ported from: internal/ast/ast.go（包级函数：Visitor 辅助之外的部分——newNode/updateNode/cloneNode、
//                    NewNodeFactory、几个 Kind 判断小函数、handWrittenVisitor 的两个函数）
//
// 与 Go 的差异（C# 语言特性）：
// - Go 的包级函数在 C# 里放与 package 同名的静态类 `Ast`（docs/architecture.md §2），生成物也 partial 进这个类
//   （`Is*` 判断、Kind 别名守卫、`String(this Kind)`）。
// - Go 的未导出函数名按 C# 习惯写成 PascalCase（`newNode` → `NewNode`、`updateNode` → `UpdateNode`、
//   `cloneNode` → `CloneNode`；`SetParseJSDocForNode` 保持 Go 的公开名）。
// - `SubtreeFacts`/`ModifierFlags`/`Symbol` 是属性（见 Node.cs 文件头），调用点 `x.SubtreeFacts()` → `x.SubtreeFacts`。
// - 本文件只收录 ast.go 的包级函数；utilities.go 的其余部分留给下一轮（见 Utilities.cs 文件头）。
using static TypeScript.Core.Core;

namespace TypeScript.Ast;

public static partial class Ast
{
    /// <summary>
    /// Go 的 <c>parseJSDocForNode</c> + <c>SetParseJSDocForNode</c>：由 parser 包注册的懒解析回调。
    /// Go 是包级变量（类型 <c>func(*SourceFile, *Node) []*Node</c>）；C# 用静态字段。
    /// （JSDoc 的懒解析随 SourceFile 一起推到下一轮，注册点在 parser 包。）
    /// </summary>
    internal static Func<SourceFile, Node, Node[]?>? _parseJSDocForNodeCallback;

    public static void SetParseJSDocForNode(Func<SourceFile, Node, Node[]?> fn) => _parseJSDocForNodeCallback = fn;

    /// <summary>Go: <c>func NewNodeFactory(hooks NodeFactoryHooks) *NodeFactory</c>。</summary>
    public static NodeFactory NewNodeFactory(NodeFactoryHooks hooks) => new() { _hooks = hooks };

    /// <summary>Go: <c>func newNode(kind Kind, data nodeData, hooks NodeFactoryHooks) *Node</c>。</summary>
    internal static Node NewNode(Kind kind, Node node, NodeFactoryHooks? hooks = null)
    {
        node.Loc = UndefinedTextRange();
        node.Kind = kind;
        hooks?.OnCreate?.Invoke(node);
        return node;
    }

    /// <summary>Go: <c>func updateNode(updated *Node, original *Node, hooks NodeFactoryHooks) *Node</c>。</summary>
    internal static Node UpdateNode(Node updated, Node original, NodeFactoryHooks hooks)
    {
        if (!ReferenceEquals(updated, original))
        {
            updated.Flags = original.Flags;
            updated.Loc = original.Loc;
            hooks.OnUpdate?.Invoke(updated, original);
        }

        return updated;
    }

    /// <summary>Go: <c>func cloneNode(updated *Node, original *Node, hooks NodeFactoryHooks) *Node</c>。</summary>
    internal static Node CloneNode(Node updated, Node original, NodeFactoryHooks hooks)
    {
        UpdateNode(updated, original, hooks);
        if (!ReferenceEquals(updated, original))
        {
            hooks.OnClone?.Invoke(updated, original);
        }

        return updated;
    }

    /// <summary>Go: <c>func IsTypeOrJSTypeAliasDeclaration(node *Node) bool</c>。</summary>
    public static bool IsTypeOrJSTypeAliasDeclaration(Node node) =>
        node.Kind is Kind.TypeAliasDeclaration or Kind.JSTypeAliasDeclaration;

    /// <summary>Go: <c>func IsImportDeclarationOrJSImportDeclaration(node *Node) bool</c>。</summary>
    public static bool IsImportDeclarationOrJSImportDeclaration(Node node) =>
        node.Kind is Kind.ImportDeclaration or Kind.JSImportDeclaration;

    /// <summary>Go: <c>func IsAnyExportAssignment(node *Node) bool</c>。</summary>
    public static bool IsAnyExportAssignment(Node node) => node.Kind == Kind.ExportAssignment;

    /// <summary>
    /// Go: <c>func forEachChild_JSDocParameterOrPropertyTag(node, v Visitor) bool</c>
    /// （handWrittenVisitor 节点的运行时子节点顺序；由生成的 ForEachChild 委托过来）。
    /// </summary>
    internal static bool ForEachChildJSDocParameterOrPropertyTag(Node node, Visitor v)
    {
        var tag = node.AsJSDocParameterOrPropertyTag()!;
        return Visit(v, tag.TagName) ||
            (tag.IsNameFirst && (Visit(v, tag._name) || Visit(v, tag.TypeExpression))) ||
            (!tag.IsNameFirst && (Visit(v, tag.TypeExpression) || Visit(v, tag._name))) ||
            VisitNodeList(v, tag.Comment);
    }

    /// <summary>Go: <c>func visitEachChild_JSDocParameterOrPropertyTag(node, v *NodeVisitor) *Node</c>。</summary>
    internal static Node? VisitEachChildJSDocParameterOrPropertyTag(Node node, NodeVisitor v)
    {
        var tag = node.AsJSDocParameterOrPropertyTag()!;
        return v.Factory.UpdateJSDocParameterOrPropertyTag(
            tag,
            v.VisitNodeWithHooks(tag.TagName),
            v.VisitNodeWithHooks(tag._name),
            tag.IsBracketed,
            v.VisitNodeWithHooks(tag.TypeExpression),
            tag.IsNameFirst,
            v.VisitNodesWithHooks(tag.Comment));
    }
}
