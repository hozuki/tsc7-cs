// ported from: internal/json/json.go（`Encoder = jsontext.Encoder` 的别名）
// + Go encoding/json/jsontext 的 Encoder 语义（WriteToken/WriteValue/StackDepth）。
//
// 写侧结构用 System.Text.Json 的 Utf8JsonWriter（Indented + IndentCharacter/IndentSize + NewLine = "\n"）
// 产生 Go 的多行形状（`"k": v`、LF、无尾部换行）；转义与数字由 C# 侧自己控制：
//   * 转义：writer 配 JavaScriptEncoder.UnsafeRelaxedJsonEscaping，再由 OutputFilterStream 按字节修正成 Go 的
//     （为什么不能直接继承 JavaScriptEncoder：见 OutputFilterStream.cs 的文件头）
//   * 数字：不走 WriteNumberValue（它会写出 1E+21、1E-07、…），而是 FloatFormat.Format 之后 WriteRawValue
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace TypeScript.Json;

public sealed class Encoder : IDisposable
{
    /// <summary>
    /// 本移植统一的 JSON 嵌套深度上限（写侧与读侧共用；Go 侧没有写死的写上限，解码侧 10000 量级）。
    /// 见 <see cref="CreateWriterOptions"/> 的实测说明：不设它就会落到 .NET 的默认 1000，
    /// 使 <c>ls.maxSelectionRangeDepth = 1000</c> 的响应在序列化时直接抛。
    /// </summary>
    internal const int MaxNestingDepth = 10000;

    private readonly Utf8JsonWriter _writer;
    private readonly OutputFilterStream _filter;
    private readonly JsonSettings _settings;

    private byte[] _stack = new byte[16];
    private int _depth;
    private bool _expectName;
    private bool _disposed;

    public Encoder(Stream output, params JsonOptions[] options)
    {
        _settings = JsonSettings.From(options);
        _filter = new OutputFilterStream(output, _settings);
        _writer = new Utf8JsonWriter(_filter, CreateWriterOptions(_settings));
    }

    // 编码期选项（Deterministic 等）；MarshalEncode 在没给 opts 时沿用这里的配置。
    internal JsonSettings Settings => _settings;

    // Go 的 jsontext.Encoder.StackDepth()：当前容器的嵌套深度（顶层值时为 0）。
    public int StackDepth => _depth;

    // Go 的 jsontext.Encoder.WriteToken(Token)：写一个 token。
    // 与 Go 一样，String token 在对象里处于“等属性名”位置时写成属性名，否则写成字符串值。
    public void WriteToken(JsonToken token)
    {
        switch (token.Kind)
        {
            case JsonKind.BeginObject:
                StartObject();
                break;
            case JsonKind.EndObject:
                EndObject();
                break;
            case JsonKind.BeginArray:
                StartArray();
                break;
            case JsonKind.EndArray:
                EndArray();
                break;
            case JsonKind.Null:
                WriteNullValue();
                break;
            case JsonKind.True:
                WriteBoolValue(true);
                break;
            case JsonKind.False:
                WriteBoolValue(false);
                break;
            case JsonKind.String:
                if (_expectName)
                {
                    WriteName(token.StringValue);
                }
                else
                {
                    WriteStringValue(token.StringValue);
                }

                break;
            case JsonKind.Number:
                WriteRawValue(token.StringValue);
                break;
            default:
                throw new JsonException($"json: invalid token: {token.Kind}");
        }
    }

    // Go 的 jsontext.Encoder.WriteValue(Value)：原样写出一个 JSON 值（不重新转义、内部不重排缩进）。
    public void WriteValue(ReadOnlySpan<byte> json)
    {
        EnsureCanWriteValue();
        var indent = RawValueIndent();
        if (indent.Length == 0)
        {
            _writer.WriteRawValue(json);
        }
        else
        {
            var prefix = Encoding.UTF8.GetBytes(indent);
            var buffer = new byte[prefix.Length + json.Length];
            prefix.CopyTo(buffer, 0);
            json.CopyTo(buffer.AsSpan(prefix.Length));
            _writer.WriteRawValue(buffer);
        }

        AfterValue();
    }

    public void WriteValue(string json)
    {
        EnsureCanWriteValue();
        var indent = RawValueIndent();
        _writer.WriteRawValue(indent.Length == 0 ? json : indent + json);
        AfterValue();
    }

    // Go 的 jsontext.Encoder：Flush 把缓冲的字节写到底层 writer（io.Writer → C# Stream）。
    public void Flush()
    {
        _writer.Flush();
        _filter.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Flush();
        _writer.Dispose();
        _filter.Flush();
    }

    // ---- 映射层（Encode.cs）用的写入原语；同时维护 token 级状态 ----

    internal void StartObject()
    {
        EnsureCanWriteValue();
        _writer.WriteStartObject();
        Push((byte)'{');
        _expectName = true;
    }

