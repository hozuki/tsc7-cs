// ported from: internal/api/proto.go:265-568
//
// 本文件搬运 proto.go 这一段里的全部消息类型（`type X struct` 两个命名类型 ModuleResolutionFallback/ResolutionMode）。
// 同区间的函数与方法归别处：`DocumentIdentifier.ToFileName/ToURI/ToAbsoluteFileName/String`、
// `DocumentIdentifier.UnmarshalJSONFrom`、`EnsurePrograms.UnmarshalJSONFrom` 与 `unmarshalers` 表在 Api/Proto.cs（集成者持有）；
// `ProjectResponse`、`DiagnosticResponse`、`PackageId`、`ResolvedModule` 在 Api/Proto.Types2.cs（proto.go:746-1920）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) **嵌入字段 → 基类继承**：Go 的 `CreateSnapshotParams`/`LanguageServerSnapshotChanges` 内嵌
//    `SnapshotRequestChangesParams`（字段提升 + JSON 平铺）。C# 落成基类继承（`: SnapshotRequestChangesParams`），
//    与 Go 的字段提升语义一致（先例：Checker/Types.Compound.cs 文件头、AstNav/Tokens.Helpers.cs 文件头都把 Go 的嵌入
//    落成 C# 基类继承）。为什么**不**展开成各自字段：session.go 有三处写 `&params.SnapshotRequestChangesParams`
//    （`toAPISnapshotRequest(ctx, &params.SnapshotRequestChangesParams)` 等），继承后 `SnapshotRequestChangesParams req = params;`
//    就是同一个实例（= Go 的取址）；字段展开只能逐字段拷贝，破坏引用语义。两个派生类的 StructSpec 仍按 Go 的字段顺序
//    （内嵌字段在前）显式列全，所以 wire 形状与 Go 一致（平铺）。基类的 UnmarshalJSONFrom/MarshalJSONTo 是 virtual，
//    派生类 override 换成自己的 Spec；因此经**基类引用**调用时会写出派生类的完整字段集，而 Go 里按
//    `*SnapshotRequestChangesParams` 序列化只写基类字段 —— 该差异只影响这种人为场景，且写全不丢数据。
// 2) **JSON 编解码**：Go 靠 reflect 读 struct tag；C# AOT 禁反射（契约 §1.1），所以每个类都实现 IJsonMarshaler/
//    IJsonUnmarshaler，并用 LsProto 的显式字段表 StructSpec/StructFieldSpec（契约 §3；实物先例 Lsp.Generated.Structures.cs）。
//    字段声明顺序 = Go 字段顺序 = 编码顺序；JSON 名 = Go tag 名（去掉选项部分）。
// 3) **required 一律 false**：API 的 json tag 没有 `lsp:"required"`（契约 §3），缺字段时 C# 保持 CLR 默认值 = Go 零值。
// 4) **rejectNull**：Go 里该字段是**指针/切片/map 且没有 omitempty/omitzero** 时写 true（共享解码器
//    LsProto.StructCodec.UnmarshalStruct 会拒绝显式 null，与 Go json/v2 对这些字段的严格性一致）；其余
//    （带 omitempty/omitzero 的指针/切片/map，以及全部值类型）写 false，解码时显式 null 按 Go 的 nil/零值处理。
//    注：兄弟切片 Api/Proto.Types2.cs 对**带 omitempty 的**指针/切片也写 true（其文件头第 3 条），与本文件规则不同；
//    差异只出现在「显式 null」这一种输入上，已登记在 yield 的不确定点里，需要统一时由集成者裁决。
// 5) **omitempty/omitzero**：omitempty 在 `""`、`0`、`false`、null、空切片（len==0）、空 map 时跳过；omitzero 在 Go 零值时
//    跳过（本文件的 `*[]T` 字段：nil 指针即零值 → 跳过）。`nonnil:"true"` 在本仓是 no-op（**总是写出**，契约 §3）。
// 6) **非 omit 的切片落成非空数组 `T[]`**（`= []` 初始化 + 解码遇显式 null 走 `null!`；编码空集合写 `[]`），与
//    Api/Proto.Types2.cs 的既有做法一致（那里文件头第 4 条记了同一约定）。Go 对 nil 切片写 `null`，本仓按既定 wire
//    约定写 `[]`；差异只在 Go 侧真的构造出 nil 切片时出现。
// 7) **`*[]T`**（Go 用指针区分「字段不存在」与「空数组」）→ `T[]?`：null = 字段不存在（omitzero 时省略不写）。
// 8) **`ModuleResolutionFallback`**（Go: `type ModuleResolutionFallback string`，proto.go:467）→ 常量字符串静态类
//    （`ModuleResolutionFallback.Resolve`/`.Unresolved`），字段类型是 `string`：C# 没有「字符串枚举」，与 Api.cs 对
//    `Method` 的处理同构。Go 的 `ModuleResolutionFallbackResolve` → C# `ModuleResolutionFallback.Resolve`。
// 9) **`ResolutionMode`**（Go: `type ResolutionMode core.ModuleKind`，proto.go:468）→ 文件级别名
//    `using ResolutionMode = TypeScript.Core.ModuleKind;`（C# enum 直接按底层整数编解码，不需要包装类型；先例
//    Core/CompilerOptions.cs 文件头第 6 条把 `core.ResolutionMode` 定为 `ModuleKind` 别名）。
// 10) **`project.ID`/`project.SyntheticProjectID`/`tspath.Path` 没有 IJsonMarshaler/IJsonUnmarshaler**（Project/TSPath
//    分片里它们是纯值类型，见 Project/Project.cs、TSPath/Path.cs）→ 本文件对这些类型显式读写
//    （`JsonToken.String(x.Value)` / `new X(dec.Decode<string>() ?? "")`），不走 `MarshalEncode`（否则运行期抛
//    `json: type ... must implement IJsonMarshaler`）。`SyntheticProjectID` 解码走 `ParseSyntheticProjectID`，
//    与 Go 的 `(*SyntheticProjectID).UnmarshalJSONFrom`（project.go:192）等价（非法值 → 抛 JsonException）。
// 11) **`Core.CompilerOptions` 目前没有 JSON 映射**（Core/CompilerOptions.cs 文件头第 3-5 行注明「本轮不实现映射」）→
//    3 个 `compilerOptions` 字段照 Api/Proto.Types2.cs 的同款写法（`MarshalEncode` / `DecodeNew`）交给 Core 分片补上后生效，
//    `Core.ProjectReference`（`CreateProgramOptions.ProjectReferences`）同样没有映射（Core/ProjectReference.cs 文件头第 3 条），
//    这是本仓已知外部缺口，不是本文件的职责（已在 yield 登记）。
// 12) **不声明 `DocumentIdentifier`/`EnsurePrograms`**（父任务裁定它们与各自手写的 UnmarshalJSONFrom 一起写在 Api/Proto.cs），
//    本文件只引用。
// 13) 数字/字符串/布尔的写出统一 `enc.WriteToken(JsonToken.Number/String/Bool(...))`（数字一律
//    `CultureInfo.InvariantCulture`）；实现了 IJsonMarshaler 的标量（ID 类型等）直接 `MarshalEncode`。
using System.Globalization;
using System.Text;
using TypeScript.Json;
using TypeScript.LsProto;
using static TypeScript.Json.Json;
using static TypeScript.LsProto.LsProto;
// System.Text.Encoder/Decoder（隐式 using）与 TypeScript.Json.Encoder/Decoder 同名 → CS0104，必须别名。
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;
// Go 的 `tspath.Path`；ImplicitUsings 会把 System.IO.Path 带进来，裸写 Path 会 CS0104。
using Path = TypeScript.TSPath.Path;
// Go 的 `type ResolutionMode core.ModuleKind`（见文件头第 9 条）。
using ResolutionMode = TypeScript.Core.ModuleKind;

