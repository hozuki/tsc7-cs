// ported from: internal/lsp/lsproto/baseproto_test.go
//
// 移植映射：
//   * `lsproto.NewBaseReader/NewBaseWriter` → C# 的 `BaseReader.NewBaseReader` / `BaseWriter.NewBaseWriter`
//     （lsproto 的 baseproto 是对 internal/jsonrpc 那份 `TypeScript.JsonRpc.Reader/Writer` 的包装，
//     工厂命名沿用仓库既有约定 `Reader.NewReader`）。
//   * Go 的 `io.Reader`/`io.Writer` → C# 的 `Stream`；Go 的 `bytes.NewReader(data)` → `new MemoryStream(data)`；
//     Go 的 `bytes.Buffer` → `MemoryStream`（比期望值就是它的 `ToArray()`）。
//   * Go 的哨兵错误（`ErrNoContentLength`/`ErrInvalidContentLength`/`ErrInvalidHeader`/`io.EOF`）→
//     C# 的异常类型（`NoContentLengthException`/`InvalidContentLengthException`/`InvalidHeaderException`/
//     `EndOfStreamException`，定义在 TypeScript.JsonRpc，见那个包的文件头）。
//   * `t.Run(name, …)` 表驱动 → 本文件的 `RunCase(name, …)` + 用例表（xunit 的每条 [Fact] 各自并行）。
//
// 消息文本按 Go 逐字对齐（`JsonRpc/BaseProto.cs` 复刻了 Go 的判定与转义），逐条记下依据：
//   1) "invalid header"：`fmt.Errorf("%w: %q", ErrInvalidHeader, line)` 里 `line` 是 ReadBytes('\n') 的原始
//      字节（含行尾 CRLF），`%q` 把它转义成字面 `\r\n` ⇒ `jsonrpc: invalid header: "Nope\r\n"`。
//   2) "missing content length"：`%w` 透传 `strconv.ParseInt` 的错误文本（空串 → `invalid syntax`）⇒
//      `jsonrpc: invalid content length: parse error: strconv.ParseInt: parsing "": invalid syntax`。
//   3) "too long content length"：Go 是 `jsonrpc: read content: unexpected EOF`（包装 io.ErrUnexpectedEOF），
//      C# 侧直接抛 `EndOfStreamException`，本文件只断言类型（.NET 自己的消息文本不作为契约）。
using TypeScript.JsonRpc;
using TypeScript.LsProto;
using Xunit;

namespace TypeScript.Tests;

