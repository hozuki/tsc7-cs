// ported from: internal/parser/reparser.go:1-752（JSDoc 惰性重解析：reparse*、gatherTypeParameters、wrapInJSDocNamespace；W6 分片，整文件）
//
// JSDoc 的惰性重解析（reparse*）：把 JSDoc 里的类型/tag 转成真正的 AST 节点（箭头函数/类型断言/类型参数等的
// 重解析入口都由 parser.go 调用本文件的成员）。
//
// 与 Go 的偏离（逐条；依据 docs/architecture.md §2/§3.1/§3.2/§3.8 与 W1 冻结的 Parser 字段契约）：
//
// 1) 包级函数（`findMatchingParameter`/`skipSatisfiesExpressions`/`getFunctionLikeHost`/`getClassLikeData`）→
//    同一个 `partial class Parser` 上的 `private static` 方法（一个 Go package = 一个 C# 类型，docs §2）。
// 2) Go 的 `(T, bool)` 返回值 → `bool TryX(..., out T)`（`findMatchingParameter` →
//    `TryFindMatchingParameter`）；失败时 `out` 参数给 Go 的零值。
// 3) Go 的 `n.AsMutable().SetX(...)` → C# `n.SetX(...)`（`Node.AsMutable()` 返回 `this`，Node.cs:151；四个
//    Set* 直接落在 Node 上）。
// 4) Go 的 `append(slice, x)`（写回字段）→ C# 集合表达式 `[.. field ?? [], x]`（数组不可变长，语义一致）；
//    Go 的"预占容量再 append"（`arena.NewSlice(0)`）→ `List<T>` + `ToArray()`（§3.2 的 Arena 条目）。
//    nil 切片可迭代 → 先判空再 foreach（C# 的 foreach 遇 null 抛 NRE，Go 是空循环）。
// 5) Go 的 `p.reparseList = append(p.reparseList, x)` → `_reparseList.Add(x)`（W1：该 List 不可整体替换）；
//    `p.reparsedClones`/`p.jsdocInfos` 同理。
// 6) Go 的 `node.AsXxx()` 后立刻解引用（`*ast.Node`→ nil 会 panic）→ C# 的 `!`：C# 抛 NRE，等价（W4 文件头
//    同款约定）。`p.addDeepCloneReparse`/`p.getInnermostNameOfJSDocNamespace` 等**可能返回 nil**，故有 `!`。
// 7) Go 的 `func(...)` 闭包 → lambda（C# 的实例方法组不能转成 `Func<...>`，见 W2/Main 的约定）；
//    不捕获变量的写 `static n => ...`（无闭包分配）。
// 8) `ast.NewDiagnostic(nil, …)` → `NewDiagnostic(null, …)`；消息名与 Go 逐字一致（`using static` 后裸名）。
// 9) 三元与 `core.IfElse`/`core.Map`/`core.Every`/`core.Find` 用 `using static TypeScript.Core.Core;` 后的裸名。
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Parser;

public sealed partial class Parser
{
    // ported from: internal/parser/reparser.go:14-18
    private void FinishReparsedNode(Node node, Node locationNode)
    {
        node.Flags = _contextFlags | NodeFlags.Reparsed;
        node.Loc = locationNode.Loc;
        OverrideParentInImmediateChildren(node);
    }

    // ported from: internal/parser/reparser.go:20-22
    private void FinishMutatedNode(Node node)
    {
        OverrideParentInImmediateChildren(node);
    }

    /// <summary>
    /// Deep-clone the given node and add the clone to the reparsed clone list. The list is used by ast.GetReparsedNodeForNode
    /// to locate reparsed clones of JSDoc nodes. Since the binder attaches symbols to reparsed nodes and not to JSDoc nodes, we
    /// need the mapping when obtaining symbols and types from JSDoc nodes.
    /// </summary>
    // ported from: internal/parser/reparser.go:24-31
    private Node? AddDeepCloneReparse(Node? node)
    {
        var clone = _factory.DeepCloneReparse(node);
        if (clone is not null)
        {
            _reparsedClones.Add(clone);
        }

        return clone;
    }

    // ported from: internal/parser/reparser.go:33-38
    private Node AddTransformedReparse(Node newNode, Node old)
    {
        FinishReparsedNode(newNode, old);
        newNode.Flags |= NodeFlags.ReparserTransformedLiteral;
        _reparsedClones.Add(newNode);
        return newNode;
    }

    // ported from: internal/parser/reparser.go:40-52
    private Node? CheckNonIdentifierName(Node? name)
    {
        // Handles the case of anonymous functions
        if (name is null)
        {
            return null;
        }

        if (TypeScript.Ast.Ast.IsIdentifier(name) && !IsValidIdentifier(name.AsIdentifier()!.Text))
        {
            var errLoc = name.Loc;
            if (errLoc.Len() == 0)
            {
                // missing name, emit error on the character before the missing name node
                errLoc = NewTextRange(name.Loc.Pos() - 1, name.Loc.Pos());
            }

            ParseErrorAtRange(errLoc, Identifier_expected);
        }

        return name;
    }

    /// <summary>
    /// Hosted tags find a host and add their children to the correct location under the host.
    /// Unhosted tags add synthetic nodes to the reparse list.
    /// </summary>
    // ported from: internal/parser/reparser.go:54-68
    private void ReparseTags(Node parent, List<Node> jsDoc)
    {
        for (var i = 0; i < jsDoc.Count; i++)
        {
            var j = jsDoc[i];
            var isLast = ReferenceEquals(j, jsDoc[^1]);
            var tags = j.AsJSDoc()!.Tags;
            if (tags?.Nodes is null)
            {
                continue;
            }

            foreach (var tag in tags.Nodes)
            {
                ReparseUnhosted(tag, parent, j);
                if (isLast)
                {
                    ReparseHosted(tag, parent, j);
                }
            }
        }
    }

