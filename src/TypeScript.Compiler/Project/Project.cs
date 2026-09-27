// ported from: internal/project/project.go:1-678
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **包级函数落点**：Go 的 `project` 包名与类型 `Project` 同名，C# 里不可能再声明 `static class Project`
//    （CS0101）⇒ 包级函数挂到"它构造/返回的类型"的静态成员上（仓内先例：`Locale.Parse`/`Locale.WithLocale`，
//    见 Locale/Locale.cs）。本文件里：
//      `NewConfiguredProject`/`NewInferredProject`/`NewProject` → `Project.NewConfiguredProject/…`；
//      `newSyntheticProject`(未导出) → `Project.NewSyntheticProject`；`newInferredProjectCommandLine` → `Project.NewInferredProjectCommandLine`；
//      `NewSyntheticProjectID`/`ParseSyntheticProjectID` → `SyntheticProjectID.NewSyntheticProjectID/ParseSyntheticProjectID`；
//      `ParseConfiguredProjectID` → `ConfiguredProjectID.ParseConfiguredProjectID`。
//
// 2) **`Kind`/`ProgramUpdateKind` 的枚举成员名与字段同名**：Go 里 `Kind` 既是类型也是字段名（`p.Kind`），
//    枚举常量是包级的（`KindInferred`…）。C# 里枚举成员必须挂在枚举类型下，而类内简单名 `Kind` 会先解析到
//    **字段** ⇒ 本文件用 `using static TypeScript.Project.Kind;` / `using static TypeScript.Project.ProgramUpdateKind;`
//    把常量引入作用域，于是 `KindInferred` 这样的写法与 Go 逐字一致。**本包其它文件如需裸写这些常量，
//    请照做**（广播里已说明）。
//
// 3) **字符串型名值类型 → `readonly record struct`**：Go 的 `type ID string`（以及 `ConfiguredProjectID`/
//    `InferredProjectID`/`SyntheticProjectID`）是"以 string 为底层类型的可比较值"。C# 没有这种类型，
//    用 `readonly record struct` + 一个 `Value` 字段表达：
//      - 值相等语义（**必须**：`autoimport` 的 `map[ProjectID]X` 依赖底层值相等，见 M8LsAutoImport 的要求）；
//      - `String()` 方法照 Go 保留（`autoimport.ProjectID` 接口就要求 `String()`）；
//      - `ID` 显式实现 `LsAutoImport.ProjectID`（Go 侧是隐式满足；C# 需要显式声明）。
//      - 转换：入向 implicit（Go 的 `ID(x)`）、出向 explicit（Go 的 `string(id)`）。
//
// 4) `p.commandLineWithTypingsFilesOnce sync.Once` → “标志 + `System.Threading.Lock` 双重检查”
//    （仓内既有做法，见 Compiler/CheckerPool.cs、Checker/Checker.New.cs）；`SetCommandLine` 里
//    `= sync.Once{}` → 重置标志。
//
// 5) `CreateProgram` 的 `defer func(){ if cleanupModuleResolver != nil { cleanupModuleResolver() } }()` →
//    `try/finally`（Go 的 defer 语义：函数返回时恰好执行一次）。
//
// 6) Go 的 `p.Program == nil` 判空在 C# 里是 `p.Program is null`；Go 的 `p.Program.GetSourceFileByPath(path) != nil`
//    → `GetSourceFileByPath(path) is not null`（Go 的 `*T` → `T?`）。
//
// 7) `slices.Concat(a, b, c)` → `[.. a, .. b, .. c]`（集合表达式；顺序与 Go 一致，`SortAndDeduplicateDiagnostics`
//    会排序去重，故拼接顺序不影响结果）。`slices.SortFunc` 的稳定排序在 C# 用 `OrderBy`（LINQ 的 OrderBy 稳定）。
//
// 8) `p.print(...)` 的 `*strings.Builder` → `StringBuilder`；`p.log(msg string)` 是 Go 里的空实现（`// !!!`），
//    C# 保留为空的 `Log` 方法（`Action<string>` 形态被 `NewCheckerPool` 需要）。
//
// 9) `p.toPath` 用 `TSPath.ToPath(fileName, currentDirectory, useCaseSensitive)`；`tspath.*` 一律走
//    `using static TypeScript.TSPath.TSPath;` 的裸名（`ToPath`/`GetDirectoryPath`/…），避免 `TSPath` 类与
//    命名空间同名带来的 CS0118。
//
// 10) `TestCommandLine()`（Go 里没有）不移植。`//go:generate` 注释不移植。
using System.Text;
using Ata = TypeScript.Project.Ata;
using JsonDecoder = TypeScript.Json.Decoder;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Core.Core;
using static TypeScript.Project.Kind;
using static TypeScript.Project.ProgramUpdateKind;
using static TypeScript.TSPath.TSPath;

