// ported from: internal/ls/jsdoc_snippet.go:1-597
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 未导出类型 `docCommentTemplate` / `commentOwnerInfo` → `internal sealed class DocCommentTemplate` /
//    `CommentOwnerInfo`（字段按 G1 约定：Go 未导出字段 → `_camelCase`）。
//    ⚠ `commentOwnerInfo.parameters` 的 Go 类型是 `[]*ast.ParameterDeclarationNode`，C# 侧 `Node.Parameters()`
//    返回 `Node[]?` ⇒ 本类字段是 `Node[]?`（`parameterDocComments` 只用到 `Name()` 与 `AsParameterDeclaration()`，
//    与 Go 调用点一致）。
//
// 2) 包级未导出函数 → 静态类 `Ls` 的 `internal static` + PascalCase（G1 约定 §0，方法不加 `_`）；
//    `*LanguageService` 的方法（`getJSDocSnippetCompletion` / `getJSDocSnippetCompletionRange`）→ `LanguageService`
//    实例成员（PascalCase）。
//
// 3) Go 的 `(T, bool)` 返回值 → C# 元组 `(T …, bool …)`；Go 的 `(int, bool)` 出参写法 `if x, ok := f(); ok` →
//    `var (x, ok) = f(); if (ok)`。Go 的指针式累加器 `snippetIndex *int` → C# `ref int snippetIndex`
//    （`transformJSDocTemplateLines` / `transformJSDocParamLine` / `transformJSDocReturnsLine` 三个函数）。
//
// 4) ⚠ **跨切片依赖（已冻结，G8 落盘；本文件不重复声明）**：
//    `Ls.GetLineEndOfPosition(SourceFile, int)`（completions.go:3033）、`Ls.EscapeSnippetText(string)`
//    （completions.go:3734）、`Ls.ClientSupportsItemSnippet/CommitCharacters/InsertReplace(Context)`
//    （completions.go:5427-5438）、以及**类型** `Ls.CompletionItem` / `Ls.CompletionList`（completions.go:110-116）。
//    Go 的嵌入 `*lsproto.CompletionItem` → C# 组合字段 `_completionItem`（本仓先例：Execute/Tsc/ExtendedConfigCache.cs
//    的 `_entry`，原因同为"被嵌入的 LsProto 类型是 sealed ⇒ 不能继承"）；`Items` 用集合表达式构造，数组/List 皆可。
//
// 5) `strings.Split/Join` → `string.Split/string.Join`；`strings.TrimLeft(s, " \t")` → `s.TrimStart(' ', '\t')`；
//    `strings.HasPrefix/HasSuffix` → `StartsWith/EndsWith(…, StringComparison.Ordinal)`；`strings.IndexByte(rest, '}')`
//    → `rest.IndexOf('}')`。`strings.Builder` → `StringBuilder`（`b.Len()` → `b.Length`）。
//
// 6) `utf8/`stringutil 的字符推进：Go 用 `stringutil.DecodeJSStringRune`（在 UTF-8 上按字节推进）；C# 的 string 是
//    UTF-16 单元序列，本仓既有等价物是 `Scanner.DecodeRuneAt(text, index, out size)`（代码单元数，先例
//    Format/Indent.cs、Format/Api.cs）⇒ 本文件统一用 `Scan.DecodeRuneAt`（`using Scan = TypeScript.Scanner.Scanner;`）。
//
// 7) `fmt.Sprintf("param%d", i)` / `{${%d:*}}` 这类模板 → C# 字符串拼接（避免在插值串里转义 `$`/`{}`）。
//
// 8) `ast.FindAncestor(tokenAtPos, ast.IsJSDoc)` → `FindAncestor(tokenAtPos, IsJSDoc)`；`astnav.GetTokenAtPosition`
//    在 C# 声明为非空返回，但 Go 侧可能返回 nil ⇒ 用 `Node?` 接收后照旧判空。
//
// 9) `parser.ParseSourceFile(...)` → `using static TypeScript.Parser.Parser;` 后裸名 `ParseSourceFile(...)`
//    （`Parser` 既是命名空间又是类，不能做限定名 —— 同 G1 §4.1 的 Ast/Collections 一类）。
//
// 10) `diagnostics.JSDoc_comment.Localize(locale.FromContext(ctx))` → `JSDoc_comment.Localize(Locale.FromContext(ctx))`
//     （`using static TypeScript.Diagnostics.Diagnostics;` + `using TypeScript.Locale;`）。
using System;
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Locale;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Format.Format;
using static TypeScript.Parser.Parser;
// 同名类型消歧（`using System` / `using TypeScript.LsProto`；先例 Ls/Utilities.cs）：
using Kind = TypeScript.Ast.Kind;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Ls;

