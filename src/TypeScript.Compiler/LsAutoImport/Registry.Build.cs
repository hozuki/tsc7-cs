// ported from: internal/ls/autoimport/registry.go（registry.go:1230-1850：`buildProjectBucket` 起的构建逻辑）。
// 类型定义（ProjectID/BucketState/RegistryBucket/Registry/RegistryBuilder 等）与前半部分在 Registry.cs。
//
// 与 Go 的偏离（逐条；前 18 条见 Registry.cs 文件头，这里只列本文件特有/需要重复强调的）：
// 1) `sync.WaitGroup` → `Core.WorkGroup`，每个 `wg.Wait()` 切一个阶段（WorkGroup 不能复用）；`foreach` 变量
//    天然按迭代捕获，只有 `for (var i = 0; …)` 需要在排队前拷 `var index = i;`（`ExtractPackage`）。
// 2) `b.nodeModules.Get(k)` / `b.directories.Get(k)` 等 dirty map 访问照 `(entry, ok)` 元组写。
// 3) `module.ResolverOptions` 是 Go 值类型 → C# 用 `ResolverOptionsCopy()` 新建实例（见 Registry.cs 偏离 8）。
// 4) Go 的 `slices.DeleteFunc(s, f)`（原地过滤、返回新长度）→ C# 的 `Array.FindAll`（新数组）。
// 5) Go 的 `maps.Copy(dst, src)` → C# 的 `foreach (var (k, v) in src) dst[k] = v;`。
// 6) Go 的 `map[string][]string` 的 `append` → C# 的 `[.. existing, .. added]`（类型统一为 `Dictionary<string, string[]>`，
//    与 `RegistryBucket.AmbientModuleNames` 的字段类型一致，省掉转换）。
// 7) `ctx.Err() != nil` → `ctx.CancellationToken.IsCancellationRequested`；`result.err` → `Exception?`
//    （只用其非空性判断"跳过桶替换"，故填 `new OperationCanceledException(ctx.CancellationToken)`）。
// 8) `LogTree.Fork` 的 nil 接收者（Go 里 nil-safe）→ C# 写 `?.`；`Logf` 保持 `if (logger is not null)` 守护。
// 9) `logger.Logf("... %v", time.Since(start))` → `Stopwatch.GetElapsedTime(...)` + C# 复合格式 `{0}`。
// 10) `GetSourceFile` 返回 null 的过滤：`ExtractPackage` 里 Go 用 `slices.DeleteFunc(rootFiles, f == nil)`；
//     第二遍的 `rootFiles`（`map[string]*ast.SourceFile`）Go **不**过滤 null，C# 的 `AliasResolver` 参数是
//     `SourceFile[]`（不可空），故这里过滤（否则 checker 内部会解引用 null；这是比 Go 更严格的等价行为）。
// 11) `Path.ForEachAncestorDirectory<T>` 在"回调从未返回 stop"时给出 `default(T)`（见 TSPath/Path.cs），
//     与 Go 返回零值一致；因此 `ResolveAmbientModuleName` 的返回类型是 `string[]?`（调用点 `?? []`）。
// 12) `strings.Contains(s, "/node_modules/")` → `s.Contains("/node_modules/", StringComparison.Ordinal)`。
using System.Threading;
using TypeScript.Ast;
using TypeScript.Binder;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsUtil;
using TypeScript.Module;
using TypeScript.PackageJson;
using TypeScript.Project.Logging;
using TypeScript.Symlinks;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsMatch;
// `MapEntry`（TypeScript.Project.Dirty）与 `TypeScript.Collections.MapEntry` 同名 ⇒ CS0104，用命名空间别名区分
// （见 Registry.cs 文件头的同类说明）。
using Dirty = TypeScript.Project.Dirty;
using Path = TypeScript.TSPath.Path;
using Stopwatch = System.Diagnostics.Stopwatch;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.LsAutoImport.AutoImport;
using static TypeScript.Module.Module;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type discoveredPackage struct</c>（registry.go:1330-1345）。</summary>
internal sealed class DiscoveredPackage
{
    internal string _packageName = "";

    internal InfoCacheEntry? _packageJson;

    internal string _realpath = "";

    internal InfoCacheEntry? _typesPackageJson;

    internal string _typesRealpath = "";

    /// <summary>桶目录路径（用作抽取上下文）。</summary>
    internal Path _dirPath;

    /// <summary>realpath 是否位于 workspace 根内。</summary>
    internal bool _isLocal;
}

/// <summary>Go: <c>type perPackageExtractionResult struct</c>（registry.go:1347-1362）。</summary>
internal sealed class PerPackageExtractionResult
{
    internal Dictionary<Path, string> _packageFiles = [];

    internal ResolvedEntrypoint[] _entrypoints = [];

    internal Dictionary<Path, Export[]> _exports = [];

    internal Dictionary<string, string[]> _ambientModules = [];

    internal int _statsExports;

    internal int _statsUsedChecker;

    internal int _skippedEntrypoints;

    internal bool _isSymlinked;

    internal Dictionary<Path, FailedAmbientModuleLookupSource> _failedAmbientModuleLookupSources = [];

    internal Set<string> _failedAmbientModuleLookupTargets = new();
}

/// <summary>Go: <c>type packageExtractionResult struct</c>（registry.go:1364-1373）。</summary>
internal sealed class PackageExtractionResult
{
    internal Dictionary<Path, Export[]> _exports = [];

    internal Dictionary<string, Dictionary<Path, string>?> _packageFiles = [];

    internal Dictionary<string, string[]> _ambientModuleNames = [];

    internal List<ResolvedEntrypoint[]> _entrypoints = [];

    internal Set<string> _workspacePackages = new();

    internal SyncMap<Path, FailedAmbientModuleLookupSource> _possibleFailedAmbientModuleLookupSources = new();

    internal SyncSet<string> _possibleFailedAmbientModuleLookupTargets = new();

    internal ExtractorStats _stats = new();

    internal int _skippedEntrypointsCount;
}

internal sealed partial class RegistryBuilder
{
    /// <summary>Go 的 <c>nodeModulesBucketTask</c>（registry.go:812-832，声明在 `updateIndexes` 内部；C# 不能声明
    /// 方法内类型，改成本类的私有嵌套类）。</summary>
    private sealed class NodeModulesBucketTask
    {
        internal Dirty.MapEntry<Path, RegistryBucket> _entry = null!;

