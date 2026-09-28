// ported from: internal/ls/selectionranges.go:1-399
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级常量 `maxSelectionRangeDepth` → `Ls.MaxSelectionRangeDepth`（`internal const`，PascalCase）；包级类型
//    `selectionRangeBuilder` → 本文件的 `internal sealed class SelectionRangeBuilder`。
//    包级未导出函数（`getSelectionChildren`/`groupChildren`/`splitChildren`/`createSyntaxList`/`getSmartSelectionRange`）
//    → `Ls` 静态类的 `internal static` 成员（PascalCase，方法不加 `_`）。
// 2) `*LanguageService` 方法 `ProvideSelectionRanges` → partial 类成员（Go 导出 ⇒ `public`）；多返回值 →
//    元组 `(SelectionRangesOrNull Response, Exception? Error)`（`SelectionRangeResponse = SelectionRangesOrNull`）。
// 3) Go 的 `newSelectionRangeBuilder(capacity)` 用 `make([]Range, 0, capacity)` + append；C# 的数组没有"len/cap"之分
//    ⇒ 显式保留 `_length`（= Go 的 len，触顶后恒为容量）与 `_oldestIndex`，`Push`/`Build` 与 Go 逐行同构。
// 4) Go 的 `lastRange lsproto.Range` 零值 + `==` 结构比较：C# 的 `Range` 是 class（无 `==` 重载）⇒ 用
//    `CompareRanges(a, b) == 0`（Range 只有 Start/End 两个字段，等价于 Go 的结构相等）。
//    注意 `CompareRanges` 在 `LsProto` 静态类里，而 `LsProto` 同时是命名空间 ⇒ 必须 `using static TypeScript.LsProto.LsProto;`
//    后裸名调用（写 `LsProto.CompareRanges(...)` 会先绑到命名空间 → CS0234；先例 LsChange/Tracker.cs:36）。
// 5) Go 的 `children[i:j]` 切片 → C# 的数组范围索引 `children[i..j]`；nil 切片 → `?? []`（Go 遍历 nil 不迭代）。
// 6) `ast.IsTemplateHead` / `ast.IsTemplateTail` 与 `ls` 包的同名成员（utilities.go）**同名**（artifacts/ls-g1-helpers.md §4.10）
//    ⇒ 本文件**不用** `using static TypeScript.Ls.Ls;`（于是 Ast 侧保持裸名），而 `Ls` 类体内的这次调用显式写
//    `TypeScript.Ast.Ast.IsTemplateHead/Tail(...)` 以对齐 Go 的 `ast.` 调用（`Ast` 是命名空间，不能做限定名，见 §4.1）。
// 7) `node.AsMappedTypeNode()` 等 C# 返回可空（`this as T`）⇒ 按 Go 类型断言成功的语义补 `!`；`astnav.GetTouchingPropertyName`
//    在 C# 声明为非空返回，Go 可能返回 nil ⇒ 用 `Node?` 接收后照旧判空。
// 8) `sourceFile.ContentMapper() == ""`（C# 返回 `""` 而不是 nil，见 Ast/SourceFile.cs:251）。
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Scanner.Scanner;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>Go: <c>const maxSelectionRangeDepth = 1000</c>（selectionranges.go:17）。</summary>
public static partial class Ls
{
    internal const int MaxSelectionRangeDepth = 1000;
}

/// <summary>
/// Go: <c>type selectionRangeBuilder struct { ranges []lsproto.Range; oldestIndex int }</c>（selectionranges.go:19-22）。
/// Go 用 `make([]Range, 0, capacity)` 的切片，触顶后按环形覆写；C# 用定长数组 + <c>_length</c> 表达同一个 len/cap 语义。
/// </summary>
internal sealed class SelectionRangeBuilder
{
    internal Range[] _ranges;
    internal int _oldestIndex;

    /// <summary>Go 的 <c>len(b.ranges)</c>：初期随 append 增长，达到容量后恒等于容量。</summary>
    internal int _length;

    internal SelectionRangeBuilder(int capacity)
    {
        _ranges = new Range[capacity];
    }

    /// <summary>Go: <c>func (b *selectionRangeBuilder) push(selectionRange lsproto.Range)</c>（selectionranges.go:27-35）。</summary>
    internal void Push(Range selectionRange)
    {
        if (_length < _ranges.Length)
        {
            _ranges[_length] = selectionRange;
            _length++;
            return;
        }

        _ranges[_oldestIndex] = selectionRange;
        _oldestIndex = (_oldestIndex + 1) % _length;
    }