namespace TypeScript.Api;

/// <summary>Go: <c>type InitializeResponse struct</c>（proto.go:265）。
/// InitializeResponse is returned by the initialize method.
/// </summary>
public sealed class InitializeResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>UseCaseSensitiveFileNames bool `json:"useCaseSensitiveFileNames"`</c>（proto.go:267）。</summary>
    public bool UseCaseSensitiveFileNames;

    /// <summary>Go: <c>CurrentDirectory string `json:"currentDirectory"`</c>（proto.go:269）。</summary>
    public string CurrentDirectory = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("useCaseSensitiveFileNames", false, false,
            static (target, dec) =>
            {
                var t = (InitializeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.UseCaseSensitiveFileNames = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("currentDirectory", false, false,
            static (target, dec) =>
            {
                var t = (InitializeResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CurrentDirectory = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (InitializeResponse)target;
            enc.WriteToken(JsonToken.String("useCaseSensitiveFileNames"));
            enc.WriteToken(JsonToken.Bool(t.UseCaseSensitiveFileNames));
        },
        static (target, enc) =>
        {
            var t = (InitializeResponse)target;
            enc.WriteToken(JsonToken.String("currentDirectory"));
            enc.WriteToken(JsonToken.String(t.CurrentDirectory));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ModuleResolutionFallback string</c>（proto.go:467）。
/// C# 没有「字符串枚举」（enum 无法承载任意字符串），所以按 Api.cs 对 `Method` 的既定做法落成常量字符串静态类；
/// 使用点是 `ModuleResolutionSpec.Fallback`（类型 `string`）。见文件头第 8 条。
/// </summary>
public static class ModuleResolutionFallback
{
    /// <summary>Go: <c>ModuleResolutionFallbackResolve ModuleResolutionFallback = "resolve"</c>（proto.go:472）。</summary>
    public const string Resolve = "resolve";

    /// <summary>Go: <c>ModuleResolutionFallbackUnresolved ModuleResolutionFallback = "unresolved"</c>（proto.go:473）。</summary>
    public const string Unresolved = "unresolved";
}

/// <summary>Go: <c>type FileNotifications struct</c>（proto.go:362）。
/// FileNotifications describes changes to files that have occurred on the host file system, used to notify the session
/// to reload cached files and reevaluate tsconfig.json `include` globs. Either InvalidateAll is true (discard all caches)
/// or Changed/Created/Deleted list individual documents.
/// </summary>
public sealed class FileNotifications : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>InvalidateAll bool `json:"invalidateAll,omitempty"`</c>（proto.go:363）。</summary>
    public bool InvalidateAll;

    /// <summary>Go: <c>Changed []DocumentIdentifier `json:"changed,omitempty"`</c>（proto.go:364）。</summary>
    public DocumentIdentifier[]? Changed;

    /// <summary>Go: <c>Created []DocumentIdentifier `json:"created,omitempty"`</c>（proto.go:365）。</summary>
    public DocumentIdentifier[]? Created;

    /// <summary>Go: <c>Deleted []DocumentIdentifier `json:"deleted,omitempty"`</c>（proto.go:366）。</summary>
    public DocumentIdentifier[]? Deleted;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("invalidateAll", false, false,
            static (target, dec) =>
            {
                var t = (FileNotifications)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InvalidateAll = dec.Decode<bool>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("changed", false, false,
            static (target, dec) =>
            {
                var t = (FileNotifications)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Changed = null; }
                else { t.Changed = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("created", false, false,
            static (target, dec) =>
            {
                var t = (FileNotifications)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Created = null; }
                else { t.Created = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("deleted", false, false,
            static (target, dec) =>
            {
                var t = (FileNotifications)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Deleted = null; }
                else { t.Deleted = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (FileNotifications)target;
            if (!t.InvalidateAll) { return; }
            enc.WriteToken(JsonToken.String("invalidateAll"));
            enc.WriteToken(JsonToken.Bool(t.InvalidateAll));
        },
        static (target, enc) =>
        {
            var t = (FileNotifications)target;
            if (t.Changed is null || t.Changed.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("changed"));
            MarshalEncode(enc, t.Changed);
        },
        static (target, enc) =>
        {
            var t = (FileNotifications)target;
            if (t.Created is null || t.Created.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("created"));
            MarshalEncode(enc, t.Created);
        },
        static (target, enc) =>
        {
            var t = (FileNotifications)target;
            if (t.Deleted is null || t.Deleted.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("deleted"));
            MarshalEncode(enc, t.Deleted);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SnapshotRequestChangesParams struct</c>（proto.go:371）。
/// SnapshotRequestChangesParams describes project, file, and program changes to apply while creating or updating a snapshot.
/// 见文件头第 1 条：`CreateSnapshotParams`/`LanguageServerSnapshotChanges` 在 C# 里继承本类（Go 是内嵌）。
/// </summary>
public class SnapshotRequestChangesParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>OpenProjects []DocumentIdentifier `json:"openProjects,omitempty"`</c>（proto.go:373）。</summary>
    public DocumentIdentifier[]? OpenProjects;

    /// <summary>Go: <c>CloseProjects []DocumentIdentifier `json:"closeProjects,omitempty"`</c>（proto.go:376）。</summary>
    public DocumentIdentifier[]? CloseProjects;

    /// <summary>Go: <c>OpenFiles []DocumentIdentifier `json:"openFiles,omitempty"`</c>（proto.go:383）。</summary>
    public DocumentIdentifier[]? OpenFiles;

    /// <summary>Go: <c>CloseFiles []DocumentIdentifier `json:"closeFiles,omitempty"`</c>（proto.go:386）。</summary>
    public DocumentIdentifier[]? CloseFiles;

    /// <summary>Go: <c>CreatePrograms []*CreateSnapshotProgramParams `json:"createPrograms,omitempty"`</c>（proto.go:388）。</summary>
    public CreateSnapshotProgramParams?[]? CreatePrograms;

    /// <summary>Go: <c>ReconfigurePrograms []*ReconfigureSnapshotProgramParams `json:"reconfigurePrograms,omitempty"`</c>（proto.go:390）。</summary>
    public ReconfigureSnapshotProgramParams?[]? ReconfigurePrograms;

    /// <summary>Go: <c>RemovePrograms []project.SyntheticProjectID `json:"removePrograms,omitempty"`</c>（proto.go:392）。</summary>
    public Project.SyntheticProjectID[]? RemovePrograms;

    /// <summary>Go: <c>EnsurePrograms *EnsurePrograms `json:"ensurePrograms,omitempty"`</c>（proto.go:395）。</summary>
    public EnsurePrograms? EnsurePrograms;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("openProjects", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OpenProjects = null; }
                else { t.OpenProjects = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("closeProjects", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CloseProjects = null; }
                else { t.CloseProjects = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("openFiles", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OpenFiles = null; }
                else { t.OpenFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("closeFiles", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CloseFiles = null; }
                else { t.CloseFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("createPrograms", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CreatePrograms = null; }
                else { t.CreatePrograms = DecodeArray<CreateSnapshotProgramParams?>(dec, static d => DecodeNullable<CreateSnapshotProgramParams>(d, static d => d.DecodeNew<CreateSnapshotProgramParams>())); }
            }
        ),
        new StructFieldSpec("reconfigurePrograms", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ReconfigurePrograms = null; }
                else { t.ReconfigurePrograms = DecodeArray<ReconfigureSnapshotProgramParams?>(dec, static d => DecodeNullable<ReconfigureSnapshotProgramParams>(d, static d => d.DecodeNew<ReconfigureSnapshotProgramParams>())); }
            }
        ),
        new StructFieldSpec("removePrograms", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RemovePrograms = null; }
                else { t.RemovePrograms = DecodeArray<Project.SyntheticProjectID>(dec, static d => ProtoTypesCodec.DecodeSyntheticProjectID(d)); }
            }
        ),
        new StructFieldSpec("ensurePrograms", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotRequestChangesParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.EnsurePrograms = null; }
                else { t.EnsurePrograms = dec.DecodeNew<EnsurePrograms>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.OpenProjects is null || t.OpenProjects.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("openProjects"));
            MarshalEncode(enc, t.OpenProjects);
        },
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.CloseProjects is null || t.CloseProjects.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("closeProjects"));
            MarshalEncode(enc, t.CloseProjects);
        },
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.OpenFiles is null || t.OpenFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("openFiles"));
            MarshalEncode(enc, t.OpenFiles);
        },
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.CloseFiles is null || t.CloseFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("closeFiles"));
            MarshalEncode(enc, t.CloseFiles);
        },
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.CreatePrograms is null || t.CreatePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("createPrograms"));
            MarshalEncode(enc, t.CreatePrograms);
        },
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.ReconfigurePrograms is null || t.ReconfigurePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("reconfigurePrograms"));
            MarshalEncode(enc, t.ReconfigurePrograms);
        },
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.RemovePrograms is null || t.RemovePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("removePrograms"));
            ProtoTypesCodec.WriteSyntheticProjectIDArray(enc, t.RemovePrograms);
        },
        static (target, enc) =>
        {
            var t = (SnapshotRequestChangesParams)target;
            if (t.EnsurePrograms is null) { return; }
            enc.WriteToken(JsonToken.String("ensurePrograms"));
            MarshalEncode(enc, t.EnsurePrograms);
        }
    ]);

    public virtual void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public virtual void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateSnapshotParams struct</c>（proto.go:421）。
