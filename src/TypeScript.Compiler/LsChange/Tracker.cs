// ported from: tsc/internal/ls/change/tracker.go:1-828
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `Tracker` 内嵌 `*printer.EmitContext` 与 `*ast.NodeFactory`（方法被提升）→ C# 用显式 public 字段
//    `EmitContext`（`PrinterNs.EmitContext`）与 `NodeFactory`（`Ast.NodeFactory`）：Go 的 `t.NewToken(...)`
//    → `NodeFactory.NewToken(...)`，`t.Factory` 等由 `EmitContext` 提供。另加 `AsNodeFactory()` 并在基类型表里
//    显式写 `NodeFactoryCoercible`（Go 里 `*Tracker` 靠内嵌的 NodeFactory **结构化**满足该接口，C# 必须声明）——
//    消费方（LsAutoImport）需要把 Tracker 当 NodeFactory 交给别的 API。
// 2) `Tracker.writer *printer.ChangeTrackerWriter` 在本包内从未被读写（全包 grep 确认）→ 省略该字段。
//    Go 的 `deletedNodes`（nil 切片）→ C# `List<DeletedNode>` 字段初值 `[]`（Go 的 append 到 nil 切片同义）。
// 3) Go 未导出字段 → `internal _camelCase`；未导出方法 → `internal` + PascalCase；导出成员原名照抄（§2、§3.3）。
// 4) `struct NodeOptions`（值语义）+ 内嵌 `LeadingTriviaOption`/`TrailingTriviaOption` 的字段提升 → C# 用
//    `sealed class NodeOptions`，提升字段拉平成同名 public 字段。后果：`ReplaceRange(sourceFile, range, node,
//    options)` 在 C# 里存的是**同一个实例引用**（Go 存的是拷贝）。调用点只读 options，语义不受影响。
//    Go 的 `NodeOptions{Prefix: ": "}` → `new NodeOptions { Prefix = ": " }`。
// 5) Go 的包级函数（含未导出的 `isSeparator`/`findIndentationColumn`/`rangeContainsRangeExclusive` 等）→ 与
//    package 同名的静态类 `LsChange`（§2），按 Go 的文件归属分散在 LsChange.cs（NewTracker/
//    GetFormatCodeSettingsForWriting）、本文件、TrackerImpl.cs、Delete.cs 四个 partial 声明里；本目录内的调用
//    点用 `using static TypeScript.LsChange.LsChange;` 裸名调用，文本与 Go 一致。
// 6) 命名空间遮蔽：本切片里**不能**裸写 `ast.X`/`format.X`/`printer.X`/`scanner.X`/`spanmap.X`——这些名字先解析到
//    同名命名空间（CS0118）。改用 `using static` 后裸写成员名（仓内先例：Binder/Checker 的
//    `using static TypeScript.Scanner.Scanner;`、LsUtil/Asi.cs、Format/Api.cs）。`TypeScript.SpanMap` 只以别名
//    `SpanMapNs` 引入，否则它的 `Kind` 会与 `Ast.Kind` 撞名（CS0104）；`Fidelity.IsExact()` 是
//    `SpanMapExtensions` 上的扩展方法，而扩展方法**不能**经 `using static`/别名命名空间导入到简单名作用域
//    （且直接 `using TypeScript.SpanMap;` 会带回 `Kind` 撞名）→ 用 `using SpanMapExt = ...SpanMapExtensions;`
//    后写成 `SpanMapExt.IsExact(fidelity)`（语义与 Go 的 `fidelity.IsExact()` 完全相同）。
//    同理 `PositionsAreOnSameLine` 在本切片（delete.go）与 `Printer` 里各有一个同名函数，二者都被
//    `using static` 引入 → 调用点显式写成 `LsChange.PositionsAreOnSameLine(...)` 消歧。
// 7) Go 的 `[]*ast.Node`（可能 nil）→ `Node[]`；`NodeList.Nodes` 是 `Node[]?`，消费处 `?? []`。
// 8) Go 的 `panic(...)` → `Debug.Fail(...)`；其后仍需返回值的地方补
//    `throw new System.Diagnostics.UnreachableException();`（禁止 `using System.Diagnostics;`）。
// 9) Go 里 `node == other` 这类**指针**比较 → `ReferenceEquals(node, other)`（SourceFile/Node 都是 class）。
// 10) `TypeScript.LsProto.Range` 与 implicit usings 带进来的 `System.Range` 同名（CS0104）→ 文件顶部
//     `using Range = TypeScript.LsProto.Range;`（同 LsProto/LspTests.cs 与 LsConv/Converters.cs 的做法）。
//     `TextEdit`/`Position` 仍走 `using TypeScript.LsProto;`；而 `LsProto` **静态类**上的
//     CompareRanges/ComparePositions 必须 `using static TypeScript.LsProto.LsProto;` 后裸名调用
//     （写 `LsProto.CompareRanges(...)` 会先绑到同名命名空间 → CS0234）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Format.Format;
using static TypeScript.LsChange.LsChange;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
using SpanMapExt = TypeScript.SpanMap.SpanMapExtensions;
using SpanMapNs = TypeScript.SpanMap;
using PrinterNs = TypeScript.Printer;
using Range = TypeScript.LsProto.Range;

namespace TypeScript.LsChange;

