// ported from: internal/api/session.go:47-694
//
// 本文件是 `Session` 的**唯一声明处**（`Api/Session.*.cs` 的其它文件都是它的 partial 分片），并持有
// `snapshotData` / `moduleResolverRegistration` / `projectRegistryData` / `checkerSetup` / `batchResponsePage` /
// `SessionOptions` / `DefaultMaxResponseBytesPerPage` 以及 `Session` 的**全部字段**
// （字段名受跨分片契约 `local://api-port-contract.md` §4 冻结，其它分片直接按名字读写）。
//
// 与 Go 的偏离（逐条说明）：
//
//  1) `(T, error)` → 正常路径返回 T、错误路径抛异常（契约 §1.2）。因此 `getProgram` / `getProject` /
//     `getSnapshotData` / `retainSnapshotData` / `releaseSnapshot` / `setupChecker` / `setupLanguageService`
//     的 error 一律落成 `ClientErrorException`，文本与 Go 的 `fmt.Errorf("%w: …", ErrClientError)` 逐字一致
//     （`ErrClientError` 的文本是 "api: client error"，见 `Api.cs`；先例 `Api/ProtocolMsgPack.cs`）。
//     `releaseSnapshot` 因此返回 `void`：Go 的 error 里只有"快照不存在"这一种信息，C# 只能靠异常表达
//     （Go 的 `handleRelease` 也是原样向外传播该 error）。
//
//  2) 并发原语按契约 §2 改写：`sync.RWMutex` → `ReaderWriterLockSlim`（EnterReadLock/EnterWriteLock）；
//     `sync.Mutex` → `System.Threading.Lock`；`sync.Map`（`batchResponsePages`）→ `ConcurrentDictionary`；
//     `atomic.Uint64` → `long` + `Interlocked`。锁的**临界区边界与 Go 逐行一致**（包括 Go 显式 Unlock 后
//     再返回错误的分支——C# 用 `try/finally` 表达，语义相同）。
//
//  3) Go 的 `snapshotData` / `projectRegistryData` / `moduleResolverRegistration` / `batchResponsePage` /
//     `checkerSetup` 都是包级未导出类型 → C# 落成 `internal` 类型，但**成员一律 public**：跨分片契约要求
//     其它 `Session.*.cs` 直接读写这些字段（本仓先例：`Project/Snapshot.cs` 的 public 字段）。
//
//  4) `checkerSetup` 在 Go 里是值类型（`func (setup checkerSetup)` 值接收者）→ C# 用 `readonly struct`；
//     `readonly struct` 要求所有实例字段 `readonly`，所以构造只能走构造函数（对应 Go 的结构体字面量）。
//
//  5) Go 的 `collections.Set[T]` 零值可用（底层 map 惰性分配、`Len()`/`Has()` 对 nil map 安全），
//     C# 的 `Collections.Set<T>` 是 class（内部 `M = new HashSet<T>()`）→ 零值语义在 C# 里必须显式
//     `new()`（`_openProjects` / `_openFiles` / `_createdPrograms` / `OpenProjects` / `OpenFiles`），
//     与 Go 的空集等价。
//
//  6) `checkerSetup.newIndexInfoResponse` 里 Go 的 `*setup.sd.newTypeResponse(...)`（返回值是 nil 时
//     Go 是 nil 解引用 panic）→ C# 先判空再抛 `DebugFailureException`（`TypeScript.DebugFailureException`，
//     与 Go 的 panic 同一语义，文本更可读）。
//
//  7) `nodeHandleFrom` 的 `fmt.Sprintf("%d.%d.%s", idx, node.Kind, path)`：Go 的 `node.Kind` 是 int32，
//     `%d` 打印十进制数字；C# 的 `Ast.Node.Kind` 是 enum（直接插值会打印成员名）→ 必须 `(int)node.Kind`。
//     `path` 用 `Path.ToString()`（`TSPath.Path` 是 record struct 且已覆写为底层字符串，等价 Go 的 `%s`）。
//
//  8) Go 的包级函数 `snapshotHandle`（session.go:544-546）→ `Session` 的 `internal static SnapshotHandle`：
//     先例 `Ls/LanguageService.cs` 把包级 `NewLanguageService` 挂到它服务的类型上；这样 `Session.*.cs` 的
//     partial 分片里可以不加限定名直接调用（跨分片契约里就是这么引用的）。实现**委托**给 `Api.SnapshotHandle`
//     （同一函数在 `Api.cs` 里已有落点，避免两处重复逻辑；`Session.Diagnostics.cs` 用的是 `Api.*` 那份）。
//
//  9) `Session` 的 `readonly`：Go 的 `NewStandaloneSession` 在 `newSession` 之后才写 `ownsSnapshotHost`
//     （session.go:486），readonly 字段不能这样赋值 → 私有构造函数多一个 `ownsSnapshotHost` 参数
//     （默认 false，等价于 Go 的 `newSession` 版本）。
//
// 10) `Project.Session` 内嵌 `*project.SnapshotHost`，C# 落成**继承**（`Project/Session.cs` 文件头第 1 条：
//     `public sealed partial class Session : SnapshotHost`）→ Go 的 `projectSession.SnapshotHost`
//     （session.go:478）在 C# 里就是 `projectSession` 本身（向上转型），没有同名属性可访问。
//
// 11) `cpuProfiler pprof.CPUProfiler`（session.go:450）在 Go 里是值类型：零值即可用、不触发任何行为。
//     C# 的对应物是 class `TypeScript.Execute.CPUProfiler`（`Execute/Pprof.cs`；`Lsp/Server.cs:329` 证实
//     真实类型名，契约 §4 写的 `Execute.Pprof.CpuProfiler` 在仓里不存在）→ 字段写成 `Execute.CPUProfiler?`
//     并由使用方惰性创建（`??=`），行为与 Go 的零值一致。
//
// 12) Go 的包级 `sessionIDCounter atomic.Uint64`（session.go:47）→ `private static long _sessionIdCounter`
//     + `Interlocked.Increment`（从 1 开始，与 Go 的 `Add(1)` 一致）。
//
// 13) Go 的 `*api.Session` 是**结构化**满足 `module.ResolutionHost`（module_resolution.go:370-373 直接把 `s`
//     当 Host 用）；Go 只看方法集是否覆盖，C# 必须显式声明接口，因此类声明带上
//     `TypeScript.Module.ResolutionHost`（`FS()` / `GetCurrentDirectory()` 本文件已提供，行为不变）。
//
// 依赖的**其它分片**成员（本文件只引用、不声明；名字已由父协调冻结）：
//     `FormatSessionID(ulong)`（session.go:4740 → `Api/Session.Diagnostics.cs`，SessionDiag）、
//     `SnapshotData.ResolveNodeHandle(Compiler.Program, NodeHandle)`（session.go:4523 → `Api/Session.Snapshot.cs`
//     的 `internal sealed partial class SnapshotData`，签名已核对：返回非空 `Ast.Node`、失败抛 ClientErrorException）、
//     `Api.Encoder.Encoder.GetNodeIndexTable(Ast.SourceFile)`（`Api/Encoder/Encoder.cs`，已核对）、
//     `Api.NewTypeResponse` / `Api.TypeHandles` / `Api.SymbolHandles`（proto.go:1196/1283/1107 → `Api/Proto.cs`，父持有）、
//     proto 消息类型 `SymbolResponse` / `TypeResponse` / `SignatureResponse`（`Api/Proto.Types2.cs`，字段名已核对）/
//     `IndexInfoResponse` / `DocumentIdentifier`（尚未落地，字段名按 Go），
//     以及 `internal sealed class ProgramResolutionContext`（module_resolution.go:27）。