    // ported from: internal/parser/reparser.go:70-156
    private void ReparseUnhosted(Node tag, Node parent, Node jsDoc)
    {
        switch (tag.Kind)
        {
            case Kind.JSDocTypedefTag:
                {
                    var typeExpression = tag.TypeExpression();
                    if (typeExpression is null)
                    {
                        break;
                    }

                    var fullName = tag.Name();
                    var isNamespace = fullName is not null && IsModuleDeclaration(fullName);
                    ModifierList? modifiers = null;
                    if (isNamespace)
                    {
                        modifiers = CreateExportModifier(tag);
                    }

                    var typeAlias = _factory.NewJSTypeAliasDeclaration(
                        modifiers,
                        AddDeepCloneReparse(CheckNonIdentifierName(GetInnermostNameOfJSDocNamespace(fullName))),
                        null,
                        null);
                    typeAlias.AsTypeAliasDeclaration()!.TypeParameters = GatherTypeParameters(jsDoc, true /*typedefOrCallback*/);
                    Node? t;
                    switch (typeExpression.Kind)
                    {
                        case Kind.JSDocTypeExpression:
                            t = AddDeepCloneReparse(typeExpression.Type());
                            break;
                        case Kind.JSDocTypeLiteral:
                            t = ReparseJSDocTypeLiteral(typeExpression);
                            break;
                        default:
                            throw new DebugFailureException("typedef tag type expression should be a name reference or a type expression" + typeExpression.Kind.String());
                    }

                    typeAlias.AsTypeAliasDeclaration()!.Type = t;
                    FinishReparsedNode(typeAlias, tag);
                    _jsdocInfos.Add(new JSDocInfo(typeAlias, [jsDoc]));
                    typeAlias.Flags |= NodeFlags.HasJSDoc;
                    var result = WrapInJSDocNamespace(fullName, typeAlias, false /*nested*/);
                    _reparseList.Add(result);
                    break;
                }

            case Kind.JSDocCallbackTag:
                {
                    var typeExpression = tag.TypeExpression();
                    if (typeExpression is null)
                    {
                        break;
                    }

                    var fullName = tag.Name();
                    var isNamespace = fullName is not null && IsModuleDeclaration(fullName);
                    ModifierList? modifiers = null;
                    if (isNamespace)
                    {
                        modifiers = CreateExportModifier(tag);
                    }

                    var functionType = ReparseJSDocSignature(typeExpression, tag, jsDoc, tag, null);
                    var typeAlias = _factory.NewJSTypeAliasDeclaration(
                        modifiers,
                        AddDeepCloneReparse(GetInnermostNameOfJSDocNamespace(fullName)),
                        null,
                        functionType);
                    typeAlias.AsTypeAliasDeclaration()!.TypeParameters = GatherTypeParameters(jsDoc, true /*typedefOrCallback*/);
                    FinishReparsedNode(typeAlias, tag);
                    _jsdocInfos.Add(new JSDocInfo(typeAlias, [jsDoc]));
                    typeAlias.Flags |= NodeFlags.HasJSDoc;
                    var result = WrapInJSDocNamespace(fullName, typeAlias, false /*nested*/);
                    _reparseList.Add(result);
                    break;
                }

            case Kind.JSDocImportTag:
                {
                    var importTag = tag.AsJSDocImportTag()!;
                    if (importTag.ImportClause is null)
                    {
                        break;
                    }

                    var importClause = AddDeepCloneReparse(importTag.ImportClause);
                    importClause!.AsImportClause()!.PhaseModifier = Kind.TypeKeyword;
                    var importDeclaration = _factory.NewJSImportDeclaration(
                        _factory.DeepCloneReparseModifiers(importTag.Modifiers()),
                        importClause,
                        AddDeepCloneReparse(importTag.ModuleSpecifier),
                        AddDeepCloneReparse(importTag.Attributes));
                    FinishReparsedNode(importDeclaration, tag);
                    _reparseList.Add(importDeclaration);
                    break;
                }

            case Kind.JSDocOverloadTag:
                {
                    // Create overload signatures only for function, method, and constructor declarations outside object literals
                    if ((IsFunctionDeclaration(parent) || IsMethodDeclaration(parent) || IsConstructorDeclaration(parent)) &&
                        (_parsingContexts & (ParsingContexts)(1 << (int)ParsingContext.ObjectLiteralMembers)) == 0)
                    {
                        _reparseList.Add(ReparseJSDocSignature(
                            tag.AsJSDocOverloadTag()!.TypeExpression!,
                            parent,
                            jsDoc,
                            tag,
                            parent.Modifiers()));
                    }

                    break;
                }
        }
    }

