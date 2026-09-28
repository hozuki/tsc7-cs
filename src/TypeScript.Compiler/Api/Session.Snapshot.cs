// ported from: internal/api/session.go:1226-1829（快照 / 构建编排器 / 配置解析 handlers）
//   + internal/api/session.go:4523-4682（resolveNodeHandle / computeSnapshotChanges / createSnapshotResponse /
//     createSnapshotOperationResponse）
//
// 本文件= 本切片的全部内容；session.go:1223-1225 的 handleInitialize 不在本切片范围内（由别的分片负责）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `(T, error)` → 正常路径返回值、错误路径抛异常（契约 §1.2）：
//    - `ErrClientError` 家族（`fmt.Errorf("%w: …", ErrClientError, …)`）→ `ClientErrorException`，Go 的嵌套 `%w` 值
//      作为 `InnerException` 保留（`fmt.Errorf("%w: A: %w", ErrClientError, err)` 的两层嵌套在 C# 折成
//      "A: {err.Message}" + InnerException = err —— Go 的 `%w` 链在 C# 没有等价物）。
//    - 非 client 的 `errors.New(...)`（build orchestrator 未找到、Spawn 不支持）→ `InvalidOperationException`：
//      Go 侧它们同样**不**是 `ErrClientError`，服务器按"内部错误"分类；C# 用同一个区分（不是 ClientErrorException）。
//    - `panic(...)` → `Debug.Fail(...)`（`TypeScript.Debug`，抛 `DebugFailureException`）。
//
// 2) Go 的**内嵌结构体** `SnapshotRequestChangesParams` → C# 的**基类继承**（Proto.Types.cs 文件头第 1 条）：
//    `CreateSnapshotParams` / `LanguageServerSnapshotChanges` 都派生自它，因此 Go 的 `&params.SnapshotRequestChangesParams`
//    在 C# 就是隐式上转型（同一个实例、无拷贝、无取址），`ToAPISnapshotRequest` / `CreateSnapshotResponse` /
//    `CreateSnapshotOperationResponse` / `ToLanguageServerSnapshotUpdate` 保持 Go 的单方法形状与参数类型。
//
// 3) `snapshotOpenState` / `languageServerSnapshotUpdate` → `internal sealed class`（Go 的未导出结构体）。
//    Go 的 `collections.Set[T]` 是"值结构体 + 指针接收者"，C# 的 `Collections.Set<T>` 是类：Go 的
//    `snapshotOpenState{}`（零值 Set，内部 map 为 nil）在 C# 里必须写成 `new SnapshotOpenState()`（字段初始化为空 Set），
//    否则 `Clone()`/`Add()` 会 NRE。
//
// 4) `apiBuildSystem` → `Session` 的**嵌套** `private sealed class ApiBuildSystem`：Go 的方法体访问
//    `s.session.snapshotHost`（同包可见的未导出字段），C# 里 `Session._snapshotHost` 是 private，只有同类可访问；
//    嵌套类是最贴近 Go"同包可见"的落法（顶层类则需要给 Session 加一个非 Go 的访问器）。
//
// 5) 集合边遍历边删：Go 允许 `for k := range m { delete(m, k) }`，C# 的 `HashSet`/`Dictionary` 会抛
//    `InvalidOperationException` → 先复制 keys（`new List<X>(set.Keys())`）再改原集合；迭代顺序本就不确定，语义等价。
//
// 6) `strconv.ParseUint(s, 10, 32)` → `uint.TryParse(s, NumberStyles.None, InvariantCulture, out idx)`：位数与 Go 的
//    bitSize=32 一致，`NumberStyles.None` 同样拒绝空白/正负号/十六进制（Go 的 ParseUint 只接受十进制数字）。
//    解析失败时 Go 用 `%w` 把 strconv 的错误包进返回值，C# 的 `TryParse` 没有异常对象 → 该信息只体现在消息文本里。
//
// 7) `time.Now()` / `time.Since(start)` → `DateTime.Now` / `DateTime.Now - start`（本仓 `System` 实现的既有约定，
//    见 Vfs/VfsTest 的 SystemClock）；`io.Discard` → `TextWriter.Null`（`io.Writer` → `TextWriter` 是本仓既有映射）。
//
// 8) `moduleResolutionError(snapshot)` 在 C# 是**抛异常**的 `ModuleResolutionError(Project.Snapshot)`（契约 §1.2），
//    所以 Go 的 `if err := …; err != nil { snapshot.Deref(); return nil, err }` 落成
//    `try { ModuleResolutionError(snapshot); } catch { snapshot.Deref(); throw; }`（Deref 后原样重抛）。
//
// 9) `s.toPath` / `s.toFileChangeSummary`（session.go:4745/4750）与 `moduleResolutionError`（module_resolution.go:285）
//    不是本切片的内容：前者由 Session.Diagnostics.cs 提供（同 partial 类的 private 成员），后者是 static 帮助方法。
//
// 10) `encoder.GetNodeIndexTable(sourceFile)` 在 C# 永不返回 null（Go 的 error 路径在那里是 panic/异常）→
//     Go 的 `if table != nil && …` 退化为只留范围检查（`table.Nodes` 是 `Ast.Node?[]`，index 0 是 nil 哨兵）。
//
// 11) `languageServerSnapshotUpdate.commit` 会写 Session 的未导出字段；C# 里 `_openProjects`/`_openFiles`/
//     `_createdPrograms` 是 **private**（只有 Session 自身可见），所以主体落在新增的私有方法
//     `Session.CommitLanguageServerSnapshotUpdate(…)` 里，外部的 `LanguageServerSnapshotUpdate.Commit(Session, Project.Snapshot)`
//     （Go 名）只做转发。这样无论 Session.cs 把字段声明成 private 还是 internal 都能编译。
//
// 12) `core.Map(programParams.Options.ConfigFileParsingDiagnostics, …)`（session.go:1372、1407）→ 私有静态
//     `MapDiagnosticResponses`：本仓现成的 `Core.Map` 对 null 输入返回**空数组**，而 Go 的 `core.Map` 返回 **nil**
//     （`ConfigFileParsingDiagnostics` 参与 JSON 的 omitempty，nil 与空数组不同）→ 这里显式保留 nil 语义。
//
// 13) 数组**元素**的可空性：Proto 侧按 Go 的 `[]*T` 声明为 `T?[]?`（`CreateProgramOptions.ProjectReferences`、
//     `ConfigFileParsingDiagnostics`），而 `project.APICreateProgramRequest.ProjectReferences` 是 `T[]?`（元素非空）。
//     从"元素可空"到"元素非空"的隐式数组转换会触发 CS8619（TreatWarningsAsErrors 下即错误）→ 在源表达式上加 `!`
//     （实测：`!` 会一并抑制数组元素可空性的告警，且不产生任何运行时开销或拷贝）；`MapDiagnosticResponses` 则把形参
//     放宽成 `DiagnosticResponse?[]?`（这是安全方向，调用点无需 `!`），元素在方法内加 `!`（Go 侧解引用 nil 元素同样 panic）。
using global::System;
using global::System.Collections.Generic;
using global::System.Globalization;
using global::System.IO;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.TsOptions;
// 注意：**不能** `using TypeScript.Project;` —— 该命名空间里有一个类也叫 `Project`（Project/Project.cs），
// using-namespace-directive 会把简单名 `Project` 绑到那个类而不是命名空间（C# 名字解析规则），
// 于是本文件里所有 `Project.Snapshot`/`Project.ID`/… 都会解析失败。故一律用 `Project.` 命名空间限定。
using RequestFileSystemApi = TypeScript.Api.RequestFileSystem.RequestFileSystem;
using TypeScript.Api.RequestFileSystem;
using Path = TypeScript.TSPath.Path;
using TscNs = TypeScript.Execute.Tsc;
using BuildNs = TypeScript.Execute.Build.Build;
using BuildOrchestratorOptions = TypeScript.Execute.Build.Options;
using static TypeScript.Ast.Ast;
using static TypeScript.Collections.Collections;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Api.Encoder.Encoder;

