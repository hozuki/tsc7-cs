// generated from: internal/lsp/lsproto/lsp_generated.go（Methods and request/response types：行 10793-11751）
// generator: artifacts/lsproto-gen/generate.mjs（确定性转换；改生成器后重跑，不要手改本文件）
//
// 与 Go 的对应关系（偏离见下）：
//   * Go 的 reflect 驱动解码（structcodec.go 的 unmarshalStruct）在这里落成每个类型一张显式字段表
//     （StructSpec/StructFieldSpec，见 StructCodec.cs）：字段名、required、null 严格度、omitzero 逐条照抄 tag。
//   * Go 的类型别名（type X = Y）不生成 C# 类型（C# 没有跨文件类型别名），使用点直接用目标类型。
//   * Go 的包级常量（如 MarkupKindMarkdown）落在 C# 侧对应类型上（MarkupKind.Markdown），见 docs/architecture.md §3.3。
//   * Go 的值结构体在这里是 class（docs/architecture.md §3.2 的 `*T` → `T?`），因此 resolve() 的“拷贝”语义
//     退化成共享引用（Resolved* 是只读视图）。
//   * 字段名与外围类型同名在 C# 里非法（CS0542）。整个 lsp_generated.go 只有 Command.Command（2834 行），
//     按同名常量（constMemberName）的裁决改叫 `Value`。

using System.Globalization;
using System.Text;
using TypeScript.Json;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.LsProto;

/// <summary>Go: <c>type Method string</c> 的常量（类型本体在 Lsp.cs 手写，这里补 partial 声明）。</summary>
public readonly partial struct Method
{
    /// <summary>Go: <c>MethodTextDocumentImplementation Method = "\"textDocument/implementation\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentImplementation = new("textDocument/implementation");

    /// <summary>Go: <c>MethodTextDocumentTypeDefinition Method = "\"textDocument/typeDefinition\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentTypeDefinition = new("textDocument/typeDefinition");

    /// <summary>Go: <c>MethodWorkspaceWorkspaceFolders Method = "\"workspace/workspaceFolders\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceWorkspaceFolders = new("workspace/workspaceFolders");

    /// <summary>Go: <c>MethodWorkspaceConfiguration Method = "\"workspace/configuration\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceConfiguration = new("workspace/configuration");

    /// <summary>Go: <c>MethodTextDocumentDocumentColor Method = "\"textDocument/documentColor\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDocumentColor = new("textDocument/documentColor");

    /// <summary>Go: <c>MethodTextDocumentColorPresentation Method = "\"textDocument/colorPresentation\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentColorPresentation = new("textDocument/colorPresentation");

    /// <summary>Go: <c>MethodTextDocumentFoldingRange Method = "\"textDocument/foldingRange\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentFoldingRange = new("textDocument/foldingRange");

    /// <summary>Go: <c>MethodWorkspaceFoldingRangeRefresh Method = "\"workspace/foldingRange/refresh\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceFoldingRangeRefresh = new("workspace/foldingRange/refresh");

    /// <summary>Go: <c>MethodTextDocumentDeclaration Method = "\"textDocument/declaration\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDeclaration = new("textDocument/declaration");

    /// <summary>Go: <c>MethodTextDocumentSelectionRange Method = "\"textDocument/selectionRange\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentSelectionRange = new("textDocument/selectionRange");

    /// <summary>Go: <c>MethodWindowWorkDoneProgressCreate Method = "\"window/workDoneProgress/create\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WindowWorkDoneProgressCreate = new("window/workDoneProgress/create");

    /// <summary>Go: <c>MethodTextDocumentPrepareCallHierarchy Method = "\"textDocument/prepareCallHierarchy\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentPrepareCallHierarchy = new("textDocument/prepareCallHierarchy");

    /// <summary>Go: <c>MethodCallHierarchyIncomingCalls Method = "\"callHierarchy/incomingCalls\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CallHierarchyIncomingCalls = new("callHierarchy/incomingCalls");

    /// <summary>Go: <c>MethodCallHierarchyOutgoingCalls Method = "\"callHierarchy/outgoingCalls\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CallHierarchyOutgoingCalls = new("callHierarchy/outgoingCalls");

    /// <summary>Go: <c>MethodTextDocumentSemanticTokensFull Method = "\"textDocument/semanticTokens/full\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentSemanticTokensFull = new("textDocument/semanticTokens/full");

    /// <summary>Go: <c>MethodTextDocumentSemanticTokensFullDelta Method = "\"textDocument/semanticTokens/full/delta\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentSemanticTokensFullDelta = new("textDocument/semanticTokens/full/delta");

    /// <summary>Go: <c>MethodTextDocumentSemanticTokensRange Method = "\"textDocument/semanticTokens/range\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentSemanticTokensRange = new("textDocument/semanticTokens/range");

    /// <summary>Go: <c>MethodWorkspaceSemanticTokensRefresh Method = "\"workspace/semanticTokens/refresh\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceSemanticTokensRefresh = new("workspace/semanticTokens/refresh");

    /// <summary>Go: <c>MethodWindowShowDocument Method = "\"window/showDocument\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WindowShowDocument = new("window/showDocument");

    /// <summary>Go: <c>MethodTextDocumentLinkedEditingRange Method = "\"textDocument/linkedEditingRange\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentLinkedEditingRange = new("textDocument/linkedEditingRange");

    /// <summary>Go: <c>MethodWorkspaceWillCreateFiles Method = "\"workspace/willCreateFiles\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceWillCreateFiles = new("workspace/willCreateFiles");

    /// <summary>Go: <c>MethodWorkspaceWillRenameFiles Method = "\"workspace/willRenameFiles\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceWillRenameFiles = new("workspace/willRenameFiles");

    /// <summary>Go: <c>MethodWorkspaceWillDeleteFiles Method = "\"workspace/willDeleteFiles\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceWillDeleteFiles = new("workspace/willDeleteFiles");

    /// <summary>Go: <c>MethodTextDocumentMoniker Method = "\"textDocument/moniker\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentMoniker = new("textDocument/moniker");

    /// <summary>Go: <c>MethodTextDocumentPrepareTypeHierarchy Method = "\"textDocument/prepareTypeHierarchy\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentPrepareTypeHierarchy = new("textDocument/prepareTypeHierarchy");

    /// <summary>Go: <c>MethodTypeHierarchySupertypes Method = "\"typeHierarchy/supertypes\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TypeHierarchySupertypes = new("typeHierarchy/supertypes");

    /// <summary>Go: <c>MethodTypeHierarchySubtypes Method = "\"typeHierarchy/subtypes\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TypeHierarchySubtypes = new("typeHierarchy/subtypes");

    /// <summary>Go: <c>MethodTextDocumentInlineValue Method = "\"textDocument/inlineValue\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentInlineValue = new("textDocument/inlineValue");

    /// <summary>Go: <c>MethodWorkspaceInlineValueRefresh Method = "\"workspace/inlineValue/refresh\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceInlineValueRefresh = new("workspace/inlineValue/refresh");

    /// <summary>Go: <c>MethodTextDocumentInlayHint Method = "\"textDocument/inlayHint\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentInlayHint = new("textDocument/inlayHint");

    /// <summary>Go: <c>MethodInlayHintResolve Method = "\"inlayHint/resolve\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method InlayHintResolve = new("inlayHint/resolve");

    /// <summary>Go: <c>MethodWorkspaceInlayHintRefresh Method = "\"workspace/inlayHint/refresh\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceInlayHintRefresh = new("workspace/inlayHint/refresh");

    /// <summary>Go: <c>MethodTextDocumentDiagnostic Method = "\"textDocument/diagnostic\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDiagnostic = new("textDocument/diagnostic");

    /// <summary>Go: <c>MethodWorkspaceDiagnostic Method = "\"workspace/diagnostic\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDiagnostic = new("workspace/diagnostic");

    /// <summary>Go: <c>MethodWorkspaceDiagnosticRefresh Method = "\"workspace/diagnostic/refresh\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDiagnosticRefresh = new("workspace/diagnostic/refresh");

    /// <summary>Go: <c>MethodTextDocumentInlineCompletion Method = "\"textDocument/inlineCompletion\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentInlineCompletion = new("textDocument/inlineCompletion");

    /// <summary>Go: <c>MethodWorkspaceTextDocumentContent Method = "\"workspace/textDocumentContent\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceTextDocumentContent = new("workspace/textDocumentContent");

    /// <summary>Go: <c>MethodWorkspaceTextDocumentContentRefresh Method = "\"workspace/textDocumentContent/refresh\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceTextDocumentContentRefresh = new("workspace/textDocumentContent/refresh");

    /// <summary>Go: <c>MethodClientRegisterCapability Method = "\"client/registerCapability\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method ClientRegisterCapability = new("client/registerCapability");

    /// <summary>Go: <c>MethodClientUnregisterCapability Method = "\"client/unregisterCapability\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method ClientUnregisterCapability = new("client/unregisterCapability");

    /// <summary>Go: <c>MethodInitialize Method = "\"initialize\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method Initialize = new("initialize");

    /// <summary>Go: <c>MethodShutdown Method = "\"shutdown\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method Shutdown = new("shutdown");

    /// <summary>Go: <c>MethodWindowShowMessageRequest Method = "\"window/showMessageRequest\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WindowShowMessageRequest = new("window/showMessageRequest");

    /// <summary>Go: <c>MethodTextDocumentWillSaveWaitUntil Method = "\"textDocument/willSaveWaitUntil\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentWillSaveWaitUntil = new("textDocument/willSaveWaitUntil");

    /// <summary>Go: <c>MethodTextDocumentCompletion Method = "\"textDocument/completion\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentCompletion = new("textDocument/completion");

    /// <summary>Go: <c>MethodCompletionItemResolve Method = "\"completionItem/resolve\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CompletionItemResolve = new("completionItem/resolve");

    /// <summary>Go: <c>MethodTextDocumentHover Method = "\"textDocument/hover\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentHover = new("textDocument/hover");

    /// <summary>Go: <c>MethodTextDocumentSignatureHelp Method = "\"textDocument/signatureHelp\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentSignatureHelp = new("textDocument/signatureHelp");

    /// <summary>Go: <c>MethodTextDocumentDefinition Method = "\"textDocument/definition\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDefinition = new("textDocument/definition");

    /// <summary>Go: <c>MethodTextDocumentReferences Method = "\"textDocument/references\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentReferences = new("textDocument/references");

    /// <summary>Go: <c>MethodTextDocumentDocumentHighlight Method = "\"textDocument/documentHighlight\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDocumentHighlight = new("textDocument/documentHighlight");

    /// <summary>Go: <c>MethodTextDocumentDocumentSymbol Method = "\"textDocument/documentSymbol\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDocumentSymbol = new("textDocument/documentSymbol");

    /// <summary>Go: <c>MethodTextDocumentCodeAction Method = "\"textDocument/codeAction\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentCodeAction = new("textDocument/codeAction");

    /// <summary>Go: <c>MethodCodeActionResolve Method = "\"codeAction/resolve\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CodeActionResolve = new("codeAction/resolve");

    /// <summary>Go: <c>MethodWorkspaceSymbol Method = "\"workspace/symbol\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceSymbol = new("workspace/symbol");

    /// <summary>Go: <c>MethodWorkspaceSymbolResolve Method = "\"workspaceSymbol/resolve\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceSymbolResolve = new("workspaceSymbol/resolve");

    /// <summary>Go: <c>MethodTextDocumentCodeLens Method = "\"textDocument/codeLens\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentCodeLens = new("textDocument/codeLens");

    /// <summary>Go: <c>MethodCodeLensResolve Method = "\"codeLens/resolve\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CodeLensResolve = new("codeLens/resolve");

    /// <summary>Go: <c>MethodWorkspaceCodeLensRefresh Method = "\"workspace/codeLens/refresh\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceCodeLensRefresh = new("workspace/codeLens/refresh");

    /// <summary>Go: <c>MethodTextDocumentDocumentLink Method = "\"textDocument/documentLink\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDocumentLink = new("textDocument/documentLink");

    /// <summary>Go: <c>MethodDocumentLinkResolve Method = "\"documentLink/resolve\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method DocumentLinkResolve = new("documentLink/resolve");

    /// <summary>Go: <c>MethodTextDocumentFormatting Method = "\"textDocument/formatting\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentFormatting = new("textDocument/formatting");

    /// <summary>Go: <c>MethodTextDocumentRangeFormatting Method = "\"textDocument/rangeFormatting\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentRangeFormatting = new("textDocument/rangeFormatting");

    /// <summary>Go: <c>MethodTextDocumentRangesFormatting Method = "\"textDocument/rangesFormatting\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentRangesFormatting = new("textDocument/rangesFormatting");

    /// <summary>Go: <c>MethodTextDocumentOnTypeFormatting Method = "\"textDocument/onTypeFormatting\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentOnTypeFormatting = new("textDocument/onTypeFormatting");

    /// <summary>Go: <c>MethodTextDocumentRename Method = "\"textDocument/rename\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentRename = new("textDocument/rename");

    /// <summary>Go: <c>MethodTextDocumentPrepareRename Method = "\"textDocument/prepareRename\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentPrepareRename = new("textDocument/prepareRename");

    /// <summary>Go: <c>MethodWorkspaceExecuteCommand Method = "\"workspace/executeCommand\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceExecuteCommand = new("workspace/executeCommand");

    /// <summary>Go: <c>MethodWorkspaceApplyEdit Method = "\"workspace/applyEdit\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceApplyEdit = new("workspace/applyEdit");

    /// <summary>Go: <c>MethodCustomRunGC Method = "\"custom/runGC\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomRunGC = new("custom/runGC");

    /// <summary>Go: <c>MethodCustomSaveHeapProfile Method = "\"custom/saveHeapProfile\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomSaveHeapProfile = new("custom/saveHeapProfile");

    /// <summary>Go: <c>MethodCustomSaveAllocProfile Method = "\"custom/saveAllocProfile\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomSaveAllocProfile = new("custom/saveAllocProfile");

    /// <summary>Go: <c>MethodCustomStartCPUProfile Method = "\"custom/startCPUProfile\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomStartCPUProfile = new("custom/startCPUProfile");

    /// <summary>Go: <c>MethodCustomStopCPUProfile Method = "\"custom/stopCPUProfile\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomStopCPUProfile = new("custom/stopCPUProfile");

    /// <summary>Go: <c>MethodCustomInitializeAPISession Method = "\"custom/initializeAPISession\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomInitializeAPISession = new("custom/initializeAPISession");

    /// <summary>Go: <c>MethodCustomProjectInfo Method = "\"custom/projectInfo\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomProjectInfo = new("custom/projectInfo");

    /// <summary>Go: <c>MethodCustomSetContentMapperContributions Method = "\"custom/setContentMapperContributions\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomSetContentMapperContributions = new("custom/setContentMapperContributions");

    /// <summary>Go: <c>MethodCustomTextDocumentSourceDefinition Method = "\"custom/textDocument/sourceDefinition\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomTextDocumentSourceDefinition = new("custom/textDocument/sourceDefinition");

    /// <summary>Go: <c>MethodCustomTextDocumentMultiDocumentHighlight Method = "\"custom/textDocument/multiDocumentHighlight\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomTextDocumentMultiDocumentHighlight = new("custom/textDocument/multiDocumentHighlight");

    /// <summary>Go: <c>MethodTextDocumentVSOnAutoInsert Method = "\"textDocument/_vs_onAutoInsert\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentVSOnAutoInsert = new("textDocument/_vs_onAutoInsert");

    /// <summary>Go: <c>MethodTextDocumentVSReferences Method = "\"textDocument/_vs_references\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentVSReferences = new("textDocument/_vs_references");

    /// <summary>Go: <c>MethodWorkspaceDidChangeWorkspaceFolders Method = "\"workspace/didChangeWorkspaceFolders\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDidChangeWorkspaceFolders = new("workspace/didChangeWorkspaceFolders");

    /// <summary>Go: <c>MethodWindowWorkDoneProgressCancel Method = "\"window/workDoneProgress/cancel\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WindowWorkDoneProgressCancel = new("window/workDoneProgress/cancel");

    /// <summary>Go: <c>MethodWorkspaceDidCreateFiles Method = "\"workspace/didCreateFiles\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDidCreateFiles = new("workspace/didCreateFiles");

    /// <summary>Go: <c>MethodWorkspaceDidRenameFiles Method = "\"workspace/didRenameFiles\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDidRenameFiles = new("workspace/didRenameFiles");

    /// <summary>Go: <c>MethodWorkspaceDidDeleteFiles Method = "\"workspace/didDeleteFiles\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDidDeleteFiles = new("workspace/didDeleteFiles");

    /// <summary>Go: <c>MethodInitialized Method = "\"initialized\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method Initialized = new("initialized");

    /// <summary>Go: <c>MethodExit Method = "\"exit\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method Exit = new("exit");

    /// <summary>Go: <c>MethodWorkspaceDidChangeConfiguration Method = "\"workspace/didChangeConfiguration\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDidChangeConfiguration = new("workspace/didChangeConfiguration");

    /// <summary>Go: <c>MethodWindowShowMessage Method = "\"window/showMessage\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WindowShowMessage = new("window/showMessage");

    /// <summary>Go: <c>MethodWindowLogMessage Method = "\"window/logMessage\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WindowLogMessage = new("window/logMessage");

    /// <summary>Go: <c>MethodTelemetryEvent Method = "\"telemetry/event\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TelemetryEvent = new("telemetry/event");

    /// <summary>Go: <c>MethodTextDocumentDidOpen Method = "\"textDocument/didOpen\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDidOpen = new("textDocument/didOpen");

    /// <summary>Go: <c>MethodTextDocumentDidChange Method = "\"textDocument/didChange\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDidChange = new("textDocument/didChange");

    /// <summary>Go: <c>MethodTextDocumentDidClose Method = "\"textDocument/didClose\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDidClose = new("textDocument/didClose");

    /// <summary>Go: <c>MethodTextDocumentDidSave Method = "\"textDocument/didSave\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentDidSave = new("textDocument/didSave");

    /// <summary>Go: <c>MethodTextDocumentWillSave Method = "\"textDocument/willSave\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentWillSave = new("textDocument/willSave");

    /// <summary>Go: <c>MethodWorkspaceDidChangeWatchedFiles Method = "\"workspace/didChangeWatchedFiles\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method WorkspaceDidChangeWatchedFiles = new("workspace/didChangeWatchedFiles");

    /// <summary>Go: <c>MethodTextDocumentPublishDiagnostics Method = "\"textDocument/publishDiagnostics\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentPublishDiagnostics = new("textDocument/publishDiagnostics");

    /// <summary>Go: <c>MethodSetTrace Method = "\"$/setTrace\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method SetTrace = new("$/setTrace");

    /// <summary>Go: <c>MethodLogTrace Method = "\"$/logTrace\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method LogTrace = new("$/logTrace");

    /// <summary>Go: <c>MethodCancelRequest Method = "\"$/cancelRequest\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CancelRequest = new("$/cancelRequest");

    /// <summary>Go: <c>MethodProgress Method = "\"$/progress\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method Progress = new("$/progress");

    /// <summary>Go: <c>MethodCustomSetLogVerbosity Method = "\"custom/setLogVerbosity\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method CustomSetLogVerbosity = new("custom/setLogVerbosity");

    /// <summary>Go: <c>MethodTextDocumentSemanticTokens Method = "\"textDocument/semanticTokens\""</c>（lsp_generated.go:10794）。</summary>
    public static readonly Method TextDocumentSemanticTokens = new("textDocument/semanticTokens");
}

