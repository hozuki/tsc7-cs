// ported from: internal/lsp/server.go:1473-1955（handleInitialize / handleInitialized / handleShutdown / handleExit /
// didChangeConfiguration / didOpen / didChange / didSave / didClose / didChangeWatchedFiles / setTrace /
// setLogVerbosity / documentDiagnostic / willRenameFiles）
// 见 Server.cs 文件头。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `json.MarshalIndent(&s.clientCapabilities, "", "\t")` → `Json.MarshalIndent(...)` + `Encoding.UTF8.GetString`。
//
// 2) `lsproto.WithClientCapabilities(s.backgroundCtx, &s.clientCapabilities)` 在 Go 里是派生一个挂值的 ctx；
//    C# 侧该值存在 AsyncLocal（LsProto/Lsp.cs:543-551 已记录），所以这里落成一次
//    `LspProto.WithClientCapabilities(_clientCapabilities)`（在 dispatch 线程上设，效果同"给会话的背景 ctx 挂值"）。
//
// 3) `lspwatcher.New(s.fs, cb, s.logger)` → `TypeScript.LspWatcher.LspWatcher.New(_fs, cb, _logger)`。
//
// 4) `fswatch.Default().HasFastRecursiveBackend()` → `TypeScript.FsWatch.FsWatch.Default().HasFastRecursiveBackend()`
//    （必须全限定：`FsWatch` 既是命名空间又是静态类，裸名会命中命名空间，见 G1 约定）。
//
// 5) `handleDidChangeWorkspaceConfiguration` 里 Go 的 `params.Settings.(map[string]any)` 类型断言 → C# 的
//    `is Dictionary<string, object?>`（Json/Decode.cs 的"按 JSON 形状解"约定）。
//
// 6) `errgroup`/`panic`/`error` 的三处形态各自照第 1 条转换；`handleShutdown` 的返回值
//    `lsproto.ShutdownResponse{}` 在 C# 是 `new Null()`（生成物把 ShutdownResponse 映射成 Null）。
using System.Text;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Project;
using TypeScript.Vfs;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using Loc = TypeScript.Locale.Locale;
using LsLanguageService = TypeScript.Ls.LanguageService;
using Range = TypeScript.LsProto.Range;
using ProjectSession = TypeScript.Project.Session;

namespace TypeScript.Lsp;

