// ported from: internal/modulespecifiers/specifiers.go:1-362（入口函数、环境模块名、Info、模块路径集合、GetEachFileNameOfModule）
//
// specifiers.go（1402 行）按 Go 的行范围拆成 6 个 partial 文件（docs/architecture.md §2 的"大文件拆分"）：
//   Specifiers.cs          1-362    入口 + ambient + Info + getAllModulePaths(Worker) + GetEachFileNameOfModule
//   Specifiers.Compute.cs  364-639  computeModuleSpecifiers + getLocalModuleSpecifier
//   Specifiers.Endings.cs  641-828  processEnding + rootDirs + node_modules 主流程
//   Specifiers.PackageJson.cs 830-1087 pkgJsonDirAttemptResult + tryDirectoryWithPackageJson + exports/imports 入口
//   Specifiers.Paths.cs    1089-1331 tryGetModuleNameFromPaths + validateEnding + tryGetModuleNameFromExportsOrImports
//   Specifiers.Public.cs   1333-1402 GetModuleSpecifier / UpdateModuleSpecifier / getModuleSpecifierWithPreferences
//
// 与 Go 的偏离（逐条）：
// 1) Go 的 `(T, bool)` 两返回值、`ResultKind` 这类枚举都在 types.go 里映射，见 Types.cs 的文件头。
// 2) `ast.GetSourceFileOfModule` 等 `ast` 包级函数用 `using static TypeScript.Ast.Ast;` + 裸名（docs §2：
//    `Ast.Xxx` 在外层命名空间里会被同名命名空间遮蔽）。
// 3) `strings.HasPrefix` → `string.StartsWith(StringComparison.Ordinal)`（Go 是纯字节前缀比较，.NET 默认
//    的区域敏感比较不能用）。
// 4) Go 在 `range` 里 delete map 是合法的；C# 不能边遍历边改，`GetAllModulePathsWorker` 里把删除延后到
//    遍历之后（遍历顺序不影响结果：每个目录桶之后会被排序）。
// 5) `slices.SortFunc`（Go 的不稳定 pdqsort）→ `List.Sort`（不稳定 introsort）：比较函数在 FileName 上
//    是全序（`comparePathsByRedirect` 最后按 FileName 比较），两侧都不存在平局，排序结果确定。
// 6) `map[string]ModulePath` 的零值语义与 C# 的 `Dictionary` 相同（键不存在即未命中）。
// 7) Go 的结构体返回值（`ambientModuleInfo`/`Info`）→ C# 见各自声明：`ambientModuleInfo` 是未导出类型，
//    用 private class；`Info` 是导出类型，用 class（带 slice 字段的类型用 class 才能保留 Go 的零值 ""，
//    见 Types.cs 文件头第 3 条）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>func GetModuleSpecifiers(moduleSymbol *ast.Symbol, checker CheckerShape, compilerOptions *core.CompilerOptions, importingSourceFile SourceFileForSpecifierGeneration, host ModuleSpecifierGenerationHost, userPreferences UserPreferences, options ModuleSpecifierOptions, forAutoImports bool) ModuleSpecifiersResult</c>。</summary>
    public static ModuleSpecifiersResult GetModuleSpecifiers(
        Symbol moduleSymbol,
        CheckerShape checker,
        CompilerOptions compilerOptions,
        SourceFileForSpecifierGeneration importingSourceFile,
        ModuleSpecifierGenerationHost host,
        UserPreferences userPreferences,
        ModuleSpecifierOptions options,
        bool forAutoImports)
    {
        return GetModuleSpecifiersWithInfo(
            moduleSymbol,
            checker,
            compilerOptions,
            importingSourceFile,
            host,
            userPreferences,
            options,
            forAutoImports);
    }

    /// <summary>Go: <c>func GetModuleSpecifiersWithInfo(...) ModuleSpecifiersResult</c>。</summary>
    public static ModuleSpecifiersResult GetModuleSpecifiersWithInfo(
        Symbol moduleSymbol,
        CheckerShape checker,
        CompilerOptions compilerOptions,
        SourceFileForSpecifierGeneration importingSourceFile,
        ModuleSpecifierGenerationHost host,
        UserPreferences userPreferences,
        ModuleSpecifierOptions options,
        bool forAutoImports)
    {
        var ambient = TryGetModuleNameFromAmbientModule(moduleSymbol, checker);
        if (ambient.Name.Length > 0)
        {
            if (forAutoImports && IsExcludedByRegex(ambient.Name, userPreferences.AutoImportSpecifierExcludeRegexes))
            {
                return new ModuleSpecifiersResult { Kind = ResultKind.Ambient, AmbientModuleSymbol = ambient.Symbol };
            }

            return new ModuleSpecifiersResult
            {
                Specifiers = [ambient.Name],
                Kind = ResultKind.Ambient,
                AmbientModuleSymbol = ambient.Symbol,
            };
        }

        var moduleSourceFile = GetSourceFileOfModule(moduleSymbol);
        if (moduleSourceFile is null)
        {
            return new ModuleSpecifiersResult();
        }

        // Use original source file name when file is from project reference output
        var moduleFileName = host.GetSourceOfProjectReferenceIfOutputIncluded(moduleSourceFile);

        var (specifiers, kind) = GetModuleSpecifiersForFileWithInfo(
            importingSourceFile,
            moduleFileName,
            compilerOptions,
            host,
            userPreferences,
            options,
            forAutoImports);
        return new ModuleSpecifiersResult { Specifiers = specifiers, Kind = kind };
    }

    /// <summary>Go: <c>func GetModuleSpecifiersForFileWithInfo(...) ([]string, ResultKind)</c>。</summary>
    public static (string[] Specifiers, ResultKind Kind) GetModuleSpecifiersForFileWithInfo(
        SourceFileForSpecifierGeneration importingSourceFile,
        string moduleFileName,
        CompilerOptions compilerOptions,
        ModuleSpecifierGenerationHost host,
        UserPreferences userPreferences,
        ModuleSpecifierOptions options,
        bool forAutoImports)
    {
        var modulePaths = GetAllModulePathsWorker(
            GetInfo(host.GetSourceOfProjectReferenceIfOutputIncluded(importingSourceFile), host),
            moduleFileName,
            host,
            compilerOptions,
            options);

        return ComputeModuleSpecifiers(
            modulePaths,
            compilerOptions,
            importingSourceFile,
            host,
            userPreferences,
            options,
            forAutoImports);
    }

    /// <summary>Go: <c>type ambientModuleInfo struct { name string; symbol *ast.Symbol }</c>（未导出）。</summary>
    private sealed class AmbientModuleInfo
    {
        public string Name = "";
        public Symbol? Symbol;
    }

    /// <summary>Go: <c>func tryGetModuleNameFromAmbientModule(moduleSymbol *ast.Symbol, checker CheckerShape) ambientModuleInfo</c>。</summary>
    private static AmbientModuleInfo TryGetModuleNameFromAmbientModule(Symbol moduleSymbol, CheckerShape checker)
    {
        // Go 的 nil slice 迭代 = 空循环；Symbol.Declarations 在 C# 里是 `Node[]?`。
        foreach (var declaration in moduleSymbol.Declarations ?? [])
        {
            if (IsModuleWithStringLiteralName(declaration)
                && (!IsModuleAugmentationExternal(declaration) || !IsExternalModuleNameRelative(declaration.Name()!.Text())))
            {
                return new AmbientModuleInfo { Name = declaration.Name()!.Text(), Symbol = moduleSymbol };
            }
        }

        // the module could be a namespace, which is export through "export=" from an ambient module.
        /**
         * declare module "m" {
         *     namespace ns {
         *         class c {}
         *     }
         *     export = ns;
         * }
         */
        // `import {c} from "m";` is valid, in which case, `moduleSymbol` is "ns", but the module name should be "m"
        foreach (var d in moduleSymbol.Declarations ?? [])
        {
            if (!IsModuleDeclaration(d))
            {
                continue;
            }

            var possibleContainer = FindAncestor(d, node => IsModuleWithStringLiteralName(node));
            if (possibleContainer is null || possibleContainer.Parent is null || !IsSourceFile(possibleContainer.Parent))
            {
                continue;
            }

            var containerSymbol = possibleContainer.Symbol;
            var exports = containerSymbol?.Exports;
            if (exports is null || !exports.TryGetValue(InternalSymbolNameExportEquals, out var sym) || sym is null)
            {
                continue;
            }

            var exportAssignmentDecl = sym.ValueDeclaration;
            if (exportAssignmentDecl is null || exportAssignmentDecl.Kind != Kind.ExportAssignment)
            {
                continue;
            }

            // Go 直接传 exportAssignmentDecl.Expression()（为 nil 时由 checker 崩）；C# 的可空性要求显式处理，
            // 这里按"取不到符号"跳过（比 Go 的崩溃更宽，见交付报告）。
            var exportExpression = exportAssignmentDecl.Expression();
            if (exportExpression is null)
            {
                continue;
            }

            var exportSymbol = checker.GetSymbolAtLocation(exportExpression);
            if (exportSymbol is null)
            {
                continue;
            }

            if ((exportSymbol.Flags & SymbolFlags.Alias) != 0)
            {
                exportSymbol = checker.GetAliasedSymbol(exportSymbol);
            }

            // TODO: Possible strada bug - isn't this insufficient in the presence of merge symbols?
            if (ReferenceEquals(exportSymbol, d.Symbol))
            {
                return new AmbientModuleInfo
                {
                    // `IsModuleWithStringLiteralName(possibleContainer)` 已保证 Name() 是 StringLiteral，非 nil。
                    Name = possibleContainer.Name()!.Text(),
                    Symbol = possibleContainer.Symbol,
                };
            }
        }

        return new AmbientModuleInfo();
    }

    /// <summary>Go: <c>type Info struct { UseCaseSensitiveFileNames bool; ImportingSourceFileName string; SourceDirectory string }</c>。</summary>
    public sealed class Info
    {
        public bool UseCaseSensitiveFileNames;
        public string ImportingSourceFileName = "";
        public string SourceDirectory = "";
    }

    /// <summary>Go: <c>func getInfo(importingSourceFileName string, host ModuleSpecifierGenerationHost) Info</c>。</summary>
    private static Info GetInfo(string importingSourceFileName, ModuleSpecifierGenerationHost host)
    {
        var sourceDirectory = GetDirectoryPath(importingSourceFileName);
        return new Info
        {
            ImportingSourceFileName = importingSourceFileName,
            SourceDirectory = sourceDirectory,
            UseCaseSensitiveFileNames = host.UseCaseSensitiveFileNames(),
        };
    }

    /// <summary>Go: <c>func getAllModulePaths(info Info, importedFileName string, host ModuleSpecifierGenerationHost, compilerOptions *core.CompilerOptions, preferences UserPreferences, options ModuleSpecifierOptions) []ModulePath</c>。</summary>
    private static ModulePath[] GetAllModulePaths(
        Info info,
        string importedFileName,
        ModuleSpecifierGenerationHost host,
        CompilerOptions compilerOptions,
        UserPreferences preferences,
        ModuleSpecifierOptions options)
    {
        // !!! use new cache model（Go 里这段被注释掉的 module specifier cache 不移植：
        //   `host.getModuleSpecifierCache()` 对应 Go 里 `// GetModuleResolutionCache() any // !!! TODO`）
        var modulePaths = GetAllModulePathsWorker(info, importedFileName, host, compilerOptions, options);

        return modulePaths;
    }

    /// <summary>Go: <c>func getAllModulePathsWorker(info Info, importedFileName string, host ModuleSpecifierGenerationHost, compilerOptions *core.CompilerOptions, options ModuleSpecifierOptions) []ModulePath</c>。</summary>
    private static ModulePath[] GetAllModulePathsWorker(
        Info info,
        string importedFileName,
        ModuleSpecifierGenerationHost host,
        CompilerOptions compilerOptions,
        ModuleSpecifierOptions options)
    {
        var allFileNames = new Dictionary<string, ModulePath>();
        var paths = GetEachFileNameOfModule(info.ImportingSourceFileName, importedFileName, host, preferSymlinks: true);
        foreach (var p in paths)
        {
            allFileNames[p.FileName] = p;
        }

        var useCaseSensitiveFileNames = info.UseCaseSensitiveFileNames;
        Comparison<ModulePath> comparePaths = (a, b) => ComparePathsByRedirect(a, b, useCaseSensitiveFileNames);

        // Sort by paths closest to importing file Name directory
        var sortedPaths = new List<ModulePath>(paths.Length);
        for (var directory = info.SourceDirectory; allFileNames.Count != 0;)
        {
            var directoryStart = EnsureTrailingDirectorySeparator(directory);
            List<ModulePath> pathsInDirectory = [];
            List<string>? matchedFileNames = null;
            foreach (var (fileName, p) in allFileNames)
            {
                if (fileName.StartsWith(directoryStart, StringComparison.Ordinal))
                {
                    pathsInDirectory.Add(p);
                    // Go 在 range 里直接 delete；C# 不能边遍历边改集合，延后删除（见文件头第 4 条）。
                    (matchedFileNames ??= []).Add(fileName);
                }
            }

            if (matchedFileNames is not null)
            {
                foreach (var fileName in matchedFileNames)
                {
                    allFileNames.Remove(fileName);
                }
            }

            if (pathsInDirectory.Count > 0)
            {
                pathsInDirectory.Sort(comparePaths);
                sortedPaths.AddRange(pathsInDirectory);
            }

            var newDirectory = GetDirectoryPath(directory);
            if (newDirectory == directory)
            {
                break;
            }

            directory = newDirectory;
        }

        if (allFileNames.Count > 0)
        {
            var remainingPaths = new List<ModulePath>(allFileNames.Values);
            remainingPaths.Sort(comparePaths);
            sortedPaths.AddRange(remainingPaths);
        }

        return sortedPaths.ToArray();
    }

    /// <summary>Go: <c>func containsIgnoredPath(s string) bool</c>（Go 里刻意复刻 tspath.ContainsIgnoredPath 的本地副本）。</summary>
    internal static bool ContainsIgnoredPath(string s)
    {
        return s.Contains("/node_modules/.", StringComparison.Ordinal)
            || s.Contains("/.git", StringComparison.Ordinal)
            || s.Contains(".#", StringComparison.Ordinal);
    }

    /// <summary>Go: <c>func ContainsNodeModules(s string) bool</c>。</summary>
    public static bool ContainsNodeModules(string s) => s.Contains("/node_modules/", StringComparison.Ordinal);

    /// <summary>
    /// Go: <c>func GetEachFileNameOfModule(importingFileName string, importedFileName string, host ModuleSpecifierGenerationHost, preferSymlinks bool) []ModulePath</c>。
    /// Returns all possible file paths for a module, including symlink alternatives. This function handles symlink
    /// resolution and provides multiple path options for module resolution.
    /// </summary>
    public static ModulePath[] GetEachFileNameOfModule(
        string importingFileName,
        string importedFileName,
        ModuleSpecifierGenerationHost host,
        bool preferSymlinks)
    {
        var cwd = host.GetCurrentDirectory();
        var importedPath = ToPath(importedFileName, cwd, host.UseCaseSensitiveFileNames());
        var referenceRedirect = "";
        var outputAndReference = host.GetProjectReferenceFromSource(importedPath);
        if (outputAndReference is not null && outputAndReference.OutputDts != "")
        {
            referenceRedirect = outputAndReference.OutputDts;
        }

        var redirects = host.GetRedirectTargets(importedPath);
        var importedFileNames = new List<string>(2 + redirects.Length);
        if (referenceRedirect.Length > 0)
        {
            importedFileNames.Add(referenceRedirect);
        }

        importedFileNames.Add(importedFileName);
        importedFileNames.AddRange(redirects);
        // Go: core.Map(importedFileNames, func(f string) string { return tspath.GetNormalizedAbsolutePath(f, cwd) })
        // —— C# 用等价的显式循环，省掉 List.ToArray() 那次中间分配（§3.8）。
        var targets = new string[importedFileNames.Count];
        for (var i = 0; i < targets.Length; i++)
        {
            targets[i] = GetNormalizedAbsolutePath(importedFileNames[i], cwd);
        }
        var shouldFilterIgnoredPaths = !Every(targets, ContainsIgnoredPath);

        var results = new List<ModulePath>(2);
        if (!preferSymlinks)
        {
            foreach (var p in targets)
            {
                if (!(shouldFilterIgnoredPaths && ContainsIgnoredPath(p)))
                {
                    results.Add(new ModulePath
                    {
                        FileName = p,
                        IsInNodeModules = ContainsNodeModules(p),
                        IsRedirect = referenceRedirect == p,
                    });
                }
            }
        }

        var symlinkCache = host.GetSymlinkCache();
        var fullImportedFileName = GetNormalizedAbsolutePath(importedFileName, cwd);
        if (symlinkCache is not null)
        {
            ForEachAncestorDirectoryStoppingAtGlobalCache<bool>(
                host.GetGlobalTypingsCacheLocation(),
                GetDirectoryPath(fullImportedFileName),
                realPathDirectory =>
                {
                    var (symlinkSet, ok) = symlinkCache.DirectoriesByRealpath().Load(
                        ToPath(realPathDirectory, cwd, host.UseCaseSensitiveFileNames()).EnsureTrailingDirectorySeparator());
                    if (!ok || symlinkSet is null)
                    {
                        return (false, false); // Continue to ancestor directory
                    }

                    // Don't want to a package to globally import from itself (importNameCodeFix_symlink_own_package.ts)
                    if (StartsWithDirectory(importingFileName, realPathDirectory, host.UseCaseSensitiveFileNames()))
                    {
                        return (false, true); // Stop search, each ancestor directory will also hit this condition
                    }

                    foreach (var target in targets)
                    {
                        if (!StartsWithDirectory(target, realPathDirectory, host.UseCaseSensitiveFileNames()))
                        {
                            continue;
                        }

                        var relative = GetRelativePathFromDirectory(
                            realPathDirectory,
                            target,
                            new ComparePathsOptions(host.UseCaseSensitiveFileNames(), cwd));
                        symlinkSet.Range(symlinkDirectory =>
                        {
                            var option = ResolvePath(symlinkDirectory, relative);
                            results.Add(new ModulePath
                            {
                                FileName = option,
                                IsInNodeModules = ContainsNodeModules(option),
                                IsRedirect = target == referenceRedirect,
                            });
                            // We found a non-ignored path in symlinks, so we can reject ignored-path realpaths
                            shouldFilterIgnoredPaths = true;
                            return true;
                        });
                    }

                    return (false, false);
                });
        }

        if (preferSymlinks)
        {
            foreach (var p in targets)
            {
                if (!(shouldFilterIgnoredPaths && ContainsIgnoredPath(p)))
                {
                    results.Add(new ModulePath
                    {
                        FileName = p,
                        IsInNodeModules = ContainsNodeModules(p),
                        IsRedirect = referenceRedirect == p,
                    });
                }
            }
        }

        return results.ToArray();
    }
}
