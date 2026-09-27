// ported from: internal/ls/autoimport/extract.go
//
// 与 Go 的偏离（逐条）：
// 1) `checker.*` 的包级函数与类型在 C# 里与命名空间 `TypeScript.Checker` 同名，裸写 `Checker` 会命中命名空间
//    （§2 的坑）→ 类型一律全限定 `TypeScript.Checker.Checker`。
// 2) Go 的 `*[]*Export`（切片指针，供多个 helper 追加）→ C# 的 `ref List<Export>`；`slices.Grow` 的预占容量
//    由 `List` 的扩容承担（语义无差），`slices.Delete` → `List.RemoveAt`（Go 用 -1 越界 panic 的分支在 C# 里
//    是 `ArgumentOutOfRangeException`，同样是 panic）。
// 3) Go 的内嵌 `symbolExtractor`（`exportExtractor` 嵌入 `*symbolExtractor`）→ C# 基类继承
//    （`ExportExtractor : SymbolExtractor`，§3.2 的"嵌入 → 继承"）。
// 4) 未导出类型 `symbolExtractor`/`exportExtractor`/`extractorStats`/`checkerLease` → C# 的
//    `SymbolExtractor`/`ExportExtractor`/`ExtractorStats`/`CheckerLease`（§2：未导出类型按 C# 习惯命名）。
// 5) `atomic.Int32` → `Interlocked`（§3.2）；读取用 `Volatile.Read`。
// 6) `module.DefaultResolver.ResolveModuleName` 在 C# 里返回 `(ResolvedModule? Module, DiagAndArgs[] Traces)`
//    （§3.2），且 `(nil).IsResolved()` 的 nil 接收者分支要显式写成 `resolved is not null && resolved.IsResolved()`。
// 7) `lsutil.GetSymbolKind/GetSymbolModifiers` 在 C# 里第一个参数是 `Checker?`（Go 传的是可能为 nil 的
//    `checkerLease.TryChecker()`）。
// 8) `ModuleID(...)`/`string(...)` 显式转换（ModuleID 是命名 string 类型的 C# 化，见 Export.cs 文件头）。
// 9) **Go 的"遍历 nil map"语义在 C# 不成立**：Go 里 `for k, v := range nilMap` 是零次迭代、`nilMap[k]` 返回零值，
//    C# 的 `Dictionary` 为 null 时迭代/索引直接 **NRE**。`Symbol.Exports` 在 Go 侧完全可能是 nil（无导出的模块），
//    所以四处 `for ... range X.Exports` 都写成 `X.Exports ?? new SymbolTable()`（第 128/174/551/583 行）。
//    唯一保留 `expression.Symbol!.Members![…]` 的地方是"Go 也会 panic"的等价路径（nil map 读取得到 nil symbol
//    再传给 createExport），不是语义偏离。
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Core;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Module.Module;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type extractorStats struct { exports, usedChecker atomic.Int32 }</c>（extract.go:41-44）。</summary>
internal sealed class ExtractorStats
{
    internal int _exports;

    internal int _usedChecker;

    /// <summary>Go: <c>stats.exports.Add(n)</c>。</summary>
    internal void AddExports(int n) => Interlocked.Add(ref _exports, n);

    /// <summary>Go: <c>stats.usedChecker.Add(n)</c>。</summary>
    internal void AddUsedChecker(int n) => Interlocked.Add(ref _usedChecker, n);

    /// <summary>Go: <c>stats.exports.Load()</c>。</summary>
    internal int Exports => Volatile.Read(ref _exports);

    /// <summary>Go: <c>stats.usedChecker.Load()</c>。</summary>
    internal int UsedChecker => Volatile.Read(ref _usedChecker);
}

/// <summary>Go: <c>type symbolExtractor struct</c>（extract.go:15-28）。</summary>
internal class SymbolExtractor
{
    internal string _packageName = "";

    internal ExtractorStats _stats = new();

    internal NameResolver _localNameResolver = null!;

    internal TypeScript.Checker.Checker _checker = null!;

    internal Func<string, Path>? _toPath;

