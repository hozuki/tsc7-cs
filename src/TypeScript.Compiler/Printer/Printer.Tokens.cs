// ported from: internal/printer/printer.go:923-1362（tokens/keywords, literals, snippet elements, names）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `*ast.TokenNode`/`ast.MemberName`/`ast.Statement` 等别名本质都是 `*ast.Node`（docs/ast.md §2）→ C#
//    参数写 `Node`，Go 里判 nil 的地方写 `Node?`（`emitKeywordNode`/`emitPunctuationNode`/`emitTokenNode`/
//    `emitBindingName`/`emitPropertyName`/`emitMemberName`/`emitModuleName`/`emitModuleExportName`/
//    `emitNestedModuleName`/`emitIdentifierNameNode` 的 `if node == nil { return }` 原样保留）。Go 里不判 nil
//    的（`emitToken`/`emitTokenEx` 的 contextNode、`emitEntityName`/`emitTemplateMiddleTail`/`emitImportAttributeName`
//    的 node）在 C# 里是非空 `Node`，调用点需要时写 `!`。
// 2) Go 的无表达式 `switch { case cond: … }` → C# 的 `if`/`else if` 链；Go 的多值 `case A, B:` → C# 的
//    `case A or B:`；`default:` 统一挪到最后（Go 里 default 的位置随意）。
// 3) `panic(fmt.Sprintf(…))` → `Debug.Fail($"…")`（静态类 `TypeScript.Debug`，见 Debug/Debug.cs 的文件头；
//    命名空间 `TypeScript.Printer` 嵌套在 `TypeScript` 下，可直接写 `Debug`）。`%v` 打在 `Kind` 上用
//    `node.Kind.String()`（KindStringer 扩展方法），打在无 stringer 的 `SnippetKind` 上用 `(int)`。
// 4) `p.writeKind` 直接读写 → `_writeKind`（Go 的 `emitPropertyName` 手工保存/恢复字段，故照写而不走 `SetWriteKind`）。
// 5) `p.uniqueHelperNames[name]` 是 Go 的两值 map 读取（缺失得 nil）→ C# 用 `_uniqueHelperNames.TryGetValue` +
//    `helperName is null` 判定（`Dictionary` 的索引器缺失会抛，绝不使用）；向 map 写入照 Go 的语义（nil map 写入
//    在 Go 里 panic → C# 用 `!` 解引用，`Dictionary` 索引器写入本身不抛）。
// 6) `p.IdToSymbol[node]` 同理 → `TryGetValue`（Go 的 `map[*ast.IdentifierNode]*ast.Symbol` → C# 的
//    `Dictionary<Node, Symbol>`，键都是标识符节点）。
// 7) `helperName.Clone(p.emitContext.Factory)` → C# 的 `Clone(NodeFactoryCoercible)` 返回 `Node?`，故加 `!`；
//    `getUniqueHelperName` 的 C# 返回类型是具体类 `Identifier`（Go 的 `*ast.IdentifierNode` 是 `*Node` 别名），
//    因此 Go 的 `helperName.AsIdentifier()` 在 C# 里直接赋值即可（`AsIdentifier()` 只会返回自身），见注释。
// 8) Go 的提升方法 `p.generateName(helperName)`（`Printer` 内嵌值类型 `nameGenerator`）→
//    `_nameGenerator.GenerateName(helperName)`（同 Printer.cs 里 `getTextOfNode` 的调用点）。
// 9) `len(tokenString)`（Go 的字节长度）→ `tokenString.Length`（UTF-16 代码单元）：token 文本全部是 ASCII，
//    两种长度相等（docs/architecture.md §3.1），`writeTokenText` 的 `pos + len(tokenString)` 照写。
// 10) `debug.Assert(cond, msg)` → `Debug.Assert(cond, msg)`（`TypeScript.Debug.Assert` 用 `params object?[]` 承接消息）。
// 11) `p.emitContext.Factory.NewUniqueNameEx(name, AutoGenerateOptions{Flags: …})` →
//     `_emitContext.Factory.NewUniqueNameEx(name, new AutoGenerateOptions { Flags = … })`（C# 的
//     `AutoGenerateOptions` 是 struct，字段用对象初始化器）。
// 12) `emitTabStop` 在 Go 里只被本文件的 `emitSnippetNode` 调用 → C# 落成 private（同 Printer.cs 的 file-local 先例）；
//     `emitSnippetNode` 的 `snippetElement *SnippetElement` 在 C# 里是按值参数 `SnippetElement`（Go 的指针只被解引用，
//     无 nil 分支）——nil 判定留给调用点（`_emitContext.SnippetElement(node)` 返回 `SnippetElement?`）。
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Printer;

