// ported from: internal/api/module_resolution.go:1-401
//
// 与 Go 的偏离（逐条说明为什么 C# 必须不同）：
//
// 1) 三个未导出类型 `moduleResolverFactory` / `programResolutionContext` / `callbackModuleResolver` → C# `internal sealed class`
//    （`ModuleResolverFactory` 实现 `project.ModuleResolverFactory` 接口，接口成员是 public，故 `NewResolver` 也是 public）。
// 2) Go 的未导出字段 `registration`/`conn`/`ctx`/`currentDirectory`/… → C# PascalCase 字段（本仓惯例：字段名保留 Go 名字、
//    只做 PascalCase 化；内部类型的字段用 `internal`）。Go 里可为 nil 的接口字段（`ipc.Conn`/`module.Resolver`）用
//    `= null!` 声明：Go 在 nil 接口值上调用方法会 panic，C# 在 null 引用上调用会 NullReferenceException，两者都是崩溃路径。
// 3) `func()`（NewResolver 的释放回调）→ `Action`；`func() {}` → `static () => { }`。
// 4) Go 的 `(T, error)` → C# 正常路径返回 T、错误路径抛异常。`fmt.Errorf("%w: …", ErrClientError)` → `ClientErrorException`，
//    Message 写成 Go `err.Error()` 的完整文本（含 `api: client error: ` 前缀，见 Api.cs 头注释，异常文本 = Go 的 error 文本）；
//    `fmt.Errorf("resolveModuleName callback failed: %w", err)` 与 `"invalid resolveModuleName callback result: %w"`
//    是普通 error（没有 ErrClientError）→ `InvalidOperationException`（本包既定落法：先例 Session.Files.cs 文件头偏离 1）。
// 5) `ResolutionMode`：Go 的 `type ResolutionMode core.ModuleKind` 是命名类型、`*ResolutionMode` 是指针 → C# 用文件级别名
//    `using ResolutionMode = TypeScript.Core.ModuleKind;`（同 Api/Proto.Types.cs 文件头第 9 条），`*ResolutionMode` 落成
//    `ResolutionMode?`（= `Core.ModuleKind?`）。于是 Go 的 `params.ResolutionMode = &mode` → `@params.ResolutionMode = mode`
//    （可空赋值，隐式转换），`core.ModuleKind(*entry.ResolutionMode)` → `entry.ResolutionMode.Value`
//    （本文件用 `entry.ResolutionMode is { } entryMode` 取出，等价于 `.Value` 前的 nil 判断）。
// 6) `ModuleResolutionFallback` 是 Go 的字符串命名类型 → C# 常量字符串静态类（Proto.Types.cs 文件头第 8 条），
//    `spec.Fallback` 是 `string`；Go 的 `switch spec.Fallback` → C# if/else 比较（常量相同，语义相同）；
//    Go 的 `%q`（`invalid module resolution fallback %q`）→ C# 手工加引号、不做 Go 风格转义（同 Session.Files.cs 偏离 2）。
// 7) `[]T`（nil 与非 nil 语义不同处）→ `T[]?`；总是非 nil 的 `[]T` → `T[]`；`map[K]V` → `Dictionary<K,V>`；
//    `make([]module.StaticResolutionEntry, 0, n)` → `List<Module.StaticResolutionEntry>`（容量 n）。
// 8) `sync.Mutex` → `System.Threading.Lock`（`lock (Mu)`，契约 §2）；Session 里两个 `sync.RWMutex` 字段按契约 §4 是
//    `ReaderWriterLockSlim`（EnterReadLock/EnterWriteLock/ExitReadLock/ExitWriteLock）。
// 9) Go 的 `atomic.Uint64.Add(1)` → `(ulong)Interlocked.Increment(ref …)`（契约 §4：atomic → long 字段 + Interlocked）。
// 10) `moduleResolverRegistration.compilerOptions` 是 `*core.CompilerOptions`（C# 字段声明为可空，Session.cs:579），
//    但 `handleCreateModuleResolver` 恒写入非 nil → 读取处用 `!` 断言（语义同 Go：nil 会在下游被解引用）。
// 11) `module.ResolverOptions` 在 Go 是**结构体**，`resolverFor` 的 `options := c.options` 是复制；C# 是引用类型，
//    故显式 `new Module.ResolverOptions { … }` 复制全部六个字段，保持"不改动 `programResolutionContext.options`"的语义。
// 12) Go 的包级函数（`compileModuleResolutionSpec`/`staticModuleResolutionToResolvedModule`/`moduleResolutionTraceToStrings`/
//    `moduleResolutionError`）→ 本 partial 类的 `internal static` 成员（C# 没有自由函数，契约 §2；同 Session.Files.cs 偏离 10）。
//    Session 的方法保留 Go 名（`handleXxx` → `HandleXxx`）。`ModuleResolverFactory` 既是本文件的类型名、又是 Session 的方法名
//    （Go 里同样重名），故构造该类型时用全限定名 `TypeScript.Api.ModuleResolverFactory`（简单名会被同名方法遮蔽 → CS0118）。
// 13) `tspath.Xxx` → `TypeScript.TSPath.TSPath.Xxx`（在 `TypeScript.Api` 里裸写 `TSPath` 会解析到命名空间 `TypeScript.TSPath`，
//    同 Session.Files.cs 偏离 12）。
// 14) Go 的 `json.Value`（`callbackResult`）→ C# `Ipc.Conn.Call` 返回 `byte[]?`；`len(callbackResult) == 0 ||
//    string(callbackResult) == "null"` → `null` / 空数组 / 与 `"null"u8` 逐字节相等（UTF-8 下与 Go 的字符串比较等价，
//    先例 ContentMapper/HostImpl.cs:1364 的 `"null"u8`）。
// 15) `json.Unmarshal(callbackResult, &staticResolution)` → `UnmarshalNew<StaticModuleResolution>(callbackResult)`
//    （失败抛异常，按第 4 条包成 `InvalidOperationException`）。
// 16) `moduleResolutionTraceToStrings`：Go 的 `core.Map` 对 nil 切片返回 nil、对非 nil 空切片返回非 nil 空切片，二者在
//    `json:"trace,omitempty"` 下都是"不写出"；本仓 `Core.Map` 对 null 输入返回空数组、不保留 nil
//    （见 Session.Snapshot.cs:398 的同类处理），故这里显式保留 nil 语义：空输入返回 `null`。
// 17) `moduleResolutionError` 在 Go 返回 `error`；本仓 `Compiler.Program.ModuleResolutionError()` 返回 `Exception?`
//    （Compiler/Program.Checker.cs:132），故 C# 版直接 `throw`（父冻结的形状：`internal static void`）。用 `throw err`
//    而非 ExceptionDispatchInfo：调用方（session.go:1251/1299）只关心异常对象本身，Go 的 error 也没有栈可言。
// 18) `Host: s` / `Host: sd.snapshot`：Go 里 `*Session` / `*project.Snapshot` 隐式满足 `module.ResolutionHost`；C# 没有结构类型，
//    二者已在类型声明上显式列出 `TypeScript.Module.ResolutionHost`（先例 Compiler/Host.cs:5-9 的同因注释）。
// 19) 本文件没有 goroutine 深递归入口（bind/check/print/emit 一个都不涉及），不需要显式 64 MiB 栈线程。
namespace TypeScript.Api;

