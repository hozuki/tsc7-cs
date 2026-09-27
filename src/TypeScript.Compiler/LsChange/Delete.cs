// ported from: tsc/internal/ls/change/delete.go:1-263
//
// 与 Go 的偏离（逐条）：
// 1) Go 的包级函数 `deleteDeclaration`/`deleteDefaultImport`/`deleteImportBinding`/`deleteVariableDeclaration`/
//    `deleteNode`/`deleteNodeInList`/`positionsAreOnSameLine`/`hasJSDocNodes` → 与 package 同名的静态类
//    `LsChange` 的 `internal static` 成员（§2），并显式传入 Tracker；调用点用
//    `using static TypeScript.LsChange.LsChange;` 裸名调用，文本与 Go 一致。`StartPositionToDeleteNodeInList`/
//    `EndPositionToDeleteNodeInList` 是 Go 里挂在 Tracker 上的方法，落在 `partial class Tracker` 里。
// 2) Go 的 `(T, bool)`/`panic`/指针比较/命名空间遮蔽同 Tracker.cs 文件头第 6、8、9 条。
// 3) `Nil` 切片 → 空数组（`?? []`）；`As*()` 访问器沿用生成物的 Go 名
//    （`AsVariableDeclarationList`/`AsPropertySignatureDeclaration`/`AsParameterDeclaration`）。
// 4) `debug.Assert(previousToken != nil, ...)` → `Debug.Assert(previousToken != null, ...)`（Go 的 `Assert` 同名同义）；
//    Go 在断言后直接解引用指针，C# 需要显式 `!`（分析器看不到 `Debug.Assert` 的后置条件）。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.LsUtil;
using TypeScript.Scanner;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Core.Core;
using static TypeScript.Format.Format;
using static TypeScript.LsChange.LsChange;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.LsChange;

public sealed partial class Tracker
{
    /// <summary>startPositionToDeleteNodeInList finds the first non-whitespace position in the leading trivia of the node</summary>
    internal int StartPositionToDeleteNodeInList(SourceFile sourceFile, Node node)
    {
        var start = GetAdjustedStartPosition(sourceFile, node, LeadingTriviaOption.IncludeAll, false);
        return SkipTriviaEx(sourceFile.Text(), start, new SkipTriviaOptions { StopAfterLineBreak = false, StopAtComments = true });
    }

    internal int EndPositionToDeleteNodeInList(SourceFile sourceFile, Node node, Node? prevNode, Node nextNode)
    {
        var end = StartPositionToDeleteNodeInList(sourceFile, nextNode);
        if (prevNode == null || PositionsAreOnSameLine(GetAdjustedEndPosition(sourceFile, node, TrailingTriviaOption.Include), end, sourceFile))
        {
            return end;
        }

        var token = FindPrecedingToken(sourceFile, GetStartOfNode(nextNode, sourceFile, false));
        if (IsSeparator(node, token))
        {
            var prevToken = FindPrecedingToken(sourceFile, GetStartOfNode(node, sourceFile, false));
            if (IsSeparator(prevNode, prevToken))
            {
                var pos = SkipTriviaEx(sourceFile.Text(), token!.End(), new SkipTriviaOptions { StopAfterLineBreak = true, StopAtComments = true });
                if (PositionsAreOnSameLine(GetStartOfNode(prevToken!, sourceFile, false), GetStartOfNode(token, sourceFile, false), sourceFile))
                {
                    if (pos > 0 && IsLineBreak(sourceFile.Text()[pos - 1]))
                    {
                        return pos - 1;
                    }

                    return pos;
                }

                if (IsLineBreak(sourceFile.Text()[pos]))
                {
                    return pos;
                }
            }
        }

        return end;
    }
}

