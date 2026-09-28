// ported from: internal/ls/importTracker.go:1-767
//
// ⚠ **归属说明（G6 接管）**：移植计划把 `importTracker.go` 划给 G7，但 `findallreferences.go` 的
// `getReferencedSymbolsForNode` / `getReferencedSymbolsForModule` / `refState` 直接用它的
// `ImportTracker` / `ImportsResult` / `ModuleReference` / `ExportInfo` / `ImpExpKind` /
// `createImportTracker` / `getImportOrExportSymbol` / `getExportInfo` / `findModuleReferences`，
// 属 G6 的硬依赖；G6 先行落地整份文件（已报 Main）。G7 请勿重复声明。
//
// 与 Go 的偏离（逐条说明）：
// 1) Go 未导出类型 → `internal` + PascalCase（`impExpKind` → `ImpExpKind`、`refOptions` 之类）；
//    Go 未导出**字段** → `_camelCase`（`.editorconfig` 的 `members_should_be_pascal_case` 只约束方法/属性/事件）。
// 2) Go 的 `type ImportTracker func(...) *ImportsResult` → C# `internal delegate ImportsResult ImportTracker(...)`；
//    `createImportTracker` 返回的闭包 → C# lambda（捕获宿主本地变量，语义一致）。
// 3) Go 的 `ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`（见 CrossProject.cs 文件头第 2 条）。
// 4) Go 的 `[]T` 可为 nil → `T[]?`；`len(x) == 0` → `(x?.Length ?? 0) == 0`；`for range nilSlice` 不迭代 ⇒ C# 用 `?? []`。
// 5) Go 的闭包互递归（`var f func(...); f = func(...)`）→ C# 局部函数（`void F(...)`），语义一致。
// 6) `debug.Assert`/`debug.Fail`/`debug.FailBadSyntaxKind` → `Debug.Assert`/`Debug.Fail`/`Debug.FailBadSyntaxKind`
//    （`Debug` 在根命名空间 `TypeScript`，见 Debug/Debug.cs 文件头）。
// 7) `core.OrElse`/`core.IfElse`/`core.Map`/`core.Some`/`core.Coalesce`/`core.FirstNonNil` → 同名 C# helper。
// 8) Go 的 `nodeSeenTracker()`（utilities.go）→ `Ls.NodeSeenTracker()`，返回 `Func<Node, bool>`。
// 9) Go 的 map 查询 `allDirectImports[moduleSymbol]` 缺键返回 nil 切片（不 panic）⇒ C# `Dictionary.TryGetValue` + `?? []`；
//    ⚠ 直接写 `dict[key]` 会抛 KeyNotFoundException（Go 的零值语义必须显式补）。
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
// 同名类型消歧（`using TypeScript.SpanMap` 之类带来的 `Kind`；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
// `using TypeScript.Checker` 会带进 `TypeScript.Checker.Program`，与 `TypeScript.Compiler.Program` 二义：
using Program = TypeScript.Compiler.Program;

namespace TypeScript.Ls;

/// <summary>Go: <c>type ImpExpKind int32</c>（importTracker.go:15-21）。</summary>
internal enum ImpExpKind
{
    Unknown = 0,
    Import = 1,
    Export = 2,
}

/// <summary>Go: <c>type ImportExportSymbol struct</c>（importTracker.go:23-27）。</summary>
internal sealed class ImportExportSymbol
{
    internal ImpExpKind _kind;
    internal Symbol? _symbol;
    internal ExportInfo? _exportInfo;
}

/// <summary>Go: <c>type ExportKind int</c>（importTracker.go:29-37）。</summary>
internal enum ExportKind
{
    Named = 0,
    Default = 1,
    ExportEquals = 2,
    UMD = 3,
    Module = 4,
}

/// <summary>Go: <c>type ExportInfo struct</c>（importTracker.go:39-42）。</summary>
internal sealed class ExportInfo
{
    internal Symbol? _exportingModuleSymbol;
    internal ExportKind _exportKind;
}

/// <summary>Go: <c>type LocationAndSymbol struct</c>（importTracker.go:44-47）。</summary>
internal sealed class LocationAndSymbol
{
    internal Node? _importLocation;
    internal Symbol? _importSymbol;
}

/// <summary>Go: <c>type ImportsResult struct</c>（importTracker.go:49-53）。</summary>
internal sealed class ImportsResult
{
    internal LocationAndSymbol[]? _importSearches;
    internal Node[]? _singleReferences;
    internal SourceFile[]? _indirectUsers;
}

/// <summary>Go: <c>type ImportTracker func(exportSymbol *ast.Symbol, exportInfo *ExportInfo, isForRename bool) *ImportsResult</c>（importTracker.go:55）。</summary>
internal delegate ImportsResult ImportTracker(Symbol exportSymbol, ExportInfo exportInfo, bool isForRename);

/// <summary>Go: <c>type ModuleReferenceKind int32</c>（importTracker.go:57-63）。</summary>
internal enum ModuleReferenceKind
{
    Import = 0,
    Reference = 1,
    Implicit = 2,
}

