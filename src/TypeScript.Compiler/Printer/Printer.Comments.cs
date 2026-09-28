// ported from: internal/printer/printer.go:5291-5810（注释）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 返回 `*commentState`（arena 指针；nil = "没有"）→ `CommentState?`（Printer.cs 文件头第 9 条）；
//    `c := p.commentStateArena.New(); *c = commentState{…}` → `ref var c = ref _commentStateArena.New();
//    c = new CommentState { … }`（New() 返回 `ref T`，ref 局部不能跨 return，故最后按值拷贝出去）。
// 2) Go 的多返回值 `emitDetachedComments(textRange) (detachedCommentsInfo, bool)` → `TryEmitDetachedComments`
//    （`out DetachedCommentsInfo`），见 ticket 契约。
// 3) Go 的 `emitCommentsBeforeToken` 返回 `(*commentState, int)` → `(CommentState?, out int newPos)`。
// 4) Go 的 `append(comments, comment)` → C# `List<CommentRange>`；`slices.Collect(seq)` → 同样逐个 append。
//    Go 侧这些切片都是 `var comments []ast.CommentRange`（nil 起始）：**没有可写注释时零分配**；C# 原来无条件
//    `new List<CommentRange>()`（每个节点一次），现已改成 `(comments ??= new List<CommentRange>()).Add(...)` 的
//    懒分配（`EmitComments` 相应接受 null，nil 与空切片同义，docs §3.2/§3.8.1）。
// 5) `core.LastOrNil(comments)` → `comments[^1]`：调用点都已由前一行保证非空，而 `Core.LastOrNil` 只接受数组
//    （这里按偏离 4 用 List）。
// 6) Go 包级 `type commentSeparator uint32` + 3 个 iota 常量 → 顶层 `internal enum CommentSeparator : uint`，
//    成员名去掉类型前缀（与 WriteKind/ListFormat 一样，Go 包级类型落成顶层声明）。
// 7) Go 的无表达式 `switch { case forceNoNewline: …; case comment.HasTrailingNewLine: …; default: … }` → `if/else if/else`
//    （Go 的 `default:` 本来就在最后，不需要挪位）。
// 8) Go 直接解引用 `p.writer` / `p.currentSourceFile` → C# 侧 `_writer`（声明为 `null!`）与 `_currentSourceFile`
//    （调用点已判空，与 Go 的 nil 检查一一对应）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Printer;

/// <summary>Go: <c>type commentSeparator uint32</c>（包级类型；成员名去掉 <c>commentSeparator</c> 前缀）。</summary>
internal enum CommentSeparator : uint
{
    None = 0,
    Before = 1,
    After = 2,
}

public sealed partial class Printer
{
    internal CommentState? EmitCommentsBeforeNode(Node node)
    {
        if (!ShouldEmitComments(node))
        {
            return null;
        }

        var emitFlags = _emitContext.EmitFlags(node);
        var commentRange = _emitContext.CommentRange(node);
        var containerPos = _containerPos;
        var containerEnd = _containerEnd;
        var declarationListContainerEnd = _declarationListContainerEnd;

        // Emit leading comments
        EmitLeadingCommentsOfNode(node, emitFlags, commentRange);
        EmitLeadingSyntheticCommentsOfNode(node, emitFlags);
        if ((emitFlags & EmitFlags.NoNestedComments) != 0)
        {
            _commentsDisabled = true;
        }

        ref var c = ref _commentStateArena.New();
        c = new CommentState
        {
            EmitFlags = emitFlags,
            CommentRange = commentRange,
            ContainerPos = containerPos,
            ContainerEnd = containerEnd,
            DeclarationListContainerEnd = declarationListContainerEnd,
        };
        return c;
    }