internal sealed class DocCommentTemplate
{
    internal string _newText = "";
}

internal sealed class CommentOwnerInfo
{
    internal Node? _commentOwner;
    internal Node[]? _parameters;
    internal bool _hasReturn;
}

public static partial class Ls
{
    internal static bool IsPotentiallyValidJSDocSnippetCompletionPosition(SourceFile file, int position)
    {
        var text = file.Text();
        var lineStart = GetLineStartPositionForPosition(position, file);
        var prefix = text[lineStart..position];
        if (!IsJSDocSnippetPrefix(prefix))
        {
            return false;
        }

        var lineEnd = GetLineEndOfPosition(file, position);
        var suffix = text[position..lineEnd];
        return IsJSDocSnippetSuffix(suffix);
    }

    internal static DocCommentTemplate? GetDocCommentTemplateAtPosition(SourceFile sourceFile, int position, bool generateReturnInDocTemplate, string newLine)
    {
        Node? tokenAtPos = GetTokenAtPosition(sourceFile, position);
        if (tokenAtPos is null)
        {
            return null;
        }

        var existingDocComment = FindAncestor(tokenAtPos, IsJSDoc);
        var (docCommentEnd, hasDocCommentAtPosition, hasClosingDocCommentAtPosition) = GetDocCommentEndAtPosition(sourceFile, position);
        var isInEmptyDocComment = existingDocComment is not null || hasDocCommentAtPosition;
        if (IsNonEmptyJSDoc(existingDocComment) && hasDocCommentAtPosition && !hasClosingDocCommentAtPosition)
        {
            var reparseText = sourceFile.Text()[..position] + " */" + sourceFile.Text()[position..];
            var reparse = ParseSourceFile(sourceFile.ParseOptions(), reparseText, sourceFile.ScriptKind);
            return GetDocCommentTemplateAtPosition(reparse, position, generateReturnInDocTemplate, newLine);
        }
        if (IsNonEmptyJSDoc(existingDocComment))
        {
            return null;
        }
        if (existingDocComment is null && hasDocCommentAtPosition)
        {
            tokenAtPos = GetTokenAtPosition(sourceFile, SkipWhitespace(sourceFile.Text(), docCommentEnd));
            if (tokenAtPos is null)
            {
                return null;
            }
        }
        var tokenStart = GetStartOfNode(tokenAtPos!, sourceFile, false /*includeJSDoc*/);
        if (!isInEmptyDocComment && tokenStart < position)
        {
            return null;
        }

        var commentOwnerInfo = GetCommentOwnerInfo(tokenAtPos!, generateReturnInDocTemplate);
        if (commentOwnerInfo is null)
        {
            return null;
        }

        var commentOwner = commentOwnerInfo._commentOwner!;
        var lastJSDoc = LastOrNil(commentOwner.JSDoc(sourceFile));
        if (GetStartOfNode(commentOwner, sourceFile, false /*includeJSDoc*/) < position ||
            (lastJSDoc is not null && existingDocComment is not null && lastJSDoc != existingDocComment))
        {
            return null;
        }

        var indentation = GetIndentationStringAtPosition(sourceFile, position);
        var tags = ParameterDocComments(commentOwnerInfo._parameters, IsSourceFileJS(sourceFile), indentation, newLine);
        if (commentOwnerInfo._hasReturn)
        {
            tags += ReturnsDocComment(indentation, newLine);
        }

        if (tags != "" && !HasJSDocTags(commentOwner, sourceFile))
        {
            var preamble = "/**" + newLine + indentation + " * ";
            var endLine = "";
            if (tokenStart == position)
            {
                endLine = newLine + indentation;
            }
            return new DocCommentTemplate { _newText = preamble + newLine + tags + indentation + " */" + endLine };
        }
        return new DocCommentTemplate { _newText = "/** */" };
    }

