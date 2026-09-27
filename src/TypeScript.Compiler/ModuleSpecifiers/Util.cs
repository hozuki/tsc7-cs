// ported from: internal/modulespecifiers/util.go（490 行，整文件；第 350-357 行的 `allKeysStartWithDot`
// 是 Go 里从未被引用的死代码，不移植——见交付报告的"跳过的 Go 符号"清单）
//
// 与 Go 的偏离（逐条）：
// 1) `regexp`（RE2，线性时间）→ `System.Text.RegularExpressions.Regex`（回溯引擎）。等价之处：两边都是
//    非锚定的部分匹配、都支持 `(?i:…)` 内联旗标。差异：RE2 不支持反向引用/环视（.NET 支持），病态模式在
//    .NET 下可能退化成指数级回溯。这里**不加** `RegexMatchTimeout`（保持 Go "编译+匹配"的契约不变），
//    见交付报告的 unverified。
// 2) Go 的正则缓存用 `sync.RWMutex` + `map[key]*regexp.Regexp`（失败时缓存 nil、超 1000 条整体清空）→
//    C# 用 `ConcurrentDictionary<key, RegexBox>`：ConcurrentDictionary 不接受 null 值，故用
//    `RegexBox`（只读结构体包一个 `Regex?`）承载"编译失败"这一档。清空用 `Clear()`。
// 3) Go 的 `pattern = strings.Clone(pattern)` 是"避免大字符串被小切片钉住"的内存优化；C# 的 string 是独立
//    对象，`pattern[1:lastSlash]` 已经产生新字符串，不需要 Clone。
// 4) Go 的 `regexPatternCacheKey{pattern, caseInsensitive}` → `record struct`（值相等即键相等）。
// 5) `paths[initial:]` 这类零拷贝切片 → 需要在函数返回处产生 string 的地方按 §3.8 用 range/IndexOf 直接切
//    （`GetJSExtensionForDeclarationFileExtension` 的 `ext[len(".d"):len(ext)-len(ExtensionTs)]` →
//    `ext[2..(ext.Length - ExtensionTs.Length)]`）。
// 6) Go 的 `panic(fmt.Sprintf(…))` → `throw new DebugFailureException(…))`（§3.2）；两处 panic 的文本照抄。
// 7) `GetNodeModulePathParts` 的状态机里 Go 直接写 `fullPath[partStart+1]`（越界即 panic）；C# 里越界是
//    IndexOutOfRangeException，这里改成 `partStart + 1 < fullPath.Length && …`——只在 Go 会 panic 的输入
//    （路径末尾就是 "/node_modules/"）上分叉，正常输入逐字等价（见函数处的注释）。
// 8) `NodeModulePathParts` 在 Go 里是 `*NodeModulePathParts`（可 nil）→ C# 是 class，`GetNodeModulePathParts`
//    返回 `NodeModulePathParts?`。
// 9) `allKeysStartWithDot` 不移植（死代码）；`nodeModulesPathParseState` → private enum : byte。
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>type regexPatternCacheKey struct { pattern string; caseInsensitive bool }</c>。</summary>
    private readonly record struct RegexPatternCacheKey(string Pattern, bool CaseInsensitive);

    /// <summary>ConcurrentDictionary 不许存 null，用它承载"编译失败"这一档（Go 缓存的是 nil）。</summary>
    private readonly record struct RegexBox(Regex? Regex);

    // Go: var (regexPatternCacheMu sync.RWMutex; regexPatternCache = map[regexPatternCacheKey]*regexp.Regexp)
    private static readonly ConcurrentDictionary<RegexPatternCacheKey, RegexBox> _regexPatternCache = new();

    /// <summary>Go: <c>func comparePathsByRedirect(a ModulePath, b ModulePath, useCaseSensitiveFileNames bool) int</c>。</summary>
    private static int ComparePathsByRedirect(ModulePath a, ModulePath b, bool useCaseSensitiveFileNames)
    {
        // Redirects sort first, matching Strada's compareBooleans(b.isRedirect, a.isRedirect).
        var c = CompareBooleans(b.IsRedirect, a.IsRedirect);
        if (c != 0)
        {
            return c;
        }

        c = CompareNumberOfDirectorySeparators(a.FileName, b.FileName);
        if (c != 0)
        {
            return c;
        }

        // Strada relies on Map insertion order to break remaining ties deterministically;
        // Go maps are unordered, so compare paths to keep the ordering stable.
        return ComparePaths(a.FileName, b.FileName, new ComparePathsOptions(useCaseSensitiveFileNames));
    }

    /// <summary>Go: <c>func PathIsBareSpecifier(path string) bool</c>。</summary>
    public static bool PathIsBareSpecifier(string path) => !PathIsAbsolute(path) && !PathIsRelative(path);

    /// <summary>Go: <c>func IsExcludedByRegex(moduleSpecifier string, excludes []string) bool</c>（nil slice 对应 null）。</summary>
    public static bool IsExcludedByRegex(string moduleSpecifier, string[]? excludes)
    {
        if (excludes is null)
        {
            return false;
        }

        foreach (var pattern in excludes)
        {
            var re = StringToRegex(pattern);
            if (re is null)
            {
                continue;
            }

            if (re.IsMatch(moduleSpecifier))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func stringToRegex(pattern string) *regexp.Regexp</c>（编译失败返回 null）。</summary>
    private static Regex? StringToRegex(string pattern)
    {
        var caseInsensitive = false;

        if (pattern.Length > 2 && pattern[0] == '/')
        {
            var lastSlash = pattern.LastIndexOf('/');
            if (lastSlash > 0)
            {
                var hasUnescapedMiddleSlash = false;
                for (var i = 1; i < lastSlash; i++)
                {
                    // Go 原文是 `i == 0 || pattern[i-1] != '\\'`；i 从 1 起，所以 `i == 0` 恒假（Go 里也是）。
                    if (pattern[i] == '/' && (i == 0 || pattern[i - 1] != '\\'))
                    {
                        hasUnescapedMiddleSlash = true;
                        break;
                    }
                }

                if (!hasUnescapedMiddleSlash)
                {
                    var flags = pattern[(lastSlash + 1)..];
                    pattern = pattern[1..lastSlash];

                    foreach (var flag in flags)
                    {
                        switch (flag)
                        {
                            case 'i':
                                caseInsensitive = true;
                                break;
                        }
                    }
                }
            }
        }

        var key = new RegexPatternCacheKey(pattern, caseInsensitive);

        if (_regexPatternCache.TryGetValue(key, out var cached))
        {
            return cached.Regex;
        }

        if (_regexPatternCache.Count > 1000)
        {
            _regexPatternCache.Clear();
        }

        var compilePattern = caseInsensitive ? "(?i:" + pattern + ")" : pattern;
        Regex? compiled;
        try
        {
            compiled = new Regex(compilePattern, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            _regexPatternCache[key] = new RegexBox(null);
            return null;
        }

        _regexPatternCache[key] = new RegexBox(compiled);
        return compiled;
    }

    /// <summary>
    /// Go: <c>func ensurePathIsNonModuleName(path string) string</c>。
    /// Ensures a path is either absolute (prefixed with `/` or `c:`) or dot-relative (prefixed
    /// with `./` or `../`) so as not to be confused with an unprefixed module name.
    /// </summary>
    private static string EnsurePathIsNonModuleName(string path)
    {
        if (PathIsBareSpecifier(path))
        {
            return "./" + path;
        }

        return path;
    }

    /// <summary>Go: <c>func GetJSExtensionForDeclarationFileExtension(ext string) string</c>。</summary>
    public static string GetJSExtensionForDeclarationFileExtension(string ext)
    {
        switch (ext)
        {
            case ExtensionDts:
                return ExtensionJs;
            case ExtensionDmts:
                return ExtensionMjs;
            case ExtensionDcts:
                return ExtensionCjs;
            default:
                // .d.json.ts and the like
                return ext[2..(ext.Length - ExtensionTs.Length)];
        }
    }

    /// <summary>Go: <c>func TryGetRealFileNameForNonJSDeclarationFileName(fileName string) string</c>。</summary>
    /// <remarks>remaps files like `foo.d.json.ts` or `foo.module.d.css.ts` back to their real non-JS names.</remarks>
    public static string TryGetRealFileNameForNonJSDeclarationFileName(string fileName)
    {
        var baseName = GetBaseFileName(fileName);
        // Ends with .ts, contains ".d.", and is NOT a standard .d.ts file
        if (!fileName.EndsWith(ExtensionTs, StringComparison.Ordinal)
            || !baseName.Contains(".d.", StringComparison.Ordinal)
            || baseName.EndsWith(ExtensionDts, StringComparison.Ordinal))
        {
            return "";
        }

        var noExtension = RemoveExtension(fileName, ExtensionTs);
        var lastDotIndex = noExtension.LastIndexOf('.');
        var ext = noExtension[lastDotIndex..];
        // Go: before, _, _ := strings.Cut(noExtension, ".d.")（截到**第一个** ".d."）
        var cutIndex = noExtension.IndexOf(".d.", StringComparison.Ordinal);
        var before = cutIndex >= 0 ? noExtension[..cutIndex] : noExtension;
        return before + ext;
    }

    /// <summary>Go: <c>func getJSExtensionForFile(fileName string, options *core.CompilerOptions) string</c>。</summary>
    private static string GetJSExtensionForFile(string fileName, CompilerOptions options)
    {
        var result = TryGetJSExtensionForFile(fileName, options);
        if (result.Length == 0)
        {
            throw new DebugFailureException($"Extension {ExtensionFromPath(fileName)} is unsupported:: FileName:: {fileName}");
        }

        return result;
    }

    /// <summary>
    /// Go: <c>func extensionFromPath(path string) string</c>。
    /// Gets the extension from a path. Path must have a valid extension.
    /// </summary>
    private static string ExtensionFromPath(string path)
    {
        var ext = TryGetExtensionFromPath(path);
        if (ext.Length == 0)
        {
            throw new DebugFailureException($"File {path} has unknown extension.");
        }

        return ext;
    }

    /// <summary>Go: <c>func tryGetAnyFileFromPath(host ModuleSpecifierGenerationHost, path string) bool</c>。</summary>
    private static bool TryGetAnyFileFromPath(ModuleSpecifierGenerationHost host, string path)
    {
        // !!! TODO: shouldn't this use readdir instead of fileexists for perf?
        // We check all js, `node` and `json` extensions in addition to TS, since node module resolution would also choose those over the directory
        var extGroups = GetSupportedExtensions(
            new CompilerOptions { AllowJs = Tristate.TSTrue },
            [".node", ".json"]);
        foreach (var exts in extGroups)
        {
            foreach (var e in exts)
            {
                var fullPath = path + e;
                if (host.FileExists(GetNormalizedAbsolutePath(fullPath, host.GetCurrentDirectory())))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Go: <c>func getPathsRelativeToRootDirs(path string, rootDirs []string, useCaseSensitiveFileNames bool) []string</c>。</summary>
    private static string[] GetPathsRelativeToRootDirs(string path, string[] rootDirs, bool useCaseSensitiveFileNames)
    {
        // Go 的 `var results []string` 是 nil slice；§3.2 在语义等同时用 []。
        var results = new List<string>();
        foreach (var rootDir in rootDirs)
        {
            var relativePath = GetRelativePathIfInSameVolume(path, rootDir, useCaseSensitiveFileNames);
            if (!IsPathRelativeToParent(relativePath))
            {
                results.Add(relativePath);
            }
        }

        return results.ToArray();
    }

    /// <summary>Go: <c>func isPathRelativeToParent(path string) bool</c>。</summary>
    private static bool IsPathRelativeToParent(string path) => path.StartsWith("..", StringComparison.Ordinal);

    /// <summary>Go: <c>func getRelativePathIfInSameVolume(path string, directoryPath string, useCaseSensitiveFileNames bool) string</c>。</summary>
    private static string GetRelativePathIfInSameVolume(string path, string directoryPath, bool useCaseSensitiveFileNames)
    {
        var relativePath = GetRelativePathToDirectoryOrUrl(
            directoryPath,
            path,
            false,
            new ComparePathsOptions(useCaseSensitiveFileNames, directoryPath));
        if (IsRootedDiskPath(relativePath))
        {
            return "";
        }

        return relativePath;
    }

    /// <summary>Go: <c>func packageJsonPathsAreEqual(a string, b string, options tspath.ComparePathsOptions) bool</c>。</summary>
    private static bool PackageJsonPathsAreEqual(string a, string b, ComparePathsOptions options)
    {
        if (a == b)
        {
            return true;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        return ComparePaths(a, b, options) == 0;
    }

    /// <summary>Go: <c>func prefersTsExtension(allowedEndings []ModuleSpecifierEnding) bool</c>。</summary>
    private static bool PrefersTsExtension(ModuleSpecifierEnding[] allowedEndings)
    {
        var jsPriority = Array.IndexOf(allowedEndings, ModuleSpecifierEnding.JsExtension);
        var tsPriority = Array.IndexOf(allowedEndings, ModuleSpecifierEnding.TsExtension);
        if (tsPriority > -1)
        {
            return tsPriority < jsPriority;
        }

        return false;
    }

    /// <summary>Go: <c>func replaceFirstStar(s string, replacement string) string</c>。</summary>
    private static string ReplaceFirstStar(string s, string replacement)
    {
        // Go: strings.Replace(s, "*", replacement, 1)
        var index = s.IndexOf('*');
        if (index < 0)
        {
            return s;
        }

        return string.Concat(s.AsSpan(0, index), replacement, s.AsSpan(index + 1));
    }

    /// <summary>Go: <c>func TrimSuffix</c>（只用在本文件的 ProcessEntrypointEnding）。</summary>
    private static string TrimSuffix(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) ? s[..^suffix.Length] : s;

    /// <summary>Go: <c>type NodeModulePathParts struct { TopLevelNodeModulesIndex, TopLevelPackageNameIndex, PackageRootIndex, FileNameIndex int }</c>。</summary>
    public sealed class NodeModulePathParts
    {
        public int TopLevelNodeModulesIndex;
        public int TopLevelPackageNameIndex;
        public int PackageRootIndex;
        public int FileNameIndex;
    }

    /// <summary>Go: <c>type nodeModulesPathParseState uint8</c>。</summary>
    private enum NodeModulesPathParseState : byte
    {
        BeforeNodeModules = 0,
        NodeModules = 1,
        Scope = 2,
        PackageContent = 3,
    }

    /// <summary>Go: <c>func GetNodeModulePathParts(fullPath string) *NodeModulePathParts</c>。</summary>
    public static NodeModulePathParts? GetNodeModulePathParts(string fullPath)
    {
        // If fullPath can't be valid module file within node_modules, returns undefined.
        // Example of expected pattern: /base/path/node_modules/[@scope/otherpackage/@otherscope/node_modules/]package/[subdirectory/]file.js
        // Returns indices:                       ^            ^                                                      ^             ^
        var topLevelNodeModulesIndex = 0;
        var topLevelPackageNameIndex = 0;
        var packageRootIndex = 0;
        var fileNameIndex = 0;

        var partStart = 0;
        var partEnd = 0;
        var state = NodeModulesPathParseState.BeforeNodeModules;

        while (partEnd >= 0)
        {
            partStart = partEnd;
            partEnd = IndexAfter(fullPath, "/", partStart + 1);
            switch (state)
            {
                case NodeModulesPathParseState.BeforeNodeModules:
                    if (fullPath.AsSpan(partStart).StartsWith("/node_modules/", StringComparison.Ordinal))
                    {
                        topLevelNodeModulesIndex = partStart;
                        topLevelPackageNameIndex = partEnd;
                        state = NodeModulesPathParseState.NodeModules;
                    }

                    break;
                case NodeModulesPathParseState.NodeModules:
                case NodeModulesPathParseState.Scope:
                    // Go 直接写 fullPath[partStart+1]（越界即 panic）；这里加了长度判断，只在路径末尾就是
                    // "/node_modules/" 这种 Go 会崩的输入上分叉（见文件头第 7 条）。
                    if (state == NodeModulesPathParseState.NodeModules && partStart + 1 < fullPath.Length && fullPath[partStart + 1] == '@')
                    {
                        state = NodeModulesPathParseState.Scope;
                    }
                    else
                    {
                        packageRootIndex = partEnd;
                        state = NodeModulesPathParseState.PackageContent;
                    }

                    break;
                case NodeModulesPathParseState.PackageContent:
                    if (fullPath.AsSpan(partStart).StartsWith("/node_modules/", StringComparison.Ordinal))
                    {
                        state = NodeModulesPathParseState.NodeModules;
                    }
                    else
                    {
                        state = NodeModulesPathParseState.PackageContent;
                    }

                    break;
            }
        }

        fileNameIndex = partStart;

        if (state > NodeModulesPathParseState.NodeModules)
        {
            return new NodeModulePathParts
            {
                TopLevelNodeModulesIndex = topLevelNodeModulesIndex,
                TopLevelPackageNameIndex = topLevelPackageNameIndex,
                PackageRootIndex = packageRootIndex,
                FileNameIndex = fileNameIndex,
            };
        }

        return null;
    }

    /// <summary>Go: <c>func GetNodeModulesPackageName(compilerOptions *core.CompilerOptions, importingSourceFile *ast.SourceFile, nodeModulesFileName string, host ModuleSpecifierGenerationHost, preferences UserPreferences, options ModuleSpecifierOptions) string</c>。</summary>
    public static string GetNodeModulesPackageName(
        CompilerOptions compilerOptions,
        SourceFile importingSourceFile, // !!! | FutureSourceFile
        string nodeModulesFileName,
        ModuleSpecifierGenerationHost host,
        UserPreferences preferences,
        ModuleSpecifierOptions options)
    {
        var info = GetInfo(importingSourceFile.FileName(), host);
        var modulePaths = GetAllModulePaths(info, nodeModulesFileName, host, compilerOptions, preferences, options);
        foreach (var modulePath in modulePaths)
        {
            var result = TryGetModuleNameAsNodeModule(modulePath, info, importingSourceFile, host, compilerOptions, preferences, packageNameOnly: true, options.OverrideImportMode);
            if (result.Length > 0)
            {
                return result;
            }
        }

        return "";
    }

    /// <summary>Go: <c>func GetPackageNameFromDirectory(fileOrDirectoryPath string) string</c>。</summary>
    public static string GetPackageNameFromDirectory(string fileOrDirectoryPath)
    {
        // Go: strings.CutLast(fileOrDirectoryPath, "/node_modules/")
        var index = fileOrDirectoryPath.LastIndexOf("/node_modules/", StringComparison.Ordinal);
        if (index < 0)
        {
            return "";
        }

        var basename = fileOrDirectoryPath[(index + "/node_modules/".Length)..];

        // Go 对空 basename 会 panic（basename[0]）；C# 这里把同样的输入当成"没有包名"返回，
        // 是对 Go 崩溃输入的防御性处理（见文件头第 7 条的同类说明）。
        if (basename.Length == 0 || basename[0] == '.')
        {
            return "";
        }

        var nextSlash = basename.IndexOf('/');
        if (nextSlash == -1)
        {
            return basename;
        }

        if (basename[0] != '@' || nextSlash == basename.Length - 1)
        {
            return basename[..nextSlash];
        }

        // Go 是 `strings.Index(basename[nextSlash+1:], "/")`（**相对**下标）+ `basename[:nextSlash+1+secondSlash]`；
        // C# 的 IndexOf(char, startIndex) 返回**绝对**下标，所以这里直接用它切片（等价）。
        var secondSlash = basename.IndexOf('/', nextSlash + 1);
        if (secondSlash == -1)
        {
            return basename;
        }

        return basename[..secondSlash];
    }

    /// <summary>
    /// Go: <c>func ProcessEntrypointEnding(entrypoint *module.ResolvedEntrypoint, prefs UserPreferences, host ModuleSpecifierGenerationHost, options *core.CompilerOptions, importingSourceFile SourceFileForSpecifierGeneration, allowedEndings []ModuleSpecifierEnding) string</c>。
    /// Processes a pre-computed module specifier from a package.json exports entrypoint according to the
    /// entrypoint's Ending type and the user's preferred endings.
    /// </summary>
    public static string ProcessEntrypointEnding(
        ResolvedEntrypoint entrypoint,
        UserPreferences prefs,
        ModuleSpecifierGenerationHost host,
        CompilerOptions options,
        SourceFileForSpecifierGeneration importingSourceFile,
        ModuleSpecifierEnding[] allowedEndings)
    {
        var specifier = entrypoint.ModuleSpecifier;
        if (entrypoint.Ending == Ending.Fixed)
        {
            return specifier;
        }

        if (allowedEndings.Length == 0)
        {
            allowedEndings = GetAllowedEndingsInPreferredOrder(
                prefs,
                host,
                options,
                importingSourceFile,
                "",
                host.GetDefaultResolutionModeForFile(importingSourceFile));
        }

        var preferredEnding = allowedEndings[0];

        // Handle declaration file extensions
        var dtsExtension = GetDeclarationFileExtension(specifier);
        if (dtsExtension.Length != 0)
        {
            switch (preferredEnding)
            {
                case ModuleSpecifierEnding.TsExtension:
                case ModuleSpecifierEnding.JsExtension:
                    // Map .d.ts -> .js, .d.mts -> .mjs, .d.cts -> .cjs
                    var jsExtension = GetJSExtensionForDeclarationFileExtension(dtsExtension);
                    return ChangeAnyExtension(specifier, jsExtension, [dtsExtension], ignoreCase: false);
                case ModuleSpecifierEnding.Minimal:
                case ModuleSpecifierEnding.Index:
                    if (entrypoint.Ending == Ending.Changeable)
                    {
                        // .d.mts/.d.cts must keep an extension; rewrite to .mjs/.cjs instead of dropping
                        if (dtsExtension == ExtensionDts)
                        {
                            specifier = RemoveExtension(specifier, dtsExtension);
                            if (preferredEnding == ModuleSpecifierEnding.Minimal)
                            {
                                specifier = TrimSuffix(specifier, "/index");
                            }

                            return specifier;
                        }

                        var changed = GetJSExtensionForDeclarationFileExtension(dtsExtension);
                        return ChangeAnyExtension(specifier, changed, [dtsExtension], ignoreCase: false);
                    }

                    // EndingExtensionChangeable - can only change extension, not remove it
                    var extensionChangeable = GetJSExtensionForDeclarationFileExtension(dtsExtension);
                    return ChangeAnyExtension(specifier, extensionChangeable, [dtsExtension], ignoreCase: false);
            }

            return specifier;
        }

        // Handle .ts/.tsx/.mts/.cts extensions
        if (FileExtensionIsOneOf(specifier, [ExtensionTs, ExtensionTsx, ExtensionMts, ExtensionCts]))
        {
            switch (preferredEnding)
            {
                case ModuleSpecifierEnding.TsExtension:
                    return specifier;
                case ModuleSpecifierEnding.JsExtension:
                    var jsExtension = TryGetJSExtensionForFile(specifier, options);
                    if (jsExtension.Length != 0)
                    {
                        return RemoveFileExtension(specifier) + jsExtension;
                    }

                    return specifier;
                case ModuleSpecifierEnding.Minimal:
                case ModuleSpecifierEnding.Index:
                    if (entrypoint.Ending == Ending.Changeable)
                    {
                        specifier = RemoveFileExtension(specifier);
                        if (preferredEnding == ModuleSpecifierEnding.Minimal)
                        {
                            specifier = TrimSuffix(specifier, "/index");
                        }

                        return specifier;
                    }

                    // EndingExtensionChangeable - can only change extension, not remove it
                    var jsExtension2 = TryGetJSExtensionForFile(specifier, options);
                    if (jsExtension2.Length != 0)
                    {
                        return RemoveFileExtension(specifier) + jsExtension2;
                    }

                    return specifier;
            }

            return specifier;
        }

        // Handle .js/.jsx/.mjs/.cjs extensions
        if (FileExtensionIsOneOf(specifier, [ExtensionJs, ExtensionJsx, ExtensionMjs, ExtensionCjs]))
        {
            switch (preferredEnding)
            {
                case ModuleSpecifierEnding.TsExtension:
                case ModuleSpecifierEnding.JsExtension:
                    return specifier;
                case ModuleSpecifierEnding.Minimal:
                case ModuleSpecifierEnding.Index:
                    if (entrypoint.Ending == Ending.Changeable)
                    {
                        specifier = RemoveFileExtension(specifier);
                        if (preferredEnding == ModuleSpecifierEnding.Minimal)
                        {
                            specifier = TrimSuffix(specifier, "/index");
                        }

                        return specifier;
                    }

                    // EndingExtensionChangeable - keep the extension
                    return specifier;
            }

            return specifier;
        }

        // For other extensions (like .json), return as-is
        return specifier;
    }
}
