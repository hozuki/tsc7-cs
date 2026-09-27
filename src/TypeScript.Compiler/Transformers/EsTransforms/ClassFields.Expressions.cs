// ported from: internal/transformers/estransforms/classfields.go:895-1685（私有/公有字段初始化器、私有标识符访问与赋值、属性与元素访问、前后缀自增自减、for/表达式语句、调用/标签模板、类静态块、二元/括号表达式）
//
// Go→C# 语义改写（本分片）：
// 1) `printer.PrivateIdentifierKind` 在 C# 侧是 NodeFactory 的**嵌套类型**（Printer/NodeFactory.cs:833）→ 文件顶部加 alias；
//    该 struct 没有 `==`，且 `static readonly` 字段不能作为 `case` 常量模式，所以 Go 的 `switch info.kind { … }`
//    一律改写成 if/else 链 + P1 的 `IsPrivateKind(a, b)` 助手。
// 2) `debug.Fail(...)` / `debug.AssertNever(...)` → 根命名空间的 `Debug.Fail(...)` / `Debug.AssertNever(...)`
//    （禁止 `using System.Diagnostics;`，否则会绑到 BCL 的 Debug）；`Debug.Fail` 不是 `[DoesNotReturn]`，其后补 Go 原有的 `return nil`。
// 3) `createCopiableReceiverExpr` 的 Go 多返回值 `(readExpression, initializeExpression)` → C# 具名元组 `(Node?, Node?)`，
//    调用点用 `var (a, b) = …` 解构（与 Go 的 `a, b := …` 一一对应）。
// 4) `visitBinaryExpression` 里对 `tx.pendingExpressions` 的"保存 → 置 nil → append → 恢复"：C# 用 `List<Node>`，
//    先复制成新 List 再 Add（实例 Add 会就地改动 count，直接 Add 会污染 `savedPendingExpressions`；Go 的 append 因 len<cap 不改变旧切片视图）。
// 5) `*ast.Expression` / `*ast.Statement` / `*ast.IdentifierNode` / `*ast.PropertyName` 等 Go 别名 → C# 一律 `Node`
//    （docs/ast.md §2：Kind 别名不生成）；`*ast.PropertyDeclaration` / `*ast.BinaryExpression` 等具体节点类保留具体 C# 类型。
// 6) `printer.EFXxx` → `EmitFlags.Xxx`；`ast.OEKXxx` → `OuterExpressionKinds.Xxx`；`ast.KindXxx` → `Kind.Xxx`；
//    `ast.NodeFlagsNone` → `NodeFlags.None`；`ast.NodeFlagsOptionalChain` → `NodeFlags.OptionalChain`。
// 7) Go 的 nil 切片实参（`append(x, nilSlice...)`、`NewFunctionBindCall(…, nil)`）→ C# `?? []`（长度 0，与 Go 的 nil 切片等义）。
// 8) 包级函数 `flattenCommaList` / `expandPreOrPostfixIncrementOrDecrementExpression` / `isStaticPropertyDeclarationOrClassStaticBlock`
//    由 P5 分片落在 `EsTransforms` 静态类 → 经 `using static` 裸名调用（本文件不定义）。
// 9) `transformers.IsSimpleInlineableExpression` / `transformers.GetNonAssignmentOperatorForCompoundAssignment` → T1 的
//    `Transformers` 静态类成员，裸名调用。
// 10) Go 的 `visitParenthesizedExpression` 在两个分支里各自短声明 `expression`；C# 禁止内层块与外层后续局部同名（CS0136），
//     故外层改名为 `visitedExpression`（唯一一处变量改名，语句顺序与其他名称保持不变）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Transformers.Transformers;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
// Go 的 printer.PrivateIdentifierKind 在 C# 侧是 `TypeScript.Printer` 的命名空间级类型（Printer/NodeFactory.cs 末尾）。
using PrivateIdentifierKind = TypeScript.Printer.PrivateIdentifierKind;
// 消歧：Ast 与 Printer 各有一个 NodeFactory（Go 的 ast.NodeFactory / printer.NodeFactory）。
using NodeFactory = TypeScript.Printer.NodeFactory;

namespace TypeScript.Transformers.EsTransforms;

