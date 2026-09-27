// ported from: internal/jsonrpc/baseproto.go
//
// C# 映射与偏离：
// 1) Go 的 `io.Reader`/`io.Writer` → C# 的 `Stream`（字节语义，与 Json 包一致）；Go 的 `bufio.Reader`/`bufio.Writer`
//    → `BufferedStream`（C# 的 Stream 本身不缓冲，ReadBytes 语义逐行照抄 Go 的 baseproto.go）。
// 2) Go 的 `var ErrX = errors.New(...)` → C# 的专用异常类型（Go 的 errors.Is → C# 的 catch；
//    见 docs/architecture.md §3.2 与 JsonRpc.cs 的文件头）。
// 3) Go 的 `Read() ([]byte, error)` 里 `io.EOF` 原样返回（调用方 `errors.Is(err, io.EOF)` 判连接正常结束）
//    → C# 的 `EndOfStreamException`（Stream 侧一致）**加上** `EndOfStream` 布尔属性：C# 的 read 返回 0
//    才是 Go 的 io.EOF，所以本方法的"正常结束"信号统一成 `EndOfStreamException`。
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
            var line = ReadLine();
            if (line is null)
            {
                throw new EndOfStreamException();
            }

            if (line.Length == 0)
            {
                break;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidHeaderException($"\"{line}\"");
            }

            if (line.AsSpan(0, colon).SequenceEqual("Content-Length"))
            {
                var text = line.AsSpan(colon + 1).Trim().ToString();
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength))
                {
                    throw new InvalidContentLengthException($"parse error: {text}");
                }

                if (contentLength < 0)
                {
                    throw new InvalidContentLengthException($"negative value {contentLength}");
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
    /// 读一行（含 <c>\n</c> 与可能的前导 <c>\r</c> 全部读入，返回去掉行尾符的文本）；流结束返回 <c>null</c>
    /// （对应 Go 的 <c>bufio.Reader.ReadBytes('\n')</c> 返回 <c>io.EOF</c>）。空行返回长度 0 的字符串
    /// （Go 的 <c>bytes.Equal(line, []byte("\r\n"))</c> 也把 <c>"\r\n"</c> 当空行——见下）。
    /// </summary>
    private string? ReadLine()
    {
        var builder = new List<byte>(16);
        while (true)
        {
            var b = _stream.ReadByte();
            if (b < 0)
            {
                return builder.Count == 0 ? null : TrimLineEnding(builder);
            }

            if (b == '\n')
            {
                return TrimLineEnding(builder);
            }

            builder.Add((byte)b);
        }
    }

    private static string TrimLineEnding(List<byte> bytes)
    {
        // Go 的读取保留行尾的 \r（`line` 含 "\r\n"），随后用 `bytes.Equal(line, []byte("\r\n"))` 判空行、
        // 用 `bytes.TrimSpace(value)` 去空白。C# 侧统一在校验前去掉尾部 \r，语义等价。
        if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
        {
            bytes.RemoveAt(bytes.Count - 1);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
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
