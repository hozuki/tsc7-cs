// ported from: tsc/internal/ls/change/trackerimpl.go:1-514
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `getNonformattedText` 返回 `(string, *ast.Node)`，调用处再 `.AsSourceFile()` → C# 直接返回
//    `(string Text, SourceFile File)`（`Printer.CreateSyntheticSourceFile` 本来就返回 `SourceFile`），调用点不变。
// 2) `GetFormatCodeSettingsForWriting`（Go 的导出包级函数）落在 `LsChange.cs`（§2：包级函数 → 与 package 同名的
//    静态类）；本文件用 `using static TypeScript.LsChange.LsChange;` 裸名调用，文本与 Go 一致。
// 3) `dedupeIdenticalEdits`/`textEditsConflict`/`hasCommentsBeforeLineBreak`/`needSemicolonBetween`/
//    `leadingIndentation`（Go 的包级函数，含未导出的）→ `LsChange` 静态类的 `internal static` 成员
//    （`TextEditsConflict` 供 xunit 用；调用点裸名，文本与 Go 一致）。
// 4) Go 的 `slices.SortStableFunc` 在 C# 没有对应物（Core 只有 DeduplicateSorted/InsertSorted）→ LINQ `OrderBy`
//    （文档保证稳定）。
// 5) Go 的 `range == range`（结构体值比较）在 C# 里是 class → 一律用
//    `CompareRanges(a, b) == 0` / `ComparePositions(a, b) == 0` 复刻值语义。
// 6) Go 的 `strings.TrimSuffix`/`HasSuffix`/`TrimLeftFunc`/`LastIndexAny`/`Join`/`slices.Collect`/`slices.AppendSeq`
//    → `EndsWith(_, StringComparison.Ordinal)` + 切片 / `TrimStart()` / `LastIndexOfAny` / `string.Join` /
//    `GetLeadingCommentRanges(...).Concat(GetTrailingCommentRanges(...))`（§3.8）。
// 7) `(T, bool)` 与 panic 同 Tracker.cs 文件头；命名空间遮蔽同 Tracker.cs 文件头第 6 条。
// 8) `format.ShouldIndentChildNode(settings, node, nil, nil)`：C# 侧该函数把 Go 的 `sourceFile *ast.SourceFile` 收窄
//    成非空 `SourceFile`（被调方 `NodeWillIndentChild` 仍接受 `SourceFile?`），Go 这里确实传 nil → 写 `null!`。
using System.Collections.Generic;
using System.Linq;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsUtil;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Format.Format;
using static TypeScript.LsChange.LsChange;
using static TypeScript.LsProto.LsProto;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Parser.Parser;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using SpanMapExt = TypeScript.SpanMap.SpanMapExtensions;
using static TypeScript.StringUtil.StringUtil;
using SpanMapNs = TypeScript.SpanMap;
using Range = TypeScript.LsProto.Range;
using TextEdit = TypeScript.LsProto.TextEdit;

namespace TypeScript.LsChange;