        internal Set<string>? _dependencyNames;

        internal string _dirName = "";

        internal Path _dirPath;

        // For granular updates.
        internal bool _isUpdate;

        internal RegistryBucket? _existingBucket;

        internal Set<string>? _dirtyPackages;

        // Filled by discovery.
        internal Set<string>? _packageNames;

        internal Set<string>? _directoryPackageNames;

        internal List<DiscoveredPackage>? _discovered;
    }

    /// <summary>Go: <c>func (b *registryBuilder) newExportExtractor(packageName, checker, moduleResolver, realpath) *exportExtractor</c>
    /// （extract.go:73-78）：Go 把 `b.base.toPath` 传给 `newSymbolExtractor`。</summary>
    internal ExportExtractor NewExportExtractor(
        string packageName,
        TypeScript.Checker.Checker checker,
        TypeScript.Module.DefaultResolver moduleResolver,
        Func<string, string>? realpath) =>
        new(packageName, checker, moduleResolver, realpath, _base._toPath);

    /// <summary>Go 的 <c>resolverOptions := b.resolverOptions; resolverOptions.Host = b.host;
    /// resolverOptions.CompilerOptions = core.EmptyCompilerOptions</c>（**值拷贝**）的 C# 版：C# 的
    /// `ResolverOptions` 是 class，必须新建实例（见 Registry.cs 文件头偏离 8）。</summary>
    private ResolverOptions ResolverOptionsCopy() => new()
    {
        Host = _host,
        CompilerOptions = EmptyCompilerOptions,
        TypingsLocation = _resolverOptions.TypingsLocation,
        ProjectName = _resolverOptions.ProjectName,
        ExtraExtensions = _resolverOptions.ExtraExtensions,
        PackageJsonCache = _resolverOptions.PackageJsonCache,
    };

