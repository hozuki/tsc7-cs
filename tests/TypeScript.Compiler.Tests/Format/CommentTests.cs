// ported from: internal/format/comment_test.go
//
// 与 Go 的差异：
// - 每个 Go 子测试 → 一个 `[Fact]`（方法名 = 子测试名的 PascalCase 转写）；`assert.Check` → `Assert.True`
//   （gotest 的 Check 是"记失败但继续"，xunit 没有对应物，所以断言顺序与 Go 逐条对应、每条独立断言）。
// - `strings.Contains` / `strings.Index` → `string.Contains(s, StringComparison.Ordinal)` / `string.IndexOf`。
// - `len(formatted) > 0` → `formatted.Length > 0`。
// - `format.X(...)` → 裸 `X(...)`（`using static TypeScript.Format.Format;`）；类型进 `TypeScript.Format` 命名空间
//   的导入按需保留。
// - `applyBulkEdits` 复用 `FormatTests.ApplyBulkEdits`（Go 侧同在包级）。
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using Xunit;
using static TypeScript.Format.Format;
using static TypeScript.Parser.Parser;

namespace TypeScript.Tests;

public class CommentTests
{
    private static SourceFile ParseUnit(string fileName, string text, ScriptKind scriptKind)
    {
        return ParseSourceFile(
            new SourceFileParseOptions { FileName = fileName, Path = new TSPath.Path(fileName) },
            text,
            scriptKind);
    }

    private static string FormatDocumentText(string originalText, string fileName, ScriptKind scriptKind, Context ctx)
    {
        var sourceFile = ParseUnit(fileName, originalText, scriptKind);
        var edits = FormatDocument(ctx, sourceFile);
        return FormatTests.ApplyBulkEdits(originalText, edits);
    }

    [Fact]
    public void TestCommentFormattingFormatCommentIssueReproduction()
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

        // Original code that causes the bug
        var originalText = "class C {\n    /**\n     *\n    */\n    async x() {}\n}";

