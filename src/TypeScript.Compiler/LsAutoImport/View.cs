// ported from: internal/ls/autoimport/view.go（view.go:1-264）+ specifiers.go（同一个 Go package 的
// "为一个 Export 算出模块说明符"逻辑，C# 侧与 Go 一样拆成两个文件：View.cs / Specifiers.cs）。
//
// 与 Go 的偏离（逐条）：
// 1) `NewView` 是导出的包级函数 → C# 的静态类 `AutoImport` 上的静态方法（§2）。
// 2) `QueryKind` 的 iota 常量去前缀（§3.3）：`QueryKindWordPrefix` → `QueryKind.WordPrefix`。
// 3) 未导出方法 `search` → `SearchWorker`（避免与导出的 `Search(query, kind)` 混淆；与 Go 的 `x` / `xWorker`
//    改名惯例一致）；未导出字段一律 `_camelCase`（§2）。
// 4) Go 的 `map[exportGroupKey][]*Export` → C# 的 `Dictionary<ExportGroupKey, List<Export>>` + 私有
//    `readonly record struct ExportGroupKey`（Go 的匿名结构体键）。
// 5) `min(a, b)`（Go 1.21 内建，作用于 int 型枚举）→ `(ExportSyntax)Math.Min((int)a, (int)b)` /
//    `(ScriptElementKind)…`（C# 没有枚举的 min）。
// 6) `rune(name[0])`：Go 取的是**首字节**，C# 取的是**首个 UTF-16 代码单元**；ASCII 名字下完全一致，
//    只有非 ASCII 首字符（如 "é"）在 JSX 的大小写判定上可能不同（Go 会看到 UTF-8 首字节 0xC3='Ã'）。
// 7) `lsutil.ShouldUseUriStyleNodeCoreModules` 等 lsutil 包级函数用 `using static` 的裸名（§2）。
// 8) `GetFixes(...)` 的 `usagePosition *lsproto.Position`（可空指针）→ `Position?`。
// 9) `GetCompletions` 里 Go 用 `map[exportGroupKey][]*Export` 分组、遍历顺序随机；C# 用 `Dictionary`（插入序稳定）
//    ⇒ 分组遍历顺序是确定性的。Go 自己的注释说明最终顺序由 `CompareFixesForSorting` 决定（"we only need to
//    produce a stable relative ordering"），所以这是**确定性增强**，不改变可观察的排序契约。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Module;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Module.Module;
using static TypeScript.Scanner.Scanner;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type QueryKind int</c>（view.go:82-88）。</summary>
public enum QueryKind : int
{
    WordPrefix = 0,
    ExactMatch = 1,
    CaseInsensitiveMatch = 2,
}

/// <summary>Go: <c>type FixAndExport struct { Fix *Fix; Export *Export }</c>（view.go:216-219）。</summary>
public sealed class FixAndExport
{
    public Fix? Fix;

    public Export? Export;
}

/// <summary>Go: <c>type View struct</c>（view.go:21-34）：一次自动导入请求的只读视图。</summary>
public sealed partial class View
{
    internal Registry _registry;

    internal SourceFile _importingFile;

    internal Path _importingFilePath;

    internal TypeScript.Compiler.Program _program;

    internal TypeScript.Checker.Checker _checker;

    internal TypeScript.ModuleSpecifiers.UserPreferences _preferences;

    internal ProjectID _projectID;

    internal TypeScript.ModuleSpecifiers.ModuleSpecifierEnding[]? _allowedEndings;

    internal Set<string> _conditions;

    internal Tristate _shouldUseUriStyleNodeCoreModules;

    internal MultiMap<ModuleID, ExistingImport>? _existingImports;

    internal bool? _shouldUseRequireForFixes;

    internal View(
        Registry registry,
        SourceFile importingFile,
        ProjectID projectID,
        TypeScript.Compiler.Program program,
        TypeScript.Checker.Checker typeChecker,
        TypeScript.ModuleSpecifiers.UserPreferences preferences,
        Path importingFilePath,
        Set<string> conditions,
        Tristate shouldUseUriStyleNodeCoreModules)
    {
        _registry = registry;
        _importingFile = importingFile;
        _importingFilePath = importingFilePath;
        _program = program;
        _checker = typeChecker;
        _projectID = projectID;
        _preferences = preferences;
        _conditions = conditions;
        _shouldUseUriStyleNodeCoreModules = shouldUseUriStyleNodeCoreModules;
    }

    /// <summary>Go: <c>func (v *View) getAllowedEndings() []modulespecifiers.ModuleSpecifierEnding</c>（view.go:57-67）。</summary>
    internal TypeScript.ModuleSpecifiers.ModuleSpecifierEnding[] GetAllowedEndings()
    {
        if (_allowedEndings is null)
        {
            var resolutionMode = _program.GetDefaultResolutionModeForFile(_importingFile);
            _allowedEndings = TypeScript.ModuleSpecifiers.ModuleSpecifiers.GetAllowedEndingsInPreferredOrder(
                _preferences,
                _program,
                _program.Options(),
                _importingFile,
                "",
                resolutionMode);
        }
        return _allowedEndings;
    }

