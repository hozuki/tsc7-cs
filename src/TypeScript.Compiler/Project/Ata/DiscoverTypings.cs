// ported from: internal/project/ata/discovertypings.go:1-334
//
// 与 Go 的偏离（逐条）：
// 1) 包级函数 `DiscoverTypings` 落到它的输入类型 **`TypingsInfo`** 上（`partial class TypingsInfo`，与 TypesMap.cs
//    同一个类型）：C# 没有包作用域，而本文件里没有别的类型可挂（Go 的 `CachedTyping` 在 ata.go）。参数表逐条保留，
//    所以是 `TypingsInfo.DiscoverTypings(fs, logger, typingsInfo, …)`。Go 里 `@internal` 的
//    `removeMinAndVersionNumbers` 全仓**没有**跨包调用点（只有本文件用）→ 这里 internal（与本仓"未导出但同程序集
//    可见"的写法一致，便于测试）；`isTypingUpToDate` 被 ata.go 的 `filterTypings` 调用 → `internal`。
// 2) 三个返回值 `(cachedTypingPaths, newTypingNames, filesToWatch)` → 具名元组。Go 的 **nil 切片与空切片在 C# 里
//    无法区分**：一律返回非 null 数组（空 = `Length == 0`），调用点/测试用长度判空（对应 Go 测试里的 `== nil`）。
// 3) Go 的 `map[string]bool`（possibleSearchDirs）与 `map[string]string`（inferredTypings）遍历**顺序随机**；
//    C# 的 `Dictionary` 是插入序 → 确定性更好（Go 的 `filesToWatch`/`inferredTypings` 顺序没有被任何断言依赖；
//    Go 测试对 `cachedTypingPaths`/`newTypingNames` 也是按集合比较的）。
// 4) `manifest.Dependencies.Value` 等（Go `Expected[T]` 的零值是 nil map）→ C# 的 `Expected<T>.Value` 可为 null
//    （见 packagejson/expected.go 文件头第 6 条），故消费处用 `is { } x` 判空（等价 `maps.Keys(nil)` = 空）。
// 5) Go 用 `json.Unmarshal` 把 manifest 解成 `packagejson.DependencyFields`，C# 侧用 `TryParse`（`Fields` 继承
//    `DependencyFields`）：对"依赖键集合"的语义逐条相同（未知字段 Go 忽略 / C# 的 `Expected<T>` 容忍类型不符），
//    malformed JSON 的错误分支与 Go 的 `err != nil` 相同。`PackageJson` 同时是命名空间名，所以按
//    PackageJson.cs 文件头的做法 `using static ...PackageJson.PackageJson;` 后写裸名（裸 `PackageJson.TryParse`
//    会命中命名空间）。
// 6) `slices.Sort` + `slices.Compact` → `SortedDistinct` 小工具（`HashSet` + `List.Sort(StringComparer.Ordinal)`，
//    等价 Go 的字节序排序/去重）。
// 7) `vfsmatch.ReadDirectory` → `using static TypeScript.Vfs.VfsMatch.VfsMatch;` 的裸名调用（仓内既有写法）。
// 8) `utf8.DecodeLastRuneInString`（按字节）→ 本文件的 `DecodeLastRuneInString`（按 UTF-16 代码单元，§3.1）；
//    孤立代理项按仓内 `RuneUtil` 的先例取其代码单元值（不是 RuneError）。`pos > 4` 的边界对 ASCII 与 Go 的字节位置
//    等价（非 BMP 文件名只影响 pos 数值，不影响结果）。
// 9) `strings.Cut`/`utf8.DecodeRuneInString` 的对应：本文件只用 `StartsWith('@')`（Go 是解码首 rune 再比较）。
// 10) `TypingsInfo.TypeAcquisition`/`CompilerOptions` 在 Go 里是裸指针（nil 会 panic），C# 里可空（`?.`）：所以
//    "TypeAcquisition/CompilerOptions 为 null" 走的是 Go 里"字段未设置"的分支，而不会像 Go 那样崩。
using System.Text;
using TypeScript.Collections;
using TypeScript.Project.Logging;
using TypeScript.Vfs;
using SemVerVersion = TypeScript.SemVer.Version;
using static TypeScript.Core.Core;
using static TypeScript.SemVer.SemVer;
using static TypeScript.PackageJson.PackageJson;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.VfsMatch.VfsMatch;