    /// <summary>Go: <c>func (b *registryBuilder) updateIndexes(ctx, change, logger)</c>（registry.go:812-1118）。</summary>
    internal void UpdateIndexes(Context ctx, RegistryChange change, LogTree? logger)
    {
        var (projectID, _) = _host.GetDefaultProject(change.RequestedFile);
        if (projectID is null)
        {
            return;
        }

        // Compute resolved package names and project reference output mappings for all projects upfront.
        // Resolved package names are needed to compute node_modules dependencies so packages that are
        // directly imported by programs are included even if not listed in package.json.
        // Project reference output mappings are needed to redirect extraction from output .d.ts files
        // to source files for packages that are project references.
        // We need all projects because a node_modules directory can be used by multiple projects.
        var allResolvedPackageNames = new Dictionary<ProjectID, Set<string>>();
        var projectReferenceOutputs = new Dictionary<Path, string>();
        // Compute which packages have implicit deep imports (subpath imports in packages
        // without exports). These packages need recursive directory search to discover
        // all auto-importable files, even when the preference is disabled.
        var allDeepImportPackages = new Set<string>();
        _projects.Range(entry =>
        {
            var program = _host.GetProgramForProject(entry.Key());
            if (program is not null)
            {
                allResolvedPackageNames[entry.Key()] = GetResolvedPackageNames(ctx, program);
                AddProjectReferenceOutputMappings(program, projectReferenceOutputs);
                var deepImportPackages = program.DeepImportPackageNames();
                if (deepImportPackages is not null)
                {
                    foreach (var name in deepImportPackages.Keys())
                    {
                        allDeepImportPackages.Add(name);
                    }
                }
            }

            return true;
        });

        var fileExcludePatterns = _userPreferences.ParsedAutoImportFileExcludePatterns(_host.FS().UseCaseSensitiveFileNames());

        // Determine which packages need recursive directory search for this build.
        // nil means all packages (preference is enabled for all).
        Set<string>? targetRecursivePackages = null;
        if (!_userPreferences.AutoImportEntrypointDirectorySearch.IsTrue())
        {
            targetRecursivePackages = allDeepImportPackages;
        }

        // --- Collect node_modules tasks ---
        var nodeModulesTasks = new List<NodeModulesBucketTask>();
        change.RequestedFile.ForEachAncestorDirectory<object?>(dirPath =>
        {
            var (nodeModulesBucket, ok) = _nodeModules.Get(dirPath);
            if (ok)
            {
                var (dirEntry, _) = _directories.Get(dirPath);
                var dirName = dirEntry!.Value()._name;
                var dependencies = ComputeDependenciesForNodeModulesDirectory(change, allResolvedPackageNames, dirName, dirPath);
                var bucketState = nodeModulesBucket!.Value()._state;
                // !!! Optimization: handle different dependency set via granular updates
                var needsFullRebuild = bucketState._multipleFilesDirty ||
                    !RegistryUtil.SetsEqual(nodeModulesBucket.Value().DependencyNames, dependencies) ||
                    !bucketState._buildPreferences.Equal(BucketBuildPreferencesFromUserPreferences(_userPreferences)) ||
                    !RecursiveSearchSubset(targetRecursivePackages, bucketState._recursiveSearchPackages);
                var dirtyPackages = bucketState.DirtyPackages();
                var canDoGranularUpdate = !needsFullRebuild && (dirtyPackages?.Len() ?? 0) > 0;

                if (needsFullRebuild)
                {
                    nodeModulesTasks.Add(new NodeModulesBucketTask
                    {
                        _entry = nodeModulesBucket,
                        _dependencyNames = dependencies,
                        _dirName = dirName,
                        _dirPath = dirPath,
                    });
                }
                else if (canDoGranularUpdate)
                {
                    nodeModulesTasks.Add(new NodeModulesBucketTask
                    {
                        _entry = nodeModulesBucket,
                        _dependencyNames = dependencies,
                        _dirName = dirName,
                        _dirPath = dirPath,
                        _isUpdate = true,
                        _existingBucket = nodeModulesBucket.Value(),
                        _dirtyPackages = dirtyPackages,
                    });
                }
            }

            return (null, false);
        }, out _);

        LogTree? nodeModulesLogger = null;
        if (logger is not null && nodeModulesTasks.Count > 0)
        {
            nodeModulesLogger = logger.Fork("Building node_modules indexes");
        }

        // --- Phase 1: Discovery (parallel per bucket) ---
        // Resolve package.json and realpath for each package in each bucket.
        var discoveryStart = Stopwatch.GetTimestamp();
        var wgDiscovery = NewWorkGroup(false);
        foreach (var task in nodeModulesTasks)
        {
            wgDiscovery.Queue(() =>
            {
                if (task._isUpdate)
                {
                    task._packageNames = task._dirtyPackages;
                }
                else
                {
                    task._directoryPackageNames = GetPackageNamesInNodeModules(CombinePaths(task._dirName, "node_modules"), _host.FS());
                    task._packageNames = Coalesce(task._dependencyNames, task._directoryPackageNames);
                }

                task._discovered = DiscoverBucketPackages(task._packageNames, task._dirName, task._dirPath);
            });
        }

        wgDiscovery.RunAndWait();
        if (nodeModulesLogger is not null)
        {
            nodeModulesLogger.Logf("Discovered packages: {0}", Stopwatch.GetElapsedTime(discoveryStart));
        }

        // --- Phase 2: Extraction (parallel per unique realpath) ---
        // Extract from main packages first. If a main package has no TypeScript entrypoints,
        // we fall back to extracting from @types in a second pass. Packages with no main
        // package extract directly from @types in the primary pass.
        var extractionStart = Stopwatch.GetTimestamp();
        var seen = new Dictionary<string, bool>();
        var extractionCache = new Dictionary<string, PerPackageExtractionResult>();
        var extractionMu = new object();
        // Collect all packages that have an @types fallback. After the primary pass, we
        // filter to only those whose main extraction failed, then deduplicate by typesRealpath.
        var typesFallbackCandidates = new List<DiscoveredPackage>();
        var wgExtraction = NewWorkGroup(false);
        foreach (var task in nodeModulesTasks)
        {
            if (task._discovered is null)
            {
                continue;
            }

            foreach (var pkg in task._discovered)
            {
                if (pkg._realpath != "")
                {
                    if (!seen.ContainsKey(pkg._realpath))
                    {
                        seen[pkg._realpath] = true;
                        var enableDirSearch = targetRecursivePackages is null ||
                            targetRecursivePackages.Has(pkg._packageName) ||
                            KnownRecursiveSearchPackages.Has(pkg._packageName);
                        // Record actual directory-searched packages so the stored set
                        // reflects reality for rebuild detection and stats.
                        if (enableDirSearch && targetRecursivePackages is not null)
                        {
                            targetRecursivePackages.Add(pkg._packageName);
                        }

                        wgExtraction.Queue(() =>
                        {
                            if (ctx.CancellationToken.IsCancellationRequested)
                            {
                                return;
                            }

                            var result = ExtractPackage(ctx, pkg._packageJson, pkg._packageName, projectReferenceOutputs, fileExcludePatterns, enableDirSearch);
                            if (result is not null)
                            {
                                lock (extractionMu)
                                {
                                    extractionCache[pkg._realpath] = result;
                                }
                            }
                        });
                    }

                    if (pkg._typesRealpath != "")
                    {
                        typesFallbackCandidates.Add(pkg);
                    }
                }
                else if (pkg._typesRealpath != "")
                {
                    if (!seen.ContainsKey(pkg._typesRealpath))
                    {
                        seen[pkg._typesRealpath] = true;
                        // @types packages always get directory search
                        targetRecursivePackages?.Add(pkg._packageName);
                        wgExtraction.Queue(() =>
                        {
                            if (ctx.CancellationToken.IsCancellationRequested)
                            {
                                return;
                            }

                            var result = ExtractPackage(ctx, pkg._typesPackageJson, pkg._packageName, projectReferenceOutputs, fileExcludePatterns, true /*enableDirectorySearch*/);
                            if (result is not null)
                            {
                                lock (extractionMu)
                                {
                                    extractionCache[pkg._typesRealpath] = result;
                                }
                            }
                        });
                    }
                }
            }
        }

        wgExtraction.RunAndWait();

        // For packages whose main extraction yielded nothing, fall back to @types.
        var wgTypesFallback = NewWorkGroup(false);
        foreach (var pkg in typesFallbackCandidates)
        {
            bool mainExtracted;
            lock (extractionMu)
            {
                mainExtracted = extractionCache.ContainsKey(pkg._realpath);
            }

            if (mainExtracted || seen.ContainsKey(pkg._typesRealpath))
            {
                continue;
            }

            seen[pkg._typesRealpath] = true;
            // @types fallback packages always get directory search
            targetRecursivePackages?.Add(pkg._packageName);
            wgTypesFallback.Queue(() =>
            {
                if (ctx.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var result = ExtractPackage(ctx, pkg._typesPackageJson, pkg._packageName, projectReferenceOutputs, fileExcludePatterns, true /*enableDirectorySearch*/);
                if (result is not null)
                {
                    lock (extractionMu)
                    {
                        extractionCache[pkg._typesRealpath] = result;
                    }
                }
            });
        }

        wgTypesFallback.RunAndWait();
        if (nodeModulesLogger is not null)
        {
            nodeModulesLogger.Logf("Extracted exports: {0} ({1} packages)", Stopwatch.GetElapsedTime(extractionStart), seen.Count);
        }

        _uniquePackageCount = seen.Count;

        // --- Phase 3: Bucket building (parallel per bucket) ---
        // Each bucket installs the shared extraction results and builds its index.
        var allResults = new List<BucketBuildResult>();
        var wgBuild = NewWorkGroup(false);

        foreach (var task in nodeModulesTasks)
        {
            var br = new BucketBuildResult
            {
                _replaceBucket = task._entry.Replace,
                _resolutionPath = task._entry.Key(),
            };
            allResults.Add(br);
            wgBuild.Queue(() =>
            {
                if (task._isUpdate)
                {
                    UpdateNodeModulesBucket(
                        ctx,
                        br,
                        task._existingBucket!,
                        task._dirtyPackages!,
                        task._discovered!,
                        extractionCache,
                        targetRecursivePackages,
                        nodeModulesLogger?.Fork(task._dirName));
                }
                else
                {
                    BuildNodeModulesBucket(
                        ctx,
                        br,
                        task._dependencyNames,
                        task._dirPath,
                        task._discovered!,
                        task._directoryPackageNames,
                        extractionCache,
                        targetRecursivePackages,
                        nodeModulesLogger?.Fork(task._dirName));
                }
            });
        }

        // Project bucket (not part of the three-phase pipeline — no cross-bucket dedup needed).
        var (project, hasProject) = _projects.Get(projectID);
        if (hasProject)
        {
            var program = _host.GetProgramForProject(projectID)!;
            Set<string>? resolvedPackageNames = allResolvedPackageNames.TryGetValue(projectID, out var names) ? names : null;
            var shouldRebuild = project!.Value()._state.HasDirtyFileBesides(change.RequestedFile) ||
                !project.Value()._state._buildPreferences.Equal(BucketBuildPreferencesFromUserPreferences(_userPreferences));
            if (!shouldRebuild && (int)project.Value()._state._newProgramStructure > 0)
            {
                if (!RegistryUtil.SetsEqual(project.Value().ResolvedPackageNames, resolvedPackageNames) ||
                    HasNewNonNodeModulesFiles(program, project.Value()))
                {
                    shouldRebuild = true;
                }
                else
                {
                    project.Change(b => b._state._newProgramStructure = NewProgramStructure.False);
                }
            }

            if (shouldRebuild)
            {
                var br = new BucketBuildResult
                {
                    _replaceBucket = project.Replace,
                    _resolutionPath = _base._toPath(program.GetCurrentDirectory()),
                };
                allResults.Add(br);
                wgBuild.Queue(() => BuildProjectBucket(
                    ctx,
                    br,
                    projectID,
                    resolvedPackageNames,
                    logger?.Fork("Building project bucket " + projectID.String())));
            }
        }

        wgBuild.RunAndWait();

        foreach (var br in allResults)
        {
            if (br._err is not null)
            {
                continue;
            }

            if (br._removedEntrypointPaths is not null)
            {
                foreach (var path in br._removedEntrypointPaths)
                {
                    _entrypoints.Delete(path);
                }
            }

            if (br._entrypoints is not null)
            {
                foreach (var (path, entries) in br._entrypoints)
                {
                    _entrypoints.Set(path, entries);
                }
            }

            br._replaceBucket(br._bucket!);
        }

        // If we failed to resolve any alias exports by ending up at a non-relative module specifier
        // that didn't resolve to another package, it's probably an ambient module declared in another package.
        // We recorded these failures, along with the name of every ambient module declared elsewhere, so we
        // can do a second pass on the failed files, this time including the ambient modules declarations that
        // were missing the first time. Example: node_modules/fs-extra/index.d.ts is simply `export * from "fs"`,
        // but when trying to resolve the `export *`, we don't know where "fs" is declared. The aliasResolver
        // tries to find packages named "fs" on the file system, but after failing, records "fs" as a failure
        // for fs-extra/index.d.ts. Meanwhile, if we also processed node_modules/@types/node/fs.d.ts, we
        // recorded that file as declaring the ambient module "fs". In the second pass, we combine those two
        // files and reprocess fs-extra/index.d.ts, this time finding "fs" declared in @types/node.
        var secondPassStart = Stopwatch.GetTimestamp();
        var secondPassFileCount = 0;
        foreach (var br in allResults)
        {
            if (br._err is not null)
            {
                continue;
            }

            if (br._possibleFailedAmbientModuleLookupTargets is null)
            {
                continue;
            }

            var rootFiles = new Dictionary<string, SourceFile?>();
            foreach (var target in br._possibleFailedAmbientModuleLookupTargets.Keys())
            {
                foreach (var fileName in ResolveAmbientModuleName(target, br._resolutionPath) ?? [])
                {
                    if (rootFiles.ContainsKey(fileName))
                    {
                        continue;
                    }

                    rootFiles[fileName] = _host.GetSourceFile(fileName, _base._toPath(fileName));
                    secondPassFileCount++;
                }
            }

            if (rootFiles.Count > 0)
            {
                var nonNullRootFiles = new List<SourceFile>(rootFiles.Count);
                foreach (var file in rootFiles.Values)
                {
                    if (file is not null)
                    {
                        nonNullRootFiles.Add(file);
                    }
                }

                var moduleResolver = NewResolver(ResolverOptionsCopy());
                var aliasResolver = new AliasResolver(
                    nonNullRootFiles.ToArray(),
                    null,
                    _host,
                    moduleResolver,
                    _base._toPath,
                    (_, _) =>
                    {
                        // no-op
                    });
                var (ch, _) = TypeScript.Checker.Checker.NewChecker(aliasResolver, null);
                br._possibleFailedAmbientModuleLookupSources!.Range((_, source) =>
                {
                    var sourceFile = aliasResolver.GetSourceFile(source!._fileName);
                    var extractor = NewExportExtractor(source._packageName, ch, moduleResolver, _host.FS().Realpath);
                    var fileExports = extractor.ExtractFromFile(sourceFile!);
                    foreach (var exp in fileExports)
                    {
                        br._bucket!.Index!.InsertAsWords(exp);
                    }

                    return true;
                });
            }
        }

        if (nodeModulesLogger is not null)
        {
            if (secondPassFileCount > 0)
            {
                nodeModulesLogger.Logf("{0} files required second pass, took {1}", secondPassFileCount, Stopwatch.GetElapsedTime(secondPassStart));
            }

            nodeModulesLogger.Logf("Total: {0}", Stopwatch.GetElapsedTime(discoveryStart));
        }
    }

    /// <summary>Go: <c>func (b *registryBuilder) buildProjectBucket(ctx, result, projectID, resolvedPackageNames, logger)</c>
    /// （registry.go:1240-1320）。</summary>
    internal void BuildProjectBucket(
        Context ctx,
        BucketBuildResult result,
        ProjectID projectID,
        Set<string>? resolvedPackageNames,
        LogTree? logger)
    {
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            result._err = new OperationCanceledException(ctx.CancellationToken);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var mu = new object();
        var fileExcludePatterns = _userPreferences.ParsedAutoImportFileExcludePatterns(_host.FS().UseCaseSensitiveFileNames());
        result._bucket = new RegistryBucket();
        var moduleResolver = NewResolver(ResolverOptionsCopy());
        var program = _host.GetProgramForProject(projectID)!;
        var projectRootPath = _base._toPath(program.GetCurrentDirectory());
        var symlinkCache = program.GetSymlinkCache();
        var (getChecker, closePool, checkerCount) = CreateCheckerPool(program);
        try
        {
            var exports = new Dictionary<Path, Export[]>();
            var wg = NewWorkGroup(false);
            var skippedFileCount = 0;
            var combinedStats = new ExtractorStats();

            foreach (var file in program.GetSourceFiles())
            {
                if (file.IsContentMapperSupplemental() || IsIgnoredFile(program, file))
                {
                    continue;
                }

                if (fileExcludePatterns is { } patterns && patterns.MatchString(file.FileName()))
                {
                    skippedFileCount++;
                    continue;
                }

                // Ordinary node_modules files are owned by node_modules buckets. Content-mapped files are not
                // discovered by those buckets, but files already transformed in the Program can be indexed here.
                if (file.ContentMapper() == "" &&
                    (file.FileName().Contains("/node_modules/", StringComparison.Ordinal) ||
                     HasSymlinkToNodeModules(file.Path(), projectRootPath, symlinkCache)))
                {
                    continue;
                }

                wg.Queue(() =>
                {
                    if (!ctx.CancellationToken.IsCancellationRequested)
                    {
                        var (checker, done) = getChecker();
                        try
                        {
                            var extractor = NewExportExtractor("", checker, moduleResolver, null);
                            var fileExports = extractor.ExtractFromFile(file);
                            lock (mu)
                            {
                                exports[file.Path()] = fileExports;
                            }

                            var stats = extractor.Stats();
                            combinedStats.AddExports(stats.Exports);
                            combinedStats.AddUsedChecker(stats.UsedChecker);
                        }
                        finally
                        {
                            done();
                        }
                    }
                });
            }

            wg.RunAndWait();

            var indexStart = Stopwatch.GetTimestamp();
            var idx = new Index<Export>();
            var paths = new Dictionary<Path, string>(exports.Count);
            foreach (var (path, fileExports) in exports)
            {
                paths[path] = ""; // Empty string for project buckets
                foreach (var exp in fileExports)
                {
                    idx.InsertAsWords(exp);
                }
            }

            result._bucket.Paths = paths;
            result._bucket.Index = idx;
            result._bucket.ResolvedPackageNames = resolvedPackageNames;
            result._bucket._state._buildPreferences = BucketBuildPreferencesFromUserPreferences(_userPreferences);

            if (logger is not null)
            {
                logger.Logf(
                    "Extracted exports: {0} ({1} exports, {2} used checker, {3} created checkers)",
                    Stopwatch.GetElapsedTime(start, indexStart),
                    combinedStats.Exports,
                    combinedStats.UsedChecker,
                    checkerCount());
                if (skippedFileCount > 0)
                {
                    logger.Logf("Skipped {0} files due to exclude patterns", skippedFileCount);
                }

                logger.Logf("Built index: {0}", Stopwatch.GetElapsedTime(indexStart));
                logger.Logf("Bucket total: {0}", Stopwatch.GetElapsedTime(start));
            }
        }
        finally
        {
            closePool();
        }
    }

    /// <summary>Go: <c>func (b *registryBuilder) computeDependenciesForNodeModulesDirectory(change, allResolvedPackageNames, dirName, dirPath)</c>
    /// （registry.go:1322-1352）。</summary>
    internal Set<string>? ComputeDependenciesForNodeModulesDirectory(
        RegistryChange change,
        Dictionary<ProjectID, Set<string>> allResolvedPackageNames,
        string dirName,
        Path dirPath)
    {
        // If any open files are in scope of this directory but not in scope of any package.json,
        // we need to add all packages in this node_modules directory.
        if (change.OpenFiles is not null)
        {
            foreach (var path in change.OpenFiles.Keys)
            {
                if (dirPath.ContainsPath(path) && GetNearestAncestorDirectoryWithPackageJson(path) is null)
                {
                    return null;
                }
            }
        }

        // Get all package.jsons that have this node_modules directory in their spine
        var dependencies = new Set<string>();
        _directories.Range(entry =>
        {
            if ((entry.Value()._packageJson?.Exists() ?? false) && dirPath.ContainsPath(entry.Key()))
            {
                AddPackageJsonDependencies(entry.Value()._packageJson!.Contents!, dependencies);
            }

            return true;
        });

        // Add packages that are directly imported by programs but not listed in package.json.
        // This ensures node_modules files are always in node_modules buckets.
        // Include packages from all projects that have this node_modules directory in their spine.
        foreach (var resolvedPackageNames in allResolvedPackageNames.Values)
        {
            foreach (var name in resolvedPackageNames.Keys())
            {
                dependencies.Add(name);
            }
        }

        return dependencies;
    }

    /// <summary>Go: <c>func (b *registryBuilder) discoverBucketPackages(packageNames, dirName, dirPath) []*discoveredPackage</c>
    /// （registry.go:1375-1421）。</summary>
    internal List<DiscoveredPackage> DiscoverBucketPackages(Set<string>? packageNames, string dirName, Path dirPath)
    {
        var result = new List<DiscoveredPackage>(packageNames?.Len() ?? 0);
        if (packageNames is null)
        {
            return result;
        }

        foreach (var packageName in packageNames.Keys())
        {
            var typesPackageName = GetTypesPackageName(packageName);
            var packageJson = _host.GetPackageJson(CombinePaths(dirName, "node_modules", packageName, "package.json"))!;
            InfoCacheEntry? typesPackageJson = null;
            if (packageName != typesPackageName)
            {
                var typesJson = _host.GetPackageJson(CombinePaths(dirName, "node_modules", typesPackageName, "package.json"))!;
                if (typesJson.DirectoryExists)
                {
                    typesPackageJson = typesJson;
                }
            }

            var realpath = "";
            if (packageJson.DirectoryExists)
            {
                realpath = _host.FS().Realpath(packageJson.PackageDirectory);
            }

            var typesRealpath = "";
            if (typesPackageJson is not null)
            {
                typesRealpath = _host.FS().Realpath(typesPackageJson.PackageDirectory);
            }

            var isLocal = realpath != "" &&
                !realpath.Contains("/node_modules/", StringComparison.Ordinal) &&
                ContainsPath(
                    _host.GetCurrentDirectory(),
                    realpath,
                    new ComparePathsOptions(_host.FS().UseCaseSensitiveFileNames()));
            result.Add(new DiscoveredPackage
            {
                _packageName = packageName,
                _packageJson = packageJson,
                _realpath = realpath,
                _typesPackageJson = typesPackageJson,
                _typesRealpath = typesRealpath,
                _dirPath = dirPath,
                _isLocal = isLocal,
            });
        }

        return result;
    }

    /// <summary>Go: <c>func (b *registryBuilder) extractPackage(ctx, packageJson, packageName, projectReferenceOutputs, fileExcludePatterns, enableDirectorySearch)</c>
    /// （registry.go:1423-1643）。</summary>
    internal PerPackageExtractionResult? ExtractPackage(
        Context ctx,
        InfoCacheEntry? packageJson,
        string packageName,
        Dictionary<Path, string> projectReferenceOutputs,
        SpecMatcher? fileExcludePatterns,
        bool enableDirectorySearch)
    {
        if (packageJson is null || !packageJson.DirectoryExists)
        {
            return null;
        }

        var (toRealpath, toSymlink) = GetPackageRealpathFuncs(_host.FS(), packageJson.PackageDirectory);
        var resolver = GetModuleResolver(_host, toRealpath, ResolverOptionsCopy());
        var packageEntrypoints = resolver.GetEntrypointsFromPackageJsonInfo(packageJson, packageName, enableDirectorySearch);
        if (packageEntrypoints is null)
        {
            return null;
        }

        var skippedEntrypoints = 0;
        if (fileExcludePatterns is { } patterns)
        {
            var count = packageEntrypoints.Length;
            packageEntrypoints = Array.FindAll(packageEntrypoints, entrypoint => !patterns.MatchString(entrypoint.ResolvedFileName));
            skippedEntrypoints = count - packageEntrypoints.Length;
        }

        if (packageEntrypoints.Length == 0)
        {
            return null;
        }

        var result = new PerPackageExtractionResult
        {
            _entrypoints = packageEntrypoints,
            _skippedEntrypoints = skippedEntrypoints,
        };

        // Resolve entrypoint source files and build the alias resolver.
        var seenFiles = NewSetWithSizeHint<Path>(packageEntrypoints.Length);
        var rootFiles = new SourceFile?[packageEntrypoints.Length];
        var symlinks = new Dictionary<Path, PathAndFileName>();
        var wg = NewWorkGroup(false);
        for (var i = 0; i < packageEntrypoints.Length; i++)
        {
            var entrypoint = packageEntrypoints[i];
            var fileName = entrypoint.SymlinkOrRealpath();
            var realpathFileName = entrypoint.ResolvedFileName;
            var realpathPath = _base._toPath(realpathFileName);

            if (projectReferenceOutputs.TryGetValue(realpathPath, out var inputFileName))
            {
                fileName = toSymlink(inputFileName);
                realpathFileName = inputFileName;
                realpathPath = _base._toPath(realpathFileName);
            }

            if (!seenFiles.AddIfAbsent(realpathPath))
            {
                continue;
            }

            if (fileName != realpathFileName)
            {
                var symlinkPath = _base._toPath(fileName);
                symlinks[realpathPath] = new PathAndFileName { _path = symlinkPath, _fileName = fileName };
                result._isSymlinked = true;
            }

            // Go 1.22 的 wg.Go 闭包按迭代捕获循环变量；C# 的 `for` 变量是共享的，必须拷一份。
            var index = i;
            wg.Queue(() =>
            {
                var file = _host.GetSourceFile(realpathFileName, realpathPath);
                if (file is not null)
                {
                    TypeScript.Binder.Binder.BindSourceFile(file);
                }

                rootFiles[index] = file;
            });
        }

        wg.RunAndWait();

        var nonNullRootFiles = new List<SourceFile>(rootFiles.Length);
        foreach (var file in rootFiles)
        {
            if (file is not null)
            {
                nonNullRootFiles.Add(file);
            }
        }

        var aliasResolver = new AliasResolver(
            nonNullRootFiles.ToArray(),
            symlinks,
            _host,
            resolver,
            _base._toPath,
            (source, moduleName) =>
            {
                result._failedAmbientModuleLookupTargets.Add(moduleName);
                if (!result._failedAmbientModuleLookupSources.ContainsKey(source.Path()))
                {
                    result._failedAmbientModuleLookupSources[source.Path()] = new FailedAmbientModuleLookupSource
                    {
                        _fileName = source.FileName(),
                    };
                }
            });

        var (ch, _) = TypeScript.Checker.Checker.NewChecker(aliasResolver, null);
        var extractor = NewExportExtractor(packageName, ch, resolver, toRealpath);

        var nonModuleFiles = new Set<Path>();
        foreach (var entrypoint in aliasResolver._rootFiles)
        {
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var fileExports = extractor.ExtractFromFile(entrypoint);
            foreach (var name in entrypoint.AmbientModuleNames ?? [])
            {
                result._ambientModules[name] = result._ambientModules.TryGetValue(name, out var existing)
                    ? [.. existing, entrypoint.FileName()]
                    : [entrypoint.FileName()];
            }

            result._packageFiles[entrypoint.Path()] = entrypoint.FileName();
            var symlink = default(PathAndFileName);
            var hasSymlink = false;
            if (aliasResolver._symlinks is not null && aliasResolver._symlinks.TryGetValue(entrypoint.Path(), out var found))
            {
                symlink = found;
                hasSymlink = true;
            }

            if (hasSymlink)
            {
                result._packageFiles[symlink._path] = symlink._fileName!;
            }

            var hasExports = fileExports.Length > 0 && entrypoint.ExternalModuleIndicator is not null;
            if (!result._failedAmbientModuleLookupSources.TryGetValue(entrypoint.Path(), out var source))
            {
                result._exports[entrypoint.Path()] = fileExports;
            }
            else
            {
                source._packageName = packageName;
                hasExports = entrypoint.ExternalModuleIndicator is not null;
            }

            if (!hasExports)
            {
                nonModuleFiles.Add(entrypoint.Path());
                if (hasSymlink)
                {
                    nonModuleFiles.Add(symlink._path);
                }
            }
        }

        // Discard entrypoints for non-module files and empty modules.
        result._entrypoints = Array.FindAll(result._entrypoints, ep => !nonModuleFiles.Has(_base._toPath(ep.ResolvedFileName)));

        var stats = extractor.Stats();
        result._statsExports = stats.Exports;
        result._statsUsedChecker = stats.UsedChecker;
        return result;
    }

    /// <summary>Go: <c>func (b *registryBuilder) buildNodeModulesBucket(ctx, result, dependencies, dirPath, discovered, directoryPackageNames, extractionCache, recursiveSearchPackages, logger)</c>
    /// （registry.go:1693-1787）。</summary>
    internal void BuildNodeModulesBucket(
        Context ctx,
        BucketBuildResult result,
        Set<string>? dependencies,
        Path dirPath,
        List<DiscoveredPackage> discovered,
        Set<string>? directoryPackageNames,
        Dictionary<string, PerPackageExtractionResult> extractionCache,
        Set<string>? recursiveSearchPackages,
        LogTree? logger)
    {
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            result._err = new OperationCanceledException(ctx.CancellationToken);
            return;
        }

        var extraction = InstallExtractions(discovered, extractionCache);

        var indexStart = Stopwatch.GetTimestamp();
        // Build PackageFiles with all directory package names; indexed packages have
        // non-nil maps, unindexed packages have nil maps.
        var allPackageFiles = new Dictionary<string, Dictionary<Path, string>?>(directoryPackageNames?.Len() ?? 0);
        if (directoryPackageNames is not null)
        {
            foreach (var pkgName in directoryPackageNames.Keys())
            {
                allPackageFiles[pkgName] = extraction._packageFiles.TryGetValue(pkgName, out var files) ? files : null;
            }
        }

        // Build Paths as reverse mapping from path to package name.
        // Only include paths for local workspace packages (eligible for granular updates).
        var paths = new Dictionary<Path, string>();
        foreach (var pkgName in extraction._workspacePackages.Keys())
        {
            if (extraction._packageFiles.TryGetValue(pkgName, out var files) && files is not null)
            {
                foreach (var path in files.Keys)
                {
                    paths[path] = pkgName;
                }
            }
        }

        result._bucket = new RegistryBucket
        {
            Index = new Index<Export>(),
            DependencyNames = dependencies,
            PackageFiles = allPackageFiles,
            AmbientModuleNames = extraction._ambientModuleNames,
            Paths = paths,
            _state = new BucketState
            {
                _buildPreferences = BucketBuildPreferencesFromUserPreferences(_userPreferences),
                _recursiveSearchPackages = recursiveSearchPackages?.Clone(),
            },
        };
        result._entrypoints = new Dictionary<Path, ResolvedEntrypoint[]>(extraction._exports.Count);
        result._possibleFailedAmbientModuleLookupSources = extraction._possibleFailedAmbientModuleLookupSources;
        result._possibleFailedAmbientModuleLookupTargets = extraction._possibleFailedAmbientModuleLookupTargets;
        foreach (var (_, fileExports) in extraction._exports)
        {
            foreach (var exp in fileExports)
            {
                result._bucket.Index.InsertAsWords(exp);
            }
        }

        foreach (var entrypointSet in extraction._entrypoints)
        {
            foreach (var entrypoint in entrypointSet)
            {
                var path = _base._toPath(entrypoint.ResolvedFileName);
                result._entrypoints[path] = result._entrypoints.TryGetValue(path, out var existing)
                    ? [.. existing, entrypoint]
                    : [entrypoint];
            }
        }

        // Compute old entrypoint paths to remove from the registry-level map.
        // For a full rebuild, all entrypoints belonging to the old bucket's packages must be removed.
        var (oldEntry, oldExists) = _nodeModules.Get(dirPath);
        if (oldExists)
        {
            var oldBucket = oldEntry!.Value();
            if (oldBucket.PackageFiles is not null)
            {
                foreach (var files in oldBucket.PackageFiles.Values)
                {
                    if (files is null)
                    {
                        continue;
                    }

                    foreach (var path in files.Keys)
                    {
                        if (_base._entrypoints.ContainsKey(path))
                        {
                            result._removedEntrypointPaths.Add(path);
                        }
                    }
                }
            }
        }

        if (logger is not null)
        {
            logger.Logf("Installed {0} exports ({1} used checker)", extraction._stats.Exports, extraction._stats.UsedChecker);
            if (extraction._skippedEntrypointsCount > 0)
            {
                logger.Logf("Skipped {0} entrypoints due to exclude patterns", extraction._skippedEntrypointsCount);
            }

            logger.Logf("Built index: {0}", Stopwatch.GetElapsedTime(indexStart));
        }

        result._err = ctx.CancellationToken.IsCancellationRequested
            ? new OperationCanceledException(ctx.CancellationToken)
            : null;
    }

