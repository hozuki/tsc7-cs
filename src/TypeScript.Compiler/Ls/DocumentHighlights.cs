// ported from: internal/ls/documenthighlights.go:1-796
//
// ⚠ **跨文件 helper 就地落地**（照抄 G1 先例，artifacts/ls-g1-helpers.md §3.2；已与 G6/`LsG6References` 对齐）：
//   本文件声明 findallreferences.go 的下列成员 —— **G6 的 `FindAllReferences.cs` 不得重复声明**（CS0111）：
//     `ReferenceUse`(28-36) / `RefOptions`(38-45) / `resolveEntrySource`(187) / `resolveEntry`(198) /
//     `getLocationOfEntryForFeature`(180) / `getRangeOfEntryForFeature`(172) / `getRangeOfEntry`(165) /
//     `getFileNameOfEntry`(169)。
//   G6 仍然负责 findallreferences.go 的其余部分（含 `getReferencedSymbolsForNode`，本文件按冻结签名调用）。
//
// 与 Go 的偏离（逐条说明）：
//
// 1) `combineMultiDocumentHighlights` 等包级未导出函数 → `Ls` 静态类的 `internal static`（PascalCase）；`*LanguageService`
//    方法 → partial 类成员（Go 导出 ⇒ `public`，`provide*` 等未导出 ⇒ `internal`）。
// 2) **`seen` 的写回语义**：Go 里 `seen` 是 `map[DocumentUri]collections.Set[Range]`，`ranges := seen[uri]` 拿到的是
//    **Set 的副本**（Set 是 struct），改完要 `seen[uri] = ranges` 写回；C# 的 `Set<T>` 是 **class**（引用语义）⇒ 改成
//    `TryGetValue` 取不到就 `new Set<RangeKey>()` 放进字典，之后原地修改（等价，且不再需要写回）。
//    另：Go 的 `Set[lsproto.Range]` 按**值**去重（Range 是可比较 struct），C# 的 `LsProto.Range` 是 class
//    ⇒ 用值键 `RangeKey`（声明在 `CrossProject.cs`，同 `Definition.cs` 的 `LocationKey` 约定）。
// 3) `refOptions{use: referenceUseNone}` → `new RefOptions { _use = ReferenceUse.None }`（这两个类型由本文件声明，见上）。
// 4) `(string, *DocumentHighlight)` → `(string FileName, DocumentHighlight? Highlight)`；`&kind` → `DocumentHighlightKind?`。
// 5) `entry.sourceFile` / `entry.kind` / `entry.node` → G1 前缀里的 `_sourceFile` / `_kind` / `_node`
//    （G1 已把 Go 的未导出字段落成 `_camelCase`，见 Ls/FindAllReferences.cs:173-181）。
// 6) `getChildrenFromNonJSDocNode(...)` → `Ls.GetChildrenFromNonJSDocNode(...) ?? []`（C# 返回可空数组）。
// 7) `slices.Backward` → 递减索引；`astnav.GetStartOfNode(astnav.FindChildOfKind(...))`（Go 假定非 nil）→ `!`。
// 8) `l.getReferencedSymbolsForNode` 由 G6 落地；本文件只调用（签名已冻结，见交付说明）。
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using TypeScript.StringUtil;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.LsConv.LsConv;
using static TypeScript.LsProto.LsProto;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;
// 同名类型消歧（`using System` / `using TypeScript.SpanMap` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>Go: <c>type referenceUse int</c>（findallreferences.go:26-36）——就地落地，见文件头。</summary>
internal enum ReferenceUse
{
    None = 0,
    Other = 1,
    References = 2,
    Rename = 3,
}

/// <summary>Go: <c>type refOptions struct</c>（findallreferences.go:38-45）——就地落地，见文件头。</summary>
internal sealed class RefOptions
{
    internal bool _findInStrings;
    internal bool _findInComments;
    internal ReferenceUse _use;
    internal bool _implementations;
    internal bool _useAliasesForRename;
}

public static partial class Ls
{
    /// <summary>Go: <c>func combineMultiDocumentHighlights(results []lsproto.MultiDocumentHighlightsOrNull) lsproto.MultiDocumentHighlightsOrNull</c>（documenthighlights.go:105-134）。</summary>
    internal static MultiDocumentHighlightsOrNull CombineMultiDocumentHighlights(List<MultiDocumentHighlightsOrNull> results)
    {
        Dictionary<DocumentUri, MultiDocumentHighlight> byURI = [];
        Dictionary<DocumentUri, Set<RangeKey>> seen = [];
        List<MultiDocumentHighlight> combinedDocuments = [];
        foreach (var result in results)
        {
            if (result.MultiDocumentHighlights is null)
            {
                continue;
            }

            foreach (var document in result.MultiDocumentHighlights)
            {
                if (document is null)
                {
                    continue;
                }

                if (!byURI.TryGetValue(document.Uri, out var combinedDocument))
                {
                    combinedDocument = new MultiDocumentHighlight { Uri = document.Uri };
                    byURI[document.Uri] = combinedDocument;
                    combinedDocuments.Add(combinedDocument);
                }

                if (!seen.TryGetValue(document.Uri, out var ranges))
                {
                    ranges = new Set<RangeKey>();
                    seen[document.Uri] = ranges;
                }

                foreach (var highlight in document.Highlights ?? [])
                {
                    if (highlight is not null && ranges.AddIfAbsent(new RangeKey(highlight.Range)))
                    {
                        combinedDocument.Highlights = [.. combinedDocument.Highlights ?? [], highlight];
                    }
                }
            }
        }

        return new MultiDocumentHighlightsOrNull { MultiDocumentHighlights = [.. combinedDocuments] };
    }