public static partial class LsChange
{
    /// <summary>deleteDeclaration deletes a node with smart handling for different node types.
    /// This handles special cases like import specifiers in lists, parameters, etc.</summary>
    internal static void DeleteDeclaration(Tracker t, Dictionary<Node, bool> deletedNodesInLists, SourceFile sourceFile, Node node)
    {
        switch (node.Kind)
        {
            case Kind.Parameter:
                var oldFunction = node.Parent!;
                if (oldFunction.Kind == Kind.ArrowFunction &&
                    (oldFunction.AsArrowFunction()!.Parameters?.Nodes ?? []).Length == 1 &&
                    FindChildOfKind(oldFunction, Kind.OpenParenToken, sourceFile) == null)
                {
                    // Lambdas with exactly one parameter are special because, after removal, there
                    // must be an empty parameter list (i.e. `()`) and this won't necessarily be the
                    // case if the parameter is simply removed (e.g. in `x => 1`).
                    t.ReplaceTextRangeWithText(sourceFile, t.GetAdjustedRange(sourceFile, node, node, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include), "()");
                }
                else
                {
                    DeleteNodeInList(t, deletedNodesInLists, sourceFile, node);
                }

                break;

            case Kind.ImportDeclaration:
            case Kind.ImportEqualsDeclaration:
                var imports = sourceFile.Imports();
                var isFirstImport = imports.Length > 0 && ReferenceEquals(node, imports[0].Parent) ||
                    ReferenceEquals(node, Find(sourceFile.Statements!.Nodes, s => IsAnyImportSyntax(s)));
                // For first import, leave header comment in place, otherwise only delete JSDoc comments
                var leadingTrivia = LeadingTriviaOption.StartLine;
                if (isFirstImport)
                {
                    leadingTrivia = LeadingTriviaOption.Exclude;
                }
                else if (HasJSDocNodes(node))
                {
                    leadingTrivia = LeadingTriviaOption.JSDoc;
                }

                DeleteNode(t, sourceFile, node, leadingTrivia, TrailingTriviaOption.Include);
                break;

            case Kind.BindingElement:
                var pattern = node.Parent!;
                var preserveComma = pattern.Kind == Kind.ArrayBindingPattern &&
                    !ReferenceEquals(node, (pattern.AsBindingPattern()!.Elements?.Nodes ?? [])[^1]);
                if (preserveComma)
                {
                    DeleteNode(t, sourceFile, node, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Exclude);
                }
                else
                {
                    DeleteNodeInList(t, deletedNodesInLists, sourceFile, node);
                }

                break;

            case Kind.VariableDeclaration:
                DeleteVariableDeclaration(t, deletedNodesInLists, sourceFile, node);
                break;

            case Kind.TypeParameter:
                DeleteNodeInList(t, deletedNodesInLists, sourceFile, node);
                break;

            case Kind.ImportSpecifier:
                var namedImports = node.Parent!;
                if ((namedImports.AsNamedImports()!.Elements?.Nodes ?? []).Length == 1)
                {
                    DeleteImportBinding(t, sourceFile, namedImports);
                }
                else
                {
                    DeleteNodeInList(t, deletedNodesInLists, sourceFile, node);
                }

                break;

            case Kind.NamespaceImport:
                DeleteImportBinding(t, sourceFile, node);
                break;

            case Kind.SemicolonToken:
                DeleteNode(t, sourceFile, node, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Exclude);
                break;

            case Kind.TypeKeyword:
                // For type keyword in import clauses, we need to delete the keyword and any trailing space
                // The trailing space is part of the next token's leading trivia, so we include it
                DeleteNode(t, sourceFile, node, LeadingTriviaOption.Exclude, TrailingTriviaOption.Include);
                break;

            case Kind.FunctionKeyword:
                DeleteNode(t, sourceFile, node, LeadingTriviaOption.Exclude, TrailingTriviaOption.Include);
                break;

            case Kind.ClassDeclaration:
            case Kind.FunctionDeclaration:
                var leadingTriviaForDeclaration = LeadingTriviaOption.StartLine;
                if (HasJSDocNodes(node))
                {
                    leadingTriviaForDeclaration = LeadingTriviaOption.JSDoc;
                }

                DeleteNode(t, sourceFile, node, leadingTriviaForDeclaration, TrailingTriviaOption.Include);
                break;

            default:
                if (node.Parent == null)
                {
                    // a misbehaving client can reach here with the SourceFile node
                    DeleteNode(t, sourceFile, node, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
                }
                else if (node.Parent.Kind == Kind.ImportClause && ReferenceEquals(node.Parent.AsImportClause()!.Name(), node))
                {
                    DeleteDefaultImport(t, sourceFile, node.Parent);
                }
                else if (node.Parent.Kind == Kind.CallExpression && Array.IndexOf(node.Parent.AsCallExpression()!.Arguments?.Nodes ?? [], node) >= 0)
                {
                    DeleteNodeInList(t, deletedNodesInLists, sourceFile, node);
                }
                else
                {
                    DeleteNode(t, sourceFile, node, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
                }

                break;
        }
    }

    internal static void DeleteDefaultImport(Tracker t, SourceFile sourceFile, Node importClause)
    {
        var clause = importClause.AsImportClause()!;
        if (clause.NamedBindings == null)
        {
            // Delete the whole import
            DeleteNode(t, sourceFile, importClause.Parent!, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
        }
        else
        {
            // import |d,| * as ns from './file'
            var name = clause.Name()!;
            var start = GetStartOfNode(name, sourceFile, false);
            var nextToken = GetTokenAtPosition(sourceFile, name.End());
            if (nextToken != null && nextToken.Kind == Kind.CommaToken)
            {
                // shift first non-whitespace position after comma to the start position of the node
                var end = SkipTriviaEx(sourceFile.Text(), nextToken.End(), new SkipTriviaOptions { StopAfterLineBreak = false, StopAtComments = true });
                t.ReplaceTextRangeWithText(sourceFile, NewTextRange(start, end), "");
            }
            else
            {
                DeleteNode(t, sourceFile, name, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
            }
        }
    }

    internal static void DeleteImportBinding(Tracker t, SourceFile sourceFile, Node node)
    {
        var importClause = node.Parent!.AsImportClause()!;
        if (importClause.Name() != null)
        {
            // Delete named imports while preserving the default import
            // import d|, * as ns| from './file'
            // import d|, { a }| from './file'
            Node? previousToken = GetTokenAtPosition(sourceFile, node.Pos() - 1);
            Debug.Assert(previousToken != null, "previousToken should not be nil");
            var start = GetStartOfNode(previousToken!, sourceFile, false);
            t.ReplaceTextRangeWithText(sourceFile, NewTextRange(start, node.End()), "");
        }
        else
        {
            // Delete the entire import declaration
            // |import * as ns from './file'|
            // |import { a } from './file'|
            var importDecl = FindAncestorKind(node, Kind.ImportDeclaration);
            Debug.Assert(importDecl != null, "importDecl should not be nil");
            DeleteNode(t, sourceFile, importDecl!, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
        }
    }

    internal static void DeleteVariableDeclaration(Tracker t, Dictionary<Node, bool> deletedNodesInLists, SourceFile sourceFile, Node node)
    {
        var parent = node.Parent!;

        if (parent.Kind == Kind.CatchClause)
        {
            // TODO: There's currently no unused diagnostic for this, could be a suggestion
            var openParen = FindChildOfKind(parent, Kind.OpenParenToken, sourceFile);
            var closeParen = FindChildOfKind(parent, Kind.CloseParenToken, sourceFile);
            Debug.Assert(openParen != null && closeParen != null, "catch clause should have parens");
            t.DeleteNodeRange(sourceFile, openParen!, closeParen!, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
            return;
        }

        if ((parent.AsVariableDeclarationList()!.Declarations?.Nodes ?? []).Length != 1)
        {
            DeleteNodeInList(t, deletedNodesInLists, sourceFile, node);
            return;
        }

        var gp = parent.Parent!;
        switch (gp.Kind)
        {
            case Kind.ForOfStatement:
            case Kind.ForInStatement:
                t.ReplaceNode(sourceFile, node, t.NodeFactory.NewObjectLiteralExpression(t.NodeFactory.NewNodeList([]), false), null);
                break;

            case Kind.ForStatement:
                DeleteNode(t, sourceFile, parent, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
                break;

            case Kind.VariableStatement:
                var leadingTrivia = LeadingTriviaOption.StartLine;
                if (HasJSDocNodes(gp))
                {
                    leadingTrivia = LeadingTriviaOption.JSDoc;
                }

                DeleteNode(t, sourceFile, gp, leadingTrivia, TrailingTriviaOption.Include);
                break;

            default:
                Debug.Fail("Unexpected grandparent kind: " + gp.Kind.String());
                break;
        }
    }

    /// <summary>deleteNode deletes a node with the specified trivia options.
    /// Warning: This deletes comments too.</summary>
    internal static void DeleteNode(Tracker t, SourceFile sourceFile, Node node, LeadingTriviaOption leadingTrivia, TrailingTriviaOption trailingTrivia)
    {
        var startPosition = t.GetAdjustedStartPosition(sourceFile, node, leadingTrivia, false);
        var endPosition = t.GetAdjustedEndPosition(sourceFile, node, trailingTrivia);
        t.ReplaceTextRangeWithText(sourceFile, NewTextRange(startPosition, endPosition), "");
    }

    internal static void DeleteNodeInList(Tracker t, Dictionary<Node, bool> deletedNodesInLists, SourceFile sourceFile, Node node)
    {
        var containingList = GetContainingList(node, sourceFile);
        Debug.Assert(containingList != null, "containingList should not be nil");
        var listNodes = containingList!.Nodes ?? [];
        var index = Array.IndexOf(listNodes, node);
        Debug.Assert(index != -1, "node should be in containing list");

        if (listNodes.Length == 1)
        {
            DeleteNode(t, sourceFile, node, LeadingTriviaOption.IncludeAll, TrailingTriviaOption.Include);
            return;
        }

        // Note: We will only delete a comma *after* a node. This will leave a trailing comma if we delete the last node.
        // That's handled in the end by finishTrailingCommaAfterDeletingNodesInList.
        Debug.Assert(!deletedNodesInLists.ContainsKey(node), "Deleting a node twice");
        deletedNodesInLists[node] = true;

        var startPos = t.StartPositionToDeleteNodeInList(sourceFile, node);
        int endPos;
        if (index == listNodes.Length - 1)
        {
            endPos = t.GetAdjustedEndPosition(sourceFile, node, TrailingTriviaOption.None);
        }
        else
        {
            Node? prevNode = null;
            if (index > 0)
            {
                prevNode = listNodes[index - 1];
            }

            endPos = t.EndPositionToDeleteNodeInList(sourceFile, node, prevNode, listNodes[index + 1]);
        }

        t.ReplaceTextRangeWithText(sourceFile, NewTextRange(startPos, endPos), "");
    }

    internal static bool PositionsAreOnSameLine(int pos1, int pos2, SourceFile sourceFile)
    {
        return GetLineStartPositionForPosition(pos1, sourceFile) == GetLineStartPositionForPosition(pos2, sourceFile);
    }

    /// <summary>hasJSDocNodes checks if a node has JSDoc comments</summary>
    internal static bool HasJSDocNodes(Node? node)
    {
        if (node == null)
        {
            return false;
        }

        // nil is ok for JSDoc - it will return empty slice if not available
        var jsdocs = node.JSDoc(null);
        return jsdocs != null && jsdocs.Length > 0;
    }
}
