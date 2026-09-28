// ported from: internal/ls/findallreferences.go（**G1 前缀**：只含 crossproject.go 直接需要的那部分）
//
// ⚠ **本文件是 findallreferences.go（2,767 行）的 G1 前缀**：
//   * G1（`Ls/CrossProject.cs`）的 `HandleCrossProject` 需要 `SymbolAndEntriesData` /
//     `SymbolEntryTransformOptions` / `NonLocalDefinition`，以及 `LanguageService.GetNonLocalDefinition` /
//     `ForEachOriginalDefinitionLocation`（它们又要 `SymbolAndEntries` / `Definition` / `ReferenceEntry` /
//     `DefinitionKind` / `EntryKind` / `canUseDefinitionSymbol` / `getFileAndStartPosFromDeclaration` /
//     `isDefinitionVisible`）；
//   * `Ls/Utilities.cs`（utilities.go）的 `GetReferenceAtPosition` 需要 `RefInfo`。
//   这些成员在 Go 里都是 `findallreferences.go` 的包级可见成员，**G6 必须在本文件追加**该 Go 文件的其余成员
//   （`ProvideReferences` / `provideSymbolsAndEntries` / `getSymbolAndEntries` / `getReferencedSymbolsForNode` …）：
//   ⚠ 不要新建同名文件、不要重复声明本文件已有的成员（CS0101/CS0111）。
//
// 与 Go 的偏离（逐条说明）：
// 1) Go 包级类型/函数 → C# **同文件内的类型**：Go 未导出的类型（`refInfo`/`position`/`nonLocalDefinition`/
//    `symbolEntryTransformOptions`/`ReferenceEntry`/`Definition`/`tripleSlashDefinition`/`EntryKind`/`definitionKind*`）
//    → `internal`；Go 导出的（`SymbolAndEntries`/`DefinitionKind`/`SymbolAndEntriesData`）→ `internal` + Go 名字
//    （它们只在 `ls` 包内用，Go 的"导出"只是同包跨文件可见 ⇒ C# 的 `internal` 就是对应可见性）。
// 2) Go 未导出的**字段**一律 `_camelCase`（internal 亦然，.editorconfig 的注释与全仓实况）；Go 导出的字段保留
//    PascalCase 原名（`Definition.Kind`、`SymbolAndEntriesData.OriginalNode`、`LspPosition` 的 `Uri`/`Position`
//    在 Go 里是导出字段 ⇒ C# 保留 `Uri`/`Position` 不加前缀）。
// 3) Go 的 `type position struct`（未导出、实现 `lsproto.HasTextDocumentPosition`）→ C# `LspPosition`：
//    改名是因为 `Position` 在本文件/本命名空间里已被 `LsProto.Position` 占用（同一个方法体里两者并用），
//    C# 的成员查找会静默选中外层命名空间的 `Position`，改名可以避免这种"看得见但看不懂"的遮蔽。
// 4) Go 的 `sync.OnceValue(f)`（返回函数、首次调用求值并缓存）→ C# `Lazy<T>` + 调用点 `.Value`
//    （`NonLocalDefinition.GetSourcePosition` / `GetGeneratedPosition`）：`Lazy` 默认 `ExecutionAndPublication`
//    与 `sync.Once` 的"至多一次、并发只算一次"语义一致。
// 5) Go 的 `position` 内嵌进 `nonLocalDefinition`（提升方法）→ C# 继承（`NonLocalDefinition : LspPosition`，
//    与 docs/architecture.md 的"Go 内嵌 → C# 继承"一致）。
// 6) `checker, done := program.GetTypeChecker(ctx); defer done()` → C# `var (checker, done) = program.GetTypeChecker(ctx);`
//    + `try/finally { done(); }`（本仓既有写法）。
// 7) `len(symbol.Declarations) > 0` / `for _, d := range symbol.Declarations` → C# `?? []` 兜底（Go 遍历 nil 切片不迭代）。
// 8) 深递归：本文件只沿声明列表迭代，不递归（`isDefinitionVisible` 只沿父链）⇒ 无需大栈线程。
//
// ============================ G6 追加（findallreferences.go 的其余成员，:124-2767）============================
//
// 与 Go 的偏离（承接上面的 1)-8)，逐条）：
// 9) `refInfo` → `RefInfo`、`position` → `LspPosition`、`nonLocalDefinition` → `NonLocalDefinition` 等 G1 前缀类型照用；
//    G6 新增：`RefSearch`(refSearch) / `InheritKey`(inheritKey，Go 结构体作 map 键 ⇒ C# 记录结构体) / `RefState`(refState)
//    / `ReferencedSymbolDefinitionInfo` / `SignatureUsage`。
// 10) **不重复声明**的跨切片成员（各自文件头已备案）：
//    * G1：`Ls.GetRangeOfNode`（实际落在 `Ls/Utilities.cs:2013`，尽管 G1 清单把它列在 G6 名下）；
//    * G2：`Ls.GetContextNode` / `Ls.LspRangeContains`；
//    * G4：`RefOptions` / `ReferenceUse`（落在 `Ls/DocumentHighlights.cs`）、`ResolveEntrySource` / `ResolveEntry` /
//      `GetLocationOfEntryForFeature` / `GetRangeOfEntryForFeature` / `GetRangeOfEntry` / `GetFileNameOfEntry`；
//    * G3：`SymbolDisplayInfo` / `Ls.GetQuickInfoAndDeclarationAtLocation` / `DisplayPartsWriter`（本文件的 vs-references 链用它）；
//    * G4：`Ls.GetSymbolKindFromNode`；G7 的 `nodeIsEligibleForRename` → **G6 就地落为 `Ls.NodeIsEligibleForRename`**（见该处注释）。
// 11) `newNodeEntry` 里 Go 的 `core.OrElse(node.Name(), node)` → `OrElse(...)`；`entry.node` 等未导出字段沿用 G1 的 `_camelCase`。
// 12) Go 的 `node.Symbol()` 在 C# 是**属性** `node.Symbol`（G1 §4.2）；`Type.Symbol()` 仍是方法（`Checker/Types.Type.cs:37`）。
// 13) `symbol.Exports[...]` / `members[...]` 是 Go map 查键（缺键返回 nil）⇒ C# `SymbolTable.GetValueOrDefault(key)`。
// 14) `findIndex`/`map`/`filter` 家族 → `Core.FindIndex/Map/Filter`；`slices.SortStableFunc` → LINQ `OrderBy`（稳定排序）。
// 15) `params.Context.IncludeDeclaration` → `@params.Context?.IncludeDeclaration ?? false`（Go 会 nil panic；C# 不 NRE）。
// 16) `GetSignatureUsages` / `GetReferencedSymbolsForNode(...sourceFiles)` 因返回 `internal` 类型 `SignatureUsage` /
//    `SymbolAndEntries` ⇒ 方法本身也是 `internal`（Go 的导出只跨文件，不跨包；ls/lsp 同装配件）。
// 17) `provideSymbolsAndEntries` 的 `combined.SymbolsAndEntries` 只在有内容时替换 —— 保持 Go `append(nil)` 的"仍是 nil"语义。
// 18) `Position`/`Location`/`Range` 是 class（LsProto 生成物）⇒ 需要"值相等"的地方显式比较（G2 的 `LocationKey`、`Ls.RangeEquals`）。
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Ls.Ls;
using static TypeScript.LsConv.LsConv;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;
// 同名类型消歧（`using TypeScript.SpanMap` 带来的 `SpanMap.Kind`；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
// `using TypeScript.Checker` 会带进 `TypeScript.Checker.Program`，与 `TypeScript.Compiler.Program` 二义（见 G1 §4 消歧清单）：
using Program = TypeScript.Compiler.Program;

namespace TypeScript.Ls;

// === types for results ===

/// <summary>Go: <c>type refInfo struct { file *ast.SourceFile; fileName string; reference *ast.FileReference; unverified bool }</c>（findallreferences.go:48-53）。</summary>
internal sealed class RefInfo(FileReference? reference, string fileName, SourceFile? file, bool unverified)
{
    internal SourceFile? _file = file;
    internal string _fileName = fileName;
    internal FileReference? _reference = reference;
    internal bool _unverified = unverified;
}

/// <summary>Go: <c>type SymbolAndEntries struct { definition *Definition; references []*ReferenceEntry }</c>（findallreferences.go:55-58）。</summary>
internal sealed class SymbolAndEntries(Definition? definition, ReferenceEntry[]? references)
{
    internal Definition? _definition = definition;
    internal ReferenceEntry[]? _references = references;

    /// <summary>Go: <c>func NewSymbolAndEntries(kind DefinitionKind, node *ast.Node, symbol *ast.Symbol, references []*ReferenceEntry) *SymbolAndEntries</c>（findallreferences.go:60-69）。</summary>
    internal static SymbolAndEntries NewSymbolAndEntries(DefinitionKind kind, Node? node, Symbol? symbol, ReferenceEntry[]? references)
    {
        return new SymbolAndEntries(new Definition { Kind = kind, _node = node, _symbol = symbol }, references);
    }

    /// <summary>References returns the reference entries for this symbol.</summary>
    internal ReferenceEntry[]? References()
    {
        return _references;
    }

    /// <summary>DefinitionNode returns the defining AST node for this symbol, if any.</summary>
    internal Node? DefinitionNode()
    {
        if (_definition is null)
        {
            return null;
        }

        if (_definition._node is not null)
        {
            return _definition._node;
        }

        if (_definition._symbol is not null && (_definition._symbol.Declarations?.Length ?? 0) > 0)
        {
            return _definition._symbol.Declarations![0];
        }

        return null;
    }

    internal Symbol? DefinitionSymbol()
    {
        if (_definition is null)
        {
            return null;
        }

        return _definition._symbol;
    }

    internal bool CanUseDefinitionSymbol()
    {
        if (_definition is null)
        {
            return false;
        }

        switch (_definition.Kind)
        {
            case DefinitionKind.Symbol:
            case DefinitionKind.This:
                return _definition._symbol is not null;
            case DefinitionKind.TripleSlashReference:
                // !!! TODO : need to find file reference instead?
                // May need to return true to indicate this to be file search instead and might need to do for import stuff as well
                // For now
                return false;
            default:
                return false;
        }
    }
}

/// <summary>Go: <c>type DefinitionKind int</c>（findallreferences.go:71-80）。</summary>
internal enum DefinitionKind
{
    Symbol = 0,
    Label = 1,
    Keyword = 2,
    This = 3,
    String = 4,
    TripleSlashReference = 5,
}

/// <summary>Go: <c>type Definition struct</c>（findallreferences.go:82-87）。</summary>
internal sealed class Definition
{
    public DefinitionKind Kind;
    internal Symbol? _symbol;
    internal Node? _node;
    internal TripleSlashDefinition? _tripleSlashFileRef;
}

/// <summary>Go: <c>type tripleSlashDefinition struct</c>（findallreferences.go:88-91）。</summary>
internal sealed class TripleSlashDefinition
{
    internal FileReference? _reference;
    internal SourceFile? _file;
}

/// <summary>Go: <c>type entryKind int</c>（findallreferences.go:93-102）。</summary>
internal enum EntryKind
{
    None = 0,
    Range = 1,
    Node = 2,
    StringLiteral = 3,
    SearchedLocalFoundProperty = 4,
    SearchedPropertyFoundLocal = 5,
}

/// <summary>Go: <c>type ReferenceEntry struct</c>（findallreferences.go:104-113）。</summary>
internal sealed class ReferenceEntry
{
    internal EntryKind _kind;
    internal Node? _node;
    internal Node? _context; // !!! ContextWithStartAndEndNode, optional
    internal SourceFile? _sourceFile;
    internal TextRange? _textRange;
    internal Location? _lspRange;
    internal bool _unmappable;

    /// <summary>Node returns the AST node for this reference entry.</summary>
    internal Node? Node()
    {
        return _node;
    }

    /// <summary>IsNodeEntry returns true if this is a node-backed reference entry.</summary>
    internal bool IsNodeEntry()
    {
        return _node is not null;
    }
}

/// <summary>Go: <c>type position struct</c>（findallreferences.go:480-488，见文件头第 3 条）。</summary>
internal class LspPosition : HasTextDocumentPosition
{
    internal DocumentUri _uri;
    internal Position _pos = null!;

    public DocumentUri TextDocumentURI()
    {
        return _uri;
    }

    public Position TextDocumentPosition()
    {
        return _pos;
    }
}

/// <summary>Go: <c>type nonLocalDefinition struct { position; GetSourcePosition func() lsproto.HasTextDocumentPosition; GetGeneratedPosition func() ... }</c>（findallreferences.go:490-495，见文件头第 4 条）。</summary>
internal sealed class NonLocalDefinition : LspPosition
{
    internal Lazy<HasTextDocumentPosition?> GetSourcePosition = null!;
    internal Lazy<HasTextDocumentPosition?> GetGeneratedPosition = null!;
}

/// <summary>Go: <c>type symbolEntryTransformOptions struct { requireLocationsResult bool; dropOriginNodes bool }</c>（findallreferences.go:631-636）。</summary>
internal sealed class SymbolEntryTransformOptions
{
    /// <summary>Force the result to be Location objects.</summary>
    internal bool _requireLocationsResult;

    /// <summary>Omit node(s) containing the original position.</summary>
    internal bool _dropOriginNodes;
}

/// <summary>Go: <c>type SymbolAndEntriesData struct { OriginalNode *ast.Node; SymbolsAndEntries []*SymbolAndEntries; Position int }</c>（findallreferences.go:638-642）。</summary>
internal sealed class SymbolAndEntriesData
{
    public Node? OriginalNode;
    public SymbolAndEntries[]? SymbolsAndEntries;
    public int Position;
}

/// <summary>Go: <c>type refSearch struct</c>（findallreferences.go:1888-1904）。</summary>
internal sealed class RefSearch
{
    /// <summary>If coming from an export, we will not recursively search for the imported symbol (since that's where we came from).</summary>
    internal ImpExpKind _comingFrom; // import, export

    internal Symbol _symbol = null!;
    internal string _text = "";
    internal string _escapedText = "";

    /// <summary>Only set if `options.implementations` is true. These are the symbols checked to get the implementations of a property access.</summary>
    internal Symbol[]? _parents;

    internal Symbol[] _allSearchSymbols = [];

    /// <summary>Whether a symbol is in the search set. Do not compare directly to `symbol` because there may be related symbols to search for. See `populateSearchSymbolSet`.</summary>
    internal Func<Symbol, bool> _includes = null!;
}

/// <summary>Go: <c>type inheritKey struct</c>（findallreferences.go:1906-1909）：Go 的结构体作 map 键 ⇒ C# 记录结构体（按值相等）。</summary>
internal readonly record struct InheritKey(Symbol Symbol, Symbol Parent);

/// <summary>Go: <c>type refState struct</c>（findallreferences.go:1911-1927）+ 其全部方法（:1945-2767）。</summary>
internal sealed class RefState
{
    internal SourceFile[]? _sourceFiles;
    internal Set<string> _sourceFilesSet = null!;
    internal string _specialSearchKind = "none"; // "none", "constructor", or "class"
    internal TypeScript.Checker.Checker _checker = null!;
    internal Context _ctx = null!;
    internal Program _program = null!;
    internal SemanticMeaning _searchMeaning;
    internal RefOptions _options = null!;
    internal List<SymbolAndEntries> _result = [];
    internal Dictionary<InheritKey, bool> _inheritsFromCache = [];
    internal Set<Node> _seenContainingTypeReferences = new Set<Node>(); // node seen tracker
    internal Set<Node> _seenReExportRHS = new Set<Node>(); // node seen tracker
    internal ImportTracker? _importTracker;
    internal Dictionary<Symbol, SymbolAndEntries> _symbolToReferences = [];
    internal Dictionary<SourceFile, Set<Symbol>> _sourceFileToSeenSymbols = [];

    /// <summary>Go: <c>func (state *refState) includesSourceFile(sourceFile *ast.SourceFile) bool</c>（findallreferences.go:1945-1947）。</summary>
    internal bool IncludesSourceFile(SourceFile sourceFile)
    {
        return _sourceFilesSet.Has(sourceFile.FileName());
    }

    /// <summary>Go: <c>func (state *refState) getImportSearches(exportSymbol *ast.Symbol, exportInfo *ExportInfo) *ImportsResult</c>（findallreferences.go:1949-1955）。</summary>
    internal ImportsResult GetImportSearches(Symbol exportSymbol, ExportInfo exportInfo)
    {
        _importTracker ??= Ls.CreateImportTracker(_ctx, _program, _sourceFiles, _sourceFilesSet, _checker);
        return _importTracker(exportSymbol, exportInfo, _options._use == ReferenceUse.Rename);
    }

    /// <summary>Go: <c>func (state *refState) createSearch(location *ast.Node, symbol *ast.Symbol, comingFrom ImpExpKind, text string, allSearchSymbols []*ast.Symbol) *refSearch</c>（findallreferences.go:1957-1992）。</summary>
    internal RefSearch CreateSearch(Node? location, Symbol symbol, ImpExpKind comingFrom, string text, Symbol[]? allSearchSymbols)
    {
        // Note: if this is an external module symbol, the name doesn't include quotes.
        // Note: getLocalSymbolForExportDefault handles `export default class C {}`, but not `export default C` or `export { C as default }`.
        // The other two forms seem to be handled downstream (e.g. in `skipPastExportOrImportSpecifier`), so special-casing the first form
        // here appears to be intentional).
        if (text.Length == 0)
        {
            var s = TypeScript.Binder.Binder.GetLocalSymbolForExportDefault(symbol);
            s ??= Ls.GetNonModuleSymbolOfMergedModuleSymbol(symbol);
            s ??= symbol;
            var symbolName = SymbolName(s);
            if (TryGetAmbientModuleNameFromSymbolName(symbolName, out var moduleName))
            {
                text = moduleName;
            }
            else
            {
                text = symbolName;
            }
        }

        if (allSearchSymbols is null || allSearchSymbols.Length == 0)
        {
            allSearchSymbols = [symbol];
        }

        var search = new RefSearch
        {
            _symbol = symbol,
            _comingFrom = comingFrom,
            _text = text,
            _escapedText = text,
            _allSearchSymbols = allSearchSymbols,
            _includes = sym => allSearchSymbols.Contains(sym),
        };
        if (_options._implementations && location is not null)
        {
            search._parents = Ls.GetParentSymbolsOfPropertyAccess(location, symbol, _checker);
        }

        return search;
    }