    /// <summary>Go: <c>func getIfElseKeywords(ifStatement *ast.IfStatement, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:387-424）。</summary>
    internal static List<Node> GetIfElseKeywords(IfStatement ifStatement, SourceFile sourceFile)
    {
        // We may be at an if statement like those in the range below:
        //
        //   ```
        //   if (...) {
        //   } else [|if (...) {}|]
        //   ````
        //
        // Traverse upwards through all parent if-statements linked by their else-branches.
        while (IsIfStatement(ifStatement.Parent!))
        {
            // See if the parent's `else` is actually the current `if` statement.
            var parentingIf = ifStatement.Parent!.AsIfStatement()!;
            var elseStatement = parentingIf.ElseStatement;
            if (!ReferenceEquals(elseStatement, ifStatement.AsNode()))
            {
                break;
            }

            ifStatement = parentingIf;
        }

        List<Node> keywords = [];

        // Traverse back down through the else branches, aggregating if/else keywords of if-statements.
        while (true)
        {
            var children = Ls.GetChildrenFromNonJSDocNode(ifStatement.AsNode(), sourceFile) ?? [];
            if (children.Length > 0 && children[0].Kind == Kind.IfKeyword)
            {
                keywords.Add(children[0]);
            }

            // Generally the 'else' keyword is second-to-last, so traverse backwards.
            for (var i = children.Length - 1; i >= 0; i--)
            {
                if (children[i].Kind == Kind.ElseKeyword)
                {
                    keywords.Add(children[i]);
                    break;
                }
            }

            var elseStatement = ifStatement.ElseStatement;
            if (elseStatement is null || !IsIfStatement(elseStatement))
            {
                break;
            }

            ifStatement = elseStatement.AsIfStatement()!;
        }

        return keywords;
    }

    /// <summary>Go: <c>func getReturnOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:426-453）。</summary>
    internal static List<Node> GetReturnOccurrences(Node node, SourceFile sourceFile)
    {
        var funcNode = FindAncestor(node.Parent, IsFunctionLike);
        if (funcNode is null)
        {
            return [];
        }

        List<Node> keywords = [];
        var body = funcNode.Body();
        if (body is not null)
        {
            ForEachReturnStatement(body, ret =>
            {
                var keyword = FindChildOfKind(ret, Kind.ReturnKeyword, sourceFile);
                if (keyword is not null)
                {
                    keywords.Add(keyword);
                }

                return false; // continue traversal
            });

            // Get all throw statements not in a try block
            var throwStatements = AggregateOwnedThrowStatements(body, sourceFile);
            foreach (var @throw in throwStatements)
            {
                var keyword = FindChildOfKind(@throw, Kind.ThrowKeyword, sourceFile);
                if (keyword is not null)
                {
                    keywords.Add(keyword);
                }
            }
        }

        return keywords;
    }

    /// <summary>Go: <c>func aggregateOwnedThrowStatements(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:455-480）。</summary>
    internal static List<Node> AggregateOwnedThrowStatements(Node node, SourceFile sourceFile)
    {
        if (IsThrowStatement(node))
        {
            return [node];
        }

        if (IsTryStatement(node))
        {
            // Exceptions thrown within a try block lacking a catch clause are "owned" in the current context.
            var statement = node.AsTryStatement()!;
            var tryBlock = statement.TryBlock;
            var catchClause = statement.CatchClause;
            var finallyBlock = statement.FinallyBlock;

            List<Node> result = [];
            if (catchClause is not null)
            {
                result = AggregateOwnedThrowStatements(catchClause, sourceFile);
            }
            else if (tryBlock is not null)
            {
                result = AggregateOwnedThrowStatements(tryBlock, sourceFile);
            }

            if (finallyBlock is not null)
            {
                result.AddRange(AggregateOwnedThrowStatements(finallyBlock, sourceFile));
            }

            return result;
        }

        // Do not cross function boundaries.
        if (IsFunctionLike(node))
        {
            return [];
        }

        return FlatMapChildren(node, sourceFile, AggregateOwnedThrowStatements);
    }

    /// <summary>Go: <c>func flatMapChildren[T any](node *ast.Node, sourceFile *ast.SourceFile, cb func(child *ast.Node, sourceFile *ast.SourceFile) []T) []T</c>（documenthighlights.go:482-492）。</summary>
    internal static List<T> FlatMapChildren<T>(Node node, SourceFile sourceFile, Func<Node, SourceFile, List<T>> cb)
    {
        List<T> result = [];

        node.ForEachChild(child =>
        {
            var value = cb(child, sourceFile);
            if (value is not null)
            {
                result.AddRange(value);
            }

            return false; // continue traversal
        });
        return result;
    }

    /// <summary>Go: <c>func getThrowOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:494-521）。</summary>
    internal static List<Node> GetThrowOccurrences(Node node, SourceFile sourceFile)
    {
        var owner = GetThrowStatementOwner(node);
        if (owner is null)
        {
            return [];
        }

        List<Node> keywords = [];

        // Aggregate all throw statements "owned" by this owner.
        var throwStatements = AggregateOwnedThrowStatements(owner, sourceFile);
        foreach (var @throw in throwStatements)
        {
            var keyword = FindChildOfKind(@throw, Kind.ThrowKeyword, sourceFile);
            if (keyword is not null)
            {
                keywords.Add(keyword);
            }
        }

        // If the "owner" is a function, then we equate 'return' and 'throw' statements in their
        // ability to "jump out" of the function, and include occurrences for both
        if (IsFunctionBlock(owner))
        {
            ForEachReturnStatement(owner, ret =>
            {
                var keyword = FindChildOfKind(ret, Kind.ReturnKeyword, sourceFile);
                if (keyword is not null)
                {
                    keywords.Add(keyword);
                }

                return false; // continue traversal
            });
        }

        return keywords;
    }

