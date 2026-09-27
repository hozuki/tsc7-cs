// ported from: internal/parser/parser.go:1-1062（ParsingContext/ParsingContexts/JSDocInfo/jsdocScannerInfo 类型与常量、
//               Parser 结构体（本文件独占字段声明）、ParserState、parserPool（Go 的 sync.Pool）、
//               ParseSourceFile/ParseIsolatedEntityName/parseJSONText（含 validateJson*）、parseError* 系列、
//               mark/rewind/lookAhead/nextToken*/nodePos/hasPrecedingLineBreak/jsdocScannerInfo、
//               parseSourceFileWorker/finishSourceFile/createJSDocCache/parseToplevelStatement/reparseTopLevelAwait、
//               列表基础设施（parseList*/parseDelimitedList/parseBracketedList/parseEmptyNodeList/createMissingList/
//               abortParsingListOrMoveToNextToken/isInSomeParsingContext/parsingContextErrors/isListElement/isListTerminator）、
//               parseExpected*/parseOptional*/parseTokenNode 系列
//               —— 到 parser.go:1062 为止；下一行 1063 是 parseStatement，属 W2 的 Parser.Statements.cs）
//
// 与 Go 的偏离（逐条说明；其余地方与 Go 逐行对应）：
//
// 1) **`parserPool`（Go 的 `sync.Pool`）→ `[ThreadStatic]` 单槽缓存**（GetParser/PutParser 语义保留）：
//    - 理由：AOT 安全（无反射、无泛型实例化技巧；`System.Buffers.ArrayPool`/`ObjectPool` 要么不适用要么需要额外
//      依赖与反射式工厂），加锁自由（`[ThreadStatic]` 是 CLR 原生 per-thread 槽位，无需 `ConcurrentBag` 的锁/拷贝），
//      容量有界（每线程最多缓存 1 个 Parser，而 `ConcurrentBag` 会随并发无界增长），与 sync.Pool 的分配次数同阶。
//    - 可见行为差异（都不影响语义）：(a) sync.Pool 是**进程级**池，线程 A 归还的 Parser 可被线程 B 取用；本实现只在
//      本线程复用，跨线程不复用（线程数 × 1 个 Parser 的额外常驻内存）。(b) sync.Pool 会在 GC 时清空池；本实现把
//      对象留到线程退役为止 —— 只会更省分配、不会无界增长。(c) Go 的 `putParser` 用结构体字面量 `*p = Parser{...}`
//      重置全部字段，C# 用 `Reset()` 逐字段重置（见该方法的注释：集合用 `Clear()` 复用容量，Go 是丢弃切片）。
//    - **重入安全**：与 Go 一致 —— 解析期间再次 GetParser 会拿到新实例（槽位已被取空），不会复用同一个 Parser。
//
// 2) **`missingListNodes` 哨兵**：Go 用"长度 0、**容量 1** 的切片"当哨兵，靠 `cap(list.Nodes) == 1 && &list.Nodes[:1][0]
//    == &missingListNodes[:1][0]` 区分"缺失的列表"与"普通空列表"。C# 数组的 Length 就是容量，空数组 Length 恒为 0，
//    没法用容量编码，所以改用**引用同一性**：`new Node[0]`（**不能**写 `[]`，那会拿到 `Array.Empty<Node>()` 单例）即哨兵，
//    `IsMissingNodeList` 只比较数组引用。`CreateMissingList()` 显式把 `Nodes` 指向哨兵 —— 与 Go 的
//    `result.Nodes = missingListNodes` 逐字对应。
//
// 3) **切片 → `List<T>` / `T[]`**（docs/architecture.md §3.2/§3.8）：
//    - Go 里"append + 下标截断（`s = s[:n]`）"的字段（diagnostics/jsDiagnostics/jsdocDiagnostics/jsdocInfos/
//      possibleAwaitSpans/reparseList/…）落地为 `List<T>`：`append` → `Add`/`AddRange`，`s = s[0:n]` → `RemoveRange`。
//      `_diagnostics` 在 ReparseTopLevelAwait 里按 Go 换成**新** List（Go 是 `p.diagnostics = []*ast.Diagnostic{}`）。
//    - `_reparseList` 是**共享的长生命周期 List**（Go 的 `p.reparseList` 是切片头，`p.reparseList = nil` 之后又 append）：
//      本文件用"进入列表时的起始下标"复刻 Go 的 `outerReparseList`/`p.reparseList = nil` 配对语义，
//      因此**任何分片都不得替换 `_reparseList` 对象**（只 `Add`，清空用 `Clear()`）。
//    - 热路径的临时集合用 `List<T>` + `CollectionsMarshal.AsSpan`（克隆/追加时不复制数组）。
//
// 4) `ast.`/`scanner.`/`diagnostics.`/`tspath.` 的包级函数按 Go 的裸名调用（`using static ...`）：本文件在
//    `TypeScript.Parser` 下，写限定名 `Ast.NewDiagnostic(...)` 会先解析到同级的**命名空间** `TypeScript.Ast`
//    （C# 只做命名空间成员查找、不落到静态类成员，实测 CS0234），仓库既有做法见 Scanner/Utilities.cs 文件头第 9 条。
//    `Debug`（根命名空间 `TypeScript` 的静态类）与 `UnreachableException` 全名写 `System.Diagnostics.UnreachableException`
//    （避免为本文件引入 `using System.Diagnostics;` 而让裸名 `Debug` 变得依赖优先级规则）。
//
// 5) `*ast.Expression`/`*ast.Statement`/`*ast.EntityName` 之类都是 `type X = Node` 别名 → C# 直接用 `Node`（docs/ast.md §2）；
//    参数/字段可能为 nil 的写 `Node?`。`ParserState` 保持 **struct**（值语义 = Go：`mark()` 的副本被 `rewind` 读，且
//    ReparseTopLevelAwait 会改自己那份 `diagnosticsLen`）。
//
// 6) Go 的 `panic` → `Debug.Fail(...)`（抛 DebugFailureException）；它不是 `[DoesNotReturn]`，所以需要"必须返回值"的地方
//    补 `throw new System.Diagnostics.UnreachableException()`（与 Parser.Statements.cs / Core/CompilerOptions.cs 一致）。
//
// 7) `[ThreadStatic]` 池里的 `Reset()` 与 Go 的 `*p = Parser{...}` 一一对应，只有两点不同（均在注释里标注）：
//    (a) 集合字段用 `Clear()` 而不是抛弃 —— 保留容量、少分配；Go 清零后丢弃底层数组（只影响常驻内存上限）。
//    (b) `_nodeSliceArena`/`_stringSliceArena` 保留同一个 Arena 对象 —— C# 的 `Arena.Clone/NewSlice` 退化成普通分配
//        （docs/architecture.md §3.2），parser 从不调用 `Arena.New()`，所以保留对象与 Go 的清零等价。
//
// 8) **Go 的 `initializeClosures()`（parser.go:148-153）折进私有构造器**：C# 的"非空字段必须在构造器退出前赋值"
//    只认构造器体内的赋值，而该方法在整个 package 里只有 `newParser` 一个调用者，所以闭包直接写在 `Parser()` 里，
//    不再单独保留这个方法（本文件唯一未成对出现的 Go 声明）。
//    包级 var `viableKeywordSuggestions`/`missingListNodes` → 同一个 partial 类里的 `private static readonly` 字段
//    （Go 的包级 var 在 C# 没有"自由变量"可放，§2：一个 Go package = 一个 C# 类型）；`parserPool` 见第 1 条。
// 9) `ParsingContexts`（Go `type ParsingContexts int`）→ `readonly struct` + 双向隐式 int 转换 + `|`/`&`/`~`/`==`/`!=`：
//    若用 enum 表达，5 个分片里每一处位运算都得显式转型（Go 到处是 `p.parsingContexts |= 1 << kind`）；包装类型让
//    `|= 1 << (int)kind`、`(ParsingContexts)(1 << (int)kind)`、`!= 0`、`== ParsingContexts.None` 都能逐字编译
//    （仓库先例：Core/Text.cs 的 `TextPos`）。`ParsingContexts.None` == Go 的零值。
// 10) 两处"方法名与返回类型同名"按 docs/ast.md 的 Color-Color 先例落成**属性**：Go 的 `p.jsdocScannerInfo()` →
//    `JsdocScannerInfo` 属性（若保持方法形态，类体里写 `JsdocScannerInfo.HasJSDoc` 会因方法遮蔽类型名而 CS0119）。
// 11) JSDoc 的 4 个 scratch 切片：Go 只做 `x = x[:0]` 复用的两个（jsdocCommentsSpace/jsdocCommentRangesSpace）→ 非空
//    `List<T>` + `Clear()`；Go 在 jsdoc.go 里写 `p.x = nil` 防重入的两个（jsdocTagCommentsSpace/jsdocTagCommentsPartsSpace）
//    → 可空 `List<T>?`（嵌套调用拿到 null，外层仍持有自己的 List，与 Go 的 nil 语义一致）。
// 12) `TypeScript.Scanner` 既是命名空间又是类型名：本文件用 `TsScanner`/`TsScannerState` 别名（裸写 `Scanner` 会
//    解析到命名空间 CS0118，别名名也不能叫 `Scanner` —— 见 §2 的 Locale 坑）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;

