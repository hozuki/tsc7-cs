// ported from: internal/testutil/tsbaseline/error_baseline.go
//
// 与 Go 的差异（逐条；行尾/字节规则见 docs/harness.md §4，是硬约束）：
// 1) `*testing.T` → `IHarnessTest`（`Error` = Go 的 `t.Error`/gotest `assert.Check`，`Fatal` = `t.Fatalf`）。
// 2) **行尾**：`harnessNewLine = "\r\n"` 逐字保留；`formatOpts.NewLine` 就是它。`newLine()` 那个"首行不加换行"的
//    计数器照抄（所以文件开头没有空行）。
// 3) Go 的 string 是 UTF-8 字节序列、`Node.Pos()` 是**字节**偏移；C# 的 string 是 UTF-16、Pos 是**代码单元**偏移
//    （docs/architecture.md §3.1）。本文件里所有切片/计数都因此改成"按**码点**"：
//      * `utf8.RuneCountInString(line[a:b])` → `RuneCount(line[a:b])`（Go 的字节切片正好落在 rune 边界上，
//        所以两者都是"该子串的码点数"，结果逐字节相同）；
//      * `nonWhitespace.ReplaceAllString(prefix, " ")` → `AppendReplacingNonWhitespace`：Go 的 `\S` 是 **Perl 类**
//        `[^\t\n\f\r ]`（不是 .NET 的 Unicode `\S`），所以这里按码点显式判定，且一个码点换一个空格（与 Go 一致）。
// 4) `assert.Check(t, cmp.Equal(a, b), msg)` → `if (a != b) t.Error(msg)`（gotest 的 Check 是"记失败但继续"）。
// 5) `slices.Clone` → `(T[])diags.Clone()`；`slices.SortFunc` 是**不稳定**排序，C# 用同样不稳定的 `Array.Sort`；
//    `ast.CompareDiagnostics` 是全序，所以两者结果相同。
// 6) `any(d).(*diagnosticwriter.ASTDiagnostic)` → `d is ASTDiagnostic`；`d.File().(*ast.SourceFile)` → `d.File() is SourceFile`。
// 7) `strings.SplitSeq(s, "\n")` → `s.Split('\n')`（Go 的 SplitSeq 与 Split 都保留尾部空元素，空串被 `continue` 跳过）。
// 8) `strings.Builder` → `StringBuilder`（`Reset()` → `Clear()`）；`strings.Join(lines, "")` → `string.Concat`。

namespace TypeScript.TestUtil.TsBaseline;

using System.Text;
using System.Text.RegularExpressions;
using TypeScript.DiagnosticWriter;
using TypeScript.Diagnostics;
using TypeScript.TestUtil.Baseline;
using TypeScript.TestUtil.HarnessUtil;
using TypeScript.TSPath;
using static TypeScript.Core.Core;
using static TypeScript.DiagnosticWriter.DiagnosticWriter;
using static TypeScript.StringUtil.StringUtil;
using static TypeScript.TSPath.TSPath;
using AstDiagnostic = TypeScript.Ast.Diagnostic;

public static partial class TsBaseline
{
    // IO
    private const string _harnessNewLine = "\r\n";

    private static readonly FormattingOptions _formatOpts = new()
    {
        NewLine = _harnessNewLine,
    };

    private static readonly Regex _diagnosticsLocationPrefix = new(@"(?im)^(lib.*\.d\.ts)\(\d+,\d+\)", RegexOptions.Compiled);

    private static readonly Regex _diagnosticsLocationPattern = new(@"(?i)(lib.*\.d\.ts):\d+:\d+", RegexOptions.Compiled);

    /// <summary>Go: <c>func DoErrorBaseline(t *testing.T, baselinePath string, inputFiles []*harnessutil.TestFile, errors []*ast.Diagnostic, pretty bool, opts baseline.Options)</c>。</summary>
    public static void DoErrorBaseline(IHarnessTest t, string baselinePath, TestFile[] inputFiles, AstDiagnostic[] errors, bool pretty, BaselineOptions opts)
    {
        baselinePath = _tsExtension.Replace(baselinePath, ".errors.txt");
        var allErrors = errors ?? [];
        string errorBaseline;
        if (allErrors.Length > 0)
        {
            errorBaseline = GetErrorBaseline(t, inputFiles, WrapASTDiagnostics(allErrors), CompareASTDiagnostics, pretty);
        }
        else
        {
            errorBaseline = Baseline.NoContent;
        }

        Baseline.Run(t, baselinePath, errorBaseline, opts);
        if (Some(allErrors, d => d.Code() == -1))
        {
            t.Fatal("Found diagnostic with code -1, which is used to log critical assertion violations in the baseline. Inspect and fix those failures.");
        }
    }