namespace TypeScript.Api;

public sealed partial class Session
{
    /// <summary>Go: <c>func (s *Session) handleCreateSnapshot(ctx context.Context, params *CreateSnapshotParams) (*CreateSnapshotResponse, error)</c>（session.go:1226-1260）。</summary>
    internal CreateSnapshotResponse HandleCreateSnapshot(Context ctx, CreateSnapshotParams @params)
    {
        var apiRequest = ToAPISnapshotRequest(ctx, @params);
        var openState = ReconcileSnapshotOpens(apiRequest, new SnapshotOpenState());
        var fileChanges = ToFileChangeSummary(@params.FileNotifications);
        Vfs.FS? snapshotFileSystem = null;
        if (@params.FileSystem is not null)
        {
            var fileSystem = RequestFileSystemApi.NewForUpdate(@params.FileSystem, FS(), GetCurrentDirectory(), fileChanges);
            snapshotFileSystem = fileSystem;
            apiRequest.FileSystem = fileSystem;
            apiRequest.ReplaceFileSystem = @params.FileSystem.Kind == Kind.Full;
        }

        var root = _snapshotHost.NewRootSnapshot();
        var (snapshot, error) = _snapshotHost.CloneSnapshot(ctx, root, fileChanges, apiRequest);
        root.Deref();
        if (error is not null)
        {
            snapshot.Deref();
            throw new ClientErrorException($"failed to create snapshot: {error.Message}", error);
        }

        try
        {
            ModuleResolutionError(snapshot);
        }
        catch (Exception)
        {
            snapshot.Deref();
            throw;
        }

        var response = CreateSnapshotResponse(snapshot, null, @params);
        RegisterSnapshot(snapshot, openState, snapshotFileSystem);
        return response;
    }

    /// <summary>Go: <c>func (s *Session) handleUpdateSnapshot(ctx context.Context, params *UpdateSnapshotParams) (*CreateSnapshotResponse, error)</c>（session.go:1261-1308）。</summary>
    internal CreateSnapshotResponse HandleUpdateSnapshot(Context ctx, UpdateSnapshotParams @params)
    {
        var baseSD = RetainSnapshotData(@params.Snapshot);
        try
        {
            var changes = @params.Changes ?? new CreateSnapshotParams();
            var apiRequest = ToAPISnapshotRequest(ctx, changes);
            var openState = ReconcileSnapshotOpens(apiRequest, new SnapshotOpenState
            {
                _openProjects = baseSD.OpenProjects,
                _openFiles = baseSD.OpenFiles,
            });
            var fileChanges = ToFileChangeSummary(changes.FileNotifications);
            var snapshotFileSystem = baseSD.FileSystem;
            if (changes.FileSystem is not null)
            {
                var baseFileSystem = snapshotFileSystem ?? FS();
                var fileSystem = RequestFileSystemApi.NewForUpdate(changes.FileSystem, baseFileSystem, GetCurrentDirectory(), fileChanges);
                snapshotFileSystem = fileSystem;
            }

            if (snapshotFileSystem is not null)
            {
                apiRequest.FileSystem = snapshotFileSystem;
                apiRequest.ReplaceFileSystem = changes.FileSystem is not null && changes.FileSystem.Kind == Kind.Full;
            }

            var (snapshot, error) = _snapshotHost.CloneSnapshot(ctx, baseSD.Snapshot, fileChanges, apiRequest);
            if (error is not null)
            {
                snapshot.Deref();
                throw new ClientErrorException($"failed to update snapshot: {error.Message}", error);
            }

            try
            {
                ModuleResolutionError(snapshot);
            }
            catch (Exception)
            {
                snapshot.Deref();
                throw;
            }

            var response = CreateSnapshotResponse(snapshot, baseSD.Snapshot, changes);
            RegisterSnapshot(snapshot, openState, snapshotFileSystem);
            return response;
        }
        finally
        {
            // Go: defer func() { _ = s.releaseSnapshot(params.Snapshot) }() —— 错误被显式丢弃。
            try
            {
                ReleaseSnapshot(@params.Snapshot);
            }
            catch (ClientErrorException)
            {
            }
        }
    }

