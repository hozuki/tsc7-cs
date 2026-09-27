// ported from: internal/contentmapper/hostimpl.go
//
// C# 映射与偏离（逐条）：
// 1) `json.Value`（= `jsontext.Value`，原始 JSON 字节）→ `byte[]`；读写一律走 `JsonEncoder.WriteValue` /
//    `JsonDecoder.ReadValue`（**不能**把它当普通值交给 Json.Marshal——本项目 Json 把裸 `byte[]` 当 base64）。
// 2) `io.ReadWriteCloser`/`io.Closer` → `Stream`（Stream 自带 Close()；Go 里那些"包装 rwc 再多一个行为"的类型
//    在 C# 里就是 Stream 的子类）。`processExitState`（未导出接口）→ `IProcessExitState`（`(int, bool)` → `TryX`）。
// 3) `context.Context` → `TypeScript.Core.Context`；`context.WithCancel`/`WithTimeout` → `CancellationTokenSource`
//    （`Core.WithCancellation` 把 token 挂回 Context）。Go 的 `go conn.Run(ctx)` → 一个专用线程（`LongRunning`）。
// 4) `time.Duration`/`time.Now` → `TimeSpan`/`Stopwatch`（`atomic.Uint64`/`atomic.Int64` → `Interlocked`）。
// 5) `dialFunc` 的 `(Conn, io.Closer, PositionEncoding, string, error)` → C# 的具名元组 + 异常；dial 只在内部使用。
// 6) `func projectSpecKey(spec) string`（用 `%p` 打印指针）→ `ProjectSpecKey`（引用相等 + 稳定哈希），语义等价的
//    指针身份比较；Go 打印成字符串只是为了当 map 键。
// 7) `PositionEncoding`（Go 的 `type PositionEncoding string`）→ `readonly struct` + `UTF8`/`UTF16` 静态字段
//    （与 locale.Locale 同款：C# 的 enum 装不下 "utf-8" 这种值）。
// 8) `errors.Join(...)`（Close/Refresh 的错误聚合）→ 收集首个错误后继续其余清理，最后抛首个异常（Go 侧调用点
//    全部只判 err != nil）。`Close()`/`Release()` 在 C# 里返回 void（Go 的 err 只用于 errors.Join）。
// 9) Go 的 `%q` 与 C# 的 `"\"…\""` 一致；`strings.TrimSpace` → `string.Trim()`。
// 10) 已删除移植期遗留的**绝对路径调试 trace**（非 Go 代码，Main 授权收口）：`SetLocale`/`Project`/`Release`/
//     `ProjectLease.Release` 四处共 8 行 `File.AppendAllText(@"D:\…\artifacts\<按进程号命名的调试 trace 文件>", …)`，
//     纯副作用、`hostimpl.go` 里没有对应物；它们会让并发测试争抢同一绝对路径的文件句柄
//     （`IOException: … being used by another process`）产生假红。只删这 8 行，其余逻辑一行未动。
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Runtime.CompilerServices;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Ipc;
using TypeScript.Json;
using TypeScript.JsonRpc;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
// `System.Text.Encoder/Decoder`（隐式 using）与 `TypeScript.Json.Encoder/Decoder` 同名 → CS0104，必须别名。
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;
// `Message` 在 TypeScript.Diagnostics（诊断消息）与 TypeScript.JsonRpc（JSON-RPC 消息）里同名 → 别名。
using RpcMessage = TypeScript.JsonRpc.Message;
using Loc = TypeScript.Locale.Locale;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.ContentMapper;

public static partial class ContentMapper
{
    /// <summary>Go: <c>const initializeTimeoutSeconds = 5</c>。</summary>
    internal const int _initializeTimeoutSeconds = 5;

    internal static readonly TimeSpan _initializeTimeout = TimeSpan.FromSeconds(_initializeTimeoutSeconds);

    // Go: content mapper 协议的方法名（导出常量）。
    public const string MethodInitialize = "initialize";
    public const string MethodOpenProject = "openProject";
    public const string MethodCloseProject = "closeProject";
    public const string MethodTransform = "transform";
}

/// <summary>Go: <c>type PositionEncoding string</c>（映射与诊断所用的坐标空间）。</summary>
public readonly struct PositionEncoding : IEquatable<PositionEncoding>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>PositionEncodingUTF8 = "utf-8"</c>。</summary>
    public static readonly PositionEncoding UTF8 = new("utf-8");

    /// <summary>Go 的 <c>PositionEncodingUTF16 = "utf-16"</c>。</summary>
    public static readonly PositionEncoding UTF16 = new("utf-16");

    /// <summary>底层字符串（Go 的 <c>string(pe)</c>）。</summary>
    public string Value => _value ?? "";

    /// <summary>Go: <c>func (pe PositionEncoding) String() string</c>（fmt 的 %q/%s 都用它）。</summary>
    public string String() => Value;

    public override string ToString() => Value;

    public bool Equals(PositionEncoding other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is PositionEncoding other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(PositionEncoding left, PositionEncoding right) => left.Equals(right);

    public static bool operator !=(PositionEncoding left, PositionEncoding right) => !left.Equals(right);

    /// <summary>Go 的 <c>PositionEncoding("utf-8")</c> 转换：C# 里用公开构造器表达（Go 的类型转换天然公开）。</summary>
    public PositionEncoding(string value) => _value = value;

    // Go 的 PositionEncoding 是 string，所以 JSON 就是一个字符串（读出方向由持有它的类型的 mapper 显式解成 string）。
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));
}

/// <summary>Go: <c>type InitializeParams struct</c>（initialize 请求的参数对象）。</summary>
public sealed class InitializeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Locale string `json:"locale,omitempty"`</c>。</summary>
    public string Locale = "";

    /// <summary>Go: <c>PositionEncodings []PositionEncoding `json:"positionEncodings"`</c>。</summary>
    public PositionEncoding[] PositionEncodings = [];

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        if (Locale.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("locale"));
            encoder.WriteToken(JsonToken.String(Locale));
        }

        encoder.WriteToken(JsonToken.String("positionEncodings"));
        encoder.WriteToken(BeginArray);
        foreach (var encoding in PositionEncodings)
        {
            encoding.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndArray);
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for InitializeParams");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "locale":
                    Locale = decoder.Decode<string>() ?? "";
                    break;
                case "positionEncodings":
                    PositionEncodings = ReadStringArray(decoder);
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }

    /// <summary>读一个字符串数组（Go 的 <c>[]PositionEncoding</c> 在 C# 里就是字符串数组）。</summary>
    internal static PositionEncoding[] ReadStringArray(JsonDecoder decoder)
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return [];
        }

        decoder.ReadToken(); // [
        var result = new List<PositionEncoding>();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            result.Add(new PositionEncoding(decoder.Decode<string>() ?? ""));
        }

        decoder.ReadToken(); // ]
        return [.. result];
    }
}

/// <summary>Go: <c>type InitializeResult struct</c>（mapper 对 initialize 的响应）。</summary>
public sealed class InitializeResult : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>PositionEncoding PositionEncoding `json:"positionEncoding"`</c>。</summary>
    public PositionEncoding PositionEncoding;

    /// <summary>Go: <c>DiagnosticSource string `json:"diagnosticSource"`</c>。</summary>
    public string DiagnosticSource = "";

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("positionEncoding"));
        PositionEncoding.MarshalJSONTo(encoder);
        encoder.WriteToken(JsonToken.String("diagnosticSource"));
        encoder.WriteToken(JsonToken.String(DiagnosticSource));
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for InitializeResult");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "positionEncoding":
                    PositionEncoding = new PositionEncoding(decoder.Decode<string>() ?? "");
                    break;
                case "diagnosticSource":
                    DiagnosticSource = decoder.Decode<string>() ?? "";
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type OpenProjectParams struct</c>（openProject 请求的参数对象）。</summary>
public sealed class OpenProjectParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ConfigFileName string `json:"configFileName"`</c>（没有配置文件时为空）。</summary>
    public string ConfigFileName = "";

    /// <summary>Go: <c>ProjectHandle string `json:"projectHandle"`</c>（host 分配的进程内不透明句柄）。</summary>
    public string ProjectHandle = "";

    /// <summary>Go: <c>Options json.Value `json:"options,omitempty"`</c>。</summary>
    public byte[]? Options;

    /// <summary>Go: <c>CompilerOptions json.Value `json:"compilerOptions"`</c>。</summary>
    public byte[]? CompilerOptions;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("configFileName"));
        encoder.WriteToken(JsonToken.String(ConfigFileName));
        encoder.WriteToken(JsonToken.String("projectHandle"));
        encoder.WriteToken(JsonToken.String(ProjectHandle));
        if (Options is { Length: > 0 })
        {
            encoder.WriteToken(JsonToken.String("options"));
            encoder.WriteValue(Options);
        }

        encoder.WriteToken(JsonToken.String("compilerOptions"));
        // Go 的 json.Value 为 nil 时写出 null（字段没有 omitzero）。
        encoder.WriteValue(CompilerOptions ?? "null"u8);
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for OpenProjectParams");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "configFileName":
                    ConfigFileName = decoder.Decode<string>() ?? "";
                    break;
                case "projectHandle":
                    ProjectHandle = decoder.Decode<string>() ?? "";
                    break;
                case "options":
                    Options = decoder.PeekKind() == JsonKind.Null ? null : decoder.ReadValue();
                    break;
                case "compilerOptions":
                    CompilerOptions = decoder.PeekKind() == JsonKind.Null ? null : decoder.ReadValue();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type OpenProjectResult struct</c>（mapper 对 openProject 的响应；ConfigIdentity 与
