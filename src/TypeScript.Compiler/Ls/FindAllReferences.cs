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
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Ls.Ls;
using static TypeScript.LsConv.LsConv;
using static TypeScript.TSPath.TSPath;
// 同名类型消歧（`using TypeScript.SpanMap` 带来的 `SpanMap.Kind`；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;

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
}
