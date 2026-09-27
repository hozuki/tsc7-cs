// ported from: internal/lsp/lsproto/structcodec.go
//
// Go 侧是"一个反射驱动的对象解码器"（unmarshalStruct 按 struct tag 现场解析每个类型的字段表，缓存到
// structSpecCache）。本移植按 docs/json.md §2 的 AOT 约束把反射换成**每个类型一张显式字段表**：
// Lsp.Generated.*.cs 里每个可编解码的类型都有一个 `internal static readonly StructSpec Spec`，
// 表项（StructFieldSpec）在生成期就把 json 名、required、是否拒绝 null、以及"读一个值"的委托确定的写死。
// 因此本文件的职责与 Go 的 structcodec.go 完全一致，只是 specFor(reflect.Type) 变成了生成器产物：
//
//   * 对象必须是对象（errNotObject）；
//   * required 字段必须出现，否则 errMissing（名字按声明顺序，最多 64 个）；
//   * 指针/切片/map 字段默认拒绝 JSON null（lsp:"required"/"nullable" 之外的都会拒绝）；
//   * 未知字段跳过（SkipValue），不报错；
using TypeScript;

//   * 判别式联合：scanDiscriminatedStruct 先读到 discriminator 为止（之前的字段延迟），
//     unmarshalDiscriminatedArm 把延迟字段 + 剩余流解进具体臂，全部成功后才赋值。
//
// 与 Go 的偏离（逐条）：
// 1) 委托代替反射：StructFieldDecoder 是 `(object target, JsonDecoder dec)`，调用点先强转成具体类型
//    （生成的类型都是 class，所以没有装箱，见 docs/architecture.md §3.2 的 `*T` → `T?`）。
// 2) 重复成员名：Go 的 jsontext 解码器默认拒绝重复属性名；C# 的 Json.Decoder 不查（这是 Json 包既有行为，
//    本包沿用）。其余严格度与 Go 一致。
// 3) Go 的 json.Value（[]byte）在 C# 侧用 Decoder.ReadValue() 读原始字节。
using System.Text;
using TypeScript.Json;
using static TypeScript.Json.Json;
// `System.Text.Encoder/Decoder`（隐式 using）与 TypeScript.Json.Encoder/Decoder 同名 → CS0104，必须别名。
using JsonEncoder = TypeScript.Json.Encoder;
using JsonDecoder = TypeScript.Json.Decoder;

namespace TypeScript.LsProto;

/// <summary>Go 的 <c>structDecoder.field(name, kind, decode)</c> 里的 decode 回调（显式字段表版）。</summary>
internal delegate void StructFieldDecoder(object target, JsonDecoder decoder);

/// <summary>Go 的 marshal 路径里"写一个字段"的回调（名字与值都由生成器写死）。</summary>
internal delegate void StructFieldEncoder(object target, JsonEncoder encoder);

/// <summary>Go: <c>type structFieldSpec struct</c>。</summary>
internal sealed class StructFieldSpec(string name, bool required, bool rejectNull, StructFieldDecoder decode)
{
    /// <summary>JSON 成员名（Go 的 <c>json:"…"</c> 去掉选项后的部分）。</summary>
    internal readonly string Name = name;

    /// <summary>Go 的 <c>lsp:"required"</c>。</summary>
    internal readonly bool Required = required;

    /// <summary>Go 的 <c>rejectNull</c>：指针/切片/map 且未标 nullable → 显式 null 报错。</summary>
    internal readonly bool RejectNull = rejectNull;

    /// <summary>读出这个字段的值并写进 target。</summary>
    internal readonly StructFieldDecoder Decode = decode;

    /// <summary>Go 的 <c>requiredID</c>：required 字段的位下标（由 StructSpec 填），非 required 为 -1。</summary>
    internal int RequiredId = -1;
}

