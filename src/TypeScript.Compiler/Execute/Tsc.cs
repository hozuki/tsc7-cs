// ported from: internal/execute/tsc.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) Go 的 `execute` package 包级函数 → 本文件的 `public static partial class Execute`（`namespace TypeScript.Execute`，§2）。
//    Go 里以 `tsc.` 限定调用的包级函数（`tsc.CreateDiagnosticReporter` 等）用 `using static TypeScript.Execute.Tsc.Tsc;`
//    后的裸名调用（§2 的"包级函数"写法）；`tsc.` 限定的**类型**（`Tsc.System`/`Tsc.ExitStatus`/`Tsc.CommandLineResult`/
//    `Tsc.Watcher`/`Tsc.EmitInput`…）保持 `Tsc.` 前缀（命名空间限定对类型永远有效）。
// 2) `context.Context` → `TypeScript.Core.Context`。
// 3) `io.Writer` → `System.IO.TextWriter`（`tsc.System.Writer()` 的既有映射，同 Compile.cs 偏离 2）。
// 4) `(T, error)` → 抛异常（§3.2），因此 `tracing.StartTracing`/`StopTracing` 的失败路径在 C# 侧是异常：Go 打印
//    "Warning: Failed to start/stop tracing: %v" 后继续 → C# `catch (Exception ex)` 后写同样的文本。**偏离点**：
//    格式串里的 `%v`（Go error 的 `Error()` 文本）→ `ex.Message`；Go 的 `StartTracing` 失败时返回 `nil`（tracing.go:186
//    的 `return nil, …`），与 C# `catch → return null` 等价（成功时返回非 nil 的 tr）。
// 5) `tspath.ForEachAncestorDirectory` 在 C# 是 `bool + out result`（TSPath/Path.cs 文件头已登记）→ `findConfigFile` 写成
//    `if (ForEachAncestorDirectory(...)) return result;` + `return "";`。
// 6) `commandLine.Raw.(*collections.OrderedMap[string, any])`：带 ok 的断言 → `as OrderedMap<string, object?>`；
//    不带 ok 的断言（`WriteConfigFile` 的参数）→ 显式转换（Go 里失败会 panic）。
// 7) `json.MarshalIndentWrite(sys.Writer(), tsConfig, "", "    ")`：C# 的 `Json.MarshalIndentWrite` 只收 `Stream`
//    （Json/Json.cs），而 `sys.Writer()` 是 `TextWriter` → 改用与它字节等价的 `Json.MarshalIndent(tsConfig, "", "    ")`
//    再按 UTF-8 解码写进 `sys.Writer()`（json.go 与 Json.cs 都注明 `MarshalIndentWrite(w, v, "", "    ")` 与
//    `MarshalIndent(v, "", "    ")` 走同一对 `WithIndentPrefix("")`/`WithIndent("    ")` 选项）。
//    Go 忽略了它的 error；C# 的 `MarshalIndent` 失败抛 `JsonException`（Core.cs 偏离 11 的既有约定），这里不吞掉。
// 8) `pprof.BeginProfiling`/`profileSession.Stop` → `Pprof.BeginProfiling`/`ProfileSession.Stop`（no-op 会话，见
//    Pprof.cs 文件头）；Go 的 `defer profileSession.Stop()` → `try/finally`（`ProfileSession` 不是 `IDisposable`，
//    try/finally 与 Go 的 defer 语义一致：函数返回时恰好执行一次）。
// 9) `strings.ToLower` → `StringUtil.ToLowerJS`（§3.4：JS 语义的完整多字符映射）。
// 10) `fs.ReadFile(path) (string, bool)` → `FS.TryReadFile(path, out contents)`；`fs.WriteFile(path, data) error` →
//     `FS.WriteFile` 失败抛异常（Vfs/Vfs.cs），故 `fmtMain` 用 `catch (Exception ex)` 复刻 Go 的
//     `if err != nil { fmt.Fprintln(sys.Writer(), err.Error()); return ExitStatusNotImplemented }`。
// 11) `fmtMain`：Go 里它是**死代码**（唯一调用点 `case "-f"` 被注释掉），本切片要求"能编译就移植"——已核对
//     `Format.WithFormatCodeSettings`/`LsUtil.GetDefaultFormatCodeSettings`/`Format.FormatDocument` 都存在，故完整移植，
//     它的调用点仍与 Go 一样保持注释。`string(pathified)` → `(string)pathified`（`TypeScript.TSPath.Path` 是
//     record struct，有显式转换运算符）。
// 12) Go 的 `*core.CompilerOptions` 可能是 nil → C# 用 `!`/`?.`（同 Diagnostics.cs 偏离 5）；`ParsedCommandLine.Locale`
//     在 C# 是**属性** `Locale`（Go 是方法 `Locale()`）。
// 13) `tsc.ExtendedConfigCache` → `Tsc.ExtendedConfigCache`（类，实现 `tsoptions.ExtendedConfigCache` **接口**；接口裸名
//     由 `using static TypeScript.TsOptions.TsOptions;` 引入——它是 `TsOptions` 类的嵌套接口，§2 的 using static 说明）。
// 14) `startTracingIfNeeded` 里的 `config.ConfigFile.SourceFile != nil`：C# 的 `TsConfigSourceFile.SourceFile` 是非空字段
//     （构造函数必填）→ 该判空恒真，只保留 `ConfigFile` 判空。
// 15) Go 的 `func(msg *diagnostics.Message, args ...any)` → `Action<Message, object?[]>`（仓库既有约定，见
//     Compiler/Host.cs 偏离 5：`CompilerHost` 的 trace 参数就是这个委托类型）→ `getTraceFromSys` 返回它，
//     内部把 `Tsc.TraceFunc`（带 `params`）适配成该委托。
// 16) `ast.NewCompilerDiagnostic`/`diagnostics.Xxx` 都以**包名**限定，而 C# 侧包名与类型名同名（`TypeScript.Ast.Ast`、
//     `TypeScript.Diagnostics.Diagnostics`）→ 按 §2 用 `using static` 引入后写裸名（`NewCompilerDiagnostic(...)`、
//     `Options_0_and_1_cannot_be_combined`），不能用 `Ast.`/`Diagnostics.` 限定（会先绑到命名空间）。
// 17) Go 的包级 `createWatcher` 在 C# 侧是**本类的静态成员**（`Execute/Watcher.cs` 的落点，§2 的"包级函数 → 与
//     package 同名的静态类"）→ 调用点写裸名 `CreateWatcher(...)`（同在 `Execute` 类内），与 Go 逐字对应；
//     `watcher.start(ctx)` → `watcher.Start(ctx)`（Go 未导出方法 → PascalCase，同程序集可见）。
using global::System;
using global::System.Text;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.TsOptions;
// 本文件的 using 在 file-scoped `namespace` **之前** ⇒ 不参与命名空间体内的简单名解析；裸写 `Project`
// 会命中命名空间 `TypeScript.Project`（M8Project 新增，来自 internal/project）而不是类型 `ContentMapper.Project`
// ⇒ CS0118。取异名别名（与 Watcher.cs 的 `CmProject` 同约定；与命名空间同名的 `using Project = …` 无效）。
using CmProject = TypeScript.ContentMapper.Project;
using Loc = TypeScript.Locale.Locale;
using Tr = TypeScript.Tracing.Tracing;
using static TypeScript.Ast.Ast;
using Comp = TypeScript.Compiler.Compiler;
using Inc = TypeScript.Execute.Incremental.Incremental;
using IncProgram = TypeScript.Execute.Incremental.Program;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Execute.Tsc.Tsc;
using static TypeScript.Format.Format;
using static TypeScript.Json.Json;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Parser.Parser;
using static TypeScript.TSPath.TSPath;
using static TypeScript.TsOptions.TsOptions;

