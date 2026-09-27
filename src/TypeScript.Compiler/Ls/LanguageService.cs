// ported from: internal/ls/languageservice.go:1-134 + internal/ls/api.go:1-44（两者合并进本文件）
//
// ⚠ **这是 `ls` 大切片的前置引导文件**（Main 裁定）：`internal/project` 的 `session.go` 会构造并返回
// `*ls.LanguageService`（只当**不透明值**传递，不调用它的任何方法），而 `internal/ls` 整包依赖 `project`。
// `ls` 切片的其余文件（completions/hover/…）请新建文件落地；`LanguageService` 已是 `public sealed partial class`
// （G1 就地确认，无需再改修饰符），后续切片在本类上追加成员即可。
//
// G1 补齐（上一版引导的缺口）：
//   * `languageservice.go:120-134` 的 `DirectoryExists` / `ReadDirectory` / `GetDirectories`（模块说明符补全用）；
//   * `api.go:1-44` 的全部成员（`ErrNoSourceFile` / `ErrNoTokenAtPosition` / `GetSymbolAtPosition` /
//     `GetSymbolAtLocation` / `GetTypeOfSymbol`）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `toPath`（未导出）→ `ToPath`（C# 方法一律 PascalCase；`.editorconfig` 的 `members_should_be_pascal_case`
//    对 method/property/event 强制 PascalCase，Go 未导出的**方法**同样不加 `_` 前缀，只有字段才带 `_`）。
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
//
// 8) `ReadDirectory` 的 `[]string` 参数可为 nil → `string[]?`；`vfsmatch.UnlimitedDepth` →
//    `TypeScript.Vfs.VfsMatch.VfsMatch.UnlimitedDepth`（`VfsMatch` 既是命名空间又是静态类，见 Host.cs 的同类说明）。
//
// 9) api.go 的 `fmt.Errorf("%w: %s", ErrNoSourceFile, fileName)` → `new Exception(msg, sentinel)`：哨兵放进
//    `InnerException` 保住身份（等价 `errors.Is`，先例 Compiler/Compiler.cs:49 的 `AsError<T>` 沿 InnerException 链查找）。
//    `astnav.GetTokenAtPosition` 在 C# 声明成非空返回（AstNav/Tokens.cs），但 Go 侧可能返回 nil ⇒ 用 `Node?` 接收后照旧判空。
//    `program` 在 Go 里可能为 nil（panic）⇒ C# 写 `program!`。
namespace TypeScript.Ls;

/// <summary>Go 的 <c>LanguageService</c>（见文件头：本文件是 `ls` 切片的前置引导）。</summary>
public sealed partial class LanguageService : SourceMap.Host
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

    /// <summary>Used for module specifier completions（languageservice.go:120-123）。</summary>
    public bool DirectoryExists(string path)
    {
        return _host.DirectoryExists(path);
    }

    /// <summary>Used for module specifier completions（languageservice.go:125-128）。</summary>
    public string[] ReadDirectory(string path, string[]? extensions, string[]? includes)
    {
        return _host.ReadDirectory(
            _program!.GetCurrentDirectory(),
            path,
            extensions,
            null /*excludes*/,
            includes,
            TypeScript.Vfs.VfsMatch.VfsMatch.UnlimitedDepth);
    }

    /// <summary>Go: <c>func (l *LanguageService) GetDirectories(path string) []string</c>（languageservice.go:130-132）。</summary>
    public string[] GetDirectories(string path)
    {
        return _host.GetDirectories(path);
    }

    // ── 以下成员来自 internal/ls/api.go:1-44（上一版引导把 api.go 并进本文件时漏掉了，G1 补齐）──

    /// <summary>Go: <c>var ErrNoSourceFile = errors.New("source file not found")</c>（api.go:13-16）。</summary>
    internal static readonly Exception ErrNoSourceFile = new("source file not found");

    /// <summary>Go: <c>var ErrNoTokenAtPosition = errors.New("no token found at position")</c>（api.go:13-16）。</summary>
    internal static readonly Exception ErrNoTokenAtPosition = new("no token found at position");

    /// <summary>
    /// Go 的 <c>fmt.Errorf("%w: %s", err, detail)</c>：把哨兵放进 <see cref="Exception.InnerException"/>
    /// 保住"是哪一个哨兵"的信息（等价 <c>errors.Is</c>，先例 Compiler/Compiler.cs:49 的 AsError 沿 InnerException 链查找）。
    /// </summary>
    private static Exception WrapError(Exception sentinel, string detail)
    {
        return new Exception($"{sentinel.Message}: {detail}", sentinel);
    }

    /// <summary>Go: <c>func (l *LanguageService) GetSymbolAtPosition(ctx context.Context, fileName string, position int) (*ast.Symbol, error)</c>（api.go:18-30）。</summary>
    public (Ast.Symbol? Symbol, Exception? Error) GetSymbolAtPosition(TypeScript.Core.Context ctx, string fileName, int position)
    {
        var (program, file) = TryGetProgramAndFile(fileName);
        if (file is null)
        {
            return (null, WrapError(ErrNoSourceFile, fileName));
        }

        Ast.Node? node = TypeScript.AstNav.AstNav.GetTokenAtPosition(file, position);
        if (node is null)
        {
            return (null, WrapError(ErrNoTokenAtPosition, $"{fileName}:{position}"));
        }

        var (checker, done) = program!.GetTypeCheckerForFile(ctx, file);
        try
        {
            return (checker.GetSymbolAtLocation(node), null);
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) GetSymbolAtLocation(ctx context.Context, node *ast.Node) *ast.Symbol</c>（api.go:32-37）。</summary>
    public Ast.Symbol? GetSymbolAtLocation(TypeScript.Core.Context ctx, Ast.Node node)
    {
        var program = GetProgram()!;
        var (checker, done) = program.GetTypeCheckerForFile(ctx, TypeScript.Ast.Ast.GetSourceFileOfNode(node)!);
        try
        {
            return checker.GetSymbolAtLocation(node);
        }
        finally
        {
            done();
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) GetTypeOfSymbol(ctx context.Context, symbol *ast.Symbol) *checker.Type</c>（api.go:39-44）。</summary>
    public Checker.Type? GetTypeOfSymbol(TypeScript.Core.Context ctx, Ast.Symbol symbol)
    {
        var program = GetProgram()!;
        var (checker, done) = program.GetTypeChecker(ctx);
        try
        {
            return checker.GetTypeOfSymbolAtLocation(symbol, null);
        }
        finally
        {
            done();
        }
    }
}