public sealed partial class Printer
{
    internal int WriteTokenText(Kind token, WriteKind writeKind, int pos)
    {
        // !!! emit leading and trailing comments
        // !!! emit leading and trailing source maps
        var tokenString = TokenToString(token);
        WriteAs(tokenString, writeKind);
        if (PositionIsSynthesized(pos))
        {
            return pos;
        }
        else
        {
            return pos + tokenString.Length;
        }
    }

    internal int EmitToken(Kind token, int pos, WriteKind writeKind, Node contextNode)
    {
        return EmitTokenEx(token, pos, writeKind, contextNode, TokenEmitFlags.None);
    }

    internal int EmitTokenEx(Kind token, int pos, WriteKind writeKind, Node contextNode, TokenEmitFlags flags)
    {
        var (state, nextPos) = EnterToken(token, pos, contextNode, flags);
        nextPos = WriteTokenText(token, writeKind, nextPos);
        ExitToken(token, nextPos, contextNode, state);
        return nextPos;
    }

    internal void EmitKeywordNode(Node? node)
    {
        EmitKeywordNodeEx(node, TokenEmitFlags.None);
    }

    internal void EmitKeywordNodeEx(Node? node, TokenEmitFlags flags)
    {
        if (node is null)
        {
            return;
        }

        var state = EnterTokenNode(node, flags);
        WriteTokenText(node.Kind, WriteKind.Keyword, node.Pos());
        ExitTokenNode(node, state);
    }

    internal void EmitPunctuationNode(Node? node)
    {
        EmitPunctuationNodeEx(node, TokenEmitFlags.None);
    }

    internal void EmitPunctuationNodeEx(Node? node, TokenEmitFlags flags)
    {
        if (node is null)
        {
            return;
        }

        var state = EnterTokenNode(node, flags);
        WriteTokenText(node.Kind, WriteKind.Punctuation, node.Pos());
        ExitTokenNode(node, state);
    }

    internal void EmitTokenNode(Node? node)
    {
        EmitTokenNodeEx(node, TokenEmitFlags.None);
    }

    internal void EmitTokenNodeEx(Node? node, TokenEmitFlags flags)
    {
        if (node is null)
        {
            return;
        }

        // Go: 无表达式的 switch（default 分支在 Go 里可以放在任意位置）。
        if (IsKeywordKind(node.Kind))
        {
            EmitKeywordNodeEx(node, flags);
        }
        else if (IsPunctuationKind(node.Kind))
        {
            EmitPunctuationNodeEx(node, flags);
        }
        else
        {
            Debug.Fail($"unexpected TokenNode: {node.Kind.String()}");
        }
    }

    //
    // Literals
    //