    /// <summary>Go: <c>func (s *Session) toAPISnapshotRequest(ctx context.Context, changes *SnapshotRequestChangesParams) (*project.APISnapshotRequest, error)</c>（session.go:1309-1437）。</summary>
    internal Project.APISnapshotRequest ToAPISnapshotRequest(Context ctx, SnapshotRequestChangesParams changes)
    {
        var openProjects = changes.OpenProjects;
        var closeProjects = changes.CloseProjects;
        var openFiles = changes.OpenFiles;
        var closeFiles = changes.CloseFiles;
        var createPrograms = changes.CreatePrograms;
        var reconfigurePrograms = changes.ReconfigurePrograms;
        var removePrograms = changes.RemovePrograms;
        var ensurePrograms = changes.EnsurePrograms;

        var apiRequest = new Project.APISnapshotRequest();

        if (openProjects is not null)
        {
            foreach (var p in openProjects)
            {
                var configFileName = p.ToAbsoluteFileName(GetCurrentDirectory());
                if (!Project.ConfiguredProjectID.ParseConfiguredProjectID(ToPath(configFileName), out var configuredProjectID))
                {
                    throw new ClientErrorException($"invalid configured project ID: {configFileName}");
                }

                apiRequest.EnsurePrograms ??= NewSetWithSizeHint<Project.ID>(openProjects.Length);
                apiRequest.EnsurePrograms.Add(configuredProjectID.AsID());
                apiRequest.OpenProjects ??= NewSetWithSizeHint<string>(openProjects.Length);
                apiRequest.OpenProjects.Add(configFileName);
            }
        }

        if (closeProjects is not null)
        {
            foreach (var p in closeProjects)
            {
                var configPath = ToPath(p.ToAbsoluteFileName(GetCurrentDirectory()));
                apiRequest.CloseProjects ??= NewSetWithSizeHint<Path>(closeProjects.Length);
                apiRequest.CloseProjects.Add(configPath);
            }
        }

        if (openFiles is not null)
        {
            foreach (var f in openFiles)
            {
                var fileName = f.ToAbsoluteFileName(GetCurrentDirectory());
                var path = ToPath(fileName);
                if (apiRequest.OpenFiles is null || apiRequest.EnsureFiles is null)
                {
                    apiRequest.OpenFiles = new Dictionary<Path, string>(openFiles.Length);
                    apiRequest.EnsureFiles = new Dictionary<Path, string>(openFiles.Length);
                }

                if (!apiRequest.OpenFiles.ContainsKey(path))
                {
                    apiRequest.OpenFiles[path] = fileName;
                    apiRequest.EnsureFiles[path] = fileName;
                }
            }
        }

        if (closeFiles is not null)
        {
            foreach (var f in closeFiles)
            {
                var path = ToPath(f.ToURI(GetCurrentDirectory()).FileName());
                apiRequest.CloseFiles ??= NewSetWithSizeHint<Path>(closeFiles.Length);
                apiRequest.CloseFiles.Add(path);
            }
        }

        apiRequest.CreatePrograms = new Project.APICreateProgramRequest[createPrograms?.Length ?? 0];
        if (createPrograms is not null)
        {
            for (var i = 0; i < createPrograms.Length; i++)
            {
                var programParams = createPrograms[i];
                if (programParams is null)
                {
                    throw new ClientErrorException($"createPrograms[{i}] must not be null");
                }

                var rootFileNames = new string[programParams.RootFiles.Length];
                for (var j = 0; j < programParams.RootFiles.Length; j++)
                {
                    rootFileNames[j] = programParams.RootFiles[j].ToAbsoluteFileName(GetCurrentDirectory());
                }

                var request = new Project.APICreateProgramRequest
                {
                    RootFileNames = rootFileNames,
                    CompilerOptions = programParams.CompilerOptions,
                };
                if (programParams.Options is not null)
                {
                    request.ProjectReferences = programParams.Options.ProjectReferences!;
                    request.ConfigFileParsingDiagnostics = MapDiagnosticResponses(programParams.Options.ConfigFileParsingDiagnostics);
                    request.ModuleResolverFactory = ModuleResolverFactory(ctx, programParams.Options);
                    request.ModuleResolverID = programParams.Options.ModuleResolver.Value;
                }

                apiRequest.CreatePrograms[i] = request;
            }
        }

        apiRequest.ReconfigurePrograms = new Project.APIReconfigureProgramRequest[reconfigurePrograms?.Length ?? 0];
        var reconfiguredProgramIDs = new Set<Project.SyntheticProjectID>();
        if (reconfigurePrograms is not null)
        {
            for (var i = 0; i < reconfigurePrograms.Length; i++)
            {
                var programParams = reconfigurePrograms[i];
                if (programParams is null)
                {
                    throw new ClientErrorException($"reconfigurePrograms[{i}] must not be null");
                }

                if (!Project.SyntheticProjectID.ParseSyntheticProjectID(programParams.Id.Value, out var programID))
                {
                    throw new ClientErrorException($"invalid synthetic project handle: {programParams.Id.Value}");
                }

                if (reconfiguredProgramIDs.Has(programID))
                {
                    throw new ClientErrorException($"synthetic program reconfigured more than once: {programID.Value}");
                }

                reconfiguredProgramIDs.Add(programID);
                var rootFileNames = new string[programParams.RootFiles.Length];
                for (var j = 0; j < programParams.RootFiles.Length; j++)
                {
                    rootFileNames[j] = programParams.RootFiles[j].ToAbsoluteFileName(GetCurrentDirectory());
                }

                var request = new Project.APIReconfigureProgramRequest
                {
                    ProgramID = programID,
                    RootFileNames = rootFileNames,
                    CompilerOptions = programParams.CompilerOptions,
                };
                if (programParams.Options is not null)
                {
                    request.ProjectReferences = programParams.Options.ProjectReferences!;
                    request.ConfigFileParsingDiagnostics = MapDiagnosticResponses(programParams.Options.ConfigFileParsingDiagnostics);
                    request.ModuleResolverFactory = ModuleResolverFactory(ctx, programParams.Options);
                    request.ModuleResolverID = programParams.Options.ModuleResolver.Value;
                }

                apiRequest.ReconfigurePrograms[i] = request;
            }
        }

        if (removePrograms is { Length: > 0 })
        {
            var removeProgramSet = NewSetWithSizeHint<Project.SyntheticProjectID>(removePrograms.Length);
            apiRequest.RemovePrograms = removeProgramSet;
            foreach (var programID in removePrograms)
            {
                if (reconfiguredProgramIDs.Has(programID))
                {
                    throw new ClientErrorException($"synthetic program cannot be reconfigured and removed: {programID.Value}");
                }

                removeProgramSet.Add(programID);
            }
        }

        if (ensurePrograms is not null)
        {
            apiRequest.EnsureAllPrograms = ensurePrograms.All;
            var programs = ensurePrograms.Projects;
            if (programs is { Length: > 0 })
            {
                var ensureSet = apiRequest.EnsurePrograms ??= NewSetWithSizeHint<Project.ID>(programs.Length);
                foreach (var program in programs)
                {
                    ensureSet.Add(program);
                }
            }
        }

        return apiRequest;
    }

