// ported from: internal/ls/languageservice.go:1-134
//
// ⚠ **这是 `ls` 大切片的前置引导文件**（Main 裁定）：`internal/project` 的 `session.go` 会构造并返回
// `*ls.LanguageService`（只当**不透明值**传递，不调用它的任何方法），而 `internal/ls` 整包依赖 `project`。
// 本文件按 Go 原样落地该文件的前 134 行（结构、`NewLanguageService` 与全部方法）；`ls` 切片的其余文件
// （completions/hover/…）请新建文件落地，`LanguageService` 若要加字段/方法请把它改成 `partial` 并沿用原名。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `toPath`（未导出）→ `ToPath`（C# 方法一律 PascalCase）。
//
// 2) `documentPositionMappers map[string]*sourcemap.DocumentPositionMapper` → `Dictionary<string, DocumentPositionMapper?>`，
//    注意 `SourceMap.GetDocumentPositionMapper(l, fileName)` 在 C# 里返回**可空**（SourceMapper.cs:432 的注释），
//    所以字典值是 `DocumentPositionMapper?`，`GetDocumentPositionMapper` 也返回 `DocumentPositionMapper?`。
//
// 3) `NewLanguageService(projectID autoimport.ProjectID, program *compiler.Program, host Host, activeFile string)`
//    → 静态方法 `LanguageService.NewLanguageService(LsAutoImport.ProjectID projectID, Compiler.Program? program, Ls.Host host, string activeFile)`
//    （Go 的包级函数；`project` 包名与 `Project` 类型同名的先例见 Locale/Locale.cs：包级函数挂到它构造的类型上）。
//    ⚠ `ls` 切片若之后新建 `Ls.cs`（同名静态类），**请勿**重复声明 `NewLanguageService`，也不要搬家——
//    调用点（`project/session.go` 的移植）已经写死 `LanguageService.NewLanguageService(...)`。
//
// 4) Go 的 `getPreparedAutoImportView` 返回 `(*autoimport.View, error)`，其中 error 是**哨兵** `ErrNeedsAutoImports`
//    （调用点用 `errors.Is` 判它，属正常控制流、不是"失败抛异常"）。这里保留双返回值：`(AutoImport.View? View, Exception? Error)`，
//    哨兵对象暴露为 `LanguageService.ErrNeedsAutoImports`（等价 Go 的包级 `var ErrNeedsAutoImports`）。
//    `ls` 切片移植调用点时应写 `if (error == LanguageService.ErrNeedsAutoImports) { … }`。
//
// 5) `program.GetSourceFile(fileName)` 在 C# 侧返回 `Ast.SourceFile?`；Go 的 `file == nil` → `file is null`；
//    `panic("file not found: " + fileName)` → `Debug.Fail(...)`（抛 DebugFailureException）+ 不可达异常。
//
// 6) `l.host.ReadFile(fileName)` → host 的 `TryReadFile`（见 Ls/Host.cs 文件头第 1 条）。
//
// 7) `autoimport.View`/`autoimport.NewView` → `LsAutoImport.View` / `LsAutoImport.AutoImport.NewView(...)`
//    （各自切片的落点；最后一个参数是 `ModuleSpecifiers.UserPreferences`，与 Go 一致）。
namespace TypeScript.Ls;

/// <summary>Go 的 <c>LanguageService</c>（见文件头：本文件是 `ls` 切片的前置引导）。</summary>
public sealed class LanguageService : SourceMap.Host
{
    internal LsAutoImport.ProjectID _projectID;
    internal Host _host;
    internal LsUtil.UserPreferences _activeConfig;
    internal Compiler.Program? _program;
    internal LsConv.Converters? _converters;
    internal Dictionary<string, SourceMap.DocumentPositionMapper?> _documentPositionMappers = [];

    /// <summary>Go: <c>var ErrNeedsAutoImports = errors.New(...)</c>（见文件头第 4 条）。</summary>
    internal static readonly Exception ErrNeedsAutoImports = new("needs auto imports");