public sealed partial class Tracker
{
    internal Dictionary<string, TextEdit[]> GetTextChangesFromChanges()
    {
        var changes = new Dictionary<string, List<TextEdit>>();
        // A content-mapped file can have several projections, each keyed separately in t.changes but
        // all sharing one original file. Their edits are collected together before being ordered and checked,
        // so duplicate edits are emitted once and conflicting edits are rejected regardless of map iteration
        // order.
        var projections = new Dictionary<string, int>();
        foreach (var (sourceFile, changesInFile) in _changes.M)
        {
            var fileName = sourceFile.OriginalFileName();
            if (_unmappableFiles.Has(fileName))
            {
                continue;
            }

            // For a content-mapped file, a mapper may reorder source text relative to the original document.
            // Convert first, then sort and check for overlap in the space where edits are actually applied.
            var textChanges = MapNonNil(changesInFile.ToArray(), change =>
            {
                // !!! targetSourceFile

                var newText = ComputeNewText(change, sourceFile, sourceFile);
                // span := createTextSpanFromRange(c.Range)
                // !!!
                // Filter out redundant changes.
                // if (span.length == newText.length && stringContainsAt(targetSourceFile.text, newText, span.start)) { return nil }

                return new TextEdit
                {
                    NewText = newText,
                    Range = ToLSPEditRange(sourceFile, change.TextRange),
                };
            });

            if (textChanges.Length > 0)
            {
                if (!changes.TryGetValue(fileName, out var existing))
                {
                    existing = [];
                    changes[fileName] = existing;
                }

                existing.AddRange(textChanges);
                projections[fileName] = projections.GetValueOrDefault(fileName) + 1;
            }
        }

        var result = new Dictionary<string, TextEdit[]>();
        foreach (var (fileName, collected) in changes)
        {
            var textChanges = collected.ToArray();
            // Converting the edits above may have found that this file cannot be represented in its original
            // text. GetChanges drops it, so its order does not matter, and the best-effort ranges left behind
            // may overlap in ways the check below would refuse.
            if (_unmappableFiles.Has(fileName))
            {
                result[fileName] = textChanges;
                continue;
            }

            // order changes by start position
            // If the start position is the same, put the shorter range first, since an empty range (x, x) may precede (x, y) but not vice-versa.
            textChanges = [.. textChanges.OrderBy(static e => e, Comparer<TextEdit>.Create(static (a, b) => CompareRanges(a.Range, b.Range)))];
            if (projections[fileName] > 1)
            {
                textChanges = DedupeIdenticalEdits(textChanges);
            }

            // verify that change intervals do not overlap, except possibly at end points.
            for (var i = 0; i < textChanges.Length - 1; i++)
            {
                if (TextEditsConflict(textChanges[i], textChanges[i + 1], projections[fileName] > 1))
                {
                    if (projections[fileName] > 1)
                    {
                        // Projections of one original range disagree about how to edit it. That is a property of
                        // the mapper's output rather than a bug here, so drop the file instead of failing.
                        _unmappableFiles.Add(fileName);
                        break;
                    }

                    // assert change[i].End <= change[i + 1].Start
                    Debug.Fail($"changes overlap: {textChanges[i].Range} and {textChanges[i + 1].Range}");
                    throw new System.Diagnostics.UnreachableException();
                }
            }

            result[fileName] = textChanges;
        }

        return result;
    }

    /// <summary>dedupeIdenticalEdits drops exact duplicates from a sorted slice of edits. When a mapper copies one span
    /// of the original into more than one projection, an edit computed against each projection describes the
    /// same change to the same original range; emitting it once per projection would apply it repeatedly.</summary>
    internal static TextEdit[] DedupeIdenticalEdits(TextEdit[] edits)
    {
        var deduped = new List<TextEdit>(edits.Length);
        foreach (var edit in edits)
        {
            if (deduped.Count > 0 && CompareRanges(deduped[^1].Range, edit.Range) == 0 && deduped[^1].NewText == edit.NewText)
            {
                continue;
            }

            deduped.Add(edit);
        }

        return deduped.ToArray();
    }

    internal static bool TextEditsConflict(TextEdit a, TextEdit b, bool multipleProjections)
    {
        if (ComparePositions(a.Range.End, b.Range.Start) > 0)
        {
            return true;
        }

        // Different insertions at the same position are ambiguous when they may come from different projections.
        return multipleProjections &&
            ComparePositions(a.Range.Start, a.Range.End) == 0 &&
            CompareRanges(a.Range, b.Range) == 0 &&
            a.NewText != b.NewText;
    }

