// ported from: internal/printer/printer.go:1-925（PrinterOptions / PrintHandlers / Printer 状态与 NewPrinter /
//   getLiteralTextOfNode / getTextOfNode / WriteKind 与低层写出 / 换行与缩进 / 行终止符计数 /
//   writeCommentRange / shouldEmit* 谓词 / shouldAllowTrailingComma）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 包级类型与包级函数同处一个 `partial class Printer`：Go 的 package 名与包内类型名相同，C# 里一个类不可能
//    既是实例类又是静态类，所以包级声明落成同一个类的 static 成员（与 Scanner/Parser 的处理一致，
//    docs/architecture.md §2）。
// 2) Go 的 `Printer` 内嵌 `PrintHandlers`（字段提升 → 调用点写 `p.OnBeforeEmitNode(...)`）→ C# 用继承
//    `Printer : PrintHandlers` + `NewPrinter` 里逐字段拷贝：Go 的 `Printer{PrintHandlers: handlers, …}` 也是
//    按值拷贝，语义一致，且调用点与 Go 逐字对应。
// 3) 未导出字段一律 `_camelCase`；`_writer` 声明为 `= null!`（Go 里是 nil 接口，构造后到 `Write`/`Emit`
//    之间不可解引用；调用点因此不必写 `!`，与 Go 的 nil 解引用 panic 语义对应）。
// 4) Go 直接读未导出 map（`p.emitContext.textSource[node]`）→ C# 走 EmitContext 的访问器 `TextSource(node)`
//    （未导出字段在 C# 里是 private，跨 partial 分片只能走访问器）。
// 5) `core.IfElse`/`core.Coalesce` → `IfElse`/`Coalesce`（Core 静态类）。
// 6) `for range count` → `for (var i = 0; i < count; i++)`。
// 7) Go 的多值 `case A, B:` → C# 的 `case Kind.A or Kind.B:`；Go 的 `default` 在 switch 里位置随意，C# 里
//    统一放在最后（有 `default` 提前 return 的地方在注释里标出 Go 的原顺序）。
// 8) `utf8.DecodeRuneInString` 的循环只用于找行终止符（行终止符全在 BMP）→ 逐 UTF-16 代码单元判断；等价于
//    Go 逐码点判断（Go 的 `size == 0` 分支在 `scan < end` 时不可达）。`strings.TrimSpace` → `AsSpan(..).Trim()`。
// 9) Go 的 `*commentState`/`*sourceMapState`（arena 指针，nil = "没有"）→ 可空结构体 `CommentState?`/
//    `SourceMapState?`（零堆分配）；arena 槽位仍按 Go "写一次、之后只读"的用法。
// 10) `fmt.Sprintf` 只出现在 panic 消息里 → 内插字符串；打在无 stringer 的整型上时用 `(int)` 与 `%v` 对应。
using System;
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Printer;

/// <summary>Go: <c>type PrinterOptions struct</c>。</summary>
public sealed class PrinterOptions
{
    public bool RemoveComments;
    public NewLineKind NewLine;
    public bool OmitTrailingSemicolon;
    public bool NoEmitHelpers;
    // Go: Module core.ModuleKind（未启用）
    // Go: ModuleResolution core.ModuleResolutionKind（未启用）
    public ScriptTarget Target;
    public bool SourceMap;
    public bool InlineSourceMap;
    public bool InlineSources;
    public bool OmitBraceSourceMapPositions;
    // Go: ExtendedDiagnostics bool（未启用）
    public bool OnlyPrintJSDocStyle;
    public bool NeverAsciiEscape;
    // Go: StripInternal bool（未启用）
    public bool PreserveSourceNewlines;
    public bool TerminateUnterminatedLiterals; // !!!
}

/// <summary>
/// Go: <c>MapSourcePosition func(source sourcemap.Source, pos int) (mappedSource sourcemap.Source, mappedPos int, ok bool)</c>。
/// Go 的三返回值在 C# 里用 out 参数表达（C# 的 Func 不能带 out）。
/// </summary>
public delegate bool MapSourcePositionFunc(SourceMap.Source source, int pos, out SourceMap.Source mappedSource, out int mappedPos);

/// <summary>Go: <c>type PrintHandlers struct</c>（未启用的钩子照 Go 注释保留）。</summary>
public class PrintHandlers
{
    /// <summary>
    /// A hook used by the Printer when generating unique names to avoid collisions with
    /// globally defined names that exist outside of the current source file.
    /// </summary>
    public Func<string, bool>? HasGlobalName;

    /// <summary>
    /// MapSourcePosition composes source-map positions before they reach the generator.
    /// Returning ok=false emits a generated-only mapping.
    /// </summary>
    public MapSourcePositionFunc? MapSourcePosition;

    public Action<Node>? OnBeforeEmitNode;
    public Action<Node>? OnAfterEmitNode;
    public Action<NodeList?>? OnBeforeEmitNodeList;
    public Action<NodeList?>? OnAfterEmitNodeList;
    public Action<Node>? OnBeforeEmitToken;
    public Action<Node>? OnAfterEmitToken;
}

