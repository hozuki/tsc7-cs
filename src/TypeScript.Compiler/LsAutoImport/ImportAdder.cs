// ported from: internal/ls/autoimport/import_adder.go
//
// 与 Go 的偏离（逐条）：
// 1) 未导出类型 `importAdder` → C# 习惯命名 `ImportAdderImpl`（实现同名导出接口 `ImportAdder`）；未导出类型
//    `addToExistingState`/`importsCollection` → `AddToExistingState`/`ImportsCollection`；未导出字段一律 `_camelCase`
//    （import_adder.go 里的字段在 Go 里全是未导出的）。
// 2) 包级函数（含未导出的 `newImportsKey`/`sortedNamedImports`/`reduceAddAsTypeOnlyValues`/`importSymbols`/
//    `getNameForExportedSymbol`/`replaceFirstIdentifierOfEntityName`）→ `public static partial class AutoImport` 上的
//    static 方法（§2）；未导出的名字按 "internal + PascalCase"。
// 3) `ImportAdder` 是导出接口 ⇒ 成员名照抄：`HasFixes()`/`AddImportFromExportedSymbol()`/`AddImportFix()`/`Edits()`；
//    `Edits()` 返回 `lsproto.TextEdit[]`。
// 4) Go 的 `NewImportAdder(ctx, program, checker, file, view, formatOptions, converters, preferences)`：Go 自己也没用
//    `program`/`file` 两个参数（只有 `checker`/`view`/`formatOptions`/`converters`/`preferences` 被存进 struct），
//    照抄保留签名（不删参数）。
// 5) `idToSymbol` 的键类型：Go 是 `map[*ast.IdentifierNode]*ast.Symbol`，但 C# 侧 `Checker.TypeToTypeNode` 的入参是
//    `Dictionary<Node, Symbol>?`（Checker/Printer.cs 的既有形态，Go 那边 `getNodeBuilderEx` 也是同一个映射）⇒
//    本文件的 `idToSymbol` 一律 `Dictionary<Node, Symbol>`（`Identifier` 是 `Node` 的子类，按键查表行为一致）。
// 6) `nodebuilder.FlagsNone` → `TypeScript.NodeBuilder.Flags.None`（NodeBuilder/Types.cs 的 [Flags] enum）。
// 7) `ast.NewNodeFactory(ast.NodeFactoryHooks{})` → `NewNodeFactory(new NodeFactoryHooks())`；
//    `ast.NewNodeVisitor(visit, factory, ast.NodeVisitorHooks{})` → `NewNodeVisitor(Visit, factory, new NodeVisitorHooks())`。
//    闭包自引用（Go 的 `var visitor *ast.NodeVisitor` + 闭包互相引用）→ C# 用**局部函数** `Visit` + 可空局部变量
//    `visitor`（先声明后赋值），语义与 Go 一致。
// 8) `slices.Sorted(maps.Keys(m))` → 取 `Keys` 后 `Array.Sort(keys, StringComparer.Ordinal)`（Go 的字符串比较就是
//    逐字节字典序）；`slices.SortFunc(fixes, cmp)` → `Array.Sort(fixes, cmp)`；`core.FlatMap` → `FlatMap`。
// 9) Go 的 `map[K]*V` 零值是 nil ⇒ C# 用可空 `Dictionary<K, V>?` 表达"还没建过这张表"（`entry.namedImports == nil`
//    的判断照写）；`reduceAddAsTypeOnlyValues` 的枚举比较要显式转 `int`（C# 不允许对 enum 用 `>`）；
//    `compilerOptions.VerbatimModuleSyntax == core.TSTrue` → `== Tristate.TSTrue`（`core.TSTrue` 在 C# 里是枚举成员
//    `Tristate.TSTrue`，`using static TypeScript.Core.Core` 带不进来）。
// 10) `debug.Assert` → `Debug.Assert`；`debug.Fail(fmt.Sprintf(...))` → `Debug.Fail("... " + fix.Kind)` +
//     `throw new System.Diagnostics.UnreachableException();`（`Debug.Fail` 没有 `[DoesNotReturn]`）。
// 11) `locale.Default` → `Loc.Default`（`TypeScript.Locale.Locale`；`Locale` 既是命名空间又是类型 ⇒ 别名 `Loc`）。
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsChange;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.LsUtil.LsUtil;
using Loc = TypeScript.Locale.Locale;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type ImportAdder interface</c>（import_adder.go:24-29）。</summary>
public interface ImportAdder
{
    bool HasFixes();

