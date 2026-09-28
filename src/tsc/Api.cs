// ported from: cmd/tsc/api.go
//
// 与 Go 的偏离（逐条给依据；命名空间/类型映射见 docs/architecture.md §2/§3）：
//
// 1) `func runAPI(args []string) int` / `func parseAPIFlags(args []string) (apiFlags, error)`（未导出包级函数）
//    → `ApiCli.RunApi` / `ApiCli.TryParseApiFlags`（`package main` 没有包名；`Api` 这个名字在 `TypeScript.*` 下
//    已被命名空间占用，见 Program.cs 文件头偏离 1）。
// 2) `type apiFlags struct` → `ApiCli.ApiFlags`（未导出类型按 C# 习惯命名，§2）。Go 的 `flag.StringVar(&result.cwd, …)`
//    把解析结果直接写进结构体字段（靠 `*T`）；C# 侧的 `FlagSet` 返回 Flag 对象（Flag.cs 文件头偏离 2）
//    ⇒ 解析成功后从各 Flag 的 `.Value` **拷贝**进结构体（字段名与 Go 逐字对应）。
// 3) `flag.NewFlagSet("api", flag.ContinueOnError)` → `FlagSet("api")`；`if err != nil { return 2 }` →
//    `if (!TryParseApiFlags(...)) return 2`（Flag.cs 文件头偏离 3：错误文本 + 用法已由 FlagSet 写到 stderr）。
// 4) `core.Must(os.Getwd())` → `Directory.GetCurrentDirectory()`（Go 的 `Must` 在 C# 里没有对应物）。
// 5) `strings.Split(flags.callbacks, ",")` → `flags.Callbacks.Split(',')`（`strings.Split` 与 `string.Split(char)`
//    逐字等价：空串已在上一步用 `!= ""` 排除，故不会出现 Go 里 `strings.Split("", ",")` 的 `[""]` 那种边界）。
// 6) `&api.StdioServerOptions{...}` → `new StdioServerOptions { ... }`（公开字段初始化器；字段名与 Go 逐字对应，
//    见 TypeScript.Compiler/Api/Server.cs）。`options.In = os.Stdin` / `options.Out = os.Stdout` →
//    `Console.OpenStandardInput()` / `Console.OpenStandardOutput()`（`io.ReadCloser`/`io.WriteCloser` → `Stream`）。
// 7) `signal.NotifyContext(...)` + `defer stop()` → `SignalContext` + `using`（Signal.cs）。
// 8) `if err := s.Run(ctx); err != nil { fmt.Fprintln(os.Stderr, err); return 1 }` → C# 的 `StdioServer.Run` 返回
//    void、失败抛异常（Api/Server.cs 的契约）⇒ `try/catch` 后打印 `ex.Message` 并返回 1（同一形状）。**唯独不吞
//    `DebugFailureException`**：Go 里 `debug.Fail` 是 panic，不是"Run 返回 error"（同 Program.cs 文件头偏离 3）。
using global::System;
using global::System.IO;
using TypeScript.Api;
using TypeScript.Core;

using static TypeScript.Bundled.Bundled;

namespace TypeScript.Tsc;

/// <summary>Go: <c>func runAPI(args []string) int</c>（api.go:41-82）与 <c>func parseAPIFlags(args []string) (apiFlags, error)</c>（api.go:26-39）。</summary>
internal static class ApiCli
{
    /// <summary>Go: <c>type apiFlags struct</c>（api.go:17-24）。</summary>
    internal sealed class ApiFlags
    {
        /// <summary>Go: <c>cwd string</c>。</summary>
        internal string Cwd = "";

        /// <summary>Go: <c>pipePath string</c>。</summary>
        internal string PipePath = "";

        /// <summary>Go: <c>callbacks string</c>。</summary>
        internal string Callbacks = "";

        /// <summary>Go: <c>async bool</c>。</summary>
        internal bool Async;

        /// <summary>Go: <c>timing bool</c>。</summary>
        internal bool Timing;

        /// <summary>Go: <c>runExternalCode bool</c>。</summary>
        internal bool RunExternalCode;
    }

    /// <summary>Go: <c>func parseAPIFlags(args []string) (apiFlags, error)</c>（api.go:26-39）。</summary>
    internal static bool TryParseApiFlags(string[] args, out ApiFlags result)
    {
        var flags = new FlagSet("api");
        var cwd = flags.String("cwd", Directory.GetCurrentDirectory(), "current working directory");
        var pipePath = flags.String("pipe", "", "use named pipe or Unix domain socket for communication instead of stdio");
        var callbacks = flags.String("callbacks", "", "comma-separated list of FS callbacks to enable (readFile,fileExists,directoryExists,getAccessibleEntries,realpath)");
        var async = flags.Bool("async", false, "use JSON-RPC protocol instead of MessagePack (for async API)");
        var timing = flags.Bool("timing", false, "collect per-request server processing time, folded into the client's timing snapshot");
        var runExternalCode = flags.Bool("runExternalCode", false, "allow projects to execute configured external plugins");

        if (!flags.TryParse(args))
        {
            // Go: return apiFlags{}, err（失败时调用方拿到零值结构体）。
            result = new ApiFlags();
            return false;
        }

        result = new ApiFlags
        {
            Cwd = cwd.Value,
            PipePath = pipePath.Value,
            Callbacks = callbacks.Value,
            Async = async.Value,
            Timing = timing.Value,
            RunExternalCode = runExternalCode.Value,
        };
        return true;
    }

    /// <summary>Go: <c>func runAPI(args []string) int</c>（api.go:41-82）。</summary>
    internal static int RunApi(string[] args)
    {
        if (!TryParseApiFlags(args, out var flags))
        {
            return 2;
        }

        var defaultLibraryPath = LibPath();

        // Go: var callbacksList []string; if flags.callbacks != "" { callbacksList = strings.Split(flags.callbacks, ",") }
        string[]? callbacksList = null;
        if (flags.Callbacks.Length != 0)
        {
            callbacksList = flags.Callbacks.Split(',');
        }

        var options = new StdioServerOptions
        {
            Err = Sys.StdErr,
            Cwd = flags.Cwd,
            DefaultLibraryPath = defaultLibraryPath,
            Callbacks = callbacksList,
            Async = flags.Async,
            CollectTiming = flags.Timing,
            RunExternalCode = flags.RunExternalCode,
            ContentMapperSpawner = Sys.NewSystem(),
        };
        if (flags.PipePath.Length != 0)
        {
            options.PipePath = flags.PipePath;
        }
        else
        {
            options.In = Console.OpenStandardInput();
            options.Out = Console.OpenStandardOutput();
        }

        var s = StdioServer.NewStdioServer(options);

        using var signals = SignalContext.NotifyContext();

        try
        {
            s.Run(signals.Context);
        }
        catch (Exception ex) when (ex is not DebugFailureException)
        {
            Sys.StdErr.Write(ex.Message + "\n");
            return 1;
        }
        return 0;
    }
}
