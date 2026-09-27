// ported from: internal/ast/ast.go（SourceFile 本体与 ContentMapper* 元数据：ast.go:2404-3110；含包级 GetDeclarationName）
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.2/§3.5/§3.8、docs/ast.md §2）：
// - Go 的未导出字段 → `_camelCase`（`fileName` → `_fileName`、`parseOptions` → `_parseOptions`、`text` → `_text`…）；
//   未导出方法 → PascalCase（`copyFrom` → `CopyFrom`、`computeDeclarationMap` → `ComputeDeclarationMap`、
//   `createToken` → `CreateToken`、`resolveJSDoc` → `ResolveJSDoc`）。同程序集其它类要用的未导出成员用 internal。
// - `sync.Once` / `RWMutex` → 字段 + `lock` + `Volatile` 双重检查（Go 的 ecmaLineMap / nameTable / positionMap /
//   identifiers / tokenCache / declarationMap / dataMu）。`bindOnce` 直接用 `isBound` 当 once 标志（语义相同：
//   第一个调用者跑完 bind 之后其它调用者才返回）。JSDoc 的 `RWMutex` 也落成 `lock`：读路径会被串行化，但 JSDoc
//   缓存只有 parser 注册回调之后才使用、且不在扫描热路径上。
// - `SetContentMapperInfo`：Go 的参数是结构体值，存的是它的**副本**指针（`&info`）→ C# 用 `MemberwiseClone()`
//   复刻这个拷贝语义，避免与 Go 的"copyFrom 传 `*other.contentMapperInfo` 又是一份拷贝"产生可观察差异。
// - `map[K]V` → `Dictionary<K,V>`：Go 的 nil map 分支保留（C# 的 null 字典 + 惰性创建）；"取不存在的键得到 nil
//   切片" → `GetValueOrDefault` + `is null`。返回 nil 切片的地方一律给 `[]`（Core.cs 文件头的约定）。
// - `TokenCacheKey`（AstTypes.cs 里是 class，而 Go 里是结构体 map 键）→ 本文件提供 `TokenCacheKeyComparer`
//   复刻 Go 的结构体键语义（Parent 指针相等 + Loc 值相等），否则 Dictionary 会退化成引用相等。
// - `Hash xxh3.Uint128` → `UInt128`（C# 没有 xxh3；这个字段是"不透明的 128 位哈希"，用途与 Go 相同。语言服务
//   落地时需要补 xxh3 实现，那是 M8 的事）。
// - `GetDeclarationMap` 的 `map[string][]*Node` → `Dictionary<string, List<Node>>`：Go 里是 append 与"替换最后一个
//   元素"，用 List 才能逐字对应。
// - `createToken` 里 Go 的 `file.text[pos:end]` 切片视图 → `Substring`：令牌必须自己持有文本（工厂方法把 text
//   存进字段），C# 字符串无法是另一个字符串的视图，这是 §3.8 无法覆盖的例外。
// - Go 的 `parseJSDocForNode` 包级变量 → `Ast._parseJSDocForNodeCallback`（Ast.cs 里的静态字段）。
// - `SourceFile` 在 Go 里结构性地满足 `HasFileName`/`SourceFileLike`，C# 要显式写在基类型表里。
// - Node.JSDoc/EagerJSDoc（ast.go:1563-1590）落在 Node.cs（见那边的注释）；本文件提供它们要用的
//   `GetCachedJSDoc`/`GetCachedJSDocUnderLock`/`ResolveJSDoc`/`SetJSDocCache`/`SetHasLazyJSDoc`。
using System.Runtime.CompilerServices;
using System.Threading;
using TypeScript.Collections;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using Path = TypeScript.TSPath.Path;
using SpanMapType = TypeScript.SpanMap.SpanMap;

namespace TypeScript.Ast;

/// <summary>
/// Go: <c>type MappedDiagnosticDirectivePolicy uint8</c>（成员名去 Go 的 <c>MappedDiagnosticDirectivePolicy</c> 前缀）。
/// </summary>
public enum MappedDiagnosticDirectivePolicy : byte
{
    /// <summary>Go: <c>MappedDiagnosticDirectivePolicyIgnore</c>。</summary>
    Ignore = 0,

    /// <summary>Go: <c>MappedDiagnosticDirectivePolicyExpect</c>。</summary>
    Expect = 1,
}

/// <summary>Go: <c>type MappedDiagnosticDirective struct</c>。</summary>
public sealed class MappedDiagnosticDirective
{
    public TextRange OriginalRange;
    public TextRange VirtualRange;
    public MappedDiagnosticDirectivePolicy Policy;
    public int UnusedCode;
    public string UnusedMessageText = "";
    public string Source = "";
}

/// <summary>
/// Go: <c>type ContentMapperSourceFileInfo struct</c>。内容映射器给源文件挂的全部元数据。
/// </summary>
public sealed class ContentMapperSourceFileInfo
{
    public string ContentMapper = "";
    public string TransformIdentity = "";
    public SourceFileParseOptions ParseOptions = new();
    public string VirtualFileName = "";
    public string OriginalText = "";