using ResolutionMode = TypeScript.Core.ModuleKind;
using TSLocale = TypeScript.Locale.Locale;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;

/// <summary>Go: <c>type moduleResolverFactory struct</c>（module_resolution.go:19-25）。</summary>
internal sealed class ModuleResolverFactory : Project.ModuleResolverFactory
{
    /// <summary>Go: <c>registration *moduleResolverRegistration</c>（module_resolution.go:20）。</summary>
    internal ModuleResolverRegistration Registration = null!;

    /// <summary>Go: <c>session *Session</c>（module_resolution.go:21）。</summary>
    internal Session Session = null!;

    /// <summary>Go: <c>conn ipc.Conn</c>（module_resolution.go:22）。</summary>
    internal Ipc.Conn Conn = null!;

    /// <summary>Go: <c>ctx context.Context</c>（module_resolution.go:23）。</summary>
    internal Core.Context Ctx = null!;

    /// <summary>Go: <c>currentDirectory string</c>（module_resolution.go:24）。</summary>
    internal string CurrentDirectory = "";

    /// <summary>Go: <c>func (f *moduleResolverFactory) NewResolver(options module.ResolverOptions) (module.Resolver, func())</c>（module_resolution.go:43-69）。</summary>
    public (Module.Resolver Resolver, Action Release) NewResolver(Module.ResolverOptions options)
    {
        options.CompilerOptions = Registration.CompilerOptions!;
        Module.Resolver fallback = Module.Module.NewResolver(options);
        if (Registration.ResolveModuleNameCallback == "")
        {
            if (Registration.Resolutions is not null)
            {
                fallback = Module.StaticResolver.NewStaticResolver(fallback, Registration.Resolutions);
            }

            return (fallback, static () => { });
        }

        var contextId = this.Session.RegisterProgramResolutionContext(fallback, options, Registration);
        Module.Resolver resolver = new CallbackModuleResolver
        {
            Registration = Registration,
            Conn = Conn,
            Ctx = Ctx,
            CurrentDirectory = CurrentDirectory,
            ProgramResolutionContextID = contextId,
            FallbackResolver = fallback,
        };
        if (Registration.Resolutions is not null)
        {
            resolver = Module.StaticResolver.NewStaticResolver(resolver, Registration.Resolutions);
        }

        return (resolver, () => this.Session.ReleaseProgramResolutionContext(contextId));
    }
}

