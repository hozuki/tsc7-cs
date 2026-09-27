// ported from: internal/compiler/fileloader.go:1-688
//   （类型定义：libResolution / LibFile / sourceFileFromReferenceDiagnostic / fileLoader / redirectsFile /
//     DuplicateSourceFile / processedFiles / jsxRuntimeImportSpecifier；loader 方法：toPath / addRootTask /
//     addRootFileTask / addAutomaticTypeDirectiveTasks / resolveAutomaticTypeDirectives / addProjectReferenceTasks /
//     sortLibs / getDefaultLibFilePriority / loadSourceFileMetaData / parseSourceFile / parseContentMappedFile /
//     getContentMapperTransformIdentity / emptyContentMappedFile / contentMapperUnavailable /
//     recordContentMapperInitializationFailure / recordContentMapperFailure / isSupportedExtension）
//   后半（fileloader.go:689-1114：getSourceFileFromReference 起的解析族）在 FileLoader.Resolution.cs。
//
// Go→C# 改写（逐条，都不改变语义）：
// 1) 包级函数（`processAllProgramFiles` 与全部 `ContentMapper*` 诊断构造器、库名/模式辅助函数）→
//    `Compiler` 静态类（见 Compiler.cs 文件头）；`updateFileIncludeProcessor` 在 IncludeProcessor.cs。
// 2) `processedFiles`（纯数据 struct）→ `public sealed partial class ProcessedFiles`（Go 的 Program 内嵌它 →
//    C# 侧 `Program : ProcessedFiles`，由 T10Program 的 Program.cs 声明；字段全部 `internal _camelCase`）。
// 3) `atomic.Int32` 的 totalFileCount/libFileCount → `Interlocked.Increment(ref _field)`（字段是 internal int）。
// 4) `sync.Once` + `moduleResolutionError error` → `Interlocked.Exchange` 标志位 + 字段（Go 的 once.Do 语义：
//    只记录**第一个**错误）。注意：C# 的 `Resolver.ResolveModuleName` 用异常而不是返回值表达失败，所以下面
//    `ResolveImportsAndModuleAugmentations`/`ResolveLibrary` 用 try/catch 复刻 Go 的"记下错误继续跑"。
// 5) `GetContentMappedSourceFiles(...) (SourceFiles, error)` → 只返回 `SourceFiles`，失败抛异常
//    （见 Compiler/Host.cs 文件头第 3 条）；Go 的 `errors.AsType` 会沿 `Unwrap()` 链查找 → C# 用 `Compiler.AsError<T>`
//    （沿 InnerException 链）。
// 6) `FS().ReadFile(path) (string, bool)` → `FS.TryReadFile(path, out contents)`。
// 7) Go 的 nil `ModeAwareCache`（`resolutionsInFile` 在 moduleNames 为空时是 nil）→ C# 空的 `ModeAwareCache`
//    （map 的 nil 与空在只读遍历/查找下行为相同；见 FilesParser.cs 文件头）。
// 8) `strings.Join(core.Flatten(...), "', '")` → `string.Join("', '", Flatten(...))`；`strings.HasPrefix(s, p)` →
//    `HasPrefix(s, p, caseSensitive: true)`（本仓的 HasPrefix 无默认参数）。
// 9) `slices.SortFunc` → `Array.Sort(array, comparison)`：Go 的 pdqsort 与 .NET 的 introsort 在 n<=12/16 时都退化成
//    插入排序（= 稳定），libFiles 的规模正是如此，故两边结果一致。
namespace TypeScript.Compiler;

using System;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using CompilerModule = TypeScript.Module.Module;
using Diagnostic = TypeScript.Ast.Diagnostic;
using Mapper = TypeScript.ContentMapper.Mapper;
using MappingError = TypeScript.SpanMap.MappingError;
using Path = TypeScript.TSPath.Path;
using TracingPhase = TypeScript.Tracing.Phase;

/// <summary>Go: <c>type libResolution struct</c>。</summary>
internal sealed class LibResolution
{
    internal string _libraryName = "";
    internal ResolvedModule? _resolution;
    internal DiagAndArgs[] _trace = [];
}