    private LanguageService(
        LsAutoImport.ProjectID projectID,
        Compiler.Program? program,
        Host host,
        string activeFile)
    {
        _projectID = projectID;
        _host = host;
        _program = program;
        _converters = host.Converters();
        _activeConfig = host.GetPreferences(activeFile);
        _documentPositionMappers = [];
    }

    /// <summary>Go: <c>func NewLanguageService(projectID autoimport.ProjectID, program *compiler.Program, host Host, activeFile string) *LanguageService</c>。</summary>
    public static LanguageService NewLanguageService(
        LsAutoImport.ProjectID projectID,
        Compiler.Program? program,
        Host host,
        string activeFile)
    {
        return new LanguageService(projectID, program, host, activeFile);
    }

    internal TSPath.Path ToPath(string fileName)
    {
        return TSPath.TSPath.ToPath(fileName, _program!.GetCurrentDirectory(), UseCaseSensitiveFileNames());
    }

    public Compiler.Program? GetProgram()
    {
        return _program;
    }

    public LsUtil.UserPreferences UserPreferences()
    {
        return _activeConfig;
    }

    public LsUtil.FormatCodeSettings FormatOptions()
    {
        return _activeConfig.FormatCodeSettings;
    }

    internal (Compiler.Program? Program, Ast.SourceFile? File) TryGetProgramAndFile(string fileName)
    {
        var program = GetProgram();
        var file = program?.GetSourceFile(fileName);
        return (program, file);
    }

    internal (Compiler.Program Program, Ast.SourceFile File) GetProgramAndFile(LsProto.DocumentUri documentURI)
    {
        var fileName = documentURI.FileName();
        var (program, file) = TryGetProgramAndFile(fileName);
        if (file is null)
        {
            Debug.Fail("file not found: " + fileName);
            throw new System.Diagnostics.UnreachableException();
        }

        return (program!, file);
    }

    public SourceMap.DocumentPositionMapper? GetDocumentPositionMapper(string fileName)
    {
        if (!_documentPositionMappers.TryGetValue(fileName, out var d))
        {
            d = SourceMap.SourceMap.GetDocumentPositionMapper(this, fileName);
            _documentPositionMappers[fileName] = d;
        }

        return d;
    }

    /// <summary>Go: <c>func (l *LanguageService) ReadFile(fileName string) (string, bool)</c>（见文件头第 6 条）。</summary>
    public bool TryReadFile(string fileName, out string contents)
    {
        return _host.TryReadFile(fileName, out contents);
    }

    public bool UseCaseSensitiveFileNames()
    {
        return _host.UseCaseSensitiveFileNames();
    }

    public SourceMap.ECMALineInfo? GetECMALineInfo(string fileName)
    {
        return _host.GetECMALineInfo(fileName);
    }

    /// <summary>
    /// getPreparedAutoImportView returns an auto-import view for the given file if the registry is prepared
    /// to provide up-to-date auto-imports for it. If not, it returns <see cref="ErrNeedsAutoImports"/>.
    /// </summary>
    internal (LsAutoImport.View? View, Exception? Error) GetPreparedAutoImportView(Ast.SourceFile fromFile, Checker.Checker typeChecker)
    {
        var registry = _host.AutoImportRegistry();
        var registryFile = fromFile;
        var canonical = fromFile.CanonicalSourceFile();
        if (canonical is not null)
        {
            registryFile = canonical;
        }

        if (!registry.IsPreparedForImportingFile(registryFile.FileName(), _projectID, UserPreferences()))
        {
            return (null, ErrNeedsAutoImports);
        }

        var view = LsAutoImport.AutoImport.NewView(registry, fromFile, _projectID, _program!, typeChecker, UserPreferences().ModuleSpecifierPreferences());
        return (view, null);
    }

    /// <summary>
    /// getCurrentAutoImportView returns an auto-import view for the given file, based on the current state
    /// of the auto-import registry, which may or may not be up-to-date.
    /// </summary>
    internal LsAutoImport.View GetCurrentAutoImportView(Ast.SourceFile fromFile, Checker.Checker typeChecker)
    {
        return LsAutoImport.AutoImport.NewView(
            _host.AutoImportRegistry(),
            fromFile,
            _projectID,
            _program!,
            typeChecker,
            UserPreferences().ModuleSpecifierPreferences());
    }
}
