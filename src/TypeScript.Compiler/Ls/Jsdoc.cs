// ported from: internal/ls/jsdoc.go:1-312
//
// 与 Go 的偏离（逐条说明）：
//
// 1) 包级**导出**函数 `GetSymbolDocumentationComment` / `GetSymbolJSDocTags` → 静态类 `Ls` 的 `public static`
//    成员（G1 约定 §0）；包级未导出函数（`declarationJSDocTags` / `getJSDocTagText` / `getJSDoc` / `getJSDocOrTag` /
//    `getMatchingJSDocTag` / `getJSDocParameterTagByPosition` / `isMatchingParameterTag` / `isMatchingTemplateTag` /
//    `isNodeWithName` / `noMappedLocation`）→ `internal static` + PascalCase（方法不加 `_`）。
//
// 2) Go 的 `JSDocTagInfo`（导出类型）保持同名 `JSDocTagInfo`，导出字段 `Name` / `Text` 保 PascalCase。
//
// 3) Go 的 nil 切片按本仓约定落成"非 null 数组"：`GetSymbolJSDocTags` 返回 `JSDocTagInfo[]`（无标签时 `[]`，
//    调用点只用 len/range ⇒ 与 Go 的 nil 切片等价）；`declarationJSDocTags` 返回 `Node[]?`（Go 的 nil 有
//    "没有 JSDoc 位置链"语义，调用点确实判空）。
//
// 4) `collections.Set[T]{}` 的 Go 零值 → `new Set<T>()`（C# 必须显式构造；空集合语义相同）；`seen.AddIfAbsent`
//    同名。`seenSymbols *collections.Set[*ast.Symbol]` 形参 → `Set<Symbol> seenSymbols`（Go 侧调用点从不传 nil）。
//
// 5) `getJSDocOrTag` 的 Go `switch { case cond: … }` 逐条落成 if/else-if：Go 的每个 case 体都以 `return` 结束
//    （最后一支 binding-element 除外），所以"首个命中即执行并离开 switch"与 if/else-if 完全等价。
//
// 6) `strings.Builder` → `StringBuilder`；`strings.Join(parts, "\n")` → `string.Join("\n", parts)`；
//    `slices.Contains(parts, doc)` → `List<string>.Contains`。
//
// 7) `core.Some/FirstOrNil/LastOrNil/Find` → `Some/FirstOrNil/LastOrNil/Find`（`using static TypeScript.Core.Core;`）。
//
// 8) `tag.AsJSDocXxx()` 返回可空 ⇒ 补 `!`；`tag.TagName()` 在 Go 里可能为 nil，C# 同样可空 ⇒ `!`（调用点都在
//    已确认有 TagName 的 JSDoc tag 上）。
//
// 9) `noMappedLocation` 的 Go 零值 `lsproto.Location{}` → `new Location()`（class，字段自身有初始化器）。
using System;
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using TypeScript.Checker;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.LsProto;
using TypeScript.Scanner;
using TypeScript.SpanMap;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
// 同名类型消歧（`using System` / `using TypeScript.LsProto`；先例 Ls/Utilities.cs）：
using CheckerType = TypeScript.Checker.Checker;
using Kind = TypeScript.Ast.Kind;

namespace TypeScript.Ls;

/// <summary>
/// JSDocTagInfo mirrors Strada's `JSDocTagInfo`, but renders the tag's text as a
/// plain string instead of `SymbolDisplayPart[]`.
/// </summary>
public sealed class JSDocTagInfo
{
    public string Name = "";

    public string Text = "";
}

public static partial class Ls
{
    /// <summary>
    /// GetSymbolDocumentationComment renders a symbol's documentation comment as plain text.
    /// It backs the API's Symbol.getDocumentationComment and mirrors Strada's
    /// getJsDocCommentsFromDeclarations: comments are gathered from each unique declaration,
    /// deduplicated, and joined with line breaks. Like Strada, it does not resolve aliases —
    /// consumers resolve aliases themselves (via getAliasedSymbol) and re-query if desired.
    /// </summary>
    public static string GetSymbolDocumentationComment(CheckerType c, Symbol? symbol)
    {
        if (symbol is null)
        {
            return "";
        }
        var parts = new List<string>();
        var seen = new Set<Node>();
        foreach (var decl in symbol.Declarations ?? [])
        {
            if (decl is null)
            {
                continue;
            }
            if (!seen.AddIfAbsent(decl))
            {
                continue;
            }
            var doc = GetDocumentationFromDeclaration(NoMappedLocation, c, symbol, decl, decl, MarkupKind.PlainText, true /*commentOnly*/);
            if (doc != "" && !parts.Contains(doc))
            {
                parts.Add(doc);
            }
        }
        return string.Join("\n", parts);
    }

