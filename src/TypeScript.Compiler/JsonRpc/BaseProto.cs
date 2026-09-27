// ported from: internal/jsonrpc/baseproto.go
//
// C# 映射与偏离：
// 1) Go 的 `io.Reader`/`io.Writer` → C# 的 `Stream`（字节语义，与 Json 包一致）；Go 的 `bufio.Reader`/`bufio.Writer`
//    → `BufferedStream`（C# 的 Stream 本身不缓冲，ReadBytes 语义逐行照抄 Go 的 baseproto.go）。
// 2) Go 的 `var ErrX = errors.New(...)` → C# 的专用异常类型（Go 的 errors.Is → C# 的 catch；
//    见 docs/architecture.md §3.2 与 JsonRpc.cs 的文件头）。
// 3) Go 的 `Read() ([]byte, error)` 里 `io.EOF` 原样返回（调用方 `errors.Is(err, io.EOF)` 判连接正常结束）
//    → C# 的 `EndOfStreamException`：C# 的 read 返回 0 才是 Go 的 io.EOF，所以本方法的"正常结束"信号
//    统一成 `EndOfStreamException`（含"头没读完就 EOF"）。
// 4) **错误消息逐字对齐 Go**（这些文本会进 lsp 套件的 baseline 对拍，是可观测行为）：
//    * `ErrInvalidHeader`：Go 的 `%q` 作用在 `ReadBytes('\n')` 的**原始字节**上（含行尾），所以
//      `"Nope\r\n"` 报 `jsonrpc: invalid header: "Nope\r\n"`（消息里是字面转义）→ 见 `GoQuote`；
//      空行判定同样照 Go 只看恰好 `"\r\n"`（裸 `\n` 是"缺冒号的头"，不是空行）。
//    * `ErrInvalidContentLength`：Go 透传 `strconv.ParseInt` 的 `NumError.Error()`，C# 侧构造等价文本
//      （`strconv.ParseInt: parsing "&lt;输入&gt;": invalid syntax | value out of range`）→ 见 `ParseIntError`。
using System.Globalization;
using System.Text;

namespace TypeScript.JsonRpc;

/// <summary>Go 的 <c>ErrInvalidHeader</c>。</summary>
public sealed class InvalidHeaderException(string detail) : Exception($"jsonrpc: invalid header: {detail}");

/// <summary>Go 的 <c>ErrInvalidContentLength</c>。</summary>
public sealed class InvalidContentLengthException(string detail) : Exception($"jsonrpc: invalid content length: {detail}");

/// <summary>Go 的 <c>ErrNoContentLength</c>。</summary>
public sealed class NoContentLengthException() : Exception("jsonrpc: no content length");

/// <summary>
/// Go: <c>type Reader struct</c>（带 Content-Length 头的 JSON-RPC/LSP 基础协议读侧）。
/// </summary>
public sealed class Reader
{
    /// <summary>Go 的 <c>bufio.Reader</c>：按行/按字节读都要缓冲，否则每个字节一次系统调用。</summary>
    private readonly BufferedStream _stream;

    private Reader(Stream stream)
    {
        _stream = new BufferedStream(stream, 4096);
    }

    /// <summary>Go: <c>func NewReader(r io.Reader) *Reader</c>。</summary>
    public static Reader NewReader(Stream stream) => new(stream);

