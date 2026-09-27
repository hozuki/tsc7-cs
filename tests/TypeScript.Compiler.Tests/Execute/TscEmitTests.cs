// ported from: internal/execute/tsc/emit_test.go
//
// 与 Go 的偏离（都是机械替换；除下面第 5 条标注的平台差异外不改变断言强度）：
// 1) Go 的假 system 用"结构体内嵌 + 外层方法遮蔽"实现（`contentMapperLoggingTestSystem` 内嵌
//    `*timingTestSystem`）→ C# 用继承 + `virtual`/`override`（`TimingTestSystem` → `ContentMapperLoggingTestSystem`）。
//    Go 的 `System` 接口在 C# 是命名空间级接口 `TypeScript.Execute.Tsc.System`（Compile.cs:38），因为 `System`
//    与根命名空间同名，本文件用别名 `TSys`，且不写裸 `System.` 前缀。
// 2) Go 的 `io.Writer` → `TextWriter`：`io.Discard` → `TextWriter.Null`；`errors.New(...)` 的 Spawn 失败 → 抛
//    `NotSupportedException`（§3.2：Go 的 error → 异常）。`Spawn` 来自 `ContentMapper.Spawner`
//    （`Stream Spawn(string[] command, string dir, Stream stderr)`）。
// 3) `GetEnvironmentVariable(name) (string, bool)` → `TryGetEnvironmentVariable(name, out value)`（§3.2）。
// 4) `time.Time`/`time.Duration` → `DateTime`/`TimeSpan`：`time.Unix(0, 0)` → `DateTime.UnixEpoch`；
//    `NestedEmitNow` 的字段类型是 `Func<DateTime>`（Go 的 `func() time.Time`）。
// 5) `bytes.Buffer` + `fmt.Fprintln`（写 "\n"）→ C# 的 `StringWriter`（`WriteLine` 默认写平台换行 "\r\n"，
//    而 Go 的 `contentMapperLogger` 落成 C# 的 `ErrorWriter().WriteLine(message)`，见 Compile.cs:82）：
//    期望值保持 Go 的 `strings.Repeat("mapper log\n", 10)`，故测试里的 StringWriter 显式设 `NewLine = "\n"`
//    （产品代码不写死换行，这是 Go/Windows 的换行差异，不是断言放松：仍是 10 行逐字节相等）。
//    Go 的 `sync.WaitGroup` + 10 个 goroutine → `Parallel.For(0, 10, …)`（C# logger 内部有锁）。
// 6) `vfstest.FromMapWithClock`/`tsoptions.NewParsedCommandLine`/`compiler.NewCachedFSCompilerHost`/
//    `compiler.NewProgram`/`incremental.NewProgram`/`incremental.CreateHost` 的 C# 形态见各自文件头；`NewProgram` 是
//    `TypeScript.Execute.Incremental.Program`（别名 `Inc.Program`）上的静态方法，`CreateHost` 在
//    `Inc.Incremental` 静态类上。
// 7) `EmitInput.CompileTimes` 在 C# 是**非空**类型（Go 是指针，调用方总是给值）→ 直接传
//    `new CompileTimes()`，`times` 的读取同 Go 的 `times.checkTime`/`times.emitTime`（internal 字段 `_checkTime`/`_emitTime`）；
//    `compiler.WriteFile` 在 C# 是 `delegate void WriteFile(string, string, WriteFileData)`（Go 返回 error）→
//    `(f, t, d) => sys.FS().WriteFile(f, t)`；`QuietDiagnosticReporter`/`QuietDiagnosticsReporter` 是
//    `TypeScript.Execute.Tsc.Tsc` 上的静态方法，直接做方法组转换。
// 8) Go 的 `t.Fatal`/`t.Fatalf` → xunit 的 `Assert.Equal`/`Assert.NotNull`（比较语义逐条对应）；
//    `.NET` 版测试的编译主体跑在 64 MiB 栈的线程上（docs/architecture.md §3.9：parser/chunker/printer 递归很深），
//    线程内异常用 `ExceptionDispatchInfo` 原样重抛（同 tests/TypeScript.Harness/HarnessTest.cs:163-179）。
// 9) Go 的 `t.Parallel()` 由 xunit 调度，不逐字搬。
//
// 命名空间：用 `TypeScript.Tests.Execute`（镜像 Execute 目录；先例 tests/TypeScript.Compiler.Tests/Printer/UtilitiesTests.cs 的
// `TypeScript.Tests.Printer`）。本切片的验收命令是 `--filter "FullyQualifiedName~Execute"`，而目录名不进 FQN，
// 只有命名空间带 "Execute" 才会被它选中。
// 未移植：无（emit_test.go 的 2 个测试全部覆盖）。
using System.Runtime.ExceptionServices;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.TSPath;
using TypeScript.TsOptions;
using TypeScript.Vfs;
using TypeScript.Vfs.VfsTest;
using Xunit;
using Comp = TypeScript.Compiler.Compiler;
using Inc = TypeScript.Execute.Incremental;
using TCompileTimes = TypeScript.Execute.Tsc.CompileTimes;
using TContentMapperLogger = TypeScript.Execute.Tsc.ContentMapperLogger;
using TEmitInput = TypeScript.Execute.Tsc.EmitInput;
using TSys = TypeScript.Execute.Tsc.System;

