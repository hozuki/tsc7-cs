// ported from: internal/module/resolver.go:1121-1700（结果构造与符号链接回映射；optional resolution settings
// （paths/rootDirs）；相对名加载；文件/扩展名试探；目录（package.json main/types/index）加载）
//
// 与 Go 的偏离（同 Resolver.cs 文件头）：
// - `x.Exists()`（Go 的 nil 接收者安全方法）= C# 的 `x is not null && x.GetContents() is not null`，
//   本文件统一写成 `if (x is { } info && info.GetContents() is { } contents)`。
// - Go 的 `VersionPaths` 零值（或 `packageInfo` 为 nil 时）用 `new VersionPaths()` 表达（Exists() 为 false）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.PackageJson;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

public static partial class Module
{
    /// <summary>Go 的 <c>(*resolutionState).createResolvedModuleHandlingSymlink</c>。</summary>
    internal static ResolvedModule CreateResolvedModuleHandlingSymlink(this ResolutionState r, Resolved? resolved)
    {
        var isExternalLibraryImport = resolved is not null && resolved.Path.Contains("/node_modules/", StringComparison.Ordinal);
        if (r._compilerOptions.PreserveSymlinks != Tristate.TSTrue &&
            isExternalLibraryImport &&
            resolved is not null &&
            resolved.OriginalPath == "" &&
            !IsExternalModuleNameRelative(r._name))
        {
            var (originalPath, resolvedFileName) = r.GetOriginalAndResolvedFileName(resolved.Path);
            if (originalPath != "")
            {
                resolved.Path = resolvedFileName;
                resolved.OriginalPath = originalPath;
            }
        }

        return r.CreateResolvedModule(resolved, isExternalLibraryImport);
    }

    /// <summary>Go 的 <c>(*resolutionState).createResolvedModule</c>。</summary>
    internal static ResolvedModule CreateResolvedModule(this ResolutionState r, Resolved? resolved, bool isExternalLibraryImport)
    {
        var resolvedModule = new ResolvedModule
        {
            ResolutionDiagnostics = [.. r._diagnostics],
        };

        if (resolved is not null)
        {
            resolvedModule.ResolvedFileName = resolved.Path;
            resolvedModule.OriginalPath = resolved.OriginalPath;
            resolvedModule.IsExternalLibraryImport = isExternalLibraryImport;
            resolvedModule.ResolvedUsingTsExtension = resolved.ResolvedUsingTsExtension;
            resolvedModule.ResolvedUsingExtraExtensions = resolved.ResolvedUsingExtraExtensions;
            resolvedModule.Extension = resolved.Extension;
            resolvedModule.PackageId = resolved.PackageId;
        }

        return resolvedModule;
    }

    /// <summary>Go 的 <c>(*resolutionState).createResolvedTypeReferenceDirective</c>。</summary>
    internal static ResolvedTypeReferenceDirective CreateResolvedTypeReferenceDirective(this ResolutionState r, Resolved? resolved, bool primary)
    {
        var resolvedTypeReferenceDirective = new ResolvedTypeReferenceDirective
        {
            ResolutionDiagnostics = [.. r._diagnostics],
        };

        if (resolved is not null && resolved.IsResolved())
        {
            if (!ExtensionIsTs(resolved.Extension))
            {
                throw new DebugFailureException("expected a TypeScript file extension");
            }

            resolvedTypeReferenceDirective.ResolvedFileName = resolved.Path;
            resolvedTypeReferenceDirective.Primary = primary;
            resolvedTypeReferenceDirective.PackageId = resolved.PackageId;
            resolvedTypeReferenceDirective.IsExternalLibraryImport = resolved.Path.Contains("/node_modules/", StringComparison.Ordinal);

            if (r._compilerOptions.PreserveSymlinks != Tristate.TSTrue)
            {
                var (originalPath, resolvedFileName) = r.GetOriginalAndResolvedFileName(resolved.Path);
                if (originalPath != "")
                {
                    resolvedTypeReferenceDirective.ResolvedFileName = resolvedFileName;
                    resolvedTypeReferenceDirective.OriginalPath = originalPath;
                }
            }
        }

        return resolvedTypeReferenceDirective;
    }

