// ported from: internal/lsp/server.go:1918-2360（各语言服务请求的转发 handler + rename/callHierarchy/workspaceSymbol/
// resolve/semanticTokens/dev 命令之外的全部请求 handler）
// 见 Server.cs 文件头。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 两类返回值形状：
//      * 注册在 `registerLanguageServiceDocumentRequestHandler` / `WithAutoImports` / `MultiProject` 上的 handler
//        保持 Go 的 `(Resp, error)`（C# 的元组 `(Resp Response, Exception? Error)`）——错误**正常返回**，
//        只有真正的异常（= Go 的 panic）才由注册辅助函数里的 catch 走 `Recover`。
//      * 注册在 `registerRequestHandler` 上的 handler（handleRename / callHierarchy / workspaceSymbol /
//        resolve 系列）返回单个 `Resp`，错误用**抛异常**表达（于是 dispatch 的同步段错误路径与 Go 一致）。
//
// 2) Go 的 `defer s.recover(reqMsg)` → 本文件的 `try { … } catch (Exception e) { Recover(reqMsg, e); return 零值; }`
//    （Go 被 recover 的函数返回零值，所以这里也返回零值，随后调用点照 Go 的样子继续 sendResult）。
//
// 3) `lsproto.CustomTextDocumentSourceDefinitionResponse` 在 Go 里是 `*LocationOrLocationsOrDefinitionLinksOrNull`
//    的别名（handler 里写 `&resp`）；C# 侧直接返回那个 union 值（写成指针只是 Go 的接口值取址，
//    json.Marshal 的对象仍是它指向的值）。
//
// 4) `core.Map(projects, ls.Project.GetProgram)` → `Core.Map(projects, p => p.GetProgram()!)`
//    （C# 的 `Ls.Project.GetProgram()` 声明为可空，Go 侧该接口方法实际不返回 nil）。
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Project;
using static TypeScript.Core.Core;
using LsLanguageService = TypeScript.Ls.LanguageService;
using LsOrchestrator = TypeScript.Ls.CrossProjectOrchestrator;
using Program = TypeScript.Compiler.Program;
using ProjectSnapshot = TypeScript.Project.Snapshot;

namespace TypeScript.Lsp;

public sealed partial class Server
{
    /// <summary>Go: <c>func (s *Server) handleHover(ctx, ls, params)</c>（server.go:1918-1920）。</summary>
    private (HoverOrNull Response, Exception? Error) HandleHover(Context ctx, LsLanguageService ls, HoverParams? @params) =>
        ls.ProvideHover(ctx, @params!);

    /// <summary>Go: <c>func (s *Server) handlePrepareRename(ctx, languageService, params)</c>（server.go:1922-1932）。</summary>
    private (RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull Response, Exception? Error) HandlePrepareRename(
        Context ctx,
        LsLanguageService languageService,
        PrepareRenameParams? @params)
    {
        var info = languageService.GetRenameInfo(ctx, "" /*newName*/, @params!.TextDocument.Uri, @params.Position);
        if (!info.CanRename)
        {
            return (new RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull(),
                new UserFacingRequestFailedError(info.LocalizedErrorMessage));
        }

        return (new RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull
        {
            PrepareRenamePlaceholder = new PrepareRenamePlaceholder
            {
                Range = info.TriggerSpan,
                Placeholder = info.DisplayName,
            },
        }, null);
    }