    /// <summary>Go: <c>SpanMap *spanmap.SpanMap</c>（nil = 映射器失败产生的占位文件）。</summary>
    public SpanMapType? SpanMap;

    public MappedDiagnosticDirective[]? DiagnosticDirectives;
    public SourceFile[]? SupplementalSourceFiles;
    public SourceFile? CanonicalSourceFile;

    /// <summary>Go 里 `ContentMapperSourceFileInfo` 是结构体，赋值即拷贝 → C# 用 <c>MemberwiseClone</c> 复刻（§3.2）。</summary>
    internal ContentMapperSourceFileInfo Clone() => (ContentMapperSourceFileInfo)MemberwiseClone();
}

/// <summary>
/// Go: <c>type SourceFile struct</c>（ast.go:2446-2524 的字段 + 各方法的宿主）。
/// </summary>
// Go 里 *ast.SourceFile 结构化满足 sourcemap.Source 与 modulespecifiers.SourceFileForSpecifierGeneration；
// C# 显式实现（docs §3.2）。
public sealed partial class SourceFile : Node, IDeclarationBase, ILocalsContainerBase, ICompositeBase, HasFileName, SourceFileLike, TypeScript.SourceMap.Source, TypeScript.ModuleSpecifiers.SourceFileForSpecifierGeneration
{
    // ── Fields set by NewSourceFile ──

    /// <summary>Go: <c>fileName string</c>（"For debugging convenience"；FileName() 走 parseOptions）。</summary>
    internal string _fileName = "";

    internal SourceFileParseOptions _parseOptions = new();
    internal string _text = "";
    internal ContentMapperSourceFileInfo? _contentMapperInfo;

    // Go: Statements *NodeList（非 optional）
    public new NodeList? Statements;

    // Go: EndOfFileToken *TokenNode
    public Node? EndOfFileToken;

    // ── Fields for lazily-computed data owned by packages outside ast ──

    private readonly object _dataMu = new();
    private Dictionary<ulong, object>? _data;

    // ── Fields set by parser ──

    internal Diagnostic[]? _diagnostics;
    private Diagnostic[]? _jsDiagnostics;
    private Diagnostic[]? _jsdocDiagnostics;
    public LanguageVariant LanguageVariant;
    public ScriptKind ScriptKind;
    public bool IsDeclarationFile;
    public Tristate UsesUriStyleNodeCoreModules;
    public int IdentifierCount;
    /// <summary>Go: <c>imports []*LiteralLikeNode</c>（utilities.go 的 SetImportsOfSourceFile 直接写字段 → internal）。</summary>
    internal Node[]? _imports;

    /// <summary>Go: <c>ModuleAugmentations []*ModuleName</c>。</summary>
    public Node[]? ModuleAugmentations;

    public string[]? AmbientModuleNames;
    public CommentDirective[]? CommentDirectives;

    // Go: jsdocCache map[*Node][]*Node + jsdocMu sync.RWMutex + hasLazyJSDoc bool
    internal Dictionary<Node, Node[]?>? _jsdocCache;
    private readonly object _jsdocMu = new();
    internal bool _hasLazyJSDoc;

    private readonly object _identifiersLock = new();
    private Set<string>? _identifiers;
    public Node[]? ReparsedClones;
    public Pragma[]? Pragmas;
    public FileReference[]? ReferencedFiles;
    public FileReference[]? TypeReferenceDirectives;
    public FileReference[]? LibReferenceDirectives;
    public CheckJsDirective? CheckJsDirective;
    public int NodeCount;
    public int TextCount;
    public Node? CommonJSModuleIndicator;

    /// <summary>
    /// Go: <c>ExternalModuleIndicator *Node</c>。若它等于 SourceFile 自身，说明这个模块是被"强制"当作外部模块的
    /// （以前叫 "true"）。
    /// </summary>
    public Node? ExternalModuleIndicator;

    // ── Fields set by binder ──

    private readonly object _bindLock = new();
    private bool _isBound;
    private Diagnostic[]? _bindDiagnostics;
    public int SymbolCount;
    public PatternAmbientModule[]? PatternAmbientModules;

    /// <summary>Go: <c>GlobalExports SymbolTable</c>（nil map ↔ C# null）。</summary>
    public SymbolTable? GlobalExports;

    // ── Fields set by ECMALineMap ──

    private readonly object _ecmaLineMapLock = new();
    private TextPos[]? _ecmaLineMap;

    // ── Fields set by language service ──

    /// <summary>Go: <c>Hash xxh3.Uint128</c>（见文件头关于 <see cref="UInt128"/> 的说明）。</summary>
    public UInt128 Hash;

    private readonly object _tokenCacheLock = new();
    private Dictionary<TokenCacheKey, Node>? _tokenCache;
    private NodeFactory? _tokenFactory;
    private readonly object _declarationMapLock = new();
    private Dictionary<string, List<Node>>? _declarationMap;
    private readonly object _nameTableLock = new();
    private Dictionary<string, int>? _nameTable;

    // ── Fields for UTF-8 to UTF-16 position mapping ──

    private readonly object _positionMapLock = new();
    private PositionMap? _positionMap;

