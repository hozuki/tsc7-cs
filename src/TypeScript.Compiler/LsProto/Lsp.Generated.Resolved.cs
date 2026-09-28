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
public sealed class ResolvedChangeAnnotationsSupportOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool GroupsOnLabel = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("groupsOnLabel", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedChangeAnnotationsSupportOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.GroupsOnLabel = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedChangeAnnotationsSupportOptions)target;
            if (!t.GroupsOnLabel) { return; }
            enc.WriteToken(JsonToken.String("groupsOnLabel"));
            enc.WriteToken(JsonToken.Bool(t.GroupsOnLabel));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedWorkspaceEditClientCapabilities struct</c>（lsp_generated.go:15255）。
/// ResolvedWorkspaceEditClientCapabilities is a resolved version of WorkspaceEditClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedWorkspaceEditClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DocumentChanges = false;

    public ResourceOperationKind[]? ResourceOperations;

    public FailureHandlingKind FailureHandling;

    public bool NormalizesLineEndings = false;

    public ResolvedChangeAnnotationsSupportOptions ChangeAnnotationSupport = new();

    public bool MetadataSupport = false;

    public bool SnippetEditSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("documentChanges", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceEditClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DocumentChanges = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resourceOperations", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceEditClientCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ResourceOperations = null; }
                else { t.ResourceOperations = DecodeArray<ResourceOperationKind>(dec, static d => new ResourceOperationKind(d.Decode<string>() ?? "")); }
            }
        ),
        new StructFieldSpec("failureHandling", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceEditClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FailureHandling = new FailureHandlingKind(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("normalizesLineEndings", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceEditClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.NormalizesLineEndings = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("changeAnnotationSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceEditClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ChangeAnnotationSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("metadataSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceEditClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.MetadataSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("snippetEditSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceEditClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SnippetEditSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceEditClientCapabilities)target;
            if (!t.DocumentChanges) { return; }
            enc.WriteToken(JsonToken.String("documentChanges"));
            enc.WriteToken(JsonToken.Bool(t.DocumentChanges));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceEditClientCapabilities)target;
            if (t.ResourceOperations is null) { return; }
            enc.WriteToken(JsonToken.String("resourceOperations"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ResourceOperations)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceEditClientCapabilities)target;
            if (t.FailureHandling.Value.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("failureHandling"));
            enc.WriteToken(JsonToken.String(t.FailureHandling.Value));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceEditClientCapabilities)target;
            if (!t.NormalizesLineEndings) { return; }
            enc.WriteToken(JsonToken.String("normalizesLineEndings"));
            enc.WriteToken(JsonToken.Bool(t.NormalizesLineEndings));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceEditClientCapabilities)target;
            if ((!t.ChangeAnnotationSupport.GroupsOnLabel)) { return; }
            enc.WriteToken(JsonToken.String("changeAnnotationSupport"));
            MarshalEncode(enc, t.ChangeAnnotationSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceEditClientCapabilities)target;
            if (!t.MetadataSupport) { return; }
            enc.WriteToken(JsonToken.String("metadataSupport"));
            enc.WriteToken(JsonToken.Bool(t.MetadataSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceEditClientCapabilities)target;
            if (!t.SnippetEditSupport) { return; }
            enc.WriteToken(JsonToken.String("snippetEditSupport"));
            enc.WriteToken(JsonToken.Bool(t.SnippetEditSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDidChangeConfigurationClientCapabilities struct</c>（lsp_generated.go:15308）。
/// ResolvedDidChangeConfigurationClientCapabilities is a resolved version of DidChangeConfigurationClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedDidChangeConfigurationClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDidChangeConfigurationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDidChangeConfigurationClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDidChangeWatchedFilesClientCapabilities struct</c>（lsp_generated.go:15324）。
/// ResolvedDidChangeWatchedFilesClientCapabilities is a resolved version of DidChangeWatchedFilesClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedDidChangeWatchedFilesClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool RelativePatternSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDidChangeWatchedFilesClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("relativePatternSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDidChangeWatchedFilesClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RelativePatternSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDidChangeWatchedFilesClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDidChangeWatchedFilesClientCapabilities)target;
            if (!t.RelativePatternSupport) { return; }
            enc.WriteToken(JsonToken.String("relativePatternSupport"));
            enc.WriteToken(JsonToken.Bool(t.RelativePatternSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientSymbolKindOptions struct</c>（lsp_generated.go:15350）。
/// ResolvedClientSymbolKindOptions is a resolved version of ClientSymbolKindOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSymbolKindOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public SymbolKind[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSymbolKindOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<SymbolKind>(dec, static d => d.Decode<SymbolKind>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientSymbolKindOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.Number(((uint)item).ToString(CultureInfo.InvariantCulture)));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientSymbolTagOptions struct</c>（lsp_generated.go:15375）。
/// ResolvedClientSymbolTagOptions is a resolved version of ClientSymbolTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSymbolTagOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public SymbolTag[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSymbolTagOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<SymbolTag>(dec, static d => d.Decode<SymbolTag>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientSymbolTagOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.Number(((uint)item).ToString(CultureInfo.InvariantCulture)));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientSymbolResolveOptions struct</c>（lsp_generated.go:15393）。
/// ResolvedClientSymbolResolveOptions is a resolved version of ClientSymbolResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSymbolResolveOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public string[]? Properties;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("properties", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSymbolResolveOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Properties = null; }
                else { t.Properties = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientSymbolResolveOptions)target;
            if (t.Properties is null) { return; }
            enc.WriteToken(JsonToken.String("properties"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.Properties)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedWorkspaceSymbolClientCapabilities struct</c>（lsp_generated.go:15412）。
/// ResolvedWorkspaceSymbolClientCapabilities is a resolved version of WorkspaceSymbolClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for a WorkspaceSymbolRequest.
/// </summary>
public sealed class ResolvedWorkspaceSymbolClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public ResolvedClientSymbolKindOptions SymbolKind = new();

    public ResolvedClientSymbolTagOptions TagSupport = new();

    public ResolvedClientSymbolResolveOptions ResolveSupport = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbolKind", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SymbolKind.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tagSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TagSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolveSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolveSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
            if ((t.SymbolKind.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("symbolKind"));
            MarshalEncode(enc, t.SymbolKind);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
            if ((t.TagSupport.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("tagSupport"));
            MarshalEncode(enc, t.TagSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceSymbolClientCapabilities)target;
            if ((t.ResolveSupport.Properties is null)) { return; }
            enc.WriteToken(JsonToken.String("resolveSupport"));
            MarshalEncode(enc, t.ResolveSupport);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedExecuteCommandClientCapabilities struct</c>（lsp_generated.go:15446）。
/// ResolvedExecuteCommandClientCapabilities is a resolved version of ExecuteCommandClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The client capabilities of a ExecuteCommandRequest.
/// </summary>
public sealed class ResolvedExecuteCommandClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedExecuteCommandClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedExecuteCommandClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedSemanticTokensWorkspaceClientCapabilities struct</c>（lsp_generated.go:15464）。
/// ResolvedSemanticTokensWorkspaceClientCapabilities is a resolved version of SemanticTokensWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedSemanticTokensWorkspaceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RefreshSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("refreshSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RefreshSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensWorkspaceClientCapabilities)target;
            if (!t.RefreshSupport) { return; }
            enc.WriteToken(JsonToken.String("refreshSupport"));
            enc.WriteToken(JsonToken.Bool(t.RefreshSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedCodeLensWorkspaceClientCapabilities struct</c>（lsp_generated.go:15488）。
/// ResolvedCodeLensWorkspaceClientCapabilities is a resolved version of CodeLensWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedCodeLensWorkspaceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RefreshSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("refreshSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeLensWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RefreshSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCodeLensWorkspaceClientCapabilities)target;
            if (!t.RefreshSupport) { return; }
            enc.WriteToken(JsonToken.String("refreshSupport"));
            enc.WriteToken(JsonToken.Bool(t.RefreshSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
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
public sealed class ResolvedFileOperationClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool DidCreate = false;

    public bool WillCreate = false;

    public bool DidRename = false;

    public bool WillRename = false;

    public bool DidDelete = false;

    public bool WillDelete = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFileOperationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("didCreate", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFileOperationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DidCreate = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("willCreate", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFileOperationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WillCreate = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("didRename", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFileOperationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DidRename = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("willRename", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFileOperationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WillRename = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("didDelete", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFileOperationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DidDelete = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("willDelete", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFileOperationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WillDelete = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedFileOperationClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFileOperationClientCapabilities)target;
            if (!t.DidCreate) { return; }
            enc.WriteToken(JsonToken.String("didCreate"));
            enc.WriteToken(JsonToken.Bool(t.DidCreate));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFileOperationClientCapabilities)target;
            if (!t.WillCreate) { return; }
            enc.WriteToken(JsonToken.String("willCreate"));
            enc.WriteToken(JsonToken.Bool(t.WillCreate));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFileOperationClientCapabilities)target;
            if (!t.DidRename) { return; }
            enc.WriteToken(JsonToken.String("didRename"));
            enc.WriteToken(JsonToken.Bool(t.DidRename));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFileOperationClientCapabilities)target;
            if (!t.WillRename) { return; }
            enc.WriteToken(JsonToken.String("willRename"));
            enc.WriteToken(JsonToken.Bool(t.WillRename));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFileOperationClientCapabilities)target;
            if (!t.DidDelete) { return; }
            enc.WriteToken(JsonToken.String("didDelete"));
            enc.WriteToken(JsonToken.Bool(t.DidDelete));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFileOperationClientCapabilities)target;
            if (!t.WillDelete) { return; }
            enc.WriteToken(JsonToken.String("willDelete"));
            enc.WriteToken(JsonToken.Bool(t.WillDelete));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedInlineValueWorkspaceClientCapabilities struct</c>（lsp_generated.go:15555）。
/// ResolvedInlineValueWorkspaceClientCapabilities is a resolved version of InlineValueWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client workspace capabilities specific to inline values.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlineValueWorkspaceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RefreshSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("refreshSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedInlineValueWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RefreshSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedInlineValueWorkspaceClientCapabilities)target;
            if (!t.RefreshSupport) { return; }
            enc.WriteToken(JsonToken.String("refreshSupport"));
            enc.WriteToken(JsonToken.Bool(t.RefreshSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedInlayHintWorkspaceClientCapabilities struct</c>（lsp_generated.go:15581）。
/// ResolvedInlayHintWorkspaceClientCapabilities is a resolved version of InlayHintWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client workspace capabilities specific to inlay hints.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlayHintWorkspaceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RefreshSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("refreshSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedInlayHintWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RefreshSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedInlayHintWorkspaceClientCapabilities)target;
            if (!t.RefreshSupport) { return; }
            enc.WriteToken(JsonToken.String("refreshSupport"));
            enc.WriteToken(JsonToken.Bool(t.RefreshSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDiagnosticWorkspaceClientCapabilities struct</c>（lsp_generated.go:15607）。
/// ResolvedDiagnosticWorkspaceClientCapabilities is a resolved version of DiagnosticWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Workspace client capabilities specific to diagnostic pull requests.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedDiagnosticWorkspaceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RefreshSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("refreshSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RefreshSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticWorkspaceClientCapabilities)target;
            if (!t.RefreshSupport) { return; }
            enc.WriteToken(JsonToken.String("refreshSupport"));
            enc.WriteToken(JsonToken.Bool(t.RefreshSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedFoldingRangeWorkspaceClientCapabilities struct</c>（lsp_generated.go:15633）。
/// ResolvedFoldingRangeWorkspaceClientCapabilities is a resolved version of FoldingRangeWorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// # Client workspace capabilities specific to folding ranges
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedFoldingRangeWorkspaceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RefreshSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("refreshSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFoldingRangeWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RefreshSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedFoldingRangeWorkspaceClientCapabilities)target;
            if (!t.RefreshSupport) { return; }
            enc.WriteToken(JsonToken.String("refreshSupport"));
            enc.WriteToken(JsonToken.Bool(t.RefreshSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedTextDocumentContentClientCapabilities struct</c>（lsp_generated.go:15661）。
/// ResolvedTextDocumentContentClientCapabilities is a resolved version of TextDocumentContentClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for a text document content provider.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedTextDocumentContentClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentContentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentContentClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedWorkspaceClientCapabilities struct</c>（lsp_generated.go:15679）。
/// ResolvedWorkspaceClientCapabilities is a resolved version of WorkspaceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Workspace specific client capabilities.
/// </summary>
public sealed class ResolvedWorkspaceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
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

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("applyEdit", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ApplyEdit = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("workspaceEdit", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WorkspaceEdit.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("didChangeConfiguration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DidChangeConfiguration.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("didChangeWatchedFiles", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DidChangeWatchedFiles.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("executeCommand", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ExecuteCommand.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("workspaceFolders", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WorkspaceFolders = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("configuration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Configuration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("semanticTokens", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SemanticTokens.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("codeLens", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CodeLens.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("fileOperations", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FileOperations.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("inlineValue", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InlineValue.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("inlayHint", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InlayHint.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("diagnostics", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Diagnostics.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("foldingRange", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FoldingRange.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("textDocumentContent", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWorkspaceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TextDocumentContent.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if (!t.ApplyEdit) { return; }
            enc.WriteToken(JsonToken.String("applyEdit"));
            enc.WriteToken(JsonToken.Bool(t.ApplyEdit));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.WorkspaceEdit.DocumentChanges && t.WorkspaceEdit.ResourceOperations is null && t.WorkspaceEdit.FailureHandling.Value.Length == 0 && !t.WorkspaceEdit.NormalizesLineEndings && (!t.WorkspaceEdit.ChangeAnnotationSupport.GroupsOnLabel) && !t.WorkspaceEdit.MetadataSupport && !t.WorkspaceEdit.SnippetEditSupport)) { return; }
            enc.WriteToken(JsonToken.String("workspaceEdit"));
            MarshalEncode(enc, t.WorkspaceEdit);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.DidChangeConfiguration.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("didChangeConfiguration"));
            MarshalEncode(enc, t.DidChangeConfiguration);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.DidChangeWatchedFiles.DynamicRegistration && !t.DidChangeWatchedFiles.RelativePatternSupport)) { return; }
            enc.WriteToken(JsonToken.String("didChangeWatchedFiles"));
            MarshalEncode(enc, t.DidChangeWatchedFiles);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.Symbol.DynamicRegistration && (t.Symbol.SymbolKind.ValueSet is null) && (t.Symbol.TagSupport.ValueSet is null) && (t.Symbol.ResolveSupport.Properties is null))) { return; }
            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.ExecuteCommand.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("executeCommand"));
            MarshalEncode(enc, t.ExecuteCommand);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if (!t.WorkspaceFolders) { return; }
            enc.WriteToken(JsonToken.String("workspaceFolders"));
            enc.WriteToken(JsonToken.Bool(t.WorkspaceFolders));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if (!t.Configuration) { return; }
            enc.WriteToken(JsonToken.String("configuration"));
            enc.WriteToken(JsonToken.Bool(t.Configuration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.SemanticTokens.RefreshSupport)) { return; }
            enc.WriteToken(JsonToken.String("semanticTokens"));
            MarshalEncode(enc, t.SemanticTokens);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.CodeLens.RefreshSupport)) { return; }
            enc.WriteToken(JsonToken.String("codeLens"));
            MarshalEncode(enc, t.CodeLens);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.FileOperations.DynamicRegistration && !t.FileOperations.DidCreate && !t.FileOperations.WillCreate && !t.FileOperations.DidRename && !t.FileOperations.WillRename && !t.FileOperations.DidDelete && !t.FileOperations.WillDelete)) { return; }
            enc.WriteToken(JsonToken.String("fileOperations"));
            MarshalEncode(enc, t.FileOperations);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.InlineValue.RefreshSupport)) { return; }
            enc.WriteToken(JsonToken.String("inlineValue"));
            MarshalEncode(enc, t.InlineValue);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.InlayHint.RefreshSupport)) { return; }
            enc.WriteToken(JsonToken.String("inlayHint"));
            MarshalEncode(enc, t.InlayHint);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.Diagnostics.RefreshSupport)) { return; }
            enc.WriteToken(JsonToken.String("diagnostics"));
            MarshalEncode(enc, t.Diagnostics);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.FoldingRange.RefreshSupport)) { return; }
            enc.WriteToken(JsonToken.String("foldingRange"));
            MarshalEncode(enc, t.FoldingRange);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWorkspaceClientCapabilities)target;
            if ((!t.TextDocumentContent.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("textDocumentContent"));
            MarshalEncode(enc, t.TextDocumentContent);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedTextDocumentSyncClientCapabilities struct</c>（lsp_generated.go:15767）。
/// ResolvedTextDocumentSyncClientCapabilities is a resolved version of TextDocumentSyncClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedTextDocumentSyncClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool WillSave = false;

    public bool WillSaveWaitUntil = false;

    public bool DidSave = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentSyncClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("willSave", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentSyncClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WillSave = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("willSaveWaitUntil", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentSyncClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WillSaveWaitUntil = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("didSave", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentSyncClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DidSave = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentSyncClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentSyncClientCapabilities)target;
            if (!t.WillSave) { return; }
            enc.WriteToken(JsonToken.String("willSave"));
            enc.WriteToken(JsonToken.Bool(t.WillSave));
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentSyncClientCapabilities)target;
            if (!t.WillSaveWaitUntil) { return; }
            enc.WriteToken(JsonToken.String("willSaveWaitUntil"));
            enc.WriteToken(JsonToken.Bool(t.WillSaveWaitUntil));
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentSyncClientCapabilities)target;
            if (!t.DidSave) { return; }
            enc.WriteToken(JsonToken.String("didSave"));
            enc.WriteToken(JsonToken.Bool(t.DidSave));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedTextDocumentFilterClientCapabilities struct</c>（lsp_generated.go:15794）。
/// ResolvedTextDocumentFilterClientCapabilities is a resolved version of TextDocumentFilterClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedTextDocumentFilterClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RelativePatternSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("relativePatternSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentFilterClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RelativePatternSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentFilterClientCapabilities)target;
            if (!t.RelativePatternSupport) { return; }
            enc.WriteToken(JsonToken.String("relativePatternSupport"));
            enc.WriteToken(JsonToken.Bool(t.RelativePatternSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedCompletionItemTagOptions struct</c>（lsp_generated.go:15814）。
/// ResolvedCompletionItemTagOptions is a resolved version of CompletionItemTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedCompletionItemTagOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public CompletionItemTag[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionItemTagOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<CompletionItemTag>(dec, static d => d.Decode<CompletionItemTag>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCompletionItemTagOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.Number(((uint)item).ToString(CultureInfo.InvariantCulture)));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCompletionItemResolveOptions struct</c>（lsp_generated.go:15832）。
/// ResolvedClientCompletionItemResolveOptions is a resolved version of ClientCompletionItemResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemResolveOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public string[]? Properties;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("properties", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemResolveOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Properties = null; }
                else { t.Properties = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemResolveOptions)target;
            if (t.Properties is null) { return; }
            enc.WriteToken(JsonToken.String("properties"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.Properties)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCompletionItemInsertTextModeOptions struct</c>（lsp_generated.go:15850）。
/// ResolvedClientCompletionItemInsertTextModeOptions is a resolved version of ClientCompletionItemInsertTextModeOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemInsertTextModeOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public InsertTextMode[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemInsertTextModeOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<InsertTextMode>(dec, static d => d.Decode<InsertTextMode>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemInsertTextModeOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.Number(((uint)item).ToString(CultureInfo.InvariantCulture)));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCompletionItemOptions struct</c>（lsp_generated.go:15867）。
/// ResolvedClientCompletionItemOptions is a resolved version of ClientCompletionItemOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemOptions : IJsonMarshaler, IJsonUnmarshaler
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

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snippetSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SnippetSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("commitCharactersSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CommitCharactersSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("documentationFormat", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.DocumentationFormat = null; }
                else { t.DocumentationFormat = DecodeArray<MarkupKind>(dec, static d => new MarkupKind(d.Decode<string>() ?? "")); }
            }
        ),
        new StructFieldSpec("deprecatedSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DeprecatedSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("preselectSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PreselectSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tagSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TagSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("insertReplaceSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InsertReplaceSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolveSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolveSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("insertTextModeSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InsertTextModeSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("labelDetailsSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LabelDetailsSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if (!t.SnippetSupport) { return; }
            enc.WriteToken(JsonToken.String("snippetSupport"));
            enc.WriteToken(JsonToken.Bool(t.SnippetSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if (!t.CommitCharactersSupport) { return; }
            enc.WriteToken(JsonToken.String("commitCharactersSupport"));
            enc.WriteToken(JsonToken.Bool(t.CommitCharactersSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if (t.DocumentationFormat is null) { return; }
            enc.WriteToken(JsonToken.String("documentationFormat"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.DocumentationFormat)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if (!t.DeprecatedSupport) { return; }
            enc.WriteToken(JsonToken.String("deprecatedSupport"));
            enc.WriteToken(JsonToken.Bool(t.DeprecatedSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if (!t.PreselectSupport) { return; }
            enc.WriteToken(JsonToken.String("preselectSupport"));
            enc.WriteToken(JsonToken.Bool(t.PreselectSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if ((t.TagSupport.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("tagSupport"));
            MarshalEncode(enc, t.TagSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if (!t.InsertReplaceSupport) { return; }
            enc.WriteToken(JsonToken.String("insertReplaceSupport"));
            enc.WriteToken(JsonToken.Bool(t.InsertReplaceSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if ((t.ResolveSupport.Properties is null)) { return; }
            enc.WriteToken(JsonToken.String("resolveSupport"));
            MarshalEncode(enc, t.ResolveSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if ((t.InsertTextModeSupport.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("insertTextModeSupport"));
            MarshalEncode(enc, t.InsertTextModeSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptions)target;
            if (!t.LabelDetailsSupport) { return; }
            enc.WriteToken(JsonToken.String("labelDetailsSupport"));
            enc.WriteToken(JsonToken.Bool(t.LabelDetailsSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCompletionItemOptionsKind struct</c>（lsp_generated.go:15937）。
/// ResolvedClientCompletionItemOptionsKind is a resolved version of ClientCompletionItemOptionsKind with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCompletionItemOptionsKind : IJsonMarshaler, IJsonUnmarshaler
{
    public CompletionItemKind[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCompletionItemOptionsKind)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<CompletionItemKind>(dec, static d => d.Decode<CompletionItemKind>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCompletionItemOptionsKind)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.Number(((uint)item).ToString(CultureInfo.InvariantCulture)));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
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
public sealed class ResolvedCompletionListCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public string[]? ItemDefaults;

    public bool ApplyKindSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("itemDefaults", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionListCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ItemDefaults = null; }
                else { t.ItemDefaults = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
        new StructFieldSpec("applyKindSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionListCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ApplyKindSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCompletionListCapabilities)target;
            if (t.ItemDefaults is null) { return; }
            enc.WriteToken(JsonToken.String("itemDefaults"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ItemDefaults)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        },
        static (target, enc) =>
        {
            var t = (ResolvedCompletionListCapabilities)target;
            if (!t.ApplyKindSupport) { return; }
            enc.WriteToken(JsonToken.String("applyKindSupport"));
            enc.WriteToken(JsonToken.Bool(t.ApplyKindSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedCompletionClientCapabilities struct</c>（lsp_generated.go:16003）。
/// ResolvedCompletionClientCapabilities is a resolved version of CompletionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Completion client capabilities
/// </summary>
public sealed class ResolvedCompletionClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public ResolvedClientCompletionItemOptions CompletionItem = new();

    public ResolvedClientCompletionItemOptionsKind CompletionItemKind = new();

    public InsertTextMode InsertTextMode;

    public bool ContextSupport = false;

    public ResolvedCompletionListCapabilities CompletionList = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("completionItem", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CompletionItem.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("completionItemKind", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CompletionItemKind.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("insertTextMode", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InsertTextMode = dec.Decode<InsertTextMode>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("contextSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ContextSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("completionList", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCompletionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CompletionList.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCompletionClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCompletionClientCapabilities)target;
            if ((!t.CompletionItem.SnippetSupport && !t.CompletionItem.CommitCharactersSupport && t.CompletionItem.DocumentationFormat is null && !t.CompletionItem.DeprecatedSupport && !t.CompletionItem.PreselectSupport && (t.CompletionItem.TagSupport.ValueSet is null) && !t.CompletionItem.InsertReplaceSupport && (t.CompletionItem.ResolveSupport.Properties is null) && (t.CompletionItem.InsertTextModeSupport.ValueSet is null) && !t.CompletionItem.LabelDetailsSupport)) { return; }
            enc.WriteToken(JsonToken.String("completionItem"));
            MarshalEncode(enc, t.CompletionItem);
        },
        static (target, enc) =>
        {
            var t = (ResolvedCompletionClientCapabilities)target;
            if ((t.CompletionItemKind.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("completionItemKind"));
            MarshalEncode(enc, t.CompletionItemKind);
        },
        static (target, enc) =>
        {
            var t = (ResolvedCompletionClientCapabilities)target;
            if (t.InsertTextMode == default) { return; }
            enc.WriteToken(JsonToken.String("insertTextMode"));
            enc.WriteToken(JsonToken.Number(((uint)t.InsertTextMode).ToString(CultureInfo.InvariantCulture)));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCompletionClientCapabilities)target;
            if (!t.ContextSupport) { return; }
            enc.WriteToken(JsonToken.String("contextSupport"));
            enc.WriteToken(JsonToken.Bool(t.ContextSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCompletionClientCapabilities)target;
            if ((t.CompletionList.ItemDefaults is null && !t.CompletionList.ApplyKindSupport)) { return; }
            enc.WriteToken(JsonToken.String("completionList"));
            MarshalEncode(enc, t.CompletionList);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedHoverClientCapabilities struct</c>（lsp_generated.go:16043）。
/// ResolvedHoverClientCapabilities is a resolved version of HoverClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedHoverClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public MarkupKind[]? ContentFormat;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedHoverClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("contentFormat", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedHoverClientCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ContentFormat = null; }
                else { t.ContentFormat = DecodeArray<MarkupKind>(dec, static d => new MarkupKind(d.Decode<string>() ?? "")); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedHoverClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedHoverClientCapabilities)target;
            if (t.ContentFormat is null) { return; }
            enc.WriteToken(JsonToken.String("contentFormat"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ContentFormat)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientSignatureParameterInformationOptions struct</c>（lsp_generated.go:16065）。
/// ResolvedClientSignatureParameterInformationOptions is a resolved version of ClientSignatureParameterInformationOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSignatureParameterInformationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool LabelOffsetSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("labelOffsetSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSignatureParameterInformationOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LabelOffsetSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientSignatureParameterInformationOptions)target;
            if (!t.LabelOffsetSupport) { return; }
            enc.WriteToken(JsonToken.String("labelOffsetSupport"));
            enc.WriteToken(JsonToken.Bool(t.LabelOffsetSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientSignatureInformationOptions struct</c>（lsp_generated.go:16086）。
/// ResolvedClientSignatureInformationOptions is a resolved version of ClientSignatureInformationOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSignatureInformationOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public MarkupKind[]? DocumentationFormat;

    public ResolvedClientSignatureParameterInformationOptions ParameterInformation = new();

    public bool ActiveParameterSupport = false;

    public bool NoActiveParameterSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("documentationFormat", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSignatureInformationOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.DocumentationFormat = null; }
                else { t.DocumentationFormat = DecodeArray<MarkupKind>(dec, static d => new MarkupKind(d.Decode<string>() ?? "")); }
            }
        ),
        new StructFieldSpec("parameterInformation", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSignatureInformationOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ParameterInformation.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("activeParameterSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSignatureInformationOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ActiveParameterSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("noActiveParameterSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSignatureInformationOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.NoActiveParameterSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientSignatureInformationOptions)target;
            if (t.DocumentationFormat is null) { return; }
            enc.WriteToken(JsonToken.String("documentationFormat"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.DocumentationFormat)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientSignatureInformationOptions)target;
            if ((!t.ParameterInformation.LabelOffsetSupport)) { return; }
            enc.WriteToken(JsonToken.String("parameterInformation"));
            MarshalEncode(enc, t.ParameterInformation);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientSignatureInformationOptions)target;
            if (!t.ActiveParameterSupport) { return; }
            enc.WriteToken(JsonToken.String("activeParameterSupport"));
            enc.WriteToken(JsonToken.Bool(t.ActiveParameterSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientSignatureInformationOptions)target;
            if (!t.NoActiveParameterSupport) { return; }
            enc.WriteToken(JsonToken.String("noActiveParameterSupport"));
            enc.WriteToken(JsonToken.Bool(t.NoActiveParameterSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedSignatureHelpClientCapabilities struct</c>（lsp_generated.go:16121）。
/// ResolvedSignatureHelpClientCapabilities is a resolved version of SignatureHelpClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a SignatureHelpRequest.
/// </summary>
public sealed class ResolvedSignatureHelpClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public ResolvedClientSignatureInformationOptions SignatureInformation = new();

    public bool ContextSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSignatureHelpClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("signatureInformation", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSignatureHelpClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SignatureInformation.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("contextSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSignatureHelpClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ContextSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedSignatureHelpClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedSignatureHelpClientCapabilities)target;
            if ((t.SignatureInformation.DocumentationFormat is null && (!t.SignatureInformation.ParameterInformation.LabelOffsetSupport) && !t.SignatureInformation.ActiveParameterSupport && !t.SignatureInformation.NoActiveParameterSupport)) { return; }
            enc.WriteToken(JsonToken.String("signatureInformation"));
            MarshalEncode(enc, t.SignatureInformation);
        },
        static (target, enc) =>
        {
            var t = (ResolvedSignatureHelpClientCapabilities)target;
            if (!t.ContextSupport) { return; }
            enc.WriteToken(JsonToken.String("contextSupport"));
            enc.WriteToken(JsonToken.Bool(t.ContextSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDeclarationClientCapabilities struct</c>（lsp_generated.go:16151）。
/// ResolvedDeclarationClientCapabilities is a resolved version of DeclarationClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.14.0
/// </summary>
public sealed class ResolvedDeclarationClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDeclarationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("linkSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDeclarationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LinkSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDeclarationClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDeclarationClientCapabilities)target;
            if (!t.LinkSupport) { return; }
            enc.WriteToken(JsonToken.String("linkSupport"));
            enc.WriteToken(JsonToken.Bool(t.LinkSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDefinitionClientCapabilities struct</c>（lsp_generated.go:16174）。
/// ResolvedDefinitionClientCapabilities is a resolved version of DefinitionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a DefinitionRequest.
/// </summary>
public sealed class ResolvedDefinitionClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDefinitionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("linkSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDefinitionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LinkSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDefinitionClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDefinitionClientCapabilities)target;
            if (!t.LinkSupport) { return; }
            enc.WriteToken(JsonToken.String("linkSupport"));
            enc.WriteToken(JsonToken.Bool(t.LinkSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedTypeDefinitionClientCapabilities struct</c>（lsp_generated.go:16197）。
/// ResolvedTypeDefinitionClientCapabilities is a resolved version of TypeDefinitionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since 3.6.0
/// </summary>
public sealed class ResolvedTypeDefinitionClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeDefinitionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("linkSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeDefinitionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LinkSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedTypeDefinitionClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedTypeDefinitionClientCapabilities)target;
            if (!t.LinkSupport) { return; }
            enc.WriteToken(JsonToken.String("linkSupport"));
            enc.WriteToken(JsonToken.Bool(t.LinkSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedImplementationClientCapabilities struct</c>（lsp_generated.go:16222）。
/// ResolvedImplementationClientCapabilities is a resolved version of ImplementationClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.6.0
/// </summary>
public sealed class ResolvedImplementationClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool LinkSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedImplementationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("linkSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedImplementationClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LinkSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedImplementationClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedImplementationClientCapabilities)target;
            if (!t.LinkSupport) { return; }
            enc.WriteToken(JsonToken.String("linkSupport"));
            enc.WriteToken(JsonToken.Bool(t.LinkSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedReferenceClientCapabilities struct</c>（lsp_generated.go:16247）。
/// ResolvedReferenceClientCapabilities is a resolved version of ReferenceClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a ReferencesRequest.
/// </summary>
public sealed class ResolvedReferenceClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedReferenceClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedReferenceClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDocumentHighlightClientCapabilities struct</c>（lsp_generated.go:16265）。
/// ResolvedDocumentHighlightClientCapabilities is a resolved version of DocumentHighlightClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a DocumentHighlightRequest.
/// </summary>
public sealed class ResolvedDocumentHighlightClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentHighlightClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDocumentHighlightClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDocumentSymbolClientCapabilities struct</c>（lsp_generated.go:16283）。
/// ResolvedDocumentSymbolClientCapabilities is a resolved version of DocumentSymbolClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client Capabilities for a DocumentSymbolRequest.
/// </summary>
public sealed class ResolvedDocumentSymbolClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public ResolvedClientSymbolKindOptions SymbolKind = new();

    public bool HierarchicalDocumentSymbolSupport = false;

    public ResolvedClientSymbolTagOptions TagSupport = new();

    public bool LabelSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbolKind", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SymbolKind.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("hierarchicalDocumentSymbolSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.HierarchicalDocumentSymbolSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tagSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TagSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("labelSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentSymbolClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LabelSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDocumentSymbolClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDocumentSymbolClientCapabilities)target;
            if ((t.SymbolKind.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("symbolKind"));
            MarshalEncode(enc, t.SymbolKind);
        },
        static (target, enc) =>
        {
            var t = (ResolvedDocumentSymbolClientCapabilities)target;
            if (!t.HierarchicalDocumentSymbolSupport) { return; }
            enc.WriteToken(JsonToken.String("hierarchicalDocumentSymbolSupport"));
            enc.WriteToken(JsonToken.Bool(t.HierarchicalDocumentSymbolSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDocumentSymbolClientCapabilities)target;
            if ((t.TagSupport.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("tagSupport"));
            MarshalEncode(enc, t.TagSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedDocumentSymbolClientCapabilities)target;
            if (!t.LabelSupport) { return; }
            enc.WriteToken(JsonToken.String("labelSupport"));
            enc.WriteToken(JsonToken.Bool(t.LabelSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCodeActionKindOptions struct</c>（lsp_generated.go:16321）。
/// ResolvedClientCodeActionKindOptions is a resolved version of ClientCodeActionKindOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeActionKindOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public CodeActionKind[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCodeActionKindOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<CodeActionKind>(dec, static d => new CodeActionKind(d.Decode<string>() ?? "")); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCodeActionKindOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCodeActionLiteralOptions struct</c>（lsp_generated.go:16342）。
/// ResolvedClientCodeActionLiteralOptions is a resolved version of ClientCodeActionLiteralOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeActionLiteralOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public ResolvedClientCodeActionKindOptions CodeActionKind = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("codeActionKind", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCodeActionLiteralOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CodeActionKind.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCodeActionLiteralOptions)target;
            if ((t.CodeActionKind.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("codeActionKind"));
            MarshalEncode(enc, t.CodeActionKind);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCodeActionResolveOptions struct</c>（lsp_generated.go:16361）。
/// ResolvedClientCodeActionResolveOptions is a resolved version of ClientCodeActionResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeActionResolveOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public string[]? Properties;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("properties", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCodeActionResolveOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Properties = null; }
                else { t.Properties = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCodeActionResolveOptions)target;
            if (t.Properties is null) { return; }
            enc.WriteToken(JsonToken.String("properties"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.Properties)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedCodeActionTagOptions struct</c>（lsp_generated.go:16379）。
/// ResolvedCodeActionTagOptions is a resolved version of CodeActionTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedCodeActionTagOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public CodeActionTag[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionTagOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<CodeActionTag>(dec, static d => d.Decode<CodeActionTag>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionTagOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.Number(((uint)item).ToString(CultureInfo.InvariantCulture)));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedCodeActionClientCapabilities struct</c>（lsp_generated.go:16397）。
/// ResolvedCodeActionClientCapabilities is a resolved version of CodeActionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The Client Capabilities of a CodeActionRequest.
/// </summary>
public sealed class ResolvedCodeActionClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
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

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("codeActionLiteralSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CodeActionLiteralSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("isPreferredSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsPreferredSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("disabledSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DisabledSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("dataSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DataSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolveSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolveSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("honorsChangeAnnotations", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.HonorsChangeAnnotations = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("documentationSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DocumentationSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tagSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeActionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TagSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if (((t.CodeActionLiteralSupport.CodeActionKind.ValueSet is null))) { return; }
            enc.WriteToken(JsonToken.String("codeActionLiteralSupport"));
            MarshalEncode(enc, t.CodeActionLiteralSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if (!t.IsPreferredSupport) { return; }
            enc.WriteToken(JsonToken.String("isPreferredSupport"));
            enc.WriteToken(JsonToken.Bool(t.IsPreferredSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if (!t.DisabledSupport) { return; }
            enc.WriteToken(JsonToken.String("disabledSupport"));
            enc.WriteToken(JsonToken.Bool(t.DisabledSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if (!t.DataSupport) { return; }
            enc.WriteToken(JsonToken.String("dataSupport"));
            enc.WriteToken(JsonToken.Bool(t.DataSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if ((t.ResolveSupport.Properties is null)) { return; }
            enc.WriteToken(JsonToken.String("resolveSupport"));
            MarshalEncode(enc, t.ResolveSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if (!t.HonorsChangeAnnotations) { return; }
            enc.WriteToken(JsonToken.String("honorsChangeAnnotations"));
            enc.WriteToken(JsonToken.Bool(t.HonorsChangeAnnotations));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if (!t.DocumentationSupport) { return; }
            enc.WriteToken(JsonToken.String("documentationSupport"));
            enc.WriteToken(JsonToken.Bool(t.DocumentationSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeActionClientCapabilities)target;
            if ((t.TagSupport.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("tagSupport"));
            MarshalEncode(enc, t.TagSupport);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCodeLensResolveOptions struct</c>（lsp_generated.go:16466）。
/// ResolvedClientCodeLensResolveOptions is a resolved version of ClientCodeLensResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientCodeLensResolveOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public string[]? Properties;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("properties", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCodeLensResolveOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Properties = null; }
                else { t.Properties = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCodeLensResolveOptions)target;
            if (t.Properties is null) { return; }
            enc.WriteToken(JsonToken.String("properties"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.Properties)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedCodeLensClientCapabilities struct</c>（lsp_generated.go:16484）。
/// ResolvedCodeLensClientCapabilities is a resolved version of CodeLensClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The client capabilities of a CodeLensRequest.
/// </summary>
public sealed class ResolvedCodeLensClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public ResolvedClientCodeLensResolveOptions ResolveSupport = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeLensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolveSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCodeLensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolveSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCodeLensClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedCodeLensClientCapabilities)target;
            if ((t.ResolveSupport.Properties is null)) { return; }
            enc.WriteToken(JsonToken.String("resolveSupport"));
            MarshalEncode(enc, t.ResolveSupport);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDocumentLinkClientCapabilities struct</c>（lsp_generated.go:16508）。
/// ResolvedDocumentLinkClientCapabilities is a resolved version of DocumentLinkClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The client capabilities of a DocumentLinkRequest.
/// </summary>
public sealed class ResolvedDocumentLinkClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool TooltipSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentLinkClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tooltipSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentLinkClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TooltipSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDocumentLinkClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDocumentLinkClientCapabilities)target;
            if (!t.TooltipSupport) { return; }
            enc.WriteToken(JsonToken.String("tooltipSupport"));
            enc.WriteToken(JsonToken.Bool(t.TooltipSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDocumentColorClientCapabilities struct</c>（lsp_generated.go:16529）。
/// ResolvedDocumentColorClientCapabilities is a resolved version of DocumentColorClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedDocumentColorClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentColorClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDocumentColorClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDocumentFormattingClientCapabilities struct</c>（lsp_generated.go:16549）。
/// ResolvedDocumentFormattingClientCapabilities is a resolved version of DocumentFormattingClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities of a DocumentFormattingRequest.
/// </summary>
public sealed class ResolvedDocumentFormattingClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentFormattingClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDocumentFormattingClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDocumentRangeFormattingClientCapabilities struct</c>（lsp_generated.go:16567）。
/// ResolvedDocumentRangeFormattingClientCapabilities is a resolved version of DocumentRangeFormattingClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities of a DocumentRangeFormattingRequest.
/// </summary>
public sealed class ResolvedDocumentRangeFormattingClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool RangesSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentRangeFormattingClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("rangesSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentRangeFormattingClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RangesSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDocumentRangeFormattingClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDocumentRangeFormattingClientCapabilities)target;
            if (!t.RangesSupport) { return; }
            enc.WriteToken(JsonToken.String("rangesSupport"));
            enc.WriteToken(JsonToken.Bool(t.RangesSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDocumentOnTypeFormattingClientCapabilities struct</c>（lsp_generated.go:16590）。
/// ResolvedDocumentOnTypeFormattingClientCapabilities is a resolved version of DocumentOnTypeFormattingClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities of a DocumentOnTypeFormattingRequest.
/// </summary>
public sealed class ResolvedDocumentOnTypeFormattingClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDocumentOnTypeFormattingClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDocumentOnTypeFormattingClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedRenameClientCapabilities struct</c>（lsp_generated.go:16606）。
/// ResolvedRenameClientCapabilities is a resolved version of RenameClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedRenameClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public bool PrepareSupport = false;

    public PrepareSupportDefaultBehavior PrepareSupportDefaultBehavior;

    public bool HonorsChangeAnnotations = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedRenameClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("prepareSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedRenameClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PrepareSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("prepareSupportDefaultBehavior", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedRenameClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PrepareSupportDefaultBehavior = dec.Decode<PrepareSupportDefaultBehavior>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("honorsChangeAnnotations", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedRenameClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.HonorsChangeAnnotations = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedRenameClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedRenameClientCapabilities)target;
            if (!t.PrepareSupport) { return; }
            enc.WriteToken(JsonToken.String("prepareSupport"));
            enc.WriteToken(JsonToken.Bool(t.PrepareSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedRenameClientCapabilities)target;
            if (t.PrepareSupportDefaultBehavior == default) { return; }
            enc.WriteToken(JsonToken.String("prepareSupportDefaultBehavior"));
            enc.WriteToken(JsonToken.Number(((uint)t.PrepareSupportDefaultBehavior).ToString(CultureInfo.InvariantCulture)));
        },
        static (target, enc) =>
        {
            var t = (ResolvedRenameClientCapabilities)target;
            if (!t.HonorsChangeAnnotations) { return; }
            enc.WriteToken(JsonToken.String("honorsChangeAnnotations"));
            enc.WriteToken(JsonToken.Bool(t.HonorsChangeAnnotations));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientFoldingRangeKindOptions struct</c>（lsp_generated.go:16647）。
/// ResolvedClientFoldingRangeKindOptions is a resolved version of ClientFoldingRangeKindOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientFoldingRangeKindOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public FoldingRangeKind[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientFoldingRangeKindOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<FoldingRangeKind>(dec, static d => new FoldingRangeKind(d.Decode<string>() ?? "")); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientFoldingRangeKindOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientFoldingRangeOptions struct</c>（lsp_generated.go:16668）。
/// ResolvedClientFoldingRangeOptions is a resolved version of ClientFoldingRangeOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientFoldingRangeOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool CollapsedText = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("collapsedText", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientFoldingRangeOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CollapsedText = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientFoldingRangeOptions)target;
            if (!t.CollapsedText) { return; }
            enc.WriteToken(JsonToken.String("collapsedText"));
            enc.WriteToken(JsonToken.Bool(t.CollapsedText));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedFoldingRangeClientCapabilities struct</c>（lsp_generated.go:16687）。
/// ResolvedFoldingRangeClientCapabilities is a resolved version of FoldingRangeClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedFoldingRangeClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public uint RangeLimit;

    public bool LineFoldingOnly = false;

    public ResolvedClientFoldingRangeKindOptions FoldingRangeKind = new();

    public ResolvedClientFoldingRangeOptions FoldingRange = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFoldingRangeClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("rangeLimit", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFoldingRangeClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RangeLimit = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("lineFoldingOnly", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFoldingRangeClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LineFoldingOnly = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("foldingRangeKind", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFoldingRangeClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FoldingRangeKind.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("foldingRange", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedFoldingRangeClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FoldingRange.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedFoldingRangeClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFoldingRangeClientCapabilities)target;
            if (t.RangeLimit == 0) { return; }
            enc.WriteToken(JsonToken.String("rangeLimit"));
            enc.WriteToken(JsonToken.Number(t.RangeLimit.ToString(CultureInfo.InvariantCulture)));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFoldingRangeClientCapabilities)target;
            if (!t.LineFoldingOnly) { return; }
            enc.WriteToken(JsonToken.String("lineFoldingOnly"));
            enc.WriteToken(JsonToken.Bool(t.LineFoldingOnly));
        },
        static (target, enc) =>
        {
            var t = (ResolvedFoldingRangeClientCapabilities)target;
            if ((t.FoldingRangeKind.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("foldingRangeKind"));
            MarshalEncode(enc, t.FoldingRangeKind);
        },
        static (target, enc) =>
        {
            var t = (ResolvedFoldingRangeClientCapabilities)target;
            if ((!t.FoldingRange.CollapsedText)) { return; }
            enc.WriteToken(JsonToken.String("foldingRange"));
            MarshalEncode(enc, t.FoldingRange);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedSelectionRangeClientCapabilities struct</c>（lsp_generated.go:16726）。
/// ResolvedSelectionRangeClientCapabilities is a resolved version of SelectionRangeClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedSelectionRangeClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSelectionRangeClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedSelectionRangeClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientDiagnosticsTagOptions struct</c>（lsp_generated.go:16746）。
/// ResolvedClientDiagnosticsTagOptions is a resolved version of ClientDiagnosticsTagOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientDiagnosticsTagOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public DiagnosticTag[]? ValueSet;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("valueSet", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientDiagnosticsTagOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ValueSet = null; }
                else { t.ValueSet = DecodeArray<DiagnosticTag>(dec, static d => d.Decode<DiagnosticTag>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientDiagnosticsTagOptions)target;
            if (t.ValueSet is null) { return; }
            enc.WriteToken(JsonToken.String("valueSet"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.ValueSet)
            {
                enc.WriteToken(JsonToken.Number(((uint)item).ToString(CultureInfo.InvariantCulture)));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedPublishDiagnosticsClientCapabilities struct</c>（lsp_generated.go:16764）。
/// ResolvedPublishDiagnosticsClientCapabilities is a resolved version of PublishDiagnosticsClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// The publish diagnostic client capabilities.
/// </summary>
public sealed class ResolvedPublishDiagnosticsClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RelatedInformation = false;

    public ResolvedClientDiagnosticsTagOptions TagSupport = new();

    public bool CodeDescriptionSupport = false;

    public bool DataSupport = false;

    public bool VersionSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("relatedInformation", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RelatedInformation = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tagSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TagSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("codeDescriptionSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CodeDescriptionSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("dataSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DataSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("versionSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.VersionSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
            if (!t.RelatedInformation) { return; }
            enc.WriteToken(JsonToken.String("relatedInformation"));
            enc.WriteToken(JsonToken.Bool(t.RelatedInformation));
        },
        static (target, enc) =>
        {
            var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
            if ((t.TagSupport.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("tagSupport"));
            MarshalEncode(enc, t.TagSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
            if (!t.CodeDescriptionSupport) { return; }
            enc.WriteToken(JsonToken.String("codeDescriptionSupport"));
            enc.WriteToken(JsonToken.Bool(t.CodeDescriptionSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
            if (!t.DataSupport) { return; }
            enc.WriteToken(JsonToken.String("dataSupport"));
            enc.WriteToken(JsonToken.Bool(t.DataSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedPublishDiagnosticsClientCapabilities)target;
            if (!t.VersionSupport) { return; }
            enc.WriteToken(JsonToken.String("versionSupport"));
            enc.WriteToken(JsonToken.Bool(t.VersionSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedCallHierarchyClientCapabilities struct</c>（lsp_generated.go:16806）。
/// ResolvedCallHierarchyClientCapabilities is a resolved version of CallHierarchyClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedCallHierarchyClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedCallHierarchyClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedCallHierarchyClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientSemanticTokensRequestOptions struct</c>（lsp_generated.go:16826）。
/// ResolvedClientSemanticTokensRequestOptions is a resolved version of ClientSemanticTokensRequestOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientSemanticTokensRequestOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public BooleanOrEmptyObject Range = new();

    public BooleanOrClientSemanticTokensRequestFullDelta Full = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("range", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSemanticTokensRequestOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Range.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("full", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientSemanticTokensRequestOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Full.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientSemanticTokensRequestOptions)target;
            if ((t.Range.Boolean is null && t.Range.EmptyObject is null)) { return; }
            enc.WriteToken(JsonToken.String("range"));
            MarshalEncode(enc, t.Range);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientSemanticTokensRequestOptions)target;
            if ((t.Full.Boolean is null && t.Full.ClientSemanticTokensRequestFullDelta is null)) { return; }
            enc.WriteToken(JsonToken.String("full"));
            MarshalEncode(enc, t.Full);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedSemanticTokensClientCapabilities struct</c>（lsp_generated.go:16849）。
/// ResolvedSemanticTokensClientCapabilities is a resolved version of SemanticTokensClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedSemanticTokensClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
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

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("requests", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Requests.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tokenTypes", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.TokenTypes = null; }
                else { t.TokenTypes = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
        new StructFieldSpec("tokenModifiers", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.TokenModifiers = null; }
                else { t.TokenModifiers = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
        new StructFieldSpec("formats", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Formats = null; }
                else { t.Formats = DecodeArray<TokenFormat>(dec, static d => new TokenFormat(d.Decode<string>() ?? "")); }
            }
        ),
        new StructFieldSpec("overlappingTokenSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.OverlappingTokenSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("multilineTokenSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.MultilineTokenSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("serverCancelSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ServerCancelSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("augmentsSyntaxTokens", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedSemanticTokensClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.AugmentsSyntaxTokens = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (((t.Requests.Range.Boolean is null && t.Requests.Range.EmptyObject is null) && (t.Requests.Full.Boolean is null && t.Requests.Full.ClientSemanticTokensRequestFullDelta is null))) { return; }
            enc.WriteToken(JsonToken.String("requests"));
            MarshalEncode(enc, t.Requests);
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (t.TokenTypes is null) { return; }
            enc.WriteToken(JsonToken.String("tokenTypes"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.TokenTypes)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (t.TokenModifiers is null) { return; }
            enc.WriteToken(JsonToken.String("tokenModifiers"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.TokenModifiers)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (t.Formats is null) { return; }
            enc.WriteToken(JsonToken.String("formats"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.Formats)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (!t.OverlappingTokenSupport) { return; }
            enc.WriteToken(JsonToken.String("overlappingTokenSupport"));
            enc.WriteToken(JsonToken.Bool(t.OverlappingTokenSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (!t.MultilineTokenSupport) { return; }
            enc.WriteToken(JsonToken.String("multilineTokenSupport"));
            enc.WriteToken(JsonToken.Bool(t.MultilineTokenSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (!t.ServerCancelSupport) { return; }
            enc.WriteToken(JsonToken.String("serverCancelSupport"));
            enc.WriteToken(JsonToken.Bool(t.ServerCancelSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedSemanticTokensClientCapabilities)target;
            if (!t.AugmentsSyntaxTokens) { return; }
            enc.WriteToken(JsonToken.String("augmentsSyntaxTokens"));
            enc.WriteToken(JsonToken.Bool(t.AugmentsSyntaxTokens));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedLinkedEditingRangeClientCapabilities struct</c>（lsp_generated.go:16916）。
/// ResolvedLinkedEditingRangeClientCapabilities is a resolved version of LinkedEditingRangeClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for the linked editing range request.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedLinkedEditingRangeClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedLinkedEditingRangeClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedLinkedEditingRangeClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedMonikerClientCapabilities struct</c>（lsp_generated.go:16938）。
/// ResolvedMonikerClientCapabilities is a resolved version of MonikerClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to the moniker request.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedMonikerClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedMonikerClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedMonikerClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedTypeHierarchyClientCapabilities struct</c>（lsp_generated.go:16958）。
/// ResolvedTypeHierarchyClientCapabilities is a resolved version of TypeHierarchyClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedTypeHierarchyClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeHierarchyClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedTypeHierarchyClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedInlineValueClientCapabilities struct</c>（lsp_generated.go:16980）。
/// ResolvedInlineValueClientCapabilities is a resolved version of InlineValueClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to inline values.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlineValueClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedInlineValueClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedInlineValueClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientInlayHintResolveOptions struct</c>（lsp_generated.go:16998）。
/// ResolvedClientInlayHintResolveOptions is a resolved version of ClientInlayHintResolveOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientInlayHintResolveOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public string[]? Properties;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("properties", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientInlayHintResolveOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Properties = null; }
                else { t.Properties = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientInlayHintResolveOptions)target;
            if (t.Properties is null) { return; }
            enc.WriteToken(JsonToken.String("properties"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.Properties)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedInlayHintClientCapabilities struct</c>（lsp_generated.go:17018）。
/// ResolvedInlayHintClientCapabilities is a resolved version of InlayHintClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Inlay hint client capabilities.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedInlayHintClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    public ResolvedClientInlayHintResolveOptions ResolveSupport = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedInlayHintClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolveSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedInlayHintClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolveSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedInlayHintClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedInlayHintClientCapabilities)target;
            if ((t.ResolveSupport.Properties is null)) { return; }
            enc.WriteToken(JsonToken.String("resolveSupport"));
            MarshalEncode(enc, t.ResolveSupport);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedDiagnosticClientCapabilities struct</c>（lsp_generated.go:17042）。
/// ResolvedDiagnosticClientCapabilities is a resolved version of DiagnosticClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to diagnostic pull requests.
/// 
/// Since: 3.17.0
/// </summary>
public sealed class ResolvedDiagnosticClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool RelatedInformation = false;

    public ResolvedClientDiagnosticsTagOptions TagSupport = new();

    public bool CodeDescriptionSupport = false;

    public bool DataSupport = false;

    public bool DynamicRegistration = false;

    public bool RelatedDocumentSupport = false;

    public bool MarkupMessageSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("relatedInformation", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RelatedInformation = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("tagSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TagSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("codeDescriptionSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CodeDescriptionSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("dataSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DataSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("relatedDocumentSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RelatedDocumentSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("markupMessageSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedDiagnosticClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.MarkupMessageSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticClientCapabilities)target;
            if (!t.RelatedInformation) { return; }
            enc.WriteToken(JsonToken.String("relatedInformation"));
            enc.WriteToken(JsonToken.Bool(t.RelatedInformation));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticClientCapabilities)target;
            if ((t.TagSupport.ValueSet is null)) { return; }
            enc.WriteToken(JsonToken.String("tagSupport"));
            MarshalEncode(enc, t.TagSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticClientCapabilities)target;
            if (!t.CodeDescriptionSupport) { return; }
            enc.WriteToken(JsonToken.String("codeDescriptionSupport"));
            enc.WriteToken(JsonToken.Bool(t.CodeDescriptionSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticClientCapabilities)target;
            if (!t.DataSupport) { return; }
            enc.WriteToken(JsonToken.String("dataSupport"));
            enc.WriteToken(JsonToken.Bool(t.DataSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticClientCapabilities)target;
            if (!t.RelatedDocumentSupport) { return; }
            enc.WriteToken(JsonToken.String("relatedDocumentSupport"));
            enc.WriteToken(JsonToken.Bool(t.RelatedDocumentSupport));
        },
        static (target, enc) =>
        {
            var t = (ResolvedDiagnosticClientCapabilities)target;
            if (!t.MarkupMessageSupport) { return; }
            enc.WriteToken(JsonToken.String("markupMessageSupport"));
            enc.WriteToken(JsonToken.Bool(t.MarkupMessageSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedInlineCompletionClientCapabilities struct</c>（lsp_generated.go:17093）。
/// ResolvedInlineCompletionClientCapabilities is a resolved version of InlineCompletionClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to inline completions.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedInlineCompletionClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool DynamicRegistration = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dynamicRegistration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedInlineCompletionClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DynamicRegistration = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedInlineCompletionClientCapabilities)target;
            if (!t.DynamicRegistration) { return; }
            enc.WriteToken(JsonToken.String("dynamicRegistration"));
            enc.WriteToken(JsonToken.Bool(t.DynamicRegistration));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedTextDocumentClientCapabilities struct</c>（lsp_generated.go:17111）。
/// ResolvedTextDocumentClientCapabilities is a resolved version of TextDocumentClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Text document specific client capabilities.
/// </summary>
public sealed class ResolvedTextDocumentClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
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

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("synchronization", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Synchronization.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("filters", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Filters.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("completion", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Completion.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("hover", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Hover.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("signatureHelp", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SignatureHelp.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("declaration", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Declaration.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("definition", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Definition.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("typeDefinition", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TypeDefinition.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("implementation", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Implementation.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("references", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.References.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("documentHighlight", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DocumentHighlight.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("documentSymbol", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DocumentSymbol.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("codeAction", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CodeAction.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("codeLens", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CodeLens.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("documentLink", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.DocumentLink.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("colorProvider", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ColorProvider.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("formatting", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Formatting.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("rangeFormatting", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RangeFormatting.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("onTypeFormatting", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.OnTypeFormatting.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("rename", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Rename.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("foldingRange", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FoldingRange.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("selectionRange", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SelectionRange.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("publishDiagnostics", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PublishDiagnostics.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("callHierarchy", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CallHierarchy.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("semanticTokens", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SemanticTokens.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("linkedEditingRange", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.LinkedEditingRange.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("moniker", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Moniker.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("typeHierarchy", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TypeHierarchy.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("inlineValue", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InlineValue.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("inlayHint", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InlayHint.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("diagnostic", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Diagnostic.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("inlineCompletion", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTextDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InlineCompletion.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Synchronization.DynamicRegistration && !t.Synchronization.WillSave && !t.Synchronization.WillSaveWaitUntil && !t.Synchronization.DidSave)) { return; }
            enc.WriteToken(JsonToken.String("synchronization"));
            MarshalEncode(enc, t.Synchronization);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Filters.RelativePatternSupport)) { return; }
            enc.WriteToken(JsonToken.String("filters"));
            MarshalEncode(enc, t.Filters);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Completion.DynamicRegistration && (!t.Completion.CompletionItem.SnippetSupport && !t.Completion.CompletionItem.CommitCharactersSupport && t.Completion.CompletionItem.DocumentationFormat is null && !t.Completion.CompletionItem.DeprecatedSupport && !t.Completion.CompletionItem.PreselectSupport && (t.Completion.CompletionItem.TagSupport.ValueSet is null) && !t.Completion.CompletionItem.InsertReplaceSupport && (t.Completion.CompletionItem.ResolveSupport.Properties is null) && (t.Completion.CompletionItem.InsertTextModeSupport.ValueSet is null) && !t.Completion.CompletionItem.LabelDetailsSupport) && (t.Completion.CompletionItemKind.ValueSet is null) && t.Completion.InsertTextMode == default && !t.Completion.ContextSupport && (t.Completion.CompletionList.ItemDefaults is null && !t.Completion.CompletionList.ApplyKindSupport))) { return; }
            enc.WriteToken(JsonToken.String("completion"));
            MarshalEncode(enc, t.Completion);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Hover.DynamicRegistration && t.Hover.ContentFormat is null)) { return; }
            enc.WriteToken(JsonToken.String("hover"));
            MarshalEncode(enc, t.Hover);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.SignatureHelp.DynamicRegistration && (t.SignatureHelp.SignatureInformation.DocumentationFormat is null && (!t.SignatureHelp.SignatureInformation.ParameterInformation.LabelOffsetSupport) && !t.SignatureHelp.SignatureInformation.ActiveParameterSupport && !t.SignatureHelp.SignatureInformation.NoActiveParameterSupport) && !t.SignatureHelp.ContextSupport)) { return; }
            enc.WriteToken(JsonToken.String("signatureHelp"));
            MarshalEncode(enc, t.SignatureHelp);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Declaration.DynamicRegistration && !t.Declaration.LinkSupport)) { return; }
            enc.WriteToken(JsonToken.String("declaration"));
            MarshalEncode(enc, t.Declaration);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Definition.DynamicRegistration && !t.Definition.LinkSupport)) { return; }
            enc.WriteToken(JsonToken.String("definition"));
            MarshalEncode(enc, t.Definition);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.TypeDefinition.DynamicRegistration && !t.TypeDefinition.LinkSupport)) { return; }
            enc.WriteToken(JsonToken.String("typeDefinition"));
            MarshalEncode(enc, t.TypeDefinition);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Implementation.DynamicRegistration && !t.Implementation.LinkSupport)) { return; }
            enc.WriteToken(JsonToken.String("implementation"));
            MarshalEncode(enc, t.Implementation);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.References.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("references"));
            MarshalEncode(enc, t.References);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.DocumentHighlight.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("documentHighlight"));
            MarshalEncode(enc, t.DocumentHighlight);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.DocumentSymbol.DynamicRegistration && (t.DocumentSymbol.SymbolKind.ValueSet is null) && !t.DocumentSymbol.HierarchicalDocumentSymbolSupport && (t.DocumentSymbol.TagSupport.ValueSet is null) && !t.DocumentSymbol.LabelSupport)) { return; }
            enc.WriteToken(JsonToken.String("documentSymbol"));
            MarshalEncode(enc, t.DocumentSymbol);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.CodeAction.DynamicRegistration && ((t.CodeAction.CodeActionLiteralSupport.CodeActionKind.ValueSet is null)) && !t.CodeAction.IsPreferredSupport && !t.CodeAction.DisabledSupport && !t.CodeAction.DataSupport && (t.CodeAction.ResolveSupport.Properties is null) && !t.CodeAction.HonorsChangeAnnotations && !t.CodeAction.DocumentationSupport && (t.CodeAction.TagSupport.ValueSet is null))) { return; }
            enc.WriteToken(JsonToken.String("codeAction"));
            MarshalEncode(enc, t.CodeAction);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.CodeLens.DynamicRegistration && (t.CodeLens.ResolveSupport.Properties is null))) { return; }
            enc.WriteToken(JsonToken.String("codeLens"));
            MarshalEncode(enc, t.CodeLens);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.DocumentLink.DynamicRegistration && !t.DocumentLink.TooltipSupport)) { return; }
            enc.WriteToken(JsonToken.String("documentLink"));
            MarshalEncode(enc, t.DocumentLink);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.ColorProvider.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("colorProvider"));
            MarshalEncode(enc, t.ColorProvider);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Formatting.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("formatting"));
            MarshalEncode(enc, t.Formatting);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.RangeFormatting.DynamicRegistration && !t.RangeFormatting.RangesSupport)) { return; }
            enc.WriteToken(JsonToken.String("rangeFormatting"));
            MarshalEncode(enc, t.RangeFormatting);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.OnTypeFormatting.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("onTypeFormatting"));
            MarshalEncode(enc, t.OnTypeFormatting);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Rename.DynamicRegistration && !t.Rename.PrepareSupport && t.Rename.PrepareSupportDefaultBehavior == default && !t.Rename.HonorsChangeAnnotations)) { return; }
            enc.WriteToken(JsonToken.String("rename"));
            MarshalEncode(enc, t.Rename);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.FoldingRange.DynamicRegistration && t.FoldingRange.RangeLimit == 0 && !t.FoldingRange.LineFoldingOnly && (t.FoldingRange.FoldingRangeKind.ValueSet is null) && (!t.FoldingRange.FoldingRange.CollapsedText))) { return; }
            enc.WriteToken(JsonToken.String("foldingRange"));
            MarshalEncode(enc, t.FoldingRange);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.SelectionRange.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("selectionRange"));
            MarshalEncode(enc, t.SelectionRange);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.PublishDiagnostics.RelatedInformation && (t.PublishDiagnostics.TagSupport.ValueSet is null) && !t.PublishDiagnostics.CodeDescriptionSupport && !t.PublishDiagnostics.DataSupport && !t.PublishDiagnostics.VersionSupport)) { return; }
            enc.WriteToken(JsonToken.String("publishDiagnostics"));
            MarshalEncode(enc, t.PublishDiagnostics);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.CallHierarchy.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("callHierarchy"));
            MarshalEncode(enc, t.CallHierarchy);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.SemanticTokens.DynamicRegistration && ((t.SemanticTokens.Requests.Range.Boolean is null && t.SemanticTokens.Requests.Range.EmptyObject is null) && (t.SemanticTokens.Requests.Full.Boolean is null && t.SemanticTokens.Requests.Full.ClientSemanticTokensRequestFullDelta is null)) && t.SemanticTokens.TokenTypes is null && t.SemanticTokens.TokenModifiers is null && t.SemanticTokens.Formats is null && !t.SemanticTokens.OverlappingTokenSupport && !t.SemanticTokens.MultilineTokenSupport && !t.SemanticTokens.ServerCancelSupport && !t.SemanticTokens.AugmentsSyntaxTokens)) { return; }
            enc.WriteToken(JsonToken.String("semanticTokens"));
            MarshalEncode(enc, t.SemanticTokens);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.LinkedEditingRange.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("linkedEditingRange"));
            MarshalEncode(enc, t.LinkedEditingRange);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Moniker.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("moniker"));
            MarshalEncode(enc, t.Moniker);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.TypeHierarchy.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("typeHierarchy"));
            MarshalEncode(enc, t.TypeHierarchy);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.InlineValue.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("inlineValue"));
            MarshalEncode(enc, t.InlineValue);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.InlayHint.DynamicRegistration && (t.InlayHint.ResolveSupport.Properties is null))) { return; }
            enc.WriteToken(JsonToken.String("inlayHint"));
            MarshalEncode(enc, t.InlayHint);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.Diagnostic.RelatedInformation && (t.Diagnostic.TagSupport.ValueSet is null) && !t.Diagnostic.CodeDescriptionSupport && !t.Diagnostic.DataSupport && !t.Diagnostic.DynamicRegistration && !t.Diagnostic.RelatedDocumentSupport && !t.Diagnostic.MarkupMessageSupport)) { return; }
            enc.WriteToken(JsonToken.String("diagnostic"));
            MarshalEncode(enc, t.Diagnostic);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTextDocumentClientCapabilities)target;
            if ((!t.InlineCompletion.DynamicRegistration)) { return; }
            enc.WriteToken(JsonToken.String("inlineCompletion"));
            MarshalEncode(enc, t.InlineCompletion);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientShowMessageActionItemOptions struct</c>（lsp_generated.go:17255）。
/// ResolvedClientShowMessageActionItemOptions is a resolved version of ClientShowMessageActionItemOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedClientShowMessageActionItemOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool AdditionalPropertiesSupport = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("additionalPropertiesSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientShowMessageActionItemOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.AdditionalPropertiesSupport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientShowMessageActionItemOptions)target;
            if (!t.AdditionalPropertiesSupport) { return; }
            enc.WriteToken(JsonToken.String("additionalPropertiesSupport"));
            enc.WriteToken(JsonToken.Bool(t.AdditionalPropertiesSupport));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedShowMessageRequestClientCapabilities struct</c>（lsp_generated.go:17275）。
/// ResolvedShowMessageRequestClientCapabilities is a resolved version of ShowMessageRequestClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Show message request client capabilities
/// </summary>
public sealed class ResolvedShowMessageRequestClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public ResolvedClientShowMessageActionItemOptions MessageActionItem = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("messageActionItem", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedShowMessageRequestClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.MessageActionItem.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedShowMessageRequestClientCapabilities)target;
            if ((!t.MessageActionItem.AdditionalPropertiesSupport)) { return; }
            enc.WriteToken(JsonToken.String("messageActionItem"));
            MarshalEncode(enc, t.MessageActionItem);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedShowDocumentClientCapabilities struct</c>（lsp_generated.go:17295）。
/// ResolvedShowDocumentClientCapabilities is a resolved version of ShowDocumentClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities for the showDocument request.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedShowDocumentClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool Support = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("support", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedShowDocumentClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Support = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedShowDocumentClientCapabilities)target;
            if (!t.Support) { return; }
            enc.WriteToken(JsonToken.String("support"));
            enc.WriteToken(JsonToken.Bool(t.Support));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedWindowClientCapabilities struct</c>（lsp_generated.go:17312）。
/// ResolvedWindowClientCapabilities is a resolved version of WindowClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// </summary>
public sealed class ResolvedWindowClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool WorkDoneProgress = false;

    public ResolvedShowMessageRequestClientCapabilities ShowMessage = new();

    public ResolvedShowDocumentClientCapabilities ShowDocument = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("workDoneProgress", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWindowClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.WorkDoneProgress = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("showMessage", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWindowClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ShowMessage.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("showDocument", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedWindowClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ShowDocument.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedWindowClientCapabilities)target;
            if (!t.WorkDoneProgress) { return; }
            enc.WriteToken(JsonToken.String("workDoneProgress"));
            enc.WriteToken(JsonToken.Bool(t.WorkDoneProgress));
        },
        static (target, enc) =>
        {
            var t = (ResolvedWindowClientCapabilities)target;
            if (((!t.ShowMessage.MessageActionItem.AdditionalPropertiesSupport))) { return; }
            enc.WriteToken(JsonToken.String("showMessage"));
            MarshalEncode(enc, t.ShowMessage);
        },
        static (target, enc) =>
        {
            var t = (ResolvedWindowClientCapabilities)target;
            if ((!t.ShowDocument.Support)) { return; }
            enc.WriteToken(JsonToken.String("showDocument"));
            MarshalEncode(enc, t.ShowDocument);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedStaleRequestSupportOptions struct</c>（lsp_generated.go:17348）。
/// ResolvedStaleRequestSupportOptions is a resolved version of StaleRequestSupportOptions with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Since: 3.18.0
/// </summary>
public sealed class ResolvedStaleRequestSupportOptions : IJsonMarshaler, IJsonUnmarshaler
{
    public bool Cancel = false;

    public string[]? RetryOnContentModified;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("cancel", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedStaleRequestSupportOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Cancel = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("retryOnContentModified", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedStaleRequestSupportOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RetryOnContentModified = null; }
                else { t.RetryOnContentModified = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedStaleRequestSupportOptions)target;
            if (!t.Cancel) { return; }
            enc.WriteToken(JsonToken.String("cancel"));
            enc.WriteToken(JsonToken.Bool(t.Cancel));
        },
        static (target, enc) =>
        {
            var t = (ResolvedStaleRequestSupportOptions)target;
            if (t.RetryOnContentModified is null) { return; }
            enc.WriteToken(JsonToken.String("retryOnContentModified"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.RetryOnContentModified)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedRegularExpressionsClientCapabilities struct</c>（lsp_generated.go:17373）。
/// ResolvedRegularExpressionsClientCapabilities is a resolved version of RegularExpressionsClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to regular expressions.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedRegularExpressionsClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public string Engine = "";

    public string Version = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("engine", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedRegularExpressionsClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Engine = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("version", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedRegularExpressionsClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Version = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedRegularExpressionsClientCapabilities)target;
            if (t.Engine.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("engine"));
            enc.WriteToken(JsonToken.String(t.Engine));
        },
        static (target, enc) =>
        {
            var t = (ResolvedRegularExpressionsClientCapabilities)target;
            if (t.Version.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("version"));
            enc.WriteToken(JsonToken.String(t.Version));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedMarkdownClientCapabilities struct</c>（lsp_generated.go:17396）。
/// ResolvedMarkdownClientCapabilities is a resolved version of MarkdownClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// Client capabilities specific to the used markdown parser.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedMarkdownClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public string Parser = "";

    public string Version = "";

    public string[]? AllowedTags;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("parser", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedMarkdownClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Parser = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("version", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedMarkdownClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Version = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("allowedTags", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedMarkdownClientCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.AllowedTags = null; }
                else { t.AllowedTags = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedMarkdownClientCapabilities)target;
            if (t.Parser.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("parser"));
            enc.WriteToken(JsonToken.String(t.Parser));
        },
        static (target, enc) =>
        {
            var t = (ResolvedMarkdownClientCapabilities)target;
            if (t.Version.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("version"));
            enc.WriteToken(JsonToken.String(t.Version));
        },
        static (target, enc) =>
        {
            var t = (ResolvedMarkdownClientCapabilities)target;
            if (t.AllowedTags is null) { return; }
            enc.WriteToken(JsonToken.String("allowedTags"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.AllowedTags)
            {
                enc.WriteToken(JsonToken.String(item));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedGeneralClientCapabilities struct</c>（lsp_generated.go:17425）。
/// ResolvedGeneralClientCapabilities is a resolved version of GeneralClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// General client capabilities.
/// 
/// Since: 3.16.0
/// </summary>
public sealed class ResolvedGeneralClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public ResolvedStaleRequestSupportOptions StaleRequestSupport = new();

    public ResolvedRegularExpressionsClientCapabilities RegularExpressions = new();

    public ResolvedMarkdownClientCapabilities Markdown = new();

    public PositionEncodingKind[]? PositionEncodings;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("staleRequestSupport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedGeneralClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.StaleRequestSupport.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("regularExpressions", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedGeneralClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RegularExpressions.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("markdown", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedGeneralClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Markdown.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("positionEncodings", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedGeneralClientCapabilities)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.PositionEncodings = null; }
                else { t.PositionEncodings = DecodeArray<PositionEncodingKind>(dec, static d => new PositionEncodingKind(d.Decode<string>() ?? "")); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedGeneralClientCapabilities)target;
            if ((!t.StaleRequestSupport.Cancel && t.StaleRequestSupport.RetryOnContentModified is null)) { return; }
            enc.WriteToken(JsonToken.String("staleRequestSupport"));
            MarshalEncode(enc, t.StaleRequestSupport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedGeneralClientCapabilities)target;
            if ((t.RegularExpressions.Engine.Length == 0 && t.RegularExpressions.Version.Length == 0)) { return; }
            enc.WriteToken(JsonToken.String("regularExpressions"));
            MarshalEncode(enc, t.RegularExpressions);
        },
        static (target, enc) =>
        {
            var t = (ResolvedGeneralClientCapabilities)target;
            if ((t.Markdown.Parser.Length == 0 && t.Markdown.Version.Length == 0 && t.Markdown.AllowedTags is null)) { return; }
            enc.WriteToken(JsonToken.String("markdown"));
            MarshalEncode(enc, t.Markdown);
        },
        static (target, enc) =>
        {
            var t = (ResolvedGeneralClientCapabilities)target;
            if (t.PositionEncodings is null) { return; }
            enc.WriteToken(JsonToken.String("positionEncodings"));
            enc.WriteToken(BeginArray);
            foreach (var item in t.PositionEncodings)
            {
                enc.WriteToken(JsonToken.String(item.Value));
            }
            enc.WriteToken(EndArray);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedExperimentalClientCapabilities struct</c>（lsp_generated.go:17478）。
/// ResolvedExperimentalClientCapabilities is a resolved version of ExperimentalClientCapabilities with all optional fields
/// converted to non-pointer values for easier access.
/// 
/// ExperimentalClientCapabilities contains experimental capabilities under development.
/// </summary>
public sealed class ResolvedExperimentalClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
{
    public bool HoverVerbosityLevel = false;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("hoverVerbosityLevel", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedExperimentalClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.HoverVerbosityLevel = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedExperimentalClientCapabilities)target;
            if (!t.HoverVerbosityLevel) { return; }
            enc.WriteToken(JsonToken.String("hoverVerbosityLevel"));
            enc.WriteToken(JsonToken.Bool(t.HoverVerbosityLevel));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedClientCapabilities struct</c>（lsp_generated.go:17497）。
/// ResolvedClientCapabilities is a version of ClientCapabilities where all nested
/// fields are values (not pointers), making it easier to access deeply nested capabilities.
/// Use (*ClientCapabilities).Resolve() to convert from ClientCapabilities.
/// 
/// Defines the capabilities provided by the client.
/// </summary>
public sealed class ResolvedClientCapabilities : IJsonMarshaler, IJsonUnmarshaler
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

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("workspace", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Workspace.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("textDocument", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TextDocument.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("window", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Window.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("general", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.General.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("experimental", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Experimental.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("_vs_supportsVisualStudioExtensions", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.VSSupportsVisualStudioExtensions = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("_vs_supportedSnippetVersion", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.VSSupportedSnippetVersion = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("_vs_supportsNotIncludingTextInTextDocumentDidOpen", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.VSSupportsNotIncludingTextInTextDocumentDidOpen = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("_vs_supportsIconExtensions", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.VSSupportsIconExtensions = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("_vs_supportsDiagnosticRequests", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedClientCapabilities)target;
                if (dec.PeekKind() != JsonKind.Null) { t.VSSupportsDiagnosticRequests = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if ((!t.Workspace.ApplyEdit && (!t.Workspace.WorkspaceEdit.DocumentChanges && t.Workspace.WorkspaceEdit.ResourceOperations is null && t.Workspace.WorkspaceEdit.FailureHandling.Value.Length == 0 && !t.Workspace.WorkspaceEdit.NormalizesLineEndings && (!t.Workspace.WorkspaceEdit.ChangeAnnotationSupport.GroupsOnLabel) && !t.Workspace.WorkspaceEdit.MetadataSupport && !t.Workspace.WorkspaceEdit.SnippetEditSupport) && (!t.Workspace.DidChangeConfiguration.DynamicRegistration) && (!t.Workspace.DidChangeWatchedFiles.DynamicRegistration && !t.Workspace.DidChangeWatchedFiles.RelativePatternSupport) && (!t.Workspace.Symbol.DynamicRegistration && (t.Workspace.Symbol.SymbolKind.ValueSet is null) && (t.Workspace.Symbol.TagSupport.ValueSet is null) && (t.Workspace.Symbol.ResolveSupport.Properties is null)) && (!t.Workspace.ExecuteCommand.DynamicRegistration) && !t.Workspace.WorkspaceFolders && !t.Workspace.Configuration && (!t.Workspace.SemanticTokens.RefreshSupport) && (!t.Workspace.CodeLens.RefreshSupport) && (!t.Workspace.FileOperations.DynamicRegistration && !t.Workspace.FileOperations.DidCreate && !t.Workspace.FileOperations.WillCreate && !t.Workspace.FileOperations.DidRename && !t.Workspace.FileOperations.WillRename && !t.Workspace.FileOperations.DidDelete && !t.Workspace.FileOperations.WillDelete) && (!t.Workspace.InlineValue.RefreshSupport) && (!t.Workspace.InlayHint.RefreshSupport) && (!t.Workspace.Diagnostics.RefreshSupport) && (!t.Workspace.FoldingRange.RefreshSupport) && (!t.Workspace.TextDocumentContent.DynamicRegistration))) { return; }
            enc.WriteToken(JsonToken.String("workspace"));
            MarshalEncode(enc, t.Workspace);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if (((!t.TextDocument.Synchronization.DynamicRegistration && !t.TextDocument.Synchronization.WillSave && !t.TextDocument.Synchronization.WillSaveWaitUntil && !t.TextDocument.Synchronization.DidSave) && (!t.TextDocument.Filters.RelativePatternSupport) && (!t.TextDocument.Completion.DynamicRegistration && (!t.TextDocument.Completion.CompletionItem.SnippetSupport && !t.TextDocument.Completion.CompletionItem.CommitCharactersSupport && t.TextDocument.Completion.CompletionItem.DocumentationFormat is null && !t.TextDocument.Completion.CompletionItem.DeprecatedSupport && !t.TextDocument.Completion.CompletionItem.PreselectSupport && (t.TextDocument.Completion.CompletionItem.TagSupport.ValueSet is null) && !t.TextDocument.Completion.CompletionItem.InsertReplaceSupport && (t.TextDocument.Completion.CompletionItem.ResolveSupport.Properties is null) && (t.TextDocument.Completion.CompletionItem.InsertTextModeSupport.ValueSet is null) && !t.TextDocument.Completion.CompletionItem.LabelDetailsSupport) && (t.TextDocument.Completion.CompletionItemKind.ValueSet is null) && t.TextDocument.Completion.InsertTextMode == default && !t.TextDocument.Completion.ContextSupport && (t.TextDocument.Completion.CompletionList.ItemDefaults is null && !t.TextDocument.Completion.CompletionList.ApplyKindSupport)) && (!t.TextDocument.Hover.DynamicRegistration && t.TextDocument.Hover.ContentFormat is null) && (!t.TextDocument.SignatureHelp.DynamicRegistration && (t.TextDocument.SignatureHelp.SignatureInformation.DocumentationFormat is null && (!t.TextDocument.SignatureHelp.SignatureInformation.ParameterInformation.LabelOffsetSupport) && !t.TextDocument.SignatureHelp.SignatureInformation.ActiveParameterSupport && !t.TextDocument.SignatureHelp.SignatureInformation.NoActiveParameterSupport) && !t.TextDocument.SignatureHelp.ContextSupport) && (!t.TextDocument.Declaration.DynamicRegistration && !t.TextDocument.Declaration.LinkSupport) && (!t.TextDocument.Definition.DynamicRegistration && !t.TextDocument.Definition.LinkSupport) && (!t.TextDocument.TypeDefinition.DynamicRegistration && !t.TextDocument.TypeDefinition.LinkSupport) && (!t.TextDocument.Implementation.DynamicRegistration && !t.TextDocument.Implementation.LinkSupport) && (!t.TextDocument.References.DynamicRegistration) && (!t.TextDocument.DocumentHighlight.DynamicRegistration) && (!t.TextDocument.DocumentSymbol.DynamicRegistration && (t.TextDocument.DocumentSymbol.SymbolKind.ValueSet is null) && !t.TextDocument.DocumentSymbol.HierarchicalDocumentSymbolSupport && (t.TextDocument.DocumentSymbol.TagSupport.ValueSet is null) && !t.TextDocument.DocumentSymbol.LabelSupport) && (!t.TextDocument.CodeAction.DynamicRegistration && ((t.TextDocument.CodeAction.CodeActionLiteralSupport.CodeActionKind.ValueSet is null)) && !t.TextDocument.CodeAction.IsPreferredSupport && !t.TextDocument.CodeAction.DisabledSupport && !t.TextDocument.CodeAction.DataSupport && (t.TextDocument.CodeAction.ResolveSupport.Properties is null) && !t.TextDocument.CodeAction.HonorsChangeAnnotations && !t.TextDocument.CodeAction.DocumentationSupport && (t.TextDocument.CodeAction.TagSupport.ValueSet is null)) && (!t.TextDocument.CodeLens.DynamicRegistration && (t.TextDocument.CodeLens.ResolveSupport.Properties is null)) && (!t.TextDocument.DocumentLink.DynamicRegistration && !t.TextDocument.DocumentLink.TooltipSupport) && (!t.TextDocument.ColorProvider.DynamicRegistration) && (!t.TextDocument.Formatting.DynamicRegistration) && (!t.TextDocument.RangeFormatting.DynamicRegistration && !t.TextDocument.RangeFormatting.RangesSupport) && (!t.TextDocument.OnTypeFormatting.DynamicRegistration) && (!t.TextDocument.Rename.DynamicRegistration && !t.TextDocument.Rename.PrepareSupport && t.TextDocument.Rename.PrepareSupportDefaultBehavior == default && !t.TextDocument.Rename.HonorsChangeAnnotations) && (!t.TextDocument.FoldingRange.DynamicRegistration && t.TextDocument.FoldingRange.RangeLimit == 0 && !t.TextDocument.FoldingRange.LineFoldingOnly && (t.TextDocument.FoldingRange.FoldingRangeKind.ValueSet is null) && (!t.TextDocument.FoldingRange.FoldingRange.CollapsedText)) && (!t.TextDocument.SelectionRange.DynamicRegistration) && (!t.TextDocument.PublishDiagnostics.RelatedInformation && (t.TextDocument.PublishDiagnostics.TagSupport.ValueSet is null) && !t.TextDocument.PublishDiagnostics.CodeDescriptionSupport && !t.TextDocument.PublishDiagnostics.DataSupport && !t.TextDocument.PublishDiagnostics.VersionSupport) && (!t.TextDocument.CallHierarchy.DynamicRegistration) && (!t.TextDocument.SemanticTokens.DynamicRegistration && ((t.TextDocument.SemanticTokens.Requests.Range.Boolean is null && t.TextDocument.SemanticTokens.Requests.Range.EmptyObject is null) && (t.TextDocument.SemanticTokens.Requests.Full.Boolean is null && t.TextDocument.SemanticTokens.Requests.Full.ClientSemanticTokensRequestFullDelta is null)) && t.TextDocument.SemanticTokens.TokenTypes is null && t.TextDocument.SemanticTokens.TokenModifiers is null && t.TextDocument.SemanticTokens.Formats is null && !t.TextDocument.SemanticTokens.OverlappingTokenSupport && !t.TextDocument.SemanticTokens.MultilineTokenSupport && !t.TextDocument.SemanticTokens.ServerCancelSupport && !t.TextDocument.SemanticTokens.AugmentsSyntaxTokens) && (!t.TextDocument.LinkedEditingRange.DynamicRegistration) && (!t.TextDocument.Moniker.DynamicRegistration) && (!t.TextDocument.TypeHierarchy.DynamicRegistration) && (!t.TextDocument.InlineValue.DynamicRegistration) && (!t.TextDocument.InlayHint.DynamicRegistration && (t.TextDocument.InlayHint.ResolveSupport.Properties is null)) && (!t.TextDocument.Diagnostic.RelatedInformation && (t.TextDocument.Diagnostic.TagSupport.ValueSet is null) && !t.TextDocument.Diagnostic.CodeDescriptionSupport && !t.TextDocument.Diagnostic.DataSupport && !t.TextDocument.Diagnostic.DynamicRegistration && !t.TextDocument.Diagnostic.RelatedDocumentSupport && !t.TextDocument.Diagnostic.MarkupMessageSupport) && (!t.TextDocument.InlineCompletion.DynamicRegistration))) { return; }
            enc.WriteToken(JsonToken.String("textDocument"));
            MarshalEncode(enc, t.TextDocument);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if ((!t.Window.WorkDoneProgress && ((!t.Window.ShowMessage.MessageActionItem.AdditionalPropertiesSupport)) && (!t.Window.ShowDocument.Support))) { return; }
            enc.WriteToken(JsonToken.String("window"));
            MarshalEncode(enc, t.Window);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if (((!t.General.StaleRequestSupport.Cancel && t.General.StaleRequestSupport.RetryOnContentModified is null) && (t.General.RegularExpressions.Engine.Length == 0 && t.General.RegularExpressions.Version.Length == 0) && (t.General.Markdown.Parser.Length == 0 && t.General.Markdown.Version.Length == 0 && t.General.Markdown.AllowedTags is null) && t.General.PositionEncodings is null)) { return; }
            enc.WriteToken(JsonToken.String("general"));
            MarshalEncode(enc, t.General);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if ((!t.Experimental.HoverVerbosityLevel)) { return; }
            enc.WriteToken(JsonToken.String("experimental"));
            MarshalEncode(enc, t.Experimental);
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if (!t.VSSupportsVisualStudioExtensions) { return; }
            enc.WriteToken(JsonToken.String("_vs_supportsVisualStudioExtensions"));
            enc.WriteToken(JsonToken.Bool(t.VSSupportsVisualStudioExtensions));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if (t.VSSupportedSnippetVersion == 0) { return; }
            enc.WriteToken(JsonToken.String("_vs_supportedSnippetVersion"));
            enc.WriteToken(JsonToken.Number(t.VSSupportedSnippetVersion.ToString(CultureInfo.InvariantCulture)));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if (!t.VSSupportsNotIncludingTextInTextDocumentDidOpen) { return; }
            enc.WriteToken(JsonToken.String("_vs_supportsNotIncludingTextInTextDocumentDidOpen"));
            enc.WriteToken(JsonToken.Bool(t.VSSupportsNotIncludingTextInTextDocumentDidOpen));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if (!t.VSSupportsIconExtensions) { return; }
            enc.WriteToken(JsonToken.String("_vs_supportsIconExtensions"));
            enc.WriteToken(JsonToken.Bool(t.VSSupportsIconExtensions));
        },
        static (target, enc) =>
        {
            var t = (ResolvedClientCapabilities)target;
            if (!t.VSSupportsDiagnosticRequests) { return; }
            enc.WriteToken(JsonToken.String("_vs_supportsDiagnosticRequests"));
            enc.WriteToken(JsonToken.Bool(t.VSSupportsDiagnosticRequests));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}
