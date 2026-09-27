// generated from: internal/lsp/lsproto/lsp_generated.go（Resolved capability types：行 15224-…）
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

/// <summary>Go 的包级函数 <c>func derefOr[T any](v *T) T</c> 落在本 package 的同名静态类里（docs/architecture.md §2）。</summary>
public static partial class LsProto
{
    /// <summary>Go: <c>func derefOr[T any](v *T) T</c>（lsp_generated.go:15225）。</summary>
    internal static T? DerefOr<T>(T? value) => value is null ? default : value;
}

/// <summary>Go: <c>type ResolvedChangeAnnotationsSupportOptions struct</c>（lsp_generated.go:15237）。
/// ResolvedChangeAnnotationsSupportOptions is a resolved version of ChangeAnnotationsSupportOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedChangeAnnotationsSupportOptions
{
    public bool GroupsOnLabel = false;
}

/// <summary>Go: <c>type ResolvedWorkspaceEditClientCapabilities struct</c>（lsp_generated.go:15255）。
/// ResolvedWorkspaceEditClientCapabilities is a resolved version of WorkspaceEditClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedWorkspaceEditClientCapabilities
{
    public bool DocumentChanges = false;

    public ResourceOperationKind[]? ResourceOperations;

    public FailureHandlingKind FailureHandling;

    public bool NormalizesLineEndings = false;

    public ResolvedChangeAnnotationsSupportOptions ChangeAnnotationSupport = new();

    public bool MetadataSupport = false;

    public bool SnippetEditSupport = false;
}

/// <summary>Go: <c>type ResolvedDidChangeConfigurationClientCapabilities struct</c>（lsp_generated.go:15308）。
/// ResolvedDidChangeConfigurationClientCapabilities is a resolved version of DidChangeConfigurationClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedDidChangeConfigurationClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedDidChangeWatchedFilesClientCapabilities struct</c>（lsp_generated.go:15324）。
/// ResolvedDidChangeWatchedFilesClientCapabilities is a resolved version of DidChangeWatchedFilesClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedDidChangeWatchedFilesClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool RelativePatternSupport = false;
}