namespace TypeScript.Project.Ata;

public sealed partial class TypingsInfo
{
    // Go: func isTypingUpToDate(cachedTyping *CachedTyping, availableTypingVersions map[string]string) bool
    internal static bool IsTypingUpToDate(CachedTyping cachedTyping, Dictionary<string, string> availableTypingVersions)
    {
        if (!availableTypingVersions.TryGetValue("ts" + VersionMajorMinor(), out var useVersion))
        {
            useVersion = availableTypingVersions["latest"];
        }

        var availableVersion = MustParse(useVersion);
        // Go: availableVersion.Compare(cachedTyping.Version) <= 0。`Compare` 的 `b == nil` 分支在 C# 里没有对应物
        // （接收者是值类型，见 SemVer/Version.cs 文件头第 4 条）：Go 的 `b == nil` 返回 1（> 0 → 不是最新），
        // 所以缓存的 Version 为 null 时这里直接给 false。
        return cachedTyping.Version is { } cachedVersion && availableVersion.Compare(cachedVersion) <= 0;
    }

    /// <summary>
    /// Go: <c>func DiscoverTypings(fs vfs.FS, logger logging.Logger, typingsInfo *TypingsInfo, fileNames []string,
    /// projectRootPath string, packageNameToTypingLocation *collections.SyncMap[string, *CachedTyping],
    /// typesRegistry map[string]map[string]string) (cachedTypingPaths, newTypingNames, filesToWatch []string)</c>。
    /// </summary>
    public static (string[] CachedTypingPaths, string[] NewTypingNames, string[] FilesToWatch) DiscoverTypings(
        FS fs,
        Logger? logger,
        TypingsInfo typingsInfo,
        string[] fileNames,
        string projectRootPath,
        SyncMap<string, CachedTyping> packageNameToTypingLocation,
        Dictionary<string, Dictionary<string, string>> typesRegistry)
    {
        // A typing name to typing file path mapping
        var inferredTypings = new Dictionary<string, string>(StringComparer.Ordinal);

        // Only infer typings for .js and .jsx files
        fileNames = Filter(fileNames, HasJSFileExtension);

        if (typingsInfo.TypeAcquisition is not null && typingsInfo.TypeAcquisition.Include is not null)
        {
            AddInferredTypings(fs, logger, inferredTypings, typingsInfo.TypeAcquisition.Include, "Explicitly included types");
        }

        var exclude = typingsInfo.TypeAcquisition?.Exclude ?? [];

        // Directories to search for package.json, bower.json and other typing information
        var filesToWatch = new List<string>();
        if (typingsInfo.CompilerOptions?.Types is null)
        {
            var possibleSearchDirs = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var fileName in fileNames)
            {
                possibleSearchDirs[GetDirectoryPath(fileName)] = true;
            }

            possibleSearchDirs[projectRootPath] = true;
            foreach (var searchDir in possibleSearchDirs.Keys)
            {
                AddTypingNamesAndGetFilesToWatch(fs, logger, inferredTypings, filesToWatch, searchDir, "bower.json", "bower_components");
                AddTypingNamesAndGetFilesToWatch(fs, logger, inferredTypings, filesToWatch, searchDir, "package.json", "node_modules");
            }
        }

        if (typingsInfo.TypeAcquisition is not null && !typingsInfo.TypeAcquisition.DisableFilenameBasedTypeAcquisition.IsTrue())
        {
            GetTypingNamesFromSourceFileNames(fs, logger, inferredTypings, fileNames);
        }

        // add typings for unresolved imports
        var modules = new List<string>();
        if (typingsInfo.UnresolvedImports is not null)
        {
            modules.Capacity = typingsInfo.UnresolvedImports.Len();
            foreach (var module in typingsInfo.UnresolvedImports.Keys())
            {
                modules.Add(NonRelativeModuleNameForTypingCache(module));
            }

            modules = [.. SortedDistinct(modules)];
        }

        AddInferredTypings(fs, logger, inferredTypings, modules, "Inferred typings from unresolved imports");

        // Remove typings that the user has added to the exclude list
        foreach (var excludeTypingName in exclude)
        {
            inferredTypings.Remove(excludeTypingName);
            logger?.Log($"ATA:: Typing for {excludeTypingName} is in exclude list, will be ignored.");
        }

