// ported from: internal/execute/tsctests/sys.go
//
// 与 Go 的偏离（逐条；都是机械替换，不改变断言强度）：
// 1) 命名冲突（§2）：`TypeScript.Execute.Tsc.System` 与本文件的 `TestSys` 同时还要用 `global::System`；本文件
//    一律用别名 `TSys` 指接口、`global::System` 前缀指 BCL，且**禁止**写裸 `System.`。
// 2) `io.Writer` → `System.IO.TextWriter`；`*strings.Builder` → `StringWriter`（`NewLine = "\n"`：Go 的
//    `fmt.Fprint`/`Fprintln` 恒写 LF，而 C# 在 Windows 上默认 CRLF —— 见 §4 的换行纪律）。
// 3) `FileMap = map[string]any` → `Dictionary<string, object>`（值可以是 `string` 或 `vfstest.MapFile`，
//    与 `VfsTest.FromMap(Dictionary<string, object>, …)` 的重载逐条对应）。
// 4) `time.Time`/`time.Duration` → `DateTime`/`TimeSpan`（统一 UTC）。
// 5) `GetEnvironmentVariable(name) (string, bool)` → `TryGetEnvironmentVariable(name, out value)`（§3.2）。
// 6) Go 的 `s.fs.FS.(iovfs.FsWithSys)` 类型断言 → C# 的 `(FsWithSys)`；`fsDiffer.FS` 与 `TestFs.Inner` 是
//    同一个对象（`FromMapWithClock` 返回的就是 `Iovfs.From(...)` = `FsWithSys`）。
// 7) `panic(...)` → `Debug.Fail(...)`（抛 `DebugFailureException`，§2）。
// 8) Go 的 `TestSys` 用"结构体内嵌"满足 `tsc.CommandLineTesting`，仍是同一批方法；C# 里显式列出接口
//    （`TSys, CommandLineTesting, CommandLineTestingWithWatchBackend`）。
// 9) `diagnostics.Version_0.Localize(locale, core.Version())` → `Version_0.Localize(loc, Core.Version())`。
// 10) Go 的 `strings.IndexRune(statusLine, ':')`（字节下标）→ `IndexOf(':')`（ASCII 下等价）。
// 11) `core.Must(strconv.Atoi(...))` → `int.Parse(..., InvariantCulture)`。
// 未移植：无（sys.go 的每个成员都有对应物）。
namespace TypeScript.Harness.TscTests;

using global::System;
using global::System.Globalization;
using global::System.IO;
using global::System.Text;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Diagnostics.Diagnostics;
using TypeScript.Execute.Incremental;
using TypeScript.Locale;
using TypeScript.TestUtil;
using TypeScript.TestUtil.ContentMapperTest;
using TypeScript.TestUtil.FsBaselineUtil;
using TypeScript.TestUtil.HarnessUtil;
using TypeScript.TSPath;
using TypeScript.Vfs;
using TypeScript.Vfs.Iovfs;
using TypeScript.Vfs.VfsTest;
using Cm = TypeScript.ContentMapper.ContentMapper;
using IncProgram = TypeScript.Execute.Incremental.Program;
using Loc = TypeScript.Locale.Locale;
using Path = TypeScript.TSPath.Path;
using Exec = TypeScript.Execute.Execute;
using TSys = TypeScript.Execute.Tsc.System;
using TscCommandLineTesting = TypeScript.Execute.Tsc.CommandLineTesting;
using TscTraceFunc = TypeScript.Execute.Tsc.TraceFunc;
using WmWatchBackend = TypeScript.Execute.WatchManager.WatchBackend;
using WmCommandLineTestingWithWatchBackend = TypeScript.Execute.WatchManager.CommandLineTestingWithWatchBackend;

using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;
using static TypeScript.Vfs.VfsTest.VfsTest;

/// <summary>Go: <c>type FileMap map[string]any</c>。</summary>
public sealed class FileMap : Dictionary<string, object>
{
}

/// <summary>Go: tsctests 包级常量/函数（<c>tscLibPath</c>/<c>tscDefaultLibContent</c>/<c>getTestLibPathFor</c>）。</summary>
public static class TscTestEnv
{
    /// <summary>Go: <c>var tscLibPath = "/home/src/tslibs/TS/Lib"</c>。</summary>
    public const string TscLibPath = "/home/src/tslibs/TS/Lib";

