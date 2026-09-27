// ported from: internal/diagnosticwriter/diagnosticwriter.go
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.2/§3.3/§3.8）：
// 1) `io.Writer` → `System.IO.TextWriter`（Go 的 `fmt.Fprint/Fprintf` → `TextWriter.Write`；调用方传 `StringWriter`）。
// 2) 包级函数/常量 → 静态类 `DiagnosticWriter`（与 package 同名，§2）；Go 未导出常量 → internal + PascalCase。
// 3) `type FileLike interface { FileName() string; Text() string; ECMALineMap() []core.TextPos }` → C# 接口 `FileLike`
//    **继承 `Ast.SourceFileLike`**（Go 靠结构化类型让 FileLike 也能喂给 `scanner.GetECMALineOfPosition`；C# 必须显式
//    写继承关系）。`ast.SourceFile` 的基类型表里加了 `TypeScript.DiagnosticWriter.FileLike`（见该文件）。
// 4) `type Diagnostic interface` → C# `Diagnostic` 接口。`ast.Diagnostic`（C# `TypeScript.Ast.Diagnostic`）与本命名空间
//    的接口同名，本文件用别名 `AstDiag`。
// 5) `ASTDiagnostic` 在 Go 里内嵌 `*ast.Diagnostic`（Code/Category/Localize 走提升）；C# 只能显式转发。
// 6) **保真点**：`ASTDiagnostic.File()` 在 Go 里对「内容映射过」的文件每次都返回**新对象**（`&renamedFile{}` /
//    `newOriginalTextFile`），而 `map[FileLike][]Diagnostic` 的键是接口值（动态类型 + 指针）⇒ 每次调用命中不同键。
//    这会让 pretty 错误汇总表**每个诊断各占一行**。C# 逐字保留（`File()` 不做缓存），`Dictionary` 用引用相等键，
//    行为因此与 Go 相同。`.errors.txt`（pretty=false）路径不经过汇总表，所以对 baseline 无影响。
// 7) `FormattingOptions` 在 Go 里总是按 `*FormattingOptions` 传递（可空）→ C# 是 class；内嵌的
//    `tspath.ComparePathsOptions` → C# 字段 `ComparePathsOptions`（Go 的字段提升在 C# 里写不出，调用点写
//    `formatOpts.ComparePathsOptions.CurrentDirectory`）。
// 8) `strings.TrimRightFunc(s, unicode.IsSpace)` → 私有 `TrimRightSpace` + `char.IsWhiteSpace`：.NET 的
//    `char.IsWhiteSpace` 与 Go 的 `unicode.IsSpace` 覆盖同一组码点（\t \n \v \f \r 空格 U+0085 U+00A0 U+1680
//    U+2000-200A U+2028 U+2029 U+202F U+205F U+3000）。
// 9) `fmt.Fprintf(w, "%*s"/"%*d", width, x)` → `x.PadLeft(width)`（Go 的 `%*s`/`%*d` 是右对齐、左补空格）；
//    `strconv.Itoa` → `int.ToString(CultureInfo.InvariantCulture)`（不变文化，避免本地数字形状）。
// 10) `strings.Repeat(s, n)` → `new string(c, n)`；`utf8`/字节切片 → 代码单元下标（§3.1）。
// 11) `slices.Contains` → `Array.IndexOf(...) >= 0`；`maps.Keys` + `slices.SortedFunc` → `Dictionary.Keys` + `OrderBy`
//     （`strings.Compare` = 序数比较 → `StringComparer.Ordinal`）。
// 12) `len(strings.Split(headerRow, " ")[0])` 在 Go 里是**字节**长度，C# 的 `string.Length` 是 UTF-16 长度；表头是
//     ASCII（"Errors  Files"）时两者相同，非 ASCII 语言下这是列宽启发式的差异（已记录）。
// 13) Go 的 `map[FileLike][]Diagnostic` → `Dictionary<FileLike, List<Diagnostic>>`（Go 的 append 语义）。
// 14) `panic("Unhandled diagnostic category")` → `Debug.Fail(...)`（§3.2）。
using System.Globalization;
using System.IO;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.SpanMap;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.TSPath.TSPath;
using Diag = TypeScript.Diagnostics.Diagnostics;
using AstDiag = TypeScript.Ast.Diagnostic;
using Loc = TypeScript.Locale.Locale;
using Scan = TypeScript.Scanner.Scanner;

