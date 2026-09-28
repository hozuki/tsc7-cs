// ported from: cmd/tsc/sys.go
//
// 与 Go 的偏离（逐条给依据；命名空间/类型映射见 docs/architecture.md §2/§3）：
//
// 1) `osSys`（未导出）→ `internal sealed class OsSys`；Go 的包级 `newSystem()`/`spawnProcess` → `Sys` 静态类的
//    `NewSystem()`/`SpawnProcess()`（`package main` 没有包名，故不叫 `Main`——`Program` 已占用该名字）。
// 2) 接口 `tsc.System` 与根命名空间 `System` 同名（Execute/Tsc/Compile.cs 文件头偏离 1）→ 本文件**禁止**裸
//    `System.` 前缀，接口用别名 `TscSystem`（该名字不与任何命名空间重名，§2 的别名规则）。
// 3) `io.Writer` → `System.IO.TextWriter`；**stdout/stderr 的 NewLine 必须显式设成 "\n"**：Go 的 `fmt.Fprintln`
//    恒写 LF，而 .NET 的 `TextWriter.NewLine` 在 Windows 上默认 "\r\n"——本仓已就此踩过坑（tests 的
//    `LfStringWriter`），CLI 的 stdout 会被逐字节对拍，差一个字符就全红。同时显式用 **UTF-8（无 BOM）** 包住
//    `os.Stdout`/`os.Stderr` 的等价物：Go 直接往外写 UTF-8 字节，而 .NET 的 `Console.Out` 在**接控制台**时用的是
//    控制台代码页（中文 Windows 上是 936），非 ASCII 输出会与 Go 不同字节。`AutoFlush = true` 对应 Go 的无缓冲
//    `*os.File`（每行一次 write）。
// 4) `time.Time`/`time.Duration` → `DateTime`/`TimeSpan`（统一 UTC，同 tests/TypeScript.Harness/TscTests/TestSys.cs
//    与 tests/TypeScript.Compiler.Tests/Execute/TscTestSystem.cs 的 System 实现）。`Now()`/`SinceStart()` 只被
//    statistics/tracing 用来算耗时与写 trace 时间戳，UTC 与本地时间的差值不影响任何输出（Go 的 time.Now() 带本地
//    时区，但两处消费者都只做**减法**）。
// 5) `GetEnvironmentVariable(name) (string, bool)` → `TryGetEnvironmentVariable(name, out value)`（§3.2）。用
//    `Environment.GetEnvironmentVariable` 的 null 表示"未定义"（.NET 5+ 对"定义为空串"返回 ""），与 `os.LookupEnv`
//    逐字一致；不能写成 `return value != ""`——那会把"定义为空串"误判成未定义。
// 6) `term.IsTerminal(int(os.Stdout.Fd()))` → `!Console.IsOutputRedirected`；`term.GetSize(...)` → `Console.WindowWidth`
//    （重定向/无控制台时抛 IOException/PlatformNotSupportedException → 0，对应 Go 的 `width, _, _ := term.GetSize(...)`
//    在出错时 width 为零值）。
// 7) `exec.Command` + `StdinPipe`/`StdoutPipe`/`Stderr = stderr` → `Process` + `RedirectStandard*`；子进程 stderr 到
//    调用方 writer 的搬运在 Go 里由 os/exec 内部的 goroutine 做，C# 侧用一个后台线程做同一件事（`CopyTo`）。
//    `cmd.WaitDelay = time.Second` 在 BCL 里没有对应物 → `Dispose` 里"关 stdin → Kill → 最多等 1 秒 → 收尸"，
//    Go 的 WaitDelay 正是这个语义（I/O 管道关闭后给进程 1 秒退出，超时则 Kill）。
// 8) `childProcess`（未导出）→ `ChildProcessStream`（BCL 的 `Stream`）。Go 的 `ReadWriteCloser` + `ExitCode() (int, bool)`
//    在 C# 里是 `Stream` + `ContentMapper.IProcessExitState.TryExitCode`（contentmapper 就靠后者报
//    "mapper 进程在初始化前退出，退出码 N"）。
// 9) `(T, error)` → 抛异常（§3.2）：Go 的 `spawnProcess` 用 error 报告 "建管道/启动失败"，C# 侧这些失败自然抛
//    （`Win32Exception`/`IOException`）；Go 的 `Close()` 忽略 `*exec.ExitError` 与 `exec.ErrWaitDelay`（"子进程非 0
//    退出不算 Close 失败"）→ C# 侧 `Dispose` 按同一流程走，不看异常种类。
// 10) `errors.AsType[*exec.ExitError]`（Go 1.26 的泛型版 errors.As）→ 不适用（见 9）。
using global::System;
using global::System.ComponentModel;
using global::System.Diagnostics.CodeAnalysis;
using global::System.IO;
using global::System.Text;
using TypeScript.ContentMapper;
using TypeScript.Vfs;

