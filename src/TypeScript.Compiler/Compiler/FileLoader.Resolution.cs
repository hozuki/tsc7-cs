// ported from: internal/compiler/fileloader.go:689-1114
//   （解析族：getSourceFileFromReference / resolveTripleslashPathReference / resolveTypeReferenceDirectives /
//     resolveImportsAndModuleAugmentations / createSyntheticImport / pathForLibFile / resolveLibrary）
//   前一半（1-688）在 FileLoader.cs。
//
// Go→C# 改写（逐条，都不改变语义）：
// 1) Go 的 `(T, *sourceFileFromReferenceDiagnostic)` / `(*resolvedRef, *processingDiagnostic)` → C# 元组。
// 2) `Resolver.ResolveModuleName` 在 C# 用**异常**表达 Go 的 `error`（见 Module/Types.cs 文件头），而 Go 的调用点
//    `if err != nil { p.moduleResolutionErrorOnce.Do(...) }` 是"记下第一个错误、继续跑" → 因此这里用 try/catch +
//    `RecordModuleResolutionError`（与 Go 的 `sync.Once` 屏障同语义：只保留第一个错误）。
// 3) `strings.Join(core.Flatten(…), "', '")` → `string.Join("', '", Flatten(…))`。
// 4) `module.GetCompilerOptionsWithRedirect` → `CompilerModule.GetCompilerOptionsWithRedirect`（裸 `Module.X` 会命中
//    命名空间 `TypeScript.Module`）。
// 5) `tsoptions.GetLibFileName(name) (string, bool)` → `TryGetLibFileName(name, out …)`。
// 6) `ast.NewHasFileName(...)` → `NewHasFileName(...)`（Ast 的包级函数，`using static TypeScript.Ast.Ast;`）。
// 7) `p.factory`/`p.factoryMu` → `_factory`/`lock (_factoryMu)`；`ast.TokenFlagsNone` → `TokenFlags.None`。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using CompilerModule = TypeScript.Module.Module;
using Diagnostic = TypeScript.Ast.Diagnostic;
using TracingPhase = TypeScript.Tracing.Phase;

namespace TypeScript.Compiler;

internal sealed partial class FileLoader
{
    internal (string FileName, SourceFileFromReferenceDiagnostic? Diagnostic) GetSourceFileFromReference(
        string fileName,
        string referenceText,
        string containingFile,
        FileIncludeReason includeReason)
    {
        var options = _opts.Config.CompilerOptions()!;
        var allowNonTsExtensions = options.AllowNonTsExtensions.IsTrue();
        var diagnosticFileName = NormalizeSlashes(referenceText);
        var useCaseSensitiveFileNames = _opts.Host.FS().UseCaseSensitiveFileNames();

        if (HasExtension(fileName))
        {
            var canonicalFileName = GetCanonicalFileName(fileName, useCaseSensitiveFileNames);
            if (!allowNonTsExtensions && !IsSupportedExtension(canonicalFileName))
            {
                if (HasJSFileExtension(canonicalFileName))
                {
                    return ("", new SourceFileFromReferenceDiagnostic
                    {
                        Message = File_0_is_a_JavaScript_file_Did_you_mean_to_enable_the_allowJs_option,
                        Args = [diagnosticFileName],
                    });
                }

                return ("", new SourceFileFromReferenceDiagnostic
                {
                    Message = File_0_has_an_unsupported_extension_The_only_supported_extensions_are_1,
                    Args = [diagnosticFileName, "'" + string.Join("', '", Flatten(_supportedExtensions)) + "'"],
                });
            }

            if (!_opts.Host.FS().FileExists(fileName))
            {
                return ("", new SourceFileFromReferenceDiagnostic
                {
                    Message = File_0_not_found,
                    Args = [diagnosticFileName],
                });
            }

            if (includeReason.IsReferencedFile() && GetCanonicalFileName(containingFile, useCaseSensitiveFileNames) == canonicalFileName)
            {
                return ("", new SourceFileFromReferenceDiagnostic
                {
                    Message = A_file_cannot_have_a_reference_to_itself,
                });
            }

            return (fileName, null);
        }

        if (allowNonTsExtensions && _opts.Host.FS().FileExists(fileName))
        {
            return (fileName, null);
        }

        if (allowNonTsExtensions)
        {
            return ("", new SourceFileFromReferenceDiagnostic
            {
                Message = File_0_not_found,
                Args = [diagnosticFileName],
            });
        }

        foreach (var ext in _supportedExtensions[0])
        {
            var candidate = fileName + ext;
            if (_opts.Host.FS().FileExists(candidate))
            {
                return (candidate, null);
            }
        }

        return ("", new SourceFileFromReferenceDiagnostic
        {
            Message = Could_not_resolve_the_path_0_with_the_extensions_Colon_1,
            Args = [diagnosticFileName, "'" + string.Join("', '", Flatten(_supportedExtensions)) + "'"],
        });
    }

