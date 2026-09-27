// ported from: internal/format/span.go:1-1041（findEnclosingNode / getScanStartPosition / getOwnOrInheritedDelta /
//            prepareRangeContainsErrorFunction / formatSpanWorker 的构造与遍历、编辑记录）
//            后半（consumeTokenAndAdvanceScanner / dynamicIndenter / getFirstNonDecoratorTokenOfNode /
//            getDynamicIndentation）在 Span.Indentation.cs（同一个 partial class，按逻辑段落切分，docs §2）。
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.1/§3.2/§3.8）：
// - 包级函数 → 静态类 `Format`；未导出类型 `formatSpanWorker`/`dynamicIndenter` → `FormatSpanWorker`/`DynamicIndenter`
//   （class，与 Go 的 `*T` 语义一致）；未导出字段 → `_camelCase`。
// - `LineAction` 是 Go 的导出类型（常量 LineActionNone/LineAdded/LineRemoved）→ public enum，成员去前缀。
// - `strings.Count(s, "\n")`（只用于 make 的容量提示）→ 本文件的私有计数循环（零分配）。
// - `slices.SortStableFunc` → `OrderBy`（LINQ 排序是稳定的，与 Go 的 stable sort 一致；建表一次，不在热路径）。
// - `slices.Backward(w.currentRules)` → 反向下标循环。
// - `utf8.DecodeRuneInString` → `Scan.DecodeRuneAt`（§3.1，size 是代码单元数）。
// - 文本切片视图 → `AsSpan`（§3.8）。
// - `panic`/`debug.Assert` → `Debug.Fail`/`Debug.Assert`；`min(a, b)` → `Math.Min`。
// - Go 的 `w.currentRules[:0]`（复用缓冲）→ `List<RuleImpl>.Clear()`。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.LsUtil.LsUtil;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.Format;

/// <summary>Go 的 <c>type LineAction int</c>（<c>LineActionNone</c>/<c>LineAdded</c>/<c>LineRemoved</c>）。</summary>
public enum LineAction
{
    None = 0,
    LineAdded = 1,
    LineRemoved = 2,
}

public static partial class Format
{
    /// <summary>find node that fully contains given text range</summary>
    internal static Node FindEnclosingNode(TextRange r, SourceFile sourceFile)
    {
        Node Find(Node n)
        {
            Node? candidate = null;
            n.ForEachChild(c =>
            {
                if ((c.Flags & NodeFlags.Reparsed) != 0)
                {
                    return false;
                }

                if (r.ContainedBy(WithTokenStart(c, sourceFile)))
                {
                    candidate = c;
                    return true;
                }

                return false;
            });

            if (candidate is not null)
            {
                var result = Find(candidate);
                if (result is not null)
                {
                    return result;
                }
            }

            return n;
        }

        return Find(sourceFile.AsNode());
    }

    /// <summary>
    /// Start of the original range might fall inside the comment - scanner will not yield appropriate results
    /// This function will look for token that is located before the start of target range
    /// and return its end as start position for the scanner.
    /// </summary>
    internal static int GetScanStartPosition(Node enclosingNode, TextRange originalRange, SourceFile sourceFile)
    {
        var adjusted = WithTokenStart(enclosingNode, sourceFile);
        var start = adjusted.Pos();
        if (start == originalRange.Pos() && enclosingNode.End() == originalRange.End())
        {
            return start;
        }

        // exclude JSDoc so the scan never starts inside a JSDoc comment
        var precedingToken = AstNav.AstNav.FindPrecedingTokenEx(sourceFile, originalRange.Pos(), null /*startNode*/, true /*excludeJSDoc*/);
        if (precedingToken is null)
        {
            // no preceding token found - start from the beginning of enclosing node
            return enclosingNode.Pos();
        }

        // preceding token ends after the start of original range (i.e when originalRange.pos falls in the middle of literal)
        // start from the beginning of enclosingNode to handle the entire 'originalRange'
        if (precedingToken.End() >= originalRange.Pos())
        {
            return enclosingNode.Pos();
        }

        return precedingToken.End();
    }

    /*
     * For cases like
     * if (a ||
     *     b ||$
     *     c) {...}
     * If we hit Enter at $ we want line '    b ||' to be indented.
     * Formatting will be applied to the last two lines.
     * Node that fully encloses these lines is binary expression 'a ||...'.
     * Initial indentation for this node will be 0.
     * Binary expressions don't introduce new indentation scopes, however it is possible
     * that some parent node on the same line does - like if statement in this case.
     * Note that we are considering parents only from the same line with initial node -
     * if parent is on the different line - its delta was already contributed
     * to the initial indentation.
     */
    internal static int GetOwnOrInheritedDelta(Node? n, FormatCodeSettings options, SourceFile sourceFile)
    {
        var previousLine = -1;
        Node? child = null;
        while (n is not null)
        {
            var line = Scan.GetECMALineOfPosition(sourceFile, WithTokenStart(n, sourceFile).Pos());
            if (previousLine != -1 && line != previousLine)
            {
                break;
            }

            if (ShouldIndentChildNode(options, n, child, sourceFile))
            {
                return options.IndentSize; // !!! nil check???
            }

            previousLine = line;
            child = n;
            n = n.Parent;
        }

        return 0;
    }

    internal static bool RangeHasNoErrors(TextRange _)
    {
        return false;
    }

    internal static Func<TextRange, bool> PrepareRangeContainsErrorFunction(Diagnostic[] errors, TextRange originalRange)
    {
        if (errors.Length == 0)
        {
            return RangeHasNoErrors;
        }

        // pick only errors that fall in range
        var sorted = Filter(errors, d => originalRange.Overlaps(d.Loc()));
        if (sorted.Length == 0)
        {
            return RangeHasNoErrors;
        }

        // Go: slices.SortStableFunc(sorted, func(a, b *ast.Diagnostic) int { return a.Pos() - b.Pos() })
        sorted = [.. System.Linq.Enumerable.OrderBy(sorted, a => a.Pos())];

        var index = 0;
        return r =>
        {
            // in current implementation sequence of arguments [r1, r2...] is monotonically increasing.
            // 'index' tracks the index of the most recent error that was checked.
            while (true)
            {
                if (index >= sorted.Length)
                {
                    // all errors in the range were already checked -> no error in specified range
                    return false;
                }

                var err = sorted[index];

                if (r.End() <= err.Pos())
                {
                    // specified range ends before the error referred by 'index' - no error in range
                    return false;
                }

                if (r.Overlaps(err.Loc()))
                {
                    // specified range overlaps with error range
                    return true;
                }

                index++;
            }
        };
    }

