// ported from: internal/execute/incremental/program.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `type SignatureUpdateKind byte` + iota → `enum SignatureUpdateKind : byte`（成员名去 Go 的前缀）。
// 2) Go 的 `Program` 是增量包里的**另一个** Program 类型（包装 `*compiler.Program`）→ C# 顶层类
//    `TypeScript.Execute.Incremental.Program`；内部引用编译器的 Program 一律用别名 `CompilerProgram`。
// 3) `func (p *Program) Program() *compiler.Program`：C# 里成员名不能与类同名（CS0542）→ 落成 `ProgramSelf()`
//    （与 Compiler/Program.Files.cs 的 `ProgramSelf()` 同一先例）；`Compiler.ProgramLike` 接口本来就不含它
//    （接口用 `ContentMapperDiagnostics()` 表达同一用途，见 Program.Emit.cs 的文件头第 4 条）。
// 4) `panic(...)` → `Debug.Fail(...)`（抛 DebugFailureException）；`ctx.Err() != nil` →
//    `ctx.CancellationToken.IsCancellationRequested`。
// 5) `time.Duration` → `TimeSpan`；`atomic.Bool`/`Mutex` → `volatile`/`lock`；`sync.Once` → 锁 + 标志位。
// 6) `json.Marshal(buildInfo)` → `Json.Marshal(buildInfo)` 后按 UTF-8 解成字符串（文档 §json.md：无尾部换行）。
// 7) `(T, error)` 的写盘错误 → 异常：`try { ... } catch (Exception err) { ... err.Message ... }`。
// 8) `Outputpaths.GetBuildInfoFileName` → `TypeScript.OutputPaths.OutputPaths.GetBuildInfoFileName`。
namespace TypeScript.Execute.Incremental;

using System.Text;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Json;
using TypeScript.TsOptions;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Execute.Incremental.Incremental;
using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;
using CompilerProgram = TypeScript.Compiler.Program;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>type SignatureUpdateKind byte</c>（program.go:23-29）。</summary>
public enum SignatureUpdateKind : byte
{
    /// <summary>Go: <c>SignatureUpdateKindComputedDts</c>。</summary>
    ComputedDts = 0,

    /// <summary>Go: <c>SignatureUpdateKindStoredAtEmit</c>。</summary>
    StoredAtEmit = 1,

    /// <summary>Go: <c>SignatureUpdateKindUsedVersion</c>。</summary>
    UsedVersion = 2,
}

/// <summary>Go: <c>type Program struct</c>（program.go:31-44）。增量程序对 <c>*compiler.Program</c> 的包装。</summary>
public sealed class Program : TypeScript.Compiler.ProgramLike
{
    internal Snapshot _snapshot = null!;
    internal CompilerProgram? _program;
    internal Host _host = null!;

    // Testing data
    internal TestingData? _testingData;

    private readonly object _nestedEmitMu = new();
    private Func<DateTime>? _nestedEmitNow;
    private int _nestedEmitDepth;
    private DateTime _nestedEmitStart;
    private TimeSpan _nestedEmitTime;

    /// <summary>Go: <c>func NewProgram(program *compiler.Program, oldProgram *Program, host Host, nestedEmitNow func() time.Time, testing bool) *Program</c>。</summary>
    public static Program NewProgram(CompilerProgram program, Program? oldProgram, Host host, Func<DateTime>? nestedEmitNow, bool testing)
    {
        var incrementalProgram = new Program
        {
            _snapshot = ProgramToSnapshot(program, oldProgram, testing),
            _program = program,
            _host = host,
            _nestedEmitNow = nestedEmitNow,
        };

        if (testing)
        {
            incrementalProgram._testingData = new TestingData
            {
                SemanticDiagnosticsPerFile = incrementalProgram._snapshot._semanticDiagnosticsPerFile,
                OldProgramSemanticDiagnosticsPerFile = oldProgram is not null
                    ? oldProgram._snapshot._semanticDiagnosticsPerFile
                    : new TypeScript.Collections.SyncMap<Path, DiagnosticsOrBuildInfoDiagnosticsWithFileName>(),
                UpdatedSignatureKinds = new Dictionary<Path, SignatureUpdateKind>(),
            };
        }
        return incrementalProgram;
    }

