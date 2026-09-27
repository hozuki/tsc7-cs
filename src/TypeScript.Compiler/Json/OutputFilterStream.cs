// C# 侧新增：把 Utf8JsonWriter 的输出字节修成 Go json/v2 的字节（docs/json.md §1.2、§2）。
//
// 为什么不用自定义 JavaScriptEncoder：JavaScriptEncoder 的两个抽象成员
//（FindFirstCharacterToEncode(char*, int)、TryEncodeUnicodeScalar(int, char*, int, out int)）都是指针签名，
// 实现它们必须开 unsafe，而本包不允许改 csproj 加 <AllowUnsafeBlocks>（docs/json.md §2 记了原因）。
// 所以这里改成“写完后按字节修正”，可观察结果与 Go 完全一致（见 tests/TypeScript.Compiler.Tests/Json 的转义用例）。
//
// 修正表（左＝UnsafeRelaxedJsonEscaping 的输出，右＝Go json/v2 的输出）：
//   \u00XX（控制字符，大写十六进制） → \u00xx（Go 用 "0123456789abcdef"）
//   其余 \uXXXX                      → 原样 UTF-8 字节：Go 不转义 DEL、U+00A0、U+0085、U+1680、
//                                      U+2000…、U+2028/U+2029、U+202F、U+205F、U+3000、U+FEFF、私用区、
//                                      astral 等，而 UnsafeRelaxed 会转义它们（实测）
//   \uD83D\uDE00（代理对）           → 原样 UTF-8 4 字节（Go 直接写 UTF-8）
//   孤立代理项                       → UnsafeRelaxed 已替换成 U+FFFD 才交给我们，原始值不可恢复
//                                      → 原样 UTF-8 的 U+FFFD（EF BF BD）。Go 会写 WTF-8，见 docs/json.md §3 的 unverified 行
//   \" \\ \b \f \n \r \t             → 原样（Go 的短转义与它一致）
//   < > & 与普通非 ASCII             → 原样（两者一致，不转义）
//
// 字符串边界识别是安全的：转义规则保证字符串内的 0x22/0x5C 一定带反斜杠前缀，且字符串内不出现裸控制字符
//（都被转义了），所以“未转义的 "”只可能是定界符、“未转义的 \u”只可能来自编码器。
//
// 偏离 Go 的一点：Go 的 jsontext.Encoder.WriteValue 是纯字节透传，而这里原样写的值也会被上面这套修正
//（Encoder.WriteValue / MarshalJSONTo 里的原始 JSON 走的是同一条路），即只改写字符串里的转义形式。
//
// 顺带处理缩进：Utf8JsonWriter 只支持“空格/制表符重复 N 次”，所以缩进串不是这两者、或带 prefix 时，
// 让 writer 用 IndentSize=1（行首空格数＝它的缩进层级），在这里逐行重排成 prefix + indent*层级。
namespace TypeScript.Json;