    internal static FormatSpanWorker NewFormatSpanWorker(
        Context ctx,
        TextRange originalRange,
        Node enclosingNode,
        int initialIndentation,
        int delta,
        FormatRequestKind requestKind,
        Func<TextRange, bool> rangeContainsError,
        SourceFile sourceFile)
    {
        return new FormatSpanWorker
        {
            _ctx = ctx,
            _originalRange = originalRange,
            _enclosingNode = enclosingNode,
            _initialIndentation = initialIndentation,
            _delta = delta,
            _requestKind = requestKind,
            _rangeContainsError = rangeContainsError,
            _sourceFile = sourceFile,
            // Go: currentRules: make([]*ruleImpl, 0, 32) —— increaseInsertionIndex 保证一个桶里不超过 32 条规则
            _currentRules = new List<RuleImpl>(32),
        };
    }
}

/// <summary>Go 的 <c>type formatSpanWorker struct</c>。</summary>
internal sealed partial class FormatSpanWorker
{
    internal TextRange _originalRange;
    internal Node _enclosingNode = null!;
    internal int _initialIndentation;
    internal int _delta;
    internal FormatRequestKind _requestKind;
    internal Func<TextRange, bool> _rangeContainsError = null!;
    internal SourceFile _sourceFile = null!;

    internal Context _ctx = null!;

    internal FormattingScanner? _formattingScanner;
    internal FormattingContext? _formattingContext;

    internal readonly List<TextChange> _edits = [];
    internal TextRangeWithKind _previousRange;
    internal int _previousRangeTriviaEnd;
    internal Node? _previousParent;
    internal int _previousRangeStartLine;

    internal Node? _childContextNode;
    internal int _lastIndentedLine;
    internal int _indentationOnLastIndentedLine;

    internal NodeVisitor? _visitor;
    internal Node? _visitingNode;
    internal DynamicIndenter? _visitingIndenter;
    internal int _visitingNodeStartLine;
    internal int _visitingUndecoratedNodeStartLine;

    internal List<RuleImpl> _currentRules = null!;

    internal static int GetNonDecoratorTokenPosOfNode(Node node, SourceFile? file)
    {
        Node? lastDecorator = null;
        if (HasDecorators(node))
        {
            lastDecorator = FindLast(node.ModifierNodes(), n => IsDecorator(n));
        }

        file ??= GetSourceFileOfNode(node)!;
        if (lastDecorator is null)
        {
            return Format.WithTokenStart(node, file).Pos();
        }

        return Scan.SkipTrivia(file.Text(), lastDecorator.End());
    }

    internal TextChange[] Execute(FormattingScanner s)
    {
        _formattingScanner = s;
        _indentationOnLastIndentedLine = -1;
        _lastIndentedLine = -1;
        var opt = Format.GetFormatCodeSettingsFromContext(_ctx);
        _formattingContext = Format.NewFormattingContext(_sourceFile, _requestKind, opt);
        // formatting context is used by rules provider
        _visitor = NewNodeVisitor(
            child =>
            {
                if (child is null)
                {
                    return child;
                }

                ProcessChildNode(_visitingNode!, _visitingIndenter!, _visitingNodeStartLine, _visitingUndecoratedNodeStartLine, child, -1, _visitingNode!, _visitingIndenter!, _visitingNodeStartLine, _visitingUndecoratedNodeStartLine, false, false);
                return child;
            },
            new NodeFactory(),
            new NodeVisitorHooks
            {
                VisitNodes = (nodes, v) =>
                {
                    // Go: `if nodes == nil { return nodes }`；C# 的委托参数标为非空，但 VisitNodesWithHooks 可能传 null
                    // （`hook(nodes!, this)`），所以判断保留。
                    if (nodes is null)
                    {
                        return nodes;
                    }

                    ProcessChildNodes(_visitingNode!, _visitingIndenter!, _visitingNodeStartLine, _visitingUndecoratedNodeStartLine, nodes, _visitingNode!, _visitingNodeStartLine, _visitingIndenter!);
                    return nodes;
                },
            });

        _formattingScanner.Advance();

        if (_formattingScanner.IsOnToken())
        {
            var startLine = Scan.GetECMALineOfPosition(_sourceFile, Format.WithTokenStart(_enclosingNode, _sourceFile).Pos());
            var undecoratedStartLine = startLine;
            if (HasDecorators(_enclosingNode))
            {
                undecoratedStartLine = Scan.GetECMALineOfPosition(_sourceFile, GetNonDecoratorTokenPosOfNode(_enclosingNode, _sourceFile));
            }

            ProcessNode(_enclosingNode, _enclosingNode, startLine, undecoratedStartLine, _initialIndentation, _delta);
        }

        // Leading trivia items get attached to and processed with the token that proceeds them. If the
        // range ends in the middle of some leading trivia, the token that proceeds them won't be in the
        // range and thus won't get processed. So we process those remaining trivia items here.
        var remainingTrivia = _formattingScanner.GetCurrentLeadingTrivia();
        if (remainingTrivia.Length > 0)
        {
            var indentation = _initialIndentation;
            if (Format.NodeWillIndentChild(_formattingContext.Options, _enclosingNode, null, _sourceFile, false))
            {
                indentation += opt.IndentSize; // !!! TODO: nil check???
            }

            IndentTriviaItems(remainingTrivia, indentation, true, item =>
            {
                var (startLine, startChar) = Scan.GetECMALineAndByteOffsetOfPosition(_sourceFile, item.Loc.Pos());
                ProcessRange(item, startLine, startChar, _enclosingNode, _enclosingNode, null);
                InsertIndentation(item.Loc.Pos(), indentation, false);
            });

            if (opt.TrimTrailingWhitespace.IsTrue())
            {
                TrimTrailingWhitespacesForRemainingRange(remainingTrivia);
            }
        }

        if (_previousRange != Format.NewTextRangeWithKind(0, 0, 0) && _formattingScanner.GetTokenFullStart() >= _originalRange.End())
        {
            // Formatting edits happen by looking at pairs of contiguous tokens (see `processPair`),
            // typically inserting or deleting whitespace between them. The recursive `processNode`
            // logic above bails out as soon as it encounters a token that is beyond the end of the
            // range we're supposed to format (or if we reach the end of the file). But this potentially
            // leaves out an edit that would occur *inside* the requested range but cannot be discovered
            // without looking at one token *beyond* the end of the range: consider the line `x = { }`
            // with a selection from the beginning of the line to the space inside the curly braces,
            // inclusive. We would expect a format-selection would delete the space (if rules apply),
            // but in order to do that, we need to process the pair ["{", "}"], but we stopped processing
            // just before getting there. This block handles this trailing edit.
            var tokenInfo = default(TextRangeWithKind);
            if (_formattingScanner.IsOnEOF())
            {
                tokenInfo = _formattingScanner.ReadEOFTokenRange();
            }
            else if (_formattingScanner.IsOnToken())
            {
                tokenInfo = _formattingScanner.ReadTokenInfo(_enclosingNode)._token;
            }

            if (tokenInfo.Loc.Pos() == _previousRangeTriviaEnd)
            {
                // We need to check that tokenInfo and previousRange are contiguous: the `originalRange`
                // may have ended in the middle of a token, which means we will have stopped formatting
                // on that token, leaving `previousRange` pointing to the token before it, but already
                // having moved the formatting scanner (where we just got `tokenInfo`) to the next token.
                // If this happens, our supposed pair [previousRange, tokenInfo] actually straddles the
                // token that intersects the end of the range we're supposed to format, so the pair will
                // produce bogus edits if we try to `processPair`. Recall that the point of this logic is
                // to perform a trailing edit at the end of the selection range: but there can be no valid
                // edit in the middle of a token where the range ended, so if we have a non-contiguous
                // pair here, we're already done and we can ignore it.
                var parent = AstNav.AstNav.FindPrecedingToken(_sourceFile, tokenInfo.Loc.End());
                if (parent is not null)
                {
                    parent = parent.Parent;
                }

                if (parent is null)
                {
                    parent = _previousParent;
                }

                var line = Scan.GetECMALineOfPosition(_sourceFile, tokenInfo.Loc.Pos());
                ProcessPair(
                    tokenInfo,
                    line,
                    parent,
                    _previousRange,
                    _previousRangeStartLine,
                    _previousParent,
                    parent,
                    null);
            }
        }

        return [.. _edits];
    }