using System.Collections.Concurrent;
using TypeScript.Collections;
using ApiEncoder = TypeScript.Api.Encoder.Encoder;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Api;

/// <summary>
/// Go: <c>type snapshotData struct</c>（session.go:53-78）：单个快照的会话状态（快照本体 + 该快照作用域的
/// 符号/类型注册表）。多个客户端可以通过引用计数共享同一个快照；`RefCount` 归零时注册表随之清理。
/// </summary>
/// <remarks>
/// C# 侧是 `partial`：`ResolveNodeHandle`（Go session.go:4523-4558）由 `Api/Session.Snapshot.cs` 的分片
/// 补在同一个类上（见文件头依赖清单），因此这里必须写 `partial`（父协调已确认）。
/// </remarks>
internal sealed partial class SnapshotData
{
    /// <summary>Go: <c>snapshot *project.Snapshot</c>（session.go:54）。</summary>
    public Project.Snapshot Snapshot = null!;

    /// <summary>Go: <c>fileSystem vfs.FS</c>（session.go:55）。</summary>
    public Vfs.FS FileSystem = null!;

    /// <summary>Go: <c>refCount int</c>（session.go:56）。</summary>
    public int RefCount;

    /// <summary>Go: <c>openProjects collections.Set[tspath.Path]</c>（session.go:58）。</summary>
    public Set<Path> OpenProjects = new();

    /// <summary>Go: <c>openFiles collections.Set[tspath.Path]</c>（session.go:59）。</summary>
    public Set<Path> OpenFiles = new();

    /// <summary>
    /// Go: <c>symbolRegistry map[SymbolID]*ast.Symbol</c>（session.go:65）。Symbol ID 来自
    /// <c>ast.GetSymbolId</c>（全局原子计数器），因此同一个 <c>*ast.Symbol</c> 在整个快照的所有项目里 ID 相同；
    /// 注册表按**快照**维度共享，以保证"从两个项目查询同一个符号得到同一个 handle"。
    /// </summary>
    public Dictionary<SymbolID, Ast.Symbol> SymbolRegistry = [];

    /// <summary>Go: <c>symbolRegistryMu sync.RWMutex</c>（session.go:66）。</summary>
    public ReaderWriterLockSlim SymbolRegistryMu = new();

    /// <summary>
    /// Go: <c>symbolCanonicalProjects map[SymbolID]project.ID</c>（session.go:74）：每个已注册符号"首次被观察到"
    /// 的项目。需要项目上下文（成员/导出顺序、node handle 解析）却没从调用方拿到项目时默认用它；首个写入者胜出，
    /// 保证稳定。由 <c>SymbolRegistryMu</c> 保护。
    /// </summary>
    public Dictionary<SymbolID, Project.ID> SymbolCanonicalProjects = [];

    /// <summary>Go: <c>projectRegistries map[project.ID]*projectRegistryData</c>（session.go:76）。</summary>
    public Dictionary<Project.ID, ProjectRegistryData> ProjectRegistries = [];

    /// <summary>Go: <c>projectRegistriesMu sync.RWMutex</c>（session.go:77）。</summary>
    public ReaderWriterLockSlim ProjectRegistriesMu = new();

    /// <summary>Go: <c>func (sd *snapshotData) getProgram(projectHandle project.ID) (*compiler.Program, error)</c>（session.go:100-113）。</summary>
    internal Compiler.Program GetProgram(Project.ID projectHandle)
    {
        var proj = GetProject(projectHandle);

        var program = proj.GetProgram();
        if (program is null)
        {
            throw new ClientErrorException("api: client error: project has no program");
        }

        return program;
    }

    /// <summary>Go: <c>func (sd *snapshotData) getProject(projectHandle project.ID) (*project.Project, error)</c>（session.go:115-123）。</summary>
    internal Project.Project GetProject(Project.ID projectHandle)
    {
        var proj = Snapshot.ProjectCollection.GetProject(projectHandle);
        if (proj is null)
        {
            throw new ClientErrorException($"api: client error: project {projectHandle.String()} not found");
        }
        return proj;
    }

