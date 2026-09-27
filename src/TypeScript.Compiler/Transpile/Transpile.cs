// ported from: internal/transpile/transpile.go
//
// 说明：本包是 `internal/testrunner/transpile_runner.go`（transpile 用例 runner）的硬前置，随 M7 一起落地。
//
// 与 Go 的差异（逐条）：
// 1) `context.Context` → `CancellationToken`（Go 侧这些调用只用到 Done()/Err()；C# 里 `Program.Emit`/
//    `GetSyntacticDiagnostics` 收的是 `TypeScript.Core.Context`，所以 worker 内部用 `new Context("",
//    CheckerLifetime.Temporary, ctx)` 包一次，语义 = Go 的 `context.Background() + 取消`）。
// 2) `(T, error)`/`nil` → `Output?`：Go 的 "ctx 取消 → Emit 返回 nil → TranspileModule 返回 nil" 保留，
//    调用方（transpile_runner.go）判 `is null` 后 `t.Fatal("transpilation was canceled")`。
// 3) `debug.Assert(cond, msg)` → `Debug.Assert(cond, msg)`（`TypeScript.Debug.Debug`，失败抛 DebugFailureException）。
// 4) `WriteFile func(...) error` → `WriteFile` 委托（无返回值，Go 的 `return nil` 消失）。
// 5) `barebonesLibContent` 用 C# 原始字符串字面量（内容逐字与 Go 一致）。
// 6) `vfs.FS` 的嵌入（`type transpileFS struct { vfs.FS; files map[string]string }`）→ C# 没有嵌入，必须实现
//    `FS` 的全部成员：Go 里"未覆写 = 走嵌入的 nil 接口 → panic"的成员在 C# 里逐个 `Debug.Fail`（见 Fs.cs）。
namespace TypeScript.Transpile;

using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.TsOptions;
using TypeScript.Core;
using TypeScript.Vfs;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;

/// <summary>Go 的 <c>package transpile</c>（单文件 transpile：transpileModule / transpileDeclaration）。</summary>
public static class Transpile
{
    /// <summary>Go: <c>type Options struct</c>（transpile.go:7-24）。</summary>
    public sealed class Options
    {
        /// <summary>Go: <c>CompilerOptions *core.CompilerOptions</c>。</summary>
        public CompilerOptions? CompilerOptions;

        /// <summary>FileName is the name to use for the transpiled file. This should only be provided if the source text relies on
        /// characteristics implied by the file's extension or path, e.g. its extension controls whether the file is
        /// parsed as a script or module, whether JSX syntax is allowed, etc. Defaults to "module.ts", or "module.tsx" if
        /// CompilerOptions.Jsx is set.</summary>
        public string FileName = "";

        /// <summary>ReportDiagnostics indicates whether syntactic and compiler option diagnostics should be included in the result.
        /// Regardless of this setting, diagnostics produced while emitting (including declaration emit errors such as those
        /// produced by isolated declarations) are always included.</summary>
        public bool ReportDiagnostics;
    }

    /// <summary>Output contains the emitted text and any requested diagnostics. Go: <c>type Output struct</c>（transpile.go:26-30）。</summary>
    public sealed class Output
    {
        public string OutputText = "";
        public Diagnostic[] Diagnostics = [];
        public string SourceMapText = "";
    }

    /// <summary>inputDirectory is the synthetic current directory used to root the single input file created for transpilation.</summary>
    private const string _inputDirectory = "/";

    /// <summary>libDirectory is the synthetic directory that the barebones default library file is placed in for declaration transpilation.</summary>
    private const string _libDirectory = "/lib";

    /// <summary>Declaration emit works without a `lib`, but some local inferences you'd expect to work won't without at least a minimal
    /// `lib` available, since the checker will type inferred declarations as `any` without these defined. Late bound symbol names,
    /// in particular, are impossible to define without `Symbol` at least partially defined.</summary>
    private const string _barebonesLibContent = """
interface Boolean {}
interface Function {}
interface CallableFunction {}
interface NewableFunction {}
interface IArguments {}
interface Number {}
interface Object {}
interface RegExp {}
interface String {}
interface Array<T> { length: number; [n: number]: T; }
interface SymbolConstructor {
    (desc?: string | number): symbol;
    for(name: string): symbol;
    readonly toStringTag: symbol;
}
declare var Symbol: SymbolConstructor;
interface Symbol {
    readonly [Symbol.toStringTag]: string;
}
""";

    /// <summary>TranspileModule transpiles a single file of source text to JavaScript using the specified options.
    /// Go: <c>func TranspileModule(ctx context.Context, input string, options Options) *Output</c>（transpile.go:80-82）。</summary>
    public static Output? TranspileModule(CancellationToken ctx, string input, Options options) =>
        TranspileWorker(ctx, input, options, false /*declaration*/);

    /// <summary>TranspileDeclaration creates a declaration (.d.ts) file from a single file of source text.
    /// Go: <c>func TranspileDeclaration(ctx context.Context, input string, options Options) *Output</c>（transpile.go:101-103）。</summary>
    public static Output? TranspileDeclaration(CancellationToken ctx, string input, Options options) =>
        TranspileWorker(ctx, input, options, true /*declaration*/);