    internal int ProcessChildNode(
        Node node,
        DynamicIndenter indenter,
        int nodeStartLine,
        int undecoratedNodeStartLine,
        Node child,
        int inheritedIndentation,
        Node parent,
        DynamicIndenter? parentDynamicIndentation,
        int parentStartLine,
        int undecoratedParentStartLine,
        bool isListItem,
        bool isFirstListItem)
    {
        Debug.Assert(!NodeIsSynthesized(child));

        if (NodeIsMissing(child) || (child.Flags & NodeFlags.Reparsed) != 0)
        {
            return inheritedIndentation;
        }

        var childStartPos = Scan.GetTokenPosOfNode(child, _sourceFile, false);
        var childStartLine = Scan.GetECMALineOfPosition(_sourceFile, childStartPos);

        var undecoratedChildStartLine = childStartLine;
        if (HasDecorators(child))
        {
            undecoratedChildStartLine = Scan.GetECMALineOfPosition(_sourceFile, GetNonDecoratorTokenPosOfNode(child, _sourceFile));
        }

        var isErrorMemberListElement = (child.Flags & NodeFlags.ThisNodeHasError) != 0 && Format.IsMemberListElement(parent, child);
        // if child is a list item - try to get its indentation, only if parent is within the original range.
        var childIndentationAmount = -1;

        if (!isErrorMemberListElement && isListItem && parent.Loc.ContainedBy(_originalRange))
        {
            childIndentationAmount = TryComputeIndentationForListItem(childStartPos, child.End(), parentStartLine, _originalRange, inheritedIndentation);
            if (childIndentationAmount != -1)
            {
                inheritedIndentation = childIndentationAmount;
            }
        }

        // child node is outside the target range - do not dive inside
        if (!_originalRange.Overlaps(child.Loc))
        {
            if (child.End() < _originalRange.Pos())
            {
                _formattingScanner!.SkipToEndOf(child.Loc);
            }

            return inheritedIndentation;
        }

        if (child.Loc.Len() == 0)
        {
            return inheritedIndentation;
        }

        while (_formattingScanner!.IsOnToken() && _formattingScanner.GetTokenFullStart() < _originalRange.End())
        {
            // proceed any parent tokens that are located prior to child.getStart()
            var tokenInfo = _formattingScanner.ReadTokenInfo(node);
            if (tokenInfo._token.Loc.End() > _originalRange.End())
            {
                return inheritedIndentation;
            }

            if (tokenInfo._token.Loc.End() > childStartPos)
            {
                if (tokenInfo._token.Loc.Pos() > childStartPos)
                {
                    _formattingScanner.SkipToStartOf(child.Loc);
                }

                // stop when formatting scanner advances past the beginning of the child
                break;
            }

            ConsumeTokenAndAdvanceScanner(tokenInfo, node, parentDynamicIndentation!, node, false);
        }

        if (!_formattingScanner.IsOnToken() || _formattingScanner.GetTokenFullStart() >= _originalRange.End())
        {
            return inheritedIndentation;
        }

        if (IsTokenKind(child.Kind))
        {
            // if child node is a token, it does not impact indentation, proceed it using parent indentation scope rules
            var tokenInfo = _formattingScanner.ReadTokenInfo(child);
            // JSX text shouldn't affect indenting
            if (child.Kind != Kind.JsxText)
            {
                Debug.Assert(tokenInfo._token.Loc.End() == child.Loc.End(), "Token end is child end");
                ConsumeTokenAndAdvanceScanner(tokenInfo, node, parentDynamicIndentation!, child, false);
                return inheritedIndentation;
            }
        }

        var effectiveParentStartLine = undecoratedParentStartLine;
        if (child.Kind == Kind.Decorator)
        {
            effectiveParentStartLine = childStartLine;
        }

        var childIndentation = 0;
        var delta = 0;
        if (isErrorMemberListElement)
        {
            childIndentation = GetCurrentIndentationAtPosition(childStartPos);
        }
        else
        {
            (childIndentation, delta) = ComputeIndentation(child, childStartLine, childIndentationAmount, node, parentDynamicIndentation!, effectiveParentStartLine);
        }

        ProcessNode(child, _childContextNode!, childStartLine, undecoratedChildStartLine, childIndentation, delta);

        _childContextNode = node;

        if (isFirstListItem && parent.Kind == Kind.ArrayLiteralExpression && inheritedIndentation == -1)
        {
            inheritedIndentation = childIndentation;
        }

        return inheritedIndentation;
    }