/// <summary>Go: <c>type WriteKind int</c>（包级类型；成员名去掉 <c>WriteKind</c> 前缀，数值与 Go 一致）。</summary>
public enum WriteKind
{
    None = 0,
    Keyword = 1,
    Operator = 2,
    Punctuation = 3,
    StringLiteral = 4,
    Parameter = 5,
    Property = 6,
    Comment = 7,
    Literal = 8,
}

/// <summary>Go: <c>type Printer struct</c>（内嵌的 PrintHandlers 见文件头第 2 条）。</summary>
public sealed partial class Printer : PrintHandlers
{
    public PrinterOptions Options = new(); // Go 的零值 struct；NewPrinter 会覆盖它。
    public Dictionary<Node, Symbol>? IdToSymbol;
    internal EmitContext _emitContext = null!;
    private SourceFile? _currentSourceFile;
    private Dictionary<string, Identifier>? _uniqueHelperNames; // Go: map[string]*ast.IdentifierNode
    private Node? _externalHelpersModuleName;                    // Go: *ast.IdentifierNode（peer 的 GetExternalHelpersModuleName 返回 Node?）
    private int _nextListElementPos;
    internal EmitTextWriter _writer = null!;
    private EmitTextWriter? _ownWriter;
    private WriteKind _writeKind;
    private bool _sourceMapsDisabled;
    private SourceMap.Generator? _sourceMapGenerator;
    private SourceMap.Source? _sourceMapSource;
    private SourceMap.SourceIndex _sourceMapSourceIndex;
    private bool _sourceMapSourceIsJson;
    private LineCharacterCache? _sourceMapLineCharCache;
    private SourceMap.Source? _mostRecentSourceMapSource;
    private SourceMap.SourceIndex _mostRecentSourceMapSourceIndex;
    private int _containerPos;
    private int _containerEnd;
    private int _declarationListContainerEnd;
    private readonly Core.Stack<DetachedCommentsInfo> _detachedCommentsInfo = new();
    private bool _commentsDisabled;
    private bool _inExtends; // whether we are emitting the `extends` clause of a ConditionalTypeNode or InferTypeNode
    private readonly NameGenerator _nameGenerator = new();
    private Func<string, string> _makeFileLevelOptimisticUniqueName = null!;
    private readonly Arena<CommentState> _commentStateArena = new();
    private readonly Arena<SourceMapState> _sourceMapStateArena = new();
}

/// <summary>Go: <c>type detachedCommentsInfo struct</c>。</summary>
internal struct DetachedCommentsInfo
{
    // 字段用 `public`：Go 的未导出字段在 C# 里若是 internal 会被 .editorconfig 的
    // `non_public_fields_should_be_underscore_camel`（IDE1006，warning → TreatWarningsAsErrors 变 error）要求
    // `_camelCase`；这里保留 Go 的读写形态（`info.NodePos`），与仓库既有的 `Module.StaticResolutionKey` 同款。
    public int NodePos;
    public int DetachedCommentEndPos;
}

/// <summary>Go: <c>type commentState struct</c>。</summary>
internal struct CommentState
{
    public EmitFlags EmitFlags;                     // holds the emit flags for the current node
    public TextRange CommentRange;                  // holds the comment range calculated for the current node
    public int ContainerPos;                        // captures the value of containerPos prior to entering an node
    public int ContainerEnd;                        // captures the value of containerEnd prior to entering an node
    public int DeclarationListContainerEnd;         // captures the value of declarationListContainerEnd prior to entering an node
}

/// <summary>Go: <c>type sourceMapState struct</c>。</summary>
internal struct SourceMapState
{
    public EmitFlags EmitFlags;                     // holds the emit flags for the current node
    public TextRange SourceMapRange;                // holds the source map range calculated for the current node
    public bool HasTokenSourceMapRange;             // captures whether the source map range was set for the current node
}

/// <summary>Go: <c>type printerState struct</c>。</summary>
internal struct PrinterState
{
    public CommentState? CommentState;
    public SourceMapState? SourceMapState;
}

