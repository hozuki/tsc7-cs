// ported from: internal/ls/lsutil/children.go
//
// **本文件是 `internal/ls/lsutil` 的局部移植**（依赖闭包，见 FormatCodeOptions.cs 的文件头）。
//
// 与 Go 的差异（C# 语言特性）：
// - 包级函数 → 静态类 `LsUtil`（§2）。
// - Go 的 `func(n *ast.Node, _ *ast.NodeVisitor) *ast.Node` 钩子 → C# 的 `NodeVisitorVisitNode`/`NodeVisitorVisitNodes`
//   委托（两个参数，第二个是 visitor）。
// - `slices.Backward(list.Nodes)` 的反向遍历 → C# 的倒序下标循环（Go 的 range-over-func 迭代器在 C# 里没有对应物，
//   docs/architecture.md §3.2 已登记的既定做法）。
// - `scanner.GetScannerForSourceFile` 的局部变量名 `scanner` 会遮蔽包名 → 改名为 `scan`。
// - Go 的 `panic(...)` → `Debug.Fail(...)`（§3.2；文本逐字保留）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.LsUtil;

public static partial class LsUtil
{
    /// <summary>Go 的 <c>func GetLastChild(node *ast.Node, sourceFile *ast.SourceFile) *ast.Node</c>。</summary>
    public static Node? GetLastChild(Node node, SourceFile sourceFile)
    {
        var lastChildNode = GetLastVisitedChild(node, sourceFile);
        if (IsJSDocSingleCommentNode(node) && lastChildNode is null)
        {
            return null;
        }

        int tokenStartPos;
        if (lastChildNode is not null)
        {
            tokenStartPos = lastChildNode.End();
        }
        else
        {
            tokenStartPos = node.Pos();
        }

        Node? lastToken = null;
        var scan = GetScannerForSourceFile(sourceFile, tokenStartPos);
        for (var startPos = tokenStartPos; startPos < node.End();)
        {
            var tokenKind = scan.Token();
            var tokenFullStart = scan.TokenFullStart();
            var tokenEnd = scan.TokenEnd();
            lastToken = sourceFile.GetOrCreateToken(tokenKind, tokenFullStart, tokenEnd, node, scan.TokenFlags);
            startPos = tokenEnd;
            scan.Scan();
        }

        return IfElse(lastToken is not null, lastToken, lastChildNode);
    }

    /// <summary>Go 的 <c>func GetLastToken(node *ast.Node, sourceFile *ast.SourceFile) *ast.Node</c>。</summary>
    public static Node? GetLastToken(Node? node, SourceFile sourceFile)
    {
        if (node is null)
        {
            return null;
        }

        if (IsTokenKind(node.Kind) || IsIdentifier(node))
        {
            return null;
        }

        AssertHasRealPosition(node);

        var lastChild = GetLastChild(node, sourceFile);
        if (lastChild is null)
        {
            return null;
        }

        if (lastChild.Kind < Kind.FirstNode)
        {
            return lastChild;
        }

        return GetLastToken(lastChild, sourceFile);
    }

    /// <summary>
    /// Gets the last visited child of the given node.
    /// NOTE: This doesn't include unvisited tokens; for this, use `getLastChild` or `getLastToken`.
    /// </summary>
    public static Node? GetLastVisitedChild(Node node, SourceFile sourceFile)
    {
        Node? lastChild = null;

        NodeVisitorVisitNode visitNode = (n, _) =>
        {
            if (n is not null && (n.Flags & NodeFlags.Reparsed) == 0)
            {
                lastChild = n;
            }

            return n;
        };

        NodeVisitorVisitNodes visitNodeList = (nodeList, _) =>
        {
            if (nodeList is not null && nodeList.Nodes is { Length: > 0 })
            {
                for (var i = nodeList.Nodes.Length - 1; i >= 0; i--)
                {
                    var v = nodeList.Nodes[i]!;
                    if ((v.Flags & NodeFlags.Reparsed) == 0)
                    {
                        lastChild = v;
                        break;
                    }
                }
            }

            return nodeList;
        };

        VisitEachChildAndJSDoc(node, sourceFile, visitNode, visitNodeList);
        return lastChild;
    }

    /// <summary>Go 的 <c>func GetFirstToken(node *ast.Node, sourceFile *ast.SourceFile) *ast.Node</c>。</summary>
    public static Node? GetFirstToken(Node? node, SourceFile sourceFile)
    {
        if (node is null)
        {
            return null;
        }

        if (IsIdentifier(node) || IsTokenKind(node.Kind))
        {
            return null;
        }

        AssertHasRealPosition(node);

        Node? firstChild = null;
        node.ForEachChild(n =>
        {
            if (n is null || (node.Flags & NodeFlags.Reparsed) != 0)
            {
                return false;
            }

            firstChild = n;
            return true;
        });

        int tokenEndPosition;
        if (firstChild is not null)
        {
            tokenEndPosition = firstChild.Pos();
        }
        else
        {
            tokenEndPosition = node.End();
        }

        var scan = GetScannerForSourceFile(sourceFile, node.Pos());
        Node? firstToken = null;
        if (node.Pos() < tokenEndPosition)
        {
            var tokenKind = scan.Token();
            var tokenFullStart = scan.TokenFullStart();
            var tokenEnd = scan.TokenEnd();
            firstToken = sourceFile.GetOrCreateToken(tokenKind, tokenFullStart, tokenEnd, node, scan.TokenFlags);
        }

        if (firstToken is not null)
        {
            return firstToken;
        }

        if (firstChild is null)
        {
            return null;
        }

        if (firstChild.Kind < Kind.FirstNode)
        {
            return firstChild;
        }

        return GetFirstToken(firstChild, sourceFile);
    }

    public static void AssertHasRealPosition(Node node)
    {
        if (PositionIsSynthesized(node.Pos()) || PositionIsSynthesized(node.End()))
        {
            Debug.Fail("Node must have a real position for this operation.");
        }
    }
}