/// <summary>Go: <c>type LibFile struct</c>（`Name`/`Replaced` 是 Go 的导出字段）。</summary>
public sealed class LibFile
{
    public string Name = "";
    internal string _path = "";
    public bool Replaced;
}

/// <summary>Go: <c>type sourceFileFromReferenceDiagnostic struct</c>。</summary>
internal sealed class SourceFileFromReferenceDiagnostic
{
    internal Message Message = null!;
    internal object?[] Args = [];
}

/// <summary>Go: <c>type fileLoader struct</c>（1-688 行在本文件，689-1114 行在 FileLoader.Resolution.cs）。</summary>
internal sealed partial class FileLoader
{
    internal ProgramOptions _opts = null!;
    internal Resolver _resolver = null!;
    internal string _defaultLibraryPath = "";
    internal ComparePathsOptions _comparePathsOptions;
    internal string[][] _supportedExtensions = [];
    internal string[][] _supportedExtensionsWithJsonIfResolveJsonModule = [];
    internal string[] _contentMapperExtensions = [];

    internal FilesParser _filesParser = null!;
    internal List<ParseTask> _rootTasks = [];

    // Go: totalFileCount atomic.Int32 / libFileCount atomic.Int32
    internal int _totalFileCount;
    internal int _libFileCount;

    internal readonly Lock _factoryMu = new();
    internal NodeFactory _factory = new();

    internal ProjectReferenceFileMapper? _projectReferenceFileMapper;
    internal Set<Path> _dtsDirectories = new();

    internal readonly SyncMap<string, LibFile> _pathForLibFileCache = new();
    internal readonly SyncMap<Path, LibResolution> _pathForLibFileResolutions = new();

    // contentMapperMu guards the content-mapper bookkeeping below, which is written concurrently as
    // content-mapped files are parsed across worker threads.
    internal readonly Lock _contentMapperMu = new();
    internal Dictionary<Mapper, int>? _contentMapperFailures;
    internal Set<Mapper> _contentMapperInitFailed = new();
    internal List<Diagnostic> _contentMapperDiagnostics = [];

    // Go: moduleResolutionErrorOnce sync.Once + moduleResolutionError error
    private Exception? _moduleResolutionError;
    private int _moduleResolutionErrorSet;

    internal Exception? ModuleResolutionError => Volatile.Read(ref _moduleResolutionError);

    /// <summary>Go: <c>p.moduleResolutionErrorOnce.Do(func() { p.moduleResolutionError = err })</c>。</summary>
    private void RecordModuleResolutionError(Exception err)
    {
        if (Interlocked.Exchange(ref _moduleResolutionErrorSet, 1) == 0)
        {
            _moduleResolutionError = err;
        }
    }

    internal Path ToPath(string file) =>
        TypeScript.TSPath.TSPath.ToPath(file, _opts.Host.GetCurrentDirectory(), _opts.Host.FS().UseCaseSensitiveFileNames());

    internal void AddRootTask(string fileName, LibFile? libFile, FileIncludeReason includeReason)
    {
        var absPath = GetNormalizedAbsolutePath(fileName, _opts.Host.GetCurrentDirectory());
        if (_opts.Config.CompilerOptions()!.AllowNonTsExtensions.IsTrue() || HasExtension(absPath))
        {
            _rootTasks.Add(new ParseTask
            {
                _normalizedFilePath = absPath,
                _libFile = libFile,
                _includeReason = includeReason,
            });
        }
    }

    internal void AddRootFileTask(string fileName, LibFile? libFile, FileIncludeReason includeReason)
    {
        var currDir = _opts.Host.GetCurrentDirectory();
        var absPath = GetNormalizedAbsolutePath(fileName, currDir);
        var containingFile = currDir;
        if (_opts.Config.ConfigFile is not null)
        {
            containingFile = GetNormalizedAbsolutePath(_opts.Config.ConfigFile.SourceFile.FileName(), currDir);
        }

        var (resolvedFile, diagnostic) = GetSourceFileFromReference(absPath, fileName, containingFile, includeReason);
        var rootTask = new ParseTask
        {
            _normalizedFilePath = resolvedFile,
            _libFile = libFile,
            _includeReason = includeReason,
        };
        if (diagnostic is not null)
        {
            rootTask._normalizedFilePath = absPath;
            rootTask._failedLookup = true;
            rootTask._processingDiagnostics =
            [
                new ProcessingDiagnostic
                {
                    Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                    Data = new IncludeExplainingDiagnostic
                    {
                        DiagnosticReason = includeReason,
                        Message = diagnostic.Message,
                        Args = diagnostic.Args,
                    },
                },
            ];
        }

        _rootTasks.Add(rootTask);
    }

