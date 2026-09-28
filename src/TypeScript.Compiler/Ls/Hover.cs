// ported from: internal/ls/hover.go:1-1163
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级未导出函数 → 静态类 `Ls` 的 `internal static` 成员（G1 约定 §0；**方法一律 PascalCase，不加 `_`**）；
//    `*LanguageService` 的方法（ProvideHover / getQuickInfoAndDocumentationForSymbol / documentationLocationMapper）
//    → `LanguageService` 的实例成员（PascalCase）。
//
// 2) 两个包级未导出常量 `symbolFormatFlags` / `typeFormatFlags` → `Ls._symbolFormatFlags` / `Ls._typeFormatFlags`
//    （Go 未导出的**字段/常量**才带 `_`）。
//
// 3) `type documentationLocationMapper func(*ast.SourceFile, core.TextRange) (lsproto.Location, spanmap.Fidelity)`
//    → 命名空间级 `internal delegate DocumentationLocationMapper`（C# 的委托声明位置不影响可见性；放命名空间级
//    可让 G5（signaturehelp）与 G6（findallreferences）直接引用该类型名）。
//
// 4) `type symbolDisplayInfo struct { displayParts *displayPartsWriter; declaration *ast.Node }` → `SymbolDisplayInfo`
//    类，字段按 G1 约定落成 `_displayParts` / `_declaration`（Go 未导出字段）。
//
// 5) `lsproto.HoverResponse` 是 Go 的类型别名（= `HoverOrNull`）⇒ C# 直接用 `HoverOrNull`（先例 Ls/Diagnostics.cs:13）。
//    Go 的 `(lsproto.HoverResponse, error)` → `(HoverOrNull Response, Exception? Error)`（本仓既有双返回值约定）。
//
// 6) `fmt.Sprintf` → 内插字符串；`strings.Builder` → `StringBuilder`；`strings.TrimRight(s, "\n")` → `s.TrimEnd('\n')`；
//    `strings.Trim(s, " ")` → `s.Trim(' ')`；`strings.TrimLeft(s, "\n")` → `s.TrimStart('\n')`；
//    `strings.IndexFunc(s, f)` → `s.IndexOfAny([' ', '|'])`（谓词恰好是这两个字符）。
//
// 7) `*commonRange != *hover.Range`（Go 的结构体值比较）→ `commonRange.Compare(hover.Range) != 0`
//    （LsProto 生成物为引用类型，`Range.Compare` 就是逐字段比较，0 = 相等）。
//
// 8) Go 的闭包（`writeTypeClassified` / `writeSignatureClassified` / `writeSymbolClassified` /
//    `writeModuleImportAttributes` / `writeNewLine` / `writeSignatures` / `writeTypeParams` / `canExpandSymbol` /
//    `tryExpandSymbol` / `writeSymbol`）→ C# 局部函数（捕获同一批局部变量，语义一致）。
//    `writeSymbol` 在 Go 里先声明后赋值为匿名函数（为了自递归）；C# 局部函数天然可自递归。
//    ⚠ Go 内层 `flags := typeFormatFlags | …` 遮蔽外层 `flags` ⇒ C# 不允许遮蔽，改名 `callFlags`（语义不变）。
//
// 9) `map[*ast.IdentifierNode]*ast.Symbol` → `Dictionary<Node, Symbol>`（C# 侧 `NodeBuilder`/`Printer.IdToSymbol`
//    就是这么声明的；见 Checker/NodeBuilder.cs:337、Printer/Printer.cs:106）；`make(map[…])` → `new Dictionary<…>()`。
//
// 10) `nodebuilder.Flags(flags & checker.TypeFormatFlagsNodeBuilderFlagsMask) | classifiedNodeBuilderFlags`
//     → `(NodeBuilderFlags)(flags & TypeFormatFlags.NodeBuilderFlagsMask) | classifiedNodeBuilderFlags`。
//
// 11) `node.AsXxx()` 返回可空 ⇒ 补 `!`；`Node[]?` 形参/返回值保留 nil 语义；`x is null` 判空与 Go 的 `== nil` 对齐。
//
// 12) 深递归：本文件的递归入口（`GetContainerNode` 只沿父链、`FindAncestor` 是容器里既有的实现）都在
//     LsConv/Ast 侧；`ProvideHover` 本身只在列表上迭代，无新增递归入口 ⇒ 按 `Provide*` 既有约定即可（由 lsp/fourslash
//     层统一派发到大栈线程）。
//
// 13) `collateral`：`getDeclarationsFromLocation`（writeNameLink 用）是 definition.go 的包级 helper，归 G2
//     （`Ls.GetDeclarationsFromLocation`，见交付清单），本文件不重复声明。
using System;
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using TypeScript.AstNav;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Compiler;
using TypeScript.Core;
using TypeScript.LsConv;
using TypeScript.LsProto;
using TypeScript.LsUtil;
using TypeScript.NodeBuilder;
using TypeScript.Printer;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.AstNav.AstNav;
using static TypeScript.Checker.Checker;
using static TypeScript.Core.Core;
using static TypeScript.Ls.Ls;
using static TypeScript.LsProto.LsProto;
using static TypeScript.LsUtil.LsUtil;
using static TypeScript.Printer.Printer;
using AstStatic = TypeScript.Ast.Ast;
using CheckerType = TypeScript.Checker.Checker;
// 同名类型消歧（`using System` / `using TypeScript.*` 带来的同名类型；先例 LsConv/Converters.cs:56）：
using Kind = TypeScript.Ast.Kind;
using NodeBuilderFlags = TypeScript.NodeBuilder.Flags;
using NodeBuilderInternalFlags = TypeScript.NodeBuilder.InternalFlags;
using Program = TypeScript.Compiler.Program;
using Range = TypeScript.LsProto.Range;
using Type = TypeScript.Checker.Type;

namespace TypeScript.Ls;

/// <summary>Go: <c>type documentationLocationMapper func(*ast.SourceFile, core.TextRange) (lsproto.Location, spanmap.Fidelity)</c>。</summary>
internal delegate (Location Location, Fidelity Fidelity) DocumentationLocationMapper(SourceFile file, TextRange fileRange);

/// <summary>symbolDisplayInfo holds the result of getSymbolDisplayPartsDocumentationAndSymbolKind.</summary>
internal sealed class SymbolDisplayInfo
{
    internal DisplayPartsWriter _displayParts = null!;
    internal Node? _declaration;
}

public static partial class Ls
{
    internal const SymbolFormatFlags _symbolFormatFlags =
        SymbolFormatFlags.WriteTypeParametersOrArguments | SymbolFormatFlags.UseOnlyExternalAliasing |
        SymbolFormatFlags.AllowAnyNodeKind | SymbolFormatFlags.UseAliasDefinedOutsideCurrentScope;

    internal const TypeFormatFlags _typeFormatFlags =
        TypeFormatFlags.UseAliasDefinedOutsideCurrentScope | TypeFormatFlags.UseInstantiationExpressions;

    /// <summary>
    /// getDocumentationForSymbol tries each documentation source in turn (call-signature documentation,
    /// declaration JSDoc, root-symbol JSDoc, alias target JSDoc) and returns the first non-empty result,
    /// formatted for contentFormat. commentOnly restricts the result to the JSDoc summary, excluding the
    /// @tag section.
    /// </summary>
    internal static string GetDocumentationForSymbol(
        DocumentationLocationMapper getMappedLocation,
        CheckerType c,
        Symbol? symbol,
        Node? node,
        Node? declaration,
        MarkupKind contentFormat,
        bool commentOnly)
    {
        var documentation = DocumentationFromSignature(getMappedLocation, c, symbol, GetCallOrNewExpression(node!), node, contentFormat, commentOnly);
        if (documentation != "")
        {
            return documentation;
        }

        documentation = DocumentationFromRootSymbols(getMappedLocation, c, symbol, node, contentFormat, commentOnly);
        if (documentation != "")
        {
            return documentation;
        }

        documentation = GetDocumentationFromDeclaration(getMappedLocation, c, symbol, declaration, node, contentFormat, commentOnly);
        if (documentation != "")
        {
            return documentation;
        }

        return DocumentationFromAlias(getMappedLocation, c, symbol, node, contentFormat, commentOnly);
    }