    /// <summary>
    /// Go: <c>func (r *Reader) Read() ([]byte, error)</c>。读到流结束（Go 的 <c>io.EOF</c>）时抛
    /// <see cref="EndOfStreamException"/>（不是 <see cref="NoContentLengthException"/>）。
    /// </summary>
    public byte[] Read()
    {
        long contentLength = 0;

        while (true)
        {
            // Go 的 `line` 是 ReadBytes('\n') 的**原始字节（含行尾）**：空行判定与错误消息里的 %q 都依赖它。
            var line = ReadLine();
            if (line is null)
            {
                throw new EndOfStreamException();
            }

            // Go: bytes.Equal(line, []byte("\r\n")) —— 恰好 "\r\n" 才算头部结束（裸 "\n" 会走到下面的缺冒号分支）。
            if (line == "\r\n")
            {
                break;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                // Go: fmt.Errorf("%w: %q", ErrInvalidHeader, line) —— %q 含行尾（如 "Nope\r\n"）。
                throw new InvalidHeaderException(GoQuote(line));
            }

            if (line.AsSpan(0, colon).SequenceEqual("Content-Length"))
            {
                var text = line.AsSpan(colon + 1).Trim().ToString();
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength))
                {
                    // Go: fmt.Errorf("%w: parse error: %w", ErrInvalidContentLength, err) —— err 是 strconv 的 NumError。
                    throw new InvalidContentLengthException($"parse error: {ParseIntError(text)}");
                }

                if (contentLength < 0)
                {
                    throw new InvalidContentLengthException($"negative value {contentLength.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        if (contentLength <= 0)
        {
            throw new NoContentLengthException();
        }

        var data = new byte[contentLength];
        var offset = 0;
        while (offset < data.Length)
        {
            var read = _stream.Read(data, offset, data.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }

        return data;
    }

    /// <summary>
    /// 读一行：返回 Go <c>bufio.Reader.ReadBytes('\n')</c> 的<b>原始字节</b>（含行尾 <c>\n</c> 与可能的前导
    /// <c>\r</c>，与 Go 一样保留行尾）；没有 <c>\n</c> 就 EOF 时返回 <c>null</c>（Go 的 <c>io.EOF</c>，残行丢掉）。
    /// 非 UTF-8 字节按 U+FFFD 解码（只在错误消息的 %q 里可见；Go 会输出 `\xff`，见 <see cref="GoQuote"/>）。
    /// </summary>
    private string? ReadLine()
    {
        var builder = new List<byte>(16);
        while (true)
        {
            var b = _stream.ReadByte();
            if (b < 0)
            {
                // EOF：残行按 Go 丢掉（返回 null = io.EOF），不当作一行头。
                return null;
            }

            builder.Add((byte)b);
            if (b == '\n')
            {
                return Encoding.UTF8.GetString(builder.ToArray());
            }
        }
    }

    /// <summary>
    /// Go 的 <c>fmt</c> 对字符串/字节切片用 <c>%q</c> 时的形状（= <c>strconv.Quote</c>）：<c>"</c>/<c>\</c> 转义，
    /// <c>\a \b \f \n \r \t \v</c> 用字母转义，其它 ASCII 控制字符用 <c>\xHH</c>，可打印字符（含非 ASCII）原样。
    /// 偏差：非 ASCII 里不可打印的符文 Go 会写 <c>\uXXXX</c>、非法 UTF-8 字节 Go 会写 <c>\xHH</c>，这里原样输出
    /// （先按 UTF-8 解码）——只有畸形头行才会踩到。
    /// </summary>
    private static string GoQuote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\a': builder.Append("\\a"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\v': builder.Append("\\v"); break;
                default:
                    if (c < 0x20 || c == 0x7F)
                    {
                        builder.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// Go 的 <c>strconv.ParseInt(s, 10, 64)</c> 失败时的错误文本（<c>strconv.NumError.Error()</c>：
    /// <c>strconv.ParseInt: parsing "&lt;s&gt;": invalid syntax | value out of range</c>）。C# 侧没有 strconv，
    /// 这里按 Go 的判定复刻：先看是不是带符号的十进制数字串（不是 → invalid syntax），是但 C# 的
    /// <c>long.TryParse</c> 仍失败 → 越界（value out of range）。空串因此是 invalid syntax。
    /// </summary>
    private static string ParseIntError(string text)
    {
        var digits = text.Length != 0 && (text[0] is '+' or '-') ? text.AsSpan(1) : text.AsSpan();
        var allDigits = digits.Length != 0;
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
            {
                allDigits = false;
                break;
            }
        }

        var reason = allDigits ? "value out of range" : "invalid syntax";
        return $"strconv.ParseInt: parsing {GoQuote(text)}: {reason}";
    }
}

/// <summary>Go: <c>type Writer struct</c>（带 Content-Length 头的 JSON-RPC 基础协议写侧）。</summary>
public sealed class Writer
{
    private readonly Stream _stream;

    private Writer(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>Go: <c>func NewWriter(w io.Writer) *Writer</c>。</summary>
    public static Writer NewWriter(Stream stream) => new(stream);

    /// <summary>Go: <c>func (w *Writer) Write(data []byte) error</c>（写完即 Flush）。</summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        var header = Encoding.ASCII.GetBytes($"Content-Length: {data.Length}\r\n\r\n");
        _stream.Write(header, 0, header.Length);
        _stream.Write(data);
        _stream.Flush();
    }
}