    internal static (int End, bool Ok, bool HasClosing) GetDocCommentEndAtPosition(SourceFile file, int position)
    {
        var text = file.Text();
        var lineStart = GetLineStartPositionForPosition(position, file);
        var lineEnd = GetLineEndOfPosition(file, position);
        var prefix = text[lineStart..position];
        var suffix = text[position..lineEnd];
        if (!TrimRightSingleLineWhitespace(prefix).EndsWith("/**", StringComparison.Ordinal))
        {
            return (0, false, false);
        }
        var (suffixEnd, hasClosing) = GetJSDocSnippetSuffixEnd(suffix);
        return (position + suffixEnd, true, hasClosing);
    }

    internal static int SkipWhitespace(string text, int position)
    {
        while (position < text.Length)
        {
            var ch = Scan.DecodeRuneAt(text, position, out var size);
            if (size == 0)
            {
                break;
            }
            if (!StringUtil.StringUtil.IsWhiteSpaceLike(ch))
            {
                break;
            }
            position += size;
        }
        return position;
    }

    internal static CommentOwnerInfo? GetCommentOwnerInfo(Node tokenAtPos, bool generateReturnInDocTemplate)
    {
        for (Node? node = tokenAtPos; node is not null; node = node.Parent)
        {
            var (info, quit) = GetCommentOwnerInfoWorker(node, generateReturnInDocTemplate);
            if (info is not null || quit)
            {
                return info;
            }
        }
        return null;
    }

    internal static (CommentOwnerInfo? Info, bool Quit) GetCommentOwnerInfoWorker(Node? commentOwner, bool generateReturnInDocTemplate)
    {
        if (commentOwner is null)
        {
            return (null, false);
        }
        switch (commentOwner.Kind)
        {
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.MethodDeclaration:
            case Kind.Constructor:
            case Kind.MethodSignature:
            case Kind.ArrowFunction:
                return (new CommentOwnerInfo { _commentOwner = commentOwner, _parameters = commentOwner.Parameters(), _hasReturn = HasReturn(commentOwner, generateReturnInDocTemplate) }, false);
            case Kind.PropertyAssignment:
                return GetCommentOwnerInfoWorker(commentOwner.AsPropertyAssignment()!.Initializer, generateReturnInDocTemplate);
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
            case Kind.EnumMember:
            case Kind.TypeAliasDeclaration:
                return (new CommentOwnerInfo { _commentOwner = commentOwner }, false);
            case Kind.PropertySignature:
                var typeNode = commentOwner.AsPropertySignatureDeclaration()!.Type;
                if (typeNode is not null && IsFunctionTypeNode(typeNode))
                {
                    return (new CommentOwnerInfo { _commentOwner = commentOwner, _parameters = typeNode.Parameters(), _hasReturn = HasReturn(typeNode, generateReturnInDocTemplate) }, false);
                }
                return (new CommentOwnerInfo { _commentOwner = commentOwner }, false);
            case Kind.VariableStatement:
                var declarations = commentOwner!.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes ?? [];
                if (declarations.Length == 1)
                {
                    var initializer = declarations[0].AsVariableDeclaration()!.Initializer;
                    if (initializer is not null)
                    {
                        var host = GetRightHandSideOfAssignment(initializer);
                        if (host is not null)
                        {
                            return (new CommentOwnerInfo { _commentOwner = commentOwner, _parameters = host.Parameters(), _hasReturn = HasReturn(host, generateReturnInDocTemplate) }, false);
                        }
                    }
                }
                return (new CommentOwnerInfo { _commentOwner = commentOwner }, false);
            case Kind.SourceFile:
                return (null, true);
            case Kind.ModuleDeclaration:
                if (commentOwner.Parent!.Kind == Kind.ModuleDeclaration)
                {
                    return (null, false);
                }
                return (new CommentOwnerInfo { _commentOwner = commentOwner }, false);
            case Kind.ExpressionStatement:
                return GetCommentOwnerInfoWorker(commentOwner.AsExpressionStatement()!.Expression, generateReturnInDocTemplate);
            case Kind.BinaryExpression:
                var binaryExpression = commentOwner.AsBinaryExpression()!;
                if (GetAssignmentDeclarationKind(commentOwner) == JSDeclarationKind.None)
                {
                    return (null, true);
                }
                if (IsFunctionLike(binaryExpression.Right!))
                {
                    return (new CommentOwnerInfo { _commentOwner = commentOwner, _parameters = binaryExpression.Right!.Parameters(), _hasReturn = HasReturn(binaryExpression.Right, generateReturnInDocTemplate) }, false);
                }
                return (new CommentOwnerInfo { _commentOwner = commentOwner }, false);
            case Kind.PropertyDeclaration:
                var propertyInitializer = commentOwner.AsPropertyDeclaration()!.Initializer;
                if (propertyInitializer is not null && IsFunctionExpressionOrArrowFunction(propertyInitializer))
                {
                    return (new CommentOwnerInfo { _commentOwner = commentOwner, _parameters = propertyInitializer.Parameters(), _hasReturn = HasReturn(propertyInitializer, generateReturnInDocTemplate) }, false);
                }
                break;
        }
        return (null, false);
    }

