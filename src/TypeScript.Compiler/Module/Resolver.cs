// ported from: internal/module/resolver.go:1-560（解析器核心：内部状态、tracer、DefaultResolver 的两条公开入口、
// type reference directive 解析、node-like 主流程）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// - Go 的 `*resolved` 用 nil 表示"继续搜索"（`shouldContinueSearching()`）+ `unresolved()` 空实例 →
//   C# 直接用 `null`（`continueSearching()` 落成 `return null;`，`unresolved()` 落成 `new Resolved()`），
//   调用点用 `if (f() is { } r) { … }` 表达 Go 的 `if r := f(); !r.shouldContinueSearching()`。
// - Go 的 `func (t *tracer) write(...)` 是 nil 接收者安全方法 → C# 调用点写 `tracer?.Write(...)`。
// - Go 的 `panic(fmt.Sprintf(...))` → `Debug.Fail(...)`（抛 DebugFailureException，§3.2）。
// - Go 的 `append` + `r.diagnostics[:n]` 回滚 → C# `List<Diagnostic>` + `RemoveRange`。
// - Go 的 `[]DiagAndArgs` 返回 nil 表示"没有 trace" → C# 空数组（调用方只判长度）。
// - Go 的 `compilerOptions.GetEffectiveTypeRoots(...)` 双返回值 → C# 命名元组 `(Result, FromConfig)`。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.PackageJson;
using TypeScript.TSPath;
using TypeScript.Vfs;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Module;

/// <summary>Go 的 <c>resolved</c>（内部解析结果；null = Go 的 <c>continueSearching()</c>）。</summary>
internal sealed class Resolved
{
    internal string Path = "";
    internal string Extension = "";
    internal PackageId PackageId;
    internal string OriginalPath = "";
    internal bool ResolvedUsingTsExtension;
    internal bool ResolvedUsingExtraExtensions;

    internal bool IsResolved()
    {
        return Path != "";
    }
}

/// <summary>Go 的 <c>resolutionKindSpecificLoader</c>。</summary>
internal delegate Resolved? ResolutionKindSpecificLoader(Extensions extensions, string candidate);

/// <summary>Go 的 <c>tracer</c>（`--traceResolution` 的可选收集器）。</summary>
internal sealed class Tracer
{
    private readonly List<DiagAndArgs> _traces = [];

    internal void Write(Message diag, params object?[] args)
    {
        _traces.Add(new DiagAndArgs(diag, args));
    }

    internal DiagAndArgs[] GetTraces()
    {
        return [.. _traces];
    }
}

/// <summary>Go 的 <c>DiagAndArgs</c>。</summary>
public struct DiagAndArgs
{
    public Message Message;
    public object?[] Args;

    public DiagAndArgs(Message message, object?[] args)
    {
        Message = message;
        Args = args;
    }
}

/// <summary>Go 的 <c>resolutionState</c>（未导出；解析过程的可变状态）。</summary>
internal sealed class ResolutionState
{
    internal DefaultResolver _resolver = null!;
    internal Tracer? _tracer;

    // request fields
    internal string _name = "";
    internal string _containingDirectory = "";
    internal bool _isConfigLookup;
    internal NodeResolutionFeatures _features;
    internal bool _esmMode;
    internal string[] _conditions = [];
    internal Extensions _extensions;
    internal CompilerOptions _compilerOptions = null!;
    internal bool _resolvePackageDirectoryOnly;

    // state fields
    //
    // candidateEndingIsFromConfig is set when the candidate file extension originated from
    // configuration (package.json fields, tsconfig.json paths entries, or wildcard substitutions)
    // rather than from the module specifier written in source code. When true, resolvedUsingTsExtension
    // is suppressed so the checker does not attempt to extract a TS extension from the original specifier.
    internal bool _candidateEndingIsFromConfig;
    internal bool _resolvedPackageDirectory;
    internal List<Diagnostic> _diagnostics = [];
}

/// <summary>Go 的 <c>DefaultResolver</c>。</summary>
public sealed partial class DefaultResolver : Resolver
{
    private readonly Caches _caches;
    private readonly ResolutionHost _host;
    private readonly CompilerOptions _compilerOptions;
    private readonly string _typingsLocation;
    private readonly string _projectName;
    private readonly string[] _extraExtensions;
    // reportDiagnostic: DiagnosticReporter