    /// <summary>Go: <c>func (b *registryBuilder) updateNodeModulesBucket(ctx, result, existingBucket, dirtyPackages, discovered, extractionCache, recursiveSearchPackages, logger)</c>
    /// （registry.go:1789-1905）。</summary>
    internal void UpdateNodeModulesBucket(
        Context ctx,
        BucketBuildResult result,
        RegistryBucket existingBucket,
        Set<string> dirtyPackages,
        List<DiscoveredPackage> discovered,
        Dictionary<string, PerPackageExtractionResult> extractionCache,
        Set<string>? recursiveSearchPackages,
        LogTree? logger)
    {
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            result._err = new OperationCanceledException(ctx.CancellationToken);
            return;
        }

        var start = Stopwatch.GetTimestamp();
        var extraction = InstallExtractions(discovered, extractionCache);

        var indexStart = Stopwatch.GetTimestamp();

        // Clone the existing index, excluding exports from dirty packages
        var newIndex = existingBucket.Index!.Clone(exp => !dirtyPackages.Has(exp.PackageName));

        // Clone PackageFiles, removing dirty packages
        var newPackageFiles = new Dictionary<string, Dictionary<Path, string>?>(existingBucket.PackageFiles ?? []);
        foreach (var pkgName in dirtyPackages.Keys())
        {
            newPackageFiles.Remove(pkgName);
        }