    /// <summary>
    /// Go: <c>var tscDefaultLibContent = stringtestutil.Dedent(`…`)</c>。Go 的模板字符串本身**零缩进**，
    /// 所以 Dedent 只去掉首尾空行；这里就是那 22 行以 "\n" 连接（无尾换行）的逐字内容。
    /// </summary>
    public const string TscDefaultLibContent =
        "/// <reference no-default-lib=\"true\"/>\n" +
        "interface Boolean {}\n" +
        "interface Function {}\n" +
        "interface CallableFunction {}\n" +
        "interface NewableFunction {}\n" +
        "interface IArguments {}\n" +
        "interface Number { toExponential: any; }\n" +
        "interface Object {}\n" +
        "interface RegExp {}\n" +
        "interface String { charAt: any; }\n" +
        "interface Array<T> { length: number; [n: number]: T; }\n" +
        "interface ReadonlyArray<T> {}\n" +
        "interface SymbolConstructor {\n" +
        "    (desc?: string | number): symbol;\n" +
        "    for(name: string): symbol;\n" +
        "    readonly toStringTag: symbol;\n" +
        "}\n" +
        "declare var Symbol: SymbolConstructor;\n" +
        "interface Symbol {\n" +
        "    readonly [Symbol.toStringTag]: string;\n" +
        "}\n" +
        "declare const console: { log(msg: any): void; };";

    /// <summary>Go: <c>func getTestLibPathFor(libName string) string</c>。</summary>
    public static string GetTestLibPathFor(string libName)
    {
        var (value, ok) = LibMap.Get(libName);
        var libFile = ok ? (string)value! : "lib." + libName + ".d.ts";
        return TscLibPath + "/" + libFile;
    }
}

/// <summary>Go: 写 "\n" 的 <see cref="StringWriter"/>（见文件头偏离 2）。</summary>
public sealed class LfStringWriter : StringWriter
{
    public LfStringWriter()
    {
        NewLine = "\n";
    }

    public LfStringWriter(StringBuilder builder) : base(builder)
    {
        NewLine = "\n";
    }
}

/// <summary>Go: <c>type TestClock struct</c>（tsctests/sys.go:69-84）——每次 <c>Now()</c> 前进一秒。</summary>
public sealed class TestClock : Clock
{
    private readonly object _mu = new();
    private readonly DateTime _start = DateTime.UtcNow;
    private DateTime _now;

    /// <summary>Go: <c>func (t *TestClock) Now() time.Time</c>。</summary>
    public DateTime Now()
    {
        lock (_mu)
        {
            if (_now == default)
            {
                _now = _start;
            }

            _now = _now.AddSeconds(1); // Simulate some time passing
            return _now;
        }
    }

    /// <summary>Go: <c>func (t *TestClock) SinceStart() time.Duration</c>。</summary>
    public TimeSpan SinceStart() => Now() - _start;
}

/// <summary>Go: <c>type TestSys struct</c>（tsctests/sys.go:154-168）。</summary>
public sealed class TestSys : TSys, TscCommandLineTesting, WmCommandLineTestingWithWatchBackend
{
    private StringWriter _currentWrite = new LfStringWriter();
    private readonly StringBuilder _programBaselines = new();
    private readonly StringBuilder _programIncludeBaselines = new();
    private TracerForBaselining _tracer = null!;
    private FSDiffer _fsDiffer = null!;
    private bool _forIncrementalCorrectness;
    private MockWatchBackend _mockWatchBackend = null!;

    /// <summary>Go: <c>fs *testFs</c>。</summary>
    public TestFs Fs = null!;

    /// <summary>Go: <c>defaultLibraryPath string</c>。</summary>
    public string DefaultLibraryPathValue = "";

    /// <summary>Go: <c>cwd string</c>。</summary>
    public string Cwd = "";

    /// <summary>Go: <c>env map[string]string</c>（可空 = Go 的 nil map）。</summary>
    public Dictionary<string, string>? Env;

    /// <summary>Go: <c>outputIsTTY bool</c>。</summary>
    public bool OutputIsTTY = true;

    /// <summary>Go: <c>clock *TestClock</c>。</summary>
    public TestClock Clock = new();