public sealed partial class Server
{
    /// <summary>Go: <c>func (s *Server) handleInitialize(ctx, params, _) (lsproto.InitializeResponse, error)</c>（server.go:1472-1673）。</summary>
    private InitializeResult? HandleInitialize(Context ctx, InitializeParams? @params, RequestMessage _)
    {
        if (_initializeParams is not null)
        {
            throw new ErrorCodeException(ErrorCode.InvalidRequest, ErrorCode.InvalidRequest.String());
        }

        _initStarted = true;

        _initializeParams = @params;
        // The spec types initializationOptions as nullable; treat both null and an
        // absent value as empty options so the rest of the server can read fields
        // off s.initializationOptions without nil-checking the container.
        if (@params!.InitializationOptions is { InitializationOptions: not null } initOptions)
        {
            _initializationOptions = initOptions.InitializationOptions;
        }
        else
        {
            _initializationOptions = new InitializationOptions();
        }

        if (_initializationOptions.LogVerbosity is { } logVerbosity && Lsp.IsValidLogVerbosity(logVerbosity))
        {
            _logger.SetVerbosity(logVerbosity);
        }

        if (_initializationOptions.TrackFlakyDiagnostics is { } trackFlaky)
        {
            _flakeLogging = trackFlaky;
        }

        _clientCapabilities = @params.Capabilities!.Resolve();
        if (_clientCapabilities.Window.WorkDoneProgress)
        {
            _projectProgress = Lsp.NewProjectLoadingProgress(this, _progressDelay);
        }

        var capabilitiesJson = Encoding.UTF8.GetString(Json.Json.MarshalIndent(_clientCapabilities, "", "\t"));
        _logger.Info("Resolved client capabilities: " + capabilitiesJson);

        _positionEncoding = PositionEncodingKind.UTF16;
        if (_clientCapabilities.General.PositionEncodings is { } encodings
            && Array.IndexOf(encodings, PositionEncodingKind.UTF8) >= 0)
        {
            _positionEncoding = PositionEncodingKind.UTF8;
        }

        if (_initializeParams!.Locale is { } localeString)
        {
            Loc.TryParse(localeString, out _locale);
        }

        _initLocale = _locale;

        if (_startWatchdog is not null && @params.ProcessId.Integer is { } processId)
        {
            _startWatchdog(processId);
        }

        var response = new InitializeResult
        {
            ServerInfo = new ServerInfo
            {
                Name = "typescript",
                Version = TypeScript.Core.Core.Version(),
            },
            Capabilities = new ServerCapabilities
            {
                PositionEncoding = _positionEncoding,
                TextDocumentSync = new TextDocumentSyncOptionsOrKind
                {
                    Options = new TextDocumentSyncOptions
                    {
                        OpenClose = true,
                        Change = TextDocumentSyncKind.Incremental,
                        Save = new BooleanOrSaveOptions
                        {
                            Boolean = true,
                        },
                    },
                },
                HoverProvider = new BooleanOrHoverOptions
                {
                    Boolean = true,
                },
                DefinitionProvider = new BooleanOrDefinitionOptions
                {
                    Boolean = true,
                },
                TypeDefinitionProvider = new BooleanOrTypeDefinitionOptionsOrTypeDefinitionRegistrationOptions
                {
                    Boolean = true,
                },
                ReferencesProvider = new BooleanOrReferenceOptions
                {
                    Boolean = true,
                },
                ImplementationProvider = new BooleanOrImplementationOptionsOrImplementationRegistrationOptions
                {
                    Boolean = true,
                },
                DiagnosticProvider = new DiagnosticOptionsOrRegistrationOptions
                {
                    Options = new DiagnosticOptions
                    {
                        Identifier = "typescript",
                        InterFileDependencies = true,
                    },
                },
                CompletionProvider = new CompletionOptions
                {
                    TriggerCharacters = Ls.Ls.CompletionTriggerCharacters,
                    ResolveProvider = true,
                    CompletionItem = new ServerCompletionItemOptions
                    {
                        LabelDetailsSupport = true,
                    },
                },
                SignatureHelpProvider = new SignatureHelpOptions
                {
                    TriggerCharacters = Ls.Ls.SignatureHelpTriggerCharacters,
                    RetriggerCharacters = Ls.Ls.SignatureHelpRetriggerCharacters,
                },
                DocumentFormattingProvider = new BooleanOrDocumentFormattingOptions
                {
                    Boolean = true,
                },
                DocumentRangeFormattingProvider = new BooleanOrDocumentRangeFormattingOptions
                {
                    Boolean = true,
                },
                DocumentOnTypeFormattingProvider = new DocumentOnTypeFormattingOptions
                {
                    FirstTriggerCharacter = "{",
                    MoreTriggerCharacter = ["}", ";", "\n"],
                },
                WorkspaceSymbolProvider = new BooleanOrWorkspaceSymbolOptions
                {
                    Boolean = true,
                },
                DocumentSymbolProvider = new BooleanOrDocumentSymbolOptions
                {
                    Boolean = true,
                },
                FoldingRangeProvider = new BooleanOrFoldingRangeOptionsOrFoldingRangeRegistrationOptions
                {
                    Boolean = true,
                },
                RenameProvider = new BooleanOrRenameOptions
                {
                    RenameOptions = new RenameOptions
                    {
                        PrepareProvider = true,
                    },
                },
                DocumentHighlightProvider = new BooleanOrDocumentHighlightOptions
                {
                    Boolean = true,
                },
                SelectionRangeProvider = new BooleanOrSelectionRangeOptionsOrSelectionRangeRegistrationOptions
                {
                    Boolean = true,
                },
                LinkedEditingRangeProvider = new BooleanOrLinkedEditingRangeOptionsOrLinkedEditingRangeRegistrationOptions
                {
                    Boolean = true,
                },
                InlayHintProvider = new BooleanOrInlayHintOptionsOrInlayHintRegistrationOptions
                {
                    Boolean = true,
                },
                CodeLensProvider = new CodeLensOptions
                {
                    ResolveProvider = true,
                },
                CodeActionProvider = new BooleanOrCodeActionOptions
                {
                    CodeActionOptions = new CodeActionOptions
                    {
                        CodeActionKinds = SupportedCodeActionKinds(),
                    },
                },
                CallHierarchyProvider = new BooleanOrCallHierarchyOptionsOrCallHierarchyRegistrationOptions
                {
                    Boolean = true,
                },
                Experimental = new ExperimentalServerCapabilities
                {
                    CustomSourceDefinitionProvider = true,
                    CustomMultiDocumentHighlightProvider = true,
                },
                VSReferencesProvider = true,
                VSOnAutoInsertProvider = new VSOnAutoInsertOptions
                {
                    VSTriggerCharacters = [">"],
                },
                Workspace = new WorkspaceOptions
                {
                    FileOperations = new FileOperationOptions
                    {
                        WillRename = new FileOperationRegistrationOptions
                        {
                            Filters = _fileRenameFilters,
                        },
                    },
                },
                SemanticTokensProvider = new SemanticTokensOptionsOrRegistrationOptions
                {
                    Options = new SemanticTokensOptions
                    {
                        Legend = Ls.Ls.SemanticTokensLegend(_clientCapabilities.TextDocument.SemanticTokens),
                        Full = new BooleanOrSemanticTokensFullDelta
                        {
                            Boolean = true,
                        },
                        Range = new BooleanOrEmptyObject
                        {
                            Boolean = true,
                        },
                    },
                },
            },
        };

        return response;
    }