/// <summary>Go: <c>type ModuleReference struct</c>（importTracker.go:65-71）。</summary>
internal sealed class ModuleReference
{
    internal ModuleReferenceKind _kind;
    internal Node? _literal; // for import and implicit kinds (StringLiteralLike)
    internal SourceFile? _referencingFile;
    internal FileReference? _ref; // for reference kind
}

public static partial class Ls
{
    /// <summary>Go: <c>func createImportTracker(ctx, program, sourceFiles, sourceFilesSet, checker) ImportTracker</c>（importTracker.go:74-81）。</summary>
    internal static ImportTracker CreateImportTracker(
        Context ctx,
        Program program,
        SourceFile[]? sourceFiles,
        Set<string> sourceFilesSet,
        TypeScript.Checker.Checker checker)
    {
        var allDirectImports = GetDirectImportsMap(ctx, program, sourceFiles, checker);
        return (exportSymbol, exportInfo, isForRename) =>
        {
            var (directImports, indirectUsers) = GetImportersForExport(sourceFiles, sourceFilesSet, allDirectImports, exportInfo, checker);
            var (importSearches, singleReferences) = GetSearchesFromDirectImports(directImports, exportSymbol, exportInfo._exportKind, checker, isForRename);
            return new ImportsResult
            {
                _importSearches = importSearches,
                _singleReferences = singleReferences,
                _indirectUsers = indirectUsers,
            };
        };
    }

    /// <summary>Go: <c>func getDirectImportsMap(ctx, program, sourceFiles, checker) map[*ast.Symbol][]*ast.Node</c>（importTracker.go:84-96）。</summary>
    internal static Dictionary<Symbol, List<Node>> GetDirectImportsMap(
        Context ctx,
        Program program,
        SourceFile[]? sourceFiles,
        TypeScript.Checker.Checker checker)
    {
        var result = new Dictionary<Symbol, List<Node>>();
        foreach (var sourceFile in sourceFiles ?? [])
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return result;
            }