/// WatchedFiles 只有声明了 dynamicConfig 的 mapper 才能返回）。</summary>
public sealed class OpenProjectResult : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ConfigIdentity string `json:"configIdentity"`</c>。</summary>
    public string ConfigIdentity = "";

    /// <summary>Go: <c>WatchedFiles []string `json:"watchedFiles,omitempty"`</c>。</summary>
    public string[]? WatchedFiles;

    /// <summary>Go: <c>OptionDiagnostics []OptionDiagnosticResult `json:"optionDiagnostics,omitempty"`</c>。</summary>
    public OptionDiagnosticResult[]? OptionDiagnostics;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("configIdentity"));
        encoder.WriteToken(JsonToken.String(ConfigIdentity));
        if (WatchedFiles is { Length: > 0 })
        {
            encoder.WriteToken(JsonToken.String("watchedFiles"));
            encoder.WriteToken(BeginArray);
            foreach (var file in WatchedFiles)
            {
                encoder.WriteToken(JsonToken.String(file));
            }

            encoder.WriteToken(EndArray);
        }

        if (OptionDiagnostics is { Length: > 0 })
        {
            encoder.WriteToken(JsonToken.String("optionDiagnostics"));
            encoder.WriteToken(BeginArray);
            foreach (var diagnostic in OptionDiagnostics)
            {
                diagnostic.MarshalJSONTo(encoder);
            }

            encoder.WriteToken(EndArray);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for OpenProjectResult");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "configIdentity":
                    ConfigIdentity = decoder.Decode<string>() ?? "";
                    break;
                case "watchedFiles":
                    WatchedFiles = ReadStringList(decoder);
                    break;
                case "optionDiagnostics":
                    OptionDiagnostics = ContentMapperJsonArrays.ReadOptionalObjectArray<OptionDiagnosticResult>(decoder);
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }

    internal static string[]? ReadStringList(JsonDecoder decoder)
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return null;
        }

        return [.. decoder.DecodeNew<List<string>>()];
    }
}

/// <summary>Go: <c>type OptionDiagnosticResult struct</c>（mapper 报告的选项诊断；path 是原始 JSON 段的数组）。</summary>
public sealed class OptionDiagnosticResult : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Path []json.Value `json:"path"`</c>。</summary>
    public byte[][] Path = [];

    /// <summary>Go: <c>MessageText string `json:"messageText"`</c>。</summary>
    public string MessageText = "";

    /// <summary>Go: <c>Code int32 `json:"code"`</c>（没有 omitzero：0 也写出）。</summary>
    public int Code;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("path"));
        encoder.WriteToken(BeginArray);
        foreach (var segment in Path)
        {
            encoder.WriteValue(segment);
        }

        encoder.WriteToken(EndArray);
        encoder.WriteToken(JsonToken.String("messageText"));
        encoder.WriteToken(JsonToken.String(MessageText));
        encoder.WriteToken(JsonToken.String("code"));
        encoder.WriteToken(JsonToken.Number(Code.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for OptionDiagnosticResult");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "path":
                    Path = ReadRawValues(decoder);
                    break;
                case "messageText":
                    MessageText = decoder.Decode<string>() ?? "";
                    break;
                case "code":
                    Code = decoder.Decode<int>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }

    /// <summary>Go 的 <c>[]json.Value</c>：逐个原始 JSON 值（byte[] 载体不能交给 Json.Marshal）。</summary>
    internal static byte[][] ReadRawValues(JsonDecoder decoder)
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return [];
        }

        decoder.ReadToken(); // [
        var result = new List<byte[]>();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            result.Add(decoder.ReadValue());
        }

        decoder.ReadToken(); // ]
        return [.. result];
    }
}

/// <summary>Go: <c>type CloseProjectParams struct</c>。</summary>
public sealed class CloseProjectParams : IJsonMarshaler, IJsonUnmarshaler
{
    public string ProjectHandle = "";

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("projectHandle"));
        encoder.WriteToken(JsonToken.String(ProjectHandle));
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for CloseProjectParams");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "projectHandle":
                    ProjectHandle = decoder.Decode<string>() ?? "";
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type TransformParams struct</c>。</summary>
public sealed class TransformParams : IJsonMarshaler, IJsonUnmarshaler
{
    public string FileName = "";
    public string Content = "";
    public string ProjectHandle = "";

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("fileName"));
        encoder.WriteToken(JsonToken.String(FileName));
        encoder.WriteToken(JsonToken.String("content"));
        encoder.WriteToken(JsonToken.String(Content));
        encoder.WriteToken(JsonToken.String("projectHandle"));
        encoder.WriteToken(JsonToken.String(ProjectHandle));
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for TransformParams");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "fileName":
                    FileName = decoder.Decode<string>() ?? "";
                    break;
                case "content":
                    Content = decoder.Decode<string>() ?? "";
                    break;
                case "projectHandle":
                    ProjectHandle = decoder.Decode<string>() ?? "";
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type MappedOutput struct</c>（虚拟源文本及其到原始输入的映射）。</summary>
public class MappedOutput : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Text string `json:"text"`</c>。</summary>
    public string Text = "";

    /// <summary>Go: <c>Extension string `json:"extension"`</c>（决定虚拟源文件的语法）。</summary>
    public string Extension = "";

    /// <summary>Go: <c>Mappings json.Value `json:"mappings,omitempty"`</c>（span map 的元组数组 JSON，
    /// 坐标空间由 initialize 协商的 positionEncoding 决定；缺省/空表示输出完全合成）。</summary>
    public byte[]? Mappings;

    /// <summary>Go: <c>DiagnosticDirectives *DiagnosticDirectives `json:"diagnosticDirectives,omitempty"`</c>。</summary>
    public DiagnosticDirectives? DiagnosticDirectives;

    public virtual void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        WriteMappedOutputMembers(encoder);
        encoder.WriteToken(EndObject);
    }

    public virtual void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for MappedOutput");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            if (!TryReadMappedOutputMember(decoder, name))
            {
                decoder.SkipValue();
            }
        }

        decoder.ReadToken();
    }

    /// <summary>C# 侧新增（为继承分片共用）：写出 MappedOutput 的成员（不含大括号）。</summary>
    protected void WriteMappedOutputMembers(JsonEncoder encoder)
    {
        encoder.WriteToken(JsonToken.String("text"));
        encoder.WriteToken(JsonToken.String(Text));
        encoder.WriteToken(JsonToken.String("extension"));
        encoder.WriteToken(JsonToken.String(Extension));
        if (Mappings is { Length: > 0 })
        {
            encoder.WriteToken(JsonToken.String("mappings"));
            encoder.WriteValue(Mappings);
        }

        if (DiagnosticDirectives is not null)
        {
            encoder.WriteToken(JsonToken.String("diagnosticDirectives"));
            DiagnosticDirectives.MarshalJSONTo(encoder);
        }
    }

    /// <summary>C# 侧新增（为继承分片共用）：读一个 MappedOutput 成员；返回 false 表示名字不属于它。</summary>
    protected bool TryReadMappedOutputMember(JsonDecoder decoder, string name)
    {
        switch (name)
        {
            case "text":
                Text = decoder.Decode<string>() ?? "";
                return true;
            case "extension":
                Extension = decoder.Decode<string>() ?? "";
                return true;
            case "mappings":
                Mappings = decoder.PeekKind() == JsonKind.Null ? null : decoder.ReadValue();
                return true;
            case "diagnosticDirectives":
                DiagnosticDirectives = decoder.PeekKind() == JsonKind.Null ? null : decoder.DecodeNew<DiagnosticDirectives>();
                return true;
            default:
                return false;
        }
    }
}

/// <summary>Go: <c>type DiagnosticDirectivePolicy uint8</c>（映射诊断指令元组里的策略数值）。</summary>
public enum DiagnosticDirectivePolicy : byte
{
    Ignore = 0,
    Expect = 1,
}

/// <summary>Go: <c>type UnusedExpectDirectiveDiagnostic struct</c>。</summary>
public sealed class UnusedExpectDirectiveDiagnostic : IJsonMarshaler, IJsonUnmarshaler
{
    public int Code;
    public string MessageText = "";

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("code"));
        encoder.WriteToken(JsonToken.Number(Code.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.String("messageText"));
        encoder.WriteToken(JsonToken.String(MessageText));
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for UnusedExpectDirectiveDiagnostic");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "code":
                    Code = decoder.Decode<int>();
                    break;
                case "messageText":
                    MessageText = decoder.Decode<string>() ?? "";
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type DiagnosticDirectives struct</c>（跨紧凑指令元组共享 unused-expect 诊断）。</summary>
public sealed class DiagnosticDirectives : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>UnusedExpectDirectiveDiagnostics []UnusedExpectDirectiveDiagnostic
    /// `json:"unusedExpectDirectiveDiagnostics"`</c>。</summary>
    public UnusedExpectDirectiveDiagnostic[] UnusedExpectDirectiveDiagnostics = [];

    /// <summary>Go: <c>Directives []MappedDiagnosticDirective `json:"directives"`</c>。</summary>
    public MappedDiagnosticDirective[] Directives = [];

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("unusedExpectDirectiveDiagnostics"));
        encoder.WriteToken(BeginArray);
        foreach (var diagnostic in UnusedExpectDirectiveDiagnostics)
        {
            diagnostic.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndArray);
        encoder.WriteToken(JsonToken.String("directives"));
        encoder.WriteToken(BeginArray);
        foreach (var directive in Directives)
        {
            directive.MarshalJSONTo(encoder);
        }

        encoder.WriteToken(EndArray);
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for DiagnosticDirectives");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "unusedExpectDirectiveDiagnostics":
                    UnusedExpectDirectiveDiagnostics = ContentMapperJsonArrays.ReadObjectArray<UnusedExpectDirectiveDiagnostic>(decoder);
                    break;
                case "directives":
                    Directives = ContentMapperJsonArrays.ReadObjectArray<MappedDiagnosticDirective>(decoder);
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>
/// Go: <c>type MappedDiagnosticDirective struct</c>。编码成
/// <c>[originalStart, originalLength, virtualStart, virtualEnd, policy, unusedExpectDirectiveIndex?]</c>；
/// 省略索引时表示"唯一的 unused-expect 诊断"，若这样的诊断不是恰好一个则非法。
/// </summary>
public sealed class MappedDiagnosticDirective : IJsonMarshaler, IJsonUnmarshaler
{
    public int OriginalStart;
    public int OriginalLength;
    public int VirtualStart;
    public int VirtualEnd;
    public DiagnosticDirectivePolicy Policy;

