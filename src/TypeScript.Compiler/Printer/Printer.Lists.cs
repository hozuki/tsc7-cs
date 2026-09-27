// ported from: internal/printer/printer.go:4726-5043（列表写出）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `emit func(p *Printer, node *ast.Node)` 方法值 → C# 的 `NodeEmitter` 委托（本文件声明一次，
//    供全部列表分片共用）；Go 的 `p.emitList((*Printer).emitKeywordNode, …)` → C# 的方法组
//    `EmitList(EmitKeywordNode, …)`。
// 2) Go 在 `*ast.NodeList` 上做 `switch children { case parentNode.TypeArgumentList(): … }`（指针相等分支）→
//    C# 的 case 标签必须是编译期常量，故落成 `if`/`else if` 链；NodeList 没有重载 `==`，`==` 即引用相等，
//    与 Go 的指针比较同义（docs §3.2：Go 的 *T 与 C# class 同构）。
// 3) `children.Nodes` 在 C# 里是 `Node[]?`（Go 的 nil 切片与空切片同义，`len(nil) == 0`）→
//    `children.Nodes?.Length ?? 0`，且只在 `children` 非 nil 时求值，与 Go 的 `len(children.Nodes)` 一致。
// 4) Go 的 `children.Nodes[start:end]`（零拷贝切片视图）→ C# `children.Nodes![start..end]`（数组 range 会复制一次；
//    `EmitListItems` 的契约签名要求 `Node[]`）。
// 5) `greatestEnd(-1, parentNode)`：Go 的 `tryGetEnd(*ast.Node)` 分支就是 `node.End()`，而 C# 的
//    `Node.End() => Loc.End()` → 直接写 `GreatestEnd(-1, parentNode.Loc)`，不为 `Node` 另加重载。
// 6) Go 的 `skipTrailingComments := p.commentsDisabled || !p.shouldEmitTrailingComments(previousSibling)`
//    可能传入 nil（`EmitFlags(nil)` 返回不含 `NoTrailingComments` 的 flags）；C# 的 `EmitFlags(Node)` /
//    `ShouldEmitTrailingComments(Node)` 参数不可空 → 把 nil 判断折进表达式
//    `previousSibling is null || !ShouldEmitTrailingComments(previousSibling)`。该差异不可观测：
//    下游用到 `skipTrailingComments` 的分支都被 `previousSibling != nil` 短路。
// 7) `for range count` → `for (var i = 0; i < count; i++)`。
// 8) `_emitContext.MostOriginal` 在 C# 返回 `Node?`；Go 的 `originalParent.Kind`（nil 会 panic）→ `originalParent!`。
// 9) Go 的 `min(start+count, length)` → `Math.Min(start + count, length)`。
// 10) `case LFNone:` → `case ListFormat.None:`（Go 里 `LFNone` 与 `LFNotDelimited` 同为 0）。
using System;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    /// <summary>Go: <c>emit func(p *Printer, node *ast.Node)</c>（作为参数传递的节点发射器；全仓只声明一次）。</summary>
    internal delegate void NodeEmitter(Node node);

    internal void EmitList(NodeEmitter emit, Node parentNode, NodeList? children, ListFormat format)
    {
        if (ShouldEmitOnMultipleLines(parentNode))
        {
            format |= ListFormat.PreferNewLine | ListFormat.Indented;
        }

        EmitListRange(emit, parentNode, children, format, -1 /*start*/, -1 /*count*/);
    }

    internal void EmitListRange(NodeEmitter emit, Node parentNode, NodeList? children, ListFormat format, int start, int count)
    {
        var isNil = children is null;

        var length = 0;
        if (!isNil)
        {
            length = children!.Nodes?.Length ?? 0;
        }

        if (start < 0)
        {
            start = 0;
        }

        if (count < 0)
        {
            count = length - start;
        }

        if (isNil && (format & ListFormat.OptionalIfNil) != 0)
        {
            return;
        }

        var isEmpty = isNil || start >= length || count <= 0;
        if (isEmpty && (format & ListFormat.OptionalIfEmpty) != 0)
        {
            OnBeforeEmitNodeList?.Invoke(children);
            OnAfterEmitNodeList?.Invoke(children);
            return;
        }

        if ((format & ListFormat.BracketsMask) != 0)
        {
            WritePunctuation(GetOpeningBracket(format));
            if (isEmpty && !isNil)
            {
                EmitTrailingComments(children!.Pos(), CommentSeparator.Before); // Emit comments within empty lists
            }
        }

        OnBeforeEmitNodeList?.Invoke(children);

        if (isEmpty)
        {
            // Write a line terminator if the parent node was multi-line
            // Go 的 `parentNode == nil` 判断照抄（C# 契约里 parentNode 非空，此分支为 Go 的防御性写法）；
            // `_currentSourceFile!`：Go 在 printer.go:4778 已先判 `p.currentSourceFile != nil`，emit 期间来源文件必然已设置，
            // nil 即 Go 的 panic（跨分片契约：`RangeIsOnSingleLine(TextRange, SourceFile)`，第二参数非可空）。
            if ((format & ListFormat.MultiLine) != 0 && !(Options.PreserveSourceNewlines && (parentNode is null || RangeIsOnSingleLine(parentNode.Loc, _currentSourceFile!))))
            {
                WriteLine();
            }
            else if ((format & ListFormat.SpaceBetweenBraces) != 0 && (format & ListFormat.NoSpaceIfEmpty) == 0)
            {
                WriteSpace();
            }
        }
        else
        {
            var end = Math.Min(start + count, length);

            EmitListItems(emit, parentNode, (children!.Nodes ?? [])[start..end], format, HasTrailingComma(parentNode, children), children.Loc);
        }

        OnAfterEmitNodeList?.Invoke(children);

        if ((format & ListFormat.BracketsMask) != 0)
        {
            if (isEmpty && !isNil)
            {
                EmitLeadingComments(children!.End(), false /*elided*/); // Emit comments within empty lists
            }
            WritePunctuation(GetClosingBracket(format));
        }
    }

    internal bool HasTrailingComma(Node parentNode, NodeList children)
    {
        // NodeList.HasTrailingComma() is unreliable on transformed nodes as some nodes may have been removed. In the event
        // we believe we may need to emit a trailing comma, we must first look to the respective node list on the original
        // node first.
        if (!children.HasTrailingComma())
        {
            return false;
        }

        var originalParent = _emitContext.MostOriginal(parentNode);
        if (originalParent == parentNode)
        {
            // if this node is the original node, we can trust the result
            return true;
        }

        if (originalParent!.Kind != parentNode.Kind)
        {
            // if the original node is some other kind of node, we cannot correlate the list
            return false;
        }

        // find the respective node list on the original parent
        NodeList? originalList = children;
        switch (originalParent.Kind)
        {
            case Kind.ObjectLiteralExpression:
                originalList = originalParent.PropertyList();
                break;
            case Kind.ArrayLiteralExpression:
                originalList = originalParent.ElementList();
                break;
            case Kind.CallExpression or Kind.NewExpression:
                // Go: switch children { case parentNode.TypeArgumentList(): … }（NodeList 指针相等）
                if (children == parentNode.TypeArgumentList())
                {
                    originalList = originalParent.TypeArgumentList();
                }
                else if (children == parentNode.ArgumentList())
                {
                    originalList = originalParent.ArgumentList();
                }
                break;
            case Kind.Constructor
                or Kind.MethodDeclaration
                or Kind.GetAccessor
                or Kind.SetAccessor
                or Kind.FunctionDeclaration
                or Kind.FunctionExpression
                or Kind.ArrowFunction
                or Kind.FunctionType
                or Kind.ConstructorType
                or Kind.CallSignature
                or Kind.ConstructSignature:
                if (children == parentNode.TypeParameterList())
                {
                    originalList = originalParent.TypeParameterList();
                }
                else if (children == parentNode.ParameterList())
                {
                    originalList = originalParent.ParameterList();
                }
                break;
            case Kind.ClassDeclaration
                or Kind.ClassExpression
                or Kind.InterfaceDeclaration
                or Kind.TypeAliasDeclaration
                or Kind.JSTypeAliasDeclaration:
                if (children == parentNode.TypeParameterList())
                {
                    originalList = originalParent.TypeParameterList();
                }
                break;
            case Kind.ObjectBindingPattern or Kind.ArrayBindingPattern:
                if (children == parentNode.ElementList())
                {
                    originalList = originalParent.ElementList();
                }
                break;
            case Kind.NamedImports or Kind.NamedExports:
                originalList = originalParent.ElementList();
                break;
            case Kind.ImportAttributes:
                originalList = originalParent.AsImportAttributes()!.Attributes;
                break;
        }

        // if we have the original list, we can use it's result.
        if (originalList is not null)
        {
            return originalList.HasTrailingComma();
        }

        return false;
    }

    internal void WriteDelimiter(ListFormat format)
    {
        switch (format & ListFormat.DelimitersMask)
        {
            case ListFormat.None:
                // no delimiter for this format
                break;
            case ListFormat.CommaDelimited:
                WritePunctuation(",");
                break;
            case ListFormat.BarDelimited:
                WriteSpace();
                WritePunctuation("|");
                break;
            case ListFormat.AsteriskDelimited:
                WriteSpace();
                WritePunctuation("*");
                WriteSpace();
                break;
            case ListFormat.AmpersandDelimited:
                WriteSpace();
                WritePunctuation("&");
                break;
        }
    }

    // Emits a list without brackets or raising events.
    //
    // NOTE: You probably don't want to call this directly and should be using `emitList` instead.
    internal void EmitListItems(
        NodeEmitter emit,
        Node parentNode,
        Node[] children,
        ListFormat format,
        bool hasTrailingComma,
        TextRange childrenTextRange)
    {
        // Write the opening line terminator or leading whitespace.
        var mayEmitInterveningComments = (format & ListFormat.NoInterveningComments) == 0;
        var shouldEmitInterveningComments = mayEmitInterveningComments;

        var leadingLineTerminatorCount = 0;
        if (children.Length > 0)
        {
            leadingLineTerminatorCount = GetLeadingLineTerminatorCount(parentNode, children[0], format);
        }
        if (leadingLineTerminatorCount > 0)
        {
            for (var i = 0; i < leadingLineTerminatorCount; i++)
            {
                WriteLine();
            }
            shouldEmitInterveningComments = false;
        }
        else if ((format & ListFormat.SpaceBetweenBraces) != 0)
        {
            WriteSpace();
        }

        // Increase the indent, if requested.
        if ((format & ListFormat.Indented) != 0)
        {
            IncreaseIndent();
        }

        var parentEnd = GreatestEnd(-1, parentNode.Loc); // Go: greatestEnd(-1, parentNode)

        // Emit each child.
        Node? previousSibling = null;
        var shouldDecreaseIndentAfterEmit = false;
        foreach (var child in children)
        {
            // Write the delimiter if this is not the first node.
            if ((format & ListFormat.AsteriskDelimited) != 0)
            {
                // always write JSDoc in the format "\n *"
                WriteLine();
                WriteDelimiter(format);
            }
            else if (previousSibling is not null)
            {
                // i.e
                //      function commentedParameters(
                //          /* Parameter a */
                //          a
                //          /* End of parameter a */ -> this comment isn't considered to be trailing comment of parameter "a" due to newline
                //          ,
                if ((format & ListFormat.DelimitersMask) != 0 && previousSibling.End() != parentEnd)
                {
                    if (!_commentsDisabled && ShouldEmitTrailingComments(previousSibling))
                    {
                        EmitLeadingComments(previousSibling.End(), false /*elided*/);
                    }
                }

                WriteDelimiter(format);

                // Write either a line terminator or whitespace to separate the elements.
                var separatingLineTerminatorCount = GetSeparatingLineTerminatorCount(previousSibling, child, format);
                if (separatingLineTerminatorCount > 0)
                {
                    // If a synthesized node in a single-line list starts on a new
                    // line, we should increase the indent.
                    if ((format & (ListFormat.LinesMask | ListFormat.Indented)) == ListFormat.SingleLine)
                    {
                        IncreaseIndent();
                        shouldDecreaseIndentAfterEmit = true;
                    }

                    if (shouldEmitInterveningComments && (format & ListFormat.DelimitersMask) != 0 && !PositionIsSynthesized(child.Pos()) && ShouldEmitLeadingComments(child))
                    {
                        var commentRange = _emitContext.CommentRange(child);
                        EmitTrailingCommentsOfPosition(commentRange.Pos(), (format & ListFormat.SpaceBetweenSiblings) != 0, true /*forceNoNewline*/);
                    }

                    for (var i = 0; i < separatingLineTerminatorCount; i++)
                    {
                        WriteLine();
                    }

                    shouldEmitInterveningComments = false;
                }
                else if ((format & ListFormat.SpaceBetweenSiblings) != 0)
                {
                    WriteSpace();
                }
            }

            // Emit this child.
            if (shouldEmitInterveningComments && ShouldEmitLeadingComments(child))
            {
                var commentRange = _emitContext.CommentRange(child);
                EmitTrailingCommentsOfPosition(commentRange.Pos(), false /*prefixSpace*/, false /*forceNoNewline*/);
            }
            else
            {
                shouldEmitInterveningComments = mayEmitInterveningComments;
            }

            _nextListElementPos = child.Pos();
            emit(child);

            if (shouldDecreaseIndentAfterEmit)
            {
                DecreaseIndent();
                shouldDecreaseIndentAfterEmit = false;
            }

            previousSibling = child;
        }

        // Write a trailing comma, if requested.
        var skipTrailingComments = _commentsDisabled || previousSibling is null || !ShouldEmitTrailingComments(previousSibling);
        var emitTrailingComma = hasTrailingComma && (format & ListFormat.AllowTrailingComma) != 0 && (format & ListFormat.CommaDelimited) != 0;
        if (emitTrailingComma)
        {
            if (previousSibling is not null && !skipTrailingComments)
            {
                EmitToken(Kind.CommaToken, previousSibling.End(), WriteKind.Punctuation, previousSibling);
            }
            else
            {
                WritePunctuation(",");
            }
        }

        // Emit any trailing comment of the last element in the list
        // i.e
        //       var array = [...
        //          2
        //          /* end of element 2 */
        //       ];
        if (previousSibling is not null && parentEnd != previousSibling.End() && (format & ListFormat.DelimitersMask) != 0 && !skipTrailingComments)
        {
            int commentsPos;
            if (emitTrailingComma && childrenTextRange.End() > 0)
            {
                commentsPos = childrenTextRange.End();
            }
            else
            {
                commentsPos = previousSibling.End();
            }
            EmitLeadingComments(commentsPos, false /*elided*/);
        }

        // Decrease the indent, if requested.
        if ((format & ListFormat.Indented) != 0)
        {
            DecreaseIndent();
        }

        // Write the closing line terminator or closing whitespace.
        var closingLineTerminatorCount = GetClosingLineTerminatorCount(parentNode, LastOrNil(children), format, childrenTextRange);
        if (closingLineTerminatorCount > 0)
        {
            for (var i = 0; i < closingLineTerminatorCount; i++)
            {
                WriteLine();
            }
        }
        else if ((format & (ListFormat.SpaceAfterList | ListFormat.SpaceBetweenBraces)) != 0)
        {
            WriteSpace();
        }
    }
}