/// <summary>Go: <c>type programResolutionContext struct</c>（module_resolution.go:27-31）。</summary>
internal sealed class ProgramResolutionContext
{
    /// <summary>Go: <c>options module.ResolverOptions</c>（module_resolution.go:28）。</summary>
    internal Module.ResolverOptions Options = null!;

    /// <summary>Go: <c>resolvers map[ModuleResolverID]module.Resolver</c>（module_resolution.go:29）。</summary>
    internal Dictionary<ModuleResolverID, Module.Resolver> Resolvers = [];

    /// <summary>Go: <c>mu sync.Mutex</c>（module_resolution.go:30）→ C# `System.Threading.Lock`（契约 §2）。</summary>
    internal Lock Mu = new();

    /// <summary>Go: <c>func (c *programResolutionContext) resolverFor(registration *moduleResolverRegistration) module.Resolver</c>（module_resolution.go:272-283）。</summary>
    internal Module.Resolver ResolverFor(ModuleResolverRegistration registration)
    {
        lock (Mu)
        {
            if (Resolvers.TryGetValue(registration.Id, out var existing))
            {
                return existing;
            }

            // Go 的 `options := c.options` 是结构体复制（见文件头第 11 条）。
            var options = new Module.ResolverOptions
            {
                Host = Options.Host,
                CompilerOptions = registration.CompilerOptions!,
                TypingsLocation = Options.TypingsLocation,
                ProjectName = Options.ProjectName,
                ExtraExtensions = Options.ExtraExtensions,
                PackageJsonCache = Options.PackageJsonCache,
            };
            var resolver = Module.Module.NewResolver(options);
            Resolvers[registration.Id] = resolver;
            return resolver;
        }
    }
}

/// <summary>Go: <c>type callbackModuleResolver struct</c>（module_resolution.go:33-41）。</summary>
internal sealed class CallbackModuleResolver : Module.Resolver
{
    /// <summary>Go: <c>registration *moduleResolverRegistration</c>（module_resolution.go:34）。</summary>
    internal ModuleResolverRegistration Registration = null!;

    /// <summary>Go: <c>conn ipc.Conn</c>（module_resolution.go:35）。</summary>
    internal Ipc.Conn Conn = null!;

