// ported from: internal/transformers/estransforms/esdecorator.go:1244-1463（partialTransformClassElement、appendDecorationStatement）
//              + :1465-1722（visitMethodDeclaration、visitGetAccessorDeclaration、visitSetAccessorDeclaration、
//                visitClassStaticBlockDeclaration、visitPropertyDeclaration）
//              + :2462-2751（createDescriptorMethod、createMethodDescriptorObject、createGetAccessorDescriptorObject、
//                createSetAccessorDescriptorObject、createAccessorPropertyDescriptorObject、createMethodDescriptorForwarder、
//                createGetAccessorDescriptorForwarder、createSetAccessorDescriptorForwarder、createMetadata、
//                createSymbolMetadata、createSymbolMetadataReference、injectClassThisAssignmentIfMissing）
//              （Go 把 partialResult/createDescriptorFunc 声明在文件中部 1232-1242，按"类型集中到分片 1"处理）
//
// 与 Go 的差异（逐条，其余地方与 Go 逐行对应）：
// 1) Go 的 `partialResult` 是返回值类型（值语义），C# 是 class（引用语义）：调用点只做字段赋值后返回，不存在别名共享，
//    语义等价（与 Binder 对 Go 结构体指针的处理一致）。
// 2) `createDescriptorFunc`（Go 的函数类型）→ 具名委托 `CreateDescriptorFunc`（在分片 1 声明）。
// 3) `tx.pendingExpressions = append(tx.pendingExpressions, x)` → `(_pendingExpressions ??= []).Add(x)`；
//    `ci.pendingStaticInitializers` 等同理（Go nil slice ↔ C# null 列表，只判长度）。
// 4) `ast.IsComputedPropertyName(propName)` 在 Go 里对 nil 返回 false；C# 的 `IsComputedPropertyName(Node)` 形参非空，
//    所以写成 `propName is not null && IsComputedPropertyName(propName)`（短路语义与 Go 相同）。
// 5) `printer.EFNoComments`/`printer.EFSingleLine` → `EmitFlags.NoComments`/`EmitFlags.SingleLine`（docs §3.3，枚举成员去前缀）。
// 6) `f.NewESDecorateClassElementContextObject(..., propertyNameExpr, ...)` 的 `nameExpr` 在 Go 侧可以是 nil（当成员无名字时），
//    C# 的形参标注为非空但实现体显式做了 null 判断 → 传 `propertyNameExpr!`（与 Go 的实参一致）。
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
    private PartialResult PartialTransformClassElement(Node member, ClassInfo? ci, CreateDescriptorFunc? createDescriptor)
    {
        var f = Factory();
        var ec = EmitContext();

        if (ci is null)
        {
            var memberModifiers = _modifierVisitor!.VisitModifiers(member.Modifiers());
            EnterName();
            var memberName = VisitPropertyName(member.Name());
            ExitName();
            return new PartialResult { Modifiers = memberModifiers, Name = memberName };
        }

        // Member decorators require privileged access to private names. However, computed property
        // evaluation occurs interspersed with decorator evaluation. This means that if we encounter
        // a computed property name we must inline decorator evaluation.

        // Collect decorators for this member. Decorator expressions evaluate outside the class body,
        // so `this` should NOT be replaced with `_classThis`.
        var savedClassThis = _classThis;
        _classThis = null;
        var memberDecorators = TransformAllDecoratorsOfDeclaration(member.Decorators());
        _classThis = savedClassThis;
        var modifiers = _modifierVisitor!.VisitModifiers(member.Modifiers());

        var result = new PartialResult { Modifiers = modifiers };

        if (memberDecorators is { Count: > 0 })
        {
            var memberDecoratorsName = CreateHelperVariable(member, "decorators");
            var memberDecoratorsArray = f.NewArrayLiteralExpression(
                f.NewNodeList(memberDecorators.ToArray()),
                false);
            var memberDecoratorsAssignment = f.NewAssignmentExpression(memberDecoratorsName, memberDecoratorsArray);
            var mi = new MemberInfo { MemberDecoratorsName = memberDecoratorsName };
            ci.MemberInfos.Set(member, mi);
            (_pendingExpressions ??= []).Add(memberDecoratorsAssignment);

            // 5. Static non-field (method/getter/setter/auto-accessor) element decorators are applied
            // 6. Non-static non-field (method/getter/setter/auto-accessor) element decorators are applied
            // 7. Static field (excl. auto-accessor) element decorators are applied
            // 8. Non-static field (excl. auto-accessor) element decorators are applied

            // Determine decorator kind
            string kind;
            if (IsGetAccessorDeclaration(member))
            {
                kind = "getter";
            }
            else if (IsSetAccessorDeclaration(member))
            {
                kind = "setter";
            }
            else if (IsMethodDeclaration(member))
            {
                kind = "method";
            }
            else if (IsAutoAccessorPropertyDeclaration(member))
            {
                kind = "accessor";
            }
            else if (IsPropertyDeclaration(member))
            {
                kind = "field";
            }
            else
            {
                Debug.Fail("Unexpected class element kind.");
                kind = "";
            }

            // Determine the property name for the context
            var propertyNameComputed = false;
            Node? propertyNameExpr = null;
            if (member.Name() is not null && (IsIdentifier(member.Name()!) || IsPrivateIdentifier(member.Name()!)))
            {
                propertyNameComputed = false;
                propertyNameExpr = member.Name();
            }
            else if (member.Name() is not null && IsPropertyNameLiteral(member.Name()))
            {
                propertyNameComputed = true;
                propertyNameExpr = f.NewStringLiteralFromNode(member.Name()!);
            }
            else if (member.Name() is not null && IsComputedPropertyName(member.Name()!))
            {
                var cpn = member.Name()!.AsComputedPropertyName()!;
                if (IsPropertyNameLiteral(cpn.Expression) && !IsIdentifier(cpn.Expression!))
                {
                    propertyNameComputed = true;
                    propertyNameExpr = f.NewStringLiteralFromNode(cpn.Expression!);
                }
                else
                {
                    EnterName();
                    (result.ReferencedName, result.Name) = VisitReferencedPropertyName(member.Name()!);
                    ExitName();
                    propertyNameComputed = true;
                    propertyNameExpr = result.ReferencedName;
                }
            }

            var contextObj = f.NewESDecorateClassElementContextObject(
                kind,
                propertyNameComputed,
                propertyNameExpr!,
                IsStatic(member),
                member.Name() is not null && IsPrivateIdentifier(member.Name()!),
                // 15.7.3 CreateDecoratorAccessObject (kind, name)
                // 2. If _kind_ is ~field~, ~method~, ~accessor~, or ~getter~, then ...
                IsPropertyDeclaration(member) || IsGetAccessorDeclaration(member) || IsMethodDeclaration(member),
                // 3. If _kind_ is ~field~, ~accessor~, or ~setter~, then ...
                IsPropertyDeclaration(member) || IsSetAccessorDeclaration(member),
                ci.MetadataReference!);

            if (IsMethodOrAccessor(member))
            {
                // produces (public elements):
                //   __esDecorate(this, null, _static_member_decorators, { kind: "method", name: "...", static: true, private: false, access: { ... } }, _staticExtraInitializers);
                //   __esDecorate(this, null, _member_decorators, { kind: "method", name: "...", static: false, private: false, access: { ... } }, _instanceExtraInitializers);
                //   __esDecorate(this, null, _static_member_decorators, { kind: "getter", name: "...", static: true, private: false, access: { ... } }, _staticExtraInitializers);
                //   __esDecorate(this, null, _member_decorators, { kind: "getter", name: "...", static: false, private: false, access: { ... } }, _instanceExtraInitializers);
                //   __esDecorate(this, null, _static_member_decorators, { kind: "setter", name: "...", static: true, private: false, access: { ... } }, _staticExtraInitializers);
                //   __esDecorate(this, null, _member_decorators, { kind: "setter", name: "...", static: false, private: false, access: { ... } }, _instanceExtraInitializers);
                //
                // produces (private elements):
                //   __esDecorate(this, _static_member_descriptor = { value() { ... } }, _static_member_decorators, { kind: "method", name: "...", static: true, private: true, access: { ... } }, _staticExtraInitializers);
                //   __esDecorate(this, _member_descriptor = { value() { ... } }, _member_decorators, { kind: "method", name: "...", static: false, private: true, access: { ... } }, _instanceExtraInitializers);
                //   __esDecorate(this, _static_member_descriptor = { get() { ... } }, _static_member_decorators, { kind: "getter", name: "...", static: true, private: true, access: { ... } }, _staticExtraInitializers);
                //   __esDecorate(this, _member_descriptor = { get() { ... } }, _member_decorators, { kind: "getter", name: "...", static: false, private: true, access: { ... } }, _instanceExtraInitializers);
                //   __esDecorate(this, _static_member_descriptor = { set() { ... } }, _static_member_decorators, { kind: "setter", name: "...", static: true, private: true, access: { ... } }, _staticExtraInitializers);
                //   __esDecorate(this, _member_descriptor = { set() { ... } }, _member_decorators, { kind: "setter", name: "...", static: false, private: true, access: { ... } }, _instanceExtraInitializers);
                var methodExtraInitializersName = ci.InstanceMethodExtraInitializersName;
                if (IsStatic(member))
                {
                    methodExtraInitializersName = ci.StaticMethodExtraInitializersName;
                }

                Debug.Assert(methodExtraInitializersName is not null, "methodExtraInitializersName should be defined");

                Node descriptorArg;
                if (IsPrivateIdentifierClassElementDeclaration(member) && createDescriptor is not null)
                {
                    // For private members, extract the method/accessor body into a descriptor object.
                    // Filter modifiers to only keep async.
                    var asyncMods = _asyncOnlyModifierVisitor!.VisitModifiers(modifiers);
                    var descriptor = createDescriptor(member, asyncMods);
                    mi.MemberDescriptorName = CreateHelperVariable(member, "descriptor");
                    result.DescriptorName = mi.MemberDescriptorName;
                    descriptorArg = f.NewAssignmentExpression(mi.MemberDescriptorName, descriptor!);
                }
                else
                {
                    descriptorArg = f.NewToken(Kind.NullKeyword);
                }

                var esDecorateExpr = f.NewESDecorateHelper(
                    f.NewThisExpression(),
                    descriptorArg,
                    memberDecoratorsName,
                    contextObj,
                    f.NewToken(Kind.NullKeyword),
                    methodExtraInitializersName!);
                var esDecorateStatement = f.NewExpressionStatement(esDecorateExpr);
                ec.SetSourceMapRange(esDecorateStatement, MoveRangePastDecorators(member));
                AppendDecorationStatement(ci, member, esDecorateStatement);
            }
            else if (IsPropertyDeclaration(member))
            {
                mi.MemberInitializersName = CreateHelperVariable(member, "initializers");
                mi.MemberExtraInitializersName = CreateHelperVariable(member, "extraInitializers");
                result.InitializersName = mi.MemberInitializersName;
                result.ExtraInitializersName = mi.MemberExtraInitializersName;
                if (IsStatic(member))
                {
                    result.ThisArg = ci.ClassThis;
                }

                Node ctorArg;
                if (IsAutoAccessorPropertyDeclaration(member))
                {
                    ctorArg = f.NewThisExpression();
                }
                else
                {
                    ctorArg = f.NewToken(Kind.NullKeyword);
                }

                Node descriptorArg;
                if (IsPrivateIdentifierClassElementDeclaration(member) && HasAccessorModifier(member) && createDescriptor is not null)
                {
                    var descriptor = createDescriptor(member, null);
                    mi.MemberDescriptorName = CreateHelperVariable(member, "descriptor");
                    result.DescriptorName = mi.MemberDescriptorName;
                    descriptorArg = f.NewAssignmentExpression(mi.MemberDescriptorName, descriptor!);
                }
                else
                {
                    descriptorArg = f.NewToken(Kind.NullKeyword);
                }

                // produces:
                //   __esDecorate(null, null, _static_member_decorators, { kind: "field", name: "...", static: true, private: ..., access: { ... } }, _staticExtraInitializers);
                //   __esDecorate(null, null, _member_decorators, { kind: "field", name: "...", static: false, private: ..., access: { ... } }, _instanceExtraInitializers);
                var esDecorateExpr = f.NewESDecorateHelper(
                    ctorArg,
                    descriptorArg,
                    memberDecoratorsName,
                    contextObj,
                    mi.MemberInitializersName,
                    mi.MemberExtraInitializersName);
                var esDecorateStatement = f.NewExpressionStatement(esDecorateExpr);
                ec.SetSourceMapRange(esDecorateStatement, MoveRangePastDecorators(member));
                AppendDecorationStatement(ci, member, esDecorateStatement);
            }
        }

        if (result.Name is null)
        {
            EnterName();
            result.Name = VisitPropertyName(member.Name());
            ExitName();
        }

        if ((modifiers is null || modifiers.Nodes is not { Length: > 0 }) && (IsMethodDeclaration(member) || IsPropertyDeclaration(member)))
        {
            // Don't emit leading comments on the name for methods and properties without modifiers, otherwise we
            // will end up printing duplicate comments.
            ec.SetEmitFlags(result.Name!, EmitFlags.NoLeadingComments);
        }

        return result;
    }

    /// <summary>
    /// appendDecorationStatement appends an __esDecorate statement to the appropriate
    /// decoration statement list on classInfo based on the member's kind and static-ness.
    /// </summary>
    private static void AppendDecorationStatement(ClassInfo ci, Node member, Node stmt)
    {
        if (IsMethodOrAccessor(member) || IsAutoAccessorPropertyDeclaration(member))
        {
            if (IsStatic(member))
            {
                (ci.StaticNonFieldDecorationStatements ??= []).Add(stmt);
            }
            else
            {
                (ci.NonStaticNonFieldDecorationStatements ??= []).Add(stmt);
            }
        }
        else if (IsPropertyDeclaration(member) && !IsAutoAccessorPropertyDeclaration(member))
        {
            if (IsStatic(member))
            {
                (ci.StaticFieldDecorationStatements ??= []).Add(stmt);
            }
            else
            {
                (ci.NonStaticFieldDecorationStatements ??= []).Add(stmt);
            }
        }
        else
        {
            Debug.Fail("Unexpected class element kind.");
        }
    }

    private Node? VisitMethodDeclaration(Node node)
    {
        EnterClassElement(node);
        var result = PartialTransformClassElement(node, _classInfoStack, CreateMethodDescriptorObject);
        if (result.DescriptorName is not null)
        {
            ExitClassElement();
            return FinishClassElement(CreateMethodDescriptorForwarder(result.Modifiers, result.Name, result.DescriptorName), node);
        }

        var parameters = Visitor().VisitNodes(node.ParameterList());
        var body = Visitor().VisitNode(node.Body());
        ExitClassElement();
        var method = node.AsMethodDeclaration()!;
        return FinishClassElement(
            Factory().UpdateMethodDeclaration(method, result.Modifiers, method.AsteriskToken, result.Name, null, null, parameters, null, null, body),
            node);
    }

    private Node? VisitGetAccessorDeclaration(Node node)
    {
        EnterClassElement(node);
        var result = PartialTransformClassElement(node, _classInfoStack, CreateGetAccessorDescriptorObject);
        if (result.DescriptorName is not null)
        {
            ExitClassElement();
            return FinishClassElement(CreateGetAccessorDescriptorForwarder(result.Modifiers, result.Name, result.DescriptorName), node);
        }

        var parameters = Visitor().VisitNodes(node.ParameterList());
        var body = Visitor().VisitNode(node.Body());
        ExitClassElement();
        var accessor = node.AsGetAccessorDeclaration()!;
        return FinishClassElement(
            Factory().UpdateGetAccessorDeclaration(accessor, result.Modifiers, result.Name, null, parameters, null, null, body),
            node);
    }

    private Node? VisitSetAccessorDeclaration(Node node)
    {
        EnterClassElement(node);
        var result = PartialTransformClassElement(node, _classInfoStack, CreateSetAccessorDescriptorObject);
        if (result.DescriptorName is not null)
        {
            ExitClassElement();
            return FinishClassElement(CreateSetAccessorDescriptorForwarder(result.Modifiers, result.Name, result.DescriptorName), node);
        }

        var parameters = Visitor().VisitNodes(node.ParameterList());
        var body = Visitor().VisitNode(node.Body());
        ExitClassElement();
        var accessor = node.AsSetAccessorDeclaration()!;
        return FinishClassElement(
            Factory().UpdateSetAccessorDeclaration(accessor, result.Modifiers, result.Name, null, parameters, null, null, body),
            node);
    }

    private Node? VisitClassStaticBlockDeclaration(Node node)
    {
        EnterClassElement(node);
        var f = Factory();

        Node? result;
        if (IsClassNamedEvaluationHelperBlock(EmitContext(), node))
        {
            result = Visitor().VisitEachChild(node);
            // Transfer AssignedName metadata to the new node so isClassNamedEvaluationHelperBlock
            // can still find it after visiting (visiting may create a new node when this->_classThis)
            var assignedName = EmitContext().AssignedName(node);
            if (assignedName is not null && !ReferenceEquals(result, node))
            {
                EmitContext().SetAssignedName(result!, assignedName);
            }
        }
        else if (IsClassThisAssignmentBlock(EmitContext(), node))
        {
            var savedClassThis = _classThis;
            _classThis = null;
            result = Visitor().VisitEachChild(node);
            _classThis = savedClassThis;
        }
        else
        {
            // Use a nested variable environment so temp vars generated during static block
            // content transformation (e.g., super access temps) stay scoped to the static block.
            var ec = EmitContext();
            ec.StartVariableEnvironment();
            result = Visitor().VisitEachChild(node);
            var varStatements = ec.EndVariableEnvironment();
            if (varStatements.Length > 0)
            {
                // Inject var declarations at the start of the static block's body
                var blockBody = result!.AsClassStaticBlockDeclaration()!.Body!.AsBlock()!;
                var newStmts = new List<Node>(varStatements.Length + blockBody.Statements!.Nodes!.Length);
                newStmts.AddRange(varStatements);
                newStmts.AddRange(blockBody.Statements.Nodes);
                result = f.NewClassStaticBlockDeclaration(null, f.NewBlock(f.NewNodeList(newStmts.ToArray()), blockBody.MultiLine));
            }

            if (_classInfoStack is not null)
            {
                _classInfoStack.HasStaticInitializers = true;
                if (_classInfoStack.PendingStaticInitializers is { Count: > 0 })
                {
                    // If we tried to inject the pending initializers into the current block, we might run into
                    // variable name collisions due to sharing this blocks scope. To avoid this, we inject a new
                    // static block that contains the pending initializers that precedes this block.
                    List<Node> stmts = [];
                    foreach (var init in _classInfoStack.PendingStaticInitializers)
                    {
                        var initStmt = f.NewExpressionStatement(init);
                        EmitContext().SetSourceMapRange(initStmt, EmitContext().SourceMapRange(init));
                        stmts.Add(initStmt);
                    }

                    var body = f.NewBlock(f.NewNodeList(stmts.ToArray()), true);
                    var staticBlock = f.NewClassStaticBlockDeclaration(null, body);
                    _classInfoStack.PendingStaticInitializers = null;
                    // Return both the new static block and the original
                    ExitClassElement();
                    return SingleOrMany([staticBlock, result!], Factory());
                }
            }
        }

        ExitClassElement();
        return result;
    }

    private Node? VisitPropertyDeclaration(Node node)
    {
        if (IsNamedEvaluationAnd(EmitContext(), node, IsAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node, CanIgnoreEmptyStringLiteralInAssignedName(node.Initializer()), "");
        }

        EnterClassElement(node);

        // TODO(rbuckton): We support decorating `declare x` fields with legacyDecorators, but we currently don't
        //                 support them with esDecorators. We need to consider whether we will support them in the
        //                 future, and how. For now, these should be elided by the `ts` transform.
        Debug.Assert(!HasSyntacticModifier(node, ModifierFlags.Ambient), "Not yet implemented.");

        // 10.2.1.3 RS: EvaluateBody
        //   Initializer : `=` AssignmentExpression
        //     ...
        //     3. If IsAnonymousFunctionDefinition(|AssignmentExpression|) is *true*, then
        //        a. Let _value_ be ? NamedEvaluation of |Initializer| with argument _functionObject_.[[ClassFieldInitializerName]].
        //     ...

        var f = Factory();
        var ec = EmitContext();

        CreateDescriptorFunc? createDescriptor = null;
        if (HasAccessorModifier(node))
        {
            createDescriptor = CreateAccessorPropertyDescriptorObject;
        }

        var result = PartialTransformClassElement(node, _classInfoStack, createDescriptor);

        ec.StartVariableEnvironment();

        var initializer = Visitor().VisitNode(node.Initializer());
        if (result.InitializersName is not null)
        {
            var thisArg = result.ThisArg ?? f.NewThisExpression();
            initializer ??= f.NewVoidZeroExpression();
            initializer = f.NewRunInitializersHelper(thisArg, result.InitializersName, initializer);
        }

        if (IsStatic(node) && _classInfoStack is not null && initializer is not null)
        {
            _classInfoStack.HasStaticInitializers = true;
        }

        var declarations = ec.EndVariableEnvironment();
        if (declarations.Length > 0)
        {
            var stmts = new Node[declarations.Length + 1];
            declarations.CopyTo(stmts, 0);
            stmts[declarations.Length] = f.NewReturnStatement(initializer!);
            initializer = f.NewImmediatelyInvokedArrowFunction(stmts);
        }

        if (_classInfoStack is not null)
        {
            if (IsStatic(node))
            {
                initializer = InjectPendingInitializers(_classInfoStack, true, initializer);
                if (result.ExtraInitializersName is not null)
                {
                    var thisArg = _classInfoStack.ClassThis ?? f.NewThisExpression();
                    (_classInfoStack.PendingStaticInitializers ??= []).Add(
                        f.NewRunInitializersHelper(thisArg, result.ExtraInitializersName, null));
                }
            }
            else
            {
                initializer = InjectPendingInitializers(_classInfoStack, false, initializer);
                if (result.ExtraInitializersName is not null)
                {
                    (_classInfoStack.PendingInstanceInitializers ??= []).Add(
                        f.NewRunInitializersHelper(f.NewThisExpression(), result.ExtraInitializersName, null));
                }
            }
        }

        ExitClassElement();

        if (HasAccessorModifier(node) && result.DescriptorName is not null)
        {
            // given:
            //  accessor #x = 1;
            //
            // emits:
            //  static {
            //      _esDecorate(null, _private_x_descriptor = { get() { return this.#x_1; }, set(value) { this.#x_1 = value; } }, ...)
            //  }
            //  ...
            //  #x_1 = 1;
            //  get #x() { return _private_x_descriptor.get.call(this); }
            //  set #x(value) { _private_x_descriptor.set.call(this, value); }

            var commentRange = ec.CommentRange(node);
            var sourceMapRange = ec.SourceMapRange(node);

            // Since we're creating two declarations where there was previously one, cache
            // the expression for any computed property names.
            var propName = node.Name();
            var getterName = result.Name;
            var setterName = result.Name;
            if (propName is not null && IsComputedPropertyName(propName) && !IsSimpleInlineableExpression(propName.Expression()!))
            {
                var cacheAssignment = FindComputedPropertyNameCacheAssignment(ec, propName);
                if (cacheAssignment is not null)
                {
                    getterName = f.UpdateComputedPropertyName(propName.AsComputedPropertyName()!, Visitor().VisitNode(propName.Expression()));
                    setterName = f.UpdateComputedPropertyName(propName.AsComputedPropertyName()!, cacheAssignment.Left);
                }
                else
                {
                    var temp = f.NewTempVariable();
                    ec.SetSourceMapRange(temp, propName.Expression()!.Loc);
                    ec.AddVariableDeclaration(temp);
                    var expression = Visitor().VisitNode(propName.Expression());
                    var assignment = f.NewAssignmentExpression(temp, expression!);
                    ec.SetSourceMapRange(assignment, propName.Expression()!.Loc);
                    getterName = f.UpdateComputedPropertyName(propName.AsComputedPropertyName()!, assignment);
                    setterName = f.UpdateComputedPropertyName(propName.AsComputedPropertyName()!, temp);
                }
            }

            var modifiersWithoutAccessor = _accessorStrippingModifierVisitor!.VisitModifiers(result.Modifiers);

            var backingField = CreateAccessorPropertyBackingField(f, node.AsPropertyDeclaration()!, modifiersWithoutAccessor, initializer);
            ec.SetOriginal(backingField, node);
            ec.SetEmitFlags(backingField, EmitFlags.NoComments);
            ec.SetSourceMapRange(backingField, sourceMapRange);
            ec.SetSourceMapRange(backingField.AsPropertyDeclaration()!.Name()!, ec.SourceMapRange(node.Name()!));

            var getter = CreateGetAccessorDescriptorForwarder(modifiersWithoutAccessor, getterName, result.DescriptorName);
            ec.SetOriginal(getter, node);
            ec.SetCommentRange(getter, commentRange);
            ec.SetSourceMapRange(getter, sourceMapRange);

            var setter = CreateSetAccessorDescriptorForwarder(modifiersWithoutAccessor, setterName, result.DescriptorName);
            ec.SetOriginal(setter, node);
            ec.SetEmitFlags(setter, EmitFlags.NoComments);
            ec.SetSourceMapRange(setter, sourceMapRange);

            return SingleOrMany([backingField, getter, setter], f);
        }

        var prop = node.AsPropertyDeclaration()!;
        return FinishClassElement(
            f.UpdatePropertyDeclaration(prop, result.Modifiers, result.Name, null, null, initializer),
            node);
    }

    /// <summary>
    /// Creates a "value", "get", or "set" method for a pseudo-PropertyDescriptor object created for
    /// a private element.
    /// </summary>
    private Node CreateDescriptorMethod(
        Node original,
        Node? name, // PrivateIdentifier
        ModifierList? modifiers,
        Node? asteriskToken,
        string kind,
        NodeList? parameters,
        Node? body)
    {
        var f = Factory();
        var ec = EmitContext();

        body ??= f.NewBlock(f.NewNodeList([]), false);

        var funcExpr = f.NewFunctionExpression(
            modifiers,
            asteriskToken,
            null, // name
            null, // typeParameters
            parameters,
            null, // type
            null, // fullSignature
            body);
        ec.SetOriginal(funcExpr, original);
        ec.SetSourceMapRange(funcExpr, MoveRangePastDecorators(original));
        ec.SetEmitFlags(funcExpr, EmitFlags.NoComments);

        var prefix = "";
        if (kind is "get" or "set")
        {
            prefix = kind;
        }

        var functionName = f.NewStringLiteralFromNode(name!);
        var namedFunction = f.NewSetFunctionNameHelper(funcExpr, functionName, prefix);

        var method = f.NewPropertyAssignment(null, f.NewIdentifier(kind), null, null, namedFunction);
        ec.SetOriginal(method, original);
        ec.SetSourceMapRange(method, MoveRangePastDecorators(original));
        ec.SetEmitFlags(method, EmitFlags.NoComments);
        return method;
    }

    /// <summary>Creates a pseudo-PropertyDescriptor object used when decorating a private MethodDeclaration.</summary>
    private Node CreateMethodDescriptorObject(Node member, ModifierList? modifiers)
    {
        var f = Factory();
        var parameters = Visitor().VisitNodes(member.ParameterList());
        var body = Visitor().VisitNode(member.Body());
        var method = member.AsMethodDeclaration()!;
        return f.NewObjectLiteralExpression(
            f.NewNodeList([
                CreateDescriptorMethod(member, member.Name(), modifiers, method.AsteriskToken, "value", parameters, body),
            ]),
            false);
    }

    /// <summary>Creates a pseudo-PropertyDescriptor object used when decorating a private GetAccessor.</summary>
    private Node CreateGetAccessorDescriptorObject(Node member, ModifierList? modifiers)
    {
        var f = Factory();
        var body = Visitor().VisitNode(member.Body());
        return f.NewObjectLiteralExpression(
            f.NewNodeList([
                CreateDescriptorMethod(member, member.Name(), modifiers, null, "get", f.NewNodeList([]), body),
            ]),
            false);
    }

    /// <summary>Creates a pseudo-PropertyDescriptor object used when decorating a private SetAccessor.</summary>
    private Node CreateSetAccessorDescriptorObject(Node member, ModifierList? modifiers)
    {
        var f = Factory();
        var parameters = Visitor().VisitNodes(member.ParameterList());
        var body = Visitor().VisitNode(member.Body());
        return f.NewObjectLiteralExpression(
            f.NewNodeList([
                CreateDescriptorMethod(member, member.Name(), modifiers, null, "set", parameters, body),
            ]),
            false);
    }

    /// <summary>
    /// Creates a pseudo-PropertyDescriptor object used when decorating a private auto-accessor PropertyDeclaration.
    /// The descriptor contains get/set methods that access the generated backing field.
    /// </summary>
    private Node CreateAccessorPropertyDescriptorObject(Node member, ModifierList? _)
    {
        //  {
        //      get() { return this.${privateName}; },
        //      set(value) { this.${privateName} = value; },
        //  }
        var f = Factory();
        var backingFieldName = f.NewGeneratedPrivateNameForNodeEx(member.Name()!, new AutoGenerateOptions { Suffix = "_accessor_storage" });
        return f.NewObjectLiteralExpression(
            f.NewNodeList([
                CreateDescriptorMethod(
                    member, member.Name(), null, null, "get",
                    f.NewNodeList([]),
                    f.NewBlock(f.NewNodeList([
                        f.NewReturnStatement(
                            f.NewPropertyAccessExpression(f.NewThisExpression(), null, backingFieldName, NodeFlags.None)),
                    ]), false)),
                CreateDescriptorMethod(
                    member, member.Name(), null, null, "set",
                    f.NewNodeList([
                        f.NewParameterDeclaration(null, null, f.NewIdentifier("value"), null, null, null),
                    ]),
                    f.NewBlock(f.NewNodeList([
                        f.NewExpressionStatement(
                            f.NewAssignmentExpression(
                                f.NewPropertyAccessExpression(f.NewThisExpression(), null, backingFieldName, NodeFlags.None),
                                f.NewIdentifier("value"))),
                    ]), false)),
            ]),
            false);
    }

    /// <summary>Creates a MethodDeclaration that forwards its invocation to a PropertyDescriptor object.</summary>
    private Node CreateMethodDescriptorForwarder(ModifierList? modifiers, Node? name, Node descriptorName)
    {
        var f = Factory();
        var staticOnly = _staticOnlyModifierVisitor!.VisitModifiers(modifiers);
        return f.NewGetAccessorDeclaration(
            staticOnly,
            name,
            null, // typeParameters
            f.NewNodeList([]),
            null, // type
            null, // fullSignature
            f.NewBlock(f.NewNodeList([
                f.NewReturnStatement(
                    f.NewPropertyAccessExpression(descriptorName, null, f.NewIdentifier("value"), NodeFlags.None)),
            ]), false));
    }

    /// <summary>Creates a GetAccessor that forwards its invocation to a PropertyDescriptor object.</summary>
    private Node CreateGetAccessorDescriptorForwarder(ModifierList? modifiers, Node? name, Node descriptorName)
    {
        var f = Factory();
        var staticOnly = _staticOnlyModifierVisitor!.VisitModifiers(modifiers);
        return f.NewGetAccessorDeclaration(
            staticOnly,
            name,
            null, // typeParameters
            f.NewNodeList([]),
            null, // type
            null, // fullSignature
            f.NewBlock(f.NewNodeList([
                f.NewReturnStatement(
                    f.NewFunctionCallCall(
                        f.NewPropertyAccessExpression(descriptorName, null, f.NewIdentifier("get"), NodeFlags.None),
                        f.NewThisExpression(),
                        [])),
            ]), false));
    }

    /// <summary>Creates a SetAccessor that forwards its invocation to a PropertyDescriptor object.</summary>
    private Node CreateSetAccessorDescriptorForwarder(ModifierList? modifiers, Node? name, Node descriptorName)
    {
        var f = Factory();
        var staticOnly = _staticOnlyModifierVisitor!.VisitModifiers(modifiers);
        return f.NewSetAccessorDeclaration(
            staticOnly,
            name,
            null, // typeParameters
            f.NewNodeList([
                f.NewParameterDeclaration(null, null, f.NewIdentifier("value"), null, null, null),
            ]),
            null, // type
            null, // fullSignature
            f.NewBlock(f.NewNodeList([
                f.NewReturnStatement(
                    f.NewFunctionCallCall(
                        f.NewPropertyAccessExpression(descriptorName, null, f.NewIdentifier("set"), NodeFlags.None),
                        f.NewThisExpression(),
                        [f.NewIdentifier("value")])),
            ]), false));
    }

    private Node CreateMetadata(Node name, Node? classSuper)
    {
        var f = Factory();

        var superMetadata = classSuper is not null
            ? CreateSymbolMetadataReference(classSuper)
            : f.NewToken(Kind.NullKeyword);

        var objectCreate = f.NewCallExpression(
            f.NewPropertyAccessExpression(f.NewIdentifier("Object"), null, f.NewIdentifier("create"), NodeFlags.None),
            null, null,
            f.NewNodeList([superMetadata]),
            NodeFlags.None);

        var symbolCheck = f.NewLogicalANDExpression(
            f.NewTypeCheck(f.NewIdentifier("Symbol"), "function"),
            f.NewPropertyAccessExpression(f.NewIdentifier("Symbol"), null, f.NewIdentifier("metadata"), NodeFlags.None));

        var conditional = f.NewConditionalExpression(
            symbolCheck,
            f.NewToken(Kind.QuestionToken),
            objectCreate,
            f.NewToken(Kind.ColonToken),
            f.NewVoidZeroExpression());

        var varDecl = f.NewVariableDeclaration(name, null, null, conditional);
        var varDeclList = f.NewVariableDeclarationList(f.NewNodeList([varDecl]), NodeFlags.Const);
        return f.NewVariableStatement(null, varDeclList);
    }

    private Node CreateSymbolMetadata(Node target, Node value)
    {
        var f = Factory();

        // Object.defineProperty(target, Symbol.metadata, { configurable: true, writable: true, enumerable: true, value })
        var symbolMetadata = f.NewPropertyAccessExpression(f.NewIdentifier("Symbol"), null, f.NewIdentifier("metadata"), NodeFlags.None);

        Node[] descriptorProps = [
            f.NewPropertyAssignment(null, f.NewIdentifier("enumerable"), null, null, f.NewTrueExpression()),
            f.NewPropertyAssignment(null, f.NewIdentifier("configurable"), null, null, f.NewTrueExpression()),
            f.NewPropertyAssignment(null, f.NewIdentifier("writable"), null, null, f.NewTrueExpression()),
            f.NewPropertyAssignment(null, f.NewIdentifier("value"), null, null, value),
        ];
        var descriptor = f.NewObjectLiteralExpression(f.NewNodeList(descriptorProps), false);

        var defineProperty = f.NewCallExpression(
            f.NewPropertyAccessExpression(f.NewIdentifier("Object"), null, f.NewIdentifier("defineProperty"), NodeFlags.None),
            null, null,
            f.NewNodeList([target, symbolMetadata, descriptor]),
            NodeFlags.None);

        var ifStatement = f.NewIfStatement(value, f.NewExpressionStatement(defineProperty), null);
        EmitContext().SetEmitFlags(ifStatement, EmitFlags.SingleLine);
        return ifStatement;
    }

    private Node CreateSymbolMetadataReference(Node classSuper)
    {
        var f = Factory();
        var symbolMetadata = f.NewPropertyAccessExpression(f.NewIdentifier("Symbol"), null, f.NewIdentifier("metadata"), NodeFlags.None);
        var elementAccess = f.NewElementAccessExpression(classSuper, null, symbolMetadata, NodeFlags.None);
        return f.NewBinaryExpression(null, elementAccess, null, f.NewToken(Kind.QuestionQuestionToken), f.NewToken(Kind.NullKeyword));
    }

    private static Node InjectClassThisAssignmentIfMissing(EmitContext ec, NodeFactory f, Node node, Node classThis)
    {
        if (ClassHasClassThisAssignment(ec, node))
        {
            return node;
        }

        // Create: static { _classThis = this; }
        var expression = f.NewAssignmentExpression(classThis, f.NewThisExpression());
        var statement = f.NewExpressionStatement(expression);
        var body = f.NewBlock(f.NewNodeList([statement]), false);
        var staticBlock = f.NewClassStaticBlockDeclaration(null, body);
        ec.SetClassThis(staticBlock, classThis);

        if (node.Name() is not null)
        {
            ec.SetSourceMapRange(statement, node.Name()!.Loc);
        }

        var newMembers = new List<Node>(1 + (node.Members()?.Length ?? 0));
        newMembers.Add(staticBlock);
        newMembers.AddRange(node.Members() ?? []);
        var membersList = f.NewNodeList(newMembers.ToArray());
        membersList.Loc = node.MemberList()!.Loc;

        Node updatedNode;
        if (IsClassDeclaration(node))
        {
            var cd = node.AsClassDeclaration()!;
            updatedNode = f.UpdateClassDeclaration(cd, cd.Modifiers(), cd.Name(), null, cd.HeritageClauses, membersList);
        }
        else
        {
            var ce = node.AsClassExpression()!;
            updatedNode = f.UpdateClassExpression(ce, ce.Modifiers(), ce.Name(), null, ce.HeritageClauses, membersList);
        }

        ec.SetClassThis(updatedNode, classThis);
        return updatedNode;
    }
}
