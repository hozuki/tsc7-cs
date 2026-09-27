// ported from: internal/modulespecifiers/specifiers.go:1089-1331（tryGetModuleNameFromPaths + validateEnding + tryGetModuleNameFromExportsOrImports）
//
// 与 Go 的偏离（在 Specifiers.cs 文件头的 7 条之外，本分片特有的）：
// 1) `strings.Cut(s, "*")` → 私有 helper `CutAtStar` 返回 `(string Prefix, string Suffix, bool Found)`（Go 的
//    Cut 语义：没找到时 before = s、after = ""）。
// 2) `slices.Contains(conditions, x)` → `Array.IndexOf(conditions, x) != -1`（避免 LINQ，§3.8）。
// 3) `exports.Value.(string)`（Go 的类型断言）→ `(string)exports.Value!`（JSONValue.Value 是 object?）。
// 4) `tspath.ComparePathsOptions{…}` → `new ComparePathsOptions(useCaseSensitiveFileNames, currentDirectory)`。
// 5) `specPair` 是未导出结构体 → private `readonly record struct`（字段名按 C# 习惯 PascalCase）。
// 6) `outputpaths.GetOutputJSFileNameWorker/GetOutputDeclarationFileNameWorker` 的 host 参数类型是
//    `OutputPathsHost`；`ModuleSpecifierGenerationHost : OutputPathsHost`（Types.cs 文件头第 7 条）使调用成立。
// 7) `getJSExtensionForFile`（本包会抛的版本）与 `module.TryGetJSExtensionForFile`（返回 "" 的版本）在 Go 里
//    是**两个不同的函数**，本分片两处分别照抄（Directory 分支用前者，Pattern 分支用后者）。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.PackageJson;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.OutputPaths.OutputPaths;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;

using TypeScript.TSPath;

namespace TypeScript.ModuleSpecifiers;

public static partial class ModuleSpecifiers
{
    /// <summary>Go: <c>type specPair struct { ending ModuleSpecifierEnding; value string }</c>（未导出）。</summary>
    private readonly record struct SpecPair(ModuleSpecifierEnding Ending, string Value);

    /// <summary>Go: <c>strings.Cut(s, "*")</c>（本分片用；两处）。</summary>
    private static (string Prefix, string Suffix, bool Found) CutAtStar(string s)
    {
        var index = s.IndexOf('*');
        if (index < 0)
        {
            return (s, "", false);
        }

        return (s[..index], s[(index + 1)..], true);
    }