    internal void ProcessChildNodes(
        Node node,
        DynamicIndenter? indenter,
        int nodeStartLine,
        int undecoratedNodeStartLine,
        NodeList nodes,
        Node parent,
        int parentStartLine,
        DynamicIndenter parentDynamicIndentation)
    {
        // Go: debug.Assert(nodes != nil)；C# 的 `NodeList nodes` 非空由类型系统保证（§3.2），保留位置断言。
        Debug.Assert(!PositionIsSynthesized(nodes.Pos()));
        Debug.Assert(!PositionIsSynthesized(nodes.End()));

        var listStartToken = Format.GetOpenTokenForList(parent, nodes);

        var listDynamicIndentation = parentDynamicIndentation;
        var startLine = parentStartLine;

        // node range is outside the target range - do not dive inside
        if (!_originalRange.Overlaps(nodes.Loc))
        {
            if (nodes.End() < _originalRange.Pos() && (nodes.Nodes is null || nodes.Nodes.Length == 0 || (nodes.Nodes[0]!.Flags & NodeFlags.Reparsed) == 0))
            {
                _formattingScanner!.SkipToEndOf(nodes.Loc);
            }

            return;
        }

        if (listStartToken != Kind.Unknown)
        {
            // introduce a new indentation scope for lists (including list start and end tokens)
            while (_formattingScanner!.IsOnToken() && _formattingScanner.GetTokenFullStart() < _originalRange.End())
            {
                var tokenInfo = _formattingScanner.ReadTokenInfo(parent);
                if (tokenInfo._token.Loc.End() > nodes.Pos())
                {
                    // stop when formatting scanner moves past the beginning of node list
                    break;
                }
                else if (tokenInfo._token.Kind == listStartToken)
                {
                    // consume list start token
                    startLine = Scan.GetECMALineOfPosition(_sourceFile, tokenInfo._token.Loc.Pos());

                    ConsumeTokenAndAdvanceScanner(tokenInfo, parent, parentDynamicIndentation, parent, false);

                    var indentationOnListStartToken = 0;
                    if (_indentationOnLastIndentedLine != -1)
                    {
                        // scanner just processed list start token so consider last indentation as list indentation
                        // function foo(): { // last indentation was 0, list item will be indented based on this value
                        //   foo: number;
                        // }: {};
                        indentationOnListStartToken = _indentationOnLastIndentedLine;
                    }
                    else
                    {
                        indentationOnListStartToken = GetCurrentIndentationAtPosition(tokenInfo._token.Loc.Pos());
                    }

                    listDynamicIndentation = GetDynamicIndentation(parent, parentStartLine, indentationOnListStartToken, _formattingContext!.Options.IndentSize);
                }
                else
                {
                    // consume any tokens that precede the list as child elements of 'node' using its indentation scope
                    ConsumeTokenAndAdvanceScanner(tokenInfo, parent, parentDynamicIndentation, parent, false);
                }
            }
        }

        var inheritedIndentation = -1;
        for (var i = 0; i < (nodes.Nodes?.Length ?? 0); i++)
        {
            var child = nodes.Nodes![i]!;
            inheritedIndentation = ProcessChildNode(node, indenter!, nodeStartLine, undecoratedNodeStartLine, child, inheritedIndentation, node, listDynamicIndentation, startLine, startLine, true, i == 0);
        }

        var listEndToken = Format.GetCloseTokenForOpenToken(listStartToken);
        if (listEndToken != Kind.Unknown && _formattingScanner!.IsOnToken() && _formattingScanner.GetTokenFullStart() < _originalRange.End())
        {
            var tokenInfo = _formattingScanner.ReadTokenInfo(parent);
            if (tokenInfo._token.Kind == Kind.CommaToken)
            {
                // consume the comma
                ConsumeTokenAndAdvanceScanner(tokenInfo, parent, listDynamicIndentation, parent, false);
                if (_formattingScanner.IsOnToken())
                {
                    tokenInfo = _formattingScanner.ReadTokenInfo(parent);
                }
                else
                {
                    return;
                }
            }

            // consume the list end token only if it is still belong to the parent
            // there might be the case when current token matches end token but does not considered as one
            // function (x: function) <--
            // without this check close paren will be interpreted as list end token for function expression which is wrong
            if (tokenInfo._token.Kind == listEndToken && tokenInfo._token.Loc.ContainedBy(parent.Loc))
            {
                // consume list end token
                ConsumeTokenAndAdvanceScanner(tokenInfo, parent, listDynamicIndentation, parent /*isListEndToken*/, true);
            }
        }
    }