    /// <summary>Go: <c>func (sd *snapshotData) nodeHandleFrom(node *ast.Node) NodeHandle</c>（session.go:125-132）：
    /// 生成"index.kind.path"形式的句柄，必要时按需为该文件建立 node 索引表。</summary>
    internal NodeHandle NodeHandleFrom(Ast.Node node)
    {
        // Go: ast.GetSourceFileOfNode(node).Path()；Go 在 node 无 sourceFile 时是 nil 解引用 panic，
        // C# 的 `!` 之后就是同样的 NRE。
        var sourceFile = Ast.Ast.GetSourceFileOfNode(node)!;
        var path = sourceFile.Path();
        var table = ApiEncoder.GetNodeIndexTable(sourceFile);
        var idx = table.GetIndex(node);
        return new NodeHandle($"{idx}.{(int)node.Kind}.{path}");
    }

    /// <summary>Go: <c>func (sd *snapshotData) getOrCreateProjectRegistry(projectID project.ID) *projectRegistryData</c>（session.go:134-159）。</summary>
    internal ProjectRegistryData GetOrCreateProjectRegistry(Project.ID projectID)
    {
        if (string.IsNullOrEmpty(projectID.Value))
        {
            throw new DebugFailureException("getOrCreateProjectRegistry: empty project ID");
        }

        // Fast path: registry already exists — read lock only.
        ProjectRegistriesMu.EnterReadLock();
        ProjectRegistryData? reg;
        try
        {
            ProjectRegistries.TryGetValue(projectID, out reg);
        }
        finally
        {
            ProjectRegistriesMu.ExitReadLock();
        }

        if (reg is not null)
        {
            return reg;
        }

        // Slow path: create under write lock.
        ProjectRegistriesMu.EnterWriteLock();
        try
        {
            if (!ProjectRegistries.TryGetValue(projectID, out var existing))
            {
                existing = new ProjectRegistryData
                {
                    TypeRegistry = [],
                    SignatureRegistry = [],
                };
                ProjectRegistries[projectID] = existing;
            }

            return existing;
        }
        finally
        {
            ProjectRegistriesMu.ExitWriteLock();
        }
    }

    /// <summary>
    /// Go: <c>func (sd *snapshotData) newSymbolResponse(symbol *ast.Symbol, canonicalProject project.ID) *SymbolResponse</c>（session.go:161-200）。
    /// <c>canonicalProject</c> 是观察到该符号的项目、必须非空；它被记录为符号的规范项目（首个写入者胜出）
    /// 并回传给客户端，供后续项目作用域的查询（成员/导出、node 解析）作默认值。
    /// </summary>
    internal SymbolResponse? NewSymbolResponse(Ast.Symbol? symbol, Project.ID canonicalProject)
    {
        if (symbol is null)
        {
            return null;
        }

        var (id, project) = RegisterSymbol(symbol, canonicalProject);
        var resp = new SymbolResponse
        {
            Id = id,
            Project = project,
            Name = Ast.Ast.EscapeSymbolName(symbol.Name),
            Flags = (uint)symbol.Flags,
            CheckFlags = (uint)symbol.CheckFlags,
        };

        if (symbol.Declarations is { Length: > 0 })
        {
            resp.Declarations = new NodeHandle[symbol.Declarations.Length];
            for (var i = 0; i < symbol.Declarations.Length; i++)
            {
                resp.Declarations[i] = NodeHandleFrom(symbol.Declarations[i]);
            }
        }

        if (symbol.ValueDeclaration is not null)
        {
            resp.ValueDeclaration = NodeHandleFrom(symbol.ValueDeclaration);
        }

        if (symbol.Parent is not null)
        {
            resp.Parent = Api.SymbolHandle(symbol.Parent);
        }

        if (symbol.ExportSymbol is not null)
        {
            resp.ExportSymbol = Api.SymbolHandle(symbol.ExportSymbol);
        }

        return resp;
    }

    /// <summary>
    /// Go: <c>func (sd *snapshotData) registerSymbol(symbol *ast.Symbol, canonicalProject project.ID) (SymbolID, project.ID)</c>（session.go:202-227）。
    /// 返回句柄及其规范项目；规范项目是首次观察到该符号的项目（稳定），且**恒非空**。
    /// </summary>
    internal (SymbolID Id, Project.ID Project) RegisterSymbol(Ast.Symbol? symbol, Project.ID canonicalProject)
    {
        if (symbol is null)
        {
            return (default, new Project.ID(""));
        }
        if (string.IsNullOrEmpty(canonicalProject.Value))
        {
            throw new DebugFailureException("registerSymbol requires a non-empty canonical project");
        }

        var id = Api.SymbolHandle(symbol);
        SymbolRegistryMu.EnterWriteLock();
        try
        {
            var existing = SymbolRegistry.GetValueOrDefault(id);
            if (existing is not null)
            {
                if (!ReferenceEquals(existing, symbol))
                {
                    throw new DebugFailureException("duplicate symbol");
                }
            }
            else
            {
                SymbolRegistry[id] = symbol;
            }

            if (!SymbolCanonicalProjects.TryGetValue(id, out var project))
            {
                SymbolCanonicalProjects[id] = canonicalProject;
                project = canonicalProject;
            }

            return (id, project);
        }
        finally
        {
            SymbolRegistryMu.ExitWriteLock();
        }
    }

    /// <summary>Go: <c>func (sd *snapshotData) newTypeResponse(projectID project.ID, t *checker.Type, c *checker.Checker) *TypeResponse</c>（session.go:229-254）。</summary>
    internal TypeResponse? NewTypeResponse(Project.ID projectID, Checker.Type? t, Checker.Checker c)
    {
        if (t is null)
        {
            return null;
        }

        var resp = Api.NewTypeResponse(t, RegisterType(projectID, t));
        if ((t.ObjectFlags() & Checker.ObjectFlags.Mapped) != 0)
        {
            var mapped = t.AsMappedType()!;
            mapped.ResolveComponents(c, t);
            resp.TypeParameter = RegisterType(projectID, mapped.TypeParameter());
            resp.ConstraintType = RegisterType(projectID, mapped.ConstraintType());
            resp.NameType = RegisterType(projectID, mapped.NameType());
            resp.TemplateType = RegisterType(projectID, mapped.TemplateType());
        }

        if (Checker.Checker.IsTupleTypeTarget(t))
        {
            var elementInfos = t.AsTupleType()!.ElementInfos();
            for (var i = 0; i < elementInfos.Length; i++)
            {
                var declaration = elementInfos[i].LabeledDeclaration();
                if (declaration is not null)
                {
                    resp.LabeledElementDeclarations ??= new NodeHandle[elementInfos.Length];
                    resp.LabeledElementDeclarations[i] = NodeHandleFrom(declaration);
                }
            }
        }

        return resp;
    }

