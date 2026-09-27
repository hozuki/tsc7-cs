// ported from: internal/printer/factory.go:1-1316
//
// 这是 **emit 专用** 的 NodeFactory（带合成节点追踪），与 Ast.NodeFactory 分开（同名不同命名空间）。
//
// 偏离 Go 的地方：
// 1) Go 用**值内嵌** `ast.NodeFactory` 复用全部 New*/Update* 工厂方法；C# 用**继承**
//    `NodeFactory : Ast.NodeFactory`（嵌入即 is-a；Ast.NodeFactory 的钩子字段 `_hooks` 是 internal，同程序集可写）。
//    与 Go 的唯一可见差异：Go 的 `f.AsNodeFactory()` 返回内嵌的那一份工厂（`&f.NodeFactory`），C# 返回 `this`
//    （静态类型仍是 Ast.NodeFactory）；两者创建节点时用的 hooks/计数器是同一份状态，行为一致。
// 2) Go 未导出字段 `emitContext` → `_emitContext`；未导出方法 `newGeneratedIdentifier`/`getName`/
//    `isIgnorableParen`/`updateOuterExpression`/`flattenComma*` → private PascalCase。
// 3) `flattenCommaElement` 在 Go 里靠 `append` 就地扩容切片；C# 用 `List<*>`（docs §3.2），遍历顺序不变。
// 4) `EnsureUseStrict`：Go 的 `append([]*ast.Statement{useStrictPrologue}, statements...)` → C# 显式新数组 + CopyTo
//    （避免 LINQ/多余拷贝）。
// 5) Go 的 `panic(fmt.Sprintf(...))` → `Debug.Fail($"...")`（docs §3.2）。
// 6) Go 的 `strconv.Itoa(n)` → `n.ToString(CultureInfo.InvariantCulture)`（数字文本，与 Go 一致）。
// 7) 参数名 `object` 是 C# 关键字 → `@object`（保留 Go 的可读性）。
using System.Collections.Generic;
using System.Globalization;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using AstNodeFactory = TypeScript.Ast.NodeFactory;

namespace TypeScript.Printer;

public sealed class NodeFactory : AstNodeFactory
{
    private readonly EmitContext _emitContext;

    public NodeFactory(EmitContext context)
    {
        _emitContext = context;
        _hooks = new NodeFactoryHooks
        {
            OnCreate = context.OnCreate,
            OnUpdate = context.OnUpdate,
            OnClone = context.OnClone,
        };
    }

    private Node NewGeneratedIdentifier(GeneratedIdentifierFlags kind, string text, Node? node, AutoGenerateOptions options)
    {
        var id = Printer.NextAutoGenerateId();

        if (text.Length == 0)
        {
            if (node is null)
            {
                text = $"(auto@{id})";
            }
            else if (IsMemberName(node))
            {
                text = node.Text();
            }
            else
            {
                text = $"(generated@{GetNodeId(_emitContext.GetNodeForGeneratedNameWorker(node, id))})";
            }
            text = Printer.FormatGeneratedName(false /*privateName*/, options.Prefix, text, options.Suffix);
        }

        var name = NewIdentifier(text);
        var autoGenerate = new AutoGenerateInfo
        {
            Id = id,
            Flags = kind | (options.Flags & ~GeneratedIdentifierFlags.KindMask),
            Prefix = options.Prefix,
            Suffix = options.Suffix,
            Node = node,
        };
        _emitContext._autoGenerate ??= [];
        _emitContext._autoGenerate[name] = autoGenerate;
        return name;
    }

    /// <summary>
    /// Allocates a new temp variable name, but does not record it in the environment. It is recommended to pass this
    /// to either `AddVariableDeclaration` or `AddLexicalDeclaration` to ensure it is properly tracked, if you are not
    /// otherwise handling it yourself.
    /// </summary>
    public Node NewTempVariable()
    {
        return NewTempVariableEx(new AutoGenerateOptions());
    }

    /// <summary>
    /// Allocates a new temp variable name, but does not record it in the environment. It is recommended to pass this
    /// to either `AddVariableDeclaration` or `AddLexicalDeclaration` to ensure it is properly tracked, if you are not
    /// otherwise handling it yourself.
    /// </summary>
    public Node NewTempVariableEx(AutoGenerateOptions options)
    {
        return NewGeneratedIdentifier(GeneratedIdentifierFlags.Auto, "", null /*node*/, options);
    }

    /// <summary>Allocates a new loop variable name.</summary>
    public Node NewLoopVariable()
    {
        return NewLoopVariableEx(new AutoGenerateOptions());
    }

    /// <summary>Allocates a new loop variable name.</summary>
    public Node NewLoopVariableEx(AutoGenerateOptions options)
    {
        return NewGeneratedIdentifier(GeneratedIdentifierFlags.Loop, "", null /*node*/, options);
    }

    /// <summary>Allocates a new unique name based on the provided text.</summary>
    public Node NewUniqueName(string text)
    {
        return NewUniqueNameEx(text, new AutoGenerateOptions());
    }

    /// <summary>Allocates a new unique name based on the provided text.</summary>
    public Node NewUniqueNameEx(string text, AutoGenerateOptions options)
    {
        return NewGeneratedIdentifier(GeneratedIdentifierFlags.Unique, text, null /*node*/, options);
    }

    /// <summary>Allocates a new unique name based on the provided node.</summary>
    public Node NewGeneratedNameForNode(Node? node)
    {
        return NewGeneratedNameForNodeEx(node, new AutoGenerateOptions());
    }

    /// <summary>Allocates a new unique name based on the provided node.</summary>
    public Node NewGeneratedNameForNodeEx(Node? node, AutoGenerateOptions options)
    {
        if (options.Prefix.Length > 0 || options.Suffix.Length > 0)
        {
            options.Flags |= GeneratedIdentifierFlags.Optimistic;
        }

        return NewGeneratedIdentifier(GeneratedIdentifierFlags.Node, "", node, options);
    }

