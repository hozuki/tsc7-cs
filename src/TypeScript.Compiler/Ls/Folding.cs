// ported from: internal/ls/folding.go:1-658
//
// ⚠ 跨文件 helper 就地落地（照抄 G1 先例，artifacts/ls-g1-helpers.md §3.2）：completions.go:3027-3045 的
//   `getLineOfPosition` / `getLineEndOfPosition` 被 **folding.go（本文件）**、jsdoc_snippet.go（G3）、completions.go（G8）
//   共用；G4 先落地这两个成员，**G3/G8 请勿重复声明**（CS0111）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级类型 `foldingRangeKey` / `regionDelimiterResult` → 本文件的 `internal struct FoldingRangeKey`
//    （实现 `IEquatable<>`：Go 的结构体 `==` 是字段级值比较，C# 的 struct 默认 `Equals` 走反射且不含 `Range` 这类引用
//     字段的深比较，必须显式实现才等价）/ `internal struct RegionDelimiterResult`。字段名去 Go 的导出性 →
//    `_camelCase`（Go 未导出字段）。
// 2) 包级未导出函数 → `Ls` 静态类的 `internal static` 成员（PascalCase、方法不加 `_`）；`LangService` 的方法
//    （`provideFoldingRange` 等）→ partial 类成员（Go 导出 ⇒ `public`）。G4 的文件**不** `using static TypeScript.Ls.Ls;`
//    （避免与 `Ast` 的 8 个同名静态成员二义，artifacts/ls-g1-helpers.md §4.10），`Ls` 侧成员一律写 `Ls.Xxx(...)`。
// 3) `slices.SortStableFunc` / `slices.DeleteFunc` → LINQ `OrderBy/ThenBy`（稳定排序）与 `Where`（保留顺序的过滤）。
// 4) Go 的 `*a.StartCharacter` 解引用（nil 会 panic）→ C# 写 `!.Value`（nil 会抛 NRE，同样是"不该发生"的崩溃）。
// 5) `sourceFile.Statements` 在 C# 是 `NodeList?` → 取 `?.Nodes ?? []`（Go 里是必填字段）。
// 6) `printer.NewNodeFactory(p)` 在 C# 返回 `Printer.NodeFactory : Ast.NodeFactory`，Go 的 `.NodeFactory` 取内嵌字段
//    这一步不需要（子类实例本身就是 `Ast.NodeFactory`）。
// 7) `ast.SignatureDeclaration` 在 Go 是 `= Node` 的**类型别名**（internal/ast/ast_generated.go:555）⇒ C# 直接用 `Node`；
//    `node.Parameters()` 返回 `Node[]?` ⇒ 传 `?? []`。
// 8) Go 的 `switch` 里 `fallthrough`（KindTryStatement → default）→ C# 的 `goto default;`。
// 9) `getLineOfPosition` 的 `scanner.GetECMALineOfPosition` 与 `GetECMALineOfStarts` 都是 Scanner 的静态成员。
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Printer;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.LsProto.LsProto;
using static TypeScript.Printer.Printer;
using static TypeScript.Scanner.Scanner;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>Go: <c>type foldingRangeKey struct {...}</c>（folding.go:30-36）：用整条 FoldingRange 做去重键（值语义）。</summary>
internal readonly struct FoldingRangeKey : IEquatable<FoldingRangeKey>
{
    internal readonly uint _startLine;
    internal readonly uint _startCharacter;
    internal readonly uint _endLine;
    internal readonly uint _endCharacter;
    internal readonly FoldingRangeKind _kind;
    internal readonly string _collapsedText;
    internal readonly bool _hasStartCharacter;
    internal readonly bool _hasEndCharacter;
    internal readonly bool _hasKind;
    internal readonly bool _hasCollapsedText;

    internal FoldingRangeKey(
        uint startLine,
        uint endLine,
        uint startCharacter,
        bool hasStartCharacter,
        uint endCharacter,
        bool hasEndCharacter,
        FoldingRangeKind kind,
        bool hasKind,
        string collapsedText,
        bool hasCollapsedText)
    {
        _startLine = startLine;
        _endLine = endLine;
        _startCharacter = startCharacter;
        _hasStartCharacter = hasStartCharacter;
        _endCharacter = endCharacter;
        _hasEndCharacter = hasEndCharacter;
        _kind = kind;
        _hasKind = hasKind;
        _collapsedText = collapsedText;
        _hasCollapsedText = hasCollapsedText;
    }

    public bool Equals(FoldingRangeKey other) =>
        _startLine == other._startLine &&
        _startCharacter == other._startCharacter &&
        _endLine == other._endLine &&
        _endCharacter == other._endCharacter &&
        _hasStartCharacter == other._hasStartCharacter &&
        _hasEndCharacter == other._hasEndCharacter &&
        _hasKind == other._hasKind &&
        _hasCollapsedText == other._hasCollapsedText &&
        (!_hasKind || _kind == other._kind) &&
        (!_hasCollapsedText || string.Equals(_collapsedText, other._collapsedText, StringComparison.Ordinal));

    public override bool Equals(object? obj) => obj is FoldingRangeKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        _startLine,
        _startCharacter,
        _endLine,
        _endCharacter,
        _hasStartCharacter,
        _hasEndCharacter,
        _hasKind ? _kind : default,
        _hasCollapsedText ? _collapsedText : null);
}