    // ported from: internal/parser/reparser.go:158-244
    private Node ReparseJSDocSignature(Node jsSignature, Node fun, Node jsDoc, Node tag, ModifierList? modifiers)
    {
        Node signature;
        var clonedModifiers = _factory.DeepCloneReparseModifiers(modifiers);
        switch (fun.Kind)
        {
            case Kind.FunctionDeclaration:
                signature = _factory.NewFunctionDeclaration(clonedModifiers, null, _factory.DeepCloneReparse(CheckNonIdentifierName(fun.Name())), null, null, null, null, null);
                break;
            case Kind.MethodDeclaration:
                signature = _factory.NewMethodDeclaration(clonedModifiers, null, _factory.DeepCloneReparse(CheckNonIdentifierName(fun.Name())), null, null, null, null, null, null);
                break;
            case Kind.Constructor:
                signature = _factory.NewConstructorDeclaration(clonedModifiers, null, null, null, null, null);
                break;
            case Kind.JSDocCallbackTag:
                signature = _factory.NewFunctionTypeNode(null, null, _factory.NewKeywordTypeNode(Kind.AnyKeyword));
                break;
            default:
                throw new DebugFailureException("Unexpected kind " + fun.Kind.String());
        }

        if (tag.Kind != Kind.JSDocCallbackTag)
        {
            signature.FunctionLikeData()!.TypeParameters = GatherTypeParameters(jsDoc, false /*typedefOrCallback*/);
        }

        var parameters = new List<Node>();
        var jsParameters = jsSignature.Parameters();
        if (jsParameters is not null)
        {
            for (var pi = 0; pi < jsParameters.Length; pi++)
            {
                var param = jsParameters[pi];
                // Go 的 switch 只处理下面三类 kind，其余 kind 下 parameter 保持 nil 并在 finishReparsedNode 处
                // 解引用（Go panic → C# NRE，等价）。
                Node? parameter = null;
                if (param.Kind == Kind.JSDocThisTag)
                {
                    var thisTag = param.AsJSDocThisTag()!;
                    var thisIdent = _factory.NewIdentifier("this");
                    thisIdent.Loc = thisTag.Loc;
                    thisIdent.Flags = _contextFlags | NodeFlags.Reparsed;
                    parameter = _factory.NewParameterDeclaration(null, null, thisIdent, null, null, null);
                    if (thisTag.TypeExpression is not null)
                    {
                        parameter.AsParameterDeclaration()!.Type = AddDeepCloneReparse(thisTag.TypeExpression.Type());
                    }
                }
                else if (param.Kind == Kind.JSDocParameterTag || param.Kind == Kind.JSDocPropertyTag)
                {
                    var jsparam = param.AsJSDocParameterOrPropertyTag()!;
                    // Skip sub-property parameters (e.g., @param x.y) - these have QualifiedNames
                    // and describe properties of a parent parameter, not standalone parameters.
                    if (IsQualifiedName(jsparam.Name()!))
                    {
                        continue;
                    }

                    Node? dotDotDotToken = null;
                    Node? paramType = null;

                    if (jsparam.TypeExpression is not null)
                    {
                        if (jsparam.TypeExpression.Type()!.Kind == Kind.JSDocVariadicType)
                        {
                            dotDotDotToken = _factory.NewToken(Kind.DotDotDotToken);
                            dotDotDotToken.Loc = jsparam.Loc;
                            dotDotDotToken.Flags = _contextFlags | NodeFlags.Reparsed;

                            var variadicType = jsparam.TypeExpression.Type()!.AsJSDocVariadicType()!;
                            paramType = ReparseJSDocTypeLiteral(variadicType.Type);
                        }
                        else
                        {
                            paramType = ReparseJSDocTypeLiteral(jsparam.TypeExpression.Type());
                        }
                    }

                    var name = jsparam.Name();
                    if (TypeScript.Ast.Ast.IsIdentifier(name!) && !IsValidIdentifier(name!.AsIdentifier()!.Text))
                    {
                        // drop invalid chars for _, if empty, write _0, etc., so we have a valid param name to emit later
                        var result = new StringBuilder();
                        var nameText = name.AsIdentifier()!.Text;
                        // Go: `for i, ch := range text` 按**码点**迭代（i 只用于判断"第一个码点"，UTF-16 下标与
                        // 字节下标在"是否为 0"上等价）→ C# 必须自己解码码点（§3.1）。
                        var i = 0;
                        while (i < nameText.Length)
                        {
                            var ch = DecodeRuneAt(nameText, i, out var size);
                            if (i == 0)
                            {
                                result.Append(IsIdentifierStart(ch) ? CodePointToString(ch) : "_");
                            }
                            else if (!IsIdentifierPart(ch))
                            {
                                result.Append('_');
                            }
                            else
                            {
                                result.Append(CodePointToString(ch));
                            }

                            i += size;
                        }

                        if (result.Length == 0)
                        {
                            result.Append('_');
                            result.Append(pi.ToString(CultureInfo.InvariantCulture));
                        }

                        name = AddTransformedReparse(_factory.NewIdentifier(result.ToString()), name);
                    }
                    else
                    {
                        name = AddDeepCloneReparse(name);
                    }

                    parameter = _factory.NewParameterDeclaration(null, dotDotDotToken, name, MakeQuestionIfOptional(jsparam), paramType, null);
                }
                FinishReparsedNode(parameter!, param);
                parameters.Add(parameter!);
                ReparseJSDocComment(parameter!, param);
            }
        }

        signature.FunctionLikeData()!.Parameters = NewNodeList(jsSignature.AsJSDocSignature()!.Parameters!.Loc, parameters.ToArray());

        if (jsSignature.Type() is not null && jsSignature.Type()!.TypeExpression() is not null)
        {
            signature.FunctionLikeData()!.Type = AddDeepCloneReparse(jsSignature.Type()!.TypeExpression()!.Type());
        }

        var loc = jsSignature;
        if (tag.Kind == Kind.JSDocOverloadTag)
        {
            loc = tag.TagName()!;
        }

        FinishReparsedNode(signature, loc);
        return signature;
    }