    private Node NewGeneratedPrivateIdentifier(GeneratedIdentifierFlags kind, string text, Node? node, AutoGenerateOptions options)
    {
        var id = Printer.NextAutoGenerateId();

        if (text.Length == 0)
        {
            if (node is null)
            {
                text = $"(auto@{id})";
            }
            else if (IsMemberName(node))
            {
                text = node.Text();
            }
            else
            {
                text = $"(generated@{GetNodeId(_emitContext.GetNodeForGeneratedNameWorker(node, id))})";
            }
            text = Printer.FormatGeneratedName(true /*privateName*/, options.Prefix, text, options.Suffix);
        }
        else if (!text.StartsWith('#'))
        {
            Debug.Fail("First character of private identifier must be #: " + text);
        }

        var name = NewPrivateIdentifier(text);
        var autoGenerate = new AutoGenerateInfo
        {
            Id = id,
            Flags = kind | (options.Flags & ~GeneratedIdentifierFlags.KindMask),
            Prefix = options.Prefix,
            Suffix = options.Suffix,
            Node = node,
        };
        _emitContext._autoGenerate ??= [];
        _emitContext._autoGenerate[name] = autoGenerate;
        return name;
    }

    /// <summary>Allocates a new unique private name based on the provided text.</summary>
    public Node NewUniquePrivateName(string text)
    {
        return NewUniquePrivateNameEx(text, new AutoGenerateOptions());
    }

    /// <summary>Allocates a new unique private name based on the provided text.</summary>
    public Node NewUniquePrivateNameEx(string text, AutoGenerateOptions options)
    {
        return NewGeneratedPrivateIdentifier(GeneratedIdentifierFlags.Unique, text, null /*node*/, options);
    }

    /// <summary>Allocates a new unique private name based on the provided node.</summary>
    public Node NewGeneratedPrivateNameForNode(Node node)
    {
        return NewGeneratedPrivateNameForNodeEx(node, new AutoGenerateOptions());
    }

    /// <summary>Allocates a new unique private name based on the provided node.</summary>
    public Node NewGeneratedPrivateNameForNodeEx(Node node, AutoGenerateOptions options)
    {
        if (options.Prefix.Length > 0 || options.Suffix.Length > 0)
        {
            options.Flags |= GeneratedIdentifierFlags.Optimistic;
        }

        return NewGeneratedPrivateIdentifier(GeneratedIdentifierFlags.Node, "", node, options);
    }

    /// <summary>
    /// Allocates a new StringLiteral whose source text is derived from the provided node. This is often used to create
    /// a string representation of an Identifier or NumericLiteral.
    /// </summary>
    public Node NewStringLiteralFromNode(Node textSourceNode)
    {
        var text = "";
        switch (textSourceNode.Kind)
        {
            case Kind.Identifier:
            case Kind.PrivateIdentifier:
            case Kind.JsxNamespacedName:
            case Kind.StringLiteral:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.NoSubstitutionTemplateLiteral:
            case Kind.TemplateHead:
            case Kind.TemplateMiddle:
            case Kind.TemplateTail:
            case Kind.RegularExpressionLiteral:
                text = textSourceNode.Text();
                break;
        }
        var node = NewStringLiteral(text, TokenFlags.None);
        _emitContext._textSource ??= [];
        _emitContext._textSource[node] = textSourceNode;
        return node;
    }

    //
    // Common Tokens
    //

    public Node NewThisExpression()
    {
        return NewKeywordExpression(Kind.ThisKeyword);
    }

    public Node NewTrueExpression()
    {
        return NewKeywordExpression(Kind.TrueKeyword);
    }

    public Node NewFalseExpression()
    {
        return NewKeywordExpression(Kind.FalseKeyword);
    }

    //
    // Common Operators
    //

    public Node NewCommaExpression(Node left, Node right)
    {
        return NewBinaryExpression(null /*modifiers*/, left, null /*typeNode*/, NewToken(Kind.CommaToken), right);
    }

    public Node NewAssignmentExpression(Node left, Node right)
    {
        return NewBinaryExpression(null /*modifiers*/, left, null /*typeNode*/, NewToken(Kind.EqualsToken), right);
    }

    public Node NewLogicalORExpression(Node left, Node right)
    {
        return NewBinaryExpression(null /*modifiers*/, left, null /*typeNode*/, NewToken(Kind.BarBarToken), right);
    }

    public Node NewLogicalANDExpression(Node left, Node right)
    {
        return NewBinaryExpression(null /*modifiers*/, left, null /*typeNode*/, NewToken(Kind.AmpersandAmpersandToken), right);
    }

    // func (f *NodeFactory) NewLogicalANDExpression(left *ast.Expression, right *ast.Expression) *ast.Expression
    // func (f *NodeFactory) NewBitwiseORExpression(left *ast.Expression, right *ast.Expression) *ast.Expression
    // func (f *NodeFactory) NewBitwiseXORExpression(left *ast.Expression, right *ast.Expression) *ast.Expression
    // func (f *NodeFactory) NewBitwiseANDExpression(left *ast.Expression, right *ast.Expression) *ast.Expression
    public Node NewStrictEqualityExpression(Node left, Node right)
    {
        return NewBinaryExpression(null /*modifiers*/, left, null /*typeNode*/, NewToken(Kind.EqualsEqualsEqualsToken), right);
    }

    public Node NewStrictInequalityExpression(Node left, Node right)
    {
        return NewBinaryExpression(null /*modifiers*/, left, null /*typeNode*/, NewToken(Kind.ExclamationEqualsEqualsToken), right);
    }

    //
    // Compound Nodes
    //

    public Node NewVoidZeroExpression()
    {
        return NewVoidExpression(NewNumericLiteral("0", TokenFlags.None));
    }

    private static void FlattenCommaElement(Node node, List<Node> expressions)
    {
        if (IsBinaryExpression(node) && NodeIsSynthesized(node) && node.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken)
        {
            FlattenCommaElement(node.AsBinaryExpression()!.Left!, expressions);
            FlattenCommaElement(node.AsBinaryExpression()!.Right!, expressions);
        }
        else
        {
            expressions.Add(node);
        }
    }

    private static Node[] FlattenCommaElements(Node[] expressions)
    {
        var result = new List<Node>();
        foreach (var expression in expressions)
        {
            FlattenCommaElement(expression, result);
        }
        return result.ToArray();
    }

    /// <summary>
    /// Converts a slice of expressions into a single comma-delimited expression. Returns null if expressions is null
    /// or empty.
    /// </summary>
    public Node? InlineExpressions(Node[]? expressions)
    {
        if (expressions is null || expressions.Length == 0)
        {
            return null;
        }
        if (expressions.Length == 1)
        {
            return expressions[0];
        }
        expressions = FlattenCommaElements(expressions);
        var expression = expressions[0];
        for (var i = 1; i < expressions.Length; i++)
        {
            expression = NewCommaExpression(expression, expressions[i]);
        }
        return expression;
    }

    //
    // Utilities
    //