public class BaseProtoTests
{
    /// <summary>Go 的 <c>t.Run(name, …)</c>：失败时把用例名加进消息。</summary>
    private static void RunCase(string name, Action run)
    {
        try
        {
            run();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"[{name}] {error.Message}", error);
        }
    }

    [Fact]
    public void TestBaseReader()
    {
        var tests = new (string Name, byte[] Input, byte[]? Value, Type? ErrorType, string? ErrorMessage)[]
        {
            ("empty",
                "Content-Length: 0\r\n\r\n"u8.ToArray(),
                null, typeof(NoContentLengthException), "jsonrpc: no content length"),
            // Go: `{name: "early end", input: []byte("oops"), err: "EOF"}` —— 头读到一半就 EOF（`ReadBytes('\n')`
            // 返回 io.EOF，jsonrpc/baseproto.go:37-44）⇒ 残行必须丢掉、返回 io.EOF，C# 侧即 EndOfStreamException
            // （Ipc/ConnSync 的干净关闭依赖它；`JsonRpc/BaseProto.cs` 的 ReadLine 已按此对齐）。
            ("early end",
                "oops"u8.ToArray(),
                null, typeof(EndOfStreamException), null),
            ("negative length",
                "Content-Length: -1\r\n\r\n"u8.ToArray(),
                null, typeof(InvalidContentLengthException), "jsonrpc: invalid content length: negative value -1"),
            ("invalid content",
                "Content-Length: 1\r\n\r\n{"u8.ToArray(),
                "{"u8.ToArray(), null, null),
            ("valid content",
                "Content-Length: 2\r\n\r\n{}"u8.ToArray(),
                "{}"u8.ToArray(), null, null),
            ("extra header values",
                "Content-Length: 2\r\nExtra: 1\r\n\r\n{}"u8.ToArray(),
                "{}"u8.ToArray(), null, null),
            ("too long content length",
                "Content-Length: 100\r\n\r\n{}"u8.ToArray(),
                null, typeof(EndOfStreamException), null),
            // Go 的消息：`jsonrpc: invalid content length: parse error: %w`，`%w` 是 `strconv.ParseInt` 的错误文本
            // （jsonrpc/baseproto.go:46-56）⇒ C# 侧逐字复刻同一判定/文本（空串 → invalid syntax）。
            ("missing content length",
                "Content-Length: \r\n\r\n{}"u8.ToArray(),
                null, typeof(InvalidContentLengthException),
                "jsonrpc: invalid content length: parse error: strconv.ParseInt: parsing \"\": invalid syntax"),
            // Go 的消息：`fmt.Errorf("%w: %q", ErrInvalidHeader, line)`，`line` 是 `ReadBytes('\n')` 的原始字节
            // （保留行尾 CRLF，`%q` 转义成字面 `\r\n`）⇒ `jsonrpc: invalid header: "Nope\r\n"`。
            ("invalid header",
                "Nope\r\n\r\n{}"u8.ToArray(),
                null, typeof(InvalidHeaderException), "jsonrpc: invalid header: \"Nope\\r\\n\""),
        };

        foreach (var (name, input, value, errorType, errorMessage) in tests)
        {
            RunCase(name, () =>
            {
                var reader = BaseReader.NewBaseReader(new MemoryStream(input));
                if (errorType is not null)
                {
                    var error = Assert.Throws(errorType, () =>
                    {
                        reader.Read();
                    });
                    if (errorMessage is not null)
                    {
                        Assert.Equal(errorMessage, error.Message);
                    }

                    return;
                }

                Assert.Equal(value, reader.Read());
            });
        }
    }

    [Fact]
    public void TestBaseReaderMultipleReads()
    {
        var data = "Content-Length: 4\r\n\r\n1234"u8.ToArray()
            .Concat("Content-Length: 2\r\n\r\n{}"u8.ToArray())
            .ToArray();
        var reader = BaseReader.NewBaseReader(new MemoryStream(data));

        Assert.Equal("1234"u8.ToArray(), reader.Read());
        Assert.Equal("{}"u8.ToArray(), reader.Read());
        Assert.Throws<EndOfStreamException>(() =>
        {
            reader.Read();
        });
    }

    [Fact]
    public void TestBaseWriter()
    {
        var tests = new (string Name, byte[] Value, byte[] Input)[]
        {
            ("empty", "{}"u8.ToArray(), "Content-Length: 2\r\n\r\n{}"u8.ToArray()),
            ("bigger object",
                "{\"key\":\"value\"}"u8.ToArray(),
                "Content-Length: 15\r\n\r\n{\"key\":\"value\"}"u8.ToArray()),
        };

        foreach (var (name, value, input) in tests)
        {
            RunCase(name, () =>
            {
                var stream = new MemoryStream();
                var writer = BaseWriter.NewBaseWriter(stream);
                writer.Write(value);
                Assert.Equal(input, stream.ToArray());
            });
        }
    }

    [Fact]
    public void TestBaseWriterWriteError()
    {
        var writer = BaseWriter.NewBaseWriter(new ErrorStream());
        var error = Assert.Throws<IOException>(() =>
        {
            writer.Write("{}"u8);
        });
        Assert.Equal("test error", error.Message);
    }

    /// <summary>Go 的 <c>errorWriter</c>：Write 直接返回错误（C# 侧是抛 IOException）。</summary>
    private sealed class ErrorStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("test error");
    }
}