    // ported from: internal/parser/reparser.go:246-287
    private Node? ReparseJSDocTypeLiteral(Node? t)
    {
        if (t is null)
        {
            return null;
        }

        if (t.Kind == Kind.JSDocTypeLiteral)
        {
            var jstypeliteral = t.AsJSDocTypeLiteral()!;
            var isArrayType = jstypeliteral.IsArrayType;
            var properties = new List<Node>();
            if (jstypeliteral.JSDocPropertyTags is not null)
            {
                foreach (var prop in jstypeliteral.JSDocPropertyTags)
                {
                    if (prop.Kind != Kind.JSDocPropertyTag && prop.Kind != Kind.JSDocParameterTag)
                    {
                        continue;
                    }

                    var jsprop = prop.AsJSDocParameterOrPropertyTag()!;
                    var name = prop.Name()!;
                    if (name.Kind == Kind.QualifiedName)
                    {
                        name = name.AsQualifiedName()!.Right!;
                    }

                    if (TypeScript.Ast.Ast.IsIdentifier(name) && !IsValidIdentifier(name.AsIdentifier()!.Text))
                    {
                        name = AddTransformedReparse(_factory.NewStringLiteral(name.AsIdentifier()!.Text, TokenFlags.None), name);
                    }
                    else
                    {
                        name = AddDeepCloneReparse(name)!;
                    }

                    var property = _factory.NewPropertySignatureDeclaration(null, name, MakeQuestionIfOptional(jsprop), null, null);
                    if (jsprop.TypeExpression is not null)
                    {
                        property.AsPropertySignatureDeclaration()!.Type = ReparseJSDocTypeLiteral(jsprop.TypeExpression.Type());
                    }

                    FinishReparsedNode(property, prop);
                    properties.Add(property);
                    ReparseJSDocComment(property, prop);
                }
            }

            t = _factory.NewTypeLiteralNode(NewNodeList(jstypeliteral.Loc, properties.ToArray()));
            if (isArrayType)
            {
                FinishReparsedNode(t, jstypeliteral.AsNode());
                t = _factory.NewArrayTypeNode(t);
            }

            FinishReparsedNode(t, jstypeliteral.AsNode());
            return t;
        }

        return AddDeepCloneReparse(t);
    }

    // ported from: internal/parser/reparser.go:289-298
    private void ReparseJSDocComment(Node node, Node tag)
    {
        var comment = tag.CommentList();
        if (comment is not null)
        {
            // Go: p.factory.NewNodeList(core.Map(comment.Nodes, p.factory.DeepCloneReparse))
            // （DeepCloneReparse 对非空输入必返回非空，故这里用显式类型实参 + `!` 得到 Node[]）
            var newComment = _factory.NewNodeList(Map<Node, Node>(comment.Nodes, n => _factory.DeepCloneReparse(n)!));
            newComment.Loc = comment.Loc;
            var propJSDoc = _factory.NewJSDoc(newComment, null);
            FinishReparsedNode(propJSDoc, tag);
            propJSDoc.Parent = node;
            _jsdocInfos.Add(new JSDocInfo(node, [propJSDoc]));
            node.Flags |= NodeFlags.HasJSDoc;
        }
    }

    // ported from: internal/parser/reparser.go:300-346
    private NodeList? GatherTypeParameters(Node j, bool typedefOrCallback)
    {
        List<Node>? typeParameters = null;
        var pos = -1;
        var endPos = -1;

        var firstTemplate = true;
        var jTags = j.AsJSDoc()!.Tags?.Nodes;
        if (jTags is not null)
        {
            foreach (var tag in jTags)
            {
                // When a JSDoc comment contains an `@typedef` or `@callback` tag, `@template` type parameter
                // declarations apply to the type being defined.
                if (!typedefOrCallback && (IsJSDocTypedefTag(tag) || IsJSDocCallbackTag(tag)))
                {
                    return null;
                }

                if (!IsJSDocTemplateTag(tag))
                {
                    continue;
                }

                if (firstTemplate)
                {
                    pos = tag.Pos();
                    firstTemplate = false;
                }

                endPos = tag.End();
                var constraint = tag.AsJSDocTemplateTag()!.Constraint;
                var firstTypeParameter = true;
                var typeParameterNodes = tag.TypeParameters();
                if (typeParameterNodes is null)
                {
                    continue;
                }

                foreach (var tp in typeParameterNodes)
                {
                    Node? reparse;
                    if (constraint is not null && firstTypeParameter)
                    {
                        reparse = _factory.NewTypeParameterDeclaration(
                            _factory.DeepCloneReparseModifiers(tp.Modifiers()),
                            AddDeepCloneReparse(CheckNonIdentifierName(tp.Name())),
                            AddDeepCloneReparse(constraint.Type()),
                            null, // expression
                            AddDeepCloneReparse(tp.AsTypeParameterDeclaration()!.DefaultType));
                        FinishReparsedNode(reparse, tp);
                    }
                    else
                    {
                        reparse = AddDeepCloneReparse(tp);
                    }

                    typeParameters ??= [];
                    typeParameters.Add(reparse!);
                    firstTypeParameter = false;
                }
            }
        }

        if (typeParameters is null || typeParameters.Count == 0)
        {
            return null;
        }

        return NewNodeList(NewTextRange(pos, endPos), typeParameters.ToArray());
    }