// `TypeScript.Scanner` 既是命名空间、又是其中的类型名：在 `TypeScript.Parser` 下裸写 `Scanner` 会先解析到**命名空间**
// （CS0118），裸写 `ScannerState` 则根本不可见（没有 `using TypeScript.Scanner;`）。别名名本身也不能叫 `Scanner`
// （外层命名空间成员优先于 using 导入，见 docs/architecture.md §2 的 Locale 坑），所以这里用 `Ts*` 前缀。
using TsScanner = TypeScript.Scanner.Scanner;
using TsScannerState = TypeScript.Scanner.ScannerState;

namespace TypeScript.Parser;

/// <summary>
/// Go: <c>type ParsingContext int</c>（成员名去 Go 的 <c>PC</c> 前缀，数值与 Go 一致，docs/architecture.md §3.3）。
/// </summary>
public enum ParsingContext : int
{
    /// <summary>Elements in source file</summary>
    SourceElements = 0,

    /// <summary>Statements in block</summary>
    BlockStatements = 1,

    /// <summary>Clauses in switch statement</summary>
    SwitchClauses = 2,

    /// <summary>Statements in switch clause</summary>
    SwitchClauseStatements = 3,

    /// <summary>Members in interface or type literal</summary>
    TypeMembers = 4,

    /// <summary>Members in class declaration</summary>
    ClassMembers = 5,

    /// <summary>Members in enum declaration</summary>
    EnumMembers = 6,

    /// <summary>Elements in a heritage clause</summary>
    HeritageClauseElement = 7,

    /// <summary>Variable declarations in variable statement</summary>
    VariableDeclarations = 8,

    /// <summary>Binding elements in object binding list</summary>
    ObjectBindingElements = 9,

    /// <summary>Binding elements in array binding list</summary>
    ArrayBindingElements = 10,

    /// <summary>Expressions in argument list</summary>
    ArgumentExpressions = 11,

    /// <summary>Members in object literal</summary>
    ObjectLiteralMembers = 12,

    /// <summary>Attributes in jsx element</summary>
    JsxAttributes = 13,

    /// <summary>Things between opening and closing JSX tags</summary>
    JsxChildren = 14,

    /// <summary>Members in array literal</summary>
    ArrayLiteralMembers = 15,

    /// <summary>Parameters in parameter list</summary>
    Parameters = 16,

    /// <summary>JSDoc parameters in parameter list of JSDoc function type</summary>
    JSDocParameters = 17,

    /// <summary>Property names in a rest type list</summary>
    RestProperties = 18,

    /// <summary>Type parameters in type parameter list</summary>
    TypeParameters = 19,

    /// <summary>Type arguments in type argument list</summary>
    TypeArguments = 20,

    /// <summary>Element types in tuple element type list</summary>
    TupleElementTypes = 21,

    /// <summary>Heritage clauses for a class or interface declaration.</summary>
    HeritageClauses = 22,

    /// <summary>Named import clause's import specifier list</summary>
    ImportOrExportSpecifiers = 23,

    /// <summary>Import attributes</summary>
    ImportAttributes = 24,

    /// <summary>Parsing via JSDocParser</summary>
    JSDocComment = 25,

    /// <summary>Number of parsing contexts</summary>
    Count = 26,
}