    /// <summary>Go: <c>func NewTscSystem(files FileMap, useCaseSensitiveFileNames bool, cwd string) *TestSys</c>。</summary>
    public static TestSys NewTscSystem(FileMap files, bool useCaseSensitiveFileNames, string cwd)
    {
        var clock = new TestClock();
        var sys = new TestSys
        {
            Clock = clock,
            Cwd = cwd,
            OutputIsTTY = true,
        };
        sys.Fs = new TestFs(FromMapWithClock(ToObjectMap(files), useCaseSensitiveFileNames, clock));
        return sys;
    }

    /// <summary>Go: <c>func GetFileMapWithBuild(files FileMap, commandLineArgs []string) FileMap</c>。</summary>
    public static FileMap GetFileMapWithBuild(FileMap files, string[] commandLineArgs)
    {
        var copy = new FileMap();
        foreach (var kv in files)
        {
            copy[kv.Key] = kv.Value;
        }

        var sys = NewTestSys(new TscInput { Files = copy }, forIncrementalCorrectness: false);
        Exec.CommandLine(Context.Background, sys, commandLineArgs, sys);
        sys.Fs.WrittenFiles.Range(key =>
        {
            if (sys.FsFromFileMap().TryReadFile(key, out var text))
            {
                files[key] = text;
            }

            return true;
        });
        return files;
    }

    /// <summary>Go: <c>func newTestSys(tscInput *tscInput, forIncrementalCorrectness bool) *TestSys</c>（tsctests/sys.go:105-152）。</summary>
    public static TestSys NewTestSys(TscInput tscInput, bool forIncrementalCorrectness)
    {
        var cwd = tscInput.Cwd;
        if (cwd.Length == 0)
        {
            cwd = "/home/src/workspaces/project";
        }

        var libPath = TscTestEnv.TscLibPath;
        if (tscInput.WindowsStyleRoot.Length != 0)
        {
            libPath = tscInput.WindowsStyleRoot + libPath[1..];
        }

        var sys = NewTscSystem(tscInput.Files, !tscInput.IgnoreCase, cwd);
        sys.DefaultLibraryPathValue = libPath;
        sys._currentWrite = new LfStringWriter();
        if (tscInput.OutputIsTTY is { } outputIsTTY)
        {
            sys.OutputIsTTY = outputIsTTY;
        }

        sys._tracer = TracerForBaselining.NewTracerForBaselining(
            new ComparePathsOptions(!tscInput.IgnoreCase, cwd),
            sys._currentWrite);
        sys.Env = tscInput.Env;
        sys._forIncrementalCorrectness = forIncrementalCorrectness;
        sys._mockWatchBackend = new MockWatchBackend
        {
            DirectoryExists = sys.Fs.DirectoryExists,
            UseCaseSensitiveFileNames = !tscInput.IgnoreCase,
        };
        sys._fsDiffer = new FSDiffer
        {
            FS = (FsWithSys)sys.Fs.Inner,
            DefaultLibs = () => sys.Fs.DefaultLibs,
            WrittenFiles = sys.Fs.WrittenFiles,
        };

        // Ensure the default library file is present
        sys.EnsureLibPathExists("lib.d.ts");
        foreach (var libFile in TargetToLibMap().Values)
        {
            sys.EnsureLibPathExists(libFile);
        }

        foreach (var libFile in LibFilesSet.Keys())
        {
            sys.EnsureLibPathExists(libFile);
        }

        return sys;
    }

    private static Dictionary<string, object> ToObjectMap(FileMap files) => files;

    /// <summary>Go: <c>func (s *TestSys) Now() time.Time</c>。</summary>
    public DateTime Now() => Clock.Now();

    /// <summary>Go: <c>func (s *TestSys) SinceStart() time.Duration</c>。</summary>
    public TimeSpan SinceStart() => Clock.SinceStart();

    /// <summary>Go: <c>func (s *TestSys) FS() vfs.FS</c>。</summary>
    public FS FS() => Fs;

    /// <summary>Go: <c>func (s *TestSys) fsFromFileMap() iovfs.FsWithSys</c>。</summary>
    public FsWithSys FsFromFileMap() => _fsDiffer.FS;

    /// <summary>Go: <c>func (s *TestSys) mapFs() *vfstest.MapFS</c>。</summary>
    public MapFS MapFs() => _fsDiffer.MapFs();