    internal void ExecuteProcessNodeVisitor(Node node, DynamicIndenter indenter, int nodeStartLine, int undecoratedNodeStartLine)
    {
        var oldNode = _visitingNode;
        var oldIndenter = _visitingIndenter;
        var oldStart = _visitingNodeStartLine;
        var oldUndecoratedStart = _visitingUndecoratedNodeStartLine;
        _visitingNode = node;
        _visitingIndenter = indenter;
        _visitingNodeStartLine = nodeStartLine;
        _visitingUndecoratedNodeStartLine = undecoratedNodeStartLine;
        node.VisitEachChild(_visitor!);
        _visitingNode = oldNode;
        _visitingIndenter = oldIndenter;
        _visitingNodeStartLine = oldStart;
        _visitingUndecoratedNodeStartLine = oldUndecoratedStart;
    }

    internal int GetCurrentIndentationAtPosition(int pos)
    {
        var startLinePosition = Format.GetLineStartPositionForPosition(pos, _sourceFile);
        return Format.FindFirstNonWhitespaceColumn(startLinePosition, pos, _sourceFile, _formattingContext!.Options);
    }

    internal (int Indentation, int Delta) ComputeIndentation(Node node, int startLine, int inheritedIndentation, Node parent, DynamicIndenter parentDynamicIndentation, int effectiveParentStartLine)
    {
        var delta = 0;
        if (Format.ShouldIndentChildNode(_formattingContext!.Options, node, null, null!))
        {
            delta = _formattingContext.Options.IndentSize;
        }

        if (effectiveParentStartLine == startLine)
        {
            // if node is located on the same line with the parent
            // - inherit indentation from the parent
            // - push children if either parent of node itself has non-zero delta
            var indentation = _indentationOnLastIndentedLine;
            if (startLine != _lastIndentedLine)
            {
                indentation = parentDynamicIndentation.GetIndentation();
            }

            delta = Math.Min(_formattingContext.Options.IndentSize, parentDynamicIndentation.GetDelta(node) + delta);
            return (indentation, delta);
        }
        else if (inheritedIndentation == -1)
        {
            if (node.Kind == Kind.OpenParenToken && startLine == _lastIndentedLine)
            {
                // the is used for chaining methods formatting
                // - we need to get the indentation on last line and the delta of parent
                return (_indentationOnLastIndentedLine, parentDynamicIndentation.GetDelta(node));
            }
            else if (Format.ChildStartsOnTheSameLineWithElseInIfStatement(parent, node, startLine, _sourceFile) ||
                Format.ChildIsUnindentedBranchOfConditionalExpression(parent, node, startLine, _sourceFile) ||
                Format.ArgumentStartsOnSameLineAsPreviousArgument(parent, node, startLine, _sourceFile))
            {
                return (parentDynamicIndentation.GetIndentation(), delta);
            }
            else
            {
                var i = parentDynamicIndentation.GetIndentation();
                if (i == -1)
                {
                    return (parentDynamicIndentation.GetIndentation(), delta);
                }

                return (i + parentDynamicIndentation.GetDelta(node), delta);
            }
        }

        return (inheritedIndentation, delta);
    }

    /// <summary>
    /// Tries to compute the indentation for a list element.
    /// If list element is not in range then
    /// function will pick its actual indentation
    /// so it can be pushed downstream as inherited indentation.
    /// If list element is in the range - its indentation will be equal
    /// to inherited indentation from its predecessors.
    /// </summary>
    internal int TryComputeIndentationForListItem(int startPos, int endPos, int parentStartLine, TextRange r, int inheritedIndentation)
    {
        var r2 = NewTextRange(startPos, endPos);
        if (r.Overlaps(r2) || r2.ContainedBy(r)) /* Not to miss zero-range nodes e.g. JsxText */
        {
            if (inheritedIndentation != -1)
            {
                return inheritedIndentation;
            }
        }
        else
        {
            var startLine = Scan.GetECMALineOfPosition(_sourceFile, startPos);
            var column = GetCurrentIndentationAtPosition(startPos);
            if (startLine != parentStartLine || startPos == column)
            {
                // Use the base indent size if it is greater than
                // the indentation of the inherited predecessor.
                var baseIndentSize = _formattingContext!.Options.BaseIndentSize;
                if (baseIndentSize > column)
                {
                    return baseIndentSize;
                }

                return column;
            }
        }

        return -1;
    }

    internal void ProcessNode(Node node, Node contextNode, int nodeStartLine, int undecoratedNodeStartLine, int indentation, int delta)
    {
        if (!_originalRange.Overlaps(Format.WithTokenStart(node, _sourceFile)))
        {
            return;
        }

        var nodeDynamicIndentation = GetDynamicIndentation(node, nodeStartLine, indentation, delta);

        // a useful observations when tracking context node
        //        /
        //      [a]
        //   /   |   \
        //  [b] [c] [d]
        // node 'a' is a context node for nodes 'b', 'c', 'd'
        // except for the leftmost leaf token in [b] - in this case context node ('e') is located somewhere above 'a'
        // this rule can be applied recursively to child nodes of 'a'.
        //
        // context node is set to parent node value after processing every child node
        // context node is set to parent of the token after processing every token

        _childContextNode = contextNode;

        // if there are any tokens that logically belong to node and interleave child nodes
        // such tokens will be consumed in processChildNode for the child that follows them
        ExecuteProcessNodeVisitor(node, nodeDynamicIndentation, nodeStartLine, undecoratedNodeStartLine);

        // proceed any tokens in the node that are located after child nodes
        while (_formattingScanner!.IsOnToken() && _formattingScanner.GetTokenFullStart() < _originalRange.End())
        {
            var tokenInfo = _formattingScanner.ReadTokenInfo(node);
            if (tokenInfo._token.Loc.End() > Math.Min(node.End(), _originalRange.End()))
            {
                break;
            }

            ConsumeTokenAndAdvanceScanner(tokenInfo, node, nodeDynamicIndentation, node, false);
        }
    }

