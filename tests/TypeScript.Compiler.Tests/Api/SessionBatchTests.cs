// ported from: internal/api/session_batch_test.go:1-177
//
// 与 Go 测试的差异（逐条）：
//
// 1) `session.handleBatchRequests` / `session.handleBatchRequests`（Go 包内可见）→ `Session.HandleBatchRequests`
//    （C# `internal` = 同包可见，测试程序集靠 `InternalsVisibleTo` 访问；契约 §4"测试可访问性"）。
// 2) Go 的 `&Session{}`（零值会话）与 `var session *Session`（nil 接收者）都没有 C# 对应物：`Session`
//    没有公开的无参构造（只有 `NewLSPSession`/`NewStandaloneSession`），实例方法也不可能在 null 上分派
//    ⇒ 前两个用例改用 `NewStandaloneSession(GetSessionInitOptions(...))`（同样的"没有 project session"形状）。
// 3) **不移植的分支**：`TestHandleBatchRequestsRecoversPerRequestPanics` 里"某条请求 panic"的断言
//    （`strings.Contains(..., "panic:")`）依赖 Go 的 `recover()`；Go 测试是在 **nil `*Session`** 上调用
//    `handleBatchRequest` 触发 nil 解引用 panic 的。C# 里 `HandleBatchRequest` 的 panic 分支是
//    `catch (DebugFailureException)`（契约 §3.2），而所有走 `HandleRequest` 的路径在缺少 project session /
//    snapshot 时抛的是 `ClientErrorException`（业务错误，不是 panic）；可达的请求里没有一条会在这条路径上
//    断言失败。因此该用例保留"单条请求失败不影响其余请求"这一**不变式**（第 2 条请求断言错误文本，
//    第 1/3 条仍返回 "pong"），`"panic:"` 前缀这一条不移植（列在交付报告的未覆盖清单里）。
// 4) `context.Background()` → `Context.Background`（Core/Context.cs：Go 的函数 → C# 的静态字段）。
// 5) `json.Value{}` / `json.Value(`…`)`（Go 的原始 JSON 字节）→ C# 的 `byte[]?`：空 payload 写 `[]`，
//    带内容的写 `"…"u8.ToArray()`（`Encoder.WriteValue` 原样透传，见 Proto.Types2.cs 的 `BatchRequest`）。
// 6) `json.Marshal(x)` 返回 `(bytes, error)` → C# 的 `TypeScript.Json.Json.Marshal(x)` 直接返回 `byte[]`
//    （失败抛异常；**必须全限定**：`Json` 在 `TypeScript.Tests` 里会先解析到命名空间 `TypeScript.Json`，
//    裸写 `Json.Marshal` 是 CS0234 —— 仓内先例：Session.Requests.cs 文件头第 10 条）；
//    `strings.Contains(string(encoded), …)` → `Assert.Contains(substr, Encoding.UTF8.GetString(encoded))`。
// 7) `json.Unmarshal(encoded, &wireResponse)` → `Json.UnmarshalNew<BatchRequestsResponse>(encoded)`。
// 8) `defer session.Close()` → `try/finally`（`Session` 不实现 `IDisposable`，仓内先例：SessionTests.cs）。
// 9) `assert.NilError(t, err)` 消失（C# 的错误路径是异常）；`assert.Assert(t, x)` → `Assert.True(x)`；
//    `len(x)` → `x.Length`；`assert.Equal(t, len(...), n)` → `Assert.Equal(n, ...)`（本仓断言是 expected 在前）。
// 10) `projecttestutil.Setup(map[string]any{})` → `Setup(new Dictionary<string, object?>())`，走
//     `using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;`。
// 11) `Method`（Go 的 `type Method string` + 常量表）→ C# 的 `Method` 静态类 `const string`（契约 §4 第 1 条），
//     字段/参数类型都是 `string`。
// 12) 用例类收进 `[Collection("ProjectHubSerial")]`（禁并行）：这些用例建真实 project 会话并驱动共享的静态状态，
//     与 SessionTests 同因（见 SessionTests.cs 的 collection 定义）。
namespace TypeScript.Tests;