    /// <summary>Go: <c>UnusedExpectDirectiveIndex *int</c>（nil = 省略）。</summary>
    public int? UnusedExpectDirectiveIndex;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.Number(OriginalStart.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.Number(OriginalLength.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.Number(VirtualStart.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.Number(VirtualEnd.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.Number(((byte)Policy).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (UnusedExpectDirectiveIndex is { } index)
        {
            encoder.WriteToken(JsonToken.Number(index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        encoder.WriteToken(EndArray);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginArray)
        {
            throw new JsonException("contentmapper: expected an array for MappedDiagnosticDirective");
        }

        var values = new List<int>(6);
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            values.Add(decoder.Decode<int>());
        }

        decoder.ReadToken(); // ]
        if (values.Count is not (5 or 6))
        {
            throw new JsonException($"diagnostic directive tuple must contain 5 or 6 elements, got {values.Count}");
        }

        OriginalStart = values[0];
        OriginalLength = values[1];
        VirtualStart = values[2];
        VirtualEnd = values[3];
        Policy = (DiagnosticDirectivePolicy)values[4];
        UnusedExpectDirectiveIndex = values.Count == 6 ? values[5] : null;
    }
}

/// <summary>Go: <c>type SupplementalOutput struct { MappedOutput }</c>（内嵌 → C# 继承）。</summary>
public sealed class SupplementalOutput : MappedOutput
{
}

/// <summary>Go: <c>type TransformResult struct { MappedOutput; Diagnostics; Supplemental }</c>。</summary>
public sealed class TransformResult : MappedOutput
{
    /// <summary>Go: <c>Diagnostics []Diagnostic `json:"diagnostics,omitempty"`</c>（原始源码坐标里的错误）。</summary>
    public Diagnostic[]? Diagnostics;

    /// <summary>Go: <c>Supplemental []SupplementalOutput `json:"supplemental,omitempty"`</c>。</summary>
    public SupplementalOutput[]? Supplemental;

    public override void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        WriteMappedOutputMembers(encoder);
        if (Diagnostics is { Length: > 0 })
        {
            encoder.WriteToken(JsonToken.String("diagnostics"));
            encoder.WriteToken(BeginArray);
            foreach (var diagnostic in Diagnostics)
            {
                diagnostic.MarshalJSONTo(encoder);
            }

            encoder.WriteToken(EndArray);
        }

        if (Supplemental is { Length: > 0 })
        {
            encoder.WriteToken(JsonToken.String("supplemental"));
            encoder.WriteToken(BeginArray);
            foreach (var supplemental in Supplemental)
            {
                supplemental.MarshalJSONTo(encoder);
            }

            encoder.WriteToken(EndArray);
        }

        encoder.WriteToken(EndObject);
    }

    public override void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for TransformResult");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            switch (name)
            {
                case "diagnostics":
                    Diagnostics = ContentMapperJsonArrays.ReadOptionalObjectArray<Diagnostic>(decoder);
                    break;
                case "supplemental":
                    Supplemental = ContentMapperJsonArrays.ReadOptionalObjectArray<SupplementalOutput>(decoder);
                    break;
                default:
                    if (!TryReadMappedOutputMember(decoder, name))
                    {
                        decoder.SkipValue();
                    }

                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type Diagnostic struct</c>（mapper 在原始源码坐标里报告的错误）。</summary>
public sealed class Diagnostic : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>MessageText string `json:"messageText"`</c>。</summary>
    public string MessageText = "";

    /// <summary>Go: <c>Start int `json:"start"`</c>（按协商的 positionEncoding 定位）。</summary>
    public int Start;

    /// <summary>Go: <c>Length int `json:"length"`</c>。</summary>
    public int Length;

    /// <summary>Go: <c>Code int32 `json:"code"`</c>（没有 omitzero）。</summary>
    public int Code;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("messageText"));
        encoder.WriteToken(JsonToken.String(MessageText));
        encoder.WriteToken(JsonToken.String("start"));
        encoder.WriteToken(JsonToken.Number(Start.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.String("length"));
        encoder.WriteToken(JsonToken.Number(Length.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.String("code"));
        encoder.WriteToken(JsonToken.Number(Code.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for Diagnostic");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "messageText":
                    MessageText = decoder.Decode<string>() ?? "";
                    break;
                case "start":
                    Start = decoder.Decode<int>();
                    break;
                case "length":
                    Length = decoder.Decode<int>();
                    break;
                case "code":
                    Code = decoder.Decode<int>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }

        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type Spawner interface</c>（启动子进程，返回它的 stdio）。</summary>
public interface Spawner
{
    /// <summary>Go: <c>Spawn(command []string, dir string, stderr io.Writer) (io.ReadWriteCloser, error)</c>。
    /// 返回的流里 Read 是子进程 stdout、Write 是它的 stdin，Close 负责拆掉进程。</summary>
    Stream Spawn(string[] command, string dir, Stream stderr);
}

/// <summary>Go: <c>type SpawnerFunc func(...)</c>（把函数适配成 <see cref="Spawner"/>）。</summary>
public sealed class SpawnerFunc(Func<string[], string, Stream, Stream> spawn) : Spawner
{
    public Stream Spawn(string[] command, string dir, Stream stderr) => spawn(command, dir, stderr);
}

/// <summary>Go: <c>type processExitState interface { ExitCode() (int, bool) }</c>（未导出 → 按 C# 习惯命名与 TryX）。</summary>
public interface IProcessExitState
{
    bool TryExitCode(out int exitCode);
}

/// <summary>Go: <c>type Logger func(message string)</c>（按完整行接收 content mapper 的协议与进程输出）。</summary>
public delegate void Logger(string message);

/// <summary>Go: <c>type HostOptions struct</c>（可选的 content mapper 进程日志）。</summary>
public sealed class HostOptions
{
    public Logger? Logger;
}

/// <summary>Go: <c>type rejectHandler struct</c>：拒绝 mapper 发起的任何请求（协议是父端驱动的）。</summary>
internal sealed class RejectHandler : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params) =>
        throw new Exception($"content mapper sent an unexpected request: {method}");

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}

/// <summary>Go: <c>type loggingProtocol struct</c>（把协议消息记成完整日志行）。</summary>
internal sealed class LoggingProtocol(Protocol inner, string mapperName, Logger logger) : Protocol
{
    private void Log(string direction, object? message)
    {
        try
        {
            logger($"[content mapper: {mapperName}] {direction}: {System.Text.Encoding.UTF8.GetString(Marshal(message))}");
        }
        catch (JsonException e)
        {
            logger($"[content mapper: {mapperName}] {direction}: <failed to serialize: {e.Message}>");
        }
    }

    public RpcMessage? ReadMessage()
    {
        var message = inner.ReadMessage();
        if (message is not null)
        {
            Log("receive", message);
        }

        return message;
    }

    public void WriteRequest(ID? id, string method, object? @params)
    {
        Log("send", new RequestMessage { ID = id, Method = method, Params = @params });
        inner.WriteRequest(id, method, @params);
    }

    public void WriteNotification(string method, object? @params)
    {
        Log("send", new RequestMessage { Method = method, Params = @params });
        inner.WriteNotification(method, @params);
    }

    public void WriteResponse(ID? id, object? result)
    {
        Log("send", new ResponseMessage { ID = id, Result = result });
        inner.WriteResponse(id, result);
    }

    public void WriteError(ID? id, ResponseError? err)
    {
        Log("send", new ResponseMessage { ID = id, Error = err });
        inner.WriteError(id, err);
    }
}

/// <summary>Go: <c>type stderrLogger struct</c>（把子进程 stderr 的字节流按行转成日志）。</summary>
internal sealed class StderrLogger(string mapperName, Logger logger) : Stream
{
    private readonly object _mu = new();
    private readonly System.Text.StringBuilder _pending = new();

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // Go: `w.pending += string(data)` 然后按 '\n' 切完整行；C# 侧用 UTF-8 解字节（等价）。
        lock (_mu)
        {
            _pending.Append(System.Text.Encoding.UTF8.GetString(buffer));
            while (true)
            {
                var text = _pending.ToString();
                var index = text.IndexOf('\n');
                if (index < 0)
                {
                    break;
                }

                Log(text[..index].TrimEnd('\r'));
                _pending.Clear();
                _pending.Append(text[(index + 1)..]);
            }
        }
    }

    /// <summary>Go: <c>func (w *stderrLogger) flush()</c>（把没换行的尾巴也记一行）。</summary>
    public void FlushPending()
    {
        lock (_mu)
        {
            if (_pending.Length != 0)
            {
                Log(_pending.ToString().TrimEnd('\r'));
                _pending.Clear();
            }
        }
    }

    private void Log(string message) => logger($"[content mapper: {mapperName}] stderr: {message}");

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>Go: <c>type loggedProcess struct</c>（Close 时先 flush stderr 的尾巴）。</summary>
internal sealed class LoggedProcess(Stream inner, StderrLogger stderr) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

    public override void Close()
    {
        try
        {
            inner.Close();
        }
        finally
        {
            stderr.FlushPending();
        }
    }

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);
}

/// <summary>Go: <c>type closeOnceReadWriteCloser struct</c>（Close 只生效一次；ExitCode 透传）。</summary>
internal sealed class CloseOnceReadWriteCloser(Stream inner) : Stream, IProcessExitState
{
    private int _closed;
    private Exception? _error;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => inner.Read(buffer);

    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);

    public override void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            try
            {
                inner.Close();
            }
            catch (Exception e)
            {
                _error = e;
            }
        }

        // Go: `func (c *closeOnceReadWriteCloser) Close() error { c.once.Do(...); return c.err }`——首次调用
        // 关掉底层流并记住错误，之后每次调用都原样返回同一个错误（host.Close 会把它并进 errors.Join）。
        if (_error is not null)
        {
            throw _error;
        }
    }

    /// <summary>Go: <c>func (c *closeOnceReadWriteCloser) ExitCode() (int, bool)</c>。</summary>
    public bool TryExitCode(out int exitCode)
    {
        if (inner is IProcessExitState state)
        {
            return state.TryExitCode(out exitCode);
        }

        exitCode = 0;
        return false;
    }

    public override void Flush() => inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => inner.SetLength(value);
}

/// <summary>Go: <c>type dialFunc</c>（建立到 mapper 的运行连接；测试替换成内存连接）。</summary>
internal delegate (Conn Conn, Stream Closer, PositionEncoding PositionEncoding, string DiagnosticSource) DialFunc(
    Context ctx,
    Mapper mapper,
    Loc diagnosticLocale);

/// <summary>Go: <c>type host struct</c>（每个 mapper identity 一个子进程；<see cref="Host"/> 的生产实现）。</summary>
internal sealed class HostImpl : Host
{
    private readonly Context _ctx;
    private readonly CancellationTokenSource _cancel;
    private readonly DialFunc _dial;
    private readonly TimingCollector _timing;
    private readonly ReaderWriterLockSlim _lifecycleMu = new(LockRecursionPolicy.NoRecursion);

    private Loc _diagnosticLocale;

    private readonly object _mu = new();
    private Dictionary<string, MapperConn>? _conns = new(StringComparer.Ordinal);
    private Dictionary<string, ProjectEntry>? _projects = new(StringComparer.Ordinal);
    private Dictionary<ProjectSpecKey, ProjectLease>? _projectLeases = [];
    private ulong _nextProjectId;