    /// <summary>Go: <c>func minimalDiagnosticsToString(diagnostics []diagnosticwriter.Diagnostic, pretty bool) string</c>。</summary>
    private static string MinimalDiagnosticsToString(Diagnostic[] diagnostics, bool pretty)
    {
        var output = new StringWriter();
        if (pretty)
        {
            FormatDiagnosticsWithColorAndContext(output, diagnostics, _formatOpts);
        }
        else
        {
            WriteFormatDiagnostics(output, diagnostics, _formatOpts);
        }

        return output.ToString();
    }

    /// <summary>Go: <c>func GetErrorBaseline[T diagnosticwriter.Diagnostic](t *testing.T, inputFiles []*harnessutil.TestFile, diagnostics []T, compareDiagnostics func(a, b T) int, pretty bool) string</c>。</summary>
    public static string GetErrorBaseline<T>(IHarnessTest t, TestFile[] inputFiles, T[] diagnostics, Comparison<T> compareDiagnostics, bool pretty)
        where T : class, Diagnostic
    {
        var allDiagnostics = diagnostics ?? [];
        var outputLines = IterateErrorBaseline(t, inputFiles, allDiagnostics, compareDiagnostics, pretty);

        if (pretty)
        {
            var summaryBuilder = new StringWriter();
            WriteErrorSummaryText(
                summaryBuilder,
                ToDiagnostics(allDiagnostics),
                _formatOpts);
            var summary = RemoveTestPathPrefixes(summaryBuilder.ToString(), false);
            outputLines = [.. outputLines, summary];
        }

        return string.Concat(outputLines);
    }

    /// <summary>
    /// Go 侧 <c>GetErrorBaseline</c> 的便捷重载：TSGO 的调用点全都传 <c>[]*ast.Diagnostic</c>（Go 靠类型参数推导），
    /// C# 里再写一层泛型推导太啰嗦，直接收 <c>ast.Diagnostic[]</c> 并在内部 <c>WrapASTDiagnostics</c>。
    /// </summary>
    public static string GetErrorBaseline(IHarnessTest t, TestFile[] inputFiles, AstDiagnostic[] diagnostics, bool pretty)
    {
        return GetErrorBaseline(t, inputFiles, WrapASTDiagnostics(diagnostics ?? []), CompareASTDiagnostics, pretty);
    }

