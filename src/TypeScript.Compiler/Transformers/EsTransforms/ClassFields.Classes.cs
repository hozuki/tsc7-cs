// ported from: internal/transformers/estransforms/classfields.go:1686-2649（类事实计算 / 类声明与类表达式的词法环境访问 / 类成员与构造函数变换）
//
// Go→C# 语义改写（本分片）：
// 1) Go 的具名回调参数 `visitor func(tx *classFieldsTransformer, node *ast.Node, facts classFacts) *ast.Node`
//    → `Func<Node, ClassFacts, Node?>`（接收者由闭包/方法组绑定的实例提供）。
// 2) Go 的递归闭包 `var check func(n *ast.Node) bool; check = func(...)` → C# 局部函数 `bool Check(Node n)`
//    （局部函数可自递归，不需要先声明再赋值；`n.ForEachChild(check)` → `n.ForEachChild(Check)`）。
// 3) Go 的 `if v := f(); v != nil {...}`（if 初始化语句）→ C# 拆成 `var v = f(); if (v != null) {...}`（同层作用域）。
// 4) Go 的 `tx.pendingExpressions = append([]*ast.Expression{x}, tx.pendingExpressions...)` → `List<Node>.Insert(0, x)`。
// 5) Go 的 `append(statements, ...)` → `List<Node>.Add/AddRange`；`transformConstructorBodyWorker` 的
//    `statementsOut []*ast.Statement`（nil 起步）→ 参数 `List<Node>?` + 返回非空 `List<Node>`（进函数时 `??= []`），
//    与 Go 的 `append(nil, ...)`（产生新切片）语义一致；调用点保持 `statements = worker(statements, ...)`。
// 6) `statements = tx.EmitContext().EndAndMergeVariableEnvironment(statements)` 之后 Go 仍用同一个切片名；
//    C# 用 `Node[] finalStatements` 承接返回的数组（`EndAndMergeVariableEnvironment` 吃/吐 `Node[]?`），
//    之后的 `len(statements)` → `finalStatements.Length`。
// 7) `make([]T, 0, n)` → `new List<T>(n)`；`slices.IndexFunc(xs, f)` → `Array.FindIndex(xs, f)`（保留 nil 数组 → -1 的语义）。
// 8) `switch`/无：`MemberContainsConstructorReference` 的闭包捕获改用局部函数（见 2）。
// 9) Go 的 `tx.lexicalEnvironment`/`tx.currentClassContainer`/... 字段 → `_camelCase`；`tx.EmitContext()`/`tx.Factory()`/
//    `tx.Visitor()` → `EmitContext()`/`Factory()`/`Visitor()`（T1 基类方法）。
// 10) `printer.EFIndented` → `EmitFlags.Indented`、`printer.EFStartOnNewLine` → `EmitFlags.StartOnNewLine`、
//     `printer.EFTransformPrivateStaticElements` → `EmitFlags.TransformPrivateStaticElements`。
// 11) `printer.AutoGenerateOptions{...}` → `new AutoGenerateOptions { ... }`（struct 的对象初始化器，不用 `default`）。
// 12) `ast.OEKAll` → `OuterExpressionKinds.All`；`core.LanguageVariantStandard` → `LanguageVariant.Standard`。
// 13) `printer.PrivateIdentifierKind` 在 C# 侧是 `TypeScript.Printer` 的命名空间级 struct → 顶部起别名 `PrivateIdentifierKind`
//     （避免与 `Ast.NodeFactory` 二义，同时照 Go 的 `printer.` 限定）。
// 14) Go 包级函数（无接收者）→ `EsTransforms` 静态类成员，调用点裸名（`using static ...EsTransforms.EsTransforms;`）。
// 15) `debug.Assert(cond, msg)` → `Debug.Assert(cond, msg)`（`Debug` 在根命名空间 `TypeScript`；禁止 `using System.Diagnostics`，
//     否则 `Debug.Fail` 会绑到 BCL 版本并直接杀进程）。
// 16) `transformers.ExtractModifiers` / `transformers.FindSuperStatementIndexPath` → 裸名（T1 的
//     `using static TypeScript.Transformers.Transformers;`，与本目录其它 ClassFields.*.cs 一致）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Printer;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.Transformers.Transformers;
using static TypeScript.Transformers.EsTransforms.EsTransforms;
using PrivateIdentifierKind = TypeScript.Printer.PrivateIdentifierKind;

namespace TypeScript.Transformers.EsTransforms;

public sealed partial class ClassFieldsTransformer : Transformer
{
    /// <summary>Go: <c>getPrivateInstanceMethodsAndAccessors</c>（classfields.go:1686-1688）。</summary>
    private Node[] GetPrivateInstanceMethodsAndAccessors(Node node)
    {
        return Filter(node.Members(), IsNonStaticMethodOrAccessorWithPrivateName);
    }