public sealed partial class Printer
{
    public static Printer NewPrinter(PrinterOptions options, PrintHandlers handlers, EmitContext? emitContext)
    {
        var printer = new Printer
        {
            Options = options,
            // Go: `if printer.emitContext == nil { printer.emitContext = NewEmitContext() }`
            // （NewEmitContext 才会把 Factory 接到自身；`new EmitContext()` 的 Factory 为 null）
            _emitContext = emitContext ?? NewEmitContext(),
        };
        // Go: `Printer{PrintHandlers: handlers, …}` 是按值拷贝内嵌结构体 → C# 逐字段拷贝（文件头第 2 条）。
        printer.HasGlobalName = handlers.HasGlobalName;
        printer.MapSourcePosition = handlers.MapSourcePosition;
        printer.OnBeforeEmitNode = handlers.OnBeforeEmitNode;
        printer.OnAfterEmitNode = handlers.OnAfterEmitNode;
        printer.OnBeforeEmitNodeList = handlers.OnBeforeEmitNodeList;
        printer.OnAfterEmitNodeList = handlers.OnAfterEmitNodeList;
        printer.OnBeforeEmitToken = handlers.OnBeforeEmitToken;
        printer.OnAfterEmitToken = handlers.OnAfterEmitToken;

        // wire up name generator
        printer._nameGenerator.Context = printer._emitContext;
        printer._nameGenerator.GetTextOfNode = node => printer.GetTextOfNode(node, false);
        printer._nameGenerator.IsFileLevelUniqueNameInCurrentFile = printer.IsFileLevelUniqueNameInCurrentFile;
        printer._makeFileLevelOptimisticUniqueName = name => printer._nameGenerator.MakeFileLevelOptimisticUniqueName(name);
        printer._containerPos = -1;
        printer._containerEnd = -1;
        printer._declarationListContainerEnd = -1;
        printer._commentsDisabled = options.RemoveComments;
        return printer;
    }

    private string GetLiteralTextOfNode(Node node, SourceFile? sourceFile, GetLiteralTextFlags flags)
    {
        if (IsStringLiteral(node))
        {
            var textSourceNode = _emitContext.TextSource(node.AsStringLiteral()!);
            if (textSourceNode is not null)
            {
                string text;
                switch (textSourceNode.Kind)
                {
                    case Kind.NumericLiteral:
                        text = textSourceNode.Text();
                        break;
                    case Kind.Identifier or Kind.PrivateIdentifier or Kind.JsxNamespacedName:
                        text = GetTextOfNode(textSourceNode, false);
                        break;
                    // Go 的 `default:` 在这个 switch 的最前面（分支顺序不影响语义，C# 里 default 放在最后）。
                    default:
                        return GetLiteralTextOfNode(textSourceNode, GetSourceFileOfNode(textSourceNode), flags);
                }

                if ((flags & GetLiteralTextFlags.JsxAttributeEscape) != 0)
                {
                    return "\"" + EscapeJsxAttributeString(text, QuoteChar.DoubleQuote) + "\"";
                }
                else if ((flags & GetLiteralTextFlags.NeverAsciiEscape) != 0 || (_emitContext.EmitFlags(node) & EmitFlags.NoAsciiEscaping) != 0)
                {
                    return "\"" + EscapeString(text, QuoteChar.DoubleQuote) + "\"";
                }
                else
                {
                    return "\"" + EscapeNonAsciiString(text, QuoteChar.DoubleQuote) + "\"";
                }
            }
        }

        // !!! Printer option to control whether to terminate unterminated literals
        if ((_emitContext.EmitFlags(node) & EmitFlags.NoAsciiEscaping) != 0)
        {
            flags |= GetLiteralTextFlags.NeverAsciiEscape;
        }
        if (Options.Target >= ScriptTarget.ES2021)
        {
            flags |= GetLiteralTextFlags.AllowNumericSeparator;
        }
        return GetLiteralText(node, Coalesce(sourceFile, _currentSourceFile), flags);
    }

    /// <summary>
    /// `node` must be one of Identifier | PrivateIdentifier | LiteralExpression | JsxNamespacedName
    /// </summary>
    internal string GetTextOfNode(Node node, bool includeTrivia)
    {
        if (IsMemberName(node) && _emitContext.GetAutoGenerateInfo(node) is not null)
        {
            return _nameGenerator.GenerateName(node);
        }

        if (IsStringLiteral(node))
        {
            var textSourceNode = _emitContext.TextSource(node.AsStringLiteral()!);
            if (textSourceNode is not null)
            {
                return GetTextOfNode(textSourceNode, includeTrivia);
            }
        }

        var canUseSourceFile = _currentSourceFile is not null && node.Parent is not null && !NodeIsSynthesized(node);

        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.JsxNamespacedName:
                if (!canUseSourceFile || GetSourceFileOfNode(node) != _emitContext.MostOriginal(_currentSourceFile!.AsNode())?.AsSourceFile())
                {
                    return node.Text();
                }
                break;
            case Kind.StringLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateHead:
            case Kind.TemplateMiddle:
            case Kind.TemplateTail:
                return GetLiteralTextOfNode(node, null /*sourceFile*/, GetLiteralTextFlags.None);
            default:
                Debug.Fail($"unexpected node: {node.Kind.String()}");
                break;
        }

