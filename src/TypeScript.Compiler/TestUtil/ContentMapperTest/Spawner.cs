// ported from: internal/testutil/contentmappertest/spawner.go
//
// C# 映射与偏离（逐条）：
// 1) `spawner`（未导出类型）→ `SpawnerImpl`：**不能**直接叫 `Spawner`——那会与本 package 要实现并返回的
//    `contentmapper.Spawner` 接口同名（本命名空间内的同名类型会遮蔽 using 进来的接口，`NewSpawner` 的返回
//    类型就再也写不出来），故加 `Impl` 后缀；Go 的未导出 → C# 的 `internal`。
// 2) Go 的 `client, server := net.Pipe()`（同步内存双工管道）在 BCL 里没有等价物 → 本文件内的
//    `SpawnerImpl.InMemoryPipe`（C# 侧新增；语义逐个照 tests/TypeScript.Compiler.Tests/Ipc/TestPipes.cs：
//    两端共用**同一把锁**，读阻塞到有数据或对端关闭，对端关闭且无数据 → 读返回 0（Go 的 io.EOF），
//    写向已关闭的一端 → 抛 IOException（Go 的 io.ErrClosedPipe）；写会唤醒读）。
//    **偏离**：net.Pipe 的写是无缓冲同步的（写到读者取走才返回），这里是带缓冲的——只影响时序，不影响断言。
// 3) Go 的 `go func() { _ = ipc.NewAsyncConn(server, handler).Run(context.Background()) }()` → C# 的
//    `Task.Factory.StartNew(…, TaskCreationOptions.LongRunning)`（阻塞的读循环占一条专用线程，与
//    contentmapper/HostImpl.cs 对同一形态的处理一致）；Go 丢弃 Run 的 error（`_ =`）→ C# 吞掉异常。
// 4) `Serve`/`NewSpawner`/`NewSpawnerWithProjectLifecycle` 是导出函数 → 同名静态类 `ContentMapperTest` 上的
//    公开静态方法（Go 的包级函数统一收进与包同名的静态类，见 architecture.md §2）。`Serve` 的 error →
//    `AsyncConn.Run` 抛异常。
using System.Diagnostics.CodeAnalysis;
using TypeScript.ContentMapper;
using TypeScript.Core;

using static TypeScript.TestUtil.ContentMapperTest.ContentMapperTest;

namespace TypeScript.TestUtil.ContentMapperTest;

public static partial class ContentMapperTest
{
    /// <summary>
    /// Go: <c>func Serve(ctx context.Context, rwc io.ReadWriteCloser) error</c>。在连接上驱动 transforming
    /// mapper，直到连接关闭或 ctx 被取消（Go 返回的 error → C# 由 <c>Run</c> 抛异常）。
    /// </summary>
    public static void Serve(Context ctx, Stream rwc) =>
        Ipc.AsyncConn.NewAsyncConn(rwc, new StaticProjectHandler(new Handler())).Run(ctx);

    /// <summary>Go: <c>func NewSpawner() contentmapper.Spawner</c>。测试用 mapper 的进程内 spawner。</summary>
    public static Spawner NewSpawner() => new SpawnerImpl(null);

    /// <summary>Go: <c>func NewSpawnerWithProjectLifecycle(lifecycle *ProjectLifecycle) contentmapper.Spawner</c>。
    /// 记录项目协议调用的进程内 spawner。</summary>
    public static Spawner NewSpawnerWithProjectLifecycle(ProjectLifecycle? lifecycle) => new SpawnerImpl(lifecycle);
}

/// <summary>
/// Go: <c>type spawner struct { lifecycle *ProjectLifecycle }</c>（未导出 → SpawnerImpl）。进程内 spawner：
/// 按 mapper 命令名造出 handler，用一对内存管道假装它是子进程的 stdio。
/// </summary>
internal sealed class SpawnerImpl(ProjectLifecycle? lifecycle) : Spawner
{
    /// <summary>Go: <c>lifecycle *ProjectLifecycle</c>（nil 表示不记录）。</summary>
    private readonly ProjectLifecycle? _lifecycle = lifecycle;

