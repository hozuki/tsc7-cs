// ported from: internal/modulespecifiers/specifiers.go:830-1087（pkgJsonDirAttemptResult + tryDirectoryWithPackageJson + exports/imports 入口）
//
// 与 Go 的偏离（在 Specifiers.cs 文件头的 7 条之外，本分片特有的）：
// 1) `packageJsonContent.Fields.Xxx`：Go 的 `Fields` 内嵌 `PathFields`/`HeaderFields`（字段提升），C# 不能提升
//    → 写 `HeaderFields.Types` / `PathFields.Exports`（ModuleStack 切片定的形状，见其交付说明）。
// 2) `versionPaths`（Go 的零值 `VersionPaths` 结构体，`Exists()` 为 false）→ C# 的 `VersionPaths` 是 class，
//    这里用 `VersionPaths? versionPaths = null` + `?.GetPaths()`，与 Go 的 `versionPaths.GetPaths() == nil` 等价。
// 3) `module.MatchPatternOrExact(...) != core.Pattern{}`：C# 的 `Pattern` 结构体没有定义 `==`，
//    按 Go 的零值含义（Text == "" && StarIndex == 0）写成 `IsZeroPattern`。
// 4) `packageJsonContent.GetContents()` 等返回可空（Go 里是 nil 指针）→ 显式判空；Go 在某些位置会
//    nil 解引用崩溃，C# 按"没有 package.json 内容"返回空（更宽，逐处注释）。
// 5) `OrderedMap.Entries()` 的 V 是 `V?`（可空注解）→ 取值处用 `!`（该 map 的值由 JSON 解码而来，
//    恒非 null；Go 的 V 是结构体，不存在 nil）。
using TypeScript.Core;
using TypeScript.PackageJson;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>type pkgJsonDirAttemptResult struct</c>（未导出；C# 用 class 以保留 Go 的零值 ""）。</summary>
    private sealed class PkgJsonDirAttemptResult
    {
        public string ModuleFileToTry = "";
        public string PackageRootPath = "";
        public bool BlockedByExports;
        public bool VerbatimFromExports;
    }

    /// <summary>Go: <c>func tryDirectoryWithPackageJson(parts NodeModulePathParts, pathObj ModulePath, importingSourceFile SourceFileForSpecifierGeneration, host ModuleSpecifierGenerationHost, overrideMode core.ResolutionMode, options *core.CompilerOptions, allowedEndings []ModuleSpecifierEnding) pkgJsonDirAttemptResult</c>。</summary>
    private static PkgJsonDirAttemptResult TryDirectoryWithPackageJson(
        NodeModulePathParts parts,
        ModulePath pathObj,
        SourceFileForSpecifierGeneration importingSourceFile,
        ModuleSpecifierGenerationHost host,
        ModuleKind overrideMode,
        CompilerOptions options,
        ModuleSpecifierEnding[] allowedEndings)
    {
        var rootIdx = parts.PackageRootIndex;
        if (rootIdx == -1)
        {
            rootIdx = pathObj.FileName.Length; // TODO: possible strada bug? -1 in js slice removes characters from the end, in go it panics - js behavior seems unwanted here?
        }

        var packageRootPath = pathObj.FileName[..rootIdx];
        var packageJsonPath = CombinePaths(packageRootPath, "package.json");
        var moduleFileToTry = pathObj.FileName;
        var maybeBlockedByTypesVersions = false;
        var packageJson = host.GetPackageJsonInfo(packageJsonPath);
        if (packageJson is null)
        {
            // No package.json exists; an index.js will still resolve as the package name
            var fileName = moduleFileToTry[(parts.PackageRootIndex + 1)..];
            if (fileName is "index.d.ts" or "index.js" or "index.ts" or "index.tsx")
            {
                return new PkgJsonDirAttemptResult { ModuleFileToTry = moduleFileToTry, PackageRootPath = packageRootPath };
            }

            return new PkgJsonDirAttemptResult { ModuleFileToTry = moduleFileToTry };
        }

        var importMode = overrideMode;
        if (importMode == ResolutionModeNone)
        {
            importMode = host.GetDefaultResolutionModeForFile(importingSourceFile);
        }

        var packageJsonContent = packageJson.GetContents();
        if (options.GetResolvePackageJsonExports())
        {
            // The package name that we found in node_modules could be different from the package
            // name in the package.json content via url/filepath dependency specifiers. We need to
            // use the actual directory name, so don't look at `packageJsonContent.name` here.
            var nodeModulesDirectoryName = packageRootPath[(parts.TopLevelPackageNameIndex + 1)..];
            var packageName = GetPackageNameFromTypesPackageName(nodeModulesDirectoryName);

            // Determine resolution mode for package.json exports condition matching.
            // TypeScript's tryDirectoryWithPackageJson uses the importing file's mode (moduleSpecifiers.ts:1257),
            // but this causes incorrect exports resolution. We fix this by checking the target file's extension
            // using the logic from getImpliedNodeFormatForEmitWorker (program.ts:4827-4838).
            // .cjs/.cts/.d.cts → CommonJS → "require" condition
            // .mjs/.mts/.d.mts → ESM → "import" condition
            if (FileExtensionIsOneOf(pathObj.FileName, [ExtensionCjs, ExtensionCts, ExtensionDcts]))
            {
                importMode = ResolutionModeCommonJS;
            }
            else if (FileExtensionIsOneOf(pathObj.FileName, [ExtensionMjs, ExtensionMts, ExtensionDmts]))
            {
                importMode = ResolutionModeESM;
            }

            var conditions = GetConditions(options, importMode);

            var fromExports = "";
            if (packageJsonContent is not null && packageJsonContent.PathFields.Exports.Type != JSONValueType.NotPresent)
            {
                fromExports = TryGetModuleNameFromExports(
                    options,
                    host,
                    pathObj.FileName,
                    packageRootPath,
                    packageName,
                    packageJsonContent.PathFields.Exports,
                    conditions);
            }

            if (fromExports.Length > 0)
            {
                return new PkgJsonDirAttemptResult
                {
                    ModuleFileToTry = fromExports,
                    VerbatimFromExports = true,
                };
            }

            if (packageJsonContent is not null && packageJsonContent.PathFields.Exports.Type != JSONValueType.NotPresent)
            {
                return new PkgJsonDirAttemptResult
                {
                    ModuleFileToTry = pathObj.FileName,
                    BlockedByExports = true,
                };
            }
        }

        // Go 的 `var versionPaths packagejson.VersionPaths` 是零值结构体（Exists() 为 false）→ C# 用 null。
        VersionPaths? versionPaths = null;
        if (packageJsonContent is not null && packageJsonContent.PathFields.TypesVersions.Type == JSONValueType.Object)
        {
            versionPaths = packageJsonContent.GetVersionPaths(null);
        }

        if (versionPaths?.GetPaths() is not null)
        {
            var subModuleName = pathObj.FileName[(packageRootPath.Length + 1)..];
            var fromPaths = TryGetModuleNameFromPaths(
                subModuleName,
                versionPaths.GetPaths()!,
                allowedEndings,
                packageRootPath,
                host,
                options);
            if (fromPaths.Length == 0)
            {
                maybeBlockedByTypesVersions = true;
            }
            else
            {
                moduleFileToTry = CombinePaths(packageRootPath, fromPaths);
            }
        }

        // If the file is the main module, it can be imported by the package name
        var mainFileRelative = "index.js";
        if (packageJsonContent is not null)
        {
            if (packageJsonContent.PathFields.Typings.Valid)
            {
                mainFileRelative = packageJsonContent.PathFields.Typings.Value;
            }
            else if (packageJsonContent.PathFields.Types.Valid)
            {
                mainFileRelative = packageJsonContent.PathFields.Types.Value;
            }
            else if (packageJsonContent.PathFields.Main.Valid)
            {
                mainFileRelative = packageJsonContent.PathFields.Main.Value;
            }
        }

        if (mainFileRelative.Length > 0
            && !(maybeBlockedByTypesVersions && !IsZeroPattern(MatchPatternOrExact(TryParsePatterns(versionPaths?.GetPaths()!), mainFileRelative))))
        {
            // The 'main' file is also subject to mapping through typesVersions, and we couldn't come up with a path
            // explicitly through typesVersions, so if it matches a key in typesVersions now, it's not reachable.
            // (The only way this can happen is if some file in a package that's not resolvable from outside the
            // package got pulled into the program anyway, e.g. transitively through a file that *is* reachable. It
            // happens very easily in fourslash tests though, since every test file listed gets included. See
            // importNameCodeFix_typesVersions.ts for an example.)
            var mainExportFile = ToPath(mainFileRelative, packageRootPath, host.UseCaseSensitiveFileNames());
            var compareOpt = new ComparePathsOptions(host.UseCaseSensitiveFileNames(), host.GetCurrentDirectory());
            if (ComparePaths(RemoveFileExtension(mainExportFile.Value), RemoveFileExtension(moduleFileToTry), compareOpt) == 0)
            {
                // ^ An arbitrary removal of file extension for this comparison is almost certainly wrong
                return new PkgJsonDirAttemptResult { PackageRootPath = packageRootPath, ModuleFileToTry = moduleFileToTry };
            }
            else if ((packageJsonContent is null || packageJsonContent.HeaderFields.Type.Value != "module")
                && !FileExtensionIsOneOf(moduleFileToTry, ExtensionsNotSupportingExtensionlessResolution)
                && HasPrefix(moduleFileToTry, mainExportFile.Value, host.UseCaseSensitiveFileNames())
                && ComparePaths(GetDirectoryPath(moduleFileToTry), RemoveTrailingDirectorySeparator(mainExportFile.Value), compareOpt) == 0
                && RemoveFileExtension(GetBaseFileName(moduleFileToTry)) == "index")
            {
                // if mainExportFile is a directory, which contains moduleFileToTry, we just try index file
                // example mainExportFile: `pkg/lib` and moduleFileToTry: `pkg/lib/index`, we can use packageRootPath
                // but this behavior is deprecated for packages with "type": "module", so we only do this for packages without "type": "module"
                // and make sure that the extension on index.{???} is something that supports omitting the extension
                return new PkgJsonDirAttemptResult { PackageRootPath = packageRootPath, ModuleFileToTry = moduleFileToTry };
            }
        }

        return new PkgJsonDirAttemptResult { ModuleFileToTry = moduleFileToTry };
    }

    /// <summary>Go: <c>module.MatchPatternOrExact(...) != core.Pattern{}</c>：Pattern 的零值判定。</summary>
    private static bool IsZeroPattern(Pattern pattern) => pattern.Text.Length == 0 && pattern.StarIndex == 0;

    /// <summary>Go: <c>func tryGetModuleNameFromExports(options *core.CompilerOptions, host ModuleSpecifierGenerationHost, targetFilePath string, packageDirectory string, packageName string, exports packagejson.ExportsOrImports, conditions []string) string</c>。</summary>
    private static string TryGetModuleNameFromExports(
        CompilerOptions options,
        ModuleSpecifierGenerationHost host,
        string targetFilePath,
        string packageDirectory,
        string packageName,
        ExportsOrImports exports,
        string[] conditions)
    {
        if (exports.IsSubpaths())
        {
            // sub-mappings
            // 3 cases:
            // * directory mappings (legacyish, key ends with / (technically allows index/extension resolution under cjs mode))
            // * pattern mappings (contains a *)
            // * exact mappings (no *, does not end with /)
            // Go: exports.AsObject()（IsSubpaths() 蕴含 Type == Object，nil 即 panic），故 `!`。
            foreach (var (k, subk) in exports.AsObject()!.Entries())
            {
                var subPackageName = GetNormalizedAbsolutePath(CombinePaths(packageName, k), "");
                var mode = MatchingMode.Exact;
                if (k.EndsWith('/'))
                {
                    mode = MatchingMode.Directory;
                }
                else if (k.Contains('*'))
                {
                    mode = MatchingMode.Pattern;
                }

                var result = TryGetModuleNameFromExportsOrImports(
                    options,
                    host,
                    targetFilePath,
                    packageDirectory,
                    subPackageName,
                    subk!, // OrderedMap 的 V? 只是可空注解，见文件头第 5 条
                    conditions,
                    mode,
                    isImports: false,
                    preferTsExtension: false);
                if (result.Length > 0)
                {
                    return result;
                }
            }
        }

        return TryGetModuleNameFromExportsOrImports(
            options,
            host,
            targetFilePath,
            packageDirectory,
            packageName,
            exports,
            conditions,
            MatchingMode.Exact,
            /*isImports*/ false,
            /*preferTsExtension*/ false);
    }

    /// <summary>Go: <c>func tryGetModuleNameFromPackageJsonImports(moduleFileName string, sourceDirectory string, options *core.CompilerOptions, host ModuleSpecifierGenerationHost, importMode core.ResolutionMode, preferTsExtension bool) string</c>。</summary>
    private static string TryGetModuleNameFromPackageJsonImports(
        string moduleFileName,
        string sourceDirectory,
        CompilerOptions options,
        ModuleSpecifierGenerationHost host,
        ModuleKind importMode,
        bool preferTsExtension)
    {
        if (!options.GetResolvePackageJsonImports())
        {
            return "";
        }

        var ancestorDirectoryWithPackageJson = host.GetNearestAncestorDirectoryWithPackageJson(sourceDirectory);
        if (ancestorDirectoryWithPackageJson.Length == 0)
        {
            return "";
        }

        var packageJsonPath = CombinePaths(ancestorDirectoryWithPackageJson, "package.json");

        var info = host.GetPackageJsonInfo(packageJsonPath);
        if (info is null)
        {
            return "";
        }

        // Go 在这里直接 `info.GetContents().Fields`（Contents 为 nil 时 Go 会 nil 解引用崩溃）；
        // C# 按"没有 package.json 内容"返回空（更宽，见文件头第 4 条）。
        var contents = info.GetContents();
        if (contents is null)
        {
            return "";
        }

        var imports = contents.PathFields.Imports;
        switch (imports.Type)
        {
            case JSONValueType.NotPresent:
            case JSONValueType.Array:
            case JSONValueType.String:
                return ""; // not present or invalid for imports
            case JSONValueType.Object:
                var conditions = GetConditions(options, importMode);
                var top = imports.AsObject()!; // case 已判 Type == Object；Go 同点解引用（nil 即 panic）。
                foreach (var (k, value) in top.Entries())
                {
                    if (k == "#" || k == "#/" || !HasPrefix(k, "#", caseSensitive: true))
                    {
                        continue; // invalid imports entry
                    }

                    if (HasPrefix(k, "#/", caseSensitive: true)
                        && options.GetModuleResolutionKind() != ModuleResolutionKind.NodeNext
                        && options.GetModuleResolutionKind() != ModuleResolutionKind.Bundler)
                    {
                        continue; // "#/" imports keys are only valid in nodenext/bundler
                    }

                    var mode = MatchingMode.Exact;
                    if (k.EndsWith('/'))
                    {
                        mode = MatchingMode.Directory;
                    }
                    else if (k.Contains('*'))
                    {
                        mode = MatchingMode.Pattern;
                    }

                    var result = TryGetModuleNameFromExportsOrImports(
                        options,
                        host,
                        moduleFileName,
                        ancestorDirectoryWithPackageJson,
                        k,
                        value!, // 见文件头第 5 条
                        conditions,
                        mode,
                        isImports: true,
                        preferTsExtension);
                    if (result.Length > 0)
                    {
                        return result;
                    }
                }

                break;
        }

        return "";
    }
}