/// <summary>
/// Go: <c>type NodeOptions struct</c>（tracker.go:19-36）。值语义 → `sealed class`（见文件头第 4 条）；
/// 内嵌的 `LeadingTriviaOption`/`TrailingTriviaOption` 字段提升拉平为同名字段。
/// </summary>
public sealed class NodeOptions
{
    /// <summary>Text to be inserted before the new node（Go: <c>Prefix</c>）。</summary>
    public string Prefix = "";

    /// <summary>Text to be inserted after the new node（Go: <c>Suffix</c>）。</summary>
    public string Suffix = "";

    /// <summary>Go: <c>indentation *int</c>（未导出）。</summary>
    internal int? _indentation;

    /// <summary>Go: <c>delta *int</c>（未导出）。</summary>
    internal int? _delta;

    /// <summary>Go: <c>joiner string</c>（未导出）。</summary>
    internal string _joiner = "";

    public LeadingTriviaOption LeadingTriviaOption;

    public TrailingTriviaOption TrailingTriviaOption;
}

/// <summary>Go: <c>type LeadingTriviaOption int</c>（成员名去掉 Go 的 <c>LeadingTriviaOption</c> 前缀）。</summary>
public enum LeadingTriviaOption : int
{
    None = 0,
    Exclude = 1,
    IncludeAll = 2,
    JSDoc = 3,
    StartLine = 4,
}

/// <summary>Go: <c>type TrailingTriviaOption int</c>（成员名去掉 Go 的 <c>TrailingTriviaOption</c> 前缀）。</summary>
public enum TrailingTriviaOption : int
{
    None = 0,
    Exclude = 1,
    ExcludeWhitespace = 2,
    Include = 3,
}

/// <summary>Go: <c>type trackerEditKind int</c>（未导出）。</summary>
internal enum TrackerEditKind : int
{
    Text = 1,
    Remove = 2,
    ReplaceWithSingleNode = 3,
    ReplaceWithMultipleNodes = 4,
}

/// <summary>Go: <c>type trackerEdit struct</c>（未导出）。内嵌的 <c>core.TextRange</c> → 同名字段。</summary>
internal sealed class TrackerEdit
{
    internal TrackerEditKind Kind;
    internal TextRange TextRange; // Go: 内嵌 core.TextRange（字段名即类型名）
    internal string NewText = ""; // kind == Text
    internal Node? Node; // single
    internal Node[]? Nodes; // multiple
    internal NodeOptions Options = new();
}

/// <summary>Go: <c>type nodesInsertedAtStartState struct</c>（未导出）。</summary>
internal sealed class NodesInsertedAtStartState
{
    internal Node Node;
    internal SourceFile SourceFile;

    internal NodesInsertedAtStartState(Node node, SourceFile sourceFile)
    {
        Node = node;
        SourceFile = sourceFile;
    }
}

/// <summary>Go: <c>type Tracker struct</c>（tracker.go:61-88）。Go 里 `*Tracker` **结构化**满足
/// <c>ast.NodeFactoryCoercible</c>（内嵌的 `*ast.NodeFactory` 提升了 `AsNodeFactory()`）；C# 没有结构化满足，
/// 所以在基类型表里显式写上。</summary>
public sealed partial class Tracker : NodeFactoryCoercible
{
    /// <summary>Go: 内嵌 <c>*printer.EmitContext</c>（见文件头第 1 条）。</summary>
    public PrinterNs.EmitContext EmitContext;

    /// <summary>Go: 内嵌 <c>*ast.NodeFactory</c>（见文件头第 1 条）。</summary>
    public Ast.NodeFactory NodeFactory;

    // initialized with
    internal FormatCodeSettings _formatSettings;
    internal string _newLine;
    internal Converters _converters;
    internal Context _ctx;

    internal MultiMap<SourceFile, TrackerEdit> _changes;
    internal List<DeletedNode> _deletedNodes = [];
    internal Dictionary<Node, NodesInsertedAtStartState> _nodesWithInsertionsAtStart;

    // unmappableFiles collects the files for which an edit could not be represented within a single
    // verbatim span of the original text. GetChanges drops their edits so a partial, corrupting change is
    // never emitted for a content-mapped file.
    internal Set<string> _unmappableFiles = new();

    // created during call to getChanges
    // !!! writer *printer.ChangeTrackerWriter（见文件头第 2 条，本包内从未使用 → 省略）

    /// <summary>Go: <c>func NewTracker(ctx context.Context, compilerOptions *core.CompilerOptions,
    /// formatOptions lsutil.FormatCodeSettings, converters *lsconv.Converters) *Tracker</c>。公开入口见
    /// <see cref="LsChange.NewTracker"/>；这个构造函数供包内和需要绕开命名空间遮蔽的消费方使用。</summary>
    public Tracker(PrinterNs.EmitContext emitContext, Context ctx, Converters converters, FormatCodeSettings formatOptions, string newLine)
    {
        EmitContext = emitContext;
        NodeFactory = emitContext.Factory;
        _changes = new MultiMap<SourceFile, TrackerEdit>();
        _ctx = ctx;
        _converters = converters;
        _formatSettings = formatOptions;
        _newLine = newLine;
        _nodesWithInsertionsAtStart = new Dictionary<Node, NodesInsertedAtStartState>();
    }

    /// <summary>供消费方（LsAutoImport）把 Tracker 当 <see cref="NodeFactory"/> 用（见文件头第 1 条）。</summary>
    public Ast.NodeFactory AsNodeFactory() => NodeFactory;