/// <summary>
/// Go: <c>type ParsingContexts int</c>（一组 <see cref="ParsingContext"/> 的位集，未导出成员用的都是按位运算）。
/// </summary>
/// <remarks>
/// Go 里它就是 int，所以 `p.parsingContexts |= 1 &lt;&lt; kind` / `p.parsingContexts &amp; (1 &lt;&lt; kind) != 0` 直接可用。
/// C# 若用 enum 表达，每个调用点都要显式转型（`(ParsingContexts)(1 &lt;&lt; (int)kind)`），5 个分片几百处调用点容易写错；
/// 所以这里按仓库既有惯例（Core/Text.cs 的 <c>TextPos</c>）做"强类型 int"：只读 struct 包一个 int + 双向隐式转换 +
/// `|`/`&amp;`/`~`/`==`/`!=`，于是 Go 的写法（以及显式转型的写法）都能逐字编译。
/// </remarks>
public readonly struct ParsingContexts : IEquatable<ParsingContexts>
{
    private readonly int _value;

    internal ParsingContexts(int value)
    {
        _value = value;
    }

    /// <summary>Go 的零值 <c>ParsingContexts(0)</c>（没有任何解析上下文）。</summary>
    internal static ParsingContexts None => default;

    public static implicit operator ParsingContexts(int value) => new(value);

    public static implicit operator int(ParsingContexts value) => value._value;

    public static ParsingContexts operator |(ParsingContexts left, ParsingContexts right) => new(left._value | right._value);

    public static ParsingContexts operator &(ParsingContexts left, ParsingContexts right) => new(left._value & right._value);

    public static ParsingContexts operator ~(ParsingContexts value) => new(~value._value);

    public static bool operator ==(ParsingContexts left, ParsingContexts right) => left._value == right._value;

    public static bool operator !=(ParsingContexts left, ParsingContexts right) => left._value != right._value;

    public bool Equals(ParsingContexts other) => _value == other._value;

    public override bool Equals(object? obj) => obj is ParsingContexts other && Equals(other);

    public override int GetHashCode() => _value;

    public override string ToString() => _value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Go: <c>type JSDocInfo struct { parent *ast.Node; jsDocs []*ast.Node }</c>。</summary>
public sealed class JSDocInfo
{
    /// <summary>Go: 字段 <c>parent *ast.Node</c>。</summary>
    internal Node? _parent;

    /// <summary>Go: 字段 <c>jsDocs []*ast.Node</c>。</summary>
    internal Node[]? _jsDocs;

    /// <summary>Go 的结构体字面量 <c>JSDocInfo{parent: …, jsDocs: …}</c> → C# 的唯一构造器。</summary>
    internal JSDocInfo(Node? parent, Node[]? jsDocs)
    {
        _parent = parent;
        _jsDocs = jsDocs;
    }
}

/// <summary>
/// Go: <c>type jsdocScannerInfo uint8</c>（成员名去 Go 常量前缀 <c>jsdocScannerInfo</c>；docs/architecture.md §3.3）。
/// </summary>
[Flags]
internal enum JsdocScannerInfo : byte
{
    None = 0,
    HasJSDoc = 1 << 0,
    HasDeprecated = 1 << 1,
    HasSeeOrLink = 1 << 2,
}

/// <summary>
/// Go: <c>type ParserState struct</c>（<see cref="Parser.Mark"/> 的返回值）。保持 struct：Go 里按值传递/复制，
/// ReparseTopLevelAwait 会修改自己那份副本的 <c>diagnosticsLen</c>。
/// </summary>
public struct ParserState
{
    internal TsScannerState _scannerState;
    internal NodeFlags _contextFlags;
    internal int _diagnosticsLen;
    internal int _jsDiagnosticsLen;
    internal int _jsdocInfosLen;
    internal int _reparsedClonesLen;
    internal bool _statementHasAwaitIdentifier;
    internal bool _hasParseError;
}

/// <summary>
/// Go: <c>type Parser struct</c>。Go 里永远以 <c>*Parser</c> 使用（方法全是指针接收者），C# 用 class 表达同一语义。
/// </summary>
/// <remarks>
/// <b>字段声明只在本文件</b>（W1 的范围就是 Go 的 Parser 结构体所在处）：其它分片（Parser.Statements.cs 等）只使用这些
/// 字段、不得再声明；名字按 docs/architecture.md §2 为未导出字段的 <c>_camelCase</c>。可见性统一 `internal`，
/// 与 `Scanner` 的处理一致（同一 partial 类型的各部分本就能互访 private，`internal` 让同程序集的其它类也能读）。
/// </remarks>
public sealed partial class Parser
{
    internal TsScanner _scanner;
    internal NodeFactory _factory;

    internal SourceFileParseOptions _opts = new();
    internal string _sourceText = "";

    internal ScriptKind _scriptKind;
    internal LanguageVariant _languageVariant;
    internal List<Diagnostic> _diagnostics = [];
    internal List<Diagnostic> _jsDiagnostics = [];
    internal List<Diagnostic> _jsdocDiagnostics = [];

    internal Kind _token;
    internal NodeFlags _sourceFlags;
    internal NodeFlags _contextFlags;
    internal ParsingContexts _parsingContexts;
    internal bool _statementHasAwaitIdentifier;
    internal bool _hasDeprecatedTag;
    internal bool _hasParseError;

    internal int _identifierCount;
    internal Set<int> _notParenthesizedArrow = new();
    internal Arena<Node> _nodeSliceArena = new();
    internal Arena<string> _stringSliceArena = new();
    internal List<JSDocInfo> _jsdocInfos = [];
    internal List<int> _possibleAwaitSpans = [];

    // Go 的 4 个 JSDoc scratch 空间。前两个 Go 只做 `x = x[:0]` 复用 → 非空 List + Clear()；
    // 后两个 Go 在 jsdoc.go 里写 `p.x = nil`（防 parseTagComments 重入，Go 自己标了 `!!! can parseTagComments call itself?`）
    // → 声明为可空，重入时嵌套调用拿到 null、外层仍持有原 List。
    internal List<string> _jsdocCommentsSpace = [];
    internal List<CommentRange> _jsdocCommentRangesSpace = [];
    internal List<string>? _jsdocTagCommentsSpace;
    internal List<Node>? _jsdocTagCommentsPartsSpace;

    internal List<Node> _reparseList = [];
    internal Node? _currentParent;
    internal Visitor _setParentFromContext;
    internal List<Node> _reparsedClones = [];

    /// <summary>
    /// Go: <c>func newParser() *Parser { res := &amp;Parser{}; res.initializeClosures(); return res }</c>。
    /// Go 的 `initializeClosures`（parser.go:148-153）把闭包赋给 `setParentFromContext`，这里直接内联在构造器里
    /// （C# 的"非空字段必须在构造器退出前赋值"要求赋值出现在构造器体内；该方法在整个 package 里也只有 newParser 一个调用者）。
    /// </summary>
    private Parser()
    {
        _scanner = NewScanner();
        _factory = new NodeFactory();
        _setParentFromContext = n =>
        {
            n.Parent = _currentParent;
            return false;
        };
    }

    /// <summary>Go: <c>var viableKeywordSuggestions = scanner.GetViableKeywordSuggestions()</c>（包级 var）。</summary>
    private static readonly string[] _viableKeywordSuggestions = GetViableKeywordSuggestions();

    /// <summary>
    /// Go: <c>var missingListNodes = make([]*ast.Node, 0, 1)</c>。**必须是独立的长度 0 数组**：写成 `[]` 会拿到
    /// <c>Array.Empty&lt;Node&gt;()</c> 单例，就会和"普通空列表"撞在一起（见文件头第 2 条）。
    /// </summary>
    private static readonly Node[] _missingListNodes = new Node[0];

    /// <summary>Go: <c>func isMissingNodeList(list *ast.NodeList) bool</c>（C# 用数组引用同一性替代 Go 的 cap+指针比较）。</summary>
    internal static bool IsMissingNodeList(NodeList? list) =>
        list is not null && ReferenceEquals(list.Nodes, _missingListNodes);

    /// <summary>Go: <c>var parserPool = sync.Pool{New: func() any { return newParser() }}</c> —— C# 方案的取舍见文件头第 1 条。</summary>
    [ThreadStatic]
    private static Parser? _pooledParser;

    /// <summary>Go: <c>func getParser() *Parser</c>。</summary>
    internal static Parser GetParser()
    {
        var parser = _pooledParser;
        if (parser is not null)
        {
            _pooledParser = null;
            return parser;
        }

        return NewParser();
    }

    /// <summary>Go: <c>func putParser(p *Parser)</c>（归还前按 Go 的 <c>*p = Parser{scanner: …, setParentFromContext: …}</c> 重置）。</summary>
    internal static void PutParser(Parser p)
    {
        p.Reset();
        _pooledParser = p;
    }

    /// <summary>Go: <c>func newParser() *Parser</c>。</summary>
    internal static Parser NewParser() => new();

    /// <summary>
    /// Go: putParser 里的 <c>*p = Parser{scanner: p.scanner, setParentFromContext: p.setParentFromContext}</c>。
    /// 与 Go 的差异见文件头第 7 条（集合 Clear() 复用容量；两个 Arena 对象保留）。
    /// </summary>
    private void Reset()
    {
        _factory = new NodeFactory();
        _opts = new SourceFileParseOptions();
        _sourceText = "";
        _scriptKind = ScriptKind.Unknown;
        _languageVariant = LanguageVariant.Standard;
        _diagnostics.Clear();
        _jsDiagnostics.Clear();
        _jsdocDiagnostics.Clear();
        _token = Kind.Unknown;
        _sourceFlags = NodeFlags.None;
        _contextFlags = NodeFlags.None;
        _parsingContexts = ParsingContexts.None;
        _statementHasAwaitIdentifier = false;
        _hasDeprecatedTag = false;
        _hasParseError = false;
        _identifierCount = 0;
        _notParenthesizedArrow.Clear();
        _jsdocInfos.Clear();
        _possibleAwaitSpans.Clear();
        _jsdocCommentsSpace.Clear();
        _jsdocCommentRangesSpace.Clear();
        _jsdocTagCommentsSpace = null;
        _jsdocTagCommentsPartsSpace = null;
        _reparseList.Clear();
        _currentParent = null;
        _reparsedClones.Clear();

        // Go 保留：scanner（initializeState 会 Reset）与 setParentFromContext（闭包捕获的是本对象，归还后仍然有效）。
    }

    /// <summary>
    /// Go: <c>func ParseSourceFile(opts ast.SourceFileParseOptions, sourceText string, scriptKind core.ScriptKind) *ast.SourceFile</c>。
    /// </summary>
    public static SourceFile ParseSourceFile(SourceFileParseOptions opts, string sourceText, ScriptKind scriptKind)
    {
        var p = GetParser();
        try
        {
            p.InitializeState(opts, sourceText, scriptKind);
            p.NextToken();
            if (p._scriptKind == ScriptKind.JSON)
            {
                return p.ParseJSONText();
            }

            return p.ParseSourceFileWorker();
        }
        finally
        {
            // Go: defer putParser(p)
            PutParser(p);
        }
    }

    /// <summary>Go: <c>func (p *Parser) isJavaScript() bool</c>。</summary>
    internal bool IsJavaScript() => _scriptKind == ScriptKind.JS || _scriptKind == ScriptKind.JSX;

    /// <summary>Go: <c>func (p *Parser) parseJSONText() *ast.SourceFile</c>。</summary>
    internal SourceFile ParseJSONText()
    {
        var pos = NodePos();
        NodeList statements;
        Node eof;

        if (_token == Kind.EndOfFile)
        {
            statements = NewNodeList(new TextRange(pos, NodePos()), []);
            eof = ParseTokenNode();
        }
        else
        {
            // Go: `var expressions any`（*ast.Expression | []*ast.Expression）；C# 用"单个 + 列表"两个局部复刻其分支。
            Node? singleExpression = null;
            List<Node>? expressions = null;

            while (_token != Kind.EndOfFile)
            {
                // Go 的 parseArrayLiteralExpression/parseObjectLiteralExpression/… 返回 *ast.Expression；C# 的移植按
                // "Go 的错误路径可能返回 nil" 写成 Node?。JSON 顶层表达式的这些分支总会得到节点，故按 Go 的语义
                // （把 nil 放进切片）用 `!` 断言非空。
                Node? expression;
                switch (_token)
                {
                    case Kind.OpenBracketToken:
                        expression = ParseArrayLiteralExpression();
                        break;
                    case Kind.TrueKeyword:
                    case Kind.FalseKeyword:
                    case Kind.NullKeyword:
                        expression = ParseTokenNode();
                        break;
                    case Kind.MinusToken:
                        if (LookAhead(static p => p.NextToken() == Kind.NumericLiteral && p.NextToken() != Kind.ColonToken))
                        {
                            expression = ParsePrefixUnaryExpression();
                        }
                        else
                        {
                            expression = ParseObjectLiteralExpression();
                        }

                        break;
                    case Kind.NumericLiteral:
                    case Kind.StringLiteral:
                        if (LookAhead(static p => p.NextToken() != Kind.ColonToken))
                        {
                            expression = ParseLiteralExpression();
                            break;
                        }

                        goto default;
                    default:
                        expression = ParseObjectLiteralExpression();
                        break;
                }

                // Error recovery: collect multiple top-level expressions
                if (expressions is not null)
                {
                    expressions.Add(expression!);
                }
                else if (singleExpression is not null)
                {
                    expressions = [singleExpression, expression!];
                    singleExpression = null;
                }
                else
                {
                    singleExpression = expression;
                    if (_token != Kind.EndOfFile)
                    {
                        ParseErrorAtCurrentToken(Unexpected_token);
                    }
                }
            }

            Node finishedExpression;
            if (expressions is not null)
            {
                finishedExpression = FinishNode(
                    _factory.NewArrayLiteralExpression(NewNodeList(new TextRange(pos, NodePos()), expressions.ToArray()), false),
                    pos);
            }
            else
            {
                finishedExpression = singleExpression!;
            }

            var statement = FinishNode(_factory.NewExpressionStatement(finishedExpression), pos);
            statements = NewNodeList(new TextRange(pos, NodePos()), [statement]);
            eof = ParseExpectedToken(Kind.EndOfFile);
        }

        var node = FinishNode(_factory.NewSourceFile(_opts, _sourceText, statements, eof), pos);
        var result = node.AsSourceFile()!;
        var resultStatements = result.Statements!;
        var resultStatementNodes = resultStatements.Nodes ?? [];
        if (resultStatementNodes.Length > 0)
        {
            ValidateJsonValue(result, resultStatementNodes[0].Expression());
        }

        FinishSourceFile(result, false);
        return result;
    }

    /// <summary>Go: <c>func getErrorSpanForNode(sourceText string, node *ast.Node) core.TextRange</c>。</summary>
    internal static TextRange GetErrorSpanForNode(string sourceText, Node node)
    {
        var pos = node.Pos();
        if (!NodeIsMissing(node))
        {
            pos = SkipTrivia(sourceText, pos);
        }

        return new TextRange(pos, node.End());
    }

    /// <summary>Go: <c>func (p *Parser) validateJsonValue(sourceFile *ast.SourceFile, valueExpression *ast.Expression)</c>。</summary>
    internal void ValidateJsonValue(SourceFile sourceFile, Node? valueExpression)
    {
        if (valueExpression is null)
        {
            return;
        }

        switch (valueExpression.Kind)
        {
            case Kind.TrueKeyword:
            case Kind.FalseKeyword:
            case Kind.NullKeyword:
            case Kind.NumericLiteral:
                return;
            case Kind.StringLiteral:
                if (!IsDoubleQuotedString(valueExpression))
                {
                    _diagnostics.Add(NewDiagnostic(sourceFile, GetErrorSpanForNode(_sourceText, valueExpression), String_literal_with_double_quotes_expected));
                }

                return;
            case Kind.PrefixUnaryExpression:
                var prefixUnary = valueExpression.AsPrefixUnaryExpression()!;
                if (prefixUnary.Operator != Kind.MinusToken || prefixUnary.Operand!.Kind != Kind.NumericLiteral)
                {
                    break; // not valid JSON syntax
                }

                return;
            case Kind.ObjectLiteralExpression:
                ValidateJsonObjectLiteral(sourceFile, valueExpression.AsObjectLiteralExpression()!);
                return;
            case Kind.ArrayLiteralExpression:
                foreach (var element in valueExpression.Elements() ?? [])
                {
                    ValidateJsonValue(sourceFile, element);
                }

                return;
            default:
                break;
        }

        _diagnostics.Add(NewDiagnostic(sourceFile, GetErrorSpanForNode(_sourceText, valueExpression), Property_value_can_only_be_string_literal_numeric_literal_true_false_null_object_literal_or_array_literal));
    }

    /// <summary>Go: <c>func isDoubleQuotedString(node *ast.Node) bool</c>。</summary>
    internal static bool IsDoubleQuotedString(Node node) =>
        IsStringLiteral(node) && (node.AsStringLiteral()!.TokenFlags & TokenFlags.SingleQuote) == 0;

    /// <summary>Go: <c>func (p *Parser) validateJsonObjectLiteral(sourceFile *ast.SourceFile, node *ast.ObjectLiteralExpression)</c>。</summary>
    internal void ValidateJsonObjectLiteral(SourceFile sourceFile, ObjectLiteralExpression node)
    {
        foreach (var element in node.Properties!.Nodes ?? [])
        {
            if (element.Kind != Kind.PropertyAssignment)
            {
                _diagnostics.Add(NewDiagnostic(sourceFile, GetErrorSpanForNode(_sourceText, element), Property_assignment_expected));
                continue;
            }

            var name = element.Name();
            if (name is not null && !IsDoubleQuotedString(name))
            {
                _diagnostics.Add(NewDiagnostic(sourceFile, GetErrorSpanForNode(_sourceText, name), String_literal_with_double_quotes_expected));
            }

            ValidateJsonValue(sourceFile, element.AsPropertyAssignment()!.Initializer);
        }
    }

    /// <summary>Go: <c>func ParseIsolatedEntityName(text string) *ast.EntityName</c>。</summary>
    public static Node? ParseIsolatedEntityName(string text)
    {
        var p = GetParser();
        try
        {
            p.InitializeState(new SourceFileParseOptions(), text, ScriptKind.JS);
            p.NextToken();
            var entityName = p.ParseEntityName(true, false, null);
            return p._token == Kind.EndOfFile && p._diagnostics.Count == 0 ? entityName : null;
        }
        finally
        {
            // Go: defer putParser(p)
            PutParser(p);
        }
    }

    /// <summary>Go: <c>func (p *Parser) initializeState(opts ast.SourceFileParseOptions, sourceText string, scriptKind core.ScriptKind)</c>。</summary>
    internal void InitializeState(SourceFileParseOptions opts, string sourceText, ScriptKind scriptKind)
    {
        if (scriptKind == ScriptKind.Unknown)
        {
            Debug.Fail("ScriptKind must be specified when parsing source file: " + opts.FileName);
        }

        // Go: `if p.scanner == nil { p.scanner = scanner.NewScanner() } else { p.scanner.Reset() }`。
        // C# 里 scanner 由构造器保证存在（池化的 Parser 也保留同一个 Scanner），所以只剩 Reset 这条路径。
        _scanner.Reset();
        _opts = opts;
        _sourceText = sourceText;
        _scriptKind = scriptKind;
        _languageVariant = GetLanguageVariant(_scriptKind);
        switch (_scriptKind)
        {
            case ScriptKind.JS:
            case ScriptKind.JSX:
                _contextFlags = NodeFlags.JavaScriptFile;
                break;
            case ScriptKind.JSON:
                _contextFlags = NodeFlags.JavaScriptFile | NodeFlags.JsonFile;
                break;
            default:
                _contextFlags = NodeFlags.None;
                break;
        }

        _scanner.SetText(_sourceText);
        _scanner.SetOnError(ScanError);
        _scanner.SetLanguageVariant(_languageVariant);
    }

    /// <summary>Go: <c>func (p *Parser) scanError(message *diagnostics.Message, pos int, length int, args ...any)</c>（scanner 的错误回调）。</summary>
    internal void ScanError(Message message, int pos, int length, params object?[] args)
    {
        ParseErrorAtRange(new TextRange(pos, pos + length), message, args);
    }

    /// <summary>Go: <c>func (p *Parser) parseErrorAt(pos int, end int, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。</summary>
    internal Diagnostic? ParseErrorAt(int pos, int end, Message message, params object?[] args) =>
        ParseErrorAtRange(new TextRange(pos, end), message, args);

    /// <summary>Go: <c>func (p *Parser) parseErrorAtCurrentToken(message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。</summary>
    internal Diagnostic? ParseErrorAtCurrentToken(Message message, params object?[] args) =>
        ParseErrorAtRange(_scanner.TokenRange(), message, args);

    /// <summary>
    /// Go: <c>func (p *Parser) parseErrorAtRange(loc core.TextRange, message *diagnostics.Message, args ...any) *ast.Diagnostic</c>。
    /// 返回**可空**：与最后一条诊断同位置时 Go 返回 nil（且不追加）。
    /// </summary>
    internal Diagnostic? ParseErrorAtRange(TextRange loc, Message message, params object?[] args)
    {
        // Don't report another error if it would just be at the same location as the last error
        Diagnostic? result = null;
        if (_diagnostics.Count == 0 || _diagnostics[^1].Pos() != loc.Pos())
        {
            result = NewDiagnostic(null, loc, message, args);
            _diagnostics.Add(result);
        }

        _hasParseError = true;
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) mark() ParserState</c>。</summary>
    internal ParserState Mark() => new()
    {
        _scannerState = _scanner.Mark(),
        _contextFlags = _contextFlags,
        _diagnosticsLen = _diagnostics.Count,
        _jsDiagnosticsLen = _jsDiagnostics.Count,
        _jsdocInfosLen = _jsdocInfos.Count,
        _reparsedClonesLen = _reparsedClones.Count,
        _statementHasAwaitIdentifier = _statementHasAwaitIdentifier,
        _hasParseError = _hasParseError,
    };

    /// <summary>
    /// Go: <c>func (p *Parser) rewind(state ParserState)</c>。Go 的切片截断 `s = s[0:n]` → `RemoveRange`：
    /// 长度不足时与 Go 的切片越界 panic 同义地抛 ArgumentOutOfRangeException。
    /// </summary>
    internal void Rewind(ParserState state)
    {
        _scanner.Rewind(state._scannerState);
        _token = _scanner.Token();
        _contextFlags = state._contextFlags;
        if (_diagnostics.Count != state._diagnosticsLen)
        {
            _diagnostics.RemoveRange(state._diagnosticsLen, _diagnostics.Count - state._diagnosticsLen);
        }

        if (_jsDiagnostics.Count != state._jsDiagnosticsLen)
        {
            _jsDiagnostics.RemoveRange(state._jsDiagnosticsLen, _jsDiagnostics.Count - state._jsDiagnosticsLen);
        }

        if (_jsdocInfos.Count != state._jsdocInfosLen)
        {
            _jsdocInfos.RemoveRange(state._jsdocInfosLen, _jsdocInfos.Count - state._jsdocInfosLen);
        }

        if (_reparsedClones.Count != state._reparsedClonesLen)
        {
            _reparsedClones.RemoveRange(state._reparsedClonesLen, _reparsedClones.Count - state._reparsedClonesLen);
        }

        _statementHasAwaitIdentifier = state._statementHasAwaitIdentifier;
        _hasParseError = state._hasParseError;
    }

    /// <summary>Go: <c>func (p *Parser) lookAhead(callback func(p *Parser) bool) bool</c>。</summary>
    internal bool LookAhead(Func<Parser, bool> callback)
    {
        var state = Mark();
        var result = callback(this);
        Rewind(state);
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) nextToken() ast.Kind</c>。</summary>
    internal Kind NextToken()
    {
        // if the keyword had an escape
        if (IsKeyword(_token) && (_scanner.HasUnicodeEscape() || _scanner.HasExtendedUnicodeEscape()))
        {
            // issue a parse error for the escape
            ParseErrorAtCurrentToken(Keywords_cannot_contain_escape_characters);
        }

        _token = _scanner.Scan();
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) nextTokenWithoutCheck() ast.Kind</c>。</summary>
    internal Kind NextTokenWithoutCheck()
    {
        _token = _scanner.Scan();
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) nextTokenJSDoc() ast.Kind</c>。</summary>
    internal Kind NextTokenJSDoc()
    {
        _token = _scanner.ScanJSDocToken();
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) nextJSDocCommentTextToken(inBackticks bool) ast.Kind</c>。</summary>
    internal Kind NextJSDocCommentTextToken(bool inBackticks)
    {
        _token = _scanner.ScanJSDocCommentTextToken(inBackticks);
        return _token;
    }

    /// <summary>Go: <c>func (p *Parser) nodePos() int</c>。</summary>
    internal int NodePos() => _scanner.TokenFullStart();

    /// <summary>Go: <c>func (p *Parser) hasPrecedingLineBreak() bool</c>。</summary>
    internal bool HasPrecedingLineBreak() => _scanner.HasPrecedingLineBreak();

    /// <summary>
    /// Go: <c>func (p *Parser) jsdocScannerInfo() jsdocScannerInfo</c>。
    /// **属性而非方法**：Go 里方法名与方法返回类型同名，C# 里方法会遮蔽类型名（`JsdocScannerInfo.HasJSDoc` 会 CS0119），
    /// 属性不会（"Color Color" 规则）—— 与 docs/ast.md 对 `SubtreeFacts`/`ModifierFlags` 的处理同一理由。
    /// </summary>
    internal JsdocScannerInfo JsdocScannerInfo
    {
        get
        {
            if (!_scanner.HasPrecedingJSDocComment())
            {
                return JsdocScannerInfo.None;
            }

            var info = JsdocScannerInfo.HasJSDoc;
            if (_scanner.HasPrecedingJSDocWithDeprecatedTag())
            {
                info |= JsdocScannerInfo.HasDeprecated;
            }

            if (_scanner.HasPrecedingJSDocWithSeeOrLink())
            {
                info |= JsdocScannerInfo.HasSeeOrLink;
            }

            return info;
        }
    }

    /// <summary>Go: <c>func (p *Parser) parseSourceFileWorker() *ast.SourceFile</c>。</summary>
    internal SourceFile ParseSourceFileWorker()
    {
        var isDeclarationFile = IsDeclarationFileName(_opts.FileName);
        if (isDeclarationFile)
        {
            _contextFlags |= NodeFlags.Ambient;
        }

        var pos = NodePos();
        var statements = ParseListIndex(ParsingContext.SourceElements, static (p, i) => p.ParseToplevelStatement(i));
        var end = NodePos();
        var endJSDoc = JsdocScannerInfo;
        var eof = ParseTokenNode();
        WithJSDoc(eof, endJSDoc);
        if (eof.Kind != Kind.EndOfFile)
        {
            Debug.Fail("Expected end of file token from scanner.");
        }

        var statementList = new List<Node>(statements);
        if (_reparseList.Count != 0)
        {
            statementList.AddRange(_reparseList);
            _reparseList.Clear();
        }

        var node = FinishNode(_factory.NewSourceFile(_opts, _sourceText, NewNodeList(new TextRange(pos, end), statementList.ToArray()), eof), pos);
        var result = node.AsSourceFile()!;
        FinishSourceFile(result, isDeclarationFile);
        if (!result.IsDeclarationFile && result.ExternalModuleIndicator is not null && _possibleAwaitSpans.Count > 0)
        {
            var reparse = FinishNode(ReparseTopLevelAwait(result), pos);
            if (!ReferenceEquals(node, reparse))
            {
                result = reparse.AsSourceFile()!;
                FinishSourceFile(result, isDeclarationFile);
            }
        }

        CollectExternalModuleReferences(result);
        if (IsInJSFile(node))
        {
            result.SetJSDiagnostics(AttachFileToDiagnostics(_jsDiagnostics.ToArray(), result));
        }

        return result;
    }

    /// <summary>Go: <c>func (p *Parser) finishSourceFile(result *ast.SourceFile, isDeclarationFile bool)</c>。</summary>
    internal void FinishSourceFile(SourceFile result, bool isDeclarationFile)
    {
        result.CommentDirectives = _scanner.CommentDirectives();
        result.Pragmas = GetCommentPragmas(_factory, _sourceText);
        ProcessPragmasIntoFields(result);
        result.SetDiagnostics(AttachFileToDiagnostics(_diagnostics.ToArray(), result));
        result.SetJSDocDiagnostics(AttachFileToDiagnostics(_jsdocDiagnostics.ToArray(), result));
        result.IsDeclarationFile = isDeclarationFile;
        result.LanguageVariant = _languageVariant;
        result.ScriptKind = _scriptKind;
        result.Flags |= _sourceFlags;
        result.NodeCount = _factory.NodeCount();
        result.TextCount = _factory.TextCount();
        result.IdentifierCount = _identifierCount;
        result.SetJSDocCache(CreateJSDocCache());

        // For non-JS files, enable lazy JSDoc parsing on demand
        if (!IsJavaScript())
        {
            result.SetHasLazyJSDoc(true);
        }

        // Go: slices.SortFunc(p.reparsedClones, ast.CompareNodePositions)（两者都不稳定排序，语义一致）
        _reparsedClones.Sort(CompareNodePositions);
        result.ReparsedClones = _reparsedClones.ToArray();
        SetExternalModuleIndicator(result, _opts.ExternalModuleIndicatorOptions);
    }

    /// <summary>Go: <c>func (p *Parser) createJSDocCache() map[*ast.Node][]*ast.Node</c>。</summary>
    internal Dictionary<Node, Node[]?>? CreateJSDocCache()
    {
        if (_jsdocInfos.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<Node, Node[]?>(_jsdocInfos.Count);
        foreach (var info in _jsdocInfos)
        {
            // Go 的 map 允许 nil 键；C# 的 Dictionary 不接受 null 键 —— 调用点（jsdoc.go/reparser.go）永远传非 nil 的 parent。
            result[info._parent!] = info._jsDocs;
        }

        return result;
    }

    /// <summary>Go: <c>func (p *Parser) parseToplevelStatement(i int) *ast.Node</c>。</summary>
    internal Node ParseToplevelStatement(int i)
    {
        _statementHasAwaitIdentifier = false;
        var statement = ParseStatement();

        // Reparsed nodes (e.g. JSDoc @typedef) produced while parsing this statement are inserted
        // into the statement list before this statement, so account for them when recording the
        // statement's index for possibleAwaitSpans.
        i += _reparseList.Count;
        if (_statementHasAwaitIdentifier && (statement.Flags & NodeFlags.AwaitContext) == 0)
        {
            if (_possibleAwaitSpans.Count == 0 || _possibleAwaitSpans[^1] != i)
            {
                _possibleAwaitSpans.Add(i);
                _possibleAwaitSpans.Add(i + 1);
            }
            else
            {
                _possibleAwaitSpans[^1] = i + 1;
            }
        }

        return statement;
    }

    /// <summary>Go: <c>func (p *Parser) reparseTopLevelAwait(sourceFile *ast.SourceFile) *ast.Node</c>。</summary>
    internal Node ReparseTopLevelAwait(SourceFile sourceFile)
    {
        if (_possibleAwaitSpans.Count % 2 == 1)
        {
            Debug.Fail("possibleAwaitSpans malformed: odd number of indices, not paired into spans.");
        }

        var statements = new List<Node>();
        // Go 这里把 p.diagnostics 换成**新的空切片**（旧的留给 savedParseDiagnostics），所以 C# 也必须换对象、不能 Clear()。
        var savedParseDiagnostics = _diagnostics;
        _diagnostics = [];

        var awaitStatementNodes = sourceFile.Statements!.Nodes ?? [];
        var afterAwaitStatement = 0;
        for (var i = 0; i < _possibleAwaitSpans.Count; i += 2)
        {
            var nextAwaitStatement = _possibleAwaitSpans[i];

            // append all non-await statements between afterAwaitStatement and nextAwaitStatement
            var prevStatement = awaitStatementNodes[afterAwaitStatement];
            var nextStatement = awaitStatementNodes[nextAwaitStatement];
            statements.AddRange(awaitStatementNodes.AsSpan(afterAwaitStatement, nextAwaitStatement - afterAwaitStatement));

            // append all diagnostics associated with the copied range
            var diagnosticStart = savedParseDiagnostics.FindIndex(diagnostic => diagnostic.Pos() >= prevStatement.Pos());
            var diagnosticEnd = -1;
            if (diagnosticStart >= 0)
            {
                // Go 的 core.FindIndex(savedParseDiagnostics[diagnosticStart:], …) 返回**相对**下标；
                // List.FindIndex(startIndex, …) 返回绝对下标，故这里减去起点还原为相对值。
                var absoluteEnd = savedParseDiagnostics.FindIndex(diagnosticStart, diagnostic => diagnostic.Pos() >= nextStatement.Pos());
                diagnosticEnd = absoluteEnd >= 0 ? absoluteEnd - diagnosticStart : -1;
            }

            if (diagnosticStart >= 0)
            {
                var diagnosticCount = diagnosticEnd >= 0 ? diagnosticEnd : savedParseDiagnostics.Count - diagnosticStart;
                _diagnostics.AddRange(CollectionsMarshal.AsSpan(savedParseDiagnostics).Slice(diagnosticStart, diagnosticCount));
            }

            var state = Mark();

            // reparse all statements between start and pos. We skip existing diagnostics for the same range and allow the parser to generate new ones.
            _contextFlags |= NodeFlags.AwaitContext;
            _scanner.ResetPos(nextStatement.Pos());
            NextToken();

            afterAwaitStatement = _possibleAwaitSpans[i + 1];
            while (_token != Kind.EndOfFile)
            {
                var startPos = _scanner.TokenFullStart();
                var statement = ParseStatement();
                statements.Add(statement);
                if (startPos == _scanner.TokenFullStart())
                {
                    NextToken();
                }

                if (afterAwaitStatement < awaitStatementNodes.Length)
                {
                    var lastAwaitStatement = awaitStatementNodes[afterAwaitStatement - 1];
                    if (statement.End() == lastAwaitStatement.End())
                    {
                        // done reparsing this section
                        break;
                    }

                    if (statement.End() > lastAwaitStatement.End())
                    {
                        // we ate into the next statement, so we must continue reparsing the next span
                        i += 2;
                        if (i < _possibleAwaitSpans.Count)
                        {
                            afterAwaitStatement = _possibleAwaitSpans[i + 1];
                        }
                        else
                        {
                            afterAwaitStatement = awaitStatementNodes.Length;
                        }
                    }
                }
            }

            // Keep diagnostics from the reparse
            state._diagnosticsLen = _diagnostics.Count;
            Rewind(state);
        }

        // append all statements between pos and the end of the list
        if (afterAwaitStatement < awaitStatementNodes.Length)
        {
            var prevStatement = awaitStatementNodes[afterAwaitStatement];
            statements.AddRange(awaitStatementNodes.AsSpan(afterAwaitStatement));

            // append all diagnostics associated with the copied range
            var diagnosticStart = savedParseDiagnostics.FindIndex(diagnostic => diagnostic.Pos() >= prevStatement.Pos());
            if (diagnosticStart >= 0)
            {
                _diagnostics.AddRange(CollectionsMarshal.AsSpan(savedParseDiagnostics).Slice(diagnosticStart));
            }
        }

        var result = _factory.NewSourceFile(sourceFile.ParseOptions(), _sourceText, NewNodeList(sourceFile.Statements!.Loc, statements.ToArray()), sourceFile.EndOfFileToken);
        foreach (var s in statements)
        {
            s.Parent = result; // force (re)set parent to reparsed source file
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func (p *Parser) parseListIndex(kind ParsingContext, parseElement func(p *Parser, index int) *ast.Node) []*ast.Node</c>。
    /// </summary>
    /// <remarks>
    /// Go 在进入本层时先保存外层的 `p.reparseList`（切片头），再把 `p.reparseList` 置 nil；解析完一个元素后，把该元素
    /// （及其嵌套列表）产生的 reparse 节点按"是否应向外传播"分流：传播的**留在**本层列表里（下一层继续往上传），不传播的
    /// 移进本层的 `list`。C# 的 `List` 没有"切片头别名"这回事，所以用**进入时的起始下标** `pendingStart` 复刻同一语义：
    /// 新产生的条目就是 `[_reparseList[pendingStart..]]`，把不传播的条目从该区间里摘掉（剩下的仍是连续的、顺序不变），
    /// 结尾再 `_reparseList = outerReparseList`（同一个对象，与 Go 的赋值等价）。
    /// </remarks>
    internal Node[] ParseListIndex(ParsingContext kind, Func<Parser, int, Node> parseElement)
    {
        var saveParsingContexts = _parsingContexts;
        _parsingContexts |= 1 << (int)kind;
        var outerReparseList = _reparseList;
        var pendingStart = outerReparseList.Count;
        var list = new List<Node>(16);
        for (var i = 0; !IsListTerminator(kind); i++)
        {
            if (IsListElement(kind, false /*inErrorRecovery*/))
            {
                var elt = parseElement(this, list.Count);
                if (_reparseList.Count != pendingStart)
                {
                    var kept = pendingStart;
                    for (var k = pendingStart; k < _reparseList.Count; k++)
                    {
                        var e = _reparseList[k];

                        // Propagate @typedef type alias declarations outwards to a context that permits them.
                        if ((IsJSTypeAliasDeclaration(e) || IsJSImportDeclaration(e)) && kind != ParsingContext.SourceElements && kind != ParsingContext.BlockStatements)
                        {
                            _reparseList[kept++] = e;
                        }
                        else
                        {
                            list.Add(e);
                        }
                    }

                    if (kept != _reparseList.Count)
                    {
                        _reparseList.RemoveRange(kept, _reparseList.Count - kept);
                    }
                }

                list.Add(elt);
                continue;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        _reparseList = outerReparseList;
        _parsingContexts = saveParsingContexts;
        return _nodeSliceArena.Clone(CollectionsMarshal.AsSpan(list));
    }

    /// <summary>Go: <c>func (p *Parser) parseList(kind ParsingContext, parseElement func(p *Parser) *ast.Node) *ast.NodeList</c>。</summary>
    internal NodeList ParseList(ParsingContext kind, Func<Parser, Node> parseElement)
    {
        var pos = NodePos();
        var nodes = ParseListIndex(kind, (p, _) => parseElement(p));
        return NewNodeList(new TextRange(pos, NodePos()), nodes);
    }

    /// <summary>
    /// Go: <c>func (p *Parser) parseDelimitedList(kind ParsingContext, parseElement func(p *Parser) *ast.Node) *ast.NodeList</c>。
    /// Return a non-nil (but possibly empty) NodeList if parsing was successful, or nil if parseElement returned nil →
    /// C# 的返回类型是 `NodeList?`，回调是 `Func&lt;Parser, Node?&gt;`。
    /// </summary>
    internal NodeList? ParseDelimitedList(ParsingContext kind, Func<Parser, Node?> parseElement)
    {
        var pos = NodePos();
        var saveParsingContexts = _parsingContexts;
        _parsingContexts |= 1 << (int)kind;
        var list = new List<Node>(16);
        while (true)
        {
            if (IsListElement(kind, false /*inErrorRecovery*/))
            {
                var startPos = NodePos();
                var element = parseElement(this);
                if (element is null)
                {
                    _parsingContexts = saveParsingContexts;

                    // Return nil to indicate parseElement failed
                    return null;
                }

                list.Add(element);
                if (ParseOptional(Kind.CommaToken))
                {
                    // No need to check for a zero length node since we know we parsed a comma
                    continue;
                }

                if (IsListTerminator(kind))
                {
                    break;
                }

                // We didn't get a comma, and the list wasn't terminated, explicitly parse
                // out a comma so we give a good error message.
                if (_token != Kind.CommaToken && kind == ParsingContext.EnumMembers)
                {
                    ParseErrorAtCurrentToken(An_enum_member_name_must_be_followed_by_a_or);
                }
                else
                {
                    ParseExpected(Kind.CommaToken);
                }

                // If the token was a semicolon, and the caller allows that, then skip it and
                // continue.  This ensures we get back on track and don't result in tons of
                // parse errors.  For example, this can happen when people do things like use
                // a semicolon to delimit object literal members.   Note: we'll have already
                // reported an error when we called parseExpected above.
                if ((kind == ParsingContext.ObjectLiteralMembers || kind == ParsingContext.ImportAttributes) && _token == Kind.SemicolonToken && !HasPrecedingLineBreak())
                {
                    NextToken();
                }

                if (startPos == NodePos())
                {
                    // What we're parsing isn't actually remotely recognizable as a element and we've consumed no tokens whatsoever
                    // Consume a token to advance the parser in some way and avoid an infinite loop
                    // This can happen when we're speculatively parsing parenthesized expressions which we think may be arrow functions,
                    // or when a modifier keyword which is disallowed as a parameter name (ie, `static` in strict mode) is supplied
                    NextToken();
                }

                continue;
            }

            if (IsListTerminator(kind))
            {
                break;
            }

            if (AbortParsingListOrMoveToNextToken(kind))
            {
                break;
            }
        }

        _parsingContexts = saveParsingContexts;
        return NewNodeList(new TextRange(pos, NodePos()), _nodeSliceArena.Clone(CollectionsMarshal.AsSpan(list)));
    }

    /// <summary>
    /// Go: <c>func (p *Parser) parseBracketedList(kind ParsingContext, parseElement func(p *Parser) *ast.Node, opening ast.Kind, closing ast.Kind) *ast.NodeList</c>。
    /// Return a non-nil (but possibly empty) NodeList if parsing was successful, a missing NodeList if the opening
    /// token wasn't found, or nil if parseElement returned nil.
    /// </summary>
    internal NodeList? ParseBracketedList(ParsingContext kind, Func<Parser, Node?> parseElement, Kind opening, Kind closing)
    {
        if (ParseExpected(opening))
        {
            var result = ParseDelimitedList(kind, parseElement);
            ParseExpected(closing);
            return result;
        }

        return CreateMissingList();
    }

    /// <summary>Go: <c>func (p *Parser) parseEmptyNodeList() *ast.NodeList</c>（Go 传 nil 切片，C# 按 docs §3.2 传空数组）。</summary>
    internal NodeList ParseEmptyNodeList() => NewNodeList(new TextRange(NodePos(), NodePos()), []);

    /// <summary>Go: <c>func (p *Parser) createMissingList() *ast.NodeList</c>。</summary>
    internal NodeList CreateMissingList()
    {
        var result = ParseEmptyNodeList();
        result.Nodes = _missingListNodes;
        return result;
    }

    /// <summary>Go: <c>func (p *Parser) abortParsingListOrMoveToNextToken(kind ParsingContext) bool</c>。Returns true if we should abort parsing.</summary>
    internal bool AbortParsingListOrMoveToNextToken(ParsingContext kind)
    {
        ParsingContextErrors(kind);
        if (IsInSomeParsingContext())
        {
            return true;
        }

        NextToken();
        return false;
    }

    /// <summary>Go: <c>func (p *Parser) isInSomeParsingContext() bool</c>。True if positioned at element or terminator of the current list or any enclosing list.</summary>
    internal bool IsInSomeParsingContext()
    {
        // We should be in at least one parsing context, be it SourceElements while parsing
        // a SourceFile, or JSDocComment when lazily parsing JSDoc.
        Debug.Assert(_parsingContexts != 0, "Missing parsing context");
        for (var kind = 0; kind < (int)ParsingContext.Count; kind++)
        {
            if ((_parsingContexts & (1 << kind)) != 0)
            {
                if (IsListElement((ParsingContext)kind, true /*inErrorRecovery*/) || IsListTerminator((ParsingContext)kind))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func (p *Parser) parsingContextErrors(context ParsingContext)</c>。</summary>
    internal void ParsingContextErrors(ParsingContext context)
    {
        switch (context)
        {
            case ParsingContext.SourceElements:
                if (_token == Kind.DefaultKeyword)
                {
                    ParseErrorAtCurrentToken(X_0_expected, "export");
                }
                else
                {
                    ParseErrorAtCurrentToken(Declaration_or_statement_expected);
                }

                break;
            case ParsingContext.BlockStatements:
                ParseErrorAtCurrentToken(Declaration_or_statement_expected);
                break;
            case ParsingContext.SwitchClauses:
                ParseErrorAtCurrentToken(X_case_or_default_expected);
                break;
            case ParsingContext.SwitchClauseStatements:
                ParseErrorAtCurrentToken(Statement_expected);
                break;
            case ParsingContext.RestProperties:
            case ParsingContext.TypeMembers:
                ParseErrorAtCurrentToken(Property_or_signature_expected);
                break;
            case ParsingContext.ClassMembers:
                ParseErrorAtCurrentToken(Unexpected_token_A_constructor_method_accessor_or_property_was_expected);
                break;
            case ParsingContext.EnumMembers:
                ParseErrorAtCurrentToken(Enum_member_expected);
                break;
            case ParsingContext.HeritageClauseElement:
                ParseErrorAtCurrentToken(Expression_expected);
                break;
            case ParsingContext.VariableDeclarations:
                if (IsKeyword(_token))
                {
                    ParseErrorAtCurrentToken(X_0_is_not_allowed_as_a_variable_declaration_name, TokenToString(_token));
                }
                else
                {
                    ParseErrorAtCurrentToken(Variable_declaration_expected);
                }

                break;
            case ParsingContext.ObjectBindingElements:
                ParseErrorAtCurrentToken(Property_destructuring_pattern_expected);
                break;
            case ParsingContext.ArrayBindingElements:
                ParseErrorAtCurrentToken(Array_element_destructuring_pattern_expected);
                break;
            case ParsingContext.ArgumentExpressions:
                ParseErrorAtCurrentToken(Argument_expression_expected);
                break;
            case ParsingContext.ObjectLiteralMembers:
                ParseErrorAtCurrentToken(Property_assignment_expected);
                break;
            case ParsingContext.ArrayLiteralMembers:
                ParseErrorAtCurrentToken(Expression_or_comma_expected);
                break;
            case ParsingContext.JSDocParameters:
                ParseErrorAtCurrentToken(Parameter_declaration_expected);
                break;
            case ParsingContext.Parameters:
                if (IsKeyword(_token))
                {
                    ParseErrorAtCurrentToken(X_0_is_not_allowed_as_a_parameter_name, TokenToString(_token));
                }
                else
                {
                    ParseErrorAtCurrentToken(Parameter_declaration_expected);
                }

                break;
            case ParsingContext.TypeParameters:
                ParseErrorAtCurrentToken(Type_parameter_declaration_expected);
                break;
            case ParsingContext.TypeArguments:
                ParseErrorAtCurrentToken(Type_argument_expected);
                break;
            case ParsingContext.TupleElementTypes:
                ParseErrorAtCurrentToken(Type_expected);
                break;
            case ParsingContext.HeritageClauses:
                ParseErrorAtCurrentToken(Unexpected_token_expected);
                break;
            case ParsingContext.ImportOrExportSpecifiers:
                if (_token == Kind.FromKeyword)
                {
                    ParseErrorAtCurrentToken(X_0_expected, "}");
                }
                else
                {
                    ParseErrorAtCurrentToken(Identifier_expected);
                }

                break;
            case ParsingContext.JsxAttributes:
            case ParsingContext.JsxChildren:
            case ParsingContext.JSDocComment:
                ParseErrorAtCurrentToken(Identifier_expected);
                break;
            case ParsingContext.ImportAttributes:
                ParseErrorAtCurrentToken(Identifier_or_string_literal_expected);
                break;
            default:
                Debug.Fail("Unhandled case in parsingContextErrors");
                break;
        }
    }

    /// <summary>Go: <c>func (p *Parser) isListElement(parsingContext ParsingContext, inErrorRecovery bool) bool</c>。</summary>
    internal bool IsListElement(ParsingContext parsingContext, bool inErrorRecovery)
    {
        switch (parsingContext)
        {
            case ParsingContext.SourceElements:
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauseStatements:
                // If we're in error recovery, then we don't want to treat ';' as an empty statement.
                // The problem is that ';' can show up in far too many contexts, and if we see one
                // and assume it's a statement, then we may bail out inappropriately from whatever
                // we're parsing.  For example, if we have a semicolon in the middle of a class, then
                // we really don't want to assume the class is over and we're on a statement in the
                // outer module.  We just want to consume and move on.
                return !(_token == Kind.SemicolonToken && inErrorRecovery) && IsStartOfStatement();
            case ParsingContext.SwitchClauses:
                return _token == Kind.CaseKeyword || _token == Kind.DefaultKeyword;
            case ParsingContext.TypeMembers:
                return LookAhead(static p => p.ScanTypeMemberStart());
            case ParsingContext.ClassMembers:
                // We allow semicolons as class elements (as specified by ES6) as long as we're
                // not in error recovery.  If we're in error recovery, we don't want an errant
                // semicolon to be treated as a class member (since they're almost always used
                // for statements.
                return LookAhead(static p => p.ScanClassMemberStart()) || (_token == Kind.SemicolonToken && !inErrorRecovery);
            case ParsingContext.EnumMembers:
                // Include open bracket computed properties. This technically also lets in indexers,
                // which would be a candidate for improved error reporting.
                return _token == Kind.OpenBracketToken || IsLiteralPropertyName();
            case ParsingContext.ObjectLiteralMembers:
                switch (_token)
                {
                    // Not an object literal member, but don't want to close the object
                    // (see `tests/cases/fourslash/completionsDotInObjectLiteral.ts`)
                    case Kind.OpenBracketToken:
                    case Kind.AsteriskToken:
                    case Kind.DotDotDotToken:
                    case Kind.DotToken:
                        return true;
                    default:
                        return IsLiteralPropertyName();
                }

            case ParsingContext.RestProperties:
                return IsLiteralPropertyName();
            case ParsingContext.ObjectBindingElements:
                return _token == Kind.OpenBracketToken || _token == Kind.DotDotDotToken || IsLiteralPropertyName();
            case ParsingContext.ImportAttributes:
                return IsImportAttributeName();
            case ParsingContext.HeritageClauseElement:
                // If we see `{ ... }` then only consume it as an expression if it is followed by `,` or `{`
                // That way we won't consume the body of a class in its heritage clause.
                if (_token == Kind.OpenBraceToken)
                {
                    return IsValidHeritageClauseObjectLiteral();
                }

                if (!inErrorRecovery)
                {
                    return IsStartOfLeftHandSideExpression() && !IsHeritageClauseExtendsOrImplementsKeyword();
                }

                // If we're in error recovery we tighten up what we're willing to match.
                // That way we don't treat something like "this" as a valid heritage clause
                // element during recovery.
                return IsIdentifier() && !IsHeritageClauseExtendsOrImplementsKeyword();
            case ParsingContext.VariableDeclarations:
                return IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.ArrayBindingElements:
                return _token == Kind.CommaToken || _token == Kind.DotDotDotToken || IsBindingIdentifierOrPrivateIdentifierOrPattern();
            case ParsingContext.TypeParameters:
                return _token == Kind.InKeyword || _token == Kind.ConstKeyword || IsIdentifier();
            case ParsingContext.ArrayLiteralMembers:
                // Not an array literal member, but don't want to close the array
                // (see `tests/cases/fourslash/completionsDotInArrayLiteralInObjectLiteral.ts`)
                if (_token == Kind.CommaToken || _token == Kind.DotToken)
                {
                    return true;
                }

                goto case ParsingContext.ArgumentExpressions;
            case ParsingContext.ArgumentExpressions:
                return _token == Kind.DotDotDotToken || IsStartOfExpression();
            case ParsingContext.Parameters:
                return IsStartOfParameter(false /*isJSDocParameter*/);
            case ParsingContext.JSDocParameters:
                return IsStartOfParameter(true /*isJSDocParameter*/);
            case ParsingContext.TypeArguments:
            case ParsingContext.TupleElementTypes:
                return _token == Kind.CommaToken || IsStartOfType(false /*inStartOfParameter*/);
            case ParsingContext.HeritageClauses:
                return IsHeritageClause();
            case ParsingContext.ImportOrExportSpecifiers:
                // bail out if the next token is [FromKeyword StringLiteral].
                // That means we're in something like `import { from "mod"`. Stop here can give better error message.
                if (_token == Kind.FromKeyword && LookAhead(static p => p.NextTokenIsTokenStringLiteral()))
                {
                    return false;
                }

                if (_token == Kind.StringLiteral)
                {
                    return true; // For "arbitrary module namespace identifiers"
                }

                return TokenIsIdentifierOrKeyword(_token);
            case ParsingContext.JsxAttributes:
                return TokenIsIdentifierOrKeyword(_token) || _token == Kind.OpenBraceToken;
            case ParsingContext.JsxChildren:
                return true;
            case ParsingContext.JSDocComment:
                return true;
            default:
                Debug.Fail("Unhandled case in isListElement");
                throw new System.Diagnostics.UnreachableException();
        }
    }

    /// <summary>Go: <c>func (p *Parser) isListTerminator(kind ParsingContext) bool</c>。</summary>
    internal bool IsListTerminator(ParsingContext kind)
    {
        if (_token == Kind.EndOfFile)
        {
            return true;
        }

        switch (kind)
        {
            case ParsingContext.BlockStatements:
            case ParsingContext.SwitchClauses:
            case ParsingContext.TypeMembers:
            case ParsingContext.ClassMembers:
            case ParsingContext.EnumMembers:
            case ParsingContext.ObjectLiteralMembers:
            case ParsingContext.ObjectBindingElements:
            case ParsingContext.ImportOrExportSpecifiers:
            case ParsingContext.ImportAttributes:
                return _token == Kind.CloseBraceToken;
            case ParsingContext.SwitchClauseStatements:
                return _token == Kind.CloseBraceToken || _token == Kind.CaseKeyword || _token == Kind.DefaultKeyword;
            case ParsingContext.HeritageClauseElement:
                return _token == Kind.OpenBraceToken || _token == Kind.ExtendsKeyword || _token == Kind.ImplementsKeyword;
            case ParsingContext.VariableDeclarations:
                // If we can consume a semicolon (either explicitly, or with ASI), then consider us done
                // with parsing the list of variable declarators.
                // In the case where we're parsing the variable declarator of a 'for-in' statement, we
                // are done if we see an 'in' keyword in front of us. Same with for-of
                // ERROR RECOVERY TWEAK:
                // For better error recovery, if we see an '=>' then we just stop immediately.  We've got an
                // arrow function here and it's going to be very unlikely that we'll resynchronize and get
                // another variable declaration.
                return CanParseSemicolon() || _token == Kind.InKeyword || _token == Kind.OfKeyword || _token == Kind.EqualsGreaterThanToken;
            case ParsingContext.TypeParameters:
                // Tokens other than '>' are here for better error recovery
                return _token == Kind.GreaterThanToken || _token == Kind.OpenParenToken || _token == Kind.OpenBraceToken || _token == Kind.ExtendsKeyword || _token == Kind.ImplementsKeyword;
            case ParsingContext.ArgumentExpressions:
                // Tokens other than ')' are here for better error recovery
                return _token == Kind.CloseParenToken || _token == Kind.SemicolonToken;
            case ParsingContext.ArrayLiteralMembers:
            case ParsingContext.TupleElementTypes:
            case ParsingContext.ArrayBindingElements:
                return _token == Kind.CloseBracketToken;
            case ParsingContext.JSDocParameters:
            case ParsingContext.Parameters:
            case ParsingContext.RestProperties:
                // Tokens other than ')' and ']' (the latter for index signatures) are here for better error recovery
                return _token == Kind.CloseParenToken || _token == Kind.CloseBracketToken /*|| token == ast.KindOpenBraceToken*/;
            case ParsingContext.TypeArguments:
                // All other tokens should cause the type-argument to terminate except comma token
                return _token != Kind.CommaToken;
            case ParsingContext.HeritageClauses:
                return _token == Kind.OpenBraceToken || _token == Kind.CloseBraceToken;
            case ParsingContext.JsxAttributes:
                return _token == Kind.GreaterThanToken || _token == Kind.SlashToken;
            case ParsingContext.JsxChildren:
                return _token == Kind.LessThanToken && LookAhead(static p => p.NextTokenIsSlash());
        }

        return false;
    }

    /// <summary>Go: <c>func (p *Parser) parseExpectedJSDoc(kind ast.Kind) bool</c>。</summary>
    internal bool ParseExpectedJSDoc(Kind kind)
    {
        if (_token == kind)
        {
            NextTokenJSDoc();
            return true;
        }

        if (!IsKeywordOrPunctuation(kind))
        {
            Debug.Fail("Invalid JSDoc kind: expected keyword or punctuation");
        }

        ParseErrorAtCurrentToken(X_0_expected, TokenToString(kind));
        return false;
    }

    /// <summary>Go: <c>func (p *Parser) parseExpectedMatchingBrackets(openKind ast.Kind, closeKind ast.Kind, openParsed bool, openPosition int)</c>。</summary>
    internal void ParseExpectedMatchingBrackets(Kind openKind, Kind closeKind, bool openParsed, int openPosition)
    {
        if (_token == closeKind)
        {
            NextToken();
            return;
        }

        var lastError = ParseErrorAtCurrentToken(X_0_expected, TokenToString(closeKind));
        if (!openParsed)
        {
            return;
        }

        if (lastError is not null)
        {
            var related = NewDiagnostic(null, new TextRange(openPosition, openPosition), The_parser_expected_to_find_a_1_to_match_the_0_token_here, TokenToString(openKind), TokenToString(closeKind));
            lastError.AddRelatedInfo(related);
        }
    }

    /// <summary>Go: <c>func (p *Parser) parseOptional(token ast.Kind) bool</c>。</summary>
    internal bool ParseOptional(Kind token)
    {
        if (_token == token)
        {
            NextToken();
            return true;
        }

        return false;
    }

    /// <summary>Go: <c>func (p *Parser) parseExpected(kind ast.Kind) bool</c>。</summary>
    internal bool ParseExpected(Kind kind) => ParseExpectedWithDiagnostic(kind, null, true);

    /// <summary>Go: <c>func (p *Parser) parseExpectedWithoutAdvancing(kind ast.Kind) bool</c>。</summary>
    internal bool ParseExpectedWithoutAdvancing(Kind kind) => ParseExpectedWithDiagnostic(kind, null, false);

    /// <summary>Go: <c>func (p *Parser) parseExpectedWithDiagnostic(kind ast.Kind, message *diagnostics.Message, shouldAdvance bool) bool</c>。</summary>
    internal bool ParseExpectedWithDiagnostic(Kind kind, Message? message, bool shouldAdvance)
    {
        if (_token == kind)
        {
            if (shouldAdvance)
            {
                NextToken();
            }

            return true;
        }

        // Report specific message if provided with one.  Otherwise, report generic fallback message.
        if (message is not null)
        {
            ParseErrorAtCurrentToken(message);
        }
        else
        {
            ParseErrorAtCurrentToken(X_0_expected, TokenToString(kind));
        }

        return false;
    }

    /// <summary>Go: <c>func (p *Parser) parseTokenNode() *ast.Node</c>。</summary>
    internal Node ParseTokenNode()
    {
        var pos = NodePos();
        var kind = _token;
        NextToken();
        return FinishNode(_factory.NewToken(kind), pos);
    }

    /// <summary>Go: <c>func (p *Parser) parseExpectedToken(kind ast.Kind) *ast.Node</c>。</summary>
    internal Node ParseExpectedToken(Kind kind)
    {
        var token = ParseOptionalToken(kind);
        if (token is null)
        {
            ParseErrorAtCurrentToken(X_0_expected, TokenToString(kind));
            token = FinishNode(_factory.NewToken(kind), NodePos());
        }

        return token;
    }

    /// <summary>Go: <c>func (p *Parser) parseOptionalToken(kind ast.Kind) *ast.Node</c>。</summary>
    internal Node? ParseOptionalToken(Kind kind) => _token == kind ? ParseTokenNode() : null;

    /// <summary>Go: <c>func (p *Parser) parseExpectedTokenJSDoc(kind ast.Kind) *ast.Node</c>。</summary>
    internal Node ParseExpectedTokenJSDoc(Kind kind)
    {
        var optional = ParseOptionalTokenJSDoc(kind);
        if (optional is null)
        {
            if (!IsKeywordOrPunctuation(kind))
            {
                Debug.Fail("expected keyword or punctuation");
            }

            ParseErrorAtCurrentToken(X_0_expected, TokenToString(kind));
            optional = FinishNode(_factory.NewToken(kind), NodePos());
        }

        return optional;
    }

    /// <summary>Go: <c>func (p *Parser) parseOptionalTokenJSDoc(kind ast.Kind) *ast.Node</c>。</summary>
    internal Node? ParseOptionalTokenJSDoc(Kind kind) => _token == kind ? ParseTokenNode() : null;
}