    /// <summary>
    /// For lack of a better name, this function takes a throw statement and returns the
    /// nearest ancestor that is a try-block (whose try statement has a catch clause),
    /// function-block, or source file.
    /// Go: documenthighlights.go:523-546。
    /// </summary>
    internal static Node? GetThrowStatementOwner(Node throwStatement)
    {
        var child = throwStatement;
        while (child.Parent is not null)
        {
            var parent = child.Parent;

            if (IsFunctionBlock(parent) || parent.Kind == Kind.SourceFile)
            {
                return parent;
            }

            // A throw-statement is only owned by a try-statement if the try-statement has
            // a catch clause, and if the throw-statement occurs within the try block.
            if (IsTryStatement(parent))
            {
                var tryStatement = parent.AsTryStatement()!;
                if (ReferenceEquals(tryStatement.TryBlock, child) && tryStatement.CatchClause is not null)
                {
                    return child;
                }
            }

            child = parent;
        }

        return null;
    }

    /// <summary>Go: <c>func getTryCatchFinallyOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:548-572）。</summary>
    internal static List<Node> GetTryCatchFinallyOccurrences(Node node, SourceFile sourceFile)
    {
        var tryStatement = node.AsTryStatement()!;

        List<Node> keywords = [];
        var token = LsUtil.LsUtil.GetFirstToken(node, sourceFile);
        if (token is not null && token.Kind == Kind.TryKeyword)
        {
            keywords.Add(token);
        }

        if (tryStatement.CatchClause is not null)
        {
            var catchToken = FindChildOfKind(node, Kind.CatchKeyword, sourceFile);
            if (catchToken is not null)
            {
                keywords.Add(catchToken);
            }
        }

        if (tryStatement.FinallyBlock is not null)
        {
            var finallyKeyword = FindChildOfKind(node, Kind.FinallyKeyword, sourceFile);
            if (finallyKeyword is not null)
            {
                keywords.Add(finallyKeyword);
            }
        }

        return keywords;
    }

    /// <summary>Go: <c>func getSwitchCaseDefaultOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:574-601）。</summary>
    internal static List<Node> GetSwitchCaseDefaultOccurrences(Node node, SourceFile sourceFile)
    {
        var switchStatement = node.AsSwitchStatement()!;

        List<Node> keywords = [];
        var token = LsUtil.LsUtil.GetFirstToken(node, sourceFile)!;
        if (token.Kind == Kind.SwitchKeyword)
        {
            keywords.Add(token);
        }

        var clauses = switchStatement.CaseBlock!.AsCaseBlock()!.Clauses;
        foreach (var clause in clauses?.Nodes ?? [])
        {
            var clauseToken = LsUtil.LsUtil.GetFirstToken(clause, sourceFile)!;
            if (clauseToken.Kind == Kind.CaseKeyword || clauseToken.Kind == Kind.DefaultKeyword)
            {
                keywords.Add(clauseToken);
            }

            var breakAndContinueStatements = AggregateAllBreakAndContinueStatements(clause, sourceFile);
            foreach (var statement in breakAndContinueStatements)
            {
                if (statement.Kind == Kind.BreakStatement && OwnsBreakOrContinueStatement(switchStatement.AsNode(), statement))
                {
                    keywords.Add(LsUtil.LsUtil.GetFirstToken(statement, sourceFile)!);
                }
            }
        }

        return keywords;
    }

    /// <summary>Go: <c>func aggregateAllBreakAndContinueStatements(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:603-611）。</summary>
    internal static List<Node> AggregateAllBreakAndContinueStatements(Node node, SourceFile sourceFile)
    {
        if (IsBreakOrContinueStatement(node))
        {
            return [node];
        }

        if (IsFunctionLike(node))
        {
            return [];
        }

        return FlatMapChildren(node, sourceFile, AggregateAllBreakAndContinueStatements);
    }

    /// <summary>Go: <c>func ownsBreakOrContinueStatement(owner, statement *ast.Node) bool</c>（documenthighlights.go:613-619）。</summary>
    internal static bool OwnsBreakOrContinueStatement(Node owner, Node statement)
    {
        var actualOwner = GetBreakOrContinueOwner(statement);
        if (actualOwner is null)
        {
            return false;
        }

        return ReferenceEquals(actualOwner, owner);
    }

    /// <summary>Go: <c>func getBreakOrContinueOwner(statement *ast.Node) *ast.Node</c>（documenthighlights.go:621-646）。</summary>
    internal static Node? GetBreakOrContinueOwner(Node statement)
    {
        return FindAncestorOrQuit(statement, node =>
        {
            switch (node.Kind)
            {
                case Kind.SwitchStatement:
                    if (statement.Kind == Kind.ContinueStatement)
                    {
                        return FindAncestorResult.False;
                    }

                    goto case Kind.ForStatement;
                case Kind.ForStatement:
                case Kind.ForInStatement:
                case Kind.ForOfStatement:
                case Kind.WhileStatement:
                case Kind.DoStatement:
                    // If the statement is labeled, check if the node is labeled by the statement's label.
                    if (statement.Label() is null || IsLabeledBy(node, statement.Label()!.Text()))
                    {
                        return FindAncestorResult.True;
                    }

                    return FindAncestorResult.False;
                default:
                    // Don't cross function boundaries.
                    if (IsFunctionLike(node))
                    {
                        return FindAncestorResult.Quit;
                    }

                    return FindAncestorResult.False;
            }
        });
    }

    /// <summary>
    /// Whether or not a 'node' is preceded by a label of the given string.
    /// Note: 'node' cannot be a SourceFile.
    /// Go: documenthighlights.go:648-658。
    /// </summary>
    internal static bool IsLabeledBy(Node node, string labelName)
    {
        return FindAncestorOrQuit(node.Parent, owner =>
        {
            if (!IsLabeledStatement(owner))
            {
                return FindAncestorResult.Quit;
            }

            if (owner.Label()!.Text() == labelName)
            {
                return FindAncestorResult.True;
            }

            return FindAncestorResult.False;
        }) is not null;
    }