    // Go: DeclarationBase.Symbol（goOnly）
    public new Symbol? Symbol;

    Symbol? IDeclarationBase.Symbol
    {
        get => Symbol;
        set => Symbol = value;
    }

    // Go: LocalsContainerBase.Locals / NextContainer（goOnly）
    public new SymbolTable? Locals;
    public Node? NextContainer;

    SymbolTable? ILocalsContainerBase.Locals
    {
        get => Locals;
        set => Locals = value;
    }

    Node? ILocalsContainerBase.NextContainer
    {
        get => NextContainer;
        set => NextContainer = value;
    }

    // Go: DeclarationBase.DeclarationData() / LocalsContainerBase.LocalsContainerData() 都 `return node`（ast.go:1513/1530）。
    // 生成的节点由生成器发射这两个 override；手写的 SourceFile 漏了它们，会让 Ast.GetLocals/IsLocalsContainer
    // 对 SourceFile 失效（NRE / 恒 false）——Binder 冒烟实测到过。
    public override IDeclarationBase? DeclarationData() => this;

    public override ILocalsContainerBase? LocalsContainerData() => this;

    // Go: CompositeBase.facts atomic.Uint32（缓存 SubtreeFacts；见 Node.SubtreeFacts）
    internal int _facts;

    int ICompositeBase.Facts
    {
        get => Volatile.Read(ref _facts);
        set => Volatile.Write(ref _facts, value);
    }

    // ── func (f *NodeFactory) NewSourceFile（ast.go:2526-2538）──

    public SourceFileParseOptions ParseOptions() => _parseOptions;

    /// <summary>Go: <c>func (node *SourceFile) Text() string</c>（遮蔽 Node.Text()：Go 里 SourceFile 的字段同名）。</summary>
    public new string Text() => _text;

    /// <summary>Go: <c>func (node *SourceFile) OriginalText() string</c>：内容映射文件返回未变换的源文本，否则同 Text()。</summary>
    public string OriginalText() => ContentMapper() != "" ? _contentMapperInfo!.OriginalText : _text;

    /// <summary>Go: <c>func (node *SourceFile) OriginalFileName() string</c>：补充源文件返回它的规范文件名，否则 FileName()。</summary>
    public string OriginalFileName()
    {
        var canonical = CanonicalSourceFile();
        return canonical is not null ? canonical.FileName() : FileName();
    }

    /// <summary>
    /// Go: <c>func (node *SourceFile) SpanMap() *spanmap.SpanMap</c>：把本文件变换后的 Text() 映射回原始未变换内容的
    /// 段映射；非内容映射文件（或失败占位）返回 nil。返回的 map 是 nil 安全的（nil map 恒等映射）。
    /// </summary>
    public SpanMapType? SpanMap() => _contentMapperInfo?.SpanMap;

    /// <summary>Go: <c>func (node *SourceFile) ContentMapper() string</c>：产生本文件的内容映射器标识，非映射文件为 ""。</summary>
    public string ContentMapper() => _contentMapperInfo?.ContentMapper ?? "";

    /// <summary>Go: <c>func (node *SourceFile) IsContentMapperFailureStub() bool</c>。</summary>
    public bool IsContentMapperFailureStub() => ContentMapper() != "" && SpanMap() is null;

    public string ContentMapperTransformIdentity() => _contentMapperInfo?.TransformIdentity ?? "";

    public string VirtualFileName() => _contentMapperInfo?.VirtualFileName ?? "";

    /// <summary>Go: <c>func (node *SourceFile) ContentMapperParseOptions() SourceFileParseOptions</c>（从映射解析缓存取得本文件时用的解析选项）。</summary>
    public SourceFileParseOptions ContentMapperParseOptions() =>
        _contentMapperInfo is not null ? _contentMapperInfo.ParseOptions : new SourceFileParseOptions();

    /// <summary>Go: <c>func (node *SourceFile) SetContentMapperInfo(info ContentMapperSourceFileInfo)</c>。</summary>
    public void SetContentMapperInfo(ContentMapperSourceFileInfo info)
    {
        if (_contentMapperInfo is not null)
        {
            throw new DebugFailureException("content mapper source file info already set");
        }

        // Go: node.contentMapperInfo = &info —— 存的是参数结构体的副本。
        _contentMapperInfo = info.Clone();
    }

    public MappedDiagnosticDirective[] DiagnosticDirectives() => _contentMapperInfo?.DiagnosticDirectives ?? [];

    /// <summary>Go: <c>func (node *SourceFile) SupplementalSourceFiles() []*SourceFile</c>：由这个规范源文件产生的补充输出。</summary>
    public SourceFile[] SupplementalSourceFiles() => _contentMapperInfo?.SupplementalSourceFiles ?? [];

    /// <summary>Go: <c>func (node *SourceFile) CanonicalSourceFile() *SourceFile</c>：这个补充源文件对应的规范输出。</summary>
    public SourceFile? CanonicalSourceFile() => _contentMapperInfo?.CanonicalSourceFile;