using static TypeScript.Bundled.Bundled;
using static TypeScript.TSPath.TSPath;
using static TypeScript.Vfs.OsVfs.Osvfs;

using Process = global::System.Diagnostics.Process;
using ProcessStartInfo = global::System.Diagnostics.ProcessStartInfo;
using TscExitStatus = TypeScript.Execute.Tsc.ExitStatus;
using TscSystem = TypeScript.Execute.Tsc.System;

namespace TypeScript.Tsc;

/// <summary>Go: <c>package main</c> 的包级常量/函数（sys.go 的 <c>newSystem</c>/<c>spawnProcess</c>）。</summary>
internal static class Sys
{
    /// <summary>Go: <c>os.Stdout</c>（见文件头偏离 3）。</summary>
    internal static readonly TextWriter StdOut = NewStdWriter(Console.OpenStandardOutput());

    /// <summary>Go: <c>os.Stderr</c>（见文件头偏离 3）。</summary>
    internal static readonly TextWriter StdErr = NewStdWriter(Console.OpenStandardError());

    private static TextWriter NewStdWriter(Stream stream) =>
        new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
            NewLine = "\n",
        };

    /// <summary>Go: <c>func newSystem() *osSys</c>（sys.go:124-138）。</summary>
    internal static OsSys NewSystem()
    {
        string cwd;
        try
        {
            cwd = Directory.GetCurrentDirectory();
        }
        catch (Exception ex)
        {
            // Go: fmt.Fprintf(os.Stderr, "Error getting current directory: %v\n", err); os.Exit(int(tsc.ExitStatusInvalidProject_OutputsSkipped))
            StdErr.Write("Error getting current directory: " + ex.Message + "\n");
            ExitInvalidProject();
            cwd = "";   // 不可达（ExitInvalidProject 标了 [DoesNotReturn]）；仅为满足确定赋值分析。
        }

        return new OsSys(
            writer: StdOut,
            errorWriter: StdErr,
            fs: WrapFS(FS()),
            defaultLibraryPath: LibPath(),
            cwd: NormalizePath(cwd),
            start: DateTime.UtcNow);
    }

    /// <summary>Go: <c>os.Exit(int(tsc.ExitStatusInvalidProject_OutputsSkipped))</c>（sys.go:128）。</summary>
    [DoesNotReturn]
    private static void ExitInvalidProject() => Environment.Exit((int)TscExitStatus.InvalidProject_OutputsSkipped);

    /// <summary>Go: <c>func spawnProcess(command []string, dir string, stderr io.Writer) (io.ReadWriteCloser, error)</c>（sys.go:74-91）。</summary>
    internal static Stream SpawnProcess(string[] command, string dir, Stream stderr)
    {
        var startInfo = new ProcessStartInfo(command[0])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (dir.Length != 0)
        {
            startInfo.WorkingDirectory = dir;
        }
        for (var i = 1; i < command.Length; i++)
        {
            startInfo.ArgumentList.Add(command[i]);
        }

        var process = Process.Start(startInfo) ?? throw new IOException("failed to start process: " + command[0]);

        // Go: cmd.Stderr = stderr（os/exec 内部起 goroutine 把子进程 stderr 搬到该 writer）。
        var stderrPump = new Thread(
            () =>
            {
                try
                {
                    process.StandardError.BaseStream.CopyTo(stderr);
                }
                catch (Exception)
                {
                    // Go 的 os/exec 在这里丢弃 copy 错误（管道被关时正常发生）。
                }
            })
        {
            IsBackground = true,
            Name = "tsc-child-stderr",
        };
        stderrPump.Start();

        return new ChildProcessStream(process, process.StandardInput.BaseStream, process.StandardOutput.BaseStream, stderrPump);
    }

    /// <summary>
    /// Go: <c>cmd := exec.Command("npm", args...); cmd.Dir = cwd; return cmd.Output()</c>（lsp.go:59-63）——
    /// 跑完、把 stdout 收成 <c>[]byte</c>；非 0 退出在 Go 里是 <c>*exec.ExitError</c>（含 stderr），C# 侧抛
    /// <see cref="ProcessExitException"/>（§3.2）。
    /// </summary>
    internal static byte[] Output(string file, string[] args, string dir)
    {
        var startInfo = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (dir.Length != 0)
        {
            startInfo.WorkingDirectory = dir;
        }
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new IOException("failed to start process: " + file);
        using var stdout = new MemoryStream();
        var stdoutPump = new Thread(
            () =>
            {
                try
                {
                    process.StandardOutput.BaseStream.CopyTo(stdout);
                }
                catch (Exception)
                {
                }
            })
        {
            IsBackground = true,
            Name = "tsc-child-stdout",
        };
        stdoutPump.Start();
        // Go: cmd.Output() 在进程结束后一并给出 stdout；stderr 只进 *exec.ExitError。
        var stderrText = process.StandardError.ReadToEnd();
        process.WaitForExit();
        stdoutPump.Join();

        if (process.ExitCode != 0)
        {
            // Go: *exec.ExitError 的 Error() 是 "exit status N"，Stderr 字段带子进程的 stderr。
            throw new ProcessExitException(process.ExitCode, stderrText);
        }
        return stdout.ToArray();
    }
}

