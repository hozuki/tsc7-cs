// ported from: internal/json/json.go（json.Unmarshal / UnmarshalRead / UnmarshalDecode 的读侧）
// + Go encoding/json/v2 的解码语义：默认拒绝重复属性名（jsontext.AllowDuplicateNames(false)）、
//   any → Dictionary<string, object?> / List<object?> / double / string / bool / null、枚举按底层整数、
//   []byte 走 base64、IJsonUnmarshaler 优先。
//
// AOT：全程静态分派（`is` 模式匹配 + typeof(T) 的元数据），不用反射、不用泛型实例化（docs/json.md §2）。
// 因此“创建实例”只能由两处提供：`UnmarshalNew<T>() where T : new()`（C# 的 new T()）或调用方给出的实例
// （UnmarshalInto / Decoder.DecodeInto）。需要创建才解得动的 JSON 形状会抛带类型名的 JsonException。
using System.Collections;
using System.Globalization;
using System.Numerics;

namespace TypeScript.Json;

internal static class JsonDecode
{
    // 解出一个值。
    //   target == null 或 typeof(object) → 按 JSON 形状解：对象 → Dictionary<string, object?>、
    //     数组 → List<object?>、数字 → double、其余 string/bool/null。
    //   target == 标量/string/bool/char/byte[]/枚举 → 按类型解。
    //   其它类型（需要实例的对象/集合）→ 抛异常，提示用 UnmarshalNew / UnmarshalInto。
    internal static object? DecodeValue(Decoder decoder, JsonSettings settings, Type? target)
    {
        // Go 的指针 → C# 的 Nullable<T>：null → null，否则按底层类型解。
        if (target is not null && Nullable.GetUnderlyingType(target) is { } underlying)
        {
            if (decoder.PeekKind() == JsonKind.Null)
            {
                decoder.Read();
                return null;
            }

            return DecodeValue(decoder, settings, underlying);
        }

        var kind = decoder.PeekKind();

        switch (kind)
        {
            case JsonKind.Null:
                decoder.Read();
                return null;

            case JsonKind.String:
            {
                decoder.Read();
                var text = decoder.CurrentString;
                if (target == typeof(byte[]))
                {
                    try
                    {
                        return Convert.FromBase64String(text);
                    }
                    catch (FormatException e)
                    {
                        throw new JsonException("json: invalid base64 string for []byte", e);
                    }
                }

                if (target == typeof(char))
                {
                    if (text.Length != 1)
                    {
                        throw new JsonException($"json: cannot unmarshal string of length {text.Length} into char");
                    }

                    return text[0];
                }

                if (target is null || target == typeof(object) || target == typeof(string))
                {
                    return text;
                }

                throw new JsonException($"json: cannot unmarshal string into {target}");
            }

            case JsonKind.Number:
                decoder.Read();
                return ParseNumber(decoder.CurrentNumber, target);

            case JsonKind.True:
            case JsonKind.False:
                decoder.Read();
                if (target is null || target == typeof(object) || target == typeof(bool))
                {
                    return kind == JsonKind.True;
                }

                throw new JsonException($"json: cannot unmarshal boolean into {target}");

            case JsonKind.BeginObject:
                if (target is not null && target != typeof(object))
                {
                    throw NeedsInstance(target, "object");
                }

                return DecodeShapeObject(decoder, settings);

            case JsonKind.BeginArray:
                if (target is not null && target != typeof(object))
                {
                    throw NeedsInstance(target, "array");
                }

                return DecodeShapeArray(decoder, settings);

            default:
                throw new JsonException("json: unexpected end of input");
        }
    }

    // C# 侧新增（Go 侧是 reflect.New）：用 `new T()` 创建实例再解进去。
    // 适合实现了 IJsonUnmarshaler 的对象、List<T>、Dictionary<K,V>；标量/string/枚举走解析路径。
    internal static T DecodeNew<T>(Decoder decoder, JsonSettings settings)
        where T : new()
    {
        var target = new T();
        if (target is not null and (IJsonUnmarshaler or IDictionary or IList))
        {
            DecodeInto(decoder, target, settings);
            return target;
        }

        // 标量/string/枚举：new T() 只是占位，真正的值由解析结果给出。
        // DecodeValue 对 JSON null 返回 null（引用类型下合法），这里的 ! 只用于消掉可空警告。
        return (T)DecodeValue(decoder, settings, typeof(T))!;
    }

    // Go 的 `json.Unmarshal(data, &v)`：把 JSON 解进已有实例（IJsonUnmarshaler / IDictionary / IList）。
    internal static void DecodeInto(Decoder decoder, object target, JsonSettings settings)
    {
        switch (target)
        {
            case IJsonUnmarshaler unmarshaler:
                unmarshaler.UnmarshalJSONFrom(decoder);
                return;
            case IDictionary dictionary:
                DecodeIntoDictionary(decoder, dictionary, settings);
                return;
            case IList list:
                DecodeIntoList(decoder, list, settings);
                return;
            default:
                throw new JsonException($"json: type {target.GetType()} must implement IJsonUnmarshaler");
        }
    }

    private static object? DecodeShapeObject(Decoder decoder, JsonSettings settings)
    {
        decoder.Read();
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        HashSet<string>? seen = settings._allowDuplicateNames ? null : new HashSet<string>(StringComparer.Ordinal);
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            CheckDuplicate(seen, name);
            map[name] = DecodeValue(decoder, settings, null);
        }