/// <summary>Go: <c>type ImplementationResponse = LocationOrLocationsOrDefinitionLinksOrNull</c>（lsp_generated.go:11194）。
/// Response type for `textDocument/implementation`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（LocationOrLocationsOrDefinitionLinksOrNull）。

/// <summary>Go: <c>type TypeDefinitionResponse = LocationOrLocationsOrDefinitionLinksOrNull</c>（lsp_generated.go:11200）。
/// Response type for `textDocument/typeDefinition`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（LocationOrLocationsOrDefinitionLinksOrNull）。

/// <summary>Go: <c>type WorkspaceFoldersResponse = WorkspaceFoldersOrNull</c>（lsp_generated.go:11206）。
/// Response type for `workspace/workspaceFolders`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（WorkspaceFoldersOrNull）。

/// <summary>Go: <c>type ConfigurationResponse = []any</c>（lsp_generated.go:11212）。
/// Response type for `workspace/configuration`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（[]any）。

/// <summary>Go: <c>type DocumentColorResponse = []*ColorInformation</c>（lsp_generated.go:11218）。
/// Response type for `textDocument/documentColor`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（[]*ColorInformation）。

/// <summary>Go: <c>type ColorPresentationResponse = []*ColorPresentation</c>（lsp_generated.go:11224）。
/// Response type for `textDocument/colorPresentation`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（[]*ColorPresentation）。

/// <summary>Go: <c>type FoldingRangeResponse = FoldingRangesOrNull</c>（lsp_generated.go:11230）。
/// Response type for `textDocument/foldingRange`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（FoldingRangesOrNull）。