    internal void AddAutomaticTypeDirectiveTasks()
    {
        string containingDirectory;
        var compilerOptions = _opts.Config.CompilerOptions()!;
        if (compilerOptions.ConfigFilePath != "")
        {
            containingDirectory = GetDirectoryPath(compilerOptions.ConfigFilePath);
        }
        else
        {
            containingDirectory = _opts.Host.GetCurrentDirectory();
        }

        var containingFileName = CombinePaths(containingDirectory, CompilerModule.InferredTypesContainingFile);
        _rootTasks.Add(new ParseTask
        {
            _normalizedFilePath = containingFileName,
            _isForAutomaticTypeDirective = true,
        });
    }

    /// <summary>Go: <c>func (p *fileLoader) resolveAutomaticTypeDirectives(containingFileName string)</c>（四返回值）。</summary>
    internal (
        ResolvedRef[] ToParse,
        ModeAwareCache<ResolvedTypeReferenceDirective> TypeResolutionsInFile,
        DiagAndArgs[] TypeResolutionsTrace,
        ProcessingDiagnostic[] PDiagnostics) ResolveAutomaticTypeDirectives(string containingFileName)
    {
        var toParse = new List<ResolvedRef>();
        var typeResolutionsTrace = new List<DiagAndArgs>();
        var pDiagnostics = new List<ProcessingDiagnostic>();
        var typeResolutionsInFile = new ModeAwareCache<ResolvedTypeReferenceDirective>();

        var automaticTypeDirectiveNames = CompilerModule.GetAutomaticTypeDirectiveNames(_opts.Config.CompilerOptions()!, _opts.Host);
        if (automaticTypeDirectiveNames.Length != 0)
        {
            foreach (var name in automaticTypeDirectiveNames)
            {
                // Under node16/nodenext module resolution, load `types`/ata include names as cjs resolution results by passing an `undefined` mode.
                // Under bundler module resolution, this also triggers the "import" condition to be used.
                var resolutionMode = ResolutionModeNone;
                var (resolvedT, trace) = _resolver.ResolveTypeReferenceDirective(name, containingFileName, resolutionMode, null);
                // Go 的 ResolvedTypeReferenceDirective.IsResolved() 没有 nil 接收者检查 → 解析器保证非 nil。
                var resolved = resolvedT!;
                var traceDone = _opts.Tracing?.Push(
                    TypeScript.Tracing.Phase.Program,
                    "processTypeReferenceDirective",
                    new Dictionary<string, object?>
                    {
                        ["directive"] = name,
                        ["hasResolved"] = resolved.IsResolved(),
                        ["refKind"] = (int)FileIncludeKind.AutomaticTypeDirectiveFile,
                    },
                    false);
                try
                {
                    typeResolutionsInFile[new ModeAwareCacheKey(name, resolutionMode)] = resolved;
                    typeResolutionsTrace.AddRange(trace);
                    if (resolved.IsResolved())
                    {
                        toParse.Add(new ResolvedRef
                        {
                            _fileName = resolved.ResolvedFileName,
                            _increaseDepth = resolved.IsExternalLibraryImport,
                            _elideOnDepth = false,
                            _includeReason = new FileIncludeReason
                            {
                                _kind = FileIncludeKind.AutomaticTypeDirectiveFile,
                                _data = new AutomaticTypeDirectiveFileData
                                {
                                    TypeReference = name,
                                    PackageId = resolved.PackageId,
                                },
                            },
                            _packageId = resolved.PackageId,
                        });
                    }
                    else
                    {
                        pDiagnostics.Add(new ProcessingDiagnostic
                        {
                            Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                            Data = new IncludeExplainingDiagnostic
                            {
                                DiagnosticReason = new FileIncludeReason
                                {
                                    _kind = FileIncludeKind.AutomaticTypeDirectiveFile,
                                    _data = new AutomaticTypeDirectiveFileData { TypeReference = name },
                                },
                                Message = Cannot_find_type_definition_file_for_0,
                                Args = [name],
                            },
                        });
                    }
                }
                finally
                {
                    traceDone?.Invoke();
                }
            }
        }

        return (toParse.ToArray(), typeResolutionsInFile, typeResolutionsTrace.ToArray(), pDiagnostics.ToArray());
    }