    /// <summary>Go: <c>core.Map(programParams.Options.ConfigFileParsingDiagnostics, func(d *DiagnosticResponse) *ast.Diagnostic { return d.ToDiagnostic() })</c>（session.go:1372、1407 两处内联调用）：C# 的 <c>Core.Map</c> 对 null 输入返回空数组（不保留 nil），而 Go 的 <c>core.Map</c> 返回 nil，故这里显式保留 nil 语义。</summary>
    internal static Ast.Diagnostic[]? MapDiagnosticResponses(DiagnosticResponse?[]? responses)
    {
        if (responses is null)
        {
            return null;
        }

        var result = new Ast.Diagnostic[responses.Length];
        for (var i = 0; i < responses.Length; i++)
        {
            result[i] = responses[i]!.ToDiagnostic();
        }

        return result;
    }

    /// <summary>Go: <c>func (s *Session) toLanguageServerSnapshotUpdate(ctx context.Context, changes *SnapshotRequestChangesParams) (*languageServerSnapshotUpdate, error)</c>（session.go:1443-1478）。</summary>
    internal LanguageServerSnapshotUpdate ToLanguageServerSnapshotUpdate(Context ctx, SnapshotRequestChangesParams changes)
    {
        var apiRequest = ToAPISnapshotRequest(ctx, changes);
        var update = new LanguageServerSnapshotUpdate
        {
            _request = apiRequest,
            _openState = ReconcileSnapshotOpens(apiRequest, new SnapshotOpenState
            {
                _openProjects = _openProjects,
                _openFiles = _openFiles,
            }),
        };

        if (apiRequest.RemovePrograms is not null)
        {
            // 见文件头第 5 条：先复制 keys 再删（Go 允许 range 时 delete）。
            foreach (var programID in new List<Project.SyntheticProjectID>(apiRequest.RemovePrograms.Keys()))
            {
                if (!_createdPrograms.Has(programID))
                {
                    apiRequest.RemovePrograms.Delete(programID);
                }
            }
        }

        if (apiRequest.ReconfigurePrograms is not null)
        {
            foreach (var reconfigure in apiRequest.ReconfigurePrograms)
            {
                if (!_createdPrograms.Has(reconfigure.ProgramID))
                {
                    throw new ClientErrorException($"synthetic program is not owned by this API session: {reconfigure.ProgramID.Value}");
                }
            }
        }

        return update;
    }

    /// <summary>Go: <c>func (u *languageServerSnapshotUpdate) commit(s *Session, snapshot *project.Snapshot)</c>（session.go:1469-1483）。
    /// 主体放在 Session 内（见文件头第 11 条）：Go 的 <c>commit</c> 写 Session 的未导出字段，C# 里它们是 private，
    /// 只有 Session 自身（含各 partial 部分与嵌套类）能访问，外部的 <c>LanguageServerSnapshotUpdate.Commit</c> 只是转发。</summary>
    internal void CommitLanguageServerSnapshotUpdate(LanguageServerSnapshotUpdate u, Project.Snapshot snapshot)
    {
        _openProjects = u._openState._openProjects.Clone();
        _openFiles = u._openState._openFiles.Clone();
        if (u._request.RemovePrograms is not null)
        {
            foreach (var programID in u._request.RemovePrograms.Keys())
            {
                _createdPrograms.Delete(programID);
            }
        }

        foreach (var program in snapshot.CreatedPrograms())
        {
            if (!program.ID().TrySynthetic(out var programID))
            {
                Debug.Fail($"created program has non-synthetic project ID: {program.ID().Value}");
            }

            _createdPrograms.Add(programID);
        }
    }

    /// <summary>Go: <c>func (s *Session) reconcileSnapshotOpens(apiRequest *project.APISnapshotRequest, base snapshotOpenState) snapshotOpenState</c>（session.go:1489-1525）。</summary>
    internal SnapshotOpenState ReconcileSnapshotOpens(Project.APISnapshotRequest apiRequest, SnapshotOpenState @base)
    {
        var state = new SnapshotOpenState
        {
            _openProjects = @base._openProjects.Clone(),
            _openFiles = @base._openFiles.Clone(),
        };

        if (apiRequest.CloseProjects is not null)
        {
            foreach (var path in new List<Path>(apiRequest.CloseProjects.Keys()))
            {
                if (state._openProjects.Has(path))
                {
                    state._openProjects.Delete(path);
                }
                else
                {
                    apiRequest.CloseProjects.Delete(path);
                }
            }
        }

        if (apiRequest.OpenProjects is not null)
        {
            foreach (var configFileName in new List<string>(apiRequest.OpenProjects.Keys()))
            {
                var path = ToPath(configFileName);
                if (state._openProjects.Has(path))
                {
                    apiRequest.OpenProjects.Delete(configFileName);
                }
                else
                {
                    state._openProjects.Add(path);
                }
            }
        }

        if (apiRequest.CloseFiles is not null)
        {
            foreach (var path in new List<Path>(apiRequest.CloseFiles.Keys()))
            {
                if (state._openFiles.Has(path))
                {
                    state._openFiles.Delete(path);
                }
                else
                {
                    apiRequest.CloseFiles.Delete(path);
                }
            }
        }

        if (apiRequest.OpenFiles is not null)
        {
            foreach (var path in new List<Path>(apiRequest.OpenFiles.Keys))
            {
                if (state._openFiles.Has(path))
                {
                    apiRequest.OpenFiles.Remove(path);
                }
                else
                {
                    state._openFiles.Add(path);
                }
            }
        }

        return state;
    }

