// ported from: cmd/tsc/lsp.go
//
// 与 Go 的偏离（逐条给依据；命名空间/类型映射见 docs/architecture.md §2/§3）：
//
// 1) `func runLSP(args []string) int`（未导出包级函数）→ `LspCli.RunLsp`。`package main` 没有包名，无法照
//    "包级函数进同名静态类"的写法；`Lsp` 这个名字在 `TypeScript.*` 下已经被命名空间占用（裸写会 CS0118），
//    所以用 `LspCli`（见 Program.cs 文件头偏离 1）。
// 2) `flag.NewFlagSet("lsp", flag.ContinueOnError)` + `String/Bool/Int` → 本工程的 `FlagSet`（Flag.cs；`*T` 输出
//    参数 → 解析后读 `.Value`）。Go 的 `_ = pipe` / `_ = socket` 是"声明了但不用"的消歧义写法（包级变量用不上时才
//    需要），C# 里未使用的局部变量不报错，所以那两个 flag **仍然定义**（对外行为一致：接受 `-pipe`/`-socket`、
//    忽略其值、并出现在用法输出里）但**不绑定变量**。
// 3) `pprof.BeginProfiling(*pprofDir, os.Stderr)` → `Pprof.BeginProfiling(...)`（Execute/Pprof.cs 的 no-op 会话）；
//    Go 的 `defer profileSession.Stop()` → `try/finally`（`ProfileSession` 不是 `IDisposable`，同 Execute/Tsc.cs
//    偏离 8）。为了保持 function 级 defer 的语义（而不是 if 块级），`*pprofDir != ""` 分支里把余下流程包在
//    try/finally 里——与 Go 的"defer 在函数返回时执行一次"等价。
// 4) `lsp.NewServer(&lsp.ServerOptions{...})` → `new LspServer(opts)`（C# 侧是构造函数，见 Lsp/Server.cs:344 的
//    `NewServer` 注释）。`lsp.ToReader(os.Stdin)`/`ToWriter(os.Stdout)` → 裸名 `ToReader(...)`/`ToWriter(...)`
//    （`using static TypeScript.Lsp.Lsp`），参数是 `Stream`（`Console.OpenStandardInput/Output()`）。
// 5) `core.Must(os.Getwd())` → `Directory.GetCurrentDirectory()`：Go 的 `Must` 在 C# 里没有对应物（被调函数本身
//    就抛，Core.cs 文件头偏离 6 已登记）。
// 6) `NpmInstall` 里 `exec.Command("npm", args...); cmd.Dir = cwd; return cmd.Output()` → `Sys.Output("npm", args, cwd)`
//    （错误从 `(T, error)` 变异常，§3.2；`ata.NpmExecutor` 的 C# 契约就是"失败抛异常"，见 Project/Ata/Ata.cs 文件头）。
// 7) `signal.NotifyContext(...)` + `defer stop()` → `SignalContext` + `using`（Signal.cs）。看门狗要的 `stop`
//    （`context.CancelFunc`）→ `SignalContext.Stop()`。
// 8) `if err := s.Run(ctx); err != nil { fmt.Fprintln(os.Stderr, err); return 1 }` → C# 的 `Server.Run` 返回 void、
//    失败抛异常（Lsp/Server.cs 文件头第 4 条）⇒ `try/catch` 后打印 `ex.Message` 并返回 1（同一形状：Go 打印的是
//    `err.Error()`）。**唯独不吞 `DebugFailureException`**：Go 里 `debug.Fail` 是 panic（进程直接死），而不是
//    "Run 返回 error"，所以让它逃出去（同 Program.cs 文件头偏离 3）。
// 9) 看门狗 goroutine → 后台线程（`IsBackground = true`，对应 Go 的 goroutine 不阻止进程退出）；Go 的
//    `time.NewTicker(5s)` + `select { case <-ctx.Done(): ...; case <-ticker.C: ... }` →
//    `ctx.CancellationToken.WaitHandle.WaitOne(5s)`（同一语义：要么被取消、要么到一个 tick）。
// 10) `os.Stdout`/`os.Stdin` → `Console.OpenStandardOutput()`/`OpenStandardInput()`（`lsp.ToReader/ToWriter` 收
//    `Stream`，而 `Console.Out`/`Console.In` 是 TextReader/TextWriter，且带 Windows 的 CRLF 语义）。
using global::System.IO;
using TypeScript.Core;
using TypeScript.ContentMapper;
using TypeScript.Execute;

using static TypeScript.Bundled.Bundled;
using static TypeScript.Lsp.Lsp;
using static TypeScript.Vfs.OsVfs.Osvfs;

using LspServer = TypeScript.Lsp.Server;
using LspServerOptions = TypeScript.Lsp.ServerOptions;

namespace TypeScript.Tsc;