    /// <summary>Go: <c>func (node *SourceFile) IsContentMapperSupplemental() bool</c>：是否是无名的补充映射输出。</summary>
    public bool IsContentMapperSupplemental() => CanonicalSourceFile() is not null;

    /// <summary>Go: <c>func (file *SourceFile) HasIdentifier(name string) bool</c>。</summary>
    public bool HasIdentifier(string name)
    {
        var identifiers = Volatile.Read(ref _identifiers);
        if (identifiers is null)
        {
            lock (_identifiersLock)
            {
                identifiers = _identifiers ??= CollectIdentifiersForSourceFile(this);
            }
        }

        return identifiers.Has(name);
    }

    /// <summary>Go: <c>func collectIdentifiersForSourceFile(sourceFile *SourceFile) collections.Set[string]</c>。</summary>
    private static Set<string> CollectIdentifiersForSourceFile(SourceFile sourceFile)
    {
        var identifiers = new Set<string>();
        bool Collect(Node node)
        {
            switch (node.Kind)
            {
                case Kind.Identifier:
                case Kind.PrivateIdentifier:
                case Kind.StringLiteral:
                case Kind.NumericLiteral:
                case Kind.BigIntLiteral:
                case Kind.NoSubstitutionTemplateLiteral:
                    identifiers.Add(node.Text());
                    break;
            }

            node.ForEachChild(Collect);
            return false;
        }

        Collect(sourceFile.AsNode());
        return identifiers;
    }

    // ── FileName / Path / parser-set accessors（ast.go:2701-2744）──

    /// <summary>
    /// Go: <c>func (file *SourceFile) GetOrComputeData[T any](key *SourceFileDataKey[T], compute func(*SourceFile) T) T</c>
    /// （ast.go:2404-2412）。懒计算数据由 ast 之外的 package 拥有，键由 <see cref="Ast.NewSourceFileDataKey{T}"/> 分配。
    /// </summary>
    public T GetOrComputeData<T>(SourceFileDataKey<T> key, Func<SourceFile, T> compute) =>
        GetDataCell(key).Value(this, compute);

    /// <summary>Go: <c>func (file *SourceFile) getDataCell[T any](key *SourceFileDataKey[T]) *sourceFileDataCell[T]</c>。</summary>
    private SourceFileDataCell<T> GetDataCell<T>(SourceFileDataKey<T>? key)
    {
        if (key is null || key._key == 0)
        {
            throw new DebugFailureException("invalid SourceFileDataKey; use NewSourceFileDataKey");
        }

        lock (_dataMu)
        {
            _data ??= [];
            if (_data.TryGetValue(key._key, out var cell))
            {
                // Go: cell.(*sourceFileDataCell[T])（类型不符即 panic）。
                return (SourceFileDataCell<T>)cell;
            }

            var created = new SourceFileDataCell<T>();
            _data[key._key] = created;
            return created;
        }
    }

    /// <summary>
    /// Go: <c>type sourceFileDataCell[T any] struct { once sync.Once; value T }</c>。
    /// **偏离**：AstTypes.cs 的文件头写的是 `Lazy&lt;T&gt;`，但 `Lazy&lt;T&gt;` 带
    /// `DynamicallyAccessedMemberTypes.PublicParameterlessConstructor` 标注，未约束的 `T` 会触发 IL2091
    /// （AOT/裁剪分析器 → 编译错误），所以这里手写与 sync.Once 等价的"恰好一次 + 可见性屏障"。
    /// </summary>
    private sealed class SourceFileDataCell<T>
    {
        private readonly object _lock = new();
        private bool _initialized;
        private T? _value;

        internal T Value(SourceFile file, Func<SourceFile, T> compute)
        {
            if (Volatile.Read(ref _initialized))
            {
                // 已初始化（标志置位时 _value 必然已赋值）。
                return _value!;
            }

            lock (_lock)
            {
                if (!_initialized)
                {
                    _value = compute(file);
                    Volatile.Write(ref _initialized, true);
                }

                return _value!;
            }
        }
    }

    public string FileName() => _parseOptions.FileName;

    public Path Path() => _parseOptions.Path;

    /// <summary>Go: <c>func (node *SourceFile) Imports() []*LiteralLikeNode</c>。</summary>
    public Node[] Imports() => _imports ?? [];

    public Diagnostic[] Diagnostics() => _diagnostics ?? [];

    public void SetDiagnostics(Diagnostic[]? diags) => _diagnostics = diags;

    public Diagnostic[] JSDiagnostics() => _jsDiagnostics ?? [];

    public void SetJSDiagnostics(Diagnostic[]? diags) => _jsDiagnostics = diags;

    public Diagnostic[] JSDocDiagnostics() => _jsdocDiagnostics ?? [];

    public void SetJSDocDiagnostics(Diagnostic[]? diags) => _jsdocDiagnostics = diags;

    public void SetJSDocCache(Dictionary<Node, Node[]?>? cache) => _jsdocCache = cache;

    public void SetHasLazyJSDoc(bool lazy) => _hasLazyJSDoc = lazy;

    /// <summary>Go: Node.JSDoc 非懒路径里的 <c>file.jsdocCache[node]</c>（不取锁，与 Go 相同）。</summary>
    internal Node[]? GetCachedJSDoc(Node node) => _jsdocCache?.GetValueOrDefault(node);