    internal static bool HasReturn(Node node, bool generateReturnInDocTemplate)
    {
        if (!generateReturnInDocTemplate)
        {
            return false;
        }
        if (IsFunctionTypeNode(node))
        {
            return true;
        }
        if (IsArrowFunction(node))
        {
            var body = node.Body();
            if (body is not null && IsExpression(body))
            {
                return true;
            }
        }
        return IsFunctionLikeDeclaration(node) && node.Body() is not null && IsBlock(node.Body()!) && ForEachReturnStatement(node.Body(), _ => true);
    }

    internal static Node? GetRightHandSideOfAssignment(Node? rightHandSide)
    {
        if (rightHandSide is null)
        {
            return null;
        }
        while (rightHandSide.Kind == Kind.ParenthesizedExpression)
        {
            rightHandSide = rightHandSide.AsParenthesizedExpression()!.Expression!;
        }
        switch (rightHandSide.Kind)
        {
            case Kind.FunctionExpression:
            case Kind.ArrowFunction:
                return rightHandSide;
            case Kind.ClassExpression:
                return Find(rightHandSide.Members(), IsConstructorDeclaration);
            default:
                return null;
        }
    }

    internal static string ParameterDocComments(Node[]? parameters, bool isJavaScriptFile, string indentation, string newLine)
    {
        var b = new StringBuilder();
        var list = parameters ?? [];
        for (var i = 0; i < list.Length; i++)
        {
            var parameter = list[i];
            var paramName = "param" + i;
            if (IsIdentifier(parameter.Name()!))
            {
                paramName = parameter.Name()!.Text();
            }
            var paramType = "";
            if (isJavaScriptFile)
            {
                if (parameter.AsParameterDeclaration()!.DotDotDotToken is not null)
                {
                    paramType = "{...any} ";
                }
                else
                {
                    paramType = "{any} ";
                }
            }
            b.Append(indentation);
            b.Append(" * @param ");
            b.Append(paramType);
            b.Append(paramName);
            b.Append(newLine);
        }
        return b.ToString();
    }

    internal static string ReturnsDocComment(string indentation, string newLine)
    {
        return indentation + " * @returns" + newLine;
    }

    internal static string GetIndentationStringAtPosition(SourceFile sourceFile, int position)
    {
        var text = sourceFile.Text();
        var lineStart = GetLineStartPositionForPosition(position, sourceFile);
        var pos = lineStart;
        while (pos < position)
        {
            var ch = Scan.DecodeRuneAt(text, pos, out var size);
            if (size == 0)
            {
                break;
            }
            if (!StringUtil.StringUtil.IsWhiteSpaceSingleLine(ch))
            {
                break;
            }
            pos += size;
        }
        return text[lineStart..pos];
    }