    /// <summary>Go: <c>func (s *Server) handleRename(ctx, params, req)</c>（server.go:1934-1978）。</summary>
    private WorkspaceEditOrNull HandleRename(Context ctx, RenameParams? @params, RequestMessage req)
    {
        var (defaultLs, orchestrator, err) = GetLanguageServiceAndCrossProjectOrchestrator(ctx, @params!.TextDocument.Uri, req);
        if (err is not null)
        {
            throw err;
        }

        var info = defaultLs!.GetRenameInfo(ctx, @params.NewName, @params.TextDocument.Uri, @params.Position);
        if (info.CanRename && info.FileToRename != "")
        {
            // We send a `willRenameFiles` request if the client allows;
            // otherwise we directly compute the edits for renaming the file.
            if (Ls.Ls.ClientSupportsWillRenameFiles(ctx))
            {
                var documentChanges = new List<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>
                {
                    new()
                    {
                        RenameFile = new RenameFile
                        {
                            Kind = new StringLiteralRename(),
                            OldUri = LsConv.LsConv.FileNameToDocumentURI(info.FileToRename),
                            NewUri = LsConv.LsConv.FileNameToDocumentURI(info.NewFileName),
                        },
                    },
                };
                return new WorkspaceEditOrNull
                {
                    WorkspaceEdit = new WorkspaceEdit
                    {
                        DocumentChanges = [.. documentChanges],
                    },
                };
            }

            var renameFilesParams = new RenameFilesParams
            {
                Files =
                [
                    new FileRename
                    {
                        OldUri = LsConv.LsConv.FileNameToDocumentURI(info.FileToRename),
                        NewUri = LsConv.LsConv.FileNameToDocumentURI(info.NewFileName),
                    },
                ],
            };
            return HandleWillRenameFilesWorker(ctx, renameFilesParams, req, true /*sendRenameFile*/);
        }

        var (renameResp, renameError) = defaultLs.ProvideRename(ctx, @params, orchestrator!);
        if (renameError is not null)
        {
            throw renameError;
        }

        return renameResp;
    }

    /// <summary>Go: <c>func (s *Server) handleSignatureHelp(ctx, languageService, params)</c>（server.go:2139-2147）。</summary>
    private (SignatureHelpOrNull Response, Exception? Error) HandleSignatureHelp(
        Context ctx,
        LsLanguageService languageService,
        SignatureHelpParams? @params) =>
        languageService.ProvideSignatureHelp(ctx, @params!.TextDocument.Uri, @params.Position, @params.Context);

    /// <summary>Go: <c>func (s *Server) handleFoldingRange(ctx, ls, params)</c>（server.go:2149-2151）。</summary>
    private (FoldingRangesOrNull Response, Exception? Error) HandleFoldingRange(Context ctx, LsLanguageService ls, FoldingRangeParams? @params) =>
        ls.ProvideFoldingRange(ctx, @params!.TextDocument.Uri);

    /// <summary>Go: <c>func (s *Server) handleVSOnAutoInsert(ctx, ls, params)</c>（server.go:2153-2155）。</summary>
    private (VSOnAutoInsertResponseItemOrNull Response, Exception? Error) HandleVSOnAutoInsert(Context ctx, LsLanguageService ls, VSOnAutoInsertParams? @params) =>
        ls.ProvideOnAutoInsert(ctx, @params!);

    /// <summary>Go: <c>func (s *Server) handleLinkedEditingRange(ctx, ls, params)</c>（server.go:2157-2159）。</summary>
    private (LinkedEditingRangesOrNull Response, Exception? Error) HandleLinkedEditingRange(
        Context ctx,
        LsLanguageService ls,
        LinkedEditingRangeParams? @params) =>
        ls.ProvideLinkedEditingRange(ctx, @params!);

    /// <summary>Go: <c>func (s *Server) handleDefinition(ctx, ls, params)</c>（server.go:2161-2163）。</summary>
    private (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) HandleDefinition(
        Context ctx,
        LsLanguageService ls,
        DefinitionParams? @params) =>
        ls.ProvideDefinition(ctx, @params!.TextDocument.Uri, @params.Position);

    /// <summary>Go: <c>func (s *Server) handleSourceDefinition(ctx, ls, params)</c>（server.go:2165-2171）。</summary>
    private (LocationOrLocationsOrDefinitionLinksOrNull? Response, Exception? Error) HandleSourceDefinition(
        Context ctx,
        LsLanguageService ls,
        TextDocumentPositionParams? @params)
    {
        var (resp, err) = ls.ProvideSourceDefinition(ctx, @params!.TextDocument.Uri, @params.Position);
        if (err is not null)
        {
            return (null, err);
        }

        return (resp, null);
    }