    internal void EmitCommentsAfterNode(Node node, CommentState? state)
    {
        if (state is null)
        {
            return;
        }

        var stateValue = state.Value; // Go 通过指针读，C# 取一次副本再读字段（等价，避免多次拷结构体）
        var emitFlags = stateValue.EmitFlags;
        var commentRange = stateValue.CommentRange;
        var containerPos = stateValue.ContainerPos;
        var containerEnd = stateValue.ContainerEnd;
        var declarationListContainerEnd = stateValue.DeclarationListContainerEnd;

        // Emit trailing comments
        if ((emitFlags & EmitFlags.NoNestedComments) != 0)
        {
            _commentsDisabled = false;
        }

        EmitTrailingSyntheticCommentsOfNode(node, emitFlags);
        EmitTrailingCommentsOfNode(node, emitFlags, commentRange, containerPos, containerEnd, declarationListContainerEnd);

        // Preserve comments from erased type annotation
        var typeNode = _emitContext.GetTypeNode(node);
        if (typeNode is not null)
        {
            EmitTrailingCommentsOfNode(node, emitFlags, typeNode.Loc, containerPos, containerEnd, declarationListContainerEnd);
        }
    }

    internal CommentState? EmitCommentsBeforeToken(Kind token, int pos, Node contextNode, TokenEmitFlags flags, out int newPos)
    {
        if ((flags & TokenEmitFlags.NoComments) != 0 || _commentsDisabled)
        {
            // Still skip trivia so that the returned pos correctly identifies the token position.
            // This is needed for trailing source map positions (writeTokenText advances pos by token length).
            if (_currentSourceFile is not null && !PositionIsSynthesized(pos))
            {
                pos = SkipTrivia(_currentSourceFile.Text(), pos);
            }

            newPos = pos;
            return null;
        }

        var startPos = pos;
        if (_currentSourceFile is not null)
        {
            pos = SkipTrivia(_currentSourceFile.Text(), startPos);
        }

        var node = _emitContext.ParseNode(contextNode);
        var isSimilarNode = node is not null && node.Kind == contextNode.Kind;
        if (!isSimilarNode)
        {
            newPos = pos;
            return null;
        }

        if (contextNode.Pos() != startPos)
        {
            var indentLeading = (flags & TokenEmitFlags.IndentLeadingComments) != 0;
            var needsIndent = indentLeading && _currentSourceFile is not null && !PositionsAreOnSameLine(startPos, pos, _currentSourceFile);
            IncreaseIndentIf(needsIndent);
            EmitLeadingComments(startPos, elided: false);
            DecreaseIndentIf(needsIndent);
        }

        newPos = pos;
        return _commentStateArena.New();
    }

    internal void EmitCommentsAfterToken(Kind token, int pos, Node contextNode, CommentState? state)
    {
        if (state is null)
        {
            return;
        }

        if (contextNode.End() != pos)
        {
            var isJsxExprContext = contextNode.Kind == Kind.JsxExpression;
            EmitTrailingComments(pos, IfElse(isJsxExprContext, CommentSeparator.None, CommentSeparator.Before));
        }
    }

    internal CommentState? EmitDetachedCommentsBeforeStatementList(Node node, TextRange detachedRange)
    {
        if (!ShouldEmitDetachedComments(node))
        {
            return null;
        }

        var emitFlags = _emitContext.EmitFlags(node);
        var containerPos = _containerPos;
        var containerEnd = _containerEnd;
        var declarationListContainerEnd = _declarationListContainerEnd;
        var skipLeadingComments = PositionIsSynthesized(detachedRange.Pos()) || (emitFlags & EmitFlags.NoLeadingComments) != 0;

        if (!skipLeadingComments)
        {
            EmitDetachedCommentsAndUpdateCommentsInfo(detachedRange);
        }

        if ((emitFlags & EmitFlags.NoNestedComments) != 0)
        {
            _commentsDisabled = true;
        }

        return new CommentState
        {
            EmitFlags = emitFlags,
            CommentRange = detachedRange,
            ContainerPos = containerPos,
            ContainerEnd = containerEnd,
            DeclarationListContainerEnd = declarationListContainerEnd,
        };
    }

    internal void EmitDetachedCommentsAfterStatementList(Node node, TextRange detachedRange, CommentState? state)
    {
        if (state is null)
        {
            return;
        }

        var emitFlags = state.Value.EmitFlags; // 只读一次（Go 通过指针读）
        var skipTrailingComments = _commentsDisabled || PositionIsSynthesized(detachedRange.End()) || (emitFlags & EmitFlags.NoTrailingComments) != 0;

        if (!skipTrailingComments)
        {
            var hasWrittenComment = EmitLeadingComments(detachedRange.End(), elided: false);
            if (hasWrittenComment && !_writer.IsAtStartOfLine())
            {
                WriteLine();
            }
        }
    }

