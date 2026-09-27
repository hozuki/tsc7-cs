// ported from: internal/execute/build/host.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的未导出类型 `host` → C# `Host`；Go 里它结构性地满足 `compiler.CompilerHost`、`incremental.BuildInfoReader`、
//    `incremental.Host`（三个 `var _ X = (*host)(nil)` 断言），C# 必须把基接口列出来。
// 2) `panic(...)` → `Debug.Fail(...)`（§3.2）：build host 不支持 tracing / content mapper project。
// 3) `(T, error)` → 失败抛异常（§3.2）；`contentmapper.ErrProjectUnavailable` → `ProjectUnavailableException`。
// 4) Go 的 `sourceFiles parseCache[ast.SourceFileParseOptions, *ast.SourceFile]` 以**整个 options 结构体**为键（Go 结构体
//    按值比较）；C# 的 `SourceFileParseOptions` 是 class（引用相等 ⇒ 缓存永不命中）。这里用与 Go 字段一一对应的
//    值类型键 `SourceFileCacheKey(Path, JSX, Force)` 复刻 Go 的结构体相等（同一个 .d.ts 在不同项目里用不同
//    `ExternalModuleIndicatorOptions` 解析时必须各自命中，所以不能只用 Path 作键）。
// 5) `time.Time` → `DateTime`（`time.Time{}` → `default`）；`sync.Mutex` → `lock`；`collections.SyncMap` → C# `SyncMap`。
// 6) Go 的 `SetMTime(file, mTime) error` → `void`（失败抛异常），与 `incremental.Host` 的既有移植一致。
// 7) 未导出方法 → internal + PascalCase（`loadOrStoreMTime` → `LoadOrStoreMTime` 等）。
namespace TypeScript.Execute.Build;

using global::System;
using global::System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using Path = TypeScript.TSPath.Path;
using Inc = TypeScript.Execute.Incremental;
using CMP = TypeScript.ContentMapper.ContentMapper;
using CMProject = TypeScript.ContentMapper.Project;
using TscCache = TypeScript.Execute.Tsc.ExtendedConfigCache;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;

/// <summary>Go 的 `SourceFileParseOptions` 结构体键（见文件头偏离 4）：值相等，字段与 Go 的结构体逐一对应。</summary>
internal readonly record struct SourceFileCacheKey(Path Path, bool JSX, bool Force);

/// <summary>Go: <c>type host struct</c>（host.go:18-30）。</summary>
internal sealed class Host : CompilerHost, Inc.BuildInfoReader, Inc.Host
{
    internal Orchestrator _orchestrator = null!;
    internal CompilerHost _host = null!;

    // Caches that last only for build cycle and then cleared out
    internal TscCache _extendedConfigCache = new();
    internal ParseCache<SourceFileCacheKey, SourceFile> _sourceFiles = new();
    internal SyncMap<Path, TimeSpan> _configTimes = new();

    // caches that stay as long as they are needed
    internal ParseCache<Path, ParsedCommandLine> _resolvedReferences = new();
    internal SyncMap<Path, DateTime> _mTimes = new();

    /// <summary>Go: <c>func (h *host) FS() vfs.FS</c>（host.go:38-40）。</summary>
    public FS FS() => _host.FS();

    /// <summary>Go: <c>func (h *host) DefaultLibraryPath() string</c>（host.go:42-44）。</summary>
    public string DefaultLibraryPath() => _host.DefaultLibraryPath();

    /// <summary>Go: <c>func (h *host) GetCurrentDirectory() string</c>（host.go:46-48）。</summary>
    public string GetCurrentDirectory() => _host.GetCurrentDirectory();

    /// <summary>Go: <c>func (h *host) Trace(msg *diagnostics.Message, args ...any)</c>（host.go:50-52）——Go 里直接 panic。</summary>
    public void Trace(Message msg, params object?[] args) =>
        Debug.Fail("build.Orchestrator.host does not support tracing; use a different host for tracing");

    /// <summary>Go: <c>func (h *host) GetSourceFile(opts ast.SourceFileParseOptions) *ast.SourceFile</c>（host.go:54-60）。</summary>
    public SourceFile? GetSourceFile(SourceFileParseOptions opts)
    {
        if (IsDeclarationFileName(opts.FileName) || FileExtensionIs(opts.FileName, ExtensionJson))
        {
            // Cache dts and json files as they will be reused
            var key = new SourceFileCacheKey(opts.Path, opts.ExternalModuleIndicatorOptions.JSX, opts.ExternalModuleIndicatorOptions.Force);
            return _sourceFiles.LoadOrStore(key, _ => _host.GetSourceFile(opts)!, false /* allowZero */);
        }
        return _host.GetSourceFile(opts);
    }

