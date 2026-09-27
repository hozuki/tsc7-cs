// ported from: internal/transformers/jsxtransforms/jsx.go:550-776
//   （JSX 运行时调用构造：jsx/jsxs/jsxDEV 参数、__source/__self dev 标志、createElement 回退、
//     React 命名空间与 factory 表达式）
//
// 与 Go 的差异（C# 语言特性）：
// 1) Go 的 `len(nonWhitespaceChildren) > 1 || (len(...) == 1 && ...)`：`children == nil` 时 Go 的切片是 nil，
//    C# 用 `[]`（两者在该表达式与后续用途上完全等价，且保住短路求值）。
// 2) `strconv.FormatInt(int64(line+1), 10)` → `(line + 1).ToString(CultureInfo.InvariantCulture)`（数字文本一致；
//    `scanner.GetECMALineAndUTF16CharacterOfPosition` 的 `UTF16Offset` 有到 int 的隐式转换）。
// 3) Go 的 `react.Flags &^= ast.NodeFlagsSynthesized` → `react.Flags &= ~NodeFlags.Synthesized`。
// 4) 参数名 `object` 是 C# 关键字 → `@object`（保留 Go 的可读性，同 Printer/NodeFactory.cs 的先例）。
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

namespace TypeScript.Transformers.JsxTransforms;

