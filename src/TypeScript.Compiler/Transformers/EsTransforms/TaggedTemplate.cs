// ported from: internal/transformers/estransforms/taggedtemplate.go（175 行，全部：newlineNormalizer、
//               taggedTemplateTransformer + newTaggedTemplateLiftRestrictionTransformer、visit、visitSourceFile、
//               visitTaggedTemplateExpression、processTaggedTemplateExpression、createTemplateCooked、getRawLiteral、
//               hasInvalidEscape）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type taggedTemplateTransformer struct { transformers.Transformer; … }`（嵌入）→ C# 的
//    `internal sealed class TaggedTemplateTransformer : Transformer`（继承，见 Transformers/Transformer.cs 契约）；
//    未导出字段 → `_camelCase`（`currentSourceFile` → `_currentSourceFile`、
//    `taggedTemplateStringDeclarations` → `_taggedTemplateStringDeclarations`）。
// 2) Go 的包级 `var newlineNormalizer = strings.NewReplacer("\r\n", "\n", "\r", "\n")` → `NormalizeNewlines(string)`
//    里的链式 `Replace`：`strings.Replacer` 单趟替换的语义在这里与"先 CRLF→LF、再把孤立 CR→LF"完全等价
//    （第一趟产出的 L 不会被第二趟再扫到），且不需要为一个静态表引入类型。
// 3) Go 的包级函数 `createTemplateCooked`/`getRawLiteral`/`hasInvalidEscape`/`newTaggedTemplateLiftRestrictionTransformer`
//    → `EsTransforms` 的 `internal static` 成员（docs/architecture.md §2）。
// 4) `scanner.GetSourceTextOfNodeFromSourceFile(ast.GetSourceFileOfNode(node), node, false)` → 裸名调用
//    （`using static TypeScript.Scanner.Scanner;`，与 Printer/Binder 的调用点写法一致）。
// 5) `text[1 : len(text)-endLen]` → `text[1..^endLen]`：`endLen` 只可能是 1 或 2，且被切掉的是纯 ASCII
//    （"`" 或 "${"）→ UTF-16 代码单元下标与 Go 的字节下标指向同一个位置（docs/architecture.md §3.1）。
// 6) Go 的 `append(slices.Clip(visitedSourceFile.Statements.Nodes), …)` → C# `List<Node>` 收集（§3.2：
//    Go 的 append 语义在 C# 里用 List 表达）；`len(taggedTemplateStringDeclarations) > 0` → `is { Count: > 0 }`。
// 7) Go 的 `append(...)` 追加到 nil 切片 → C# 用可空 `List<Node>` 并在 visitSourceFile 开头置回 null（与 Go 的
//    `tx.taggedTemplateStringDeclarations = nil` 一致）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Scanner.Scanner;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.EsTransforms;

internal sealed class TaggedTemplateTransformer : Transformer
{
    private SourceFile? _currentSourceFile;

    private List<Node>? _taggedTemplateStringDeclarations;

    /// <summary>Go: <c>func (tx *taggedTemplateTransformer) visit(node *ast.Node) *ast.Node</c>。</summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsInvalidTemplateEscape) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.SourceFile:
                return VisitSourceFile(node.AsSourceFile()!);
            case Kind.TaggedTemplateExpression:
                return VisitTaggedTemplateExpression(node.AsTaggedTemplateExpression()!);
            default:
                return Visitor().VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (tx *taggedTemplateTransformer) visitSourceFile(node *ast.SourceFile) *ast.Node</c>。</summary>
    private Node VisitSourceFile(SourceFile node)
    {
        _currentSourceFile = node;
        _taggedTemplateStringDeclarations = null;
        var visited = Visitor().VisitEachChild(node)!;

        if (_taggedTemplateStringDeclarations is { Count: > 0 })
        {
            var visitedSourceFile = visited.AsSourceFile()!;
            var statements = new List<Node>(visitedSourceFile.Statements!.Nodes ?? [])
            {
                Factory().NewVariableStatement(
                    null, /*modifiers*/
                    Factory().NewVariableDeclarationList(
                        Factory().NewNodeList(_taggedTemplateStringDeclarations.ToArray()),
                        NodeFlags.None)),
            };
            var stmtList = Factory().NewNodeList(statements.ToArray());
            stmtList.Loc = node.Statements!.Loc;
            visited = Factory().UpdateSourceFile(visitedSourceFile, stmtList, visitedSourceFile.EndOfFileToken);
        }

        EmitContext().AddEmitHelper(visited, EmitContext().ReadEmitHelpers());
        return visited;
    }

