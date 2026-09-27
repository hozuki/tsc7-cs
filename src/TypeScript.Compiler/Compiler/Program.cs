// ported from: internal/compiler/program.go:37-300（ProgramOptions / lazyValue / packageNamesInfo / Program 字段与基础访问器 /
// GetSourceFileFromReference / NewProgram / UpdateProgram / ReuseProgram）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `Program` 内嵌 `processedFiles`（纯数据 struct、无方法）→ C# **基类** `ProcessedFiles`
//    （先例：`ast.SourceFile` 内嵌 `Node` struct → `SourceFile : Node`）；派生类直接读写它的 internal 字段。
// 2) `sync.Once` 字段（commonSourceDirectoryOnce / sourceFilesToEmitOnce / hasTSFileOnce / packagesMapOnce）
//    → `System.Threading.Lock` + 标志位的双重检查（先例 SourceFile.cs / Checker.Struct.cs）。
// 3) `lazyValue[T]`（sync.Once + atomic.Bool + tryReuse）→ 本文件的 `LazyValue<T>`（同成员名、同语义）。
// 4) `(T, bool)` 返回值 → `bool TryX(..., out T)`；`panic(...)` → `Debug.Fail(...)`。
// 5) Go 在 `NewProgram` 里**不初始化** `comparePathsOptions`（只有 `ReuseProgram` 才从旧程序继承），这是 Go 侧既有行为，
//    照抄：C# 里 `default(ComparePathsOptions)` 与 Go 的 `ComparePathsOptions{}` 语义一致（CurrentDirectory 为空串）。
// 6) Go 的 `CreateCheckerPool func(*Program) CheckerPool` → `Func<Program, CheckerPool>?`。
// 7) `CommandLine()`/`Host()`/`Tracing()` 等未导出字段的访问器按 §2 保持 Go 的 PascalCase 方法名。
namespace TypeScript.Compiler;

using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Module;
using TypeScript.PackageJson;
using TypeScript.Tracing;
using TypeScript.TsOptions;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Module.Module;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>type ProgramOptions struct</c>（program.go:37）。</summary>
public sealed class ProgramOptions
{
    /// <summary>Go: <c>Host CompilerHost</c>。</summary>
    public CompilerHost Host = null!;

    /// <summary>Go: <c>Config *tsoptions.ParsedCommandLine</c>。</summary>
    public ParsedCommandLine Config = null!;

    /// <summary>Go: <c>UseSourceOfProjectReference bool</c>。</summary>
    public bool UseSourceOfProjectReference;

    /// <summary>Go: <c>SingleThreaded core.Tristate</c>。</summary>
    public Tristate SingleThreaded;

    /// <summary>Go: <c>CreateCheckerPool func(*Program) CheckerPool</c>。</summary>
    public Func<Program, CheckerPool>? CreateCheckerPool;

    /// <summary>Go: <c>TypingsLocation string</c>。</summary>
    public string TypingsLocation = "";

    /// <summary>Go: <c>ProjectName string</c>。</summary>
    public string ProjectName = "";

    /// <summary>Go: <c>Tracing *tracing.Tracing</c>。</summary>
    public Tracing? Tracing;

    /// <summary>Go: <c>CreateModuleResolver func(options module.ResolverOptions) module.Resolver</c>。</summary>
    public Func<ResolverOptions, Resolver>? CreateModuleResolver;

    /// <summary>Go: <c>SkipModuleResolution bool</c>（跳过全部模块/类型引用解析，但仍收集 emit 需要的 import 元数据）。</summary>
    public bool SkipModuleResolution;

    /// <summary>Go: <c>func (p *ProgramOptions) canUseProjectReferenceSource() bool</c>（program.go:52）。</summary>
    internal bool CanUseProjectReferenceSource() =>
        UseSourceOfProjectReference && !Config.CompilerOptions()!.DisableSourceOfProjectReferenceRedirect.IsTrue();
}

/// <summary>Go: <c>type lazyValue[T any] struct</c>（program.go:56）。</summary>
internal sealed class LazyValue<T> where T : class
{
    // Go: value *T
    private T? _value;
    // Go: once sync.Once
    private readonly Lock _lock = new();
    // Go: initialized atomic.Bool
    private bool _initialized;

    /// <summary>Go: <c>func (l *lazyValue[T]) getValue(compute func() *T) *T</c>（program.go:62）。</summary>
    internal T? GetValue(Func<T?> compute)
    {
        lock (_lock)
        {
            if (!_initialized)
            {
                _value ??= compute();
                _initialized = true;
            }
        }
        return _value;
    }