    /// <summary>Go: <c>func (sd *snapshotData) registerType(projectID project.ID, t *checker.Type) TypeID</c>（session.go:256-275）。</summary>
    internal TypeID RegisterType(Project.ID projectID, Checker.Type? t)
    {
        if (t is null)
        {
            return default;
        }

        var id = Api.TypeHandle(t);
        var reg = GetOrCreateProjectRegistry(projectID);
        reg.TypeRegistryMu.EnterWriteLock();
        try
        {
            var existing = reg.TypeRegistry.GetValueOrDefault(id);

            if (existing is not null)
            {
                if (!ReferenceEquals(existing, t))
                {
                    throw new DebugFailureException("duplicate type");
                }
                return id;
            }

            reg.TypeRegistry[id] = t;
            return id;
        }
        finally
        {
            reg.TypeRegistryMu.ExitWriteLock();
        }
    }

    /// <summary>Go: <c>func (sd *snapshotData) resolveSymbolHandle(handle SymbolID) (*ast.Symbol, error)</c>（session.go:277-292）。</summary>
    internal Ast.Symbol ResolveSymbolHandle(SymbolID handle)
    {
        if (handle.Value == 0)
        {
            throw new ClientErrorException("api: client error: empty symbol handle");
        }

        SymbolRegistryMu.EnterReadLock();
        Ast.Symbol? symbol;
        try
        {
            SymbolRegistry.TryGetValue(handle, out symbol);
        }
        finally
        {
            SymbolRegistryMu.ExitReadLock();
        }

        if (symbol is null)
        {
            throw new ClientErrorException($"api: client error: symbol handle {handle.Value} not found in snapshot registry");
        }

        return symbol;
    }

    /// <summary>Go: <c>func (sd *snapshotData) resolveTypeHandle(projectID project.ID, handle TypeID) (*checker.Type, error)</c>（session.go:294-320）。</summary>
    internal Checker.Type ResolveTypeHandle(Project.ID projectID, TypeID handle)
    {
        if (handle.Value == 0)
        {
            throw new ClientErrorException("api: client error: empty type handle");
        }
        if (string.IsNullOrEmpty(projectID.Value))
        {
            throw new ClientErrorException($"api: client error: empty project ID for type handle {handle.Value}");
        }

        ProjectRegistriesMu.EnterReadLock();
        ProjectRegistryData? reg;
        try
        {
            ProjectRegistries.TryGetValue(projectID, out reg);
        }
        finally
        {
            ProjectRegistriesMu.ExitReadLock();
        }

        if (reg is null)
        {
            throw new ClientErrorException($"api: client error: type handle {handle.Value} not found (no registry for project {projectID.String()})");
        }

        reg.TypeRegistryMu.EnterReadLock();
        Checker.Type? t;
        try
        {
            reg.TypeRegistry.TryGetValue(handle, out t);
        }
        finally
        {
            reg.TypeRegistryMu.ExitReadLock();
        }

        if (t is null)
        {
            throw new ClientErrorException($"api: client error: type handle {handle.Value} not found in project registry");
        }

        return t;
    }

    /// <summary>Go: <c>func (sd *snapshotData) resolveSignatureHandle(projectID project.ID, handle SignatureID) (*checker.Signature, error)</c>（session.go:322-348）。</summary>
    internal Checker.Signature ResolveSignatureHandle(Project.ID projectID, SignatureID handle)
    {
        if (handle.Value == 0)
        {
            throw new ClientErrorException("api: client error: empty signature handle");
        }
        if (string.IsNullOrEmpty(projectID.Value))
        {
            throw new ClientErrorException($"api: client error: empty project ID for signature handle {handle.Value}");
        }

        ProjectRegistriesMu.EnterReadLock();
        ProjectRegistryData? reg;
        try
        {
            ProjectRegistries.TryGetValue(projectID, out reg);
        }
        finally
        {
            ProjectRegistriesMu.ExitReadLock();
        }

        if (reg is null)
        {
            throw new ClientErrorException($"api: client error: signature handle {handle.Value} not found (no registry for project {projectID.String()})");
        }

        reg.SignatureRegistryMu.EnterReadLock();
        Checker.Signature? sig;
        try
        {
            reg.SignatureRegistry.TryGetValue(handle, out sig);
        }
        finally
        {
            reg.SignatureRegistryMu.ExitReadLock();
        }

        if (sig is null)
        {
            throw new ClientErrorException($"api: client error: signature handle {handle.Value} not found in project registry");
        }

        return sig;
    }

    /// <summary>Go: <c>func (sd *snapshotData) newSignatureResponse(projectID project.ID, sig *checker.Signature) *SignatureResponse</c>（session.go:350-380）。</summary>
    internal SignatureResponse? NewSignatureResponse(Project.ID projectID, Checker.Signature? sig)
    {
        if (sig is null)
        {
            return null;
        }

        var resp = new SignatureResponse
        {
            Id = RegisterSignature(projectID, sig),
            Flags = (uint)sig.Flags(),
        };

        if (sig.Declaration() is not null)
        {
            resp.Declaration = NodeHandleFrom(sig.Declaration()!);
        }

        if (sig.TypeParameters().Length > 0)
        {
            resp.TypeParameters = Api.TypeHandles(sig.TypeParameters());
        }

        if (sig.Parameters().Length > 0)
        {
            resp.Parameters = Api.SymbolHandles(sig.Parameters());
        }

        if (sig.ThisParameter() is not null)
        {
            resp.ThisParameter = Api.SymbolHandle(sig.ThisParameter()!);
        }

        if (sig.Target() is not null)
        {
            resp.Target = Api.SignatureHandle(sig.Target()!);
        }

        return resp;
    }

