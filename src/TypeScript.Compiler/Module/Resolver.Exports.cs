// ported from: internal/module/resolver.go:546-1119（node-like 主流程尾段：self-name / imports / exports /
// 条件 target；package.json outDir 输入文件回映射；node_modules 目录级加载）
//
// 与 Go 的偏离（同 Resolver.cs 文件头；另外这几条是本文件特有的）：
// - Go 的 `defer func(){ r.name, r.containingDirectory = … }()` 写在函数中段但作用于**整个函数**返回 →
//   C# 用 try/finally 包住 defer 之后的语句（这两条路径都 return，语义完全等价）。
// - Go 的提升字段访问 `Contents.Exports`（`Fields` 内嵌 `PathFields`）→ C# 无法提升，写
//   `contents.PathFields.Exports`（见 PackageJson/ 文件头）。`x.Contents.Foo` 一律先取
//   `var contents = x.GetContents()`（Go 的 `x.Exists()` = `Contents != nil`，C# 里用模式匹配表达）。
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
    /// <summary>Go 的 <c>(*resolutionState).resolveNodeLikeWorker</c>（resolver.go:546-568）。</summary>
    internal static ResolvedModule ResolveNodeLikeWorker(this ResolutionState r)
    {
        if (r.TryLoadModuleUsingOptionalResolutionSettings() is { } optionalSettingsResolved)
        {
            return r.CreateResolvedModuleHandlingSymlink(optionalSettingsResolved);
        }

        if (!IsExternalModuleNameRelative(r._name))
        {
            if ((r._features & NodeResolutionFeatures.Imports) != 0 && r._name.StartsWith('#'))
            {
                if (r.LoadModuleFromImports() is { } importsResolved)
                {
                    return r.CreateResolvedModuleHandlingSymlink(importsResolved);
                }
            }

            if ((r._features & NodeResolutionFeatures.SelfName) != 0)
            {
                if (r.LoadModuleFromSelfNameReference() is { } selfNameResolved)
                {
                    return r.CreateResolvedModuleHandlingSymlink(selfNameResolved);
                }
            }

            if (r._name.Contains(':'))
            {
                r._tracer?.Write(Skipping_module_0_that_looks_like_an_absolute_URI_target_file_types_Colon_1, r._name, r._extensions.String());
                return r.CreateResolvedModule(null, false);
            }

            r._tracer?.Write(Loading_module_0_from_node_modules_folder_target_file_types_Colon_1, r._name, r._extensions.String());
            if (r.LoadModuleFromNearestNodeModulesDirectory(false /*typesScopeOnly*/) is { } nodeModulesResolved)
            {
                return r.CreateResolvedModuleHandlingSymlink(nodeModulesResolved);
            }

            if ((r._extensions & Extensions.Declaration) != 0)
            {
                if (r.ResolveFromTypeRoot() is { } typeRootResolved)
                {
                    return r.CreateResolvedModuleHandlingSymlink(typeRootResolved);
                }
            }
        }
        else
        {
            var candidate = NormalizePathForCJSResolution(r._containingDirectory, r._name);
            var resolved = r.NodeLoadModuleByRelativeName(r._extensions, candidate, true);
            return r.CreateResolvedModule(
                resolved,
                resolved is not null && resolved.Path.Contains("/node_modules/", StringComparison.Ordinal));
        }

        return r.CreateResolvedModule(null, false);
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromSelfNameReference</c>。</summary>
    internal static Resolved? LoadModuleFromSelfNameReference(this ResolutionState r)
    {
        var directoryPath = GetNormalizedAbsolutePath(r._containingDirectory, r._resolver.Host.GetCurrentDirectory());
        var scope = r.GetPackageScopeForPath(directoryPath);
        var scopeContents = scope?.GetContents();
        if (scopeContents is null || scopeContents.PathFields.Exports.IsFalsy())
        {
            // !!! falsy check seems wrong?
            return null;
        }

        if (!scopeContents.HeaderFields.Name.TryGetValue(out var name))
        {
            return null;
        }

        var parts = GetPathComponents(r._name, "");
        var nameParts = GetPathComponents(name, "");
        if (parts.Length < nameParts.Length || !nameParts.AsSpan().SequenceEqual(parts.AsSpan(0, nameParts.Length)))
        {
            return null;
        }

        var trailingParts = parts[nameParts.Length..];
        string subpath;
        if (trailingParts.Length > 0)
        {
            subpath = CombinePaths(".", trailingParts);
        }
        else
        {
            subpath = ".";
        }

        // Maybe TODO: splitting extensions into two priorities should be unnecessary, except
        // https://github.com/microsoft/TypeScript/issues/50762 makes the behavior different.
        // As long as that bug exists, we need to do two passes here in self-name loading
        // in order to be consistent with (non-self) library-name loading in
        // `loadModuleFromNearestNodeModulesDirectoryWorker`, which uses two passes in order
        // to prioritize `@types` packages higher up the directory tree over untyped
        // implementation packages. See the selfNameModuleAugmentation.ts test for why this
        // matters.
        //
        // However, there's an exception. If the user has `allowJs` and `declaration`, we need
        // to ensure that self-name imports of their own package can resolve back to their
        // input JS files via `tryLoadInputFileForPath` at a higher priority than their output
        // declaration files, so we need to do a single pass with all extensions for that case.
        // Go: scope := r.getPackageScopeForPath(directoryPath); 上面的 `scopeContents is null` 分支同时覆盖
        // `scope == nil`（C# 用 `scope?.GetContents()` 表达）与 `scope.Contents == nil` 两种情况，所以走到这里
        // scope 必然非空；Go 侧后续同样直接解引用（nil 即 panic），故用 `!`。
        if (r._compilerOptions.GetAllowJS() && !r._containingDirectory.Contains("/node_modules/", StringComparison.Ordinal))
        {
            return r.LoadModuleFromExports(scope!, r._extensions, subpath);
        }

        var priorityExtensions = r._extensions & (Extensions.TypeScript | Extensions.Declaration);
        var secondaryExtensions = r._extensions & ~(Extensions.TypeScript | Extensions.Declaration);
        if (r.LoadModuleFromExports(scope!, priorityExtensions, subpath) is { } priorityResolved)
        {
            return priorityResolved;
        }

        return r.LoadModuleFromExports(scope!, secondaryExtensions, subpath);
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromImports</c>。</summary>
    internal static Resolved? LoadModuleFromImports(this ResolutionState r)
    {
        if (r._name == "#" || (r._name.StartsWith("#/", StringComparison.Ordinal) && (r._features & NodeResolutionFeatures.ImportsPatternRoot) == 0))
        {
            r._tracer?.Write(Invalid_import_specifier_0_has_no_possible_resolutions, r._name);
            return null;
        }

        var directoryPath = GetNormalizedAbsolutePath(r._containingDirectory, r._resolver.Host.GetCurrentDirectory());
        var scope = r.GetPackageScopeForPath(directoryPath);
        if (scope is null)
        {
            r._tracer?.Write(Directory_0_has_no_containing_package_json_scope_Imports_will_not_resolve, directoryPath);
            return null;
        }

        var contents = scope.GetContents();
        if (contents is null || contents.PathFields.Imports.Type != JSONValueType.Object)
        {
            // !!! Old compiler only checks for undefined, but then assumes `imports` is an object if present.
            // Maybe should have a new diagnostic for imports of an invalid type. Also, array should be handled?
            r._tracer?.Write(X_package_json_scope_0_has_no_imports_defined, scope.PackageDirectory);
            return null;
        }

        // 上面的守卫已确认 `contents.PathFields.Imports.Type == Object`；Go 的 AsObject() 在此必然返回非 nil（nil 即 panic）。
        if (r.LoadModuleFromExportsOrImports(r._extensions, r._name, contents.PathFields.Imports.AsObject()!, scope /*isImports*/, true) is { } result)
        {
            return result;
        }

        r._tracer?.Write(Import_specifier_0_does_not_exist_in_package_json_scope_at_path_1, r._name, scope.PackageDirectory);
        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromExports</c>。</summary>
    internal static Resolved? LoadModuleFromExports(this ResolutionState r, InfoCacheEntry packageInfo, Extensions ext, string subpath)
    {
        // !!! This is ported exactly, but the falsy check seems wrong
        var packageJson = packageInfo.GetContents();
        if (packageJson is null || packageJson.PathFields.Exports.IsFalsy())
        {
            return null;
        }

        var exports = packageJson.PathFields.Exports;
        if (subpath == ".")
        {
            ExportsOrImports? mainExport = null;
            switch (exports.Type)
            {
                case JSONValueType.String:
                case JSONValueType.Array:
                    mainExport = exports;
                    break;
                case JSONValueType.Object:
                    if (exports.IsConditions())
                    {
                        mainExport = exports;
                    }
                    // case JSONValueType.Object：Go 的 AsObject()（nil 即 panic），故 `!`。
                    else if (exports.AsObject()!.Get(".").Value is { } dot)
                    {
                        mainExport = dot;
                    }

                    break;
            }

            if (mainExport is not null && mainExport.Type != JSONValueType.NotPresent)
            {
                return r.LoadModuleFromTargetExportOrImport(ext, subpath, packageInfo, false /*isImports*/, mainExport, "", false /*isPattern*/, ".");
            }
        }
        else if (exports.Type == JSONValueType.Object && exports.IsSubpaths())
        {
            // 上面的 `exports.Type == Object && exports.IsSubpaths()` 已确定是对象；Go 同点直接解引用（nil 即 panic）。
            if (r.LoadModuleFromExportsOrImports(ext, subpath, exports.AsObject()!, packageInfo, false /*isImports*/) is { } subpathResult)
            {
                return subpathResult;
            }
        }

        r._tracer?.Write(Export_specifier_0_does_not_exist_in_package_json_scope_at_path_1, subpath, packageInfo.PackageDirectory);
        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromExportsOrImports</c>。</summary>
    internal static Resolved? LoadModuleFromExportsOrImports(
        this ResolutionState r,
        Extensions extensions,
        string moduleName,
        OrderedMap<string, ExportsOrImports> lookupTable,
        InfoCacheEntry scope,
        bool isImports)
    {
        if (!moduleName.EndsWith('/') && !moduleName.Contains('*'))
        {
            var (target, found) = lookupTable.Get(moduleName);
            if (found && target is not null)
            {
                return r.LoadModuleFromTargetExportOrImport(extensions, moduleName, scope, isImports, target, "", false /*isPattern*/, moduleName);
            }
        }

        var expandingKeys = new List<string>(lookupTable.Size());
        foreach (var key in lookupTable.Keys())
        {
            if (key.AsSpan().Count('*') == 1 || key.EndsWith('/'))
            {
                expandingKeys.Add(key);
            }
        }

        expandingKeys.Sort(ComparePatternKeys);

        foreach (var potentialTarget in expandingKeys)
        {
            if ((r._features & NodeResolutionFeatures.ExportsPatternTrailers) != 0 && MatchesPatternWithTrailer(potentialTarget, moduleName))
            {
                var (target, targetFound) = lookupTable.Get(potentialTarget);
                if (targetFound && target is not null)
                {
                    var starPos = potentialTarget.IndexOf('*');
                    var subpath = moduleName[starPos..(moduleName.Length - (potentialTarget.Length - 1 - starPos))];
                    return r.LoadModuleFromTargetExportOrImport(extensions, moduleName, scope, isImports, target, subpath, true, potentialTarget);
                }
            }
            else if (potentialTarget.EndsWith('*') && moduleName.StartsWith(potentialTarget.AsSpan(0, potentialTarget.Length - 1), StringComparison.Ordinal))
            {
                var (target, targetFound) = lookupTable.Get(potentialTarget);
                if (targetFound && target is not null)
                {
                    var subpath = moduleName[(potentialTarget.Length - 1)..];
                    return r.LoadModuleFromTargetExportOrImport(extensions, moduleName, scope, isImports, target, subpath, true, potentialTarget);
                }
            }
            else if (moduleName.StartsWith(potentialTarget, StringComparison.Ordinal))
            {
                var (target, targetFound) = lookupTable.Get(potentialTarget);
                if (targetFound && target is not null)
                {
                    var subpath = moduleName[potentialTarget.Length..];
                    return r.LoadModuleFromTargetExportOrImport(extensions, moduleName, scope, isImports, target, subpath, false, potentialTarget);
                }
            }
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromTargetExportOrImport</c>。</summary>
    internal static Resolved? LoadModuleFromTargetExportOrImport(
        this ResolutionState r,
        Extensions extensions,
        string moduleName,
        InfoCacheEntry scope,
        bool isImports,
        ExportsOrImports target,
        string subpath,
        bool isPattern,
        string key)
    {
        switch (target.Type)
        {
            case JSONValueType.String:
                var targetString = target.AsString();
                if (!isPattern && subpath.Length > 0 && !targetString.EndsWith('/'))
                {
                    r._tracer?.Write(X_package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    return null;
                }

                if (!targetString.StartsWith("./", StringComparison.Ordinal))
                {
                    if (isImports && !targetString.StartsWith("../", StringComparison.Ordinal) && !targetString.StartsWith('/') && !IsRootedDiskPath(targetString))
                    {
                        var combinedLookup = targetString + subpath;
                        if (isPattern)
                        {
                            combinedLookup = targetString.Replace("*", subpath, StringComparison.Ordinal);
                        }

                        var scopeContainingDirectory = EnsureTrailingDirectorySeparator(scope.PackageDirectory);
                        r._tracer?.Write(Using_0_subpath_1_with_target_2, "imports", key, combinedLookup);
                        r._tracer?.Write(Resolving_module_0_from_1, combinedLookup, scopeContainingDirectory);
                        var name = r._name;
                        var containingDirectory = r._containingDirectory;
                        r._name = combinedLookup;
                        r._containingDirectory = scopeContainingDirectory;
                        try
                        {
                            // 注：C# 的 switch 各 section 共享同一个声明空间，这里不能叫 result（下面 case 里已有 result）
                            var combinedResult = r.ResolveNodeLike();
                            if (combinedResult.IsResolved())
                            {
                                return new Resolved
                                {
                                    Path = combinedResult.ResolvedFileName,
                                    Extension = combinedResult.Extension,
                                    PackageId = combinedResult.PackageId,
                                    OriginalPath = combinedResult.OriginalPath,
                                    ResolvedUsingTsExtension = combinedResult.ResolvedUsingTsExtension,
                                };
                            }

                            return null;
                        }
                        finally
                        {
                            r._name = name;
                            r._containingDirectory = containingDirectory;
                        }
                    }

                    r._tracer?.Write(X_package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    return null;
                }

                string[] parts;
                if (PathIsRelative(targetString))
                {
                    parts = GetPathComponents(targetString, "")[1..];
                }
                else
                {
                    parts = GetPathComponents(targetString, "");
                }

                var partsAfterFirst = parts[1..];
                if (System.Array.IndexOf(partsAfterFirst, "..") >= 0 || System.Array.IndexOf(partsAfterFirst, ".") >= 0 || System.Array.IndexOf(partsAfterFirst, "node_modules") >= 0)
                {
                    r._tracer?.Write(X_package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    return null;
                }

                var resolvedTarget = CombinePaths(scope.PackageDirectory, targetString);
                // TODO: Assert that `resolvedTarget` is actually within the package directory? That's what the spec says.... but I'm not sure we need
                // to be in the business of validating everyone's import and export map correctness.
                var subpathParts = GetPathComponents(subpath, "");
                if (System.Array.IndexOf(subpathParts, "..") >= 0 || System.Array.IndexOf(subpathParts, ".") >= 0 || System.Array.IndexOf(subpathParts, "node_modules") >= 0)
                {
                    r._tracer?.Write(X_package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    return null;
                }

                if (r._tracer is not null)
                {
                    string messageTarget;
                    if (isPattern)
                    {
                        messageTarget = targetString.Replace("*", subpath, StringComparison.Ordinal);
                    }
                    else
                    {
                        messageTarget = targetString + subpath;
                    }

                    r._tracer.Write(Using_0_subpath_1_with_target_2, IfElse(isImports, "imports", "exports"), key, messageTarget);
                }

                string finalPath;
                if (isPattern)
                {
                    finalPath = GetNormalizedAbsolutePath(resolvedTarget.Replace("*", subpath, StringComparison.Ordinal), r._resolver.Host.GetCurrentDirectory());
                }
                else
                {
                    finalPath = GetNormalizedAbsolutePath(resolvedTarget + subpath, r._resolver.Host.GetCurrentDirectory());
                }

                if (r.TryLoadInputFileForPath(finalPath, subpath, CombinePaths(scope.PackageDirectory, "package.json"), isImports) is { } inputLink)
                {
                    inputLink.PackageId = r.GetPackageId(inputLink.Path, scope);
                    return inputLink;
                }

                if (r.LoadFileNameFromPackageJSONField(extensions, finalPath, targetString) is { } result)
                {
                    result.PackageId = r.GetPackageId(result.Path, scope);
                    return result;
                }

                return null;

            case JSONValueType.Object:
                r._tracer?.Write(Entering_conditional_exports);
                // case JSONValueType.Object：Go 的 AsObject()（nil 即 panic），故 `!`。
                foreach (var condition in target.AsObject()!.Keys())
                {
                    if (r.ConditionMatches(condition))
                    {
                        r._tracer?.Write(Matched_0_condition_1, IfElse(isImports, "imports", "exports"), condition);
                        var (subTarget, subTargetFound) = target.AsObject()!.Get(condition);
                        if (subTargetFound && subTarget is not null)
                        {
                            var conditionResult = r.LoadModuleFromTargetExportOrImport(extensions, moduleName, scope, isImports, subTarget, subpath, isPattern, key);
                            if (conditionResult is not null)
                            {
                                if (conditionResult.IsResolved())
                                {
                                    r._tracer?.Write(Resolved_under_condition_0, condition);
                                }

                                r._tracer?.Write(Exiting_conditional_exports);
                                return conditionResult;
                            }

                            r._tracer?.Write(Failed_to_resolve_under_condition_0, condition);
                        }
                    }
                    else
                    {
                        r._tracer?.Write(Saw_non_matching_condition_0, condition);
                    }
                }

                r._tracer?.Write(Exiting_conditional_exports);
                return null;
            case JSONValueType.Array:
                if (target.AsArray().Length == 0)
                {
                    r._tracer?.Write(X_package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
                    return null;
                }

                foreach (var elem in target.AsArray())
                {
                    if (r.LoadModuleFromTargetExportOrImport(extensions, moduleName, scope, isImports, elem, subpath, isPattern, key) is { } elementResult)
                    {
                        return elementResult;
                    }
                }

                break;

            case JSONValueType.Null:
                r._tracer?.Write(X_package_json_scope_0_explicitly_maps_specifier_1_to_null, scope.PackageDirectory, moduleName);
                return new Resolved();
        }

        r._tracer?.Write(X_package_json_scope_0_has_invalid_type_for_target_of_specifier_1, scope.PackageDirectory, moduleName);
        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).tryLoadInputFileForPath</c>。</summary>
    internal static Resolved? TryLoadInputFileForPath(this ResolutionState r, string finalPath, string entry, string packagePath, bool isImports)
    {
        // Replace any references to outputs for files in the program with the input files to support package self-names used with outDir
        if (!r._isConfigLookup &&
            (r._compilerOptions.DeclarationDir != "" || r._compilerOptions.OutDir != "") &&
            !finalPath.Contains("/node_modules/", StringComparison.Ordinal) &&
            (r._compilerOptions.ConfigFilePath == "" || ContainsPath(
                GetDirectoryPath(packagePath),
                r._compilerOptions.ConfigFilePath,
                new ComparePathsOptions(r._resolver.Host.FS().UseCaseSensitiveFileNames(), r._resolver.Host.GetCurrentDirectory()))))
        {
            // Note: this differs from Strada's tryLoadInputFileForPath in that it
            // does not attempt to perform "guesses", instead requring a clear root indicator.
            string rootDir;
            if (r._compilerOptions.RootDir != "")
            {
                // A `rootDir` compiler option strongly indicates the root location
                rootDir = r._compilerOptions.RootDir;
            }
            else if (r._compilerOptions.ConfigFilePath != "")
            {
                // When no explicit rootDir is set, treat the config file's directory as the project root, which establishes the common source directory, so no other locations need to be checked.
                rootDir = GetDirectoryPath(r._compilerOptions.ConfigFilePath);
            }
            else
            {
                var diagnostic = Ast.Ast.NewDiagnostic(
                    null,
                    new TextRange(0, 0),
                    IfElse(
                        isImports,
                        The_project_root_is_ambiguous_but_is_required_to_resolve_import_map_entry_0_in_file_1_Supply_the_rootDir_compiler_option_to_disambiguate,
                        The_project_root_is_ambiguous_but_is_required_to_resolve_export_map_entry_0_in_file_1_Supply_the_rootDir_compiler_option_to_disambiguate),
                    IfElse(entry == "", ".", entry), // replace empty string with `.` - the reverse of the operation done when entries are built - so main entrypoint errors don't look weird
                    packagePath);
                r._diagnostics.Add(diagnostic);
                return new Resolved();
            }

            var candidateDirectories = r.GetOutputDirectoriesForBaseDirectory(rootDir);
            foreach (var candidateDir in candidateDirectories)
            {
                if (ContainsPath(candidateDir, finalPath, new ComparePathsOptions(r._resolver.Host.FS().UseCaseSensitiveFileNames(), r._resolver.Host.GetCurrentDirectory())))
                {
                    // The matched export is looking up something in either the out declaration or js dir, now map the written path back into the source dir and source extension
                    var pathFragment = "";
                    if (finalPath.Length > candidateDir.Length)
                    {
                        pathFragment = finalPath[(candidateDir.Length + 1)..]; // +1 to also remove directory separator
                    }

                    var possibleInputBase = CombinePaths(rootDir, pathFragment);
                    string[] jsAndDtsExtensions = [ExtensionMjs, ExtensionCjs, ExtensionJs, ExtensionJson, ExtensionDmts, ExtensionDcts, ExtensionDts];
                    foreach (var ext in jsAndDtsExtensions)
                    {
                        if (FileExtensionIs(possibleInputBase, ext))
                        {
                            var inputExts = GetPossibleOriginalInputExtensionForExtension(possibleInputBase);
                            foreach (var possibleExt in inputExts)
                            {
                                if (!ExtensionIsOk(r._extensions, possibleExt))
                                {
                                    continue;
                                }

                                var possibleInputWithInputExtension = ChangeExtension(possibleInputBase, possibleExt);
                                if (r._resolver.Host.FS().FileExists(possibleInputWithInputExtension))
                                {
                                    var resolved = r.LoadFileNameFromPackageJSONField(r._extensions, possibleInputWithInputExtension, "");
                                    if (resolved is not null)
                                    {
                                        return resolved;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).getOutputDirectoriesForBaseDirectory</c>。</summary>
    internal static string[] GetOutputDirectoriesForBaseDirectory(this ResolutionState r, string commonSourceDirGuess)
    {
        // Config file output paths are processed to be relative to the host's current directory, while
        // otherwise the paths are resolved relative to the common source dir the compiler puts together
        var currentDir = IfElse(r._compilerOptions.ConfigFilePath != "", r._resolver.Host.GetCurrentDirectory(), commonSourceDirGuess);
        var candidateDirectories = new List<string>();
        if (r._compilerOptions.DeclarationDir != "")
        {
            candidateDirectories.Add(GetNormalizedAbsolutePath(CombinePaths(currentDir, r._compilerOptions.DeclarationDir), r._resolver.Host.GetCurrentDirectory()));
        }

        if (r._compilerOptions.OutDir != "" && r._compilerOptions.OutDir != r._compilerOptions.DeclarationDir)
        {
            candidateDirectories.Add(GetNormalizedAbsolutePath(CombinePaths(currentDir, r._compilerOptions.OutDir), r._resolver.Host.GetCurrentDirectory()));
        }

        return [.. candidateDirectories];
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromNearestNodeModulesDirectory</c>。</summary>
    internal static Resolved? LoadModuleFromNearestNodeModulesDirectory(this ResolutionState r, bool typesScopeOnly)
    {
        var mode = ResolutionModeCommonJS;
        if (r._esmMode || r.ConditionMatches("import"))
        {
            mode = ResolutionModeESM;
        }

        // Do (up to) two passes through node_modules:
        //   1. For each ancestor node_modules directory, try to find:
        //      i.  TS/DTS files in the implementation package
        //      ii. DTS files in the @types package
        //   2. For each ancestor node_modules directory, try to find:
        //      i.  JS files in the implementation package
        var priorityExtensions = r._extensions & (Extensions.TypeScript | Extensions.Declaration);
        var secondaryExtensions = r._extensions & ~(Extensions.TypeScript | Extensions.Declaration);
        // (1)
        if (priorityExtensions != 0)
        {
            r._tracer?.Write(Searching_all_ancestor_node_modules_directories_for_preferred_extensions_Colon_0, priorityExtensions.String());
            if (r.LoadModuleFromNearestNodeModulesDirectoryWorker(priorityExtensions, mode, typesScopeOnly) is { } priorityResult)
            {
                return priorityResult;
            }
        }

        // (2)
        if (secondaryExtensions != 0 && !typesScopeOnly)
        {
            r._tracer?.Write(Searching_all_ancestor_node_modules_directories_for_fallback_extensions_Colon_0, secondaryExtensions.String());
            return r.LoadModuleFromNearestNodeModulesDirectoryWorker(secondaryExtensions, mode, typesScopeOnly);
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromNearestNodeModulesDirectoryWorker</c>。</summary>
    internal static Resolved? LoadModuleFromNearestNodeModulesDirectoryWorker(this ResolutionState r, Extensions ext, ModuleKind mode, bool typesScopeOnly)
    {
        ForEachAncestorDirectory(
            r._containingDirectory,
            directory =>
            {
                // !!! stop at global cache
                if (GetBaseFileName(directory) != "node_modules")
                {
                    var result = r.LoadModuleFromImmediateNodeModulesDirectory(ext, directory, typesScopeOnly);
                    return (result, result is not null);
                }

                return ((Resolved?)null, false);
            },
            out var result);

        return result;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromImmediateNodeModulesDirectory</c>。</summary>
    internal static Resolved? LoadModuleFromImmediateNodeModulesDirectory(this ResolutionState r, Extensions extensions, string directory, bool typesScopeOnly)
    {
        var nodeModulesFolder = CombinePaths(directory, "node_modules");
        if (!r._resolver.Host.FS().DirectoryExists(nodeModulesFolder))
        {
            r._tracer?.Write(Directory_0_does_not_exist_skipping_all_lookups_in_it, nodeModulesFolder);
            return null;
        }

        if (!typesScopeOnly)
        {
            if (r.LoadModuleFromSpecificNodeModulesDirectory(extensions, r._name, nodeModulesFolder) is { } packageResult)
            {
                return packageResult;
            }
        }

        if ((extensions & Extensions.Declaration) != 0)
        {
            var nodeModulesAtTypes = CombinePaths(nodeModulesFolder, "@types");
            if (!r._resolver.Host.FS().DirectoryExists(nodeModulesAtTypes))
            {
                r._tracer?.Write(Directory_0_does_not_exist_skipping_all_lookups_in_it, nodeModulesAtTypes);
                return null;
            }

            return r.LoadModuleFromSpecificNodeModulesDirectory(Extensions.Declaration, r.MangleScopedPackageName(r._name), nodeModulesAtTypes);
        }

        return null;
    }

    /// <summary>Go 的 <c>(*resolutionState).loadModuleFromSpecificNodeModulesDirectory</c>（resolver.go:1005-1120）。</summary>
    internal static Resolved? LoadModuleFromSpecificNodeModulesDirectory(this ResolutionState r, Extensions ext, string moduleName, string nodeModulesDirectory)
    {
        // Strip any trailing directory separator so that imports like `pkg/` and `pkg`
        // produce identical `candidate` and `packageDirectory` strings. Otherwise the
        // `package.json` info cache (which is keyed by normalized path but stores the
        // caller's `PackageDirectory` verbatim) can hand back, under concurrent
        // inserts, an entry whose `PackageDirectory` doesn't match `candidate`,
        // causing `loadNodeModuleFromDirectoryWorker`'s `ComparePaths(candidate, ...)`
        // check to fail and skip loading the package's `main`/`types` entry.
        // https://github.com/microsoft/TypeScript/tsc/issues/3526
        var candidate = RemoveTrailingDirectorySeparator(NormalizePath(CombinePaths(nodeModulesDirectory, moduleName)));
        var packageName = ParsePackageName(moduleName, out var rest);
        var packageDirectory = CombinePaths(nodeModulesDirectory, packageName);
        if (packageName == "")
        {
            packageDirectory = candidate;
        }

        if (r._resolvePackageDirectoryOnly)
        {
            if (r._resolver.Host.FS().DirectoryExists(packageDirectory))
            {
                return new Resolved { Path = packageDirectory };
            }

            return null;
        }

        InfoCacheEntry? rootPackageInfo = null;
        // First look for a nested package.json, as in `node_modules/foo/bar/package.json`
        var packageInfo = r.GetPackageJsonInfo(candidate);
        // But only if we're not respecting export maps (if we are, we might redirect around this location)
        if (rest != "" && packageInfo is not null)
        {
            if ((r._features & NodeResolutionFeatures.Exports) != 0)
            {
                rootPackageInfo = r.GetPackageJsonInfo(packageDirectory);
            }

            var rootPackageContents = rootPackageInfo?.GetContents();
            if (rootPackageContents is null || rootPackageContents.PathFields.Exports.Type == JSONValueType.NotPresent)
            {
                if (r.LoadModuleFromFile(ext, candidate) is { } fromFile)
                {
                    return fromFile;
                }

                if (r.LoadNodeModuleFromDirectoryWorker(ext, candidate, packageInfo) is { } fromDirectory)
                {
                    fromDirectory.PackageId = r.GetPackageId(fromDirectory.Path, packageInfo);
                    return fromDirectory;
                }
            }
        }

        ResolutionKindSpecificLoader loader = (extensions, candidate) =>
        {
            var contents = packageInfo?.GetContents();
            if (rest != "" || !r._esmMode)
            {
                if (r.LoadModuleFromFile(extensions, candidate) is { } fromFile)
                {
                    fromFile.PackageId = r.GetPackageId(fromFile.Path, packageInfo);
                    return fromFile;
                }
            }

            if (r.LoadNodeModuleFromDirectoryWorker(extensions, candidate, packageInfo) is { } fromDirectory)
            {
                fromDirectory.PackageId = r.GetPackageId(fromDirectory.Path, packageInfo);
                return fromDirectory;
            }

            if (rest == "" && contents is not null &&
                (contents.PathFields.Exports.Type == JSONValueType.NotPresent || contents.PathFields.Exports.Type == JSONValueType.Null) &&
                r._esmMode)
            {
                // EsmMode disables index lookup in `loadNodeModuleFromDirectoryWorker` generally, however non-relative package resolutions still assume
                // a default `index.js` entrypoint if no `main` or `exports` are present
                if (r.LoadModuleFromFile(extensions, CombinePaths(candidate, "index.js")) is { } indexResult)
                {
                    indexResult.PackageId = r.GetPackageId(indexResult.Path, packageInfo);
                    return indexResult;
                }
            }

            return null;
        };

        if (rest != "")
        {
            packageInfo = rootPackageInfo;
            if (packageInfo is null)
            {
                // Previous `packageInfo` may have been from a nested package.json; ensure we have the one from the package root now.
                packageInfo = r.GetPackageJsonInfo(packageDirectory);
            }
        }

        if (packageInfo is not null)
        {
            r._resolvedPackageDirectory = true;
            var contents = packageInfo.GetContents();
            if ((r._features & NodeResolutionFeatures.Exports) != 0 &&
                contents is not null &&
                !contents.PathFields.Exports.IsFalsy())
            {
                // package exports are higher priority than file/directory/typesVersions lookups and (and, if there's exports present*, blocks them)
                // *Well, weirdly enough a top-level `"exports": null` does NOT block fallback resolution.
                // https://github.com/microsoft/TypeScript/pull/49327
                return r.LoadModuleFromExports(packageInfo, ext, CombinePaths(".", rest));
            }

            if (rest != "" && contents is not null)
            {
                var versionPaths = contents.GetVersionPaths(r.GetTraceFunc());
                if (versionPaths.Exists())
                {
                    r._tracer?.Write(X_package_json_has_a_typesVersions_entry_0_that_matches_compiler_version_1_looking_for_a_pattern_to_match_module_name_2, versionPaths.Version, Version(), rest);
                    var versionPathsMap = versionPaths.GetPaths();
                    if (versionPathsMap is not null)
                    {
                        var pathPatterns = TryParsePatterns(versionPathsMap);
                        if (r.TryLoadModuleUsingPaths(ext, rest, packageDirectory, versionPathsMap, pathPatterns, loader) is { } fromPaths)
                        {
                            return fromPaths;
                        }
                    }
                }
            }
        }

        return loader(ext, candidate);
    }
}
