// ported from: internal/transformers/estransforms/utilities.go（289 行，全部：
//               convertClassDeclarationToClassExpression、createNotNullCondition、superAccessState +
//               initSuperAccessVisitor/visitSuperAccessNode/substituteSuperAccessesInBody/
//               substituteCallExpressionWithSuperAccess/createSuperElementAccessInAsyncMethod/
//               createSuperAccessVariableStatement/trackSuperAccess、createAccessorPropertyBackingField）
//
// 与 Go 的差异（逐条）：
// 1) Go 的 `type superAccessState struct`（被 asyncTransformer/forawaitTransformer **嵌入**）→ C# 的
//    `internal sealed class SuperAccessState`（C# 无结构体嵌入）：两个变换器各持有一个 `_super` 字段并转发调用。
//    未导出字段按 docs/architecture.md §2 命名成 `_camelCase` + `internal`（async.go/forawait.go 是**另一个类**，
//    要读写这些字段）；只在本类内使用的 `factory` → `private _factory`。
// 2) Go 的 `func (s *superAccessState) xxx()` → PascalCase 实例方法（`InitSuperAccessVisitor` 等）；
//    `visitSuperAccessNode` 的签名（`func(*ast.Node) *ast.Node`）与 Ast 的具名委托 `NodeVisitorVisit`
//    （`Node? (Node)`）一致，所以 `NewNodeVisitor(VisitSuperAccessNode)` 直接传方法组。
// 3) 文件内**声明顺序**与 Go 不同：Go 允许包级类型夹在函数中间，C# 的 partial class 成员必须成块 →
//    三个包级函数（`ConvertClassDeclarationToClassExpression`/`CreateNotNullCondition`/
//    `CreateAccessorPropertyBackingField`）放进 `EsTransforms`，`SuperAccessState` 类放其后。
//    三者的可见性是 `internal static`（Go 未导出；跨类调用者是独立类）。
// 4) Go 的 `for name := range s.capturedSuperProperties.Values()` → `foreach`（`Collections.OrderedSet<T>.Values()`
//    返回 `IEnumerable<T>`）。
// 5) Go 的 `append([]*ast.Node{...}, ...)` 这类构造 → C# 集合表达式/`List<Node>`（docs/architecture.md §3.2）。
// 6) Go 的 `*ast.IdentifierNode`/`*ast.Expression`/`*ast.Statement` 在 C# 里都是 `Node`（无类型别名，docs/ast.md §2）。
// 7) `NodeFactory` 用别名消歧：Ast 与 Printer 各有一个同名类型（docs/architecture.md §2 命名遮蔽）；Go 的
//    `*printer.NodeFactory` 就是 `TypeScript.Printer.NodeFactory`。
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using static TypeScript.Transformers.Transformers;
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.EsTransforms;

public static partial class EsTransforms
{
    /// <summary>Go: <c>func convertClassDeclarationToClassExpression(emitContext *printer.EmitContext, node *ast.ClassDeclaration) *ast.Expression</c>。</summary>
    internal static Node ConvertClassDeclarationToClassExpression(EmitContext emitContext, ClassDeclaration node)
    {
        var updated = emitContext.Factory.NewClassExpression(
            ExtractModifiers(emitContext, node.Modifiers(), ~ModifierFlags.ExportDefault),
            node.Name(),
            node.TypeParameters,
            node.HeritageClauses,
            node.Members);
        emitContext.SetOriginal(updated, node);
        updated.Loc = node.Loc;
        return updated;
    }

    /// <summary>Go: <c>func createNotNullCondition(emitContext *printer.EmitContext, left *ast.Node, right *ast.Node, invert bool) *ast.Node</c>。</summary>
    internal static Node CreateNotNullCondition(EmitContext emitContext, Node left, Node right, bool invert)
    {
        var token = Kind.ExclamationEqualsEqualsToken;
        var op = Kind.AmpersandAmpersandToken;
        if (invert)
        {
            token = Kind.EqualsEqualsEqualsToken;
            op = Kind.BarBarToken;
        }

        return emitContext.Factory.NewBinaryExpression(
            null,
            emitContext.Factory.NewBinaryExpression(
                null,
                left,
                null,
                emitContext.Factory.NewToken(token),
                emitContext.Factory.NewKeywordExpression(Kind.NullKeyword)),
            null,
            emitContext.Factory.NewToken(op),
            emitContext.Factory.NewBinaryExpression(
                null,
                right,
                null,
                emitContext.Factory.NewToken(token),
                emitContext.Factory.NewVoidZeroExpression()));
    }