    /// <summary>同义于 <see cref="LsChange.NewTracker"/> 的静态工厂：`Tracker` 这个名字在任何命名空间里都先解析到
    /// 类型（不像 `LsChange` 会先绑到同名命名空间），所以外部消费方用它最省事（见 LsChange.cs 文件头）。</summary>
    public static Tracker NewTracker(Context ctx, CompilerOptions compilerOptions, FormatCodeSettings formatOptions, Converters converters)
    {
        return LsChange.NewTracker(ctx, compilerOptions, formatOptions, converters);
    }

    // GetChanges returns the accumulated text edits grouped by file name. Any file whose edits could not be
    // faithfully mapped back onto content-mapped original text is omitted from the returned map, and its name
    // is included in the returned slice. Dropping the whole file (rather than the individual edit) keeps a
    // logical change atomic, and returning the result inline means a caller cannot forget to check it or
    // accidentally emit a partial, corrupting change.
    // Note: after calling this, the Tracker object must be discarded!
    public (Dictionary<string, TextEdit[]> Changes, string[]? UnmappableFiles) GetChanges()
    {
        FinishDeleteDeclarations();
        FinishNodesWithInsertionsAtStart();
        var changes = GetTextChangesFromChanges();
        // !!! changes for new files
        if (_unmappableFiles.Len() == 0)
        {
            return (changes, null);
        }

        var unmappable = new List<string>(_unmappableFiles.Len());
        foreach (var fileName in _unmappableFiles.Keys())
        {
            changes.Remove(fileName);
            unmappable.Add(fileName);
        }

        unmappable.Sort(StringComparer.Ordinal); // Go: slices.Sort（字符串按字节序）
        return (changes, unmappable.ToArray());
    }

    // fromLSPEditRange converts an LSP range to a source file range. For a content-mapped file, an original
    // range may have several projections; this selects the one belonging to sourceFile. A range with no exact
    // projection cannot be written back and marks the file unmappable.
    internal TextRange FromLSPEditRange(SourceFile sourceFile, Range lsprotoRange)
    {
        var spans = _converters.FromLSPRangeForSourceFile(sourceFile, lsprotoRange, SpanMapNs.Feature.All);
        foreach (var span in spans)
        {
            if (SpanMapExt.IsExact(span.Fidelity) && ReferenceEquals(span.Script, sourceFile))
            {
                return span.Span;
            }
        }

        _unmappableFiles.Add(sourceFile.OriginalFileName());
        if (spans.Length > 0)
        {
            return spans[0].Span;
        }

        return NewTextRange(0, 0);
    }

    // toLSPEditRange converts a source file range to an LSP range. For a content-mapped file, the range is
    // mapped back to the original document. If it does not fall entirely within a single verbatim span, the
    // edit cannot be represented safely: the file is recorded so GetChanges drops its edits, and a best-effort
    // range is returned so the accumulated edits stay well-formed.
    internal Range ToLSPEditRange(SourceFile sourceFile, TextRange textRange)
    {
        var (r, fidelity) = _converters.ToLSPRange(sourceFile, textRange);
        if (!SpanMapExt.IsExact(fidelity))
        {
            // The range does not map into a single verbatim span, so the edit cannot be represented safely in
            // the original text. Record the file so GetChanges drops its edits, keeping the best-effort range so
            // the accumulated edits stay well-formed.
            _unmappableFiles.Add(sourceFile.OriginalFileName());
        }

        return r;
    }

    public void ReplaceNode(SourceFile sourceFile, Node oldNode, Node newNode, NodeOptions? options)
    {
        if (options == null)
        {
            // defaults to `useNonAdjustedPositions`
            options = new NodeOptions
            {
                LeadingTriviaOption = LeadingTriviaOption.Exclude,
                TrailingTriviaOption = TrailingTriviaOption.Exclude,
            };
        }

        ReplaceRange(sourceFile, GetAdjustedRange(sourceFile, oldNode, oldNode, options.LeadingTriviaOption, options.TrailingTriviaOption), newNode, options);
    }

    public void ReplaceNodeWithNodes(SourceFile sourceFile, Node oldNode, Node[] newNodes, NodeOptions? options)
    {
        if (options == null)
        {
            options = new NodeOptions
            {
                LeadingTriviaOption = LeadingTriviaOption.Exclude,
                TrailingTriviaOption = TrailingTriviaOption.Exclude,
            };
        }

        ReplaceRangeWithNodes(sourceFile, GetAdjustedRange(sourceFile, oldNode, oldNode, options.LeadingTriviaOption, options.TrailingTriviaOption), newNodes, options);
    }

    /// <summary>ReplaceRange replaces textRange in sourceFile with newNode.</summary>
    public void ReplaceRange(SourceFile sourceFile, TextRange textRange, Node newNode, NodeOptions options)
    {
        _changes.Add(sourceFile, new TrackerEdit { Kind = TrackerEditKind.ReplaceWithSingleNode, TextRange = textRange, Options = options, Node = newNode });
    }

    /// <summary>ReplaceRangeWithText replaces an LSP range with text. For a content-mapped file, the LSP range is in the
    /// original document; text may be placed at any exact projection because it does not need formatting context.</summary>
    public void ReplaceRangeWithText(SourceFile sourceFile, Range lsprotoRange, string text)
    {
        ReplaceTextRangeWithText(sourceFile, FromLSPEditRange(sourceFile, lsprotoRange), text);
    }

    /// <summary>ReplaceTextRangeWithText replaces textRange in sourceFile with text. For a content-mapped file, GetChanges
    /// maps the range back to the original document and drops the file if the edit cannot be represented there.</summary>
    public void ReplaceTextRangeWithText(SourceFile sourceFile, TextRange textRange, string text)
    {
        _changes.Add(sourceFile, new TrackerEdit { Kind = TrackerEditKind.Text, TextRange = textRange, NewText = text });
    }