    /// <summary>Go: <c>func iterateErrorBaseline[T diagnosticwriter.Diagnostic](t *testing.T, inputFiles []*harnessutil.TestFile, inputDiagnostics []T, compareDiagnostics func(a, b T) int, pretty bool) []string</c>。</summary>
    private static string[] IterateErrorBaseline<T>(IHarnessTest t, TestFile[] inputFiles, T[] inputDiagnostics, Comparison<T> compareDiagnostics, bool pretty)
        where T : class, Diagnostic
    {
        var diagnostics = (T[])inputDiagnostics.Clone();
        Array.Sort(diagnostics, compareDiagnostics);

        var outputLines = new StringBuilder();
        // Count up all errors that were found in files other than lib.d.ts so we don't miss any
        var totalErrorsReportedInNonLibraryNonTsconfigFiles = 0;
        var errorsReported = 0;

        var firstLine = true;

        string NewLine()
        {
            if (firstLine)
            {
                firstLine = false;
                return "";
            }

            return "\r\n";
        }

        var result = new List<string>();

        void OutputErrorText(Diagnostic diag)
        {
            var message = FlattenDiagnosticMessage(diag, _harnessNewLine, TypeScript.Locale.Locale.Default);

            var errLines = new List<string>();
            foreach (var rawLine in RemoveTestPathPrefixes(message, false).Split('\n'))
            {
                var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;
                if (line.Length == 0)
                {
                    continue;
                }

                var outLine = $"!!! {diag.Category().Name()} TS{diag.Code()}: {line}";
                errLines.Add(outLine);
            }

            foreach (var info in diag.RelatedInformation() ?? [])
            {
                var location = "";
                if (info.File() is { } infoFile)
                {
                    location = " " + FormatLocation(
                        infoFile,
                        info.Pos(),
                        _formatOpts,
                        (output, text, _) => output.Write(text));
                }

                location = RemoveTestPathPrefixes(location, false);
                if (location.Length > 0 && IsDefaultLibraryFile(info.File()!.FileName()))
                {
                    location = _diagnosticsLocationPattern.Replace(location, "$1:--:--");
                }

                errLines.Add($"!!! related TS{info.Code()}{location}: {FlattenDiagnosticMessage(info, _harnessNewLine, TypeScript.Locale.Locale.Default)}");
            }

            foreach (var e in errLines)
            {
                outputLines.Append(NewLine());
                outputLines.Append(e);
            }

            errorsReported++;

            // do not count errors from lib.d.ts here, they are computed separately as numLibraryDiagnostics
            // if lib.d.ts is explicitly included in input files and there are some errors in it (i.e. because of duplicate identifiers)
            // then they will be added twice thus triggering 'total errors' assertion with condition
            // Similarly for tsconfig, which may be in the input files and contain errors.
            // 'totalErrorsReportedInNonLibraryNonTsconfigFiles + numLibraryDiagnostics + numTsconfigDiagnostics, diagnostics.length
            if (diag.File() is null
                || !IsDefaultLibraryFile(diag.File()!.FileName()) && !IsTsConfigFile(diag.File()!.FileName()))
            {
                totalErrorsReportedInNonLibraryNonTsconfigFiles++;
            }
        }

        var topDiagnostics = MinimalDiagnosticsToString(ToDiagnostics(diagnostics), pretty);
        topDiagnostics = RemoveTestPathPrefixes(topDiagnostics, false);
        topDiagnostics = _diagnosticsLocationPrefix.Replace(topDiagnostics, "$1(--,--)");

        result.Add(topDiagnostics + _harnessNewLine + _harnessNewLine);

        // Report global errors
        foreach (var error in diagnostics)
        {
            if (error.File() is null)
            {
                OutputErrorText(error);
            }
        }

        result.Add(outputLines.ToString());
        outputLines.Clear();
        errorsReported = 0;

        // 'merge' the lines of each input file with any errors associated with it
        var dupeCase = new HashSet<string>();
        foreach (var inputFile in inputFiles)
        {
            // Filter down to the errors in the file
            var fileErrors = Filter(diagnostics, e => e.File() is not null
                && ComparePaths(
                    RemoveTestPathPrefixes(e.File()!.FileName(), false),
                    RemoveTestPathPrefixes(inputFile.UnitName, false),
                    new ComparePathsOptions()) == 0);

            // Header
            outputLines.Append(
                $"{NewLine()}==== {RemoveTestPathPrefixes(inputFile.UnitName, false)} ({fileErrors.Length} errors) ====");

            // Make sure we emit something for every error
            var markedErrorCount = 0;

            // For each line, emit the line followed by any error squiggles matching this line
            var lineStarts = ComputeECMALineStarts(inputFile.Content);
            var lines = _lineDelimiter.Split(inputFile.Content);

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                if (line.Length > 0 && line[^1] == '\r')
                {
                    line = line[..^1];
                }

                var thisLineStart = (int)lineStarts[lineIndex];
                int nextLineStart;
                // On the last line of the file, fake the next line start number so that we handle errors on the last character of the file correctly
                if (lineIndex == lines.Length - 1)
                {
                    nextLineStart = inputFile.Content.Length;
                }
                else
                {
                    nextLineStart = (int)lineStarts[lineIndex + 1];
                }

                // Emit this line from the original file
                outputLines.Append(NewLine());
                outputLines.Append("    ");
                outputLines.Append(line);
                foreach (var errDiagnostic in fileErrors)
                {
                    // Does any error start or continue on to this line? Emit squiggles
                    var errStart = errDiagnostic.Pos();
                    var end = errStart + errDiagnostic.Len();
                    if (end >= thisLineStart && (errStart < nextLineStart || lineIndex == lines.Length - 1))
                    {
                        // How many characters from the start of this line the error starts at (could be positive or negative)
                        var relativeOffset = errStart - thisLineStart;
                        // How many characters of the error are on this line (might be longer than this line in reality)
                        var length = (end - errStart) - Math.Max(0, thisLineStart - errStart);
                        // Calculate the start of the squiggle
                        var squiggleStart = Math.Max(0, relativeOffset);
                        // TODO/REVIEW: this doesn't work quite right in the browser if a multi file test has files whose names are just the right length relative to one another
                        outputLines.Append(NewLine());
                        outputLines.Append("    ");
                        AppendReplacingNonWhitespace(line[..squiggleStart], outputLines);
                        // This was `new Array(count).join("~")`; which maps 0 to "", 1 to "", 2 to "~", 3 to "~~", etc.
                        var squiggleEnd = Math.Max(squiggleStart, Math.Min(squiggleStart + length, line.Length));
                        outputLines.Append('~', RuneCount(line[squiggleStart..squiggleEnd]));
                        // If the error ended here, or we're at the end of the file, emit its message
                        if (lineIndex == lines.Length - 1 || nextLineStart > end)
                        {
                            OutputErrorText(errDiagnostic);
                            markedErrorCount++;
                        }
                    }
                }
            }

            // Verify we didn't miss any errors in this file
            if (markedErrorCount != fileErrors.Length)
            {
                t.Error("count of errors in " + inputFile.UnitName);
            }

            var isDupe = dupeCase.Contains(SanitizeTestFilePath(inputFile.UnitName));
            result.Add(outputLines.ToString());
            if (isDupe)
            {
                // Case-duplicated files on a case-insensitive build will have errors reported in both the dupe and the original
                // thanks to the canse-insensitive path comparison on the error file path - We only want to count those errors once
                // for the assert below, so we subtract them here.
                totalErrorsReportedInNonLibraryNonTsconfigFiles -= errorsReported;
            }

            outputLines.Clear();
            errorsReported = 0;
        }