    /// <summary>Go: <c>func (sd *snapshotData) registerSignature(projectID project.ID, sig *checker.Signature) SignatureID</c>（session.go:382-404）。</summary>
    internal SignatureID RegisterSignature(Project.ID projectID, Checker.Signature? sig)
    {
        if (sig is null)
        {
            return default;
        }

        var id = Api.SignatureHandle(sig);
        var reg = GetOrCreateProjectRegistry(projectID);
        reg.SignatureRegistryMu.EnterWriteLock();
        try
        {
            var existing = reg.SignatureRegistry.GetValueOrDefault(id);

            if (existing is not null)
            {
                if (!ReferenceEquals(existing, sig))
                {
                    throw new DebugFailureException("duplicate signature");
                }
                return id;
            }

            reg.SignatureRegistry[id] = sig;
            return id;
        }
        finally
        {
            reg.SignatureRegistryMu.ExitWriteLock();
        }
    }
}

/// <summary>Go: <c>type moduleResolverRegistration struct</c>（session.go:80-90）。</summary>
internal sealed class ModuleResolverRegistration
{
    /// <summary>Go: <c>id ModuleResolverID</c>（session.go:81）。</summary>
    public ModuleResolverID Id;

    /// <summary>Go: <c>compilerOptions *core.CompilerOptions</c>（session.go:82）。</summary>
    public Core.CompilerOptions? CompilerOptions;

    /// <summary>Go: <c>resolutions *module.StaticResolutions</c>（session.go:83）。</summary>
    public Module.StaticResolutions? Resolutions;

    /// <summary>Go: <c>resolveModuleNameCallback string</c>（session.go:84）。</summary>
    public string ResolveModuleNameCallback = "";
}

/// <summary>
/// Go: <c>type projectRegistryData struct</c>（session.go:91-99）：按项目隔离的类型/签名注册表。
/// 类型与签名用的是"每个 checker 各自的顺序 ID"，同一个本地 ID 会在多个项目里重复出现；按项目分表避免碰撞，
/// 项目被移除时也能干净地整体释放。
/// </summary>
internal sealed class ProjectRegistryData
{
    /// <summary>Go: <c>typeRegistry map[TypeID]*checker.Type</c>（session.go:92）。</summary>
    public Dictionary<TypeID, Checker.Type> TypeRegistry = [];

    /// <summary>Go: <c>typeRegistryMu sync.RWMutex</c>（session.go:93）。</summary>
    public ReaderWriterLockSlim TypeRegistryMu = new();

    /// <summary>Go: <c>signatureRegistry map[SignatureID]*checker.Signature</c>（session.go:95）。</summary>
    public Dictionary<SignatureID, Checker.Signature> SignatureRegistry = [];

    /// <summary>Go: <c>signatureRegistryMu sync.RWMutex</c>（session.go:96）。</summary>
    public ReaderWriterLockSlim SignatureRegistryMu = new();
}

/// <summary>
/// Go: <c>type checkerSetup struct</c>（session.go:592-598）：需要类型检查器的那些 handler 共用的上下文。
/// 字段名由跨分片契约冻结（`Sd` / `Program` / `Checker` / `Done` / `ProjectID`）。
/// </summary>
internal readonly struct CheckerSetup(
    SnapshotData sd,
    Compiler.Program program,
    Checker.Checker checker,
    Action done,
    Project.ID projectID)
{
    /// <summary>Go: <c>sd *snapshotData</c>（session.go:593）。</summary>
    public readonly SnapshotData Sd = sd;

    /// <summary>Go: <c>program *compiler.Program</c>（session.go:594）。</summary>
    public readonly Compiler.Program Program = program;

    /// <summary>Go: <c>checker *checker.Checker</c>（session.go:595）。</summary>
    public readonly Checker.Checker Checker = checker;

    /// <summary>Go: <c>done func()</c>（session.go:596）：调用方必须 defer 它以释放 checker。</summary>
    public readonly Action Done = done;

    /// <summary>Go: <c>projectID project.ID</c>（session.go:597）。</summary>
    public readonly Project.ID ProjectID = projectID;

    /// <summary>Go: <c>func (setup checkerSetup) newSymbolResponse(sym *ast.Symbol) *SymbolResponse</c>（session.go:600-602）。</summary>
    internal SymbolResponse? NewSymbolResponse(Ast.Symbol? sym)
    {
        return Sd.NewSymbolResponse(sym, ProjectID);
    }

    /// <summary>Go: <c>func (setup checkerSetup) newSignatureResponse(sig *checker.Signature) *SignatureResponse</c>（session.go:604-606）。</summary>
    internal SignatureResponse? NewSignatureResponse(Checker.Signature? sig)
    {
        return Sd.NewSignatureResponse(ProjectID, sig);
    }

    /// <summary>Go: <c>func (setup checkerSetup) newIndexInfoResponse(info *checker.IndexInfo) *IndexInfoResponse</c>（session.go:608-621）。</summary>
    internal IndexInfoResponse? NewIndexInfoResponse(Checker.IndexInfo? info)
    {
        if (info is null)
        {
            return null;
        }

        // 见文件头偏离 6：Go 的 `*setup.sd.newTypeResponse(...)` 在 nil 时是解引用 panic。
        var keyType = Sd.NewTypeResponse(ProjectID, info.KeyType(), Checker)
            ?? throw new DebugFailureException("newIndexInfoResponse: IndexInfo has no key type");
        var valueType = Sd.NewTypeResponse(ProjectID, info.ValueType(), Checker)
            ?? throw new DebugFailureException("newIndexInfoResponse: IndexInfo has no value type");

        var result = new IndexInfoResponse
        {
            KeyType = keyType,
            ValueType = valueType,
            IsReadonly = info.IsReadonly(),
        };
        if (info.Declaration() is not null)
        {
            result.Declaration = Sd.NodeHandleFrom(info.Declaration()!);
        }
        return result;
    }

    /// <summary>Go: <c>func (setup checkerSetup) resolveTypeHandle(id TypeID) (*checker.Type, error)</c>（session.go:623-625）。</summary>
    internal Checker.Type ResolveTypeHandle(TypeID id)
    {
        return Sd.ResolveTypeHandle(ProjectID, id);
    }

    /// <summary>Go: <c>func (setup checkerSetup) resolveSymbolHandle(id SymbolID) (*ast.Symbol, error)</c>（session.go:627-629）。</summary>
    internal Ast.Symbol ResolveSymbolHandle(SymbolID id)
    {
        return Sd.ResolveSymbolHandle(id);
    }

    /// <summary>Go: <c>func (setup checkerSetup) resolveSignatureHandle(id SignatureID) (*checker.Signature, error)</c>（session.go:631-635）。</summary>
    internal Checker.Signature ResolveSignatureHandle(SignatureID id)
    {
        return Sd.ResolveSignatureHandle(ProjectID, id);
    }

    /// <summary>
    /// Go: <c>func (setup checkerSetup) resolveLocation(handle NodeHandle, file *DocumentIdentifier, position *uint32) (*ast.Node, error)</c>（session.go:637-651）。
    /// 位置以 node handle 或"文件 + 位置"给出；两者都缺时返回 nil（Go 同样返回 `nil, nil`，**不**是错误）。
    /// </summary>
    /// <remarks>
    /// 偏离：Go 的错误文本用 `%v` 打印整个 `DocumentIdentifier` 结构体；C# 没有结构体字面量打印，
    /// 改为打印其文件名（同类信息、更可读）。
    /// </remarks>
    internal Ast.Node? ResolveLocation(NodeHandle handle, DocumentIdentifier? file, uint? position)
    {
        if (handle.Value.Length != 0)
        {
            return Sd.ResolveNodeHandle(Program, handle);
        }
        if (file is not null && position is not null)
        {
            var sourceFile = Program.GetSourceFile(file.ToFileName());
            if (sourceFile is null)
            {
                throw new ClientErrorException($"api: client error: source file not found: {file.ToFileName()}");
            }
            return AstNav.AstNav.GetTouchingPropertyName(
                sourceFile,
                sourceFile.GetPositionMap().UTF16ToUTF8((int)position.Value));
        }
        return null;
    }
}