namespace TypeScript.DiagnosticWriter;

/// <summary>Go 的 <c>type FormattedWriter func(output io.Writer, text string, formatStyle string)</c>。</summary>
public delegate void FormattedWriter(TextWriter output, string text, string formatStyle);

/// <summary>Go 的 <c>type ErrorSummary struct</c>。</summary>
public sealed class ErrorSummary
{
    public int TotalErrorCount;
    public Diagnostic[] GlobalErrors = [];
    public Dictionary<FileLike, List<Diagnostic>> ErrorsByFile = [];
    public FileLike[] SortedFiles = [];
}

/// <summary>Go 的 <c>type FileLike interface { FileName() string; Text() string; ECMALineMap() []core.TextPos }</c>。</summary>
public interface FileLike : SourceFileLike
{
    string FileName();
}

/// <summary>Diagnostic interface abstracts over ast.Diagnostic and LSP diagnostics.</summary>
public interface Diagnostic
{
    FileLike? File();

    int Pos();

    int End();

    int Len();

    int Code();

    Category Category();

    /// <summary>
    /// Source is a custom prefix shown before the code instead of "TS" (empty means "TS"). A non-empty
    /// value marks the diagnostic as coming from an external source (e.g. a content mapper).
    /// </summary>
    string Source();

    string Localize(Loc locale);

    Diagnostic[]? MessageChain();

    Diagnostic[]? RelatedInformation();
}

/// <summary>ASTDiagnostic wraps ast.Diagnostic to implement the Diagnostic interface.</summary>
public sealed class ASTDiagnostic : Diagnostic
{
    public readonly AstDiag Diagnostic;

    public ASTDiagnostic(AstDiag d)
    {
        Diagnostic = d;
    }

    public Diagnostic[]? RelatedInformation()
    {
        var related = Diagnostic.RelatedInformation();
        if (related is null)
        {
            return null;
        }

        var result = new Diagnostic[related.Length];
        for (var i = 0; i < related.Length; i++)
        {
            result[i] = new ASTDiagnostic(related[i]!);
        }

        return result;
    }

    public FileLike? File()
    {
        var file = Diagnostic.File();
        if (file is null)
        {
            return null;
        }

        var fileName = file.FileName();
        if (file.CanonicalSourceFile() is { } canonical)
        {
            fileName = canonical.FileName();
        }

        if (Resolve()._useOriginal)
        {
            // The mapper's own diagnostics (Source != "") already carry original ranges; compiler
            // diagnostics have their transformed ranges mapped back. Both render against the original,
            // untransformed text. Diagnostics in synthesized code (see resolve) keep the virtual text.
            return new OriginalTextFile(file, fileName);
        }

        if (!string.Equals(fileName, file.FileName(), StringComparison.Ordinal))
        {
            return new RenamedFile(file, fileName);
        }

        return file;
    }

    public string Source()
    {
        return Diagnostic.Source();
    }

    public int Pos() => Resolve()._loc.Pos();

    public int End() => Resolve()._loc.End();

    public int Len() => Resolve()._loc.Len();

    public int Code() => Diagnostic.Code();

    public Category Category() => Diagnostic.Category();

    public string Localize(Loc locale) => Diagnostic.Localize(locale);

    // resolvedLocation describes how a diagnostic on a content-mapped file should be reported.
    internal readonly struct ResolvedLocation
    {
        internal readonly TextRange _loc;
        internal readonly bool _useOriginal;   // render against the file's original, untransformed text
        internal readonly bool _synthesized;   // the range is in virtual code with no corresponding original location

        internal ResolvedLocation(TextRange loc, bool useOriginal, bool synthesized)
        {
            _loc = loc;
            _useOriginal = useOriginal;
            _synthesized = synthesized;
        }
    }