    /// <summary>Go: <c>func tryGetModuleNameFromPaths(relativeToBaseUrl string, paths *collections.OrderedMap[string, []string], allowedEndings []ModuleSpecifierEnding, baseDirectory string, host ModuleSpecifierGenerationHost, compilerOptions *core.CompilerOptions) string</c>。</summary>
    private static string TryGetModuleNameFromPaths(
        string relativeToBaseUrl,
        OrderedMap<string, string[]> paths,
        ModuleSpecifierEnding[] allowedEndings,
        string baseDirectory,
        ModuleSpecifierGenerationHost host,
        CompilerOptions compilerOptions)
    {
        var caseSensitive = host.UseCaseSensitiveFileNames();
        foreach (var (key, values) in paths.Entries())
        {
            foreach (var patternText in values ?? [])
            {
                var normalized = NormalizePath(patternText);
                var pattern = GetRelativePathIfInSameVolume(normalized, baseDirectory, caseSensitive);
                if (pattern.Length == 0)
                {
                    pattern = normalized;
                }

                var (prefix, suffix, ok) = CutAtStar(pattern);

                // In module resolution, if `pattern` itself has an extension, a file with that extension is looked up directly,
                // meaning a '.ts' or '.d.ts' extension is allowed to resolve. This is distinct from the case where a '*' substitution
                // causes a module specifier to have an extension, i.e. the extension comes from the module specifier in a JS/TS file
                // and matches the '*'. For example:
                //
                // Module Specifier      | Path Mapping (key: [pattern]) | Interpolation       | Resolution Action
                // ---------------------->------------------------------->--------------------->---------------------------------------------------------------
                // import "@app/foo"    -> "@app/*": ["./src/app/*.ts"] -> "./src/app/foo.ts" -> tryFile("./src/app/foo.ts") || [continue resolution algorithm]
                // import "@app/foo.ts" -> "@app/*": ["./src/app/*"]    -> "./src/app/foo.ts" -> [continue resolution algorithm]
                //
                // (https://github.com/microsoft/TypeScript/blob/ad4ded80e1d58f0bf36ac16bea71bc10d9f09895/src/compiler/moduleNameResolver.ts#L2509-L2516)
                //
                // The interpolation produced by both scenarios is identical, but only in the former, where the extension is encoded in
                // the path mapping rather than in the module specifier, will we prioritize a file lookup on the interpolation result.
                // (In fact, currently, the latter scenario will necessarily fail since no resolution mode recognizes '.ts' as a valid
                // extension for a module specifier.)
                //
                // Here, this means we need to be careful about whether we generate a match from the target filename (typically with a
                // .ts extension) or the possible relative module specifiers representing that file:
                //
                // Filename            | Relative Module Specifier Candidates         | Path Mapping                 | Filename Result    | Module Specifier Results
                // --------------------<----------------------------------------------<------------------------------<-------------------||----------------------------
                // dist/haha.d.ts      <- dist/haha, dist/haha.js                     <- "@app/*": ["./dist/*.d.ts"] <- @app/haha        || (none)
                // dist/haha.d.ts      <- dist/haha, dist/haha.js                     <- "@app/*": ["./dist/*"]      <- (none)           || @app/haha, @app/haha.js
                // dist/foo/index.d.ts <- dist/foo, dist/foo/index, dist/foo/index.js <- "@app/*": ["./dist/*.d.ts"] <- @app/foo/index   || (none)
                // dist/foo/index.d.ts <- dist/foo, dist/foo/index, dist/foo/index.js <- "@app/*": ["./dist/*"]      <- (none)           || @app/foo, @app/foo/index, @app/foo/index.js
                // dist/wow.js.js      <- dist/wow.js, dist/wow.js.js                 <- "@app/*": ["./dist/*.js"]   <- @app/wow.js      || @app/wow, @app/wow.js
                //
                // The "Filename Result" can be generated only if `pattern` has an extension. Care must be taken that the list of
                // relative module specifiers to run the interpolation (a) is actually valid for the module resolution mode, (b) takes
                // into account the existence of other files (e.g. 'dist/wow.js' cannot refer to 'dist/wow.js.js' if 'dist/wow.js'
                // exists) and (c) that they are ordered by preference. The last row shows that the filename result and module
                // specifier results are not mutually exclusive. Note that the filename result is a higher priority in module
                // resolution, but as long criteria (b) above is met, I don't think its result needs to be the highest priority result
                // in module specifier generation. I have included it last, as it's difficult to tell exactly where it should be
                // sorted among the others for a particular value of `importModuleSpecifierEnding`.
                List<SpecPair> candidates = [];
                foreach (var ending in allowedEndings)
                {
                    var result = ProcessEnding(
                        relativeToBaseUrl,
                        [ending],
                        compilerOptions,
                        host);
                    candidates.Add(new SpecPair(ending, result));
                }

                if (TryGetExtensionFromPath(pattern).Length > 0)
                {
                    candidates.Add(new SpecPair(ModuleSpecifierEnding.JsExtension, relativeToBaseUrl));
                }

                var candidateArray = candidates.ToArray();
                if (ok)
                {
                    foreach (var c in candidateArray)
                    {
                        var value = c.Value;
                        if (value.Length >= prefix.Length + suffix.Length
                            && HasPrefix(value, prefix, caseSensitive) // TODO: possible strada bug: these are not case-switched in strada
                            && HasSuffix(value, suffix, caseSensitive)
                            && ValidateEnding(c, relativeToBaseUrl, compilerOptions, host))
                        {
                            var matchedStar = value[prefix.Length..(value.Length - suffix.Length)];
                            if (!PathIsRelative(matchedStar))
                            {
                                return ReplaceFirstStar(key, matchedStar);
                            }
                        }
                    }
                }
                else if (Some(candidateArray, c => c.Ending != ModuleSpecifierEnding.Minimal && pattern == c.Value)
                    || Some(candidateArray, c => c.Ending == ModuleSpecifierEnding.Minimal && pattern == c.Value && ValidateEnding(c, relativeToBaseUrl, compilerOptions, host)))
                {
                    return key;
                }
            }
        }

        return "";
    }