/// <summary>Go: <c>type ResolvedClientSymbolKindOptions struct</c>（lsp_generated.go:15350）。
/// ResolvedClientSymbolKindOptions is a resolved version of ClientSymbolKindOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSymbolKindOptions
{
    public SymbolKind[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedClientSymbolTagOptions struct</c>（lsp_generated.go:15375）。
/// ResolvedClientSymbolTagOptions is a resolved version of ClientSymbolTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSymbolTagOptions
{
    public SymbolTag[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedClientSymbolResolveOptions struct</c>（lsp_generated.go:15393）。
/// ResolvedClientSymbolResolveOptions is a resolved version of ClientSymbolResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSymbolResolveOptions
{
    public string[]? Properties;
}

/// <summary>Go: <c>type ResolvedWorkspaceSymbolClientCapabilities struct</c>（lsp_generated.go:15412）。
/// ResolvedWorkspaceSymbolClientCapabilities is a resolved version of WorkspaceSymbolClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for a WorkspaceSymbolRequest.
/// </summary>
public sealed class ResolvedWorkspaceSymbolClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientSymbolKindOptions SymbolKind = new();

    public ResolvedClientSymbolTagOptions TagSupport = new();

    public ResolvedClientSymbolResolveOptions ResolveSupport = new();
}

/// <summary>Go: <c>type ResolvedExecuteCommandClientCapabilities struct</c>（lsp_generated.go:15446）。
/// ResolvedExecuteCommandClientCapabilities is a resolved version of ExecuteCommandClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The client capabilities of a ExecuteCommandRequest.
/// </summary>
public sealed class ResolvedExecuteCommandClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedSemanticTokensWorkspaceClientCapabilities struct</c>（lsp_generated.go:15464）。
/// ResolvedSemanticTokensWorkspaceClientCapabilities is a resolved version of SemanticTokensWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedSemanticTokensWorkspaceClientCapabilities
{
    public bool RefreshSupport = false;
}

/// <summary>Go: <c>type ResolvedCodeLensWorkspaceClientCapabilities struct</c>（lsp_generated.go:15488）。
/// ResolvedCodeLensWorkspaceClientCapabilities is a resolved version of CodeLensWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedCodeLensWorkspaceClientCapabilities
{
    public bool RefreshSupport = false;
}

/// <summary>Go: <c>type ResolvedFileOperationClientCapabilities struct</c>（lsp_generated.go:15517）。
/// ResolvedFileOperationClientCapabilities is a resolved version of FileOperationClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Capabilities relating to events from file operations by the user in the client.
/// 
/// These events do not come from the file system, they come from user operations
/// like renaming a file in the UI.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedFileOperationClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool DidCreate = false;

    public bool WillCreate = false;

    public bool DidRename = false;

    public bool WillRename = false;

    public bool DidDelete = false;

    public bool WillDelete = false;
}

/// <summary>Go: <c>type ResolvedInlineValueWorkspaceClientCapabilities struct</c>（lsp_generated.go:15555）。
/// ResolvedInlineValueWorkspaceClientCapabilities is a resolved version of InlineValueWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client workspace capabilities specific to inline values.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlineValueWorkspaceClientCapabilities
{
    public bool RefreshSupport = false;
}

/// <summary>Go: <c>type ResolvedInlayHintWorkspaceClientCapabilities struct</c>（lsp_generated.go:15581）。
/// ResolvedInlayHintWorkspaceClientCapabilities is a resolved version of InlayHintWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client workspace capabilities specific to inlay hints.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlayHintWorkspaceClientCapabilities
{
    public bool RefreshSupport = false;
}

/// <summary>Go: <c>type ResolvedDiagnosticWorkspaceClientCapabilities struct</c>（lsp_generated.go:15607）。
/// ResolvedDiagnosticWorkspaceClientCapabilities is a resolved version of DiagnosticWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Workspace client capabilities specific to diagnostic pull requests.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedDiagnosticWorkspaceClientCapabilities
{
    public bool RefreshSupport = false;
}

/// <summary>Go: <c>type ResolvedFoldingRangeWorkspaceClientCapabilities struct</c>（lsp_generated.go:15633）。
/// ResolvedFoldingRangeWorkspaceClientCapabilities is a resolved version of FoldingRangeWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// # Client workspace capabilities specific to folding ranges
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedFoldingRangeWorkspaceClientCapabilities
{
    public bool RefreshSupport = false;
}

/// <summary>Go: <c>type ResolvedTextDocumentContentClientCapabilities struct</c>（lsp_generated.go:15661）。
/// ResolvedTextDocumentContentClientCapabilities is a resolved version of TextDocumentContentClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for a text document content provider.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedTextDocumentContentClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedWorkspaceClientCapabilities struct</c>（lsp_generated.go:15679）。
/// ResolvedWorkspaceClientCapabilities is a resolved version of WorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Workspace specific client capabilities.
/// </summary>
public sealed class ResolvedWorkspaceClientCapabilities
{
    public bool ApplyEdit = false;

    public ResolvedWorkspaceEditClientCapabilities WorkspaceEdit = new();

    public ResolvedDidChangeConfigurationClientCapabilities DidChangeConfiguration = new();

    public ResolvedDidChangeWatchedFilesClientCapabilities DidChangeWatchedFiles = new();

    public ResolvedWorkspaceSymbolClientCapabilities Symbol = new();

    public ResolvedExecuteCommandClientCapabilities ExecuteCommand = new();

    public bool WorkspaceFolders = false;

    public bool Configuration = false;

    public ResolvedSemanticTokensWorkspaceClientCapabilities SemanticTokens = new();

    public ResolvedCodeLensWorkspaceClientCapabilities CodeLens = new();

    public ResolvedFileOperationClientCapabilities FileOperations = new();

    public ResolvedInlineValueWorkspaceClientCapabilities InlineValue = new();

    public ResolvedInlayHintWorkspaceClientCapabilities InlayHint = new();

    public ResolvedDiagnosticWorkspaceClientCapabilities Diagnostics = new();

    public ResolvedFoldingRangeWorkspaceClientCapabilities FoldingRange = new();

    public ResolvedTextDocumentContentClientCapabilities TextDocumentContent = new();
}

/// <summary>Go: <c>type ResolvedTextDocumentSyncClientCapabilities struct</c>（lsp_generated.go:15767）。
/// ResolvedTextDocumentSyncClientCapabilities is a resolved version of TextDocumentSyncClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedTextDocumentSyncClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool WillSave = false;

    public bool WillSaveWaitUntil = false;

    public bool DidSave = false;
}

/// <summary>Go: <c>type ResolvedTextDocumentFilterClientCapabilities struct</c>（lsp_generated.go:15794）。
/// ResolvedTextDocumentFilterClientCapabilities is a resolved version of TextDocumentFilterClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedTextDocumentFilterClientCapabilities
{
    public bool RelativePatternSupport = false;
}

/// <summary>Go: <c>type ResolvedCompletionItemTagOptions struct</c>（lsp_generated.go:15814）。
/// ResolvedCompletionItemTagOptions is a resolved version of CompletionItemTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedCompletionItemTagOptions
{
    public CompletionItemTag[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedClientCompletionItemResolveOptions struct</c>（lsp_generated.go:15832）。
/// ResolvedClientCompletionItemResolveOptions is a resolved version of ClientCompletionItemResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemResolveOptions
{
    public string[]? Properties;
}

/// <summary>Go: <c>type ResolvedClientCompletionItemInsertTextModeOptions struct</c>（lsp_generated.go:15850）。
/// ResolvedClientCompletionItemInsertTextModeOptions is a resolved version of ClientCompletionItemInsertTextModeOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemInsertTextModeOptions
{
    public InsertTextMode[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedClientCompletionItemOptions struct</c>（lsp_generated.go:15867）。
/// ResolvedClientCompletionItemOptions is a resolved version of ClientCompletionItemOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemOptions
{
    public bool SnippetSupport = false;

    public bool CommitCharactersSupport = false;

    public MarkupKind[]? DocumentationFormat;

    public bool DeprecatedSupport = false;

    public bool PreselectSupport = false;

    public ResolvedCompletionItemTagOptions TagSupport = new();

    public bool InsertReplaceSupport = false;

    public ResolvedClientCompletionItemResolveOptions ResolveSupport = new();

    public ResolvedClientCompletionItemInsertTextModeOptions InsertTextModeSupport = new();

    public bool LabelDetailsSupport = false;
}

/// <summary>Go: <c>type ResolvedClientCompletionItemOptionsKind struct</c>（lsp_generated.go:15937）。
/// ResolvedClientCompletionItemOptionsKind is a resolved version of ClientCompletionItemOptionsKind with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemOptionsKind
{
    public CompletionItemKind[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedCompletionListCapabilities struct</c>（lsp_generated.go:15965）。
/// ResolvedCompletionListCapabilities is a resolved version of CompletionListCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The client supports the following `CompletionList` specific
/// capabilities.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedCompletionListCapabilities
{
    public string[]? ItemDefaults;

    public bool ApplyKindSupport = false;
}

/// <summary>Go: <c>type ResolvedCompletionClientCapabilities struct</c>（lsp_generated.go:16003）。
/// ResolvedCompletionClientCapabilities is a resolved version of CompletionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Completion client capabilities
/// </summary>
public sealed class ResolvedCompletionClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientCompletionItemOptions CompletionItem = new();

    public ResolvedClientCompletionItemOptionsKind CompletionItemKind = new();

    public InsertTextMode InsertTextMode;

    public bool ContextSupport = false;

    public ResolvedCompletionListCapabilities CompletionList = new();
}

/// <summary>Go: <c>type ResolvedHoverClientCapabilities struct</c>（lsp_generated.go:16043）。
/// ResolvedHoverClientCapabilities is a resolved version of HoverClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedHoverClientCapabilities
{
    public bool DynamicRegistration = false;

    public MarkupKind[]? ContentFormat;
}

/// <summary>Go: <c>type ResolvedClientSignatureParameterInformationOptions struct</c>（lsp_generated.go:16065）。
/// ResolvedClientSignatureParameterInformationOptions is a resolved version of ClientSignatureParameterInformationOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSignatureParameterInformationOptions
{
    public bool LabelOffsetSupport = false;
}

/// <summary>Go: <c>type ResolvedClientSignatureInformationOptions struct</c>（lsp_generated.go:16086）。
/// ResolvedClientSignatureInformationOptions is a resolved version of ClientSignatureInformationOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSignatureInformationOptions
{
    public MarkupKind[]? DocumentationFormat;

    public ResolvedClientSignatureParameterInformationOptions ParameterInformation = new();

    public bool ActiveParameterSupport = false;

    public bool NoActiveParameterSupport = false;
}

/// <summary>Go: <c>type ResolvedSignatureHelpClientCapabilities struct</c>（lsp_generated.go:16121）。
/// ResolvedSignatureHelpClientCapabilities is a resolved version of SignatureHelpClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a SignatureHelpRequest.
/// </summary>
public sealed class ResolvedSignatureHelpClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientSignatureInformationOptions SignatureInformation = new();

    public bool ContextSupport = false;
}

/// <summary>Go: <c>type ResolvedDeclarationClientCapabilities struct</c>（lsp_generated.go:16151）。
/// ResolvedDeclarationClientCapabilities is a resolved version of DeclarationClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.14.0
/// </summary>
public sealed class ResolvedDeclarationClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;
}

/// <summary>Go: <c>type ResolvedDefinitionClientCapabilities struct</c>（lsp_generated.go:16174）。
/// ResolvedDefinitionClientCapabilities is a resolved version of DefinitionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a DefinitionRequest.
/// </summary>
public sealed class ResolvedDefinitionClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;
}

/// <summary>Go: <c>type ResolvedTypeDefinitionClientCapabilities struct</c>（lsp_generated.go:16197）。
/// ResolvedTypeDefinitionClientCapabilities is a resolved version of TypeDefinitionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since 3.6.0
/// </summary>
public sealed class ResolvedTypeDefinitionClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;
}

/// <summary>Go: <c>type ResolvedImplementationClientCapabilities struct</c>（lsp_generated.go:16222）。
/// ResolvedImplementationClientCapabilities is a resolved version of ImplementationClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.6.0
/// </summary>
public sealed class ResolvedImplementationClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;
}

/// <summary>Go: <c>type ResolvedReferenceClientCapabilities struct</c>（lsp_generated.go:16247）。
/// ResolvedReferenceClientCapabilities is a resolved version of ReferenceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a ReferencesRequest.
/// </summary>
public sealed class ResolvedReferenceClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedDocumentHighlightClientCapabilities struct</c>（lsp_generated.go:16265）。
/// ResolvedDocumentHighlightClientCapabilities is a resolved version of DocumentHighlightClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a DocumentHighlightRequest.
/// </summary>
public sealed class ResolvedDocumentHighlightClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedDocumentSymbolClientCapabilities struct</c>（lsp_generated.go:16283）。
/// ResolvedDocumentSymbolClientCapabilities is a resolved version of DocumentSymbolClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a DocumentSymbolRequest.
/// </summary>
public sealed class ResolvedDocumentSymbolClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientSymbolKindOptions SymbolKind = new();

    public bool HierarchicalDocumentSymbolSupport = false;

    public ResolvedClientSymbolTagOptions TagSupport = new();

    public bool LabelSupport = false;
}