/// <summary>Go: <c>type batchResponsePage struct</c>（session.go:459-462）：一批已编码的响应（分页用）。</summary>
internal sealed class BatchResponsePage
{
    /// <summary>Go: <c>encodedResponses []json.Value</c>（session.go:460）。Go 的 `json.Value` 是原始 JSON 字节
    /// → C# 的 `byte[]`（契约 §5）。</summary>
    public byte[][] EncodedResponses = [];
}

/// <summary>Go: <c>type SessionOptions struct</c>（session.go:467-472）：API 会话的配置。</summary>
public sealed class SessionOptions
{
    /// <summary>Go: <c>UseBinaryResponses bool</c>（session.go:470）：为 msgpack 协议启用二进制响应。</summary>
    public bool UseBinaryResponses;
}

/// <summary>
/// Go: <c>type Session struct</c>（session.go:407-458）：一个 API 会话，通过 LSP 服务器提供对 TypeScript
/// 语言服务的程序化访问。它实现 <c>Handler</c> 接口来处理传入的 API 请求。会话可以同时持有多个快照，
/// 每个快照有自己的符号/类型注册表以维持对象同一性。
/// </summary>
/// <remarks>
/// 这是 `Session` 的**唯一声明处**；`HandleRequest` / `HandleNotification` 与各 `handleXxx` 分散在
/// `Api/Session.*.cs` 的 partial 分片里。
/// </remarks>
public sealed partial class Session : TypeScript.Ipc.Handler, TypeScript.Module.ResolutionHost
{
    /// <summary>Go: <c>var sessionIDCounter atomic.Uint64</c>（session.go:47）。见文件头偏离 12。</summary>
    internal static long _sessionIdCounter;

    /// <summary>Go: <c>id string</c>（session.go:408）。</summary>
    internal readonly string _id;

    /// <summary>Go: <c>snapshotHost *project.SnapshotHost</c>（session.go:409）。</summary>
    internal readonly Project.SnapshotHost _snapshotHost;

    /// <summary>Go: <c>ownsSnapshotHost bool</c>（session.go:410）。</summary>
    internal readonly bool _ownsSnapshotHost;

    /// <summary>Go: <c>withLocale func(context.Context) context.Context</c>（session.go:411）：恒非空
    /// （`newSession` 会把 nil 换成恒等函数）。</summary>
    internal readonly Func<Core.Context, Core.Context> _withLocale;

    /// <summary>Go: <c>projectSession *project.Session</c>（session.go:412）。</summary>
    internal Project.Session? _projectSession;

    /// <summary>Go: <c>closeOnce sync.Once</c>（session.go:414）→ 标志 + `_closeMu` 双检。</summary>
    internal bool _closeOnce;

    /// <summary>Go: <c>sync.Once</c> 的锁（见 `_closeOnce`）。</summary>
    internal readonly Lock _closeMu = new();

    /// <summary>Go: <c>useBinaryResponses bool</c>（session.go:417）：使用 MessagePackProtocol 时为 true。</summary>
    internal bool _useBinaryResponses;

    /// <summary>Go: <c>batchResponsePages sync.Map</c>（session.go:418）。</summary>
    internal readonly ConcurrentDictionary<string, BatchResponsePage> _batchResponsePages = new();

    /// <summary>Go: <c>nextBatchResponsePageID atomic.Uint64</c>（session.go:419）。</summary>
    internal long _nextBatchResponsePageId;

    /// <summary>Go: <c>snapshots map[SnapshotID]*snapshotData</c>（session.go:428）：句柄 → 该快照的数据。</summary>
    internal readonly Dictionary<SnapshotID, SnapshotData> _snapshots = [];

    /// <summary>Go: <c>snapshotsMu sync.RWMutex</c>（session.go:429）：只用于短小的、以 map 为边界的临界区，
    /// 绝不跨慢操作（快照更新/checker 查询）持有；读 handler 取读锁，handleRelease 与快照创建记账取写锁。</summary>
    internal readonly ReaderWriterLockSlim _snapshotsMu = new();