using System.Collections.Generic;
using System.Text;
using TypeScript.Api;
using TypeScript.Core;
using TypeScript.Tests.ProjectTestUtil;
using Xunit;
using static TypeScript.Json.Json;
using static TypeScript.Tests.ProjectTestUtil.ProjectTestUtil;
using ApiSession = TypeScript.Api.Session;

/// <summary>Go: <c>internal/api/session_batch_test.go</c> 的 8 个用例。</summary>
[Collection("ProjectHubSerial")]
public class SessionBatchTests
{
    /// <summary>Go: <c>TestHandleBatchRequests</c>（session_batch_test.go:13-36）。</summary>
    [Fact]
    public void TestHandleBatchRequests()
    {
        var (init, _) = GetSessionInitOptions(new Dictionary<string, object?>(), null, new TypingsInstallerOptions());
        var session = ApiSession.NewStandaloneSession(init, null);
        try
        {
            var response = session.HandleBatchRequests(Context.Background, new BatchRequestsParams
            {
                Requests =
                [
                    new BatchRequest { Method = "ping", Params = [] },
                    new BatchRequest { Method = "unknown", Params = [] },
                ],
            });

            Assert.Equal(2, response.Responses.Length);
            Assert.Equal("ping", response.Responses[0].Method);
            Assert.Equal("pong", response.Responses[0].Result);
            Assert.Equal("", response.Responses[0].Error);
            Assert.Equal("unknown", response.Responses[1].Method);
            var requestErr = response.Responses[1].Error;
            Assert.Contains("unknown API method", requestErr);

            var encoded = TypeScript.Json.Json.Marshal(response);
            Assert.Contains(
                "\"error\":\"api: invalid request: unknown API method \\\"unknown\\\"\"",
                Encoding.UTF8.GetString(encoded));
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestHandleBatchRequestsRecoversPerRequestPanics</c>（session_batch_test.go:38-54）。
    /// 见文件头第 3 条：C# 侧断言的是"单条请求失败被隔离"这一不变式，`"panic:"` 前缀不移植。</summary>
    [Fact]
    public void TestHandleBatchRequestsRecoversPerRequestPanics()
    {
        var (init, _) = GetSessionInitOptions(new Dictionary<string, object?>(), null, new TypingsInstallerOptions());
        var session = ApiSession.NewStandaloneSession(init, null);
        try
        {
            var response = session.HandleBatchRequests(Context.Background, new BatchRequestsParams
            {
                Requests =
                [
                    new BatchRequest { Method = "ping", Params = [] },
                    new BatchRequest
                    {
                        Method = Method.GetAnyType,
                        Params = """{"snapshot":1,"project":"project"}"""u8.ToArray(),
                    },
                    new BatchRequest { Method = "ping", Params = [] },
                ],
            });

            Assert.Equal("pong", response.Responses[0].Result);
            Assert.Contains("snapshot 1 not found", response.Responses[1].Error);
            Assert.Equal("pong", response.Responses[2].Result);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestBatchResponseEncodesEmptyResult</c>（session_batch_test.go:56-62）。</summary>
    [Fact]
    public void TestBatchResponseEncodesEmptyResult()
    {
        var encoded = TypeScript.Json.Json.Marshal(new BatchResponse { Method = Method.GetSignaturesOfType, Result = Array.Empty<object?>() });
        Assert.Equal("""{"method":"getSignaturesOfType","result":[]}""", Encoding.UTF8.GetString(encoded));
    }

    /// <summary>Go: <c>TestHandleBatchRequestsPaginatesResponses</c>（session_batch_test.go:64-105）。</summary>
    [Fact]
    public void TestHandleBatchRequestsPaginatesResponses()
    {
        var (projectSession, _) = Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            const int maxResponseBytesPerPage = 150;
            var requests = new BatchRequest[10];
            for (var i = 0; i < requests.Length; i++)
            {
                requests[i] = new BatchRequest { Method = "ping", Params = [] };
            }

            var response = session.HandleBatchRequests(Context.Background, new BatchRequestsParams
            {
                Requests = requests,
                MaxResponseBytesPerPage = maxResponseBytesPerPage,
            });

            var responses = new List<BatchResponse>();
            while (true)
            {
                var encoded = TypeScript.Json.Json.Marshal(response);
                Assert.True(encoded.Length <= maxResponseBytesPerPage);
                var wireResponse = UnmarshalNew<BatchRequestsResponse>(encoded);
                responses.AddRange(wireResponse.Responses);
                if (wireResponse.ContinuationToken == "")
                {
                    break;
                }

                response = session.HandleBatchRequests(Context.Background, new BatchRequestsParams
                {
                    ContinuationToken = wireResponse.ContinuationToken,
                    MaxResponseBytesPerPage = maxResponseBytesPerPage,
                });
            }

            Assert.Equal(requests.Length, responses.Count);
            foreach (var r in responses)
            {
                Assert.Equal("ping", r.Method);
                Assert.Equal("pong", r.Result);
            }
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestHandleBatchRequestsAllowsOversizedSingleResponse</c>（session_batch_test.go:107-122）。</summary>
    [Fact]
    public void TestHandleBatchRequestsAllowsOversizedSingleResponse()
    {
        var (projectSession, _) = Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleBatchRequests(Context.Background, new BatchRequestsParams
            {
                Requests = [new BatchRequest { Method = "ping", Params = [] }],
                MaxResponseBytesPerPage = 1,
            });

            Assert.Single(response.Responses);
            Assert.Equal("", response.ContinuationToken);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestHandleBatchRequestsPageLimitIsRequestScoped</c>（session_batch_test.go:124-149）。</summary>
    [Fact]
    public void TestHandleBatchRequestsPageLimitIsRequestScoped()
    {
        var (projectSession, _) = Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var requests = new BatchRequest[]
            {
                new() { Method = "ping", Params = [] },
                new() { Method = "ping", Params = [] },
            };

            var limited = session.HandleBatchRequests(Context.Background, new BatchRequestsParams
            {
                Requests = requests,
                MaxResponseBytesPerPage = 1,
            });
            Assert.Single(limited.Responses);
            Assert.NotEqual("", limited.ContinuationToken);

            var unlimited = session.HandleBatchRequests(Context.Background, new BatchRequestsParams { Requests = requests });
            Assert.Equal(requests.Length, unlimited.Responses.Length);
            Assert.Equal("", unlimited.ContinuationToken);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestHandleBatchRequestsRejectsInvalidContinuationToken</c>（session_batch_test.go:151-161）。</summary>
    [Fact]
    public void TestHandleBatchRequestsRejectsInvalidContinuationToken()
    {
        var (projectSession, _) = Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var e = Assert.Throws<ClientErrorException>(() => session.HandleBatchRequests(
                Context.Background,
                new BatchRequestsParams { ContinuationToken = "invalid" }));
            Assert.Contains("invalid batch continuation token", e.Message);
        }
        finally
        {
            session.Close();
        }
    }

    /// <summary>Go: <c>TestHandleBatchRequestsRejectsNestedBatch</c>（session_batch_test.go:163-177）。</summary>
    [Fact]
    public void TestHandleBatchRequestsRejectsNestedBatch()
    {
        var (projectSession, _) = Setup(new Dictionary<string, object?>());
        var session = ApiSession.NewLSPSession(projectSession, null);
        try
        {
            var response = session.HandleBatchRequests(Context.Background, new BatchRequestsParams
            {
                Requests = [new BatchRequest { Method = Method.BatchRequests, Params = """{"requests":[]}"""u8.ToArray() }],
            });

            Assert.Single(response.Responses);
            Assert.Contains("batchRequests cannot be nested", response.Responses[0].Error);
        }
        finally
        {
            session.Close();
        }
    }
}
