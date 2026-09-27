// ported from: internal/execute/tsc/diagnostics.go
//
// 与 Go 的偏离（docs/architecture.md §2/§3）：
// 1) 本命名空间里有名为 `System` 的接口（Go 的 `tsc.System`），会遮蔽根命名空间 `System` →
//    本文件一律 `using global::System;`/`using global::System.IO;` 且禁止 `System.` 前缀。
// 2) `io.Writer` → `System.IO.TextWriter`；`fmt.Fprint(w, s)` → `w.Write(s)`。
// 3) Go 的 `time.Time.Format("03:04:05 PM")` → `DateTime.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture)`
//    （Go 的参考时间布局与 .NET 自定义格式在这四个字段上等价：两位 12 小时制 + AM/PM）。
// 4) `GetEnvironmentVariable(name) (string, bool)` → `TryGetEnvironmentVariable(name, out value)`（§3.2）；
//    Go 忽略 ok 的地方 C# 用 `out _`/`&&` 表达同样的短路。
// 5) Go 的 `*core.CompilerOptions` 可能是 nil → `CompilerOptions?`（`shouldBePretty` 的 `options == nil` 分支保留）。
// 6) Go 的 `core.IfElse(a, b)` 选两个函数值 → C# 需要显式委托类型的局部变量（方法组无目标类型）。
// 7) 未导出类型 `colors` → C# `Colors`；未导出方法 `bold`/`blue`/… → PascalCase（§2）。
using global::System;
using global::System.Globalization;
using global::System.IO;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.Diagnostics;
using DW = TypeScript.DiagnosticWriter.DiagnosticWriter;
using Loc = TypeScript.Locale.Locale;
using AstDiag = TypeScript.Ast.Diagnostic;
using DWDiag = TypeScript.DiagnosticWriter.Diagnostic;
using FormattingOptions = TypeScript.DiagnosticWriter.FormattingOptions;
using ComparePathsOptions = TypeScript.TSPath.ComparePathsOptions;

namespace TypeScript.Execute.Tsc;

public static partial class Tsc
{
    /// <summary>Go: <c>func getFormatOptsOfSys(sys System, locale locale.Locale) *diagnosticwriter.FormattingOptions</c>（diagnostics.go:14-21）。</summary>
    internal static FormattingOptions GetFormatOptsOfSys(System sys, Loc locale) => new()
    {
        NewLine = "\n",
        ComparePathsOptions = new ComparePathsOptions(sys.FS().UseCaseSensitiveFileNames(), sys.GetCurrentDirectory()),
        Locale = locale,
    };

    /// <summary>Go: <c>func defaultIsPretty(sys System) bool</c>（diagnostics.go:43-59）。</summary>
    internal static bool DefaultIsPretty(System sys)
    {
        if (sys.TryGetEnvironmentVariable("FORCE_COLOR", out var forceColor))
        {
            switch (forceColor)
            {
                case "":
                case "1":
                case "2":
                case "3":
                case "true":
                    return true;
                default:
                    return false;
            }
        }
        if (sys.TryGetEnvironmentVariable("NO_COLOR", out var noColor) && noColor != "")
        {
            return false;
        }
        if (sys.TryGetEnvironmentVariable("TERM", out var term) && term == "dumb")
        {
            return false;
        }
        return sys.WriteOutputIsTTY();
    }

    /// <summary>Go: <c>func shouldBePretty(sys System, options *core.CompilerOptions) bool</c>（diagnostics.go:61-66）。</summary>
    internal static bool ShouldBePretty(System sys, CompilerOptions? options)
    {
        if (options is null || options.Pretty.IsUnknown())
        {
            return DefaultIsPretty(sys);
        }
        return options.Pretty.IsTrue();
    }
}

/// <summary>Go: <c>type DiagnosticReporter = func(*ast.Diagnostic)</c>（diagnostics.go:23）。</summary>
public delegate void DiagnosticReporter(AstDiag diagnostic);