    /// <summary>Go: <c>openProjects collections.Set[tspath.Path]</c>（session.go:433）：本 API 客户端拥有的
    /// 规范 LSP 状态资源，由 `_languageServerUpdateMu` 保护。</summary>
    internal Set<Path> _openProjects = new();

    /// <summary>Go: <c>openFiles collections.Set[tspath.Path]</c>（session.go:434）。</summary>
    internal Set<Path> _openFiles = new();

    /// <summary>Go: <c>createdPrograms collections.Set[project.SyntheticProjectID]</c>（session.go:435）。</summary>
    internal Set<Project.SyntheticProjectID> _createdPrograms = new();

    /// <summary>Go: <c>languageServerUpdateMu sync.Mutex</c>（session.go:437）。</summary>
    internal readonly Lock _languageServerUpdateMu = new();

    /// <summary>Go: <c>buildOrchestrators map[BuildOrchestratorID]*build.Orchestrator</c>（session.go:439）。</summary>
    internal readonly Dictionary<BuildOrchestratorID, Execute.Build.Orchestrator> _buildOrchestrators = [];

    /// <summary>Go: <c>buildMu sync.Mutex</c>（session.go:440）。</summary>
    internal readonly Lock _buildMu = new();

    /// <summary>Go: <c>nextModuleResolverID atomic.Uint64</c>（session.go:442）。</summary>
    internal long _nextModuleResolverId;

    /// <summary>Go: <c>moduleResolvers map[ModuleResolverID]*moduleResolverRegistration</c>（session.go:443）。</summary>
    internal readonly Dictionary<ModuleResolverID, ModuleResolverRegistration> _moduleResolvers = [];

    /// <summary>Go: <c>moduleResolversMu sync.RWMutex</c>（session.go:444）。</summary>
    internal readonly ReaderWriterLockSlim _moduleResolversMu = new();

    /// <summary>Go: <c>nextProgramResolutionContextID atomic.Uint64</c>（session.go:445）。</summary>
    internal long _nextProgramResolutionContextId;

    /// <summary>Go: <c>programResolutionContexts map[uint64]*programResolutionContext</c>（session.go:446）。</summary>
    internal readonly Dictionary<ulong, ProgramResolutionContext> _programResolutionContexts = [];

    /// <summary>Go: <c>programResolutionContextsMu sync.RWMutex</c>（session.go:447）。</summary>
    internal readonly ReaderWriterLockSlim _programResolutionContextsMu = new();

    /// <summary>Go: <c>sourceFileLeases map[SourceFileLeaseID]*project.SourceFileLease</c>（session.go:448）。</summary>
    internal readonly Dictionary<SourceFileLeaseID, Project.SourceFileLease> _sourceFileLeases = [];

    /// <summary>Go: <c>sourceFileLeasesMu sync.Mutex</c>（session.go:449）。</summary>
    internal readonly Lock _sourceFileLeasesMu = new();

    /// <summary>Go: <c>nextSourceFileLeaseID atomic.Uint64</c>（session.go:450）。</summary>
    internal long _nextSourceFileLeaseId;

    /// <summary>Go: <c>conn ipc.Conn</c>（session.go:451）。</summary>
    internal Ipc.Conn? _conn;

    /// <summary>Go: <c>cpuProfiler pprof.CPUProfiler</c>（session.go:453）。见文件头偏离 11。</summary>
    internal Execute.CPUProfiler? _cpuProfiler;

    /// <summary>Go: <c>func newSession(snapshotHost *project.SnapshotHost, withLocale func(context.Context) context.Context, options *SessionOptions) *Session</c>（session.go:491-511）。
    /// 见文件头偏离 9（多一个 `ownsSnapshotHost` 参数以保持 readonly 语义）。</summary>
    internal Session(
        Project.SnapshotHost snapshotHost,
        Func<Core.Context, Core.Context>? withLocale,
        SessionOptions? options,
        bool ownsSnapshotHost = false)
    {
        var id = (ulong)Interlocked.Increment(ref _sessionIdCounter);
        _id = FormatSessionID(id);
        _snapshotHost = snapshotHost;
        _withLocale = withLocale ?? (static ctx => ctx);
        _ownsSnapshotHost = ownsSnapshotHost;
        if (options is not null)
        {
            _useBinaryResponses = options.UseBinaryResponses;
        }
    }

    /// <summary>Go: <c>func NewLSPSession(projectSession *project.Session, options *SessionOptions) *Session</c>（session.go:477-482）。</summary>
    public static Session NewLSPSession(Project.Session projectSession, SessionOptions? options)
    {
        // Go: projectSession.SnapshotHost —— C# 里 Project.Session 继承自 Project.SnapshotHost（见文件头偏离 10）。
        var s = new Session(projectSession, projectSession.WithCurrentLocale, options);
        s._projectSession = projectSession;
        return s;
    }

    /// <summary>Go: <c>func NewStandaloneSession(init *project.SessionInit, options *SessionOptions) *Session</c>（session.go:484-489）。</summary>
    public static Session NewStandaloneSession(Project.SessionInit init, SessionOptions? options)
    {
        var snapshotHost = Project.SnapshotHost.NewSnapshotHost(init);
        return new Session(snapshotHost, null, options, ownsSnapshotHost: true);
    }

    /// <summary>Go: <c>DefaultMaxResponseBytesPerPage = 300_000_000</c>（session.go:472-474）：在 V8 最大字符串长度
    /// 之下为 base64 膨胀留出空间，同时取整到偶数十进制值。</summary>
    public const int DefaultMaxResponseBytesPerPage = 300_000_000;

    /// <summary>Go: <c>func (s *Session) ID() string</c>（session.go:513-515）：本会话的唯一标识。</summary>
    public string ID()
    {
        return _id;
    }

    /// <summary>Go: <c>func (s *Session) SetConnection(conn ipc.Conn)</c>（session.go:517-519）。</summary>
    public void SetConnection(Ipc.Conn conn)
    {
        _conn = conn;
    }