    /// <summary>Go: <c>func (tx *taggedTemplateTransformer) visitTaggedTemplateExpression(node *ast.TaggedTemplateExpression) *ast.Node</c>。</summary>
    private Node? VisitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        return ProcessTaggedTemplateExpression(node);
    }

    /// <summary>Go: <c>func (tx *taggedTemplateTransformer) processTaggedTemplateExpression(node *ast.TaggedTemplateExpression) *ast.Node</c>。</summary>
    private Node? ProcessTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        var tag = Visitor().VisitNode(node.Tag);
        var template = node.Template;

        if (!HasInvalidEscape(template!))
        {
            return Visitor().VisitEachChild(node);
        }

        var f = Factory();

        // Build up the template arguments and the raw and cooked strings for the template.
        var templateArguments = new List<Node> { null! }; // placeholder for the template object
        var cookedStrings = new List<Node>();
        var rawStrings = new List<Node>();

        if (IsNoSubstitutionTemplateLiteral(template!))
        {
            cookedStrings.Add(CreateTemplateCooked(f, template!.TemplateLiteralLikeData()!));
            rawStrings.Add(GetRawLiteral(f, template!));
        }
        else
        {
            var te = template!.AsTemplateExpression()!;
            cookedStrings.Add(CreateTemplateCooked(f, te.Head!.TemplateLiteralLikeData()!));
            rawStrings.Add(GetRawLiteral(f, te.Head!));
            foreach (var span in te.TemplateSpans!.Nodes ?? [])
            {
                var ts = span.AsTemplateSpan()!;
                cookedStrings.Add(CreateTemplateCooked(f, ts.Literal!.TemplateLiteralLikeData()!));
                rawStrings.Add(GetRawLiteral(f, ts.Literal!));
                templateArguments.Add(Visitor().VisitNode(ts.Expression)!);
            }
        }

        var helperCall = f.NewTemplateObjectHelper(
            f.NewArrayLiteralExpression(f.NewNodeList(cookedStrings.ToArray()), false),
            f.NewArrayLiteralExpression(f.NewNodeList(rawStrings.ToArray()), false));

        // Create a variable to cache the template object if we're in a module.
        // Do not do this in the global scope, as any variable we currently generate could conflict with
        // variables from outside of the current compilation. In the future, we can revisit this behavior.
        if (IsExternalModule(_currentSourceFile!))
        {
            var tempVar = f.NewUniqueName("templateObject");
            _taggedTemplateStringDeclarations ??= [];
            _taggedTemplateStringDeclarations.Add(
                f.NewVariableDeclaration(tempVar, null, null, null));
            templateArguments[0] = f.NewLogicalORExpression(
                tempVar,
                f.NewAssignmentExpression(tempVar, helperCall));
        }
        else
        {
            templateArguments[0] = helperCall;
        }

        var call = f.NewCallExpression(tag, null /*questionDotToken*/, null /*typeArguments*/, f.NewNodeList(templateArguments.ToArray()), NodeFlags.None);
        call.Loc = node.Loc;
        return call;
    }
}

public static partial class EsTransforms
{
    /// <summary>Go: <c>var newlineNormalizer = strings.NewReplacer("\r\n", "\n", "\r", "\n")</c>（见本文件头第 2 条）。</summary>
    private static string NormalizeNewlines(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>Go: <c>func createTemplateCooked(f *printer.NodeFactory, template *ast.TemplateLiteralLikeNodeBase) *ast.Node</c>。</summary>
    internal static Node CreateTemplateCooked(NodeFactory f, ITemplateLiteralLikeNodeBase template)
    {
        if ((template.TemplateFlags & TokenFlags.IsInvalid) != 0)
        {
            return f.NewVoidZeroExpression();
        }

        return f.NewStringLiteral(template.Text, TokenFlags.None);
    }

    /// <summary>Go: <c>func getRawLiteral(f *printer.NodeFactory, node *ast.Node) *ast.Node</c>。</summary>
    internal static Node GetRawLiteral(NodeFactory f, Node node)
    {
        var text = node.TemplateLiteralLikeData()!.RawText;
        if (text.Length == 0)
        {
            text = GetSourceTextOfNodeFromSourceFile(GetSourceFileOfNode(node)!, node, false /*includeTrivia*/);
            // text contains the original source, it will also contain quotes ("`"), dollar signs and braces ("${" and "}"),
            // thus we need to remove those characters.
            // First template piece starts with "`", others with "}"
            // Last template piece ends with "`", others with "${"
            var isLast = node.Kind == Kind.NoSubstitutionTemplateLiteral || node.Kind == Kind.TemplateTail;
            var endLen = 2;
            if (isLast)
            {
                endLen = 1;
            }

            text = text[1..^endLen];
        }

        // Newline normalization:
        // ES6 Spec 11.8.6.1 - Static Semantics of TV's and TRV's
        // <CR><LF> and <CR> LineTerminatorSequences are normalized to <LF> for both TV and TRV.
        text = NormalizeNewlines(text);

        var result = f.NewStringLiteral(text, TokenFlags.None);
        result.Loc = node.Loc;
        return result;
    }

    /// <summary>Go: <c>func hasInvalidEscape(template *ast.Node) bool</c>。</summary>
    internal static bool HasInvalidEscape(Node template)
    {
        if (IsNoSubstitutionTemplateLiteral(template))
        {
            return (template.TemplateLiteralLikeData()!.TemplateFlags & TokenFlags.ContainsInvalidEscape) != 0;
        }

        var te = template.AsTemplateExpression()!;
        if ((te.Head!.TemplateLiteralLikeData()!.TemplateFlags & TokenFlags.ContainsInvalidEscape) != 0)
        {
            return true;
        }

        foreach (var span in te.TemplateSpans!.Nodes ?? [])
        {
            if ((span.AsTemplateSpan()!.Literal!.TemplateLiteralLikeData()!.TemplateFlags & TokenFlags.ContainsInvalidEscape) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func newTaggedTemplateLiftRestrictionTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>。</summary>
    internal static Transformer? NewTaggedTemplateLiftRestrictionTransformer(TransformOptions opts)
    {
        var tx = new TaggedTemplateTransformer();
        return tx.NewTransformer(tx.Visit, opts.Context);
    }
}