    void AddImportFromExportedSymbol(Symbol symbol, bool isValidTypeOnlyUseSite);

    void AddImportFix(Fix fix);

    TextEdit[] Edits();
}

/// <summary>Go: <c>type addToExistingState struct</c>（import_adder.go:31-36）：对一个已存在的 import clause 或
/// binding pattern 的累积修改。</summary>
internal sealed class AddToExistingState
{
    internal Node? _importClauseOrBindingPattern;

    internal NewImportBinding? _defaultImport;

    internal Dictionary<string, NewImportBinding> _namedImports = new();
}

/// <summary>Go: <c>type importsCollection struct</c>（import_adder.go:38-44）：为某个 module specifier 要新建的
/// import。</summary>
internal sealed class ImportsCollection
{
    internal NewImportBinding? _defaultImport;

    internal Dictionary<string, NewImportBinding>? _namedImports;

    internal NewImportBinding? _namespaceLikeImport;

    internal bool _useRequire;
}

/// <summary>Go: <c>type importAdder struct</c>（import_adder.go:52-72）。</summary>
internal sealed class ImportAdderImpl : ImportAdder
{
    // Context
    internal Context _ctx;

    internal TypeScript.Checker.Checker _checker;

    internal View _view;

    internal FormatCodeSettings _formatOptions;

    internal Converters _converters;

    internal UserPreferences _preferences;

    // State
    /// <summary>Namespace fixes don't conflict, so just build a list。</summary>
    internal List<Fix> _addToNamespace = [];

    /// <summary>JSDoc type import fixes。</summary>
    internal List<Fix> _importType = [];

    /// <summary>importClauseOrBindingPattern -> default or named bindings。</summary>
    internal Dictionary<Node, AddToExistingState> _addToExisting = new();

    /// <summary>module specifier + type only -> imports。</summary>
    internal Dictionary<string, ImportsCollection> _newImports = new();

    // !!! removeExisting, verbatimImports?

    /// <summary>Go 的结构体字面量 `&importAdder{...}`（只有 <see cref="AutoImport.NewImportAdder"/> 会构造它）。</summary>
    internal ImportAdderImpl(
        Context ctx,
        TypeScript.Checker.Checker checker,
        View view,
        FormatCodeSettings formatOptions,
        Converters converters,
        UserPreferences preferences)
    {
        _ctx = ctx;
        _checker = checker;
        _view = view;
        _formatOptions = formatOptions;
        _converters = converters;
        _preferences = preferences;
    }

    /// <summary>Go: <c>func (adder *importAdder) HasFixes() bool</c>（import_adder.go:84-89）。</summary>
    public bool HasFixes() =>
        _addToNamespace.Count > 0 ||
        _importType.Count > 0 ||
        _addToExisting.Count > 0 ||
        _newImports.Count > 0;

    /// <summary>Go: <c>func (adder *importAdder) AddImportFromExportedSymbol(exportedSymbol *ast.Symbol, isValidTypeOnlyUseSite bool)</c>（import_adder.go:92-104）。
    /// !!! referenceImport</summary>
    public void AddImportFromExportedSymbol(Symbol exportedSymbol, bool isValidTypeOnlyUseSite)
    {
        var symbol = _checker.GetMergedSymbol(_checker.SkipAlias(exportedSymbol));
        var exportInfos = GetAllExportsForSymbol(symbol!);
        if (exportInfos.Length == 0)
        {
            // If no exportInfo is found, this means export could not be resolved when we have filtered for autoImportFileExcludePatterns,
            //     so we should not generate an import.
            // debug.Assert(len(adder.ls.UserPreferences().AutoImportFileExcludePatterns) > 0)
            return;
        }
        var fix = GetImportFixForSymbol(_view, exportInfos, isValidTypeOnlyUseSite);
        if (fix is not null)
        {
            // !!! referenceImport -> propertyName
            AddImportFix(fix);
        }
    }

