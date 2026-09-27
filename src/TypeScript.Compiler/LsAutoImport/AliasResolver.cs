// ported from: internal/ls/autoimport/aliasresolver.go
//
// 与 Go 的偏离（逐条）：
// 1) 未导出类型 `aliasResolver`/`pathAndFileName` → C# 的 `AliasResolver`/`PathAndFileName`（§2）。
// 2) Go 的 `panic("unimplemented")` → `Unimplemented()`（`Debug.Fail` + `throw new UnreachableException()`，
//    因为 `Debug.Fail` 没有 `[DoesNotReturn]`）。**这些分支在 Go 里就是"不会被调用"的断言**，不是占位实现：
//    aliasResolver 只被 checker 用来做符号解析，诊断/emit 相关入口永远不会走到。
// 3) `collections.SyncMap[...]`（Go 的值类型，零值可用）→ C# 的 `SyncMap<K,V>`（class，需构造）：
//    字段与 `LoadOrStore` 的语义保持（`(V?, bool)` 元组）。
// 4) `(*ResolvedModule).IsResolved()` 在 Go 里自带 nil 检查 → 调用点显式写 `resolved is not null && resolved.IsResolved()`（§3.2）。
// 5) `sourceFile.Path()`/`toPath(fileName)` 返回 `tspath.Path`，Go 的 `map[tspath.Path]X` → `Dictionary<Path, X>`。
// 6) `binder.BindSourceFile` → 全限定 `TypeScript.Binder.Binder.BindSourceFile`（裸 `Binder` 会命中命名空间）。
// 7) 接口 `Checker.Program` 的成员名/形状与 Go 的 `checker.Program` 逐条对应（见 Checker/Checker.Program.cs）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Module;
using TypeScript.PackageJson;
using TypeScript.TsOptions;
using Path = TypeScript.TSPath.Path;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.LsAutoImport;

/// <summary>Go: <c>type pathAndFileName struct { path tspath.Path; fileName string }</c>（aliasresolver.go:16-19）。</summary>
internal struct PathAndFileName
{
    internal Path _path;

    internal string _fileName;
}

/// <summary>Go: <c>type aliasResolver struct</c>（aliasresolver.go:21-35）：以一组根文件冒充 `checker.Program`，
/// 只实现 checker 解析别名符号真正会用到的那几个入口。</summary>
internal sealed class AliasResolver : TypeScript.Checker.Program
{
    internal Func<string, Path> _toPath;

    internal RegistryCloneHost _host;

    internal DefaultResolver _moduleResolver;

    internal SourceFile[] _rootFiles;

    /// <summary>symlinks maps from realpath to symlinked path and file name</summary>
    internal Dictionary<Path, PathAndFileName>? _symlinks;

    internal Action<HasFileName, string> _onFailedAmbientModuleLookup;

    internal SyncMap<Path, SyncMap<ModeAwareCacheKey, ResolvedModule>> _resolvedModules = new();

    public AliasResolver(
        SourceFile[] rootFiles,
        Dictionary<Path, PathAndFileName>? symlinks,
        RegistryCloneHost host,
        DefaultResolver moduleResolver,
        Func<string, Path> toPath,
        Action<HasFileName, string> onFailedAmbientModuleLookup)
    {
        _toPath = toPath;
        _host = host;
        _moduleResolver = moduleResolver;
        _rootFiles = rootFiles;
        _symlinks = symlinks;
        _onFailedAmbientModuleLookup = onFailedAmbientModuleLookup;
    }

    /// <summary>BindSourceFiles implements checker.Program.</summary>
    public void BindSourceFiles()
    {
        // We will bind as we parse（Go 原文：这里故意什么都不做）。
    }

    /// <summary>SourceFiles implements checker.Program.</summary>
    public SourceFile[] SourceFiles() => _rootFiles;

    /// <summary>Options implements checker.Program.</summary>
    public CompilerOptions Options() => new() { NoCheck = Tristate.TSTrue };

    /// <summary>GetCurrentDirectory implements checker.Program.</summary>
    public string GetCurrentDirectory() => _host.GetCurrentDirectory();

    /// <summary>UseCaseSensitiveFileNames implements checker.Program.</summary>
    public bool UseCaseSensitiveFileNames() => _host.FS().UseCaseSensitiveFileNames();

    /// <summary>GetSourceFile implements checker.Program.</summary>
    public SourceFile? GetSourceFile(string fileName)
    {
        var file = _host.GetSourceFile(fileName, _toPath(fileName));
        // file may be nil due to symlink/realpath mismatch; see TestAutoImportBuilderFS
        if (file is null)
        {
            return null;
        }
        TypeScript.Binder.Binder.BindSourceFile(file);
        return file;
    }

    /// <summary>GetDefaultResolutionModeForFile implements checker.Program.</summary>
    public ModuleKind GetDefaultResolutionModeForFile(HasFileName file) => ModuleKind.ESNext;

    /// <summary>GetEmitModuleFormatOfFile implements checker.Program.</summary>
    public ModuleKind GetEmitModuleFormatOfFile(HasFileName sourceFile) => ModuleKind.ESNext;

    /// <summary>GetEmitSyntaxForUsageLocation implements checker.Program.</summary>
    public ModuleKind GetEmitSyntaxForUsageLocation(HasFileName sourceFile, Node usageLocation) => ModuleKind.ESNext;

    /// <summary>GetImpliedNodeFormatForEmit implements checker.Program.</summary>
    public ModuleKind GetImpliedNodeFormatForEmit(HasFileName sourceFile) => ModuleKind.ESNext;

