// ported from: internal/packagejson/jsonvalue.go
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
//
// 1) **JSONValue 必须是 class**：Go 的 JSONValue 是 struct，但 `UnmarshalJSONFrom` 是**指针接收者**（要就地写回
//    解码结果），`AsObject/AsArray/AsString` 是值接收者。C# 里 struct + 方法无法"就地写回调用方的槽位"，
//    所以落成 class（引用语义 = Go 的 `*JSONValue`）；`OrderedMap[string, JSONValue]` 里存的因此是引用，
//    Go 取出来的是**副本** —— 调用点不依赖这个差别（TSGO 的调用点只读不写）。
//    另一个后果：class 不能被 `sealed`（`ExportsOrImports` 要继承它，见 exportsorimports.go 的内嵌），
//    所以 `JSONValue` 是**可继承**的 public class。
//
// 2) `Value any` → `object?`：解码后的形状与 docs/json.md §2 一致 —— 对象 → `OrderedMap<string, JSONValue>`、
//    数组 → `JSONValue[]`（Go 的 `[]JSONValue`）、数字 → `double`（Go 的 `float64`）、字符串 → `string`、
//    布尔 → `bool`、null → `null`。
//
// 3) `String()` stringer → `JSONValueTypeExtensions.String(this JSONValueType)`（docs §3.3：C# 的 enum 不能覆写
//    `ToString()`，stringer 落成同命名空间里的扩展方法；调用点仍写 `t.String()`）。
//
// 4) `unmarshalJSONValueFrom[T]`（泛型私有方法）照搬成 C# 的 internal 泛型方法（`where T : JSONValue, new()`）：
//    Go 靠 reflect 给 `[]T` / `OrderedMap[string,T]` 造元素，C# 无反射（docs §3.6），元素改用 `new T()` +
//    `dec.DecodeInto(element)` 显式创建 —— 这正是 Go 的 `json.UnmarshalDecode(dec, &element)` 的语义
//    （元素类型实现了 UnmarshalerFrom，就由它自己解）。internal 而不是 private：`ExportsOrImports`
//    在 Go 里是**同包**调用（`e.JSONValue.unmarshalJSONValueFrom[...]`），C# 的 private 到不了派生类。
//
// 5) `panic(fmt.Sprintf(...))` → `throw new DebugFailureException(...)`（与 Ast/Node.cs 同款；
//    Go 的 `%v` 打在实现了 Stringer 的 JSONValueType 上就是 `Type.String()`）。
//
// 6) Go 的 `dec.PeekKind()` 返回 `jsontext.Kind`（byte，`'n'`/`'"'`/…）；C# 是 `JsonKind` 枚举（数值钉在 ASCII）。
//
// 7) 对象/数组元素的重复键：Go 走 `OrderedMap.UnmarshalerFrom`（内部 `json.UnmarshalDecode`），这里用同样
//    "后者覆盖前者" 的 `Set`（TSGO 的 package.json 解码在 Parse 里显式开了 AllowDuplicateNames(true)）。
using TypeScript.Collections;
using TypeScript.Json;

namespace TypeScript.PackageJson;

/// <summary>
/// Go: <c>type JSONValueType int8</c>（数值 0..6 与 TS/Go 保持一致，见 docs/architecture.md §3.3）。
/// 成员名去掉 Go 常量的类型前缀：<c>JSONValueTypeNotPresent</c> → <c>NotPresent</c>。
/// </summary>
public enum JSONValueType : sbyte
{
    NotPresent = 0,
    Null = 1,
    String = 2,
    Number = 3,
    Boolean = 4,
    Array = 5,
    Object = 6,
}

/// <summary>
/// Go 的 stringer <c>func (t JSONValueType) String() string</c>（docs §3.3 的落法：扩展方法）。
/// 合法值返回 Go 的文本；`NotPresent`（Go 的 default 分支）返回 <c>unknown(0)</c>。
/// </summary>
public static class JSONValueTypeExtensions
{
    public static string String(this JSONValueType value)
    {
        switch (value)
        {
            case JSONValueType.Null:
                return "null";
            case JSONValueType.String:
                return "string";
            case JSONValueType.Number:
                return "number";
            case JSONValueType.Boolean:
                return "boolean";
            case JSONValueType.Array:
                return "array";
            case JSONValueType.Object:
                return "object";
            default:
                return $"unknown({(sbyte)value})";
        }
    }
}