    /// <summary>Go: Node.EagerJSDoc 懒路径里在 <c>jsdocMu.RLock</c> 下的 <c>file.jsdocCache[node]</c>。</summary>
    internal Node[]? GetCachedJSDocUnderLock(Node node)
    {
        lock (_jsdocMu)
        {
            return _jsdocCache?.GetValueOrDefault(node);
        }
    }

    /// <summary>Go: <c>func (node *SourceFile) resolveJSDoc(n *Node) []*Node</c>（懒解析 + 缓存）。</summary>
    internal Node[]? ResolveJSDoc(Node n)
    {
        var parseJSDocForNode = Ast._parseJSDocForNodeCallback ??
            throw new DebugFailureException("resolveJSDoc called but parseJSDocForNode is not registered; ensure the parser package is imported");

        // 快路径：在读锁下查缓存。
        lock (_jsdocMu)
        {
            if (_jsdocCache is not null && _jsdocCache.TryGetValue(n, out var cached))
            {
                return cached;
            }
        }

        // 慢路径：解析并写缓存（取写锁后再查一次，对应 Go 的双重检查）。
        lock (_jsdocMu)
        {
            if (_jsdocCache is not null && _jsdocCache.TryGetValue(n, out var cached))
            {
                return cached;
            }

            var jsdocs = parseJSDocForNode(this, n);
            _jsdocCache ??= [];
            _jsdocCache[n] = jsdocs;
            return jsdocs;
        }
    }

    public Diagnostic[] BindDiagnostics() => _bindDiagnostics ?? [];

    public void SetBindDiagnostics(Diagnostic[]? diags) => _bindDiagnostics = diags;

    // ── nodeData 方法集 ──

    /// <summary>Go: <c>func (node *SourceFile) ForEachChild(v Visitor) bool</c>。</summary>
    public override bool ForEachChild(Visitor v) =>
        Ast.VisitNodeList(v, Statements) || Ast.Visit(v, EndOfFileToken);

    /// <summary>Go: <c>func (node *SourceFile) VisitEachChild(v *NodeVisitor) *Node</c>。</summary>
    public override Node? VisitEachChild(NodeVisitor v) =>
        v.Factory.UpdateSourceFile(this, v.VisitTopLevelStatementsWithHooks(Statements), v.VisitTokenWithHooks(EndOfFileToken));

    /// <summary>Go: <c>func (node *SourceFile) IsJS() bool</c>。</summary>
    public bool IsJS() => Ast.IsSourceFileJS(this);

    /// <summary>Go: <c>func (node *SourceFile) copyFrom(other *SourceFile)</c>。</summary>
    internal void CopyFrom(SourceFile other)
    {
        // 不拷贝 NewSourceFile 设置的字段（Text/FileName/Path/Statements）。
        if (other._contentMapperInfo is not null)
        {
            SetContentMapperInfo(other._contentMapperInfo);
        }

        LanguageVariant = other.LanguageVariant;
        ScriptKind = other.ScriptKind;
        IsDeclarationFile = other.IsDeclarationFile;
        UsesUriStyleNodeCoreModules = other.UsesUriStyleNodeCoreModules;
        _imports = other._imports;
        ModuleAugmentations = other.ModuleAugmentations;
        AmbientModuleNames = other.AmbientModuleNames;
        CommentDirectives = other.CommentDirectives;
        Pragmas = other.Pragmas;
        ReferencedFiles = other.ReferencedFiles;
        TypeReferenceDirectives = other.TypeReferenceDirectives;
        LibReferenceDirectives = other.LibReferenceDirectives;
        CommonJSModuleIndicator = other.CommonJSModuleIndicator;
        ExternalModuleIndicator = other.ExternalModuleIndicator;
        Flags |= other.Flags;
    }

    /// <summary>Go: <c>func (node *SourceFile) Clone(f NodeFactoryCoercible) *Node</c>。</summary>
    public override Node? Clone(NodeFactoryCoercible f)
    {
        var factory = f.AsNodeFactory();
        var updated = factory.NewSourceFile(_parseOptions, _text, Statements, EndOfFileToken);
        var newFile = updated.AsSourceFile()!;
        newFile.CopyFrom(this);
        return Ast.CloneNode(updated, this, factory._hooks);
    }

    /// <summary>Go: <c>func (node *SourceFile) computeSubtreeFacts() SubtreeFacts</c>（ast.go:2821-2823）。</summary>
    internal override SubtreeFacts ComputeSubtreeFacts() =>
        Ast.PropagateNodeListSubtreeFacts(Statements, Ast.PropagateSubtreeFacts);

    // ── ECMALineMap / name table / position map / bind（ast.go:2834-2925）──

    /// <summary>Go: <c>func (node *SourceFile) ECMALineMap() []core.TextPos</c>。</summary>
    public TextPos[] ECMALineMap()
    {
        var lineMap = Volatile.Read(ref _ecmaLineMap);
        if (lineMap is not null)
        {
            return lineMap;
        }

        lock (_ecmaLineMapLock)
        {
            return _ecmaLineMap ??= ComputeECMALineStarts(Text());
        }
    }

