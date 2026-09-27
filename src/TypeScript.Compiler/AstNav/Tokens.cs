// ported from: internal/astnav/tokens.go:1-337（shouldRescanLessThanLessThanToken / scanNavigationToken /
//   GetTouchingPropertyName / GetTouchingToken / GetTokenAtPosition / getTokenAtPosition / getPosition /
//   findRightmostNode / VisitEachChildAndJSDoc）
//
// 与 Go 的差异（逐条）：
// - 位置一律是 UTF-16 代码单元下标（docs/architecture.md §3.1）；`scanner.GetScannerForSourceFile` 等同理。
// - 包级函数放静态类 `AstNav`（docs/architecture.md §2）。注意 `AstNav` 同时是命名空间名：包外要写全名
//   `TypeScript.AstNav.AstNav.X` 或用 `using static`（同名陷阱）。
// - Go 的内联闭包（`visitNode`/`visitNodes`/`testNode`/`getIncludedPrecedingToken`）→ C# 的局部函数（都会**改写**
//   被捕获的 `next`/`prevSubtree`/`left`/`nodeAfterLeft`，局部函数按 Go 的顺序声明在使用之前）。
// - `panic(fmt.Sprintf(...))` → `throw new DebugFailureException($"...")`。
// - `core.BinarySearchUniqueFunc(x, cmp)` 的 `(index, match)` → `TryBinarySearchUniqueFunc(x, cmp, out index)`（§3.2），
//   下标语义（找不到时是插入点）不变。
// - `ast.NewNodeVisitor(core.Identity, nil, hooks)` 的 visit 参数 → C# 的 `static n => n` 局部委托（等价于 Identity）。
// - `ast.ModifierList.NodeList` 在 C# 里是基类字段（`ModifierList : NodeList`），`&modifiers.NodeList` → `modifiers`。
namespace TypeScript.AstNav;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public static partial class AstNav
{
    private static bool ShouldRescanLessThanLessThanToken(Scanner.Scanner s, Node containingNode, Kind token)
    {
        return token == Kind.LessThanLessThanToken && IsJsxChild(containingNode);
    }

    private static Kind ScanNavigationToken(Scanner.Scanner s, Node containingNode)
    {
        var token = s.Token();
        if (ShouldRescanLessThanLessThanToken(s, containingNode, token))
        {
            return s.ReScanJsxToken(true /*allowMultilineJsxText*/);
        }

        return token;
    }

    public static Node GetTouchingPropertyName(SourceFile sourceFile, int position)
    {
        return GetTokenAtPositionWorker(sourceFile, position, false /*allowPositionInLeadingTrivia*/, node =>
            IsPropertyNameLiteral(node) || IsKeywordKind(node.Kind) || IsPrivateIdentifier(node));
    }

    public static Node GetTouchingToken(SourceFile sourceFile, int position)
    {
        return GetTokenAtPositionWorker(sourceFile, position, false /*allowPositionInLeadingTrivia*/, null);
    }

    public static Node GetTokenAtPosition(SourceFile sourceFile, int position)
    {
        return GetTokenAtPositionWorker(sourceFile, position, true /*allowPositionInLeadingTrivia*/, null);
    }

