// ported from: internal/ls/format.go:1-301
//
// ⚠ `getRangeOfEnclosingComment`（format.go:257-300）**已由 G1 就地落在 `Ls/Utilities.cs`**（artifacts/ls-g1-helpers.md §3.2），
//   本文件**不含**它的声明（CS0111）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `*LanguageService` 方法 → partial 类成员，Go 导出 ⇒ `public`、未导出 ⇒ `internal`；`toLSProtoTextEdits` 等在
//    Go 里是未导出方法 ⇒ `internal`（方法 PascalCase，不加 `_`）。
// 2) 多返回值 `(lsproto.TextEditsOrNull, error)` → 元组 `(TextEditsOrNull Response, Exception? Error)`
//    （`DocumentFormattingResponse = DocumentRangeFormattingResponse = DocumentOnTypeFormattingResponse = TextEditsOrNull`）。
// 3) Go 的 `var edits []*lsproto.TextEdit`（nil 切片）+ `&edits` → C# 用 `TextEdit?[]?`：Go 的"nil 切片指针"在 JSON
//    里就是 `null`，C# 的 `null` 数组同形（union 字段声明就是 `TextEdit?[]?`）。
// 4) Go 的 `mappedFormattingRange` 是**结构体**（值语义：`candidate` 是副本，改 `candidate.originalRange` 不影响
//    candidates 数组）⇒ C# 用 `struct`（不是 class），否则 `NonOverlappingFormattingRanges` 的原地过滤会把
//    candidates 自己也改掉。
// 5) `slices.SortStableFunc` → LINQ `OrderBy/ThenBy`（`Enumerable.OrderBy` 是稳定排序，与 SortStableFunc 一致）。
// 6) `cmp.Compare(a.NewText, b.NewText)` → `string.CompareOrdinal`（Go 的字符串 `<` 就是按字节序，C# 用序数比较）。
// 7) `core.TextChange.Pos()/End()` → C# 的 `TextChange.Range.Pos()/End()`（Core/TextChange.cs 只有 `Range`/`NewText` 字段）。
// 8) `format.WithFormatCodeSettings/FormatDocument/...` → `using static TypeScript.Format.Format;` 的裸名调用
//    （`Format` 既是命名空间又是静态类，见 artifacts/ls-g1-helpers.md §4.1）。
// 9) `lsutil.FromLSFormatOptions` → 裸名（`using static TypeScript.LsUtil.LsUtil;`）；`options *lsproto.FormattingOptions`
//    在 C# 侧声明为 `FormattingOptions?`（Go 的指针语义），传给 FromLSFormatOptions 时补 `!`（Go 侧解引用 nil 同样 panic）。
// 10) `l.converters` → `_converters!`；`l.UserPreferences()`/`l.FormatOptions()` 是 partial 类的既有成员。
// 11) `spanmap.KindVerbatim` → `TypeScript.SpanMap.Kind.Verbatim`（`Kind` 已被 `using Kind = TypeScript.Ast.Kind;` 占用）。
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Format;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Format.Format;
using static TypeScript.LsProto.LsProto;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Scanner.Scanner;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>
/// Go: <c>type mappedFormattingRange struct { projection *ast.SourceFile; segment spanmap.Segment; originalRange core.TextRange }</c>
/// （format.go:142-146）。值语义（见文件头第 4 条）。
/// </summary>
internal struct MappedFormattingRange
{
    internal SourceFile _projection;
    internal Segment _segment;
    internal TextRange _originalRange;
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) toLSProtoTextEdits(file *ast.SourceFile, changes []core.TextChange) []*lsproto.TextEdit</c>（format.go:15-26）。</summary>
    internal TextEdit?[]? ToLSProtoTextEdits(SourceFile file, TextChange[] changes)
    {
        List<TextEdit?> result = new(changes.Length);
        foreach (var c in changes)
        {
            var (lspRange, fidelity) = _converters!.ToLSPRange(file, new TextRange(c.Range.Pos(), c.Range.End()));
            if (!fidelity.IsExact())
            {
                return null;
            }

            result.Add(new TextEdit
            {
                NewText = c.NewText,
                Range = lspRange,
            });
        }

        return [.. result];
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideFormatDocument(ctx, documentURI, options *lsproto.FormattingOptions) (lsproto.DocumentFormattingResponse, error)</c>（format.go:28-43）。</summary>
    public (TextEditsOrNull Response, Exception? Error) ProvideFormatDocument(
        Context ctx,
        DocumentUri documentURI,
        FormattingOptions? options)
    {
        if (UserPreferences().EnableFormatting.IsFalse())
        {
            return (new TextEditsOrNull(), null);
        }

        var (_, file) = GetProgramAndFile(documentURI);
        var formatOpts = FromLSFormatOptions(FormatOptions(), options!);
        TextEdit?[]? edits;
        if (file.ContentMapper() == "")
        {
            edits = ToLSProtoTextEdits(file, GetFormattingEditsForDocument(ctx, file, formatOpts));
        }
        else
        {
            edits = GetFormattingEditsForMappedRange(ctx, file, formatOpts, new TextRange(0, file.OriginalText().Length));
        }

        return (new TextEditsOrNull { TextEdits = edits }, null);
    }

    /// <summary>
    /// getFormattingEditsForMappedRange formats each formatting-enabled verbatim intersection with originalRange.
    /// Duplicate formatting projections are unsupported. If mappings overlap anyway, each original-text position
    /// is formatted only once, preferring the earliest and then longest applicable mapping.
    /// Go: format.go:48-84。
    /// </summary>
    internal TextEdit?[] GetFormattingEditsForMappedRange(Context ctx, SourceFile file, FormatCodeSettings options, TextRange originalRange)
    {
        var projections = new List<SourceFile> { file };
        projections.AddRange(file.SupplementalSourceFiles());
        List<MappedFormattingRange> candidates = [];
        foreach (var projection in projections)
        {
            var spanMap = projection.SpanMap();
            if (spanMap is null)
            {
                continue;
            }

            foreach (var segment in spanMap.Segments())
            {
                if (segment.Kind != TypeScript.SpanMap.Kind.Verbatim || (segment.Features & Feature.Formatting) == 0)
                {
                    continue;
                }

                var originalStart = Math.Max(originalRange.Pos(), segment.OriginalStart);
                var originalEnd = Math.Min(originalRange.End(), segment.OriginalEnd);
                if (originalStart >= originalEnd)
                {
                    continue;
                }

                candidates.Add(new MappedFormattingRange
                {
                    _projection = projection,
                    _segment = segment,
                    _originalRange = new TextRange(originalStart, originalEnd),
                });
            }
        }

        List<TextEdit?> edits = [];
        foreach (var candidate in NonOverlappingFormattingRanges([.. candidates]))
        {
            var virtualRange = new TextRange(
                (int)candidate._segment.VirtualStart + candidate._originalRange.Pos() - (int)candidate._segment.OriginalStart,
                (int)candidate._segment.VirtualStart + candidate._originalRange.End() - (int)candidate._segment.OriginalStart);
            foreach (var change in GetFormattingEditsForRange(ctx, candidate._projection, options, virtualRange))
            {
                if (change.Range.Pos() < virtualRange.Pos() || change.Range.End() > virtualRange.End())
                {
                    continue;
                }

                var (lspRange, fidelity) = _converters!.ToLSPRangeForFeature(candidate._projection, new TextRange(change.Range.Pos(), change.Range.End()), Feature.Formatting);
                if (!fidelity.IsExact())
                {
                    continue;
                }

                edits.Add(new TextEdit { Range = lspRange, NewText = change.NewText });
            }
        }

        return
        [
            .. edits
                .OrderBy(e => e!.Range, Comparer<Range>.Create(CompareRanges))
                .ThenBy(e => e!.NewText, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// nonOverlappingFormattingRanges chooses at most one formatting projection for each original-text position.
    /// Candidates are ordered by original start and then descending end, so a longer mapping wins when several
    /// mappings start together:
    ///
    /// <code>
    /// candidates:  [---------- A ----------)
    ///              [---- B ----)
    /// result:      [---------- A ----------)
    /// </code>
    ///
    /// Since starts are ordered, a candidate can only overlap the end of the last accepted range. Its start is
    /// trimmed to that end, preserving any uncovered suffix:
    ///
    /// <code>
    /// candidates:  [------- A -------)
    ///                     [------- B ----------)
    /// result:      [------- A -------)[-- B' --)
    /// </code>
    ///
    /// Fully covered candidates have no suffix and are discarded. The segment itself is retained so callers can
    /// translate a trimmed original range to the corresponding offset in its virtual projection.
    /// Go: format.go:148-183。
    /// </summary>
    internal static MappedFormattingRange[] NonOverlappingFormattingRanges(MappedFormattingRange[] candidates)
    {
        var sorted = candidates
            .OrderBy(c => c._originalRange.Pos())
            .ThenByDescending(c => c._originalRange.End())
            .ToArray();

        List<MappedFormattingRange> result = [];
        foreach (var candidate in sorted)
        {
            var trimmed = candidate;
            if (result.Count > 0)
            {
                trimmed._originalRange = trimmed._originalRange.WithPos(Math.Max(trimmed._originalRange.Pos(), result[^1]._originalRange.End()));
            }

            if (trimmed._originalRange.Len() > 0)
            {
                result.Add(trimmed);
            }
        }

        return [.. result];
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideFormatDocumentRange(ctx, documentURI, options, r lsproto.Range) (lsproto.DocumentRangeFormattingResponse, error)</c>（format.go:185-211）。</summary>
    public (TextEditsOrNull Response, Exception? Error) ProvideFormatDocumentRange(
        Context ctx,
        DocumentUri documentURI,
        FormattingOptions? options,
        Range r)
    {
        if (UserPreferences().EnableFormatting.IsFalse())
        {
            return (new TextEditsOrNull(), null);
        }

        var (_, file) = GetProgramAndFile(documentURI);
        var formatOpts = FromLSFormatOptions(FormatOptions(), options!);
        if (file.ContentMapper() != "")
        {
            var mappedEdits = GetFormattingEditsForMappedRange(ctx, file, formatOpts, _converters!.FromLSPRangeToOriginal(file, r));
            return (new TextEditsOrNull { TextEdits = mappedEdits }, null);
        }

        var ranges = _converters!.FromLSPRangeForSourceFile(file, r, Feature.Formatting);
        if (ranges.Length != 1 || !ranges[0].Fidelity.IsExact())
        {
            return (new TextEditsOrNull(), null);
        }

        file = ranges[0].Script;
        var edits = ToLSProtoTextEdits(file, GetFormattingEditsForRange(
            ctx,
            file,
            formatOpts,
            ranges[0].Span));
        return (new TextEditsOrNull { TextEdits = edits }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideFormatDocumentOnType(ctx, documentURI, options, position, character) (lsproto.DocumentOnTypeFormattingResponse, error)</c>（format.go:213-238）。</summary>
    public (TextEditsOrNull Response, Exception? Error) ProvideFormatDocumentOnType(
        Context ctx,
        DocumentUri documentURI,
        FormattingOptions? options,
        Position position,
        string character)
    {
        if (UserPreferences().EnableFormatting.IsFalse())
        {
            return (new TextEditsOrNull(), null);
        }

        var (_, file) = GetProgramAndFile(documentURI);
        var formatOpts = FromLSFormatOptions(FormatOptions(), options!);
        var positions = _converters!.FromLSPPositionForSourceFile(file, position, Feature.Formatting);
        if (positions.Length != 1 || !positions[0].Fidelity.IsExact())
        {
            return (new TextEditsOrNull(), null);
        }

        file = positions[0].Script;
        var edits = ToLSProtoTextEdits(file, GetFormattingEditsAfterKeystroke(
            ctx,
            file,
            formatOpts,
            positions[0].Position,
            character));
        return (new TextEditsOrNull { TextEdits = edits }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) getFormattingEditsForRange(ctx, file, options, r core.TextRange) []core.TextChange</c>（format.go:240-247）。</summary>
    internal TextChange[] GetFormattingEditsForRange(
        Context ctx,
        SourceFile file,
        FormatCodeSettings options,
        TextRange r)
    {
        ctx = WithFormatCodeSettings(ctx, options, options.NewLineCharacter);
        return FormatSelection(ctx, file, r.Pos(), r.End());
    }

    /// <summary>Go: <c>func (l *LanguageService) getFormattingEditsForDocument(ctx, file, options) []core.TextChange</c>（format.go:249-255）。</summary>
    internal TextChange[] GetFormattingEditsForDocument(
        Context ctx,
        SourceFile file,
        FormatCodeSettings options)
    {
        ctx = WithFormatCodeSettings(ctx, options, options.NewLineCharacter);
        return FormatDocument(ctx, file);
    }

    /// <summary>Go: <c>func (l *LanguageService) getFormattingEditsAfterKeystroke(ctx, file, options, position int, key string) []core.TextChange</c>（format.go:257-...）。</summary>
    internal TextChange[] GetFormattingEditsAfterKeystroke(
        Context ctx,
        SourceFile file,
        FormatCodeSettings options,
        int position,
        string key)
    {
        ctx = WithFormatCodeSettings(ctx, options, options.NewLineCharacter);

        var tokenAtPosition = GetTokenAtPosition(file, position);
        if (Ls.IsInComment(file, position, tokenAtPosition) is null)
        {
            switch (key)
            {
                case "{":
                    return FormatOnOpeningCurly(ctx, file, position);
                case "}":
                    return FormatOnClosingCurly(ctx, file, position);
                case ";":
                    return FormatOnSemicolon(ctx, file, position);
                case "\n":
                    return FormatOnEnter(ctx, file, position);
                default:
                    return [];
            }
        }

        return [];
    }
}