    internal void AddProjectReferenceTasks(bool singleThreaded)
    {
        _projectReferenceFileMapper = new ProjectReferenceFileMapper
        {
            _opts = _opts,
            _host = _opts.Host,
        };
        var projectReferences = _opts.Config.ResolvedProjectReferencePaths();
        if (projectReferences.Length == 0)
        {
            return;
        }

        var parser = new ProjectReferenceParser
        {
            _loader = this,
            _wg = NewWorkGroup(singleThreaded),
        };
        var rootTasks = Compiler.CreateProjectReferenceParseTasks(projectReferences);
        parser.Parse(rootTasks);
    }

    internal void SortLibs(SourceFile[] libFiles)
    {
        Array.Sort(libFiles, (f1, f2) => GetDefaultLibFilePriority(f1).CompareTo(GetDefaultLibFilePriority(f2)));
    }

    internal int GetDefaultLibFilePriority(SourceFile a)
    {
        // defaultLibraryPath and a.FileName() are absolute and normalized; a prefix check should suffice.
        var defaultLibraryPath = RemoveTrailingDirectorySeparator(_defaultLibraryPath);
        var aFileName = a.FileName();

        if (HasPrefix(aFileName, defaultLibraryPath, caseSensitive: true)
            && aFileName.Length > defaultLibraryPath.Length
            && aFileName[defaultLibraryPath.Length] == DirectorySeparator)
        {
            // avoid tspath.GetBaseFileName; we know these paths are already absolute and normalized.
            var basename = aFileName[(aFileName.LastIndexOf(DirectorySeparator) + 1)..];
            if (basename == "lib.d.ts" || basename == "lib.es6.d.ts")
            {
                return 0;
            }

            var name = TrimLibPrefixAndDtsSuffix(basename);
            var index = Array.IndexOf(Libs, name);
            if (index != -1)
            {
                return index + 1;
            }
        }

        return Libs.Length + 2;
    }

    // Go: strings.TrimSuffix(strings.TrimPrefix(basename, "lib."), ".d.ts")
    private static string TrimLibPrefixAndDtsSuffix(string basename)
    {
        var name = basename.StartsWith("lib.", StringComparison.Ordinal) ? basename["lib.".Length..] : basename;
        return name.EndsWith(".d.ts", StringComparison.Ordinal) ? name[..^".d.ts".Length] : name;
    }

    internal SourceFileMetaData LoadSourceFileMetaData(string fileName)
    {
        if (_opts.SkipModuleResolution)
        {
            return new SourceFileMetaData
            {
                ImpliedNodeFormat = GetImpliedNodeFormatForFile(fileName, ""),
            };
        }

        var packageJsonScope = _resolver.GetPackageScopeForPath(GetDirectoryPath(fileName));
        var moduleResolutionKind = _opts.Config.CompilerOptions()!.GetModuleResolutionKind();

        var packageJsonType = "";
        var packageJsonDirectory = "";
        if (packageJsonScope is not null && packageJsonScope.Exists())
        {
            packageJsonDirectory = packageJsonScope.PackageDirectory;
            // Go: value, ok := packageJsonScope.Contents.Type.GetValue()（Go 的 HeaderFields 是内嵌提升，C# 必须显式）
            if (packageJsonScope.Contents!.HeaderFields.Type.TryGetValue(out var value))
            {
                if (!FileExtensionIsOneOf(fileName, [ExtensionMts, ExtensionCts, ExtensionMjs, ExtensionCjs])
                    && ModuleResolutionKind.Node16 <= moduleResolutionKind && moduleResolutionKind <= ModuleResolutionKind.NodeNext
                    || fileName.Contains("/node_modules/", StringComparison.Ordinal))
                {
                    packageJsonType = value;
                }
            }
        }

        var impliedNodeFormat = GetImpliedNodeFormatForFile(fileName, packageJsonType);
        return new SourceFileMetaData
        {
            PackageJsonType = packageJsonType,
            PackageJsonDirectory = packageJsonDirectory,
            ImpliedNodeFormat = impliedNodeFormat,
        };
    }