/// <summary>
/// Go: <c>type JSONValue struct { Type JSONValueType; Value any }</c>（package.json 里出现的任意 JSON 值的形状
/// 化表示）。见文件头第 1 条：Go 是 struct + 指针接收者，C# 是 class（且不可 sealed，ExportsOrImports 继承它）。
/// </summary>
public class JSONValue : IJsonUnmarshaler
{
    /// <summary>Go 的导出字段 <c>Type JSONValueType</c>。</summary>
    public JSONValueType Type;

    /// <summary>Go 的导出字段 <c>Value any</c>：形状见文件头第 2 条。</summary>
    public object? Value;

    public bool IsPresent()
    {
        return Type != JSONValueType.NotPresent;
    }

    public bool IsFalsy()
    {
        switch (Type)
        {
            case JSONValueType.NotPresent:
            case JSONValueType.Null:
                return true;
            case JSONValueType.String:
                // Go: v.Value == ""（any 与无类型常量比较 → 动态类型必须是 string 且为空）
                return (string?)Value == "";
            case JSONValueType.Number:
                // Go: v.Value == 0（float64 零值）
                return (double)Value! == 0;
            case JSONValueType.Boolean:
                // Go: !v.Value.(bool)
                return !(bool)Value!;
            default:
                return false;
        }
    }

    public OrderedMap<string, JSONValue>? AsObject()
    {
        if (Type != JSONValueType.Object)
        {
            // Go: panic(fmt.Sprintf("expected object, got %v", v.Type))
            throw new DebugFailureException($"expected object, got {Type.String()}");
        }

        return (OrderedMap<string, JSONValue>?)Value;
    }

    public JSONValue[] AsArray()
    {
        if (Type != JSONValueType.Array)
        {
            throw new DebugFailureException($"expected array, got {Type.String()}");
        }

        return (JSONValue[])Value!;
    }

    public string AsString()
    {
        if (Type != JSONValueType.String)
        {
            throw new DebugFailureException($"expected string, got {Type.String()}");
        }

        return (string)Value!;
    }

    // Go: var _ json.UnmarshalerFrom = (*JSONValue)(nil)
    /// <summary>Go 的 <c>UnmarshalJSONFrom</c>：按下一个 token 的种类把值就地解进本对象。</summary>
    public void UnmarshalJSONFrom(Decoder dec)
    {
        UnmarshalJSONValueFrom<JSONValue>(dec);
    }

    // Go: func (v *JSONValue) unmarshalJSONValueFrom[T any](dec *json.Decoder) error —— 元素类型参数化：
    // JSONValue 自己用 T = JSONValue，ExportsOrImports 用 T = ExportsOrImports（见文件头第 4 条）。
    internal void UnmarshalJSONValueFrom<T>(Decoder dec)
        where T : JSONValue, new()
    {
        switch (dec.PeekKind())
        {
            case JsonKind.Null:
                dec.ReadToken();
                Value = null;
                Type = JSONValueType.Null;
                return;
            case JsonKind.String:
                Type = JSONValueType.String;
                // Go: json.UnmarshalDecode(dec, &v.Value)（Value 是 any → 解成 string）
                Value = dec.Decode<object?>();
                return;
            case JsonKind.BeginArray:
                dec.ReadToken();
                var elements = new List<T>();
                while (dec.PeekKind() != JsonKind.EndArray)
                {
                    // Go: var element T; json.UnmarshalDecode(dec, &element)（元素的解码器决定怎么解）
                    var element = new T();
                    dec.DecodeInto(element);
                    elements.Add(element);
                }

                dec.ReadToken();
                Type = JSONValueType.Array;
                Value = elements.ToArray();
                return;
            case JsonKind.BeginObject:
                var @object = new OrderedMap<string, T>();
                dec.ReadToken();
                while (dec.PeekKind() != JsonKind.EndObject)
                {
                    // Go 走 OrderedMap 的 UnmarshalerFrom：成员名是一个字符串 token，值紧跟其后。
                    var key = dec.ReadName();
                    var element = new T();
                    dec.DecodeInto(element);
                    @object.Set(key, element);
                }

                dec.ReadToken();
                Type = JSONValueType.Object;
                Value = @object;
                return;
            case JsonKind.True:
            case JsonKind.False:
                Type = JSONValueType.Boolean;
                Value = dec.Decode<object?>();
                return;
            default:
                Type = JSONValueType.Number;
                Value = dec.Decode<object?>();
                return;
        }
    }
}
