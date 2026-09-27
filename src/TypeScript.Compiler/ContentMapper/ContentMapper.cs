// ported from: internal/contentmapper/contentmapper.go
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `Mapper` 内嵌 `Definition` + `Manifest`（字段提升：`m.Package`/`m.Name`/`m.Exec`…）→ C# 用
//    `Mapper : Definition`（Package/Extensions/Options 走继承）+ Manifest 的 5 个字段平铺到 `Mapper`。
//    另加 `Mapper(Definition, Manifest)` 与 `SetManifest(Manifest)`（C# 侧新增；Go 的复合字面量
//    `Mapper{Definition: d, Manifest: m}` / 赋值 `m.Manifest = manifest` 的对应物——平铺后无法整体赋值）。
// 2) Go 的 `json.Value`（= `jsontext.Value`，本质 `[]byte` 的原始 JSON）→ C# 的 `byte[]`；本项目的 Json 把
//    `byte[]` 当 base64（docs/json.md §2），所以原始 JSON 一律走 `JsonEncoder.WriteValue`/`JsonDecoder.ReadValue`。
// 3) `compilerOptionFields`（Go 用 reflect 读 `CompilerOptions` 的 json tag → 字段下标）→ 复用
//    `TypeScript.TsOptions.CompilerOptionFields`（TsOptions 切片按同一份 Go struct 生成的有序字段表，含
//    JsonName/Get/IsZero）。**本包因此引用 TsOptions**（Go 的依赖方向相反：tsoptions → contentmapper）；
//    只共享字段表，没有反向调用，报告里记为已知偏离。
// 4) `MarshalDeclaredOptions` 返回 `OrderedMap<string, byte[]>`（= Go 的 `OrderedMap[string, json.Value]`）。
//    **坑**：值类型是原始 JSON 字节，这种 map 不能直接交给 `Json.Marshal`（会写成 base64）；需要 JSON 文本请用
//    `MarshalDeclaredOptionsJson`（C# 侧新增，`TransformIdentity` 内部就用它）。
// 5) `ErrProjectUnavailable`（Go 的 `var ErrProjectUnavailable = errors.New(...)`）→ `ProjectUnavailableException`
//    （Go 的 errors.Is → C# 的 catch；见 docs/architecture.md §3.2）。
// 6) `TransformIdentity`/`MarshalDeclaredOptions` 需要 xxh3：Go 用 `github.com/zeebo/xxh3`，C# 侧是自写的
//    `TypeScript.Xxh3.Xxh3`（见该文件头；`Uint128` → `System.UInt128`）。
using System.Text;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Json;
using TypeScript.TsOptions;
using static TypeScript.Collections.Collections;
using static TypeScript.Json.Json;
// `CompilerOptionFields`/`CompilerOptionField` 是 TsOptions 包级静态类的成员 → using static 后裸名可用。
using static TypeScript.TsOptions.TsOptions;
// `System.Text.Encoder/JsonDecoder`（隐式 using）与 `TypeScript.Json.Encoder/JsonDecoder` 同名 → CS0104，必须别名。
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.ContentMapper;