    internal SourceFile? ParseSourceFile(ParseTask t)
    {
        var traceDone = _opts.Tracing?.Push(
            TypeScript.Tracing.Phase.Parse,
            "createSourceFile",
            new Dictionary<string, object?> { ["path"] = t._normalizedFilePath },
            true);
        try
        {
            var path = ToPath(t._normalizedFilePath);
            var options = _projectReferenceFileMapper!.GetCompilerOptionsForFile(t);
            var parseOptions = new SourceFileParseOptions
            {
                FileName = t._normalizedFilePath,
                Path = path,
                ExternalModuleIndicatorOptions = GetExternalModuleIndicatorOptions(t._normalizedFilePath, options, t._metadata),
            };
            if (FileExtensionIsOneOf(t._normalizedFilePath, _contentMapperExtensions))
            {
                return ParseContentMappedFile(parseOptions);
            }

            return _opts.Host.GetSourceFile(parseOptions);
        }
        finally
        {
            traceDone?.Invoke();
        }
    }

    // parseContentMappedFile produces a content-mapped virtual source file via the host's content
    // mapper, preserving the original file name and retaining the untransformed text on the
    // source file. Content mapper extensions only reach the parser when content mappers are configured.
    //
    // When initialization fails, one program diagnostic is reported and the mapper is not attempted for
    // subsequent files. Other failures produce per-file diagnostics and count toward a failure budget; after
    // Compiler.MaxContentMapperFailures, one program diagnostic reports that the mapper was disabled and subsequent
    // files are silently substituted with empty files. It returns null only if the file cannot be read.
    internal SourceFile? ParseContentMappedFile(SourceFileParseOptions opts)
    {
        // 只有文件扩展名命中 contentMapperExtensions 才会走到这里，所以必定有 mapper（Go 同样直接解引用）。
        var mapper = _opts.Config.GetContentMapperForFileName(opts.FileName)!;
        var label = mapper.DiagnosticName();
        var transformIdentity = GetContentMapperTransformIdentity(mapper);
        if (ContentMapperUnavailable(mapper))
        {
            // The mapper failed initialization or exceeded its failure budget; add the file empty without re-reporting.
            return EmptyContentMappedFile(opts, mapper.Identity(), transformIdentity);
        }

        SourceFiles files;
        try
        {
            files = _opts.Host.GetContentMappedSourceFiles(opts, mapper);
        }
        catch (Exception ex)
        {
            var sourceFile = EmptyContentMappedFile(opts, mapper.Identity(), transformIdentity);
            // Go: errors.AsType[*contentmapper.TransformError](err) —— 沿 Unwrap 链。
            if (Compiler.AsError<TransformError>(ex) is { Kind: TransformErrorKind.Initialize } transformError)
            {
                RecordContentMapperInitializationFailure(mapper, label, transformError);
                return sourceFile;
            }

            if (RecordContentMapperFailure(mapper, label))
            {
                Diagnostic diagnostic;
                if (Compiler.AsError<MappingError>(ex) is { } problem)
                {
                    diagnostic = Compiler.ContentMapperMappingDiagnostic(sourceFile, label, problem);
                }
                else
                {
                    diagnostic = Compiler.ContentMapperTransformDiagnostic(sourceFile, label, ex);
                }

                sourceFile.SetDiagnostics([.. sourceFile.Diagnostics(), diagnostic]);
            }

            return sourceFile;
        }

        // Go 的 host 在读不到文件时返回空 SourceFiles（Canonical == nil）。
        return files.Canonical;
    }