    internal void EndObject()
    {
        _writer.WriteEndObject();
        Pop();
        AfterValue();
    }

    internal void StartArray()
    {
        EnsureCanWriteValue();
        _writer.WriteStartArray();
        Push((byte)'[');
        _expectName = false;
    }

    internal void EndArray()
    {
        _writer.WriteEndArray();
        Pop();
        AfterValue();
    }

    internal void WriteName(string name)
    {
        _writer.WritePropertyName(name);
        _expectName = false;
    }

    internal void WriteStringValue(string value)
    {
        // 与 Go 的 jsontext 一致：String token 落在“等属性名”位置时写成属性名
        //（internal/collections/ordered_map.go 的 `json.MarshalEncode(enc, key)` 依赖这一点）。
        if (_expectName)
        {
            WriteName(value);
            return;
        }

        _writer.WriteStringValue(value);
        AfterValue();
    }

    internal void WriteBoolValue(bool value)
    {
        EnsureCanWriteValue();
        _writer.WriteBooleanValue(value);
        AfterValue();
    }

    internal void WriteNullValue()
    {
        EnsureCanWriteValue();
        _writer.WriteNullValue();
        AfterValue();
    }

    // 数字一律走这里：Go 的数字文本已经由 FloatFormat/整数格式化算好。
    internal void WriteRawValue(string json)
    {
        EnsureCanWriteValue();
        var indent = RawValueIndent();
        _writer.WriteRawValue(indent.Length == 0 ? json : indent + json);
        AfterValue();
    }

    // Utf8JsonWriter 的 WriteRawValue 不参与缩进（实测：数组元素之间既不换行也不缩进），
    // 所以原样写的值落在数组里时要自己补 "\n" + 缩进；对象成员的值紧跟 ": " 之后，不能补。
    // NeedsReindent（非空格/制表符缩进或带 prefix）时 writer 用 IndentSize=1，交给 OutputFilterStream 重排。
    private string RawValueIndent()
    {
        if (!_settings._hasIndent || _depth == 0 || _stack[_depth - 1] != (byte)'[')
        {
            return "";
        }

        var builder = new StringBuilder();
        builder.Append('\n');
        if (_settings.NeedsReindent)
        {
            builder.Append(' ', _depth);
        }
        else
        {
            for (var i = 0; i < _depth; i++)
            {
                builder.Append(_settings._indent);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Go 侧（internal/json）的写侧没有写死的嵌套深度上限（Marshal 按 Go 栈递归，只有解码侧有 10000 的
    /// <c>maxNestingDepth</c> 量级约束）；C# 侧此前漏设 <see cref="JsonWriterOptions.MaxDepth"/>，落到
    /// <c>Utf8JsonWriter</c> 的**默认 1000**。实测（G4 的 selectionranges 移植用例，1000 层 SelectionRange 链）：
    /// <code>
    /// InvalidOperationException: CurrentDepth (1000) is equal to or larger than the maximum allowed depth of 1000.
    /// Cannot write the next JSON object or array.
    /// </code>
    /// 而 <c>ls.maxSelectionRangeDepth = 1000</c> 正好产出 1000 层深的响应 ⇒ lsp 服务端序列化深层嵌套文件的
    /// selection ranges 会直接抛。这里把上限对齐到 10000（与解码侧同一常量 <c>Json.MaxNestingDepth</c>，
    /// 远大于服务端任何真实负载），恢复 Go 的"不等价于失败"行为。
    /// </summary>
    private static JsonWriterOptions CreateWriterOptions(in JsonSettings settings)
    {
        var options = new JsonWriterOptions
        {
            MaxDepth = MaxNestingDepth,
            Indented = settings._hasIndent,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        if (settings._hasIndent)
        {
            if (settings.NeedsReindent)
            {
                // 由 OutputFilterStream 逐行重排：行首空格数＝writer 的缩进层级。
                options.IndentCharacter = ' ';
                options.IndentSize = 1;
            }
            else if (settings.IndentIsTab)
            {
                options.IndentCharacter = '\t';
                options.IndentSize = settings._indent.Length;
            }
            else
            {
                options.IndentCharacter = ' ';
                options.IndentSize = Math.Max(settings._indent.Length, 1);
            }
        }

        return options;
    }

    private void Push(byte container)
    {
        if (_depth == _stack.Length)
        {
            Array.Resize(ref _stack, _stack.Length * 2);
        }

        _stack[_depth++] = container;
    }

    private void Pop()
    {
        if (_depth == 0)
        {
            throw new JsonException("json: unbalanced end of object/array");
        }

        _depth--;
    }

    private void EnsureCanWriteValue()
    {
        if (_expectName)
        {
            throw new JsonException("json: cannot write a value when a property name is expected");
        }
    }

    private void AfterValue()
    {
        _expectName = _depth > 0 && _stack[_depth - 1] == (byte)'{';
    }
}
