// ported from: internal/testutil/parsetestutil/parsetestutil.go
//
// 与 Go 的差异（逐条）：
// 1) `*testing.T` → `IHarnessTest`（`t.Error`）。
// 2) `core.GetScriptKindFromFileName` → `TypeScript.Core.Core.GetScriptKindFromFileName`（`using static Core`）。
// 3) `tspath.Path(fileName)` → `(Path)fileName`（Path 的显式转换运算符，见 TSPath/Path.cs）。
// 4) `ast.NodeVisitorHooks{...}` 的四个钩子是 Go 的内联闭包 → C# 的具名委托（`NodeVisitorVisitNode` 等），
//    形参可空性按仓库既有写法（DeepClone.cs）在闭包内判 null（Go 同样判 nil）。
// 5) 自引用闭包（visitor 的 visit 回调调用 visitor 自己的 VisitEachChild）→ C# 用可空局部变量 + `!`。
// 6) `newSyntheticRecursiveVisitor` 是未导出函数 → C# `private static`（名字 PascalCase）。
// 7) `MarkSyntheticRecursive` 的 `Node` 参数：Go 传 nil 会 panic（VisitNode(nil) 里解引用），C# 声明为非空。

namespace TypeScript.TestUtil.ParseTestUtil;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.DiagnosticWriter;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.DiagnosticWriter.DiagnosticWriter;
using Path = TypeScript.TSPath.Path;

/// <summary>Go: <c>package parsetestutil</c>。</summary>
public static class ParseTestUtil
{
    /// <summary>Simplifies parsing an input string into a SourceFile for testing purposes.</summary>
    public static SourceFile ParseTypeScript(string text, bool jsx)
    {
        var fileName = IfElse(jsx, "/main.tsx", "/main.ts");
        var file = TypeScript.Parser.Parser.ParseSourceFile(
            new SourceFileParseOptions
            {
                FileName = fileName,
                Path = (Path)fileName,
            },
            text,
            GetScriptKindFromFileName(fileName));
        return file;
    }

    /// <summary>Asserts that the given file has no parse diagnostics.</summary>
    public static void CheckDiagnostics(IHarnessTest t, SourceFile file)
    {
        if (file.Diagnostics().Length > 0)
        {
            var b = new StringWriter();
            WriteFormatDiagnostics(
                b,
                FromASTDiagnostics(file.Diagnostics()),
                new FormattingOptions
                {
                    NewLine = "\n",
                });
            t.Error(b.ToString());
        }
    }

    /// <summary>Asserts that the given file has no parse diagnostics and asserts the given message.</summary>
    public static void CheckDiagnosticsMessage(IHarnessTest t, SourceFile file, string message)
    {
        if (file.Diagnostics().Length > 0)
        {
            var b = new StringWriter();
            WriteFormatDiagnostics(
                b,
                FromASTDiagnostics(file.Diagnostics()),
                new FormattingOptions
                {
                    NewLine = "\n",
                });
            t.Error(message + b.ToString());
        }
    }

    /// <summary>Go: <c>func newSyntheticRecursiveVisitor() *ast.NodeVisitor</c>。</summary>
    private static NodeVisitor NewSyntheticRecursiveVisitor()
    {
        NodeVisitor? v = null;
        v = NewNodeVisitor(
            node => v!.VisitEachChild(node),
            new NodeFactory(),
            new NodeVisitorHooks
            {
                VisitNode = (node, vv) =>
                {
                    if (node is not null)
                    {
                        node.Loc = UndefinedTextRange();
                    }

                    return vv.VisitNode(node);
                },
                VisitToken = (node, vv) =>
                {
                    if (node is not null)
                    {
                        node.Loc = UndefinedTextRange();
                    }

                    return vv.VisitNode(node);
                },
                VisitNodes = (nodes, vv) =>
                {
                    if (nodes is not null)
                    {
                        nodes.Loc = UndefinedTextRange();
                    }

                    return vv.VisitNodes(nodes);
                },
                VisitModifiers = (nodes, vv) =>
                {
                    if (nodes is not null)
                    {
                        nodes.Loc = UndefinedTextRange();
                    }

                    return vv.VisitModifiers(nodes);
                },
            });
        return v;
    }

    /// <summary>Sets the Loc of the given node and every Node in its subtree to an undefined TextRange (-1,-1).</summary>
    public static void MarkSyntheticRecursive(Node node)
    {
        NewSyntheticRecursiveVisitor().VisitNode(node);
    }
}
