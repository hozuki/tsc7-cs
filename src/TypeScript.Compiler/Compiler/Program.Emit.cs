// ported from: internal/compiler/program.go:1837-2059（WriteFileData / WriteFile / EmitOptions / EmitResult /
// SourceMapEmitResult / Emit / CombineEmitResults / ProgramLike / HandleNoEmitOptions / GetDiagnosticsOfAnyProgram）
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `Emit` 返回 `*EmitResult`（可为 nil）→ C# `EmitResult?`（调用点本来就要判 nil）。
// 2) Go 的 `sync.Pool`（writer 复用）→ `ConcurrentBag<EmitTextWriter>`（Take/Add，语义相同：池空就新建）。
// 3) Go 的 `writeFile func(...) error` → C# `WriteFile` 委托**无返回值**（错误走异常，§5.4）；`WriteFileData` 由本文件定义。
// 4) `ProgramLike.Program() *Program`（Go 用来读 `contentMapperDiagnostics`）在 C# 里无法实现（CS0542：成员名不能与类同名）
//    → 接口成员改成 `ContentMapperDiagnostics()`（语义相同，就是那一个字段）。
// 5) `ctx.Err()` → `ctx.CancellationToken.IsCancellationRequested`（Core.Context 的对应物）。
// 6) `printer.NewTextWriter` → `TypeScript.Printer.Printer.NewTextWriter`（命名空间与类同名，必须全限定）。
// 7) `EmitOnly` 枚举由 T12EmitterHost 的 `Emitter.cs` 声明（`EmitAll`/`EmitOnlyJs`/`EmitOnlyDts`/`EmitOnlyBuilderSignature`）。
namespace TypeScript.Compiler;

using System.Collections.Concurrent;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Printer;
using TypeScript.Tracing;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;

/// <summary>Go: <c>type WriteFileData struct</c>（program.go:1837）。</summary>
public sealed class WriteFileData
{
    /// <summary>Go: <c>SourceMapUrlPos int</c>。</summary>
    public int SourceMapUrlPos;

    /// <summary>Go: <c>BuildInfo any</c>。</summary>
    public object? BuildInfo;

    /// <summary>Go: <c>Diagnostics []*ast.Diagnostic</c>。</summary>
    public Diagnostic[] Diagnostics = [];

    /// <summary>Go: <c>SkippedDtsWrite bool</c>。</summary>
    public bool SkippedDtsWrite;

    /// <summary>Go: <c>SourceFile *ast.SourceFile</c>。</summary>
    public SourceFile? SourceFile;
}

/// <summary>Go: <c>type WriteFile func(fileName string, text string, data *WriteFileData) error</c>（program.go:1845）——`error` → 异常。</summary>
public delegate void WriteFile(string fileName, string text, WriteFileData data);

/// <summary>Go: <c>type EmitOptions struct</c>（program.go:1847）。</summary>
public sealed class EmitOptions
{
    /// <summary>Go: <c>TargetSourceFiles []*ast.SourceFile</c>（nil 表示发射全部）。</summary>
    public SourceFile[]? TargetSourceFiles;

    /// <summary>Go: <c>EmitOnly EmitOnly</c>。</summary>
    public EmitOnly EmitOnly;

    /// <summary>Go: <c>ForceEmit bool</c>。</summary>
    public bool ForceEmit;

    /// <summary>Go: <c>WriteFile WriteFile</c>。</summary>
    public WriteFile? WriteFile;
}

/// <summary>Go: <c>type EmitResult struct</c>（program.go:1854）。</summary>
public sealed class EmitResult
{
    /// <summary>Go: <c>EmitSkipped bool</c>。</summary>
    public bool EmitSkipped;

    /// <summary>Go: <c>Diagnostics []*ast.Diagnostic</c>（含声明发射诊断）。</summary>
    public Diagnostic[] Diagnostics = [];

    /// <summary>Go: <c>EmittedFiles []string</c>（编译器写盘的文件）。</summary>
    public string[] EmittedFiles = [];

    /// <summary>Go: <c>SourceMaps []*SourceMapEmitResult</c>。</summary>
    public SourceMapEmitResult[] SourceMaps = [];
}

/// <summary>Go: <c>type SourceMapEmitResult struct</c>（program.go:1861）。</summary>
public sealed class SourceMapEmitResult
{
    /// <summary>Go: <c>InputSourceFileNames []string</c>（与 sourceMap.sources 一一对应）。</summary>
    public string[] InputSourceFileNames = [];

    /// <summary>Go: <c>SourceMap *sourcemap.RawSourceMap</c>。</summary>
    public TypeScript.SourceMap.RawSourceMap? SourceMap;

    /// <summary>Go: <c>GeneratedFile string</c>。</summary>
    public string GeneratedFile = "";
}