/// <summary>Go: <c>type FoldingRangeRefreshResponse = Null</c>（lsp_generated.go:11236）。
/// Response type for `workspace/foldingRange/refresh`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type DeclarationResponse = LocationOrLocationsOrDeclarationLinksOrNull</c>（lsp_generated.go:11242）。
/// Response type for `textDocument/declaration`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（LocationOrLocationsOrDeclarationLinksOrNull）。

/// <summary>Go: <c>type SelectionRangeResponse = SelectionRangesOrNull</c>（lsp_generated.go:11248）。
/// Response type for `textDocument/selectionRange`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（SelectionRangesOrNull）。

/// <summary>Go: <c>type WorkDoneProgressCreateResponse = Null</c>（lsp_generated.go:11254）。
/// Response type for `window/workDoneProgress/create`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type CallHierarchyPrepareResponse = CallHierarchyItemsOrNull</c>（lsp_generated.go:11260）。
/// Response type for `textDocument/prepareCallHierarchy`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（CallHierarchyItemsOrNull）。

/// <summary>Go: <c>type CallHierarchyIncomingCallsResponse = CallHierarchyIncomingCallsOrNull</c>（lsp_generated.go:11266）。
/// Response type for `callHierarchy/incomingCalls`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（CallHierarchyIncomingCallsOrNull）。

/// <summary>Go: <c>type CallHierarchyOutgoingCallsResponse = CallHierarchyOutgoingCallsOrNull</c>（lsp_generated.go:11272）。
/// Response type for `callHierarchy/outgoingCalls`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（CallHierarchyOutgoingCallsOrNull）。

/// <summary>Go: <c>type SemanticTokensResponse = SemanticTokensOrNull</c>（lsp_generated.go:11278）。
/// Response type for `textDocument/semanticTokens/full`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（SemanticTokensOrNull）。

/// <summary>Go: <c>type SemanticTokensDeltaResponse = SemanticTokensOrSemanticTokensDeltaOrNull</c>（lsp_generated.go:11284）。
/// Response type for `textDocument/semanticTokens/full/delta`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（SemanticTokensOrSemanticTokensDeltaOrNull）。

/// <summary>Go: <c>type SemanticTokensRangeResponse = SemanticTokensOrNull</c>（lsp_generated.go:11290）。
/// Response type for `textDocument/semanticTokens/range`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（SemanticTokensOrNull）。

/// <summary>Go: <c>type SemanticTokensRefreshResponse = Null</c>（lsp_generated.go:11296）。
/// Response type for `workspace/semanticTokens/refresh`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type ShowDocumentResponse = *ShowDocumentResult</c>（lsp_generated.go:11302）。
/// Response type for `window/showDocument`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*ShowDocumentResult）。

/// <summary>Go: <c>type LinkedEditingRangeResponse = LinkedEditingRangesOrNull</c>（lsp_generated.go:11308）。
/// Response type for `textDocument/linkedEditingRange`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（LinkedEditingRangesOrNull）。

/// <summary>Go: <c>type WillCreateFilesResponse = WorkspaceEditOrNull</c>（lsp_generated.go:11314）。
/// Response type for `workspace/willCreateFiles`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（WorkspaceEditOrNull）。

/// <summary>Go: <c>type WillRenameFilesResponse = WorkspaceEditOrNull</c>（lsp_generated.go:11320）。
/// Response type for `workspace/willRenameFiles`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（WorkspaceEditOrNull）。

/// <summary>Go: <c>type WillDeleteFilesResponse = WorkspaceEditOrNull</c>（lsp_generated.go:11326）。
/// Response type for `workspace/willDeleteFiles`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（WorkspaceEditOrNull）。

/// <summary>Go: <c>type MonikerResponse = MonikersOrNull</c>（lsp_generated.go:11332）。
/// Response type for `textDocument/moniker`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（MonikersOrNull）。

/// <summary>Go: <c>type TypeHierarchyPrepareResponse = TypeHierarchyItemsOrNull</c>（lsp_generated.go:11338）。
/// Response type for `textDocument/prepareTypeHierarchy`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TypeHierarchyItemsOrNull）。

/// <summary>Go: <c>type TypeHierarchySupertypesResponse = TypeHierarchyItemsOrNull</c>（lsp_generated.go:11344）。
/// Response type for `typeHierarchy/supertypes`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TypeHierarchyItemsOrNull）。

/// <summary>Go: <c>type TypeHierarchySubtypesResponse = TypeHierarchyItemsOrNull</c>（lsp_generated.go:11350）。
/// Response type for `typeHierarchy/subtypes`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TypeHierarchyItemsOrNull）。

/// <summary>Go: <c>type InlineValueResponse = InlineValuesOrNull</c>（lsp_generated.go:11356）。
/// Response type for `textDocument/inlineValue`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（InlineValuesOrNull）。

/// <summary>Go: <c>type InlineValueRefreshResponse = Null</c>（lsp_generated.go:11362）。
/// Response type for `workspace/inlineValue/refresh`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type InlayHintResponse = InlayHintsOrNull</c>（lsp_generated.go:11368）。
/// Response type for `textDocument/inlayHint`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（InlayHintsOrNull）。

/// <summary>Go: <c>type InlayHintResolveResponse = *InlayHint</c>（lsp_generated.go:11374）。
/// Response type for `inlayHint/resolve`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*InlayHint）。

/// <summary>Go: <c>type InlayHintRefreshResponse = Null</c>（lsp_generated.go:11380）。
/// Response type for `workspace/inlayHint/refresh`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type DocumentDiagnosticResponse = RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport</c>（lsp_generated.go:11386）。
/// Response type for `textDocument/diagnostic`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport）。

/// <summary>Go: <c>type WorkspaceDiagnosticResponse = *WorkspaceDiagnosticReport</c>（lsp_generated.go:11392）。
/// Response type for `workspace/diagnostic`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*WorkspaceDiagnosticReport）。

/// <summary>Go: <c>type DiagnosticRefreshResponse = Null</c>（lsp_generated.go:11398）。
/// Response type for `workspace/diagnostic/refresh`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type InlineCompletionResponse = InlineCompletionListOrItemsOrNull</c>（lsp_generated.go:11404）。
/// Response type for `textDocument/inlineCompletion`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（InlineCompletionListOrItemsOrNull）。

/// <summary>Go: <c>type TextDocumentContentResponse = *TextDocumentContentResult</c>（lsp_generated.go:11410）。
/// Response type for `workspace/textDocumentContent`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*TextDocumentContentResult）。

/// <summary>Go: <c>type TextDocumentContentRefreshResponse = Null</c>（lsp_generated.go:11416）。
/// Response type for `workspace/textDocumentContent/refresh`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type RegistrationResponse = Null</c>（lsp_generated.go:11422）。
/// Response type for `client/registerCapability`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type UnregistrationResponse = Null</c>（lsp_generated.go:11428）。
/// Response type for `client/unregisterCapability`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type InitializeResponse = *InitializeResult</c>（lsp_generated.go:11434）。
/// Response type for `initialize`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*InitializeResult）。

/// <summary>Go: <c>type ShutdownResponse = Null</c>（lsp_generated.go:11440）。
/// Response type for `shutdown`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type ShowMessageResponse = MessageActionItemOrNull</c>（lsp_generated.go:11446）。
/// Response type for `window/showMessageRequest`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（MessageActionItemOrNull）。

/// <summary>Go: <c>type WillSaveTextDocumentWaitUntilResponse = TextEditsOrNull</c>（lsp_generated.go:11452）。
/// Response type for `textDocument/willSaveWaitUntil`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TextEditsOrNull）。

/// <summary>Go: <c>type CompletionResponse = CompletionItemsOrListOrNull</c>（lsp_generated.go:11458）。
/// Response type for `textDocument/completion`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（CompletionItemsOrListOrNull）。

/// <summary>Go: <c>type CompletionResolveResponse = *CompletionItem</c>（lsp_generated.go:11464）。
/// Response type for `completionItem/resolve`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*CompletionItem）。

/// <summary>Go: <c>type HoverResponse = HoverOrNull</c>（lsp_generated.go:11470）。
/// Response type for `textDocument/hover`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（HoverOrNull）。

/// <summary>Go: <c>type SignatureHelpResponse = SignatureHelpOrNull</c>（lsp_generated.go:11476）。
/// Response type for `textDocument/signatureHelp`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（SignatureHelpOrNull）。

/// <summary>Go: <c>type DefinitionResponse = LocationOrLocationsOrDefinitionLinksOrNull</c>（lsp_generated.go:11482）。
/// Response type for `textDocument/definition`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（LocationOrLocationsOrDefinitionLinksOrNull）。

/// <summary>Go: <c>type ReferencesResponse = LocationsOrNull</c>（lsp_generated.go:11488）。
/// Response type for `textDocument/references`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（LocationsOrNull）。

/// <summary>Go: <c>type DocumentHighlightResponse = DocumentHighlightsOrNull</c>（lsp_generated.go:11494）。
/// Response type for `textDocument/documentHighlight`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（DocumentHighlightsOrNull）。

/// <summary>Go: <c>type DocumentSymbolResponse = SymbolInformationsOrDocumentSymbolsOrNull</c>（lsp_generated.go:11500）。
/// Response type for `textDocument/documentSymbol`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（SymbolInformationsOrDocumentSymbolsOrNull）。

/// <summary>Go: <c>type CodeActionResponse = CommandOrCodeActionArrayOrNull</c>（lsp_generated.go:11506）。
/// Response type for `textDocument/codeAction`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（CommandOrCodeActionArrayOrNull）。

/// <summary>Go: <c>type CodeActionResolveResponse = *CodeAction</c>（lsp_generated.go:11512）。
/// Response type for `codeAction/resolve`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*CodeAction）。

/// <summary>Go: <c>type WorkspaceSymbolResponse = SymbolInformationsOrWorkspaceSymbolsOrNull</c>（lsp_generated.go:11518）。
/// Response type for `workspace/symbol`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（SymbolInformationsOrWorkspaceSymbolsOrNull）。

/// <summary>Go: <c>type WorkspaceSymbolResolveResponse = *WorkspaceSymbol</c>（lsp_generated.go:11524）。
/// Response type for `workspaceSymbol/resolve`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*WorkspaceSymbol）。

/// <summary>Go: <c>type CodeLensResponse = CodeLensesOrNull</c>（lsp_generated.go:11530）。
/// Response type for `textDocument/codeLens`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（CodeLensesOrNull）。

/// <summary>Go: <c>type CodeLensResolveResponse = *CodeLens</c>（lsp_generated.go:11536）。
/// Response type for `codeLens/resolve`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*CodeLens）。