    /// <summary>
    /// Go: <c>func (file *SourceFile) GetNameTable() map[string]int</c>：文件名 → 位置；同名出现多次时值是 -1。
    /// </summary>
    public Dictionary<string, int> GetNameTable()
    {
        var nameTable = Volatile.Read(ref _nameTable);
        if (nameTable is not null)
        {
            return nameTable;
        }

        lock (_nameTableLock)
        {
            if (_nameTable is not null)
            {
                return _nameTable;
            }

            var table = new Dictionary<string, int>(IdentifierCount);
            bool Walk(Node node)
            {
                if ((Ast.IsIdentifier(node) && !Ast.IsTagName(node) && node.Text() != "") ||
                    (Ast.IsStringOrNumericLiteralLike(node) && Ast.LiteralIsName(node)) ||
                    Ast.IsPrivateIdentifier(node))
                {
                    var text = node.Text();
                    if (table.ContainsKey(text))
                    {
                        table[text] = -1;
                    }
                    else
                    {
                        table[text] = node.Pos();
                    }
                }

                node.ForEachChild(Walk);
                var jsdocNodes = node.JSDoc(this);
                if (jsdocNodes is not null)
                {
                    foreach (var jsdoc in jsdocNodes)
                    {
                        jsdoc.ForEachChild(Walk);
                    }
                }

                return false;
            }

            ForEachChild(Walk);

            return _nameTable = table;
        }
    }

    /// <summary>Go: <c>func (node *SourceFile) IsBound() bool</c>。</summary>
    public bool IsBound() => Volatile.Read(ref _isBound);

    /// <summary>Go: <c>func (file *SourceFile) GetPositionMap() *PositionMap</c>（惰性计算）。</summary>
    public PositionMap GetPositionMap()
    {
        var positionMap = Volatile.Read(ref _positionMap);
        if (positionMap is not null)
        {
            return positionMap;
        }

        lock (_positionMapLock)
        {
            return _positionMap ??= Ast.ComputePositionMap(Text());
        }
    }

    /// <summary>Go: <c>func (node *SourceFile) BindOnce(bind func())</c>。</summary>
    public void BindOnce(Action bind)
    {
        // Go: bindOnce sync.Once + isBound atomic.Bool；这里用 isBound 本身当 once 标志（第一个调用者跑完后
        // 别人的 Volatile.Read 就能看到 true 而直接返回，与 sync.Once 的可观察行为一致）。
        if (Volatile.Read(ref _isBound))
        {
            return;
        }

        lock (_bindLock)
        {
            if (_isBound)
            {
                return;
            }

            bind();
            Volatile.Write(ref _isBound, true);
        }
    }

    // ── 令牌缓存（ast.go:2926-2995）──

    /// <summary>
    /// Go: <c>func (node *SourceFile) GetOrCreateToken(kind Kind, pos int, end int, parent *Node, flags TokenFlags) *TokenNode</c>。
    /// 只用于文件里本来就有的令牌，不要用来造合成令牌。
    /// </summary>
    public Node GetOrCreateToken(Kind kind, int pos, int end, Node parent, TokenFlags flags)
    {
        lock (_tokenCacheLock)
        {
            var loc = NewTextRange(pos, end);
            var key = new TokenCacheKey { Parent = parent, Loc = loc };
            if (_tokenCache is not null && _tokenCache.TryGetValue(key, out var cached))
            {
                if (cached.Kind != kind)
                {
                    throw new DebugFailureException($"Token cache mismatch: {cached.Kind.String()} != {kind.String()}");
                }

                return cached;
            }

            if ((parent.Flags & NodeFlags.Reparsed) != 0)
            {
                throw new DebugFailureException($"Cannot create token from reparsed node of kind {parent.Kind.String()}");
            }

            _tokenCache ??= new Dictionary<TokenCacheKey, Node>(TokenCacheKeyComparer.Instance);
            var token = CreateToken(kind, this, pos, end, flags);
            token.Loc = loc;
            token.Parent = parent;
            _tokenCache[key] = token;
            return token;
        }
    }

    /// <summary>Go: <c>func createToken(kind Kind, file *SourceFile, pos, end int, flags TokenFlags) *Node</c>（kind 必须是令牌 kind）。</summary>
    private static Node CreateToken(Kind kind, SourceFile file, int pos, int end, TokenFlags flags)
    {
        file._tokenFactory ??= Ast.NewNodeFactory(new NodeFactoryHooks());
        var text = file._text.Substring(pos, end - pos);
        switch (kind)
        {
            case Kind.NumericLiteral:
                return file._tokenFactory.NewNumericLiteral(text, flags);
            case Kind.BigIntLiteral:
                return file._tokenFactory.NewBigIntLiteral(text, flags);
            case Kind.StringLiteral:
                return file._tokenFactory.NewStringLiteral(text, flags);
            case Kind.JsxText:
            case Kind.JsxTextAllWhiteSpaces:
                return file._tokenFactory.NewJsxText(text, kind == Kind.JsxTextAllWhiteSpaces);
            case Kind.RegularExpressionLiteral:
                return file._tokenFactory.NewRegularExpressionLiteral(text, flags);
            case Kind.NoSubstitutionTemplateLiteral:
                return file._tokenFactory.NewNoSubstitutionTemplateLiteral(text, flags);
            case Kind.TemplateHead:
                return file._tokenFactory.NewTemplateHead(text, "" /*rawText*/, flags);
            case Kind.TemplateMiddle:
                return file._tokenFactory.NewTemplateMiddle(text, "" /*rawText*/, flags);
            case Kind.TemplateTail:
                return file._tokenFactory.NewTemplateTail(text, "" /*rawText*/, flags);
            case Kind.Identifier:
                return file._tokenFactory.NewIdentifier(text);
            case Kind.PrivateIdentifier:
                return file._tokenFactory.NewPrivateIdentifier(text);
            default: // 标点与关键字
                return file._tokenFactory.NewToken(kind);
        }
    }