    /// <summary>ReplaceRangeWithNodes replaces textRange in sourceFile with newNodes.</summary>
    public void ReplaceRangeWithNodes(SourceFile sourceFile, TextRange textRange, Node[] newNodes, NodeOptions options)
    {
        if (newNodes.Length == 1)
        {
            ReplaceRange(sourceFile, textRange, newNodes[0], options);
            return;
        }

        _changes.Add(sourceFile, new TrackerEdit { Kind = TrackerEditKind.ReplaceWithMultipleNodes, TextRange = textRange, Nodes = newNodes, Options = options });
    }

    /// <summary>insertTextAt inserts text at an offset in sourceFile.</summary>
    internal void InsertTextAt(SourceFile sourceFile, TextPos pos, string text)
    {
        ReplaceTextRangeWithText(sourceFile, NewTextRange(pos, pos), text);
    }

    /// <summary>InsertText inserts text at an LSP position.</summary>
    public void InsertText(SourceFile sourceFile, Position pos, string text)
    {
        // Go: lsproto.Range{Start: pos, End: pos}（Position 是值类型 ⇒ 两个独立副本）
        ReplaceRangeWithText(sourceFile, new Range { Start = pos, End = new Position { Line = pos.Line, Character = pos.Character } }, text);
    }

    public void InsertNodeAt(SourceFile sourceFile, TextPos pos, Node newNode, NodeOptions options)
    {
        ReplaceRange(sourceFile, NewTextRange(pos, pos), newNode, options);
    }

    public void InsertNodesAt(SourceFile sourceFile, TextPos pos, Node[] newNodes, NodeOptions options)
    {
        ReplaceRangeWithNodes(sourceFile, NewTextRange(pos, pos), newNodes, options);
    }

    public void InsertNodeAfter(SourceFile sourceFile, Node after, Node newNode)
    {
        var endPosition = EndPosForInsertNodeAfter(sourceFile, after, newNode);
        InsertNodeAt(sourceFile, endPosition, newNode, GetInsertNodeAfterOptions(sourceFile, after));
    }

    public void InsertNodesAfter(SourceFile sourceFile, Node after, Node[] newNodes)
    {
        var endPosition = EndPosForInsertNodeAfter(sourceFile, after, newNodes[0]);
        InsertNodesAt(sourceFile, endPosition, newNodes, GetInsertNodeAfterOptions(sourceFile, after));
    }

    public void InsertNodeBefore(SourceFile sourceFile, Node before, Node newNode, bool blankLineBetween, LeadingTriviaOption leadingTriviaOption)
    {
        InsertNodeAt(sourceFile, new TextPos(GetAdjustedStartPosition(sourceFile, before, leadingTriviaOption, false)), newNode, GetOptionsForInsertNodeBefore(before, newNode, blankLineBetween));
    }

    /// <summary>TryInsertTypeAnnotation inserts a type annotation after the appropriate position on a node
    /// (after the close paren for function-like, after the name/exclamation/question for variable-like).
    /// Returns true if successful.</summary>
    public bool TryInsertTypeAnnotation(SourceFile sourceFile, Node node, Node typeNode)
    {
        Node? endNode;
        if (IsFunctionLike(node))
        {
            endNode = FindChildOfKind(node, Kind.CloseParenToken, sourceFile);
            if (endNode == null)
            {
                if (!IsArrowFunction(node))
                {
                    return false;
                }

                // If no `)`, is an arrow function `x => x`, so use the end of the first parameter
                var parameters = node.Parameters() ?? [];
                if (parameters.Length == 0)
                {
                    return false;
                }

                endNode = parameters[0];
            }
        }
        else
        {
            switch (node.Kind)
            {
                case Kind.VariableDeclaration:
                    endNode = node.AsVariableDeclaration()!.ExclamationToken;
                    break;
                case Kind.PropertySignature:
                    endNode = node.AsPropertySignatureDeclaration()!.PostfixToken;
                    break;
                case Kind.PropertyDeclaration:
                    endNode = node.AsPropertyDeclaration()!.PostfixToken;
                    break;
                case Kind.Parameter:
                    endNode = node.AsParameterDeclaration()!.QuestionToken;
                    break;
                default:
                    endNode = null;
                    break;
            }

            endNode ??= node.Name();
        }

        if (endNode == null)
        {
            return false;
        }

        InsertNodeAt(sourceFile, new TextPos(endNode.End()), typeNode, new NodeOptions { Prefix = ": " });
        return true;
    }

    /// <summary>ParenthesizeArrowParameters wraps the parameters of a paren-less arrow function in `(` and `)`.
    /// This is a no-op if the arrow function already has parens.</summary>
    public void ParenthesizeArrowParameters(SourceFile sourceFile, Node arrowFunc)
    {
        if (FindChildOfKind(arrowFunc, Kind.CloseParenToken, sourceFile) != null)
        {
            return;
        }

        var parameters = arrowFunc.Parameters() ?? [];
        if (parameters.Length == 0)
        {
            return;
        }

        var firstParam = parameters[0];
        var lastParam = parameters[^1];
        var startPos = GetStartOfNode(firstParam, sourceFile, false);
        InsertTextAt(sourceFile, new TextPos(startPos), "(");
        InsertTextAt(sourceFile, new TextPos(lastParam.End()), ")");
    }

