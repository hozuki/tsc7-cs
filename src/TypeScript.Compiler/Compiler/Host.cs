// ported from: internal/compiler/host.go（122 行，全部）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出 struct `compilerHost` 实现导出接口 `CompilerHost` → `internal sealed class CompilerHostImpl :
//    CompilerHost`（先例：Go `referenceResolver` → `ReferenceResolverImpl`）。
//    `CompilerHost` 上显式列出两个 Go 的**隐式接口满足**：`TypeScript.Module.ResolutionHost` 与
//    `ParseConfigHost`（都是 `FS()` + `GetCurrentDirectory()`）。Go 的 `compilerHost` 有这两个方法，所以
//    `GetParsedCommandLineOfConfigFilePath(..., h, ...)` 能把它当 `sys ParseConfigHost` 传、
//    `module.ResolverOptions{Host: opts.Host}` 也能把它当 `module.ResolutionHost` 传；C# 必须显式声明
//    继承关系（与 `TypeScript.OutputPaths.OutputPaths` 显式声明实现 declarations.OutputPaths 同理）。
//    `ParseConfigHost` 是 `TypeScript.TsOptions.TsOptions` 的**嵌套接口**，靠 `using static` 引入（using static 也
//    导入嵌套类型），所以这里写裸名。
// 2) Go 未导出字段 → `internal _camelCase`：`_currentDirectory`/`_fs`/`_defaultLibraryPath`/`_extendedConfigCache`/
//    `_trace`/`_contentMapperProject`。
// 3) `GetContentMappedSourceFiles(...) (contentmapper.SourceFiles, error)` → 只返回 `SourceFiles`，失败**抛异常**
//    （docs §3.2/§5.4：`(T, error)` → 抛异常）。`contentmapper.ErrProjectUnavailable` 是 Go 的哨兵 error，C# 侧
//    已有对应类型 `ProjectUnavailableException`（ContentMapper/ContentMapper.cs:279），调用点按异常类型区分
//    （Go 侧调用点用 `errors.AsType`，见 fileloader.go 的 parseContentMappedFile）。
// 4) `fs.ReadFile(path) (string, bool)` → `FS.TryReadFile(path, out string contents)`（docs §3.2：(T,bool) → TryX）。
// 5) `func(msg *diagnostics.Message, args ...any)` → `Action<Message, object?[]>`（仓库先例：
//    `PackageJson.Cache.GetVersionPaths(Action<Message, object?[]>? trace = null)`、`Module.Resolver.PackageJson`）。
//    Go 的 `if trace == nil` 分支用 `?? static (_, _) => { }` 兜住。
// 6) 两个包级构造函数 `NewCachedFSCompilerHost`/`NewCompilerHost` 落进与包同名的静态类 `Compiler`
//    （docs/architecture.md §2），故调用点是 `Compiler.NewCompilerHost(...)`（同文件末尾的 partial）。
// 7) `cachedvfs.From(fs)` → `using static TypeScript.Vfs.CachedVfs.CachedVfs;` 后的裸 `From(fs)`。
// 8) `parser.ParseSourceFile(opts, text, core.EnsureScriptKindFromFileName(opts.FileName))` →
//    `using static TypeScript.Parser.Parser;` 后的裸 `ParseSourceFile(...)`（Go 的包级函数）。
// 9) `tsoptions.GetParsedCommandLineOfConfigFilePath(fileName, path, nil, nil, h, h.extendedConfigCache)` →
//    `using static TypeScript.TsOptions.TsOptions;` 后的裸名；两个 `nil` → `null`（`CompilerOptions?` /
//    `OrderedMap<string, object?>?`）。
namespace TypeScript.Compiler;

using System;
using TypeScript.Ast;
using TypeScript.ContentMapper;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Core.Core;
using static TypeScript.Parser.Parser;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Vfs.CachedVfs.CachedVfs;
using Path = TypeScript.TSPath.Path;

/// <summary>
/// Go: <c>type CompilerHost interface { … }</c>。<br/>
/// 额外列出两个 Go 里**隐式**满足的接口（C# 是名义类型，不显式声明就没法当宿主传）：
/// <c>module.ResolutionHost</c>（`FS()` + `GetCurrentDirectory()`）与 <c>tsoptions.ParseConfigHost</c>
/// （同样是 `FS()` + `GetCurrentDirectory()`）—— Go 的 `compilerHost` 有这两个方法，所以
/// `module.ResolverOptions{Host: opts.Host}`、`module.GetAutomaticTypeDirectiveNames(options, host)`、
/// `newProjectReferenceDtsFakingHost(...) module.ResolutionHost`、
/// `tsoptions.ParseCommandLine(args, host)`/`GetParsedCommandLineOfConfigFilePath(..., sys, ...)` 都能直接
/// 传宿主接口。实现类一个 public 方法同时满足所有接口。
/// </summary>
public interface CompilerHost : TypeScript.Module.ResolutionHost, ParseConfigHost
{
    // Go 的 `CompilerHost` 自己又列了一遍 `FS()`/`GetCurrentDirectory()`（它们同时由 module.ResolutionHost 要求）。
    // C# 里就是隐藏继承成员 → 按 ModuleSpecifiers/Types.cs 的既有做法用 `new` 显式隐藏，保住 Go 的声明面
    // （api-parity 逐成员核对）；实现类一个 public 方法同时满足两者。
    new FS FS();