    internal HostImpl(Context ctx, Loc diagnosticLocale, TimingCollector timing, DialFunc dial)
    {
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
        _cancel = cancel;
        _ctx = WithCancellation(ctx, cancel.Token);
        _diagnosticLocale = diagnosticLocale;
        _timing = timing;
        _dial = dial;

        // Go: `h.stop = context.AfterFunc(ctx, func() { _ = h.Close() })`。C# 侧不保存"停止回调"的句柄：
        // ctx 取消后再次调用 Close 是幂等的（见 Close），效果与 Go 的 stop+cancel 一致。
        if (ctx.CancellationToken.CanBeCanceled)
        {
            ctx.CancellationToken.Register(static state => ((HostImpl)state!).Close(), this);
        }
    }

    public Timings Timings() => _timing.Snapshot();

    public void SetLocale(Loc diagnosticLocale)
    {
        _lifecycleMu.EnterWriteLock();
        try
        {
            if (_diagnosticLocale.String() == diagnosticLocale.String())
            {
                return;
            }

            _diagnosticLocale = diagnosticLocale;

            var closers = new List<Stream>();
            lock (_mu)
            {
                if (_conns is not null)
                {
                    foreach (var entry in _conns.Values)
                    {
                        if (entry.Closer is not null)
                        {
                            closers.Add(entry.Closer);
                        }

                        entry.Conn = null;
                        entry.Closer = null;
                        entry.Error = null;
                        entry.PositionEncoding = default;
                        entry.DiagnosticSource = "";
                    }
                }

                if (_projects is not null)
                {
                    foreach (var project in _projects.Values)
                    {
                        project.Opened = false;
                    }
                }
            }

            foreach (var closer in closers)
            {
                // Go: `_ = closer.Close()`（SetLocale 明确丢弃关闭错误）。
                try
                {
                    closer.Close();
                }
                catch (Exception)
                {
                }
            }
        }
        finally
        {
            _lifecycleMu.ExitWriteLock();
        }
    }

    public Project Project(ProjectSpec spec)
    {
        _lifecycleMu.EnterReadLock();
        try
        {
            var key = new ProjectSpecKey(spec);
            lock (_mu)
            {
                if (_projects is null || _projectLeases is null || _conns is null)
                {
                    return null!;
                }

                if (_projectLeases.TryGetValue(key, out var existing))
                {
                    return existing.RetainLocked();
                }

                var lease = new ProjectLease(this, key, (Mapper[])spec.Mappers.Clone());
                lease.Refs = 1;
                foreach (var mapper in spec.Mappers)
                {
                    var entryKey = $"{mapper.Identity()}:{_nextProjectId}";
                    _nextProjectId++;
                    var entry = new ProjectEntry { Mapper = mapper, Spec = spec, ProjectHandle = entryKey };
                    _projects[entryKey] = entry;
                    if (!_conns.TryGetValue(mapper.Identity(), out var connEntry))
                    {
                        connEntry = new MapperConn();
                        _conns[mapper.Identity()] = connEntry;
                    }

                    connEntry.Refs++;
                    lease._entries[mapper] = entryKey;
                }

                _projectLeases[key] = lease;
                return lease;
            }
        }
        finally
        {
            _lifecycleMu.ExitReadLock();
        }
    }

    internal void OpenProjectLocked(Context ctx, ProjectEntry entry)
    {
        if (entry.Opened)
        {
            return;
        }

        var (conn, _, diagnosticSource) = ConnForLocked(entry.Mapper);
        // Go 的 `json.Marshal(entry.spec.CompilerOptions)`：指针为 nil 时写出 `null`。
        var compilerOptions = entry.Spec.CompilerOptions is null
            ? "null"u8.ToArray()
            : MarshalCompilerOptions(entry.Spec.CompilerOptions);
        var mapperTiming = _timing.Mapper(entry.Mapper.Identity());
        var start = mapperTiming.StartRequest();
        byte[]? raw;
        try
        {
            raw = conn.Call(ctx, MethodOpenProject, new OpenProjectParams
            {
                ConfigFileName = entry.Spec.ConfigFileName,
                ProjectHandle = entry.ProjectHandle,
                Options = entry.Mapper.Options,
                CompilerOptions = compilerOptions,
            });
        }
        finally
        {
            mapperTiming.FinishRequest(mapperTiming.OpenProject, start);
        }

        OpenProjectResult result;
        try
        {
            result = UnmarshalNew<OpenProjectResult>(raw ?? []);
        }
        catch (JsonException)
        {
            throw new ProjectError(ProjectErrorKind.MalformedResponse);
        }

        if (entry.Mapper.DynamicConfig && result.ConfigIdentity.Length == 0)
        {
            throw new ProjectError(ProjectErrorKind.MissingConfigIdentity);
        }

        if (!entry.Mapper.DynamicConfig && result.ConfigIdentity.Length != 0)
        {
            throw new ProjectError(ProjectErrorKind.UnexpectedConfigIdentity);
        }

        if (!entry.Mapper.DynamicConfig && result.WatchedFiles is { Length: > 0 })
        {
            throw new ProjectError(ProjectErrorKind.UnexpectedWatchedFiles);
        }

        entry.ConfigIdentity = result.ConfigIdentity;
        if (result.WatchedFiles is not null)
        {
            foreach (var fileName in result.WatchedFiles)
            {
                if (!PathIsAbsolute(fileName))
                {
                    throw new ProjectError(ProjectErrorKind.NonAbsoluteWatchedFile);
                }
            }
        }

        entry.WatchedFiles = result.WatchedFiles is null ? [] : (string[])result.WatchedFiles.Clone();
        var optionDiagnostics = result.OptionDiagnostics ?? [];
        entry.OptionDiagnostics = new OptionDiagnostic[optionDiagnostics.Length];
        for (var i = 0; i < optionDiagnostics.Length; i++)
        {
            var diagnostic = optionDiagnostics[i];
            var path = new OptionPathSegment[diagnostic.Path.Length];
            for (var j = 0; j < diagnostic.Path.Length; j++)
            {
                var rawSegment = diagnostic.Path[j];
                path[j] = new OptionPathSegment();
                // Go 的 `rawSegment.Kind()`：jsontext.Value 的首字节决定种类（'"' = 字符串，'0' = 数字）。
                var kind = rawSegment.Length > 0 ? (char)rawSegment[0] : '\0';
                switch (kind)
                {
                    case '"':
                        try
                        {
                            path[j].Property = Unmarshal<string>(rawSegment) ?? "";
                        }
                        catch (JsonException)
                        {
                            throw new ProjectError(ProjectErrorKind.MalformedResponse);
                        }

                        break;
                    case '0':
                        int index;
                        try
                        {
                            index = Unmarshal<int>(rawSegment);
                        }
                        catch (JsonException)
                        {
                            throw new ProjectError(ProjectErrorKind.MalformedResponse);
                        }

                        if (index < 0)
                        {
                            throw new ProjectError(ProjectErrorKind.MalformedResponse);
                        }

                        path[j].Index = index;
                        path[j].IsIndex = true;
                        break;
                    default:
                        throw new ProjectError(ProjectErrorKind.MalformedResponse);
                }
            }

            entry.OptionDiagnostics[i] = new OptionDiagnostic
            {
                Mapper = entry.Mapper,
                Path = path,
                Source = diagnosticSource,
                Code = diagnostic.Code,
                MessageText = diagnostic.MessageText,
            };
        }

        entry.Opened = true;
    }

    private void CloseProject(Mapper mapper, Conn conn, string projectHandle)
    {
        var mapperTiming = _timing.Mapper(mapper.Identity());
        var start = mapperTiming.StartRequest();
        try
        {
            conn.Call(_ctx, MethodCloseProject, new CloseProjectParams { ProjectHandle = projectHandle });
        }
        finally
        {
            mapperTiming.FinishRequest(mapperTiming.CloseProject, start);
        }
    }