    /// <summary>Go: <c>func (s spawner) Spawn(command []string, dir string, stderr io.Writer) (io.ReadWriteCloser, error)</c>。</summary>
    public Stream Spawn(string[] command, string dir, Stream stderr)
    {
        var handler = HandlerForMapper(command, _lifecycle);
        if (command[0] != DynamicVerbatimMapper)
        {
            handler = new StaticProjectHandler(handler);
        }

        var (client, server) = InMemoryPipe.Create();
        var serverHandler = handler;

        // Go: `go func() { _ = ipc.NewAsyncConn(server, handler).Run(context.Background()) }()`。
        Task.Factory.StartNew(
            () =>
            {
                try
                {
                    Ipc.AsyncConn.NewAsyncConn(server, serverHandler).Run(Context.Background);
                }
                catch (Exception)
                {
                    // Go 的 `_ = …Run(…)`：读循环的错误被丢弃（对端关闭时这里会拿到 IOException）。
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return client;
    }

    /// <summary>
    /// C# 侧新增：Go 的 <c>net.Pipe()</c>（同步内存双工管道）的等价物。两端共用**同一把锁**——否则写端
    /// `Monitor.PulseAll` 的是自己的监视器、而读端 `Monitor.Wait` 在对端监视器上，写永远唤不醒读者。
    /// </summary>
    private static class InMemoryPipe
    {
        /// <summary>建一对互相连通的管道（Go: <c>client, server := net.Pipe()</c>）。</summary>
        internal static (Stream Client, Stream Server) Create()
        {
            var mu = new object();
            var client = new PipeEnd(mu);
            var server = new PipeEnd(mu);
            client.SetPeer(server);
            server.SetPeer(client);
            return (client, server);
        }

        private sealed class PipeEnd(object mu) : Stream
        {
            /// <summary>对端写入、本端尚未读走的字节（<see cref="_head"/> 之前的已消费）。</summary>
            private readonly List<byte> _inbound = [];

            private int _head;
            private bool _closed;
            private bool _peerClosed;

            [AllowNull]
            private PipeEnd _peer;

            /// <summary>C# 侧新增：接上对端（Go 的两端互相引用）。赋值必须在声明类型内部完成——
            /// 只在包含类型里写 `client._peer = …` 时编译器会报 CS0649「字段从未赋值」。</summary>
            internal void SetPeer(PipeEnd peer) => _peer = peer;

            public override bool CanRead => true;

            public override bool CanWrite => true;

            public override bool CanSeek => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

            public override int Read(Span<byte> buffer)
            {
                lock (mu)
                {
                    while (true)
                    {
                        if (_head < _inbound.Count)
                        {
                            var count = Math.Min(buffer.Length, _inbound.Count - _head);
                            for (var i = 0; i < count; i++)
                            {
                                buffer[i] = _inbound[_head + i];
                            }

                            _head += count;
                            if (_head == _inbound.Count)
                            {
                                _inbound.Clear();
                                _head = 0;
                            }

                            return count;
                        }

                        // 本端已关（Go 的 io.ErrClosedPipe）。
                        if (_closed)
                        {
                            throw new IOException("io: read/write on closed pipe");
                        }

                        // 对端关闭且没有数据 → Go 的 io.EOF（读返回 0）。
                        if (_peerClosed)
                        {
                            return 0;
                        }

                        Monitor.Wait(mu);
                    }
                }
            }

            public override int ReadByte()
            {
                Span<byte> one = stackalloc byte[1];
                return Read(one) == 0 ? -1 : one[0];
            }

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                lock (mu)
                {
                    if (_closed || _peerClosed)
                    {
                        throw new IOException("io: read/write on closed pipe");
                    }

                    foreach (var b in buffer)
                    {
                        _peer._inbound.Add(b);
                    }

                    Monitor.PulseAll(mu);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    lock (mu)
                    {
                        if (!_closed)
                        {
                            // 两端本来就是同一条管道：关掉本端 = 唤醒双方（对端后续读到 EOF、写入抛异常）。
                            _closed = true;
                            _peer._peerClosed = true;
                            Monitor.PulseAll(mu);
                        }
                    }
                }

                base.Dispose(disposing);
            }
        }
    }
}
