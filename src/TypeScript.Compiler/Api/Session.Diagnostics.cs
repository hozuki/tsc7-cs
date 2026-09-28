// ported from: internal/api/session.go:4683-5118
//   （Close / releaseLanguageServerRefs / formatSessionID / toPath / toFileChangeSummary / getDiagnostics /
//    handleGet{Syntactic,Bind,Semantic,Suggestion,Declaration}Diagnostics /
//    handleGetConfigFileParsingDiagnostics / handleGetProgramDiagnostics / handleGetGlobalDiagnostics /
//    resolveOptionalSourceFile / handleGetReferencesToSymbolInFile / handleGetSignatureUsages /
//    handleGetCompletionsAtPosition / handleGetReferencedSymbolsForNode）
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `sync.Once`（`Close` 的 `s.closeOnce.Do(...)`）→ **标志 `_closeOnce` + `_closeMu`（`System.Threading.Lock`）双检**
//    （本仓约定，先例 `Project/SnapshotHost.cs` 文件头第 3 条 `SourceFileLease.releaseOnce`）。
//    可观察差异：Go 的 `Once.Do` 会让**并发**的第二个调用者阻塞到第一个执行完；C# 侧第二个调用者立即返回。
//    两者都保证释放路径恰好执行一次（"恰好一次"才是 API 契约，阻塞与否不在契约内）。
//
// 2) Go 的 `(T, error)` → 正常路径返回 T、失败抛异常（docs/architecture.md §3.2）。因此 Go 里
//    **吞掉错误**的地方（`releaseLanguageServerRefs` 里 `if err != nil { return }`）在 C# 落成
//    `try { … } catch (Exception) { return; }`：异常对象本身不丢失，只是本函数按 Go 的语义忽略它。
//
// 3) `getDiagnostics` 的 `getter func(*compiler.Program, context.Context, *ast.SourceFile) []*ast.Diagnostic`
//    是 Go 的**具名函数类型** → C# 用 BCL 的 `Func<Compiler.Program, Context, SourceFile?, Diagnostic[]>`。
//    理由：C# 没有 Go 的包级 func 类型；另建 delegate 类型会与并行分片撞名，而 `Func<>` 是现成的等价物
//    （AOT 安全、无反射），Go 的 `(*compiler.Program).GetXxxDiagnostics` 方法值直接靠方法组转换传入。
//
// 4) 可空性逐点对齐 Go 的 nil：`getDiagnostics` 的返回形状来自 `NewDiagnosticResponses`，
//    Go 在 `len(diags) == 0` 时返回 nil ⇒ C# `DiagnosticResponse[]?`（null 即 Go 的 nil）。
//    omitempty 的 `params.Files` ⇒ `DocumentIdentifier[]?`；`SnapshotChanges.RemovedProjects`、
//    `ProjectFileChanges.ChangedFiles/DeletedFiles` 同理（Go 空即 nil ⇒ C# null）。
//    `handleGetReferencedSymbolsForNode` 的 `var result []ReferencedSymbolEntry` Go 是 nil（JSON `null`），
//    C# 用 `Count == 0 ? null : …` 显式保留这一区别（与相邻 `make(…, 0, n)` 的 `[]` 不同）。
//
// 5) `formatSessionID` 是 Go 的**包级未导出函数**（session.go:4740-4743，被 session.go:497 的 `newSession` 调用）
//    → 本 partial 的 `private static string FormatSessionID(ulong id)`：partial 类的成员在**所有**分部文件之间互通，
//    所以 `Api/Session.cs`（SessionCore）可以直接裸名调用它，无需共享 static 容器。
//    `fmt.Sprintf("api-session-%d", id)` → `ToString(CultureInfo.InvariantCulture)`：Go 的 `%d` 恒为 ASCII 数字，
//    而 C# 的插值/`ToString()` 走当前区域设置（部分区域会输出非 ASCII 数字）。
//
// 6) `tspath.ToPath(fileName, basePath, useCaseSensitiveFileNames)` → `TSPath.TSPath.ToPath(...)`：
//    `TSPath` 既是命名空间又是静态类（contract §2），只有全限定 `TSPath.TSPath` 才无歧义。
//    同理 `collections.NewSetWithSizeHint` → 裸名 `NewSetWithSizeHint`（`using static
//    TypeScript.Collections.Collections;`），因为 `Collections` 这个简单名在这里绑的是**命名空间**。
//
// 7) Go 的 `sd.resolveNodeHandle` / `checkerSetup.resolveSymbolHandle` **每条失败路径都返回 `(nil, err)`**，
//    成功路径永不返回 nil，所以 `handle*` 里 `if node == nil { return nil, nil }`、
//    `if symbol == nil { return nil, nil }` 是**死代码**（Go 里也不可能命中）；C# 的错误已由异常承载，
//    这些分支删除，行为不变。
//
// 8) **本文件不含 session.go:4560-4682**（`computeSnapshotChanges` / `createSnapshotResponse` /
//    `createSnapshotOperationResponse`）：同批移植的 `Api/Session.Snapshot.cs` 已逐行承载这三个
//    （它按 Go 的两个调用点把 `request` 展平成 `CreateSnapshotParams` / `LanguageServerSnapshotChanges`
//    两个入口 + 一个 `CreateSnapshotResponseCore`），本文件再定义 `ComputeSnapshotChanges` 会 CS0111 重复成员。
//    因此本文件也不引用 `SnapshotHandle` / `NewProjectResponse`（那两个是它们的依赖）。
//
// 9) Go 的 `defer setup.done()` / `defer preparedSnapshot.Deref()` → `try/finally`。
//
// 10) `handleGetCompletionsAtPosition` 的 `errors.Is(err, ls.ErrNeedsAutoImports)`：C# 的 `ls` 切片
//     （Ls/Completions.cs，大文件头第 4 条）把该哨兵错误**当返回值承载**
//     （`(CompletionList? List, Exception? Error)`），所以这里照其约定用
//     `error == TypeScript.Ls.LanguageService.ErrNeedsAutoImports`（引用相等即 errors.Is 的等价物）判定，
//     而不是 catch；`if err != nil || result == nil { return nil, err }` →
//     `if (error is not null) throw error; if (result is null) return null;`（异常传播 == Go 的返回 error）。
//
// 11) `ctx.Err()` → `ctx.CancellationToken.ThrowIfCancellationRequested()`（Core.Context 把取消承载在
//     CancellationToken 上，见 Core/Context.cs 文件头第 3 条）。
//
// 12) Go 的 `append(slice, x)` 在 C# 落成 `List<T>` + `ToArray()`（C# 数组长度不可变，Go 的 append 可能原地扩容）；
//     只在确有多个元素时建列表，保留 Go 的"空即 nil"形状（见第 4 条）。
//
// 13) 错误文本：Go 的 `fmt.Errorf("%w: …", ErrClientError, v)` 用 `%v` 打印结构体（如 `DocumentIdentifier`）
//     时是 Go 的默认结构格式；C# 用字符串插值即 `ToString()`（Api 分片的 proto 类型是 C# 类，格式不同）。
//     错误**类型**（`ClientErrorException`）与"失败即抛"的语义完全一致，只有文本细节不同。
namespace TypeScript.Api;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using Core = TypeScript.Core.Core;
using Path = TypeScript.TSPath.Path;
using static TypeScript.Collections.Collections;