    public Action Acquire(Mapper[] mappers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var identities = new List<string>(mappers.Length);
        lock (_mu)
        {
            if (_conns is not null)
            {
                foreach (var mapper in mappers)
                {
                    var identity = mapper.Identity();
                    if (!seen.Add(identity))
                    {
                        continue;
                    }

                    identities.Add(identity);
                    if (!_conns.TryGetValue(identity, out var entry))
                    {
                        entry = new MapperConn();
                        _conns[identity] = entry;
                    }

                    entry.Refs++;
                }
            }
        }

        // Go 的 sync.OnceFunc：释放函数只生效一次。
        var released = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                Release(identities);
            }
        };
    }

    /// <summary>Go: <c>func (h *host) Transform(mapper *Mapper, request Request) (Result, error)</c>
    /// （用一个短命的默认选项项目）。</summary>
    public Result Transform(Mapper mapper, Request request)
    {
        var project = Project(new ProjectSpec
        {
            Mappers = [mapper],
            CompilerOptions = new CompilerOptions(),
        });
        try
        {
            return project.Transform(mapper, request);
        }
        finally
        {
            project.Close();
        }
    }

    internal Result TransformLocked(Context ctx, Mapper mapper, Request request, string projectHandle)
    {
        if (projectHandle.Length == 0)
        {
            throw new Exception("content mapper project handle is required");
        }

        // Go: `conn, positionEncoding, diagnosticSource, err := h.connFor(mapper); if err != nil { return
        // Result{}, NewTransformError(TransformErrorKindInitialize, err) }`（hostimpl.go:770-777）。
        Conn conn;
        PositionEncoding positionEncoding;
        string diagnosticSource;
        try
        {
            (conn, positionEncoding, diagnosticSource) = ConnFor(mapper);
        }
        catch (Exception e)
        {
            throw new TransformError(TransformErrorKind.Initialize, e);
        }

        var mapperTiming = _timing.Mapper(mapper.Identity());
        var start = mapperTiming.StartRequest();
        byte[]? raw;
        try
        {
            raw = conn.Call(ctx, MethodTransform, new TransformParams
            {
                FileName = request.FileName,
                Content = request.Content,
                ProjectHandle = projectHandle,
            });
        }
        catch (Exception e) when (e is not TransformError)
        {
            throw new TransformError(TransformErrorKind.Request, e);
        }
        finally
        {
            mapperTiming.FinishRequest(mapperTiming.Transform, start);
        }

        try
        {
            return DecodeTransformResult(raw ?? [], request.Content, positionEncoding, diagnosticSource);
        }
        catch (Exception e) when (e is not TransformError)
        {
            throw new TransformError(TransformErrorKind.Response, e);
        }
    }

    /// <summary>Go: <c>func (h *host) Close() error</c>（可重复调用；ctx 取消时自动调用）。</summary>
    public void Close()
    {
        Exception? firstError = null;
        _lifecycleMu.EnterWriteLock();
        try
        {
            _cancel.Cancel();
            List<Stream> closers = [];
            lock (_mu)
            {
                if (_conns is not null)
                {
                    foreach (var entry in _conns.Values)
                    {
                        if (entry.Closer is not null)
                        {
                            closers.Add(entry.Closer);
                        }
                    }
                }

                _conns = null;
                _projects = null;
                _projectLeases = null;
            }

            foreach (var closer in closers)
            {
                try
                {
                    closer.Close();
                }
                catch (Exception e)
                {
                    firstError ??= e;
                }
            }
        }
        finally
        {
            _lifecycleMu.ExitWriteLock();
        }

        if (firstError is not null)
        {
            throw firstError;
        }
    }

    /// <summary>Go: <c>func (h *host) connFor(mapper *Mapper)</c>（首次使用时启动进程；同 identity 共享）。</summary>
    internal (Conn Conn, PositionEncoding PositionEncoding, string DiagnosticSource) ConnFor(Mapper mapper)
    {
        lock (_mu)
        {
            return ConnForLocked(mapper);
        }
    }

    private (Conn Conn, PositionEncoding PositionEncoding, string DiagnosticSource) ConnForLocked(Mapper mapper)
    {
        if (_conns is null)
        {
            throw new Exception("content mapper host is closed");
        }

        var identity = mapper.Identity();
        if (!_conns.TryGetValue(identity, out var entry))
        {
            entry = new MapperConn();
            _conns[identity] = entry;
        }

        if (entry.Conn is not null || entry.Error is not null)
        {
            return entry.Error is not null
                ? throw entry.Error
                : (entry.Conn!, entry.PositionEncoding, entry.DiagnosticSource);
        }

        try
        {
            var (conn, closer, positionEncoding, diagnosticSource) = _dial(_ctx, mapper, _diagnosticLocale);
            entry.Conn = conn;
            entry.Closer = closer;
            entry.PositionEncoding = positionEncoding;
            entry.DiagnosticSource = diagnosticSource;
            return (conn, positionEncoding, diagnosticSource);
        }
        catch (Exception e)
        {
            // Go 缓存 dial 的失败，避免对坏 mapper 反复重试（并原样返回同一个错误对象）。
            entry.Error = e;
            throw;
        }
    }

    internal void Release(List<string> identities)
    {
        List<Stream> closers = [];
        lock (_mu)
        {
            if (_conns is not null)
            {
                foreach (var identity in identities)
                {
                    if (!_conns.TryGetValue(identity, out var entry))
                    {
                        continue;
                    }

                    entry.Refs--;
                    if (entry.Refs == 0)
                    {
                        _conns.Remove(identity);
                        if (entry.Closer is not null)
                        {
                            closers.Add(entry.Closer);
                        }
                    }
                }
            }
        }

        var closerCount = closers.Count;
        foreach (var closer in closers)
        {
            // Go: `_ = closer.Close()`（release 明确丢弃关闭错误）。
            try
            {
                closer.Close();
            }
            catch (Exception)
            {
            }
        }
    }

    internal Dictionary<string, ProjectEntry>? Projects
    {
        get
        {
            lock (_mu)
            {
                return _projects;
            }
        }
    }

    internal Dictionary<string, MapperConn>? Conns
    {
        get
        {
            lock (_mu)
            {
                return _conns;
            }
        }
    }

    internal Dictionary<ProjectSpecKey, ProjectLease>? ProjectLeases
    {
        get
        {
            lock (_mu)
            {
                return _projectLeases;
            }
        }
    }

    /// <summary>Go 的 <c>h.closeProject(...)</c>：调用方已持有 <c>_mu</c>（lease 的方法都在锁内调用）。</summary>
    internal void CloseProjectForLease(Mapper mapper, Conn conn, string projectHandle) =>
        CloseProject(mapper, conn, projectHandle);

    internal object Mu => _mu;

    internal void EnterLifecycleReadLock() => _lifecycleMu.EnterReadLock();

    internal void ExitLifecycleReadLock() => _lifecycleMu.ExitReadLock();

    internal Context Ctx => _ctx;
}

/// <summary>Go: <c>type projectEntry struct</c>。</summary>
internal sealed class ProjectEntry
{
    public Mapper Mapper = null!;
    public ProjectSpec Spec = null!;
    public string ProjectHandle = "";
    public bool Opened;
    public string ConfigIdentity = "";
    public string[] WatchedFiles = [];
    public OptionDiagnostic[] OptionDiagnostics = [];
}

/// <summary>Go: <c>type mapperConn struct</c>（一个 identity 的连接；<c>refs</c> 是活跃的 Acquire 数）。</summary>
internal sealed class MapperConn
{
    public Conn? Conn;
    public Stream? Closer;

    /// <summary>Go: <c>err</c>（非 nil 表示该 mapper 启动失败，缓存起来避免反复重试）。</summary>
    public Exception? Error;

    public PositionEncoding PositionEncoding;
    public string DiagnosticSource = "";
    public int Refs;
}

/// <summary>Go: <c>type operationTiming struct</c>（未导出的计数器；导出的是 <see cref="OperationTiming"/>）。</summary>
internal sealed class OperationTimingCollector
{
    private long _count;
    private long _durationTicks;

    public void Record(long startTimestamp)
    {
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _durationTicks, Stopwatch.GetElapsedTime(startTimestamp).Ticks);
    }

    public OperationTiming Snapshot() => new()
    {
        Count = (ulong)Interlocked.Read(ref _count),
        Duration = TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks)),
    };
}

/// <summary>Go: <c>type timingCollector struct</c>。</summary>
internal sealed class TimingCollector
{
    private readonly object _mu = new();
    private readonly Dictionary<string, MapperTimingCollector> _mappers = new(StringComparer.Ordinal);
    private ulong _activeRequests;
    private long _requestWaitStart;
    private TimeSpan _requestWaitElapsed;

    public MapperTimingCollector Mapper(string identity)
    {
        lock (_mu)
        {
            if (_mappers.TryGetValue(identity, out var existing))
            {
                return existing;
            }

            var timing = new MapperTimingCollector(this);
            _mappers[identity] = timing;
            return timing;
        }
    }

    public Timings Snapshot()
    {
        TimeSpan requestWait;
        Dictionary<string, MapperTimingCollector> mappers;
        lock (_mu)
        {
            requestWait = _requestWaitElapsed;
            if (_activeRequests != 0)
            {
                requestWait += Stopwatch.GetElapsedTime(_requestWaitStart);
            }

            mappers = new Dictionary<string, MapperTimingCollector>(_mappers, StringComparer.Ordinal);
        }

        var result = new Timings { Mappers = new Dictionary<string, MapperTimings>(mappers.Count, StringComparer.Ordinal), RequestWait = requestWait };
        foreach (var (identity, timing) in mappers)
        {
            result.Mappers[identity] = new MapperTimings
            {
                Spawn = timing.Spawn.Snapshot(),
                Initialize = timing.Initialize.Snapshot(),
                OpenProject = timing.OpenProject.Snapshot(),
                CloseProject = timing.CloseProject.Snapshot(),
                Transform = timing.Transform.Snapshot(),
            };
        }

        return result;
    }

    internal long StartRequest()
    {
        lock (_mu)
        {
            if (_activeRequests == 0)
            {
                _requestWaitStart = Stopwatch.GetTimestamp();
            }

            _activeRequests++;
        }

        return Stopwatch.GetTimestamp();
    }

    internal void FinishRequest(OperationTimingCollector operation, long start)
    {
        operation.Record(start);
        lock (_mu)
        {
            _activeRequests--;
            if (_activeRequests == 0)
            {
                _requestWaitElapsed += Stopwatch.GetElapsedTime(_requestWaitStart);
            }
        }
    }
}

/// <summary>Go: <c>type mapperTimingCollector struct</c>。</summary>
internal sealed class MapperTimingCollector(TimingCollector owner)
{
    public readonly OperationTimingCollector Spawn = new();
    public readonly OperationTimingCollector Initialize = new();
    public readonly OperationTimingCollector OpenProject = new();
    public readonly OperationTimingCollector CloseProject = new();
    public readonly OperationTimingCollector Transform = new();

    public long StartRequest() => owner.StartRequest();

    public void FinishRequest(OperationTimingCollector operation, long start) => owner.FinishRequest(operation, start);
}

/// <summary>Go: <c>type projectLease struct</c>（共享的 mapper 项目配置句柄）。</summary>
internal sealed class ProjectLease : Project
{
    private readonly HostImpl _host;
    private readonly ProjectSpecKey _key;
    private readonly Mapper[] _mappers;
    internal readonly Dictionary<Mapper, string> _entries = new(ReferenceEqualityComparer.Instance);

    public int Refs;
    private int _closed;

    public ProjectLease(HostImpl host, ProjectSpecKey key, Mapper[] mappers)
    {
        _host = host;
        _key = key;
        _mappers = mappers;
    }

    public Project RetainLocked()
    {
        Refs++;
        return new RetainedProject(this);
    }

    public void Refresh()
    {
        _host.EnterLifecycleReadLock();
        try
        {
            lock (_host.Mu)
            {
                var projects = _host.Projects;
                var conns = _host.Conns;
                if (projects is null)
                {
                    return;
                }

                Exception? firstError = null;
                foreach (var key in _entries.Values)
                {
                    if (!projects.TryGetValue(key, out var entry) || !entry.Opened)
                    {
                        continue;
                    }

                    if (conns is not null && conns.TryGetValue(entry.Mapper.Identity(), out var connEntry) && connEntry.Conn is not null)
                    {
                        try
                        {
                            _host.CloseProjectForLease(entry.Mapper, connEntry.Conn, entry.ProjectHandle);
                        }
                        catch (Exception e)
                        {
                            firstError ??= e;
                        }
                    }

                    entry.Opened = false;
                }

                if (firstError is not null)
                {
                    throw firstError;
                }
            }
        }
        finally
        {
            _host.ExitLifecycleReadLock();
        }
    }

    public string[] Identities()
    {
        _host.EnterLifecycleReadLock();
        try
        {
            lock (_host.Mu)
            {
                var projects = _host.Projects;
                if (projects is null)
                {
                    return [];
                }

                var identities = new List<string>(_mappers.Length);
                foreach (var mapper in _mappers)
                {
                    if (!_entries.TryGetValue(mapper, out var key) || !projects.TryGetValue(key, out var entry))
                    {
                        continue;
                    }

                    if (mapper.DynamicConfig)
                    {
                        _host.OpenProjectLocked(_host.Ctx, entry);
                        identities.Add(CombinedIdentity(mapper, entry.ConfigIdentity, entry.Spec.CompilerOptions));
                    }
                    else
                    {
                        var hash = Xxh3.Xxh3.Bytes(mapper.TransformIdentity(entry.Spec.CompilerOptions));
                        identities.Add(mapper.Identity() + ":" + Convert.ToHexStringLower(hash));
                    }
                }

                return [.. identities];
            }
        }
        finally
        {
            _host.ExitLifecycleReadLock();
        }
    }