    /// <summary>Go: <c>func (adder *importAdder) Edits() []*lsproto.TextEdit</c>（import_adder.go:106-168）。</summary>
    public TextEdit[] Edits()
    {
        // !!! organize imports?
        var tracker = Tracker.NewTracker(_ctx, _view._program.Options(), _formatOptions, _converters);
        var quotePreference = GetQuotePreference(_view._importingFile, _preferences);
        foreach (var fix in _addToNamespace)
        {
            AutoImport.AddNamespaceQualifier(fix, tracker, _view._importingFile, Loc.Default);
        }
        foreach (var fix in _importType)
        {
            AutoImport.AddImportType(fix, _view._importingFile, _preferences, tracker, Loc.Default);
        }
        foreach (var (clauseOrPattern, entry) in _addToExisting)
        {
            AutoImport.AddToExistingImport(
                tracker,
                _view._importingFile,
                clauseOrPattern,
                entry._defaultImport,
                AutoImport.SortedNamedImports(entry._namedImports),
                _preferences);
        }

        List<Node> newDeclarations = [];
        foreach (var (key, newImport) in _newImports)
        {
            var moduleSpecifier = key[2..]; // From `${0 | 1}|${moduleSpecifier}` format
            Node[] declarations;
            if (newImport._useRequire)
            {
                declarations = AutoImport.GetNewRequires(
                    tracker,
                    moduleSpecifier,
                    quotePreference,
                    newImport._defaultImport,
                    AutoImport.SortedNamedImports(newImport._namedImports),
                    newImport._namespaceLikeImport,
                    _view._program.Options());
            }
            else
            {
                declarations = AutoImport.GetNewImports(
                    tracker,
                    moduleSpecifier,
                    quotePreference,
                    newImport._defaultImport,
                    AutoImport.SortedNamedImports(newImport._namedImports),
                    newImport._namespaceLikeImport,
                    _view._program.Options(),
                    _preferences);
            }
            newDeclarations.AddRange(declarations);
        }

        if (newDeclarations.Count > 0)
        {
            AutoImport.InsertImports(tracker, _view._importingFile, newDeclarations.ToArray(), true /*blankLineBetween*/, _preferences);
        }

        // Unmappable files are dropped by GetChanges, so a content-mapped importing file that cannot be
        // faithfully rewritten yields no edits rather than a corrupting one.
        var (changes, _) = tracker.GetChanges();
        return changes.TryGetValue(_view._importingFile.OriginalFileName(), out var edits) ? edits : [];
    }