            ForEachImport(program, sourceFile, (importDecl, moduleSpecifier) =>
            {
                var moduleSymbol = checker.GetSymbolAtLocation(moduleSpecifier);
                if (moduleSymbol is not null)
                {
                    if (!result.TryGetValue(moduleSymbol, out var list))
                    {
                        list = [];
                        result[moduleSymbol] = list;
                    }

                    list.Add(importDecl);
                }
            });
        }

        return result;
    }

    /// <summary>Go: <c>func forEachImport(program, sourceFile, action func(importStatement, imported *ast.Node))</c>（importTracker.go:100-133）。</summary>
    internal static void ForEachImport(Program program, SourceFile sourceFile, Action<Node, Node> action)
    {
        var implicitImports = new List<Node>();
        var (_, jsxSpecifier) = program.GetJSXRuntimeImportSpecifier(sourceFile.Path());
        if (jsxSpecifier is not null)
        {
            implicitImports.Add(jsxSpecifier);
        }

        var importHelpersSpecifier = program.GetImportHelpersImportSpecifier(sourceFile.Path());
        if (importHelpersSpecifier is not null)
        {
            implicitImports.Add(importHelpersSpecifier);
        }

        if (sourceFile.ExternalModuleIndicator is not null || sourceFile.Imports().Length + implicitImports.Count != 0)
        {
            foreach (var i in sourceFile.Imports())
            {
                action(ImportFromModuleSpecifier(i)!, i);
            }

            foreach (var i in implicitImports)
            {
                action(ImportFromModuleSpecifier(i)!, i);
            }
        }
        else
        {
            ForEachPossibleImportOrExportStatement(sourceFile.AsNode(), node =>
            {
                switch (node.Kind)
                {
                    case Kind.ExportDeclaration:
                    case Kind.ImportDeclaration:
                    case Kind.JSImportDeclaration:
                        if (node.ModuleSpecifier() is { } specifier && IsStringLiteral(specifier))
                        {
                            action(node, specifier);
                        }

                        break;
                    case Kind.ImportEqualsDeclaration:
                        if (IsExternalModuleImportEquals(node))
                        {
                            action(node, node.AsImportEqualsDeclaration()!.ModuleReference!.Expression()!);
                        }

                        break;
                }

                return false;
            });
        }
    }

    /// <summary>Go: <c>func forEachPossibleImportOrExportStatement(sourceFileLike *ast.Node, action func(statement *ast.Node) bool) bool</c>（importTracker.go:135-144）。</summary>
    internal static bool ForEachPossibleImportOrExportStatement(Node sourceFileLike, Func<Node, bool> action)
    {
        foreach (var statement in GetStatementsOfSourceFileLike(sourceFileLike) ?? [])
        {
            if (action(statement) || (IsAmbientModuleDeclaration(statement) && ForEachPossibleImportOrExportStatement(statement, action)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func getSourceFileLikeForImportDeclaration(node *ast.Node) *ast.Node</c>（importTracker.go:146-155）。</summary>
    internal static Node GetSourceFileLikeForImportDeclaration(Node node)
    {
        if (IsCallExpression(node) || IsJSDocImportTag(node))
        {
            return GetSourceFileOfNode(node)!.AsNode();
        }

        var parent = node.Parent!;
        if (IsSourceFile(parent))
        {
            return parent;
        }

        Debug.Assert(IsModuleBlock(parent) && IsAmbientModuleDeclaration(parent.Parent!), "Expected ambient module declaration");
        return parent.Parent!;
    }

    /// <summary>Go: <c>func isAmbientModuleDeclaration(node *ast.Node) bool</c>（importTracker.go:157-159）。</summary>
    internal static bool IsAmbientModuleDeclaration(Node node)
    {
        return IsModuleDeclaration(node) && IsStringLiteral(node.Name()!);
    }

    /// <summary>Go: <c>func getStatementsOfSourceFileLike(node *ast.Node) []*ast.Node</c>（importTracker.go:161-169）。</summary>
    internal static Node[]? GetStatementsOfSourceFileLike(Node node)
    {
        if (IsSourceFile(node))
        {
            return node.Statements();
        }

        if (node.Body() is { } body)
        {
            return body.Statements();
        }

        return null;
    }

    /// <summary>Go: <c>func getImportersForExport(sourceFiles, sourceFilesSet, allDirectImports, exportInfo, checker) ([]*ast.Node, []*ast.SourceFile)</c>（importTracker.go:171-311）。</summary>
    internal static (Node[] DirectImports, SourceFile[] IndirectUsers) GetImportersForExport(
        SourceFile[]? sourceFiles,
        Set<string> sourceFilesSet,
        Dictionary<Symbol, List<Node>> allDirectImports,
        ExportInfo exportInfo,
        TypeScript.Checker.Checker checker)
    {
        var directImports = new List<Node>();
        var indirectUserDeclarations = new List<Node>();
        var markSeenDirectImport = NodeSeenTracker();
        var markSeenIndirectUser = NodeSeenTracker();
        var isAvailableThroughGlobal = IsSourceFileWithGlobalExports(exportInfo._exportingModuleSymbol!.ValueDeclaration);

        List<Node> GetDirectImports(Symbol moduleSymbol)
        {
            return allDirectImports.TryGetValue(moduleSymbol, out var list) ? list : [];
        }

        // Adds a module and all of its transitive dependencies as possible indirect users
        void AddIndirectUser(Node sourceFileLike, bool addTransitiveDependencies)
        {
            // When isAvailableThroughGlobal, getIndirectUsers already returns all source files,
            // so indirectUserDeclarations is never consulted. Nothing to do here.
            if (isAvailableThroughGlobal)
            {
                return;
            }

            if (!markSeenIndirectUser(sourceFileLike))
            {
                return;
            }

            indirectUserDeclarations.Add(sourceFileLike);
            if (!addTransitiveDependencies)
            {
                return;
            }

            var moduleSymbol = checker.GetMergedSymbol(sourceFileLike.Symbol);
            if (moduleSymbol is null)
            {
                return;
            }

            Debug.Assert((moduleSymbol.Flags & SymbolFlags.Module) != 0, "module symbol expected");
            foreach (var directImport in GetDirectImports(moduleSymbol))
            {
                if (!IsImportTypeNode(directImport))
                {
                    AddIndirectUser(GetSourceFileLikeForImportDeclaration(directImport), true /*addTransitiveDependencies*/);
                }
            }
        }

        bool IsExported(Node? node, bool stopAtAmbientModule)
        {
            while (node is not null && !(stopAtAmbientModule && IsAmbientModuleDeclaration(node)))
            {
                if (HasSyntacticModifier(node, ModifierFlags.Export))
                {
                    return true;
                }

                node = node.Parent;
            }

            return false;
        }

        void HandleImportCall(Node importCall)
        {
            var top = FindAncestor(importCall, IsAmbientModuleDeclaration);
            top ??= GetSourceFileOfNode(importCall)!.AsNode();
            AddIndirectUser(top, IsExported(importCall, true /*stopAtAmbientModule*/));
        }

        void HandleNamespaceImport(Node importDeclaration, Node name, bool isReExport, bool alreadyAddedDirect)
        {
            if (exportInfo._exportKind == ExportKind.ExportEquals)
            {
                // This is a direct import, not import-as-namespace.
                if (!alreadyAddedDirect)
                {
                    directImports.Add(importDeclaration);
                }
            }
            else if (!isAvailableThroughGlobal)
            {
                var sourceFileLike = GetSourceFileLikeForImportDeclaration(importDeclaration);
                Debug.Assert(IsSourceFile(sourceFileLike) || IsModuleDeclaration(sourceFileLike), "source file like expected");
                AddIndirectUser(sourceFileLike, isReExport || FindNamespaceReExports(sourceFileLike, name, checker));
            }
        }

        void HandleDirectImports(Symbol exportingModuleSymbol)
        {
            var theseDirectImports = GetDirectImports(exportingModuleSymbol);
            foreach (var direct in theseDirectImports)
            {
                if (!markSeenDirectImport(direct))
                {
                    continue;
                }

                // !!! cancellation
                switch (direct.Kind)
                {
                    case Kind.CallExpression:
                        if (IsImportCall(direct))
                        {
                            HandleImportCall(direct);
                        }
                        else if (!isAvailableThroughGlobal)
                        {
                            var parent = direct.Parent!;
                            if (exportInfo._exportKind == ExportKind.ExportEquals && IsVariableDeclaration(parent))
                            {
                                var name = parent.Name();
                                if (name is not null && IsIdentifier(name))
                                {
                                    directImports.Add(name);
                                }
                            }
                        }

                        break;
                    case Kind.Identifier:
                        // Nothing
                        break;
                    case Kind.ImportEqualsDeclaration:
                        HandleNamespaceImport(direct, direct.Name()!, HasSyntacticModifier(direct, ModifierFlags.Export), false /*alreadyAddedDirect*/);
                        break;
                    case Kind.ImportDeclaration:
                    case Kind.JSImportDeclaration:
                    case Kind.JSDocImportTag:
                        directImports.Add(direct);
                        if (direct.ImportClause() is { } importClause)
                        {
                            if (importClause.AsImportClause()!.NamedBindings is { } namedBindings && IsNamespaceImport(namedBindings))
                            {
                                HandleNamespaceImport(direct, namedBindings.Name()!, false /*isReExport*/, true /*alreadyAddedDirect*/);
                                break;
                            }
                        }

                        if (!isAvailableThroughGlobal && IsDefaultImport(direct))
                        {
                            AddIndirectUser(GetSourceFileLikeForImportDeclaration(direct), false);
                            // Add a check for indirect uses to handle synthetic default imports
                        }

                        break;
                    case Kind.ExportDeclaration:
                        var exportClause = direct.AsExportDeclaration()!.ExportClause;
                        if (exportClause is null)
                        {
                            // This is `export * from "foo"`, so imports of this module may import the export too.
                            HandleDirectImports(GetContainingModuleSymbol(direct, checker)!);
                        }
                        else if (IsNamespaceExport(exportClause))
                        {
                            // `export * as foo from "foo"` add to indirect uses
                            AddIndirectUser(GetSourceFileLikeForImportDeclaration(direct), true /*addTransitiveDependencies*/);
                        }
                        else
                        {
                            // This is `export { foo } from "foo"` and creates an alias symbol, so recursive search will get handle re-exports.
                            directImports.Add(direct);
                        }

                        break;
                    case Kind.ImportType:
                        // Only check for typeof import('xyz')
                        if (!isAvailableThroughGlobal && direct.AsImportTypeNode()!.IsTypeOf && direct.AsImportTypeNode()!.Qualifier is null && IsExported(direct, false))
                        {
                            AddIndirectUser(GetSourceFileOfNode(direct)!.AsNode(), true /*addTransitiveDependencies*/);
                        }

                        directImports.Add(direct);
                        break;
                    default:
                        Debug.FailBadSyntaxKind(direct, "Unexpected import kind.");
                        break;
                }
            }
        }

        SourceFile[] GetIndirectUsers()
        {
            if (isAvailableThroughGlobal)
            {
                // It has `export as namespace`, so anything could potentially use it.
                return sourceFiles ?? [];
            }

            // Module augmentations may use this module's exports without importing it.
            foreach (var decl in exportInfo._exportingModuleSymbol.Declarations ?? [])
            {
                if (IsExternalModuleAugmentation(decl) && sourceFilesSet.Has(GetSourceFileOfNode(decl)!.FileName()))
                {
                    AddIndirectUser(decl, false);
                }
            }

            // This may return duplicates (if there are multiple module declarations in a single source file, all importing the same thing as a namespace), but `State.markSearchedSymbol` will handle that.
            return Map(indirectUserDeclarations.ToArray(), GetSourceFileOfNode)!;
        }

        HandleDirectImports(exportInfo._exportingModuleSymbol);
        return (directImports.ToArray(), GetIndirectUsers());
    }

    /// <summary>Go: <c>func getContainingModuleSymbol(importer *ast.Node, checker *checker.Checker) *ast.Symbol</c>（importTracker.go:313-315）。</summary>
    internal static Symbol? GetContainingModuleSymbol(Node importer, TypeScript.Checker.Checker checker)
    {
        return checker.GetMergedSymbol(GetSourceFileLikeForImportDeclaration(importer).Symbol);
    }

    /// <summary>Go: <c>func findNamespaceReExports(sourceFileLike, name *ast.Node, checker) bool</c>（importTracker.go:317-326）。</summary>
    internal static bool FindNamespaceReExports(Node sourceFileLike, Node name, TypeScript.Checker.Checker checker)
    {
        var namespaceImportSymbol = checker.GetSymbolAtLocation(name);
        return ForEachPossibleImportOrExportStatement(sourceFileLike, statement =>
        {
            if (!IsExportDeclaration(statement))
            {
                return false;
            }

            var exportClause = statement.AsExportDeclaration()!.ExportClause;
            var moduleSpecifier = statement.ModuleSpecifier();
            return moduleSpecifier is null && exportClause is not null && IsNamedExports(exportClause) && Some(exportClause.Elements(), element =>
            {
                return checker.GetExportSpecifierLocalTargetSymbol(element) == namespaceImportSymbol;
            });
        });
    }

    /// <summary>Go: <c>func getSearchesFromDirectImports(directImports, exportSymbol, exportKind, checker, isForRename) ([]LocationAndSymbol, []*ast.Node)</c>（importTracker.go:328-435）。</summary>
    internal static (LocationAndSymbol[] ImportSearches, Node[] SingleReferences) GetSearchesFromDirectImports(
        Node[]? directImports,
        Symbol exportSymbol,
        ExportKind exportKind,
        TypeScript.Checker.Checker checker,
        bool isForRename)
    {
        var importSearches = new List<LocationAndSymbol>();
        var singleReferences = new List<Node>();

        void AddSearch(Node? location, Symbol? symbol)
        {
            importSearches.Add(new LocationAndSymbol { _importLocation = location, _importSymbol = symbol });
        }

        bool IsNameMatch(string name)
        {
            // Use name of "default" even in `export =` case because we may have allowSyntheticDefaultImports
            return name == exportSymbol.Name || (exportKind != ExportKind.Named && name == InternalSymbolNameDefault);
        }

        // `import x = require("./x")` or `import * as x from "./x"`.
        // An `export =` may be imported by this syntax, so it may be a direct import.
        // If it's not a direct import, it will be in `indirectUsers`, so we don't have to do anything here.
        void HandleNamespaceImportLike(Node importName)
        {
            // Don't rename an import that already has a different name than the export.
            if (exportKind == ExportKind.ExportEquals && (!isForRename || IsNameMatch(importName.Text())))
            {
                AddSearch(importName, checker.GetSymbolAtLocation(importName));
            }
        }

        void SearchForNamedImport(Node? namedBindings)
        {
            if (namedBindings is null)
            {
                return;
            }

            foreach (var element in namedBindings.Elements() ?? [])
            {
                var name = element.Name()!;
                var propertyName = element.PropertyName();
                if (!IsNameMatch(OrElse(propertyName, name)!.Text()))
                {
                    continue;
                }

                if (propertyName is not null)
                {
                    // This is `import { foo as bar } from "./a"` or `export { foo as bar } from "./a"`. `foo` isn't a local in the file, so just add it as a single reference.
                    singleReferences.Add(propertyName);
                    // If renaming `{ foo as bar }`, don't touch `bar`, just `foo`.
                    // But do rename `foo` in ` { default as foo }` if that's the original export name.
                    if (!isForRename || name.Text() == exportSymbol.Name)
                    {
                        // Search locally for `bar`.
                        AddSearch(name, checker.GetSymbolAtLocation(name));
                    }
                }
                else
                {
                    Symbol? localSymbol;
                    if (IsExportSpecifier(element) && element.PropertyName() is not null)
                    {
                        localSymbol = checker.GetExportSpecifierLocalTargetSymbol(element);
                    }
                    else
                    {
                        localSymbol = checker.GetSymbolAtLocation(name);
                    }

                    AddSearch(name, localSymbol);
                }
            }
        }

        void HandleImport(Node decl)
        {
            if (IsImportEqualsDeclaration(decl))
            {
                if (IsExternalModuleImportEquals(decl))
                {
                    HandleNamespaceImportLike(decl.Name()!);
                }

                return;
            }

            if (IsIdentifier(decl))
            {
                HandleNamespaceImportLike(decl);
                return;
            }

            if (IsImportTypeNode(decl))
            {
                if (decl.AsImportTypeNode()!.Qualifier is { } qualifier)
                {
                    var firstIdentifier = GetFirstIdentifier(qualifier)!;
                    if (firstIdentifier.Text() == SymbolName(exportSymbol))
                    {
                        singleReferences.Add(firstIdentifier);
                    }
                }
                else if (exportKind == ExportKind.ExportEquals)
                {
                    singleReferences.Add(decl.AsImportTypeNode()!.Argument!.AsLiteralTypeNode()!.Literal!);
                }

                return;
            }

            // Ignore if there's a grammar error
            if (!IsStringLiteral(decl.ModuleSpecifier()!))
            {
                return;
            }

            if (IsExportDeclaration(decl))
            {
                if (decl.AsExportDeclaration()!.ExportClause is { } exportClause && IsNamedExports(exportClause))
                {
                    SearchForNamedImport(exportClause);
                }

                return;
            }

            if (decl.ImportClause() is { } importClause)
            {
                if (importClause.AsImportClause()!.NamedBindings is { } namedBindings)
                {
                    switch (namedBindings.Kind)
                    {
                        case Kind.NamespaceImport:
                            HandleNamespaceImportLike(namedBindings.Name()!);
                            break;
                        case Kind.NamedImports:
                            // 'default' might be accessed as a named import `{ default as foo }`.
                            if (exportKind == ExportKind.Named || exportKind == ExportKind.Default)
                            {
                                SearchForNamedImport(namedBindings);
                            }

                            break;
                    }
                }

                // `export =` might be imported by a default import if `--allowSyntheticDefaultImports` is on, so this handles both ExportKind.Default and ExportKind.ExportEquals.
                // If a default import has the same name as the default export, allow to rename it.
                // Given `import f` and `export default function f`, we will rename both, but for `import g` we will rename just that.
                if (importClause.Name() is { } name && (exportKind == ExportKind.Default || exportKind == ExportKind.ExportEquals) && (!isForRename || name.Text() == SymbolNameNoDefault(exportSymbol)))
                {
                    var defaultImportAlias = checker.GetSymbolAtLocation(name);
                    AddSearch(name, defaultImportAlias);
                }
            }
        }

        foreach (var decl in directImports ?? [])
        {
            HandleImport(decl);
        }

        return (importSearches.ToArray(), singleReferences.ToArray());
    }

    /// <summary>Go: <c>func getImportOrExportSymbol(node, symbol, checker, comingFromExport) *ImportExportSymbol</c>（importTracker.go:437-586）。</summary>
    internal static ImportExportSymbol? GetImportOrExportSymbol(
        Node node,
        Symbol symbol,
        TypeScript.Checker.Checker checker,
        bool comingFromExport)
    {
        ImportExportSymbol? ExportInfoOf(Symbol sym, ExportKind kind)
        {
            if (GetExportInfo(sym, kind, checker) is { } info)
            {
                return new ImportExportSymbol
                {
                    _kind = ImpExpKind.Export,
                    _symbol = sym,
                    _exportInfo = info,
                };
            }

            return null;
        }

        ImportExportSymbol? GetExport()
        {
            ImportExportSymbol? GetExportAssignmentExport(Node ex)
            {
                // Get the symbol for the `export =` node; its parent is the module it's the export of.
                if (ex.Symbol!.Parent is null)
                {
                    return null;
                }

                var exportKind = IfElse(ex.AsExportAssignment()!.IsExportEquals, ExportKind.ExportEquals, ExportKind.Default);
                return new ImportExportSymbol
                {
                    _kind = ImpExpKind.Export,
                    _symbol = symbol,
                    _exportInfo = new ExportInfo
                    {
                        _exportingModuleSymbol = ex.Symbol!.Parent,
                        _exportKind = exportKind,
                    },
                };
            }

            // Not meant for use with export specifiers or export assignment.
            ExportKind GetExportKindForDeclaration(Node n)
            {
                if (HasSyntacticModifier(n, ModifierFlags.Default))
                {
                    return ExportKind.Default;
                }

                return ExportKind.Named;
            }

            ImportExportSymbol? GetSpecialPropertyExport(Node n, bool useLhsSymbol)
            {
                ExportKind kind;
                switch (GetAssignmentDeclarationKind(n))
                {
                    case JSDeclarationKind.ExportsProperty:
                        kind = ExportKind.Named;
                        break;
                    case JSDeclarationKind.ModuleExports:
                        kind = ExportKind.ExportEquals;
                        break;
                    default:
                        return null;
                }

                var sym = symbol;
                if (useLhsSymbol)
                {
                    sym = n.Symbol!;
                }

                if (sym is null)
                {
                    return null;
                }

                return ExportInfoOf(sym, kind);
            }

            var parent = node.Parent!;
            var grandparent = parent.Parent!;
            if (symbol.ExportSymbol is not null)
            {
                if (IsPropertyAccessExpression(parent))
                {
                    // When accessing an export of a JS module, there's no alias. The symbol will still be flagged as an export even though we're at the use.
                    // So check that we are at the declaration.
                    if (IsBinaryExpression(grandparent) && (symbol.Declarations?.Contains(parent) ?? false))
                    {
                        return GetSpecialPropertyExport(grandparent, false /*useLhsSymbol*/);
                    }

                    return null;
                }

                return ExportInfoOf(symbol.ExportSymbol, GetExportKindForDeclaration(parent));
            }

            var exportNode = GetExportNode(parent, node);
            if (exportNode is not null && (HasSyntacticModifier(exportNode, ModifierFlags.Export) || IsImplicitlyExportedJSDocDeclaration(exportNode)))
            {
                if (IsImportEqualsDeclaration(exportNode) && exportNode.AsImportEqualsDeclaration()!.ModuleReference == node)
                {
                    // We're at `Y` in `export import X = Y`. This is not the exported symbol, the left-hand-side is. So treat this as an import statement.
                    if (comingFromExport)
                    {
                        return null;
                    }

                    var lhsSymbol = checker.GetSymbolAtLocation(exportNode.Name()!);
                    return new ImportExportSymbol
                    {
                        _kind = ImpExpKind.Import,
                        _symbol = lhsSymbol,
                    };
                }

                return ExportInfoOf(symbol, GetExportKindForDeclaration(exportNode));
            }

            if (IsNamespaceExport(parent))
            {
                return ExportInfoOf(symbol, ExportKind.Named);
            }

            if (IsExportAssignment(parent))
            {
                return GetExportAssignmentExport(parent);
            }

            if (IsExportAssignment(grandparent))
            {
                return GetExportAssignmentExport(grandparent);
            }

            if (IsBinaryExpression(parent))
            {
                return GetSpecialPropertyExport(parent, true /*useLhsSymbol*/);
            }

            if (IsBinaryExpression(grandparent))
            {
                return GetSpecialPropertyExport(grandparent, true /*useLhsSymbol*/);
            }

            if (IsJSDocTypedefTag(parent) || IsJSDocCallbackTag(parent))
            {
                return ExportInfoOf(symbol, ExportKind.Named);
            }

            return null;
        }

        ImportExportSymbol? GetImport()
        {
            if (!IsNodeImport(node))
            {
                return null;
            }

            // JS destructuring from `require(...)` is import-like for references, but the binding element
            // itself is still a local variable symbol rather than an alias.
            Symbol? importedSymbol;
            if ((symbol.Flags & SymbolFlags.Alias) != 0)
            {
                importedSymbol = checker.GetImmediateAliasedSymbol(symbol);
            }
            else
            {
                importedSymbol = GetPropertySymbolOfObjectBindingPatternWithoutPropertyName(symbol, checker);
            }

            if (importedSymbol is null)
            {
                return null;
            }

            // Search on the local symbol in the exporting module, not the exported symbol.
            importedSymbol = SkipExportSpecifierSymbol(importedSymbol, checker);
            if (importedSymbol is null)
            {
                return null;
            }

            // Similarly, skip past the symbol for 'export ='
            if (importedSymbol.Name == "export=")
            {
                importedSymbol = GetExportEqualsLocalSymbol(importedSymbol, checker);
                if (importedSymbol is null)
                {
                    return null;
                }
            }

            // If the import has a different name than the export, do not continue searching.
            // If `importedName` is undefined, do continue searching as the export is anonymous.
            // (All imports returned from this function will be ignored anyway if we are in rename and this is a not a named export.)
            var importedName = SymbolNameNoDefault(importedSymbol);
            if (importedName.Length == 0 || importedName == InternalSymbolNameDefault || importedName == symbol.Name)
            {
                return new ImportExportSymbol
                {
                    _kind = ImpExpKind.Import,
                    _symbol = importedSymbol,
                };
            }

            return null;
        }

        var result = GetExport();
        if (result is null && !comingFromExport)
        {
            result = GetImport();
        }

        return result;
    }

    /// <summary>Go: <c>func getExportInfo(exportSymbol *ast.Symbol, exportKind ExportKind, c *checker.Checker) *ExportInfo</c>（importTracker.go:588-601）。</summary>
    internal static ExportInfo? GetExportInfo(Symbol exportSymbol, ExportKind exportKind, TypeScript.Checker.Checker c)
    {
        // Parent can be nil if an `export` is not at the top-level (which is a compile error).
        if (exportSymbol.Parent is not null)
        {
            var exportingModuleSymbol = c.GetMergedSymbol(exportSymbol.Parent);
            // `export` may appear in a namespace. In that case, just rely on global search.
            if (exportingModuleSymbol is not null && TypeScript.Checker.Checker.IsExternalModuleSymbol(exportingModuleSymbol))
            {
                return new ExportInfo
                {
                    _exportingModuleSymbol = exportingModuleSymbol,
                    _exportKind = exportKind,
                };
            }
        }

        return null;
    }

    /// <summary>Go: <c>func getExportNode(parent, node *ast.Node) *ast.Node</c>（importTracker.go:603-619）。</summary>
    internal static Node? GetExportNode(Node parent, Node node)
    {
        Node? declaration = null;
        if (IsVariableDeclaration(parent))
        {
            declaration = parent;
        }
        else if (IsBindingElement(parent))
        {
            declaration = WalkUpBindingElementsAndPatterns(parent);
        }

        if (declaration is not null)
        {
            if (parent.Name() == node && !IsCatchClause(declaration.Parent!) && IsVariableStatement(declaration.Parent!.Parent!))
            {
                return declaration.Parent!.Parent;
            }

            return null;
        }

        return parent;
    }

    /// <summary>Go: <c>func isNodeImport(node *ast.Node) bool</c>（importTracker.go:621-635）。</summary>
    internal static bool IsNodeImport(Node node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case Kind.ImportEqualsDeclaration:
                return parent.Name() == node && IsExternalModuleImportEquals(parent);
            case Kind.ImportSpecifier:
                // For a rename import `{ foo as bar }`, don't search for the imported symbol. Just find local uses of `bar`.
                return parent.PropertyName() is null;
            case Kind.ImportClause:
            case Kind.NamespaceImport:
                Debug.Assert(parent.Name() == node, "Expected import clause name to match node");
                return true;
            case Kind.BindingElement:
                return IsInJSFile(node) && IsVariableDeclarationInitializedToBareOrAccessedRequire(parent.Parent!.Parent!);
        }

        return false;
    }

    /// <summary>Go: <c>func isExternalModuleImportEquals(node *ast.Node) bool</c>（importTracker.go:637-640）。</summary>
    internal static bool IsExternalModuleImportEquals(Node node)
    {
        var moduleReference = node.AsImportEqualsDeclaration()!.ModuleReference!;
        return IsExternalModuleReference(moduleReference) && moduleReference.Expression()!.Kind == Kind.StringLiteral;
    }

    /// <summary>Go: <c>func skipExportSpecifierSymbol(symbol *ast.Symbol, checker *checker.Checker) *ast.Symbol</c>（importTracker.go:642-657）。</summary>
    internal static Symbol? SkipExportSpecifierSymbol(Symbol symbol, TypeScript.Checker.Checker checker)
    {
        // For `export { foo } from './bar", there's nothing to skip, because it does not create a new alias. But `export { foo } does.
        foreach (var declaration in symbol.Declarations ?? [])
        {
            if (IsExportSpecifier(declaration) && declaration.PropertyName() is null && declaration.Parent!.Parent!.ModuleSpecifier() is null)
            {
                return OrElse(checker.GetExportSpecifierLocalTargetSymbol(declaration), symbol);
            }

            if (IsPropertyAccessExpression(declaration) && IsModuleExportsAccessExpression(declaration.Expression()!) && !IsPrivateIdentifier(declaration.Name()!))
            {
                // Export of form 'module.exports.propName = expr';
                return checker.GetSymbolAtLocation(declaration);
            }

            if (IsShorthandPropertyAssignment(declaration) && IsBinaryExpression(declaration.Parent!.Parent!) && GetAssignmentDeclarationKind(declaration.Parent!.Parent!) == JSDeclarationKind.ModuleExports)
            {
                return checker.GetExportSpecifierLocalTargetSymbol(declaration.Name()!);
            }
        }

        return symbol;
    }

    /// <summary>Go: <c>func getExportEqualsLocalSymbol(importedSymbol *ast.Symbol, checker *checker.Checker) *ast.Symbol</c>（importTracker.go:659-680）。</summary>
    internal static Symbol? GetExportEqualsLocalSymbol(Symbol importedSymbol, TypeScript.Checker.Checker checker)
    {
        if ((importedSymbol.Flags & SymbolFlags.Alias) != 0)
        {
            return checker.GetImmediateAliasedSymbol(importedSymbol);
        }

        var decl = importedSymbol.ValueDeclaration;
        Debug.Assert(decl is not null, "Expected value declaration");
        if (IsExportAssignment(decl!))
        {
            return decl!.Expression()!.Symbol;
        }

        if (IsBinaryExpression(decl!))
        {
            return decl!.AsBinaryExpression()!.Right!.Symbol;
        }

        if (IsSourceFile(decl!))
        {
            return decl!.Symbol;
        }

        return null;
    }

    /// <summary>Go: <c>func symbolNameNoDefault(symbol *ast.Symbol) string</c>（importTracker.go:682-693）。</summary>
    internal static string SymbolNameNoDefault(Symbol symbol)
    {
        if (symbol.Name != InternalSymbolNameDefault)
        {
            return symbol.Name;
        }

        foreach (var decl in symbol.Declarations ?? [])
        {
            var name = GetNameOfDeclaration(decl);
            if (name is not null && IsIdentifier(name))
            {
                return name.Text();
            }
        }

        return "";
    }

    /// <summary>Go: <c>func findModuleReferences(program, sourceFiles, searchModuleSymbol, checker) []ModuleReference</c>（importTracker.go:695-767）。</summary>
    internal static ModuleReference[] FindModuleReferences(
        Program program,
        SourceFile[]? sourceFiles,
        Symbol searchModuleSymbol,
        TypeScript.Checker.Checker checker)
    {
        var refs = new List<ModuleReference>();

        foreach (var referencingFile in sourceFiles ?? [])
        {
            var searchSourceFile = searchModuleSymbol.ValueDeclaration;
            if (searchSourceFile is not null && searchSourceFile.Kind == Kind.SourceFile)
            {
                // Check <reference path> directives
                foreach (var @ref in referencingFile.ReferencedFiles ?? [])
                {
                    if (program.GetSourceFileFromReference(referencingFile, @ref) == searchSourceFile.AsSourceFile())
                    {
                        refs.Add(new ModuleReference
                        {
                            _kind = ModuleReferenceKind.Reference,
                            _referencingFile = referencingFile,
                            _ref = @ref,
                        });
                    }
                }

                // Check <reference types> directives
                foreach (var @ref in referencingFile.TypeReferenceDirectives ?? [])
                {
                    var referenced = program.GetResolvedTypeReferenceDirectiveFromTypeReferenceDirective(@ref, referencingFile);
                    if (referenced is not null && referenced.ResolvedFileName == searchSourceFile.AsSourceFile()!.FileName())
                    {
                        refs.Add(new ModuleReference
                        {
                            _kind = ModuleReferenceKind.Reference,
                            _referencingFile = referencingFile,
                            _ref = @ref,
                        });
                    }
                }
            }

            // Check all imports (including require() calls)
            ForEachImport(program, referencingFile, (importDecl, moduleSpecifier) =>
            {
                var moduleSymbol = checker.GetSymbolAtLocation(moduleSpecifier);
                if (moduleSymbol == searchModuleSymbol)
                {
                    if (NodeIsSynthesized(importDecl))
                    {
                        refs.Add(new ModuleReference
                        {
                            _kind = ModuleReferenceKind.Implicit,
                            _literal = moduleSpecifier,
                            _referencingFile = referencingFile,
                        });
                    }
                    else
                    {
                        refs.Add(new ModuleReference
                        {
                            _kind = ModuleReferenceKind.Import,
                            _literal = moduleSpecifier,
                        });
                    }
                }
            });
        }

        return refs.ToArray();
    }
}