    internal string ComputeNewText(TrackerEdit change, SourceFile targetSourceFile, SourceFile sourceFile)
    {
        switch (change.Kind)
        {
            case TrackerEditKind.Remove:
                return "";
            case TrackerEditKind.Text:
                return change.NewText;
        }

        var positions = _converters.FromLSPPositionForSourceFile(
            sourceFile,
            ToLSPEditRange(sourceFile, change.TextRange).Start,
            SpanMapNs.Feature.All);
        var result = "";
        var found = false;
        // The original range may have multiple verbatim copies; it is safe to lose their identity only when
        // formatting at every exact projection produces the same edit.
        foreach (var mapped in positions)
        {
            if (!SpanMapExt.IsExact(mapped.Fidelity))
            {
                continue;
            }

            var projection = mapped.Script;
            var pos = mapped.Position.Value;
            string FormatNode(Node n)
            {
                return GetFormattedTextOfNode(n, targetSourceFile, projection, pos, change.Options);
            }

            string text;
            switch (change.Kind)
            {
                case TrackerEditKind.ReplaceWithMultipleNodes:
                    var joiner = change.Options._joiner;
                    if (joiner == "")
                    {
                        joiner = _newLine;
                    }

                    text = string.Join(joiner, Map(change.Nodes, n => TrimSuffix(FormatNode(n), _newLine)));
                    break;
                case TrackerEditKind.ReplaceWithSingleNode:
                    text = FormatNode(change.Node!);
                    break;
                default:
                    Debug.Fail($"change kind {(int)change.Kind} should have been handled earlier");
                    throw new System.Diagnostics.UnreachableException();
            }

            // Strip initial indentation if text will be inserted in the middle of the line.
            var noIndent = text;
            if (!(change.Options._indentation != null || GetLineStartPositionForPosition(pos, projection) == pos))
            {
                noIndent = text.TrimStart();
            }

            var candidate = change.Options.Prefix + noIndent + IfElse(noIndent.EndsWith(change.Options.Suffix, StringComparison.Ordinal), "", change.Options.Suffix);
            if (found && candidate != result)
            {
                _unmappableFiles.Add(sourceFile.OriginalFileName());
                return "";
            }

            result = candidate;
            found = true;
        }

        if (!found)
        {
            _unmappableFiles.Add(sourceFile.OriginalFileName());
        }

        return ReindentInsertedLines(sourceFile, change, result);
    }

    /// <summary>
    /// reindentInsertedLines fixes the indentation of a line an insertion introduces into the document the
    /// edit is applied to. The inserted text forms its own line when it ends in a newline, and that line has to
    /// pick up the indentation of the line it is being spliced into. Where the indentation goes depends on
    /// which side of the insertion point it already sits on:
    ///
    ///   - Inserting after a line's indentation (`\t\tfoo` with the point before `foo`) leaves the inserted text
    ///     indented but pushes the rest of the line down bare, so the indentation is repeated after the text.
    ///   - Inserting at the very start of a line leaves the existing text indented but puts the inserted text at
    ///     column zero, so the indentation is emitted before the text.
    ///
    /// The indentation is read from the original text at the edit's original position, so it holds for a
    /// content-mapped file whose projection is indented differently from the document the edit is applied to.
    /// Edits carrying an explicit indentation option are left alone.
    /// </summary>
    internal string ReindentInsertedLines(SourceFile sourceFile, TrackerEdit change, string text)
    {
        if (text == "" || change.TextRange.Pos() != change.TextRange.End() || change.Options._indentation != null)
        {
            return text;
        }

        if (!text.EndsWith(_newLine, StringComparison.Ordinal))
        {
            return text;
        }

        var original = sourceFile.OriginalText();
        var pos = change.TextRange.Pos();
        if (sourceFile.SpanMap() is { } spans)
        {
            var (mapped, fidelity) = spans.VirtualToOriginalPosition(new TextPos(pos));
            if (!SpanMapExt.IsExact(fidelity))
            {
                return text;
            }

            pos = mapped.Value;
        }

        if (pos < 0 || pos > original.Length)
        {
            return text;
        }

        var lineStart = original[..pos].LastIndexOfAny(['\r', '\n']) + 1;
        var beforePoint = original[lineStart..pos];
        if (beforePoint == "")
        {
            // At the start of a line: the existing text keeps its indentation, and the inserted line needs it —
            // but only when the formatter left the text at column zero. Where the formatter already indented it
            // (inserting into a multi-line list, say), that indentation is the correct one.
            if (LeadingIndentation(text) != "")
            {
                return text;
            }

            return LeadingIndentation(original[lineStart..]) + text;
        }

        if (LeadingIndentation(beforePoint) != beforePoint)
        {
            return text;
        }

        // Just past the indentation: the inserted line already has it, the text pushed down needs it back.
        return text + beforePoint;
    }