    /// <summary>Go: <c>func (l *lazyValue[T]) tryReuse(from *lazyValue[T])</c>（program.go:72）。</summary>
    internal void TryReuse(LazyValue<T> from)
    {
        if (Volatile.Read(ref from._initialized))
        {
            lock (_lock)
            {
                _value = from._value;
                _initialized = true;
            }
        }
    }
}

/// <summary>Go: <c>type packageNamesInfo struct</c>（program.go:79）。</summary>
internal sealed class PackageNamesInfo(Set<string> resolved, Set<string> unresolved, Set<string> deepImportPackages)
{
    /// <summary>Go: <c>resolved *collections.Set[string]</c>。</summary>
    public Set<string> Resolved = resolved;

    /// <summary>Go: <c>unresolved *collections.Set[string]</c>。</summary>
    public Set<string> Unresolved = unresolved;

    /// <summary>Go: <c>deepImportPackages *collections.Set[string]</c>。</summary>
    public Set<string> DeepImportPackages = deepImportPackages;
}

/// <summary>Go: <c>type Program struct</c>（program.go:85）——内嵌的 `processedFiles` 落成 C# 基类。</summary>
public sealed partial class Program : ProcessedFiles, TypeScript.Checker.Program, SourceFileMayBeEmittedHost, ProgramLike
{
    /// <summary>Go: <c>opts ProgramOptions</c>。</summary>
    internal ProgramOptions _opts;

    /// <summary>Go: <c>checkerPool CheckerPool</c>（总是有值；项目系统的池也走这里）。</summary>
    internal CheckerPool _checkerPool = null!;

    /// <summary>Go: <c>compilerCheckerPool *checkerPool</c>（只有内置的 compiler 池在用的时候才非 nil）。</summary>
    internal CheckerPoolImpl? _compilerCheckerPool;

    /// <summary>Go: <c>comparePathsOptions tspath.ComparePathsOptions</c>。</summary>
    internal ComparePathsOptions _comparePathsOptions;

    /// <summary>Go: <c>usesUriStyleNodeCoreModules core.Tristate</c>（Go 侧只在 ReuseProgram 里继承，从不赋值 → 恒为 TSUnknown）。</summary>
    private Tristate _usesUriStyleNodeCoreModules = Tristate.TSUnknown;

    // Go: commonSourceDirectory string + commonSourceDirectoryOnce sync.Once
    private string _commonSourceDirectory = "";
    private readonly Lock _commonSourceDirectoryOnceLock = new();
    private bool _commonSourceDirectoryComputed;

    /// <summary>Go: <c>declarationDiagnosticCache collections.SyncMap[*ast.SourceFile, []*ast.Diagnostic]</c>。</summary>
    private readonly SyncMap<SourceFile, Diagnostic[]> _declarationDiagnosticCache = new();

    // Go: programDiagnostics []*ast.Diagnostic
    private Diagnostic[] _programDiagnostics = [];
    // Go: hasEmitBlockingDiagnostics collections.Set[tspath.Path]（ReuseProgram 会整体搬运该 map）
    internal Set<Path> _hasEmitBlockingDiagnostics = new();
    // Go: contentMapperOptionDiagnostics []*ast.Diagnostic
    private Diagnostic[] _contentMapperOptionDiagnostics = [];

    // Go: sourceFilesToEmitOnce sync.Once + sourceFilesToEmit []*ast.SourceFile
    private readonly Lock _sourceFilesToEmitOnceLock = new();
    private bool _sourceFilesToEmitComputed;
    private SourceFile[]? _sourceFilesToEmit;

    // Go: unresolvedImports lazyValue[collections.Set[string]]
    private readonly LazyValue<Set<string>> _unresolvedImports = new();
    // Go: knownSymlinks lazyValue[symlinks.KnownSymlinks]
    private readonly LazyValue<Symlinks.KnownSymlinks> _knownSymlinks = new();
    // Go: packageNames lazyValue[packageNamesInfo]
    private readonly LazyValue<PackageNamesInfo> _packageNames = new();

    // Go: hasTSFileOnce sync.Once + hasTSFile bool
    private readonly Lock _hasTSFileOnceLock = new();
    private bool _hasTSFileComputed;
    private bool _hasTSFile;

    // Go: packagesMapOnce sync.Once + packagesMap map[string]bool
    private readonly Lock _packagesMapOnceLock = new();
    private bool _packagesMapComputed;
    private Dictionary<string, bool>? _packagesMap;

    /// <summary>Go: <c>&amp;Program{opts: opts}</c>（program.go:286）。</summary>
    internal Program(ProgramOptions opts)
    {
        _opts = opts;
    }

    /// <summary>Go: <c>func (p *Program) FileExists(path string) bool</c>（program.go:129）——<c>checker.Program</c>。</summary>
    public bool FileExists(string path) => Host().FS().FileExists(path);

