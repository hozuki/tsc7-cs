// M7 验收 harness 的 `*testing.T` 实现（Go 侧 `internal/testrunner/*_test.go` 的 TestLocal 入口）。
//
// 与 Go 的对应关系：
//   * 子测试名 = Go 的相对子测试名（`TestLocal/<用例>` → 这里用 `<用例>`，嵌套用 `/` 连接）。
//   * 单用例过滤 = Go 的 `go test -run 'TestLocal/<name>'` → 这里的 `--case <子串>`（见 ShouldRun）。
//   * t.Fatal → 记录失败并抛 TestFailureException（只中止当前子测试，父级继续，与 Go 的 FailNow 一致）。
//   * t.Error → 记录失败，不中止。
//   * t.Skip  → 记录跳过并抛 SkipTestException（当前子测试结束，父级继续）。
//   * t.Parallel → 记录意图，实际顺序执行（baseline 逐字节比较要求确定性；docs/harness.md §4 也指出
//      并行 emit 会造成若干用例的非确定性）。
//   * 深递归：每个子测试跑在 64 MiB 栈的线程上（docs/architecture.md §3.9，binder 压栈用例会崩进程）。
namespace TypeScript.Harness;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using TypeScript.TestUtil;

/// <summary>一次运行的失败/跳过记录（父子共享）。</summary>
public sealed class RunReport
{
    public readonly List<(string Name, string Message)> Failures = [];
    public readonly List<(string Name, string Message)> Skips = [];

    /// <summary>顶层（用例级）子测试名 = 实际执行过的用例（含变体，变体名 = `basename 配置名`）。</summary>
    public readonly HashSet<string> CasesRun = [];

    public void Fail(string name, string message) => Failures.Add((name, message));

    public void Skip(string name, string message) => Skips.Add((name, message));

    /// <summary>某个用例是否有失败（失败名的第一段 = 用例名）。</summary>
    public bool CaseFailed(string caseName)
    {
        foreach (var (name, _) in Failures)
        {
            if (name == caseName || name.StartsWith(caseName + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>Go 的 <c>*testing.T</c> 在 harness 里的替身实现。</summary>
public sealed class HarnessTest : IHarnessTest
{
    private const int _stackSize = 64 * 1024 * 1024;

    private readonly RunReport _report;
    private readonly string[] _cases;

    private HarnessTest(string name, RunReport report, string[] cases)
    {
        Name = name;
        _report = report;
        _cases = cases;
    }

    public static HarnessTest Root(RunReport report, string[] cases) => new("", report, cases);

    public string Name { get; }

    public CancellationToken CancellationToken => CancellationToken.None;

    /// <summary>Go 的 <c>go test -run</c> 语义的简化版：子串命中即可，
    /// 且祖先名（过滤器是它的超串）也要执行，否则无法下钻到叶子子测试。</summary>
    private bool ShouldRun(string name)
    {
        if (_cases.Length == 0)
        {
            return true;
        }
        foreach (var filter in _cases)
        {
            if (name.Contains(filter, StringComparison.Ordinal) || filter.Contains(name, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>非 Go 源：供 harness 的进度/超时看门狗判断该子测试是否会被执行（见 IHarnessTest.WillRun）。</summary>
    public bool WillRun(string name) => ShouldRun(name);

    public void Run(string name, Action<IHarnessTest> body)
    {
        var childName = Name.Length == 0 ? name : Name + "/" + name;
        if (!ShouldRun(childName))
        {
            return;
        }

        var child = new HarnessTest(childName, _report, _cases);
        if (Name.Length == 0)
        {
            // 顶层 = 用例级（Go: TestLocal/<用例> 的那一段）。
            _report.CasesRun.Add(name);
        }
        try
        {
            RunOnBigStackThread(() => body(child));
        }
        catch (SkipTestException)
        {
            // Go: 子测试 t.Skip → 只有该子测试被标记跳过，父级继续。
        }
        catch (TestFailureException)
        {
            // 失败已由 Fatal/Error 记入 report；Go 的 FailNow 只中止当前子测试。
        }
    }

    public void Parallel()
    {
        // 顺序执行（见文件头）。
    }

    [DoesNotReturn]
    public void Skip(string message)
    {
        _report.Skip(Name, message);
        throw new SkipTestException(message);
    }

    [DoesNotReturn]
    public void Fatal(string message)
    {
        _report.Fail(Name, message);
        throw new TestFailureException(message);
    }

    public void Error(string message)
    {
        _report.Fail(Name, message);
    }

    private static void RunOnBigStackThread(Action body)
    {
        ExceptionDispatchInfo? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
        }, _stackSize);
        thread.Start();
        thread.Join();
        captured?.Throw();
    }
}
