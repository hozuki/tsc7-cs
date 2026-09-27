// ported from: internal/astnav/tokens.go:339-689（comparison* 常量、FindPrecedingToken / FindPrecedingTokenEx /
//   isValidPrecedingNode / GetStartOfNode / findRightmostValidToken / FindNextToken）
//
// 与 Go 的差异：与 Tokens.cs 文件头同源（局部函数、TryBinarySearchUniqueFunc、DebugFailureException）；
// 另外 `find` 这类**递归**的 Go 闭包（`var find func(...); find = func(...)`）→ C# 的递归局部函数。
namespace TypeScript.AstNav;

using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public static partial class AstNav
{
    // Finds the leftmost token satisfying `position < token.End()`.
    // If the leftmost token satisfying `position < token.End()` is invalid, or if position
    // is in the trivia of that leftmost token,
    // we will find the rightmost valid token with `token.End() <= position`.
    public static Node? FindPrecedingToken(SourceFile sourceFile, int position)
    {
        return FindPrecedingTokenEx(sourceFile, position, null, false);
    }

    public static Node? FindPrecedingTokenEx(SourceFile sourceFile, int position, Node? startNode, bool excludeJSDoc)
    {
        Node? Find(Node n)
        {
            if (IsNonWhitespaceToken(n) && n.Kind != Kind.EndOfFile)
            {
                return n;
            }

            // `foundChild` is the leftmost node that contains the target position.
            // `prevChild` is the last visited child of the current node.
            Node? foundChild = null;
            Node? prevChild = null;
            // 见 Tokens.cs 的注释：钩子可能收到 null。
            Node? VisitNodeFn(Node node, NodeVisitor _)
            {
                // skip synthesized nodes (that will now exist because of jsdoc handling)
                if (node is null || (node.Flags & NodeFlags.Reparsed) != 0)
                {
                    return node;
                }

                if (foundChild is not null)
                {
                    // We cannot abort visiting children, so once the desired child is found, we do nothing.
                    return node;
                }

                if (position < node.End() && (prevChild is null || prevChild.End() <= position))
                {
                    foundChild = node;
                }
                else
                {
                    prevChild = node;
                }

                return node;
            }

            NodeList? VisitNodesFn(NodeList nodeList, NodeVisitor _)
            {
                if (foundChild is not null)
                {
                    return nodeList;
                }

                if (nodeList is not null && nodeList.Nodes is not null && nodeList.Nodes.Length > 0)
                {
                    var nodes = nodeList.Nodes;
                    var match = TryBinarySearchUniqueFunc(nodes, (middle, _) =>
                    {
                        // synthetic jsdoc nodes should have jsdocNode.End() <= n.Pos()
                        if ((nodes[middle].Flags & NodeFlags.Reparsed) != 0)
                        {
                            return _comparisonLessThan;
                        }

                        if (position < nodes[middle].End())
                        {
                            if (middle == 0 || position >= nodes[middle - 1].End())
                            {
                                return _comparisonEqualTo;
                            }

                            return _comparisonGreaterThan;
                        }

                        return _comparisonLessThan;
                    }, out var index);

                    if (match)
                    {
                        foundChild = nodes[index];
                    }

                    var validLookupIndex = IfElse(match, index - 1, nodes.Length - 1);
                    for (var i = validLookupIndex; i >= 0; i--)
                    {
                        if ((nodes[i].Flags & NodeFlags.Reparsed) != 0)
                        {
                            continue;
                        }

                        if (prevChild is null)
                        {
                            prevChild = nodes[i];
                        }
                    }
                }

                return nodeList;
            }

            VisitEachChildAndJSDoc(n, sourceFile, VisitNodeFn, VisitNodesFn);

            if (foundChild is not null)
            {
                // Note that the span of a node's tokens is [getStartOfNode(node, ...), node.end).
                // Given that `position < child.end` and child has constituent tokens, we distinguish these cases:
                // 1) `position` precedes `child`'s tokens or `child` has no tokens (ie: in a comment or whitespace preceding `child`):
                // we need to find the last token in a previous child node or child tokens.
                // 2) `position` is within the same span: we recurse on `child`.
                var start = GetStartOfNode(foundChild, sourceFile, !excludeJSDoc /*includeJSDoc*/);
                var lookInPreviousChild = start >= position || // cursor in the leading trivia or preceding tokens
                    !IsValidPrecedingNode(foundChild, sourceFile);
                if (lookInPreviousChild)
                {
                    if (position >= foundChild.Pos())
                    {
                        // Find jsdoc preceding the foundChild.
                        Node? jsDoc = null;
                        var nodeJSDoc = n.JSDoc(sourceFile);
                        if (nodeJSDoc is not null)
                        {
                            for (var i = nodeJSDoc.Length - 1; i >= 0; i--)
                            {
                                if (nodeJSDoc[i].Pos() >= foundChild.Pos())
                                {
                                    jsDoc = nodeJSDoc[i];
                                    break;
                                }
                            }
                        }

                        if (jsDoc is not null)
                        {
                            if (!excludeJSDoc && position < jsDoc.End())
                            {
                                return Find(jsDoc);
                            }

                            return FindRightmostValidToken(jsDoc.End(), sourceFile, n, position, excludeJSDoc);
                        }

                        return FindRightmostValidToken(foundChild.Pos(), sourceFile, n, -1 /*position*/, excludeJSDoc);
                    }
                    else
                    {
                        // Answer is in tokens between two visited children.
                        return FindRightmostValidToken(foundChild.Pos(), sourceFile, n, position, excludeJSDoc);
                    }
                }
                else
                {
                    // position is in [foundChild.getStart(), foundChild.End): recur.
                    return Find(foundChild);
                }
            }

            // We have two cases here: either the position is at the end of the file,
            // or the desired token is in the unvisited trailing tokens of the current node.
            if (position >= n.End())
            {
                return FindRightmostValidToken(n.End(), sourceFile, n, -1 /*position*/, excludeJSDoc);
            }

            return FindRightmostValidToken(n.End(), sourceFile, n, position, excludeJSDoc);
        }

        var node = startNode ?? sourceFile;
        var result = Find(node);
        if (result is not null && IsWhitespaceOnlyJsxText(result))
        {
            throw new DebugFailureException("Expected result to be a non-whitespace token.");
        }

        return result;
    }

    private static bool IsValidPrecedingNode(Node node, SourceFile sourceFile)
    {
        if (node.Kind == Kind.EndOfFile)
        {
            var jsDocs = node.JSDoc(sourceFile);
            return jsDocs is not null && jsDocs.Length > 0;
        }

        var start = GetStartOfNode(node, sourceFile, false /*includeJSDoc*/);
        var width = node.End() - start;
        return !(IsWhitespaceOnlyJsxText(node) || width == 0);
    }

    public static int GetStartOfNode(Node node, SourceFile file, bool includeJSDoc)
    {
        return Scanner.Scanner.GetTokenPosOfNode(node, file, includeJSDoc);
    }

    // Looks for rightmost valid token in the range [startPos, endPos).
    // If position is >= 0, looks for rightmost valid token that precedes or touches that position.
    private static Node? FindRightmostValidToken(int endPos, SourceFile sourceFile, Node containingNode, int position, bool excludeJSDoc)
    {
        if (position == -1)
        {
            position = containingNode.End();
        }

        // Go 的 `var find func(n *ast.Node, endPos int) *ast.Node` 第一行就是 `if n == nil { return nil }`；
        // 递归时也会传入 nil（rightmostValidNode 为空时），所以参数是可空的。
        Node? Find(Node? n, int endPos)
        {
            if (n is null)
            {
                return null;
            }

            if (IsNonWhitespaceToken(n))
            {
                return n;
            }

            Node? rightmostValidNode = null;
            var rightmostVisitedNodes = new List<Node>(1); // Nodes after the last valid node.
            var hasChildren = false;
            bool ShouldVisitNode(Node node)
            {
                // Node is synthetic or out of the desired range: don't visit it.
                return !((node.Flags & NodeFlags.Reparsed) != 0 ||
                    node.End() > endPos || GetStartOfNode(node, sourceFile, !excludeJSDoc /*includeJSDoc*/) >= position);
            }

            Node? VisitNodeFn(Node node, NodeVisitor _)
            {
                if (node is null || (node.Flags & NodeFlags.Reparsed) != 0)
                {
                    return node;
                }

                hasChildren = true;
                if (!ShouldVisitNode(node))
                {
                    return node;
                }

                rightmostVisitedNodes.Add(node);
                if (IsValidPrecedingNode(node, sourceFile))
                {
                    rightmostValidNode = node;
                    rightmostVisitedNodes.Clear();
                }

                return node;
            }

            NodeList? VisitNodesFn(NodeList nodeList, NodeVisitor _)
            {
                if (nodeList is not null && nodeList.Nodes is not null && nodeList.Nodes.Length > 0)
                {
                    hasChildren = true;
                    var nodes = nodeList.Nodes;
                    TryBinarySearchUniqueFunc(nodes, (middle, node) =>
                    {
                        if (node.End() > endPos)
                        {
                            return _comparisonGreaterThan;
                        }

                        return _comparisonLessThan;
                    }, out var index);

                    var validIndex = -1;
                    for (var i = index - 1; i >= 0; i--)
                    {
                        if (!ShouldVisitNode(nodes[i]))
                        {
                            continue;
                        }

                        if (IsValidPrecedingNode(nodes[i], sourceFile))
                        {
                            validIndex = i;
                            rightmostValidNode = nodes[i];
                            break;
                        }
                    }

                    for (var i = validIndex + 1; i < index; i++)
                    {
                        if (!ShouldVisitNode(nodes[i]))
                        {
                            continue;
                        }

                        rightmostVisitedNodes.Add(nodes[i]);
                    }
                }

                return nodeList;
            }

            VisitEachChildAndJSDoc(n, sourceFile, VisitNodeFn, VisitNodesFn);

            // Three cases:
            // 1. The answer is a token of `rightmostValidNode`.
            // 2. The answer is one of the unvisited tokens that occur after the rightmost valid node.
            // 3. The current node is a childless, token-less node. The answer is the current node.

            // Case 2: Look at unvisited trailing tokens that occur in between the rightmost visited nodes.
            if (!ShouldSkipChild(n)) // JSDoc nodes don't include trivia tokens as children.
            {
                var startPos = rightmostValidNode is not null ? rightmostValidNode.End() : n.Pos();
                var scan = Scanner.Scanner.GetScannerForSourceFile(sourceFile, startPos);
                var tokens = new List<Node>();
                foreach (var visitedNode in rightmostVisitedNodes)
                {
                    // Trailing tokens that occur before this node.
                    for (; startPos < Math.Min(visitedNode.Pos(), position);)
                    {
                        var token = ScanNavigationToken(scan, n);
                        var tokenStart = scan.TokenStart();
                        if (tokenStart >= Math.Min(visitedNode.Pos(), position))
                        {
                            break;
                        }

                        var tokenFullStart = scan.TokenFullStart();
                        var tokenEnd = scan.TokenEnd();
                        startPos = tokenEnd;
                        var flags = scan.TokenFlags;
                        tokens.Add(sourceFile.GetOrCreateToken(token, tokenFullStart, tokenEnd, n, flags));
                        scan.Scan();
                    }

                    startPos = visitedNode.End();
                    scan.ResetPos(startPos);
                    scan.Scan();
                }

                // Trailing tokens after last visited node.
                for (; startPos < Math.Min(endPos, position);)
                {
                    var token = ScanNavigationToken(scan, n);
                    var tokenStart = scan.TokenStart();
                    if (tokenStart >= Math.Min(endPos, position))
                    {
                        break;
                    }

                    var tokenFullStart = scan.TokenFullStart();
                    var tokenEnd = scan.TokenEnd();
                    startPos = tokenEnd;
                    var flags = scan.TokenFlags;
                    tokens.Add(sourceFile.GetOrCreateToken(token, tokenFullStart, tokenEnd, n, flags));
                    scan.Scan();
                }

                var lastToken = tokens.Count - 1;
                // Find preceding valid token.
                for (var i = lastToken; i >= 0; i--)
                {
                    if (!IsWhitespaceOnlyJsxText(tokens[i]))
                    {
                        return tokens[i];
                    }
                }
            }

            // Case 3: childless node.
            if (!hasChildren)
            {
                if (n != containingNode)
                {
                    return n;
                }

                return null;
            }

            // Case 1: recur on rightmostValidNode.
            if (rightmostValidNode is not null)
            {
                endPos = rightmostValidNode.End();
            }

            return Find(rightmostValidNode, endPos);
        }

        return Find(containingNode, endPos);
    }

    public static Node? FindNextToken(Node previousToken, Node parent, SourceFile file)
    {
        Node? Find(Node n)
        {
            if (IsTokenKind(n.Kind) && n.Pos() == previousToken.End())
            {
                // this is token that starts at the end of previous token - return it
                return n;
            }

            // Node that contains `previousToken` or occurs immediately after it.
            Node? foundNode = null;
            Node? VisitNodeFn(Node node, NodeVisitor _)
            {
                if (node is not null && (node.Flags & NodeFlags.Reparsed) == 0 &&
                    node.Pos() <= previousToken.End() && node.End() > previousToken.End())
                {
                    foundNode = node;
                }

                return node;
            }

            NodeList? VisitNodesFn(NodeList nodeList, NodeVisitor _)
            {
                if (nodeList is not null && nodeList.Nodes is not null && nodeList.Nodes.Length > 0 && foundNode is null)
                {
                    var nodes = nodeList.Nodes;
                    var match = TryBinarySearchUniqueFunc(nodes, (_, node) =>
                    {
                        if ((node.Flags & NodeFlags.Reparsed) != 0)
                        {
                            return _comparisonLessThan;
                        }

                        if (node.Pos() > previousToken.End())
                        {
                            return _comparisonGreaterThan;
                        }

                        if (node.End() <= previousToken.Pos())
                        {
                            return _comparisonLessThan;
                        }

                        return _comparisonEqualTo;
                    }, out var index);

                    if (match)
                    {
                        foundNode = nodes[index];
                    }
                }

                return nodeList;
            }

            VisitEachChildAndJSDoc(n, file, VisitNodeFn, VisitNodesFn);

            // Cases:
            // 1. no answer exists
            // 2. answer is an unvisited token
            // 3. answer is in the visited found node

            // Case 3: look for the next token inside the found node.
            if (foundNode is not null)
            {
                return Find(foundNode);
            }

            var startPos = previousToken.End();

            // Case 2: look for the next token directly.
            if (startPos >= n.Pos() && startPos < n.End())
            {
                var scan = Scanner.Scanner.GetScannerForSourceFile(file, startPos);
                var token = scan.Token();
                var tokenFullStart = scan.TokenFullStart();
                var tokenEnd = scan.TokenEnd();
                var flags = scan.TokenFlags;

                // Use tokenFullStart (which includes leading trivia) to match TS's
                // findNextToken behavior where `n.pos === previousToken.end` is checked
                // (TS's pos includes trivia, same as Go's Pos()/tokenFullStart).
                if (tokenFullStart == previousToken.End())
                {
                    return file.GetOrCreateToken(token, tokenFullStart, tokenEnd, n, flags);
                }

                throw new DebugFailureException($"Expected to find next token at {previousToken.End()}, got token {token.String()} at {tokenFullStart}");
            }

            // Case 3: no answer.
            return null;
        }

        return Find(parent);
    }
}