public sealed partial class ClassFieldsTransformer : Transformer
{
    /// <summary>Go: <c>transformPrivateFieldInitializer</c>(classfields.go:895-947)。</summary>
    private Node? TransformPrivateFieldInitializer(PropertyDeclaration node)
    {
        if (ShouldTransformClassElementToWeakMap(node.AsNode()))
        {
            // If we are transforming private elements into WeakMap/WeakSet, we should elide the node.
            var info = AccessPrivateIdentifier(node.Name()!);
            Debug.Assert(info != null, "Undeclared private name for property declaration.");

            // Leave invalid code untransformed
            if (!info!._isValid)
            {
                return node.AsNode();
            }

            // If we encounter a valid private static field and we're not transforming
            // class static blocks, convert to a static block initializer.
            if (info._isStatic && !_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                // TODO: fix
                var statement = TransformPropertyOrClassStaticBlock(node.AsNode(), Factory().NewThisExpression());
                if (statement != null)
                {
                    return Factory().NewClassStaticBlockDeclaration(
                        null, /*modifiers*/
                        Factory().NewBlock(Factory().NewNodeList([statement]), true /*multiLine*/));
                }
            }

            return null;
        }

        if (_shouldTransformInitializersUsingSet && !HasStaticModifier(node.AsNode()) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null &&
            (_lexicalEnvironment._data._facts & ClassFacts.WillHoistInitializersToConstructor) != 0)
        {
            return Factory().UpdatePropertyDeclaration(
                node,
                Visitor().VisitModifiers(node.Modifiers()),
                node.Name(),
                null, /*postfixToken*/
                null, /*typeNode*/
                null /*initializer*/);
        }

        if (IsNamedEvaluationAnd(EmitContext(), node.AsNode(), _isAnonymousClassNeedingAssignedName!))
        {
            node = TransformNamedEvaluation(EmitContext(), node.AsNode(), false, "").AsPropertyDeclaration()!;
        }

        return Factory().UpdatePropertyDeclaration(
            node,
            _modifierVisitor.VisitModifiers(node.Modifiers()),
            VisitPropertyName(node.Name()),
            null, /*postfixToken*/
            null, /*typeNode*/
            Visitor().VisitNode(node.Initializer));
    }

    /// <summary>Go: <c>transformPublicFieldInitializer</c>(classfields.go:949-991)。</summary>
    private Node? TransformPublicFieldInitializer(PropertyDeclaration node)
    {
        if (_shouldTransformInitializers && !IsAutoAccessorPropertyDeclaration(node.AsNode()))
        {
            // Elide the property declaration; the initializer will be moved to the constructor.
            // For computed property names, we still need to emit the expression.
            var expr = GetPropertyNameExpressionIfNeeded(node.Name()!, node.Initializer != null || _compilerOptions.GetUseDefineForClassFields());
            if (expr != null)
            {
                foreach (var e in FlattenCommaList(expr))
                {
                    AddPendingExpressions(e);
                }
            }

            // When target >= ES2022 (i.e., !shouldTransformPrivateElementsOrClassStaticBlocks) and we
            // still need to transform initializers (useDefineForClassFields: false), static property
            // initializers must be converted into `static { this.x = ...; }` blocks so that `this`
            // refers to the class constructor inside the static block.
            if (IsStatic(node.AsNode()) && !_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                var initializerStatement = TransformPropertyOrClassStaticBlock(node.AsNode(), Factory().NewThisExpression());
                if (initializerStatement != null)
                {
                    var staticBlock = Factory().NewClassStaticBlockDeclaration(
                        null, /*modifiers*/
                        Factory().NewBlock(Factory().NewNodeList([initializerStatement]), false));

                    EmitContext().SetOriginal(staticBlock, node.AsNode());
                    EmitContext().SetCommentRange(staticBlock, node.Loc);

                    EmitContext().AddEmitFlags(initializerStatement, EmitFlags.NoComments);
                    return staticBlock;
                }
            }

            return null;
        }

        return Factory().UpdatePropertyDeclaration(
            node,
            _modifierVisitor.VisitModifiers(node.Modifiers()),
            VisitPropertyName(node.Name()),
            null, /*postfixToken*/
            null, /*typeNode*/
            Visitor().VisitNode(node.Initializer));
    }

    /// <summary>Go: <c>transformFieldInitializer</c>(classfields.go:993-999)。</summary>
    private Node? TransformFieldInitializer(PropertyDeclaration node)
    {
        Debug.Assert(!HasDecorators(node.AsNode()), "Decorators should already have been transformed and elided.");
        if (IsPrivateIdentifierClassElementDeclaration(node.AsNode()))
        {
            return TransformPrivateFieldInitializer(node);
        }
        return TransformPublicFieldInitializer(node);
    }

    /// <summary>Go: <c>shouldTransformAutoAccessorsInCurrentClass</c>(classfields.go:1001-1009)。</summary>
    private bool ShouldTransformAutoAccessorsInCurrentClass()
    {
        if (_shouldTransformAutoAccessors)
        {
            return true;
        }
        // When targeting ESNext with useDefineForClassFields: false, auto-accessors are only
        // transformed if the current class will hoist initializers to the constructor.
        return _lexicalEnvironment != null && _lexicalEnvironment._data != null &&
            (_lexicalEnvironment._data._facts & ClassFacts.WillHoistInitializersToConstructor) != 0;
    }

    /// <summary>Go: <c>visitPropertyDeclaration</c>(classfields.go:1011-1020)。</summary>
    private Node? VisitPropertyDeclaration(Node node)
    {
        // If this is an auto-accessor, we defer to `transformAutoAccessor`. That function
        // will in turn call `transformFieldInitializer` as needed.
        var propDecl = node.AsPropertyDeclaration()!;
        if (IsAutoAccessorPropertyDeclaration(node) && (ShouldTransformAutoAccessorsInCurrentClass() ||
            HasStaticModifier(node) && ShouldAlwaysTransformPrivateStaticElements(node)))
        {
            return TransformAutoAccessor(propDecl);
        }
        return TransformFieldInitializer(propDecl);
    }

    /// <summary>Go: <c>createPrivateIdentifierAccess</c>(classfields.go:1022-1025)。</summary>
    private Node? CreatePrivateIdentifierAccess(PrivateIdentifierInfo info, Node receiver)
    {
        receiver = Visitor().VisitNode(receiver)!;
        return CreatePrivateIdentifierAccessHelper(info, receiver);
    }