/// <summary>Go: <c>type DiagnosticsReporter = func(diagnostics []*ast.Diagnostic)</c>（diagnostics.go:136）。</summary>
public delegate void DiagnosticsReporter(AstDiag[] diagnostics);

public static partial class Tsc
{
    /// <summary>Go: <c>func QuietDiagnosticReporter(diagnostic *ast.Diagnostic)</c>（diagnostics.go:25）。</summary>
    public static void QuietDiagnosticReporter(AstDiag diagnostic)
    {
    }

    /// <summary>Go: <c>func CreateDiagnosticReporter(sys, w, locale, options) DiagnosticReporter</c>（diagnostics.go:27-41）。</summary>
    public static DiagnosticReporter CreateDiagnosticReporter(System sys, TextWriter w, Loc locale, CompilerOptions? options)
    {
        if (options is not null && options.Quiet.IsTrue())
        {
            return QuietDiagnosticReporter;
        }
        var formatOpts = GetFormatOptsOfSys(sys, locale);
        if (ShouldBePretty(sys, options))
        {
            return diagnostic =>
            {
                DW.FormatDiagnosticWithColorAndContext(w, DW.WrapASTDiagnostic(diagnostic), formatOpts);
                w.Write(formatOpts.NewLine);
            };
        }
        return diagnostic => DW.WriteFormatDiagnostic(w, DW.WrapASTDiagnostic(diagnostic), formatOpts);
    }

    /// <summary>Go: <c>func QuietDiagnosticsReporter(diagnostics []*ast.Diagnostic)</c>（diagnostics.go:138）。</summary>
    public static void QuietDiagnosticsReporter(AstDiag[] diagnostics)
    {
    }

    /// <summary>Go: <c>func CreateReportErrorSummary(sys, locale, options) DiagnosticsReporter</c>（diagnostics.go:140-148）。</summary>
    public static DiagnosticsReporter CreateReportErrorSummary(System sys, Loc locale, CompilerOptions? options)
    {
        if (ShouldBePretty(sys, options))
        {
            var formatOpts = GetFormatOptsOfSys(sys, locale);
            return diagnostics => DW.WriteErrorSummaryText(
                sys.Writer(),
                DW.FromASTDiagnostics(diagnostics),
                formatOpts);
        }
        return QuietDiagnosticsReporter;
    }

    /// <summary>Go: <c>func CreateBuilderStatusReporter(sys, w, locale, options, testing) DiagnosticReporter</c>（diagnostics.go:150-166）。</summary>
    public static DiagnosticReporter CreateBuilderStatusReporter(
        System sys,
        TextWriter w,
        Loc locale,
        CompilerOptions? options,
        CommandLineTesting? testing)
    {
        if (options is not null && options.Quiet.IsTrue())
        {
            return QuietDiagnosticReporter;
        }

        var formatOpts = GetFormatOptsOfSys(sys, locale);
        Action<TextWriter, string, DWDiag, FormattingOptions> writeStatus = ShouldBePretty(sys, options)
            ? DW.FormatDiagnosticsStatusWithColorAndTime
            : DW.FormatDiagnosticsStatusAndTime;
        return diagnostic =>
        {
            var writerDiagnostic = DW.WrapASTDiagnostic(diagnostic);
            if (testing is not null)
            {
                testing.OnBuildStatusReportStart(w);
            }
            try
            {
                writeStatus(w, sys.Now().ToString("hh:mm:ss tt", CultureInfo.InvariantCulture), writerDiagnostic, formatOpts);
                w.Write(formatOpts.NewLine);
                w.Write(formatOpts.NewLine);
            }
            finally
            {
                testing?.OnBuildStatusReportEnd(w);
            }
        };
    }