    /// <summary>Go: <c>func (v *View) Search(query string, kind QueryKind) []*Export</c>（view.go:90-107）。</summary>
    public Export[] Search(string query, QueryKind kind)
    {
        Export[] SearchFn(RegistryBucket bucket) => kind switch
        {
            QueryKind.WordPrefix => bucket.Index!.SearchWordPrefix(query),
            QueryKind.ExactMatch => bucket.Index!.Find(query, true),
            QueryKind.CaseInsensitiveMatch => bucket.Index!.Find(query, false),
            _ => throw Unreachable("unreachable"),
        };

        return SearchWorker(SearchFn);
    }

    /// <summary>Go: <c>func (v *View) SearchByExportID(id ExportID) []*Export</c>（view.go:109-116）。</summary>
    public Export[] SearchByExportID(ExportID id)
    {
        Export[] SearchFn(RegistryBucket bucket) => Filter(bucket.Index!._entries.ToArray(), e => e.ExportID == id);

        return SearchWorker(SearchFn);
    }

    /// <summary>Go: <c>func (v *View) search(searchFn func(*RegistryBucket) []*Export) []*Export</c>（view.go:118-171）。</summary>
    internal Export[] SearchWorker(Func<RegistryBucket, Export[]> searchFn)
    {
        List<Export>? results = null;

        if (_registry._projects.TryGetValue(_projectID, out var bucket))
        {
            var exports = searchFn(bucket);
            results = new List<Export>(exports.Length);
            foreach (var e in exports)
            {
                if ((string)e.ModuleID == (string)_importingFile.Path())
                {
                    // Don't auto-import from the importing file itself
                    continue;
                }
                results.Add(e);
            }
        }

        // Compute the set of packages accessible to the importing file.
        // This includes packages from package.json dependencies (aggregated from ancestor directories)
        // plus packages that are directly imported by the project's program files.
        // If no package.json is found, allowedPackages remains nil and all packages are allowed.
        Set<string>? allowedPackages = null;
        _ = _importingFile.Path().GetDirectoryPath().ForEachAncestorDirectory<object?>(dirPath =>
        {
            if (_registry._directories.TryGetValue(dirPath, out var dir))
            {
                var pj = dir._packageJson;
                if (pj is not null && pj.Exists() && pj.Contents is { Parseable: true } contents)
                {
                    // Initialize to empty set if this is the first package.json we've seen
                    allowedPackages ??= new Set<string>();
                    AutoImport.AddPackageJsonDependencies(contents, allowedPackages);
                }
            }
            return (null, false);
        }, out _);
        // If we found at least one package.json, also include packages directly imported by the project
        if (allowedPackages is not null)
        {
            if (_registry._projects.TryGetValue(_projectID, out var projectBucket))
            {
                allowedPackages = allowedPackages.UnionedWith(projectBucket.ResolvedPackageNames);
            }
        }

        var excludePackages = new Set<string>();
        _ = _importingFile.Path().GetDirectoryPath().ForEachAncestorDirectory<object?>(dirPath =>
        {
            if (_registry._nodeModules.TryGetValue(dirPath, out var nodeModulesBucket))
            {
                var exports = searchFn(nodeModulesBucket);
                results ??= new List<Export>(exports.Length);
                foreach (var e in exports)
                {
                    // Exclude packages found in lower node_modules (shadowing)
                    if (excludePackages.Has(e.PackageName))
                    {
                        continue;
                    }
                    // If allowedPackages is nil, no package.json was found, so include all packages.
                    // Otherwise, only include packages that are dependencies or directly imported.
                    if (allowedPackages is not null && !allowedPackages.Has(e.PackageName))
                    {
                        continue;
                    }
                    results.Add(e);
                }

                // As we go up the directory tree, exclude packages found in lower node_modules
                if (nodeModulesBucket.PackageFiles is not null)
                {
                    foreach (var pkgName in nodeModulesBucket.PackageFiles.Keys)
                    {
                        excludePackages.Add(pkgName);
                    }
                }
            }
            return (null, false);
        }, out _);
        return results?.ToArray() ?? [];
    }

    /// <summary>Go 的匿名结构体键 <c>exportGroupKey</c>（view.go:222-226）。</summary>
    private readonly record struct ExportGroupKey(ExportID Target, string Name, string AmbientModuleOrPackageName);