    /// <summary>InsertModifierBefore inserts a modifier token (like 'type') before a node with a trailing space.</summary>
    public void InsertModifierBefore(SourceFile sourceFile, Kind modifier, Node before)
    {
        var pos = GetStartOfNode(before, sourceFile, false);
        var token = NodeFactory.NewToken(modifier);
        token.Loc = NewTextRange(pos, pos);
        token.Parent = before.Parent;
        InsertNodeAt(sourceFile, new TextPos(pos), token, new NodeOptions { Suffix = " " });
    }

    /// <summary>Delete queues a node for deletion with smart handling of list items, imports, etc.
    /// The actual deletion happens in finishDeleteDeclarations during GetChanges.</summary>
    public void Delete(SourceFile sourceFile, Node node)
    {
        _deletedNodes.Add(new DeletedNode(sourceFile, node));
    }

    /// <summary>DeleteRange deletes a text range from the source file.</summary>
    public void DeleteRange(SourceFile sourceFile, TextRange textRange)
    {
        ReplaceTextRangeWithText(sourceFile, textRange, "");
    }

    /// <summary>DeleteNode deletes a node immediately with specified trivia options.
    /// Stop! Consider using Delete instead, which has logic for deleting nodes from delimited lists.</summary>
    public void DeleteNode(SourceFile sourceFile, Node node, LeadingTriviaOption leadingTrivia, TrailingTriviaOption trailingTrivia)
    {
        ReplaceTextRangeWithText(sourceFile, GetAdjustedRange(sourceFile, node, node, leadingTrivia, trailingTrivia), "");
    }

    /// <summary>DeleteNodeRange deletes a range of nodes with specified trivia options.</summary>
    public void DeleteNodeRange(SourceFile sourceFile, Node startNode, Node endNode, LeadingTriviaOption leadingTrivia, TrailingTriviaOption trailingTrivia)
    {
        var startPosition = GetAdjustedStartPosition(sourceFile, startNode, leadingTrivia, false);
        var endPosition = GetAdjustedEndPosition(sourceFile, endNode, trailingTrivia);
        ReplaceTextRangeWithText(sourceFile, NewTextRange(startPosition, endPosition), "");
    }

    /// <summary>finishDeleteDeclarations processes all queued deletions with smart handling for lists and trailing commas.</summary>
    internal void FinishDeleteDeclarations()
    {
        var deletedNodesInLists = new Dictionary<Node, bool>();

        foreach (var deleted in _deletedNodes)
        {
            // Skip if this node is contained within another deleted node
            var isContained = false;
            foreach (var other in _deletedNodes)
            {
                if (ReferenceEquals(other.SourceFile, deleted.SourceFile) && !ReferenceEquals(other.Node, deleted.Node) &&
                    RangeContainsRangeExclusive(other.Node, deleted.Node))
                {
                    isContained = true;
                    break;
                }
            }

            if (isContained)
            {
                continue;
            }

            DeleteDeclaration(this, deletedNodesInLists, deleted.SourceFile, deleted.Node);
        }

        // Handle trailing commas for last elements in lists
        foreach (var node in deletedNodesInLists.Keys)
        {
            var sourceFile = GetSourceFileOfNode(node);
            var list = GetContainingList(node, sourceFile!);
            if (list == null || !ReferenceEquals(node, (list.Nodes ?? [])[^1]))
            {
                continue;
            }

            var lastNonDeletedIndex = -1;
            for (var i = (list.Nodes ?? []).Length - 2; i >= 0; i--)
            {
                if (!deletedNodesInLists.ContainsKey(list.Nodes![i]))
                {
                    lastNonDeletedIndex = i;
                    break;
                }
            }

            if (lastNonDeletedIndex != -1)
            {
                var start = list.Nodes![lastNonDeletedIndex].End();
                var end = StartPositionToDeleteNodeInList(sourceFile!, list.Nodes[lastNonDeletedIndex + 1]);
                ReplaceTextRangeWithText(sourceFile!, NewTextRange(start, end), "");
            }
        }
    }

    internal TextPos EndPosForInsertNodeAfter(SourceFile sourceFile, Node after, Node newNode)
    {
        if (NeedSemicolonBetween(after, newNode) && sourceFile.Text()[after.End() - 1] != ';')
        {
            // check if previous statement ends with semicolon
            // if not - insert semicolon to preserve the code from changing the meaning due to ASI
            var endPos = after.End();
            var semicolon = NodeFactory.NewToken(Kind.SemicolonToken);
            semicolon.Loc = NewTextRange(after.End(), after.End());
            semicolon.Parent = after.Parent;
            ReplaceRange(
                sourceFile,
                NewTextRange(endPos, endPos),
                semicolon,
                new NodeOptions());
        }

        return new TextPos(GetAdjustedEndPosition(sourceFile, after, TrailingTriviaOption.None));
    }

