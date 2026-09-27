// ported from: internal/format/util.go
//
// 与 Go 的差异（C# 语言特性）：
// - 包级函数 → 静态类 `Format`；Go 未导出函数 → internal + PascalCase（`GetLineStartPositionForPosition` 是
//   导出函数，保持 public）。
// - Go 的指针相等判断（`node.TypeParameterList() == list`）在 C# 里是引用相等，语义一致（NodeList 是 class）。
// - `case ast.KindTypeLiteral: return …` 之后的 `return ast.KindUnknown` 保留（Go 的 switch 不需要 fallthrough 的
//   情形在 C# 里逐字对应）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

public static partial class Format
{
    internal static bool RangeIsOnOneLine(TextRange node, SourceFile file)
    {
        var startLine = Scan.GetECMALineOfPosition(file, node.Pos());
        var endLine = Scan.GetECMALineOfPosition(file, node.End());
        return startLine == endLine;
    }

    internal static Kind GetOpenTokenForList(Node node, NodeList? list)
    {
        switch (node.Kind)
        {
            case Kind.Constructor:
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.ArrowFunction:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.FunctionType:
            case Kind.ConstructorType:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                if (ReferenceEquals(node.TypeParameterList(), list))
                {
                    return Kind.LessThanToken;
                }
                else if (ReferenceEquals(node.ParameterList(), list))
                {
                    return Kind.OpenParenToken;
                }

                break;
            case Kind.CallExpression:
            case Kind.NewExpression:
                if (ReferenceEquals(node.TypeArgumentList(), list))
                {
                    return Kind.LessThanToken;
                }
                else if (ReferenceEquals(node.ArgumentList(), list))
                {
                    return Kind.OpenParenToken;
                }

                break;
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.TypeAliasDeclaration:
                if (ReferenceEquals(node.TypeParameterList(), list))
                {
                    return Kind.LessThanToken;
                }

                break;
            case Kind.TypeReference:
            case Kind.TaggedTemplateExpression:
            case Kind.TypeQuery:
            case Kind.ExpressionWithTypeArguments:
            case Kind.ImportType:
                if (ReferenceEquals(node.TypeArgumentList(), list))
                {
                    return Kind.LessThanToken;
                }

                break;
            case Kind.TypeLiteral:
                return Kind.OpenBraceToken;
        }

        return Kind.Unknown;
    }

    internal static Kind GetCloseTokenForOpenToken(Kind kind)
    {
        // TODO: matches strada - seems like it could handle more pairs of braces, though? [] notably missing
        switch (kind)
        {
            case Kind.OpenParenToken:
                return Kind.CloseParenToken;
            case Kind.LessThanToken:
                return Kind.GreaterThanToken;
            case Kind.OpenBraceToken:
                return Kind.CloseBraceToken;
        }

        return Kind.Unknown;
    }

    public static int GetLineStartPositionForPosition(int position, SourceFile sourceFile)
    {
        var lineStarts = Scan.GetECMALineStarts(sourceFile);
        var line = Scan.GetECMALineOfPosition(sourceFile, position);
        return lineStarts[line];
    }

    /// <summary>
    /// Validating `expectedTokenKind` ensures the token was typed in the context we expect (eg: not a comment).
    /// </summary>
    /// <param name="expectedTokenKind">The kind of the last token constituting the desired parent node.</param>
    internal static Node? FindImmediatelyPrecedingTokenOfKind(int end, Kind expectedTokenKind, SourceFile sourceFile)
    {
        var precedingToken = AstNav.AstNav.FindPrecedingToken(sourceFile, end);
        if (precedingToken is null || precedingToken.Kind != expectedTokenKind || precedingToken.End() != end)
        {
            return null;
        }

        return precedingToken;
    }

    /// <summary>
    /// Finds the highest node enclosing `node` at the same list level as `node`
    /// and whose end does not exceed `node.end`.
    ///
    /// <para>Consider typing the following</para>
    /// <code>
    /// let x = 1;
    /// while (true) {
    /// }
    /// </code>
    /// <para>Upon typing the closing curly, we want to format the entire `while`-statement, but not the preceding
    /// variable declaration.</para>
    /// </summary>
    internal static Node? FindOutermostNodeWithinListLevel(Node? node)
    {
        var current = node;
        while (current is not null &&
            current.Parent is not null &&
            current.Parent.End() == node!.End() &&
            !IsListElement(current.Parent, current))
        {
            current = current.Parent;
        }

        return current;
    }

    // Returns true if node is a element in some list in parent
    // i.e. parent is class declaration with the list of members and node is one of members.
    internal static bool IsListElement(Node parent, Node node)
    {
        switch (parent.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
                return node.Loc.ContainedBy(parent.MemberList()!.Loc);
            case Kind.ModuleDeclaration:
                var body = parent.Body();
                return body is not null && body.Kind == Kind.ModuleBlock && node.Loc.ContainedBy(body.StatementList()!.Loc);
            case Kind.SourceFile:
            case Kind.Block:
            case Kind.ModuleBlock:
                return node.Loc.ContainedBy(parent.StatementList()!.Loc);
            case Kind.CatchClause:
                return node.Loc.ContainedBy(parent.AsCatchClause()!.Block!.StatementList()!.Loc);
        }

        return false;
    }

    internal static bool IsMemberListElement(Node parent, Node node)
    {
        switch (parent.Kind)
        {
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
            case Kind.TypeLiteral:
            case Kind.MappedType:
                return node.Loc.ContainedBy(parent.MemberList()!.Loc);
        }

        return false;
    }
}
