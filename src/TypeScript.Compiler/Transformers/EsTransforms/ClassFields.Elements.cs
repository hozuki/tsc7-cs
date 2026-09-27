// ported from: internal/transformers/estransforms/classfields.go:378-894（解构赋值目标 / 类元素访问器 / 命名求值访问器 / 迭代与类元素状态管理 / auto-accessor 变换）
//
// Go→C# 语义改写（本分片）：
// 1) Go 的 `func(tx *classFieldsTransformer, node *ast.Node) *ast.Node` 具名回调参数 → C# 方法组 `Func<Node, Node?>`：
//    Go `(*classFieldsTransformer).visitEachChildOfNode` → `VisitEachChildOfNode`（回调不再带 tx 参数，改用 this）。
// 2) `*ast.ClassElement` / `*ast.PropertyName` / `*ast.IdentifierNode` / `*ast.Expression` 等 Kind 别名 → `Node`
//    （docs/ast.md §2：Kind 别名不生成，Expression/Statement 在 C# 里就是 Node）。
// 3) `info.kind == printer.PrivateIdentifierKindMethod` 之类的 `==` → `IsPrivateKind(info._kind, PrivateIdentifierKind.Method)`
//    （该 struct 没有 `==`，static readonly 不能作 case 常量；helper 见 ClassFields.cs）。
// 4) `if alias, ok := tx.classAliases[declaration]; ok` → `_classAliases != null && _classAliases.TryGetValue(declaration, out var alias)`
//    （Go 读 nil map 返回零值，C# 的 Dictionary 不行）。
// 5) Go 的 `append(xs, ...)` → `List<T>.Add/AddRange`；`InlineExpressions`/`NewSyntaxList`/`VisitSlice` 需要 `Node[]` → `.ToArray()`。
// 6) `debug.Assert` / `debug.FailBadSyntaxKind` → `Debug.Assert` / `Debug.FailBadSyntaxKind`（根命名空间 Debug，ClassFields.cs 同款）。
// 7) `printer.EFNoComments` → `EmitFlags.NoComments`；`printer.EFTransformPrivateStaticElements` → `EmitFlags.TransformPrivateStaticElements`。
// 8) `ast.CreateModifiersFromModifierFlags(flags, tx.Factory().NewModifier)` → 方法组直传 `Factory().NewModifier`。
// 9) `tx.accessorFieldResultVisitor.VisitSlice([]*ast.Node{...})` → `var (visited, _) = _accessorFieldResultVisitor.VisitSlice([backingField, getter, setter])`。
// 10) `printer.PrivateIdentifierKind` 是 C# `Printer.NodeFactory` 的嵌套类型 → 文件级 using 别名。
// 11) 可空性：Go 的 `*ast.Node` 可为 nil → C# `Node?`；既有 API 要求非空处补 `!`（纯空断言，无行为差异）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Transformers.Transformers;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using PrivateIdentifierKind = TypeScript.Printer.PrivateIdentifierKind;

namespace TypeScript.Transformers.EsTransforms;