namespace TypeScript.Execute;

public static partial class Execute
{
    /// <summary>Go: <c>func startTracingIfNeeded(sys tsc.System, config *tsoptions.ParsedCommandLine, testing tsc.CommandLineTesting) *tracing.Tracing</c>（tsc.go:24-42）。</summary>
    internal static Tr? StartTracingIfNeeded(Tsc.System sys, ParsedCommandLine config, Tsc.CommandLineTesting? testing)
    {
        var traceDir = config.CompilerOptions()!.GenerateTrace;
        if (traceDir == "")
        {
            return null;
        }
        var configFilePath = "";
        if (config.ConfigFile is not null)
        {
            // Go: config.ConfigFile != nil && config.ConfigFile.SourceFile != nil（见文件头偏离 14）。
            configFilePath = config.ConfigFile.SourceFile.FileName();
        }
        try
        {
            return Tr.StartTracing(sys.FS(), traceDir, configFilePath, testing is not null);
        }
        catch (Exception ex)
        {
            // Go: fmt.Fprintf(sys.Writer(), "Warning: Failed to start tracing: %v\n", err)
            sys.Writer().Write("Warning: Failed to start tracing: " + ex.Message + "\n");
            return null;
        }
    }

    /// <summary>Go: <c>func stopTracing(sys tsc.System, tr *tracing.Tracing)</c>（tsc.go:44-51）。</summary>
    internal static void StopTracing(Tsc.System sys, Tr? tr)
    {
        if (tr is null)
        {
            return;
        }
        try
        {
            tr.StopTracing();
        }
        catch (Exception ex)
        {
            // Go: fmt.Fprintf(sys.Writer(), "Warning: Failed to stop tracing: %v\n", err)
            sys.Writer().Write("Warning: Failed to stop tracing: " + ex.Message + "\n");
        }
    }

