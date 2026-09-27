// ported from: internal/printer/syntheticfile.go
//
// Go 的 `(text string, positioned *ast.Node)` → C# 元组 `(string Text, Node Positioned)`（docs §3.2：多返回值）。
// `sourceFile may be nil` → C# `SourceFile?`。
// `strings.TrimSuffix(text, newLine)` → 序数 `EndsWith` + 切片（不分配 Substring 中间量）。
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using AstNodeFactory = TypeScript.Ast.NodeFactory;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    /// <summary>
    /// PrintAndPositionNode prints a synthesized node to text using the standard
    /// change-tracker printer options, trims the trailing newline, and assigns
    /// positions to the resulting node tree.
    /// sourceFile may be nil; when non-nil it is passed to the printer for comment
    /// preservation.
    /// The returned text is the printed source, and positioned is the node with
    /// concrete source positions assigned to it and all descendants.
    /// </summary>
    public static (string Text, Node Positioned) PrintAndPositionNode(AstNodeFactory factory, Node node, SourceFile? sourceFile, string newLine, int indentSize, EmitContext emitContext)
    {
        var writer = NewChangeTrackerWriter(newLine, indentSize);
        NewPrinter(
            new PrinterOptions
            {
                NewLine = GetNewLineKind(newLine),
                NeverAsciiEscape = true,
                PreserveSourceNewlines = true,
                TerminateUnterminatedLiterals = true,
            },
            writer.GetPrintHandlers(),
            emitContext).Write(node, sourceFile, writer, null);

        var text = writer.String();
        if (text.EndsWith(newLine, StringComparison.Ordinal))
        {
            text = text[..^newLine.Length];
        }
        var positioned = writer.AssignPositionsToNode(node, factory);
        return (text, positioned);
    }

    /// <summary>
    /// CreateSyntheticSourceFile wraps a positioned node in a synthetic source file
    /// suitable for use with the formatter. The node must already have valid source
    /// positions assigned (e.g. via PrintAndPositionNode or AssignPositionsToNode).
    /// </summary>
    public static SourceFile CreateSyntheticSourceFile(AstNodeFactory factory, Node node, string text, SourceFileParseOptions parseOptions)
    {
        var eof = factory.NewToken(Kind.EndOfFile);
        eof.Loc = NewTextRange(text.Length, text.Length);
        var statements = factory.NewNodeList([node]);
        statements.Loc = NewTextRange(node.Pos(), node.End());
        var syntheticFile = factory.NewSourceFile(
            parseOptions,
            text,
            statements,
            eof);
        syntheticFile.Loc = NewTextRange(0, text.Length);
        SetParentInChildren(syntheticFile);
        return syntheticFile.AsSourceFile()!;
    }
}