    /// <summary>Go: <c>func (s *Server) handleInitialized(ctx, params) error</c>（server.go:1675-1785）。</summary>
    private Exception? HandleInitialized(Context ctx, InitializedParams? _)
    {
        var disablePushDiagnostics = false;
        var enableTelemetry = false;
        if (_initializationOptions.DisablePushDiagnostics is { } dpd)
        {
            disablePushDiagnostics = dpd;
        }

        if (_initializationOptions.EnableTelemetry is { } et)
        {
            enableTelemetry = et;
        }

        var runExternalCode = false;
        if (_initializationOptions.RunExternalCode is { } rec)
        {
            runExternalCode = rec;
        }

        var hasDynamicWatchRegistration = _clientCapabilities.Workspace.DidChangeWatchedFiles.DynamicRegistration;
        if (hasDynamicWatchRegistration)
        {
            _logger.Logf("file watching: using LSP client-side watching (client supports dynamic registration)");
            _watchEnabled = true;
        }
        else if (TypeScript.FsWatch.FsWatch.Default().HasFastRecursiveBackend())
        {
            // The client cannot watch files itself, but the builtin watcher has a
            // backend with efficient recursive watching (Windows or FSEvents), so
            // fall back to watching files in-process.
            _logger.Logf("file watching: using builtin in-process watcher (client lacks dynamic watch registration)");
            _watchEnabled = true;
            _builtinWatcher = global::TypeScript.LspWatcher.LspWatcher.New(
                _fs,
                changes =>
                {
                    if (_session is not null)
                    {
                        _session.DidChangeWatchedFiles(_backgroundCtx, changes);
                    }
                },
                _logger);
        }
        else
        {
            // The client cannot watch files and the builtin watcher backend lacks
            // efficient recursive watching, so file watching is disabled.
            _logger.Logf("file watching: disabled (client lacks dynamic watch registration and builtin watcher backend is not fast-recursive)");
        }

        var cwd = _cwd;
        if (_clientCapabilities.Workspace.WorkspaceFolders
            && _initializeParams!.WorkspaceFolders is { WorkspaceFolders: { Length: 1 } workspaceFolders })
        {
            cwd = new DocumentUri(workspaceFolders[0]!.Uri.Value).FileName();
        }
        else if (_initializeParams!.RootUri.DocumentUri is { } rootUri)
        {
            cwd = rootUri.FileName();
        }
        else if (_initializeParams.RootPath?.String is { } rootPath)
        {
            cwd = rootPath;
        }

        if (!TSPath.TSPath.PathIsAbsolute(cwd))
        {
            cwd = _cwd;
        }

        _telemetryEnabled = enableTelemetry;

        // 见文件头第 2 条：Go 用 WithClientCapabilities 派生 ctx，C# 用 AsyncLocal（在每个请求的异步流里重设）。
        WithClientCapabilities(_clientCapabilities);

        _session = ProjectSession.NewSession(new SessionInit
        {
            BackgroundCtx = _backgroundCtx,
            Options = new SessionOptions
            {
                CurrentDirectory = cwd,
                DefaultLibraryPath = _defaultLibraryPath,
                TypingsLocation = _typingsLocation,
                PositionEncoding = _positionEncoding,
                WatchEnabled = _watchEnabled,
                LoggingEnabled = true,
                TelemetryEnabled = enableTelemetry,
                DebounceDelay = TimeSpan.FromMilliseconds(500),
                PushDiagnosticsEnabled = !disablePushDiagnostics,
                RunExternalCode = runExternalCode,
            },
            FS = _fs,
            Logger = _logger,
            Client = _client ?? this,
            NpmExecutor = this,
            Spawner = ContentMapperSpawner(),
            ContentMapperLogger = ContentMapperLogger(),
            ParseCache = _parseCache,
        });

        var userPreferences = RequestConfiguration(ctx);
        _session.InitializeWithUserConfig(userPreferences);

        var (_, registerError) = SendClientRequest(ctx, ClientRegisterCapabilityInfo, new RegistrationParams
        {
            Registrations =
            [
                new Registration
                {
                    Id = "typescript-config-watch-id",
                    RegisterOptions = new RegisterOptions
                    {
                        WorkspaceDidChangeConfiguration = new DidChangeConfigurationRegistrationOptions
                        {
                            Section = new StringOrStrings
                            {
                                Strings = ["js/ts", "typescript", "javascript", "editor"],
                            },
                        },
                    },
                },
            ],
        });
        if (registerError is not null)
        {
            return new InvalidOperationException("failed to register configuration change watcher", registerError);
        }

        // !!! temporary.
        // Remove when we have `handleDidChangeConfiguration`/implicit project config support
        // derived from 'js/ts.implicitProjectConfig.*'.
        if (_compilerOptionsForInferredProjects is not null)
        {
            _session.DidChangeCompilerOptionsForInferredProjects(ctx, _compilerOptionsForInferredProjects);
        }

        _session.StartPerformanceTelemetry();

        SignalInitComplete();
        return null;
    }