    /// <summary>
    /// Emits literals of the following kinds
    ///
    /// SyntaxKindNumericLiteral
    /// SyntaxKindBigIntLiteral
    /// SyntaxKindStringLiteral
    /// SyntaxKindNoSubstitutionTemplateLiteral
    /// SyntaxKindRegularExpressionLiteral
    /// SyntaxKindTemplateHead
    /// SyntaxKindTemplateMiddle
    /// SyntaxKindTemplateTail
    /// </summary>
    internal void EmitLiteral(Node node, GetLiteralTextFlags flags)
    {
        // Add NeverAsciiEscape flag if the printer option is set
        if (Options.NeverAsciiEscape)
        {
            flags |= GetLiteralTextFlags.NeverAsciiEscape;
        }
        if (Options.TerminateUnterminatedLiterals)
        {
            flags |= GetLiteralTextFlags.TerminateUnterminatedLiterals;
        }

        var text = GetLiteralTextOfNode(node, null /*sourceFile*/, flags);

        // !!! Printer option to control source map emit, which causes us to use a different write method on the
        // emit text writer:

        ////if (
        ////	(printerOptions.sourceMap || printerOptions.inlineSourceMap)
        ////	&& (node.kind === SyntaxKindStringLiteral || isTemplateLiteralKind(node.kind))
        ////) {
        ////	writeLiteral(text);
        ////} else {

        // Quick info expects all literals to be called with writeStringLiteral, as there's no specific type for
        // numberLiterals
        _writer.WriteStringLiteral(text);

        // }
    }

    internal void EmitNumericLiteral(NumericLiteral node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitBigIntLiteral(BigIntLiteral node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None); // TODO: Preserve numeric literal separators after Strada migration
        ExitNode(node.AsNode(), state);
    }