    /// <summary>
    /// resolve determines where and against which text a diagnostic should be reported. A content mapper's
    /// own diagnostics already carry original ranges. A compiler diagnostic on a content-mapped file has its
    /// virtual range mapped back to the original; if it falls entirely within synthesized code, there is no
    /// original location, so it is shown against the virtual text and flagged as synthesized.
    /// </summary>
    internal ResolvedLocation Resolve()
    {
        var loc = Diagnostic.Loc();
        var file = Diagnostic.File();
        if (file is null)
        {
            return new ResolvedLocation(loc, false, false);
        }

        if (Diagnostic.Source() != "")
        {
            return new ResolvedLocation(loc, true, false);
        }

        if (file.SpanMap() is { } spanMap)
        {
            var (mapped, fidelity) = spanMap.VirtualToOriginalSpan(loc);
            if (fidelity == Fidelity.None)
            {
                return new ResolvedLocation(loc, false, true);
            }

            return new ResolvedLocation(mapped, true, false);
        }

        return new ResolvedLocation(loc, false, false);
    }

    public Diagnostic[] MessageChain()
    {
        var chain = Diagnostic.MessageChain();
        var result = new List<Diagnostic>(chain is null ? 1 : chain.Length + 1);
        if (chain is not null)
        {
            foreach (var c in chain)
            {
                result.Add(new ASTDiagnostic(c!));
            }
        }

        if (Resolve()._synthesized)
        {
            // The diagnostic points into synthesized virtual code; make clear the shown location is not in the
            // original file, and which content mapper produced it.
            var note = NewCompilerDiagnostic(
                Diag.This_location_is_in_virtual_code_produced_by_the_content_mapper_0_and_has_no_corresponding_location_in_the_original_file,
                Diagnostic.File()!.ContentMapper());
            result.Add(DiagnosticWriter.WrapASTDiagnostic(note));
        }

        return [.. result];
    }
}

/// <summary>
/// originalTextFile presents a source file's original (untransformed) text as a FileLike, so that
/// diagnostics whose ranges point into that text render at the correct locations.
/// </summary>
internal sealed class OriginalTextFile : FileLike
{
    private readonly string _fileName;
    private readonly string _text;
    private readonly TextPos[] _lineMap;

    internal OriginalTextFile(SourceFile file, string fileName)
    {
        var text = file.OriginalText();
        _fileName = fileName;
        _text = text;
        _lineMap = [.. ComputeECMALineStarts(text)];
    }

    public string FileName() => _fileName;

    public string Text() => _text;

    public TextPos[] ECMALineMap() => _lineMap;
}

internal sealed class RenamedFile : FileLike
{
    private readonly SourceFile _file;
    private readonly string _fileName;

    internal RenamedFile(SourceFile file, string fileName)
    {
        _file = file;
        _fileName = fileName;
    }

    public string FileName() => _fileName;

    public string Text() => _file.Text();

    public TextPos[] ECMALineMap() => _file.ECMALineMap();
}

public static partial class DiagnosticWriter
{
    public static ASTDiagnostic WrapASTDiagnostic(AstDiag d)
    {
        return new ASTDiagnostic(d);
    }

    public static ASTDiagnostic[] WrapASTDiagnostics(AstDiag[] diags)
    {
        var result = new ASTDiagnostic[diags.Length];
        for (var i = 0; i < diags.Length; i++)
        {
            result[i] = WrapASTDiagnostic(diags[i]!);
        }

        return result;
    }

    public static Diagnostic[] FromASTDiagnostics(AstDiag[] diags)
    {
        var result = new Diagnostic[diags.Length];
        for (var i = 0; i < diags.Length; i++)
        {
            result[i] = WrapASTDiagnostic(diags[i]!);
        }

        return result;
    }

    public static Diagnostic[] ToDiagnostics<T>(T[] diags)
        where T : Diagnostic
    {
        var result = new Diagnostic[diags.Length];
        for (var i = 0; i < diags.Length; i++)
        {
            result[i] = diags[i]!;
        }

        return result;
    }