    internal DefaultResolver(
        Caches caches,
        ResolutionHost host,
        CompilerOptions compilerOptions,
        string typingsLocation,
        string projectName,
        string[] extraExtensions)
    {
        _caches = caches;
        _host = host;
        _compilerOptions = compilerOptions;
        _typingsLocation = typingsLocation;
        _projectName = projectName;
        _extraExtensions = extraExtensions;
    }

    internal ResolutionHost Host => _host;

    internal CompilerOptions CompilerOptions => _compilerOptions;

    internal string[] ExtraExtensions => _extraExtensions;

    internal string TypingsLocation => _typingsLocation;

    /// <summary>Go 的 <c>r.packageJsonInfoCache.Get(path)</c>（promoted 字段的 C# 等价访问器）。</summary>
    internal InfoCacheEntry? PackageJsonCacheGet(string packageJsonPath)
    {
        return _caches._packageJsonInfoCache.Get(packageJsonPath);
    }

    /// <summary>Go 的 <c>r.packageJsonInfoCache.Set(path, info)</c>。</summary>
    internal InfoCacheEntry PackageJsonCacheSet(string packageJsonPath, InfoCacheEntry info)
    {
        return _caches._packageJsonInfoCache.Set(packageJsonPath, info);
    }

    private Tracer? NewTraceBuilder()
    {
        if (_compilerOptions.TraceResolution == Tristate.TSTrue)
        {
            return new Tracer();
        }

        return null;
    }

    public InfoCacheEntry? GetPackageScopeForPath(string directory)
    {
        return new ResolutionState { _compilerOptions = _compilerOptions, _resolver = this }.GetPackageScopeForPath(directory);
    }

    public void PackageJsonCacheEntries(Func<TSPath.Path, InfoCacheEntry?, bool> f)
    {
        _caches._packageJsonInfoCache.Range(f);
    }

    public (ResolvedTypeReferenceDirective? Directive, DiagAndArgs[] Traces) ResolveTypeReferenceDirective(
        string typeReferenceDirectiveName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        var containingDirectory = GetDirectoryPath(containingFile);
        var traceBuilder = NewTraceBuilder();

        var fromInferredTypesContainingFile = containingFile.EndsWith(Module.InferredTypesContainingFile, StringComparison.Ordinal);

        var cacheKey = new TypeRefDirectiveResolutionCacheKey(
            containingDirectory,
            typeReferenceDirectiveName,
            resolutionMode,
            Module.GetRedirectConfigName(redirectedReference),
            fromInferredTypesContainingFile);

        if (traceBuilder is null)
        {
            var (cached, ok) = _caches._typeRefDirectiveResolutionCache.Get(cacheKey);
            if (ok && cached is not null)
            {
                return (cached, []);
            }
        }

        var compilerOptions = Module.GetCompilerOptionsWithRedirect(_compilerOptions, redirectedReference);

        var (typeRoots, fromConfig) = compilerOptions.GetEffectiveTypeRoots(_host.GetCurrentDirectory());
        traceBuilder?.Write(Resolving_type_reference_directive_0_containing_file_1_root_directory_2, typeReferenceDirectiveName, containingFile, string.Join(",", typeRoots));
        traceBuilder?.TraceResolutionUsingProjectReference(redirectedReference);

        var state = Module.NewResolutionState(typeReferenceDirectiveName, containingDirectory, true /*isTypeReferenceDirective*/, resolutionMode, compilerOptions, redirectedReference, this, traceBuilder);
        var result = state.ResolveTypeReferenceDirective(typeRoots, fromConfig, fromInferredTypesContainingFile);

        traceBuilder?.TraceTypeReferenceDirectiveResult(typeReferenceDirectiveName, result);

        _caches._typeRefDirectiveResolutionCache.Set(cacheKey, result);

        return (result, traceBuilder?.GetTraces() ?? []);
    }

