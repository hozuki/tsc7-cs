// generated from: internal/lsp/lsproto/lsp_generated.go（Union types：行 11752-14803）
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

/// <summary>Go: <c>type IntegerOrString struct</c>（lsp_generated.go:11754）。
/// </summary>
public sealed class IntegerOrString : IJsonMarshaler, IJsonUnmarshaler
{
    public int? Integer;

    public string? String;

    /// <summary>Go: <c>(o *IntegerOrString) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11761）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Integer is not null ? 1 : 0) + (String is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of IntegerOrString should be set", count);
        if (Integer is not null)
        {
            enc.WriteToken(JsonToken.Number((Integer).Value.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
    }

    /// <summary>Go: <c>(o *IntegerOrString) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11767）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Integer = null;
        String = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Number:
                Integer = dec.Decode<int?>();
                return;
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            default:
                throw ErrInvalidKind("IntegerOrString", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type DocumentSelectorOrNull struct</c>（lsp_generated.go:11782）。
/// </summary>
public sealed class DocumentSelectorOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public TextDocumentFilterLanguageOrSchemeOrPattern[]? DocumentSelector;

    /// <summary>Go: <c>(o *DocumentSelectorOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11788）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (DocumentSelector is not null ? 1 : 0);
        AssertAtMostOne("more than one element of DocumentSelectorOrNull is set", count);
        if (DocumentSelector is not null)
        {
            MarshalEncode(enc, DocumentSelector);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *DocumentSelectorOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11794）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        DocumentSelector = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                DocumentSelector = DecodeArray<TextDocumentFilterLanguageOrSchemeOrPattern>(dec, static d => d.DecodeNew<TextDocumentFilterLanguageOrSchemeOrPattern>());
                return;
            default:
                throw ErrInvalidKind("DocumentSelectorOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrEmptyObject struct</c>（lsp_generated.go:11809）。
/// </summary>
public sealed class BooleanOrEmptyObject : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public EmptyObject? EmptyObject;

    /// <summary>Go: <c>(o *BooleanOrEmptyObject) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11816）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (EmptyObject is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrEmptyObject should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (EmptyObject is not null)
        {
            MarshalEncode(enc, EmptyObject);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrEmptyObject) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11822）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        EmptyObject = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                EmptyObject = DecodeNullable<EmptyObject>(dec, static d => d.DecodeNew<EmptyObject>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrEmptyObject", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrSemanticTokensFullDelta struct</c>（lsp_generated.go:11838）。
/// </summary>
public sealed class BooleanOrSemanticTokensFullDelta : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public SemanticTokensFullDelta? SemanticTokensFullDelta;

    /// <summary>Go: <c>(o *BooleanOrSemanticTokensFullDelta) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11845）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (SemanticTokensFullDelta is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrSemanticTokensFullDelta should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (SemanticTokensFullDelta is not null)
        {
            MarshalEncode(enc, SemanticTokensFullDelta);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrSemanticTokensFullDelta) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11851）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        SemanticTokensFullDelta = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                SemanticTokensFullDelta = DecodeNullable<SemanticTokensFullDelta>(dec, static d => d.DecodeNew<SemanticTokensFullDelta>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrSemanticTokensFullDelta", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile struct</c>（lsp_generated.go:11867）。
/// </summary>
public sealed class TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile : IJsonMarshaler, IJsonUnmarshaler
{
    public TextDocumentEdit? TextDocumentEdit;

    public CreateFile? CreateFile;

    public RenameFile? RenameFile;

    public DeleteFile? DeleteFile;

    /// <summary>Go: <c>(o *TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11876）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (TextDocumentEdit is not null ? 1 : 0) + (CreateFile is not null ? 1 : 0) + (RenameFile is not null ? 1 : 0) + (DeleteFile is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile should be set", count);
        if (TextDocumentEdit is not null)
        {
            MarshalEncode(enc, TextDocumentEdit);
            return;
        }
        if (CreateFile is not null)
        {
            MarshalEncode(enc, CreateFile);
            return;
        }
        if (RenameFile is not null)
        {
            MarshalEncode(enc, RenameFile);
            return;
        }
        if (DeleteFile is not null)
        {
            MarshalEncode(enc, DeleteFile);
            return;
        }
    }

    /// <summary>Go: <c>(o *TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11882）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        TextDocumentEdit = null;
        CreateFile = null;
        RenameFile = null;
        DeleteFile = null;
        var state = ScanDiscriminatedStruct(dec, "TextDocumentEditOrCreateFileOrRenameFileOrDeleteFile", "kind");
        switch (state.DiscriminatorText)
        {
            case "\"rename\"":
                RenameFile = UnmarshalDiscriminatedArm<RenameFile>(state, RenameFile.Spec);
                return;
            case "\"create\"":
                CreateFile = UnmarshalDiscriminatedArm<CreateFile>(state, CreateFile.Spec);
                return;
            case "\"delete\"":
                DeleteFile = UnmarshalDiscriminatedArm<DeleteFile>(state, DeleteFile.Spec);
                return;
            default:
                TextDocumentEdit = UnmarshalDiscriminatedArm<TextDocumentEdit>(state, TextDocumentEdit.Spec);
                return;
        }
    }

}

/// <summary>Go: <c>type StringOrInlayHintLabelParts struct</c>（lsp_generated.go:11901）。
/// </summary>
public sealed class StringOrInlayHintLabelParts : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    public InlayHintLabelPart?[]? InlayHintLabelParts;

    /// <summary>Go: <c>(o *StringOrInlayHintLabelParts) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11908）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0) + (InlayHintLabelParts is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of StringOrInlayHintLabelParts should be set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (InlayHintLabelParts is not null)
        {
            MarshalEncode(enc, InlayHintLabelParts);
            return;
        }
    }

    /// <summary>Go: <c>(o *StringOrInlayHintLabelParts) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11914）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        InlayHintLabelParts = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.BeginArray:
                InlayHintLabelParts = DecodeArray<InlayHintLabelPart?>(dec, static d => DecodeNullable<InlayHintLabelPart>(d, static d => d.DecodeNew<InlayHintLabelPart>()));
                return;
            default:
                throw ErrInvalidKind("StringOrInlayHintLabelParts", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type StringOrMarkupContent struct</c>（lsp_generated.go:11929）。
/// </summary>
public sealed class StringOrMarkupContent : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    public MarkupContent? MarkupContent;

    /// <summary>Go: <c>(o *StringOrMarkupContent) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11936）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0) + (MarkupContent is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of StringOrMarkupContent should be set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (MarkupContent is not null)
        {
            MarshalEncode(enc, MarkupContent);
            return;
        }
    }

    /// <summary>Go: <c>(o *StringOrMarkupContent) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11942）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        MarkupContent = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.BeginObject:
                MarkupContent = DecodeNullable<MarkupContent>(dec, static d => d.DecodeNew<MarkupContent>());
                return;
            default:
                throw ErrInvalidKind("StringOrMarkupContent", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type WorkspaceFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport struct</c>（lsp_generated.go:11957）。
/// </summary>
public sealed class WorkspaceFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport : IJsonMarshaler, IJsonUnmarshaler
{
    public WorkspaceFullDocumentDiagnosticReport? FullDocumentDiagnosticReport;

    public WorkspaceUnchangedDocumentDiagnosticReport? UnchangedDocumentDiagnosticReport;

    /// <summary>Go: <c>(o *WorkspaceFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11964）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (FullDocumentDiagnosticReport is not null ? 1 : 0) + (UnchangedDocumentDiagnosticReport is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of WorkspaceFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport should be set", count);
        if (FullDocumentDiagnosticReport is not null)
        {
            MarshalEncode(enc, FullDocumentDiagnosticReport);
            return;
        }
        if (UnchangedDocumentDiagnosticReport is not null)
        {
            MarshalEncode(enc, UnchangedDocumentDiagnosticReport);
            return;
        }
    }

    /// <summary>Go: <c>(o *WorkspaceFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:11970）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        FullDocumentDiagnosticReport = null;
        UnchangedDocumentDiagnosticReport = null;
        var state = ScanDiscriminatedStruct(dec, "WorkspaceFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport", "kind");
        switch (state.DiscriminatorText)
        {
            case "\"full\"":
                FullDocumentDiagnosticReport = UnmarshalDiscriminatedArm<WorkspaceFullDocumentDiagnosticReport>(state, WorkspaceFullDocumentDiagnosticReport.Spec);
                return;
            case "\"unchanged\"":
                UnchangedDocumentDiagnosticReport = UnmarshalDiscriminatedArm<WorkspaceUnchangedDocumentDiagnosticReport>(state, WorkspaceUnchangedDocumentDiagnosticReport.Spec);
                return;
            default:
                throw state.InvalidDiscriminator();
        }
    }

}

/// <summary>Go: <c>type StringOrStringValue struct</c>（lsp_generated.go:11987）。
/// </summary>
public sealed class StringOrStringValue : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    public StringValue? StringValue;

    /// <summary>Go: <c>(o *StringOrStringValue) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:11994）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0) + (StringValue is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of StringOrStringValue should be set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (StringValue is not null)
        {
            MarshalEncode(enc, StringValue);
            return;
        }
    }

    /// <summary>Go: <c>(o *StringOrStringValue) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12000）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        StringValue = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.BeginObject:
                StringValue = DecodeNullable<StringValue>(dec, static d => d.DecodeNew<StringValue>());
                return;
            default:
                throw ErrInvalidKind("StringOrStringValue", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type IntegerOrNull struct</c>（lsp_generated.go:12015）。
/// </summary>
public sealed class IntegerOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public int? Integer;

    /// <summary>Go: <c>(o *IntegerOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12021）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Integer is not null ? 1 : 0);
        AssertAtMostOne("more than one element of IntegerOrNull is set", count);
        if (Integer is not null)
        {
            enc.WriteToken(JsonToken.Number((Integer).Value.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *IntegerOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12027）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Integer = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.Number:
                Integer = dec.Decode<int?>();
                return;
            default:
                throw ErrInvalidKind("IntegerOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type StringOrNull struct</c>（lsp_generated.go:12042）。
/// </summary>
public sealed class StringOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    /// <summary>Go: <c>(o *StringOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12048）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0);
        AssertAtMostOne("more than one element of StringOrNull is set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *StringOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12054）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            default:
                throw ErrInvalidKind("StringOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type DocumentUriOrNull struct</c>（lsp_generated.go:12069）。
/// </summary>
public sealed class DocumentUriOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public DocumentUri? DocumentUri;

    /// <summary>Go: <c>(o *DocumentUriOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12075）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (DocumentUri is not null ? 1 : 0);
        AssertAtMostOne("more than one element of DocumentUriOrNull is set", count);
        if (DocumentUri is not null)
        {
            enc.WriteToken(JsonToken.String((DocumentUri).Value.Value));
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *DocumentUriOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12081）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        DocumentUri = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.String:
                DocumentUri = DecodeNullable<DocumentUri>(dec, static d => new DocumentUri(d.Decode<string>() ?? ""));
                return;
            default:
                throw ErrInvalidKind("DocumentUriOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type InitializationOptionsOrNull struct</c>（lsp_generated.go:12096）。
/// </summary>
public sealed class InitializationOptionsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public InitializationOptions? InitializationOptions;

    /// <summary>Go: <c>(o *InitializationOptionsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12102）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (InitializationOptions is not null ? 1 : 0);
        AssertAtMostOne("more than one element of InitializationOptionsOrNull is set", count);
        if (InitializationOptions is not null)
        {
            MarshalEncode(enc, InitializationOptions);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *InitializationOptionsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12108）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        InitializationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                InitializationOptions = DecodeNullable<InitializationOptions>(dec, static d => d.DecodeNew<InitializationOptions>());
                return;
            default:
                throw ErrInvalidKind("InitializationOptionsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type WorkspaceFoldersOrNull struct</c>（lsp_generated.go:12123）。
/// </summary>
public sealed class WorkspaceFoldersOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public WorkspaceFolder?[]? WorkspaceFolders;

    /// <summary>Go: <c>(o *WorkspaceFoldersOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12129）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (WorkspaceFolders is not null ? 1 : 0);
        AssertAtMostOne("more than one element of WorkspaceFoldersOrNull is set", count);
        if (WorkspaceFolders is not null)
        {
            MarshalEncode(enc, WorkspaceFolders);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *WorkspaceFoldersOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12135）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        WorkspaceFolders = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                WorkspaceFolders = DecodeArray<WorkspaceFolder?>(dec, static d => DecodeNullable<WorkspaceFolder>(d, static d => d.DecodeNew<WorkspaceFolder>()));
                return;
            default:
                throw ErrInvalidKind("WorkspaceFoldersOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type StringOrStrings struct</c>（lsp_generated.go:12150）。
/// </summary>
public sealed class StringOrStrings : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    public string[]? Strings;

    /// <summary>Go: <c>(o *StringOrStrings) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12157）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0) + (Strings is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of StringOrStrings should be set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (Strings is not null)
        {
            MarshalEncode(enc, Strings);
            return;
        }
    }

    /// <summary>Go: <c>(o *StringOrStrings) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12163）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        Strings = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.BeginArray:
                Strings = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "");
                return;
            default:
                throw ErrInvalidKind("StringOrStrings", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type TextDocumentContentChangePartialOrWholeDocument struct</c>（lsp_generated.go:12178）。
/// </summary>
public sealed class TextDocumentContentChangePartialOrWholeDocument : IJsonMarshaler, IJsonUnmarshaler
{
    public TextDocumentContentChangePartial? Partial;

    public TextDocumentContentChangeWholeDocument? WholeDocument;

    /// <summary>Go: <c>(o *TextDocumentContentChangePartialOrWholeDocument) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12185）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Partial is not null ? 1 : 0) + (WholeDocument is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of TextDocumentContentChangePartialOrWholeDocument should be set", count);
        if (Partial is not null)
        {
            MarshalEncode(enc, Partial);
            return;
        }
        if (WholeDocument is not null)
        {
            MarshalEncode(enc, WholeDocument);
            return;
        }
    }

    /// <summary>Go: <c>(o *TextDocumentContentChangePartialOrWholeDocument) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12191）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Partial = null;
        WholeDocument = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "range"))
        {
            case 0:
                Partial = UnmarshalNew<TextDocumentContentChangePartial>(data);
                return;
            default:
                WholeDocument = UnmarshalNew<TextDocumentContentChangeWholeDocument>(data);
                return;
        }
    }

}

/// <summary>Go: <c>type TextEditOrInsertReplaceEdit struct</c>（lsp_generated.go:12208）。
/// </summary>
public sealed class TextEditOrInsertReplaceEdit : IJsonMarshaler, IJsonUnmarshaler
{
    public TextEdit? TextEdit;

    public InsertReplaceEdit? InsertReplaceEdit;

    /// <summary>Go: <c>(o *TextEditOrInsertReplaceEdit) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12215）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (TextEdit is not null ? 1 : 0) + (InsertReplaceEdit is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of TextEditOrInsertReplaceEdit should be set", count);
        if (TextEdit is not null)
        {
            MarshalEncode(enc, TextEdit);
            return;
        }
        if (InsertReplaceEdit is not null)
        {
            MarshalEncode(enc, InsertReplaceEdit);
            return;
        }
    }

    /// <summary>Go: <c>(o *TextEditOrInsertReplaceEdit) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12221）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        TextEdit = null;
        InsertReplaceEdit = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "insert", "range"))
        {
            case 0:
                InsertReplaceEdit = UnmarshalNew<InsertReplaceEdit>(data);
                return;
            case 1:
                TextEdit = UnmarshalNew<TextEdit>(data);
                return;
        }
        throw ErrInvalidValue("TextEditOrInsertReplaceEdit", data);
    }

}

/// <summary>Go: <c>type MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings struct</c>（lsp_generated.go:12239）。
/// </summary>
public sealed class MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings : IJsonMarshaler, IJsonUnmarshaler
{
    public MarkupContent? MarkupContent;

    public string? String;

    public MarkedStringWithLanguage? MarkedStringWithLanguage;

    public StringOrMarkedStringWithLanguage[]? MarkedStrings;

    /// <summary>Go: <c>(o *MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12248）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (MarkupContent is not null ? 1 : 0) + (String is not null ? 1 : 0) + (MarkedStringWithLanguage is not null ? 1 : 0) + (MarkedStrings is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings should be set", count);
        if (MarkupContent is not null)
        {
            MarshalEncode(enc, MarkupContent);
            return;
        }
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (MarkedStringWithLanguage is not null)
        {
            MarshalEncode(enc, MarkedStringWithLanguage);
            return;
        }
        if (MarkedStrings is not null)
        {
            MarshalEncode(enc, MarkedStrings);
            return;
        }
    }

    /// <summary>Go: <c>(o *MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12254）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        MarkupContent = null;
        String = null;
        MarkedStringWithLanguage = null;
        MarkedStrings = null;
        switch (dec.PeekKind())
        {
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "kind", "language"))
                {
                    case 0:
                        MarkupContent = UnmarshalNew<MarkupContent>(data);
                        return;
                    case 1:
                        MarkedStringWithLanguage = UnmarshalNew<MarkedStringWithLanguage>(data);
                        return;
                }
                throw ErrInvalidValue("MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings", data);
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.BeginArray:
                MarkedStrings = DecodeArray<StringOrMarkedStringWithLanguage>(dec, static d => d.DecodeNew<StringOrMarkedStringWithLanguage>());
                return;
            default:
                throw ErrInvalidKind("MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type UintegerOrNull struct</c>（lsp_generated.go:12283）。
/// </summary>
public sealed class UintegerOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public uint? Uinteger;

    /// <summary>Go: <c>(o *UintegerOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12289）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Uinteger is not null ? 1 : 0);
        AssertAtMostOne("more than one element of UintegerOrNull is set", count);
        if (Uinteger is not null)
        {
            enc.WriteToken(JsonToken.Number((Uinteger).Value.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *UintegerOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12295）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Uinteger = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.Number:
                Uinteger = dec.Decode<uint?>();
                return;
            default:
                throw ErrInvalidKind("UintegerOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type LocationOrLocationUriOnly struct</c>（lsp_generated.go:12310）。
/// </summary>
public sealed class LocationOrLocationUriOnly : IJsonMarshaler, IJsonUnmarshaler
{
    public Location? Location;

    public LocationUriOnly? LocationUriOnly;

    /// <summary>Go: <c>(o *LocationOrLocationUriOnly) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12317）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Location is not null ? 1 : 0) + (LocationUriOnly is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of LocationOrLocationUriOnly should be set", count);
        if (Location is not null)
        {
            MarshalEncode(enc, Location);
            return;
        }
        if (LocationUriOnly is not null)
        {
            MarshalEncode(enc, LocationUriOnly);
            return;
        }
    }

    /// <summary>Go: <c>(o *LocationOrLocationUriOnly) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12323）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Location = null;
        LocationUriOnly = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "range"))
        {
            case 0:
                Location = UnmarshalNew<Location>(data);
                return;
            default:
                LocationUriOnly = UnmarshalNew<LocationUriOnly>(data);
                return;
        }
    }

}

/// <summary>Go: <c>type WorkDoneProgressBeginOrReportOrEnd struct</c>（lsp_generated.go:12340）。
/// </summary>
public sealed class WorkDoneProgressBeginOrReportOrEnd : IJsonMarshaler, IJsonUnmarshaler
{
    public WorkDoneProgressBegin? Begin;

    public WorkDoneProgressReport? Report;

    public WorkDoneProgressEnd? End;

    /// <summary>Go: <c>(o *WorkDoneProgressBeginOrReportOrEnd) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12348）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Begin is not null ? 1 : 0) + (Report is not null ? 1 : 0) + (End is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of WorkDoneProgressBeginOrReportOrEnd should be set", count);
        if (Begin is not null)
        {
            MarshalEncode(enc, Begin);
            return;
        }
        if (Report is not null)
        {
            MarshalEncode(enc, Report);
            return;
        }
        if (End is not null)
        {
            MarshalEncode(enc, End);
            return;
        }
    }

    /// <summary>Go: <c>(o *WorkDoneProgressBeginOrReportOrEnd) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12354）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Begin = null;
        Report = null;
        End = null;
        var state = ScanDiscriminatedStruct(dec, "WorkDoneProgressBeginOrReportOrEnd", "kind");
        switch (state.DiscriminatorText)
        {
            case "\"begin\"":
                Begin = UnmarshalDiscriminatedArm<WorkDoneProgressBegin>(state, WorkDoneProgressBegin.Spec);
                return;
            case "\"report\"":
                Report = UnmarshalDiscriminatedArm<WorkDoneProgressReport>(state, WorkDoneProgressReport.Spec);
                return;
            case "\"end\"":
                End = UnmarshalDiscriminatedArm<WorkDoneProgressEnd>(state, WorkDoneProgressEnd.Spec);
                return;
            default:
                throw state.InvalidDiscriminator();
        }
    }

}

/// <summary>Go: <c>type TextEditOrAnnotatedTextEditOrSnippetTextEdit struct</c>（lsp_generated.go:12373）。
/// </summary>
public sealed class TextEditOrAnnotatedTextEditOrSnippetTextEdit : IJsonMarshaler, IJsonUnmarshaler
{
    public TextEdit? TextEdit;

    public AnnotatedTextEdit? AnnotatedTextEdit;

    public SnippetTextEdit? SnippetTextEdit;

    /// <summary>Go: <c>(o *TextEditOrAnnotatedTextEditOrSnippetTextEdit) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12381）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (TextEdit is not null ? 1 : 0) + (AnnotatedTextEdit is not null ? 1 : 0) + (SnippetTextEdit is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of TextEditOrAnnotatedTextEditOrSnippetTextEdit should be set", count);
        if (TextEdit is not null)
        {
            MarshalEncode(enc, TextEdit);
            return;
        }
        if (AnnotatedTextEdit is not null)
        {
            MarshalEncode(enc, AnnotatedTextEdit);
            return;
        }
        if (SnippetTextEdit is not null)
        {
            MarshalEncode(enc, SnippetTextEdit);
            return;
        }
    }

    /// <summary>Go: <c>(o *TextEditOrAnnotatedTextEditOrSnippetTextEdit) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12387）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        TextEdit = null;
        AnnotatedTextEdit = null;
        SnippetTextEdit = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "snippet", "annotationId"))
        {
            case 0:
                SnippetTextEdit = UnmarshalNew<SnippetTextEdit>(data);
                return;
            case 1:
                AnnotatedTextEdit = UnmarshalNew<AnnotatedTextEdit>(data);
                return;
            default:
                TextEdit = UnmarshalNew<TextEdit>(data);
                return;
        }
    }

}

/// <summary>Go: <c>type FullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport struct</c>（lsp_generated.go:12407）。
/// </summary>
public sealed class FullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport : IJsonMarshaler, IJsonUnmarshaler
{
    public FullDocumentDiagnosticReport? FullDocumentDiagnosticReport;

