// ported from: internal/packagejson/expected.go
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
//
// 1) **Expected<T> 必须是 class**：Go 用 `*Expected[T]`（`UnmarshalJSON` 是指针接收者，要就地改写），
//    并且它是 group 结构体里的**可寻址字段**。C# 里 struct 无法"就地改写调用方的槽位 + 支持 GetValue 返回副本"，
//    所以落成 class（引用语义 = Go 的 `*Expected[T]`）。各 group 的 `Expected<...>` 字段都显式 `= new()`
//    （Go 是零值结构体字段，C# 里 null 会 NRE）。
//
// 2) `GetValue() (value T, ok bool)` → `bool TryGetValue(out T value)`（docs/architecture.md §3.2）。
//
// 3) `ExpectedJSONType()` 在 Go 用 `reflect.TypeFor[T]().Kind()` 判 T 的种类；AOT 禁反射（docs §3.6），
//    C# 改成 `typeof(T)` 的元数据分派（`IsArray`/`IDictionary`/基本类型比较）。映射与 Go 的 Kind 表逐条对齐：
//    String→"string"、Bool→"boolean"、Slice/Array→"array"、Map→"object"、整数族→"number"、其余→"unknown"
//    （注意 Go 的 float32/float64 **不在**表里，所以 double 也落 "unknown"；这里同样不把 double 算作 number）。
//
// 4) `UnmarshalJSON(data []byte)` 是 Go 的 **v1 风格**自定义解码器：拿到的是**整个字段的原始字节**、永远返回 nil
//    （失败只是"没解出来"，Valid 保持 false）。C# 的 `IJsonUnmarshaler` 是 decoder 语义，所以实现里先
//    `dec.ReadValue()` 把整个值取出来（**无论成败都消费掉**，与 Go 的字节语义一致），再用一个临时 Decoder 解。
//
// 5) `json.Unmarshal(data, &e.Value)` 需要"创建 T 的实例"（struct/切片/map）；C# 无反射，改成 `typeof(T)`
//    的显式分派：`Dictionary<string,string>`（Go 的 `map[string]string`）与 `ContentMapperFields`/`TypeScriptFields`
//    走 `DecodeNew<T>()`（= Go 的 reflect.New），`string[]`（Go 的 `[]string`）手写读取。其余类型走 `Decode<T>()`
//    （标量 / `object?` 形状；对需要实例的形状会抛 JsonException → Valid 保持 false，与 Go 的解码失败一致）。
//
// 6) **Value 的零值**：Go 的零值是 T 的零值（`Expected[string]` 失败时是 `""`），C# 的 `default(T)` 对引用类型是
//    `null` —— 解码失败或字段缺失时 `Value` 是 null 而不是 ""。测试按 C# 语义断言（见 packagejson_test）。
//
// 7) Go 的 `actualJSONType` 未导出但**同包可见**（`Parse`/测试都看得到）→ C# `internal string _actualJSONType`
//    （docs §2：需要被同程序集其它类访问的未导出成员用 internal + `_camelCase`）。
using System.Collections;
using TypeScript.Json;

namespace TypeScript.PackageJson;

/// <summary>
/// Go: <c>type Expected[T any] struct { actualJSONType string; Null bool; Valid bool; Value T }</c>。
/// 一个"可能不存在 / 类型可能不匹配"的 package.json 字段：`Null` 区分"值是 JSON null"，`Valid` 表示"按期望类型解出来了"。
/// </summary>
public sealed class Expected<T> : TypeValidatedField, IJsonUnmarshaler
{
    internal string _actualJSONType = "";

    /// <summary>Go 的导出字段 <c>Null</c>：JSON 值是 `null`。</summary>
    public bool Null;

    /// <summary>Go 的导出字段 <c>Valid</c>：值按 T 解出来了。</summary>
    public bool Valid;

    /// <summary>Go 的导出字段 <c>Value</c>（零值语义见文件头第 6 条）。</summary>
    public T Value = default!;