    /// <summary>Go: <c>func (s *Session) registerSnapshot(snapshot *project.Snapshot, openState snapshotOpenState, fileSystem vfs.FS)</c>（session.go:1526-1552）。</summary>
    internal void RegisterSnapshot(Project.Snapshot snapshot, SnapshotOpenState openState, Vfs.FS? fileSystem)
    {
        // If the same snapshot ID is returned (no changes), we increment the ref count
        // so each client-side Snapshot can be disposed independently.
        var handle = SnapshotHandle(snapshot);
        _snapshotsMu.EnterWriteLock();
        try
        {
            if (_snapshots.TryGetValue(handle, out var sd))
            {
                // Same snapshot already stored — release the caller's ref since
                // the stored snapshot already has one, and bump the API refcount.
                snapshot.Deref();
                sd.RefCount++;
            }
            else
            {
                sd = new SnapshotData
                {
                    Snapshot = snapshot,
                    FileSystem = fileSystem!,
                    RefCount = 1,
                    OpenProjects = openState._openProjects.Clone(),
                    OpenFiles = openState._openFiles.Clone(),
                    SymbolRegistry = new(),
                    SymbolCanonicalProjects = new(),
                    ProjectRegistries = new(),
                };
                _snapshots[handle] = sd;
            }
        }
        finally
        {
            _snapshotsMu.ExitWriteLock();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetCurrentLanguageServerSnapshot(ctx context.Context, params *GetCurrentLanguageServerSnapshotParams) (*CreateSnapshotResponse, error)</c>（session.go:1553-1591）。</summary>
    /// <remarks>测试可访问性：Go 里 `handleGetCurrentLanguageServerSnapshot` 是包内可见，C# 用 `internal` +
    /// `InternalsVisibleTo("TypeScript.Compiler.Tests")` 表达同一边界（契约 §4）。</remarks>
    internal CreateSnapshotResponse HandleGetCurrentLanguageServerSnapshot(Context ctx, GetCurrentLanguageServerSnapshotParams @params)
    {
        var projectSession = _projectSession;
        if (projectSession is null)
        {
            throw new ClientErrorException("getCurrentLanguageServerSnapshot requires an LSP-connected API session");
        }

        Project.Snapshot? baseSnapshot = null;
        var retainedBase = false;
        try
        {
            if (@params.BaseSnapshot.Value != 0)
            {
                var baseSD = RetainSnapshotData(@params.BaseSnapshot);
                retainedBase = true;
                baseSnapshot = baseSD.Snapshot;
            }

            lock (_languageServerUpdateMu)
            {
                var changes = @params.Changes ?? new LanguageServerSnapshotChanges();
                var update = ToLanguageServerSnapshotUpdate(ctx, changes);

                Project.Snapshot snapshot;
                try
                {
                    snapshot = projectSession.APIUpdate(ctx, new Project.FileChangeSummary(), update._request);
                }
                catch (Exception e)
                {
                    throw new ClientErrorException($"failed to update language server snapshot: {e.Message}", e);
                }

                update.Commit(this, snapshot);
                var response = CreateSnapshotResponse(snapshot, baseSnapshot, changes);
                RegisterSnapshot(snapshot, new SnapshotOpenState
                {
                    _openProjects = _openProjects,
                    _openFiles = _openFiles,
                }, null);
                return response;
            }
        }
        finally
        {
            if (retainedBase)
            {
                // Go: defer func() { _ = s.releaseSnapshot(params.BaseSnapshot) }() —— 错误被显式丢弃。
                try
                {
                    ReleaseSnapshot(@params.BaseSnapshot);
                }
                catch (ClientErrorException)
                {
                }
            }
        }
    }

    /// <summary>Go: <c>func (s *Session) handleRelease(ctx context.Context, params *ReleaseParams) (any, error)</c>（session.go:1592-1605）。</summary>
    internal object? HandleRelease(Context ctx, ReleaseParams? @params)
    {
        if (@params is null || @params.Snapshot.Value == 0)
        {
            throw new ClientErrorException("empty handle");
        }

        ReleaseSnapshot(@params.Snapshot);
        return true;
    }

    /// <summary>Go: <c>func (s *Session) handleGetDefaultProjectForFile(ctx context.Context, params *GetDefaultProjectForFileParams) (*ProjectResponse, error)</c>（session.go:1606-1620，@gen-proto-nullable）。</summary>
    internal ProjectResponse? HandleGetDefaultProjectForFile(Context ctx, GetDefaultProjectForFileParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);

        var uri = @params.File.ToURI(GetCurrentDirectory());
        var proj = sd.Snapshot.GetDefaultProject(uri);
        if (proj is null)
        {
            return null;
        }

        return Api.NewProjectResponse(proj);
    }

    /// <summary>Go: <c>func (s *Session) handleCreateBuildOrchestrator(ctx context.Context, params *CreateBuildOrchestratorParams) (*CreateBuildOrchestratorResponse, error)</c>（session.go:1621-1641）。</summary>
    internal CreateBuildOrchestratorResponse HandleCreateBuildOrchestrator(Context ctx, CreateBuildOrchestratorParams @params)
    {
        var buildSys = GetBuildSys(@params);
        var command = ParseBuildCommandLine(@params.RootNames, buildSys);
        var response = new CreateBuildOrchestratorResponse();
        if (@params.CompilerOptions is not null)
        {
            command.CompilerOptions = @params.CompilerOptions;
        }

        if (@params.BuildOptions is not null)
        {
            command.BuildOptions = @params.BuildOptions;
        }

        var orchestrator = BuildNs.NewOrchestrator(new BuildOrchestratorOptions
        {
            Sys = buildSys,
            Command = command,
        });
        response.BuildOrchestratorID = Api.NewBuildOrchestratorID();
        lock (_buildMu)
        {
            _buildOrchestrators[response.BuildOrchestratorID] = orchestrator;
        }

        return response;
    }

    /// <summary>Go: <c>func (s *Session) handleDisposeBuildOrchestrator(ctx context.Context, params *DisposeBuildOrchestratorParams) (any, error)</c>（session.go:1642-1651）。</summary>
    internal object? HandleDisposeBuildOrchestrator(Context ctx, DisposeBuildOrchestratorParams @params)
    {
        lock (_buildMu)
        {
            if (!_buildOrchestrators.ContainsKey(@params.BuildOrchestratorID))
            {
                throw new InvalidOperationException("build orchestrator not found while disposing");
            }

            _buildOrchestrators.Remove(@params.BuildOrchestratorID);
        }

        return true;
    }

    /// <summary>Go: <c>func (s *Session) handleBuild(ctx context.Context, params *BuildParams) (*BuildResponse, error)</c>（session.go:1652-1666）。</summary>
    internal BuildResponse HandleBuild(Context ctx, BuildParams @params)
    {
        lock (_buildMu)
        {
            if (!_buildOrchestrators.TryGetValue(@params.BuildOrchestratorID, out var orchestrator))
            {
                throw new InvalidOperationException($"build orchestrator not found while building {@params.Project}");
            }

            var result = orchestrator.Build(ctx, @params.Project);

            return new BuildResponse
            {
                Status = result.Result.Status,
                Diagnostics = Api.NewDiagnosticResponses([.. result.Errors]),
                Statistics = result.Statistics,
            };
        }
    }

    /// <summary>Go: <c>func (s *Session) handleBuildReferences(ctx context.Context, params *BuildParams) (*BuildResponse, error)</c>（session.go:1667-1681）。</summary>
    internal BuildResponse HandleBuildReferences(Context ctx, BuildParams @params)
    {
        lock (_buildMu)
        {
            if (!_buildOrchestrators.TryGetValue(@params.BuildOrchestratorID, out var orchestrator))
            {
                throw new InvalidOperationException($"build orchestrator not found for building references for {@params.Project}");
            }

            var result = orchestrator.BuildReferences(ctx, @params.Project);

            return new BuildResponse
            {
                Status = result.Result.Status,
                Diagnostics = Api.NewDiagnosticResponses([.. result.Errors]),
                Statistics = result.Statistics,
            };
        }
    }

    /// <summary>Go: <c>func (s *Session) handleCleanBuild(ctx context.Context, params *CleanBuildParams) (*CleanBuildResponse, error)</c>（session.go:1682-1696）。</summary>
    internal CleanBuildResponse HandleCleanBuild(Context ctx, CleanBuildParams @params)
    {
        lock (_buildMu)
        {
            if (!_buildOrchestrators.TryGetValue(@params.BuildOrchestratorID, out var orchestrator))
            {
                throw new InvalidOperationException($"build orchestrator not found while cleaning {@params.Project}");
            }

            var result = orchestrator.Clean(@params.Project);
            return new CleanBuildResponse
            {
                Status = result.Result.Status,
                Diagnostics = Api.NewDiagnosticResponses([.. result.Errors]),
                Statistics = result.Statistics,
                FilesDeleted = [.. result.FilesToDelete],
            };
        }
    }

    /// <summary>Go: <c>func (s *Session) handleCleanReferences(ctx context.Context, params *CleanBuildParams) (*CleanBuildResponse, error)</c>（session.go:1697-1711）。</summary>
    internal CleanBuildResponse HandleCleanReferences(Context ctx, CleanBuildParams @params)
    {
        lock (_buildMu)
        {
            if (!_buildOrchestrators.TryGetValue(@params.BuildOrchestratorID, out var orchestrator))
            {
                throw new InvalidOperationException($"build orchestrator not found while cleaning references for {@params.Project}");
            }

            var result = orchestrator.CleanReferences(@params.Project);
            return new CleanBuildResponse
            {
                Status = result.Result.Status,
                Diagnostics = Api.NewDiagnosticResponses([.. result.Errors]),
                Statistics = result.Statistics,
                FilesDeleted = [.. result.FilesToDelete],
            };
        }
    }

    /// <summary>Go: <c>func (s *Session) getBuildSys(params *CreateBuildOrchestratorParams) tsc.System</c>（session.go:1712-1723）。</summary>
    internal TscNs.System GetBuildSys(CreateBuildOrchestratorParams @params)
    {
        var currentDirectory = @params.Cwd;
        if (currentDirectory.Length == 0)
        {
            currentDirectory = GetCurrentDirectory();
        }

        return new ApiBuildSystem(this, currentDirectory, DateTime.Now);
    }

    /// <summary>Go: <c>func computeSnapshotChanges(prev *project.Snapshot, next *project.Snapshot) *SnapshotChanges</c>（session.go:4560-4608）。</summary>
    internal static SnapshotChanges ComputeSnapshotChanges(Project.Snapshot prev, Project.Snapshot next)
    {
        var prevProjects = prev.ProjectCollection.ProjectsByID();
        var nextProjects = next.ProjectCollection.ProjectsByID();

        var changes = new SnapshotChanges();
        var changedProjects = new Dictionary<Project.ID, ProjectFileChanges?>();
        var removedProjects = new List<Project.ID>();

        DiffOrderedMaps(
            prevProjects,
            nextProjects,
            // onAdded: new project — nothing to retain from previous snapshot.
            static (_, _) => { },
            // onRemoved: project removed entirely.
            (_, oldProj) =>
            {
                if (oldProj is not null)
                {
                    removedProjects.Add(oldProj.ID());
                }
            },
            // onModified: project changed, diff its files.
            (_, oldProj, newProj) =>
            {
                if (oldProj is null || newProj is null)
                {
                    return;
                }

                if (ReferenceEquals(oldProj.GetProgram(), newProj.GetProgram()))
                {
                    return;
                }

                Dictionary<Path, Ast.SourceFile>? oldFiles = null;
                if (oldProj.GetProgram() is { } oldProgram)
                {
                    oldFiles = oldProgram.FilesByPath();
                }

                Dictionary<Path, Ast.SourceFile>? newFiles = null;
                if (newProj.GetProgram() is { } newProgram)
                {
                    newFiles = newProgram.FilesByPath();
                }

                var projectChanges = new ProjectFileChanges();
                var deletedFiles = new List<Path>();
                var changedFiles = new List<Path>();
                Core.Core.DiffMaps(
                    oldFiles,
                    newFiles,
                    null, // onAdded: new file in project, not a change.
                    (path, _) => deletedFiles.Add(path),
                    (path, _, _) => changedFiles.Add(path));
                if (changedFiles.Count > 0)
                {
                    projectChanges.ChangedFiles = [.. changedFiles];
                }

                if (deletedFiles.Count > 0)
                {
                    projectChanges.DeletedFiles = [.. deletedFiles];
                }

                if (changedFiles.Count > 0 || deletedFiles.Count > 0)
                {
                    changedProjects[newProj.ID()] = projectChanges;
                }
            });

        if (changedProjects.Count > 0)
        {
            changes.ChangedProjects = changedProjects;
        }

        if (removedProjects.Count > 0)
        {
            changes.RemovedProjects = [.. removedProjects];
        }

        return changes;
    }

    /// <summary>Go: <c>func (s *Session) createSnapshotResponse(snapshot *project.Snapshot, base *project.Snapshot, request *SnapshotRequestChangesParams) *CreateSnapshotResponse</c>（session.go:4609-4644）。</summary>
    internal CreateSnapshotResponse CreateSnapshotResponse(Project.Snapshot snapshot, Project.Snapshot? @base, SnapshotRequestChangesParams request)
    {
        var operation = CreateSnapshotOperationResponse(snapshot, request);
        if (@base is null)
        {
            var projects = snapshot.ProjectCollection.Projects();
            var projectResponses = new List<ProjectResponse>(projects.Length);
            foreach (var proj in projects)
            {
                if (proj.CommandLine is not null)
                {
                    projectResponses.Add(Api.NewProjectResponse(proj));
                }
            }

            return new CreateSnapshotResponse
            {
                Snapshot = SnapshotHandle(snapshot),
                Projects = [.. projectResponses],
                Operation = operation,
            };
        }

        var responses = new List<ProjectResponse>();
        DiffOrderedMaps(
            @base.ProjectCollection.ProjectsByID(),
            snapshot.ProjectCollection.ProjectsByID(),
            (_, proj) =>
            {
                if (proj is not null && proj.CommandLine is not null)
                {
                    responses.Add(Api.NewProjectResponse(proj));
                }
            },
            static (_, _) => { },
            (_, oldProj, newProj) =>
            {
                if (!ReferenceEquals(oldProj, newProj) && newProj is not null && newProj.CommandLine is not null)
                {
                    responses.Add(Api.NewProjectResponse(newProj));
                }
            });
        return new CreateSnapshotResponse
        {
            Snapshot = SnapshotHandle(snapshot),
            Projects = [.. responses],
            Changes = ComputeSnapshotChanges(@base, snapshot),
            Operation = operation,
        };
    }

    /// <summary>Go: <c>func (s *Session) createSnapshotOperationResponse(snapshot *project.Snapshot, request *SnapshotRequestChangesParams) *SnapshotOperationResponse</c>（session.go:4645-4682）。</summary>
    internal SnapshotOperationResponse CreateSnapshotOperationResponse(
        Project.Snapshot snapshot,
        SnapshotRequestChangesParams? request)
    {
        var operation = new SnapshotOperationResponse();
        if (request is null)
        {
            return operation;
        }

        var createPrograms = request.CreatePrograms;
        if (createPrograms is not null)
        {
            var createdPrograms = snapshot.CreatedPrograms();
            if (createdPrograms.Length != createPrograms.Length)
            {
                Debug.Fail("created program result count does not match request");
            }

            var results = new Project.SyntheticProjectID[createdPrograms.Length];
            for (var i = 0; i < createdPrograms.Length; i++)
            {
                if (!createdPrograms[i].ID().TrySynthetic(out var programID))
                {
                    Debug.Fail("created program has non-synthetic project ID");
                }

                results[i] = programID;
            }

            operation.CreatedPrograms = results;
        }

        var openFiles = request.OpenFiles;
        if (openFiles is not null)
        {
            var results = new OpenedFileOperationResult[openFiles.Length];
            for (var i = 0; i < openFiles.Length; i++)
            {
                var file = openFiles[i];
                var proj = snapshot.GetDefaultProject(file.ToURI(GetCurrentDirectory()));
                if (proj is null)
                {
                    Debug.Fail("no project found for opened file " + file.ToAbsoluteFileName(GetCurrentDirectory()));
                }

                results[i] = new OpenedFileOperationResult { Project = proj!.ID() };
            }

            operation.OpenedFiles = results;
        }

        return operation;
    }

    /// <summary>Go: <c>type apiBuildSystem struct { session *Session; currentDirectory string; start time.Time }</c>（session.go:1725-1729）
    /// 及其全部方法（session.go:1731-1747）。嵌套在 <see cref="Session"/> 内以贴近 Go 的"同包可见"（见文件头第 4 条）。</summary>
    internal sealed class ApiBuildSystem : TscNs.System
    {
        internal readonly Session _session;
        internal readonly string _currentDirectory;
        internal readonly DateTime _start;

        /// <summary>Go: <c>apiBuildSystem{session: s, currentDirectory: …, start: time.Now()}</c>（session.go:1712-1723 的结构体字面量）。</summary>
        internal ApiBuildSystem(Session session, string currentDirectory, DateTime start)
        {
            _session = session;
            _currentDirectory = currentDirectory;
            _start = start;
        }

        /// <summary>Go: <c>func (s *apiBuildSystem) Writer() io.Writer</c>（session.go:1731）：<c>io.Discard</c> → <c>TextWriter.Null</c>。</summary>
        public TextWriter Writer() => TextWriter.Null;

        /// <summary>Go: <c>func (s *apiBuildSystem) ErrorWriter() io.Writer</c>（session.go:1732）。</summary>
        public TextWriter ErrorWriter() => TextWriter.Null;

        /// <summary>Go: <c>func (s *apiBuildSystem) FS() vfs.FS</c>（session.go:1733）。</summary>
        public Vfs.FS FS() => _session._snapshotHost.FS();

        /// <summary>Go: <c>func (s *apiBuildSystem) DefaultLibraryPath() string</c>（session.go:1734）。</summary>
        public string DefaultLibraryPath() => _session.DefaultLibraryPath();

        /// <summary>Go: <c>func (s *apiBuildSystem) GetCurrentDirectory() string</c>（session.go:1735）。</summary>
        public string GetCurrentDirectory() => _currentDirectory;

        /// <summary>Go: <c>func (s *apiBuildSystem) WriteOutputIsTTY() bool</c>（session.go:1736）。</summary>
        public bool WriteOutputIsTTY() => false;

        /// <summary>Go: <c>func (s *apiBuildSystem) GetWidthOfTerminal() int</c>（session.go:1737）。</summary>
        public int GetWidthOfTerminal() => 0;

        /// <summary>Go: <c>func (s *apiBuildSystem) GetEnvironmentVariable(name string) (string, bool)</c>（session.go:1738-1740，契约 §3.2 的 <c>(T, bool)</c> → <c>TryX</c>）。</summary>
        public bool TryGetEnvironmentVariable(string name, out string value)
        {
            value = "";
            return false;
        }

        /// <summary>Go: <c>func (s *apiBuildSystem) Spawn(command []string, dir string, stderr io.Writer) (io.ReadWriteCloser, error)</c>（session.go:1742-1744）：Go 的 <c>errors.New</c> → <c>InvalidOperationException</c>（见文件头第 1 条）。</summary>
        public Stream Spawn(string[] command, string dir, Stream stderr)
        {
            throw new InvalidOperationException("spawning processes is not supported by the API build orchestrator");
        }

        /// <summary>Go: <c>func (s *apiBuildSystem) Now() time.Time</c>（session.go:1745）。</summary>
        public DateTime Now() => DateTime.Now;

        /// <summary>Go: <c>func (s *apiBuildSystem) SinceStart() time.Duration</c>（session.go:1746）。</summary>
        public TimeSpan SinceStart() => DateTime.Now - _start;
    }
}

/// <summary>Go: <c>type languageServerSnapshotUpdate struct</c>（session.go:1438-1441）。</summary>
internal sealed class LanguageServerSnapshotUpdate
{
    /// <summary>Go: <c>request *project.APISnapshotRequest</c>。</summary>
    internal Project.APISnapshotRequest _request = null!;

