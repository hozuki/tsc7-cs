// ported from: internal/api/proto.go:746-1920（`ParseConfigFileParams` 起、到 `DiagnosticSourceLineResponse` 止的**全部类型声明**）
//
// 本文件只搬 proto.go 这一段里的**类型**（`type X struct` 与 `type ImportAdderActionKind string`）。同区间的函数
// （jsonValueToAny、toProtocolJSONValue、NewConfigFileResponse、NewProjectResponse、NewPackageId、symbolHandles、
// typeHandles、newTypeResponse、literalValueToJSON、diagnosticSourceLines）连同 `unmarshalers` 表与
// `BatchRequestsResponse.MarshalJSONTo` 都归 Api/Proto.cs（集成者持有），本文件不重复定义。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **JSON 编解码**：Go 靠 reflect 读 struct tag；C# AOT 禁反射（契约 §1.1），所以每个类都实现
//    IJsonMarshaler/IJsonUnmarshaler，并用 LsProto 的显式字段表 StructSpec/StructFieldSpec（契约 §3 的模板）。
//    字段声明顺序 = Go 字段顺序 = 编码顺序；JSON 名 = Go 的 tag 名（去掉选项后的部分）。
// 2) **required 一律 false**：API 的 json tag 没有 `lsp:"required"`，Go 缺字段时保持零值，C# 同理 ——
//    字段的 CLR 默认值就是 Go 零值（string ""、bool false、数值 0、切片 []、指针 null）。
// 3) **rejectNull 一律 false**（**与契约 §3 的建议相反，理由如下**）：契约 §3 把 lsproto structcodec 的 LSP
//    严格语义（非 optional 的指针/切片/map 字段拒绝显式 null）推广到了整个 API 包，但 API 包的 Go 侧用的是
//    std `encoding/json/v2`（internal/json 只是 99 行薄包装），它对 nilable 种类（指针/切片/map/接口）的显式
//    JSON null **是接受的**（置 nil），证据：`internal/api/session_createprogram_test.go:186/222` 用
//    `json.Unmarshal([]byte(`{"reconfigurePrograms":[null]}`), &v)` 并断言 **NilError**（把它解成 nil 元素，
//    由 session 层再报 "must not be null"）。所以 true 会让 C# 服务端拒掉 Go 侧合法的请求（例如 Go 客户端
//    把没有 omitempty 的 nil 切片 marshal 成 `"requests": null`）。这里统一 false，解码体里对 nilable 字段
//    显式处理 null（置 null，= Go 的 nil），值类型字段遇 null 则保持零值（Go 侧不会这么发）。
//    注意：`nonnil:"true"` 在 TSGO 里只是**文档性** tag（internal/json 完全没有处理它），所以是 no-op；
//    但它标记的字段类型在 C# 里落成非空（`T` 而不是 `T?`）。
// 4) **omitempty / omitzero**（契约 §3）：omitempty 在 string==""、数值==0、bool==false、指针==null、
//    切片==null 或 len==0、object?==null 时跳过；omitzero 在 Go 零值时跳过（ID 的 `Value==0`、NodeHandle 的
//    `Value==""`、bool==false）；非 omit 的切片一律写出：null 时写 `[]`（先例 LsProto 的 ConfigurationParams）。
// 5) **`Method`**：Go 的 `type Method string` 在 C# 是 `static class Method` + const string（Api.cs 文件头
//    第 1 条），所以 `BatchRequest.Method`/`BatchResponse.Method` 的字段类型是 `string`。
// 6) **`any` → `object?`**：`ConfigFileResponse.Raw`/`ReadConfigFileResponse.Config`/`BatchResponse.Result`/
//    `TypeResponse.Value`/`ConstantValueResponse.Value` 都是 `object?`。Json 编码器按动态类型分派
//    （IJsonMarshaler / IDictionary / IEnumerable / 标量）；**注意 `byte[]` 会被编码成 base64**，所以这些字段
//    里若装的是“原始 JSON 字节”，必须由集成者用 `Encoder.WriteValue(...)` 的路子收口（本仓 Json 命名空间
//    目前没有 RawJson 包装类型，已在 yield 的不确定点里登记）。
// 7) **`json.Value` → `byte[]`**：`BatchRequest.Params` 是原始 JSON 字节（Go 的 json.Value 就是 []byte）；
//    解码用 `dec.ReadValue()`（原样读出、连引号一起），编码用 `enc.WriteValue(t.Params)`（原样透传）。
// 8) **ID / 句柄类型**：Go 的 `SnapshotID uint64`/`SymbolID uint64`/`TypeID uint32`/`SignatureID uint64`/
//    `SourceFileLeaseID uint64`/`BuildOrchestratorID uint64`/`NodeHandle string` 是 Api.cs 的 readonly struct
//    （都实现 IJsonMarshaler）；解码一律 `new X(dec.Decode<ulong|uint>())`，编码 `MarshalEncode(enc, t.X)`。
// 9) **`project.ID` 与 `core.UTF16Offset`**：Go 里分别是 `type ID string` 与 `type UTF16Offset int`，JSON 里是
//    裸 string/number。C# 的这两个类型目前**没有** IJsonMarshaler（Project/Core 分片），所以本文件对它们直接
//    读写底层值（`enc.WriteStringValue(t.X.Value)` / `new Project.ID(dec.Decode<string>() ?? "")`、
//    `MarshalEncode(enc, t.X.Value)` / `new Core.UTF16Offset(dec.Decode<int>())`），不走 MarshalEncode。
// 10) **`CreateBuildOrchestratorParams` 的嵌入指针**：Go 内嵌 `*core.BuildOptions`/`*core.CompilerOptions`
//    （带 json tag，字段名靠 tag）。C# 没有字段提升 → 落成两个普通 public 字段 `BuildOptions`/`CompilerOptions`
//    （类型 `Core.BuildOptions?`/`Core.CompilerOptions?`），JSON 名取自 tag（`buildOptions`/`compilerOptions`）。
// 11) **`BuildOrchestrator` 不参与 JSON**（Go 是三个 `func(project string) tsc.ExitStatus` 字段的纯内部结构）：
//    落成 `public sealed class BuildOrchestrator` 的三个 `Func<string, Execute.Tsc.ExitStatus>` 字段，不实现
//    IJsonMarshaler/IJsonUnmarshaler，也没有 Spec。
// 12) **`BatchRequestsResponse.encodedResponses`**（未导出 `[]json.Value`，只给手写的 MarshalJSONTo 用）→
//    `public byte[][]? EncodedResponses;`，**不进** Spec（Go 的 tag 路径也看不见它）。
//    `BatchRequestsResponse.MarshalJSONTo` 由 Api/Proto.cs 手写（本文件只提供字段 + 读路径的 Spec）。
// 13) **`ImportAdderActionKind`**（`type ImportAdderActionKind string`）→ 照 LsProto 的 FoldingRangeKind
//    （readonly struct + 静态单例，JSON 是字符串，见 Lsp.Generated.Enums.cs）。
// 14) **`core.ResolutionMode`**：C# 侧 `type ResolutionMode = ModuleKind` 是类型别名（Core/CompilerOptions.cs
//    文件头第 6 条），所以本文件里凡是 `core.ResolutionMode` 都写 `Core.ModuleKind`。
// 15) **`DiagnosticResponse` 自引用**（MessageChain/RelatedInformation 是 `[]*DiagnosticResponse`）：C# 直接
//    引用自身，解码用 `DecodeArray<DiagnosticResponse?>` + `DecodeNullable`（Go 的元素是指针，可为 JSON null）。
// 16) **已知外部缺口（不是本文件的职责，见 yield 的不确定点）**：`Core.CompilerOptions`/`Core.BuildOptions`/
//    `Core.TypeAcquisition`/`Core.ProjectReference`/`Execute.Tsc.Statistics` 与 `PackageJson.JSONValue` 的
//    **写侧**目前都没有 IJsonMarshaler（对应分片的文件头写着“本轮不实现映射”）→ 本文件按 Go 的类型声明这些
//    字段，运行时需要那些分片补上 JSON 映射，否则 Json 编码器会抛 "must implement IJsonMarshaler"。
using TypeScript.Json;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;
using StructSpec = TypeScript.LsProto.StructSpec;
using StructFieldSpec = TypeScript.LsProto.StructFieldSpec;

namespace TypeScript.Api;