    /// <summary>Go: <c>func createAccessorPropertyBackingField(f *printer.NodeFactory, node *ast.PropertyDeclaration, modifiers *ast.ModifierList, initializer *ast.Expression) *ast.Node</c>。</summary>
    internal static Node CreateAccessorPropertyBackingField(NodeFactory f, PropertyDeclaration node, ModifierList? modifiers, Node? initializer)
    {
        return f.UpdatePropertyDeclaration(
            node,
            modifiers,
            f.NewGeneratedPrivateNameForNodeEx(node.Name()!, new AutoGenerateOptions { Suffix = "_accessor_storage" }),
            null, /*postfixToken*/
            null, /*typeNode*/
            initializer);
    }
}

/// <summary>
/// Go: <c>type superAccessState struct</c>：tracks super property/element accesses and super property assignments
/// within async function or async generator bodies. It is embedded by both asyncTransformer
/// and forawaitTransformer to share the tracking logic.
/// </summary>
internal sealed class SuperAccessState
{
    private NodeFactory _factory = null!;

    /// <summary>Keeps track of property names accessed on super (`super.x`) within async functions.</summary>
    internal OrderedSet<string>? _capturedSuperProperties;

    /// <summary>Whether the async function contains an element access on super (`super[x]`).</summary>
    internal bool _hasSuperElementAccess;

    internal bool _hasSuperPropertyAssignment;

    // Go: superBinding/superIndexBinding 的类型是 *ast.IdentifierNode，而 Go 的 `IdentifierNode` 是 `= Node` 的
    // union 别名（ast_generated.go 的别名块）→ C# 用 `Node?`（docs/ast.md §2：别名不生成类型）。
    internal Node? _superBinding;
    internal Node? _superIndexBinding;
    internal NodeVisitor? _superAccessVisitor;

    /// <summary>Go: <c>func (s *superAccessState) initSuperAccessVisitor(emitContext *printer.EmitContext, factory *printer.NodeFactory)</c>。</summary>
    internal void InitSuperAccessVisitor(EmitContext emitContext, NodeFactory factory)
    {
        _factory = factory;
        _superAccessVisitor = emitContext.NewNodeVisitor(VisitSuperAccessNode);
    }

    /// <summary>
    /// Go: <c>func (s *superAccessState) visitSuperAccessNode(node *ast.Node) *ast.Node</c>。
    /// Walks the async/generator body and replaces super property/element accesses with _super/_superIndex
    /// references. This is necessary because the async body ends up inside a generator function where
    /// `super` is not valid.
    /// </summary>
    internal Node? VisitSuperAccessNode(Node node)
    {
        switch (node.Kind)
        {
            case Kind.CallExpression:
            {
                var call = node.AsCallExpression()!;
                if (IsSuperProperty(call.Expression))
                {
                    return SubstituteCallExpressionWithSuperAccess(call, _superAccessVisitor!);
                }

                return _superAccessVisitor!.VisitEachChild(node);
            }

            case Kind.PropertyAccessExpression:
                if (node.Expression()!.Kind == Kind.SuperKeyword)
                {
                    // super.x → _super.x
                    return _factory.NewPropertyAccessExpression(
                        _superBinding, null, node.Name(), NodeFlags.None);
                }

                return _superAccessVisitor!.VisitEachChild(node);

            case Kind.ElementAccessExpression:
                if (node.Expression()!.Kind == Kind.SuperKeyword)
                {
                    // super[x] → _superIndex(x) or _superIndex(x).value
                    return CreateSuperElementAccessInAsyncMethod(
                        node.AsElementAccessExpression()!.ArgumentExpression!);
                }

                return _superAccessVisitor!.VisitEachChild(node);

            // Don't recurse into non-arrow function scopes or classes
            case Kind.FunctionExpression:
            case Kind.FunctionDeclaration:
            case Kind.MethodDeclaration:
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.Constructor:
            case Kind.ClassDeclaration:
            case Kind.ClassExpression:
                return node;

            default:
                return _superAccessVisitor!.VisitEachChild(node);
        }
    }