    // ported from: internal/parser/reparser.go:348-580
    private void ReparseHosted(Node tag, Node parent, Node jsDoc)
    {
        switch (tag.Kind)
        {
            case Kind.JSDocTypeTag:
                {
                    switch (parent.Kind)
                    {
                        case Kind.VariableStatement:
                            {
                                var declarationList = parent.AsVariableStatement()!.DeclarationList;
                                if (declarationList is not null)
                                {
                                    var declarations = declarationList.AsVariableDeclarationList()!.Declarations!.Nodes;
                                    if (declarations is not null)
                                    {
                                        foreach (var declaration in declarations)
                                        {
                                            if (declaration.Type() is null && tag.TypeExpression() is not null)
                                            {
                                                declaration.SetType(AddDeepCloneReparse(tag.TypeExpression()!.Type()));
                                                FinishMutatedNode(declaration);
                                                return;
                                            }
                                        }
                                    }
                                }

                                break;
                            }

                        case Kind.VariableDeclaration:
                        case Kind.ExportAssignment:
                        case Kind.PropertyDeclaration:
                        case Kind.PropertyAssignment:
                        case Kind.ShorthandPropertyAssignment:
                        case Kind.GetAccessor:
                            {
                                if (parent.Type() is null && tag.TypeExpression() is not null)
                                {
                                    parent.SetType(AddDeepCloneReparse(tag.TypeExpression()!.Type()));
                                    FinishMutatedNode(parent);
                                    return;
                                }

                                break;
                            }

                        case Kind.Parameter:
                            {
                                if (parent.Type() is null && tag.TypeExpression() is not null)
                                {
                                    parent.SetType(ReparseJSDocTypeLiteral(tag.TypeExpression()!.Type()));
                                    FinishMutatedNode(parent);
                                    return;
                                }

                                break;
                            }

                        case Kind.ExpressionStatement:
                            {
                                if (parent.Expression()!.Kind == Kind.BinaryExpression)
                                {
                                    var bin = parent.Expression()!.AsBinaryExpression()!;
                                    if (GetAssignmentDeclarationKind(bin.AsNode()) != JSDeclarationKind.None && tag.TypeExpression() is not null)
                                    {
                                        bin.SetType(AddDeepCloneReparse(tag.TypeExpression()!.Type()));
                                        FinishMutatedNode(bin.AsNode());
                                        return;
                                    }
                                }

                                break;
                            }

                        case Kind.ReturnStatement:
                        case Kind.ParenthesizedExpression:
                            {
                                if (parent.Expression() is not null && tag.TypeExpression() is not null)
                                {
                                    parent.SetExpression(MakeNewCast(
                                        AddDeepCloneReparse(tag.TypeExpression()!.Type()),
                                        parent.Expression()!,
                                        true /*isAssertion*/));
                                    FinishMutatedNode(parent);
                                    return;
                                }

                                break;
                            }
                    }

                    var fun = GetFunctionLikeHost(parent);
                    if (fun is not null)
                    {
                        var funParameters = fun.Parameters();
                        var noTypedParams = Every(funParameters, param => param.Type() is null);
                        if (fun.TypeParameterList() is null && fun.Type() is null && noTypedParams && tag.TypeExpression() is not null)
                        {
                            fun.FunctionLikeData()!.FullSignature = AddDeepCloneReparse(tag.TypeExpression()!.Type());
                            FinishMutatedNode(fun);
                        }
                    }

                    break;
                }

            case Kind.JSDocSatisfiesTag:
                {
                    switch (parent.Kind)
                    {
                        case Kind.VariableStatement:
                            {
                                var declarationList = parent.AsVariableStatement()!.DeclarationList;
                                if (declarationList is not null)
                                {
                                    var declarations = declarationList.AsVariableDeclarationList()!.Declarations!.Nodes;
                                    if (declarations is not null)
                                    {
                                        foreach (var declaration in declarations)
                                        {
                                            if (declaration.Initializer() is not null && tag.TypeExpression() is not null)
                                            {
                                                declaration.SetInitializer(MakeNewCast(
                                                    AddDeepCloneReparse(tag.TypeExpression()!.Type()),
                                                    declaration.Initializer()!,
                                                    false /*isAssertion*/));
                                                FinishMutatedNode(declaration);
                                                break;
                                            }
                                        }
                                    }
                                }

                                break;
                            }

                        case Kind.VariableDeclaration:
                        case Kind.PropertyDeclaration:
                        case Kind.PropertyAssignment:
                            {
                                if (parent.Initializer() is not null && tag.TypeExpression() is not null)
                                {
                                    parent.SetInitializer(MakeNewCast(
                                        AddDeepCloneReparse(tag.TypeExpression()!.Type()),
                                        parent.Initializer()!,
                                        false /*isAssertion*/));
                                    FinishMutatedNode(parent);
                                }

                                break;
                            }

                        case Kind.ShorthandPropertyAssignment:
                            {
                                var shorthand = parent.AsShorthandPropertyAssignment()!;
                                if (shorthand.ObjectAssignmentInitializer is not null && tag.AsJSDocSatisfiesTag()!.TypeExpression is not null)
                                {
                                    shorthand.ObjectAssignmentInitializer = MakeNewCast(
                                        AddDeepCloneReparse(tag.AsJSDocSatisfiesTag()!.TypeExpression!.Type()),
                                        shorthand.ObjectAssignmentInitializer,
                                        false /*isAssertion*/);
                                    FinishMutatedNode(parent);
                                }

                                break;
                            }

                        case Kind.ReturnStatement:
                        case Kind.ParenthesizedExpression:
                        case Kind.ExportAssignment:
                            {
                                if (parent.Expression() is not null && tag.TypeExpression() is not null)
                                {
                                    parent.SetExpression(MakeNewCast(
                                        AddDeepCloneReparse(tag.TypeExpression()!.Type()),
                                        parent.Expression()!,
                                        false /*isAssertion*/));
                                    FinishMutatedNode(parent);
                                }

                                break;
                            }

                        case Kind.ExpressionStatement:
                            {
                                if (parent.Expression()!.Kind == Kind.BinaryExpression)
                                {
                                    var bin = parent.Expression()!.AsBinaryExpression()!;
                                    if (GetAssignmentDeclarationKind(bin.AsNode()) != JSDeclarationKind.None && tag.TypeExpression() is not null)
                                    {
                                        bin.Right = MakeNewCast(
                                            AddDeepCloneReparse(tag.TypeExpression()!.Type()),
                                            bin.Right!,
                                            false /*isAssertion*/);
                                        FinishMutatedNode(bin.AsNode());
                                    }
                                }

                                break;
                            }
                    }

                    break;
                }

            case Kind.JSDocTemplateTag:
                {
                    var fun = GetFunctionLikeHost(parent);
                    if (fun is not null)
                    {
                        if (fun.TypeParameters() is null && fun.FunctionLikeData()!.FullSignature is null)
                        {
                            fun.FunctionLikeData()!.TypeParameters = GatherTypeParameters(jsDoc, false /*typedefOrCallback*/);
                            FinishMutatedNode(fun);
                        }
                    }
                    else if (parent.Kind == Kind.ClassDeclaration)
                    {
                        var @class = parent.AsClassDeclaration()!;
                        if (@class.TypeParameters is null)
                        {
                            @class.TypeParameters = GatherTypeParameters(jsDoc, false /*typedefOrCallback*/);
                            FinishMutatedNode(parent);
                        }
                    }
                    else if (parent.Kind == Kind.ClassExpression)
                    {
                        var @class = parent.AsClassExpression()!;
                        if (@class.TypeParameters is null)
                        {
                            @class.TypeParameters = GatherTypeParameters(jsDoc, false /*typedefOrCallback*/);
                            FinishMutatedNode(parent);
                        }
                    }

                    break;
                }

            case Kind.JSDocParameterTag:
                {
                    var fun = GetFunctionLikeHost(parent);
                    if (fun is not null && fun.FunctionLikeData()!.FullSignature is null)
                    {
                        var parameterTag = tag.AsJSDocParameterOrPropertyTag()!;
                        if (TryFindMatchingParameter(fun, parameterTag, jsDoc, out var param))
                        {
                            if (param!.Type is null && parameterTag.TypeExpression is not null)
                            {
                                param.AsParameterDeclaration()!.Type = ReparseJSDocTypeLiteral(parameterTag.TypeExpression.Type());
                            }

                            if (param.QuestionToken is null)
                            {
                                var question = MakeQuestionIfOptional(parameterTag);
                                if (question is not null)
                                {
                                    param.QuestionToken = question;
                                }
                            }

                            FinishMutatedNode(param.AsNode());
                        }
                    }

                    break;
                }

            case Kind.JSDocThisTag:
                {
                    var fun = GetFunctionLikeHost(parent);
                    if (fun is not null)
                    {
                        var parameters = fun.Parameters();
                        if (parameters is null || parameters.Length == 0 ||
                            (parameters[0].Name()!.Kind != Kind.ThisKeyword && !IsThisIdentifier(parameters[0].Name()!)))
                        {
                            var thisParam = _factory.NewParameterDeclaration(
                                null, /* decorators */
                                null, /* modifiers */
                                _factory.NewIdentifier("this"),
                                null, /* questionToken */
                                null, /* type */
                                null /* initializer */);
                            if (tag.AsJSDocThisTag()!.TypeExpression is not null)
                            {
                                thisParam.AsParameterDeclaration()!.Type = AddDeepCloneReparse(tag.AsJSDocThisTag()!.TypeExpression!.Type());
                            }

                            FinishReparsedNode(thisParam, tag.TagName()!);

                            // Go: newParams := p.nodeSliceArena.NewSlice(len(params) + 1)；params 为 nil 时 len 为 0
                            var newParams = _nodeSliceArena.NewSlice((parameters?.Length ?? 0) + 1);
                            newParams[0] = thisParam;
                            // Go: for i, param := range params { newParams[i+1] = param }
                            parameters?.CopyTo(newParams, 1);

                            fun.FunctionLikeData()!.Parameters = NewNodeList(fun.ParameterList()!.Loc, newParams);
                            FinishMutatedNode(fun);
                        }
                    }

                    break;
                }

            case Kind.JSDocReturnTag:
                {
                    var fun = GetFunctionLikeHost(parent);
                    if (fun is not null && fun.FunctionLikeData()!.FullSignature is null)
                    {
                        if (fun.Type() is null && tag.TypeExpression() is not null)
                        {
                            fun.FunctionLikeData()!.Type = AddDeepCloneReparse(tag.TypeExpression()!.Type());
                            FinishMutatedNode(fun);
                        }
                    }

                    break;
                }

            case Kind.JSDocReadonlyTag:
            case Kind.JSDocPrivateTag:
            case Kind.JSDocPublicTag:
            case Kind.JSDocProtectedTag:
            case Kind.JSDocOverrideTag:
                {
                    if (parent.Kind == Kind.ExpressionStatement)
                    {
                        parent = parent.Expression()!;
                    }

                    // Go 用 `fallthrough` 让 Method/Get/Set 三个 kind 共享 PropertyDeclaration/Constructor/
                    // BinaryExpression 的处理；C# 的 switch 没有 fallthrough，故合并成一组、把"object literal
                    // 里不算类成员"的检查写成前置条件（语义与 Go 完全一致）。
                    switch (parent.Kind)
                    {
                        case Kind.MethodDeclaration:
                        case Kind.GetAccessor:
                        case Kind.SetAccessor:
                        case Kind.PropertyDeclaration:
                        case Kind.Constructor:
                        case Kind.BinaryExpression:
                            if ((parent.Kind is Kind.MethodDeclaration or Kind.GetAccessor or Kind.SetAccessor) &&
                                (_parsingContexts & (ParsingContexts)(1 << (int)ParsingContext.ObjectLiteralMembers)) != 0)
                            {
                                // In object literals these aren't class-like members, so JSDoc modifiers like @override
                                // or @readonly aren't real modifiers there; reparsing them produces spurious grammar errors (#4437).
                                return;
                            }

                            var keyword = Kind.Unknown;
                            switch (tag.Kind)
                            {
                                case Kind.JSDocReadonlyTag:
                                    keyword = Kind.ReadonlyKeyword;
                                    break;
                                case Kind.JSDocPrivateTag:
                                    keyword = Kind.PrivateKeyword;
                                    break;
                                case Kind.JSDocPublicTag:
                                    keyword = Kind.PublicKeyword;
                                    break;
                                case Kind.JSDocProtectedTag:
                                    keyword = Kind.ProtectedKeyword;
                                    break;
                                case Kind.JSDocOverrideTag:
                                    keyword = Kind.OverrideKeyword;
                                    break;
                            }

                            var modifier = _factory.NewModifier(keyword);
                            modifier.Loc = tag.Loc;
                            modifier.Flags = _contextFlags | NodeFlags.Reparsed;
                            Node[] nodes;
                            TextRange loc;
                            var parentModifiers = parent.Modifiers();
                            if (parentModifiers is null)
                            {
                                nodes = _nodeSliceArena.NewSlice(1);
                                nodes[0] = modifier;
                                loc = tag.Loc;
                            }
                            else
                            {
                                nodes = [.. parent.ModifierNodes() ?? [], modifier];
                                loc = parentModifiers.Loc;
                            }

                            parent.SetModifiers(NewModifierList(loc, nodes));
                            FinishMutatedNode(parent);
                            break;
                    }

                    break;
                }

            case Kind.JSDocImplementsTag:
                {
                    var @class = GetClassLikeData(parent);
                    if (@class is not null)
                    {
                        var implementsTag = tag.AsJSDocImplementsTag()!;
                        var heritageClauses = @class.HeritageClauses;
                        if (heritageClauses?.Nodes is not null)
                        {
                            Node? implementsClause = Find(heritageClauses.Nodes, node => node.AsHeritageClause()!.Token == Kind.ImplementsKeyword);
                            if (implementsClause is not null)
                            {
                                var clause = implementsClause.AsHeritageClause()!;
                                clause.Types!.Nodes = [.. clause.Types!.Nodes ?? [], AddDeepCloneReparse(implementsTag.ClassName)!];
                                FinishMutatedNode(implementsClause);
                                return;
                            }
                        }

                        var typesList = NewNodeList(implementsTag.ClassName!.Loc, _nodeSliceArena.NewSlice1(AddDeepCloneReparse(implementsTag.ClassName)!));
                        var heritageClause = _factory.NewHeritageClause(Kind.ImplementsKeyword, typesList);
                        FinishReparsedNode(heritageClause, implementsTag.ClassName!);

                        if (@class.HeritageClauses is null)
                        {
                            var newHeritageClauses = NewNodeList(implementsTag.ClassName!.Loc, _nodeSliceArena.NewSlice1(heritageClause));
                            @class.HeritageClauses = newHeritageClauses;
                        }
                        else
                        {
                            @class.HeritageClauses.Nodes = [.. @class.HeritageClauses.Nodes ?? [], heritageClause];
                        }

                        FinishMutatedNode(parent);
                    }

                    break;
                }

            case Kind.JSDocAugmentsTag:
                {
                    var @class = GetClassLikeData(parent);
                    if (@class is not null && @class.HeritageClauses is not null)
                    {
                        Node? extendsClause = Find(@class.HeritageClauses.Nodes, node => node.AsHeritageClause()!.Token == Kind.ExtendsKeyword);
                        var extendsTypes = extendsClause?.AsHeritageClause()!.Types!.Nodes;
                        if (extendsTypes is { Length: 1 })
                        {
                            var target = extendsTypes[0].AsExpressionWithTypeArguments()!;
                            var source = tag.ClassName()!.AsExpressionWithTypeArguments()!;
                            if (HasSamePropertyAccessName(target.Expression, source.Expression))
                            {
                                if (target.TypeArguments is null && source.TypeArguments is not null)
                                {
                                    var sourceArguments = source.TypeArguments.Nodes;
                                    var newArguments = _nodeSliceArena.NewSlice(sourceArguments!.Length);
                                    for (var i = 0; i < sourceArguments.Length; i++)
                                    {
                                        newArguments[i] = AddDeepCloneReparse(sourceArguments[i])!;
                                    }

                                    target.TypeArguments = NewNodeList(source.TypeArguments.Loc, newArguments);
                                    FinishMutatedNode(target.AsNode());
                                }
                            }
                        }
                    }

                    break;
                }
        }
    }