    internal void EmitStringLiteral(StringLiteral node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitNoSubstitutionTemplateLiteral(NoSubstitutionTemplateLiteral node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitRegularExpressionLiteral(RegularExpressionLiteral node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None);
        ExitNode(node.AsNode(), state);
    }

    //
    // Pseudo-literals
    //

    internal void EmitTemplateHead(TemplateHead node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitTemplateMiddle(TemplateMiddle node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitTemplateTail(TemplateTail node)
    {
        var state = EnterNode(node.AsNode());
        EmitLiteral(node.AsNode(), GetLiteralTextFlags.None);
        ExitNode(node.AsNode(), state);
    }

    /// <summary>Go: <c>emitTemplateMiddleTail(node *ast.TemplateMiddleOrTail)</c>（`*ast.TemplateMiddleOrTail` 是 `*Node` 别名）。</summary>
    internal void EmitTemplateMiddleTail(Node node)
    {
        switch (node.Kind)
        {
            case Kind.TemplateMiddle:
                EmitTemplateMiddle(node.AsTemplateMiddle()!);
                break;
            case Kind.TemplateTail:
                EmitTemplateTail(node.AsTemplateTail()!);
                break;
        }
    }

    //
    // Snippet Elements
    //

    internal void EmitSnippetNode(Node node, SnippetElement snippetElement)
    {
        switch (snippetElement.Kind)
        {
            case SnippetKind.TabStop:
                EmitTabStop(node, snippetElement);
                break;
            // Go 的 default 分支（panic 未处理的 snippet element kind）。
            default:
                Debug.Fail($"Unhandled snippet element kind: {(int)snippetElement.Kind}");
                break;
        }
    }

    private void EmitTabStop(Node node, SnippetElement snippetElement)
    {
        Debug.Assert(node.Kind == Kind.EmptyStatement, "Snippet tab stops can only be emitted on empty statements");
        _writer.RawWrite($"${snippetElement.Order}");
    }

    //
    // Names
    //

    internal void EmitIdentifierText(Identifier node)
    {
        var f = GetSourceFileOfNode(node.AsNode());
        Debug.Assert(f is null || _currentSourceFile is null || f.FileName() == _currentSourceFile.FileName());
        var text = GetTextOfNode(node.AsNode(), false /*includeTrivia*/);

        if (IdToSymbol is not null)
        {
            if (IdToSymbol.TryGetValue(node.AsNode(), out var symbol))
            {
                WriteSymbol(text, symbol);
                return;
            }
        }
        Write(text);
    }

    internal void EmitIdentifierName(Identifier node)
    {
        var state = EnterNode(node.AsNode());
        EmitIdentifierText(node);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitIdentifierNameNode(Node? node)
    {
        if (node is null)
        {
            return;
        }
        EmitIdentifierName(node.AsIdentifier()!);
    }

    internal Identifier GetUniqueHelperName(string name)
    {
        // Go: helperName := p.uniqueHelperNames[name]（nil map 或缺键都得到 nil，故这里不能用会抛的索引器）。
        var helperName = _uniqueHelperNames is not null && _uniqueHelperNames.TryGetValue(name, out var existing) ? existing : null;
        if (helperName is null)
        {
            // Go 返回 *ast.IdentifierNode（*Node 别名），C# 的 NewUniqueNameEx 返回 Node → 断言成具体类。
            helperName = (Identifier)_emitContext.Factory.NewUniqueNameEx(
                name,
                new AutoGenerateOptions
                {
                    Flags = GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.Optimistic,
                });
            _nameGenerator.GenerateName(helperName);
            // Go 里向 nil map 写入会 panic；`!` 与之一一对应（两个调用点都已确认 map 非 nil）。
            _uniqueHelperNames![name] = helperName;
            return helperName;
        }
        // Go: helperName.Clone(p.emitContext.Factory)（C# 的 Clone 返回 Node?，Go 返回 *ast.IdentifierNode）。
        return (Identifier)helperName.Clone(_emitContext.Factory)!;
    }

    internal void EmitIdentifierReference(Identifier node)
    {
        if ((_externalHelpersModuleName is not null || _uniqueHelperNames is not null) &&
            (_emitContext.EmitFlags(node.AsNode()) & EmitFlags.HelperName) != 0)
        {
            if (_externalHelpersModuleName is not null)
            {
                // Substitute `__helper` with `tslib_1.__helper`
                var helper = _emitContext.Factory.NewPropertyAccessExpression(
                    _externalHelpersModuleName!.Clone(_emitContext.Factory),
                    null, /*questionDotToken*/
                    node.Clone(_emitContext.Factory),
                    NodeFlags.None);
                _emitContext.AssignCommentAndSourceMapRanges(helper, node.AsNode());
                EmitPropertyAccessExpression(helper.AsPropertyAccessExpression()!);
                return;
            }
            if (_uniqueHelperNames is not null)
            {
                // Substitute `__helper` with `__helper_1` if there is a conflict in an ES module.
                var helperName = GetUniqueHelperName(node.Text);
                _emitContext.AssignCommentAndSourceMapRanges(helperName, node.AsNode());
                node = helperName; // Go: node = helperName.AsIdentifier()（C# 的 helperName 已是具体类 Identifier）。
            }
        }

        var state = EnterNode(node.AsNode());
        EmitIdentifierText(node);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitBindingIdentifier(Identifier node)
    {
        if (_uniqueHelperNames is not null &&
            (_emitContext.EmitFlags(node.AsNode()) & EmitFlags.HelperName) != 0)
        {
            // Substitute `__helper` with `__helper_1` if there is a conflict in an ES module.
            var helperName = GetUniqueHelperName(node.Text);
            _emitContext.AssignCommentAndSourceMapRanges(helperName, node.AsNode());
            node = helperName; // Go: node = helperName.AsIdentifier()（C# 的 helperName 已是具体类 Identifier）。
        }

        var state = EnterNode(node.AsNode());
        EmitIdentifierText(node);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitLabelIdentifier(Identifier node)
    {
        var state = EnterNode(node.AsNode());
        EmitIdentifierText(node);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitPrivateIdentifier(PrivateIdentifier node)
    {
        var state = EnterNode(node.AsNode());
        Write(GetTextOfNode(node.AsNode(), false /*includeTrivia*/));
        ExitNode(node.AsNode(), state);
    }

    internal void EmitQualifiedName(QualifiedName node)
    {
        var state = EnterNode(node.AsNode());
        EmitEntityName(node.Left!);
        WritePunctuation(".");
        EmitMemberName(node.Right);
        ExitNode(node.AsNode(), state);
    }

    internal void EmitComputedPropertyName(ComputedPropertyName node)
    {
        var state = EnterNode(node.AsNode());
        WritePunctuation("[");
        EmitExpression(node.Expression!, OperatorPrecedenceDisallowComma);
        WritePunctuation("]");
        ExitNode(node.AsNode(), state);
    }

    internal void EmitEntityName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierReference(node.AsIdentifier()!);
                break;
            case Kind.QualifiedName:
                EmitQualifiedName(node.AsQualifiedName()!);
                break;
            case Kind.PropertyAccessExpression:
                // TypeQuery nodes may have PropertyAccessExpression as exprName (e.g. typeof foo.x).
                // TS's emitter handles this via generic emit(); we dispatch to expression emitter here.
                EmitExpression(node, OperatorPrecedenceDisallowComma);
                break;
            default:
                Debug.Fail($"unexpected EntityName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitBindingName(Node? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitBindingIdentifier(node.AsIdentifier()!);
                break;
            case Kind.ObjectBindingPattern:
                EmitObjectBindingPattern(node.AsBindingPattern()!);
                break;
            case Kind.ArrayBindingPattern:
                EmitArrayBindingPattern(node.AsBindingPattern()!);
                break;
            default:
                Debug.Fail($"unexpected BindingName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitPropertyName(Node? node)
    {
        if (node is null)
        {
            return;
        }

        var savedWriteKind = _writeKind;
        _writeKind = WriteKind.Property;

        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierName(node.AsIdentifier()!);
                break;
            case Kind.PrivateIdentifier:
                EmitPrivateIdentifier(node.AsPrivateIdentifier()!);
                break;
            case Kind.StringLiteral:
                EmitStringLiteral(node.AsStringLiteral()!);
                break;
            case Kind.NoSubstitutionTemplateLiteral:
                EmitNoSubstitutionTemplateLiteral(node.AsNoSubstitutionTemplateLiteral()!);
                break;
            case Kind.NumericLiteral:
                EmitNumericLiteral(node.AsNumericLiteral()!);
                break;
            case Kind.BigIntLiteral:
                EmitBigIntLiteral(node.AsBigIntLiteral()!);
                break;
            case Kind.ComputedPropertyName:
                EmitComputedPropertyName(node.AsComputedPropertyName()!);
                break;
            default:
                Debug.Fail($"unexpected PropertyName: {node.Kind.String()}");
                break;
        }

        _writeKind = savedWriteKind;
    }

    internal void EmitMemberName(Node? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierName(node.AsIdentifier()!);
                break;
            case Kind.PrivateIdentifier:
                EmitPrivateIdentifier(node.AsPrivateIdentifier()!);
                break;
            default:
                Debug.Fail($"unexpected MemberName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitModuleName(Node? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitBindingIdentifier(node.AsIdentifier()!);
                break;
            case Kind.StringLiteral:
                EmitStringLiteral(node.AsStringLiteral()!);
                break;
            default:
                Debug.Fail($"unexpected ModuleName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitModuleExportName(Node? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierName(node.AsIdentifier()!);
                break;
            case Kind.StringLiteral:
                EmitStringLiteral(node.AsStringLiteral()!);
                break;
            default:
                Debug.Fail($"unexpected ModuleExportName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitImportAttributeName(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierName(node.AsIdentifier()!);
                break;
            case Kind.StringLiteral:
                EmitStringLiteral(node.AsStringLiteral()!);
                break;
            default:
                Debug.Fail($"unexpected ImportAttributeName: {node.Kind.String()}");
                break;
        }
    }

    internal void EmitNestedModuleName(Node? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.Identifier:
                EmitIdentifierName(node.AsIdentifier()!);
                break;
            case Kind.StringLiteral:
                EmitStringLiteral(node.AsStringLiteral()!);
                break;
            default:
                Debug.Fail($"unexpected ModuleName: {node.Kind.String()}");
                break;
        }
    }
}
