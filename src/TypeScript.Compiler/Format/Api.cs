// ported from: internal/format/api.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.1/§3.2）：
// - 包级函数 → 静态类 `Format`；`FormatRequestKind` 的 iota 常量 → C# enum（成员名去 `FormatRequestKind` 前缀）。
// - **context 携带值**：Go 的 `formatContextKey` + `context.WithValue`（两个键：formatOptionsKey/formatNewlineKey）
//   在 C# 里落成 `TypeScript.Core.Context` 上的两个属性（`FormatCodeSettings`/`FormatNewLine`）——Core 不认识
//   lsutil，所以按 Go 的 `context.Value` 语义存 `object?`，`GetFormatCodeSettingsFromContext` 做类型转换
//   （= Go 的类型断言）。`WithFormatCodeSettings` 用 Context 的 internal 构造器派生新实例（不可变派生，同 Go）。
//   Go 的 `formatContextKey`/`formatOptionsKey`/`formatNewlineKey` 因此没有对应物（记录在报告里）。
// - `&span`（`*core.TextRange` 忽略区间）→ `TextRange?`。
// - `utf8.DecodeRuneInString` → `Scan.DecodeRuneAt`（§3.1）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using static TypeScript.Core.Core;
using static TypeScript.LsUtil.LsUtil;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

/// <summary>Go 的 <c>type FormatRequestKind int</c>。</summary>
public enum FormatRequestKind
{
    FormatDocument = 0,
    FormatSelection = 1,
    FormatOnEnter = 2,
    FormatOnSemicolon = 3,
    FormatOnOpeningCurlyBrace = 4,
    FormatOnClosingCurlyBrace = 5,
}

public static partial class Format
{
    public static Context WithFormatCodeSettings(Context ctx, FormatCodeSettings options, string newLine)
    {
        // Go: ctx = context.WithValue(ctx, formatOptionsKey, options); ctx = context.WithValue(ctx, formatNewlineKey, newLine)
        // In strada, the rules map was both globally cached *and* cached into the context, for some reason. We skip that here and just use the global one.
        return new Context(ctx.RequestID, ctx.CheckerLifetime, ctx.CancellationToken, ctx.Locale, options, newLine);
    }

    public static FormatCodeSettings GetFormatCodeSettingsFromContext(Context ctx)
    {
        if (ctx.FormatCodeSettings is FormatCodeSettings opt)
        {
            return opt;
        }

        return GetDefaultFormatCodeSettings();
    }

    // TODO: Move into broader LS - more than just the formatter uses the newline editor setting/host new line
    public static string GetNewLineOrDefaultFromContext(Context ctx)
    {
        var opt = GetFormatCodeSettingsFromContext(ctx);
        if (opt.NewLineCharacter.Length > 0)
        {
            return opt.NewLineCharacter;
        }

        var host = ctx.FormatNewLine ?? "";
        if (host.Length > 0)
        {
            return host;
        }

        return "\n";
    }

    public static TextChange[] FormatSpan(Context ctx, TextRange span, SourceFile file, FormatRequestKind kind)
    {
        // find the smallest node that fully wraps the range and compute the initial indentation for the node
        var enclosingNode = FindEnclosingNode(span, file);
        var opts = GetFormatCodeSettingsFromContext(ctx);

        return NewFormattingScanner(
            file.Text(),
            file.LanguageVariant,
            GetScanStartPosition(enclosingNode, span, file),
            span.End(),
            NewFormatSpanWorker(
                ctx,
                span,
                enclosingNode,
                GetIndentationForNode(enclosingNode, span, file, opts),
                GetOwnOrInheritedDelta(enclosingNode, opts, file),
                kind,
                PrepareRangeContainsErrorFunction(file.Diagnostics(), span),
                file));
    }

    public static TextChange[] FormatNodeGivenIndentation(Context ctx, Node node, SourceFile file, LanguageVariant languageVariant, int initialIndentation, int delta)
    {
        var textRange = NewTextRange(node.Pos(), node.End());
        return NewFormattingScanner(
            file.Text(),
            languageVariant,
            textRange.Pos(),
            textRange.End(),
            NewFormatSpanWorker(
                ctx,
                textRange,
                node,
                initialIndentation,
                delta,
                FormatRequestKind.FormatSelection,
                static _ => false, // assume that node does not have any errors
                file));
    }

    internal static TextChange[] FormatNodeLines(Context ctx, SourceFile sourceFile, Node? node, FormatRequestKind requestKind)
    {
        if (node is null)
        {
            return [];
        }

        var tokenStart = Scan.GetTokenPosOfNode(node, sourceFile, false);
        var lineStart = GetLineStartPositionForPosition(tokenStart, sourceFile);
        var span = NewTextRange(lineStart, node.End());
        return FormatSpan(ctx, span, sourceFile, requestKind);
    }