    /// <summary>Go: <c>func validateEnding(c specPair, relativeToBaseUrl string, compilerOptions *core.CompilerOptions, host ModuleSpecifierGenerationHost) bool</c>。</summary>
    private static bool ValidateEnding(
        SpecPair c,
        string relativeToBaseUrl,
        CompilerOptions compilerOptions,
        ModuleSpecifierGenerationHost host)
    {
        // Optimization: `removeExtensionAndIndexPostFix` can query the file system (a good bit) if `ending` is `Minimal`, the basename
        // is 'index', and a `host` is provided. To avoid that until it's unavoidable, we ran the function with no `host` above. Only
        // here, after we've checked that the minimal ending is indeed a match (via the length and prefix/suffix checks / `some` calls),
        // do we check that the host-validated result is consistent with the answer we got before. If it's not, it falls back to the
        // `ModuleSpecifierEnding.Index` result, which should already be in the list of candidates if `Minimal` was. (Note: the assumption here is
        // that every module resolution mode that supports dropping extensions also supports dropping `/index`. Like literally
        // everything else in this file, this logic needs to be updated if that's not true in some future module resolution mode.)
        return c.Ending != ModuleSpecifierEnding.Minimal
            || c.Value == ProcessEnding(relativeToBaseUrl, [c.Ending], compilerOptions, host);
    }

