// ported from: internal/lsp/server.go:1191-1465（handler 注册表 + 各注册辅助函数 + crossProjectOrchestrator）
// 见 Server.cs 文件头。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) Go 的 handler 签名是 `func(*Server, context.Context, *lsproto.RequestMessage) (func() error, error)`：
//    "同步段的错误" 与 "异步段" 分开返回。C# 侧把"同步段的错误"落成**异常**（统一约定），
//    于是同步段失败 = handler 抛异常（dispatchLoop 的 catch 会走与 Go 的 handleError 相同的路径），
//    返回值只剩 `Func<Exception?>?`（异步段，null 表示没有异步工作）。`func() error` 的 error 用
//    `Exception?` 表达（null = 成功），与 G1 在 `Ls.HandleCrossProject`、`WithLanguageServiceAndSnapshot`
//    等处既有约定一致。
//
// 2) Go 的 `defer s.recover(req)`（panic 恢复）→ C# 的 `try { … } catch (Exception e) { s.Recover(req, e); return null; }`。
//    Go 里被 recover 的函数返回**零值**（error 为 nil），所以 C# 侧也返回 null（= 不再额外报错），
//    与 Go 的双响应行为（recover 先发一条错误响应、调用点随后又 sendResult 一次）逐字一致。
//
// 3) `handlers = sync.OnceValue(func() handlerMap { … })` → `Lazy<Dictionary<Method, Handler>>`；
//    C# 的 `Lazy` 默认 ExecutionAndPublication 与 `sync.OnceValue` 语义一致。
//
// 4) `req.UnmarshalParams[Req]()` → `req.UnmarshalParams<Req>()`（LsProto 的扩展方法，`where Req : new()`）。
//
// 5) `crossProjectOrchestrator.GetProjectsLoadingProjectTree` 返回 Go 的 `iter.Seq[ls.Project]`；
//    C# 侧先把快照里的项目**物化**成列表再返回（C# 的迭代器不能在 `WithSnapshotLoadingProjectTree`
//    的回调里 `yield`，回调必须在返回前跑完）。与 G1 在 `CrossProject.GetResultsIterator` 的处理同因同果。
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsProto;
using static TypeScript.LsProto.LsProto;
using Path = TypeScript.TSPath.Path;
using LspProject = TypeScript.Ls.Project;
using LsLanguageService = TypeScript.Ls.LanguageService;
using LsOrchestrator = TypeScript.Ls.CrossProjectOrchestrator;
using ProjectProject = TypeScript.Project.Project;
using ProjectSnapshot = TypeScript.Project.Snapshot;

namespace TypeScript.Lsp;

/// <summary>
/// Go: <c>type handlerMap map[lsproto.Method]func(*Server, context.Context, *lsproto.RequestMessage) (func() error, error)</c>
/// （server.go:1222）。C# 侧"同步段的 error"变成异常（见文件头第 1 条），返回值只剩异步段委托。
/// </summary>
internal delegate Func<Exception?>? Handler(Server server, Context ctx, RequestMessage req);

public sealed partial class Server
{
    /// <summary>Go: <c>var handlers = sync.OnceValue(func() handlerMap { … })</c>（server.go:1224-1289）。</summary>
    private static readonly Lazy<Dictionary<Method, Handler>> _handlers = new(BuildHandlers);

    private static Dictionary<Method, Handler> Handlers() => _handlers.Value;

