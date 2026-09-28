// ported from: internal/compiler/filesparser.go
//
// Go→C# 改写（逐条，都不改变语义）：
// 1) `sync.Pool` → `ConcurrentBag<ParseTaskData>`（TryTake 失败即 `new`，等价于 Go 的 New 回调；AOT 安全，无反射）。
// 2) append 型的切片（`subTasks`/`processingDiagnostics`/`missingFiles`/`files`…）→ `List<T>`（数组没有 len<cap 视图，§3.2）。
// 3) `map[*parseTaskData]string` 等以指针为键的 map → `Dictionary<T, …>`（C# 类的默认相等 = 引用相等，与 Go 的指针相等一致）。
// 4) `clear(td.tasks)` / `math.MaxInt` → `Clear()` / `int.MaxValue`。
// 5) `slices.Sort(keys)`（`[]tspath.Path`，Go 的 `<` 是序数字符串比较）→ `Array.Sort(keys, static (a, b) =>
//    string.CompareOrdinal(a.Value, b.Value))`（`Path` 是 record struct，没有 IComparable，不能直接排）。
// 6) Go 的 nil map（`redirectTargetsMap`/`outputFileToProjectReferenceSource` 只在特定开关下创建）→ 一律创建：
//    nil map 的读与空字典一致，而原代码对它们的**写**也只在创建时才发生；Program 侧（T10Program）按非空字典访问。
//    `_jsxRuntimeImportSpecifiers`/`_importHelpersImportSpecifiers`/`_redirectFilesByPath` 保留可空（Go 里确实是 nil）。
// 7) Go 的 nil `ModeAwareCache`（`task.resolutionsInFile` 在 moduleNames 为空时是 nil）→ 空 `ModeAwareCache`
//    （只读遍历/查找行为相同）。
// 8) `PackageId` 作字典键：C# 结构体的默认相等是字段级，Go 的零值是 `""`；本包内 `data.packageId` 只来自
//    resolver 输出（构造路径一致），故与 Go 的 `==` 等价。
using System.Collections.Concurrent;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Diagnostic = TypeScript.Ast.Diagnostic;
using Path = TypeScript.TSPath.Path;
using TracingPhase = TypeScript.Tracing.Phase;

namespace TypeScript.Compiler;

/// <summary>Go: <c>type parseTask struct</c>（实现 <see cref="HasFileName"/>）。</summary>
internal sealed class ParseTask : HasFileName
{
    internal string _normalizedFilePath = "";
    internal Path _path;
    internal SourceFile? _file;
    internal LibFile? _libFile;
    internal ParseTask? _redirectedParseTask;
    internal List<ParseTask> _subTasks = [];
    internal bool _loaded;
    internal bool _startedSubTasks;
    internal bool _isForAutomaticTypeDirective;
    internal bool _isContentMapperSupplemental;
    internal bool _failedLookup;
    internal FileIncludeReason? _includeReason;
    internal PackageId _packageId;

    internal SourceFileMetaData _metadata = new();
    internal ModeAwareCache<ResolvedModule>? _resolutionsInFile;
    internal DiagAndArgs[] _resolutionsTrace = [];
    internal ModeAwareCache<ResolvedTypeReferenceDirective>? _typeResolutionsInFile;
    internal DiagAndArgs[] _typeResolutionsTrace = [];
    internal List<Diagnostic> _resolutionDiagnostics = [];
    internal List<ProcessingDiagnostic> _processingDiagnostics = [];
    internal Node? _importHelpersImportSpecifier;
    internal JsxRuntimeImportSpecifier? _jsxRuntimeImportSpecifier;

    internal bool _increaseDepth;
    internal bool _elideOnDepth;

    internal ParseTask? _loadedTask;
    internal List<FileIncludeReason> _allIncludeReasons = [];

    public string FileName() => _normalizedFilePath;

    public Path Path() => _path;