    /// <summary>Go 的 <c>(*resolutionState).getOriginalAndResolvedFileName</c>。</summary>
    internal static (string OriginalPath, string ResolvedFileName) GetOriginalAndResolvedFileName(this ResolutionState r, string fileName)
    {
        var resolvedFileName = r.RealPath(fileName);
        var comparePathsOptions = new ComparePathsOptions(r._resolver.Host.FS().UseCaseSensitiveFileNames(), r._resolver.Host.GetCurrentDirectory());
        if (ComparePaths(fileName, resolvedFileName, comparePathsOptions) == 0)
        {
            // If the fileName and realpath are differing only in casing, prefer fileName
            // so that we can issue correct errors for casing under forceConsistentCasingInFileNames
            return ("", fileName);
        }

        return (fileName, resolvedFileName);
    }

    /// <summary>Go 的 <c>(*resolutionState).tryLoadModuleUsingOptionalResolutionSettings</c>。</summary>
    internal static Resolved? TryLoadModuleUsingOptionalResolutionSettings(this ResolutionState r)
    {
        if (r.TryLoadModuleUsingPathsIfEligible() is { } resolved)
        {
            return resolved;
        }

        if (!IsExternalModuleNameRelative(r._name))
        {
            // No more tryLoadModuleUsingBaseUrl.
            return null;
        }

        return r.TryLoadModuleUsingRootDirs();
    }

    /// <summary>Go 的 <c>(*resolutionState).getParsedPatternsForPaths</c>。</summary>
    internal static ParsedPatterns GetParsedPatternsForPaths(this ResolutionState r)
    {
        return r._resolver.GetParsedPatternsForPaths(r._compilerOptions);
    }

    /// <summary>Go 的 <c>(*resolutionState).tryLoadModuleUsingPathsIfEligible</c>。</summary>
    internal static Resolved? TryLoadModuleUsingPathsIfEligible(this ResolutionState r)
    {
        var paths = r._compilerOptions.Paths;
        if ((paths?.Size() ?? 0) > 0 && !PathIsRelative(r._name))
        {
            r._tracer?.Write(X_paths_option_is_specified_looking_for_a_pattern_to_match_module_name_0, r._name);
        }
        else
        {
            return null;
        }

        var baseDirectory = r._compilerOptions.GetPathsBasePath(r._resolver.Host.GetCurrentDirectory());
        var pathPatterns = r.GetParsedPatternsForPaths();
        return r.TryLoadModuleUsingPaths(
            r._extensions,
            r._name,
            baseDirectory,
            paths!,
            pathPatterns,
            (extensions, candidate) => r.NodeLoadModuleByRelativeName(extensions, candidate, true /*considerPackageJson*/));
    }