    private static Dictionary<Method, Handler> BuildHandlers()
    {
        var handlers = new Dictionary<Method, Handler>();

        handlers.RegisterRequestHandler(InitializeInfo, static (s, ctx, @params, req) => s.HandleInitialize(ctx, @params, req));
        handlers.RegisterNotificationHandler(InitializedInfo, static (s, ctx, @params) => s.HandleInitialized(ctx, @params));
        handlers.RegisterRequestHandler(ShutdownInfo, static (s, ctx, @params, req) => s.HandleShutdown(ctx, @params, req));
        handlers.RegisterNotificationHandler(ExitInfo, static (s, ctx, @params) => s.HandleExit(ctx, @params));

        handlers.RegisterNotificationHandler(WorkspaceDidChangeConfigurationInfo, static (s, ctx, @params) => s.HandleDidChangeWorkspaceConfiguration(ctx, @params));
        handlers.RegisterNotificationHandler(TextDocumentDidOpenInfo, static (s, ctx, @params) => s.HandleDidOpen(ctx, @params));
        handlers.RegisterNotificationHandler(TextDocumentDidChangeInfo, static (s, ctx, @params) => s.HandleDidChange(ctx, @params));
        handlers.RegisterNotificationHandler(TextDocumentDidSaveInfo, static (s, ctx, @params) => s.HandleDidSave(ctx, @params));
        handlers.RegisterNotificationHandler(TextDocumentDidCloseInfo, static (s, ctx, @params) => s.HandleDidClose(ctx, @params));
        handlers.RegisterNotificationHandler(WorkspaceDidChangeWatchedFilesInfo, static (s, ctx, @params) => s.HandleDidChangeWatchedFiles(ctx, @params));
        handlers.RegisterNotificationHandler(SetTraceInfo, static (s, ctx, @params) => s.HandleSetTrace(ctx, @params));
        handlers.RegisterNotificationHandler(CustomSetLogVerbosityInfo, static (s, ctx, @params) => s.HandleSetLogVerbosity(ctx, @params));
        handlers.RegisterRequestHandler(WorkspaceWillRenameFilesInfo, static (s, ctx, @params, req) => s.HandleWillRenameFiles(ctx, @params, req));

        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentDiagnosticInfo, static (s, ctx, ls, @params) => s.HandleDocumentDiagnostic(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentHoverInfo, static (s, ctx, ls, @params) => s.HandleHover(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentDefinitionInfo, static (s, ctx, ls, @params) => s.HandleDefinition(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(CustomTextDocumentSourceDefinitionInfo, static (s, ctx, ls, @params) => s.HandleSourceDefinition(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentTypeDefinitionInfo, static (s, ctx, ls, @params) => s.HandleTypeDefinition(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentSignatureHelpInfo, static (s, ctx, ls, @params) => s.HandleSignatureHelp(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentFormattingInfo, static (s, ctx, ls, @params) => s.HandleDocumentFormat(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentRangeFormattingInfo, static (s, ctx, ls, @params) => s.HandleDocumentRangeFormat(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentOnTypeFormattingInfo, static (s, ctx, ls, @params) => s.HandleDocumentOnTypeFormat(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentDocumentSymbolInfo, static (s, ctx, ls, @params) => s.HandleDocumentSymbol(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentDocumentHighlightInfo, static (s, ctx, ls, @params) => s.HandleDocumentHighlight(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(CustomTextDocumentMultiDocumentHighlightInfo, static (s, ctx, ls, @params) => s.HandleMultiDocumentHighlight(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentSelectionRangeInfo, static (s, ctx, ls, @params) => s.HandleSelectionRange(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentInlayHintInfo, static (s, ctx, ls, @params) => s.HandleInlayHint(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentCodeLensInfo, static (s, ctx, ls, @params) => s.HandleCodeLens(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentCodeActionInfo, static (s, ctx, ls, @params) => s.HandleCodeAction(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentPrepareCallHierarchyInfo, static (s, ctx, ls, @params) => s.HandlePrepareCallHierarchy(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentFoldingRangeInfo, static (s, ctx, ls, @params) => s.HandleFoldingRange(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentPrepareRenameInfo, static (s, ctx, ls, @params) => s.HandlePrepareRename(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentLinkedEditingRangeInfo, static (s, ctx, ls, @params) => s.HandleLinkedEditingRange(ctx, ls, @params));

        handlers.RegisterLanguageServiceWithAutoImportsRequestHandler(TextDocumentCompletionInfo, static (s, ctx, ls, @params) => s.HandleCompletion(ctx, ls, @params));
        handlers.RegisterLanguageServiceWithAutoImportsRequestHandler(TextDocumentCodeActionInfo, static (s, ctx, ls, @params) => s.HandleCodeAction(ctx, ls, @params));

        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentVSOnAutoInsertInfo, static (s, ctx, ls, @params) => s.HandleVSOnAutoInsert(ctx, ls, @params));

        handlers.RegisterMultiProjectReferenceRequestHandler(TextDocumentReferencesInfo, static (ls, ctx, @params, orchestrator) => ls.ProvideReferences(ctx, @params!, orchestrator));
        handlers.RegisterMultiProjectReferenceRequestHandler(TextDocumentVSReferencesInfo, static (ls, ctx, @params, orchestrator) => ls.ProvideVSReferences(ctx, @params!, orchestrator));
        handlers.RegisterRequestHandler(TextDocumentRenameInfo, static (s, ctx, @params, req) => s.HandleRename(ctx, @params, req));
        handlers.RegisterMultiProjectReferenceRequestHandler(TextDocumentImplementationInfo, static (ls, ctx, @params, orchestrator) => ls.ProvideImplementations(ctx, @params!, orchestrator));

        handlers.RegisterRequestHandler(CallHierarchyIncomingCallsInfo, static (s, ctx, @params, req) => s.HandleCallHierarchyIncomingCalls(ctx, @params, req));
        handlers.RegisterRequestHandler(CallHierarchyOutgoingCallsInfo, static (s, ctx, @params, req) => s.HandleCallHierarchyOutgoingCalls(ctx, @params, req));

        handlers.RegisterRequestHandler(WorkspaceSymbolInfo, static (s, ctx, @params, req) => s.HandleWorkspaceSymbol(ctx, @params, req));
        handlers.RegisterRequestHandler(CompletionItemResolveInfo, static (s, ctx, @params, req) => s.HandleCompletionItemResolve(ctx, @params, req));
        handlers.RegisterRequestHandler(CodeLensResolveInfo, static (s, ctx, @params, req) => s.HandleCodeLensResolve(ctx, @params, req));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentSemanticTokensFullInfo, static (s, ctx, ls, @params) => s.HandleSemanticTokensFull(ctx, ls, @params));
        handlers.RegisterLanguageServiceDocumentRequestHandler(TextDocumentSemanticTokensRangeInfo, static (s, ctx, ls, @params) => s.HandleSemanticTokensRange(ctx, ls, @params));

        // Developer/debugging commands
        handlers.RegisterRequestHandler(CustomRunGCInfo, static (s, ctx, @params, req) => s.HandleRunGC(ctx, @params, req));
        handlers.RegisterRequestHandler(CustomSaveHeapProfileInfo, static (s, ctx, @params, req) => s.HandleSaveHeapProfile(ctx, @params, req));
        handlers.RegisterRequestHandler(CustomSaveAllocProfileInfo, static (s, ctx, @params, req) => s.HandleSaveAllocProfile(ctx, @params, req));
        handlers.RegisterRequestHandler(CustomStartCPUProfileInfo, static (s, ctx, @params, req) => s.HandleStartCPUProfile(ctx, @params, req));
        handlers.RegisterRequestHandler(CustomStopCPUProfileInfo, static (s, ctx, @params, req) => s.HandleStopCPUProfile(ctx, @params, req));

        // Go: handlers.registerRequestHandler(lsproto.CustomInitializeAPISessionInfo, (*Server).handleInitializeAPISession)（server.go:1294）。
        // `internal/api` 已移植（`TypeScript.Api`），实现见 Server.ApiSession.cs。
        handlers.RegisterRequestHandler(CustomInitializeAPISessionInfo, static (s, ctx, @params, req) => s.HandleInitializeAPISession(ctx, @params, req));
        handlers.RegisterRequestHandler(CustomProjectInfoInfo, static (s, ctx, @params, req) => s.HandleProjectInfo(ctx, @params, req));
        handlers.RegisterRequestHandler(CustomSetContentMapperContributionsInfo, static (s, ctx, @params, req) => s.HandleSetContentMapperContributions(ctx, @params, req));
        return handlers;
    }
}

/// <summary>
/// Go 的 <c>handlerMap</c> 的注册辅助函数（server.go:1291-1410）。
/// C# 的扩展方法必须定义在**非泛型静态类**里，所以它们不能留在 <see cref="Server"/> 内（先例：G1 的
/// `Ls.CrossProject` 组合器等包级 helper 都放在静态类上）。
/// </summary>
internal static class HandlerMapExtensions
{
    /// <summary>Go: <c>func (handlers handlerMap) registerNotificationHandler[Req any](info, fn)</c>（server.go:1291-1305）。</summary>
    internal static void RegisterNotificationHandler<Req>(
        this Dictionary<Method, Handler> handlers,
        NotificationInfo<Req> info,
        Func<Server, Context, Req, Exception?> fn)
        where Req : new()
    {
        handlers[info.Method] = (s, ctx, req) =>
        {
            if (s._session is null && req.Method != Method.Initialized)
            {
                throw new ErrorCodeException(ErrorCode.ServerNotInitialized, ErrorCode.ServerNotInitialized.String());
            }

            var @params = req.UnmarshalParams<Req>();
            var err = fn(s, ctx, @params);
            if (err is not null)
            {
                throw err;
            }

            if (ctx.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ctx.CancellationToken);
            }

            return null;
        };
    }

    /// <summary>Go: <c>func (handlers handlerMap) registerRequestHandler[Req, Resp any](info, fn)</c>（server.go:1307-1326）。</summary>
    internal static void RegisterRequestHandler<Req, Resp>(
        this Dictionary<Method, Handler> handlers,
        RequestInfo<Req, Resp> info,
        Func<Server, Context, Req, RequestMessage, Resp> fn)
        where Req : new()
    {
        handlers[info.Method] = (s, ctx, req) =>
        {
            if (s._session is null && req.Method != Method.Initialize)
            {
                throw new ErrorCodeException(ErrorCode.ServerNotInitialized, ErrorCode.ServerNotInitialized.String());
            }

            var @params = req.UnmarshalParams<Req>();
            var resp = fn(s, ctx, @params, req);
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(ctx.CancellationToken);
            }

            var sendError = s.SendResult(req.ID, resp);
            if (sendError is not null)
            {
                throw sendError;
            }

            return null;
        };
    }

    /// <summary>Go: <c>func (handlers handlerMap) registerLanguageServiceDocumentRequestHandler[Req, Resp any](info, fn)</c>（server.go:1328-1350）。</summary>
    internal static void RegisterLanguageServiceDocumentRequestHandler<Req, Resp>(
        this Dictionary<Method, Handler> handlers,
        RequestInfo<Req, Resp> info,
        Func<Server, Context, LsLanguageService, Req, (Resp Response, Exception? Error)> fn)
        where Req : new()
    {
        handlers[info.Method] = (s, ctx, req) =>
        {
            var @params = req.UnmarshalParams<Req>();
            var (ls, err) = s._session!.GetLanguageService(ctx, ((HasTextDocumentURI)@params!).TextDocumentURI());
            if (err is not null)
            {
                throw err;
            }

            return () =>
            {
                try
                {
                    var (resp, lsErr) = fn(s, ctx, ls!, @params);
                    // After any language service request, check if new global diagnostics were
                    // discovered during checking and push updated tsconfig diagnostics if so.
                    s._session!.EnqueuePublishGlobalDiagnostics();
                    if (lsErr is not null)
                    {
                        return lsErr;
                    }

                    if (ctx.CancellationToken.IsCancellationRequested)
                    {
                        return new OperationCanceledException(ctx.CancellationToken);
                    }

                    return s.SendResult(req.ID, resp);
                }
                catch (Exception e)
                {
                    s.Recover(req, e);
                    return null;
                }
            };
        };
    }

    /// <summary>Go: <c>func (handlers handlerMap) registerLanguageServiceWithAutoImportsRequestHandler[Req, Resp any](info, fn)</c>（server.go:1352-1386）。</summary>
    internal static void RegisterLanguageServiceWithAutoImportsRequestHandler<Req, Resp>(
        this Dictionary<Method, Handler> handlers,
        RequestInfo<Req, Resp> info,
        Func<Server, Context, LsLanguageService, Req, (Resp Response, Exception? Error)> fn)
        where Req : new()
    {
        handlers[info.Method] = (s, ctx, req) =>
        {
            var @params = req.UnmarshalParams<Req>();
            var (asyncWork, err) = s._session!.WithLanguageServiceAndSnapshot(
                ctx,
                ((HasTextDocumentURI)@params!).TextDocumentURI(),
                (languageService, snapshot) =>
                {
                    return ((Func<Exception?>)(() =>
                    {
                        try
                        {
                            var (resp, lsErr) = fn(s, ctx, languageService, @params);
                            if (ReferenceEquals(lsErr, LsLanguageService.ErrNeedsAutoImports))
                            {
                                var (ls2, lsErr2) = s._session!.GetLanguageServiceWithAutoImports(ctx, snapshot, ((HasTextDocumentURI)@params!).TextDocumentURI());
                                if (lsErr2 is not null)
                                {
                                    return lsErr2;
                                }

                                lsErr = lsErr2;
                                var (resp2, lsErr3) = fn(s, ctx, ls2!, @params);
                                if (ReferenceEquals(lsErr3, LsLanguageService.ErrNeedsAutoImports))
                                {
                                    throw new InvalidOperationException(info.Method.Value + " returned ErrNeedsAutoImports even after enabling auto imports");
                                }

                                lsErr = lsErr3;
                                resp = resp2;
                            }

                            if (lsErr is not null)
                            {
                                return lsErr;
                            }

                            if (ctx.CancellationToken.IsCancellationRequested)
                            {
                                return new OperationCanceledException(ctx.CancellationToken);
                            }

                            return s.SendResult(req.ID, resp);
                        }
                        catch (Exception e)
                        {
                            s.Recover(req, e);
                            return null;
                        }
                    }), (Exception?)null);
                });
            if (err is not null)
            {
                throw err;
            }

            return asyncWork;
        };
    }

    /// <summary>Go: <c>func (handlers handlerMap) registerMultiProjectReferenceRequestHandler[Req, Resp any](info, fn)</c>（server.go:1388-1410）。</summary>
    internal static void RegisterMultiProjectReferenceRequestHandler<Req, Resp>(
        this Dictionary<Method, Handler> handlers,
        RequestInfo<Req, Resp> info,
        Func<LsLanguageService, Context, Req, LsOrchestrator, (Resp Response, Exception? Error)> fn)
        where Req : new()
    {
        handlers[info.Method] = (s, ctx, req) =>
        {
            var @params = req.UnmarshalParams<Req>();

            // !!! sheetal: multiple projects that contain the file through symlinks
            var (defaultLs, orchestrator, err) = s.GetLanguageServiceAndCrossProjectOrchestrator(ctx, ((HasTextDocumentPosition)@params!).TextDocumentURI(), req);
            if (err is not null)
            {
                throw err;
            }

            return () =>
            {
                try
                {
                    var (resp, lsErr) = fn(defaultLs!, ctx, @params, orchestrator!);
                    if (lsErr is not null)
                    {
                        return lsErr;
                    }

                    if (ctx.CancellationToken.IsCancellationRequested)
                    {
                        return new OperationCanceledException(ctx.CancellationToken);
                    }

                    return s.SendResult(req.ID, resp);
                }
                catch (Exception e)
                {
                    s.Recover(req, e);
                    return null;
                }
            };
        };
    }
}

public sealed partial class Server
{
    /// <summary>Go: <c>type crossProjectOrchestrator struct</c>（server.go:1412-1418）。</summary>
    private sealed class CrossProjectOrchestratorImpl(Server server, RequestMessage req, LspProject defaultProject, LspProject[] allProjects)
        : LsOrchestrator
    {
        internal readonly Server _server = server;
        internal readonly RequestMessage _req = req;
        internal readonly LspProject _defaultProject = defaultProject;
        internal readonly LspProject[] _allProjects = allProjects;

        /// <summary>Go: <c>func (c *crossProjectOrchestrator) GetDefaultProject() ls.Project</c>（server.go:1422-1424）。</summary>
        public LspProject GetDefaultProject() => _defaultProject;

        /// <summary>Go: <c>func (c *crossProjectOrchestrator) GetAllProjectsForInitialRequest() []ls.Project</c>（server.go:1426-1428）。</summary>
        public LspProject[] GetAllProjectsForInitialRequest() => _allProjects;

        /// <summary>Go: <c>func (c *crossProjectOrchestrator) GetLanguageServiceForProjectWithFile(ctx, p, uri)</c>（server.go:1430-1432）。</summary>
        public LsLanguageService? GetLanguageServiceForProjectWithFile(Context ctx, LspProject project, DocumentUri uri) =>
            _server._session!.GetLanguageServiceForProjectWithFile(ctx, (ProjectProject)project, uri);

        /// <summary>Go: <c>func (c *crossProjectOrchestrator) GetProjectsForFile(ctx, uri) ([]ls.Project, error)</c>（server.go:1434-1436）。</summary>
        public (LspProject[] Projects, Exception? Error) GetProjectsForFile(Context ctx, DocumentUri uri)
        {
            var (projects, err) = _server._session!.GetProjectsForFile(ctx, uri);
            return (projects ?? [], err);
        }

        /// <summary>Go: <c>func (c *crossProjectOrchestrator) GetProjectsLoadingProjectTree(ctx, requestedProjectTrees) iter.Seq[ls.Project]</c>（server.go:1438-1446）。</summary>
        public IEnumerable<LspProject> GetProjectsLoadingProjectTree(Context ctx, Set<Path> requestedProjectTrees)
        {
            // 见文件头第 5 条：先把快照里的项目物化。
            var result = new List<LspProject>();
            _server._session!.WithSnapshotLoadingProjectTree(ctx, requestedProjectTrees, (snapshot) =>
            {
                foreach (var project in snapshot.ProjectCollection.LanguageServiceProjects())
                {
                    result.Add(project);
                }
            });
            return result;
        }
    }

    /// <summary>Go: <c>func (s *Server) getLanguageServiceAndCrossProjectOrchestrator(ctx, uri, req)</c>（server.go:1448-1454）。</summary>
    internal (LsLanguageService? DefaultLs, LsOrchestrator? Orchestrator, Exception? Error) GetLanguageServiceAndCrossProjectOrchestrator(
        Context ctx,
        DocumentUri uri,
        RequestMessage req)
    {
        var (defaultProject, defaultLs, allProjects, err) = _session!.GetLanguageServiceAndProjectsForFile(ctx, uri);
        LsOrchestrator? orchestrator = null;
        if (err is null)
        {
            orchestrator = new CrossProjectOrchestratorImpl(this, req, defaultProject!, allProjects ?? []);
        }

        return (defaultLs, orchestrator, err);
    }
}
