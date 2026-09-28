// ported from: internal/lsp/server.go:262-873 + 708-900 + 2368-2480（project.Client / ata.NpmExecutor 的实现、
// 内容映射器动态注册、配置请求、若干无 user 可见行为的刷新请求）
// 见 Server.cs 文件头。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `project.Client` 的每个方法在 Go 里返回 error，C# 的接口是 `void`（Project/Client.cs 文件头第 1 条），
//    所以"失败"在这里是**抛异常**；调用点（project.Session）用 try/catch 接住。
//
// 2) `ate.NpmExecutor.NpmInstall(cwd string, args []string) ([]byte, error)` → 返回 `byte[]`，失败抛异常。
//
// 3) `SendTelemetry` 在 telemetry 关闭时 Go 会 panic → C# 用 `Debug.Fail`（根命名空间 TypeScript 的 Debug 类，
//    docs/architecture.md §3.2；`Debug` 在 TypeScript.* 里可直接裸名使用）。
//
// 4) `lsproto.DidChangeWatchedFilesRegistrationOptions{Watchers: watchers}`：Go 的 `watchers` 是
//    `[]*lsproto.FileSystemWatcher`；C# 的生成物是 `FileSystemWatcher?[]?`（元素可空）。
//
// 5) `*(string)` 形式的指针字面量 `new("file")` → C# 直接给字符串；`*bool` → C# 的 `bool?`。
//    生成物里 `RegisterOptions` 的字段名与 Go 的一致（server.go 的注册表逐字对照即可）。
//
// 6) `map[string]any`（Go 的 json 解码结果）→ C# 的 `Dictionary<string, object?>`（Json/Decode.cs:18-19 的
//    "按 JSON 形状解"约定；`Decode<object?>()` 对 JSON 对象就是 Dictionary）。
//    所以 `RequestConfiguration` 里 Go 的 `userPrefs.(map[string]any)` 类型断言在 C# 里是
//    `is Dictionary<string, object?> config`。
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Project;
using static TypeScript.LsProto.LsProto;
using FileSystemWatcher = TypeScript.LsProto.FileSystemWatcher;
using DiagnosticMessage = TypeScript.Diagnostics.Message;
using Loc = TypeScript.Locale.Locale;
using ProjectSession = TypeScript.Project.Session;
using TelemetryEvent = TypeScript.LsProto.RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull;

namespace TypeScript.Lsp;

public sealed partial class Server
{
    /// <summary>Go: <c>const contentMapper*RegistrationID</c>（server.go:348-374）。</summary>
    internal const string ContentMapperDidOpenRegistrationId = "content-mapper-did-open";
    internal const string ContentMapperDidChangeRegistrationId = "content-mapper-did-change";
    internal const string ContentMapperDidCloseRegistrationId = "content-mapper-did-close";
    internal const string ContentMapperDiagnosticRegistrationId = "content-mapper-diagnostic";
    internal const string ContentMapperHoverRegistrationId = "content-mapper-hover";
    internal const string ContentMapperSignatureHelpRegistrationId = "content-mapper-signature-help";
    internal const string ContentMapperDefinitionRegistrationId = "content-mapper-definition";
    internal const string ContentMapperTypeDefinitionRegistrationId = "content-mapper-type-definition";
    internal const string ContentMapperImplementationRegistrationId = "content-mapper-implementation";
    internal const string ContentMapperReferencesRegistrationId = "content-mapper-references";
    internal const string ContentMapperDocumentHighlightRegistrationId = "content-mapper-document-highlight";
    internal const string ContentMapperCompletionRegistrationId = "content-mapper-completion";
    internal const string ContentMapperRenameRegistrationId = "content-mapper-rename";
    internal const string ContentMapperSemanticTokensRegistrationId = "content-mapper-semantic-tokens";
    internal const string ContentMapperDocumentSymbolRegistrationId = "content-mapper-document-symbol";
    internal const string ContentMapperFoldingRangeRegistrationId = "content-mapper-folding-range";
    internal const string ContentMapperSelectionRangeRegistrationId = "content-mapper-selection-range";
    internal const string ContentMapperInlayHintRegistrationId = "content-mapper-inlay-hint";
    internal const string ContentMapperCodeLensRegistrationId = "content-mapper-code-lens";
    internal const string ContentMapperCodeActionRegistrationId = "content-mapper-code-action";
    internal const string ContentMapperFormattingRegistrationId = "content-mapper-formatting";
    internal const string ContentMapperRangeFormattingRegistrationId = "content-mapper-range-formatting";
    internal const string ContentMapperOnTypeFormattingRegistrationId = "content-mapper-on-type-formatting";
    internal const string ContentMapperLinkedEditingRegistrationId = "content-mapper-linked-editing";
    internal const string ContentMapperCallHierarchyRegistrationId = "content-mapper-call-hierarchy";
    internal const string ContentMapperWillRenameFilesRegistrationId = "content-mapper-will-rename-files";

