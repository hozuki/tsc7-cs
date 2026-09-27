// ported from: internal/transformers/jsxtransforms/jsx.go:1-549
//   （JSXTransformer 字段/构造、隐式运行时 import、visit 分派、visitSourceFile、JSX 元素/片段/子节点、
//     getTagName、JSX 属性 → props 对象/表达式）
//
// 与 Go 的差异（C# 语言特性，逐条对应 docs/architecture.md §2/§3）：
// 1) Go 的方法表达式（`tagTransform := (*JSXTransformer).visitJsxOpeningLikeElementJSX` 再按条件改写）
//    → C# 的三元调用（`cond ? M1(...) : M2(...)`）：语义与 Go 完全相同（location 先算好），且不产生委托分配。
// 2) Go 的 `slices.Insert` → `InsertStatementAfterPrologue` 里的显式数组拷贝；`slices.Clone` + `slices.Delete`
//    → 只分配 len-1 的新数组（调用点只顺序遍历，语义相同）。
// 3) Go 的 `defer tx.setInChild(prev)` → `try/finally`。
// 4) `map[string]*ast.Node` → `Dictionary<string, Node>`；`collections.OrderedMap` 同名同形。
// 5) Go 的 package 级函数（`NewJSXTransformer`/`hasKeyAfterPropsSpread`/`insertStatementAfterPrologue`/
//    `sortImportSpecifiers`/`getSortedSpecifiers`/`hasProto`）按 §2 落进与 package 同名的
//    `public static partial class JsxTransforms`。
// 6) Go 的 `panic(...)` → 返回类型可空处用 `Debug.Fail(...)` + `return null`，非空处用
//    `throw new DebugFailureException(...)`（`Debug.Fail` 的内部实现就是 throw 同一个异常，见 Debug.cs 文件头）。
// 7) Go 的 `tx.Visitor().Visit(x)`（NodeVisitor 的 Visit 字段，回调自身处理 nil）→ C# 的
//    `Visitor().VisitNode(x)`：`VisitNode` 对 null 直接返回 null（与 Go 的回调 `if node == nil { return nil }` 等价），
//    并且是本仓库既有约定（既有调用点全部走 `VisitNode`）。差异只可能出现在"回调返回 SyntaxList"时
//    （VisitNode 会做单元素提升），JSX 变换里回调只返回 call/string/spread/object 等节点，不会出现。
// 8) Go 的 `map[string]rune`（实体表）留在 `Jsx.Text.cs` 的 `JsxTransforms` 里。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

namespace TypeScript.Transformers.JsxTransforms;

/// <summary>Go: <c>type JSXTransformer struct</c>（jsx.go:19-31）。</summary>
public sealed partial class JSXTransformer : Transformer
{
    private readonly CompilerOptions _compilerOptions;
    private readonly EmitResolver _emitResolver;

    private string _importSpecifier = "";
    private Node? _filenameDeclaration;
    private readonly OrderedMap<string, Dictionary<string, Node>> _utilizedImplicitRuntimeImports = new();

    /// <summary>Go 的未导出字段 <c>inJsxChild</c>。</summary>
    private bool _inJsxChild;

    private SourceFile? _currentSourceFile;