    /// <summary>Go: <c>func (state *refState) referenceAdder(searchSymbol *ast.Symbol) func(*ast.Node, entryKind)</c>（findallreferences.go:1994-2004）。</summary>
    internal Action<Node, EntryKind> ReferenceAdder(Symbol searchSymbol)
    {
        if (!_symbolToReferences.TryGetValue(searchSymbol, out var symbolAndEntries))
        {
            symbolAndEntries = SymbolAndEntries.NewSymbolAndEntries(DefinitionKind.Symbol, null, searchSymbol, null);
            _symbolToReferences[searchSymbol] = symbolAndEntries;
            _result.Add(symbolAndEntries);
        }

        return (node, kind) =>
        {
            symbolAndEntries._references = [.. symbolAndEntries._references ?? [], Ls.NewNodeEntryWithKind(node, kind)];
        };
    }

    /// <summary>Go: <c>func (state *refState) addReference(referenceLocation *ast.Node, symbol *ast.Symbol, kind entryKind)</c>（findallreferences.go:2006-2018）。</summary>
    internal void AddReference(Node referenceLocation, Symbol symbol, EntryKind kind)
    {
        // if rename symbol from default export anonymous function, for example `export default function() {}`, we do not need to add reference
        if (_options._use == ReferenceUse.Rename && referenceLocation.Kind == Kind.DefaultKeyword)
        {
            return;
        }

        var addRef = ReferenceAdder(symbol);
        if (_options._implementations)
        {
            AddImplementationReferences(referenceLocation, n => addRef(n, kind));
        }
        else
        {
            addRef(referenceLocation, kind);
        }
    }

    /// <summary>Go: <c>func (state *refState) addImplementationReferences(refNode *ast.Node, addRef func(*ast.Node))</c>（findallreferences.go:2113-2171）。</summary>
    internal void AddImplementationReferences(Node refNode, Action<Node> addRef)
    {
        // Check if we found a function/propertyAssignment/method with an implementation or initializer
        if (IsDeclarationName(refNode) && IsImplementation(refNode.Parent!))
        {
            addRef(refNode);
            return;
        }

        if (refNode.Kind != Kind.Identifier)
        {
            return;
        }

        if (refNode.Parent!.Kind == Kind.ShorthandPropertyAssignment)
        {
            // Go ahead and dereference the shorthand assignment by going to its definition
            GetReferenceEntriesForShorthandPropertyAssignment(refNode, _checker, addRef);
        }

        // Check if the node is within an extends or implements clause

        if (Ls.GetContainingNodeIfInHeritageClause(refNode) is { } containingNode)
        {
            addRef(containingNode);
            return;
        }

        // If we got a type reference, try and see if the reference applies to any expressions that can implement an interface
        // Find the first node whose parent isn't a type node -- i.e., the highest type node.
        var typeNode = FindAncestor(refNode, a => !IsQualifiedName(a.Parent!) && !IsTypeNode(a.Parent) && !IsTypeElement(a.Parent));

        if (typeNode is null || typeNode.Parent!.Type() is null)
        {
            return;
        }

        var typeHavingNode = typeNode.Parent;
        if (typeHavingNode.Type() == typeNode && _seenContainingTypeReferences.AddIfAbsent(typeHavingNode))
        {
            void AddIfImplementation(Node e)
            {
                if (IsImplementationExpression(e))
                {
                    addRef(e);
                }
            }

            if (HasInitializer(typeHavingNode))
            {
                AddIfImplementation(typeHavingNode.Initializer()!);
            }
            else if (IsFunctionLike(typeHavingNode) && typeHavingNode.Body() is not null)
            {
                var body = typeHavingNode.Body();
                if (body!.Kind == Kind.Block)
                {
                    ForEachReturnStatement(body, returnStatement =>
                    {
                        if (returnStatement.Expression() is { } expr)
                        {
                            AddIfImplementation(expr);
                        }

                        return false;
                    });
                }
                else
                {
                    AddIfImplementation(body);
                }
            }
            else if (IsAssertionExpression(typeHavingNode) || IsSatisfiesExpression(typeHavingNode))
            {
                AddIfImplementation(typeHavingNode.Expression()!);
            }
        }
    }

    /// <summary>Go: <c>func (state *refState) getReferencesInContainerOrFiles(symbol *ast.Symbol, search *refSearch)</c>（findallreferences.go:2173-2186）。</summary>
    internal void GetReferencesInContainerOrFiles(Symbol symbol, RefSearch search)
    {
        // Try to get the smallest valid scope that we can limit our search to;
        // otherwise we'll need to search globally (i.e. include each file).
        if (Ls.GetSymbolScope(symbol) is { } scope)
        {
            var addReferencesHere = scope.Kind != Kind.SourceFile || ((_sourceFiles?.Contains(scope.AsSourceFile()!)) ?? false);
            GetReferencesInContainer(scope, GetSourceFileOfNode(scope)!, search, addReferencesHere);
        }
        else
        {
            // Global search
            foreach (var sourceFile in _sourceFiles ?? [])
            {
                // state.cancellationToken.throwIfCancellationRequested();
                SearchForName(sourceFile, search);
            }
        }
    }

    /// <summary>Go: <c>func (state *refState) getReferencesInSourceFile(sourceFile *ast.SourceFile, search *refSearch, addReferencesHere bool)</c>（findallreferences.go:2188-2191）。</summary>
    internal void GetReferencesInSourceFile(SourceFile sourceFile, RefSearch search, bool addReferencesHere)
    {
        // state.cancellationToken.throwIfCancellationRequested();
        GetReferencesInContainer(sourceFile.AsNode(), sourceFile, search, addReferencesHere);
    }

    /// <summary>Go: <c>func (state *refState) getReferencesInContainer(container *ast.Node, sourceFile *ast.SourceFile, search *refSearch, addReferencesHere bool)</c>（findallreferences.go:2193-2204）。</summary>
    internal void GetReferencesInContainer(Node container, SourceFile sourceFile, RefSearch search, bool addReferencesHere)
    {
        // Search within node "container" for references for a search value, where the search value is defined as a
        //     tuple of (searchSymbol, searchText, searchLocation, and searchMeaning).
        // searchLocation: a node where the search value
        if (!MarkSearchedSymbols(sourceFile, search._allSearchSymbols))
        {
            return;
        }

        foreach (var position in Ls.GetPossibleSymbolReferencePositions(sourceFile, search._text, container))
        {
            GetReferencesAtLocation(sourceFile, position, search, addReferencesHere);
        }
    }

    /// <summary>Go: <c>func (state *refState) markSearchedSymbols(sourceFile *ast.SourceFile, symbols []*ast.Symbol) bool</c>（findallreferences.go:2206-2219）。</summary>
    internal bool MarkSearchedSymbols(SourceFile sourceFile, Symbol[] symbols)
    {
        if (!_sourceFileToSeenSymbols.TryGetValue(sourceFile, out var seenSymbols))
        {
            seenSymbols = new Set<Symbol>();
            _sourceFileToSeenSymbols[sourceFile] = seenSymbols;
        }

        var anyNewSymbols = false;
        foreach (var sym in symbols)
        {
            if (seenSymbols.AddIfAbsent(sym))
            {
                anyNewSymbols = true;
            }
        }

        return anyNewSymbols;
    }

    /// <summary>Go: <c>func (state *refState) getReferencesAtLocation(sourceFile *ast.SourceFile, position int, search *refSearch, addReferencesHere bool)</c>（findallreferences.go:2221-2290）。</summary>
    internal void GetReferencesAtLocation(SourceFile sourceFile, int position, RefSearch search, bool addReferencesHere)
    {
        var referenceLocation = TypeScript.AstNav.AstNav.GetTouchingPropertyName(sourceFile, position);

        if (!Ls.IsValidReferencePosition(referenceLocation, search._text))
        {
            // This wasn't the start of a token.  Check to see if it might be a
            // match in a comment or string if that's what the caller is asking
            // for.

            // !!! not implemented
            // if (!state.options.implementations && (state.options.findInStrings && isInString(sourceFile, position) || state.options.findInComments && isInNonReferenceComment(sourceFile, position))) {
            // 	// In the case where we're looking inside comments/strings, we don't have an actual definition.  So just use 'undefined' here.  Features like
            // 	// 'Rename' won't care (as they ignore the definitions), and features like 'FindReferences' will just filter out these results.
            // 	state.addStringOrCommentReference(sourceFile.FileName, createTextSpan(position, search.text.length));
            // }

            return;
        }

        if ((Ls.GetMeaningFromLocation(referenceLocation) & _searchMeaning) == 0)
        {
            return;
        }

        var referenceSymbol = _checker.GetSymbolAtLocation(referenceLocation);
        if (referenceSymbol is null)
        {
            return;
        }

        var parent = referenceLocation.Parent!;
        if (parent.Kind == Kind.ImportSpecifier && parent.PropertyName() == referenceLocation)
        {
            // This is added through `singleReferences` in ImportsResult. If we happen to see it again, don't add it again.
            return;
        }

        if (parent.Kind == Kind.ExportSpecifier)
        {
            GetReferencesAtExportSpecifier(referenceLocation, referenceSymbol, parent.AsExportSpecifier()!, search, addReferencesHere, false /*alwaysGetReferences*/);
            return;
        }

        var (relatedSymbol, relatedSymbolKind) = GetRelatedSymbol(search, referenceSymbol, referenceLocation);
        if (relatedSymbol is null)
        {
            GetReferenceForShorthandProperty(referenceSymbol, search);
            return;
        }

        switch (_specialSearchKind)
        {
            case "none":
                if (addReferencesHere)
                {
                    AddReference(referenceLocation, relatedSymbol, relatedSymbolKind);
                }

                break;
            case "constructor":
                AddConstructorReferences(referenceLocation, relatedSymbol, search, addReferencesHere);
                break;
            case "class":
                AddClassStaticThisReferences(referenceLocation, relatedSymbol, search, addReferencesHere);
                break;
        }

        // Use the parent symbol if the location is commonjs require syntax on javascript files only.
        if (IsInJSFile(referenceLocation) && referenceLocation.Parent!.Kind == Kind.BindingElement &&
            IsVariableDeclarationInitializedToBareOrAccessedRequire(referenceLocation.Parent!.Parent!.Parent!))
        {
            referenceSymbol = referenceLocation.Parent!.Symbol!;
            // The parent will not have a symbol if it's an ObjectBindingPattern (when destructuring is used).  In
            // this case, just skip it, since the bound identifiers are not an alias of the import.
            if (referenceSymbol is null)
            {
                return;
            }
        }

        GetImportOrExportReferences(referenceLocation, referenceSymbol, search);
    }

    /// <summary>Go: <c>func (state *refState) addConstructorReferences(referenceLocation, symbol *ast.Symbol, search *refSearch, addReferencesHere bool)</c>（findallreferences.go:2292-2316）。</summary>
    internal void AddConstructorReferences(Node referenceLocation, Symbol symbol, RefSearch search, bool addReferencesHere)
    {
        if (IsNewExpressionTarget(referenceLocation, false, false) && addReferencesHere)
        {
            AddReference(referenceLocation, symbol, EntryKind.Node);
        }

        Action<Node, EntryKind> Pusher() => ReferenceAdder(search._symbol);

        if (IsClassLike(referenceLocation.Parent!))
        {
            // This is the class declaration containing the constructor.
            var sourceFile = GetSourceFileOfNode(referenceLocation)!;
            FindOwnConstructorReferences(search._symbol, sourceFile, n => Pusher()(n, EntryKind.Node));
        }
        else
        {
            // If this class appears in `extends C`, then the extending class' "super" calls are references.
            if (TryGetClassByExtendingIdentifier(referenceLocation) is { } classExtending)
            {
                FindSuperConstructorAccesses(classExtending, n => Pusher()(n, EntryKind.Node));
                FindInheritedConstructorReferences(classExtending);
            }
        }
    }

    /// <summary>Go: <c>func (state *refState) addClassStaticThisReferences(referenceLocation, symbol *ast.Symbol, search *refSearch, addReferencesHere bool)</c>（findallreferences.go:2318-2353）。</summary>
    internal void AddClassStaticThisReferences(Node referenceLocation, Symbol symbol, RefSearch search, bool addReferencesHere)
    {
        if (addReferencesHere)
        {
            AddReference(referenceLocation, symbol, EntryKind.Node);
        }

        var classLike = referenceLocation.Parent!;
        if (_options._use == ReferenceUse.Rename || !IsClassLike(classLike))
        {
            return;
        }

        var addRef = ReferenceAdder(search._symbol);
        var members = classLike.Members();
        if (members is null)
        {
            return;
        }

        foreach (var member in members)
        {
            if (!(Ls.IsMethodOrAccessor(member) && HasStaticModifier(member)))
            {
                continue;
            }

            if (member.Body() is { } body)
            {
                void Cb(Node node)
                {
                    if (node.Kind == Kind.ThisKeyword)
                    {
                        addRef(node, EntryKind.Node);
                    }
                    else if (!IsFunctionLike(node) && !IsClassLike(node))
                    {
                        node.ForEachChild(child =>
                        {
                            Cb(child);
                            return false;
                        });
                    }
                }

                Cb(body);
            }
        }
    }

    /// <summary>Go: <c>func (state *refState) findInheritedConstructorReferences(classDeclaration *ast.ClassLikeDeclaration)</c>（findallreferences.go:2355-2362）。</summary>
    internal void FindInheritedConstructorReferences(Node classDeclaration)
    {
        if (HasOwnConstructor(classDeclaration))
        {
            return;
        }

        var classSymbol = classDeclaration.Symbol!;
        var search = CreateSearch(null, classSymbol, ImpExpKind.Unknown, "", null);
        GetReferencesInContainerOrFiles(classSymbol, search);
    }

    /// <summary>Go: <c>func (state *refState) getImportOrExportReferences(referenceLocation, referenceSymbol *ast.Symbol, search *refSearch)</c>（findallreferences.go:2364-2376）。</summary>
    internal void GetImportOrExportReferences(Node referenceLocation, Symbol referenceSymbol, RefSearch search)
    {
        var importOrExport = Ls.GetImportOrExportSymbol(referenceLocation, referenceSymbol, _checker, search._comingFrom == ImpExpKind.Export);
        if (importOrExport is null)
        {
            return;
        }

        if (importOrExport._kind == ImpExpKind.Import)
        {
            if (!Ls.IsForRenameWithPrefixAndSuffixText(_options))
            {
                SearchForImportedSymbol(importOrExport._symbol!);
            }
        }
        else
        {
            SearchForImportsOfExport(referenceLocation, importOrExport._symbol!, importOrExport._exportInfo!);
        }
    }

    /// <summary>Go: <c>func (state *refState) markSeenReExportRHS(node *ast.Node) bool</c>（findallreferences.go:2378-2380）。</summary>
    internal bool MarkSeenReExportRHS(Node node)
    {
        return _seenReExportRHS.AddIfAbsent(node);
    }

    /// <summary>Go: <c>func (state *refState) getReferencesAtExportSpecifier(...)</c>（findallreferences.go:2382-2453）。</summary>
    internal void GetReferencesAtExportSpecifier(
        Node referenceLocation,
        Symbol referenceSymbol,
        ExportSpecifier exportSpecifier,
        RefSearch search,
        bool addReferencesHere,
        bool alwaysGetReferences)
    {
        Debug.Assert(!alwaysGetReferences || _options._useAliasesForRename, "If alwaysGetReferences is true, then prefix/suffix text must be enabled");

        var exportDeclaration = exportSpecifier.Parent!.Parent!.AsExportDeclaration()!;
        var propertyName = exportSpecifier.PropertyName;
        var name = exportSpecifier.Name()!;
        var localSymbol = GetLocalSymbolForExportSpecifier(referenceLocation, referenceSymbol, exportSpecifier, _checker);

        if (!alwaysGetReferences && !search._includes(localSymbol!))
        {
            return;
        }

        void AddRef()
        {
            if (addReferencesHere)
            {
                AddReference(referenceLocation, localSymbol!, EntryKind.Node);
            }
        }

        if (propertyName is null)
        {
            // Don't rename at `export { default } from "m";`. (but do continue to search for imports of the re-export)
            if (!(_options._use == ReferenceUse.Rename && ModuleExportNameIsDefault(name)))
            {
                AddRef();
            }
        }
        else if (referenceLocation == propertyName)
        {
            // For `export { foo as bar } from "baz"`, "`foo`" will be added from the singleReferences for import searches of the original export.
            // For `export { foo as bar };`, where `foo` is a local, so add it now.
            if (exportDeclaration.ModuleSpecifier is null)
            {
                AddRef();
            }

            if (addReferencesHere && _options._use != ReferenceUse.Rename && MarkSeenReExportRHS(name))
            {
                var exportSymbol = exportSpecifier.Symbol!;
                Debug.Assert(exportSymbol is not null, "exportSpecifier.Symbol() should not be nil");
                AddReference(name, exportSymbol!, EntryKind.Node);
            }
        }
        else
        {
            if (MarkSeenReExportRHS(referenceLocation))
            {
                AddRef();
            }
        }

        // For `export { foo as bar }`, rename `foo`, but not `bar`.
        if (!Ls.IsForRenameWithPrefixAndSuffixText(_options) || alwaysGetReferences)
        {
            var isDefaultExport = ModuleExportNameIsDefault(referenceLocation) || ModuleExportNameIsDefault(exportSpecifier.Name()!);
            var exportKind = ExportKind.Named;
            if (isDefaultExport)
            {
                exportKind = ExportKind.Default;
            }

            var exportSymbol = exportSpecifier.Symbol!;
            Debug.Assert(exportSymbol is not null, "exportSpecifier.Symbol() should not be nil");
            var exportInfo = Ls.GetExportInfo(exportSymbol!, exportKind, _checker);
            if (exportInfo is not null)
            {
                SearchForImportsOfExport(referenceLocation, exportSymbol!, exportInfo);
            }
        }

        // At `export { x } from "foo"`, also search for the imported symbol `"foo".x`.
        if (search._comingFrom != ImpExpKind.Export && exportDeclaration.ModuleSpecifier is not null && propertyName is null && !Ls.IsForRenameWithPrefixAndSuffixText(_options))
        {
            var imported = _checker.GetExportSpecifierLocalTargetSymbol(exportSpecifier.AsNode());
            if (imported is not null)
            {
                SearchForImportedSymbol(imported);
            }
        }
    }

