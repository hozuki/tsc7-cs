// ported from: internal/contentmapper/host_test.go（测试支撑：假 spawner、内存管道、可复用的假 mapper）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `net.Pipe()` → 本仓库 Ipc 测试已落地的 `TestPipes.Create()`（tests/.../Ipc/TestPipes.cs）。
// 2) Go 的 `io.ReadWriteCloser` 嵌入 + 覆写一个方法（countingReadWriteCloser / exitedReadWriteCloser /
//    exitOnCloseReadWriteCloser / closeSignalReadWriteCloser）→ C# 不能嵌入，这里用 `DelegatingStream` 基类；
//    覆写的是 `Dispose(bool)`（`Stream.Close()` 默认就转调它），Go 覆写的是 `Close()`。
// 3) Go 的 `sync/atomic.Int32` → `Interlocked` + 属性读取。
// 4) Go 的 `interface{ handlesProjects() }` 类型断言 → 标记接口 `IHandlesProjects`。
// 5) Go 的 `chan struct{}`（阻塞/放行、关闭信号）→ `ManualResetEventSlim`。
// 6) Go 的 `recordingMapper` 里 `json.Marshal(p.CompilerOptions)`（字段是 json.Value，原样写出）→ C# 的
//    `p.CompilerOptions` 就是原始字节，直接按 UTF-8 解码（**不能**再 Marshal 一次，那会写成 base64）。
// 7) 协议结构体在 C# 里是显式构造（Go 的复合字面量同形）。
using System.Text;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using static TypeScript.ContentMapper.ContentMapper;
using static TypeScript.Core.Core;
using static TypeScript.Json.Json;
using static TypeScript.SpanMap.SpanMap;
using static TypeScript.TSPath.TSPath;
using ProtocolDiagnostic = TypeScript.ContentMapper.Diagnostic;

namespace TypeScript.Tests;

/// <summary>Go: <c>interface{ handlesProjects() }</c>（标记接口 + 类型断言）。</summary>
internal interface IHandlesProjects;

/// <summary>测试用的透传 Stream：Go 靠 `io.ReadWriteCloser` 嵌入，C# 靠继承（覆写 <see cref="Dispose(bool)"/>）。</summary>
internal class DelegatingStream(Stream inner) : Stream
{
    protected Stream Inner { get; } = inner;

    public override bool CanRead => Inner.CanRead;

    public override bool CanSeek => Inner.CanSeek;

    public override bool CanWrite => Inner.CanWrite;

    public override long Length => Inner.Length;

    public override long Position
    {
        get => Inner.Position;
        set => Inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => Inner.Read(buffer);

    public override void Write(byte[] buffer, int offset, int count) => Inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => Inner.Write(buffer);

    public override void Flush() => Inner.Flush();

    public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);

    public override void SetLength(long value) => Inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Go: <c>countingReadWriteCloser</c>（统计 Close 次数）。</summary>
internal sealed class CountingStream(Stream inner, FakeSpawner spawner) : DelegatingStream(inner)
{
    private int _closed;

    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _closed, 1) == 0)
        {
            spawner.CountClose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Go: <c>exitedReadWriteCloser</c>（进程已经退出，退出码固定）。</summary>
internal sealed class ExitedStream(Stream inner, int exitCode) : DelegatingStream(inner), IProcessExitState
{
    public bool TryExitCode(out int code)
    {
        code = exitCode;
        return true;
    }
}

/// <summary>Go: <c>exitOnCloseReadWriteCloser</c>（Close 之后才算退出）。</summary>
internal sealed class ExitOnCloseStream(Stream inner) : DelegatingStream(inner), IProcessExitState
{
    private int _exited;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Volatile.Write(ref _exited, 1);
        }

        base.Dispose(disposing);
    }

    public bool TryExitCode(out int code)
    {
        code = 1;
        return Volatile.Read(ref _exited) != 0;
    }
}

/// <summary>Go: <c>closeSignalReadWriteCloser</c>（Close 时发一个信号）。</summary>
internal sealed class CloseSignalStream(Stream inner, ManualResetEventSlim closed) : DelegatingStream(inner)
{
    private int _signalled;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Interlocked.Exchange(ref _signalled, 1) == 0)
        {
            closed.Set();
        }
    }
}

/// <summary>Go: <c>fakeSpawner</c>（每次 spawn 都用内存管道接一个进程内 mapper，并统计 spawn/close）。</summary>
internal sealed class FakeSpawner : Spawner
{
    private int _spawns;
    private int _closes;

    /// <summary>Go: <c>handler ipc.Handler</c>（nil 时用 <see cref="FakeMapper"/>）。</summary>
    public Handler? Handler;

    public int Spawns => Volatile.Read(ref _spawns);

    public int Closes => Volatile.Read(ref _closes);

    internal void CountClose() => Interlocked.Increment(ref _closes);

