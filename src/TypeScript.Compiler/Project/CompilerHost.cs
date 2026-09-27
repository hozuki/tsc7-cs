// ported from: internal/project/compilerhost.go:1-174（全部）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出结构体 `compilerHost` → C# `internal sealed class CompilerHost`（M8Project 仲裁的定名；
//    命名空间 `TypeScript.Project` 与接口 `TypeScript.Compiler.CompilerHost` 同名但不同命名空间，
//    所以基类型列表写全限定名）。字段（Go 未导出 → internal `_camelCase`）：`configFilePath`→`_configFilePath`、
//    `currentDirectory`→`_currentDirectory`、`sessionOptions`→`_sessionOptions`、`sourceFS`→`_sourceFS`、
//    `configFileRegistry`→`_configFileRegistry`、`project`→`_project`、`builder`→`_builder`、`logger`→`_logger`、
//    `contentMapperProject`→`_contentMapperProject`、`contentMapperOnce`→`_contentMapperOnceLock` + `_contentMapperOnceDone`。
// 2) `contentMapperOnce sync.Once` → **"标志 + `System.Threading.Lock` 双重检查"**（仓内先例 Compiler/CheckerPool.cs
//    文件头第 3 条）：整个 Do 体在锁内执行（并发的第二个调用者会阻塞到第一个跑完，正是 sync.Once 的保证），
//    `_contentMapperOnceDone = true` 放在 `finally` 里 —— Go 的 `sync.Once.doSlow` 用 `defer o.done.Store(1)`，
//    即使 f panic 也会标记完成，二者一致。
// 3) `newCompilerHost(...)` 是包级构造函数 → 按 M8Project 广播 #2 挂成 `CompilerHost.NewCompilerHost(...)`。
// 4) `builder.fs`/`builder.toPath`/`builder.sessionOptions`/`builder.ctx`/`builder.parseCache`/
//    `builder.contentMappedParseCache`/`builder.contentMapperHost`/`builder.configFileRegistryBuilder` →
//    `_builder._fs`/`_builder._toPath`/`_builder._sessionOptions`/`_builder._ctx`/`_builder._parseCache`/
//    `_builder._contentMappedParseCache`/`_builder._contentMapperHost`/`_builder._configFileRegistryBuilder`
//    （C 组 `ProjectCollectionBuilder` 的 internal 字段名，Go 同名去 PascalCase 加下划线）；
//    `builder.configFileRegistryBuilder.acquireConfigForProject(fileName, path, project, logger)` →
//    `_builder._configFileRegistryBuilder.AcquireConfigForProject(...)`；`project.configFilePath` →
//    `_project._configFilePath`；`project.getCommandLineWithTypingsFiles()` → `_project.GetCommandLineWithTypingsFiles()`。
// 5) `c.sourceFS.source = snapshotFS` → `_sourceFS._source = snapshotFS`（A 组 `SourceFS` 的 internal 字段 `_source`）；
//    注意 `_sourceFS` 的类型是 `SourceFS`（跟踪包装器），**不是** `SnapshotFS`。
// 6) `panic("freeze can only be called once")` / `panic("method must not be called after snapshot initialization")`
//    → `Debug.Fail(...)`（抛 DebugFailureException，docs §3.2）。
// 7) `GetContentMappedSourceFiles(...) (contentmapper.SourceFiles, error)` → 只返回 `SourceFiles`、失败抛异常
//    （docs §3.2；与已落地的 `TypeScript.Compiler.CompilerHostImpl.GetContentMappedSourceFiles` 同款签名）。
//    Go 的 `contentmapper.ErrProjectUnavailable` 哨兵 error → 已落地的 `ProjectUnavailableException`（ContentMapper.cs:279）。
//    Go 的 `err = contentmapper.CheckSupplementalFileNameCollisions(...); if err != nil { Deref(key); return {}, err }`
//    落成 `try { CheckSupplementalFile… } catch { Deref(key); throw; }`。
//    另：Go 的 `identity, err := project.Identity(mapper); if err != nil { return {}, NewTransformError(TransformErrorKindProject, err) }`
//    在 C# 里**不再包一层** —— `ContentMapper` 的 C# 移植已按 §3.2 把失败直接抛成具体异常
//    （`TransformError`/`ProjectError` 等，HostImpl.cs:2177-2178），再包一层会改变异常类型，调用点按具体类型区分。
//    Go 的"未找到 → 空串"分支在 C# 里同样返回 ""（HostImpl.cs:2055）。
// 8) `xxh3.Hash128([]byte(identity))` → `HashString128(identity)`（Xxh3.cs 文件头：按 UTF-8 编码后哈希，与 Go 的
//    string 字节序列一致）。
// 9) `contentmapper.Project`/`contentmapper.ProjectSpec` → 别名 `CmProject`/`CmProjectSpec`
//    （`TypeScript.ContentMapper` 既是命名空间又有同名静态类，裸名会歧义；仓内先例 Execute/Tsc.cs:61）。
//    `contentmapper.SourceFiles`/`Mapper` 经 `using TypeScript.ContentMapper;` 裸名使用。
// 10) `msg.Localize(locale.Default, args...)` → `msg.Localize(Locale.Default, args)`（`Message.Localize(Loc, params object?[]?)`，
//     Diagnostics.cs:131）。Go 里 logger 为 nil 会 panic，C# 同样不做 nil 检查（`_logger!`）。
// 11) `contentMappedParseCacheKey(...)` → `ContentMappedParseCacheKey.NewContentMappedParseCacheKey(...)`（Go 原名与类型
//     同名，C# 禁止 ⇒ 加 `New` 前缀，详见 ParseCache.cs 文件头第 5 条）；
//     `NewParseCacheKey(...)` → `ParseCacheKey.NewParseCacheKey(...)`（包级函数挂到它构造的类型上，见广播 #2）。
namespace TypeScript.Project;