    /// <summary>Go: <c>func (adder *importAdder) AddImportFix(fix *Fix)</c>（import_adder.go:181-315）。
    /// AddImportFix adds a fix to the import adder, accumulating it with other fixes
    /// so that multiple imports from the same module are coalesced into a single import statement.</summary>
    public void AddImportFix(Fix fix)
    {
        var symbolName = fix.AutoImportFix.Name;
        var compilerOptions = _view._program.Options();

        switch (fix.AutoImportFix.Kind)
        {
            case AutoImportFixKind.UseNamespace:
                _addToNamespace.Add(fix);
                break;
            case AutoImportFixKind.JsdocTypeImport:
                _importType.Add(fix);
                break;
            case AutoImportFixKind.AddToExisting:
            {
                var existingFix = AutoImport.GetAddToExistingImportFix(_view._importingFile, fix);
                if (!_addToExisting.TryGetValue(existingFix._importClauseOrBindingPattern, out var entry))
                {
                    entry = new AddToExistingState
                    {
                        _importClauseOrBindingPattern = existingFix._importClauseOrBindingPattern,
                    };
                    _addToExisting[existingFix._importClauseOrBindingPattern] = entry;
                }

                if (fix.AutoImportFix.ImportKind == ImportKind.Named)
                {
                    var prevTypeOnly = entry._namedImports.TryGetValue(symbolName, out var prevImport)
                        ? prevImport.AddAsTypeOnly
                        : default;
                    entry._namedImports[symbolName] = new NewImportBinding
                    {
                        Kind = ImportKind.Named,
                        Name = symbolName,
                        AddAsTypeOnly = AutoImport.ReduceAddAsTypeOnlyValues(prevTypeOnly, fix.AutoImportFix.AddAsTypeOnly),
                        PropertyName = existingFix._namedImport!.PropertyName,
                    };
                }
                else
                {
                    // Default import
                    Debug.Assert(
                        entry._defaultImport is null || entry._defaultImport.Name == symbolName,
                        "(Add to Existing) Default import should be missing or match symbolName");
                    var prevTypeOnly = entry._defaultImport?.AddAsTypeOnly ?? default;
                    entry._defaultImport = new NewImportBinding
                    {
                        Kind = ImportKind.Default,
                        Name = symbolName,
                        AddAsTypeOnly = AutoImport.ReduceAddAsTypeOnlyValues(prevTypeOnly, fix.AutoImportFix.AddAsTypeOnly),
                    };
                }
                break;
            }
            case AutoImportFixKind.AddNew:
            {
                var entry = GetNewImportEntry(fix.AutoImportFix.ModuleSpecifier, fix.AutoImportFix.ImportKind, fix.AutoImportFix.UseRequire, fix.AutoImportFix.AddAsTypeOnly);
                Debug.Assert(
                    entry._useRequire == fix.AutoImportFix.UseRequire,
                    "(Add new) Tried to add an `import` and a `require` for the same module");

                switch (fix.AutoImportFix.ImportKind)
                {
                    case ImportKind.Default:
                    {
                        Debug.Assert(
                            entry._defaultImport is null || entry._defaultImport.Name == symbolName,
                            "(Add new) Default import should be missing or match symbolName");
                        var prevTypeOnly = entry._defaultImport?.AddAsTypeOnly ?? default;
                        entry._defaultImport = new NewImportBinding
                        {
                            Kind = ImportKind.Default,
                            Name = symbolName,
                            AddAsTypeOnly = AutoImport.ReduceAddAsTypeOnlyValues(prevTypeOnly, fix.AutoImportFix.AddAsTypeOnly),
                        };
                        break;
                    }

                    case ImportKind.Named:
                    {
                        entry._namedImports ??= new Dictionary<string, NewImportBinding>();
                        var prevTypeOnly = entry._namedImports.TryGetValue(symbolName, out var prevImport)
                            ? prevImport.AddAsTypeOnly
                            : default;
                        entry._namedImports[symbolName] = new NewImportBinding
                        {
                            Kind = ImportKind.Named,
                            Name = symbolName,
                            AddAsTypeOnly = AutoImport.ReduceAddAsTypeOnlyValues(prevTypeOnly, fix.AutoImportFix.AddAsTypeOnly),
                            // !!! propertyName
                        };
                        break;
                    }

                    case ImportKind.CommonJS:
                    {
                        if (compilerOptions.VerbatimModuleSyntax == Tristate.TSTrue)
                        {
                            entry._namedImports ??= new Dictionary<string, NewImportBinding>();
                            var prevTypeOnly = entry._namedImports.TryGetValue(symbolName, out var prevImport)
                                ? prevImport.AddAsTypeOnly
                                : default;
                            entry._namedImports[symbolName] = new NewImportBinding
                            {
                                Kind = ImportKind.CommonJS,
                                Name = symbolName,
                                AddAsTypeOnly = AutoImport.ReduceAddAsTypeOnlyValues(prevTypeOnly, fix.AutoImportFix.AddAsTypeOnly),
                                // !!! propertyName
                            };
                        }
                        else
                        {
                            Debug.Assert(
                                entry._namespaceLikeImport is null || entry._namespaceLikeImport.Name == symbolName,
                                "Namespacelike import should be missing or match symbolName");
                            entry._namespaceLikeImport = new NewImportBinding
                            {
                                Kind = ImportKind.CommonJS,
                                Name = symbolName,
                                AddAsTypeOnly = fix.AutoImportFix.AddAsTypeOnly,
                            };
                        }
                        break;
                    }

                    case ImportKind.Namespace:
                    {
                        Debug.Assert(
                            entry._namespaceLikeImport is null || entry._namespaceLikeImport.Name == symbolName,
                            "Namespacelike import should be missing or match symbolName");
                        entry._namespaceLikeImport = new NewImportBinding
                        {
                            Kind = ImportKind.Namespace,
                            Name = symbolName,
                            AddAsTypeOnly = fix.AutoImportFix.AddAsTypeOnly,
                        };
                        break;
                    }
                }
                break;
            }
            case AutoImportFixKind.PromoteTypeOnly:
                // Excluding from fix-all
                break;
            default:
                Debug.Fail("Unexpected fix kind: " + fix.AutoImportFix.Kind);
                throw new System.Diagnostics.UnreachableException();
        }
    }