    public Stream Spawn(string[] command, string dir, Stream stderr)
    {
        Interlocked.Increment(ref _spawns);
        var handler = Handler ?? new FakeMapper();
        if (handler is not IHandlesProjects)
        {
            handler = new NoOpProjectMapper(handler);
        }

        var (client, server) = TestPipes.Create();
        var conn = AsyncConn.NewAsyncConn(server, handler);
        // Go: `go func() { _ = ipc.NewAsyncConn(server, handler).Run(context.Background()) }()`。读循环是长期
        // 阻塞的（Go 的 goroutine 无成本），C# 侧按项目约定用 LongRunning 专用线程，避免占线程池线程。
        _ = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    conn.Run(Context.Background);
                }
                catch (Exception)
                {
                    // Go: `go func() { _ = ….Run(context.Background()) }()`（错误被丢弃）。
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return new CountingStream(client, this);
    }
}

/// <summary>Go: <c>noOpProjectMapper</c>（把 openProject/closeProject 用空结果答掉，其余转给内层 handler）。</summary>
internal sealed class NoOpProjectMapper(Handler inner) : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params) => method switch
    {
        MethodOpenProject => new OpenProjectResult(),
        MethodCloseProject => null,
        _ => inner.HandleRequest(ctx, method, @params),
    };

    public void HandleNotification(Context ctx, string method, byte[]? @params) => inner.HandleNotification(ctx, method, @params);
}

/// <summary>Go: <c>fakeMapper</c>（原样变换内容，并报告一个诊断）。</summary>
internal sealed class FakeMapper : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return new InitializeResult { PositionEncoding = PositionEncoding.UTF8, DiagnosticSource = "vue" };
            case MethodTransform:
                var p = UnmarshalNew<TransformParams>(@params ?? []);
                var mappings = New(
                [
                    new Segment
                    {
                        VirtualEnd = new TextPos(p.Content.Length),
                        OriginalEnd = new TextPos(p.Content.Length),
                        Kind = Kind.Verbatim,
                    },
                ]).Marshal();
                return new TransformResult
                {
                    Text = p.Content,
                    Extension = ".ts",
                    Mappings = mappings,
                    Diagnostics =
                    [
                        new ProtocolDiagnostic
                        {
                            MessageText = "boom",
                            Start = 0,
                            Length = Math.Min(3, p.Content.Length),
                            Code = 9999,
                        },
                    ],
                };
            default:
                throw new Exception($"unexpected method {method}");
        }
    }

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}

/// <summary>Go: <c>responseMapper</c>（transform 的返回值由回调决定）。</summary>
internal sealed class ResponseMapper(Func<TransformParams, object?> response) : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return new InitializeResult { PositionEncoding = PositionEncoding.UTF8, DiagnosticSource = "mapper" };
            case MethodTransform:
                var p = UnmarshalNew<TransformParams>(@params ?? []);
                return response(p);
            default:
                throw new Exception($"unexpected method {method}");
        }
    }

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}

/// <summary>Go: <c>protocolDiagnosticDirectives</c>。</summary>
internal static class ProtocolDiagnostics
{
    internal static DiagnosticDirectives Directives(
        MappedDiagnosticDirective[] directives,
        params UnusedExpectDirectiveDiagnostic[] unused) =>
        new() { UnusedExpectDirectiveDiagnostics = unused, Directives = directives };
}

/// <summary>Go: <c>unicodeMapper</c>（按协商的坐标空间发非 ASCII 位置）。</summary>
internal sealed class UnicodeMapper(PositionEncoding encoding, string? source = null) : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                var p = UnmarshalNew<InitializeParams>(@params ?? []);
                var offered = p.PositionEncodings.Contains(encoding);
                if (!offered && (encoding == PositionEncoding.UTF8 || encoding == PositionEncoding.UTF16))
                {
                    throw new Exception($"position encoding \"{encoding.String()}\" was not offered");
                }

                return new InitializeResult { PositionEncoding = encoding, DiagnosticSource = source ?? "mapper" };
            case MethodTransform:
                var t = UnmarshalNew<TransformParams>(@params ?? []);
                int emojiLength, textLength;
                switch (encoding.String())
                {
                    case "utf-8":
                        emojiLength = 2;
                        textLength = 3;
                        break;
                    case "utf-16":
                        emojiLength = 1;
                        textLength = 2;
                        break;
                    default:
                        return new TransformResult { Text = t.Content, Extension = ".ts" };
                }

                // Go: json.Marshal([][5]int{…})（手写等价的紧凑数组）。
                var mappings = Encoding.UTF8.GetBytes(
                    $"[[0,{emojiLength},0,{emojiLength},{(int)Kind.Verbatim}],"
                    + $"[{emojiLength},{textLength - emojiLength},{emojiLength},{textLength - emojiLength},{(int)Kind.Verbatim}]]");
                return new TransformResult
                {
                    Text = t.Content,
                    Extension = ".ts",
                    Mappings = mappings,
                    DiagnosticDirectives = ProtocolDiagnostics.Directives(
                    [
                        new MappedDiagnosticDirective
                        {
                            OriginalStart = emojiLength,
                            OriginalLength = textLength - emojiLength,
                            VirtualStart = emojiLength,
                            VirtualEnd = textLength,
                            Policy = DiagnosticDirectivePolicy.Ignore,
                        },
                    ]),
                    Diagnostics =
                    [
                        new ProtocolDiagnostic
                        {
                            MessageText = "after non-ASCII character",
                            Start = emojiLength,
                            Length = textLength - emojiLength,
                            Code = 1001,
                        },
                    ],
                };
            default:
                throw new Exception($"unexpected method {method}");
        }
    }

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}

