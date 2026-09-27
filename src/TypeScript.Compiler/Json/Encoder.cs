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
    private readonly Utf8JsonWriter writer;
    private readonly OutputFilterStream filter;
    private readonly JsonSettings settings;

    private byte[] stack = new byte[16];
    private int depth;
    private bool expectName;
    private bool disposed;

    public Encoder(Stream output, params JsonOptions[] options)
    {
        settings = JsonSettings.From(options);
        filter = new OutputFilterStream(output, settings);
        writer = new Utf8JsonWriter(filter, CreateWriterOptions(settings));
    }

    // 编码期选项（Deterministic 等）；MarshalEncode 在没给 opts 时沿用这里的配置。
    internal JsonSettings Settings => settings;

    // Go 的 jsontext.Encoder.StackDepth()：当前容器的嵌套深度（顶层值时为 0）。
    public int StackDepth => depth;

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
                if (expectName)
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
            writer.WriteRawValue(json);
        }
        else
        {
            var prefix = Encoding.UTF8.GetBytes(indent);
            var buffer = new byte[prefix.Length + json.Length];
            prefix.CopyTo(buffer, 0);
            json.CopyTo(buffer.AsSpan(prefix.Length));
            writer.WriteRawValue(buffer);
        }

        AfterValue();
    }

    public void WriteValue(string json)
    {
        EnsureCanWriteValue();
        var indent = RawValueIndent();
        writer.WriteRawValue(indent.Length == 0 ? json : indent + json);
        AfterValue();
    }

    // Go 的 jsontext.Encoder：Flush 把缓冲的字节写到底层 writer（io.Writer → C# Stream）。
    public void Flush()
    {
        writer.Flush();
        filter.Flush();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        writer.Flush();
        writer.Dispose();
        filter.Flush();
    }

    // ---- 映射层（Encode.cs）用的写入原语；同时维护 token 级状态 ----

    internal void StartObject()
    {
        EnsureCanWriteValue();
        writer.WriteStartObject();
        Push((byte)'{');
        expectName = true;
    }

    internal void EndObject()
    {
        writer.WriteEndObject();
        Pop();
        AfterValue();
    }

    internal void StartArray()
    {
        EnsureCanWriteValue();
        writer.WriteStartArray();
        Push((byte)'[');
        expectName = false;
    }

    internal void EndArray()
    {
        writer.WriteEndArray();
        Pop();
        AfterValue();
    }

    internal void WriteName(string name)
    {
        writer.WritePropertyName(name);
        expectName = false;
    }

    internal void WriteStringValue(string value)
    {
        // 与 Go 的 jsontext 一致：String token 落在“等属性名”位置时写成属性名
        //（internal/collections/ordered_map.go 的 `json.MarshalEncode(enc, key)` 依赖这一点）。
        if (expectName)
        {
            WriteName(value);
            return;
        }

        writer.WriteStringValue(value);
        AfterValue();
    }

    internal void WriteBoolValue(bool value)
    {
        EnsureCanWriteValue();
        writer.WriteBooleanValue(value);
        AfterValue();
    }

    internal void WriteNullValue()
    {
        EnsureCanWriteValue();
        writer.WriteNullValue();
        AfterValue();
    }

    // 数字一律走这里：Go 的数字文本已经由 FloatFormat/整数格式化算好。
    internal void WriteRawValue(string json)
    {
        EnsureCanWriteValue();
        var indent = RawValueIndent();
        writer.WriteRawValue(indent.Length == 0 ? json : indent + json);
        AfterValue();
    }

    // Utf8JsonWriter 的 WriteRawValue 不参与缩进（实测：数组元素之间既不换行也不缩进），
    // 所以原样写的值落在数组里时要自己补 "\n" + 缩进；对象成员的值紧跟 ": " 之后，不能补。
    // NeedsReindent（非空格/制表符缩进或带 prefix）时 writer 用 IndentSize=1，交给 OutputFilterStream 重排。
    private string RawValueIndent()
    {
        if (!settings.HasIndent || depth == 0 || stack[depth - 1] != (byte)'[')
        {
            return "";
        }

        var builder = new StringBuilder();
        builder.Append('\n');
        if (settings.NeedsReindent)
        {
            builder.Append(' ', depth);
        }
        else
        {
            for (var i = 0; i < depth; i++)
            {
                builder.Append(settings.Indent);
            }
        }

        return builder.ToString();
    }

    private static JsonWriterOptions CreateWriterOptions(in JsonSettings settings)
    {
        var options = new JsonWriterOptions
        {
            Indented = settings.HasIndent,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        if (settings.HasIndent)
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
                options.IndentSize = settings.Indent.Length;
            }
            else
            {
                options.IndentCharacter = ' ';
                options.IndentSize = Math.Max(settings.Indent.Length, 1);
            }
        }

        return options;
    }

    private void Push(byte container)
    {
        if (depth == stack.Length)
        {
            Array.Resize(ref stack, stack.Length * 2);
        }

        stack[depth++] = container;
    }

    private void Pop()
    {
        if (depth == 0)
        {
            throw new JsonException("json: unbalanced end of object/array");
        }

        depth--;
    }

    private void EnsureCanWriteValue()
    {
        if (expectName)
        {
            throw new JsonException("json: cannot write a value when a property name is expected");
        }
    }

    private void AfterValue()
    {
        expectName = depth > 0 && stack[depth - 1] == (byte)'{';
    }
}