    internal void EmitLeadingCommentsOfNode(Node node, EmitFlags emitFlags, TextRange commentRange)
    {
        var pos = commentRange.Pos();
        var end = commentRange.End();

        // Save current container state on the stack.
        if ((!PositionIsSynthesized(pos) || !PositionIsSynthesized(end)) && pos != end)
        {
            // We have to explicitly check that the node is JsxText because if the compilerOptions.jsx is "preserve" we will not do any transformation.
            // It is expensive to walk entire tree just to set one kind of node to have no comments.
            var skipLeadingComments = PositionIsSynthesized(pos) || (emitFlags & EmitFlags.NoLeadingComments) != 0 || node.Kind == Kind.JsxText;
            var skipTrailingComments = PositionIsSynthesized(end) || (emitFlags & EmitFlags.NoTrailingComments) != 0 || node.Kind == Kind.JsxText;

            // Emit leading comments if the position is not synthesized and the node
            // has not opted out from emitting leading comments.
            if (!skipLeadingComments)
            {
                EmitLeadingComments(pos, node.Kind == Kind.NotEmittedStatement /*elided*/);
            }

            if (!skipLeadingComments || (pos >= 0 && (emitFlags & EmitFlags.NoLeadingComments) != 0))
            {
                // Advance the container position if comments get emitted or if they've been disabled explicitly using NoLeadingComments.
                _containerPos = pos;
            }

            if (!skipTrailingComments || (end >= 0 && (emitFlags & EmitFlags.NoTrailingComments) != 0))
            {
                // Advance the container end if comments get emitted or if they've been disabled explicitly using NoTrailingComments.
                _containerEnd = end;

                // To avoid invalid comment emit in a down-level binding pattern, we
                // keep track of the last declaration list container's end
                if (node.Kind == Kind.VariableDeclarationList)
                {
                    _declarationListContainerEnd = end;
                }
            }
        }
    }

    internal void EmitTrailingCommentsOfNode(Node node, EmitFlags emitFlags, TextRange commentRange, int containerPos, int containerEnd, int declarationListContainerEnd)
    {
        var pos = commentRange.Pos();
        var end = commentRange.End();
        var skipTrailingComments = end < 0 || (emitFlags & EmitFlags.NoTrailingComments) != 0 || node.Kind == Kind.JsxText;
        if ((!PositionIsSynthesized(pos) || !PositionIsSynthesized(end)) && pos != end)
        {
            // Restore previous container state.
            _containerPos = containerPos;
            _containerEnd = containerEnd;
            _declarationListContainerEnd = declarationListContainerEnd;

            // Emit trailing comments if the position is not synthesized and the node
            // has not opted out from emitting leading comments and is an emitted node.
            if (!skipTrailingComments && node.Kind != Kind.NotEmittedStatement)
            {
                EmitTrailingComments(end, CommentSeparator.Before);
            }
        }
    }

    internal void EmitLeadingSyntheticCommentsOfNode(Node node, EmitFlags emitFlags)
    {
        if ((emitFlags & EmitFlags.NoLeadingComments) != 0)
        {
            return;
        }
        var synth = _emitContext.GetSyntheticLeadingComments(node);
        // Go 的 nil 切片 range 不迭代 → C# 判空等价（文件头第 4 条）。
        if (synth is not null)
        {
            foreach (var c in synth)
            {
                EmitLeadingSynthesizedComment(c);
            }
        }
    }

    internal void EmitLeadingSynthesizedComment(SynthesizedComment comment)
    {
        if (comment.HasLeadingNewLine || comment.Kind == Kind.SingleLineCommentTrivia)
        {
            _writer.WriteLine();
        }
        WriteSynthesizedComment(comment);
        if (comment.HasTrailingNewLine || comment.Kind == Kind.SingleLineCommentTrivia)
        {
            _writer.WriteLine();
        }
        else
        {
            _writer.WriteSpace(" ");
        }
    }

    internal void EmitTrailingSyntheticCommentsOfNode(Node node, EmitFlags emitFlags)
    {
        if ((emitFlags & EmitFlags.NoTrailingComments) != 0)
        {
            return;
        }
        var synth = _emitContext.GetSyntheticTrailingComments(node);
        // Go 的 nil 切片 range 不迭代 → C# 判空等价（文件头第 4 条）。
        if (synth is not null)
        {
            foreach (var c in synth)
            {
                EmitTrailingSynthesizedComment(c);
            }
        }
    }

