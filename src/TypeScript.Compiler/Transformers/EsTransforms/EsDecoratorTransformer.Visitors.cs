// ported from: internal/transformers/estransforms/esdecorator.go:1724-2460
//               （visitThisExpression、visitCallExpression、visitTaggedTemplateExpression、visitPropertyAccessExpression、
//                visitElementAccessExpression、visitParameterDeclaration、visitNamedEvaluationSite、
//                isAnonymousClassNeedingAssignedName、canIgnoreEmptyStringLiteralInAssignedName、visitForStatement、
//                visitExpressionStatement、visitBinaryExpression、visitPreOrPostfixUnaryExpression、
//                visitReferencedPropertyName、visitPropertyName、visitComputedPropertyName、
//                visitDestructuringAssignmentTarget、visitAssignmentElement、visitAssignmentRestElement、
//                visitArrayAssignmentElement、visitAssignmentPropertyNode、visitShorthandAssignmentProperty、
//                visitAssignmentRestProperty、visitObjectAssignmentElement、visitAssignmentPattern、visitExportAssignment、
//                visitParenthesizedExpression、visitPartiallyEmittedExpression、prependExpressions、
//                injectPendingExpressions、injectPendingInitializers、transformAllDecoratorsOfDeclaration、
//                transformDecorator、createCallBinding、shouldBeCapturedInTempVariable）
//
// 与 Go 的差异（逐条，其余地方与 Go 逐行对应）：
// 1) Go 的多返回值 `(target, thisArg)`/`(referencedName, name)` → C# 的具名元组（与 Visitor.VisitSlice 的既有先例一致）。
// 2) Go 在 `*ast.Node` 上调用 `ast.IsXxx(nil)` 依赖"nil 时返回 false"；C# 的 Is* 形参非空 → 写成
//    `x is not null && IsXxx(x)`（短路语义相同，见 `visitPropertyName`/`visitAssignmentPropertyNode`）。
// 3) `transformers.IsSimpleInlineableExpression`/`GetNonAssignmentOperatorForCompoundAssignment`/`MoveRangePastModifiers`
//    → `using static TypeScript.Transformers.Transformers;` 后的裸名（docs/architecture.md §2）。
// 4) `isAnonymousClassNeedingAssignedName`/`canIgnoreEmptyStringLiteralInAssignedName`（Go 包级函数）落在
//    `EsTransforms`（本文件的文件末尾），前者形参非空（Go 侧对 nil 会 panic，调用点保证非 nil）、后者照 Go 判 nil。
// 5) `f.InlineExpressions(exprs)`/`f.NewAssignmentExpression(...)` 等声明为可空返回（Go 返回 *Expression 可能为 nil），
//    Go 在这些点上靠不变量保证非 nil（`prependExpressions` 里 pending 非空、`assign` 的右操作数非 nil）→ C# 用 `!`。
// 6) Go 的 `expandPreOrPostfixIncrementOrDecrementExpression`/`findComputedPropertyNameCacheAssignment` 来自
//    classfields.go、`isClassNamedEvaluationHelperBlock` 等来自 namedevaluation.go（T2/T5 分片），这里裸名调用。
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

internal sealed partial class EsDecoratorTransformer
{
    private Node VisitThisExpression(Node node)
    {
        if (_classThis is not null)
        {
            return _classThis;
        }

        return node;
    }

