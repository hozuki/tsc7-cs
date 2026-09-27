// ported from: internal/ls/lsutil/completednode.go
//
// **本文件是 `internal/ls/lsutil` 的局部移植**（依赖闭包，见 FormatCodeOptions.cs 的文件头）。
//
// 与 Go 的差异（C# 语言特性）：
// - 包级函数 → 静态类 `LsUtil`（§2）。
// - `case …: fallthrough` 在 C# 里用 `goto case <目标标签>` 表达（C# 只允许空 case 直落）。
// - Go 的 `n.AsXxx()` 返回 `*Xxx`（nil 安全调用）→ C# 是 `as` 转换的结果，链式访问要 `!`（同文件内已由
//   上面的 `case` 判断非空，属 §3.5 允许的"同一方法内已证明非空"）。
// - `panic(...)` → `Debug.Fail(...)`（§3.2；文本逐字保留）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;

namespace TypeScript.LsUtil;

public static partial class LsUtil
{
    /// <summary>
    /// PositionBelongsToNode returns true if the position belongs to the node.
    /// Assumes `candidate.Pos() &lt;= position` holds.
    /// </summary>
    public static bool PositionBelongsToNode(Node candidate, int position, SourceFile file)
    {
        if (candidate.Pos() > position)
        {
            Debug.Fail("Expected candidate.pos <= position");
        }

        return position < candidate.End() || !IsCompletedNode(candidate, file);
    }

    public static bool IsCompletedNode(Node? n, SourceFile sourceFile)
    {
        if (n is null || NodeIsMissing(n))
        {
            return false;
        }

        switch (n.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
            case Kind.ObjectLiteralExpression:
            case Kind.ObjectBindingPattern:
            case Kind.TypeLiteral:
            case Kind.Block:
            case Kind.ModuleBlock:
            case Kind.CaseBlock:
            case Kind.NamedImports:
            case Kind.NamedExports:
                return NodeEndsWith(n, Kind.CloseBraceToken, sourceFile);

            case Kind.CatchClause:
                return IsCompletedNode(n.AsCatchClause()!.Block, sourceFile);

            case Kind.NewExpression:
                if (n.ArgumentList() is null)
                {
                    return true;
                }

                goto case Kind.CallExpression;

            case Kind.CallExpression:
            case Kind.ParenthesizedExpression:
            case Kind.ParenthesizedType:
                return NodeEndsWith(n, Kind.CloseParenToken, sourceFile);

            case Kind.FunctionType:
            case Kind.ConstructorType:
                return IsCompletedNode(n.Type(), sourceFile);

            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.ConstructSignature:
            case Kind.CallSignature:
            case Kind.ArrowFunction:
                if (n.Body() is not null)
                {
                    return IsCompletedNode(n.Body(), sourceFile);
                }

                if (n.Type() is not null)
                {
                    return IsCompletedNode(n.Type(), sourceFile);
                }

                // Even though type parameters can be unclosed, we can get away with
                // having at least a closing paren.
                return HasChildOfKind(n, Kind.CloseParenToken, sourceFile);

            case Kind.ModuleDeclaration:
                return n.Body() is not null && IsCompletedNode(n.Body(), sourceFile);

            case Kind.IfStatement:
                if (n.AsIfStatement()!.ElseStatement is not null)
                {
                    return IsCompletedNode(n.AsIfStatement()!.ElseStatement, sourceFile);
                }

                return IsCompletedNode(n.AsIfStatement()!.ThenStatement, sourceFile);

            case Kind.ExpressionStatement:
                return IsCompletedNode(n.Expression(), sourceFile) ||
                    HasChildOfKind(n, Kind.SemicolonToken, sourceFile);

            case Kind.ArrayLiteralExpression:
            case Kind.ArrayBindingPattern:
            case Kind.ElementAccessExpression:
            case Kind.ComputedPropertyName:
            case Kind.TupleType:
                return NodeEndsWith(n, Kind.CloseBracketToken, sourceFile);

            case Kind.IndexSignature:
                if (n.AsIndexSignatureDeclaration()!.Type is not null)
                {
                    return IsCompletedNode(n.AsIndexSignatureDeclaration()!.Type, sourceFile);
                }

                return HasChildOfKind(n, Kind.CloseBracketToken, sourceFile);

            case Kind.CaseClause:
            case Kind.DefaultClause:
                // there is no such thing as terminator token for CaseClause/DefaultClause so for simplicity always consider them non-completed
                return false;

            case Kind.ForStatement:
            case Kind.ForInStatement:
            case Kind.ForOfStatement:
            case Kind.WhileStatement:
                return IsCompletedNode(n.Statement(), sourceFile);

            case Kind.DoStatement:
                // rough approximation: if DoStatement has While keyword - then if node is completed is checking the presence of ')';
                if (HasChildOfKind(n, Kind.WhileKeyword, sourceFile))
                {
                    return NodeEndsWith(n, Kind.CloseParenToken, sourceFile);
                }

                return IsCompletedNode(n.Statement(), sourceFile);

            case Kind.TypeQuery:
                return IsCompletedNode(n.AsTypeQueryNode()!.ExprName, sourceFile);

            case Kind.TypeOfExpression:
            case Kind.DeleteExpression:
            case Kind.VoidExpression:
            case Kind.YieldExpression:
            case Kind.SpreadElement:
                return IsCompletedNode(n.Expression(), sourceFile);

            case Kind.TaggedTemplateExpression:
                return IsCompletedNode(n.AsTaggedTemplateExpression()!.Template, sourceFile);

            case Kind.TemplateExpression:
                if (n.AsTemplateExpression()!.TemplateSpans is null)
                {
                    return false;
                }

                var lastSpan = LastOrNil(n.AsTemplateExpression()!.TemplateSpans!.Nodes);
                return IsCompletedNode(lastSpan, sourceFile);

            case Kind.TemplateSpan:
                return NodeIsPresent(n.AsTemplateSpan()!.Literal);

            case Kind.ExportDeclaration:
            case Kind.ImportDeclaration:
                return NodeIsPresent(n.ModuleSpecifier());

            case Kind.PrefixUnaryExpression:
                return IsCompletedNode(n.AsPrefixUnaryExpression()!.Operand, sourceFile);

            case Kind.BinaryExpression:
                return IsCompletedNode(n.AsBinaryExpression()!.Right, sourceFile);

            case Kind.ConditionalExpression:
                return IsCompletedNode(n.AsConditionalExpression()!.WhenFalse, sourceFile);

            default:
                return true;
        }
    }