    internal void EmitTrailingSynthesizedComment(SynthesizedComment comment)
    {
        if (!_writer.IsAtStartOfLine())
        {
            _writer.WriteSpace(" ");
        }
        WriteSynthesizedComment(comment);
        if (comment.HasTrailingNewLine)
        {
            _writer.WriteLine();
        }
    }

    internal static string FormatSynthesizedComment(SynthesizedComment comment)
    {
        if (comment.Kind == Kind.MultiLineCommentTrivia)
        {
            return "/*" + comment.Text + "*/";
        }
        return "//" + comment.Text;
    }

    internal void WriteSynthesizedComment(SynthesizedComment comment)
    {
        var text = FormatSynthesizedComment(comment);
        TextPos[]? lineMap = null;
        if (comment.Kind == Kind.MultiLineCommentTrivia)
        {
            lineMap = ComputeECMALineStarts(text);
        }
        WriteCommentRangeWorker(text, lineMap, comment.Kind, NewTextRange(0, text.Length));
    }

    internal bool EmitLeadingComments(int pos, bool elided)
    {
        // Emit the leading comments only if the container's pos doesn't match because the container should take care of emitting these comments
        if (_commentsDisabled || _currentSourceFile is null || PositionIsSynthesized(pos) || pos == _containerPos)
        {
            return false;
        }

        var tripleSlash = Tristate.TSUnknown;
        if (!elided)
        {
            if (pos == 0 && _currentSourceFile is not null && _currentSourceFile.IsDeclarationFile)
            {
                tripleSlash = Tristate.TSFalse;
            }
        }
        else if (pos == 0)
        {
            // If the node will not be emitted in JS, remove all the comments(normal, pinned and ///) associated with the node,
            // unless it is a triple slash comment at the top of the file.
            // For Example:
            //      /// <reference-path ...>
            //      declare var x;
            //      /// <reference-path ...>
            //      interface F {}
            //  The first /// will NOT be removed while the second one will be removed even though both node will not be emitted
            tripleSlash = Tristate.TSTrue;
        }
        else
        {
            return false;
        }

        // skip detached comments
        if (_detachedCommentsInfo.Len() > 0)
        {
            var info = _detachedCommentsInfo.Peek();
            if (info.NodePos == pos)
            {
                pos = _detachedCommentsInfo.Pop().DetachedCommentEndPos;
            }
        }

        // Go: `var comments []ast.CommentRange`（printer.go:5563，nil 切片 + append）——没有可写注释时**不分配**；
        // C# 原来无条件 `new List<CommentRange>()`，每个节点一次（docs §3.8.1）。
        List<CommentRange>? comments = null;
        // Go: p.currentSourceFile.Text() —— 前面的守卫已 return（currentSourceFile == nil），Go 直接解引用，故 `!`。
        foreach (var comment in GetLeadingCommentRanges(_emitContext.Factory.AsNodeFactory(), _currentSourceFile!.Text(), pos))
        {
            if (ShouldWriteComment(comment) && ShouldEmitCommentIfTripleSlash(comment, tripleSlash))
            {
                (comments ??= new List<CommentRange>()).Add(comment);
            }
        }

        if (comments is { Count: > 0 } && ShouldEmitNewLineBeforeLeadingCommentOfPosition(pos, comments[0].Range.Pos()))
        {
            WriteLine();
        }

        // Leading comments are emitted as /*leading comment1*/space/*leading comment*/space
        return EmitComments(comments, CommentSeparator.After);
    }

    internal bool ShouldEmitCommentIfTripleSlash(CommentRange comment, Tristate tripleSlash)
    {
        switch (tripleSlash)
        {
            case Tristate.TSTrue:
                return IsTripleSlashComment(comment);
            case Tristate.TSFalse:
                return !IsTripleSlashComment(comment);
            default: // Go 的 `default:`（位置不变，本来就在最后）
                return true;
        }
    }