    internal static bool IsNonEmptyJSDoc(Node? jsdoc)
    {
        if (jsdoc is null)
        {
            return false;
        }
        var data = jsdoc.AsJSDoc()!;
        return (data.Comment is not null && (data.Comment.Nodes?.Length ?? 0) > 0) || (data.Tags is not null && (data.Tags.Nodes?.Length ?? 0) > 0);
    }

    internal static bool HasJSDocTags(Node node, SourceFile file)
    {
        var jsdocs = node.JSDoc(file);
        if (jsdocs is null || jsdocs.Length == 0)
        {
            return false;
        }
        var tags = jsdocs[^1].AsJSDoc()!.Tags;
        return tags is not null && (tags.Nodes?.Length ?? 0) > 0;
    }

    internal static string TemplateToSnippet(string template, string newLine)
    {
        if (template == "/** */")
        {
            return "/**" + newLine + " * $0" + newLine + " */";
        }

        var snippetIndex = 1;
        template = EscapeSnippetText(template);
        template = StripJSDocTemplateIndentation(template, newLine);
        return TransformJSDocTemplateLines(template, newLine, ref snippetIndex);
    }

    internal static string StripJSDocTemplateIndentation(string template, string newLine)
    {
        var lines = template.Split(newLine);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart(' ', '\t');
            if (trimmed.StartsWith('/'))
            {
                lines[i] = trimmed;
            }
            else if (trimmed.StartsWith('*'))
            {
                lines[i] = " " + trimmed;
            }
        }
        return string.Join(newLine, lines);
    }

    internal static string TransformJSDocTemplateLines(string template, string newLine, ref int snippetIndex)
    {
        var lines = template.Split(newLine);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i > 0 && lines[i - 1].StartsWith("/**", StringComparison.Ordinal) && LineHasOnlyJSDocAsterisk(line))
            {
                lines[i] = line + "$0";
                continue;
            }
            var (transformed, ok) = TransformJSDocParamLine(line, ref snippetIndex);
            if (ok)
            {
                lines[i] = transformed;
                continue;
            }
            (transformed, ok) = TransformJSDocReturnsLine(line, ref snippetIndex);
            if (ok)
            {
                lines[i] = transformed;
            }
        }
        return string.Join(newLine, lines);
    }

    internal static bool LineHasOnlyJSDocAsterisk(string line)
    {
        line = line.TrimStart(' ', '\t');
        return line.StartsWith('*') && IsOnlySpacesOrTabs(line[1..]);
    }

    internal static (string Text, bool Ok) TransformJSDocParamLine(string line, ref int snippetIndex)
    {
        var prefix = "";
        var rest = line;
        if (rest.StartsWith(' '))
        {
            prefix = " ";
            rest = rest[1..];
        }
        if (!rest.StartsWith("* @param", StringComparison.Ordinal))
        {
            return ("", false);
        }
        rest = rest["* @param".Length..];
        if (!StartsWithSingleLineWhitespace(rest))
        {
            return ("", false);
        }
        rest = rest.TrimStart(' ', '\t');

        var typeText = "";
        if (rest.StartsWith('{'))
        {
            var closeBrace = rest.IndexOf('}');
            if (closeBrace < 0)
            {
                return ("", false);
            }
            typeText = " " + rest[..(closeBrace + 1)];
            rest = rest[(closeBrace + 1)..];
            if (!StartsWithSingleLineWhitespace(rest))
            {
                return ("", false);
            }
            rest = rest.TrimStart(' ', '\t');
        }

        var (paramName, remaining, ok) = ScanNonWhitespace(rest);
        if (!ok || !IsOnlySpacesOrTabs(remaining))
        {
            return ("", false);
        }

        var outText = prefix + "* @param ";
        if (typeText == " {any}" || typeText == " {*}")
        {
            outText += "{${" + snippetIndex + ":*}} ";
            snippetIndex++;
        }
        else if (typeText != "")
        {
            outText += typeText + " ";
        }
        outText += paramName + " ${" + snippetIndex + "}";
        snippetIndex++;
        return (outText, true);
    }

    internal static (string Text, bool Ok) TransformJSDocReturnsLine(string line, ref int snippetIndex)
    {
        var prefix = "";
        var rest = line;
        if (rest.StartsWith(' '))
        {
            prefix = " ";
            rest = rest[1..];
        }
        if (!rest.StartsWith("* @returns", StringComparison.Ordinal) || !IsOnlySpacesOrTabs(rest["* @returns".Length..]))
        {
            return ("", false);
        }
        var text = prefix + "* @returns ${" + snippetIndex + "}";
        snippetIndex++;
        return (text, true);
    }

    internal static (string Word, string Remaining, bool Ok) ScanNonWhitespace(string text)
    {
        if (text == "")
        {
            return ("", "", false);
        }
        var i = 0;
        while (i < text.Length)
        {
            var ch = Scan.DecodeRuneAt(text, i, out var size);
            if (size == 0 || StringUtil.StringUtil.IsWhiteSpaceLike(ch))
            {
                if (i == 0)
                {
                    return ("", "", false);
                }
                return (text[..i], text[i..], true);
            }
            i += size;
        }
        return (text, "", true);
    }

    internal static bool IsJSDocSnippetPrefix(string prefix)
    {
        var trimmed = TrimRightSingleLineWhitespace(prefix);
        if (trimmed.EndsWith("/**", StringComparison.Ordinal))
        {
            return true;
        }
        var start = SkipSingleLineWhitespace(prefix, 0);
        if (start >= trimmed.Length || trimmed[start] != '/')
        {
            return false;
        }
        if (start + 3 > trimmed.Length)
        {
            return false;
        }
        // Go 的 `for i := start + 1; i < len(trimmed); i++ { if trimmed[i] != '*' { return false } }`。
        if (trimmed.AsSpan(start + 1).IndexOfAnyExcept('*') >= 0)
        {
            return false;
        }
        return trimmed.Length - start >= 3;
    }

    internal static (int Start, bool Ok) GetJSDocSnippetPrefixStart(string prefix)
    {
        var trimmed = TrimRightSingleLineWhitespace(prefix);
        for (var i = trimmed.Length - 1; i >= 0 && trimmed[i] == '*'; i--)
        {
            if (i > 0 && trimmed[i - 1] == '/')
            {
                return (i - 1, true);
            }
        }
        if (trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            return (trimmed.Length - 1, true);
        }
        return (0, false);
    }

    internal static bool IsJSDocSnippetSuffix(string suffix)
    {
        var trimmed = TrimRightSingleLineWhitespace(suffix[SkipSingleLineWhitespace(suffix, 0)..]);
        if (trimmed == "")
        {
            return true;
        }
        if (!trimmed.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }
        // Go 的 `for i := 0; i < len(trimmed)-1; i++ { if trimmed[i] != '*' { return false } }`（末尾那个 '/' 不检查）。
        if (trimmed.AsSpan(0, trimmed.Length - 1).IndexOfAnyExcept('*') >= 0)
        {
            return false;
        }
        return true;
    }

    internal static (int End, bool Ok) GetJSDocSnippetSuffixEnd(string suffix)
    {
        var pos = SkipSingleLineWhitespace(suffix, 0);
        while (pos < suffix.Length && suffix[pos] == '*')
        {
            pos++;
        }
        if (pos < suffix.Length && suffix[pos] == '/')
        {
            return (pos + 1, true);
        }
        return (0, false);
    }

    internal static string TrimRightSingleLineWhitespace(string text)
    {
        var end = 0;
        for (var pos = 0; pos < text.Length;)
        {
            var ch = Scan.DecodeRuneAt(text, pos, out var size);
            if (size == 0)
            {
                break;
            }
            pos += size;
            if (!StringUtil.StringUtil.IsWhiteSpaceSingleLine(ch))
            {
                end = pos;
            }
        }
        return text[..end];
    }

    internal static int SkipSingleLineWhitespace(string text, int pos)
    {
        while (pos < text.Length)
        {
            var ch = Scan.DecodeRuneAt(text, pos, out var size);
            if (size == 0 || !StringUtil.StringUtil.IsWhiteSpaceSingleLine(ch))
            {
                break;
            }
            pos += size;
        }
        return pos;
    }

    internal static bool IsOnlySingleLineWhitespace(string text)
    {
        return SkipSingleLineWhitespace(text, 0) == text.Length;
    }

    internal static bool StartsWithSingleLineWhitespace(string text)
    {
        if (text == "")
        {
            return false;
        }
        var ch = Scan.DecodeRuneAt(text, 0, out var size);
        return size != 0 && StringUtil.StringUtil.IsWhiteSpaceSingleLine(ch);
    }

    internal static bool IsOnlySpacesOrTabs(string text)
    {
        foreach (var ch in text)
        {
            if (ch != ' ' && ch != '\t')
            {
                return false;
            }
        }
        return true;
    }
}

