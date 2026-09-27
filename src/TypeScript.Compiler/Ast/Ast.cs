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
    /// （JSDoc 的懒解析已在 SourceFile.ResolveJSDoc + Node.JSDoc/EagerJSDoc 落地，注册点在 parser 包。）
    /// </summary>
    internal static Func<SourceFile, Node, Node[]?>? _parseJSDocForNodeCallback;

    public static void SetParseJSDocForNode(Func<SourceFile, Node, Node[]?> fn) => _parseJSDocForNodeCallback = fn;

    /// <summary>Go: <c>func NewNodeFactory(hooks NodeFactoryHooks) *NodeFactory</c>。</summary>
    public static NodeFactory NewNodeFactory(NodeFactoryHooks hooks) => new() { _hooks = hooks };

    /// <summary>Go: <c>func NewNodeVisitor(visit func(node *Node) *Node, factory *NodeFactory, hooks NodeVisitorHooks) *NodeVisitor</c>（visitor.go:28；factory 为 nil 时给零值工厂）。</summary>
    public static NodeVisitor NewNodeVisitor(NodeVisitorVisit? visit, NodeFactory? factory, NodeVisitorHooks? hooks)
        => new(visit, factory, hooks);

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

    // ── 读写访问判定（ast.go:1268-1520，本轮从"推迟"状态落地）──

    /// <summary>
    /// Go: <c>func IsDeclarationNode(node *Node) bool</c>（ast.go:1515，utilities.go 的 IsDeclaration 直接用它）。
    /// **偏离**：参数写成 `Node?`（Go 里 nil 会 panic），null 时返回 false —— 调用点（utilities.go 的
    /// `IsDeclaration(Node?)`）需要可空参数。
    /// </summary>
    public static bool IsDeclarationNode(Node? node) => node is not null && node.DeclarationData() is not null;

    /// <summary>Go: <c>func IsLocalsContainer(node *Node) bool</c>（ast.go:1552）。参数可空的理由同上。</summary>
    public static bool IsLocalsContainer(Node? node) => node is not null && node.LocalsContainerData() is not null;

    /// <summary>Go: <c>func IsWriteOnlyAccess(node *Node) bool</c>。</summary>
    public static bool IsWriteOnlyAccess(Node node) => AccessKindOf(node) == AccessKind.Write;

    /// <summary>Go: <c>func IsWriteAccess(node *Node) bool</c>。</summary>
    public static bool IsWriteAccess(Node node) => AccessKindOf(node) != AccessKind.Read;

    /// <summary>Go: <c>func IsWriteAccessForReference(node *Node) bool</c>。</summary>
    public static bool IsWriteAccessForReference(Node node)
    {
        var decl = GetDeclarationFromName(node);
        return (decl is not null && DeclarationIsWriteAccess(decl)) ||
            node.Kind == Kind.DefaultKeyword ||
            IsWriteAccess(node);
    }

    /// <summary>Go: <c>func GetDeclarationFromName(name *Node) *Declaration</c>（Go 里 <c>Declaration = Node</c> 别名）。</summary>
    public static Node? GetDeclarationFromName(Node? name)
    {
        if (name is null || name.Parent is null)
        {
            return null;
        }

        var parent = name.Parent;
        switch (name.Kind)
        {
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.NumericLiteral:
                if (IsComputedPropertyName(parent))
                {
                    return parent.Parent;
                }

                // Go 的 fallthrough。
                goto case Kind.Identifier;
            case Kind.Identifier:
                if (IsDeclaration(parent))
                {
                    if (ReferenceEquals(parent.Name(), name))
                    {
                        return parent;
                    }

                    return null;
                }

                if (IsQualifiedName(parent))
                {
                    var tag = parent.Parent;
                    if (tag is not null && IsJSDocParameterTag(tag) && ReferenceEquals(tag.Name(), parent))
                    {
                        return tag;
                    }

                    return null;
                }

                // Go: binExp := parent.Parent（为 nil 时下面的 IsBinaryExpression 会 panic；C# 里 `Is*` 的参数
                // 是非空的，所以这里显式判空——这是与 Go 唯一的差异，且只在 Go 会崩的畸形输入上体现）。
                var binExp = parent.Parent;
                if (binExp is not null && IsBinaryExpression(binExp) && GetAssignmentDeclarationKind(binExp) != JSDeclarationKind.None)
                {
                    // Go: (binExp.left as BindableStaticNameExpression).symbol || binExp.symbol
                    var leftHasSymbol = false;
                    var left = binExp.AsBinaryExpression()!.Left;
                    if (left is not null && left.Symbol is not null)
                    {
                        leftHasSymbol = true;
                    }

                    if ((leftHasSymbol || binExp.Symbol is not null) && ReferenceEquals(GetNameOfDeclaration(binExp), name))
                    {
                        return binExp;
                    }
                }

                break;
            case Kind.PrivateIdentifier:
                if (IsDeclaration(parent) && ReferenceEquals(parent.Name(), name))
                {
                    return parent;
                }

                break;
        }

        return null;
    }

    /// <summary>Go: <c>func declarationIsWriteAccess(decl *Node) bool</c>（未导出 → C# PascalCase）。</summary>
    private static bool DeclarationIsWriteAccess(Node? decl)
    {
        if (decl is null)
        {
            return false;
        }

        // 环境声明里的任何东西都当作写访问，因为它可能来自 JS。
        if ((decl.Flags & NodeFlags.Ambient) != 0)
        {
            return true;
        }

        switch (decl.Kind)
        {
            case Kind.BinaryExpression:
            case Kind.BindingElement:
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.DefaultKeyword:
            case Kind.EnumDeclaration:
            case Kind.EnumMember:
            case Kind.ExportSpecifier:
            case Kind.ImportClause: // default import
            case Kind.ImportEqualsDeclaration:
            case Kind.ImportSpecifier:
            case Kind.InterfaceDeclaration:
            case Kind.JSDocCallbackTag:
            case Kind.JSDocTypedefTag:
            case Kind.JsxAttribute:
            case Kind.ModuleDeclaration:
            case Kind.NamespaceExportDeclaration:
            case Kind.NamespaceImport:
            case Kind.NamespaceExport:
            case Kind.Parameter:
            case Kind.ShorthandPropertyAssignment:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.TypeParameter:
                return true;

            case Kind.PropertyAssignment:

                // 在 `({ x: y } = 0);` 里 `x` 不是写访问。
                return !IsArrayLiteralOrObjectLiteralDestructuringPattern(decl.Parent!);

            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.Constructor:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:

                // 函数只有在提供值（有 body）时才算写。
                switch (decl.Kind)
                {
                    case Kind.FunctionDeclaration:
                        return decl.AsFunctionDeclaration()!.Body is not null;
                    case Kind.FunctionExpression:
                        return decl.AsFunctionExpression()!.Body is not null;
                    case Kind.Constructor:

                        // constructor 节点把 body 存在父节点上？这里与其它一样处理。
                        return decl.AsConstructorDeclaration()!.Body is not null;
                    case Kind.MethodDeclaration:
                        return decl.AsMethodDeclaration()!.Body is not null;
                    case Kind.GetAccessor:
                        return decl.AsGetAccessorDeclaration()!.Body is not null;
                    case Kind.SetAccessor:
                        return decl.AsSetAccessorDeclaration()!.Body is not null;
                }

                return false;

            case Kind.VariableDeclaration:
            case Kind.PropertyDeclaration:

                // 变量/属性只有带 initializer、或在 catch 子句里时才算写。
                var hasInit = false;
                switch (decl.Kind)
                {
                    case Kind.VariableDeclaration:
                        hasInit = decl.AsVariableDeclaration()!.Initializer is not null;
                        break;
                    case Kind.PropertyDeclaration:
                        hasInit = decl.AsPropertyDeclaration()!.Initializer is not null;
                        break;
                }

                return hasInit || IsCatchClause(decl.Parent!);

            case Kind.MethodSignature:
            case Kind.PropertySignature:
            case Kind.JSDocPropertyTag:
            case Kind.JSDocParameterTag:
                return false;

            default:

                // 保持 TS 的行为：遇到预期外的 kind 就崩。
                throw new DebugFailureException("Unhandled case in declarationIsWriteAccess");
        }
    }

    /// <summary>Go: <c>func IsArrayLiteralOrObjectLiteralDestructuringPattern(node *Node) bool</c>（ast.go:1387-1404）。</summary>
    public static bool IsArrayLiteralOrObjectLiteralDestructuringPattern(Node node)
    {
        if (!(IsArrayLiteralExpression(node) || IsObjectLiteralExpression(node)))
        {
            return false;
        }

        // 参数与 Go 一样不做判空（Go 在这里对 nil parent 会 panic，C# 等价于 NRE）。
        var parent = node.Parent!;

        // [a, b, c] 来自：[a, b, c] = someExpression;
        if (IsBinaryExpression(parent) &&
            ReferenceEquals(parent.AsBinaryExpression()!.Left, node) &&
            parent.AsBinaryExpression()!.OperatorToken!.Kind == Kind.EqualsToken)
        {
            return true;
        }

        // [a, b, c] 来自：for([a, b, c] of expression)
        if (IsForOfStatement(parent) && ReferenceEquals(parent.Initializer(), node))
        {
            return true;
        }

        // {x, a: {a, b, c} } = someExpression
        if (IsPropertyAssignment(parent))
        {
            return IsArrayLiteralOrObjectLiteralDestructuringPattern(parent.Parent!);
        }

        // [a, b, c] of  /  [x, [a, b, c] ] = someExpression
        return IsArrayLiteralOrObjectLiteralDestructuringPattern(parent);
    }

    /// <summary>Go: <c>func accessKind(node *Node) AccessKind</c>（未导出 → C# PascalCase）。</summary>
    private static AccessKind AccessKindOf(Node node)
    {
        var parent = node.Parent;
        if (parent is null)
        {
            return AccessKind.Read;
        }

        switch (parent.Kind)
        {
            case Kind.ParenthesizedExpression:
                return AccessKindOf(parent);
            case Kind.PrefixUnaryExpression:
            {
                var op = parent.AsPrefixUnaryExpression()!.Operator;
                return op is Kind.PlusPlusToken or Kind.MinusMinusToken ? AccessKind.ReadWrite : AccessKind.Read;
            }

            case Kind.PostfixUnaryExpression:
            {
                var op = parent.AsPostfixUnaryExpression()!.Operator;
                return op is Kind.PlusPlusToken or Kind.MinusMinusToken ? AccessKind.ReadWrite : AccessKind.Read;
            }

            case Kind.BinaryExpression:
                if (ReferenceEquals(parent.AsBinaryExpression()!.Left, node))
                {
                    var op = parent.AsBinaryExpression()!.OperatorToken!;
                    if (IsAssignmentOperator(op.Kind))
                    {
                        if (op.Kind == Kind.EqualsToken)
                        {
                            return AccessKind.Write;
                        }

                        return AccessKind.ReadWrite;
                    }
                }

                return AccessKind.Read;
            case Kind.PropertyAccessExpression:
                if (!ReferenceEquals(parent.AsPropertyAccessExpression()!.Name(), node))
                {
                    return AccessKind.Read;
                }

                return AccessKindOf(parent);
            case Kind.PropertyAssignment:
            {
                var parentAccess = AccessKindOf(parent.Parent!);

                // 在 `({ x: varname }) = { x: 1 }` 里，左边的 `x` 是读、右边的 `x` 是写。
                if (ReferenceEquals(node, parent.AsPropertyAssignment()!.Name()))
                {
                    return ReverseAccessKind(parentAccess);
                }

                return parentAccess;
            }

            case Kind.ShorthandPropertyAssignment:

                // 假定访问的是局部变量，因为我们不会为 --noUnusedLocals 检查公开属性。
                if (ReferenceEquals(node, parent.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer))
                {
                    return AccessKind.Read;
                }

                return AccessKindOf(parent.Parent!);
            case Kind.ArrayLiteralExpression:
                return AccessKindOf(parent);
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
                if (ReferenceEquals(node, parent.AsForInOrOfStatement()!.Initializer))
                {
                    return AccessKind.Write;
                }

                return AccessKind.Read;
            default:
                return AccessKind.Read;
        }
    }

    /// <summary>Go: <c>func reverseAccessKind(a AccessKind) AccessKind</c>（未导出 → C# PascalCase）。</summary>
    private static AccessKind ReverseAccessKind(AccessKind a)
    {
        switch (a)
        {
            case AccessKind.Read:
                return AccessKind.Write;
            case AccessKind.Write:
                return AccessKind.Read;
            case AccessKind.ReadWrite:
                return AccessKind.ReadWrite;
        }

        throw new DebugFailureException("Unhandled case in reverseAccessKind");
    }
}

/// <summary>Go: <c>type AccessKind int32</c>（成员名去 <c>AccessKind</c> 前缀，§3.3）。</summary>
public enum AccessKind
{
    /// <summary>只读一个变量。</summary>
    Read = 0,

    /// <summary>只写、从不读取，例如 `x=1;`。</summary>
    Write = 1,

    /// <summary>又读又写，例如 `f(x++);`、`x/=1`。</summary>
    ReadWrite = 2,
}