    /// <summary>Go: <c>func getBreakOrContinueStatementOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:660-673）。</summary>
    internal static List<Node> GetBreakOrContinueStatementOccurrences(Node node, SourceFile sourceFile)
    {
        var owner = GetBreakOrContinueOwner(node);
        if (owner is not null)
        {
            switch (owner.Kind)
            {
                case Kind.ForStatement:
                case Kind.ForInStatement:
                case Kind.ForOfStatement:
                case Kind.DoStatement:
                case Kind.WhileStatement:
                    return GetLoopBreakContinueOccurrences(owner, sourceFile);
                case Kind.SwitchStatement:
                    return GetSwitchCaseDefaultOccurrences(owner, sourceFile);
            }
        }

        return [];
    }

    /// <summary>Go: <c>func getLoopBreakContinueOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:675-698）。</summary>
    internal static List<Node> GetLoopBreakContinueOccurrences(Node node, SourceFile sourceFile)
    {
        List<Node> keywords = [];

        var token = LsUtil.LsUtil.GetFirstToken(node, sourceFile)!;
        if (token.Kind == Kind.ForKeyword || token.Kind == Kind.DoKeyword || token.Kind == Kind.WhileKeyword)
        {
            keywords.Add(token);
            if (node.Kind == Kind.DoStatement)
            {
                var loopTokens = Ls.GetChildrenFromNonJSDocNode(node, sourceFile) ?? [];
                for (var i = loopTokens.Length - 1; i >= 0; i--)
                {
                    if (loopTokens[i].Kind == Kind.WhileKeyword)
                    {
                        keywords.Add(loopTokens[i]);
                        break;
                    }
                }
            }
        }

        var breakAndContinueStatements = AggregateAllBreakAndContinueStatements(node, sourceFile);
        foreach (var statement in breakAndContinueStatements)
        {
            var statementToken = LsUtil.LsUtil.GetFirstToken(statement, sourceFile)!;
            if (OwnsBreakOrContinueStatement(node, statement) && (statementToken.Kind == Kind.BreakKeyword || statementToken.Kind == Kind.ContinueKeyword))
            {
                keywords.Add(statementToken);
            }
        }

        return keywords;
    }

    /// <summary>Go: <c>func getAsyncAndAwaitOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:700-728）。</summary>
    internal static List<Node> GetAsyncAndAwaitOccurrences(Node node, SourceFile sourceFile)
    {
        var fun = GetContainingFunction(node);
        if (fun is null)
        {
            return [];
        }

        List<Node> keywords = [];

        foreach (var modifier in fun.ModifierNodes() ?? [])
        {
            if (modifier.Kind == Kind.AsyncKeyword)
            {
                keywords.Add(modifier);
            }
        }

        fun.ForEachChild(child =>
        {
            TraverseWithoutCrossingFunction(child, sourceFile, child =>
            {
                if (IsAwaitExpression(child))
                {
                    var token = LsUtil.LsUtil.GetFirstToken(child, sourceFile)!;
                    if (token.Kind == Kind.AwaitKeyword)
                    {
                        keywords.Add(token);
                    }
                }
            });
            return false; // continue traversal
        });

        return keywords;
    }

    /// <summary>Go: <c>func getYieldOccurrences(node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:730-751）。</summary>
    internal static List<Node> GetYieldOccurrences(Node node, SourceFile sourceFile)
    {
        var parentFunc = FindAncestor(node.Parent, IsFunctionLike);
        if (parentFunc is null)
        {
            return [];
        }

        List<Node> keywords = [];

        parentFunc.ForEachChild(child =>
        {
            TraverseWithoutCrossingFunction(child, sourceFile, child =>
            {
                if (IsYieldExpression(child))
                {
                    var token = LsUtil.LsUtil.GetFirstToken(child, sourceFile)!;
                    if (token.Kind == Kind.YieldKeyword)
                    {
                        keywords.Add(token);
                    }
                }
            });
            return false; // continue traversal
        });

        return keywords;
    }

    /// <summary>Go: <c>func traverseWithoutCrossingFunction(node *ast.Node, sourceFile *ast.SourceFile, cb func(*ast.Node))</c>（documenthighlights.go:753-762）。</summary>
    internal static void TraverseWithoutCrossingFunction(Node node, SourceFile sourceFile, Action<Node> cb)
    {
        cb(node);
        if (!IsFunctionLike(node) && !IsClassLike(node) && !IsInterfaceDeclaration(node) && !IsModuleDeclaration(node) && !IsTypeAliasDeclaration(node) && !IsTypeNode(node))
        {
            node.ForEachChild(child =>
            {
                TraverseWithoutCrossingFunction(child, sourceFile, cb);
                return false; // continue traversal
            });
        }
    }

    /// <summary>Go: <c>func getModifierOccurrences(kind ast.Kind, node *ast.Node, sourceFile *ast.SourceFile) []*ast.Node</c>（documenthighlights.go:764-776）。</summary>
    internal static List<Node> GetModifierOccurrences(Kind kind, Node node, SourceFile sourceFile)
    {
        List<Node> result = [];

        var nodesToSearch = GetNodesToSearchForModifier(node, ModifierToFlag(kind));
        foreach (var n in nodesToSearch)
        {
            var modifier = FindModifier(n, kind);
            if (modifier is not null)
            {
                result.Add(modifier);
            }
        }

        return result;
    }