    /// <summary>realpath, if set, is used to resolve symlinks for ModuleID generation.
    /// This ensures that symlinked packages use their realpath as ModuleID,
    /// deduplicating exports from files that appear via multiple symlink paths.</summary>
    internal Func<string, string>? _realpath;

    internal SymbolExtractor(string packageName, TypeScript.Checker.Checker checker, Func<string, Path>? toPath, Func<string, string>? realpath)
    {
        _packageName = packageName;
        _checker = checker;
        _localNameResolver = new NameResolver { CompilerOptions = EmptyCompilerOptions };
        _stats = new ExtractorStats();
        _toPath = toPath;
        _realpath = realpath;
    }

    internal ExtractorStats Stats() => _stats;

    /// <summary>Go: <c>func (e *symbolExtractor) getModuleID(file *ast.SourceFile) ModuleID</c>（extract.go:71-77）。</summary>
    internal ModuleID GetModuleID(SourceFile file)
    {
        if (_realpath is not null && _toPath is not null)
        {
            var realpath = _realpath(file.FileName());
            return (ModuleID)(string)_toPath(realpath);
        }
        return (ModuleID)(string)file.Path();
    }

    /// <summary>Go: <c>func (e *symbolExtractor) getModuleIDForSymbol(symbol *ast.Symbol) (ModuleID, bool)</c>（extract.go:79-93）。</summary>
    internal bool TryGetModuleIDForSymbol(Symbol symbol, out ModuleID moduleID)
    {
        moduleID = default;
        if (!AutoImport.TryGetModuleIDAndFileNameOfModuleSymbol(symbol, out var id, out var fileName))
        {
            return false;
        }
        // If fileName is set, this is a source file that may need realpath normalization
        if (fileName != "" && _realpath is not null)
        {
            var decl = GetNonAugmentationDeclaration(symbol);
            if (decl is not null && decl.Kind == Kind.SourceFile)
            {
                moduleID = GetModuleID(decl.AsSourceFile()!);
                return true;
            }
        }
        moduleID = id;
        return true;
    }