    /// <summary>Go: <c>func tryGetModuleNameFromExportsOrImports(options *core.CompilerOptions, host ModuleSpecifierGenerationHost, targetFilePath string, packageDirectory string, packageName string, exports packagejson.ExportsOrImports, conditions []string, mode MatchingMode, isImports bool, preferTsExtension bool) string</c>。</summary>
    internal static string TryGetModuleNameFromExportsOrImports(
        CompilerOptions options,
        ModuleSpecifierGenerationHost host,
        string targetFilePath,
        string packageDirectory,
        string packageName,
        ExportsOrImports exports,
        string[] conditions,
        MatchingMode mode,
        bool isImports,
        bool preferTsExtension)
    {
        switch (exports.Type)
        {
            case JSONValueType.NotPresent:
                return "";
            case JSONValueType.String:
                var strValue = (string)exports.Value!;

                // possible strada bug? Always uses compilerOptions of the host project, not those applicable to the targeted package.json!
                var outputFile = "";
                var declarationFile = "";
                if (isImports)
                {
                    outputFile = GetOutputJSFileNameWorker(targetFilePath, options, host);
                    declarationFile = GetOutputDeclarationFileNameWorker(targetFilePath, options, host);
                }

                var pathOrPattern = GetNormalizedAbsolutePath(CombinePaths(packageDirectory, strValue), "");
                var extensionSwappedTarget = "";
                if (HasTSFileExtension(targetFilePath))
                {
                    extensionSwappedTarget = RemoveFileExtension(targetFilePath) + TryGetJSExtensionForFile(targetFilePath, options);
                }

                var canTryTsExtension = preferTsExtension && HasImplementationTSFileExtension(targetFilePath);

                var compareOpts = new ComparePathsOptions(host.UseCaseSensitiveFileNames(), host.GetCurrentDirectory());

                switch (mode)
                {
                    case MatchingMode.Exact:
                        if ((extensionSwappedTarget.Length > 0 && ComparePaths(extensionSwappedTarget, pathOrPattern, compareOpts) == 0)
                            || ComparePaths(targetFilePath, pathOrPattern, compareOpts) == 0
                            || (outputFile.Length > 0 && ComparePaths(outputFile, pathOrPattern, compareOpts) == 0)
                            || (declarationFile.Length > 0 && ComparePaths(declarationFile, pathOrPattern, compareOpts) == 0))
                        {
                            return packageName;
                        }

                        break;
                    case MatchingMode.Directory:
                        if (canTryTsExtension && ContainsPath(targetFilePath, pathOrPattern, compareOpts))
                        {
                            var fragment = GetRelativePathFromDirectory(pathOrPattern, targetFilePath, compareOpts);
                            return GetNormalizedAbsolutePath(CombinePaths(CombinePaths(packageName, strValue), fragment), "");
                        }

                        if (extensionSwappedTarget.Length > 0 && ContainsPath(pathOrPattern, extensionSwappedTarget, compareOpts))
                        {
                            var fragment = GetRelativePathFromDirectory(pathOrPattern, extensionSwappedTarget, compareOpts);
                            return GetNormalizedAbsolutePath(CombinePaths(CombinePaths(packageName, strValue), fragment), "");
                        }

                        if (!canTryTsExtension && ContainsPath(pathOrPattern, targetFilePath, compareOpts))
                        {
                            var fragment = GetRelativePathFromDirectory(pathOrPattern, targetFilePath, compareOpts);
                            return GetNormalizedAbsolutePath(CombinePaths(CombinePaths(packageName, strValue), fragment), "");
                        }

                        if (outputFile.Length > 0 && ContainsPath(pathOrPattern, outputFile, compareOpts))
                        {
                            var fragment = GetRelativePathFromDirectory(pathOrPattern, outputFile, compareOpts);
                            return CombinePaths(packageName, fragment);
                        }

                        if (declarationFile.Length > 0 && ContainsPath(pathOrPattern, declarationFile, compareOpts))
                        {
                            var fragment = GetRelativePathFromDirectory(pathOrPattern, declarationFile, compareOpts);
                            var jsExtension = GetJSExtensionForFile(declarationFile, options);
                            var fragmentWithJsExtension = ChangeExtension(fragment, jsExtension);
                            return CombinePaths(packageName, fragmentWithJsExtension);
                        }

                        break;
                    case MatchingMode.Pattern:
                        var (leadingSlice, trailingSlice, _) = CutAtStar(pathOrPattern);
                        var caseSensitive = host.UseCaseSensitiveFileNames();
                        if (canTryTsExtension && HasPrefixAndSuffixWithoutOverlap(targetFilePath, leadingSlice, trailingSlice, caseSensitive))
                        {
                            var starReplacement = targetFilePath[leadingSlice.Length..(targetFilePath.Length - trailingSlice.Length)];
                            return ReplaceFirstStar(packageName, starReplacement);
                        }

                        if (extensionSwappedTarget.Length > 0 && HasPrefixAndSuffixWithoutOverlap(extensionSwappedTarget, leadingSlice, trailingSlice, caseSensitive))
                        {
                            var starReplacement = extensionSwappedTarget[leadingSlice.Length..(extensionSwappedTarget.Length - trailingSlice.Length)];
                            return ReplaceFirstStar(packageName, starReplacement);
                        }

                        if (!canTryTsExtension && HasPrefixAndSuffixWithoutOverlap(targetFilePath, leadingSlice, trailingSlice, caseSensitive))
                        {
                            var starReplacement = targetFilePath[leadingSlice.Length..(targetFilePath.Length - trailingSlice.Length)];
                            return ReplaceFirstStar(packageName, starReplacement);
                        }

                        if (outputFile.Length > 0 && HasPrefixAndSuffixWithoutOverlap(outputFile, leadingSlice, trailingSlice, caseSensitive))
                        {
                            var starReplacement = outputFile[leadingSlice.Length..(outputFile.Length - trailingSlice.Length)];
                            return ReplaceFirstStar(packageName, starReplacement);
                        }

                        if (declarationFile.Length > 0 && HasPrefixAndSuffixWithoutOverlap(declarationFile, leadingSlice, trailingSlice, caseSensitive))
                        {
                            var starReplacement = declarationFile[leadingSlice.Length..(declarationFile.Length - trailingSlice.Length)];
                            var substituted = ReplaceFirstStar(packageName, starReplacement);
                            var jsExtension = TryGetJSExtensionForFile(declarationFile, options);
                            if (jsExtension.Length > 0)
                            {
                                return ChangeFullExtension(substituted, jsExtension);
                            }
                        }

                        break;
                }

                return "";
            case JSONValueType.Array:
                var array = exports.AsArray();
                foreach (var e in array)
                {
                    var result = TryGetModuleNameFromExportsOrImports(options, host, targetFilePath, packageDirectory, packageName, e, conditions, mode, isImports, preferTsExtension);
                    if (result.Length > 0)
                    {
                        return result;
                    }
                }

                break;
            case JSONValueType.Object:
                // conditional mapping
                var obj = exports.AsObject()!; // case 已判 Type == Object；Go 同点解引用（nil 即 panic）。
                foreach (var (key, value) in obj.Entries())
                {
                    if (key == "default"
                        || Array.IndexOf(conditions, key) != -1
                        || (Array.IndexOf(conditions, "types") != -1 && IsApplicableVersionedTypesKey(key)))
                    {
                        var result = TryGetModuleNameFromExportsOrImports(options, host, targetFilePath, packageDirectory, packageName, value!, conditions, mode, isImports, preferTsExtension);
                        if (result.Length > 0)
                        {
                            return result;
                        }
                    }
                }

                break;
            case JSONValueType.Null:
                return "";
        }

        return "";
    }
}