    // ── 声明表（ast.go:2997-3103）──

    /// <summary>Go: <c>func (node *SourceFile) GetDeclarationMap() map[string][]*Node</c>。</summary>
    public Dictionary<string, List<Node>> GetDeclarationMap()
    {
        lock (_declarationMapLock)
        {
            return _declarationMap ??= ComputeDeclarationMap();
        }
    }

    /// <summary>Go: <c>func (node *SourceFile) computeDeclarationMap() map[string][]*Node</c>。</summary>
    private Dictionary<string, List<Node>> ComputeDeclarationMap()
    {
        var result = new Dictionary<string, List<Node>>();

        void AddDeclaration(Node declaration)
        {
            var name = Ast.GetDeclarationName(declaration);
            if (name != "")
            {
                if (!result.TryGetValue(name, out var list))
                {
                    list = [];
                    result[name] = list;
                }

                list.Add(declaration);
            }
        }

        bool Visit(Node node)
        {
            switch (node.Kind)
            {
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                {
                    var declarationName = Ast.GetDeclarationName(node);
                    if (declarationName != "")
                    {
                        if (!result.TryGetValue(declarationName, out var declarations))
                        {
                            declarations = [];
                            result[declarationName] = declarations;
                        }

                        var lastDeclaration = declarations.Count != 0 ? declarations[^1] : null;

                        // 这个声明属于一个"重载组"吗？
                        if (lastDeclaration is not null &&
                            ReferenceEquals(node.Parent, lastDeclaration.Parent) &&
                            ReferenceEquals(node.Symbol, lastDeclaration.Symbol))
                        {
                            // 若上一个是重载而当前是实现，就覆盖它。
                            if (node.Body() is not null && lastDeclaration.Body() is null)
                            {
                                declarations[^1] = node;
                            }
                        }
                        else
                        {
                            declarations.Add(node);
                        }
                    }

                    node.ForEachChild(Visit);
                    break;
                }

                case Kind.ClassDeclaration:
                case Kind.ClassExpression:
                case Kind.InterfaceDeclaration:
                case Kind.TypeAliasDeclaration:
                case Kind.EnumDeclaration:
                case Kind.ModuleDeclaration:
                case Kind.ImportEqualsDeclaration:
                case Kind.ImportClause:
                case Kind.NamespaceImport:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.TypeLiteral:
                    AddDeclaration(node);
                    node.ForEachChild(Visit);
                    break;
                case Kind.ImportSpecifier:
                case Kind.ExportSpecifier:
                    if (node.PropertyName() is not null)
                    {
                        AddDeclaration(node);
                    }

                    break;
                case Kind.Parameter:

                    // 只考虑参数属性（parameter properties）。
                    if (!Ast.HasSyntacticModifier(node, ModifierFlags.ParameterPropertyModifier))
                    {
                        break;
                    }

                    // Go 的 fallthrough。
                    goto case Kind.VariableDeclaration;
                case Kind.VariableDeclaration:
                case Kind.BindingElement:
                {
                    var name = node.Name();
                    if (name is not null)
                    {
                        if (Ast.IsBindingPattern(name))
                        {
                            node.Name()!.ForEachChild(Visit);
                        }
                        else
                        {
                            if (node.Initializer() is not null)
                            {
                                Visit(node.Initializer()!);
                            }

                            AddDeclaration(node);
                        }
                    }

                    break;
                }

                case Kind.EnumMember:
                case Kind.PropertyDeclaration:
                case Kind.PropertySignature:
                    AddDeclaration(node);
                    break;
                case Kind.ExportDeclaration:
                {
                    // 处理具名导出，例如：export {a, b as B} from "mod";
                    var exportClause = node.AsExportDeclaration()!.ExportClause;
                    if (exportClause is not null)
                    {
                        if (Ast.IsNamedExports(exportClause))
                        {
                            foreach (var element in exportClause.Elements() ?? [])
                            {
                                Visit(element);
                            }
                        }
                        else
                        {
                            Visit(exportClause.AsNamespaceExport()!.Name()!);
                        }
                    }

                    break;
                }

                case Kind.ImportDeclaration:
                {
                    var importClause = node.AsImportDeclaration()!.ImportClause;
                    if (importClause is not null)
                    {
                        // 处理默认导入，例如：import d from "mod";
                        if (importClause.Name() is not null)
                        {
                            AddDeclaration(importClause.Name()!);
                        }

                        // 处理导入里的具名绑定，例如：
                        //     import * as NS from "mod";
                        //     import {a, b as B} from "mod";
                        var namedBindings = importClause.AsImportClause()!.NamedBindings;
                        if (namedBindings is not null)
                        {
                            if (namedBindings.Kind == Kind.NamespaceImport)
                            {
                                AddDeclaration(namedBindings);
                            }
                            else
                            {
                                foreach (var element in namedBindings.Elements() ?? [])
                                {
                                    Visit(element);
                                }
                            }
                        }
                    }

                    break;
                }

                case Kind.BinaryExpression:
                    switch (Ast.GetAssignmentDeclarationKind(node))
                    {
                        case JSDeclarationKind.ExportsProperty:
                        case JSDeclarationKind.ThisProperty:
                        case JSDeclarationKind.Property:
                            AddDeclaration(node);
                            break;
                    }

                    node.ForEachChild(Visit);
                    break;
                default:
                    node.ForEachChild(Visit);
                    break;
            }

            return false;
        }

        ForEachChild(Visit);
        return result;
    }
}