namespace TypeScript.Tests.Execute;

using static TypeScript.Execute.Tsc.Tsc;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Vfs.VfsTest.VfsTest;

public class TscEmitTests
{
    // docs/architecture.md §3.9 / tests/TypeScript.Harness/HarnessTest.cs:48
    private const int _stackSize = 64 * 1024 * 1024;

    /// <summary>Go: <c>type controlledClock struct</c>（emit_test.go:56-96）。</summary>
    private sealed class ControlledClock
    {
        private readonly object _mu = new();
        private DateTime _now;
        private bool _nestedEmitInProgress;
        private int _nestedEmitCalls;

        internal ControlledClock(DateTime now) => _now = now;

        internal DateTime Now()
        {
            lock (_mu)
            {
                return _now;
            }
        }

        internal DateTime NestedEmitNow()
        {
            lock (_mu)
            {
                _nestedEmitCalls++;
                if (_nestedEmitInProgress)
                {
                    _now = _now.AddSeconds(1);
                }
                _nestedEmitInProgress = !_nestedEmitInProgress;
                return _now;
            }
        }

        internal int NestedEmitCalls()
        {
            lock (_mu)
            {
                return _nestedEmitCalls;
            }
        }

        internal TimeSpan SinceStart() => TimeSpan.Zero;
    }

    /// <summary>Go: <c>type fileClock struct</c>（emit_test.go:60-73）：每次读都前进一秒的模拟文件时钟。</summary>
    private sealed class FileClock : Clock
    {
        private readonly object _mu = new();
        private DateTime _now;

        public DateTime Now()
        {
            lock (_mu)
            {
                _now = _now.AddSeconds(1);
                return _now;
            }
        }

        public TimeSpan SinceStart() => TimeSpan.Zero;
    }

    /// <summary>Go: <c>type timingTestSystem struct</c>（emit_test.go:98-131）。</summary>
    private class TimingTestSystem : TSys
    {
        internal FS _fs = null!;
        internal ControlledClock _clock = null!;

        public TextWriter Writer() => TextWriter.Null;

        public virtual TextWriter ErrorWriter() => TextWriter.Null;

        public FS FS() => _fs;

        public string DefaultLibraryPath() => "/lib";

        public string GetCurrentDirectory() => "/project";

        public bool WriteOutputIsTTY() => false;

        public int GetWidthOfTerminal() => 0;

        public virtual bool TryGetEnvironmentVariable(string name, out string value)
        {
            value = "";
            return false;
        }

        public DateTime Now() => _clock.Now();

        public TimeSpan SinceStart() => _clock.SinceStart();

        public Stream Spawn(string[] command, string dir, Stream stderr) =>
            throw new NotSupportedException("spawn not implemented in timingTestSystem");
    }

    /// <summary>Go: <c>type contentMapperLoggingTestSystem struct</c>（emit_test.go:24-36）。</summary>
    private sealed class ContentMapperLoggingTestSystem : TimingTestSystem
    {
        private readonly StringWriter _stderr = new() { NewLine = "\n" };

        internal bool _enabled;

        public override bool TryGetEnvironmentVariable(string name, out string value)
        {
            if (name == "TS_CONTENT_MAPPER_DEBUG" && _enabled)
            {
                value = "1";
                return true;
            }
            value = "";
            return false;
        }

        public override TextWriter ErrorWriter() => _stderr;

        internal string Stderr() => _stderr.ToString();
    }

