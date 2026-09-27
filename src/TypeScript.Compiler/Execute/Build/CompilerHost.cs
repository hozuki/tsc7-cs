// ported from: internal/execute/build/compilerHost.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的未导出类型 `compilerHost` → C# `ProjectCompilerHost`（`CompilerHost` 名字已被 `compiler.CompilerHost`
//    接口占用；`host` 已被 host.go 的 `Host` 占用）。Go 里它结构性地满足 `compiler.CompilerHost`，C# 显式实现。
// 2) `(T, error)` → 失败抛异常（§3.2）：`contentmapper.ErrProjectUnavailable` → `ProjectUnavailableException`；
//    `TransformAndParse`/`CheckSupplementalFileNameCollisions` 的 error 由它们自己抛。
// 3) Go 的 `(contentmapper.SourceFiles, error)` 里"读不到文件"返回**零值 SourceFiles + nil error** →
//    C# 返回 `new SourceFiles()`（Go 的 `contentmapper.SourceFiles{}`）。
// 4) Go 的 `trace func(msg *diagnostics.Message, args ...any)` → `Tsc.TraceFunc`（同 tsc/emit.go 的移植）。
namespace TypeScript.Execute.Build;

using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using Path = TypeScript.TSPath.Path;
using CMP = TypeScript.ContentMapper.ContentMapper;
using CMProject = TypeScript.ContentMapper.Project;
using TraceFunc = TypeScript.Execute.Tsc.TraceFunc;

/// <summary>Go: <c>type compilerHost struct</c>（compilerHost.go:13-17）。</summary>
internal sealed class ProjectCompilerHost : CompilerHost
{
    internal Host _host = null!;
    internal TraceFunc _trace = null!;
    internal CMProject? _contentMapperProject;

    /// <summary>Go: <c>func (h *compilerHost) FS() vfs.FS</c>（compilerHost.go:21-23）。</summary>
    public FS FS() => _host.FS();

    /// <summary>Go: <c>func (h *compilerHost) DefaultLibraryPath() string</c>（compilerHost.go:25-27）。</summary>
    public string DefaultLibraryPath() => _host.DefaultLibraryPath();

    /// <summary>Go: <c>func (h *compilerHost) GetCurrentDirectory() string</c>（compilerHost.go:29-31）。</summary>
    public string GetCurrentDirectory() => _host.GetCurrentDirectory();

    /// <summary>Go: <c>func (h *compilerHost) Trace(msg *diagnostics.Message, args ...any)</c>（compilerHost.go:33-35）。</summary>
    public void Trace(Message msg, params object?[] args) => _trace(msg, args);

    /// <summary>Go: <c>func (h *compilerHost) GetSourceFile(opts ast.SourceFileParseOptions) *ast.SourceFile</c>（compilerHost.go:37-39）。</summary>
    public SourceFile? GetSourceFile(SourceFileParseOptions opts) => _host.GetSourceFile(opts);

    /// <summary>Go: <c>func (h *compilerHost) GetContentMappedSourceFiles(parseOptions, mapper) (contentmapper.SourceFiles, error)</c>（compilerHost.go:41-54）。</summary>
    public SourceFiles GetContentMappedSourceFiles(SourceFileParseOptions parseOptions, Mapper mapper)
    {
        if (_contentMapperProject is null)
        {
            throw new ProjectUnavailableException();
        }
        if (!FS().TryReadFile(parseOptions.FileName, out var content))
        {
            return new SourceFiles();
        }
        var files = CMP.TransformAndParse(parseOptions, content, mapper, _contentMapperProject);
        CMP.CheckSupplementalFileNameCollisions(files, FS().FileExists);
        return files;
    }

    /// <summary>Go: <c>func (h *compilerHost) ContentMapperProject() contentmapper.Project</c>（compilerHost.go:56-58）。</summary>
    public CMProject? ContentMapperProject() => _contentMapperProject;

    /// <summary>Go: <c>func (h *compilerHost) GetResolvedProjectReference(fileName string, path tspath.Path) *tsoptions.ParsedCommandLine</c>（compilerHost.go:60-62）。</summary>
    public ParsedCommandLine? GetResolvedProjectReference(string fileName, Path path) =>
        _host.GetResolvedProjectReference(fileName, path);
}