/// <summary>Go: <c>type structSpec struct</c>（Go 里由 reflect 现场构建并缓存，这里由生成器写死）。</summary>
internal sealed class StructSpec
{
    internal StructSpec(StructFieldSpec[] fields, StructFieldEncoder[] encoders)
    {
        Fields = fields;
        Encoders = encoders;
        ByName = new Dictionary<string, StructFieldSpec>(fields.Length, StringComparer.Ordinal);
        var requiredNames = new List<string>();
        var mask = 0UL;
        foreach (var field in fields)
        {
            if (field.Required)
            {
                if (requiredNames.Count >= 64)
                {
                    throw new DebugFailureException("structcodec: more than 64 required fields are supported");
                }

                field.RequiredId = requiredNames.Count;
                mask |= 1UL << field.RequiredId;
                requiredNames.Add(field.Name);
            }

            ByName[field.Name] = field;
        }

        RequiredNames = [.. requiredNames];
        RequiredMask = mask;
    }

    /// <summary>按声明顺序的字段表（编码顺序就是它）。</summary>
    internal readonly StructFieldSpec[] Fields;

    /// <summary>编码器：顺序与 Fields 一致（Go 的 json 编码器按声明顺序写字段）。</summary>
    internal readonly StructFieldEncoder[] Encoders;

    /// <summary>Go 的 <c>byName</c>。</summary>
    internal readonly Dictionary<string, StructFieldSpec> ByName;

    /// <summary>Go 的 <c>requiredNames</c>（按声明顺序，errMissing 用它拼顺序）。</summary>
    internal readonly string[] RequiredNames;

    /// <summary>Go 的 <c>requiredMask</c>。</summary>
    internal readonly ulong RequiredMask;

    /// <summary>Go: <c>structSpec.byName[name]</c>（两段式）。</summary>
    internal bool TryGetField(string name, out StructFieldSpec field) => ByName.TryGetValue(name, out field!);
}

/// <summary>Go 的 <c>deferredStructField</c>：判别式出现之前读到的字段（名字 + 原始值）。</summary>
internal sealed class DeferredStructField(string name, byte[] value)
{
    internal readonly string Name = name;
    internal readonly byte[] Value = value;
}

/// <summary>Go 的 <c>discriminatedStructDecoder</c>。</summary>
internal sealed class DiscriminatedStructDecoder(JsonDecoder dec, string typeName, string discriminator)
{
    /// <summary>Go 的 <c>dec</c>。</summary>
    internal readonly JsonDecoder Dec = dec;

    /// <summary>Go 的 <c>typeName</c>（只用于错误消息）。</summary>
    internal readonly string TypeName = typeName;

    /// <summary>Go 的 <c>discriminator</c>（如 "kind"）。</summary>
    internal readonly string Discriminator = discriminator;

    /// <summary>Go 的 <c>discriminatorValue json.Value</c>。</summary>
    internal byte[]? DiscriminatorValue;

    /// <summary>Go 的 <c>hasDiscriminator</c>。</summary>
    internal bool HasDiscriminator;

    /// <summary>Go 的 <c>deferred</c>。</summary>
    internal readonly List<DeferredStructField> Deferred = [];

    /// <summary>Go 的 <c>closed</c>：对象是否已经整体读完（判别式就是最后一个成员）。</summary>
    internal bool Closed;

    /// <summary>Go 的 <c>string(state.discriminatorValue)</c>：判别式的原始 JSON 文本。</summary>
    internal string DiscriminatorText => Encoding.UTF8.GetString(DiscriminatorValue ?? []);

    /// <summary>Go: <c>func (d discriminatedStructDecoder) invalidDiscriminator() error</c>。</summary>
    internal JsonException InvalidDiscriminator()
    {
        if (!HasDiscriminator)
        {
            return new JsonException($"invalid {TypeName}: missing discriminator \"{Discriminator}\"");
        }

        return new JsonException($"invalid {TypeName} discriminator \"{Discriminator}\": {DiscriminatorText}");
    }
}

/// <summary>Go 的 <c>struct{}</c>：LSP 里的空对象（如 BooleanOrEmptyObject 的 EmptyObject 臂）。</summary>
public sealed class EmptyObject : IJsonMarshaler, IJsonUnmarshaler
{
    /// <summary>Go: 空结构体的 json.Marshal → <c>{}</c>。</summary>
    public void MarshalJSONTo(JsonEncoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(EndObject);
    }

    /// <summary>Go: 把对象解进 <c>struct{}</c>（成员全部忽略，非对象报错）。</summary>
    public void UnmarshalJSONFrom(JsonDecoder decoder)
    {
        if (decoder.PeekKind() != JsonKind.BeginObject)
        {
            throw LsProto.ErrNotObject(decoder.PeekKind());
        }

        decoder.ReadToken();
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            decoder.ReadName();
            decoder.SkipValue();
        }

