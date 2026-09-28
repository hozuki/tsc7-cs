// ported from: cmd/tsc/main.go
//
// 与 Go 的偏离（逐条给依据；命名空间/类型映射见 docs/architecture.md §2/§3）：
//
// 1) `package main` 在 Go 里没有包名 → 本工程所有文件统一 `namespace TypeScript.Tsc`（RootNamespace=TypeScript +
//    目录名 `src/tsc`）。**注意**：`TypeScript.Tsc` 是本编译单元里真实存在的命名空间，所以本工程**禁止**写裸
//    `Tsc.` / `Execute.` / `Lsp.` / `Core.` / `Bundled.` 这类限定名去指库里的**同名**类型或静态类（名字解析会先
//    命中外层命名空间 `TypeScript.Tsc`/`TypeScript.Execute`/…，CS0118/CS0426），一律改用**不重名**的别名
//    （TscSystem/TscResult/…）或 `using static` 后的裸成员名（§2 的既有约定，同 tests/TypeScript.Harness/TscTests/
//    TestSys.cs 的 `TSys`）。
// 2) `func main() { os.Exit(runMain()) }`：Go 的主 goroutine 栈可增长，而 .NET 主线程栈固定（默认 1 MB），binder/
//    checker/printer 的深递归会**直接杀进程**（docs/architecture.md §3.9 的实测）⇒ 主流程跑在显式 64 MiB 栈线程上
//    （同常量：Execute/Build/Orchestrator.cs:1271、Project/Background/Queue.cs:42、Lsp/Server.cs:228）。异常不能跨
//    线程传播 → `ExceptionDispatchInfo` 捕获后在 Main 上重抛（§3.9 的既有做法，同 tests/TypeScript.Harness/
//    HarnessTest.cs:164-179、tests/TypeScript.Compiler.Tests/Execute/TscTestSystem.cs:105-125）。
//    退出码走 `Environment.Exit(code)`，与 `os.Exit` 语义一致：不跑任何 defer/finally、不等其它线程（watch 模式下的
//    watcher goroutine 亦然）。
// 3) 未捕获异常（Go 的 panic）：C# 侧不模仿 Go 的 "panic: … + goroutine 栈 + exit 2"，直接让运行时打印**未处理异常**
//    （栈迹更详细，退出码由运行时的未处理异常路径决定）。这是刻意选择：伪造 Go 的 panic 格式只会丢信息。
// 4) `core.ApplyDebugStackLimit()` → 裸名 `ApplyDebugStackLimit()`（Go 包级函数 → 与包同名的静态类，文件级 `using
//    static`，§2）。Go 读 TS_GO_DEBUG_STACK_LIMIT 调 runtime/debug.SetMaxStack；.NET 没有对应 API，C# 侧只做解析
//    （Core.cs 文件头偏离 7），真正的"大栈"由本文件的 64 MiB 栈线程提供。
// 5) `osutil.Args()` → 裸名 `Args()`（TypeScript.OsUtil.OsUtil）。`args[1:]` → `args[1..]`：Go 在 args 为空时
//    `args[1:]` 会 panic，C# 的 range 不会——但 `os.Args`/`osutil.Args()` 恒非空（BCL 的 GetCommandLineArgs() 同样
//    至少含 argv[0]），故不可观察。
// 6) `signal.NotifyContext(context.Background(), SIGINT, SIGTERM)` + `defer stop()` 是 Go 的 API → 本工程的
//    `SignalContext.NotifyContext()`（Signal.cs）+ `using`（deviation 清单见 Signal.cs 文件头）。
// 7) `result.Status` 是 `tsc.ExitStatus`（Go 的 int 别名）→ C# 是枚举 `TscExitStatus`，`(int)` 转换取同一数值。
// 8) `execute.CommandLine(ctx, sys, args, nil)` → 裸名 `CommandLine(ctx, sys, args, null)`（`nil` 的 C# 对应物是 `null`
//    的 `CommandLineTesting`；该参数在 Go 里也是可空的接口）。
using global::System.Runtime.ExceptionServices;
using TypeScript.Core;

using static TypeScript.Core.Core;
using static TypeScript.Execute.Execute;
using static TypeScript.OsUtil.OsUtil;

namespace TypeScript.Tsc;

/// <summary>Go: <c>func main()</c>（main.go:14-16）与 <c>func runMain() int</c>（main.go:18-33）。</summary>
public static class Program
{
    /// <summary>
    /// docs/architecture.md §3.9 的 64 MiB 栈线程（与 Execute/Build/Orchestrator.cs:1271、
    /// Project/Background/Queue.cs:42、Lsp/Server.cs:228 是同一个常量）。
    /// </summary>
    private const int _stackSize = 64 * 1024 * 1024;

    /// <summary>Go: <c>func main() { os.Exit(runMain()) }</c>（main.go:14-16）。</summary>
    public static void Main()
    {
        // Go: enablevtprocessing_windows.go 的 init()（在 main 之前无条件执行；C# 没有"包级 init"，
        // 这里在 Main 首行调用，覆盖 --lsp/--api 与编译器三条路径）。
        EnableVtProcessing.Run();

        var exitCode = 0;
        ExceptionDispatchInfo? captured = null;

        // 见文件头偏离 2：CLI 主流程必须跑在大栈线程上。
        var thread = new Thread(
            () =>
            {
                try
                {
                    exitCode = RunMain();
                }
                catch (Exception ex)
                {
                    captured = ExceptionDispatchInfo.Capture(ex);
                }
            },
            _stackSize);
        thread.Start();
        thread.Join();

        // Go: main 里未捕获的 panic 由运行时打印（见文件头偏离 3）。
        captured?.Throw();

        // Go: os.Exit(runMain())。
        Environment.Exit(exitCode);
    }

    /// <summary>Go: <c>func runMain() int</c>（main.go:18-33）。</summary>
    internal static int RunMain()
    {
        ApplyDebugStackLimit();

        var args = Args()[1..];
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--lsp":
                    return LspCli.RunLsp(args[1..]);
                case "--api":
                    return ApiCli.RunApi(args[1..]);
            }
        }

        using var signals = SignalContext.NotifyContext();
        var result = CommandLine(signals.Context, Sys.NewSystem(), args, null);
        return (int)result.Status;
    }
}
