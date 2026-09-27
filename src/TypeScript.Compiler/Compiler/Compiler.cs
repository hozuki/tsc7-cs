// ported from: internal/compiler/fileloader.go:26-34,152-228,467-678,1000-1114
//   （包级：libResolution 前的常量 `maxContentMapperFailures`、`processAllProgramFiles`、
//     全部 `ContentMapper*` 诊断构造器、`getLibraryNameFromLibFileName`、`getInferredLibraryNameResolveFrom`、
//     `getModeForTypeReferenceDirectiveInFile`、`getDefaultResolutionModeForFile`、`getModeForUsageLocation`、
//     `importSyntaxAffectsModuleResolution`、`getEmitSyntaxForUsageLocationWorker`）
//   `externalHelpersModuleNameText` 也在本文件（fileloader.go:828）。
//
// 与 Go 的偏离（逐条）：
// 1) 包级函数集中在与本包同名的静态类 `Compiler`（docs/architecture.md §2）；Go 导出函数保持 `public`，
//    未导出的是 `internal`。`processAllProgramFiles` 的调用点是 T10Program 的 `Program.cs`。
// 2) `errors.AsType[*T](err)` **沿 `Unwrap()` 链**查找 → C# 的 `AsError<T>(err)` 沿 `InnerException` 链查找
//    （ContentMapper 的 `TransformError`/`ProjectError` 把内层异常放在 `InnerException`，与 Go 的 `Unwrap` 对应）。
// 3) `Arena`/`fmt.Sprintf("%x", …Bytes())` → `Convert.ToHexStringLower(Xxh3.Xxh3.Bytes(...))`
//    （同 `ContentMapper.HostImpl.cs` 的 `combinedIdentity`，Go 的 `%x` 就是小写十六进制字节序列）。
// 4) `usage.Parent` 在 C# 是 `Node?`，Go 直接解引用 → 写 `usage.Parent!`。
using System.Text;
using TypeScript.Ast;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.TsOptions;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using CompilerModule = TypeScript.Module.Module;
using Diagnostic = TypeScript.Ast.Diagnostic;
using Mapper = TypeScript.ContentMapper.Mapper;
using MappingError = TypeScript.SpanMap.MappingError;
using MappingErrorKind = TypeScript.SpanMap.MappingErrorKind;
using Path = TypeScript.TSPath.Path;
using TracingPhase = TypeScript.Tracing.Phase;
using TypeScript.TSPath;

namespace TypeScript.Compiler;

public static partial class Compiler
{
    /// <summary>Go: <c>maxContentMapperFailures</c>——一个 content mapper 在被停用前允许的变换失败次数。</summary>
    internal const int MaxContentMapperFailures = 5;

    /// <summary>Go: <c>externalHelpersModuleNameText</c>（fileloader.go:828，TODO(jakebailey): dedupe）。</summary>
    internal const string ExternalHelpersModuleNameText = "tslib";