        decoder.Read();
        return map;
    }

    private static object? DecodeShapeArray(Decoder decoder, JsonSettings settings)
    {
        decoder.Read();
        var list = new List<object?>();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            list.Add(DecodeValue(decoder, settings, null));
        }

        decoder.Read();
        return list;
    }

    private static void DecodeIntoDictionary(Decoder decoder, IDictionary target, JsonSettings settings)
    {
        var kind = decoder.PeekKind();
        if (kind == JsonKind.Null)
        {
            // Go：JSON null 解进 map 不动原有值。
            decoder.Read();
            return;
        }

        if (kind != JsonKind.BeginObject)
        {
            throw Mismatch(kind, target.GetType());
        }

        decoder.Read();
        var (keyType, valueType) = DictionaryTypes(target.GetType());
        target.Clear();
        HashSet<string>? seen = settings._allowDuplicateNames ? null : new HashSet<string>(StringComparer.Ordinal);
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadName();
            CheckDuplicate(seen, name);
            var key = ParseKey(name, keyType);
            var value = DecodeValue(decoder, settings, valueType);
            target[key] = value;
        }

        decoder.Read();
    }

    private static void DecodeIntoList(Decoder decoder, IList target, JsonSettings settings)
    {
        var kind = decoder.PeekKind();
        if (kind == JsonKind.Null)
        {
            decoder.Read();
            return;
        }

        if (kind != JsonKind.BeginArray)
        {
            throw Mismatch(kind, target.GetType());
        }

        decoder.Read();
        var type = target.GetType();
        var elementType = ListElementType(type);

        if (type.IsArray)
        {
            // C# 的数组长度固定：JSON 元素个数必须与目标数组长度一致（Go 的切片可以重新分配）。
            var index = 0;
            while (decoder.PeekKind() != JsonKind.EndArray)
            {
                if (index >= target.Count)
                {
                    throw new JsonException($"json: cannot unmarshal more than {target.Count} elements into {type}");
                }

                target[index++] = DecodeValue(decoder, settings, elementType);
            }

            decoder.Read();
            if (index != target.Count)
            {
                throw new JsonException($"json: cannot unmarshal {index} elements into {type} of length {target.Count}");
            }

            return;
        }

        target.Clear();
        while (decoder.PeekKind() != JsonKind.EndArray)
        {
            target.Add(DecodeValue(decoder, settings, elementType));
        }

        decoder.Read();
    }

    private static void CheckDuplicate(HashSet<string>? seen, string name)
    {
        if (seen is not null && !seen.Add(name))
        {
            // Go json/v2 默认拒绝重复属性名。
            throw new JsonException($"json: duplicate object member name {name}");
        }
    }

    private static object? ParseNumber(string text, Type? target)
    {
        try
        {
            if (target is null || target == typeof(object) || target == typeof(double))
            {
                // Go 的 any（interface）默认把 JSON 数字解成 float64。
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            if (target.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(target);
                return Enum.ToObject(target, ParseNumber(text, underlying)!);
            }

            return Type.GetTypeCode(target) switch
            {
                TypeCode.Single => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                TypeCode.Decimal => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                TypeCode.SByte => sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.Byte => byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.Int16 => short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.UInt16 => ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.Int32 => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.UInt32 => uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.Int64 => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.UInt64 => ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                TypeCode.Double => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                _ => target == typeof(Int128)
                    ? Int128.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
                    : target == typeof(UInt128)
                        ? UInt128.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
                        : target == typeof(BigInteger)
                            ? BigInteger.Parse(text, CultureInfo.InvariantCulture)
                            : throw new JsonException($"json: cannot unmarshal number into {target}"),
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new JsonException($"json: cannot unmarshal {text} into {target}", e);
        }
    }

    private static object ParseKey(string name, Type keyType)
    {
        if (keyType == typeof(string) || keyType == typeof(object))
        {
            return name;
        }

        return ParseNumber(name, keyType)!;
    }

    // 泛型集合的键/值类型（用 typeof(T) 的元数据，不涉及成员反射）。
    private static (Type Key, Type Value) DictionaryTypes(Type type)
    {
        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            if (arguments.Length == 2)
            {
                return (arguments[0], arguments[1]);
            }
        }

        // 非泛型 IDictionary（Hashtable 等）：Go 侧没有对应物，按最常见的 string→object? 处理。
        return (typeof(string), typeof(object));
    }

    private static Type ListElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType() ?? typeof(object);
        }

        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            if (arguments.Length == 1)
            {
                return arguments[0];
            }
        }

        return typeof(object);
    }

    private static JsonException NeedsInstance(Type type, string shape)
    {
        return new JsonException(
            $"json: cannot unmarshal {shape} into {type}: type {type} must implement IJsonUnmarshaler, "
            + "or be created by the caller (use UnmarshalNew<T>() / UnmarshalInto(...))");
    }

    private static JsonException Mismatch(JsonKind kind, Type type)
    {
        return new JsonException($"json: cannot unmarshal {KindName(kind)} into {type}");
    }

    private static string KindName(JsonKind kind)
    {
        return kind switch
        {
            JsonKind.BeginObject => "object",
            JsonKind.EndObject => "object end",
            JsonKind.BeginArray => "array",
            JsonKind.EndArray => "array end",
            JsonKind.String => "string",
            JsonKind.Number => "number",
            JsonKind.True or JsonKind.False => "boolean",
            JsonKind.Null => "null",
            _ => kind.ToString(),
        };
    }
}