    /// <summary>Go: <c>func (s *TestSys) ensureLibPathExists(path string)</c>。</summary>
    public void EnsureLibPathExists(string path)
    {
        path = DefaultLibraryPathValue + "/" + path;
        if (!FsFromFileMap().TryReadFile(path, out _))
        {
            Fs.DefaultLibs ??= new SyncSet<string>();
            Fs.DefaultLibs.Add(path);
            FsFromFileMap().WriteFile(path, TscTestEnv.TscDefaultLibContent);
        }
    }

    /// <summary>Go: <c>func (s *TestSys) DefaultLibraryPath() string</c>。</summary>
    public string DefaultLibraryPath() => DefaultLibraryPathValue;

    /// <summary>Go: <c>func (s *TestSys) GetCurrentDirectory() string</c>。</summary>
    public string GetCurrentDirectory() => Cwd;

    /// <summary>Go: <c>func (s *TestSys) Writer() io.Writer</c>。</summary>
    public TextWriter Writer() => _currentWrite;

    /// <summary>Go: <c>func (s *TestSys) ErrorWriter() io.Writer</c>。</summary>
    public TextWriter ErrorWriter() => _currentWrite;

    /// <summary>Go: <c>func (s *TestSys) WriteOutputIsTTY() bool</c>。</summary>
    public bool WriteOutputIsTTY() => OutputIsTTY;

    /// <summary>Go: <c>func (s *TestSys) GetWidthOfTerminal() int</c>。</summary>
    public int GetWidthOfTerminal()
    {
        if (TryGetEnvironmentVariable("TS_TEST_TERMINAL_WIDTH", out var widthStr) && widthStr.Length != 0)
        {
            return int.Parse(widthStr, CultureInfo.InvariantCulture);
        }

        return 0;
    }

    /// <summary>Go: <c>func (s *TestSys) GetEnvironmentVariable(name string) (string, bool)</c>。</summary>
    public bool TryGetEnvironmentVariable(string name, out string value)
    {
        if (Env is not null && Env.TryGetValue(name, out var found))
        {
            value = found;
            return true;
        }

        value = "";
        return false;
    }

    /// <summary>Go: <c>func (s *TestSys) Spawn(command []string, dir string, stderr io.Writer) (io.ReadWriteCloser, error)</c>。</summary>
    public Stream Spawn(string[] command, string dir, Stream stderr) =>
        ContentMapperTest.NewSpawner().Spawn(command, dir, stderr);

    /// <summary>Go: <c>func (s *TestSys) OnEmittedFiles(result *compiler.EmitResult, mTimesCache *collections.SyncMap[tspath.Path, time.Time])</c>。</summary>
    public void OnEmittedFiles(EmitResult? result, SyncMap<Path, DateTime>? mTimesCache)
    {
        if (result is not null)
        {
            foreach (var file in result.EmittedFiles)
            {
                var modTime = MapFs().GetModTime(file);
                if (_fsDiffer.SerializedDiff() is { } serializedDiff)
                {
                    if (serializedDiff.Snap.TryGetValue(file, out var diff) && diff.MTime == modTime)
                    {
                        // Even though written, timestamp was reverted
                        continue;
                    }
                }

                // Ensure that the timestamp for emitted files is in the order
                var now = Now();
                FsFromFileMap().Chtimes(file, default, now);

                // Update the mTime cache in --b mode to store the updated timestamp so tests will behave deteministically when finding newest output
                if (mTimesCache is not null)
                {
                    var path = ToPath(file, GetCurrentDirectory(), FS().UseCaseSensitiveFileNames());
                    if (mTimesCache.Load(path).Ok)
                    {
                        mTimesCache.Store(path, now);
                    }
                }
            }
        }
    }

    /// <summary>Go: <c>func (s *TestSys) OnListFilesStart(w io.Writer)</c>。</summary>
    public void OnListFilesStart(TextWriter w) => w.Write(TscTestMarkers.ListFileStart + "\n");

    /// <summary>Go: <c>func (s *TestSys) OnListFilesEnd(w io.Writer)</c>。</summary>
    public void OnListFilesEnd(TextWriter w) => w.Write(TscTestMarkers.ListFileEnd + "\n");

    /// <summary>Go: <c>func (s *TestSys) OnStatisticsStart(w io.Writer)</c>。</summary>
    public void OnStatisticsStart(TextWriter w) => w.Write(TscTestMarkers.StatisticsStart + "\n");

    /// <summary>Go: <c>func (s *TestSys) OnStatisticsEnd(w io.Writer)</c>。</summary>
    public void OnStatisticsEnd(TextWriter w) => w.Write(TscTestMarkers.StatisticsEnd + "\n");

