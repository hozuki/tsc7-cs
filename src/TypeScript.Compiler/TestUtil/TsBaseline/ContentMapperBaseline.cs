// ported from: internal/testutil/tsbaseline/contentmapper_baseline.go
//
// 与 Go 的差异（逐条）：
// 1) `*testing.T` → `IHarnessTest`（本文件只透传给 baseline.Run）。
// 2) `program.Program()`（ProgramLike → *Program）→ 显式向下转型（见 JsEmitBaseline.cs 的 `ProgramOf`）。
// 3) `fmt.Fprintf(..., "%s", file.ScriptKind)` → `ScriptKind.String()`（Go 的 stringer；见 Core/ScriptKind.cs 文件头）。
// 4) `fmt.Fprintf(..., "%v", mapper.Definition.Extensions)`：Go 的 `%v` 打印 `[]string` 是 `[a b c]` 形式
//    （nil/空 → `[]`）。C# 没有等价格式，这里逐字复刻：`"[" + string.Join(" ", exts) + "]"`。
// 5) 本文件的换行一律 `\n`（`_contentMapperFormatOpts.NewLine = "\n"`），与 error/js baseline 的 CRLF 不同 —— 照抄。
// 6) `program.GetSourceFiles()` 里 Go 用 `for _, file := range`（切片，非 nil）；C# 侧同序。

namespace TypeScript.TestUtil.TsBaseline;

using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Ast;
using TypeScript.Compiler;
using TypeScript.ContentMapper;
using TypeScript.Core;
using TypeScript.TestUtil.Baseline;
using Diagnostic = TypeScript.Ast.Diagnostic;
using DW = TypeScript.DiagnosticWriter;
using static TypeScript.DiagnosticWriter.DiagnosticWriter;

public static partial class TsBaseline
{
    private static readonly Regex _ansiEscape = new("\u001b\\[[0-9;]*m", RegexOptions.Compiled);

    private static readonly DW.FormattingOptions _contentMapperFormatOpts = new()
    {
        NewLine = "\n",
    };

    /// <summary>Go: <c>func DoContentMapperBaseline(t *testing.T, baselinePath string, program compiler.ProgramLike, diagnostics []*ast.Diagnostic, opts baseline.Options)</c>。</summary>
    public static void DoContentMapperBaseline(
        IHarnessTest t,
        string baselinePath,
        ProgramLike program,
        Diagnostic[] diagnostics,
        BaselineOptions opts)
    {
        var content = GetContentMapperBaseline(program, diagnostics);
        if (content == "")
        {
            return;
        }

        Baseline.Run(t, _tsExtension.Replace(baselinePath, ".contentmapper"), content, opts);
    }

    /// <summary>Go: <c>func getContentMapperBaseline(program compiler.ProgramLike, diagnostics []*ast.Diagnostic) string</c>。</summary>
    private static string GetContentMapperBaseline(ProgramLike program, Diagnostic[] diagnostics)
    {
        var prog = ProgramOf(program);
        var mapped = new Dictionary<string, Mapper>();
        var files = new List<SourceFile>();
        foreach (var file in program.GetSourceFiles())
        {
            if (prog.GetContentMapper(file) is { } mapper)
            {
                mapped[file.FileName()] = mapper;
                files.Add(file);
            }
        }

        if (files.Count == 0)
        {
            return "";
        }

        var b = new StringBuilder();
        foreach (var file in files)
        {
            var mapper = mapped[file.FileName()];
            b.Append($"//// [{RemoveTestPathPrefixes(file.FileName(), false)}] (ScriptKind: {file.ScriptKind.String()}, ContentMapper: {FormatGoSlice(mapper.Extensions)})\n");
            b.Append("--- Original ---\n");
            b.Append(EnsureTrailingNewline(file.OriginalText()));
            b.Append("--- Transformed ---\n");
            b.Append(EnsureTrailingNewline(file.Text()));
            b.Append('\n');
        }

        var fileDiagnostics = new List<Diagnostic>();
        foreach (var d in diagnostics ?? [])
        {
            if (d.File() is { } dFile && mapped.ContainsKey(dFile.FileName()))
            {
                fileDiagnostics.Add(d);
            }
        }

        b.Append("=== Diagnostics ===\n\n");
        if (fileDiagnostics.Count == 0)
        {
            b.Append(Baseline.NoContent + "\n");
            return b.ToString();
        }

        var rendered = new StringWriter();
        FormatDiagnosticsWithColorAndContext(
            rendered,
            ToDiagnostics(WrapASTDiagnostics([.. fileDiagnostics])),
            _contentMapperFormatOpts);
        b.Append(RemoveTestPathPrefixes(_ansiEscape.Replace(rendered.ToString(), ""), false));
        return b.ToString();
    }

    /// <summary>Go: <c>func ensureTrailingNewline(s string) string</c>。</summary>
    private static string EnsureTrailingNewline(string s)
    {
        if (s == "" || s.EndsWith('\n'))
        {
            return s;
        }

        return s + "\n";
    }

    /// <summary>Go 的 <c>fmt "%v"</c> 对 <c>[]string</c> 的输出（见文件头第 4 条）。</summary>
    private static string FormatGoSlice(string[]? values)
    {
        return "[" + string.Join(" ", values ?? []) + "]";
    }
}