    internal (ResolvedRef? Reference, ProcessingDiagnostic? Diagnostic) ResolveTripleslashPathReference(
        string moduleName,
        string containingFile,
        int index)
    {
        var basePath = GetDirectoryPath(containingFile);
        var referencedFileName = moduleName;

        if (!IsRootedDiskPath(moduleName))
        {
            referencedFileName = CombinePaths(basePath, moduleName);
        }

        var normalizedFileName = NormalizePath(referencedFileName);
        var includeReason = new FileIncludeReason
        {
            _kind = FileIncludeKind.ReferenceFile,
            _data = new ReferencedFileData
            {
                File = ToPath(containingFile),
                Index = index,
            },
        };

        var (resolvedFileName, diagnostic) = GetSourceFileFromReference(
            normalizedFileName,
            moduleName,
            containingFile,
            includeReason);
        if (diagnostic is not null)
        {
            return (null, new ProcessingDiagnostic
            {
                Kind = ProcessingDiagnosticKind.ExplainingFileInclude,
                Data = new IncludeExplainingDiagnostic
                {
                    DiagnosticReason = includeReason,
                    Message = diagnostic.Message,
                    Args = diagnostic.Args,
                },
            });
        }

        return (new ResolvedRef
        {
            _fileName = resolvedFileName,
            _includeReason = includeReason,
        }, null);
    }

    internal void ResolveTypeReferenceDirectives(ParseTask t)
    {
        var file = t._file!;
        if (file.TypeReferenceDirectives is not { Length: > 0 })
        {
            return;
        }

        var traceDone = _opts.Tracing?.Push(
            TracingPhase.Program,
            "resolveTypeReferenceDirectiveNamesWorker",
            new Dictionary<string, object?> { ["containingFileName"] = file.FileName() },
            false);
        try
        {
            var meta = t._metadata;
            var typeResolutionsInFile = new ModeAwareCache<ResolvedTypeReferenceDirective>();
            var typeResolutionsTrace = new List<DiagAndArgs>();
            var typeReferenceDirectives = file.TypeReferenceDirectives;
            for (var index = 0; index < typeReferenceDirectives.Length; index++)
            {
                var reference = typeReferenceDirectives[index];
                var (redirect, fileName) = _projectReferenceFileMapper!.GetRedirectForResolution(file);
                var resolutionMode = Compiler.GetModeForTypeReferenceDirectiveInFile(
                    reference,
                    file,
                    meta,
                    CompilerModule.GetCompilerOptionsWithRedirect(_opts.Config.CompilerOptions()!, redirect));
                var (resolvedIn, trace) = _resolver.ResolveTypeReferenceDirective(reference.FileName, fileName, resolutionMode, redirect);
                // Go 的 ResolvedTypeReferenceDirective.IsResolved() 没有 nil 接收者检查 → 解析器保证非 nil。
                var resolved = resolvedIn!;
                var perRefTraceDone = _opts.Tracing?.Push(
                    TracingPhase.Program,
                    "processTypeReferenceDirective",
                    new Dictionary<string, object?>
                    {
                        ["directive"] = reference.FileName,
                        ["hasResolved"] = resolved.IsResolved(),
                        ["refKind"] = (int)FileIncludeKind.TypeReferenceDirective,
                        ["refPath"] = t._path.Value,
                    },
                    false);
                try
                {
                    typeResolutionsInFile[new ModeAwareCacheKey(reference.FileName, resolutionMode)] = resolved;
                    var includeReason = new FileIncludeReason
                    {
                        _kind = FileIncludeKind.TypeReferenceDirective,
                        _data = new ReferencedFileData
                        {
                            File = t._path,
                            Index = index,
                        },
                    };
                    typeResolutionsTrace.AddRange(trace);

                    if (resolved.IsResolved())
                    {
                        t.AddSubTask(new ResolvedRef
                        {
                            _fileName = resolved.ResolvedFileName,
                            _increaseDepth = resolved.IsExternalLibraryImport,
                            _elideOnDepth = false,
                            _includeReason = includeReason,
                            _packageId = resolved.PackageId,
                        }, null);
                    }
                    else
                    {
                        t._processingDiagnostics.Add(new ProcessingDiagnostic
                        {
                            Kind = ProcessingDiagnosticKind.UnknownReference,
                            Data = includeReason,
                        });
                    }
                }
                finally
                {
                    perRefTraceDone?.Invoke();
                }
            }

            t._typeResolutionsInFile = typeResolutionsInFile;
            t._typeResolutionsTrace = typeResolutionsTrace.ToArray();
        }
        finally
        {
            traceDone?.Invoke();
        }
    }