    /// <summary>
    /// GetSymbolJSDocTags collects a symbol's JSDoc tags. It backs the API's Symbol.getJsDocTags
    /// and mirrors Strada's getJsDocTagsFromDeclarations, except each tag's text is rendered as a
    /// plain string rather than SymbolDisplayPart[]. Tags with no text have an empty Text field.
    /// </summary>
    public static JSDocTagInfo[] GetSymbolJSDocTags(Symbol? symbol)
    {
        if (symbol is null)
        {
            return [];
        }
        var infos = new List<JSDocTagInfo>();
        var seen = new Set<Node>();
        foreach (var decl in symbol.Declarations ?? [])
        {
            if (decl is null)
            {
                continue;
            }
            if (!seen.AddIfAbsent(decl))
            {
                continue;
            }
            var tags = DeclarationJSDocTags(decl);
            // Skip comments containing @typedef/@callback since they're not associated with a
            // particular declaration, unless they also carry @param/@return (treated as local docs).
            var hasTypedef = Some(tags, t => t.Kind == Kind.JSDocTypedefTag || t.Kind == Kind.JSDocCallbackTag);
            var hasParamOrReturn = Some(tags, t => t.Kind == Kind.JSDocParameterTag || t.Kind == Kind.JSDocReturnTag);
            if (hasTypedef && !hasParamOrReturn)
            {
                continue;
            }
            foreach (var tag in tags ?? [])
            {
                infos.Add(new JSDocTagInfo { Name = tag.TagName()!.Text(), Text = GetJSDocTagText(tag) });
            }
        }
        return infos.ToArray();
    }