/// <summary>Go: <c>type osSys struct</c>（sys.go:19-25）——进程级 <c>tsc.System</c> 实现。</summary>
internal sealed class OsSys : TscSystem
{
    /// <summary>Go: <c>writer io.Writer</c>（os.Stdout；NewLine 已固定为 "\n"，见 Sys 文件头偏离 3）。</summary>
    private readonly TextWriter _writer;

    /// <summary>Go: <c>ErrorWriter() io.Writer</c> 返回的 os.Stderr。</summary>
    private readonly TextWriter _errorWriter;

    /// <summary>Go: <c>fs vfs.FS</c>。</summary>
    private readonly FS _fs;

    /// <summary>Go: <c>defaultLibraryPath string</c>。</summary>
    private readonly string _defaultLibraryPath;

    /// <summary>Go: <c>cwd string</c>。</summary>
    private readonly string _cwd;

    /// <summary>Go: <c>start time.Time</c>。</summary>
    private readonly DateTime _start;

    internal OsSys(TextWriter writer, TextWriter errorWriter, FS fs, string defaultLibraryPath, string cwd, DateTime start)
    {
        _writer = writer;
        _errorWriter = errorWriter;
        _fs = fs;
        _defaultLibraryPath = defaultLibraryPath;
        _cwd = cwd;
        _start = start;
    }

    /// <summary>Go: <c>func (s *osSys) SinceStart() time.Duration</c>（sys.go:27-29）。</summary>
    public TimeSpan SinceStart() => DateTime.UtcNow - _start;

    /// <summary>Go: <c>func (s *osSys) Now() time.Time</c>（sys.go:31-33）。</summary>
    public DateTime Now() => DateTime.UtcNow;

    /// <summary>Go: <c>func (s *osSys) FS() vfs.FS</c>（sys.go:35-37）。</summary>
    public FS FS() => _fs;

    /// <summary>Go: <c>func (s *osSys) DefaultLibraryPath() string</c>（sys.go:39-41）。</summary>
    public string DefaultLibraryPath() => _defaultLibraryPath;

    /// <summary>Go: <c>func (s *osSys) GetCurrentDirectory() string</c>（sys.go:43-45）。</summary>
    public string GetCurrentDirectory() => _cwd;

    /// <summary>Go: <c>func (s *osSys) Writer() io.Writer</c>（sys.go:47-49）。</summary>
    public TextWriter Writer() => _writer;

    /// <summary>Go: <c>func (s *osSys) ErrorWriter() io.Writer</c>（sys.go:51-53）。</summary>
    public TextWriter ErrorWriter() => _errorWriter;

    /// <summary>Go: <c>func (s *osSys) WriteOutputIsTTY() bool</c>（sys.go:55-57）。</summary>
    public bool WriteOutputIsTTY() => !Console.IsOutputRedirected;

    /// <summary>Go: <c>func (s *osSys) GetWidthOfTerminal() int</c>（sys.go:59-62）。</summary>
    public int GetWidthOfTerminal()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (PlatformNotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>Go: <c>func (s *osSys) GetEnvironmentVariable(name string) (string, bool)</c>（sys.go:64-66）。</summary>
    public bool TryGetEnvironmentVariable(string name, out string value)
    {
        var found = Environment.GetEnvironmentVariable(name);
        value = found ?? "";
        return found is not null;
    }

    /// <summary>Go: <c>func (s *osSys) Spawn(command []string, dir string, stderr io.Writer) (io.ReadWriteCloser, error)</c>（sys.go:68-70）。</summary>
    public Stream Spawn(string[] command, string dir, Stream stderr) => Sys.SpawnProcess(command, dir, stderr);
}

/// <summary>
/// Go: <c>type childProcess struct</c>（sys.go:95-122）：把子进程的 stdout 当读端、stdin 当写端合成一个流；
/// <c>Close</c> 杀掉并收尸。
/// </summary>
internal sealed class ChildProcessStream(Process process, Stream stdin, Stream stdout, Thread stderrPump) : Stream, IProcessExitState
{
    /// <summary>Go: <c>cmd *exec.Cmd</c>。</summary>
    private readonly Process _process = process;

    /// <summary>Go: <c>stdin io.WriteCloser</c>。</summary>
    private readonly Stream _stdin = stdin;

    /// <summary>Go: <c>stdout io.Reader</c>。</summary>
    private readonly Stream _stdout = stdout;