    private Node? VisitCallExpression(Node node)
    {
        var call = node.AsCallExpression()!;
        if (IsSuperProperty(call.Expression) && _classThis is not null)
        {
            var expression = Visitor().VisitNode(call.Expression);
            var argumentsList = Visitor().VisitNodes(call.Arguments);
            var invocation = Factory().NewFunctionCallCall(expression!, _classThis, argumentsList!.Nodes ?? []);
            EmitContext().SetOriginal(invocation, node);
            invocation.Loc = node.Loc;
            return invocation;
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitTaggedTemplateExpression(Node node)
    {
        var tte = node.AsTaggedTemplateExpression()!;
        if (IsSuperProperty(tte.Tag) && _classThis is not null)
        {
            var tag = Visitor().VisitNode(tte.Tag);
            var boundTag = Factory().NewFunctionBindCall(tag!, _classThis, []);
            EmitContext().SetOriginal(boundTag, node);
            boundTag.Loc = node.Loc;
            var template = Visitor().VisitNode(tte.Template);
            return Factory().UpdateTaggedTemplateExpression(tte, boundTag, null, null, template, tte.Flags);
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitPropertyAccessExpression(Node node)
    {
        var pa = node.AsPropertyAccessExpression()!;
        if (IsSuperProperty(node) && pa.Name() is not null && IsIdentifier(pa.Name()!) && _classThis is not null && _classSuper is not null)
        {
            var propertyName = Factory().NewStringLiteralFromNode(pa.Name()!);
            var superProperty = Factory().NewReflectGetCall(_classSuper, propertyName, _classThis);
            EmitContext().SetOriginal(superProperty, pa.Expression);
            superProperty.Loc = pa.Expression!.Loc;
            return superProperty;
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitElementAccessExpression(Node node)
    {
        var ea = node.AsElementAccessExpression()!;
        if (IsSuperProperty(node) && _classThis is not null && _classSuper is not null)
        {
            var propertyName = Visitor().VisitNode(ea.ArgumentExpression);
            var superProperty = Factory().NewReflectGetCall(_classSuper, propertyName!, _classThis);
            EmitContext().SetOriginal(superProperty, ea.Expression);
            superProperty.Loc = ea.Expression!.Loc;
            return superProperty;
        }

        return Visitor().VisitEachChild(node);
    }

    // 8.6.3 RS: IteratorBindingInitialization
    //
    //	SingleNameBinding : BindingIdentifier Initializer?
    //	  ...
    //	  5. If |Initializer| is present and _v_ is *undefined*, then
    //	     a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    //	        i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    //	  ...
    //
    // 14.3.3.3 RS: KeyedBindingInitialization
    //
    //	SingleNameBinding : BindingIdentifier Initializer?
    //	  ...
    //	  4. If |Initializer| is present and _v_ is *undefined*, then
    //	     a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    //	        i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    //	  ...
    private Node? VisitParameterDeclaration(Node node)
    {
        var paramNode = node;
        if (IsNamedEvaluationAnd(EmitContext(), paramNode, IsAnonymousClassNeedingAssignedName))
        {
            paramNode = TransformNamedEvaluation(EmitContext(), paramNode, CanIgnoreEmptyStringLiteralInAssignedName(paramNode.Initializer()), "");
        }

        var pd = paramNode.AsParameterDeclaration()!;
        var updated = Factory().UpdateParameterDeclaration(
            pd,
            null, // modifiers - strip all modifiers (including decorators)
            pd.DotDotDotToken,
            Visitor().VisitNode(pd.Name()),
            null, // questionToken
            null, // type
            Visitor().VisitNode(pd.Initializer));
        if (!ReferenceEquals(updated, paramNode))
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitContext().SetCommentRange(updated, paramNode.Loc);
            var newLoc = MoveRangePastModifiers(paramNode);
            updated.Loc = newLoc;
            EmitContext().SetSourceMapRange(updated, newLoc);
            EmitContext().SetEmitFlags(updated.Name()!, EmitFlags.NoTrailingSourceMap);
        }

        return updated;
    }

    /// <summary>
    /// visitNamedEvaluationSite replaces Strada's visitPropertyAssignment, visitVariableDeclaration,
    /// and visitBindingElement, which all share the same logic.
    ///
    /// 13.2.5.5 RS: PropertyDefinitionEvaluation (PropertyAssignment)
    ///
    ///	PropertyAssignment : PropertyName `:` AssignmentExpression
    ///	  ...
    ///	  5. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true* and _isProtoSetter_ is *false*, then
    ///	     a. Let _popValue_ be ? NamedEvaluation of |AssignmentExpression| with argument _propKey_.
    ///	  ...
    ///
    /// 14.3.1.2 RS: Evaluation (VariableDeclaration)
    ///
    ///	LexicalBinding : BindingIdentifier Initializer
    ///	  ...
    ///	  3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///	     a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///	  ...
    ///
    /// 14.3.2.1 RS: Evaluation (VariableDeclaration)
    ///
    ///	VariableDeclaration : BindingIdentifier Initializer
    ///	  ...
    ///	  3. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///	     a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///	  ...
    ///
    /// 8.6.3 RS: IteratorBindingInitialization (BindingElement)
    ///
    ///	SingleNameBinding : BindingIdentifier Initializer?
    ///	  ...
    ///	  5. If |Initializer| is present and _v_ is *undefined*, then
    ///	     a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///	        i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///	  ...
    ///
    /// 14.3.3.3 RS: KeyedBindingInitialization (BindingElement)
    ///
    ///	SingleNameBinding : BindingIdentifier Initializer?
    ///	  ...
    ///	  4. If |Initializer| is present and _v_ is *undefined*, then
    ///	     a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
    ///	        i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _bindingId_.
    ///	  ...
    /// </summary>
    private Node? VisitNamedEvaluationSite(Node node, Node? classExpr)
    {
        if (IsNamedEvaluationAnd(EmitContext(), node, IsAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node, CanIgnoreEmptyStringLiteralInAssignedName(classExpr), "");
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitForStatement(Node node)
    {
        var f = Factory();
        var forStmt = node.AsForStatement()!;
        return f.UpdateForStatement(
            forStmt,
            _discardedVisitor!.VisitNode(forStmt.Initializer),
            Visitor().VisitNode(forStmt.Condition),
            _discardedVisitor!.VisitNode(forStmt.Incrementor),
            EmitContext().VisitIterationBody(forStmt.Statement!, Visitor()));
    }

    private Node? VisitExpressionStatement(Node node)
    {
        return _discardedVisitor!.VisitEachChild(node);
    }

    private Node? VisitBinaryExpression(Node node, bool discarded)
    {
        var f = Factory();
        var ec = EmitContext();
        var bin = node.AsBinaryExpression()!;

        if (IsDestructuringAssignment(node))
        {
            var left = VisitAssignmentPattern(bin.Left!);
            var right = Visitor().VisitNode(bin.Right);
            return f.UpdateBinaryExpression(bin, null, left, null, bin.OperatorToken, right);
        }

        if (IsAssignmentExpression(node, false))
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

            if (IsNamedEvaluationAnd(ec, node, IsAnonymousClassNeedingAssignedName))
            {
                node = TransformNamedEvaluation(ec, node, CanIgnoreEmptyStringLiteralInAssignedName(bin.Right), "");
                return Visitor().VisitEachChild(node);
            }

            if (IsSuperProperty(bin.Left) && _classThis is not null && _classSuper is not null)
            {
                Node? setterName = null;
                if (IsElementAccessExpression(bin.Left!))
                {
                    setterName = Visitor().VisitNode(bin.Left!.AsElementAccessExpression()!.ArgumentExpression);
                }
                else if (IsPropertyAccessExpression(bin.Left!) && bin.Left!.AsPropertyAccessExpression()!.Name() is { } leftName && IsIdentifier(leftName))
                {
                    setterName = f.NewStringLiteralFromNode(leftName);
                }

                if (setterName is not null)
                {
                    // super.x = ...
                    // super.x += ...
                    // super[x] = ...
                    // super[x] += ...
                    Node? expression = Visitor().VisitNode(bin.Right);
                    if (IsCompoundAssignment(bin.OperatorToken!.Kind))
                    {
                        var getterName = setterName;
                        if (!IsSimpleInlineableExpression(setterName))
                        {
                            getterName = f.NewTempVariable();
                            ec.AddVariableDeclaration(getterName);
                            setterName = f.NewAssignmentExpression(getterName, setterName);
                        }

                        var superPropertyGet = f.NewReflectGetCall(_classSuper, getterName, _classThis);
                        ec.SetOriginal(superPropertyGet, bin.Left!);
                        superPropertyGet.Loc = bin.Left!.Loc;
                        expression = f.AsNodeFactory().NewBinaryExpression(
                            null,
                            superPropertyGet,
                            null,
                            f.NewToken(GetNonAssignmentOperatorForCompoundAssignment(bin.OperatorToken!.Kind)),
                            expression);
                        expression.Loc = node.Loc;
                    }

                    Node? temp = null;
                    if (!discarded)
                    {
                        temp = f.NewTempVariable();
                        ec.AddVariableDeclaration(temp);
                    }

                    if (temp is not null)
                    {
                        expression = f.NewAssignmentExpression(temp, expression!);
                        expression.Loc = node.Loc;
                    }

                    expression = f.NewReflectSetCall(_classSuper, setterName, expression!, _classThis);
                    ec.SetOriginal(expression, node);
                    expression.Loc = node.Loc;
                    if (temp is not null)
                    {
                        expression = f.NewCommaExpression(expression, temp);
                        expression.Loc = node.Loc;
                    }

                    return expression;
                }
            }
        }

        if (bin.OperatorToken!.Kind == Kind.CommaToken)
        {
            var left = _discardedVisitor!.VisitNode(bin.Left);
            Node? right;
            if (discarded)
            {
                right = _discardedVisitor!.VisitNode(bin.Right);
            }
            else
            {
                right = Visitor().VisitNode(bin.Right);
            }

            return f.UpdateBinaryExpression(bin, null, left, null, bin.OperatorToken, right);
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitPreOrPostfixUnaryExpression(Node node, bool discarded)
    {
        var f = Factory();
        var ec = EmitContext();

        Kind @operator;
        Node? operandNode;
        if (IsPrefixUnaryExpression(node))
        {
            @operator = node.AsPrefixUnaryExpression()!.Operator;
            operandNode = node.AsPrefixUnaryExpression()!.Operand;
        }
        else
        {
            @operator = node.AsPostfixUnaryExpression()!.Operator;
            operandNode = node.AsPostfixUnaryExpression()!.Operand;
        }

        if (@operator == Kind.PlusPlusToken || @operator == Kind.MinusMinusToken)
        {
            var operand = SkipParentheses(operandNode);
            if (IsSuperProperty(operand) && _classThis is not null && _classSuper is not null)
            {
                Node? setterName = null;
                if (IsElementAccessExpression(operand!))
                {
                    setterName = Visitor().VisitNode(operand!.AsElementAccessExpression()!.ArgumentExpression);
                }
                else if (IsPropertyAccessExpression(operand!) && operand!.AsPropertyAccessExpression()!.Name() is { } operandName && IsIdentifier(operandName))
                {
                    setterName = f.NewStringLiteralFromNode(operandName);
                }

                if (setterName is not null)
                {
                    var getterName = setterName;
                    if (!IsSimpleInlineableExpression(setterName))
                    {
                        getterName = f.NewTempVariable();
                        ec.AddVariableDeclaration(getterName);
                        setterName = f.NewAssignmentExpression(getterName, setterName);
                    }

                    Node expression = f.NewReflectGetCall(_classSuper, getterName, _classThis);
                    ec.SetOriginal(expression, node);
                    expression.Loc = node.Loc;

                    // If the result of this expression is discarded (i.e., it's in a position where the result
                    // will be otherwise unused, such as in an expression statement or the left side of a comma), we
                    // don't need to create an extra temp variable to hold the result:
                    //
                    //  source (discarded):
                    //    super.x++;
                    //  generated:
                    //    _a = Reflect.get(_super, "x"), _a++, Reflect.set(_super, "x", _a);
                    //
                    // Above, the temp variable `_a` is used to perform the correct coercion (i.e., number or
                    // bigint). Since the result of the postfix unary is discarded, we don't need to capture the
                    // result of the expression.
                    //
                    //  source (not discarded):
                    //    y = super.x++;
                    //  generated:
                    //    y = (_a = Reflect.get(_super, "x"), _b = _a++, Reflect.set(_super, "x", _a), _b);
                    //
                    // When the result isn't discarded, we introduce a new temp variable (`_b`) to capture the
                    // result of the operation so that we can provide it to `y` when the assignment is complete.
                    Node? temp = null;
                    if (!discarded)
                    {
                        temp = f.NewTempVariable();
                        ec.AddVariableDeclaration(temp);
                    }

                    expression = ExpandPreOrPostfixIncrementOrDecrementExpression(f, ec, node, expression, temp);

                    expression = f.NewReflectSetCall(_classSuper, setterName, expression, _classThis);
                    ec.SetOriginal(expression, node);
                    expression.Loc = node.Loc;

                    if (temp is not null)
                    {
                        expression = f.NewCommaExpression(expression, temp);
                        expression.Loc = node.Loc;
                    }

                    return expression;
                }
            }
        }

        return Visitor().VisitEachChild(node);
    }

    private (Node? ReferencedName, Node? Name) VisitReferencedPropertyName(Node node)
    {
        if (IsPropertyNameLiteral(node) || IsPrivateIdentifier(node))
        {
            return (Factory().NewStringLiteralFromNode(node), Visitor().VisitNode(node));
        }

        var cpn = node.AsComputedPropertyName()!;
        if (IsPropertyNameLiteral(cpn.Expression) && !IsIdentifier(cpn.Expression!))
        {
            return (Factory().NewStringLiteralFromNode(cpn.Expression!), Visitor().VisitNode(node));
        }

        var referencedName = Factory().NewGeneratedNameForNode(node);
        EmitContext().AddVariableDeclaration(referencedName);

        var key = Factory().NewPropKeyHelper(Visitor().VisitNode(cpn.Expression)!);
        var assignment = Factory().NewAssignmentExpression(referencedName, key);
        var updatedName = Factory().UpdateComputedPropertyName(cpn, InjectPendingExpressions(assignment));
        return (referencedName, updatedName);
    }

    private Node? VisitPropertyName(Node? node)
    {
        if (node is not null && IsComputedPropertyName(node))
        {
            return VisitComputedPropertyName(node);
        }

        return Visitor().VisitNode(node);
    }

    private Node? VisitComputedPropertyName(Node node)
    {
        var cpn = node.AsComputedPropertyName()!;
        var expression = Visitor().VisitNode(cpn.Expression);
        if (!IsSimpleInlineableExpression(expression!))
        {
            expression = InjectPendingExpressions(expression);
        }

        return Factory().UpdateComputedPropertyName(cpn, expression);
    }

    private Node? VisitDestructuringAssignmentTarget(Node node)
    {
        if (IsObjectLiteralExpression(node) || IsArrayLiteralExpression(node))
        {
            return VisitAssignmentPattern(node);
        }

        if (IsSuperProperty(node) && _classThis is not null && _classSuper is not null)
        {
            var f = Factory();
            var ec = EmitContext();
            Node? propertyName = null;
            if (IsElementAccessExpression(node))
            {
                propertyName = Visitor().VisitNode(node.AsElementAccessExpression()!.ArgumentExpression);
            }
            else if (IsPropertyAccessExpression(node) && node.AsPropertyAccessExpression()!.Name() is { } paName && IsIdentifier(paName))
            {
                propertyName = f.NewStringLiteralFromNode(paName);
            }

            if (propertyName is not null)
            {
                var paramName = f.NewTempVariable();
                var expression = f.NewAssignmentTargetWrapper(
                    paramName,
                    f.NewReflectSetCall(
                        _classSuper,
                        propertyName,
                        paramName,
                        _classThis));
                ec.SetOriginal(expression, node);
                expression.Loc = node.Loc;
                return expression;
            }
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitAssignmentElement(Node node)
    {
        // 13.15.5.5 RS: IteratorDestructuringAssignmentEvaluation
        //   AssignmentElement : DestructuringAssignmentTarget Initializer?
        //     ...
        //     4. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
        //           i. Let _v_ be ? NamedEvaluation of |Initializer| with argument _lref_.[[ReferencedName]].
        //     ...
        if (IsAssignmentExpression(node, true /*excludeCompoundAssignment*/))
        {
            var f = Factory();
            var bin = node.AsBinaryExpression()!;
            if (IsNamedEvaluationAnd(EmitContext(), node, IsAnonymousClassNeedingAssignedName))
            {
                node = TransformNamedEvaluation(EmitContext(), node, CanIgnoreEmptyStringLiteralInAssignedName(bin.Right), "");
                bin = node.AsBinaryExpression()!;
            }

            var assignmentTarget = VisitDestructuringAssignmentTarget(bin.Left!);
            var initializer = Visitor().VisitNode(bin.Right);
            return f.UpdateBinaryExpression(bin, null, assignmentTarget, null, bin.OperatorToken, initializer);
        }

        return VisitDestructuringAssignmentTarget(node);
    }

    private Node? VisitAssignmentRestElement(Node node)
    {
        var se = node.AsSpreadElement()!;
        if (IsLeftHandSideExpression(se.Expression))
        {
            var f = Factory();
            var expression = VisitDestructuringAssignmentTarget(se.Expression!);
            return f.UpdateSpreadElement(se, expression);
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitArrayAssignmentElement(Node node)
    {
        Debug.Assert(IsArrayBindingOrAssignmentElement(node));
        if (IsSpreadElement(node))
        {
            return VisitAssignmentRestElement(node);
        }

        if (!IsOmittedExpression(node))
        {
            return VisitAssignmentElement(node);
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitAssignmentPropertyNode(Node node)
    {
        // AssignmentProperty : PropertyName `:` AssignmentElement
        // AssignmentElement : DestructuringAssignmentTarget Initializer?

        // 13.15.5.6 RS: KeyedDestructuringAssignmentEvaluation
        //   AssignmentElement : DestructuringAssignmentTarget Initializer?
        //     ...
        //     3. If |Initializer| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousfunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
        //           i. Let _rhsValue_ be ? NamedEvaluation of |Initializer| with argument _lref_.[[ReferencedName]].
        //     ...

        var f = Factory();
        var pa = node.AsPropertyAssignment()!;
        var name = Visitor().VisitNode(pa.Name());
        if (IsAssignmentExpression(pa.Initializer, true /*excludeCompoundAssignment*/))
        {
            var assignmentElement = VisitAssignmentElement(pa.Initializer!);
            return f.UpdatePropertyAssignment(pa, null, name, null, null, assignmentElement);
        }

        if (IsLeftHandSideExpression(pa.Initializer))
        {
            var assignmentElement = VisitDestructuringAssignmentTarget(pa.Initializer!);
            return f.UpdatePropertyAssignment(pa, null, name, null, null, assignmentElement);
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitShorthandAssignmentProperty(Node node)
    {
        // AssignmentProperty : IdentifierReference Initializer?

        // 13.15.5.3 RS: PropertyDestructuringAssignmentEvaluation
        //   AssignmentProperty : IdentifierReference Initializer?
        //     ...
        //     4. If |Initializer?| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _P_.
        //     ...
        if (IsNamedEvaluationAnd(EmitContext(), node, IsAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(
                EmitContext(),
                node,
                CanIgnoreEmptyStringLiteralInAssignedName(node.AsShorthandPropertyAssignment()!.ObjectAssignmentInitializer),
                "");
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitAssignmentRestProperty(Node node)
    {
        var sa = node.AsSpreadAssignment()!;
        if (IsLeftHandSideExpression(sa.Expression))
        {
            var f = Factory();
            var expression = VisitDestructuringAssignmentTarget(sa.Expression!);
            return f.UpdateSpreadAssignment(sa, expression);
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitObjectAssignmentElement(Node node)
    {
        Debug.Assert(IsObjectBindingOrAssignmentElement(node));
        if (IsSpreadAssignment(node))
        {
            return VisitAssignmentRestProperty(node);
        }

        if (IsShorthandPropertyAssignment(node))
        {
            return VisitShorthandAssignmentProperty(node);
        }

        if (IsPropertyAssignment(node))
        {
            return VisitAssignmentPropertyNode(node);
        }

        return Visitor().VisitEachChild(node);
    }

    private Node? VisitAssignmentPattern(Node node)
    {
        var f = Factory();
        if (IsArrayLiteralExpression(node))
        {
            var ale = node.AsArrayLiteralExpression()!;
            var elements = _arrayAssignmentVisitor!.VisitNodes(ale.Elements);
            return f.UpdateArrayLiteralExpression(ale, elements, ale.MultiLine);
        }

        var ole = node.AsObjectLiteralExpression()!;
        var properties = _objectAssignmentVisitor!.VisitNodes(ole.Properties);
        return f.UpdateObjectLiteralExpression(ole, properties, ole.MultiLine);
    }

    private Node? VisitExportAssignment(Node node)
    {
        // 16.2.3.7 RS: Evaluation
        //   ExportDeclaration : `export` `default` AssignmentExpression `;`
        //     1. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |AssignmentExpression| with argument `"default"`.
        //     ...
        return VisitNamedEvaluationSite(node, node.Expression());
    }

    private Node? VisitParenthesizedExpression(Node node, bool discarded)
    {
        // 8.4.5 RS: NamedEvaluation
        //   ParenthesizedExpression : `(` Expression `)`
        //     ...
        //     2. Return ? NamedEvaluation of |Expression| with argument _name_.

        var f = Factory();
        var pe = node.AsParenthesizedExpression()!;
        Node? expression;
        if (discarded)
        {
            expression = _discardedVisitor!.VisitNode(pe.Expression);
        }
        else
        {
            expression = Visitor().VisitNode(pe.Expression);
        }

        return f.UpdateParenthesizedExpression(pe, expression);
    }

    private Node? VisitPartiallyEmittedExpression(Node node, bool discarded)
    {
        // Emulates 8.4.5 RS: NamedEvaluation
        var pe = node.AsPartiallyEmittedExpression()!;
        Node? expression;
        if (discarded)
        {
            expression = _discardedVisitor!.VisitNode(pe.Expression);
        }
        else
        {
            expression = Visitor().VisitNode(pe.Expression);
        }

        return Factory().UpdatePartiallyEmittedExpression(pe, expression);
    }

    /// <summary>
    /// prependExpressions prepends a list of expressions before a target expression, preserving
    /// parenthesization. If expression is nil, the pending expressions are inlined alone.
    /// </summary>
    private Node? PrependExpressions(List<Node>? pending, Node? expression)
    {
        var f = Factory();
        if (pending is null || pending.Count == 0)
        {
            return expression;
        }

        if (expression is null)
        {
            return f.InlineExpressions(pending.ToArray());
        }

        if (IsParenthesizedExpression(expression))
        {
            var pe = expression.AsParenthesizedExpression()!;
            var exprs = new Node[pending.Count + 1];
            pending.CopyTo(exprs);
            exprs[pending.Count] = pe.Expression!;
            return f.UpdateParenthesizedExpression(pe, f.InlineExpressions(exprs));
        }

        var exprs2 = new Node[pending.Count + 1];
        pending.CopyTo(exprs2);
        exprs2[pending.Count] = expression;
        return f.InlineExpressions(exprs2);
    }

    private Node? InjectPendingExpressions(Node? expression)
    {
        var result = PrependExpressions(_pendingExpressions, expression);
        Debug.Assert(result is not null);
        if (!ReferenceEquals(result, expression))
        {
            _pendingExpressions = null;
        }

        return result;
    }

    private Node? InjectPendingInitializers(ClassInfo ci, bool isStatic, Node? expression)
    {
        var pending = isStatic ? ci.PendingStaticInitializers : ci.PendingInstanceInitializers;
        var result = PrependExpressions(pending, expression);
        if (!ReferenceEquals(result, expression))
        {
            if (isStatic)
            {
                ci.PendingStaticInitializers = null;
            }
            else
            {
                ci.PendingInstanceInitializers = null;
            }
        }

        return result;
    }

    /// <summary>Transforms all of the decorators for a declaration into an array of expressions.</summary>
    private List<Node>? TransformAllDecoratorsOfDeclaration(Node[]? decorators)
    {
        if (decorators is null || decorators.Length == 0)
        {
            return null;
        }

        var result = new List<Node>(decorators.Length);
        foreach (var d in decorators)
        {
            result.Add(TransformDecorator(d));
        }

        return result;
    }

    /// <summary>Transforms a decorator into an expression.</summary>
    private Node TransformDecorator(Node decorator)
    {
        var expression = Visitor().VisitNode(decorator.AsDecorator()!.Expression)!;
        EmitContext().SetEmitFlags(expression, EmitFlags.NoComments);

        // preserve the 'this' binding for an access expression
        var innerExpression = SkipOuterExpressions(expression, OuterExpressionKinds.All);
        if (IsAccessExpression(innerExpression))
        {
            var (target, thisArg) = CreateCallBinding(expression);
            var bindCall = Factory().NewFunctionBindCall(target!, thisArg!, []);
            return Factory().RestoreOuterExpressions(expression, bindCall, OuterExpressionKinds.All);
        }

        return expression;
    }

    private (Node? Target, Node? ThisArg) CreateCallBinding(Node expression)
    {
        var f = Factory();
        var callee = SkipOuterExpressions(expression, OuterExpressionKinds.All);
        if (IsSuperProperty(callee))
        {
            return (callee, f.NewThisExpression());
        }

        if (callee!.Kind == Kind.SuperKeyword)
        {
            return (callee, f.NewThisExpression());
        }

        if ((EmitContext().EmitFlags(callee) & EmitFlags.HelperName) != 0)
        {
            return (callee, f.NewVoidZeroExpression());
        }

        if (IsPropertyAccessExpression(callee))
        {
            var pa = callee.AsPropertyAccessExpression()!;
            if (ShouldBeCapturedInTempVariable(pa.Expression!))
            {
                var thisArg = f.NewTempVariable();
                EmitContext().AddVariableDeclaration(thisArg);
                var assign = f.NewAssignmentExpression(thisArg, pa.Expression!);
                assign.Loc = pa.Expression!.Loc;
                var target = f.NewPropertyAccessExpression(assign, null, pa.Name(), NodeFlags.None);
                target.Loc = callee.Loc;
                return (target, thisArg);
            }

            return (callee, pa.Expression);
        }

        if (IsElementAccessExpression(callee))
        {
            var ea = callee.AsElementAccessExpression()!;
            if (ShouldBeCapturedInTempVariable(ea.Expression!))
            {
                var thisArg = f.NewTempVariable();
                EmitContext().AddVariableDeclaration(thisArg);
                var assign = f.NewAssignmentExpression(thisArg, ea.Expression!);
                assign.Loc = ea.Expression!.Loc;
                var target = f.NewElementAccessExpression(assign, null, ea.ArgumentExpression, NodeFlags.None);
                target.Loc = callee.Loc;
                return (target, thisArg);
            }

            return (callee, ea.Expression);
        }

        return (expression, f.NewVoidZeroExpression());
    }

    private static bool ShouldBeCapturedInTempVariable(Node node)
    {
        // This is a simplified version of the general shouldBeCapturedInTempVariable from
        // nodeFactory with cacheIdentifiers=true, since createCallBinding in this transform
        // always caches identifiers.
        var target = SkipParentheses(node);
        switch (target!.Kind)
        {
            case Kind.Identifier:
                // cacheIdentifiers is always true for this transform's createCallBinding
                return true;
            case Kind.ThisKeyword:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.StringLiteral:
                return false;
            default:
                return true;
        }
    }
}

public static partial class EsTransforms
{
    // Go: esdecorator.go:1877 —— 注意 Go 侧对 nil 会 panic（`ast.IsClassExpression(nil)` 解引用 node.Kind），
    // 调用点（isNamedEvaluationAnd 的 cb）保证非 nil，所以 C# 形参非空、与 `Func<Node, bool>` 精确匹配
    // （可空形参会触发 CS8622）。
    internal static bool IsAnonymousClassNeedingAssignedName(Node node)
    {
        return IsClassExpression(node) && node.Name() is null && IsDecoratedClassLike(node);
    }

    // Go: esdecorator.go:1885
    // The IIFE produced for `(@dec class {})` will result in an assigned name of the form
    // `var class_1 = class { };`, and thus the empty string cannot be ignored. However, The IIFE
    // produced for `(class { @dec x; })` will not result in an assigned name since it
    // transforms to `return class { };`, and thus the empty string *can* be ignored.
    internal static bool CanIgnoreEmptyStringLiteralInAssignedName(Node? node)
    {
        if (node is null)
        {
            return false;
        }

        var innerExpression = SkipOuterExpressions(node, OuterExpressionKinds.All);
        return IsClassExpression(innerExpression!) && innerExpression!.Name() is null && !ClassOrConstructorParameterIsDecorated(false, innerExpression);
    }
}