    internal void ResolveImportsAndModuleAugmentations(ParseTask t)
    {
        var traceDone = _opts.Tracing?.Push(
            TracingPhase.Program,
            "resolveModuleNamesWorker",
            new Dictionary<string, object?> { ["containingFileName"] = t._file!.FileName() },
            false);
        try
        {
            var file = t._file!;
            var meta = t._metadata;

            var moduleNames = new List<Node>(file.Imports().Length + (file.ModuleAugmentations?.Length ?? 0) + 2);

            var isJavaScriptFile = IsSourceFileJS(file);
            var isExternalModuleFile = IsExternalModule(file);

            var (redirect, fileName) = _projectReferenceFileMapper!.GetRedirectForResolution(file);
            var optionsForFile = CompilerModule.GetCompilerOptionsWithRedirect(_opts.Config.CompilerOptions()!, redirect);
            if (isJavaScriptFile || (!file.IsDeclarationFile && (optionsForFile.GetIsolatedModules() || isExternalModuleFile)))
            {
                if (optionsForFile.ImportHelpers.IsTrue())
                {
                    var specifier = CreateSyntheticImport(Compiler.ExternalHelpersModuleNameText, file);
                    moduleNames.Add(specifier);
                    t._importHelpersImportSpecifier = specifier;
                }
            }

            if (isJavaScriptFile || file.ScriptKind == ScriptKind.TSX)
            {
                var jsxImport = GetJSXRuntimeImport(GetJSXImplicitImportBase(optionsForFile, file), optionsForFile);
                if (jsxImport != "")
                {
                    var specifier = CreateSyntheticImport(jsxImport, file);
                    moduleNames.Add(specifier);
                    t._jsxRuntimeImportSpecifier = new JsxRuntimeImportSpecifier
                    {
                        _moduleReference = jsxImport,
                        _specifier = specifier,
                    };
                }
            }

            var importsStart = moduleNames.Count;

            moduleNames.AddRange(file.Imports());
            foreach (var imp in file.ModuleAugmentations ?? [])
            {
                if (imp.Kind == Kind.StringLiteral)
                {
                    moduleNames.Add(imp);
                }
                // Do nothing if it's an Identifier; we don't need to do module resolution for `declare global`.
            }

            if (_opts.SkipModuleResolution)
            {
                return;
            }

            if (moduleNames.Count != 0)
            {
                var resolutionsInFile = new ModeAwareCache<ResolvedModule>();
                var resolutionsTrace = new List<DiagAndArgs>();
                var imports = file.Imports();

                for (var index = 0; index < moduleNames.Count; index++)
                {
                    var entry = moduleNames[index];
                    var moduleName = entry.Text();
                    if (moduleName == "")
                    {
                        continue;
                    }

                    var mode = Compiler.GetModeForUsageLocation(file.FileName(), meta, entry, optionsForFile);
                    ResolvedModule? resolvedModule;
                    DiagAndArgs[] trace;
                    try
                    {
                        (resolvedModule, trace) = _resolver.ResolveModuleName(moduleName, fileName, mode, redirect);
                    }
                    catch (Exception ex)
                    {
                        RecordModuleResolutionError(ex);
                        resolvedModule = null;
                        trace = [];
                    }

                    resolvedModule ??= new ResolvedModule();
                    resolutionsInFile[new ModeAwareCacheKey(moduleName, mode)] = resolvedModule;
                    resolutionsTrace.AddRange(trace);

                    if (!resolvedModule.IsResolved())
                    {
                        continue;
                    }

                    var resolvedFileName = resolvedModule.ResolvedFileName;
                    var isFromNodeModulesSearch = resolvedModule.IsExternalLibraryImport;
                    // Don't treat redirected files as JS files.
                    var isJsFile = !resolvedModule.ResolvedUsingExtraExtensions
                        && !FileExtensionIsOneOf(resolvedFileName, SupportedTSExtensionsWithJsonFlat)
                        && _projectReferenceFileMapper.GetRedirectParsedCommandLineForResolution(
                            NewHasFileName(resolvedFileName, ToPath(resolvedFileName))) is null;
                    var isJsFileFromNodeModules = isFromNodeModulesSearch && isJsFile && resolvedFileName.Contains("/node_modules/", StringComparison.Ordinal);

                    // add file to program only if:
                    // - resolution was successful
                    // - noResolve is falsy
                    // - module name comes from the list of imports
                    // - it's not a top level JavaScript module that exceeded the search max

                    var importIndex = index - importsStart;

                    var shouldAddFile = moduleName != ""
                        && CompilerModule.GetResolutionDiagnostic(optionsForFile, resolvedModule, file) is null
                        && !optionsForFile.NoResolve.IsTrue()
                        && !(isJsFile && !optionsForFile.GetAllowJS())
                        && (importIndex < 0 || (importIndex < imports.Length && (IsInJSFile(imports[importIndex]) || (imports[importIndex].Flags & NodeFlags.JSDoc) == 0)));

                    if (shouldAddFile)
                    {
                        t.AddSubTask(new ResolvedRef
                        {
                            _fileName = resolvedFileName,
                            _increaseDepth = resolvedModule.IsExternalLibraryImport,
                            _elideOnDepth = isJsFileFromNodeModules,
                            _includeReason = new FileIncludeReason
                            {
                                _kind = FileIncludeKind.Import,
                                _data = new ReferencedFileData
                                {
                                    File = t._path,
                                    Index = importIndex,
                                    Synthetic = importIndex < 0 ? entry : null,
                                },
                            },
                            _packageId = resolvedModule.PackageId,
                        }, null);
                    }
                }

                t._resolutionsInFile = resolutionsInFile;
                t._resolutionsTrace = resolutionsTrace.ToArray();
            }
        }
        finally
        {
            traceDone?.Invoke();
        }
    }