    internal bool ShouldEmitNewLineBeforeLeadingCommentOfPosition(int pos, int commentPos)
    {
        // If the leading comments start on different line than the start of node, write new line
        return _currentSourceFile is not null &&
            pos != commentPos &&
            ComputeLineOfPosition(_currentSourceFile.ECMALineMap(), pos) != ComputeLineOfPosition(_currentSourceFile.ECMALineMap(), commentPos);
    }

    internal void EmitLeadingCommentsOfPosition(int pos)
    {
        if (_commentsDisabled || pos == -1)
        {
            return;
        }

        EmitLeadingComments(pos, elided: false);
    }

    internal void EmitTrailingComments(int pos, CommentSeparator commentSeparator)
    {
        if (_commentsDisabled)
        {
            return;
        }
        // Emit the trailing comments only if the container's end doesn't match because the container should take care of emitting these comments
        if (_commentsDisabled || _currentSourceFile is null || _containerEnd != -1 && (pos == _containerEnd || pos == _declarationListContainerEnd))
        {
            return;
        }

        // Go: `var comments []ast.CommentRange`（printer.go:5613，nil 切片 + append）——同 emitLeadingComments。
        List<CommentRange>? comments = null;
        foreach (var comment in GetTrailingCommentRanges(_emitContext.Factory.AsNodeFactory(), _currentSourceFile.Text(), pos))
        {
            if (ShouldWriteComment(comment))
            {
                (comments ??= new List<CommentRange>()).Add(comment);
            }
        }

        // trailing comments are normally emitted as space/*trailing comment1*/space/*trailing comment2*/
        _ = EmitComments(comments, commentSeparator); // Go 丢弃返回值（`emitComments` 是语句调用）
    }

    internal void EmitTrailingCommentsOfPosition(int pos, bool prefixSpace, bool forceNoNewline)
    {
        if (_commentsDisabled || _currentSourceFile is null)
        {
            return;
        }
        if (_containerEnd != -1 && (pos == _containerEnd || pos == _declarationListContainerEnd))
        {
            return;
        }

        // Go: printer.go:5632 `var comments []ast.CommentRange` + append（每个注释都 append）——空序列时 Go 不分配。
        List<CommentRange>? comments = null;
        foreach (var comment in GetTrailingCommentRanges(_emitContext.Factory.AsNodeFactory(), _currentSourceFile.Text(), pos))
        {
            (comments ??= new List<CommentRange>()).Add(comment);
        }
        if (comments is null)
        {
            return;
        }

        foreach (var comment in comments)
        {
            if (prefixSpace)
            {
                if (!ShouldWriteComment(comment))
                {
                    continue;
                }
                if (!_writer.IsAtStartOfLine())
                {
                    WriteSpace();
                }
                EmitComment(comment);
                if (comment.HasTrailingNewLine)
                {
                    WriteLine();
                }
                continue;
            }

            EmitComment(comment);
            if (forceNoNewline)
            {
                if (comment.Kind == Kind.SingleLineCommentTrivia)
                {
                    WriteLine();
                }
            }
            else if (comment.HasTrailingNewLine)
            {
                WriteLine();
            }
            else
            {
                WriteSpace();
            }
        }
    }

    internal void EmitDetachedCommentsAndUpdateCommentsInfo(TextRange textRange)
    {
        if (_currentSourceFile is null)
        {
            return;
        }
        if (TryEmitDetachedComments(textRange, out var currentDetachedCommentInfo))
        {
            _detachedCommentsInfo.Push(currentDetachedCommentInfo);
        }
    }