    // ported from: internal/parser/reparser.go:582-590
    private Node? MakeQuestionIfOptional(JSDocParameterOrPropertyTag parameter)
    {
        Node? questionToken = null;
        if (parameter.IsBracketed ||
            parameter.TypeExpression is not null && parameter.TypeExpression.Type()!.Kind == Kind.JSDocOptionalType)
        {
            questionToken = _factory.NewToken(Kind.QuestionToken);
            questionToken.Loc = parameter.Loc;
            questionToken.Flags = _contextFlags | NodeFlags.Reparsed;
        }

        return questionToken;
    }

    // ported from: internal/parser/reparser.go:592-613
    private static bool TryFindMatchingParameter(
        Node fun,
        JSDocParameterOrPropertyTag parameterTag,
        Node jsDoc,
        out ParameterDeclaration? result)
    {
        var tagIndex = -1;
        var paramCount = -1;
        var tags = jsDoc.AsJSDoc()!.Tags?.Nodes;
        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                if (tag.Kind == Kind.JSDocParameterTag)
                {
                    paramCount++;
                    if (ReferenceEquals(tag.AsJSDocParameterOrPropertyTag(), parameterTag))
                    {
                        tagIndex = paramCount;
                        break;
                    }
                }
            }
        }

        var parameters = fun.Parameters();
        if (parameters is not null)
        {
            for (var parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                if (parameter.Name()!.Kind == Kind.Identifier)
                {
                    if (parameterTag.Name()!.Kind == Kind.Identifier &&
                        (parameter.Name()!.Text() == parameterTag.Name()!.Text() ||
                         (parameterIndex == tagIndex && parameterTag.Name()!.Text().Length == 0)))
                    {
                        result = parameter.AsParameterDeclaration();
                        return true;
                    }
                }
                else if (parameterIndex == tagIndex)
                {
                    result = parameter.AsParameterDeclaration();
                    return true;
                }
            }
        }

        result = null;
        return false;
    }

    // ported from: internal/parser/reparser.go:615-620
    private static Node? SkipSatisfiesExpressions(Node? node)
    {
        while (node is not null && node.Kind == Kind.SatisfiesExpression)
        {
            node = node.Expression();
        }

        return node;
    }

    // ported from: internal/parser/reparser.go:622-641
    private static Node? GetFunctionLikeHost(Node host)
    {
        Node? fun = host;
        switch (host.Kind)
        {
            case Kind.VariableStatement:
                {
                    var nodes = host.AsVariableStatement()!.DeclarationList!.AsVariableDeclarationList()!.Declarations!.Nodes;
                    if (nodes is { Length: > 0 })
                    {
                        fun = nodes[0].Initializer();
                    }

                    break;
                }

            case Kind.PropertyAssignment:
            case Kind.PropertyDeclaration:
                fun = host.Initializer();
                break;
            case Kind.ExportAssignment:
            case Kind.ReturnStatement:
                fun = host.Expression();
                break;
            case Kind.ExpressionStatement:
                fun = GetRightMostAssignedExpression(host.Expression());
                break;
        }

        fun = SkipSatisfiesExpressions(fun);
        if (IsFunctionLike(fun))
        {
            return fun;
        }

        return null;
    }

    // ported from: internal/parser/reparser.go:643-651
    private Node MakeNewCast(Node? t, Node e, bool isAssertion)
    {
        Node assert;
        if (isAssertion)
        {
            assert = _factory.NewAsExpression(e, t);
        }
        else
        {
            assert = _factory.NewSatisfiesExpression(e, t);
        }

        FinishNodeWithEnd(assert, e.Pos(), e.End());
        return assert;
    }

    // ported from: internal/parser/reparser.go:653-662
    private static IClassLikeBase? GetClassLikeData(Node parent)
    {
        IClassLikeBase? @class = null;
        switch (parent.Kind)
        {
            case Kind.ClassDeclaration:
                @class = parent.AsClassDeclaration()!.ClassLikeData();
                break;
            case Kind.ClassExpression:
                @class = parent.AsClassExpression()!.ClassLikeData();
                break;
        }

        return @class;
    }

    // ported from: internal/parser/reparser.go:664-669
    private ModifierList CreateExportModifier(Node locationNode)
    {
        var exportModifier = _factory.NewModifier(Kind.ExportKeyword);
        exportModifier.Loc = locationNode.Loc;
        exportModifier.Flags = _contextFlags | NodeFlags.Reparsed;
        var nodes = _nodeSliceArena.NewSlice1(exportModifier);
        return NewModifierList(locationNode.Loc, nodes);
    }

    /// <summary>
    /// getInnermostNameOfJSDocNamespace returns the innermost identifier from a
    /// JSDoc namespace chain (ModuleDeclaration). For a simple identifier, it returns
    /// the identifier itself. For "A.B.C", it returns the identifier "C".
    /// </summary>
    // ported from: internal/parser/reparser.go:671-683
    private Node? GetInnermostNameOfJSDocNamespace(Node? fullName)
    {
        if (fullName is null)
        {
            return null;
        }

        while (fullName.Kind == Kind.ModuleDeclaration)
        {
            var body = fullName.AsModuleDeclaration()!.Body;
            if (body is null)
            {
                return fullName.Name();
            }

            fullName = body;
        }

        return fullName;
    }

    /// <summary>
    /// wrapInJSDocNamespace wraps a statement (typically a type alias) in namespace
    /// declarations corresponding to a JSDoc dotted name. For example, given name
    /// "A.B.C" and a type alias for C, this produces:
    /// <code>namespace A { namespace B { type C = ... } }</code>
    /// If the name is a simple identifier (not a ModuleDeclaration), it returns the
    /// statement as-is.
    /// </summary>
    // ported from: internal/parser/reparser.go:685-703
    private Node WrapInJSDocNamespace(Node? fullName, Node statement, bool nested)
    {
        if (fullName is null || !IsModuleDeclaration(fullName))
        {
            return statement;
        }

        // Recursively wrap from outermost to innermost. Inner namespaces always get an export modifier
        // so members are accessible via dotted access from outside. The outermost namespace is treated as
        // exported only in module files via IsImplicitlyExportedJSDocDeclaration (in the binder), so it
        // does not get an explicit export modifier here.
        var wrapped = WrapInJSDocNamespace(fullName.Body(), statement, true /*nested*/);
        var block = _factory.NewModuleBlock(NewNodeList(fullName.Loc, _nodeSliceArena.NewSlice1(wrapped)));
        FinishReparsedNode(block, fullName);
        ModifierList? modifiers = null;
        if (nested)
        {
            modifiers = CreateExportModifier(fullName);
        }

        var result = _factory.NewModuleDeclaration(modifiers, Kind.NamespaceKeyword, AddDeepCloneReparse(fullName.Name()), null, block);
        FinishReparsedNode(result, fullName);
        _reparsedClones.Add(result);
        return result;
    }
}