    /// <summary>
    /// This function should be used to insert nodes in lists when nodes don't carry separators as the part of the node range,
    /// i.e. arguments in arguments lists, parameters in parameter lists etc.
    /// Note that separators are part of the node in statements and class elements.
    /// </summary>
    public void InsertNodeInListAfter(SourceFile sourceFile, Node after, Node newNode, NodeList? containingList)
    {
        containingList ??= GetContainingList(after, sourceFile);
        if (containingList == null)
        {
            // Debug.fail("node is not a list element")
            return;
        }

        var listNodes = containingList.Nodes ?? [];
        var index = Array.IndexOf(listNodes, after);
        if (index < 0)
        {
            return;
        }

        var end = after.End();
        if (index != listNodes.Length - 1)
        {
            // any element except the last one
            // use next sibling as an anchor
            var nextToken = GetTokenAtPosition(sourceFile, after.End());
            if (nextToken != null && IsSeparator(after, nextToken))
            {
                // for list
                // a, b, c
                // create change for adding 'e' after 'a' as
                // - find start of next element after a (it is b)
                // - use next element start as start and end position in final change
                // - build text of change by formatting the text of node + whitespace trivia of b

                // in multiline case it will work as
                //   a,
                //   b,
                //   c,
                // result - '*' denotes leading trivia that will be inserted after new text (displayed as '#')
                //   a,
                //   insertedtext<separator>#
                // ###b,
                //   c,
                var nextNode = listNodes[index + 1];
                var startPos = SkipTriviaEx(sourceFile.Text(), nextNode.Pos(), new SkipTriviaOptions { StopAfterLineBreak = false, StopAtComments = true });

                // write separator and leading trivia of the next element as suffix
                var suffix = TokenToString(nextToken.Kind) + sourceFile.Text()[nextToken.End()..startPos];
                InsertNodesAt(sourceFile, new TextPos(startPos), [newNode], new NodeOptions { Suffix = suffix });
            }

            return;
        }

        var afterStart = GetStartOfNode(after, sourceFile, false);
        var afterStartLinePosition = GetLineStartPositionForPosition(afterStart, sourceFile);

        // insert element after the last element in the list that has more than one item
        // pick the element preceding the after element to:
        // - pick the separator
        // - determine if list is a multiline
        var multilineList = false;

        // if list has only one element then we'll format is as multiline if node has comment in trailing trivia, or as singleline otherwise
        // i.e. var x = 1 // this is x
        //     | new element will be inserted at this position
        var separator = Kind.CommaToken; // SyntaxKind.CommaToken | SyntaxKind.SemicolonToken
        if (listNodes.Length != 1)
        {
            // otherwise, if list has more than one element, pick separator from the list
            var tokenBeforeInsertPosition = FindPrecedingToken(sourceFile, after.Pos());
            separator = IfElse(IsSeparator(after, tokenBeforeInsertPosition), tokenBeforeInsertPosition!.Kind, Kind.CommaToken);
            // determine if list is multiline by checking lines of after element and element that precedes it.
            var afterMinusOneStartLinePosition = GetLineStartPositionForPosition(GetStartOfNode(listNodes[index - 1], sourceFile, false), sourceFile);
            multilineList = afterMinusOneStartLinePosition != afterStartLinePosition;
        }

        if (HasCommentsBeforeLineBreak(sourceFile.Text(), after.End()) || !LsChange.PositionsAreOnSameLine(containingList.Pos(), containingList.End(), sourceFile))
        {
            // in this case we'll always treat containing list as multiline
            multilineList = true;
        }

        if (multilineList)
        {
            // insert separator immediately following the 'after' node to preserve comments in trailing trivia
            var separatorToken = NodeFactory.NewToken(separator);
            var separatorString = TokenToString(separator);
            separatorToken.Loc = NewTextRange(end, end + separatorString.Length);
            separatorToken.Parent = after.Parent;
            var endPos = end;
            ReplaceRange(sourceFile, NewTextRange(endPos, endPos), separatorToken, new NodeOptions());
            // use the same indentation as 'after' item
            var indentation = FindFirstNonWhitespaceColumn(afterStartLinePosition, afterStart, sourceFile, _formatSettings);
            // insert element before the line break on the line that contains 'after' element
            var insertPos = SkipTriviaEx(sourceFile.Text(), end, new SkipTriviaOptions { StopAfterLineBreak = true, StopAtComments = false });
            // find position before "\n" or "\r\n"
            while (insertPos != end && IsLineBreak(sourceFile.Text()[insertPos - 1]))
            {
                insertPos--;
            }

            ReplaceRange(
                sourceFile,
                NewTextRange(insertPos, insertPos),
                newNode,
                new NodeOptions
                {
                    _indentation = indentation,
                    Prefix = _newLine,
                });
        }
        else
        {
            var separatorString = TokenToString(separator);
            var endPos = end;
            ReplaceRange(sourceFile, NewTextRange(endPos, endPos), newNode, new NodeOptions { Prefix = separatorString + " " });
        }
    }

    /// <summary>InsertImportSpecifierAtIndex inserts a new import specifier at the specified index in a NamedImports list</summary>
    public void InsertImportSpecifierAtIndex(SourceFile sourceFile, Node newSpecifier, Node namedImports, int index)
    {
        var namedImportsNode = namedImports.AsNamedImports()!;
        var elements = namedImportsNode.Elements?.Nodes ?? [];

        Node? prevSpecifier = null;
        if (index > 0 && index - 1 < elements.Length)
        {
            prevSpecifier = elements[index - 1];
        }

        if (prevSpecifier != null)
        {
            InsertNodeInListAfter(sourceFile, prevSpecifier, newSpecifier, null);
        }
        else
        {
            InsertNodeBefore(
                sourceFile,
                elements[0],
                newSpecifier,
                !LsChange.PositionsAreOnSameLine(GetStartOfNode(elements[0], sourceFile, false), GetStartOfNode(namedImports.Parent!.Parent!, sourceFile, false), sourceFile),
                LeadingTriviaOption.None);
        }
    }

