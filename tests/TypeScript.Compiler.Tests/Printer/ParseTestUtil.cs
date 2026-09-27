// ported from: internal/testutil/parsetestutil/parsetestutil.go
//
// 与 Go 的偏离（逐条）：
//   * Go 用 `diagnosticwriter.WriteFormatDiagnostics` 把诊断格式化成 Go/TS 的编译错误文本；
//     `internal/diagnosticwriter` 尚未移植，所以 CheckDiagnostics / CheckDiagnosticsMessage 只断言
//     "没有诊断"（与 Go 的 `len(file.Diagnostics()) > 0` 完全同条件），失败信息用 Diagnostic 自身暴露的
//     成员拼出（Code / MessageText 或 MessageKey / 参数 / Pos-End）—— 比 Go 少一层文件+行列+颜色的格式化，
//     不影响断言语义。
//   * `core.IfElse(jsx, "/main.tsx", "/main.ts")` → C# 条件表达式；`tspath.Path(fileName)` → `new Path(fileName)`。
//   * Go 的 `t.Cleanup`/`t.Helper` 没有对应物（xunit 的失败堆栈已经指向调用点）。
using TypeScript.Ast;
using Xunit;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Parser.Parser;
using Path = TypeScript.TSPath.Path;

namespace TypeScript.Tests;

public static class ParseTestUtil
{
    /// <summary>Go: <c>func ParseTypeScript(text string, jsx bool) *ast.SourceFile</c>。</summary>
    public static SourceFile ParseTypeScript(string text, bool jsx)
    {
        var fileName = jsx ? "/main.tsx" : "/main.ts";
        return ParseSourceFile(
            new SourceFileParseOptions
            {
                FileName = fileName,
                Path = new Path(fileName),
            },
            text,
            GetScriptKindFromFileName(fileName));
    }

    /// <summary>Go: <c>func CheckDiagnostics(t *testing.T, file *ast.SourceFile)</c>。</summary>
    public static void CheckDiagnostics(SourceFile file) => CheckDiagnosticsMessage(file, "");

    /// <summary>Go: <c>func CheckDiagnosticsMessage(t *testing.T, file *ast.SourceFile, message string)</c>。</summary>
    public static void CheckDiagnosticsMessage(SourceFile file, string message)
    {
        var diagnostics = file.Diagnostics();
        Assert.True(diagnostics.Length == 0, message + FormatDiagnostics(diagnostics));
    }

    /// <summary>Go: <c>func MarkSyntheticRecursive(node *ast.Node)</c>。</summary>
    public static void MarkSyntheticRecursive(Node node) => NewSyntheticRecursiveVisitor().VisitNode(node);

    /// <summary>Go: <c>func newSyntheticRecursiveVisitor() *ast.NodeVisitor</c>。</summary>
    private static NodeVisitor NewSyntheticRecursiveVisitor()
    {
        NodeVisitor visitor = null!;
        visitor = NewNodeVisitor(
            node => visitor.VisitEachChild(node),
            new NodeFactory(),
            new NodeVisitorHooks
            {
                VisitNode = (node, v) =>
                {
                    if (node is not null)
                    {
                        node.Loc = UndefinedTextRange();
                    }

                    return v.VisitNode(node);
                },
                VisitToken = (node, v) =>
                {
                    if (node is not null)
                    {
                        node.Loc = UndefinedTextRange();
                    }

                    return v.VisitNode(node);
                },
                VisitNodes = (nodes, v) =>
                {
                    if (nodes is not null)
                    {
                        nodes.Loc = UndefinedTextRange();
                    }

                    return v.VisitNodes(nodes);
                },
                VisitModifiers = (nodes, v) =>
                {
                    if (nodes is not null)
                    {
                        nodes.Loc = UndefinedTextRange();
                    }

                    return v.VisitModifiers(nodes);
                },
            });
        return visitor;
    }

    /// <summary>
    /// Go: <c>diagnosticwriter.WriteFormatDiagnostics</c>（未移植）的替代品：每条诊断一行，
    /// <c>TS{code}: {messageKey} [args] ({pos}-{end})</c>。
    /// </summary>
    private static string FormatDiagnostics(Diagnostic[] diagnostics)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var diagnostic in diagnostics)
        {
            builder.Append("TS");
            builder.Append(diagnostic.Code());
            builder.Append(": ");
            // Go 打的是 diagnostics.Message.Text（+ 参数）；C# 的 MessageText() 只在外部诊断（SetExternalData）
            // 时有值，其余情况下退回到 MessageKey（等于 Go 的 message.Key）。
            builder.Append(diagnostic.MessageText() is { Length: > 0 } text ? text : diagnostic.MessageKey().ToString());
            if (diagnostic.MessageArgs() is { Length: > 0 } args)
            {
                builder.Append(" [");
                builder.Append(string.Join(", ", args));
                builder.Append(']');
            }

            builder.Append(" (");
            builder.Append(diagnostic.Pos());
            builder.Append('-');
            builder.Append(diagnostic.End());
            builder.Append(')');
            builder.Append('\n');
        }

        return builder.ToString();
    }
}