public sealed partial class Session
{
    /// <summary>Go: <c>func (s *Session) Close()</c>（session.go:4683-4705）。关闭会话并释放所有活动快照，
    /// 无论它们的引用计数如何（见文件头第 1、9 条）。</summary>
    public void Close()
    {
        lock (_closeMu)
        {
            if (_closeOnce)
            {
                return;
            }
            _closeOnce = true;
        }

        ReleaseLanguageServerRefs();
        ReleaseSourceFileLeases();

        Project.Snapshot[] snapshots;
        _snapshotsMu.EnterWriteLock();
        try
        {
            snapshots = new Project.Snapshot[_snapshots.Count];
            var i = 0;
            foreach (var sd in _snapshots.Values)
            {
                snapshots[i++] = sd.Snapshot;
            }
            // Go 的 clear(s.snapshots)：清空内容，已分配的空间保留复用。
            _snapshots.Clear();
        }
        finally
        {
            _snapshotsMu.ExitWriteLock();
        }

        foreach (var snapshot in snapshots)
        {
            snapshot.Deref();
        }

        if (_ownsSnapshotHost)
        {
            _snapshotHost.Close();
        }
        _batchResponsePages.Clear();
    }

    /// <summary>Go: <c>func (s *Session) releaseLanguageServerRefs()</c>（session.go:4706-4739）。
    /// 把本会话通过 API 打开的 project/file 以及创建的 program 一次性交还给 project.Session
    /// （`_languageServerUpdateMu` 在 Go 里是 `defer Unlock`，C# 用 `lock` 覆盖同一区间）。</summary>
    internal void ReleaseLanguageServerRefs()
    {
        if (_projectSession is null)
        {
            return;
        }

        lock (_languageServerUpdateMu)
        {
            if (_openProjects.Len() == 0 && _openFiles.Len() == 0 && _createdPrograms.Len() == 0)
            {
                return;
            }

            var apiRequest = new Project.APISnapshotRequest();
            if (_openProjects.Len() > 0)
            {
                apiRequest.CloseProjects = _openProjects.Clone();
            }
            if (_openFiles.Len() > 0)
            {
                apiRequest.CloseFiles = _openFiles.Clone();
            }
            if (_createdPrograms.Len() > 0)
            {
                apiRequest.RemovePrograms = NewSetWithSizeHint<Project.SyntheticProjectID>(_createdPrograms.Len());
                foreach (var programId in _createdPrograms.Keys())
                {
                    apiRequest.RemovePrograms.Add(programId);
                }
            }

            Project.Snapshot snapshot;
            try
            {
                snapshot = _projectSession.APIUpdate(
                    _withLocale(Context.Background),
                    new Project.FileChangeSummary(),
                    apiRequest);
            }
            catch (Exception)
            {
                // Go: if err != nil { return }（见文件头第 2 条）。
                return;
            }

            snapshot.Deref();
            _openProjects.Clear();
            _openFiles.Clear();
            _createdPrograms.Clear();
        }
    }