    /// <summary>
    /// Checks if node ends with 'expectedLastToken'.
    /// If child at position 'length - 1' is 'SemicolonToken' it is skipped and 'expectedLastToken' is compared with child at position 'length - 2'.
    /// </summary>
    private static bool NodeEndsWith(Node n, Kind expectedLastToken, SourceFile sourceFile)
    {
        var lastChildNode = GetLastVisitedChild(n, sourceFile);
        var lastNodeAndTokens = new List<Node>();
        int tokenStartPos;
        if (lastChildNode is not null)
        {
            lastNodeAndTokens.Add(lastChildNode);
            tokenStartPos = lastChildNode.End();
        }
        else
        {
            tokenStartPos = n.Pos();
        }

        var scan = Scanner.Scanner.GetScannerForSourceFile(sourceFile, tokenStartPos);
        for (var startPos = tokenStartPos; startPos < n.End();)
        {
            var tokenKind = scan.Token();
            var tokenFullStart = scan.TokenFullStart();
            var tokenEnd = scan.TokenEnd();
            var token = sourceFile.GetOrCreateToken(tokenKind, tokenFullStart, tokenEnd, n, scan.TokenFlags);
            lastNodeAndTokens.Add(token);
            startPos = tokenEnd;
            scan.Scan();
        }

        if (lastNodeAndTokens.Count == 0)
        {
            return false;
        }

        var lastChild = lastNodeAndTokens[^1]!;
        if (lastChild.Kind == expectedLastToken)
        {
            return true;
        }
        else if (lastChild.Kind == Kind.SemicolonToken && lastNodeAndTokens.Count > 1)
        {
            return lastNodeAndTokens[^2]!.Kind == expectedLastToken;
        }

        return false;
    }

    private static bool HasChildOfKind(Node containingNode, Kind kind, SourceFile sourceFile)
    {
        return FindChildOfKind(containingNode, kind, sourceFile) is not null;
    }
}