    /// <summary>Go: <c>memberContainsConstructorReference</c>（classfields.go:1695-1736）。</summary>
    private bool MemberContainsConstructorReference(Node member, Node classDecl)
    {
        // memberContainsConstructorReference checks if a class member's body contains an identifier
        // that resolves to the class declaration. Replaces Strada's resolver.hasNodeCheckFlag(member,
        // NodeCheckFlags.ContainsConstructorReference) by walking the AST with the EmitResolver.
        // Only checks member bodies (not computed property names), since computed property names
        // are evaluated during class definition when the binding is still correct.
        var classOriginal = EmitContext().MostOriginal(classDecl);
        var className = GetNameOfDeclaration(classDecl);

        bool Check(Node n)
        {
            if (IsIdentifier(n) && n != className)
            {
                var decl = _resolver.GetReferencedValueDeclaration(n);
                if (decl == classOriginal)
                {
                    return true;
                }
            }
            // For PropertyAccessExpression, only check the expression, not the name.
            // The .Name() is a property access name, not a value reference to the class.
            if (IsPropertyAccessExpression(n))
            {
                return Check(n.Expression()!);
            }
            return n.ForEachChild(Check);
        }

        // Check only the body/initializer of the member, not the name (which may be
        // a computed property name that shouldn't trigger alias substitution).
        if (IsClassStaticBlockDeclaration(member))
        {
            var body = member.AsClassStaticBlockDeclaration()!.Body;
            if (body != null && Check(body.AsNode()))
            {
                return true;
            }
        }
        else
        {
            var body = member.Body();
            if (body != null && Check(body))
            {
                return true;
            }
        }
        if (IsPropertyDeclaration(member))
        {
            var init = member.Initializer();
            if (init != null && Check(init))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>classContainsConstructorReference</c>（classfields.go:1738-1745）。</summary>
    private bool ClassContainsConstructorReference(Node node)
    {
        // classContainsConstructorReference checks if any member of a class contains
        // references to the class's own constructor. Replaces Strada's
        // resolver.hasNodeCheckFlag(node, NodeCheckFlags.ContainsConstructorReference).
        foreach (var member in node.Members()!)
        {
            if (MemberContainsConstructorReference(member, node))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>getClassFacts</c>（classfields.go:1747-1813）。</summary>
    private ClassFacts GetClassFacts(Node node)
    {
        var facts = ClassFacts.None;

        var original = EmitContext().MostOriginal(node);
        if (IsClassLike(original) && ClassOrConstructorParameterIsDecorated(_legacyDecorators /*useLegacyDecorators*/, original))
        {
            facts |= ClassFacts.ClassWasDecorated;
        }

        if (_shouldTransformPrivateElementsOrClassStaticBlocks &&
            (ClassHasClassThisAssignment(EmitContext(), node) || ClassHasExplicitlyAssignedName(EmitContext(), node)))
        {
            facts |= ClassFacts.NeedsClassConstructorReference;
        }

        var containsPublicInstanceFields = false;
        var containsInitializedPublicInstanceFields = false;
        var containsInstancePrivateElements = false;
        var containsInstanceAutoAccessors = false;

        foreach (var member in node.Members()!)
        {
            if (IsStatic(member))
            {
                if (member.Name() != null && (IsPrivateIdentifier(member.Name()!) || IsAutoAccessorPropertyDeclaration(member)) &&
                    _shouldTransformPrivateElementsOrClassStaticBlocks)
                {
                    facts |= ClassFacts.NeedsClassConstructorReference;
                }
                else if (IsAutoAccessorPropertyDeclaration(member) && _shouldTransformAutoAccessors &&
                    node.Name() == null && EmitContext().ClassThis(node) == null)
                {
                    facts |= ClassFacts.NeedsClassConstructorReference;
                }
                if (IsPropertyDeclaration(member) || IsClassStaticBlockDeclaration(member))
                {
                    if (_shouldTransformThisInStaticInitializers && (member.SubtreeFacts & SubtreeFacts.ContainsLexicalThis) != 0)
                    {
                        facts |= ClassFacts.NeedsSubstitutionForThisInClassStaticField;
                        if ((facts & ClassFacts.ClassWasDecorated) == 0)
                        {
                            facts |= ClassFacts.NeedsClassConstructorReference;
                        }
                    }
                    if (_shouldTransformSuperInStaticInitializers && (member.SubtreeFacts & SubtreeFacts.ContainsLexicalSuper) != 0)
                    {
                        if ((facts & ClassFacts.ClassWasDecorated) == 0)
                        {
                            facts |= ClassFacts.NeedsClassConstructorReference | ClassFacts.NeedsClassSuperReference;
                        }
                    }
                }
            }
            else if (!HasAbstractModifier(EmitContext().MostOriginal(member)))
            {
                if (IsAutoAccessorPropertyDeclaration(member))
                {
                    containsInstanceAutoAccessors = true;
                    containsInstancePrivateElements = containsInstancePrivateElements || IsPrivateIdentifierClassElementDeclaration(member);
                }
                else if (IsPrivateIdentifierClassElementDeclaration(member))
                {
                    containsInstancePrivateElements = true;
                    if (MemberContainsConstructorReference(member, node))
                    {
                        facts |= ClassFacts.NeedsClassConstructorReference;
                    }
                }
                else if (IsPropertyDeclaration(member))
                {
                    containsPublicInstanceFields = true;
                    containsInitializedPublicInstanceFields = containsInitializedPublicInstanceFields || member.Initializer() != null;
                }
            }
        }

        var willHoistInitializersToConstructor = (_shouldTransformInitializersUsingDefine && containsPublicInstanceFields) ||
            (_shouldTransformInitializersUsingSet && containsInitializedPublicInstanceFields) ||
            (_shouldTransformPrivateElementsOrClassStaticBlocks && containsInstancePrivateElements) ||
            (_shouldTransformPrivateElementsOrClassStaticBlocks && containsInstanceAutoAccessors && _shouldTransformAutoAccessors);

        if (willHoistInitializersToConstructor)
        {
            facts |= ClassFacts.WillHoistInitializersToConstructor;
        }

        return facts;
    }

    /// <summary>Go: <c>visitExpressionWithTypeArgumentsInHeritageClause</c>（classfields.go:1815-1833）。</summary>
    private Node? VisitExpressionWithTypeArgumentsInHeritageClause(ExpressionWithTypeArguments node)
    {
        var facts = ClassFacts.None;
        if (_lexicalEnvironment != null && _lexicalEnvironment._data != null)
        {
            facts = _lexicalEnvironment._data._facts;
        }
        if ((facts & ClassFacts.NeedsClassSuperReference) != 0)
        {
            var temp = Factory().NewTempVariableEx(new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.ReservedInNestedScopes,
            });
            EmitContext().AddVariableDeclaration(temp);
            GetClassLexicalEnvironment()._superClassReference = temp;
            return Factory().UpdateExpressionWithTypeArguments(
                node,
                Factory().NewAssignmentExpression(temp, Visitor().VisitNode(node.Expression)!),
                null /*typeArguments*/
            );
        }
        return _heritageClauseVisitor.VisitEachChild(node.AsNode());
    }

    /// <summary>Go: <c>visitInNewClassLexicalEnvironment</c>（classfields.go:1835-1887）。</summary>
    private Node? VisitInNewClassLexicalEnvironment(Node node, Func<Node, ClassFacts, Node?> visitor)
    {
        var savedCurrentClassContainer = _currentClassContainer;
        var savedPendingExpressions = _pendingExpressions;
        var savedLexicalEnvironment = _lexicalEnvironment;
        _currentClassContainer = node;
        _pendingExpressions = [];
        StartClassLexicalEnvironment();
        var original = EmitContext().MostOriginal(node);
        _enclosingClassDeclarations.Add(original!);

        if (_shouldTransformPrivateElementsOrClassStaticBlocks || NodeHasTransformPrivateStaticElementsFlag(node))
        {
            var name = GetNameOfDeclaration(node);
            if (name != null && IsIdentifier(name))
            {
                GetPrivateIdentifierEnvironment()._data._className = name;
            }
            else
            {
                var assignedName = EmitContext().AssignedName(node);
                if (assignedName != null)
                {
                    if (IsStringLiteral(assignedName))
                    {
                        // If the assigned name has a textSourceNode that is an identifier, use it directly.
                        var textSourceNode = EmitContext().TextSource(assignedName);
                        if (textSourceNode != null && IsIdentifier(textSourceNode))
                        {
                            GetPrivateIdentifierEnvironment()._data._className = textSourceNode;
                        }
                        else if (IsIdentifierText(assignedName.Text(), LanguageVariant.Standard))
                        {
                            // If the text is a valid identifier, create an identifier from it.
                            var prefixName = Factory().NewIdentifier(assignedName.Text());
                            GetPrivateIdentifierEnvironment()._data._className = prefixName;
                        }
                    }
                }
            }
        }

        if (_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            var privateInstanceMethodsAndAccessors = GetPrivateInstanceMethodsAndAccessors(node);
            if (privateInstanceMethodsAndAccessors.Length > 0)
            {
                GetPrivateIdentifierEnvironment()._data._weakSetName = CreateHoistedVariableForClass(
                    "instances",
                    privateInstanceMethodsAndAccessors[0].Name()!,
                    ""
                );
            }
        }

        var facts = GetClassFacts(node);
        if (facts != ClassFacts.None)
        {
            GetClassLexicalEnvironment()._facts = facts;
        }

        var result = visitor(node, facts);
        _enclosingClassDeclarations.Delete(original!);
        EndClassLexicalEnvironment();
        Debug.Assert(_lexicalEnvironment == savedLexicalEnvironment);
        _currentClassContainer = savedCurrentClassContainer;
        _pendingExpressions = savedPendingExpressions;
        _lexicalEnvironment = savedLexicalEnvironment;
        return result;
    }

    /// <summary>Go: <c>visitClassDeclaration</c>（classfields.go:1889-1891）。</summary>
    private Node? VisitClassDeclaration(ClassDeclaration node)
    {
        return VisitInNewClassLexicalEnvironment(node.AsNode(), VisitClassDeclarationInNewClassLexicalEnvironment);
    }

    /// <summary>Go: <c>visitClassDeclarationInNewClassLexicalEnvironment</c>（classfields.go:1893-2001）。</summary>
    private Node VisitClassDeclarationInNewClassLexicalEnvironment(Node node, ClassFacts facts)
    {
        var classDecl = node.AsClassDeclaration()!;
        // If a class has private static fields, or a static field has a `this` or `super` reference,
        // then we need to allocate a temp variable to hold on to that reference.
        Node? pendingClassReferenceAssignment = null;
        if ((facts & ClassFacts.NeedsClassConstructorReference) != 0)
        {
            // If we aren't transforming class static blocks, then we can't reuse `_classThis` since in
            // `class C { ... static { _classThis = ... } }; _classThis = C` the outer assignment would occur *after*
            // class static blocks evaluate and would overwrite the replacement constructor produced by class
            // decorators.

            // If we are transforming class static blocks, then we can reuse `_classThis` since the assignment
            // will be evaluated *before* the transformed static blocks are evaluated and thus won't overwrite
            // the replacement constructor.

            if (_shouldTransformPrivateElementsOrClassStaticBlocks && EmitContext().ClassThis(node) != null)
            {
                var classThis = EmitContext().ClassThis(node);
                GetClassLexicalEnvironment()._classConstructor = classThis;
                pendingClassReferenceAssignment = Factory().NewAssignmentExpression(
                    classThis!,
                    Factory().GetLocalName(node)
                );
            }
            else
            {
                var temp = Factory().NewTempVariableEx(new AutoGenerateOptions
                {
                    Flags = GeneratedIdentifierFlags.ReservedInNestedScopes,
                });
                EmitContext().AddVariableDeclaration(temp);
                GetClassLexicalEnvironment()._classConstructor = temp.Clone(Factory());
                pendingClassReferenceAssignment = Factory().NewAssignmentExpression(
                    temp,
                    Factory().GetLocalName(node)
                );
            }
        }

        if (EmitContext().ClassThis(node) != null)
        {
            GetClassLexicalEnvironment()._classThis = EmitContext().ClassThis(node);
        }

        var isClassWithConstructorReference = ClassContainsConstructorReference(node);

        // Register class alias BEFORE visiting members (Strada registers after, since its
        // onSubstituteNode runs at emit time; we substitute eagerly during transformation).
        var alias = GetClassLexicalEnvironment()._classConstructor;
        if (isClassWithConstructorReference && alias != null)
        {
            _classAliases![EmitContext().MostOriginal(node)!] = alias;
        }

        var modifiers = _modifierVisitor.VisitModifiers(classDecl.Modifiers());
        var heritageClauses = _heritageClauseVisitor.VisitNodes(classDecl.HeritageClauses);
        var (members, membersPrologue) = TransformClassMembers(node);

        var statements = new List<Node>();

        if (pendingClassReferenceAssignment != null)
        {
            _pendingExpressions.Insert(0, pendingClassReferenceAssignment);
        }

        // Write any pending expressions from elided or moved computed property names
        if (_pendingExpressions.Count > 0)
        {
            statements.Add(Factory().NewExpressionStatement(Factory().InlineExpressions(_pendingExpressions.ToArray())!));
        }

        // A class declaration without a name needs a generated name if it has static
        // initialized properties, since those will be moved outside the class body and
        // need to reference the class by name.
        var name = classDecl.Name();

        if (_shouldTransformInitializersUsingSet || _shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            // Emit static property assignment. Because classDeclaration is lexically evaluated,
            // it is safe to emit static property assignment after classDeclaration
            // From ES6 specification:
            //   HasLexicalDeclaration (N) : Determines if the argument identifier has a binding in this environment record that was created using
            //                               a lexical declaration such as a LexicalDeclaration or a ClassDeclaration.
            var staticProperties = GetStaticPropertiesAndClassStaticBlock(node);
            if (staticProperties.Length > 0)
            {
                if (name == null)
                {
                    name = Factory().NewGeneratedNameForNode(node);
                }
                statements = AddPropertyOrClassStaticBlockStatements(statements, staticProperties, Factory().GetLocalName(node));
            }
        }

        var isExport = HasSyntacticModifier(node, ModifierFlags.Export);
        var isDefault = HasSyntacticModifier(node, ModifierFlags.Default);

        if (statements.Count > 0 && isExport && isDefault)
        {
            modifiers = ExtractModifiers(EmitContext(), modifiers, ~ModifierFlags.ExportDefault);
            var exportAssignment = Factory().NewExportAssignment(null, false /*isExportEquals*/, null /*typeNode*/, Factory().GetLocalName(node));
            statements.Add(exportAssignment);
        }

        var updatedClass = Factory().UpdateClassDeclaration(
            classDecl,
            modifiers,
            name,
            null, /*typeParameters*/
            heritageClauses,
            members
        );

        var result = new List<Node>(1 + statements.Count + 1);
        if (membersPrologue != null)
        {
            result.Add(Factory().NewExpressionStatement(membersPrologue));
        }
        result.Add(updatedClass);
        result.AddRange(statements);
        return Factory().NewSyntaxList(result.ToArray());
    }

    /// <summary>Go: <c>visitClassExpression</c>（classfields.go:2003-2005）。</summary>
    private Node? VisitClassExpression(ClassExpression node)
    {
        return VisitInNewClassLexicalEnvironment(node.AsNode(), VisitClassExpressionInNewClassLexicalEnvironment);
    }

    /// <summary>Go: <c>visitClassExpressionInNewClassLexicalEnvironment</c>（classfields.go:2007-2179）。</summary>
    private Node? VisitClassExpressionInNewClassLexicalEnvironment(Node node, ClassFacts facts)
    {
        var classExpr = node.AsClassExpression()!;

        // If this class expression is a transformation of a decorated class declaration,
        // then we want to output the pendingExpressions as statements, not as inlined
        // expressions with the class statement.
        //
        // In this case, we use pendingStatements to produce the same output as the
        // class declaration transformation. The VariableStatement visitor will insert
        // these statements after the class expression variable statement.
        var isDecoratedClassDeclaration = (facts & ClassFacts.ClassWasDecorated) != 0;

        if (EmitContext().ClassThis(node) != null)
        {
            GetClassLexicalEnvironment()._classThis = EmitContext().ClassThis(node);
        }

        Node? temp = null;
        if ((facts & ClassFacts.NeedsClassConstructorReference) != 0)
        {
            if ((_shouldTransformPrivateElementsOrClassStaticBlocks || NodeHasTransformPrivateStaticElementsFlag(node)) && EmitContext().ClassThis(node) != null)
            {
                var classThis = EmitContext().ClassThis(node);
                GetClassLexicalEnvironment()._classConstructor = classThis;
                temp = classThis;
            }
            else
            {
                temp = Factory().NewTempVariableEx(new AutoGenerateOptions
                {
                    Flags = GeneratedIdentifierFlags.ReservedInNestedScopes,
                });
                if (ClassExpressionNeedsBlockScopedTemp())
                {
                    EmitContext().AddLexicalDeclaration(temp);
                }
                else
                {
                    EmitContext().AddVariableDeclaration(temp);
                }
                GetClassLexicalEnvironment()._classConstructor = temp.Clone(Factory());
            }
        }

        var staticPropertiesOrClassStaticBlocks = GetStaticPropertiesAndClassStaticBlock(node);

        // Pre-compute whether the class expression will need a temp variable wrapper.
        // Strada registers class aliases AFTER transformClassMembers (since onSubstituteNode runs
        // at emit time), but we must predict this before visiting members since we substitute
        // eagerly. This requires pre-detecting willHavePrivatePendingExpressions.
        var isClassWithConstructorReference = false;
        var hasTransformableStatics = false;
        var deferTempDeclaration = false;
        if (!isDecoratedClassDeclaration)
        {
            isClassWithConstructorReference = ClassContainsConstructorReference(node);
            hasTransformableStatics = (_shouldTransformPrivateElementsOrClassStaticBlocks ||
                NodeHasTransformPrivateStaticElementsFlag(node)) &&
                Some(staticPropertiesOrClassStaticBlocks, n =>
                {
                    return IsClassStaticBlockDeclaration(n) ||
                        IsPrivateIdentifierClassElementDeclaration(n) ||
                        (_shouldTransformInitializers && IsInitializedProperty(n));
                });

            // Private instance elements (fields, methods, accessors) transformed to
            // WeakMap/WeakSet will add initialization expressions to pendingExpressions
            // during transformClassMembers. Pre-detect this so we know whether the class
            // will be wrapped with a temp variable.
            var willHavePrivatePendingExpressions = _shouldTransformPrivateElementsOrClassStaticBlocks &&
                Some(node.Members(), n =>
                {
                    return IsPrivateIdentifierClassElementDeclaration(n) && !HasStaticModifier(n) && ShouldTransformClassElementToWeakMap(n);
                });
            var willNeedTempWrapper = hasTransformableStatics || willHavePrivatePendingExpressions;

            // Register class alias BEFORE visiting members (Strada registers after, since its
            // onSubstituteNode runs at emit time). Only register when the class will be wrapped
            // with a temp, matching Strada's conditional registration.
            if (isClassWithConstructorReference && willNeedTempWrapper && GetClassLexicalEnvironment()._classConstructor == null)
            {
                // Create temp early so the alias is available during member visiting, even though in the Strada
                // reference the temp would be created later in the pendingExpressions branch.
                temp = Factory().NewTempVariableEx(new AutoGenerateOptions
                {
                    Flags = GeneratedIdentifierFlags.ReservedInNestedScopes,
                });
                // Defer AddVariableDeclaration to preserve Strada's variable declaration ordering.
                deferTempDeclaration = true;
                GetClassLexicalEnvironment()._classConstructor = temp.Clone(Factory());
            }
            var alias = GetClassLexicalEnvironment()._classConstructor;
            if (isClassWithConstructorReference && willNeedTempWrapper && alias != null)
            {
                _classAliases![EmitContext().MostOriginal(node)!] = alias;
            }
        }

        var modifiers = _modifierVisitor.VisitModifiers(classExpr.Modifiers());
        var heritageClauses = _heritageClauseVisitor.VisitNodes(classExpr.HeritageClauses);
        var (members, membersPrologue) = TransformClassMembers(node);

        if (deferTempDeclaration)
        {
            if (ClassExpressionNeedsBlockScopedTemp())
            {
                EmitContext().AddLexicalDeclaration(temp!);
            }
            else
            {
                EmitContext().AddVariableDeclaration(temp!);
            }
        }

        var classExpression = Factory().UpdateClassExpression(
            classExpr,
            modifiers,
            classExpr.Name(),
            null, /*typeParameters*/
            heritageClauses,
            members
        );

        var expressions = new List<Node>();
        if (membersPrologue != null)
        {
            expressions.Add(membersPrologue);
        }

        if (!isDecoratedClassDeclaration)
        {
            if (hasTransformableStatics || _pendingExpressions.Count > 0)
            {
                if (temp == null)
                {
                    temp = Factory().NewTempVariableEx(new AutoGenerateOptions
                    {
                        Flags = GeneratedIdentifierFlags.ReservedInNestedScopes,
                    });
                    if (ClassExpressionNeedsBlockScopedTemp())
                    {
                        EmitContext().AddLexicalDeclaration(temp);
                    }
                    else
                    {
                        EmitContext().AddVariableDeclaration(temp);
                    }
                    GetClassLexicalEnvironment()._classConstructor = temp.Clone(Factory());
                    if (isClassWithConstructorReference)
                    {
                        _classAliases![EmitContext().MostOriginal(node)!] = GetClassLexicalEnvironment()._classConstructor!;
                    }
                }

                expressions.Add(Factory().NewAssignmentExpression(temp, classExpression));

                // Add any pending expressions leftover from elided or relocated computed property names
                expressions.AddRange(_pendingExpressions);

                expressions.AddRange(GenerateInitializedPropertyExpressionsOrClassStaticBlock(staticPropertiesOrClassStaticBlocks, temp));
                expressions.Add(temp.Clone(Factory())!);
            }
            else
            {
                expressions.Add(classExpression);
            }
        }
        else
        {
            // Decorated class declaration path: emit static properties as separate statements
            // via pendingStatements, matching the class declaration output structure.

            // Write any pending expressions from elided or moved computed property names
            if (_pendingExpressions.Count > 0)
            {
                foreach (var expr in _pendingExpressions)
                {
                    _pendingStatements.Add(Factory().NewExpressionStatement(expr));
                }
            }

            // Emit static properties as statements (via pendingStatements) using the class's
            // internal name as the receiver, matching the class declaration output structure.
            if (staticPropertiesOrClassStaticBlocks.Length > 0)
            {
                var classThisOrName = EmitContext().ClassThis(node);
                if (classThisOrName == null)
                {
                    classThisOrName = Factory().GetLocalName(node);
                }
                _pendingStatements = AddPropertyOrClassStaticBlockStatements(_pendingStatements, staticPropertiesOrClassStaticBlocks, classThisOrName);
            }

            if (temp != null)
            {
                expressions.Add(Factory().NewAssignmentExpression(temp, classExpression));
            }
            else if (_shouldTransformPrivateElementsOrClassStaticBlocks && EmitContext().ClassThis(node) != null)
            {
                expressions.Add(Factory().NewAssignmentExpression(EmitContext().ClassThis(node)!, classExpression));
            }
            else
            {
                expressions.Add(classExpression);
            }
        }

        if (expressions.Count > 1)
        {
            EmitContext().AddEmitFlags(classExpression, EmitFlags.Indented);
            foreach (var expr in expressions)
            {
                EmitContext().AddEmitFlags(expr, EmitFlags.StartOnNewLine);
            }
        }
        return Factory().InlineExpressions(expressions.ToArray());
    }

    /// <summary>Go: <c>visitClassStaticBlockDeclaration</c>（classfields.go:2181-2188）。</summary>
    private Node? VisitClassStaticBlockDeclaration(Node node)
    {
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks)
        {
            return Visitor().VisitEachChild(node);
        }
        // ClassStaticBlockDeclaration for classes are transformed in visitClassDeclaration/visitClassExpression.
        return null;
    }

    /// <summary>
    /// Go: <c>visitThisExpression</c>（classfields.go:2194-2221）。取代 Strada 的 substituteThisExpression /
    /// onSubstituteNode：Strada 在 emit 期替换 <c>this</c>，本移植在变换期即时替换。Strada 的 noSubstitution 集合
    /// （ensureDynamicThisIfNeeded）不再需要，因为 transformAutoAccessor() 直接传接收者而不是发出 <c>this</c>。
    /// </summary>
    private Node VisitThisExpression(Node node)
    {
        if (_insideComputedPropertyName && _shouldTransformThisInStaticInitializers &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null)
        {
            // Don't replace `this` in computed property names for ES-decorated classes.
            // The esDecorator transformer wraps them in an arrow IIFE where `this` already
            // refers to the correct outer scope.
            if ((_lexicalEnvironment._data._facts & ClassFacts.ClassWasDecorated) == 0 || _legacyDecorators)
            {
                var classThis = TryGetClassThisNoContainer();
                if (classThis != null)
                {
                    return classThis;
                }
            }
        }
        if (_shouldTransformThisInStaticInitializers && _currentClassElement != null &&
            (IsClassStaticBlockDeclaration(_currentClassElement) ||
                (IsPropertyDeclaration(_currentClassElement) && HasStaticModifier(_currentClassElement))) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null)
        {
            var classThis = TryGetClassThisNoContainer();
            if (classThis != null)
            {
                return classThis;
            }
            // When the class was decorated with legacy decorators and no class constructor
            // reference is available, the decorator may replace the constructor, so `this`
            // cannot reliably point to the class. Use `(void 0)` instead.
            if ((_lexicalEnvironment._data._facts & ClassFacts.ClassWasDecorated) != 0 && _legacyDecorators)
            {
                return Factory().NewParenthesizedExpression(Factory().NewVoidZeroExpression());
            }
        }
        return node;
    }

    /// <summary>Go: <c>transformClassMembers</c>（classfields.go:2223-2346）。</summary>
    private (NodeList? Members, Node? Prologue) TransformClassMembers(Node node)
    {
        var shouldTransformPrivateStaticElementsInClass = (EmitContext().EmitFlags(node) & EmitFlags.TransformPrivateStaticElements) != 0;

        // Declare private names
        if (_shouldTransformPrivateElementsOrClassStaticBlocks || _shouldTransformPrivateStaticElementsInFile)
        {
            foreach (var member in node.Members()!)
            {
                if (IsPrivateIdentifierClassElementDeclaration(member))
                {
                    if (ShouldTransformClassElementToWeakMap(member))
                    {
                        AddPrivateIdentifierToEnvironment(member);
                    }
                    else
                    {
                        var env = GetPrivateIdentifierEnvironment();
                        SetPrivateIdentifier(env, member.Name()!, new PrivateIdentifierInfo
                        {
                            _kind = PrivateIdentifierKind.Untransformed,
                        });
                    }
                }
            }

            if (_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                if (GetPrivateInstanceMethodsAndAccessors(node).Length > 0)
                {
                    CreateBrandCheckWeakSetForPrivateMethods();
                }
            }

            if (ShouldTransformAutoAccessorsInCurrentClass())
            {
                foreach (var member in node.Members()!)
                {
                    if (IsAutoAccessorPropertyDeclaration(member))
                    {
                        var storageName = Factory().NewGeneratedPrivateNameForNodeEx(member.Name()!, new AutoGenerateOptions { Suffix = "_accessor_storage" });
                        if (_shouldTransformPrivateElementsOrClassStaticBlocks ||
                            shouldTransformPrivateStaticElementsInClass && HasStaticModifier(member))
                        {
                            AddPrivateIdentifierPropertyDeclarationToEnvironment(member, storageName);
                        }
                        else
                        {
                            var env = GetPrivateIdentifierEnvironment();
                            // Only register as untransformed if it hasn't already been registered
                            // by the first loop (e.g., if esDecorators expanded a private auto-accessor
                            // into a backing field with the same generated name).
                            if (!TryGetPrivateIdentifier(env, storageName, out _))
                            {
                                SetPrivateIdentifier(env, storageName, new PrivateIdentifierInfo
                                {
                                    _kind = PrivateIdentifierKind.Untransformed,
                                });
                            }
                        }
                    }
                }
            }
        }

        var members = _classElementVisitor.VisitNodes(node.MemberList());

        // Create a synthetic constructor if necessary
        Node? syntheticConstructor = null;
        if (!Some(members!.Nodes, IsConstructorDeclaration))
        {
            syntheticConstructor = TransformConstructor(null, node);
        }

        // If there are pending expressions create a class static block in which to evaluate them, but only if
        // class static blocks are not also being transformed. This block will be injected at the top of the class
        // to ensure that expressions from computed property names are evaluated before any other static
        // initializers.
        Node? syntheticStaticBlock = null;
        Node? prologue = null;
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks && _pendingExpressions.Count > 0)
        {
            var statement = Factory().NewExpressionStatement(Factory().InlineExpressions(_pendingExpressions.ToArray())!);
            if ((statement.SubtreeFacts & SubtreeFacts.ContainsLexicalThisOrSuper) != 0)
            {
                // If there are `this` or `super` references from computed property names, shift the expression
                // into an arrow function to be evaluated in the outer scope so that `this` and `super` are
                // properly captured.
                var temp = Factory().NewTempVariable();
                EmitContext().AddVariableDeclaration(temp);
                var arrow = Factory().NewArrowFunction(
                    null,                           /*modifiers*/
                    null,                           /*typeParameters*/
                    Factory().NewNodeList(null),    /*parameters*/
                    null,                           /*returnType*/
                    null,                           /*fullSignature*/
                    Factory().NewToken(Kind.EqualsGreaterThanToken), /*equalsGreaterThanToken*/
                    Factory().NewBlock(Factory().NewNodeList([statement]), false /*multiline*/)
                );
                prologue = Factory().NewAssignmentExpression(temp, arrow);
                statement = Factory().NewExpressionStatement(
                    Factory().NewCallExpression(temp, null /*questionDotToken*/, null /*typeArguments*/, Factory().NewNodeList(null), NodeFlags.None)
                );
            }

            var block = Factory().NewBlock(Factory().NewNodeList([statement]), false /*multiline*/);
            syntheticStaticBlock = Factory().NewClassStaticBlockDeclaration(null /*modifiers*/, block);
            _pendingExpressions = [];
        }

        // If we created a synthetic constructor or class static block, add them to the visited members
        if (syntheticConstructor != null || syntheticStaticBlock != null)
        {
            var memberNodes = members!.Nodes;
            var membersArray = new List<Node>(memberNodes!.Length + 2);

            // Find and preserve classThis assignment block and named evaluation helper block at the top
            var classThisIdx = memberNodes is null ? -1 : Array.FindIndex(memberNodes, n => IsClassThisAssignmentBlock(EmitContext(), n));
            var namedEvalIdx = memberNodes is null ? -1 : Array.FindIndex(memberNodes, n => IsClassNamedEvaluationHelperBlock(EmitContext(), n));

            if (classThisIdx >= 0)
            {
                membersArray.Add(memberNodes![classThisIdx]);
            }
            if (namedEvalIdx >= 0)
            {
                membersArray.Add(memberNodes![namedEvalIdx]);
            }
            if (syntheticConstructor != null)
            {
                membersArray.Add(syntheticConstructor);
            }
            if (syntheticStaticBlock != null)
            {
                membersArray.Add(syntheticStaticBlock);
            }

            for (var i = 0; i < memberNodes!.Length; i++)
            {
                var member = memberNodes[i];
                if (i != classThisIdx && i != namedEvalIdx)
                {
                    membersArray.Add(member);
                }
            }
            members = Factory().NewNodeList(membersArray.ToArray());
            members.Loc = node.MemberList()!.Loc;
        }

        return (members, prologue);
    }

    /// <summary>Go: <c>createBrandCheckWeakSetForPrivateMethods</c>（classfields.go:2348-2363）。</summary>
    private void CreateBrandCheckWeakSetForPrivateMethods()
    {
        var env = GetPrivateIdentifierEnvironment();
        var weakSetName = env._data._weakSetName;
        Debug.Assert(weakSetName is not null, "weakSetName should be set in private identifier environment");

        AddPendingExpressions(
            Factory().NewAssignmentExpression(
                weakSetName!,
                Factory().NewNewExpression(
                    Factory().NewIdentifier("WeakSet"),
                    null, /*typeArguments*/
                    Factory().NewNodeList(null)
                )
            )
        );
    }

    /// <summary>Go: <c>transformConstructor</c>（classfields.go:2365-2422）。</summary>
    private Node? TransformConstructor(ConstructorDeclaration? constructor, Node container)
    {
        // NOTE: The Strada reference pre-visits the constructor via `visitNode(constructor, visitor)` before
        // checking WillHoistInitializersToConstructor. This is not done here because Go's variable environment
        // (StartVariableEnvironment/EndAndMergeVariableEnvironment) is scoped inside transformConstructorBody.
        // Pre-visiting would hoist variables outside that scope, causing them to appear after field initializers
        // instead of before. Instead, we visit parameters and body separately within the correct scopes.
        if (_lexicalEnvironment == null || _lexicalEnvironment._data == null ||
            (_lexicalEnvironment._data._facts & ClassFacts.WillHoistInitializersToConstructor) == 0)
        {
            if (constructor != null)
            {
                return Visitor().VisitEachChild(constructor.AsNode());
            }
            return null;
        }

        var extendsClauseElement = GetClassExtendsHeritageElement(container);
        var isDerivedClass = extendsClauseElement != null &&
            SkipOuterExpressions(extendsClauseElement.Expression(), OuterExpressionKinds.All)!.Kind != Kind.NullKeyword;

        NodeList? parameters = null;
        if (constructor != null)
        {
            parameters = Visitor().VisitNodes(constructor.Parameters);
        }

        var body = TransformConstructorBody(container, constructor, isDerivedClass);
        if (body == null)
        {
            if (constructor != null)
            {
                return Visitor().VisitEachChild(constructor.AsNode());
            }
            return null;
        }

        if (constructor != null)
        {
            Debug.Assert(parameters is not null);
            return Factory().UpdateConstructorDeclaration(
                constructor,
                null, /*modifiers*/
                null, /*typeParameters*/
                parameters,
                null, /*returnType*/
                null, /*fullSignature*/
                body
            );
        }

        if (parameters == null)
        {
            parameters = Factory().NewNodeList(null);
        }

        var result = Factory().NewConstructorDeclaration(
            null, /*modifiers*/
            null, /*typeParameters*/
            parameters,
            null, /*returnType*/
            null, /*fullSignature*/
            body
        );
        result.Loc = container.Loc;
        return result;
    }

    /// <summary>Go: <c>transformConstructorBodyWorker</c>（classfields.go:2424-2501）。</summary>
    private List<Node> TransformConstructorBodyWorker(
        List<Node>? statementsOut,
        Node[] statementsIn,
        int statementOffset,
        int[] superPath,
        int superPathDepth,
        List<Node> initializerStatements,
        ConstructorDeclaration constructor
    )
    {
        var superStatementIndex = superPath[superPathDepth];
        var superStatement = statementsIn[superStatementIndex];

        // Visit statements before super
        var (visited, _) = Visitor().VisitSlice(statementsIn[statementOffset..superStatementIndex]);
        statementsOut ??= [];
        statementsOut.AddRange(visited!);
        statementOffset = superStatementIndex + 1;

        if (IsTryStatement(superStatement))
        {
            var tryBlock = superStatement.AsTryStatement()!.TryBlock!.AsBlock()!;
            var tryBlockStatements = TransformConstructorBodyWorker(
                null,
                tryBlock.Statements!.Nodes!,
                0, /*statementOffset*/
                superPath,
                superPathDepth + 1,
                initializerStatements,
                constructor
            );
            var tryStatementList = Factory().NewNodeList(tryBlockStatements.ToArray());
            tryStatementList.Loc = tryBlock.Statements.Loc;

            var catchClause = Visitor().VisitNode(superStatement.AsTryStatement()!.CatchClause);
            var finallyBlock = Visitor().VisitNode(superStatement.AsTryStatement()!.FinallyBlock);

            var updated = Factory().UpdateTryStatement(
                superStatement.AsTryStatement()!,
                Factory().UpdateBlock(tryBlock, tryStatementList, tryBlock.MultiLine),
                catchClause,
                finallyBlock
            );
            statementsOut.Add(updated);
        }
        else
        {
            var (visitedSuper, _) = Visitor().VisitSlice(statementsIn[superStatementIndex..(superStatementIndex + 1)]);
            statementsOut.AddRange(visitedSuper!);

            // Add the property initializers. Transforms this:
            //
            //  public x = 1;
            //
            // Into this:
            //
            //  constructor() {
            //      this.x = 1;
            //  }
            //
            // If we do useDefineForClassFields, they'll be converted elsewhere.
            // We instead *remove* them from the transformed output at this stage.

            // parameter-property assignments should occur immediately after the prologue and `super()`,
            // so only count the statements that immediately follow.
            while (statementOffset < statementsIn.Length)
            {
                var stmt = statementsIn[statementOffset];
                var orig = EmitContext().MostOriginal(stmt);
                if (IsParameterPropertyDeclaration(orig, constructor.AsNode()))
                {
                    statementOffset++;
                }
                else
                {
                    break;
                }
            }

            statementsOut.AddRange(initializerStatements);
        }

        // Visit remaining statements
        var (visited2, _) = Visitor().VisitSlice(statementsIn[statementOffset..]);
        statementsOut.AddRange(visited2!);
        return statementsOut;
    }

    /// <summary>Go: <c>transformConstructorBody</c>（classfields.go:2503-2635）。</summary>
    private Node? TransformConstructorBody(Node container, ConstructorDeclaration? constructor, bool isDerivedClass)
    {
        var instanceProperties = GetProperties(container, false /*requireInitializer*/, false /*isStatic*/);
        var properties = instanceProperties;
        if (!_compilerOptions.GetUseDefineForClassFields())
        {
            properties = Filter(properties, prop => prop.Initializer() != null || IsPrivateIdentifier(prop.Name()!) || HasAccessorModifier(prop));
        }

        var privateMethodsAndAccessors = GetPrivateInstanceMethodsAndAccessors(container);
        var needsConstructorBody = properties.Length > 0 || privateMethodsAndAccessors.Length > 0;

        // Only generate synthetic constructor when there are property initializers to move.
        if (constructor == null && !needsConstructorBody)
        {
            return EmitContext().VisitFunctionBody(null!, Visitor());
        }

        EmitContext().StartVariableEnvironment();

        var needsSyntheticConstructor = constructor == null && isDerivedClass;
        var statements = new List<Node>();

        // Add the property initializers. Transforms this:
        //
        //  public x = 1;
        //
        // Into this:
        //
        //  constructor() {
        //      this.x = 1;
        //  }
        //
        var initializerStatements = new List<Node>();
        var receiver = Factory().NewThisExpression();

        // private methods can be called in property initializers, they should execute first
        initializerStatements = AddInstanceMethodStatements(initializerStatements, privateMethodsAndAccessors, receiver);

        if (constructor != null)
        {
            var parameterProperties = Filter(instanceProperties, prop => IsParameterPropertyDeclaration(EmitContext().MostOriginal(prop), constructor.AsNode()));
            var nonParameterProperties = Filter(properties, prop => !IsParameterPropertyDeclaration(EmitContext().MostOriginal(prop), constructor.AsNode()));
            initializerStatements = AddPropertyOrClassStaticBlockStatements(initializerStatements, parameterProperties, receiver);
            initializerStatements = AddPropertyOrClassStaticBlockStatements(initializerStatements, nonParameterProperties, receiver);
        }
        else
        {
            initializerStatements = AddPropertyOrClassStaticBlockStatements(initializerStatements, properties, receiver);
        }

        if (constructor != null && constructor.Body != null)
        {
            var body = constructor.Body.AsBlock()!;

            // Copy prologue
            foreach (var stmt in body.Statements!.Nodes!)
            {
                if (IsPrologueDirective(stmt))
                {
                    statements.Add(stmt);
                }
                else
                {
                    break;
                }
            }
            var statementOffset = statements.Count;

            var superPath = FindSuperStatementIndexPath(body.Statements.Nodes!, statementOffset);
            if (superPath.Length > 0)
            {
                statements = TransformConstructorBodyWorker(statements, body.Statements.Nodes!, statementOffset, superPath, 0, initializerStatements, constructor);
            }
            else
            {
                // parameter-property assignments should occur immediately after the prologue and `super()`,
                // so only count the statements that immediately follow.
                while (statementOffset < body.Statements.Nodes!.Length)
                {
                    var stmt = body.Statements.Nodes[statementOffset];
                    var orig = EmitContext().MostOriginal(stmt);
                    if (IsParameterPropertyDeclaration(orig, constructor.AsNode()))
                    {
                        statementOffset++;
                    }
                    else
                    {
                        break;
                    }
                }
                statements.AddRange(initializerStatements);
                var (visited, _) = Visitor().VisitSlice(body.Statements.Nodes![statementOffset..]);
                statements.AddRange(visited!);
            }
        }
        else
        {
            if (needsSyntheticConstructor)
            {
                // Add a synthetic `super` call:
                //
                //  super(...arguments);
                //
                var superCall = Factory().NewExpressionStatement(
                    Factory().NewCallExpression(
                        Factory().NewKeywordExpression(Kind.SuperKeyword),
                        null, /*typeArguments*/
                        null, /*questionDotToken*/
                        Factory().NewNodeList([Factory().NewSpreadElement(Factory().NewIdentifier("arguments"))]),
                        NodeFlags.None
                    )
                );
                statements.Add(superCall);
            }
            statements.AddRange(initializerStatements);
        }

        // Go 的 statements 在此变成 EndAndMergeVariableEnvironment 返回的切片（C# 用数组承接，后文 len(statements) → Length）。
        Node[] finalStatements = EmitContext().EndAndMergeVariableEnvironment(statements.ToArray()) ?? [];

        if (finalStatements.Length == 0 && constructor == null)
        {
            return null;
        }

        bool multiLine;
        if (constructor != null && constructor.Body != null &&
            constructor.Body.AsBlock()!.Statements!.Nodes!.Length >= finalStatements.Length)
        {
            multiLine = constructor.Body.AsBlock()!.MultiLine;
        }
        else
        {
            multiLine = finalStatements.Length > 0;
        }

        var statementList = Factory().NewNodeList(finalStatements);
        if (constructor != null && constructor.Body != null)
        {
            statementList.Loc = constructor.Body.AsBlock()!.Statements!.Loc;
        }
        else
        {
            statementList.Loc = NewTextRange(container.MemberList()!.Loc.Pos(), container.MemberList()!.Loc.End());
        }

        var block = Factory().NewBlock(statementList, multiLine);
        if (constructor != null && constructor.Body != null)
        {
            block.Loc = constructor.Body.Loc;
        }
        return block;
    }

    /// <summary>Go: <c>addPropertyOrClassStaticBlockStatements</c>（classfields.go:2637-2648）——为属性初始化器生成赋值语句。</summary>
    private List<Node> AddPropertyOrClassStaticBlockStatements(List<Node> statements, Node[] properties, Node receiver)
    {
        foreach (var property in properties)
        {
            if (IsStatic(property) && !_shouldTransformPrivateElementsOrClassStaticBlocks)
            {
                continue;
            }
            var statement = TransformPropertyOrClassStaticBlock(property, receiver);
            if (statement != null)
            {
                statements.Add(statement);
            }
        }
        return statements;
    }
}