/// <summary>Go: <c>type ParseConfigFileParams struct</c>（proto.go:746）。</summary>
public sealed class ParseConfigFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:747）。</summary>
    public DocumentIdentifier File = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (ParseConfigFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ParseConfigFileParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ParseCommandLineParams struct</c>（proto.go:750）。</summary>
public sealed class ParseCommandLineParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>CommandLine []string `json:"commandLine"`</c>（proto.go:751）。无 omit → 总是写出。</summary>
    public string[] CommandLine = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("commandLine", false, false,
            static (target, dec) =>
            {
                var t = (ParseCommandLineParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CommandLine = null!; }
                else { t.CommandLine = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "")!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ParseCommandLineParams)target;
            enc.WriteToken(JsonToken.String("commandLine"));
            WriteStringArray(enc, t.CommandLine);
        },
    ]);

    /// <summary>Go 的 `json.MarshalEncode(enc, []string)`：C# 侧手写等价编码（本类只用于这一处）。</summary>
    private static void WriteStringArray(JsonEncoder enc, string[]? values)
    {
        enc.WriteToken(BeginArray);
        if (values is not null)
        {
            foreach (var value in values)
            {
                MarshalEncode(enc, value);
            }
        }

        enc.WriteToken(EndArray);
    }

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ReadConfigFileParams struct</c>（proto.go:754）。</summary>
public sealed class ReadConfigFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:755）。</summary>
    public DocumentIdentifier File = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (ReadConfigFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ReadConfigFileParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ParseJsonConfigFileContentParams struct</c>（proto.go:758）。</summary>
public sealed class ParseJsonConfigFileContentParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>JSON packagejson.JSONValue `json:"json"`</c>（proto.go:759）。Go 的值结构体 → C# 的 class 字段。
    /// 注意：`PackageJson.JSONValue` 目前只实现 IJsonUnmarshaler（读侧），写侧需要 PackageJson 分片补映射（文件头第 16 条）。</summary>
    public PackageJson.JSONValue JSON = new();

    /// <summary>Go: <c>ConfigDirectory *string `json:"configDirectory,omitempty"`</c>（proto.go:760）。</summary>
    public string? ConfigDirectory;

    /// <summary>Go: <c>ConfigFileName *DocumentIdentifier `json:"configFileName,omitempty"`</c>（proto.go:761）。</summary>
    public DocumentIdentifier? ConfigFileName;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("json", false, false,
            static (target, dec) =>
            {
                var t = (ParseJsonConfigFileContentParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.JSON.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("configDirectory", false, false,
            static (target, dec) =>
            {
                var t = (ParseJsonConfigFileContentParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ConfigDirectory = null; }
                else { t.ConfigDirectory = dec.Decode<string>(); }
            }
        ),
        new StructFieldSpec("configFileName", false, false,
            static (target, dec) =>
            {
                var t = (ParseJsonConfigFileContentParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ConfigFileName = null; }
                else { t.ConfigFileName = DecodeNullable<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ParseJsonConfigFileContentParams)target;
            enc.WriteToken(JsonToken.String("json"));
            MarshalEncode(enc, t.JSON);
        },
        static (target, enc) =>
        {
            var t = (ParseJsonConfigFileContentParams)target;
            if (t.ConfigDirectory is null) { return; }
            enc.WriteToken(JsonToken.String("configDirectory"));
            MarshalEncode(enc, t.ConfigDirectory);
        },
        static (target, enc) =>
        {
            var t = (ParseJsonConfigFileContentParams)target;
            if (t.ConfigFileName is null) { return; }
            enc.WriteToken(JsonToken.String("configFileName"));
            MarshalEncode(enc, t.ConfigFileName);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TranspileOptions struct</c>（proto.go:789）。</summary>
public sealed class TranspileOptions : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>CompilerOptions *core.CompilerOptions `json:"compilerOptions,omitempty"`</c>（proto.go:790）。
    /// `Core.CompilerOptions` 目前没有 JSON 映射（文件头第 16 条）。</summary>
    public Core.CompilerOptions? CompilerOptions;

    /// <summary>Go: <c>FileName string `json:"fileName,omitempty"`</c>（proto.go:791）。</summary>
    public string FileName = "";

    /// <summary>Go: <c>ReportDiagnostics bool `json:"reportDiagnostics,omitempty"`</c>（proto.go:792）。</summary>
    public bool ReportDiagnostics;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("compilerOptions", false, false,
            static (target, dec) =>
            {
                var t = (TranspileOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CompilerOptions = null; }
                else { t.CompilerOptions = DecodeNullable<Core.CompilerOptions>(dec, static d => d.DecodeNew<Core.CompilerOptions>()); }
            }
        ),
        new StructFieldSpec("fileName", false, false,
            static (target, dec) =>
            {
                var t = (TranspileOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("reportDiagnostics", false, false,
            static (target, dec) =>
            {
                var t = (TranspileOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ReportDiagnostics = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TranspileOptions)target;
            if (t.CompilerOptions is not null)
            {
                enc.WriteToken(JsonToken.String("compilerOptions"));
                MarshalEncode(enc, t.CompilerOptions);
            }
        },
        static (target, enc) =>
        {
            var t = (TranspileOptions)target;
            if (t.FileName.Length != 0)
            {
                enc.WriteToken(JsonToken.String("fileName"));
                MarshalEncode(enc, t.FileName);
            }
        },
        static (target, enc) =>
        {
            var t = (TranspileOptions)target;
            if (!t.ReportDiagnostics)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("reportDiagnostics"));
            MarshalEncode(enc, t.ReportDiagnostics);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateSourceFileOptions struct</c>（proto.go:795）。</summary>
public sealed class CreateSourceFileOptions : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ScriptKind core.ScriptKind `json:"scriptKind,omitempty"`</c>（proto.go:796）。</summary>
    public Core.ScriptKind ScriptKind;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("scriptKind", false, false,
            static (target, dec) =>
            {
                var t = (CreateSourceFileOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ScriptKind = dec.Decode<Core.ScriptKind>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateSourceFileOptions)target;
            if (t.ScriptKind == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("scriptKind"));
            MarshalEncode(enc, t.ScriptKind);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateSourceFileParams struct</c>（proto.go:799）。</summary>
public sealed class CreateSourceFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>FileName string `json:"fileName"`</c>（proto.go:800）。</summary>
    public string FileName = "";

    /// <summary>Go: <c>SourceText string `json:"sourceText"`</c>（proto.go:801）。</summary>
    public string SourceText = "";

    /// <summary>Go: <c>Options CreateSourceFileOptions `json:"options"`</c>（proto.go:802）。</summary>
    public CreateSourceFileOptions Options = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("fileName", false, false,
            static (target, dec) =>
            {
                var t = (CreateSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("sourceText", false, false,
            static (target, dec) =>
            {
                var t = (CreateSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SourceText = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("options", false, false,
            static (target, dec) =>
            {
                var t = (CreateSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Options.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateSourceFileParams)target;
            enc.WriteToken(JsonToken.String("fileName"));
            MarshalEncode(enc, t.FileName);
        },
        static (target, enc) =>
        {
            var t = (CreateSourceFileParams)target;
            enc.WriteToken(JsonToken.String("sourceText"));
            MarshalEncode(enc, t.SourceText);
        },
        static (target, enc) =>
        {
            var t = (CreateSourceFileParams)target;
            enc.WriteToken(JsonToken.String("options"));
            MarshalEncode(enc, t.Options);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateSourceFileFromFileParams struct</c>（proto.go:805）。</summary>
public sealed class CreateSourceFileFromFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>FileName string `json:"fileName"`</c>（proto.go:806）。</summary>
    public string FileName = "";

    /// <summary>Go: <c>Options CreateSourceFileOptions `json:"options"`</c>（proto.go:807）。</summary>
    public CreateSourceFileOptions Options = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("fileName", false, false,
            static (target, dec) =>
            {
                var t = (CreateSourceFileFromFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("options", false, false,
            static (target, dec) =>
            {
                var t = (CreateSourceFileFromFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Options.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateSourceFileFromFileParams)target;
            enc.WriteToken(JsonToken.String("fileName"));
            MarshalEncode(enc, t.FileName);
        },
        static (target, enc) =>
        {
            var t = (CreateSourceFileFromFileParams)target;
            enc.WriteToken(JsonToken.String("options"));
            MarshalEncode(enc, t.Options);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TranspileParams struct</c>（proto.go:810）。</summary>
public sealed class TranspileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Input string `json:"input"`</c>（proto.go:811）。</summary>
    public string Input = "";

    /// <summary>Go: <c>Options TranspileOptions `json:"options"`</c>（proto.go:812）。</summary>
    public TranspileOptions Options = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("input", false, false,
            static (target, dec) =>
            {
                var t = (TranspileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Input = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("options", false, false,
            static (target, dec) =>
            {
                var t = (TranspileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Options.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TranspileParams)target;
            enc.WriteToken(JsonToken.String("input"));
            MarshalEncode(enc, t.Input);
        },
        static (target, enc) =>
        {
            var t = (TranspileParams)target;
            enc.WriteToken(JsonToken.String("options"));
            MarshalEncode(enc, t.Options);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TranspileFromFileParams struct</c>（proto.go:815）。</summary>
public sealed class TranspileFromFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>FileName string `json:"fileName"`</c>（proto.go:816）。</summary>
    public string FileName = "";

    /// <summary>Go: <c>Options TranspileOptions `json:"options"`</c>（proto.go:817）。</summary>
    public TranspileOptions Options = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("fileName", false, false,
            static (target, dec) =>
            {
                var t = (TranspileFromFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("options", false, false,
            static (target, dec) =>
            {
                var t = (TranspileFromFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Options.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TranspileFromFileParams)target;
            enc.WriteToken(JsonToken.String("fileName"));
            MarshalEncode(enc, t.FileName);
        },
        static (target, enc) =>
        {
            var t = (TranspileFromFileParams)target;
            enc.WriteToken(JsonToken.String("options"));
            MarshalEncode(enc, t.Options);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TranspileOutputResponse struct</c>（proto.go:820）。</summary>
public sealed class TranspileOutputResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>OutputText string `json:"outputText"`</c>（proto.go:821）。</summary>
    public string OutputText = "";

    /// <summary>Go: <c>Diagnostics []*DiagnosticResponse `json:"diagnostics,omitempty"`</c>（proto.go:822）。</summary>
    public DiagnosticResponse[]? Diagnostics;

    /// <summary>Go: <c>SourceMapText string `json:"sourceMapText,omitempty"`</c>（proto.go:823）。</summary>
    public string SourceMapText = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("outputText", false, false,
            static (target, dec) =>
            {
                var t = (TranspileOutputResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.OutputText = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("diagnostics", false, false,
            static (target, dec) =>
            {
                var t = (TranspileOutputResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Diagnostics = null; }
                else { t.Diagnostics = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
        new StructFieldSpec("sourceMapText", false, false,
            static (target, dec) =>
            {
                var t = (TranspileOutputResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SourceMapText = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TranspileOutputResponse)target;
            enc.WriteToken(JsonToken.String("outputText"));
            MarshalEncode(enc, t.OutputText);
        },
        static (target, enc) =>
        {
            var t = (TranspileOutputResponse)target;
            if (t.Diagnostics is null || t.Diagnostics.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("diagnostics"));
            MarshalEncode(enc, t.Diagnostics);
        },
        static (target, enc) =>
        {
            var t = (TranspileOutputResponse)target;
            if (t.SourceMapText.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("sourceMapText"));
            MarshalEncode(enc, t.SourceMapText);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type BatchRequestsParams struct</c>（proto.go:826）。</summary>
public sealed class BatchRequestsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Requests []BatchRequest `json:"requests"`</c>（proto.go:827）。</summary>
    public BatchRequest[] Requests = [];

    /// <summary>Go: <c>ContinuationToken string `json:"continuationToken,omitempty"`</c>（proto.go:828）。</summary>
    public string ContinuationToken = "";

    /// <summary>Go: <c>MaxResponseBytesPerPage int `json:"maxResponseBytesPerPage,omitempty"`</c>（proto.go:829）。</summary>
    public int MaxResponseBytesPerPage;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("requests", false, false,
            static (target, dec) =>
            {
                var t = (BatchRequestsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Requests = null!; }
                else { t.Requests = DecodeArray<BatchRequest>(dec, static d => d.DecodeNew<BatchRequest>())!; }
            }
        ),
        new StructFieldSpec("continuationToken", false, false,
            static (target, dec) =>
            {
                var t = (BatchRequestsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ContinuationToken = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("maxResponseBytesPerPage", false, false,
            static (target, dec) =>
            {
                var t = (BatchRequestsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.MaxResponseBytesPerPage = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (BatchRequestsParams)target;
            enc.WriteToken(JsonToken.String("requests"));
            if (t.Requests is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Requests)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (BatchRequestsParams)target;
            if (t.ContinuationToken.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("continuationToken"));
            MarshalEncode(enc, t.ContinuationToken);
        },
        static (target, enc) =>
        {
            var t = (BatchRequestsParams)target;
            if (t.MaxResponseBytesPerPage == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("maxResponseBytesPerPage"));
            MarshalEncode(enc, t.MaxResponseBytesPerPage);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type BatchRequest struct</c>（proto.go:832）。</summary>
public sealed class BatchRequest : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Method Method `json:"method"`</c>（proto.go:833）。Go 的 <c>Method</c> 是 string 新类型，
    /// C# 是 <c>Method</c> 静态类 + const string（Api.cs 文件头第 1 条），故字段类型是 <c>string</c>。</summary>
    public string Method = "";

    /// <summary>Go: <c>Params json.Value `json:"params,omitempty"`</c>（proto.go:834）：原始 JSON 字节，原样透传。</summary>
    public byte[]? Params;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("method", false, false,
            static (target, dec) =>
            {
                var t = (BatchRequest)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Method = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("params", false, false,
            static (target, dec) =>
            {
                var t = (BatchRequest)target;
                t.Params = dec.ReadValue();
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (BatchRequest)target;
            enc.WriteToken(JsonToken.String("method"));
            MarshalEncode(enc, t.Method);
        },
        static (target, enc) =>
        {
            var t = (BatchRequest)target;
            if (t.Params is null || t.Params.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("params"));
            enc.WriteValue(t.Params);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type BatchRequestsResponse struct</c>（proto.go:837）。
/// 写路径是 Go 手写的 <c>MarshalJSONTo</c>（proto.go:845-880），由 Api/Proto.cs 移植；本文件只提供字段与读路径的 Spec。</summary>
public sealed class BatchRequestsResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Responses []BatchResponse `json:"responses" nonnil:"true"`</c>（proto.go:838）。</summary>
    public BatchResponse[] Responses = [];

    /// <summary>Go: <c>ContinuationToken string `json:"continuationToken,omitempty"`</c>（proto.go:839）。</summary>
    public string ContinuationToken = "";

    /// <summary>Go: <c>encodedResponses []json.Value</c>（proto.go:840，未导出）：手写 <c>MarshalJSONTo</c> 用它写出
    /// 已经序列化好的响应体（避免二次编码）。**不进 Spec**（Go 的 tag 路径也看不到它）。</summary>
    public byte[][]? EncodedResponses;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("responses", false, false,
            static (target, dec) =>
            {
                var t = (BatchRequestsResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Responses = null!; }
                else { t.Responses = DecodeArray<BatchResponse>(dec, static d => d.DecodeNew<BatchResponse>())!; }
            }
        ),
        new StructFieldSpec("continuationToken", false, false,
            static (target, dec) =>
            {
                var t = (BatchRequestsResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ContinuationToken = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (BatchRequestsResponse)target;
            enc.WriteToken(JsonToken.String("responses"));
            if (t.Responses is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Responses)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (BatchRequestsResponse)target;
            if (t.ContinuationToken.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("continuationToken"));
            MarshalEncode(enc, t.ContinuationToken);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    /// <summary>Go: <c>func (r *BatchRequestsResponse) MarshalJSONTo(enc *json.Encoder) error</c>（proto.go:845-880）。
    /// 逐行直译（不走 Spec）：`EncodedResponses` 非 nil 时写出已经序列化好的响应体（分页），否则逐条编码
    /// <c>Responses</c>；`ContinuationToken` 为空时不写。</summary>
    public void MarshalJSONTo(JsonEncoder enc)
    {
        enc.WriteToken(BeginObject);
        // Go 用 `enc.WriteValue(json.Value(`"responses"`))` 写**属性名**（encoding/json/v2 的 WriteValue 允许在
        // 名字位置写一个已经编码好的字符串 token）；C# 的 `Encoder.WriteValue` 会做状态检查
        // （`EnsureCanWriteValue` → "cannot write a value when a property name is expected"），
        // 所以名字改用与生成代码同一形状的 `WriteToken(JsonToken.String(...))`（先例：本类的 Spec 编码器）。
        enc.WriteToken(JsonToken.String("responses"));
        enc.WriteToken(BeginArray);
        if (EncodedResponses is not null)
        {
            foreach (var response in EncodedResponses)
            {
                enc.WriteValue(response);
            }
        }
        else
        {
            for (var i = 0; i < Responses.Length; i++)
            {
                MarshalEncode(enc, Responses[i]);
            }
        }

        enc.WriteToken(EndArray);
        if (ContinuationToken.Length != 0)
        {
            // 见上面 "responses" 的说明：名字位置不能用 WriteValue。
            enc.WriteToken(JsonToken.String("continuationToken"));
            MarshalEncode(enc, ContinuationToken);
        }

        enc.WriteToken(EndObject);
    }
}

/// <summary>Go: <c>type BatchResponse struct</c>（proto.go:882）。</summary>
public sealed class BatchResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Method Method `json:"method"`</c>（proto.go:883）。</summary>
    public string Method = "";

    /// <summary>Go: <c>Result any `json:"result"`</c>（proto.go:884）。见文件头第 6 条（原始 JSON 需集成者收口）。</summary>
    public object? Result;

    /// <summary>Go: <c>Error string `json:"error,omitempty"`</c>（proto.go:885）。</summary>
    public string Error = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("method", false, false,
            static (target, dec) =>
            {
                var t = (BatchResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Method = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("result", false, false,
            static (target, dec) =>
            {
                var t = (BatchResponse)target;
                t.Result = dec.Decode<object?>();
            }
        ),
        new StructFieldSpec("error", false, false,
            static (target, dec) =>
            {
                var t = (BatchResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Error = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (BatchResponse)target;
            enc.WriteToken(JsonToken.String("method"));
            MarshalEncode(enc, t.Method);
        },
        static (target, enc) =>
        {
            var t = (BatchResponse)target;
            enc.WriteToken(JsonToken.String("result"));
            MarshalEncode(enc, t.Result);
        },
        static (target, enc) =>
        {
            var t = (BatchResponse)target;
            if (t.Error.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("error"));
            MarshalEncode(enc, t.Error);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ReleaseParams struct</c>（proto.go:889）。</summary>
public sealed class ReleaseParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:890）。</summary>
    public SnapshotID Snapshot;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (ReleaseParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ReleaseParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ReleaseSourceFileParams struct</c>（proto.go:893）。</summary>
public sealed class ReleaseSourceFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Lease SourceFileLeaseID `json:"lease"`</c>（proto.go:894）。</summary>
    public SourceFileLeaseID Lease;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("lease", false, false,
            static (target, dec) =>
            {
                var t = (ReleaseSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Lease = new SourceFileLeaseID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ReleaseSourceFileParams)target;
            enc.WriteToken(JsonToken.String("lease"));
            MarshalEncode(enc, t.Lease);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ProfileParams struct</c>（proto.go:897）。</summary>
public sealed class ProfileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Dir string `json:"dir"`</c>（proto.go:898）。</summary>
    public string Dir = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("dir", false, false,
            static (target, dec) =>
            {
                var t = (ProfileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Dir = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ProfileParams)target;
            enc.WriteToken(JsonToken.String("dir"));
            MarshalEncode(enc, t.Dir);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ProfileResult struct</c>（proto.go:901）。</summary>
public sealed class ProfileResult : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>File string `json:"file"`</c>（proto.go:902）。</summary>
    public string File = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (ProfileResult)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ProfileResult)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateBuildOrchestratorParams struct</c>（proto.go:905）。
/// Go 内嵌 `*core.BuildOptions`/`*core.CompilerOptions`（字段提升）；C# 落成两个普通字段（文件头第 10 条）。</summary>
public sealed class CreateBuildOrchestratorParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>RootNames []string `json:"rootNames"`</c>（proto.go:906）。</summary>
    public string[] RootNames = [];

    /// <summary>Go: <c>Cwd string `json:"cwd,omitempty"`</c>（proto.go:907）。</summary>
    public string Cwd = "";

    /// <summary>Go: 内嵌 <c>*core.BuildOptions `json:"buildOptions,omitempty"`</c>（proto.go:909）。</summary>
    public Core.BuildOptions? BuildOptions;

    /// <summary>Go: 内嵌 <c>*core.CompilerOptions `json:"compilerOptions,omitempty"`</c>（proto.go:910）。</summary>
    public Core.CompilerOptions? CompilerOptions;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("rootNames", false, false,
            static (target, dec) =>
            {
                var t = (CreateBuildOrchestratorParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RootNames = null!; }
                else { t.RootNames = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "")!; }
            }
        ),
        new StructFieldSpec("cwd", false, false,
            static (target, dec) =>
            {
                var t = (CreateBuildOrchestratorParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Cwd = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("buildOptions", false, false,
            static (target, dec) =>
            {
                var t = (CreateBuildOrchestratorParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.BuildOptions = null; }
                else { t.BuildOptions = DecodeNullable<Core.BuildOptions>(dec, static d => d.DecodeNew<Core.BuildOptions>()); }
            }
        ),
        new StructFieldSpec("compilerOptions", false, false,
            static (target, dec) =>
            {
                var t = (CreateBuildOrchestratorParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CompilerOptions = null; }
                else { t.CompilerOptions = DecodeNullable<Core.CompilerOptions>(dec, static d => d.DecodeNew<Core.CompilerOptions>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateBuildOrchestratorParams)target;
            enc.WriteToken(JsonToken.String("rootNames"));
            if (t.RootNames is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.RootNames)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (CreateBuildOrchestratorParams)target;
            if (t.Cwd.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("cwd"));
            MarshalEncode(enc, t.Cwd);
        },
        static (target, enc) =>
        {
            var t = (CreateBuildOrchestratorParams)target;
            if (t.BuildOptions is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("buildOptions"));
            MarshalEncode(enc, t.BuildOptions);
        },
        static (target, enc) =>
        {
            var t = (CreateBuildOrchestratorParams)target;
            if (t.CompilerOptions is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("compilerOptions"));
            MarshalEncode(enc, t.CompilerOptions);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateBuildOrchestratorResponse struct</c>（proto.go:913）。</summary>
public sealed class CreateBuildOrchestratorResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>BuildOrchestratorID BuildOrchestratorID `json:"buildOrchestratorID"`</c>（proto.go:914）。</summary>
    public BuildOrchestratorID BuildOrchestratorID;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("buildOrchestratorID", false, false,
            static (target, dec) =>
            {
                var t = (CreateBuildOrchestratorResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.BuildOrchestratorID = new BuildOrchestratorID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateBuildOrchestratorResponse)target;
            enc.WriteToken(JsonToken.String("buildOrchestratorID"));
            MarshalEncode(enc, t.BuildOrchestratorID);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type DisposeBuildOrchestratorParams struct</c>（proto.go:917）。</summary>
public sealed class DisposeBuildOrchestratorParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>BuildOrchestratorID BuildOrchestratorID `json:"buildOrchestratorID"`</c>（proto.go:918）。</summary>
    public BuildOrchestratorID BuildOrchestratorID;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("buildOrchestratorID", false, false,
            static (target, dec) =>
            {
                var t = (DisposeBuildOrchestratorParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.BuildOrchestratorID = new BuildOrchestratorID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (DisposeBuildOrchestratorParams)target;
            enc.WriteToken(JsonToken.String("buildOrchestratorID"));
            MarshalEncode(enc, t.BuildOrchestratorID);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type BuildParams struct</c>（proto.go:921）。</summary>
public sealed class BuildParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>BuildOrchestratorID BuildOrchestratorID `json:"buildOrchestratorID"`</c>（proto.go:922）。</summary>
    public BuildOrchestratorID BuildOrchestratorID;

    /// <summary>Go: <c>Project string `json:"project,omitempty"`</c>（proto.go:923）。</summary>
    public string Project = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("buildOrchestratorID", false, false,
            static (target, dec) =>
            {
                var t = (BuildParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.BuildOrchestratorID = new BuildOrchestratorID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (BuildParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (BuildParams)target;
            enc.WriteToken(JsonToken.String("buildOrchestratorID"));
            MarshalEncode(enc, t.BuildOrchestratorID);
        },
        static (target, enc) =>
        {
            var t = (BuildParams)target;
            if (t.Project.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type BuildResponse struct</c>（proto.go:926）。
/// `Statistics`（`Execute.Tsc.Statistics`）目前没有 JSON 映射（文件头第 16 条）。</summary>
public sealed class BuildResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Status tsc.ExitStatus `json:"status"`</c>（proto.go:927）。</summary>
    public Execute.Tsc.ExitStatus Status;

    /// <summary>Go: <c>Diagnostics []*DiagnosticResponse `json:"diagnostics,omitempty"`</c>（proto.go:928）。</summary>
    public DiagnosticResponse[]? Diagnostics;

    /// <summary>Go: <c>Statistics tsc.Statistics `json:"statistics"`</c>（proto.go:929）。</summary>
    public Execute.Tsc.Statistics Statistics = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("status", false, false,
            static (target, dec) =>
            {
                var t = (BuildResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Status = dec.Decode<Execute.Tsc.ExitStatus>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("diagnostics", false, false,
            static (target, dec) =>
            {
                var t = (BuildResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Diagnostics = null; }
                else { t.Diagnostics = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
        new StructFieldSpec("statistics", false, false,
            static (target, dec) =>
            {
                var t = (BuildResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Statistics = dec.DecodeNew<Execute.Tsc.Statistics>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (BuildResponse)target;
            enc.WriteToken(JsonToken.String("status"));
            MarshalEncode(enc, t.Status);
        },
        static (target, enc) =>
        {
            var t = (BuildResponse)target;
            if (t.Diagnostics is null || t.Diagnostics.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("diagnostics"));
            MarshalEncode(enc, t.Diagnostics);
        },
        static (target, enc) =>
        {
            var t = (BuildResponse)target;
            enc.WriteToken(JsonToken.String("statistics"));
            MarshalEncode(enc, t.Statistics);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CleanBuildParams struct</c>（proto.go:932）。</summary>
public sealed class CleanBuildParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>BuildOrchestratorID BuildOrchestratorID `json:"buildOrchestratorID"`</c>（proto.go:933）。</summary>
    public BuildOrchestratorID BuildOrchestratorID;

    /// <summary>Go: <c>Project string `json:"project,omitempty"`</c>（proto.go:934）。</summary>
    public string Project = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("buildOrchestratorID", false, false,
            static (target, dec) =>
            {
                var t = (CleanBuildParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.BuildOrchestratorID = new BuildOrchestratorID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (CleanBuildParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CleanBuildParams)target;
            enc.WriteToken(JsonToken.String("buildOrchestratorID"));
            MarshalEncode(enc, t.BuildOrchestratorID);
        },
        static (target, enc) =>
        {
            var t = (CleanBuildParams)target;
            if (t.Project.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CleanBuildResponse struct</c>（proto.go:937）。</summary>
public sealed class CleanBuildResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Status tsc.ExitStatus `json:"status"`</c>（proto.go:938）。</summary>
    public Execute.Tsc.ExitStatus Status;

    /// <summary>Go: <c>Diagnostics []*DiagnosticResponse `json:"diagnostics,omitempty"`</c>（proto.go:939）。</summary>
    public DiagnosticResponse[]? Diagnostics;

    /// <summary>Go: <c>Statistics tsc.Statistics `json:"statistics"`</c>（proto.go:940）。</summary>
    public Execute.Tsc.Statistics Statistics = new();

    /// <summary>Go: <c>FilesDeleted []string `json:"filesDeleted,omitempty"`</c>（proto.go:941）。</summary>
    public string[]? FilesDeleted;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("status", false, false,
            static (target, dec) =>
            {
                var t = (CleanBuildResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Status = dec.Decode<Execute.Tsc.ExitStatus>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("diagnostics", false, false,
            static (target, dec) =>
            {
                var t = (CleanBuildResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Diagnostics = null; }
                else { t.Diagnostics = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
        new StructFieldSpec("statistics", false, false,
            static (target, dec) =>
            {
                var t = (CleanBuildResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Statistics = dec.DecodeNew<Execute.Tsc.Statistics>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("filesDeleted", false, false,
            static (target, dec) =>
            {
                var t = (CleanBuildResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.FilesDeleted = null; }
                else { t.FilesDeleted = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CleanBuildResponse)target;
            enc.WriteToken(JsonToken.String("status"));
            MarshalEncode(enc, t.Status);
        },
        static (target, enc) =>
        {
            var t = (CleanBuildResponse)target;
            if (t.Diagnostics is null || t.Diagnostics.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("diagnostics"));
            MarshalEncode(enc, t.Diagnostics);
        },
        static (target, enc) =>
        {
            var t = (CleanBuildResponse)target;
            enc.WriteToken(JsonToken.String("statistics"));
            MarshalEncode(enc, t.Statistics);
        },
        static (target, enc) =>
        {
            var t = (CleanBuildResponse)target;
            if (t.FilesDeleted is null || t.FilesDeleted.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("filesDeleted"));
            MarshalEncode(enc, t.FilesDeleted);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type BuildOrchestrator struct</c>（proto.go:944）。
/// **纯内部结构、不参与 JSON**（Go 里也没有 json tag）：三个回调由 session 注入到 <c>execute/build.Orchestrator</c>
/// 上，所以 C# 侧只有三个 <c>Func&lt;string, Execute.Tsc.ExitStatus&gt;</c> 字段，不实现 IJsonMarshaler/IJsonUnmarshaler
/// （文件头第 11 条）。</summary>
public sealed class BuildOrchestrator
{
    /// <summary>Go: <c>Build func(project string) tsc.ExitStatus</c>（proto.go:945）。</summary>
    public Func<string, Execute.Tsc.ExitStatus> Build = static _ => Execute.Tsc.ExitStatus.Success;

    /// <summary>Go: <c>BuildReferences func(project string) tsc.ExitStatus</c>（proto.go:946）。</summary>
    public Func<string, Execute.Tsc.ExitStatus> BuildReferences = static _ => Execute.Tsc.ExitStatus.Success;

    /// <summary>Go: <c>CleanReferences func(project string) tsc.ExitStatus</c>（proto.go:947）。</summary>
    public Func<string, Execute.Tsc.ExitStatus> CleanReferences = static _ => Execute.Tsc.ExitStatus.Success;
}

/// <summary>Go: <c>type ConfigFileResponse struct</c>（proto.go:950）。
/// `Options`（`Core.CompilerOptions`）目前没有 JSON 映射（文件头第 16 条）。</summary>
public sealed class ConfigFileResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>FileNames []string `json:"fileNames" nonnil:"true"`</c>（proto.go:951）。</summary>
    public string[] FileNames = [];

    /// <summary>Go: <c>Options *core.CompilerOptions `json:"options" nonnil:"true"`</c>（proto.go:952）。</summary>
    public Core.CompilerOptions Options = new();

    /// <summary>Go: <c>BuildOptions *core.BuildOptions `json:"buildOptions,omitempty"`</c>（proto.go:953）。</summary>
    public Core.BuildOptions? BuildOptions;

    /// <summary>Go: <c>ProjectReferences []*core.ProjectReference `json:"projectReferences,omitempty"`</c>（proto.go:954）。</summary>
    public Core.ProjectReference[]? ProjectReferences;

    /// <summary>Go: <c>TypeAcquisition *core.TypeAcquisition `json:"typeAcquisition,omitempty"`</c>（proto.go:955）。</summary>
    public Core.TypeAcquisition? TypeAcquisition;

    /// <summary>Go: <c>CompileOnSave *bool `json:"compileOnSave,omitempty"`</c>（proto.go:956）。</summary>
    public bool? CompileOnSave;

    /// <summary>Go: <c>Raw any `json:"raw,omitempty"`</c>（proto.go:957）。见文件头第 6 条。</summary>
    public object? Raw;

    /// <summary>Go: <c>Errors []*DiagnosticResponse `json:"errors" nonnil:"true"`</c>（proto.go:958）。</summary>
    public DiagnosticResponse[] Errors = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("fileNames", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.FileNames = null!; }
                else { t.FileNames = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "")!; }
            }
        ),
        new StructFieldSpec("options", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Options = dec.DecodeNew<Core.CompilerOptions>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("buildOptions", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.BuildOptions = null; }
                else { t.BuildOptions = DecodeNullable<Core.BuildOptions>(dec, static d => d.DecodeNew<Core.BuildOptions>()); }
            }
        ),
        new StructFieldSpec("projectReferences", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ProjectReferences = null; }
                else { t.ProjectReferences = DecodeArray<Core.ProjectReference>(dec, static d => d.DecodeNew<Core.ProjectReference>()!)!; }
            }
        ),
        new StructFieldSpec("typeAcquisition", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.TypeAcquisition = null; }
                else { t.TypeAcquisition = DecodeNullable<Core.TypeAcquisition>(dec, static d => d.DecodeNew<Core.TypeAcquisition>()); }
            }
        ),
        new StructFieldSpec("compileOnSave", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CompileOnSave = null; }
                else { t.CompileOnSave = dec.Decode<bool?>(); }
            }
        ),
        new StructFieldSpec("raw", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                t.Raw = dec.Decode<object?>();
            }
        ),
        new StructFieldSpec("errors", false, false,
            static (target, dec) =>
            {
                var t = (ConfigFileResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Errors = null!; }
                else { t.Errors = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            enc.WriteToken(JsonToken.String("fileNames"));
            if (t.FileNames is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.FileNames)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            enc.WriteToken(JsonToken.String("options"));
            MarshalEncode(enc, t.Options);
        },
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            if (t.BuildOptions is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("buildOptions"));
            MarshalEncode(enc, t.BuildOptions);
        },
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            if (t.ProjectReferences is null || t.ProjectReferences.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("projectReferences"));
            MarshalEncode(enc, t.ProjectReferences);
        },
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            if (t.TypeAcquisition is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("typeAcquisition"));
            MarshalEncode(enc, t.TypeAcquisition);
        },
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            if (t.CompileOnSave is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("compileOnSave"));
            MarshalEncode(enc, t.CompileOnSave);
        },
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            if (t.Raw is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("raw"));
            MarshalEncode(enc, t.Raw);
        },
        static (target, enc) =>
        {
            var t = (ConfigFileResponse)target;
            enc.WriteToken(JsonToken.String("errors"));
            if (t.Errors is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Errors)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ReadConfigFileResponse struct</c>（proto.go:961）。</summary>
public sealed class ReadConfigFileResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Config any `json:"config"`</c>（proto.go:962）。见文件头第 6 条。</summary>
    public object? Config;

    /// <summary>Go: <c>Error *DiagnosticResponse `json:"error,omitempty"`</c>（proto.go:963）。</summary>
    public DiagnosticResponse? Error;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("config", false, false,
            static (target, dec) =>
            {
                var t = (ReadConfigFileResponse)target;
                t.Config = dec.Decode<object?>();
            }
        ),
        new StructFieldSpec("error", false, false,
            static (target, dec) =>
            {
                var t = (ReadConfigFileResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Error = null; }
                else { t.Error = DecodeNullable<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ReadConfigFileResponse)target;
            enc.WriteToken(JsonToken.String("config"));
            MarshalEncode(enc, t.Config);
        },
        static (target, enc) =>
        {
            var t = (ReadConfigFileResponse)target;
            if (t.Error is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("error"));
            MarshalEncode(enc, t.Error);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetDefaultProjectForFileParams struct</c>（proto.go:966）。</summary>
public sealed class GetDefaultProjectForFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:967）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:968）。</summary>
    public DocumentIdentifier File = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetDefaultProjectForFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetDefaultProjectForFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetDefaultProjectForFileParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetDefaultProjectForFileParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ProjectResponse struct</c>（proto.go:971）。
/// `Id` 是 Go 的 `project.ID`（string 新类型）；C# 的 `Project.ID` 没有 IJsonMarshaler，故按 string 语义读写
/// （文件头第 9 条）。`CompilerOptions`（`Core.CompilerOptions`）目前没有 JSON 映射（文件头第 16 条）。</summary>
public sealed class ProjectResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Id project.ID `json:"id"`</c>（proto.go:972）。</summary>
    public Project.ID Id;

    /// <summary>Go: <c>ConfigFileName string `json:"configFileName"`</c>（proto.go:973）。</summary>
    public string ConfigFileName = "";

    /// <summary>Go: <c>CurrentDirectory string `json:"currentDirectory"`</c>（proto.go:974）。</summary>
    public string CurrentDirectory = "";

    /// <summary>Go: <c>Dirty bool `json:"dirty"`</c>（proto.go:975）。</summary>
    public bool Dirty;

    /// <summary>Go: <c>ParsedCommandLine *ConfigFileResponse `json:"parsedCommandLine" nonnil:"true"`</c>（proto.go:976）。</summary>
    public ConfigFileResponse ParsedCommandLine = new();

    /// <summary>Go: <c>RootFiles []string `json:"rootFiles" nonnil:"true"`</c>（proto.go:978，Deprecated）。</summary>
    public string[] RootFiles = [];

    /// <summary>Go: <c>CompilerOptions *core.CompilerOptions `json:"compilerOptions" nonnil:"true"`</c>（proto.go:980，Deprecated）。</summary>
    public Core.CompilerOptions CompilerOptions = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("id", false, false,
            static (target, dec) =>
            {
                var t = (ProjectResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Id = new Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("configFileName", false, false,
            static (target, dec) =>
            {
                var t = (ProjectResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ConfigFileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("currentDirectory", false, false,
            static (target, dec) =>
            {
                var t = (ProjectResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CurrentDirectory = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("dirty", false, false,
            static (target, dec) =>
            {
                var t = (ProjectResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Dirty = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("parsedCommandLine", false, false,
            static (target, dec) =>
            {
                var t = (ProjectResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ParsedCommandLine.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("rootFiles", false, false,
            static (target, dec) =>
            {
                var t = (ProjectResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RootFiles = null!; }
                else { t.RootFiles = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "")!; }
            }
        ),
        new StructFieldSpec("compilerOptions", false, false,
            static (target, dec) =>
            {
                var t = (ProjectResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CompilerOptions = dec.DecodeNew<Core.CompilerOptions>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ProjectResponse)target;
            enc.WriteToken(JsonToken.String("id"));
            MarshalEncode(enc, t.Id.Value);
        },
        static (target, enc) =>
        {
            var t = (ProjectResponse)target;
            enc.WriteToken(JsonToken.String("configFileName"));
            MarshalEncode(enc, t.ConfigFileName);
        },
        static (target, enc) =>
        {
            var t = (ProjectResponse)target;
            enc.WriteToken(JsonToken.String("currentDirectory"));
            MarshalEncode(enc, t.CurrentDirectory);
        },
        static (target, enc) =>
        {
            var t = (ProjectResponse)target;
            enc.WriteToken(JsonToken.String("dirty"));
            MarshalEncode(enc, t.Dirty);
        },
        static (target, enc) =>
        {
            var t = (ProjectResponse)target;
            enc.WriteToken(JsonToken.String("parsedCommandLine"));
            MarshalEncode(enc, t.ParsedCommandLine);
        },
        static (target, enc) =>
        {
            var t = (ProjectResponse)target;
            enc.WriteToken(JsonToken.String("rootFiles"));
            if (t.RootFiles is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.RootFiles)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (ProjectResponse)target;
            enc.WriteToken(JsonToken.String("compilerOptions"));
            MarshalEncode(enc, t.CompilerOptions);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolAtPositionParams struct</c>（proto.go:1055）。</summary>
public sealed class GetSymbolAtPositionParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1056）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1057）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1058）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Position uint32 `json:"position"`</c>（proto.go:1059）。</summary>
    public uint Position;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("position", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Position = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolAtPositionParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolAtPositionParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolAtPositionParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolAtPositionParams)target;
            enc.WriteToken(JsonToken.String("position"));
            MarshalEncode(enc, t.Position);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolsAtPositionsParams struct</c>（proto.go:1062）。</summary>
public sealed class GetSymbolsAtPositionsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1063）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1064）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1065）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Positions []uint32 `json:"positions"`</c>（proto.go:1066）。</summary>
    public uint[] Positions = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsAtPositionsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsAtPositionsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsAtPositionsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("positions", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsAtPositionsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Positions = null!; }
                else { t.Positions = DecodeArray<uint>(dec, static d => d.Decode<uint>())!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolsAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("positions"));
            if (t.Positions is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Positions)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolOfSourceFileParams struct</c>（proto.go:1069）。</summary>
public sealed class GetSymbolOfSourceFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1070）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1071）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1072）。</summary>
    public DocumentIdentifier File = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolOfSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolOfSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolOfSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolOfSourceFileParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolOfSourceFileParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolOfSourceFileParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolsOfSourceFilesParams struct</c>（proto.go:1075）。</summary>
public sealed class GetSymbolsOfSourceFilesParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1076）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1077）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Files []DocumentIdentifier `json:"files"`</c>（proto.go:1078）。</summary>
    public DocumentIdentifier[] Files = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsOfSourceFilesParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsOfSourceFilesParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("files", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsOfSourceFilesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Files = null!; }
                else { t.Files = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>())!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolsOfSourceFilesParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsOfSourceFilesParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsOfSourceFilesParams)target;
            enc.WriteToken(JsonToken.String("files"));
            if (t.Files is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Files)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolAtLocationParams struct</c>（proto.go:1081）。</summary>
public sealed class GetSymbolAtLocationParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1082）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1083）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1084）。</summary>
    public NodeHandle Location;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolAtLocationParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolAtLocationParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolAtLocationParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolsAtLocationsParams struct</c>（proto.go:1087）。</summary>
public sealed class GetSymbolsAtLocationsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1088）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1089）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Locations []NodeHandle `json:"locations"`</c>（proto.go:1090）。</summary>
    public NodeHandle[] Locations = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsAtLocationsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsAtLocationsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("locations", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsAtLocationsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Locations = null!; }
                else { t.Locations = DecodeArray<NodeHandle>(dec, static d => new NodeHandle(d.Decode<string>() ?? ""))!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolsAtLocationsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsAtLocationsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsAtLocationsParams)target;
            enc.WriteToken(JsonToken.String("locations"));
            if (t.Locations is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Locations)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SymbolResponse struct</c>（proto.go:1093）。</summary>
public sealed class SymbolResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Id SymbolID `json:"id"`</c>（proto.go:1094）。</summary>
    public SymbolID Id;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1097）：符号首次被观察到的项目，
    /// 也是后续随项目变化查询的默认项目。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Name string `json:"name"`</c>（proto.go:1098）。</summary>
    public string Name = "";

    /// <summary>Go: <c>Flags uint32 `json:"flags"`</c>（proto.go:1099）。</summary>
    public uint Flags;

    /// <summary>Go: <c>CheckFlags uint32 `json:"checkFlags"`</c>（proto.go:1100）。</summary>
    public uint CheckFlags;

    /// <summary>Go: <c>Declarations []NodeHandle `json:"declarations,omitempty"`</c>（proto.go:1101）。</summary>
    public NodeHandle[]? Declarations;

    /// <summary>Go: <c>ValueDeclaration NodeHandle `json:"valueDeclaration,omitempty"`</c>（proto.go:1102）。</summary>
    public NodeHandle ValueDeclaration;

    /// <summary>Go: <c>Parent SymbolID `json:"parent,omitzero"`</c>（proto.go:1103）。</summary>
    public SymbolID Parent;

    /// <summary>Go: <c>ExportSymbol SymbolID `json:"exportSymbol,omitzero"`</c>（proto.go:1104）。</summary>
    public SymbolID ExportSymbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("id", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Id = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("flags", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Flags = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("checkFlags", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CheckFlags = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("declarations", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Declarations = null; }
                else { t.Declarations = DecodeArray<NodeHandle>(dec, static d => new NodeHandle(d.Decode<string>() ?? "")); }
            }
        ),
        new StructFieldSpec("valueDeclaration", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ValueDeclaration = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("parent", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Parent = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("exportSymbol", false, false,
            static (target, dec) =>
            {
                var t = (SymbolResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ExportSymbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            enc.WriteToken(JsonToken.String("id"));
            MarshalEncode(enc, t.Id);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            enc.WriteToken(JsonToken.String("flags"));
            MarshalEncode(enc, t.Flags);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            enc.WriteToken(JsonToken.String("checkFlags"));
            MarshalEncode(enc, t.CheckFlags);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            if (t.Declarations is null || t.Declarations.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("declarations"));
            MarshalEncode(enc, t.Declarations);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            if (t.ValueDeclaration.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("valueDeclaration"));
            MarshalEncode(enc, t.ValueDeclaration);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            if (t.Parent.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("parent"));
            MarshalEncode(enc, t.Parent);
        },
        static (target, enc) =>
        {
            var t = (SymbolResponse)target;
            if (t.ExportSymbol.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("exportSymbol"));
            MarshalEncode(enc, t.ExportSymbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypeOfSymbolParams struct</c>（proto.go:1118）。</summary>
public sealed class GetTypeOfSymbolParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1119）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1120）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Symbol SymbolID `json:"symbol"`</c>（proto.go:1121）。</summary>
    public SymbolID Symbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeOfSymbolParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeOfSymbolParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeOfSymbolParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypeOfSymbolParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypeOfSymbolParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypeOfSymbolParams)target;
            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypesOfSymbolsParams struct</c>（proto.go:1124）。</summary>
public sealed class GetTypesOfSymbolsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1125）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1126）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Symbols []SymbolID `json:"symbols"`</c>（proto.go:1127）。</summary>
    public SymbolID[] Symbols = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypesOfSymbolsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypesOfSymbolsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbols", false, false,
            static (target, dec) =>
            {
                var t = (GetTypesOfSymbolsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Symbols = null!; }
                else { t.Symbols = DecodeArray<SymbolID>(dec, static d => new SymbolID(d.Decode<ulong>()))!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypesOfSymbolsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypesOfSymbolsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypesOfSymbolsParams)target;
            enc.WriteToken(JsonToken.String("symbols"));
            if (t.Symbols is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Symbols)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TypeResponse struct</c>（proto.go:1130）。字段极多，Spec 的表项分三段落笔（内容与其它类同形）。</summary>
public sealed class TypeResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Id TypeID `json:"id"`</c>（proto.go:1131）。</summary>
    public TypeID Id;

    /// <summary>Go: <c>Flags uint32 `json:"flags"`</c>（proto.go:1132）。</summary>
    public uint Flags;

    /// <summary>Go: <c>ObjectFlags uint32 `json:"objectFlags,omitempty"`</c>（proto.go:1133）。</summary>
    public uint ObjectFlags;

    /// <summary>Go: <c>IsTupleType bool `json:"isTupleType,omitempty"`</c>（proto.go:1134）。</summary>
    public bool IsTupleType;

    /// <summary>Go: <c>Value any `json:"value"`</c>（proto.go:1138）：字面量类型数据；BigInt 字面量编码成有符号十进制
    /// 字符串（JSON 没有 bigint），缺省值是 null。见文件头第 6 条。</summary>
    public object? Value;

    /// <summary>Go: <c>Target TypeID `json:"target,omitzero"`</c>（proto.go:1140）：ObjectType / TypeReference /
    /// StringMappingType / IndexType 的 target。</summary>
    public TypeID Target;

    /// <summary>Go: <c>TypeParameters []TypeID `json:"typeParameters,omitempty"`</c>（proto.go:1143，InterfaceType 数据）。</summary>
    public TypeID[]? TypeParameters;

    /// <summary>Go: <c>OuterTypeParameters []TypeID `json:"outerTypeParameters,omitempty"`</c>（proto.go:1144）。</summary>
    public TypeID[]? OuterTypeParameters;

    /// <summary>Go: <c>LocalTypeParameters []TypeID `json:"localTypeParameters,omitempty"`</c>（proto.go:1145）。</summary>
    public TypeID[]? LocalTypeParameters;

    /// <summary>Go: <c>ElementFlags []checker.ElementFlags `json:"elementFlags,omitempty"`</c>（proto.go:1148，TupleType 数据）。</summary>
    public Checker.ElementFlags[]? ElementFlags;

    /// <summary>Go: <c>FixedLength *int `json:"fixedLength,omitempty"`</c>（proto.go:1149）。</summary>
    public int? FixedLength;

    /// <summary>Go: <c>TupleReadonly *bool `json:"readonly,omitempty"`</c>（proto.go:1150）。</summary>
    public bool? TupleReadonly;

    /// <summary>Go: <c>LabeledElementDeclarations []NodeHandle `json:"labeledElementDeclarations,omitempty"`</c>（proto.go:1151）。</summary>
    public NodeHandle[]? LabeledElementDeclarations;

    /// <summary>Go: <c>ObjectType TypeID `json:"objectType,omitzero"`</c>（proto.go:1154，IndexedAccessType 数据）。</summary>
    public TypeID ObjectType;

    /// <summary>Go: <c>IndexType TypeID `json:"indexType,omitzero"`</c>（proto.go:1155）。</summary>
    public TypeID IndexType;

    /// <summary>Go: <c>CheckType TypeID `json:"checkType,omitzero"`</c>（proto.go:1158，ConditionalType 数据）。</summary>
    public TypeID CheckType;

    /// <summary>Go: <c>ExtendsType TypeID `json:"extendsType,omitzero"`</c>（proto.go:1159）。</summary>
    public TypeID ExtendsType;

    /// <summary>Go: <c>BaseType TypeID `json:"baseType,omitzero"`</c>（proto.go:1162，SubstitutionType 数据）。</summary>
    public TypeID BaseType;

    /// <summary>Go: <c>SubstConstraint TypeID `json:"substConstraint,omitzero"`</c>（proto.go:1163）。</summary>
    public TypeID SubstConstraint;

    /// <summary>Go: <c>TypeParameter TypeID `json:"typeParameter,omitzero"`</c>（proto.go:1166，MappedType 数据）。</summary>
    public TypeID TypeParameter;

    /// <summary>Go: <c>ConstraintType TypeID `json:"constraintType,omitzero"`</c>（proto.go:1167）。</summary>
    public TypeID ConstraintType;

    /// <summary>Go: <c>NameType TypeID `json:"nameType,omitzero"`</c>（proto.go:1168）。</summary>
    public TypeID NameType;

    /// <summary>Go: <c>TemplateType TypeID `json:"templateType,omitzero"`</c>（proto.go:1169）。</summary>
    public TypeID TemplateType;

    /// <summary>Go: <c>Texts []string `json:"texts,omitempty"`</c>（proto.go:1172，TemplateLiteralType 的文本段）。</summary>
    public string[]? Texts;

    /// <summary>Go: <c>FreshType TypeID `json:"freshType,omitzero"`</c>（proto.go:1175，FreshableType 数据）。</summary>
    public TypeID FreshType;

    /// <summary>Go: <c>RegularType TypeID `json:"regularType,omitzero"`</c>（proto.go:1176）。</summary>
    public TypeID RegularType;

    /// <summary>Go: <c>IsThisType bool `json:"isThisType,omitempty"`</c>（proto.go:1179，TypeParameter 数据）。</summary>
    public bool IsThisType;

    /// <summary>Go: <c>ThisType TypeID `json:"thisType,omitzero"`</c>（proto.go:1182，InterfaceType 数据）。</summary>
    public TypeID ThisType;

    /// <summary>Go: <c>IntrinsicName string `json:"intrinsicName,omitempty"`</c>（proto.go:1185，IntrinsicType 数据）。</summary>
    public string IntrinsicName = "";

    /// <summary>Go: <c>AliasTypeArguments []TypeID `json:"aliasTypeArguments,omitempty"`</c>（proto.go:1188，TypeAlias 数据）。</summary>
    public TypeID[]? AliasTypeArguments;

    /// <summary>Go: <c>AliasSymbol SymbolID `json:"aliasSymbol,omitzero"`</c>（proto.go:1189）。</summary>
    public SymbolID AliasSymbol;

    /// <summary>Go: <c>Symbol SymbolID `json:"symbol,omitzero"`</c>（proto.go:1192）：结构化类型关联的符号。</summary>
    public SymbolID Symbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("id", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Id = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("flags", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Flags = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("objectFlags", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ObjectFlags = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("isTupleType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsTupleType = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("value", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                t.Value = dec.Decode<object?>();
            }
        ),
        new StructFieldSpec("target", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Target = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("typeParameters", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.TypeParameters = null; }
                else { t.TypeParameters = DecodeArray<TypeID>(dec, static d => new TypeID(d.Decode<uint>())); }
            }
        ),
        new StructFieldSpec("outerTypeParameters", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OuterTypeParameters = null; }
                else { t.OuterTypeParameters = DecodeArray<TypeID>(dec, static d => new TypeID(d.Decode<uint>())); }
            }
        ),
        new StructFieldSpec("localTypeParameters", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.LocalTypeParameters = null; }
                else { t.LocalTypeParameters = DecodeArray<TypeID>(dec, static d => new TypeID(d.Decode<uint>())); }
            }
        ),
        new StructFieldSpec("elementFlags", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ElementFlags = null; }
                else { t.ElementFlags = DecodeArray<Checker.ElementFlags>(dec, static d => d.Decode<Checker.ElementFlags>()); }
            }
        ),
        new StructFieldSpec("fixedLength", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.FixedLength = null; }
                else { t.FixedLength = dec.Decode<int?>(); }
            }
        ),
        new StructFieldSpec("readonly", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.TupleReadonly = null; }
                else { t.TupleReadonly = dec.Decode<bool?>(); }
            }
        ),
        new StructFieldSpec("labeledElementDeclarations", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.LabeledElementDeclarations = null; }
                else { t.LabeledElementDeclarations = DecodeArray<NodeHandle>(dec, static d => new NodeHandle(d.Decode<string>() ?? "")); }
            }
        ),
        new StructFieldSpec("objectType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ObjectType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("indexType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IndexType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("checkType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CheckType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("extendsType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ExtendsType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("baseType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.BaseType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("substConstraint", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SubstConstraint = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("typeParameter", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TypeParameter = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("constraintType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ConstraintType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("nameType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.NameType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("templateType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TemplateType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("texts", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Texts = null; }
                else { t.Texts = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
        new StructFieldSpec("freshType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FreshType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("regularType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.RegularType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("isThisType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsThisType = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("thisType", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ThisType = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("intrinsicName", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IntrinsicName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("aliasTypeArguments", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.AliasTypeArguments = null; }
                else { t.AliasTypeArguments = DecodeArray<TypeID>(dec, static d => new TypeID(d.Decode<uint>())); }
            }
        ),
        new StructFieldSpec("aliasSymbol", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.AliasSymbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (TypeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            enc.WriteToken(JsonToken.String("id"));
            MarshalEncode(enc, t.Id);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            enc.WriteToken(JsonToken.String("flags"));
            MarshalEncode(enc, t.Flags);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.ObjectFlags == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("objectFlags"));
            MarshalEncode(enc, t.ObjectFlags);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (!t.IsTupleType)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("isTupleType"));
            MarshalEncode(enc, t.IsTupleType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            enc.WriteToken(JsonToken.String("value"));
            MarshalEncode(enc, t.Value);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.Target.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("target"));
            MarshalEncode(enc, t.Target);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.TypeParameters is null || t.TypeParameters.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("typeParameters"));
            MarshalEncode(enc, t.TypeParameters);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.OuterTypeParameters is null || t.OuterTypeParameters.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("outerTypeParameters"));
            MarshalEncode(enc, t.OuterTypeParameters);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.LocalTypeParameters is null || t.LocalTypeParameters.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("localTypeParameters"));
            MarshalEncode(enc, t.LocalTypeParameters);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.ElementFlags is null || t.ElementFlags.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("elementFlags"));
            MarshalEncode(enc, t.ElementFlags);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.FixedLength is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("fixedLength"));
            MarshalEncode(enc, t.FixedLength);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.TupleReadonly is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("readonly"));
            MarshalEncode(enc, t.TupleReadonly);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.LabeledElementDeclarations is null || t.LabeledElementDeclarations.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("labeledElementDeclarations"));
            MarshalEncode(enc, t.LabeledElementDeclarations);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.ObjectType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("objectType"));
            MarshalEncode(enc, t.ObjectType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.IndexType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("indexType"));
            MarshalEncode(enc, t.IndexType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.CheckType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("checkType"));
            MarshalEncode(enc, t.CheckType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.ExtendsType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("extendsType"));
            MarshalEncode(enc, t.ExtendsType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.BaseType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("baseType"));
            MarshalEncode(enc, t.BaseType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.SubstConstraint.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("substConstraint"));
            MarshalEncode(enc, t.SubstConstraint);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.TypeParameter.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("typeParameter"));
            MarshalEncode(enc, t.TypeParameter);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.ConstraintType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("constraintType"));
            MarshalEncode(enc, t.ConstraintType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.NameType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("nameType"));
            MarshalEncode(enc, t.NameType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.TemplateType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("templateType"));
            MarshalEncode(enc, t.TemplateType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.Texts is null || t.Texts.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("texts"));
            MarshalEncode(enc, t.Texts);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.FreshType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("freshType"));
            MarshalEncode(enc, t.FreshType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.RegularType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("regularType"));
            MarshalEncode(enc, t.RegularType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (!t.IsThisType)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("isThisType"));
            MarshalEncode(enc, t.IsThisType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.ThisType.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("thisType"));
            MarshalEncode(enc, t.ThisType);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.IntrinsicName.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("intrinsicName"));
            MarshalEncode(enc, t.IntrinsicName);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.AliasTypeArguments is null || t.AliasTypeArguments.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("aliasTypeArguments"));
            MarshalEncode(enc, t.AliasTypeArguments);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.AliasSymbol.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("aliasSymbol"));
            MarshalEncode(enc, t.AliasSymbol);
        },
        static (target, enc) =>
        {
            var t = (TypeResponse)target;
            if (t.Symbol.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ConstantValueResponse struct</c>（proto.go:1320）。</summary>
public sealed class ConstantValueResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>IsNumber bool `json:"isNumber"`</c>（proto.go:1321）。</summary>
    public bool IsNumber;

    /// <summary>Go: <c>Value any `json:"value"`</c>（proto.go:1322）。见文件头第 6 条。</summary>
    public object? Value;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("isNumber", false, false,
            static (target, dec) =>
            {
                var t = (ConstantValueResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsNumber = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("value", false, false,
            static (target, dec) =>
            {
                var t = (ConstantValueResponse)target;
                t.Value = dec.Decode<object?>();
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ConstantValueResponse)target;
            enc.WriteToken(JsonToken.String("isNumber"));
            MarshalEncode(enc, t.IsNumber);
        },
        static (target, enc) =>
        {
            var t = (ConstantValueResponse)target;
            enc.WriteToken(JsonToken.String("value"));
            MarshalEncode(enc, t.Value);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SignatureResponse struct</c>（proto.go:1325）。</summary>
public sealed class SignatureResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Id SignatureID `json:"id"`</c>（proto.go:1326）。</summary>
    public SignatureID Id;

    /// <summary>Go: <c>Flags uint32 `json:"flags"`</c>（proto.go:1327）。</summary>
    public uint Flags;

    /// <summary>Go: <c>Declaration NodeHandle `json:"declaration,omitempty"`</c>（proto.go:1328）。</summary>
    public NodeHandle Declaration;

    /// <summary>Go: <c>TypeParameters []TypeID `json:"typeParameters,omitempty"`</c>（proto.go:1329）。</summary>
    public TypeID[]? TypeParameters;

    /// <summary>Go: <c>Parameters []SymbolID `json:"parameters,omitempty"`</c>（proto.go:1330）。</summary>
    public SymbolID[]? Parameters;

    /// <summary>Go: <c>ThisParameter SymbolID `json:"thisParameter,omitzero"`</c>（proto.go:1331）。</summary>
    public SymbolID ThisParameter;

    /// <summary>Go: <c>Target SignatureID `json:"target,omitzero"`</c>（proto.go:1332）。</summary>
    public SignatureID Target;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("id", false, false,
            static (target, dec) =>
            {
                var t = (SignatureResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Id = new SignatureID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("flags", false, false,
            static (target, dec) =>
            {
                var t = (SignatureResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Flags = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("declaration", false, false,
            static (target, dec) =>
            {
                var t = (SignatureResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Declaration = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("typeParameters", false, false,
            static (target, dec) =>
            {
                var t = (SignatureResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.TypeParameters = null; }
                else { t.TypeParameters = DecodeArray<TypeID>(dec, static d => new TypeID(d.Decode<uint>())); }
            }
        ),
        new StructFieldSpec("parameters", false, false,
            static (target, dec) =>
            {
                var t = (SignatureResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Parameters = null; }
                else { t.Parameters = DecodeArray<SymbolID>(dec, static d => new SymbolID(d.Decode<ulong>())); }
            }
        ),
        new StructFieldSpec("thisParameter", false, false,
            static (target, dec) =>
            {
                var t = (SignatureResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ThisParameter = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("target", false, false,
            static (target, dec) =>
            {
                var t = (SignatureResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Target = new SignatureID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SignatureResponse)target;
            enc.WriteToken(JsonToken.String("id"));
            MarshalEncode(enc, t.Id);
        },
        static (target, enc) =>
        {
            var t = (SignatureResponse)target;
            enc.WriteToken(JsonToken.String("flags"));
            MarshalEncode(enc, t.Flags);
        },
        static (target, enc) =>
        {
            var t = (SignatureResponse)target;
            if (t.Declaration.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("declaration"));
            MarshalEncode(enc, t.Declaration);
        },
        static (target, enc) =>
        {
            var t = (SignatureResponse)target;
            if (t.TypeParameters is null || t.TypeParameters.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("typeParameters"));
            MarshalEncode(enc, t.TypeParameters);
        },
        static (target, enc) =>
        {
            var t = (SignatureResponse)target;
            if (t.Parameters is null || t.Parameters.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("parameters"));
            MarshalEncode(enc, t.Parameters);
        },
        static (target, enc) =>
        {
            var t = (SignatureResponse)target;
            if (t.ThisParameter.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("thisParameter"));
            MarshalEncode(enc, t.ThisParameter);
        },
        static (target, enc) =>
        {
            var t = (SignatureResponse)target;
            if (t.Target.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("target"));
            MarshalEncode(enc, t.Target);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSourceFileParams struct</c>（proto.go:1335）。</summary>
public sealed class GetSourceFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1336）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1337）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1338）。</summary>
    public DocumentIdentifier File = new();

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetSourceFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSourceFileParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSourceFileParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSourceFileParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSourceFileNamesParams struct</c>（proto.go:1341）。</summary>
public sealed class GetSourceFileNamesParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1342）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1343）。</summary>
    public TypeScript.Project.ID Project;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSourceFileNamesParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSourceFileNamesParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSourceFileNamesParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSourceFileNamesParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetModeForUsageLocationParams struct</c>（proto.go:1346）。</summary>
public sealed class GetModeForUsageLocationParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1347）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1348）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1349）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Usage NodeHandle `json:"usage"`</c>（proto.go:1350）。</summary>
    public NodeHandle Usage;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForUsageLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForUsageLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForUsageLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("usage", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForUsageLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Usage = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetModeForUsageLocationParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetModeForUsageLocationParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetModeForUsageLocationParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetModeForUsageLocationParams)target;
            enc.WriteToken(JsonToken.String("usage"));
            MarshalEncode(enc, t.Usage);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetModeForResolutionAtIndexParams struct</c>（proto.go:1353）。</summary>
public sealed class GetModeForResolutionAtIndexParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1354）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1355）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1356）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Index int `json:"index"`</c>（proto.go:1357）。</summary>
    public int Index;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForResolutionAtIndexParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForResolutionAtIndexParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForResolutionAtIndexParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("index", false, false,
            static (target, dec) =>
            {
                var t = (GetModeForResolutionAtIndexParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Index = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetModeForResolutionAtIndexParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetModeForResolutionAtIndexParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetModeForResolutionAtIndexParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetModeForResolutionAtIndexParams)target;
            enc.WriteToken(JsonToken.String("index"));
            MarshalEncode(enc, t.Index);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetResolvedModuleParams struct</c>（proto.go:1360）。</summary>
public sealed class GetResolvedModuleParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1361）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1362）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1363）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>ModuleName string `json:"moduleName"`</c>（proto.go:1364）。</summary>
    public string ModuleName = "";

    /// <summary>Go: <c>Mode core.ResolutionMode `json:"mode"`</c>（proto.go:1365）：C# 侧是 `Core.ModuleKind`（文件头第 14 条）。</summary>
    public Core.ModuleKind Mode;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("moduleName", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ModuleName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("mode", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Mode = dec.Decode<Core.ModuleKind>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetResolvedModuleParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedModuleParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedModuleParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedModuleParams)target;
            enc.WriteToken(JsonToken.String("moduleName"));
            MarshalEncode(enc, t.ModuleName);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedModuleParams)target;
            enc.WriteToken(JsonToken.String("mode"));
            MarshalEncode(enc, t.Mode);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetResolvedModuleFromModuleSpecifierParams struct</c>（proto.go:1368）。</summary>
public sealed class GetResolvedModuleFromModuleSpecifierParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1369）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1370）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>ModuleSpecifier NodeHandle `json:"moduleSpecifier"`</c>（proto.go:1371）。</summary>
    public NodeHandle ModuleSpecifier;

    /// <summary>Go: <c>SourceFile *DocumentIdentifier `json:"sourceFile,omitempty"`</c>（proto.go:1372）。</summary>
    public DocumentIdentifier? SourceFile;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleFromModuleSpecifierParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleFromModuleSpecifierParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("moduleSpecifier", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleFromModuleSpecifierParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ModuleSpecifier = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("sourceFile", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedModuleFromModuleSpecifierParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.SourceFile = null; }
                else { t.SourceFile = DecodeNullable<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetResolvedModuleFromModuleSpecifierParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedModuleFromModuleSpecifierParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedModuleFromModuleSpecifierParams)target;
            enc.WriteToken(JsonToken.String("moduleSpecifier"));
            MarshalEncode(enc, t.ModuleSpecifier);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedModuleFromModuleSpecifierParams)target;
            if (t.SourceFile is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("sourceFile"));
            MarshalEncode(enc, t.SourceFile);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetResolvedTypeReferenceDirectiveParams struct</c>（proto.go:1375）。</summary>
public sealed class GetResolvedTypeReferenceDirectiveParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1376）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1377）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1378）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>TypeDirectiveName string `json:"typeDirectiveName"`</c>（proto.go:1379）。</summary>
    public string TypeDirectiveName = "";

    /// <summary>Go: <c>Mode core.ResolutionMode `json:"mode"`</c>（proto.go:1380）→ `Core.ModuleKind`。</summary>
    public Core.ModuleKind Mode;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("typeDirectiveName", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TypeDirectiveName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("mode", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Mode = dec.Decode<Core.ModuleKind>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveParams)target;
            enc.WriteToken(JsonToken.String("typeDirectiveName"));
            MarshalEncode(enc, t.TypeDirectiveName);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveParams)target;
            enc.WriteToken(JsonToken.String("mode"));
            MarshalEncode(enc, t.Mode);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetResolvedTypeReferenceDirectiveFromReferenceParams struct</c>（proto.go:1383）。</summary>
public sealed class GetResolvedTypeReferenceDirectiveFromReferenceParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1384）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1385）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>SourceFile DocumentIdentifier `json:"sourceFile"`</c>（proto.go:1386）。</summary>
    public DocumentIdentifier SourceFile = new();

    /// <summary>Go: <c>TypeDirectiveName string `json:"typeDirectiveName"`</c>（proto.go:1387）。</summary>
    public string TypeDirectiveName = "";

    /// <summary>Go: <c>ResolutionMode core.ResolutionMode `json:"resolutionMode"`</c>（proto.go:1388）→ `Core.ModuleKind`。</summary>
    public Core.ModuleKind ResolutionMode;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("sourceFile", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SourceFile.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("typeDirectiveName", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TypeDirectiveName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolutionMode", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolutionMode = dec.Decode<Core.ModuleKind>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
            enc.WriteToken(JsonToken.String("sourceFile"));
            MarshalEncode(enc, t.SourceFile);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
            enc.WriteToken(JsonToken.String("typeDirectiveName"));
            MarshalEncode(enc, t.TypeDirectiveName);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedTypeReferenceDirectiveFromReferenceParams)target;
            enc.WriteToken(JsonToken.String("resolutionMode"));
            MarshalEncode(enc, t.ResolutionMode);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type PackageId struct</c>（proto.go:1391）。</summary>
public sealed class PackageId : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Name string `json:"name"`</c>（proto.go:1392）。</summary>
    public string Name = "";

    /// <summary>Go: <c>SubModuleName string `json:"subModuleName"`</c>（proto.go:1393）。</summary>
    public string SubModuleName = "";

    /// <summary>Go: <c>Version string `json:"version"`</c>（proto.go:1394）。</summary>
    public string Version = "";

    /// <summary>Go: <c>PeerDependencies string `json:"peerDependencies"`</c>（proto.go:1395）。</summary>
    public string PeerDependencies = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (PackageId)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("subModuleName", false, false,
            static (target, dec) =>
            {
                var t = (PackageId)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SubModuleName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("version", false, false,
            static (target, dec) =>
            {
                var t = (PackageId)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Version = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("peerDependencies", false, false,
            static (target, dec) =>
            {
                var t = (PackageId)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PeerDependencies = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (PackageId)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
        static (target, enc) =>
        {
            var t = (PackageId)target;
            enc.WriteToken(JsonToken.String("subModuleName"));
            MarshalEncode(enc, t.SubModuleName);
        },
        static (target, enc) =>
        {
            var t = (PackageId)target;
            enc.WriteToken(JsonToken.String("version"));
            MarshalEncode(enc, t.Version);
        },
        static (target, enc) =>
        {
            var t = (PackageId)target;
            enc.WriteToken(JsonToken.String("peerDependencies"));
            MarshalEncode(enc, t.PeerDependencies);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedModule struct</c>（proto.go:1410）。</summary>
public sealed class ResolvedModule : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ResolvedFileName string `json:"resolvedFileName"`</c>（proto.go:1411）。</summary>
    public string ResolvedFileName = "";

    /// <summary>Go: <c>OriginalPath string `json:"originalPath,omitempty"`</c>（proto.go:1412）。</summary>
    public string OriginalPath = "";

    /// <summary>Go: <c>Extension string `json:"extension"`</c>（proto.go:1413）。</summary>
    public string Extension = "";

    /// <summary>Go: <c>ResolvedUsingTsExtension bool `json:"resolvedUsingTsExtension,omitempty"`</c>（proto.go:1414）。</summary>
    public bool ResolvedUsingTsExtension;

    /// <summary>Go: <c>ResolvedUsingExtraExtensions bool `json:"resolvedUsingExtraExtensions,omitempty"`</c>（proto.go:1415）。</summary>
    public bool ResolvedUsingExtraExtensions;

    /// <summary>Go: <c>PackageId *PackageId `json:"packageId,omitempty"`</c>（proto.go:1416）。</summary>
    public PackageId? PackageId;

    /// <summary>Go: <c>IsExternalLibraryImport bool `json:"isExternalLibraryImport,omitempty"`</c>（proto.go:1417）。</summary>
    public bool IsExternalLibraryImport;

    /// <summary>Go: <c>AlternateResult string `json:"alternateResult,omitempty"`</c>（proto.go:1418）。</summary>
    public string AlternateResult = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("resolvedFileName", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolvedFileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("originalPath", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() != JsonKind.Null) { t.OriginalPath = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("extension", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Extension = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolvedUsingTsExtension", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolvedUsingTsExtension = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolvedUsingExtraExtensions", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolvedUsingExtraExtensions = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("packageId", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.PackageId = null; }
                else { t.PackageId = DecodeNullable<PackageId>(dec, static d => d.DecodeNew<PackageId>()); }
            }
        ),
        new StructFieldSpec("isExternalLibraryImport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsExternalLibraryImport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("alternateResult", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedModule)target;
                if (dec.PeekKind() != JsonKind.Null) { t.AlternateResult = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            enc.WriteToken(JsonToken.String("resolvedFileName"));
            MarshalEncode(enc, t.ResolvedFileName);
        },
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            if (t.OriginalPath.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("originalPath"));
            MarshalEncode(enc, t.OriginalPath);
        },
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            enc.WriteToken(JsonToken.String("extension"));
            MarshalEncode(enc, t.Extension);
        },
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            if (!t.ResolvedUsingTsExtension)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("resolvedUsingTsExtension"));
            MarshalEncode(enc, t.ResolvedUsingTsExtension);
        },
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            if (!t.ResolvedUsingExtraExtensions)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("resolvedUsingExtraExtensions"));
            MarshalEncode(enc, t.ResolvedUsingExtraExtensions);
        },
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            if (t.PackageId is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("packageId"));
            MarshalEncode(enc, t.PackageId);
        },
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            if (!t.IsExternalLibraryImport)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("isExternalLibraryImport"));
            MarshalEncode(enc, t.IsExternalLibraryImport);
        },
        static (target, enc) =>
        {
            var t = (ResolvedModule)target;
            if (t.AlternateResult.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("alternateResult"));
            MarshalEncode(enc, t.AlternateResult);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolvedTypeReferenceDirective struct</c>（proto.go:1421）。</summary>
public sealed class ResolvedTypeReferenceDirective : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Primary bool `json:"primary"`</c>（proto.go:1422）。</summary>
    public bool Primary;

    /// <summary>Go: <c>ResolvedFileName string `json:"resolvedFileName"`</c>（proto.go:1423）。</summary>
    public string ResolvedFileName = "";

    /// <summary>Go: <c>OriginalPath string `json:"originalPath,omitempty"`</c>（proto.go:1424）。</summary>
    public string OriginalPath = "";

    /// <summary>Go: <c>PackageId *PackageId `json:"packageId,omitempty"`</c>（proto.go:1425）。</summary>
    public PackageId? PackageId;

    /// <summary>Go: <c>IsExternalLibraryImport bool `json:"isExternalLibraryImport,omitempty"`</c>（proto.go:1426）。</summary>
    public bool IsExternalLibraryImport;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("primary", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeReferenceDirective)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Primary = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolvedFileName", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeReferenceDirective)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolvedFileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("originalPath", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeReferenceDirective)target;
                if (dec.PeekKind() != JsonKind.Null) { t.OriginalPath = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("packageId", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeReferenceDirective)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.PackageId = null; }
                else { t.PackageId = DecodeNullable<PackageId>(dec, static d => d.DecodeNew<PackageId>()); }
            }
        ),
        new StructFieldSpec("isExternalLibraryImport", false, false,
            static (target, dec) =>
            {
                var t = (ResolvedTypeReferenceDirective)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsExternalLibraryImport = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolvedTypeReferenceDirective)target;
            enc.WriteToken(JsonToken.String("primary"));
            MarshalEncode(enc, t.Primary);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTypeReferenceDirective)target;
            enc.WriteToken(JsonToken.String("resolvedFileName"));
            MarshalEncode(enc, t.ResolvedFileName);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTypeReferenceDirective)target;
            if (t.OriginalPath.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("originalPath"));
            MarshalEncode(enc, t.OriginalPath);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTypeReferenceDirective)target;
            if (t.PackageId is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("packageId"));
            MarshalEncode(enc, t.PackageId);
        },
        static (target, enc) =>
        {
            var t = (ResolvedTypeReferenceDirective)target;
            if (!t.IsExternalLibraryImport)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("isExternalLibraryImport"));
            MarshalEncode(enc, t.IsExternalLibraryImport);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SourceFileMetadata struct</c>（proto.go:1430）：program 保存的单个源文件元数据。</summary>
public sealed class SourceFileMetadata : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>IsDefaultLibrary bool `json:"isDefaultLibrary"`</c>（proto.go:1431）。</summary>
    public bool IsDefaultLibrary;

    /// <summary>Go: <c>IsFromExternalLibrary bool `json:"isFromExternalLibrary"`</c>（proto.go:1432）。</summary>
    public bool IsFromExternalLibrary;

    /// <summary>Go: <c>PackageJsonType string `json:"packageJsonType"`</c>（proto.go:1433）。</summary>
    public string PackageJsonType = "";

    /// <summary>Go: <c>PackageJsonDirectory string `json:"packageJsonDirectory"`</c>（proto.go:1434）。</summary>
    public string PackageJsonDirectory = "";

    /// <summary>Go: <c>ImpliedNodeFormat core.ResolutionMode `json:"impliedNodeFormat"`</c>（proto.go:1435）→ `Core.ModuleKind`。</summary>
    public Core.ModuleKind ImpliedNodeFormat;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("isDefaultLibrary", false, false,
            static (target, dec) =>
            {
                var t = (SourceFileMetadata)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsDefaultLibrary = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("isFromExternalLibrary", false, false,
            static (target, dec) =>
            {
                var t = (SourceFileMetadata)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsFromExternalLibrary = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("packageJsonType", false, false,
            static (target, dec) =>
            {
                var t = (SourceFileMetadata)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PackageJsonType = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("packageJsonDirectory", false, false,
            static (target, dec) =>
            {
                var t = (SourceFileMetadata)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PackageJsonDirectory = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("impliedNodeFormat", false, false,
            static (target, dec) =>
            {
                var t = (SourceFileMetadata)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ImpliedNodeFormat = dec.Decode<Core.ModuleKind>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SourceFileMetadata)target;
            enc.WriteToken(JsonToken.String("isDefaultLibrary"));
            MarshalEncode(enc, t.IsDefaultLibrary);
        },
        static (target, enc) =>
        {
            var t = (SourceFileMetadata)target;
            enc.WriteToken(JsonToken.String("isFromExternalLibrary"));
            MarshalEncode(enc, t.IsFromExternalLibrary);
        },
        static (target, enc) =>
        {
            var t = (SourceFileMetadata)target;
            enc.WriteToken(JsonToken.String("packageJsonType"));
            MarshalEncode(enc, t.PackageJsonType);
        },
        static (target, enc) =>
        {
            var t = (SourceFileMetadata)target;
            enc.WriteToken(JsonToken.String("packageJsonDirectory"));
            MarshalEncode(enc, t.PackageJsonDirectory);
        },
        static (target, enc) =>
        {
            var t = (SourceFileMetadata)target;
            enc.WriteToken(JsonToken.String("impliedNodeFormat"));
            MarshalEncode(enc, t.ImpliedNodeFormat);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolveNameParams struct</c>（proto.go:1438）。</summary>
public sealed class ResolveNameParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1439）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1440）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Name string `json:"name"`</c>（proto.go:1441）。</summary>
    public string Name = "";

    /// <summary>Go: <c>Location NodeHandle `json:"location,omitempty"`</c>（proto.go:1442）：可选的定位上下文节点。</summary>
    public NodeHandle Location;

    /// <summary>Go: <c>File *DocumentIdentifier `json:"file,omitempty"`</c>（proto.go:1443）：可选的定位上下文文件
    /// （Location 的替代）。</summary>
    public DocumentIdentifier? File;

    /// <summary>Go: <c>Position *uint32 `json:"position,omitempty"`</c>（proto.go:1444）：文件内位置（与 File 搭配）。</summary>
    public uint? Position;

    /// <summary>Go: <c>Meaning uint32 `json:"meaning"`</c>（proto.go:1445）：要找的符号种类位集（SymbolFlags）。</summary>
    public uint Meaning;

    /// <summary>Go: <c>ExcludeGlobals bool `json:"excludeGlobals,omitempty"`</c>（proto.go:1446）：是否排除全局符号。</summary>
    public bool ExcludeGlobals;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.File = null; }
                else { t.File = DecodeNullable<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("position", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Position = null; }
                else { t.Position = dec.Decode<uint?>(); }
            }
        ),
        new StructFieldSpec("meaning", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Meaning = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("excludeGlobals", false, false,
            static (target, dec) =>
            {
                var t = (ResolveNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ExcludeGlobals = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            if (t.Location.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            if (t.File is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            if (t.Position is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("position"));
            MarshalEncode(enc, t.Position);
        },
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            enc.WriteToken(JsonToken.String("meaning"));
            MarshalEncode(enc, t.Meaning);
        },
        static (target, enc) =>
        {
            var t = (ResolveNameParams)target;
            if (!t.ExcludeGlobals)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("excludeGlobals"));
            MarshalEncode(enc, t.ExcludeGlobals);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolsInScopeParams struct</c>（proto.go:1451）：返回给定位置上所有可见符号。</summary>
public sealed class GetSymbolsInScopeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1452）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1453）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location,omitempty"`</c>（proto.go:1454）。</summary>
    public NodeHandle Location;

    /// <summary>Go: <c>File *DocumentIdentifier `json:"file,omitempty"`</c>（proto.go:1455）。</summary>
    public DocumentIdentifier? File;

    /// <summary>Go: <c>Position *uint32 `json:"position,omitempty"`</c>（proto.go:1456）。</summary>
    public uint? Position;

    /// <summary>Go: <c>Meaning uint32 `json:"meaning"`</c>（proto.go:1457）：要找的符号种类位集（SymbolFlags）。</summary>
    public uint Meaning;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsInScopeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsInScopeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsInScopeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsInScopeParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.File = null; }
                else { t.File = DecodeNullable<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("position", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsInScopeParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Position = null; }
                else { t.Position = dec.Decode<uint?>(); }
            }
        ),
        new StructFieldSpec("meaning", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolsInScopeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Meaning = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolsInScopeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsInScopeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsInScopeParams)target;
            if (t.Location.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsInScopeParams)target;
            if (t.File is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsInScopeParams)target;
            if (t.Position is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("position"));
            MarshalEncode(enc, t.Position);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolsInScopeParams)target;
            enc.WriteToken(JsonToken.String("meaning"));
            MarshalEncode(enc, t.Meaning);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypePropertyParams struct</c>（proto.go:1461）：所有 type 子属性端点共用。</summary>
public sealed class GetTypePropertyParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1462）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1463）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"objectId"`</c>（proto.go:1464）。</summary>
    public TypeID Type;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypePropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypePropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("objectId", false, false,
            static (target, dec) =>
            {
                var t = (GetTypePropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypePropertyParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypePropertyParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypePropertyParams)target;
            enc.WriteToken(JsonToken.String("objectId"));
            MarshalEncode(enc, t.Type);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSymbolPropertyParams struct</c>（proto.go:1468）：所有 symbol 子属性端点共用。</summary>
public sealed class GetSymbolPropertyParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1469）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1470）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Symbol SymbolID `json:"objectId"`</c>（proto.go:1471）。</summary>
    public SymbolID Symbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolPropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolPropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("objectId", false, false,
            static (target, dec) =>
            {
                var t = (GetSymbolPropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSymbolPropertyParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolPropertyParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSymbolPropertyParams)target;
            enc.WriteToken(JsonToken.String("objectId"));
            MarshalEncode(enc, t.Symbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSignaturePropertyParams struct</c>（proto.go:1475）：所有 signature 子属性端点共用。</summary>
public sealed class GetSignaturePropertyParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1476）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1477）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Signature SignatureID `json:"objectId"`</c>（proto.go:1478）。</summary>
    public SignatureID Signature;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSignaturePropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSignaturePropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("objectId", false, false,
            static (target, dec) =>
            {
                var t = (GetSignaturePropertyParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Signature = new SignatureID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSignaturePropertyParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSignaturePropertyParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSignaturePropertyParams)target;
            enc.WriteToken(JsonToken.String("objectId"));
            MarshalEncode(enc, t.Signature);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetContextualTypeParams struct</c>（proto.go:1482）：返回节点的上下文类型。</summary>
public sealed class GetContextualTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1483）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1484）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1485）。</summary>
    public NodeHandle Location;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetContextualTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetContextualTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetContextualTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetContextualTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetContextualTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetContextualTypeParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetContextualTypeForArgumentParams struct</c>（proto.go:1488）。</summary>
public sealed class GetContextualTypeForArgumentParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1489）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1490）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1491）。</summary>
    public NodeHandle Location;

    /// <summary>Go: <c>Index int32 `json:"index"`</c>（proto.go:1492）。</summary>
    public int Index;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetContextualTypeForArgumentParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetContextualTypeForArgumentParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetContextualTypeForArgumentParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("index", false, false,
            static (target, dec) =>
            {
                var t = (GetContextualTypeForArgumentParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Index = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetContextualTypeForArgumentParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetContextualTypeForArgumentParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetContextualTypeForArgumentParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
        static (target, enc) =>
        {
            var t = (GetContextualTypeForArgumentParams)target;
            enc.WriteToken(JsonToken.String("index"));
            MarshalEncode(enc, t.Index);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypeOfSymbolAtLocationParams struct</c>（proto.go:1496）：返回符号在指定位置收窄后的类型。</summary>
public sealed class GetTypeOfSymbolAtLocationParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1497）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1498）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Symbol SymbolID `json:"symbol"`</c>（proto.go:1499）。</summary>
    public SymbolID Symbol;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1500）。</summary>
    public NodeHandle Location;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeOfSymbolAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeOfSymbolAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeOfSymbolAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeOfSymbolAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypeOfSymbolAtLocationParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypeOfSymbolAtLocationParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypeOfSymbolAtLocationParams)target;
            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
        static (target, enc) =>
        {
            var t = (GetTypeOfSymbolAtLocationParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetReferencesToSymbolInFileParams struct</c>（proto.go:1504）。</summary>
public sealed class GetReferencesToSymbolInFileParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1505）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1506）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1507）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Symbol SymbolID `json:"symbol"`</c>（proto.go:1508）。</summary>
    public SymbolID Symbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencesToSymbolInFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencesToSymbolInFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencesToSymbolInFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencesToSymbolInFileParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetReferencesToSymbolInFileParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetReferencesToSymbolInFileParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetReferencesToSymbolInFileParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetReferencesToSymbolInFileParams)target;
            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetReferencedSymbolsForNodeParams struct</c>（proto.go:1512）。</summary>
public sealed class GetReferencedSymbolsForNodeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1513）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1514）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Node NodeHandle `json:"node"`</c>（proto.go:1515）。</summary>
    public NodeHandle Node;

    /// <summary>Go: <c>Position int `json:"position"`</c>（proto.go:1516）。</summary>
    public int Position;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencedSymbolsForNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencedSymbolsForNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("node", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencedSymbolsForNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Node = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("position", false, false,
            static (target, dec) =>
            {
                var t = (GetReferencedSymbolsForNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Position = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetReferencedSymbolsForNodeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetReferencedSymbolsForNodeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetReferencedSymbolsForNodeParams)target;
            enc.WriteToken(JsonToken.String("node"));
            MarshalEncode(enc, t.Node);
        },
        static (target, enc) =>
        {
            var t = (GetReferencedSymbolsForNodeParams)target;
            enc.WriteToken(JsonToken.String("position"));
            MarshalEncode(enc, t.Position);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ReferencedSymbolEntry struct</c>（proto.go:1520）：一个符号定义及其引用。</summary>
public sealed class ReferencedSymbolEntry : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Definition NodeHandle `json:"definition"`</c>（proto.go:1521）。</summary>
    public NodeHandle Definition;

    /// <summary>Go: <c>Symbol *SymbolResponse `json:"symbol,omitempty"`</c>（proto.go:1522）。</summary>
    public SymbolResponse? Symbol;

    /// <summary>Go: <c>References []NodeHandle `json:"references" nonnil:"true"`</c>（proto.go:1523）。</summary>
    public NodeHandle[] References = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("definition", false, false,
            static (target, dec) =>
            {
                var t = (ReferencedSymbolEntry)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Definition = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (ReferencedSymbolEntry)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Symbol = null; }
                else { t.Symbol = DecodeNullable<SymbolResponse>(dec, static d => d.DecodeNew<SymbolResponse>()); }
            }
        ),
        new StructFieldSpec("references", false, false,
            static (target, dec) =>
            {
                var t = (ReferencedSymbolEntry)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.References = null!; }
                else { t.References = DecodeArray<NodeHandle>(dec, static d => new NodeHandle(d.Decode<string>() ?? ""))!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ReferencedSymbolEntry)target;
            enc.WriteToken(JsonToken.String("definition"));
            MarshalEncode(enc, t.Definition);
        },
        static (target, enc) =>
        {
            var t = (ReferencedSymbolEntry)target;
            if (t.Symbol is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
        static (target, enc) =>
        {
            var t = (ReferencedSymbolEntry)target;
            enc.WriteToken(JsonToken.String("references"));
            if (t.References is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.References)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSignatureUsagesParams struct</c>（proto.go:1527）。</summary>
public sealed class GetSignatureUsagesParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1528）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1529）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>SignatureDecl NodeHandle `json:"signatureDecl"`</c>（proto.go:1530）。</summary>
    public NodeHandle SignatureDecl;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSignatureUsagesParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSignatureUsagesParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("signatureDecl", false, false,
            static (target, dec) =>
            {
                var t = (GetSignatureUsagesParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.SignatureDecl = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSignatureUsagesParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSignatureUsagesParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSignatureUsagesParams)target;
            enc.WriteToken(JsonToken.String("signatureDecl"));
            MarshalEncode(enc, t.SignatureDecl);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SignatureUsageResponse struct</c>（proto.go:1534）：签名的一次使用（名字-调用点对）。</summary>
public sealed class SignatureUsageResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Name NodeHandle `json:"name"`</c>（proto.go:1535）。</summary>
    public NodeHandle Name;

    /// <summary>Go: <c>Call NodeHandle `json:"call,omitempty"`</c>（proto.go:1536）。</summary>
    public NodeHandle Call;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (SignatureUsageResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("call", false, false,
            static (target, dec) =>
            {
                var t = (SignatureUsageResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Call = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SignatureUsageResponse)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
        static (target, enc) =>
        {
            var t = (SignatureUsageResponse)target;
            if (t.Call.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("call"));
            MarshalEncode(enc, t.Call);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetCompletionsAtPositionParams struct</c>（proto.go:1540）。</summary>
public sealed class GetCompletionsAtPositionParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1541）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1542）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1543）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Position uint32 `json:"position"`</c>（proto.go:1544）。</summary>
    public uint Position;

    /// <summary>Go: <c>TriggerCharacter *string `json:"triggerCharacter,omitempty"`</c>（proto.go:1545）。</summary>
    public string? TriggerCharacter;

    /// <summary>Go: <c>IncludeSymbol bool `json:"includeSymbol,omitempty"`</c>（proto.go:1546）。</summary>
    public bool IncludeSymbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetCompletionsAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetCompletionsAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetCompletionsAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("position", false, false,
            static (target, dec) =>
            {
                var t = (GetCompletionsAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Position = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("triggerCharacter", false, false,
            static (target, dec) =>
            {
                var t = (GetCompletionsAtPositionParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.TriggerCharacter = null; }
                else { t.TriggerCharacter = dec.Decode<string>(); }
            }
        ),
        new StructFieldSpec("includeSymbol", false, false,
            static (target, dec) =>
            {
                var t = (GetCompletionsAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IncludeSymbol = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetCompletionsAtPositionParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetCompletionsAtPositionParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetCompletionsAtPositionParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetCompletionsAtPositionParams)target;
            enc.WriteToken(JsonToken.String("position"));
            MarshalEncode(enc, t.Position);
        },
        static (target, enc) =>
        {
            var t = (GetCompletionsAtPositionParams)target;
            if (t.TriggerCharacter is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("triggerCharacter"));
            MarshalEncode(enc, t.TriggerCharacter);
        },
        static (target, enc) =>
        {
            var t = (GetCompletionsAtPositionParams)target;
            if (!t.IncludeSymbol)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("includeSymbol"));
            MarshalEncode(enc, t.IncludeSymbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CompletionEntryLabelDetailsResponse struct</c>（proto.go:1550）：补全项的附加标签文本。</summary>
public sealed class CompletionEntryLabelDetailsResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Detail *string `json:"detail,omitempty"`</c>（proto.go:1551）。</summary>
    public string? Detail;

    /// <summary>Go: <c>Description *string `json:"description,omitempty"`</c>（proto.go:1552）。</summary>
    public string? Description;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("detail", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryLabelDetailsResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Detail = null; }
                else { t.Detail = dec.Decode<string>(); }
            }
        ),
        new StructFieldSpec("description", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryLabelDetailsResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Description = null; }
                else { t.Description = dec.Decode<string>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CompletionEntryLabelDetailsResponse)target;
            if (t.Detail is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("detail"));
            MarshalEncode(enc, t.Detail);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryLabelDetailsResponse)target;
            if (t.Description is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("description"));
            MarshalEncode(enc, t.Description);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CompletionEntryResponse struct</c>（proto.go:1556）：单个补全项。</summary>
public sealed class CompletionEntryResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Name string `json:"name"`</c>（proto.go:1557）。</summary>
    public string Name = "";

    /// <summary>Go: <c>Kind uint32 `json:"kind,omitempty"`</c>（proto.go:1558）。</summary>
    public uint Kind;

    /// <summary>Go: <c>SortText *string `json:"sortText,omitempty"`</c>（proto.go:1559）。</summary>
    public string? SortText;

    /// <summary>Go: <c>InsertText *string `json:"insertText,omitempty"`</c>（proto.go:1560）。</summary>
    public string? InsertText;

    /// <summary>Go: <c>FilterText *string `json:"filterText,omitempty"`</c>（proto.go:1561）。</summary>
    public string? FilterText;

    /// <summary>Go: <c>Detail *string `json:"detail,omitempty"`</c>（proto.go:1562）。</summary>
    public string? Detail;

    /// <summary>Go: <c>LabelDetails *CompletionEntryLabelDetailsResponse `json:"labelDetails,omitempty"`</c>（proto.go:1563）。</summary>
    public CompletionEntryLabelDetailsResponse? LabelDetails;

    /// <summary>Go: <c>Symbol *SymbolResponse `json:"symbol,omitempty"`</c>（proto.go:1564）。</summary>
    public SymbolResponse? Symbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("kind", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Kind = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("sortText", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.SortText = null; }
                else { t.SortText = dec.Decode<string>(); }
            }
        ),
        new StructFieldSpec("insertText", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.InsertText = null; }
                else { t.InsertText = dec.Decode<string>(); }
            }
        ),
        new StructFieldSpec("filterText", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.FilterText = null; }
                else { t.FilterText = dec.Decode<string>(); }
            }
        ),
        new StructFieldSpec("detail", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Detail = null; }
                else { t.Detail = dec.Decode<string>(); }
            }
        ),
        new StructFieldSpec("labelDetails", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.LabelDetails = null; }
                else { t.LabelDetails = DecodeNullable<CompletionEntryLabelDetailsResponse>(dec, static d => d.DecodeNew<CompletionEntryLabelDetailsResponse>()); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (CompletionEntryResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Symbol = null; }
                else { t.Symbol = DecodeNullable<SymbolResponse>(dec, static d => d.DecodeNew<SymbolResponse>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            if (t.Kind == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("kind"));
            MarshalEncode(enc, t.Kind);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            if (t.SortText is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("sortText"));
            MarshalEncode(enc, t.SortText);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            if (t.InsertText is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("insertText"));
            MarshalEncode(enc, t.InsertText);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            if (t.FilterText is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("filterText"));
            MarshalEncode(enc, t.FilterText);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            if (t.Detail is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("detail"));
            MarshalEncode(enc, t.Detail);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            if (t.LabelDetails is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("labelDetails"));
            MarshalEncode(enc, t.LabelDetails);
        },
        static (target, enc) =>
        {
            var t = (CompletionEntryResponse)target;
            if (t.Symbol is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CompletionInfoResponse struct</c>（proto.go:1568）：补全项列表的包装。</summary>
public sealed class CompletionInfoResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>IsIncomplete bool `json:"isIncomplete"`</c>（proto.go:1569）。</summary>
    public bool IsIncomplete;

    /// <summary>Go: <c>Entries []*CompletionEntryResponse `json:"entries" nonnil:"true"`</c>（proto.go:1570）。</summary>
    public CompletionEntryResponse[] Entries = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("isIncomplete", false, false,
            static (target, dec) =>
            {
                var t = (CompletionInfoResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsIncomplete = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("entries", false, false,
            static (target, dec) =>
            {
                var t = (CompletionInfoResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Entries = null!; }
                else { t.Entries = DecodeArray<CompletionEntryResponse?>(dec, static d => DecodeNullable<CompletionEntryResponse>(d, static d => d.DecodeNew<CompletionEntryResponse>()))!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CompletionInfoResponse)target;
            enc.WriteToken(JsonToken.String("isIncomplete"));
            MarshalEncode(enc, t.IsIncomplete);
        },
        static (target, enc) =>
        {
            var t = (CompletionInfoResponse)target;
            enc.WriteToken(JsonToken.String("entries"));
            if (t.Entries is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Entries)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetIntrinsicTypeParams struct</c>（proto.go:1574）：内建类型 getter（anyType、stringType 等）共用。</summary>
public sealed class GetIntrinsicTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1575）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1576）。</summary>
    public TypeScript.Project.ID Project;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetIntrinsicTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetIntrinsicTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetIntrinsicTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetIntrinsicTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type WellKnownSymbolsResponse struct</c>（proto.go:1582）：每个 checker 的单例符号
/// （unknown、undefined、arguments）句柄，客户端不必为它们每次往返。</summary>
public sealed class WellKnownSymbolsResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Unknown SymbolID `json:"unknown"`</c>（proto.go:1583）。</summary>
    public SymbolID Unknown;

    /// <summary>Go: <c>Undefined SymbolID `json:"undefined"`</c>（proto.go:1584）。</summary>
    public SymbolID Undefined;

    /// <summary>Go: <c>Arguments SymbolID `json:"arguments"`</c>（proto.go:1585）。</summary>
    public SymbolID Arguments;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("unknown", false, false,
            static (target, dec) =>
            {
                var t = (WellKnownSymbolsResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Unknown = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("undefined", false, false,
            static (target, dec) =>
            {
                var t = (WellKnownSymbolsResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Undefined = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("arguments", false, false,
            static (target, dec) =>
            {
                var t = (WellKnownSymbolsResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Arguments = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (WellKnownSymbolsResponse)target;
            enc.WriteToken(JsonToken.String("unknown"));
            MarshalEncode(enc, t.Unknown);
        },
        static (target, enc) =>
        {
            var t = (WellKnownSymbolsResponse)target;
            enc.WriteToken(JsonToken.String("undefined"));
            MarshalEncode(enc, t.Undefined);
        },
        static (target, enc) =>
        {
            var t = (WellKnownSymbolsResponse)target;
            enc.WriteToken(JsonToken.String("arguments"));
            MarshalEncode(enc, t.Arguments);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type WellKnownSignaturesResponse struct</c>（proto.go:1591）：每个 checker 的单例 unknown 签名句柄。</summary>
public sealed class WellKnownSignaturesResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Unknown SignatureID `json:"unknown"`</c>（proto.go:1592）。</summary>
    public SignatureID Unknown;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("unknown", false, false,
            static (target, dec) =>
            {
                var t = (WellKnownSignaturesResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Unknown = new SignatureID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (WellKnownSignaturesResponse)target;
            enc.WriteToken(JsonToken.String("unknown"));
            MarshalEncode(enc, t.Unknown);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetBaseTypeOfLiteralTypeParams struct</c>（proto.go:1596）：返回字面量类型的基础类型。</summary>
public sealed class GetBaseTypeOfLiteralTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1597）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1598）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1599）。</summary>
    public TypeID Type;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetBaseTypeOfLiteralTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetBaseTypeOfLiteralTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (GetBaseTypeOfLiteralTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetBaseTypeOfLiteralTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetBaseTypeOfLiteralTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetBaseTypeOfLiteralTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetNonNullableTypeParams struct</c>（proto.go:1603）。</summary>
public sealed class GetNonNullableTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1604）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1605）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1606）。</summary>
    public TypeID Type;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetNonNullableTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetNonNullableTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (GetNonNullableTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetNonNullableTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetNonNullableTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetNonNullableTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypeFromTypeNodeParams struct</c>（proto.go:1610）。</summary>
public sealed class GetTypeFromTypeNodeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1611）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1612）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1613）。</summary>
    public NodeHandle Location;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeFromTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeFromTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeFromTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypeFromTypeNodeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypeFromTypeNodeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypeFromTypeNodeParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetWidenedTypeParams struct</c>（proto.go:1617）。</summary>
public sealed class GetWidenedTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1618）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1619）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1620）。</summary>
    public TypeID Type;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetWidenedTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetWidenedTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (GetWidenedTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetWidenedTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetWidenedTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetWidenedTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetParameterTypeParams struct</c>（proto.go:1624）。</summary>
public sealed class GetParameterTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1625）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1626）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Signature SignatureID `json:"signature"`</c>（proto.go:1627）。</summary>
    public SignatureID Signature;

    /// <summary>Go: <c>Index int32 `json:"index"`</c>（proto.go:1628）。</summary>
    public int Index;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetParameterTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetParameterTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("signature", false, false,
            static (target, dec) =>
            {
                var t = (GetParameterTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Signature = new SignatureID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("index", false, false,
            static (target, dec) =>
            {
                var t = (GetParameterTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Index = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetParameterTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetParameterTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetParameterTypeParams)target;
            enc.WriteToken(JsonToken.String("signature"));
            MarshalEncode(enc, t.Signature);
        },
        static (target, enc) =>
        {
            var t = (GetParameterTypeParams)target;
            enc.WriteToken(JsonToken.String("index"));
            MarshalEncode(enc, t.Index);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type IsArrayLikeTypeParams struct</c>（proto.go:1632）：判断类型是否 array-like。</summary>
public sealed class IsArrayLikeTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1633）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1634）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1635）。</summary>
    public TypeID Type;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (IsArrayLikeTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (IsArrayLikeTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (IsArrayLikeTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (IsArrayLikeTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (IsArrayLikeTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (IsArrayLikeTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type IsTypeAssignableToParams struct</c>（proto.go:1639）：判断两个类型之间的可赋值性。</summary>
public sealed class IsTypeAssignableToParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1640）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1641）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Source TypeID `json:"source"`</c>（proto.go:1642）。</summary>
    public TypeID Source;

    /// <summary>Go: <c>Target TypeID `json:"target"`</c>（proto.go:1643）。</summary>
    public TypeID Target;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (IsTypeAssignableToParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (IsTypeAssignableToParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("source", false, false,
            static (target, dec) =>
            {
                var t = (IsTypeAssignableToParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Source = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("target", false, false,
            static (target, dec) =>
            {
                var t = (IsTypeAssignableToParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Target = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (IsTypeAssignableToParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (IsTypeAssignableToParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (IsTypeAssignableToParams)target;
            enc.WriteToken(JsonToken.String("source"));
            MarshalEncode(enc, t.Source);
        },
        static (target, enc) =>
        {
            var t = (IsTypeAssignableToParams)target;
            enc.WriteToken(JsonToken.String("target"));
            MarshalEncode(enc, t.Target);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetSignaturesOfTypeParams struct</c>（proto.go:1646）。</summary>
public sealed class GetSignaturesOfTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1647）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1648）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1649）。</summary>
    public TypeID Type;

    /// <summary>Go: <c>Kind int32 `json:"kind"`</c>（proto.go:1650）。</summary>
    public int Kind;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetSignaturesOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetSignaturesOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (GetSignaturesOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("kind", false, false,
            static (target, dec) =>
            {
                var t = (GetSignaturesOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Kind = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetSignaturesOfTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetSignaturesOfTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetSignaturesOfTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
        static (target, enc) =>
        {
            var t = (GetSignaturesOfTypeParams)target;
            enc.WriteToken(JsonToken.String("kind"));
            MarshalEncode(enc, t.Kind);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetResolvedSignatureParams struct</c>（proto.go:1653）。</summary>
public sealed class GetResolvedSignatureParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1654）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1655）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1656）。</summary>
    public NodeHandle Location;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedSignatureParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedSignatureParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetResolvedSignatureParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetResolvedSignatureParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedSignatureParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetResolvedSignatureParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypeAtLocationParams struct</c>（proto.go:1659）。</summary>
public sealed class GetTypeAtLocationParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1660）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1661）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1662）。</summary>
    public NodeHandle Location;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtLocationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypeAtLocationParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypeAtLocationParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypeAtLocationParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypeAtLocationsParams struct</c>（proto.go:1665）。</summary>
public sealed class GetTypeAtLocationsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1666）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1667）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Locations []NodeHandle `json:"locations"`</c>（proto.go:1668）。</summary>
    public NodeHandle[] Locations = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtLocationsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtLocationsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("locations", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtLocationsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Locations = null!; }
                else { t.Locations = DecodeArray<NodeHandle>(dec, static d => new NodeHandle(d.Decode<string>() ?? ""))!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypeAtLocationsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypeAtLocationsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypeAtLocationsParams)target;
            enc.WriteToken(JsonToken.String("locations"));
            if (t.Locations is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Locations)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypeAtPositionParams struct</c>（proto.go:1671）。</summary>
public sealed class GetTypeAtPositionParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1672）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1673）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1674）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Position uint32 `json:"position"`</c>（proto.go:1675）。</summary>
    public uint Position;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("position", false, false,
            static (target, dec) =>
            {
                var t = (GetTypeAtPositionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Position = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypeAtPositionParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypeAtPositionParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypeAtPositionParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetTypeAtPositionParams)target;
            enc.WriteToken(JsonToken.String("position"));
            MarshalEncode(enc, t.Position);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetTypesAtPositionsParams struct</c>（proto.go:1678）。</summary>
public sealed class GetTypesAtPositionsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1679）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1680）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1681）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Positions []uint32 `json:"positions"`</c>（proto.go:1682）。</summary>
    public uint[] Positions = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetTypesAtPositionsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetTypesAtPositionsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetTypesAtPositionsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("positions", false, false,
            static (target, dec) =>
            {
                var t = (GetTypesAtPositionsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Positions = null!; }
                else { t.Positions = DecodeArray<uint>(dec, static d => d.Decode<uint>())!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetTypesAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetTypesAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetTypesAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetTypesAtPositionsParams)target;
            enc.WriteToken(JsonToken.String("positions"));
            if (t.Positions is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Positions)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ImportAdderActionKind string</c>（proto.go:1685）+ `ImportAdderActionKindImportSymbol`（proto.go:1688）。
/// C# 落成 readonly struct + 静态单例，JSON 里是字符串（先例 LsProto 的 FoldingRangeKind，见文件头第 13 条）。</summary>
public readonly struct ImportAdderActionKind : IEquatable<ImportAdderActionKind>, IJsonMarshaler
{
    private readonly string? _value;

    /// <summary>Go 的 <c>ImportAdderActionKind(value)</c> 转换（入向 implicit、出向 explicit，与 LsProto 的枚举新类型一致）。</summary>
    public ImportAdderActionKind(string value) => _value = value;

    /// <summary>Go 的 <c>string(value)</c>。</summary>
    public string Value => _value ?? "";

    public static implicit operator ImportAdderActionKind(string value) => new(value);

    public static explicit operator string(ImportAdderActionKind value) => value.Value;

    public override string ToString() => Value;

    public bool Equals(ImportAdderActionKind other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is ImportAdderActionKind other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static bool operator ==(ImportAdderActionKind left, ImportAdderActionKind right) => left.Equals(right);

    public static bool operator !=(ImportAdderActionKind left, ImportAdderActionKind right) => !left.Equals(right);

    /// <summary>Go: <c>func (k ImportAdderActionKind) MarshalJSONTo(enc *json.Encoder) error</c>：命名 string 类型按字符串写出。</summary>
    public void MarshalJSONTo(JsonEncoder encoder) => encoder.WriteToken(JsonToken.String(Value));

    /// <summary>Go: <c>ImportAdderActionKindImportSymbol ImportAdderActionKind = "importSymbol"</c>（proto.go:1688）。</summary>
    public static readonly ImportAdderActionKind ImportSymbol = new("importSymbol");
}

/// <summary>Go: <c>type ImportAdderAction struct</c>（proto.go:1691）。</summary>
public sealed class ImportAdderAction : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Kind ImportAdderActionKind `json:"kind"`</c>（proto.go:1692）。</summary>
    public ImportAdderActionKind Kind;

    /// <summary>Go: <c>Symbol SymbolID `json:"symbol,omitempty"`</c>（proto.go:1693）。</summary>
    public SymbolID Symbol;

    /// <summary>Go: <c>IsValidTypeOnlyUseSite *bool `json:"isValidTypeOnlyUseSite,omitempty"`</c>（proto.go:1694）。</summary>
    public bool? IsValidTypeOnlyUseSite;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("kind", false, false,
            static (target, dec) =>
            {
                var t = (ImportAdderAction)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Kind = new ImportAdderActionKind(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (ImportAdderAction)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("isValidTypeOnlyUseSite", false, false,
            static (target, dec) =>
            {
                var t = (ImportAdderAction)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.IsValidTypeOnlyUseSite = null; }
                else { t.IsValidTypeOnlyUseSite = dec.Decode<bool?>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ImportAdderAction)target;
            enc.WriteToken(JsonToken.String("kind"));
            MarshalEncode(enc, t.Kind);
        },
        static (target, enc) =>
        {
            var t = (ImportAdderAction)target;
            if (t.Symbol.Value == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
        static (target, enc) =>
        {
            var t = (ImportAdderAction)target;
            if (t.IsValidTypeOnlyUseSite is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("isValidTypeOnlyUseSite"));
            MarshalEncode(enc, t.IsValidTypeOnlyUseSite);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetImportAdderEditsParams struct</c>（proto.go:1697）。</summary>
public sealed class GetImportAdderEditsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1698）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1699）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1700）。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Actions []ImportAdderAction `json:"actions"`</c>（proto.go:1701）。</summary>
    public ImportAdderAction[] Actions = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetImportAdderEditsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetImportAdderEditsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (GetImportAdderEditsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("actions", false, false,
            static (target, dec) =>
            {
                var t = (GetImportAdderEditsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Actions = null!; }
                else { t.Actions = DecodeArray<ImportAdderAction>(dec, static d => d.DecodeNew<ImportAdderAction>())!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetImportAdderEditsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetImportAdderEditsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetImportAdderEditsParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (GetImportAdderEditsParams)target;
            enc.WriteToken(JsonToken.String("actions"));
            if (t.Actions is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Actions)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TextEdit struct</c>（proto.go:1704）：API 的文本编辑（pos/end UTF-16 偏移，非 LSP 的 TextEdit）。</summary>
public sealed class TextEdit : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Pos int `json:"pos"`</c>（proto.go:1705）。</summary>
    public int Pos;

    /// <summary>Go: <c>End int `json:"end"`</c>（proto.go:1706）。</summary>
    public int End;

    /// <summary>Go: <c>NewText string `json:"newText"`</c>（proto.go:1707）。</summary>
    public string NewText = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("pos", false, false,
            static (target, dec) =>
            {
                var t = (TextEdit)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Pos = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("end", false, false,
            static (target, dec) =>
            {
                var t = (TextEdit)target;
                if (dec.PeekKind() != JsonKind.Null) { t.End = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("newText", false, false,
            static (target, dec) =>
            {
                var t = (TextEdit)target;
                if (dec.PeekKind() != JsonKind.Null) { t.NewText = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TextEdit)target;
            enc.WriteToken(JsonToken.String("pos"));
            MarshalEncode(enc, t.Pos);
        },
        static (target, enc) =>
        {
            var t = (TextEdit)target;
            enc.WriteToken(JsonToken.String("end"));
            MarshalEncode(enc, t.End);
        },
        static (target, enc) =>
        {
            var t = (TextEdit)target;
            enc.WriteToken(JsonToken.String("newText"));
            MarshalEncode(enc, t.NewText);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TypeToTypeNodeParams struct</c>（proto.go:1711）。</summary>
public sealed class TypeToTypeNodeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1712）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1713）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1714）。</summary>
    public TypeID Type;

    /// <summary>Go: <c>Location NodeHandle `json:"location,omitempty"`</c>（proto.go:1715）。</summary>
    public NodeHandle Location;

    /// <summary>Go: <c>Flags int32 `json:"flags,omitempty"`</c>（proto.go:1716）。</summary>
    public int Flags;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (TypeToTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (TypeToTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (TypeToTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (TypeToTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("flags", false, false,
            static (target, dec) =>
            {
                var t = (TypeToTypeNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Flags = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TypeToTypeNodeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (TypeToTypeNodeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (TypeToTypeNodeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
        static (target, enc) =>
        {
            var t = (TypeToTypeNodeParams)target;
            if (t.Location.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
        static (target, enc) =>
        {
            var t = (TypeToTypeNodeParams)target;
            if (t.Flags == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("flags"));
            MarshalEncode(enc, t.Flags);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SignatureToSignatureDeclarationParams struct</c>（proto.go:1720）。</summary>
public sealed class SignatureToSignatureDeclarationParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1721）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1722）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Signature SignatureID `json:"signature"`</c>（proto.go:1723）。</summary>
    public SignatureID Signature;

    /// <summary>Go: <c>Kind int32 `json:"kind"`</c>（proto.go:1724）。</summary>
    public int Kind;

    /// <summary>Go: <c>Location NodeHandle `json:"location,omitempty"`</c>（proto.go:1725）。</summary>
    public NodeHandle Location;

    /// <summary>Go: <c>Flags int32 `json:"flags,omitempty"`</c>（proto.go:1726）。</summary>
    public int Flags;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (SignatureToSignatureDeclarationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (SignatureToSignatureDeclarationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("signature", false, false,
            static (target, dec) =>
            {
                var t = (SignatureToSignatureDeclarationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Signature = new SignatureID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("kind", false, false,
            static (target, dec) =>
            {
                var t = (SignatureToSignatureDeclarationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Kind = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (SignatureToSignatureDeclarationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("flags", false, false,
            static (target, dec) =>
            {
                var t = (SignatureToSignatureDeclarationParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Flags = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SignatureToSignatureDeclarationParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (SignatureToSignatureDeclarationParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (SignatureToSignatureDeclarationParams)target;
            enc.WriteToken(JsonToken.String("signature"));
            MarshalEncode(enc, t.Signature);
        },
        static (target, enc) =>
        {
            var t = (SignatureToSignatureDeclarationParams)target;
            enc.WriteToken(JsonToken.String("kind"));
            MarshalEncode(enc, t.Kind);
        },
        static (target, enc) =>
        {
            var t = (SignatureToSignatureDeclarationParams)target;
            if (t.Location.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
        static (target, enc) =>
        {
            var t = (SignatureToSignatureDeclarationParams)target;
            if (t.Flags == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("flags"));
            MarshalEncode(enc, t.Flags);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type PrintNodeParams struct</c>（proto.go:1730）。</summary>
public sealed class PrintNodeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Data string `json:"data"`</c>（proto.go:1731）：base64 编码的二进制 AST 数据。</summary>
    public string Data = "";

    /// <summary>Go: <c>PreserveSourceNewlines bool `json:"preserveSourceNewlines,omitempty"`</c>（proto.go:1732）。</summary>
    public bool PreserveSourceNewlines;

    /// <summary>Go: <c>NeverAsciiEscape bool `json:"neverAsciiEscape,omitempty"`</c>（proto.go:1733）。</summary>
    public bool NeverAsciiEscape;

    /// <summary>Go: <c>TerminateUnterminatedLiterals bool `json:"terminateUnterminatedLiterals,omitempty"`</c>（proto.go:1734）。</summary>
    public bool TerminateUnterminatedLiterals;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("data", false, false,
            static (target, dec) =>
            {
                var t = (PrintNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Data = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("preserveSourceNewlines", false, false,
            static (target, dec) =>
            {
                var t = (PrintNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.PreserveSourceNewlines = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("neverAsciiEscape", false, false,
            static (target, dec) =>
            {
                var t = (PrintNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.NeverAsciiEscape = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("terminateUnterminatedLiterals", false, false,
            static (target, dec) =>
            {
                var t = (PrintNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.TerminateUnterminatedLiterals = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (PrintNodeParams)target;
            enc.WriteToken(JsonToken.String("data"));
            MarshalEncode(enc, t.Data);
        },
        static (target, enc) =>
        {
            var t = (PrintNodeParams)target;
            if (!t.PreserveSourceNewlines)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("preserveSourceNewlines"));
            MarshalEncode(enc, t.PreserveSourceNewlines);
        },
        static (target, enc) =>
        {
            var t = (PrintNodeParams)target;
            if (!t.NeverAsciiEscape)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("neverAsciiEscape"));
            MarshalEncode(enc, t.NeverAsciiEscape);
        },
        static (target, enc) =>
        {
            var t = (PrintNodeParams)target;
            if (!t.TerminateUnterminatedLiterals)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("terminateUnterminatedLiterals"));
            MarshalEncode(enc, t.TerminateUnterminatedLiterals);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type EmitParams struct</c>（proto.go:1737）。</summary>
public sealed class EmitParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1738）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1739）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>EmitOnly *uint32 `json:"emitOnly,omitempty"`</c>（proto.go:1740）。</summary>
    public uint? EmitOnly;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (EmitParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (EmitParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("emitOnly", false, false,
            static (target, dec) =>
            {
                var t = (EmitParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.EmitOnly = null; }
                else { t.EmitOnly = dec.Decode<uint?>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (EmitParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (EmitParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (EmitParams)target;
            if (t.EmitOnly is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("emitOnly"));
            MarshalEncode(enc, t.EmitOnly);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SelectedFilesEmitParams struct</c>（proto.go:1743）。</summary>
public sealed class SelectedFilesEmitParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1744）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1745）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Files []DocumentIdentifier `json:"files"`</c>（proto.go:1746）。</summary>
    public DocumentIdentifier[] Files = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (SelectedFilesEmitParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (SelectedFilesEmitParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("files", false, false,
            static (target, dec) =>
            {
                var t = (SelectedFilesEmitParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Files = null!; }
                else { t.Files = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>())!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SelectedFilesEmitParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (SelectedFilesEmitParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (SelectedFilesEmitParams)target;
            enc.WriteToken(JsonToken.String("files"));
            if (t.Files is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Files)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type EmitResponse struct</c>（proto.go:1749）。</summary>
public sealed class EmitResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>EmitSkipped bool `json:"emitSkipped"`</c>（proto.go:1750）。</summary>
    public bool EmitSkipped;

    /// <summary>Go: <c>Diagnostics []*DiagnosticResponse `json:"diagnostics" nonnil:"true"`</c>（proto.go:1751）。</summary>
    public DiagnosticResponse[] Diagnostics = [];

    /// <summary>Go: <c>EmittedFiles []string `json:"emittedFiles" nonnil:"true"`</c>（proto.go:1752）。</summary>
    public string[] EmittedFiles = [];

    /// <summary>Go: <c>EmittedFilesContents []string `json:"emittedFilesContents" nonnil:"true"`</c>（proto.go:1755）：
    /// 源快照使用完整文件系统时与 EmittedFiles 平行；write-through emit 时为空。</summary>
    public string[] EmittedFilesContents = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("emitSkipped", false, false,
            static (target, dec) =>
            {
                var t = (EmitResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.EmitSkipped = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("diagnostics", false, false,
            static (target, dec) =>
            {
                var t = (EmitResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Diagnostics = null!; }
                else { t.Diagnostics = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
        new StructFieldSpec("emittedFiles", false, false,
            static (target, dec) =>
            {
                var t = (EmitResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.EmittedFiles = null!; }
                else { t.EmittedFiles = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "")!; }
            }
        ),
        new StructFieldSpec("emittedFilesContents", false, false,
            static (target, dec) =>
            {
                var t = (EmitResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.EmittedFilesContents = null!; }
                else { t.EmittedFilesContents = DecodeArray<string>(dec, static d => d.Decode<string>() ?? "")!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (EmitResponse)target;
            enc.WriteToken(JsonToken.String("emitSkipped"));
            MarshalEncode(enc, t.EmitSkipped);
        },
        static (target, enc) =>
        {
            var t = (EmitResponse)target;
            enc.WriteToken(JsonToken.String("diagnostics"));
            if (t.Diagnostics is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Diagnostics)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (EmitResponse)target;
            enc.WriteToken(JsonToken.String("emittedFiles"));
            if (t.EmittedFiles is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.EmittedFiles)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (EmitResponse)target;
            enc.WriteToken(JsonToken.String("emittedFilesContents"));
            if (t.EmittedFilesContents is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.EmittedFilesContents)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type EmitOutputFile struct</c>（proto.go:1758）。</summary>
public sealed class EmitOutputFile : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>FileName string `json:"fileName"`</c>（proto.go:1759）。</summary>
    public string FileName = "";

    /// <summary>Go: <c>Text string `json:"text"`</c>（proto.go:1760）。</summary>
    public string Text = "";

    /// <summary>Go: <c>SourceFileName *string `json:"sourceFileName,omitempty"`</c>（proto.go:1761）。</summary>
    public string? SourceFileName;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("fileName", false, false,
            static (target, dec) =>
            {
                var t = (EmitOutputFile)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("text", false, false,
            static (target, dec) =>
            {
                var t = (EmitOutputFile)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Text = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("sourceFileName", false, false,
            static (target, dec) =>
            {
                var t = (EmitOutputFile)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.SourceFileName = null; }
                else { t.SourceFileName = dec.Decode<string>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (EmitOutputFile)target;
            enc.WriteToken(JsonToken.String("fileName"));
            MarshalEncode(enc, t.FileName);
        },
        static (target, enc) =>
        {
            var t = (EmitOutputFile)target;
            enc.WriteToken(JsonToken.String("text"));
            MarshalEncode(enc, t.Text);
        },
        static (target, enc) =>
        {
            var t = (EmitOutputFile)target;
            if (t.SourceFileName is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("sourceFileName"));
            MarshalEncode(enc, t.SourceFileName);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type EmitOutputResponse struct</c>（proto.go:1764）。</summary>
public sealed class EmitOutputResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>EmitSkipped bool `json:"emitSkipped"`</c>（proto.go:1765）。</summary>
    public bool EmitSkipped;

    /// <summary>Go: <c>Diagnostics []*DiagnosticResponse `json:"diagnostics" nonnil:"true"`</c>（proto.go:1766）。</summary>
    public DiagnosticResponse[] Diagnostics = [];

    /// <summary>Go: <c>OutputFiles []*EmitOutputFile `json:"outputFiles" nonnil:"true"`</c>（proto.go:1767）。</summary>
    public EmitOutputFile[] OutputFiles = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("emitSkipped", false, false,
            static (target, dec) =>
            {
                var t = (EmitOutputResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.EmitSkipped = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("diagnostics", false, false,
            static (target, dec) =>
            {
                var t = (EmitOutputResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Diagnostics = null!; }
                else { t.Diagnostics = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
        new StructFieldSpec("outputFiles", false, false,
            static (target, dec) =>
            {
                var t = (EmitOutputResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OutputFiles = null!; }
                else { t.OutputFiles = DecodeArray<EmitOutputFile?>(dec, static d => DecodeNullable<EmitOutputFile>(d, static d => d.DecodeNew<EmitOutputFile>()))!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (EmitOutputResponse)target;
            enc.WriteToken(JsonToken.String("emitSkipped"));
            MarshalEncode(enc, t.EmitSkipped);
        },
        static (target, enc) =>
        {
            var t = (EmitOutputResponse)target;
            enc.WriteToken(JsonToken.String("diagnostics"));
            if (t.Diagnostics is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.Diagnostics)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
        static (target, enc) =>
        {
            var t = (EmitOutputResponse)target;
            enc.WriteToken(JsonToken.String("outputFiles"));
            if (t.OutputFiles is null)
            {
                enc.WriteToken(BeginArray);
                enc.WriteToken(EndArray);
            }
            else
            {
                enc.WriteToken(BeginArray);
                foreach (var item in t.OutputFiles)
                {
                    MarshalEncode(enc, item);
                }

                enc.WriteToken(EndArray);
            }
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type FormatNodeForInsertionParams struct</c>（proto.go:1771）。</summary>
public sealed class FormatNodeForInsertionParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1772）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1773）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>File DocumentIdentifier `json:"file"`</c>（proto.go:1774）：插入目标文件。</summary>
    public DocumentIdentifier File = new();

    /// <summary>Go: <c>Position uint32 `json:"position"`</c>（proto.go:1775）：目标文件里插入位置的 UTF-16 偏移。</summary>
    public uint Position;

    /// <summary>Go: <c>Data string `json:"data"`</c>（proto.go:1776）：合成节点的 base64 二进制 AST 数据。</summary>
    public string Data = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (FormatNodeForInsertionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (FormatNodeForInsertionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("file", false, false,
            static (target, dec) =>
            {
                var t = (FormatNodeForInsertionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.File.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("position", false, false,
            static (target, dec) =>
            {
                var t = (FormatNodeForInsertionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Position = dec.Decode<uint>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("data", false, false,
            static (target, dec) =>
            {
                var t = (FormatNodeForInsertionParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Data = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (FormatNodeForInsertionParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (FormatNodeForInsertionParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (FormatNodeForInsertionParams)target;
            enc.WriteToken(JsonToken.String("file"));
            MarshalEncode(enc, t.File);
        },
        static (target, enc) =>
        {
            var t = (FormatNodeForInsertionParams)target;
            enc.WriteToken(JsonToken.String("position"));
            MarshalEncode(enc, t.Position);
        },
        static (target, enc) =>
        {
            var t = (FormatNodeForInsertionParams)target;
            enc.WriteToken(JsonToken.String("data"));
            MarshalEncode(enc, t.Data);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CheckerTypeParams struct</c>（proto.go:1780）：作用于 type 的 checker 方法参数。</summary>
public sealed class CheckerTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1781）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1782）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1783）。</summary>
    public TypeID Type;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (CheckerTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (CheckerTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (CheckerTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CheckerTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (CheckerTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (CheckerTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetPropertyOfTypeParams struct</c>（proto.go:1787）：取类型的具名属性。</summary>
public sealed class GetPropertyOfTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1788）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1789）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1790）。</summary>
    public TypeID Type;

    /// <summary>Go: <c>Name string `json:"name"`</c>（proto.go:1791）。</summary>
    public string Name = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetPropertyOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetPropertyOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (GetPropertyOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (GetPropertyOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetPropertyOfTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetPropertyOfTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetPropertyOfTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
        static (target, enc) =>
        {
            var t = (GetPropertyOfTypeParams)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetIndexInfoOfTypeParams struct</c>（proto.go:1794）。</summary>
public sealed class GetIndexInfoOfTypeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1795）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1796）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Type TypeID `json:"type"`</c>（proto.go:1797）。</summary>
    public TypeID Type;

    /// <summary>Go: <c>Kind int32 `json:"kind"`</c>（proto.go:1798）。</summary>
    public int Kind;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetIndexInfoOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetIndexInfoOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (GetIndexInfoOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Type = new TypeID(dec.Decode<uint>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("kind", false, false,
            static (target, dec) =>
            {
                var t = (GetIndexInfoOfTypeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Kind = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetIndexInfoOfTypeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetIndexInfoOfTypeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetIndexInfoOfTypeParams)target;
            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
        static (target, enc) =>
        {
            var t = (GetIndexInfoOfTypeParams)target;
            enc.WriteToken(JsonToken.String("kind"));
            MarshalEncode(enc, t.Kind);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetMemberInModuleExportsParams struct</c>（proto.go:1802）。</summary>
public sealed class GetMemberInModuleExportsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1803）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1804）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Symbol SymbolID `json:"symbol"`</c>（proto.go:1805）。</summary>
    public SymbolID Symbol;

    /// <summary>Go: <c>Name string `json:"name"`</c>（proto.go:1806）。</summary>
    public string Name = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetMemberInModuleExportsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetMemberInModuleExportsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (GetMemberInModuleExportsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (GetMemberInModuleExportsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetMemberInModuleExportsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetMemberInModuleExportsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetMemberInModuleExportsParams)target;
            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
        static (target, enc) =>
        {
            var t = (GetMemberInModuleExportsParams)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CheckerNodeParams struct</c>（proto.go:1810）：作用于节点位置的 checker 方法参数。</summary>
public sealed class CheckerNodeParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1811）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1812）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Location NodeHandle `json:"location"`</c>（proto.go:1813）。</summary>
    public NodeHandle Location;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (CheckerNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (CheckerNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("location", false, false,
            static (target, dec) =>
            {
                var t = (CheckerNodeParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Location = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CheckerNodeParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (CheckerNodeParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (CheckerNodeParams)target;
            enc.WriteToken(JsonToken.String("location"));
            MarshalEncode(enc, t.Location);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CheckerSymbolParams struct</c>（proto.go:1817）：作用于符号的 checker 方法参数。</summary>
public sealed class CheckerSymbolParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1818）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1819）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Symbol SymbolID `json:"symbol"`</c>（proto.go:1820）。</summary>
    public SymbolID Symbol;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (CheckerSymbolParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (CheckerSymbolParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("symbol", false, false,
            static (target, dec) =>
            {
                var t = (CheckerSymbolParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Symbol = new SymbolID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CheckerSymbolParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (CheckerSymbolParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (CheckerSymbolParams)target;
            enc.WriteToken(JsonToken.String("symbol"));
            MarshalEncode(enc, t.Symbol);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type JSDocTagInfo struct</c>（proto.go:1825）：单个 JSDoc 标签，镜像 Strada 的 JSDocTagInfo，
/// 但标签文本是普通字符串而不是 SymbolDisplayPart[]。</summary>
public sealed class JSDocTagInfo : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Name string `json:"name"`</c>（proto.go:1826）。</summary>
    public string Name = "";

    /// <summary>Go: <c>Text string `json:"text,omitempty"`</c>（proto.go:1827）。</summary>
    public string Text = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("name", false, false,
            static (target, dec) =>
            {
                var t = (JSDocTagInfo)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Name = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("text", false, false,
            static (target, dec) =>
            {
                var t = (JSDocTagInfo)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Text = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (JSDocTagInfo)target;
            enc.WriteToken(JsonToken.String("name"));
            MarshalEncode(enc, t.Name);
        },
        static (target, enc) =>
        {
            var t = (JSDocTagInfo)target;
            if (t.Text.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("text"));
            MarshalEncode(enc, t.Text);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CheckerSignatureParams struct</c>（proto.go:1831）：作用于签名的 checker 方法参数。</summary>
public sealed class CheckerSignatureParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1832）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1833）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Signature SignatureID `json:"signature"`</c>（proto.go:1834）。</summary>
    public SignatureID Signature;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (CheckerSignatureParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (CheckerSignatureParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("signature", false, false,
            static (target, dec) =>
            {
                var t = (CheckerSignatureParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Signature = new SignatureID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CheckerSignatureParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (CheckerSignatureParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (CheckerSignatureParams)target;
            enc.WriteToken(JsonToken.String("signature"));
            MarshalEncode(enc, t.Signature);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type TypePredicateResponse struct</c>（proto.go:1838）：getTypePredicateOfSignature 的响应。</summary>
public sealed class TypePredicateResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Kind int32 `json:"kind"`</c>（proto.go:1839）。</summary>
    public int Kind;

    /// <summary>Go: <c>ParameterIndex int32 `json:"parameterIndex"`</c>（proto.go:1840）。</summary>
    public int ParameterIndex;

    /// <summary>Go: <c>ParameterName string `json:"parameterName,omitempty"`</c>（proto.go:1841）。</summary>
    public string ParameterName = "";

    /// <summary>Go: <c>Type *TypeResponse `json:"type,omitempty"`</c>（proto.go:1842）。</summary>
    public TypeResponse? Type;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("kind", false, false,
            static (target, dec) =>
            {
                var t = (TypePredicateResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Kind = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("parameterIndex", false, false,
            static (target, dec) =>
            {
                var t = (TypePredicateResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ParameterIndex = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("parameterName", false, false,
            static (target, dec) =>
            {
                var t = (TypePredicateResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ParameterName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("type", false, false,
            static (target, dec) =>
            {
                var t = (TypePredicateResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Type = null; }
                else { t.Type = DecodeNullable<TypeResponse>(dec, static d => d.DecodeNew<TypeResponse>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (TypePredicateResponse)target;
            enc.WriteToken(JsonToken.String("kind"));
            MarshalEncode(enc, t.Kind);
        },
        static (target, enc) =>
        {
            var t = (TypePredicateResponse)target;
            enc.WriteToken(JsonToken.String("parameterIndex"));
            MarshalEncode(enc, t.ParameterIndex);
        },
        static (target, enc) =>
        {
            var t = (TypePredicateResponse)target;
            if (t.ParameterName.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("parameterName"));
            MarshalEncode(enc, t.ParameterName);
        },
        static (target, enc) =>
        {
            var t = (TypePredicateResponse)target;
            if (t.Type is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("type"));
            MarshalEncode(enc, t.Type);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type IndexInfoResponse struct</c>（proto.go:1846）：单个索引签名。</summary>
public sealed class IndexInfoResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>KeyType TypeResponse `json:"keyType"`</c>（proto.go:1847）。</summary>
    public TypeResponse KeyType = new();

    /// <summary>Go: <c>ValueType TypeResponse `json:"valueType"`</c>（proto.go:1848）。</summary>
    public TypeResponse ValueType = new();

    /// <summary>Go: <c>IsReadonly bool `json:"isReadonly,omitempty"`</c>（proto.go:1849）。</summary>
    public bool IsReadonly;

    /// <summary>Go: <c>Declaration NodeHandle `json:"declaration,omitempty"`</c>（proto.go:1850）。</summary>
    public NodeHandle Declaration;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("keyType", false, false,
            static (target, dec) =>
            {
                var t = (IndexInfoResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.KeyType.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("valueType", false, false,
            static (target, dec) =>
            {
                var t = (IndexInfoResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ValueType.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("isReadonly", false, false,
            static (target, dec) =>
            {
                var t = (IndexInfoResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.IsReadonly = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("declaration", false, false,
            static (target, dec) =>
            {
                var t = (IndexInfoResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Declaration = new NodeHandle(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (IndexInfoResponse)target;
            enc.WriteToken(JsonToken.String("keyType"));
            MarshalEncode(enc, t.KeyType);
        },
        static (target, enc) =>
        {
            var t = (IndexInfoResponse)target;
            enc.WriteToken(JsonToken.String("valueType"));
            MarshalEncode(enc, t.ValueType);
        },
        static (target, enc) =>
        {
            var t = (IndexInfoResponse)target;
            if (!t.IsReadonly)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("isReadonly"));
            MarshalEncode(enc, t.IsReadonly);
        },
        static (target, enc) =>
        {
            var t = (IndexInfoResponse)target;
            if (t.Declaration.Value.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("declaration"));
            MarshalEncode(enc, t.Declaration);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SourceFileResponse struct</c>（proto.go:1855）：源文件的二进制编码 AST 数据（base64）。</summary>
public sealed class SourceFileResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Data string `json:"data"`</c>（proto.go:1857）：编码器格式的 base64 二进制 AST 数据。</summary>
    public string Data = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("data", false, false,
            static (target, dec) =>
            {
                var t = (SourceFileResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Data = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SourceFileResponse)target;
            enc.WriteToken(JsonToken.String("data"));
            MarshalEncode(enc, t.Data);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetDiagnosticsParams struct</c>（proto.go:1861）：按文件的诊断方法参数。</summary>
public sealed class GetDiagnosticsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1862）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1863）。</summary>
    public TypeScript.Project.ID Project;

    /// <summary>Go: <c>Files []DocumentIdentifier `json:"files,omitempty"`</c>（proto.go:1864）。</summary>
    public DocumentIdentifier[]? Files;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetDiagnosticsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetDiagnosticsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("files", false, false,
            static (target, dec) =>
            {
                var t = (GetDiagnosticsParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Files = null; }
                else { t.Files = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetDiagnosticsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetDiagnosticsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
        static (target, enc) =>
        {
            var t = (GetDiagnosticsParams)target;
            if (t.Files is null || t.Files.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("files"));
            MarshalEncode(enc, t.Files);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetProjectDiagnosticsParams struct</c>（proto.go:1868）：项目级诊断方法参数。</summary>
public sealed class GetProjectDiagnosticsParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:1869）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:1870）。</summary>
    public TypeScript.Project.ID Project;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetProjectDiagnosticsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (GetProjectDiagnosticsParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new TypeScript.Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetProjectDiagnosticsParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (GetProjectDiagnosticsParams)target;
            enc.WriteToken(JsonToken.String("project"));
            MarshalEncode(enc, t.Project.Value);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type DiagnosticResponse struct</c>（proto.go:1874）：单个诊断的 API 响应（自引用：MessageChain /
/// RelatedInformation 是 <c>[]*DiagnosticResponse</c>，见文件头第 15 条）。</summary>
public sealed class DiagnosticResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>FileName string `json:"fileName,omitempty"`</c>（proto.go:1876）：该诊断所属文件路径（若有）。</summary>
    public string FileName = "";

    /// <summary>Go: <c>Pos int `json:"pos"`</c>（proto.go:1878）：诊断在源文件里的起始位置。</summary>
    public int Pos;

    /// <summary>Go: <c>End int `json:"end"`</c>（proto.go:1880）：诊断在源文件里的结束位置。</summary>
    public int End;

    /// <summary>Go: <c>StartPosition *DiagnosticPositionResponse `json:"startPosition,omitempty"`</c>（proto.go:1882）：
    /// Pos 的零基行列位置。</summary>
    public DiagnosticPositionResponse? StartPosition;

    /// <summary>Go: <c>EndPosition *DiagnosticPositionResponse `json:"endPosition,omitempty"`</c>（proto.go:1884）：
    /// End 的零基行列位置。</summary>
    public DiagnosticPositionResponse? EndPosition;

    /// <summary>Go: <c>SourceLines []*DiagnosticSourceLineResponse `json:"sourceLines,omitempty"`</c>（proto.go:1886）：
    /// 渲染该诊断（含上下文）所需的源行。</summary>
    public DiagnosticSourceLineResponse[]? SourceLines;

    /// <summary>Go: <c>Code int32 `json:"code"`</c>（proto.go:1888）：诊断错误码。</summary>
    public int Code;

    /// <summary>Go: <c>Category diagnostics.Category `json:"category"`</c>（proto.go:1890）：诊断类别（error/warning/
    /// suggestion/message）。</summary>
    public Diagnostics.Category Category;

    /// <summary>Go: <c>Source string `json:"source,omitempty"`</c>（proto.go:1892）：自定义诊断码前缀，空值即默认 "TS"。</summary>
    public string Source = "";

    /// <summary>Go: <c>Text string `json:"text"`</c>（proto.go:1894）：本地化后的诊断文本。</summary>
    public string Text = "";

    /// <summary>Go: <c>ReportsUnnecessary bool `json:"reportsUnnecessary,omitzero"`</c>（proto.go:1896）：该诊断标记多余代码。</summary>
    public bool ReportsUnnecessary;

    /// <summary>Go: <c>ReportsDeprecated bool `json:"reportsDeprecated,omitzero"`</c>（proto.go:1898）：该诊断标记已废弃代码。</summary>
    public bool ReportsDeprecated;

    /// <summary>Go: <c>MessageChain []*DiagnosticResponse `json:"messageChain,omitempty"`</c>（proto.go:1900）：链式诊断消息。</summary>
    public DiagnosticResponse[]? MessageChain;

    /// <summary>Go: <c>RelatedInformation []*DiagnosticResponse `json:"relatedInformation,omitempty"`</c>（proto.go:1902）：
    /// 相关信息诊断。</summary>
    public DiagnosticResponse[]? RelatedInformation;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("fileName", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.FileName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("pos", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Pos = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("end", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.End = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("startPosition", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.StartPosition = null; }
                else { t.StartPosition = DecodeNullable<DiagnosticPositionResponse>(dec, static d => d.DecodeNew<DiagnosticPositionResponse>()); }
            }
        ),
        new StructFieldSpec("endPosition", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.EndPosition = null; }
                else { t.EndPosition = DecodeNullable<DiagnosticPositionResponse>(dec, static d => d.DecodeNew<DiagnosticPositionResponse>()); }
            }
        ),
        new StructFieldSpec("sourceLines", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.SourceLines = null; }
                else { t.SourceLines = DecodeArray<DiagnosticSourceLineResponse>(dec, static d => d.DecodeNew<DiagnosticSourceLineResponse>()!)!; }
            }
        ),
        new StructFieldSpec("code", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Code = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("category", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Category = dec.Decode<Diagnostics.Category>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("source", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Source = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("text", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Text = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("reportsUnnecessary", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ReportsUnnecessary = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("reportsDeprecated", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ReportsDeprecated = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("messageChain", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.MessageChain = null; }
                else { t.MessageChain = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
        new StructFieldSpec("relatedInformation", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RelatedInformation = null; }
                else { t.RelatedInformation = DecodeArray<DiagnosticResponse>(dec, static d => d.DecodeNew<DiagnosticResponse>()!)!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (t.FileName.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("fileName"));
            MarshalEncode(enc, t.FileName);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            enc.WriteToken(JsonToken.String("pos"));
            MarshalEncode(enc, t.Pos);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            enc.WriteToken(JsonToken.String("end"));
            MarshalEncode(enc, t.End);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (t.StartPosition is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("startPosition"));
            MarshalEncode(enc, t.StartPosition);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (t.EndPosition is null)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("endPosition"));
            MarshalEncode(enc, t.EndPosition);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (t.SourceLines is null || t.SourceLines.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("sourceLines"));
            MarshalEncode(enc, t.SourceLines);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            enc.WriteToken(JsonToken.String("code"));
            MarshalEncode(enc, t.Code);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            enc.WriteToken(JsonToken.String("category"));
            MarshalEncode(enc, t.Category);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (t.Source.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("source"));
            MarshalEncode(enc, t.Source);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            enc.WriteToken(JsonToken.String("text"));
            MarshalEncode(enc, t.Text);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (!t.ReportsUnnecessary)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("reportsUnnecessary"));
            MarshalEncode(enc, t.ReportsUnnecessary);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (!t.ReportsDeprecated)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("reportsDeprecated"));
            MarshalEncode(enc, t.ReportsDeprecated);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (t.MessageChain is null || t.MessageChain.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("messageChain"));
            MarshalEncode(enc, t.MessageChain);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticResponse)target;
            if (t.RelatedInformation is null || t.RelatedInformation.Length == 0)
            {
                return;
            }

            enc.WriteToken(JsonToken.String("relatedInformation"));
            MarshalEncode(enc, t.RelatedInformation);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type DiagnosticPositionResponse struct</c>（proto.go:1905）。
/// `Character` 是 Go 的 `core.UTF16Offset`（int 新类型）；C# 的 `Core.UTF16Offset` 没有 IJsonMarshaler，
/// 故按数字语义读写 `.Value`（文件头第 9 条）。</summary>
public sealed class DiagnosticPositionResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Line int `json:"line"`</c>（proto.go:1906）：零基行号。</summary>
    public int Line;

    /// <summary>Go: <c>Character core.UTF16Offset `json:"character"`</c>（proto.go:1907）：UTF-16 代码单元列号。</summary>
    public Core.UTF16Offset Character;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("line", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticPositionResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Line = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("character", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticPositionResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Character = new Core.UTF16Offset(dec.Decode<int>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (DiagnosticPositionResponse)target;
            enc.WriteToken(JsonToken.String("line"));
            MarshalEncode(enc, t.Line);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticPositionResponse)target;
            enc.WriteToken(JsonToken.String("character"));
            MarshalEncode(enc, t.Character.Value);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type DiagnosticSourceLineResponse struct</c>（proto.go:1910）：渲染诊断用的一行源文本。</summary>
public sealed class DiagnosticSourceLineResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Line int `json:"line"`</c>（proto.go:1911）。</summary>
    public int Line;

    /// <summary>Go: <c>Text string `json:"text"`</c>（proto.go:1912）。</summary>
    public string Text = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("line", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticSourceLineResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Line = dec.Decode<int>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("text", false, false,
            static (target, dec) =>
            {
                var t = (DiagnosticSourceLineResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Text = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (DiagnosticSourceLineResponse)target;
            enc.WriteToken(JsonToken.String("line"));
            MarshalEncode(enc, t.Line);
        },
        static (target, enc) =>
        {
            var t = (DiagnosticSourceLineResponse)target;
            enc.WriteToken(JsonToken.String("text"));
            MarshalEncode(enc, t.Text);
        },
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}