public sealed partial class LanguageService
{
    internal CompletionList? GetJSDocSnippetCompletion(Context ctx, SourceFile file, int position)
    {
        if (UserPreferences().EnableJSDocCompletions.IsFalse())
        {
            return null;
        }
        if (!Ls.IsPotentiallyValidJSDocSnippetCompletionPosition(file, position))
        {
            return null;
        }
        var newLine = FormatOptions().NewLineCharacter;
        if (newLine == "")
        {
            newLine = "\n";
        }
        var template = Ls.GetDocCommentTemplateAtPosition(file, position, UserPreferences().GenerateReturnInDocTemplate.IsTrue(), newLine);
        if (template is null)
        {
            return null;
        }

        var insertText = template._newText;
        InsertTextFormat? insertTextFormat = null;
        if (Ls.ClientSupportsItemSnippet(ctx))
        {
            insertText = Ls.TemplateToSnippet(insertText, newLine);
            insertTextFormat = InsertTextFormat.Snippet;
        }

        var editRange = GetJSDocSnippetCompletionRange(ctx, file, position, insertText);
        string[]? commitCharacters = null;
        if (Ls.ClientSupportsItemCommitCharacters(ctx))
        {
            commitCharacters = [];
        }
        var item = new CompletionItem
        {
            _completionItem = new LsProto.CompletionItem
            {
                Label = "/** */",
                Kind = CompletionItemKind.Text,
                Detail = JSDoc_comment.Localize(TypeScript.Locale.Locale.FromContext(ctx)),
                SortText = "\0",
                InsertTextFormat = insertTextFormat,
                TextEdit = editRange,
                CommitCharacters = commitCharacters,
            },
        };
        return new CompletionList
        {
            IsIncomplete = false,
            Items = [item],
        };
    }