    /// <summary>Go: <c>ctx context.Context</c>（module_resolution.go:36）。</summary>
    internal Core.Context Ctx = null!;

    /// <summary>Go: <c>currentDirectory string</c>（module_resolution.go:37）。</summary>
    internal string CurrentDirectory = "";

    /// <summary>Go: <c>snapshot SnapshotID</c>（module_resolution.go:38）。</summary>
    internal SnapshotID Snapshot;

    /// <summary>Go: <c>programResolutionContextID uint64</c>（module_resolution.go:39）。</summary>
    internal ulong ProgramResolutionContextID;

    /// <summary>Go: <c>fallbackResolver module.Resolver</c>（module_resolution.go:40）。</summary>
    internal Module.Resolver FallbackResolver = null!;

    /// <summary>Go: <c>func (p *callbackModuleResolver) ResolveModuleName(moduleName string, containingFile string, resolutionMode core.ResolutionMode, redirectedReference module.ResolvedProjectReference) (*module.ResolvedModule, []module.DiagAndArgs, error)</c>（module_resolution.go:71-78）。</summary>
    public (Module.ResolvedModule? Module, Module.DiagAndArgs[] Traces) ResolveModuleName(
        string moduleName,
        string containingFile,
        ResolutionMode resolutionMode,
        Module.ResolvedProjectReference? redirectedReference)
    {
        return ResolveModuleName(moduleName, containingFile, TypeScript.TSPath.TSPath.GetDirectoryPath(containingFile), resolutionMode, redirectedReference);
    }

    /// <summary>Go: <c>func (p *callbackModuleResolver) ResolveModuleNameFromDirectory(moduleName string, containingDirectory string, resolutionMode core.ResolutionMode) (*module.ResolvedModule, []module.DiagAndArgs, error)</c>（module_resolution.go:80-86）。</summary>
    public (Module.ResolvedModule? Module, Module.DiagAndArgs[] Traces) ResolveModuleNameFromDirectory(
        string moduleName,
        string containingDirectory,
        ResolutionMode resolutionMode)
    {
        return ResolveModuleName(moduleName, containingDirectory, containingDirectory, resolutionMode, null);
    }

    /// <summary>Go: <c>func (p *callbackModuleResolver) resolveModuleName(moduleName string, containingFile string, containingDirectory string, resolutionMode core.ResolutionMode, redirectedReference module.ResolvedProjectReference) (*module.ResolvedModule, []module.DiagAndArgs, error)</c>（module_resolution.go:88-120）。
    /// Go 的未导出方法 → C# private 重载（与上面的 public 方法同名不同元数；Go 的 `containingFile` 形参在 C# 侧保留但不使用）。</summary>
    internal (Module.ResolvedModule? Module, Module.DiagAndArgs[] Traces) ResolveModuleName(
        string moduleName,
        string containingFile,
        string containingDirectory,
        ResolutionMode resolutionMode,
        Module.ResolvedProjectReference? redirectedReference)
    {
        var @params = new ResolveModuleNameCallbackParams
        {
            ModuleName = moduleName,
            ContainingDirectory = containingDirectory,
        };
        if (Snapshot.Value != 0)
        {
            @params.Snapshot = Snapshot;
        }

        if (ProgramResolutionContextID != 0)
        {
            @params.InProgressSnapshot = ProgramResolutionContextID;
        }

        @params.ResolutionMode = resolutionMode;

        byte[]? callbackResult;
        try
        {
            callbackResult = Conn.Call(Ctx, Registration.ResolveModuleNameCallback, @params);
        }
        catch (Exception err)
        {
            throw new InvalidOperationException($"resolveModuleName callback failed: {err.Message}", err);
        }

        if (callbackResult is null || callbackResult.Length == 0 || callbackResult.AsSpan().SequenceEqual("null"u8))
        {
            return (null, []);
        }

        StaticModuleResolution staticResolution;
        try
        {
            staticResolution = UnmarshalNew<StaticModuleResolution>(callbackResult);
        }
        catch (Exception err)
        {
            throw new InvalidOperationException($"invalid resolveModuleName callback result: {err.Message}", err);
        }

        return (Session.StaticModuleResolutionToResolvedModule(staticResolution, CurrentDirectory), []);
    }