    /// <summary>Go: <c>func (s *Server) handleTypeDefinition(ctx, ls, params)</c>（server.go:2173-2175）。</summary>
    private (LocationOrLocationsOrDefinitionLinksOrNull Response, Exception? Error) HandleTypeDefinition(
        Context ctx,
        LsLanguageService ls,
        TypeDefinitionParams? @params) =>
        ls.ProvideTypeDefinition(ctx, @params!.TextDocument.Uri, @params.Position);

    /// <summary>Go: <c>func (s *Server) handleCompletion(ctx, languageService, params)</c>（server.go:2177-2184）。</summary>
    private (CompletionItemsOrListOrNull Response, Exception? Error) HandleCompletion(
        Context ctx,
        LsLanguageService languageService,
        CompletionParams? @params) =>
        languageService.ProvideCompletion(ctx, @params!.TextDocument.Uri, @params.Position, @params.Context);

    /// <summary>Go: <c>func (s *Server) handleCompletionItemResolve(ctx, params, reqMsg)</c>（server.go:2186-2197）。</summary>
    private CompletionItem? HandleCompletionItemResolve(Context ctx, CompletionItem? @params, RequestMessage reqMsg)
    {
        try
        {
            var data = @params!.Data;
            if (data is null)
            {
                throw new InvalidOperationException("completion item data is nil");
            }

            var (languageService, err) = _session!.GetLanguageService(ctx, LsConv.LsConv.FileNameToDocumentURI(data.FileName));
            if (err is not null)
            {
                throw err;
            }

            var (resp, resolveError) = languageService!.ResolveCompletionItem(ctx, @params, data);
            if (resolveError is not null)
            {
                throw resolveError;
            }

            return resp;
        }
        catch (Exception e)
        {
            Recover(reqMsg, e);
            return null;
        }
    }

    /// <summary>Go: <c>func (s *Server) handleDocumentFormat(ctx, ls, params)</c>（server.go:2199-2205）。</summary>
    private (TextEditsOrNull Response, Exception? Error) HandleDocumentFormat(
        Context ctx,
        LsLanguageService ls,
        DocumentFormattingParams? @params) =>
        ls.ProvideFormatDocument(ctx, @params!.TextDocument.Uri, @params.Options);

    /// <summary>Go: <c>func (s *Server) handleDocumentRangeFormat(ctx, ls, params)</c>（server.go:2207-2214）。</summary>
    private (TextEditsOrNull Response, Exception? Error) HandleDocumentRangeFormat(
        Context ctx,
        LsLanguageService ls,
        DocumentRangeFormattingParams? @params) =>
        ls.ProvideFormatDocumentRange(ctx, @params!.TextDocument.Uri, @params.Options, @params.Range);

    /// <summary>Go: <c>func (s *Server) handleDocumentOnTypeFormat(ctx, ls, params)</c>（server.go:2216-2224）。</summary>
    private (TextEditsOrNull Response, Exception? Error) HandleDocumentOnTypeFormat(
        Context ctx,
        LsLanguageService ls,
        DocumentOnTypeFormattingParams? @params) =>
        ls.ProvideFormatDocumentOnType(ctx, @params!.TextDocument.Uri, @params.Options, @params.Position, @params.Ch);

    /// <summary>Go: <c>func (s *Server) handleWorkspaceSymbol(ctx, params, reqMsg)</c>（server.go:2226-2251）。</summary>
    private SymbolInformationsOrWorkspaceSymbolsOrNull HandleWorkspaceSymbol(Context ctx, WorkspaceSymbolParams? @params, RequestMessage reqMsg)
    {
        SymbolInformationsOrWorkspaceSymbolsOrNull? resp = null;
        Exception? lsErr = null;

        void ProvideSymbols(ProjectSnapshot snapshot, Program[] programs)
        {
            try
            {
                (resp, lsErr) = Ls.Ls.ProvideWorkspaceSymbols(
                    ctx,
                    programs,
                    snapshot.Converters(),
                    snapshot.UserPreferences(),
                    @params!.Query);
            }
            catch (Exception e)
            {
                Recover(reqMsg, e);
            }
        }

        if (@params!.TextDocument is not null
            && _session!.Config().WorkspaceSymbolsScope == LsUtil.LsUtil.WorkspaceSymbolsScopeCurrentProject)
        {
            var uri = @params.TextDocument.Uri;
            _session.WithSnapshotForDocument(ctx, uri, snapshot =>
            {
                var programs = Map(snapshot.GetLanguageServiceProjectsContainingFile(uri), p => p.GetProgram()!);
                ProvideSymbols(snapshot, programs);
            });
        }
        else
        {
            _session!.WithSnapshotLoadingProjectTree(ctx, null, snapshot =>
            {
                var programs = Map(snapshot.ProjectCollection.LanguageServiceProjects(), p => p.GetProgram()!);
                ProvideSymbols(snapshot, programs);
            });
        }

        if (lsErr is not null)
        {
            throw lsErr;
        }

        return resp!;
    }

