// ported from: internal/scanner/utilities.go（146 行，scanner 包的工具函数）
//
// 本文件是该文件的完整移植：12 个包级函数一一对应，顺序与 Go 一致（每个成员上方的
// `/// <summary>Go: <c>func …</c>。</summary>` 给出 Go 原文签名与行号，可与 Go 源并排阅读）。
// Go 包级未导出函数 → `internal static` 的 PascalCase 成员（tokenIsIdentifierOrKeyword →
// TokenIsIdentifierOrKeyword）；导出函数 → `public static` 且名字原样保留（IdentifierToKeywordKind…）。
// 所有成员都落在 `Scanner` 这个 partial 类上（Go 的 `scanner` 包 = 这个类，见 Scanner.cs 的文件头）。
//
// 与 Go 的偏离（都是 C# 语言特性，逐条依据 docs/architecture.md §2/§3 与 docs/ast.md）：
// 1) 下标一律是 UTF-16 代码单元（docs §3.1）：Go 的 `utf8.DecodeRuneInString` → Scanner.DecodeRuneAt
//    （返回码点 + size，size 是代码单元数），`len(name)` → `name.Length`。IsIdentifierText 的推进逻辑
//    因此逐字照抄（只是 size 的含义从"字节数"变成"代码单元数"）。
// 2) `strings.TrimLeftFunc`/`strings.TrimRightFunc` 在 StringUtil 里没有对应物（StringUtil 只有按码点的
//    判定函数 IsWhiteSpaceLike/IsLineBreak），这里写成按代码单元下标的私有 helper 或内联 while 循环；
//    谓词集合与 Go 完全一致，行为等价（见 TrimLeftWhiteSpaceLike 的注释）。
// 3) Go 的 `strings.Builder` → StringBuilder（`new StringBuilder(text.Length)` 复刻 `result.Grow(len(text))`）；
//    `strings.ContainsRune(name, '-')` → `name.Contains('-')`。
// 4) Go 的切片 `text[a:b]` → C# 的 range 下标 `text[a..b]`：Go 返回零拷贝子切片，C# 这里会新建字符串。
//    这些点只出现在 JSDoc 文本提取路径（非 scanner 热路径），且函数返回类型就是 string（span 用不了）。
// 5) `map[string]ast.Kind` 未命中在 Go 里返回零值 ast.KindUnknown → C# 用 TryGetValue 后取 Kind.Unknown，
//    绝不用会抛异常的索引器（`textToKeyword` 见 Scanner 的 `_textToKeyword`）。
// 6) `strings.TrimRightFunc(b.String(), unicode.IsSpace)` → 手写的 `char.IsWhiteSpace` 反向裁剪：
//    .NET 的 char.IsWhiteSpace 与 Go 的 unicode.IsSpace 在 BMP 上是同一集合（Go 额外特判 0x85/0xA0，
//    .NET 的 CharUnicodeInfo 同样把它们算作空白；两者都不含 0xFEFF）。
// 7) `debug.FailBadSyntaxKind` 在 C# 里抛 DebugFailureException（docs §3.2），而不是 panic 之后的不可达返回；
//    语句顺序照 Go 保留（失败分支后面仍是 `return text`，与 Go 的 unreachable return 对应）。
// 8) `core.ComputeECMALineStarts` 返回 ECMALineStarts 结构体（有 Length 与下标器），`len(lineStarts)` → Length，
//    `int(lineStarts[i+1])` 的显式转换由隐式 operator 承担。
// 9) Go 的 `ast.IsXxx(...)` 不能写成 `Ast.IsXxx(...)`：本文件在 `TypeScript.Scanner` 下、usings 写在
//    namespace 之前（与 Core/TSPath/StringUtil 的写法一致），此时裸名 `Ast` 会先解析成外层命名空间
//    `TypeScript.Ast` 而不是其中的静态类（docs §2"外层命名空间里同名的命名空间会先被找到"；用探针
//    artifacts 级别的临时工程实测是 CS0234）。因此按仓库惯例 `using static TypeScript.Ast.Ast;`，
//    调用点写成裸函数名（`NodeIsMissing(node)`、`IsTypeNode(current)`…），与 Go 的 `ast.NodeIsMissing` 一一对应。

using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Scanner;

public sealed partial class Scanner
{
    /// <summary>Go: <c>func tokenIsIdentifierOrKeyword(token ast.Kind) bool</c>（Go:14-16）。</summary>
    internal static bool TokenIsIdentifierOrKeyword(Kind token) => token >= Kind.Identifier;