public sealed partial class Program
{
    /// <summary>Go: <c>func (p *Program) Emit(ctx context.Context, options EmitOptions) *EmitResult</c>（program.go:1870）。</summary>
    public EmitResult? Emit(Context ctx, EmitOptions options)
    {
        Action doneTrace = Noop;
        if (_opts.Tracing is not null)
        {
            doneTrace = _opts.Tracing.Push(Phase.Emit, "emit", null, true);
        }
        try
        {
            if (!options.ForceEmit && options.EmitOnly != EmitOnly.EmitOnlyBuilderSignature)
            {
                var result = Compiler.HandleNoEmitOptions(ctx, this, options.TargetSourceFiles, null);
                if (result is not null || ctx.CancellationToken.IsCancellationRequested)
                {
                    return result;
                }
            }

            var newLine = Options().NewLine.GetNewLineCharacter();
            var writerPool = new ConcurrentBag<EmitTextWriter>();
            var wg = NewWorkGroup(SingleThreaded());
            var emitters = new List<Emitter>();
            var forceDtsEmit = options.EmitOnly == EmitOnly.EmitOnlyBuilderSignature || options.ForceEmit && options.EmitOnly == EmitOnly.EmitOnlyDts;
            var forceJsEmit = options.ForceEmit && options.EmitOnly == EmitOnly.EmitOnlyJs;
            var sourceFiles = GetSourceFilesToEmit(options.TargetSourceFiles, forceDtsEmit, forceJsEmit);

            foreach (var sourceFile in sourceFiles)
            {
                var emitter = new Emitter
                {
                    _sourceFile = sourceFile,
                    _emitOnly = options.EmitOnly,
                    _forceEmit = options.ForceEmit,
                    _writeFile = options.WriteFile,
                    _tr = _opts.Tracing,
                };
                emitters.Add(emitter);
                var currentEmitter = emitter;
                var currentSourceFile = sourceFile;
                wg.Queue(() =>
                {
                    var (host, done) = EmitHostImpl.NewEmitHost(ctx, this, currentSourceFile);
                    try
                    {
                        currentEmitter._host = host;

                        // 取一个空闲的 writer
                        if (!writerPool.TryTake(out var writer))
                        {
                            writer = TypeScript.Printer.Printer.NewTextWriter(newLine, 0);
                        }
                        writer.Clear();

                        // 挂上 writer 并执行发射
                        currentEmitter._writer = writer;
                        currentEmitter._paths = TypeScript.OutputPaths.OutputPaths.GetOutputPathsFor(currentSourceFile, host.Options(), host, new TypeScript.OutputPaths.ForceEmitPaths
                        {
                            Dts = forceDtsEmit,
                            Js = forceJsEmit,
                            DeclarationMap = options.ForceEmit && options.EmitOnly == EmitOnly.EmitOnlyDts,
                        });
                        currentEmitter.Emit();
                        currentEmitter._writer = null!;

                        // 把 writer 放回池里
                        writerPool.Add(writer);
                    }
                    finally
                    {
                        done();
                    }
                });
            }

            // 等 emit 完成
            wg.RunAndWait();

            // 按输入顺序收集结果
            return Compiler.CombineEmitResults([.. emitters.Select(static e => e._emitResult)]);
        }
        finally
        {
            doneTrace();
        }
    }
}

/// <summary>
/// Go: <c>type ProgramLike interface</c>（program.go:1957）。
/// `Program() *Program` 在 C# 里不可实现（成员名与类同名 → CS0542）→ 用 `ContentMapperDiagnostics()` 表达同一用途。
/// </summary>
public interface ProgramLike
{
    CompilerOptions Options();

    SourceFile? GetSourceFile(string path);

    SourceFile[] GetSourceFiles();

    Diagnostic[] GetConfigFileParsingDiagnostics();

    Diagnostic[] GetSyntacticDiagnostics(Context ctx, SourceFile? file);

    Diagnostic[] GetBindDiagnostics(Context ctx, SourceFile? file);

    Diagnostic[] GetProgramDiagnostics();

    Diagnostic[] GetGlobalDiagnostics(Context ctx);

    Diagnostic[] GetSemanticDiagnostics(Context ctx, SourceFile? file);

    Diagnostic[] GetDeclarationDiagnostics(Context ctx, SourceFile? file);

    Diagnostic[] GetSuggestionDiagnostics(Context ctx, SourceFile? file);

    EmitResult? Emit(Context ctx, EmitOptions options);

    string CommonSourceDirectory();

    bool IsSourceFileDefaultLibrary(TSPath.Path path);

    /// <summary>Go 的 <c>Program() *Program</c> 的替代：返回底层程序的 <c>contentMapperDiagnostics</c>。</summary>
    Diagnostic[] ContentMapperDiagnostics();
}

public sealed partial class Program
{
    /// <summary>Go: <c>ProgramLike</c> 里的 `Program()` 用到的 <c>contentMapperDiagnostics</c>（program.go:2035）。</summary>
    public Diagnostic[] ContentMapperDiagnostics() => _contentMapperDiagnostics;
}