    /// <summary>Go: <c>func (state *refState) searchForImportedSymbol(symbol *ast.Symbol)</c>（findallreferences.go:2455-2461）。</summary>
    // Go to the symbol we imported from and find references for it.
    internal void SearchForImportedSymbol(Symbol symbol)
    {
        foreach (var declaration in symbol.Declarations ?? [])
        {
            var exportingFile = GetSourceFileOfNode(declaration)!;
            // Need to search in the file even if it's not in the search-file set, because it might export the symbol.
            GetReferencesInSourceFile(exportingFile, CreateSearch(declaration, symbol, ImpExpKind.Import, "", null), IncludesSourceFile(exportingFile));
        }
    }

    /// <summary>Go: <c>func (state *refState) searchForImportsOfExport(exportLocation, exportSymbol *ast.Symbol, exportInfo *ExportInfo)</c>（findallreferences.go:2463-2499）。</summary>
    // Search for all imports of a given exported symbol using `State.getImportSearches`.
    internal void SearchForImportsOfExport(Node exportLocation, Symbol exportSymbol, ExportInfo exportInfo)
    {
        var r = GetImportSearches(exportSymbol, exportInfo);

        // For `import { foo as bar }` just add the reference to `foo`, and don't otherwise search in the file.
        if ((r._singleReferences?.Length ?? 0) != 0)
        {
            var addRef = ReferenceAdder(exportSymbol);
            foreach (var singleRef in r._singleReferences!)
            {
                if (ShouldAddSingleReference(singleRef))
                {
                    addRef(singleRef, EntryKind.Node);
                }
            }
        }

        // For each import, find all references to that import in its source file.
        foreach (var i in r._importSearches ?? [])
        {
            GetReferencesInSourceFile(GetSourceFileOfNode(i._importLocation!)!, CreateSearch(i._importLocation, i._importSymbol!, ImpExpKind.Export, "", null), true /*addReferencesHere*/);
        }

        if ((r._indirectUsers?.Length ?? 0) != 0)
        {
            RefSearch? indirectSearch = null;
            switch (exportInfo._exportKind)
            {
                case ExportKind.Named:
                    indirectSearch = CreateSearch(exportLocation, exportSymbol, ImpExpKind.Export, "", null);
                    break;
                case ExportKind.Default:
                    // Search for a property access to '.default'. This can't be renamed.
                    if (_options._use != ReferenceUse.Rename)
                    {
                        indirectSearch = CreateSearch(exportLocation, exportSymbol, ImpExpKind.Export, "default", null);
                    }

                    break;
            }

            if (indirectSearch is not null)
            {
                foreach (var indirectUser in r._indirectUsers!)
                {
                    SearchForName(indirectUser, indirectSearch);
                }
            }
        }
    }

    /// <summary>Go: <c>func (state *refState) shouldAddSingleReference(singleRef *ast.Node) bool</c>（findallreferences.go:2501-2514）。</summary>
    internal bool ShouldAddSingleReference(Node singleRef)
    {
        if (!HasMatchingMeaning(singleRef))
        {
            return false;
        }

        if (_options._use != ReferenceUse.Rename)
        {
            return true;
        }

        // Don't rename an import type `import("./module-name")` when renaming `name` in `export = name;`
        if (!IsIdentifier(singleRef) && !IsImportOrExportSpecifier(singleRef.Parent!))
        {
            return false;
        }

        // At `default` in `import { default as x }` or `export { default as x }`, do add a reference, but do not rename.
        return !(IsImportOrExportSpecifier(singleRef.Parent!) && ModuleExportNameIsDefault(singleRef));
    }

    /// <summary>Go: <c>func (state *refState) hasMatchingMeaning(referenceLocation *ast.Node) bool</c>（findallreferences.go:2516-2518）。</summary>
    internal bool HasMatchingMeaning(Node referenceLocation)
    {
        return (Ls.GetMeaningFromLocation(referenceLocation) & _searchMeaning) != 0;
    }

    /// <summary>Go: <c>func (state *refState) getReferenceForShorthandProperty(referenceSymbol *ast.Symbol, search *refSearch)</c>（findallreferences.go:2520-2536）。</summary>
    internal void GetReferenceForShorthandProperty(Symbol referenceSymbol, RefSearch search)
    {
        if ((referenceSymbol.Flags & SymbolFlags.Transient) != 0 || referenceSymbol.ValueDeclaration is null)
        {
            return;
        }

        var shorthandValueSymbol = _checker.GetShorthandAssignmentValueSymbol(referenceSymbol.ValueDeclaration);
        var name = GetNameOfDeclaration(referenceSymbol.ValueDeclaration);

        // Because in short-hand property assignment, an identifier which stored as name of the short-hand property assignment
        // has two meanings: property name and property value. Therefore when we do findAllReference at the position where
        // an identifier is declared, the language service should return the position of the variable declaration as well as
        // the position in short-hand property assignment excluding property accessing. However, if we do findAllReference at the
        // position of property accessing, the referenceEntry of such position will be handled in the first case.
        if (name is not null && shorthandValueSymbol is not null && search._includes(shorthandValueSymbol))
        {
            AddReference(name, shorthandValueSymbol, EntryKind.Node);
        }
    }

    /// <summary>Go: <c>func (state *refState) populateSearchSymbolSet(symbol, location, isForRename, providePrefixAndSuffixText, implementations) []*ast.Symbol</c>（findallreferences.go:2538-2561）。</summary>
    internal Symbol[] PopulateSearchSymbolSet(Symbol symbol, Node? location, bool isForRename, bool providePrefixAndSuffixText, bool implementations)
    {
        if (location is null)
        {
            return [symbol];
        }

        var result = new List<Symbol>();
        ForEachRelatedSymbol(
            symbol,
            location,
            isForRename,
            !(isForRename && providePrefixAndSuffixText),
            (sym, root, @base) =>
            {
                // static method/property and instance method/property might have the same name. Only include static or only include instance.
                if (@base is not null)
                {
                    if (Ls.IsStaticSymbol(symbol) != Ls.IsStaticSymbol(@base))
                    {
                        @base = null;
                    }
                }

                result.Add(Coalesce(@base, Coalesce(root, sym))!);
                return null;
            }, // when try to find implementation, implementations is true, and not allowed to find base class
            /*allowBaseTypes*/ _ => !implementations);
        return result.ToArray();
    }

    /// <summary>Go: <c>func (state *refState) getRelatedSymbol(search *refSearch, referenceSymbol *ast.Symbol, referenceLocation *ast.Node) (*ast.Symbol, entryKind)</c>（findallreferences.go:2563-2593）。</summary>
    internal (Symbol? Symbol, EntryKind Kind) GetRelatedSymbol(RefSearch search, Symbol referenceSymbol, Node referenceLocation)
    {
        return ForEachRelatedSymbol(
            referenceSymbol,
            referenceLocation,
            false, /*isForRenamePopulateSearchSymbolSet*/
            _options._use != ReferenceUse.Rename || _options._useAliasesForRename, /*onlyIncludeBindingElementAtReferenceLocation*/
            (sym, rootSymbol, baseSymbol) =>
            {
                // check whether the symbol used to search itself is just the searched one.
                if (baseSymbol is not null)
                {
                    // static method/property and instance method/property might have the same name. Only check static or only check instance.
                    if (Ls.IsStaticSymbol(referenceSymbol) != Ls.IsStaticSymbol(baseSymbol))
                    {
                        baseSymbol = null;
                    }
                }

                var searchSym = Coalesce(baseSymbol, Coalesce(rootSymbol, sym));
                if (searchSym is not null && search._includes(searchSym))
                {
                    if (rootSymbol is not null && (sym!.CheckFlags & CheckFlags.Synthetic) == 0)
                    {
                        return rootSymbol;
                    }

                    return sym;
                }

                // For a base type, use the symbol for the derived type. For a synthetic (e.g. union) property, use the union symbol.
                return null;
            },
            rootSymbol => !(((search._parents?.Length ?? 0) != 0) && !Some(search._parents, parent => ExplicitlyInheritsFrom(rootSymbol.Parent!, parent!))));
    }

