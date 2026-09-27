// ported from: internal/transformers/estransforms/esdecorator.go:577-1230
//               （transformClassLike、emitMemberInfoDeclarations、visitClassDeclaration、visitClassExpression、
//                prepareConstructor、transformConstructorBodyWorker、visitConstructorDeclaration、finishClassElement）
//
// 与 Go 的差异（逐条，其余地方与 Go 逐行对应）：
// 1) Go 的 `[]*ast.Statement` 局部列表 → `List<Node>`（`append` → `Add`/`AddRange`，`x = append(x, y...)` 且随后被丢弃
//    的临时列表 → `Clear()`）；`make([]*ast.Node, 0, n)` → `new List<Node>(n)`（Go 的预占容量语义）。
// 2) `members.Nodes[:i+1]` / `[i+1:]`（Go 切片视图 + append 拷贝）→ `nodes[..(i+1)]` / `nodes[(i+1)..]`（C# 数组 range，
//    同样是拷贝；C# 数组没有 len<cap 视图，见 docs/architecture.md §3.2）。
// 3) `ec.MergeEnvironment(statements, declarations)` 的 Go 签名收 `[]*ast.Statement`（返回同类型）；C# 是
//    `MergeEnvironment(Node[]?, Node[])`（Printer/EmitContext.cs），所以传 `.ToArray()`、接收 `Node[]?`。
// 4) `SplitStandardPrologue` 在"全是 prologue"时返回 nil 的 rest（与 Go 的 nil slice 一致）→ C# 用 `?? []` 把
//    nil 折叠成空数组（Go 的 nil 切片在 `for range`/`VisitSlice` 里的行为与空切片相同）。
// 5) `FindSuperStatementIndexPath` 返回 `int[]?`（Go 的 nil slice "未找到"）→ C# 用模式 `is { Length: > 0 }` 判定。
// 6) `debug.Assert(ci.classThis != nil)` → `Debug.Assert(ci.ClassThis is not null)`。
// 7) 可空性：`ec.MostOriginal(...)`/`f.InlineExpressions(...)` 等声明为可空返回，但 Go 侧在此处有非 nil 不变量
//    （调用点逐一注释）→ C# 用 `!` 表达同一不变量。
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
    private Node TransformClassLike(Node node)
    {
        var f = Factory();
        var ec = EmitContext();

        ec.StartVariableEnvironment();

        // When a class has class decorators we end up transforming it into a statement that would otherwise give it an
        // assigned name. If the class doesn't have an assigned name, we'll give it an assigned name of `""`.
        if (!ClassHasDeclaredOrExplicitlyAssignedName(ec, node) && ClassOrConstructorParameterIsDecorated(false, node))
        {
            node = InjectClassNamedEvaluationHelperBlockIfMissing(ec, node, f.NewStringLiteral("", TokenFlags.None), null);
        }

        var classReference = f.GetLocalNameEx(node, new NodeFactory.AssignedNameOptions());
        var ci = CreateClassInfo(node);
        List<Node> classDefinitionStatements = [];
        List<Node>? leadingBlockStatements = null;
        List<Node>? trailingBlockStatements = null;
        Node? syntheticConstructor = null;
        NodeList? heritageClauses = null;
        var shouldTransformPrivateStaticElementsInClass = false;

        // 1. Class decorators are evaluated outside the private name scope of the class.
        //
        // - Since class decorators don't have privileged access to private names defined inside the class,
        //   they must be evaluated outside of the class body.
        // - Since a class decorator can replace the class constructor, we must define a variable to keep track
        //   of the mutated class.
        // - Since a class decorator can add extra initializers, we must define a variable to keep track of
        //   extra initializers.
        var classDecorators = TransformAllDecoratorsOfDeclaration(node.Decorators());
        if (classDecorators is not null && classDecorators.Count > 0)
        {
            Debug.Assert(ci.ClassThis is not null);

            ci.ClassDecoratorsName = f.NewUniqueNameEx("_classDecorators", new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
            });
            ci.ClassDescriptorName = f.NewUniqueNameEx("_classDescriptor", new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
            });
            ci.ClassExtraInitializersName = f.NewUniqueNameEx("_classExtraInitializers", new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
            });

            var decoratorsArray = f.NewArrayLiteralExpression(
                f.NewNodeList(classDecorators.ToArray()),
                false);

            classDefinitionStatements.AddRange([
                CreateLet(ci.ClassDecoratorsName, decoratorsArray),
                CreateLet(ci.ClassDescriptorName, null),
                CreateLet(ci.ClassExtraInitializersName, f.NewArrayLiteralExpression(f.NewNodeList(null), false)),
                CreateLet(ci.ClassThis!, null),
            ]);

            if (classDecorators.Count > 0 && ci.HasStaticPrivateClassElements)
            {
                shouldTransformPrivateStaticElementsInClass = true;
                _shouldTransformPrivateStaticElementsInFile = true;
            }
        }

        // 2. ClassHeritage clause is evaluated outside of the private name scope of the class.
        var extendsClause = GetHeritageClause(node, Kind.ExtendsKeyword);
        Node? extendsElement = null;
        if (extendsClause is not null)
        {
            var hc = extendsClause.AsHeritageClause()!;
            if (hc.Types is not null && hc.Types.Nodes is { Length: > 0 })
            {
                extendsElement = hc.Types.Nodes[0];
            }
        }

        Node? extendsExpression = null;
        if (extendsElement is not null)
        {
            extendsExpression = Visitor().VisitNode(extendsElement.AsExpressionWithTypeArguments()!.Expression);
        }

        if (extendsExpression is not null)
        {
            // Rewrite `super` in static initializers so that we can use the correct `this`.
            ci.ClassSuper = f.NewUniqueNameEx("_classSuper", new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel,
            });

            // Ensure we do not give the class or function an assigned name due to the variable by prefixing it
            // with `0, `.
            var unwrapped = SkipOuterExpressions(extendsExpression, OuterExpressionKinds.All);
            var safeExtendsExpression = extendsExpression;
            if ((IsClassExpression(unwrapped!) && unwrapped!.Name() is null) ||
                (IsFunctionExpression(unwrapped!) && unwrapped!.Name() is null) ||
                IsArrowFunction(unwrapped!))
            {
                safeExtendsExpression = f.NewCommaExpression(
                    f.NewNumericLiteral("0", TokenFlags.None),
                    extendsExpression);
            }

            classDefinitionStatements.Add(CreateLet(ci.ClassSuper, safeExtendsExpression));

            var updatedExtendsElement = f.UpdateExpressionWithTypeArguments(extendsElement!.AsExpressionWithTypeArguments()!, ci.ClassSuper, null);
            var hc = extendsClause!.AsHeritageClause()!;
            var updatedExtendsClause = f.UpdateHeritageClause(hc, hc.Token, f.NewNodeList([updatedExtendsElement]));
            heritageClauses = f.NewNodeList([updatedExtendsClause]);
        }

        var renamedClassThis = ci.ClassThis ?? f.NewThisExpression();

        // 3. The name of the class is assigned.
        //
        // If the class did not have a name, the caller should have performed injectClassNamedEvaluationHelperBlockIfMissing
        // prior to calling this function if a name was needed.

        // 4. For each member:
        //    a. Member Decorators are evaluated
        //    b. Computed Property Name is evaluated, if present
        //
        // We visit members in two passes:
        // - The first pass visits methods, accessors, and fields to collect decorators and computed property names.
        // - The second pass visits the constructor to add instance initializers.
        //
        // NOTE: If there are no constructors, but there are instance initializers, a synthetic constructor is added.
        EnterClass(ci);

        (leadingBlockStatements ??= []).Add(CreateMetadata(ci.MetadataReference!, ci.ClassSuper));

        // Since the constructor can appear anywhere in the class body and its transform depends on other class elements,
        // we must first visit all non-constructor members, then visit the constructor, all while maintaining document order.
        var members = _nonConstructorClassElementVisitor!.VisitNodes(node.MemberList());
        members = _constructorClassElementVisitor!.VisitNodes(members);

        // Handle pending expressions (computed property names and decorator evaluations)
        if (_pendingExpressions is { Count: > 0 })
        {
            // If a pending expression contains a lexical `this`, we'll need to capture the lexical `this` of the
            // container and transform it in the expression. This ensures we use the correct `this` in the resulting
            // class `static` block. We don't use substitution here because the size of the tree we are visiting
            // is likely to be small and doesn't justify the complexity of introducing substitution.
            _outerThis = null;
            foreach (var pendingExpression in _pendingExpressions)
            {
                // If a pending expression contains lexical `this`, capture it
                var expr = pendingExpression;
                if ((expr.SubtreeFacts & SubtreeFacts.ContainsLexicalThis) != 0)
                {
                    expr = _outerThisVisitor!.VisitNode(expr)!;
                }

                var statement = f.NewExpressionStatement(expr);
                (leadingBlockStatements ??= []).Add(statement);
            }

            if (_outerThis is not null)
            {
                classDefinitionStatements.Insert(0, CreateLet(_outerThis, f.NewThisExpression()));
            }

            _pendingExpressions = null;
        }

        ExitClass();

        // If there are instance initializers but no constructor, synthesize one
        if (ci.PendingInstanceInitializers is { Count: > 0 } && GetFirstConstructorWithBody(node) is null)
        {
            var initializerStatements = PrepareConstructor(ci);
            if (initializerStatements is { Count: > 0 })
            {
                var isDerivedClass = extendsElement is not null &&
                    SkipOuterExpressions(extendsElement.AsExpressionWithTypeArguments()!.Expression, OuterExpressionKinds.All)!.Kind != Kind.NullKeyword;
                List<Node> constructorStatements = [];
                if (isDerivedClass)
                {
                    var spreadArguments = f.NewSpreadElement(f.NewIdentifier("arguments"));
                    var superCall = f.NewCallExpression(f.NewKeywordExpression(Kind.SuperKeyword), null, null, f.NewNodeList([spreadArguments]), NodeFlags.None);
                    constructorStatements.Add(f.NewExpressionStatement(superCall));
                }

                constructorStatements.AddRange(initializerStatements);
                var constructorBody = f.NewBlock(f.NewNodeList(constructorStatements.ToArray()), true);
                syntheticConstructor = f.NewConstructorDeclaration(null, null, f.NewNodeList(null), null, null, constructorBody);
            }
        }

        // Used in class definition steps 5,7,11
        if (ci.StaticMethodExtraInitializersName is not null)
        {
            classDefinitionStatements.Add(
                CreateLet(ci.StaticMethodExtraInitializersName, f.NewArrayLiteralExpression(f.NewNodeList(null), false)));
        }

        // Used in class definition steps 6,8, and construction
        if (ci.InstanceMethodExtraInitializersName is not null)
        {
            classDefinitionStatements.Add(
                CreateLet(ci.InstanceMethodExtraInitializersName, f.NewArrayLiteralExpression(f.NewNodeList(null), false)));
        }

        // Used in class definition steps 7, 8, 12, and construction.
        // Emit member info variable declarations; the reference implementation emits static member vars first, then non-static.
        if (ci.MemberInfos.Size() > 0)
        {
            classDefinitionStatements.AddRange(EmitMemberInfoDeclarations(ci, true /*isStatic*/) ?? []);
            classDefinitionStatements.AddRange(EmitMemberInfoDeclarations(ci, false /*isStatic*/) ?? []);
        }

        // 5. Static non-field element decorators are applied
        (leadingBlockStatements ??= []).AddRange(ci.StaticNonFieldDecorationStatements ?? []);

        // 6. Non-static non-field element decorators are applied
        (leadingBlockStatements ??= []).AddRange(ci.NonStaticNonFieldDecorationStatements ?? []);

        // 7. Static field element decorators are applied
        (leadingBlockStatements ??= []).AddRange(ci.StaticFieldDecorationStatements ?? []);

        // 8. Non-static field element decorators are applied
        (leadingBlockStatements ??= []).AddRange(ci.NonStaticFieldDecorationStatements ?? []);

        // 9. Class decorators are applied
        // 10. Class binding is initialized
        //
        // produces:
        //   __esDecorate(null, _classDescriptor = { value: this }, _classDecorators, { kind: "class", name: this.name, metadata }, null, _classExtraInitializers);
        if (ci.ClassDescriptorName is not null && ci.ClassDecoratorsName is not null && ci.ClassExtraInitializersName is not null && ci.ClassThis is not null)
        {
            var valueProperty = f.NewPropertyAssignment(null, f.NewIdentifier("value"), null, null, renamedClassThis);
            var classDescriptor = f.NewObjectLiteralExpression(f.NewNodeList([valueProperty]), false);
            var classDescriptorAssignment = f.NewAssignmentExpression(ci.ClassDescriptorName, classDescriptor);
            var classNameReference = f.NewPropertyAccessExpression(renamedClassThis, null, f.NewIdentifier("name"), NodeFlags.None);

            var contextObj = f.NewESDecorateClassContextObject(classNameReference, ci.MetadataReference!);

            var esDecorateHelper = f.NewESDecorateHelper(
                f.NewToken(Kind.NullKeyword),
                classDescriptorAssignment,
                ci.ClassDecoratorsName,
                contextObj,
                f.NewToken(Kind.NullKeyword),
                ci.ClassExtraInitializersName);
            var esDecorateStatement = f.NewExpressionStatement(esDecorateHelper);
            ec.SetSourceMapRange(esDecorateStatement, MoveRangePastDecorators(node));
            (leadingBlockStatements ??= []).Add(esDecorateStatement);

            // produces:
            //   C = _classThis = _classDescriptor.value;
            var classDescriptorValueRef = f.NewPropertyAccessExpression(ci.ClassDescriptorName, null, f.NewIdentifier("value"), NodeFlags.None);
            var classThisAssignment = f.NewAssignmentExpression(ci.ClassThis, classDescriptorValueRef);
            var classReferenceAssignment = f.NewAssignmentExpression(classReference, classThisAssignment);
            (leadingBlockStatements ??= []).Add(f.NewExpressionStatement(classReferenceAssignment));
        }

        // produces:
        //   if (metadata) Object.defineProperty(C, Symbol.metadata, { configurable: true, writable: true, value: metadata });
        (leadingBlockStatements ??= []).Add(CreateSymbolMetadata(renamedClassThis, ci.MetadataReference!));

        // 11. Static extra initializers
        // 12. Static fields are initialized
        if (ci.PendingStaticInitializers is { Count: > 0 })
        {
            foreach (var initializer in ci.PendingStaticInitializers)
            {
                var initializerStatement = f.NewExpressionStatement(initializer);
                ec.SetSourceMapRange(initializerStatement, ec.SourceMapRange(initializer));
                (trailingBlockStatements ??= []).Add(initializerStatement);
            }

            ci.PendingStaticInitializers = null;
        }

        // 13. Class extra initializers
        if (ci.ClassExtraInitializersName is not null)
        {
            var runClassInitializersHelper = f.NewRunInitializersHelper(renamedClassThis, ci.ClassExtraInitializersName, null);
            var runClassInitializersStatement = f.NewExpressionStatement(runClassInitializersHelper);
            if (node.Name() is not null)
            {
                ec.SetSourceMapRange(runClassInitializersStatement, node.Name()!.Loc);
            }
            else
            {
                ec.SetSourceMapRange(runClassInitializersStatement, MoveRangePastDecorators(node));
            }

            (trailingBlockStatements ??= []).Add(runClassInitializersStatement);
        }

        // If there are no other static initializers to run, combine the leading and trailing block statements
        if (leadingBlockStatements is { Count: > 0 } && trailingBlockStatements is { Count: > 0 } && !ci.HasStaticInitializers)
        {
            leadingBlockStatements.AddRange(trailingBlockStatements);
            trailingBlockStatements = null;
        }

        // prepare a leading `static {}` block, if necessary
        //
        // produces:
        //   class C {
        //       static { ... }
        //       ...
        //   }
        Node? leadingStaticBlock = null;
        if (leadingBlockStatements is { Count: > 0 })
        {
            leadingStaticBlock = f.NewClassStaticBlockDeclaration(
                null,
                f.NewBlock(f.NewNodeList(leadingBlockStatements.ToArray()), true));
        }

        if (leadingStaticBlock is not null && shouldTransformPrivateStaticElementsInClass)
        {
            // We use EFTransformPrivateStaticElements as a marker on a class static block
            // to inform the classFields transform that it shouldn't rename `this` to `_classThis` in the
            // transformed class static block.
            ec.SetEmitFlags(leadingStaticBlock, EmitFlags.TransformPrivateStaticElements);
        }

        // prepare a trailing `static {}` block, if necessary
        //
        // produces:
        //   class C {
        //       ...
        //       static { ... }
        //   }
        Node? trailingStaticBlock = null;
        if (trailingBlockStatements is { Count: > 0 })
        {
            trailingStaticBlock = f.NewClassStaticBlockDeclaration(
                null,
                f.NewBlock(f.NewNodeList(trailingBlockStatements.ToArray()), true));
        }

        // Assemble new members list
        if (leadingStaticBlock is not null || syntheticConstructor is not null || trailingStaticBlock is not null)
        {
            var memberNodes = members!.Nodes!;
            var newMembers = new List<Node>(memberNodes.Length + 3);

            // Find the existing NamedEvaluation helper block index
            var existingNamedEvaluationHelperBlockIndex = -1;
            for (var i = 0; i < memberNodes.Length; i++)
            {
                if (IsClassNamedEvaluationHelperBlock(ec, memberNodes[i]))
                {
                    existingNamedEvaluationHelperBlockIndex = i;
                    break;
                }
            }

            // add the leading `static {}` block
            if (leadingStaticBlock is not null)
            {
                // add the `static {}` block after any existing NamedEvaluation helper block, if one exists.
                newMembers.AddRange(memberNodes[..(existingNamedEvaluationHelperBlockIndex + 1)]);
                newMembers.Add(leadingStaticBlock);
                newMembers.AddRange(memberNodes[(existingNamedEvaluationHelperBlockIndex + 1)..]);
            }
            else
            {
                newMembers.AddRange(memberNodes);
            }

            // append the synthetic constructor, if necessary
            if (syntheticConstructor is not null)
            {
                newMembers.Add(syntheticConstructor);
            }

            // append a trailing `static {}` block, if necessary
            if (trailingStaticBlock is not null)
            {
                newMembers.Add(trailingStaticBlock);
            }

            var membersList = f.NewNodeList(newMembers.ToArray());
            membersList.Loc = members.Loc;
            members = membersList;
        }

        var lexicalEnvironment = ec.EndVariableEnvironment();

        Node classExpression;
        if (classDecorators is { Count: > 0 })
        {
            classExpression = f.NewClassExpression(null, null, null, heritageClauses, members);
            ec.SetOriginal(classExpression, node);
            if (ci.ClassThis is not null)
            {
                classExpression = InjectClassThisAssignmentIfMissing(ec, f, classExpression, ci.ClassThis);
            }

            // We use `var` instead of `let` so we can leverage NamedEvaluation to define the class name
            // and still be able to ensure it is initialized prior to any use in `static {}`.

            // produces:
            //   (() => {
            //       let _classDecorators = [...];
            //       let _classDescriptor;
            //       let _classExtraInitializers = [];
            //       let _classThis;
            //       ...
            //       var C = class {
            //           static {
            //               __esDecorate(null, _classDescriptor = { value: this }, _classDecorators, ...);
            //               C = _classThis = _classDescriptor.value;
            //           }
            //           static x = 1;
            //           static y = C.x; // `C` will already be defined here.
            //           static { ... }
            //       };
            //       return C;
            //   })();

            var classReferenceDeclaration = f.NewVariableDeclaration(classReference, null, null, classExpression);
            var classReferenceVarDeclList = f.NewVariableDeclarationList(f.NewNodeList([classReferenceDeclaration]), NodeFlags.None);
            Node returnExpr = ci.ClassThis is not null
                ? f.NewAssignmentExpression(classReference, ci.ClassThis)
                : classReference;
            classDefinitionStatements.AddRange([
                f.NewVariableStatement(null, classReferenceVarDeclList),
                f.NewReturnStatement(returnExpr),
            ]);
        }
        else
        {
            // produces:
            //   return <classExpression>;
            classExpression = f.NewClassExpression(null, node.Name(), null, heritageClauses, members);
            ec.SetOriginal(classExpression, node);
            classDefinitionStatements.Add(f.NewReturnStatement(classExpression));
        }

        if (shouldTransformPrivateStaticElementsInClass)
        {
            ec.AddEmitFlags(classExpression, EmitFlags.TransformPrivateStaticElements);
            foreach (var member in classExpression.Members()!)
            {
                if ((IsPrivateIdentifierClassElementDeclaration(member) || IsAutoAccessorPropertyDeclaration(member)) && HasStaticModifier(member))
                {
                    ec.AddEmitFlags(member, EmitFlags.TransformPrivateStaticElements);
                }
            }
        }

        var mergedStatements = ec.MergeEnvironment(classDefinitionStatements.ToArray(), lexicalEnvironment);
        return f.NewImmediatelyInvokedArrowFunction(mergedStatements!);
    }

    /// <summary>Generates let declarations for member decorator info variables, filtered by static/non-static.</summary>
    private List<Node>? EmitMemberInfoDeclarations(ClassInfo ci, bool isStatic)
    {
        var f = Factory();
        List<Node>? stmts = null;
        foreach (var (member, memberInfo) in ci.MemberInfos.Entries())
        {
            if (IsStatic(member) != isStatic)
            {
                continue;
            }

            var mi = memberInfo!;
            (stmts ??= []).Add(CreateLet(mi.MemberDecoratorsName!, null));
            if (mi.MemberInitializersName is not null)
            {
                (stmts ??= []).Add(CreateLet(mi.MemberInitializersName, f.NewArrayLiteralExpression(f.NewNodeList(null), false)));
            }

            if (mi.MemberExtraInitializersName is not null)
            {
                (stmts ??= []).Add(CreateLet(mi.MemberExtraInitializersName, f.NewArrayLiteralExpression(f.NewNodeList(null), false)));
            }

            if (mi.MemberDescriptorName is not null)
            {
                (stmts ??= []).Add(CreateLet(mi.MemberDescriptorName, null));
            }
        }

        return stmts;
    }

    private Node? VisitClassDeclaration(ClassDeclaration node)
    {
        if (IsDecoratedClassLike(node))
        {
            var f = Factory();
            var ec = EmitContext();
            List<Node> statements = [];

            Node? originalClass = ec.MostOriginal(node);
            if (!IsClassLike(originalClass))
            {
                originalClass = node;
            }

            Node className;
            if (originalClass!.Name() is not null)
            {
                className = f.NewStringLiteralFromNode(originalClass.Name()!);
            }
            else
            {
                className = f.NewStringLiteral("default", TokenFlags.None);
            }

            var isExport = HasSyntacticModifier(node, ModifierFlags.Export);
            var isDefault = HasSyntacticModifier(node, ModifierFlags.Default);

            Node classNode = node;
            if (node.Name() is null)
            {
                classNode = InjectClassNamedEvaluationHelperBlockIfMissing(ec, classNode, className, null);
            }

            if (isExport && isDefault)
            {
                var iife = TransformClassLike(classNode);
                if (classNode.Name() is not null)
                {
                    // produces:
                    //   let C = (() => { ... })();
                    //   export default C;
                    var varDecl = f.NewVariableDeclaration(f.GetLocalName(classNode), null, null, iife);
                    ec.SetOriginal(varDecl, classNode);
                    var varDecls = f.NewVariableDeclarationList(f.NewNodeList([varDecl]), NodeFlags.Let);
                    var varStatement = f.NewVariableStatement(null, varDecls);
                    statements.Add(varStatement);

                    var exportStatement = f.NewExportDefault(f.GetDeclarationName(classNode));
                    ec.SetOriginal(exportStatement, classNode);
                    ec.AssignCommentRange(exportStatement, classNode);
                    ec.SetSourceMapRange(exportStatement, MoveRangePastDecorators(classNode));
                    statements.Add(exportStatement);
                }
                else
                {
                    // produces:
                    //   export default (() => { ... })();
                    var exportStatement = f.NewExportDefault(iife);
                    ec.SetOriginal(exportStatement, classNode);
                    ec.AssignCommentRange(exportStatement, classNode);
                    ec.SetSourceMapRange(exportStatement, MoveRangePastDecorators(classNode));
                    statements.Add(exportStatement);
                }
            }
            else
            {
                Debug.Assert(classNode.Name() is not null, "A class declaration that is not a default export must have a name.");
                // produces:
                //   let C = (() => { ... })();
                var iife = TransformClassLike(classNode);
                var modifiers = _exportStrippingModifierVisitor!.VisitModifiers(classNode.Modifiers());

                var declName = f.GetLocalNameEx(classNode, new NodeFactory.AssignedNameOptions { AllowSourceMaps = true });
                var varDecl = f.NewVariableDeclaration(declName, null, null, iife);
                ec.SetOriginal(varDecl, classNode);
                var varDecls = f.NewVariableDeclarationList(f.NewNodeList([varDecl]), NodeFlags.Let);
                var varStatement = f.NewVariableStatement(modifiers, varDecls);
                ec.SetOriginal(varStatement, classNode);
                ec.AssignCommentRange(varStatement, classNode);
                statements.Add(varStatement);

                if (isExport)
                {
                    // produces:
                    //   export { C };
                    var exportStatement = f.NewExternalModuleExport(declName);
                    ec.SetOriginal(exportStatement, classNode);
                    statements.Add(exportStatement);
                }
            }

            return SingleOrMany(statements.ToArray(), f);
        }

        // Non-decorated class
        var nonDecoratedModifiers = _modifierVisitor!.VisitModifiers(node.Modifiers());
        var heritageClauses = Visitor().VisitNodes(node.HeritageClauses);
        EnterClass(null);
        var members = _classElementVisitor!.VisitNodes(node.Members);
        ExitClass();
        return Factory().UpdateClassDeclaration(node, nonDecoratedModifiers, node.Name(), null, heritageClauses, members);
    }

    private Node? VisitClassExpression(ClassExpression node)
    {
        if (IsDecoratedClassLike(node))
        {
            var iife = TransformClassLike(node);
            EmitContext().SetOriginal(iife, node);
            return iife;
        }

        var modifiers = _modifierVisitor!.VisitModifiers(node.Modifiers());
        var heritageClauses = Visitor().VisitNodes(node.HeritageClauses);
        EnterClass(null);
        var members = _classElementVisitor!.VisitNodes(node.Members);
        ExitClass();
        return Factory().UpdateClassExpression(node, modifiers, node.Name(), null, heritageClauses, members);
    }

    private List<Node>? PrepareConstructor(ClassInfo ci)
    {
        // Decorated instance members can add "extra" initializers to the instance. If a class contains any instance
        // fields, we'll inject the `__runInitializers()` call for these extra initializers into the initializer of
        // the first class member that will be initialized. However, if the class does not contain any fields that
        // we can piggyback on, we need to synthesize a `__runInitializers()` call in the constructor instead.
        if (ci.PendingInstanceInitializers is not { Count: > 0 })
        {
            return null;
        }

        var f = Factory();
        List<Node> statements = [
            f.NewExpressionStatement(f.InlineExpressions(ci.PendingInstanceInitializers.ToArray())!),
        ];
        ci.PendingInstanceInitializers = null;
        return statements;
    }

    private List<Node>? TransformConstructorBodyWorker(
        List<Node>? statementsOut,
        Node[] statementsIn,
        int statementOffset,
        int[] superPath,
        int superPathDepth,
        List<Node> initializerStatements)
    {
        var superStatementIndex = superPath[superPathDepth];
        // Visit statements before super
        if (superStatementIndex > statementOffset)
        {
            for (var i = statementOffset; i < superStatementIndex; i++)
            {
                (statementsOut ??= []).Add(Visitor().VisitNode(statementsIn[i])!);
            }
        }

        var superStatement = statementsIn[superStatementIndex];
        if (IsTryStatement(superStatement))
        {
            // Recurse into try block
            var tryBlockNode = superStatement.AsTryStatement()!.TryBlock!;
            var tryBlock = tryBlockNode.AsBlock()!;
            var tryBlockStatements = TransformConstructorBodyWorker(null, tryBlock.Statements!.Nodes!, 0, superPath, superPathDepth + 1, initializerStatements);

            var newTryBlock = Factory().NewBlock(Factory().NewNodeList(tryBlockStatements!.ToArray()), true);
            // Use the original try block's range even though the statements may differ due to
            // injected initializer statements. This preserves source map fidelity for the enclosing
            // try statement.
            newTryBlock.Loc = tryBlockNode.Loc;

            Node? catchClause = null;
            if (superStatement.AsTryStatement()!.CatchClause is not null)
            {
                catchClause = Visitor().VisitNode(superStatement.AsTryStatement()!.CatchClause);
            }

            Node? finallyBlock = null;
            if (superStatement.AsTryStatement()!.FinallyBlock is not null)
            {
                finallyBlock = Visitor().VisitNode(superStatement.AsTryStatement()!.FinallyBlock);
            }

            var updated = Factory().UpdateTryStatement(superStatement.AsTryStatement()!, newTryBlock, catchClause, finallyBlock);
            (statementsOut ??= []).Add(updated);
        }
        else
        {
            (statementsOut ??= []).Add(Visitor().VisitNode(superStatement)!);
            statementsOut.AddRange(initializerStatements);
        }

        // Visit statements after super
        if (superStatementIndex + 1 < statementsIn.Length)
        {
            for (var i = superStatementIndex + 1; i < statementsIn.Length; i++)
            {
                (statementsOut ??= []).Add(Visitor().VisitNode(statementsIn[i])!);
            }
        }

        return statementsOut;
    }

    private Node? VisitConstructorDeclaration(Node node)
    {
        EnterClassElement(node);
        var modifiers = _modifierVisitor!.VisitModifiers(node.Modifiers());
        var parameters = Visitor().VisitNodes(node.ParameterList());

        Node? body = null;
        var ctor = node.AsConstructorDeclaration()!;
        if (ctor.Body is not null && _classInfoStack is not null)
        {
            // If there are instance extra initializers we need to add them to the body along with any
            // field initializers
            var initializerStatements = PrepareConstructor(_classInfoStack);
            if (initializerStatements is { Count: > 0 })
            {
                List<Node>? stmts = [];
                var (prologue, restNodes) = Factory().SplitStandardPrologue(ctor.Body.AsBlock()!.Statements!.Nodes!);
                stmts.AddRange(prologue);
                var rest = restNodes ?? [];

                var superStatementIndices = FindSuperStatementIndexPath(rest, 0);
                if (superStatementIndices is { Length: > 0 })
                {
                    stmts = TransformConstructorBodyWorker(stmts, rest, 0, superStatementIndices, 0, initializerStatements);
                }
                else
                {
                    stmts!.AddRange(initializerStatements);
                    var (visited, _) = Visitor().VisitSlice(rest);
                    stmts.AddRange(visited!);
                }

                body = Factory().NewBlock(Factory().NewNodeList(stmts!.ToArray()), true);
                EmitContext().SetOriginal(body, ctor.Body);
                body.Loc = ctor.Body.Loc;
            }
        }

        body ??= Visitor().VisitNode(ctor.Body);
        ExitClassElement();
        return Factory().UpdateConstructorDeclaration(ctor, modifiers, null, parameters, null, null, body);
    }

    private Node? FinishClassElement(Node? updated, Node original)
    {
        if (!ReferenceEquals(updated, original))
        {
            // While we emit the source map for the node after skipping decorators and modifiers,
            // we need to emit the comments for the original range.
            EmitContext().AssignCommentRange(updated!, original);
            EmitContext().SetSourceMapRange(updated!, MoveRangePastDecorators(original));
        }

        return updated;
    }
}