    /// <summary>Go: <c>func getNodesToSearchForModifier(declaration *ast.Node, modifierFlag ast.ModifierFlags) []*ast.Node</c>（documenthighlights.go:778-...）。</summary>
    internal static List<Node> GetNodesToSearchForModifier(Node declaration, ModifierFlags modifierFlag)
    {
        List<Node> result = [];

        var container = declaration.Parent;
        if (container is null)
        {
            return [];
        }

        // Types of node whose children might have modifiers.
        switch (container.Kind)
        {
            case Kind.ModuleBlock:
            case Kind.SourceFile:
            case Kind.Block:
            case Kind.CaseClause:
            case Kind.DefaultClause:
                // Container is either a class declaration or the declaration is a classDeclaration
                if ((modifierFlag & ModifierFlags.Abstract) != 0 && IsClassDeclaration(declaration))
                {
                    result.AddRange(declaration.Members() ?? []);
                    result.Add(declaration);
                    return result;
                }
                else
                {
                    result.AddRange(container.Statements() ?? []);
                    return result;
                }

            case Kind.Constructor:
            case Kind.MethodDeclaration:
            case Kind.FunctionDeclaration:
                // Parameters and, if inside a class, also class members
                result.AddRange(container.Parameters() ?? []);
                if (IsClassLike(container.Parent!))
                {
                    result.AddRange(container.Parent!.Members() ?? []);
                }

                return result;
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
            case Kind.InterfaceDeclaration:
            case Kind.TypeLiteral:
                var nodes = container.Members() ?? [];
                result.AddRange(nodes);
                // If we're an accessibility modifier, we're in an instance member and should search
                // the constructor's parameter list for instance members as well.
                if ((modifierFlag & (ModifierFlags.AccessibilityModifier | ModifierFlags.Readonly)) != 0)
                {
                    Node? constructor = null;

                    foreach (var member in nodes)
                    {
                        if (IsConstructorDeclaration(member))
                        {
                            constructor = member;
                            break;
                        }
                    }

                    if (constructor is not null)
                    {
                        result.AddRange(constructor.Parameters() ?? []);
                    }
                }
                else if ((modifierFlag & ModifierFlags.Abstract) != 0)
                {
                    result.Add(container);
                }

                return result;
            default:
                // Syntactically invalid positions or unsupported containers
                return [];
        }
    }

    /// <summary>Go: <c>func findModifier(node *ast.Node, kind ast.Kind) *ast.Node</c>（documenthighlights.go:...-796）。</summary>
    internal static Node? FindModifier(Node node, Kind kind)
    {
        foreach (var modifier in node.ModifierNodes() ?? [])
        {
            if (modifier.Kind == kind)
            {
                return modifier;
            }
        }

        return null;
    }
}

public sealed partial class LanguageService
{
    /// <summary>Go: <c>func (l *LanguageService) ProvideDocumentHighlights(ctx, documentUri, documentPosition) (lsproto.DocumentHighlightResponse, error)</c>（documenthighlights.go:19-33）。</summary>
    public (DocumentHighlightsOrNull Response, Exception? Error) ProvideDocumentHighlights(Context ctx, DocumentUri documentUri, Position documentPosition)
    {
        var (result, err) = ProvideDocumentHighlightsWorker(ctx, documentUri, documentPosition, null);
        if (err is not null)
        {
            return (new DocumentHighlightsOrNull(), err);
        }

        // Extract highlights for the current file only.
        List<DocumentHighlight?> documentHighlights = [];
        if (result.MultiDocumentHighlights is not null)
        {
            foreach (var mh in result.MultiDocumentHighlights)
            {
                if (mh is not null && mh.Uri == documentUri)
                {
                    documentHighlights.AddRange(mh.Highlights ?? []);
                }
            }
        }

        return (new DocumentHighlightsOrNull { DocumentHighlights = [.. documentHighlights] }, null);
    }