    internal bool TryEmitDetachedComments(TextRange textRange, out DetachedCommentsInfo result)
    {
        result = default;
        if (_currentSourceFile is null)
        {
            return false;
        }

        var text = _currentSourceFile.Text();
        var lineMap = _currentSourceFile.ECMALineMap();

        // Go: printer.go:5686 `var leadingComments []ast.CommentRange`（nil 切片；`slices.Collect` 空序列也返回 nil）
        // —— 没有注释时不分配（docs §3.8.1）。
        List<CommentRange>? leadingComments = null;
        if (_commentsDisabled)
        {
            // removeComments is true, only reserve pinned comment at the top of file
            // For example:
            //      /*! Pinned Comment */
            //
            //      var x = 10;
            if (textRange.Pos() == 0)
            {
                foreach (var comment in GetLeadingCommentRanges(_emitContext.Factory.AsNodeFactory(), text, textRange.Pos()))
                {
                    if (IsPinnedComment(text, comment))
                    {
                        (leadingComments ??= new List<CommentRange>()).Add(comment);
                    }
                }
            }
        }
        else
        {
            // removeComments is false, just get detached as normal and bypass the process to filter comment
            // Go: `slices.Collect(seq)` —— 逐个 append（空序列得到 nil 切片）。
            foreach (var comment in GetLeadingCommentRanges(_emitContext.Factory.AsNodeFactory(), text, textRange.Pos()))
            {
                (leadingComments ??= new List<CommentRange>()).Add(comment);
            }
        }

        if (leadingComments is { Count: > 0 })
        {
            var detachedComments = new List<CommentRange>();
            CommentRange? lastComment = null;
            for (var i = 0; i < leadingComments.Count; i++)
            {
                var comment = leadingComments[i];
                if (i > 0)
                {
                    var lastCommentLine = ComputeLineOfPosition(lineMap, lastComment!.Range.End());
                    var commentLine = ComputeLineOfPosition(lineMap, comment.Range.Pos());

                    if (commentLine >= lastCommentLine + 2)
                    {
                        // There was a blank line between the last comment and this comment.  This
                        // comment is not part of the copyright comments.  Return what we have so
                        // far.
                        break;
                    }
                }

                detachedComments.Add(comment);
                lastComment = comment;
            }

            if (detachedComments.Count > 0)
            {
                // All comments look like they could have been part of the copyright header.  Make
                // sure there is at least one blank line between it and the node.  If not, it's not
                // a copyright header.
                var lastCommentLine = ComputeLineOfPosition(lineMap, detachedComments[^1].Range.End());
                var nodeLine = ComputeLineOfPosition(lineMap, SkipTrivia(text, textRange.Pos()));
                if (nodeLine >= lastCommentLine + 2)
                {
                    // Valid detachedComments

                    // Filter to only comments that should be written (e.g., JSDoc-style in declaration emit)
                    // Go: printer.go:5706 `var commentsToEmit []ast.CommentRange` + 条件 append —— 无内容时不分配。
                    List<CommentRange>? commentsToEmit = null;
                    foreach (var comment in detachedComments)
                    {
                        if (ShouldWriteComment(comment))
                        {
                            (commentsToEmit ??= new List<CommentRange>()).Add(comment);
                        }
                    }

                    if (commentsToEmit is not null)
                    {
                        if (ShouldEmitNewLineBeforeLeadingCommentOfPosition(textRange.Pos(), commentsToEmit[0].Range.Pos()))
                        {
                            WriteLine();
                        }

                        EmitComments(commentsToEmit, CommentSeparator.After);
                    }
                    result = new DetachedCommentsInfo { NodePos = textRange.Pos(), DetachedCommentEndPos = detachedComments[^1].Range.End() };
                    return true;
                }
            }
        }
        return false;
    }

    internal bool EmitComments(List<CommentRange>? comments, CommentSeparator commentSeparator)
    {
        var interveningSeparator = false;
        // Go: `if len(comments) == 0 { return false }`（printer.go:5767）——nil 与空切片同义（docs §3.2）。
        if (comments is null || comments.Count == 0)
        {
            return false;
        }

        if (commentSeparator == CommentSeparator.Before)
        {
            WriteSpace();
        }

        foreach (var comment in comments)
        {
            if (interveningSeparator)
            {
                WriteSpace();
                interveningSeparator = false;
            }

            EmitComment(comment);

            if (comment.Kind == Kind.SingleLineCommentTrivia || comment.HasTrailingNewLine && commentSeparator != CommentSeparator.None)
            {
                WriteLine();
            }
            else
            {
                interveningSeparator = commentSeparator != CommentSeparator.None;
            }
        }

        if (interveningSeparator && commentSeparator == CommentSeparator.After)
        {
            WriteSpace();
        }

        return true;
    }

    internal void EmitComment(CommentRange comment)
    {
        EmitPos(comment.Range.Pos());
        WriteCommentRange(comment);
        EmitPos(comment.Range.End());
    }

    internal bool IsTripleSlashComment(CommentRange comment)
    {
        return _currentSourceFile is not null &&
            IsRecognizedTripleSlashComment(_currentSourceFile.Text(), comment);
    }
}