    /// <summary>Go: <c>func IdentifierToKeywordKind(node *ast.Identifier) ast.Kind</c>（Go:18-20）。</summary>
    // Go 的 map 未命中返回零值 ast.KindUnknown（= 0），所以这里不能用会抛 KeyNotFoundException 的索引器。
    public static Kind IdentifierToKeywordKind(Identifier node) => _textToKeyword.TryGetValue(node.Text, out var kind) ? kind : Kind.Unknown;

    /// <summary>Go: <c>func GetSourceTextOfNodeFromSourceFile(sourceFile *ast.SourceFile, node *ast.Node, includeTrivia bool) string</c>（Go:22-24）。</summary>
    public static string GetSourceTextOfNodeFromSourceFile(SourceFile sourceFile, Node node, bool includeTrivia)
    {
        return GetTextOfNodeFromSourceText(sourceFile.Text(), node, includeTrivia);
    }

    /// <summary>Go: <c>func isJSDocTypeExpressionOrChild(node *ast.Node) bool</c>（Go:26-39）。</summary>
    internal static bool IsJSDocTypeExpressionOrChild(Node node)
    {
        if (IsJSDocTypeExpression(node))
        {
            return true;
        }

        if ((node.Flags & (NodeFlags.JSDoc | NodeFlags.Reparsed)) == 0)
        {
            return false;
        }

        // Go 的 for 循环把 current 从头节点一路走到 nil；Go 的 `current.Parent` 是 *Node（nil 即根）。
        for (Node? current = node; current is not null; current = current.Parent)
        {
            if (IsTypeNode(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func normalizeJSDocTypeSourceText(text string) string</c>（Go:41-62）。</summary>
    internal static string NormalizeJSDocTypeSourceText(string text)
    {
        var lineStarts = ComputeECMALineStarts(text);
        if (lineStarts.Length == 1)
        {
            return StripLeadingJSDocComment(text);
        }

        var result = new StringBuilder(text.Length); // Go: result.Grow(len(text))
        var newLine = NewLineKind.LF.GetNewLineCharacter();
        for (var i = 0; i < lineStarts.Length; i++)
        {
            if (i > 0)
            {
                result.Append(newLine);
            }

            var lineStart = (int)lineStarts[i];
            var lineEnd = text.Length;
            if (i + 1 < lineStarts.Length)
            {
                lineEnd = lineStarts[i + 1];
            }

            // Go: strings.TrimRightFunc(text[lineStart:lineEnd], stringutil.IsLineBreak)——按代码单元从右裁掉换行符，
            // 只裁 '\n'/'\r'/U+2028/U+2029（不能用 .NET 的 TrimEnd()，它按 char.IsWhiteSpace 裁剪，集合不同）。
            while (lineEnd > lineStart && IsLineBreak(text[lineEnd - 1]))
            {
                lineEnd--;
            }

            result.Append(StripLeadingJSDocComment(text[lineStart..lineEnd]));
        }

        return result.ToString();
    }

    /// <summary>Go: <c>func stripLeadingJSDocComment(line string) string</c>（Go:64-70）。</summary>
    internal static string StripLeadingJSDocComment(string line)
    {
        line = TrimLeftWhiteSpaceLike(line);
        if (line.Length > 0 && line[0] == '*')
        {
            line = line[1..]; // Go: line[1:]
        }

        return TrimLeftWhiteSpaceLike(line);
    }

    /// <summary>Go: <c>func GetTextOfNodeFromSourceText(sourceText string, node *ast.Node, includeTrivia bool) string</c>（Go:72-100）。</summary>
    public static string GetTextOfNodeFromSourceText(string sourceText, Node node, bool includeTrivia)
    {
        if (NodeIsMissing(node))
        {
            return "";
        }

        var pos = node.Pos();
        if (!includeTrivia)
        {
            pos = SkipTrivia(sourceText, pos);
        }

        var text = sourceText[pos..node.End()]; // Go: sourceText[pos:node.End()]
        if (IsJSDocTypeExpressionOrChild(node))
        {
            text = NormalizeJSDocTypeSourceText(text);
        }

        if ((node.Flags & NodeFlags.ReparserTransformedLiteral) != 0)
        {
            // This is similar to `getLiteralTextOfNode` in the printer, but without the context of an `emitContext` to provide overrides
            if (IsStringLiteral(node))
            {
                if ((node.AsStringLiteral()!.TokenFlags & TokenFlags.SingleQuote) != 0)
                {
                    return "'" + text + "'";
                }

                return "\"" + text + "\"";
            }
            else if (IsIdentifier(node))
            {
                return node.Text();
            }

            // Only the above node kinds are currently transformed into one another by the reparser, requiring the textual remapping.
            // (Any reamppings done by emit transforms are handled by `getLiteralTextOfNode` in the printer)
            // Fail on any other kinds.
            Debug.FailBadSyntaxKind(node, "Unexpected reparser-transformed node kind");
        }

        return text;
    }

    /// <summary>Go: <c>func GetTextOfNode(node *ast.Node) string</c>（Go:102-104）。</summary>
    public static string GetTextOfNode(Node node)
    {
        // Go 里 node 不在任何 SourceFile 里时 GetSourceFileOfNode 返回 nil，紧接着的 sourceFile.Text() 会 panic；
        // C# 侧用 `!` 表达同一契约（越界使用即 NRE），docs §3.2 的 nil → NRE 处理方式。
        return GetSourceTextOfNodeFromSourceFile(GetSourceFileOfNode(node)!, node, false /*includeTrivia*/);
    }

    /// <summary>Go: <c>func GetTextOfJSDocComment(comment *ast.NodeList) string</c>（Go:106-120）。</summary>
    public static string GetTextOfJSDocComment(NodeList? comment)
    {
        if (comment is null)
        {
            return "";
        }

        var b = new StringBuilder();
        foreach (var n in comment.Nodes ?? []) // Go 的 range 覆盖 nil 切片 = 零次迭代
        {
            switch (n.Kind)
            {
                case Kind.JSDocText:
                    b.Append(n.Text());
                    break;
                case Kind.JSDocLink:
                case Kind.JSDocLinkCode:
                case Kind.JSDocLinkPlain:
                    b.Append(GetTextOfNode(n));
                    break;
            }
        }

        // Go: strings.TrimRightFunc(b.String(), unicode.IsSpace)——手写的 code unit 反向裁剪（见文件头第 6 条）。
        var end = b.Length;
        while (end > 0 && char.IsWhiteSpace(b[end - 1]))
        {
            end--;
        }

        return end == b.Length ? b.ToString() : b.ToString(0, end);
    }

    /// <summary>Go: <c>func DeclarationNameToString(name *ast.Node) string</c>（Go:122-127）。</summary>
    public static string DeclarationNameToString(Node? name)
    {
        if (name is null || name.Pos() == name.End())
        {
            return "(Missing)";
        }

        return GetTextOfNode(name);
    }

    /// <summary>Go: <c>func IsIdentifierText(name string, languageVariant core.LanguageVariant) bool</c>（Go:129-142）。</summary>
    public static bool IsIdentifierText(string name, LanguageVariant languageVariant)
    {
        var ch = DecodeRuneAt(name, 0, out var size);
        if (!IsIdentifierStart(ch))
        {
            return false;
        }

        for (var i = size; i < name.Length;)
        {
            ch = DecodeRuneAt(name, i, out size);
            if (!IsIdentifierPartEx(ch, languageVariant))
            {
                return false;
            }

            i += size;
        }

        return true;
    }

    /// <summary>Go: <c>func IsIntrinsicJsxName(name string) bool</c>（Go:144-146）。</summary>
    public static bool IsIntrinsicJsxName(string name)
    {
        return name.Length != 0 && (name[0] >= 'a' && name[0] <= 'z' || name.Contains('-'));
    }

    /// <summary>Go: <c>strings.TrimLeftFunc(line, stringutil.IsWhiteSpaceLike)</c>（Go:65/69）的 C# 版本。</summary>
    /// <remarks>
    /// StringUtil 里没有 TrimLeftFunc（它只提供按码点的判定），所以这里用下标循环自己裁。
    /// IsWhiteSpaceLike(ch) = IsWhiteSpaceSingleLine(ch) || IsLineBreak(ch)，与 Go 的谓词逐字一致；
    /// 逐 <c>char</c> 判定与逐 rune 判定等价：这两个集合只含 BMP 字符，且孤立代理项不是空白。
    /// </remarks>
    private static string TrimLeftWhiteSpaceLike(string line)
    {
        var start = 0;
        var end = line.Length;
        while (start < end && IsWhiteSpaceLike(line[start]))
        {
            start++;
        }

        return start == 0 ? line : line[start..];
    }
}