    /// <summary>Go: <c>func (v *View) GetCompletions(prefix string, position lsproto.Position, forJSX, isTypeOnlyLocation bool) []*FixAndExport</c>（view.go:221-264）。</summary>
    public FixAndExport[] GetCompletions(string prefix, Position position, bool forJSX, bool isTypeOnlyLocation)
    {
        var results = Search(prefix, QueryKind.WordPrefix);

        var grouped = new Dictionary<ExportGroupKey, List<Export>>(results.Length);
        foreach (var e in results)
        {
            var name = e.Name();
            if (!IsIdentifierText(name, LanguageVariant.Standard))
            {
                continue;
            }
            if (forJSX && !(RuneUtil.IsUpperRune(name[0]) || e.IsRenameable()))
            {
                continue;
            }
            var target = e.ExportID;
            if (e.Target != default)
            {
                target = e.Target;
            }
            // Go: view.go:205（分组键 `ambientModuleOrPackageName: core.FirstNonZero(e.AmbientModuleName(), e.PackageName)`，
            // 分组循环见 view.go:190-217）。Go 的 FirstNonZero 按 `*new(T)` 判零 ⇒ 空串不是"非零值"。
            // Core.FirstNonZero 的 string 重载保持该语义（否则 `pkg=""` 与 `pkg="dom7"` 的同一 target 导出会被并进
            // 同一组，MinAllFunc 会挤掉其中一条，见 TestCompletionsImportDefaultExportCrash2 / …_addToNamedWithDifferentCacheValue）。
            var key = new ExportGroupKey(
                target,
                name,
                FirstNonZero(e.AmbientModuleName(), e.PackageName));
            if (e.PackageName == "@types/node" || ((string)e.Path).Contains("/node_modules/@types/node/", StringComparison.Ordinal))
            {
                if (UnprefixedNodeCoreModules.ContainsKey(key.AmbientModuleOrPackageName))
                {
                    // Group URI-style and non-URI style node core modules together so the ranking logic
                    // is allowed to drop one if an explicit preference is detected.
                    key = key with { AmbientModuleOrPackageName = "node:" + key.AmbientModuleOrPackageName };
                }
            }
            if (grouped.TryGetValue(key, out var existing))
            {
                var replaced = false;
                for (var i = 0; i < existing.Count; i++)
                {
                    var ex = existing[i];
                    if (e.ExportID == ex.ExportID)
                    {
                        existing[i] = new Export
                        {
                            ModuleID = e.ExportID.ModuleID,
                            ExportName = e.ExportID.ExportName,
                            ModuleFileName = e.ModuleFileName,
                            PackageName = e.PackageName,
                            IsTypeOnly = e.IsTypeOnly || ex.IsTypeOnly,
                            Syntax = (ExportSyntax)Math.Min((int)e.Syntax, (int)ex.Syntax),
                            Flags = e.Flags | ex.Flags,
                            ScriptElementKind = (ScriptElementKind)Math.Min((int)e.ScriptElementKind, (int)ex.ScriptElementKind),
                            ScriptElementKindModifiers = e.ScriptElementKindModifiers | ex.ScriptElementKindModifiers,
                            _localName = e._localName,
                            Target = e.Target,
                            Path = e.Path,
                        };
                        replaced = true;
                        break;
                    }
                }
                if (replaced)
                {
                    continue;
                }
            }
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }
            list.Add(e);
        }

        var fixes = new List<FixAndExport>(results.Length);
        int CompareFixes(FixAndExport a, FixAndExport b) => CompareFixesForRanking(a.Fix!, b.Fix!);

        foreach (var exps in grouped.Values)
        {
            var fixesForGroup = new List<FixAndExport>(exps.Count);
            foreach (var e in exps)
            {
                foreach (var fix in GetFixes(e, forJSX, isTypeOnlyLocation, position))
                {
                    fixesForGroup.Add(new FixAndExport { Fix = fix, Export = e });
                }
            }
            fixes.AddRange(MinAllFunc(fixesForGroup.ToArray(), CompareFixes));
        }

        // The client will do additional sorting by SortText and Label, so we don't
        // need to consider the name in our sorting here; we only need to produce a
        // stable relative ordering between completions that the client will consider
        // equivalent.
        fixes.Sort((a, b) => CompareFixesForSorting(a.Fix!, b.Fix!));

        return fixes.ToArray();
    }

    private static Exception Unreachable(string message)
    {
        Debug.Fail(message);
        return new System.Diagnostics.UnreachableException();
    }
}

/// <summary>Go 的 <c>autoimport</c> 包（包级函数/常量放这里，见 docs/architecture.md §2）。</summary>
public static partial class AutoImport
{
    /// <summary>Go: <c>func NewView(registry *Registry, importingFile *ast.SourceFile, projectID ProjectID, program *compiler.Program, typeChecker *checker.Checker, preferences modulespecifiers.UserPreferences) *View</c>（view.go:36-55）。</summary>
    public static View NewView(
        Registry registry,
        SourceFile importingFile,
        ProjectID projectID,
        TypeScript.Compiler.Program program,
        TypeScript.Checker.Checker typeChecker,
        TypeScript.ModuleSpecifiers.UserPreferences preferences)
    {
        var importingFilePath = importingFile.Path();
        if (importingFile.CanonicalSourceFile() is { } canonical)
        {
            importingFilePath = canonical.Path();
        }
        return new View(
            registry,
            importingFile,
            projectID,
            program,
            typeChecker,
            preferences,
            importingFilePath,
            NewSetFromItems(GetConditions(program.Options(), program.GetDefaultResolutionModeForFile(importingFile))),
            ShouldUseUriStyleNodeCoreModules(importingFile, program));
    }
}