    public UnchangedDocumentDiagnosticReport? UnchangedDocumentDiagnosticReport;

    /// <summary>Go: <c>(o *FullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12414）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (FullDocumentDiagnosticReport is not null ? 1 : 0) + (UnchangedDocumentDiagnosticReport is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of FullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport should be set", count);
        if (FullDocumentDiagnosticReport is not null)
        {
            MarshalEncode(enc, FullDocumentDiagnosticReport);
            return;
        }
        if (UnchangedDocumentDiagnosticReport is not null)
        {
            MarshalEncode(enc, UnchangedDocumentDiagnosticReport);
            return;
        }
    }

    /// <summary>Go: <c>(o *FullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12420）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        FullDocumentDiagnosticReport = null;
        UnchangedDocumentDiagnosticReport = null;
        var state = ScanDiscriminatedStruct(dec, "FullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport", "kind");
        switch (state.DiscriminatorText)
        {
            case "\"full\"":
                FullDocumentDiagnosticReport = UnmarshalDiscriminatedArm<FullDocumentDiagnosticReport>(state, FullDocumentDiagnosticReport.Spec);
                return;
            case "\"unchanged\"":
                UnchangedDocumentDiagnosticReport = UnmarshalDiscriminatedArm<UnchangedDocumentDiagnosticReport>(state, UnchangedDocumentDiagnosticReport.Spec);
                return;
            default:
                throw state.InvalidDiscriminator();
        }
    }

}

/// <summary>Go: <c>type TextDocumentSyncOptionsOrKind struct</c>（lsp_generated.go:12437）。
/// </summary>
public sealed class TextDocumentSyncOptionsOrKind : IJsonMarshaler, IJsonUnmarshaler
{
    public TextDocumentSyncOptions? Options;

    public TextDocumentSyncKind? Kind;