    internal LineAction ProcessPair(TextRangeWithKind currentItem, int currentStartLine, Node? currentParent, TextRangeWithKind previousItem, int previousStartLine, Node? previousParent, Node? contextNode, DynamicIndenter? dynamicIndentation)
    {
        _formattingContext!.UpdateContext(previousItem, previousParent, currentItem, currentParent, contextNode);

        _currentRules.Clear();
        Format.GetRules(_formattingContext, _currentRules);

        var trimTrailingWhitespaces = !_formattingContext.Options.TrimTrailingWhitespace.IsFalse();
        var lineAction = LineAction.None;

        if (_currentRules.Count > 0)
        {
            // Apply rules in reverse order so that higher priority rules (which are first in the array)
            // win in a conflict with lower priority rules.
            for (var i = _currentRules.Count - 1; i >= 0; i--)
            {
                var rule = _currentRules[i];
                lineAction = ApplyRuleEdits(rule, previousItem, previousStartLine, currentItem, currentStartLine);
                if (dynamicIndentation is not null)
                {
                    switch (lineAction)
                    {
                        case LineAction.LineRemoved:
                            // Handle the case where the next line is moved to be the end of this line.
                            // In this case we don't indent the next line in the next pass.
                            if (Scan.GetTokenPosOfNode(currentParent!, _sourceFile, false) == currentItem.Loc.Pos())
                            {
                                dynamicIndentation.RecomputeIndentation( /*lineAddedByFormatting*/ false, contextNode!);
                            }

                            break;
                        case LineAction.LineAdded:
                            // Handle the case where token2 is moved to the new line.
                            // In this case we indent token2 in the next pass but we set
                            // sameLineIndent flag to notify the indenter that the indentation is within the line.
                            if (Scan.GetTokenPosOfNode(currentParent!, _sourceFile, false) == currentItem.Loc.Pos())
                            {
                                dynamicIndentation.RecomputeIndentation( /*lineAddedByFormatting*/ true, contextNode!);
                            }

                            break;
                        default:
                            Debug.Assert(lineAction == LineAction.None);
                            break;
                    }
                }

                // We need to trim trailing whitespace between the tokens if they were on different lines, and no rule was applied to put them on the same line
                trimTrailingWhitespaces = trimTrailingWhitespaces && (rule.Action() & RuleAction.DeleteSpace) == 0 && rule.Flags() != RuleFlags.CanDeleteNewLines;
            }
        }
        else
        {
            trimTrailingWhitespaces = trimTrailingWhitespaces && currentItem.Kind != Kind.EndOfFile;
        }

        if (currentStartLine != previousStartLine && trimTrailingWhitespaces)
        {
            // We need to trim trailing whitespace between the tokens if they were on different lines, and no rule was applied to put them on the same line
            TrimTrailingWhitespacesForLines(previousStartLine, currentStartLine, previousItem);
        }

        return lineAction;
    }

    internal LineAction ApplyRuleEdits(RuleImpl rule, TextRangeWithKind previousRange, int previousStartLine, TextRangeWithKind currentRange, int currentStartLine)
    {
        var onLaterLine = currentStartLine != previousStartLine;
        switch (rule.Action())
        {
            case RuleAction.StopProcessingSpaceActions:
                // no action required
                return LineAction.None;
            case RuleAction.DeleteSpace:
                if (previousRange.Loc.End() != currentRange.Loc.Pos())
                {
                    // delete characters starting from t1.end up to t2.pos exclusive
                    RecordDelete(previousRange.Loc.End(), currentRange.Loc.Pos() - previousRange.Loc.End());
                    if (onLaterLine)
                    {
                        return LineAction.LineRemoved;
                    }

                    return LineAction.None;
                }

                break;
            case RuleAction.DeleteToken:
                RecordDelete(previousRange.Loc.Pos(), previousRange.Loc.Len());
                break;
            case RuleAction.InsertNewLine:
                // exit early if we on different lines and rule cannot change number of newlines
                // if line1 and line2 are on subsequent lines then no edits are required - ok to exit
                // if line1 and line2 are separated with more than one newline - ok to exit since we cannot delete extra new lines
                if (rule.Flags() != RuleFlags.CanDeleteNewLines && previousStartLine != currentStartLine)
                {
                    return LineAction.None;
                }

                // edit should not be applied if we have one line feed between elements
                var lineDelta = currentStartLine - previousStartLine;
                if (lineDelta != 1)
                {
                    RecordReplace(previousRange.Loc.End(), currentRange.Loc.Pos() - previousRange.Loc.End(), Format.GetNewLineOrDefaultFromContext(_ctx));
                    if (onLaterLine)
                    {
                        return LineAction.None;
                    }

                    return LineAction.LineAdded;
                }

                break;
            case RuleAction.InsertSpace:
                // exit early if we on different lines and rule cannot change number of newlines
                if (rule.Flags() != RuleFlags.CanDeleteNewLines && previousStartLine != currentStartLine)
                {
                    return LineAction.None;
                }

                var posDelta = currentRange.Loc.Pos() - previousRange.Loc.End();
                // Go: strings.HasPrefix(w.sourceFile.Text()[previousRange.Loc.End():], " ")
                if (posDelta != 1 || !_sourceFile.Text().AsSpan(previousRange.Loc.End()).StartsWith(" "))
                {
                    RecordReplace(previousRange.Loc.End(), posDelta, " ");
                    if (onLaterLine)
                    {
                        return LineAction.LineRemoved;
                    }

                    return LineAction.None;
                }

                break;
            case RuleAction.InsertTrailingSemicolon:
                RecordInsert(previousRange.Loc.End(), ";");
                break;
        }

        return LineAction.None;
    }

    internal LineAction ProcessRange(TextRangeWithKind r, int rangeStartLine, int rangeStartCharacter, Node parent, Node contextNode, DynamicIndenter? dynamicIndentation)
    {
        var rangeHasError = _rangeContainsError(r.Loc);
        var lineAction = LineAction.None;
        if (!rangeHasError)
        {
            if (_previousRange == Format.NewTextRangeWithKind(0, 0, 0))
            {
                // trim whitespaces starting from the beginning of the span up to the current line
                var originalStartLine = Scan.GetECMALineOfPosition(_sourceFile, _originalRange.Pos());
                TrimTrailingWhitespacesForLines(originalStartLine, rangeStartLine, Format.NewTextRangeWithKind(0, 0, 0));
            }
            else
            {
                lineAction = ProcessPair(r, rangeStartLine, parent, _previousRange, _previousRangeStartLine, _previousParent, contextNode, dynamicIndentation);
            }
        }

        _previousRange = r;
        _previousRangeTriviaEnd = r.Loc.End();
        _previousParent = parent;
        _previousRangeStartLine = rangeStartLine;

        return lineAction;
    }

