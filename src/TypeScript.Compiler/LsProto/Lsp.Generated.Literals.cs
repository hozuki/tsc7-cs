// generated from: internal/lsp/lsproto/lsp_generated.go（Literal types：行 14804-15223）
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

/// <summary>Go: <c>type StringLiteralBegin struct</c>（lsp_generated.go:14807）。
/// StringLiteralBegin is a literal type for "begin"
/// </summary>
public sealed class StringLiteralBegin : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralBegin) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14811）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"begin\"");

    /// <summary>Go: <c>(o *StringLiteralBegin) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14817）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"begin\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralBegin", "\"begin\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralReport struct</c>（lsp_generated.go:14829）。
/// StringLiteralReport is a literal type for "report"
/// </summary>
public sealed class StringLiteralReport : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralReport) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14833）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"report\"");

    /// <summary>Go: <c>(o *StringLiteralReport) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14839）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"report\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralReport", "\"report\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralEnd struct</c>（lsp_generated.go:14851）。
/// StringLiteralEnd is a literal type for "end"
/// </summary>
public sealed class StringLiteralEnd : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralEnd) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14855）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"end\"");

    /// <summary>Go: <c>(o *StringLiteralEnd) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14861）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"end\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralEnd", "\"end\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralCreate struct</c>（lsp_generated.go:14873）。
/// StringLiteralCreate is a literal type for "create"
/// </summary>
public sealed class StringLiteralCreate : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralCreate) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14877）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"create\"");

    /// <summary>Go: <c>(o *StringLiteralCreate) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14883）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"create\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralCreate", "\"create\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralRename struct</c>（lsp_generated.go:14895）。
/// StringLiteralRename is a literal type for "rename"
/// </summary>
public sealed class StringLiteralRename : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralRename) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14899）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"rename\"");

    /// <summary>Go: <c>(o *StringLiteralRename) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14905）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"rename\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralRename", "\"rename\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralDelete struct</c>（lsp_generated.go:14917）。
/// StringLiteralDelete is a literal type for "delete"
/// </summary>
public sealed class StringLiteralDelete : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralDelete) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14921）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"delete\"");

    /// <summary>Go: <c>(o *StringLiteralDelete) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14927）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"delete\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralDelete", "\"delete\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralFull struct</c>（lsp_generated.go:14939）。
/// StringLiteralFull is a literal type for "full"
/// </summary>
public sealed class StringLiteralFull : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralFull) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14943）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"full\"");

    /// <summary>Go: <c>(o *StringLiteralFull) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14949）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"full\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralFull", "\"full\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralUnchanged struct</c>（lsp_generated.go:14961）。
/// StringLiteralUnchanged is a literal type for "unchanged"
/// </summary>
public sealed class StringLiteralUnchanged : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralUnchanged) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14965）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"unchanged\"");

    /// <summary>Go: <c>(o *StringLiteralUnchanged) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14971）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"unchanged\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralUnchanged", "\"unchanged\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralSnippet struct</c>（lsp_generated.go:14983）。
/// StringLiteralSnippet is a literal type for "snippet"
/// </summary>
public sealed class StringLiteralSnippet : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralSnippet) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:14987）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"snippet\"");

    /// <summary>Go: <c>(o *StringLiteralSnippet) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:14993）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"snippet\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralSnippet", "\"snippet\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralLanguageServerErrorResponse struct</c>（lsp_generated.go:15005）。
/// StringLiteralLanguageServerErrorResponse is a literal type for "languageServer.errorResponse"
/// </summary>
public sealed class StringLiteralLanguageServerErrorResponse : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralLanguageServerErrorResponse) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15009）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"languageServer.errorResponse\"");

    /// <summary>Go: <c>(o *StringLiteralLanguageServerErrorResponse) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15015）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"languageServer.errorResponse\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralLanguageServerErrorResponse", "\"languageServer.errorResponse\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralError struct</c>（lsp_generated.go:15027）。
/// StringLiteralError is a literal type for "error"
/// </summary>
public sealed class StringLiteralError : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralError) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15031）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"error\"");

    /// <summary>Go: <c>(o *StringLiteralError) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15037）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"error\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralError", "\"error\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralLanguageServerPerformanceStats struct</c>（lsp_generated.go:15049）。