public sealed partial class ClassFieldsTransformer
{
    /// <summary>Go: <c>visitDestructuringAssignmentTarget</c>（classfields.go:378-414）。</summary>
    private Node? VisitDestructuringAssignmentTarget(Node node)
    {
        if (IsObjectLiteralExpression(node) || IsArrayLiteralExpression(node))
        {
            return VisitAssignmentPattern(node);
        }
        if (IsPropertyAccessExpression(node) && IsPrivateIdentifier(node.AsPropertyAccessExpression()!.Name()!))
        {
            return WrapPrivateIdentifierForDestructuringTarget(node);
        }
        if (_shouldTransformSuperInStaticInitializers && _currentClassElement != null &&
            IsSuperProperty(node) &&
            IsStaticPropertyDeclarationOrClassStaticBlock(_currentClassElement) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null)
        {
            var data = _lexicalEnvironment._data;
            if ((data._facts & ClassFacts.ClassWasDecorated) != 0)
            {
                return VisitInvalidSuperProperty(node);
            }
            if (data._classConstructor != null && data._superClassReference != null)
            {
                Node? name = null;
                if (IsElementAccessExpression(node))
                {
                    name = Visitor().VisitNode(node.AsElementAccessExpression()!.ArgumentExpression);
                }
                else if (IsPropertyAccessExpression(node) && IsIdentifier(node.AsPropertyAccessExpression()!.Name()!))
                {
                    name = Factory().NewStringLiteralFromNode(node.AsPropertyAccessExpression()!.Name()!);
                }
                if (name != null)
                {
                    var temp = Factory().NewTempVariable();
                    var setExpr = Factory().NewReflectSetCall(
                        data._superClassReference!,
                        name,
                        temp,
                        data._classConstructor!
                    );
                    return Factory().NewAssignmentTargetWrapper(temp, setExpr);
                }
            }
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>visitClassElement</c>（classfields.go:416-437）——访问类的成员。</summary>
    private Node? VisitClassElement(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Constructor:
                return SetCurrentClassElementAnd(node, VisitConstructorDeclaration, node);
            case Kind.GetAccessor:
            case Kind.SetAccessor:
            case Kind.MethodDeclaration:
                return SetCurrentClassElementAnd(node, VisitMethodOrAccessorDeclaration, node);
            case Kind.PropertyDeclaration:
                return SetCurrentClassElementAnd(node, VisitPropertyDeclaration, node);
            case Kind.ClassStaticBlockDeclaration:
                return SetCurrentClassElementAnd(node, VisitClassStaticBlockDeclaration, node);
            case Kind.ComputedPropertyName:
                return VisitComputedPropertyName(node.AsComputedPropertyName()!);
            case Kind.SemicolonClassElement:
                return node;
            default:
                if (IsModifierLike(node))
                {
                    return VisitModifier(node);
                }
                return Visit(node);
        }
    }

    /// <summary>Go: <c>visitPropertyName</c>（classfields.go:439-445）——访问类成员的属性名。</summary>
    private Node? VisitPropertyName(Node? name)
    {
        if (IsComputedPropertyName(name!))
        {
            return VisitComputedPropertyName(name!.AsComputedPropertyName()!);
        }
        return Visitor().VisitNode(name);
    }

    /// <summary>Go: <c>visitAccessorFieldResult</c>（classfields.go:447-460）——第二遍访问 auto-accessor 字段变换的结果。</summary>
    private Node? VisitAccessorFieldResult(Node node)
    {
        switch (node.Kind)
        {
            case Kind.PropertyDeclaration:
                return TransformFieldInitializer(node.AsPropertyDeclaration()!);
            case Kind.GetAccessor:
            case Kind.SetAccessor:
                return VisitClassElement(node);
            default:
                Debug.FailBadSyntaxKind(node, "Expected node to either be a PropertyDeclaration, GetAccessorDeclaration, or SetAccessorDeclaration");
                return null;
        }
    }

    // visitIdentifier replaces Strada's onSubstituteNode/trySubstituteClassAlias. Instead of
    // substituting at emit time using NodeCheckFlags.ConstructorReference, we resolve the
    // identifier to its declaration and check if that declaration has a registered alias.
    /// <summary>Go: <c>visitIdentifier</c>（classfields.go:462-477）。</summary>
    private Node? VisitIdentifier(Identifier node)
    {
        var declaration = _resolver.GetReferencedValueDeclaration(EmitContext().MostOriginal(node.AsNode())!);
        if (declaration != null)
        {
            if (_classAliases != null && _classAliases.TryGetValue(declaration, out var alias) && _enclosingClassDeclarations.Has(declaration))
            {
                var clone = alias.Clone(Factory())!;
                EmitContext().SetSourceMapRange(clone, node.Loc);
                EmitContext().SetCommentRange(clone, node.Loc);
                return clone;
            }
        }
        return node.AsNode();
    }

    // visitPrivateIdentifier handles an undeclared private name. Replace it with an empty
    // identifier to indicate a problem with the code.
    // Note: private identifiers in statement position (e.g., `#;`) are intercepted earlier
    // by visitExpressionStatement, which preserves them so the runtime throws a SyntaxError.
    /// <summary>Go: <c>visitPrivateIdentifier</c>（classfields.go:479-490）。</summary>
    private Node? VisitPrivateIdentifier(Node node)
    {
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            return node;
        }
        if (_parentNode != null && IsStatement(_parentNode))
        {
            return node;
        }
        var result = Factory().NewIdentifier("");
        EmitContext().SetOriginal(result, node);
        return result;
    }