/// <summary>Go: <c>type ResolvedClientCodeActionKindOptions struct</c>（lsp_generated.go:16321）。
/// ResolvedClientCodeActionKindOptions is a resolved version of ClientCodeActionKindOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeActionKindOptions
{
    public CodeActionKind[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedClientCodeActionLiteralOptions struct</c>（lsp_generated.go:16342）。
/// ResolvedClientCodeActionLiteralOptions is a resolved version of ClientCodeActionLiteralOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeActionLiteralOptions
{
    public ResolvedClientCodeActionKindOptions CodeActionKind = new();
}

/// <summary>Go: <c>type ResolvedClientCodeActionResolveOptions struct</c>（lsp_generated.go:16361）。
/// ResolvedClientCodeActionResolveOptions is a resolved version of ClientCodeActionResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeActionResolveOptions
{
    public string[]? Properties;
}

/// <summary>Go: <c>type ResolvedCodeActionTagOptions struct</c>（lsp_generated.go:16379）。
/// ResolvedCodeActionTagOptions is a resolved version of CodeActionTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedCodeActionTagOptions
{
    public CodeActionTag[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedCodeActionClientCapabilities struct</c>（lsp_generated.go:16397）。
/// ResolvedCodeActionClientCapabilities is a resolved version of CodeActionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The Client Capabilities of a CodeActionRequest.
/// </summary>
public sealed class ResolvedCodeActionClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientCodeActionLiteralOptions CodeActionLiteralSupport = new();

    public bool IsPreferredSupport = false;

    public bool DisabledSupport = false;

    public bool DataSupport = false;

    public ResolvedClientCodeActionResolveOptions ResolveSupport = new();

    public bool HonorsChangeAnnotations = false;

    public bool DocumentationSupport = false;

    public ResolvedCodeActionTagOptions TagSupport = new();
}

/// <summary>Go: <c>type ResolvedClientCodeLensResolveOptions struct</c>（lsp_generated.go:16466）。
/// ResolvedClientCodeLensResolveOptions is a resolved version of ClientCodeLensResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeLensResolveOptions
{
    public string[]? Properties;
}

/// <summary>Go: <c>type ResolvedCodeLensClientCapabilities struct</c>（lsp_generated.go:16484）。
/// ResolvedCodeLensClientCapabilities is a resolved version of CodeLensClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The client capabilities of a CodeLensRequest.
/// </summary>
public sealed class ResolvedCodeLensClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientCodeLensResolveOptions ResolveSupport = new();
}

/// <summary>Go: <c>type ResolvedDocumentLinkClientCapabilities struct</c>（lsp_generated.go:16508）。
/// ResolvedDocumentLinkClientCapabilities is a resolved version of DocumentLinkClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The client capabilities of a DocumentLinkRequest.
/// </summary>
public sealed class ResolvedDocumentLinkClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool TooltipSupport = false;
}

/// <summary>Go: <c>type ResolvedDocumentColorClientCapabilities struct</c>（lsp_generated.go:16529）。
/// ResolvedDocumentColorClientCapabilities is a resolved version of DocumentColorClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedDocumentColorClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedDocumentFormattingClientCapabilities struct</c>（lsp_generated.go:16549）。
/// ResolvedDocumentFormattingClientCapabilities is a resolved version of DocumentFormattingClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities of a DocumentFormattingRequest.
/// </summary>
public sealed class ResolvedDocumentFormattingClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedDocumentRangeFormattingClientCapabilities struct</c>（lsp_generated.go:16567）。
/// ResolvedDocumentRangeFormattingClientCapabilities is a resolved version of DocumentRangeFormattingClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities of a DocumentRangeFormattingRequest.
/// </summary>
public sealed class ResolvedDocumentRangeFormattingClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool RangesSupport = false;
}