    internal Node CreateSyntheticImport(string text, SourceFile file)
    {
        lock (_factoryMu)
        {
            var externalHelpersModuleReference = _factory.NewStringLiteral(text, TokenFlags.None);
            var importDecl = _factory.NewImportDeclaration(null, null, externalHelpersModuleReference, null);
            externalHelpersModuleReference.Parent = importDecl;
            importDecl.Parent = file.AsNode();
            return externalHelpersModuleReference;
        }
    }

    internal LibFile PathForLibFile(string name)
    {
        var (cached, cachedOk) = _pathForLibFileCache.Load(name);
        if (cachedOk)
        {
            return cached!;
        }

        var path = CombinePaths(_defaultLibraryPath, name);
        var replaced = false;
        if (!_opts.SkipModuleResolution && _opts.Config.CompilerOptions()!.LibReplacement.IsTrue() && name != "lib.d.ts")
        {
            var libraryName = Compiler.GetLibraryNameFromLibFileName(name);
            var resolveFrom = Compiler.GetInferredLibraryNameResolveFrom(_opts.Config.CompilerOptions()!, _opts.Host.GetCurrentDirectory(), name);
            var (resolution, trace) = ResolveLibrary(libraryName, resolveFrom);
            if (resolution is not null && resolution.IsResolved())
            {
                path = resolution.ResolvedFileName;
                replaced = true;
            }

            _pathForLibFileResolutions.LoadOrStore(ToPath(resolveFrom), new LibResolution
            {
                _libraryName = libraryName,
                _resolution = resolution,
                _trace = trace,
            });
        }

        var (libPath, _) = _pathForLibFileCache.LoadOrStore(name, new LibFile
        {
            Name = name,
            _path = path,
            Replaced = replaced,
        });
        return libPath!;
    }

    internal (ResolvedModule? Resolution, DiagAndArgs[] Trace) ResolveLibrary(string libraryName, string resolveFrom)
    {
        var traceDone = _opts.Tracing?.Push(
            TracingPhase.Program,
            "resolveLibrary",
            new Dictionary<string, object?> { ["resolveFrom"] = resolveFrom },
            false);
        try
        {
            try
            {
                return _resolver.ResolveModuleName(libraryName, resolveFrom, ModuleKind.CommonJS, null);
            }
            catch (Exception ex)
            {
                RecordModuleResolutionError(ex);
                return (null, []);
            }
        }
        finally
        {
            traceDone?.Invoke();
        }
    }
}