    /// <summary>Go: <c>func (s *TestSys) OnBuildStatusReportStart(w io.Writer)</c>。</summary>
    public void OnBuildStatusReportStart(TextWriter w) => w.Write(TscTestMarkers.BuildStatusReportStart + "\n");

    /// <summary>Go: <c>func (s *TestSys) OnBuildStatusReportEnd(w io.Writer)</c>。</summary>
    public void OnBuildStatusReportEnd(TextWriter w) => w.Write(TscTestMarkers.BuildStatusReportEnd + "\n");

    /// <summary>Go: <c>func (s *TestSys) OnWatchStatusReportStart()</c>。</summary>
    public void OnWatchStatusReportStart() => Writer().Write(TscTestMarkers.WatchStatusReportStart + "\n");

    /// <summary>Go: <c>func (s *TestSys) OnWatchStatusReportEnd()</c>。</summary>
    public void OnWatchStatusReportEnd() => Writer().Write(TscTestMarkers.WatchStatusReportEnd + "\n");

    /// <summary>Go: <c>func (s *TestSys) GetTrace(w io.Writer, locale locale.Locale) func(msg *diagnostics.Message, args ...any)</c>。</summary>
    public TscTraceFunc GetTrace(TextWriter w, Loc locale)
    {
        return (msg, args) =>
        {
            w.Write(TscTestMarkers.TraceStart + "\n");
            try
            {
                // With tsc -b building projects in parallel we cannot serialize the package.json lookup trace
                // so trace as if it wasnt cached
                var str = msg.Localize(locale, args);
                _tracer.TraceWithWriter(w, str, ReferenceEquals(w, Writer()));
            }
            finally
            {
                w.Write(TscTestMarkers.TraceEnd + "\n");
            }
        };
    }

    /// <summary>Go: <c>func (s *TestSys) writeHeaderToBaseline(builder *strings.Builder, program *incremental.Program)</c>。</summary>
    private void WriteHeaderToBaseline(StringBuilder builder, IncProgram program)
    {
        if (builder.Length != 0)
        {
            builder.Append('\n');
        }

        if (program.Options().ConfigFilePath is { Length: > 0 } configFilePath)
        {
            builder.Append(GetRelativePathFromDirectory(Cwd, configFilePath, new ComparePathsOptions(
                FS().UseCaseSensitiveFileNames(),
                GetCurrentDirectory())));
            builder.Append("::\n");
        }
    }

    /// <summary>Go: <c>func (s *TestSys) WatchBackend() watchmanager.WatchBackend</c>。</summary>
    public WmWatchBackend WatchBackend() => _mockWatchBackend;

