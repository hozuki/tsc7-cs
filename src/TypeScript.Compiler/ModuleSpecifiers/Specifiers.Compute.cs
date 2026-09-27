// ported from: internal/modulespecifiers/specifiers.go:364-639（computeModuleSpecifiers + getLocalModuleSpecifier）
//
// 与 Go 的偏离（在 Specifiers.cs 文件头的 7 条之外，本分片特有的）：
// 1) `resolvedModule.IsResolved()`：Go 的方法**自带 nil 检查**，C# 的实例方法不能在 null 上分派（§3.2），
//    调用点写成 `resolvedModule is not null && resolvedModule.IsResolved()`。
// 2) `tspath.Path` 是 `record struct`（TSPath/Path.cs），Go 的 `==` / `ContainsPath` 直接对应 `==` / 方法调用。
// 3) `string(projectDirectory)`（Go 的 Path → string 转换）→ `projectDirectory.Value`。
// 4) `preferences.getAllowedEndingsInPreferredOrder(importMode)` → 属性调用（见 Preferences.cs 文件头第 1 条）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>func computeModuleSpecifiers(modulePaths []ModulePath, compilerOptions *core.CompilerOptions, importingSourceFile SourceFileForSpecifierGeneration, host ModuleSpecifierGenerationHost, userPreferences UserPreferences, options ModuleSpecifierOptions, forAutoImport bool) ([]string, ResultKind)</c>。</summary>
    private static (string[] Specifiers, ResultKind Kind) ComputeModuleSpecifiers(
        ModulePath[] modulePaths,
        CompilerOptions compilerOptions,
        SourceFileForSpecifierGeneration importingSourceFile,
        ModuleSpecifierGenerationHost host,
        UserPreferences userPreferences,
        ModuleSpecifierOptions options,
        bool forAutoImport)
    {
        var info = GetInfo(importingSourceFile.FileName(), host);
        var preferences = GetModuleSpecifierPreferences(userPreferences, host, compilerOptions, importingSourceFile, "");

        var existingSpecifier = "";
        foreach (var modulePath in modulePaths)
        {
            var targetPath = ToPath(modulePath.FileName, host.GetCurrentDirectory(), info.UseCaseSensitiveFileNames);
            Node? existingImport = null;
            foreach (var importSpecifier in importingSourceFile.Imports())
            {
                var resolvedModule = host.GetResolvedModuleFromModuleSpecifier(importingSourceFile, importSpecifier);
                if (resolvedModule is not null && resolvedModule.IsResolved()
                    && ToPath(resolvedModule.ResolvedFileName, host.GetCurrentDirectory(), info.UseCaseSensitiveFileNames) == targetPath)
                {
                    existingImport = importSpecifier;
                    break;
                }
            }

            if (existingImport is not null)
            {
                if (preferences.RelativePreference == RelativePreferenceKind.NonRelative && PathIsRelative(existingImport.Text()))
                {
                    // If the preference is for non-relative and the module specifier is relative, ignore it
                    continue;
                }

                var existingMode = host.GetModeForUsageLocation(importingSourceFile, existingImport);
                var targetMode = options.OverrideImportMode;
                if (targetMode == ResolutionModeNone)
                {
                    targetMode = host.GetDefaultResolutionModeForFile(importingSourceFile);
                }

                if (existingMode != targetMode && existingMode != ResolutionModeNone && targetMode != ResolutionModeNone)
                {
                    // If the candidate import mode doesn't match the mode we're generating for, don't consider it
                    continue;
                }

                existingSpecifier = existingImport.Text();
                break;
            }
        }

        if (existingSpecifier != "")
        {
            return ([existingSpecifier], ResultKind.None);
        }

        var importedFileIsInNodeModules = Some(modulePaths, p => p.IsInNodeModules);

        // Module specifier priority:
        //   1. "Bare package specifiers" (e.g. "@foo/bar") resulting from a path through node_modules to a package.json's "types" entry
        //   2. Specifiers generated using "paths" from tsconfig
        //   3. Non-relative specfiers resulting from a path through node_modules (e.g. "@foo/bar/path/to/file")
        //   4. Relative paths
        List<string> pathsSpecifiers = [];
        List<string> redirectPathsSpecifiers = [];
        List<string> nodeModulesSpecifiers = [];
        List<string> relativeSpecifiers = [];

        foreach (var modulePath in modulePaths)
        {
            var specifier = "";
            if (modulePath.IsInNodeModules)
            {
                specifier = TryGetModuleNameAsNodeModule(modulePath, info, importingSourceFile, host, compilerOptions, userPreferences, packageNameOnly: false, options.OverrideImportMode);
            }

            if (specifier.Length > 0 && !(forAutoImport && IsExcludedByRegex(specifier, preferences.ExcludeRegexes)))
            {
                nodeModulesSpecifiers.Add(specifier);
                if (modulePath.IsRedirect)
                {
                    // If we got a specifier for a redirect, it was a bare package specifier (e.g. "@foo/bar",
                    // not "@foo/bar/path/to/file"). No other specifier will be this good, so stop looking.
                    return (nodeModulesSpecifiers.ToArray(), ResultKind.NodeModules);
                }
            }

            var importMode = options.OverrideImportMode;
            if (importMode == ResolutionModeNone)
            {
                importMode = host.GetDefaultResolutionModeForFile(importingSourceFile);
            }

            var local = GetLocalModuleSpecifier(
                modulePath.FileName,
                info,
                compilerOptions,
                host,
                importMode,
                preferences,
                pathsOnly: modulePath.IsRedirect || specifier.Length > 0);
            if (local.Length == 0 || (forAutoImport && IsExcludedByRegex(local, preferences.ExcludeRegexes)))
            {
                continue;
            }

            if (modulePath.IsRedirect)
            {
                redirectPathsSpecifiers.Add(local);
            }
            else if (PathIsBareSpecifier(local))
            {
                if (ContainsNodeModules(local))
                {
                    // We could be in this branch due to inappropriate use of `baseUrl`, not intentional `paths`
                    // usage. It's impossible to reason about where to prioritize baseUrl-generated module
                    // specifiers, but if they contain `/node_modules/`, they're going to trigger a portability
                    // error, so *at least* don't prioritize those.
                    relativeSpecifiers.Add(local);
                }
                else
                {
                    pathsSpecifiers.Add(local);
                }
            }
            else if (forAutoImport || !importedFileIsInNodeModules || modulePath.IsInNodeModules)
            {
                // Why this extra conditional, not just an `else`? If some path to the file contained
                // 'node_modules', but we can't create a non-relative specifier (e.g. "@foo/bar/path/to/file"),
                // that means we had to go through a *sibling's* node_modules, not one we can access directly.
                // If some path to the file was in node_modules but another was not, this likely indicates that
                // we have a monorepo structure with symlinks. In this case, the non-nodeModules path is
                // probably the realpath, e.g. "../bar/path/to/file", but a relative path to another package
                // in a monorepo is probably not portable. So, the module specifier we actually go with will be
                // the relative path through node_modules, so that the declaration emitter can produce a
                // portability error. (See declarationEmitReexportedSymlinkReference3)
                relativeSpecifiers.Add(local);
            }
        }

        if (pathsSpecifiers.Count > 0)
        {
            return (pathsSpecifiers.ToArray(), ResultKind.Paths);
        }

        if (redirectPathsSpecifiers.Count > 0)
        {
            return (redirectPathsSpecifiers.ToArray(), ResultKind.Redirect);
        }

        if (nodeModulesSpecifiers.Count > 0)
        {
            return (nodeModulesSpecifiers.ToArray(), ResultKind.NodeModules);
        }

        return (relativeSpecifiers.ToArray(), ResultKind.Relative);
    }

    /// <summary>Go: <c>func getLocalModuleSpecifier(moduleFileName string, info Info, compilerOptions *core.CompilerOptions, host ModuleSpecifierGenerationHost, importMode core.ResolutionMode, preferences ModuleSpecifierPreferences, pathsOnly bool) string</c>。</summary>
    private static string GetLocalModuleSpecifier(
        string moduleFileName,
        Info info,
        CompilerOptions compilerOptions,
        ModuleSpecifierGenerationHost host,
        ModuleKind importMode,
        ModuleSpecifierPreferences preferences,
        bool pathsOnly)
    {
        var paths = compilerOptions.Paths;
        var rootDirs = compilerOptions.RootDirs;

        if (pathsOnly && paths is null)
        {
            return "";
        }

        var sourceDirectory = info.SourceDirectory;

        var allowedEndings = preferences.GetAllowedEndingsInPreferredOrder(importMode);
        var relativePath = "";
        if (rootDirs is not null && rootDirs.Length > 0)
        {
            relativePath = TryGetModuleNameFromRootDirs(rootDirs, moduleFileName, sourceDirectory, allowedEndings, compilerOptions, host);
        }

        if (relativePath.Length == 0)
        {
            relativePath = ProcessEnding(
                EnsurePathIsNonModuleName(GetRelativePathFromDirectory(
                    sourceDirectory,
                    moduleFileName,
                    new ComparePathsOptions(host.UseCaseSensitiveFileNames(), host.GetCurrentDirectory()))),
                allowedEndings,
                compilerOptions,
                host);
        }

        if ((paths is null && !compilerOptions.GetResolvePackageJsonImports()) || preferences.RelativePreference == RelativePreferenceKind.Relative)
        {
            if (pathsOnly)
            {
                return "";
            }

            return relativePath;
        }

        var root = compilerOptions.GetPathsBasePath(host.GetCurrentDirectory());
        var baseDirectory = GetNormalizedAbsolutePath(root, host.GetCurrentDirectory());
        var relativeToBaseUrl = GetRelativePathIfInSameVolume(moduleFileName, baseDirectory, host.UseCaseSensitiveFileNames());
        if (relativeToBaseUrl.Length == 0)
        {
            if (pathsOnly)
            {
                return "";
            }

            return relativePath;
        }

        var fromPackageJsonImports = "";
        if (!pathsOnly)
        {
            fromPackageJsonImports = TryGetModuleNameFromPackageJsonImports(
                moduleFileName,
                sourceDirectory,
                compilerOptions,
                host,
                importMode,
                PrefersTsExtension(allowedEndings));
        }

        var fromPaths = "";
        if ((pathsOnly || fromPackageJsonImports.Length == 0) && paths is not null)
        {
            fromPaths = TryGetModuleNameFromPaths(
                relativeToBaseUrl,
                paths,
                allowedEndings,
                baseDirectory,
                host,
                compilerOptions);
        }

        if (pathsOnly)
        {
            return fromPaths;
        }

        string maybeNonRelative;
        if (fromPackageJsonImports.Length > 0)
        {
            maybeNonRelative = fromPackageJsonImports;
        }
        else
        {
            maybeNonRelative = fromPaths;
        }

        if (maybeNonRelative.Length == 0)
        {
            return relativePath;
        }

        var relativeIsExcluded = IsExcludedByRegex(relativePath, preferences.ExcludeRegexes);
        var nonRelativeIsExcluded = IsExcludedByRegex(maybeNonRelative, preferences.ExcludeRegexes);
        if (!relativeIsExcluded && nonRelativeIsExcluded)
        {
            return relativePath;
        }

        if (relativeIsExcluded && !nonRelativeIsExcluded)
        {
            return maybeNonRelative;
        }

        if (preferences.RelativePreference == RelativePreferenceKind.NonRelative && !PathIsRelative(maybeNonRelative))
        {
            return maybeNonRelative;
        }

        if (preferences.RelativePreference == RelativePreferenceKind.ExternalNonRelative && !PathIsRelative(maybeNonRelative))
        {
            TSPath.Path projectDirectory;
            if (compilerOptions.ConfigFilePath.Length > 0)
            {
                projectDirectory = ToPath(GetDirectoryPath(compilerOptions.ConfigFilePath), host.GetCurrentDirectory(), host.UseCaseSensitiveFileNames());
            }
            else
            {
                projectDirectory = ToPath(host.GetCurrentDirectory(), host.GetCurrentDirectory(), host.UseCaseSensitiveFileNames());
            }

            var canonicalSourceDirectory = ToPath(sourceDirectory, host.GetCurrentDirectory(), host.UseCaseSensitiveFileNames());
            var modulePath = ToPath(moduleFileName, projectDirectory.Value, host.UseCaseSensitiveFileNames());

            var sourceIsInternal = projectDirectory.ContainsPath(canonicalSourceDirectory);
            var targetIsInternal = projectDirectory.ContainsPath(modulePath);
            if ((sourceIsInternal && !targetIsInternal) || (!sourceIsInternal && targetIsInternal))
            {
                // 1. The import path crosses the boundary of the tsconfig.json-containing directory.
                //
                //      src/
                //        tsconfig.json
                //        index.ts -------
                //      lib/              | (path crosses tsconfig.json)
                //        imported.ts <---
                //
                return maybeNonRelative;
            }

            var nearestTargetPackageJson = host.GetNearestAncestorDirectoryWithPackageJson(GetDirectoryPath(modulePath.Value));
            var nearestSourcePackageJson = host.GetNearestAncestorDirectoryWithPackageJson(sourceDirectory);

            if (!PackageJsonPathsAreEqual(
                nearestTargetPackageJson,
                nearestSourcePackageJson,
                new ComparePathsOptions(host.UseCaseSensitiveFileNames(), host.GetCurrentDirectory())))
            {
                // 2. The importing and imported files are part of different packages.
                //
                //      packages/a/
                //        package.json
                //        index.ts --------
                //      packages/b/        | (path crosses package.json)
                //        package.json     |
                //        component.ts <---
                //
                return maybeNonRelative;
            }

            return relativePath;
        }

        // Prefer a relative import over a baseUrl import if it has fewer components.
        if (IsPathRelativeToParent(maybeNonRelative) || CountPathComponents(relativePath) < CountPathComponents(maybeNonRelative))
        {
            return relativePath;
        }

        return maybeNonRelative;
    }
}