using System;
using TypeScript.Ast;
using TypeScript.ContentMapper;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using static TypeScript.Binder.Binder;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Xxh3.Xxh3;
using CmProject = TypeScript.ContentMapper.Project;
using CmProjectSpec = TypeScript.ContentMapper.ProjectSpec;
using Loc = TypeScript.Locale.Locale;
using Path = TypeScript.TSPath.Path;

/// <summary>
/// Go: <c>type compilerHost struct</c>（compilerhost.go:16-34）。实现 <c>compiler.CompilerHost</c>：
/// 快照内的项目编译宿主，文件经 <see cref="SourceFS"/> 从 FileSource 取，并缓存在 parseCache 里。
/// </summary>
internal sealed class CompilerHost : TypeScript.Compiler.CompilerHost
{
    internal Path _configFilePath;
    internal string _currentDirectory = "";
    internal SessionOptions _sessionOptions = null!;

    internal SourceFS _sourceFS = null!;
    internal ConfigFileRegistry? _configFileRegistry;

    internal Project? _project;
    internal ProjectCollectionBuilder? _builder;
    internal Logging.LogTree? _logger;
    internal CmProject? _contentMapperProject;
    // Go: `contentMapperOnce sync.Once`（见文件头第 2 条）。
    private readonly Lock _contentMapperOnceLock = new();
    private bool _contentMapperOnceDone;

    /// <summary>Go: <c>func newCompilerHost(currentDirectory string, project *Project, builder *ProjectCollectionBuilder, logger *logging.LogTree) *compilerHost</c>（compilerhost.go:36-52）。</summary>
    internal static CompilerHost NewCompilerHost(string currentDirectory, Project project, ProjectCollectionBuilder builder, Logging.LogTree? logger)
    {
        return new CompilerHost
        {
            _configFilePath = project._configFilePath,
            _currentDirectory = currentDirectory,
            _sessionOptions = builder._sessionOptions,

            _sourceFS = SourceFS.NewSourceFS(true, builder._fs, builder._toPath),

            _project = project,
            _builder = builder,
            _logger = logger,
        };
    }

    /// <summary>
    /// Go: <c>func (c *compilerHost) freeze(snapshotFS *SnapshotFS, configFileRegistry *ConfigFileRegistry)</c>
    /// （compilerhost.go:54-65）。清掉对可变状态的引用，使 compilerHost 在快照 finalize 之后仍可安全使用
    /// （用法见 snapshot.go）。
    /// </summary>
    internal void Freeze(SnapshotFS snapshotFS, ConfigFileRegistry configFileRegistry)
    {
        if (_builder is null)
        {
            // Go: panic("freeze can only be called once")
            Debug.Fail("freeze can only be called once");
            return;
        }

        _sourceFS._source = snapshotFS;
        _sourceFS.DisableTracking();
        _configFileRegistry = configFileRegistry;
        _builder = null;
        _project = null;
        _logger = null;
    }

    /// <summary>Go: <c>func (c *compilerHost) ensureAlive()</c>（compilerhost.go:67-71）。</summary>
    internal void EnsureAlive()
    {
        if (_builder is null || _project is null)
        {
            // Go: panic("method must not be called after snapshot initialization")
            Debug.Fail("method must not be called after snapshot initialization");
        }
    }

    /// <summary>Go: <c>func (c *compilerHost) DefaultLibraryPath() string</c>（compilerhost.go:74-76）—— 实现 compiler.CompilerHost。</summary>
    public string DefaultLibraryPath()
    {
        return _sessionOptions.DefaultLibraryPath;
    }

    /// <summary>Go: <c>func (c *compilerHost) FS() vfs.FS</c>（compilerhost.go:79-81）—— 实现 compiler.CompilerHost。</summary>
    public FS FS()
    {
        return _sourceFS;
    }

    /// <summary>Go: <c>func (c *compilerHost) GetCurrentDirectory() string</c>（compilerhost.go:84-86）—— 实现 compiler.CompilerHost。</summary>
    public string GetCurrentDirectory()
    {
        return _currentDirectory;
    }

