// ported from: internal/lsp/lsproto/lsp_generated.go:4607-5213（Registration）
//
// 为什么手写：RegisterOptions 是**外部标记联合**（method ↔ 选项字段一一对应，字段本身没有 json tag），
// Go 的代码生成器对这两个方法做了特判（不像其它 390 个类型那样转发给 unmarshalStruct）。
// 本文件的形状与 Go 逐句对应，只有 C# 语言特性导致的必要差异：
//   * Go 的 `panic("RegisterOptions must be set")` → Debug.Fail（抛 DebugFailureException）；
//     Debug.Fail 没有 `[DoesNotReturn]`，所以其后补 `throw new System.Diagnostics.UnreachableException()`。
//   * Go 的 `json.Unmarshal(rawRegisterOptions, &v)` → `ReadOptions<T>(raw)`（C# 的 `new T()` 需要泛型约束，
//     逐臂写 `var v T` 无法复用；语义相同：解不进就抛 JsonException）。
//   * `countNonNil`（structcodec.go:316 的 reflect 枚举）在这里把 RegisterOptions 的 48 个字段逐个写死。
//   * Go 的 `switch { case … }` / `switch Method(method)`（方法名是常量）在 C# 里不能用 struct 做 case，
//     改成同序的 if/else 链，且**方法名一律走生成的 Method 常量**（不重复字符串字面量）。
//   * 成员名比较用 LsProto.JsonKeyCheck 直接比原始 token（避免为每个成员名解码出一个临时 string）。
//
// 字段与接口声明在 Lsp.Generated.Structures.cs（partial）。

using TypeScript.Json;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.LsProto;

public sealed partial class Registration
{
    /// <summary>Go: <c>func (s *Registration) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:4671）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        if (RegisterOptions is null)
        {
            Debug.Fail("RegisterOptions must be set");
            throw new System.Diagnostics.UnreachableException();
        }

        AssertOnlyOne("exactly one element of RegisterOptions should be set", CountNonNil(RegisterOptions));

        enc.WriteToken(BeginObject);
        enc.WriteToken(JsonToken.String("id"));
        MarshalEncode(enc, Id);
        enc.WriteToken(JsonToken.String("method"));

