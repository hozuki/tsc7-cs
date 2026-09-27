// ported from: internal/ipc/transport.go（+ transport_unix.go / transport_windows.go 的构建标签分支）
//
// C# 映射与偏离（逐条）：
// 1) Go 的 `io.ReadWriteCloser` → C# 的 `Stream`（字节语义）；`Accept() (io.ReadWriteCloser, error)` →
//    `Stream Accept()`（失败抛异常），`io.EOF`（StdioTransport 第二次 Accept）→ `EndOfStreamException`。
// 2) **构建标签 → 运行时判断**：Go 用 `//go:build windows` / `!windows` 分两个文件实现
//    `newPipeListener`/`GeneratePipePath`；C# 用 `OperatingSystem.IsWindows()` 在运行时二选一，
//    两条实现都保留在本文件：
//      - Windows：`System.IO.Pipes.NamedPipeServerStream`（Go 用 github.com/Microsoft/go-winio 的 ListenPipe）。
//        注意 .NET 会在"名字"前自己拼 `\\.\pipe\`，所以这里先把 Go 的完整 pipe 路径去掉该前缀；
//        且 .NET 的 NamedPipeServerStream 一次只服务一个连接实例，每次 Accept 都要新建一个（winio 的
//        listener 内部也是这么做的，连接建立后该实例就是连接本身）。
//      - Unix：`Socket(AddressFamily.Unix)` + `UnixDomainSocketEndPoint`（Go 用 net.Listen("unix", path)），
//        建立连接后返回 `NetworkStream`（Go 返回 *net.UnixConn）。同样先删掉可能残留的 socket 文件。
// 3) Go 的 `listener.Close()` → C# 的 `Socket.Dispose()`/待连接管道流 Dispose（后者用于打断阻塞中的
//    `WaitForConnection`）；关闭后再 Accept 抛 `IOException`（Go 返回 net.ErrClosed）。
// 4) Go 的 `os.TempDir()` → C# 的 `Path.GetTempPath()`；Go 的 `path.Join` → `Path.Combine`
//    （Windows 分支不用它：直接拼 `\\.\pipe\` 前缀）。
// 5) Go 的匿名嵌入（`stdioConn{io.Reader; io.Writer; …}`）→ C# 里显式把读写转发给两个 Stream；
//    `Close()` 的"先 stdin 后 stdout、返回第一个错误"→ C# 里两个都关、stdin 的异常优先抛出。
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

namespace TypeScript.Ipc;

/// <summary>Go: <c>type Transport interface</c>（接受 API 客户端的连接）。</summary>
public interface Transport
{
    /// <summary>Go: <c>Accept() (io.ReadWriteCloser, error)</c>。等待并返回下一个连接。</summary>
    Stream Accept();

    /// <summary>Go: <c>Close() error</c>。停止接受新连接。</summary>
    void Close();
}

/// <summary>
/// Go: <c>type PipeTransport struct</c>（监听 Unix domain socket 或 Windows named pipe）。
/// </summary>
public sealed class PipeTransport : Transport
{
    private readonly IPipeListener _listener;

    private PipeTransport(IPipeListener listener)
    {
        _listener = listener;
    }

    /// <summary>
    /// Go: <c>func NewPipeTransport(path string) (*PipeTransport, error)</c>。
    /// Windows 建 named pipe、Unix 建 unix socket（见文件头第 2 条）。
    /// </summary>
    public static PipeTransport NewPipeTransport(string path)
    {
        IPipeListener listener = OperatingSystem.IsWindows()
            ? new WindowsPipeListener(path)
            : new UnixSocketListener(path);
        return new PipeTransport(listener);
    }

    /// <summary>Go: <c>func (t *PipeTransport) Accept() (io.ReadWriteCloser, error)</c>。</summary>
    public Stream Accept() => _listener.Accept();

    /// <summary>Go: <c>func (t *PipeTransport) Close() error</c>。</summary>
    public void Close() => _listener.Close();

    /// <summary>Go: <c>func (t *PipeTransport) Path() string</c>（= listener.Addr().String()，即给定的路径）。</summary>
    public string Path() => _listener.Address;

    /// <summary>对应 Go 的 <c>net.Listener</c>（两种平台各一个实现）。</summary>
    private interface IPipeListener
    {
        string Address { get; }

        Stream Accept();

        void Close();
    }

    /// <summary>Windows：go-winio 的 ListenPipe 的 .NET 对应物。</summary>
    private sealed class WindowsPipeListener : IPipeListener
    {
        /// <summary>.NET 自己会加 <c>\\.\pipe\</c> 前缀，这里的名字必须是去掉前缀后的部分。</summary>
        private readonly string _pipeName;

        private readonly object _mu = new();

        /// <summary>正在等待连接的管道实例（Close 时 Dispose 它会打断阻塞中的 WaitForConnection）。</summary>
        private NamedPipeServerStream? _pending;

        private bool _closed;

        internal WindowsPipeListener(string path)
        {
            Address = path;
            const string prefix = @"\\.\pipe\";
            _pipeName = path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : path;
        }

        public string Address { get; }