/// <summary>Go: <c>type CodeLensRefreshResponse = Null</c>（lsp_generated.go:11542）。
/// Response type for `workspace/codeLens/refresh`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type DocumentLinkResponse = DocumentLinksOrNull</c>（lsp_generated.go:11548）。
/// Response type for `textDocument/documentLink`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（DocumentLinksOrNull）。

/// <summary>Go: <c>type DocumentLinkResolveResponse = *DocumentLink</c>（lsp_generated.go:11554）。
/// Response type for `documentLink/resolve`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*DocumentLink）。

/// <summary>Go: <c>type DocumentFormattingResponse = TextEditsOrNull</c>（lsp_generated.go:11560）。
/// Response type for `textDocument/formatting`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TextEditsOrNull）。

/// <summary>Go: <c>type DocumentRangeFormattingResponse = TextEditsOrNull</c>（lsp_generated.go:11566）。
/// Response type for `textDocument/rangeFormatting`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TextEditsOrNull）。

/// <summary>Go: <c>type DocumentRangesFormattingResponse = TextEditsOrNull</c>（lsp_generated.go:11572）。
/// Response type for `textDocument/rangesFormatting`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TextEditsOrNull）。

/// <summary>Go: <c>type DocumentOnTypeFormattingResponse = TextEditsOrNull</c>（lsp_generated.go:11578）。
/// Response type for `textDocument/onTypeFormatting`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（TextEditsOrNull）。

/// <summary>Go: <c>type RenameResponse = WorkspaceEditOrNull</c>（lsp_generated.go:11584）。
/// Response type for `textDocument/rename`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（WorkspaceEditOrNull）。

/// <summary>Go: <c>type PrepareRenameResponse = RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull</c>（lsp_generated.go:11590）。
/// Response type for `textDocument/prepareRename`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull）。

/// <summary>Go: <c>type ExecuteCommandResponse = LSPAnyOrNull</c>（lsp_generated.go:11596）。
/// Response type for `workspace/executeCommand`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（LSPAnyOrNull）。

/// <summary>Go: <c>type ApplyWorkspaceEditResponse = *ApplyWorkspaceEditResult</c>（lsp_generated.go:11602）。
/// Response type for `workspace/applyEdit`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*ApplyWorkspaceEditResult）。

/// <summary>Go: <c>type RunGCResponse = Null</c>（lsp_generated.go:11608）。
/// Response type for `custom/runGC`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type SaveHeapProfileResponse = *ProfileResult</c>（lsp_generated.go:11614）。
/// Response type for `custom/saveHeapProfile`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*ProfileResult）。

/// <summary>Go: <c>type SaveAllocProfileResponse = *ProfileResult</c>（lsp_generated.go:11620）。
/// Response type for `custom/saveAllocProfile`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*ProfileResult）。

/// <summary>Go: <c>type StartCPUProfileResponse = Null</c>（lsp_generated.go:11626）。
/// Response type for `custom/startCPUProfile`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type StopCPUProfileResponse = *ProfileResult</c>（lsp_generated.go:11632）。
/// Response type for `custom/stopCPUProfile`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*ProfileResult）。

/// <summary>Go: <c>type CustomInitializeAPISessionResponse = *InitializeAPISessionResult</c>（lsp_generated.go:11638）。
/// Response type for `custom/initializeAPISession`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*InitializeAPISessionResult）。

/// <summary>Go: <c>type CustomProjectInfoResponse = *ProjectInfoResult</c>（lsp_generated.go:11644）。
/// Response type for `custom/projectInfo`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*ProjectInfoResult）。

/// <summary>Go: <c>type CustomSetContentMapperContributionsResponse = Null</c>（lsp_generated.go:11650）。
/// Response type for `custom/setContentMapperContributions`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（Null）。

/// <summary>Go: <c>type CustomTextDocumentSourceDefinitionResponse = *LocationOrLocationsOrDefinitionLinksOrNull</c>（lsp_generated.go:11656）。
/// Response type for `custom/textDocument/sourceDefinition`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（*LocationOrLocationsOrDefinitionLinksOrNull）。

/// <summary>Go: <c>type CustomMultiDocumentHighlightResponse = MultiDocumentHighlightsOrNull</c>（lsp_generated.go:11662）。
/// Response type for `custom/textDocument/multiDocumentHighlight`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（MultiDocumentHighlightsOrNull）。

/// <summary>Go: <c>type VSOnAutoInsertResponse = VSOnAutoInsertResponseItemOrNull</c>（lsp_generated.go:11668）。
/// Response type for `textDocument/_vs_onAutoInsert`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（VSOnAutoInsertResponseItemOrNull）。

/// <summary>Go: <c>type VSReferencesResponse = VSReferenceItemsOrNull</c>（lsp_generated.go:11674）。
/// Response type for `textDocument/_vs_references`
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（VSReferenceItemsOrNull）。

/// <summary>Go: <c>type TelemetryEvent = RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull</c>（lsp_generated.go:11750）。
/// </summary>
// C# 侧不生成类型：Go 的类型别名在这里由使用点直接用目标类型（RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull）。

public static partial class LsProto
{
    /// <summary>Go: <c>var TextDocumentImplementationInfo = RequestInfo[*ImplementationParams, ImplementationResponse]{Method: MethodTextDocumentImplementation}</c>（lsp_generated.go:11197）。</summary>
    public static readonly RequestInfo<ImplementationParams?, LocationOrLocationsOrDefinitionLinksOrNull> TextDocumentImplementationInfo = new(Method.TextDocumentImplementation, static d => d.DecodeNew<LocationOrLocationsOrDefinitionLinksOrNull>());
    
    /// <summary>Go: <c>var TextDocumentTypeDefinitionInfo = RequestInfo[*TypeDefinitionParams, TypeDefinitionResponse]{Method: MethodTextDocumentTypeDefinition}</c>（lsp_generated.go:11203）。</summary>
    public static readonly RequestInfo<TypeDefinitionParams?, LocationOrLocationsOrDefinitionLinksOrNull> TextDocumentTypeDefinitionInfo = new(Method.TextDocumentTypeDefinition, static d => d.DecodeNew<LocationOrLocationsOrDefinitionLinksOrNull>());
    
    /// <summary>Go: <c>var WorkspaceWorkspaceFoldersInfo = RequestInfo[NoParams, WorkspaceFoldersResponse]{Method: MethodWorkspaceWorkspaceFolders}</c>（lsp_generated.go:11209）。</summary>
    public static readonly RequestInfo<NoParams, WorkspaceFoldersOrNull> WorkspaceWorkspaceFoldersInfo = new(Method.WorkspaceWorkspaceFolders, static d => d.DecodeNew<WorkspaceFoldersOrNull>());
    
    /// <summary>Go: <c>var WorkspaceConfigurationInfo = RequestInfo[*ConfigurationParams, ConfigurationResponse]{Method: MethodWorkspaceConfiguration}</c>（lsp_generated.go:11215）。</summary>
    public static readonly RequestInfo<ConfigurationParams?, object?[]?> WorkspaceConfigurationInfo = new(Method.WorkspaceConfiguration, static d => DecodeArray<object?>(d, static d => d.Decode<object?>()));
    
    /// <summary>Go: <c>var TextDocumentDocumentColorInfo = RequestInfo[*DocumentColorParams, DocumentColorResponse]{Method: MethodTextDocumentDocumentColor}</c>（lsp_generated.go:11221）。</summary>
    public static readonly RequestInfo<DocumentColorParams?, ColorInformation?[]?> TextDocumentDocumentColorInfo = new(Method.TextDocumentDocumentColor, static d => DecodeArray<ColorInformation?>(d, static d => DecodeNullable<ColorInformation>(d, static d => d.DecodeNew<ColorInformation>())));
    
    /// <summary>Go: <c>var TextDocumentColorPresentationInfo = RequestInfo[*ColorPresentationParams, ColorPresentationResponse]{Method: MethodTextDocumentColorPresentation}</c>（lsp_generated.go:11227）。</summary>
    public static readonly RequestInfo<ColorPresentationParams?, ColorPresentation?[]?> TextDocumentColorPresentationInfo = new(Method.TextDocumentColorPresentation, static d => DecodeArray<ColorPresentation?>(d, static d => DecodeNullable<ColorPresentation>(d, static d => d.DecodeNew<ColorPresentation>())));
    
    /// <summary>Go: <c>var TextDocumentFoldingRangeInfo = RequestInfo[*FoldingRangeParams, FoldingRangeResponse]{Method: MethodTextDocumentFoldingRange}</c>（lsp_generated.go:11233）。</summary>
    public static readonly RequestInfo<FoldingRangeParams?, FoldingRangesOrNull> TextDocumentFoldingRangeInfo = new(Method.TextDocumentFoldingRange, static d => d.DecodeNew<FoldingRangesOrNull>());
    