        // Go 的 `switch { case s.RegisterOptions.X != nil: }`：取第一个非 nil 字段，同时记下 method 与 opts。
        // assertOnlyOne 已保证恰好一个字段非 nil，而下面的链覆盖了计数用的同一批字段，所以不会落空（Go 的 switch 也没有 default）。
        var o = RegisterOptions;
        var method = default(Method);
        object? opts = null;
        if (o.TextDocumentImplementation is not null) { method = Method.TextDocumentImplementation; opts = o.TextDocumentImplementation; }
        else if (o.TextDocumentTypeDefinition is not null) { method = Method.TextDocumentTypeDefinition; opts = o.TextDocumentTypeDefinition; }
        else if (o.TextDocumentDocumentColor is not null) { method = Method.TextDocumentDocumentColor; opts = o.TextDocumentDocumentColor; }
        else if (o.TextDocumentColorPresentation is not null) { method = Method.TextDocumentColorPresentation; opts = o.TextDocumentColorPresentation; }
        else if (o.TextDocumentFoldingRange is not null) { method = Method.TextDocumentFoldingRange; opts = o.TextDocumentFoldingRange; }
        else if (o.TextDocumentDeclaration is not null) { method = Method.TextDocumentDeclaration; opts = o.TextDocumentDeclaration; }
        else if (o.TextDocumentSelectionRange is not null) { method = Method.TextDocumentSelectionRange; opts = o.TextDocumentSelectionRange; }
        else if (o.TextDocumentPrepareCallHierarchy is not null) { method = Method.TextDocumentPrepareCallHierarchy; opts = o.TextDocumentPrepareCallHierarchy; }
        else if (o.TextDocumentSemanticTokens is not null) { method = Method.TextDocumentSemanticTokens; opts = o.TextDocumentSemanticTokens; }
        else if (o.TextDocumentLinkedEditingRange is not null) { method = Method.TextDocumentLinkedEditingRange; opts = o.TextDocumentLinkedEditingRange; }
        else if (o.WorkspaceWillCreateFiles is not null) { method = Method.WorkspaceWillCreateFiles; opts = o.WorkspaceWillCreateFiles; }
        else if (o.WorkspaceWillRenameFiles is not null) { method = Method.WorkspaceWillRenameFiles; opts = o.WorkspaceWillRenameFiles; }
        else if (o.WorkspaceWillDeleteFiles is not null) { method = Method.WorkspaceWillDeleteFiles; opts = o.WorkspaceWillDeleteFiles; }
        else if (o.TextDocumentMoniker is not null) { method = Method.TextDocumentMoniker; opts = o.TextDocumentMoniker; }
        else if (o.TextDocumentPrepareTypeHierarchy is not null) { method = Method.TextDocumentPrepareTypeHierarchy; opts = o.TextDocumentPrepareTypeHierarchy; }
        else if (o.TextDocumentInlineValue is not null) { method = Method.TextDocumentInlineValue; opts = o.TextDocumentInlineValue; }
        else if (o.TextDocumentInlayHint is not null) { method = Method.TextDocumentInlayHint; opts = o.TextDocumentInlayHint; }
        else if (o.TextDocumentDiagnostic is not null) { method = Method.TextDocumentDiagnostic; opts = o.TextDocumentDiagnostic; }
        else if (o.TextDocumentInlineCompletion is not null) { method = Method.TextDocumentInlineCompletion; opts = o.TextDocumentInlineCompletion; }
        else if (o.WorkspaceTextDocumentContent is not null) { method = Method.WorkspaceTextDocumentContent; opts = o.WorkspaceTextDocumentContent; }
        else if (o.TextDocumentWillSaveWaitUntil is not null) { method = Method.TextDocumentWillSaveWaitUntil; opts = o.TextDocumentWillSaveWaitUntil; }
        else if (o.TextDocumentCompletion is not null) { method = Method.TextDocumentCompletion; opts = o.TextDocumentCompletion; }
        else if (o.TextDocumentHover is not null) { method = Method.TextDocumentHover; opts = o.TextDocumentHover; }
        else if (o.TextDocumentSignatureHelp is not null) { method = Method.TextDocumentSignatureHelp; opts = o.TextDocumentSignatureHelp; }
        else if (o.TextDocumentDefinition is not null) { method = Method.TextDocumentDefinition; opts = o.TextDocumentDefinition; }
        else if (o.TextDocumentReferences is not null) { method = Method.TextDocumentReferences; opts = o.TextDocumentReferences; }
        else if (o.TextDocumentDocumentHighlight is not null) { method = Method.TextDocumentDocumentHighlight; opts = o.TextDocumentDocumentHighlight; }
        else if (o.TextDocumentDocumentSymbol is not null) { method = Method.TextDocumentDocumentSymbol; opts = o.TextDocumentDocumentSymbol; }
        else if (o.TextDocumentCodeAction is not null) { method = Method.TextDocumentCodeAction; opts = o.TextDocumentCodeAction; }
        else if (o.WorkspaceSymbol is not null) { method = Method.WorkspaceSymbol; opts = o.WorkspaceSymbol; }
        else if (o.TextDocumentCodeLens is not null) { method = Method.TextDocumentCodeLens; opts = o.TextDocumentCodeLens; }
        else if (o.TextDocumentDocumentLink is not null) { method = Method.TextDocumentDocumentLink; opts = o.TextDocumentDocumentLink; }
        else if (o.TextDocumentFormatting is not null) { method = Method.TextDocumentFormatting; opts = o.TextDocumentFormatting; }
        else if (o.TextDocumentRangeFormatting is not null) { method = Method.TextDocumentRangeFormatting; opts = o.TextDocumentRangeFormatting; }
        else if (o.TextDocumentRangesFormatting is not null) { method = Method.TextDocumentRangesFormatting; opts = o.TextDocumentRangesFormatting; }
        else if (o.TextDocumentOnTypeFormatting is not null) { method = Method.TextDocumentOnTypeFormatting; opts = o.TextDocumentOnTypeFormatting; }
        else if (o.TextDocumentRename is not null) { method = Method.TextDocumentRename; opts = o.TextDocumentRename; }
        else if (o.WorkspaceExecuteCommand is not null) { method = Method.WorkspaceExecuteCommand; opts = o.WorkspaceExecuteCommand; }
        else if (o.WorkspaceDidCreateFiles is not null) { method = Method.WorkspaceDidCreateFiles; opts = o.WorkspaceDidCreateFiles; }
        else if (o.WorkspaceDidRenameFiles is not null) { method = Method.WorkspaceDidRenameFiles; opts = o.WorkspaceDidRenameFiles; }
        else if (o.WorkspaceDidDeleteFiles is not null) { method = Method.WorkspaceDidDeleteFiles; opts = o.WorkspaceDidDeleteFiles; }
        else if (o.WorkspaceDidChangeConfiguration is not null) { method = Method.WorkspaceDidChangeConfiguration; opts = o.WorkspaceDidChangeConfiguration; }
        else if (o.TextDocumentDidOpen is not null) { method = Method.TextDocumentDidOpen; opts = o.TextDocumentDidOpen; }
        else if (o.TextDocumentDidChange is not null) { method = Method.TextDocumentDidChange; opts = o.TextDocumentDidChange; }
        else if (o.TextDocumentDidClose is not null) { method = Method.TextDocumentDidClose; opts = o.TextDocumentDidClose; }
        else if (o.TextDocumentDidSave is not null) { method = Method.TextDocumentDidSave; opts = o.TextDocumentDidSave; }
        else if (o.TextDocumentWillSave is not null) { method = Method.TextDocumentWillSave; opts = o.TextDocumentWillSave; }
        else if (o.WorkspaceDidChangeWatchedFiles is not null) { method = Method.WorkspaceDidChangeWatchedFiles; opts = o.WorkspaceDidChangeWatchedFiles; }