    public string Identity(Mapper mapper)
    {
        _host.EnterLifecycleReadLock();
        try
        {
            lock (_host.Mu)
            {
                var projects = _host.Projects;
                if (projects is null || !_entries.TryGetValue(mapper, out var key) || !projects.TryGetValue(key, out var entry))
                {
                    return "";
                }

                if (mapper.DynamicConfig)
                {
                    _host.OpenProjectLocked(_host.Ctx, entry);
                    return CombinedIdentity(mapper, entry.ConfigIdentity, entry.Spec.CompilerOptions);
                }

                var hash = Xxh3.Xxh3.Bytes(mapper.TransformIdentity(entry.Spec.CompilerOptions));
                return mapper.Identity() + ":" + Convert.ToHexStringLower(hash);
            }
        }
        finally
        {
            _host.ExitLifecycleReadLock();
        }
    }

    public string[] WatchedFiles()
    {
        _host.EnterLifecycleReadLock();
        try
        {
            lock (_host.Mu)
            {
                var projects = _host.Projects;
                if (projects is null)
                {
                    return [];
                }

                var files = new List<string>();
                foreach (var key in _entries.Values)
                {
                    if (!projects.TryGetValue(key, out var entry))
                    {
                        continue;
                    }

                    if (entry.Mapper.DynamicConfig)
                    {
                        _host.OpenProjectLocked(_host.Ctx, entry);
                    }

                    files.AddRange(entry.WatchedFiles);
                }

                // Go 的 slices.Sort + slices.Compact。
                files.Sort(StringComparer.Ordinal);
                var result = new List<string>(files.Count);
                foreach (var file in files)
                {
                    if (result.Count == 0 || !string.Equals(result[^1], file, StringComparison.Ordinal))
                    {
                        result.Add(file);
                    }
                }

                return [.. result];
            }
        }
        finally
        {
            _host.ExitLifecycleReadLock();
        }
    }

    public OptionDiagnostic[] Diagnostics()
    {
        _host.EnterLifecycleReadLock();
        try
        {
            lock (_host.Mu)
            {
                var projects = _host.Projects;
                if (projects is null)
                {
                    return [];
                }

                var diagnostics = new List<OptionDiagnostic>();
                foreach (var mapper in _mappers)
                {
                    if (!_entries.TryGetValue(mapper, out var key) || !projects.TryGetValue(key, out var entry) || !entry.Opened)
                    {
                        continue;
                    }

                    diagnostics.AddRange(entry.OptionDiagnostics);
                }

                return [.. diagnostics];
            }
        }
        finally
        {
            _host.ExitLifecycleReadLock();
        }
    }

    public Result Transform(Mapper mapper, Request request)
    {
        _host.EnterLifecycleReadLock();
        try
        {
            string handle;
            lock (_host.Mu)
            {
                var projects = _host.Projects;
                if (projects is null || !_entries.TryGetValue(mapper, out var key) || !projects.TryGetValue(key, out var entry))
                {
                    throw new Exception("content mapper project is closed");
                }

                try
                {
                    _host.OpenProjectLocked(_host.Ctx, entry);
                }
                catch (Exception e)
                {
                    throw e is InitializeError
                        ? new TransformError(TransformErrorKind.Initialize, e)
                        : new TransformError(TransformErrorKind.Project, e);
                }

                handle = entry.ProjectHandle;
            }

            return _host.TransformLocked(_host.Ctx, mapper, request, handle);
        }
        finally
        {
            _host.ExitLifecycleReadLock();
        }
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            Release();
        }
    }

    /// <summary>Go: <c>func (p *projectLease) release() error</c>。每个保留引用各自减一次 refs
    /// （<see cref="RetainedProject.Close"/> 直接调它，绕开 <see cref="Close"/> 的 once）。</summary>
    internal void Release()
    {
        Exception? firstError = null;
        _host.EnterLifecycleReadLock();
        try
        {
            List<string> releasedIdentities = [];
            lock (_host.Mu)
            {
                Refs--;
                if (Refs < 0)
                {
                    throw new InvalidOperationException("content mapper project reference count below zero");
                }

                if (Refs != 0)
                {
                    return;
                }

                var leases = _host.ProjectLeases;
                var projects = _host.Projects;
                var conns = _host.Conns;
                if (leases is not null && projects is not null)
                {
                    if (leases.TryGetValue(_key, out var current) && ReferenceEquals(current, this))
                    {
                        leases.Remove(_key);
                    }

                    foreach (var key in _entries.Values)
                    {
                        if (!projects.TryGetValue(key, out var entry))
                        {
                            continue;
                        }

                        if (entry.Opened && conns is not null &&
                            conns.TryGetValue(entry.Mapper.Identity(), out var connEntry) && connEntry.Conn is not null)
                        {
                            try
                            {
                                _host.CloseProjectForLease(entry.Mapper, connEntry.Conn, entry.ProjectHandle);
                            }
                            catch (Exception e)
                            {
                                firstError ??= e;
                            }
                        }

                        projects.Remove(key);
                        releasedIdentities.Add(entry.Mapper.Identity());
                    }
                }

                _host.Release(releasedIdentities);
            }

            if (firstError is not null)
            {
                throw firstError;
            }
        }
        finally
        {
            _host.ExitLifecycleReadLock();
        }
    }
}

/// <summary>Go: <c>type retainedProject struct</c>（每个保留引用各自 Close 一次）。</summary>
internal sealed class RetainedProject(ProjectLease lease) : Project
{
    private int _closed;

    public void Refresh() => lease.Refresh();

    public string[] Identities() => lease.Identities();

    public string Identity(Mapper mapper) => lease.Identity(mapper);

    public string[] WatchedFiles() => lease.WatchedFiles();

    public OptionDiagnostic[] Diagnostics() => lease.Diagnostics();

    public Result Transform(Mapper mapper, Request request) => lease.Transform(mapper, request);

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            // Go: `func (p *retainedProject) Close() { p.once.Do(func() { err = p.projectLease.release() }) }`
            // —— **直接调 release()**（不是 lease.Close()）：每个保留引用都自己减一次 refs，
            // projectLease.Close() 的 once 只保护"这个 lease 自己被直接 Close"的那一次。
            lease.Release();
        }
    }
}

/// <summary>C# 侧新增（Go 用 <c>%p</c> 拼字符串做键）：按**引用身份**比较的规格键。</summary>
internal sealed class ProjectSpecKey : IEquatable<ProjectSpecKey>
{
    private readonly string _configFileName;
    private readonly CompilerOptions? _compilerOptions;
    private readonly Mapper[] _mappers;

    public ProjectSpecKey(ProjectSpec spec)
    {
        _configFileName = spec.ConfigFileName;
        _compilerOptions = spec.CompilerOptions;
        _mappers = spec.Mappers;
    }

    public bool Equals(ProjectSpecKey? other)
    {
        if (other is null || !string.Equals(_configFileName, other._configFileName, StringComparison.Ordinal) ||
            !ReferenceEquals(_compilerOptions, other._compilerOptions) ||
            _mappers.Length != other._mappers.Length)
        {
            return false;
        }

        for (var i = 0; i < _mappers.Length; i++)
        {
            if (!ReferenceEquals(_mappers[i], other._mappers[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ProjectSpecKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_configFileName, StringComparer.Ordinal);
        hash.Add(RuntimeHelpers.GetHashCode(_compilerOptions));
        foreach (var mapper in _mappers)
        {
            hash.Add(RuntimeHelpers.GetHashCode(mapper));
        }

        return hash.ToHashCode();
    }
}


/// <summary>C# 侧新增：读一个对象数组（Json 包的 List&lt;T&gt; 路径不创建元素实例，见 docs/json.md §2）。</summary>
internal static class ContentMapperJsonArrays
{
    internal static T[] ReadObjectArray<T>(JsonDecoder decoder)
        where T : class, IJsonUnmarshaler, new()
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return [];
        }

        decoder.ReadToken(); // [
        var list = new List<T>();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            list.Add(decoder.DecodeNew<T>());
        }

        decoder.ReadToken(); // ]
        return [.. list];
    }

    internal static T[]? ReadOptionalObjectArray<T>(JsonDecoder decoder)
        where T : class, IJsonUnmarshaler, new()
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return null;
        }

        return ReadObjectArray<T>(decoder);
    }
}

public static partial class ContentMapper
{
    /// <summary>Go: <c>func combinedIdentity(mapper *Mapper, configIdentity string, compilerOptions
    /// *core.CompilerOptions) string</c>。</summary>
    internal static string CombinedIdentity(Mapper mapper, string configIdentity, CompilerOptions? compilerOptions)
    {
        var transformIdentity = Xxh3.Xxh3.Bytes(mapper.TransformIdentity(compilerOptions));
        var identity = System.Text.Encoding.UTF8.GetBytes(mapper.Identity());
        var mapperOptions = mapper.Options ?? [];
        var config = System.Text.Encoding.UTF8.GetBytes(configIdentity);
        var buffer = new byte[identity.Length + 1 + mapperOptions.Length + 1 + config.Length + 1 + transformIdentity.Length];
        var offset = 0;
        identity.CopyTo(buffer, offset);
        offset += identity.Length;
        buffer[offset++] = 0;
        mapperOptions.CopyTo(buffer, offset);
        offset += mapperOptions.Length;
        buffer[offset++] = 0;
        config.CopyTo(buffer, offset);
        offset += config.Length;
        buffer[offset++] = 0;
        transformIdentity.CopyTo(buffer, offset);
        var hash = Xxh3.Xxh3.Bytes(Xxh3.Xxh3.Hash128(buffer));
        return mapper.Identity() + ":" + Convert.ToHexStringLower(hash);
    }

    /// <summary>Go: <c>func NewHost(ctx context.Context, spawner Spawner, diagnosticLocale locale.Locale) Host</c>。</summary>
    public static Host NewHost(Context ctx, Spawner spawner, Loc diagnosticLocale) =>
        NewHostWithOptions(ctx, spawner, diagnosticLocale, new HostOptions());