        // Add newly extracted package files
        foreach (var (pkgName, files) in extraction._packageFiles)
        {
            newPackageFiles[pkgName] = files;
        }

        // Clone Paths, removing dirty package paths
        var newPaths = new Dictionary<Path, string>(existingBucket.Paths?.Count ?? 0);
        if (existingBucket.Paths is not null)
        {
            foreach (var (path, pkgName) in existingBucket.Paths)
            {
                if (dirtyPackages.Has(pkgName))
                {
                    continue;
                }

                newPaths[path] = pkgName;
            }
        }

        // Add paths for newly extracted workspace packages
        foreach (var pkgName in extraction._workspacePackages.Keys())
        {
            if (extraction._packageFiles.TryGetValue(pkgName, out var files) && files is not null)
            {
                foreach (var path in files.Keys)
                {
                    newPaths[path] = pkgName;
                }
            }
        }

        // Clone AmbientModuleNames, removing dirty package entries
        var newAmbientModuleNames = new Dictionary<string, string[]>(existingBucket.AmbientModuleNames?.Count ?? 0);
        if (existingBucket.AmbientModuleNames is not null)
        {
            foreach (var (moduleName, fileNames) in existingBucket.AmbientModuleNames)
            {
                // Filter out files from dirty packages
                List<string>? filtered = null;
                foreach (var fileName in fileNames)
                {
                    var path = _base._toPath(fileName);
                    if (existingBucket.Paths is not null &&
                        existingBucket.Paths.TryGetValue(path, out var pkgName) &&
                        dirtyPackages.Has(pkgName))
                    {
                        continue;
                    }

                    (filtered ??= []).Add(fileName);
                }

                if (filtered is not null)
                {
                    newAmbientModuleNames[moduleName] = filtered.ToArray();
                }
            }
        }