    /// <summary>
    /// declarationJSDocTags returns the JSDoc tags associated with a declaration, walking the
    /// JSDoc comment location chain like the checker's getAllJSDocTags.
    /// </summary>
    internal static Node[]? DeclarationJSDocTags(Node node)
    {
        if ((node.Flags & NodeFlags.JSDoc) == 0)
        {
            for (Node? current = node; current is not null; current = GetNextJSDocCommentLocation(current))
            {
                var jsdocs = current.JSDoc(null);
                if (jsdocs is null || jsdocs.Length == 0)
                {
                    continue;
                }
                var lastJSDoc = jsdocs[^1].AsJSDoc()!;
                if (lastJSDoc.Tags is not null)
                {
                    return lastJSDoc.Tags.Nodes;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// getJSDocTagText renders the text of a single JSDoc tag as a plain string, mirroring
    /// Strada's getCommentDisplayParts collapsed from SymbolDisplayPart[] to a string.
    /// </summary>
    internal static string GetJSDocTagText(Node tag)
    {
        var comment = Scanner.Scanner.GetTextOfJSDocComment(tag.CommentList());
        string AddComment(string s)
        {
            if (comment == "")
            {
                return s;
            }
            return s + " " + comment;
        }
        switch (tag.Kind)
        {
            case Kind.JSDocThrowsTag:
                var te = tag.AsJSDocThrowsTag()!.TypeExpression;
                if (te is not null)
                {
                    return AddComment(Scanner.Scanner.GetTextOfNode(te));
                }
                return comment;
            case Kind.JSDocImplementsTag:
                return AddComment(Scanner.Scanner.GetTextOfNode(tag.AsJSDocImplementsTag()!.ClassName!));
            case Kind.JSDocAugmentsTag:
                return AddComment(Scanner.Scanner.GetTextOfNode(tag.AsJSDocAugmentsTag()!.ClassName!));
            case Kind.JSDocTemplateTag:
                var templateTag = tag.AsJSDocTemplateTag()!;
                var b = new StringBuilder();
                if (templateTag.Constraint is not null)
                {
                    b.Append(Scanner.Scanner.GetTextOfNode(templateTag.Constraint));
                }
                var typeParameters = templateTag.TypeParameters;
                if (typeParameters is not null)
                {
                    var parameters = typeParameters.Nodes ?? [];
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        if (i == 0 && b.Length != 0)
                        {
                            b.Append(" ");
                        }
                        if (i != 0)
                        {
                            b.Append(", ");
                        }
                        b.Append(Scanner.Scanner.GetTextOfNode(parameters[i]));
                    }
                }
                if (comment != "")
                {
                    if (b.Length != 0)
                    {
                        b.Append(" ");
                    }
                    b.Append(comment);
                }
                return b.ToString();
            case Kind.JSDocTypeTag:
                return AddComment(Scanner.Scanner.GetTextOfNode(tag.AsJSDocTypeTag()!.TypeExpression!));
            case Kind.JSDocSatisfiesTag:
                return AddComment(Scanner.Scanner.GetTextOfNode(tag.AsJSDocSatisfiesTag()!.TypeExpression!));
            case Kind.JSDocSeeTag:
                var ne = tag.AsJSDocSeeTag()!.NameExpression;
                if (ne is not null)
                {
                    return AddComment(Scanner.Scanner.GetTextOfNode(ne));
                }
                return comment;
            case Kind.JSDocParameterTag:
            case Kind.JSDocPropertyTag:
                var name = tag.Name();
                if (name is not null)
                {
                    return AddComment(Scanner.Scanner.GetTextOfNode(name));
                }
                return comment;
            default:
                return comment;
        }
    }

    internal static Node? GetJSDoc(Node node)
    {
        return LastOrNil(node.JSDoc(null));
    }

    internal static Node? GetJSDocOrTag(CheckerType c, Node? node, Set<Symbol> seenSymbols)
    {
        if (node is null)
        {
            return null;
        }
        var jsdoc = GetJSDoc(node);
        if (jsdoc is not null)
        {
            return jsdoc;
        }
        if (IsParameterDeclaration(node))
        {
            var name = node.Name()!;
            if (IsBindingPattern(name))
            {
                // For binding patterns, match JSDoc @param tags by position rather than by name
                return GetJSDocParameterTagByPosition(c, node);
            }
            return GetMatchingJSDocTag(c, node.Parent!, name.Text(), IsMatchingParameterTag, seenSymbols);
        }
        if (IsTypeParameterDeclaration(node))
        {
            return GetMatchingJSDocTag(c, node.Parent!, node.Name()!.Text(), IsMatchingTemplateTag, seenSymbols);
        }
        if (IsVariableDeclaration(node) && node.Parent is { } variableDeclarationListParent && IsVariableDeclarationList(variableDeclarationListParent) && FirstOrNil(variableDeclarationListParent.AsVariableDeclarationList()!.Declarations!.Nodes) == node)
        {
            return GetJSDocOrTag(c, node.Parent!.Parent, seenSymbols);
        }
        if ((IsFunctionExpressionOrArrowFunction(node) || IsClassExpression(node)) &&
            (IsVariableDeclaration(node.Parent!) || IsPropertyDeclaration(node.Parent!) || IsPropertyAssignment(node.Parent!)) &&
            node.Parent!.Initializer() == node)
        {
            return GetJSDocOrTag(c, node.Parent, seenSymbols);
        }
        if (IsBindingElement(node) && IsObjectBindingPattern(node.Parent!))
        {
            var name = node.PropertyNameOrName();
            if (IsIdentifier(name!))
            {
                var objectType = c.GetTypeAtLocation(node.Parent!);
                if (objectType is not null)
                {
                    var prop = c.GetPropertyOfType(objectType, name!.Text());
                    if (prop is not null)
                    {
                        foreach (var d in prop.Declarations ?? [])
                        {
                            var dJsdoc = GetJSDoc(d);
                            if (dJsdoc is not null)
                            {
                                return dJsdoc;
                            }
                        }
                    }
                }
            }
        }
        var symbol = node.Symbol;
        if (symbol is not null && node.Parent is not null)
        {
            if (IsFunctionDeclaration(node) || IsMethodDeclaration(node) || IsMethodSignatureDeclaration(node) || IsConstructorDeclaration(node) || IsConstructSignatureDeclaration(node))
            {
                var firstSignature = Find(symbol.Declarations, d => IsFunctionLike(d));
                if (firstSignature is not null && node != firstSignature)
                {
                    var jsDoc = GetJSDocOrTag(c, firstSignature, seenSymbols);
                    if (jsDoc is not null)
                    {
                        return jsDoc;
                    }
                }
            }
            if (IsClassOrInterfaceLike(node.Parent))
            {
                var isStatic = HasStaticModifier(node);
                var classType = c.GetDeclaredTypeOfSymbol(node.Parent.Symbol!);
                if (isStatic)
                {
                    // For static members, use the checker's base constructor type resolution.
                    // This correctly handles intersection constructor types from mixins
                    // (e.g., typeof MixinClass & T) by preserving the full intersection.
                    var staticBaseType = c.GetApparentType(c.GetBaseConstructorTypeOfClass(classType));
                    var prop = c.GetPropertyOfType(staticBaseType, symbol.Name);
                    if (prop is not null && prop.ValueDeclaration is not null && seenSymbols.AddIfAbsent(prop))
                    {
                        var jsDoc = GetJSDocOrTag(c, prop.ValueDeclaration, seenSymbols);
                        if (jsDoc is not null)
                        {
                            return jsDoc;
                        }
                    }
                }
                else
                {
                    foreach (var baseType in c.GetBaseTypes(classType))
                    {
                        var prop = c.GetPropertyOfType(baseType, symbol.Name);
                        if (prop is not null && prop.ValueDeclaration is not null && seenSymbols.AddIfAbsent(prop))
                        {
                            var jsDoc = GetJSDocOrTag(c, prop.ValueDeclaration, seenSymbols);
                            if (jsDoc is not null)
                            {
                                return jsDoc;
                            }
                        }
                    }
                }
            }
        }
        return null;
    }

    internal static Node? GetMatchingJSDocTag(CheckerType c, Node? node, string name, Func<Node, string, bool> match, Set<Symbol> seenSymbols)
    {
        var jsdoc = GetJSDocOrTag(c, node, seenSymbols);
        if (jsdoc is not null && jsdoc.Kind == Kind.JSDoc)
        {
            var tags = jsdoc.AsJSDoc()!.Tags;
            if (tags is not null)
            {
                foreach (var tag in tags.Nodes ?? [])
                {
                    if (match(tag, name))
                    {
                        return tag;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// getJSDocParameterTagByPosition finds a JSDoc @param tag for a binding pattern parameter by position.
    /// Since binding patterns don't have a simple name, we match the @param tag at the same index as the parameter.
    /// </summary>
    internal static Node? GetJSDocParameterTagByPosition(CheckerType c, Node param)
    {
        var parent = param.Parent;
        if (parent is null)
        {
            return null;
        }

        // Find the parameter's index in the parent's parameters list
        var parameters = parent.Parameters();
        var paramIndex = -1;
        for (var i = 0; i < (parameters?.Length ?? 0); i++)
        {
            if (parameters![i].AsNode() == param)
            {
                paramIndex = i;
                break;
            }
        }
        if (paramIndex < 0)
        {
            return null;
        }

        // Get the JSDoc for the parent function/method
        var jsdoc = GetJSDocOrTag(c, parent, new Set<Symbol>());
        if (jsdoc is null || jsdoc.Kind != Kind.JSDoc)
        {
            return null;
        }

        // Collect all @param tags in order
        var tags = jsdoc.AsJSDoc()!.Tags;
        if (tags is null)
        {
            return null;
        }

        var paramTagIndex = 0;
        foreach (var tag in tags.Nodes ?? [])
        {
            if (tag.Kind == Kind.JSDocParameterTag)
            {
                if (paramTagIndex == paramIndex)
                {
                    return tag;
                }
                paramTagIndex++;
            }
        }
        return null;
    }

    internal static bool IsMatchingParameterTag(Node tag, string name)
    {
        return tag.Kind == Kind.JSDocParameterTag && IsNodeWithName(tag, name);
    }

    internal static bool IsMatchingTemplateTag(Node tag, string name)
    {
        return tag.Kind == Kind.JSDocTemplateTag && Some(tag.TypeParameters(), tp => IsNodeWithName(tp, name));
    }

    internal static bool IsNodeWithName(Node node, string name)
    {
        var nodeName = node.Name();
        return IsIdentifier(nodeName!) && nodeName!.Text() == name;
    }

    internal static (Location Location, Fidelity Fidelity) NoMappedLocation(SourceFile file, TextRange range)
    {
        return (new Location(), Fidelity.None);
    }
}
