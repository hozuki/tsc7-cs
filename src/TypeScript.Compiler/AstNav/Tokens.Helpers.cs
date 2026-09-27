// ported from: internal/astnav/tokens.go:691-794（getNodeVisitor / shouldSkipChild / FindChildOfKind）
//
// 与 Go 的差异：与 Tokens.cs 文件头同源。另：
// - `ast.NewNodeVisitor(core.Identity, nil, hooks)` 的 visit 参数 → `static n => n`（等价于 Identity）。
// - Go 的 `VisitModifiers` 钩子里直接调用可能为 nil 的 `wrappedVisitNodes`（Go 里会 panic）；C# 里显式抛
//   DebugFailureException（同样不可达：两个调用点都传了 visitNodes）。
// - `modifiers.NodeList`（Go 的嵌入字段）→ C# 的基类引用 `modifiers`（`ModifierList : NodeList`）。
// - `ForEachChildAndJSDoc` 的 visitor 在 C# 里是 `Visitor`（`delegate bool Visitor(Node node)`）。
namespace TypeScript.AstNav;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;

public static partial class AstNav
{
    private static NodeVisitor GetNodeVisitor(NodeVisitorVisitNode? visitNode, NodeVisitorVisitNodes? visitNodes)
    {
        NodeVisitorVisitNode? wrappedVisitNode = null;
        if (visitNode is not null)
        {
            wrappedVisitNode = (n, v) => IsJSDocSingleCommentNodeComment(n) ? n : visitNode(n, v);
        }

        NodeVisitorVisitNodes? wrappedVisitNodes = null;
        if (visitNodes is not null)
        {
            wrappedVisitNodes = (n, v) => IsJSDocSingleCommentNodeList(n) ? n : visitNodes(n, v);
        }

        return NewNodeVisitor(
            static n => n, // Go: core.Identity
            null,
            new NodeVisitorHooks
            {
                VisitNode = wrappedVisitNode,
                VisitToken = wrappedVisitNode is null ? null : (n, v) => wrappedVisitNode(n, v),
                VisitNodes = wrappedVisitNodes,
                VisitModifiers = (modifiers, visitor) =>
                {
                    if (modifiers is not null)
                    {
                        if (wrappedVisitNodes is null)
                        {
                            throw new DebugFailureException("wrappedVisitNodes is required when visiting modifiers");
                        }

                        wrappedVisitNodes(modifiers, visitor);
                    }

                    return modifiers;
                },
            });
    }

    private static bool ShouldSkipChild(Node node) =>
        node.Kind == Kind.JSDoc ||
        node.Kind == Kind.JSDocText ||
        node.Kind == Kind.JSDocTypeLiteral ||
        node.Kind == Kind.JSDocSignature ||
        IsJSDocLinkLike(node) ||
        IsJSDocTag(node);

    /// <summary>
    /// Searches for a child node or token of the specified kind within a containing node.
    /// This function scans through both AST nodes and intervening tokens to find the first match.
    /// </summary>
    public static Node? FindChildOfKind(Node containingNode, Kind kind, SourceFile sourceFile)
    {
        var lastNodePos = containingNode.Pos();
        var scan = Scanner.Scanner.GetScannerForSourceFile(sourceFile, lastNodePos);

        Node? foundChild = null;
        bool VisitNodeFn(Node node)
        {
            if ((node.Flags & NodeFlags.Reparsed) != 0)
            {
                return false;
            }

            // Look for child in preceding tokens.
            var startPos = lastNodePos;
            while (startPos < node.Pos())
            {
                var tokenKind = scan.Token();
                var tokenEnd = scan.TokenEnd();
                if (tokenKind == kind)
                {
                    var tokenFullStart = scan.TokenFullStart();
                    var flags = scan.TokenFlags;
                    foundChild = sourceFile.GetOrCreateToken(tokenKind, tokenFullStart, tokenEnd, containingNode, flags);
                    return true;
                }

                startPos = tokenEnd;
                scan.Scan();
            }

            if (node.Kind == kind)
            {
                foundChild = node;
                return true;
            }

            lastNodePos = node.End();
            scan.ResetPos(lastNodePos);
            return false;
        }

        ForEachChildAndJSDoc(containingNode, sourceFile, VisitNodeFn);

        if (foundChild is not null)
        {
            return foundChild;
        }

        // Look for child in trailing tokens.
        var trailingStartPos = lastNodePos;
        while (trailingStartPos < containingNode.End())
        {
            var tokenKind = scan.Token();
            var tokenEnd = scan.TokenEnd();
            if (tokenKind == kind)
            {
                var tokenFullStart = scan.TokenFullStart();
                var flags = scan.TokenFlags;
                return sourceFile.GetOrCreateToken(tokenKind, tokenFullStart, tokenEnd, containingNode, flags);
            }

            trailingStartPos = tokenEnd;
            scan.Scan();
        }

        return null;
    }
}
