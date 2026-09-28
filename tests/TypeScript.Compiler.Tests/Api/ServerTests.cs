// ported from: internal/api/server_test.go:1-38
//
// 与 Go 测试的差异（逐条）：
//
// 1) `func serverRunError(ctx context.Context, err error) error` → `Api.ServerRunError(Context, Exception)`
//    （Server.cs 文件头）：C# 返回**要抛的异常**或 `null`，`null` 对应 Go 的 `nil`。于是
//    `assert.NilError(t, x)` → `Assert.Null(x)`；`assert.ErrorIs(t, x, err)`（C# 没有 error 包装链）→
//    `Assert.Same(err, x)`（返回的就是同一个异常实例）。
// 2) Go 的第一个子测试（"EOF"）传的是 **nil error**：`serverRunError(context.Background(), nil)`；C# 的形参是
//    非空 `Exception err`，因此传 `null!`（可空标注只是编译期契约，运行期行为与 Go 的 nil 一致）。
// 3) `context.WithCancel` + `cancel()` → `new CancellationTokenSource()` + `Cancel()` +
//    `Core.WithCancellation(Context.Background, cts.Token)`（Core/Context.cs 文件头的既定映射）。
// 4) Go 的哨兵 `context.Canceled` → C# 的 `OperationCanceledException`：C# 的 `Context` 只携带"是否已取消"
//    （`CancellationToken`），没有 Go 那个可比较的哨兵值，所以"身份"这一条断言用一个具体实例表达
//    （`Assert.Same` 仍然钉住"原样返回同一个对象"）。
// 5) `t.Run(名字, …)` 子测试 → 一个独立 `[Fact]`（方法名 = `TestServerRunError` + 用例名 PascalCase）；
//    `t.Parallel()` 不移植（xunit 默认按 collection 并行）。
namespace TypeScript.Tests;

using TypeScript.Core;
using Xunit;
using static TypeScript.Api.Api;
using static TypeScript.Core.Core;

/// <summary>Go: <c>internal/api/server_test.go</c> 的 <c>TestServerRunError</c>（server_test.go:11）。</summary>
public class ServerTests
{
    /// <summary>Go: <c>TestServerRunError/EOF</c>（server_test.go:14-17）。</summary>
    [Fact]
    public void TestServerRunErrorEOF()
    {
        Assert.Null(ServerRunError(Context.Background, null!));
    }

    /// <summary>Go: <c>TestServerRunError/context_cancellation</c>（server_test.go:19-24）。</summary>
    [Fact]
    public void TestServerRunErrorContextCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Null(ServerRunError(WithCancellation(Context.Background, cts.Token), new OperationCanceledException()));
    }

    /// <summary>Go: <c>TestServerRunError/unrelated_cancellation</c>（server_test.go:26-30）。</summary>
    [Fact]
    public void TestServerRunErrorUnrelatedCancellation()
    {
        var err = new OperationCanceledException();
        Assert.Same(err, ServerRunError(Context.Background, err));
    }

    /// <summary>Go: <c>TestServerRunError/context_cancellation_supersedes_server_error</c>（server_test.go:32-37）。</summary>
    [Fact]
    public void TestServerRunErrorContextCancellationSupersedesServerError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Null(ServerRunError(WithCancellation(Context.Background, cts.Token), new Exception("server failed")));
    }
}