    /// <summary>Go: <c>func (s *Server) handleShutdown(ctx, _, _) (lsproto.ShutdownResponse, error)</c>（server.go:1787-1793）。</summary>
    private Null HandleShutdown(Context ctx, NoParams _, RequestMessage __)
    {
        _builtinWatcher?.Close();
        _session!.Close();
        return new Null();
    }

    /// <summary>Go: <c>func (s *Server) handleExit(ctx, _) error</c>（server.go:1795-1797）：返回 io.EOF 表示正常退出。</summary>
    private Exception? HandleExit(Context ctx, NoParams _) => new EndOfStreamException();

    /// <summary>Go: <c>func (s *Server) handleDidChangeWorkspaceConfiguration(ctx, params) error</c>（server.go:1799-1807）。</summary>
    private Exception? HandleDidChangeWorkspaceConfiguration(Context ctx, DidChangeConfigurationParams? @params)
    {
        if (@params!.Settings is null)
        {
            return null;
        }

        if (@params.Settings is Dictionary<string, object?> settings)
        {
            _session!.Configure(LsUtil.LsUtil.ParseUserPreferences(settings));
        }

        return null;
    }

    /// <summary>
    /// Go 的 <c>[]*lsproto.X</c> 在生成物里是 `X?[]`（元素可空），而消费方
    /// （<c>Session.DidChangeWatchedFiles</c>、<c>LsProto.CompareDiagnostics</c>）的形参是 `X[]`。
    /// 两者是**同一个运行时类型**（可空引用注解在运行时被擦除），经 `object` 转一次即可消除 CS8619，
    /// 且零开销：不是拷贝、不是投影，也不是 `!`（`!` 只压制顶层 null 警告，压不住元素级差异）。
    /// 只用于引用元素类型（本文件两处都是 LsProto 的类）。
    /// </summary>
    private static T[] AsNonNullArray<T>(T?[]? value) => value is null ? [] : (T[])(object)value;