    internal void Load(FileLoader loader)
    {
        _loaded = true;
        if (_isForAutomaticTypeDirective)
        {
            LoadAutomaticTypeDirectives(loader);
            return;
        }

        if (_failedLookup)
        {
            // The root file name did not resolve to a supported extension; the task
            // exists only to carry its processing diagnostic, so nothing is parsed.
            return;
        }

        var traceDone = loader._opts.Tracing?.Push(
            TracingPhase.Program,
            "findSourceFile",
            new Dictionary<string, object?> { ["fileName"] = _normalizedFilePath },
            false);
        try
        {
            var redirect = loader._projectReferenceFileMapper!.GetParseFileRedirect(this);
            if (redirect != "")
            {
                Redirect(loader, redirect);
                return;
            }

            if (!_isContentMapperSupplemental && HasExtension(_normalizedFilePath))
            {
                var optionsForExtensionCheck = loader._opts.Config.CompilerOptions()!;
                var allowNonTsExtensions = optionsForExtensionCheck.AllowNonTsExtensions.IsTrue();
                if (!allowNonTsExtensions)
                {
                    var canonicalFileName = GetCanonicalFileName(_normalizedFilePath, loader._opts.Host.FS().UseCaseSensitiveFileNames());
                    if (!loader.IsSupportedExtension(canonicalFileName))
                    {
                        if (HasJSFileExtension(canonicalFileName))
                        {
                            _processingDiagnostics.Add(new ProcessingDiagnostic
                            {
                                Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                                Data = new IncludeExplainingDiagnostic
                                {
                                    DiagnosticReason = _includeReason,
                                    Message = File_0_is_a_JavaScript_file_Did_you_mean_to_enable_the_allowJs_option,
                                    Args = [_normalizedFilePath],
                                },
                            });
                        }
                        else
                        {
                            _processingDiagnostics.Add(new ProcessingDiagnostic
                            {
                                Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                                Data = new IncludeExplainingDiagnostic
                                {
                                    DiagnosticReason = _includeReason,
                                    Message = File_0_has_an_unsupported_extension_The_only_supported_extensions_are_1,
                                    Args = [_normalizedFilePath, "'" + string.Join("', '", Flatten(loader._supportedExtensions)) + "'"],
                                },
                            });
                        }

                        return;
                    }
                }
            }

            Interlocked.Increment(ref loader._totalFileCount);
            if (_libFile is not null)
            {
                Interlocked.Increment(ref loader._libFileCount);
                // Default lib files are all scripts; we can safely skip looking up their package.json
                // to avoid adding spurious lookups to file watcher tracking.
                _metadata = new SourceFileMetaData { ImpliedNodeFormat = ModuleKind.CommonJS };
            }
            else
            {
                _metadata = loader.LoadSourceFileMetaData(_normalizedFilePath);
            }

            var file = _file;
            file ??= loader.ParseSourceFile(this);
            if (file is null)
            {
                return;
            }

            _file = file;
            if (file.VirtualFileName() is { Length: > 0 } virtualFileName)
            {
                _metadata.ImpliedNodeFormat = GetImpliedNodeFormatForFile(virtualFileName, _metadata.PackageJsonType);
            }

            var referencedFiles = file.ReferencedFiles ?? [];
            _subTasks = new List<ParseTask>(referencedFiles.Length + file.Imports().Length + (file.ModuleAugmentations?.Length ?? 0));

            var compilerOptions = loader._opts.Config.CompilerOptions()!;
            if (!compilerOptions.NoResolve.IsTrue() && !loader._opts.SkipModuleResolution)
            {
                for (var index = 0; index < referencedFiles.Length; index++)
                {
                    var (resolvedRef, processingDiagnostic) = loader.ResolveTripleslashPathReference(referencedFiles[index].FileName, file.FileName(), index);
                    if (processingDiagnostic is not null)
                    {
                        _processingDiagnostics.Add(processingDiagnostic);
                        continue;
                    }

                    AddSubTask(resolvedRef!, null);
                }

                loader.ResolveTypeReferenceDirectives(this);
            }

            if (compilerOptions.NoLib != Tristate.TSTrue && !loader._opts.SkipModuleResolution)
            {
                var libReferenceDirectives = file.LibReferenceDirectives ?? [];
                for (var index = 0; index < libReferenceDirectives.Length; index++)
                {
                    var includeReason = new FileIncludeReason
                    {
                        _kind = FileIncludeKind.LibReferenceDirective,
                        _data = new ReferencedFileData
                        {
                            File = _path,
                            Index = index,
                        },
                    };
                    if (TryGetLibFileName(libReferenceDirectives[index].FileName, out var name))
                    {
                        var libFile = loader.PathForLibFile(name);
                        AddSubTask(new ResolvedRef
                        {
                            _fileName = libFile._path,
                            _includeReason = includeReason,
                        }, libFile);
                    }
                    else
                    {
                        _processingDiagnostics.Add(new ProcessingDiagnostic
                        {
                            Kind = ProcessingDiagnosticKind.UnknownReference,
                            Data = includeReason,
                        });
                    }
                }
            }

            loader.ResolveImportsAndModuleAugmentations(this);
            foreach (var supplemental in file.SupplementalSourceFiles())
            {
                _subTasks.Add(new ParseTask
                {
                    _normalizedFilePath = supplemental.FileName(),
                    _file = supplemental,
                    _isContentMapperSupplemental = true,
                    _includeReason = new FileIncludeReason
                    {
                        _kind = FileIncludeKind.ContentMapperSupplemental,
                        _data = _path,
                    },
                });
            }
        }
        finally
        {
            traceDone?.Invoke();
        }
    }