    /// <summary>Go: <c>func NewHostWithOptions(ctx context.Context, spawner Spawner, diagnosticLocale locale.Locale,
    /// options HostOptions) Host</c>。host 的生命周期绑定 ctx：取消 ctx（例如 CLI 的 SIGINT 信号 context、watch
    /// 会话结束）会拆掉每个 mapper 进程。</summary>
    public static Host NewHostWithOptions(Context ctx, Spawner spawner, Loc diagnosticLocale, HostOptions options)
    {
        var logger = options.Logger;
        var timing = new TimingCollector();
        return NewWithDial(ctx, diagnosticLocale, timing, (dialCtx, mapper, locale) =>
        {
            if (mapper.Exec.Length == 0)
            {
                throw new Exception($"content mapper \"{mapper.Package}\" declares no command to run");
            }

            var mapperTiming = timing.Mapper(mapper.Identity());
            var diagnosticName = mapper.DiagnosticName();
            var spawnStart = Stopwatch.GetTimestamp();
            Stream stderr = Stream.Null;
            StderrLogger? stderrLog = null;
            if (logger is not null)
            {
                stderrLog = new StderrLogger(diagnosticName, logger);
                stderr = stderrLog;
            }

            Stream rwc;
            try
            {
                rwc = spawner.Spawn(mapper.Exec, mapper.PackageDirectory, stderr);
            }
            catch (Exception e)
            {
                mapperTiming.Spawn.Record(spawnStart);
                throw InitializeError.Create(
                    InitializeErrorKind.ProcessStart,
                    mapperName: diagnosticName,
                    command: mapper.Exec[0],
                    detail: e.Message);
            }

            mapperTiming.Spawn.Record(spawnStart);
            if (stderrLog is not null)
            {
                rwc = new LoggedProcess(rwc, stderrLog);
            }

            rwc = new CloseOnceReadWriteCloser(rwc);
            Protocol protocol = JSONRPCProtocol.NewJSONRPCProtocol(rwc);
            if (logger is not null)
            {
                protocol = new LoggingProtocol(protocol, diagnosticName, logger);
            }

            var conn = AsyncConn.NewAsyncConnWithProtocol(rwc, protocol, new RejectHandler());

            // Go: `go func() { _ = conn.Run(ctx); _ = rwc.Close() }()`（读循环跑在专用线程上）。
            var runStream = rwc;
            var runCtx = dialCtx;
            _ = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        conn.Run(runCtx);
                    }
                    finally
                    {
                        runStream.Close();
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            using var initializeCts = CancellationTokenSource.CreateLinkedTokenSource(dialCtx.CancellationToken);
            initializeCts.CancelAfter(_initializeTimeout);
            var initializeCtx = WithCancellation(dialCtx, initializeCts.Token);
            var initializeStart = mapperTiming.StartRequest();
            PositionEncoding positionEncoding;
            string diagnosticSource;
            Exception? initializeError;
            try
            {
                (positionEncoding, diagnosticSource) = Handshake(initializeCtx, conn, locale);
                initializeError = null;
            }
            catch (Exception e)
            {
                initializeError = e;
                positionEncoding = default;
                diagnosticSource = "";
            }
            finally
            {
                mapperTiming.FinishRequest(mapperTiming.Initialize, initializeStart);
            }

            var initializeTimedOut = initializeCts.IsCancellationRequested;
            if (initializeError is not null)
            {
                var exitCode = 0;
                var exited = rwc is IProcessExitState state && state.TryExitCode(out exitCode);
                rwc.Close();
                if (initializeError is InitializeError protocolError)
                {
                    protocolError.MapperName = diagnosticName;
                    throw protocolError;
                }

                if (exited)
                {
                    throw InitializeError.Create(InitializeErrorKind.ProcessExit, mapperName: diagnosticName, exitCode: exitCode);
                }

                if (initializeTimedOut || initializeError is OperationCanceledException)
                {
                    throw InitializeError.Create(
                        InitializeErrorKind.NoResponse,
                        mapperName: diagnosticName,
                        timeoutSeconds: _initializeTimeoutSeconds);
                }

                throw InitializeError.Create(InitializeErrorKind.Request, mapperName: diagnosticName, detail: initializeError.Message);
            }

            return (conn, rwc, positionEncoding, diagnosticSource);
        });
    }

    internal static HostImpl NewWithDial(Context ctx, Loc diagnosticLocale, TimingCollector timing, DialFunc dial) =>
        new(ctx, diagnosticLocale, timing, dial);

    /// <summary>Go: <c>func handshake(ctx context.Context, conn ipc.Conn, diagnosticLocale locale.Locale)
    /// (PositionEncoding, string, error)</c>。</summary>
    internal static (PositionEncoding PositionEncoding, string DiagnosticSource) Handshake(Context ctx, Conn conn, Loc diagnosticLocale)
    {
        var raw = conn.Call(ctx, MethodInitialize, new InitializeParams
        {
            Locale = diagnosticLocale.String(),
            PositionEncodings = [PositionEncoding.UTF8, PositionEncoding.UTF16],
        });

        InitializeResult res;
        try
        {
            res = UnmarshalNew<InitializeResult>(raw ?? []);
        }
        catch (JsonException e)
        {
            throw InitializeError.Create(InitializeErrorKind.InvalidResponse, detail: e.Message);
        }

        if (res.PositionEncoding != PositionEncoding.UTF8 && res.PositionEncoding != PositionEncoding.UTF16)
        {
            throw InitializeError.Create(InitializeErrorKind.PositionEncoding, positionEncoding: res.PositionEncoding);
        }

        if (res.DiagnosticSource.Trim().Length == 0)
        {
            throw InitializeError.Create(InitializeErrorKind.EmptyDiagnosticSource);
        }

        if (EquateStringCaseInsensitive(res.DiagnosticSource, "typescript") || EquateStringCaseInsensitive(res.DiagnosticSource, "tsc"))
        {
            throw InitializeError.Create(InitializeErrorKind.ReservedDiagnosticSource, diagnosticSource: res.DiagnosticSource);
        }

        foreach (var extension in Flatten(AllSupportedExtensionsWithJson))
        {
            var name = extension.Length > 0 && extension[0] == '.' ? extension[1..] : extension;
            if (EquateStringCaseInsensitive(res.DiagnosticSource, name))
            {
                throw InitializeError.Create(InitializeErrorKind.ReservedDiagnosticSource, diagnosticSource: res.DiagnosticSource);
            }
        }

        return (res.PositionEncoding, res.DiagnosticSource);
    }

    /// <summary>Go: <c>func decodeTransformResult(raw json.Value, originalText string, positionEncoding
    /// PositionEncoding, diagnosticSource string) (Result, error)</c>。</summary>
    internal static Result DecodeTransformResult(byte[] raw, string originalText, PositionEncoding positionEncoding, string diagnosticSource)
    {
        TransformResult res;
        try
        {
            res = UnmarshalNew<TransformResult>(raw);
        }
        catch (JsonException e)
        {
            throw new Exception(e.Message, e);
        }

        var (mapped, originalPositions) = DecodeMappedOutput(res, originalText, positionEncoding, diagnosticSource);
        var result = new Result
        {
            Text = mapped.Text,
            VirtualExtension = mapped.VirtualExtension,
            Mappings = mapped.Mappings,
            DiagnosticDirectives = mapped.DiagnosticDirectives,
        };

        var supplemental = new List<MappedResult>();
        var sourceSupplemental = res.Supplemental ?? [];
        for (var i = 0; i < sourceSupplemental.Length; i++)
        {
            try
            {
                var (mappedSupplemental, _) = DecodeMappedOutput(sourceSupplemental[i], originalText, positionEncoding, diagnosticSource);
                supplemental.Add(mappedSupplemental);
            }
            catch (DiagnosticDirectiveError e)
            {
                e.SupplementalIndex = i;
                throw;
            }
        }

        result.Supplemental = [.. supplemental];
        var diagnostics = new List<TypeScript.Ast.Diagnostic>();
        foreach (var d in res.Diagnostics ?? [])
        {
            if (d.Start < 0 || d.Length < 0 || d.Start > int.MaxValue - d.Length)
            {
                throw new Exception($"invalid content mapper diagnostic range [{d.Start}, {d.Start + d.Length})");
            }

            int start;
            int end;
            try
            {
                start = originalPositions.Normalize(d.Start);
            }
            catch (Exception e)
            {
                throw new Exception($"invalid content mapper diagnostic start: {e.Message}", e);
            }

            try
            {
                end = originalPositions.Normalize(d.Start + d.Length);
            }
            catch (Exception e)
            {
                throw new Exception($"invalid content mapper diagnostic end: {e.Message}", e);
            }

            diagnostics.Add(NewExternalDiagnostic(
                null,
                NewTextRange(start, end),
                diagnosticSource,
                Category.Error,
                d.Code,
                d.MessageText));
        }

        result.Diagnostics = [.. diagnostics];
        return result;
    }

    /// <summary>Go: <c>func decodeMappedOutput(output MappedOutput, originalText string, positionEncoding
    /// PositionEncoding, diagnosticSource string) (MappedResult, *positionNormalizer, error)</c>。</summary>
    internal static (MappedResult Mapped, PositionNormalizer OriginalPositions) DecodeMappedOutput(
        MappedOutput output,
        string originalText,
        PositionEncoding positionEncoding,
        string diagnosticSource)
    {
        if (!IsSupportedVirtualExtension(output.Extension))
        {
            throw new InvalidVirtualExtensionError(output.Extension);
        }

        var result = new MappedResult
        {
            Text = output.Text,
            VirtualExtension = output.Extension,
        };

        var virtualPositions = new PositionNormalizer(output.Text, positionEncoding);
        var originalPositions = new PositionNormalizer(originalText, positionEncoding);

        // 成功的变换总带 span map。缺省/空映射表示输出完全合成（没有段对应原始内容），所以解码成空映射而不是
        // nil（nil 表示"不是 content-mapped"）。
        if (output.Mappings is { Length: > 0 })
        {
            SpanMap.SpanMap mappings;
            try
            {
                mappings = SpanMap.SpanMap.Unmarshal(output.Mappings);
            }
            catch (Exception e)
            {
                throw new Exception(e.Message, e);
            }

            result.Mappings = NormalizeMappings(mappings, virtualPositions, originalPositions);
        }
        else
        {
            result.Mappings = SpanMap.SpanMap.New(null);
        }

        result.DiagnosticDirectives = NormalizeDiagnosticDirectives(output.DiagnosticDirectives, virtualPositions, originalPositions, diagnosticSource);
        return (result, originalPositions);
    }