    /// <summary>Go: <c>func (s *Server) handleDocumentSymbol(ctx, ls, params)</c>（server.go:2253-2255）。</summary>
    private (SymbolInformationsOrDocumentSymbolsOrNull Response, Exception? Error) HandleDocumentSymbol(
        Context ctx,
        LsLanguageService ls,
        DocumentSymbolParams? @params) =>
        ls.ProvideDocumentSymbols(ctx, @params!.TextDocument.Uri);

    /// <summary>Go: <c>func (s *Server) handleDocumentHighlight(ctx, ls, params)</c>（server.go:2257-2259）。</summary>
    private (DocumentHighlightsOrNull Response, Exception? Error) HandleDocumentHighlight(
        Context ctx,
        LsLanguageService ls,
        DocumentHighlightParams? @params) =>
        ls.ProvideDocumentHighlights(ctx, @params!.TextDocument.Uri, @params.Position);

    /// <summary>Go: <c>func (s *Server) handleMultiDocumentHighlight(ctx, ls, params)</c>（server.go:2261-2263）。</summary>
    private (MultiDocumentHighlightsOrNull Response, Exception? Error) HandleMultiDocumentHighlight(
        Context ctx,
        LsLanguageService ls,
        MultiDocumentHighlightParams? @params) =>
        ls.ProvideMultiDocumentHighlights(ctx, @params!.TextDocument.Uri, @params.Position, @params.FilesToSearch ?? []);

    /// <summary>Go: <c>func (s *Server) handleSelectionRange(ctx, ls, params)</c>（server.go:2265-2267）。</summary>
    private (SelectionRangesOrNull Response, Exception? Error) HandleSelectionRange(
        Context ctx,
        LsLanguageService ls,
        SelectionRangeParams? @params) =>
        ls.ProvideSelectionRanges(ctx, @params!);

    /// <summary>Go: <c>func (s *Server) handleCodeAction(ctx, ls, params)</c>（server.go:2269-2271）。</summary>
    private (CommandOrCodeActionArrayOrNull Response, Exception? Error) HandleCodeAction(
        Context ctx,
        LsLanguageService ls,
        CodeActionParams? @params) =>
        ls.ProvideCodeActions(ctx, @params!);

    /// <summary>Go: <c>func (s *Server) handleInlayHint(ctx, languageService, params)</c>（server.go:2273-2279）。</summary>
    private (InlayHintsOrNull Response, Exception? Error) HandleInlayHint(
        Context ctx,
        LsLanguageService languageService,
        InlayHintParams? @params) =>
        languageService.ProvideInlayHint(ctx, @params!);

    /// <summary>Go: <c>func (s *Server) handleCodeLens(ctx, ls, params)</c>（server.go:2281-2283）。</summary>
    private (CodeLensesOrNull Response, Exception? Error) HandleCodeLens(Context ctx, LsLanguageService ls, CodeLensParams? @params) =>
        ls.ProvideCodeLenses(ctx, @params!.TextDocument.Uri);