    // emptyContentMappedFile produces an empty TypeScript source file for a content-mapped file whose
    // transform could not be used, retaining the original content for diagnostics. Importers see it as an
    // empty module rather than triggering a "cannot find module" error. It is still marked as content-mapped
    // so it is excluded from emit like a successfully mapped file.
    internal string GetContentMapperTransformIdentity(Mapper mapper)
    {
        var project = _opts.Host.ContentMapperProject();
        if (project is not null)
        {
            try
            {
                return project.Identity(mapper);
            }
            catch (Exception)
            {
                // Go: if identity, err := project.Identity(mapper); err == nil { … }
            }
        }

        return Convert.ToHexStringLower(Xxh3.Xxh3.Bytes(mapper.TransformIdentity(_opts.Config.CompilerOptions())));
    }

    internal SourceFile EmptyContentMappedFile(SourceFileParseOptions opts, string mapperIdentity, string transformIdentity)
    {
        _opts.Host.FS().TryReadFile(opts.FileName, out var content);
        var sourceFile = Parser.Parser.ParseSourceFile(opts, "", ScriptKind.TS);
        sourceFile.SetContentMapperInfo(new ContentMapperSourceFileInfo
        {
            ContentMapper = mapperIdentity,
            TransformIdentity = transformIdentity,
            ParseOptions = opts,
            VirtualFileName = opts.FileName + ExtensionTs,
            OriginalText = content,
        });
        return sourceFile;
    }

    // contentMapperUnavailable reports whether mapper failed initialization or exceeded its failure budget.
    internal bool ContentMapperUnavailable(Mapper? mapper)
    {
        if (mapper is null)
        {
            return false;
        }

        lock (_contentMapperMu)
        {
            // Go: `p.contentMapperInitFailed.Has(mapper) || p.contentMapperFailures[mapper] >= maxContentMapperFailures`
            // —— nil map 下标读取返回零值。C# 的 `dict?[key]` 只对 **null 字典**短路，字典非空而键不存在时
            // 仍会抛 KeyNotFoundException，所以必须显式 TryGetValue（等价于 Go 的零值 0）。
            return _contentMapperInitFailed.Has(mapper)
                || (_contentMapperFailures is not null
                    && _contentMapperFailures.TryGetValue(mapper, out var failures)
                    && failures >= Compiler.MaxContentMapperFailures);
        }
    }

    internal void RecordContentMapperInitializationFailure(Mapper mapper, string label, Exception err)
    {
        lock (_contentMapperMu)
        {
            if (_contentMapperInitFailed.Has(mapper))
            {
                return;
            }

            _contentMapperInitFailed.Add(mapper);
            _contentMapperDiagnostics.Add(Compiler.ContentMapperInitializationDiagnostic(label, err));
        }
    }

    // recordContentMapperFailure counts a transform failure for mapper. It returns whether the failure
    // should be reported for this file (false once the mapper is already disabled). On the failure that
    // reaches Compiler.MaxContentMapperFailures it appends a single program diagnostic disabling the mapper.
    internal bool RecordContentMapperFailure(Mapper mapper, string label)
    {
        lock (_contentMapperMu)
        {
            _contentMapperFailures ??= new Dictionary<Mapper, int>();
            if (_contentMapperFailures.GetValueOrDefault(mapper) >= Compiler.MaxContentMapperFailures)
            {
                return false;
            }

            _contentMapperFailures[mapper] = _contentMapperFailures.GetValueOrDefault(mapper) + 1;
            if (_contentMapperFailures[mapper] >= Compiler.MaxContentMapperFailures)
            {
                _contentMapperDiagnostics.Add(NewCompilerDiagnostic(
                    The_content_mapper_0_failed_1_times_and_will_not_be_used,
                    label,
                    Compiler.MaxContentMapperFailures));
            }

            return true;
        }
    }