    /// <summary>Go: <c>func (b *selectionRangeBuilder) build(result *lsproto.SelectionRange) *lsproto.SelectionRange</c>（selectionranges.go:37-46）。</summary>
    internal SelectionRange? Build(SelectionRange? result)
    {
        for (var i = 0; i < _length; i++)
        {
            var index = (_oldestIndex + i) % _length;
            result = new SelectionRange
            {
                Range = _ranges[index],
                Parent = result,
            };
        }

        return result;
    }
}

public static partial class Ls
{
    /// <summary>Go: <c>func getSelectionChildren(factory *ast.NodeFactory, node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（selectionranges.go:57-107）。</summary>
    internal static Node[] GetSelectionChildren(NodeFactory factory, Node node, SourceFile sourceFile)
    {
        if (!IsMappedTypeNode(node))
        {
            return GetChildrenFromNonJSDocNode(node, sourceFile) ?? [];
        }

        var children = GetChildrenFromNonJSDocNode(node, sourceFile) ?? [];
        if (children.Length < 2)
        {
            return children;
        }

        var openBraceToken = children[0];
        var closeBraceToken = children[^1];
        if (openBraceToken.Kind != Kind.OpenBraceToken || closeBraceToken.Kind != Kind.CloseBraceToken)
        {
            return children;
        }

        var mappedType = node.AsMappedTypeNode()!;
        children = children[1..^1];

        // Group `-/+readonly` and `-/+?`.
        var groupedWithPlusMinusTokens = GroupChildren(factory, children, child =>
            ReferenceEquals(child, mappedType.ReadonlyToken) ||
            child.Kind == Kind.ReadonlyKeyword ||
            ReferenceEquals(child, mappedType.QuestionToken) ||
            child.Kind == Kind.QuestionToken);

        // Group the type parameter with its surrounding brackets.
        var groupedWithBrackets = GroupChildren(factory, groupedWithPlusMinusTokens, child =>
            child.Kind == Kind.OpenBracketToken ||
            child.Kind == Kind.TypeParameter ||
            child.Kind == Kind.CloseBracketToken);

        // Go exposes the trailing semicolon directly, so keep it in the right-hand
        // group to produce the same effective selection tree as Strada.
        return
        [
            openBraceToken,
            CreateSyntaxList(factory, SplitChildren(factory, groupedWithBrackets, child => child.Kind == Kind.ColonToken, false)),
            closeBraceToken,
        ];
    }

    /// <summary>Go: <c>func groupChildren(factory *ast.NodeFactory, children []*ast.Node, groupOn func(*ast.Node) bool) []*ast.Node</c>（selectionranges.go:109-126）。</summary>
    internal static Node[] GroupChildren(NodeFactory factory, Node[] children, Func<Node, bool> groupOn)
    {
        List<Node> result = [];
        List<Node> group = [];
        foreach (var child in children)
        {
            if (groupOn(child))
            {
                group.Add(child);
            }
            else
            {
                if (group.Count > 0)
                {
                    result.Add(CreateSyntaxList(factory, [.. group]));
                    group.Clear();
                }

                result.Add(child);
            }
        }

        if (group.Count > 0)
        {
            result.Add(CreateSyntaxList(factory, [.. group]));
        }

        return [.. result];
    }

    /// <summary>Go: <c>func splitChildren(factory, children, pivotOn, separateTrailingSemicolon) []*ast.Node</c>（selectionranges.go:128-167）。</summary>
    internal static Node[] SplitChildren(
        NodeFactory factory,
        Node[] children,
        Func<Node, bool> pivotOn,
        bool separateTrailingSemicolon)
    {
        if (children.Length < 2)
        {
            return children;
        }

        var splitTokenIndex = -1;
        for (var i = 0; i < children.Length; i++)
        {
            if (pivotOn(children[i]))
            {
                splitTokenIndex = i;
                break;
            }
        }

        if (splitTokenIndex == -1)
        {
            return children;
        }

        var leftChildren = children[..splitTokenIndex];
        var splitToken = children[splitTokenIndex];
        var lastToken = children[^1];
        var separateLastToken = separateTrailingSemicolon && lastToken.Kind == Kind.SemicolonToken;
        var rightEnd = children.Length;
        if (separateLastToken)
        {
            rightEnd--;
        }

        var rightChildren = children[(splitTokenIndex + 1)..rightEnd];

        List<Node> result = new(4);
        if (leftChildren.Length > 0)
        {
            result.Add(CreateSyntaxList(factory, leftChildren));
        }

        result.Add(splitToken);
        if (rightChildren.Length > 0)
        {
            result.Add(CreateSyntaxList(factory, rightChildren));
        }

        if (separateLastToken)
        {
            result.Add(lastToken);
        }

        return [.. result];
    }

    /// <summary>Go: <c>func createSyntaxList(factory *ast.NodeFactory, children []*ast.Node) *ast.Node</c>（selectionranges.go:169-173）。</summary>
    internal static Node CreateSyntaxList(NodeFactory factory, Node[] children)
    {
        var list = factory.NewSyntaxList(children);
        list.Loc = new TextRange(children[0].Pos(), children[^1].End());
        return list;
    }