    internal void Redirect(FileLoader loader, string fileName)
    {
        _redirectedParseTask = new ParseTask
        {
            _normalizedFilePath = NormalizePath(fileName),
            _libFile = _libFile,
            _includeReason = _includeReason,
        };
        // increaseDepth and elideOnDepth are not copied to redirects, otherwise their depth would be double counted.
        _subTasks = [_redirectedParseTask];
    }

    internal void LoadAutomaticTypeDirectives(FileLoader loader)
    {
        var traceDone = loader._opts.Tracing?.Push(TracingPhase.Program, "processTypeReferences", null, false);
        try
        {
            var (toParseTypeRefs, typeResolutionsInFile, typeResolutionsTrace, pDiagnostics) =
                loader.ResolveAutomaticTypeDirectives(_normalizedFilePath);
            _typeResolutionsInFile = typeResolutionsInFile;
            _typeResolutionsTrace = typeResolutionsTrace;
            _processingDiagnostics.AddRange(pDiagnostics);
            foreach (var typeResolution in toParseTypeRefs)
            {
                AddSubTask(typeResolution, null);
            }
        }
        finally
        {
            traceDone?.Invoke();
        }
    }

    internal void AddSubTask(ResolvedRef reference, LibFile? libFile)
    {
        var normalizedFilePath = NormalizePath(reference._fileName);
        var subTask = new ParseTask
        {
            _normalizedFilePath = normalizedFilePath,
            _libFile = libFile,
            _increaseDepth = reference._increaseDepth,
            _elideOnDepth = reference._elideOnDepth,
            _includeReason = reference._includeReason,
            _packageId = reference._packageId,
        };
        _subTasks.Add(subTask);
    }
}

/// <summary>Go: <c>type resolvedRef struct</c>。</summary>
internal sealed class ResolvedRef
{
    internal string _fileName = "";
    internal bool _increaseDepth;
    internal bool _elideOnDepth;
    internal FileIncludeReason? _includeReason;
    internal PackageId _packageId;
}

/// <summary>Go: <c>type filesParser struct</c>。</summary>
internal sealed class FilesParser
{
    internal WorkGroup _wg = null!;
    internal readonly SyncMap<Path, ParseTaskData> _taskDataByPath = new();
    internal int _maxDepth;

    // Go: var parseTaskDataPool = sync.Pool{New: func() any { return &parseTaskData{ tasks: make(map[string]*parseTask, 1) } }}
    private static readonly ConcurrentBag<ParseTaskData> _parseTaskDataPool = new();

    private static ParseTaskData GetParseTaskData(ParseTask task)
    {
        if (!_parseTaskDataPool.TryTake(out var td))
        {
            td = new ParseTaskData();
        }

        td._tasks[task._normalizedFilePath] = task;
        td._lowestDepth = int.MaxValue;
        return td;
    }

    private static void PutParseTaskData(ParseTaskData td)
    {
        td._tasks.Clear();
        _parseTaskDataPool.Add(td);
    }