    private static Output? TranspileWorker(CancellationToken cancellationToken, string input, Options options, bool declaration)
    {
        var opts = options.CompilerOptions is not null
            ? options.CompilerOptions.Clone()
            : new CompilerOptions();

        // Clear options that do not apply to single-file transpilation.
        opts.Incremental = Tristate.TSUnknown;
        opts.Declaration = Tristate.TSUnknown;
        opts.EmitDeclarationOnly = Tristate.TSUnknown;
        opts.NoEmit = Tristate.TSUnknown;
        opts.Lib = null;
        opts.OutFile = "";
        opts.Composite = Tristate.TSUnknown;
        opts.TsBuildInfoFile = "";
        opts.Paths = null;
        opts.RootDirs = null;
        opts.Types = null;
        opts.AllowImportingTsExtensions = Tristate.TSUnknown;
        opts.NoEmitOnError = Tristate.TSUnknown;
        opts.DeclarationDir = "";

        // Do not set `isolatedModules` if `verbatimModuleSyntax` was supplied, since it would be redundant.
        if (!opts.VerbatimModuleSyntax.IsTrue())
        {
            opts.IsolatedModules = Tristate.TSTrue;
        }
        opts.NoCheck = Tristate.TSTrue;
        opts.NoResolve = Tristate.TSTrue;

        // transpileModule/transpileDeclaration do not write anything to disk, so there's no need to verify there are
        // no conflicts between input and output paths.
        opts.SuppressOutputPathCheck = Tristate.TSTrue;

        // FileName can be a non-ts file.
        opts.AllowNonTsExtensions = Tristate.TSTrue;

        if (declaration)
        {
            opts.Declaration = Tristate.TSTrue;
            opts.EmitDeclarationOnly = Tristate.TSTrue;
            opts.IsolatedDeclarations = Tristate.TSTrue;
        }
        else
        {
            opts.Declaration = Tristate.TSFalse;
            opts.DeclarationMap = Tristate.TSFalse;
            opts.IsolatedDeclarations = Tristate.TSFalse;
        }

        // When transpiling declarations, we need a lib. GetDefaultLibFileName will cause the barebones lib below to
        // be used instead of a real lib.
        if (declaration)
        {
            opts.NoLib = Tristate.TSFalse;
        }
        else
        {
            opts.NoLib = Tristate.TSTrue;
        }

        // If jsx is specified, then treat the file as .tsx.
        var fileName = options.FileName;
        if (fileName == "")
        {
            if (opts.Jsx != JsxEmit.None)
            {
                fileName = "module.tsx";
            }
            else
            {
                fileName = "module.ts";
            }
        }
        var inputFileName = GetNormalizedAbsolutePath(fileName, _inputDirectory);

        var files = new Dictionary<string, string>
        {
            [inputFileName] = input,
        };

        // Declaration emit needs a default lib to resolve global types (e.g. `Array`, `Symbol`); plain transpilation
        // sets NoLib so none is read. The default lib name depends on the configured target.
        if (declaration)
        {
            var libFileName = GetDefaultLibFileName(opts);
            files[CombinePaths(_libDirectory, libFileName)] = _barebonesLibContent;
        }

        var host = Compiler.NewCompilerHost(_inputDirectory, new TranspileFS(files), _libDirectory, null, null, null);

        var program = TypeScript.Compiler.Compiler.NewProgram(new ProgramOptions
        {
            Config = new ParsedCommandLine
            {
                ParsedConfig = new ParsedOptions
                {
                    FileNames = [inputFileName],
                    CompilerOptions = opts,
                },
            },
            Host = host,
            SkipModuleResolution = true,
        });

        var ctx = new Context("", CheckerLifetime.Temporary, cancellationToken);

        var allDiagnostics = new List<Diagnostic>();
        if (options.ReportDiagnostics)
        {
            var sourceFile = program.GetSourceFile(inputFileName);
            allDiagnostics.AddRange(program.GetSyntacticDiagnostics(ctx, sourceFile));
            allDiagnostics.AddRange(program.GetConfigFileParsingDiagnostics());
            allDiagnostics.AddRange(program.GetProgramDiagnostics());
        }

        var emitOnly = EmitOnly.EmitAll;
        if (declaration)
        {
            emitOnly = EmitOnly.EmitOnlyDts;
        }

        var outputText = "";
        var sourceMapText = "";
        var hasOutputText = false;
        var hasSourceMapText = false;
        var result = program.Emit(ctx, new EmitOptions
        {
            EmitOnly = emitOnly,
            ForceEmit = declaration,
            WriteFile = (fileName, text, _) =>
            {
                if (fileName.EndsWith(".map", StringComparison.Ordinal))
                {
                    Debug.Assert(!hasSourceMapText, "Unexpected multiple source map outputs, file: " + fileName);
                    sourceMapText = text;
                    hasSourceMapText = true;
                }
                else
                {
                    Debug.Assert(!hasOutputText, "Unexpected multiple outputs, file: " + fileName);
                    outputText = text;
                    hasOutputText = true;
                }
            },
        });
        if (result is null)
        {
            return null;
        }

        // Diagnostics produced during emit (e.g. isolated declaration errors) are always included, regardless of
        // ReportDiagnostics.
        allDiagnostics.AddRange(result.Diagnostics);

        Debug.Assert(hasOutputText, "Output generation failed");

        return new Output
        {
            OutputText = outputText,
            Diagnostics = [.. allDiagnostics],
            SourceMapText = sourceMapText,
        };
    }
}