    /// <summary>Go: <c>func supportedCodeActionKinds() []lsproto.CodeActionKind</c>（server.go:376-385）。</summary>
    private static CodeActionKind[] SupportedCodeActionKinds() =>
    [
        CodeActionKind.QuickFix,
        CodeActionKind.SourceOrganizeImportsTs,
        CodeActionKind.SourceRemoveUnusedImportsTs,
        CodeActionKind.SourceSortImportsTs,
        CodeActionKind.SourceFixAllTs,
    ];

    /// <summary>Go: <c>func (s *Server) supportsContentMapperRegistration(id string) bool</c>（server.go:387-435）。</summary>
    private bool SupportsContentMapperRegistration(string id)
    {
        switch (id)
        {
            case ContentMapperDidOpenRegistrationId:
            case ContentMapperDidChangeRegistrationId:
            case ContentMapperDidCloseRegistrationId:
                return _clientCapabilities.TextDocument.Synchronization.DynamicRegistration;
            case ContentMapperDiagnosticRegistrationId:
                return _clientCapabilities.TextDocument.Diagnostic.DynamicRegistration;
            case ContentMapperHoverRegistrationId:
                return _clientCapabilities.TextDocument.Hover.DynamicRegistration;
            case ContentMapperSignatureHelpRegistrationId:
                return _clientCapabilities.TextDocument.SignatureHelp.DynamicRegistration;
            case ContentMapperDefinitionRegistrationId:
                return _clientCapabilities.TextDocument.Definition.DynamicRegistration;
            case ContentMapperTypeDefinitionRegistrationId:
                return _clientCapabilities.TextDocument.TypeDefinition.DynamicRegistration;
            case ContentMapperImplementationRegistrationId:
                return _clientCapabilities.TextDocument.Implementation.DynamicRegistration;
            case ContentMapperReferencesRegistrationId:
                return _clientCapabilities.TextDocument.References.DynamicRegistration;
            case ContentMapperDocumentHighlightRegistrationId:
                return _clientCapabilities.TextDocument.DocumentHighlight.DynamicRegistration;
            case ContentMapperCompletionRegistrationId:
                return _clientCapabilities.TextDocument.Completion.DynamicRegistration;
            case ContentMapperRenameRegistrationId:
                return _clientCapabilities.TextDocument.Rename.DynamicRegistration;
            case ContentMapperSemanticTokensRegistrationId:
                return _clientCapabilities.TextDocument.SemanticTokens.DynamicRegistration;
            case ContentMapperDocumentSymbolRegistrationId:
                return _clientCapabilities.TextDocument.DocumentSymbol.DynamicRegistration;
            case ContentMapperFoldingRangeRegistrationId:
                return _clientCapabilities.TextDocument.FoldingRange.DynamicRegistration;
            case ContentMapperSelectionRangeRegistrationId:
                return _clientCapabilities.TextDocument.SelectionRange.DynamicRegistration;
            case ContentMapperInlayHintRegistrationId:
                return _clientCapabilities.TextDocument.InlayHint.DynamicRegistration;
            case ContentMapperCodeLensRegistrationId:
                return _clientCapabilities.TextDocument.CodeLens.DynamicRegistration;
            case ContentMapperCodeActionRegistrationId:
                return _clientCapabilities.TextDocument.CodeAction.DynamicRegistration;
            case ContentMapperFormattingRegistrationId:
                return _clientCapabilities.TextDocument.Formatting.DynamicRegistration;
            case ContentMapperRangeFormattingRegistrationId:
                return _clientCapabilities.TextDocument.RangeFormatting.DynamicRegistration;
            case ContentMapperOnTypeFormattingRegistrationId:
                return _clientCapabilities.TextDocument.OnTypeFormatting.DynamicRegistration;
            case ContentMapperLinkedEditingRegistrationId:
                return _clientCapabilities.TextDocument.LinkedEditingRange.DynamicRegistration;
            case ContentMapperCallHierarchyRegistrationId:
                return _clientCapabilities.TextDocument.CallHierarchy.DynamicRegistration;
            case ContentMapperWillRenameFilesRegistrationId:
                return _clientCapabilities.Workspace.FileOperations.DynamicRegistration &&
                    _clientCapabilities.Workspace.FileOperations.WillRename;
            default:
                return false;
        }
    }

