// 测试专用辅助（Go 测试里用的是标准库的 net.Pipe，C# 的 BCL 没有等价物）。
//
// Go 的 `net.Pipe()` 是**同步的内存双向管道**：读阻塞到有数据或对端关闭，写阻塞到读者取走。
// 这里实现一个等价的阻塞式双向管道，可观察行为对齐 net.Pipe：
//   - 读：本端缓冲有数据就返回；没有数据且**对端已关**→ 返回 0（Go 的 io.EOF）；本端已关 → 抛 IOException
//     （Go 的 io.ErrClosedPipe）。
//   - 写：本端或对端已关 → 抛 IOException。
//   - 关闭：唤醒所有等待者；对端的后续读得到 EOF。
// **偏离**：net.Pipe 的写是无缓冲同步的（写到读者取走才返回），这里是带缓冲的——只影响时序，不影响断言。
using System.Diagnostics.CodeAnalysis;

namespace TypeScript.Tests;

/// <summary>Go 的 <c>net.Pipe()</c> 的替身。</summary>
internal static class TestPipes
{
    /// <summary>建一对互相连通的管道（Go: <c>client, server := net.Pipe()</c>）。</summary>
    internal static (Stream Client, Stream Server) Create()
    {
        var mu = new object();
        var client = new TestPipeEnd(mu);
        var server = new TestPipeEnd(mu);
        client._peer = server;
        server._peer = client;
        return (client, server);
    }

    private sealed class TestPipeEnd(object mu) : Stream
    {
        // **修的 bug**：两端原各自持一把锁，`Write` 却去 `PulseAll` **自己**的监视器，而读者 `Monitor.Wait`
        // 在对端监视器上 —— 写永远唤不醒读者（读循环永久阻塞，表现出来就是 content mapper 的 initialize
        // 5 秒超时）。`net.Pipe` 的两端本来就是同一条管道，这里改成两端共用一把锁（同时修正了
        // `Dispose` 关掉对端后唤不醒对端读者的同一问题）。
        // 对端写入、本端读取的字节（<see cref="_head"/> 之前的已消费）。
        private readonly List<byte> _inbound = new();

        private int _head;
        private bool _closed;
        private bool _peerClosed;

        [AllowNull]
        internal TestPipeEnd _peer;

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

                    if (_closed)
                    {
                        throw new IOException("io: read/write on closed pipe");
                    }

                    if (_peerClosed)
                    {
                        // Go: 对端关闭且没有数据 → io.EOF（读返回 0）。
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