        // Apply formatting once
        var firstFormatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);

        // Check that the asterisk is not corrupted
        Assert.True(!firstFormatted.Contains("*/\n   /", StringComparison.Ordinal), "should not corrupt */ to /");
        Assert.True(firstFormatted.Contains("*/", StringComparison.Ordinal), "should preserve */ token");
        Assert.True(firstFormatted.Contains("async", StringComparison.Ordinal), "should preserve async keyword");

        // Apply formatting a second time to test stability
        var secondFormatted = FormatDocumentText(firstFormatted, "/test.ts", ScriptKind.TS, ctx);

        // Check that second formatting doesn't introduce corruption
        Assert.True(!secondFormatted.Contains(" sync x()", StringComparison.Ordinal), "should not corrupt async to sync");
        Assert.True(secondFormatted.Contains("async", StringComparison.Ordinal), "should preserve async keyword on second pass");
    }

    [Fact]
    public void TestCommentFormattingFormatJsDocWithTabIndentation()
    {
        var ctx = WithFormatCodeSettings(Context.Background, FormatTests.TabsSettings(), "\n");

        // Original code with tab indentation (tabs represented as \t)
        var originalText = "class Foo {\n\t/**\n\t * @param {string} argument - This is a param description.\n\t */\n\texample(argument) {\nconsole.log(argument);\n\t}\n}";

        var formatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);

        // Check that tabs come before spaces (not spaces before tabs)
        // The comment lines should have format: tab followed by space and asterisk
        // NOT: space followed by tab and asterisk
        Assert.True(!formatted.Contains(" \t*", StringComparison.Ordinal), "should not have space before tab before asterisk");
        Assert.True(formatted.Contains("\t *", StringComparison.Ordinal), "should have tab before space before asterisk");

        // Verify console.log is properly indented with tabs
        Assert.True(formatted.Contains("\t\tconsole.log", StringComparison.Ordinal), "console.log should be indented with two tabs");
    }

    [Fact]
    public void TestCommentFormattingFormatCommentInsideMultiLineArgumentList()
    {
        var ctx = WithFormatCodeSettings(Context.Background, FormatTests.TabsSettings(), "\n");

        // Original code with proper indentation
        var originalText = "console.log(\n\t\"a\",\n\t// the second arg\n\t\"b\"\n);";

        var formatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);

        // The comment should remain indented with a tab
        Assert.True(formatted.Contains("\t// the second arg", StringComparison.Ordinal), "comment should be indented with tab");
        // The comment should not lose its indentation
        Assert.True(!formatted.Contains("\n// the second arg", StringComparison.Ordinal), "comment should not lose indentation");
    }

    [Fact]
    public void TestCommentFormattingFormatCommentInChainedMethodCalls()
    {
        var ctx = WithFormatCodeSettings(Context.Background, FormatTests.TabsSettings(), "\n");

        // Original code with proper indentation
        var originalText = "foo\n\t.bar()\n\t// A second call\n\t.baz();";

        var formatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);

        // The comment should remain indented
        Assert.True(
            formatted.Contains("\t// A second call", StringComparison.Ordinal) || formatted.Contains("   // A second call", StringComparison.Ordinal),
            "comment should be indented");
        // The comment should not lose its indentation
        Assert.True(!formatted.Contains("\n// A second call", StringComparison.Ordinal), "comment should not lose indentation");
    }

    // Regression test for issue #1928 - panic when formatting chained method call with comment
    [Fact]
    public void TestCommentFormattingFormatChainedMethodCallWithCommentIssue1928()
    {
        var ctx = WithFormatCodeSettings(Context.Background, FormatTests.TabsSettings(), "\n");

        // This code previously caused a panic with "strings: negative Repeat count"
        // because tokenIndentation was -1 and was being used directly for indentation
        var originalText = "foo\n\t.bar()\n\t// A second call\n\t.baz();";

        // Apply formatting - should not panic
        var formatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);

        // Verify the comment maintains proper indentation and doesn't lose it
        Assert.True(
            formatted.Contains("\t// A second call", StringComparison.Ordinal) || formatted.Contains("   // A second call", StringComparison.Ordinal),
            "comment should be indented");
        Assert.True(!formatted.Contains("\n// A second call", StringComparison.Ordinal), "comment should not be at column 0");
    }

    [Fact]
    public void TestCommentFormattingMultilineCommentInsideBlockThatOpensOnFirstLineIssue2649()
    {
        var ctx = WithFormatCodeSettings(
            Context.Background,
            new FormatCodeSettings
            {
                TabSize = 4,
                IndentSize = 4,
                BaseIndentSize = 0,
                NewLineCharacter = "\n",
                ConvertTabsToSpaces = Tristate.TSFalse,
                IndentStyle = IndentStyle.Smart,
                TrimTrailingWhitespace = Tristate.TSTrue,
            },
            "\n");

        var originalText = "document.addEventListener('DOMContentLoaded', () => {\n    /** @type {NodeListOf<HTMLSpanElement>} */\n    const elements = document.querySelectorAll('.test')\n});";

        var formatted = FormatDocumentText(originalText, "/test.js", ScriptKind.JS, ctx);
        Assert.True(formatted.Length > 0, "formatted text should not be empty");
    }

    [Fact]
    public void TestCommentFormattingSingleLineCommentInsideBlockThatOpensOnFirstLineIssue2649()
    {
        var ctx = WithFormatCodeSettings(
            Context.Background,
            new FormatCodeSettings
            {
                TabSize = 4,
                IndentSize = 4,
                BaseIndentSize = 0,
                NewLineCharacter = "\n",
                ConvertTabsToSpaces = Tristate.TSFalse,
                IndentStyle = IndentStyle.Smart,
                TrimTrailingWhitespace = Tristate.TSTrue,
            },
            "\n");

        var originalText = "document.addEventListener('DOMContentLoaded', () => {\n    // a comment\n    const x = 1\n});";

        var formatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);
        Assert.True(formatted.Length > 0, "formatted text should not be empty");
    }

    private static Context SelectionContext() => WithFormatCodeSettings(
        Context.Background,
        new FormatCodeSettings
        {
            TabSize = 4,
            IndentSize = 4,
            BaseIndentSize = 0,
            NewLineCharacter = "\n",
            ConvertTabsToSpaces = Tristate.TSTrue,
            IndentStyle = IndentStyle.Smart,
            TrimTrailingWhitespace = Tristate.TSTrue,
        },
        "\n");

    [Fact]
    public void TestFormatSelectionPreservesCommentsEndsInsideComment()
    {
        var ctx = SelectionContext();

        // Reproduce: const test/* comment */=5;
        // When selecting a range that ends inside the comment (before */), format selection should not delete the comment.
        var originalText = "const test/* comment */=5;";

        var sourceFile = ParseUnit("/test.ts", originalText, ScriptKind.TS);

        // Select a range that starts at the beginning of the line and ends inside the block comment.
        // This covers `const test/* comment`, stopping before the closing `*/`.
        var commentStart = originalText.IndexOf("/*", StringComparison.Ordinal);
        var selectionEnd = commentStart + "/* comment".Length; // ends inside the comment, before the closing `*/`

        var edits = FormatSelection(ctx, sourceFile, 0, selectionEnd);
        var formatted = FormatTests.ApplyBulkEdits(originalText, edits);

        // The entire statement should be preserved unchanged
        Assert.Equal(originalText, formatted);
    }

    [Fact]
    public void TestFormatSelectionPreservesCommentsStartsInsideComment()
    {
        var ctx = SelectionContext();

        var originalText = "const test/* comment */=5;";

        var sourceFile = ParseUnit("/test.ts", originalText, ScriptKind.TS);

        // Select from inside the comment to the end
        var commentStart = originalText.IndexOf("/*", StringComparison.Ordinal);
        var selectionStart = commentStart + 3; // inside the comment

        var edits = FormatSelection(ctx, sourceFile, selectionStart, originalText.Length);
        var formatted = FormatTests.ApplyBulkEdits(originalText, edits);

        // The entire statement should be preserved unchanged
        Assert.Equal(originalText, formatted);
    }

    [Fact]
    public void TestFormatSelectionPreservesCommentsFullDocumentFormatAddsSpaces()
    {
        var ctx = WithFormatCodeSettings(
            Context.Background,
            new FormatCodeSettings
            {
                TabSize = 4,
                IndentSize = 4,
                BaseIndentSize = 0,
                NewLineCharacter = "\n",
                ConvertTabsToSpaces = Tristate.TSTrue,
                IndentStyle = IndentStyle.Smart,
                TrimTrailingWhitespace = Tristate.TSTrue,
                InsertSpaceBeforeAndAfterBinaryOperators = Tristate.TSTrue,
            },
            "\n");

        var originalText = "const test/* comment */=5;";

        var formatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);

        // Full document format should preserve the comment and add spaces around `=`
        Assert.Equal("const test/* comment */ = 5;", formatted);
    }

    [Fact]
    public void TestSliceBoundsPanicFormatCodeWithTrailingSemicolonShouldNotPanic()
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

        // Code from the issue that causes slice bounds panic
        var originalText = "const _enableDisposeWithListenerWarning = false\n\t// || Boolean(\"TRUE\") // causes a linter warning so that it cannot be pushed\n\t;\n";

        // This should not panic
        var formatted = FormatDocumentText(originalText, "/test.ts", ScriptKind.TS, ctx);

        // Basic sanity checks
        Assert.True(formatted.Length > 0, "formatted text should not be empty");
        Assert.True(formatted.Contains("_enableDisposeWithListenerWarning", StringComparison.Ordinal), "should preserve variable name");
    }
}