/// <summary>program.go 的三个包级函数（`Compiler` 静态类的 partial，见 Pkg.cs）。</summary>
public static partial class Compiler
{
    /// <summary>Go: <c>func CombineEmitResults(results []*EmitResult) *EmitResult</c>（program.go:1936）。</summary>
    public static EmitResult CombineEmitResults(EmitResult?[] results)
    {
        var result = new EmitResult();
        foreach (var emitResult in results)
        {
            if (emitResult is null)
            {
                continue; // 跳过 nil 结果
            }
            if (emitResult.EmitSkipped)
            {
                result.EmitSkipped = true;
            }
            result.Diagnostics = [.. result.Diagnostics, .. emitResult.Diagnostics];
            result.EmittedFiles = [.. result.EmittedFiles, .. emitResult.EmittedFiles];
            if (emitResult.SourceMaps.Length != 0)
            {
                result.SourceMaps = [.. result.SourceMaps, .. emitResult.SourceMaps];
            }
        }
        return result;
    }

    /// <summary>Go: <c>func HandleNoEmitOptions(ctx, program, files, emitBuildInfo) *EmitResult</c>（program.go:1968，镜像 tsc 的 handleNoEmitOptions）。</summary>
    public static EmitResult? HandleNoEmitOptions(Context ctx, ProgramLike program, SourceFile[]? files, Func<EmitResult?>? emitBuildInfo)
    {
        if (!program.Options().NoEmit.IsTrue())
        {
            if (!program.Options().NoEmitOnError.IsTrue())
            {
                return null; // NoEmit 为 false 且 NoEmitOnError 也为 false → 正常走 emit
            }

            var diagnostics = GetDiagnosticsOfAnyProgram(
                ctx,
                program,
                files,
                true,
                program.GetBindDiagnostics,
                program.GetSemanticDiagnostics);
            if (diagnostics.Length == 0)
            {
                return null; // 开了 NoEmitOnError 但没有诊断 → 正常走 emit
            }
            return new EmitResult
            {
                Diagnostics = diagnostics,
                EmitSkipped = true,
            };
        }
        if (files is not null)
        {
            return new EmitResult { EmitSkipped = true };
        }
        if (emitBuildInfo is not null)
        {
            var result = emitBuildInfo();
            if (result is not null)
            {
                return result;
            }
        }
        return new EmitResult();
    }

    /// <summary>Go: <c>func GetDiagnosticsOfAnyProgram(ctx, program, files, skipNoEmitCheckForDtsDiagnostics, getBindDiagnostics, getSemanticDiagnostics) []*ast.Diagnostic</c>（program.go:2001）。</summary>
    public static Diagnostic[] GetDiagnosticsOfAnyProgram(
        Context ctx,
        ProgramLike program,
        SourceFile[]? files,
        bool skipNoEmitCheckForDtsDiagnostics,
        Func<Context, SourceFile?, Diagnostic[]> getBindDiagnostics,
        Func<Context, SourceFile?, Diagnostic[]> getSemanticDiagnostics)
    {
        var allDiagnostics = new List<Diagnostic>(program.GetConfigFileParsingDiagnostics());
        var configFileParsingDiagnosticsLength = allDiagnostics.Count;

        List<Diagnostic> AppendDiagnosticsForAllFiles(List<Diagnostic> diagnostics, Func<Context, SourceFile?, Diagnostic[]> getDiagnostics)
        {
            if (files is null)
            {
                diagnostics.AddRange(getDiagnostics(ctx, null));
                return diagnostics;
            }
            foreach (var file in files)
            {
                diagnostics.AddRange(getDiagnostics(ctx, file));
            }
            return diagnostics;
        }

        var syntacticDiagnostics = AppendDiagnosticsForAllFiles([], program.GetSyntacticDiagnostics);
        if (syntacticDiagnostics.Count > 0)
        {
            // 每个文件的 content mapper 失败是语法诊断，但"禁用反复失败的 mapper"那条无位置诊断仍要报出来。
            allDiagnostics.AddRange(program.ContentMapperDiagnostics());
        }
        allDiagnostics.AddRange(syntacticDiagnostics);

        // 如果没有任何语法错误，再尝试取程序（选项）、global 与语义错误。
        if (allDiagnostics.Count == configFileParsingDiagnosticsLength)
        {
            allDiagnostics.AddRange(program.GetProgramDiagnostics());

            // 提早 bind，这样可以计时。
            AppendDiagnosticsForAllFiles([], getBindDiagnostics);

            if (program.Options().ListFilesOnly.IsFalseOrUnknown())
            {
                allDiagnostics.AddRange(program.GetGlobalDiagnostics(ctx));

                if (allDiagnostics.Count == configFileParsingDiagnosticsLength)
                {
                    AppendDiagnosticsForAllFiles(allDiagnostics, getSemanticDiagnostics);
                    if (program is Program p)
                    {
                        // 增量程序会把 checking globals 与文件诊断一起缓存；
                        // 这里再扫一遍还会收集到签名生成过程中顺带产生的 global。
                        allDiagnostics.AddRange(p.GetGlobalDiagnostics(ctx));
                    }
                }

                if ((skipNoEmitCheckForDtsDiagnostics || program.Options().NoEmit.IsTrue()) &&
                    program.Options().GetEmitDeclarations() &&
                    allDiagnostics.Count == configFileParsingDiagnosticsLength)
                {
                    AppendDiagnosticsForAllFiles(allDiagnostics, program.GetDeclarationDiagnostics);
                }
            }
        }
        return [.. allDiagnostics];
    }
}