    internal void Parse(FileLoader loader, List<ParseTask> tasks)
    {
        Start(loader, tasks, 0);
        _wg.RunAndWait();
    }

    internal void Start(FileLoader loader, List<ParseTask> tasks, int depth)
    {
        for (var i = 0; i < tasks.Count; i++)
        {
            // C# 的 for 循环变量只有一个实例 → 闭包必须按迭代捕获（Go 的 range 变量每个迭代一份）。
            var index = i;
            var task = tasks[i];
            task._path = loader.ToPath(task._normalizedFilePath);
            var candidate = GetParseTaskData(task);
            var (dataIn, loaded) = _taskDataByPath.LoadOrStore(task._path, candidate);
            if (loaded)
            {
                PutParseTaskData(candidate);
            }

            var data = dataIn!;
            _wg.Queue(() =>
            {
                lock (data._mu)
                {
                    var startSubtasks = false;
                    if (loaded)
                    {
                        if (data._tasks.TryGetValue(task._normalizedFilePath, out var existingTask))
                        {
                            tasks[index]._loadedTask = existingTask;
                        }
                        else
                        {
                            data._tasks[task._normalizedFilePath] = task;
                            // This is new task for file name - so load subtasks if there was loading for any other casing
                            startSubtasks = data._startedSubTasks;
                        }
                    }

                    // Propagate packageId to data if we have one and data doesn't yet
                    if (data._packageId.Name == "" && task._packageId.Name != "")
                    {
                        data._packageId = task._packageId;
                    }

                    var currentDepth = IfElse(task._increaseDepth, depth + 1, depth);
                    if (currentDepth < data._lowestDepth)
                    {
                        // If we're seeing this task at a lower depth than before,
                        // reprocess its subtasks to ensure they are loaded.
                        data._lowestDepth = currentDepth;
                        startSubtasks = true;
                        data._startedSubTasks = true;
                    }

                    if (task._elideOnDepth && currentDepth > _maxDepth)
                    {
                        return;
                    }

                    foreach (var taskByFileName in data._tasks.Values)
                    {
                        var loadSubTasks = startSubtasks;
                        if (!taskByFileName._loaded)
                        {
                            taskByFileName.Load(loader);
                            if (taskByFileName._redirectedParseTask is not null)
                            {
                                // Always load redirected task
                                loadSubTasks = true;
                                data._startedSubTasks = true;
                            }
                        }

                        if (!taskByFileName._startedSubTasks && loadSubTasks)
                        {
                            taskByFileName._startedSubTasks = true;
                            Start(loader, taskByFileName._subTasks, data._lowestDepth);
                        }
                    }
                }
            });
        }
    }