    internal TextEditOrInsertReplaceEdit? GetJSDocSnippetCompletionRange(Context ctx, SourceFile file, int position, string newText)
    {
        var text = file.Text();
        var lineStart = GetLineStartPositionForPosition(position, file);
        var prefix = text[lineStart..position];
        var start = position;
        var (prefixStart, hasPrefixStart) = Ls.GetJSDocSnippetPrefixStart(prefix);
        if (hasPrefixStart)
        {
            start = lineStart + prefixStart;
        }

        var lineEnd = Ls.GetLineEndOfPosition(file, position);
        var suffix = text[position..lineEnd];
        var end = position;
        var (suffixEnd, hasSuffixEnd) = Ls.GetJSDocSnippetSuffixEnd(suffix);
        if (hasSuffixEnd)
        {
            end += suffixEnd;
        }

        var (replacementRange, fidelity) = CreateLspRangeFromBounds(start, end, file);
        if (!fidelity.IsExact())
        {
            return null;
        }
        if (Ls.ClientSupportsItemInsertReplace(ctx))
        {
            return new TextEditOrInsertReplaceEdit
            {
                InsertReplaceEdit = new InsertReplaceEdit
                {
                    NewText = newText,
                    Insert = replacementRange,
                    Replace = replacementRange,
                },
            };
        }
        return new TextEditOrInsertReplaceEdit
        {
            TextEdit = new TextEdit
            {
                NewText = newText,
                Range = replacementRange,
            },
        };
    }
}