        public Stream Accept()
        {
            NamedPipeServerStream server;
            lock (_mu)
            {
                if (_closed)
                {
                    // Go: listener 关闭后 Accept 返回 net.ErrClosed。
                    throw new IOException("ipc: pipe transport closed");
                }

                _pending ??= NewServer();
                server = _pending;
            }

            server.WaitForConnection();

            lock (_mu)
            {
                if (ReferenceEquals(_pending, server))
                {
                    // 连接建立后，这个实例本身就是连接（与 winio 的 listener 行为一致）。
                    _pending = null;
                }
            }

            return server;
        }

        public void Close()
        {
            NamedPipeServerStream? pending;
            lock (_mu)
            {
                _closed = true;
                pending = _pending;
                _pending = null;
            }

            pending?.Dispose();
        }

        private NamedPipeServerStream NewServer() => new(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.None);
    }

    /// <summary>Unix：<c>net.Listen("unix", path)</c> 的 .NET 对应物。</summary>
    private sealed class UnixSocketListener : IPipeListener
    {
        private readonly Socket _socket;

        internal UnixSocketListener(string path)
        {
            Address = path;

            // Go: `_ = os.Remove(path)`（残留的 socket 文件会让 Bind 失败；错误一律忽略）。
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // 与 Go 的 `_ =` 一致。
            }

            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _socket.Bind(new UnixDomainSocketEndPoint(path));
            _socket.Listen(128);
        }

        public string Address { get; }

        public Stream Accept()
        {
            if (!_socket.IsBound)
            {
                // Go: listener 关闭后 Accept 返回 net.ErrClosed。
                throw new IOException("ipc: pipe transport closed");
            }

            var accepted = _socket.Accept();
            return new NetworkStream(accepted, ownsSocket: true);
        }

        public void Close() => _socket.Dispose();
    }
}

/// <summary>Go: <c>type StdioTransport struct</c>（把 stdin/stdout 包成只接受一次的传输）。</summary>
public sealed class StdioTransport : Transport
{
    /// <summary>Go: <c>stdin io.ReadCloser</c>。</summary>
    private readonly Stream _stdin;

    /// <summary>Go: <c>stdout io.WriteCloser</c>。</summary>
    private readonly Stream _stdout;

    /// <summary>Go: <c>used bool</c>。</summary>
    private bool _used;

    private StdioTransport(Stream stdin, Stream stdout)
    {
        _stdin = stdin;
        _stdout = stdout;
    }

    /// <summary>Go: <c>func NewStdioTransport(stdin io.ReadCloser, stdout io.WriteCloser) *StdioTransport</c>。</summary>
    public static StdioTransport NewStdioTransport(Stream stdin, Stream stdout) => new(stdin, stdout);

    /// <summary>Go: <c>func (t *StdioTransport) Accept() (io.ReadWriteCloser, error)</c>（第二次返回 io.EOF）。</summary>
    public Stream Accept()
    {
        if (_used)
        {
            throw new EndOfStreamException();
        }

        _used = true;
        return new StdioConn(_stdin, _stdout);
    }

    /// <summary>Go: <c>func (t *StdioTransport) Close() error</c>（Go 侧也是空实现）。</summary>
    public void Close()
    {
    }
}

/// <summary>Go: <c>type stdioConn struct</c>（读 stdin、写 stdout；Close 两个都关）。</summary>
internal sealed class StdioConn : Stream
{
    private readonly Stream _stdin;
    private readonly Stream _stdout;
    private bool _closed;

    internal StdioConn(Stream stdin, Stream stdout)
    {
        _stdin = stdin;
        _stdout = stdout;
    }

    public override bool CanRead => true;

    public override bool CanWrite => true;

    // Go 的 stdioConn 没有嵌入 io.Seeker（只有 Reader/Writer）。
    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _stdin.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _stdin.Read(buffer);

    public override int ReadByte() => _stdin.ReadByte();

    public override void Write(byte[] buffer, int offset, int count) => _stdout.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => _stdout.Write(buffer);

    public override void WriteByte(byte value) => _stdout.WriteByte(value);

    public override void Flush() => _stdout.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Go: <c>func (c *stdioConn) Close() error</c>——先关 stdin 再关 stdout，返回第一个错误。</summary>
    protected override void Dispose(bool disposing)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        if (!disposing)
        {
            base.Dispose(disposing);
            return;
        }

        Exception? stdinError = null;
        Exception? stdoutError = null;
        try
        {
            _stdin.Dispose();
        }
        catch (Exception e)
        {
            stdinError = e;
        }

        try
        {
            _stdout.Dispose();
        }
        catch (Exception e)
        {
            stdoutError = e;
        }

        base.Dispose(disposing);

        if (stdinError is not null)
        {
            ExceptionDispatchInfo.Capture(stdinError).Throw();
        }

        if (stdoutError is not null)
        {
            ExceptionDispatchInfo.Capture(stdoutError).Throw();
        }
    }
}

/// <summary>
/// Go 的 <c>ipc</c> 包级函数（本分片对应 transport_unix.go/transport_windows.go）。
/// </summary>
public static partial class Ipc
{
    /// <summary>
    /// Go: <c>func GeneratePipePath(name string) string</c>。Windows 是
    /// <c>\\.\pipe\&lt;name&gt;</c>，Unix 是 <c>path.Join(os.TempDir(), name)</c>（运行时判断，见文件头第 2/4 条）。
    /// </summary>
    public static string GeneratePipePath(string name) => OperatingSystem.IsWindows()
        ? @"\\.\pipe\" + name
        : Path.Combine(Path.GetTempPath(), name);
}