/// <summary>
/// Go: <c>type Definition struct</c>。tsconfig 的 <c>contentMappers</c> 里声明的一个 content mapper：
/// 实现它的 npm 包 + 它注册的（否则不被支持的）文件扩展名。
/// </summary>
public class Definition : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: <c>Package string `json:"package"`</c>。</summary>
    public string Package = "";

    /// <summary>Go: <c>Extensions []string `json:"extensions"`</c>（没有 omitzero：nil 也写成 <c>[]</c>）。</summary>
    public string[] Extensions = [];

    /// <summary>Go: <c>Options json.Value `json:"options,omitempty"`</c>（原始 JSON 字节）。</summary>
    public byte[]? Options;

    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("package"));
        encoder.WriteToken(JsonToken.String(Package));
        encoder.WriteToken(JsonToken.String("extensions"));
        encoder.WriteToken(BeginArray);
        foreach (var extension in Extensions ?? [])
        {
            encoder.WriteToken(JsonToken.String(extension));
        }

        encoder.WriteToken(EndArray);

        // Go 的 `omitempty`：len(Options) == 0（nil 或空）都省略。
        if (Options is { Length: > 0 })
        {
            encoder.WriteToken(JsonToken.String("options"));
            encoder.WriteValue(Options);
        }

        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentmapper: expected an object for Definition");
        }

        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            switch (decoder.ReadName())
            {
                case "package":
                    Package = decoder.Decode<string>() ?? "";
                    break;
                case "extensions":
                    Extensions = decoder.PeekKind() == JsonKind.Null
                        ? []
                        : [.. decoder.DecodeNew<List<string>>()];
                    break;
                case "options":
                    Options = decoder.PeekKind() == JsonKind.Null ? null : decoder.ReadValue();
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
/// Go: <c>type Manifest struct</c>。mapper 包的 package.json 里读出的信息：name/version（组成 identity）、
/// 运行它的 argv、以及它声明依赖的编译选项。Go 里没有 json tag（由 tsoptions 从 package.json 逐字段填），
/// 也就不需要 JSON 映射。
/// </summary>
public sealed class Manifest
{
    public string Name = "";
    public string Version = "";
    public string[] Exec = [];
    public string[] CompilerOptions = [];
    public bool DynamicConfig;
}

/// <summary>
/// Go: <c>type Mapper struct { Definition; Manifest `json:"-"`; PackageDirectory, ContributionID string }</c>。
/// 已解析的 content mapper：tsconfig 的 Definition + 从 package.json 解析出的 Manifest + 作为工作目录的
/// 包目录。注意 <see cref="Dictionary{TKey,TValue}"/> 里用 Mapper 做键时按**引用**相等（Go 用 `*Mapper`
/// 指针身份），所以本类不提供值相等。
/// </summary>
public sealed class Mapper : Definition
{
    // ---- Manifest 的 5 个字段（Go 的内嵌提升）----

    public string Name = "";
    public string Version = "";
    public string[] Exec = [];
    public string[] CompilerOptions = [];
    public bool DynamicConfig;

    /// <summary>Go: <c>PackageDirectory</c>（package 解析返回的真实路径目录，作为 mapper 的工作目录）。</summary>
    public string PackageDirectory = "";

    /// <summary>Go: <c>ContributionID</c>（LSP 客户端扩展为推断项目提供的映射器）。</summary>
    public string ContributionID = "";

    public Mapper()
    {
    }

    /// <summary>C# 侧新增：Go 的复合字面量 <c>Mapper{Definition: def, Manifest: manifest}</c>。</summary>
    public Mapper(Definition definition, Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Package = definition.Package;
        Extensions = definition.Extensions;
        Options = definition.Options;
        SetManifest(manifest);
    }

    /// <summary>C# 侧新增：Go 的 <c>mapper.Manifest = manifest</c>（平铺后无法整体赋值）。</summary>
    public void SetManifest(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Name = manifest.Name;
        Version = manifest.Version;
        Exec = manifest.Exec;
        CompilerOptions = manifest.CompilerOptions;
        DynamicConfig = manifest.DynamicConfig;
    }

    /// <summary>Go: <c>func (m *Mapper) DiagnosticName() string</c>。</summary>
    public string DiagnosticName()
    {
        if (Name.Length != 0)
        {
            return Name;
        }

        return Package.Length != 0 ? Package : ContributionID;
    }

    /// <summary>Go: <c>func (m *Mapper) Identity() string</c>。</summary>
    public string Identity() => ContributionID.Length != 0 ? ContributionID + " (" + ManifestIdentity() + ")" : ManifestIdentity();

    /// <summary>Go: <c>func (m *Mapper) manifestIdentity() string</c>（未导出 → PascalCase）。</summary>
    private string ManifestIdentity()
    {
        if (Name.Length == 0)
        {
            return "";
        }

        return Version.Length == 0 ? Name : Name + "@" + Version;
    }

    /// <summary>
    /// Go: <c>func (m *Mapper) TransformIdentity(options *core.CompilerOptions) xxh3.Uint128</c>。把"除文件内容外
    /// 决定变换结果的一切"（mapper 身份 + 它声明依赖的编译选项值）折成一个指纹，用于缓存键。
    /// </summary>
    public UInt128 TransformIdentity(CompilerOptions? options)
    {
        var declared = MarshalDeclaredOptionsJson(options);
        var identity = Encoding.UTF8.GetBytes(Identity());
        var mapperOptions = Options ?? [];
        var buffer = new byte[identity.Length + 1 + mapperOptions.Length + 1 + declared.Length];
        var offset = 0;
        identity.CopyTo(buffer, offset);
        offset += identity.Length;
        buffer[offset++] = 0;
        mapperOptions.CopyTo(buffer, offset);
        offset += mapperOptions.Length;
        buffer[offset++] = 0;
        declared.CopyTo(buffer, offset);
        return Xxh3.Xxh3.Hash128(buffer);
    }

    /// <summary>
    /// Go: <c>func (m *Mapper) MarshalDeclaredOptions(options *core.CompilerOptions)
    /// (*collections.OrderedMap[string, json.Value], error)</c>。只 marshal 该 mapper 声明依赖的选项
    /// （manifest 的 <c>compilerOptions</c>），按声明顺序，未设置的跳过。
    /// 值是原始 JSON 字节——不要直接 `Json.Marshal` 这个 map（会按 base64 写）。
    /// </summary>
    public OrderedMap<string, byte[]> MarshalDeclaredOptions(CompilerOptions? options)
    {
        var result = new OrderedMap<string, byte[]>(CompilerOptions.Length);
        if (options is null || CompilerOptions.Length == 0)
        {
            return result;
        }

        var fields = ContentMapper._compilerOptionsByJsonName;
        foreach (var name in CompilerOptions)
        {
            if (!fields.TryGetValue(name, out var field) || field.IsZero(options))
            {
                continue;
            }

            result.Set(name, ContentMapper.EncodeOptionValue(field, options));
        }

        return result;
    }

    /// <summary>C# 侧新增：把 <see cref="MarshalDeclaredOptions"/> 的结果写成 JSON 文本（Go 的
    /// <c>json.Marshal(declared)</c>；值是原始 JSON，所以不能走 Json.Marshal）。</summary>
    public byte[] MarshalDeclaredOptionsJson(CompilerOptions? options)
    {
        var buffer = new MemoryStream();
        var encoder = new JsonEncoder(buffer);
        try
        {
            encoder.WriteToken(BeginObject);
            if (options is not null && CompilerOptions.Length != 0)
            {
                var fields = ContentMapper._compilerOptionsByJsonName;
                foreach (var name in CompilerOptions)
                {
                    if (!fields.TryGetValue(name, out var field) || field.IsZero(options))
                    {
                        continue;
                    }

                    encoder.WriteToken(JsonToken.String(name));
                    ContentMapper.WriteOptionValue(encoder, field, options);
                }
            }

            encoder.WriteToken(EndObject);
            encoder.Flush();
        }
        finally
        {
            encoder.Dispose();
        }

        return buffer.ToArray();
    }
}

/// <summary>Go 的 <c>var ErrProjectUnavailable</c>（调用方原样返回给上层）。</summary>
public sealed class ProjectUnavailableException() : Exception("content mapper project is unavailable");

public static partial class ContentMapper
{
    // Go: var supportedVirtualExtensions = collections.NewSetFromItems(".js", …)
    private static readonly Set<string> _supportedVirtualExtensions = NewSetFromItems<string>(
        ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts", ".json");

    /// <summary>Go: <c>func IsSupportedVirtualExtension(extension string) bool</c>。</summary>
    public static bool IsSupportedVirtualExtension(string extension) => _supportedVirtualExtensions.Has(extension);

    /// <summary>
    /// Go 的 <c>compilerOptionFields()</c>：json 名 → 字段（表来自 <c>TsOptions.CompilerOptionFields</c>，
    /// 只有带 json tag 的字段才在表里，与 Go 的 <c>tag.Get("json") != "" &amp;&amp; != "-"</c> 一致）。
    /// </summary>
    internal static readonly Dictionary<string, CompilerOptionField> _compilerOptionsByJsonName = BuildCompilerOptionsByJsonName();

    private static Dictionary<string, CompilerOptionField> BuildCompilerOptionsByJsonName()
    {
        var map = new Dictionary<string, CompilerOptionField>(StringComparer.Ordinal);
        foreach (var field in CompilerOptionFields)
        {
            if (field.JsonName.Length != 0)
            {
                map[field.JsonName] = field;
            }
        }

        return map;
    }

    /// <summary>
    /// Go 的 <c>json.Marshal(options)</c>（hostimpl.go 的 openProject 参数用的就是它）：按 struct 声明顺序、
    /// 逐字段 <c>omitzero</c>。C# 侧由 <c>TsOptions.CompilerOptionFields</c> 的有序表驱动。
    /// </summary>
    internal static byte[] MarshalCompilerOptions(CompilerOptions options)
    {
        var buffer = new MemoryStream();
        var encoder = new JsonEncoder(buffer);
        try
        {
            encoder.WriteToken(BeginObject);
            foreach (var field in CompilerOptionFields)
            {
                if (field.IsZero(options))
                {
                    continue;
                }

                encoder.WriteToken(JsonToken.String(field.JsonName));
                WriteOptionValue(encoder, field, options);
            }

            encoder.WriteToken(EndObject);
            encoder.Flush();
        }
        finally
        {
            encoder.Dispose();
        }

        return buffer.ToArray();
    }

    // Go 的 `json.Marshal(field.Interface())`：
    // - Tristate 有自己的 MarshalJSON（布尔 true/false，null 表示 Unknown）——C# 的 enum 不能实现接口，
    //   所以走 Core 的扩展方法（Core/Tristate.cs 文件头第 4 条明确要求调用点这么做）。
    // - 其余类型由 Json 包的静态分派处理（string/bool/整数/枚举按底层整数/数组/OrderedMap 的 MarshalJSONTo）。
    // - `PluginImport[]`：Core.PluginImport 在 C# 侧还没有 JSON 映射（Go 靠 reflect 的 `json:"name"`），
    //   这里就地写出，保持与 Go 逐字节一致；等 Core 侧补上 mapper 后可删掉这个分支（见报告）。
    internal static void WriteOptionValue(JsonEncoder encoder, CompilerOptionField field, CompilerOptions options)
    {
        if (field.GetTristate is not null)
        {
            encoder.WriteValue(field.GetTristate(options).MarshalJSON());
            return;
        }

        var value = field.Get(options);
        if (value is PluginImport[] plugins)
        {
            encoder.WriteToken(BeginArray);
            foreach (var plugin in plugins)
            {
                if (plugin is null)
                {
                    encoder.WriteToken(Null);
                    continue;
                }

                encoder.WriteToken(BeginObject);
                encoder.WriteToken(JsonToken.String("name"));
                encoder.WriteToken(JsonToken.String(plugin.Name));
                encoder.WriteToken(EndObject);
            }

            encoder.WriteToken(EndArray);
            return;
        }

        MarshalEncode(encoder, value);
    }

    /// <summary>Go 的 <c>json.Marshal(field.Interface())</c> 单值形态（<see cref="WriteOptionValue"/> 的字节版）。</summary>
    internal static byte[] EncodeOptionValue(CompilerOptionField field, CompilerOptions options)
    {
        var buffer = new MemoryStream();
        var encoder = new JsonEncoder(buffer);
        try
        {
            WriteOptionValue(encoder, field, options);
            encoder.Flush();
        }
        finally
        {
            encoder.Dispose();
        }

        return buffer.ToArray();
    }
}