    /// <summary>Go: <c>func (s *Server) handleDidOpen(ctx, params) error</c>（server.go:1809-1812）。</summary>
    private Exception? HandleDidOpen(Context ctx, DidOpenTextDocumentParams? @params)
    {
        var doc = @params!.TextDocument!;
        _session!.DidOpenFile(ctx, doc.Uri, doc.Version, doc.Text, doc.LanguageId);
        return null;
    }

    /// <summary>Go: <c>func (s *Server) handleDidChange(ctx, params) error</c>（server.go:1814-1817）。</summary>
    private Exception? HandleDidChange(Context ctx, DidChangeTextDocumentParams? @params)
    {
        _session!.DidChangeFile(
            ctx,
            @params!.TextDocument.Uri,
            @params.TextDocument.Version,
            @params.ContentChanges ?? []);
        return null;
    }

    /// <summary>Go: <c>func (s *Server) handleDidSave(ctx, params) error</c>（server.go:1819-1822）。</summary>
    private Exception? HandleDidSave(Context ctx, DidSaveTextDocumentParams? @params)
    {
        _session!.DidSaveFile(ctx, @params!.TextDocument.Uri);
        return null;
    }

    /// <summary>Go: <c>func (s *Server) handleDidClose(ctx, params) error</c>（server.go:1824-1827）。</summary>
    private Exception? HandleDidClose(Context ctx, DidCloseTextDocumentParams? @params)
    {
        _session!.DidCloseFile(ctx, @params!.TextDocument.Uri);
        return null;
    }

    /// <summary>Go: <c>func (s *Server) handleDidChangeWatchedFiles(ctx, params) error</c>（server.go:1829-1832）。</summary>
    private Exception? HandleDidChangeWatchedFiles(Context ctx, DidChangeWatchedFilesParams? @params)
    {
        _session!.DidChangeWatchedFiles(ctx, AsNonNullArray(@params!.Changes));
        return null;
    }

    /// <summary>Go: <c>func (s *Server) handleSetTrace(_, _) error</c>（server.go:1834-1840）：故意是 no-op。</summary>
    private Exception? HandleSetTrace(Context _, SetTraceParams? __) => null;

    /// <summary>Go: <c>func (s *Server) handleSetLogVerbosity(_, params) error</c>（server.go:1842-1848）。</summary>
    private Exception? HandleSetLogVerbosity(Context _, SetLogVerbosityParams? @params)
    {
        if (!Lsp.IsValidLogVerbosity(@params!.Verbosity))
        {
            throw new ErrorCodeException(
                ErrorCode.InvalidParams,
                $"{ErrorCode.InvalidParams.String()}: invalid log verbosity {(int)@params.Verbosity}");
        }

        _logger.SetVerbosity(@params.Verbosity);
        return null;
    }