namespace TypeScript.Project;

public static partial class ProjectConstants
{
    internal const string InferredProjectName = "/dev/null/inferred"; // lowercase so toPath is a no-op regardless of settings

    internal const string SyntheticProjectPrefix = "/dev/null/synthetic/";

    internal const string Hr = "-----------------------------------------------";
}

/// <summary>Go 的 <c>type ID string</c>（同时满足 <c>autoimport.ProjectID</c> 与 <c>ata.ProjectID</c>，两者都只要求 String()）。</summary>
public readonly record struct ID(string Value) : LsAutoImport.ProjectID, Ata.ProjectID
{
    /// <summary>Go: <c>func (id ID) String() string</c>（同时满足 <c>autoimport.ProjectID</c>/<c>ata.ProjectID</c>）。</summary>
    public string String()
    {
        return Value;
    }

    public override string ToString()
    {
        return Value;
    }

    /// <summary>Go 的 <c>string(id)</c>（出向显式）。</summary>
    public static explicit operator string(ID id)
    {
        return id.Value;
    }

    /// <summary>Go 的 <c>ID(value)</c>（入向隐式，便于调用点贴近 Go）。</summary>
    public static implicit operator ID(string value)
    {
        return new ID(value);
    }

    /// <summary>Go: <c>func (id ID) Configured() (ConfiguredProjectID, bool)</c>。</summary>
    public bool TryConfigured(out ConfiguredProjectID result)
    {
        return ConfiguredProjectID.ParseConfiguredProjectID(new Path(Value), out result);
    }

    /// <summary>Go: <c>func (id ID) Inferred() (InferredProjectID, bool)</c>。</summary>
    public bool TryInferred(out InferredProjectID result)
    {
        result = InferredProjectID.InferredProjectIDConst;
        return this == result.AsID();
    }

    /// <summary>Go: <c>func (id ID) Synthetic() (SyntheticProjectID, bool)</c>。</summary>
    public bool TrySynthetic(out SyntheticProjectID result)
    {
        return SyntheticProjectID.ParseSyntheticProjectID(Value, out result);
    }
}

/// <summary>Go 的 <c>type ConfiguredProjectID tspath.Path</c>。</summary>
public readonly record struct ConfiguredProjectID(Path Value)
{
    public Path Path()
    {
        return Value;
    }

    public ID AsID()
    {
        return new ID(Value.Value);
    }

    /// <summary>Go: <c>func ParseConfiguredProjectID(value tspath.Path) (ConfiguredProjectID, bool)</c>。</summary>
    public static bool ParseConfiguredProjectID(Path value, out ConfiguredProjectID result)
    {
        result = default;
        var id = new ID(value.Value);
        if (id.Value.Length == 0)
        {
            return false;
        }

        if (id.TryInferred(out _))
        {
            return false;
        }

        if (id.TrySynthetic(out _))
        {
            return false;
        }

        result = new ConfiguredProjectID(value);
        return true;
    }
}

/// <summary>Go 的 <c>type InferredProjectID string</c>。</summary>
public readonly record struct InferredProjectID(string Value)
{
    internal static readonly InferredProjectID InferredProjectIDConst = new(ProjectConstants.InferredProjectName);

    public ID AsID()
    {
        return new ID(Value);
    }

    public string String()
    {
        return Value;
    }
}

/// <summary>Go 的 <c>type SyntheticProjectID string</c>（**非** readonly：Go 的 <c>UnmarshalJSONFrom</c> 要写回 <c>*id</c>）。</summary>
public record struct SyntheticProjectID(string Value) : Json.IJsonUnmarshaler
{
    /// <summary>Go: <c>func NewSyntheticProjectID(id int) SyntheticProjectID</c>。</summary>
    public static SyntheticProjectID NewSyntheticProjectID(int id)
    {
        if (id <= 0)
        {
            Debug.Fail($"invalid synthetic project ID: {id}");
        }

        return new SyntheticProjectID(ProjectConstants.SyntheticProjectPrefix + id);
    }