    /// <summary>Go: <c>func (e *symbolExtractor) extractFromSymbol(...)</c>（extract.go:172-260）。</summary>
    internal void ExtractFromSymbol(string name, Symbol symbol, ModuleID moduleID, string moduleFileName, SourceFile file, ref List<Export> exports)
    {
        if (ShouldIgnoreSymbol(symbol))
        {
            return;
        }

        if (name == InternalSymbolNameExportStar)
        {
            var checkerLease = new CheckerLease { _checker = _checker };
            var allExports = new List<Symbol>(_checker.GetExportsOfModule(symbol.Parent!));
            // allExports includes named exports from the file that will be processed separately;
            // we want to add only the ones that come from the star
            foreach (var (exportName, namedExport) in symbol.Parent!.Exports ?? new SymbolTable())
            {
                if (exportName != InternalSymbolNameExportStar)
                {
                    var idx = allExports.IndexOf(namedExport);
                    if (idx >= 0 || ShouldIgnoreSymbol(namedExport))
                    {
                        allExports.RemoveAt(idx);
                    }
                }
            }

            foreach (var reexportedSymbol in allExports)
            {
                var (export, _) = CreateExport(reexportedSymbol, moduleID, moduleFileName, ExportSyntax.Star, file, checkerLease);
                if (export is not null)
                {
                    var parent = checkerLease.GetChecker().GetMergedSymbol(reexportedSymbol.Parent);
                    if (parent is not null && parent.IsExternalModule())
                    {
                        if (TryGetModuleIDForSymbol(parent, out var targetModuleID))
                        {
                            export.Target = new ExportID(targetModuleID, reexportedSymbol.Name);
                        }
                    }
                    export._through = InternalSymbolNameExportStar;
                    exports.Add(export);
                }
            }
            return;
        }

        var syntax = GetSyntax(symbol);
        var lease = new CheckerLease { _checker = _checker };
        var (createdExport, target) = CreateExport(symbol, moduleID, moduleFileName, syntax, file, lease);
        if (createdExport is null)
        {
            return;
        }

        exports.Add(createdExport);

        if (target is not null)
        {
            if (syntax == ExportSyntax.Equals && (target.Flags & SymbolFlags.Namespace) != 0)
            {
                foreach (var (innerName, namedExport) in target.Exports ?? new SymbolTable())
                {
                    if (innerName != InternalSymbolNameExportStar)
                    {
                        var (innerExport, _) = CreateExport(namedExport, moduleID, moduleFileName, syntax, file, lease);
                        if (innerExport is not null)
                        {
                            innerExport._through = name;
                            exports.Add(innerExport);
                        }
                    }
                }
            }
        }
        else if (syntax == ExportSyntax.CommonJSModuleExports)
        {
            var expression = symbol.Declarations![0].AsBinaryExpression()!.Right!;
            if (expression.Kind == Kind.ObjectLiteralExpression)
            {
                // what is actually desirable here? I think it would be reasonable to only treat these as exports
                // if *every* property is a shorthand property or identifier: identifier
                // At least, it would be sketchy if there were any methods, computed properties...
                foreach (var prop in expression.AsObjectLiteralExpression()!.Properties!.Nodes ?? [])
                {
                    if (IsShorthandPropertyAssignment(prop) || (IsPropertyAssignment(prop) && prop.AsPropertyAssignment()!.Name()!.Kind == Kind.Identifier))
                    {
                        var (propExport, _) = CreateExport(
                            expression.Symbol!.Members![prop.Name()!.Text()],
                            moduleID,
                            moduleFileName,
                            syntax,
                            file,
                            lease);
                        if (propExport is not null)
                        {
                            propExport._through = name;
                            exports.Add(propExport);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Go: <c>func (e *symbolExtractor) createExport(...)</c>（extract.go:262-345）。</summary>
    internal (Export? Export, Symbol? Target) CreateExport(Symbol symbol, ModuleID moduleID, string moduleFileName, ExportSyntax syntax, SourceFile file, CheckerLease checkerLease)
    {
        if (ShouldIgnoreSymbol(symbol))
        {
            return (null, null);
        }

        var export = new Export
        {
            ExportName = symbol.Name,
            ModuleID = moduleID,
            ModuleFileName = moduleFileName,
            Syntax = syntax,
            Flags = symbol.CombinedLocalAndExportSymbolFlags(),
            Path = file.Path(),
            PackageName = _packageName,
        };

        if (syntax == ExportSyntax.UMD)
        {
            export.ExportName = InternalSymbolNameExportEquals;
            export._localName = symbol.Name;
        }

        Symbol? targetSymbol = null;
        if ((symbol.Flags & SymbolFlags.Alias) != 0)
        {
            targetSymbol = TryResolveSymbol(symbol, syntax, checkerLease);
            if (targetSymbol is not null)
            {
                Node? decl = null;
                if (targetSymbol.Declarations is { Length: > 0 })
                {
                    decl = targetSymbol.Declarations[0];
                }
                else if ((targetSymbol.CheckFlags & CheckFlags.Mapped) != 0)
                {
                    var mappedDecl = checkerLease.GetChecker().GetMappedTypeSymbolOfProperty(targetSymbol);
                    if (mappedDecl is not null && mappedDecl.Declarations is { Length: > 0 })
                    {
                        decl = mappedDecl.Declarations[0];
                    }
                }
                if (decl is null)
                {
                    // !!! consider GetImmediateAliasedSymbol to go as far as we can
                    decl = symbol.Declarations![0];
                }
                if (decl is null)
                {
                    Debug.Fail("no declaration for aliased symbol");
                    throw new System.Diagnostics.UnreachableException();
                }

                var parent = targetSymbol.Parent;
                var checker = checkerLease.TryChecker();
                if (checker is not null)
                {
                    export.Flags = checker.GetSymbolFlags(targetSymbol);
                    export.IsTypeOnly = checker.GetTypeOnlyAliasDeclaration(symbol) is not null;
                    parent = checker.GetMergedSymbol(parent);
                }
                else
                {
                    export.Flags = targetSymbol.Flags;
                    export.IsTypeOnly = Some(symbol.Declarations, IsPartOfTypeOnlyImportOrExportDeclaration);
                }
                export.ScriptElementKind = GetSymbolKind(checkerLease.TryChecker(), targetSymbol, decl);
                export.ScriptElementKindModifiers = GetSymbolModifiers(checkerLease.TryChecker(), targetSymbol);
                var targetModuleID = (ModuleID)(string)GetSourceFileOfNode(decl)!.Path();
                if (parent is not null && parent.IsExternalModule())
                {
                    if (TryGetModuleIDForSymbol(parent, out var id))
                    {
                        targetModuleID = id;
                    }
                }
                export.Target = new ExportID(targetModuleID, targetSymbol.Name);
            }
        }
        else
        {
            export.ScriptElementKind = GetSymbolKind(checkerLease.TryChecker(), symbol, symbol.Declarations![0]);
            export.ScriptElementKindModifiers = GetSymbolModifiers(checkerLease.TryChecker(), symbol);
        }

        if (symbol.Name == InternalSymbolNameDefault || symbol.Name == InternalSymbolNameExportEquals)
        {
            var namedSymbol = symbol;
            if (TypeScript.Binder.Binder.GetLocalSymbolForExportDefault(symbol) is { } localSymbol)
            {
                namedSymbol = localSymbol;
            }
            export._localName = AutoImport.GetDefaultLikeExportNameFromDeclaration(namedSymbol);
            if (IsUnusableName(export._localName))
            {
                export._localName = export.Target.ExportName;
            }
            if (IsUnusableName(export._localName))
            {
                if (targetSymbol is not null)
                {
                    namedSymbol = targetSymbol;
                    if (TypeScript.Binder.Binder.GetLocalSymbolForExportDefault(targetSymbol) is { } innerLocalSymbol)
                    {
                        namedSymbol = innerLocalSymbol;
                    }
                    export._localName = AutoImport.GetDefaultLikeExportNameFromDeclaration(namedSymbol);
                }
            }
            if (IsUnusableName(export._localName))
            {
                // Last resort: derive identifier from the file name. Use FileName() (original
                // casing) rather than ModuleID/Path() which is lowercased on case-insensitive
                // file systems, losing PascalCase.
                export._localName = ModuleSpecifierToValidIdentifier(FileNameForDefaultExportName(targetSymbol, moduleFileName, moduleID), false);
            }
        }

        if (IsUnusableName(export.Name()))
        {
            return (null, null);
        }

        _stats.AddExports(1);
        if (checkerLease.TryChecker() is not null)
        {
            _stats.AddUsedChecker(1);
        }

        return (export, targetSymbol);
    }

    /// <summary>Go: <c>func (e *symbolExtractor) tryResolveSymbol(...)</c>（extract.go:347-386）。</summary>
    internal Symbol? TryResolveSymbol(Symbol symbol, ExportSyntax syntax, CheckerLease checkerLease)
    {
        if (!IsNonLocalAlias(symbol, SymbolFlags.None))
        {
            return symbol;
        }

        Node? loc = null;
        var name = "";
        switch (syntax)
        {
            case ExportSyntax.Named:
            {
                var decl = GetDeclarationOfKind(symbol, Kind.ExportSpecifier)!;
                if (decl.Parent!.Parent!.AsExportDeclaration()!.ModuleSpecifier is null)
                {
                    var n = FirstNonZero(decl.Name(), decl.PropertyName());
                    if (n is not null && n.Kind == Kind.Identifier)
                    {
                        loc = n;
                        name = n.Text();
                    }
                }
                break;
            }
            // !!! check if module.exports = foo is marked as an alias
            case ExportSyntax.Equals:
                if (symbol.Name != InternalSymbolNameExportEquals)
                {
                    break;
                }
                goto case ExportSyntax.DefaultDeclaration;
            case ExportSyntax.DefaultDeclaration:
            {
                var decl = GetDeclarationOfKind(symbol, Kind.ExportAssignment)!;
                if (decl.Expression()!.Kind == Kind.Identifier)
                {
                    loc = decl.Expression();
                    name = loc!.Text();
                }
                break;
            }
        }

        if (loc is not null)
        {
            var local = _localNameResolver.Resolve(loc, name, SymbolFlags.All, null, false, false);
            if (local is not null && !IsNonLocalAlias(local, SymbolFlags.None))
            {
                return local;
            }
        }

        var checker = checkerLease.GetChecker();
        var resolved = checker.GetAliasedSymbol(symbol);
        if (!checker.IsUnknownSymbol(resolved))
        {
            return resolved;
        }
        return null;
    }

    /// <summary>Go: <c>func shouldIgnoreSymbol(symbol *ast.Symbol) bool</c>（extract.go:388-393）。</summary>
    internal static bool ShouldIgnoreSymbol(Symbol symbol) => (symbol.Flags & SymbolFlags.Prototype) != 0;

    /// <summary>Go: <c>func getSyntax(symbol *ast.Symbol) ExportSyntax</c>（extract.go:395-421）。</summary>
    internal static ExportSyntax GetSyntax(Symbol symbol)
    {
        foreach (var decl in symbol.Declarations ?? [])
        {
            switch (decl.Kind)
            {
                case Kind.ExportSpecifier:
                    return ExportSyntax.Named;
                case Kind.ExportAssignment:
                    return decl.AsExportAssignment()!.IsExportEquals ? ExportSyntax.Equals : ExportSyntax.DefaultDeclaration;
                case Kind.NamespaceExportDeclaration:
                    return ExportSyntax.UMD;
                case Kind.BinaryExpression:
                    switch (GetAssignmentDeclarationKind(decl))
                    {
                        case JSDeclarationKind.ModuleExports:
                            return ExportSyntax.CommonJSModuleExports;
                        case JSDeclarationKind.ExportsProperty:
                            return ExportSyntax.CommonJSExportsProperty;
                    }
                    break;
                default:
                    if ((GetCombinedModifierFlags(decl) & ModifierFlags.Default) != 0)
                    {
                        return ExportSyntax.DefaultModifier;
                    }
                    return ExportSyntax.Modifier;
            }
        }
        return ExportSyntax.None;
    }

    /// <summary>Go: <c>func isUnusableName(name string) bool</c>（extract.go:423-429）。</summary>
    internal static bool IsUnusableName(string? name) =>
        name is null || name == "" ||
        name == "_default" ||
        name == InternalSymbolNameExportStar ||
        name == InternalSymbolNameDefault ||
        name == InternalSymbolNameExportEquals;

    /// <summary>Go: <c>func fileNameForDefaultExportName(targetSymbol *ast.Symbol, moduleFileName string, moduleID ModuleID) string</c>（extract.go:431-446）。</summary>
    internal static string FileNameForDefaultExportName(Symbol? targetSymbol, string moduleFileName, ModuleID moduleID)
    {
        if (targetSymbol is not null && targetSymbol.Declarations is { Length: > 0 })
        {
            var fn = GetSourceFileOfNode(targetSymbol.Declarations[0])?.FileName();
            if (!string.IsNullOrEmpty(fn))
            {
                return fn;
            }
        }
        if (moduleFileName != "")
        {
            return moduleFileName;
        }
        return (string)moduleID;
    }
}

/// <summary>Go: <c>type exportExtractor struct { *symbolExtractor; moduleResolver *module.DefaultResolver }</c>（extract.go:30-33）。</summary>
internal sealed class ExportExtractor : SymbolExtractor
{
    internal TypeScript.Module.DefaultResolver _moduleResolver;

    internal ExportExtractor(string packageName, TypeScript.Checker.Checker checker, TypeScript.Module.DefaultResolver moduleResolver, Func<string, string>? realpath, Func<string, Path>? toPath)
        : base(packageName, checker, toPath, realpath)
    {
        _moduleResolver = moduleResolver;
    }

    /// <summary>Go: <c>func (e *exportExtractor) extractFromFile(file *ast.SourceFile) []*Export</c>（extract.go:95-117）。</summary>
    internal Export[] ExtractFromFile(SourceFile file)
    {
        if (file.Symbol is not null)
        {
            return ExtractFromModule(file);
        }
        if (file.AmbientModuleNames is { Length: > 0 })
        {
            var exportCount = 0;
            foreach (var statement in file.Statements?.Nodes ?? [])
            {
                if (IsModuleWithStringLiteralName(statement) && IsNonPatternAmbientModuleDeclaration(file, statement.AsModuleDeclaration()!))
                {
                    exportCount += statement.AsModuleDeclaration()!.Symbol?.Exports?.Count ?? 0;
                }
            }
            var exports = new List<Export>(exportCount);
            foreach (var statement in file.Statements?.Nodes ?? [])
            {
                if (IsModuleWithStringLiteralName(statement) && IsNonPatternAmbientModuleDeclaration(file, statement.AsModuleDeclaration()!))
                {
                    ExtractFromModuleDeclaration(statement.AsModuleDeclaration()!, file, (ModuleID)statement.Name()!.Text(), "", ref exports);
                }
            }
            return exports.ToArray();
        }
        return [];
    }

    /// <summary>Go: <c>func isNonPatternAmbientModuleDeclaration(file, decl) bool</c>（extract.go:119-126）。</summary>
    internal static bool IsNonPatternAmbientModuleDeclaration(SourceFile file, ModuleDeclaration decl)
    {
        foreach (var module in file.PatternAmbientModules ?? [])
        {
            if (module.Symbol == decl.Symbol)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Go: <c>func (e *exportExtractor) extractFromModule(file *ast.SourceFile) []*Export</c>（extract.go:128-155）。</summary>
    internal Export[] ExtractFromModule(SourceFile file)
    {
        var moduleAugmentations = MapNonNil(file.ModuleAugmentations, name =>
        {
            var decl = name.Parent!;
            if (IsGlobalScopeAugmentation(decl))
            {
                return null;
            }
            return decl.AsModuleDeclaration();
        });
        var augmentationExportCount = 0;
        foreach (var decl in moduleAugmentations)
        {
            augmentationExportCount += decl!.Symbol?.Exports?.Count ?? 0;
        }
        var moduleID = GetModuleID(file);
        var exports = new List<Export>((file.Symbol!.Exports?.Count ?? 0) + augmentationExportCount);
        foreach (var (name, symbol) in file.Symbol.Exports ?? new SymbolTable())
        {
            ExtractFromSymbol(name, symbol, moduleID, file.FileName(), file, ref exports);
        }
        foreach (var decl in moduleAugmentations)
        {
            var name = decl!.Name()!.AsStringLiteral()!.Text;
            var augmentationModuleID = (ModuleID)name;
            var moduleFileName = "";
            if (IsExternalModuleNameRelative(name))
            {
                var (resolved, _) = _moduleResolver.ResolveModuleName(name, file.FileName(), ModuleKind.CommonJS, null);
                if (resolved is not null && resolved.IsResolved())
                {
                    moduleFileName = resolved.ResolvedFileName;
                    augmentationModuleID = (ModuleID)(string)_toPath!(moduleFileName);
                }
                else
                {
                    // :shrug:
                    moduleFileName = ResolvePath(GetDirectoryPath(file.FileName()), name);
                    augmentationModuleID = (ModuleID)(string)_toPath!(moduleFileName);
                }
            }
            ExtractFromModuleDeclaration(decl, file, augmentationModuleID, moduleFileName, ref exports);
        }
        return exports.ToArray();
    }

    /// <summary>Go: <c>func (e *exportExtractor) extractFromModuleDeclaration(...)</c>（extract.go:157-161）。</summary>
    internal void ExtractFromModuleDeclaration(ModuleDeclaration decl, SourceFile file, ModuleID moduleID, string moduleFileName, ref List<Export> exports)
    {
        foreach (var (name, symbol) in decl.Symbol!.Exports ?? new SymbolTable())
        {
            ExtractFromSymbol(name, symbol, moduleID, moduleFileName, file, ref exports);
        }
    }
}

/// <summary>Go: <c>type checkerLease struct { used bool; checker *checker.Checker }</c>（extract.go:46-49）。</summary>
internal sealed class CheckerLease
{
    internal bool _used;

    internal TypeScript.Checker.Checker? _checker;

    /// <summary>Go: <c>func (l *checkerLease) GetChecker() *checker.Checker</c>（extract.go:51-54）。</summary>
    public TypeScript.Checker.Checker GetChecker()
    {
        _used = true;
        return _checker!;
    }

    /// <summary>Go: <c>func (l *checkerLease) TryChecker() *checker.Checker</c>（extract.go:56-61）。</summary>
    public TypeScript.Checker.Checker? TryChecker() => _used ? _checker : null;
}