        // Add newly extracted ambient module names
        foreach (var (moduleName, fileNames) in extraction._ambientModuleNames)
        {
            newAmbientModuleNames[moduleName] = newAmbientModuleNames.TryGetValue(moduleName, out var existing)
                ? [.. existing, .. fileNames]
                : fileNames;
        }

        // Collect entrypoint paths that need to be removed from the registry-level map
        // (paths belonging to dirty packages)
        var removedEntrypointPaths = new List<Path>();
        foreach (var path in _base._entrypoints.Keys)
        {
            if (existingBucket.Paths is not null &&
                existingBucket.Paths.TryGetValue(path, out var pkgName) &&
                dirtyPackages.Has(pkgName))
            {
                removedEntrypointPaths.Add(path);
            }
        }

        // Build new entrypoints from extraction
        var newEntrypoints = new Dictionary<Path, ResolvedEntrypoint[]>();
        foreach (var entrypointSet in extraction._entrypoints)
        {
            foreach (var entrypoint in entrypointSet)
            {
                var path = _base._toPath(entrypoint.ResolvedFileName);
                newEntrypoints[path] = newEntrypoints.TryGetValue(path, out var existing)
                    ? [.. existing, entrypoint]
                    : [entrypoint];
            }
        }

        // Insert newly extracted exports into the index
        foreach (var (_, fileExports) in extraction._exports)
        {
            foreach (var exp in fileExports)
            {
                newIndex.InsertAsWords(exp);
            }
        }