    /// <summary>Go: <c>func (s *Server) handleDocumentDiagnostic(ctx, languageService, params)</c>（server.go:1850-1905）。</summary>
    private (RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport Response, Exception? Error) HandleDocumentDiagnostic(
        Context ctx,
        LsLanguageService languageService,
        DocumentDiagnosticParams? @params)
    {
        ctx = WithCheckerLifetime(ctx, CheckerLifetime.Diagnostics);
        if (_flakeLogging == DiagnosticFlakeLogLevel.Off)
        {
            return languageService.ProvideDiagnostics(ctx, @params!.TextDocument.Uri);
        }

        var (direct, err) = languageService.ProvideDiagnostics(ctx, @params!.TextDocument.Uri);
        if (err is not null)
        {
            return (direct, err);
        }

        languageService.GetProgram()!.Emit(ctx, new global::TypeScript.Compiler.EmitOptions
        {
            WriteFile = static (fileName, text, data) =>
            {
                // do nothing
            },
        });
        var (secondary, err2) = languageService.ProvideDiagnostics(ctx, @params.TextDocument.Uri);
        if (err2 is not null)
        {
            return (direct, err);
        }

        var (missingFromPre, missingFromPost) = CompareDiagnostics(
            AsNonNullArray(direct.FullDocumentDiagnosticReport!.Items),
            AsNonNullArray(secondary.FullDocumentDiagnosticReport!.Items));
        if (missingFromPre.Length == 0 && missingFromPost.Length == 0)
        {
            return (direct, err);
        }

        var diff = GenerateDiagnosticDiffString(missingFromPre, missingFromPost, static d => d.AsString());

        _logger.Error(diff);

        if (_telemetryEnabled)
        {
            var sanitizedDiff = GenerateDiagnosticDiffString(missingFromPre, missingFromPost, static d => d.CodeAsString());
            _ = SendNotification(TelemetryEventInfo, new RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull
            {
                RequestFailureTelemetryEvent = new RequestFailureTelemetryEvent
                {
                    Properties = new RequestFailureTelemetryProperties
                    {
                        ErrorCode = ErrorCode.InternalError.String(),
                        RequestMethod = "textDocument.diagnostic.flakeLog",
                        Stack = sanitizedDiff,
                    },
                },
            });
        }

        if (_flakeLogging == DiagnosticFlakeLogLevel.Panic)
        {
            throw new InvalidOperationException("flaky diagnostic(s) logged:\n" + diff);
        }

        return (direct, err);
    }

    /// <summary>Go: <c>func generateDiagnosticDiffString(missingFromPre, missingFromPost, stringifier) string</c>（server.go:1907-1916）。</summary>
    private static string GenerateDiagnosticDiffString(
        Diagnostic[] missingFromPre,
        Diagnostic[] missingFromPost,
        Func<Diagnostic, string> stringifier)
    {
        var b = new StringBuilder();
        foreach (var elem in missingFromPre)
        {
            b.Append($"Diagnostic {stringifier(elem)} was present after emit but not before emit\n");
        }

        foreach (var elem in missingFromPost)
        {
            b.Append($"Diagnostic {stringifier(elem)} was present before emit but not after emit\n");
        }

        return b.ToString();
    }

    /// <summary>Go: <c>func (s *Server) handleWillRenameFiles(ctx, params, msg)</c>（server.go:2030-2032）。</summary>
    private WorkspaceEditOrNull HandleWillRenameFiles(Context ctx, RenameFilesParams? @params, RequestMessage msg) =>
        HandleWillRenameFilesWorker(ctx, @params!, msg, sendRenameFile: false);