/// CreateSnapshotParams are the parameters for creating a new independent snapshot.
/// Go 内嵌 `SnapshotRequestChangesParams`（proto.go:422）→ C# 基类继承（文件头第 1 条）。
/// </summary>
public sealed class CreateSnapshotParams : SnapshotRequestChangesParams
{
    /// <summary>Go: <c>FileNotifications *FileNotifications `json:"fileNotifications,omitempty"`</c>（proto.go:424）。</summary>
    public FileNotifications? FileNotifications;

    /// <summary>Go: <c>FileSystem *requestfilesystem.RequestFileSystem `json:"fileSystem,omitempty"`</c>（proto.go:428）。</summary>
    public RequestFileSystem.RequestFileSystem? FileSystem;

    // 注意：内嵌字段（OpenProjects…EnsurePrograms）在 StructSpec 里按 Go 的字段顺序列在本类自己的两个字段**之前**。
    // `new`：Go 的内嵌是字段提升，C# 落成基类继承后派生类的同名字段表会隐藏基类的（本类自己的 Spec 才是完整的
    // 字段顺序，UnmarshalJSONFrom/MarshalJSONTo 都是 override 后引用本类的这个成员）。
    internal new static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("openProjects", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OpenProjects = null; }
                else { t.OpenProjects = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("closeProjects", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CloseProjects = null; }
                else { t.CloseProjects = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("openFiles", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OpenFiles = null; }
                else { t.OpenFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("closeFiles", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CloseFiles = null; }
                else { t.CloseFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("createPrograms", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CreatePrograms = null; }
                else { t.CreatePrograms = DecodeArray<CreateSnapshotProgramParams?>(dec, static d => DecodeNullable<CreateSnapshotProgramParams>(d, static d => d.DecodeNew<CreateSnapshotProgramParams>())); }
            }
        ),
        new StructFieldSpec("reconfigurePrograms", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ReconfigurePrograms = null; }
                else { t.ReconfigurePrograms = DecodeArray<ReconfigureSnapshotProgramParams?>(dec, static d => DecodeNullable<ReconfigureSnapshotProgramParams>(d, static d => d.DecodeNew<ReconfigureSnapshotProgramParams>())); }
            }
        ),
        new StructFieldSpec("removePrograms", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RemovePrograms = null; }
                else { t.RemovePrograms = DecodeArray<Project.SyntheticProjectID>(dec, static d => ProtoTypesCodec.DecodeSyntheticProjectID(d)); }
            }
        ),
        new StructFieldSpec("ensurePrograms", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.EnsurePrograms = null; }
                else { t.EnsurePrograms = dec.DecodeNew<EnsurePrograms>(); }
            }
        ),
        new StructFieldSpec("fileNotifications", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.FileNotifications = null; }
                else { t.FileNotifications = dec.DecodeNew<FileNotifications>(); }
            }
        ),
        new StructFieldSpec("fileSystem", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.FileSystem = null; }
                else { t.FileSystem = dec.DecodeNew<RequestFileSystem.RequestFileSystem>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.OpenProjects is null || t.OpenProjects.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("openProjects"));
            MarshalEncode(enc, t.OpenProjects);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.CloseProjects is null || t.CloseProjects.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("closeProjects"));
            MarshalEncode(enc, t.CloseProjects);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.OpenFiles is null || t.OpenFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("openFiles"));
            MarshalEncode(enc, t.OpenFiles);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.CloseFiles is null || t.CloseFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("closeFiles"));
            MarshalEncode(enc, t.CloseFiles);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.CreatePrograms is null || t.CreatePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("createPrograms"));
            MarshalEncode(enc, t.CreatePrograms);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.ReconfigurePrograms is null || t.ReconfigurePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("reconfigurePrograms"));
            MarshalEncode(enc, t.ReconfigurePrograms);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.RemovePrograms is null || t.RemovePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("removePrograms"));
            ProtoTypesCodec.WriteSyntheticProjectIDArray(enc, t.RemovePrograms);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.EnsurePrograms is null) { return; }
            enc.WriteToken(JsonToken.String("ensurePrograms"));
            MarshalEncode(enc, t.EnsurePrograms);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.FileNotifications is null) { return; }
            enc.WriteToken(JsonToken.String("fileNotifications"));
            MarshalEncode(enc, t.FileNotifications);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotParams)target;
            if (t.FileSystem is null) { return; }
            enc.WriteToken(JsonToken.String("fileSystem"));
            MarshalEncode(enc, t.FileSystem);
        }
    ]);

    public override void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public override void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateSnapshotProgramParams struct</c>（proto.go:431）。</summary>
public sealed class CreateSnapshotProgramParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>RootFiles []DocumentIdentifier `json:"rootFiles"`</c>（proto.go:432）。无 omit → 总是写出。</summary>
    public DocumentIdentifier[] RootFiles = [];

    /// <summary>Go: <c>CompilerOptions core.CompilerOptions `json:"compilerOptions"`</c>（proto.go:433）。
    /// 值类型字段：Go 把 JSON 解进**已有**实例（未出现的字段保持原值），C# 用 `DecodeNew` 新实例（零值 + 出现的字段），
    /// 与 Go 的零值初始状态一致。</summary>
    public Core.CompilerOptions CompilerOptions = new();

    /// <summary>Go: <c>Options *CreateProgramOptions `json:"options,omitempty"`</c>（proto.go:434）。</summary>
    public CreateProgramOptions? Options;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("rootFiles", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotProgramParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RootFiles = null!; }
                else { t.RootFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>())!; }
            }
        ),
        new StructFieldSpec("compilerOptions", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotProgramParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CompilerOptions = dec.DecodeNew<Core.CompilerOptions>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("options", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotProgramParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Options = null; }
                else { t.Options = dec.DecodeNew<CreateProgramOptions>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateSnapshotProgramParams)target;
            enc.WriteToken(JsonToken.String("rootFiles"));
            MarshalEncode(enc, t.RootFiles);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotProgramParams)target;
            enc.WriteToken(JsonToken.String("compilerOptions"));
            MarshalEncode(enc, t.CompilerOptions);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotProgramParams)target;
            if (t.Options is null) { return; }
            enc.WriteToken(JsonToken.String("options"));
            MarshalEncode(enc, t.Options);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ReconfigureSnapshotProgramParams struct</c>（proto.go:437）。</summary>
public sealed class ReconfigureSnapshotProgramParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Id project.SyntheticProjectID `json:"id"`</c>（proto.go:438）。</summary>
    public Project.SyntheticProjectID Id;

    /// <summary>Go: <c>RootFiles []DocumentIdentifier `json:"rootFiles"`</c>（proto.go:439）。无 omit → 总是写出。</summary>
    public DocumentIdentifier[] RootFiles = [];

    /// <summary>Go: <c>CompilerOptions core.CompilerOptions `json:"compilerOptions"`</c>（proto.go:440）。</summary>
    public Core.CompilerOptions CompilerOptions = new();

    /// <summary>Go: <c>Options *CreateProgramOptions `json:"options,omitempty"`</c>（proto.go:441）。</summary>
    public CreateProgramOptions? Options;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("id", false, false,
            static (target, dec) =>
            {
                var t = (ReconfigureSnapshotProgramParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Id = ProtoTypesCodec.DecodeSyntheticProjectID(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("rootFiles", false, false,
            static (target, dec) =>
            {
                var t = (ReconfigureSnapshotProgramParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RootFiles = null!; }
                else { t.RootFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>())!; }
            }
        ),
        new StructFieldSpec("compilerOptions", false, false,
            static (target, dec) =>
            {
                var t = (ReconfigureSnapshotProgramParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CompilerOptions = dec.DecodeNew<Core.CompilerOptions>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("options", false, false,
            static (target, dec) =>
            {
                var t = (ReconfigureSnapshotProgramParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Options = null; }
                else { t.Options = dec.DecodeNew<CreateProgramOptions>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ReconfigureSnapshotProgramParams)target;
            enc.WriteToken(JsonToken.String("id"));
            enc.WriteToken(JsonToken.String(t.Id.Value));
        },
        static (target, enc) =>
        {
            var t = (ReconfigureSnapshotProgramParams)target;
            enc.WriteToken(JsonToken.String("rootFiles"));
            MarshalEncode(enc, t.RootFiles);
        },
        static (target, enc) =>
        {
            var t = (ReconfigureSnapshotProgramParams)target;
            enc.WriteToken(JsonToken.String("compilerOptions"));
            MarshalEncode(enc, t.CompilerOptions);
        },
        static (target, enc) =>
        {
            var t = (ReconfigureSnapshotProgramParams)target;
            if (t.Options is null) { return; }
            enc.WriteToken(JsonToken.String("options"));
            MarshalEncode(enc, t.Options);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type UpdateSnapshotParams struct</c>（proto.go:444）。</summary>
public sealed class UpdateSnapshotParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:445）。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Changes *CreateSnapshotParams `json:"changes,omitempty"`</c>（proto.go:446）。</summary>
    public CreateSnapshotParams? Changes;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (UpdateSnapshotParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("changes", false, false,
            static (target, dec) =>
            {
                var t = (UpdateSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Changes = null; }
                else { t.Changes = dec.DecodeNew<CreateSnapshotParams>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (UpdateSnapshotParams)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (UpdateSnapshotParams)target;
            if (t.Changes is null) { return; }
            enc.WriteToken(JsonToken.String("changes"));
            MarshalEncode(enc, t.Changes);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type GetCurrentLanguageServerSnapshotParams struct</c>（proto.go:449）。</summary>
public sealed class GetCurrentLanguageServerSnapshotParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>BaseSnapshot SnapshotID `json:"baseSnapshot,omitempty"`</c>（proto.go:450）。omitempty → 0 时跳过。</summary>
    public SnapshotID BaseSnapshot;

    /// <summary>Go: <c>Changes *LanguageServerSnapshotChanges `json:"changes,omitempty"`</c>（proto.go:451）。</summary>
    public LanguageServerSnapshotChanges? Changes;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("baseSnapshot", false, false,
            static (target, dec) =>
            {
                var t = (GetCurrentLanguageServerSnapshotParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.BaseSnapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("changes", false, false,
            static (target, dec) =>
            {
                var t = (GetCurrentLanguageServerSnapshotParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Changes = null; }
                else { t.Changes = dec.DecodeNew<LanguageServerSnapshotChanges>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (GetCurrentLanguageServerSnapshotParams)target;
            if (t.BaseSnapshot.Value == 0) { return; }
            enc.WriteToken(JsonToken.String("baseSnapshot"));
            MarshalEncode(enc, t.BaseSnapshot);
        },
        static (target, enc) =>
        {
            var t = (GetCurrentLanguageServerSnapshotParams)target;
            if (t.Changes is null) { return; }
            enc.WriteToken(JsonToken.String("changes"));
            MarshalEncode(enc, t.Changes);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type LanguageServerSnapshotChanges struct</c>（proto.go:456）。
/// LanguageServerSnapshotChanges describes API-driven changes to adopt into the language server's canonical state.
/// Go 内嵌 `SnapshotRequestChangesParams`（proto.go:457）→ C# 基类继承（文件头第 1 条）。
/// </summary>
public sealed class LanguageServerSnapshotChanges : SnapshotRequestChangesParams
{
    // 本类没有自己的字段：StructSpec 就是内嵌字段（Go 的字段顺序）。`new` 的理由同 CreateSnapshotParams.Spec。
    internal new static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("openProjects", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OpenProjects = null; }
                else { t.OpenProjects = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("closeProjects", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CloseProjects = null; }
                else { t.CloseProjects = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("openFiles", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OpenFiles = null; }
                else { t.OpenFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("closeFiles", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CloseFiles = null; }
                else { t.CloseFiles = DecodeArray<DocumentIdentifier>(dec, static d => d.DecodeNew<DocumentIdentifier>()); }
            }
        ),
        new StructFieldSpec("createPrograms", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CreatePrograms = null; }
                else { t.CreatePrograms = DecodeArray<CreateSnapshotProgramParams?>(dec, static d => DecodeNullable<CreateSnapshotProgramParams>(d, static d => d.DecodeNew<CreateSnapshotProgramParams>())); }
            }
        ),
        new StructFieldSpec("reconfigurePrograms", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ReconfigurePrograms = null; }
                else { t.ReconfigurePrograms = DecodeArray<ReconfigureSnapshotProgramParams?>(dec, static d => DecodeNullable<ReconfigureSnapshotProgramParams>(d, static d => d.DecodeNew<ReconfigureSnapshotProgramParams>())); }
            }
        ),
        new StructFieldSpec("removePrograms", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RemovePrograms = null; }
                else { t.RemovePrograms = DecodeArray<Project.SyntheticProjectID>(dec, static d => ProtoTypesCodec.DecodeSyntheticProjectID(d)); }
            }
        ),
        new StructFieldSpec("ensurePrograms", false, false,
            static (target, dec) =>
            {
                var t = (LanguageServerSnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.EnsurePrograms = null; }
                else { t.EnsurePrograms = dec.DecodeNew<EnsurePrograms>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.OpenProjects is null || t.OpenProjects.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("openProjects"));
            MarshalEncode(enc, t.OpenProjects);
        },
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.CloseProjects is null || t.CloseProjects.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("closeProjects"));
            MarshalEncode(enc, t.CloseProjects);
        },
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.OpenFiles is null || t.OpenFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("openFiles"));
            MarshalEncode(enc, t.OpenFiles);
        },
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.CloseFiles is null || t.CloseFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("closeFiles"));
            MarshalEncode(enc, t.CloseFiles);
        },
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.CreatePrograms is null || t.CreatePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("createPrograms"));
            MarshalEncode(enc, t.CreatePrograms);
        },
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.ReconfigurePrograms is null || t.ReconfigurePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("reconfigurePrograms"));
            MarshalEncode(enc, t.ReconfigurePrograms);
        },
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.RemovePrograms is null || t.RemovePrograms.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("removePrograms"));
            ProtoTypesCodec.WriteSyntheticProjectIDArray(enc, t.RemovePrograms);
        },
        static (target, enc) =>
        {
            var t = (LanguageServerSnapshotChanges)target;
            if (t.EnsurePrograms is null) { return; }
            enc.WriteToken(JsonToken.String("ensurePrograms"));
            MarshalEncode(enc, t.EnsurePrograms);
        }
    ]);

    public override void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public override void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateProgramOptions struct</c>（proto.go:460）。</summary>
public sealed class CreateProgramOptions : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ProjectReferences []*core.ProjectReference `json:"projectReferences,omitempty"`</c>（proto.go:461）。</summary>
    public Core.ProjectReference?[]? ProjectReferences;

    /// <summary>Go: <c>ConfigFileParsingDiagnostics []*DiagnosticResponse `json:"configFileParsingDiagnostics,omitempty"`</c>（proto.go:462）。</summary>
    public DiagnosticResponse?[]? ConfigFileParsingDiagnostics;

    /// <summary>Go: <c>ModuleResolver ModuleResolverID `json:"moduleResolver,omitempty"`</c>（proto.go:463）。omitempty → 0 时跳过。</summary>
    public ModuleResolverID ModuleResolver;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("projectReferences", false, false,
            static (target, dec) =>
            {
                var t = (CreateProgramOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ProjectReferences = null; }
                else { t.ProjectReferences = DecodeArray<Core.ProjectReference?>(dec, static d => DecodeNullable<Core.ProjectReference>(d, static d => d.DecodeNew<Core.ProjectReference>())); }
            }
        ),
        new StructFieldSpec("configFileParsingDiagnostics", false, false,
            static (target, dec) =>
            {
                var t = (CreateProgramOptions)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ConfigFileParsingDiagnostics = null; }
                else { t.ConfigFileParsingDiagnostics = DecodeArray<DiagnosticResponse?>(dec, static d => DecodeNullable<DiagnosticResponse>(d, static d => d.DecodeNew<DiagnosticResponse>())); }
            }
        ),
        new StructFieldSpec("moduleResolver", false, false,
            static (target, dec) =>
            {
                var t = (CreateProgramOptions)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ModuleResolver = new ModuleResolverID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateProgramOptions)target;
            if (t.ProjectReferences is null || t.ProjectReferences.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("projectReferences"));
            MarshalEncode(enc, t.ProjectReferences);
        },
        static (target, enc) =>
        {
            var t = (CreateProgramOptions)target;
            if (t.ConfigFileParsingDiagnostics is null || t.ConfigFileParsingDiagnostics.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("configFileParsingDiagnostics"));
            MarshalEncode(enc, t.ConfigFileParsingDiagnostics);
        },
        static (target, enc) =>
        {
            var t = (CreateProgramOptions)target;
            if (t.ModuleResolver.Value == 0) { return; }
            enc.WriteToken(JsonToken.String("moduleResolver"));
            MarshalEncode(enc, t.ModuleResolver);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ModuleResolutionSpec struct</c>（proto.go:476）。</summary>
public sealed class ModuleResolutionSpec : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Fallback ModuleResolutionFallback `json:"fallback"`</c>（proto.go:477）。
    /// Go 的命名 string 类型 → C# `string`（文件头第 8 条）；取值见 <see cref="ModuleResolutionFallback"/>。</summary>
    public string Fallback = "";

    /// <summary>Go: <c>Entries []*ModuleResolutionEntry `json:"entries" nonnil:"true"`</c>（proto.go:478）。nonnil → 总是写出。</summary>
    public ModuleResolutionEntry?[] Entries = [];

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("fallback", false, false,
            static (target, dec) =>
            {
                var t = (ModuleResolutionSpec)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Fallback = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("entries", false, false,
            static (target, dec) =>
            {
                var t = (ModuleResolutionSpec)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Entries = null!; }
                else { t.Entries = DecodeArray<ModuleResolutionEntry?>(dec, static d => DecodeNullable<ModuleResolutionEntry>(d, static d => d.DecodeNew<ModuleResolutionEntry>()))!; }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ModuleResolutionSpec)target;
            enc.WriteToken(JsonToken.String("fallback"));
            enc.WriteToken(JsonToken.String(t.Fallback));
        },
        static (target, enc) =>
        {
            var t = (ModuleResolutionSpec)target;
            enc.WriteToken(JsonToken.String("entries"));
            MarshalEncode(enc, t.Entries);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ModuleResolutionEntry struct</c>（proto.go:481）。</summary>
public sealed class ModuleResolutionEntry : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ModuleName string `json:"moduleName"`</c>（proto.go:482）。</summary>
    public string ModuleName = "";

    /// <summary>Go: <c>ContainingDirectory *DocumentIdentifier `json:"containingDirectory,omitempty"`</c>（proto.go:483）。</summary>
    public DocumentIdentifier? ContainingDirectory;

    /// <summary>Go: <c>ResolutionMode *ResolutionMode `json:"resolutionMode,omitempty"`</c>（proto.go:484）。
    /// Go 的 `*core.ModuleKind`（文件头第 9 条）→ `ModuleKind?`；omitempty 对**指针**是「nil 才跳过」
    /// （ModuleKind 的 0 值不跳过）。</summary>
    public ResolutionMode? ResolutionMode;

    /// <summary>Go: <c>Result *StaticModuleResolution `json:"result" nonnil:"true"`</c>（proto.go:485）。nonnil → 总是写出。</summary>
    public StaticModuleResolution? Result;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("moduleName", false, false,
            static (target, dec) =>
            {
                var t = (ModuleResolutionEntry)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ModuleName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("containingDirectory", false, false,
            static (target, dec) =>
            {
                var t = (ModuleResolutionEntry)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ContainingDirectory = null; }
                else { t.ContainingDirectory = dec.DecodeNew<DocumentIdentifier>(); }
            }
        ),
        new StructFieldSpec("resolutionMode", false, false,
            static (target, dec) =>
            {
                var t = (ModuleResolutionEntry)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ResolutionMode = null; }
                else { t.ResolutionMode = (ResolutionMode)dec.Decode<int>(); }
            }
        ),
        new StructFieldSpec("result", false, false,
            static (target, dec) =>
            {
                var t = (ModuleResolutionEntry)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Result = null; }
                else { t.Result = dec.DecodeNew<StaticModuleResolution>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ModuleResolutionEntry)target;
            enc.WriteToken(JsonToken.String("moduleName"));
            enc.WriteToken(JsonToken.String(t.ModuleName));
        },
        static (target, enc) =>
        {
            var t = (ModuleResolutionEntry)target;
            if (t.ContainingDirectory is null) { return; }
            enc.WriteToken(JsonToken.String("containingDirectory"));
            MarshalEncode(enc, t.ContainingDirectory);
        },
        static (target, enc) =>
        {
            var t = (ModuleResolutionEntry)target;
            if (t.ResolutionMode is null) { return; }
            enc.WriteToken(JsonToken.String("resolutionMode"));
            MarshalEncode(enc, (int)t.ResolutionMode.Value);
        },
        static (target, enc) =>
        {
            var t = (ModuleResolutionEntry)target;
            enc.WriteToken(JsonToken.String("result"));
            MarshalEncode(enc, t.Result);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type StaticModuleResolution struct</c>（proto.go:488）。</summary>
public sealed class StaticModuleResolution : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ResolvedFileName *DocumentIdentifier `json:"resolvedFileName,omitempty"`</c>（proto.go:489）。</summary>
    public DocumentIdentifier? ResolvedFileName;

    /// <summary>Go: <c>OriginalPath *DocumentIdentifier `json:"originalPath,omitempty"`</c>（proto.go:490）。</summary>
    public DocumentIdentifier? OriginalPath;

    /// <summary>Go: <c>PackageID *PackageId `json:"packageId,omitempty"`</c>（proto.go:491）。
    /// `PackageId` 由 Api/Proto.Types2.cs（proto.go:1391）声明。</summary>
    public PackageId? PackageID;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("resolvedFileName", false, false,
            static (target, dec) =>
            {
                var t = (StaticModuleResolution)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ResolvedFileName = null; }
                else { t.ResolvedFileName = dec.DecodeNew<DocumentIdentifier>(); }
            }
        ),
        new StructFieldSpec("originalPath", false, false,
            static (target, dec) =>
            {
                var t = (StaticModuleResolution)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OriginalPath = null; }
                else { t.OriginalPath = dec.DecodeNew<DocumentIdentifier>(); }
            }
        ),
        new StructFieldSpec("packageId", false, false,
            static (target, dec) =>
            {
                var t = (StaticModuleResolution)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.PackageID = null; }
                else { t.PackageID = dec.DecodeNew<PackageId>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (StaticModuleResolution)target;
            if (t.ResolvedFileName is null) { return; }
            enc.WriteToken(JsonToken.String("resolvedFileName"));
            MarshalEncode(enc, t.ResolvedFileName);
        },
        static (target, enc) =>
        {
            var t = (StaticModuleResolution)target;
            if (t.OriginalPath is null) { return; }
            enc.WriteToken(JsonToken.String("originalPath"));
            MarshalEncode(enc, t.OriginalPath);
        },
        static (target, enc) =>
        {
            var t = (StaticModuleResolution)target;
            if (t.PackageID is null) { return; }
            enc.WriteToken(JsonToken.String("packageId"));
            MarshalEncode(enc, t.PackageID);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateModuleResolverParams struct</c>（proto.go:494）。</summary>
public sealed class CreateModuleResolverParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>CompilerOptions core.CompilerOptions `json:"compilerOptions"`</c>（proto.go:495）。
    /// 值类型字段：解码语义同 CreateSnapshotProgramParams.CompilerOptions（文件头第 11 条：Core 侧尚无 JSON 映射）。</summary>
    public Core.CompilerOptions CompilerOptions = new();

    /// <summary>Go: <c>ModuleResolutions *ModuleResolutionSpec `json:"moduleResolutions,omitempty"`</c>（proto.go:496）。</summary>
    public ModuleResolutionSpec? ModuleResolutions;

    /// <summary>Go: <c>ResolveModuleNameCallback string `json:"resolveModuleNameCallback,omitempty"`</c>（proto.go:497）。</summary>
    public string ResolveModuleNameCallback = "";

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("compilerOptions", false, false,
            static (target, dec) =>
            {
                var t = (CreateModuleResolverParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.CompilerOptions = dec.DecodeNew<Core.CompilerOptions>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("moduleResolutions", false, false,
            static (target, dec) =>
            {
                var t = (CreateModuleResolverParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ModuleResolutions = null; }
                else { t.ModuleResolutions = dec.DecodeNew<ModuleResolutionSpec>(); }
            }
        ),
        new StructFieldSpec("resolveModuleNameCallback", false, false,
            static (target, dec) =>
            {
                var t = (CreateModuleResolverParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ResolveModuleNameCallback = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateModuleResolverParams)target;
            enc.WriteToken(JsonToken.String("compilerOptions"));
            MarshalEncode(enc, t.CompilerOptions);
        },
        static (target, enc) =>
        {
            var t = (CreateModuleResolverParams)target;
            if (t.ModuleResolutions is null) { return; }
            enc.WriteToken(JsonToken.String("moduleResolutions"));
            MarshalEncode(enc, t.ModuleResolutions);
        },
        static (target, enc) =>
        {
            var t = (CreateModuleResolverParams)target;
            if (t.ResolveModuleNameCallback.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("resolveModuleNameCallback"));
            enc.WriteToken(JsonToken.String(t.ResolveModuleNameCallback));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ReleaseModuleResolverParams struct</c>（proto.go:500）。</summary>
public sealed class ReleaseModuleResolverParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Resolver ModuleResolverID `json:"resolver"`</c>（proto.go:501）。</summary>
    public ModuleResolverID Resolver;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("resolver", false, false,
            static (target, dec) =>
            {
                var t = (ReleaseModuleResolverParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Resolver = new ModuleResolverID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ReleaseModuleResolverParams)target;
            enc.WriteToken(JsonToken.String("resolver"));
            MarshalEncode(enc, t.Resolver);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolveModuleNameParams struct</c>（proto.go:504）。</summary>
public sealed class ResolveModuleNameParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot,omitempty"`</c>（proto.go:505）。omitempty → 0 时跳过。</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>InProgressSnapshot uint64 `json:"inProgressSnapshot,omitempty"`</c>（proto.go:506）。omitempty → 0 时跳过。</summary>
    public ulong InProgressSnapshot;

    /// <summary>Go: <c>Resolver ModuleResolverID `json:"resolver"`</c>（proto.go:507）。</summary>
    public ModuleResolverID Resolver;

    /// <summary>Go: <c>ModuleName string `json:"moduleName"`</c>（proto.go:508）。</summary>
    public string ModuleName = "";

    /// <summary>Go: <c>ContainingDirectory DocumentIdentifier `json:"containingDirectory"`</c>（proto.go:509）。</summary>
    public DocumentIdentifier ContainingDirectory = new();

    /// <summary>Go: <c>ResolutionMode *ResolutionMode `json:"resolutionMode,omitempty"`</c>（proto.go:510）。</summary>
    public ResolutionMode? ResolutionMode;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("inProgressSnapshot", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.InProgressSnapshot = dec.Decode<ulong>(); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolver", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Resolver = new ModuleResolverID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("moduleName", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ModuleName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("containingDirectory", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameParams)target;
                // Go 把 JSON 解进**已有**字段实例（DocumentIdentifier 的 UnmarshalJSONFrom 只写它认识的成员）。
                if (dec.PeekKind() != JsonKind.Null) { t.ContainingDirectory.UnmarshalJSONFrom(dec); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolutionMode", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ResolutionMode = null; }
                else { t.ResolutionMode = (ResolutionMode)dec.Decode<int>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolveModuleNameParams)target;
            if (t.Snapshot.Value == 0) { return; }
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameParams)target;
            if (t.InProgressSnapshot == 0) { return; }
            enc.WriteToken(JsonToken.String("inProgressSnapshot"));
            MarshalEncode(enc, t.InProgressSnapshot);
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameParams)target;
            enc.WriteToken(JsonToken.String("resolver"));
            MarshalEncode(enc, t.Resolver);
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameParams)target;
            enc.WriteToken(JsonToken.String("moduleName"));
            enc.WriteToken(JsonToken.String(t.ModuleName));
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameParams)target;
            enc.WriteToken(JsonToken.String("containingDirectory"));
            MarshalEncode(enc, t.ContainingDirectory);
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameParams)target;
            if (t.ResolutionMode is null) { return; }
            enc.WriteToken(JsonToken.String("resolutionMode"));
            MarshalEncode(enc, (int)t.ResolutionMode.Value);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolveModuleNameCallbackParams struct</c>（proto.go:513）。</summary>
public sealed class ResolveModuleNameCallbackParams : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ModuleName string `json:"moduleName"`</c>（proto.go:514）。</summary>
    public string ModuleName = "";

    /// <summary>Go: <c>ContainingDirectory string `json:"containingDirectory"`</c>（proto.go:515）。</summary>
    public string ContainingDirectory = "";

    /// <summary>Go: <c>ResolutionMode *ResolutionMode `json:"resolutionMode,omitempty"`</c>（proto.go:516）。</summary>
    public ResolutionMode? ResolutionMode;

    /// <summary>Go: <c>Snapshot *SnapshotID `json:"snapshot,omitempty"`</c>（proto.go:517）。`*SnapshotID` → `SnapshotID?`。</summary>
    public SnapshotID? Snapshot;

    /// <summary>Go: <c>InProgressSnapshot *uint64 `json:"inProgressSnapshot,omitempty"`</c>（proto.go:518）。</summary>
    public ulong? InProgressSnapshot;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("moduleName", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameCallbackParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ModuleName = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("containingDirectory", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameCallbackParams)target;
                if (dec.PeekKind() != JsonKind.Null) { t.ContainingDirectory = dec.Decode<string>() ?? ""; }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("resolutionMode", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameCallbackParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ResolutionMode = null; }
                else { t.ResolutionMode = (ResolutionMode)dec.Decode<int>(); }
            }
        ),
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameCallbackParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Snapshot = null; }
                else { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
            }
        ),
        new StructFieldSpec("inProgressSnapshot", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameCallbackParams)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.InProgressSnapshot = null; }
                else { t.InProgressSnapshot = dec.Decode<ulong>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolveModuleNameCallbackParams)target;
            enc.WriteToken(JsonToken.String("moduleName"));
            enc.WriteToken(JsonToken.String(t.ModuleName));
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameCallbackParams)target;
            enc.WriteToken(JsonToken.String("containingDirectory"));
            enc.WriteToken(JsonToken.String(t.ContainingDirectory));
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameCallbackParams)target;
            if (t.ResolutionMode is null) { return; }
            enc.WriteToken(JsonToken.String("resolutionMode"));
            MarshalEncode(enc, (int)t.ResolutionMode.Value);
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameCallbackParams)target;
            if (t.Snapshot is null) { return; }
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot.Value);
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameCallbackParams)target;
            if (t.InProgressSnapshot is null) { return; }
            enc.WriteToken(JsonToken.String("inProgressSnapshot"));
            MarshalEncode(enc, t.InProgressSnapshot.Value);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ResolveModuleNameResult struct</c>（proto.go:521）。</summary>
