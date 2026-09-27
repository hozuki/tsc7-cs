// ported from: Go 的 `testing.T`（internal/testrunner/*、internal/testutil/* 都收它）
//
// 移植说明：Go 的 harness 全靠 `*testing.T`（t.Run 子测试、t.Skip、t.Fatal、t.Error、t.Parallel、t.Context）。
// C# 的 `TypeScript.Compiler` 是被引用的库，不能引 xunit/nunit，所以这里定义 harness 自己的最小替身
// `IHarnessTest`，语义逐条对齐 Go：
//   * `t.Run(name, fn)`   → `Run`，子测试名 = Go 的相对子测试名（`TestLocal/<用例>`）。
//   * `t.Skip(...)`       → `Skip`，抛 `SkipTestException`（Go 用 runtime.Goexit()；C# 用异常展开）。
//   * `t.Fatal(...)`      → `Fatal`，抛 `TestFailureException`（Go 的 FailNow 也是立即终止当前测试 goroutine）。
//   * `t.Error(...)`      → `Error`，记录失败但**不**终止（Go 的 Error 语义；Baseline.Run 用它）。
//   * `t.Parallel()`      → `Parallel`，Go 用它把子测试交给 test 框架并发；C# 侧由实现决定（默认 no-op）。
//   * `t.Context()`       → `CancellationToken`（internal/transpile 的 TranspileModule 收 context）。
namespace TypeScript.TestUtil;

using System.Diagnostics.CodeAnalysis;

/// <summary>Go: <c>t.Skip(...)</c> 的非局部退出（对应 runtime.Goexit + Skip 标记）。</summary>
public sealed class SkipTestException : Exception
{
    public SkipTestException(string message) : base(message)
    {
    }
}

/// <summary>Go: <c>t.Fatal(...)</c> / <c>t.Fatalf(...)</c> 的非局部退出（FailNow 语义）。</summary>
public sealed class TestFailureException : Exception
{
    public TestFailureException(string message) : base(message)
    {
    }
}

/// <summary>Go 的 <c>testing.T</c> 在 harness 里被用到的那部分接口（见文件头注释的逐条对应）。</summary>
public interface IHarnessTest
{
    /// <summary>Go: <c>t.Name()</c>（baseline 记录用相对子测试名）。</summary>
    string Name { get; }

    /// <summary>Go: <c>t.Context()</c>（internal/transpile 的 TranspileModule/TranspileDeclaration）。</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>Go: <c>t.Run(name, func(t *testing.T){...})</c>。</summary>
    void Run(string name, Action<IHarnessTest> body);

    /// <summary>Go: <c>t.Parallel()</c>。</summary>
    void Parallel();

    /// <summary>Go: <c>t.Skip(args...)</c> → 抛 <see cref="SkipTestException"/>。</summary>
    [DoesNotReturn]
    void Skip(string message);

    /// <summary>Go: <c>t.Fatal(args...)</c> / <c>t.Fatalf(...)</c> → 抛 <see cref="TestFailureException"/>。</summary>
    [DoesNotReturn]
    void Fatal(string message);

    /// <summary>Go: <c>t.Error(args...)</c>（标记失败但不终止）。</summary>
    void Error(string message);

    /// <summary>
    /// 非 Go 源：该子测试名是否会被本次运行选中（用于 harness 的进度/超时看门狗，避免为被过滤掉的用例打印噪音）。
    /// 默认实现返回 true（等价于"没有过滤"）。
    /// </summary>
    bool WillRun(string name) => true;
}