    /// <summary>Go: <c>func (adder *importAdder) getNewImportEntry(moduleSpecifier string, importKind lsproto.ImportKind, useRequire bool, addAsTypeOnly lsproto.AddAsTypeOnly) *importsCollection</c>（import_adder.go:332-367）。</summary>
    internal ImportsCollection GetNewImportEntry(string moduleSpecifier, ImportKind importKind, bool useRequire, AddAsTypeOnly addAsTypeOnly)
    {
        // A default import that requires type-only makes the whole import type-only.
        // (We could add `default` as a named import, but that style seems undesirable.)
        // Under `--preserveValueImports` and `--importsNotUsedAsValues=error`, if a
        // module default-exports a type but named-exports some values (weird), you would
        // have to use a type-only default import and non-type-only named imports. These
        // require two separate import declarations, so we build this into the map key.
        var typeOnlyKey = AutoImport.NewImportsKey(moduleSpecifier, true /*topLevelTypeOnly*/);
        var nonTypeOnlyKey = AutoImport.NewImportsKey(moduleSpecifier, false /*topLevelTypeOnly*/);
        _newImports.TryGetValue(typeOnlyKey, out var typeOnlyEntry);
        _newImports.TryGetValue(nonTypeOnlyKey, out var nonTypeOnlyEntry);
        var newEntry = new ImportsCollection
        {
            _useRequire = useRequire,
        };

        if (importKind == ImportKind.Default && addAsTypeOnly == AddAsTypeOnly.Required)
        {
            if (typeOnlyEntry is not null)
            {
                return typeOnlyEntry;
            }
            _newImports[typeOnlyKey] = newEntry;
            return newEntry;
        }

        if (addAsTypeOnly == AddAsTypeOnly.Allowed && (typeOnlyEntry is not null || nonTypeOnlyEntry is not null))
        {
            if (typeOnlyEntry is not null)
            {
                return typeOnlyEntry;
            }
            return nonTypeOnlyEntry!;
        }

        if (nonTypeOnlyEntry is not null)
        {
            return nonTypeOnlyEntry;
        }

        _newImports[nonTypeOnlyKey] = newEntry;
        return newEntry;
    }

    /// <summary>Go: <c>func (adder *importAdder) getAllExportsForSymbol(symbol *ast.Symbol) []*Export</c>（import_adder.go:369-374）。</summary>
    internal Export[] GetAllExportsForSymbol(Symbol symbol)
    {
        if (AutoImport.SymbolToExport(symbol, _checker) is { } export)
        {
            return _view.SearchByExportID(export.ExportID);
        }
        return [];
    }

    /// <summary>Go: <c>func (adder *importAdder) getImportFixForSymbol(view *View, exports []*Export, isValidTypeOnlyUseSite bool) *Fix</c>（import_adder.go:490-501）。</summary>
    internal static Fix? GetImportFixForSymbol(View view, Export[] exports, bool isValidTypeOnlyUseSite)
    {
        var fixes = FlatMap(exports, export => view.GetFixes(export, false /*forJSX*/, isValidTypeOnlyUseSite, null /*usagePosition*/));
        Array.Sort(fixes, (a, b) => view.CompareFixesForRanking(a, b));
        if (fixes.Length > 0)
        {
            return fixes[0];
        }
        return null;
    }
}

/// <summary>Go 的 <c>autoimport</c> 包（包级函数/常量放这里，见 docs/architecture.md §2）。</summary>
public static partial class AutoImport
{
    /// <summary>Go: <c>func newImportsKey(moduleSpecifier string, topLevelTypeOnly bool) string</c>（import_adder.go:46-51）。</summary>
    internal static string NewImportsKey(string moduleSpecifier, bool topLevelTypeOnly)
    {
        if (topLevelTypeOnly)
        {
            return "1|" + moduleSpecifier;
        }
        return "0|" + moduleSpecifier;
    }

    /// <summary>Go: <c>func NewImportAdder(ctx context.Context, program *compiler.Program, checker *checker.Checker, file *ast.SourceFile, view *View, formatOptions lsutil.FormatCodeSettings, converters *lsconv.Converters, preferences lsutil.UserPreferences) ImportAdder</c>（import_adder.go:74-82）。
    /// <paramref name="program"/> 与 <paramref name="file"/> 照 Go 保留但未被使用（Go 侧同样如此）。</summary>
    public static ImportAdder NewImportAdder(
        Context ctx,
        TypeScript.Compiler.Program program,
        TypeScript.Checker.Checker checker,
        SourceFile file,
        View view,
        FormatCodeSettings formatOptions,
        Converters converters,
        UserPreferences preferences)
    {
        return new ImportAdderImpl(
            ctx,
            checker,
            view,
            formatOptions,
            converters,
            preferences);
    }