    /// <summary>Go: <c>func (s *TestSys) OnProgram(program *incremental.Program)</c>。</summary>
    public void OnProgram(IncProgram program)
    {
        WriteHeaderToBaseline(_programBaselines, program);

        var testingData = program.GetTestingData()!;
        _programBaselines.Append("SemanticDiagnostics::\n");
        foreach (var file in program.GetProgram().GetSourceFiles())
        {
            if (testingData.SemanticDiagnosticsPerFile.Load(file.Path()) is { Ok: true } loaded)
            {
                var oldDiagnostics = testingData.OldProgramSemanticDiagnosticsPerFile.Load(file.Path());
                if (!oldDiagnostics.Ok || !ReferenceEquals(oldDiagnostics.Value, loaded.Value))
                {
                    _programBaselines.Append("*refresh*    ");
                    _programBaselines.Append(file.FileName());
                    _programBaselines.Append('\n');
                }
            }
            else
            {
                _programBaselines.Append("*not cached* ");
                _programBaselines.Append(file.FileName());
                _programBaselines.Append('\n');
            }
        }

        // Write signature updates
        _programBaselines.Append("Signatures::\n");
        foreach (var file in program.GetProgram().GetSourceFiles())
        {
            if (testingData.UpdatedSignatureKinds.TryGetValue(file.Path(), out var kind))
            {
                switch (kind)
                {
                    case SignatureUpdateKind.ComputedDts:
                        _programBaselines.Append("(computed .d.ts) ");
                        _programBaselines.Append(file.FileName());
                        _programBaselines.Append('\n');
                        break;
                    case SignatureUpdateKind.StoredAtEmit:
                        _programBaselines.Append("(stored at emit) ");
                        _programBaselines.Append(file.FileName());
                        _programBaselines.Append('\n');
                        break;
                    case SignatureUpdateKind.UsedVersion:
                        _programBaselines.Append("(used version)   ");
                        _programBaselines.Append(file.FileName());
                        _programBaselines.Append('\n');
                        break;
                }
            }
        }

        var filesWithoutIncludeReason = new List<string>();
        var fileNotInProgramWithIncludeReason = new List<string>();
        var includeReasons = program.GetProgram().GetIncludeReasons();
        foreach (var file in program.GetProgram().GetSourceFiles())
        {
            if (!includeReasons.ContainsKey(file.Path()))
            {
                filesWithoutIncludeReason.Add(file.Path().Value);
            }
        }

        foreach (var path in includeReasons.Keys)
        {
            if (program.GetProgram().GetSourceFileByPath(path) is null && !program.GetProgram().IsMissingPath(path))
            {
                fileNotInProgramWithIncludeReason.Add(path.Value);
            }
        }

        if (filesWithoutIncludeReason.Count > 0 || fileNotInProgramWithIncludeReason.Count > 0)
        {
            WriteHeaderToBaseline(_programIncludeBaselines, program);
            _programIncludeBaselines.Append("!!! Expected all files to have include reasons\nfilesWithoutIncludeReason::\n");
            foreach (var file in filesWithoutIncludeReason)
            {
                _programIncludeBaselines.Append("  ");
                _programIncludeBaselines.Append(file);
                _programIncludeBaselines.Append('\n');
            }

            _programIncludeBaselines.Append("filesNotInProgramWithIncludeReason::\n");
            foreach (var file in fileNotInProgramWithIncludeReason)
            {
                _programIncludeBaselines.Append("  ");
                _programIncludeBaselines.Append(file);
                _programIncludeBaselines.Append('\n');
            }
        }
    }

    /// <summary>Go: <c>func (s *TestSys) baselinePrograms(baseline *strings.Builder, header string) string</c>。</summary>
    public string BaselinePrograms(StringBuilder baseline, string header)
    {
        baseline.Append(_programBaselines);
        _programBaselines.Clear();
        var result = "";
        if (_programIncludeBaselines.Length > 0)
        {
            result += $"\n\n{header}\n!!! Include reasons expectations don't match pls review!!!\n";
            result += _programIncludeBaselines;
            _programIncludeBaselines.Clear();
            baseline.Append(result);
        }

        return result;
    }

    /// <summary>Go: <c>func (s *TestSys) serializeState(baseline *strings.Builder)</c>。</summary>
    public void SerializeState(StringBuilder baseline)
    {
        BaselineOutput(baseline);
        BaselineFSWithDiff(baseline);
        // todo watch
        // this.serializeWatches(baseline);
        // this.timeoutCallbacks.serialize(baseline);
        // this.immediateCallbacks.serialize(baseline);
        // this.pendingInstalls.serialize(baseline);
        // this.service?.baseline();
    }

    /// <summary>Go: <c>func (s *TestSys) baselineOutput(baseline io.Writer)</c>。</summary>
    private void BaselineOutput(StringBuilder baseline)
    {
        baseline.Append("\nOutput::\n");
        var output = GetOutput(forComparing: false);
        baseline.Append(output);
    }

    /// <summary>Go: <c>func (s *TestSys) getOutput(forComparing bool) string</c>。</summary>
    public string GetOutput(bool forComparing)
    {
        var lines = _currentWrite.ToString().Split('\n');
        var transformer = new OutputSanitizer
        {
            ForComparing = forComparing,
            Lines = lines,
            OutputLines = new List<string>(lines.Length),
        };
        return transformer.TransformLines();
    }

    /// <summary>Go: <c>func (s *TestSys) clearOutput()</c>。</summary>
    public void ClearOutput()
    {
        var sb = _currentWrite.GetStringBuilder();
        sb.Clear();
        _tracer.Reset();
    }

    /// <summary>Go: <c>func (s *TestSys) baselineFSwithDiff(baseline io.Writer)</c>。</summary>
    public void BaselineFSWithDiff(StringBuilder baseline)
    {
        var writer = new LfStringWriter();
        _fsDiffer.BaselineFSWithDiff(writer);
        baseline.Append(writer.ToString());
    }

    /// <summary>Go: <c>func (s *TestSys) writeFileNoError(path string, content string)</c>。</summary>
    public void WriteFileNoError(string path, string content) => FsFromFileMap().WriteFile(path, content);