    /// <summary>Go: <c>createPrivateIdentifierAccessHelper</c>(classfields.go:1027-1062)。</summary>
    private Node? CreatePrivateIdentifierAccessHelper(PrivateIdentifierInfo info, Node receiver)
    {
        EmitContext().SetCommentRange(receiver, NewTextRange(-1, receiver.End()));

        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Accessor))
        {
            return Factory().NewClassPrivateFieldGetHelper(
                receiver,
                info._brandCheckIdentifier!,
                info._kind,
                info._getterName);
        }
        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Method))
        {
            return Factory().NewClassPrivateFieldGetHelper(
                receiver,
                info._brandCheckIdentifier!,
                info._kind,
                info._methodName);
        }
        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Field))
        {
            Node? f = null;
            if (info._isStatic)
            {
                f = info._variableName;
            }
            return Factory().NewClassPrivateFieldGetHelper(
                receiver,
                info._brandCheckIdentifier!,
                info._kind,
                f);
        }
        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Untransformed))
        {
            Debug.Fail("Access helpers should not be created for untransformed private elements");
            return null;
        }
        Debug.AssertNever(info, "Unknown private element type");
        return null;
    }

    /// <summary>Go: <c>visitPropertyAccessExpression</c>(classfields.go:1064-1106)。</summary>
    private Node? VisitPropertyAccessExpression(PropertyAccessExpression node)
    {
        if (IsPrivateIdentifier(node.Name()!))
        {
            var info = AccessPrivateIdentifier(node.Name()!);
            if (info != null)
            {
                var result = CreatePrivateIdentifierAccess(info, node.Expression!)!;
                EmitContext().SetOriginal(result, node.AsNode());
                result.Loc = node.Loc;
                return result;
            }
        }
        if (_shouldTransformSuperInStaticInitializers && _currentClassElement != null &&
            IsSuperProperty(node.AsNode()) && IsIdentifier(node.Name()!) &&
            IsStaticPropertyDeclarationOrClassStaticBlock(_currentClassElement) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null)
        {
            var data = _lexicalEnvironment._data;
            if ((data._facts & ClassFacts.ClassWasDecorated) != 0)
            {
                return VisitInvalidSuperProperty(node.AsNode());
            }
            if (data._classConstructor != null && data._superClassReference != null)
            {
                // converts `super.x` into `Reflect.get(_baseTemp, "x", _classTemp)`
                var superProperty = Factory().NewReflectGetCall(
                    data._superClassReference,
                    Factory().NewStringLiteralFromNode(node.Name()!),
                    data._classConstructor);
                EmitContext().SetOriginal(superProperty, node.Expression);
                superProperty.Loc = node.Expression!.Loc;
                return superProperty;
            }
        }
        // Visit only the expression, not the name (when it's a regular identifier), to prevent
        // substitution of property names. Strada's onSubstituteNode only fires for
        // EmitHint.Expression, which excludes the .name of PropertyAccessExpression.
        // Private identifier names are still visited through VisitEachChild so they can be
        // transformed by visitPrivateIdentifier.
        if (IsIdentifier(node.Name()!))
        {
            return VisitPropertyAccessExpressionForSubstitution(node);
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>
    /// Go: <c>visitPropertyAccessExpressionForSubstitution</c>(classfields.go:1108-1114)。visits only the expression of a
    /// PropertyAccessExpression, leaving the name unchanged. This prevents the name from being treated as a standalone
    /// identifier reference and incorrectly substituted with a class alias.
    /// </summary>
    private Node? VisitPropertyAccessExpressionForSubstitution(PropertyAccessExpression node)
    {
        var expression = Visitor().VisitNode(node.Expression);
        if (expression != node.Expression)
        {
            return Factory().UpdatePropertyAccessExpression(node, expression, node.QuestionDotToken, node.Name(), node.Flags);
        }
        return node.AsNode();
    }

    /// <summary>Go: <c>visitElementAccessExpression</c>(classfields.go:1116-1138)。</summary>
    private Node? VisitElementAccessExpression(ElementAccessExpression node)
    {
        if (_shouldTransformSuperInStaticInitializers && _currentClassElement != null &&
            IsSuperProperty(node.AsNode()) &&
            IsStaticPropertyDeclarationOrClassStaticBlock(_currentClassElement) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null)
        {
            var data = _lexicalEnvironment._data;
            if ((data._facts & ClassFacts.ClassWasDecorated) != 0)
            {
                return VisitInvalidSuperProperty(node.AsNode());
            }
            if (data._classConstructor != null && data._superClassReference != null)
            {
                // converts `super[x]` into `Reflect.get(_baseTemp, x, _classTemp)`
                var superProperty = Factory().NewReflectGetCall(
                    data._superClassReference,
                    Visitor().VisitNode(node.ArgumentExpression)!,
                    data._classConstructor);
                EmitContext().SetOriginal(superProperty, node.Expression);
                superProperty.Loc = node.Expression!.Loc;
                return superProperty;
            }
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitPreOrPostfixUnaryExpression</c>(classfields.go:1140-1242)。</summary>
    private Node? VisitPreOrPostfixUnaryExpression(Node node, bool discarded)
    {
        Kind @operator;
        Node? operand;
        if (IsPrefixUnaryExpression(node))
        {
            @operator = node.AsPrefixUnaryExpression()!.Operator;
            operand = node.AsPrefixUnaryExpression()!.Operand;
        }
        else
        {
            @operator = node.AsPostfixUnaryExpression()!.Operator;
            operand = node.AsPostfixUnaryExpression()!.Operand;
        }

        if (@operator == Kind.PlusPlusToken || @operator == Kind.MinusMinusToken)
        {
            var operandSkipped = SkipParentheses(operand)!;

            // Private identifier property access
            if (IsPropertyAccessExpression(operandSkipped) && IsPrivateIdentifier(operandSkipped.Name()!))
            {
                var info = AccessPrivateIdentifier(operandSkipped.Name()!);
                if (info != null)
                {
                    var receiver = Visitor().VisitNode(operandSkipped.Expression());
                    var (readExpression, initializeExpression) = CreateCopiableReceiverExpr(receiver!);

                    var expression = CreatePrivateIdentifierAccessHelper(info, readExpression!)!;
                    Node? temp = null;
                    if (!IsPrefixUnaryExpression(node) && !discarded)
                    {
                        temp = Factory().NewTempVariable();
                        EmitContext().AddVariableDeclaration(temp);
                    }
                    expression = ExpandPreOrPostfixIncrementOrDecrementExpression(Factory(), EmitContext(), node, expression, temp);
                    var assignReceiver = readExpression;
                    if (initializeExpression != null)
                    {
                        assignReceiver = initializeExpression;
                    }
                    expression = CreatePrivateIdentifierAssignment(info, assignReceiver!, expression, Kind.EqualsToken)!;
                    EmitContext().SetOriginal(expression, node);
                    expression.Loc = node.Loc;
                    if (temp != null)
                    {
                        expression = Factory().NewCommaExpression(expression, temp);
                        expression.Loc = node.Loc;
                    }
                    return expression;
                }
            }
            else if (_shouldTransformSuperInStaticInitializers && _currentClassElement != null &&
                IsSuperProperty(operandSkipped) &&
                IsStaticPropertyDeclarationOrClassStaticBlock(_currentClassElement) &&
                _lexicalEnvironment != null && _lexicalEnvironment._data != null)
            {
                // converts `++super.a` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = ++_a), _classTemp), _b)`
                // converts `++super[f()]` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = ++_b), _classTemp), _c)`
                // converts `--super.a` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = --_a), _classTemp), _b)`
                // converts `--super[f()]` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = --_b), _classTemp), _c)`
                // converts `super.a++` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = _a++), _classTemp), _b)`
                // converts `super[f()]++` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = _b++), _classTemp), _c)`
                // converts `super.a--` into `(Reflect.set(_baseTemp, "a", (_a = Reflect.get(_baseTemp, "a", _classTemp), _b = _a--), _classTemp), _b)`
                // converts `super[f()]--` into `(Reflect.set(_baseTemp, _a = f(), (_b = Reflect.get(_baseTemp, _a, _classTemp), _c = _b--), _classTemp), _c)`
                var data = _lexicalEnvironment._data;
                if ((data._facts & ClassFacts.ClassWasDecorated) != 0)
                {
                    var visitedExpr = VisitInvalidSuperProperty(operandSkipped);
                    if (IsPrefixUnaryExpression(node))
                    {
                        return Factory().UpdatePrefixUnaryExpression(node.AsPrefixUnaryExpression()!, node.AsPrefixUnaryExpression()!.Operator, visitedExpr);
                    }
                    return Factory().UpdatePostfixUnaryExpression(node.AsPostfixUnaryExpression()!, visitedExpr, node.AsPostfixUnaryExpression()!.Operator);
                }
                if (data._classConstructor != null && data._superClassReference != null)
                {
                    Node? setterName = null;
                    Node? getterName = null;
                    if (IsPropertyAccessExpression(operandSkipped))
                    {
                        if (IsIdentifier(operandSkipped.Name()!))
                        {
                            getterName = Factory().NewStringLiteralFromNode(operandSkipped.Name()!);
                            setterName = getterName;
                        }
                    }
                    else if (IsElementAccessExpression(operandSkipped))
                    {
                        if (IsSimpleInlineableExpression(operandSkipped.AsElementAccessExpression()!.ArgumentExpression!))
                        {
                            getterName = operandSkipped.AsElementAccessExpression()!.ArgumentExpression;
                            setterName = getterName;
                        }
                        else
                        {
                            getterName = Factory().NewTempVariable();
                            EmitContext().AddVariableDeclaration(getterName);
                            setterName = Factory().NewAssignmentExpression(getterName, Visitor().VisitNode(operandSkipped.AsElementAccessExpression()!.ArgumentExpression)!);
                        }
                    }
                    if (setterName != null && getterName != null)
                    {
                        var expression = Factory().NewReflectGetCall(data._superClassReference, getterName, data._classConstructor);
                        expression.Loc = operandSkipped.Loc;

                        Node? temp = null;
                        if (!discarded)
                        {
                            temp = Factory().NewTempVariable();
                            EmitContext().AddVariableDeclaration(temp);
                        }
                        expression = ExpandPreOrPostfixIncrementOrDecrementExpression(Factory(), EmitContext(), node, expression, temp);
                        expression = Factory().NewReflectSetCall(data._superClassReference, setterName, expression, data._classConstructor);
                        EmitContext().SetOriginal(expression, node);
                        expression.Loc = node.Loc;
                        if (temp != null)
                        {
                            expression = Factory().NewCommaExpression(expression, temp);
                            expression.Loc = node.Loc;
                        }
                        return expression;
                    }
                }
            }
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>visitForStatement</c>(classfields.go:1244-1253)。</summary>
    private Node VisitForStatement(ForStatement node)
    {
        var initializer = _discardedValueVisitor.VisitNode(node.Initializer);
        var condition = Visitor().VisitNode(node.Condition);
        var incrementor = _discardedValueVisitor.VisitNode(node.Incrementor);
        var saved = _inIterationStatement;
        _inIterationStatement = true;
        var body = EmitContext().VisitIterationBody(node.Statement!, Visitor());
        _inIterationStatement = saved;
        return Factory().UpdateForStatement(node, initializer, condition, incrementor, body);
    }

    /// <summary>Go: <c>visitExpressionStatement</c>(classfields.go:1255-1267)。</summary>
    private Node VisitExpressionStatement(ExpressionStatement node)
    {
        // Preserve private identifiers that appear directly as the expression of an
        // ExpressionStatement (e.g., `#;`). This is error-recovery output from the parser
        // for invalid syntax. Keeping it ensures the runtime throws a SyntaxError rather
        // than silently succeeding with an empty statement.
        if (IsPrivateIdentifier(node.Expression!) && _shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            return node.AsNode();
        }
        return Factory().UpdateExpressionStatement(
            node,
            _discardedValueVisitor.VisitNode(node.Expression));
    }

    /// <summary>Go: <c>createCopiableReceiverExpr</c>(classfields.go:1269-1281)。</summary>
    private (Node? ReadExpression, Node? InitializeExpression) CreateCopiableReceiverExpr(Node receiver)
    {
        var clone = receiver;
        if (!NodeIsSynthesized(receiver))
        {
            clone = receiver.Clone(Factory())!;
        }
        if (IsSimpleInlineableExpression(receiver))
        {
            return (clone, null);
        }
        var readExpression = Factory().NewTempVariable();
        EmitContext().AddVariableDeclaration(readExpression);
        var initializeExpression = Factory().NewAssignmentExpression(readExpression, clone);
        return (readExpression, initializeExpression);
    }

    /// <summary>Go: <c>visitCallExpression</c>(classfields.go:1283-1336)。</summary>
    private Node? VisitCallExpression(CallExpression node)
    {
        if (IsPropertyAccessExpression(node.Expression!) && IsPrivateIdentifier(node.Expression!.AsPropertyAccessExpression()!.Name()!) &&
            AccessPrivateIdentifier(node.Expression.AsPropertyAccessExpression()!.Name()!) != null)
        {
            // obj.#x()

            // Transform call expressions of private names to properly bind the `this` parameter.
            var (thisArg, target) = CreateCallBinding(node.Expression);
            var visitedTarget = Visitor().VisitNode(target);
            var visitedThisArg = Visitor().VisitNode(thisArg);
            var visitedArgs = Visitor().VisitNodes(node.Arguments);
            var allArgs = new List<Node>(1 + (visitedArgs?.Nodes?.Length ?? 0));
            allArgs.Add(visitedThisArg!);
            allArgs.AddRange(visitedArgs?.Nodes ?? []);
            if ((node.Flags & NodeFlags.OptionalChain) != 0)
            {
                return Factory().UpdateCallExpression(
                    node,
                    Factory().NewPropertyAccessExpression(visitedTarget, node.QuestionDotToken, Factory().NewIdentifier("call"), NodeFlags.OptionalChain),
                    null, /*questionDotToken*/
                    null, /*typeArguments*/
                    Factory().NewNodeList(allArgs.ToArray()),
                    node.Flags);
            }
            return Factory().UpdateCallExpression(
                node,
                Factory().NewPropertyAccessExpression(visitedTarget, null, Factory().NewIdentifier("call"), NodeFlags.None),
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Factory().NewNodeList(allArgs.ToArray()),
                node.Flags);
        }

        if (_shouldTransformSuperInStaticInitializers && _currentClassElement != null &&
            IsSuperProperty(node.Expression) &&
            IsStaticPropertyDeclarationOrClassStaticBlock(_currentClassElement) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null &&
            _lexicalEnvironment._data._classConstructor != null)
        {
            // super.x()
            // super[x]()

            // converts `super.f(...)` into `Reflect.get(_baseTemp, "f", _classTemp).call(_classTemp, ...)`
            var invocation = Factory().NewFunctionCallCall(
                Visitor().VisitNode(node.Expression)!,
                _lexicalEnvironment._data._classConstructor,
                Visitor().VisitNodes(node.Arguments)?.Nodes ?? []);
            EmitContext().SetOriginal(invocation, node.AsNode());
            invocation.Loc = node.Loc;
            return invocation;
        }

        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitTaggedTemplateExpression</c>(classfields.go:1338-1384)。</summary>
    private Node? VisitTaggedTemplateExpression(TaggedTemplateExpression node)
    {
        if (IsPropertyAccessExpression(node.Tag!) && IsPrivateIdentifier(node.Tag!.AsPropertyAccessExpression()!.Name()!) &&
            AccessPrivateIdentifier(node.Tag.AsPropertyAccessExpression()!.Name()!) != null)
        {
            // Bind the `this` correctly for tagged template literals when the tag is a private identifier property access.
            var (thisArg, target) = CreateCallBinding(node.Tag);
            var bindExpr = Factory().NewCallExpression(
                Factory().NewPropertyAccessExpression(Visitor().VisitNode(target), null, Factory().NewIdentifier("bind"), NodeFlags.None),
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Factory().NewNodeList([Visitor().VisitNode(thisArg)!]),
                NodeFlags.None);
            return Factory().UpdateTaggedTemplateExpression(
                node,
                bindExpr,
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Visitor().VisitNode(node.Template),
                node.Flags);
        }

        if (_shouldTransformSuperInStaticInitializers && _currentClassElement != null &&
            IsSuperProperty(node.Tag) &&
            IsStaticPropertyDeclarationOrClassStaticBlock(_currentClassElement) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null &&
            _lexicalEnvironment._data._classConstructor != null)
        {
            // converts `` super.f`x` `` into `` Reflect.get(_baseTemp, "f", _classTemp).bind(_classTemp)`x` ``
            var invocation = Factory().NewFunctionBindCall(
                Visitor().VisitNode(node.Tag)!,
                _lexicalEnvironment._data._classConstructor,
                []);
            EmitContext().SetOriginal(invocation, node.AsNode());
            invocation.Loc = node.Loc;
            return Factory().UpdateTaggedTemplateExpression(
                node,
                invocation,
                null, /*questionDotToken*/
                null, /*typeArguments*/
                Visitor().VisitNode(node.Template),
                node.Flags);
        }

        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>transformClassStaticBlockDeclaration</c>(classfields.go:1386-1422)。</summary>
    private Node? TransformClassStaticBlockDeclaration(Node node)
    {
        if (_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            if (IsClassThisAssignmentBlock(EmitContext(), node))
            {
                var result = Visitor().VisitNode(node.AsClassStaticBlockDeclaration()!.Body!.AsBlock()!.Statements!.Nodes![0].Expression());
                // If the generated `_classThis` assignment is a noop (i.e., `_classThis = _classThis`), we can
                // eliminate the expression
                if (IsAssignmentExpression(result, true /*excludeCompoundAssignment*/))
                {
                    var binary = result!.AsBinaryExpression()!;
                    if (binary.Left == binary.Right)
                    {
                        return null;
                    }
                }
                return result;
            }

            if (IsClassNamedEvaluationHelperBlock(EmitContext(), node))
            {
                return Visitor().VisitNode(node.AsClassStaticBlockDeclaration()!.Body!.AsBlock()!.Statements!.Nodes![0].Expression());
            }

            EmitContext().StartVariableEnvironment();
            Node[]? statements = SetCurrentClassElementAndVisitStatements(node, node.AsClassStaticBlockDeclaration()!.Body!.AsBlock()!.Statements!.Nodes ?? []);
            statements = EmitContext().EndAndMergeVariableEnvironment(statements);

            var iife = Factory().NewImmediatelyInvokedArrowFunction(statements!);
            var arrowFunction = SkipParentheses(iife.Expression())!;
            EmitContext().SetOriginal(arrowFunction, node);
            EmitContext().AddEmitFlags(arrowFunction, EmitFlags.NoLexicalArguments);
            // Preserve the statement list source range so the printer can emit detached comments
            // (e.g., `// do` inside an otherwise empty static block)
            arrowFunction.AsArrowFunction()!.Body!.AsBlock()!.Statements!.Loc = node.AsClassStaticBlockDeclaration()!.Body!.AsBlock()!.Statements!.Loc;
            EmitContext().SetOriginal(iife, node);
            EmitContext().AssignSourceMapRange(iife, node);
            EmitContext().AddEmitFlags(arrowFunction, EmitFlags.NoLexicalThis);
            return iife;
        }
        return null;
    }

    /// <summary>Go: <c>setCurrentClassElementAndVisitStatements</c>(classfields.go:1424-1430)。</summary>
    private Node[] SetCurrentClassElementAndVisitStatements(Node classElement, Node[] statements)
    {
        var savedCurrentClassElement = _currentClassElement;
        _currentClassElement = classElement;
        var (result, _) = Visitor().VisitSlice(statements);
        _currentClassElement = savedCurrentClassElement;
        return result!;
    }

    /// <summary>Go: <c>isAnonymousClassNeedingAssignedNameWorker</c>(classfields.go:1432-1450)。</summary>
    private bool IsAnonymousClassNeedingAssignedNameWorker(Node node)
    {
        if (IsClassExpression(node) && node.Name() == null)
        {
            var staticPropertiesOrClassStaticBlocks = GetStaticPropertiesAndClassStaticBlock(node);
            if (Some(staticPropertiesOrClassStaticBlocks, n =>
                IsClassNamedEvaluationHelperBlock(EmitContext(), n)))
            {
                return false;
            }
            var hasTransformableStatics = (_shouldTransformPrivateElementsOrClassStaticBlocks ||
                NodeHasTransformPrivateStaticElementsFlag(node)) &&
                Some(staticPropertiesOrClassStaticBlocks, n =>
                    IsClassStaticBlockDeclaration(n) ||
                    IsPrivateIdentifierClassElementDeclaration(n) ||
                    _shouldTransformInitializers && IsInitializedProperty(n));
            return hasTransformableStatics;
        }
        return false;
    }

    /// <summary>Go: <c>visitBinaryExpression</c>(classfields.go:1452-1612)。</summary>
    private Node? VisitBinaryExpression(BinaryExpression node, bool discarded)
    {
        if (IsDestructuringAssignment(node.AsNode()))
        {
            // ({ x: obj.#x } = ...)
            // ({ x: super.x } = ...)
            // ({ x: super[x] } = ...)
            var savedPendingExpressions = _pendingExpressions;
            _pendingExpressions = [];
            var updated = Factory().UpdateBinaryExpression(
                node,
                null,
                _assignmentTargetVisitor.VisitNode(node.Left),
                null,
                node.OperatorToken,
                Visitor().VisitNode(node.Right));
            Node? result;
            if (_pendingExpressions.Count > 0)
            {
                var exprs = new List<Node>(_pendingExpressions) { updated };
                result = Factory().InlineExpressions(exprs.ToArray());
            }
            else
            {
                result = updated;
            }
            _pendingExpressions = savedPendingExpressions;
            return result!;
        }

        if (IsAssignmentExpression(node.AsNode(), false /*excludeCompound*/))
        {
            // 13.15.2 RS: Evaluation
            //   AssignmentExpression : LeftHandSideExpression `=` AssignmentExpression
            //     1. If |LeftHandSideExpression| is neither an |ObjectLiteral| nor an |ArrayLiteral|, then
            //        a. Let _lref_ be ? Evaluation of |LeftHandSideExpression|.
            //        b. If IsAnonymousFunctionDefinition(|AssignmentExpression|) and IsIdentifierRef of |LeftHandSideExpression| are both *true*, then
            //           i. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...
            //
            //   AssignmentExpression : LeftHandSideExpression `&&=` AssignmentExpression
            //     ...
            //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and IsIdentifierRef of |LeftHandSideExpression| is *true*, then
            //        a. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...
            //
            //   AssignmentExpression : LeftHandSideExpression `||=` AssignmentExpression
            //     ...
            //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and IsIdentifierRef of |LeftHandSideExpression| is *true*, then
            //        a. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...
            //
            //   AssignmentExpression : LeftHandSideExpression `??=` AssignmentExpression
            //     ...
            //     4. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and IsIdentifierRef of |LeftHandSideExpression| is *true*, then
            //        a. Let _rval_ be ? NamedEvaluation of |AssignmentExpression| with argument _lref_.[[ReferencedName]].
            //     ...

            if (IsNamedEvaluationAnd(EmitContext(), node.AsNode(), _isAnonymousClassNeedingAssignedName!))
            {
                node = TransformNamedEvaluation(EmitContext(), node.AsNode(), false, "").AsBinaryExpression()!;
                Debug.Assert(node.AsNode() != null && IsAssignmentExpression(node.AsNode(), false));
            }

            var left = SkipOuterExpressions(node.Left, OuterExpressionKinds.PartiallyEmittedExpressions | OuterExpressionKinds.Parentheses)!;
            if (IsPropertyAccessExpression(left) && IsPrivateIdentifier(left.Name()!))
            {
                // obj.#x = ...
                var info = AccessPrivateIdentifier(left.Name()!);
                if (info != null)
                {
                    var result = CreatePrivateIdentifierAssignment(info, left.Expression()!, node.Right!, node.OperatorToken!.Kind)!;
                    EmitContext().SetOriginal(result, node.AsNode());
                    result.Loc = node.Loc;
                    return result;
                }
            }
            else if (_shouldTransformSuperInStaticInitializers && _currentClassElement != null &&
                IsSuperProperty(node.Left) &&
                IsStaticPropertyDeclarationOrClassStaticBlock(_currentClassElement) &&
                _lexicalEnvironment != null && _lexicalEnvironment._data != null)
            {
                // super.x = ...
                // super[x] = ...
                // super.x += ...
                // super.x -= ...
                var data = _lexicalEnvironment._data;
                if ((data._facts & ClassFacts.ClassWasDecorated) != 0)
                {
                    return Factory().UpdateBinaryExpression(
                        node,
                        null,
                        VisitInvalidSuperProperty(node.Left!),
                        null,
                        node.OperatorToken,
                        Visitor().VisitNode(node.Right));
                }
                if (data._classConstructor != null && data._superClassReference != null)
                {
                    Node? setterName = null;
                    if (IsElementAccessExpression(node.Left!))
                    {
                        setterName = Visitor().VisitNode(node.Left!.AsElementAccessExpression()!.ArgumentExpression);
                    }
                    else if (IsPropertyAccessExpression(node.Left!) && IsIdentifier(node.Left!.AsPropertyAccessExpression()!.Name()!))
                    {
                        setterName = Factory().NewStringLiteralFromNode(node.Left!.AsPropertyAccessExpression()!.Name()!);
                    }
                    if (setterName != null)
                    {
                        // converts `super.x = 1` into `(Reflect.set(_baseTemp, "x", _a = 1, _classTemp), _a)`
                        // converts `super[f()] = 1` into `(Reflect.set(_baseTemp, f(), _a = 1, _classTemp), _a)`
                        // converts `super.x += 1` into `(Reflect.set(_baseTemp, "x", _a = Reflect.get(_baseTemp, "x", _classtemp) + 1, _classTemp), _a)`
                        // converts `super[f()] += 1` into `(Reflect.set(_baseTemp, _a = f(), _b = Reflect.get(_baseTemp, _a, _classtemp) + 1, _classTemp), _b)`

                        var expression = Visitor().VisitNode(node.Right);
                        if (IsCompoundAssignment(node.OperatorToken!.Kind))
                        {
                            var getterName = setterName;
                            if (!IsSimpleInlineableExpression(setterName))
                            {
                                getterName = Factory().NewTempVariable();
                                EmitContext().AddVariableDeclaration(getterName);
                                setterName = Factory().NewAssignmentExpression(getterName, setterName);
                            }
                            var superPropertyGet = Factory().NewReflectGetCall(
                                data._superClassReference,
                                getterName,
                                data._classConstructor);
                            EmitContext().SetOriginal(superPropertyGet, node.Left);
                            superPropertyGet.Loc = node.Left!.Loc;
                            expression = Factory().NewBinaryExpression(
                                null,
                                superPropertyGet,
                                null,
                                Factory().NewToken(GetNonAssignmentOperatorForCompoundAssignment(node.OperatorToken.Kind)),
                                expression);
                            expression.Loc = node.Loc;
                        }

                        Node? temp = null;
                        if (!discarded)
                        {
                            temp = Factory().NewTempVariable();
                            EmitContext().AddVariableDeclaration(temp);
                        }
                        if (temp != null)
                        {
                            expression = Factory().NewAssignmentExpression(temp, expression!);
                            expression.Loc = node.Loc;
                        }

                        expression = Factory().NewReflectSetCall(
                            data._superClassReference,
                            setterName,
                            expression!,
                            data._classConstructor);
                        EmitContext().SetOriginal(expression, node.AsNode());
                        expression.Loc = node.Loc;

                        if (temp != null)
                        {
                            expression = Factory().NewCommaExpression(expression, temp);
                            expression.Loc = node.Loc;
                        }
                        return expression;
                    }
                }
            }
        }

        if (node.OperatorToken!.Kind == Kind.InKeyword && IsPrivateIdentifier(node.Left!))
        {
            // #x in obj
            return TransformPrivateIdentifierInInExpression(node);
        }

        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitParenthesizedExpression</c>(classfields.go:1614-1625)。</summary>
    private Node VisitParenthesizedExpression(ParenthesizedExpression node, bool discarded)
    {
        // 8.4.5 RS: NamedEvaluation
        //   ParenthesizedExpression : `(` Expression `)`
        //     ...
        //     2. Return ? NamedEvaluation of |Expression| with argument _name_.
        if (discarded)
        {
            var expression = _discardedValueVisitor.VisitNode(node.Expression);
            return Factory().UpdateParenthesizedExpression(node, expression);
        }
        // Go 在两个分支里各自短声明 `expression`；C# 不允许内层块的名字与外层后续局部同名（CS0136）→ 外层改名 visitedExpression。
        var visitedExpression = Visitor().VisitNode(node.Expression);
        return Factory().UpdateParenthesizedExpression(node, visitedExpression);
    }

    /// <summary>Go: <c>createPrivateIdentifierAssignment</c>(classfields.go:1627-1684)。</summary>
    private Node? CreatePrivateIdentifierAssignment(PrivateIdentifierInfo info, Node receiver, Node right, Kind @operator)
    {
        receiver = Visitor().VisitNode(receiver)!;
        right = Visitor().VisitNode(right)!;

        if (IsCompoundAssignment(@operator))
        {
            var (readExpression, initializeExpression) = CreateCopiableReceiverExpr(receiver);
            if (initializeExpression != null)
            {
                receiver = initializeExpression;
            }
            else
            {
                receiver = readExpression!;
            }
            right = Factory().NewBinaryExpression(
                null,
                CreatePrivateIdentifierAccessHelper(info, readExpression!)!,
                null,
                Factory().NewToken(GetNonAssignmentOperatorForCompoundAssignment(@operator)),
                right);
        }

        EmitContext().SetCommentRange(receiver, NewTextRange(-1, receiver.End()));

        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Accessor))
        {
            return Factory().NewClassPrivateFieldSetHelper(
                receiver,
                info._brandCheckIdentifier!,
                right,
                info._kind,
                info._setterName);
        }
        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Method))
        {
            return Factory().NewClassPrivateFieldSetHelper(
                receiver,
                info._brandCheckIdentifier!,
                right,
                info._kind,
                null);
        }
        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Field))
        {
            Node? f = null;
            if (info._isStatic)
            {
                f = info._variableName;
            }
            return Factory().NewClassPrivateFieldSetHelper(
                receiver,
                info._brandCheckIdentifier!,
                right,
                info._kind,
                f);
        }
        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Untransformed))
        {
            Debug.Fail("Access helpers should not be created for untransformed private elements");
            return null;
        }
        Debug.AssertNever(info, "Unknown private element type");
        return null;
    }
}