    /// <summary>Go: <c>func getSmartSelectionRange(l *LanguageService, sourceFile *ast.SourceFile, pos int) *lsproto.SelectionRange</c>（selectionranges.go:175-399）。</summary>
    internal static SelectionRange? GetSmartSelectionRange(LanguageService l, SourceFile sourceFile, int pos)
    {
        var factory = new NodeFactory();
        // Traversal discovers ranges from broadest to most specific, so retain the newest ranges nearest to the cursor
        var ranges = new SelectionRangeBuilder(MaxSelectionRangeDepth - 1);
        SelectionRange? root = null;
        var lastRange = new Range();
        if (sourceFile.ContentMapper() == "")
        {
            var (fullRange, _) = l._converters!.ToLSPRange(sourceFile, new TextRange(sourceFile.Pos(), sourceFile.End()));
            root = new SelectionRange { Range = fullRange };
            lastRange = fullRange;
        }

        bool NodeContainsPosition(Node? node)
        {
            if (node is null)
            {
                return false;
            }

            var start = GetTokenPosOfNode(node, sourceFile, true /*includeJSDoc*/);
            var end = node.End();
            return start <= pos && pos < end;
        }

        bool PositionShouldSnapToNode(Node node)
        {
            if (pos < node.End())
            {
                return true;
            }

            if (node.End() == pos)
            {
                Node? touchingPropertyName = GetTouchingPropertyName(sourceFile, pos);
                return touchingPropertyName is not null && touchingPropertyName.Pos() < node.End();
            }

            return false;
        }

        void PushSelectionRange(int start, int end)
        {
            if (start == end)
            {
                return;
            }

            if (!(start <= pos && pos <= end))
            {
                return;
            }

            var (lspRange, fidelity) = l._converters!.ToLSPRangeForFeature(sourceFile, new TextRange(start, end), Feature.SelectionRanges);
            if (fidelity.IsNone())
            {
                return;
            }

            if (CompareRanges(lastRange, lspRange) == 0)
            {
                return;
            }

            lastRange = lspRange;

            ranges.Push(lspRange);
        }

        void PushSelectionCommentRange(int start, int end)
        {
            PushSelectionRange(start, end);

            var commentPos = start;
            var text = sourceFile.Text();
            while (commentPos < end && commentPos < text.Length && text[commentPos] == '/')
            {
                commentPos++;
            }

            PushSelectionRange(commentPos, end);
        }

        bool PositionsAreOnSameLine(int pos1, int pos2)
        {
            if (pos1 == pos2)
            {
                return true;
            }

            var lineStarts = sourceFile.ECMALineMap();
            return ComputeLineOfPosition(lineStarts, pos1) == ComputeLineOfPosition(lineStarts, pos2);
        }

        bool ShouldSkipNode(Node node, Node? parent)
        {
            if (IsBlock(node))
            {
                return true;
            }

            if (IsTemplateSpan(node) || TypeScript.Ast.Ast.IsTemplateHead(node) || TypeScript.Ast.Ast.IsTemplateTail(node))
            {
                return true;
            }

            if (parent is not null && IsVariableDeclarationList(node) && IsVariableStatement(parent))
            {
                return true;
            }

            // Skip lone variable declarations
            if (parent is not null && IsVariableDeclaration(node) && IsVariableDeclarationList(parent))
            {
                var decl = parent.AsVariableDeclarationList();
                if (decl is not null && (decl.Declarations?.Nodes?.Length ?? 0) == 1)
                {
                    return true;
                }
            }

            if (IsJSDocTypeExpression(node) || IsJSDocSignature(node) || IsJSDocTypeLiteral(node))
            {
                return true;
            }

            return false;
        }

        Node? current;
        for (current = sourceFile.AsNode(); current is not null;)
        {
            Node? next = null;
            var parent = current;

            Node? Visit(Node node)
            {
                if (next is null)
                {
                    CommentRange? foundComment = null;
                    foreach (var comment in GetTrailingCommentRanges(factory, sourceFile.Text(), node.End()))
                    {
                        foundComment = comment;
                        break;
                    }

                    if (foundComment is not null && foundComment.Kind == Kind.SingleLineCommentTrivia)
                    {
                        PushSelectionCommentRange(foundComment.Range.Pos(), foundComment.Range.End());
                    }

                    if (NodeContainsPosition(node))
                    {
                        // Add range for multi-line function bodies before skipping the block
                        if (IsBlock(node) && IsFunctionLikeDeclaration(parent))
                        {
                            if (!PositionsAreOnSameLine(GetStartOfNode(node, sourceFile, false), node.End()))
                            {
                                var start = GetStartOfNode(node, sourceFile, false);
                                var end = node.End();
                                PushSelectionRange(start, end);
                            }
                        }

                        // Synthesize a stop for '${ ... }' since '${' and '}' actually belong to siblings.
                        if (IsTemplateSpan(parent))
                        {
                            var templateSpan = parent.AsTemplateSpan()!;
                            if (templateSpan.Literal is not null)
                            {
                                // Start from just before the '${' and end after the '}'
                                // The '${' is 2 characters before the expression start
                                var spanStart = node.Pos() - 2;
                                // The '}' is the first character of the template literal (middle or tail)
                                var spanEnd = GetStartOfNode(templateSpan.Literal, sourceFile, false) + 1;
                                // Validate the positions are reasonable
                                var text = sourceFile.Text();
                                if (spanStart >= 0 && spanEnd <= text.Length && spanStart < spanEnd)
                                {
                                    PushSelectionRange(spanStart, spanEnd);
                                }
                            }
                        }

                        if (!ShouldSkipNode(node, parent))
                        {
                            var start = GetStartOfNode(node, sourceFile, false);
                            var end = node.End();
                            PushSelectionRange(start, end);

                            if (IsMappedTypeNode(node))
                            {
                                for (var selectionParent = node; ;)
                                {
                                    Node? selectionChild = null;
                                    foreach (var child in GetSelectionChildren(factory, selectionParent, sourceFile))
                                    {
                                        var childStart = GetTokenPosOfNode(child, sourceFile, true /*includeJSDoc*/);
                                        if (childStart > pos)
                                        {
                                            break;
                                        }

                                        if (PositionShouldSnapToNode(child))
                                        {
                                            PushSelectionRange(childStart, child.End());
                                            selectionChild = child;
                                            break;
                                        }
                                    }

                                    if (selectionChild is null || !IsSyntaxList(selectionChild))
                                    {
                                        break;
                                    }

                                    selectionParent = selectionChild;
                                }
                            }

                            // String literals should have a stop both inside and outside their quotes.
                            if (IsStringLiteral(node) || node.Kind == Kind.TemplateExpression || node.Kind == Kind.NoSubstitutionTemplateLiteral)
                            {
                                // Only add inner content range if there's actually content (handles unterminated literals)
                                if (start + 1 < end - 1)
                                {
                                    PushSelectionRange(start + 1, end - 1);
                                }
                            }
                        }

                        next = node;
                    }
                }

                return node;
            }

            NodeList? VisitNodes(NodeList nodes, NodeVisitor v)
            {
                if (nodes is not null && (nodes.Nodes?.Length ?? 0) > 0)
                {
                    var shouldSkipList = parent is not null && (IsVariableDeclarationList(parent) || IsTemplateExpression(parent));

                    if (!shouldSkipList)
                    {
                        var start = GetStartOfNode(nodes.Nodes![0], sourceFile, false);
                        var end = nodes.Nodes[nodes.Nodes.Length - 1].End();

                        if (start <= pos && pos < end)
                        {
                            PushSelectionRange(start, end);
                        }
                    }
                }

                return v.VisitNodes(nodes);
            }

            // Visit JSDoc nodes first if they exist
            foreach (var jsdoc in current.JSDoc(sourceFile) ?? [])
            {
                Visit(jsdoc);
            }

            var tempVisitor = new NodeVisitor(Visit, null, new NodeVisitorHooks { VisitNodes = VisitNodes });

            current.VisitEachChild(tempVisitor);
            current = next;
        }

        return ranges.Build(root);
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideSelectionRanges(ctx context.Context, params *lsproto.SelectionRangeParams) (lsproto.SelectionRangeResponse, error)</c>（selectionranges.go:48-55）。</summary>
    public (SelectionRangesOrNull Response, Exception? Error) ProvideSelectionRanges(Context ctx, SelectionRangeParams @params)
    {
        var (_, sourceFile) = GetProgramAndFile(@params.TextDocument.Uri);
        if (sourceFile is null)
        {
            return (new SelectionRangesOrNull(), null);
        }

        List<SelectionRange?> results = new(@params.Positions?.Length ?? 0);
        foreach (var position in @params.Positions ?? [])
        {
            var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, position, Feature.SelectionRanges);
            if (positions.Length != 1 || !positions[0].Fidelity.IsSingleSegment())
            {
                return (new SelectionRangesOrNull(), null);
            }

            var selectionRange = Ls.GetSmartSelectionRange(this, positions[0].Script, positions[0].Position);
            if (selectionRange is not null)
            {
                results.Add(selectionRange);
            }
        }

        return (new SelectionRangesOrNull { SelectionRanges = [.. results] }, null);
    }
}