    string DefaultLibraryPath();

    new string GetCurrentDirectory();

    void Trace(Message msg, params object?[] args);

    SourceFile? GetSourceFile(SourceFileParseOptions opts);

    // GetContentMappedSourceFiles produces the source file for a content-mapped (foreign) file by running
    // the given mapper's transform on the file's content. The caller resolves the mapper (and owns the
    // failure accounting), so implementations must use it as-is. It returns an empty result if the file cannot
    // be read, or throws if the transform fails or the mapper produces invalid position mappings.
    // Implementations may cache successful results.
    SourceFiles GetContentMappedSourceFiles(SourceFileParseOptions parseOptions, Mapper mapper);

    // ContentMapperProject returns the project-scoped content mapper used by this host, or nil when the
    // command line has no content mappers. The project owns transform identity and lifecycle state.
    Project? ContentMapperProject();

    ParsedCommandLine? GetResolvedProjectReference(string fileName, Path path);
}

internal sealed class CompilerHostImpl : CompilerHost
{
    internal string _currentDirectory = "";
    internal FS _fs = null!;
    internal string _defaultLibraryPath = "";
    internal ExtendedConfigCache? _extendedConfigCache;
    internal Action<Message, object?[]> _trace = null!;
    internal Project? _contentMapperProject;

    public FS FS()
    {
        return _fs;
    }

    public string DefaultLibraryPath()
    {
        return _defaultLibraryPath;
    }

    public string GetCurrentDirectory()
    {
        return _currentDirectory;
    }

    public void Trace(Message msg, params object?[] args)
    {
        _trace(msg, args);
    }

    public SourceFile? GetSourceFile(SourceFileParseOptions opts)
    {
        // Go: text, ok := h.FS().ReadFile(opts.FileName); if !ok { return nil }
        if (!FS().TryReadFile(opts.FileName, out var text))
        {
            return null;
        }

        return ParseSourceFile(opts, text, EnsureScriptKindFromFileName(opts.FileName));
    }

    public SourceFiles GetContentMappedSourceFiles(SourceFileParseOptions parseOptions, Mapper mapper)
    {
        if (_contentMapperProject is null)
        {
            // Go: return contentmapper.SourceFiles{}, contentmapper.ErrProjectUnavailable
            throw new ProjectUnavailableException();
        }

        // Go: content, ok := h.FS().ReadFile(parseOptions.FileName); if !ok { return contentmapper.SourceFiles{}, nil }
        if (!FS().TryReadFile(parseOptions.FileName, out var content))
        {
            return new SourceFiles();
        }

        var files = TransformAndParse(parseOptions, content, mapper, _contentMapperProject);
        CheckSupplementalFileNameCollisions(files, FS().FileExists);
        return files;
    }

    public Project? ContentMapperProject()
    {
        return _contentMapperProject;
    }

    public ParsedCommandLine? GetResolvedProjectReference(string fileName, Path path)
    {
        // Go: commandLine, _ := tsoptions.GetParsedCommandLineOfConfigFilePath(fileName, path, nil, nil /*optionsRaw*/, h, h.extendedConfigCache)
        var (commandLine, _) = GetParsedCommandLineOfConfigFilePath(fileName, path, null, null /*optionsRaw*/, this, _extendedConfigCache);
        return commandLine;
    }
}

/// <summary>Go 包级函数（package compiler）→ 与包同名的静态类（docs/architecture.md §2）。</summary>
public static partial class Compiler
{
    public static CompilerHost NewCachedFSCompilerHost(
        string currentDirectory,
        FS fs,
        string defaultLibraryPath,
        ExtendedConfigCache? extendedConfigCache,
        Action<Message, object?[]>? trace,
        Project? contentMapperProject)
    {
        return NewCompilerHost(currentDirectory, From(fs), defaultLibraryPath, extendedConfigCache, trace, contentMapperProject);
    }

    public static CompilerHost NewCompilerHost(
        string currentDirectory,
        FS fs,
        string defaultLibraryPath,
        ExtendedConfigCache? extendedConfigCache,
        Action<Message, object?[]>? trace,
        Project? contentMapperProject)
    {
        // Go: if trace == nil { trace = func(msg *diagnostics.Message, args ...any) {} }
        trace ??= static (_, _) => { };
        return new CompilerHostImpl
        {
            _currentDirectory = currentDirectory,
            _fs = fs,
            _defaultLibraryPath = defaultLibraryPath,
            _extendedConfigCache = extendedConfigCache,
            _trace = trace,
            _contentMapperProject = contentMapperProject,
        };
    }
}