        method.MarshalJSONTo(enc);
        enc.WriteToken(JsonToken.String("registerOptions"));
        MarshalEncode(enc, opts);
        enc.WriteToken(EndObject);
    }

    /// <summary>Go: <c>func (s *Registration) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:4851）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Id = "";
        RegisterOptions = null;

        // Go 的 `const ( missingId uint = 1 << iota; missingMethod; _missingLast )` + `missing := _missingLast - 1`。
        const int missingId = 1 << 0;
        const int missingMethod = 1 << 1;
        const int missingLast = 1 << 2;
        var missing = missingLast - 1;

        if (dec.PeekKind() != JsonKind.BeginObject)
        {
            throw ErrNotObject(dec.PeekKind());
        }

        dec.ReadToken();

        var method = "";
        byte[]? rawRegisterOptions = null;

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadValue();
            if (JsonKeyCheck(name, "id"))
            {
                missing &= ~missingId;
                if (dec.PeekKind() != JsonKind.Null) { Id = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
            else if (JsonKeyCheck(name, "method"))
            {
                missing &= ~missingMethod;
                if (dec.PeekKind() != JsonKind.Null) { method = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
            else if (JsonKeyCheck(name, "registerOptions"))
            {
                rawRegisterOptions = dec.ReadValue();
            }
            else
            {
                dec.SkipValue();
            }
        }

        dec.ReadToken();

        if (missing != 0)
        {
            var missingProps = new List<string>();
            if ((missing & missingId) != 0)
            {
                missingProps.Add("id");
            }

            if ((missing & missingMethod) != 0)
            {
                missingProps.Add("method");
            }

            throw ErrMissing(missingProps);
        }

        if (rawRegisterOptions is not null && rawRegisterOptions.Length > 0)
        {
            var o = new RegisterOptions();
            RegisterOptions = o;
            var m = new Method(method);
            if (m == Method.TextDocumentImplementation) { o.TextDocumentImplementation = ReadOptions<ImplementationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentTypeDefinition) { o.TextDocumentTypeDefinition = ReadOptions<TypeDefinitionRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDocumentColor) { o.TextDocumentDocumentColor = ReadOptions<DocumentColorRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentColorPresentation) { o.TextDocumentColorPresentation = ReadOptions<DocumentColorRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentFoldingRange) { o.TextDocumentFoldingRange = ReadOptions<FoldingRangeRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDeclaration) { o.TextDocumentDeclaration = ReadOptions<DeclarationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentSelectionRange) { o.TextDocumentSelectionRange = ReadOptions<SelectionRangeRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentPrepareCallHierarchy) { o.TextDocumentPrepareCallHierarchy = ReadOptions<CallHierarchyRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentSemanticTokens) { o.TextDocumentSemanticTokens = ReadOptions<SemanticTokensRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentLinkedEditingRange) { o.TextDocumentLinkedEditingRange = ReadOptions<LinkedEditingRangeRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceWillCreateFiles) { o.WorkspaceWillCreateFiles = ReadOptions<FileOperationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceWillRenameFiles) { o.WorkspaceWillRenameFiles = ReadOptions<FileOperationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceWillDeleteFiles) { o.WorkspaceWillDeleteFiles = ReadOptions<FileOperationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentMoniker) { o.TextDocumentMoniker = ReadOptions<MonikerRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentPrepareTypeHierarchy) { o.TextDocumentPrepareTypeHierarchy = ReadOptions<TypeHierarchyRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentInlineValue) { o.TextDocumentInlineValue = ReadOptions<InlineValueRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentInlayHint) { o.TextDocumentInlayHint = ReadOptions<InlayHintRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDiagnostic) { o.TextDocumentDiagnostic = ReadOptions<DiagnosticRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentInlineCompletion) { o.TextDocumentInlineCompletion = ReadOptions<InlineCompletionRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceTextDocumentContent) { o.WorkspaceTextDocumentContent = ReadOptions<TextDocumentContentRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentWillSaveWaitUntil) { o.TextDocumentWillSaveWaitUntil = ReadOptions<TextDocumentRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentCompletion) { o.TextDocumentCompletion = ReadOptions<CompletionRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentHover) { o.TextDocumentHover = ReadOptions<HoverRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentSignatureHelp) { o.TextDocumentSignatureHelp = ReadOptions<SignatureHelpRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDefinition) { o.TextDocumentDefinition = ReadOptions<DefinitionRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentReferences) { o.TextDocumentReferences = ReadOptions<ReferenceRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDocumentHighlight) { o.TextDocumentDocumentHighlight = ReadOptions<DocumentHighlightRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDocumentSymbol) { o.TextDocumentDocumentSymbol = ReadOptions<DocumentSymbolRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentCodeAction) { o.TextDocumentCodeAction = ReadOptions<CodeActionRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceSymbol) { o.WorkspaceSymbol = ReadOptions<WorkspaceSymbolRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentCodeLens) { o.TextDocumentCodeLens = ReadOptions<CodeLensRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDocumentLink) { o.TextDocumentDocumentLink = ReadOptions<DocumentLinkRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentFormatting) { o.TextDocumentFormatting = ReadOptions<DocumentFormattingRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentRangeFormatting) { o.TextDocumentRangeFormatting = ReadOptions<DocumentRangeFormattingRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentRangesFormatting) { o.TextDocumentRangesFormatting = ReadOptions<DocumentRangeFormattingRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentOnTypeFormatting) { o.TextDocumentOnTypeFormatting = ReadOptions<DocumentOnTypeFormattingRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentRename) { o.TextDocumentRename = ReadOptions<RenameRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceExecuteCommand) { o.WorkspaceExecuteCommand = ReadOptions<ExecuteCommandRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceDidCreateFiles) { o.WorkspaceDidCreateFiles = ReadOptions<FileOperationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceDidRenameFiles) { o.WorkspaceDidRenameFiles = ReadOptions<FileOperationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceDidDeleteFiles) { o.WorkspaceDidDeleteFiles = ReadOptions<FileOperationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceDidChangeConfiguration) { o.WorkspaceDidChangeConfiguration = ReadOptions<DidChangeConfigurationRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDidOpen) { o.TextDocumentDidOpen = ReadOptions<TextDocumentRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDidChange) { o.TextDocumentDidChange = ReadOptions<TextDocumentChangeRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDidClose) { o.TextDocumentDidClose = ReadOptions<TextDocumentRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentDidSave) { o.TextDocumentDidSave = ReadOptions<TextDocumentSaveRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.TextDocumentWillSave) { o.TextDocumentWillSave = ReadOptions<TextDocumentRegistrationOptions>(rawRegisterOptions); }
            else if (m == Method.WorkspaceDidChangeWatchedFiles) { o.WorkspaceDidChangeWatchedFiles = ReadOptions<DidChangeWatchedFilesRegistrationOptions>(rawRegisterOptions); }
            else
            {
                throw new JsonException($"unknown registration method: {method}");
            }
        }
        else
        {
            throw new JsonException($"missing registerOptions for method: {method}");
        }
    }

    /// <summary>Go 的 <c>json.Unmarshal(rawRegisterOptions, &amp;v)</c>：解进具体臂类型的新实例（解不进则抛 JsonException）。</summary>
    private static T ReadOptions<T>(byte[] raw)
        where T : IJsonUnmarshaler, new()
    {
        var v = new T();
        v.UnmarshalJSONFrom(new JsonDecoder(raw));
        return v;
    }

    /// <summary>Go: <c>func countNonNil(v any) int</c>（structcodec.go:316）：RegisterOptions 是外部标记联合、
    /// 没有 json tag，所以 Go 用 reflect 数非 nil 字段；这里按声明顺序逐个写死（与 marshal 的 if/else 链同序）。</summary>
    private static int CountNonNil(RegisterOptions o) =>
        (o.TextDocumentImplementation is not null ? 1 : 0)
        + (o.TextDocumentTypeDefinition is not null ? 1 : 0)
        + (o.TextDocumentDocumentColor is not null ? 1 : 0)
        + (o.TextDocumentColorPresentation is not null ? 1 : 0)
        + (o.TextDocumentFoldingRange is not null ? 1 : 0)
        + (o.TextDocumentDeclaration is not null ? 1 : 0)
        + (o.TextDocumentSelectionRange is not null ? 1 : 0)
        + (o.TextDocumentPrepareCallHierarchy is not null ? 1 : 0)
        + (o.TextDocumentSemanticTokens is not null ? 1 : 0)
        + (o.TextDocumentLinkedEditingRange is not null ? 1 : 0)
        + (o.WorkspaceWillCreateFiles is not null ? 1 : 0)
        + (o.WorkspaceWillRenameFiles is not null ? 1 : 0)
        + (o.WorkspaceWillDeleteFiles is not null ? 1 : 0)
        + (o.TextDocumentMoniker is not null ? 1 : 0)
        + (o.TextDocumentPrepareTypeHierarchy is not null ? 1 : 0)
        + (o.TextDocumentInlineValue is not null ? 1 : 0)
        + (o.TextDocumentInlayHint is not null ? 1 : 0)
        + (o.TextDocumentDiagnostic is not null ? 1 : 0)
        + (o.TextDocumentInlineCompletion is not null ? 1 : 0)
        + (o.WorkspaceTextDocumentContent is not null ? 1 : 0)
        + (o.TextDocumentWillSaveWaitUntil is not null ? 1 : 0)
        + (o.TextDocumentCompletion is not null ? 1 : 0)
        + (o.TextDocumentHover is not null ? 1 : 0)
        + (o.TextDocumentSignatureHelp is not null ? 1 : 0)
        + (o.TextDocumentDefinition is not null ? 1 : 0)
        + (o.TextDocumentReferences is not null ? 1 : 0)
        + (o.TextDocumentDocumentHighlight is not null ? 1 : 0)
        + (o.TextDocumentDocumentSymbol is not null ? 1 : 0)
        + (o.TextDocumentCodeAction is not null ? 1 : 0)
        + (o.WorkspaceSymbol is not null ? 1 : 0)
        + (o.TextDocumentCodeLens is not null ? 1 : 0)
        + (o.TextDocumentDocumentLink is not null ? 1 : 0)
        + (o.TextDocumentFormatting is not null ? 1 : 0)
        + (o.TextDocumentRangeFormatting is not null ? 1 : 0)
        + (o.TextDocumentRangesFormatting is not null ? 1 : 0)
        + (o.TextDocumentOnTypeFormatting is not null ? 1 : 0)
        + (o.TextDocumentRename is not null ? 1 : 0)
        + (o.WorkspaceExecuteCommand is not null ? 1 : 0)
        + (o.WorkspaceDidCreateFiles is not null ? 1 : 0)
        + (o.WorkspaceDidRenameFiles is not null ? 1 : 0)
        + (o.WorkspaceDidDeleteFiles is not null ? 1 : 0)
        + (o.WorkspaceDidChangeConfiguration is not null ? 1 : 0)
        + (o.TextDocumentDidOpen is not null ? 1 : 0)
        + (o.TextDocumentDidChange is not null ? 1 : 0)
        + (o.TextDocumentDidClose is not null ? 1 : 0)
        + (o.TextDocumentDidSave is not null ? 1 : 0)
        + (o.TextDocumentWillSave is not null ? 1 : 0)
        + (o.WorkspaceDidChangeWatchedFiles is not null ? 1 : 0);
}