    public static int CompareASTDiagnostics(ASTDiagnostic a, ASTDiagnostic b)
    {
        return CompareDiagnostics(a.Diagnostic, b.Diagnostic);
    }
}

/// <summary>Go 的 <c>type FormattingOptions struct { Locale locale.Locale; tspath.ComparePathsOptions; NewLine string }</c>。</summary>
public sealed class FormattingOptions
{
    public Loc Locale;

    /// <summary>Go 内嵌的 <c>tspath.ComparePathsOptions</c>（字段提升在 C# 里写不出，改成同名字段）。</summary>
    public ComparePathsOptions ComparePathsOptions;

    public string NewLine = "";
}

public static partial class DiagnosticWriter
{
    internal const string ForegroundColorEscapeGrey = "\u001b[90m";
    internal const string ForegroundColorEscapeRed = "\u001b[91m";
    internal const string ForegroundColorEscapeYellow = "\u001b[93m";
    internal const string ForegroundColorEscapeBlue = "\u001b[94m";
    internal const string ForegroundColorEscapeCyan = "\u001b[96m";

    internal const string GutterStyleSequence = "\u001b[7m";
    internal const string GutterSeparator = " ";
    internal const string ResetEscapeSequence = "\u001b[0m";
    internal const string Ellipsis = "...";

    public static void FormatDiagnosticsWithColorAndContext(TextWriter output, Diagnostic[] diags, FormattingOptions formatOpts)
    {
        if (diags.Length == 0)
        {
            return;
        }

        for (var i = 0; i < diags.Length; i++)
        {
            if (i > 0)
            {
                output.Write(formatOpts.NewLine);
            }

            FormatDiagnosticWithColorAndContext(output, diags[i]!, formatOpts);
        }
    }

    public static void FormatDiagnosticWithColorAndContext(TextWriter output, Diagnostic diagnostic, FormattingOptions formatOpts)
    {
        if (diagnostic.File() is { } file)
        {
            var pos = diagnostic.Pos();
            WriteLocation(output, file, pos, formatOpts, WriteWithStyleAndReset);
            output.Write(" - ");
        }

        WriteWithStyleAndReset(output, diagnostic.Category().Name(), GetCategoryFormat(diagnostic.Category()));
        output.Write($"{ForegroundColorEscapeGrey} {DiagnosticPrefix(diagnostic)}{diagnostic.Code()}: {ResetEscapeSequence}");
        WriteFlattenedDiagnosticMessage(output, diagnostic, formatOpts.NewLine, formatOpts.Locale);

        if (diagnostic.File() is not null && diagnostic.Code() != Diag.File_appears_to_be_binary.Code())
        {
            output.Write(formatOpts.NewLine);
            WriteCodeSnippet(output, diagnostic.File()!, diagnostic.Pos(), diagnostic.Len(), GetCategoryFormat(diagnostic.Category()), "", formatOpts);
            output.Write(formatOpts.NewLine);
        }

        var relatedInformation = diagnostic.RelatedInformation();
        if (relatedInformation is { Length: > 0 })
        {
            foreach (var related in relatedInformation)
            {
                if (related.File() is { } relatedFile)
                {
                    output.Write(formatOpts.NewLine);
                    output.Write("  ");
                    var pos = related.Pos();
                    WriteLocation(output, relatedFile, pos, formatOpts, WriteWithStyleAndReset);
                    output.Write(" - ");
                    WriteFlattenedDiagnosticMessage(output, related, formatOpts.NewLine, formatOpts.Locale);
                    WriteCodeSnippet(output, relatedFile, pos, related.Len(), ForegroundColorEscapeCyan, "    ", formatOpts);
                }

                output.Write(formatOpts.NewLine);
            }
        }
    }