    internal static string LeadingIndentation(string text)
    {
        var end = 0;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
        {
            end++;
        }

        return text[..end];
    }

    internal string GetFormattedTextOfNode(Node nodeIn, SourceFile targetSourceFile, SourceFile sourceFile, int pos, NodeOptions options)
    {
        var (text, sourceFileLike) = GetNonformattedText(nodeIn, targetSourceFile);
        // !!! if (validate) validate(node, text);
        var formatOptions = GetFormatCodeSettingsForWriting(_formatSettings, targetSourceFile);

        int initialIndentation;
        var delta = 0;
        if (options._indentation == null)
        {
            initialIndentation = GetIndentation(pos, sourceFile, formatOptions, options.Prefix == _newLine || GetLineStartPositionForPosition(pos, sourceFile) == pos);
        }
        else
        {
            initialIndentation = options._indentation.Value;
        }

        if (options._delta != null)
        {
            delta = options._delta.Value;
        }
        else if (formatOptions.IndentSize != 0 && ShouldIndentChildNode(formatOptions, nodeIn, null, null!))
        {
            delta = formatOptions.IndentSize;
        }

        var changes = FormatNodeGivenIndentation(_ctx, sourceFileLike, sourceFileLike, targetSourceFile.LanguageVariant, initialIndentation, delta);
        return ApplyBulkEdits(text, changes);
    }

    internal (string Text, SourceFile File) GetNonformattedText(Node node, SourceFile sourceFile)
    {
        var (text, nodeOut) = PrintAndPositionNode(NodeFactory, node, sourceFile, _newLine, _formatSettings.IndentSize, EmitContext);
        var sourceFileLike = CreateSyntheticSourceFile(
            NodeFactory,
            nodeOut,
            text,
            new SourceFileParseOptions { FileName = sourceFile.FileName(), Path = sourceFile.Path() });
        return (text, sourceFileLike);
    }

    // method on the changeTracker because use of converters
    /// <summary>GetAdjustedRange computes the adjusted range for a node in a source file, accounting for trivia.</summary>
    public TextRange GetAdjustedRange(SourceFile sourceFile, Node startNode, Node endNode, LeadingTriviaOption leadingOption, TrailingTriviaOption trailingOption)
    {
        return NewTextRange(
            GetAdjustedStartPosition(sourceFile, startNode, leadingOption, false),
            GetAdjustedEndPosition(sourceFile, endNode, trailingOption));
    }

