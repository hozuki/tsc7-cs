// ported from: internal/parser/utilities.go:1-56（语言变体、token 判定、JSDocCommentRanges；W6 分片，整文件）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
//
// 1) 包级函数 → `partial class Parser` 上的 `internal static`（一个 Go package = 一个 C# 类型，docs §2）；
//    名字按 Go 的导出性：导出的（`GetJSDocCommentRanges`）保持原样，未导出的转 PascalCase。
//
// 2) `GetJSDocCommentRanges` 的 Go 形状是"传入可复用的切片、返回同一底层数组的切片"（调用点随后用
//    `p.jsdocCommentRangesSpace = ranges[:0]` 复用容量）。C# 的数组没有 len<cap 视图（docs §3.2），
//    所以入参与返回值都改成 `List<CommentRange>`：传进来的 List 就是返回的 List，调用点的 `[:0]` 用
//    `Clear()` 表达（只在迭代结束后调用，语义与 Go 一致）。
//
// 3) Go 的 `slices.DeleteFunc` → `List.RemoveAll`（都保序、就地删除）；`list != nil` 的 nil 判断改成
//    `list is not null`（Go 的 nil 切片在这里等价于"空列表"）。
//
// 4) docs §3.1：Go 的 `len(text)` 是**字节**长度，C# 用 UTF-16 代码单元长度。`IsJSDocLikeText` 里两者
//    只可能在被检查的前 4 个字符含非 ASCII 时不同，而调用点只传以 ASCII `/**` 开头的注释范围
//    （jsdoc.go:144），故等价。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Parser;

public sealed partial class Parser
{
    /// <summary>Go: <c>func getLanguageVariant(scriptKind core.ScriptKind) core.LanguageVariant</c>。</summary>
    internal static LanguageVariant GetLanguageVariant(ScriptKind scriptKind)
    {
        switch (scriptKind)
        {
            case ScriptKind.TSX:
            case ScriptKind.JSX:
            case ScriptKind.JS:
            case ScriptKind.JSON:
                // .tsx and .jsx files are treated as jsx language variant.
                return LanguageVariant.JSX;
        }

        return LanguageVariant.Standard;
    }

    /// <summary>Go: <c>func tokenIsIdentifierOrKeyword(token ast.Kind) bool</c>。</summary>
    internal static bool TokenIsIdentifierOrKeyword(Kind token) => token >= Kind.Identifier;

    /// <summary>Go: <c>func tokenIsIdentifierOrKeywordOrGreaterThan(token ast.Kind) bool</c>。</summary>
    internal static bool TokenIsIdentifierOrKeywordOrGreaterThan(Kind token) =>
        token == Kind.GreaterThanToken || TokenIsIdentifierOrKeyword(token);

    /// <summary>Go: <c>func GetJSDocCommentRanges(f *ast.NodeFactory, commentRanges []ast.CommentRange, node *ast.Node, text string) []ast.CommentRange</c>。</summary>
    public static List<CommentRange> GetJSDocCommentRanges(
        NodeFactory f,
        List<CommentRange>? commentRanges,
        Node node,
        string text)
    {
        var result = commentRanges ?? [];
        switch (node.Kind)
        {
            case Kind.Parameter:
            case Kind.TypeParameter:
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
            case Kind.ParenthesizedExpression:
            case Kind.VariableDeclaration:
            case Kind.ExportSpecifier:
                foreach (var commentRange in GetTrailingCommentRanges(f, text, node.Pos()))
                {
                    result.Add(commentRange);
                }

                foreach (var commentRange in GetLeadingCommentRanges(f, text, node.Pos()))
                {
                    result.Add(commentRange);
                }

                break;
            default:
                foreach (var commentRange in GetLeadingCommentRanges(f, text, node.Pos()))
                {
                    result.Add(commentRange);
                }

                break;
        }

        // Keep if the comment starts with '/**' but not if it is '/**/'
        // （C# 的 Ast.CommentRange 把 Go 内嵌的 core.TextRange 存成字段 `Range`，故 Pos()/End() 要走 Range。）
        result.RemoveAll(comment =>
        {
            var commentStart = comment.Range.Pos();
            var commentLen = comment.Range.End() - commentStart;
            return comment.Range.End() > node.End() ||
                commentLen < 4 ||
                text[commentStart + 1] != '*' ||
                text[commentStart + 2] != '*' ||
                text[commentStart + 3] == '/';
        });

        return result;
    }

    /// <summary>Go: <c>func isKeywordOrPunctuation(token ast.Kind) bool</c>。</summary>
    internal static bool IsKeywordOrPunctuation(Kind token) => IsKeywordKind(token) || IsPunctuationKind(token);

    /// <summary>Go: <c>func isJSDocLikeText(text string) bool</c>。</summary>
    internal static bool IsJSDocLikeText(string text) =>
        text.Length >= 4 && text[1] == '*' && text[2] == '*' && text[3] != '/';
}