    /// <summary>GetModeForUsageLocation implements checker.Program.</summary>
    public ModuleKind GetModeForUsageLocation(HasFileName file, Node moduleSpecifier) => ModuleKind.ESNext;

    /// <summary>GetResolvedModule implements checker.Program.</summary>
    public ResolvedModule? GetResolvedModule(HasFileName currentSourceFile, string moduleReference, ModuleKind mode)
    {
        var (cache, _) = _resolvedModules.LoadOrStore(currentSourceFile.Path(), new SyncMap<ModeAwareCacheKey, ResolvedModule>());
        cache ??= new SyncMap<ModeAwareCacheKey, ResolvedModule>();
        var key = new ModeAwareCacheKey(moduleReference, mode);
        var (cached, ok) = cache.Load(key);
        if (ok)
        {
            return cached;
        }
        var (resolvedModule, _) = _moduleResolver.ResolveModuleName(moduleReference, currentSourceFile.FileName(), mode, null);
        var (stored, _) = cache.LoadOrStore(key, resolvedModule!);
        if ((stored is null || !stored.IsResolved()) && !PathIsRelative(moduleReference))
        {
            _onFailedAmbientModuleLookup(currentSourceFile, moduleReference);
        }
        return stored;
    }

    /// <summary>GetSourceFileForResolvedModule implements checker.Program.</summary>
    public SourceFile? GetSourceFileForResolvedModule(string fileName) => GetSourceFile(fileName);

    /// <summary>GetResolvedModules implements checker.Program.</summary>
    public Dictionary<Path, ModeAwareCache<ResolvedModule>> GetResolvedModules()
    {
        // only used when producing diagnostics, which hopefully the checker won't do
        return null!;
    }

    // ---

    /// <summary>GetSymlinkCache implements checker.Program.</summary>
    public Symlinks.KnownSymlinks? GetSymlinkCache() => throw UnimplementedException();

    /// <summary>GetSourceFileMetaData implements checker.Program.</summary>
    public SourceFileMetaData GetSourceFileMetaData(Path path) => throw UnimplementedException();

    /// <summary>CommonSourceDirectory implements checker.Program.</summary>
    public string CommonSourceDirectory() => throw UnimplementedException();

    /// <summary>ContentMapperExtensions implements checker.Program.</summary>
    public string[] ContentMapperExtensions() => [];

    /// <summary>FileExists implements checker.Program.</summary>
    public bool FileExists(string fileName) => throw UnimplementedException();

    /// <summary>GetGlobalTypingsCacheLocation implements checker.Program.</summary>
    public string GetGlobalTypingsCacheLocation() => throw UnimplementedException();

    /// <summary>GetImportHelpersImportSpecifier implements checker.Program.</summary>
    public Node? GetImportHelpersImportSpecifier(Path path) => throw UnimplementedException();

    /// <summary>GetJSXRuntimeImportSpecifier implements checker.Program.</summary>
    public (string moduleReference, Node? specifier) GetJSXRuntimeImportSpecifier(Path path) => ("", null);

    /// <summary>GetNearestAncestorDirectoryWithPackageJson implements checker.Program.</summary>
    public string GetNearestAncestorDirectoryWithPackageJson(string dirname) => throw UnimplementedException();

    /// <summary>GetPackageJsonInfo implements checker.Program.</summary>
    public InfoCacheEntry? GetPackageJsonInfo(string pkgJsonPath) => throw UnimplementedException();

    /// <summary>GetProjectReferenceFromOutputDts implements checker.Program.</summary>
    public SourceOutputAndProjectReference? GetProjectReferenceFromOutputDts(Path path) => throw UnimplementedException();

    /// <summary>GetProjectReferenceFromSource implements checker.Program.</summary>
    public SourceOutputAndProjectReference? GetProjectReferenceFromSource(Path path) => throw UnimplementedException();

    /// <summary>GetRedirectForResolution implements checker.Program.</summary>
    public ParsedCommandLine? GetRedirectForResolution(HasFileName file) => throw UnimplementedException();

    /// <summary>GetRedirectTargets implements checker.Program.</summary>
    public string[] GetRedirectTargets(Path path) => throw UnimplementedException();

    /// <summary>GetResolvedModuleFromModuleSpecifier implements checker.Program.</summary>
    public ResolvedModule? GetResolvedModuleFromModuleSpecifier(HasFileName file, Node moduleSpecifier) => throw UnimplementedException();

    /// <summary>GetSourceOfProjectReferenceIfOutputIncluded implements checker.Program.</summary>
    public string GetSourceOfProjectReferenceIfOutputIncluded(HasFileName file) => throw UnimplementedException();

    /// <summary>IsSourceFileDefaultLibrary implements checker.Program.</summary>
    public bool IsSourceFileDefaultLibrary(Path path) => false;

    /// <summary>SourceFileMayBeEmitted implements checker.Program.</summary>
    public bool SourceFileMayBeEmitted(SourceFile sourceFile, bool forceDtsEmit) => throw UnimplementedException();

    /// <summary>GetPackagesMap implements checker.Program.</summary>
    public Dictionary<string, bool> GetPackagesMap() => null!;

    /// <summary>Go 的 <c>panic("unimplemented")</c> 的 C# 落点（`Debug.Fail` 之后必须显式抛）。</summary>
    private static Exception UnimplementedException()
    {
        Debug.Fail("unimplemented");
        return new System.Diagnostics.UnreachableException();
    }
}