    /// <summary>Go: <c>func (state *refState) forEachRelatedSymbol(...)</c>（findallreferences.go:2595-2730）。</summary>
    internal (Symbol? Symbol, EntryKind Kind) ForEachRelatedSymbol(
        Symbol symbol,
        Node? location,
        bool isForRenamePopulateSearchSymbolSet,
        bool onlyIncludeBindingElementAtReferenceLocation,
        Func<Symbol, Symbol?, Symbol?, Symbol?> cbSymbol,
        Func<Symbol, bool> allowBaseTypes)
    {
        Symbol? FromRoot(Symbol sym)
        {
            // If this is a union property:
            //   - In populateSearchSymbolsSet we will add all the symbols from all its source symbols in all unioned types.
            //   - In findRelatedSymbol, we will just use the union symbol if any source symbol is included in the search.
            // If the symbol is an instantiation from a another symbol (e.g. widened symbol):
            //   - In populateSearchSymbolsSet, add the root the list
            //   - In findRelatedSymbol, return the source symbol if that is in the search. (Do not return the instantiation symbol.)
            foreach (var rootSymbol in _checker.GetRootSymbols(sym))
            {
                if (cbSymbol(sym, rootSymbol, null /*baseSymbol*/) is { } result)
                {
                    return result;
                }

                // Add symbol of properties/methods of the same name in base classes and implemented interfaces definitions
                if (rootSymbol.Parent is not null && (rootSymbol.Parent.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0 && allowBaseTypes(rootSymbol))
                {
                    var baseResult = Ls.GetPropertySymbolsFromBaseTypes(rootSymbol.Parent, rootSymbol.Name, _checker, @base => cbSymbol(sym, rootSymbol, @base));
                    if (baseResult is not null)
                    {
                        return baseResult;
                    }
                }
            }

            return null;
        }

        if (location is not null && Ls.GetContainingObjectLiteralElement(location) is { } containingObjectLiteralElement)
        {
            /* Because in short-hand property assignment, location has two meaning : property name and as value of the property
             * When we do findAllReference at the position of the short-hand property assignment, we would want to have references to position of
             * property name and variable declaration of the identifier.
             * Like in below example, when querying for all references for an identifier 'name', of the property assignment, the language service
             * should show both 'name' in 'obj' and 'name' in variable declaration
             *      const name = "Foo";
             *      const obj = { name };
             * In order to do that, we will populate the search set with the value symbol of the identifier as a value of the property assignment
             * so that when matching with potential reference symbol, both symbols from property declaration and variable declaration
             * will be included correctly.
             */
            var shorthandValueSymbol = _checker.GetShorthandAssignmentValueSymbol(location.Parent);
            // gets the local symbol
            if (shorthandValueSymbol is not null && isForRenamePopulateSearchSymbolSet)
            {
                // When renaming 'x' in `const o = { x }`, just rename the local variable, not the property.
                return (cbSymbol(shorthandValueSymbol, null /*rootSymbol*/, null /*baseSymbol*/), EntryKind.SearchedLocalFoundProperty);
            }

            // If the location is in a context sensitive location (i.e. in an object literal) try
            // to get a contextual type for it, and add the property symbol from the contextual
            // type to the search set
            if (_checker.GetContextualType(containingObjectLiteralElement.Parent!, TypeScript.Checker.ContextFlags.None) is { } contextualType)
            {
                var symbols = _checker.GetPropertySymbolsFromContextualType(containingObjectLiteralElement, contextualType, true /*unionSymbolOk*/);
                foreach (var sym in symbols)
                {
                    if (FromRoot(sym) is { } res)
                    {
                        return (res, EntryKind.SearchedPropertyFoundLocal);
                    }
                }
            }

            // If the location is name of property symbol from object literal destructuring pattern
            // Search the property symbol
            //      for ( { property: p2 } of elems) { }
            if (_checker.GetPropertySymbolOfDestructuringAssignment(location) is { } propertySymbol)
            {
                if (cbSymbol(propertySymbol, null /*rootSymbol*/, null /*baseSymbol*/) is { } res)
                {
                    return (res, EntryKind.SearchedPropertyFoundLocal);
                }
            }

            if (shorthandValueSymbol is not null)
            {
                if (cbSymbol(shorthandValueSymbol, null /*rootSymbol*/, null /*baseSymbol*/) is { } res)
                {
                    return (res, EntryKind.SearchedLocalFoundProperty);
                }
            }
        }

        if (location is not null && Ls.GetMergedAliasedSymbolOfNamespaceExportDeclaration(location, symbol, _checker) is { } aliasedSymbol)
        {
            // In case of UMD module and global merging, search for global as well
            if (cbSymbol(aliasedSymbol, null /*rootSymbol*/, null /*baseSymbol*/) is { } res)
            {
                return (res, EntryKind.Node);
            }
        }

        if (FromRoot(symbol) is { } res2)
        {
            return (res2, EntryKind.Node);
        }

        if (symbol.ValueDeclaration is not null && IsParameterPropertyDeclaration(symbol.ValueDeclaration, symbol.ValueDeclaration.Parent!))
        {
            var (paramProp1, paramProp2) = _checker.GetSymbolsOfParameterPropertyDeclaration(symbol.ValueDeclaration, symbol.Name);
            Debug.Assert(
                (paramProp1.Flags & SymbolFlags.FunctionScopedVariable) != 0 && (paramProp2.Flags & SymbolFlags.ClassMember) != 0,
                "GetSymbolsOfParameterPropertyDeclaration must return (parameter, member) pair");
            return (FromRoot((symbol.Flags & SymbolFlags.FunctionScopedVariable) != 0 ? paramProp2 : paramProp1), EntryKind.Node);
        }

        if (GetDeclarationOfKind(symbol, Kind.ExportSpecifier) is { } exportSpecifier && (!isForRenamePopulateSearchSymbolSet || exportSpecifier.PropertyName() is null))
        {
            if (_checker.GetExportSpecifierLocalTargetSymbol(exportSpecifier) is { } localSymbol)
            {
                if (cbSymbol(localSymbol, null /*rootSymbol*/, null /*baseSymbol*/) is { } res)
                {
                    return (res, EntryKind.Node);
                }
            }
        }

        // symbolAtLocation for a binding element is the local symbol. See if the search symbol is the property.
        // Don't do this when populating search set for a rename when prefix and suffix text will be provided -- just rename the local.
        if (!isForRenamePopulateSearchSymbolSet)
        {
            Symbol? bindingElementPropertySymbol;
            if (onlyIncludeBindingElementAtReferenceLocation)
            {
                if (!IsObjectBindingElementWithoutPropertyName(location!.Parent!))
                {
                    return (null, EntryKind.None);
                }

                bindingElementPropertySymbol = Ls.GetPropertySymbolFromBindingElement(_checker, location!.Parent);
            }
            else
            {
                bindingElementPropertySymbol = Ls.GetPropertySymbolOfObjectBindingPatternWithoutPropertyName(symbol, _checker);
            }

            if (bindingElementPropertySymbol is null)
            {
                return (null, EntryKind.None);
            }

            return (FromRoot(bindingElementPropertySymbol), EntryKind.SearchedPropertyFoundLocal);
        }

        Debug.Assert(isForRenamePopulateSearchSymbolSet, "Expected rename search symbol set");

        // due to the above assert and the arguments at the uses of this function,
        // (onlyIncludeBindingElementAtReferenceLocation <=> !providePrefixAndSuffixTextForRename) holds
        var includeOriginalSymbolOfBindingElement = onlyIncludeBindingElementAtReferenceLocation;

        if (includeOriginalSymbolOfBindingElement)
        {
            if (Ls.GetPropertySymbolOfObjectBindingPatternWithoutPropertyName(symbol, _checker) is { } bindingElementPropertySymbol)
            {
                return (FromRoot(bindingElementPropertySymbol), EntryKind.SearchedPropertyFoundLocal);
            }
        }

        return (null, EntryKind.None);
    }

    /// <summary>Go: <c>func (state *refState) searchForName(sourceFile *ast.SourceFile, search *refSearch)</c>（findallreferences.go:2732-2736）。</summary>
    // Search for all occurrences of an identifier in a source file (and filter out the ones that match).
    internal void SearchForName(SourceFile sourceFile, RefSearch search)
    {
        if (sourceFile.GetNameTable().ContainsKey(search._escapedText))
        {
            GetReferencesInSourceFile(sourceFile, search, true /*addReferencesHere*/);
        }
    }

    /// <summary>Go: <c>func (state *refState) explicitlyInheritsFrom(symbol *ast.Symbol, parent *ast.Symbol) bool</c>（findallreferences.go:2738-2767）。</summary>
    internal bool ExplicitlyInheritsFrom(Symbol symbol, Symbol parent)
    {
        if (symbol == parent)
        {
            return true;
        }

        // Check cache first
        var key = new InheritKey(symbol, parent);
        if (_inheritsFromCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // Set to false initially to prevent infinite recursion
        _inheritsFromCache[key] = false;

        if (symbol.Declarations is null)
        {
            return false;
        }

        var inherits = Some(symbol.Declarations, declaration =>
        {
            var superTypeNodes = Ls.GetAllSuperTypeNodes(declaration);
            return Some(superTypeNodes, typeReference =>
            {
                var typ = _checker.GetTypeAtLocation(typeReference.AsNode()!);
                return typ is not null && typ.Symbol() is not null && ExplicitlyInheritsFrom(typ.Symbol()!, parent);
            });
        });

        // Update cache with the actual result
        _inheritsFromCache[key] = inherits;
        return inherits;
    }
}

/// <summary>Go: <c>type referencedSymbolDefinitionInfo struct</c>（findallreferences.go:866-870）。</summary>
internal sealed class ReferencedSymbolDefinitionInfo
{
    internal Node _node = null!;
    internal Location _location = null!;
    internal VSClassifiedTextElement _displayText = null!;
}

/// <summary>Go: <c>type SignatureUsage struct</c>（findallreferences.go:1210-1214）：<c>Name</c> 是标识符引用节点，<c>Call</c> 是所在调用表达式（非调用位置时为 nil）。</summary>
internal sealed class SignatureUsage
{
    internal Node? _name;
    internal Node? _call;
}

public static partial class Ls
{
    /// <summary>Go: <c>func getFileAndStartPosFromDeclaration(declaration *ast.Node) (*ast.SourceFile, core.TextPos)</c>（findallreferences.go:496-502）。</summary>
    internal static (SourceFile File, TextPos StartPos) GetFileAndStartPosFromDeclaration(Node declaration)
    {
        var file = GetSourceFileOfNode(declaration)!;
        var name = OrElse(GetNameOfDeclaration(declaration), declaration)!;
        var textRange = GetRangeOfNode(name, file, null /*endNode*/);

        return (file, new TextPos(textRange.Pos()));
    }

    /// <summary>
    /// This is special handling to determine if we should load up more projects and find location in other projects.
    /// By default arrows (and such other ast kinds) are not visible as declaration emitter doesnt need them.
    /// But we want to handle them specially so that they are visible if their parent is visible.
    /// </summary>
    internal static bool IsDefinitionVisible(EmitResolver emitResolver, Node declaration)
    {
        if (emitResolver.IsDeclarationVisible(declaration))
        {
            return true;
        }

        if (declaration.Parent is null)
        {
            return false;
        }

        // Variable initializers are visible if variable is visible
        if (HasInitializer(declaration.Parent) && declaration.Parent.Initializer() == declaration)
        {
            return IsDefinitionVisible(emitResolver, declaration.Parent);
        }

        // Handle some exceptions here like arrow function, members of class and object literal expression which are technically not visible but we want the definition to be determined by its parent
        switch (declaration.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.MethodDeclaration:
                // Private/protected properties/methods are not visible
                if (HasModifier(declaration, ModifierFlags.Private) || IsPrivateIdentifier(declaration.Name()!))
                {
                    return false;
                }

                // Public properties/methods are visible if its parents are visible, so:
                // falls through
                goto case Kind.Constructor;
            case Kind.Constructor:
            case Kind.PropertyAssignment:
            case Kind.ShorthandPropertyAssignment:
            case Kind.ObjectLiteralExpression:
            case Kind.ClassExpression:
            case Kind.ArrowFunction:
            case Kind.FunctionExpression:
                return IsDefinitionVisible(emitResolver, declaration.Parent);
            default:
                return false;
        }
    }

    // === functions for find all ref implementation ===

    // ⚠ `getRangeOfNode`（findallreferences.go:335-352）已由 G1 落在 `Ls/Utilities.cs:2013`（同一 `Ls` 静态类）
    //   ⇒ 本文件不重复声明，直接调用（Go 的签名逐字一致：`(Node, SourceFile?, Node?) -> core.TextRange`）。
    //   （G1 §3.2 "跨文件借调 helper" 表里写的是"归 G6"，实际落地在 Utilities.cs —— 以代码为准。）

    /// <summary>Go: <c>func isValidReferencePosition(node *ast.Node, searchSymbolName string) bool</c>（findallreferences.go:354-376）。</summary>
    internal static bool IsValidReferencePosition(Node node, string searchSymbolName)
    {
        switch (node.Kind)
        {
            case Kind.PrivateIdentifier:
                // !!!
                // if (isJSDocMemberName(node.Parent)) {
                // 	return true;
                // }
                return node.Text().Length == searchSymbolName.Length;
            case Kind.Identifier:
                return node.Text().Length == searchSymbolName.Length;
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.StringLiteral:
                return node.Text().Length == searchSymbolName.Length && (IsLiteralNameOfPropertyDeclarationOrIndexAccess(node) ||
                    IsNameOfModuleDeclaration(node) ||
                    IsExpressionOfExternalModuleImportEqualsDeclaration(node) ||
                    (IsCallExpression(node.Parent!) && IsBindableObjectDefinePropertyCall(node.Parent!) && node.Parent!.Arguments()![1] == node) ||
                    IsImportOrExportSpecifier(node.Parent!));
            case Kind.NumericLiteral:
                return IsLiteralNameOfPropertyDeclarationOrIndexAccess(node) && node.Text().Length == searchSymbolName.Length;
            case Kind.DefaultKeyword:
                return "default".Length == searchSymbolName.Length;
        }

        return false;
    }

    /// <summary>Go: <c>func isForRenameWithPrefixAndSuffixText(options refOptions) bool</c>（findallreferences.go:378-380）。</summary>
    internal static bool IsForRenameWithPrefixAndSuffixText(RefOptions options)
    {
        return options._use == ReferenceUse.Rename && options._useAliasesForRename;
    }

    /// <summary>Go: <c>func skipPastExportOrImportSpecifierOrUnion(symbol *ast.Symbol, node *ast.Node, checker *checker.Checker, useLocalSymbolForExportSpecifier bool) *ast.Symbol</c>（findallreferences.go:382-405）。</summary>
    internal static Symbol? SkipPastExportOrImportSpecifierOrUnion(Symbol symbol, Node? node, TypeScript.Checker.Checker checker, bool useLocalSymbolForExportSpecifier)
    {
        if (node is null)
        {
            return null;
        }

        var parent = node.Parent!;
        if (parent.Kind == Kind.ExportSpecifier && useLocalSymbolForExportSpecifier)
        {
            return GetLocalSymbolForExportSpecifier(node, symbol, parent.AsExportSpecifier()!, checker);
        }

        // If the symbol is declared as part of a declaration like `{ type: "a" } | { type: "b" }`, use the property on the union type to get more references.
        return FirstNonNil(symbol.Declarations, decl =>
        {
            if (decl.Parent is null)
            {
                // Ignore UMD module and global merge and CJS module end exports symbols
                if ((symbol.Flags & (SymbolFlags.Transient | SymbolFlags.ModuleExports)) != 0)
                {
                    return null;
                }

                // Assertions for GH#21814. We should be handling SourceFile symbols in `getReferencedSymbolsForModule` instead of getting here.
                Debug.Fail($"Unexpected symbol at {decl.Kind}: {symbol.Name}");
                return null;
            }

            if (decl.Parent.Kind == Kind.TypeLiteral && decl.Parent.Parent!.Kind == Kind.UnionType)
            {
                return checker.GetPropertyOfType(checker.GetTypeFromTypeNode(decl.Parent.Parent!), symbol.Name);
            }

            return null;
        });
    }

    /// <summary>Go: <c>func getSymbolScope(symbol *ast.Symbol) *ast.Node</c>（findallreferences.go:407-478）。</summary>
    internal static Node? GetSymbolScope(Symbol symbol)
    {
        // If this is the symbol of a named function expression or named class expression,
        // then named references are limited to its own scope.
        var valueDeclaration = symbol.ValueDeclaration;
        if (valueDeclaration is not null && (valueDeclaration.Kind == Kind.FunctionExpression || valueDeclaration.Kind == Kind.ClassExpression))
        {
            return valueDeclaration;
        }

        if ((symbol.Declarations?.Length ?? 0) == 0)
        {
            return null;
        }

        var declarations = symbol.Declarations!;
        // If this is private property or method, the scope is the containing class
        if ((symbol.Flags & (SymbolFlags.Property | SymbolFlags.Method)) != 0)
        {
            var privateDeclaration = Find(declarations, d => HasModifier(d, ModifierFlags.Private) || IsPrivateIdentifierClassElementDeclaration(d));
            if (privateDeclaration is not null)
            {
                return FindAncestorKind(privateDeclaration, Kind.ClassDeclaration);
            }

            // Else this is a public property and could be accessed from anywhere.
            return null;
        }

        // If symbol is of object binding pattern element without property name we would want to
        // look for property too and that could be anywhere
        if (Some(declarations, IsObjectBindingElementWithoutPropertyName))
        {
            return null;
        }

        /*
            If the symbol has a parent, it's globally visible unless:
            - It's a private property (handled above).
            - It's a type parameter.
            - The parent is an external module: then we should only search in the module (and recurse on the export later).
            - But if the parent has `export as namespace`, the symbol is globally visible through that namespace.
        */
        var exposedByParent = symbol.Parent is not null && (symbol.Flags & SymbolFlags.TypeParameter) == 0;
        if (exposedByParent && !(TypeScript.Checker.Checker.IsExternalModuleSymbol(symbol.Parent!) && !IsSourceFileWithGlobalExports(symbol.Parent!.ValueDeclaration)))
        {
            return null;
        }

        Node? scope = null;
        foreach (var declaration in declarations)
        {
            var container = GetContainerNode(declaration);
            if (scope is not null && scope != container)
            {
                // Different declarations have different containers, bail out
                return null;
            }

            if (container is null || (container.Kind == Kind.SourceFile && !IsExternalOrCommonJSModule(container.AsSourceFile()!)))
            {
                // This is a global variable and not an external module, any declaration defined
                // within this scope is visible outside the file
                return null;
            }

            scope = container;
        }

        // If symbol.parent, this means we are in an export of an external module. (Otherwise we would have returned `undefined` above.)
        // For an export of a module, we may be in a declaration file, and it may be accessed elsewhere. E.g.:
        //     declare module "a" { export type T = number; }
        //     declare module "b" { import { T } from "a"; export const x: T; }
        // So we must search the whole source file. (Because we will mark the source file as seen, we we won't return to it when searching for imports.)
        if (exposedByParent)
        {
            return GetSourceFileOfNode(scope!)!.AsNode();
        }

        return scope; // TODO: GH#18217
    }

    /// <summary>Go: <c>func newNodeEntryWithKind(node *ast.Node, kind entryKind) *ReferenceEntry</c>（findallreferences.go:208-212）。</summary>
    internal static ReferenceEntry NewNodeEntryWithKind(Node node, EntryKind kind)
    {
        var e = NewNodeEntry(node);
        e._kind = kind;
        return e;
    }

    /// <summary>Go: <c>func newNodeEntry(node *ast.Node) *ReferenceEntry</c>（findallreferences.go:214-221）。</summary>
    internal static ReferenceEntry NewNodeEntry(Node node)
    {
        // creates nodeEntry with `kind == entryKindNode`
        return new ReferenceEntry
        {
            _kind = EntryKind.Node,
            _node = OrElse(node.Name(), node),
            _context = GetContextNodeForNodeEntry(node),
        };
    }

    /// <summary>Go: <c>func getContextNodeForNodeEntry(node *ast.Node) *ast.Node</c>（findallreferences.go:223-284）。</summary>
    internal static Node? GetContextNodeForNodeEntry(Node node)
    {
        if (IsDeclaration(node))
        {
            return GetContextNode(node);
        }

        if (node.Parent is null)
        {
            return null;
        }

        if (!IsDeclaration(node.Parent) && !IsExportAssignment(node.Parent))
        {
            // Special property assignment in javascript
            if (IsInJSFile(node))
            {
                // !!! jsdoc: check if branch still needed
                Node? binaryExpression = null;
                if (IsBinaryExpression(node.Parent))
                {
                    binaryExpression = node.Parent;
                }
                else if (IsAccessExpression(node.Parent) && IsBinaryExpression(node.Parent.Parent!) && node.Parent.Parent!.AsBinaryExpression()!.Left == node.Parent)
                {
                    binaryExpression = node.Parent.Parent;
                }

                if (binaryExpression is not null && GetAssignmentDeclarationKind(binaryExpression) != JSDeclarationKind.None)
                {
                    return GetContextNode(binaryExpression);
                }
            }

            // Jsx Tags
            switch (node.Parent.Kind)
            {
                case Kind.JsxOpeningElement:
                case Kind.JsxClosingElement:
                    return node.Parent.Parent;
                case Kind.JsxSelfClosingElement:
                case Kind.LabeledStatement:
                case Kind.BreakStatement:
                case Kind.ContinueStatement:
                    return node.Parent;
                case Kind.StringLiteral:
                case Kind.NoSubstitutionTemplateLiteral:
                    if (TryGetImportFromModuleSpecifier(node) is { } validImport)
                    {
                        // ⚠ Go 的谓词忽略形参、直接看外层 `node`（findallreferences.go:259-261）——1:1 保留。
                        var declOrStatement = FindAncestor(validImport, _ => IsDeclaration(node) || IsStatement(node) || IsJSDocTag(node));
                        if (IsDeclaration(declOrStatement!))
                        {
                            return GetContextNode(declOrStatement!);
                        }

                        return declOrStatement;
                    }

                    break;
            }

            // Handle computed property name
            var propertyName = FindAncestor(node, IsComputedPropertyName);
            if (propertyName is not null)
            {
                return GetContextNode(propertyName.Parent!);
            }

            return null;
        }

        if (node.Parent.Name() == node || // node is name of declaration, use parent
            node.Parent.Kind == Kind.Constructor ||
            node.Parent.Kind == Kind.ExportAssignment ||
            // Property name of the import export specifier or binding pattern, use parent
            ((IsImportOrExportSpecifier(node.Parent) || node.Parent.Kind == Kind.BindingElement) && node.Parent.PropertyName() == node) ||
            // Is default export
            (node.Kind == Kind.DefaultKeyword && HasSyntacticModifier(node.Parent, ModifierFlags.ExportDefault)))
        {
            return GetContextNode(node.Parent);
        }

        return null;
    }

    /// <summary>Go: <c>func isStringLiteralPropertyReference(node *ast.StringLiteralLike, checker *checker.Checker) bool</c>（findallreferences.go:1394-1399）。</summary>
    internal static bool IsStringLiteralPropertyReference(Node node, TypeScript.Checker.Checker checker)
    {
        if (IsPropertySignatureDeclaration(node.Parent!))
        {
            return checker.GetPropertyOfType(checker.GetTypeAtLocation(node.Parent!.Parent!), node.Text()) is not null;
        }

        return false;
    }

    /// <summary>Go: <c>func getReferencedSymbolsSpecial(node *ast.Node, sourceFiles []*ast.SourceFile) []*SymbolAndEntries</c>（findallreferences.go:1421-1475）。</summary>
    internal static SymbolAndEntries[]? GetReferencedSymbolsSpecial(Node node, SourceFile[]? sourceFiles)
    {
        if (IsTypeKeyword(node.Kind))
        {
            // A void expression (i.e., `void foo()`) is not special, but the `void` type is.
            if (node.Kind == Kind.VoidKeyword && node.Parent!.Kind == Kind.VoidExpression)
            {
                return null;
            }

            // A modifier readonly (like on a property declaration) is not special;
            // a readonly type keyword (like `readonly string[]`) is.
            if (node.Kind == Kind.ReadonlyKeyword && !IsReadonlyTypeOperator(node))
            {
                return null;
            }

            // Likewise, when we *are* looking for a special keyword, make sure we
            // *don't* include readonly member modifiers.
            return GetAllReferencesForKeyword(
                sourceFiles,
                node.Kind,
                // cancellationToken,
                node.Kind == Kind.ReadonlyKeyword);
        }

        if (node.Parent is not null && IsImportMeta(node.Parent) && node.Parent.Name() == node)
        {
            return GetAllReferencesForImportMeta(sourceFiles);
        }

        if (node.Kind == Kind.StaticKeyword && node.Parent!.Kind == Kind.ClassStaticBlockDeclaration)
        {
            return [new SymbolAndEntries(new Definition { Kind = DefinitionKind.Keyword, _node = node }, [NewNodeEntry(node)])];
        }

        // Labels
        if (Ls.IsJumpStatementTarget(node))
        {
            // if we have a label definition, look within its statement for references, if not, then
            // the label is undefined and we have no results..
            if (GetTargetLabel(node.Parent!, node.Text()) is { } labelDefinition)
            {
                return GetLabelReferencesInNode(labelDefinition.Parent!, labelDefinition);
            }

            return null;
        }

        if (Ls.IsLabelOfLabeledStatement(node))
        {
            // it is a label definition and not a target, search within the parent labeledStatement
            return GetLabelReferencesInNode(node.Parent!, node);
        }

        if (Ls.IsThis(node))
        {
            return GetReferencesForThisKeyword(node, sourceFiles /*, cancellationToken*/);
        }

        if (node.Kind == Kind.SuperKeyword)
        {
            return GetReferencesForSuperKeyword(node);
        }

        return null;
    }

    /// <summary>Go: <c>func getLabelReferencesInNode(container *ast.Node, targetLabel *ast.Node) []*SymbolAndEntries</c>（findallreferences.go:1477-1488）。</summary>
    internal static SymbolAndEntries[] GetLabelReferencesInNode(Node container, Node targetLabel)
    {
        var sourceFile = GetSourceFileOfNode(container)!;
        var labelName = targetLabel.Text();
        var references = MapNonNil(GetPossibleSymbolReferenceNodes(sourceFile, labelName, container), node =>
        {
            // Only pick labels that are either the target label, or have a target that is the target label
            if (node == targetLabel || (Ls.IsJumpStatementTarget(node) && GetTargetLabel(node, labelName) == targetLabel))
            {
                return NewNodeEntry(node);
            }

            return null!;
        });
        return [SymbolAndEntries.NewSymbolAndEntries(DefinitionKind.Label, targetLabel, null, references)];
    }

    /// <summary>Go: <c>func getReferencesForThisKeyword(thisOrSuperKeyword *ast.Node, sourceFiles []*ast.SourceFile) []*SymbolAndEntries</c>（findallreferences.go:1490-1566）。</summary>
    internal static SymbolAndEntries[]? GetReferencesForThisKeyword(Node thisOrSuperKeyword, SourceFile[]? sourceFiles)
    {
        var searchSpaceNode = GetThisContainer(thisOrSuperKeyword, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/)!;

        // Whether 'this' occurs in a static context within a class.
        var staticFlag = ModifierFlags.Static;
        bool IsParameterName(Node n)
        {
            return n.Kind == Kind.Identifier && n.Parent!.Kind == Kind.Parameter && n.Parent!.Name() == n;
        }

        switch (searchSpaceNode.Kind)
        {
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                if ((searchSpaceNode.Kind == Kind.MethodDeclaration || searchSpaceNode.Kind == Kind.MethodSignature) && IsObjectLiteralMethod(searchSpaceNode))
                {
                    staticFlag &= searchSpaceNode.ModifierFlags;
                    searchSpaceNode = searchSpaceNode.Parent!; // re-assign to be the owning object literals
                    break;
                }

                staticFlag &= searchSpaceNode.ModifierFlags;
                searchSpaceNode = searchSpaceNode.Parent!; // re-assign to be the owning class
                break;
            case Kind.SourceFile:
                if (IsExternalModule(searchSpaceNode.AsSourceFile()!) || IsParameterName(thisOrSuperKeyword))
                {
                    return null;
                }

                break;
            case Kind.FunctionDeclaration:
            case Kind.FunctionExpression:
                // Computed properties in classes are not handled here because references to this are illegal,
                // so there is no point finding references to them.
                break;
            default:
                return null;
        }

        var filesToSearch = sourceFiles;
        if (searchSpaceNode.Kind != Kind.SourceFile)
        {
            filesToSearch = [GetSourceFileOfNode(searchSpaceNode)!];
        }

        var references = Map(
            FlatMap(filesToSearch, sourceFile =>
            {
                // cancellationToken.throwIfCancellationRequested();
                return Filter(
                    GetPossibleSymbolReferenceNodes(sourceFile, "this", IfElse(searchSpaceNode.Kind == Kind.SourceFile, sourceFile.AsNode(), searchSpaceNode)),
                    node =>
                    {
                        if (!Ls.IsThis(node))
                        {
                            return false;
                        }

                        var container = GetThisContainer(node /*includeArrowFunctions*/, false, false /*includeClassComputedPropertyName*/);
                        if (!CanHaveSymbol(container!))
                        {
                            return false;
                        }

                        switch (searchSpaceNode.Kind)
                        {
                            case Kind.FunctionExpression:
                            case Kind.FunctionDeclaration:
                                return searchSpaceNode.Symbol == container!.Symbol;
                            case Kind.MethodDeclaration:
                            case Kind.MethodSignature:
                                return IsObjectLiteralMethod(searchSpaceNode) && searchSpaceNode.Symbol == container!.Symbol;
                            case Kind.ClassExpression:
                            case Kind.ClassDeclaration:
                            case Kind.ObjectLiteralExpression:
                                // Make sure the container belongs to the same class/object literals
                                // and has the appropriate static modifier from the original container.
                                return container!.Parent is not null && CanHaveSymbol(container.Parent) && searchSpaceNode.Symbol == container.Parent.Symbol && IsStatic(container) == (staticFlag != ModifierFlags.None);
                            case Kind.SourceFile:
                                return container!.Kind == Kind.SourceFile && !IsExternalModule(container.AsSourceFile()!) && !IsParameterName(node);
                        }

                        return false;
                    });
            }),
            n => NewNodeEntry(n));

        var thisParameter = FirstNonNil(references, @ref =>
        {
            if (@ref._node!.Parent!.Kind == Kind.Parameter)
            {
                return @ref._node;
            }

            return null;
        });
        thisParameter ??= thisOrSuperKeyword;
        return [SymbolAndEntries.NewSymbolAndEntries(DefinitionKind.This, thisParameter, searchSpaceNode.Symbol, references)];
    }

    /// <summary>Go: <c>func getReferencesForSuperKeyword(superKeyword *ast.Node) []*SymbolAndEntries</c>（findallreferences.go:1568-1602）。</summary>
    internal static SymbolAndEntries[]? GetReferencesForSuperKeyword(Node superKeyword)
    {
        var searchSpaceNode = GetSuperContainer(superKeyword, false /*stopOnFunctions*/);
        if (searchSpaceNode is null)
        {
            return null;
        }

        // Whether 'super' occurs in a static context within a class.
        var staticFlag = ModifierFlags.Static;

        switch (searchSpaceNode.Kind)
        {
            case Kind.PropertyDeclaration:
            case Kind.PropertySignature:
            case Kind.MethodDeclaration:
            case Kind.MethodSignature:
            case Kind.Constructor:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                staticFlag &= searchSpaceNode.ModifierFlags;
                searchSpaceNode = searchSpaceNode.Parent!; // re-assign to be the owning class
                break;
            default:
                return null;
        }

        var sourceFile = GetSourceFileOfNode(searchSpaceNode)!;
        var references = MapNonNil(GetPossibleSymbolReferenceNodes(sourceFile, "super", searchSpaceNode), node =>
        {
            if (node.Kind != Kind.SuperKeyword)
            {
                return null!;
            }

            var container = GetSuperContainer(node, false /*stopOnFunctions*/);

            // If we have a 'super' container, we must have an enclosing class.
            // Now make sure the owning class is the same as the search-space
            // and has the same static qualifier as the original 'super's owner.
            if (container is not null && IsStatic(container) == (staticFlag != ModifierFlags.None) && container.Parent!.Symbol == searchSpaceNode.Symbol)
            {
                return NewNodeEntry(node);
            }

            return null!;
        });

        return [SymbolAndEntries.NewSymbolAndEntries(DefinitionKind.Symbol, null, searchSpaceNode.Symbol, references)];
    }

    /// <summary>Go: <c>func getAllReferencesForImportMeta(sourceFiles []*ast.SourceFile) []*SymbolAndEntries</c>（findallreferences.go:1604-1618）。</summary>
    internal static SymbolAndEntries[]? GetAllReferencesForImportMeta(SourceFile[]? sourceFiles)
    {
        var references = FlatMap(sourceFiles, sourceFile =>
        {
            return MapNonNil(GetPossibleSymbolReferenceNodes(sourceFile, "meta", sourceFile.AsNode()), node =>
            {
                var parent = node.Parent!;
                if (IsImportMeta(parent))
                {
                    return NewNodeEntry(parent);
                }

                return null!;
            });
        });
        if (references.Length == 0)
        {
            return null;
        }

        return [new SymbolAndEntries(new Definition { Kind = DefinitionKind.Keyword, _node = references[0]._node }, references)];
    }

    /// <summary>Go: <c>func getAllReferencesForKeyword(sourceFiles []*ast.SourceFile, keywordKind ast.Kind, filterReadOnlyTypeOperator bool) []*SymbolAndEntries</c>（findallreferences.go:1620-1635）。</summary>
    internal static SymbolAndEntries[]? GetAllReferencesForKeyword(SourceFile[]? sourceFiles, Kind keywordKind, bool filterReadOnlyTypeOperator)
    {
        // references is a list of NodeEntry
        var references = FlatMap(sourceFiles, sourceFile =>
        {
            // cancellationToken.throwIfCancellationRequested();
            return MapNonNil(GetPossibleSymbolReferenceNodes(sourceFile, TokenToString(keywordKind), sourceFile.AsNode()), referenceLocation =>
            {
                if (referenceLocation.Kind == keywordKind && (!filterReadOnlyTypeOperator || IsReadonlyTypeOperator(referenceLocation)))
                {
                    return NewNodeEntry(referenceLocation);
                }

                return null!;
            });
        });
        if (references.Length == 0)
        {
            return null;
        }

        return [SymbolAndEntries.NewSymbolAndEntries(DefinitionKind.Keyword, references[0]._node, null, references)];
    }

    /// <summary>Go: <c>func getPossibleSymbolReferenceNodes(sourceFile *ast.SourceFile, symbolName string, container *ast.Node) []*ast.Node</c>（findallreferences.go:1637-1644）。</summary>
    internal static Node[] GetPossibleSymbolReferenceNodes(SourceFile sourceFile, string symbolName, Node? container)
    {
        return MapNonNil(GetPossibleSymbolReferencePositions(sourceFile, symbolName, container), pos =>
        {
            var referenceLocation = TypeScript.AstNav.AstNav.GetTouchingPropertyName(sourceFile, pos);
            if (referenceLocation != sourceFile.AsNode())
            {
                return referenceLocation;
            }

            return null!;
        });
    }

    /// <summary>Go: <c>func getPossibleSymbolReferencePositions(sourceFile *ast.SourceFile, symbolName string, container *ast.Node) []int</c>（findallreferences.go:1646-1690）。</summary>
    internal static int[] GetPossibleSymbolReferencePositions(SourceFile sourceFile, string symbolName, Node? container)
    {
        var positions = new List<int>();

        // Go 的 `text[pos]` 取的是 **UTF-8 字节**（且随后 `rune(byte)` 把该字节当作 U+0000–U+00FF 码点），
        // 而 C# 的位置是 UTF-16 下标、`text[i]` 取的是 char。为逐字复刻 Go 的"相邻字符"判定，这里显式还原
        // Go 看到的那个字节，再交给 IsIdentifierPart（Go: `rune(text[position-1])` / `rune(text[endPosition])`）。
        // 匹配（符号名）永远是 ASCII 或完整码点，故只在"相邻字符"这一处需要字节语义。
        static int GoUtf8LeadByte(string s, int index)
        {
            // 匹配之后那个字符的首字节：<0x80 即自身；2/3 字节序列 0xC0/0xE0|…；代理对（4 字节）0xF0|…。
            var ch = s[index];
            if (ch < 0x80)
            {
                return ch;
            }

            if (char.IsHighSurrogate(ch) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
            {
                return 0xF0 | (char.ConvertToUtf32(ch, s[index + 1]) >> 18);
            }

            return ch < 0x800 ? 0xC0 | (ch >> 6) : 0xE0 | (ch >> 12);
        }

        static int GoUtf8TrailingByte(char ch)
        {
            // 匹配之前那个字符的末字节：ASCII 即自身，否则恒为 0x80|(码点&0x3F)（低位代理也满足该式）。
            return ch < 0x80 ? ch : 0x80 | (ch & 0x3F);
        }

        // TODO: Cache symbol existence for files to save text search
        // Also, need to make this work for unicode escapes.

        // Be resilient in the face of a symbol with no name or zero length name
        if (symbolName.Length == 0)
        {
            return [.. positions];
        }

        var text = sourceFile.Text();
        var sourceLength = text.Length;
        var symbolNameLength = symbolName.Length;

        container ??= sourceFile.AsNode();

        // ⚠ 1:1 复刻 Go：`position := strings.Index(text[container.Pos():], symbolName)` —— 首个匹配的偏移是
        //    **相对 `container.Pos()`** 的（不含切片起点），之后的 `position = startIndex + foundIndex` 才是**绝对**偏移。
        //    因此首轮用 `text[position-1]` / 与 `container.End()` 比较时用的是位移后的下标（只有 container.Pos()==0 时才一致），
        //    这正是 Go 的行为：container 非 0 起点时首轮匹配可能被误判/落成位移位置（见 findAllRefsWithLeadingUnderscoreNames8：
        //    函数表达式容器下声明名被丢弃）。**不要**"修正"成绝对偏移。
        var firstMatch = text.IndexOf(symbolName, container.Pos(), StringComparison.Ordinal);
        var position = firstMatch < 0 ? -1 : firstMatch - container.Pos();
        var endPos = container.End();
        while (position >= 0 && position < endPos)
        {
            // We found a match.  Make sure it's not part of a larger word (i.e. the char
            // before and after it have to be a non-identifier char).
            var endPosition = position + symbolNameLength;

            if ((position == 0 || !IsIdentifierPart(GoUtf8TrailingByte(text[position - 1]))) &&
                (endPosition == sourceLength || !IsIdentifierPart(GoUtf8LeadByte(text, endPosition))))
            {
                // Found a real match.  Keep searching.
                positions.Add(position);
            }

            var startIndex = position + symbolNameLength + 1;
            if (startIndex > text.Length)
            {
                break;
            }

            // C# 的 IndexOf 返回绝对偏移（Go 的 strings.Index(text[startIndex:], …) 需再加 startIndex）。
            var foundIndex = text.IndexOf(symbolName, startIndex, StringComparison.Ordinal);
            if (foundIndex != -1)
            {
                position = foundIndex;
            }
            else
            {
                break;
            }
        }

        return [.. positions];
    }

    /// <summary>Go: <c>func findFirstJsxNode(root *ast.Node) *ast.Node</c>（findallreferences.go:1692-1716）：递归查找第一个 JSX element/self-closing/fragment。</summary>
    internal static Node? FindFirstJsxNode(Node root)
    {
        Node? Visit(Node node)
        {
            // Check if this is a JSX node we're looking for
            switch (node.Kind)
            {
                case Kind.JsxElement:
                case Kind.JsxSelfClosingElement:
                case Kind.JsxFragment:
                    return node;
            }

            // Skip subtree if it doesn't contain JSX
            if ((node.SubtreeFacts & SubtreeFacts.ContainsJsx) == 0)
            {
                return null;
            }

            // Traverse children to find JSX node
            Node? result = null;
            node.ForEachChild(child =>
            {
                result = Visit(child);
                return result is not null; // Stop if found
            });
            return result;
        }

        return Visit(root);
    }

    /// <summary>Go: <c>func getReferencesForNonModule(referencedFile *ast.SourceFile, program *compiler.Program) []*ReferenceEntry</c>（findallreferences.go:1718-1721；Go 里也是 !!! not implemented）。</summary>
    internal static ReferenceEntry[] GetReferencesForNonModule(SourceFile referencedFile, Program program)
    {
        // !!! not implemented
        return [];
    }

    /// <summary>Go: <c>func getMergedAliasedSymbolOfNamespaceExportDeclaration(node *ast.Node, symbol *ast.Symbol, checker *checker.Checker) *ast.Symbol</c>（findallreferences.go:1723-1733）。</summary>
    internal static Symbol? GetMergedAliasedSymbolOfNamespaceExportDeclaration(Node? node, Symbol? symbol, TypeScript.Checker.Checker checker)
    {
        if (node?.Parent is not null && node.Parent.Kind == Kind.NamespaceExportDeclaration && symbol is not null)
        {
            if (checker.ResolveAlias(symbol, out var aliasedSymbol) && aliasedSymbol is not null)
            {
                var targetSymbol = checker.GetMergedSymbol(aliasedSymbol);
                if (aliasedSymbol != targetSymbol)
                {
                    return targetSymbol;
                }
            }
        }

        return null;
    }

    /// <summary>Go: <c>func getSpecialSearchKind(node *ast.Node) string</c>（findallreferences.go:1838-1854）。</summary>
    internal static string GetSpecialSearchKind(Node? node)
    {
        if (node is null)
        {
            return "none";
        }

        switch (node.Kind)
        {
            case Kind.Constructor:
            case Kind.ConstructorKeyword:
                return "constructor";
            case Kind.Identifier:
                if (IsClassLike(node.Parent!))
                {
                    Debug.Assert(node.Parent!.Name() == node, "Expected class-like name to match node");
                    return "class";
                }

                goto default;
            default:
                return "none";
        }
    }

    /// <summary>Go: <c>func getReferencedSymbolsForSymbol(ctx, program, originalSymbol, node, sourceFiles, sourceFilesSet, checker, options) []*SymbolAndEntries</c>（findallreferences.go:1856-1886）。</summary>
    internal static SymbolAndEntries[] GetReferencedSymbolsForSymbol(
        Context ctx,
        Program program,
        Symbol originalSymbol,
        Node? node,
        SourceFile[]? sourceFiles,
        Set<string> sourceFilesSet,
        TypeScript.Checker.Checker checker,
        RefOptions options)
    {
        // Core find-all-references algorithm for a normal symbol.

        var symbol = Coalesce(SkipPastExportOrImportSpecifierOrUnion(originalSymbol, node, checker /*useLocalSymbolForExportSpecifier*/, !IsForRenameWithPrefixAndSuffixText(options)), originalSymbol)!;

        // Compute the meaning from the location and the symbol it references
        var searchMeaning = SemanticMeaning.All;
        if (options._use != ReferenceUse.Rename)
        {
            searchMeaning = GetIntersectingMeaningFromDeclarations(node, symbol, SemanticMeaning.All);
        }

        var state = NewState(ctx, program, sourceFiles, sourceFilesSet, node, checker, searchMeaning, options);

        Node? exportSpecifier = null;
        if (IsForRenameWithPrefixAndSuffixText(options) && (symbol.Declarations?.Length ?? 0) != 0)
        {
            exportSpecifier = Find(symbol.Declarations, IsExportSpecifier);
        }

        if (exportSpecifier is not null)
        {
            // When renaming at an export specifier, rename the export and not the thing being exported.
            state.GetReferencesAtExportSpecifier(
                exportSpecifier.Name()!,
                symbol,
                exportSpecifier.AsExportSpecifier()!,
                state.CreateSearch(node, originalSymbol, ImpExpKind.Unknown /*comingFrom*/, "", null),
                true /*addReferencesHere*/,
                true /*alwaysGetReferences*/);
        }
        else if (node is not null && node.Kind == Kind.DefaultKeyword && symbol.Name == InternalSymbolNameDefault && symbol.Parent is not null)
        {
            state.AddReference(node, symbol, EntryKind.Node);
            state.SearchForImportsOfExport(node, symbol, new ExportInfo { _exportingModuleSymbol = symbol.Parent, _exportKind = ExportKind.Default });
        }
        else
        {
            var search = state.CreateSearch(node, symbol, ImpExpKind.Unknown /*comingFrom*/, "", state.PopulateSearchSymbolSet(symbol, node, options._use == ReferenceUse.Rename, options._useAliasesForRename, options._implementations));
            state.GetReferencesInContainerOrFiles(symbol, search);
        }

        return [.. state._result];
    }

    /// <summary>Go: <c>func newState(ctx, program, sourceFiles, sourceFilesSet, node, checker, searchMeaning, options) *refState</c>（findallreferences.go:1929-1943）。</summary>
    internal static RefState NewState(
        Context ctx,
        Program program,
        SourceFile[]? sourceFiles,
        Set<string> sourceFilesSet,
        Node? node,
        TypeScript.Checker.Checker checker,
        SemanticMeaning searchMeaning,
        RefOptions options)
    {
        return new RefState
        {
            _sourceFiles = sourceFiles,
            _sourceFilesSet = sourceFilesSet,
            _specialSearchKind = GetSpecialSearchKind(node),
            _checker = checker,
            _ctx = ctx,
            _program = program,
            _searchMeaning = searchMeaning,
            _options = options,
            _inheritsFromCache = [],
            _symbolToReferences = [],
            _sourceFileToSeenSymbols = [],
        };
    }

    /// <summary>Go: <c>func getReferenceEntriesForShorthandPropertyAssignment(node *ast.Node, checker *checker.Checker, addReference func(*ast.Node))</c>（findallreferences.go:2020-2033）。</summary>
    internal static void GetReferenceEntriesForShorthandPropertyAssignment(Node node, TypeScript.Checker.Checker checker, Action<Node> addReference)
    {
        var refSymbol = checker.GetSymbolAtLocation(node);
        if (refSymbol?.ValueDeclaration is null)
        {
            return;
        }

        var shorthandSymbol = checker.GetShorthandAssignmentValueSymbol(refSymbol.ValueDeclaration);
        if (shorthandSymbol is not null && (shorthandSymbol.Declarations?.Length ?? 0) > 0)
        {
            foreach (var declaration in shorthandSymbol.Declarations!)
            {
                if ((Ls.GetMeaningFromDeclaration(declaration) & SemanticMeaning.Value) != 0)
                {
                    addReference(declaration);
                }
            }
        }
    }

    /// <summary>Go: <c>func isMethodOrAccessor(node *ast.Node) bool</c>（findallreferences.go:2035-2037）。</summary>
    internal static bool IsMethodOrAccessor(Node node)
    {
        return node.Kind == Kind.MethodDeclaration || node.Kind == Kind.GetAccessor || node.Kind == Kind.SetAccessor;
    }

    /// <summary>Go: <c>func tryGetClassByExtendingIdentifier(node *ast.Node) *ast.ClassLikeDeclaration</c>（findallreferences.go:2039-2041）。</summary>
    internal static Node? TryGetClassByExtendingIdentifier(Node node)
    {
        return TryGetClassExtendingExpressionWithTypeArguments(ClimbPastPropertyAccess(node)!.Parent!);
    }

    /// <summary>Go: <c>func getClassConstructorSymbol(classSymbol *ast.Symbol) *ast.Symbol</c>（findallreferences.go:2043-2048）。</summary>
    internal static Symbol? GetClassConstructorSymbol(Symbol? classSymbol)
    {
        if (classSymbol?.Members is null)
        {
            return null;
        }

        return classSymbol.Members.GetValueOrDefault(InternalSymbolNameConstructor);
    }

    /// <summary>Go: <c>func hasOwnConstructor(classDeclaration *ast.ClassLikeDeclaration) bool</c>（findallreferences.go:2050-2052）。</summary>
    internal static bool HasOwnConstructor(Node classDeclaration)
    {
        return GetClassConstructorSymbol(classDeclaration.Symbol) is not null;
    }

    /// <summary>Go: <c>func findOwnConstructorReferences(classSymbol *ast.Symbol, sourceFile *ast.SourceFile, addNode func(*ast.Node))</c>（findallreferences.go:2054-2081）。</summary>
    internal static void FindOwnConstructorReferences(Symbol classSymbol, SourceFile sourceFile, Action<Node> addNode)
    {
        var constructorSymbol = GetClassConstructorSymbol(classSymbol);
        if (constructorSymbol is not null && (constructorSymbol.Declarations?.Length ?? 0) > 0)
        {
            foreach (var decl in constructorSymbol.Declarations!)
            {
                if (decl.Kind == Kind.Constructor)
                {
                    if (TypeScript.AstNav.AstNav.FindChildOfKind(decl, Kind.ConstructorKeyword, sourceFile) is { } ctrKeyword)
                    {
                        addNode(ctrKeyword);
                    }
                }
            }
        }

        if (classSymbol.Exports is not null)
        {
            foreach (var (_, member) in classSymbol.Exports)
            {
                var decl = member.ValueDeclaration;
                if (decl is not null && decl.Kind == Kind.MethodDeclaration)
                {
                    if (decl.Body() is { } body)
                    {
                        ForEachDescendantOfKind(body, Kind.ThisKeyword, thisKeyword =>
                        {
                            if (IsNewExpressionTarget(thisKeyword, false, false))
                            {
                                addNode(thisKeyword);
                            }
                        });
                    }
                }
            }
        }
    }

    /// <summary>Go: <c>func findSuperConstructorAccesses(classDeclaration *ast.ClassLikeDeclaration, addNode func(*ast.Node))</c>（findallreferences.go:2083-2101）。</summary>
    internal static void FindSuperConstructorAccesses(Node classDeclaration, Action<Node> addNode)
    {
        var constructorSymbol = GetClassConstructorSymbol(classDeclaration.Symbol);
        if (constructorSymbol is null || (constructorSymbol.Declarations?.Length ?? 0) == 0)
        {
            return;
        }

        foreach (var decl in constructorSymbol.Declarations!)
        {
            if (decl.Kind == Kind.Constructor)
            {
                if (decl.Body() is { } body)
                {
                    ForEachDescendantOfKind(body, Kind.SuperKeyword, node =>
                    {
                        if (IsCallExpressionTarget(node, false, false))
                        {
                            addNode(node);
                        }
                    });
                }
            }
        }
    }

    /// <summary>Go: <c>func forEachDescendantOfKind(node *ast.Node, kind ast.Kind, action func(*ast.Node))</c>（findallreferences.go:2103-2111）。</summary>
    internal static void ForEachDescendantOfKind(Node node, Kind kind, Action<Node> action)
    {
        node.ForEachChild(child =>
        {
            if (child.Kind == kind)
            {
                action(child);
            }

            ForEachDescendantOfKind(child, kind, action);
            return false;
        });
    }

    /// <summary>Go: <c>func isDeclarationOfSymbol(node *ast.Node, target *ast.Symbol) bool</c>（findallreferences.go:1081-1103）。</summary>
    internal static bool IsDeclarationOfSymbol(Node? node, Symbol? target)
    {
        if (node is null || target is null)
        {
            return false;
        }

        Node? source = null;
        if (GetDeclarationFromName(node) is { } decl)
        {
            source = decl;
        }
        else if (node.Kind == Kind.DefaultKeyword)
        {
            source = node.Parent;
        }
        else if (IsLiteralComputedPropertyDeclarationName(node))
        {
            source = node.Parent!.Parent;
        }
        else if (node.Kind == Kind.ConstructorKeyword && IsConstructorDeclaration(node.Parent!))
        {
            source = node.Parent!.Parent;
        }

        // !!!
        // const commonjsSource = source && isBinaryExpression(source) ? source.left as unknown as Declaration : undefined;

        return source is not null && Some(target.Declarations, d => d == source);
    }

    /// <summary>
    /// Go: <c>func nodeIsEligibleForRename(node *ast.Node) bool</c>（rename.go:209-226）。
    /// ⚠ **归属偏离**：Go 里它在 `rename.go`（G7 票面），但 `findallreferences.go:684` 的
    /// `provideSymbolsAndEntriesAtPosition` 依赖它，而本仓既定先例是"跨文件 helper 就地落地 + 备案"
    /// （G1 的 `GetRangeOfNode`、G4 的 `RefOptions` 同理）⇒ G6 就地落在本文件，已报 Main；G7 勿重复声明。
    /// </summary>
    internal static bool NodeIsEligibleForRename(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.StringLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.ThisKeyword:
                return true;
            case Kind.NumericLiteral:
                return IsLiteralNameOfPropertyDeclarationOrIndexAccess(node);
            default:
                return false;
        }
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) getNonLocalDefinition(ctx context.Context, entry *SymbolAndEntries) *nonLocalDefinition</c>（findallreferences.go:504-559）。</summary>
    internal NonLocalDefinition? GetNonLocalDefinition(Context ctx, SymbolAndEntries entry)
    {
        if (!entry.CanUseDefinitionSymbol())
        {
            return null;
        }

        var program = GetProgram()!;
        var (checker, done) = program.GetTypeChecker(ctx);
        try
        {
            var emitResolver = checker.GetEmitResolver();
            foreach (var d in entry._definition!._symbol!.Declarations ?? [])
            {
                if (IsDefinitionVisible(emitResolver, d))
                {
                    var (file, startPos) = GetFileAndStartPosFromDeclaration(d);
                    var fileName = file.FileName();
                    var (lspPosition, fidelity) = _converters!.ToLSPPosition(file, startPos);
                    if (fidelity == Fidelity.None)
                    {
                        continue;
                    }

                    return new NonLocalDefinition
                    {
                        _uri = FileNameToDocumentURI(fileName),
                        _pos = lspPosition,
                        GetSourcePosition = new Lazy<HasTextDocumentPosition?>(() =>
                        {
                            var mapped = TryGetSourcePosition(fileName, startPos);
                            if (mapped is not null)
                            {
                                var (mappedPosition, mappedFidelity) = _converters!.ToLSPPosition(GetScript(mapped.FileName)!, new TextPos(mapped.Pos));
                                if (mappedFidelity == Fidelity.None)
                                {
                                    return null;
                                }

                                return new LspPosition
                                {
                                    _uri = FileNameToDocumentURI(mapped.FileName),
                                    _pos = mappedPosition,
                                };
                            }

                            return null;
                        }),
                        GetGeneratedPosition = new Lazy<HasTextDocumentPosition?>(() =>
                        {
                            var mapped = TryGetGeneratedPosition(fileName, startPos);
                            if (mapped is not null)
                            {
                                var (mappedPosition, mappedFidelity) = _converters!.ToLSPPosition(GetScript(mapped.FileName)!, new TextPos(mapped.Pos));
                                if (mappedFidelity == Fidelity.None)
                                {
                                    return null;
                                }

                                return new LspPosition
                                {
                                    _uri = FileNameToDocumentURI(mapped.FileName),
                                    _pos = mappedPosition,
                                };
                            }

                            return null;
                        }),
                    };
                }
            }
        }
        finally
        {
            done();
        }

        return null;
    }

    /// <summary>Go: <c>func (l *LanguageService) forEachOriginalDefinitionLocation(ctx, entry, cb)</c>（findallreferences.go:600-629）。</summary>
    internal void ForEachOriginalDefinitionLocation(Context ctx, SymbolAndEntries entry, Action<DocumentUri, Position> cb)
    {
        if (!entry.CanUseDefinitionSymbol())
        {
            return;
        }

        var program = GetProgram()!;
        foreach (var d in entry._definition!._symbol!.Declarations ?? [])
        {
            var (file, startPos) = GetFileAndStartPosFromDeclaration(d);
            var fileName = file.FileName();
            if (IsDeclarationFileName(fileName))
            {
                // Map to ts position
                var mapped = TryGetSourcePosition(file.FileName(), startPos);
                if (mapped is not null)
                {
                    var (lspPosition, fidelity) = _converters!.ToLSPPosition(GetScript(mapped.FileName)!, new TextPos(mapped.Pos));
                    if (fidelity != Fidelity.None)
                    {
                        cb(FileNameToDocumentURI(mapped.FileName), lspPosition);
                    }
                }
            }
            else if (program.IsSourceFromProjectReference(ToPath(fileName)))
            {
                var (lspPosition, fidelity) = _converters!.ToLSPPosition(file, startPos);
                if (fidelity != Fidelity.None)
                {
                    cb(FileNameToDocumentURI(fileName), lspPosition);
                }
            }
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) provideSymbolsAndEntries(ctx, uri, documentPosition, isRename, implementations) (SymbolAndEntriesData, bool)</c>（findallreferences.go:644-675）。</summary>
    internal (SymbolAndEntriesData Data, bool Ok) ProvideSymbolsAndEntries(Context ctx, DocumentUri uri, Position documentPosition, bool isRename, bool implementations)
    {
        // `findReferencedSymbols` except only computes the information needed to return reference locations
        var (program, sourceFile) = GetProgramAndFile(uri);
        var feature = Feature.References;
        if (implementations)
        {
            feature = Feature.Implementation;
        }
        else if (isRename)
        {
            feature = Feature.Rename;
        }

        var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, documentPosition, feature);
        if (positions.Length == 0)
        {
            return (new SymbolAndEntriesData(), false);
        }

        var combined = new SymbolAndEntriesData();
        var ok = false;
        foreach (var mapped in positions)
        {
            if (!mapped.Fidelity.IsSingleSegment())
            {
                continue;
            }

            var (data, found) = ProvideSymbolsAndEntriesAtPosition(ctx, program, mapped.Script, mapped.Position, isRename, implementations);
            if (!found)
            {
                continue;
            }

            if (!ok)
            {
                combined.OriginalNode = data.OriginalNode;
                combined.Position = data.Position;
                ok = true;
            }

            if (data.SymbolsAndEntries is { Length: > 0 })
            {
                combined.SymbolsAndEntries = [.. combined.SymbolsAndEntries ?? [], .. data.SymbolsAndEntries];
            }
        }

        return (combined, ok);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideSymbolsAndEntriesAtPosition(ctx, program, sourceFile, position, isRename, implementations) (SymbolAndEntriesData, bool)</c>（findallreferences.go:677-724）。</summary>
    internal (SymbolAndEntriesData Data, bool Ok) ProvideSymbolsAndEntriesAtPosition(Context ctx, Program program, SourceFile sourceFile, int position, bool isRename, bool implementations)
    {
        var node = TypeScript.AstNav.AstNav.GetTouchingPropertyName(sourceFile, position);
        if (isRename)
        {
            // Adjust modifier/keyword nodes to the declaration name, matching Strada's findRenameLocations.
            node = Ls.GetAdjustedLocation(node, true /*forRename*/, sourceFile)!;
        }

        if ((isRename && !Ls.NodeIsEligibleForRename(node)) || (implementations && IsSourceFile(node)))
        {
            return (new SymbolAndEntriesData { OriginalNode = node, Position = position }, false);
        }

        var entries = GetSymbolAndEntries(ctx, position, node, program, isRename, implementations);
        if (!implementations)
        {
            return (new SymbolAndEntriesData { OriginalNode = node, SymbolsAndEntries = entries, Position = position }, true);
        }

        var implementationEntries = new List<SymbolAndEntries>();
        var queue = new List<ReferenceEntry>();
        var seenNodes = new Set<Node>();
        var seenDefinitions = new Set<Symbol>();
        void AddToQueue(SymbolAndEntries[]? symbolAndEntries)
        {
            foreach (var s in symbolAndEntries ?? [])
            {
                var newReferences = new List<ReferenceEntry>();
                foreach (var @ref in s._references ?? [])
                {
                    if (seenNodes.AddIfAbsent(@ref._node!))
                    {
                        queue.Add(@ref);
                        newReferences.Add(@ref);
                    }
                }

                if (newReferences.Count > 0 || s._definition is null || seenDefinitions.AddIfAbsent(s._definition._symbol!))
                {
                    implementationEntries.Add(new SymbolAndEntries(s._definition, newReferences.ToArray()));
                }
            }
        }

        AddToQueue(entries);
        while (queue.Count != 0)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return (new SymbolAndEntriesData(), false);
            }

            var entry = queue[0];
            queue.RemoveAt(0);
            if (entry._node is not null)
            {
                AddToQueue(GetSymbolAndEntries(ctx, entry._node.Pos(), entry._node, program, isRename, implementations));
            }
        }

        return (new SymbolAndEntriesData { OriginalNode = node, SymbolsAndEntries = implementationEntries.ToArray(), Position = position }, true);
    }

    /// <summary>Go: <c>func (l *LanguageService) getSymbolAndEntries(ctx, position, node, program, isRename, implementations) []*SymbolAndEntries</c>（findallreferences.go:726-745）。</summary>
    internal SymbolAndEntries[]? GetSymbolAndEntries(Context ctx, int position, Node node, Program program, bool isRename, bool implementations)
    {
        RefOptions options;
        if (!isRename)
        {
            options = new RefOptions { _use = ReferenceUse.References };
            if (implementations)
            {
                options._implementations = true;
            }
        }
        else
        {
            options = new RefOptions
            {
                _use = ReferenceUse.Rename,
                _useAliasesForRename = UserPreferences().UseAliasesForRename.IsTrueOrUnknown(),
            };
        }

        return GetReferencedSymbolsForNode(ctx, position, node, program, program.GetSourceFiles(), options);
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideReferences(ctx, params, orchestrator) (lsproto.ReferencesResponse, error)</c>（findallreferences.go:747-759）。</summary>
    public (LocationsOrNull Response, Exception? Error) ProvideReferences(Context ctx, ReferenceParams @params, CrossProjectOrchestrator orchestrator)
    {
        return HandleCrossProject(
            ctx,
            @params,
            orchestrator,
            (ls, c, p, data, options) => ls.SymbolAndEntriesToReferences(c, p, data, options),
            Ls.CombineReferences,
            false, /*isRename*/
            false, /*implementations*/
            new SymbolEntryTransformOptions(),
            null, /*defaultProjectData*/
            (ls, c, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(c, uri, pos, rename, impl));
    }

    /// <summary>Go: <c>func (l *LanguageService) provideReferencesFromData(ctx, params, orchestrator, data) (lsproto.ReferencesResponse, error)</c>（findallreferences.go:761-773）。</summary>
    internal (LocationsOrNull Response, Exception? Error) ProvideReferencesFromData(Context ctx, ReferenceParams @params, CrossProjectOrchestrator orchestrator, SymbolAndEntriesData data)
    {
        return HandleCrossProject(
            ctx,
            @params,
            orchestrator,
            (ls, c, p, d, options) => ls.SymbolAndEntriesToReferences(c, p, d, options),
            Ls.CombineReferences,
            false, /*isRename*/
            false, /*implementations*/
            new SymbolEntryTransformOptions(),
            data,
            (ls, c, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(c, uri, pos, rename, impl));
    }

    /// <summary>Go: <c>func (l *LanguageService) symbolAndEntriesToReferences(ctx, params, data, options) (lsproto.ReferencesResponse, error)</c>（findallreferences.go:789-798）。</summary>
    internal (LocationsOrNull Response, Exception? Error) SymbolAndEntriesToReferences(Context ctx, ReferenceParams @params, SymbolAndEntriesData data, SymbolEntryTransformOptions options)
    {
        // `findReferencedSymbols` except only computes the information needed to return reference locations
        var locations = new List<Location>();
        var seenLocations = new Set<LocationKey>();
        foreach (var symbol in data.SymbolsAndEntries ?? [])
        {
            var symbolLocations = ConvertSymbolAndEntriesToLocations(symbol, @params.Context?.IncludeDeclaration ?? false, Feature.References);
            locations = Ls.CombineLocationArray(locations, symbolLocations, seenLocations);
        }

        return (new LocationsOrNull { Locations = locations.ToArray() }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideImplementations(ctx, params, orchestrator) (lsproto.ImplementationResponse, error)</c>（findallreferences.go:1016-1018）。</summary>
    public (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) ProvideImplementations(Context ctx, ImplementationParams @params, CrossProjectOrchestrator orchestrator)
    {
        return ProvideImplementationsEx(ctx, @params, new SymbolEntryTransformOptions(), orchestrator);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideImplementationsEx(ctx, params, options, orchestrator) (lsproto.ImplementationResponse, error)</c>（findallreferences.go:1020-1032）。</summary>
    internal (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) ProvideImplementationsEx(Context ctx, ImplementationParams @params, SymbolEntryTransformOptions options, CrossProjectOrchestrator orchestrator)
    {
        return HandleCrossProject(
            ctx,
            @params,
            orchestrator,
            (ls, c, p, data, opts) => ls.SymbolAndEntriesToImplementations(c, p, data, opts),
            Ls.CombineImplementations,
            false, /*isRename*/
            true, /*implementations*/
            options,
            null, /*defaultProjectData*/
            (ls, c, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(c, uri, pos, rename, impl));
    }

    /// <summary>Go: <c>func (l *LanguageService) provideImplementationsFromData(ctx, params, options, orchestrator, data) (lsproto.ImplementationResponse, error)</c>（findallreferences.go:1034-1046）。</summary>
    internal (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) ProvideImplementationsFromData(Context ctx, ImplementationParams @params, SymbolEntryTransformOptions options, CrossProjectOrchestrator orchestrator, SymbolAndEntriesData data)
    {
        return HandleCrossProject(
            ctx,
            @params,
            orchestrator,
            (ls, c, p, d, opts) => ls.SymbolAndEntriesToImplementations(c, p, d, opts),
            Ls.CombineImplementations,
            false, /*isRename*/
            true, /*implementations*/
            options,
            data,
            (ls, c, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(c, uri, pos, rename, impl));
    }

    /// <summary>Go: <c>func (l *LanguageService) symbolAndEntriesToImplementations(ctx, params, data, options) (lsproto.ImplementationResponse, error)</c>（findallreferences.go:1048-1066）。</summary>
    internal (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) SymbolAndEntriesToImplementations(Context ctx, ImplementationParams @params, SymbolAndEntriesData data, SymbolEntryTransformOptions options)
    {
        var seenNodes = new Set<Node>();
        var entries = new List<ReferenceEntry>();
        foreach (var entry in data.SymbolsAndEntries ?? [])
        {
            foreach (var @ref in entry._references ?? [])
            {
                if (seenNodes.AddIfAbsent(@ref._node!) && (!options._dropOriginNodes || !@ref._node!.Loc.ContainsInclusive(data.Position)))
                {
                    entries.Add(@ref);
                }
            }
        }

        if (!options._requireLocationsResult && LsProto.LsProto.GetClientCapabilities().TextDocument.Implementation.LinkSupport)
        {
            var links = ConvertEntriesToLocationLinks(entries.ToArray(), Feature.Implementation);
            return (new LocationOrLocationsOrDefinitionLinksOrNull { DefinitionLinks = links }, null);
        }

        var locations = ConvertEntriesToLocations(entries.ToArray(), Feature.Implementation);
        return (new LocationOrLocationsOrDefinitionLinksOrNull { Locations = locations }, null);
    }

    // == functions for conversions ==

    /// <summary>Go: <c>func (l *LanguageService) convertSymbolAndEntriesToLocations(s *SymbolAndEntries, includeDeclarations bool, feature spanmap.Feature) []lsproto.Location</c>（findallreferences.go:1068-1079）。</summary>
    internal Location[] ConvertSymbolAndEntriesToLocations(SymbolAndEntries s, bool includeDeclarations, Feature feature)
    {
        var references = s._references;

        // !!! includeDeclarations
        if (!includeDeclarations && s._definition is not null)
        {
            references = Filter(references, entry => !Ls.IsDeclarationOfSymbol(entry._node, s._definition._symbol));
        }

        return ConvertEntriesToLocations(references, feature);
    }

    /// <summary>Go: <c>func (l *LanguageService) convertEntriesToLocations(entries []*ReferenceEntry, feature spanmap.Feature) []lsproto.Location</c>（findallreferences.go:1105-1114）。</summary>
    internal Location[] ConvertEntriesToLocations(ReferenceEntry[]? entries, Feature feature)
    {
        var locations = new List<Location>();
        foreach (var entry in entries ?? [])
        {
            var (location, ok) = GetLocationOfEntryForFeature(entry, feature);
            if (ok)
            {
                locations.Add(location);
            }
        }

        return locations.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) convertEntriesToLocationLinks(entries []*ReferenceEntry, feature spanmap.Feature) []*lsproto.LocationLink</c>（findallreferences.go:1116-1147）。</summary>
    internal LocationLink[] ConvertEntriesToLocationLinks(ReferenceEntry[]? entries, Feature feature)
    {
        var links = new List<LocationLink>();
        foreach (var entry in entries ?? [])
        {
            // Get the selection range (the actual reference)
            var (loc, ok) = GetLocationOfEntryForFeature(entry, feature);
            if (!ok)
            {
                continue;
            }

            var targetSelectionRange = loc.Range;
            var targetRange = targetSelectionRange;

            // For entries with nodes, compute ranges directly from the node
            if (entry._node is not null)
            {
                // Get the context range (broader scope including declaration context)
                var contextTextRange = Ls.ToContextRange(entry._textRange, entry._sourceFile!, entry._context);
                if (contextTextRange is not null)
                {
                    var (contextLocation, fidelity) = SourceFileRangeToLSPLocationForFeature(entry._sourceFile!, contextTextRange.Value, feature);
                    if (!fidelity.IsNone() && contextLocation.Uri == loc.Uri)
                    {
                        targetRange = contextLocation.Range;
                    }
                }
            }

            links.Add(new LocationLink
            {
                TargetUri = FileNameToDocumentURI(entry._sourceFile!.OriginalFileName()),
                TargetRange = targetRange,
                TargetSelectionRange = targetSelectionRange,
            });
        }

        return links.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) mergeReferences(program *compiler.Program, referencesToMerge ...[]*SymbolAndEntries) []*SymbolAndEntries</c>（findallreferences.go:1149-1200）。</summary>
    internal SymbolAndEntries[] MergeReferences(Program program, params SymbolAndEntries[]?[] referencesToMerge)
    {
        var result = new List<SymbolAndEntries>();
        int GetSourceFileIndexOfEntry(ReferenceEntry entry)
        {
            ResolveEntrySource(entry);
            return Array.IndexOf(program.GetSourceFiles(), entry._sourceFile);
        }

        foreach (var references in referencesToMerge)
        {
            if ((references?.Length ?? 0) == 0)
            {
                continue;
            }

            if (result.Count == 0)
            {
                result.AddRange(references!);
                continue;
            }

            foreach (var entry in references!)
            {
                if (entry._definition is null || entry._definition.Kind != DefinitionKind.Symbol)
                {
                    result.Add(entry);
                    continue;
                }

                var symbol = entry._definition._symbol;
                var refIndex = FindIndex(result.ToArray(), @ref => @ref._definition is not null &&
                    @ref._definition.Kind == DefinitionKind.Symbol &&
                    @ref._definition._symbol == symbol);
                if (refIndex == -1)
                {
                    result.Add(entry);
                    continue;
                }

                var reference = result[refIndex];
                // `slices.SortStableFunc` → LINQ `OrderBy`（同样是稳定排序）。
                var sortedRefs = (reference._references ?? []).Concat(entry._references ?? [])
                    .OrderBy(e => e, Comparer<ReferenceEntry>.Create((entry1, entry2) =>
                    {
                        var entry1File = GetSourceFileIndexOfEntry(entry1);
                        var entry2File = GetSourceFileIndexOfEntry(entry2);
                        if (entry1File != entry2File)
                        {
                            return entry1File.CompareTo(entry2File);
                        }

                        return LsProto.LsProto.CompareRanges(GetRangeOfEntry(entry1), GetRangeOfEntry(entry2));
                    }))
                    .ToArray();
                result[refIndex] = new SymbolAndEntries(reference._definition, sortedRefs);
            }
        }

        return result.ToArray();
    }

    /// <summary>Go: <c>func (l *LanguageService) GetReferencedSymbolsForNode(ctx, position, node, sourceFiles) []*SymbolAndEntries</c>（findallreferences.go:1202-1208）。</summary>
    /// <remarks>`SymbolAndEntries` 在 C# 侧是 `internal`（只在 ls/lsp 装配件内使用）⇒ 本方法同 `internal`（Go 的"导出"只跨文件，不跨包）。</remarks>
    internal SymbolAndEntries[]? GetReferencedSymbolsForNode(Context ctx, int position, Node node, SourceFile[]? sourceFiles)
    {
        return GetReferencedSymbolsForNode(
            ctx,
            position,
            node,
            _program!,
            sourceFiles ?? [],
            new RefOptions { _use = ReferenceUse.References });
    }

    /// <summary>Go: <c>func (l *LanguageService) GetSignatureUsages(ctx, signatureDecl) []SignatureUsage</c>（findallreferences.go:1218-1267）。</summary>
    /// <remarks>`SignatureUsage` 在 C# 侧是 `internal` ⇒ 本方法同 `internal`（同 `GetReferencedSymbolsForNode` 的说明）。</remarks>
    internal SignatureUsage[]? GetSignatureUsages(Context ctx, Node signatureDecl)
    {
        var name = signatureDecl.Name();
        if (name is null || !IsIdentifier(name))
        {
            return null;
        }

        var sourceFiles = _program!.GetSourceFiles();
        var entries = GetReferencedSymbolsForNode(ctx, name.Pos(), name, sourceFiles);

        // Collect all declaration name nodes for the target symbol so we can
        // filter them out — the caller wants usages, not declarations.
        var declNames = new Set<Node>();
        foreach (var entry in entries ?? [])
        {
            if (entry._definition?._symbol is { } symbol)
            {
                foreach (var decl in symbol.Declarations ?? [])
                {
                    if (decl.Name() is { } n)
                    {
                        declNames.Add(n);
                    }
                }
            }
        }

        var result = new List<SignatureUsage>();
        foreach (var entry in entries ?? [])
        {
            foreach (var @ref in entry.References() ?? [])
            {
                if (!@ref.IsNodeEntry())
                {
                    continue;
                }

                var node = @ref.Node();
                if (node is null || declNames.Has(node))
                {
                    continue;
                }

                var called = ClimbPastPropertyAccess(node)!;

                Node? callExpr = null;
                var calledParent = called.Parent;
                if (calledParent is not null && IsCallExpression(calledParent) && calledParent.Expression() == called)
                {
                    callExpr = calledParent;
                }

                result.Add(new SignatureUsage { _name = node, _call = callExpr });
            }
        }

        return result.ToArray();
    }

    // === functions for find all ref implementation ===

    /// <summary>Go: <c>func (l *LanguageService) getReferencedSymbolsForNode(ctx, position, node, program, sourceFiles, options) []*SymbolAndEntries</c>（findallreferences.go:1269-1352）。</summary>
    internal SymbolAndEntries[]? GetReferencedSymbolsForNode(Context ctx, int position, Node node, Program program, SourceFile[]? sourceFiles, RefOptions options)
    {
        // !!! cancellationToken
        var sourceFilesSet = new Set<string>(sourceFiles?.Length ?? 0);
        foreach (var file in sourceFiles ?? [])
        {
            sourceFilesSet.Add(file.FileName());
        }

        if (options._use == ReferenceUse.References || options._use == ReferenceUse.Rename)
        {
            node = Ls.GetAdjustedLocation(node, options._use == ReferenceUse.Rename, GetSourceFileOfNode(node))!;
        }

        var (checker, done) = program.GetTypeChecker(ctx);
        try
        {
            if (node.Kind == Kind.SourceFile)
            {
                var resolvedRef = Ls.GetReferenceAtPosition(node.AsSourceFile()!, position, program);
                if (resolvedRef is null || resolvedRef._file is null)
                {
                    return null;
                }

                if (checker.GetMergedSymbol(resolvedRef._file.Symbol) is { } moduleSymbol)
                {
                    return GetReferencedSymbolsForModule(ctx, program, moduleSymbol /*excludeImportTypeOfExportEquals*/, false, sourceFiles, sourceFilesSet);
                }

                // !!! not implemented
                // fileIncludeReasons := program.getFileIncludeReasons();
                // if (!fileIncludeReasons) {
                // 	return nil
                // }
                return
                [
                    new SymbolAndEntries(
                        new Definition
                        {
                            Kind = DefinitionKind.TripleSlashReference,
                            _tripleSlashFileRef = new TripleSlashDefinition { _reference = resolvedRef._reference },
                        },
                        Ls.GetReferencesForNonModule(resolvedRef._file, program /*fileIncludeReasons,*/)),
                ];
            }

            if (!options._implementations)
            {
                // !!! cancellationToken
                if (Ls.GetReferencedSymbolsSpecial(node, sourceFiles) is { } special)
                {
                    return special;
                }
            }

            // constructors should use the class symbol, detected by name, if present
            var symbol = checker.GetSymbolAtLocation(IfElse(node.Kind == Kind.Constructor && node.Parent!.Name() is not null, node.Parent!.Name(), node)!);

            // Could not find a symbol e.g. unknown identifier
            if (symbol is null)
            {
                // String literal might be a property (and thus have a symbol), so do this here rather than in getReferencedSymbolsSpecial.
                if (!options._implementations && IsStringLiteralLike(node))
                {
                    if (Ls.IsModuleSpecifierLike(node))
                    {
                        // !!! not implemented
                        // fileIncludeReasons := program.GetFileIncludeReasons()
                        // if referencedFile := program.GetResolvedModuleFromModuleSpecifier(node, nil /*sourceFile*/); referencedFile != nil {
                        // return []*SymbolAndEntries{{
                        // 	definition: &Definition{Kind: definitionKindString, node: node},
                        // 	references: getReferencesForNonModule(referencedFile, program /*fileIncludeReasons,*/),
                        // }}
                        // }
                        // Fall through to string literal references. This is not very likely to return
                        // anything useful, but I guess it's better than nothing, and there's an existing
                        // test that expects this to happen (fourslash/cases/untypedModuleImport.ts).
                    }

                    return GetReferencesForStringLiteral(ctx, node, sourceFiles, checker);
                }

                return null;
            }

            if (symbol.Name == InternalSymbolNameExportEquals)
            {
                if (symbol.Parent is null)
                {
                    return null;
                }

                return GetReferencedSymbolsForModule(ctx, program, symbol.Parent, false /*excludeImportTypeOfExportEquals*/, sourceFiles, sourceFilesSet);
            }

            var moduleReferences = GetReferencedSymbolsForModuleIfDeclaredBySourceFile(ctx, symbol, program, sourceFiles, checker, options, sourceFilesSet);
            if (moduleReferences is not null && (symbol.Flags & SymbolFlags.Transient) == 0)
            {
                return moduleReferences;
            }

            var aliasedSymbol = Ls.GetMergedAliasedSymbolOfNamespaceExportDeclaration(node, symbol, checker);
            var moduleReferencesOfExportTarget = GetReferencedSymbolsForModuleIfDeclaredBySourceFile(ctx, aliasedSymbol, program, sourceFiles, checker, options, sourceFilesSet);

            var references = Ls.GetReferencedSymbolsForSymbol(ctx, program, symbol, node, sourceFiles, sourceFilesSet, checker, options);
            return MergeReferences(program, [moduleReferences, references, moduleReferencesOfExportTarget]);
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) getReferencesForStringLiteral(ctx, node *ast.StringLiteralLike, sourceFiles, checker) []*SymbolAndEntries</c>（findallreferences.go:1354-1392）。</summary>
    internal SymbolAndEntries[] GetReferencesForStringLiteral(Context ctx, Node node, SourceFile[]? sourceFiles, TypeScript.Checker.Checker checker)
    {
        var t = Ls.GetContextualTypeFromParentOrAncestorTypeNode(node, checker);
        var references = FlatMap(sourceFiles, sourceFile =>
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return [];
            }

            var entries = new List<ReferenceEntry>();
            var possibleReferences = Ls.GetPossibleSymbolReferenceNodes(sourceFile, node.Text(), null /*container*/);
            foreach (var @ref in possibleReferences)
            {
                if (IsStringLiteralLike(@ref) && @ref.Text() == node.Text())
                {
                    if (t is not null)
                    {
                        var refType = Ls.GetContextualTypeFromParentOrAncestorTypeNode(@ref, checker);
                        if (t != checker.GetStringType() &&
                            (t == refType || Ls.IsStringLiteralPropertyReference(@ref, checker)))
                        {
                            entries.Add(Ls.NewNodeEntryWithKind(@ref, EntryKind.StringLiteral));
                        }
                    }
                    else
                    {
                        if (Ls.IsNoSubstitutionTemplateLiteral(@ref) && !TypeScript.Printer.Printer.RangeIsOnSingleLine(@ref.Loc, sourceFile))
                        {
                            continue;
                        }

                        entries.Add(Ls.NewNodeEntryWithKind(@ref, EntryKind.StringLiteral));
                    }
                }
            }

            return entries.ToArray();
        });

        return
        [
            new SymbolAndEntries(
                new Definition { Kind = DefinitionKind.String, _node = node },
                references),
        ];
    }

    /// <summary>Go: <c>func (l *LanguageService) getReferencedSymbolsForModuleIfDeclaredBySourceFile(ctx, symbol, program, sourceFiles, checker, options, sourceFilesSet) []*SymbolAndEntries</c>（findallreferences.go:1401-1419）。</summary>
    internal SymbolAndEntries[]? GetReferencedSymbolsForModuleIfDeclaredBySourceFile(
        Context ctx,
        Symbol? symbol,
        Program program,
        SourceFile[]? sourceFiles,
        TypeScript.Checker.Checker checker,
        RefOptions options,
        Set<string> sourceFilesSet)
    {
        var moduleSourceFileName = "";
        if (symbol is null || !((symbol.Flags & SymbolFlags.Module) != 0 && (symbol.Declarations?.Length ?? 0) != 0))
        {
            return null;
        }

        if (Find(symbol.Declarations, IsSourceFile) is { } moduleSourceFile)
        {
            moduleSourceFileName = moduleSourceFile.AsSourceFile()!.FileName();
        }
        else
        {
            return null;
        }

        var exportEquals = symbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals);
        // If exportEquals != nil, we're about to add references to `import("mod")` anyway, so don't double-count them.
        var moduleReferences = GetReferencedSymbolsForModule(ctx, program, symbol, exportEquals is not null, sourceFiles, sourceFilesSet);
        if (exportEquals is null || (exportEquals.Flags & SymbolFlags.Alias) == 0 || !sourceFilesSet.Has(moduleSourceFileName))
        {
            return moduleReferences;
        }

        checker.ResolveAlias(exportEquals, out var resolvedSymbol);
        return MergeReferences(
            program,
            [moduleReferences, Ls.GetReferencedSymbolsForSymbol(ctx, program, resolvedSymbol!, null /*node*/, sourceFiles, sourceFilesSet, checker /*, cancellationToken*/, options)]);
    }

    /// <summary>Go: <c>func (l *LanguageService) getReferencedSymbolsForModule(ctx, program, symbol, excludeImportTypeOfExportEquals, sourceFiles, sourceFilesSet) []*SymbolAndEntries</c>（findallreferences.go:1735-1836）。</summary>
    internal SymbolAndEntries[] GetReferencedSymbolsForModule(
        Context ctx,
        Program program,
        Symbol symbol,
        bool excludeImportTypeOfExportEquals,
        SourceFile[]? sourceFiles,
        Set<string> sourceFilesSet)
    {
        Debug.Assert(symbol.ValueDeclaration is not null, "Expected module symbol to have a value declaration");

        var (checker, done) = program.GetTypeChecker(ctx);
        try
        {
            var moduleRefs = Ls.FindModuleReferences(program, sourceFiles, symbol, checker);
            var references = new List<ReferenceEntry>();
            foreach (var reference in moduleRefs)
            {
                switch (reference._kind)
                {
                    case ModuleReferenceKind.Import:
                        var parent = reference._literal!.Parent!;
                        if (IsLiteralTypeNode(parent))
                        {
                            var importType = parent.Parent!;
                            if (IsImportTypeNode(importType))
                            {
                                var importTypeNode = importType.AsImportTypeNode()!;
                                if (excludeImportTypeOfExportEquals && importTypeNode.Qualifier is null)
                                {
                                    continue;
                                }
                            }
                        }

                        // import("foo") with no qualifier will reference the `export =` of the module, which may be referenced anyway.
                        references.Add(Ls.NewNodeEntry(reference._literal));
                        break;
                    case ModuleReferenceKind.Implicit:
                        // For implicit references (e.g., JSX runtime imports), return the first JSX node,
                        // the first statement, or the whole file
                        Node? rangeNode = null;

                        // Skip the JSX search for tslib imports
                        if (reference._literal!.Text() != "tslib")
                        {
                            rangeNode = Ls.FindFirstJsxNode(reference._referencingFile!.AsNode());
                        }

                        if (rangeNode is null)
                        {
                            if ((reference._referencingFile!.Statements()?.Length ?? 0) > 0)
                            {
                                rangeNode = reference._referencingFile.Statements()![0];
                            }
                            else
                            {
                                rangeNode = reference._referencingFile.AsNode();
                            }
                        }

                        references.Add(Ls.NewNodeEntry(rangeNode));
                        break;
                    case ModuleReferenceKind.Reference:
                        references.Add(new ReferenceEntry
                        {
                            _kind = EntryKind.Range,
                            _sourceFile = reference._referencingFile,
                            _textRange = reference._ref!.Range,
                        });
                        break;
                }
            }

            // Add references to the module declarations themselves
            if ((symbol.Declarations?.Length ?? 0) > 0)
            {
                foreach (var decl in symbol.Declarations!)
                {
                    switch (decl.Kind)
                    {
                        case Kind.SourceFile:
                            // Don't include the source file itself. (This may not be ideal behavior, but awkward to include an entire file as a reference.)
                            continue;
                        case Kind.ModuleDeclaration:
                            if (sourceFilesSet.Has(GetSourceFileOfNode(decl)!.FileName()))
                            {
                                references.Add(Ls.NewNodeEntry(decl.AsModuleDeclaration()!.Name()!));
                            }

                            break;
                        default:
                            // This may be merged with something (e.g. a class merged with a namespace).
                            continue;
                    }
                }
            }

            // Handle export equals declarations
            var exported = symbol.Exports?.GetValueOrDefault(InternalSymbolNameExportEquals);
            if (exported is not null && (exported.Declarations?.Length ?? 0) > 0)
            {
                foreach (var decl in exported.Declarations!)
                {
                    var sourceFile = GetSourceFileOfNode(decl)!;
                    if (sourceFilesSet.Has(sourceFile.FileName()))
                    {
                        Node? node;
                        // At `module.exports = ...`, reference node is `module`
                        if (IsBinaryExpression(decl) && IsPropertyAccessExpression(decl.AsBinaryExpression()!.Left!))
                        {
                            node = decl.AsBinaryExpression()!.Left!.Expression();
                        }
                        else if (IsExportAssignment(decl))
                        {
                            // Find the export keyword
                            node = TypeScript.AstNav.AstNav.FindChildOfKind(decl, Kind.ExportKeyword, sourceFile);
                            Debug.Assert(node is not null, "Expected to find export keyword");
                        }
                        else
                        {
                            node = GetNameOfDeclaration(decl);
                            node ??= decl;
                        }

                        references.Add(Ls.NewNodeEntry(node!));
                    }
                }
            }

            if (references.Count > 0)
            {
                return
                [
                    new SymbolAndEntries(
                        new Definition { Kind = DefinitionKind.Symbol, _symbol = symbol },
                        references.ToArray()),
                ];
            }

            return [];
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideVSReferences(ctx, params, orchestrator) (lsproto.VSReferencesResponse, error)</c>（findallreferences.go:775-787）。</summary>
    internal (VSReferenceItemsOrNull Response, Exception? Error) ProvideVSReferences(Context ctx, ReferenceParams @params, CrossProjectOrchestrator orchestrator)
    {
        return HandleCrossProject(
            ctx,
            @params,
            orchestrator,
            (ls, c, p, data, options) => ls.SymbolAndEntriesToVSReferences(c, p, data, options),
            Ls.CombineVSReferences,
            false, /*isRename*/
            false, /*implementations*/
            new SymbolEntryTransformOptions(),
            null, /*defaultProjectData*/
            (ls, c, uri, pos, rename, impl) => ls.ProvideSymbolsAndEntries(c, uri, pos, rename, impl));
    }

    /// <summary>Go: <c>func (l *LanguageService) symbolAndEntriesToVSReferences(ctx, params, data, options) (lsproto.VSReferencesResponse, error)</c>（findallreferences.go:800-864）。</summary>
    internal (VSReferenceItemsOrNull Response, Exception? Error) SymbolAndEntriesToVSReferences(Context ctx, ReferenceParams @params, SymbolAndEntriesData data, SymbolEntryTransformOptions options)
    {
        var caps = LsProto.LsProto.GetClientCapabilities();
        var vsCapability = caps.VSSupportsVisualStudioExtensions;
        var items = new List<VSReferenceItem>();
        var id = 0;
        var projectName = _projectID.String();

        foreach (var s in data.SymbolsAndEntries ?? [])
        {
            if (s._definition is null)
            {
                continue;
            }

            // Convert definition to info
            var defInfo = DefinitionToReferencedSymbolDefinitionInfo(ctx, s._definition, data.OriginalNode, vsCapability, Feature.References);
            if (defInfo is null)
            {
                continue;
            }

            // Create the definition item
            var definitionId = id;
            var emptyStr = "";
            var defItem = new VSReferenceItem
            {
                VSId = definitionId,
                VSLocation = defInfo._location,
                VSDefinitionText = defInfo._displayText,
                VSKind = [VSReferenceKind.Unknown],
                VSProjectName = projectName,
                VSContainingType = emptyStr,
            };
            items.Add(defItem);
            id++;

            // Create reference items grouped under the definition
            foreach (var @ref in s._references ?? [])
            {
                // Skip the declaration itself (already represented by the definition item)
                if (s._definition._symbol is not null && Ls.IsDeclarationOfSymbol(@ref._node, s._definition._symbol))
                {
                    continue;
                }

                var (refLocation, ok) = GetLocationOfEntryForFeature(@ref, Feature.References);
                if (!ok)
                {
                    continue;
                }

                // Determine read/write kind
                var kind = VSReferenceKind.Read;
                if (@ref._kind != EntryKind.Range && @ref._node is not null && IsWriteAccessForReference(@ref._node))
                {
                    kind = VSReferenceKind.Write;
                }

                var refItem = new VSReferenceItem
                {
                    VSId = id,
                    VSDefinitionId = definitionId,
                    VSLocation = refLocation,
                    VSKind = [kind],
                    VSProjectName = projectName,
                };
                items.Add(refItem);
                id++;
            }
        }

        return (new VSReferenceItemsOrNull { VSReferenceItems = items.ToArray() }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) definitionToReferencedSymbolDefinitionInfo(ctx, def *Definition, originalNode *ast.Node, vsCapability bool, feature spanmap.Feature) *referencedSymbolDefinitionInfo</c>（findallreferences.go:873-995）。</summary>
    internal ReferencedSymbolDefinitionInfo? DefinitionToReferencedSymbolDefinitionInfo(Context ctx, Definition def, Node? originalNode, bool vsCapability, Feature feature)
    {
        switch (def.Kind)
        {
            case DefinitionKind.Symbol:
                var symbol = def._symbol;
                if (symbol is null)
                {
                    return null;
                }

                // Get display parts
                var element = GetDefinitionKindAndDisplayParts(ctx, symbol, originalNode, vsCapability);

                // Get the definition node
                Node? node;
                if ((symbol.Declarations?.Length ?? 0) > 0)
                {
                    var decl = symbol.Declarations![0];
                    node = OrElse(decl.Name(), decl);
                }
                else
                {
                    node = originalNode;
                }

                var (loc, ok) = GetLocationOfEntryForFeature(new ReferenceEntry { _kind = EntryKind.Node, _node = node }, feature);
                if (!ok)
                {
                    return null;
                }

                return new ReferencedSymbolDefinitionInfo { _node = node!, _location = loc, _displayText = element };

            case DefinitionKind.Label:
                var labelNode = def._node;
                if (labelNode is null)
                {
                    return null;
                }

                var (labelLoc, labelOk) = GetLocationOfEntryForFeature(new ReferenceEntry { _kind = EntryKind.Node, _node = labelNode }, feature);
                if (!labelOk)
                {
                    return null;
                }

                return new ReferencedSymbolDefinitionInfo
                {
                    _node = labelNode,
                    _location = labelLoc,
                    _displayText = new VSClassifiedTextElement
                    {
                        Runs = [new VSClassifiedTextRun { Text = labelNode.Text(), ClassificationTypeName = ClassificationTypeName.Text.Value }],
                    },
                };

            case DefinitionKind.Keyword:
                var keywordNode = def._node;
                if (keywordNode is null)
                {
                    return null;
                }

                var name = TokenToString(keywordNode.Kind);
                var (keywordLoc, keywordOk) = GetLocationOfEntryForFeature(new ReferenceEntry { _kind = EntryKind.Node, _node = keywordNode }, feature);
                if (!keywordOk)
                {
                    return null;
                }

                return new ReferencedSymbolDefinitionInfo
                {
                    _node = keywordNode,
                    _location = keywordLoc,
                    _displayText = new VSClassifiedTextElement
                    {
                        Runs = [new VSClassifiedTextRun { Text = name, ClassificationTypeName = ClassificationTypeName.Keyword.Value }],
                    },
                };

            case DefinitionKind.This:
                var thisNode = def._node;
                if (thisNode is null)
                {
                    return null;
                }

                var thisSymbol = def._symbol;
                if (thisSymbol is null)
                {
                    return null;
                }

                var thisElement = GetDefinitionKindAndDisplayParts(ctx, thisSymbol, thisNode, vsCapability);
                var (thisLoc, thisOk) = GetLocationOfEntryForFeature(new ReferenceEntry { _kind = EntryKind.Node, _node = thisNode }, feature);
                if (!thisOk)
                {
                    return null;
                }

                return new ReferencedSymbolDefinitionInfo { _node = thisNode, _location = thisLoc, _displayText = thisElement };

            case DefinitionKind.String:
                var stringNode = def._node;
                if (stringNode is null)
                {
                    return null;
                }

                var (stringLoc, stringOk) = GetLocationOfEntryForFeature(new ReferenceEntry { _kind = EntryKind.Node, _node = stringNode }, feature);
                if (!stringOk)
                {
                    return null;
                }

                return new ReferencedSymbolDefinitionInfo
                {
                    _node = stringNode,
                    _location = stringLoc,
                    _displayText = new VSClassifiedTextElement
                    {
                        Runs = [new VSClassifiedTextRun { Text = stringNode.Text(), ClassificationTypeName = ClassificationTypeName.String.Value }],
                    },
                };

            case DefinitionKind.TripleSlashReference:
                if (def._tripleSlashFileRef?._file is null)
                {
                    return null;
                }

                var tripleNode = def._tripleSlashFileRef._file.AsNode();
                var (tripleLoc, tripleOk) = GetLocationOfEntryForFeature(new ReferenceEntry { _kind = EntryKind.Node, _node = tripleNode }, feature);
                if (!tripleOk)
                {
                    return null;
                }

                return new ReferencedSymbolDefinitionInfo
                {
                    _node = tripleNode,
                    _location = tripleLoc,
                    _displayText = new VSClassifiedTextElement
                    {
                        Runs =
                        [
                            new VSClassifiedTextRun
                            {
                                Text = "\"" + def._tripleSlashFileRef._reference!.FileName + "\"",
                                ClassificationTypeName = ClassificationTypeName.String.Value,
                            },
                        ],
                    },
                };

            default:
                return null;
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) getDefinitionKindAndDisplayParts(ctx, symbol *ast.Symbol, originalNode *ast.Node, vsCapability bool) *lsproto.VSClassifiedTextElement</c>（findallreferences.go:997-1014）。</summary>
    internal VSClassifiedTextElement GetDefinitionKindAndDisplayParts(Context ctx, Symbol symbol, Node? originalNode, bool vsCapability)
    {
        var program = GetProgram()!;
        var (c, done) = program.GetTypeChecker(ctx);
        try
        {
            var meaning = Ls.GetIntersectingMeaningFromDeclarations(originalNode, symbol, SemanticMeaning.All);

            var info = Ls.GetQuickInfoAndDeclarationAtLocation(c, symbol, originalNode!, null, vsCapability, meaning);

            if (vsCapability)
            {
                return new VSClassifiedTextElement { Runs = info._displayParts.GetRuns() };
            }

            // Fallback: single unclassified run with the full text
            var text = info._displayParts.String();
            return new VSClassifiedTextElement
            {
                Runs = [new VSClassifiedTextRun { Text = text, ClassificationTypeName = ClassificationTypeName.Text.Value }],
            };
        }
        finally
        {
            done();
        }
    }
}