    /// <summary>
    /// Go 用结构体字面量 + `NewTransformer` 两步构造；C# 的字段是 readonly，所以用 internal 构造函数
    /// （只有 <see cref="JsxTransforms.NewJSXTransformer"/> 会调用）。
    /// </summary>
    internal JSXTransformer(CompilerOptions compilerOptions, EmitResolver emitResolver)
    {
        _compilerOptions = compilerOptions;
        _emitResolver = emitResolver;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) getCurrentFileNameExpression() *ast.Node</c>（jsx.go:42-56）。</summary>
    private Node GetCurrentFileNameExpression()
    {
        if (_filenameDeclaration is not null)
        {
            return _filenameDeclaration.AsVariableDeclaration()!.Name()!;
        }

        var d = Factory().NewVariableDeclaration(
            Factory().NewUniqueNameEx("_jsxFileName", new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
            }),
            null,
            null,
            Factory().NewStringLiteral(_currentSourceFile!.FileName(), TokenFlags.None));
        _filenameDeclaration = d;
        return d.AsVariableDeclaration()!.Name()!;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) getJsxFactoryCalleePrimitive(isStaticChildren bool) string</c>（jsx.go:58-66）。</summary>
    private string GetJsxFactoryCalleePrimitive(bool isStaticChildren)
    {
        if (_compilerOptions.Jsx == JsxEmit.ReactJSXDev)
        {
            return "jsxDEV";
        }

        if (isStaticChildren)
        {
            return "jsxs";
        }

        return "jsx";
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) getJsxFactoryCallee(isStaticChildren bool) *ast.Node</c>（jsx.go:68-71）。</summary>
    private Node GetJsxFactoryCallee(bool isStaticChildren)
    {
        var t = GetJsxFactoryCalleePrimitive(isStaticChildren);
        return GetImplicitImportForName(t);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) getImplicitJsxFragmentReference() *ast.Node</c>（jsx.go:73-75）。</summary>
    private Node GetImplicitJsxFragmentReference()
    {
        return GetImplicitImportForName("Fragment");
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) getImplicitImportForName(name string) *ast.Node</c>（jsx.go:77-100）。</summary>
    private Node GetImplicitImportForName(string name)
    {
        var importSource = _importSpecifier;
        if (name != "createElement")
        {
            importSource = GetJSXRuntimeImport(importSource, _compilerOptions);
        }

        var (existing, ok) = _utilizedImplicitRuntimeImports.Get(importSource);
        if (ok)
        {
            if (existing!.TryGetValue(name, out var elem))
            {
                return elem.AsImportSpecifier()!.Name()!;
            }
        }
        else
        {
            existing = [];
            _utilizedImplicitRuntimeImports.Set(importSource, existing);
        }

        var generatedName = Factory().NewUniqueNameEx("_" + name, new AutoGenerateOptions
        {
            Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel | GeneratedIdentifierFlags.AllowNameSubstitution,
        });
        var specifier = Factory().NewImportSpecifier(false, Factory().NewIdentifier(name), generatedName);
        _emitResolver.SetReferencedImportDeclaration(generatedName, specifier);
        existing![name] = specifier;
        return specifier.Name()!;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) setInChild(v bool)</c>（jsx.go:102-104）。</summary>
    private void SetInChild(bool v)
    {
        _inJsxChild = v;
    }

    /// <summary>
    /// Go: <c>func (tx *JSXTransformer) visit(node *ast.Node) *ast.Node</c>（jsx.go:106-139）。
    /// Go 里 <c>NewJSXTransformer</c> 与它是同一个 package；C# 里 <c>NewJSXTransformer</c> 在
    /// <see cref="JsxTransforms"/> 类里，取方法组需要 internal。
    /// </summary>
    internal Node? Visit(Node node)
    {
        if ((node.SubtreeFacts & SubtreeFacts.ContainsJsx) == 0)
        {
            return node;
        }

        switch (node.Kind)
        {
            case Kind.SourceFile:
                SetInChild(false);
                return VisitSourceFile(node.AsSourceFile()!);
            case Kind.JsxElement:
                return VisitJsxElement(node.AsJsxElement()!);
            case Kind.JsxSelfClosingElement:
                return VisitJsxSelfClosingElement(node.AsJsxSelfClosingElement()!);
            case Kind.JsxFragment:
                return VisitJsxFragment(node.AsJsxFragment()!);
            case Kind.JsxOpeningElement:
                // Go: panic("JsxOpeningElement should not be visited, handled in visitJsxElement")
                Debug.Fail("JsxOpeningElement should not be visited, handled in visitJsxElement");
                return null;
            case Kind.JsxOpeningFragment:
                // Go: panic("JsxOpeningFragment should not be visited, handled in visitJsxFragment")
                Debug.Fail("JsxOpeningFragment should not be visited, handled in visitJsxFragment");
                return null;
            case Kind.JsxText:
                SetInChild(false);
                return VisitJsxText(node.AsJsxText()!);
            case Kind.JsxExpression:
                SetInChild(false);
                return VisitJsxExpression(node.AsJsxExpression()!);
        }

        SetInChild(false);
        return node.VisitEachChild(Visitor()); // by default, do nothing
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) shouldUseCreateElement(node *ast.Node) bool</c>（jsx.go:157-159）。</summary>
    private bool ShouldUseCreateElement(Node node)
    {
        return _importSpecifier.Length == 0 || JsxTransforms.HasKeyAfterPropsSpread(node);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) isAnyPrologueDirective(node *ast.Node) bool</c>（jsx.go:175-177）。</summary>
    private bool IsAnyPrologueDirective(Node node)
    {
        return IsPrologueDirective(node) || (EmitContext().EmitFlags(node) & EmitFlags.CustomPrologue) != 0;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) insertStatementAfterCustomPrologue(to []*ast.Node, statement *ast.Node) []*ast.Node</c>（jsx.go:179-181）。</summary>
    private Node[] InsertStatementAfterCustomPrologue(Node[] to, Node? statement)
    {
        return JsxTransforms.InsertStatementAfterPrologue(
            to,
            statement,
            // Go: (*JSXTransformer).isAnyPrologueDirective（C# 无方法表达式 → 无捕获静态 lambda，会被缓存）
            static (JSXTransformer tx, Node node) => tx.IsAnyPrologueDirective(node),
            this);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitSourceFile(file *ast.SourceFile) *ast.Node</c>（jsx.go:197-278）。</summary>
    private Node VisitSourceFile(SourceFile file)
    {
        if (file.IsDeclarationFile)
        {
            return file;
        }

        _currentSourceFile = file;
        _importSpecifier = GetJSXImplicitImportBase(_compilerOptions, file);
        _filenameDeclaration = null;
        _utilizedImplicitRuntimeImports.Clear();

        var visited = file.VisitEachChild(Visitor())!;
        EmitContext().AddEmitHelper(visited, EmitContext().ReadEmitHelpers());
        var statements = visited.Statements() ?? [];
        var statementsUpdated = false;
        if (_filenameDeclaration is not null)
        {
            statements = InsertStatementAfterCustomPrologue(statements, Factory().NewVariableStatement(null, Factory().NewVariableDeclarationList(
                Factory().NewNodeList([_filenameDeclaration!]),
                NodeFlags.Const)));
            statementsUpdated = true;
        }

        if (_utilizedImplicitRuntimeImports.Size() > 0)
        {
            if (IsExternalModule(file))
            {
                statementsUpdated = true;
                var newStatements = new List<Node>(_utilizedImplicitRuntimeImports.Size());
                foreach (var (importSource, importSpecifiersMap) in _utilizedImplicitRuntimeImports.Entries())
                {
                    var s = Factory().NewImportDeclaration(
                        null,
                        Factory().NewImportClause(Kind.Unknown, null, Factory().NewNamedImports(Factory().NewNodeList(JsxTransforms.GetSortedSpecifiers(importSpecifiersMap!)))),
                        Factory().NewStringLiteral(importSource, TokenFlags.None),
                        null);
                    SetParentInChildren(s);
                    newStatements.Add(s);
                }

                foreach (var e in newStatements)
                {
                    statements = InsertStatementAfterCustomPrologue(statements, e);
                }
            }
            else if (IsExternalOrCommonJSModule(file))
            {
                statementsUpdated = true;
                var newStatements = new List<Node>(_utilizedImplicitRuntimeImports.Size());
                foreach (var (importSource, importSpecifiersMap) in _utilizedImplicitRuntimeImports.Entries())
                {
                    var sorted = JsxTransforms.GetSortedSpecifiers(importSpecifiersMap!);
                    var asBindingElems = new List<Node>(sorted.Length);
                    foreach (var elem in sorted)
                    {
                        asBindingElems.Add(Factory().NewBindingElement(null, elem.PropertyName(), elem.AsImportSpecifier()!.Name(), null));
                    }

                    var s = Factory().NewVariableStatement(null, Factory().NewVariableDeclarationList(Factory().NewNodeList([Factory().NewVariableDeclaration(
                        Factory().NewBindingPattern(Kind.ObjectBindingPattern, Factory().NewNodeList(asBindingElems.ToArray())),
                        null,
                        null,
                        Factory().NewCallExpression(
                            Factory().NewIdentifier("require"),
                            null,
                            null,
                            Factory().NewNodeList([Factory().NewStringLiteral(importSource, TokenFlags.None)]),
                            NodeFlags.None))]),
                        NodeFlags.Const));
                    SetParentInChildren(s);
                    newStatements.Add(s);
                }

                foreach (var e in newStatements)
                {
                    statements = InsertStatementAfterCustomPrologue(statements, e);
                }
            }

            // else: Do nothing (script file) - consider an error in the checker?
        }

        if (statementsUpdated)
        {
            visited = Factory().UpdateSourceFile(file, Factory().NewNodeList(statements), file.EndOfFileToken);
        }

        _currentSourceFile = null;
        _importSpecifier = "";
        _filenameDeclaration = null;
        _utilizedImplicitRuntimeImports.Clear();

        return visited;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxElement(element *ast.JsxElement) *ast.Node</c>（jsx.go:280-287）。</summary>
    private Node VisitJsxElement(JsxElement element)
    {
        var location = NewTextRange(SkipTrivia(_currentSourceFile!.Text(), element.Pos()), element.End());
        // Go: tagTransform := (*JSXTransformer).visitJsxOpeningLikeElementJSX，必要时改写为 …CreateElement；
        // Go 传入的是 element.OpeningElement（不是 JsxElement 本身；getTagName/Attributes 只认 opening-like 节点）。
        var openingElement = element.OpeningElement!;
        return ShouldUseCreateElement(element)
            ? VisitJsxOpeningLikeElementCreateElement(openingElement, element.Children, location)
            : VisitJsxOpeningLikeElementJSX(openingElement, element.Children, location);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxSelfClosingElement(element *ast.JsxSelfClosingElement) *ast.Node</c>（jsx.go:289-296）。</summary>
    private Node VisitJsxSelfClosingElement(JsxSelfClosingElement element)
    {
        var location = NewTextRange(SkipTrivia(_currentSourceFile!.Text(), element.Pos()), element.End());
        return ShouldUseCreateElement(element)
            ? VisitJsxOpeningLikeElementCreateElement(element, null, location)
            : VisitJsxOpeningLikeElementJSX(element, null, location);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxFragment(fragment *ast.JsxFragment) *ast.Node</c>（jsx.go:298-305）。</summary>
    private Node VisitJsxFragment(JsxFragment fragment)
    {
        var location = NewTextRange(SkipTrivia(_currentSourceFile!.Text(), fragment.Pos()), fragment.End());
        var openingFragment = fragment.OpeningFragment!.AsJsxOpeningFragment()!;
        return _importSpecifier.Length == 0
            ? VisitJsxOpeningFragmentCreateElement(openingFragment, fragment.Children, location)
            : VisitJsxOpeningFragmentJSX(openingFragment, fragment.Children, location);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) convertJsxChildrenToChildrenPropObject(children []*ast.JsxChild) *ast.Node</c>（jsx.go:307-313）。</summary>
    private Node? ConvertJsxChildrenToChildrenPropObject(Node[] children)
    {
        var prop = ConvertJsxChildrenToChildrenPropAssignment(children);
        if (prop is null)
        {
            return null;
        }

        return Factory().NewObjectLiteralExpression(Factory().NewNodeList([prop]), false);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) transformJsxChildToExpression(node *ast.Node) *ast.Node</c>（jsx.go:315-320）。</summary>
    private Node? TransformJsxChildToExpression(Node node)
    {
        var prev = _inJsxChild;
        SetInChild(true);
        try
        {
            return Visitor().VisitNode(node);
        }
        finally
        {
            SetInChild(prev);
        }
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) convertJsxChildrenToChildrenPropAssignment(children []*ast.JsxChild) *ast.Node</c>（jsx.go:322-346）。</summary>
    private Node? ConvertJsxChildrenToChildrenPropAssignment(Node[] children)
    {
        var nonWhitespceChildren = GetSemanticJsxChildren(children);
        if (nonWhitespceChildren.Length == 1 && (nonWhitespceChildren[0].Kind != Kind.JsxExpression || nonWhitespceChildren[0].AsJsxExpression()!.DotDotDotToken is null))
        {
            var result = TransformJsxChildToExpression(nonWhitespceChildren[0]);
            if (result is null)
            {
                return null;
            }

            return Factory().NewPropertyAssignment(null, Factory().NewIdentifier("children"), null, null, result);
        }

        // For multiple children in the children property array, don't set StartOnNewLine
        // on child elements — the array literal is single-line.
        var results = new List<Node>(nonWhitespceChildren.Length);
        foreach (var child in nonWhitespceChildren)
        {
            var res = TransformJsxChildToExpression(child);
            if (res is null)
            {
                continue;
            }

            EmitContext().SetEmitFlags(res, EmitContext().EmitFlags(res) & ~EmitFlags.StartOnNewLine);
            results.Add(res);
        }

        if (results.Count == 0)
        {
            return null;
        }

        return Factory().NewPropertyAssignment(null, Factory().NewIdentifier("children"), null, null, Factory().NewArrayLiteralExpression(Factory().NewNodeList(results.ToArray()), false));
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) getTagName(node *ast.Node) *ast.Node</c>（jsx.go:348-365）。</summary>
    private Node GetTagName(Node node)
    {
        if (node.Kind == Kind.JsxElement)
        {
            return GetTagName(node.AsJsxElement()!.OpeningElement!);
        }

        if (IsJsxOpeningLikeElement(node))
        {
            var tagName = node.TagName()!;
            if (IsIdentifier(tagName) && IsIntrinsicJsxName(tagName.Text()))
            {
                return Factory().NewStringLiteral(tagName.Text(), TokenFlags.None);
            }

            if (IsJsxNamespacedName(tagName))
            {
                return Factory().NewStringLiteral(
                    tagName.AsJsxNamespacedName()!.Namespace!.Text() + ":" + tagName.AsJsxNamespacedName()!.Name()!.Text(), TokenFlags.None);
            }

            return Factory().CreateExpressionFromEntityName(tagName);
        }

        // Go: panic("unhandled node kind passed to getTagName: " + node.Kind.String())
        // （返回类型非空 → 用 throw；Debug.Fail 内部就是 throw DebugFailureException，见 Debug.cs 文件头）
        throw new DebugFailureException("unhandled node kind passed to getTagName: " + node.Kind.String());
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxOpeningLikeElementJSX(element *ast.Node, children *ast.NodeList, location core.TextRange) *ast.Node</c>（jsx.go:367-400）。</summary>
    private Node VisitJsxOpeningLikeElementJSX(Node element, NodeList? children, TextRange location)
    {
        var tagName = GetTagName(element);
        Node? childrenProp = null;
        if (children is not null && children.Nodes is { Length: > 0 })
        {
            childrenProp = ConvertJsxChildrenToChildrenPropAssignment(children.Nodes);
        }

        Node? keyAttr = null;
        var attrs = element.Attributes()!.Properties() ?? [];
        for (var i = 0; i < attrs.Length; i++)
        {
            var p = attrs[i];
            if (p.Kind == Kind.JsxAttribute && p.AsJsxAttribute()!.Name() is { } attrName && IsIdentifier(attrName) && attrName.Text() == "key")
            {
                keyAttr = p;
                // Go: attrs = slices.Clone(attrs); attrs = slices.Delete(attrs, i, i+1)
                var clone = new Node[attrs.Length - 1];
                System.Array.Copy(attrs, 0, clone, 0, i);
                System.Array.Copy(attrs, i + 1, clone, i, attrs.Length - i - 1);
                attrs = clone;
                break;
            }
        }

        Node @object;
        if (attrs.Length > 0)
        {
            @object = TransformJsxAttributesToObjectProps(attrs, childrenProp);
        }
        else
        {
            var objectChildren = new List<Node>();
            if (childrenProp is not null)
            {
                objectChildren.Add(childrenProp);
            }

            // When there are no attributes, React wants {}
            @object = Factory().NewObjectLiteralExpression(Factory().NewNodeList(objectChildren.ToArray()), false);
        }

        return VisitJsxOpeningLikeElementOrFragmentJSX(
            tagName,
            @object,
            keyAttr,
            children,
            location);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) transformJsxAttributesToObjectProps(attrs []*ast.Node, childrenProp *ast.Node) *ast.Node</c>（jsx.go:402-409）。</summary>
    private Node TransformJsxAttributesToObjectProps(Node[] attrs, Node? childrenProp)
    {
        var target = _compilerOptions.GetEmitScriptTarget();
        if (target >= ScriptTarget.ES2018)
        {
            // target has object spreads, can keep as-is
            return Factory().NewObjectLiteralExpression(Factory().NewNodeList(TransformJsxAttributesToProps(attrs, childrenProp)), false);
        }

        return TransformJsxAttributesToExpression(attrs, childrenProp);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) transformJsxAttributesToExpression(attrs []*ast.Node, childrenProp *ast.Node) *ast.Node</c>（jsx.go:411-454）。</summary>
    private Node TransformJsxAttributesToExpression(Node[] attrs, Node? childrenProp)
    {
        var expressions = new List<Node>(2);
        var properties = new List<Node>(attrs.Length);

        foreach (var attr in attrs)
        {
            if (IsJsxSpreadAttribute(attr))
            {
                // as an optimization we try to flatten the first level of spread inline object
                // as if its props would be passed as JSX attributes
                if (IsObjectLiteralExpression(attr.Expression()!) && !JsxTransforms.HasProto(attr.Expression()!.AsObjectLiteralExpression()!))
                {
                    foreach (var prop in attr.Expression()!.Properties() ?? [])
                    {
                        if (IsSpreadAssignment(prop))
                        {
                            (expressions, properties) = CombinePropertiesIntoNewExpression(expressions, properties);
                            expressions.Add(Visitor().VisitNode(prop.Expression())!);
                            continue;
                        }

                        properties.Add(Visitor().VisitNode(prop)!);
                    }

                    continue;
                }

                (expressions, properties) = CombinePropertiesIntoNewExpression(expressions, properties);
                expressions.Add(Visitor().VisitNode(attr.Expression())!);
                continue;
            }

            properties.Add(TransformJsxAttributeToObjectLiteralElement(attr.AsJsxAttribute()!));
        }

        if (childrenProp is not null)
        {
            properties.Add(childrenProp);
        }

        (expressions, _) = CombinePropertiesIntoNewExpression(expressions, properties);

        if (expressions.Count > 0 && !IsObjectLiteralExpression(expressions[0]))
        {
            // We must always emit at least one object literal before a spread attribute
            // as the JSX always factory expects a fresh object, so we need to make a copy here
            // we also avoid mutating an external reference by doing this (first expression is used as assign's target)
            expressions.Insert(0, Factory().NewObjectLiteralExpression(Factory().NewNodeList([]), false));
        }

        if (expressions.Count == 1)
        {
            return expressions[0];
        }

        return Factory().NewAssignHelper(expressions.ToArray(), _compilerOptions.GetEmitScriptTarget());
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) combinePropertiesIntoNewExpression(expressions []*ast.Expression, props []*ast.ObjectLiteralElement) ([]*ast.Expression, []*ast.ObjectLiteralElement)</c>（jsx.go:456-463）。</summary>
    private (List<Node> Expressions, List<Node> Props) CombinePropertiesIntoNewExpression(List<Node> expressions, List<Node> props)
    {
        if (props.Count == 0)
        {
            return (expressions, props);
        }

        var newObj = Factory().NewObjectLiteralExpression(Factory().NewNodeList(props.ToArray()), false);
        expressions.Add(newObj);
        return (expressions, []);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) transformJsxAttributesToProps(attrs []*ast.Node, childrenProp *ast.Node) []*ast.Node</c>（jsx.go:465-479）。</summary>
    private Node[] TransformJsxAttributesToProps(Node[] attrs, Node? childrenProp)
    {
        var props = new List<Node>(attrs.Length);
        foreach (var attr in attrs)
        {
            if (attr.Kind == Kind.JsxSpreadAttribute)
            {
                var res = TransformJsxSpreadAttributesToProps(attr.AsJsxSpreadAttribute()!);
                props.AddRange(res);
            }
            else
            {
                props.Add(TransformJsxAttributeToObjectLiteralElement(attr.AsJsxAttribute()!));
            }
        }

        if (childrenProp is not null)
        {
            props.Add(childrenProp);
        }

        return props.ToArray();
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) transformJsxSpreadAttributesToProps(node *ast.JsxSpreadAttribute) []*ast.Node</c>（jsx.go:490-496）。</summary>
    private Node[] TransformJsxSpreadAttributesToProps(JsxSpreadAttribute node)
    {
        if (IsObjectLiteralExpression(node.Expression!) && !JsxTransforms.HasProto(node.Expression!.AsObjectLiteralExpression()!))
        {
            var (res, _) = Visitor().VisitSlice(node.Expression!.Properties());
            return res ?? [];
        }

        return [Factory().NewSpreadAssignment(Visitor().VisitNode(node.Expression)!)];
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) transformJsxAttributeToObjectLiteralElement(node *ast.JsxAttribute) *ast.Node</c>（jsx.go:498-502）。</summary>
    private Node TransformJsxAttributeToObjectLiteralElement(JsxAttribute node)
    {
        var name = GetAttributeName(node);
        var expression = TransformJsxAttributeInitializer(node.Initializer);
        return Factory().NewPropertyAssignment(null, name, null, null, expression);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) getAttributeName(node *ast.JsxAttribute) *ast.Node</c>（jsx.go:509-522）。</summary>
    /// <remarks>
    /// Emit an attribute name, which is quoted if it needs to be quoted. Because
    /// these emit into an object literal property name, we don't need to be worried
    /// about keywords, just non-identifier characters.
    /// </remarks>
    private Node GetAttributeName(JsxAttribute node)
    {
        var name = node.Name()!;
        if (IsIdentifier(name))
        {
            var text = name.Text();
            if (IsIdentifierText(text, LanguageVariant.Standard))
            {
                return name;
            }

            return Factory().NewStringLiteral(text, TokenFlags.None);
        }

        // must be jsx namespace
        return Factory().NewStringLiteral(
            name.AsJsxNamespacedName()!.Namespace!.Text() + ":" + name.AsJsxNamespacedName()!.Name()!.Text(), TokenFlags.None);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) transformJsxAttributeInitializer(node *ast.Node) *ast.Node</c>（jsx.go:524-548）。</summary>
    private Node TransformJsxAttributeInitializer(Node? node)
    {
        if (node is null)
        {
            return Factory().NewTrueExpression();
        }

        if (node.Kind == Kind.StringLiteral)
        {
            // Always recreate the literal to escape any escape sequences or newlines which may be in the original jsx string and which
            // Need to be escaped to be handled correctly in a normal string
            var res = Factory().NewStringLiteral(JsxTransforms.DecodeEntities(node.Text()), node.AsStringLiteral()!.TokenFlags);
            res.Loc = node.Loc;
            // Preserve the original quote style (single vs double quotes)
            res.AsStringLiteral()!.TokenFlags = node.AsStringLiteral()!.TokenFlags;
            return res;
        }

        if (node.Kind == Kind.JsxExpression)
        {
            if (node.Expression() is null)
            {
                return Factory().NewTrueExpression();
            }

            return Visitor().VisitNode(node.Expression())!;
        }

        if (IsJsxElement(node) || IsJsxSelfClosingElement(node) || IsJsxFragment(node))
        {
            SetInChild(false);
            return Visitor().VisitNode(node)!;
        }

        // Go: panic("Unhandled node kind found in jsx initializer: " + node.Kind.String())
        throw new DebugFailureException("Unhandled node kind found in jsx initializer: " + node.Kind.String());
    }
}

/// <summary>Go 的 <c>package jsxtransforms</c> 包级成员（jsx.go:32-40、141-155、161-173、183-195、481-488）。</summary>
public static partial class JsxTransforms
{
    /// <summary>Go: <c>func NewJSXTransformer(opts *transformers.TransformOptions) *transformers.Transformer</c>（jsx.go:32-40）。</summary>
    public static Transformer NewJSXTransformer(TransformOptions opts)
    {
        var compilerOptions = opts.CompilerOptions;
        var emitContext = opts.Context;
        // Go: tx := &JSXTransformer{compilerOptions: compilerOptions, emitResolver: opts.EmitResolver}
        var tx = new JSXTransformer(compilerOptions!, opts.EmitResolver!);
        return tx.NewTransformer(tx.Visit, emitContext);
    }

    /// <summary>
    /// The react jsx/jsxs transform falls back to <c>createElement</c> when an explicit <c>key</c> argument comes after a spread
    /// （jsx.go:141-155）。
    /// </summary>
    internal static bool HasKeyAfterPropsSpread(Node node)
    {
        var spread = false;
        var opener = node;
        if (node.Kind == Kind.JsxElement)
        {
            opener = node.AsJsxElement()!.OpeningElement!;
        } // otherwise self-closing

        foreach (var elem in opener.Attributes()!.Properties() ?? [])
        {
            if (IsJsxSpreadAttribute(elem) && (!IsObjectLiteralExpression(elem.Expression()!) || Some(elem.Expression()!.Properties(), IsSpreadAssignment)))
            {
                spread = true;
            }
            else if (spread && IsJsxAttribute(elem) && IsIdentifier(elem.Name()!) && elem.Name()!.Text() == "key")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Go: <c>func insertStatementAfterPrologue[T any](to []*ast.Node, statement *ast.Node, isPrologueDirective func(callee T, node *ast.Node) bool, callee T) []*ast.Node</c>（jsx.go:161-173）。</summary>
    internal static Node[] InsertStatementAfterPrologue<T>(Node[] to, Node? statement, System.Func<T, Node, bool> isPrologueDirective, T callee)
    {
        if (statement is null)
        {
            return to;
        }

        var statementIdx = 0;
        // skip all prologue directives to insert at the correct position
        for (; statementIdx < to.Length; statementIdx++)
        {
            if (!isPrologueDirective(callee, to[statementIdx]))
            {
                break;
            }
        }

        // Go: slices.Insert(to, statementIdx, statement)
        var inserted = new Node[to.Length + 1];
        System.Array.Copy(to, 0, inserted, 0, statementIdx);
        inserted[statementIdx] = statement;
        System.Array.Copy(to, statementIdx, inserted, statementIdx + 1, to.Length - statementIdx);
        return inserted;
    }

    /// <summary>Go: <c>func sortImportSpecifiers(a *ast.Node, b *ast.Node) int</c>（jsx.go:183-189）。</summary>
    private static int SortImportSpecifiers(Node a, Node b)
    {
        var res = CompareStringsCaseSensitive(a.PropertyName()!.Text(), b.PropertyName()!.Text());
        if (res != 0)
        {
            return res;
        }

        return CompareStringsCaseSensitive(a.AsImportSpecifier()!.Name()!.Text(), b.AsImportSpecifier()!.Name()!.Text());
    }

    /// <summary>Go: <c>func getSortedSpecifiers(m map[string]*ast.Node) []*ast.Node</c>（jsx.go:191-195）。</summary>
    internal static Node[] GetSortedSpecifiers(Dictionary<string, Node> m)
    {
        var res = new List<Node>(m.Values);
        res.Sort(SortImportSpecifiers);
        return res.ToArray();
    }

    /// <summary>Go: <c>func hasProto(obj *ast.ObjectLiteralExpression) bool</c>（jsx.go:481-488）。</summary>
    internal static bool HasProto(ObjectLiteralExpression obj)
    {
        foreach (var p in obj.Properties!.Nodes ?? [])
        {
            if (IsPropertyAssignment(p) && (IsStringLiteral(p.Name()!) || IsIdentifier(p.Name()!)) && p.Name()!.Text() == "__proto__")
            {
                return true;
            }
        }

        return false;
    }
}