    internal void ProcessTrivia(TextRangeWithKind[] trivia, Node parent, Node contextNode, DynamicIndenter? dynamicIndentation)
    {
        foreach (var triviaItem in trivia)
        {
            if (Format.IsComment(triviaItem.Kind) && triviaItem.Loc.ContainedBy(_originalRange))
            {
                var (triviaItemStartLine, triviaItemStartCharacter) = Scan.GetECMALineAndByteOffsetOfPosition(_sourceFile, triviaItem.Loc.Pos());
                ProcessRange(triviaItem, triviaItemStartLine, triviaItemStartCharacter, parent, contextNode, dynamicIndentation);
            }
        }
    }

    /// <summary>
    /// Trimming will be done for lines after the previous range.
    /// Exclude comments as they had been previously processed.
    /// </summary>
    internal void TrimTrailingWhitespacesForRemainingRange(TextRangeWithKind[] trivias)
    {
        var startPos = _originalRange.Pos();
        if (_previousRange != Format.NewTextRangeWithKind(0, 0, 0))
        {
            startPos = _previousRange.Loc.End();
        }

        foreach (var trivia in trivias)
        {
            if (Format.IsComment(trivia.Kind))
            {
                if (startPos < trivia.Loc.Pos())
                {
                    TrimTrailingWitespacesForPositions(startPos, trivia.Loc.Pos() - 1, _previousRange);
                }

                startPos = trivia.Loc.End() + 1;
            }
        }

        if (startPos < _originalRange.End())
        {
            TrimTrailingWitespacesForPositions(startPos, _originalRange.End(), _previousRange);
        }
    }

    internal void TrimTrailingWitespacesForPositions(int startPos, int endPos, TextRangeWithKind previousRange)
    {
        var startLine = Scan.GetECMALineOfPosition(_sourceFile, startPos);
        var endLine = Scan.GetECMALineOfPosition(_sourceFile, endPos);

        TrimTrailingWhitespacesForLines(startLine, endLine + 1, previousRange);
    }

    internal void TrimTrailingWhitespacesForLines(int line1, int line2, TextRangeWithKind r)
    {
        var lineStarts = Scan.GetECMALineStarts(_sourceFile);
        for (var line = line1; line < line2; line++)
        {
            var lineStartPosition = lineStarts[line];
            var lineEndPosition = Scan.GetECMAEndLinePosition(_sourceFile, line);

            // do not trim whitespaces in comments or template expression
            if (r != Format.NewTextRangeWithKind(0, 0, 0) && (Format.IsComment(r.Kind) || Format.IsStringOrRegularExpressionOrTemplateLiteral(r.Kind)) && r.Loc.Pos() <= lineEndPosition && r.Loc.End() > lineEndPosition)
            {
                continue;
            }

            var whitespaceStart = GetTrailingWhitespaceStartPosition(lineStartPosition, lineEndPosition);
            if (whitespaceStart != -1)
            {
                if (whitespaceStart != lineStartPosition)
                {
                    var rune = Scan.DecodeRuneAt(_sourceFile.Text(), whitespaceStart - 1, out _);
                    Debug.Assert(!StringUtil.StringUtil.IsWhiteSpaceSingleLine(rune));
                }

                RecordDelete(whitespaceStart, lineEndPosition + 1 - whitespaceStart);
            }
        }
    }

    /// <param name="start">The position of the first character in range</param>
    /// <param name="end">The position of the last character in range</param>
    internal int GetTrailingWhitespaceStartPosition(int start, int end)
    {
        var pos = end;
        var text = _sourceFile.Text();
        while (pos >= start)
        {
            var ch = Scan.DecodeRuneAt(text, pos, out var size);
            if (size == 0)
            {
                pos--; // multibyte character, rewind more
                continue;
            }

            if (!StringUtil.StringUtil.IsWhiteSpaceSingleLine(ch))
            {
                break;
            }

            pos--;
        }

        if (pos != end)
        {
            return pos + 1;
        }

        return -1;
    }

    internal void InsertIndentation(int pos, int indentation, bool lineAdded)
    {
        var indentationString = Format.GetIndentationString(indentation, _formattingContext!.Options);
        if (lineAdded)
        {
            // new line is added before the token by the formatting rules
            // insert indentation string at the very beginning of the token
            RecordReplace(pos, 0, indentationString);
        }
        else
        {
            var (tokenStartLine, tokenStartCharacter) = Scan.GetECMALineAndByteOffsetOfPosition(_sourceFile, pos);
            var startLinePosition = Scan.GetECMALineStarts(_sourceFile)[tokenStartLine];
            if (indentation != CharacterToColumn(startLinePosition, tokenStartCharacter) || IndentationIsDifferent(indentationString, startLinePosition))
            {
                RecordReplace(startLinePosition, tokenStartCharacter, indentationString);
            }
        }
    }

    internal int CharacterToColumn(int startLinePosition, int characterInLine)
    {
        var column = 0;
        var text = _sourceFile.Text();
        for (var i = 0; i < characterInLine; i++)
        {
            if (text[startLinePosition + i] == '\t')
            {
                if (_formattingContext!.Options.TabSize > 0)
                {
                    column += _formattingContext.Options.TabSize - (column % _formattingContext.Options.TabSize);
                }
            }
            else
            {
                column++;
            }
        }

        return column;
    }

    internal bool IndentationIsDifferent(string indentationString, int startLinePosition)
    {
        var text = _sourceFile.Text();
        var end = startLinePosition + indentationString.Length;
        if (end > text.Length)
        {
            return true;
        }

        return !indentationString.AsSpan().SequenceEqual(text.AsSpan(startLinePosition, indentationString.Length));
    }