    // getTokenAtPosition returns a token at the given position in the source file.
    // The token can be a real node in the AST, or a synthesized token constructed
    // with information from the scanner. Synthesized tokens are only created when
    // needed, and they are stored in the source file's token cache such that multiple
    // calls to getTokenAtPosition with the same position will return the same object
    // in memory. If there is no token at the given position (possible when
    // `allowPositionInLeadingTrivia` is false), the lowest node that encloses the
    // position is returned.
    private static Node GetTokenAtPositionWorker(
        SourceFile sourceFile,
        int position,
        bool allowPositionInLeadingTrivia,
        Func<Node, bool>? includePrecedingTokenAtEndPosition)
    {
        // `next` tracks the node whose children will be visited on the next iteration.
        // `prevSubtree` is a node whose end position is equal to the target position,
        // only if `includePrecedingTokenAtEndPosition` is provided. Once set, the next
        // iteration of the loop will test the rightmost token of `prevSubtree` to see
        // if it should be returned.
        Node? next = null;
        Node? prevSubtree = null;
        Node current = sourceFile;

        // `left` tracks the lower boundary of the node/token that could be returned,
        // and is eventually the scanner's start position, if the scanner is used.
        var left = 0;

        // `nodeAfterLeft` tracks the first node we visit after visiting the node that advances `left`.
        // When scanning in between nodes for token, we should only scan up to the start of `nodeAfterLeft`.
        Node? nodeAfterLeft = null;

        Node? GetIncludedPrecedingToken(Node subtree)
        {
            var child = FindPrecedingTokenEx(sourceFile, position, subtree, false /*excludeJSDoc*/);
            if (child is not null && child.End() == position && includePrecedingTokenAtEndPosition!(child))
            {
                return child;
            }

            return null;
        }

        int TestNode(Node node)
        {
            if (node.Kind != Kind.EndOfFile && node.End() == position &&
                includePrecedingTokenAtEndPosition is not null && (node.Flags & NodeFlags.Reparsed) == 0)
            {
                if (prevSubtree is not null && GetIncludedPrecedingToken(prevSubtree) is not null)
                {
                    return 0;
                }

                prevSubtree = node;
            }

            // A node "contains" the position if position < end, except nodes at the file end
            // treat end as inclusive (there's nowhere else to look). This applies to the EOF
            // token itself, and to JSDoc nodes reaching EOF (e.g. unterminated JSDoc comments).
            if (node.End() < position || (node.End() == position &&
                node.Kind != Kind.EndOfFile &&
                (!IsJSDocKind(node.Kind) || node.End() != sourceFile.EndOfFileToken!.End())))
            {
                return -1;
            }

            var nodePos = GetPosition(node, sourceFile, allowPositionInLeadingTrivia);
            if (nodePos > position)
            {
                return 1;
            }

            return 0;
        }

        // We zero in on the node that contains the target position by visiting each
        // child and JSDoc comment of the current node. Node children are walked in
        // order, while node lists are binary searched.
        // 注意：C# 的钩子委托声明是非可空参数，但 Ast.VisitNodeWithHooks 在子节点为 null 时照样把 null
        // 传给钩子（Go 的 visitNode 就是靠 `node == nil` 挡住的），所以这里必须保留 null 检查。
        Node? VisitNodeFn(Node node, NodeVisitor _)
        {
            // We can't abort visiting children, so once a match is found, we set `next`
            // and do nothing on subsequent visits.
            if (node is null || (node.Flags & NodeFlags.Reparsed) != 0)
            {
                return null;
            }

            nodeAfterLeft ??= node;
            if (next is null)
            {
                var result = TestNode(node);
                if (result < 0)
                {
                    if (!IsJSDocKind(node.Kind))
                    {
                        // We can't move the left boundary into or beyond JSDoc,
                        // because we may end up returning the token after this JSDoc,
                        // constructing it with the scanner, and we need to include
                        // all its leading trivia in its position.
                        left = node.End();
                    }

                    nodeAfterLeft = null;
                }
                else if (result == 0)
                {
                    next = node;
                }
            }

            return node;
        }

        NodeList? VisitNodeListFn(NodeList nodeList, NodeVisitor _)
        {
            if (nodeList is null || nodeList.Nodes is null || nodeList.Nodes.Length == 0)
            {
                return nodeList;
            }

            var nodes = nodeList.Nodes;
            if (nodeAfterLeft is null)
            {
                foreach (var node in nodes)
                {
                    if ((node.Flags & NodeFlags.Reparsed) == 0)
                    {
                        nodeAfterLeft = node;
                        break;
                    }
                }
            }

            if (next is null)
            {
                if (nodeList.End() == position && includePrecedingTokenAtEndPosition is not null)
                {
                    left = nodeList.End();
                    nodeAfterLeft = null;
                    for (var i = nodes.Length - 1; i >= 0; i--)
                    {
                        var v = nodes[i];
                        if ((v.Flags & NodeFlags.Reparsed) == 0)
                        {
                            prevSubtree = v;
                            break;
                        }
                    }
                }
                else if (nodeList.End() <= position)
                {
                    left = nodeList.End();
                    nodeAfterLeft = null;
                }
                else if (nodeList.Pos() <= position)
                {
                    var match = TryBinarySearchUniqueFunc(nodes, (middle, node) =>
                    {
                        if ((node.Flags & NodeFlags.Reparsed) != 0)
                        {
                            return 0;
                        }

                        var cmp = TestNode(node);
                        if (cmp < 0)
                        {
                            left = node.End();
                            nodeAfterLeft = null;
                            for (var i = middle + 1; i < nodes.Length; i++)
                            {
                                if ((nodes[i].Flags & NodeFlags.Reparsed) == 0)
                                {
                                    nodeAfterLeft = nodes[i];
                                    break;
                                }
                            }
                        }

                        return cmp;
                    }, out var index);

                    if (match && (nodes[index].Flags & NodeFlags.Reparsed) != 0)
                    {
                        // filter and search again
                        nodes = Filter(nodes, node => (node.Flags & NodeFlags.Reparsed) == 0);
                        match = TryBinarySearchUniqueFunc(nodes, (middle, node) =>
                        {
                            var cmp = TestNode(node);
                            if (cmp < 0)
                            {
                                left = node.End();
                                if (middle + 1 < nodes.Length)
                                {
                                    nodeAfterLeft = nodes[middle + 1];
                                }
                                else
                                {
                                    nodeAfterLeft = null;
                                }
                            }

                            return cmp;
                        }, out index);
                    }

                    if (match)
                    {
                        next = nodes[index];
                    }
                }
            }

            return nodeList;
        }

        while (true)
        {
            VisitEachChildAndJSDoc(current, sourceFile, VisitNodeFn, VisitNodeListFn);

            // If prevSubtree was set on the last iteration, it ends at the target position.
            // Check if the rightmost token of prevSubtree should be returned based on the
            // `includePrecedingTokenAtEndPosition` callback.
            if (prevSubtree is not null)
            {
                var child = GetIncludedPrecedingToken(prevSubtree);
                if (child is not null)
                {
                    // Optimization: includePrecedingTokenAtEndPosition only ever returns true
                    // for real AST nodes, so we don't run the scanner here.
                    return child;
                }

                prevSubtree = null;
            }

            // No node was found that contains the target position, so we've gone as deep as
            // we can in the AST. We've either found a token, or we need to run the scanner
            // to construct one that isn't stored in the AST.
            if (next is null)
            {
                if (IsTokenKind(current.Kind) || ShouldSkipChild(current))
                {
                    return current;
                }

                var scan = Scanner.Scanner.GetScannerForSourceFile(sourceFile, left);
                var end = current.End();

                // We should only scan up to the start of the next node in the AST after the node ending at position `left`.
                // It is necessary to enforce this invariant in cases where `position` occurs in between two node/tokens,
                // such that we would not find a token in the loop below before we reach the next node.
                // We can fall into this case when `allowPositionInLeadingTrivia` is false and `position` is in a leading trivia,
                // or when `position` would be in the leading trivia of a node but this node is inside JSDoc:
                // ```
                // /**
                //  * @type {{
                //  */*$*/ identifier: boolean;
                //  * }}
                //  */
                // ```
                // The position of marker '$' falls in between the asterisk token and the identifier token, but is not
                // part of the leading trivia for `identifier`.
                if (nodeAfterLeft is not null)
                {
                    end = nodeAfterLeft.Pos();
                }

                for (; left < end;)
                {
                    var token = ScanNavigationToken(scan, current);
                    var tokenFullStart = scan.TokenFullStart();
                    var tokenStart = IfElse(allowPositionInLeadingTrivia, tokenFullStart, scan.TokenStart());
                    var tokenEnd = scan.TokenEnd();
                    var flags = scan.TokenFlags;
                    if (tokenEnd > end)
                    {
                        break;
                    }

                    if (tokenStart <= position && position < tokenEnd)
                    {
                        if (token == Kind.Identifier || !IsTokenKind(token))
                        {
                            if (IsJSDocKind(current.Kind))
                            {
                                return current;
                            }

                            throw new DebugFailureException($"did not expect {current.Kind.String()} to have {token.String()} in its trivia");
                        }

                        return sourceFile.GetOrCreateToken(token, tokenFullStart, tokenEnd, current, flags);
                    }

                    if (includePrecedingTokenAtEndPosition is not null && tokenEnd == position)
                    {
                        var prevToken = sourceFile.GetOrCreateToken(token, tokenFullStart, tokenEnd, current, flags);
                        if (includePrecedingTokenAtEndPosition(prevToken))
                        {
                            return prevToken;
                        }
                    }

                    left = tokenEnd;
                    scan.Scan();
                }

                return current;
            }

            current = next;
            left = current.Pos();
            nodeAfterLeft = null;
            next = null;
        }
    }