public sealed partial class JSXTransformer
{
    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxOpeningLikeElementOrFragmentJSX(...)</c>（jsx.go:550-603）。</summary>
    private Node VisitJsxOpeningLikeElementOrFragmentJSX(
        Node tagName,
        Node @object,
        Node? keyAttr,
        NodeList? children,
        TextRange location)
    {
        var nonWhitespaceChildren = children is null ? [] : GetSemanticJsxChildren(children.Nodes ?? []);
        var isStaticChildren = nonWhitespaceChildren.Length > 1
            || (nonWhitespaceChildren.Length == 1
                && IsJsxExpression(nonWhitespaceChildren[0])
                && nonWhitespaceChildren[0].AsJsxExpression()!.DotDotDotToken is not null);
        var args = new List<Node>(3) { tagName, @object };
        // function jsx(type, config, maybeKey) {}
        // "maybeKey" is optional. It is acceptable to use "_jsx" without a third argument
        if (keyAttr is not null)
        {
            args.Add(TransformJsxAttributeInitializer(keyAttr.Initializer()));
        }

        if (_compilerOptions.Jsx == JsxEmit.ReactJSXDev)
        {
            var originalFile = EmitContext().MostOriginal(_currentSourceFile!);
            if (originalFile is not null && IsSourceFile(originalFile))
            {
                // "maybeKey" has to be replaced with "void 0" to not break the jsxDEV signature
                if (keyAttr is null)
                {
                    args.Add(Factory().NewVoidZeroExpression());
                }

                // isStaticChildren development flag
                if (isStaticChildren)
                {
                    args.Add(Factory().NewTrueExpression());
                }
                else
                {
                    args.Add(Factory().NewFalseExpression());
                }

                // __source development flag
                var (line, col) = GetECMALineAndUTF16CharacterOfPosition(originalFile.AsSourceFile()!, location.Pos());
                args.Add(Factory().NewObjectLiteralExpression(Factory().NewNodeList(
                [
                    Factory().NewPropertyAssignment(null, Factory().NewIdentifier("fileName"), null, null, GetCurrentFileNameExpression()),
                    Factory().NewPropertyAssignment(null, Factory().NewIdentifier("lineNumber"), null, null, Factory().NewNumericLiteral((line + 1).ToString(CultureInfo.InvariantCulture), TokenFlags.None)),
                    Factory().NewPropertyAssignment(null, Factory().NewIdentifier("columnNumber"), null, null, Factory().NewNumericLiteral((col + 1).ToString(CultureInfo.InvariantCulture), TokenFlags.None)),
                ]), false));
                // __self development flag
                args.Add(Factory().NewThisExpression());
            }
        }

        var element = Factory().NewCallExpression(GetJsxFactoryCallee(isStaticChildren), null, null, Factory().NewNodeList(args.ToArray()), NodeFlags.None);
        element.Loc = location;

        if (_inJsxChild)
        {
            EmitContext().AddEmitFlags(element, EmitFlags.StartOnNewLine);
        }

        return element;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxOpeningFragmentJSX(fragment *ast.JsxOpeningFragment, children *ast.NodeList, location core.TextRange) *ast.Node</c>（jsx.go:605-623）。</summary>
    private Node VisitJsxOpeningFragmentJSX(JsxOpeningFragment fragment, NodeList? children, TextRange location)
    {
        Node? childrenProps = null;
        if (children is not null && children.Nodes is { Length: > 0 })
        {
            var result = ConvertJsxChildrenToChildrenPropObject(children.Nodes);
            if (result is not null)
            {
                childrenProps = result;
            }
        }

        childrenProps ??= Factory().NewObjectLiteralExpression(Factory().NewNodeList([]), false);
        return VisitJsxOpeningLikeElementOrFragmentJSX(
            GetImplicitJsxFragmentReference(),
            childrenProps,
            null,
            children,
            location);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) createReactNamespace(reactNamespace string, parent *ast.Node) *ast.Node</c>（jsx.go:625-649）。</summary>
    private Node CreateReactNamespace(string reactNamespace, Node parent)
    {
        // To ensure the emit resolver can properly resolve the namespace, we need to
        // treat this identifier as if it were a source tree node by clearing the `Synthesized`
        // flag and setting a parent node. TODO: Is this still true? The emit resolver is supposed to be
        // hardened aginast this, so long as the node retains original node pointers back to a parsed node
        if (reactNamespace.Length == 0)
        {
            reactNamespace = "React";
        }

        var react = Factory().NewIdentifier(reactNamespace);
        react.Flags &= ~NodeFlags.Synthesized;

        // Set the parent that is in parse tree
        // this makes sure that parent chain is intact for checker to traverse complete scope tree
        react.Parent = EmitContext().ParseNode(parent); // Parent is intentionally wired to a parse-tree node for resolver traversal.

        // If the identifier refers to an exported member of a namespace, substitute with
        // a qualified namespace property access (e.g., `React` -> `M.React`).
        // See also: RuntimeSyntaxTransformer.visitExpressionIdentifier in runtimesyntax.go
        var container = _emitResolver.GetReferencedExportContainer(react, false /*prefixLocals*/);
        if (container is not null && IsModuleDeclaration(container))
        {
            var containerName = Factory().NewGeneratedNameForNode(container);
            return Factory().NewPropertyAccessExpression(containerName, null, react, NodeFlags.None);
        }

        return react;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) createJsxFactoryExpressionFromEntityName(e *ast.Node, parent *ast.Node) *ast.Node</c>（jsx.go:651-658）。</summary>
    private Node CreateJsxFactoryExpressionFromEntityName(Node e, Node parent)
    {
        if (IsQualifiedName(e))
        {
            var left = CreateJsxFactoryExpressionFromEntityName(e.AsQualifiedName()!.Left!, parent);
            var right = Factory().NewIdentifier(e.AsQualifiedName()!.Right!.Text());
            return Factory().NewPropertyAccessExpression(left, null, right, NodeFlags.None);
        }

        return CreateReactNamespace(e.Text(), parent);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) createJsxPseudoFactoryExpression(parent *ast.Node, e *ast.Node, target string) *ast.Node</c>（jsx.go:660-670）。</summary>
    private Node CreateJsxPseudoFactoryExpression(Node parent, Node? e, string target)
    {
        if (e is not null)
        {
            return CreateJsxFactoryExpressionFromEntityName(e, parent);
        }

        return Factory().NewPropertyAccessExpression(
            CreateReactNamespace(_compilerOptions.ReactNamespace, parent),
            null,
            Factory().NewIdentifier(target),
            NodeFlags.None);
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) createJsxFactoryExpression(parent *ast.Node) *ast.Node</c>（jsx.go:672-675）。</summary>
    private Node CreateJsxFactoryExpression(Node parent)
    {
        var e = _emitResolver.GetJsxFactoryEntity(_currentSourceFile!);
        return CreateJsxPseudoFactoryExpression(parent, e, "createElement");
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) createJsxFragmentFactoryExpression(parent *ast.Node) *ast.Node</c>（jsx.go:677-680）。</summary>
    private Node CreateJsxFragmentFactoryExpression(Node parent)
    {
        var e = _emitResolver.GetJsxFragmentFactoryEntity(_currentSourceFile!);
        return CreateJsxPseudoFactoryExpression(parent, e, "Fragment");
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxOpeningLikeElementCreateElement(element *ast.Node, children *ast.NodeList, location core.TextRange) *ast.Node</c>（jsx.go:682-734）。</summary>
    private Node VisitJsxOpeningLikeElementCreateElement(Node element, NodeList? children, TextRange location)
    {
        var tagName = GetTagName(element);
        var attrs = element.Attributes()!.Properties() ?? [];
        Node objectProperties;
        if (attrs.Length > 0)
        {
            objectProperties = TransformJsxAttributesToObjectProps(attrs, null);
        }
        else
        {
            objectProperties = Factory().NewKeywordExpression(Kind.NullKeyword); // When there are no attributes, React wants "null"
        }

        Node callee;
        if (_importSpecifier.Length == 0)
        {
            callee = CreateJsxFactoryExpression(element);
        }
        else
        {
            callee = GetImplicitImportForName("createElement");
        }

        var newChildren = new List<Node>();
        if (children is not null && children.Nodes is { Length: > 0 })
        {
            foreach (var c in children.Nodes)
            {
                var res = TransformJsxChildToExpression(c);
                if (res is not null)
                {
                    newChildren.Add(res);
                }
            }
        }

        // Add StartOnNewLine flag only if there are multiple actual children (after filtering)
        if (newChildren.Count > 1)
        {
            foreach (var child in newChildren)
            {
                EmitContext().AddEmitFlags(child, EmitFlags.StartOnNewLine);
            }
        }

        var args = new List<Node>(newChildren.Count + 2) { tagName, objectProperties };
        args.AddRange(newChildren);

        var result = Factory().NewCallExpression(
            callee,
            null,
            null,
            Factory().NewNodeList(args.ToArray()),
            NodeFlags.None);
        result.Loc = location;

        if (_inJsxChild)
        {
            EmitContext().AddEmitFlags(result, EmitFlags.StartOnNewLine);
        }

        return result;
    }

    /// <summary>Go: <c>func (tx *JSXTransformer) visitJsxOpeningFragmentCreateElement(fragment *ast.JsxOpeningFragment, children *ast.NodeList, location core.TextRange) *ast.Node</c>（jsx.go:736-775）。</summary>
    private Node VisitJsxOpeningFragmentCreateElement(JsxOpeningFragment fragment, NodeList? children, TextRange location)
    {
        var tagName = CreateJsxFragmentFactoryExpression(fragment);
        var callee = CreateJsxFactoryExpression(fragment);

        var newChildren = new List<Node>();
        if (children is not null && children.Nodes is { Length: > 0 })
        {
            foreach (var c in children.Nodes)
            {
                var res = TransformJsxChildToExpression(c);
                if (res is not null)
                {
                    newChildren.Add(res);
                }
            }
        }

        // Add StartOnNewLine flag only if there are multiple actual children (after filtering)
        if (newChildren.Count > 1)
        {
            foreach (var child in newChildren)
            {
                EmitContext().AddEmitFlags(child, EmitFlags.StartOnNewLine);
            }
        }

        var args = new List<Node>(newChildren.Count + 2) { tagName, Factory().NewKeywordExpression(Kind.NullKeyword) };
        args.AddRange(newChildren);

        var result = Factory().NewCallExpression(
            callee,
            null,
            null,
            Factory().NewNodeList(args.ToArray()),
            NodeFlags.None);
        result.Loc = location;

        if (_inJsxChild)
        {
            EmitContext().AddEmitFlags(result, EmitFlags.StartOnNewLine);
        }

        return result;
    }
}