public sealed class ResolveModuleNameResult : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ResolvedModule *ResolvedModule `json:"resolvedModule,omitempty"`</c>（proto.go:522）。
    /// `ResolvedModule` 由 Api/Proto.Types2.cs（proto.go:1410）声明。</summary>
    public ResolvedModule? ResolvedModule;

    /// <summary>Go: <c>Trace []string `json:"trace,omitempty"`</c>（proto.go:524）。Trace is provided when
    /// compilerOptions.traceResolution is true.</summary>
    public string[]? Trace;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("resolvedModule", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameResult)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ResolvedModule = null; }
                else { t.ResolvedModule = dec.DecodeNew<ResolvedModule>(); }
            }
        ),
        new StructFieldSpec("trace", false, false,
            static (target, dec) =>
            {
                var t = (ResolveModuleNameResult)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Trace = null; }
                else { t.Trace = DecodeArray<string>(dec, static d => d.Decode<string>() ?? ""); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ResolveModuleNameResult)target;
            if (t.ResolvedModule is null) { return; }
            enc.WriteToken(JsonToken.String("resolvedModule"));
            MarshalEncode(enc, t.ResolvedModule);
        },
        static (target, enc) =>
        {
            var t = (ResolveModuleNameResult)target;
            if (t.Trace is null || t.Trace.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("trace"));
            MarshalEncode(enc, t.Trace);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type ProjectFileChanges struct</c>（proto.go:528）。
/// ProjectFileChanges describes what source files changed within a single project.
/// </summary>
public sealed class ProjectFileChanges : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ChangedFiles []tspath.Path `json:"changedFiles,omitempty"`</c>（proto.go:530）。
    /// ChangedFiles lists source file paths whose content differs.</summary>
    public Path[]? ChangedFiles;

    /// <summary>Go: <c>DeletedFiles []tspath.Path `json:"deletedFiles,omitempty"`</c>（proto.go:532）。
    /// DeletedFiles lists source file paths removed from the project's program.</summary>
    public Path[]? DeletedFiles;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("changedFiles", false, false,
            static (target, dec) =>
            {
                var t = (ProjectFileChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.ChangedFiles = null; }
                else { t.ChangedFiles = DecodeArray<Path>(dec, static d => new Path(d.Decode<string>() ?? "")); }
            }
        ),
        new StructFieldSpec("deletedFiles", false, false,
            static (target, dec) =>
            {
                var t = (ProjectFileChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.DeletedFiles = null; }
                else { t.DeletedFiles = DecodeArray<Path>(dec, static d => new Path(d.Decode<string>() ?? "")); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (ProjectFileChanges)target;
            if (t.ChangedFiles is null || t.ChangedFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("changedFiles"));
            ProtoTypesCodec.WritePathArray(enc, t.ChangedFiles);
        },
        static (target, enc) =>
        {
            var t = (ProjectFileChanges)target;
            if (t.DeletedFiles is null || t.DeletedFiles.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("deletedFiles"));
            ProtoTypesCodec.WritePathArray(enc, t.DeletedFiles);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SnapshotChanges struct</c>（proto.go:538）。
/// SnapshotChanges describes what changed between a response base and a new snapshot. Changes are reported per-project
/// so clients can track cache refs at the (snapshot, project) level.
/// </summary>
public sealed class SnapshotChanges : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>ChangedProjects map[project.ID]*ProjectFileChanges `json:"changedProjects,omitempty"`</c>（proto.go:541）。
    /// ChangedProjects maps project handles to the file changes within that project. Projects not listed here
    /// (and not in RemovedProjects) are unchanged.</summary>
    public Dictionary<Project.ID, ProjectFileChanges?>? ChangedProjects;

    /// <summary>Go: <c>RemovedProjects []project.ID `json:"removedProjects,omitempty"`</c>（proto.go:544）。
    /// RemovedProjects lists project handles that were present in the previous snapshot but absent from the new one.</summary>
    public Project.ID[]? RemovedProjects;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("changedProjects", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null)
                {
                    dec.ReadToken();
                    t.ChangedProjects = null;
                }
                else
                {
                    var map = new Dictionary<Project.ID, ProjectFileChanges?>();
                    dec.ReadToken();
                    while (dec.PeekKind() != JsonKind.EndObject)
                    {
                        var key = new Project.ID(dec.ReadName());
                        map[key] = DecodeNullable<ProjectFileChanges>(dec, static d => d.DecodeNew<ProjectFileChanges>());
                    }
                    dec.ReadToken();
                    t.ChangedProjects = map;
                }
            }
        ),
        new StructFieldSpec("removedProjects", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotChanges)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.RemovedProjects = null; }
                else { t.RemovedProjects = DecodeArray<Project.ID>(dec, static d => new Project.ID(d.Decode<string>() ?? "")); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SnapshotChanges)target;
            if (t.ChangedProjects is null || t.ChangedProjects.Count == 0) { return; }
            enc.WriteToken(JsonToken.String("changedProjects"));
            enc.WriteToken(BeginObject);
            foreach (var pair in t.ChangedProjects)
            {
                enc.WriteToken(JsonToken.String(pair.Key.Value));
                MarshalEncode(enc, pair.Value);
            }
            enc.WriteToken(EndObject);
        },
        static (target, enc) =>
        {
            var t = (SnapshotChanges)target;
            if (t.RemovedProjects is null || t.RemovedProjects.Length == 0) { return; }
            enc.WriteToken(JsonToken.String("removedProjects"));
            ProtoTypesCodec.WriteProjectIDArray(enc, t.RemovedProjects);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type CreateSnapshotResponse struct</c>（proto.go:548）。
/// CreateSnapshotResponse is returned by createSnapshot.
/// </summary>
public sealed class CreateSnapshotResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Snapshot SnapshotID `json:"snapshot"`</c>（proto.go:550）。
    /// Snapshot is the handle for the newly created snapshot.</summary>
    public SnapshotID Snapshot;

    /// <summary>Go: <c>Projects []*ProjectResponse `json:"projects" nonnil:"true"`</c>（proto.go:553）。
    /// Projects contains all projects when no response base was supplied, or only projects added or replaced relative
    /// to that base. nonnil → 总是写出；`ProjectResponse` 由 Api/Proto.Types2.cs（proto.go:971）声明。</summary>
    public ProjectResponse?[] Projects = [];

    /// <summary>Go: <c>Changes *SnapshotChanges `json:"changes,omitempty"`</c>（proto.go:555）。
    /// Changes describes source file differences from the response base.</summary>
    public SnapshotChanges? Changes;

    /// <summary>Go: <c>Operation *SnapshotOperationResponse `json:"operation" nonnil:"true"`</c>（proto.go:557）。
    /// Operation describes results correlated with the request that produced the snapshot. nonnil → 总是写出。</summary>
    public SnapshotOperationResponse? Operation;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("snapshot", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotResponse)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Snapshot = new SnapshotID(dec.Decode<ulong>()); }
                else { dec.ReadToken(); }
            }
        ),
        new StructFieldSpec("projects", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Projects = null!; }
                else { t.Projects = DecodeArray<ProjectResponse?>(dec, static d => DecodeNullable<ProjectResponse>(d, static d => d.DecodeNew<ProjectResponse>()))!; }
            }
        ),
        new StructFieldSpec("changes", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Changes = null; }
                else { t.Changes = dec.DecodeNew<SnapshotChanges>(); }
            }
        ),
        new StructFieldSpec("operation", false, false,
            static (target, dec) =>
            {
                var t = (CreateSnapshotResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.Operation = null; }
                else { t.Operation = dec.DecodeNew<SnapshotOperationResponse>(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (CreateSnapshotResponse)target;
            enc.WriteToken(JsonToken.String("snapshot"));
            MarshalEncode(enc, t.Snapshot);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotResponse)target;
            enc.WriteToken(JsonToken.String("projects"));
            MarshalEncode(enc, t.Projects);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotResponse)target;
            if (t.Changes is null) { return; }
            enc.WriteToken(JsonToken.String("changes"));
            MarshalEncode(enc, t.Changes);
        },
        static (target, enc) =>
        {
            var t = (CreateSnapshotResponse)target;
            enc.WriteToken(JsonToken.String("operation"));
            MarshalEncode(enc, t.Operation);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type SnapshotOperationResponse struct</c>（proto.go:560）。</summary>
public sealed class SnapshotOperationResponse : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>CreatedPrograms *[]project.SyntheticProjectID `json:"createdPrograms,omitzero"`</c>（proto.go:561）。
    /// Go 的 `*[]T` 用指针区分「字段不存在」与「空数组」→ C# `T[]?`（null = 不存在；omitzero → 省略不写，见文件头第 7 条）。</summary>
    public Project.SyntheticProjectID[]? CreatedPrograms;

    /// <summary>Go: <c>OpenedFiles *[]*OpenedFileOperationResult `json:"openedFiles,omitzero"`</c>（proto.go:562）。</summary>
    public OpenedFileOperationResult?[]? OpenedFiles;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("createdPrograms", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotOperationResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.CreatedPrograms = null; }
                else { t.CreatedPrograms = DecodeArray<Project.SyntheticProjectID>(dec, static d => ProtoTypesCodec.DecodeSyntheticProjectID(d)); }
            }
        ),
        new StructFieldSpec("openedFiles", false, false,
            static (target, dec) =>
            {
                var t = (SnapshotOperationResponse)target;
                if (dec.PeekKind() == JsonKind.Null) { dec.ReadToken(); t.OpenedFiles = null; }
                else { t.OpenedFiles = DecodeArray<OpenedFileOperationResult?>(dec, static d => DecodeNullable<OpenedFileOperationResult>(d, static d => d.DecodeNew<OpenedFileOperationResult>())); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (SnapshotOperationResponse)target;
            if (t.CreatedPrograms is null) { return; }
            enc.WriteToken(JsonToken.String("createdPrograms"));
            ProtoTypesCodec.WriteSyntheticProjectIDArray(enc, t.CreatedPrograms);
        },
        static (target, enc) =>
        {
            var t = (SnapshotOperationResponse)target;
            if (t.OpenedFiles is null) { return; }
            enc.WriteToken(JsonToken.String("openedFiles"));
            MarshalEncode(enc, t.OpenedFiles);
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>Go: <c>type OpenedFileOperationResult struct</c>（proto.go:565）。</summary>
public sealed class OpenedFileOperationResult : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Project project.ID `json:"project"`</c>（proto.go:566）。</summary>
    public Project.ID Project;

    internal static readonly StructSpec Spec = new(
    [
        new StructFieldSpec("project", false, false,
            static (target, dec) =>
            {
                var t = (OpenedFileOperationResult)target;
                if (dec.PeekKind() != JsonKind.Null) { t.Project = new Project.ID(dec.Decode<string>() ?? ""); }
                else { dec.ReadToken(); }
            }
        ),
    ],
    [
        static (target, enc) =>
        {
            var t = (OpenedFileOperationResult)target;
            enc.WriteToken(JsonToken.String("project"));
            enc.WriteToken(JsonToken.String(t.Project.Value));
        }
    ]);

    public void UnmarshalJSONFrom(JsonDecoder dec) => UnmarshalStruct(this, Spec, dec);

    public void MarshalJSONTo(JsonEncoder enc) => MarshalStruct(this, Spec, enc);
}

/// <summary>本文件专用的读写信笺（C# 专属）：`project.ID` / `project.SyntheticProjectID` / `tspath.Path` 在 C# 侧
/// 没有 IJsonMarshaler/IJsonUnmarshaler（Project/TSPath 分片里是纯值类型，见文件头第 10 条），所以这里显式读写它们的
/// 底层值；Go 侧这几个类型由 encoding/json 按底层 string 直接处理，等价。</summary>
internal static class ProtoTypesCodec
{
    /// <summary>Go: <c>func (id *SyntheticProjectID) UnmarshalJSONFrom(dec *json.Decoder) error</c>（project.go:192-202）：
    /// 读 JSON 字符串并校验，非法值返回错误（C# 抛 JsonException）。</summary>
    internal static Project.SyntheticProjectID DecodeSyntheticProjectID(JsonDecoder dec)
    {
        var value = dec.Decode<string>() ?? "";
        if (!Project.SyntheticProjectID.ParseSyntheticProjectID(value, out var parsed))
        {
            throw new JsonException($"invalid synthetic project ID: {value}");
        }

        return parsed;
    }

    /// <summary>Go: `[]project.SyntheticProjectID` 的写侧（命名 string 类型 → JSON 字符串数组）。</summary>
    internal static void WriteSyntheticProjectIDArray(JsonEncoder enc, Project.SyntheticProjectID[]? ids)
    {
        enc.WriteToken(BeginArray);
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                enc.WriteToken(JsonToken.String(id.Value));
            }
        }

        enc.WriteToken(EndArray);
    }

    /// <summary>Go: `[]project.ID` 的写侧（命名 string 类型 → JSON 字符串数组）。</summary>
    internal static void WriteProjectIDArray(JsonEncoder enc, Project.ID[]? ids)
    {
        enc.WriteToken(BeginArray);
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                enc.WriteToken(JsonToken.String(id.Value));
            }
        }

        enc.WriteToken(EndArray);
    }

    /// <summary>Go: `[]tspath.Path` 的写侧（命名 string 类型 → JSON 字符串数组）。</summary>
    internal static void WritePathArray(JsonEncoder enc, Path[]? paths)
    {
        enc.WriteToken(BeginArray);
        if (paths is not null)
        {
            foreach (var p in paths)
            {
                enc.WriteToken(JsonToken.String(p.Value));
            }
        }

        enc.WriteToken(EndArray);
    }
}