    /// <summary>Go: <c>var WorkspaceFoldingRangeRefreshInfo = RequestInfo[NoParams, FoldingRangeRefreshResponse]{Method: MethodWorkspaceFoldingRangeRefresh}</c>（lsp_generated.go:11239）。</summary>
    public static readonly RequestInfo<NoParams, Null> WorkspaceFoldingRangeRefreshInfo = new(Method.WorkspaceFoldingRangeRefresh, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var TextDocumentDeclarationInfo = RequestInfo[*DeclarationParams, DeclarationResponse]{Method: MethodTextDocumentDeclaration}</c>（lsp_generated.go:11245）。</summary>
    public static readonly RequestInfo<DeclarationParams?, LocationOrLocationsOrDeclarationLinksOrNull> TextDocumentDeclarationInfo = new(Method.TextDocumentDeclaration, static d => d.DecodeNew<LocationOrLocationsOrDeclarationLinksOrNull>());
    
    /// <summary>Go: <c>var TextDocumentSelectionRangeInfo = RequestInfo[*SelectionRangeParams, SelectionRangeResponse]{Method: MethodTextDocumentSelectionRange}</c>（lsp_generated.go:11251）。</summary>
    public static readonly RequestInfo<SelectionRangeParams?, SelectionRangesOrNull> TextDocumentSelectionRangeInfo = new(Method.TextDocumentSelectionRange, static d => d.DecodeNew<SelectionRangesOrNull>());
    
    /// <summary>Go: <c>var WindowWorkDoneProgressCreateInfo = RequestInfo[*WorkDoneProgressCreateParams, WorkDoneProgressCreateResponse]{Method: MethodWindowWorkDoneProgressCreate}</c>（lsp_generated.go:11257）。</summary>
    public static readonly RequestInfo<WorkDoneProgressCreateParams?, Null> WindowWorkDoneProgressCreateInfo = new(Method.WindowWorkDoneProgressCreate, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var TextDocumentPrepareCallHierarchyInfo = RequestInfo[*CallHierarchyPrepareParams, CallHierarchyPrepareResponse]{Method: MethodTextDocumentPrepareCallHierarchy}</c>（lsp_generated.go:11263）。</summary>
    public static readonly RequestInfo<CallHierarchyPrepareParams?, CallHierarchyItemsOrNull> TextDocumentPrepareCallHierarchyInfo = new(Method.TextDocumentPrepareCallHierarchy, static d => d.DecodeNew<CallHierarchyItemsOrNull>());
    
    /// <summary>Go: <c>var CallHierarchyIncomingCallsInfo = RequestInfo[*CallHierarchyIncomingCallsParams, CallHierarchyIncomingCallsResponse]{Method: MethodCallHierarchyIncomingCalls}</c>（lsp_generated.go:11269）。</summary>
    public static readonly RequestInfo<CallHierarchyIncomingCallsParams?, CallHierarchyIncomingCallsOrNull> CallHierarchyIncomingCallsInfo = new(Method.CallHierarchyIncomingCalls, static d => d.DecodeNew<CallHierarchyIncomingCallsOrNull>());
    
    /// <summary>Go: <c>var CallHierarchyOutgoingCallsInfo = RequestInfo[*CallHierarchyOutgoingCallsParams, CallHierarchyOutgoingCallsResponse]{Method: MethodCallHierarchyOutgoingCalls}</c>（lsp_generated.go:11275）。</summary>
    public static readonly RequestInfo<CallHierarchyOutgoingCallsParams?, CallHierarchyOutgoingCallsOrNull> CallHierarchyOutgoingCallsInfo = new(Method.CallHierarchyOutgoingCalls, static d => d.DecodeNew<CallHierarchyOutgoingCallsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentSemanticTokensFullInfo = RequestInfo[*SemanticTokensParams, SemanticTokensResponse]{Method: MethodTextDocumentSemanticTokensFull}</c>（lsp_generated.go:11281）。</summary>
    public static readonly RequestInfo<SemanticTokensParams?, SemanticTokensOrNull> TextDocumentSemanticTokensFullInfo = new(Method.TextDocumentSemanticTokensFull, static d => d.DecodeNew<SemanticTokensOrNull>());
    
    /// <summary>Go: <c>var TextDocumentSemanticTokensFullDeltaInfo = RequestInfo[*SemanticTokensDeltaParams, SemanticTokensDeltaResponse]{Method: MethodTextDocumentSemanticTokensFullDelta}</c>（lsp_generated.go:11287）。</summary>
    public static readonly RequestInfo<SemanticTokensDeltaParams?, SemanticTokensOrSemanticTokensDeltaOrNull> TextDocumentSemanticTokensFullDeltaInfo = new(Method.TextDocumentSemanticTokensFullDelta, static d => d.DecodeNew<SemanticTokensOrSemanticTokensDeltaOrNull>());
    
    /// <summary>Go: <c>var TextDocumentSemanticTokensRangeInfo = RequestInfo[*SemanticTokensRangeParams, SemanticTokensRangeResponse]{Method: MethodTextDocumentSemanticTokensRange}</c>（lsp_generated.go:11293）。</summary>
    public static readonly RequestInfo<SemanticTokensRangeParams?, SemanticTokensOrNull> TextDocumentSemanticTokensRangeInfo = new(Method.TextDocumentSemanticTokensRange, static d => d.DecodeNew<SemanticTokensOrNull>());
    
    /// <summary>Go: <c>var WorkspaceSemanticTokensRefreshInfo = RequestInfo[NoParams, SemanticTokensRefreshResponse]{Method: MethodWorkspaceSemanticTokensRefresh}</c>（lsp_generated.go:11299）。</summary>
    public static readonly RequestInfo<NoParams, Null> WorkspaceSemanticTokensRefreshInfo = new(Method.WorkspaceSemanticTokensRefresh, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var WindowShowDocumentInfo = RequestInfo[*ShowDocumentParams, ShowDocumentResponse]{Method: MethodWindowShowDocument}</c>（lsp_generated.go:11305）。</summary>
    public static readonly RequestInfo<ShowDocumentParams?, ShowDocumentResult?> WindowShowDocumentInfo = new(Method.WindowShowDocument, static d => DecodeNullable<ShowDocumentResult>(d, static d => d.DecodeNew<ShowDocumentResult>()));
    
    /// <summary>Go: <c>var TextDocumentLinkedEditingRangeInfo = RequestInfo[*LinkedEditingRangeParams, LinkedEditingRangeResponse]{Method: MethodTextDocumentLinkedEditingRange}</c>（lsp_generated.go:11311）。</summary>
    public static readonly RequestInfo<LinkedEditingRangeParams?, LinkedEditingRangesOrNull> TextDocumentLinkedEditingRangeInfo = new(Method.TextDocumentLinkedEditingRange, static d => d.DecodeNew<LinkedEditingRangesOrNull>());
    
    /// <summary>Go: <c>var WorkspaceWillCreateFilesInfo = RequestInfo[*CreateFilesParams, WillCreateFilesResponse]{Method: MethodWorkspaceWillCreateFiles}</c>（lsp_generated.go:11317）。</summary>
    public static readonly RequestInfo<CreateFilesParams?, WorkspaceEditOrNull> WorkspaceWillCreateFilesInfo = new(Method.WorkspaceWillCreateFiles, static d => d.DecodeNew<WorkspaceEditOrNull>());
    
    /// <summary>Go: <c>var WorkspaceWillRenameFilesInfo = RequestInfo[*RenameFilesParams, WillRenameFilesResponse]{Method: MethodWorkspaceWillRenameFiles}</c>（lsp_generated.go:11323）。</summary>
    public static readonly RequestInfo<RenameFilesParams?, WorkspaceEditOrNull> WorkspaceWillRenameFilesInfo = new(Method.WorkspaceWillRenameFiles, static d => d.DecodeNew<WorkspaceEditOrNull>());
    
    /// <summary>Go: <c>var WorkspaceWillDeleteFilesInfo = RequestInfo[*DeleteFilesParams, WillDeleteFilesResponse]{Method: MethodWorkspaceWillDeleteFiles}</c>（lsp_generated.go:11329）。</summary>
    public static readonly RequestInfo<DeleteFilesParams?, WorkspaceEditOrNull> WorkspaceWillDeleteFilesInfo = new(Method.WorkspaceWillDeleteFiles, static d => d.DecodeNew<WorkspaceEditOrNull>());
    
    /// <summary>Go: <c>var TextDocumentMonikerInfo = RequestInfo[*MonikerParams, MonikerResponse]{Method: MethodTextDocumentMoniker}</c>（lsp_generated.go:11335）。</summary>
    public static readonly RequestInfo<MonikerParams?, MonikersOrNull> TextDocumentMonikerInfo = new(Method.TextDocumentMoniker, static d => d.DecodeNew<MonikersOrNull>());
    
    /// <summary>Go: <c>var TextDocumentPrepareTypeHierarchyInfo = RequestInfo[*TypeHierarchyPrepareParams, TypeHierarchyPrepareResponse]{Method: MethodTextDocumentPrepareTypeHierarchy}</c>（lsp_generated.go:11341）。</summary>
    public static readonly RequestInfo<TypeHierarchyPrepareParams?, TypeHierarchyItemsOrNull> TextDocumentPrepareTypeHierarchyInfo = new(Method.TextDocumentPrepareTypeHierarchy, static d => d.DecodeNew<TypeHierarchyItemsOrNull>());
    
    /// <summary>Go: <c>var TypeHierarchySupertypesInfo = RequestInfo[*TypeHierarchySupertypesParams, TypeHierarchySupertypesResponse]{Method: MethodTypeHierarchySupertypes}</c>（lsp_generated.go:11347）。</summary>
    public static readonly RequestInfo<TypeHierarchySupertypesParams?, TypeHierarchyItemsOrNull> TypeHierarchySupertypesInfo = new(Method.TypeHierarchySupertypes, static d => d.DecodeNew<TypeHierarchyItemsOrNull>());
    
    /// <summary>Go: <c>var TypeHierarchySubtypesInfo = RequestInfo[*TypeHierarchySubtypesParams, TypeHierarchySubtypesResponse]{Method: MethodTypeHierarchySubtypes}</c>（lsp_generated.go:11353）。</summary>
    public static readonly RequestInfo<TypeHierarchySubtypesParams?, TypeHierarchyItemsOrNull> TypeHierarchySubtypesInfo = new(Method.TypeHierarchySubtypes, static d => d.DecodeNew<TypeHierarchyItemsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentInlineValueInfo = RequestInfo[*InlineValueParams, InlineValueResponse]{Method: MethodTextDocumentInlineValue}</c>（lsp_generated.go:11359）。</summary>
    public static readonly RequestInfo<InlineValueParams?, InlineValuesOrNull> TextDocumentInlineValueInfo = new(Method.TextDocumentInlineValue, static d => d.DecodeNew<InlineValuesOrNull>());
    
    /// <summary>Go: <c>var WorkspaceInlineValueRefreshInfo = RequestInfo[NoParams, InlineValueRefreshResponse]{Method: MethodWorkspaceInlineValueRefresh}</c>（lsp_generated.go:11365）。</summary>
    public static readonly RequestInfo<NoParams, Null> WorkspaceInlineValueRefreshInfo = new(Method.WorkspaceInlineValueRefresh, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var TextDocumentInlayHintInfo = RequestInfo[*InlayHintParams, InlayHintResponse]{Method: MethodTextDocumentInlayHint}</c>（lsp_generated.go:11371）。</summary>
    public static readonly RequestInfo<InlayHintParams?, InlayHintsOrNull> TextDocumentInlayHintInfo = new(Method.TextDocumentInlayHint, static d => d.DecodeNew<InlayHintsOrNull>());
    
    /// <summary>Go: <c>var InlayHintResolveInfo = RequestInfo[*InlayHint, InlayHintResolveResponse]{Method: MethodInlayHintResolve}</c>（lsp_generated.go:11377）。</summary>
    public static readonly RequestInfo<InlayHint?, InlayHint?> InlayHintResolveInfo = new(Method.InlayHintResolve, static d => DecodeNullable<InlayHint>(d, static d => d.DecodeNew<InlayHint>()));
    
    /// <summary>Go: <c>var WorkspaceInlayHintRefreshInfo = RequestInfo[NoParams, InlayHintRefreshResponse]{Method: MethodWorkspaceInlayHintRefresh}</c>（lsp_generated.go:11383）。</summary>
    public static readonly RequestInfo<NoParams, Null> WorkspaceInlayHintRefreshInfo = new(Method.WorkspaceInlayHintRefresh, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var TextDocumentDiagnosticInfo = RequestInfo[*DocumentDiagnosticParams, DocumentDiagnosticResponse]{Method: MethodTextDocumentDiagnostic}</c>（lsp_generated.go:11389）。</summary>
    public static readonly RequestInfo<DocumentDiagnosticParams?, RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport> TextDocumentDiagnosticInfo = new(Method.TextDocumentDiagnostic, static d => d.DecodeNew<RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport>());
    
    /// <summary>Go: <c>var WorkspaceDiagnosticInfo = RequestInfo[*WorkspaceDiagnosticParams, WorkspaceDiagnosticResponse]{Method: MethodWorkspaceDiagnostic}</c>（lsp_generated.go:11395）。</summary>
    public static readonly RequestInfo<WorkspaceDiagnosticParams?, WorkspaceDiagnosticReport?> WorkspaceDiagnosticInfo = new(Method.WorkspaceDiagnostic, static d => DecodeNullable<WorkspaceDiagnosticReport>(d, static d => d.DecodeNew<WorkspaceDiagnosticReport>()));
    
    /// <summary>Go: <c>var WorkspaceDiagnosticRefreshInfo = RequestInfo[NoParams, DiagnosticRefreshResponse]{Method: MethodWorkspaceDiagnosticRefresh}</c>（lsp_generated.go:11401）。</summary>
    public static readonly RequestInfo<NoParams, Null> WorkspaceDiagnosticRefreshInfo = new(Method.WorkspaceDiagnosticRefresh, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var TextDocumentInlineCompletionInfo = RequestInfo[*InlineCompletionParams, InlineCompletionResponse]{Method: MethodTextDocumentInlineCompletion}</c>（lsp_generated.go:11407）。</summary>
    public static readonly RequestInfo<InlineCompletionParams?, InlineCompletionListOrItemsOrNull> TextDocumentInlineCompletionInfo = new(Method.TextDocumentInlineCompletion, static d => d.DecodeNew<InlineCompletionListOrItemsOrNull>());
    
    /// <summary>Go: <c>var WorkspaceTextDocumentContentInfo = RequestInfo[*TextDocumentContentParams, TextDocumentContentResponse]{Method: MethodWorkspaceTextDocumentContent}</c>（lsp_generated.go:11413）。</summary>
    public static readonly RequestInfo<TextDocumentContentParams?, TextDocumentContentResult?> WorkspaceTextDocumentContentInfo = new(Method.WorkspaceTextDocumentContent, static d => DecodeNullable<TextDocumentContentResult>(d, static d => d.DecodeNew<TextDocumentContentResult>()));
    
    /// <summary>Go: <c>var WorkspaceTextDocumentContentRefreshInfo = RequestInfo[*TextDocumentContentRefreshParams, TextDocumentContentRefreshResponse]{Method: MethodWorkspaceTextDocumentContentRefresh}</c>（lsp_generated.go:11419）。</summary>
    public static readonly RequestInfo<TextDocumentContentRefreshParams?, Null> WorkspaceTextDocumentContentRefreshInfo = new(Method.WorkspaceTextDocumentContentRefresh, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var ClientRegisterCapabilityInfo = RequestInfo[*RegistrationParams, RegistrationResponse]{Method: MethodClientRegisterCapability}</c>（lsp_generated.go:11425）。</summary>
    public static readonly RequestInfo<RegistrationParams?, Null> ClientRegisterCapabilityInfo = new(Method.ClientRegisterCapability, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var ClientUnregisterCapabilityInfo = RequestInfo[*UnregistrationParams, UnregistrationResponse]{Method: MethodClientUnregisterCapability}</c>（lsp_generated.go:11431）。</summary>
    public static readonly RequestInfo<UnregistrationParams?, Null> ClientUnregisterCapabilityInfo = new(Method.ClientUnregisterCapability, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var InitializeInfo = RequestInfo[*InitializeParams, InitializeResponse]{Method: MethodInitialize}</c>（lsp_generated.go:11437）。</summary>
    public static readonly RequestInfo<InitializeParams?, InitializeResult?> InitializeInfo = new(Method.Initialize, static d => DecodeNullable<InitializeResult>(d, static d => d.DecodeNew<InitializeResult>()));
    
    /// <summary>Go: <c>var ShutdownInfo = RequestInfo[NoParams, ShutdownResponse]{Method: MethodShutdown}</c>（lsp_generated.go:11443）。</summary>
    public static readonly RequestInfo<NoParams, Null> ShutdownInfo = new(Method.Shutdown, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var WindowShowMessageRequestInfo = RequestInfo[*ShowMessageRequestParams, ShowMessageResponse]{Method: MethodWindowShowMessageRequest}</c>（lsp_generated.go:11449）。</summary>
    public static readonly RequestInfo<ShowMessageRequestParams?, MessageActionItemOrNull> WindowShowMessageRequestInfo = new(Method.WindowShowMessageRequest, static d => d.DecodeNew<MessageActionItemOrNull>());
    
    /// <summary>Go: <c>var TextDocumentWillSaveWaitUntilInfo = RequestInfo[*WillSaveTextDocumentParams, WillSaveTextDocumentWaitUntilResponse]{Method: MethodTextDocumentWillSaveWaitUntil}</c>（lsp_generated.go:11455）。</summary>
    public static readonly RequestInfo<WillSaveTextDocumentParams?, TextEditsOrNull> TextDocumentWillSaveWaitUntilInfo = new(Method.TextDocumentWillSaveWaitUntil, static d => d.DecodeNew<TextEditsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentCompletionInfo = RequestInfo[*CompletionParams, CompletionResponse]{Method: MethodTextDocumentCompletion}</c>（lsp_generated.go:11461）。</summary>
    public static readonly RequestInfo<CompletionParams?, CompletionItemsOrListOrNull> TextDocumentCompletionInfo = new(Method.TextDocumentCompletion, static d => d.DecodeNew<CompletionItemsOrListOrNull>());
    
    /// <summary>Go: <c>var CompletionItemResolveInfo = RequestInfo[*CompletionItem, CompletionResolveResponse]{Method: MethodCompletionItemResolve}</c>（lsp_generated.go:11467）。</summary>
    public static readonly RequestInfo<CompletionItem?, CompletionItem?> CompletionItemResolveInfo = new(Method.CompletionItemResolve, static d => DecodeNullable<CompletionItem>(d, static d => d.DecodeNew<CompletionItem>()));
    
    /// <summary>Go: <c>var TextDocumentHoverInfo = RequestInfo[*HoverParams, HoverResponse]{Method: MethodTextDocumentHover}</c>（lsp_generated.go:11473）。</summary>
    public static readonly RequestInfo<HoverParams?, HoverOrNull> TextDocumentHoverInfo = new(Method.TextDocumentHover, static d => d.DecodeNew<HoverOrNull>());
    
    /// <summary>Go: <c>var TextDocumentSignatureHelpInfo = RequestInfo[*SignatureHelpParams, SignatureHelpResponse]{Method: MethodTextDocumentSignatureHelp}</c>（lsp_generated.go:11479）。</summary>
    public static readonly RequestInfo<SignatureHelpParams?, SignatureHelpOrNull> TextDocumentSignatureHelpInfo = new(Method.TextDocumentSignatureHelp, static d => d.DecodeNew<SignatureHelpOrNull>());
    
    /// <summary>Go: <c>var TextDocumentDefinitionInfo = RequestInfo[*DefinitionParams, DefinitionResponse]{Method: MethodTextDocumentDefinition}</c>（lsp_generated.go:11485）。</summary>
    public static readonly RequestInfo<DefinitionParams?, LocationOrLocationsOrDefinitionLinksOrNull> TextDocumentDefinitionInfo = new(Method.TextDocumentDefinition, static d => d.DecodeNew<LocationOrLocationsOrDefinitionLinksOrNull>());
    
    /// <summary>Go: <c>var TextDocumentReferencesInfo = RequestInfo[*ReferenceParams, ReferencesResponse]{Method: MethodTextDocumentReferences}</c>（lsp_generated.go:11491）。</summary>
    public static readonly RequestInfo<ReferenceParams?, LocationsOrNull> TextDocumentReferencesInfo = new(Method.TextDocumentReferences, static d => d.DecodeNew<LocationsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentDocumentHighlightInfo = RequestInfo[*DocumentHighlightParams, DocumentHighlightResponse]{Method: MethodTextDocumentDocumentHighlight}</c>（lsp_generated.go:11497）。</summary>
    public static readonly RequestInfo<DocumentHighlightParams?, DocumentHighlightsOrNull> TextDocumentDocumentHighlightInfo = new(Method.TextDocumentDocumentHighlight, static d => d.DecodeNew<DocumentHighlightsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentDocumentSymbolInfo = RequestInfo[*DocumentSymbolParams, DocumentSymbolResponse]{Method: MethodTextDocumentDocumentSymbol}</c>（lsp_generated.go:11503）。</summary>
    public static readonly RequestInfo<DocumentSymbolParams?, SymbolInformationsOrDocumentSymbolsOrNull> TextDocumentDocumentSymbolInfo = new(Method.TextDocumentDocumentSymbol, static d => d.DecodeNew<SymbolInformationsOrDocumentSymbolsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentCodeActionInfo = RequestInfo[*CodeActionParams, CodeActionResponse]{Method: MethodTextDocumentCodeAction}</c>（lsp_generated.go:11509）。</summary>
    public static readonly RequestInfo<CodeActionParams?, CommandOrCodeActionArrayOrNull> TextDocumentCodeActionInfo = new(Method.TextDocumentCodeAction, static d => d.DecodeNew<CommandOrCodeActionArrayOrNull>());
    
    /// <summary>Go: <c>var CodeActionResolveInfo = RequestInfo[*CodeAction, CodeActionResolveResponse]{Method: MethodCodeActionResolve}</c>（lsp_generated.go:11515）。</summary>
    public static readonly RequestInfo<CodeAction?, CodeAction?> CodeActionResolveInfo = new(Method.CodeActionResolve, static d => DecodeNullable<CodeAction>(d, static d => d.DecodeNew<CodeAction>()));
    
    /// <summary>Go: <c>var WorkspaceSymbolInfo = RequestInfo[*WorkspaceSymbolParams, WorkspaceSymbolResponse]{Method: MethodWorkspaceSymbol}</c>（lsp_generated.go:11521）。</summary>
    public static readonly RequestInfo<WorkspaceSymbolParams?, SymbolInformationsOrWorkspaceSymbolsOrNull> WorkspaceSymbolInfo = new(Method.WorkspaceSymbol, static d => d.DecodeNew<SymbolInformationsOrWorkspaceSymbolsOrNull>());
    
    /// <summary>Go: <c>var WorkspaceSymbolResolveInfo = RequestInfo[*WorkspaceSymbol, WorkspaceSymbolResolveResponse]{Method: MethodWorkspaceSymbolResolve}</c>（lsp_generated.go:11527）。</summary>
    public static readonly RequestInfo<WorkspaceSymbol?, WorkspaceSymbol?> WorkspaceSymbolResolveInfo = new(Method.WorkspaceSymbolResolve, static d => DecodeNullable<WorkspaceSymbol>(d, static d => d.DecodeNew<WorkspaceSymbol>()));
    
    /// <summary>Go: <c>var TextDocumentCodeLensInfo = RequestInfo[*CodeLensParams, CodeLensResponse]{Method: MethodTextDocumentCodeLens}</c>（lsp_generated.go:11533）。</summary>
    public static readonly RequestInfo<CodeLensParams?, CodeLensesOrNull> TextDocumentCodeLensInfo = new(Method.TextDocumentCodeLens, static d => d.DecodeNew<CodeLensesOrNull>());
    
    /// <summary>Go: <c>var CodeLensResolveInfo = RequestInfo[*CodeLens, CodeLensResolveResponse]{Method: MethodCodeLensResolve}</c>（lsp_generated.go:11539）。</summary>
    public static readonly RequestInfo<CodeLens?, CodeLens?> CodeLensResolveInfo = new(Method.CodeLensResolve, static d => DecodeNullable<CodeLens>(d, static d => d.DecodeNew<CodeLens>()));
    
    /// <summary>Go: <c>var WorkspaceCodeLensRefreshInfo = RequestInfo[NoParams, CodeLensRefreshResponse]{Method: MethodWorkspaceCodeLensRefresh}</c>（lsp_generated.go:11545）。</summary>
    public static readonly RequestInfo<NoParams, Null> WorkspaceCodeLensRefreshInfo = new(Method.WorkspaceCodeLensRefresh, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var TextDocumentDocumentLinkInfo = RequestInfo[*DocumentLinkParams, DocumentLinkResponse]{Method: MethodTextDocumentDocumentLink}</c>（lsp_generated.go:11551）。</summary>
    public static readonly RequestInfo<DocumentLinkParams?, DocumentLinksOrNull> TextDocumentDocumentLinkInfo = new(Method.TextDocumentDocumentLink, static d => d.DecodeNew<DocumentLinksOrNull>());
    
    /// <summary>Go: <c>var DocumentLinkResolveInfo = RequestInfo[*DocumentLink, DocumentLinkResolveResponse]{Method: MethodDocumentLinkResolve}</c>（lsp_generated.go:11557）。</summary>
    public static readonly RequestInfo<DocumentLink?, DocumentLink?> DocumentLinkResolveInfo = new(Method.DocumentLinkResolve, static d => DecodeNullable<DocumentLink>(d, static d => d.DecodeNew<DocumentLink>()));
    
    /// <summary>Go: <c>var TextDocumentFormattingInfo = RequestInfo[*DocumentFormattingParams, DocumentFormattingResponse]{Method: MethodTextDocumentFormatting}</c>（lsp_generated.go:11563）。</summary>
    public static readonly RequestInfo<DocumentFormattingParams?, TextEditsOrNull> TextDocumentFormattingInfo = new(Method.TextDocumentFormatting, static d => d.DecodeNew<TextEditsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentRangeFormattingInfo = RequestInfo[*DocumentRangeFormattingParams, DocumentRangeFormattingResponse]{Method: MethodTextDocumentRangeFormatting}</c>（lsp_generated.go:11569）。</summary>
    public static readonly RequestInfo<DocumentRangeFormattingParams?, TextEditsOrNull> TextDocumentRangeFormattingInfo = new(Method.TextDocumentRangeFormatting, static d => d.DecodeNew<TextEditsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentRangesFormattingInfo = RequestInfo[*DocumentRangesFormattingParams, DocumentRangesFormattingResponse]{Method: MethodTextDocumentRangesFormatting}</c>（lsp_generated.go:11575）。</summary>
    public static readonly RequestInfo<DocumentRangesFormattingParams?, TextEditsOrNull> TextDocumentRangesFormattingInfo = new(Method.TextDocumentRangesFormatting, static d => d.DecodeNew<TextEditsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentOnTypeFormattingInfo = RequestInfo[*DocumentOnTypeFormattingParams, DocumentOnTypeFormattingResponse]{Method: MethodTextDocumentOnTypeFormatting}</c>（lsp_generated.go:11581）。</summary>
    public static readonly RequestInfo<DocumentOnTypeFormattingParams?, TextEditsOrNull> TextDocumentOnTypeFormattingInfo = new(Method.TextDocumentOnTypeFormatting, static d => d.DecodeNew<TextEditsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentRenameInfo = RequestInfo[*RenameParams, RenameResponse]{Method: MethodTextDocumentRename}</c>（lsp_generated.go:11587）。</summary>
    public static readonly RequestInfo<RenameParams?, WorkspaceEditOrNull> TextDocumentRenameInfo = new(Method.TextDocumentRename, static d => d.DecodeNew<WorkspaceEditOrNull>());
    
    /// <summary>Go: <c>var TextDocumentPrepareRenameInfo = RequestInfo[*PrepareRenameParams, PrepareRenameResponse]{Method: MethodTextDocumentPrepareRename}</c>（lsp_generated.go:11593）。</summary>
    public static readonly RequestInfo<PrepareRenameParams?, RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull> TextDocumentPrepareRenameInfo = new(Method.TextDocumentPrepareRename, static d => d.DecodeNew<RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull>());
    
    /// <summary>Go: <c>var WorkspaceExecuteCommandInfo = RequestInfo[*ExecuteCommandParams, ExecuteCommandResponse]{Method: MethodWorkspaceExecuteCommand}</c>（lsp_generated.go:11599）。</summary>
    public static readonly RequestInfo<ExecuteCommandParams?, LSPAnyOrNull> WorkspaceExecuteCommandInfo = new(Method.WorkspaceExecuteCommand, static d => d.DecodeNew<LSPAnyOrNull>());
    
    /// <summary>Go: <c>var WorkspaceApplyEditInfo = RequestInfo[*ApplyWorkspaceEditParams, ApplyWorkspaceEditResponse]{Method: MethodWorkspaceApplyEdit}</c>（lsp_generated.go:11605）。</summary>
    public static readonly RequestInfo<ApplyWorkspaceEditParams?, ApplyWorkspaceEditResult?> WorkspaceApplyEditInfo = new(Method.WorkspaceApplyEdit, static d => DecodeNullable<ApplyWorkspaceEditResult>(d, static d => d.DecodeNew<ApplyWorkspaceEditResult>()));
    
    /// <summary>Go: <c>var CustomRunGCInfo = RequestInfo[NoParams, RunGCResponse]{Method: MethodCustomRunGC}</c>（lsp_generated.go:11611）。</summary>
    public static readonly RequestInfo<NoParams, Null> CustomRunGCInfo = new(Method.CustomRunGC, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var CustomSaveHeapProfileInfo = RequestInfo[*ProfileParams, SaveHeapProfileResponse]{Method: MethodCustomSaveHeapProfile}</c>（lsp_generated.go:11617）。</summary>
    public static readonly RequestInfo<ProfileParams?, ProfileResult?> CustomSaveHeapProfileInfo = new(Method.CustomSaveHeapProfile, static d => DecodeNullable<ProfileResult>(d, static d => d.DecodeNew<ProfileResult>()));
    
    /// <summary>Go: <c>var CustomSaveAllocProfileInfo = RequestInfo[*ProfileParams, SaveAllocProfileResponse]{Method: MethodCustomSaveAllocProfile}</c>（lsp_generated.go:11623）。</summary>
    public static readonly RequestInfo<ProfileParams?, ProfileResult?> CustomSaveAllocProfileInfo = new(Method.CustomSaveAllocProfile, static d => DecodeNullable<ProfileResult>(d, static d => d.DecodeNew<ProfileResult>()));
    
    /// <summary>Go: <c>var CustomStartCPUProfileInfo = RequestInfo[*ProfileParams, StartCPUProfileResponse]{Method: MethodCustomStartCPUProfile}</c>（lsp_generated.go:11629）。</summary>
    public static readonly RequestInfo<ProfileParams?, Null> CustomStartCPUProfileInfo = new(Method.CustomStartCPUProfile, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var CustomStopCPUProfileInfo = RequestInfo[NoParams, StopCPUProfileResponse]{Method: MethodCustomStopCPUProfile}</c>（lsp_generated.go:11635）。</summary>
    public static readonly RequestInfo<NoParams, ProfileResult?> CustomStopCPUProfileInfo = new(Method.CustomStopCPUProfile, static d => DecodeNullable<ProfileResult>(d, static d => d.DecodeNew<ProfileResult>()));
    
    /// <summary>Go: <c>var CustomInitializeAPISessionInfo = RequestInfo[*InitializeAPISessionParams, CustomInitializeAPISessionResponse]{Method: MethodCustomInitializeAPISession}</c>（lsp_generated.go:11641）。</summary>
    public static readonly RequestInfo<InitializeAPISessionParams?, InitializeAPISessionResult?> CustomInitializeAPISessionInfo = new(Method.CustomInitializeAPISession, static d => DecodeNullable<InitializeAPISessionResult>(d, static d => d.DecodeNew<InitializeAPISessionResult>()));
    
    /// <summary>Go: <c>var CustomProjectInfoInfo = RequestInfo[*ProjectInfoParams, CustomProjectInfoResponse]{Method: MethodCustomProjectInfo}</c>（lsp_generated.go:11647）。</summary>
    public static readonly RequestInfo<ProjectInfoParams?, ProjectInfoResult?> CustomProjectInfoInfo = new(Method.CustomProjectInfo, static d => DecodeNullable<ProjectInfoResult>(d, static d => d.DecodeNew<ProjectInfoResult>()));
    
    /// <summary>Go: <c>var CustomSetContentMapperContributionsInfo = RequestInfo[*SetContentMapperContributionsParams, CustomSetContentMapperContributionsResponse]{Method: MethodCustomSetContentMapperContributions}</c>（lsp_generated.go:11653）。</summary>
    public static readonly RequestInfo<SetContentMapperContributionsParams?, Null> CustomSetContentMapperContributionsInfo = new(Method.CustomSetContentMapperContributions, static d => d.DecodeNew<Null>());
    
    /// <summary>Go: <c>var CustomTextDocumentSourceDefinitionInfo = RequestInfo[*TextDocumentPositionParams, CustomTextDocumentSourceDefinitionResponse]{Method: MethodCustomTextDocumentSourceDefinition}</c>（lsp_generated.go:11659）。</summary>
    public static readonly RequestInfo<TextDocumentPositionParams?, LocationOrLocationsOrDefinitionLinksOrNull?> CustomTextDocumentSourceDefinitionInfo = new(Method.CustomTextDocumentSourceDefinition, static d => DecodeNullable<LocationOrLocationsOrDefinitionLinksOrNull>(d, static d => d.DecodeNew<LocationOrLocationsOrDefinitionLinksOrNull>()));
    
    /// <summary>Go: <c>var CustomTextDocumentMultiDocumentHighlightInfo = RequestInfo[*MultiDocumentHighlightParams, CustomMultiDocumentHighlightResponse]{Method: MethodCustomTextDocumentMultiDocumentHighlight}</c>（lsp_generated.go:11665）。</summary>
    public static readonly RequestInfo<MultiDocumentHighlightParams?, MultiDocumentHighlightsOrNull> CustomTextDocumentMultiDocumentHighlightInfo = new(Method.CustomTextDocumentMultiDocumentHighlight, static d => d.DecodeNew<MultiDocumentHighlightsOrNull>());
    
    /// <summary>Go: <c>var TextDocumentVSOnAutoInsertInfo = RequestInfo[*VSOnAutoInsertParams, VSOnAutoInsertResponse]{Method: MethodTextDocumentVSOnAutoInsert}</c>（lsp_generated.go:11671）。</summary>
    public static readonly RequestInfo<VSOnAutoInsertParams?, VSOnAutoInsertResponseItemOrNull> TextDocumentVSOnAutoInsertInfo = new(Method.TextDocumentVSOnAutoInsert, static d => d.DecodeNew<VSOnAutoInsertResponseItemOrNull>());
    
    /// <summary>Go: <c>var TextDocumentVSReferencesInfo = RequestInfo[*ReferenceParams, VSReferencesResponse]{Method: MethodTextDocumentVSReferences}</c>（lsp_generated.go:11677）。</summary>
    public static readonly RequestInfo<ReferenceParams?, VSReferenceItemsOrNull> TextDocumentVSReferencesInfo = new(Method.TextDocumentVSReferences, static d => d.DecodeNew<VSReferenceItemsOrNull>());
    
    /// <summary>Go: <c>var WorkspaceDidChangeWorkspaceFoldersInfo = NotificationInfo[*DidChangeWorkspaceFoldersParams]{Method: MethodWorkspaceDidChangeWorkspaceFolders}</c>（lsp_generated.go:11680）。</summary>
    public static readonly NotificationInfo<DidChangeWorkspaceFoldersParams?> WorkspaceDidChangeWorkspaceFoldersInfo = new(Method.WorkspaceDidChangeWorkspaceFolders);
    
    /// <summary>Go: <c>var WindowWorkDoneProgressCancelInfo = NotificationInfo[*WorkDoneProgressCancelParams]{Method: MethodWindowWorkDoneProgressCancel}</c>（lsp_generated.go:11683）。</summary>
    public static readonly NotificationInfo<WorkDoneProgressCancelParams?> WindowWorkDoneProgressCancelInfo = new(Method.WindowWorkDoneProgressCancel);
    
    /// <summary>Go: <c>var WorkspaceDidCreateFilesInfo = NotificationInfo[*CreateFilesParams]{Method: MethodWorkspaceDidCreateFiles}</c>（lsp_generated.go:11686）。</summary>
    public static readonly NotificationInfo<CreateFilesParams?> WorkspaceDidCreateFilesInfo = new(Method.WorkspaceDidCreateFiles);
    
    /// <summary>Go: <c>var WorkspaceDidRenameFilesInfo = NotificationInfo[*RenameFilesParams]{Method: MethodWorkspaceDidRenameFiles}</c>（lsp_generated.go:11689）。</summary>
    public static readonly NotificationInfo<RenameFilesParams?> WorkspaceDidRenameFilesInfo = new(Method.WorkspaceDidRenameFiles);
    
    /// <summary>Go: <c>var WorkspaceDidDeleteFilesInfo = NotificationInfo[*DeleteFilesParams]{Method: MethodWorkspaceDidDeleteFiles}</c>（lsp_generated.go:11692）。</summary>
    public static readonly NotificationInfo<DeleteFilesParams?> WorkspaceDidDeleteFilesInfo = new(Method.WorkspaceDidDeleteFiles);
    
    /// <summary>Go: <c>var InitializedInfo = NotificationInfo[*InitializedParams]{Method: MethodInitialized}</c>（lsp_generated.go:11695）。</summary>
    public static readonly NotificationInfo<InitializedParams?> InitializedInfo = new(Method.Initialized);
    
    /// <summary>Go: <c>var ExitInfo = NotificationInfo[NoParams]{Method: MethodExit}</c>（lsp_generated.go:11698）。</summary>
    public static readonly NotificationInfo<NoParams> ExitInfo = new(Method.Exit);
    
    /// <summary>Go: <c>var WorkspaceDidChangeConfigurationInfo = NotificationInfo[*DidChangeConfigurationParams]{Method: MethodWorkspaceDidChangeConfiguration}</c>（lsp_generated.go:11701）。</summary>
    public static readonly NotificationInfo<DidChangeConfigurationParams?> WorkspaceDidChangeConfigurationInfo = new(Method.WorkspaceDidChangeConfiguration);
    
    /// <summary>Go: <c>var WindowShowMessageInfo = NotificationInfo[*ShowMessageParams]{Method: MethodWindowShowMessage}</c>（lsp_generated.go:11704）。</summary>
    public static readonly NotificationInfo<ShowMessageParams?> WindowShowMessageInfo = new(Method.WindowShowMessage);
    
    /// <summary>Go: <c>var WindowLogMessageInfo = NotificationInfo[*LogMessageParams]{Method: MethodWindowLogMessage}</c>（lsp_generated.go:11707）。</summary>
    public static readonly NotificationInfo<LogMessageParams?> WindowLogMessageInfo = new(Method.WindowLogMessage);
    
    /// <summary>Go: <c>var TelemetryEventInfo = NotificationInfo[TelemetryEvent]{Method: MethodTelemetryEvent}</c>（lsp_generated.go:11710）。</summary>
    public static readonly NotificationInfo<RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull> TelemetryEventInfo = new(Method.TelemetryEvent);
    
    /// <summary>Go: <c>var TextDocumentDidOpenInfo = NotificationInfo[*DidOpenTextDocumentParams]{Method: MethodTextDocumentDidOpen}</c>（lsp_generated.go:11713）。</summary>
    public static readonly NotificationInfo<DidOpenTextDocumentParams?> TextDocumentDidOpenInfo = new(Method.TextDocumentDidOpen);
    
    /// <summary>Go: <c>var TextDocumentDidChangeInfo = NotificationInfo[*DidChangeTextDocumentParams]{Method: MethodTextDocumentDidChange}</c>（lsp_generated.go:11716）。</summary>
    public static readonly NotificationInfo<DidChangeTextDocumentParams?> TextDocumentDidChangeInfo = new(Method.TextDocumentDidChange);
    
    /// <summary>Go: <c>var TextDocumentDidCloseInfo = NotificationInfo[*DidCloseTextDocumentParams]{Method: MethodTextDocumentDidClose}</c>（lsp_generated.go:11719）。</summary>
    public static readonly NotificationInfo<DidCloseTextDocumentParams?> TextDocumentDidCloseInfo = new(Method.TextDocumentDidClose);
    
    /// <summary>Go: <c>var TextDocumentDidSaveInfo = NotificationInfo[*DidSaveTextDocumentParams]{Method: MethodTextDocumentDidSave}</c>（lsp_generated.go:11722）。</summary>
    public static readonly NotificationInfo<DidSaveTextDocumentParams?> TextDocumentDidSaveInfo = new(Method.TextDocumentDidSave);
    
    /// <summary>Go: <c>var TextDocumentWillSaveInfo = NotificationInfo[*WillSaveTextDocumentParams]{Method: MethodTextDocumentWillSave}</c>（lsp_generated.go:11725）。</summary>
    public static readonly NotificationInfo<WillSaveTextDocumentParams?> TextDocumentWillSaveInfo = new(Method.TextDocumentWillSave);
    
    /// <summary>Go: <c>var WorkspaceDidChangeWatchedFilesInfo = NotificationInfo[*DidChangeWatchedFilesParams]{Method: MethodWorkspaceDidChangeWatchedFiles}</c>（lsp_generated.go:11728）。</summary>
    public static readonly NotificationInfo<DidChangeWatchedFilesParams?> WorkspaceDidChangeWatchedFilesInfo = new(Method.WorkspaceDidChangeWatchedFiles);
    
    /// <summary>Go: <c>var TextDocumentPublishDiagnosticsInfo = NotificationInfo[*PublishDiagnosticsParams]{Method: MethodTextDocumentPublishDiagnostics}</c>（lsp_generated.go:11731）。</summary>
    public static readonly NotificationInfo<PublishDiagnosticsParams?> TextDocumentPublishDiagnosticsInfo = new(Method.TextDocumentPublishDiagnostics);
    
    /// <summary>Go: <c>var SetTraceInfo = NotificationInfo[*SetTraceParams]{Method: MethodSetTrace}</c>（lsp_generated.go:11734）。</summary>
    public static readonly NotificationInfo<SetTraceParams?> SetTraceInfo = new(Method.SetTrace);
    
    /// <summary>Go: <c>var LogTraceInfo = NotificationInfo[*LogTraceParams]{Method: MethodLogTrace}</c>（lsp_generated.go:11737）。</summary>
    public static readonly NotificationInfo<LogTraceParams?> LogTraceInfo = new(Method.LogTrace);
    
    /// <summary>Go: <c>var CancelRequestInfo = NotificationInfo[*CancelParams]{Method: MethodCancelRequest}</c>（lsp_generated.go:11740）。</summary>
    public static readonly NotificationInfo<CancelParams?> CancelRequestInfo = new(Method.CancelRequest);
    
    /// <summary>Go: <c>var ProgressInfo = NotificationInfo[*ProgressParams]{Method: MethodProgress}</c>（lsp_generated.go:11743）。</summary>
    public static readonly NotificationInfo<ProgressParams?> ProgressInfo = new(Method.Progress);
    
    /// <summary>Go: <c>var CustomSetLogVerbosityInfo = NotificationInfo[*SetLogVerbosityParams]{Method: MethodCustomSetLogVerbosity}</c>（lsp_generated.go:11746）。</summary>
    public static readonly NotificationInfo<SetLogVerbosityParams?> CustomSetLogVerbosityInfo = new(Method.CustomSetLogVerbosity);
    
}