    // Go: TestContentMapperLoggerEnvironmentVariable（emit_test.go:38-54）。
    [Fact]
    public void TestContentMapperLoggerEnvironmentVariable()
    {
        var sys = new ContentMapperLoggingTestSystem();
        Assert.Null(TContentMapperLogger.New(sys));

        sys._enabled = true;
        var logger = TContentMapperLogger.New(sys);
        Assert.NotNull(logger);

        Parallel.For(0, 10, _ => logger!.Invoke("mapper log"));

        Assert.Equal(string.Concat(Enumerable.Repeat("mapper log\n", 10)), sys.Stderr());
    }

    // Go: TestIncrementalDeclarationEmitTimeIsExcludedFromCheckTime（emit_test.go:133-204）。
    [Fact]
    public void TestIncrementalDeclarationEmitTimeIsExcludedFromCheckTime()
    {
        var clock = new ControlledClock(DateTime.UnixEpoch);
        TCompileTimes times = null!;
        RunOnBigStackThread(() =>
        {
            var files = new Dictionary<string, string>
            {
                ["/lib/lib.d.ts"] = """
                    interface Array<T> {}
                    interface Boolean {}
                    interface CallableFunction {}
                    interface Function {}
                    interface IArguments {}
                    interface NewableFunction {}
                    interface Number {}
                    interface Object {}
                    interface RegExp {}
                    interface String {}
                    """,
                ["/project/hub.ts"] = """
                    export interface Box {
                        value: string;
                    }
                    export const make = (): Box => ({ value: "ok" });
                    """,
                ["/project/spoke.ts"] = """import { make, type Box } from "./hub"; export const value: Box = make();""",
            };
            var sys = new TimingTestSystem { _fs = FromMapWithClock(files, true, new FileClock()), _clock = clock };
            var options = new CompilerOptions
            {
                Declaration = Tristate.TSTrue,
                Incremental = Tristate.TSTrue,
                Module = ModuleKind.ESNext,
                NoEmit = Tristate.TSTrue,
                TsBuildInfoFile = "/project/tsconfig.tsbuildinfo",
            };
            var config = ParsedCommandLine.NewParsedCommandLine(
                options,
                ["/lib/lib.d.ts", "/project/hub.ts", "/project/spoke.ts"],
                null,
                new ComparePathsOptions(useCaseSensitiveFileNames: true, currentDirectory: "/project"));

            var (oldProgram, _) = Compile(sys, config, clock, null);
            sys.FS().WriteFile("/project/hub.ts", files["/project/hub.ts"] + "\n// comment only change\n");
            (_, times) = Compile(sys, config, clock, oldProgram);
        });

        Assert.Equal(TimeSpan.Zero, times._checkTime);
        Assert.Equal(TimeSpan.FromSeconds(2), times._emitTime);
        Assert.Equal(4, clock.NestedEmitCalls());
    }

    /// <summary>Go: 测试体内的闭包 <c>compile(oldProgram *incremental.Program) (*incremental.Program, *CompileTimes)</c>（emit_test.go:153-179）。</summary>
    private static (Inc.Program Program, TCompileTimes Times) Compile(TSys sys, ParsedCommandLine config, ControlledClock clock, Inc.Program? oldProgram)
    {
        var host = Comp.NewCachedFSCompilerHost(sys.GetCurrentDirectory(), sys.FS(), sys.DefaultLibraryPath(), null, null, null);
        var program = Comp.NewProgram(new ProgramOptions
        {
            Config = config,
            Host = host,
        });
        if (program.GetSourceFile("/lib/lib.d.ts") is null)
        {
            throw new InvalidOperationException("default library was not loaded");
        }
        var incrementalProgram = Inc.Program.NewProgram(program, oldProgram, Inc.Incremental.CreateHost(host), clock.NestedEmitNow, false);
        var times = new TCompileTimes();
        EmitFilesAndReportErrors(new TEmitInput
        {
            Sys = sys,
            ProgramLike = incrementalProgram,
            Program = program,
            Config = config,
            ReportDiagnostic = QuietDiagnosticReporter,
            ReportErrorSummary = QuietDiagnosticsReporter,
            Writer = TextWriter.Null,
            WriteFile = (fileName, text, data) => sys.FS().WriteFile(fileName, text),
            CompileTimes = times,
        });
        return (incrementalProgram, times);
    }

    /// <summary>docs/architecture.md §3.9：递归很深的入口点必须在大栈线程上跑（同 HarnessTest.cs:163-179）。</summary>
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