        // 到这里 canUseSourceFile 必为 true（其余分支都已 return），故 currentSourceFile 非空。
        Debug.Assert(canUseSourceFile);
        return GetSourceTextOfNodeFromSourceFile(_currentSourceFile!, node, includeTrivia);
    }

    //
    // Low-level writing
    //

    internal void WriteAs(string text, WriteKind writeKind)
    {
        switch (writeKind)
        {
            case WriteKind.None:
                _writer.Write(text);
                break;
            case WriteKind.Parameter:
                WriteParameter(text);
                break;
            case WriteKind.Keyword:
                WriteKeyword(text);
                break;
            case WriteKind.Operator:
                WriteOperator(text);
                break;
            case WriteKind.Property:
                WriteProperty(text);
                break;
            case WriteKind.Punctuation:
                WritePunctuation(text);
                break;
            case WriteKind.StringLiteral:
                _writer.WriteStringLiteral(text);
                break;
            case WriteKind.Comment:
                WriteComment(text);
                break;
            case WriteKind.Literal:
                WriteLiteral(text);
                break;
            default:
                Debug.Fail($"unexpected printer.WriteKind: {(int)writeKind}");
                break;
        }
    }

    internal void Write(string text)
    {
        WriteAs(text, _writeKind);
    }

    internal WriteKind SetWriteKind(WriteKind kind)
    {
        var previous = _writeKind;
        _writeKind = kind;
        return previous;
    }

    internal void WriteSymbol(string text, Symbol? optSymbol)
    {
        if (optSymbol is null)
        {
            Write(text);
        }
        else
        {
            _writer.WriteSymbol(text, optSymbol);
        }
    }

    internal void WriteLiteral(string text)
    {
        _writer.WriteLiteral(text);
    }

    internal void WritePunctuation(string text)
    {
        _writer.WritePunctuation(text);
    }

    internal void WriteOperator(string text)
    {
        _writer.WriteOperator(text);
    }

    internal void WriteKeyword(string text)
    {
        _writer.WriteKeyword(text);
    }

    internal void WriteProperty(string text)
    {
        _writer.WriteProperty(text);
    }

    internal void WriteParameter(string text)
    {
        _writer.WriteParameter(text);
    }

    internal void WriteComment(string text)
    {
        _writer.WriteComment(text);
    }

    internal void WriteSpace()
    {
        _writer.WriteSpace(" ");
    }

    internal void WriteLine()
    {
        _writer.WriteLine();
    }

    internal void WriteLineRepeat(int count)
    {
        for (var i = 0; i < count; i++)
        {
            WriteLine();
        }
    }

    internal void WriteLines(string text)
    {
        var lines = SplitLines(text);
        var indentation = GuessIndentation(lines);
        foreach (var line in lines)
        {
            // Go: line = line[indentation:] —— 结果立刻作为 string 交给 writer 接口（writer 只收 string），
            // 所以这里无法保留 span（docs §3.8 的例外，文本一出即消费）。
            var trimmed = indentation > 0 ? line[indentation..] : line;
            if (trimmed.Length > 0)
            {
                WriteLine();
                Write(trimmed);
            }
        }
    }

    internal void WriteTrailingSemicolon()
    {
        _writer.WriteTrailingSemicolon(";");
    }

    internal void IncreaseIndent()
    {
        _writer.IncreaseIndent();
    }

    internal void DecreaseIndent()
    {
        _writer.DecreaseIndent();
    }

    internal void IncreaseIndentIf(bool indentRequested)
    {
        if (indentRequested)
        {
            IncreaseIndent();
        }
    }

    internal void DecreaseIndentIf(bool indentRequested)
    {
        if (indentRequested)
        {
            DecreaseIndent();
        }
    }

    internal void WriteLineOrSpace(Node parentNode, Node prevChildNode, Node nextChildNode)
    {
        if (ShouldEmitOnSingleLine(parentNode))
        {
            WriteSpace();
        }
        else if (Options.PreserveSourceNewlines)
        {
            var lines = GetLinesBetweenNodes(parentNode, prevChildNode, nextChildNode);
            if (lines > 0)
            {
                WriteLineRepeat(lines);
            }
            else
            {
                WriteSpace();
            }
        }
        else
        {
            WriteLine();
        }
    }

    internal void WriteLinesAndIndent(int lineCount, bool writeSpaceIfNotIndenting)
    {
        if (lineCount > 0)
        {
            IncreaseIndent();
            WriteLineRepeat(lineCount);
        }
        else if (writeSpaceIfNotIndenting)
        {
            WriteSpace();
        }
    }

    internal bool WriteLineSeparatorsAndIndentBefore(Node node, Node parent)
    {
        if (Options.PreserveSourceNewlines)
        {
            var leadingNewlines = GetLeadingLineTerminatorCount(parent, node, ListFormat.None);
            if (leadingNewlines > 0)
            {
                WriteLinesAndIndent(leadingNewlines /*writeSpaceIfNotIndenting*/, false);
                return true;
            }
        }
        return false;
    }

    internal void WriteLineSeparatorsAfter(Node node, Node parent)
    {
        if (Options.PreserveSourceNewlines)
        {
            var trailingNewlines = GetClosingLineTerminatorCount(parent, node, ListFormat.None, NewTextRange(-1, -1) /*childrenTextRange*/);
            if (trailingNewlines > 0)
            {
                WriteLineRepeat(trailingNewlines);
            }
        }
    }

    internal int GetLinesBetweenNodes(Node parent, Node node1, Node node2)
    {
        if (ShouldElideIndentation(parent))
        {
            return 0;
        }

        parent = SkipSynthesizedParentheses(parent);
        node1 = SkipSynthesizedParentheses(node1);
        node2 = SkipSynthesizedParentheses(node2);

        // Always use a newline for synthesized code if the synthesizer desires it.
        if (ShouldEmitOnNewLine(node2, ListFormat.None))
        {
            return 1;
        }

        // 捕获到局部：字段在 lambda 里会被可空分析重置（docs §3.5）。
        var sourceFile = _currentSourceFile;
        if (sourceFile is not null && !NodeIsSynthesized(parent) && !NodeIsSynthesized(node1) && !NodeIsSynthesized(node2))
        {
            if (Options.PreserveSourceNewlines)
            {
                return GetEffectiveLines(
                    includeComments => GetLinesBetweenRangeEndAndRangeStart(
                        node1.Loc,
                        node2.Loc,
                        sourceFile,
                        includeComments));
            }
            return IfElse(RangeEndIsOnSameLineAsRangeStart(node1.Loc, node2.Loc, sourceFile), 0, 1);
        }

        return 0;
    }

    internal int GetEffectiveLines(Func<bool, int> getLineDifference)
    {
        // If 'preserveSourceNewlines' is disabled, we should never call this function
        // because it could be more expensive than alternative approximations.
        if (!Options.PreserveSourceNewlines)
        {
            Debug.Fail("Should not be called when preserveSourceNewlines is false");
        }
        // We start by measuring the line difference from a position to its adjacent comments,
        // so that this is counted as a one-line difference, not two:
        //
        //   node1;
        //   // NODE2 COMMENT
        //   node2;
        var lines = getLineDifference( /*includeComments*/ true);
        if (lines == 0)
        {
            // However, if the line difference considering comments was 0, we might have this:
            //
            //   node1; // NODE2 COMMENT
            //   node2;
            //
            // in which case we should be ignoring node2's comment, so this too is counted as
            // a one-line difference, not zero.
            return getLineDifference( /*includeComments*/ false);
        }
        return lines;
    }

    internal int GetLeadingLineTerminatorCount(Node? parentNode, Node? firstChild, ListFormat format)
    {
        if ((format & ListFormat.PreserveLines) != 0 || Options.PreserveSourceNewlines)
        {
            if ((format & ListFormat.PreferNewLine) != 0)
            {
                return 1;
            }

            if (firstChild is null)
            {
                return IfElse(parentNode is null || _currentSourceFile is not null && RangeIsOnSingleLine(parentNode.Loc, _currentSourceFile), 0, 1);
            }
            if (_nextListElementPos > 0 && firstChild.Pos() == _nextListElementPos)
            {
                // If this child starts at the beginning of a list item in a parent list, its leading
                // line terminators have already been written as the separating line terminators of the
                // parent list. Example:
                //
                // class Foo {
                //   constructor() {}
                //   public foo() {}
                // }
                //
                // The outer list is the list of class members, with one line terminator between the
                // constructor and the method. The constructor is written, the separating line terminator
                // is written, and then we start emitting the method. Its modifiers ([public]) constitute an inner
                // list, so we look for its leading line terminators. If we didn't know that we had already
                // written a newline as part of the parent list, it would appear that we need to write a
                // leading newline to start the modifiers.
                return 0;
            }
            if (firstChild.Kind == Kind.JsxText)
            {
                // JsxText will be written with its leading whitespace, so don't add more manually.
                return 0;
            }
            var sourceFile = _currentSourceFile;
            if (sourceFile is not null && parentNode is not null &&
                !PositionIsSynthesized(parentNode.Pos()) &&
                !NodeIsSynthesized(firstChild) &&
                (firstChild.Parent is null /*|| getOriginalNode(firstChild.Parent) == getOriginalNode(parentNode)*/))
            {
                if (Options.PreserveSourceNewlines)
                {
                    return GetEffectiveLines(
                        includeComments => GetLinesBetweenPositionAndPrecedingNonWhitespaceCharacter(
                            firstChild.Pos(),
                            parentNode.Pos(),
                            sourceFile,
                            includeComments));
                }
                return IfElse(RangeStartPositionsAreOnSameLine(parentNode.Loc, firstChild.Loc, sourceFile), 0, 1);
            }
            if (ShouldEmitOnNewLine(firstChild, format))
            {
                return 1;
            }
        }
        return IfElse((format & ListFormat.MultiLine) != 0, 1, 0);
    }

    internal int GetSeparatingLineTerminatorCount(Node? previousNode, Node? nextNode, ListFormat format)
    {
        if ((format & ListFormat.PreserveLines) != 0 || Options.PreserveSourceNewlines)
        {
            if (previousNode is null || nextNode is null)
            {
                return 0;
            }
            if (nextNode.Kind == Kind.JsxText)
            {
                // JsxText will be written with its leading whitespace, so don't add more manually.
                return 0;
            }
            var sourceFile = _currentSourceFile;
            if (sourceFile is not null && !NodeIsSynthesized(previousNode) && !NodeIsSynthesized(nextNode))
            {
                if (Options.PreserveSourceNewlines && SiblingNodePositionsAreComparable(_emitContext, previousNode, nextNode))
                {
                    return GetEffectiveLines(
                        includeComments => GetLinesBetweenRangeEndAndRangeStart(
                            previousNode.Loc,
                            nextNode.Loc,
                            sourceFile,
                            includeComments));
                }
                else if (!Options.PreserveSourceNewlines && OriginalNodesHaveSameParent(_emitContext, previousNode, nextNode))
                {
                    // If `preserveSourceNewlines` is `false` we do not intend to preserve the effective lines between the
                    // previous and next node. Instead we naively check whether nodes are on separate lines within the
                    // same node parent. If so, we intend to preserve a single line terminator. This is less precise and
                    // expensive than checking with `preserveSourceNewlines` as above, but the goal is not to preserve the
                    // effective source lines between two sibling nodes.
                    return IfElse(RangeEndIsOnSameLineAsRangeStart(previousNode.Loc, nextNode.Loc, sourceFile), 0, 1);
                }
                // If the two nodes are not comparable, add a line terminator based on the format that can indicate
                // whether new lines are preferred or not.
                return IfElse((format & ListFormat.PreferNewLine) != 0, 1, 0);
            }
            else if (ShouldEmitOnNewLine(previousNode, format) || ShouldEmitOnNewLine(nextNode, format))
            {
                return 1;
            }
        }
        // Go: p.shouldEmitOnNewLine(nextNode, ListFormatNone) —— 与上面第一个分支不同，这个 else-if 里 Go 不做
        // nil 检查、直接解引用（nil 即 panic），C# 用 `!` 保持同一不变量（上层调用者在 null 时不会走到这里）。
        else if (ShouldEmitOnNewLine(nextNode!, ListFormat.None))
        {
            return 1;
        }
        return IfElse((format & ListFormat.MultiLine) != 0, 1, 0);
    }

    internal int GetClosingLineTerminatorCount(Node? parentNode, Node? lastChild, ListFormat format, TextRange childrenTextRange)
    {
        if ((format & ListFormat.PreserveLines) != 0 || Options.PreserveSourceNewlines)
        {
            if ((format & ListFormat.PreferNewLine) != 0)
            {
                return 1;
            }
            if (lastChild is null)
            {
                return IfElse(parentNode is null || _currentSourceFile is not null && RangeIsOnSingleLine(parentNode.Loc, _currentSourceFile), 0, 1);
            }
            var sourceFile = _currentSourceFile;
            if (sourceFile is not null && parentNode is not null && !PositionIsSynthesized(parentNode.Pos()) && !NodeIsSynthesized(lastChild) && (lastChild.Parent is null || lastChild.Parent == parentNode))
            {
                if (Options.PreserveSourceNewlines)
                {
                    var end = GreatestEnd(lastChild.End(), childrenTextRange);
                    return GetEffectiveLines(
                        includeComments => GetLinesBetweenPositionAndNextNonWhitespaceCharacter(
                            end,
                            parentNode.End(),
                            sourceFile,
                            includeComments));
                }
                return IfElse(RangeEndPositionsAreOnSameLine(parentNode.Loc, lastChild.Loc, sourceFile), 0, 1);
            }
            if (ShouldEmitOnNewLine(lastChild, format))
            {
                return 1;
            }
        }
        if ((format & ListFormat.MultiLine) != 0 && (format & ListFormat.NoTrailingNewLine) == 0)
        {
            return 1;
        }
        return 0;
    }

    internal void WriteCommentRange(CommentRange comment)
    {
        if (_currentSourceFile is null)
        {
            return;
        }

        var text = _currentSourceFile.Text();
        var lineMap = _currentSourceFile.ECMALineMap();
        WriteCommentRangeWorker(text, lineMap, comment.Kind, comment.Range);
    }

    internal void WriteCommentRangeWorker(string text, TextPos[]? lineMap, Kind kind, TextRange loc)
    {
        if (kind == Kind.MultiLineCommentTrivia)
        {
            var indentSize = GetDefaultIndentSize();
            var firstLine = ComputeLineOfPosition(lineMap!, loc.Pos());
            var lineCount = lineMap!.Length;
            var firstCommentLineIndent = -1;
            var pos = loc.Pos();
            var currentLine = firstLine;
            for (; pos < loc.End(); currentLine++)
            {
                int nextLineStart;
                if (currentLine + 1 == lineCount)
                {
                    nextLineStart = text.Length + 1;
                }
                else
                {
                    nextLineStart = (int)lineMap[currentLine + 1];
                }

                if (pos != loc.Pos())
                {
                    // If we are not emitting first line, we need to write the spaces to adjust the alignment
                    if (firstCommentLineIndent == -1)
                    {
                        firstCommentLineIndent = CalculateIndent(text, (int)lineMap[firstLine], loc.Pos());
                    }

                    // These are number of spaces writer is going to write at current indent
                    var currentWriterIndentSpacing = _writer.GetIndent() * indentSize;

                    // Number of spaces we want to be writing
                    // eg: Assume writer indent
                    // module m {
                    //         /* starts at character 9 this is line 1
                    //    * starts at character pos 4 line                        --1  = 8 - 8 + 3
                    //   More left indented comment */                            --2  = 8 - 8 + 2
                    //     class c { }
                    // }
                    // module m {
                    //     /* this is line 1 -- Assume current writer indent 8
                    //      * line                                                --3 = 8 - 4 + 5
                    //            More right indented comment */                  --4 = 8 - 4 + 11
                    //     class c { }
                    // }
                    var spacesToEmit = currentWriterIndentSpacing - firstCommentLineIndent + CalculateIndent(text, pos, nextLineStart);
                    if (spacesToEmit > 0)
                    {
                        var numberOfSingleSpacesToEmit = spacesToEmit % indentSize;
                        var indentSizeSpaceString = GetIndentString((spacesToEmit - numberOfSingleSpacesToEmit) / indentSize, indentSize);

                        // Write indent size string ( in eg 1: = "", 2: "" , 3: string with 8 spaces 4: string with 12 spaces
                        _writer.RawWrite(indentSizeSpaceString);

                        // Emit the single spaces (in eg: 1: 3 spaces, 2: 2 spaces, 3: 1 space, 4: 3 spaces)
                        while (numberOfSingleSpacesToEmit > 0)
                        {
                            _writer.RawWrite(" ");
                            numberOfSingleSpacesToEmit--;
                        }
                    }
                    else
                    {
                        // No spaces to emit write empty string
                        _writer.RawWrite("");
                    }
                }

                // Write the comment line text
                var end = Math.Min(loc.End(), nextLineStart);
                // Go 在这里用 utf8.DecodeRuneInString 逐个码点找行终止符；行终止符全在 BMP（LF/CR/LS/PS），
                // 逐 UTF-16 代码单元判断与逐码点判断等价（文件头第 8 条）。
                for (var scan = pos; scan < end; scan++)
                {
                    if (IsLineBreak(text[scan]))
                    {
                        end = scan;
                        break;
                    }
                }
                var currentLineText = text.AsSpan(pos, end - pos).Trim();
                if (currentLineText.Length > 0)
                {
                    WriteComment(currentLineText.ToString());
                    if (end != loc.End())
                    {
                        WriteLine();
                    }
                }
                else
                {
                    // Empty string - make sure we write empty line
                    _writer.WriteLineForce(true);
                }

                pos = nextLineStart;
            }
        }
        else
        {
            // Single line comment of style //....
            WriteComment(text[loc.Pos()..loc.End()]);
        }
    }

    //
    // Custom emit behavior stubs (i.e., from `EmitNode`, `EmitFlags`, etc.)
    //

    internal bool ShouldEmitComments(Node node)
    {
        return !_commentsDisabled &&
            _currentSourceFile is not null &&
            !IsSourceFile(node);
    }

    internal bool ShouldWriteComment(CommentRange comment)
    {
        return !Options.OnlyPrintJSDocStyle ||
            _currentSourceFile is not null && IsJSDocLikeText(_currentSourceFile.Text(), comment) ||
            _currentSourceFile is not null && IsPinnedComment(_currentSourceFile.Text(), comment);
    }

    internal bool ShouldEmitIndented(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.Indented) != 0;
    }

    internal bool ShouldElideIndentation(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.NoIndentation) != 0;
    }

    internal bool ShouldEmitOnSingleLine(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.SingleLine) != 0;
    }

    internal bool ShouldEmitOnMultipleLines(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.MultiLine) != 0;
    }

    internal bool ShouldEmitBlockFunctionBodyOnSingleLine(Block body)
    {
        // We must emit a function body as a single-line body in the following case:
        // * The body has NodeEmitFlags.SingleLine specified.

        // We must emit a function body as a multi-line body in the following cases:
        // * The body is explicitly marked as multi-line.
        // * A non-synthesized body's start and end position are on different lines.
        // * Any statement in the body starts on a new line.

        if (ShouldEmitOnSingleLine(body.AsNode()))
        {
            return true;
        }

        if (body.MultiLine)
        {
            return false;
        }

        if (!NodeIsSynthesized(body.AsNode()) && _currentSourceFile is not null && !RangeIsOnSingleLine(body.Loc, _currentSourceFile))
        {
            return false;
        }

        var statements = body.Statements!; // Go 直接解引用（nil 即 panic）；C# 侧同。
        if (GetLeadingLineTerminatorCount(body.AsNode(), FirstOrNil(statements.Nodes), ListFormat.PreserveLines) > 0 ||
            GetClosingLineTerminatorCount(body.AsNode(), LastOrNil(statements.Nodes), ListFormat.PreserveLines, statements.Loc) > 0)
        {
            return false;
        }

        Node? previousStatement = null;
        // Go: `body.Statements.Nodes` 是切片，nil 时 `range nil` 合法（parser 为无 body 的 accessor 合成空 Block →
        // NodeList.Nodes == null）。C# 的 `!` 只是编译期注解，foreach null 会抛 NRE。
        foreach (var statement in statements.Nodes ?? Array.Empty<Node>())
        {
            if (GetSeparatingLineTerminatorCount(previousStatement, statement, ListFormat.PreserveLines) > 0)
            {
                return false;
            }

            previousStatement = statement;
        }

        return true;
    }

    internal bool ShouldEmitOnNewLine(Node node, ListFormat format)
    {
        if ((_emitContext.EmitFlags(node) & EmitFlags.StartOnNewLine) != 0)
        {
            return true;
        }
        return (format & ListFormat.PreferNewLine) != 0;
    }

    internal bool ShouldEmitSourceMaps(Node node)
    {
        return !_sourceMapsDisabled &&
            _sourceMapSource is not null &&
            !IsSourceFile(node) &&
            !IsInJsonFile(node);
    }

    internal bool ShouldEmitTokenSourceMaps(Kind token, int pos, Node contextNode, TokenEmitFlags flags)
    {
        // We don't emit source positions for most tokens as it tends to be quite noisy, however
        // we need to emit source positions for open and close braces so that tools like istanbul
        // can map branches for code coverage. However, we still omit brace source positions when
        // the output is a declaration file.
        return (flags & TokenEmitFlags.NoSourceMaps) == 0 &&
            ShouldEmitSourceMaps(contextNode) &&
            !Options.OmitBraceSourceMapPositions && (token == Kind.OpenBraceToken || token == Kind.CloseBraceToken);
    }

    internal bool ShouldEmitLeadingComments(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.NoLeadingComments) == 0;
    }

    internal bool ShouldEmitTrailingComments(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.NoTrailingComments) == 0;
    }

    internal bool ShouldEmitNestedComments(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.NoNestedComments) == 0;
    }

    internal bool ShouldEmitDetachedComments(Node node)
    {
        if (!IsSourceFile(node))
        {
            return true;
        }

        var file = node.AsSourceFile()!;

        // Emit detached comment if there are no prologue directives or if the first node is synthesized.
        // The synthesized node will have no leading comment so some comments may be missed.
        // Go: len(file.Statements.Nodes) == 0 || … —— nil 切片与空切片同义，C# 侧显式判空（docs §3.2）。
        var nodes = file.Statements!.Nodes;
        return nodes is null ||
            nodes.Length == 0 ||
            !IsPrologueDirective(nodes[0]) ||
            NodeIsSynthesized(nodes[0]);
    }

    internal bool HasCommentsAtPosition(int pos)
    {
        if (_currentSourceFile is null)
        {
            return false;
        }

        foreach (var _ in GetTrailingCommentRanges(_emitContext.Factory.AsNodeFactory(), _currentSourceFile.Text(), pos + 1))
        {
            return true;
        }
        foreach (var _ in GetLeadingCommentRanges(_emitContext.Factory.AsNodeFactory(), _currentSourceFile.Text(), pos + 1))
        {
            return true;
        }
        return false;
    }

    internal bool ShouldEmitIndirectCall(Node node)
    {
        return (_emitContext.EmitFlags(node) & EmitFlags.IndirectCall) != 0;
    }

    internal bool ShouldAllowTrailingComma(Node node, NodeList? list)
    {
        if (_currentSourceFile is null || _currentSourceFile.ScriptKind == ScriptKind.JSON)
        {
            return false;
        }

        switch (node.Kind)
        {
            case Kind.ObjectLiteralExpression:
                return true;
            case Kind.ArrayLiteralExpression:
            case Kind.ArrowFunction:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.FunctionType:
            case Kind.ConstructorType:
            case Kind.CallSignature:
            case Kind.ConstructSignature:
            case Kind.TaggedTemplateExpression:
            case Kind.ObjectBindingPattern:
            case Kind.ArrayBindingPattern:
            case Kind.NamedImports:
            case Kind.NamedExports:
            case Kind.ImportAttributes:
                return true;
            case Kind.ClassExpression:
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
                return list == node.TypeParameterList();
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
            case Kind.MethodDeclaration:
                return true;
            case Kind.CallExpression:
                return true;
            case Kind.NewExpression:
                return true;
        }

        return false;
    }
}