    /// <summary>Go: <c>func normalizeDiagnosticDirectives(diagnosticDirectives *DiagnosticDirectives, virtualPositions,
    /// originalPositions *positionNormalizer, diagnosticSource string) ([]ast.MappedDiagnosticDirective, error)</c>。</summary>
    internal static TypeScript.Ast.MappedDiagnosticDirective[] NormalizeDiagnosticDirectives(
        DiagnosticDirectives? diagnosticDirectives,
        PositionNormalizer virtualPositions,
        PositionNormalizer originalPositions,
        string diagnosticSource)
    {
        if (diagnosticDirectives is null)
        {
            return [];
        }

        var directives = diagnosticDirectives.Directives;
        var result = new TypeScript.Ast.MappedDiagnosticDirective[directives.Length];
        for (var i = 0; i < directives.Length; i++)
        {
            var directive = directives[i];
            DiagnosticDirectiveError DirectiveError(DiagnosticDirectiveErrorKind kind) => new(kind, i, -1);

            var normalized = new TypeScript.Ast.MappedDiagnosticDirective { Source = diagnosticSource };
            switch (directive.Policy)
            {
                case DiagnosticDirectivePolicy.Ignore:
                    normalized.Policy = MappedDiagnosticDirectivePolicy.Ignore;
                    break;
                case DiagnosticDirectivePolicy.Expect:
                    var unusedDiagnosticIndex = 0;
                    if (directive.UnusedExpectDirectiveIndex is { } explicitIndex)
                    {
                        unusedDiagnosticIndex = explicitIndex;
                    }
                    else if (diagnosticDirectives.UnusedExpectDirectiveDiagnostics.Length != 1)
                    {
                        throw DirectiveError(DiagnosticDirectiveErrorKind.ExpectMissingUnusedDiagnostic);
                    }

                    if (unusedDiagnosticIndex < 0 || unusedDiagnosticIndex >= diagnosticDirectives.UnusedExpectDirectiveDiagnostics.Length)
                    {
                        throw DirectiveError(DiagnosticDirectiveErrorKind.InvalidUnusedDiagnosticIndex);
                    }

                    var unusedDiagnostic = diagnosticDirectives.UnusedExpectDirectiveDiagnostics[unusedDiagnosticIndex];
                    normalized.Policy = MappedDiagnosticDirectivePolicy.Expect;
                    normalized.UnusedCode = unusedDiagnostic.Code;
                    normalized.UnusedMessageText = unusedDiagnostic.MessageText;
                    break;
                default:
                    var validationError = DirectiveError(DiagnosticDirectiveErrorKind.InvalidPolicy);
                    validationError.Policy = directive.Policy;
                    throw validationError;
            }

            if (directive.VirtualStart < 0 || directive.VirtualEnd < directive.VirtualStart)
            {
                throw DirectiveError(DiagnosticDirectiveErrorKind.InvalidRange);
            }

            int virtualStart;
            int virtualEnd;
            try
            {
                virtualStart = virtualPositions.Normalize(directive.VirtualStart);
                virtualEnd = virtualPositions.Normalize(directive.VirtualEnd);
            }
            catch (Exception)
            {
                throw DirectiveError(DiagnosticDirectiveErrorKind.InvalidRange);
            }

            normalized.VirtualRange = NewTextRange(virtualStart, virtualEnd);
            var validOriginalRange = directive.OriginalStart >= 0 && directive.OriginalLength >= 0 &&
                                     directive.OriginalStart <= int.MaxValue - directive.OriginalLength;
            if (validOriginalRange)
            {
                try
                {
                    var originalStart = originalPositions.Normalize(directive.OriginalStart);
                    var originalEnd = originalPositions.Normalize(directive.OriginalStart + directive.OriginalLength);
                    normalized.OriginalRange = NewTextRange(originalStart, originalEnd);
                }
                catch (Exception)
                {
                    validOriginalRange = false;
                }
            }

            if (normalized.Policy == MappedDiagnosticDirectivePolicy.Expect && !validOriginalRange)
            {
                throw DirectiveError(DiagnosticDirectiveErrorKind.InvalidRange);
            }

            result[i] = normalized;
        }

        // Go 的 slices.SortFunc（稳定排序 + 按虚拟起点）后检查重叠。
        var sorted = result
            .Select((directive, index) => (Directive: directive, Index: index))
            .OrderBy(entry => entry.Directive.VirtualRange.Pos())
            .ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].Directive.VirtualRange.Pos() < sorted[i - 1].Directive.VirtualRange.End())
            {
                throw new DiagnosticDirectiveError(DiagnosticDirectiveErrorKind.Overlap, sorted[i].Index, -1);
            }
        }

        return result;
    }

    /// <summary>Go: <c>func normalizeMappings(mappings *spanmap.SpanMap, virtualPositions *positionNormalizer,
    /// originalPositions *positionNormalizer) (*spanmap.SpanMap, error)</c>。</summary>
    internal static SpanMap.SpanMap NormalizeMappings(
        SpanMap.SpanMap mappings,
        PositionNormalizer virtualPositions,
        PositionNormalizer originalPositions)
    {
        var segments = mappings.Segments();
        for (var i = 0; i < segments.Length; i++)
        {
            ref var segment = ref segments[i];
            // Go 逐个字段 wrap（hostimpl.go:1291-1315），错误文本逐字对应。
            segment.VirtualStart = NormalizeMappingPos(i, "virtual start", segment.VirtualStart, virtualPositions.NormalizeTextPos);
            segment.VirtualEnd = NormalizeMappingPos(i, "virtual end", segment.VirtualEnd, virtualPositions.NormalizeTextPos);
            segment.OriginalStart = NormalizeMappingPos(i, "original start", segment.OriginalStart, originalPositions.NormalizeTextPos);
            segment.OriginalEnd = NormalizeMappingPos(i, "original end", segment.OriginalEnd, originalPositions.NormalizeTextPos);
        }

        return SpanMap.SpanMap.New(segments);
    }

    private static TextPos NormalizeMappingPos(int index, string field, TextPos position, Func<TextPos, TextPos> normalize)
    {
        try
        {
            return normalize(position);
        }
        catch (Exception e)
        {
            throw new Exception($"invalid content mapper mapping {index} {field}: {e.Message}", e);
        }
    }
}

/// <summary>
/// Go: <c>type positionNormalizer struct</c>。把 mapper 报告的坐标（按 initialize 协商的 positionEncoding）
/// 换算成 C#/TS 内部使用的 UTF-16 代码单元坐标，并拒绝落在 Unicode 码点中间的偏移。
/// </summary>
/// <remarks>
/// Go 用 <c>ast.ComputePositionMap</c>（UTF-8 ↔ UTF-16 全表）做换算；本移植的 <c>PositionMap</c> 是恒等映射
/// （docs/architecture.md §3.1：C# 内部一律 UTF-16），所以这里自己建"发散点"表：只记录**非 ASCII 码点**
/// （UTF-8 长度 ≠ UTF-16 长度）的偏移，ASCII 区间内外都是 1:1，用二分查找 + 偏移即可精确换算，且不随文本长度线性占内存。
/// </remarks>
internal sealed class PositionNormalizer
{
    private readonly string _text;
    private readonly PositionEncoding _encoding;
    private readonly int _length;
    private readonly int[] _divergenceUtf8Offset;
    private readonly int[] _divergenceUtf16Index;
    private readonly int[] _divergenceUtf8Length;
    private readonly int[] _divergenceUtf16Length;

    internal PositionNormalizer(string text, PositionEncoding encoding)
    {
        _text = text;
        _encoding = encoding;

        if (encoding == PositionEncoding.UTF8)
        {
            List<int> offsets = [];
            List<int> indices = [];
            List<int> byteLengths = [];
            List<int> unitLengths = [];
            var utf8Offset = 0;
            var utf16Index = 0;
            while (utf16Index < text.Length)
            {
                var c = text[utf16Index];
                int utf16Length;
                int utf8Length;
                if (char.IsHighSurrogate(c) && utf16Index + 1 < text.Length && char.IsLowSurrogate(text[utf16Index + 1]))
                {
                    utf16Length = 2;
                    utf8Length = 4;
                }
                else
                {
                    utf16Length = 1;
                    // 与 Encoding.UTF8 一致：孤立代理项按替换字符 U+FFFD 编码成 3 字节。
                    utf8Length = c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                }

                if (utf8Length != utf16Length)
                {
                    offsets.Add(utf8Offset);
                    indices.Add(utf16Index);
                    byteLengths.Add(utf8Length);
                    unitLengths.Add(utf16Length);
                }

                utf8Offset += utf8Length;
                utf16Index += utf16Length;
            }

            _divergenceUtf8Offset = [.. offsets];
            _divergenceUtf16Index = [.. indices];
            _divergenceUtf8Length = [.. byteLengths];
            _divergenceUtf16Length = [.. unitLengths];
            _length = utf8Offset;
        }
        else
        {
            _divergenceUtf8Offset = [];
            _divergenceUtf16Index = [];
            _divergenceUtf8Length = [];
            _divergenceUtf16Length = [];
            _length = text.Length;
        }
    }

    internal TextPos NormalizeTextPos(TextPos position) => new(Normalize(position.Value));

    /// <summary>Go: <c>func (n *positionNormalizer) normalize(position int) (int, error)</c>（UTF-16 索引）。</summary>
    internal int Normalize(int position)
    {
        if (position < 0)
        {
            throw new Exception($"position {position} is negative");
        }

        if (position > _length)
        {
            throw new Exception($"position {position} exceeds {_encoding.String()} length {_length}");
        }

        if (_encoding != PositionEncoding.UTF8)
        {
            // UTF-16 坐标：位置落在代理对中间就是切开了码点。
            if (position < _text.Length && char.IsLowSurrogate(_text[position]) && position > 0 && char.IsHighSurrogate(_text[position - 1]))
            {
                throw new Exception($"position {position} splits a Unicode code point");
            }

            return position;
        }

        // 二分找最后一个 utf8Offset <= position 的发散点。
        var low = 0;
        var high = _divergenceUtf8Offset.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (_divergenceUtf8Offset[mid] <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (found < 0)
        {
            // ASCII 前缀：字节偏移 == 代码单元偏移。
            return position;
        }

        var start = _divergenceUtf8Offset[found];
        if (position < start + _divergenceUtf8Length[found])
        {
            if (position == start)
            {
                return _divergenceUtf16Index[found];
            }

            throw new Exception($"position {position} splits a Unicode code point");
        }

        return _divergenceUtf16Index[found] + _divergenceUtf16Length[found] + (position - (start + _divergenceUtf8Length[found]));
    }
}