    /// <summary>Go: os/exec 内部搬运 stderr 的 goroutine（见 Sys 文件头偏离 7）。</summary>
    private readonly Thread _stderrPump = stderrPump;

    private bool _closed;

    /// <summary>Go: <c>cmd.ProcessState.ExitCode()</c> 的缓存（Close 之后 Process 已释放，见 <see cref="TryExitCode"/>）。</summary>
    private int _exitCode;

    /// <summary>Go: <c>cmd.ProcessState != nil</c>（= 已经 Wait 过）。</summary>
    private bool _hasExitCode;

    /// <summary>Go: <c>cmd.WaitDelay = time.Second</c>（见 Sys 文件头偏离 7）。</summary>
    private const int _waitDelayMs = 1000;

    public override bool CanRead => true;

    public override bool CanWrite => true;

    public override bool CanSeek => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>Go: <c>func (p *childProcess) Read(b []byte) (int, error)</c>（sys.go:101）。</summary>
    public override int Read(byte[] buffer, int offset, int count) => _stdout.Read(buffer, offset, count);

    /// <summary>Go: <c>func (p *childProcess) Read(b []byte) (int, error)</c>（sys.go:101）。</summary>
    public override int Read(Span<byte> buffer) => _stdout.Read(buffer);

    /// <summary>Go: <c>func (p *childProcess) Write(b []byte) (int, error)</c>（sys.go:102）。</summary>
    public override void Write(byte[] buffer, int offset, int count) => _stdin.Write(buffer, offset, count);

    /// <summary>Go: <c>func (p *childProcess) Write(b []byte) (int, error)</c>（sys.go:102）。</summary>
    public override void Write(ReadOnlySpan<byte> buffer) => _stdin.Write(buffer);

    public override void Flush() => _stdin.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Go: <c>func (p *childProcess) ExitCode() (int, bool)</c>（sys.go:104-109）——<c>ProcessState == nil</c> 时是 (0, false)。
    /// **偏离**：Go 的 <c>cmd.ProcessState</c> 在 Wait 之后一直有效，而 C# 的 <c>Process</c> 一旦 Dispose，
    /// <c>HasExited</c>/<c>ExitCode</c> 就抛 <c>InvalidOperationException</c>（"No process is associated with this object"，
    /// 实测）→ 退出码在 <see cref="Dispose"/> 里存下来，Close 之后仍能像 Go 一样返回真值
    /// （contentmapper 的 <c>closeOnceReadWriteCloser</c> 就会在 Close 之后透传 ExitCode）。
    /// </summary>
    public bool TryExitCode(out int exitCode)
    {
        if (_closed)
        {
            exitCode = _exitCode;
            return _hasExitCode;
        }
        if (!_process.HasExited)
        {
            exitCode = 0;
            return false;
        }
        exitCode = _process.ExitCode;
        return true;
    }

    /// <summary>Go: <c>func (p *childProcess) Close() error</c>（sys.go:111-122）。</summary>
    protected override void Dispose(bool disposing)
    {
        if (!disposing || _closed)
        {
            base.Dispose(disposing);
            return;
        }
        _closed = true;

        // Go: _ = p.stdin.Close()
        try
        {
            _stdin.Dispose();
        }
        catch (IOException)
        {
        }

        // Go: _ = p.cmd.Process.Kill()（已经退出时 Kill 会失败，Go 一样忽略）。
        KillIgnoringErrors();

        // Go: cmd.WaitDelay = time.Second + err := p.cmd.Wait()（见 Sys 文件头偏离 7/9）。
        if (!_process.WaitForExit(_waitDelayMs))
        {
            KillIgnoringErrors();
            _process.WaitForExit();
        }
        // Go 的 cmd.Wait() 总会填好 ProcessState；这里在释放 Process 之前把退出码存下来（见 TryExitCode 的偏离说明）。
        _hasExitCode = _process.HasExited;
        if (_hasExitCode)
        {
            _exitCode = _process.ExitCode;
        }
        _stderrPump.Join();
        _process.Dispose();

        base.Dispose(disposing);
    }

    private void KillIgnoringErrors()
    {
        try
        {
            _process.Kill();
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }
}

/// <summary>Go: <c>*exec.ExitError</c>（<see cref="Sys.Output"/> 的失败路径；§3.2 的 <c>(T, error)</c> → 异常）。</summary>
internal sealed class ProcessExitException(int exitCode, string stderr)
    : Exception("exit status " + exitCode + (stderr.Length != 0 ? ": " + stderr : ""))
{
    /// <summary>Go: <c>ExitError.ProcessState.ExitCode()</c>。</summary>
    internal readonly int ExitCode = exitCode;

    /// <summary>Go: <c>ExitError.Stderr</c>。</summary>
    internal readonly string Stderr = stderr;
}
