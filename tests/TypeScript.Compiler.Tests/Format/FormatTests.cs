// ported from: internal/format/api_test.go / format_test.go / indent_test.go / indent_getindentation_test.go
//
// 与 Go 的差异：
// - `*testing.T` → xunit：`t.Run(name, …)` 的表驱动/子用例 → `[Theory]`/`[MemberData]` 或独立 `[Fact]`
//   （docs/architecture.md §5）；`assert.Assert(t, cond, msg)` → `Assert.True(cond, msg)`。
// - **`format.X` 前缀**：Go 里 `format` 是包名、`format.FormatDocument` 是包级函数；C# 里包级函数在静态类
//   `TypeScript.Format.Format`，而裸写 `Format` 会先解析到**命名空间** `TypeScript.Format`（§2 的坑），
//   所以测试用 `using static TypeScript.Format.Format;` + 裸函数名。
// - `format.WithFormatCodeSettings(t.Context(), …)` → `WithFormatCodeSettings(Context.Background, …)`
//   （C# 侧 ctx 是 `TypeScript.Core.Context`）。
// - `parser.ParseSourceFile(ast.SourceFileParseOptions{…}, text, core.ScriptKindTS)` →
//   `ParseSourceFile(new SourceFileParseOptions { FileName = …, Path = new Path(…) }, text, ScriptKind.TS)`。
// - `BenchmarkFormat` 不移植（C# 侧没有与 Go benchmark 对应的基础设施）。
// - `TestFormat`（api_test.go 的 "format checker.ts"）依赖 TSGO 的 `testdata/fixtures/compiler/checker.ts`；
//   本机 TSGO 检出缺失时**不失败**（既有语料测试如 ParserCorpusTests 也是同样处理），只跳过断言。
using System.IO;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using Xunit;
using static TypeScript.Format.Format;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Parser.Parser;
using TPath = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public class FormatTests
{
    /// <summary>Go: <c>func applyBulkEdits(text string, edits []core.TextChange) string</c>（api_test.go 的同名局部实现）。</summary>
    internal static string ApplyBulkEdits(string text, TextChange[] edits)
    {
        var b = new StringBuilder(text.Length);
        var lastEnd = 0;
        foreach (var e in edits)
        {
            var start = e.Range.Pos();
            if (start != lastEnd)
            {
                b.Append(text.AsSpan(lastEnd, start - lastEnd));
            }

            b.Append(e.NewText);

            lastEnd = e.Range.End();
        }

        b.Append(text.AsSpan(lastEnd));

        return b.ToString();
    }

    internal static SourceFile ParseUnit(string fileName, string text, ScriptKind scriptKind)
    {
        return ParseSourceFile(
            new SourceFileParseOptions { FileName = fileName, Path = new TPath(fileName) },
            text,
            scriptKind);
    }

    private static SourceFile ParseTs(string text) => ParseUnit("/test.ts", text, ScriptKind.TS);

    /// <summary>comment_test.go 里反复出现的 `InsertSpaceBeforeTypeAnnotation: core.TSTrue` + tab 设定。</summary>
    internal static FormatCodeSettings TabsSettings() => new()
    {
        TabSize = 4,
        IndentSize = 4,
        BaseIndentSize = 0,
        NewLineCharacter = "\n",
        ConvertTabsToSpaces = Tristate.TSFalse, // Use tabs
        IndentStyle = IndentStyle.Smart,
        TrimTrailingWhitespace = Tristate.TSTrue,
        InsertSpaceBeforeTypeAnnotation = Tristate.TSTrue,
    };

    // ── api_test.go ──

    [Fact]
    public void TestFormat()
    {
        var ctx = WithFormatCodeSettings(
            Context.Background,
            new FormatCodeSettings
            {
                TabSize = 4,
                IndentSize = 4,
                BaseIndentSize = 4,
                NewLineCharacter = "\n",
                ConvertTabsToSpaces = Tristate.TSTrue,
                IndentStyle = IndentStyle.Smart,
                TrimTrailingWhitespace = Tristate.TSTrue,
                InsertSpaceBeforeTypeAnnotation = Tristate.TSTrue,
            },
            "\n");

        var filePath = Path.Combine(TypeScript.Repo.Repo.TestDataPath(), "fixtures", "compiler", "checker.ts");
        if (!File.Exists(filePath))
        {
            // TSGO 检出缺失时不失败（见文件头）。
            return;
        }

        var text = File.ReadAllText(filePath);
        var sourceFile = ParseUnit("/checker.ts", text, ScriptKind.TS);
        var edits = FormatDocument(ctx, sourceFile);
        var newText = ApplyBulkEdits(text, edits);
        Assert.True(newText.Length > 0);
        Assert.True(text != newText);
    }

    // ── format_test.go ──

    public static IEnumerable<object[]> NoTrailingSpaceCases()
    {
        yield return ["simple statement without trailing newline", "1;"];
        yield return ["function call without trailing newline", "console.log('hello');"];
        yield return ["if block on single line", "if (true) { }"];
        yield return ["class declaration", "class A {\n    // Class Contents Go Here\n}"];
        yield return ["class declaration with trailing newline", "class A {\n    // Class Contents Go Here\n}\n"];
        yield return ["empty block", "if (true) {}"];
        yield return ["module declaration", "module M { }"];
        yield return ["enum declaration", "enum E { A, B }"];
    }

    [Theory]
    [MemberData(nameof(NoTrailingSpaceCases))]
    public void TestFormatNoTrailingSpace(string name, string text)
    {
        _ = name;
        var ctx = WithFormatCodeSettings(
            Context.Background,
            new FormatCodeSettings
            {
                TabSize = 4,
                IndentSize = 4,
                NewLineCharacter = "\n",
                ConvertTabsToSpaces = Tristate.TSTrue,
                IndentStyle = IndentStyle.Smart,
                TrimTrailingWhitespace = Tristate.TSTrue,
            },
            "\n");

        var sourceFile = ParseTs(text);
        var edits = FormatDocument(ctx, sourceFile);
        var newText = ApplyBulkEdits(text, edits);
        // Formatting should not add trailing whitespace at end of file
        var i = 0;
        foreach (var line in newText.Split('\n'))
        {
            i++;
            var trimmed = line.TrimEnd(' ', '\t');
            Assert.True(line == trimmed, $"Formatter should not add trailing whitespace on line {i}");
        }
    }

    // ── indent_test.go ──

    [Fact]
    public void TestGetContainingListNamedImports()
    {
        var text = """
            import type {
                AAA,
                BBB,
            } from "./bar";
            """;

        var sourceFile = ParseTs(text);

        // Find ImportSpecifier nodes (AAA and BBB)
        var importSpecifiers = new List<Node>();
        ForEachDescendantOfKind(sourceFile.AsNode(), Kind.ImportSpecifier, node => importSpecifiers.Add(node));

        Assert.True(importSpecifiers.Count == 2, $"Expected 2 import specifiers, got {importSpecifiers.Count}");

        // Test GetContainingList for each import specifier
        foreach (var specifier in importSpecifiers)
        {
            var list = GetContainingList(specifier, sourceFile);
            Assert.True(list is not null, "GetContainingList should return non-nil for import specifier");
            Assert.True(list!.Nodes!.Length == 2, $"Expected list with 2 elements, got {list.Nodes!.Length}");
        }
    }

    private static void ForEachDescendantOfKind(Node node, Kind kind, Action<Node> action)
    {
        node.ForEachChild(child =>
        {
            if (child.Kind == kind)
            {
                action(child);
            }

            ForEachDescendantOfKind(child, kind, action);
            return false;
        });
    }

    // ── indent_getindentation_test.go ──

    [Fact]
    public void TestGetIndentationForNamedImportsPosition()
    {
        var text = "import {\n    type SomeInterface,\n} from \"./exports.js\";";
        // Position 9: \n
        // Position 10: first space of "    type SomeInterface"

        var sourceFile = ParseTs(text);

        var options = GetDefaultFormatCodeSettings();

        // The line that contains "    type SomeInterface" starts at position 9 (the \n).
        // The getAdjustedStartPosition with LeadingTriviaOptionNone returns line start.
        // Let's test at position 9 (start of line containing the specifier)
        var lineStart = GetLineStartPositionForPosition(14, sourceFile); // 14 is somewhere in "    type"

        var indent = GetIndentation(lineStart, sourceFile, options, true);

        Assert.True(indent == 4, $"Expected indentation 4, got {indent}");
    }
}