    public void InsertAtTopOfFile(SourceFile sourceFile, Node[] insert, bool blankLineBetween)
    {
        if (insert.Length == 0)
        {
            return;
        }

        var pos = GetInsertionPositionAtSourceFileTop(sourceFile);
        var originalPos = pos;
        // A content mapper may synthesize a header. Advance to the first writable segment so the insertion
        // maps exactly to the original file, and use its original position when deciding leading trivia.
        if (sourceFile.SpanMap() is { } spanMap)
        {
            foreach (var segment in spanMap.Segments())
            {
                if (segment.Kind != SpanMapNs.Kind.Verbatim || segment.VirtualEnd.Value <= pos)
                {
                    continue;
                }

                if (segment.VirtualStart.Value > pos)
                {
                    pos = segment.VirtualStart.Value;
                }

                originalPos = segment.OriginalStart.Value + pos - segment.VirtualStart.Value;
                break;
            }
        }

        var options = new NodeOptions();
        if (originalPos != 0)
        {
            options.Prefix = _newLine;
        }

        if (sourceFile.Text().Length == 0 || !IsLineBreak(sourceFile.Text()[pos]))
        {
            options.Suffix = _newLine;
        }

        if (blankLineBetween)
        {
            options.Suffix += _newLine;
        }

        if (insert.Length == 1)
        {
            InsertNodeAt(sourceFile, new TextPos(pos), insert[0], options);
        }
        else
        {
            InsertNodesAt(sourceFile, new TextPos(pos), insert, options);
        }
    }

    public void InsertMemberAtStart(SourceFile sourceFile, Node node, Node newElement)
    {
        InsertNodeAtStartWorker(sourceFile, node, newElement);
    }

    internal void InsertNodeAtStartWorker(SourceFile sourceFile, Node node, Node newElement)
    {
        var indentation = TryComputeIndentationFromExistingMembers(sourceFile, node);
        if (indentation < 0)
        {
            indentation = TryComputeIndentationForNewMember(sourceFile, node);
        }

        var members = GetMembersOrProperties(node);
        if (members == null)
        {
            return;
        }

        InsertNodeAt(sourceFile, new TextPos(members.Pos()), newElement, GetInsertNodeAtStartInsertOptions(sourceFile, node, indentation));
    }

    internal int TryComputeIndentationForNewMember(SourceFile sourceFile, Node node)
    {
        var nodeStart = GetStartOfNode(node, sourceFile, false);
        var lineStart = GetLineStartPositionForPosition(nodeStart, sourceFile);

        var tabSize = _formatSettings.TabSize;
        if (tabSize <= 0)
        {
            tabSize = 4;
        }

        var indentSize = _formatSettings.IndentSize;
        if (indentSize <= 0)
        {
            indentSize = 4;
        }

        return Math.Max(FindIndentationColumn(sourceFile.Text(), lineStart, nodeStart, tabSize), 0) + indentSize;
    }

    internal int TryComputeIndentationFromExistingMembers(SourceFile sourceFile, Node node)
    {
        var members = GetMembersOrProperties(node);
        if (members == null)
        {
            return -1;
        }

        var indentation = -1;
        var text = sourceFile.Text();
        var tabSize = _formatSettings.TabSize;
        var last = node;

        if (tabSize <= 0)
        {
            tabSize = 4;
        }

        foreach (var member in members.Nodes ?? [])
        {
            if (member == null)
            {
                continue;
            }

            if (RangeStartPositionsAreOnSameLine(last.Loc, member.Loc, sourceFile))
            {
                return -1;
            }

            var memberStart = GetStartOfNode(member, sourceFile, false);
            var lineStart = GetLineStartPositionForPosition(memberStart, sourceFile);
            var column = FindIndentationColumn(text, lineStart, memberStart, tabSize);
            if (column < 0)
            {
                return -1;
            }

            if (indentation >= 0)
            {
                if (indentation != column)
                {
                    return -1;
                }

                last = member;
                continue;
            }

            indentation = column;
            last = member;
        }

        return indentation;
    }

    internal NodeOptions GetInsertNodeAfterOptions(SourceFile sourceFile, Node node)
    {
        var newLineChar = _newLine;
        NodeOptions options;
        switch (node.Kind)
        {
            case Kind.Parameter:
                // default opts
                options = new NodeOptions();
                break;
            case Kind.ClassDeclaration or Kind.ModuleDeclaration:
                options = new NodeOptions { Prefix = newLineChar, Suffix = newLineChar };
                break;

            case Kind.VariableDeclaration or Kind.StringLiteral or Kind.Identifier:
                options = new NodeOptions { Prefix = ", " };
                break;

            case Kind.PropertyAssignment:
                options = new NodeOptions { Suffix = "," + newLineChar };
                break;

            case Kind.ExportKeyword:
                options = new NodeOptions { Prefix = " " };
                break;

            default:
                if (!(IsStatement(node) || IsClassOrTypeElement(node)))
                {
                    // Else we haven't handled this kind of node yet -- add it
                    Debug.Fail("unimplemented node type " + node.Kind.String() + " in changeTracker.getInsertNodeAfterOptions");
                    throw new System.Diagnostics.UnreachableException();
                }

                options = new NodeOptions { Suffix = newLineChar };
                break;
        }

        if (node.End() == sourceFile.End() && IsStatement(node))
        {
            options.Prefix = _newLine + options.Prefix;
        }

        return options;
    }