    /// <summary>Go: <c>func (l *LanguageService) ProvideMultiDocumentHighlights(ctx, documentUri, documentPosition, filesToSearch []lsproto.DocumentUri) (lsproto.CustomMultiDocumentHighlightResponse, error)</c>（documenthighlights.go:35-37）。</summary>
    public (MultiDocumentHighlightsOrNull Response, Exception? Error) ProvideMultiDocumentHighlights(Context ctx, DocumentUri documentUri, Position documentPosition, DocumentUri[] filesToSearch)
    {
        return ProvideDocumentHighlightsWorker(ctx, documentUri, documentPosition, filesToSearch);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideDocumentHighlightsWorker(ctx, documentUri, documentPosition, filesToSearch []lsproto.DocumentUri) (lsproto.MultiDocumentHighlightsOrNull, error)</c>（documenthighlights.go:39-50）。</summary>
    internal (MultiDocumentHighlightsOrNull Response, Exception? Error) ProvideDocumentHighlightsWorker(Context ctx, DocumentUri documentUri, Position documentPosition, DocumentUri[]? filesToSearch)
    {
        var (program, sourceFile) = GetProgramAndFile(documentUri);
        var positions = _converters!.FromLSPPositionForSourceFile(sourceFile, documentPosition, Feature.DocumentHighlights);
        List<MultiDocumentHighlightsOrNull> results = new(positions.Length);
        foreach (var mapped in positions)
        {
            if (mapped.Fidelity.IsSingleSegment())
            {
                results.Add(ProvideDocumentHighlightsAtPosition(ctx, documentUri, mapped.Position, program, mapped.Script, filesToSearch));
            }
        }

        return (Ls.CombineMultiDocumentHighlights(results), null);
    }

    /// <summary>Go: <c>func (l *LanguageService) provideDocumentHighlightsAtPosition(...) lsproto.MultiDocumentHighlightsOrNull</c>（documenthighlights.go:52-103）。</summary>
    internal MultiDocumentHighlightsOrNull ProvideDocumentHighlightsAtPosition(Context ctx, DocumentUri documentUri, int position, Program program, SourceFile sourceFile, DocumentUri[]? filesToSearch)
    {
        var node = GetTouchingPropertyName(sourceFile, position);

        // Cheap JSX check before resolving files to search.
        if (node.Parent is not null && (node.Parent.Kind == Kind.JsxClosingElement || (node.Parent.Kind == Kind.JsxOpeningElement && ReferenceEquals(node.Parent.TagName(), node))))
        {
            Node? openingElement = null;
            Node? closingElement = null;
            if (IsJsxElement(node.Parent.Parent!))
            {
                openingElement = node.Parent.Parent!.AsJsxElement()!.OpeningElement;
                closingElement = node.Parent.Parent!.AsJsxElement()!.ClosingElement;
            }

            List<DocumentHighlight?> highlights = [];
            var kind = DocumentHighlightKind.Read;
            if (openingElement is not null)
            {
                var (lspRange, fidelity) = CreateLspRangeFromNodeForFeature(openingElement, sourceFile, Feature.DocumentHighlights);
                if (!fidelity.IsNone())
                {
                    highlights.Add(new DocumentHighlight { Range = lspRange, Kind = kind });
                }
            }

            if (closingElement is not null)
            {
                var (lspRange, fidelity) = CreateLspRangeFromNodeForFeature(closingElement, sourceFile, Feature.DocumentHighlights);
                if (!fidelity.IsNone())
                {
                    highlights.Add(new DocumentHighlight { Range = lspRange, Kind = kind });
                }
            }

            List<MultiDocumentHighlight> multiHighlights =
            [
                new MultiDocumentHighlight { Uri = documentUri, Highlights = [.. highlights] },
            ];
            return new MultiDocumentHighlightsOrNull
            {
                MultiDocumentHighlights = [.. multiHighlights],
            };
        }

        // Resolve the source files to search, deduplicating by file name.
        List<SourceFile> sourceFiles = [];
        var seenFiles = new Set<string>(filesToSearch?.Length ?? 0);
        foreach (var uri in filesToSearch ?? [])
        {
            var fileName = uri.FileName();
            if (!seenFiles.AddIfAbsent(fileName))
            {
                continue;
            }

            var sf = program.GetSourceFile(fileName);
            if (sf is not null)
            {
                sourceFiles.Add(sf);
            }
        }

        if (sourceFiles.Count == 0)
        {
            sourceFiles.Add(sourceFile);
        }

        var multiHighlights2 = GetSemanticDocumentHighlights(ctx, position, node, program, [.. sourceFiles]);
        if (multiHighlights2.Count == 0)
        {
            // Fall back to syntactic highlights for the current file only.
            var syntacticHighlights = GetSyntacticDocumentHighlights(node, sourceFile);
            if (syntacticHighlights.Count > 0)
            {
                multiHighlights2 =
                [
                    new MultiDocumentHighlight { Uri = documentUri, Highlights = [.. syntacticHighlights] },
                ];
            }
        }

        return new MultiDocumentHighlightsOrNull { MultiDocumentHighlights = [.. multiHighlights2] };
    }

    /// <summary>Go: <c>func (l *LanguageService) getSemanticDocumentHighlights(ctx, position int, node *ast.Node, program *compiler.Program, sourceFiles []*ast.SourceFile) []*lsproto.MultiDocumentHighlight</c>（documenthighlights.go:136-168）。</summary>
    internal List<MultiDocumentHighlight> GetSemanticDocumentHighlights(Context ctx, int position, Node node, Program program, SourceFile[] sourceFiles)
    {
        var options = new RefOptions { _use = ReferenceUse.None };
        var referenceEntries = GetReferencedSymbolsForNode(ctx, position, node, program, sourceFiles, options);
        if (referenceEntries is null)
        {
            return [];
        }

        // Group highlights by file
        Dictionary<string, List<DocumentHighlight?>> fileHighlights = [];
        foreach (var entry in referenceEntries)
        {
            foreach (var reference in entry._references ?? [])
            {
                var (fileName, highlight) = ToDocumentHighlight(reference);
                if (highlight is null)
                {
                    continue;
                }

                if (!fileHighlights.TryGetValue(fileName, out var list))
                {
                    list = [];
                    fileHighlights[fileName] = list;
                }

                list.Add(highlight);
            }
        }

        List<MultiDocumentHighlight> result = [];
        foreach (var sf in sourceFiles)
        {
            var fileName = sf.OriginalFileName();
            if (fileHighlights.TryGetValue(fileName, out var highlights))
            {
                result.Add(new MultiDocumentHighlight
                {
                    Uri = FileNameToDocumentURI(fileName),
                    Highlights = [.. highlights],
                });
            }
        }

        return result;
    }

    /// <summary>Go: <c>func (l *LanguageService) toDocumentHighlight(entry *ReferenceEntry) (string, *lsproto.DocumentHighlight)</c>（documenthighlights.go:170-194）。</summary>
    internal (string FileName, DocumentHighlight? Highlight) ToDocumentHighlight(ReferenceEntry entry)
    {
        entry = ResolveEntry(entry);
        var fileName = entry._sourceFile!.OriginalFileName();

        var kind = DocumentHighlightKind.Read;
        var (lspRange, ok) = GetRangeOfEntryForFeature(entry, Feature.DocumentHighlights);
        if (!ok)
        {
            return (fileName, null);
        }

        if (entry._kind == EntryKind.Range)
        {
            return (fileName, new DocumentHighlight
            {
                Range = lspRange,
                Kind = kind,
            });
        }

        // Determine write access for node references.
        if (IsWriteAccessForReference(entry._node!))
        {
            kind = DocumentHighlightKind.Write;
        }

        var dh = new DocumentHighlight
        {
            Range = lspRange,
            Kind = kind,
        };

        return (fileName, dh);
    }

    /// <summary>Go: <c>func (l *LanguageService) getSyntacticDocumentHighlights(node *ast.Node, sourceFile *ast.SourceFile) []*lsproto.DocumentHighlight</c>（documenthighlights.go:196-253）。</summary>
    internal List<DocumentHighlight?> GetSyntacticDocumentHighlights(Node node, SourceFile sourceFile)
    {
        switch (node.Kind)
        {
            case Kind.IfKeyword:
            case Kind.ElseKeyword:
                if (IsIfStatement(node.Parent!))
                {
                    return GetIfElseOccurrences(node.Parent!.AsIfStatement()!, sourceFile);
                }

                return [];
            case Kind.ReturnKeyword:
                return UseParent(node.Parent, IsReturnStatement, Ls.GetReturnOccurrences, sourceFile);
            case Kind.ThrowKeyword:
                return UseParent(node.Parent, IsThrowStatement, Ls.GetThrowOccurrences, sourceFile);
            case Kind.TryKeyword:
            case Kind.CatchKeyword:
            case Kind.FinallyKeyword:
                Node? tryStatement;
                if (node.Kind == Kind.CatchKeyword)
                {
                    tryStatement = node.Parent!.Parent;
                }
                else
                {
                    tryStatement = node.Parent;
                }

                return UseParent(tryStatement, IsTryStatement, Ls.GetTryCatchFinallyOccurrences, sourceFile);
            case Kind.SwitchKeyword:
                return UseParent(node.Parent, IsSwitchStatement, Ls.GetSwitchCaseDefaultOccurrences, sourceFile);
            case Kind.CaseKeyword:
            case Kind.DefaultKeyword:
                if (IsDefaultClause(node.Parent!) || IsCaseClause(node.Parent!))
                {
                    return UseParent(node.Parent!.Parent!.Parent, IsSwitchStatement, Ls.GetSwitchCaseDefaultOccurrences, sourceFile);
                }

                return [];
            case Kind.BreakKeyword:
            case Kind.ContinueKeyword:
                return UseParent(node.Parent, IsBreakOrContinueStatement, Ls.GetBreakOrContinueStatementOccurrences, sourceFile);
            case Kind.ForKeyword:
            case Kind.WhileKeyword:
            case Kind.DoKeyword:
                return UseParent(node.Parent, n => IsIterationStatement(n, true), Ls.GetLoopBreakContinueOccurrences, sourceFile);
            case Kind.ConstructorKeyword:
                return GetFromAllDeclarations(IsConstructorDeclaration, [Kind.ConstructorKeyword], node, sourceFile);
            case Kind.GetKeyword:
            case Kind.SetKeyword:
                return GetFromAllDeclarations(IsAccessor, [Kind.GetKeyword, Kind.SetKeyword], node, sourceFile);
            case Kind.AwaitKeyword:
                return UseParent(node.Parent, IsAwaitExpression, Ls.GetAsyncAndAwaitOccurrences, sourceFile);
            case Kind.AsyncKeyword:
                return HighlightSpans(Ls.GetAsyncAndAwaitOccurrences(node, sourceFile), sourceFile);
            case Kind.YieldKeyword:
                return HighlightSpans(Ls.GetYieldOccurrences(node, sourceFile), sourceFile);
            case Kind.InKeyword:
            case Kind.OutKeyword:
                return [];
            default:
                if (IsModifierKind(node.Kind) && (IsDeclaration(node.Parent) || IsVariableStatement(node.Parent!)))
                {
                    return HighlightSpans(Ls.GetModifierOccurrences(node.Kind, node.Parent!, sourceFile), sourceFile);
                }

                return [];
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) useParent(node *ast.Node, nodeTest func(*ast.Node) bool, getNodes func(*ast.Node, *ast.SourceFile) []*ast.Node, sourceFile *ast.SourceFile) []*lsproto.DocumentHighlight</c>（documenthighlights.go:255-260）。</summary>
    internal List<DocumentHighlight?> UseParent(Node? node, Func<Node, bool> nodeTest, Func<Node, SourceFile, List<Node>> getNodes, SourceFile sourceFile)
    {
        if (nodeTest(node!))
        {
            return HighlightSpans(getNodes(node!, sourceFile), sourceFile);
        }

        return [];
    }

    /// <summary>Go: <c>func (l *LanguageService) highlightSpans(nodes []*ast.Node, sourceFile *ast.SourceFile) []*lsproto.DocumentHighlight</c>（documenthighlights.go:262-276）。</summary>
    internal List<DocumentHighlight?> HighlightSpans(List<Node> nodes, SourceFile sourceFile)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        List<DocumentHighlight?> highlights = [];
        var kind = DocumentHighlightKind.Read;
        foreach (var node in nodes)
        {
            if (node is not null)
            {
                var (lspRange, fidelity) = CreateLspRangeFromNodeForFeature(node, sourceFile, Feature.DocumentHighlights);
                if (!fidelity.IsNone())
                {
                    highlights.Add(new DocumentHighlight { Range = lspRange, Kind = kind });
                }
            }
        }

        return highlights;
    }

    /// <summary>Go: <c>func (l *LanguageService) getFromAllDeclarations(nodeTest func(*ast.Node) bool, keywords []ast.Kind, node *ast.Node, sourceFile *ast.SourceFile) []*lsproto.DocumentHighlight</c>（documenthighlights.go:278-301）。</summary>
    internal List<DocumentHighlight?> GetFromAllDeclarations(Func<Node, bool> nodeTest, Kind[] keywords, Node node, SourceFile sourceFile)
    {
        return UseParent(node.Parent, nodeTest, (decl, sf) =>
        {
            List<Node> symbolDecls = [];
            if (CanHaveSymbol(decl))
            {
                var symbol = decl.Symbol;
                if (symbol is not null)
                {
                    foreach (var d in symbol.Declarations ?? [])
                    {
                        if (nodeTest(d))
                        {
                            foreach (var c in Ls.GetChildrenFromNonJSDocNode(d, sourceFile) ?? [])
                            {
                                foreach (var k in keywords)
                                {
                                    if (c.Kind == k)
                                    {
                                        symbolDecls.Add(c);
                                        goto outer;
                                    }
                                }
                            }
                        }

                        outer:;
                    }
                }
            }

            return symbolDecls;
        }, sourceFile);
    }

    /// <summary>Go: <c>func (l *LanguageService) getIfElseOccurrences(ifStatement *ast.IfStatement, sourceFile *ast.SourceFile) []*lsproto.DocumentHighlight</c>（documenthighlights.go:303-...）。</summary>
    internal List<DocumentHighlight?> GetIfElseOccurrences(IfStatement ifStatement, SourceFile sourceFile)
    {
        var keywords = Ls.GetIfElseKeywords(ifStatement, sourceFile);
        var kind = DocumentHighlightKind.Read;
        List<DocumentHighlight?> highlights = [];

        // We'd like to highlight else/ifs together if they are only separated by whitespace
        // (i.e. the keywords are separated by no comments, no newlines).
        for (var i = 0; i < keywords.Count; i++)
        {
            if (keywords[i].Kind == Kind.ElseKeyword && i < keywords.Count - 1)
            {
                var elseKeyword = keywords[i];
                var ifKeyword = keywords[i + 1]; // this *should* always be an 'if' keyword.
                var shouldCombine = true;

                // Avoid recalculating getStart() by iterating backwards.
                var ifTokenStart = GetTokenPosOfNode(ifKeyword, sourceFile, false);
                if (ifTokenStart < 0)
                {
                    ifTokenStart = ifKeyword.Pos();
                }

                for (var j = ifTokenStart - 1; j >= elseKeyword.End(); j--)
                {
                    if (!IsWhiteSpaceSingleLine(sourceFile.Text()[j]))
                    {
                        shouldCombine = false;
                        break;
                    }
                }

                if (shouldCombine)
                {
                    var (lspRange, fidelity) = CreateLspRangeFromBounds(SkipTrivia(sourceFile.Text(), elseKeyword.Pos()), ifKeyword.End(), sourceFile);
                    if (!fidelity.IsNone())
                    {
                        highlights.Add(new DocumentHighlight { Range = lspRange, Kind = kind });
                    }

                    i++; // skip the next keyword
                    continue;
                }
            }

            // Ordinary case: just highlight the keyword.
            {
                var (lspRange, fidelity) = CreateLspRangeFromNodeForFeature(keywords[i], sourceFile, Feature.DocumentHighlights);
                if (!fidelity.IsNone())
                {
                    highlights.Add(new DocumentHighlight { Range = lspRange, Kind = kind });
                }
            }
        }

        return highlights;
    }

    // ── 以下成员来自 findallreferences.go（跨文件 helper 就地落地，见文件头；G6 不得重复声明）──

    /// <summary>Go: <c>func (l *LanguageService) resolveEntrySource(entry *ReferenceEntry)</c>（findallreferences.go:187-196）。</summary>
    internal void ResolveEntrySource(ReferenceEntry entry)
    {
        if (entry._sourceFile is null)
        {
            Debug.Assert(entry._node is not null, "reference entry must have a node or source file");
            entry._sourceFile = GetSourceFileOfNode(entry._node);
        }

        if (entry._textRange is null)
        {
            var textRange = Ls.GetRangeOfNode(entry._node!, entry._sourceFile, null /*endNode*/);
            entry._textRange = textRange;
        }
    }

    /// <summary>Go: <c>func (l *LanguageService) resolveEntry(entry *ReferenceEntry) *ReferenceEntry</c>（findallreferences.go:198-207）。</summary>
    internal ReferenceEntry ResolveEntry(ReferenceEntry entry)
    {
        ResolveEntrySource(entry);
        if (entry._lspRange is null)
        {
            var (location, fidelity) = SourceFileRangeToLSPLocation(entry._sourceFile!, entry._textRange!.Value);
            entry._lspRange = location;
            entry._unmappable = !fidelity.IsSingleSegment();
        }

        return entry;
    }

    /// <summary>Go: <c>func (l *LanguageService) getLocationOfEntryForFeature(entry *ReferenceEntry, feature spanmap.Feature) (lsproto.Location, bool)</c>（findallreferences.go:180-185）。</summary>
    internal (Location Location, bool Ok) GetLocationOfEntryForFeature(ReferenceEntry entry, Feature feature)
    {
        ResolveEntrySource(entry);
        var (location, fidelity) = SourceFileRangeToLSPLocationForFeature(entry._sourceFile!, entry._textRange!.Value, feature);
        return (location, fidelity.IsSingleSegment());
    }

    /// <summary>Go: <c>func (l *LanguageService) getRangeOfEntryForFeature(entry *ReferenceEntry, feature spanmap.Feature) (lsproto.Range, bool)</c>（findallreferences.go:172-175）。</summary>
    internal (Range Range, bool Ok) GetRangeOfEntryForFeature(ReferenceEntry entry, Feature feature)
    {
        var (location, ok) = GetLocationOfEntryForFeature(entry, feature);
        return (location.Range, ok);
    }

    /// <summary>Go: <c>func (l *LanguageService) getRangeOfEntry(entry *ReferenceEntry) lsproto.Range</c>（findallreferences.go:165-167）。</summary>
    internal Range GetRangeOfEntry(ReferenceEntry entry)
    {
        return ResolveEntry(entry)._lspRange!.Range;
    }

    /// <summary>Go: <c>func (l *LanguageService) getFileNameOfEntry(entry *ReferenceEntry) lsproto.DocumentUri</c>（findallreferences.go:169-171）。</summary>
    internal DocumentUri GetFileNameOfEntry(ReferenceEntry entry)
    {
        return ResolveEntry(entry)._lspRange!.Uri;
    }
}