/// <summary>Go: <c>type ResolvedDocumentOnTypeFormattingClientCapabilities struct</c>（lsp_generated.go:16590）。
/// ResolvedDocumentOnTypeFormattingClientCapabilities is a resolved version of DocumentOnTypeFormattingClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities of a DocumentOnTypeFormattingRequest.
/// </summary>
public sealed class ResolvedDocumentOnTypeFormattingClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedRenameClientCapabilities struct</c>（lsp_generated.go:16606）。
/// ResolvedRenameClientCapabilities is a resolved version of RenameClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedRenameClientCapabilities
{
    public bool DynamicRegistration = false;

    public bool PrepareSupport = false;

    public PrepareSupportDefaultBehavior PrepareSupportDefaultBehavior;

    public bool HonorsChangeAnnotations = false;
}

/// <summary>Go: <c>type ResolvedClientFoldingRangeKindOptions struct</c>（lsp_generated.go:16647）。
/// ResolvedClientFoldingRangeKindOptions is a resolved version of ClientFoldingRangeKindOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientFoldingRangeKindOptions
{
    public FoldingRangeKind[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedClientFoldingRangeOptions struct</c>（lsp_generated.go:16668）。
/// ResolvedClientFoldingRangeOptions is a resolved version of ClientFoldingRangeOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientFoldingRangeOptions
{
    public bool CollapsedText = false;
}

/// <summary>Go: <c>type ResolvedFoldingRangeClientCapabilities struct</c>（lsp_generated.go:16687）。
/// ResolvedFoldingRangeClientCapabilities is a resolved version of FoldingRangeClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedFoldingRangeClientCapabilities
{
    public bool DynamicRegistration = false;

    public uint RangeLimit;

    public bool LineFoldingOnly = false;

    public ResolvedClientFoldingRangeKindOptions FoldingRangeKind = new();

    public ResolvedClientFoldingRangeOptions FoldingRange = new();
}

/// <summary>Go: <c>type ResolvedSelectionRangeClientCapabilities struct</c>（lsp_generated.go:16726）。
/// ResolvedSelectionRangeClientCapabilities is a resolved version of SelectionRangeClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedSelectionRangeClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedClientDiagnosticsTagOptions struct</c>（lsp_generated.go:16746）。
/// ResolvedClientDiagnosticsTagOptions is a resolved version of ClientDiagnosticsTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientDiagnosticsTagOptions
{
    public DiagnosticTag[]? ValueSet;
}

/// <summary>Go: <c>type ResolvedPublishDiagnosticsClientCapabilities struct</c>（lsp_generated.go:16764）。
/// ResolvedPublishDiagnosticsClientCapabilities is a resolved version of PublishDiagnosticsClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The publish diagnostic client capabilities.
/// </summary>
public sealed class ResolvedPublishDiagnosticsClientCapabilities
{
    public bool RelatedInformation = false;

    public ResolvedClientDiagnosticsTagOptions TagSupport = new();

    public bool CodeDescriptionSupport = false;

    public bool DataSupport = false;

    public bool VersionSupport = false;
}

/// <summary>Go: <c>type ResolvedCallHierarchyClientCapabilities struct</c>（lsp_generated.go:16806）。
/// ResolvedCallHierarchyClientCapabilities is a resolved version of CallHierarchyClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedCallHierarchyClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedClientSemanticTokensRequestOptions struct</c>（lsp_generated.go:16826）。
/// ResolvedClientSemanticTokensRequestOptions is a resolved version of ClientSemanticTokensRequestOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSemanticTokensRequestOptions
{
    public BooleanOrEmptyObject Range = new();

    public BooleanOrClientSemanticTokensRequestFullDelta Full = new();
}

/// <summary>Go: <c>type ResolvedSemanticTokensClientCapabilities struct</c>（lsp_generated.go:16849）。
/// ResolvedSemanticTokensClientCapabilities is a resolved version of SemanticTokensClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedSemanticTokensClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientSemanticTokensRequestOptions Requests = new();

    public string[]? TokenTypes;

    public string[]? TokenModifiers;

    public TokenFormat[]? Formats;

    public bool OverlappingTokenSupport = false;

    public bool MultilineTokenSupport = false;

    public bool ServerCancelSupport = false;

    public bool AugmentsSyntaxTokens = false;
}

/// <summary>Go: <c>type ResolvedLinkedEditingRangeClientCapabilities struct</c>（lsp_generated.go:16916）。
/// ResolvedLinkedEditingRangeClientCapabilities is a resolved version of LinkedEditingRangeClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for the linked editing range request.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedLinkedEditingRangeClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedMonikerClientCapabilities struct</c>（lsp_generated.go:16938）。
/// ResolvedMonikerClientCapabilities is a resolved version of MonikerClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to the moniker request.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedMonikerClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedTypeHierarchyClientCapabilities struct</c>（lsp_generated.go:16958）。
/// ResolvedTypeHierarchyClientCapabilities is a resolved version of TypeHierarchyClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedTypeHierarchyClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedInlineValueClientCapabilities struct</c>（lsp_generated.go:16980）。
/// ResolvedInlineValueClientCapabilities is a resolved version of InlineValueClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to inline values.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlineValueClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedClientInlayHintResolveOptions struct</c>（lsp_generated.go:16998）。
/// ResolvedClientInlayHintResolveOptions is a resolved version of ClientInlayHintResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientInlayHintResolveOptions
{
    public string[]? Properties;
}

/// <summary>Go: <c>type ResolvedInlayHintClientCapabilities struct</c>（lsp_generated.go:17018）。
/// ResolvedInlayHintClientCapabilities is a resolved version of InlayHintClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Inlay hint client capabilities.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlayHintClientCapabilities
{
    public bool DynamicRegistration = false;

    public ResolvedClientInlayHintResolveOptions ResolveSupport = new();
}

/// <summary>Go: <c>type ResolvedDiagnosticClientCapabilities struct</c>（lsp_generated.go:17042）。
/// ResolvedDiagnosticClientCapabilities is a resolved version of DiagnosticClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to diagnostic pull requests.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedDiagnosticClientCapabilities
{
    public bool RelatedInformation = false;

    public ResolvedClientDiagnosticsTagOptions TagSupport = new();

    public bool CodeDescriptionSupport = false;

    public bool DataSupport = false;

    public bool DynamicRegistration = false;

    public bool RelatedDocumentSupport = false;

    public bool MarkupMessageSupport = false;
}

/// <summary>Go: <c>type ResolvedInlineCompletionClientCapabilities struct</c>（lsp_generated.go:17093）。
/// ResolvedInlineCompletionClientCapabilities is a resolved version of InlineCompletionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to inline completions.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedInlineCompletionClientCapabilities
{
    public bool DynamicRegistration = false;
}

/// <summary>Go: <c>type ResolvedTextDocumentClientCapabilities struct</c>（lsp_generated.go:17111）。
/// ResolvedTextDocumentClientCapabilities is a resolved version of TextDocumentClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Text document specific client capabilities.
/// </summary>
public sealed class ResolvedTextDocumentClientCapabilities
{
    public ResolvedTextDocumentSyncClientCapabilities Synchronization = new();

    public ResolvedTextDocumentFilterClientCapabilities Filters = new();

    public ResolvedCompletionClientCapabilities Completion = new();

    public ResolvedHoverClientCapabilities Hover = new();

    public ResolvedSignatureHelpClientCapabilities SignatureHelp = new();

    public ResolvedDeclarationClientCapabilities Declaration = new();

    public ResolvedDefinitionClientCapabilities Definition = new();

    public ResolvedTypeDefinitionClientCapabilities TypeDefinition = new();

    public ResolvedImplementationClientCapabilities Implementation = new();

    public ResolvedReferenceClientCapabilities References = new();

    public ResolvedDocumentHighlightClientCapabilities DocumentHighlight = new();

    public ResolvedDocumentSymbolClientCapabilities DocumentSymbol = new();

    public ResolvedCodeActionClientCapabilities CodeAction = new();

    public ResolvedCodeLensClientCapabilities CodeLens = new();

    public ResolvedDocumentLinkClientCapabilities DocumentLink = new();

    public ResolvedDocumentColorClientCapabilities ColorProvider = new();

    public ResolvedDocumentFormattingClientCapabilities Formatting = new();

    public ResolvedDocumentRangeFormattingClientCapabilities RangeFormatting = new();

    public ResolvedDocumentOnTypeFormattingClientCapabilities OnTypeFormatting = new();

    public ResolvedRenameClientCapabilities Rename = new();

    public ResolvedFoldingRangeClientCapabilities FoldingRange = new();

    public ResolvedSelectionRangeClientCapabilities SelectionRange = new();

    public ResolvedPublishDiagnosticsClientCapabilities PublishDiagnostics = new();

    public ResolvedCallHierarchyClientCapabilities CallHierarchy = new();

    public ResolvedSemanticTokensClientCapabilities SemanticTokens = new();

    public ResolvedLinkedEditingRangeClientCapabilities LinkedEditingRange = new();

    public ResolvedMonikerClientCapabilities Moniker = new();

    public ResolvedTypeHierarchyClientCapabilities TypeHierarchy = new();

    public ResolvedInlineValueClientCapabilities InlineValue = new();

    public ResolvedInlayHintClientCapabilities InlayHint = new();

    public ResolvedDiagnosticClientCapabilities Diagnostic = new();

    public ResolvedInlineCompletionClientCapabilities InlineCompletion = new();
}

/// <summary>Go: <c>type ResolvedClientShowMessageActionItemOptions struct</c>（lsp_generated.go:17255）。
/// ResolvedClientShowMessageActionItemOptions is a resolved version of ClientShowMessageActionItemOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientShowMessageActionItemOptions
{
    public bool AdditionalPropertiesSupport = false;
}

/// <summary>Go: <c>type ResolvedShowMessageRequestClientCapabilities struct</c>（lsp_generated.go:17275）。
/// ResolvedShowMessageRequestClientCapabilities is a resolved version of ShowMessageRequestClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Show message request client capabilities
/// </summary>
public sealed class ResolvedShowMessageRequestClientCapabilities
{
    public ResolvedClientShowMessageActionItemOptions MessageActionItem = new();
}

/// <summary>Go: <c>type ResolvedShowDocumentClientCapabilities struct</c>（lsp_generated.go:17295）。
/// ResolvedShowDocumentClientCapabilities is a resolved version of ShowDocumentClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for the showDocument request.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedShowDocumentClientCapabilities
{
    public bool Support = false;
}

/// <summary>Go: <c>type ResolvedWindowClientCapabilities struct</c>（lsp_generated.go:17312）。
/// ResolvedWindowClientCapabilities is a resolved version of WindowClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedWindowClientCapabilities
{
    public bool WorkDoneProgress = false;

    public ResolvedShowMessageRequestClientCapabilities ShowMessage = new();

    public ResolvedShowDocumentClientCapabilities ShowDocument = new();
}

/// <summary>Go: <c>type ResolvedStaleRequestSupportOptions struct</c>（lsp_generated.go:17348）。
/// ResolvedStaleRequestSupportOptions is a resolved version of StaleRequestSupportOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedStaleRequestSupportOptions
{
    public bool Cancel = false;

    public string[]? RetryOnContentModified;
}

/// <summary>Go: <c>type ResolvedRegularExpressionsClientCapabilities struct</c>（lsp_generated.go:17373）。
/// ResolvedRegularExpressionsClientCapabilities is a resolved version of RegularExpressionsClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to regular expressions.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedRegularExpressionsClientCapabilities
{
    public string Engine = "";

    public string Version = "";
}

/// <summary>Go: <c>type ResolvedMarkdownClientCapabilities struct</c>（lsp_generated.go:17396）。
/// ResolvedMarkdownClientCapabilities is a resolved version of MarkdownClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to the used markdown parser.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedMarkdownClientCapabilities
{
    public string Parser = "";

    public string Version = "";

    public string[]? AllowedTags;
}

/// <summary>Go: <c>type ResolvedGeneralClientCapabilities struct</c>（lsp_generated.go:17425）。
/// ResolvedGeneralClientCapabilities is a resolved version of GeneralClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// General client capabilities.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedGeneralClientCapabilities
{
    public ResolvedStaleRequestSupportOptions StaleRequestSupport = new();

    public ResolvedRegularExpressionsClientCapabilities RegularExpressions = new();

    public ResolvedMarkdownClientCapabilities Markdown = new();

    public PositionEncodingKind[]? PositionEncodings;
}

/// <summary>Go: <c>type ResolvedExperimentalClientCapabilities struct</c>（lsp_generated.go:17478）。
/// ResolvedExperimentalClientCapabilities is a resolved version of ExperimentalClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// ExperimentalClientCapabilities contains experimental capabilities under development.
/// </summary>
public sealed class ResolvedExperimentalClientCapabilities
{
    public bool HoverVerbosityLevel = false;
}

/// <summary>Go: <c>type ResolvedClientCapabilities struct</c>（lsp_generated.go:17497）。
/// ResolvedClientCapabilities is a version of ClientCapabilities where all nested
/// fields are values (not pointers), making it easier to access deeply nested capabilities.
/// Use (*ClientCapabilities).Resolve() to convert from ClientCapabilities.
/// 
/// Defines the capabilities provided by the client.
/// </summary>
public sealed class ResolvedClientCapabilities
{
    public ResolvedWorkspaceClientCapabilities Workspace = new();

    public ResolvedTextDocumentClientCapabilities TextDocument = new();

    public ResolvedWindowClientCapabilities Window = new();

    public ResolvedGeneralClientCapabilities General = new();

    public ResolvedExperimentalClientCapabilities Experimental = new();

    public bool VSSupportsVisualStudioExtensions = false;

    public int VSSupportedSnippetVersion;

    public bool VSSupportsNotIncludingTextInTextDocumentDidOpen = false;

    public bool VSSupportsIconExtensions = false;

    public bool VSSupportsDiagnosticRequests = false;
}