    public Node CreateExpressionFromEntityName(Node node)
    {
        if (IsQualifiedName(node))
        {
            var left = CreateExpressionFromEntityName(node.AsQualifiedName()!.Left!);
            var right = node.AsQualifiedName()!.Right!.Clone(AsNodeFactory())!;
            right.Loc = node.AsQualifiedName()!.Right!.Loc;
            // TODO(rbuckton): Does this need to be parented?
            right.Parent = node.AsQualifiedName()!.Right!.Parent;
            var propAccess = NewPropertyAccessExpression(left, null, right, NodeFlags.None);
            propAccess.Loc = node.Loc;
            return propAccess;
        }
        var res = node.Clone(AsNodeFactory())!;
        res.Loc = node.Loc;
        // TODO(rbuckton): Does this need to be parented?
        res.Parent = node.Parent;
        return res;
    }

    public Node RestoreEnclosingLabel(Node node, LabeledStatement? outermostLabeledStatement)
    {
        if (outermostLabeledStatement is null)
        {
            return node;
        }
        var innerLabel = node;
        if (IsLabeledStatement(outermostLabeledStatement.Statement!))
        {
            innerLabel = RestoreEnclosingLabel(node, outermostLabeledStatement.Statement!.AsLabeledStatement());
        }
        return UpdateLabeledStatement(
            outermostLabeledStatement,
            outermostLabeledStatement.Label,
            innerLabel);
    }

    /// <summary>CreateForOfBindingStatement creates a statement to bind the iteration value.</summary>
    public Node CreateForOfBindingStatement(Node node, Node boundValue)
    {
        if (IsVariableDeclarationList(node))
        {
            var firstDeclaration = node.AsVariableDeclarationList()!.Declarations!.Nodes![0];
            var updatedDeclaration = UpdateVariableDeclaration(
                firstDeclaration.AsVariableDeclaration()!,
                firstDeclaration.Name(),
                null, /*exclamationToken*/
                null, /*type*/
                boundValue);
            var statement = NewVariableStatement(
                null,
                UpdateVariableDeclarationList(
                    node.AsVariableDeclarationList()!,
                    NewNodeList([updatedDeclaration]),
                    node.AsVariableDeclarationList()!.Flags));
            statement.Loc = node.Loc;
            return statement;
        }
        var updatedExpression = NewAssignmentExpression(node, boundValue);
        updatedExpression.Loc = node.Loc;
        var expressionStatement = NewExpressionStatement(updatedExpression);
        expressionStatement.Loc = node.Loc;
        return expressionStatement;
    }

    public Node NewTypeCheck(Node value, string tag)
    {
        if (tag == "null")
        {
            return NewStrictEqualityExpression(value, NewKeywordExpression(Kind.NullKeyword));
        }
        else if (tag == "undefined")
        {
            return NewStrictEqualityExpression(value, NewVoidZeroExpression());
        }
        else
        {
            return NewStrictEqualityExpression(NewTypeOfExpression(value), NewStringLiteral(tag, TokenFlags.None));
        }
    }

    public Node NewMethodCall(Node @object, Node methodName, Node[] argumentsList)
    {
        // Preserve the optionality of `object`.
        if (IsCallExpression(@object) && (@object.Flags & NodeFlags.OptionalChain) != 0)
        {
            return NewCallExpression(
                NewPropertyAccessExpression(@object, null, methodName, NodeFlags.None),
                null,
                null,
                NewNodeList(argumentsList),
                NodeFlags.OptionalChain);
        }
        return NewCallExpression(
            NewPropertyAccessExpression(@object, null, methodName, NodeFlags.None),
            null,
            null,
            NewNodeList(argumentsList),
            NodeFlags.None);
    }

    public Node NewGlobalMethodCall(string globalObjectName, string methodName, Node[] argumentsList)
    {
        return NewMethodCall(NewIdentifier(globalObjectName), NewIdentifier(methodName), argumentsList);
    }

    public Node NewFunctionCallCall(Node target, Node thisArg, Node[] argumentsList)
    {
        if (thisArg is null)
        {
            throw new DebugFailureException("Attempted to construct function call call without this argument expression");
        }
        var args = new Node[argumentsList.Length + 1];
        args[0] = thisArg;
        Array.Copy(argumentsList, 0, args, 1, argumentsList.Length);
        return NewMethodCall(target, NewIdentifier("call"), args);
    }

    public Node NewArraySliceCall(Node array, int start)
    {
        var args = new List<Node>();
        if (start != 0)
        {
            args.Add(NewNumericLiteral(start.ToString(CultureInfo.InvariantCulture), TokenFlags.None));
        }
        return NewMethodCall(array, NewIdentifier("slice"), args.ToArray());
    }

    /// <summary>
    /// Determines whether a node is a parenthesized expression that can be ignored when recreating outer expressions.
    ///
    /// A parenthesized expression can be ignored when all of the following are true:
    ///
    /// - It's `pos` and `end` are not -1
    /// - It does not have a custom source map range
    /// - It does not have a custom comment range
    /// - It does not have synthetic leading or trailing comments
    ///
    /// If an outermost parenthesized expression is ignored, but the containing expression requires a parentheses around
    /// the expression to maintain precedence, a new parenthesized expression should be created automatically when
    /// the containing expression is created/updated.
    /// </summary>
    private bool IsIgnorableParen(Node node)
    {
        return IsParenthesizedExpression(node) &&
            NodeIsSynthesized(node) &&
            RangeIsSynthesized(_emitContext.SourceMapRange(node)) &&
            RangeIsSynthesized(_emitContext.CommentRange(node)); // &&
        // len(emitContext.SyntheticLeadingComments(node)) == 0 &&
        // len(emitContext.SyntheticTrailingComments(node)) == 0
    }

    private Node UpdateOuterExpression(Node outerExpression, Node expression)
    {
        switch (outerExpression.Kind)
        {
            case Kind.ParenthesizedExpression:
                return UpdateParenthesizedExpression(outerExpression.AsParenthesizedExpression()!, expression);
            case Kind.TypeAssertionExpression:
                return UpdateTypeAssertion(outerExpression.AsTypeAssertion()!, outerExpression.Type(), expression);
            case Kind.AsExpression:
                return UpdateAsExpression(outerExpression.AsAsExpression()!, expression, outerExpression.Type());
            case Kind.SatisfiesExpression:
                return UpdateSatisfiesExpression(outerExpression.AsSatisfiesExpression()!, expression, outerExpression.Type());
            case Kind.NonNullExpression:
                return UpdateNonNullExpression(outerExpression.AsNonNullExpression()!, expression, outerExpression.Flags);
            case Kind.ExpressionWithTypeArguments:
                return UpdateExpressionWithTypeArguments(outerExpression.AsExpressionWithTypeArguments()!, expression, outerExpression.TypeArgumentList());
            case Kind.PartiallyEmittedExpression:
                return UpdatePartiallyEmittedExpression(outerExpression.AsPartiallyEmittedExpression()!, expression);
            default:
                Debug.Fail($"Unexpected outer expression kind: {outerExpression.Kind}");
                return expression;
        }
    }