    /// <summary>Go: <c>func formatSessionID(id uint64) string</c>（session.go:4740-4744）。
    /// Go 的包级未导出函数落在本 partial 上，供 `Api/Session.cs` 的 `newSession` 裸名调用（见文件头第 5 条）。</summary>
    internal static string FormatSessionID(ulong id)
    {
        return "api-session-" + id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Go: <c>func (s *Session) toPath(fileName string) tspath.Path</c>（session.go:4745-4749）。
    /// 把文件名按当前目录与大小写敏感性规范化为路径（见文件头第 6 条）。</summary>
    internal Path ToPath(string fileName)
    {
        return TSPath.TSPath.ToPath(fileName, GetCurrentDirectory(), UseCaseSensitiveFileNames());
    }

    /// <summary>Go: <c>func (s *Session) toFileChangeSummary(changes *FileNotifications) project.FileChangeSummary</c>
    /// （session.go:4750-4778）。`changes` 为 nil 时返回零值摘要；`InvalidateAll` 短路并置
    /// `IncludesWatchChangeOutsideNodeModules`（见文件头第 4 条）。</summary>
    internal Project.FileChangeSummary ToFileChangeSummary(FileNotifications? changes)
    {
        if (changes is null)
        {
            return new Project.FileChangeSummary();
        }

        var summary = new Project.FileChangeSummary();
        if (changes.InvalidateAll)
        {
            summary.InvalidateAll = true;
            summary.IncludesWatchChangeOutsideNodeModules = true;
            return summary;
        }

        var cwd = GetCurrentDirectory();
        if (changes.Changed is not null)
        {
            foreach (var doc in changes.Changed)
            {
                summary.Changed.Add(doc.ToURI(cwd));
            }
        }
        if (changes.Created is not null)
        {
            foreach (var doc in changes.Created)
            {
                summary.Created.Add(doc.ToURI(cwd));
            }
        }
        if (changes.Deleted is not null)
        {
            foreach (var doc in changes.Deleted)
            {
                summary.Deleted.Add(doc.ToURI(cwd));
            }
        }

        if (summary.Changed.Len() + summary.Created.Len() + summary.Deleted.Len() > 0)
        {
            summary.IncludesWatchChangeOutsideNodeModules = true;
        }

        return summary;
    }

    /// <summary>Go: <c>func (s *Session) getDiagnostics(ctx context.Context, params *GetDiagnosticsParams, getter func(*compiler.Program, context.Context, *ast.SourceFile) []*ast.Diagnostic) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4779-4805）。`params.Files` 非 nil 时逐文件取诊断（每个文件解析成 source file，
    /// 解析失败即错误）；否则对整组文件（`nil` source file）取一次（见文件头第 3、4 条）。</summary>
    internal DiagnosticResponse[]? GetDiagnostics(
        Context ctx,
        GetDiagnosticsParams @params,
        Func<Compiler.Program, Context, SourceFile?, Diagnostic[]> getter)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);