/// <summary>Go: <c>type regionDelimiterResult struct { isStart bool; name string }</c>（folding.go:394-397）。</summary>
internal struct RegionDelimiterResult
{
    internal bool _isStart;
    internal string _name;
}

/// <summary>Go: <c>type regionStart struct { position int; collapsedText *string }</c>（folding.go:151-154，函数内局部类型）。</summary>
internal struct RegionStart
{
    internal int _position;
    internal string? _collapsedText;
}

public static partial class Ls
{
    /// <summary>Go: <c>func getLineOfPosition(file *ast.SourceFile, pos int) int</c>（completions.go:3027-3030；跨文件 helper，见文件头）。</summary>
    internal static int GetLineOfPosition(SourceFile file, int pos)
    {
        return GetECMALineOfPosition(file, pos);
    }

    /// <summary>Go: <c>func getLineEndOfPosition(file *ast.SourceFile, pos int) int</c>（completions.go:3033-3045；跨文件 helper，见文件头）。</summary>
    internal static int GetLineEndOfPosition(SourceFile file, int pos)
    {
        var line = GetLineOfPosition(file, pos);
        var lineStarts = GetECMALineStarts(file);
        int lastCharPos;
        if (line + 1 >= lineStarts.Length)
        {
            lastCharPos = file.End();
        }
        else
        {
            lastCharPos = lineStarts[line + 1] - 1;
        }

        var fullText = file.Text();
        if (lastCharPos > 0 && lastCharPos < fullText.Length && fullText[lastCharPos] == '\n' && fullText[lastCharPos - 1] == '\r')
        {
            return lastCharPos - 1;
        }

        return lastCharPos;
    }

    /// <summary>Go: <c>func keyForFoldingRange(foldingRange *lsproto.FoldingRange) foldingRangeKey</c>（folding.go:38-60）。</summary>
    internal static FoldingRangeKey KeyForFoldingRange(FoldingRange foldingRange)
    {
        return new FoldingRangeKey(
            foldingRange.StartLine,
            foldingRange.EndLine,
            foldingRange.StartCharacter ?? 0,
            foldingRange.StartCharacter is not null,
            foldingRange.EndCharacter ?? 0,
            foldingRange.EndCharacter is not null,
            foldingRange.Kind ?? default,
            foldingRange.Kind is not null,
            foldingRange.CollapsedText ?? "",
            foldingRange.CollapsedText is not null);
    }

    /// <summary>Go: <c>func visitNode(ctx, n *ast.Node, depthRemaining int, sourceFile *ast.SourceFile, l *LanguageService) []*lsproto.FoldingRange</c>（folding.go:206-306）。</summary>
    internal static List<FoldingRange?> VisitNode(Context ctx, Node n, int depthRemaining, SourceFile sourceFile, LanguageService l)
    {
        List<FoldingRange?> foldingRange = [];
        if ((n.Flags & NodeFlags.Reparsed) != 0 || depthRemaining == 0 || ctx.CancellationToken.IsCancellationRequested)
        {
            return foldingRange;
        }

        if ((!IsBinaryExpression(n) && IsDeclaration(n)) || IsVariableStatement(n) || IsReturnStatement(n) || IsCallOrNewExpression(n) || n.Kind == Kind.EndOfFile)
        {
            foldingRange.AddRange(AddOutliningForLeadingCommentsForNode(ctx, n, sourceFile, l));
        }

        if (IsFunctionLike(n) && n.Parent is not null && IsBinaryExpression(n.Parent) && n.Parent.AsBinaryExpression()!.Left is not null && IsPropertyAccessExpression(n.Parent.AsBinaryExpression()!.Left!))
        {
            foldingRange.AddRange(AddOutliningForLeadingCommentsForNode(ctx, n.Parent.AsBinaryExpression()!.Left!, sourceFile, l));
        }

        if (IsBlock(n))
        {
            var statements = n.AsBlock()!.Statements;
            if (statements is not null)
            {
                foldingRange.AddRange(AddOutliningForLeadingCommentsForPos(ctx, statements.End(), sourceFile, l));
            }
        }

        if (IsModuleBlock(n))
        {
            var statements = n.AsModuleBlock()!.Statements;
            if (statements is not null)
            {
                foldingRange.AddRange(AddOutliningForLeadingCommentsForPos(ctx, statements.End(), sourceFile, l));
            }
        }

        if (IsClassLike(n) || IsInterfaceDeclaration(n))
        {
            NodeList? members;
            if (IsClassDeclaration(n))
            {
                members = n.AsClassDeclaration()!.Members;
            }
            else if (IsClassExpression(n))
            {
                members = n.AsClassExpression()!.Members;
            }
            else
            {
                members = n.AsInterfaceDeclaration()!.Members;
            }

            if (members is not null)
            {
                foldingRange.AddRange(AddOutliningForLeadingCommentsForPos(ctx, members.End(), sourceFile, l));
            }
        }

        var span = GetOutliningSpanForNode(ctx, n, sourceFile, l);
        if (span is not null)
        {
            foldingRange.Add(span);
        }

        depthRemaining--;
        if (IsCallExpression(n))
        {
            depthRemaining++;
            var expressionNodes = VisitNode(ctx, n.Expression()!, depthRemaining, sourceFile, l);
            foldingRange.AddRange(expressionNodes);
            depthRemaining--;
            foreach (var arg in n.Arguments() ?? [])
            {
                if (arg is not null)
                {
                    foldingRange.AddRange(VisitNode(ctx, arg, depthRemaining, sourceFile, l));
                }
            }

            foreach (var typeArg in n.TypeArguments() ?? [])
            {
                if (typeArg is not null)
                {
                    foldingRange.AddRange(VisitNode(ctx, typeArg, depthRemaining, sourceFile, l));
                }
            }
        }
        else if (IsIfStatement(n) && n.AsIfStatement()!.ElseStatement is not null && IsIfStatement(n.AsIfStatement()!.ElseStatement!))
        {
            // Consider an 'else if' to be on the same depth as the 'if'.
            var ifStatement = n.AsIfStatement()!;
            var expressionNodes = VisitNode(ctx, n.Expression()!, depthRemaining, sourceFile, l);
            foldingRange.AddRange(expressionNodes);
            var thenNode = VisitNode(ctx, ifStatement.ThenStatement!, depthRemaining, sourceFile, l);
            foldingRange.AddRange(thenNode);
            depthRemaining++;
            var elseNode = VisitNode(ctx, ifStatement.ElseStatement!, depthRemaining, sourceFile, l);
            foldingRange.AddRange(elseNode);
            depthRemaining--;
        }
        else
        {
            n.ForEachChild(node =>
            {
                var childNode = VisitNode(ctx, node, depthRemaining, sourceFile, l);
                foldingRange.AddRange(childNode);
                return false;
            });
        }

        depthRemaining++;
        return foldingRange;
    }