    /// <summary>Go: <c>func (p *callbackModuleResolver) ResolveTypeReferenceDirective(typeReferenceDirectiveName string, containingFile string, resolutionMode core.ResolutionMode, redirectedReference module.ResolvedProjectReference) (*module.ResolvedTypeReferenceDirective, []module.DiagAndArgs)</c>（module_resolution.go:122-129）。</summary>
    public (Module.ResolvedTypeReferenceDirective? Directive, Module.DiagAndArgs[] Traces) ResolveTypeReferenceDirective(
        string typeReferenceDirectiveName,
        string containingFile,
        ResolutionMode resolutionMode,
        Module.ResolvedProjectReference? redirectedReference)
    {
        return FallbackResolver.ResolveTypeReferenceDirective(typeReferenceDirectiveName, containingFile, resolutionMode, redirectedReference);
    }

    /// <summary>Go: <c>func (p *callbackModuleResolver) GetPackageScopeForPath(directory string) *packagejson.InfoCacheEntry</c>（module_resolution.go:131-133）。</summary>
    public PackageJson.InfoCacheEntry? GetPackageScopeForPath(string directory)
    {
        return FallbackResolver.GetPackageScopeForPath(directory);
    }

    /// <summary>Go: <c>func (p *callbackModuleResolver) PackageJsonCacheEntries(f func(key tspath.Path, value *packagejson.InfoCacheEntry) bool)</c>（module_resolution.go:135-137）。</summary>
    public void PackageJsonCacheEntries(Func<TypeScript.TSPath.Path, PackageJson.InfoCacheEntry?, bool> f)
    {
        FallbackResolver.PackageJsonCacheEntries(f);
    }

    /// <summary>Go: <c>func (p *callbackModuleResolver) ResolvePackageDirectory(moduleName string, containingFile string, resolutionMode core.ResolutionMode, redirectedReference module.ResolvedProjectReference) *module.ResolvedModule</c>（module_resolution.go:139-146）。</summary>
    public Module.ResolvedModule? ResolvePackageDirectory(
        string moduleName,
        string containingFile,
        ResolutionMode resolutionMode,
        Module.ResolvedProjectReference? redirectedReference)
    {
        return FallbackResolver.ResolvePackageDirectory(moduleName, containingFile, resolutionMode, redirectedReference);
    }
}

public sealed partial class Session
{
    /// <summary>Go: <c>func compileModuleResolutionSpec(spec *ModuleResolutionSpec, currentDirectory string, useCaseSensitive bool) (*module.StaticResolutions, error)</c>（module_resolution.go:148-194）。</summary>
    internal static Module.StaticResolutions? CompileModuleResolutionSpec(
        ModuleResolutionSpec? spec,
        string currentDirectory,
        bool useCaseSensitive)
    {
        if (spec is null)
        {
            return null;
        }

        bool fallbackToResolution;
        if (spec.Fallback == ModuleResolutionFallback.Resolve)
        {
            fallbackToResolution = true;
        }
        else if (spec.Fallback == ModuleResolutionFallback.Unresolved)
        {
            fallbackToResolution = false;
        }
        else
        {
            throw new ClientErrorException($"api: client error: invalid module resolution fallback \"{spec.Fallback}\"");
        }

        // Go 的 `len(spec.Entries)` 把 nil 当 0（`Entries` 的 Go tag 是 nonnil，但解码仍可能得到 nil）。
        var specEntries = spec.Entries ?? [];
        var entries = new List<Module.StaticResolutionEntry>(specEntries.Length);
        for (var i = 0; i < specEntries.Length; i++)
        {
            var entry = specEntries[i];
            if (entry is null)
            {
                throw new ClientErrorException($"api: client error: module resolution entry {i} is null");
            }

            if (entry.ModuleName == "")
            {
                throw new ClientErrorException($"api: client error: module resolution entry {i} has an empty moduleName");
            }

            if (entry.Result is null)
            {
                throw new ClientErrorException($"api: client error: module resolution entry {i} has no result");
            }

            var staticEntry = new Module.StaticResolutionEntry { ModuleName = entry.ModuleName };
            if (entry.ContainingDirectory is not null)
            {
                staticEntry.ContainingDirectory = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(
                    entry.ContainingDirectory.ToAbsoluteFileName(currentDirectory),
                    currentDirectory);
            }

            if (entry.ResolutionMode is { } entryMode)
            {
                var mode = entryMode;
                if (mode != ResolutionModeNone && mode != ResolutionModeCommonJS && mode != ResolutionModeESM)
                {
                    throw new ClientErrorException($"api: client error: module resolution entry {i} has invalid resolutionMode {mode.String()}");
                }

                staticEntry.ResolutionMode = mode;
            }

            staticEntry.Result = StaticModuleResolutionToResolvedModule(entry.Result, currentDirectory);
            entries.Add(staticEntry);
        }

        try
        {
            return Module.StaticResolutions.NewStaticResolutions([.. entries], fallbackToResolution, currentDirectory, useCaseSensitive);
        }
        catch (ArgumentException err)
        {
            // Go: `fmt.Errorf("%w: %w", ErrClientError, err)`。
            throw new ClientErrorException($"api: client error: {err.Message}", err);
        }
    }