    internal ProcessedFiles GetProcessedFiles(FileLoader loader)
    {
        var totalFileCount = loader._totalFileCount;
        var libFileCount = loader._libFileCount;

        var missingFiles = new List<string>();
        var duplicateSourceFiles = new List<DuplicateSourceFile>();
        var files = new List<SourceFile>(Math.Max(0, totalFileCount - libFileCount));
        // totalFileCount here since we append files to it later to construct the final list
        var libFiles = new List<SourceFile>(totalFileCount);

        var filesByPath = new Dictionary<Path, SourceFile>(totalFileCount);
        // stores 'filename -> file association' ignoring case
        // used to track cases when two file names differ only in casing
        Dictionary<string, ParseTask>? tasksSeenByNameIgnoreCase = null;
        if (loader._comparePathsOptions.UseCaseSensitiveFileNames)
        {
            tasksSeenByNameIgnoreCase = new Dictionary<string, ParseTask>(totalFileCount);
        }

        var includeProcessor = new IncludeProcessor
        {
            _fileIncludeReasons = new Dictionary<Path, List<FileIncludeReason>>(totalFileCount),
        };
        // Go: 只在 !canUseProjectReferenceSource() 时创建（nil map）；nil map 读与空字典一致，故这里一律创建。
        var outputFileToProjectReferenceSource = new Dictionary<Path, string>(totalFileCount);
        var resolvedModules = new Dictionary<Path, ModeAwareCache<ResolvedModule>>(totalFileCount + 1);
        var typeResolutionsInFile = new Dictionary<Path, ModeAwareCache<ResolvedTypeReferenceDirective>>(totalFileCount);
        var sourceFileMetaDatas = new Dictionary<Path, SourceFileMetaData>(totalFileCount);
        Dictionary<Path, JsxRuntimeImportSpecifier>? jsxRuntimeImportSpecifiers = null;
        Dictionary<Path, Node>? importHelpersImportSpecifiers = null;
        var sourceFilesFoundSearchingNodeModules = new Set<Path>();
        var libFilesMap = new Dictionary<Path, LibFile>(libFileCount);

        // 同上：Go 只在 !DeduplicatePackages.IsFalse() 时创建；nil map 下写入也从不发生，故一律创建。
        var redirectTargetsMap = new Dictionary<Path, string[]>();
        Dictionary<Path, RedirectsFile>? redirectFilesByPath = null;
        Dictionary<PackageId, SourceFile>? packageIdToSourceFile = null;
        if (!loader._opts.Config.CompilerOptions()!.DeduplicatePackages.IsFalse())
        {
            packageIdToSourceFile = new Dictionary<PackageId, SourceFile>();
        }

        // recordedDuplicates tracks, per task data, the set of file-name casings that
        // have already been recorded in duplicateSourceFiles. A file that is reached
        // from multiple import sites is walked once per site, but each distinct casing
        // is only parsed and acquired in the parse cache once. Recording the same casing
        // as a duplicate more than once would cause it to be released more times than it
        // was acquired when the snapshot is disposed, leaving a dangling cache entry that
        // panics the next time it is referenced.
        Dictionary<ParseTaskData, Set<string>>? recordedDuplicates = null;

        void CollectFiles(List<ParseTask> tasks, Dictionary<ParseTaskData, string> seen)
        {
            foreach (var taskIn in tasks)
            {
                var task = taskIn;
                var includeReason = task._includeReason;
                // Exclude automatic type directive tasks from include reason processing,
                // as these are internal implementation details and should not contribute
                // to the reasons for including files.
                if (task._redirectedParseTask is null && !task._isForAutomaticTypeDirective)
                {
                    if (task._loadedTask is not null)
                    {
                        task = task._loadedTask;
                    }

                    AddIncludeReason(includeProcessor, task, includeReason!);
                }

                var (dataIn, _) = _taskDataByPath.Load(task._path);
                if (!task._loaded)
                {
                    continue;
                }

                // ensure we only walk each task once
                var data = dataIn!;
                if (seen.TryGetValue(data, out var checkedName))
                {
                    if (task._file is not null && checkedName != task._normalizedFilePath)
                    {
                        recordedDuplicates ??= new Dictionary<ParseTaskData, Set<string>>();
                        if (!recordedDuplicates.TryGetValue(data, out var dups))
                        {
                            dups = new Set<string>();
                            recordedDuplicates[data] = dups;
                        }

                        if (dups.AddIfAbsent(task._normalizedFilePath))
                        {
                            duplicateSourceFiles.Add(CreateDuplicateSourceFile(task._file));
                        }
                    }

                    if (!loader._opts.Config.CompilerOptions()!.ForceConsistentCasingInFileNames.IsFalse())
                    {
                        // Check if it differs only in drive letters its ok to ignore that error:
                        var checkedAbsolutePath = GetNormalizedAbsolutePathWithoutRoot(checkedName, loader._comparePathsOptions.CurrentDirectory);
                        var inputAbsolutePath = GetNormalizedAbsolutePathWithoutRoot(task._normalizedFilePath, loader._comparePathsOptions.CurrentDirectory);
                        if (checkedAbsolutePath != inputAbsolutePath)
                        {
                            includeProcessor.AddProcessingDiagnosticsForFileCasing(task._path, checkedName, task._normalizedFilePath, includeReason!);
                        }
                    }

                    continue;
                }
                else
                {
                    seen[data] = task._normalizedFilePath;
                }

                if (tasksSeenByNameIgnoreCase is not null)
                {
                    var pathLowerCase = ToFileNameLowerCase(task._path.Value);
                    if (tasksSeenByNameIgnoreCase.TryGetValue(pathLowerCase, out var taskByIgnoreCase))
                    {
                        includeProcessor.AddProcessingDiagnosticsForFileCasing(taskByIgnoreCase._path, taskByIgnoreCase._normalizedFilePath, task._normalizedFilePath, includeReason!);
                    }
                    else
                    {
                        tasksSeenByNameIgnoreCase[pathLowerCase] = task;
                    }
                }

                foreach (var trace in task._typeResolutionsTrace)
                {
                    loader._opts.Host.Trace(trace.Message, trace.Args);
                }

                foreach (var trace in task._resolutionsTrace)
                {
                    loader._opts.Host.Trace(trace.Message, trace.Args);
                }

                var file = task._file;
                if (packageIdToSourceFile is not null && data._packageId.Name != "")
                {
                    if (packageIdToSourceFile.TryGetValue(data._packageId, out var packageIdFile))
                    {
                        if (file is not null)
                        {
                            // Package deduplication keeps the first package instance in the
                            // program, but we still parsed this file and acquired it through
                            // the host, so snapshot disposal must release that extra owner.
                            duplicateSourceFiles.Add(CreateDuplicateSourceFile(file));
                        }

                        var packageIdFilePath = packageIdFile.Path();
                        redirectTargetsMap[packageIdFilePath] =
                            [.. redirectTargetsMap.GetValueOrDefault(packageIdFilePath) ?? [], task._normalizedFilePath];
                        redirectFilesByPath ??= new Dictionary<Path, RedirectsFile>(totalFileCount);
                        redirectFilesByPath[task._path] = new RedirectsFile
                        {
                            _index = files.Count + redirectFilesByPath.Count,
                            _fileName = task._normalizedFilePath,
                            _path = task._path,
                            _target = packageIdFilePath,
                        };
                        filesByPath[task._path] = packageIdFile;
                        if (data._lowestDepth > 0)
                        {
                            sourceFilesFoundSearchingNodeModules.Add(task._path);
                        }

                        continue;
                    }
                    else if (file is not null)
                    {
                        packageIdToSourceFile[data._packageId] = file;
                    }
                }

                if (task._subTasks.Count > 0)
                {
                    CollectFiles(task._subTasks, seen);
                }

                // Exclude automatic type directive tasks from include reason processing,
                // as these are internal implementation details and should not contribute
                // to the reasons for including files.
                if (task._redirectedParseTask is not null)
                {
                    if (!loader._opts.CanUseProjectReferenceSource())
                    {
                        outputFileToProjectReferenceSource[task._redirectedParseTask._path] = task.FileName();
                    }

                    continue;
                }

                if (task._isForAutomaticTypeDirective)
                {
                    typeResolutionsInFile[task._path] = task._typeResolutionsInFile ?? new ModeAwareCache<ResolvedTypeReferenceDirective>();
                    if (task._processingDiagnostics.Count > 0)
                    {
                        includeProcessor._processingDiagnostics.AddRange(task._processingDiagnostics);
                    }

                    continue;
                }

                var path = task._path;

                if (task._processingDiagnostics.Count > 0)
                {
                    includeProcessor._processingDiagnostics.AddRange(task._processingDiagnostics);
                }

                if (file is null)
                {
                    missingFiles.Add(task._normalizedFilePath);
                    continue;
                }

                if (task._libFile is not null)
                {
                    libFiles.Add(file);
                    libFilesMap[path] = task._libFile;
                }
                else
                {
                    files.Add(file);
                }

                filesByPath[path] = file;
                resolvedModules[path] = task._resolutionsInFile ?? new ModeAwareCache<ResolvedModule>();
                typeResolutionsInFile[path] = task._typeResolutionsInFile ?? new ModeAwareCache<ResolvedTypeReferenceDirective>();
                sourceFileMetaDatas[path] = task._metadata;

                if (task._jsxRuntimeImportSpecifier is not null)
                {
                    jsxRuntimeImportSpecifiers ??= new Dictionary<Path, JsxRuntimeImportSpecifier>(totalFileCount);
                    jsxRuntimeImportSpecifiers[path] = task._jsxRuntimeImportSpecifier;
                }

                if (task._importHelpersImportSpecifier is not null)
                {
                    importHelpersImportSpecifiers ??= new Dictionary<Path, Node>(totalFileCount);
                    importHelpersImportSpecifiers[path] = task._importHelpersImportSpecifier;
                }

                if (data._lowestDepth > 0)
                {
                    sourceFilesFoundSearchingNodeModules.Add(path);
                }
            }
        }

        CollectFiles(loader._rootTasks, new Dictionary<ParseTaskData, string>(totalFileCount));
        var libFilesArray = libFiles.ToArray();
        loader.SortLibs(libFilesArray);

        var allFiles = new SourceFile[libFilesArray.Length + files.Count];
        libFilesArray.CopyTo(allFiles, 0);
        files.CopyTo(allFiles, libFilesArray.Length);

        if (redirectFilesByPath is not null)
        {
            foreach (var redirectFile in redirectFilesByPath.Values)
            {
                redirectFile._index += libFilesArray.Length;
            }
        }

        var keys = loader._pathForLibFileResolutions.Keys().ToArray();
        Array.Sort(keys, static (a, b) => string.CompareOrdinal(a.Value, b.Value));
        foreach (var key in keys)
        {
            var (value, _) = loader._pathForLibFileResolutions.Load(key);
            // Go 的 libResolution.resolution 是同一条 ResolveModuleName 结果（Go 侧同样假定非 nil）。
            resolvedModules[key] = new ModeAwareCache<ResolvedModule>
            {
                [new ModeAwareCacheKey(value!._libraryName, ModuleKind.CommonJS)] = value!._resolution!,
            };
            foreach (var trace in value!._trace)
            {
                loader._opts.Host.Trace(trace.Message, trace.Args);
            }
        }

        return new ProcessedFiles
        {
            _finishedProcessing = true,
            _resolver = loader._resolver,
            _files = allFiles,
            _duplicateSourceFiles = duplicateSourceFiles.ToArray(),
            _filesByPath = filesByPath,
            _projectReferenceFileMapper = loader._projectReferenceFileMapper!,
            _resolvedModules = resolvedModules,
            _typeResolutionsInFile = typeResolutionsInFile,
            _sourceFileMetaDatas = sourceFileMetaDatas,
            _jsxRuntimeImportSpecifiers = jsxRuntimeImportSpecifiers,
            _importHelpersImportSpecifiers = importHelpersImportSpecifiers,
            _sourceFilesFoundSearchingNodeModules = sourceFilesFoundSearchingNodeModules,
            _libFiles = libFilesMap,
            _missingFiles = missingFiles.ToArray(),
            _includeProcessor = includeProcessor,
            _outputFileToProjectReferenceSource = outputFileToProjectReferenceSource,
            _redirectTargetsMap = redirectTargetsMap,
            _redirectFilesByPath = redirectFilesByPath,
            _contentMapperDiagnostics = [.. loader._contentMapperDiagnostics],
            _moduleResolutionError = loader.ModuleResolutionError,
        };
    }