    internal static string DocumentationFromSignature(
        DocumentationLocationMapper getMappedLocation,
        CheckerType c,
        Symbol? symbol,
        Node? node,
        Node? location,
        MarkupKind contentFormat,
        bool commentOnly)
    {
        if (node is null)
        {
            return "";
        }
        var signature = c.GetResolvedSignature(node);
        if (signature is null)
        {
            return "";
        }
        var declaration = signature.Declaration();
        if (declaration is null)
        {
            return "";
        }
        if (IsCallSignatureDeclaration(declaration) || IsConstructSignatureDeclaration(declaration))
        {
            return GetDocumentationFromDeclaration(getMappedLocation, c, symbol, declaration, location, contentFormat, commentOnly);
        }
        return "";
    }

    internal static string DocumentationFromAlias(
        DocumentationLocationMapper getMappedLocation,
        CheckerType c,
        Symbol? symbol,
        Node? node,
        MarkupKind contentFormat,
        bool commentOnly)
    {
        if (symbol is null || (symbol.Flags & SymbolFlags.Alias) == 0)
        {
            return "";
        }

        var aliasedSymbol = c.GetAliasedSymbol(symbol);
        if (aliasedSymbol is null || aliasedSymbol == c.GetUnknownSymbol())
        {
            return "";
        }

        var candidates = new List<Symbol> { aliasedSymbol };
        if (aliasedSymbol.ExportSymbol is not null)
        {
            candidates.Add(aliasedSymbol.ExportSymbol);
        }

        foreach (var candidate in candidates)
        {
            var aliasedDeclaration = OrElse(candidate.ValueDeclaration, FirstOrNil(candidate.Declarations));
            if (aliasedDeclaration is null)
            {
                continue;
            }

            var documentation = GetDocumentationFromDeclaration(getMappedLocation, c, candidate, aliasedDeclaration, node, contentFormat, commentOnly);
            if (documentation != "")
            {
                return documentation;
            }
        }

        return "";
    }

    internal static string DocumentationFromRootSymbols(
        DocumentationLocationMapper getMappedLocation,
        CheckerType c,
        Symbol? symbol,
        Node? node,
        MarkupKind contentFormat,
        bool commentOnly)
    {
        if (symbol is null)
        {
            return "";
        }

        var rootSymbols = c.GetRootSymbols(symbol);
        if (rootSymbols.Length <= 1)
        {
            return "";
        }

        var docs = new List<string>();
        foreach (var rootSymbol in rootSymbols)
        {
            if (rootSymbol is null)
            {
                continue;
            }
            var declarations = rootSymbol.Declarations;
            if ((declarations is null || declarations.Length == 0) && rootSymbol.ValueDeclaration is not null)
            {
                declarations = [rootSymbol.ValueDeclaration];
            }
            foreach (var declaration in declarations ?? [])
            {
                var documentation = GetDocumentationFromDeclaration(getMappedLocation, c, rootSymbol, declaration, node, contentFormat, commentOnly);
                if (documentation != "")
                {
                    docs = [.. AppendIfUnique(docs.ToArray(), documentation)];
                }
            }
        }
        return string.Join("\n", docs);
    }

    internal static string GetDocumentationFromDeclaration(
        DocumentationLocationMapper getMappedLocation,
        CheckerType c,
        Symbol? symbol,
        Node? declaration,
        Node? location,
        MarkupKind contentFormat,
        bool commentOnly)
    {
        if (declaration is null)
        {
            return "";
        }
        var isMarkdown = contentFormat == MarkupKind.Markdown;
        var b = new StringBuilder();
        var jsdoc = GetJSDocOrTag(c, declaration, new Set<Symbol>());
        if (jsdoc is not null && !(((declaration.Flags & NodeFlags.Reparsed) == 0) && ContainsTypedefTag(jsdoc)))
        {
            WriteComments(getMappedLocation, b, c, jsdoc.Comments(), isMarkdown);
            if (jsdoc.Kind == Kind.JSDoc && !commentOnly)
            {
                var tags = jsdoc.AsJSDoc()!.Tags;
                if (tags is not null)
                {
                    foreach (var tag in tags.Nodes ?? [])
                    {
                        if (tag.Kind == Kind.JSDocTypeTag || tag.Kind == Kind.JSDocTypedefTag || tag.Kind == Kind.JSDocCallbackTag)
                        {
                            continue;
                        }
                        b.Append("\n\n");
                        if (isMarkdown)
                        {
                            b.Append("*@");
                            b.Append(tag.TagName()!.Text());
                            b.Append("*");
                        }
                        else
                        {
                            b.Append("@");
                            b.Append(tag.TagName()!.Text());
                        }
                        switch (tag.Kind)
                        {
                            case Kind.JSDocParameterTag:
                            case Kind.JSDocPropertyTag:
                                WriteOptionalEntityName(b, tag.Name());
                                break;
                            case Kind.JSDocAugmentsTag:
                                WriteOptionalEntityName(b, tag.ClassName());
                                break;
                            case Kind.JSDocTemplateTag:
                                var typeParameters = tag.TypeParameters();
                                for (var i = 0; i < (typeParameters?.Length ?? 0); i++)
                                {
                                    if (i != 0)
                                    {
                                        b.Append(",");
                                    }
                                    WriteOptionalEntityName(b, typeParameters![i].Name());
                                }
                                break;
                        }
                        var comments = tag.Comments();
                        if (tag.Kind == Kind.JSDocUnknownTag && tag.TagName()!.Text() == "example")
                        {
                            var commentText = Scanner.Scanner.GetTextOfJSDocComment(tag.CommentList());
                            if (commentText.StartsWith("<caption>", StringComparison.Ordinal))
                            {
                                var captionEnd = commentText.IndexOf("</caption>", StringComparison.Ordinal);
                                if (captionEnd > 0)
                                {
                                    b.Append(" — ");
                                    b.Append(commentText["<caption>".Length..captionEnd]);
                                    commentText = commentText[(captionEnd + "</caption>".Length)..];
                                    // Trim leading blank lines from commentText
                                    while (true)
                                    {
                                        var s1 = commentText.TrimStart(' ', '\t');
                                        var s2 = s1.TrimStart('\r', '\n');
                                        if (s1.Length == s2.Length)
                                        {
                                            break;
                                        }
                                        commentText = s2;
                                    }
                                }
                            }
                            b.Append("\n");
                            if (commentText.Length > 6 && commentText.StartsWith("```", StringComparison.Ordinal) && commentText.EndsWith("```", StringComparison.Ordinal) && commentText.Contains('\n'))
                            {
                                b.Append(commentText);
                                b.Append("\n");
                            }
                            else
                            {
                                WriteCode(b, "tsx", commentText);
                            }
                        }
                        else if (tag.Kind == Kind.JSDocSeeTag && tag.AsJSDocSeeTag()!.NameExpression is not null)
                        {
                            b.Append(" — ");
                            WriteNameLink(getMappedLocation, b, c, tag.AsJSDocSeeTag()!.NameExpression!.Name()!, "", false /*quote*/, isMarkdown);
                            if (comments is not null && comments.Length != 0)
                            {
                                b.Append(" ");
                                WriteComments(getMappedLocation, b, c, comments, isMarkdown);
                            }
                        }
                        else if (tag.Kind == Kind.JSDocThrowsTag && tag.AsJSDocThrowsTag()!.TypeExpression is not null)
                        {
                            b.Append(" — ");
                            b.Append(Scanner.Scanner.GetTextOfNode(tag.AsJSDocThrowsTag()!.TypeExpression!));
                            if (comments is not null && comments.Length != 0)
                            {
                                b.Append(" ");
                                WriteComments(getMappedLocation, b, c, comments, isMarkdown);
                            }
                        }
                        else if (comments is not null && comments.Length != 0)
                        {
                            b.Append(" ");
                            if (comments[0].Kind != Kind.JSDocText || !comments[0].Text()!.StartsWith("-", StringComparison.Ordinal))
                            {
                                b.Append("— ");
                            }
                            WriteComments(getMappedLocation, b, c, comments, isMarkdown);
                        }
                    }
                }
            }
        }
        return b.ToString();
    }