    internal bool IndentTriviaItems(TextRangeWithKind[] trivia, int commentIndentation, bool indentNextTokenOrTrivia, Action<TextRangeWithKind> indentSingleLine)
    {
        foreach (var triviaItem in trivia)
        {
            var triviaInRange = triviaItem.Loc.ContainedBy(_originalRange);
            switch (triviaItem.Kind)
            {
                case Kind.MultiLineCommentTrivia:
                    if (triviaInRange)
                    {
                        IndentMultilineComment(triviaItem.Loc, commentIndentation, !indentNextTokenOrTrivia, true);
                    }

                    indentNextTokenOrTrivia = false;
                    break;
                case Kind.SingleLineCommentTrivia:
                    if (indentNextTokenOrTrivia && triviaInRange)
                    {
                        indentSingleLine(triviaItem);
                    }

                    indentNextTokenOrTrivia = false;
                    break;
                case Kind.NewLineTrivia:
                    indentNextTokenOrTrivia = true;
                    break;
            }
        }

        return indentNextTokenOrTrivia;
    }

    internal void IndentMultilineComment(TextRange commentRange, int indentation, bool firstLineIsIndented, bool indentFinalLine)
    {
        // split comment in lines
        var startLine = Scan.GetECMALineOfPosition(_sourceFile, commentRange.Pos());
        var endLine = Scan.GetECMALineOfPosition(_sourceFile, commentRange.End());

        if (startLine == endLine)
        {
            if (!firstLineIsIndented)
            {
                // treat as single line comment
                InsertIndentation(commentRange.Pos(), indentation, false);
            }

            return;
        }

        var parts = new List<TextRange>(CountNewLines(_sourceFile.Text().AsSpan(commentRange.Pos(), commentRange.Len())));
        var startPos = commentRange.Pos();
        for (var line = startLine; line < endLine; line++)
        {
            var endOfLine = Scan.GetECMAEndLinePosition(_sourceFile, line);
            parts.Add(NewTextRange(startPos, endOfLine));
            startPos = Scan.GetECMALineStarts(_sourceFile)[line + 1];
        }

        if (indentFinalLine)
        {
            parts.Add(NewTextRange(startPos, commentRange.End()));
        }

        if (parts.Count == 0)
        {
            return;
        }

        var startLinePos = Scan.GetECMALineStarts(_sourceFile)[startLine];

        var (nonWhitespaceInFirstPartCharacter, nonWhitespaceInFirstPartColumn) = Format.FindFirstNonWhitespaceCharacterAndColumn(startLinePos, parts[0].Pos(), _sourceFile, _formattingContext!.Options);

        var startIndex = 0;

        if (firstLineIsIndented)
        {
            startIndex = 1;
            startLine++;
        }

        // shift all parts on the delta size
        var delta = indentation - nonWhitespaceInFirstPartColumn;
        for (var i = startIndex; i < parts.Count; i++)
        {
            // Go 在这里用 `:=` 遮蔽了外层的 startLinePos；C# 不允许同名遮蔽（CS0136），故改名 lineStartPos。
            var lineStartPos = Scan.GetECMALineStarts(_sourceFile)[startLine];
            var nonWhitespaceCharacter = nonWhitespaceInFirstPartCharacter;
            var nonWhitespaceColumn = nonWhitespaceInFirstPartColumn;
            if (i != 0)
            {
                (nonWhitespaceCharacter, nonWhitespaceColumn) = Format.FindFirstNonWhitespaceCharacterAndColumn(parts[i].Pos(), parts[i].End(), _sourceFile, _formattingContext.Options);
            }

            var newIndentation = nonWhitespaceColumn + delta;
            if (newIndentation > 0)
            {
                var indentationString = Format.GetIndentationString(newIndentation, _formattingContext.Options);
                RecordReplace(lineStartPos, nonWhitespaceCharacter, indentationString);
            }
            else
            {
                RecordDelete(lineStartPos, nonWhitespaceCharacter);
            }

            startLine++;
        }
    }

    /// <summary>Go: <c>strings.Count(w.sourceFile.Text()[commentRange.Pos():commentRange.End()], "\n")</c>。</summary>
    internal static int CountNewLines(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            if (ch == '\n')
            {
                count++;
            }
        }

        return count;
    }

    internal static TextChange CreateTextChangeFromStartLength(int start, int length, string newText)
    {
        return new TextChange(NewTextRange(start, start + length), newText);
    }

    internal void RecordDelete(int start, int length)
    {
        if (length != 0)
        {
            _edits.Add(CreateTextChangeFromStartLength(start, length, ""));
        }
    }

    internal void RecordReplace(int start, int length, string newText)
    {
        if (length != 0 || newText != "")
        {
            _edits.Add(CreateTextChangeFromStartLength(start, length, newText));
        }
    }

    internal void RecordInsert(int start, string text)
    {
        if (text != "")
        {
            _edits.Add(CreateTextChangeFromStartLength(start, 0, text));
        }
    }
}

public static partial class Format
{
    internal static bool IsStringOrRegularExpressionOrTemplateLiteral(Kind kind)
    {
        return kind == Kind.StringLiteral || kind == Kind.RegularExpressionLiteral || IsTemplateLiteralKind(kind);
    }

    internal static bool IsComment(Kind kind)
    {
        return kind == Kind.SingleLineCommentTrivia || kind == Kind.MultiLineCommentTrivia;
    }

    /// <summary>Go 的 <c>func getIndentationString(indentation int, options lsutil.FormatCodeSettings) string</c>。</summary>
    internal static string GetIndentationString(int indentation, FormatCodeSettings options)
    {
        // go's `strings.Repeat` already has static, global caching for repeated tabs and spaces, so there's no need to cache here like in strada
        if (!options.ConvertTabsToSpaces.IsTrue())
        {
            if (options.TabSize == 0)
            {
                return "";
            }

            var tabs = (int)Math.Floor((double)indentation / options.TabSize);
            var spaces = indentation - (tabs * options.TabSize);
            var res = new string('\t', tabs);
            if (spaces > 0)
            {
                res += new string(' ', spaces);
            }

            return res;
        }
        else
        {
            return new string(' ', indentation);
        }
    }
}