    /// <summary>Go: <c>func (s *superAccessState) substituteSuperAccessesInBody(body *ast.Node) *ast.Node</c>。</summary>
    internal Node? SubstituteSuperAccessesInBody(Node body)
    {
        return _superAccessVisitor!.VisitNode(body);
    }

    /// <summary>Go: <c>func (s *superAccessState) substituteCallExpressionWithSuperAccess(call *ast.CallExpression, visitor *ast.NodeVisitor) *ast.Node</c>：handles super.x(args) and super[x](args).</summary>
    internal Node? SubstituteCallExpressionWithSuperAccess(CallExpression call, NodeVisitor visitor)
    {
        var expression = call.Expression;
        Node? target;

        if (IsPropertyAccessExpression(expression!))
        {
            // super.x(args) → _super.x.call(this, args)
            target = _factory.NewPropertyAccessExpression(
                _superBinding, null,
                expression!.AsPropertyAccessExpression()!.Name(), NodeFlags.None);
        }
        else if (IsElementAccessExpression(expression!))
        {
            // super[x](args) → _superIndex(x).call(this, args) or _superIndex(x).value.call(this, args)
            target = CreateSuperElementAccessInAsyncMethod(
                expression!.AsElementAccessExpression()!.ArgumentExpression!);
        }
        else
        {
            return visitor.VisitEachChild(call);
        }

        var callTarget = _factory.NewPropertyAccessExpression(
            target, null,
            _factory.NewIdentifier("call"), NodeFlags.None);

        var allArgs = new List<Node> { _factory.NewThisExpression() };
        if (call.Arguments is not null)
        {
            var visitedArgs = visitor.VisitNodes(call.Arguments);
            if (visitedArgs is not null)
            {
                allArgs.AddRange(visitedArgs.Nodes ?? []);
            }
        }

        var result = _factory.NewCallExpression(
            callTarget, null, null,
            _factory.NewNodeList(allArgs.ToArray()), NodeFlags.None);
        result.Loc = call.Loc;
        return result;
    }

    /// <summary>Go: <c>func (s *superAccessState) createSuperElementAccessInAsyncMethod(argumentExpression *ast.Node) *ast.Node</c>：creates _superIndex(x) or _superIndex(x).value.</summary>
    internal Node? CreateSuperElementAccessInAsyncMethod(Node argumentExpression)
    {
        var superIndexCall = _factory.NewCallExpression(
            _superIndexBinding, null, null,
            _factory.NewNodeList([argumentExpression]),
            NodeFlags.None);
        if (_hasSuperPropertyAssignment)
        {
            return _factory.NewPropertyAccessExpression(
                superIndexCall, null,
                _factory.NewIdentifier("value"), NodeFlags.None);
        }

        return superIndexCall;
    }