    /// <summary>Go: <c>type TestingData struct</c>（program.go:69-73）。</summary>
    public sealed class TestingData
    {
        public TypeScript.Collections.SyncMap<Path, DiagnosticsOrBuildInfoDiagnosticsWithFileName> SemanticDiagnosticsPerFile = new();
        public TypeScript.Collections.SyncMap<Path, DiagnosticsOrBuildInfoDiagnosticsWithFileName> OldProgramSemanticDiagnosticsPerFile = new();
        public Dictionary<Path, SignatureUpdateKind> UpdatedSignatureKinds = new();
    }

    public TestingData? GetTestingData() => _testingData;

    internal Action BeginNestedEmit()
    {
        Func<DateTime>? now;
        lock (_nestedEmitMu)
        {
            now = _nestedEmitNow;
            if (now is null)
            {
                return static () => { };
            }
            if (_nestedEmitDepth == 0)
            {
                _nestedEmitStart = now();
            }
            _nestedEmitDepth++;
        }

        return () =>
        {
            lock (_nestedEmitMu)
            {
                _nestedEmitDepth--;
                if (_nestedEmitDepth == 0)
                {
                    _nestedEmitTime += now() - _nestedEmitStart;
                }
            }
        };
    }

    public TimeSpan TakeNestedEmitTime()
    {
        lock (_nestedEmitMu)
        {
            var nestedEmitTime = _nestedEmitTime;
            _nestedEmitTime = TimeSpan.Zero;
            return nestedEmitTime;
        }
    }

    private void PanicIfNoProgram(string method)
    {
        if (_program is null)
        {
            Debug.Fail(method + ": should not be called without program");
        }
    }

    public CompilerProgram GetProgram()
    {
        PanicIfNoProgram("GetProgram");
        return _program!;
    }

    public bool HasChangedDtsFile() => _snapshot._hasChangedDtsFile;

    // Options implements compiler.AnyProgram interface.
    public CompilerOptions Options() => _snapshot._options;

    // CommonSourceDirectory implements compiler.AnyProgram interface.
    public string CommonSourceDirectory()
    {
        PanicIfNoProgram("CommonSourceDirectory");
        return _program!.CommonSourceDirectory();
    }

    /// <summary>Go: <c>func (p *Program) Program() *compiler.Program</c>（CS0542 → <c>ProgramSelf</c>）。</summary>
    public CompilerProgram ProgramSelf()
    {
        PanicIfNoProgram("Program");
        return _program!;
    }

    // IsSourceFileDefaultLibrary implements compiler.AnyProgram interface.
    public bool IsSourceFileDefaultLibrary(Path path)
    {
        PanicIfNoProgram("IsSourceFileDefaultLibrary");
        return _program!.IsSourceFileDefaultLibrary(path);
    }

    // GetSourceFiles implements compiler.AnyProgram interface.
    public SourceFile[] GetSourceFiles()
    {
        PanicIfNoProgram("GetSourceFiles");
        return _program!.GetSourceFiles();
    }

    // GetSourceFile implements compiler.AnyProgram interface.
    public SourceFile? GetSourceFile(string path)
    {
        PanicIfNoProgram("GetSourceFile");
        return _program!.GetSourceFile(path);
    }

    // GetConfigFileParsingDiagnostics implements compiler.AnyProgram interface.
    public Diagnostic[] GetConfigFileParsingDiagnostics()
    {
        PanicIfNoProgram("GetConfigFileParsingDiagnostics");
        return _program!.GetConfigFileParsingDiagnostics();
    }