    /// <summary>Go: <c>func CommandLine(ctx, sys, commandLineArgs []string, testing) tsc.CommandLineResult</c>（tsc.go:53-64）。</summary>
    public static Tsc.CommandLineResult CommandLine(Context ctx, Tsc.System sys, string[] commandLineArgs, Tsc.CommandLineTesting? testing)
    {
        if (commandLineArgs.Length > 0)
        {
            switch (TypeScript.StringUtil.StringUtil.ToLowerJS(commandLineArgs[0]))
            {
                case "-b":
                case "--b":
                case "-build":
                case "--build":
                    return TscBuildCompilation(ctx, sys, ParseBuildCommandLine(commandLineArgs, sys), testing);
                    // case "-f":
                    // 	return fmtMain(sys, commandLineArgs[1], commandLineArgs[1])
            }
        }

        return TscCompilation(ctx, sys, ParseCommandLine(commandLineArgs, sys), testing);
    }

    /// <summary>Go: <c>func fmtMain(sys tsc.System, input, output string) tsc.ExitStatus</c>（tsc.go:66-89）。</summary>
    internal static Tsc.ExitStatus FmtMain(Tsc.System sys, string input, string output)
    {
        var ctx = WithFormatCodeSettings(Context.Background, GetDefaultFormatCodeSettings(), "\n");
        input = (string)ToPath(input, sys.GetCurrentDirectory(), sys.FS().UseCaseSensitiveFileNames());
        output = (string)ToPath(output, sys.GetCurrentDirectory(), sys.FS().UseCaseSensitiveFileNames());
        if (!sys.FS().TryReadFile(input, out var text))
        {
            sys.Writer().WriteLine("File not found: " + input);
            return Tsc.ExitStatus.NotImplemented;
        }
        var pathified = ToPath(input, sys.GetCurrentDirectory(), true);
        var sourceFile = ParseSourceFile(new SourceFileParseOptions
        {
            FileName = (string)pathified,
            Path = pathified,
        }, text, GetScriptKindFromFileName((string)pathified));
        var edits = FormatDocument(ctx, sourceFile);
        var newText = ApplyBulkEdits(text, edits);

        try
        {
            sys.FS().WriteFile(output, newText);
        }
        catch (Exception ex)
        {
            sys.Writer().WriteLine(ex.Message);
            return Tsc.ExitStatus.NotImplemented;
        }
        return Tsc.ExitStatus.Success;
    }