    // method on the changeTracker because use of converters
    internal int GetAdjustedStartPosition(SourceFile sourceFile, Node node, LeadingTriviaOption leadingOption, bool hasTrailingComment)
    {
        if (leadingOption == LeadingTriviaOption.JSDoc)
        {
            var jsDocComments = GetJSDocCommentRanges(NodeFactory, null, node, sourceFile.Text());
            if (jsDocComments.Count > 0)
            {
                return GetLineStartPositionForPosition(jsDocComments[0].Range.Pos(), sourceFile);
            }
        }

        var start = GetStartOfNode(node, sourceFile, false);
        var startOfLinePos = GetLineStartPositionForPosition(start, sourceFile);

        switch (leadingOption)
        {
            case LeadingTriviaOption.Exclude:
                return start;
            case LeadingTriviaOption.StartLine:
                if (node.Loc.ContainsInclusive(startOfLinePos))
                {
                    return startOfLinePos;
                }

                return start;
        }

        var fullStart = node.Pos();
        if (fullStart == start)
        {
            return start;
        }

        var lineStarts = sourceFile.ECMALineMap();
        var fullStartLineIndex = ComputeLineOfPosition(lineStarts, fullStart);
        var fullStartLinePos = (int)lineStarts[fullStartLineIndex];
        if (startOfLinePos == fullStartLinePos)
        {
            // full start and start of the node are on the same line
            //   a,     b;
            //    ^     ^
            //    |   start
            // fullstart
            // when b is replaced - we usually want to keep the leading trvia
            // when b is deleted - we delete it
            if (leadingOption == LeadingTriviaOption.IncludeAll)
            {
                return fullStart;
            }

            return start;
        }

        // if node has a trailing comments, use comment end position as the text has already been included.
        if (hasTrailingComment)
        {
            // Check first for leading comments as if the node is the first import, we want to exclude the trivia;
            // otherwise we get the trailing comments.
            var comments = GetLeadingCommentRanges(NodeFactory, sourceFile.Text(), fullStart).ToList();
            if (comments.Count == 0)
            {
                comments = GetTrailingCommentRanges(NodeFactory, sourceFile.Text(), fullStart).ToList();
            }

            if (comments.Count > 0)
            {
                return SkipTriviaEx(sourceFile.Text(), comments[0].Range.End(), new SkipTriviaOptions { StopAfterLineBreak = true, StopAtComments = true });
            }
        }

        // get start position of the line following the line that contains fullstart position
        // (but only if the fullstart isn't the very beginning of the file)
        var nextLineStart = IfElse(fullStart > 0, 1, 0);
        var adjustedStartPosition = (int)lineStarts[fullStartLineIndex + nextLineStart];
        // skip whitespaces/newlines
        adjustedStartPosition = SkipTriviaEx(sourceFile.Text(), adjustedStartPosition, new SkipTriviaOptions { StopAtComments = true });
        return lineStarts[ComputeLineOfPosition(lineStarts, adjustedStartPosition)];
    }

    // method on the changeTracker because of converters
    /// <summary>Return the end position of a multiline comment of it is on another line; otherwise returns `undefined`;</summary>
    internal int GetEndPositionOfMultilineTrailingComment(SourceFile sourceFile, Node node, TrailingTriviaOption trailingOpt)
    {
        if (trailingOpt == TrailingTriviaOption.Include)
        {
            // If the trailing comment is a multiline comment that extends to the next lines,
            // return the end of the comment and track it for the next nodes to adjust.
            var lineStarts = sourceFile.ECMALineMap();
            var nodeEndLine = ComputeLineOfPosition(lineStarts, node.End());
            foreach (var comment in GetTrailingCommentRanges(NodeFactory, sourceFile.Text(), node.End()))
            {
                // Single line can break the loop as trivia will only be this line.
                // Comments on subsequent lines are also ignored.
                if (comment.Kind == Kind.SingleLineCommentTrivia || ComputeLineOfPosition(lineStarts, comment.Range.Pos()) > nodeEndLine)
                {
                    break;
                }

                // Get the end line of the comment and compare against the end line of the node.
                // If the comment end line position and the multiline comment extends to multiple lines,
                // then is safe to return the end position.
                var commentEndLine = ComputeLineOfPosition(lineStarts, comment.Range.End());
                if (commentEndLine > nodeEndLine)
                {
                    return SkipTriviaEx(sourceFile.Text(), comment.Range.End(), new SkipTriviaOptions { StopAfterLineBreak = true, StopAtComments = true });
                }
            }
        }

        return 0;
    }

    // method on the changeTracker because of converters
    internal int GetAdjustedEndPosition(SourceFile sourceFile, Node node, TrailingTriviaOption trailingOption)
    {
        if (trailingOption == TrailingTriviaOption.Exclude)
        {
            return node.End();
        }

        if (trailingOption == TrailingTriviaOption.ExcludeWhitespace)
        {
            var comments = GetTrailingCommentRanges(NodeFactory, sourceFile.Text(), node.End())
                .Concat(GetLeadingCommentRanges(NodeFactory, sourceFile.Text(), node.End()))
                .ToList();
            if (comments.Count > 0)
            {
                var realEnd = comments[^1].Range.End();
                if (realEnd != 0)
                {
                    return realEnd;
                }
            }

            return node.End();
        }

        var multilineEndPosition = GetEndPositionOfMultilineTrailingComment(sourceFile, node, trailingOption);
        if (multilineEndPosition != 0)
        {
            return multilineEndPosition;
        }

        var newEnd = SkipTriviaEx(sourceFile.Text(), node.End(), new SkipTriviaOptions { StopAfterLineBreak = true });

        if (newEnd != node.End() && (trailingOption == TrailingTriviaOption.Include || IsLineBreak(sourceFile.Text()[newEnd - 1])))
        {
            return newEnd;
        }

        return node.End();
    }