    /// <summary>Go: <c>func addOutliningForLeadingCommentsForNode(ctx, n *ast.Node, sourceFile, l) []*lsproto.FoldingRange</c>（folding.go:308-313）。</summary>
    internal static List<FoldingRange?> AddOutliningForLeadingCommentsForNode(Context ctx, Node n, SourceFile sourceFile, LanguageService l)
    {
        if (IsJsxText(n))
        {
            return [];
        }

        return AddOutliningForLeadingCommentsForPos(ctx, n.Pos(), sourceFile, l);
    }

    /// <summary>Go: <c>func addOutliningForLeadingCommentsForPos(ctx, pos int, sourceFile, l) []*lsproto.FoldingRange</c>（folding.go:315-392）。</summary>
    internal static List<FoldingRange?> AddOutliningForLeadingCommentsForPos(Context ctx, int pos, SourceFile sourceFile, LanguageService l)
    {
        var p = new EmitContext();
        List<FoldingRange?> foldingRange = [];
        var firstSingleLineCommentStart = -1;
        var lastSingleLineCommentEnd = -1;
        var singleLineCommentCount = 0;
        var foldingRangeKindComment = FoldingRangeKind.Comment;

        FoldingRange? CombineAndAddMultipleSingleLineComments()
        {
            // Only outline spans of two or more consecutive single line comments
            if (singleLineCommentCount > 1)
            {
                return CreateFoldingRangeFromBounds(ctx, firstSingleLineCommentStart, lastSingleLineCommentEnd, foldingRangeKindComment, sourceFile, l);
            }

            return null;
        }

        var sourceText = sourceFile.Text();
        foreach (var comment in GetLeadingCommentRanges(NewNodeFactory(p), sourceText, pos))
        {
            var commentPos = comment.Range.Pos();
            var commentEnd = comment.Range.End();

            if (ctx.CancellationToken.IsCancellationRequested)
            {
                return [];
            }

            switch (comment.Kind)
            {
                case Kind.SingleLineCommentTrivia:
                {
                    // never fold region delimiters into single-line comment regions
                    var commentText = sourceText[commentPos..commentEnd];
                    if (ParseRegionDelimiter(commentText) is not null)
                    {
                        var comments = CombineAndAddMultipleSingleLineComments();
                        if (comments is not null)
                        {
                            foldingRange.Add(comments);
                        }

                        singleLineCommentCount = 0;
                        break;
                    }

                    // For single line comments, combine consecutive ones (2 or more) into
                    // a single span from the start of the first till the end of the last
                    if (singleLineCommentCount == 0)
                    {
                        firstSingleLineCommentStart = commentPos;
                    }

                    lastSingleLineCommentEnd = commentEnd;
                    singleLineCommentCount++;
                    break;
                }
                case Kind.MultiLineCommentTrivia:
                {
                    var comments = CombineAndAddMultipleSingleLineComments();
                    if (comments is not null)
                    {
                        foldingRange.Add(comments);
                    }

                    var commentRange = CreateFoldingRangeFromBounds(ctx, commentPos, commentEnd, foldingRangeKindComment, sourceFile, l);
                    if (commentRange is not null)
                    {
                        foldingRange.Add(commentRange);
                    }

                    singleLineCommentCount = 0;
                    break;
                }
                default:
                    Debug.AssertNever(comment.Kind);
                    break;
            }
        }

        var addedComments = CombineAndAddMultipleSingleLineComments();
        if (addedComments is not null)
        {
            foldingRange.Add(addedComments);
        }

        return foldingRange;
    }