/// StringLiteralLanguageServerPerformanceStats is a literal type for "languageServer.performanceStats"
/// </summary>
public sealed class StringLiteralLanguageServerPerformanceStats : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralLanguageServerPerformanceStats) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15053）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"languageServer.performanceStats\"");

    /// <summary>Go: <c>(o *StringLiteralLanguageServerPerformanceStats) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15059）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"languageServer.performanceStats\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralLanguageServerPerformanceStats", "\"languageServer.performanceStats\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralUsage struct</c>（lsp_generated.go:15071）。
/// StringLiteralUsage is a literal type for "usage"
/// </summary>
public sealed class StringLiteralUsage : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralUsage) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15075）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"usage\"");

    /// <summary>Go: <c>(o *StringLiteralUsage) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15081）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"usage\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralUsage", "\"usage\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralLanguageServerProjectInfo struct</c>（lsp_generated.go:15093）。
/// StringLiteralLanguageServerProjectInfo is a literal type for "languageServer.projectInfo"
/// </summary>
public sealed class StringLiteralLanguageServerProjectInfo : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralLanguageServerProjectInfo) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15097）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"languageServer.projectInfo\"");

    /// <summary>Go: <c>(o *StringLiteralLanguageServerProjectInfo) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15103）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"languageServer.projectInfo\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralLanguageServerProjectInfo", "\"languageServer.projectInfo\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralClassifiedTextRun struct</c>（lsp_generated.go:15115）。
/// StringLiteralClassifiedTextRun is a literal type for "ClassifiedTextRun"
/// </summary>
public sealed class StringLiteralClassifiedTextRun : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralClassifiedTextRun) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15119）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"ClassifiedTextRun\"");

    /// <summary>Go: <c>(o *StringLiteralClassifiedTextRun) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15125）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"ClassifiedTextRun\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralClassifiedTextRun", "\"ClassifiedTextRun\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralClassifiedTextElement struct</c>（lsp_generated.go:15137）。
/// StringLiteralClassifiedTextElement is a literal type for "ClassifiedTextElement"
/// </summary>
public sealed class StringLiteralClassifiedTextElement : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralClassifiedTextElement) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15141）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"ClassifiedTextElement\"");

    /// <summary>Go: <c>(o *StringLiteralClassifiedTextElement) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15147）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"ClassifiedTextElement\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralClassifiedTextElement", "\"ClassifiedTextElement\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralImageId struct</c>（lsp_generated.go:15159）。
/// StringLiteralImageId is a literal type for "ImageId"
/// </summary>
public sealed class StringLiteralImageId : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralImageId) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15163）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"ImageId\"");

    /// <summary>Go: <c>(o *StringLiteralImageId) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15169）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"ImageId\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralImageId", "\"ImageId\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralImageElement struct</c>（lsp_generated.go:15181）。
/// StringLiteralImageElement is a literal type for "ImageElement"
/// </summary>
public sealed class StringLiteralImageElement : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralImageElement) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15185）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"ImageElement\"");

    /// <summary>Go: <c>(o *StringLiteralImageElement) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15191）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"ImageElement\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralImageElement", "\"ImageElement\"", v);
        }
    }

}

/// <summary>Go: <c>type StringLiteralContainerElement struct</c>（lsp_generated.go:15203）。
/// StringLiteralContainerElement is a literal type for "ContainerElement"
/// </summary>
public sealed class StringLiteralContainerElement : IJsonMarshaler, IJsonUnmarshaler
{

    /// <summary>Go: <c>(o StringLiteralContainerElement) MarshalJSONTo(enc *json.Encoder) error</c>（lsp_generated.go:15207）。</summary>
    public void MarshalJSONTo(JsonEncoder enc) => enc.WriteValue("\"ContainerElement\"");

    /// <summary>Go: <c>(o *StringLiteralContainerElement) UnmarshalJSONFrom(dec *json.Decoder) error</c>（lsp_generated.go:15213）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder dec)
    {
        var v = dec.ReadValue();
        if (!v.AsSpan().SequenceEqual("\"ContainerElement\""u8))
        {
            throw ErrLiteralMismatch("StringLiteralContainerElement", "\"ContainerElement\"", v);
        }
    }

}