    public Node RestoreOuterExpressions(Node? outerExpression, Node innerExpression, OuterExpressionKinds kinds)
    {
        if (outerExpression is not null && IsOuterExpression(outerExpression, kinds) && !IsIgnorableParen(outerExpression))
        {
            return UpdateOuterExpression(
                outerExpression,
                RestoreOuterExpressions(outerExpression.Expression(), innerExpression, OuterExpressionKinds.All));
        }
        return innerExpression;
    }

    /// <summary>Ensures `"use strict"` is the first statement of a slice of statements.</summary>
    public Node[] EnsureUseStrict(Node[] statements)
    {
        foreach (var statement in statements)
        {
            if (IsPrologueDirective(statement) && statement.Expression()!.Text() == "use strict")
            {
                return statements;
            }
            else
            {
                break;
            }
        }
        var useStrictPrologue = NewExpressionStatement(NewStringLiteral("use strict", TokenFlags.None));
        var result = new Node[statements.Length + 1];
        result[0] = useStrictPrologue;
        Array.Copy(statements, 0, result, 1, statements.Length);
        return result;
    }

    /// <summary>Splits a slice of statements into two parts: standard prologue statements and the rest.</summary>
    public (Node[] Prologue, Node[]? RestNodes) SplitStandardPrologue(Node[] source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (!IsPrologueDirective(source[i]))
            {
                return (source[..i], source[i..]);
            }
        }
        return (source, null);
    }

    /// <summary>
    /// Splits a slice of statements into two parts: custom prologue statements (e.g., with `EFCustomPrologue` set) and
    /// the rest.
    /// </summary>
    public (Node[]? Prologue, Node[] RestNodes) SplitCustomPrologue(Node[] source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            if (IsPrologueDirective(source[i]) || (_emitContext.EmitFlags(source[i]) & EmitFlags.CustomPrologue) == 0)
            {
                return (source[..i], source[i..]);
            }
        }
        return (null, source);
    }

    //
    // Declaration Names
    //

    /// <summary>Go: <c>type NameOptions struct</c>。</summary>
    public struct NameOptions
    {
        /// <summary>indicates whether comments may be emitted for the name.</summary>
        public bool AllowComments;

        /// <summary>indicates whether source maps may be emitted for the name.</summary>
        public bool AllowSourceMaps;
    }

    /// <summary>Go: <c>type AssignedNameOptions struct</c>。</summary>
    public struct AssignedNameOptions
    {
        /// <summary>indicates whether comments may be emitted for the name.</summary>
        public bool AllowComments;

        /// <summary>indicates whether source maps may be emitted for the name.</summary>
        public bool AllowSourceMaps;

        /// <summary>indicates whether the assigned name of a declaration shouldn't be considered.</summary>
        public bool IgnoreAssignedName;
    }

    private Node GetName(Node? node, EmitFlags emitFlags, AssignedNameOptions opts)
    {
        Node? nodeName = null;
        if (node is not null)
        {
            if (opts.IgnoreAssignedName)
            {
                nodeName = GetNonAssignedNameOfDeclaration(node);
            }
            else
            {
                nodeName = GetNameOfDeclaration(node);
            }
        }

        if (nodeName is not null)
        {
            var name = nodeName.Clone(this)!;
            if (!opts.AllowComments)
            {
                emitFlags |= EmitFlags.NoComments;
            }
            if (!opts.AllowSourceMaps)
            {
                emitFlags |= EmitFlags.NoSourceMap;
            }
            _emitContext.AddEmitFlags(name, emitFlags);
            return name;
        }

        return NewGeneratedNameForNode(node);
    }

    /// <summary>
    /// Gets the local name of a declaration. This is primarily used for declarations that can be referred to by name in
    /// the declaration's immediate scope (classes, enums, namespaces). A local name will *never* be prefixed with a
    /// module or namespace export modifier like "exports." when emitted as an expression.
    /// </summary>
    public Node GetLocalName(Node? node)
    {
        return GetLocalNameEx(node, default);
    }

    /// <summary>
    /// Gets the local name of a declaration. This is primarily used for declarations that can be referred to by name in
    /// the declaration's immediate scope (classes, enums, namespaces). A local name will *never* be prefixed with a
    /// module or namespace export modifier like "exports." when emitted as an expression.
    /// </summary>
    public Node GetLocalNameEx(Node? node, AssignedNameOptions opts)
    {
        return GetName(node, EmitFlags.LocalName, opts);
    }

    /// <summary>
    /// Gets the export name of a declaration. This is primarily used for declarations that can be
    /// referred to by name in the declaration's immediate scope (classes, enums, namespaces). An
    /// export name will *always* be prefixed with an module or namespace export modifier like
    /// `"exports."` when emitted as an expression if the name points to an exported symbol.
    /// </summary>
    public Node GetExportName(Node? node)
    {
        return GetExportNameEx(node, default);
    }

    /// <summary>
    /// Gets the export name of a declaration. This is primarily used for declarations that can be
    /// referred to by name in the declaration's immediate scope (classes, enums, namespaces). An
    /// export name will *always* be prefixed with an module or namespace export modifier like
    /// `"exports."` when emitted as an expression if the name points to an exported symbol.
    /// </summary>
    public Node GetExportNameEx(Node? node, AssignedNameOptions opts)
    {
        return GetName(node, EmitFlags.ExportName, opts);
    }

    /// <summary>Gets the name of a declaration to use during emit.</summary>
    public Node GetDeclarationName(Node? node)
    {
        return GetDeclarationNameEx(node, default);
    }

    /// <summary>Gets the name of a declaration to use during emit.</summary>
    public Node GetDeclarationNameEx(Node? node, NameOptions opts)
    {
        return GetName(node, EmitFlags.None, new AssignedNameOptions { AllowComments = opts.AllowComments, AllowSourceMaps = opts.AllowSourceMaps });
    }

    public Node GetNamespaceMemberName(Node ns, Node name, NameOptions opts)
    {
        if (!_emitContext.HasAutoGenerateInfo(name))
        {
            name = name.Clone(this)!;
        }
        var qualifiedName = NewPropertyAccessExpression(ns, null /*questionDotToken*/, name, NodeFlags.None);
        _emitContext.AssignCommentAndSourceMapRanges(qualifiedName, name);
        if (!opts.AllowComments)
        {
            _emitContext.AddEmitFlags(qualifiedName, EmitFlags.NoComments);
        }
        if (!opts.AllowSourceMaps)
        {
            _emitContext.AddEmitFlags(qualifiedName, EmitFlags.NoSourceMap);
        }
        return qualifiedName;
    }

    /// <summary>
    /// Gets the export name of a declaration for use in expressions.
    ///
    /// An export name will *always* be prefixed with a module or namespace export modifier like
    /// `"exports."` when emitted as an expression if the name points to an exported symbol.
    /// </summary>
    public Node GetExternalModuleOrNamespaceExportName(Node? ns, Node node, bool allowComments, bool allowSourceMaps)
    {
        if (ns is not null && HasSyntacticModifier(node, ModifierFlags.Export))
        {
            var nameOpts = new NameOptions { AllowComments = allowComments, AllowSourceMaps = allowSourceMaps };
            return GetNamespaceMemberName(ns, GetDeclarationNameEx(node, nameOpts), nameOpts);
        }
        return GetExportNameEx(node, new AssignedNameOptions { AllowComments = allowComments, AllowSourceMaps = allowSourceMaps });
    }

    //
    // Emit Helpers
    //

    /// <summary>Allocates a new Identifier representing a reference to a helper function.</summary>
    public Node NewUnscopedHelperName(string name)
    {
        var node = NewIdentifier(name);
        _emitContext.SetEmitFlags(node, EmitFlags.HelperName);
        return node;
    }

    // TypeScript Helpers

    public Node NewDecorateHelper(Node[] decoratorExpressions, Node target, Node? memberName, Node? descriptor)
    {
        _emitContext.RequestEmitHelper(Printer._decorateHelper);

        var argumentsArray = new List<Node>
        {
            NewArrayLiteralExpression(NewNodeList(decoratorExpressions), true),
            target,
        };
        if (memberName is not null)
        {
            argumentsArray.Add(memberName);
            if (descriptor is not null)
            {
                argumentsArray.Add(descriptor);
            }
        }

        return NewCallExpression(
            NewUnscopedHelperName("__decorate"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList(argumentsArray.ToArray()),
            NodeFlags.None);
    }

    public Node NewMetadataHelper(string metadataKey, Node metadataValue)
    {
        _emitContext.RequestEmitHelper(Printer._metadataHelper);

        return NewCallExpression(
            NewUnscopedHelperName("__metadata"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([
                NewStringLiteral(metadataKey, TokenFlags.None),
                metadataValue,
            ]),
            NodeFlags.None);
    }

    public Node NewParamHelper(Node expression, int parameterOffset, TextRange location)
    {
        _emitContext.RequestEmitHelper(Printer._paramHelper);
        var helper = NewCallExpression(
            NewUnscopedHelperName("__param"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([NewNumericLiteral(parameterOffset.ToString(CultureInfo.InvariantCulture), TokenFlags.None), expression]),
            NodeFlags.None);
        helper.Loc = location;
        return helper;
    }

    // ESNext Helpers

    public Node NewAddDisposableResourceHelper(Node envBinding, Node value, bool async)
    {
        _emitContext.RequestEmitHelper(Printer._addDisposableResourceHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__addDisposableResource"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([envBinding, value, NewKeywordExpression(IfElse(async, Kind.TrueKeyword, Kind.FalseKeyword))]),
            NodeFlags.None);
    }

    public Node NewDisposeResourcesHelper(Node envBinding)
    {
        _emitContext.RequestEmitHelper(Printer._disposeResourcesHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__disposeResources"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([envBinding]),
            NodeFlags.None);
    }

    // Class Fields Helpers

    /// <summary>Go: <c>type PrivateIdentifierKind string</c>。</summary>
    public readonly struct PrivateIdentifierKind
    {
        public readonly string Value;

        public PrivateIdentifierKind(string value)
        {
            Value = value;
        }

        public static implicit operator string(PrivateIdentifierKind kind) => kind.Value;

        public static readonly PrivateIdentifierKind Field = new("f");
        public static readonly PrivateIdentifierKind Method = new("m");
        public static readonly PrivateIdentifierKind Accessor = new("a");
        public static readonly PrivateIdentifierKind Untransformed = new("untransformed");
    }

    public Node NewClassPrivateFieldGetHelper(Node receiver, Node state, PrivateIdentifierKind kind, Node? fn)
    {
        _emitContext.RequestEmitHelper(Printer._classPrivateFieldGetHelper);
        Node[] args;
        if (fn is null)
        {
            args = [receiver, state, NewStringLiteral(kind.Value, TokenFlags.None)];
        }
        else
        {
            args = [receiver, state, NewStringLiteral(kind.Value, TokenFlags.None), fn];
        }
        return NewCallExpression(
            NewUnscopedHelperName("__classPrivateFieldGet"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList(args),
            NodeFlags.None);
    }

    public Node NewClassPrivateFieldSetHelper(Node receiver, Node state, Node value, PrivateIdentifierKind kind, Node? fn)
    {
        _emitContext.RequestEmitHelper(Printer._classPrivateFieldSetHelper);
        Node[] args;
        if (fn is null)
        {
            args = [receiver, state, value, NewStringLiteral(kind.Value, TokenFlags.None)];
        }
        else
        {
            args = [receiver, state, value, NewStringLiteral(kind.Value, TokenFlags.None), fn];
        }
        return NewCallExpression(
            NewUnscopedHelperName("__classPrivateFieldSet"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList(args),
            NodeFlags.None);
    }

    public Node NewClassPrivateFieldInHelper(Node state, Node receiver)
    {
        _emitContext.RequestEmitHelper(Printer._classPrivateFieldInHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__classPrivateFieldIn"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([state, receiver]),
            NodeFlags.None);
    }

    /// <summary>Creates `Object.defineProperty(target, name, descriptor)`.</summary>
    public Node NewObjectDefinePropertyCall(Node target, Node name, Node descriptor)
    {
        return NewCallExpression(
            NewPropertyAccessExpression(
                NewIdentifier("Object"),
                null,
                NewIdentifier("defineProperty"),
                NodeFlags.None),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([target, name, descriptor]),
            NodeFlags.None);
    }

    /// <summary>Creates `Reflect.get(target, propertyKey, receiver)`.</summary>
    public Node NewReflectGetCall(Node target, Node propertyKey, Node receiver)
    {
        return NewCallExpression(
            NewPropertyAccessExpression(
                NewIdentifier("Reflect"),
                null,
                NewIdentifier("get"),
                NodeFlags.None),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([target, propertyKey, receiver]),
            NodeFlags.None);
    }

    /// <summary>Creates `Reflect.set(target, propertyKey, value, receiver)`.</summary>
    public Node NewReflectSetCall(Node target, Node propertyKey, Node value, Node receiver)
    {
        return NewCallExpression(
            NewPropertyAccessExpression(
                NewIdentifier("Reflect"),
                null,
                NewIdentifier("set"),
                NodeFlags.None),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([target, propertyKey, value, receiver]),
            NodeFlags.None);
    }

    /// <summary>Creates `target.bind(thisArg, ...args)`.</summary>
    public Node NewFunctionBindCall(Node target, Node thisArg, Node[] argumentsList)
    {
        var args = new Node[argumentsList.Length + 1];
        args[0] = thisArg;
        Array.Copy(argumentsList, 0, args, 1, argumentsList.Length);
        return NewMethodCall(target, NewIdentifier("bind"), args);
    }

    /// <summary>Creates `(() => { ...statements })()` — an immediately invoked arrow function.</summary>
    public Node NewImmediatelyInvokedArrowFunction(Node[] statements)
    {
        var arrow = NewArrowFunction(
            null,                          /*modifiers*/
            null,                          /*typeParameters*/
            NewNodeList([]),               /*parameters*/
            null,                          /*returnType*/
            null,                          /*fullSignature*/
            NewToken(Kind.EqualsGreaterThanToken), /*equalsGreaterThanToken*/
            NewBlock(NewNodeList(statements), true));
        return NewCallExpression(
            NewParenthesizedExpression(arrow),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([]),
            NodeFlags.None);
    }

    /// <summary>Creates `export default &lt;expression&gt;;`.</summary>
    public Node NewExportDefault(Node expression)
    {
        return NewExportAssignment(null, false, null, expression);
    }

    /// <summary>Creates `export { &lt;name&gt; };`.</summary>
    public Node NewExternalModuleExport(Node name)
    {
        var specifier = NewExportSpecifier(false, null, name);
        var namedExports = NewNamedExports(NewNodeList([specifier]));
        return NewExportDeclaration(null, false, namedExports, null, null);
    }

    // ES2018 Helpers
    // Chains a sequence of expressions using the __assign helper or Object.assign if available in the target
    public Node NewAssignHelper(Node[] attributesSegments, ScriptTarget scriptTarget)
    {
        return NewCallExpression(NewPropertyAccessExpression(NewIdentifier("Object"), null, NewIdentifier("assign"), NodeFlags.None), null, null, NewNodeList(attributesSegments), NodeFlags.None);
    }

    // ES2018 Destructuring Helpers

    public Node NewRestHelper(Node value, Node[] elements, Node[]? computedTempVariables, TextRange location)
    {
        _emitContext.RequestEmitHelper(Printer._restHelper);
        var propertyNames = new List<Node>();
        var computedTempVariableOffset = 0;
        for (var i = 0; i < elements.Length; i++)
        {
            if (i == elements.Length - 1)
            {
                break;
            }
            var element = elements[i];
            var propertyName = TryGetPropertyNameOfBindingOrAssignmentElement(element);
            if (propertyName is not null)
            {
                if (IsComputedPropertyName(propertyName))
                {
                    Debug.Assert(computedTempVariables is not null, "Encountered computed property name but 'computedTempVariables' argument was not provided.");
                    var temp = computedTempVariables![computedTempVariableOffset];
                    computedTempVariableOffset++;
                    // typeof _tmp === "symbol" ? _tmp : _tmp + ""
                    propertyNames.Add(NewConditionalExpression(
                        NewTypeCheck(temp, "symbol"),
                        NewToken(Kind.QuestionToken),
                        temp,
                        NewToken(Kind.ColonToken),
                        NewBinaryExpression(null, temp, null, NewToken(Kind.PlusToken), NewStringLiteral("", TokenFlags.None))));
                }
                else
                {
                    propertyNames.Add(NewStringLiteralFromNode(propertyName));
                }
            }
        }
        var propNames = NewArrayLiteralExpression(NewNodeList(propertyNames.ToArray()), false);
        propNames.Loc = location;
        return NewCallExpression(
            NewUnscopedHelperName("__rest"),
            null,
            null,
            NewNodeList([
                value,
                propNames,
            ]),
            NodeFlags.None);
    }

    // ES2018 Helpers

    /// <summary>Allocates a new Call expression to the `__await` helper.</summary>
    public Node NewAwaitHelper(Node expression)
    {
        _emitContext.RequestEmitHelper(Printer._awaitHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__await"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([expression]),
            NodeFlags.None);
    }

    /// <summary>Allocates a new Call expression to the `__asyncGenerator` helper.</summary>
    public Node NewAsyncGeneratorHelper(Node generatorFunc, bool hasLexicalThis)
    {
        _emitContext.RequestEmitHelper(Printer._awaitHelper);
        _emitContext.RequestEmitHelper(Printer._asyncGeneratorHelper);

        // Mark this node as originally an async function body
        _emitContext.AddEmitFlags(generatorFunc, EmitFlags.AsyncFunctionBody | EmitFlags.ReuseTempVariableScope);

        Node thisArg;
        if (hasLexicalThis)
        {
            thisArg = NewKeywordExpression(Kind.ThisKeyword);
        }
        else
        {
            thisArg = NewVoidZeroExpression();
        }

        return NewCallExpression(
            NewUnscopedHelperName("__asyncGenerator"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([
                thisArg,
                NewIdentifier("arguments"),
                generatorFunc,
            ]),
            NodeFlags.None);
    }

    /// <summary>Allocates a new Call expression to the `__asyncDelegator` helper.</summary>
    public Node NewAsyncDelegatorHelper(Node expression)
    {
        _emitContext.RequestEmitHelper(Printer._awaitHelper);
        _emitContext.RequestEmitHelper(Printer._asyncDelegatorHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__asyncDelegator"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([expression]),
            NodeFlags.None);
    }

    /// <summary>Allocates a new Call expression to the `__asyncValues` helper.</summary>
    public Node NewAsyncValuesHelper(Node expression)
    {
        _emitContext.RequestEmitHelper(Printer._asyncValuesHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__asyncValues"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([expression]),
            NodeFlags.None);
    }

    // !!! ES2017 Helpers

    /// <summary>Allocates a new Call expression to the `__awaiter` helper.</summary>
    public Node NewAwaiterHelper(bool hasLexicalThis, Node? argumentsExpression, NodeList? parameters, Node body)
    {
        _emitContext.RequestEmitHelper(Printer._awaiterHelper);

        NodeList paramsList;
        if (parameters is not null)
        {
            paramsList = parameters;
        }
        else
        {
            paramsList = NewNodeList([]);
        }

        var generatorFunc = NewFunctionExpression(
            null, /*modifiers*/
            NewToken(Kind.AsteriskToken),
            null, /*name*/
            null, /*typeParameters*/
            paramsList,
            null, /*returnType*/
            null, /*fullSignature*/
            body);

        // Mark this node as originally an async function body
        _emitContext.AddEmitFlags(generatorFunc, EmitFlags.AsyncFunctionBody | EmitFlags.ReuseTempVariableScope);

        Node thisArg;
        if (hasLexicalThis)
        {
            thisArg = NewKeywordExpression(Kind.ThisKeyword);
        }
        else
        {
            thisArg = NewVoidZeroExpression();
        }

        Node argsArg;
        if (argumentsExpression is not null)
        {
            argsArg = argumentsExpression;
        }
        else
        {
            argsArg = NewVoidZeroExpression();
        }

        return NewCallExpression(
            NewUnscopedHelperName("__awaiter"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([
                thisArg,
                argsArg,
                NewVoidZeroExpression(),
                generatorFunc,
            ]),
            NodeFlags.None);
    }

    // ES Decorator Helpers

    public Node NewESDecorateClassContextObject(Node nameExpr, Node metadata)
    {
        Node[] props =
        [
            NewPropertyAssignment(null, NewIdentifier("kind"), null, null, NewStringLiteral("class", TokenFlags.None)),
            NewPropertyAssignment(null, NewIdentifier("name"), null, null, nameExpr),
            NewPropertyAssignment(null, NewIdentifier("metadata"), null, null, metadata),
        ];
        return NewObjectLiteralExpression(NewNodeList(props), false);
    }

    public Node NewESDecorateClassElementAccessGetMethod(bool nameComputed, Node? nameExpr)
    {
        Node accessor;
        if (nameComputed)
        {
            accessor = NewElementAccessExpression(NewIdentifier("obj"), null, nameExpr, NodeFlags.None);
        }
        else
        {
            accessor = NewPropertyAccessExpression(NewIdentifier("obj"), null, nameExpr, NodeFlags.None);
        }

        var objParam = NewParameterDeclaration(null, null, NewIdentifier("obj"), null, null, null);

        var arrow = NewArrowFunction(
            null, null,
            NewNodeList([objParam]),
            null, null,
            NewToken(Kind.EqualsGreaterThanToken),
            accessor);

        return NewPropertyAssignment(null, NewIdentifier("get"), null, null, arrow);
    }

    public Node NewESDecorateClassElementAccessSetMethod(bool nameComputed, Node? nameExpr)
    {
        Node accessor;
        if (nameComputed)
        {
            accessor = NewElementAccessExpression(NewIdentifier("obj"), null, nameExpr, NodeFlags.None);
        }
        else
        {
            accessor = NewPropertyAccessExpression(NewIdentifier("obj"), null, nameExpr, NodeFlags.None);
        }

        var assignment = NewAssignmentExpression(accessor, NewIdentifier("value"));
        var stmt = NewExpressionStatement(assignment);
        var body = NewBlock(NewNodeList([stmt]), false);

        var objParam = NewParameterDeclaration(null, null, NewIdentifier("obj"), null, null, null);
        var valueParam = NewParameterDeclaration(null, null, NewIdentifier("value"), null, null, null);

        var arrow = NewArrowFunction(
            null, null,
            NewNodeList([objParam, valueParam]),
            null, null,
            NewToken(Kind.EqualsGreaterThanToken),
            body);

        return NewPropertyAssignment(null, NewIdentifier("set"), null, null, arrow);
    }

    public Node NewESDecorateClassElementAccessHasMethod(bool nameComputed, Node? nameExpr)
    {
        // The property name for the "in" expression
        Node? propertyName;
        if (!nameComputed && nameExpr is not null && IsIdentifier(nameExpr))
        {
            propertyName = NewStringLiteralFromNode(nameExpr);
        }
        else
        {
            propertyName = nameExpr;
        }

        var objParam = NewParameterDeclaration(null, null, NewIdentifier("obj"), null, null, null);
        var inExpr = NewBinaryExpression(null, propertyName, null, NewToken(Kind.InKeyword), NewIdentifier("obj"));

        var arrow = NewArrowFunction(
            null, null,
            NewNodeList([objParam]),
            null, null,
            NewToken(Kind.EqualsGreaterThanToken),
            inExpr);

        return NewPropertyAssignment(null, NewIdentifier("has"), null, null, arrow);
    }

    /// <summary>
    /// Creates the "access" object for a class element decorator context.
    ///
    /// 15.7.3 CreateDecoratorAccessObject (kind, name)
    ///
    ///  2. If _kind_ is ~field~, ~method~, ~accessor~, or ~getter~, then
    ///     a. Let _getAccess_ be a new Abstract Closure with parameters (_object_) that captures _kind_ and _name_ ...
    ///     b. Perform ! CreateDataPropertyOrThrow(_access_, "get", _getAccess_).
    ///  3. If _kind_ is ~field~, ~accessor~, or ~setter~, then
    ///     a. Let _setAccess_ be a new Abstract Closure with parameters (_object_, _value_) that captures _kind_ and _name_ ...
    ///     b. Perform ! CreateDataPropertyOrThrow(_access_, "set", _setAccess_).
    /// </summary>
    public Node NewESDecorateClassElementAccessObject(bool nameComputed, Node? nameExpr, bool hasGet, bool hasSet)
    {
        var accessProps = new List<Node>();

        // "has" method: obj => name in obj
        accessProps.Add(NewESDecorateClassElementAccessHasMethod(nameComputed, nameExpr));

        // "get" method: obj => obj.name or obj => obj[name]
        if (hasGet)
        {
            accessProps.Add(NewESDecorateClassElementAccessGetMethod(nameComputed, nameExpr));
        }

        // "set" method: (obj, value) => { obj.name = value; } or (obj, value) => { obj[name] = value; }
        if (hasSet)
        {
            accessProps.Add(NewESDecorateClassElementAccessSetMethod(nameComputed, nameExpr));
        }

        return NewObjectLiteralExpression(NewNodeList(accessProps.ToArray()), false);
    }

    public Node NewESDecorateClassElementContextObject(
        string kind,
        bool nameComputed,
        Node nameExpr,
        bool isStatic,
        bool isPrivate,
        bool hasGet,
        bool hasSet,
        Node metadata)
    {
        // Build the name value for the context's "name" property
        Node? nameValue;
        if (!nameComputed && nameExpr is not null && (IsPrivateIdentifier(nameExpr) || IsIdentifier(nameExpr)))
        {
            nameValue = NewStringLiteralFromNode(nameExpr);
        }
        else
        {
            nameValue = nameExpr;
        }

        // Build the access object with has/get/set arrow functions
        var accessObj = NewESDecorateClassElementAccessObject(nameComputed, nameExpr, hasGet, hasSet);

        Node staticExpr;
        if (isStatic)
        {
            staticExpr = NewTrueExpression();
        }
        else
        {
            staticExpr = NewFalseExpression();
        }

        Node privateExpr;
        if (isPrivate)
        {
            privateExpr = NewTrueExpression();
        }
        else
        {
            privateExpr = NewFalseExpression();
        }

        Node[] props =
        [
            NewPropertyAssignment(null, NewIdentifier("kind"), null, null, NewStringLiteral(kind, TokenFlags.None)),
            NewPropertyAssignment(null, NewIdentifier("name"), null, null, nameValue),
            NewPropertyAssignment(null, NewIdentifier("static"), null, null, staticExpr),
            NewPropertyAssignment(null, NewIdentifier("private"), null, null, privateExpr),
            NewPropertyAssignment(null, NewIdentifier("access"), null, null, accessObj),
            NewPropertyAssignment(null, NewIdentifier("metadata"), null, null, metadata),
        ];
        return NewObjectLiteralExpression(NewNodeList(props), false);
    }

    public Node NewESDecorateHelper(Node ctor, Node descriptorIn, Node decorators, Node contextIn, Node initializers, Node extraInitializers)
    {
        _emitContext.RequestEmitHelper(Printer._esDecorateHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__esDecorate"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([ctor, descriptorIn, decorators, contextIn, initializers, extraInitializers]),
            NodeFlags.None);
    }

    public Node NewRunInitializersHelper(Node thisArg, Node initializers, Node? value)
    {
        _emitContext.RequestEmitHelper(Printer._runInitializersHelper);
        Node[] arguments;
        if (value is not null)
        {
            arguments = [thisArg, initializers, value];
        }
        else
        {
            arguments = [thisArg, initializers];
        }
        return NewCallExpression(
            NewUnscopedHelperName("__runInitializers"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList(arguments),
            NodeFlags.None);
    }

    // ES2015 Helpers

    public Node NewTemplateObjectHelper(Node cookedArray, Node rawArray)
    {
        _emitContext.RequestEmitHelper(Printer._makeTemplateObjectHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__makeTemplateObject"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([cookedArray, rawArray]),
            NodeFlags.None);
    }

    public Node NewPropKeyHelper(Node expr)
    {
        _emitContext.RequestEmitHelper(Printer._propKeyHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__propKey"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([expr]),
            NodeFlags.None);
    }

    public Node NewSetFunctionNameHelper(Node fn, Node name, string prefix)
    {
        _emitContext.RequestEmitHelper(Printer._setFunctionNameHelper);
        Node[] arguments;
        if (prefix.Length > 0)
        {
            arguments = [fn, name, NewStringLiteral(prefix, TokenFlags.None)];
        }
        else
        {
            arguments = [fn, name];
        }
        return NewCallExpression(
            NewUnscopedHelperName("__setFunctionName"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList(arguments),
            NodeFlags.None);
    }

    // ES Module Helpers

    /// <summary>Allocates a new Call expression to the `__importDefault` helper.</summary>
    public Node NewImportDefaultHelper(Node expression)
    {
        _emitContext.RequestEmitHelper(Printer._importDefaultHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__importDefault"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([expression]),
            NodeFlags.None);
    }

    /// <summary>Allocates a new Call expression to the `__importStar` helper.</summary>
    public Node NewImportStarHelper(Node expression)
    {
        _emitContext.RequestEmitHelper(Printer._importStarHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__importStar"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([expression]),
            NodeFlags.None);
    }

    /// <summary>Allocates a new Call expression to the `__exportStar` helper.</summary>
    public Node NewExportStarHelper(Node moduleExpression, Node exportsExpression)
    {
        _emitContext.RequestEmitHelper(Printer._exportStarHelper);
        return NewCallExpression(
            NewUnscopedHelperName("__exportStar"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList([moduleExpression, exportsExpression]),
            NodeFlags.None);
    }

    public Node NewAssignmentTargetWrapper(Node paramName, Node expression)
    {
        var setAccessor = NewSetAccessorDeclaration(
            null, /*modifiers*/
            NewIdentifier("value"),
            null, /*typeParameters*/
            NewNodeList([
                NewParameterDeclaration(null, null, paramName, null, null, null),
            ]),
            null, /*returnType*/
            null, /*fullSignature*/
            NewBlock(NewNodeList([
                NewExpressionStatement(expression),
            ]), false));
        var objLiteral = NewObjectLiteralExpression(NewNodeList([setAccessor]), false);
        // Explicit parens required because of v8 regression (https://bugs.chromium.org/p/v8/issues/detail?id=9560)
        return NewPropertyAccessExpression(
            NewParenthesizedExpression(objLiteral),
            null, /*questionDotToken*/
            NewIdentifier("value"),
            NodeFlags.None);
    }

    /// <summary>Allocates a new Call expression to the `__rewriteRelativeImportExtension` helper.</summary>
    public Node NewRewriteRelativeImportExtensionsHelper(Node firstArgument, bool preserveJsx)
    {
        _emitContext.RequestEmitHelper(Printer._rewriteRelativeImportExtensionsHelper);
        Node[] arguments;
        if (preserveJsx)
        {
            arguments = [firstArgument, NewToken(Kind.TrueKeyword)];
        }
        else
        {
            arguments = [firstArgument];
        }
        return NewCallExpression(
            NewUnscopedHelperName("__rewriteRelativeImportExtension"),
            null, /*questionDotToken*/
            null, /*typeArguments*/
            NewNodeList(arguments),
            NodeFlags.None);
    }
}

public sealed partial class Printer
{
    public static NodeFactory NewNodeFactory(EmitContext context) => new(context);
}