    /// <summary>Go: <c>func (s *TestSys) removeNoError(path string)</c>。</summary>
    public void RemoveNoError(string path) => FsFromFileMap().Remove(path);

    /// <summary>Go: <c>func (s *TestSys) readFileNoError(path string) string</c>。</summary>
    public string ReadFileNoError(string path)
    {
        if (!FsFromFileMap().TryReadFile(path, out var content))
        {
            Debug.Fail("File not found: " + path);
        }

        return content;
    }

    /// <summary>Go: <c>func (s *TestSys) renameFileNoError(oldPath string, newPath string)</c>。</summary>
    public void RenameFileNoError(string oldPath, string newPath)
    {
        WriteFileNoError(newPath, ReadFileNoError(oldPath));
        RemoveNoError(oldPath);
    }

    /// <summary>Go: <c>func (s *TestSys) replaceFileText(path string, oldText string, newText string)</c>。</summary>
    public void ReplaceFileText(string path, string oldText, string newText)
    {
        var content = ReadFileNoError(path);
        WriteFileNoError(path, ReplaceFirstOccurrence(content, oldText, newText));
    }

    /// <summary>Go: <c>func (s *TestSys) replaceFileTextAll(path string, oldText string, newText string)</c>。</summary>
    public void ReplaceFileTextAll(string path, string oldText, string newText)
    {
        var content = ReadFileNoError(path);
        WriteFileNoError(path, content.Replace(oldText, newText, StringComparison.Ordinal));
    }

    /// <summary>Go: <c>func (s *TestSys) appendFile(path string, text string)</c>。</summary>
    public void AppendFile(string path, string text) => WriteFileNoError(path, ReadFileNoError(path) + text);

    /// <summary>Go: <c>func (s *TestSys) prependFile(path string, text string)</c>。</summary>
    public void PrependFile(string path, string text) => WriteFileNoError(path, text + ReadFileNoError(path));

    /// <summary>Go: <c>strings.Replace(content, oldText, newText, 1)</c>。</summary>
    private static string ReplaceFirstOccurrence(string content, string oldText, string newText)
    {
        var index = content.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0)
        {
            return content;
        }

        return content[..index] + newText + content[(index + oldText.Length)..];
    }

    /// <summary>内部（非 Go 源）：字节级相同的 FS 与 diff —— 供 FSDiffer 的辅助实现复用。</summary>
    internal FSDiffer Differ => _fsDiffer;

    /// <summary>内部（非 Go 源）：Go 的 <c>sys.mockWatchBackend</c> 字段（runner.go 直接读它）。</summary>
    public MockWatchBackend MockWatchBackend => _mockWatchBackend;

    /// <summary>内部（非 Go 源）：Go 的 <c>forIncrementalCorrectness</c> 字段。</summary>
    internal bool ForIncrementalCorrectness => _forIncrementalCorrectness;
}

/// <summary>Go: tsctests/sys.go 的 <c>var (…)</c> 区块（markers）。</summary>
public static class TscTestMarkers
{
    public const string FakeTimeStamp = "HH:MM:SS AM";
    public const string FakeDuration = "d.ddds";

    public const string BuildStartingAt = "build starting at ";
    public const string BuildFinishedIn = "build finished in ";
    public const string ListFileStart = "!!! List files start";
    public const string ListFileEnd = "!!! List files end";
    public const string StatisticsStart = "!!! Statistics start";
    public const string StatisticsEnd = "!!! Statistics end";
    public const string BuildStatusReportStart = "!!! Build Status Report Start";
    public const string BuildStatusReportEnd = "!!! Build Status Report End";
    public const string WatchStatusReportStart = "!!! Watch Status Report Start";
    public const string WatchStatusReportEnd = "!!! Watch Status Report End";
    public const string TraceStart = "!!! Trace start";
    public const string TraceEnd = "!!! Trace end";
}

/// <summary>Go: <c>type outputSanitizer struct</c>（tsctests/sys.go:452-459）。</summary>
public sealed class OutputSanitizer
{
    public bool ForComparing;
    public string[] Lines = [];
    public int Index;
    public List<string> OutputLines = [];