    /// <summary>Go: <c>(o *TextDocumentSyncOptionsOrKind) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12444）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Options is not null ? 1 : 0) + (Kind is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of TextDocumentSyncOptionsOrKind should be set", count);
        if (Options is not null)
        {
            MarshalEncode(enc, Options);
            return;
        }
        if (Kind is not null)
        {
            enc.WriteToken(JsonToken.Number(((uint)(Kind).Value).ToString(CultureInfo.InvariantCulture)));
            return;
        }
    }

    /// <summary>Go: <c>(o *TextDocumentSyncOptionsOrKind) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12450）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Options = null;
        Kind = null;
        switch (dec.PeekKind())
        {
            case JsonKind.BeginObject:
                Options = DecodeNullable<TextDocumentSyncOptions>(dec, static d => d.DecodeNew<TextDocumentSyncOptions>());
                return;
            case JsonKind.Number:
                Kind = dec.Decode<TextDocumentSyncKind?>();
                return;
            default:
                throw ErrInvalidKind("TextDocumentSyncOptionsOrKind", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrHoverOptions struct</c>（lsp_generated.go:12465）。
/// </summary>
public sealed class BooleanOrHoverOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public HoverOptions? HoverOptions;

    /// <summary>Go: <c>(o *BooleanOrHoverOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12472）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (HoverOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrHoverOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (HoverOptions is not null)
        {
            MarshalEncode(enc, HoverOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrHoverOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12478）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        HoverOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                HoverOptions = DecodeNullable<HoverOptions>(dec, static d => d.DecodeNew<HoverOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrHoverOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrDeclarationOptionsOrDeclarationRegistrationOptions struct</c>（lsp_generated.go:12494）。
/// </summary>
public sealed class BooleanOrDeclarationOptionsOrDeclarationRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public DeclarationOptions? DeclarationOptions;

    public DeclarationRegistrationOptions? DeclarationRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrDeclarationOptionsOrDeclarationRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12502）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (DeclarationOptions is not null ? 1 : 0) + (DeclarationRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrDeclarationOptionsOrDeclarationRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (DeclarationOptions is not null)
        {
            MarshalEncode(enc, DeclarationOptions);
            return;
        }
        if (DeclarationRegistrationOptions is not null)
        {
            MarshalEncode(enc, DeclarationRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrDeclarationOptionsOrDeclarationRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12508）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        DeclarationOptions = null;
        DeclarationRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        DeclarationRegistrationOptions = UnmarshalNew<DeclarationRegistrationOptions>(data);
                        return;
                    default:
                        DeclarationOptions = UnmarshalNew<DeclarationOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrDeclarationOptionsOrDeclarationRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrDefinitionOptions struct</c>（lsp_generated.go:12534）。
/// </summary>
public sealed class BooleanOrDefinitionOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public DefinitionOptions? DefinitionOptions;

    /// <summary>Go: <c>(o *BooleanOrDefinitionOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12541）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (DefinitionOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrDefinitionOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (DefinitionOptions is not null)
        {
            MarshalEncode(enc, DefinitionOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrDefinitionOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12547）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        DefinitionOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                DefinitionOptions = DecodeNullable<DefinitionOptions>(dec, static d => d.DecodeNew<DefinitionOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrDefinitionOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrTypeDefinitionOptionsOrTypeDefinitionRegistrationOptions struct</c>（lsp_generated.go:12563）。
/// </summary>
public sealed class BooleanOrTypeDefinitionOptionsOrTypeDefinitionRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public TypeDefinitionOptions? TypeDefinitionOptions;

    public TypeDefinitionRegistrationOptions? TypeDefinitionRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrTypeDefinitionOptionsOrTypeDefinitionRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12571）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (TypeDefinitionOptions is not null ? 1 : 0) + (TypeDefinitionRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrTypeDefinitionOptionsOrTypeDefinitionRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (TypeDefinitionOptions is not null)
        {
            MarshalEncode(enc, TypeDefinitionOptions);
            return;
        }
        if (TypeDefinitionRegistrationOptions is not null)
        {
            MarshalEncode(enc, TypeDefinitionRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrTypeDefinitionOptionsOrTypeDefinitionRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12577）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        TypeDefinitionOptions = null;
        TypeDefinitionRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        TypeDefinitionRegistrationOptions = UnmarshalNew<TypeDefinitionRegistrationOptions>(data);
                        return;
                    default:
                        TypeDefinitionOptions = UnmarshalNew<TypeDefinitionOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrTypeDefinitionOptionsOrTypeDefinitionRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrImplementationOptionsOrImplementationRegistrationOptions struct</c>（lsp_generated.go:12603）。
/// </summary>
public sealed class BooleanOrImplementationOptionsOrImplementationRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public ImplementationOptions? ImplementationOptions;

    public ImplementationRegistrationOptions? ImplementationRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrImplementationOptionsOrImplementationRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12611）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (ImplementationOptions is not null ? 1 : 0) + (ImplementationRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrImplementationOptionsOrImplementationRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (ImplementationOptions is not null)
        {
            MarshalEncode(enc, ImplementationOptions);
            return;
        }
        if (ImplementationRegistrationOptions is not null)
        {
            MarshalEncode(enc, ImplementationRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrImplementationOptionsOrImplementationRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12617）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        ImplementationOptions = null;
        ImplementationRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        ImplementationRegistrationOptions = UnmarshalNew<ImplementationRegistrationOptions>(data);
                        return;
                    default:
                        ImplementationOptions = UnmarshalNew<ImplementationOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrImplementationOptionsOrImplementationRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrReferenceOptions struct</c>（lsp_generated.go:12643）。
/// </summary>
public sealed class BooleanOrReferenceOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public ReferenceOptions? ReferenceOptions;

    /// <summary>Go: <c>(o *BooleanOrReferenceOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12650）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (ReferenceOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrReferenceOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (ReferenceOptions is not null)
        {
            MarshalEncode(enc, ReferenceOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrReferenceOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12656）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        ReferenceOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                ReferenceOptions = DecodeNullable<ReferenceOptions>(dec, static d => d.DecodeNew<ReferenceOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrReferenceOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrDocumentHighlightOptions struct</c>（lsp_generated.go:12672）。
/// </summary>
public sealed class BooleanOrDocumentHighlightOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public DocumentHighlightOptions? DocumentHighlightOptions;

    /// <summary>Go: <c>(o *BooleanOrDocumentHighlightOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12679）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (DocumentHighlightOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrDocumentHighlightOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (DocumentHighlightOptions is not null)
        {
            MarshalEncode(enc, DocumentHighlightOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrDocumentHighlightOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12685）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        DocumentHighlightOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                DocumentHighlightOptions = DecodeNullable<DocumentHighlightOptions>(dec, static d => d.DecodeNew<DocumentHighlightOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrDocumentHighlightOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrDocumentSymbolOptions struct</c>（lsp_generated.go:12701）。
/// </summary>
public sealed class BooleanOrDocumentSymbolOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public DocumentSymbolOptions? DocumentSymbolOptions;

    /// <summary>Go: <c>(o *BooleanOrDocumentSymbolOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12708）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (DocumentSymbolOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrDocumentSymbolOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (DocumentSymbolOptions is not null)
        {
            MarshalEncode(enc, DocumentSymbolOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrDocumentSymbolOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12714）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        DocumentSymbolOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                DocumentSymbolOptions = DecodeNullable<DocumentSymbolOptions>(dec, static d => d.DecodeNew<DocumentSymbolOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrDocumentSymbolOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrCodeActionOptions struct</c>（lsp_generated.go:12730）。
/// </summary>
public sealed class BooleanOrCodeActionOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public CodeActionOptions? CodeActionOptions;

    /// <summary>Go: <c>(o *BooleanOrCodeActionOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12737）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (CodeActionOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrCodeActionOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (CodeActionOptions is not null)
        {
            MarshalEncode(enc, CodeActionOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrCodeActionOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12743）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        CodeActionOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                CodeActionOptions = DecodeNullable<CodeActionOptions>(dec, static d => d.DecodeNew<CodeActionOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrCodeActionOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrDocumentColorOptionsOrDocumentColorRegistrationOptions struct</c>（lsp_generated.go:12759）。
/// </summary>
public sealed class BooleanOrDocumentColorOptionsOrDocumentColorRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public DocumentColorOptions? DocumentColorOptions;

    public DocumentColorRegistrationOptions? DocumentColorRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrDocumentColorOptionsOrDocumentColorRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12767）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (DocumentColorOptions is not null ? 1 : 0) + (DocumentColorRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrDocumentColorOptionsOrDocumentColorRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (DocumentColorOptions is not null)
        {
            MarshalEncode(enc, DocumentColorOptions);
            return;
        }
        if (DocumentColorRegistrationOptions is not null)
        {
            MarshalEncode(enc, DocumentColorRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrDocumentColorOptionsOrDocumentColorRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12773）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        DocumentColorOptions = null;
        DocumentColorRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        DocumentColorRegistrationOptions = UnmarshalNew<DocumentColorRegistrationOptions>(data);
                        return;
                    default:
                        DocumentColorOptions = UnmarshalNew<DocumentColorOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrDocumentColorOptionsOrDocumentColorRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrWorkspaceSymbolOptions struct</c>（lsp_generated.go:12799）。
/// </summary>
public sealed class BooleanOrWorkspaceSymbolOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public WorkspaceSymbolOptions? WorkspaceSymbolOptions;

    /// <summary>Go: <c>(o *BooleanOrWorkspaceSymbolOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12806）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (WorkspaceSymbolOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrWorkspaceSymbolOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (WorkspaceSymbolOptions is not null)
        {
            MarshalEncode(enc, WorkspaceSymbolOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrWorkspaceSymbolOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12812）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        WorkspaceSymbolOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                WorkspaceSymbolOptions = DecodeNullable<WorkspaceSymbolOptions>(dec, static d => d.DecodeNew<WorkspaceSymbolOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrWorkspaceSymbolOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrDocumentFormattingOptions struct</c>（lsp_generated.go:12828）。
/// </summary>
public sealed class BooleanOrDocumentFormattingOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public DocumentFormattingOptions? DocumentFormattingOptions;

    /// <summary>Go: <c>(o *BooleanOrDocumentFormattingOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12835）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (DocumentFormattingOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrDocumentFormattingOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (DocumentFormattingOptions is not null)
        {
            MarshalEncode(enc, DocumentFormattingOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrDocumentFormattingOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12841）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        DocumentFormattingOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                DocumentFormattingOptions = DecodeNullable<DocumentFormattingOptions>(dec, static d => d.DecodeNew<DocumentFormattingOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrDocumentFormattingOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrDocumentRangeFormattingOptions struct</c>（lsp_generated.go:12857）。
/// </summary>
public sealed class BooleanOrDocumentRangeFormattingOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public DocumentRangeFormattingOptions? DocumentRangeFormattingOptions;

    /// <summary>Go: <c>(o *BooleanOrDocumentRangeFormattingOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12864）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (DocumentRangeFormattingOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrDocumentRangeFormattingOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (DocumentRangeFormattingOptions is not null)
        {
            MarshalEncode(enc, DocumentRangeFormattingOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrDocumentRangeFormattingOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12870）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        DocumentRangeFormattingOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                DocumentRangeFormattingOptions = DecodeNullable<DocumentRangeFormattingOptions>(dec, static d => d.DecodeNew<DocumentRangeFormattingOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrDocumentRangeFormattingOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrRenameOptions struct</c>（lsp_generated.go:12886）。
/// </summary>
public sealed class BooleanOrRenameOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public RenameOptions? RenameOptions;

    /// <summary>Go: <c>(o *BooleanOrRenameOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12893）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (RenameOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrRenameOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (RenameOptions is not null)
        {
            MarshalEncode(enc, RenameOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrRenameOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12899）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        RenameOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                RenameOptions = DecodeNullable<RenameOptions>(dec, static d => d.DecodeNew<RenameOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrRenameOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrFoldingRangeOptionsOrFoldingRangeRegistrationOptions struct</c>（lsp_generated.go:12915）。
/// </summary>
public sealed class BooleanOrFoldingRangeOptionsOrFoldingRangeRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public FoldingRangeOptions? FoldingRangeOptions;

    public FoldingRangeRegistrationOptions? FoldingRangeRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrFoldingRangeOptionsOrFoldingRangeRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12923）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (FoldingRangeOptions is not null ? 1 : 0) + (FoldingRangeRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrFoldingRangeOptionsOrFoldingRangeRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (FoldingRangeOptions is not null)
        {
            MarshalEncode(enc, FoldingRangeOptions);
            return;
        }
        if (FoldingRangeRegistrationOptions is not null)
        {
            MarshalEncode(enc, FoldingRangeRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrFoldingRangeOptionsOrFoldingRangeRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12929）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        FoldingRangeOptions = null;
        FoldingRangeRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        FoldingRangeRegistrationOptions = UnmarshalNew<FoldingRangeRegistrationOptions>(data);
                        return;
                    default:
                        FoldingRangeOptions = UnmarshalNew<FoldingRangeOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrFoldingRangeOptionsOrFoldingRangeRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrSelectionRangeOptionsOrSelectionRangeRegistrationOptions struct</c>（lsp_generated.go:12955）。
/// </summary>
public sealed class BooleanOrSelectionRangeOptionsOrSelectionRangeRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public SelectionRangeOptions? SelectionRangeOptions;

    public SelectionRangeRegistrationOptions? SelectionRangeRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrSelectionRangeOptionsOrSelectionRangeRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:12963）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (SelectionRangeOptions is not null ? 1 : 0) + (SelectionRangeRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrSelectionRangeOptionsOrSelectionRangeRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (SelectionRangeOptions is not null)
        {
            MarshalEncode(enc, SelectionRangeOptions);
            return;
        }
        if (SelectionRangeRegistrationOptions is not null)
        {
            MarshalEncode(enc, SelectionRangeRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrSelectionRangeOptionsOrSelectionRangeRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:12969）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        SelectionRangeOptions = null;
        SelectionRangeRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        SelectionRangeRegistrationOptions = UnmarshalNew<SelectionRangeRegistrationOptions>(data);
                        return;
                    default:
                        SelectionRangeOptions = UnmarshalNew<SelectionRangeOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrSelectionRangeOptionsOrSelectionRangeRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrCallHierarchyOptionsOrCallHierarchyRegistrationOptions struct</c>（lsp_generated.go:12995）。
/// </summary>
public sealed class BooleanOrCallHierarchyOptionsOrCallHierarchyRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public CallHierarchyOptions? CallHierarchyOptions;

    public CallHierarchyRegistrationOptions? CallHierarchyRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrCallHierarchyOptionsOrCallHierarchyRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13003）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (CallHierarchyOptions is not null ? 1 : 0) + (CallHierarchyRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrCallHierarchyOptionsOrCallHierarchyRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (CallHierarchyOptions is not null)
        {
            MarshalEncode(enc, CallHierarchyOptions);
            return;
        }
        if (CallHierarchyRegistrationOptions is not null)
        {
            MarshalEncode(enc, CallHierarchyRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrCallHierarchyOptionsOrCallHierarchyRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13009）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        CallHierarchyOptions = null;
        CallHierarchyRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        CallHierarchyRegistrationOptions = UnmarshalNew<CallHierarchyRegistrationOptions>(data);
                        return;
                    default:
                        CallHierarchyOptions = UnmarshalNew<CallHierarchyOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrCallHierarchyOptionsOrCallHierarchyRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrLinkedEditingRangeOptionsOrLinkedEditingRangeRegistrationOptions struct</c>（lsp_generated.go:13035）。
/// </summary>
public sealed class BooleanOrLinkedEditingRangeOptionsOrLinkedEditingRangeRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public LinkedEditingRangeOptions? LinkedEditingRangeOptions;

    public LinkedEditingRangeRegistrationOptions? LinkedEditingRangeRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrLinkedEditingRangeOptionsOrLinkedEditingRangeRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13043）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (LinkedEditingRangeOptions is not null ? 1 : 0) + (LinkedEditingRangeRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrLinkedEditingRangeOptionsOrLinkedEditingRangeRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (LinkedEditingRangeOptions is not null)
        {
            MarshalEncode(enc, LinkedEditingRangeOptions);
            return;
        }
        if (LinkedEditingRangeRegistrationOptions is not null)
        {
            MarshalEncode(enc, LinkedEditingRangeRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrLinkedEditingRangeOptionsOrLinkedEditingRangeRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13049）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        LinkedEditingRangeOptions = null;
        LinkedEditingRangeRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        LinkedEditingRangeRegistrationOptions = UnmarshalNew<LinkedEditingRangeRegistrationOptions>(data);
                        return;
                    default:
                        LinkedEditingRangeOptions = UnmarshalNew<LinkedEditingRangeOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrLinkedEditingRangeOptionsOrLinkedEditingRangeRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type SemanticTokensOptionsOrRegistrationOptions struct</c>（lsp_generated.go:13075）。
/// </summary>
public sealed class SemanticTokensOptionsOrRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public SemanticTokensOptions? Options;

    public SemanticTokensRegistrationOptions? RegistrationOptions;

    /// <summary>Go: <c>(o *SemanticTokensOptionsOrRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13082）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Options is not null ? 1 : 0) + (RegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of SemanticTokensOptionsOrRegistrationOptions should be set", count);
        if (Options is not null)
        {
            MarshalEncode(enc, Options);
            return;
        }
        if (RegistrationOptions is not null)
        {
            MarshalEncode(enc, RegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *SemanticTokensOptionsOrRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13088）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Options = null;
        RegistrationOptions = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "documentSelector"))
        {
            case 0:
                RegistrationOptions = UnmarshalNew<SemanticTokensRegistrationOptions>(data);
                return;
            default:
                Options = UnmarshalNew<SemanticTokensOptions>(data);
                return;
        }
    }

}

/// <summary>Go: <c>type BooleanOrMonikerOptionsOrMonikerRegistrationOptions struct</c>（lsp_generated.go:13105）。
/// </summary>
public sealed class BooleanOrMonikerOptionsOrMonikerRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public MonikerOptions? MonikerOptions;

    public MonikerRegistrationOptions? MonikerRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrMonikerOptionsOrMonikerRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13113）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (MonikerOptions is not null ? 1 : 0) + (MonikerRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrMonikerOptionsOrMonikerRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (MonikerOptions is not null)
        {
            MarshalEncode(enc, MonikerOptions);
            return;
        }
        if (MonikerRegistrationOptions is not null)
        {
            MarshalEncode(enc, MonikerRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrMonikerOptionsOrMonikerRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13119）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        MonikerOptions = null;
        MonikerRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        MonikerRegistrationOptions = UnmarshalNew<MonikerRegistrationOptions>(data);
                        return;
                    default:
                        MonikerOptions = UnmarshalNew<MonikerOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrMonikerOptionsOrMonikerRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrTypeHierarchyOptionsOrTypeHierarchyRegistrationOptions struct</c>（lsp_generated.go:13145）。
/// </summary>
public sealed class BooleanOrTypeHierarchyOptionsOrTypeHierarchyRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public TypeHierarchyOptions? TypeHierarchyOptions;

    public TypeHierarchyRegistrationOptions? TypeHierarchyRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrTypeHierarchyOptionsOrTypeHierarchyRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13153）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (TypeHierarchyOptions is not null ? 1 : 0) + (TypeHierarchyRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrTypeHierarchyOptionsOrTypeHierarchyRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (TypeHierarchyOptions is not null)
        {
            MarshalEncode(enc, TypeHierarchyOptions);
            return;
        }
        if (TypeHierarchyRegistrationOptions is not null)
        {
            MarshalEncode(enc, TypeHierarchyRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrTypeHierarchyOptionsOrTypeHierarchyRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13159）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        TypeHierarchyOptions = null;
        TypeHierarchyRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        TypeHierarchyRegistrationOptions = UnmarshalNew<TypeHierarchyRegistrationOptions>(data);
                        return;
                    default:
                        TypeHierarchyOptions = UnmarshalNew<TypeHierarchyOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrTypeHierarchyOptionsOrTypeHierarchyRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrInlineValueOptionsOrInlineValueRegistrationOptions struct</c>（lsp_generated.go:13185）。
/// </summary>
public sealed class BooleanOrInlineValueOptionsOrInlineValueRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public InlineValueOptions? InlineValueOptions;

    public InlineValueRegistrationOptions? InlineValueRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrInlineValueOptionsOrInlineValueRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13193）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (InlineValueOptions is not null ? 1 : 0) + (InlineValueRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrInlineValueOptionsOrInlineValueRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (InlineValueOptions is not null)
        {
            MarshalEncode(enc, InlineValueOptions);
            return;
        }
        if (InlineValueRegistrationOptions is not null)
        {
            MarshalEncode(enc, InlineValueRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrInlineValueOptionsOrInlineValueRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13199）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        InlineValueOptions = null;
        InlineValueRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        InlineValueRegistrationOptions = UnmarshalNew<InlineValueRegistrationOptions>(data);
                        return;
                    default:
                        InlineValueOptions = UnmarshalNew<InlineValueOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrInlineValueOptionsOrInlineValueRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrInlayHintOptionsOrInlayHintRegistrationOptions struct</c>（lsp_generated.go:13225）。
/// </summary>
public sealed class BooleanOrInlayHintOptionsOrInlayHintRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public InlayHintOptions? InlayHintOptions;

    public InlayHintRegistrationOptions? InlayHintRegistrationOptions;

    /// <summary>Go: <c>(o *BooleanOrInlayHintOptionsOrInlayHintRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13233）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (InlayHintOptions is not null ? 1 : 0) + (InlayHintRegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrInlayHintOptionsOrInlayHintRegistrationOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (InlayHintOptions is not null)
        {
            MarshalEncode(enc, InlayHintOptions);
            return;
        }
        if (InlayHintRegistrationOptions is not null)
        {
            MarshalEncode(enc, InlayHintRegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrInlayHintOptionsOrInlayHintRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13239）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        InlayHintOptions = null;
        InlayHintRegistrationOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "documentSelector"))
                {
                    case 0:
                        InlayHintRegistrationOptions = UnmarshalNew<InlayHintRegistrationOptions>(data);
                        return;
                    default:
                        InlayHintOptions = UnmarshalNew<InlayHintOptions>(data);
                        return;
                }
            default:
                throw ErrInvalidKind("BooleanOrInlayHintOptionsOrInlayHintRegistrationOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type DiagnosticOptionsOrRegistrationOptions struct</c>（lsp_generated.go:13265）。
/// </summary>
public sealed class DiagnosticOptionsOrRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public DiagnosticOptions? Options;

    public DiagnosticRegistrationOptions? RegistrationOptions;

    /// <summary>Go: <c>(o *DiagnosticOptionsOrRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13272）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Options is not null ? 1 : 0) + (RegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of DiagnosticOptionsOrRegistrationOptions should be set", count);
        if (Options is not null)
        {
            MarshalEncode(enc, Options);
            return;
        }
        if (RegistrationOptions is not null)
        {
            MarshalEncode(enc, RegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *DiagnosticOptionsOrRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13278）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Options = null;
        RegistrationOptions = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "documentSelector"))
        {
            case 0:
                RegistrationOptions = UnmarshalNew<DiagnosticRegistrationOptions>(data);
                return;
            default:
                Options = UnmarshalNew<DiagnosticOptions>(data);
                return;
        }
    }

}

/// <summary>Go: <c>type BooleanOrInlineCompletionOptions struct</c>（lsp_generated.go:13295）。
/// </summary>
public sealed class BooleanOrInlineCompletionOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public InlineCompletionOptions? InlineCompletionOptions;

    /// <summary>Go: <c>(o *BooleanOrInlineCompletionOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13302）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (InlineCompletionOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrInlineCompletionOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (InlineCompletionOptions is not null)
        {
            MarshalEncode(enc, InlineCompletionOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrInlineCompletionOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13308）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        InlineCompletionOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                InlineCompletionOptions = DecodeNullable<InlineCompletionOptions>(dec, static d => d.DecodeNew<InlineCompletionOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrInlineCompletionOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type PatternOrRelativePattern struct</c>（lsp_generated.go:13324）。
/// </summary>
public sealed class PatternOrRelativePattern : IJsonMarshaler, IJsonUnmarshaler
{
    public string? Pattern;

    public RelativePattern? RelativePattern;

    /// <summary>Go: <c>(o *PatternOrRelativePattern) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13331）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Pattern is not null ? 1 : 0) + (RelativePattern is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of PatternOrRelativePattern should be set", count);
        if (Pattern is not null)
        {
            MarshalEncode(enc, Pattern);
            return;
        }
        if (RelativePattern is not null)
        {
            MarshalEncode(enc, RelativePattern);
            return;
        }
    }

    /// <summary>Go: <c>(o *PatternOrRelativePattern) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13337）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Pattern = null;
        RelativePattern = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                Pattern = dec.Decode<string>();
                return;
            case JsonKind.BeginObject:
                RelativePattern = DecodeNullable<RelativePattern>(dec, static d => d.DecodeNew<RelativePattern>());
                return;
            default:
                throw ErrInvalidKind("PatternOrRelativePattern", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type RangeOrEditRangeWithInsertReplace struct</c>（lsp_generated.go:13352）。
/// </summary>
public sealed class RangeOrEditRangeWithInsertReplace : IJsonMarshaler, IJsonUnmarshaler
{
    public Range? Range;

    public EditRangeWithInsertReplace? EditRangeWithInsertReplace;

    /// <summary>Go: <c>(o *RangeOrEditRangeWithInsertReplace) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13359）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Range is not null ? 1 : 0) + (EditRangeWithInsertReplace is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of RangeOrEditRangeWithInsertReplace should be set", count);
        if (Range is not null)
        {
            MarshalEncode(enc, Range);
            return;
        }
        if (EditRangeWithInsertReplace is not null)
        {
            MarshalEncode(enc, EditRangeWithInsertReplace);
            return;
        }
    }

    /// <summary>Go: <c>(o *RangeOrEditRangeWithInsertReplace) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13365）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Range = null;
        EditRangeWithInsertReplace = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "start", "insert"))
        {
            case 0:
                Range = UnmarshalNew<Range>(data);
                return;
            case 1:
                EditRangeWithInsertReplace = UnmarshalNew<EditRangeWithInsertReplace>(data);
                return;
        }
        throw ErrInvalidValue("RangeOrEditRangeWithInsertReplace", data);
    }

}

/// <summary>Go: <c>type BooleanOrSaveOptions struct</c>（lsp_generated.go:13383）。
/// </summary>
public sealed class BooleanOrSaveOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public SaveOptions? SaveOptions;

    /// <summary>Go: <c>(o *BooleanOrSaveOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13390）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (SaveOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrSaveOptions should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (SaveOptions is not null)
        {
            MarshalEncode(enc, SaveOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrSaveOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13396）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        SaveOptions = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                SaveOptions = DecodeNullable<SaveOptions>(dec, static d => d.DecodeNew<SaveOptions>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrSaveOptions", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type TextDocumentContentOptionsOrRegistrationOptions struct</c>（lsp_generated.go:13412）。
/// </summary>
public sealed class TextDocumentContentOptionsOrRegistrationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public TextDocumentContentOptions? Options;

    public TextDocumentContentRegistrationOptions? RegistrationOptions;

    /// <summary>Go: <c>(o *TextDocumentContentOptionsOrRegistrationOptions) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13419）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Options is not null ? 1 : 0) + (RegistrationOptions is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of TextDocumentContentOptionsOrRegistrationOptions should be set", count);
        if (Options is not null)
        {
            MarshalEncode(enc, Options);
            return;
        }
        if (RegistrationOptions is not null)
        {
            MarshalEncode(enc, RegistrationOptions);
            return;
        }
    }

    /// <summary>Go: <c>(o *TextDocumentContentOptionsOrRegistrationOptions) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13425）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Options = null;
        RegistrationOptions = null;
        var data = dec.ReadValue();
        var vOptions = new TextDocumentContentOptions();
        if (TryUnmarshalInto(data, vOptions))
        {
            Options = vOptions;
            return;
        }
        var vRegistrationOptions = new TextDocumentContentRegistrationOptions();
        if (TryUnmarshalInto(data, vRegistrationOptions))
        {
            RegistrationOptions = vRegistrationOptions;
            return;
        }
        throw ErrInvalidValue("TextDocumentContentOptionsOrRegistrationOptions", data);
    }

}

/// <summary>Go: <c>type StringOrTuple struct</c>（lsp_generated.go:13445）。
/// </summary>
public sealed class StringOrTuple : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    public uint[]? Tuple;

    /// <summary>Go: <c>(o *StringOrTuple) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13452）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0) + (Tuple is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of StringOrTuple should be set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (Tuple is not null)
        {
            MarshalEncode(enc, Tuple);
            return;
        }
    }

    /// <summary>Go: <c>(o *StringOrTuple) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13458）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        Tuple = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.BeginArray:
                Tuple = DecodeFixedArray<uint>(dec, 2, static d => d.Decode<uint>());
                return;
            default:
                throw ErrInvalidKind("StringOrTuple", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type StringOrBoolean struct</c>（lsp_generated.go:13473）。
/// </summary>
public sealed class StringOrBoolean : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    public bool? Boolean;

    /// <summary>Go: <c>(o *StringOrBoolean) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13480）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0) + (Boolean is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of StringOrBoolean should be set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
    }

    /// <summary>Go: <c>(o *StringOrBoolean) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13486）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        Boolean = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            default:
                throw ErrInvalidKind("StringOrBoolean", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type WorkspaceFolderOrURI struct</c>（lsp_generated.go:13502）。
/// </summary>
public sealed class WorkspaceFolderOrURI : IJsonMarshaler, IJsonUnmarshaler
{
    public WorkspaceFolder? WorkspaceFolder;

    public URI? URI;

    /// <summary>Go: <c>(o *WorkspaceFolderOrURI) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13509）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (WorkspaceFolder is not null ? 1 : 0) + (URI is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of WorkspaceFolderOrURI should be set", count);
        if (WorkspaceFolder is not null)
        {
            MarshalEncode(enc, WorkspaceFolder);
            return;
        }
        if (URI is not null)
        {
            enc.WriteToken(JsonToken.String((URI).Value.Value));
            return;
        }
    }

    /// <summary>Go: <c>(o *WorkspaceFolderOrURI) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13515）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        WorkspaceFolder = null;
        URI = null;
        switch (dec.PeekKind())
        {
            case JsonKind.BeginObject:
                WorkspaceFolder = DecodeNullable<WorkspaceFolder>(dec, static d => d.DecodeNew<WorkspaceFolder>());
                return;
            case JsonKind.String:
                URI = DecodeNullable<URI>(dec, static d => new URI(d.Decode<string>() ?? ""));
                return;
            default:
                throw ErrInvalidKind("WorkspaceFolderOrURI", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type BooleanOrClientSemanticTokensRequestFullDelta struct</c>（lsp_generated.go:13530）。
/// </summary>
public sealed class BooleanOrClientSemanticTokensRequestFullDelta : IJsonMarshaler, IJsonUnmarshaler
{
    public bool? Boolean;

    public ClientSemanticTokensRequestFullDelta? ClientSemanticTokensRequestFullDelta;

    /// <summary>Go: <c>(o *BooleanOrClientSemanticTokensRequestFullDelta) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13537）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Boolean is not null ? 1 : 0) + (ClientSemanticTokensRequestFullDelta is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of BooleanOrClientSemanticTokensRequestFullDelta should be set", count);
        if (Boolean is not null)
        {
            enc.WriteToken(JsonToken.Bool((Boolean).Value));
            return;
        }
        if (ClientSemanticTokensRequestFullDelta is not null)
        {
            MarshalEncode(enc, ClientSemanticTokensRequestFullDelta);
            return;
        }
    }

    /// <summary>Go: <c>(o *BooleanOrClientSemanticTokensRequestFullDelta) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13543）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Boolean = null;
        ClientSemanticTokensRequestFullDelta = null;
        switch (dec.PeekKind())
        {
            case JsonKind.True or JsonKind.False:
                Boolean = dec.PeekKind() == JsonKind.True;
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                ClientSemanticTokensRequestFullDelta = DecodeNullable<ClientSemanticTokensRequestFullDelta>(dec, static d => d.DecodeNew<ClientSemanticTokensRequestFullDelta>());
                return;
            default:
                throw ErrInvalidKind("BooleanOrClientSemanticTokensRequestFullDelta", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type VSImageElementOrClassifiedTextElementOrContainerElement struct</c>（lsp_generated.go:13559）。
/// </summary>
public sealed class VSImageElementOrClassifiedTextElementOrContainerElement : IJsonMarshaler, IJsonUnmarshaler
{
    public VSImageElement? ImageElement;

    public VSClassifiedTextElement? ClassifiedTextElement;

    public VSContainerElement? ContainerElement;

    /// <summary>Go: <c>(o *VSImageElementOrClassifiedTextElementOrContainerElement) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13567）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (ImageElement is not null ? 1 : 0) + (ClassifiedTextElement is not null ? 1 : 0) + (ContainerElement is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of VSImageElementOrClassifiedTextElementOrContainerElement should be set", count);
        if (ImageElement is not null)
        {
            MarshalEncode(enc, ImageElement);
            return;
        }
        if (ClassifiedTextElement is not null)
        {
            MarshalEncode(enc, ClassifiedTextElement);
            return;
        }
        if (ContainerElement is not null)
        {
            MarshalEncode(enc, ContainerElement);
            return;
        }
    }

    /// <summary>Go: <c>(o *VSImageElementOrClassifiedTextElementOrContainerElement) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13573）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        ImageElement = null;
        ClassifiedTextElement = null;
        ContainerElement = null;
        var state = ScanDiscriminatedStruct(dec, "VSImageElementOrClassifiedTextElementOrContainerElement", "_vs_type");
        switch (state.DiscriminatorText)
        {
            case "\"ContainerElement\"":
                ContainerElement = UnmarshalDiscriminatedArm<VSContainerElement>(state, VSContainerElement.Spec);
                return;
            case "\"ImageElement\"":
                ImageElement = UnmarshalDiscriminatedArm<VSImageElement>(state, VSImageElement.Spec);
                return;
            case "\"ClassifiedTextElement\"":
                ClassifiedTextElement = UnmarshalDiscriminatedArm<VSClassifiedTextElement>(state, VSClassifiedTextElement.Spec);
                return;
            default:
                throw state.InvalidDiscriminator();
        }
    }

}

/// <summary>Go: <c>type LocationOrLocationsOrDefinitionLinksOrNull struct</c>（lsp_generated.go:13592）。
/// </summary>
public sealed class LocationOrLocationsOrDefinitionLinksOrNull : IJsonMarshaler, IJsonUnmarshaler, HasLocations
{
    public Location? Location;

    public Location[]? Locations;

    public LocationLink?[]? DefinitionLinks;

    /// <summary>Go: <c>(o LocationOrLocationsOrDefinitionLinksOrNull) GetLocations() *[]Location</c>（lsp_generated.go:13637）。</summary>
    public Location[]? GetLocations() => Locations;

    /// <summary>Go: <c>(o *LocationOrLocationsOrDefinitionLinksOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13600）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Location is not null ? 1 : 0) + (Locations is not null ? 1 : 0) + (DefinitionLinks is not null ? 1 : 0);
        AssertAtMostOne("more than one element of LocationOrLocationsOrDefinitionLinksOrNull is set", count);
        if (Location is not null)
        {
            MarshalEncode(enc, Location);
            return;
        }
        if (Locations is not null)
        {
            MarshalEncode(enc, Locations);
            return;
        }
        if (DefinitionLinks is not null)
        {
            MarshalEncode(enc, DefinitionLinks);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *LocationOrLocationsOrDefinitionLinksOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13606）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Location = null;
        Locations = null;
        DefinitionLinks = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                Location = DecodeNullable<Location>(dec, static d => d.DecodeNew<Location>());
                return;
            case JsonKind.BeginArray:
                var data = dec.ReadValue();
                if (TryUnmarshalArray(data, static d => d.DecodeNew<Location>(), out var vLocations))
                {
                    Locations = vLocations;
                    return;
                }
                if (TryUnmarshalArray(data, static d => DecodeNullable<LocationLink>(d, static d => d.DecodeNew<LocationLink>()), out var vDefinitionLinks))
                {
                    DefinitionLinks = vDefinitionLinks;
                    return;
                }
                throw ErrInvalidValue("LocationOrLocationsOrDefinitionLinksOrNull", data);
            default:
                throw ErrInvalidKind("LocationOrLocationsOrDefinitionLinksOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type FoldingRangesOrNull struct</c>（lsp_generated.go:13641）。
/// </summary>
public sealed class FoldingRangesOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public FoldingRange?[]? FoldingRanges;

    /// <summary>Go: <c>(o *FoldingRangesOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13647）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (FoldingRanges is not null ? 1 : 0);
        AssertAtMostOne("more than one element of FoldingRangesOrNull is set", count);
        if (FoldingRanges is not null)
        {
            MarshalEncode(enc, FoldingRanges);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *FoldingRangesOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13653）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        FoldingRanges = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                FoldingRanges = DecodeArray<FoldingRange?>(dec, static d => DecodeNullable<FoldingRange>(d, static d => d.DecodeNew<FoldingRange>()));
                return;
            default:
                throw ErrInvalidKind("FoldingRangesOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type LocationOrLocationsOrDeclarationLinksOrNull struct</c>（lsp_generated.go:13668）。
/// </summary>
public sealed class LocationOrLocationsOrDeclarationLinksOrNull : IJsonMarshaler, IJsonUnmarshaler, HasLocations
{
    public Location? Location;

    public Location[]? Locations;

    public LocationLink?[]? DeclarationLinks;

    /// <summary>Go: <c>(o LocationOrLocationsOrDeclarationLinksOrNull) GetLocations() *[]Location</c>（lsp_generated.go:13713）。</summary>
    public Location[]? GetLocations() => Locations;

    /// <summary>Go: <c>(o *LocationOrLocationsOrDeclarationLinksOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13676）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Location is not null ? 1 : 0) + (Locations is not null ? 1 : 0) + (DeclarationLinks is not null ? 1 : 0);
        AssertAtMostOne("more than one element of LocationOrLocationsOrDeclarationLinksOrNull is set", count);
        if (Location is not null)
        {
            MarshalEncode(enc, Location);
            return;
        }
        if (Locations is not null)
        {
            MarshalEncode(enc, Locations);
            return;
        }
        if (DeclarationLinks is not null)
        {
            MarshalEncode(enc, DeclarationLinks);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *LocationOrLocationsOrDeclarationLinksOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13682）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Location = null;
        Locations = null;
        DeclarationLinks = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                Location = DecodeNullable<Location>(dec, static d => d.DecodeNew<Location>());
                return;
            case JsonKind.BeginArray:
                var data = dec.ReadValue();
                if (TryUnmarshalArray(data, static d => d.DecodeNew<Location>(), out var vLocations))
                {
                    Locations = vLocations;
                    return;
                }
                if (TryUnmarshalArray(data, static d => DecodeNullable<LocationLink>(d, static d => d.DecodeNew<LocationLink>()), out var vDeclarationLinks))
                {
                    DeclarationLinks = vDeclarationLinks;
                    return;
                }
                throw ErrInvalidValue("LocationOrLocationsOrDeclarationLinksOrNull", data);
            default:
                throw ErrInvalidKind("LocationOrLocationsOrDeclarationLinksOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type SelectionRangesOrNull struct</c>（lsp_generated.go:13717）。
/// </summary>
public sealed class SelectionRangesOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public SelectionRange?[]? SelectionRanges;

    /// <summary>Go: <c>(o *SelectionRangesOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13723）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (SelectionRanges is not null ? 1 : 0);
        AssertAtMostOne("more than one element of SelectionRangesOrNull is set", count);
        if (SelectionRanges is not null)
        {
            MarshalEncode(enc, SelectionRanges);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *SelectionRangesOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13729）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        SelectionRanges = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                SelectionRanges = DecodeArray<SelectionRange?>(dec, static d => DecodeNullable<SelectionRange>(d, static d => d.DecodeNew<SelectionRange>()));
                return;
            default:
                throw ErrInvalidKind("SelectionRangesOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type CallHierarchyItemsOrNull struct</c>（lsp_generated.go:13744）。
/// </summary>
public sealed class CallHierarchyItemsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public CallHierarchyItem?[]? CallHierarchyItems;

    /// <summary>Go: <c>(o *CallHierarchyItemsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13750）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (CallHierarchyItems is not null ? 1 : 0);
        AssertAtMostOne("more than one element of CallHierarchyItemsOrNull is set", count);
        if (CallHierarchyItems is not null)
        {
            MarshalEncode(enc, CallHierarchyItems);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *CallHierarchyItemsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13756）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        CallHierarchyItems = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                CallHierarchyItems = DecodeArray<CallHierarchyItem?>(dec, static d => DecodeNullable<CallHierarchyItem>(d, static d => d.DecodeNew<CallHierarchyItem>()));
                return;
            default:
                throw ErrInvalidKind("CallHierarchyItemsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type CallHierarchyIncomingCallsOrNull struct</c>（lsp_generated.go:13771）。
/// </summary>
public sealed class CallHierarchyIncomingCallsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public CallHierarchyIncomingCall?[]? CallHierarchyIncomingCalls;

    /// <summary>Go: <c>(o *CallHierarchyIncomingCallsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13777）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (CallHierarchyIncomingCalls is not null ? 1 : 0);
        AssertAtMostOne("more than one element of CallHierarchyIncomingCallsOrNull is set", count);
        if (CallHierarchyIncomingCalls is not null)
        {
            MarshalEncode(enc, CallHierarchyIncomingCalls);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *CallHierarchyIncomingCallsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13783）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        CallHierarchyIncomingCalls = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                CallHierarchyIncomingCalls = DecodeArray<CallHierarchyIncomingCall?>(dec, static d => DecodeNullable<CallHierarchyIncomingCall>(d, static d => d.DecodeNew<CallHierarchyIncomingCall>()));
                return;
            default:
                throw ErrInvalidKind("CallHierarchyIncomingCallsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type CallHierarchyOutgoingCallsOrNull struct</c>（lsp_generated.go:13798）。
/// </summary>
public sealed class CallHierarchyOutgoingCallsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public CallHierarchyOutgoingCall?[]? CallHierarchyOutgoingCalls;

    /// <summary>Go: <c>(o *CallHierarchyOutgoingCallsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13804）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (CallHierarchyOutgoingCalls is not null ? 1 : 0);
        AssertAtMostOne("more than one element of CallHierarchyOutgoingCallsOrNull is set", count);
        if (CallHierarchyOutgoingCalls is not null)
        {
            MarshalEncode(enc, CallHierarchyOutgoingCalls);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *CallHierarchyOutgoingCallsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13810）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        CallHierarchyOutgoingCalls = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                CallHierarchyOutgoingCalls = DecodeArray<CallHierarchyOutgoingCall?>(dec, static d => DecodeNullable<CallHierarchyOutgoingCall>(d, static d => d.DecodeNew<CallHierarchyOutgoingCall>()));
                return;
            default:
                throw ErrInvalidKind("CallHierarchyOutgoingCallsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type SemanticTokensOrNull struct</c>（lsp_generated.go:13825）。
/// </summary>
public sealed class SemanticTokensOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public SemanticTokens? SemanticTokens;

    /// <summary>Go: <c>(o *SemanticTokensOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13831）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (SemanticTokens is not null ? 1 : 0);
        AssertAtMostOne("more than one element of SemanticTokensOrNull is set", count);
        if (SemanticTokens is not null)
        {
            MarshalEncode(enc, SemanticTokens);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *SemanticTokensOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13837）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        SemanticTokens = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                SemanticTokens = DecodeNullable<SemanticTokens>(dec, static d => d.DecodeNew<SemanticTokens>());
                return;
            default:
                throw ErrInvalidKind("SemanticTokensOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type SemanticTokensOrSemanticTokensDeltaOrNull struct</c>（lsp_generated.go:13852）。
/// </summary>
public sealed class SemanticTokensOrSemanticTokensDeltaOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public SemanticTokens? SemanticTokens;

    public SemanticTokensDelta? SemanticTokensDelta;

    /// <summary>Go: <c>(o *SemanticTokensOrSemanticTokensDeltaOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13859）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (SemanticTokens is not null ? 1 : 0) + (SemanticTokensDelta is not null ? 1 : 0);
        AssertAtMostOne("more than one element of SemanticTokensOrSemanticTokensDeltaOrNull is set", count);
        if (SemanticTokens is not null)
        {
            MarshalEncode(enc, SemanticTokens);
            return;
        }
        if (SemanticTokensDelta is not null)
        {
            MarshalEncode(enc, SemanticTokensDelta);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *SemanticTokensOrSemanticTokensDeltaOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13865）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        SemanticTokens = null;
        SemanticTokensDelta = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "data", "edits"))
                {
                    case 0:
                        SemanticTokens = UnmarshalNew<SemanticTokens>(data);
                        return;
                    case 1:
                        SemanticTokensDelta = UnmarshalNew<SemanticTokensDelta>(data);
                        return;
                }
                throw ErrInvalidValue("SemanticTokensOrSemanticTokensDeltaOrNull", data);
            default:
                throw ErrInvalidKind("SemanticTokensOrSemanticTokensDeltaOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type LinkedEditingRangesOrNull struct</c>（lsp_generated.go:13891）。
/// </summary>
public sealed class LinkedEditingRangesOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public LinkedEditingRanges? LinkedEditingRanges;

    /// <summary>Go: <c>(o *LinkedEditingRangesOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13897）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (LinkedEditingRanges is not null ? 1 : 0);
        AssertAtMostOne("more than one element of LinkedEditingRangesOrNull is set", count);
        if (LinkedEditingRanges is not null)
        {
            MarshalEncode(enc, LinkedEditingRanges);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *LinkedEditingRangesOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13903）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        LinkedEditingRanges = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                LinkedEditingRanges = DecodeNullable<LinkedEditingRanges>(dec, static d => d.DecodeNew<LinkedEditingRanges>());
                return;
            default:
                throw ErrInvalidKind("LinkedEditingRangesOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type WorkspaceEditOrNull struct</c>（lsp_generated.go:13918）。
/// </summary>
public sealed class WorkspaceEditOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public WorkspaceEdit? WorkspaceEdit;

    /// <summary>Go: <c>(o *WorkspaceEditOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13924）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (WorkspaceEdit is not null ? 1 : 0);
        AssertAtMostOne("more than one element of WorkspaceEditOrNull is set", count);
        if (WorkspaceEdit is not null)
        {
            MarshalEncode(enc, WorkspaceEdit);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *WorkspaceEditOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13930）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        WorkspaceEdit = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                WorkspaceEdit = DecodeNullable<WorkspaceEdit>(dec, static d => d.DecodeNew<WorkspaceEdit>());
                return;
            default:
                throw ErrInvalidKind("WorkspaceEditOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type MonikersOrNull struct</c>（lsp_generated.go:13945）。
/// </summary>
public sealed class MonikersOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public Moniker?[]? Monikers;

    /// <summary>Go: <c>(o *MonikersOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13951）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Monikers is not null ? 1 : 0);
        AssertAtMostOne("more than one element of MonikersOrNull is set", count);
        if (Monikers is not null)
        {
            MarshalEncode(enc, Monikers);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *MonikersOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13957）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Monikers = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                Monikers = DecodeArray<Moniker?>(dec, static d => DecodeNullable<Moniker>(d, static d => d.DecodeNew<Moniker>()));
                return;
            default:
                throw ErrInvalidKind("MonikersOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type TypeHierarchyItemsOrNull struct</c>（lsp_generated.go:13972）。
/// </summary>
public sealed class TypeHierarchyItemsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public TypeHierarchyItem?[]? TypeHierarchyItems;

    /// <summary>Go: <c>(o *TypeHierarchyItemsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:13978）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (TypeHierarchyItems is not null ? 1 : 0);
        AssertAtMostOne("more than one element of TypeHierarchyItemsOrNull is set", count);
        if (TypeHierarchyItems is not null)
        {
            MarshalEncode(enc, TypeHierarchyItems);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *TypeHierarchyItemsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:13984）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        TypeHierarchyItems = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                TypeHierarchyItems = DecodeArray<TypeHierarchyItem?>(dec, static d => DecodeNullable<TypeHierarchyItem>(d, static d => d.DecodeNew<TypeHierarchyItem>()));
                return;
            default:
                throw ErrInvalidKind("TypeHierarchyItemsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type InlineValuesOrNull struct</c>（lsp_generated.go:13999）。
/// </summary>
public sealed class InlineValuesOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public InlineValueTextOrVariableLookupOrEvaluatableExpression[]? InlineValues;

    /// <summary>Go: <c>(o *InlineValuesOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14005）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (InlineValues is not null ? 1 : 0);
        AssertAtMostOne("more than one element of InlineValuesOrNull is set", count);
        if (InlineValues is not null)
        {
            MarshalEncode(enc, InlineValues);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *InlineValuesOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14011）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        InlineValues = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                InlineValues = DecodeArray<InlineValueTextOrVariableLookupOrEvaluatableExpression>(dec, static d => d.DecodeNew<InlineValueTextOrVariableLookupOrEvaluatableExpression>());
                return;
            default:
                throw ErrInvalidKind("InlineValuesOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type InlayHintsOrNull struct</c>（lsp_generated.go:14026）。
/// </summary>
public sealed class InlayHintsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public InlayHint?[]? InlayHints;

    /// <summary>Go: <c>(o *InlayHintsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14032）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (InlayHints is not null ? 1 : 0);
        AssertAtMostOne("more than one element of InlayHintsOrNull is set", count);
        if (InlayHints is not null)
        {
            MarshalEncode(enc, InlayHints);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *InlayHintsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14038）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        InlayHints = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                InlayHints = DecodeArray<InlayHint?>(dec, static d => DecodeNullable<InlayHint>(d, static d => d.DecodeNew<InlayHint>()));
                return;
            default:
                throw ErrInvalidKind("InlayHintsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport struct</c>（lsp_generated.go:14053）。
/// </summary>
public sealed class RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport : IJsonMarshaler, IJsonUnmarshaler
{
    public RelatedFullDocumentDiagnosticReport? FullDocumentDiagnosticReport;

    public RelatedUnchangedDocumentDiagnosticReport? UnchangedDocumentDiagnosticReport;

    /// <summary>Go: <c>(o *RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14060）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (FullDocumentDiagnosticReport is not null ? 1 : 0) + (UnchangedDocumentDiagnosticReport is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport should be set", count);
        if (FullDocumentDiagnosticReport is not null)
        {
            MarshalEncode(enc, FullDocumentDiagnosticReport);
            return;
        }
        if (UnchangedDocumentDiagnosticReport is not null)
        {
            MarshalEncode(enc, UnchangedDocumentDiagnosticReport);
            return;
        }
    }

    /// <summary>Go: <c>(o *RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14066）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        FullDocumentDiagnosticReport = null;
        UnchangedDocumentDiagnosticReport = null;
        var state = ScanDiscriminatedStruct(dec, "RelatedFullDocumentDiagnosticReportOrUnchangedDocumentDiagnosticReport", "kind");
        switch (state.DiscriminatorText)
        {
            case "\"full\"":
                FullDocumentDiagnosticReport = UnmarshalDiscriminatedArm<RelatedFullDocumentDiagnosticReport>(state, RelatedFullDocumentDiagnosticReport.Spec);
                return;
            case "\"unchanged\"":
                UnchangedDocumentDiagnosticReport = UnmarshalDiscriminatedArm<RelatedUnchangedDocumentDiagnosticReport>(state, RelatedUnchangedDocumentDiagnosticReport.Spec);
                return;
            default:
                throw state.InvalidDiscriminator();
        }
    }

}

/// <summary>Go: <c>type InlineCompletionListOrItemsOrNull struct</c>（lsp_generated.go:14083）。
/// </summary>
public sealed class InlineCompletionListOrItemsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public InlineCompletionList? List;

    public InlineCompletionItem?[]? Items;

    /// <summary>Go: <c>(o *InlineCompletionListOrItemsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14090）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (List is not null ? 1 : 0) + (Items is not null ? 1 : 0);
        AssertAtMostOne("more than one element of InlineCompletionListOrItemsOrNull is set", count);
        if (List is not null)
        {
            MarshalEncode(enc, List);
            return;
        }
        if (Items is not null)
        {
            MarshalEncode(enc, Items);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *InlineCompletionListOrItemsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14096）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        List = null;
        Items = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                List = DecodeNullable<InlineCompletionList>(dec, static d => d.DecodeNew<InlineCompletionList>());
                return;
            case JsonKind.BeginArray:
                Items = DecodeArray<InlineCompletionItem?>(dec, static d => DecodeNullable<InlineCompletionItem>(d, static d => d.DecodeNew<InlineCompletionItem>()));
                return;
            default:
                throw ErrInvalidKind("InlineCompletionListOrItemsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type MessageActionItemOrNull struct</c>（lsp_generated.go:14114）。
/// </summary>
public sealed class MessageActionItemOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public MessageActionItem? MessageActionItem;

    /// <summary>Go: <c>(o *MessageActionItemOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14120）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (MessageActionItem is not null ? 1 : 0);
        AssertAtMostOne("more than one element of MessageActionItemOrNull is set", count);
        if (MessageActionItem is not null)
        {
            MarshalEncode(enc, MessageActionItem);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *MessageActionItemOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14126）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        MessageActionItem = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                MessageActionItem = DecodeNullable<MessageActionItem>(dec, static d => d.DecodeNew<MessageActionItem>());
                return;
            default:
                throw ErrInvalidKind("MessageActionItemOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type TextEditsOrNull struct</c>（lsp_generated.go:14141）。
/// </summary>
public sealed class TextEditsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public TextEdit?[]? TextEdits;

    /// <summary>Go: <c>(o *TextEditsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14147）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (TextEdits is not null ? 1 : 0);
        AssertAtMostOne("more than one element of TextEditsOrNull is set", count);
        if (TextEdits is not null)
        {
            MarshalEncode(enc, TextEdits);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *TextEditsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14153）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        TextEdits = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                TextEdits = DecodeArray<TextEdit?>(dec, static d => DecodeNullable<TextEdit>(d, static d => d.DecodeNew<TextEdit>()));
                return;
            default:
                throw ErrInvalidKind("TextEditsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type CompletionItemsOrListOrNull struct</c>（lsp_generated.go:14168）。
/// </summary>
public sealed class CompletionItemsOrListOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public CompletionItem?[]? Items;

    public CompletionList? List;

    /// <summary>Go: <c>(o *CompletionItemsOrListOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14175）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Items is not null ? 1 : 0) + (List is not null ? 1 : 0);
        AssertAtMostOne("more than one element of CompletionItemsOrListOrNull is set", count);
        if (Items is not null)
        {
            MarshalEncode(enc, Items);
            return;
        }
        if (List is not null)
        {
            MarshalEncode(enc, List);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *CompletionItemsOrListOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14181）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Items = null;
        List = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                Items = DecodeArray<CompletionItem?>(dec, static d => DecodeNullable<CompletionItem>(d, static d => d.DecodeNew<CompletionItem>()));
                return;
            case JsonKind.BeginObject:
                List = DecodeNullable<CompletionList>(dec, static d => d.DecodeNew<CompletionList>());
                return;
            default:
                throw ErrInvalidKind("CompletionItemsOrListOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type HoverOrNull struct</c>（lsp_generated.go:14199）。
/// </summary>
public sealed class HoverOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public Hover? Hover;

    /// <summary>Go: <c>(o *HoverOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14205）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Hover is not null ? 1 : 0);
        AssertAtMostOne("more than one element of HoverOrNull is set", count);
        if (Hover is not null)
        {
            MarshalEncode(enc, Hover);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *HoverOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14211）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Hover = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                Hover = DecodeNullable<Hover>(dec, static d => d.DecodeNew<Hover>());
                return;
            default:
                throw ErrInvalidKind("HoverOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type SignatureHelpOrNull struct</c>（lsp_generated.go:14226）。
/// </summary>
public sealed class SignatureHelpOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public SignatureHelp? SignatureHelp;

    /// <summary>Go: <c>(o *SignatureHelpOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14232）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (SignatureHelp is not null ? 1 : 0);
        AssertAtMostOne("more than one element of SignatureHelpOrNull is set", count);
        if (SignatureHelp is not null)
        {
            MarshalEncode(enc, SignatureHelp);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *SignatureHelpOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14238）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        SignatureHelp = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                SignatureHelp = DecodeNullable<SignatureHelp>(dec, static d => d.DecodeNew<SignatureHelp>());
                return;
            default:
                throw ErrInvalidKind("SignatureHelpOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type LocationsOrNull struct</c>（lsp_generated.go:14253）。
/// </summary>
public sealed class LocationsOrNull : IJsonMarshaler, IJsonUnmarshaler, HasLocations
{
    public Location[]? Locations;

    /// <summary>Go: <c>(o LocationsOrNull) GetLocations() *[]Location</c>（lsp_generated.go:14280）。</summary>
    public Location[]? GetLocations() => Locations;

    /// <summary>Go: <c>(o *LocationsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14259）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Locations is not null ? 1 : 0);
        AssertAtMostOne("more than one element of LocationsOrNull is set", count);
        if (Locations is not null)
        {
            MarshalEncode(enc, Locations);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *LocationsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14265）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Locations = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                Locations = DecodeArray<Location>(dec, static d => d.DecodeNew<Location>());
                return;
            default:
                throw ErrInvalidKind("LocationsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type DocumentHighlightsOrNull struct</c>（lsp_generated.go:14284）。
/// </summary>
public sealed class DocumentHighlightsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public DocumentHighlight?[]? DocumentHighlights;

    /// <summary>Go: <c>(o *DocumentHighlightsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14290）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (DocumentHighlights is not null ? 1 : 0);
        AssertAtMostOne("more than one element of DocumentHighlightsOrNull is set", count);
        if (DocumentHighlights is not null)
        {
            MarshalEncode(enc, DocumentHighlights);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *DocumentHighlightsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14296）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        DocumentHighlights = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                DocumentHighlights = DecodeArray<DocumentHighlight?>(dec, static d => DecodeNullable<DocumentHighlight>(d, static d => d.DecodeNew<DocumentHighlight>()));
                return;
            default:
                throw ErrInvalidKind("DocumentHighlightsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type SymbolInformationsOrDocumentSymbolsOrNull struct</c>（lsp_generated.go:14311）。
/// </summary>
public sealed class SymbolInformationsOrDocumentSymbolsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public SymbolInformation?[]? SymbolInformations;

    public DocumentSymbol?[]? DocumentSymbols;

    /// <summary>Go: <c>(o *SymbolInformationsOrDocumentSymbolsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14318）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (SymbolInformations is not null ? 1 : 0) + (DocumentSymbols is not null ? 1 : 0);
        AssertAtMostOne("more than one element of SymbolInformationsOrDocumentSymbolsOrNull is set", count);
        if (SymbolInformations is not null)
        {
            MarshalEncode(enc, SymbolInformations);
            return;
        }
        if (DocumentSymbols is not null)
        {
            MarshalEncode(enc, DocumentSymbols);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *SymbolInformationsOrDocumentSymbolsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14324）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        SymbolInformations = null;
        DocumentSymbols = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                var data = dec.ReadValue();
                if (TryUnmarshalArray(data, static d => DecodeNullable<SymbolInformation>(d, static d => d.DecodeNew<SymbolInformation>()), out var vSymbolInformations))
                {
                    SymbolInformations = vSymbolInformations;
                    return;
                }
                if (TryUnmarshalArray(data, static d => DecodeNullable<DocumentSymbol>(d, static d => d.DecodeNew<DocumentSymbol>()), out var vDocumentSymbols))
                {
                    DocumentSymbols = vDocumentSymbols;
                    return;
                }
                throw ErrInvalidValue("SymbolInformationsOrDocumentSymbolsOrNull", data);
            default:
                throw ErrInvalidKind("SymbolInformationsOrDocumentSymbolsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type CommandOrCodeAction struct</c>（lsp_generated.go:14352）。
/// </summary>
public sealed class CommandOrCodeAction : IJsonMarshaler, IJsonUnmarshaler
{
    public Command? Command;

    public CodeAction? CodeAction;

    /// <summary>Go: <c>(o *CommandOrCodeAction) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14359）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Command is not null ? 1 : 0) + (CodeAction is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of CommandOrCodeAction should be set", count);
        if (Command is not null)
        {
            MarshalEncode(enc, Command);
            return;
        }
        if (CodeAction is not null)
        {
            MarshalEncode(enc, CodeAction);
            return;
        }
    }

    /// <summary>Go: <c>(o *CommandOrCodeAction) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14365）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Command = null;
        CodeAction = null;
        var data = dec.ReadValue();
        var vCommand = new Command();
        if (TryUnmarshalInto(data, vCommand))
        {
            Command = vCommand;
            return;
        }
        var vCodeAction = new CodeAction();
        if (TryUnmarshalInto(data, vCodeAction))
        {
            CodeAction = vCodeAction;
            return;
        }
        throw ErrInvalidValue("CommandOrCodeAction", data);
    }

}

/// <summary>Go: <c>type CommandOrCodeActionArrayOrNull struct</c>（lsp_generated.go:14385）。
/// </summary>
public sealed class CommandOrCodeActionArrayOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public CommandOrCodeAction[]? CommandOrCodeActionArray;

    /// <summary>Go: <c>(o *CommandOrCodeActionArrayOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14391）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (CommandOrCodeActionArray is not null ? 1 : 0);
        AssertAtMostOne("more than one element of CommandOrCodeActionArrayOrNull is set", count);
        if (CommandOrCodeActionArray is not null)
        {
            MarshalEncode(enc, CommandOrCodeActionArray);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *CommandOrCodeActionArrayOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14397）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        CommandOrCodeActionArray = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                CommandOrCodeActionArray = DecodeArray<CommandOrCodeAction>(dec, static d => d.DecodeNew<CommandOrCodeAction>());
                return;
            default:
                throw ErrInvalidKind("CommandOrCodeActionArrayOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type SymbolInformationsOrWorkspaceSymbolsOrNull struct</c>（lsp_generated.go:14412）。
/// </summary>
public sealed class SymbolInformationsOrWorkspaceSymbolsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public SymbolInformation?[]? SymbolInformations;

    public WorkspaceSymbol?[]? WorkspaceSymbols;

    /// <summary>Go: <c>(o *SymbolInformationsOrWorkspaceSymbolsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14419）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (SymbolInformations is not null ? 1 : 0) + (WorkspaceSymbols is not null ? 1 : 0);
        AssertAtMostOne("more than one element of SymbolInformationsOrWorkspaceSymbolsOrNull is set", count);
        if (SymbolInformations is not null)
        {
            MarshalEncode(enc, SymbolInformations);
            return;
        }
        if (WorkspaceSymbols is not null)
        {
            MarshalEncode(enc, WorkspaceSymbols);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *SymbolInformationsOrWorkspaceSymbolsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14425）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        SymbolInformations = null;
        WorkspaceSymbols = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                var data = dec.ReadValue();
                if (TryUnmarshalArray(data, static d => DecodeNullable<SymbolInformation>(d, static d => d.DecodeNew<SymbolInformation>()), out var vSymbolInformations))
                {
                    SymbolInformations = vSymbolInformations;
                    return;
                }
                if (TryUnmarshalArray(data, static d => DecodeNullable<WorkspaceSymbol>(d, static d => d.DecodeNew<WorkspaceSymbol>()), out var vWorkspaceSymbols))
                {
                    WorkspaceSymbols = vWorkspaceSymbols;
                    return;
                }
                throw ErrInvalidValue("SymbolInformationsOrWorkspaceSymbolsOrNull", data);
            default:
                throw ErrInvalidKind("SymbolInformationsOrWorkspaceSymbolsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type CodeLensesOrNull struct</c>（lsp_generated.go:14453）。
/// </summary>
public sealed class CodeLensesOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public CodeLens?[]? CodeLenses;

    /// <summary>Go: <c>(o *CodeLensesOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14459）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (CodeLenses is not null ? 1 : 0);
        AssertAtMostOne("more than one element of CodeLensesOrNull is set", count);
        if (CodeLenses is not null)
        {
            MarshalEncode(enc, CodeLenses);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *CodeLensesOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14465）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        CodeLenses = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                CodeLenses = DecodeArray<CodeLens?>(dec, static d => DecodeNullable<CodeLens>(d, static d => d.DecodeNew<CodeLens>()));
                return;
            default:
                throw ErrInvalidKind("CodeLensesOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type DocumentLinksOrNull struct</c>（lsp_generated.go:14480）。
/// </summary>
public sealed class DocumentLinksOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public DocumentLink?[]? DocumentLinks;

    /// <summary>Go: <c>(o *DocumentLinksOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14486）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (DocumentLinks is not null ? 1 : 0);
        AssertAtMostOne("more than one element of DocumentLinksOrNull is set", count);
        if (DocumentLinks is not null)
        {
            MarshalEncode(enc, DocumentLinks);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *DocumentLinksOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14492）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        DocumentLinks = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                DocumentLinks = DecodeArray<DocumentLink?>(dec, static d => DecodeNullable<DocumentLink>(d, static d => d.DecodeNew<DocumentLink>()));
                return;
            default:
                throw ErrInvalidKind("DocumentLinksOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull struct</c>（lsp_generated.go:14507）。
/// </summary>
public sealed class RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public Range? Range;

    public PrepareRenamePlaceholder? PrepareRenamePlaceholder;

    public PrepareRenameDefaultBehavior? PrepareRenameDefaultBehavior;

    /// <summary>Go: <c>(o *RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14515）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Range is not null ? 1 : 0) + (PrepareRenamePlaceholder is not null ? 1 : 0) + (PrepareRenameDefaultBehavior is not null ? 1 : 0);
        AssertAtMostOne("more than one element of RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull is set", count);
        if (Range is not null)
        {
            MarshalEncode(enc, Range);
            return;
        }
        if (PrepareRenamePlaceholder is not null)
        {
            MarshalEncode(enc, PrepareRenamePlaceholder);
            return;
        }
        if (PrepareRenameDefaultBehavior is not null)
        {
            MarshalEncode(enc, PrepareRenameDefaultBehavior);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14521）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Range = null;
        PrepareRenamePlaceholder = null;
        PrepareRenameDefaultBehavior = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var data = dec.ReadValue();
                switch (JsonObjectHasKey(data, "start", "range", "defaultBehavior"))
                {
                    case 0:
                        Range = UnmarshalNew<Range>(data);
                        return;
                    case 1:
                        PrepareRenamePlaceholder = UnmarshalNew<PrepareRenamePlaceholder>(data);
                        return;
                    case 2:
                        PrepareRenameDefaultBehavior = UnmarshalNew<PrepareRenameDefaultBehavior>(data);
                        return;
                }
                throw ErrInvalidValue("RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull", data);
            default:
                throw ErrInvalidKind("RangeOrPrepareRenamePlaceholderOrPrepareRenameDefaultBehaviorOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type LSPAnyOrNull struct</c>（lsp_generated.go:14550）。
/// </summary>
public sealed class LSPAnyOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public object? LSPAny;

    /// <summary>Go: <c>(o *LSPAnyOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14556）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (LSPAny is not null ? 1 : 0);
        AssertAtMostOne("more than one element of LSPAnyOrNull is set", count);
        if (LSPAny is not null)
        {
            MarshalEncode(enc, LSPAny);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *LSPAnyOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14562）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        LSPAny = null;
        var data = dec.ReadValue();
        if (data.AsSpan().SequenceEqual("null"u8))
        {
            return;
        }
        if (TryUnmarshalAny(data, out var vLSPAny))
        {
            LSPAny = vLSPAny;
            return;
        }
        throw ErrInvalidValue("LSPAnyOrNull", data);
    }

}

/// <summary>Go: <c>type MultiDocumentHighlightsOrNull struct</c>（lsp_generated.go:14581）。
/// </summary>
public sealed class MultiDocumentHighlightsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public MultiDocumentHighlight?[]? MultiDocumentHighlights;

    /// <summary>Go: <c>(o *MultiDocumentHighlightsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14587）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (MultiDocumentHighlights is not null ? 1 : 0);
        AssertAtMostOne("more than one element of MultiDocumentHighlightsOrNull is set", count);
        if (MultiDocumentHighlights is not null)
        {
            MarshalEncode(enc, MultiDocumentHighlights);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *MultiDocumentHighlightsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14593）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        MultiDocumentHighlights = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                MultiDocumentHighlights = DecodeArray<MultiDocumentHighlight?>(dec, static d => DecodeNullable<MultiDocumentHighlight>(d, static d => d.DecodeNew<MultiDocumentHighlight>()));
                return;
            default:
                throw ErrInvalidKind("MultiDocumentHighlightsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type VSOnAutoInsertResponseItemOrNull struct</c>（lsp_generated.go:14608）。
/// </summary>
public sealed class VSOnAutoInsertResponseItemOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public VSOnAutoInsertResponseItem? VSOnAutoInsertResponseItem;

    /// <summary>Go: <c>(o *VSOnAutoInsertResponseItemOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14614）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (VSOnAutoInsertResponseItem is not null ? 1 : 0);
        AssertAtMostOne("more than one element of VSOnAutoInsertResponseItemOrNull is set", count);
        if (VSOnAutoInsertResponseItem is not null)
        {
            MarshalEncode(enc, VSOnAutoInsertResponseItem);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *VSOnAutoInsertResponseItemOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14620）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        VSOnAutoInsertResponseItem = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                VSOnAutoInsertResponseItem = DecodeNullable<VSOnAutoInsertResponseItem>(dec, static d => d.DecodeNew<VSOnAutoInsertResponseItem>());
                return;
            default:
                throw ErrInvalidKind("VSOnAutoInsertResponseItemOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type VSReferenceItemsOrNull struct</c>（lsp_generated.go:14635）。
/// </summary>
public sealed class VSReferenceItemsOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public VSReferenceItem?[]? VSReferenceItems;

    /// <summary>Go: <c>(o *VSReferenceItemsOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14641）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (VSReferenceItems is not null ? 1 : 0);
        AssertAtMostOne("more than one element of VSReferenceItemsOrNull is set", count);
        if (VSReferenceItems is not null)
        {
            MarshalEncode(enc, VSReferenceItems);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *VSReferenceItemsOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14647）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        VSReferenceItems = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginArray:
                VSReferenceItems = DecodeArray<VSReferenceItem?>(dec, static d => DecodeNullable<VSReferenceItem>(d, static d => d.DecodeNew<VSReferenceItem>()));
                return;
            default:
                throw ErrInvalidKind("VSReferenceItemsOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull struct</c>（lsp_generated.go:14662）。
/// </summary>
public sealed class RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull : IJsonMarshaler, IJsonUnmarshaler
{
    public RequestFailureTelemetryEvent? RequestFailureTelemetryEvent;

    public PerformanceStatsTelemetryEvent? PerformanceStatsTelemetryEvent;

    public ProjectInfoTelemetryEvent? ProjectInfoTelemetryEvent;

    /// <summary>Go: <c>(o *RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14670）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (RequestFailureTelemetryEvent is not null ? 1 : 0) + (PerformanceStatsTelemetryEvent is not null ? 1 : 0) + (ProjectInfoTelemetryEvent is not null ? 1 : 0);
        AssertAtMostOne("more than one element of RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull is set", count);
        if (RequestFailureTelemetryEvent is not null)
        {
            MarshalEncode(enc, RequestFailureTelemetryEvent);
            return;
        }
        if (PerformanceStatsTelemetryEvent is not null)
        {
            MarshalEncode(enc, PerformanceStatsTelemetryEvent);
            return;
        }
        if (ProjectInfoTelemetryEvent is not null)
        {
            MarshalEncode(enc, ProjectInfoTelemetryEvent);
            return;
        }
        MarshalEncode(enc, null);
    }

    /// <summary>Go: <c>(o *RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14676）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        RequestFailureTelemetryEvent = null;
        PerformanceStatsTelemetryEvent = null;
        ProjectInfoTelemetryEvent = null;
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                return;
            case JsonKind.BeginObject:
                var state = ScanDiscriminatedStruct(dec, "RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull", "eventName");
                switch (state.DiscriminatorText)
                {
                    case "\"languageServer.projectInfo\"":
                        ProjectInfoTelemetryEvent = UnmarshalDiscriminatedArm<ProjectInfoTelemetryEvent>(state, ProjectInfoTelemetryEvent.Spec);
                        return;
                    case "\"languageServer.errorResponse\"":
                        RequestFailureTelemetryEvent = UnmarshalDiscriminatedArm<RequestFailureTelemetryEvent>(state, RequestFailureTelemetryEvent.Spec);
                        return;
                    case "\"languageServer.performanceStats\"":
                        PerformanceStatsTelemetryEvent = UnmarshalDiscriminatedArm<PerformanceStatsTelemetryEvent>(state, PerformanceStatsTelemetryEvent.Spec);
                        return;
                    default:
                        throw state.InvalidDiscriminator();
                }
            default:
                throw ErrInvalidKind("RequestFailureTelemetryEventOrPerformanceStatsTelemetryEventOrProjectInfoTelemetryEventOrNull", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type TextDocumentFilterLanguageOrSchemeOrPattern struct</c>（lsp_generated.go:14703）。
/// </summary>
public sealed class TextDocumentFilterLanguageOrSchemeOrPattern : IJsonMarshaler, IJsonUnmarshaler
{
    public TextDocumentFilterLanguage? Language;

    public TextDocumentFilterScheme? Scheme;

    public TextDocumentFilterPattern? Pattern;

    /// <summary>Go: <c>(o *TextDocumentFilterLanguageOrSchemeOrPattern) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14711）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Language is not null ? 1 : 0) + (Scheme is not null ? 1 : 0) + (Pattern is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of TextDocumentFilterLanguageOrSchemeOrPattern should be set", count);
        if (Language is not null)
        {
            MarshalEncode(enc, Language);
            return;
        }
        if (Scheme is not null)
        {
            MarshalEncode(enc, Scheme);
            return;
        }
        if (Pattern is not null)
        {
            MarshalEncode(enc, Pattern);
            return;
        }
    }

    /// <summary>Go: <c>(o *TextDocumentFilterLanguageOrSchemeOrPattern) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14717）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Language = null;
        Scheme = null;
        Pattern = null;
        var data = dec.ReadValue();
        var vLanguage = new TextDocumentFilterLanguage();
        if (TryUnmarshalInto(data, vLanguage))
        {
            Language = vLanguage;
            return;
        }
        var vScheme = new TextDocumentFilterScheme();
        if (TryUnmarshalInto(data, vScheme))
        {
            Scheme = vScheme;
            return;
        }
        var vPattern = new TextDocumentFilterPattern();
        if (TryUnmarshalInto(data, vPattern))
        {
            Pattern = vPattern;
            return;
        }
        throw ErrInvalidValue("TextDocumentFilterLanguageOrSchemeOrPattern", data);
    }

}

/// <summary>Go: <c>type StringOrMarkedStringWithLanguage struct</c>（lsp_generated.go:14742）。
/// </summary>
public sealed class StringOrMarkedStringWithLanguage : IJsonMarshaler, IJsonUnmarshaler
{
    public string? String;

    public MarkedStringWithLanguage? MarkedStringWithLanguage;

    /// <summary>Go: <c>(o *StringOrMarkedStringWithLanguage) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14749）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (String is not null ? 1 : 0) + (MarkedStringWithLanguage is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of StringOrMarkedStringWithLanguage should be set", count);
        if (String is not null)
        {
            MarshalEncode(enc, String);
            return;
        }
        if (MarkedStringWithLanguage is not null)
        {
            MarshalEncode(enc, MarkedStringWithLanguage);
            return;
        }
    }

    /// <summary>Go: <c>(o *StringOrMarkedStringWithLanguage) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14755）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        String = null;
        MarkedStringWithLanguage = null;
        switch (dec.PeekKind())
        {
            case JsonKind.String:
                String = dec.Decode<string>();
                return;
            case JsonKind.BeginObject:
                MarkedStringWithLanguage = DecodeNullable<MarkedStringWithLanguage>(dec, static d => d.DecodeNew<MarkedStringWithLanguage>());
                return;
            default:
                throw ErrInvalidKind("StringOrMarkedStringWithLanguage", dec.PeekKind());
        }
    }

}

/// <summary>Go: <c>type InlineValueTextOrVariableLookupOrEvaluatableExpression struct</c>（lsp_generated.go:14770）。
/// </summary>
public sealed class InlineValueTextOrVariableLookupOrEvaluatableExpression : IJsonMarshaler, IJsonUnmarshaler
{
    public InlineValueText? Text;

    public InlineValueVariableLookup? VariableLookup;

    public InlineValueEvaluatableExpression? EvaluatableExpression;

    /// <summary>Go: <c>(o *InlineValueTextOrVariableLookupOrEvaluatableExpression) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14778）。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        var count = (Text is not null ? 1 : 0) + (VariableLookup is not null ? 1 : 0) + (EvaluatableExpression is not null ? 1 : 0);
        AssertOnlyOne("exactly one element of InlineValueTextOrVariableLookupOrEvaluatableExpression should be set", count);
        if (Text is not null)
        {
            MarshalEncode(enc, Text);
            return;
        }
        if (VariableLookup is not null)
        {
            MarshalEncode(enc, VariableLookup);
            return;
        }
        if (EvaluatableExpression is not null)
        {
            MarshalEncode(enc, EvaluatableExpression);
            return;
        }
    }

    /// <summary>Go: <c>(o *InlineValueTextOrVariableLookupOrEvaluatableExpression) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14784）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        Text = null;
        VariableLookup = null;
        EvaluatableExpression = null;
        var data = dec.ReadValue();
        switch (JsonObjectHasKey(data, "text", "caseSensitiveLookup"))
        {
            case 0:
                Text = UnmarshalNew<InlineValueText>(data);
                return;
            case 1:
                VariableLookup = UnmarshalNew<InlineValueVariableLookup>(data);
                return;
            default:
                EvaluatableExpression = UnmarshalNew<InlineValueEvaluatableExpression>(data);
                return;
        }
    }

}