    private static int GetPosition(Node node, SourceFile sourceFile, bool allowPositionInLeadingTrivia)
    {
        if (allowPositionInLeadingTrivia)
        {
            return node.Pos();
        }

        return Scanner.Scanner.GetTokenPosOfNode(node, sourceFile, true /*includeJSDoc*/);
    }

    private static Node? FindRightmostNode(Node node)
    {
        Node? next = null;
        var current = node;
        Node? VisitNodeFn(Node n, NodeVisitor _)
        {
            if (n is not null)
            {
                next = n;
            }

            return n;
        }

        NodeList? VisitNodesFn(NodeList nodeList, NodeVisitor visitor)
        {
            if (nodeList is not null && nodeList.Nodes is not null)
            {
                var rightmost = FindLastVisibleNode(nodeList.Nodes);
                if (rightmost is not null)
                {
                    next = rightmost;
                }
            }

            return nodeList;
        }

        var visitor = GetNodeVisitor(VisitNodeFn, VisitNodesFn);

        while (true)
        {
            current.VisitEachChild(visitor);
            if (next is null)
            {
                return current;
            }

            current = next;
            next = null;
        }
    }

    public static void VisitEachChildAndJSDoc(
        Node node,
        SourceFile sourceFile,
        NodeVisitorVisitNode visitNode,
        NodeVisitorVisitNodes visitNodes)
    {
        var visitor = GetNodeVisitor(visitNode, visitNodes);
        var jsDocs = node.JSDoc(sourceFile);
        if (jsDocs is not null)
        {
            foreach (var jsdoc in jsDocs)
            {
                if (visitor.Hooks.VisitNode is not null)
                {
                    visitor.Hooks.VisitNode(jsdoc, visitor);
                }
                else
                {
                    visitor.VisitNode(jsdoc);
                }
            }
        }

        node.VisitEachChild(visitor);
    }

    private const int _comparisonLessThan = -1;
    private const int _comparisonEqualTo = 0;
    private const int _comparisonGreaterThan = 1;
}