    private static DuplicateSourceFile CreateDuplicateSourceFile(SourceFile file) => new()
    {
        ParseOptions = file.ParseOptions(),
        ContentMapperParseOptions = file.ContentMapperParseOptions(),
        Hash = file.Hash,
        ScriptKind = file.ScriptKind,
        ContentMapper = file.ContentMapper(),
        IsContentMapperFailureStub = file.IsContentMapperFailureStub(),
    };

    private static void AddIncludeReason(IncludeProcessor includeProcessor, ParseTask task, FileIncludeReason reason)
    {
        if (task._redirectedParseTask is not null)
        {
            AddIncludeReason(includeProcessor, task._redirectedParseTask, reason);
        }
        else if (task._loaded)
        {
            if (includeProcessor._fileIncludeReasons.TryGetValue(task._path, out var existing))
            {
                existing.Add(reason);
            }
            else
            {
                includeProcessor._fileIncludeReasons[task._path] = [reason];
            }
        }
    }
}

/// <summary>Go: <c>type parseTaskData struct</c>。</summary>
internal sealed class ParseTaskData
{
    /// <summary>map of tasks by file casing。</summary>
    internal Dictionary<string, ParseTask> _tasks = new(1);

    internal readonly Lock _mu = new();
    internal int _lowestDepth;
    internal bool _startedSubTasks;
    internal PackageId _packageId;
}