    /// <summary>Go: <c>func staticModuleResolutionToResolvedModule(staticResolution *StaticModuleResolution, currentDirectory string) *module.ResolvedModule</c>（module_resolution.go:196-221）。</summary>
    internal static Module.ResolvedModule? StaticModuleResolutionToResolvedModule(
        StaticModuleResolution? staticResolution,
        string currentDirectory)
    {
        if (staticResolution is null || staticResolution.ResolvedFileName is null)
        {
            return null;
        }

        var resolvedFileName = staticResolution.ResolvedFileName;
        var result = new Module.ResolvedModule
        {
            ResolvedFileName = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(
                resolvedFileName.ToAbsoluteFileName(currentDirectory),
                currentDirectory),
        };
        if (staticResolution.OriginalPath is not null)
        {
            result.OriginalPath = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(
                staticResolution.OriginalPath.ToAbsoluteFileName(currentDirectory),
                currentDirectory);
        }

        if (staticResolution.PackageID is { } packageId)
        {
            result.PackageId = new Module.PackageId
            {
                Name = packageId.Name,
                SubModuleName = packageId.SubModuleName,
                Version = packageId.Version,
                PeerDependencies = packageId.PeerDependencies,
            };
        }

        var originalPath = result.ResolvedFileName;
        if (result.OriginalPath != "")
        {
            originalPath = result.OriginalPath;
        }

        result.Extension = TypeScript.TSPath.TSPath.TryGetExtensionFromPath(result.ResolvedFileName);
        result.IsExternalLibraryImport = originalPath.Contains("/node_modules/", StringComparison.Ordinal);
        return result;
    }

    /// <summary>Go: <c>func moduleResolutionTraceToStrings(trace []module.DiagAndArgs) []string</c>（module_resolution.go:223-227）。
    /// Go 的 nil 切片 → `null`（见文件头第 16 条；`ResolveModuleNameResult.Trace` 是 omitempty）。</summary>
    internal static string[]? ModuleResolutionTraceToStrings(Module.DiagAndArgs[] trace)
    {
        if (trace.Length == 0)
        {
            return null;
        }

        return Map(trace, static entry => entry.Message.Localize(TSLocale.Default, entry.Args));
    }

    /// <summary>Go: <c>func moduleResolutionError(snapshot *project.Snapshot) error</c>（module_resolution.go:285-294）。
    /// 父冻结的形状：抛异常而非返回 error（见文件头第 17 条）。</summary>
    internal static void ModuleResolutionError(Project.Snapshot snapshot)
    {
        foreach (var project in snapshot.ProjectCollection.Projects())
        {
            if (project.Program is not null)
            {
                var err = project.Program.ModuleResolutionError();
                if (err is not null)
                {
                    throw err;
                }
            }
        }
    }

