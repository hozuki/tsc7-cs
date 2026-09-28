// ported from: internal/lsp/server.go:226-228（`apiSessions`/`apiSessionsMu` 字段）
//   + internal/lsp/server.go:2280-2360（handleInitializeAPISession / generateAPIPipePath / removeAPISession）
//   + internal/lsp/server.go:1294（handler 注册）
//
// 本文件是 Server 的 partial 分片，落点是"API 会话"这一功能块（Go 侧这三段散在 server.go 里；
// C# 按功能切分文件，与 Server.Client.cs / Server.Initialize.cs 等分片一致）。
// 补齐它之后，`Server.cs` 文件头第 9 条（该功能未移植）作废，见那里的说明。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `internal/api.NewLSPSession(s.session, nil)` → `TypeScript.Api.Session.NewLSPSession(_session, null)`。
//    Go 侧该函数解引用 `projectSession.SnapshotHost`（nil 会 panic），所以 C# 的形参声明为**非空**
//    `Project.Session`，调用点用 `_session!`（此处已由 handler 表的前置检查保证非空：registerRequestHandler
//    在 `s._session == nil` 且方法不是 `initialize` 时抛 ServerNotInitialized）。
//
// 2) Go 的 `go func() { … }()` 里"Accept 连接 → 建 AsyncConn → Run(apiCtx)"整段 → C# 的**显式 64 MiB 栈线程**
//    （`_stackSize`，与 dispatch/write/read 循环同一形状，见 Server.cs 文件头第 1 条：API 会话要跑
//    bind/check/emit 这些深递归路径）。`defer` 链在 C# 里是 `try/finally`；Go 的 `recover()` → `catch (Exception)`。
//
// 3) Go 的 `defer apiSession.Close()` + `s.removeAPISession(apiSession.ID())` 在最外层 defer 里 →
//    C# 的 `finally` 中依次调用（顺序与 Go 一致：先 Close 再 remove）。
//
// 4) `context.WithCancel(s.backgroundCtx)` → `CancellationTokenSource.CreateLinkedTokenSource(_backgroundCtx.CancellationToken)`
//    （C# 侧既有的 `Core.Context.WithCancellation` 包装；见 Server.cs 的 Run）。
//
// 5) `ipc.NewPipeTransport` 的错误 → 抛异常（C# 侧 NewPipeTransport 抛），`fmt.Errorf("failed to create API transport: %w", err)`
//    → `ApiException`（文本同 Go 格式串，原始异常留在 InnerException）。
//
// 6) `rand.Uint64()`（math/rand）→ `Random.Shared.NextUInt64()`；`time.Now().UnixNano()` → `DateTime.UtcNow.Ticks`
//    换算的纳秒（与 Go 的 UnixNano 同为"高熵种子"用途；**不是**协议字段，客户端不解析这个字符串）。
using System.Security.Cryptography;
using TypeScript.Api;
using TypeScript.Core;
using TypeScript.Ipc;
using TypeScript.LsProto;
using static TypeScript.Core.Core;
using ProjectSession = TypeScript.Project.Session;

namespace TypeScript.Lsp;

/// <summary>Go 的 api 包/ipc 错误包装：`fmt.Errorf("failed to create API transport: %w", err)`。</summary>
internal sealed class ApiSessionException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed partial class Server
{
    /// <summary>Go: <c>apiSessions map[string]*api.Session</c>（server.go:227）：「活跃的 API 会话，按 ID 索引」。
    /// Go 的惰性 `if s.apiSessions == nil { make(...) }` 在 C# 里由字段初始化完成（Dictionary 恒非 null）。</summary>
    private readonly Dictionary<string, Api.Session> _apiSessions = [];

    /// <summary>Go: <c>apiSessionsMu sync.Mutex</c>（server.go:228）。</summary>
    private readonly Lock _apiSessionsMu = new();

    /// <summary>Go: <c>func (s *Server) handleInitializeAPISession(ctx, params, _) (lsproto.CustomInitializeAPISessionResponse, error)</c>
    /// （server.go:2280-2344）。</summary>
    private InitializeAPISessionResult? HandleInitializeAPISession(Context ctx, InitializeAPISessionParams? @params, RequestMessage _)
    {
        lock (_apiSessionsMu)
        {
            var apiSession = TypeScript.Api.Session.NewLSPSession(_session!, null);

            // Use provided pipe path or generate a unique one
            var pipePath = @params?.Pipe is { Length: > 0 } provided ? provided : GenerateApiPipePath();

            PipeTransport transport;
            try
            {
                transport = PipeTransport.NewPipeTransport(pipePath);
            }
            catch (Exception err)
            {
                throw new ApiSessionException($"failed to create API transport: {err.Message}", err);
            }

            // Start accepting connections in the background
            var thread = new Thread(
                () =>
                {
                    try
                    {
                        Stream rwc;
                        try
                        {
                            rwc = transport.Accept();
                        }
                        finally
                        {
                            transport.Close();
                        }

                        // Create a cancellable context for the API connection
                        using var apiCtx = CancellationTokenSource.CreateLinkedTokenSource(_backgroundCtx.CancellationToken);

                        try
                        {
                            var conn = AsyncConn.NewAsyncConn(rwc, apiSession);
                            apiSession.SetConnection(conn);
                            try
                            {
                                conn.Run(WithCancellation(_backgroundCtx, apiCtx.Token));
                            }
                            catch (Exception apiErr)
                            {
                                _logger.Errorf("API session %s: %v", apiSession.ID(), apiErr.Message);
                            }
                        }
                        catch (Exception panic)
                        {
                            // Go 的 recover()：记日志、取消 context、关掉底层连接。
                            _logger.Errorf("API session %s: panic: %v\n%s", apiSession.ID(), panic.Message, panic.ToString());
                            CancelQuietly(apiCtx);
                            try
                            {
                                rwc.Close();
                            }
                            catch (Exception)
                            {
                                // Go 里 rwc.Close() 的错误被忽略。
                            }
                        }
                    }
                    catch (Exception acceptErr)
                    {
                        _logger.Errorf("API session %s: failed to accept connection: %v", apiSession.ID(), acceptErr.Message);
                    }
                    finally
                    {
                        apiSession.Close();
                        RemoveApiSession(apiSession.ID());
                    }
                },
                _stackSize)
            {
                IsBackground = true,
                Name = "tsgo-api-session",
            };
            thread.Start();

            _apiSessions[apiSession.ID()] = apiSession;

            return new InitializeAPISessionResult
            {
                SessionId = apiSession.ID(),
                Pipe = pipePath,
            };
        }
    }

    /// <summary>Go: <c>func (s *Server) generateAPIPipePath() string</c>（server.go:2349-2354）：
    /// 用时间 + 随机源生成高熵路径（`tsgo-api-%x-%x`）。</summary>
    private string GenerateApiPipePath()
    {
        var now = (ulong)DateTime.UtcNow.Ticks * 100;
        var rnd = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8), 0);
        return Ipc.Ipc.GeneratePipePath($"tsgo-api-{now:x}-{rnd:x}");
    }

    /// <summary>Go: <c>func (s *Server) removeAPISession(id string)</c>（server.go:2356-2360）。</summary>
    private void RemoveApiSession(string id)
    {
        lock (_apiSessionsMu)
        {
            _apiSessions.Remove(id);
        }
    }
}