public static partial class Ast
{
    /// <summary>Go: <c>func GetDeclarationName(declaration *Node) string</c>（ast.go:3086-3103）。</summary>
    public static string GetDeclarationName(Node declaration)
    {
        var name = GetNonAssignedNameOfDeclaration(declaration);
        if (name is not null)
        {
            if (IsComputedPropertyName(name))
            {
                var expression = name.Expression();
                if (IsStringOrNumericLiteralLike(expression))
                {
                    return expression!.Text();
                }

                if (expression is not null && IsPropertyAccessExpression(expression))
                {
                    return expression!.Name()!.Text();
                }
            }
            else if (IsPropertyName(name))
            {
                return name.Text();
            }
        }

        return "";
    }
}

/// <summary>
/// Go 里 <c>TokenCacheKey</c> 是结构体（map 键，值语义）；C# 的 TokenCacheKey 是 class（AstTypes.cs），默认是引用
/// 相等。这个 comparer 把 Go 的结构体键语义补回来：Parent 指针相等 + Loc 值相等（Node 不覆写 GetHashCode，
/// 所以默认哈希就是引用哈希，与 Go 用指针做键一致）。
/// </summary>
internal sealed class TokenCacheKeyComparer : IEqualityComparer<TokenCacheKey>
{
    internal static TokenCacheKeyComparer Instance { get; } = new();

    public bool Equals(TokenCacheKey? x, TokenCacheKey? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        return x is not null && y is not null && ReferenceEquals(x.Parent, y.Parent) && x.Loc == y.Loc;
    }

    public int GetHashCode(TokenCacheKey obj) =>
        HashCode.Combine(obj.Parent is not null ? RuntimeHelpers.GetHashCode(obj.Parent) : 0, obj.Loc.GetHashCode());
}

/// <summary>
/// Go 的 `func (f *NodeFactory) NewSourceFile` / `UpdateSourceFile`（ast.go:2526-2538、2825-2833）是工厂方法，
/// 所以落在 partial class NodeFactory（与 NodeFactory.cs / Nodes.Generated.cs 拼同一个类），而不是 SourceFile 上。
/// </summary>
public partial class NodeFactory
{
    /// <summary>Go: <c>func (f *NodeFactory) NewSourceFile(opts SourceFileParseOptions, text string, statements *NodeList, endOfFileToken *TokenNode) *Node</c>。</summary>
    public Node NewSourceFile(SourceFileParseOptions opts, string text, NodeList? statements, Node? endOfFileToken)
    {
        if (GetEncodedRootLength(opts.FileName) == 0 || opts.FileName != NormalizePath(opts.FileName))
        {
            // Go 的 %q 会转义特殊字符；这里只加引号（文件名里的转义差异不影响任何调用点的判断）。
            throw new DebugFailureException($"fileName should be normalized and absolute: \"{opts.FileName}\"");
        }

        var data = new SourceFile
        {
            _fileName = opts.FileName,
            _parseOptions = opts,
            _text = text,
            Statements = statements,
            EndOfFileToken = endOfFileToken,
        };
        return NewNode(Kind.SourceFile, data);
    }

    /// <summary>Go: <c>func (f *NodeFactory) UpdateSourceFile(node *SourceFile, statements *StatementList, endOfFileToken *TokenNode) *Node</c>。</summary>
    public Node UpdateSourceFile(SourceFile node, NodeList? statements, Node? endOfFileToken)
    {
        if (!ReferenceEquals(statements, node.Statements) || !ReferenceEquals(endOfFileToken, node.EndOfFileToken))
        {
            var updated = NewSourceFile(node._parseOptions, node._text, statements, endOfFileToken).AsSourceFile()!;
            updated.CopyFrom(node);
            return Ast.UpdateNode(updated, node, _hooks);
        }

        return node;
    }
}