/// <summary>Go: <c>func runLSP(args []string) int</c>（lsp.go:20-74）。</summary>
internal static class LspCli
{
    /// <summary>Go: <c>time.NewTicker(5 * time.Second)</c>（lsp.go:100）。</summary>
    private static readonly TimeSpan _watchdogInterval = TimeSpan.FromSeconds(5);

    /// <summary>Go: <c>func runLSP(args []string) int</c>（lsp.go:20-74）。</summary>
    internal static int RunLsp(string[] args)
    {
        // Go: flags := flag.NewFlagSet("lsp", flag.ContinueOnError)
        var flags = new FlagSet("lsp");
        var stdio = flags.Bool("stdio", false, "use stdio for communication");
        var pprofDir = flags.String("pprofDir", "", "Generate pprof CPU/memory profiles to the given directory.");
        flags.String("pipe", "", "use named pipe for communication");
        flags.String("socket", "", "use socket for communication");
        var clientProcessId = flags.Int("clientProcessId", 0, "use the given PID for the parent process watchdog");
        if (!flags.TryParse(args))
        {
            return 2;
        }

        if (!stdio.Value)
        {
            Sys.StdErr.Write("only stdio is supported\n");
            return 1;
        }

        if (pprofDir.Value.Length != 0)
        {
            Sys.StdErr.Write("pprof profiles will be written to: " + pprofDir.Value + "\n");
            // Go: profileSession := pprof.BeginProfiling(*pprofDir, os.Stderr); defer profileSession.Stop()
            var profileSession = Pprof.BeginProfiling(pprofDir.Value, Sys.StdErr);
            try
            {
                return RunServer(clientProcessId.Value);
            }
            finally
            {
                profileSession.Stop();
            }
        }

        return RunServer(clientProcessId.Value);
    }

    /// <summary>
    /// Go: <c>func runLSP</c> 里 "fs := bundled.WrapFS(osvfs.FS())" 到函数末尾的那一段（lsp.go:44-73）。
    /// C# 侧拆成一个私有方法，只为让 <c>defer profileSession.Stop()</c> 的 try/finally 保持 Go 的形状（见文件头偏离 3）。
    /// </summary>
    private static int RunServer(int clientProcessId)
    {
        var fs = WrapFS(FS());
        var defaultLibraryPath = LibPath();
        var typingsLocation = GetGlobalTypingsCacheLocation();

        using var signals = SignalContext.NotifyContext();

        var s = new LspServer(new LspServerOptions
        {
            In = ToReader(Console.OpenStandardInput()),
            Out = ToWriter(Console.OpenStandardOutput()),
            Err = Sys.StdErr,
            Cwd = Directory.GetCurrentDirectory(),
            FS = fs,
            DefaultLibraryPath = defaultLibraryPath,
            TypingsLocation = typingsLocation,
            NpmInstall = (cwd, npmArgs) => Sys.Output("npm", npmArgs, cwd),
            Spawn = new SpawnerFunc((command, dir, stderr) => Sys.SpawnProcess(command, dir, stderr)),
            ProgressDelay = TimeSpan.FromMilliseconds(250),
            SetParentProcessID = NewParentProcessWatchdog(signals.Context, signals.Stop, clientProcessId),
        });

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

    /// <summary>
    /// Go: <c>func newParentProcessWatchdog(ctx context.Context, stop context.CancelFunc, clientProcessID int) func(int)</c>
    /// （lsp.go:79-90）。返回 nil 表示"平台不支持进程存活检测，或 PID 已由 <c>-clientProcessId</c> 显式给出"。
    /// </summary>
    private static Action<int>? NewParentProcessWatchdog(Context ctx, Action stop, int clientProcessId)
    {
        if (!ProcessAlive.Supported)
        {
            return null;
        }
        if (clientProcessId > 0)
        {
            StartParentProcessWatchdog(ctx, stop, clientProcessId);
            return null;
        }
        return parentPid => StartParentProcessWatchdog(ctx, stop, parentPid);
    }

    /// <summary>
    /// Go: <c>func startParentProcessWatchdog(ctx context.Context, stop context.CancelFunc, parentPID int)</c>
    /// （lsp.go:95-115）——父进程死掉就取消 ctx，避免编辑器崩溃后留下孤儿 language server。
    /// </summary>
    private static void StartParentProcessWatchdog(Context ctx, Action stop, int parentPid)
    {
        if (parentPid <= 0)
        {
            return;
        }

        var watchdog = new Thread(
            () =>
            {
                // Go: for { select { case <-ctx.Done(): return; case <-ticker.C: ... } }
                while (!ctx.CancellationToken.WaitHandle.WaitOne(_watchdogInterval))
                {
                    if (!ProcessAlive.IsProcessAlive(parentPid))
                    {
                        Sys.StdErr.Write("Parent process " + parentPid + " has exited, shutting down.\n");
                        stop();
                        return;
                    }
                }
            })
        {
            IsBackground = true,
            Name = "tsc-parent-watchdog",
        };
        watchdog.Start();
    }
}