    /// <summary>Go: <c>func (s *Session) moduleResolverFactory(ctx context.Context, options *CreateProgramOptions) (project.ModuleResolverFactory, error)</c>（module_resolution.go:229-249）。</summary>
    internal Project.ModuleResolverFactory? ModuleResolverFactory(Core.Context ctx, CreateProgramOptions options)
    {
        if (options.ModuleResolver.Value == 0)
        {
            return null;
        }

        _moduleResolversMu.EnterReadLock();
        ModuleResolverRegistration? data;
        try
        {
            _moduleResolvers.TryGetValue(options.ModuleResolver, out data);
        }
        finally
        {
            _moduleResolversMu.ExitReadLock();
        }

        if (data is null)
        {
            throw new ClientErrorException($"api: client error: module resolver {options.ModuleResolver.Value} not found");
        }

        if (data.ResolveModuleNameCallback != "" && _conn is null)
        {
            throw new ClientErrorException("api: client error: API connection is not initialized");
        }

        return new TypeScript.Api.ModuleResolverFactory
        {
            Registration = data,
            Session = this,
            Conn = _conn!,
            Ctx = ctx,
            CurrentDirectory = GetCurrentDirectory(),
        };
    }

    /// <summary>Go: <c>func (s *Session) registerProgramResolutionContext(resolver module.Resolver, options module.ResolverOptions, registration *moduleResolverRegistration) uint64</c>（module_resolution.go:251-264）。</summary>
    internal ulong RegisterProgramResolutionContext(
        Module.Resolver resolver,
        Module.ResolverOptions options,
        ModuleResolverRegistration registration)
    {
        var id = (ulong)Interlocked.Increment(ref _nextProgramResolutionContextId);
        _programResolutionContextsMu.EnterWriteLock();
        try
        {
            _programResolutionContexts[id] = new ProgramResolutionContext
            {
                Options = options,
                Resolvers = new Dictionary<ModuleResolverID, Module.Resolver> { [registration.Id] = resolver },
            };
        }
        finally
        {
            _programResolutionContextsMu.ExitWriteLock();
        }

        return id;
    }

