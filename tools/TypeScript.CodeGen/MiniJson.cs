// 生成器专用的小 JSON 解析器。
//
// 为什么不用 System.Text.Json 的反射序列化：Directory.Build.props 打开了 IsAotCompatible（AOT/trim 分析器 +
// TreatWarningsAsErrors），JsonSerializer 会报 IL2026/IL3050。这个解析器只服务于生成器的小输入
// （ast.json 的 schema），不引入任何反射。
//
// 为了保持 JSON 对象成员顺序（生成物依赖 ast.json 里的声明顺序），返回的是有序的 JsonObject。
using System.Globalization;
using System.Text;

namespace TypeScript.CodeGen;

/// <summary>保持插入顺序的 JSON 对象。</summary>
internal sealed class JsonObject
{
    private readonly List<KeyValuePair<string, object?>> _items = [];
    private readonly Dictionary<string, object?> _map = new(StringComparer.Ordinal);

    public IReadOnlyList<KeyValuePair<string, object?>> Items => _items;

    public int Count => _items.Count;

    public bool Has(string key) => _map.ContainsKey(key);

    public object? this[string key] => _map.TryGetValue(key, out var value) ? value : null;

    public void Add(string key, object? value)
    {
        if (_map.ContainsKey(key))
        {
            throw new InvalidDataException($"JSON 对象里有重复的键：{key}");
        }

        _items.Add(new KeyValuePair<string, object?>(key, value));
        _map.Add(key, value);
    }
}

internal static class MiniJson
{
    public static JsonObject ParseObject(string text)
    {
        var index = 0;
        var value = ParseValue(text, ref index);
        SkipWhitespace(text, ref index);
        if (index != text.Length)
        {
            throw new InvalidDataException("JSON 尾部有多余内容。");
        }

        return value as JsonObject ?? throw new InvalidDataException("顶层 JSON 不是对象。");
    }

    private static object? ParseValue(string text, ref int index)
    {
        SkipWhitespace(text, ref index);
        if (index >= text.Length)
        {
            throw new InvalidDataException("JSON 意外结束。");
        }

        var c = text[index];
        switch (c)
        {
            case '{':
                return ParseObjectBody(text, ref index);
            case '[':
                return ParseArrayBody(text, ref index);
            case '"':
                return ParseString(text, ref index);
            case 't':
                Expect(text, ref index, "true");
                return true;
            case 'f':
                Expect(text, ref index, "false");
                return false;
            case 'n':
                Expect(text, ref index, "null");
                return null;
            default:
                return ParseNumber(text, ref index);
        }
    }

    private static JsonObject ParseObjectBody(string text, ref int index)
    {
        var result = new JsonObject();
        index++; // '{'
        SkipWhitespace(text, ref index);
        if (index < text.Length && text[index] == '}')
        {
            index++;
            return result;
        }

        while (true)
        {
            SkipWhitespace(text, ref index);
            var key = ParseString(text, ref index);
            SkipWhitespace(text, ref index);
            if (index >= text.Length || text[index] != ':')
            {
                throw new InvalidDataException("JSON 对象缺少 ':'。");
            }

            index++;
            result.Add(key, ParseValue(text, ref index));
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new InvalidDataException("JSON 对象未闭合。");
            }

            if (text[index] == ',')
            {
                index++;
                continue;
            }

            if (text[index] == '}')
            {
                index++;
                return result;
            }

            throw new InvalidDataException("JSON 对象里出现非法字符。");
        }
    }

    private static List<object?> ParseArrayBody(string text, ref int index)
    {
        var result = new List<object?>();
        index++; // '['
        SkipWhitespace(text, ref index);
        if (index < text.Length && text[index] == ']')
        {
            index++;
            return result;
        }

        while (true)
        {
            result.Add(ParseValue(text, ref index));
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new InvalidDataException("JSON 数组未闭合。");
            }

            if (text[index] == ',')
            {
                index++;
                continue;
            }

            if (text[index] == ']')
            {
                index++;
                return result;
            }

            throw new InvalidDataException("JSON 数组里出现非法字符。");
        }
    }

    private static string ParseString(string text, ref int index)
    {
        if (index >= text.Length || text[index] != '"')
        {
            throw new InvalidDataException("JSON 期望字符串。");
        }

        index++;
        var sb = new StringBuilder();
        while (true)
        {
            if (index >= text.Length)
            {
                throw new InvalidDataException("JSON 字符串未闭合。");
            }

            var c = text[index++];
            if (c == '"')
            {
                return sb.ToString();
            }

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            var escape = text[index++];
            switch (escape)
            {
                case '"':
                    sb.Append('"');
                    break;
                case '\\':
                    sb.Append('\\');
                    break;
                case '/':
                    sb.Append('/');
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u':
                    var code = int.Parse(text.AsSpan(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    index += 4;
                    sb.Append((char)code);
                    break;
                default:
                    throw new InvalidDataException($"未知的 JSON 转义：\\{escape}");
            }
        }
    }

    private static double ParseNumber(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && (char.IsAsciiDigit(text[index]) || text[index] is '-' or '+' or '.' or 'e' or 'E'))
        {
            index++;
        }

        var span = text.AsSpan(start, index - start);
        return double.Parse(span, CultureInfo.InvariantCulture);
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && text[index] is ' ' or '\t' or '\n' or '\r')
        {
            index++;
        }
    }

    private static void Expect(string text, ref int index, string literal)
    {
        if (!text.AsSpan(index).StartsWith(literal, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"JSON 期望 {literal}。");
        }

        index += literal.Length;
    }
}