        decoder.ReadToken();
    }
}

public static partial class LsProto
{
    // ---- Go 的 structcodec.go 包级函数（reflect 部分已由生成器的字段表取代）----------------

    /// <summary>Go: <c>func unmarshalStruct(v any, dec *json.Decoder) error</c>。</summary>
    internal static void UnmarshalStruct(object target, StructSpec spec, JsonDecoder dec)
    {
        if (dec.PeekKind() != JsonKind.BeginObject)
        {
            throw ErrNotObject(dec.PeekKind());
        }

        dec.ReadToken();

        var seen = 0UL;
        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (!spec.TryGetField(name, out var field))
            {
                dec.SkipValue();
                continue;
            }

            if (field.Required)
            {
                seen |= 1UL << field.RequiredId;
            }

            if (field.RejectNull && dec.PeekKind() == JsonKind.Null)
            {
                throw ErrNull(name);
            }

            field.Decode(target, dec);
        }

        dec.ReadToken();
        CheckRequired(spec, seen);
    }

    /// <summary>Go: <c>func (d *structDecoder) finish() error</c>（required 字段缺失检查）。</summary>
    internal static void CheckRequired(StructSpec spec, ulong seen)
    {
        var missing = spec.RequiredMask & ~seen;
        if (missing == 0)
        {
            return;
        }

        var missingProps = new List<string>();
        for (var id = 0; id < spec.RequiredNames.Length; id++)
        {
            if ((missing & (1UL << id)) != 0)
            {
                missingProps.Add(spec.RequiredNames[id]);
            }
        }

        throw ErrMissing(missingProps);
    }

    /// <summary>Go: <c>func (d *structDecoder) field(name, kind, decode)</c>（从已读出的原始值解一个字段）。</summary>
    internal static void DecodeFieldFromValue(object target, StructSpec spec, string name, byte[] value, ref ulong seen)
    {
        if (!spec.TryGetField(name, out var field))
        {
            return;
        }

        if (field.Required)
        {
            seen |= 1UL << field.RequiredId;
        }

        var dec = new JsonDecoder(value);
        if (field.RejectNull && dec.PeekKind() == JsonKind.Null)
        {
            throw ErrNull(name);
        }

        field.Decode(target, dec);
    }

    /// <summary>把判别式之后剩余的流式成员解进目标（Go: unmarshalDiscriminatedArm 的 <c>!state.closed</c> 分支）。</summary>
    internal static void DrainRemainingFields(object target, StructSpec spec, JsonDecoder dec, ref ulong seen)
    {
        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (!spec.TryGetField(name, out var field))
            {
                dec.SkipValue();
                continue;
            }

            if (field.Required)
            {
                seen |= 1UL << field.RequiredId;
            }

            if (field.RejectNull && dec.PeekKind() == JsonKind.Null)
            {
                throw ErrNull(name);
            }

            field.Decode(target, dec);
        }

        dec.ReadToken();
    }

    /// <summary>Go: <c>func scanDiscriminatedStruct(dec, typeName, discriminator) (discriminatedStructDecoder, error)</c>。</summary>
    internal static DiscriminatedStructDecoder ScanDiscriminatedStruct(JsonDecoder dec, string typeName, string discriminator)
    {
        if (dec.PeekKind() != JsonKind.BeginObject)
        {
            throw ErrNotObject(dec.PeekKind());
        }

        dec.ReadToken();

        var state = new DiscriminatedStructDecoder(dec, typeName, discriminator);
        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (name == discriminator)
            {
                state.DiscriminatorValue = dec.ReadValue();
                state.HasDiscriminator = true;
                return state;
            }

            state.Deferred.Add(new DeferredStructField(name, dec.ReadValue()));
        }

        dec.ReadToken();
        state.Closed = true;
        return state;
    }

    /// <summary>Go: <c>func unmarshalDiscriminatedArm[T any](state, out **T) error</c>（全部成功后才返回臂）。</summary>
    internal static T UnmarshalDiscriminatedArm<T>(DiscriminatedStructDecoder state, StructSpec spec)
        where T : class, new()
    {
        var target = new T();
        var seen = 0UL;
        if (state.HasDiscriminator)
        {
            DecodeFieldFromValue(target, spec, state.Discriminator, state.DiscriminatorValue ?? [], ref seen);
        }

        foreach (var field in state.Deferred)
        {
            DecodeFieldFromValue(target, spec, field.Name, field.Value, ref seen);
        }

        if (!state.Closed)
        {
            DrainRemainingFields(target, spec, state.Dec, ref seen);
        }

        CheckRequired(spec, seen);
        return target;
    }

    /// <summary>Go: <c>func marshalUnion(v any, enc, name, nullable) error</c> 的写侧驱动（联合体的字段表就是它的 Spec）。</summary>
    internal static void MarshalStruct(object target, StructSpec spec, JsonEncoder enc)
    {
        enc.WriteToken(BeginObject);
        foreach (var encode in spec.Encoders)
        {
            encode(target, enc);
        }

        enc.WriteToken(EndObject);
    }

    // ---- 生成器用的解码小工具（Go 侧是 json.UnmarshalDecode/json.Unmarshal 的等价物）--------

    /// <summary>解一个可为 null 的值：null → default（Go 的 <c>json.UnmarshalDecode(dec, &amp;*T)</c> 收到 null 时保持 nil）。</summary>
    internal static T? DecodeNullable<T>(JsonDecoder dec, Func<JsonDecoder, T> readItem)
    {
        if (dec.PeekKind() == JsonKind.Null)
        {
            dec.ReadToken();
            return default;
        }

        return readItem(dec);
    }

    /// <summary>解一个切片（Go 的 <c>[]T</c>）：null → null（nil 切片），否则读整个数组。</summary>
    internal static T[]? DecodeArray<T>(JsonDecoder dec, Func<JsonDecoder, T> readItem)
    {
        if (dec.PeekKind() == JsonKind.Null)
        {
            dec.ReadToken();
            return null;
        }

        if (dec.PeekKind() != JsonKind.BeginArray)
        {
            throw new JsonException($"json: expected array, got {(char)dec.PeekKind()}");
        }

        dec.ReadToken();
        var items = new List<T>();
        while (dec.PeekKind() != JsonKind.EndArray)
        {
            items.Add(readItem(dec));
        }

        dec.ReadToken();
        return items.ToArray();
    }

    /// <summary>解一个定长数组（Go 的 <c>[N]T</c>）：元素个数必须恰好是 N（与 Go 的 json 一致）。</summary>
    internal static T[] DecodeFixedArray<T>(JsonDecoder dec, int length, Func<JsonDecoder, T> readItem)
    {
        if (dec.PeekKind() != JsonKind.BeginArray)
        {
            throw new JsonException($"json: expected array, got {(char)dec.PeekKind()}");
        }

        dec.ReadToken();
        var items = new T[length];
        var index = 0;
        while (dec.PeekKind() != JsonKind.EndArray)
        {
            if (index >= length)
            {
                throw new JsonException($"json: too many elements for [{length}]T");
            }

            items[index++] = readItem(dec);
        }

        dec.ReadToken();
        if (index != length)
        {
            throw new JsonException($"json: too few elements for [{length}]T ({index})");
        }

        return items;
    }

    /// <summary>Go 的 <c>if err := json.Unmarshal(data, &amp;v); err == nil</c>（联合体的"逐个试"解码）。</summary>
    internal static bool TryUnmarshalInto<T>(byte[] data, T target)
        where T : IJsonUnmarshaler
    {
        try
        {
            target.UnmarshalJSONFrom(new JsonDecoder(data));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Go 的 <c>if err := json.Unmarshal(data, &amp;v); err == nil</c>，其中 v 是切片（联合体的分支逐个试）。</summary>
    internal static bool TryUnmarshalArray<T>(byte[] data, Func<JsonDecoder, T> readItem, out T[]? value)
    {
        try
        {
            value = DecodeArray(new JsonDecoder(data), readItem);
            return true;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Go 的 <c>if err := json.Unmarshal(data, &amp;v); err == nil</c>，其中 v 是 <c>any</c>。</summary>
    internal static bool TryUnmarshalAny(byte[] data, out object? value)
    {
        try
        {
            value = Unmarshal<object?>(data);
            return true;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
    }
}
