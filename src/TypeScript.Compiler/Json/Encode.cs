// ported from: internal/json/json.go（json.Marshal / MarshalIndent / MarshalEncode 的写侧）
// + Go encoding/json/v2 的编码语义：any 按动态类型、map[K]V 默认枚举顺序（Deterministic(true) 时按 key 排序）、
//   枚举按底层整数、[]byte 走 base64、IJsonMarshaler 优先。
//
// AOT：全程静态分派（`is` 模式匹配），不用反射、不用泛型实例化（docs/json.md §2）。
// 结构体/类的字段级映射在 Go 里靠 reflect；C# 侧要求这些类型自己实现 IJsonMarshaler —— 见 §2 与“如何为某个类型提供 JSON”。
using System.Collections;
using System.Globalization;
using System.Numerics;

namespace TypeScript.Json;

internal static class JsonEncode
{
    internal static void EncodeValue(Encoder encoder, object? value, JsonSettings settings)
    {
        // Go 的 MarshalerTo 优先于一切内建形状。
        if (value is IJsonMarshaler marshaler)
        {
            marshaler.MarshalJSONTo(encoder);
            return;
        }

        switch (value)
        {
            case null:
                encoder.WriteNullValue();
                return;
            case string text:
                encoder.WriteStringValue(text);
                return;
            case bool boolean:
                encoder.WriteBoolValue(boolean);
                return;
            case char character:
                encoder.WriteStringValue(character.ToString());
                return;
            case byte[] bytes:
                // Go 的 []byte → base64 字符串。
                encoder.WriteStringValue(Convert.ToBase64String(bytes));
                return;
            case double d:
                encoder.WriteRawValue(FloatFormat.Format(d));
                return;
            case float f:
                encoder.WriteRawValue(FloatFormat.Format(f));
                return;
            case decimal m:
                encoder.WriteRawValue(m.ToString(CultureInfo.InvariantCulture));
                return;
            case sbyte i8:
                encoder.WriteRawValue(i8.ToString(CultureInfo.InvariantCulture));
                return;
            case byte u8:
                encoder.WriteRawValue(u8.ToString(CultureInfo.InvariantCulture));
                return;
            case short i16:
                encoder.WriteRawValue(i16.ToString(CultureInfo.InvariantCulture));
                return;
            case ushort u16:
                encoder.WriteRawValue(u16.ToString(CultureInfo.InvariantCulture));
                return;
            case int i32:
                encoder.WriteRawValue(i32.ToString(CultureInfo.InvariantCulture));
                return;
            case uint u32:
                encoder.WriteRawValue(u32.ToString(CultureInfo.InvariantCulture));
                return;
            case long i64:
                encoder.WriteRawValue(i64.ToString(CultureInfo.InvariantCulture));
                return;
            case ulong u64:
                encoder.WriteRawValue(u64.ToString(CultureInfo.InvariantCulture));
                return;
            case Int128 i128:
                encoder.WriteRawValue(i128.ToString(CultureInfo.InvariantCulture));
                return;
            case UInt128 u128:
                encoder.WriteRawValue(u128.ToString(CultureInfo.InvariantCulture));
                return;
            case BigInteger big:
                encoder.WriteRawValue(big.ToString(CultureInfo.InvariantCulture));
                return;
            case Half half:
                encoder.WriteRawValue(FloatFormat.Format((float)half));
                return;
            case Enum enumValue:
                encoder.WriteRawValue(FormatEnum(enumValue));
                return;
            case IDictionary dictionary:
                EncodeDictionary(encoder, dictionary, settings);
                return;
            case IEnumerable items:
                EncodeArray(encoder, items, settings);
                return;
            default:
                throw new JsonException($"json: type {value.GetType()} must implement IJsonMarshaler");
        }
    }

    private static void EncodeDictionary(Encoder encoder, IDictionary dictionary, JsonSettings settings)
    {
        encoder.StartObject();
        if (settings._deterministic)
        {
            // Go 的 json.Deterministic(true)：map 键排序后输出（默认是枚举顺序）。
            var entries = new List<KeyValuePair<object, object?>>(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                entries.Add(new KeyValuePair<object, object?>(entry.Key, entry.Value));
            }

            entries.Sort(static (a, b) => a.Key is string left
                ? string.CompareOrdinal(left, (string)b.Key)
                : Comparer<object>.Default.Compare(a.Key, b.Key));
            foreach (var entry in entries)
            {
                encoder.WriteName(FormatKey(entry.Key));
                EncodeValue(encoder, entry.Value, settings);
            }
        }
        else
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                encoder.WriteName(FormatKey(entry.Key));
                EncodeValue(encoder, entry.Value, settings);
            }
        }

        encoder.EndObject();
    }

    private static void EncodeArray(Encoder encoder, IEnumerable items, JsonSettings settings)
    {
        encoder.StartArray();
        foreach (var item in items)
        {
            EncodeValue(encoder, item, settings);
        }

        encoder.EndArray();
    }

    // Go 的 resolveKeyName：string / 整数 / 枚举（按底层整数）→ 名字；其它类型报错。
    private static string FormatKey(object key)
    {
        return key switch
        {
            string text => text,
            sbyte i8 => i8.ToString(CultureInfo.InvariantCulture),
            byte u8 => u8.ToString(CultureInfo.InvariantCulture),
            short i16 => i16.ToString(CultureInfo.InvariantCulture),
            ushort u16 => u16.ToString(CultureInfo.InvariantCulture),
            int i32 => i32.ToString(CultureInfo.InvariantCulture),
            uint u32 => u32.ToString(CultureInfo.InvariantCulture),
            long i64 => i64.ToString(CultureInfo.InvariantCulture),
            ulong u64 => u64.ToString(CultureInfo.InvariantCulture),
            Int128 i128 => i128.ToString(CultureInfo.InvariantCulture),
            UInt128 u128 => u128.ToString(CultureInfo.InvariantCulture),
            BigInteger big => big.ToString(CultureInfo.InvariantCulture),
            Enum enumValue => FormatEnum(enumValue),
            _ => throw new JsonException($"json: unsupported map key type: {key.GetType()}"),
        };
    }

    // Go 的枚举是整数类型；C# 的 enum 按底层整数写出。
    private static string FormatEnum(Enum value)
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(value.GetType())) switch
        {
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        };
    }
}