    /// <summary>
    /// Go: <c>func (s *Server) RegisterContentMapperExtensions(ctx, extensions []string) error</c>（server.go:437-705）。
    /// </summary>
    public void RegisterContentMapperExtensions(Context ctx, string[] extensions)
    {
        if (!_clientCapabilities.TextDocument.Synchronization.DynamicRegistration)
        {
            return;
        }

        lock (_contentMapperRegistrationMu)
        {
            if (_contentMapperExtensionsRegistered)
            {
                var unregistrations = new List<Unregistration>
                {
                    new() { Id = ContentMapperDidOpenRegistrationId, Method = (string)Method.TextDocumentDidOpen },
                    new() { Id = ContentMapperDidChangeRegistrationId, Method = (string)Method.TextDocumentDidChange },
                    new() { Id = ContentMapperDidCloseRegistrationId, Method = (string)Method.TextDocumentDidClose },
                    new() { Id = ContentMapperDiagnosticRegistrationId, Method = (string)Method.TextDocumentDiagnostic },
                    new() { Id = ContentMapperHoverRegistrationId, Method = (string)Method.TextDocumentHover },
                    new() { Id = ContentMapperSignatureHelpRegistrationId, Method = (string)Method.TextDocumentSignatureHelp },
                    new() { Id = ContentMapperDefinitionRegistrationId, Method = (string)Method.TextDocumentDefinition },
                    new() { Id = ContentMapperTypeDefinitionRegistrationId, Method = (string)Method.TextDocumentTypeDefinition },
                    new() { Id = ContentMapperImplementationRegistrationId, Method = (string)Method.TextDocumentImplementation },
                    new() { Id = ContentMapperReferencesRegistrationId, Method = (string)Method.TextDocumentReferences },
                    new() { Id = ContentMapperDocumentHighlightRegistrationId, Method = (string)Method.TextDocumentDocumentHighlight },
                    new() { Id = ContentMapperCompletionRegistrationId, Method = (string)Method.TextDocumentCompletion },
                    new() { Id = ContentMapperRenameRegistrationId, Method = (string)Method.TextDocumentRename },
                    new() { Id = ContentMapperSemanticTokensRegistrationId, Method = (string)Method.TextDocumentSemanticTokens },
                    new() { Id = ContentMapperDocumentSymbolRegistrationId, Method = (string)Method.TextDocumentDocumentSymbol },
                    new() { Id = ContentMapperFoldingRangeRegistrationId, Method = (string)Method.TextDocumentFoldingRange },
                    new() { Id = ContentMapperSelectionRangeRegistrationId, Method = (string)Method.TextDocumentSelectionRange },
                    new() { Id = ContentMapperInlayHintRegistrationId, Method = (string)Method.TextDocumentInlayHint },
                    new() { Id = ContentMapperCodeLensRegistrationId, Method = (string)Method.TextDocumentCodeLens },
                    new() { Id = ContentMapperCodeActionRegistrationId, Method = (string)Method.TextDocumentCodeAction },
                    new() { Id = ContentMapperFormattingRegistrationId, Method = (string)Method.TextDocumentFormatting },
                    new() { Id = ContentMapperRangeFormattingRegistrationId, Method = (string)Method.TextDocumentRangeFormatting },
                    new() { Id = ContentMapperOnTypeFormattingRegistrationId, Method = (string)Method.TextDocumentOnTypeFormatting },
                    new() { Id = ContentMapperLinkedEditingRegistrationId, Method = (string)Method.TextDocumentLinkedEditingRange },
                    new() { Id = ContentMapperCallHierarchyRegistrationId, Method = (string)Method.TextDocumentPrepareCallHierarchy },
                    new() { Id = ContentMapperWillRenameFilesRegistrationId, Method = (string)Method.WorkspaceWillRenameFiles },
                };
                unregistrations.RemoveAll(r => !SupportsContentMapperRegistration(r.Id));
                var (_, unregisterError) = SendClientRequest(ctx, ClientUnregisterCapabilityInfo, new UnregistrationParams
                {
                    Unregisterations = [.. unregistrations],
                });
                if (unregisterError is not null)
                {
                    throw new InvalidOperationException("failed to unregister content mapper text document sync", unregisterError);
                }

                _contentMapperExtensionsRegistered = false;
            }

            if (extensions.Length == 0)
            {
                return;
            }

            var filters = new List<TextDocumentFilterLanguageOrSchemeOrPattern>(extensions.Length);
            foreach (var ext in extensions)
            {
                filters.Add(new TextDocumentFilterLanguageOrSchemeOrPattern
                {
                    Pattern = new TextDocumentFilterPattern
                    {
                        Pattern = new PatternOrRelativePattern { Pattern = "**/*" + ext },
                    },
                });
            }

            var selector = new DocumentSelectorOrNull { DocumentSelector = [.. filters] };
            var contentMapperFileRenameFilters = new List<FileOperationFilter>(extensions.Length);
            foreach (var extension in extensions)
            {
                contentMapperFileRenameFilters.Add(new FileOperationFilter
                {
                    Scheme = "file",
                    Pattern = new FileOperationPattern
                    {
                        Glob = "**/*" + extension,
                    },
                });
            }

            var registrations = new List<Registration>
            {
                new()
                {
                    Id = ContentMapperDidOpenRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentDidOpen = new TextDocumentRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperDidChangeRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentDidChange = new TextDocumentChangeRegistrationOptions
                        {
                            DocumentSelector = selector,
                            SyncKind = TextDocumentSyncKind.Incremental,
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperDidCloseRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentDidClose = new TextDocumentRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperDiagnosticRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentDiagnostic = new DiagnosticRegistrationOptions
                        {
                            DocumentSelector = selector,
                            Identifier = "typescript",
                            InterFileDependencies = true,
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperHoverRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentHover = new HoverRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperSignatureHelpRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentSignatureHelp = new SignatureHelpRegistrationOptions
                        {
                            DocumentSelector = selector,
                            TriggerCharacters = Ls.Ls.SignatureHelpTriggerCharacters,
                            RetriggerCharacters = Ls.Ls.SignatureHelpRetriggerCharacters,
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperDefinitionRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentDefinition = new DefinitionRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperTypeDefinitionRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentTypeDefinition = new TypeDefinitionRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperImplementationRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentImplementation = new ImplementationRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperReferencesRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentReferences = new ReferenceRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperDocumentHighlightRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentDocumentHighlight = new DocumentHighlightRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperCompletionRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentCompletion = new CompletionRegistrationOptions
                        {
                            DocumentSelector = selector,
                            TriggerCharacters = Ls.Ls.CompletionTriggerCharacters,
                            ResolveProvider = true,
                            CompletionItem = new ServerCompletionItemOptions
                            {
                                LabelDetailsSupport = true,
                            },
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperRenameRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentRename = new RenameRegistrationOptions
                        {
                            DocumentSelector = selector,
                            PrepareProvider = true,
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperSemanticTokensRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentSemanticTokens = new SemanticTokensRegistrationOptions
                        {
                            DocumentSelector = selector,
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
                new()
                {
                    Id = ContentMapperDocumentSymbolRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentDocumentSymbol = new DocumentSymbolRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperFoldingRangeRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentFoldingRange = new FoldingRangeRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperSelectionRangeRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentSelectionRange = new SelectionRangeRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperInlayHintRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentInlayHint = new InlayHintRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperCodeLensRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentCodeLens = new CodeLensRegistrationOptions
                        {
                            DocumentSelector = selector,
                            ResolveProvider = true,
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperCodeActionRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentCodeAction = new CodeActionRegistrationOptions
                        {
                            DocumentSelector = selector,
                            CodeActionKinds = SupportedCodeActionKinds(),
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperFormattingRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentFormatting = new DocumentFormattingRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperRangeFormattingRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentRangeFormatting = new DocumentRangeFormattingRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperOnTypeFormattingRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentOnTypeFormatting = new DocumentOnTypeFormattingRegistrationOptions
                        {
                            DocumentSelector = selector,
                            FirstTriggerCharacter = "{",
                            MoreTriggerCharacter = ["}", ";", "\n"],
                        },
                    },
                },
                new()
                {
                    Id = ContentMapperLinkedEditingRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentLinkedEditingRange = new LinkedEditingRangeRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperCallHierarchyRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        TextDocumentPrepareCallHierarchy = new CallHierarchyRegistrationOptions { DocumentSelector = selector },
                    },
                },
                new()
                {
                    Id = ContentMapperWillRenameFilesRegistrationId,
                    RegisterOptions = new RegisterOptions
                    {
                        WorkspaceWillRenameFiles = new FileOperationRegistrationOptions { Filters = [.. contentMapperFileRenameFilters] },
                    },
                },
            };
            registrations.RemoveAll(r => !SupportsContentMapperRegistration(r.Id));
            var (_, registerError) = SendClientRequest(ctx, ClientRegisterCapabilityInfo, new RegistrationParams
            {
                Registrations = [.. registrations],
            });
            if (registerError is not null)
            {
                throw new InvalidOperationException("failed to register content mapper text document sync", registerError);
            }

            _contentMapperExtensionsRegistered = true;
        }
    }

    /// <summary>Go: <c>func (s *Server) WatchFiles(ctx, id, watchers) error</c>（server.go:262-285）——实现 project.Client。</summary>
    public void WatchFiles(Context ctx, WatcherID id, FileSystemWatcher[] watchers)
    {
        if (_builtinWatcher is not null)
        {
            try
            {
                _builtinWatcher.WatchFiles(id.Value, watchers);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("failed to register file watcher", e);
            }

            _watchers.Add(id);
            return;
        }

        var (_, err) = SendClientRequest(ctx, ClientRegisterCapabilityInfo, new RegistrationParams
        {
            Registrations =
            [
                new Registration
                {
                    Id = id.Value,
                    RegisterOptions = new RegisterOptions
                    {
                        WorkspaceDidChangeWatchedFiles = new DidChangeWatchedFilesRegistrationOptions
                        {
                            Watchers = watchers,
                        },
                    },
                },
            ],
        });
        if (err is not null)
        {
            throw new InvalidOperationException("failed to register file watcher", err);
        }

        _watchers.Add(id);
    }

    /// <summary>Go: <c>func (s *Server) UnwatchFiles(ctx, id) error</c>（server.go:287-323）——实现 project.Client。</summary>
    public void UnwatchFiles(Context ctx, WatcherID id)
    {
        if (_builtinWatcher is not null)
        {
            if (!_watchers.Has(id))
            {
                throw new InvalidOperationException($"no file watcher exists with ID {id.Value}");
            }

            try
            {
                _builtinWatcher.UnwatchFiles(id.Value);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("failed to unregister file watcher", e);
            }

            _watchers.Delete(id);
            return;
        }

        if (_watchers.Has(id))
        {
            var (_, err) = SendClientRequest(ctx, ClientUnregisterCapabilityInfo, new UnregistrationParams
            {
                Unregisterations =
                [
                    new Unregistration
                    {
                        Id = id.Value,
                        Method = (string)Method.WorkspaceDidChangeWatchedFiles,
                    },
                ],
            });
            if (err is not null)
            {
                throw new InvalidOperationException("failed to unregister file watcher", err);
            }

            _watchers.Delete(id);
            return;
        }

        throw new InvalidOperationException($"no file watcher exists with ID {id.Value}");
    }

    /// <summary>Go: <c>func (s *Server) RefreshDiagnostics(ctx) error</c>（server.go:708-724）——实现 project.Client。</summary>
    public void RefreshDiagnostics(Context ctx)
    {
        if (!_clientCapabilities.Workspace.Diagnostics.RefreshSupport)
        {
            return;
        }

        if (ctx.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ctx.CancellationToken);
        }

        // Fire-and-forget: the client always returns null, and waiting for the response
        // can cause the server to hang if the client is slow or unresponsive.
        // Any response from the client will be silently ignored by the read loop.
        var err = SendClientRequestFireAndForget(WorkspaceDiagnosticRefreshInfo, new NoParams());
        if (err is not null)
        {
            throw new InvalidOperationException("failed to refresh diagnostics", err);
        }
    }

    /// <summary>Go: <c>func (s *Server) PublishDiagnostics(ctx, params) error</c>（server.go:726-729）——实现 project.Client。</summary>
    public void PublishDiagnostics(Context ctx, PublishDiagnosticsParams @params)
    {
        var err = SendNotification(TextDocumentPublishDiagnosticsInfo, @params);
        if (err is not null)
        {
            throw err;
        }
    }

    /// <summary>Go: <c>func (s *Server) SendTelemetry(ctx, telemetry) error</c>（server.go:731-737）——实现 project.Client。</summary>
    public void SendTelemetry(Context ctx, TelemetryEvent telemetry)
    {
        if (!_telemetryEnabled)
        {
            Debug.Fail("SendTelemetry called with telemetry disabled");
        }

        var err = SendNotification(TelemetryEventInfo, telemetry);
        if (err is not null)
        {
            throw err;
        }
    }

    /// <summary>Go: <c>func (s *Server) IsActive() bool</c>（server.go:739-742）——实现 project.Client。</summary>
    public bool IsActive()
    {
        var last = Interlocked.Read(ref _lastRequestTimeMs);
        return last == 0 || (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last) <= 60_000;
    }

    /// <summary>Go: <c>func (s *Server) RefreshInlayHints(ctx) error</c>（server.go:744-753）。</summary>
    public void RefreshInlayHints(Context ctx)
    {
        if (!_clientCapabilities.Workspace.InlayHint.RefreshSupport)
        {
            return;
        }

        var err = SendClientRequestFireAndForget(WorkspaceInlayHintRefreshInfo, new NoParams());
        if (err is not null)
        {
            throw new InvalidOperationException("failed to refresh inlay hints", err);
        }
    }

    /// <summary>Go: <c>func (s *Server) RefreshCodeLens(ctx) error</c>（server.go:755-764）。</summary>
    public void RefreshCodeLens(Context ctx)
    {
        if (!_clientCapabilities.Workspace.CodeLens.RefreshSupport)
        {
            return;
        }

        var err = SendClientRequestFireAndForget(WorkspaceCodeLensRefreshInfo, new NoParams());
        if (err is not null)
        {
            throw new InvalidOperationException("failed to refresh code lens", err);
        }
    }

    /// <summary>Go: <c>func (s *Server) ProgressStart(message, args ...any)</c>（server.go:766-770）——实现 project.Client。</summary>
    public void ProgressStart(DiagnosticMessage message, params object?[] args) => _projectProgress?.Start(message, args);

    /// <summary>Go: <c>func (s *Server) ProgressFinish(message, args ...any)</c>（server.go:772-776）——实现 project.Client。</summary>
    public void ProgressFinish(DiagnosticMessage message, params object?[] args) => _projectProgress?.Finish(message, args);

    /// <summary>Go: <c>func (s *Server) GetLocale() locale.Locale</c>（server.go:778-782）——实现 project.Client。</summary>
    public Loc GetLocale()
    {
        _localeMu.EnterReadLock();
        try
        {
            return _locale;
        }
        finally
        {
            _localeMu.ExitReadLock();
        }
    }

    /// <summary>Go: <c>func (s *Server) SetLocale(localeString string)</c>（server.go:784-796）——实现 project.Client。</summary>
    public void SetLocale(string localeString)
    {
        var newLocale = _initLocale;
        if (localeString != "auto")
        {
            if (!Loc.TryParse(localeString, out var parsed))
            {
                return;
            }

            newLocale = parsed;
        }

        _localeMu.EnterWriteLock();
        try
        {
            _locale = newLocale;
        }
        finally
        {
            _localeMu.ExitWriteLock();
        }
    }

    /// <summary>Go: <c>func (s *Server) RequestConfiguration(ctx) (lsutil.UserPreferences, error)</c>（server.go:798-864）。</summary>
    private LsUtil.UserPreferences RequestConfiguration(Context ctx)
    {
        var caps = GetClientCapabilities();
        if (!caps.Workspace.Configuration)
        {
            var opts = _initializationOptions;
            if (opts.UserPreferences is not null)
            {
                var userPrefs = opts.UserPreferences;
                _logger.Logf(
                    "received formatting options from initialization: {0}\n{1}",
                    userPrefs.GetType().FullName ?? userPrefs.GetType().Name,
                    userPrefs);
                if (userPrefs is Dictionary<string, object?> config)
                {
                    return LsUtil.LsUtil.ParseUserPreferences(new Dictionary<string, object?> { ["js/ts"] = config });
                }
            }

            return LsUtil.LsUtil.NewDefaultUserPreferences();
        }

        var (configs, err) = SendClientRequest(ctx, WorkspaceConfigurationInfo, new ConfigurationParams
        {
            Items =
            [
                new ConfigurationItem { Section = "js/ts" },
                new ConfigurationItem { Section = "typescript" },
                new ConfigurationItem { Section = "javascript" },
                new ConfigurationItem { Section = "editor" },
            ],
        });
        if (err is not null)
        {
            throw new InvalidOperationException("configure request failed", err);
        }

        var configMap = new Dictionary<string, object?>();
        if (configs is not null)
        {
            for (var i = 0; i < configs.Length; i++)
            {
                switch (i)
                {
                    case 0:
                        configMap["js/ts"] = configs[i];
                        break;
                    case 1:
                        configMap["typescript"] = configs[i];
                        break;
                    case 2:
                        configMap["javascript"] = configs[i];
                        break;
                    case 3:
                        configMap["editor"] = configs[i];
                        break;
                }
            }
        }

        _logger.Logf(
            "received options from workspace/configuration request:\njs/ts: {0}\n\ntypescript: {1}\n\njavascript: {2}\n\neditor: {3}\n",
            configMap["js/ts"],
            configMap["typescript"],
            configMap["javascript"],
            configMap["editor"]);
        return LsUtil.LsUtil.ParseUserPreferences(configMap);
    }

    /// <summary>Go: <c>func contentMapperFallbackResponse(method lsproto.Method, err error) (any, bool)</c>（server.go:1247-1271）。</summary>
    private static (object? Response, bool Ok) ContentMapperFallbackResponse(Method method, Exception err)
    {
        if (!IsNoProjectForUnknownScriptKind(err))
        {
            return (null, false);
        }

        if (method == Method.TextDocumentDiagnostic)
        {
            return (new RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport
            {
                FullDocumentDiagnosticReport = new RelatedFullDocumentDiagnosticReport
                {
                    Items = [],
                },
            }, true);
        }

        if (method == Method.TextDocumentHover
            || method == Method.TextDocumentSignatureHelp
            || method == Method.TextDocumentDefinition
            || method == Method.TextDocumentTypeDefinition
            || method == Method.TextDocumentImplementation
            || method == Method.TextDocumentReferences
            || method == Method.TextDocumentDocumentHighlight
            || method == Method.TextDocumentCompletion
            || method == Method.TextDocumentRename)
        {
            return (new Null(), true);
        }

        return (null, false);
    }

    /// <summary>Go 的 <c>errors.Is(err, project.ErrNoProjectForUnknownScriptKind)</c>（哨兵在 InnerException 链上）。</summary>
    private static bool IsNoProjectForUnknownScriptKind(Exception? err)
    {
        for (var e = err; e is not null; e = e.InnerException)
        {
            if (ReferenceEquals(e, ProjectSession.ErrNoProjectForUnknownScriptKind))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func (s *Server) NpmInstall(cwd string, args []string) ([]byte, error)</c>（server.go:2370-2372）——实现 ata.NpmExecutor。</summary>
    public byte[] NpmInstall(string cwd, string[] args) => _npmInstall!(cwd, args);

    /// <summary>Go: <c>func (s *Server) contentMapperSpawner() contentmapper.Spawner</c>（server.go:2374-2379）。</summary>
    private global::TypeScript.ContentMapper.Spawner? ContentMapperSpawner() => _spawn;

    /// <summary>Go: <c>func (s *Server) contentMapperLogger() contentmapper.Logger</c>（server.go:2381-2387）。</summary>
    private global::TypeScript.ContentMapper.Logger ContentMapperLogger() =>
        message =>
        {
            if (_logger.IsTracing())
            {
                _logger.Info(message);
            }
        };

    /// <summary>Go: <c>func (s *Server) SetCompilerOptionsForInferredProjects(ctx, options)</c>（server.go:2363-2368）。</summary>
    public void SetCompilerOptionsForInferredProjects(Context ctx, CompilerOptions options)
    {
        _compilerOptionsForInferredProjects = options;
        _session?.DidChangeCompilerOptionsForInferredProjects(ctx, options);
    }
}