    /// <summary>Go: <c>func (s *Session) GetCurrentDirectory() string</c>（session.go:521-523）。</summary>
    public string GetCurrentDirectory()
    {
        return _snapshotHost.GetCurrentDirectory();
    }

    /// <summary>Go: <c>func (s *Session) FS() vfs.FS</c>（session.go:525-530）。</summary>
    public Vfs.FS FS()
    {
        if (_projectSession is not null)
        {
            return _projectSession.FS();
        }
        return _snapshotHost.FS();
    }

    /// <summary>Go: <c>func (s *Session) DefaultLibraryPath() string</c>（session.go:532-537）。</summary>
    public string DefaultLibraryPath()
    {
        if (_projectSession is not null)
        {
            return _projectSession.DefaultLibraryPath();
        }
        return _snapshotHost.DefaultLibraryPath();
    }

    /// <summary>Go: <c>func (s *Session) useCaseSensitiveFileNames() bool</c>（session.go:539-541）。</summary>
    internal bool UseCaseSensitiveFileNames()
    {
        return _snapshotHost.FS().UseCaseSensitiveFileNames();
    }

    /// <summary>Go: <c>func snapshotHandle(snapshot *project.Snapshot) SnapshotID</c>（session.go:544-546）：由快照 ID 生成句柄。
    /// 实现委托给 `Api.SnapshotHandle`（同一个 Go 包级函数在 `Api.cs` 里已有落点，避免两处逻辑重复）；本成员存在的
    /// 意义是让 `Session.*.cs` 的 partial 分片能不加限定名直接调用（跨分片契约就是这么引用的）。</summary>
    internal static SnapshotID SnapshotHandle(Project.Snapshot snapshot)
    {
        return Api.SnapshotHandle(snapshot);
    }

    /// <summary>Go: <c>func (s *Session) getSnapshotData(handle SnapshotID) (*snapshotData, error)</c>（session.go:549-558）：按句柄查快照数据。</summary>
    internal SnapshotData GetSnapshotData(SnapshotID handle)
    {
        _snapshotsMu.EnterReadLock();
        SnapshotData? sd;
        try
        {
            _snapshots.TryGetValue(handle, out sd);
        }
        finally
        {
            _snapshotsMu.ExitReadLock();
        }
        if (sd is null)
        {
            throw new ClientErrorException($"api: client error: snapshot {handle.Value} not found");
        }
        return sd;
    }

    /// <summary>Go: <c>func (s *Session) retainSnapshotData(handle SnapshotID) (*snapshotData, error)</c>（session.go:560-569）：
    /// 在一次操作要构造派生快照期间把快照数据钉住（`RefCount++`）。</summary>
    internal SnapshotData RetainSnapshotData(SnapshotID handle)
    {
        _snapshotsMu.EnterWriteLock();
        try
        {
            if (!_snapshots.TryGetValue(handle, out var sd))
            {
                throw new ClientErrorException($"api: client error: snapshot {handle.Value} not found");
            }
            sd.RefCount++;
            return sd;
        }
        finally
        {
            _snapshotsMu.ExitWriteLock();
        }
    }

    /// <summary>Go: <c>func (s *Session) releaseSnapshot(handle SnapshotID) error</c>（session.go:571-590）：
    /// `RefCount--`，归零时从表里移除并 `Deref()` 快照（见文件头偏离 1：错误 → 异常，故返回 void）。</summary>
    internal void ReleaseSnapshot(SnapshotID handle)
    {
        _snapshotsMu.EnterWriteLock();
        SnapshotData? sd;
        try
        {
            sd = _snapshots.GetValueOrDefault(handle);
            if (sd is null)
            {
                throw new ClientErrorException($"api: client error: snapshot {handle.Value} not found");
            }
            sd.RefCount--;
            if (sd.RefCount <= 0)
            {
                _snapshots.Remove(handle);
            }
            else
            {
                sd = null;
            }
        }
        finally
        {
            _snapshotsMu.ExitWriteLock();
        }

        sd?.Snapshot.Deref();
    }

    /// <summary>Go: <c>func (s *Session) setupChecker(ctx context.Context, snapshot SnapshotID, projectHandle project.ID) (checkerSetup, error)</c>（session.go:653-683）：
    /// 解析快照、程序与类型检查器；调用方必须调用 `CheckerSetup.Done` 释放 checker。</summary>
    internal CheckerSetup SetupChecker(Core.Context ctx, SnapshotID snapshot, Project.ID projectHandle)
    {
        var sd = GetSnapshotData(snapshot);

        var program = sd.GetProgram(projectHandle);

        var (c, done) = program.GetTypeChecker(Core.Core.WithCheckerLifetime(ctx, Core.CheckerLifetime.API));
        return new CheckerSetup(sd, program, c, done, projectHandle);
    }

    /// <summary>
    /// Go: <c>func (s *Session) setupLanguageService(snapshot *project.Snapshot, program *compiler.Program, projectHandle project.ID, activeFile string) (*ls.LanguageService, error)</c>（session.go:685-692）。
    /// 与 `setupChecker` 不同，它**不**从池里取 checker：只需要 LS 的调用方因此不必阻塞在/占住池化 checker 上。
    /// LS 内部会按 ctx 的 checker lifetime 自己取 checker；若 handler 返回的句柄之后要在 API checker 上再查询，
    /// 调用方需先用 `Core.WithCheckerLifetime(ctx, Core.CheckerLifetime.API)` 包装 ctx（仅在该 LS 操作
    /// 恰好取一次 checker 时安全，嵌套取用会在这唯一的常驻 checker 上死锁）。
    /// </summary>
    internal Ls.LanguageService SetupLanguageService(Project.Snapshot snapshot, Compiler.Program program, Project.ID projectHandle, string activeFile)
    {
        var proj = snapshot.ProjectCollection.GetProject(projectHandle);
        if (proj is null)
        {
            throw new ClientErrorException($"api: client error: project {projectHandle.String()} not found");
        }
        return Ls.LanguageService.NewLanguageService(proj.ID(), program, snapshot, activeFile);
    }
}