    /// <summary>
    /// Go: <c>func errors.AsType[T](err) (T, bool)</c>——沿 <c>Unwrap()</c> 链查找。C# 里 <c>error</c> 是异常，
    /// 内层原因放在 <see cref="Exception.InnerException"/>，所以这里沿该链找第一个 <typeparamref name="T"/>。
    /// </summary>
    internal static T? AsError<T>(Exception? err)
        where T : Exception
    {
        for (var current = err; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Go: <c>func processAllProgramFiles(opts ProgramOptions, singleThreaded bool) processedFiles</c>（fileloader.go:152）。</summary>
    internal static ProcessedFiles ProcessAllProgramFiles(ProgramOptions opts, bool singleThreaded)
    {
        var compilerOptions = opts.Config.CompilerOptions()!;
        var rootFiles = opts.Config.FileNames();
        var supportedExtensions = GetSupportedExtensions(compilerOptions, opts.Config.ContentMapperExtensions());
        var supportedExtensionsWithJsonIfResolveJsonModule = GetSupportedExtensionsWithJsonIfResolveJsonModule(compilerOptions, supportedExtensions);
        var maxNodeModuleJsDepth = compilerOptions.MaxNodeModuleJsDepth ?? 0;
        var loader = new FileLoader
        {
            _opts = opts,
            _defaultLibraryPath = GetNormalizedAbsolutePath(opts.Host.DefaultLibraryPath(), opts.Host.GetCurrentDirectory()),
            _comparePathsOptions = new ComparePathsOptions(
                opts.Host.FS().UseCaseSensitiveFileNames(),
                opts.Host.GetCurrentDirectory()),
            _filesParser = new FilesParser
            {
                _wg = NewWorkGroup(singleThreaded),
                _maxDepth = maxNodeModuleJsDepth,
            },
            _rootTasks = new List<ParseTask>(rootFiles.Length + (compilerOptions.Lib?.Length ?? 0)),
            _supportedExtensions = supportedExtensions,
            _supportedExtensionsWithJsonIfResolveJsonModule = supportedExtensionsWithJsonIfResolveJsonModule,
            _contentMapperExtensions = opts.Config.ContentMapperExtensions(),
        };
        loader.AddProjectReferenceTasks(singleThreaded);
        var resolverOptions = new ResolverOptions
        {
            Host = loader._projectReferenceFileMapper!._host!,
            CompilerOptions = compilerOptions,
            TypingsLocation = opts.TypingsLocation,
            ProjectName = opts.ProjectName,
            ExtraExtensions = opts.Config.ContentMapperExtensions(),
        };
        loader._resolver = opts.CreateModuleResolver is not null
            ? opts.CreateModuleResolver(resolverOptions)
            : CompilerModule.NewResolver(resolverOptions);

        var traceDone = opts.Tracing?.Push(
            TracingPhase.Program,
            "processRootFiles",
            new Dictionary<string, object?> { ["count"] = rootFiles.Length },
            false);
        try
        {
            for (var index = 0; index < rootFiles.Length; index++)
            {
                loader.AddRootFileTask(rootFiles[index], null, new FileIncludeReason
                {
                    _kind = FileIncludeKind.RootFile,
                    _data = index,
                });
            }

            if (rootFiles.Length > 0 && compilerOptions.NoLib.IsFalseOrUnknown())
            {
                if (compilerOptions.Lib is null)
                {
                    var name = GetDefaultLibFileName(compilerOptions);
                    var libFile = loader.PathForLibFile(name);
                    loader.AddRootTask(libFile._path, libFile, new FileIncludeReason { _kind = FileIncludeKind.LibFile });
                }
                else
                {
                    for (var index = 0; index < compilerOptions.Lib.Length; index++)
                    {
                        if (TryGetLibFileName(compilerOptions.Lib[index], out var name))
                        {
                            var libFile = loader.PathForLibFile(name);
                            loader.AddRootTask(libFile._path, libFile, new FileIncludeReason
                            {
                                _kind = FileIncludeKind.LibFile,
                                _data = index,
                            });
                        }
                        // !!! error on unknown name
                    }
                }
            }

            if (rootFiles.Length > 0 && !opts.SkipModuleResolution)
            {
                loader.AddAutomaticTypeDirectiveTasks();
            }

            loader._filesParser.Parse(loader, loader._rootTasks);

            // Clear out loader and host to ensure its not used post program creation
            loader._projectReferenceFileMapper!._loader = null;
            loader._projectReferenceFileMapper!._host = null;

            return loader._filesParser.GetProcessedFiles(loader);
        }
        finally
        {
            traceDone?.Invoke();
        }
    }

    /// <summary>Go: <c>func contentMapperTransformDiagnostic(file *ast.SourceFile, label string, err error) *ast.Diagnostic</c>（fileloader.go:467）。</summary>
    internal static Diagnostic ContentMapperTransformDiagnostic(SourceFile file, string label, Exception err)
    {
        if (AsError<SupplementalFileCollisionError>(err) is { } collision)
        {
            return ContentMapperTransformDiagnosticChain(file, label, Content_mapper_supplemental_output_file_0_conflicts_with_an_existing_file, collision.FileName);
        }

        if (AsError<TransformError>(err) is { } transformError)
        {
            switch (transformError.Kind)
            {
                case TransformErrorKind.Initialize:
                    if (AsError<InitializeError>(transformError) is { } initializeError)
                    {
                        switch (initializeError.Kind)
                        {
                            case InitializeErrorKind.PositionEncoding:
                                return ContentMapperTransformDiagnosticChain(file, label, The_content_mapper_selected_unsupported_position_encoding_0, initializeError.PositionEncoding);
                            case InitializeErrorKind.EmptyDiagnosticSource:
                                return ContentMapperTransformDiagnosticChain(file, label, The_content_mapper_diagnostic_source_must_not_be_empty);
                            case InitializeErrorKind.ReservedDiagnosticSource:
                                return ContentMapperTransformDiagnosticChain(file, label, The_content_mapper_diagnostic_source_0_is_reserved_by_TypeScript, initializeError.DiagnosticSource);
                        }
                    }

                    return ContentMapperTransformDiagnosticChain(file, label, The_content_mapper_process_could_not_be_started_or_initialized);
                case TransformErrorKind.Project:
                    return ContentMapperTransformDiagnosticChain(file, label, ContentMapperProjectErrorDiagnostic(transformError));
                case TransformErrorKind.Request:
                    return ContentMapperTransformDiagnosticChain(file, label, The_content_mapper_process_failed_while_handling_the_transform_request);
                case TransformErrorKind.Response:
                    if (AsError<InvalidVirtualExtensionError>(transformError) is { } extensionError)
                    {
                        return ContentMapperTransformDiagnosticChain(file, label, The_content_mapper_returned_an_output_with_unsupported_virtual_extension_0, extensionError.Extension);
                    }

                    if (AsError<DiagnosticDirectiveError>(transformError) is { } directiveError)
                    {
                        Diagnostic? detail = null;
                        switch (directiveError.Kind)
                        {
                            case DiagnosticDirectiveErrorKind.InvalidRange:
                                detail = NewCompilerDiagnostic(Diagnostic_directive_0_returned_by_the_content_mapper_has_an_invalid_range, directiveError.Index);
                                break;
                            case DiagnosticDirectiveErrorKind.InvalidPolicy:
                                detail = NewCompilerDiagnostic(The_content_mapper_returned_a_diagnostic_directive_with_invalid_policy_0, directiveError.Policy);
                                break;
                            case DiagnosticDirectiveErrorKind.ExpectMissingUnusedDiagnostic:
                                detail = NewCompilerDiagnostic(Diagnostic_directive_0_returned_by_the_content_mapper_must_specify_unusedExpectDirectiveIndex_when_there_is_not_exactly_one_unusedExpectDirectiveDiagnostics_entry, directiveError.Index);
                                break;
                            case DiagnosticDirectiveErrorKind.InvalidUnusedDiagnosticIndex:
                                detail = NewCompilerDiagnostic(Diagnostic_directive_0_returned_by_the_content_mapper_has_an_invalid_unusedExpectDirectiveIndex, directiveError.Index);
                                break;
                            case DiagnosticDirectiveErrorKind.Overlap:
                                detail = NewCompilerDiagnostic(The_content_mapper_returned_diagnostic_directives_with_overlapping_virtual_ranges);
                                break;
                        }

                        if (detail is not null)
                        {
                            if (directiveError.SupplementalIndex >= 0)
                            {
                                detail = NewDiagnosticChain(detail, The_invalid_diagnostic_directive_is_in_supplemental_output_0_returned_by_the_content_mapper, directiveError.SupplementalIndex);
                            }

                            return ContentMapperTransformDiagnosticWithDetail(file, label, detail);
                        }
                    }

                    return ContentMapperTransformDiagnosticChain(file, label, The_content_mapper_returned_an_invalid_transform_response);
                case TransformErrorKind.Mappings:
                    return NewDiagnostic(file, NewTextRange(0, 0), The_content_mapper_0_did_not_provide_the_required_position_mappings, label);
            }
        }

        return NewDiagnostic(file, NewTextRange(0, 0), The_content_mapper_0_failed_to_transform_this_file, label);
    }

    /// <summary>ContentMapperProjectErrorDiagnostic returns the localized diagnostic message for a project setup error.</summary>
    /// <remarks>Go: <c>func ContentMapperProjectErrorDiagnostic(err error) *diagnostics.Message</c>（fileloader.go:523）。</remarks>
    public static Message ContentMapperProjectErrorDiagnostic(Exception err)
    {
        if (AsError<ProjectError>(err) is { } projectError)
        {
            switch (projectError.Kind)
            {
                case ProjectErrorKind.MalformedResponse:
                    return The_content_mapper_returned_a_project_response_that_could_not_be_decoded;
                case ProjectErrorKind.MissingConfigIdentity:
                    return The_content_mapper_did_not_return_configIdentity_which_is_required_when_the_content_mapper_has_dynamicConfig_Colon_true_in_its_package_json;
                case ProjectErrorKind.NonAbsoluteWatchedFile:
                    return The_content_mapper_returned_a_non_absolute_path_in_watchedFiles;
                case ProjectErrorKind.UnexpectedConfigIdentity:
                    return The_content_mapper_returned_configIdentity_which_is_only_allowed_when_it_declares_dynamicConfig_Colon_true_in_its_package_json;
                case ProjectErrorKind.UnexpectedWatchedFiles:
                    return The_content_mapper_returned_watchedFiles_which_is_only_allowed_when_it_declares_dynamicConfig_Colon_true_in_its_package_json;
            }
        }

        return The_content_mapper_process_failed_while_handling_the_project_request;
    }

    internal static Diagnostic ContentMapperTransformDiagnosticChain(SourceFile file, string label, Message message, params object?[] args) =>
        ContentMapperTransformDiagnosticWithDetail(file, label, NewCompilerDiagnostic(message, args));

    internal static Diagnostic ContentMapperTransformDiagnosticWithDetail(SourceFile file, string label, Diagnostic detail) =>
        NewDiagnostic(
            file,
            NewTextRange(0, 0),
            The_content_mapper_0_failed_to_transform_this_file,
            label).AddMessageChain(detail);

    // contentMapperMappingDiagnostic builds the diagnostic reported against a mapper that produced an
    // invalid span map, including the offsets involved so the mapper's author can locate the problem.
    internal static Diagnostic ContentMapperMappingDiagnostic(SourceFile file, string label, MappingError problem)
    {
        var loc = NewTextRange(0, 0);
        switch (problem.Kind)
        {
            case MappingErrorKind.Overlap:
                return NewDiagnostic(file, loc, The_content_mapper_0_produced_overlapping_or_out_of_order_position_mappings_near_virtual_offset_1, label, (int)problem.VirtualPos);
            case MappingErrorKind.OutOfBounds:
                return NewDiagnostic(file, loc, The_content_mapper_0_produced_a_position_mapping_that_points_outside_the_original_content_original_offset_1, label, (int)problem.OriginalPos);
            case MappingErrorKind.VerbatimMismatch:
                return NewDiagnostic(file, loc, The_content_mapper_0_produced_a_verbatim_mapping_that_does_not_match_the_original_content_virtual_offset_1_original_offset_2, label, (int)problem.VirtualPos, (int)problem.OriginalPos);
            case MappingErrorKind.Kind:
                return NewDiagnostic(file, loc, The_content_mapper_0_produced_a_position_mapping_with_an_invalid_kind_near_virtual_offset_1, label, (int)problem.VirtualPos);
            case MappingErrorKind.Feature:
                return NewDiagnostic(file, loc, The_content_mapper_0_produced_invalid_mapping_features_near_original_offset_1, label, (int)problem.OriginalPos);
            default:
                return NewDiagnostic(file, loc, The_content_mapper_0_did_not_provide_the_required_position_mappings, label);
        }
    }

    /// <summary>ContentMapperInitializationDiagnostic returns a fileless diagnostic for a mapper initialization failure.</summary>
    /// <remarks>Go: <c>func ContentMapperInitializationDiagnostic(label string, err error) *ast.Diagnostic</c>（fileloader.go:601）。</remarks>
    public static Diagnostic ContentMapperInitializationDiagnostic(string label, Exception err)
    {
        if (AsError<InitializeError>(err) is { } namedError && label == "")
        {
            label = namedError.MapperName;
        }

        var diagnostic = NewCompilerDiagnostic(The_content_mapper_0_could_not_be_initialized, label);
        if (AsError<InitializeError>(err) is { } initializeError)
        {
            switch (initializeError.Kind)
            {
                case InitializeErrorKind.ProcessStart:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_command_0_could_not_be_started_Colon_1, initializeError.Command, initializeError.Detail));
                case InitializeErrorKind.ProcessExit:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_process_exited_before_responding_to_the_initialize_request_exit_code_0, initializeError.ExitCode));
                case InitializeErrorKind.NoResponse:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_did_not_respond_to_the_initialize_request_within_0_seconds, initializeError.TimeoutSeconds));
                case InitializeErrorKind.InvalidResponse:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_returned_an_initialize_response_that_could_not_be_decoded_Colon_0, initializeError.Detail));
                case InitializeErrorKind.Request:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_s_initialize_request_failed_Colon_0, initializeError.Detail));
                case InitializeErrorKind.PositionEncoding:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_selected_unsupported_position_encoding_0, initializeError.PositionEncoding));
                case InitializeErrorKind.EmptyDiagnosticSource:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_diagnostic_source_must_not_be_empty));
                case InitializeErrorKind.ReservedDiagnosticSource:
                    return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_diagnostic_source_0_is_reserved_by_TypeScript, initializeError.DiagnosticSource));
            }
        }

        return diagnostic.AddMessageChain(NewCompilerDiagnostic(The_content_mapper_process_could_not_be_started_or_initialized));
    }

    /// <summary>ContentMapperProjectDiagnostic returns a fileless diagnostic for project setup or mapper initialization.</summary>
    /// <remarks>Go: <c>func ContentMapperProjectDiagnostic(err error) *ast.Diagnostic</c>（fileloader.go:630）。</remarks>
    public static Diagnostic ContentMapperProjectDiagnostic(Exception err)
    {
        if (AsError<InitializeError>(err) is not null)
        {
            return ContentMapperInitializationDiagnostic("", err);
        }

        return NewCompilerDiagnostic(ContentMapperProjectErrorDiagnostic(err));
    }

    /// <summary>Go: <c>func getLibraryNameFromLibFileName(libFileName string) string</c>（fileloader.go:1000）。</summary>
    internal static string GetLibraryNameFromLibFileName(string libFileName)
    {
        // Support resolving to lib.dom.d.ts -> @typescript/lib-dom, and
        //                      lib.dom.iterable.d.ts -> @typescript/lib-dom/iterable
        //                      lib.es2015.symbol.wellknown.d.ts -> @typescript/lib-es2015/symbol-wellknown
        var components = libFileName.Split('.');
        var path = new StringBuilder();
        path.Append("@typescript/lib-");
        if (components.Length > 1)
        {
            path.Append(components[1]);
        }

        var i = 2;
        while (i < components.Length && components[i] != "" && components[i] != "d")
        {
            path.Append(i == 2 ? '/' : '-');
            path.Append(components[i]);
            i++;
        }

        return path.ToString();
    }

    /// <summary>Go: <c>func getInferredLibraryNameResolveFrom(options *core.CompilerOptions, currentDirectory string, libFileName string) string</c>（fileloader.go:1023）。</summary>
    internal static string GetInferredLibraryNameResolveFrom(CompilerOptions options, string currentDirectory, string libFileName)
    {
        string containingDirectory;
        if (options.ConfigFilePath != "")
        {
            containingDirectory = TSPath.TSPath.GetDirectoryPath(options.ConfigFilePath);
        }
        else
        {
            containingDirectory = currentDirectory;
        }

        return TSPath.TSPath.CombinePaths(containingDirectory, "__lib_node_modules_lookup_" + libFileName + "__.ts");
    }

    /// <summary>Go: <c>func getModeForTypeReferenceDirectiveInFile(ref *ast.FileReference, file *ast.SourceFile, meta ast.SourceFileMetaData, options *core.CompilerOptions) core.ResolutionMode</c>（fileloader.go:1033）。</summary>
    internal static ModuleKind GetModeForTypeReferenceDirectiveInFile(FileReference reference, SourceFile file, SourceFileMetaData meta, CompilerOptions options)
    {
        if (reference.ResolutionMode != ResolutionModeNone)
        {
            return reference.ResolutionMode;
        }
        else
        {
            return GetDefaultResolutionModeForFile(file.FileName(), meta, options);
        }
    }

    /// <summary>Go: <c>func getDefaultResolutionModeForFile(fileName string, meta ast.SourceFileMetaData, options *core.CompilerOptions) core.ResolutionMode</c>（fileloader.go:1041）。</summary>
    internal static ModuleKind GetDefaultResolutionModeForFile(string fileName, SourceFileMetaData meta, CompilerOptions options)
    {
        if (ImportSyntaxAffectsModuleResolution(options))
        {
            return GetImpliedNodeFormatForEmitWorker(fileName, options.GetEmitModuleKind(), meta);
        }
        else
        {
            return ResolutionModeNone;
        }
    }

    /// <summary>Go: <c>func getModeForUsageLocation(fileName string, meta ast.SourceFileMetaData, usage *ast.StringLiteralLike, options *core.CompilerOptions) core.ResolutionMode</c>（fileloader.go:1049）。</summary>
    internal static ModuleKind GetModeForUsageLocation(string fileName, SourceFileMetaData meta, Node usage, CompilerOptions? options)
    {
        var parent = usage.Parent!;
        if (IsImportDeclaration(parent) || parent.Kind == Kind.JSImportDeclaration || IsExportDeclaration(parent) || IsJSDocImportTag(parent))
        {
            var isTypeOnly = IsExclusivelyTypeOnlyImportOrExport(parent);
            if (isTypeOnly)
            {
                var (overrides, ok) = (ResolutionModeNone, false);
                switch (parent.Kind)
                {
                    case Kind.ImportDeclaration:
                    case Kind.JSImportDeclaration:
                        (overrides, ok) = parent.AsImportDeclaration()!.Attributes.GetResolutionModeOverride(null);
                        break;
                    case Kind.ExportDeclaration:
                        (overrides, ok) = parent.AsExportDeclaration()!.Attributes.GetResolutionModeOverride(null);
                        break;
                    case Kind.JSDocImportTag:
                        (overrides, ok) = parent.AsJSDocImportTag()!.Attributes.GetResolutionModeOverride(null);
                        break;
                }

                if (ok)
                {
                    return overrides;
                }
            }
        }

        if (IsLiteralTypeNode(parent) && IsImportTypeNode(parent.Parent!))
        {
            var (overrides, ok) = parent.Parent!.AsImportTypeNode()!.Attributes.GetResolutionModeOverride(null);
            if (ok)
            {
                return overrides;
            }
        }

        if (options is not null && ImportSyntaxAffectsModuleResolution(options))
        {
            return GetEmitSyntaxForUsageLocationWorker(fileName, meta, usage, options);
        }

        return ResolutionModeNone;
    }

    /// <summary>Go: <c>func importSyntaxAffectsModuleResolution(options *core.CompilerOptions) bool</c>（fileloader.go:1081）。</summary>
    internal static bool ImportSyntaxAffectsModuleResolution(CompilerOptions options)
    {
        var moduleResolution = options.GetModuleResolutionKind();
        return ModuleResolutionKind.Node16 <= moduleResolution && moduleResolution <= ModuleResolutionKind.NodeNext
            || options.GetResolvePackageJsonExports()
            || options.GetResolvePackageJsonImports();
    }

    /// <summary>Go: <c>func getEmitSyntaxForUsageLocationWorker(fileName string, meta ast.SourceFileMetaData, usage *ast.Node, options *core.CompilerOptions) core.ResolutionMode</c>（fileloader.go:1087）。</summary>
    internal static ModuleKind GetEmitSyntaxForUsageLocationWorker(string fileName, SourceFileMetaData meta, Node usage, CompilerOptions options)
    {
        var parent = usage.Parent!;
        if (IsRequireCall(parent, false /*requireStringLiteralLikeArgument*/) || IsExternalModuleReference(parent) && IsImportEqualsDeclaration(parent.Parent!))
        {
            return ModuleKind.CommonJS;
        }

        var fileEmitMode = GetEmitModuleFormatOfFileWorker(fileName, options, meta);
        if (IsImportCall(WalkUpParenthesizedExpressions(parent)))
        {
            if (ShouldTransformImportCall(fileName, options, fileEmitMode))
            {
                return ModuleKind.CommonJS;
            }
            else
            {
                return ModuleKind.ESNext;
            }
        }

        // If we're in --module preserve on an input file, we know that an import
        // is an import. But if this is a declaration file, we'd prefer to use the
        // impliedNodeFormat. Since we want things to be consistent between the two,
        // we need to issue errors when the user writes ESM syntax in a definitely-CJS
        // file, until/unless declaration emit can indicate a true ESM import. On the
        // other hand, writing CJS syntax in a definitely-ESM file is fine, since declaration
        // emit preserves the CJS syntax.
        if (fileEmitMode == ModuleKind.CommonJS)
        {
            return ModuleKind.CommonJS;
        }
        else
        {
            if (fileEmitMode.IsNonNodeESM() || fileEmitMode == ModuleKind.Preserve)
            {
                return ModuleKind.ESNext;
            }
        }

        return ModuleKind.None;
    }
}