    /// <summary>
    /// Go: <c>func (s *superAccessState) createSuperAccessVariableStatement() *ast.Node</c>。
    /// Creates a variable named `_super` with accessor properties for the given property names:
    /// <code>
    /// const _super = Object.create(null, {
    ///     x: { get: () => super.x },                           // read-only
    ///     x: { get: () => super.x, set: (v) => super.x = v }, // read-write
    /// });
    /// </code>
    /// </summary>
    internal Node? CreateSuperAccessVariableStatement()
    {
        var f = _factory;
        var accessors = new List<Node>();

        foreach (var name in _capturedSuperProperties!.Values())
        {
            var descriptorProperties = new List<Node>();

            // getter: get: () => super.name
            var getterBody = f.NewPropertyAccessExpression(
                f.NewKeywordExpression(Kind.SuperKeyword), null,
                f.NewIdentifier(name), NodeFlags.None);
            var getterArrow = f.NewArrowFunction(
                null, null,
                f.NewNodeList([]),
                null, null,
                f.NewToken(Kind.EqualsGreaterThanToken),
                getterBody);
            var getter = f.NewPropertyAssignment(null, f.NewIdentifier("get"), null, null, getterArrow);
            descriptorProperties.Add(getter);

            if (_hasSuperPropertyAssignment)
            {
                // setter: set: v => super.name = v
                var vParam = f.NewParameterDeclaration(null, null, f.NewIdentifier("v"), null, null, null);
                var superProp = f.NewPropertyAccessExpression(
                    f.NewKeywordExpression(Kind.SuperKeyword), null,
                    f.NewIdentifier(name), NodeFlags.None);
                var assignExpr = f.NewAssignmentExpression(superProp, f.NewIdentifier("v"));
                var setterArrow = f.NewArrowFunction(
                    null, null,
                    f.NewNodeList([vParam]),
                    null, null,
                    f.NewToken(Kind.EqualsGreaterThanToken),
                    assignExpr);
                var setter = f.NewPropertyAssignment(null, f.NewIdentifier("set"), null, null, setterArrow);
                descriptorProperties.Add(setter);
            }

            var descriptor = f.NewObjectLiteralExpression(f.NewNodeList(descriptorProperties.ToArray()), false);
            var accessor = f.NewPropertyAssignment(null, f.NewIdentifier(name), null, null, descriptor);
            accessors.Add(accessor);
        }

        var descriptorsObject = f.NewObjectLiteralExpression(f.NewNodeList(accessors.ToArray()), true);

        var objectCreateCall = f.NewCallExpression(
            f.NewPropertyAccessExpression(
                f.NewIdentifier("Object"), null,
                f.NewIdentifier("create"), NodeFlags.None), null, null,
            f.NewNodeList([
                f.NewKeywordExpression(Kind.NullKeyword),
                descriptorsObject,
            ]),
            NodeFlags.None);

        var decl = f.NewVariableDeclaration(_superBinding, null, null, objectCreateCall);
        var declList = f.NewVariableDeclarationList(f.NewNodeList([decl]), NodeFlags.Const);
        return f.NewVariableStatement(null, declList);
    }

    /// <summary>
    /// Go: <c>func (s *superAccessState) trackSuperAccess(node *ast.Node)</c>：records super property/element accesses and
    /// super property assignments for the enclosing async method body. Called from both the main visitor and auxiliary
    /// visitors to ensure super accesses are tracked regardless of whether the node has transform flags.
    /// </summary>
    internal void TrackSuperAccess(Node node)
    {
        if (_capturedSuperProperties is null)
        {
            return;
        }

        switch (node.Kind)
        {
            case Kind.PropertyAccessExpression:
                if (node.Expression()!.Kind == Kind.SuperKeyword)
                {
                    _capturedSuperProperties.Add(node.Name()!.Text());
                }

                break;
            case Kind.ElementAccessExpression:
                if (node.Expression()!.Kind == Kind.SuperKeyword)
                {
                    _hasSuperElementAccess = true;
                }

                break;
            case Kind.BinaryExpression:
                if (IsAssignmentOperator(node.AsBinaryExpression()!.OperatorToken!.Kind) && AssignmentTargetContainsSuperProperty(node.AsBinaryExpression()!.Left!))
                {
                    _hasSuperPropertyAssignment = true;
                }

                break;
            case Kind.PrefixUnaryExpression:
                if (IsUpdateExpression(node) && AssignmentTargetContainsSuperProperty(node.AsPrefixUnaryExpression()!.Operand!))
                {
                    _hasSuperPropertyAssignment = true;
                }

                break;
            case Kind.PostfixUnaryExpression:
                if (IsUpdateExpression(node) && AssignmentTargetContainsSuperProperty(node.AsPostfixUnaryExpression()!.Operand!))
                {
                    _hasSuperPropertyAssignment = true;
                }

                break;
        }
    }
}