    internal static void WriteCodeSnippet(TextWriter writer, FileLike sourceFile, int start, int length, string squiggleColor, string indent, FormattingOptions formatOpts)
    {
        var first = Scan.GetECMALineAndUTF16CharacterOfPosition(sourceFile, start);
        var firstLine = first.Line;
        var firstLineChar = (int)first.Character;

        var last = Scan.GetECMALineAndUTF16CharacterOfPosition(sourceFile, start + length);
        var lastLine = last.Line;
        var lastLineChar = (int)last.Character;
        if (length == 0)
        {
            lastLineChar++; // When length is zero, squiggle the character right after the start position.
        }

        var lastLineOfFile = Scan.GetECMALineOfPosition(sourceFile, sourceFile.Text().Length);

        var hasMoreThanFiveLines = lastLine - firstLine >= 4;
        var gutterWidth = (lastLine + 1).ToString(CultureInfo.InvariantCulture).Length;
        if (hasMoreThanFiveLines)
        {
            gutterWidth = Math.Max(Ellipsis.Length, gutterWidth);
        }

        for (var i = firstLine; i <= lastLine; i++)
        {
            writer.Write(formatOpts.NewLine);

            // If the error spans over 5 lines, we'll only show the first 2 and last 2 lines,
            // so we'll skip ahead to the second-to-last line.
            if (hasMoreThanFiveLines && firstLine + 1 < i && i < lastLine - 1)
            {
                writer.Write(indent);
                writer.Write(GutterStyleSequence);
                writer.Write(Ellipsis.PadLeft(gutterWidth));
                writer.Write(ResetEscapeSequence);
                writer.Write(GutterSeparator);
                writer.Write(formatOpts.NewLine);
                i = lastLine - 1;
            }

            var lineStart = Scan.GetECMAPositionOfLineAndByteOffset(sourceFile, i, 0);
            int lineEnd;
            if (i < lastLineOfFile)
            {
                lineEnd = Scan.GetECMAPositionOfLineAndByteOffset(sourceFile, i + 1, 0);
            }
            else
            {
                lineEnd = sourceFile.Text().Length;
            }

            // Go: strings.TrimRightFunc(text[lineStart:lineEnd], unicode.IsSpace)，随后把制表符换成单个空格
            var lineContent = TrimRightSpace(sourceFile.Text().AsSpan(lineStart, lineEnd - lineStart)).Replace('\t', ' ');

            // Output the gutter and the actual contents of the line.
            writer.Write(indent);
            writer.Write(GutterStyleSequence);
            writer.Write((i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(gutterWidth));
            writer.Write(ResetEscapeSequence);
            writer.Write(GutterSeparator);
            writer.Write(lineContent);
            writer.Write(formatOpts.NewLine);

            // Output the gutter and the error span for the line using tildes.
            writer.Write(indent);
            writer.Write(GutterStyleSequence);
            writer.Write("".PadLeft(gutterWidth));
            writer.Write(ResetEscapeSequence);
            writer.Write(GutterSeparator);
            writer.Write(squiggleColor);
            if (i == firstLine)
            {
                // If we're on the last line, then limit it to the last character of the last line.
                // Otherwise, we'll just squiggle the rest of the line, giving 'slice' no end position.
                int lastCharForLine;
                if (i == lastLine)
                {
                    lastCharForLine = lastLineChar;
                }
                else
                {
                    lastCharForLine = (int)UTF16Len(lineContent);
                }

                // Fill with spaces until the first character,
                // then squiggle the remainder of the line.
                writer.Write(new string(' ', firstLineChar));
                writer.Write(new string('~', lastCharForLine - firstLineChar));
            }
            else if (i == lastLine)
            {
                // Squiggle until the final character.
                writer.Write(new string('~', lastLineChar));
            }
            else
            {
                // Squiggle the entire line.
                writer.Write(new string('~', (int)UTF16Len(lineContent)));
            }

            writer.Write(ResetEscapeSequence);
        }
    }

    /// <summary>Go: <c>strings.TrimRightFunc(s, unicode.IsSpace)</c>（见文件头第 8 条）。</summary>
    internal static string TrimRightSpace(ReadOnlySpan<char> s)
    {
        var end = s.Length;
        while (end > 0 && char.IsWhiteSpace(s[end - 1]))
        {
            end--;
        }

        return s[..end].ToString();
    }

    public static string FlattenDiagnosticMessage(Diagnostic d, string newLine, Loc locale)
    {
        var output = new StringWriter();
        WriteFlattenedDiagnosticMessage(output, d, newLine, locale);
        return output.ToString();
    }

    public static void WriteFlattenedASTDiagnosticMessage(TextWriter writer, AstDiag diagnostic, string newline, Loc locale)
    {
        WriteFlattenedDiagnosticMessage(writer, WrapASTDiagnostic(diagnostic), newline, locale);
    }

    public static void WriteFlattenedDiagnosticMessage(TextWriter writer, Diagnostic diagnostic, string newline, Loc locale)
    {
        writer.Write(diagnostic.Localize(locale));

        var chain = diagnostic.MessageChain();
        if (chain is not null)
        {
            foreach (var c in chain)
            {
                FlattenDiagnosticMessageChain(writer, c, newline, locale, 1 /*level*/);
            }
        }
    }

    private static void FlattenDiagnosticMessageChain(TextWriter writer, Diagnostic chain, string newLine, Loc locale, int level)
    {
        writer.Write(newLine);
        for (var i = 0; i < level; i++)
        {
            writer.Write("  ");
        }

        writer.Write(chain.Localize(locale));
        var childChain = chain.MessageChain();
        if (childChain is not null)
        {
            foreach (var child in childChain)
            {
                FlattenDiagnosticMessageChain(writer, child, newLine, locale, level + 1);
            }
        }
    }

    /// <summary>
    /// diagnosticPrefix returns the prefix shown before a diagnostic's code, e.g. "TS" for compiler
    /// diagnostics or a content mapper's custom source for its diagnostics.
    /// </summary>
    internal static string DiagnosticPrefix(Diagnostic diagnostic)
    {
        var source = diagnostic.Source();
        if (source != "")
        {
            return source;
        }

        return "TS";
    }

    internal static string GetCategoryFormat(Category category)
    {
        switch (category)
        {
            case Category.Error:
                return ForegroundColorEscapeRed;
            case Category.Warning:
                return ForegroundColorEscapeYellow;
            case Category.Suggestion:
                return ForegroundColorEscapeGrey;
            case Category.Message:
                return ForegroundColorEscapeBlue;
        }

        Debug.Fail("Unhandled diagnostic category");
        return "";
    }

    internal static void WriteWithStyleAndReset(TextWriter output, string text, string formatStyle)
    {
        output.Write(formatStyle);
        output.Write(text);
        output.Write(ResetEscapeSequence);
    }

    public static void WriteLocation(TextWriter output, FileLike file, int pos, FormattingOptions? formatOpts, FormattedWriter writeWithStyleAndReset)
    {
        var (firstLine, firstChar) = Scan.GetECMALineAndUTF16CharacterOfPosition(file, pos);
        string relativeFileName;
        if (formatOpts is not null)
        {
            relativeFileName = ConvertToRelativePath(file.FileName(), formatOpts.ComparePathsOptions);
        }
        else
        {
            relativeFileName = file.FileName();
        }

        writeWithStyleAndReset(output, relativeFileName, ForegroundColorEscapeCyan);
        output.Write(":");
        writeWithStyleAndReset(output, (firstLine + 1).ToString(CultureInfo.InvariantCulture), ForegroundColorEscapeYellow);
        output.Write(":");
        writeWithStyleAndReset(output, ((int)firstChar + 1).ToString(CultureInfo.InvariantCulture), ForegroundColorEscapeYellow);
    }

    // Some of these lived in watch.ts, but they're not specific to the watch API.

    public static void WriteErrorSummaryText(TextWriter output, Diagnostic[] allDiagnostics, FormattingOptions formatOpts)
    {
        // Roughly corresponds to 'getErrorSummaryText' from watch.ts

        var errorSummary = GetErrorSummary(allDiagnostics);
        var totalErrorCount = errorSummary.TotalErrorCount;
        if (totalErrorCount == 0)
        {
            return;
        }

        FileLike? firstFile = null;
        if (errorSummary.SortedFiles.Length > 0)
        {
            firstFile = errorSummary.SortedFiles[0];
        }

        var firstFileName = PrettyPathForFileError(firstFile, firstFile is null ? null : errorSummary.ErrorsByFile[firstFile], formatOpts);
        var numErroringFiles = errorSummary.ErrorsByFile.Count;

        string message;
        if (totalErrorCount == 1)
        {
            // Special-case a single error.
            if (errorSummary.GlobalErrors.Length > 0 || firstFileName == "")
            {
                message = Diag.Found_1_error.Localize(formatOpts.Locale);
            }
            else
            {
                message = Diag.Found_1_error_in_0.Localize(formatOpts.Locale, firstFileName);
            }
        }
        else
        {
            switch (numErroringFiles)
            {
                case 0:
                    // No file-specific errors.
                    message = Diag.Found_0_errors.Localize(formatOpts.Locale, totalErrorCount);
                    break;
                case 1:
                    // One file with errors.
                    message = Diag.Found_0_errors_in_the_same_file_starting_at_Colon_1.Localize(formatOpts.Locale, totalErrorCount, firstFileName);
                    break;
                default:
                    // Multiple files with errors.
                    message = Diag.Found_0_errors_in_1_files.Localize(formatOpts.Locale, totalErrorCount, numErroringFiles);
                    break;
            }
        }

        output.Write(formatOpts.NewLine);
        output.Write(message);
        output.Write(formatOpts.NewLine);
        output.Write(formatOpts.NewLine);
        if (numErroringFiles > 1)
        {
            WriteTabularErrorsDisplay(output, errorSummary, formatOpts);
            output.Write(formatOpts.NewLine);
        }
    }

    internal static ErrorSummary GetErrorSummary(Diagnostic[] diags)
    {
        var totalErrorCount = 0;
        var globalErrors = new List<Diagnostic>();
        var errorsByFile = new Dictionary<FileLike, List<Diagnostic>>();

        foreach (var diagnostic in diags)
        {
            if (diagnostic.Category() != Category.Error)
            {
                continue;
            }

            totalErrorCount++;
            if (diagnostic.File() is null)
            {
                globalErrors.Add(diagnostic);
            }
            else
            {
                // Go: errorsByFile[diagnostic.File()] = append(errorsByFile[diagnostic.File()], diagnostic)
                // —— File() 对内容映射过的文件每次返回新对象，键因此是"每次调用一个"（见文件头第 6 条）。
                var key = diagnostic.File()!;
                if (!errorsByFile.TryGetValue(key, out var list))
                {
                    list = [];
                    errorsByFile[key] = list;
                }

                list.Add(diagnostic);
            }
        }

        // !!!
        // Need an ordered map here, but sorting for consistency.
        var sortedFiles = errorsByFile.Count == 0
            ? []
            : errorsByFile.Keys.OrderBy(f => f.FileName(), StringComparer.Ordinal).ToArray();

        return new ErrorSummary
        {
            TotalErrorCount = totalErrorCount,
            GlobalErrors = [.. globalErrors],
            ErrorsByFile = errorsByFile,
            SortedFiles = sortedFiles,
        };
    }

    internal static void WriteTabularErrorsDisplay(TextWriter output, ErrorSummary errorSummary, FormattingOptions formatOpts)
    {
        var sortedFiles = errorSummary.SortedFiles;

        var maxErrors = 0;
        foreach (var errorsForFile in errorSummary.ErrorsByFile.Values)
        {
            maxErrors = Math.Max(maxErrors, errorsForFile.Count);
        }

        // !!!
        // TODO (drosen): This was never localized.
        // Should make this better.
        var headerRow = Diag.Errors_Files.Localize(formatOpts.Locale);
        var leftColumnHeadingLength = headerRow.Split(' ')[0].Length;
        var lengthOfBiggestErrorCount = maxErrors.ToString(CultureInfo.InvariantCulture).Length;
        var leftPaddingGoal = Math.Max(leftColumnHeadingLength, lengthOfBiggestErrorCount);
        var headerPadding = Math.Max(lengthOfBiggestErrorCount - leftColumnHeadingLength, 0);

        output.Write(new string(' ', headerPadding));
        output.Write(headerRow);
        output.Write(formatOpts.NewLine);

        foreach (var file in sortedFiles)
        {
            var fileErrors = errorSummary.ErrorsByFile[file];
            var errorCount = fileErrors.Count;

            output.Write(errorCount.ToString(CultureInfo.InvariantCulture).PadLeft(leftPaddingGoal));
            output.Write("  ");
            output.Write(PrettyPathForFileError(file, fileErrors, formatOpts));
            output.Write(formatOpts.NewLine);
        }
    }

    internal static string PrettyPathForFileError(FileLike? file, List<Diagnostic>? fileErrors, FormattingOptions formatOpts)
    {
        if (file is null || fileErrors is null || fileErrors.Count == 0)
        {
            return "";
        }

        var line = Scan.GetECMALineOfPosition(file, fileErrors[0]!.Pos());
        var fileName = file.FileName();
        if (PathIsAbsolute(fileName) && PathIsAbsolute(formatOpts.ComparePathsOptions.CurrentDirectory))
        {
            fileName = ConvertToRelativePath(file.FileName(), formatOpts.ComparePathsOptions);
        }

        return $"{fileName}{ForegroundColorEscapeGrey}:{line + 1}{ResetEscapeSequence}";
    }

    public static void WriteFormatDiagnostics(TextWriter output, Diagnostic[] diagnostics, FormattingOptions formatOpts)
    {
        foreach (var diagnostic in diagnostics)
        {
            WriteFormatDiagnostic(output, diagnostic, formatOpts);
        }
    }

    public static void WriteFormatDiagnostic(TextWriter output, Diagnostic diagnostic, FormattingOptions formatOpts)
    {
        if (diagnostic.File() is { } file)
        {
            var (line, character) = Scan.GetECMALineAndUTF16CharacterOfPosition(file, diagnostic.Pos());
            var fileName = file.FileName();
            var relativeFileName = ConvertToRelativePath(fileName, formatOpts.ComparePathsOptions);
            output.Write($"{relativeFileName}({line + 1},{(int)character + 1}): ");
        }

        output.Write($"{diagnostic.Category().Name()} {DiagnosticPrefix(diagnostic)}{diagnostic.Code()}: ");
        WriteFlattenedDiagnosticMessage(output, diagnostic, formatOpts.NewLine, formatOpts.Locale);
        output.Write(formatOpts.NewLine);
    }

    public static void FormatDiagnosticsStatusWithColorAndTime(TextWriter output, string time, Diagnostic diag, FormattingOptions formatOpts)
    {
        output.Write("[");
        WriteWithStyleAndReset(output, time, ForegroundColorEscapeGrey);
        output.Write("] ");
        WriteFlattenedDiagnosticMessage(output, diag, formatOpts.NewLine, formatOpts.Locale);
    }

    public static void FormatDiagnosticsStatusAndTime(TextWriter output, string time, Diagnostic diag, FormattingOptions formatOpts)
    {
        output.Write(time);
        output.Write(" - ");
        WriteFlattenedDiagnosticMessage(output, diag, formatOpts.NewLine, formatOpts.Locale);
    }

    public static readonly int[] ScreenStartingCodes =
    [
        Diag.Starting_compilation_in_watch_mode.Code(),
        Diag.File_change_detected_Starting_incremental_compilation.Code(),
    ];

    public static bool TryClearScreen(TextWriter output, Diagnostic diag, CompilerOptions options)
    {
        if (!options.PreserveWatchOutput.IsTrue() &&
            !options.ExtendedDiagnostics.IsTrue() &&
            !options.Diagnostics.IsTrue() &&
            Array.IndexOf(ScreenStartingCodes, diag.Code()) >= 0)
        {
            output.Write("\x1B[2J\x1B[3J\x1B[H"); // Clear screen and move cursor to home position
            return true;
        }

        return false;
    }
}