        var numLibraryDiagnostics = CountWhere(
            diagnostics,
            d => d.File() is not null && (IsDefaultLibraryFile(d.File()!.FileName()) || IsBuiltFile(d.File()!.FileName())));
        var numTsconfigDiagnostics = CountWhere(
            diagnostics,
            d => d.File() is not null && IsTsConfigFile(d.File()!.FileName()));
        var numContentMapperSupplementalDiagnostics = CountWhere(
            diagnostics,
            d =>
            {
                if (d is ASTDiagnostic astDiagnostic)
                {
                    var file = astDiagnostic.Diagnostic.File();
                    return file is not null && file.IsContentMapperSupplemental();
                }

                return d.File() is TypeScript.Ast.SourceFile sourceFile && sourceFile.IsContentMapperSupplemental();
            });

        // Verify we didn't miss any errors in total
        if (totalErrorsReportedInNonLibraryNonTsconfigFiles + numLibraryDiagnostics + numTsconfigDiagnostics + numContentMapperSupplementalDiagnostics != diagnostics.Length)
        {
            t.Error("total number of errors");
        }

        return [.. result];
    }

    /// <summary>Go: <c>func formatLocation(file diagnosticwriter.FileLike, pos int, formatOpts *diagnosticwriter.FormattingOptions, writeWithStyleAndReset diagnosticwriter.FormattedWriter) string</c>。</summary>
    private static string FormatLocation(FileLike file, int pos, FormattingOptions formatOpts, FormattedWriter writeWithStyleAndReset)
    {
        var output = new StringWriter();
        WriteLocation(output, file, pos, formatOpts, writeWithStyleAndReset);
        return output.ToString();
    }

    /// <summary>
    /// Go: <c>utf8.RuneCountInString(s)</c> —— s 一定落在码点边界上（Pos 来自 Go 的字节偏移 / C# 的 UTF-16 偏移），
    /// 所以"码点数"就是"UTF-16 长度 - 代理对个数"。
    /// </summary>
    private static int RuneCount(string s)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Go: <c>nonWhitespace.ReplaceAllString(text, " ")</c>，其中 <c>nonWhitespace = regexp.MustCompile(`\S`)</c>。
    /// Go 的 <c>\s</c> 是 Perl 类 <c>[\t\n\f\r ]</c>（**不含** \v、NBSP 等），.NET 的 <c>\s</c> 是 Unicode 语义，
    /// 所以这里按码点显式判定：非空白码点 → 一个空格（Go 是一个 rune 一个空格），空白码点原样保留。
    /// </summary>
    private static void AppendReplacingNonWhitespace(string text, StringBuilder builder)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                // 一个码点（代理对）→ 一个空格。
                builder.Append(' ');
                i++;
                continue;
            }

            builder.Append(ch switch
            {
                '\t' or '\n' or '\f' or '\r' or ' ' => ch,
                _ => ' ',
            });
        }
    }
}