        result._bucket = new RegistryBucket
        {
            Index = newIndex,
            DependencyNames = existingBucket.DependencyNames,
            PackageFiles = newPackageFiles,
            AmbientModuleNames = newAmbientModuleNames,
            Paths = newPaths,
            _state = new BucketState
            {
                _buildPreferences = BucketBuildPreferencesFromUserPreferences(_userPreferences),
                _recursiveSearchPackages = recursiveSearchPackages?.Clone(),
            },
        };
        result._entrypoints = newEntrypoints;
        result._removedEntrypointPaths = removedEntrypointPaths;
        result._possibleFailedAmbientModuleLookupSources = extraction._possibleFailedAmbientModuleLookupSources;
        result._possibleFailedAmbientModuleLookupTargets = extraction._possibleFailedAmbientModuleLookupTargets;

        if (logger is not null)
        {
            logger.Logf(
                "Granular update of {0} packages: {1} ({2} exports)",
                dirtyPackages.Len(),
                Stopwatch.GetElapsedTime(start, indexStart),
                extraction._stats.Exports);
            logger.Logf("Built index: {0}", Stopwatch.GetElapsedTime(indexStart));
        }

        result._err = ctx.CancellationToken.IsCancellationRequested
            ? new OperationCanceledException(ctx.CancellationToken)
            : null;
    }

    /// <summary>Go: <c>func (b *registryBuilder) getNearestAncestorDirectoryWithPackageJson(filePath) *directory</c>
    /// （registry.go:1907-1915）。</summary>
    internal RegistryDirectory? GetNearestAncestorDirectoryWithPackageJson(Path filePath)
    {
        filePath.GetDirectoryPath().ForEachAncestorDirectory<RegistryDirectory?>(dirPath =>
        {
            var (dirEntry, ok) = _directories.Get(dirPath);
            if (ok && (dirEntry!.Value()._packageJson?.Exists() ?? false))
            {
                return (dirEntry.Value(), true);
            }

            return (null, false);
        }, out var result);
        return result;
    }

    /// <summary>Go: <c>func (b *registryBuilder) resolveAmbientModuleName(moduleName, fromPath) []string</c>
    /// （registry.go:1917-1926）。Go 在未命中时返回 nil（见本文件偏离 11）。</summary>
    internal string[]? ResolveAmbientModuleName(string moduleName, Path fromPath)
    {
        fromPath.ForEachAncestorDirectory<string[]?>(dirPath =>
        {
            var (bucket, ok) = _nodeModules.Get(dirPath);
            if (ok &&
                bucket!.Value().AmbientModuleNames is { } ambientModuleNames &&
                ambientModuleNames.TryGetValue(moduleName, out var fileNames))
            {
                return (fileNames, true);
            }

            return (null, false);
        }, out var result);
        return result;
    }
}