    internal static string FormatQuickInfo(string quickInfo)
    {
        var b = new StringBuilder(32);
        WriteCode(b, "typescript", quickInfo);
        return b.ToString();
    }

    internal static bool ShouldGetType(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                // If we're in a JSDoc node with no associated symbol, no binding has taken place for the node and
                // we can't answer questions about types of declaration nodes (such as property declarations).
                return !((node.Flags & NodeFlags.JSDoc) != 0 && IsDeclarationName(node)) && !IsLabelName(node) && !IsTagName(node) && !AstStatic.IsConstTypeReference(node.Parent!);
            case Kind.ThisKeyword:
            case Kind.ThisType:
            case Kind.SuperKeyword:
            case Kind.NamedTupleMember:
                return true;
            case Kind.MetaProperty:
                return IsImportMeta(node);
            default:
                return false;
        }
    }

    /// <summary>
    /// getQuickInfoAndDeclarationAtLocation builds classified display parts using displayPartsWriter when vsCapability is true.
    /// When vsCapability is false, it still builds the plain text string but skips classification runs.
    /// </summary>
    internal static SymbolDisplayInfo GetQuickInfoAndDeclarationAtLocation(
        CheckerType c,
        Symbol? symbol,
        Node node,
        VerbosityContext? vc,
        bool vsCapability,
        SemanticMeaning meaning)
    {
        var container = GetContainerNode(node);
        vc ??= new VerbosityContext();
        var dpw = new DisplayPartsWriter(vsCapability);

        // Source file for printer context
        SourceFile? sourceFile = null;
        sourceFile = GetSourceFileOfNode(node);

        // nodeBuilderFlags for classified output (same as signatureHelpNodeBuilderFlags)
        const NodeBuilderFlags classifiedNodeBuilderFlags = NodeBuilderFlags.IgnoreErrors | NodeBuilderFlags.UseAliasDefinedOutsideCurrentScope | NodeBuilderFlags.WriteTypeParametersInQualifiedName;

        // writeTypeClassified writes a type to dpw with proper classification (punctuation, symbols, keywords).
        // Falls back to flat text when vsCapability is false or when TypeToTypeNode fails.
        void WriteTypeClassified(Type t, Node? enclosing, TypeFormatFlags flags)
        {
            flags |= TypeFormatFlags.MultilineObjectLiterals;
            if (!vsCapability)
            {
                dpw.Write(c.TypeToStringEx(t, enclosing, flags, vc));
                return;
            }
            var emitContext = NewEmitContext();
            var idToSymbol = new Dictionary<Node, Symbol>();
            var nb = NewNodeBuilderEx(c, emitContext, idToSymbol);
            var combinedFlags = (NodeBuilderFlags)(flags & TypeFormatFlags.NodeBuilderFlagsMask) | classifiedNodeBuilderFlags;
            var typeNode = nb.TypeToTypeNode(t, enclosing, combinedFlags, NodeBuilderInternalFlags.None, null);
            if (typeNode is null)
            {
                dpw.Write(c.TypeToStringEx(t, enclosing, flags, vc));
                return;
            }
            var p = NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), emitContext);
            p.IdToSymbol = idToSymbol;
            var tempDpw = new DisplayPartsWriter(true);
            p.Write(typeNode, sourceFile, tempDpw, null);
            dpw.WriteFrom(tempDpw);
        }

        // writeSignatureClassified writes a signature to dpw with proper classification.
        void WriteSignatureClassified(Signature sig, Node? enclosing, TypeFormatFlags flags)
        {
            flags |= TypeFormatFlags.MultilineObjectLiterals;
            if (!vsCapability)
            {
                dpw.Write(c.SignatureToStringEx(sig, enclosing, flags, vc));
                return;
            }
            var isConstructor = (sig.Flags() & SignatureFlags.Construct) != 0 && (flags & TypeFormatFlags.WriteCallStyleSignature) == 0;
            Kind sigOutput;
            if ((flags & TypeFormatFlags.WriteArrowStyleSignature) != 0)
            {
                if (isConstructor)
                {
                    sigOutput = Kind.ConstructorType;
                }
                else
                {
                    sigOutput = Kind.FunctionType;
                }
            }
            else
            {
                if (isConstructor)
                {
                    sigOutput = Kind.ConstructSignature;
                }
                else
                {
                    sigOutput = Kind.CallSignature;
                }
            }
            var emitContext = NewEmitContext();
            var idToSymbol = new Dictionary<Node, Symbol>();
            var nb = NewNodeBuilderEx(c, emitContext, idToSymbol);
            var combinedFlags = (NodeBuilderFlags)(flags & TypeFormatFlags.NodeBuilderFlagsMask) | classifiedNodeBuilderFlags;
            var sigNode = nb.SignatureToSignatureDeclaration(sig, sigOutput, enclosing, combinedFlags, NodeBuilderInternalFlags.None, null);
            if (sigNode is null)
            {
                dpw.Write(c.SignatureToStringEx(sig, enclosing, flags, vc));
                return;
            }
            var p = NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), emitContext);
            p.IdToSymbol = idToSymbol;
            var tempDpw = new DisplayPartsWriter(true);
            p.Write(sigNode, sourceFile, tempDpw, null);
            dpw.WriteFrom(tempDpw);
        }

        // writeSymbolClassified writes a symbol name to dpw with proper classification based on symbol flags.
        void WriteSymbolClassified(Symbol symbol, Node? enclosing, SymbolFlags meaning, SymbolFormatFlags flags)
        {
            if (!vsCapability)
            {
                dpw.Write(c.SymbolToStringEx(symbol, enclosing, meaning, flags));
                return;
            }
            // Use WriteSymbol which calls classificationForSymbol to determine the correct classification
            var text = c.SymbolToStringEx(symbol, enclosing, meaning, flags);
            dpw.WriteSymbol(text, symbol);
        }

        void WriteModuleImportAttributes(Symbol symbol)
        {
            var declaration = Find(symbol.Declarations, declaration => IsModuleDeclaration(declaration) && declaration.AsModuleDeclaration()!.Attributes is not null);
            if (declaration is null)
            {
                return;
            }
            var attributes = declaration.AsModuleDeclaration()!.Attributes;
            var emitContext = NewEmitContext();
            emitContext.SetEmitFlags(attributes!, EmitFlags.SingleLine);
            var p = NewPrinter(new PrinterOptions { NewLine = NewLineKind.LF }, new PrintHandlers(), emitContext);
            var tempDpw = new DisplayPartsWriter(vsCapability);
            p.Write(attributes!, GetSourceFileOfNode(declaration), tempDpw, null);
            dpw.WriteKeyword(" with ");
            dpw.WriteFrom(tempDpw);
        }

        if ((node.Kind == Kind.ThisKeyword && IsInExpressionContext(node)) || IsThisInTypeQuery(node))
        {
            dpw.WriteKeyword("this");
            dpw.WritePunctuation(": ");
            WriteTypeClassified(c.GetTypeAtLocation(node), container, _typeFormatFlags);
            return new SymbolDisplayInfo { _displayParts = dpw };
        }
        if (symbol is null)
        {
            if (ShouldGetType(node))
            {
                WriteTypeClassified(c.GetTypeAtLocation(node), container, _typeFormatFlags);
            }
            return new SymbolDisplayInfo { _displayParts = dpw };
        }
        var visitedAliases = new Set<Symbol>();
        var aliasLevel = 0;
        Node? firstDeclaration = null;
        void SetDeclaration(Node? declaration)
        {
            if (firstDeclaration is null)
            {
                firstDeclaration = declaration;
            }
        }
        void WriteNewLine()
        {
            if (dpw.String() != "")
            {
                dpw.Write("\n");
            }
            if (aliasLevel != 0)
            {
                dpw.WritePunctuation("(");
                dpw.Write("alias");
                dpw.WritePunctuation(") ");
            }
        }
        void WriteSignatures(Signature[] signatures, string prefix, bool parenthesized, Symbol symbol)
        {
            for (var i = 0; i < signatures.Length; i++)
            {
                var sig = signatures[i];
                WriteNewLine();
                if (i == 3 && signatures.Length >= 5)
                {
                    dpw.WriteComment($"// +{signatures.Length - 3} more overloads");
                    break;
                }
                if (parenthesized)
                {
                    dpw.WritePunctuation("(");
                    dpw.Write(prefix);
                    dpw.WritePunctuation(") ");
                }
                else
                {
                    dpw.WriteKeyword(prefix);
                }
                WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                if ((symbol.Flags & SymbolFlags.Optional) != 0)
                {
                    dpw.WritePunctuation("?");
                }
                WriteSignatureClassified(sig, container, _typeFormatFlags | TypeFormatFlags.WriteCallStyleSignature | TypeFormatFlags.WriteTypeArgumentsOfSignature);
            }
        }
        void WriteTypeParams(Type[] parameters)
        {
            if (parameters.Length > 0)
            {
                dpw.WritePunctuation("<");
                for (var i = 0; i < parameters.Length; i++)
                {
                    var tp = parameters[i];
                    if (i != 0)
                    {
                        dpw.WritePunctuation(", ");
                    }
                    WriteSymbolClassified(tp.Symbol()!, null, SymbolFlags.None, _symbolFormatFlags);
                    var cons = c.GetConstraintOfTypeParameter(tp);
                    if (cons is not null)
                    {
                        dpw.WriteKeyword(" extends ");
                        WriteTypeClassified(cons, null, _typeFormatFlags);
                    }
                    var def = c.GetDefaultFromTypeParameter(tp);
                    if (def is not null)
                    {
                        dpw.WriteOperator(" = ");
                        WriteTypeClassified(def, null, _typeFormatFlags);
                    }
                }
                dpw.WritePunctuation(">");
            }
        }
        var symbolWasExpanded = false;
        bool CanExpandSymbol(Symbol symbol)
        {
            if (vc is null)
            {
                return false;
            }
            // Only offer symbol-level expansion for types that tryExpandSymbol handles:
            // class, interface, enum, namespace/module. For functions/variables/properties,
            // the node builder's probeTypeExpandability detects expandable type components.
            if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface | SymbolFlags.Namespace)) == 0)
            {
                return false;
            }
            Type? t;
            if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
            {
                t = c.GetDeclaredTypeOfSymbol(symbol);
            }
            else
            {
                t = c.GetTypeOfSymbolAtLocation(symbol, node);
            }
            if (t is null || c.IsLibTypeForHoverVerbosity(t))
            {
                return false;
            }
            if (vc.Level > 0)
            {
                return true;
            }
            // At level 0, signal that expansion is possible but don't expand
            vc.CanIncreaseVerbosity = true;
            return false;
        }
        // tryExpandSymbol checks if a symbol can be expanded at the current verbosity level.
        bool TryExpandSymbol(Symbol symbol, SymbolFlags meaning)
        {
            if (symbolWasExpanded)
            {
                return true;
            }
            if (CanExpandSymbol(symbol))
            {
                var expandVC = new VerbosityContext
                {
                    Level = vc!.Level - 1,
                    MaxTruncationLength = vc.MaxTruncationLength,
                };
                var expanded = c.ExpandSymbolForHover(symbol, meaning, expandVC);
                if (expanded != "")
                {
                    vc.CanIncreaseVerbosity = vc.CanIncreaseVerbosity || expandVC.CanIncreaseVerbosity;
                    vc.Truncated = vc.Truncated || expandVC.Truncated;
                    dpw.Write(expanded);
                    symbolWasExpanded = true;
                    return true;
                }
            }
            return false;
        }
        void WriteSymbol(Symbol symbol)
        {
            // Recursively write all meanings of alias
            if ((symbol.Flags & SymbolFlags.Alias) != 0 && visitedAliases.AddIfAbsent(symbol))
            {
                var aliasedSymbol = c.GetAliasedSymbol(symbol);
                if (aliasedSymbol != c.GetUnknownSymbol())
                {
                    aliasLevel++;
                    WriteSymbol(aliasedSymbol);
                    aliasLevel--;
                }
            }
            SymbolFlags flags;
            switch (meaning)
            {
                case SemanticMeaning.Value:
                    flags = symbol.Flags & (SymbolFlags.Value | SymbolFlags.Signature);
                    break;
                case SemanticMeaning.Type:
                    flags = symbol.Flags & SymbolFlags.Type;
                    break;
                case SemanticMeaning.Namespace:
                    flags = symbol.Flags & SymbolFlags.Namespace;
                    break;
                default:
                    flags = symbol.Flags & (SymbolFlags.Value | SymbolFlags.Signature | SymbolFlags.Type | SymbolFlags.Namespace);
                    break;
            }
            if (flags == 0)
            {
                if (aliasLevel != 0 || dpw.String() != "")
                {
                    return;
                }
                flags = symbol.Flags & (SymbolFlags.Value | SymbolFlags.Signature | SymbolFlags.Type | SymbolFlags.Namespace);
                if (flags == 0)
                {
                    return;
                }
            }
            if ((flags & SymbolFlags.Property) != 0 && symbol.ValueDeclaration is not null && IsMethodDeclaration(symbol.ValueDeclaration))
            {
                flags = SymbolFlags.Method;
            }
            if ((flags & (SymbolFlags.Variable | SymbolFlags.Property | SymbolFlags.Accessor)) != 0)
            {
                WriteNewLine();
                if ((symbol.CheckFlags & CheckFlags.IndexSymbol) == 0)
                {
                    if ((flags & SymbolFlags.Property) != 0)
                    {
                        dpw.WritePunctuation("(");
                        dpw.Write("property");
                        dpw.WritePunctuation(") ");
                    }
                    else if ((flags & SymbolFlags.Accessor) != 0)
                    {
                        dpw.WritePunctuation("(");
                        dpw.Write("accessor");
                        dpw.WritePunctuation(") ");
                    }
                    else
                    {
                        var decl = symbol.ValueDeclaration;
                        if (decl is not null)
                        {
                            decl = GetRootDeclaration(decl)!;
                            if (IsParameterDeclaration(decl!))
                            {
                                dpw.WritePunctuation("(");
                                dpw.Write("parameter");
                                dpw.WritePunctuation(") ");
                            }
                            else if (IsVarLet(decl))
                            {
                                dpw.WriteKeyword("let ");
                            }
                            else if (IsVarConst(decl))
                            {
                                dpw.WriteKeyword("const ");
                            }
                            else if (IsVarUsing(decl))
                            {
                                dpw.WriteKeyword("using ");
                            }
                            else if (IsVarAwaitUsing(decl))
                            {
                                dpw.WriteKeyword("await ");
                                dpw.WriteKeyword("using ");
                            }
                            else
                            {
                                dpw.WriteKeyword("var ");
                            }
                        }
                    }
                    if (symbol.Name == InternalSymbolNameExportEquals && symbol.Parent is not null && (symbol.Parent.Flags & SymbolFlags.Module) != 0)
                    {
                        dpw.Write("exports");
                    }
                    else
                    {
                        WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                    }
                    if ((symbol.Flags & SymbolFlags.Optional) != 0)
                    {
                        dpw.WritePunctuation("?");
                    }
                    dpw.WritePunctuation(": ");
                }
                var callNode = GetCallOrNewExpression(node);
                if (callNode is not null)
                {
                    var callFlags = _typeFormatFlags | TypeFormatFlags.WriteTypeArgumentsOfSignature | TypeFormatFlags.WriteArrowStyleSignature;
                    if (IsCallExpression(callNode))
                    {
                        callFlags |= TypeFormatFlags.WriteCallStyleSignature;
                    }
                    WriteSignatureClassified(c.GetResolvedSignature(callNode)!, container, callFlags);
                }
                else
                {
                    var t = c.GetTypeOfSymbolAtLocation(symbol, node);
                    // If the type is a constrained type parameter, support expansion:
                    // Level 0: show just "T", signal canIncreaseVerbosity
                    // Level 1+: show "T extends Constraint" with the constraint expanded at level-1
                    if (vc is not null && t.Symbol() is not null && (t.Symbol()!.Flags & SymbolFlags.TypeParameter) != 0 && c.GetConstraintOfTypeParameter(t) is not null)
                    {
                        if (vc.Level > 0)
                        {
                            var expandVC = new VerbosityContext
                            {
                                Level = vc.Level - 1,
                                MaxTruncationLength = vc.MaxTruncationLength,
                            };
                            dpw.Write(TypeParameterToString(c, t, container, expandVC));
                            vc.CanIncreaseVerbosity = vc.CanIncreaseVerbosity || expandVC.CanIncreaseVerbosity;
                            vc.Truncated = vc.Truncated || expandVC.Truncated;
                        }
                        else
                        {
                            WriteTypeClassified(t, container, _typeFormatFlags);
                            vc.CanIncreaseVerbosity = true;
                        }
                    }
                    else
                    {
                        WriteTypeClassified(t, container, _typeFormatFlags);
                    }
                }
                SetDeclaration(OrElse(symbol.ValueDeclaration, FirstOrNil(symbol.Declarations)));
            }
            if ((flags & SymbolFlags.EnumMember) != 0)
            {
                WriteNewLine();
                dpw.WritePunctuation("(");
                dpw.Write("enum member");
                dpw.WritePunctuation(") ");
                var t = c.GetTypeOfSymbol(symbol);
                WriteTypeClassified(t, container, _typeFormatFlags);
                if ((t.Flags() & TypeFlags.Literal) != 0)
                {
                    dpw.WriteOperator(" = ");
                    dpw.WriteLiteral(t.AsLiteralType()!.ToString());
                }
                SetDeclaration(symbol.ValueDeclaration);
            }
            if ((flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0)
            {
                var isMethod = (flags & SymbolFlags.Method) != 0;
                var prefix = IfElse(isMethod, "method", "function ");
                if (IsIdentifier(node) && (IsFunctionLikeDeclaration(node.Parent!) || IsMethodSignatureDeclaration(node.Parent!)) && node.Parent!.Name() == node && Array.IndexOf(symbol.Declarations ?? [], node.Parent) >= 0)
                {
                    SetDeclaration(node.Parent);
                    var signatures = new[] { c.GetSignatureFromDeclaration(node.Parent!)! };
                    WriteSignatures(signatures, prefix, isMethod, symbol);
                }
                else
                {
                    var signatures = GetSignaturesAtLocation(c, symbol, SignatureKind.Call, node);
                    if (signatures.Length == 1)
                    {
                        var d = signatures[0].Declaration();
                        if (d is not null && (d.Flags & NodeFlags.JSDoc) == 0)
                        {
                            SetDeclaration(d);
                        }
                    }
                    WriteSignatures(signatures, prefix, isMethod, symbol);
                }
                SetDeclaration(symbol.ValueDeclaration);
            }
            if ((flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
            {
                if (node.Kind == Kind.ThisKeyword || IsThisInTypeQuery(node))
                {
                    WriteNewLine();
                    dpw.WriteKeyword("this");
                }
                else if (node.Kind == Kind.ConstructorKeyword && (IsConstructorDeclaration(node.Parent!) || IsConstructSignatureDeclaration(node.Parent!)))
                {
                    SetDeclaration(node.Parent);
                    var signatures = new[] { c.GetSignatureFromDeclaration(node.Parent!)! };
                    WriteSignatures(signatures, "constructor ", false, symbol);
                }
                else
                {
                    Signature[] signatures = [];
                    if ((flags & SymbolFlags.Class) != 0 && GetCallOrNewExpression(node) is not null)
                    {
                        signatures = GetSignaturesAtLocation(c, symbol, SignatureKind.Construct, node);
                    }
                    if (signatures.Length == 1)
                    {
                        var d = signatures[0].Declaration();
                        if (d is not null && (d.Flags & NodeFlags.JSDoc) == 0)
                        {
                            SetDeclaration(d);
                        }
                        WriteSignatures(signatures, "constructor ", false, symbol);
                    }
                    else
                    {
                        WriteNewLine();
                        if ((flags & SymbolFlags.Class) != 0)
                        {
                            var classExpression = GetDeclarationOfKind(symbol, Kind.ClassExpression);
                            if (classExpression is not null)
                            {
                                // Local class expression: show "(local class)" prefix
                                dpw.WritePunctuation("(");
                                dpw.Write("local class");
                                dpw.WritePunctuation(") ");
                            }
                            if (!TryExpandSymbol(symbol, flags))
                            {
                                if (classExpression is null)
                                {
                                    if (Some(symbol.Declarations, d => IsClassDeclaration(d) && HasAbstractModifier(d)))
                                    {
                                        dpw.WriteKeyword("abstract ");
                                    }
                                    dpw.WriteKeyword("class ");
                                }
                                WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                                var parameters = c.GetDeclaredTypeOfSymbol(symbol).AsInterfaceType()!.LocalTypeParameters();
                                WriteTypeParams(parameters);
                            }
                        }
                        else
                        {
                            if (!TryExpandSymbol(symbol, flags))
                            {
                                dpw.WriteKeyword("interface ");
                                WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                                var parameters = c.GetDeclaredTypeOfSymbol(symbol).AsInterfaceType()!.LocalTypeParameters();
                                WriteTypeParams(parameters);
                            }
                        }
                    }
                }
                if ((flags & SymbolFlags.Class) != 0)
                {
                    SetDeclaration(symbol.ValueDeclaration);
                }
                else
                {
                    SetDeclaration(Find(symbol.Declarations, IsInterfaceDeclaration));
                }
            }
            if ((flags & SymbolFlags.Enum) != 0)
            {
                WriteNewLine();
                if (!TryExpandSymbol(symbol, flags))
                {
                    if (Some(symbol.Declarations, d => IsEnumDeclaration(d) && IsEnumConst(d)))
                    {
                        dpw.WriteKeyword("const ");
                    }
                    dpw.WriteKeyword("enum ");
                    WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                }
                SetDeclaration(Find(symbol.Declarations, IsEnumDeclaration));
            }
            if ((flags & SymbolFlags.Module) != 0)
            {
                WriteNewLine();
                if (!TryExpandSymbol(symbol, flags))
                {
                    var isModule = symbol.ValueDeclaration is not null && (IsSourceFile(symbol.ValueDeclaration) || IsAmbientModule(symbol.ValueDeclaration));
                    dpw.WriteKeyword(IfElse(isModule, "module ", "namespace "));
                    WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                    WriteModuleImportAttributes(symbol);
                }
                SetDeclaration(Find(symbol.Declarations, IsModuleDeclaration));
            }
            if ((flags & SymbolFlags.TypeParameter) != 0)
            {
                WriteNewLine();
                dpw.WritePunctuation("(");
                dpw.Write("type parameter");
                dpw.WritePunctuation(") ");
                if (IsIdentifier(node) && IsTypeReferenceNode(node.Parent!) && IsDistributedTypeParameter(c.GetTypeAtLocation(node.Parent!)))
                {
                    dpw.WritePunctuation("(");
                    dpw.Write("distributed");
                    dpw.WritePunctuation(") ");
                }
                var tp = c.GetDeclaredTypeOfSymbol(symbol);
                WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                var cons = c.GetConstraintOfTypeParameter(tp);
                if (cons is not null)
                {
                    dpw.WriteKeyword(" extends ");
                    WriteTypeClassified(cons, container, _typeFormatFlags);
                }
                // Show context: "in ClassName<T>" or "in funcName<T>(...)"
                if (symbol.Parent is not null)
                {
                    // Class/Interface type parameter
                    dpw.WriteKeyword(" in ");
                    WriteSymbolClassified(symbol.Parent, container, SymbolFlags.None, _symbolFormatFlags);
                    var parentType = c.GetDeclaredTypeOfSymbol(symbol.Parent);
                    if (parentType.AsInterfaceType() is not null)
                    {
                        var parentParams = parentType.AsInterfaceType()!.LocalTypeParameters();
                        WriteTypeParams(parentParams);
                    }
                }
                else
                {
                    // Method/function type parameter
                    var decl = GetDeclarationOfKind(symbol, Kind.TypeParameter);
                    if (decl is not null && decl.Parent is not null)
                    {
                        var declaration = decl.Parent;
                        if (IsFunctionLike(declaration))
                        {
                            dpw.WriteKeyword(" in ");
                            if (declaration.Kind == Kind.ConstructSignature)
                            {
                                dpw.WriteKeyword("new ");
                            }
                            else if (declaration.Kind != Kind.CallSignature && declaration.Name() is not null)
                            {
                                WriteSymbolClassified(declaration.Symbol!, container, SymbolFlags.None, _symbolFormatFlags);
                            }
                            var sig = c.GetSignatureFromDeclaration(declaration);
                            if (sig is not null)
                            {
                                WriteSignatureClassified(sig, container, _typeFormatFlags | TypeFormatFlags.WriteTypeArgumentsOfSignature);
                            }
                        }
                        else if (IsTypeAliasDeclaration(declaration))
                        {
                            dpw.WriteKeyword(" in ");
                            dpw.WriteKeyword("type ");
                            WriteSymbolClassified(declaration.Symbol!, container, SymbolFlags.None, _symbolFormatFlags);
                            var declSymbol = declaration.Symbol;
                            if (declSymbol is not null)
                            {
                                var taParams = c.GetTypeAliasTypeParameters(declSymbol);
                                WriteTypeParams(taParams);
                            }
                        }
                    }
                }
                SetDeclaration(Find(symbol.Declarations, IsTypeParameterDeclaration));
            }
            if ((flags & SymbolFlags.TypeAlias) != 0)
            {
                WriteNewLine();
                dpw.WriteKeyword("type ");
                WriteSymbolClassified(symbol, container, SymbolFlags.None, _symbolFormatFlags);
                WriteTypeParams(c.GetTypeAliasTypeParameters(symbol));
                dpw.WriteOperator(" = ");
                Type typeAliasType;
                if (node.Parent is not null && AstStatic.IsConstTypeReference(node.Parent))
                {
                    typeAliasType = c.GetTypeAtLocation(node.Parent);
                }
                else
                {
                    typeAliasType = c.GetDeclaredTypeOfSymbol(symbol);
                }
                WriteTypeClassified(typeAliasType, container, _typeFormatFlags | TypeFormatFlags.InTypeAlias);
                SetDeclaration(Find(symbol.Declarations, IsTypeOrJSTypeAliasDeclaration));
            }
            if ((flags & SymbolFlags.Signature) != 0)
            {
                WriteNewLine();
                WriteTypeClassified(c.GetTypeOfSymbol(symbol), container, _typeFormatFlags);
            }
        }
        WriteSymbol(symbol);

        return new SymbolDisplayInfo { _displayParts = dpw, _declaration = firstDeclaration };
    }

    /// <summary>typeParameterToString renders a type parameter declaration (e.g., "T extends FooType").</summary>
    internal static string TypeParameterToString(CheckerType c, Type t, Node? enclosingDeclaration, VerbosityContext vc)
    {
        return c.TypeParameterToStringEx(t, enclosingDeclaration, vc);
    }

    internal static Node GetNodeForQuickInfo(Node node)
    {
        if (node.Parent is null)
        {
            return node;
        }
        if (IsNewExpression(node.Parent) && node.Pos() == node.Parent.Pos())
        {
            return node.Parent.Expression()!;
        }
        if (IsNamedTupleMember(node.Parent) && node.Pos() == node.Parent.Pos())
        {
            return node.Parent;
        }
        if (IsImportMeta(node.Parent) && node.Parent.Name() == node)
        {
            return node.Parent;
        }
        if (IsJsxNamespacedName(node.Parent))
        {
            return node.Parent;
        }
        return node;
    }

    internal static Symbol? GetSymbolAtLocationForQuickInfo(CheckerType c, Node node)
    {
        var objectElement = GetContainingObjectLiteralElement(node);
        if (objectElement is not null)
        {
            var contextualType = c.GetContextualType(objectElement.Parent!, ContextFlags.None);
            if (contextualType is not null)
            {
                var properties = c.GetPropertySymbolsFromContextualType(objectElement, contextualType, false /*unionSymbolOk*/);
                if (properties.Length == 1)
                {
                    return properties[0];
                }
            }
        }
        return c.GetSymbolAtLocation(node);
    }

    internal static Signature[] GetSignaturesAtLocation(CheckerType c, Symbol symbol, SignatureKind kind, Node node)
    {
        var signatures = c.GetSignaturesOfType(c.RemoveMissingOrUndefinedType(c.GetTypeOfSymbol(symbol)), kind);
        if (signatures.Length > 1 || (signatures.Length == 1 && signatures[0].TypeParameters().Length != 0))
        {
            var callNode = GetCallOrNewExpression(node);
            if (callNode is not null)
            {
                // We have a call or new expression, return the resolved signature
                return [c.GetResolvedSignature(callNode)!];
            }
        }
        return signatures;
    }

    internal static Node? GetCallOrNewExpression(Node node)
    {
        if (IsSourceFile(node))
        {
            return null;
        }
        if (IsPropertyAccessExpression(node.Parent!) && node.Parent!.Name() == node)
        {
            node = node.Parent;
        }
        if ((IsCallExpression(node.Parent!) || IsNewExpression(node.Parent!)) && node.Parent!.Expression() == node)
        {
            return node.Parent;
        }
        return null;
    }

    internal static bool ContainsTypedefTag(Node jsdoc)
    {
        if (jsdoc.Kind == Kind.JSDoc)
        {
            var tags = jsdoc.AsJSDoc()!.Tags;
            if (tags is not null)
            {
                foreach (var tag in tags.Nodes ?? [])
                {
                    if (tag.Kind == Kind.JSDocTypedefTag || tag.Kind == Kind.JSDocCallbackTag)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    internal static void WriteCode(StringBuilder b, string lang, string code)
    {
        if (code == "")
        {
            return;
        }
        var ticks = 3;
        while (code.Contains(new string('`', ticks), StringComparison.Ordinal))
        {
            ticks++;
        }
        for (var i = 0; i < ticks; i++)
        {
            b.Append('`');
        }
        b.Append(lang);
        b.Append('\n');
        b.Append(code);
        b.Append('\n');
        for (var i = 0; i < ticks; i++)
        {
            b.Append('`');
        }
        b.Append('\n');
    }

    internal static void WriteComments(DocumentationLocationMapper getMappedLocation, StringBuilder b, CheckerType c, Node[]? comments, bool isMarkdown)
    {
        foreach (var comment in comments ?? [])
        {
            switch (comment.Kind)
            {
                case Kind.JSDocText:
                    b.Append(comment.Text());
                    break;
                case Kind.JSDocLink:
                case Kind.JSDocLinkPlain:
                    WriteJSDocLink(getMappedLocation, b, c, comment, false /*quote*/, isMarkdown);
                    break;
                case Kind.JSDocLinkCode:
                    WriteJSDocLink(getMappedLocation, b, c, comment, true /*quote*/, isMarkdown);
                    break;
            }
        }
    }

    internal static void WriteJSDocLink(DocumentationLocationMapper getMappedLocation, StringBuilder b, CheckerType c, Node link, bool quote, bool isMarkdown)
    {
        var name = link.Name();
        var text = link.Text()!.Trim(' ');
        if (name is null)
        {
            WriteQuotedString(b, text, quote && isMarkdown);
            return;
        }
        if (IsIdentifier(name) && (name.Text() == "http" || name.Text() == "https") && text.StartsWith("://", StringComparison.Ordinal))
        {
            var linkText = name.Text() + text;
            var linkUri = linkText;
            var commentPos = linkText.IndexOfAny([' ', '|']);
            if (commentPos >= 0)
            {
                linkUri = linkText[..commentPos];
                linkText = TrimCommentPrefix(linkText[commentPos..]);
                if (linkText == "")
                {
                    linkText = linkUri;
                }
            }
            if (isMarkdown)
            {
                WriteMarkdownLink(b, linkText, linkUri, quote);
            }
            else
            {
                WriteQuotedString(b, linkText, false);
                if (linkText != linkUri)
                {
                    b.Append(" (");
                    b.Append(linkUri);
                    b.Append(")");
                }
            }
            return;
        }
        WriteNameLink(getMappedLocation, b, c, name, text, quote, isMarkdown);
    }

    internal static void WriteNameLink(DocumentationLocationMapper getMappedLocation, StringBuilder b, CheckerType c, Node name, string text, bool quote, bool isMarkdown)
    {
        var declarations = GetDeclarationsFromLocation(c, name);
        if (declarations.Length != 0)
        {
            var declaration = declarations[0];
            var file = GetSourceFileOfNode(declaration)!;
            var node = OrElse(GetNameOfDeclaration(declaration), declaration);
            var (loc, fidelity) = getMappedLocation(file, CreateRangeFromNode(node!, file));
            var prefixLen = IfElse(text.StartsWith("()", StringComparison.Ordinal), 2, 0);
            var linkText = TrimCommentPrefix(text[prefixLen..]);
            if (linkText == "")
            {
                linkText = GetEntityNameString(name) + text[..prefixLen];
            }
            if (isMarkdown && fidelity.IsSingleSegment())
            {
                var linkUri = $"{loc.Uri.Value}#{loc.Range.Start.Line + 1},{loc.Range.Start.Character + 1}-{loc.Range.End.Line + 1},{loc.Range.End.Character + 1}";
                WriteMarkdownLink(b, linkText, linkUri, quote);
            }
            else
            {
                WriteQuotedString(b, linkText, false);
            }
            return;
        }
        WriteQuotedString(b, GetEntityNameString(name) + IfElse(text.Length != 0, " ", "") + text, quote && isMarkdown);
    }

    internal static string TrimCommentPrefix(string text)
    {
        var s = text.TrimStart(' ');
        if (s.StartsWith('|'))
        {
            s = s[1..];
        }
        return s.TrimStart(' ');
    }

    internal static void WriteMarkdownLink(StringBuilder b, string text, string uri, bool quote)
    {
        b.Append('[');
        WriteQuotedString(b, text, quote);
        b.Append("](");
        b.Append(uri);
        b.Append(')');
    }

    internal static void WriteOptionalEntityName(StringBuilder b, Node? name)
    {
        if (name is not null)
        {
            b.Append(" ");
            WriteQuotedString(b, GetEntityNameString(name), true /*quote*/);
        }
    }

    internal static void WriteQuotedString(StringBuilder b, string str, bool quote)
    {
        if (quote && !str.Contains('`'))
        {
            b.Append('`');
            b.Append(str);
            b.Append('`');
        }
        else
        {
            b.Append(str);
        }
    }

    internal static string GetEntityNameString(Node name)
    {
        var b = new StringBuilder();
        WriteEntityNameParts(b, name);
        return b.ToString();
    }

    internal static void WriteEntityNameParts(StringBuilder b, Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
                b.Append(node.Text());
                break;
            case Kind.QualifiedName:
                WriteEntityNameParts(b, node.AsQualifiedName()!.Left!);
                b.Append('.');
                WriteEntityNameParts(b, node.AsQualifiedName()!.Right!);
                break;
            case Kind.PropertyAccessExpression:
                WriteEntityNameParts(b, node.Expression()!);
                b.Append('.');
                WriteEntityNameParts(b, node.Name()!);
                break;
            case Kind.ParenthesizedExpression:
            case Kind.ExpressionWithTypeArguments:
                WriteEntityNameParts(b, node.Expression()!);
                break;
            case Kind.JSDocNameReference:
                WriteEntityNameParts(b, node.Name()!);
                break;
        }
    }
}

public sealed partial class LanguageService
{
    public (HoverOrNull Response, Exception? Error) ProvideHover(Context ctx, HoverParams @params)
    {
        var caps = GetClientCapabilities();
        var contentFormat = PreferredMarkupKind(caps.TextDocument.Hover.ContentFormat);

        var verbosityLevel = 0;
        if (@params.VerbosityLevel is not null)
        {
            verbosityLevel = @params.VerbosityLevel.Value;
        }

        var (program, file) = GetProgramAndFile(@params.TextDocument.Uri);
        var positions = _converters!.FromLSPPositionForSourceFile(file, @params.Position, Feature.Hover);
        var hovers = new List<Hover>();
        foreach (var projection in positions)
        {
            if (!projection.Fidelity.IsSingleSegment())
            {
                continue;
            }
            file = projection.Script;
            var position = (int)projection.Position;
            var node = GetTouchingPropertyName(file, position);
            if (IsSourceFile(node) || (IsPropertyAccessOrQualifiedName(node) && Ls.IsInComment(file, position, node) is null))
            {
                // Avoid giving quickInfo for the sourceFile as a whole or inside the comment of a/**/.b
                continue;
            }
            var (c, done) = program.GetTypeCheckerForFile(ctx, file);
            var rangeNode = GetNodeForQuickInfo(node);
            var symbol = GetSymbolAtLocationForQuickInfo(c, rangeNode);

            // Always create VerbosityContext for hover so that canExpandSymbol can signal
            // canIncreaseVerbosity even at Level 0. The nodebuilder also detects expandable
            // types at Level 0 via shouldExpandType (maxExpansionDepth = 0).
            var maxTruncLen = UserPreferences().MaximumHoverLength;
            if (maxTruncLen <= 0)
            {
                maxTruncLen = 500;
            }
            var vc = new VerbosityContext
            {
                Level = verbosityLevel,
                MaxTruncationLength = maxTruncLen,
            };

            var vsCapability = caps.VSSupportsVisualStudioExtensions;
            var (quickInfo, documentation, vsDocumentation, quickInfoRuns) = GetQuickInfoAndDocumentationForSymbol(c, symbol, rangeNode, contentFormat, vc, vsCapability);
            if (quickInfo == "")
            {
                done();
                continue;
            }
            var rangeFile = GetSourceFileOfNode(rangeNode)!;
            var textRange = Ls.GetRangeOfNode(rangeNode, rangeFile, null /*endNode*/);
            var (hoverRange, hoverFidelity) = _converters!.ToLSPRangeForFeature(rangeFile, textRange, Feature.Hover);

            string content;
            if (contentFormat == MarkupKind.Markdown)
            {
                content = Ls.FormatQuickInfo(quickInfo) + documentation;
            }
            else
            {
                content = quickInfo + documentation;
            }

            var hover = new Hover
            {
                Contents = new MarkupContentOrStringOrMarkedStringWithLanguageOrMarkedStrings
                {
                    MarkupContent = new MarkupContent
                    {
                        Kind = contentFormat,
                        Value = content,
                    },
                },
            };
            if (hoverFidelity.IsSingleSegment())
            {
                hover.Range = hoverRange;
            }

            if (caps.Experimental.HoverVerbosityLevel)
            {
                hover.CanIncreaseVerbosity = vc.CanIncreaseVerbosity && !vc.Truncated;
            }

            // Clients that support Visual Studio extensions (e.g. VS itself, when Corsa/Native TS Preview is
            // enabled) render `_vs_rawContent` in place of `contents`. Without it, VS shows plain markdown
            // with no symbol icon and no syntax coloring, unlike the legacy TSServer-backed hover path.
            if (vsCapability && quickInfoRuns.Length > 0)
            {
                var kind = ScriptElementKind.Keyword;
                var modifiers = ScriptElementKindModifier.None;
                if (symbol is not null)
                {
                    // Resolve aliases to their target before computing the icon kind, so e.g. `import { x }`
                    // shows the icon for whatever `x` actually is (const, function, ...) rather than a
                    // generic alias icon. GetSymbolModifiers already accounts for the alias target itself.
                    var iconSymbol = symbol;
                    if ((symbol.Flags & SymbolFlags.Alias) != 0)
                    {
                        var resolved = c.GetAliasedSymbol(symbol);
                        if (resolved is not null && resolved != symbol)
                        {
                            iconSymbol = resolved;
                        }
                    }
                    kind = GetSymbolKind(c, iconSymbol, rangeNode);
                    modifiers = GetSymbolModifiers(c, symbol);
                }
                var imageId = GetVSHoverImageId(kind, modifiers);
                VSClassifiedTextRun[]? documentationRuns = null;
                var docText = vsDocumentation.TrimStart('\n');
                if (docText != "")
                {
                    documentationRuns = [new VSClassifiedTextRun { ClassificationTypeName = ClassificationTypeName.Text.Value, Text = docText }];
                }
                hover.VSRawContent = BuildVSHoverRawContent(imageId, quickInfoRuns, documentationRuns);
            }

            done();
            hovers.Add(hover);
        }
        if (hovers.Count == 0)
        {
            return (new HoverOrNull(), null);
        }
        if (hovers.Count == 1)
        {
            return (new HoverOrNull { Hover = hovers[0] }, null);
        }

        var combined = hovers[0];
        var contents = new List<string>(hovers.Count);
        var seenContents = new Set<string>();
        var rawContents = new List<VSImageElementOrClassifiedTextElementOrContainerElement>();
        var commonRange = combined.Range;
        foreach (var hover in hovers)
        {
            var content = hover.Contents.MarkupContent!.Value.TrimEnd('\n');
            if (seenContents.AddIfAbsent(content))
            {
                contents.Add(content);
                if (hover.VSRawContent is not null)
                {
                    rawContents.Add(new VSImageElementOrClassifiedTextElementOrContainerElement { ContainerElement = hover.VSRawContent });
                }
            }
            combined.CanIncreaseVerbosity = combined.CanIncreaseVerbosity || hover.CanIncreaseVerbosity;
            if (commonRange is null || hover.Range is null || commonRange.Compare(hover.Range) != 0)
            {
                commonRange = null;
            }
        }
        var separator = "\n\n";
        if (contentFormat == MarkupKind.Markdown)
        {
            separator = "\n\n---\n\n";
        }
        combined.Contents.MarkupContent!.Value = string.Join(separator, contents);
        combined.Range = commonRange;
        switch (rawContents.Count)
        {
            case 0:
                combined.VSRawContent = null;
                break;
            case 1:
                combined.VSRawContent = rawContents[0].ContainerElement;
                break;
            default:
                combined.VSRawContent = new VSContainerElement
                {
                    Style = VSContainerElementStyle.Stacked,
                    Elements = rawContents.ToArray(),
                };
                break;
        }
        return (new HoverOrNull { Hover = combined }, null);
    }

    internal (string QuickInfo, string Documentation, string VSDocumentation, VSClassifiedTextRun[] QuickInfoRuns) GetQuickInfoAndDocumentationForSymbol(
        CheckerType c,
        Symbol? symbol,
        Node node,
        MarkupKind contentFormat,
        VerbosityContext vc,
        bool vsCapability)
    {
        var info = Ls.GetQuickInfoAndDeclarationAtLocation(c, symbol, node, vc, vsCapability, Ls.GetMeaningFromLocation(node));
        var quickInfo = info._displayParts.String();
        if (quickInfo == "")
        {
            return ("", "", "", []);
        }
        var quickInfoRuns = info._displayParts.GetRuns();

        var documentation = Ls.GetDocumentationForSymbol(DocumentationLocationMapper(Feature.Hover), c, symbol, node, info._declaration, contentFormat, false /*commentOnly*/);

        // VS's rich hover (_vs_rawContent) renders documentation as plain colorized text with no Markdown
        // parser, so it can't use the tag section (@param/@returns/@example/@see, etc.) that
        // getDocumentationFromDeclaration renders with '*@tag*' bolding and ```-fenced @example blocks --
        // those would show up as literal asterisks/backticks. This also matches the legacy TSServer-backed
        // VS hover (TypeScript-VS's HoverService.cs), which only ever surfaced the JSDoc summary
        // (TSServer's quickinfo `documentation`) and never included the tag section at all (TSServer
        // exposes tags via a separate `tags` field that legacy VS hover never read). So request
        // comment-only, plain-text documentation for the VS path instead of reusing `documentation`.
        var vsDocumentation = "";
        if (vsCapability)
        {
            vsDocumentation = Ls.GetDocumentationForSymbol(DocumentationLocationMapper(Feature.Hover), c, symbol, node, info._declaration, MarkupKind.PlainText, true /*commentOnly*/);
        }

        return (quickInfo, documentation, vsDocumentation, quickInfoRuns);
    }

    internal DocumentationLocationMapper DocumentationLocationMapper(Feature feature)
    {
        return (SourceFile file, TextRange fileRange) => SourceFileRangeToLSPLocationForFeature(file, fileRange, feature);
    }
}