    /// <summary>Go: <c>func CreateWatchStatusReporter(sys, locale, options, testing) DiagnosticReporter</c>（diagnostics.go:168-181）。</summary>
    public static DiagnosticReporter CreateWatchStatusReporter(
        System sys,
        Loc locale,
        CompilerOptions? options,
        CommandLineTesting? testing)
    {
        var formatOpts = GetFormatOptsOfSys(sys, locale);
        Action<TextWriter, string, DWDiag, FormattingOptions> writeStatus = ShouldBePretty(sys, options)
            ? DW.FormatDiagnosticsStatusWithColorAndTime
            : DW.FormatDiagnosticsStatusAndTime;
        return diagnostic =>
        {
            var writerDiagnostic = DW.WrapASTDiagnostic(diagnostic);
            var writer = sys.Writer();
            if (testing is not null)
            {
                testing.OnWatchStatusReportStart();
            }
            try
            {
                DW.TryClearScreen(writer, writerDiagnostic, options!);
                writeStatus(writer, sys.Now().ToString("hh:mm:ss tt", CultureInfo.InvariantCulture), writerDiagnostic, formatOpts);
                writer.Write(formatOpts.NewLine);
                writer.Write(formatOpts.NewLine);
            }
            finally
            {
                testing?.OnWatchStatusReportEnd();
            }
        };
    }
}

/// <summary>Go: <c>type colors struct</c>（diagnostics.go:68-75）。</summary>
internal sealed class Colors
{
    internal bool ShowColors;

    internal bool IsWindows;
    internal bool IsWindowsTerminal;
    internal bool IsVSCode;
    internal bool SupportsRicherColors;

    /// <summary>Go: <c>func (c *colors) bold(str string) string</c>（diagnostics.go:98-103）。</summary>
    internal string Bold(string str) => !ShowColors ? str : "\u001b[1m" + str + "\u001b[22m";

    /// <summary>Go: <c>func (c *colors) blue(str string) string</c>（diagnostics.go:105-116）。</summary>
    internal string Blue(string str)
    {
        if (!ShowColors)
        {
            return str;
        }

        // Effectively Powershell and Command prompt users use cyan instead
        // of blue because the default theme doesn't show blue with enough contrast.
        if (IsWindows && !IsWindowsTerminal && !IsVSCode)
        {
            return BrightWhite(str);
        }
        return "\u001b[94m" + str + "\u001b[39m";
    }

    /// <summary>Go: <c>func (c *colors) blueBackground(str string) string</c>（diagnostics.go:118-127）。</summary>
    internal string BlueBackground(string str)
    {
        if (!ShowColors)
        {
            return str;
        }
        return SupportsRicherColors
            ? "\u001B[48;5;68m" + str + "\u001B[39;49m"
            : "\u001b[44m" + str + "\u001B[39;49m";
    }

    /// <summary>Go: <c>func (c *colors) brightWhite(str string) string</c>（diagnostics.go:129-134）。</summary>
    internal string BrightWhite(string str) => !ShowColors ? str : "\u001b[97m" + str + "\u001b[39m";
}

public static partial class Tsc
{
    /// <summary>Go: <c>func createColors(sys System) *colors</c>（diagnostics.go:77-96）。</summary>
    internal static Colors CreateColors(System sys)
    {
        if (!DefaultIsPretty(sys))
        {
            return new Colors { ShowColors = false };
        }

        sys.TryGetEnvironmentVariable("OS", out var os);
        var isWindows = TypeScript.StringUtil.StringUtil.ToLowerJS(os).Contains("windows");
        sys.TryGetEnvironmentVariable("WT_SESSION", out var wtSession);
        sys.TryGetEnvironmentVariable("TERM_PROGRAM", out var termProgram);
        sys.TryGetEnvironmentVariable("COLORTERM", out var colorTerm);
        sys.TryGetEnvironmentVariable("TERM", out var term);

        return new Colors
        {
            ShowColors = true,
            IsWindows = isWindows,
            IsWindowsTerminal = wtSession != "",
            IsVSCode = termProgram == "vscode",
            SupportsRicherColors = colorTerm == "truecolor" || term == "xterm-256color",
        };
    }
}