    private static readonly string _englishVersion = Version_0.Localize(Loc.Default, Core.Version());
    private static readonly string _fakeEnglishVersion = Version_0.Localize(Loc.Default, HarnessUtil.FakeTSVersion);
    private static readonly Loc _czech = Loc.FromTag("cs");
    private static readonly string _czechVersion = Version_0.Localize(_czech, Core.Version());
    private static readonly string _fakeCzechVersion = Version_0.Localize(_czech, HarnessUtil.FakeTSVersion);

    /// <summary>Go: <c>func (o *outputSanitizer) addOutputLine(s string)</c>。</summary>
    private void AddOutputLine(string s)
    {
        s = s.Replace($"'{Core.Version()}'", $"'{HarnessUtil.FakeTSVersion}'", StringComparison.Ordinal);
        s = s.Replace(_englishVersion, _fakeEnglishVersion, StringComparison.Ordinal);
        s = s.Replace(_czechVersion, _fakeCzechVersion, StringComparison.Ordinal);
        s = FSDiffer.SanitizeInternalSymbolName(s);
        OutputLines.Add(s);
    }

    /// <summary>Go: <c>func (o *outputSanitizer) sanitizeBuildStatusTimeStamp() string</c>。</summary>
    private string SanitizeBuildStatusTimeStamp()
    {
        var statusLine = Lines[Index];
        var hhSeparator = statusLine.IndexOf(':');
        if (hhSeparator < 2)
        {
            Debug.Fail("Expected timestamp");
        }

        return statusLine[..(hhSeparator - 2)] + TscTestMarkers.FakeTimeStamp +
            statusLine[(hhSeparator + TscTestMarkers.FakeTimeStamp.Length - 2)..];
    }

    /// <summary>Go: <c>func (o *outputSanitizer) transformLines() string</c>。</summary>
    public string TransformLines()
    {
        for (; Index < Lines.Length; Index++)
        {
            var line = Lines[Index];
            if (line.StartsWith(TscTestMarkers.BuildStartingAt, StringComparison.Ordinal))
            {
                if (!ForComparing)
                {
                    AddOutputLine(TscTestMarkers.BuildStartingAt + TscTestMarkers.FakeTimeStamp);
                }

                continue;
            }

            if (line.StartsWith(TscTestMarkers.BuildFinishedIn, StringComparison.Ordinal))
            {
                if (!ForComparing)
                {
                    AddOutputLine(TscTestMarkers.BuildFinishedIn + TscTestMarkers.FakeDuration);
                }

                continue;
            }

            if (!AddOrSkipLinesForComparing(TscTestMarkers.ListFileStart, TscTestMarkers.ListFileEnd, false, null) &&
                !AddOrSkipLinesForComparing(TscTestMarkers.StatisticsStart, TscTestMarkers.StatisticsEnd, true, null) &&
                !AddOrSkipLinesForComparing(TscTestMarkers.TraceStart, TscTestMarkers.TraceEnd, false, null) &&
                !AddOrSkipLinesForComparing(TscTestMarkers.BuildStatusReportStart, TscTestMarkers.BuildStatusReportEnd, false, SanitizeBuildStatusTimeStamp) &&
                !AddOrSkipLinesForComparing(TscTestMarkers.WatchStatusReportStart, TscTestMarkers.WatchStatusReportEnd, false, SanitizeBuildStatusTimeStamp))
            {
                AddOutputLine(line);
            }
        }

        return string.Join("\n", OutputLines);
    }

    /// <summary>Go: <c>func (o *outputSanitizer) addOrSkipLinesForComparing(...)</c>。</summary>
    private bool AddOrSkipLinesForComparing(
        string lineStart,
        string lineEnd,
        bool skipEvenIfNotComparing,
        Func<string>? sanitizeFirstLine)
    {
        if (Lines[Index] != lineStart)
        {
            return false;
        }

        Index++;
        var isFirstLine = true;
        for (; Index < Lines.Length; Index++)
        {
            if (Lines[Index] == lineEnd)
            {
                return true;
            }

            if (!ForComparing && !skipEvenIfNotComparing)
            {
                var line = Lines[Index];
                if (isFirstLine && sanitizeFirstLine is not null)
                {
                    line = sanitizeFirstLine();
                    isFirstLine = false;
                }

                AddOutputLine(line);
            }
        }

        // Go: panic("Expected lineEnd" + lineEnd + " not found after " + lineStart)
        Debug.Fail("Expected lineEnd" + lineEnd + " not found after " + lineStart);
        return true;
    }
}