    // GetSyntacticDiagnostics implements compiler.AnyProgram interface.
    public Diagnostic[] GetSyntacticDiagnostics(Context ctx, SourceFile? file)
    {
        PanicIfNoProgram("GetSyntacticDiagnostics");
        return _program!.GetSyntacticDiagnostics(ctx, file);
    }

    // GetBindDiagnostics implements compiler.AnyProgram interface.
    public Diagnostic[] GetBindDiagnostics(Context ctx, SourceFile? file)
    {
        PanicIfNoProgram("GetBindDiagnostics");
        return _program!.GetBindDiagnostics(ctx, file);
    }

    public Diagnostic[] GetProgramDiagnostics()
    {
        PanicIfNoProgram("GetProgramDiagnostics");
        return _program!.GetProgramDiagnostics();
    }

    public Diagnostic[] GetGlobalDiagnostics(Context ctx)
    {
        PanicIfNoProgram("GetGlobalDiagnostics");
        return _program!.GetGlobalDiagnostics(ctx);
    }

    // GetSemanticDiagnostics implements compiler.AnyProgram interface.
    public Diagnostic[] GetSemanticDiagnostics(Context ctx, SourceFile? file)
    {
        PanicIfNoProgram("GetSemanticDiagnostics");
        if (_snapshot._options.NoCheck.IsTrue())
        {
            return [];
        }

        // Ensure all the diagnsotics are cached
        CollectSemanticDiagnosticsOfAffectedFiles(ctx, file);
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            return [];
        }

        // Return result from cache
        if (file is not null)
        {
            return GetSemanticDiagnosticsOfFile(file);
        }