    public ID AsID()
    {
        return new ID(Value);
    }

    public string String()
    {
        return Value;
    }

    /// <summary>Go: <c>func (id *SyntheticProjectID) UnmarshalJSONFrom(dec *json.Decoder) error</c>。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var value = dec.Decode<string>() ?? "";
        if (!ParseSyntheticProjectID(value, out var parsed))
        {
            throw new Json.JsonException($"invalid synthetic project ID: {value}");
        }

        this = parsed;
    }

    /// <summary>Go: <c>func ParseSyntheticProjectID(value string) (SyntheticProjectID, bool)</c>。</summary>
    public static bool ParseSyntheticProjectID(string value, out SyntheticProjectID result)
    {
        result = default;
        if (!value.StartsWith(ProjectConstants.SyntheticProjectPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = value[ProjectConstants.SyntheticProjectPrefix.Length..];
        if (!int.TryParse(suffix, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            return false;
        }

        result = NewSyntheticProjectID(id);
        return true;
    }
}

/// <summary>Go 的 <c>type Kind int</c>。</summary>
public enum Kind
{
    KindInferred = 0,
    KindConfigured = 1,
    KindSynthetic = 2,
}

/// <summary>Go 的 <c>type ProgramUpdateKind int</c>。</summary>
public enum ProgramUpdateKind
{
    ProgramUpdateKindNone = 0,
    ProgramUpdateKindCloned = 1,
    ProgramUpdateKindSameFileNames = 2,
    ProgramUpdateKindNewFiles = 3,
}

/// <summary>Go 的 <c>type PendingReload int</c>。</summary>
public enum PendingReload
{
    PendingReloadNone = 0,
    PendingReloadFileNames = 1,
    PendingReloadFull = 2,
}

/// <summary>
/// Go 的 <c>Project</c>。If changing struct fields, also update the Clone method（见 <see cref="Clone"/>）。
/// </summary>
public sealed class Project : Ls.Project, Dirty.Cloneable<Project>
{
    public Kind Kind;

    internal ID _id;
    internal string _currentDirectory = "";
    internal string _configFileName = "";
    internal Path _configFilePath;

    internal bool _dirty;
    internal Path _dirtyFilePath;

    internal CompilerHost? _host;
    public TsOptions.ParsedCommandLine? CommandLine;
    internal TsOptions.ParsedCommandLine? _commandLineWithTypingsFiles;
    internal readonly Lock _commandLineWithTypingsFilesOnceLock = new();
    internal bool _commandLineWithTypingsFilesOnceDone;
    public Compiler.Program? Program;

    /// <summary>The kind of update that was performed on the program last time it was updated。</summary>
    public ProgramUpdateKind ProgramUpdateKind;

    /// <summary>The ID of the snapshot that created the program stored in this project。</summary>
    public ulong ProgramLastUpdate;

    /// <summary>
    /// Set of projects that this project could be referencing.
    /// Only set before actually loading config file to get actual project references.
    /// </summary>
    internal Collections.Set<Path>? _potentialProjectReferences;

    internal WatchedFiles<Collections.SyncSet<Path>>? _programFilesWatch;
    internal WatchedFiles<PatternsAndIgnored>? _typingsWatch;
    internal WatchedFiles<string[]>? _contentMapperWatch;
    internal Collections.Set<Path>? _contentMapperWatchedFiles;

    internal CheckerPool? _checkerPool;

    internal ModuleResolverFactory? _moduleResolverFactory;
    internal ulong _moduleResolverID;

    /// <summary>
    /// installedTypingsInfo is the value of <c>project.ComputeTypingsInfo()</c> that was
    /// used during the most recently completed typings installation.
    /// </summary>
    internal Ata.TypingsInfo? _installedTypingsInfo;

    /// <summary>typingsFiles are the root files added by the typings installer。</summary>
    internal string[] _typingsFiles = [];

    internal Project()
    {
    }

    /// <summary>Go: <c>func NewConfiguredProject(configFileName string, configFilePath tspath.Path, builder *ProjectCollectionBuilder, logger *logging.LogTree) *Project</c>。</summary>
    public static Project NewConfiguredProject(
        string configFileName,
        Path configFilePath,
        ProjectCollectionBuilder builder,
        Logging.LogTree? logger)
    {
        if (!ConfiguredProjectID.ParseConfiguredProjectID(configFilePath, out var configuredProjectID))
        {
            Debug.Fail($"invalid configured project ID: {configFilePath.Value}");
        }

        var project = NewProject(configuredProjectID.AsID(), Kind.KindConfigured, GetDirectoryPath(configFileName), builder, logger);
        project._configFileName = configFileName;
        project._configFilePath = configFilePath;
        return project;
    }

    /// <summary>Go: <c>func NewInferredProject(currentDirectory string, compilerOptions *core.CompilerOptions, rootFileNames []string, projectReferences []*core.ProjectReference, contentMappers []*contentmapper.Mapper, builder *ProjectCollectionBuilder, logger *logging.LogTree) *Project</c>。</summary>
    public static Project NewInferredProject(
        string currentDirectory,
        Core.CompilerOptions? compilerOptions,
        string[] rootFileNames,
        Core.ProjectReference[] projectReferences,
        ContentMapper.Mapper[] contentMappers,
        ProjectCollectionBuilder builder,
        Logging.LogTree? logger)
    {
        var p = NewProject(InferredProjectID.InferredProjectIDConst.AsID(), Kind.KindInferred, currentDirectory, builder, logger);
        if (compilerOptions is null)
        {
            compilerOptions = new Core.CompilerOptions
            {
                AllowJs = Core.Tristate.TSTrue,
                Module = Core.ModuleKind.ESNext,
                ModuleResolution = Core.ModuleResolutionKind.Bundler,
                Target = ScriptTargetLatestStandard,
                Jsx = Core.JsxEmit.ReactJSX,
                AllowImportingTsExtensions = Core.Tristate.TSTrue,
                StrictNullChecks = Core.Tristate.TSTrue,
                StrictFunctionTypes = Core.Tristate.TSTrue,
                SourceMap = Core.Tristate.TSTrue,
                AllowNonTsExtensions = Core.Tristate.TSTrue,
                ResolveJsonModule = Core.Tristate.TSTrue,
            };
        }

        p.CommandLine = NewInferredProjectCommandLine(
            compilerOptions,
            rootFileNames,
            projectReferences,
            contentMappers,
            new TSPath.ComparePathsOptions(
                useCaseSensitiveFileNames: builder._fs._fs.UseCaseSensitiveFileNames(),
                currentDirectory: currentDirectory));
        return p;
    }

    /// <summary>Go: <c>func newSyntheticProject(...)</c>（未导出）。</summary>
    internal static Project NewSyntheticProject(
        SyntheticProjectID id,
        string currentDirectory,
        Core.CompilerOptions compilerOptions,
        string[] rootFileNames,
        Core.ProjectReference[] projectReferences,
        ContentMapper.Mapper[] contentMappers,
        ProjectCollectionBuilder builder,
        Logging.LogTree? logger)
    {
        var project = NewProject(id.AsID(), Kind.KindSynthetic, currentDirectory, builder, logger);
        project.CommandLine = NewInferredProjectCommandLine(
            compilerOptions,
            rootFileNames,
            projectReferences,
            contentMappers,
            new TSPath.ComparePathsOptions(
                useCaseSensitiveFileNames: builder._fs._fs.UseCaseSensitiveFileNames(),
                currentDirectory: currentDirectory));
        return project;
    }

    /// <summary>Go: <c>func newInferredProjectCommandLine(...)</c>（未导出）。</summary>
    internal static TsOptions.ParsedCommandLine NewInferredProjectCommandLine(
        Core.CompilerOptions compilerOptions,
        string[] rootFileNames,
        Core.ProjectReference[] projectReferences,
        ContentMapper.Mapper[] contentMappers,
        TSPath.ComparePathsOptions comparePathsOptions)
    {
        var commandLine = TsOptions.ParsedCommandLine.NewParsedCommandLine(compilerOptions, rootFileNames, projectReferences, comparePathsOptions);
        commandLine.ParsedConfig.ContentMappers = contentMappers;
        return commandLine;
    }

    /// <summary>Go: <c>func NewProject(id ID, kind Kind, currentDirectory string, builder *ProjectCollectionBuilder, logger *logging.LogTree) *Project</c>。</summary>
    public static Project NewProject(
        ID id,
        Kind kind,
        string currentDirectory,
        ProjectCollectionBuilder builder,
        Logging.LogTree? logger)
    {
        logger?.Log($"Creating {kind.String()}Project: {id.Value}, currentDirectory: {currentDirectory}");
        var project = new Project
        {
            Kind = kind,
            _id = id,
            _currentDirectory = currentDirectory,
            _dirty = true,
        };

        project._programFilesWatch = WatchedFiles<Collections.SyncSet<Path>>.NewWatchedFiles(
            "program files for " + id.Value,
            LsProto.WatchKind.Create | LsProto.WatchKind.Change | LsProto.WatchKind.Delete,
            LsProto.LsProto.GetClientCapabilities().Workspace.DidChangeWatchedFiles.RelativePatternSupport,
            Watch.CreateResolutionLookupGlobMapper(builder._sessionOptions.CurrentDirectory, builder._sessionOptions.DefaultLibraryPath, project._currentDirectory, builder._fs._fs.UseCaseSensitiveFileNames()));
        if (builder._sessionOptions.TypingsLocation.Length != 0)
        {
            project._typingsWatch = WatchedFiles<PatternsAndIgnored>.NewWatchedFiles(
                "typings installer files",
                LsProto.WatchKind.Create | LsProto.WatchKind.Change | LsProto.WatchKind.Delete,
                LsProto.LsProto.GetClientCapabilities().Workspace.DidChangeWatchedFiles.RelativePatternSupport,
                Identity);
        }

        project._contentMapperWatch = WatchedFiles<string[]>.NewWatchedFilesForPaths(
            "content mapper configuration files for " + id.Value,
            LsProto.WatchKind.Create | LsProto.WatchKind.Change | LsProto.WatchKind.Delete,
            LsProto.LsProto.GetClientCapabilities().Workspace.DidChangeWatchedFiles.RelativePatternSupport,
            builder._sessionOptions.CurrentDirectory,
            builder._sessionOptions.CurrentDirectory,
            builder._fs._fs.UseCaseSensitiveFileNames());
        return project;
    }

    public string CurrentDirectory()
    {
        return _currentDirectory;
    }

    /// <summary>
    /// DisplayName returns a short, human-readable name for the project,
    /// relative to the given workspace root directory.
    /// For configured projects, this is the config file path made relative.
    /// For inferred projects, this is the last component of the current directory.
    /// </summary>
    public string DisplayName(string cwd)
    {
        if (Kind == Kind.KindInferred)
        {
            return GetBaseFileName(_currentDirectory);
        }

        var name = ID().Value;
        if (Kind == Kind.KindConfigured)
        {
            name = ConfigFileName();
        }

        return ConvertToRelativePath(name, new TSPath.ComparePathsOptions(currentDirectory: cwd));
    }

    public ID ID()
    {
        return _id;
    }

    /// <summary>ConfigFileName panics if Kind() is not KindConfigured。</summary>
    public string ConfigFileName()
    {
        if (Kind != Kind.KindConfigured)
        {
            Debug.Fail("ConfigFileName called on non-configured project");
        }

        return _configFileName;
    }

    /// <summary>ConfigFilePath panics if Kind() is not KindConfigured。</summary>
    public Path ConfigFilePath()
    {
        if (Kind != Kind.KindConfigured)
        {
            Debug.Fail("ConfigFilePath called on non-configured project");
        }

        return _configFilePath;
    }

    /// <summary>Go: <c>func (p *Project) Id() string</c>（实现 <c>ls.Project</c>）。</summary>
    public string Id()
    {
        return ID().Value;
    }

    public Compiler.Program? GetProgram()
    {
        return Program;
    }

    public bool IsDirty()
    {
        return _dirty;
    }

    /// <summary>
    /// GetProjectDiagnostics returns program diagnostics combined with any global
    /// diagnostics discovered during checking. These are the diagnostics reported on
    /// the tsconfig.json file.
    /// </summary>
    public Ast.Diagnostic[] GetProjectDiagnostics(Core.Context ctx)
    {
        Ast.Diagnostic[] globalDiags = [];
        if (_checkerPool is not null)
        {
            globalDiags = _checkerPool.GetGlobalDiagnostics();
        }

        return Compiler.Program.SortAndDeduplicateDiagnostics(
            [.. Program!.GetConfigFileParsingDiagnostics(), .. Program.GetProgramDiagnostics(), .. globalDiags]);
    }

    public bool HasFile(string fileName)
    {
        return ContainsFile(ToPath(fileName));
    }

    internal bool ContainsFile(Path path)
    {
        return Program is not null && Program.GetSourceFileByPath(path) is not null;
    }

    public bool IsSourceFromProjectReference(Path path)
    {
        return Program is not null && Program.IsSourceFromProjectReference(path);
    }

    /// <summary>Go: <c>func (p *Project) Clone() *Project</c>（字段清单必须与 Go 的 Clone 保持同步）。</summary>
    public Project Clone()
    {
        return new Project
        {
            Kind = Kind,
            _id = _id,
            _currentDirectory = _currentDirectory,
            _configFileName = _configFileName,
            _configFilePath = _configFilePath,

            _dirty = _dirty,
            _dirtyFilePath = _dirtyFilePath,

            _host = _host,
            CommandLine = CommandLine,
            _commandLineWithTypingsFiles = _commandLineWithTypingsFiles,
            Program = Program,
            ProgramUpdateKind = ProgramUpdateKind.ProgramUpdateKindNone,
            ProgramLastUpdate = ProgramLastUpdate,
            _potentialProjectReferences = _potentialProjectReferences,

            _programFilesWatch = _programFilesWatch,
            _typingsWatch = _typingsWatch,
            _contentMapperWatch = _contentMapperWatch,
            _contentMapperWatchedFiles = _contentMapperWatchedFiles,

            _checkerPool = _checkerPool,

            _moduleResolverFactory = _moduleResolverFactory,
            _moduleResolverID = _moduleResolverID,

            _installedTypingsInfo = _installedTypingsInfo,
            _typingsFiles = _typingsFiles,
        };
    }

    /// <summary>
    /// SetCommandLine reassigns the project's command line and resets all state derived
    /// from it. Changing the command line always requires a full program rebuild, so the
    /// project is marked fully dirty. It also resets:
    /// <list type="bullet">
    /// <item>the memoized command line augmented with typings files (and its once-flag, so
    /// the augmented command line is rebuilt from the new command line on next access);</item>
    /// <item>potentialProjectReferences, the pre-load placeholder derived from the old
    /// command line (always nil for inferred projects, which have no project references).</item>
    /// </list>
    /// </summary>
    public void SetCommandLine(TsOptions.ParsedCommandLine commandLine)
    {
        CommandLine = commandLine;
        _commandLineWithTypingsFiles = null;
        // Go: p.commandLineWithTypingsFilesOnce = sync.Once{}
        lock (_commandLineWithTypingsFilesOnceLock)
        {
            _commandLineWithTypingsFilesOnceDone = false;
        }

        _potentialProjectReferences = null;
        _dirty = true;
        _dirtyFilePath = default;
    }

    /// <summary>getCommandLineWithTypingsFiles returns the command line augmented with typing files if ATA is enabled。</summary>
    internal TsOptions.ParsedCommandLine? GetCommandLineWithTypingsFiles()
    {
        if (_typingsFiles.Length == 0)
        {
            return CommandLine;
        }

        // Check if ATA is enabled for this project
        var typeAcquisition = GetTypeAcquisition();
        if (typeAcquisition is null || !typeAcquisition.Enable.IsTrue())
        {
            return CommandLine;
        }

        lock (_commandLineWithTypingsFilesOnceLock)
        {
            if (!_commandLineWithTypingsFilesOnceDone)
            {
                if (_commandLineWithTypingsFiles is null)
                {
                    // Create an augmented command line that includes typing files
                    var originalRootNames = CommandLine!.FileNames();
                    var newRootNames = new string[originalRootNames.Length + _typingsFiles.Length];
                    originalRootNames.CopyTo(newRootNames, 0);
                    _typingsFiles.CopyTo(newRootNames, originalRootNames.Length);

                    _commandLineWithTypingsFiles = CommandLine.WithFileNames(newRootNames);
                }

                _commandLineWithTypingsFilesOnceDone = true;
            }
        }

        return _commandLineWithTypingsFiles;
    }

    internal void SetPotentialProjectReference(Path configFilePath)
    {
        if (_potentialProjectReferences is null)
        {
            _potentialProjectReferences = new Collections.Set<Path>();
        }
        else
        {
            _potentialProjectReferences = _potentialProjectReferences.Clone();
        }

        _potentialProjectReferences.Add(configFilePath);
    }

    internal bool HasPotentialProjectReference(ProjectTreeRequest projectTreeRequest)
    {
        if (CommandLine is not null)
        {
            foreach (var path in CommandLine.ResolvedProjectReferencePaths())
            {
                if (projectTreeRequest.IsProjectReferenced(ToPath(path)))
                {
                    return true;
                }
            }
        }
        else if (_potentialProjectReferences is not null)
        {
            foreach (var path in _potentialProjectReferences.M)
            {
                if (projectTreeRequest.IsProjectReferenced(path))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public CreateProgramResult CreateProgram()
    {
        var updateKind = ProgramUpdateKind.ProgramUpdateKindNewFiles;
        var programCloned = false;
        Compiler.Program? newProgram;

        // Define a fresh CreateCheckerPool closure for this call. Each invocation of
        // CreateProgram must use its own closure so that concurrent goroutines cloning
        // the same project never share a captured variable through a stale closure
        // stored in the old program's options.
        Func<Compiler.Program, Compiler.CheckerPool> createCheckerPool = program =>
            CheckerPool.NewCheckerPool(_host!._sessionOptions.CheckerPoolOptions, program, Log);
        Action? cleanupModuleResolver = null;
        Func<Module.ResolverOptions, Module.Resolver> createModuleResolver = options =>
        {
            if (_moduleResolverFactory is null)
            {
                return Module.Module.NewResolver(options);
            }

            var (resolver, cleanup) = _moduleResolverFactory.NewResolver(options);
            cleanupModuleResolver = cleanup;
            return resolver;
        };
        try
        {
            // Create the command line, potentially augmented with typing files
            var commandLine = GetCommandLineWithTypingsFiles();
            if (_dirtyFilePath != default && Program is not null && Program.CommandLine() == commandLine)
            {
                Ast.SourceFile? dirtyFile;
                var (result, file, cloned) = Program.UpdateProgram(_dirtyFilePath, _host!, createCheckerPool, createModuleResolver);
                newProgram = result;
                dirtyFile = file;
                programCloned = cloned;
                if (programCloned)
                {
                    updateKind = ProgramUpdateKind.ProgramUpdateKindCloned;
                    foreach (var sourceFile in newProgram!.SourceFiles())
                    {
                        // Use pointer identity: dirtyFile is the exact instance UpdateProgram acquired,
                        // and it is the only file whose refcount is already accounted for.
                        if (!ReferenceEquals(sourceFile, dirtyFile) && !sourceFile.IsContentMapperFailureStub() && !sourceFile.IsContentMapperSupplemental())
                        {
                            // UpdateProgram acquired the changed file only, so we need to ref everything else
                            if (sourceFile.ContentMapper().Length != 0)
                            {
                                _host!._builder!.ContentMappedParseCache.Ref(ContentMappedParseCacheKey.ContentMappedParseCacheKeyForFile(sourceFile));
                            }
                            else
                            {
                                _host!._builder!.ParseCache.Ref(ParseCacheKey.ParseCacheKeyForFile(sourceFile));
                            }
                        }
                    }

                    foreach (var duplicate in newProgram!.DuplicateSourceFiles())
                    {
                        if (!duplicate.IsContentMapperFailureStub)
                        {
                            if (duplicate.ContentMapper.Length != 0)
                            {
                                _host!._builder!.ContentMappedParseCache.Ref(ContentMappedParseCacheKey.ContentMappedParseCacheKeyForDuplicate(duplicate));
                            }
                            else
                            {
                                _host!._builder!.ParseCache.Ref(ParseCacheKey.ParseCacheKeyForDuplicate(duplicate));
                            }
                        }
                    }
                }
                else if (dirtyFile is not null)
                {
                    // UpdateProgram always acquires the dirty file before deciding whether it can
                    // reuse the old program. If it falls back to a full rebuild, release that
                    // speculative acquire so the rebuilt program is the only remaining owner.
                    if (dirtyFile.ContentMapper().Length != 0)
                    {
                        _host!._builder!.ContentMappedParseCache.Deref(ContentMappedParseCacheKey.ContentMappedParseCacheKeyForFile(dirtyFile));
                    }
                    else
                    {
                        _host!._builder!.ParseCache.Deref(ParseCacheKey.ParseCacheKeyForFile(dirtyFile));
                    }
                }
            }
            else
            {
                var typingsLocation = "";
                if (GetTypeAcquisition()!.Enable.IsTrue())
                {
                    typingsLocation = _host!._sessionOptions.TypingsLocation;
                }

                newProgram = Compiler.Compiler.NewProgram(
                    new Compiler.ProgramOptions
                    {
                        Host = _host!,
                        Config = commandLine!,
                        UseSourceOfProjectReference = true,
                        TypingsLocation = typingsLocation,
                        CreateCheckerPool = createCheckerPool,
                        CreateModuleResolver = createModuleResolver,
                    });
            }
        }
        finally
        {
            cleanupModuleResolver?.Invoke();
        }

        if (!programCloned && Program is not null && Program.HasSameFileNames(newProgram))
        {
            updateKind = ProgramUpdateKind.ProgramUpdateKindSameFileNames;
        }

        newProgram.BindSourceFiles();

        return new CreateProgramResult { Program = newProgram, UpdateKind = updateKind };
    }

    public WatchedFiles<Collections.SyncSet<Path>> CloneWatchers()
    {
        return _programFilesWatch!.Clone(_host!._sourceFS._seenFiles!);
    }

    internal void Log(string msg)
    {
        // !!!
    }

    internal Path ToPath(string fileName)
    {
        return TSPath.TSPath.ToPath(fileName, _currentDirectory, _host!.FS().UseCaseSensitiveFileNames());
    }

    internal string Print(bool writeFileNames, bool writeFileExplanation, StringBuilder builder)
    {
        builder.Append($"\nProject '{ID().Value}'\n");
        if (Program is null)
        {
            builder.Append("\tFiles (0) NoProgram\n");
        }
        else
        {
            var sourceFiles = Program.GetSourceFiles();
            builder.Append($"\tFiles ({sourceFiles.Length})\n");
            if (writeFileNames)
            {
                foreach (var sourceFile in sourceFiles)
                {
                    builder.Append("\t\t");
                    builder.Append(sourceFile.FileName());
                    builder.Append('\n');
                }

                // !!!
                // if writeFileExplanation {}
            }
        }

        builder.Append(ProjectConstants.Hr);
        return builder.ToString();
    }

    /// <summary>GetTypeAcquisition returns the type acquisition settings for this project。</summary>
    public Core.TypeAcquisition? GetTypeAcquisition()
    {
        if (Kind is Kind.KindInferred or Kind.KindSynthetic)
        {
            // For inferred and synthetic projects, use default settings.
            return new Core.TypeAcquisition
            {
                Enable = Core.Tristate.TSTrue,
                Include = null,
                Exclude = null,
                DisableFilenameBasedTypeAcquisition = Core.Tristate.TSFalse,
            };
        }

        if (CommandLine is not null)
        {
            return CommandLine.TypeAcquisition();
        }

        return null;
    }

    /// <summary>GetUnresolvedImports extracts unresolved imports from this project's program。</summary>
    public Collections.Set<string>? GetUnresolvedImports()
    {
        if (Program is null)
        {
            return null;
        }

        return Program.GetUnresolvedImports();
    }

    /// <summary>ShouldTriggerATA determines if ATA should be triggered for this project。</summary>
    public bool ShouldTriggerATA(ulong snapshotID)
    {
        if (Program is null || CommandLine is null)
        {
            return false;
        }

        var typeAcquisition = GetTypeAcquisition();
        if (typeAcquisition is null || !typeAcquisition.Enable.IsTrue())
        {
            return false;
        }

        if (_installedTypingsInfo is null || (ProgramLastUpdate == snapshotID && ProgramUpdateKind == ProgramUpdateKind.ProgramUpdateKindNewFiles))
        {
            return true;
        }

        return !_installedTypingsInfo.Equals(ComputeTypingsInfo());
    }

    public Ata.TypingsInfo ComputeTypingsInfo()
    {
        return new Ata.TypingsInfo
        {
            CompilerOptions = CommandLine!.CompilerOptions(),
            TypeAcquisition = GetTypeAcquisition(),
            UnresolvedImports = GetUnresolvedImports(),
        };
    }
}

/// <summary>Go 的 <c>CreateProgramResult</c>。</summary>
public sealed class CreateProgramResult
{
    public Compiler.Program? Program;
    public ProgramUpdateKind UpdateKind;
}