    public (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleName(
        string moduleName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        return ResolveModuleNameWorker(moduleName, containingFile, GetDirectoryPath(containingFile), resolutionMode, redirectedReference);
    }

    public (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleNameFromDirectory(
        string moduleName,
        string containingDirectory,
        ModuleKind resolutionMode)
    {
        return ResolveModuleNameWorker(moduleName, containingDirectory, containingDirectory, resolutionMode, null);
    }

    private (ResolvedModule? Module, DiagAndArgs[] Traces) ResolveModuleNameWorker(
        string moduleName,
        string containingFile,
        string containingDirectory,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        var traceBuilder = NewTraceBuilder();

        var cacheKey = new ModuleResolutionCacheKey(
            containingDirectory,
            moduleName,
            resolutionMode,
            Module.GetRedirectConfigName(redirectedReference));

        if (traceBuilder is null)
        {
            var (cached, ok) = _caches._moduleResolutionCache.Get(cacheKey);
            if (ok && cached is not null)
            {
                return (cached, []);
            }
        }

        var compilerOptions = Module.GetCompilerOptionsWithRedirect(_compilerOptions, redirectedReference);
        traceBuilder?.Write(Resolving_module_0_from_1, moduleName, containingFile);
        traceBuilder?.TraceResolutionUsingProjectReference(redirectedReference);

        var moduleResolution = compilerOptions.GetModuleResolutionKind();
        if (compilerOptions.ModuleResolution != moduleResolution)
        {
            traceBuilder?.Write(Module_resolution_kind_is_not_specified_using_0, moduleResolution.String());
        }
        else
        {
            traceBuilder?.Write(Explicitly_specified_module_resolution_kind_Colon_0, moduleResolution.String());
        }

        ResolvedModule result;
        switch (moduleResolution)
        {
            case ModuleResolutionKind.Node16:
            case ModuleResolutionKind.NodeNext:
            case ModuleResolutionKind.Bundler:
                var state = Module.NewResolutionState(moduleName, containingDirectory, false /*isTypeReferenceDirective*/, resolutionMode, compilerOptions, redirectedReference, this, traceBuilder);
                result = state.ResolveNodeLike();
                break;
            default:
                throw new DebugFailureException($"Unexpected moduleResolution: {(int)moduleResolution}");
        }

        if (traceBuilder is not null)
        {
            if (result.IsResolved())
            {
                if (result.PackageId.Name != "")
                {
                    traceBuilder.Write(Module_name_0_was_successfully_resolved_to_1_with_Package_ID_2, moduleName, result.ResolvedFileName, result.PackageId.ToString());
                }
                else
                {
                    traceBuilder.Write(Module_name_0_was_successfully_resolved_to_1, moduleName, result.ResolvedFileName);
                }
            }
            else
            {
                traceBuilder.Write(Module_name_0_was_not_resolved, moduleName);
            }
        }

        var finalResult = TryResolveFromTypingsLocation(moduleName, containingDirectory, result, traceBuilder);
        _caches._moduleResolutionCache.Set(cacheKey, finalResult);

        return (finalResult, traceBuilder?.GetTraces() ?? []);
    }

    public ResolvedModule? ResolvePackageDirectory(
        string moduleName,
        string containingFile,
        ModuleKind resolutionMode,
        ResolvedProjectReference? redirectedReference)
    {
        var compilerOptions = Module.GetCompilerOptionsWithRedirect(_compilerOptions, redirectedReference);
        var containingDirectory = GetDirectoryPath(containingFile);
        var state = Module.NewResolutionState(moduleName, containingDirectory, false /*isTypeReferenceDirective*/, resolutionMode, compilerOptions, redirectedReference, this, null);
        state._resolvePackageDirectoryOnly = true;
        var result = state.LoadModuleFromNearestNodeModulesDirectory(false /*typesScopeOnly*/);
        if (result is not null && result.Path != "")
        {
            return state.CreateResolvedModuleHandlingSymlink(result);
        }

        return null;
    }

    private ResolvedModule TryResolveFromTypingsLocation(string moduleName, string containingDirectory, ResolvedModule originalResult, Tracer? traceBuilder)
    {
        if (_typingsLocation == "" ||
            IsExternalModuleNameRelative(moduleName) ||
            (originalResult.ResolvedFileName != "" && ExtensionIsOneOf(originalResult.Extension, SupportedTSExtensionsWithJsonFlat)))
        {
            return originalResult;
        }

        var state = Module.NewResolutionState(
            moduleName,
            containingDirectory,
            false, /*isTypeReferenceDirective*/
            ModuleKind.None, // resolutionMode,
            _compilerOptions,
            null, // redirectedReference,
            this,
            traceBuilder);
        traceBuilder?.Write(Auto_discovery_for_typings_is_enabled_in_project_0_Running_extra_resolution_pass_for_module_1_using_cache_location_2, _projectName, moduleName, _typingsLocation);
        var globalResolved = state.LoadModuleFromImmediateNodeModulesDirectory(Extensions.Declaration, _typingsLocation, false);
        if (globalResolved is null)
        {
            return originalResult;
        }

        var result = state.CreateResolvedModule(globalResolved, true);
        result.ResolutionDiagnostics = [.. originalResult.ResolutionDiagnostics, .. result.ResolutionDiagnostics];
        return result;
    }

    internal ResolvedModule ResolveConfig(string moduleName, string containingFile)
    {
        var containingDirectory = GetDirectoryPath(containingFile);
        var state = Module.NewResolutionState(moduleName, containingDirectory, false /*isTypeReferenceDirective*/, ModuleKind.CommonJS, _compilerOptions, null, this, null);
        state._isConfigLookup = true;
        state._extensions = Extensions.Json;
        return state.ResolveNodeLike();
    }

    internal ParsedPatterns GetParsedPatternsForPaths(CompilerOptions compilerOptions)
    {
        return _caches._parsedPatternsForPaths.Get(compilerOptions.Paths ?? new OrderedMap<string, string[]>());
    }
}

/// <summary>Go 的 <c>ResolverOptions</c>。</summary>
public sealed class ResolverOptions
{
    public ResolutionHost Host = null!;
    public CompilerOptions CompilerOptions = null!;
    public string TypingsLocation = "";
    public string ProjectName = "";
    public string[]? ExtraExtensions;
    public InfoCache? PackageJsonCache;
}

public static partial class Module
{
    public static DefaultResolver NewResolver(ResolverOptions opts)
    {
        var caches = opts.PackageJsonCache is not null
            ? new Caches(opts.PackageJsonCache)
            : NewCaches(opts.Host.GetCurrentDirectory(), opts.Host.FS().UseCaseSensitiveFileNames(), opts.CompilerOptions);
        return new DefaultResolver(
            caches,
            opts.Host,
            opts.CompilerOptions,
            opts.TypingsLocation,
            opts.ProjectName,
            opts.ExtraExtensions ?? []);
    }

    public static CompilerOptions GetCompilerOptionsWithRedirect(CompilerOptions compilerOptions, ResolvedProjectReference? redirectedReference)
    {
        if (redirectedReference is null)
        {
            return compilerOptions;
        }

        if (redirectedReference.CompilerOptions() is { } optionsFromRedirect)
        {
            return optionsFromRedirect;
        }

        return compilerOptions;
    }

    internal static ResolutionState NewResolutionState(
        string name,
        string containingDirectory,
        bool isTypeReferenceDirective,
        ModuleKind resolutionMode,
        CompilerOptions compilerOptions,
        ResolvedProjectReference? redirectedReference,
        DefaultResolver resolver,
        Tracer? traceBuilder)
    {
        var state = new ResolutionState
        {
            _name = name,
            _containingDirectory = containingDirectory,
            _compilerOptions = GetCompilerOptionsWithRedirect(compilerOptions, redirectedReference),
            _resolver = resolver,
            _tracer = traceBuilder,
        };

        if (isTypeReferenceDirective)
        {
            state._extensions = Extensions.Declaration;
        }
        else if (compilerOptions.NoDtsResolution == Tristate.TSTrue)
        {
            state._extensions = Extensions.ImplementationFiles;
        }
        else
        {
            state._extensions = Extensions.TypeScript | Extensions.JavaScript | Extensions.Declaration;
        }

        if (!isTypeReferenceDirective && compilerOptions.GetResolveJsonModule())
        {
            state._extensions |= Extensions.Json;
        }

        switch (compilerOptions.GetModuleResolutionKind())
        {
            case ModuleResolutionKind.Node16:
                state._features = NodeResolutionFeatures.Node16Default;
                state._esmMode = resolutionMode == ModuleKind.ESNext;
                state._conditions = GetConditions(compilerOptions, resolutionMode);
                break;
            case ModuleResolutionKind.NodeNext:
                state._features = NodeResolutionFeatures.NodeNextDefault;
                state._esmMode = resolutionMode == ModuleKind.ESNext;
                state._conditions = GetConditions(compilerOptions, resolutionMode);
                break;
            case ModuleResolutionKind.Bundler:
                state._features = GetNodeResolutionFeatures(compilerOptions);
                state._conditions = GetConditions(compilerOptions, resolutionMode);
                break;
        }

        return state;
    }

    internal static void TraceResolutionUsingProjectReference(this Tracer tracer, ResolvedProjectReference? redirectedReference)
    {
        if (redirectedReference is not null && redirectedReference.CompilerOptions() is not null)
        {
            tracer.Write(Using_compiler_options_of_project_reference_redirect_0, redirectedReference.ConfigName());
        }
    }

    internal static void TraceTypeReferenceDirectiveResult(this Tracer tracer, string typeReferenceDirectiveName, ResolvedTypeReferenceDirective result)
    {
        if (!result.IsResolved())
        {
            tracer.Write(Type_reference_directive_0_was_not_resolved, typeReferenceDirectiveName);
        }
        else if (result.PackageId.Name != "")
        {
            tracer.Write(
                Type_reference_directive_0_was_successfully_resolved_to_1_with_Package_ID_2_primary_Colon_3,
                typeReferenceDirectiveName,
                result.ResolvedFileName,
                result.PackageId.ToString(),
                result.Primary);
        }
        else
        {
            tracer.Write(
                Type_reference_directive_0_was_successfully_resolved_to_1_primary_Colon_2,
                typeReferenceDirectiveName,
                result.ResolvedFileName,
                result.Primary);
        }
    }

    /// <summary>Go 的 <c>(*resolutionState).resolveTypeReferenceDirective</c>。</summary>
    internal static ResolvedTypeReferenceDirective ResolveTypeReferenceDirective(
        this ResolutionState r,
        string[] typeRoots,
        bool fromConfig,
        bool fromInferredTypesContainingFile)
    {
        // Primary lookup
        if (typeRoots.Length > 0)
        {
            r._tracer?.Write(Resolving_with_primary_search_path_0, string.Join(", ", typeRoots));
            foreach (var typeRoot in typeRoots)
            {
                var candidate = r.GetCandidateFromTypeRoot(typeRoot);
                var directoryExists = r._resolver.Host.FS().DirectoryExists(typeRoot);
                if (!directoryExists)
                {
                    r._tracer?.Write(Directory_0_does_not_exist_skipping_all_lookups_in_it, typeRoot);
                    continue;
                }

                if (fromConfig)
                {
                    // Custom typeRoots resolve as file or directory just like we do modules
                    if (r.LoadModuleFromFile(Extensions.Declaration, candidate) is { } resolvedFromFile)
                    {
                        var packageDirectory = ParseNodeModuleFromPath(resolvedFromFile.Path, false);
                        if (packageDirectory != "")
                        {
                            resolvedFromFile.PackageId = r.GetPackageId(resolvedFromFile.Path, r.GetPackageJsonInfo(packageDirectory));
                        }

                        return r.CreateResolvedTypeReferenceDirective(resolvedFromFile, true /*primary*/);
                    }
                }

                if (r.LoadNodeModuleFromDirectory(Extensions.Declaration, candidate, true /*considerPackageJson*/) is { } resolvedFromDirectory)
                {
                    return r.CreateResolvedTypeReferenceDirective(resolvedFromDirectory, true /*primary*/);
                }
            }
        }
        else
        {
            r._tracer?.Write(Root_directory_cannot_be_determined_skipping_primary_search_paths);
        }

        // Secondary lookup
        Resolved? resolved;
        if (!fromConfig || !fromInferredTypesContainingFile)
        {
            r._tracer?.Write(Looking_up_in_node_modules_folder_initial_location_0, r._containingDirectory);
            if (!IsExternalModuleNameRelative(r._name))
            {
                resolved = r.LoadModuleFromNearestNodeModulesDirectory(false /*typesScopeOnly*/);
            }
            else
            {
                var candidate = NormalizePathForCJSResolution(r._containingDirectory, r._name);
                resolved = r.NodeLoadModuleByRelativeName(Extensions.Declaration, candidate, true /*considerPackageJson*/);
            }
        }
        else
        {
            r._tracer?.Write(Resolving_type_reference_directive_for_program_that_specifies_custom_typeRoots_skipping_lookup_in_node_modules_folder);
            resolved = null;
        }

        return r.CreateResolvedTypeReferenceDirective(resolved, false /*primary*/);
    }

    /// <summary>Go 的 <c>(*resolutionState).getCandidateFromTypeRoot</c>。</summary>
    internal static string GetCandidateFromTypeRoot(this ResolutionState r, string typeRoot)
    {
        var nameForLookup = r._name;
        if (typeRoot.EndsWith("/node_modules/@types", StringComparison.Ordinal) || typeRoot.EndsWith("/node_modules/@types/", StringComparison.Ordinal))
        {
            nameForLookup = r.MangleScopedPackageName(r._name);
        }

        return CombinePaths(typeRoot, nameForLookup);
    }

    /// <summary>Go 的 <c>(*resolutionState).mangleScopedPackageName</c>。</summary>
    internal static string MangleScopedPackageName(this ResolutionState r, string name)
    {
        var mangled = Module.MangleScopedPackageName(name);
        if (r._tracer is not null && mangled != name)
        {
            r._tracer.Write(Scoped_package_detected_looking_in_0, mangled);
        }

        return mangled;
    }

    /// <summary>
    /// resolveFromTypeRoot tries to resolve a module name from the configured typeRoots.
    /// This is used as a fallback after node_modules resolution fails, for declaration file lookups.
    /// Returns nil if typeRoots is not configured or if no matching module is found in any typeRoot directory.
    /// </summary>
    internal static Resolved? ResolveFromTypeRoot(this ResolutionState r)
    {
        if (r._compilerOptions.TypeRoots is not { } typeRoots)
        {
            return null;
        }

        foreach (var typeRoot in typeRoots)
        {
            var candidate = r.GetCandidateFromTypeRoot(typeRoot);
            var directoryExists = r._resolver.Host.FS().DirectoryExists(typeRoot);
            if (!directoryExists)
            {
                r._tracer?.Write(Directory_0_does_not_exist_skipping_all_lookups_in_it, typeRoot);
                continue;
            }

            if (r.LoadModuleFromFile(Extensions.Declaration, candidate) is { } resolvedFromFile)
            {
                var packageDirectory = ParseNodeModuleFromPath(resolvedFromFile.Path, false);
                if (packageDirectory != "")
                {
                    resolvedFromFile.PackageId = r.GetPackageId(resolvedFromFile.Path, r.GetPackageJsonInfo(packageDirectory));
                }

                return resolvedFromFile;
            }

            if (r.LoadNodeModuleFromDirectory(Extensions.Declaration, candidate, true /*considerPackageJson*/) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    internal static InfoCacheEntry? GetPackageScopeForPath(this ResolutionState r, string directory)
    {
        return ForEachAncestorDirectoryStoppingAtGlobalCache<InfoCacheEntry?>(
            r._resolver.TypingsLocation,
            directory,
            ancestorDirectory =>
            {
                if (r.GetPackageJsonInfo(ancestorDirectory) is { } result)
                {
                    return (result, true);
                }

                return (null, false);
            });
    }

    internal static ResolvedModule ResolveNodeLike(this ResolutionState r)
    {
        if (r._tracer is not null)
        {
            var conditions = string.Join(", ", Map(r._conditions, c => "'" + c + "'"));
            if (r._esmMode)
            {
                r._tracer.Write(Resolving_in_0_mode_with_conditions_1, "ESM", conditions);
            }
            else
            {
                r._tracer.Write(Resolving_in_0_mode_with_conditions_1, "CJS", conditions);
            }
        }

        var result = r.ResolveNodeLikeWorker();
        if (r._resolvedPackageDirectory &&
            !r._isConfigLookup &&
            (r._features & NodeResolutionFeatures.Exports) != 0 &&
            (r._extensions & (Extensions.TypeScript | Extensions.Declaration)) != 0 &&
            !IsExternalModuleNameRelative(r._name) &&
            result.IsResolved() &&
            result.IsExternalLibraryImport &&
            !ExtensionIsOk(Extensions.TypeScript | Extensions.Declaration, result.Extension) &&
            System.Array.IndexOf(r._conditions, "import") >= 0)
        {
            r._tracer?.Write(Resolution_of_non_relative_name_failed_trying_with_modern_Node_resolution_features_disabled_to_see_if_npm_library_needs_configuration_update);
            r._features &= ~NodeResolutionFeatures.Exports;
            r._extensions &= Extensions.TypeScript | Extensions.Declaration;
            var diagnosticsCount = r._diagnostics.Count;
            var diagnosticResult = r.ResolveNodeLikeWorker();
            if (diagnosticResult.IsResolved() && diagnosticResult.IsExternalLibraryImport)
            {
                result.AlternateResult = diagnosticResult.ResolvedFileName;
            }

            r._diagnostics.RemoveRange(diagnosticsCount, r._diagnostics.Count - diagnosticsCount);
        }

        return result;
    }
}
