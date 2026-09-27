// ported from: internal/ls/lsutil/asi.go
//
// **本文件是 `internal/ls/lsutil` 的局部移植**（依赖闭包，见 FormatCodeOptions.cs 的文件头）。
//
// 与 Go 的差异（C# 语言特性）：
// - 包级函数 → 静态类 `LsUtil`（§2）。
// - Go 的 `func(ancestor *ast.Node) ast.FindAncestorResult` 内联函数 → C# 的 `Func<Node, FindAncestorResult>`。
// - `ast.IsModuleBlock`/`ast.IsFunctionBlock` 在 C# 生成物里接受**非空** `Node`（Go 是 `*Node` 且 nil 安全），
//   调用点先判空再调用，语义不变。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.LsUtil;

public static partial class LsUtil
{
    /// <summary>Go 的 <c>func PositionIsASICandidate(pos int, context *ast.Node, file *ast.SourceFile) bool</c>。</summary>
    public static bool PositionIsASICandidate(int pos, Node? context, SourceFile file)
    {
        var contextAncestor = FindAncestorOrQuit(context, ancestor =>
        {
            if (ancestor.End() != pos)
            {
                return FindAncestorResult.Quit;
            }

            return ToFindAncestorResult(SyntaxMayBeASICandidate(ancestor.Kind));
        });

        return contextAncestor is not null && NodeIsASICandidate(contextAncestor, file);
    }

    public static bool SyntaxMayBeASICandidate(Kind kind)
    {
        return SyntaxRequiresTrailingCommaOrSemicolonOrASI(kind) ||
            SyntaxRequiresTrailingFunctionBlockOrSemicolonOrASI(kind) ||
            SyntaxRequiresTrailingModuleBlockOrSemicolonOrASI(kind) ||
            SyntaxRequiresTrailingSemicolonOrASI(kind);
    }

    public static bool SyntaxRequiresTrailingCommaOrSemicolonOrASI(Kind kind)
    {
        return kind == Kind.CallSignature ||
            kind == Kind.ConstructSignature ||
            kind == Kind.IndexSignature ||
            kind == Kind.PropertySignature ||
            kind == Kind.MethodSignature;
    }

    public static bool SyntaxRequiresTrailingFunctionBlockOrSemicolonOrASI(Kind kind)
    {
        return kind == Kind.FunctionDeclaration ||
            kind == Kind.Constructor ||
            kind == Kind.MethodDeclaration ||
            kind == Kind.GetAccessor ||
            kind == Kind.SetAccessor;
    }

    public static bool SyntaxRequiresTrailingModuleBlockOrSemicolonOrASI(Kind kind)
    {
        return kind == Kind.ModuleDeclaration;
    }

    public static bool SyntaxRequiresTrailingSemicolonOrASI(Kind kind)
    {
        return kind == Kind.VariableStatement ||
            kind == Kind.ExpressionStatement ||
            kind == Kind.DoStatement ||
            kind == Kind.ContinueStatement ||
            kind == Kind.BreakStatement ||
            kind == Kind.ReturnStatement ||
            kind == Kind.ThrowStatement ||
            kind == Kind.DebuggerStatement ||
            kind == Kind.PropertyDeclaration ||
            kind == Kind.TypeAliasDeclaration ||
            kind == Kind.ImportDeclaration ||
            kind == Kind.ImportEqualsDeclaration ||
            kind == Kind.ExportDeclaration ||
            kind == Kind.NamespaceExportDeclaration ||
            kind == Kind.ExportAssignment;
    }

    public static bool NodeIsASICandidate(Node node, SourceFile file)
    {
        var lastToken = GetLastToken(node, file);
        if (lastToken is not null && lastToken.Kind == Kind.SemicolonToken)
        {
            return false;
        }

        if (SyntaxRequiresTrailingCommaOrSemicolonOrASI(node.Kind))
        {
            if (lastToken is not null && lastToken.Kind == Kind.CommaToken)
            {
                return false;
            }
        }
        else if (SyntaxRequiresTrailingModuleBlockOrSemicolonOrASI(node.Kind))
        {
            var lastChild = GetLastChild(node, file);
            if (lastChild is not null && IsModuleBlock(lastChild))
            {
                return false;
            }
        }
        else if (SyntaxRequiresTrailingFunctionBlockOrSemicolonOrASI(node.Kind))
        {
            var lastChild = GetLastChild(node, file);
            if (lastChild is not null && IsFunctionBlock(lastChild))
            {
                return false;
            }
        }
        else if (!SyntaxRequiresTrailingSemicolonOrASI(node.Kind))
        {
            return false;
        }

        // See comment in parser's `parseDoStatement`
        if (node.Kind == Kind.DoStatement)
        {
            return true;
        }

        var topNode = FindAncestor(node, ancestor => ancestor.Parent is null);
        var nextToken = AstNav.AstNav.FindNextToken(node, topNode!, file);
        if (nextToken is null || nextToken.Kind == Kind.CloseBraceToken)
        {
            return true;
        }

        var startLine = GetECMALineOfPosition(file, node.End());
        var endLine = GetECMALineOfPosition(file, AstNav.AstNav.GetStartOfNode(nextToken, file, false /*includeJSDoc*/));
        return startLine != endLine;
    }
}
