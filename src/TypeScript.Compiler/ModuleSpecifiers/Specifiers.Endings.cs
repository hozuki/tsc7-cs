// ported from: internal/modulespecifiers/specifiers.go:641-828（processEnding + rootDirs + node_modules 主流程）
//
// 与 Go 的偏离（在 Specifiers.cs 文件头的 7 条之外，本分片特有的）：
// 1) `processEnding` 的 host 参数在 Go 里被显式判 nil（`if host != nil && …`）→ C# 参数类型是
//    `ModuleSpecifierGenerationHost?`，调用点传非 null 也可（隐式转换）。
// 2) `strings.Count(s, "/")` → 本分片的私有 helper `CountSlashes`（逐代码单元计数，§3.8）。
// 3) `strings.TrimSuffix(s, "/index")` → Util.cs 的 `TrimSuffix`（Ordinal 语义）。
// 4) `slices.Index(allowedEndings, X)` → `Array.IndexOf(allowedEndings, X)`（Go 的 slices.Index 就是线性查找）。
// 5) `moduleSpecifier[0:parts.TopLevelNodeModulesIndex]` → C# 的 range `[..index]`（越界同样抛异常）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>func processEnding(fileName string, allowedEndings []ModuleSpecifierEnding, options *core.CompilerOptions, host ModuleSpecifierGenerationHost) string</c>。</summary>
    private static string ProcessEnding(
        string fileName,
        ModuleSpecifierEnding[] allowedEndings,
        CompilerOptions options,
        ModuleSpecifierGenerationHost? host)
    {
        if (FileExtensionIsOneOf(fileName, [ExtensionJson, ExtensionMjs, ExtensionCjs]))
        {
            return fileName;
        }

        var noExtension = RemoveFileExtension(fileName);
        if (fileName == noExtension)
        {
            return fileName;
        }

        var jsPriority = Array.IndexOf(allowedEndings, ModuleSpecifierEnding.JsExtension);
        var tsPriority = Array.IndexOf(allowedEndings, ModuleSpecifierEnding.TsExtension);
        if (FileExtensionIsOneOf(fileName, [ExtensionMts, ExtensionCts]) && tsPriority != -1 && tsPriority < jsPriority)
        {
            return fileName;
        }

        if (FileExtensionIsOneOf(fileName, [ExtensionDmts, ExtensionDcts]))
        {
            var inputExt = GetDeclarationFileExtension(fileName);
            var ext = GetJSExtensionForDeclarationFileExtension(inputExt);
            return RemoveExtension(fileName, inputExt) + ext;
        }

        if (FileExtensionIsOneOf(fileName, [ExtensionMts, ExtensionCts]))
        {
            return noExtension + GetJSExtensionForFile(fileName, options);
        }

        if (!FileExtensionIsOneOf(fileName, [ExtensionDts])
            && FileExtensionIsOneOf(fileName, [ExtensionTs])
            && fileName.Contains(".d.", StringComparison.Ordinal))
        {
            // `foo.d.json.ts` and the like - remap back to `foo.json`
            var result = TryGetRealFileNameForNonJSDeclarationFileName(fileName);
            if (result != "")
            {
                return result;
            }
        }

        switch (allowedEndings[0])
        {
            case ModuleSpecifierEnding.Minimal:
                var withoutIndex = TrimSuffix(noExtension, "/index");
                if (host is not null && withoutIndex != noExtension && TryGetAnyFileFromPath(host, withoutIndex))
                {
                    // Can't remove index if there's a file by the same name as the directory.
                    // Probably more callers should pass `host` so we can determine this?
                    return noExtension;
                }

                return withoutIndex;
            case ModuleSpecifierEnding.Index:
                return noExtension;
            case ModuleSpecifierEnding.JsExtension:
                return noExtension + GetJSExtensionForFile(fileName, options);
            case ModuleSpecifierEnding.TsExtension:
                // For now, we don't know if this import is going to be type-only, which means we don't
                // know if a .d.ts extension is valid, so use no extension or a .js extension
                if (IsDeclarationFileName(fileName))
                {
                    var extensionlessPriority = -1;
                    for (var i = 0; i < allowedEndings.Length; i++)
                    {
                        if (allowedEndings[i] == ModuleSpecifierEnding.Minimal || allowedEndings[i] == ModuleSpecifierEnding.Index)
                        {
                            extensionlessPriority = i;
                            break;
                        }
                    }

                    if (extensionlessPriority != -1 && extensionlessPriority < jsPriority)
                    {
                        return noExtension;
                    }

                    return noExtension + GetJSExtensionForFile(fileName, options);
                }

                return fileName;
            default:
                Debug.AssertNever(allowedEndings[0]);
                return "";
        }
    }

    /// <summary>Go: <c>func tryGetModuleNameFromRootDirs(rootDirs []string, moduleFileName string, sourceDirectory string, allowedEndings []ModuleSpecifierEnding, compilerOptions *core.CompilerOptions, host ModuleSpecifierGenerationHost) string</c>。</summary>
    private static string TryGetModuleNameFromRootDirs(
        string[] rootDirs,
        string moduleFileName,
        string sourceDirectory,
        ModuleSpecifierEnding[] allowedEndings,
        CompilerOptions compilerOptions,
        ModuleSpecifierGenerationHost host)
    {
        var normalizedTargetPaths = GetPathsRelativeToRootDirs(moduleFileName, rootDirs, host.UseCaseSensitiveFileNames());
        if (normalizedTargetPaths.Length == 0)
        {
            return "";
        }

        var normalizedSourcePaths = GetPathsRelativeToRootDirs(sourceDirectory, rootDirs, host.UseCaseSensitiveFileNames());
        var shortest = "";
        var shortestSepCount = 0;
        foreach (var sourcePath in normalizedSourcePaths)
        {
            foreach (var targetPath in normalizedTargetPaths)
            {
                var candidate = EnsurePathIsNonModuleName(GetRelativePathFromDirectory(
                    sourcePath,
                    targetPath,
                    new ComparePathsOptions(host.UseCaseSensitiveFileNames(), host.GetCurrentDirectory())));
                var candidateSepCount = CountSlashes(candidate);
                if (shortest.Length == 0 || candidateSepCount < shortestSepCount)
                {
                    shortest = candidate;
                    shortestSepCount = candidateSepCount;
                }
            }
        }

        if (shortest.Length == 0)
        {
            return "";
        }

        return ProcessEnding(shortest, allowedEndings, compilerOptions, host);
    }

    /// <summary>Go: <c>func tryGetModuleNameAsNodeModule(pathObj ModulePath, info Info, importingSourceFile SourceFileForSpecifierGeneration, host ModuleSpecifierGenerationHost, options *core.CompilerOptions, userPreferences UserPreferences, packageNameOnly bool, overrideMode core.ResolutionMode) string</c>。</summary>
    private static string TryGetModuleNameAsNodeModule(
        ModulePath pathObj,
        Info info,
        SourceFileForSpecifierGeneration importingSourceFile,
        ModuleSpecifierGenerationHost host,
        CompilerOptions options,
        UserPreferences userPreferences,
        bool packageNameOnly,
        ModuleKind overrideMode)
    {
        var parts = GetNodeModulePathParts(pathObj.FileName);
        if (parts is null)
        {
            return "";
        }

        // Simplify the full file path to something that can be resolved by Node.
        var preferences = GetModuleSpecifierPreferences(userPreferences, host, options, importingSourceFile, "");
        var allowedEndings = preferences.GetAllowedEndingsInPreferredOrder(ResolutionModeNone);

        var caseSensitive = host.UseCaseSensitiveFileNames();
        var moduleSpecifier = pathObj.FileName;
        var isPackageRootPath = false;
        if (!packageNameOnly)
        {
            var packageRootIndex = parts.PackageRootIndex;
            var moduleFileName = "";
            while (true)
            {
                // If the module could be imported by a directory name, use that directory's name
                var pkgJsonResults = TryDirectoryWithPackageJson(
                    parts,
                    pathObj,
                    importingSourceFile,
                    host,
                    overrideMode,
                    options,
                    allowedEndings);
                var moduleFileToTry = pkgJsonResults.ModuleFileToTry;
                var packageRootPath = pkgJsonResults.PackageRootPath;
                var blockedByExports = pkgJsonResults.BlockedByExports;
                var verbatimFromExports = pkgJsonResults.VerbatimFromExports;
                if (blockedByExports)
                {
                    return ""; // File is under this package.json, but is not publicly exported - there's no way to name it via `node_modules` resolution
                }

                if (verbatimFromExports)
                {
                    return moduleFileToTry;
                }

                if (packageRootPath.Length > 0)
                {
                    moduleSpecifier = packageRootPath;
                    isPackageRootPath = true;
                    break;
                }

                if (moduleFileName.Length == 0)
                {
                    moduleFileName = moduleFileToTry;
                }

                // try with next level of directory
                packageRootIndex = IndexAfter(pathObj.FileName, "/", packageRootIndex + 1);
                if (packageRootIndex == -1)
                {
                    moduleSpecifier = ProcessEnding(moduleFileName, allowedEndings, options, host);
                    break;
                }
            }
        }

        if (pathObj.IsRedirect && !isPackageRootPath)
        {
            return "";
        }

        var globalTypingsCacheLocation = host.GetGlobalTypingsCacheLocation();

        // Get a path that's relative to node_modules or the importing file's path
        // if node_modules folder is in this folder or any of its parents folders, no need to keep it.
        var pathToTopLevelNodeModules = moduleSpecifier[..parts.TopLevelNodeModulesIndex];

        if (!HasPrefix(info.SourceDirectory, pathToTopLevelNodeModules, caseSensitive)
            || (globalTypingsCacheLocation.Length > 0 && HasPrefix(globalTypingsCacheLocation, pathToTopLevelNodeModules, caseSensitive)))
        {
            return "";
        }

        // If the module was found in @types, get the actual Node package name
        var nodeModulesDirectoryName = moduleSpecifier[(parts.TopLevelPackageNameIndex + 1)..];
        return GetPackageNameFromTypesPackageName(nodeModulesDirectoryName);
    }

    /// <summary>Go: <c>strings.Count(s, "/")</c>（本文件内用；§3.8 的逐代码单元扫描）。</summary>
    private static int CountSlashes(string s)
    {
        var count = 0;
        foreach (var c in s.AsSpan())
        {
            if (c == '/')
            {
                count++;
            }
        }

        return count;
    }
}
