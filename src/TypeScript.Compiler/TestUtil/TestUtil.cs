// ported from: internal/testutil/testutil.go
//
// 与 Go 的差异：
// 1) Go 的 `defer testutil.RecoverAndFail(t, msg)` 只在对端 panic 时才失败；C# 没有 defer，改成
//    `RecoverAndFail(t, msg, body)`：包住 body，任何非 Skip/非 Fail 的异常都转成 `t.Fatal(msg + 异常)`。
//    （Go 的版本用 debug.Stack() 附栈；C# 里 `Exception.ToString()` 自带栈。）
// 2) `TestProgramIsSingleThreaded`：Go 的默认值是 `!race.Enabled`（非 race 构建 = 单线程）；C# 无 race 构建，
//    默认 true，仍保留 `TS_TEST_PROGRAM_SINGLE_THREADED` 环境变量覆盖（与 Go 同名同语义）。
// 3) `AssertPanics`：Go 用 `recover()` 取 panic 值再 `assert.Equal`；C# 的对应物是捕获异常对象。
//    `Skip`/`Fatal` 走的是 runtime.Goexit（recover 抓不到）→ 这里直接放行，语义与 Go 一致。
namespace TypeScript.TestUtil;

/// <summary>Go 的 <c>internal/testutil</c> 包级函数。</summary>
public static class TestUtil
{
    /// <summary>Go: <c>func RecoverAndFail(t *testing.T, msg string)</c>（testutil.go:30-35）。</summary>
    public static void RecoverAndFail(IHarnessTest t, string message, Action body)
    {
        try
        {
            body();
        }
        catch (SkipTestException)
        {
            throw;
        }
        catch (TestFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Go: t.Fatalf("%s:\n%v\n%s", msg, r, string(debug.Stack()))
            t.Fatal(message + ":\n" + ex);
        }
    }

    /// <summary>Go: <c>func AssertPanics(tb testing.TB, fn func(), expected any, msgAndArgs ...any)</c>（testutil.go:12-27）。</summary>
    public static void AssertPanics(IHarnessTest tb, Action fn, object? expected, params object?[] msgAndArgs)
    {
        object? got = null;

        try
        {
            fn();
        }
        catch (SkipTestException)
        {
            // Go: FailNow 走 runtime.Goexit，recover() 抓不到 → 放行。
            throw;
        }
        catch (TestFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            got = ex;
        }

        var extra = msgAndArgs.Length == 0 ? "" : " (" + string.Join(", ", msgAndArgs) + ")";
        if (got is null)
        {
            // Go: assert.Assert(tb, got != nil, msgAndArgs...)
            tb.Fatal("expected a panic, but none occurred" + extra);
            return;
        }
        if (!Equals(got, expected))
        {
            // Go: assert.Equal(tb, got, expected, msgAndArgs...)
            tb.Fatal($"expected panic {expected ?? "nil"}, got {got}" + extra);
        }
    }

    /// <summary>Go: <c>func TestProgramIsSingleThreaded() bool</c>（testutil.go:37-49）。</summary>
    public static bool TestProgramIsSingleThreaded()
    {
        // Leave Program in SingleThreaded mode unless explicitly configured or in race mode.
        var v = Environment.GetEnvironmentVariable("TS_TEST_PROGRAM_SINGLE_THREADED");
        if (!string.IsNullOrEmpty(v))
        {
            if (bool.TryParse(v, out var b))
            {
                return b;
            }
        }
        return true; // Go: return !race.Enabled（C# 无 race 构建）
    }
}