    /// <summary>Go: <c>func (c *compilerHost) GetResolvedProjectReference(fileName string, path tspath.Path) *tsoptions.ParsedCommandLine</c>（compilerhost.go:89-98）—— 实现 compiler.CompilerHost。</summary>
    public ParsedCommandLine? GetResolvedProjectReference(string fileName, Path path)
    {
        if (_builder is null)
        {
            return _configFileRegistry!.GetConfig(path);
        }

        // acquireConfigForProject 会绕过 sourceFS，所以在这里手动跟踪该文件。
        _sourceFS.Track(fileName);
        return _builder._configFileRegistryBuilder.AcquireConfigForProject(fileName, path, _project!, _logger);
    }

    /// <summary>
    /// Go: <c>func (c *compilerHost) GetSourceFile(opts ast.SourceFileParseOptions) *ast.SourceFile</c>（compilerhost.go:101-108）。
    /// 文件缓存在 parseCache 里，并对进行中的 program 立即可获取。
    /// </summary>
    public SourceFile? GetSourceFile(SourceFileParseOptions opts)
    {
        EnsureAlive();
        var fh = _sourceFS.GetFileByPath(opts.FileName, opts.Path);
        if (fh is not null)
        {
            var key = ParseCacheKey.NewParseCacheKey(opts, fh.Hash(), fh.Kind());
            return _builder!._parseCache.Acquire(key, fh);
        }

        return null;
    }

    /// <summary>
    /// Go: <c>func (c *compilerHost) GetContentMappedSourceFiles(parseOptions ast.SourceFileParseOptions, mapper *contentmapper.Mapper) (contentmapper.SourceFiles, error)</c>
    /// （compilerhost.go:111-141）—— 实现 compiler.CompilerHost。
    /// </summary>
    public SourceFiles GetContentMappedSourceFiles(SourceFileParseOptions parseOptions, Mapper mapper)
    {
        EnsureAlive();
        var fh = _sourceFS.GetFileByPath(parseOptions.FileName, parseOptions.Path);
        if (fh is null)
        {
            return new SourceFiles();
        }

        var diagnosticLocale = Loc.FromContext(_builder!._ctx);
        var project = ContentMapperProject();
        if (project is null)
        {
            // Go: return contentmapper.SourceFiles{}, contentmapper.ErrProjectUnavailable
            throw new ProjectUnavailableException();
        }

        // Go: identity, err := project.Identity(mapper)。C# 侧失败即抛（见文件头第 7 条）。
        var identity = project.Identity(mapper);
        var transformIdentity = HashString128(identity);
        var key = ContentMappedParseCacheKey.NewContentMappedParseCacheKey(parseOptions, fh.Hash(), transformIdentity, diagnosticLocale);
        var files = _builder!._contentMappedParseCache.AcquireOrError(key, () =>
        {
            var produced = TransformAndParse(parseOptions, fh.Content(), mapper, project);
            produced.Canonical!.Hash = key.Hash;
            BindSourceFile(produced.Canonical);
            foreach (var supplemental in produced.Supplemental)
            {
                supplemental.Hash = key.Hash;
                BindSourceFile(supplemental);
            }

            return produced;
        });

        try
        {
            CheckSupplementalFileNameCollisions(files, FS().FileExists);
        }
        catch
        {
            _builder!._contentMappedParseCache.Deref(key);
            throw;
        }

        return files;
    }

    /// <summary>Go: <c>func (c *compilerHost) ContentMapperProject() contentmapper.Project</c>（compilerhost.go:143-159）。</summary>
    public CmProject? ContentMapperProject()
    {
        // Go: c.contentMapperOnce.Do(func() { … })（见文件头第 2 条）。
        lock (_contentMapperOnceLock)
        {
            if (_contentMapperOnceDone)
            {
                return _contentMapperProject;
            }

            try
            {
                // 先取到局部变量：`_builder` 是可空字段（Go 的 freeze 会把它置 nil），而方法调用会让 C# 的可空流分析
                // 重置字段的已判空状态（CS8602）；Go 侧同样是"两次读字段"，但该字段只在构造/Freeze 时被赋值。
                var builder = _builder;
                if (builder is null || builder._contentMapperHost is null)
                {
                    return _contentMapperProject;
                }

                // Go: `commandLine := c.project.getCommandLineWithTypingsFiles()`（返回 nil 时 Go 解引用即 panic ⇒ C# 用 `!`）
                var commandLine = _project!.GetCommandLineWithTypingsFiles()!;
                if ((commandLine.ContentMappers() ?? []).Length == 0)
                {
                    return _contentMapperProject;
                }

                _contentMapperProject = builder._contentMapperHost.Project(new CmProjectSpec
                {
                    ConfigFileName = commandLine.ConfigName(),
                    Mappers = commandLine.ContentMappers()!,
                    CompilerOptions = commandLine.CompilerOptions(),
                });
                return _contentMapperProject;
            }
            finally
            {
                _contentMapperOnceDone = true;
            }
        }
    }

    /// <summary>Go: <c>func (c *compilerHost) Trace(msg *diagnostics.Message, args ...any)</c>（compilerhost.go:162-164）—— 实现 compiler.CompilerHost。</summary>
    public void Trace(Message msg, params object?[] args)
    {
        _logger!.Log(msg.Localize(Loc.Default, args));
    }
}
