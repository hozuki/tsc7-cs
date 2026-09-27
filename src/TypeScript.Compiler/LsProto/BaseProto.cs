// ported from: internal/lsp/lsproto/baseproto.go
//
// C# 映射与偏离：
// 1) Go 的 `BaseReader`/`BaseWriter` 是**内嵌** `*jsonrpc.Reader`/`*jsonrpc.Writer` 的包装（向后兼容），
//    内嵌会让 `Read`/`Write` 提升到外层。C# 没有内嵌提升，且 jsonrpc 侧的两个类型是 `sealed`
//    （src/TypeScript.Compiler/JsonRpc/BaseProto.cs，本切片不改它），**不能**用继承，因此这里用组合 +
//    显式转发 `Read()`/`Write(...)`：调用点写法（`r.Read()` / `w.Write(data)`）与 Go 的提升结果一致。
//    `NewBaseReader`/`NewBaseWriter` 按仓库里既有构造函数先例（`Reader.NewReader`/`Writer.NewWriter`）
//    放在类型上做静态工厂。
// 2) Go 的 `io.Reader`/`io.Writer` → C# 的 `Stream`（与 jsonrpc 侧一致）。
using TypeScript.JsonRpc;

namespace TypeScript.LsProto;

/// <summary>Go: <c>type BaseReader struct { *jsonrpc.Reader }</c>（向后兼容的包装）。</summary>
public sealed class BaseReader
{
    /// <summary>Go 的内嵌字段 <c>*jsonrpc.Reader</c>。</summary>
    private readonly Reader _reader;

    private BaseReader(Stream stream)
    {
        _reader = Reader.NewReader(stream);
    }

    /// <summary>Go: <c>func NewBaseReader(r io.Reader) *BaseReader</c>。</summary>
    public static BaseReader NewBaseReader(Stream stream) => new(stream);

    /// <summary>Go 的内嵌提升：<c>func (r *Reader) Read() ([]byte, error)</c>。</summary>
    public byte[] Read() => _reader.Read();
}

/// <summary>Go: <c>type BaseWriter struct { *jsonrpc.Writer }</c>（向后兼容的包装）。</summary>
public sealed class BaseWriter
{
    /// <summary>Go 的内嵌字段 <c>*jsonrpc.Writer</c>。</summary>
    private readonly Writer _writer;

    private BaseWriter(Stream stream)
    {
        _writer = Writer.NewWriter(stream);
    }

    /// <summary>Go: <c>func NewBaseWriter(w io.Writer) *BaseWriter</c>。</summary>
    public static BaseWriter NewBaseWriter(Stream stream) => new(stream);

    /// <summary>Go 的内嵌提升：<c>func (w *Writer) Write(data []byte) error</c>。</summary>
    public void Write(ReadOnlySpan<byte> data) => _writer.Write(data);
}