    internal bool IsSupportedExtension(string canonicalFileName)
    {
        foreach (var group in _supportedExtensionsWithJsonIfResolveJsonModule)
        {
            if (FileExtensionIsOneOf(canonicalFileName, group))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Go: <c>type redirectsFile struct</c>（实现 <see cref="HasFileName"/>）。</summary>
internal sealed class RedirectsFile : HasFileName
{
    /// <summary>Go: <c>index int</c>——这个 redirect 文件需要被遍历到的位置。</summary>
    internal int _index;

    internal string _fileName = "";
    internal Path _path;

    /// <summary>Go: <c>target tspath.Path</c>。</summary>
    internal Path _target;

    public string FileName() => _fileName;

    public Path Path() => _path;
}

/// <summary>Go: <c>type DuplicateSourceFile struct</c>（字段名照 Go 的导出字段）。</summary>
public sealed class DuplicateSourceFile
{
    public SourceFileParseOptions ParseOptions = new();

    /// <summary>ContentMapperParseOptions are the acquire-time options for a content-mapped parse-cache entry.</summary>
    public SourceFileParseOptions ContentMapperParseOptions = new();

    public UInt128 Hash;
    public ScriptKind ScriptKind;

    /// <summary>ContentMapper is the identity of the content mapper that produced this file,
    /// or "" if the file is not content-mapped.</summary>
    public string ContentMapper = "";

    /// <summary>IsContentMapperFailureStub reports whether the file is an empty placeholder
    /// from a failed transform.</summary>
    public bool IsContentMapperFailureStub;
}

/// <summary>Go: <c>type processedFiles struct</c>（纯数据；Go 里被 `Program` 内嵌 → C# 里是 `Program` 的基类，
/// 所以字段一律 `internal`，见 Compiler/Program.cs）。</summary>
public partial class ProcessedFiles
{
    internal Resolver _resolver = null!;
    internal SourceFile[] _files = [];

    /// <summary>duplicateSourceFiles tracks parsed files loaded during program construction
    /// that were later dropped from the final program, such as losing filename
    /// casing variants for the same path or files hidden behind package redirect
    /// deduplication. Their parse-cache acquires still need to be balanced when
    /// the program is disposed.</summary>
    internal DuplicateSourceFile[] _duplicateSourceFiles = [];

    internal Dictionary<Path, SourceFile> _filesByPath = new();
    internal ProjectReferenceFileMapper _projectReferenceFileMapper = null!;
    internal string[] _missingFiles = [];
    internal Dictionary<Path, ModeAwareCache<ResolvedModule>> _resolvedModules = new();
    internal Dictionary<Path, ModeAwareCache<ResolvedTypeReferenceDirective>> _typeResolutionsInFile = new();
    internal Dictionary<Path, SourceFileMetaData> _sourceFileMetaDatas = new();
    internal Dictionary<Path, JsxRuntimeImportSpecifier>? _jsxRuntimeImportSpecifiers;
    internal Dictionary<Path, Node>? _importHelpersImportSpecifiers;
    internal Dictionary<Path, LibFile> _libFiles = new();

    /// <summary>List of present unsupported extensions.</summary>
    internal Set<Path> _sourceFilesFoundSearchingNodeModules = new();

    internal IncludeProcessor _includeProcessor = null!;

    /// <summary>if file was included using source file and its output is actually part of program
    /// this contains mapping from output to source file。</summary>
    internal Dictionary<Path, string> _outputFileToProjectReferenceSource = new();

    /// <summary>Key is a file path. Value is the list of files that redirect to it (same package, different install location)。</summary>
    internal Dictionary<Path, string[]> _redirectTargetsMap = new();

    /// <summary>filesByPath for redirect files.</summary>
    internal Dictionary<Path, RedirectsFile>? _redirectFilesByPath;

    /// <summary>Program-level diagnostics reported when a content mapper fails fatally (reported once per mapper)。</summary>
    internal Diagnostic[] _contentMapperDiagnostics = [];

    internal Exception? _moduleResolutionError;
    internal bool _finishedProcessing;
}

/// <summary>Go: <c>type jsxRuntimeImportSpecifier struct</c>。</summary>
internal sealed class JsxRuntimeImportSpecifier
{
    internal string _moduleReference = "";
    internal Node _specifier = null!;
}