    /// <summary>Go: <c>func (p *Program) GetCurrentDirectory() string</c>（program.go:134）。</summary>
    public string GetCurrentDirectory() => Host().GetCurrentDirectory();

    /// <summary>Go: <c>func (p *Program) ContentMapperProject() contentmapper.Project</c>（program.go:138）。</summary>
    public TypeScript.ContentMapper.Project? ContentMapperProject() => _opts.Host.ContentMapperProject();

    /// <summary>Go: <c>func (p *Program) GetGlobalTypingsCacheLocation() string</c>（program.go:143）。</summary>
    public string GetGlobalTypingsCacheLocation() => _opts.TypingsLocation;

    /// <summary>Go: <c>func (p *Program) GetNearestAncestorDirectoryWithPackageJson(dirname string) string</c>（program.go:148）。</summary>
    public string GetNearestAncestorDirectoryWithPackageJson(string dirname)
    {
        var scoped = _resolver.GetPackageScopeForPath(dirname);
        if (scoped is not null && scoped.Exists())
        {
            return scoped.PackageDirectory;
        }
        return "";
    }

    /// <summary>Go: <c>func (p *Program) GetPackageJsonInfo(pkgJsonPath string) *packagejson.InfoCacheEntry</c>（program.go:157）。</summary>
    public InfoCacheEntry? GetPackageJsonInfo(string pkgJsonPath)
    {
        var directory = GetDirectoryPath(pkgJsonPath);
        var scoped = _resolver.GetPackageScopeForPath(directory);
        if (scoped is not null && scoped.Exists() && scoped.PackageDirectory == directory)
        {
            return scoped;
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Program) PackageJsonCacheEntries(f func(key tspath.Path, value *packagejson.InfoCacheEntry) bool)</c>（program.go:167）。</summary>
    public void PackageJsonCacheEntries(Func<Path, InfoCacheEntry?, bool> f) => _resolver.PackageJsonCacheEntries(f);

    /// <summary>Go: <c>func (p *Program) GetRedirectTargets(path tspath.Path) []string</c>（program.go:173）。</summary>
    public string[] GetRedirectTargets(Path path) =>
        _redirectTargetsMap.TryGetValue(path, out var targets) ? targets : [];

    /// <summary>Go: <c>func (p *Program) GetSourceOfProjectReferenceIfOutputIncluded(file ast.HasFileName) string</c>（program.go:181）。</summary>
    public string GetSourceOfProjectReferenceIfOutputIncluded(HasFileName file) =>
        _outputFileToProjectReferenceSource.TryGetValue(file.Path(), out var source) ? source : file.FileName();

    /// <summary>Go: <c>func (p *Program) GetProjectReferenceFromSource(path tspath.Path) *tsoptions.SourceOutputAndProjectReference</c>（program.go:189）。</summary>
    public SourceOutputAndProjectReference? GetProjectReferenceFromSource(Path path) =>
        _projectReferenceFileMapper.GetProjectReferenceFromSource(path);

    /// <summary>Go: <c>func (p *Program) IsSourceFromProjectReference(path tspath.Path) bool</c>（program.go:194）。</summary>
    public bool IsSourceFromProjectReference(Path path) =>
        _projectReferenceFileMapper.IsSourceFromProjectReference(path);

    /// <summary>Go: <c>func (p *Program) GetProjectReferenceFromOutputDts(path tspath.Path) *tsoptions.SourceOutputAndProjectReference</c>（program.go:198）。</summary>
    public SourceOutputAndProjectReference? GetProjectReferenceFromOutputDts(Path path) =>
        _projectReferenceFileMapper.GetProjectReferenceFromOutputDts(path);

    /// <summary>Go: <c>func (p *Program) GetResolvedProjectReferenceFor(path tspath.Path) (*tsoptions.ParsedCommandLine, bool)</c>（program.go:202）。</summary>
    public bool TryGetResolvedProjectReferenceFor(Path path, out ParsedCommandLine? config) =>
        _projectReferenceFileMapper.TryGetResolvedReferenceFor(path, out config);

    /// <summary>Go: <c>func (p *Program) GetRedirectForResolution(file ast.HasFileName) *tsoptions.ParsedCommandLine</c>（program.go:206）。</summary>
    public ParsedCommandLine? GetRedirectForResolution(HasFileName file) =>
        _projectReferenceFileMapper.GetRedirectForResolution(file).Item1;

    /// <summary>Go: <c>func (p *Program) GetParseFileRedirect(fileName string) string</c>（program.go:211）。</summary>
    public string GetParseFileRedirect(string fileName) =>
        _projectReferenceFileMapper.GetParseFileRedirect(NewHasFileName(fileName, TypeScript.TSPath.TSPath.ToPath(fileName, GetCurrentDirectory(), UseCaseSensitiveFileNames())));

    /// <summary>
    /// Go: <c>func (p *Program) GetResolvedProjectReferences() []*tsoptions.ParsedCommandLine</c>（program.go:215）。
    /// Go 的元素可以是 nil（`task.resolved == nil` 时仍会写进 map）→ C# `ParsedCommandLine?[]`。
    /// </summary>
    public ParsedCommandLine?[] GetResolvedProjectReferences() =>
        _projectReferenceFileMapper.GetResolvedProjectReferences();

    /// <summary>Go: <c>func (p *Program) RangeResolvedProjectReference(f func(path, config, parent, index) bool) bool</c>（program.go:219）。</summary>
    public bool RangeResolvedProjectReference(Func<Path, ParsedCommandLine?, ParsedCommandLine?, int, bool> f) =>
        _projectReferenceFileMapper.RangeResolvedProjectReference(f);

    /// <summary>Go: <c>func (p *Program) RangeResolvedProjectReferenceInChildConfig(...) bool</c>（program.go:223）。</summary>
    public bool RangeResolvedProjectReferenceInChildConfig(
        ParsedCommandLine childConfig,
        Func<Path, ParsedCommandLine?, ParsedCommandLine?, int, bool> f) =>
        _projectReferenceFileMapper.RangeResolvedProjectReferenceInChildConfig(childConfig, f);

    /// <summary>Go: <c>func (p *Program) UseCaseSensitiveFileNames() bool</c>（program.go:231）。</summary>
    public bool UseCaseSensitiveFileNames() => Host().FS().UseCaseSensitiveFileNames();

    /// <summary>Go: <c>func (p *Program) UsesUriStyleNodeCoreModules() core.Tristate</c>（program.go:235）。</summary>
    public Tristate UsesUriStyleNodeCoreModules() => _usesUriStyleNodeCoreModules;

    /// <summary>
    /// Go: <c>func (p *Program) GetSourceFileFromReference(origin *ast.SourceFile, ref *ast.FileReference) *ast.SourceFile</c>（program.go:242）。
    /// 行为与 <c>processSourceFile</c> 类似，但没有诊断、也不改状态。
    /// </summary>
    public SourceFile? GetSourceFileFromReference(SourceFile origin, FileReference reference)
    {
        var fileName = ResolvePath(GetDirectoryPath(origin.FileName()), reference.FileName);
        var supportedExtensionsBase = GetSupportedExtensions(Options(), CommandLine().ContentMapperExtensions());
        var supportedExtensions = GetSupportedExtensionsWithJsonIfResolveJsonModule(Options(), supportedExtensionsBase);
        var allowNonTsExtensions = Options().AllowNonTsExtensions.IsTrue();
        if (HasExtension(fileName))
        {
            if (!allowNonTsExtensions)
            {
                var canonicalFileName = GetCanonicalFileName(fileName, UseCaseSensitiveFileNames());
                var supported = false;
                foreach (var group in supportedExtensions)
                {
                    if (FileExtensionIsOneOf(canonicalFileName, group))
                    {
                        supported = true;
                        break;
                    }
                }
                if (!supported)
                {
                    return null; // 不支持的扩展名一律失败
                }
            }

            return GetSourceFileForResolvedModule(fileName);
        }

        if (allowNonTsExtensions)
        {
            var extensionless = GetSourceFileForResolvedModule(fileName);
            if (extensionless is not null)
            {
                return extensionless;
            }
        }

        // 只从第一组受支持的扩展名（应当是 .ts/.tsx/.d.ts）里尝试追加扩展名
        foreach (var extension in supportedExtensions[0])
        {
            var result = GetSourceFileForResolvedModule(fileName + extension);
            if (result is not null)
            {
                return result;
            }
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Program) initCheckerPool()</c>（program.go:435）。</summary>
    internal void InitCheckerPool()
    {
        if (!_finishedProcessing)
        {
            Debug.Fail("Program must finish processing files before initializing checker pool");
        }

        if (_opts.CreateCheckerPool is not null)
        {
            _checkerPool = _opts.CreateCheckerPool(this);
        }
        else
        {
            var pool = Compiler.NewCheckerPoolWithTracing(this, _opts.Tracing);
            _checkerPool = pool;
            _compilerCheckerPool = pool;
        }
    }

    /// <summary>Go: <c>func (p *Program) GetCheckerPool() CheckerPool</c>（program.go:450）。</summary>
    public CheckerPool GetCheckerPool() => _checkerPool;

    /// <summary>Go: <c>func (p *Program) canReplaceFileInProgram(file1, file2 *ast.SourceFile) bool</c>（program.go:454）。</summary>
    private bool CanReplaceFileInProgram(SourceFile file1, SourceFile? file2)
    {
        return file2 is not null &&
            file1.ParseOptions() == file2.ParseOptions() &&
            file1.ScriptKind == file2.ScriptKind &&
            IsExternalOrCommonJSModule(file1) == IsExternalOrCommonJSModule(file2) &&
            file1.UsesUriStyleNodeCoreModules == file2.UsesUriStyleNodeCoreModules &&
            EqualFunc(file1.Imports(), file2.Imports(), (n1, n2) =>
                EqualModuleSpecifiers(n1, n2) &&
                GetModeForUsageLocation(file1, n1) == GetModeForUsageLocation(file2, n2)) &&
            EqualFunc(file1.ModuleAugmentations, file2.ModuleAugmentations, EqualModuleAugmentationNames) &&
            Same(file1.AmbientModuleNames, file2.AmbientModuleNames) &&
            EqualFunc(file1.ReferencedFiles, file2.ReferencedFiles, EqualFileReferences) &&
            EqualFunc(file1.TypeReferenceDirectives, file2.TypeReferenceDirectives, EqualFileReferences) &&
            EqualFunc(file1.LibReferenceDirectives, file2.LibReferenceDirectives, EqualFileReferences) &&
            EqualCheckJSDirectives(file1.CheckJsDirective, file2.CheckJsDirective);
    }

    /// <summary>Go: <c>func (p *Program) needsImportHelpersImportSpecifier(file *ast.SourceFile) bool</c>（program.go:472）。</summary>
    private bool NeedsImportHelpersImportSpecifier(SourceFile file)
    {
        var (redirect, _) = _projectReferenceFileMapper.GetRedirectForResolution(file);
        var optionsForFile = GetCompilerOptionsWithRedirect(_opts.Config.CompilerOptions()!, redirect);
        if (!optionsForFile.ImportHelpers.IsTrue())
        {
            return false;
        }

        var isJavaScriptFile = IsSourceFileJS(file);
        var isExternalModuleFile = IsExternalModule(file);
        if (!isJavaScriptFile && (file.IsDeclarationFile || (!optionsForFile.GetIsolatedModules() && !isExternalModuleFile)))
        {
            return false;
        }
        return true;
    }

    /// <summary>Go: <c>func (p *Program) jsxRuntimeImportSpecifier(file *ast.SourceFile) string</c>（program.go:486）。</summary>
    private string JsxRuntimeImportSpecifier(SourceFile file)
    {
        if (!IsSourceFileJS(file) && file.ScriptKind != ScriptKind.TSX)
        {
            return "";
        }
        var (redirect, _) = _projectReferenceFileMapper.GetRedirectForResolution(file);
        var optionsForFile = GetCompilerOptionsWithRedirect(_opts.Config.CompilerOptions()!, redirect);
        return GetJSXRuntimeImport(GetJSXImplicitImportBase(optionsForFile, file), optionsForFile);
    }

    /// <summary>Go: <c>func equalModuleSpecifiers(n1, n2 *ast.Node) bool</c>（program.go:495）——包级函数。</summary>
    internal static bool EqualModuleSpecifiers(Node n1, Node n2) =>
        n1.Kind == n2.Kind && (!IsStringLiteral(n1) || n1.Text() == n2.Text());

    /// <summary>Go: <c>func equalModuleAugmentationNames(n1, n2 *ast.Node) bool</c>（program.go:499）——包级函数。</summary>
    private static bool EqualModuleAugmentationNames(Node n1, Node n2) =>
        n1.Kind == n2.Kind && n1.Text() == n2.Text();

    /// <summary>Go: <c>func equalFileReferences(f1, f2 *ast.FileReference) bool</c>（program.go:503）——包级函数。</summary>
    private static bool EqualFileReferences(FileReference f1, FileReference f2) =>
        f1.FileName == f2.FileName && f1.ResolutionMode == f2.ResolutionMode && f1.Preserve == f2.Preserve;

    /// <summary>Go: <c>func equalCheckJSDirectives(d1, d2 *ast.CheckJsDirective) bool</c>（program.go:507）——包级函数。</summary>
    private static bool EqualCheckJSDirectives(CheckJsDirective? d1, CheckJsDirective? d2) =>
        d1 is null && d2 is null || d1 is not null && d2 is not null && d1.Enabled == d2.Enabled;

    /// <summary>Go 的 <c>slices.EqualFunc</c>（C# 侧 Core 没有对应物，逐元素比较）。</summary>
    internal static bool EqualFunc<T>(T[]? a, T[]? b, Func<T, T, bool> equals) where T : class
    {
        a ??= [];
        b ??= [];
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (!equals(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Go: <c>func (p *Program) SourceFiles() []*ast.SourceFile</c>（program.go:511）。</summary>
    public SourceFile[] SourceFiles() => _files;

    /// <summary>Go: <c>func (p *Program) DuplicateSourceFiles() []*DuplicateSourceFile</c>（program.go:512）。</summary>
    public DuplicateSourceFile[] DuplicateSourceFiles() => _duplicateSourceFiles;

    /// <summary>Go: <c>func (p *Program) Options() *core.CompilerOptions</c>（program.go:513）。</summary>
    public CompilerOptions Options() => _opts.Config.CompilerOptions()!;

    /// <summary>
    /// Go: <c>func (p *Program) GetContentMapper(file *ast.SourceFile) *contentmapper.Mapper</c>（program.go:517）：
    /// 返回产生该文件的 content mapper，没有则 nil。
    /// </summary>
    public TypeScript.ContentMapper.Mapper? GetContentMapper(SourceFile file)
    {
        if (file.ContentMapper() == "")
        {
            return null;
        }
        var mapper = _opts.Config.GetContentMapperForFileName(file.FileName());
        if (mapper is not null && mapper.Identity() == file.ContentMapper())
        {
            return mapper;
        }
        return null;
    }

    /// <summary>Go: <c>func (p *Program) ContentMapperExtensions() []string</c>（program.go:528）。</summary>
    public string[] ContentMapperExtensions() => _opts.Config.ContentMapperExtensions();

    /// <summary>Go: <c>func (p *Program) CommandLine() *tsoptions.ParsedCommandLine</c>（program.go:529）。</summary>
    public ParsedCommandLine CommandLine() => _opts.Config;

    /// <summary>Go: <c>func (p *Program) Host() CompilerHost</c>（program.go:530）。</summary>
    public CompilerHost Host() => _opts.Host;

    /// <summary>Go: <c>func (p *Program) Tracing() *tracing.Tracing</c>（program.go:531）。</summary>
    public TypeScript.Tracing.Tracing? Tracing() => _opts.Tracing;

    /// <summary>Go: <c>func (p *Program) GetConfigFileParsingDiagnostics() []*ast.Diagnostic</c>（program.go:532）。</summary>
    public Diagnostic[] GetConfigFileParsingDiagnostics() =>
        [.. _opts.Config.GetConfigFileParsingDiagnostics()];

    /// <summary>Go: <c>func (p *Program) GetUnresolvedImports() *collections.Set[string]</c>（program.go:538）：结果缓存、只算一次。</summary>
    public Set<string>? GetUnresolvedImports() => _unresolvedImports.GetValue(ExtractUnresolvedImports);

    /// <summary>Go: <c>func (p *Program) extractUnresolvedImports() *collections.Set[string]</c>（program.go:542）。</summary>
    private Set<string> ExtractUnresolvedImports()
    {
        var unresolvedSet = new Set<string>();

        foreach (var sourceFile in _files)
        {
            var unresolvedImports = ExtractUnresolvedImportsFromSourceFile(sourceFile);
            foreach (var import in unresolvedImports)
            {
                unresolvedSet.Add(import);
            }
        }

        return unresolvedSet;
    }

    /// <summary>Go: <c>func (p *Program) extractUnresolvedImportsFromSourceFile(file *ast.SourceFile) []string</c>（program.go:555）。</summary>
    private List<string> ExtractUnresolvedImportsFromSourceFile(SourceFile file)
    {
        var unresolvedImports = new List<string>();

        if (_resolvedModules.TryGetValue(file.Path(), out var resolvedModules))
        {
            foreach (var (cacheKey, resolution) in resolvedModules)
            {
                var resolved = resolution.IsResolved();
                if ((!resolved || !ExtensionIsOneOf(resolution.Extension, SupportedTSExtensionsWithJsonFlat)) &&
                    !IsExternalModuleNameRelative(cacheKey.Name))
                {
                    unresolvedImports.Add(cacheKey.Name);
                }
            }
        }

        return unresolvedImports;
    }

    /// <summary>Go: <c>func (p *Program) SingleThreaded() bool</c>（program.go:570）。</summary>
    public bool SingleThreaded() => _opts.SingleThreaded.DefaultIfUnknown(Options().SingleThreaded).IsTrue();

    /// <summary>Go 的 <c>&amp;Program{processedFiles: …}</c>（内嵌 struct 的整体浅拷贝）→ 逐字段拷贝。</summary>
    internal void CopyProcessedFiles(ProcessedFiles from)
    {
        _resolver = from._resolver;
        _files = from._files;
        _duplicateSourceFiles = from._duplicateSourceFiles;
        _filesByPath = from._filesByPath;
        _projectReferenceFileMapper = from._projectReferenceFileMapper;
        _missingFiles = from._missingFiles;
        _resolvedModules = from._resolvedModules;
        _typeResolutionsInFile = from._typeResolutionsInFile;
        _sourceFileMetaDatas = from._sourceFileMetaDatas;
        _jsxRuntimeImportSpecifiers = from._jsxRuntimeImportSpecifiers;
        _importHelpersImportSpecifiers = from._importHelpersImportSpecifiers;
        _libFiles = from._libFiles;
        _sourceFilesFoundSearchingNodeModules = from._sourceFilesFoundSearchingNodeModules;
        _includeProcessor = from._includeProcessor;
        _outputFileToProjectReferenceSource = from._outputFileToProjectReferenceSource;
        _redirectTargetsMap = from._redirectTargetsMap;
        _redirectFilesByPath = from._redirectFilesByPath;
        _contentMapperDiagnostics = from._contentMapperDiagnostics;
        _moduleResolutionError = from._moduleResolutionError;
        _finishedProcessing = from._finishedProcessing;
    }

    /// <summary>
    /// Go: <c>func (p *Program) UpdateProgram(changedFilePath, newHost, createCheckerPool, createModuleResolver) (*Program, *ast.SourceFile, bool)</c>（program.go:305）：
    /// 返回"已知只有 changedFilePath 变了"的新程序，外加"旧程序数据是否被复用"的布尔值。
    /// </summary>
    public (Program Result, SourceFile? NewFile, bool Reused) UpdateProgram(
        Path changedFilePath,
        CompilerHost newHost,
        Func<Program, CheckerPool>? createCheckerPool,
        Func<ResolverOptions, Resolver>? createModuleResolver)
    {
        var (result, newFile, reused) = ReuseProgram(changedFilePath, newHost, createCheckerPool, createModuleResolver);
        if (reused)
        {
            return (result!, newFile, true);
        }

        var newOpts = _opts;
        newOpts.Host = newHost;
        if (createCheckerPool is not null)
        {
            newOpts.CreateCheckerPool = createCheckerPool;
        }
        if (createModuleResolver is not null)
        {
            newOpts.CreateModuleResolver = createModuleResolver;
        }
        return (Compiler.NewProgram(newOpts), newFile, false);
    }

    /// <summary>
    /// Go: <c>func (p *Program) ReuseProgram(changedFilePath, newHost, createCheckerPool, createModuleResolver) (*Program, *ast.SourceFile, bool)</c>（program.go:332）：
    /// 只就地替换 changedFilePath、复用其余部分；失败时返回 <c>(null, newFile, false)</c>（不做整体重建）。
    /// </summary>
    public (Program? Result, SourceFile? NewFile, bool Reused) ReuseProgram(
        Path changedFilePath,
        CompilerHost newHost,
        Func<Program, CheckerPool>? createCheckerPool,
        Func<ResolverOptions, Resolver>? createModuleResolver)
    {
        var newOpts = _opts;
        newOpts.Host = newHost;
        if (createCheckerPool is not null)
        {
            newOpts.CreateCheckerPool = createCheckerPool;
        }
        if (createModuleResolver is not null)
        {
            newOpts.CreateModuleResolver = createModuleResolver;
        }
        var oldFile = _filesByPath[changedFilePath];
        SourceFile? newFile;
        SourceFile[] oldSupplementalFiles = [];
        SourceFile[] newSupplementalFiles = [];
        if (oldFile.ContentMapper() != "")
        {
            // content-mapped 文件是跑外部转换产出的，单纯重解析复现不了。通过 host 重跑转换；
            // 任何失败（或文件缺失）都回退到整体重建，从而走 file loader 的失败策略。
            var mapper = newOpts.Config.GetContentMapperForFileName(oldFile.FileName());
            try
            {
                var files = newHost.GetContentMappedSourceFiles(oldFile.ParseOptions(), mapper!);
                newFile = files.Canonical;
                oldSupplementalFiles = oldFile.SupplementalSourceFiles();
                newSupplementalFiles = files.Supplemental;
            }
            catch (Exception)
            {
                // Go: `if err != nil { return nil, nil, false }`（error → 异常，§5.4）
                return (null, null, false);
            }
        }
        else
        {
            newFile = newHost.GetSourceFile(oldFile.ParseOptions());
        }

        // 若该文件属于包重定向组（同一包装在多个 node_modules 位置），需要整体重建：
        // 重定向目标可能要重算。
        var inRedirectFiles = _redirectFilesByPath?.ContainsKey(changedFilePath) ?? false;
        var isRedirectTarget = _redirectTargetsMap.ContainsKey(changedFilePath);
        if (inRedirectFiles || isRedirectTarget)
        {
            return (null, newFile, false);
        }

        if (!CanReplaceFileInProgram(oldFile, newFile))
        {
            return (null, newFile, false);
        }
        // 克隆不会重算合成的 helper / JSX-runtime import 记账：任一版本需要这些 import 就整体重建。
        // （`newFile!`：Go 里上一行的 `canReplaceFileInProgram(file1, file2)` 已判定 file2 != nil。）
        if ((_importHelpersImportSpecifiers?.ContainsKey(oldFile.Path()) ?? false) || NeedsImportHelpersImportSpecifier(newFile!))
        {
            return (null, newFile, false);
        }
        if ((_jsxRuntimeImportSpecifiers?.ContainsKey(oldFile.Path()) ?? false) || JsxRuntimeImportSpecifier(newFile!) != "")
        {
            return (null, newFile, false);
        }
        if (oldSupplementalFiles.Length != newSupplementalFiles.Length)
        {
            return (null, newFile, false);
        }
        for (var i = 0; i < oldSupplementalFiles.Length; i++)
        {
            var oldSupplemental = oldSupplementalFiles[i];
            var newSupplemental = newSupplementalFiles[i];
            if (oldSupplemental.Path() != newSupplemental.Path() ||
                !CanReplaceFileInProgram(oldSupplemental, newSupplemental))
            {
                return (null, newFile, false);
            }
            if ((_importHelpersImportSpecifiers?.ContainsKey(oldSupplemental.Path()) ?? false) || NeedsImportHelpersImportSpecifier(newSupplemental))
            {
                return (null, newFile, false);
            }
            if ((_jsxRuntimeImportSpecifiers?.ContainsKey(oldSupplemental.Path()) ?? false) || JsxRuntimeImportSpecifier(newSupplemental) != "")
            {
                return (null, newFile, false);
            }
        }

        // TODO: 配置变化时要重新校验编译选项吗？
        var result = new Program(newOpts)
        {
            _comparePathsOptions = _comparePathsOptions,
            _usesUriStyleNodeCoreModules = _usesUriStyleNodeCoreModules,
            _programDiagnostics = _programDiagnostics,
            _hasEmitBlockingDiagnostics = _hasEmitBlockingDiagnostics,
            _contentMapperOptionDiagnostics = (Diagnostic[])_contentMapperOptionDiagnostics.Clone(),
        };
        result.CopyProcessedFiles(this);
        result._unresolvedImports.TryReuse(_unresolvedImports);
        result._knownSymlinks.TryReuse(_knownSymlinks);
        result._packageNames.TryReuse(_packageNames);
        result.InitCheckerPool();
        var index = FindIndex(result._files, file => file.Path() == newFile!.Path());
        result._files = (SourceFile[])result._files.Clone();
        result._files[index] = newFile!;
        result._filesByPath = new Dictionary<Path, SourceFile>(result._filesByPath);
        result._filesByPath[newFile!.Path()] = newFile;
        if (oldSupplementalFiles.Length != 0)
        {
            for (var i = 0; i < oldSupplementalFiles.Length; i++)
            {
                var oldSupplemental = oldSupplementalFiles[i];
                var newSupplemental = newSupplementalFiles[i];
                var supplementalIndex = FindIndex(result._files, file => ReferenceEquals(file, oldSupplemental));
                result._files[supplementalIndex] = newSupplemental;
                result._filesByPath[newSupplemental.Path()] = newSupplemental;
            }
        }
        Compiler.UpdateFileIncludeProcessor(result);
        return (result, newFile, true);
    }
}

/// <summary>program.go 的包级工厂（`Compiler` 静态类的 partial，见 Pkg.cs）。</summary>
public static partial class Compiler
{
    /// <summary>Go: <c>func NewProgram(opts ProgramOptions) *Program</c>（program.go:285）。</summary>
    public static Program NewProgram(ProgramOptions opts)
    {
        var p = new Program(opts);
        // Go: `defer p.opts.Tracing.Push(...)()`（先求值 Push，再把返回的停止函数延后执行）
        Action doneTrace = static () => { };
        if (p._opts.Tracing is not null)
        {
            doneTrace = p._opts.Tracing.Push(
                Phase.Program,
                "createProgram",
                new Dictionary<string, object?> { ["configFilePath"] = opts.Config.CompilerOptions()!.ConfigFilePath },
                true);
        }
        try
        {
            p.CopyProcessedFiles(ProcessAllProgramFiles(p._opts, p.SingleThreaded()));
            p.InitCheckerPool();
            p.VerifyCompilerOptions();
            p.CollectContentMapperOptionDiagnostics();
            return p;
        }
        finally
        {
            doneTrace();
        }
    }
}