    /// <summary>Go: <c>func (s *Server) handleCodeLensResolve(ctx, codeLens, reqMsg)</c>（server.go:2285-2311）。</summary>
    private CodeLens? HandleCodeLensResolve(Context ctx, CodeLens? codeLens, RequestMessage reqMsg)
    {
        var (defaultLs, orchestrator, err) = GetLanguageServiceAndCrossProjectOrchestrator(ctx, codeLens!.Data!.Uri, reqMsg);
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ctx.CancellationToken);
        }

        if (err is not null)
        {
            // This can happen if a codeLens/resolve request comes in after a program change.
            // While it's true that handlers should latch onto a specific snapshot
            // while processing requests, we just set `Data.Uri` based on
            // some older snapshot's contents. The content could have been modified,
            // or the file itself could have been removed from the session entirely.
            // Note this won't bail out on every change, but will prevent crashing
            // based on non-existent files and line maps from shortened files.
            throw new ErrorCodeException(ErrorCode.ContentModified, ErrorCode.ContentModified.String());
        }

        try
        {
            var (resp, resolveError) = defaultLs!.ResolveCodeLens(
                ctx,
                codeLens,
                _initializationOptions.CodeLensShowLocationsCommandName,
                orchestrator!);
            if (resolveError is not null)
            {
                throw resolveError;
            }

            return resp;
        }
        catch (Exception e)
        {
            Recover(reqMsg, e);
            return null;
        }
    }

    /// <summary>Go: <c>func (s *Server) handlePrepareCallHierarchy(ctx, languageService, params)</c>（server.go:2313-2319）。</summary>
    private (CallHierarchyItemsOrNull Response, Exception? Error) HandlePrepareCallHierarchy(
        Context ctx,
        LsLanguageService languageService,
        CallHierarchyPrepareParams? @params) =>
        languageService.ProvidePrepareCallHierarchy(ctx, @params!.TextDocument.Uri, @params.Position);

    /// <summary>Go: <c>func (s *Server) handleCallHierarchyIncomingCalls(ctx, params, reqMsg)</c>（server.go:2321-2333）。</summary>
    private CallHierarchyIncomingCallsOrNull HandleCallHierarchyIncomingCalls(
        Context ctx,
        CallHierarchyIncomingCallsParams? @params,
        RequestMessage reqMsg)
    {
        var (defaultLs, orchestrator, err) = GetLanguageServiceAndCrossProjectOrchestrator(ctx, @params!.Item!.Uri, reqMsg);
        if (err is not null)
        {
            throw err;
        }

        var (resp, callsError) = defaultLs!.ProvideCallHierarchyIncomingCalls(ctx, @params.Item, orchestrator!);
        if (callsError is not null)
        {
            throw callsError;
        }

        return resp;
    }

    /// <summary>Go: <c>func (s *Server) handleCallHierarchyOutgoingCalls(ctx, params, _)</c>（server.go:2335-2345）。</summary>
    private CallHierarchyOutgoingCallsOrNull HandleCallHierarchyOutgoingCalls(
        Context ctx,
        CallHierarchyOutgoingCallsParams? @params,
        RequestMessage _)
    {
        var (languageService, err) = _session!.GetLanguageService(ctx, @params!.Item!.Uri);
        if (err is not null)
        {
            throw err;
        }

        var (resp, callsError) = languageService!.ProvideCallHierarchyOutgoingCalls(ctx, @params.Item);
        if (callsError is not null)
        {
            throw callsError;
        }

        return resp;
    }

    /// <summary>Go: <c>func (s *Server) handleSemanticTokensFull(ctx, ls, params)</c>（server.go:2347-2349）。</summary>
    private (SemanticTokensOrNull Response, Exception? Error) HandleSemanticTokensFull(
        Context ctx,
        LsLanguageService ls,
        SemanticTokensParams? @params) =>
        ls.ProvideSemanticTokens(ctx, @params!.TextDocument.Uri);

    /// <summary>Go: <c>func (s *Server) handleSemanticTokensRange(ctx, ls, params)</c>（server.go:2351-2353）。</summary>
    private (SemanticTokensOrNull Response, Exception? Error) HandleSemanticTokensRange(
        Context ctx,
        LsLanguageService ls,
        SemanticTokensRangeParams? @params) =>
        ls.ProvideSemanticTokensRange(ctx, @params!.TextDocument.Uri, @params.Range);
}