    /// <summary>Go: <c>transformPrivateIdentifierInInExpression</c>（classfields.go:492-502）——访问 `#id in expr`。</summary>
    private Node? TransformPrivateIdentifierInInExpression(BinaryExpression node)
    {
        var info = AccessPrivateIdentifier(node.Left!);
        if (info != null)
        {
            var receiver = Visitor().VisitNode(node.Right)!;
            var result = Factory().NewClassPrivateFieldInHelper(info._brandCheckIdentifier!, receiver);
            EmitContext().SetOriginal(result, node.AsNode());
            return result;
        }
        // Private name has not been declared. Subsequent transformers will handle this error
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitPropertyAssignment</c>（classfields.go:504-516）。</summary>
    private Node? VisitPropertyAssignment(PropertyAssignment node)
    {
        // 13.2.5.5 RS: PropertyDefinitionEvaluation
        //   PropertyAssignment : PropertyName `:` AssignmentExpression
        //     ...
        //     5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and _isProtoSetter_ is *false*, then
        //        a. Let _popValue_ be ? NamedEvaluation of |AssignmentExpression| with argument _propKey_.
        //     ...

        if (IsNamedEvaluationAnd(EmitContext(), node.AsNode(), _isAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node.AsNode(), false /*ignoreEmptyStringLiteral*/, "" /*assignedName*/)!.AsPropertyAssignment()!;
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitVariableStatement</c>（classfields.go:518-534）。</summary>
    private Node? VisitVariableStatement(VariableStatement node)
    {
        var savedPendingStatements = _pendingStatements;
        _pendingStatements = [];

        var visitedNode = Visitor().VisitEachChild(node.AsNode())!;

        if (_pendingStatements.Count > 0)
        {
            var result = new List<Node>(1 + _pendingStatements.Count);
            result.Add(visitedNode);
            result.AddRange(_pendingStatements);
            _pendingStatements = savedPendingStatements;
            return Factory().NewSyntaxList(result.ToArray());
        }

        _pendingStatements = savedPendingStatements;
        return visitedNode;
    }

    /// <summary>Go: <c>visitVariableDeclaration</c>（classfields.go:536-555）。</summary>
    private Node? VisitVariableDeclaration(VariableDeclaration node)
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

        if (IsNamedEvaluationAnd(EmitContext(), node.AsNode(), _isAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node.AsNode(), false, "")!.AsVariableDeclaration()!;
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitParameterDeclaration</c>（classfields.go:557-578）。</summary>
    private Node? VisitParameterDeclaration(ParameterDeclaration node)
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

        if (IsNamedEvaluationAnd(EmitContext(), node.AsNode(), _isAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node.AsNode(), false, "")!.AsParameterDeclaration()!;
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitBindingElement</c>（classfields.go:580-601）。</summary>
    private Node? VisitBindingElement(BindingElement node)
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

        if (IsNamedEvaluationAnd(EmitContext(), node.AsNode(), _isAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node.AsNode(), false, "")!.AsBindingElement()!;
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitExportAssignment</c>（classfields.go:603-621）。</summary>
    private Node? VisitExportAssignment(ExportAssignment node)
    {
        // 16.2.3.7 RS: Evaluation
        //   ExportDeclaration : `export` `default` AssignmentExpression `;`
        //     1. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |AssignmentExpression| with argument `"default"`.
        //     ...

        // NOTE: Since emit for `export =` translates to `module.exports = ...`, the assigned name of the class
        // is `""`.
        if (IsNamedEvaluationAnd(EmitContext(), node.AsNode(), _isAnonymousClassNeedingAssignedName))
        {
            var assignedName = "";
            if (!node.IsExportEquals)
            {
                assignedName = "default";
            }
            node = TransformNamedEvaluation(EmitContext(), node.AsNode(), true /*ignoreEmptyStringLiteral*/, assignedName)!.AsExportAssignment()!;
        }
        return Visitor().VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>injectPendingExpressions</c>（classfields.go:623-638）。</summary>
    private Node? InjectPendingExpressions(Node? expression)
    {
        if (_pendingExpressions.Count > 0)
        {
            if (IsParenthesizedExpression(expression!))
            {
                _pendingExpressions.Add(expression!.Expression()!);
                expression = Factory().UpdateParenthesizedExpression(
                    expression!.AsParenthesizedExpression()!,
                    Factory().InlineExpressions(_pendingExpressions.ToArray())
                );
            }
            else
            {
                var exprs = new List<Node>(_pendingExpressions) { expression! };
                expression = Factory().InlineExpressions(exprs.ToArray());
            }
            _pendingExpressions = [];
        }
        return expression;
    }

    /// <summary>Go: <c>visitComputedPropertyName</c>（classfields.go:640-654）。</summary>
    private Node? VisitComputedPropertyName(ComputedPropertyName node)
    {
        // Computed property names are evaluated in the enclosing scope, not the current class.
        // Replaces Strada's onEmitNode for ComputedPropertyName which switches to
        // lexicalEnvironment?.previous. We do this explicitly during transformation.
        var savedLexicalEnvironment = _lexicalEnvironment;
        var savedInsideComputedPropertyName = _insideComputedPropertyName;
        _insideComputedPropertyName = true;
        if (_lexicalEnvironment != null && _lexicalEnvironment._previous != null)
        {
            _lexicalEnvironment = _lexicalEnvironment._previous;
        }
        var expression = Visitor().VisitNode(node.Expression);
        _lexicalEnvironment = savedLexicalEnvironment;
        _insideComputedPropertyName = savedInsideComputedPropertyName;
        return Factory().UpdateComputedPropertyName(node, InjectPendingExpressions(expression));
    }

    /// <summary>Go: <c>visitConstructorDeclaration</c>（classfields.go:656-661）。</summary>
    private Node? VisitConstructorDeclaration(Node node)
    {
        if (_currentClassContainer != null)
        {
            return TransformConstructor(node.AsConstructorDeclaration()!, _currentClassContainer);
        }
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>shouldTransformClassElementToWeakMap</c>（classfields.go:663-668）。</summary>
    private bool ShouldTransformClassElementToWeakMap(Node node)
    {
        if (_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            return true;
        }
        return ShouldAlwaysTransformPrivateStaticElements(node);
    }

    /// <summary>Go: <c>shouldAlwaysTransformPrivateStaticElements</c>（classfields.go:670-672）。</summary>
    private bool ShouldAlwaysTransformPrivateStaticElements(Node node)
    {
        return HasStaticModifier(node) && (EmitContext().EmitFlags(node) & EmitFlags.TransformPrivateStaticElements) != 0;
    }

    // nodeHasTransformPrivateStaticElementsFlag checks the emit flag on a class node (not a member).
    // Unlike shouldAlwaysTransformPrivateStaticElements, this does not check HasStaticModifier,
    // since class nodes themselves don't have a static modifier.
    /// <summary>Go: <c>nodeHasTransformPrivateStaticElementsFlag</c>（classfields.go:677-679）。</summary>
    private bool NodeHasTransformPrivateStaticElementsFlag(Node node)
    {
        return (EmitContext().EmitFlags(node) & EmitFlags.TransformPrivateStaticElements) != 0;
    }

    /// <summary>Go: <c>visitMethodOrAccessorDeclaration</c>（classfields.go:681-712）。</summary>
    private Node? VisitMethodOrAccessorDeclaration(Node node)
    {
        Debug.Assert(!HasDecorators(node));

        if (!IsPrivateIdentifierClassElementDeclaration(node) || !ShouldTransformClassElementToWeakMap(node))
        {
            return _classElementVisitor.VisitEachChild(node);
        }

        // leave invalid code untransformed
        var info = AccessPrivateIdentifier(node.Name()!);
        Debug.Assert(info != null, "Undeclared private name for property declaration.");
        if (!info!._isValid)
        {
            return node;
        }

        var functionName = GetHoistedFunctionName(node);
        if (functionName != null)
        {
            var modifiers = ExtractNonStaticNonAccessorModifiers(node);
            EmitContext().StartVariableEnvironment();
            var saved = _inIterationStatement;
            _inIterationStatement = false;
            var body = EmitContext().VisitFunctionBody(node.Body()!, Visitor());
            var parameters = Visitor().VisitNodes(node.ParameterList());
            _inIterationStatement = saved;

            var funcExpr = Factory().NewFunctionExpression(modifiers, node.BodyData()!.AsteriskToken, functionName, null, parameters, null, null, body);
            var assignment = Factory().NewAssignmentExpression(functionName, funcExpr);
            AddPendingExpressions(assignment);
        }

        // remove method declaration from class
        return null;
    }

    /// <summary>Go: <c>extractNonStaticNonAccessorModifiers</c>（classfields.go:714-716）。</summary>
    private ModifierList? ExtractNonStaticNonAccessorModifiers(Node node)
    {
        return ExtractModifiers(EmitContext(), node.Modifiers(), ~(ModifierFlags.Static | ModifierFlags.Accessor));
    }

    /// <summary>Go: <c>setCurrentClassElementAnd</c>（classfields.go:718-728）。</summary>
    private Node? SetCurrentClassElementAnd(Node? classElement, Func<Node, Node?> visitor, Node node)
    {
        if (classElement != _currentClassElement)
        {
            var saved = _currentClassElement;
            _currentClassElement = classElement;
            var result = visitor(node);
            _currentClassElement = saved;
            return result;
        }
        return visitor(node);
    }

    // visitEachChildOfNode just calls Visitor.VisitEachChild, but is necessary to avoid repeated closure allocations when passing as a callback.
    /// <summary>Go: <c>visitEachChildOfNode</c>（classfields.go:730-732）。</summary>
    private Node? VisitEachChildOfNode(Node node)
    {
        return Visitor().VisitEachChild(node);
    }

    /// <summary>Go: <c>setInIterationStatementAnd</c>（classfields.go:734-743）。</summary>
    private Node? SetInIterationStatementAnd(bool inIteration, Func<Node, Node?> visitor, Node node)
    {
        if (_inIterationStatement != inIteration)
        {
            var saved = _inIterationStatement;
            _inIterationStatement = inIteration;
            var result = visitor(node);
            _inIterationStatement = saved;
            return result;
        }
        return visitor(node);
    }

    /// <summary>Go: <c>clearClassElementAndVisitEachChild</c>（classfields.go:745-747）。</summary>
    private Node? ClearClassElementAndVisitEachChild(Node node)
    {
        return SetCurrentClassElementAnd(null, VisitEachChildOfNode, node);
    }

    // visitFunctionExpressionOrDeclaration handles lexical environment scoping for function
    // expressions and declarations, mirroring Strada's onEmitNode behavior.
    //
    // In Strada, onEmitNode checks whether a FunctionExpression has been registered in
    // lexicalEnvironmentMap (via its original node). If found, the lexical environment is
    // restored; otherwise it is cleared (since regular functions create a new `this` scope).
    //
    // Since Corsa performs substitution eagerly (no emit-time hooks), we replicate this by
    // preserving currentClassElement for function expressions whose original node is a class
    // member of the current class. This allows visitThisExpression to correctly substitute
    // `this` -> `_classThis` inside synthesized functions (e.g., ES decorator descriptor
    // methods for static private auto-accessors).
    /// <summary>Go: <c>visitFunctionExpressionOrDeclaration</c>（classfields.go:761-779）。</summary>
    private Node? VisitFunctionExpressionOrDeclaration(Node node)
    {
        if (_currentClassElement != null)
        {
            var original = EmitContext().MostOriginal(node);
            if (original != node && _currentClassContainer != null)
            {
                foreach (var member in _currentClassContainer.Members() ?? [])
                {
                    if (EmitContext().MostOriginal(member) == original && IsStatic(member))
                    {
                        // The function expression originates from a static class member (e.g., a
                        // descriptor method synthesized by the ES decorator transformer for a
                        // static private auto-accessor). Preserve the current class element so
                        // that visitThisExpression can substitute `this` with `_classThis`.
                        // Non-static members must NOT preserve the class element because `this`
                        // inside their descriptor functions should remain dynamic.
                        return VisitEachChildOfNode(node);
                    }
                }
            }
        }
        return SetCurrentClassElementAnd(null, VisitEachChildOfNode, node);
    }

    /// <summary>Go: <c>setClassElementAndVisitEachChild</c>（classfields.go:781-783）。</summary>
    private Node? SetClassElementAndVisitEachChild(Node node)
    {
        return SetCurrentClassElementAnd(node, VisitEachChildOfNode, node);
    }

    /// <summary>Go: <c>getHoistedFunctionName</c>（classfields.go:785-801）。</summary>
    private Node? GetHoistedFunctionName(Node node)
    {
        Debug.Assert(node.Name() != null && IsPrivateIdentifier(node.Name()!));
        var info = AccessPrivateIdentifier(node.Name()!);
        Debug.Assert(info != null, "Undeclared private name for property declaration.");
        if (IsPrivateKind(info!._kind, PrivateIdentifierKind.Method))
        {
            return info._methodName;
        }
        if (IsPrivateKind(info._kind, PrivateIdentifierKind.Accessor))
        {
            if (IsGetAccessorDeclaration(node))
            {
                return info._getterName;
            }
            if (IsSetAccessorDeclaration(node))
            {
                return info._setterName;
            }
        }
        return null;
    }

    /// <summary>Go: <c>tryGetClassThis</c>（classfields.go:803-811）。</summary>
    private Node? TryGetClassThis()
    {
        var classThis = TryGetClassThisNoContainer();
        if (classThis != null)
        {
            return classThis;
        }
        if (_currentClassContainer != null)
        {
            return _currentClassContainer.Name();
        }
        return null;
    }

    /// <summary>Go: <c>tryGetClassThisNoContainer</c>（classfields.go:813-822）。</summary>
    private Node? TryGetClassThisNoContainer()
    {
        var lex = GetClassLexicalEnvironment();
        if (lex._classThis != null)
        {
            return lex._classThis;
        }
        if (lex._classConstructor != null)
        {
            return lex._classConstructor;
        }
        return null;
    }

    // transformAutoAccessor transforms an auto-accessor property:
    //
    //	accessor x = 1;
    //
    // into:
    //
    //	#x = 1;
    //	get x() { return this.#x; }
    //	set x(value) { this.#x = value; }
    /// <summary>Go: <c>transformAutoAccessor</c>（classfields.go:833-894）。</summary>
    private Node? TransformAutoAccessor(PropertyDeclaration node)
    {
        var commentRange = EmitContext().CommentRange(node.AsNode());
        var sourceMapRange = EmitContext().SourceMapRange(node.AsNode());

        // Since we're creating two declarations where there was previously one, cache
        // the expression for any computed property names.
        var name = node.Name();
        var getterName = name;
        var setterName = name;
        if (IsComputedPropertyName(name!) && !IsSimpleInlineableExpression(name!.Expression()!))
        {
            var cacheAssignment = FindComputedPropertyNameCacheAssignment(EmitContext(), name!);
            if (cacheAssignment != null)
            {
                getterName = Factory().UpdateComputedPropertyName(name!.AsComputedPropertyName()!, Visitor().VisitNode(name!.Expression()));
                setterName = Factory().UpdateComputedPropertyName(name!.AsComputedPropertyName()!, cacheAssignment.Left);
            }
            else
            {
                var temp = Factory().NewTempVariable();
                EmitContext().SetSourceMapRange(temp, name!.Expression()!.Loc);
                EmitContext().AddVariableDeclaration(temp);
                var expression = Visitor().VisitNode(name!.Expression());
                var assignment = Factory().NewAssignmentExpression(temp, expression!);
                EmitContext().SetSourceMapRange(assignment, name!.Expression()!.Loc);
                getterName = Factory().UpdateComputedPropertyName(name!.AsComputedPropertyName()!, assignment);
                setterName = Factory().UpdateComputedPropertyName(name!.AsComputedPropertyName()!, temp);
            }
        }

        var modifiers = _modifierVisitor.VisitModifiers(node.Modifiers());
        var backingField = CreateAccessorPropertyBackingField(Factory(), node, modifiers, node.Initializer)!;
        EmitContext().SetOriginal(backingField, node.AsNode());
        EmitContext().AddEmitFlags(backingField, EmitFlags.NoComments);
        EmitContext().SetSourceMapRange(backingField, sourceMapRange);

        Node? receiver;
        if (IsStatic(node.AsNode()))
        {
            receiver = TryGetClassThis();
            if (receiver == null)
            {
                receiver = Factory().NewThisExpression();
            }
        }
        else
        {
            receiver = Factory().NewThisExpression();
        }

        var getter = CreateAccessorPropertyGetRedirector(node, modifiers, getterName!, receiver!)!;
        EmitContext().SetOriginal(getter, node.AsNode());
        EmitContext().SetCommentRange(getter, commentRange);
        EmitContext().SetSourceMapRange(getter, sourceMapRange);

        // create a fresh copy of the modifiers so that we don't duplicate comments
        ModifierList? setterModifiers = null;
        if (modifiers != null)
        {
            setterModifiers = Factory().NewModifierList(CreateModifiersFromModifierFlags(modifiers.ModifierFlags, Factory().NewModifier));
        }
        var setter = CreateAccessorPropertySetRedirector(node, setterModifiers, setterName!, receiver!)!;
        EmitContext().SetOriginal(setter, node.AsNode());
        EmitContext().AddEmitFlags(setter, EmitFlags.NoComments);
        EmitContext().SetSourceMapRange(setter, sourceMapRange);

        // Visit the results in a second pass
        var (visited, _) = _accessorFieldResultVisitor.VisitSlice([backingField, getter, setter]);
        return Factory().NewSyntaxList(visited);
    }
}