    /// <summary>Go: <c>func (h *host) GetContentMappedSourceFiles(...) (contentmapper.SourceFiles, error)</c>（host.go:62-64）。</summary>
    public SourceFiles GetContentMappedSourceFiles(SourceFileParseOptions parseOptions, Mapper mapper) =>
        throw new ProjectUnavailableException();

    /// <summary>Go: <c>func (h *host) ContentMapperProject() contentmapper.Project</c>（host.go:66-68）——Go 里直接 panic。</summary>
    public CMProject? ContentMapperProject()
    {
        Debug.Fail("build.Orchestrator.host does not support content mapper project; use an individual project's compiler host instead");
        throw new System.Diagnostics.UnreachableException();
    }

    /// <summary>Go: <c>func (h *host) GetResolvedProjectReference(fileName string, path tspath.Path) *tsoptions.ParsedCommandLine</c>（host.go:70-86）。</summary>
    public ParsedCommandLine? GetResolvedProjectReference(string fileName, Path path) =>
        _resolvedReferences.LoadOrStore(
            path,
            path =>
            {
                var configStart = _orchestrator._opts.Sys.Now();
                // Wrap command line options in "compilerOptions" key to match tsconfig.json structure
                OrderedMap<string, object?>? commandLineRaw = null;
                if (_orchestrator._opts.Command.Raw is OrderedMap<string, object?> raw)
                {
                    var wrapped = new OrderedMap<string, object?>();
                    wrapped.Set("compilerOptions", raw);
                    commandLineRaw = wrapped;
                }
                var (commandLine, _) = GetParsedCommandLineOfConfigFilePath(
                    fileName,
                    path,
                    _orchestrator._opts.Command.CompilerOptions,
                    commandLineRaw,
                    this,
                    _extendedConfigCache);
                var configTime = _orchestrator._opts.Sys.Now() - configStart;
                _configTimes.Store(path, configTime);
                return commandLine!;
            },
            true /* allowZero */);

    /// <summary>Go: <c>func (h *host) ReadBuildInfo(config *tsoptions.ParsedCommandLine) *incremental.BuildInfo</c>（host.go:87-92）。</summary>
    public Inc.BuildInfo? ReadBuildInfo(ParsedCommandLine config)
    {
        var configPath = _orchestrator.ToPath(config.ConfigName());
        var task = _orchestrator.GetTask(configPath);
        var (buildInfo, _) = task.LoadOrStoreBuildInfo(_orchestrator, _orchestrator.ToPath(config.ConfigName()), config.GetBuildInfoFileName());
        return buildInfo;
    }

    /// <summary>Go: <c>func (h *host) GetMTime(file string) time.Time</c>（host.go:94-96）。</summary>
    public DateTime GetMTime(string file) => LoadOrStoreMTime(file, null, true);

    /// <summary>Go: <c>func (h *host) SetMTime(file string, mTime time.Time) error</c>（host.go:98-100）。</summary>
    public void SetMTime(string file, DateTime mTime) => FS().Chtimes(file, default, mTime);

    /// <summary>Go: <c>func (h *host) loadOrStoreMTime(file, oldCache, store) time.Time</c>（host.go:102-119）。</summary>
    internal DateTime LoadOrStoreMTime(string file, SyncMap<Path, DateTime>? oldCache, bool store)
    {
        var path = _orchestrator.ToPath(file);
        var (existing, loaded) = _mTimes.Load(path);
        if (loaded)
        {
            return existing;
        }
        var found = false;
        var mTime = default(DateTime);
        if (oldCache is not null)
        {
            (mTime, found) = oldCache.Load(path);
        }
        if (!found)
        {
            mTime = Inc.Incremental.GetMTime(_host, file);
        }
        if (store)
        {
            (mTime, _) = _mTimes.LoadOrStore(path, mTime);
        }
        return mTime;
    }

    /// <summary>Go: <c>func (h *host) storeMTime(file string, mTime time.Time)</c>（host.go:121-124）。</summary>
    internal void StoreMTime(string file, DateTime mTime)
    {
        var path = _orchestrator.ToPath(file);
        _mTimes.Store(path, mTime);
    }

    /// <summary>Go: <c>func (h *host) storeMTimeFromOldCache(file string, oldCache) </c>（host.go:126-131）。</summary>
    internal void StoreMTimeFromOldCache(string file, SyncMap<Path, DateTime> oldCache)
    {
        var path = _orchestrator.ToPath(file);
        var (mTime, found) = oldCache.Load(path);
        if (found)
        {
            _mTimes.Store(path, mTime);
        }
    }
}
