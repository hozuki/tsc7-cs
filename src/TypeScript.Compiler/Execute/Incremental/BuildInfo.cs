// ported from: internal/execute/incremental/buildInfo.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3 + docs/json.md §4）：
// 1) Go 的 `encoding/json/v2` + struct tag 自动映射 → C# 侧**没有反射映射**（AOT），所以本文件的每个类型都
//    显式实现 `IJsonMarshaler`/`IJsonUnmarshaler`。字段的**书写顺序 = Go 结构体声明顺序 = Go 的输出键序**；
//    `omitzero` 逐字段照抄（见第 3 条）。
// 2) Go 的 `type BuildInfoFileId int` / `BuildInfoFileIdListId int` → 只读结构体包装（照 Core/Text.cs 的
//    `TextPos` 先例）：两个方向的 implicit 转换 + IComparable，于是 Go 的 `fileId-1`、`fileId < 1`、
//    `int(fileId)`、`slices.Sort(fileIds)`、`fmt.Sprintf("%d", id)` 都能逐字保留。
// 3) **`omitzero` = Go 的零值**：字符串 ""、bool false、整数/枚举 0、**切片/map 的 nil**（注意：Go 的
//    json/v2 判的是 `reflect.Value.IsZero()`，非 nil 的空切片**会**被写成 `[]`）。所以集合字段一律用
//    `List<T>?`：null = Go 的 nil（省略），非 null = Go 的切片（写数组，哪怕是空数组）。
// 4) Go 的 `MarshalJSON`/`UnmarshalJSON`（自定义形状）→ `MarshalJSONTo`/`UnmarshalJSONFrom`。Go 里
//    "先试 A 再试 B" 的 unmarshal 用 `PeekKind()` 分派；非法形状抛 `JsonException`（Go 返回 error，
//    调用方 `ReadBuildInfo` 吃掉错误返回 nil）。
// 5) Go 的 `b.Options.Entries()` 在 nil 接收者上返回空序列 → C# 侧显式 `if (_options is not null)`。
// 6) `iter.Seq[string]` / `iter.Seq[tspath.Path]` → `IEnumerable<string>` / `IEnumerable<Path>`。
// 7) Go 导出类型原样 PascalCase；未导出类型 `buildInfoFileInfoNoSignature`/`WithSignature` →
//    `internal` 的 `BuildInfoFileInfoNoSignature`/`BuildInfoFileInfoWithSignature`。
namespace TypeScript.Execute.Incremental;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Json;
using TypeScript.TsOptions;
using static TypeScript.Collections.Collections;
using static TypeScript.Core.Core;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>type BuildInfoFileId int</c>（buildInfo.go:15）。</summary>
public readonly struct BuildInfoFileId : IEquatable<BuildInfoFileId>, IComparable<BuildInfoFileId>
{
    public readonly int Value;

    public BuildInfoFileId(int value)
    {
        Value = value;
    }

    // Go 的 BuildInfoFileId(x) / int(id) 都是显式转换，这里两个方向都做成 implicit（同 Core.TextPos）。
    public static implicit operator int(BuildInfoFileId id) => id.Value;

    public static implicit operator BuildInfoFileId(int value) => new(value);

    public bool Equals(BuildInfoFileId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is BuildInfoFileId other && Equals(other);

    public override int GetHashCode() => Value;

    public int CompareTo(BuildInfoFileId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Go: <c>type BuildInfoFileIdListId int</c>（buildInfo.go:16）。</summary>
public readonly struct BuildInfoFileIdListId : IEquatable<BuildInfoFileIdListId>, IComparable<BuildInfoFileIdListId>
{
    public readonly int Value;

    public BuildInfoFileIdListId(int value)
    {
        Value = value;
    }

    public static implicit operator int(BuildInfoFileIdListId id) => id.Value;

    public static implicit operator BuildInfoFileIdListId(int value) => new(value);

    public bool Equals(BuildInfoFileIdListId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is BuildInfoFileIdListId other && Equals(other);

    public override int GetHashCode() => Value;

    public int CompareTo(BuildInfoFileIdListId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Go: <c>type BuildInfoRoot struct</c>（buildInfo.go:30-34）。
/// - for incremental program buildinfo: <c>Start</c> / <c>Start..End</c>
/// - for non incremental program buildinfo: <c>NonIncremental</c>（根文件名）
/// </summary>
public sealed class BuildInfoRoot : IJsonMarshaler, IJsonUnmarshaler
{
    public BuildInfoFileId Start;
    public BuildInfoFileId End;

    /// <summary>Root of a non incremental program。</summary>
    public string NonIncremental = "";

    public void MarshalJSONTo(Encoder encoder)
    {
        if (Start != 0)
        {
            if (End != 0)
            {
                // Go: json.Marshal([2]BuildInfoFileId{b.Start, b.End})
                encoder.WriteToken(BeginArray);
                encoder.WriteToken(JsonToken.Number(Start.Value.ToString(CultureInfo.InvariantCulture)));
                encoder.WriteToken(JsonToken.Number(End.Value.ToString(CultureInfo.InvariantCulture)));
                encoder.WriteToken(EndArray);
            }
            else
            {
                encoder.WriteToken(JsonToken.Number(Start.Value.ToString(CultureInfo.InvariantCulture)));
            }
        }
        else
        {
            encoder.WriteToken(JsonToken.String(NonIncremental));
        }
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        // Go: 先试 *[2]int，再试 int，最后试 string。
        switch (decoder.PeekKind())
        {
            case JsonKind.BeginArray:
                decoder.ReadToken();
                var start = decoder.Decode<int>();
                var end = decoder.Decode<int>();
                decoder.ReadToken(); // ]
                Start = start;
                End = end;
                return;
            case JsonKind.Number:
                Start = decoder.Decode<int>();
                return;
            case JsonKind.String:
                NonIncremental = decoder.Decode<string>() ?? "";
                return;
            default:
                throw new JsonException("invalid BuildInfoRoot");
        }
    }
}

/// <summary>Go: <c>type buildInfoFileInfoNoSignature struct</c>（buildInfo.go:74-79）。</summary>
internal sealed class BuildInfoFileInfoNoSignature : IJsonMarshaler, IJsonUnmarshaler
{
    public string Version = "";
    public bool NoSignature;
    public bool AffectsGlobalScope;

    /// <summary>Go: <c>ImpliedNodeFormat core.ResolutionMode</c>（= <c>ModuleKind</c>）。</summary>
    public ModuleKind ImpliedNodeFormat;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        if (Version.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("version"));
            encoder.WriteToken(JsonToken.String(Version));
        }
        if (NoSignature)
        {
            encoder.WriteToken(JsonToken.String("noSignature"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (AffectsGlobalScope)
        {
            encoder.WriteToken(JsonToken.String("affectsGlobalScope"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (ImpliedNodeFormat != 0)
        {
            encoder.WriteToken(JsonToken.String("impliedNodeFormat"));
            encoder.WriteToken(JsonToken.Number(((int)ImpliedNodeFormat).ToString(CultureInfo.InvariantCulture)));
        }
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("invalid buildInfoFileInfoNoSignature");
        }
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "version":
                    Version = decoder.Decode<string>() ?? "";
                    break;
                case "noSignature":
                    NoSignature = decoder.Decode<bool>();
                    break;
                case "affectsGlobalScope":
                    AffectsGlobalScope = decoder.Decode<bool>();
                    break;
                case "impliedNodeFormat":
                    ImpliedNodeFormat = (ModuleKind)decoder.Decode<int>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type buildInfoFileInfoWithSignature struct</c>（buildInfo.go:84-89）。</summary>
internal sealed class BuildInfoFileInfoWithSignature : IJsonMarshaler, IJsonUnmarshaler
{
    public string Version = "";
    public string Signature = "";
    public bool AffectsGlobalScope;
    public ModuleKind ImpliedNodeFormat;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        if (Version.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("version"));
            encoder.WriteToken(JsonToken.String(Version));
        }
        if (Signature.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("signature"));
            encoder.WriteToken(JsonToken.String(Signature));
        }
        if (AffectsGlobalScope)
        {
            encoder.WriteToken(JsonToken.String("affectsGlobalScope"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (ImpliedNodeFormat != 0)
        {
            encoder.WriteToken(JsonToken.String("impliedNodeFormat"));
            encoder.WriteToken(JsonToken.Number(((int)ImpliedNodeFormat).ToString(CultureInfo.InvariantCulture)));
        }
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("invalid buildInfoFileInfoWithSignature");
        }
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "version":
                    Version = decoder.Decode<string>() ?? "";
                    break;
                case "signature":
                    Signature = decoder.Decode<string>() ?? "";
                    break;
                case "affectsGlobalScope":
                    AffectsGlobalScope = decoder.Decode<bool>();
                    break;
                case "impliedNodeFormat":
                    ImpliedNodeFormat = (ModuleKind)decoder.Decode<int>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type BuildInfoFileInfo struct</c>（buildInfo.go:91-95）。</summary>
public sealed class BuildInfoFileInfo : IJsonMarshaler, IJsonUnmarshaler
{
    internal string _signature = "";
    internal BuildInfoFileInfoNoSignature? _noSignature;
    internal BuildInfoFileInfoWithSignature? _fileInfo;

    public FileInfo? GetFileInfo()
    {
        // Go 的 `if b == nil` 分支在 C# 里是死代码（实例方法不能在 null 上分派）。
        if (_signature != "")
        {
            return new FileInfo
            {
                _version = _signature,
                _signature = _signature,
                _impliedNodeFormat = ModuleKind.CommonJS,
            };
        }
        if (_noSignature is not null)
        {
            return new FileInfo
            {
                _version = _noSignature.Version,
                _affectsGlobalScope = _noSignature.AffectsGlobalScope,
                _impliedNodeFormat = _noSignature.ImpliedNodeFormat,
            };
        }
        return new FileInfo
        {
            _version = _fileInfo!.Version,
            _signature = IfElse(_fileInfo.Signature == "", _fileInfo.Version, _fileInfo.Signature),
            _affectsGlobalScope = _fileInfo.AffectsGlobalScope,
            _impliedNodeFormat = _fileInfo.ImpliedNodeFormat,
        };
    }

    public bool HasSignature() => _signature != "";

    public void MarshalJSONTo(Encoder encoder)
    {
        if (_signature != "")
        {
            encoder.WriteToken(JsonToken.String(_signature));
            return;
        }
        if (_noSignature is not null)
        {
            _noSignature.MarshalJSONTo(encoder);
            return;
        }
        _fileInfo!.MarshalJSONTo(encoder);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        // Go: 先试 string，再试 noSignature（要求 NoSignature 为 true），最后试 withSignature。
        if (decoder.PeekKind() == JsonKind.String)
        {
            _signature = decoder.Decode<string>() ?? "";
            return;
        }
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("invalid BuildInfoFileInfo");
        }
        // Go 对同一份 data 依次试两个结构体：noSignature 多一个 noSignature 字段，withSignature 多一个
        // signature 字段；其余键完全相同。这里一遍读出全部键，再按 Go 的判定二分。
        var version = "";
        var signature = "";
        var noSignature = false;
        var affectsGlobalScope = false;
        var impliedNodeFormat = ModuleKind.None;
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "version":
                    version = decoder.Decode<string>() ?? "";
                    break;
                case "signature":
                    signature = decoder.Decode<string>() ?? "";
                    break;
                case "noSignature":
                    noSignature = decoder.Decode<bool>();
                    break;
                case "affectsGlobalScope":
                    affectsGlobalScope = decoder.Decode<bool>();
                    break;
                case "impliedNodeFormat":
                    impliedNodeFormat = (ModuleKind)decoder.Decode<int>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }
        decoder.ReadToken();
        if (!noSignature)
        {
            _fileInfo = new BuildInfoFileInfoWithSignature
            {
                Version = version,
                Signature = signature,
                AffectsGlobalScope = affectsGlobalScope,
                ImpliedNodeFormat = impliedNodeFormat,
            };
            return;
        }
        _noSignature = new BuildInfoFileInfoNoSignature
        {
            Version = version,
            NoSignature = true,
            AffectsGlobalScope = affectsGlobalScope,
            ImpliedNodeFormat = impliedNodeFormat,
        };
    }
}

/// <summary>Go: <c>type BuildInfoReferenceMapEntry struct</c>（buildInfo.go:177-180）。</summary>
public sealed class BuildInfoReferenceMapEntry : IJsonMarshaler, IJsonUnmarshaler
{
    public BuildInfoFileId FileId;
    public BuildInfoFileIdListId FileIdListId;

    public void MarshalJSONTo(Encoder encoder)
    {
        // Go: json.Marshal([2]int{int(b.FileId), int(b.FileIdListId)})
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.Number(FileIdListId.Value.ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(EndArray);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginArray)
        {
            throw new JsonException("invalid BuildInfoReferenceMapEntry");
        }
        FileId = decoder.Decode<int>();
        FileIdListId = decoder.Decode<int>();
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type BuildInfoDiagnostic struct</c>（buildInfo.go:198-216）。</summary>
public sealed class BuildInfoDiagnostic : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>BuildInfoFileId if it is for a File thats other than its stored for。</summary>
    public BuildInfoFileId File;

    public bool NoFile;
    public int Pos;
    public int End;
    public int Code;
    public Category Category;
    public string Source = "";
    public string MessageText = "";
    public Key MessageKey;

    /// <summary>Go 的 nil 切片 → C# 的 null（omitzero 只省略 nil）。</summary>
    public List<string>? MessageArgs;

    public List<BuildInfoDiagnostic>? MessageChain;
    public List<BuildInfoDiagnostic>? RelatedInformation;
    public bool ReportsUnnecessary;
    public bool ReportsDeprecated;
    public bool SkippedOnNoEmit;
    public BuildInfoRepopulateInfo? RepopulateInfo;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        if (File != 0)
        {
            encoder.WriteToken(JsonToken.String("file"));
            encoder.WriteToken(JsonToken.Number(File.Value.ToString(CultureInfo.InvariantCulture)));
        }
        if (NoFile)
        {
            encoder.WriteToken(JsonToken.String("noFile"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (Pos != 0)
        {
            encoder.WriteToken(JsonToken.String("pos"));
            encoder.WriteToken(JsonToken.Number(Pos.ToString(CultureInfo.InvariantCulture)));
        }
        if (End != 0)
        {
            encoder.WriteToken(JsonToken.String("end"));
            encoder.WriteToken(JsonToken.Number(End.ToString(CultureInfo.InvariantCulture)));
        }
        if (Code != 0)
        {
            encoder.WriteToken(JsonToken.String("code"));
            encoder.WriteToken(JsonToken.Number(Code.ToString(CultureInfo.InvariantCulture)));
        }
        if (Category != 0)
        {
            encoder.WriteToken(JsonToken.String("category"));
            encoder.WriteToken(JsonToken.Number(((int)Category).ToString(CultureInfo.InvariantCulture)));
        }
        if (Source.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("source"));
            encoder.WriteToken(JsonToken.String(Source));
        }
        if (MessageText.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("messageText"));
            encoder.WriteToken(JsonToken.String(MessageText));
        }
        if (MessageKey.Value.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("messageKey"));
            encoder.WriteToken(JsonToken.String(MessageKey.Value));
        }
        if (MessageArgs is not null)
        {
            encoder.WriteToken(JsonToken.String("messageArgs"));
            encoder.WriteToken(BeginArray);
            foreach (var arg in MessageArgs)
            {
                encoder.WriteToken(JsonToken.String(arg));
            }
            encoder.WriteToken(EndArray);
        }
        if (MessageChain is not null)
        {
            encoder.WriteToken(JsonToken.String("messageChain"));
            encoder.WriteToken(BeginArray);
            foreach (var chain in MessageChain)
            {
                chain.MarshalJSONTo(encoder);
            }
            encoder.WriteToken(EndArray);
        }
        if (RelatedInformation is not null)
        {
            encoder.WriteToken(JsonToken.String("relatedInformation"));
            encoder.WriteToken(BeginArray);
            foreach (var info in RelatedInformation)
            {
                info.MarshalJSONTo(encoder);
            }
            encoder.WriteToken(EndArray);
        }
        if (ReportsUnnecessary)
        {
            encoder.WriteToken(JsonToken.String("reportsUnnecessary"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (ReportsDeprecated)
        {
            encoder.WriteToken(JsonToken.String("reportsDeprecated"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (SkippedOnNoEmit)
        {
            encoder.WriteToken(JsonToken.String("skippedOnNoEmit"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (RepopulateInfo is not null)
        {
            encoder.WriteToken(JsonToken.String("repopulateInfo"));
            RepopulateInfo.MarshalJSONTo(encoder);
        }
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("invalid BuildInfoDiagnostic");
        }
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "file":
                    File = decoder.Decode<int>();
                    break;
                case "noFile":
                    NoFile = decoder.Decode<bool>();
                    break;
                case "pos":
                    Pos = decoder.Decode<int>();
                    break;
                case "end":
                    End = decoder.Decode<int>();
                    break;
                case "code":
                    Code = decoder.Decode<int>();
                    break;
                case "category":
                    Category = (Category)decoder.Decode<int>();
                    break;
                case "source":
                    Source = decoder.Decode<string>() ?? "";
                    break;
                case "messageText":
                    MessageText = decoder.Decode<string>() ?? "";
                    break;
                case "messageKey":
                    MessageKey = new Key(decoder.Decode<string>() ?? "");
                    break;
                case "messageArgs":
                    MessageArgs = DecodeStringListOrNull(decoder);
                    break;
                case "messageChain":
                    MessageChain = DecodeObjectListOrNull<BuildInfoDiagnostic>(decoder);
                    break;
                case "relatedInformation":
                    RelatedInformation = DecodeObjectListOrNull<BuildInfoDiagnostic>(decoder);
                    break;
                case "reportsUnnecessary":
                    ReportsUnnecessary = decoder.Decode<bool>();
                    break;
                case "reportsDeprecated":
                    ReportsDeprecated = decoder.Decode<bool>();
                    break;
                case "skippedOnNoEmit":
                    SkippedOnNoEmit = decoder.Decode<bool>();
                    break;
                case "repopulateInfo":
                    RepopulateInfo = decoder.DecodeNew<BuildInfoRepopulateInfo>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type BuildInfoRepopulateInfo struct</c>（buildInfo.go:218-223）。</summary>
public sealed class BuildInfoRepopulateInfo : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go 的 tag 是 <c>json:"kind"</c>（没有 omitzero）→ 恒写出。</summary>
    public RepopulateDiagnosticKind Kind;

    public string ModuleReference = "";
    public ModuleKind Mode;
    public string PackageName = "";

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("kind"));
        encoder.WriteToken(JsonToken.Number(((int)Kind).ToString(CultureInfo.InvariantCulture)));
        if (ModuleReference.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("moduleReference"));
            encoder.WriteToken(JsonToken.String(ModuleReference));
        }
        if (Mode != 0)
        {
            encoder.WriteToken(JsonToken.String("mode"));
            encoder.WriteToken(JsonToken.Number(((int)Mode).ToString(CultureInfo.InvariantCulture)));
        }
        if (PackageName.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("packageName"));
            encoder.WriteToken(JsonToken.String(PackageName));
        }
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("invalid BuildInfoRepopulateInfo");
        }
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "kind":
                    Kind = (RepopulateDiagnosticKind)decoder.Decode<int>();
                    break;
                case "moduleReference":
                    ModuleReference = decoder.Decode<string>() ?? "";
                    break;
                case "mode":
                    Mode = (ModuleKind)decoder.Decode<int>();
                    break;
                case "packageName":
                    PackageName = decoder.Decode<string>() ?? "";
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type BuildInfoDiagnosticsOfFile struct</c>（buildInfo.go:225-228）。</summary>
public sealed class BuildInfoDiagnosticsOfFile : IJsonMarshaler, IJsonUnmarshaler
{
    public BuildInfoFileId FileId;
    public List<BuildInfoDiagnostic>? Diagnostics;

    public void MarshalJSONTo(Encoder encoder)
    {
        // Go: json.Marshal([]any{b.FileId, b.Diagnostics})
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
        if (Diagnostics is null)
        {
            encoder.WriteToken(Null);
        }
        else
        {
            encoder.WriteToken(BeginArray);
            foreach (var diagnostic in Diagnostics)
            {
                diagnostic.MarshalJSONTo(encoder);
            }
            encoder.WriteToken(EndArray);
        }
        encoder.WriteToken(EndArray);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginArray)
        {
            throw new JsonException("invalid BuildInfoDiagnosticsOfFile");
        }
        FileId = decoder.Decode<int>();
        Diagnostics = DecodeObjectListOrNull<BuildInfoDiagnostic>(decoder);
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type BuildInfoSemanticDiagnostic struct</c>（buildInfo.go:261-264）。</summary>
public sealed class BuildInfoSemanticDiagnostic : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>File is not in changedSet and still doesnt have cached diagnostics。</summary>
    public BuildInfoFileId FileId;

    /// <summary>Diagnostics for file。</summary>
    public BuildInfoDiagnosticsOfFile? Diagnostics;

    public void MarshalJSONTo(Encoder encoder)
    {
        if (FileId != 0)
        {
            encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        Diagnostics!.MarshalJSONTo(encoder);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.PeekKind() == JsonKind.Number)
        {
            FileId = decoder.Decode<int>();
            return;
        }
        Diagnostics = decoder.DecodeNew<BuildInfoDiagnosticsOfFile>();
    }
}

/// <summary>
/// Go: <c>type BuildInfoFilePendingEmit struct</c>（buildInfo.go:294-297）。<br/>
/// fileId if pending emit is same as what compilerOptions suggest / [fileId] if pending emit is only dts file emit /
/// [fileId, emitKind] if any other type emit is pending。
/// </summary>
public sealed class BuildInfoFilePendingEmit : IJsonMarshaler, IJsonUnmarshaler
{
    public BuildInfoFileId FileId;
    public FileEmitKind EmitKind;

    public void MarshalJSONTo(Encoder encoder)
    {
        if (EmitKind == 0)
        {
            encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        if (EmitKind == FileEmitKind.Dts)
        {
            encoder.WriteToken(BeginArray);
            encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
            encoder.WriteToken(EndArray);
            return;
        }
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.Number(((uint)EmitKind).ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(EndArray);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.PeekKind() == JsonKind.Number)
        {
            FileId = decoder.Decode<int>();
            return;
        }
        if (decoder.ReadToken().Kind != JsonKind.BeginArray)
        {
            throw new JsonException("invalid BuildInfoFilePendingEmit");
        }
        var intTuple = new List<int>();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            intTuple.Add(decoder.Decode<int>());
        }
        decoder.ReadToken();
        switch (intTuple.Count)
        {
            case 1:
                FileId = intTuple[0];
                EmitKind = FileEmitKind.Dts;
                return;
            case 2:
                FileId = intTuple[0];
                EmitKind = (FileEmitKind)intTuple[1];
                return;
            default:
                throw new JsonException($"invalid BuildInfoFilePendingEmit: expected 1 or 2 integers, got {intTuple.Count}");
        }
    }
}

/// <summary>
/// Go: <c>type BuildInfoEmitSignature struct</c>（buildInfo.go:343-348）。<br/>
/// [fileId, signature] if different from file's signature / fileId if file wasnt emitted。
/// </summary>
public sealed class BuildInfoEmitSignature : IJsonMarshaler, IJsonUnmarshaler
{
    public BuildInfoFileId FileId;

    /// <summary>Signature if it is different from file's Signature。</summary>
    public string Signature = "";

    /// <summary>true if signature is different only in dtsMap value。</summary>
    public bool DiffersOnlyInDtsMap;

    /// <summary>true if signature is different in options used to emit file。</summary>
    public bool DiffersInOptions;

    internal bool NoEmitSignature() => Signature == "" && !DiffersOnlyInDtsMap && !DiffersInOptions;

    internal EmitSignature ToEmitSignature(Path path, SyncMap<Path, EmitSignature> emitSignatures)
    {
        var signature = "";
        string[]? signatureWithDifferentOptions = null;
        if (DiffersOnlyInDtsMap)
        {
            var (info, _) = emitSignatures.Load(path);
            signatureWithDifferentOptions = [info!._signature];
        }
        else if (DiffersInOptions)
        {
            signatureWithDifferentOptions = [Signature];
        }
        else
        {
            signature = Signature;
        }
        return new EmitSignature
        {
            _signature = signature,
            _signatureWithDifferentOptions = signatureWithDifferentOptions,
        };
    }

    public void MarshalJSONTo(Encoder encoder)
    {
        if (NoEmitSignature())
        {
            encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
            return;
        }
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.Number(FileId.Value.ToString(CultureInfo.InvariantCulture)));
        if (DiffersOnlyInDtsMap)
        {
            // Go: signature = []string{}（非 nil 空切片 → 写出 []）
            encoder.WriteToken(BeginArray);
            encoder.WriteToken(EndArray);
        }
        else if (DiffersInOptions)
        {
            encoder.WriteToken(BeginArray);
            encoder.WriteToken(JsonToken.String(Signature));
            encoder.WriteToken(EndArray);
        }
        else
        {
            encoder.WriteToken(JsonToken.String(Signature));
        }
        encoder.WriteToken(EndArray);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.PeekKind() == JsonKind.Number)
        {
            FileId = decoder.Decode<int>();
            return;
        }
        if (decoder.ReadToken().Kind != JsonKind.BeginArray)
        {
            throw new JsonException("invalid BuildInfoEmitSignature");
        }
        if (decoder.PeekKind() != JsonKind.Number)
        {
            throw new JsonException("invalid fileId in BuildInfoEmitSignature: expected number");
        }
        FileId = decoder.Decode<int>();
        switch (decoder.PeekKind())
        {
            case JsonKind.String:
                Signature = decoder.Decode<string>() ?? "";
                break;
            case JsonKind.BeginArray:
                decoder.ReadToken();
                var count = 0;
                while (decoder.PeekKind() != JsonKind.EndArray)
                {
                    if (count == 0)
                    {
                        Signature = decoder.Decode<string>() ?? "";
                    }
                    else
                    {
                        decoder.SkipValue();
                    }
                    count++;
                }
                decoder.ReadToken();
                if (count == 0)
                {
                    DiffersOnlyInDtsMap = true;
                }
                else if (count == 1)
                {
                    DiffersInOptions = true;
                }
                else
                {
                    throw new JsonException($"invalid signature in BuildInfoEmitSignature: expected string or []string with 0 or 1 element, got {count} elements");
                }
                break;
            default:
                throw new JsonException("invalid signature in BuildInfoEmitSignature: expected string or []string");
        }
        if (decoder.PeekKind() != JsonKind.EndArray)
        {
            throw new JsonException("invalid BuildInfoEmitSignature: expected 2 elements");
        }
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type BuildInfoResolvedRoot struct</c>（buildInfo.go:445-448）。</summary>
public sealed class BuildInfoResolvedRoot : IJsonMarshaler, IJsonUnmarshaler
{
    public BuildInfoFileId Resolved;
    public BuildInfoFileId Root;

    public void MarshalJSONTo(Encoder encoder)
    {
        // Go: json.Marshal([2]BuildInfoFileId{b.Resolved, b.Root})
        encoder.WriteToken(BeginArray);
        encoder.WriteToken(JsonToken.Number(Resolved.Value.ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(JsonToken.Number(Root.Value.ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(EndArray);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginArray)
        {
            throw new JsonException("invalid BuildInfoResolvedRoot");
        }
        Resolved = decoder.Decode<int>();
        Root = decoder.Decode<int>();
        decoder.ReadToken();
    }
}

/// <summary>Go: <c>type BuildInfo struct</c>（buildInfo.go:466-492）。</summary>
public sealed class BuildInfo : IJsonMarshaler, IJsonUnmarshaler
{
    public string Version = "";

    // Common between incremental and tsc -b buildinfo for non incremental programs
    public bool Errors;
    public bool CheckPending;
    public List<BuildInfoRoot>? Root;
    public List<string>? PackageJsons;
    public List<string>? MissingPackageJsons;
    public List<string>? ContentMapperIdentities;

    // IncrementalProgram info
    public List<string>? FileNames;
    public List<BuildInfoFileInfo>? FileInfos;
    public List<List<BuildInfoFileId>>? FileIdsList;
    public OrderedMap<string, object?>? Options;
    public List<BuildInfoReferenceMapEntry>? ReferencedMap;
    public List<BuildInfoSemanticDiagnostic>? SemanticDiagnosticsPerFile;
    public List<BuildInfoDiagnosticsOfFile?>? EmitDiagnosticsPerFile;
    public List<BuildInfoFileId>? ChangeFileSet;
    public List<BuildInfoFilePendingEmit>? AffectedFilesPendingEmit;

    /// <summary>Because this is only output file in the program, we dont need fileId to deduplicate name。</summary>
    public string LatestChangedDtsFile = "";

    public List<BuildInfoEmitSignature>? EmitSignatures;
    public List<BuildInfoResolvedRoot>? ResolvedRoot;

    // NonIncrementalProgram info
    public bool SemanticErrors;

    public bool IsValidVersion() => Version == TypeScript.Core.Core.Version();

    /// <summary>Go: <c>func (b *BuildInfo) ContentMapperIdentitiesMatch(current []string) bool</c>。</summary>
    public bool ContentMapperIdentitiesMatch(List<string>? current)
    {
        // Go: slices.Equal(b.ContentMapperIdentities, current)（nil 与非 nil 空切片相等）。
        var left = ContentMapperIdentities;
        var leftLength = left?.Count ?? 0;
        var rightLength = current?.Count ?? 0;
        if (leftLength != rightLength)
        {
            return false;
        }
        for (var i = 0; i < leftLength; i++)
        {
            if (left![i] != current![i])
            {
                return false;
            }
        }
        return true;
    }

    public bool IsIncremental() => FileNames is { Count: > 0 };

    internal string FileName(BuildInfoFileId fileId)
    {
        if (fileId < 1 || (int)fileId > (FileNames?.Count ?? 0))
        {
            return "";
        }
        return FileNames![(int)fileId - 1];
    }

    internal BuildInfoFileInfo? FileInfo(BuildInfoFileId fileId)
    {
        if (fileId < 1 || (int)fileId > (FileInfos?.Count ?? 0))
        {
            return null;
        }
        return FileInfos![(int)fileId - 1];
    }

    public CompilerOptions GetCompilerOptions(string buildInfoDirectory)
    {
        var options = new CompilerOptions();
        if (Options is not null)
        {
            foreach (var (option, value) in Options.Entries())
            {
                if (buildInfoDirectory != "")
                {
                    var (result, ok) = ConvertOptionToAbsolutePath(option, value, CommandLineCompilerOptionsMap, buildInfoDirectory);
                    if (ok)
                    {
                        ParseCompilerOptions(option, result, options);
                        continue;
                    }
                }
                ParseCompilerOptions(option, value, options);
            }
        }
        return options;
    }

    public bool IsEmitPending(ParsedCommandLine resolved, string buildInfoDirectory)
    {
        // Some of the emit files like source map or dts etc are not yet done
        if (!resolved.CompilerOptions()!.NoEmit.IsTrue() || resolved.CompilerOptions()!.GetEmitDeclarations())
        {
            var pendingEmit = GetPendingEmitKindWithOptions(resolved.CompilerOptions()!, GetCompilerOptions(buildInfoDirectory));
            if (resolved.CompilerOptions()!.NoEmit.IsTrue())
            {
                pendingEmit &= FileEmitKind.DtsErrors;
            }
            return pendingEmit != 0;
        }
        return false;
    }

    public IEnumerable<string> GetPackageJsons(string buildInfoDirectory) =>
        GetNormalizedPaths(PackageJsons, buildInfoDirectory);

    public IEnumerable<string> GetMissingPackageJsons(string buildInfoDirectory) =>
        GetNormalizedPaths(MissingPackageJsons, buildInfoDirectory);

    public BuildInfoRootInfoReader GetBuildInfoRootInfoReader(string buildInfoDirectory, TypeScript.TSPath.ComparePathsOptions comparePathOptions)
    {
        var resolvedRootFileInfos = new Dictionary<Path, BuildInfoFileInfo>(FileNames?.Count ?? 0);
        // Roots of the File
        var rootToResolved = NewOrderedMapWithSizeHint<Path, Path>(FileNames?.Count ?? 0);
        var resolvedToRoot = new Dictionary<Path, Path>(ResolvedRoot?.Count ?? 0);
        Path ToPath2(string fileName) => ToPath(fileName, buildInfoDirectory, comparePathOptions.UseCaseSensitiveFileNames);

        // Create map from resolvedRoot to Root
        if (ResolvedRoot is not null)
        {
            foreach (var resolved in ResolvedRoot)
            {
                var resolvedRoot = FileName(resolved.Resolved);
                var root = FileName(resolved.Root);
                if (resolvedRoot != "" && root != "")
                {
                    resolvedToRoot[ToPath2(resolvedRoot)] = ToPath2(root);
                }
            }
        }

        void AddRoot(string resolvedRoot, BuildInfoFileInfo? fileInfo)
        {
            if (resolvedRoot == "")
            {
                return;
            }
            var resolvedRootPath = ToPath2(resolvedRoot);
            if (resolvedToRoot.TryGetValue(resolvedRootPath, out var rootPath))
            {
                rootToResolved.Set(rootPath, resolvedRootPath);
            }
            else
            {
                rootToResolved.Set(resolvedRootPath, resolvedRootPath);
            }
            if (fileInfo is not null)
            {
                resolvedRootFileInfos[resolvedRootPath] = fileInfo;
            }
        }

        if (Root is not null)
        {
            foreach (var root in Root)
            {
                if (root.NonIncremental != "")
                {
                    AddRoot(root.NonIncremental, null);
                }
                else if (root.End == 0)
                {
                    AddRoot(FileName(root.Start), FileInfo(root.Start));
                }
                else
                {
                    for (var i = (int)root.Start; i <= (int)root.End; i++)
                    {
                        AddRoot(FileName(i), FileInfo(i));
                    }
                }
            }
        }

        return new BuildInfoRootInfoReader
        {
            _resolvedRootFileInfos = resolvedRootFileInfos,
            _rootToResolved = rootToResolved,
        };
    }

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        if (Version.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("version"));
            encoder.WriteToken(JsonToken.String(Version));
        }
        if (Errors)
        {
            encoder.WriteToken(JsonToken.String("errors"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        if (CheckPending)
        {
            encoder.WriteToken(JsonToken.String("checkPending"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        WriteArray(encoder, "root", Root, static (enc, item) => item.MarshalJSONTo(enc));
        WriteStringArray(encoder, "packageJsons", PackageJsons);
        WriteStringArray(encoder, "missingPackageJsons", MissingPackageJsons);
        WriteStringArray(encoder, "contentMapperIdentities", ContentMapperIdentities);
        WriteStringArray(encoder, "fileNames", FileNames);
        WriteArray(encoder, "fileInfos", FileInfos, static (enc, item) => item.MarshalJSONTo(enc));
        if (FileIdsList is not null)
        {
            encoder.WriteToken(JsonToken.String("fileIdsList"));
            encoder.WriteToken(BeginArray);
            foreach (var fileIds in FileIdsList)
            {
                encoder.WriteToken(BeginArray);
                foreach (var fileId in fileIds)
                {
                    encoder.WriteToken(JsonToken.Number(fileId.Value.ToString(CultureInfo.InvariantCulture)));
                }
                encoder.WriteToken(EndArray);
            }
            encoder.WriteToken(EndArray);
        }
        if (Options is not null)
        {
            encoder.WriteToken(JsonToken.String("options"));
            Options.MarshalJSONTo(encoder);
        }
        WriteArray(encoder, "referencedMap", ReferencedMap, static (enc, item) => item.MarshalJSONTo(enc));
        WriteArray(encoder, "semanticDiagnosticsPerFile", SemanticDiagnosticsPerFile, static (enc, item) => item.MarshalJSONTo(enc));
        WriteArray(encoder, "emitDiagnosticsPerFile", EmitDiagnosticsPerFile, static (enc, item) =>
        {
            if (item is null)
            {
                // Go 的 `[]*BuildInfoDiagnosticsOfFile` 可以含 nil → 写成 null。
                enc.WriteToken(Null);
            }
            else
            {
                item.MarshalJSONTo(enc);
            }
        });
        if (ChangeFileSet is not null)
        {
            encoder.WriteToken(JsonToken.String("changeFileSet"));
            encoder.WriteToken(BeginArray);
            foreach (var fileId in ChangeFileSet)
            {
                encoder.WriteToken(JsonToken.Number(fileId.Value.ToString(CultureInfo.InvariantCulture)));
            }
            encoder.WriteToken(EndArray);
        }
        WriteArray(encoder, "affectedFilesPendingEmit", AffectedFilesPendingEmit, static (enc, item) => item.MarshalJSONTo(enc));
        if (LatestChangedDtsFile.Length != 0)
        {
            encoder.WriteToken(JsonToken.String("latestChangedDtsFile"));
            encoder.WriteToken(JsonToken.String(LatestChangedDtsFile));
        }
        WriteArray(encoder, "emitSignatures", EmitSignatures, static (enc, item) => item.MarshalJSONTo(enc));
        WriteArray(encoder, "resolvedRoot", ResolvedRoot, static (enc, item) => item.MarshalJSONTo(enc));
        if (SemanticErrors)
        {
            encoder.WriteToken(JsonToken.String("semanticErrors"));
            encoder.WriteToken(JsonToken.Bool(true));
        }
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("invalid BuildInfo");
        }
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "version":
                    Version = decoder.Decode<string>() ?? "";
                    break;
                case "errors":
                    Errors = decoder.Decode<bool>();
                    break;
                case "checkPending":
                    CheckPending = decoder.Decode<bool>();
                    break;
                case "root":
                    Root = DecodeObjectListOrNull<BuildInfoRoot>(decoder);
                    break;
                case "packageJsons":
                    PackageJsons = DecodeStringListOrNull(decoder);
                    break;
                case "missingPackageJsons":
                    MissingPackageJsons = DecodeStringListOrNull(decoder);
                    break;
                case "contentMapperIdentities":
                    ContentMapperIdentities = DecodeStringListOrNull(decoder);
                    break;
                case "fileNames":
                    FileNames = DecodeStringListOrNull(decoder);
                    break;
                case "fileInfos":
                    FileInfos = DecodeObjectListOrNull<BuildInfoFileInfo>(decoder);
                    break;
                case "fileIdsList":
                    FileIdsList = DecodeFileIdListListOrNull(decoder);
                    break;
                case "options":
                    Options = DecodeInstanceOrNull<OrderedMap<string, object?>>(decoder);
                    break;
                case "referencedMap":
                    ReferencedMap = DecodeObjectListOrNull<BuildInfoReferenceMapEntry>(decoder);
                    break;
                case "semanticDiagnosticsPerFile":
                    SemanticDiagnosticsPerFile = DecodeObjectListOrNull<BuildInfoSemanticDiagnostic>(decoder);
                    break;
                case "emitDiagnosticsPerFile":
                    EmitDiagnosticsPerFile = DecodeNullableObjectListOrNull<BuildInfoDiagnosticsOfFile>(decoder);
                    break;
                case "changeFileSet":
                    ChangeFileSet = DecodeFileIdListOrNull(decoder);
                    break;
                case "affectedFilesPendingEmit":
                    AffectedFilesPendingEmit = DecodeObjectListOrNull<BuildInfoFilePendingEmit>(decoder);
                    break;
                case "latestChangedDtsFile":
                    LatestChangedDtsFile = decoder.Decode<string>() ?? "";
                    break;
                case "emitSignatures":
                    EmitSignatures = DecodeObjectListOrNull<BuildInfoEmitSignature>(decoder);
                    break;
                case "resolvedRoot":
                    ResolvedRoot = DecodeObjectListOrNull<BuildInfoResolvedRoot>(decoder);
                    break;
                case "semanticErrors":
                    SemanticErrors = decoder.Decode<bool>();
                    break;
                default:
                    decoder.SkipValue();
                    break;
            }
        }
        decoder.ReadToken();
    }

    // Go 的 `omitzero`：nil 切片省略；非 nil 写出数组（含空数组）。
    private static void WriteArray<T>(Encoder encoder, string name, List<T>? items, Action<Encoder, T> writeItem)
    {
        if (items is null)
        {
            return;
        }
        encoder.WriteToken(JsonToken.String(name));
        encoder.WriteToken(BeginArray);
        foreach (var item in items)
        {
            writeItem(encoder, item);
        }
        encoder.WriteToken(EndArray);
    }

    private static void WriteStringArray(Encoder encoder, string name, List<string>? items)
    {
        if (items is null)
        {
            return;
        }
        encoder.WriteToken(JsonToken.String(name));
        encoder.WriteToken(BeginArray);
        foreach (var item in items)
        {
            encoder.WriteToken(JsonToken.String(item));
        }
        encoder.WriteToken(EndArray);
    }
}

/// <summary>Go: <c>type BuildInfoRootInfoReader struct</c>（buildInfo.go:633-636）。</summary>
public sealed class BuildInfoRootInfoReader
{
    internal Dictionary<Path, BuildInfoFileInfo> _resolvedRootFileInfos = new();
    internal OrderedMap<Path, Path> _rootToResolved = new();

    /// <summary>Go: <c>func (b *BuildInfoRootInfoReader) GetBuildInfoFileInfo(inputFilePath tspath.Path) (*BuildInfoFileInfo, tspath.Path)</c>。</summary>
    public (BuildInfoFileInfo? Info, Path Resolved) GetBuildInfoFileInfo(Path inputFilePath)
    {
        if (_resolvedRootFileInfos.TryGetValue(inputFilePath, out var info))
        {
            return (info, inputFilePath);
        }
        var (resolved, ok) = _rootToResolved.Get(inputFilePath);
        if (ok)
        {
            return (_resolvedRootFileInfos.GetValueOrDefault(resolved), resolved);
        }
        return (null, (Path)"");
    }

    /// <summary>Go: <c>func (b *BuildInfoRootInfoReader) Roots() iter.Seq[tspath.Path]</c>。</summary>
    public IEnumerable<Path> Roots() => _rootToResolved.Keys();
}

public static partial class Incremental
{
    /// <summary>Go 的 <c>core.Map(List&lt;T&gt;?, f)</c>（BuildInfo 的集合字段是 <c>List&lt;T&gt;?</c>，见 BuildInfo.cs）。</summary>
    internal static U[] MapList<T, U>(List<T>? list, Func<T, U> f)
    {
        if (list is null)
        {
            return [];
        }
        var result = new U[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            result[i] = f(list[i]);
        }
        return result;
    }

    /// <summary>同 <see cref="MapList"/>，但保留 Go 的 nil（用于要写进 JSON 的字段：nil 要省略、非 nil 空切片要写 <c>[]</c>）。</summary>
    internal static List<U>? MapListOrNull<T, U>(List<T>? list, Func<T, U> f)
    {
        if (list is null)
        {
            return null;
        }
        var result = new List<U>(list.Count);
        foreach (var item in list)
        {
            result.Add(f(item));
        }
        return result;
    }

    /// <summary>Go 的 <c>[]T</c> → <c>List&lt;T&gt;?</c>（保留 nil）。</summary>
    internal static List<T>? ToListOrNull<T>(T[]? array) => array is null ? null : [.. array];

    /// <summary>
    /// Go 的 <c>json.Unmarshal(data, &amp;v)</c>（v 是切片）：JSON <c>null</c> → nil 切片（保持 null），
    /// 否则解成非 nil 的 <c>List&lt;T&gt;</c>，每个元素由 <paramref name="readItem"/> 解。
    /// （C# 的 `UnmarshalNew&lt;List&lt;T&gt;&gt;` 只能解标量元素：元素为对象时要逐个 `DecodeNew&lt;T&gt;`。）
    /// </summary>
    internal static List<T>? DecodeArrayOrNull<T>(Decoder decoder, Func<Decoder, T> readItem)
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return null;
        }
        decoder.ReadToken(); // [
        var list = new List<T>();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            list.Add(readItem(decoder));
        }
        decoder.ReadToken(); // ]
        return list;
    }

    /// <summary>Go 的 <c>[]string</c> 字段。</summary>
    internal static List<string>? DecodeStringListOrNull(Decoder decoder) =>
        DecodeArrayOrNull(decoder, static d => d.Decode<string>() ?? "");

    /// <summary>Go 的 <c>[]*T</c> 字段（T 实现 <see cref="IJsonUnmarshaler"/>）。</summary>
    internal static List<T>? DecodeObjectListOrNull<T>(Decoder decoder)
        where T : class, IJsonUnmarshaler, new() =>
        DecodeArrayOrNull(decoder, static d => d.DecodeNew<T>());

    /// <summary>Go 的 <c>[]*T</c> 字段，元素可以是 JSON <c>null</c>（Go 的 nil 元素）。</summary>
    internal static List<T?>? DecodeNullableObjectListOrNull<T>(Decoder decoder)
        where T : class, IJsonUnmarshaler, new() =>
        DecodeArrayOrNull(decoder, static d =>
        {
            if (d.PeekKind() == JsonKind.Null)
            {
                d.ReadToken();
                return null;
            }
            return d.DecodeNew<T>();
        });

    /// <summary>Go 的 <c>[]BuildInfoFileId</c>（自定义整数类型，JsonDecode 不认识它，只能逐个解 int 再转）。</summary>
    internal static List<BuildInfoFileId>? DecodeFileIdListOrNull(Decoder decoder) =>
        DecodeArrayOrNull(decoder, static d => (BuildInfoFileId)d.Decode<int>());

    /// <summary>Go 的 <c>[][]BuildInfoFileId</c>（fileIdsList）。</summary>
    internal static List<List<BuildInfoFileId>>? DecodeFileIdListListOrNull(Decoder decoder) =>
        DecodeArrayOrNull(decoder, static d => DecodeFileIdListOrNull(d) ?? []);

    /// <summary>同上，但目标是需要实例的对象类型（Go 的 <c>&amp;p</c> 为 nil 时 JSON <c>null</c> → nil 指针）。</summary>
    internal static T? DecodeInstanceOrNull<T>(Decoder decoder)
        where T : class, new()
    {
        if (decoder.PeekKind() == JsonKind.Null)
        {
            decoder.ReadToken();
            return null;
        }
        return decoder.DecodeNew<T>();
    }

    /// <summary>Go: <c>func newBuildInfoFileInfo(fileInfo *FileInfo) *BuildInfoFileInfo</c>（buildInfo.go:97）。</summary>
    internal static BuildInfoFileInfo NewBuildInfoFileInfo(FileInfo fileInfo)
    {
        if (fileInfo._version == fileInfo._signature)
        {
            if (!fileInfo._affectsGlobalScope && fileInfo._impliedNodeFormat == ModuleKind.CommonJS)
            {
                return new BuildInfoFileInfo { _signature = fileInfo._signature };
            }
        }
        else if (fileInfo._signature == "")
        {
            return new BuildInfoFileInfo
            {
                _noSignature = new BuildInfoFileInfoNoSignature
                {
                    Version = fileInfo._version,
                    NoSignature = true,
                    AffectsGlobalScope = fileInfo._affectsGlobalScope,
                    ImpliedNodeFormat = fileInfo._impliedNodeFormat,
                },
            };
        }
        return new BuildInfoFileInfo
        {
            _fileInfo = new BuildInfoFileInfoWithSignature
            {
                Version = fileInfo._version,
                Signature = IfElse(fileInfo._signature == fileInfo._version, "", fileInfo._signature),
                AffectsGlobalScope = fileInfo._affectsGlobalScope,
                ImpliedNodeFormat = fileInfo._impliedNodeFormat,
            },
        };
    }

    /// <summary>
    /// Go: <c>func ContentMapperIdentities(project contentmapper.Project) ([]string, error)</c>（buildInfo.go:501-506）。
    /// 错误路径 → 抛异常（§3.2）；`nil` project 返回 `null`（= Go 的 nil 切片）。
    /// </summary>
    public static List<string>? ContentMapperIdentities(TypeScript.ContentMapper.Project? project)
    {
        if (project is null)
        {
            return null;
        }
        return [.. project.Identities()];
    }

    /// <summary>Go: <c>func IsBuildInfoFileNameDefaultLibrary(fileName string) bool</c>（buildInfo.go:518）。</summary>
    public static bool IsBuildInfoFileNameDefaultLibrary(string fileName) =>
        !PathIsRelative(fileName) && !PathIsAbsolute(fileName);

    /// <summary>Go: <c>func getNormalizedPaths(paths []string, buildInfoDirectory string) iter.Seq[string]</c>。</summary>
    internal static IEnumerable<string> GetNormalizedPaths(List<string>? paths, string buildInfoDirectory)
    {
        if (paths is null)
        {
            yield break;
        }
        foreach (var path in paths)
        {
            yield return GetNormalizedAbsolutePath(path, buildInfoDirectory);
        }
    }
}