    public static TextChange[] FormatDocument(Context ctx, SourceFile sourceFile)
    {
        return FormatSpan(ctx, NewTextRange(0, sourceFile.End()), sourceFile, FormatRequestKind.FormatDocument);
    }

    public static TextChange[] FormatSelection(Context ctx, SourceFile sourceFile, int start, int end)
    {
        return FormatSpan(ctx, NewTextRange(GetLineStartPositionForPosition(start, sourceFile), end), sourceFile, FormatRequestKind.FormatSelection);
    }

    public static TextChange[] FormatOnOpeningCurly(Context ctx, SourceFile sourceFile, int position)
    {
        var openingCurly = FindImmediatelyPrecedingTokenOfKind(position, Kind.OpenBraceToken, sourceFile);
        if (openingCurly is null)
        {
            return [];
        }

        var curlyBraceRange = openingCurly.Parent;
        var outermostNode = FindOutermostNodeWithinListLevel(curlyBraceRange);
        /**
         * We limit the span to end at the opening curly to handle the case where
         * the brace matched to that just typed will be incorrect after further edits.
         * For example, we could type the opening curly for the following method
         * body without brace-matching activated:
         * ```
         * class C {
         *     foo()
         * }
         * ```
         * and we wouldn't want to move the closing brace.
         */
        var textRange = NewTextRange(GetLineStartPositionForPosition(Scan.GetTokenPosOfNode(outermostNode!, sourceFile, false), sourceFile), position);
        return FormatSpan(ctx, textRange, sourceFile, FormatRequestKind.FormatOnOpeningCurlyBrace);
    }

    public static TextChange[] FormatOnClosingCurly(Context ctx, SourceFile sourceFile, int position)
    {
        var precedingToken = FindImmediatelyPrecedingTokenOfKind(position, Kind.CloseBraceToken, sourceFile);
        return FormatNodeLines(ctx, sourceFile, FindOutermostNodeWithinListLevel(precedingToken), FormatRequestKind.FormatOnClosingCurlyBrace);
    }

    public static TextChange[] FormatOnSemicolon(Context ctx, SourceFile sourceFile, int position)
    {
        var semicolon = FindImmediatelyPrecedingTokenOfKind(position, Kind.SemicolonToken, sourceFile);
        return FormatNodeLines(ctx, sourceFile, FindOutermostNodeWithinListLevel(semicolon), FormatRequestKind.FormatOnSemicolon);
    }

    public static TextChange[] FormatOnEnter(Context ctx, SourceFile sourceFile, int position)
    {
        var line = Scan.GetECMALineOfPosition(sourceFile, position);
        if (line == 0)
        {
            return [];
        }

        // get start position for the previous line
        var startPos = Scan.GetECMALineStarts(sourceFile)[line - 1];
        // After the enter key, the cursor is now at a new line. The new line may or may not contain non-whitespace characters.
        // If the new line has only whitespaces, we won't want to format this line, because that would remove the indentation as
        // trailing whitespaces. So the end of the formatting span should be the later one between:
        //  1. the end of the previous line
        //  2. the last non-whitespace character in the current line
        var endOfFormatSpan = Scan.GetECMAEndLinePosition(sourceFile, line);
        var text = sourceFile.Text();
        while (endOfFormatSpan > startPos)
        {
            // Go: ch, s := utf8.DecodeRuneInString(text[endOfFormatSpan:]) —— 在 UTF-16 代码单元上下标解码（§3.1）
            var ch = Scan.DecodeRuneAt(text, endOfFormatSpan, out var size);
            if (size == 0 || StringUtil.StringUtil.IsWhiteSpaceSingleLine(ch)) // on multibyte character keep backing up
            {
                endOfFormatSpan--;
                continue;
            }

            break;
        }

        // if the character at the end of the span is a line break, we shouldn't include it, because it indicates we don't want to
        // touch the current line at all. Also, on some OSes the line break consists of two characters (\r\n), we should test if the
        // previous character before the end of format span is line break character as well.
        var chAtEnd = Scan.DecodeRuneAt(text, endOfFormatSpan, out _);
        if (StringUtil.StringUtil.IsLineBreak(chAtEnd))
        {
            endOfFormatSpan--;
        }

        var span = NewTextRange(
            startPos,
            // end value is exclusive so add 1 to the result
            endOfFormatSpan + 1);

        return FormatSpan(ctx, span, sourceFile, FormatRequestKind.FormatOnEnter);
    }
}
