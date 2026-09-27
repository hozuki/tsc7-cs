// ported from: internal/transformers/estransforms/namedevaluation.go（535 行，全部：isClassNamedEvaluationHelperBlock、
//               classHasExplicitlyAssignedName、classHasDeclaredOrExplicitlyAssignedName、isAnonymousFunctionDefinition、
//               isNamedEvaluation、isNamedEvaluationAnd、getAssignedNameOfIdentifier、getAssignedNameOfPropertyName、
//               createClassNamedEvaluationHelperBlock、injectClassNamedEvaluationHelperBlockIfMissing、
//               finishTransformNamedEvaluation、transformNamedEvaluationOfXxx（8 个）、transformNamedEvaluation）
//
// 与 Go 的差异（逐条）：
// 1) Go 的包级函数 → `public static partial class EsTransforms` 的 `internal static` 成员（docs/architecture.md §2）；
//    classfields.go/esdecorator.go/using.go/async.go 是**独立类**，所以都取 `internal`。
//    Go 未导出名 → PascalCase（`isNamedEvaluation` → `IsNamedEvaluation`）。
// 2) Go 的 `type anonymousFunctionDefinition = ast.Node`（类型别名）→ C# 直接用 `Node`（docs/ast.md §2：别名不生成）。
//    回调参数 `cb func(*anonymousFunctionDefinition) bool` → `Func<Node, bool>?`。
// 3) Go 的多返回值 `(assignedName *ast.Expression, updatedName *ast.PropertyName)` →
//    C# 具名元组 `(Node AssignedName, Node UpdatedName)`（仓库先例：`Core.PositionToLineAndByteOffset`）。
// 4) Go 的 `debug.Fail(...)` 之后还有 `return`（Go 允许，因为 Fail 不返回）→ C# 的 `Debug.Fail` 声明的返回类型是
//    void，所以 `return` 仍可达、不触发 CS0162（docs/architecture.md §3.2 的机械改写）。
// 5) Go 的 `slices.IndexFunc(node.Members(), ...) + 1` → `Array.FindIndex(node.Members()!, ...) + 1`
//    （两者在"找不到"时都返回 -1，+1 后同为 0 → leading 空、trailing 是全部）。
// 6) `len(x)` 作用在可能为 nil 的切片上（Go 的 nil 切片长度是 0）→ C# 写 `x?.Length ?? 0`/`x is { Length: >= 2 }`，
//    避免把 Go 的"nil → 0"变成 NRE。
// 7) Go 的 `a == b`（*Node 指针比较）→ C# `ReferenceEquals(a, b)`（显式表达引用相等，Node 未重载 `==`）。
// 8) Go 的 `*ast.Expression`/`*ast.PropertyName`/`*ast.ClassLikeDeclaration`/`*ast.StringLiteralNode` 等都是
//    `= Node` 的 union 别名（除了 `StringLiteralNode` 是具体类型）→ 参数/返回值一律 `Node`，
//    `getAssignedNameOfIdentifier` 返回具体类型 `StringLiteral`（Go 是 `*ast.StringLiteralNode`）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
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
    /// <summary>
    /// Gets whether a node is a `static {}` block containing only a single call to the `__setFunctionName` helper where that
    /// call's second argument is the value stored in the `assignedName` property of the block's `EmitNode`.
    /// @internal
    /// </summary>
    internal static bool IsClassNamedEvaluationHelperBlock(EmitContext emitContext, Node node)
    {
        if (!IsClassStaticBlockDeclaration(node) || (node.AsClassStaticBlockDeclaration()!.Body!.Statements()?.Length ?? 0) != 1)
        {
            return false;
        }

        var statement = node.AsClassStaticBlockDeclaration()!.Body!.Statements()![0];
        if (IsExpressionStatement(statement))
        {
            var expression = statement.Expression();
            if (emitContext.IsCallToHelper(expression!, "__setFunctionName"))
            {
                var arguments = expression!.AsCallExpression()!.Arguments;
                var nodes = arguments!.Nodes;
                return nodes is { Length: >= 2 } &&
                    ReferenceEquals(nodes[1], emitContext.AssignedName(node));
            }
        }

        return false;
    }

    /// <summary>
    /// Gets whether a `ClassLikeDeclaration` has a `static {}` block containing only a single call to the
    /// `__setFunctionName` helper.
    /// @internal
    /// </summary>
    internal static bool ClassHasExplicitlyAssignedName(EmitContext emitContext, Node node)
    {
        if (emitContext.AssignedName(node) is not null)
        {
            foreach (var member in node.Members() ?? [])
            {
                if (IsClassNamedEvaluationHelperBlock(emitContext, member))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gets whether a `ClassLikeDeclaration` has a declared name or contains a `static {}` block containing only a single
    /// call to the `__setFunctionName` helper.
    /// @internal
    /// </summary>
    internal static bool ClassHasDeclaredOrExplicitlyAssignedName(EmitContext emitContext, Node node)
    {
        return node.Name() is not null || ClassHasExplicitlyAssignedName(emitContext, node);
    }

    /// <summary>
    /// Indicates whether an expression is an anonymous function definition.
    /// See https://tc39.es/ecma262/#sec-isanonymousfunctiondefinition
    /// </summary>
    internal static bool IsAnonymousFunctionDefinition(EmitContext emitContext, Node node, Func<Node, bool>? cb)
    {
        node = SkipOuterExpressions(node, OuterExpressionKinds.All)!;
        switch (node.Kind)
        {
            case Kind.ClassExpression:
                if (ClassHasDeclaredOrExplicitlyAssignedName(emitContext, node))
                {
                    return false;
                }

                break;
            case Kind.FunctionExpression:
                if (node.AsFunctionExpression()!.Name() is not null)
                {
                    return false;
                }

                break;
            case Kind.ArrowFunction:
                // arrow functions are always anonymous
                break;
            default:
                return false;
        }

        if (cb is not null)
        {
            return cb(node);
        }

        return true;
    }

    /// <summary>Go: <c>func isNamedEvaluation(emitContext *printer.EmitContext, node *ast.Node) bool</c>。</summary>
    internal static bool IsNamedEvaluation(EmitContext emitContext, Node node)
    {
        return IsNamedEvaluationAnd(emitContext, node, null);
    }

    /// <summary>Go: <c>func isNamedEvaluationAnd(emitContext *printer.EmitContext, node *ast.Node, cb func(*anonymousFunctionDefinition) bool) bool</c>。</summary>
    internal static bool IsNamedEvaluationAnd(EmitContext emitContext, Node node, Func<Node, bool>? cb)
    {
        if (!IsNamedEvaluationSource(node))
        {
            return false;
        }

        switch (node.Kind)
        {
            case Kind.ShorthandPropertyAssignment:
                return IsAnonymousFunctionDefinition(emitContext, node.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer!, cb);
            case Kind.PropertyAssignment:
            case Kind.VariableDeclaration:
            case Kind.Parameter:
            case Kind.BindingElement:
            case Kind.PropertyDeclaration:
                return IsAnonymousFunctionDefinition(emitContext, node.Initializer()!, cb);
            case Kind.BinaryExpression:
                return IsAnonymousFunctionDefinition(emitContext, node.AsBinaryExpression()!.Right!, cb);
            case Kind.ExportAssignment:
                return IsAnonymousFunctionDefinition(emitContext, node.Expression()!, cb);
            default:
                // Go: debug.Fail("Unhandled case in isNamedEvaluation")（抛异常；返回语句只为满足 C# 控制流）
                Debug.Fail("Unhandled case in isNamedEvaluation");
                return false;
        }
    }

    /// <summary>Gets a string literal to use as the assigned name of an anonymous class or function declaration.</summary>
    internal static StringLiteral GetAssignedNameOfIdentifier(EmitContext emitContext, Node name, Node expression)
    {
        var original = emitContext.MostOriginal(SkipOuterExpressions(expression, OuterExpressionKinds.All));
        if ((IsClassDeclaration(original!) || IsFunctionDeclaration(original!)) &&
            original!.Name() is null && HasSyntacticModifier(original!, ModifierFlags.Default))
        {
            // Go 的 Factory.NewStringLiteral 返回 *ast.StringLiteralNode；C# 的生成工厂统一返回 `Node`
            // （与 Go 的静态类型逐处对应时按 Go 的类型做向下转换）。
            return (StringLiteral)emitContext.Factory.NewStringLiteral("default", TokenFlags.None);
        }

        // Go: emitContext.Factory.NewStringLiteralFromNode(name) 的 Go 返回类型是 *ast.StringLiteralNode；
        // C# 的 Printer.NodeFactory.NewStringLiteralFromNode 声明成 `Node`（Go 的 `*StringLiteral`），
        // 所以这里按 Go 的静态类型做一次向下转换（工厂实现就是 NewStringLiteral）。
        return (StringLiteral)emitContext.Factory.NewStringLiteralFromNode(name);
    }

    /// <summary>Go: <c>func getAssignedNameOfPropertyName(emitContext *printer.EmitContext, name *ast.PropertyName, assignedNameText string) (assignedName *ast.Expression, updatedName *ast.PropertyName)</c>。</summary>
    internal static (Node AssignedName, Node UpdatedName) GetAssignedNameOfPropertyName(EmitContext emitContext, Node name, string assignedNameText)
    {
        var factory = emitContext.Factory;
        if (assignedNameText.Length > 0)
        {
            var assignedName = factory.NewStringLiteral(assignedNameText, TokenFlags.None);
            return (assignedName, name);
        }

        if (IsPropertyNameLiteral(name) || IsPrivateIdentifier(name))
        {
            var assignedName = factory.NewStringLiteralFromNode(name);
            return (assignedName, name);
        }

        var expression = name.Expression();
        if (IsPropertyNameLiteral(expression) && !IsIdentifier(expression!))
        {
            var assignedName = factory.NewStringLiteralFromNode(expression!);
            return (assignedName, name);
        }

        Debug.Assert(IsComputedPropertyName(name), "Expected computed property name");

        var generatedName = factory.NewGeneratedNameForNode(name);
        emitContext.AddVariableDeclaration(generatedName);

        var key = factory.NewPropKeyHelper(expression!);
        var assignment = factory.NewAssignmentExpression(generatedName, key);
        var updatedName = factory.UpdateComputedPropertyName(name.AsComputedPropertyName()!, assignment);
        return (generatedName, updatedName);
    }

    /// <summary>
    /// Creates a class `static {}` block used to dynamically set the name of a class.
    /// <para>The assignedName parameter is the expression used to resolve the assigned name at runtime. This expression should not produce
    /// side effects.</para>
    /// <para>The thisExpression parameter overrides the expression to use for the actual `this` reference. This can be used to provide an
    /// expression that has already had its `EmitFlags` set or may have been tracked to prevent substitution.</para>
    /// </summary>
    internal static Node CreateClassNamedEvaluationHelperBlock(EmitContext emitContext, Node assignedName, Node? thisExpression)
    {
        // produces:
        //
        //  static { __setFunctionName(this, "C"); }
        //
        thisExpression ??= emitContext.Factory.NewThisExpression();

        var factory = emitContext.Factory;
        var expression = factory.NewSetFunctionNameHelper(thisExpression, assignedName, "" /*prefix*/);
        var statement = factory.NewExpressionStatement(expression);
        var body = factory.NewBlock(factory.NewNodeList([statement]), false /*multiLine*/);
        var block = factory.NewClassStaticBlockDeclaration(null /*modifiers*/, body);

        // We use `emitNode.assignedName` to indicate this is a NamedEvaluation helper block
        // and to stash the expression used to resolve the assigned name.
        emitContext.SetAssignedName(block, assignedName);
        return block;
    }

    /// <summary>Injects a class `static {}` block used to dynamically set the name of a class, if one does not already exist.</summary>
    internal static Node InjectClassNamedEvaluationHelperBlockIfMissing(
        EmitContext emitContext,
        Node node,
        Node assignedName,
        Node? thisExpression)
    {
        // given:
        //
        //  let C = class {
        //  };
        //
        // produces:
        //
        //  let C = class {
        //      static { __setFunctionName(this, "C"); }
        //  };

        // NOTE: If the class has a `_classThis` assignment block, this helper will be injected after that block.

        if (ClassHasExplicitlyAssignedName(emitContext, node))
        {
            return node;
        }

        var factory = emitContext.Factory;
        var namedEvaluationBlock = CreateClassNamedEvaluationHelperBlock(emitContext, assignedName, thisExpression);
        if (node.Name() is not null)
        {
            emitContext.SetSourceMapRange(namedEvaluationBlock.Body()!.Statements()![0], node.Name()!.Loc);
        }

        var classMembers = node.Members() ?? [];
        var insertionIndex = Array.FindIndex(classMembers, n => IsClassThisAssignmentBlock(emitContext, n)) + 1;
        var leading = classMembers[..insertionIndex];
        var trailing = classMembers[insertionIndex..];

        var members = new List<Node>();
        members.AddRange(leading);
        members.Add(namedEvaluationBlock);
        members.AddRange(trailing);
        var membersList = factory.NewNodeList(members.ToArray());
        membersList.Loc = node.MemberList()!.Loc;

        var oldNode = node;
        if (IsClassDeclaration(node))
        {
            node = factory.UpdateClassDeclaration(
                node.AsClassDeclaration()!,
                node.Modifiers(),
                node.Name(),
                node.TypeParameterList(),
                node.AsClassDeclaration()!.HeritageClauses,
                membersList);
        }
        else
        {
            node = factory.UpdateClassExpression(
                node.AsClassExpression()!,
                node.Modifiers(),
                node.Name(),
                node.TypeParameterList(),
                node.AsClassExpression()!.HeritageClauses,
                membersList);
        }

        emitContext.SetAssignedName(node, assignedName);

        // Transfer ClassThis from old to new node, since UpdateClassExpression creates
        // a new node that won't have ClassThis set on it.
        var ct = emitContext.ClassThis(oldNode);
        if (ct is not null)
        {
            emitContext.SetClassThis(node, ct);
        }

        return node;
    }

    /// <summary>Go: <c>func finishTransformNamedEvaluation(emitContext *printer.EmitContext, expression *ast.Node, assignedName *ast.Expression, ignoreEmptyStringLiteral bool) *ast.Expression</c>。</summary>
    internal static Node FinishTransformNamedEvaluation(
        EmitContext emitContext,
        Node expression,
        Node assignedName,
        bool ignoreEmptyStringLiteral)
    {
        if (ignoreEmptyStringLiteral && IsStringLiteral(assignedName) && assignedName.Text().Length == 0)
        {
            return expression;
        }

        var factory = emitContext.Factory;
        var innerExpression = SkipOuterExpressions(expression, OuterExpressionKinds.All)!;

        Node updatedExpression;
        if (IsClassExpression(innerExpression))
        {
            updatedExpression = InjectClassNamedEvaluationHelperBlockIfMissing(emitContext, innerExpression, assignedName, null /*thisExpression*/);
        }
        else
        {
            updatedExpression = factory.NewSetFunctionNameHelper(innerExpression, assignedName, "" /*prefix*/);
        }

        return factory.RestoreOuterExpressions(expression, updatedExpression, OuterExpressionKinds.All);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfPropertyAssignment(context *printer.EmitContext, node *ast.PropertyAssignment, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfPropertyAssignment(EmitContext context, PropertyAssignment node, bool ignoreEmptyStringLiteral, string assignedNameText)
    {
        // 13.2.5.5 RS: PropertyDefinitionEvaluation
        //   PropertyAssignment : PropertyName `:` AssignmentExpression
        //     ...
        //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and _isProtoSetter_ is *false*, then
        //        a. Let _popValue_ be ? NamedEvaluation of |AssignmentExpression| with argument _propKey_.
        //     ...

        var factory = context.Factory;
        var (assignedName, name) = GetAssignedNameOfPropertyName(context, node.Name()!, assignedNameText);
        var initializer = FinishTransformNamedEvaluation(context, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdatePropertyAssignment(node, null /*modifiers*/, name, null /*postfixToken*/, null /*typeNode*/, initializer);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfShorthandAssignmentProperty(emitContext *printer.EmitContext, node *ast.ShorthandPropertyAssignment, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfShorthandAssignmentProperty(EmitContext emitContext, ShorthandPropertyAssignment node, bool ignoreEmptyStringLiteral, string assignedNameText)
    {
        // 13.15.5.3 RS: PropertyDestructuringAssignmentEvaluation
        //   AssignmentProperty : IdentifierReference Initializer?
        //     ...
        //     4. If |Initializer?| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _P_.
        //     ...

        var factory = emitContext.Factory;
        Node assignedName;
        if (assignedNameText.Length > 0)
        {
            assignedName = factory.NewStringLiteral(assignedNameText, TokenFlags.None);
        }
        else
        {
            assignedName = GetAssignedNameOfIdentifier(emitContext, node.Name()!, node.ObjectAssignmentInitializer!);
        }

        var objectAssignmentInitializer = FinishTransformNamedEvaluation(emitContext, node.ObjectAssignmentInitializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateShorthandPropertyAssignment(
            node,
            null, /*modifiers*/
            node.Name(),
            null, /*postfixToken*/
            null, /*typeNode*/
            node.EqualsToken,
            objectAssignmentInitializer);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfVariableDeclaration(emitContext *printer.EmitContext, node *ast.VariableDeclaration, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfVariableDeclaration(EmitContext emitContext, VariableDeclaration node, bool ignoreEmptyStringLiteral, string assignedNameText)
    {
        // 14.3.1.2 RS: Evaluation
        //   LexicalBinding : BindingIdentifier Initializer
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...
        //
        // 14.3.2.1 RS: Evaluation
        //   VariableDeclaration : BindingIdentifier Initializer
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        var factory = emitContext.Factory;
        Node assignedName;
        if (assignedNameText.Length > 0)
        {
            assignedName = factory.NewStringLiteral(assignedNameText, TokenFlags.None);
        }
        else
        {
            assignedName = GetAssignedNameOfIdentifier(emitContext, node.Name()!, node.Initializer!);
        }

        var initializer = FinishTransformNamedEvaluation(emitContext, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateVariableDeclaration(
            node,
            node.Name(),
            null, /*exclamationToken*/
            null, /*typeNode*/
            initializer);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfParameterDeclaration(emitContext *printer.EmitContext, node *ast.ParameterDeclaration, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfParameterDeclaration(EmitContext emitContext, ParameterDeclaration node, bool ignoreEmptyStringLiteral, string assignedNameText)
    {
        // 8.6.3 RS: IteratorBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     5. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...
        //
        // 14.3.3.3 RS: KeyedBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     4. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        var factory = emitContext.Factory;
        Node assignedName;
        if (assignedNameText.Length > 0)
        {
            assignedName = factory.NewStringLiteral(assignedNameText, TokenFlags.None);
        }
        else
        {
            assignedName = GetAssignedNameOfIdentifier(emitContext, node.Name()!, node.Initializer!);
        }

        var initializer = FinishTransformNamedEvaluation(emitContext, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateParameterDeclaration(
            node,
            null, /*modifiers*/
            node.DotDotDotToken,
            node.Name(),
            null, /*questionToken*/
            null, /*typeNode*/
            initializer);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfBindingElement(emitContext *printer.EmitContext, node *ast.BindingElement, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfBindingElement(EmitContext emitContext, BindingElement node, bool ignoreEmptyStringLiteral, string assignedNameText)
    {
        // 8.6.3 RS: IteratorBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     5. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...
        //
        // 14.3.3.3 RS: KeyedBindingInitialization
        //   SingleNameBinding : BindingIdentifier Initializer?
        //     ...
        //     4. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
        //     ...

        var factory = emitContext.Factory;
        Node assignedName;
        if (assignedNameText.Length > 0)
        {
            assignedName = factory.NewStringLiteral(assignedNameText, TokenFlags.None);
        }
        else
        {
            assignedName = GetAssignedNameOfIdentifier(emitContext, node.Name()!, node.Initializer!);
        }

        var initializer = FinishTransformNamedEvaluation(emitContext, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateBindingElement(
            node,
            node.DotDotDotToken,
            node.PropertyName,
            node.Name(),
            initializer);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfPropertyDeclaration(emitContext *printer.EmitContext, node *ast.PropertyDeclaration, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfPropertyDeclaration(EmitContext emitContext, PropertyDeclaration node, bool ignoreEmptyStringLiteral, string assignedNameText)
    {
        // 10.2.1.3 RS: EvaluateBody
        //   Initializer : `=` AssignmentExpression
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _functionObject_.[[ClassFieldInitializerName]].
        //     ...

        var factory = emitContext.Factory;
        var (assignedName, name) = GetAssignedNameOfPropertyName(emitContext, node.Name()!, assignedNameText);
        var initializer = FinishTransformNamedEvaluation(emitContext, node.Initializer!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdatePropertyDeclaration(
            node,
            node.Modifiers(),
            name,
            null, /*postfixToken*/
            null, /*typeNode*/
            initializer);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfAssignmentExpression(emitContext *printer.EmitContext, node *ast.BinaryExpression, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfAssignmentExpression(EmitContext emitContext, BinaryExpression node, bool ignoreEmptyStringLiteral, string assignedNameText)
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

        var factory = emitContext.Factory;
        Node assignedName;
        if (assignedNameText.Length > 0)
        {
            assignedName = factory.NewStringLiteral(assignedNameText, TokenFlags.None);
        }
        else
        {
            assignedName = GetAssignedNameOfIdentifier(emitContext, node.Left!, node.Right!);
        }

        var right = FinishTransformNamedEvaluation(emitContext, node.Right!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateBinaryExpression(
            node,
            null, /*modifiers*/
            node.Left,
            null, /*typeNode*/
            node.OperatorToken,
            right);
    }

    /// <summary>Go: <c>func transformNamedEvaluationOfExportAssignment(emitContext *printer.EmitContext, node *ast.ExportAssignment, ignoreEmptyStringLiteral bool, assignedNameText string) *ast.Expression</c>。</summary>
    internal static Node TransformNamedEvaluationOfExportAssignment(EmitContext emitContext, ExportAssignment node, bool ignoreEmptyStringLiteral, string assignedNameText)
    {
        // 16.2.3.7 RS: Evaluation
        //   ExportDeclaration : `export` `default` AssignmentExpression `;`
        //     1. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |AssignmentExpression| with argument `"default"`.
        //     ...

        // NOTE: Since emit for `export =` translates to `module.exports = ...`, the assigned name of the class or function
        // is `""`.

        var factory = emitContext.Factory;
        Node assignedName;
        if (assignedNameText.Length > 0)
        {
            assignedName = factory.NewStringLiteral(assignedNameText, TokenFlags.None);
        }
        else if (node.IsExportEquals)
        {
            assignedName = factory.NewStringLiteral("", TokenFlags.None);
        }
        else
        {
            assignedName = factory.NewStringLiteral("default", TokenFlags.None);
        }

        var expression = FinishTransformNamedEvaluation(emitContext, node.Expression!, assignedName, ignoreEmptyStringLiteral);
        return factory.UpdateExportAssignment(
            node,
            null, /*modifiers*/
            node.IsExportEquals,
            null, /*typeNode*/
            expression);
    }

    /// <summary>Performs a shallow transformation of a `NamedEvaluation` node, such that a valid name will be assigned.</summary>
    internal static Node TransformNamedEvaluation(EmitContext context, Node node, bool ignoreEmptyStringLiteral, string assignedName)
    {
        switch (node.Kind)
        {
            case Kind.PropertyAssignment:
                return TransformNamedEvaluationOfPropertyAssignment(context, node.AsPropertyAssignment()!, ignoreEmptyStringLiteral, assignedName);
            case Kind.ShorthandPropertyAssignment:
                return TransformNamedEvaluationOfShorthandAssignmentProperty(context, node.AsShorthandPropertyAssignment()!, ignoreEmptyStringLiteral, assignedName);
            case Kind.VariableDeclaration:
                return TransformNamedEvaluationOfVariableDeclaration(context, node.AsVariableDeclaration()!, ignoreEmptyStringLiteral, assignedName);
            case Kind.Parameter:
                return TransformNamedEvaluationOfParameterDeclaration(context, node.AsParameterDeclaration()!, ignoreEmptyStringLiteral, assignedName);
            case Kind.BindingElement:
                return TransformNamedEvaluationOfBindingElement(context, node.AsBindingElement()!, ignoreEmptyStringLiteral, assignedName);
            case Kind.PropertyDeclaration:
                return TransformNamedEvaluationOfPropertyDeclaration(context, node.AsPropertyDeclaration()!, ignoreEmptyStringLiteral, assignedName);
            case Kind.BinaryExpression:
                return TransformNamedEvaluationOfAssignmentExpression(context, node.AsBinaryExpression()!, ignoreEmptyStringLiteral, assignedName);
            case Kind.ExportAssignment:
                return TransformNamedEvaluationOfExportAssignment(context, node.AsExportAssignment()!, ignoreEmptyStringLiteral, assignedName);
            default:
                // Go: debug.Fail("Unhandled case in transformNamedEvaluation"); return node
                Debug.Fail("Unhandled case in transformNamedEvaluation");
                return node;
        }
    }
}