        // Add the cached typing locations for inferred typings that are already installed
        packageNameToTypingLocation.Range((name, typing) =>
        {
            typesRegistry.TryGetValue(name, out var registryEntry);
            // Go: inferredTypings[name] == "" —— 键不存在时也是 ""（零值），所以判空要连"不存在"一起算。
            var inferredIsEmpty = !inferredTypings.TryGetValue(name, out var inferred) || inferred.Length == 0;
            if (inferredIsEmpty && registryEntry is not null && typing is not null && IsTypingUpToDate(typing, registryEntry))
            {
                inferredTypings[name] = typing.TypingsLocation;
            }

            return true;
        });

        var cachedTypingPaths = new List<string>();
        var newTypingNames = new List<string>();
        foreach (var pair in inferredTypings)
        {
            if (pair.Value.Length != 0)
            {
                cachedTypingPaths.Add(pair.Value);
            }
            else
            {
                newTypingNames.Add(pair.Key);
            }
        }

        logger?.Log(
            $"ATA:: Finished typings discovery: cachedTypingsPaths: {FormatStringSlice(cachedTypingPaths)} "
            + $"newTypingNames: {FormatStringSlice(newTypingNames)}, filesToWatch {FormatStringSlice(filesToWatch)}");
        return (cachedTypingPaths.ToArray(), newTypingNames.ToArray(), filesToWatch.ToArray());
    }

    // Go: func addInferredTyping(inferredTypings map[string]string, typingName string)
    private static void AddInferredTyping(Dictionary<string, string> inferredTypings, string typingName)
    {
        if (!inferredTypings.ContainsKey(typingName))
        {
            inferredTypings[typingName] = "";
        }
    }

    // Go: func addInferredTypings(fs vfs.FS, logger logging.Logger, inferredTypings map[string]string, typingNames []string, message string)
    private static void AddInferredTypings(
        FS fs,
        Logger? logger,
        Dictionary<string, string> inferredTypings,
        IReadOnlyList<string> typingNames,
        string message)
    {
        logger?.Log($"ATA:: {message}: {FormatStringSlice(typingNames)}");
        foreach (var typingName in typingNames)
        {
            AddInferredTyping(inferredTypings, typingName);
        }
    }

    /// <summary>
    /// Infer typing names from given file names. For example, the file name "jquery-min.2.3.4.js"
    /// should be inferred to the 'jquery' typing name; and "angular-route.1.2.3.js" should be inferred
    /// to the 'angular-route' typing name.
    /// </summary>
    /// <param name="fileNames">are the names for source files in the project</param>
    private static void GetTypingNamesFromSourceFileNames(
        FS fs,
        Logger? logger,
        Dictionary<string, string> inferredTypings,
        string[] fileNames)
    {
        var hasJsxFile = false;
        var fromFileNames = new List<string>();
        foreach (var fileName in fileNames)
        {
            hasJsxFile = hasJsxFile || FileExtensionIs(fileName, ExtensionJsx);
            var inferredTypingName = RemoveFileExtension(ToFileNameLowerCase(GetBaseFileName(fileName)));
            var cleanedTypingName = RemoveMinAndVersionNumbers(inferredTypingName);
            if (_safeFileNameToTypeName.TryGetValue(cleanedTypingName, out var typeName))
            {
                fromFileNames.Add(typeName);
            }
        }

        if (fromFileNames.Count > 0)
        {
            AddInferredTypings(fs, logger, inferredTypings, fromFileNames, "Inferred typings from file names");
        }

        if (hasJsxFile)
        {
            logger?.Log("ATA:: Inferred 'react' typings due to presence of '.jsx' extension");
            AddInferredTyping(inferredTypings, "react");
        }
    }

    /// <summary>
    /// Adds inferred typings from manifest/module pairs (think package.json + node_modules).
    /// </summary>
    /// <param name="projectRootPath">is the path to the directory where to look for package.json, bower.json and other typing information</param>
    /// <param name="manifestName">is the name of the manifest (package.json or bower.json)</param>
    /// <param name="modulesDirName">is the directory name for modules (node_modules or bower_components). Should be lowercase!</param>
    /// <param name="filesToWatch">are the files to watch for changes. We will push things into this array.</param>
    private static void AddTypingNamesAndGetFilesToWatch(
        FS fs,
        Logger? logger,
        Dictionary<string, string> inferredTypings,
        List<string> filesToWatch,
        string projectRootPath,
        string manifestName,
        string modulesDirName)
    {
        // First, we check the manifests themselves. They're not
        // _required_, but they allow us to do some filtering when dealing
        // with big flat dep directories.
        var manifestPath = CombinePaths(projectRootPath, manifestName);
        var manifestTypingNames = new List<string>();
        if (fs.TryReadFile(manifestPath, out var manifestContents))
        {
            filesToWatch.Add(manifestPath);
            if (TryParse(Encoding.UTF8.GetBytes(manifestContents), out var manifest))
            {
                // Go: manifest.Dependencies.Value / DevDependencies / OptionalDependencies / PeerDependencies 的键
                // （Go 的 maps.Keys 顺序随机；C# 是字典插入序，见文件头第 3 条）。
                if (manifest.DependencyFields.Dependencies.Value is { } dependencies)
                {
                    manifestTypingNames.AddRange(dependencies.Keys);
                }

                if (manifest.DependencyFields.DevDependencies.Value is { } devDependencies)
                {
                    manifestTypingNames.AddRange(devDependencies.Keys);
                }

                if (manifest.DependencyFields.OptionalDependencies.Value is { } optionalDependencies)
                {
                    manifestTypingNames.AddRange(optionalDependencies.Keys);
                }

                if (manifest.DependencyFields.PeerDependencies.Value is { } peerDependencies)
                {
                    manifestTypingNames.AddRange(peerDependencies.Keys);
                }

                AddInferredTypings(fs, logger, inferredTypings, manifestTypingNames, "Typing names in '" + manifestPath + "' dependencies");
            }
        }

        // Now we scan the directories for typing information in
        // already-installed dependencies (if present). Note that this
        // step happens regardless of whether a manifest was present,
        // which is certainly a valid configuration, if an unusual one.
        var packagesFolderPath = CombinePaths(projectRootPath, modulesDirName);
        filesToWatch.Add(packagesFolderPath);
        if (!fs.DirectoryExists(packagesFolderPath))
        {
            return;
        }

        // There's two cases we have to take into account here:
        // 1. If manifest is undefined, then we're not using a manifest.
        //    That means that we should scan _all_ dependencies at the top
        //    level of the modulesDir.
        // 2. If manifest is defined, then we can do some special
        //    filtering to reduce the amount of scanning we need to do.
        //
        // Previous versions of this algorithm checked for a `_requiredBy`
        // field in the package.json, but that field is only present in
        // `npm@>=3 <7`.

        // Package names that do **not** provide their own typings, so
        // we'll look them up.
        var packageNames = new List<string>();

        var dependencyManifestNames = new List<string>();
        if (manifestTypingNames.Count > 0)
        {
            // This is #1 described above.
            foreach (var typingName in manifestTypingNames)
            {
                dependencyManifestNames.Add(CombinePaths(packagesFolderPath, typingName, manifestName));
            }
        }
        else
        {
            // And #2. Depth = 3 because scoped packages look like `node_modules/@foo/bar/package.json`
            var depth = 3;
            foreach (var manifestFile in ReadDirectory(fs, projectRootPath, packagesFolderPath, [ExtensionJson], null, null, depth))
            {
                if (GetBaseFileName(manifestFile) != manifestName)
                {
                    continue;
                }

                // It's ok to treat
                // `node_modules/@foo/bar/package.json` as a manifest,
                // but not `node_modules/jquery/nested/package.json`.
                // We only assume depth 3 is ok for formally scoped
                // packages. So that needs this dance here.

                var pathComponents = GetPathComponents(manifestFile, "");
                var lenPathComponents = pathComponents.Length;
                var isScoped = pathComponents[lenPathComponents - 3].StartsWith('@');

                if ((isScoped && ToFileNameLowerCase(pathComponents[lenPathComponents - 4]) == modulesDirName) // `node_modules/@foo/bar`
                    || (!isScoped && ToFileNameLowerCase(pathComponents[lenPathComponents - 3]) == modulesDirName)) // `node_modules/foo`
                {
                    dependencyManifestNames.Add(manifestFile);
                }
            }
        }

        logger?.Log($"ATA:: Searching for typing names in {packagesFolderPath}; all files: {FormatStringSlice(dependencyManifestNames)}");

        // Once we have the names of things to look up, we iterate over
        // and either collect their included typings, or add them to the
        // list of typings we need to look up separately.
        foreach (var dependencyManifestPath in dependencyManifestNames)
        {
            if (!fs.TryReadFile(dependencyManifestPath, out var dependencyManifestContents))
            {
                continue;
            }

            // If the package has its own d.ts typings, those will take precedence. Otherwise the package name will be used
            // to download d.ts files from DefinitelyTyped
            if (!TryParse(Encoding.UTF8.GetBytes(dependencyManifestContents), out var dependencyManifest))
            {
                continue;
            }

            var manifestNameValue = dependencyManifest.HeaderFields.Name.Value ?? "";
            if (manifestNameValue.Length == 0)
            {
                continue;
            }

            var ownTypes = dependencyManifest.PathFields.Types.Value ?? "";
            if (ownTypes.Length == 0)
            {
                ownTypes = dependencyManifest.PathFields.Typings.Value ?? "";
            }

            if (ownTypes.Length != 0)
            {
                var absolutePath = GetNormalizedAbsolutePath(ownTypes, GetDirectoryPath(dependencyManifestPath));
                if (fs.FileExists(absolutePath))
                {
                    logger?.Log($"ATA::     Package '{manifestNameValue}' provides its own types.");
                    inferredTypings[manifestNameValue] = absolutePath;
                }
                else
                {
                    logger?.Log($"ATA::     Package '{manifestNameValue}' provides its own types but they are missing.");
                }
            }
            else
            {
                packageNames.Add(manifestNameValue);
            }
        }

        AddInferredTypings(fs, logger, inferredTypings, packageNames, "    Found package names");
    }

    /// <summary>
    /// Takes a string like "jquery-min.4.2.3" and returns "jquery".
    /// </summary>
    internal static string RemoveMinAndVersionNumbers(string fileName)
    {
        // We used to use the regex /[.-]((min)|(\d+(\.\d+)*))$/ and would just .replace it twice.
        // Unfortunately, that regex has O(n^2) performance because v8 doesn't match from the end of the string.
        // Instead, we now essentially scan the filename (backwards) ourselves.
        var end = fileName.Length;
        var pos = end;
        while (pos > 0)
        {
            var ch = DecodeLastRuneInString(fileName, pos, out var size);
            if (ch >= '0' && ch <= '9')
            {
                // Match a \d+ segment
                while (true)
                {
                    pos -= size;
                    ch = DecodeLastRuneInString(fileName, pos, out size);
                    if (pos <= 0 || ch < '0' || ch > '9')
                    {
                        break;
                    }
                }
            }
            else if (pos > 4 && (ch == 'n' || ch == 'N'))
            {
                // Looking for "min" or "min"
                // Already matched the 'n'
                pos -= size;
                ch = DecodeLastRuneInString(fileName, pos, out size);
                if (ch != 'i' && ch != 'I')
                {
                    break;
                }

                pos -= size;
                ch = DecodeLastRuneInString(fileName, pos, out size);
                if (ch != 'm' && ch != 'M')
                {
                    break;
                }

                pos -= size;
                ch = DecodeLastRuneInString(fileName, pos, out size);
            }
            else
            {
                // This character is not part of either suffix pattern
                break;
            }

            if (ch != '-' && ch != '.')
            {
                break;
            }

            pos -= size;
            end = pos;
        }

        return fileName[..end];
    }

    // Go: utf8.DecodeLastRuneInString(s[:end])（§3.1：UTF-16 代码单元；`end <= 0` 时是 Go 的 (RuneError, 0)）。
    private static int DecodeLastRuneInString(string s, int end, out int size)
    {
        if (end <= 0)
        {
            size = 0;
            return 0xFFFD;
        }

        if (end >= 2 && char.IsLowSurrogate(s[end - 1]) && char.IsHighSurrogate(s[end - 2]))
        {
            size = 2;
            return char.ConvertToUtf32(s[end - 2], s[end - 1]);
        }

        size = 1;
        return s[end - 1];
    }

    // Go: slices.Sort + slices.Compact（元素是字符串 → Ordinal 排序/相等）。
    private static List<string> SortedDistinct(List<string> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            if (seen.Add(item))
            {
                result.Add(item);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    // Go 的 `%v` 打 []string → `[a b c]`（空切片是 `[]`）。日志文本与 Go 逐字一致（ata.go 也用这个小工具）。
    internal static string FormatStringSlice(IReadOnlyList<string> items)
    {
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(items[i]);
        }

        builder.Append(']');
        return builder.ToString();
    }
}