    // ============= utilities =============

    internal static bool HasCommentsBeforeLineBreak(string text, int start)
    {
        foreach (var ch in text[start..])
        {
            if (!IsWhiteSpaceSingleLine(ch))
            {
                return ch == '/';
            }
        }

        return false;
    }

    internal static bool NeedSemicolonBetween(Node a, Node b)
    {
        return (IsPropertySignatureDeclaration(a) || IsPropertyDeclaration(a)) &&
            IsClassOrTypeElement(b) &&
            b.Name()!.Kind == Kind.ComputedPropertyName ||
            IsStatementButNotDeclaration(a) &&
                IsStatementButNotDeclaration(b); // TODO: only if b would start with a `(` or `[`
    }

    internal int GetInsertionPositionAtSourceFileTop(SourceFile sourceFile)
    {
        Node? lastPrologue = null;
        foreach (var node in sourceFile.Statements?.Nodes ?? [])
        {
            if (IsPrologueDirective(node))
            {
                lastPrologue = node;
            }
            else
            {
                break;
            }
        }

        var position = 0;
        var text = sourceFile.Text();
        void AdvancePastLineBreak()
        {
            if (position >= text.Length)
            {
                return;
            }

            var ch = text[position];
            if (IsLineBreak(ch))
            {
                position++;
                if (position < text.Length && ch == '\r' && text[position] == '\n')
                {
                    position++;
                }
            }
        }

        if (lastPrologue != null)
        {
            position = lastPrologue.End();
            AdvancePastLineBreak();
            return position;
        }

        var shebang = GetShebang(text);
        if (shebang != "")
        {
            position = shebang.Length;
            AdvancePastLineBreak();
        }

        var ranges = GetLeadingCommentRanges(NodeFactory, text, position).ToList();
        if (ranges.Count == 0)
        {
            return position;
        }

        // Find the first attached comment to the first node and add before it
        CommentRange? lastComment = null;
        var pinnedOrTripleSlash = false;
        var firstNodeLine = -1;

        var lenStatements = (sourceFile.Statements?.Nodes ?? []).Length;
        var lineMap = sourceFile.ECMALineMap();
        foreach (var r in ranges)
        {
            if (r.Kind == Kind.MultiLineCommentTrivia)
            {
                if (IsPinnedComment(text, r))
                {
                    lastComment = r;
                    pinnedOrTripleSlash = true;
                    continue;
                }
            }
            else if (IsRecognizedTripleSlashComment(text, r))
            {
                lastComment = r;
                pinnedOrTripleSlash = true;
                continue;
            }

            if (lastComment != null)
            {
                // Always insert after pinned or triple slash comments
                if (pinnedOrTripleSlash)
                {
                    break;
                }

                // There was a blank line between the last comment and this comment.
                // This comment is not part of the copyright comments
                var commentLine = ComputeLineOfPosition(lineMap, r.Range.Pos());
                var lastCommentEndLine = ComputeLineOfPosition(lineMap, lastComment.Range.End());
                if (commentLine >= lastCommentEndLine + 2)
                {
                    break;
                }
            }

            if (lenStatements > 0)
            {
                if (firstNodeLine == -1)
                {
                    firstNodeLine = ComputeLineOfPosition(lineMap, GetStartOfNode(sourceFile.Statements!.Nodes![0], sourceFile, false));
                }

                var commentEndLine = ComputeLineOfPosition(lineMap, r.Range.End());
                if (firstNodeLine < commentEndLine + 2)
                {
                    break;
                }
            }

            lastComment = r;
            pinnedOrTripleSlash = false;
        }

        if (lastComment != null)
        {
            position = lastComment.Range.End();
            AdvancePastLineBreak();
        }

        return position;
    }
}