    internal NodeOptions GetOptionsForInsertNodeBefore(Node before, Node inserted, bool blankLineBetween)
    {
        if (IsStatement(before) || IsClassOrTypeElement(before))
        {
            if (blankLineBetween)
            {
                return new NodeOptions { Suffix = _newLine + _newLine };
            }

            return new NodeOptions { Suffix = _newLine };
        }
        else if (before.Kind == Kind.VariableDeclaration)
        {
            // insert `x = 1, ` into `const x = 1, y = 2;
            return new NodeOptions { Suffix = ", " };
        }
        else if (before.Kind == Kind.Parameter)
        {
            if (inserted.Kind == Kind.Parameter)
            {
                return new NodeOptions { Suffix = ", " };
            }

            return new NodeOptions();
        }
        else if ((before.Kind == Kind.StringLiteral && before.Parent != null && before.Parent.Kind == Kind.ImportDeclaration) || before.Kind == Kind.NamedImports)
        {
            return new NodeOptions { Suffix = ", " };
        }
        else if (before.Kind == Kind.ImportSpecifier)
        {
            var suffix = ",";
            if (blankLineBetween)
            {
                suffix += _newLine;
            }
            else
            {
                suffix += " ";
            }

            return new NodeOptions { Suffix = suffix };
        }

        // We haven't handled this kind of node yet -- add it
        Debug.Fail("unimplemented node type " + before.Kind.String() + " in changeTracker.getOptionsForInsertNodeBefore");
        throw new System.Diagnostics.UnreachableException();
    }

    internal NodeOptions GetInsertNodeAtStartInsertOptions(SourceFile sourceFile, Node node, int indentation)
    {
        var state = _nodesWithInsertionsAtStart.GetValueOrDefault(node);
        var hasPreviousInsertion = state != null;
        if (state == null)
        {
            state = new NodesInsertedAtStartState(node, sourceFile);
            _nodesWithInsertionsAtStart[node] = state;
        }

        var members = GetMembersOrProperties(node);
        var isObjectLiteral = IsObjectLiteralExpression(node);
        var isJSON = IsJsonSourceFile(sourceFile);

        var hasMembers = members != null && (members.Nodes ?? []).Length > 0;

        var insertTrailingComma = isObjectLiteral && (hasMembers || !isJSON);
        var insertLeadingComma = isObjectLiteral && isJSON && !hasMembers && hasPreviousInsertion;

        var suffix = "";
        if (insertTrailingComma)
        {
            suffix = ",";
        }
        else if (IsInterfaceDeclaration(node) && !hasMembers)
        {
            suffix = ";";
        }

        var prefix = _newLine;
        if (insertLeadingComma)
        {
            prefix = "," + prefix;
        }

        return new NodeOptions { _indentation = indentation, Prefix = prefix, Suffix = suffix };
    }

    internal void FinishNodesWithInsertionsAtStart()
    {
        foreach (var state in _nodesWithInsertionsAtStart.Values)
        {
            if (state == null)
            {
                continue;
            }

            var openBrace = FindChildOfKind(state.Node, Kind.OpenBraceToken, state.SourceFile);
            if (openBrace == null)
            {
                continue;
            }

            var closeBrace = FindChildOfKind(state.Node, Kind.CloseBraceToken, state.SourceFile);
            if (closeBrace == null)
            {
                continue;
            }

            var members = GetMembersOrProperties(state.Node);
            var isEmpty = members == null || (members.Nodes ?? []).Length == 0;
            var isSingleLine = LsChange.PositionsAreOnSameLine(openBrace.End(), closeBrace.End(), state.SourceFile);

            if (isEmpty && isSingleLine && openBrace.End() != closeBrace.End() - 1)
            {
                DeleteRange(state.SourceFile, NewTextRange(openBrace.End(), closeBrace.End() - 1));
            }

            if (isSingleLine)
            {
                InsertTextAt(state.SourceFile, new TextPos(closeBrace.End() - 1), _newLine);
            }
        }
    }

    // ============= tracker.go 里的包级函数（静态类 LsChange 的成员，见文件头第 5 条）=============

    internal static NodeList? GetMembersOrProperties(Node node)
    {
        if (IsObjectLiteralExpression(node))
        {
            return node.PropertyList();
        }

        return node.MemberList();
    }

    internal static bool RangeContainsRangeExclusive(Node outer, Node inner)
    {
        return outer.Pos() < inner.Pos() && inner.End() < outer.End();
    }

    internal static bool IsSeparator(Node node, Node? candidate)
    {
        return candidate != null && node.Parent != null && (candidate.Kind == Kind.CommaToken || (candidate.Kind == Kind.SemicolonToken && node.Parent.Kind == Kind.ObjectLiteralExpression));
    }

    internal static int FindIndentationColumn(string text, int lineStart, int memberStart, int tabSize)
    {
        var column = 0;

        for (var i = lineStart; i < memberStart && i < text.Length; i++)
        {
            var ch = text[i];

            if (IsLineBreak(ch))
            {
                return -1;
            }

            if (IsWhiteSpaceSingleLine(ch))
            {
                column = AdvanceIndentationColumn(column, ch, tabSize);
                continue;
            }

            return column;
        }

        return column;
    }

    internal static int AdvanceIndentationColumn(int column, char ch, int tabSize)
    {
        if (ch == '\t')
        {
            return column + tabSize - (column % tabSize);
        }

        return column + 1;
    }
}

/// <summary>Go: <c>type deletedNode struct</c>（tracker.go:90-93，未导出）。</summary>
internal readonly struct DeletedNode(SourceFile sourceFile, Node node)
{
    internal readonly SourceFile SourceFile = sourceFile;
    internal readonly Node Node = node;
}