internal sealed class OutputFilterStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _reindent;
    private readonly byte[] _indentPrefixBytes;
    private readonly byte[] _indentBytes;

    private byte[] _inputBuffer = new byte[4096];
    private byte[] _outputBuffer = new byte[4096];
    private int _outputLength;

    private readonly byte[] _carry = new byte[12];
    private int _carryLength;

    private bool _inString;
    private bool _pendingNewline;
    private int _pendingSpaces;

    internal OutputFilterStream(Stream inner, in JsonSettings settings)
    {
        this._inner = inner;
        _reindent = settings.NeedsReindent;
        _indentPrefixBytes = System.Text.Encoding.UTF8.GetBytes(settings._indentPrefix);
        _indentBytes = System.Text.Encoding.UTF8.GetBytes(settings._indent);
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var total = _carryLength + buffer.Length;
        if (_inputBuffer.Length < total + 12)
        {
            var size = _inputBuffer.Length;
            while (size < total + 12)
            {
                size *= 2;
            }

            _inputBuffer = new byte[size];
        }

        if (_carryLength > 0)
        {
            _carry.AsSpan(0, _carryLength).CopyTo(_inputBuffer);
        }

        buffer.CopyTo(_inputBuffer.AsSpan(_carryLength));

        // 末尾最多 12 字节可能是不完整的转义序列（\uD83D\uDE00），留给下一次 Write。
        var consumed = Process(_inputBuffer.AsSpan(0, total));
        _carryLength = total - consumed;
        if (_carryLength > 0)
        {
            _inputBuffer.AsSpan(consumed, _carryLength).CopyTo(_carry);
        }

        FlushOutput();
    }

    public override void WriteByte(byte value)
    {
        Span<byte> one = stackalloc byte[1];
        one[0] = value;
        Write(one);
    }

    public override void Flush()
    {
        if (_carryLength > 0)
        {
            // 编码器的输出一定以完整的转义序列结束；走到这里说明上游写坏了。
            throw new JsonException("json: incomplete escape sequence in encoder output");
        }

        if (_pendingNewline)
        {
            _outputLength = AppendByte(_outputLength, (byte)'\n');
            _pendingNewline = false;
        }

        FlushOutput();
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    // 处理一段输入，返回已确定处理的字节数（返回点之后是可能不完整的转义序列）。
    private int Process(ReadOnlySpan<byte> span)
    {
        var length = span.Length;
        var p = 0;

        while (p < length)
        {
            if (!_inString)
            {
                var b = span[p];
                if (b == (byte)'"')
                {
                    _inString = true;
                }

                Emit(b);
                p++;
                continue;
            }

            var c = span[p];
            if (c == (byte)'"')
            {
                _inString = false;
                Emit(c);
                p++;
                continue;
            }

            if (c != (byte)'\\')
            {
                Emit(c);
                p++;
                continue;
            }

            if (p + 1 >= length)
            {
                break;
            }

            var e = span[p + 1];
            if (e != (byte)'u')
            {
                // \\ \" \b \f \n \r \t：Go 与 UnsafeRelaxedJsonEscaping 完全一致。
                Emit(c);
                Emit(e);
                p += 2;
                continue;
            }

            if (p + 5 >= length)
            {
                break;
            }

            var code = ParseHex4(span.Slice(p + 2, 4));
            if (code < 0)
            {
                throw new JsonException("json: invalid \\u escape in encoder output");
            }

            if (code is >= 0xD800 and <= 0xDBFF)
            {
                // 代理对 → 原样 UTF-8；孤立高代理项 → 原样 WTF-8（Go 的 AllowInvalidUTF8 行为）。
                if (p + 11 >= length)
                {
                    break;
                }

                if (span[p + 6] == (byte)'\\' && span[p + 7] == (byte)'u')
                {
                    var low = ParseHex4(span.Slice(p + 8, 4));
                    if (low is >= 0xDC00 and <= 0xDFFF)
                    {
                        EmitRune(0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00));
                        p += 12;
                        continue;
                    }
                }

                EmitRune(code);
                p += 6;
                continue;
            }

            if (code is >= 0xDC00 and <= 0xDFFF)
            {
                EmitRune(code);
                p += 6;
                continue;
            }

            if (code is 0x7F or 0x2028 or 0x2029)
            {
                // Go 不转义 DEL，也不做 JS 安全的行分隔符转义。
                EmitRune(code);
                p += 6;
                continue;
            }

            if (code < 0x20)
            {
                EmitEscapedHex((byte)code);
                p += 6;
                continue;
            }

            // 其它 \uXXXX：UnsafeRelaxedJsonEscaping 会转义一批“类空格/格式/私用区”字符
            //（U+00A0、U+0085、U+1680、U+2000…、U+2028/U+2029、U+202F、U+205F、U+3000、U+FEFF、U+E000、
            // astral、DEL、以及孤立代理项被替换后的 U+FFFD），而 Go 一律原样写 UTF-8 字节 → 解回原始字节。
            EmitRune(code);
            p += 6;
        }

        return p;
    }

    private static int ParseHex4(ReadOnlySpan<byte> span)
    {
        var value = 0;
        for (var i = 0; i < 4; i++)
        {
            var c = span[i];
            int digit;
            if (c >= (byte)'0' && c <= (byte)'9')
            {
                digit = c - '0';
            }
            else if (c >= (byte)'a' && c <= (byte)'f')
            {
                digit = c - 'a' + 10;
            }
            else if (c >= (byte)'A' && c <= (byte)'F')
            {
                digit = c - 'A' + 10;
            }
            else
            {
                return -1;
            }

            value = (value << 4) | digit;
        }

        return value;
    }

    // 把字节交给输出（重缩进模式下行首的空格串会被换成 prefix + indent*层级）。
    private void Emit(byte b)
    {
        if (_reindent)
        {
            if (_pendingNewline)
            {
                if (b == (byte)' ')
                {
                    _pendingSpaces++;
                    return;
                }

                _outputLength = AppendByte(_outputLength, (byte)'\n');
                _outputLength = Append(_outputLength, _indentPrefixBytes);
                for (var i = 0; i < _pendingSpaces; i++)
                {
                    _outputLength = Append(_outputLength, _indentBytes);
                }

                _pendingNewline = false;
                _pendingSpaces = 0;
            }
            else if (b == (byte)'\n')
            {
                _pendingNewline = true;
                _pendingSpaces = 0;
                return;
            }
        }

        _outputLength = AppendByte(_outputLength, b);
    }

    private void EmitRune(int code)
    {
        if (code < 0x80)
        {
            Emit((byte)code);
        }
        else if (code < 0x800)
        {
            EnsureOutput(2);
            _outputBuffer[_outputLength++] = (byte)(0xC0 | (code >> 6));
            _outputBuffer[_outputLength++] = (byte)(0x80 | (code & 0x3F));
        }
        else if (code < 0x10000)
        {
            EnsureOutput(3);
            _outputBuffer[_outputLength++] = (byte)(0xE0 | (code >> 12));
            _outputBuffer[_outputLength++] = (byte)(0x80 | ((code >> 6) & 0x3F));
            _outputBuffer[_outputLength++] = (byte)(0x80 | (code & 0x3F));
        }
        else
        {
            EnsureOutput(4);
            _outputBuffer[_outputLength++] = (byte)(0xF0 | (code >> 18));
            _outputBuffer[_outputLength++] = (byte)(0x80 | ((code >> 12) & 0x3F));
            _outputBuffer[_outputLength++] = (byte)(0x80 | ((code >> 6) & 0x3F));
            _outputBuffer[_outputLength++] = (byte)(0x80 | (code & 0x3F));
        }
    }

    private void EmitEscapedHex(byte b)
    {
        _outputLength = AppendByte(_outputLength, (byte)'\\');
        _outputLength = AppendByte(_outputLength, (byte)'u');
        _outputLength = AppendByte(_outputLength, (byte)'0');
        _outputLength = AppendByte(_outputLength, (byte)'0');
        AppendHex(_outputLength, b);
        _outputLength += 2;
    }

    private void AppendHex(int at, byte b)
    {
        EnsureOutput(2);
        _outputBuffer[at] = HexDigit(b >> 4);
        _outputBuffer[at + 1] = HexDigit(b & 0xF);
    }

    private static byte HexDigit(int nibble)
    {
        return (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
    }

    private int AppendByte(int at, byte b)
    {
        EnsureOutput(1);
        _outputBuffer[at] = b;
        return at + 1;
    }

    private int Append(int at, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return at;
        }

        EnsureOutput(bytes.Length);
        bytes.CopyTo(_outputBuffer, at);
        return at + bytes.Length;
    }

    private void EnsureOutput(int extra)
    {
        if (_outputLength + extra <= _outputBuffer.Length)
        {
            return;
        }

        var size = _outputBuffer.Length;
        while (size < _outputLength + extra)
        {
            size *= 2;
        }

        Array.Resize(ref _outputBuffer, size);
    }

    private void FlushOutput()
    {
        if (_outputLength == 0)
        {
            return;
        }

        _inner.Write(_outputBuffer.AsSpan(0, _outputLength));
        _outputLength = 0;
    }
}