    /// <summary>Go: <c>func parseRegionDelimiter(lineText string) *regionDelimiterResult</c>（folding.go:399-424）。</summary>
    internal static RegionDelimiterResult? ParseRegionDelimiter(string lineText)
    {
        // We trim the leading whitespace and // without the regex since the
        // multiple potential whitespace matches can make for some gnarly backtracking behavior
        lineText = lineText.TrimStart();
        if (!lineText.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        lineText = lineText[2..].Trim();
        if (lineText.EndsWith('\r'))
        {
            lineText = lineText[..^1];
        }

        if (!lineText.StartsWith('#'))
        {
            return null;
        }

        lineText = lineText[1..];
        var isStart = true;
        if (lineText.StartsWith("end", StringComparison.Ordinal))
        {
            isStart = false;
            lineText = lineText[3..];
        }

        if (!lineText.StartsWith("region", StringComparison.Ordinal))
        {
            return null;
        }

        lineText = lineText[6..];
        return new RegionDelimiterResult
        {
            _isStart = isStart,
            _name = lineText.Trim(),
        };
    }

    /// <summary>Go: <c>func getOutliningSpanForNode(ctx, n *ast.Node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:426-489）。</summary>
    internal static FoldingRange? GetOutliningSpanForNode(Context ctx, Node n, SourceFile sourceFile, LanguageService l)
    {
        switch (n.Kind)
        {
            case Kind.Block:
                if (IsFunctionLike(n.Parent))
                {
                    return FunctionSpan(ctx, n.Parent!, n, sourceFile, l);
                }

                // Check if the block is standalone, or 'attached' to some parent statement.
                // If the latter, we want to collapse the block, but consider its hint span
                // to be the entire span of the parent.
                switch (n.Parent!.Kind)
                {
                    case Kind.DoStatement:
                    case Kind.ForInStatement:
                    case Kind.ForOfStatement:
                    case Kind.ForStatement:
                    case Kind.IfStatement:
                    case Kind.WhileStatement:
                    case Kind.WithStatement:
                    case Kind.CatchClause:
                        return SpanForNode(ctx, n, Kind.OpenBraceToken, true /*useFullStart*/, sourceFile, l);
                    case Kind.TryStatement:
                    {
                        // Could be the try-block, or the finally-block.
                        var tryStatement = n.Parent.AsTryStatement()!;
                        if (ReferenceEquals(tryStatement.TryBlock, n))
                        {
                            return SpanForNode(ctx, n, Kind.OpenBraceToken, true /*useFullStart*/, sourceFile, l);
                        }
                        else if (ReferenceEquals(tryStatement.FinallyBlock, n))
                        {
                            var span = SpanForNode(ctx, n, Kind.OpenBraceToken, true /*useFullStart*/, sourceFile, l);
                            if (span is not null)
                            {
                                return span;
                            }
                        }

                        goto default;
                    }
                    default:
                    {
                        // Block was a standalone block.  In this case we want to only collapse
                        // the span of the block, independent of any parent span.
                        var (textRange, fidelity) = l.CreateLspRangeFromNodeForFeature(n, sourceFile, Feature.FoldingRanges);
                        if (fidelity.IsNone())
                        {
                            return null;
                        }

                        return CreateFoldingRange(ctx, textRange, "", "");
                    }
                }
            case Kind.ModuleBlock:
                return SpanForNode(ctx, n, Kind.OpenBraceToken, true /*useFullStart*/, sourceFile, l);
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
            case Kind.CaseBlock:
            case Kind.TypeLiteral:
            case Kind.ObjectBindingPattern:
                return SpanForNode(ctx, n, Kind.OpenBraceToken, true /*useFullStart*/, sourceFile, l);
            case Kind.TupleType:
                return SpanForNode(ctx, n, Kind.OpenBracketToken, !IsTupleTypeNode(n.Parent!), sourceFile, l);
            case Kind.CaseClause:
            case Kind.DefaultClause:
                return SpanForNodeArray(ctx, n.AsCaseOrDefaultClause()!.Statements, sourceFile, l);
            case Kind.ObjectLiteralExpression:
                return SpanForNode(ctx, n, Kind.OpenBraceToken, !IsArrayLiteralExpression(n.Parent!) && !IsCallExpression(n.Parent!), sourceFile, l);
            case Kind.ArrayLiteralExpression:
                return SpanForNode(ctx, n, Kind.OpenBracketToken, !IsArrayLiteralExpression(n.Parent!) && !IsCallExpression(n.Parent!), sourceFile, l);
            case Kind.JsxElement:
            case Kind.JsxFragment:
                return SpanForJSXElement(ctx, n, sourceFile, l);
            case Kind.JsxSelfClosingElement:
            case Kind.JsxOpeningElement:
                return SpanForJSXAttributes(ctx, n, sourceFile, l);
            case Kind.TemplateExpression:
            case Kind.NoSubstitutionTemplateLiteral:
                return SpanForTemplateLiteral(ctx, n, sourceFile, l);
            case Kind.ArrayBindingPattern:
                return SpanForNode(ctx, n, Kind.OpenBracketToken, !IsBindingElement(n.Parent!), sourceFile, l);
            case Kind.ArrowFunction:
                return SpanForArrowFunction(ctx, n, sourceFile, l);
            case Kind.CallExpression:
                return SpanForCallExpression(ctx, n, sourceFile, l);
            case Kind.ParenthesizedExpression:
                return SpanForParenthesizedExpression(ctx, n, sourceFile, l);
            case Kind.NamedImports:
            case Kind.NamedExports:
            case Kind.ImportAttributes:
                return SpanForImportExportElements(ctx, n, sourceFile, l);
        }

        return null;
    }

    /// <summary>Go: <c>func spanForImportExportElements(ctx, node *ast.Node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:491-513）。</summary>
    internal static FoldingRange? SpanForImportExportElements(Context ctx, Node node, SourceFile sourceFile, LanguageService l)
    {
        NodeList? elements = null;
        switch (node.Kind)
        {
            case Kind.NamedImports:
                elements = node.AsNamedImports()!.Elements;
                break;
            case Kind.NamedExports:
                elements = node.AsNamedExports()!.Elements;
                break;
            case Kind.ImportAttributes:
                elements = node.AsImportAttributes()!.Attributes;
                break;
        }

        if (elements is null || (elements.Nodes?.Length ?? 0) == 0)
        {
            return null;
        }

        var openToken = FindChildOfKind(node, Kind.OpenBraceToken, sourceFile);
        var closeToken = FindChildOfKind(node, Kind.CloseBraceToken, sourceFile);
        if (openToken is null || closeToken is null || PositionsAreOnSameLine(openToken.Pos(), closeToken.Pos(), sourceFile))
        {
            return null;
        }

        return RangeBetweenTokens(ctx, openToken, closeToken, sourceFile, false /*useFullStart*/, l);
    }

    /// <summary>Go: <c>func spanForParenthesizedExpression(ctx, node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:515-524）。</summary>
    internal static FoldingRange? SpanForParenthesizedExpression(Context ctx, Node node, SourceFile sourceFile, LanguageService l)
    {
        var start = GetStartOfNode(node, sourceFile, false /*includeJSDoc*/);
        if (PositionsAreOnSameLine(start, node.End(), sourceFile))
        {
            return null;
        }

        var (textRange, fidelity) = l.CreateFoldingRangeFromBounds(start, node.End(), sourceFile);
        if (fidelity.IsNone())
        {
            return null;
        }

        return CreateFoldingRange(ctx, textRange, "", "");
    }

    /// <summary>Go: <c>func spanForCallExpression(ctx, node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:526-537）。</summary>
    internal static FoldingRange? SpanForCallExpression(Context ctx, Node node, SourceFile sourceFile, LanguageService l)
    {
        var callExpression = node.AsCallExpression()!;
        if (callExpression.Arguments is null || (callExpression.Arguments.Nodes?.Length ?? 0) == 0)
        {
            return null;
        }

        var openToken = FindChildOfKind(node, Kind.OpenParenToken, sourceFile);
        var closeToken = FindChildOfKind(node, Kind.CloseParenToken, sourceFile);
        if (openToken is null || closeToken is null || PositionsAreOnSameLine(openToken.Pos(), closeToken.Pos(), sourceFile))
        {
            return null;
        }

        return RangeBetweenTokens(ctx, openToken, closeToken, sourceFile, true /*useFullStart*/, l);
    }

    /// <summary>Go: <c>func spanForArrowFunction(ctx, node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:539-548）。</summary>
    internal static FoldingRange? SpanForArrowFunction(Context ctx, Node node, SourceFile sourceFile, LanguageService l)
    {
        var arrowFunctionNode = node.AsArrowFunction()!;
        if (IsBlock(arrowFunctionNode.Body!) || IsParenthesizedExpression(arrowFunctionNode.Body!) || PositionsAreOnSameLine(arrowFunctionNode.Body!.Pos(), arrowFunctionNode.Body.End(), sourceFile))
        {
            return null;
        }

        var (textRange, fidelity) = l.CreateFoldingRangeFromBounds(arrowFunctionNode.Body.Pos(), arrowFunctionNode.Body.End(), sourceFile);
        if (fidelity.IsNone())
        {
            return null;
        }

        return CreateFoldingRange(ctx, textRange, "", "");
    }

    /// <summary>Go: <c>func spanForTemplateLiteral(ctx, node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:550-555）。</summary>
    internal static FoldingRange? SpanForTemplateLiteral(Context ctx, Node node, SourceFile sourceFile, LanguageService l)
    {
        if (node.Kind == Kind.NoSubstitutionTemplateLiteral && node.Text().Length == 0)
        {
            return null;
        }

        return CreateFoldingRangeFromBounds(ctx, GetStartOfNode(node, sourceFile, false /*includeJSDoc*/), node.End(), "", sourceFile, l);
    }

    /// <summary>Go: <c>func spanForJSXElement(ctx, node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:557-575）。</summary>
    internal static FoldingRange? SpanForJSXElement(Context ctx, Node node, SourceFile sourceFile, LanguageService l)
    {
        if (node.Kind == Kind.JsxElement)
        {
            var jsxElement = node.AsJsxElement()!;
            var (textRange, fidelity) = l.CreateFoldingRangeFromBounds(GetStartOfNode(jsxElement.OpeningElement!, sourceFile, false /*includeJSDoc*/), jsxElement.ClosingElement!.End(), sourceFile);
            if (fidelity.IsNone())
            {
                return null;
            }

            var tagName = GetTextOfNode(jsxElement.OpeningElement!.TagName()!);
            var bannerText = "<" + tagName + ">...</" + tagName + ">";
            return CreateFoldingRange(ctx, textRange, "", bannerText);
        }

        // JsxFragment
        var jsxFragment = node.AsJsxFragment()!;
        var (fragmentRange, fragmentFidelity) = l.CreateFoldingRangeFromBounds(GetStartOfNode(jsxFragment.OpeningFragment!, sourceFile, false /*includeJSDoc*/), jsxFragment.ClosingFragment!.End(), sourceFile);
        if (fragmentFidelity.IsNone())
        {
            return null;
        }

        return CreateFoldingRange(ctx, fragmentRange, "", "<>...</>");
    }

    /// <summary>Go: <c>func spanForJSXAttributes(ctx, node, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:577-587）。</summary>
    internal static FoldingRange? SpanForJSXAttributes(Context ctx, Node node, SourceFile sourceFile, LanguageService l)
    {
        Node? attributes;
        if (node.Kind == Kind.JsxSelfClosingElement)
        {
            attributes = node.AsJsxSelfClosingElement()!.Attributes;
        }
        else
        {
            attributes = node.AsJsxOpeningElement()!.Attributes;
        }

        if ((attributes?.Properties()?.Length ?? 0) == 0)
        {
            return null;
        }

        return CreateFoldingRangeFromBounds(ctx, GetStartOfNode(node, sourceFile, false /*includeJSDoc*/), node.End(), "", sourceFile, l);
    }

    /// <summary>Go: <c>func spanForNodeArray(ctx, statements *ast.NodeList, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:589-597）。</summary>
    internal static FoldingRange? SpanForNodeArray(Context ctx, NodeList? statements, SourceFile sourceFile, LanguageService l)
    {
        if (statements is not null && (statements.Nodes?.Length ?? 0) != 0)
        {
            var (textRange, fidelity) = l.CreateFoldingRangeFromBounds(statements.Pos(), statements.End(), sourceFile);
            if (fidelity.IsNone())
            {
                return null;
            }

            return CreateFoldingRange(ctx, textRange, "", "");
        }

        return null;
    }

    /// <summary>Go: <c>func spanForNode(ctx, node, open ast.Kind, useFullStart bool, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:599-609）。</summary>
    internal static FoldingRange? SpanForNode(Context ctx, Node node, Kind open, bool useFullStart, SourceFile sourceFile, LanguageService l)
    {
        var closeBrace = Kind.CloseBraceToken;
        if (open != Kind.OpenBraceToken)
        {
            closeBrace = Kind.CloseBracketToken;
        }

        var openToken = FindChildOfKind(node, open, sourceFile);
        var closeToken = FindChildOfKind(node, closeBrace, sourceFile);
        if (openToken is not null && closeToken is not null)
        {
            return RangeBetweenTokens(ctx, openToken, closeToken, sourceFile, useFullStart, l);
        }

        return null;
    }

    /// <summary>Go: <c>func rangeBetweenTokens(ctx, openToken, closeToken, sourceFile, useFullStart bool, l) *lsproto.FoldingRange</c>（folding.go:611-624）。</summary>
    internal static FoldingRange? RangeBetweenTokens(Context ctx, Node openToken, Node closeToken, SourceFile sourceFile, bool useFullStart, LanguageService l)
    {
        Range textRange;
        Fidelity fidelity;
        if (useFullStart)
        {
            (textRange, fidelity) = l.CreateFoldingRangeFromBounds(openToken.Pos(), closeToken.End(), sourceFile);
        }
        else
        {
            (textRange, fidelity) = l.CreateFoldingRangeFromBounds(GetStartOfNode(openToken, sourceFile, false /*includeJSDoc*/), closeToken.End(), sourceFile);
        }

        if (fidelity.IsNone())
        {
            return null;
        }

        return CreateFoldingRange(ctx, textRange, "", "");
    }

    /// <summary>Go: <c>func supportsCollapsedText(ctx context.Context) bool</c>（folding.go:626-628）。</summary>
    internal static bool SupportsCollapsedText(Context ctx)
    {
        return GetClientCapabilities().TextDocument.FoldingRange.FoldingRange.CollapsedText;
    }

    /// <summary>Go: <c>func createFoldingRange(ctx, textRange, foldingRangeKind, collapsedText string) *lsproto.FoldingRange</c>（folding.go:630-...）。</summary>
    internal static FoldingRange CreateFoldingRange(Context ctx, Range textRange, FoldingRangeKind foldingRangeKind, string collapsedText)
    {
        FoldingRangeKind? kind = null;
        if (foldingRangeKind != "")
        {
            kind = foldingRangeKind;
        }

        var result = new FoldingRange
        {
            StartLine = textRange.Start.Line,
            StartCharacter = textRange.Start.Character,
            EndLine = textRange.End.Line,
            EndCharacter = textRange.End.Character,
            Kind = kind,
        };
        if (collapsedText != "" && SupportsCollapsedText(ctx))
        {
            result.CollapsedText = collapsedText;
        }

        return result;
    }

    /// <summary>Go: <c>func createFoldingRangeFromBounds(ctx, pos int, end int, foldingRangeKind, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:624-...）。</summary>
    internal static FoldingRange? CreateFoldingRangeFromBounds(Context ctx, int pos, int end, FoldingRangeKind foldingRangeKind, SourceFile sourceFile, LanguageService l)
    {
        var (textRange, fidelity) = l.CreateFoldingRangeFromBounds(pos, end, sourceFile);
        if (fidelity.IsNone())
        {
            return null;
        }

        return CreateFoldingRange(ctx, textRange, foldingRangeKind, "");
    }

    /// <summary>Go: <c>func functionSpan(ctx, node, body, sourceFile, l) *lsproto.FoldingRange</c>（folding.go:634-641）。</summary>
    internal static FoldingRange? FunctionSpan(Context ctx, Node node, Node body, SourceFile sourceFile, LanguageService l)
    {
        var openToken = TryGetFunctionOpenToken(node, body, sourceFile);
        var closeToken = FindChildOfKind(body, Kind.CloseBraceToken, sourceFile);
        if (openToken is not null && closeToken is not null)
        {
            return RangeBetweenTokens(ctx, openToken, closeToken, sourceFile, true /*useFullStart*/, l);
        }

        return null;
    }

    /// <summary>Go: <c>func tryGetFunctionOpenToken(node *ast.SignatureDeclaration, body *ast.Node, sourceFile) *ast.Node</c>（folding.go:643-651）。</summary>
    internal static Node? TryGetFunctionOpenToken(Node node, Node body, SourceFile sourceFile)
    {
        if (IsNodeArrayMultiLine(node.Parameters() ?? [], sourceFile))
        {
            var openParenToken = FindChildOfKind(node, Kind.OpenParenToken, sourceFile);
            if (openParenToken is not null)
            {
                return openParenToken;
            }
        }

        return FindChildOfKind(body, Kind.OpenBraceToken, sourceFile);
    }

    /// <summary>Go: <c>func isNodeArrayMultiLine(list []*ast.Node, sourceFile) bool</c>（folding.go:653-658）。</summary>
    internal static bool IsNodeArrayMultiLine(Node[] list, SourceFile sourceFile)
    {
        if (list.Length == 0)
        {
            return false;
        }

        return !PositionsAreOnSameLine(list[0].Pos(), list[^1].End(), sourceFile);
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideFoldingRange(ctx context.Context, documentURI lsproto.DocumentUri) (lsproto.FoldingRangeResponse, error)</c>（folding.go:19-50）。</summary>
    public (FoldingRangesOrNull Response, Exception? Error) ProvideFoldingRange(Context ctx, DocumentUri documentURI)
    {
        var (_, sourceFile) = GetProgramAndFile(documentURI);
        List<SourceFile> projections = [sourceFile, .. sourceFile.SupplementalSourceFiles()];
        List<FoldingRange?> res = [];
        foreach (var projection in projections)
        {
            var ranges = AddNodeOutliningSpans(ctx, projection);
            ranges.AddRange(AddRegionOutliningSpans(ctx, projection));
            if (GetClientCapabilities().TextDocument.FoldingRange.LineFoldingOnly)
            {
                ranges = AdjustFoldingEnd(ranges, projection);
            }

            res.AddRange(ranges);
        }

        res =
        [
            .. res
                .OrderBy(r => r!.StartLine)
                .ThenBy(r => r!.StartCharacter!.Value)
                .ThenBy(r => r!.EndLine)
                .ThenBy(r => r!.EndCharacter!.Value),
        ];
        Set<FoldingRangeKey> seen = new();
        res = [.. res.Where(foldingRange => seen.AddIfAbsent(Ls.KeyForFoldingRange(foldingRange!)))];
        return (new FoldingRangesOrNull { FoldingRanges = [.. res] }, null);
    }

    /// <summary>
    /// adjustFoldingEnd adjusts the end line of folding ranges when the client signals lineFoldingOnly.
    /// This mirrors the behavior of VS Code's built-in TypeScript extension (workaround for vscode#47240).
    /// When lineFoldingOnly is true, we hide lines from startLine+1 to endLine. And to keep closing
    /// brackets/braces visible, we subtract 1 from endLine when the range ends with a closing pair character.
    /// Go: folding.go:62-90。
    /// </summary>
    internal List<FoldingRange?> AdjustFoldingEnd(List<FoldingRange?> ranges, SourceFile sourceFile)
    {
        var sourceText = sourceFile.Text();
        List<FoldingRange?> result = new(ranges.Count);
        foreach (var r in ranges)
        {
            if (r!.EndCharacter is not null && r.EndCharacter > 0)
            {
                var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, new Position
                {
                    Line = r.EndLine,
                    Character = r.EndCharacter.Value,
                }, Feature.FoldingRanges);
                MappedPosition<SourceFile>? position = null;
                for (var i = 0; i < positions.Length; i++)
                {
                    if (ReferenceEquals(positions[i].Script, sourceFile) && !positions[i].Fidelity.IsNone())
                    {
                        position = positions[i];
                        break;
                    }
                }

                if (position is not null && position.Value.Position > 0 && position.Value.Position <= sourceText.Length)
                {
                    var endOffset = position.Value.Position;
                    var foldEndChar = sourceText[endOffset - 1];
                    if (foldEndChar == '}' || foldEndChar == ']' || foldEndChar == ')' || foldEndChar == '`' || foldEndChar == '>')
                    {
                        if (r.EndLine > r.StartLine)
                        {
                            r.EndLine--;
                        }
                    }
                }
            }

            result.Add(r);
        }

        return result;
    }

    /// <summary>Go: <c>func (l *LanguageService) addNodeOutliningSpans(ctx, sourceFile) []*lsproto.FoldingRange</c>（folding.go:92-135）。</summary>
    internal List<FoldingRange?> AddNodeOutliningSpans(Context ctx, SourceFile sourceFile)
    {
        var depthRemaining = 40;
        var current = 0;

        var statements = sourceFile.Statements?.Nodes ?? [];
        var n = statements.Length;
        List<FoldingRange?> foldingRange = new(40);
        while (current < n)
        {
            while (current < n && !IsAnyImportSyntax(statements[current]))
            {
                foldingRange.AddRange(Ls.VisitNode(ctx, statements[current], depthRemaining, sourceFile, this));
                current++;
            }

            if (current == n)
            {
                break;
            }

            var firstImport = current;
            while (current < n && IsAnyImportSyntax(statements[current]))
            {
                foldingRange.AddRange(Ls.VisitNode(ctx, statements[current], depthRemaining, sourceFile, this));
                current++;
            }

            var lastImport = current - 1;
            if (lastImport != firstImport)
            {
                var foldingRangeKind = FoldingRangeKind.Imports;
                var imports = Ls.CreateFoldingRangeFromBounds(
                    ctx,
                    GetStartOfNode(FindChildOfKind(statements[firstImport], Kind.ImportKeyword, sourceFile)!, sourceFile, false /*includeJSDoc*/),
                    statements[lastImport].End(),
                    foldingRangeKind,
                    sourceFile,
                    this);
                if (imports is not null)
                {
                    foldingRange.Add(imports);
                }
            }
        }

        // Visit the EOF Token so that comments which aren't attached to statements are included.
        foldingRange.AddRange(Ls.VisitNode(ctx, sourceFile.EndOfFileToken!, depthRemaining, sourceFile, this));
        return foldingRange;
    }

    /// <summary>Go: <c>func (l *LanguageService) addRegionOutliningSpans(ctx, sourceFile) []*lsproto.FoldingRange</c>（folding.go:137-204）。</summary>
    internal List<FoldingRange?> AddRegionOutliningSpans(Context ctx, SourceFile sourceFile)
    {
        List<RegionStart> regions = new(40);
        List<FoldingRange?> @out = new(40);
        var lineStarts = GetECMALineStarts(sourceFile);
        foreach (var currentLineStart in lineStarts)
        {
            var lineEnd = Ls.GetLineEndOfPosition(sourceFile, currentLineStart);
            var lineText = sourceFile.Text()[(int)currentLineStart..lineEnd];
            var result = Ls.ParseRegionDelimiter(lineText);
            if (result is null || Ls.IsInComment(sourceFile, currentLineStart, GetTokenAtPosition(sourceFile, currentLineStart)) is not null)
            {
                continue;
            }

            if (result.Value._isStart)
            {
                var region = new RegionStart
                {
                    _position = lineText.IndexOf("//", StringComparison.Ordinal) + currentLineStart,
                };
                if (Ls.SupportsCollapsedText(ctx))
                {
                    var collapsedText = "#region";
                    if (result.Value._name != "")
                    {
                        collapsedText = result.Value._name;
                    }

                    region._collapsedText = collapsedText;
                }

                regions.Add(region);
            }
            else
            {
                if (regions.Count > 0)
                {
                    var region = regions[^1];
                    regions.RemoveAt(regions.Count - 1);
                    var (textRange, fidelity) = CreateFoldingRangeFromBounds(region._position, lineEnd, sourceFile);
                    if (fidelity.IsNone())
                    {
                        continue;
                    }

                    var foldingRange = Ls.CreateFoldingRange(ctx, textRange, FoldingRangeKind.Region, "");
                    foldingRange.CollapsedText = region._collapsedText;
                    @out.Add(foldingRange);
                }
            }
        }

        return @out;
    }

    /// <summary>Go: <c>func (l *LanguageService) createFoldingRangeFromBounds(start, end int, sourceFile) (lsproto.Range, spanmap.Fidelity)</c>（folding.go:630-632）。</summary>
    internal (Range Range, Fidelity Fidelity) CreateFoldingRangeFromBounds(int start, int end, SourceFile sourceFile)
    {
        return _converters!.ToLSPRangeForFeature(sourceFile, new TextRange(start, end), Feature.FoldingRanges);
    }
}