/// <summary>Go: <c>invalidDiagnosticMapper</c>（诊断位置落在码点中间）。</summary>
internal sealed class InvalidDiagnosticMapper(PositionEncoding encoding) : Handler
{
    public object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                return new InitializeResult { PositionEncoding = encoding, DiagnosticSource = "mapper" };
            case MethodTransform:
                return new TransformResult
                {
                    Extension = ".ts",
                    Diagnostics =
                    [
                        new ProtocolDiagnostic { MessageText = "invalid boundary", Start = 1, Code = 1002 },
                    ],
                };
            default:
                throw new Exception($"unexpected method {method}");
        }
    }

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }
}

/// <summary>Go: <c>recordingMapper</c>（记录项目配置与生命周期请求）。</summary>
internal class RecordingMapper : Handler, IHandlesProjects
{
    private readonly object _mu = new();

    public string Received = "";
    public string ReceivedOptions = "";
    public string ReceivedLocale = "";
    public List<string> ProjectHandles = [];
    public List<string> ClosedHandles = [];
    public string TransformHandle = "";
    public string TransformParams = "";
    public string[] WatchedFiles = [];
    public string? ConfigIdentity;
    public bool DynamicConfig;
    public OptionDiagnosticResult[] OptionDiagnostics = [];

    public virtual object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        switch (method)
        {
            case MethodInitialize:
                var init = UnmarshalNew<InitializeParams>(@params ?? []);
                lock (_mu)
                {
                    ReceivedLocale = init.Locale;
                }

                return new InitializeResult { PositionEncoding = PositionEncoding.UTF8, DiagnosticSource = "mapper" };
            case MethodOpenProject:
                var open = UnmarshalNew<OpenProjectParams>(@params ?? []);
                lock (_mu)
                {
                    ProjectHandles.Add(open.ProjectHandle);
                    Received = Encoding.UTF8.GetString(open.CompilerOptions ?? []);
                    ReceivedOptions = Encoding.UTF8.GetString(open.Options ?? []);
                }

                string[]? watchedFiles;
                string? configIdentityOverride;
                OptionDiagnosticResult[] optionDiagnostics;
                lock (_mu)
                {
                    watchedFiles = WatchedFiles.Length == 0 ? null : WatchedFiles;
                    configIdentityOverride = ConfigIdentity;
                    optionDiagnostics = OptionDiagnostics;
                }

                if (!DynamicConfig && watchedFiles is null && configIdentityOverride is null && optionDiagnostics.Length == 0)
                {
                    return new OpenProjectResult();
                }

                if (DynamicConfig && watchedFiles is null)
                {
                    watchedFiles = [CombinePaths(GetDirectoryPath(open.ConfigFileName), "mapper.config.js")];
                }

                var configIdentity = "";
                if (DynamicConfig)
                {
                    configIdentity = "config:" + Encoding.UTF8.GetString(open.Options ?? []);
                }

                if (configIdentityOverride is not null)
                {
                    configIdentity = configIdentityOverride;
                }

                return new OpenProjectResult
                {
                    ConfigIdentity = configIdentity,
                    WatchedFiles = watchedFiles,
                    OptionDiagnostics = optionDiagnostics.Length == 0 ? null : optionDiagnostics,
                };
            case MethodCloseProject:
                var close = UnmarshalNew<CloseProjectParams>(@params ?? []);
                lock (_mu)
                {
                    ClosedHandles.Add(close.ProjectHandle);
                }

                return null;
            case MethodTransform:
                var transform = UnmarshalNew<TransformParams>(@params ?? []);
                lock (_mu)
                {
                    TransformHandle = transform.ProjectHandle;
                    TransformParams = Encoding.UTF8.GetString(@params ?? []);
                }

                return new TransformResult { Text = transform.Content, Extension = ".ts" };
            default:
                throw new Exception($"unexpected method {method}");
        }
    }

    public void HandleNotification(Context ctx, string method, byte[]? @params)
    {
    }

    public List<string> ProjectHandlesSnapshot()
    {
        lock (_mu)
        {
            return [.. ProjectHandles];
        }
    }

    public List<string> ClosedHandlesSnapshot()
    {
        lock (_mu)
        {
            return [.. ClosedHandles];
        }
    }
}

/// <summary>Go: <c>blockingMapper</c>（transform 阻塞到测试放行）。</summary>
internal sealed class BlockingMapper(ManualResetEventSlim started, ManualResetEventSlim proceed) : RecordingMapper
{
    public override object? HandleRequest(Context ctx, string method, byte[]? @params)
    {
        if (method == MethodTransform)
        {
            started.Set();
            proceed.Wait();
        }

        return base.HandleRequest(ctx, method, @params);
    }
}