    /// <summary>Go: <c>func (s *Session) releaseProgramResolutionContext(id uint64)</c>（module_resolution.go:266-270）。</summary>
    internal void ReleaseProgramResolutionContext(ulong id)
    {
        _programResolutionContextsMu.EnterWriteLock();
        try
        {
            _programResolutionContexts.Remove(id);
        }
        finally
        {
            _programResolutionContextsMu.ExitWriteLock();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleCreateModuleResolver(params *CreateModuleResolverParams) (ModuleResolverID, error)</c>（module_resolution.go:296-316）。</summary>
    internal ModuleResolverID HandleCreateModuleResolver(CreateModuleResolverParams @params)
    {
        var provider = CompileModuleResolutionSpec(
            @params.ModuleResolutions,
            GetCurrentDirectory(),
            FS().UseCaseSensitiveFileNames());
        var id = new ModuleResolverID((ulong)Interlocked.Increment(ref _nextModuleResolverId));
        var data = new ModuleResolverRegistration
        {
            Id = id,
            CompilerOptions = @params.CompilerOptions,
            Resolutions = provider,
            ResolveModuleNameCallback = @params.ResolveModuleNameCallback,
        };
        _moduleResolversMu.EnterWriteLock();
        try
        {
            _moduleResolvers[id] = data;
        }
        finally
        {
            _moduleResolversMu.ExitWriteLock();
        }

        return id;
    }

    /// <summary>Go: <c>func (s *Session) handleReleaseModuleResolver(params *ReleaseModuleResolverParams) (any, error)</c>（module_resolution.go:318-329）。</summary>
    internal object? HandleReleaseModuleResolver(ReleaseModuleResolverParams @params)
    {
        bool ok;
        _moduleResolversMu.EnterWriteLock();
        try
        {
            ok = _moduleResolvers.Remove(@params.Resolver);
        }
        finally
        {
            _moduleResolversMu.ExitWriteLock();
        }

        if (!ok)
        {
            throw new ClientErrorException($"api: client error: module resolver {@params.Resolver.Value} not found");
        }

        return null;
    }

    /// <summary>Go: <c>func (s *Session) handleResolveModuleName(ctx context.Context, params *ResolveModuleNameParams) (*ResolveModuleNameResult, error)</c>（module_resolution.go:331-401）。</summary>
    internal ResolveModuleNameResult HandleResolveModuleName(Core.Context ctx, ResolveModuleNameParams @params)
    {
        if (@params.ModuleName == "")
        {
            throw new ClientErrorException("api: client error: moduleName is empty");
        }

        _moduleResolversMu.EnterReadLock();
        ModuleResolverRegistration? data;
        try
        {
            _moduleResolvers.TryGetValue(@params.Resolver, out data);
        }
        finally
        {
            _moduleResolversMu.ExitReadLock();
        }

        if (data is null)
        {
            throw new ClientErrorException($"api: client error: module resolver {@params.Resolver.Value} not found");
        }

        ResolutionMode mode = ResolutionModeNone;
        if (@params.ResolutionMode is { } paramsMode)
        {
            mode = paramsMode;
            if (mode != ResolutionModeNone && mode != ResolutionModeCommonJS && mode != ResolutionModeESM)
            {
                throw new ClientErrorException($"api: client error: invalid resolutionMode {mode.String()}");
            }
        }

        var containingDirectory = TypeScript.TSPath.TSPath.GetNormalizedAbsolutePath(
            @params.ContainingDirectory.ToAbsoluteFileName(GetCurrentDirectory()),
            GetCurrentDirectory());

        Module.Resolver resolver;
        if (@params.Snapshot.Value != 0 && @params.InProgressSnapshot != 0)
        {
            throw new ClientErrorException("api: client error: snapshot and inProgressSnapshot are mutually exclusive");
        }

        if (@params.InProgressSnapshot != 0)
        {
            _programResolutionContextsMu.EnterReadLock();
            ProgramResolutionContext? resolutionContext;
            try
            {
                _programResolutionContexts.TryGetValue(@params.InProgressSnapshot, out resolutionContext);
            }
            finally
            {
                _programResolutionContextsMu.ExitReadLock();
            }

            if (resolutionContext is null)
            {
                throw new ClientErrorException($"api: client error: in-progress snapshot {@params.InProgressSnapshot} not found");
            }

            resolver = resolutionContext.ResolverFor(data);
        }
        else if (@params.Snapshot.Value != 0)
        {
            var sd = GetSnapshotData(@params.Snapshot);
            resolver = Module.Module.NewResolver(new Module.ResolverOptions
            {
                Host = sd.Snapshot,
                CompilerOptions = data.CompilerOptions!,
                ExtraExtensions = sd.Snapshot.ContentMapperExtensions(),
            });
        }
        else
        {
            resolver = Module.Module.NewResolver(new Module.ResolverOptions
            {
                Host = this,
                CompilerOptions = data.CompilerOptions!,
            });
        }

        if (data.ResolveModuleNameCallback != "")
        {
            resolver = new CallbackModuleResolver
            {
                Registration = data,
                Conn = _conn!,
                Ctx = ctx,
                CurrentDirectory = GetCurrentDirectory(),
                Snapshot = @params.Snapshot,
                ProgramResolutionContextID = @params.InProgressSnapshot,
                FallbackResolver = resolver,
            };
        }

        if (data.Resolutions is not null)
        {
            resolver = Module.StaticResolver.NewStaticResolver(resolver, data.Resolutions);
        }

        var (result, trace) = resolver.ResolveModuleNameFromDirectory(@params.ModuleName, containingDirectory, mode);
        return new ResolveModuleNameResult
        {
            ResolvedModule = NewResolvedModuleResponse(result),
            Trace = ModuleResolutionTraceToStrings(trace),
        };
    }
}