    /// <summary>Go: <c>openState snapshotOpenState</c>。</summary>
    internal SnapshotOpenState _openState = null!;

    /// <summary>Go: <c>func (u *languageServerSnapshotUpdate) commit(s *Session, snapshot *project.Snapshot)</c>（session.go:1469-1483）：主体落在 Session 的 <c>CommitLanguageServerSnapshotUpdate</c>（见文件头第 11 条）。</summary>
    internal void Commit(Session s, Project.Snapshot snapshot) => s.CommitLanguageServerSnapshotUpdate(this, snapshot);
}

/// <summary>Go: <c>type snapshotOpenState struct { openProjects collections.Set[tspath.Path]; openFiles collections.Set[tspath.Path] }</c>（session.go:1484-1487）。
/// Go 的零值 Set（内部 map 为 nil）在 C# 里是空 Set（见文件头第 3 条）。</summary>
internal sealed class SnapshotOpenState
{
    /// <summary>Go: <c>openProjects collections.Set[tspath.Path]</c>。</summary>
    internal Set<Path> _openProjects = new();

    /// <summary>Go: <c>openFiles collections.Set[tspath.Path]</c>。</summary>
    internal Set<Path> _openFiles = new();
}

/// <summary>Go: <c>func (sd *snapshotData) resolveNodeHandle(program *compiler.Program, handle NodeHandle) (*ast.Node, error)</c>（session.go:4523-4559）。
/// `snapshotData` 由 Session.cs 声明为 partial（见文件头：本切片只补这一个方法）。</summary>
internal sealed partial class SnapshotData
{
    /// <summary>Go: <c>func (sd *snapshotData) resolveNodeHandle(program *compiler.Program, handle NodeHandle) (*ast.Node, error)</c>（session.go:4523-4559）：
    /// 句柄格式 "index.kind.path"，只用到 index 与 path（kind 仅作信息）；失败抛 <see cref="ClientErrorException"/>。</summary>
    internal Ast.Node ResolveNodeHandle(TypeScript.Compiler.Program program, NodeHandle handle)
    {
        string s = handle;
        // Format: "index.kind.path" — we need index and path, kind is informational only.
        var firstDot = s.IndexOf('.');
        if (firstDot == -1)
        {
            throw new ClientErrorException($"invalid node handle \"{handle.Value}\"");
        }

        var secondDot = s.IndexOf('.', firstDot + 1);
        if (secondDot == -1)
        {
            throw new ClientErrorException($"invalid node handle \"{handle.Value}\"");
        }

        if (!uint.TryParse(s[..firstDot], NumberStyles.None, CultureInfo.InvariantCulture, out var idx))
        {
            throw new ClientErrorException($"invalid node handle \"{handle.Value}\"");
        }

        var path = new Path(s[(secondDot + 1)..]);
        var sourceFile = program.GetSourceFileByPath(path);
        if (sourceFile is null)
        {
            throw new ClientErrorException($"node handle \"{handle.Value}\" could not be resolved (file may not be loaded or handle may be stale)");
        }

        var table = GetNodeIndexTable(sourceFile);
        if (idx < (ulong)table.Nodes.Length)
        {
            var node = table.Nodes[idx];
            if (node is not null)
            {
                return node;
            }
        }

        throw new ClientErrorException($"node handle \"{handle.Value}\" could not be resolved (file may not be loaded or handle may be stale)");
    }
}