    /// <summary>Go: <c>func tscBuildCompilation(ctx, sys, buildCommand *tsoptions.ParsedBuildCommandLine, testing) tsc.CommandLineResult</c>（tsc.go:91-113）。</summary>
    internal static Tsc.CommandLineResult TscBuildCompilation(Context ctx, Tsc.System sys, ParsedBuildCommandLine buildCommand, Tsc.CommandLineTesting? testing)
    {
        var locale = buildCommand.Locale;
        var buildCompilerOptions = buildCommand.CompilerOptions!;
        var reportDiagnostic = CreateDiagnosticReporter(sys, sys.Writer(), locale, buildCommand.CompilerOptions);

        if (buildCommand.Errors.Length > 0)
        {
            foreach (var err in buildCommand.Errors)
            {
                reportDiagnostic(err);
            }
            return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
        }

        ProfileSession? profileSession = null;
        if (buildCompilerOptions.PprofDir != "")
        {
            // !!! stderr?
            profileSession = Pprof.BeginProfiling(buildCompilerOptions.PprofDir, sys.Writer());
        }
        try
        {
            if (buildCompilerOptions.Help.IsTrue())
            {
                PrintVersion(sys, locale);
                PrintBuildHelp(sys, locale, BuildOpts);
                return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.Success };
            }

            var orchestrator = Build.Build.NewOrchestrator(new Build.Options
            {
                Sys = sys,
                Command = buildCommand,
                Testing = testing,
            });
            return orchestrator.Start(ctx);
        }
        finally
        {
            profileSession?.Stop();
        }
    }

    /// <summary>Go: <c>func tscCompilation(ctx, sys, commandLine *tsoptions.ParsedCommandLine, testing) tsc.CommandLineResult</c>（tsc.go:115-236）。</summary>
    internal static Tsc.CommandLineResult TscCompilation(Context ctx, Tsc.System sys, ParsedCommandLine commandLine, Tsc.CommandLineTesting? testing)
    {
        var configFileName = "";
        var locale = commandLine.Locale;
        var reportDiagnostic = CreateDiagnosticReporter(sys, sys.Writer(), locale, commandLine.CompilerOptions());

        if (commandLine.Errors.Length > 0)
        {
            foreach (var e in commandLine.Errors)
            {
                reportDiagnostic(e);
            }
            return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
        }

        ProfileSession? profileSession = null;
        if (commandLine.CompilerOptions()!.PprofDir != "")
        {
            // !!! stderr?
            profileSession = Pprof.BeginProfiling(commandLine.CompilerOptions()!.PprofDir, sys.Writer());
        }
        try
        {
            if (commandLine.CompilerOptions()!.Init.IsTrue())
            {
                WriteConfigFile(sys, locale, reportDiagnostic, (OrderedMap<string, object?>)commandLine.Raw!);
                return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.Success };
            }

            if (commandLine.CompilerOptions()!.Version.IsTrue())
            {
                PrintVersion(sys, locale);
                return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.Success };
            }

            if (commandLine.CompilerOptions()!.Help.IsTrue() || commandLine.CompilerOptions()!.All.IsTrue())
            {
                PrintHelp(sys, locale, commandLine);
                return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.Success };
            }

            if (commandLine.CompilerOptions()!.Watch.IsTrue() && commandLine.CompilerOptions()!.ListFilesOnly.IsTrue())
            {
                reportDiagnostic(NewCompilerDiagnostic(Options_0_and_1_cannot_be_combined, "watch", "listFilesOnly"));
                return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
            }

            if (commandLine.CompilerOptions()!.Project != "")
            {
                if (commandLine.FileNames().Length != 0)
                {
                    reportDiagnostic(NewCompilerDiagnostic(Option_project_cannot_be_mixed_with_source_files_on_a_command_line));
                    return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
                }

                var fileOrDirectory = NormalizePath(commandLine.CompilerOptions()!.Project);
                if (sys.FS().DirectoryExists(fileOrDirectory))
                {
                    configFileName = CombinePaths(fileOrDirectory, "tsconfig.json");
                    if (!sys.FS().FileExists(configFileName))
                    {
                        reportDiagnostic(NewCompilerDiagnostic(Cannot_find_a_tsconfig_json_file_at_the_current_directory_Colon_0, configFileName));
                        return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
                    }
                }
                else
                {
                    configFileName = fileOrDirectory;
                    if (!sys.FS().FileExists(configFileName))
                    {
                        reportDiagnostic(NewCompilerDiagnostic(The_specified_path_does_not_exist_Colon_0, fileOrDirectory));
                        return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
                    }
                }
            }
            else if (!commandLine.CompilerOptions()!.IgnoreConfig.IsTrue() || commandLine.FileNames().Length == 0)
            {
                var searchPath = NormalizePath(sys.GetCurrentDirectory());
                configFileName = FindConfigFile(searchPath, sys.FS().FileExists, "tsconfig.json");
                if (commandLine.FileNames().Length != 0)
                {
                    if (configFileName != "")
                    {
                        // Error to not specify config file
                        reportDiagnostic(NewCompilerDiagnostic(X_tsconfig_json_is_present_but_will_not_be_loaded_if_files_are_specified_on_commandline_Use_ignoreConfig_to_skip_this_error));
                        return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
                    }
                }
                else if (configFileName == "")
                {
                    if (commandLine.CompilerOptions()!.ShowConfig.IsTrue())
                    {
                        reportDiagnostic(NewCompilerDiagnostic(Cannot_find_a_tsconfig_json_file_at_the_current_directory_Colon_0, NormalizePath(sys.GetCurrentDirectory())));
                    }
                    else
                    {
                        PrintVersion(sys, locale);
                        PrintHelp(sys, locale, commandLine);
                    }
                    return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsSkipped };
                }
            }

            // !!! convert to options with absolute paths is usually done here, but for ease of implementation, it's done in `tsoptions.ParseCommandLine()`
            var compilerOptionsFromCommandLine = commandLine.CompilerOptions()!;
            var configForCompilation = commandLine;
            ExtendedConfigCache extendedConfigCache = new Tsc.ExtendedConfigCache();
            var compileTimes = new Tsc.CompileTimes();
            OrderedMap<string, object?>? commandLineRaw = null;
            if (configFileName != "")
            {
                var configStart = sys.Now();
                if (commandLine.Raw as OrderedMap<string, object?> is { } raw)
                {
                    // Wrap command line options in a "compilerOptions" key to match tsconfig.json structure
                    var wrapped = new OrderedMap<string, object?>();
                    wrapped.Set("compilerOptions", raw);
                    commandLineRaw = wrapped;
                }
                var (configParseResult, errors) = GetParsedCommandLineOfConfigFile(configFileName, compilerOptionsFromCommandLine, commandLineRaw, sys, extendedConfigCache);
                compileTimes.ConfigTime = sys.Now() - configStart;
                if (errors.Length != 0)
                {
                    // these are unrecoverable errors--exit to report them as diagnostics
                    foreach (var e in errors)
                    {
                        reportDiagnostic(e);
                    }
                    return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.DiagnosticsPresent_OutputsGenerated };
                }
                configForCompilation = configParseResult!;
                // Updater to reflect pretty
                reportDiagnostic = CreateDiagnosticReporter(sys, sys.Writer(), locale, commandLine.CompilerOptions());
            }

            var reportErrorSummary = CreateReportErrorSummary(sys, locale, configForCompilation.CompilerOptions());
            if (compilerOptionsFromCommandLine.ShowConfig.IsTrue())
            {
                ShowConfig(sys, configForCompilation, configFileName);
                return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.Success };
            }
            if (configForCompilation.CompilerOptions()!.Watch.IsTrue())
            {
                var watcher = CreateWatcher(
                    sys,
                    configForCompilation,
                    compilerOptionsFromCommandLine,
                    commandLineRaw,
                    reportDiagnostic,
                    reportErrorSummary,
                    testing);
                watcher.Start(ctx);
                return new Tsc.CommandLineResult { Status = Tsc.ExitStatus.Success, Watcher = watcher };
            }
            else if (configForCompilation.CompilerOptions()!.IsIncremental())
            {
                return PerformIncrementalCompilation(
                    ctx,
                    sys,
                    configForCompilation,
                    reportDiagnostic,
                    reportErrorSummary,
                    extendedConfigCache,
                    compileTimes,
                    testing);
            }
            return PerformCompilation(
                ctx,
                sys,
                configForCompilation,
                reportDiagnostic,
                reportErrorSummary,
                extendedConfigCache,
                compileTimes,
                testing);
        }
        finally
        {
            profileSession?.Stop();
        }
    }

    /// <summary>Go: <c>func findConfigFile(searchPath string, fileExists func(string) bool, configName string) string</c>（tsc.go:238-249）。</summary>
    internal static string FindConfigFile(string searchPath, Func<string, bool> fileExists, string configName)
    {
        if (ForEachAncestorDirectory(searchPath, ancestor =>
        {
            var fullConfigName = CombinePaths(ancestor, configName);
            if (fileExists(fullConfigName))
            {
                return (fullConfigName, true);
            }
            return (fullConfigName, false);
        }, out var result))
        {
            return result;
        }
        return "";
    }

    /// <summary>Go: <c>func getTraceFromSys(sys tsc.System, locale locale.Locale, testing tsc.CommandLineTesting) func(msg *diagnostics.Message, args ...any)</c>（tsc.go:251-253）。</summary>
    internal static Action<Message, object?[]> GetTraceFromSys(Tsc.System sys, Loc locale, Tsc.CommandLineTesting? testing)
    {
        var trace = GetTraceWithWriterFromSys(sys.Writer(), locale, testing);
        return (msg, args) => trace(msg, args);
    }

    /// <summary>Go: <c>func performIncrementalCompilation(...) tsc.CommandLineResult</c>（tsc.go:255-313）。</summary>
    internal static Tsc.CommandLineResult PerformIncrementalCompilation(
        Context ctx,
        Tsc.System sys,
        ParsedCommandLine config,
        Tsc.DiagnosticReporter reportDiagnostic,
        Tsc.DiagnosticsReporter reportErrorSummary,
        ExtendedConfigCache extendedConfigCache,
        Tsc.CompileTimes compileTimes,
        Tsc.CommandLineTesting? testing)
    {
        var contentMapperHost = NewContentMapperHost(ctx, sys, config.CompilerOptions()!);
        var contentMapperProject = GetContentMapperProject(contentMapperHost, config);
        try
        {
            var host = Comp.NewCachedFSCompilerHost(
                sys.GetCurrentDirectory(),
                sys.FS(),
                sys.DefaultLibraryPath(),
                extendedConfigCache,
                GetTraceFromSys(sys, config.Locale, testing),
                contentMapperProject);
            var buildInfoReadStart = sys.Now();
            var oldProgram = Inc.ReadBuildInfoProgram(config, Inc.NewBuildInfoReader(host), host);
            compileTimes.BuildInfoReadTime = sys.Now() - buildInfoReadStart;

            var tr = StartTracingIfNeeded(sys, config, testing);

            var parseStart = sys.Now();
            var program = Comp.NewProgram(new ProgramOptions
            {
                Config = config,
                Host = host,
                Tracing = tr,
            });
            compileTimes.ParseTime = sys.Now() - parseStart;
            var changesComputeStart = sys.Now();
            var incrementalProgram = IncProgram.NewProgram(program, oldProgram, Inc.CreateHost(host), sys.Now, testing is not null);
            compileTimes.ChangesComputeTime = sys.Now() - changesComputeStart;
            if (contentMapperHost is not null)
            {
                compileTimes.ContentMapperTimes = contentMapperHost.Timings();
            }
            var (result, _) = EmitAndReportStatistics(new Tsc.EmitInput
            {
                Sys = sys,
                ProgramLike = incrementalProgram,
                Program = incrementalProgram.GetProgram(),
                Config = config,
                ReportDiagnostic = reportDiagnostic,
                ReportErrorSummary = reportErrorSummary,
                Writer = sys.Writer(),
                // Go 未设置 WriteFile（nil → compiler 用 CompilerHost.WriteFile 落盘）
                CompileTimes = compileTimes,
                Testing = testing,
                Tracing = tr,
            });

            StopTracing(sys, tr);

            if (testing is not null)
            {
                testing.OnProgram(incrementalProgram);
            }
            return new Tsc.CommandLineResult
            {
                Status = result.Status,
            };
        }
        finally
        {
            contentMapperProject?.Close();
        }
    }

    /// <summary>Go: <c>func performCompilation(...) tsc.CommandLineResult</c>（tsc.go:315-364）。</summary>
    internal static Tsc.CommandLineResult PerformCompilation(
        Context ctx,
        Tsc.System sys,
        ParsedCommandLine config,
        Tsc.DiagnosticReporter reportDiagnostic,
        Tsc.DiagnosticsReporter reportErrorSummary,
        ExtendedConfigCache extendedConfigCache,
        Tsc.CompileTimes compileTimes,
        Tsc.CommandLineTesting? testing)
    {
        var contentMapperHost = NewContentMapperHost(ctx, sys, config.CompilerOptions()!);
        var contentMapperProject = GetContentMapperProject(contentMapperHost, config);
        try
        {
            var host = Comp.NewCachedFSCompilerHost(
                sys.GetCurrentDirectory(),
                sys.FS(),
                sys.DefaultLibraryPath(),
                extendedConfigCache,
                GetTraceFromSys(sys, config.Locale, testing),
                contentMapperProject);

            var tr = StartTracingIfNeeded(sys, config, testing);

            var parseStart = sys.Now();
            var program = Comp.NewProgram(new ProgramOptions
            {
                Config = config,
                Host = host,
                Tracing = tr,
            });
            compileTimes.ParseTime = sys.Now() - parseStart;
            if (contentMapperHost is not null)
            {
                compileTimes.ContentMapperTimes = contentMapperHost.Timings();
            }
            var (result, _) = EmitAndReportStatistics(new Tsc.EmitInput
            {
                Sys = sys,
                ProgramLike = program,
                Program = program,
                Config = config,
                ReportDiagnostic = reportDiagnostic,
                ReportErrorSummary = reportErrorSummary,
                Writer = sys.Writer(),
                // Go 未设置 WriteFile（nil → compiler 用 CompilerHost.WriteFile 落盘）
                CompileTimes = compileTimes,
                Testing = testing,
                Tracing = tr,
            });

            StopTracing(sys, tr);

            return new Tsc.CommandLineResult
            {
                Status = result.Status,
            };
        }
        finally
        {
            contentMapperProject?.Close();
        }
    }

    /// <summary>Go: <c>func getContentMapperProject(host contentmapper.Host, config *tsoptions.ParsedCommandLine) contentmapper.Project</c>（tsc.go:366-378）。</summary>
    internal static CmProject? GetContentMapperProject(Host? host, ParsedCommandLine config)
    {
        if (host is null || config.ContentMappers() is not { Length: > 0 })
        {
            return null;
        }
        return host.Project(new ProjectSpec
        {
            ConfigFileName = config.ConfigName(),
            Mappers = config.ContentMappers()!,
            CompilerOptions = config.CompilerOptions(),
        });
    }

    /// <summary>Go: <c>func showConfig(sys tsc.System, config *tsoptions.ParsedCommandLine, configFileName string)</c>（tsc.go:380-383）。</summary>
    internal static void ShowConfig(Tsc.System sys, ParsedCommandLine config, string configFileName)
    {
        var tsConfig = ConvertToTSConfig(config, configFileName);
        sys.Writer().Write(Encoding.UTF8.GetString(MarshalIndent(tsConfig, "", "    ")));
    }
}