    /// <summary>Go 的 <c>(*resolutionState).tryLoadModuleUsingPaths</c>。</summary>
    internal static Resolved? TryLoadModuleUsingPaths(
        this ResolutionState r,
        Extensions extensions,
        string moduleName,
        string containingDirectory,
        OrderedMap<string, string[]> paths,
        ParsedPatterns pathPatterns,
        ResolutionKindSpecificLoader loader)
    {
        var matchedPattern = MatchPatternOrExact(pathPatterns, moduleName);
        if (matchedPattern.IsValid())
        {
            var matchedStar = matchedPattern.MatchedText(moduleName);
            r._tracer?.Write(Module_name_0_matched_pattern_1, moduleName, matchedPattern.Text);
            foreach (var subst in paths.GetOrZero(matchedPattern.Text) ?? [])
            {
                var path = ReplaceFirst(subst, "*", matchedStar);
                var candidate = NormalizePath(CombinePaths(containingDirectory, path));
                r._tracer?.Write(Trying_substitution_0_candidate_module_location_Colon_1, subst, path);

                // A path mapping may have an extension
                var extensionFromSubst = TryGetExtensionFromPath(subst);
                if (extensionFromSubst != "")
                {
                    if (r.TryFile(candidate, out var candidatePath))
                    {
                        return new Resolved
                        {
                            Path = candidatePath,
                            Extension = extensionFromSubst,
                        };
                    }
                }

                // When the substitution path has an explicit extension, the extension came from the
                // paths config, not the module specifier. Suppress resolvedUsingTsExtension in that case.
                var saveCandidateEndingIsFromConfig = r._candidateEndingIsFromConfig;
                if (extensionFromSubst != "")
                {
                    r._candidateEndingIsFromConfig = true;
                }

                var resolved = loader(extensions, candidate);
                r._candidateEndingIsFromConfig = saveCandidateEndingIsFromConfig;
                if (resolved is not null)
                {
                    return resolved;
                }
            }
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).tryLoadModuleUsingRootDirs</c>。</summary>
    internal static Resolved? TryLoadModuleUsingRootDirs(this ResolutionState r)
    {
        if (r._compilerOptions.RootDirs is not { Length: > 0 } rootDirs)
        {
            return null;
        }

        r._tracer?.Write(X_rootDirs_option_is_set_using_it_to_resolve_relative_module_name_0, r._name);

        var candidate = NormalizePath(CombinePaths(r._containingDirectory, r._name));

        var matchedRootDir = "";
        var matchedNormalizedPrefix = "";
        foreach (var rootDir in rootDirs)
        {
            // rootDirs are expected to be absolute
            // in case of tsconfig.json this will happen automatically - compiler will expand relative names
            // using location of tsconfig.json as base location
            var normalizedRoot = NormalizePath(rootDir);
            if (!normalizedRoot.EndsWith('/'))
            {
                normalizedRoot += "/";
            }

            var isLongestMatchingPrefix = candidate.StartsWith(normalizedRoot, StringComparison.Ordinal) &&
                (matchedNormalizedPrefix == "" || matchedNormalizedPrefix.Length < normalizedRoot.Length);

            r._tracer?.Write(Checking_if_0_is_the_longest_matching_prefix_for_1_2, normalizedRoot, candidate, isLongestMatchingPrefix);

            if (isLongestMatchingPrefix)
            {
                matchedNormalizedPrefix = normalizedRoot;
                matchedRootDir = rootDir;
            }
        }

        if (matchedNormalizedPrefix != "")
        {
            r._tracer?.Write(Longest_matching_prefix_for_0_is_1, candidate, matchedNormalizedPrefix);
            var suffix = candidate[matchedNormalizedPrefix.Length..];

            // first - try to load from a initial location
            r._tracer?.Write(Loading_0_from_the_root_dir_1_candidate_location_2, suffix, matchedNormalizedPrefix, candidate);
            Resolved? Loader(Extensions extensions, string candidate) => r.NodeLoadModuleByRelativeName(extensions, candidate, true /*considerPackageJson*/);

            if (Loader(r._extensions, candidate) is { } resolvedFileName)
            {
                return resolvedFileName;
            }

            r._tracer?.Write(Trying_other_entries_in_rootDirs);
            // then try to resolve using remaining entries in rootDirs
            foreach (var rootDir in rootDirs)
            {
                if (rootDir == matchedRootDir)
                {
                    // skip the initially matched entry
                    continue;
                }

                var otherCandidate = CombinePaths(NormalizePath(rootDir), suffix);
                r._tracer?.Write(Loading_0_from_the_root_dir_1_candidate_location_2, suffix, rootDir, otherCandidate);
                if (Loader(r._extensions, otherCandidate) is { } otherResolvedFileName)
                {
                    return otherResolvedFileName;
                }
            }

            r._tracer?.Write(Module_resolution_using_rootDirs_has_failed);
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).nodeLoadModuleByRelativeName</c>。</summary>
    internal static Resolved? NodeLoadModuleByRelativeName(this ResolutionState r, Extensions extensions, string candidate, bool considerPackageJson)
    {
        r._tracer?.Write(Loading_module_as_file_Slash_folder_candidate_module_location_0_target_file_types_Colon_1, candidate, extensions.String());
        if (!HasTrailingDirectorySeparator(candidate))
        {
            var parentOfCandidate = GetDirectoryPath(candidate);
            if (!r._resolver.Host.FS().DirectoryExists(parentOfCandidate))
            {
                r._tracer?.Write(Directory_0_does_not_exist_skipping_all_lookups_in_it, parentOfCandidate);
                return null;
            }

            var resolvedFromFile = r.LoadModuleFromFile(extensions, candidate);
            if (resolvedFromFile is not null)
            {
                if (considerPackageJson)
                {
                    if (ParseNodeModuleFromPath(resolvedFromFile.Path /*isFolder*/, false) is { Length: > 0 } packageDirectory)
                    {
                        resolvedFromFile.PackageId = r.GetPackageId(resolvedFromFile.Path, r.GetPackageJsonInfo(packageDirectory));
                    }
                }

                return resolvedFromFile;
            }
        }

        if (!r._resolver.Host.FS().DirectoryExists(candidate))
        {
            r._tracer?.Write(Directory_0_does_not_exist_skipping_all_lookups_in_it, candidate);
            return null;
        }

        // esm mode relative imports shouldn't do any directory lookups (either inside `package.json`
        // files or implicit `index.js`es). This is a notable departure from cjs norms, where `./foo/pkg`
        // could have been redirected by `./foo/pkg/package.json` to an arbitrary location!
        if (!r._esmMode)
        {
            return r.LoadNodeModuleFromDirectory(extensions, candidate, considerPackageJson);
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromFile</c>。</summary>
    internal static Resolved? LoadModuleFromFile(this ResolutionState r, Extensions extensions, string candidate)
    {
        // ./foo.js -> ./foo.ts
        var resolvedByReplacingExtension = r.LoadModuleFromFileNoImplicitExtensions(extensions, candidate);
        if (resolvedByReplacingExtension is not null)
        {
            return resolvedByReplacingExtension;
        }

        // ./foo -> ./foo.ts
        if (!r._esmMode)
        {
            return r.TryAddingExtensions(candidate, extensions, "");
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromFileNoImplicitExtensions</c>。</summary>
    internal static Resolved? LoadModuleFromFileNoImplicitExtensions(this ResolutionState r, Extensions extensions, string candidate)
    {
        var @base = GetBaseFileName(candidate);
        if (!@base.Contains('.'))
        {
            return null; // extensionless import, no lookups performed, since we don't support extensionless files
        }

        var extensionless = RemoveFileExtension(candidate);
        if (extensionless == candidate)
        {
            // Once TS native extensions are handled, handle arbitrary extensions for declaration file mapping
            var extension = GetLongestExtensionFromPath(candidate, r._resolver.ExtraExtensions, false);
            if (extension == "")
            {
                extension = candidate[candidate.LastIndexOf('.')..];
            }

            extensionless = RemoveExtension(candidate, extension);
        }

        var resolvedExtension = candidate[extensionless.Length..];
        r._tracer?.Write(File_name_0_has_a_1_extension_stripping_it, candidate, resolvedExtension);
        return r.TryAddingExtensions(extensionless, extensions, resolvedExtension);
    }

    /// <summary>Go 的 <c>(*resolutionState).tryAddingExtensions</c>。</summary>
    internal static Resolved? TryAddingExtensions(this ResolutionState r, string extensionless, Extensions extensions, string originalExtension)
    {
        var directory = GetDirectoryPath(extensionless);
        if (directory != "" && !r._resolver.Host.FS().DirectoryExists(directory))
        {
            return null;
        }

        switch (originalExtension)
        {
            case ExtensionMjs:
            case ExtensionMts:
            case ExtensionDmts:
                if ((extensions & Extensions.TypeScript) != 0)
                {
                    if (r.TryExtension(ExtensionMts, extensionless, originalExtension == ExtensionMts || originalExtension == ExtensionDmts) is { } mtsResolved)
                    {
                        return mtsResolved;
                    }
                }

                if ((extensions & Extensions.Declaration) != 0)
                {
                    if (r.TryExtension(ExtensionDmts, extensionless, originalExtension == ExtensionMts || originalExtension == ExtensionDmts) is { } dmtsResolved)
                    {
                        return dmtsResolved;
                    }
                }

                if ((extensions & Extensions.JavaScript) != 0)
                {
                    if (r.TryExtension(ExtensionMjs, extensionless, false) is { } mjsResolved)
                    {
                        return mjsResolved;
                    }
                }

                return null;
            case ExtensionCjs:
            case ExtensionCts:
            case ExtensionDcts:
                if ((extensions & Extensions.TypeScript) != 0)
                {
                    if (r.TryExtension(ExtensionCts, extensionless, originalExtension == ExtensionCts || originalExtension == ExtensionDcts) is { } ctsResolved)
                    {
                        return ctsResolved;
                    }
                }

                if ((extensions & Extensions.Declaration) != 0)
                {
                    if (r.TryExtension(ExtensionDcts, extensionless, originalExtension == ExtensionCts || originalExtension == ExtensionDcts) is { } dctsResolved)
                    {
                        return dctsResolved;
                    }
                }

                if ((extensions & Extensions.JavaScript) != 0)
                {
                    if (r.TryExtension(ExtensionCjs, extensionless, false) is { } cjsResolved)
                    {
                        return cjsResolved;
                    }
                }

                return null;
            case ExtensionJson:
                if ((extensions & Extensions.Declaration) != 0)
                {
                    if (r.TryExtension(".d.json.ts", extensionless, false) is { } dJsonResolved)
                    {
                        return dJsonResolved;
                    }
                }

                if ((extensions & Extensions.Json) != 0)
                {
                    if (r.TryExtension(ExtensionJson, extensionless, false) is { } jsonResolved)
                    {
                        return jsonResolved;
                    }
                }

                return null;
            case ExtensionTsx:
            case ExtensionJsx:
                // basically idendical to the ts/js case below, but prefers matching tsx and jsx files exactly before falling back to the ts or js file path
                // (historically, we disallow having both a a.ts and a.tsx file in the same compilation, since their outputs clash)
                // TODO: We should probably error if `"./a.tsx"` resolved to `"./a.ts"`, right?
                if ((extensions & Extensions.TypeScript) != 0)
                {
                    if (r.TryExtension(ExtensionTsx, extensionless, originalExtension == ExtensionTsx) is { } tsxResolved)
                    {
                        return tsxResolved;
                    }

                    if (r.TryExtension(ExtensionTs, extensionless, originalExtension == ExtensionTsx) is { } tsResolved)
                    {
                        return tsResolved;
                    }
                }

                if ((extensions & Extensions.Declaration) != 0)
                {
                    if (r.TryExtension(ExtensionDts, extensionless, originalExtension == ExtensionTsx) is { } dtsResolved)
                    {
                        return dtsResolved;
                    }
                }

                if ((extensions & Extensions.JavaScript) != 0)
                {
                    if (r.TryExtension(ExtensionJsx, extensionless, false) is { } jsxResolved)
                    {
                        return jsxResolved;
                    }

                    if (r.TryExtension(ExtensionJs, extensionless, false) is { } jsResolved)
                    {
                        return jsResolved;
                    }
                }

                return null;
            case ExtensionTs:
            case ExtensionDts:
            case ExtensionJs:
            case "":
                if ((extensions & Extensions.TypeScript) != 0)
                {
                    if (r.TryExtension(ExtensionTs, extensionless, originalExtension == ExtensionTs || originalExtension == ExtensionDts) is { } tsCaseResolved)
                    {
                        return tsCaseResolved;
                    }

                    if (r.TryExtension(ExtensionTsx, extensionless, originalExtension == ExtensionTs || originalExtension == ExtensionDts) is { } tsxCaseResolved)
                    {
                        return tsxCaseResolved;
                    }
                }

                if ((extensions & Extensions.Declaration) != 0)
                {
                    if (r.TryExtension(ExtensionDts, extensionless, originalExtension == ExtensionTs || originalExtension == ExtensionDts) is { } dtsCaseResolved)
                    {
                        return dtsCaseResolved;
                    }
                }

                if ((extensions & Extensions.JavaScript) != 0)
                {
                    if (r.TryExtension(ExtensionJs, extensionless, false) is { } jsCaseResolved)
                    {
                        return jsCaseResolved;
                    }

                    if (r.TryExtension(ExtensionJsx, extensionless, false) is { } jsxCaseResolved)
                    {
                        return jsxCaseResolved;
                    }
                }

                if (r._isConfigLookup)
                {
                    if (r.TryExtension(ExtensionJson, extensionless, false) is { } configJsonResolved)
                    {
                        return configJsonResolved;
                    }
                }

                return null;
            default:
                if (System.Array.IndexOf(r._resolver.ExtraExtensions, originalExtension) >= 0)
                {
                    // A fully specified import of an extraExtension resolves directly to the file.
                    if (r.TryExtension(originalExtension, extensionless, false) is { } extraResolved)
                    {
                        extraResolved.ResolvedUsingExtraExtensions = true;
                        return extraResolved;
                    }
                }

                if ((extensions & Extensions.Declaration) != 0 && !IsDeclarationFileName(extensionless + originalExtension))
                {
                    if (r.TryExtension(".d" + originalExtension + ".ts", extensionless, false) is { } dResolved)
                    {
                        return dResolved;
                    }
                }

                return null;
        }
    }

    /// <summary>Go 的 <c>(*resolutionState).tryExtension</c>。</summary>
    internal static Resolved? TryExtension(this ResolutionState r, string extension, string extensionless, bool resolvedUsingTsExtension)
    {
        var fileName = extensionless + extension;
        if (r.TryFile(fileName, out var path))
        {
            return new Resolved
            {
                Path = path,
                Extension = extension,
                ResolvedUsingTsExtension = !r._candidateEndingIsFromConfig && resolvedUsingTsExtension,
            };
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).tryFile</c>。</summary>
    internal static bool TryFile(this ResolutionState r, string fileName, out string path)
    {
        if (r._compilerOptions.ModuleSuffixes is not { Length: > 0 } moduleSuffixes)
        {
            path = fileName;
            return r.TryFileLookup(fileName);
        }

        var ext = TryGetExtensionFromPath(fileName);
        var fileNameNoExtension = RemoveExtension(fileName, ext);
        foreach (var suffix in moduleSuffixes)
        {
            var suffixPath = fileNameNoExtension + suffix + ext;
            if (r.TryFileLookup(suffixPath))
            {
                path = suffixPath;
                return true;
            }
        }

        path = fileName;
        return false;
    }

    /// <summary>Go 的 <c>(*resolutionState).tryFileLookup</c>。</summary>
    internal static bool TryFileLookup(this ResolutionState r, string fileName)
    {
        if (r._resolver.Host.FS().FileExists(fileName))
        {
            r._tracer?.Write(File_0_exists_use_it_as_a_name_resolution_result, fileName);
            return true;
        }

        r._tracer?.Write(File_0_does_not_exist, fileName);
        return false;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadNodeModuleFromDirectory</c>。</summary>
    internal static Resolved? LoadNodeModuleFromDirectory(this ResolutionState r, Extensions extensions, string candidate, bool considerPackageJson)
    {
        InfoCacheEntry? packageInfo = null;
        if (considerPackageJson)
        {
            packageInfo = r.GetPackageJsonInfo(candidate);
        }

        return r.LoadNodeModuleFromDirectoryWorker(extensions, candidate, packageInfo);
    }

    /// <summary>Go 的 <c>(*resolutionState).loadNodeModuleFromDirectoryWorker</c>。</summary>
    internal static Resolved? LoadNodeModuleFromDirectoryWorker(this ResolutionState r, Extensions ext, string candidate, InfoCacheEntry? packageInfo)
    {
        var packageFile = "";
        var versionPaths = new VersionPaths();
        if (packageInfo is { } info && info.GetContents() is { } infoContents)
        {
            versionPaths = infoContents.GetVersionPaths(r.GetTraceFunc());
            if (ComparePaths(candidate, info.PackageDirectory, new ComparePathsOptions(r._resolver.Host.FS().UseCaseSensitiveFileNames())) == 0)
            {
                if (r.GetPackageFile(ext, info, out var file))
                {
                    packageFile = file;
                }
            }
        }

        ResolutionKindSpecificLoader loader = (extensions, candidate) =>
        {
            if (r.LoadFileNameFromPackageJSONField(extensions, candidate, packageFile) is { } fromFile)
            {
                return fromFile;
            }

            // Even if `extensions == extensionsDeclaration`, we can still look up a .ts file as a result of package.json "types"
            // !!! should we not set this before the filename lookup above?
            var expandedExtensions = extensions;
            if (extensions == Extensions.Declaration)
            {
                expandedExtensions = Extensions.TypeScript | Extensions.Declaration;
            }

            // Disable `esmMode` for the resolution of the package path for CJS-mode packages (so the `main` field can omit extensions)
            var saveESMMode = r._esmMode;
            var saveCandidateEndingIsFromConfig = r._candidateEndingIsFromConfig;
            r._candidateEndingIsFromConfig = true;
            if (packageInfo is { } loaderInfo && loaderInfo.GetContents() is { } loaderContents && loaderContents.HeaderFields.Type.Value != "module")
            {
                r._esmMode = false;
            }

            var result = r.NodeLoadModuleByRelativeName(expandedExtensions, candidate, false /*considerPackageJson*/);
            r._esmMode = saveESMMode;
            r._candidateEndingIsFromConfig = saveCandidateEndingIsFromConfig;
            return result;
        };

        string indexPath;
        if (r._isConfigLookup)
        {
            indexPath = CombinePaths(candidate, "tsconfig");
        }
        else
        {
            indexPath = CombinePaths(candidate, "index");
        }

        if (versionPaths.Exists() && (packageFile == "" || ContainsPath(candidate, packageFile, new ComparePathsOptions())))
        {
            string moduleName;
            if (packageFile != "")
            {
                moduleName = GetRelativePathFromDirectory(candidate, packageFile, new ComparePathsOptions());
            }
            else
            {
                moduleName = GetRelativePathFromDirectory(candidate, indexPath, new ComparePathsOptions());
            }

            r._tracer?.Write(X_package_json_has_a_typesVersions_entry_0_that_matches_compiler_version_1_looking_for_a_pattern_to_match_module_name_2, versionPaths.Version, Version(), moduleName);
            var pathPatterns = TryParsePatterns(versionPaths.GetPaths() ?? new OrderedMap<string, string[]>());
            if (r.TryLoadModuleUsingPaths(ext, moduleName, candidate, versionPaths.GetPaths() ?? new OrderedMap<string, string[]>(), pathPatterns, loader) is { } result)
            {
                if (result.PackageId.Name != "")
                {
                    // !!! are these asserts really necessary?
                    throw new DebugFailureException("expected packageId to be empty");
                }

                return result;
            }
        }

        if (packageFile != "")
        {
            if (loader(ext, packageFile) is { } packageFileResult)
            {
                if (packageFileResult.PackageId.Name != "")
                {
                    // !!! are these asserts really necessary?
                    throw new DebugFailureException("expected packageId to be empty");
                }

                return packageFileResult;
            }
        }

        // ESM mode resolutions don't do package 'index' lookups
        if (!r._esmMode)
        {
            if (!r._resolver.Host.FS().DirectoryExists(candidate))
            {
                return null;
            }

            return r.LoadModuleFromFile(ext, indexPath);
        }

        return null;
    }
}