        if (@params.Files is not null)
        {
            var allDiags = new List<Diagnostic>();
            foreach (var file in @params.Files)
            {
                var sourceFile = ResolveOptionalSourceFile(program, file);
                allDiags.AddRange(getter(program, ctx, sourceFile));
            }
            return Api.NewDiagnosticResponses(allDiags.ToArray());
        }

        return Api.NewDiagnosticResponses(getter(program, ctx, null));
    }

    /// <summary>Go: <c>func (s *Session) handleGetSyntacticDiagnostics(ctx context.Context, params *GetDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4806-4811）。</summary>
    internal object? HandleGetSyntacticDiagnostics(Context ctx, GetDiagnosticsParams @params)
    {
        ctx = Core.WithCheckerLifetime(ctx, CheckerLifetime.Diagnostics);
        return GetDiagnostics(ctx, @params, static (program, c, file) => program.GetSyntacticDiagnostics(c, file));
    }

    /// <summary>Go: <c>func (s *Session) handleGetBindDiagnostics(ctx context.Context, params *GetDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4812-4817）。</summary>
    internal object? HandleGetBindDiagnostics(Context ctx, GetDiagnosticsParams @params)
    {
        ctx = Core.WithCheckerLifetime(ctx, CheckerLifetime.Diagnostics);
        return GetDiagnostics(ctx, @params, static (program, c, file) => program.GetBindDiagnostics(c, file));
    }

    /// <summary>Go: <c>func (s *Session) handleGetSemanticDiagnostics(ctx context.Context, params *GetDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4818-4823）。</summary>
    internal object? HandleGetSemanticDiagnostics(Context ctx, GetDiagnosticsParams @params)
    {
        ctx = Core.WithCheckerLifetime(ctx, CheckerLifetime.Diagnostics);
        return GetDiagnostics(ctx, @params, static (program, c, file) => program.GetSemanticDiagnostics(c, file));
    }

    /// <summary>Go: <c>func (s *Session) handleGetSuggestionDiagnostics(ctx context.Context, params *GetDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4824-4829）。</summary>
    internal object? HandleGetSuggestionDiagnostics(Context ctx, GetDiagnosticsParams @params)
    {
        ctx = Core.WithCheckerLifetime(ctx, CheckerLifetime.Diagnostics);
        return GetDiagnostics(ctx, @params, static (program, c, file) => program.GetSuggestionDiagnostics(c, file));
    }

    /// <summary>Go: <c>func (s *Session) handleGetDeclarationDiagnostics(ctx context.Context, params *GetDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4830-4836）。</summary>
    internal object? HandleGetDeclarationDiagnostics(Context ctx, GetDiagnosticsParams @params)
    {
        ctx = Core.WithCheckerLifetime(ctx, CheckerLifetime.Diagnostics);
        return GetDiagnostics(ctx, @params, static (program, c, file) => program.GetDeclarationDiagnostics(c, file));
    }

    /// <summary>Go: <c>func (s *Session) handleGetConfigFileParsingDiagnostics(ctx context.Context, params *GetProjectDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4837-4853）。返回配置文件的解析诊断。</summary>
    internal object? HandleGetConfigFileParsingDiagnostics(Context ctx, GetProjectDiagnosticsParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);

        var diags = program.GetConfigFileParsingDiagnostics();
        return Api.NewDiagnosticResponses(diags);
    }

    /// <summary>Go: <c>func (s *Session) handleGetProgramDiagnostics(ctx context.Context, params *GetProjectDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4854-4870）。返回 program 级诊断（含 options 诊断）。</summary>
    internal object? HandleGetProgramDiagnostics(Context ctx, GetProjectDiagnosticsParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);

        var diags = program.GetProgramDiagnostics();
        return Api.NewDiagnosticResponses(diags);
    }

    /// <summary>Go: <c>func (s *Session) handleGetGlobalDiagnostics(ctx context.Context, params *GetProjectDiagnosticsParams) ([]*DiagnosticResponse, error)</c>
    /// （session.go:4871-4902）。返回全局（非文件特定）语义诊断。</summary>
    internal object? HandleGetGlobalDiagnostics(Context ctx, GetProjectDiagnosticsParams @params)
    {
        ctx = Core.WithCheckerLifetime(ctx, CheckerLifetime.Diagnostics);
        var sd = GetSnapshotData(@params.Snapshot);
        var proj = sd.GetProject(@params.Project);

        var program = proj.GetProgram() ?? throw new ClientErrorException("project has no program");

        // Global diagnostics are accumulated lazily by the project's checker pool as
        // files are checked. Force a full semantic pass so any global (non-file-specific)
        // diagnostics are produced; otherwise this would return an empty result for
        // projects using an external checker pool (the typical API case), since
        // compiler.Program.GetGlobalDiagnostics only reports for the internal pool.
        program.GetSemanticDiagnostics(ctx, null);

        var diags = Core.Filter(
            proj.GetProjectDiagnostics(ctx),
            static d => d.File() is null);
        return Api.NewDiagnosticResponses(diags);
    }

    /// <summary>Go: <c>func (s *Session) resolveOptionalSourceFile(program *compiler.Program, file *DocumentIdentifier) (*ast.SourceFile, error)</c>
    /// （session.go:4903-4914）。file 为 nil 表示"所有文件"，返回 nil；否则解析 source file，
    /// 找不到即 ClientError（Go 的 `&copy` 只读指针 ⇒ C# 按值传，nil 语义由 `DocumentIdentifier?` 承载）。</summary>
    internal SourceFile? ResolveOptionalSourceFile(Compiler.Program program, DocumentIdentifier? file)
    {
        if (file is null)
        {
            return null;
        }

        var sourceFile = program.GetSourceFile(file.ToFileName());
        if (sourceFile is null)
        {
            throw new ClientErrorException($"source file not found: {file}");
        }

        return sourceFile;
    }

    /// <summary>Go: <c>func (s *Session) handleGetReferencesToSymbolInFile(ctx context.Context, params *GetReferencesToSymbolInFileParams) ([]NodeHandle, error)</c>
    /// （session.go:4915-4943）。返回文件中所有引用该 symbol 的标识符节点句柄
    /// （`defer setup.done()` → try/finally，见文件头第 7、9 条）。</summary>
    internal object? HandleGetReferencesToSymbolInFile(Context ctx, GetReferencesToSymbolInFileParams @params)
    {
        var setup = SetupChecker(ctx, @params.Snapshot, @params.Project);
        try
        {
            var symbol = setup.ResolveSymbolHandle(@params.Symbol);

            var sourceFile = setup.Program.GetSourceFile(@params.File.ToFileName())
                ?? throw new ClientErrorException($"source file not found: {@params.File}");

            var nodes = setup.Checker.GetReferencesToSymbolInFile(sourceFile, symbol);
            var result = new NodeHandle[nodes.Length];
            for (var i = 0; i < nodes.Length; i++)
            {
                result[i] = setup.Sd.NodeHandleFrom(nodes[i]);
            }
            return result;
        }
        finally
        {
            setup.Done();
        }
    }

    /// <summary>Go: <c>func (s *Session) handleGetSignatureUsages(ctx context.Context, params *GetSignatureUsagesParams) ([]SignatureUsageResponse, error)</c>
    /// （session.go:4944-4986）。把签名声明的每个使用点表达成 name/call 句柄对。</summary>
    internal object? HandleGetSignatureUsages(Context ctx, GetSignatureUsagesParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);

        var signatureDecl = sd.ResolveNodeHandle(program, @params.SignatureDecl);

        var langSvc = SetupLanguageService(sd.Snapshot, program, @params.Project, "");

        var usages = langSvc.GetSignatureUsages(ctx, signatureDecl);
        if (usages is null)
        {
            return null;
        }

        var result = new SignatureUsageResponse[usages.Length];
        for (var i = 0; i < usages.Length; i++)
        {
            var u = usages[i];
            var entry = new SignatureUsageResponse
            {
                Name = sd.NodeHandleFrom(u._name!),
            };
            if (u._call is not null)
            {
                entry.Call = sd.NodeHandleFrom(u._call);
            }
            result[i] = entry;
        }
        return result;
    }

    /// <summary>Go: <c>func (s *Session) handleGetCompletionsAtPosition(ctx context.Context, params *GetCompletionsAtPositionParams) (*CompletionInfoResponse, error)</c>
    /// （session.go:4987-5066）。`IncludeSymbol` 时把 ctx 提升到 API checker 生命周期；遇到
    /// `ErrNeedsAutoImports` 哨兵时用带 auto-import 的新快照重跑一次（见文件头第 10、11 条）。</summary>
    internal object? HandleGetCompletionsAtPosition(Context ctx, GetCompletionsAtPositionParams @params)
    {
        if (@params.IncludeSymbol)
        {
            ctx = Core.WithCheckerLifetime(ctx, CheckerLifetime.API);
        }

        var sd = GetSnapshotData(@params.Snapshot);

        (Ls.CompletionList? List, Exception? Error) Run(Project.Snapshot snapshot, Compiler.Program program)
        {
            var sourceFile = program.GetSourceFile(@params.File.ToFileName());
            if (sourceFile is null)
            {
                return (null, null);
            }
            var langSvc = SetupLanguageService(snapshot, program, @params.Project, "");
            var internalPos = sourceFile.GetPositionMap().UTF16ToUTF8((int)@params.Position);
            return langSvc.GetCompletionsAtPosition(
                ctx,
                sourceFile,
                internalPos,
                @params.TriggerCharacter,
                @params.IncludeSymbol);
        }

        var program = sd.GetProgram(@params.Project);
        var (result, error) = Run(sd.Snapshot, program);
        if (error == TypeScript.Ls.LanguageService.ErrNeedsAutoImports)
        {
            var preparedSnapshot = _snapshotHost.CloneSnapshotWithAutoImports(
                ctx,
                sd.Snapshot,
                @params.File.ToURI(GetCurrentDirectory()),
                null);
            try
            {
                if (_projectSession is not null)
                {
                    _projectSession.TryAdoptSnapshotInBackground(sd.Snapshot, preparedSnapshot);
                }

                ctx.CancellationToken.ThrowIfCancellationRequested();

                var projectId = @params.Project;
                var proj = preparedSnapshot.ProjectCollection.GetProject(projectId)
                    ?? throw new ClientErrorException($"project {projectId} not found");

                program = proj.GetProgram() ?? throw new ClientErrorException("project has no program");

                (result, error) = Run(preparedSnapshot, program);
            }
            finally
            {
                preparedSnapshot.Deref();
            }
        }
        if (error is not null)
        {
            throw error;
        }
        if (result is null)
        {
            return null;
        }

        var entries = new List<CompletionEntryResponse>(result.Items.Length);
        foreach (var item in result.Items)
        {
            var completionItem = item._completionItem;
            var entry = new CompletionEntryResponse
            {
                Name = completionItem.Label,
                SortText = completionItem.SortText,
                InsertText = completionItem.InsertText,
                FilterText = completionItem.FilterText,
                Detail = completionItem.Detail,
            };
            if (completionItem.Kind is { } kind)
            {
                entry.Kind = (uint)kind;
            }
            if (completionItem.LabelDetails is { } labelDetails)
            {
                entry.LabelDetails = new CompletionEntryLabelDetailsResponse
                {
                    Detail = labelDetails.Detail,
                    Description = labelDetails.Description,
                };
            }
            if (item.Symbol is not null)
            {
                entry.Symbol = sd.NewSymbolResponse(item.Symbol, @params.Project);
            }
            entries.Add(entry);
        }

        return new CompletionInfoResponse
        {
            IsIncomplete = result.IsIncomplete,
            Entries = entries.ToArray(),
        };
    }

    /// <summary>Go: <c>func (s *Session) handleGetReferencedSymbolsForNode(ctx context.Context, params *GetReferencedSymbolsForNodeParams) ([]ReferencedSymbolEntry, error)</c>
    /// （session.go:5067-5118）。返回节点上找到的所有引用的句柄（见文件头第 4、7 条）。</summary>
    internal object? HandleGetReferencedSymbolsForNode(Context ctx, GetReferencedSymbolsForNodeParams @params)
    {
        var sd = GetSnapshotData(@params.Snapshot);
        var program = sd.GetProgram(@params.Project);

        var node = sd.ResolveNodeHandle(program, @params.Node);

        var langSvc = SetupLanguageService(sd.Snapshot, program, @params.Project, "");

        var sourceFiles = program.GetSourceFiles();
        var entries = langSvc.GetReferencedSymbolsForNode(ctx, @params.Position, node, sourceFiles);
        if (entries is null)
        {
            return null;
        }

        var result = new List<ReferencedSymbolEntry>();
        foreach (var entry in entries)
        {
            var defNode = entry.DefinitionNode();
            if (defNode is null)
            {
                continue;
            }

            var refs = new List<NodeHandle>();
            foreach (var reference in entry.References() ?? [])
            {
                if (!reference.IsNodeEntry())
                {
                    continue;
                }
                refs.Add(sd.NodeHandleFrom(reference.Node()!));
            }

            var re = new ReferencedSymbolEntry
            {
                Definition = sd.NodeHandleFrom(defNode),
                References = refs.ToArray(),
            };
            if (entry.DefinitionSymbol() is { } sym)
            {
                re.Symbol = sd.NewSymbolResponse(sym, @params.Project);
            }
            result.Add(re);
        }

        // Go 的 `var result []ReferencedSymbolEntry` 在没有条目时是 nil（JSON null），
        // 与上面 `make(..., 0, n)` 的 `[]` 不同（见文件头第 4 条）。
        return result.Count == 0 ? null : result.ToArray();
    }
}
