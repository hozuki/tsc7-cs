// ported from: internal/testutil/emittestutil/emittestutil.go
//
// 与 Go 的偏离（逐条）：
//   * Go 的 `strings.TrimSuffix(text, "\n")`（去掉至多一个行尾 LF）→
//     `text.EndsWith('\n') ? text[..^1] : text`。
//   * Go 的 CheckDiagnosticsMessage 走 `diagnosticwriter`（未移植，见 ParseTestUtil.cs 文件头），
//     断言条件不变（reparse 后无诊断），失败信息只列每条诊断的 Code/Message/位置。
//   * `file.LanguageVariant == core.LanguageVariantJSX` → `file.LanguageVariant == LanguageVariant.JSX`。
//   * Go 的 `printer := printer.NewPrinter(...)` 遮蔽包名 → C# 里 `Printer` 类用别名 `PrinterClass`
//     （`TypeScript.Printer` 同时是命名空间，外层命名空间成员优先于 using 导入的类型，见 PrinterTests.cs 文件头）。
using TypeScript.Ast;
using TypeScript.Core;
using Xunit;
using EmitContext = TypeScript.Printer.EmitContext;
using PrintHandlers = TypeScript.Printer.PrintHandlers;
using PrinterClass = TypeScript.Printer.Printer;
using PrinterOptions = TypeScript.Printer.PrinterOptions;

namespace TypeScript.Tests;

public static class EmitTestUtil
{
    /// <summary>Go: <c>func CheckEmit(t *testing.T, emitContext *printer.EmitContext, file *ast.SourceFile, expected string)</c>。</summary>
    public static void CheckEmit(EmitContext? emitContext, SourceFile file, string expected)
    {
        var printer = PrinterClass.NewPrinter(
            new PrinterOptions
            {
                NewLine = NewLineKind.LF,
            },
            new PrintHandlers(),
            emitContext);
        var text = printer.EmitSourceFile(file);
        var actual = text.EndsWith('\n') ? text[..^1] : text;
        Assert.Equal(expected, actual);
        var file2 = ParseTestUtil.ParseTypeScript(text, file.LanguageVariant == LanguageVariant.JSX);
        ParseTestUtil.CheckDiagnosticsMessage(file2, "error on reparse: ");
    }
}
