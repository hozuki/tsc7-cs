// ported from: internal/testrunner/runner.go
namespace TypeScript.TestRunner;

using TypeScript.TestUtil;

/// <summary>Go: <c>type Runner interface</c>（runner.go:5-8）。</summary>
public interface Runner
{
    string[] EnumerateTestFiles();

    void RunTests(IHarnessTest t);
}

/// <summary>Go 的 <c>testrunner</c> 包级声明（runner.go）。</summary>
public static partial class TestRunner
{
    /// <summary>Go: <c>func runTests(t *testing.T, runners []Runner)</c>（runner.go:10-14）。</summary>
    internal static void RunTests(IHarnessTest t, Runner[] runners)
    {
        foreach (var runner in runners)
        {
            runner.RunTests(t);
        }
    }
}