    // Go: func (e *Expected[T]) UnmarshalJSON(data []byte) error
    public void UnmarshalJSONFrom(Decoder dec)
    {
        var data = dec.ReadValue();
        if (IsNullLiteral(data))
        {
            // Go: *e = Expected[T]{Null: true, actualJSONType: "null"}（Valid/Value 归零）
            Null = true;
            Valid = false;
            Value = default!;
            _actualJSONType = "null";
            return;
        }

        // Go 用 data[0] 判实际 JSON 类型（ReadValue 已去掉前导空白，字符串值带引号）。
        switch (data[0])
        {
            case (byte)'"':
                _actualJSONType = "string";
                break;
            case (byte)'t':
            case (byte)'f':
                _actualJSONType = "boolean";
                break;
            case (byte)'[':
                _actualJSONType = "array";
                break;
            case (byte)'{':
                _actualJSONType = "object";
                break;
            default:
                _actualJSONType = "number";
                break;
        }

        // Go: if json.Unmarshal(data, &e.Value) == nil { e.Valid = true }
        if (TryUnmarshal(data, out var value))
        {
            Value = value;
            Valid = true;
        }
    }

    public bool IsPresent()
    {
        return _actualJSONType != "";
    }

    public bool IsValid()
    {
        return Valid;
    }

    /// <summary>Go: <c>GetValue() (value T, ok bool)</c> —— ok 即 <see cref="Valid"/>。</summary>
    public bool TryGetValue(out T value)
    {
        value = Value;
        return Valid;
    }

    public string ExpectedJSONType()
    {
        return ExpectedTypeName();
    }

    public string ActualJSONType()
    {
        return _actualJSONType;
    }

    internal static string ExpectedTypeName()
    {
        // Go: switch reflect.TypeFor[T]().Kind()（见文件头第 3 条）
        var type = typeof(T);
        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type.IsArray)
        {
            return "array";
        }

        if (typeof(IDictionary).IsAssignableFrom(type)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)))
        {
            return "object";
        }

        if (type == typeof(sbyte) || type == typeof(byte)
            || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong))
        {
            return "number";
        }

        return "unknown";
    }

    private static bool IsNullLiteral(byte[] data)
    {
        // Go: string(data) == "null"
        return data.Length == 4
            && data[0] == (byte)'n' && data[1] == (byte)'u' && data[2] == (byte)'l' && data[3] == (byte)'l';
    }

    private static bool TryUnmarshal(byte[] data, out T value)
    {
        var dec = new Decoder(data);
        try
        {
            value = DecodeValue(dec);
            return true;
        }
        catch (JsonException)
        {
            // Go: json.Unmarshal 返回非 nil error → e.Valid 保持 false
            value = default!;
            return false;
        }
    }

    private static T DecodeValue(Decoder dec)
    {
        // 需要"创建实例"的形状：C# 无反射（docs §3.6），按 T 显式分派（见文件头第 5 条）。
        if (typeof(T) == typeof(string[]))
        {
            return (T)(object)DecodeStringArray(dec);
        }

        if (typeof(T) == typeof(Dictionary<string, string>))
        {
            return (T)(object)dec.DecodeNew<Dictionary<string, string>>();
        }

        if (typeof(T) == typeof(ContentMapperFields))
        {
            return (T)(object)dec.DecodeNew<ContentMapperFields>();
        }

        if (typeof(T) == typeof(TypeScriptFields))
        {
            return (T)(object)dec.DecodeNew<TypeScriptFields>();
        }

        return dec.Decode<T>()!;
    }

    // Go 的 `[]string` 由 encoding/json 直接造切片；C# 的数组只能手写（`new()` 约束不接受数组类型）。
    private static string[] DecodeStringArray(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginArray)
        {
            throw new JsonException("json: cannot unmarshal non-array into string[]");
        }

        var list = new List<string>();
        while (dec.PeekKind() != JsonKind.EndArray)
        {
            // Go 解 null 元素到 string 得到零值 ""。
            list.Add(dec.Decode<string>() ?? "");
        }

        dec.ReadToken();
        return list.ToArray();
    }
}

// Go 的包级函数 `ExpectedOf`（expected.go:69-75）按用户钉死的落法成为 `PackageJson` 上的 public static 成员
// （理由见 PackageJson.cs 文件头第 4 条：包名与类型名同名，静态类无处安放；与 Scanner 的先例一致）。
public sealed partial class PackageJson
{
    /// <summary>
    /// Go: <c>func ExpectedOf[T any](value T) Expected[T]</c>：把已知的值包成"已出现、且合法"的字段
    /// （`actualJSONType` 取 T 的期望类型名）。测试与调用点用它构造期望值。
    /// </summary>
    public static Expected<T> ExpectedOf<T>(T value)
    {
        return new Expected<T> { Value = value, Valid = true, _actualJSONType = Expected<T>.ExpectedTypeName() };
    }
}