        var diagnostics = new List<Diagnostic>();
        foreach (var sourceFile in _program!.GetSourceFiles())
        {
            diagnostics.AddRange(GetSemanticDiagnosticsOfFile(sourceFile));
        }
        return [.. diagnostics];
    }

    private Diagnostic[] GetSemanticDiagnosticsOfFile(SourceFile file)
    {
        var (cachedDiagnostics, ok) = _snapshot._semanticDiagnosticsPerFile.Load(file.Path());
        if (!ok)
        {
            Debug.Fail("After handling all the affected files, there shouldnt be more changes");
        }
        return
        [
            .. TypeScript.Compiler.Compiler.FilterNoEmitSemanticDiagnostics(cachedDiagnostics!.GetDiagnostics(_program!, file), _snapshot._options),
            .. _program!.GetIncludeProcessorDiagnostics(file),
        ];
    }

    // GetDeclarationDiagnostics implements compiler.AnyProgram interface.
    public Diagnostic[] GetDeclarationDiagnostics(Context ctx, SourceFile? file)
    {
        PanicIfNoProgram("GetDeclarationDiagnostics");
        var result = EmitFiles(ctx, this, new EmitOptions
        {
            // Go: `TargetSourceFiles: core.SingleElementSlice(file)`（program.go:228）—— `file == nil` 时 Go 的
            // `SingleElementSlice` 返回 **nil 切片**（不是空切片），而 `emitFiles`/`emitAllAffectedFiles` 正是用
            // `options.TargetSourceFiles != nil` 区分"整个程序的声明诊断"与"单个文件"。若这里退化成空数组，
            // `isForDtsErrors` 分支会走 `FlatMap(空) => 空诊断`，`--declaration` 下的 TS4094 等声明诊断全部丢失。
            TargetSourceFiles = file is null ? null : [file],
        }, true);
        if (result is not null)
        {
            return result.Diagnostics;
        }
        return [];
    }

    // GetSuggestionDiagnostics implements compiler.AnyProgram interface.
    public Diagnostic[] GetSuggestionDiagnostics(Context ctx, SourceFile? file)
    {
        PanicIfNoProgram("GetSuggestionDiagnostics");
        // TODO: incremental suggestion diagnostics (only relevant in editor incremental builder?)
        return _program!.GetSuggestionDiagnostics(ctx, file);
    }

    /// <summary>Go: <c>Program() *Program</c> 的 C# 替代（见接口注释）。</summary>
    public Diagnostic[] ContentMapperDiagnostics() => _program!.ContentMapperDiagnostics();

    public EmitResult? Emit(Context ctx, EmitOptions options)
    {
        PanicIfNoProgram("Emit");

        EmitResult? result = null;
        if (!options.ForceEmit && options.EmitOnly != EmitOnly.EmitOnlyBuilderSignature)
        {
            Func<EmitResult?>? emitBuildInfo = null;
            if (Options().NoEmit.IsTrue())
            {
                emitBuildInfo = () => EmitBuildInfo(ctx, options);
            }
            result = TypeScript.Compiler.Compiler.HandleNoEmitOptions(ctx, this, options.TargetSourceFiles, emitBuildInfo);
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
        if (result is not null)
        {
            if (options.TargetSourceFiles is not null || Options().NoEmit.IsTrue())
            {
                return result;
            }

            // Emit buildInfo and combine result
            var buildInfoResult = EmitBuildInfo(ctx, options);
            if (buildInfoResult is not null)
            {
                result.Diagnostics = [.. result.Diagnostics, .. buildInfoResult.Diagnostics];
                result.EmittedFiles = [.. result.EmittedFiles, .. buildInfoResult.EmittedFiles];
            }
            return result;
        }
        return EmitFiles(ctx, this, options, false);
    }

    // Handle affected files and cache the semantic diagnostics for all of them or the file asked for
    private void CollectSemanticDiagnosticsOfAffectedFiles(Context ctx, SourceFile? file)
    {
        if (_snapshot.CanUseIncrementalState())
        {
            // Get all affected files
            Incremental.CollectAllAffectedFiles(ctx, this);
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (_snapshot._semanticDiagnosticsPerFile.Size() == _program!.GetSourceFiles().Length)
            {
                // If we have all the files,
                return;
            }
        }

        List<SourceFile> affectedFiles = [];
        if (file is not null)
        {
            var (_, ok) = _snapshot._semanticDiagnosticsPerFile.Load(file.Path());
            if (ok)
            {
                return;
            }
            affectedFiles.Add(file);
        }
        else
        {
            foreach (var sourceFile in _program!.GetSourceFiles())
            {
                var (_, ok) = _snapshot._semanticDiagnosticsPerFile.Load(sourceFile.Path());
                if (!ok)
                {
                    affectedFiles.Add(sourceFile);
                }
            }
        }

        // Get their diagnostics and cache them
        var diagnosticsPerFile = _program!.GetSemanticDiagnosticsForIncremental(ctx, [.. affectedFiles]);
        // commit changes if no err
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            return;
        }

        // Commit changes to snapshot
        foreach (var (sourceFile, diagnostics) in diagnosticsPerFile)
        {
            _snapshot._semanticDiagnosticsPerFile.Store(sourceFile.Path(), new DiagnosticsOrBuildInfoDiagnosticsWithFileName { _diagnostics = diagnostics });
        }
        if (_snapshot._semanticDiagnosticsPerFile.Size() == _program.GetSourceFiles().Length && _snapshot._checkPending && !_snapshot._options.NoCheck.IsTrue())
        {
            _snapshot._checkPending = false;
        }
        _snapshot._buildInfoEmitPending = true;
    }

    internal EmitResult? EmitBuildInfo(Context ctx, EmitOptions options)
    {
        Action doneTrace = static () => { };
        if (_program!.Tracing() is { } tr)
        {
            doneTrace = tr.Push(TypeScript.Tracing.Phase.Emit, "emitBuildInfo", null, true);
        }
        try
        {
            var buildInfoFileName = TypeScript.OutputPaths.OutputPaths.GetBuildInfoFileName(_snapshot._options, new TypeScript.TSPath.ComparePathsOptions(
                _program.UseCaseSensitiveFileNames(),
                _program.GetCurrentDirectory()));
            if (buildInfoFileName == "" || _program.IsEmitBlocked(buildInfoFileName))
            {
                return null;
            }
            if (_snapshot._hasErrors == Tristate.TSUnknown)
            {
                EnsureHasErrorsForState(ctx, _program);
                if (_snapshot._hasErrors != _snapshot._hasErrorsFromOldState || _snapshot._hasSemanticErrors != _snapshot._hasSemanticErrorsFromOldState)
                {
                    _snapshot._buildInfoEmitPending = true;
                }
            }
            if (_snapshot._packageJsons is null)
            {
                EnsurePackageJsonsForState();
                if (!SequenceEqual(_snapshot._packageJsons, _snapshot._packageJsonsFromOldState) ||
                    !SequenceEqual(_snapshot._missingPackageJsons, _snapshot._missingPackageJsonsFromOldState))
                {
                    _snapshot._buildInfoEmitPending = true;
                }
            }
            if (!_snapshot._buildInfoEmitPending)
            {
                return null;
            }
            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
            BuildInfo buildInfo;
            try
            {
                buildInfo = SnapshotToBuildInfo(_snapshot, _program, buildInfoFileName);
            }
            catch (Exception err)
            {
                return new EmitResult
                {
                    EmitSkipped = true,
                    Diagnostics = [TypeScript.Compiler.Compiler.ContentMapperProjectDiagnostic(err)],
                };
            }
            var text = Encoding.UTF8.GetString(Marshal(buildInfo));
            try
            {
                if (options.WriteFile is not null)
                {
                    options.WriteFile(buildInfoFileName, text, new WriteFileData { BuildInfo = buildInfo });
                }
                else
                {
                    _program.Host().FS().WriteFile(buildInfoFileName, text);
                }
            }
            catch (Exception err)
            {
                return new EmitResult
                {
                    EmitSkipped = true,
                    Diagnostics =
                    [
                        NewCompilerDiagnostic(Diagnostics.Could_not_write_file_0_Colon_1, buildInfoFileName, err.Message),
                    ],
                };
            }
            _snapshot._buildInfoEmitPending = false;
            return new EmitResult
            {
                EmitSkipped = false,
                EmittedFiles = [buildInfoFileName],
            };
        }
        finally
        {
            doneTrace();
        }
    }

    private void EnsureHasErrorsForState(Context ctx, CompilerProgram program)
    {
        Func<bool>? hasIncludeProcessingDiagnostics = null;
        var hasEmitDiagnostics = false;
        if (_snapshot.CanUseIncrementalState())
        {
            foreach (var file in program.GetSourceFiles())
            {
                if (_snapshot._emitDiagnosticsPerFile.Load(file.Path()).Ok)
                {
                    // emit diagnostics will be encoded in buildInfo;
                    hasEmitDiagnostics = true;
                    break;
                }
                if (hasIncludeProcessingDiagnostics is null && _program!.GetIncludeProcessorDiagnostics(file).Length > 0)
                {
                    hasIncludeProcessingDiagnostics = static () => true;
                }
            }
            hasIncludeProcessingDiagnostics ??= static () => false;
        }
        else
        {
            hasEmitDiagnostics = _snapshot._hasEmitDiagnostics;
            hasIncludeProcessingDiagnostics = () => Some(program.GetSourceFiles(), file => _program!.GetIncludeProcessorDiagnostics(file).Length > 0);
        }

        if (hasEmitDiagnostics)
        {
            // Record this for only non incremental build info
            _snapshot._hasErrors = IfElse(_snapshot._options.IsIncremental(), Tristate.TSFalse, Tristate.TSTrue);
            // Dont need to encode semantic errors state since the emit diagnostics are encoded
            _snapshot._hasSemanticErrors = false;
            return;
        }

        if (hasIncludeProcessingDiagnostics() ||
            program.GetConfigFileParsingDiagnostics().Length > 0 ||
            program.GetSyntacticDiagnostics(ctx, null).Length > 0 ||
            program.GetProgramDiagnostics().Length > 0 ||
            program.GetGlobalDiagnostics(ctx).Length > 0)
        {
            _snapshot._hasErrors = Tristate.TSTrue;
            // Dont need to encode semantic errors state since the syntax and program diagnostics are encoded as present
            _snapshot._hasSemanticErrors = false;
            return;
        }

        _snapshot._hasErrors = Tristate.TSFalse;
        // Check semantic and emit diagnostics first as we dont need to ask program about it
        if (Some(_program!.GetSourceFiles(), file =>
        {
            var (semanticDiagnostics, ok) = _snapshot._semanticDiagnosticsPerFile.Load(file.Path());
            if (!ok)
            {
                // Missing semantic diagnostics in cache will be encoded in incremental buildInfo
                return _snapshot._options.IsIncremental();
            }
            if (semanticDiagnostics!._diagnostics is { Length: > 0 } || semanticDiagnostics._buildInfoDiagnostics is { Length: > 0 })
            {
                // cached semantic diagnostics will be encoded in buildInfo
                return true;
            }
            return false;
        }))
        {
            // Because semantic diagnostics are recorded in buildInfo, we dont need to encode hasErrors in incremental buildInfo
            // But encode as errors in non incremental buildInfo
            _snapshot._hasSemanticErrors = !_snapshot._options.IsIncremental();
        }
    }

    private void EnsurePackageJsonsForState()
    {
        var config = GetDirectoryPath(_program!.CommandLine().ConfigName());
        if (config != "")
        {
            _program.PackageJsonCacheEntries((key, value) =>
            {
                if (value is null)
                {
                    return true;
                }
                var packageJson = CombinePaths(value.PackageDirectory, "package.json");
                if (value.Exists() || value.DirectoryExists)
                {
                    packageJson = _program.Host().FS().Realpath(packageJson);
                }
                if (value.Exists())
                {
                    var packageJsons = _snapshot._packageJsons ?? [];
                    _snapshot._packageJsons = [.. packageJsons, packageJson];
                }
                else if (packageJson.Contains("/node_modules/", StringComparison.Ordinal))
                {
                    var missingPackageJsons = _snapshot._missingPackageJsons ?? [];
                    _snapshot._missingPackageJsons = [.. missingPackageJsons, packageJson];
                }
                return true;
            });
        }
        _snapshot._packageJsons = NormalizePackageJsons(_snapshot._packageJsons);
        _snapshot._missingPackageJsons = NormalizePackageJsons(_snapshot._missingPackageJsons);
    }

    internal string[] PackageJsonLookupPaths()
    {
        var config = GetDirectoryPath(_program!.CommandLine().ConfigName());
        if (config == "")
        {
            return [];
        }

        var packageJsons = new List<string>();
        _program.PackageJsonCacheEntries((key, value) =>
        {
            if (value is null)
            {
                return true;
            }
            var packageJson = CombinePaths(value.PackageDirectory, "package.json");
            if (value.Exists() || value.DirectoryExists)
            {
                packageJson = _program.Host().FS().Realpath(packageJson);
            }
            packageJsons.Add(packageJson);
            return true;
        });
        var result = packageJsons.ToArray();
        Array.Sort(result, StringComparer.Ordinal);
        return Deduplicate(result);
    }
}

public static partial class Incremental
{
    /// <summary>Go: <c>func normalizePackageJsons(packageJsons []string) []string</c>（program.go:477-483）。</summary>
    internal static string[] NormalizePackageJsons(string[]? packageJsons)
    {
        if (packageJsons is null)
        {
            return [];
        }
        Array.Sort(packageJsons, StringComparer.Ordinal);
        return Deduplicate(packageJsons);
    }

    /// <summary>Go 的 <c>slices.Equal(a, b)</c>（nil 与非 nil 空切片相等）。</summary>
    internal static bool SequenceEqual(string[]? a, string[]? b)
    {
        var left = a ?? [];
        var right = b ?? [];
        if (left.Length != right.Length)
        {
            return false;
        }
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }
        return true;
    }
}