    /// <summary>Go: <c>func sortedNamedImports(m map[string]*newImportBinding) []*newImportBinding</c>（import_adder.go:170-179）。</summary>
    internal static NewImportBinding?[] SortedNamedImports(Dictionary<string, NewImportBinding>? m)
    {
        if (m is null)
        {
            return [];
        }
        var keys = m.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);
        var result = new NewImportBinding?[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            result[i] = m[keys[i]];
        }
        return result;
    }

    /// <summary>Go: <c>func reduceAddAsTypeOnlyValues(prevValue, newValue lsproto.AddAsTypeOnly) lsproto.AddAsTypeOnly</c>（import_adder.go:317-330）。
    /// `NotAllowed` overrides `Required` because one addition of a new import might be required to be type-only
    /// because of `--importsNotUsedAsValues=error`, but if a second addition of the same import is `NotAllowed`
    /// to be type-only, the reason the first one was `Required` - the unused runtime dependency - is now moot.
    /// Alternatively, if one addition is `Required` because it has no value meaning under `--preserveValueImports`
    /// and `--isolatedModules`, it should be impossible for another addition to be `NotAllowed` since that would
    /// mean a type is being referenced in a value location。</summary>
    internal static AddAsTypeOnly ReduceAddAsTypeOnlyValues(AddAsTypeOnly prevValue, AddAsTypeOnly newValue)
    {
        if ((int)newValue > (int)prevValue)
        {
            return newValue;
        }
        return prevValue;
    }

    /// <summary>Go: <c>func TypeToAutoImportableTypeNode(c *checker.Checker, importAdder ImportAdder, t *checker.Type, contextNode *ast.Node) *ast.TypeNode</c>（import_adder.go:376-386）。</summary>
    public static Node? TypeToAutoImportableTypeNode(
        TypeScript.Checker.Checker c,
        ImportAdder importAdder,
        TypeScript.Checker.Type t,
        Node contextNode /* !!! flags */)
    {
        var idToSymbol = new Dictionary<Node, Symbol>();
        var typeNode = c.TypeToTypeNode(t, contextNode, TypeScript.NodeBuilder.Flags.None, idToSymbol);
        if (typeNode is null)
        {
            return null;
        }
        return TypeNodeToAutoImportableTypeNode(typeNode, importAdder, idToSymbol);
    }

    /// <summary>Go: <c>func TypeNodeToAutoImportableTypeNode(typeNode *ast.TypeNode, importAdder ImportAdder, idToSymbol map[*ast.IdentifierNode]*ast.Symbol) *ast.TypeNode</c>（import_adder.go:388-402）。
    /// TypeNodeToAutoImportableTypeNode converts import type references in a type node to
    /// simple type references and registers needed imports with the import adder。</summary>
    public static Node? TypeNodeToAutoImportableTypeNode(
        Node typeNode,
        ImportAdder importAdder,
        Dictionary<Node, Symbol> idToSymbol)
    {
        var (referenceTypeNode, importableSymbols) = TryGetAutoImportableReferenceFromTypeNode(typeNode, idToSymbol);
        if (referenceTypeNode is not null)
        {
            if (importAdder is not null)
            {
                ImportSymbols(importAdder, importableSymbols);
            }
            typeNode = referenceTypeNode;
        }

        // !!! handle type node reuse: nodes needs to be fresh here but also preserve symbols
        return typeNode;
    }

    /// <summary>Go: <c>func importSymbols(importAdder ImportAdder, symbols []*ast.Symbol)</c>（import_adder.go:404-408）。</summary>
    internal static void ImportSymbols(ImportAdder importAdder, Symbol[] symbols)
    {
        foreach (var symbol in symbols)
        {
            importAdder.AddImportFromExportedSymbol(symbol, true /*isValidTypeOnlyUseSite*/);
        }
    }

    /// <summary>Go: <c>func TryGetAutoImportableReferenceFromTypeNode(importTypeNode *ast.TypeNode, idToSymbol map[*ast.IdentifierNode]*ast.Symbol) (*ast.TypeNode, []*ast.Symbol)</c>（import_adder.go:414-451）。
    /// Given a type node containing 'import("./a").SomeType&lt;import("./b").OtherType&lt;...&gt;&gt;',
    /// returns an equivalent type reference node with any nested ImportTypeNodes also replaced
    /// with type references, and a list of symbols that must be imported to use the type reference.
    /// TryGetAutoImportableReferenceFromTypeNode converts import type references in a type node
    /// to simple type references and returns the transformed type node and the symbols that need
    /// to be imported。</summary>
    public static (Node? TypeNode, Symbol[] Symbols) TryGetAutoImportableReferenceFromTypeNode(
        Node importTypeNode,
        Dictionary<Node, Symbol> idToSymbol)
    {
        List<Symbol> symbols = [];
        NodeVisitor? visitor = null;
        var factory = NewNodeFactory(new NodeFactoryHooks());

        Node? Visit(Node node)
        {
            if (IsLiteralImportTypeNode(node) && node.AsImportTypeNode()!.Qualifier is { } qualifierNode)
            {
                // Symbol for the left-most thing after the dot
                var firstIdentifier = GetFirstIdentifier(qualifierNode)!;
                if (!idToSymbol.TryGetValue(firstIdentifier, out var symbol))
                {
                    // if symbol is missing then this doesn't come from a synthesized import type node
                    // it has to be an import type node authored by the user and thus it has to be valid
                    // it can't refer to reserved internal symbol names and such
                    return node.VisitEachChild(visitor!);
                }
                var name = GetNameForExportedSymbol(symbol, false /*preferCapitalized*/);
                Node qualifier;
                if (name != firstIdentifier.Text())
                {
                    qualifier = ReplaceFirstIdentifierOfEntityName(factory, qualifierNode, factory.NewIdentifier(name));
                }
                else
                {
                    qualifier = qualifierNode;
                }
                symbols.Add(symbol);
                var typeArguments = visitor!.VisitNodes(node.AsImportTypeNode()!.TypeArguments);
                return factory.NewTypeReferenceNode(qualifier, typeArguments);
            }
            return visitor!.VisitEachChild(node);
        }

        visitor = NewNodeVisitor(Visit, factory, new NodeVisitorHooks());

        var typeNode = visitor.VisitNode(importTypeNode);
        Debug.Assert(typeNode is null || IsTypeNode(typeNode), "expected a type node");
        return (typeNode, symbols.ToArray());
    }

    /// <summary>Go: <c>func getNameForExportedSymbol(symbol *ast.Symbol, preferCapitalized bool) string</c>（import_adder.go:453-466）。
    /// If a type checker and multiple files are available, consider using `forEachNameOfDefaultExport`
    /// instead, which searches for names of re-exported defaults/namespaces in target files。</summary>
    internal static string GetNameForExportedSymbol(Symbol symbol, bool preferCapitalized)
    {
        if (symbol.Name == InternalSymbolNameExportEquals || symbol.Name == InternalSymbolNameDefault)
        {
            // Names for default exports:
            // - export default foo => foo
            // - export { foo as default } => foo
            // - export default 0 => filename converted to camelCase
            var name = GetDefaultLikeExportNameFromDeclaration(symbol);
            if (name != "")
            {
                return name;
            }
            Debug.Assert(symbol.Parent is not null, "Expected exported symbol to have module symbol as parent");
            return ModuleSymbolToValidIdentifier(symbol.Parent!, preferCapitalized);
        }
        return symbol.Name;
    }

    /// <summary>Go: <c>func replaceFirstIdentifierOfEntityName(factory *ast.NodeFactory, name *ast.EntityName, newIdentifier *ast.IdentifierNode) *ast.EntityName</c>（import_adder.go:468-476）。</summary>
    internal static Node ReplaceFirstIdentifierOfEntityName(NodeFactory factory, Node name, Node newIdentifier)
    {
        if (name.Kind == Kind.Identifier)
        {
            return newIdentifier;
        }
        return factory.NewQualifiedName(
            ReplaceFirstIdentifierOfEntityName(factory, name.AsQualifiedName()!.Left!, newIdentifier),
            name.AsQualifiedName()!.Right!);
    }
}
