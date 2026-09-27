// ported from: internal/printer/printer.go:4522-4725（catch/属性赋值/枚举成员/JSDoc/源文件头/helper 表）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) **`emitSourceFile` 落成 `EmitSourceFileWorker`**：Go 的同名导出函数 `EmitSourceFile` 已经落在
//    Printer.Emit.cs（返回 string 的包装，Go 源 5051-5053），而 C# 不允许仅返回类型不同的同名同参成员
//    （CS0111），所以未导出的那个必须换名。随之**调用点**（Printer.Emit.cs 的
//    `case Kind.SourceFile`）必须从 `EmitSourceFile(node.AsSourceFile()!)` 改成
//    `EmitSourceFileWorker(node.AsSourceFile()!)` —— 现在那里绑的是返回 string 的包装（语句位置丢弃返回值），
//    语义不对。其余 11 个成员的名字与 Go 的 PascalCase 化名字一致，无需调用点改动。
// 2) `slices.Clone(helpers)` + `slices.SortStableFunc(helpers, compareEmitHelpers)` → `new List<EmitHelper>(…)`
//    + 本文件的 `SortEmitHelpersStable`（与 Ast/Diagnostic.cs、Vfs/VfsMatch/VfsMatch.cs 里 `slices.SortStableFunc`
//    的落地方式一致：Array.Sort 的下标兜底；`List.Sort`/`Array.Sort` 都是不稳定排序，直接用会让同优先级
//    helper 的相对顺序偏离 Go）。注意 `GetEmitHelpers` 直接返回 EmitContext 内部数组，克隆不可省（Go 的
//    slices.Clone 同样是拷贝）。
// 3) `fmt.Sprintf` 只用于拼 `/// <reference …>` → 内插字符串（`%s`/`%v` 逐个对应）。
// 4) Go 的 `p.writer.IsAtStartOfLine()` → 直接读未导出的 `_writer` 字段（Printer.cs 文件头第 3 条）。
// 5) `panic("not implemented")`（`emitJSDocNode`，Go 侧本就未实现）→ `Debug.Fail("not implemented")`。
// 6) `core.ScriptKindJSON` → `ScriptKind.JSON`；`core.ResolutionModeNone`/`core.ResolutionModeESM` → Core 的
//    `ModuleKind` 常量（`ResolutionMode` 在 C# 里没有别名，见 Core/CompilerOptions.cs 的注释）。
// 7) Go 对 nil 切片做 `range`/`len` 是合法的 → C# 的 `NodeList.Nodes`/`FileReference[]` 可空，显式提前返回
//    （`emitPrologueDirectives`、`emitDirective`）。
// 8) `savedCurrentSourceFile`/`savedCommentsDisabled` → `_currentSourceFile`/`_commentsDisabled`（同 partial 类，
//    private 可见）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    internal void EmitCatchClause(CatchClause node)
    {
        var state = EnterNode(node.AsNode());
        var openParenPos = EmitToken(Kind.CatchKeyword, node.Pos(), WriteKind.Keyword, node.AsNode());
        WriteSpace();

        if (node.VariableDeclaration is not null)
        {
            EmitToken(Kind.OpenParenToken, openParenPos, WriteKind.Punctuation, node.AsNode());
            EmitVariableDeclaration(node.VariableDeclaration.AsVariableDeclaration()!);
            EmitToken(Kind.CloseParenToken, node.VariableDeclaration.End(), WriteKind.Punctuation, node.AsNode());
            WriteSpace();
        }

        EmitBlock(node.Block!.AsBlock()!);
        ExitNode(node.AsNode(), state);
    }

    //
    // Property assignments
    //

    internal void EmitPropertyAssignment(PropertyAssignment node)
    {
        var state = EnterNode(node.AsNode());
        EmitPropertyName(node.Name());
        WritePunctuation(":");
        WriteSpace();
        // This is to ensure that we emit comment in the following case:
        //      For example:
        //          obj = {
        //              id: /*comment1*/ ()=>void
        //          }
        // "comment1" is not considered to be leading comment for node.initializer
        // but rather a trailing comment on the previous node.
        var initializer = node.Initializer!;
        if ((_emitContext.EmitFlags(initializer) & EmitFlags.NoLeadingComments) == 0)
        {
            var commentRange = _emitContext.CommentRange(initializer);
            EmitTrailingComments(commentRange.Pos(), CommentSeparator.After);
        }
        EmitExpression(initializer, OperatorPrecedenceDisallowComma);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitShorthandPropertyAssignment(ShorthandPropertyAssignment node)
    {
        var state = EnterNode(node.AsNode());
        EmitPropertyName(node.Name());
        if (node.ObjectAssignmentInitializer is not null)
        {
            WriteSpace();
            WritePunctuation("=");
            WriteSpace();
            EmitExpression(node.ObjectAssignmentInitializer, OperatorPrecedenceDisallowComma);
        }
        ExitNode(node.AsNode(), state);
    }

    internal void EmitSpreadAssignment(SpreadAssignment node)
    {
        var state = EnterNode(node.AsNode());
        if (node.Expression is not null)
        {
            EmitToken(Kind.DotDotDotToken, node.Pos(), WriteKind.Punctuation, node.AsNode());
            EmitExpression(node.Expression, OperatorPrecedenceDisallowComma);
        }
        ExitNode(node.AsNode(), state);
    }

    //
    // Enum
    //

    internal void EmitEnumMember(EnumMember node)
    {
        var state = EnterNode(node.AsNode());
        EmitPropertyName(node.Name());
        EmitInitializer(node.Initializer, node.Name()!.End(), node.AsNode());
        ExitNode(node.AsNode(), state);
    }

    internal void EmitEnumMemberNode(Node node)
    {
        EmitEnumMember(node.AsEnumMember()!);
    }

    //
    // JSDoc
    //

    internal void EmitJSDocNode(Node node)
    {
        // !!!
        Debug.Fail("not implemented");
    }

    //
    // Top-level nodes
    //

    internal void EmitShebangIfNeeded(SourceFile node)
    {
        if (NodeIsSynthesized(node.AsNode()))
        {
            return;
        }
        var shebang = GetShebang(node.Text());
        if (shebang != "")
        {
            WriteComment(shebang);
            WriteLine();
        }
    }

    internal int EmitPrologueDirectives(NodeList statements)
    {
        var nodes = statements.Nodes;
        if (nodes is null)
        {
            return 0;
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            var statement = nodes[i]!;
            if (IsPrologueDirective(statement))
            {
                WriteLine();
                EmitStatement(statement);
            }
            else
            {
                return i;
            }
        }
        return nodes.Length;
    }

    internal bool EmitHelpers(Node node)
    {
        var helpersEmitted = false;
        var sourceFile = _currentSourceFile;
        var shouldSkip = Options.NoEmitHelpers || (sourceFile is not null && _emitContext.HasRecordedExternalHelpers(sourceFile));
        var helpers = _emitContext.GetEmitHelpers(node);
        if (helpers is { Length: > 0 })
        {
            var list = new List<EmitHelper>(helpers);
            SortEmitHelpersStable(list, CompareEmitHelpers);
            foreach (var helper in list)
            {
                if (!helper.Scoped)
                {
                    // Skip the helper if it can be skipped and the noEmitHelpers compiler
                    // option is set, or if it can be imported and the importHelpers compiler
                    // option is set.
                    if (shouldSkip)
                    {
                        continue;
                    }
                }
                if (helper.TextCallback is not null)
                {
                    WriteLines(helper.TextCallback(_makeFileLevelOptimisticUniqueName));
                }
                else
                {
                    WriteLines(helper.Text);
                }
                helpersEmitted = true;
            }
        }

        return helpersEmitted;
    }

    /// <summary>
    /// Go 的 <c>emitSourceFile</c>。C# 里必须换名：同名的导出包装 <c>EmitSourceFile</c> 已在 Printer.Emit.cs
    /// 落地（返回 string），而 C# 不允许仅返回类型不同的同名同参成员（CS0111）。
    /// </summary>
    internal void EmitSourceFileWorker(SourceFile node)
    {
        var savedCurrentSourceFile = _currentSourceFile;
        var savedCommentsDisabled = _commentsDisabled;
        _currentSourceFile = node;

        WriteLine();

        PushNameGenerationScope(node.AsNode());
        GenerateAllNames(node.Statements);

        var index = 0;
        CommentState? state;
        if (node.ScriptKind != ScriptKind.JSON)
        {
            EmitShebangIfNeeded(node);
            index = EmitPrologueDirectives(node.Statements!);
            if (!_writer.IsAtStartOfLine())
            {
                WriteLine();
            }
            state = EmitDetachedCommentsBeforeStatementList(node.AsNode(), node.Statements!.Loc);
            EmitHelpers(node.AsNode());
            if (node.IsDeclarationFile)
            {
                EmitTripleSlashDirectives(node);
            }
        }
        else
        {
            state = EmitDetachedCommentsBeforeStatementList(node.AsNode(), node.Statements!.Loc);
        }

        // !!! Emit triple-slash directives
        EmitListRange(
            EmitStatement,
            node.AsNode(),
            node.Statements,
            ListFormat.MultiLine,
            index,
            -1 /*count*/
        );
        PopNameGenerationScope(node.AsNode());
        EmitDetachedCommentsAfterStatementList(node.AsNode(), node.Statements!.Loc, state);
        _currentSourceFile = savedCurrentSourceFile;
        _commentsDisabled = savedCommentsDisabled;
    }

    internal void EmitTripleSlashDirectives(SourceFile node)
    {
        EmitDirective("path", node.ReferencedFiles);
        EmitDirective("types", node.TypeReferenceDirectives);
        EmitDirective("lib", node.LibReferenceDirectives);
    }

    internal void EmitDirective(string kind, FileReference[]? refs)
    {
        if (refs is null)
        {
            return;
        }
        foreach (var @ref in refs)
        {
            var resolutionMode = "";
            if (@ref.ResolutionMode != ResolutionModeNone)
            {
                resolutionMode = $"resolution-mode=\"{(IfElse(@ref.ResolutionMode == ResolutionModeESM, "import", "require"))}\" ";
            }
            WriteComment($"/// <reference {kind}=\"{@ref.FileName}\" {resolutionMode}{IfElse(@ref.Preserve, "preserve=\"true\" ", "")}/>");
            WriteLine();
        }
    }

    /// <summary>Go: <c>slices.SortStableFunc</c>（C# 的 List.Sort/Array.Sort 都不稳定，这里用下标兜底保持稳定性）。</summary>
    private static void SortEmitHelpersStable(List<EmitHelper> list, Comparison<EmitHelper> comparison)
    {
        var snapshot = list.ToArray();
        var indices = new int[snapshot.Length];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        Array.Sort(indices, (a, b) =>
        {
            var c = comparison(snapshot[a], snapshot[b]);
            return c != 0 ? c : a - b;
        });
        for (var i = 0; i < indices.Length; i++)
        {
            list[i] = snapshot[indices[i]];
        }
    }
}