    /// <summary>Go: <c>func (s *Server) handleWillRenameFilesWorker(ctx, params, _, sendRenameFile)</c>（server.go:2036-2137）。</summary>
    private WorkspaceEditOrNull HandleWillRenameFilesWorker(
        Context ctx,
        RenameFilesParams @params,
        RequestMessage _,
        bool sendRenameFile)
    {
        if (@params.Files is not { Length: > 0 })
        {
            return new WorkspaceEditOrNull();
        }

        var uris = new List<DocumentUri>(@params.Files.Length);
        foreach (var file in @params.Files)
        {
            uris.Add(file!.OldUri);
        }

        if (uris.Count == 0)
        {
            return new WorkspaceEditOrNull();
        }

        var services = _session!.GetLanguageServicesForDocumentsLoadingProjectTree(ctx, [.. uris]);

        // Go: `type editKey struct { uri lsproto.DocumentUri; range_ lsproto.Range }`。`Range` 是含指针字段的
        // 结构体（Go 按地址比较），C# 的 `Range` 是 class（默认引用比较），所以 `(DocumentUri, Range)` 元组
        // 的相等语义与 Go 一致（DocumentUri 值比较 + Range 引用比较）。
        var seenEdits = new Dictionary<(DocumentUri Uri, Range Range), string>();
        var seenRenames = new HashSet<DocumentUri>();
        var documentChanges = new List<TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile>();

        foreach (var languageService in services)
        {
            foreach (var file in @params.Files)
            {
                var changes = languageService.GetEditsForFileRename(ctx, file!.OldUri, file.NewUri);
                foreach (var change in changes)
                {
                    if (change.RenameFile is not null)
                    {
                        if (seenRenames.Add(change.RenameFile.OldUri))
                        {
                            documentChanges.Add(change);
                        }
                    }
                    else if (change.TextDocumentEdit is not null)
                    {
                        var uri = change.TextDocumentEdit.TextDocument.Uri;
                        var deduped = new List<TextEditOrAnnotatedTextEditOrSnippetTextEdit>();
                        foreach (var edit in change.TextDocumentEdit.Edits ?? [])
                        {
                            if (edit.TextEdit is not null)
                            {
                                var key = (uri, edit.TextEdit.Range);
                                if (seenEdits.TryGetValue(key, out var prev) && prev == edit.TextEdit.NewText)
                                {
                                    continue;
                                }

                                seenEdits[key] = edit.TextEdit.NewText;
                            }

                            deduped.Add(edit);
                        }

                        if (deduped.Count > 0)
                        {
                            documentChanges.Add(new TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile
                            {
                                TextDocumentEdit = new TextDocumentEdit
                                {
                                    TextDocument = change.TextDocumentEdit.TextDocument,
                                    Edits = [.. deduped],
                                },
                            });
                        }
                    }
                }
            }
        }

        if (sendRenameFile)
        {
            foreach (var file in @params.Files)
            {
                documentChanges.Add(new TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile
                {
                    RenameFile = new RenameFile
                    {
                        Kind = new StringLiteralRename(),
                        OldUri = file!.OldUri,
                        NewUri = file.NewUri,
                    },
                });
            }
        }

        if (documentChanges.Count == 0)
        {
            return new WorkspaceEditOrNull();
        }

        if (Ls.Ls.ClientSupportsDocumentChanges(ctx))
        {
            return new WorkspaceEditOrNull
            {
                WorkspaceEdit = new WorkspaceEdit
                {
                    DocumentChanges = [.. documentChanges],
                },
            };
        }

        var changes2 = new Dictionary<DocumentUri, TextEdit?[]?>();
        foreach (var change in documentChanges)
        {
            if (change.TextDocumentEdit is not null)
            {
                var uri = change.TextDocumentEdit.TextDocument.Uri;
                var list = changes2.TryGetValue(uri, out var existing) ? new List<TextEdit?>(existing ?? []) : [];
                foreach (var edit in change.TextDocumentEdit.Edits ?? [])
                {
                    if (edit.TextEdit is not null)
                    {
                        list.Add(edit.TextEdit);
                    }
                }

                changes2[uri] = [.. list];
            }
        }

        return new WorkspaceEditOrNull
        {
            WorkspaceEdit = new WorkspaceEdit
            {
                Changes = changes2,
            },
        };
    }
}
