// ported from: internal/transformers/estransforms/classfields.go:2650-3618（属性/类静态块变换、私有名环境与
//              hoisting、解构赋值目标改写、访问器重定向，以及 classfields.go 的包级 helper）
//
// Go→C# 语义改写（本分片）：
// 1) Go 未导出成员 → C# PascalCase 方法 / `_camelCase` 字段；同一 partial 类的成员一律 `private`（C# 的 private
//    是类型级，跨 partial 文件可访问）。Go 的包级函数（无接收者）→ `EsTransforms` 静态类的 `internal static` 成员。
// 2) `*ast.Expression`/`*ast.Statement`/`*ast.IdentifierNode`/`*ast.PropertyName`/`*ast.ModifierList` 等别名
//    一律 `Node`（docs/ast.md §2：C# 不生成 Kind 别名）；`*ast.ModifierList` 需要保形处用 `ModifierList?`。
// 3) `panic`/`debug.Fail` → `Debug.Fail(...)`；`Debug.Fail` 之后方法体末尾补 `return null;`/`return node;` 以满足控制流。
// 4) `(T, bool)` 返回 → `bool TryX(..., out T)`（`getPrivateIdentifier` → `TryGetPrivateIdentifier`）。
//    Go 里 `previousInfo, _ := tx.getPrivateIdentifier(env, name)` 也走同一个 TryX（丢弃 bool）。
// 5) Go 多返回值 `(thisArg, target)` → C# 命名元组 `(Node ThisArg, Node Target)`（`createCallBinding`）。
// 6) Go 的 `[]*ast.Expression`/`[]*ast.Node` 返回：与 `core.Some/Filter`（形参是 `T[]`）配合的用 `Node[]`；
//    需要就地追加再返回的（`addInstanceMethodStatements`）用 `List<Node>`。局部累积用 `List<Node>` + `.ToArray()`。
// 7) `printer.AutoGenerateOptions{...}` → `new AutoGenerateOptions { ... }`（struct，字段用对象初始化器）；**不用** `default`。
// 8) `printer.PrivateIdentifierKindXxx` → `PrivateIdentifierKind.Xxx`；struct 无 `==` 且 static readonly 不能做 `case`
//    常量，故比较统一用 ClassFields.cs 的 `IsPrivateKind(a, b)`，Go 的 `switch info.kind` 改写成 if/else。
// 9) `setPrivateIdentifier(env, x, &privateIdentifierInfo{...})` → `SetPrivateIdentifier(env, x, new PrivateIdentifierInfo { ... })`。
// 10) Go 的 nil 切片 → `List<Node>`（`_pendingExpressions.AddRange`），`Factory().NewNodeList(nil)` → `NewNodeList(null)`。
// 11) `for env := tx.lexicalEnvironment; env != nil; env = env.previous` → `for (var env = _lexicalEnvironment; env != null; env = env._previous)`。
// 12) Go 的 `iter.Seq[*ast.Expression]` → `IEnumerable<Node>`：`FlattenCommaList` 用 C# 迭代器 `yield return`
//     复刻 push 模型的短路（消费方 break ⇒ 停止产出）；`FlattenCommaListWorker` 保留 Go 的
//     `func(node, yield func(*ast.Expression) bool) bool` 形态供 1:1 对照。
// 13) `createMemberAccessForPropertyName` 里 Go 在两个分支各声明一次 `expression`（内层 `:=` 遮蔽）；C# 同一块内
//     重复声明同名局部是 CS0136，故合并成一个 `Node expression`（行为完全一致：内层分支立即 return）。
// 14) Go 的 `ast.SkipOuterExpressions(node, 0)`：C# 的 kinds 是 `OuterExpressionKinds` 枚举，字面量 0 可隐式转换，
//     语义同为"不跳过任何外层表达式"（`IsOuterExpression` 对 0 恒 false）。
// 15) `NodeFactory` 在 C# 里有 `Ast.NodeFactory` 与 `Printer.NodeFactory` 两个同名类型 → 恢复 Go 的显式限定别名。
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
using PrivateIdentifierKind = TypeScript.Printer.PrivateIdentifierKind;

namespace TypeScript.Transformers.EsTransforms;

public sealed partial class ClassFieldsTransformer : Transformer
{
    /// <summary>
    /// Go: <c>func (tx *classFieldsTransformer) transformPropertyOrClassStaticBlock(property *ast.Node, receiver *ast.Expression) *ast.Node</c>（classfields.go:2650-2688）。
    /// </summary>
    private Node? TransformPropertyOrClassStaticBlock(Node property, Node receiver)
    {
        Node? expression;
        if (IsClassStaticBlockDeclaration(property))
        {
            expression = SetCurrentClassElementAnd(property, TransformClassStaticBlockDeclaration, property);
        }
        else
        {
            expression = TransformProperty(property, receiver);
        }
        if (expression == null)
        {
            return null;
        }

        var statement = Factory().NewExpressionStatement(expression);
        EmitContext().SetOriginal(statement, property);
        EmitContext().AddEmitFlags(statement, EmitContext().EmitFlags(property) & EmitFlags.NoComments);
        EmitContext().SetCommentRange(statement, property.Loc);

        var propertyOriginalNode = EmitContext().MostOriginal(property)!;
        if (IsParameterDeclaration(propertyOriginalNode))
        {
            EmitContext().SetSourceMapRange(statement, propertyOriginalNode.Loc);
            EmitContext().AddEmitFlags(statement, EmitFlags.NoComments);
        }
        else
        {
            EmitContext().SetSourceMapRange(statement, MoveRangePastModifiers(property));
        }

        // `setOriginalNode` *copies* the `emitNode` from `property`, so now both
        // `statement` and `expression` have a copy of the synthesized comments.
        // Drop the comments from expression to avoid printing them twice.
        EmitContext().SetSyntheticLeadingComments(expression, null);
        EmitContext().SetSyntheticTrailingComments(expression, null);

        // If the property was originally an auto-accessor, don't emit comments here since they will be attached to
        // the synthesized getter.
        if (HasAccessorModifier(propertyOriginalNode))
        {
            EmitContext().AddEmitFlags(statement, EmitFlags.NoComments);
        }

        return statement;
    }

    // generateInitializedPropertyExpressionsOrClassStaticBlock generates assignment expressions for property initializers.
    /// <summary>Go: <c>func (tx *classFieldsTransformer) generateInitializedPropertyExpressionsOrClassStaticBlock(propertiesOrClassStaticBlocks []*ast.Node, receiver *ast.Expression) []*ast.Expression</c>（classfields.go:2690-2711）。</summary>
    private Node[] GenerateInitializedPropertyExpressionsOrClassStaticBlock(Node[] propertiesOrClassStaticBlocks, Node receiver)
    {
        var expressions = new List<Node>();
        foreach (var property in propertiesOrClassStaticBlocks)
        {
            Node? expression;
            if (IsClassStaticBlockDeclaration(property))
            {
                expression = SetCurrentClassElementAnd(property, TransformClassStaticBlockDeclaration, property);
            }
            else
            {
                expression = TransformProperty(property, receiver);
            }
            if (expression == null)
            {
                continue;
            }
            EmitContext().SetOriginalEx(expression, property, true /*allowOverwrite*/);
            EmitContext().AssignCommentAndSourceMapRanges(expression, property);
            expressions.Add(expression);
        }
        return expressions.ToArray();
    }

    // transformProperty transforms a property initializer into an assignment expression.
    /// <summary>Go: <c>func (tx *classFieldsTransformer) transformProperty(property *ast.PropertyDeclaration, receiver *ast.Expression) *ast.Expression</c>（classfields.go:2713-2727）。</summary>
    private Node? TransformProperty(Node property, Node receiver)
    {
        var savedCurrentClassElement = _currentClassElement;
        var transformed = TransformPropertyWorker(property, receiver);
        if (transformed != null && HasStaticModifier(property))
        {
            EmitContext().AddEmitFlags(transformed, EmitFlags.NoLexicalThis);
        }
        if (transformed != null && HasStaticModifier(property) &&
            _lexicalEnvironment != null && _lexicalEnvironment._data != null && _lexicalEnvironment._data._facts != 0)
        {
            // capture the lexical environment for the member
            EmitContext().SetOriginal(transformed, property);
            EmitContext().SetSourceMapRange(transformed, EmitContext().SourceMapRange(property.Name()!));
        }
        _currentClassElement = savedCurrentClassElement;
        return transformed;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) transformPropertyWorker(property *ast.PropertyDeclaration, receiver *ast.Expression) *ast.Expression</c>（classfields.go:2729-2834）。</summary>
    private Node? TransformPropertyWorker(Node property, Node receiver)
    {
        // We generate a name here in order to reuse the value cached by the relocated computed name expression (which uses the same generated name)
        var emitAssignment = !_compilerOptions.GetUseDefineForClassFields();

        if (IsNamedEvaluationAnd(EmitContext(), property, _isAnonymousClassNeedingAssignedName))
        {
            property = TransformNamedEvaluation(EmitContext(), property, false, "").AsPropertyDeclaration()!;
        }

        var propertyName = property.Name()!;
        if (HasAccessorModifier(property))
        {
            propertyName = Factory().NewGeneratedPrivateNameForNodeEx(property.Name()!, new AutoGenerateOptions { Suffix = "_accessor_storage" });
        }
        else if (IsComputedPropertyName(propertyName) && !IsSimpleInlineableExpression(propertyName.Expression()!))
        {
            propertyName = Factory().UpdateComputedPropertyName(
                propertyName.AsComputedPropertyName()!,
                Factory().NewGeneratedNameForNode(propertyName));
        }

        if (HasStaticModifier(property))
        {
            _currentClassElement = property;
        }

        if (IsPrivateIdentifier(propertyName) && ShouldTransformClassElementToWeakMap(property))
        {
            var info = AccessPrivateIdentifier(propertyName);
            if (info != null)
            {
                if (IsPrivateKind(info._kind, PrivateIdentifierKind.Field))
                {
                    if (!info._isStatic)
                    {
                        return CreatePrivateInstanceFieldInitializer(
                            Factory(),
                            receiver,
                            Visitor().VisitNode(property.Initializer()),
                            info._brandCheckIdentifier!);
                    }
                    return CreatePrivateStaticFieldInitializer(
                        Factory(),
                        info._variableName,
                        Visitor().VisitNode(property.Initializer()));
                }
                return null;
            }
            else
            {
                Debug.Fail("Undeclared private name for property declaration.");
            }
        }

        if ((IsPrivateIdentifier(propertyName) || HasStaticModifier(property)) && property.Initializer() == null)
        {
            return null;
        }

        // TODO: can we get rid of this original checking and better coordinate with runtimesyntax?
        if (HasAbstractModifier(EmitContext().MostOriginal(property)!))
        {
            return null;
        }

        var initializer = Visitor().VisitNode(property.Initializer());
        var propertyOriginalNode = EmitContext().MostOriginal(property)!;
        if (IsParameterPropertyDeclaration(propertyOriginalNode, propertyOriginalNode.Parent) && IsIdentifier(propertyName))
        {
            // A parameter-property declaration always overrides the initializer. The only time a parameter-property
            // declaration *should* have an initializer is when decorators have added initializers that need to run before
            // any other initializer
            var localName = propertyName.Clone(Factory())!;
            if (initializer != null)
            {
                // unwrap `(__runInitializers(this, _instanceExtraInitializers), void 0)`
                if (IsParenthesizedExpression(initializer) &&
                    IsCommaExpression(initializer.Expression()!) &&
                    EmitContext().IsCallToHelper(initializer.Expression()!.AsBinaryExpression()!.Left!, "__runInitializers") &&
                    IsVoidExpression(initializer.Expression()!.AsBinaryExpression()!.Right!) &&
                    IsNumericLiteral(initializer.Expression()!.AsBinaryExpression()!.Right!.Expression()!))
                {
                    initializer = initializer.Expression()!.AsBinaryExpression()!.Left;
                }
                initializer = Factory().InlineExpressions(new Node[] { initializer!, localName })!;
            }
            else
            {
                initializer = localName;
            }
            EmitContext().AddEmitFlags(propertyName, EmitFlags.NoComments | EmitFlags.NoSourceMap);
            EmitContext().SetSourceMapRange(localName, propertyOriginalNode.Name()!.Loc);
            EmitContext().AddEmitFlags(localName, EmitFlags.NoComments);
        }
        else if (initializer == null)
        {
            initializer = Factory().NewVoidZeroExpression();
        }

        if (emitAssignment || IsPrivateIdentifier(propertyName))
        {
            var memberAccess = CreateMemberAccessForPropertyName(Factory(), EmitContext(), receiver, propertyName, propertyName);
            EmitContext().AddEmitFlags(memberAccess, EmitFlags.NoLeadingComments);
            return Factory().NewAssignmentExpression(memberAccess, initializer);
        }

        // useDefineForClassFields: Object.defineProperty
        Node name;
        if (IsComputedPropertyName(propertyName))
        {
            name = propertyName.Expression()!;
        }
        else if (IsIdentifier(propertyName))
        {
            name = Factory().NewStringLiteral(propertyName.Text(), TokenFlags.None);
        }
        else
        {
            name = propertyName;
        }
        var descriptor = Factory().NewObjectLiteralExpression(Factory().NewNodeList(new Node[]
        {
            Factory().NewPropertyAssignment(null, Factory().NewIdentifier("enumerable"), null, null, Factory().NewTrueExpression()),
            Factory().NewPropertyAssignment(null, Factory().NewIdentifier("configurable"), null, null, Factory().NewTrueExpression()),
            Factory().NewPropertyAssignment(null, Factory().NewIdentifier("writable"), null, null, Factory().NewTrueExpression()),
            Factory().NewPropertyAssignment(null, Factory().NewIdentifier("value"), null, null, initializer),
        }), true);
        return Factory().NewObjectDefinePropertyCall(receiver, name, descriptor);
    }

    // addInstanceMethodStatements generates brand-check initializer for private methods.
    /// <summary>Go: <c>func (tx *classFieldsTransformer) addInstanceMethodStatements(statements []*ast.Statement, methods []*ast.Node, receiver *ast.Expression) []*ast.Statement</c>（classfields.go:2836-2851）。</summary>
    private List<Node> AddInstanceMethodStatements(List<Node> statements, Node[] methods, Node receiver)
    {
        if (!_shouldTransformPrivateElementsOrClassStaticBlocks || methods.Length == 0)
        {
            return statements;
        }

        var env = GetPrivateIdentifierEnvironment();
        var weakSetName = env._data._weakSetName;
        Debug.Assert(weakSetName != null, "weakSetName should be set in private identifier environment");

        statements.Add(
            Factory().NewExpressionStatement(
                CreatePrivateInstanceMethodInitializer(Factory(), receiver, weakSetName!)));
        return statements;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitInvalidSuperProperty(node *ast.Node) *ast.Node</c>（classfields.go:2853-2874）。</summary>
    private Node VisitInvalidSuperProperty(Node node)
    {
        if (IsPropertyAccessExpression(node))
        {
            return Factory().UpdatePropertyAccessExpression(
                node.AsPropertyAccessExpression()!,
                Factory().NewVoidZeroExpression(),
                null,
                node.Name(),
                node.Flags);
        }
        return Factory().UpdateElementAccessExpression(
            node.AsElementAccessExpression()!,
            Factory().NewVoidZeroExpression(),
            null,
            Visitor().VisitNode(node.AsElementAccessExpression()!.ArgumentExpression),
            node.Flags);
    }

    // getPropertyNameExpressionIfNeeded transforms a computed property name, then either returns an expression
    // which caches the value of the result or the expression itself if the value is either unused or safe to
    // inline into multiple locations.
    // shouldHoist indicates whether the expression needs to be reused (i.e., for an initializer or a decorator).
    /// <summary>Go: <c>func (tx *classFieldsTransformer) getPropertyNameExpressionIfNeeded(name *ast.PropertyName, shouldHoist bool) *ast.Expression</c>（classfields.go:2876-2908）。</summary>
    private Node? GetPropertyNameExpressionIfNeeded(Node name, bool shouldHoist)
    {
        if (!IsComputedPropertyName(name))
        {
            return null;
        }
        var cacheAssignment = FindComputedPropertyNameCacheAssignment(EmitContext(), name);
        // Switch to outer lex env for computed property name expressions, matching
        // Strada reference's onEmitNode behavior for ComputedPropertyName.
        var savedLexicalEnvironment = _lexicalEnvironment;
        var savedInsideComputedPropertyName = _insideComputedPropertyName;
        _insideComputedPropertyName = true;
        if (_lexicalEnvironment != null && _lexicalEnvironment._previous != null)
        {
            _lexicalEnvironment = _lexicalEnvironment._previous;
        }
        var expression = Visitor().VisitNode(name.Expression());
        _lexicalEnvironment = savedLexicalEnvironment;
        _insideComputedPropertyName = savedInsideComputedPropertyName;
        var innerExpression = SkipPartiallyEmittedExpressions(expression)!;
        var inlinable = IsSimpleInlineableExpression(innerExpression);
        var alreadyTransformed = cacheAssignment != null || (IsAssignmentExpression(innerExpression, true /*excludeCompoundAssignment*/) && IsIdentifier(innerExpression.AsBinaryExpression()!.Left!) && IsGeneratedIdentifier(EmitContext(), innerExpression.AsBinaryExpression()!.Left!));
        if (!alreadyTransformed && !inlinable && shouldHoist)
        {
            var generatedName = Factory().NewGeneratedNameForNode(name);
            if (RequiresBlockScopedVar())
            {
                EmitContext().AddLexicalDeclaration(generatedName);
            }
            else
            {
                EmitContext().AddVariableDeclaration(generatedName);
            }
            return Factory().NewAssignmentExpression(generatedName, expression!);
        }
        if (inlinable || IsIdentifier(innerExpression))
        {
            return null;
        }
        return expression;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) startClassLexicalEnvironment()</c>（classfields.go:2910-2912）。</summary>
    private void StartClassLexicalEnvironment()
    {
        _lexicalEnvironment = new ClassLexicalEnv { _previous = _lexicalEnvironment };
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) endClassLexicalEnvironment()</c>（classfields.go:2914-2916）。</summary>
    private void EndClassLexicalEnvironment()
    {
        _lexicalEnvironment = _lexicalEnvironment!._previous;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) getClassLexicalEnvironment() *classLexicalEnvironment</c>（classfields.go:2918-2924）。</summary>
    private ClassLexicalEnvironment GetClassLexicalEnvironment()
    {
        Debug.Assert(_lexicalEnvironment != null);
        if (_lexicalEnvironment!._data == null)
        {
            _lexicalEnvironment._data = new ClassLexicalEnvironment();
        }
        return _lexicalEnvironment._data!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) getPrivateIdentifierEnvironment() *privateEnvironment</c>（classfields.go:2926-2934）。</summary>
    private PrivateEnvironment GetPrivateIdentifierEnvironment()
    {
        Debug.Assert(_lexicalEnvironment != null);
        if (_lexicalEnvironment!._privateEnv == null)
        {
            _lexicalEnvironment._privateEnv = new PrivateEnvironment
            {
                _members = new Dictionary<string, PrivateIdentifierInfo>(),
            };
        }
        return _lexicalEnvironment._privateEnv!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) addPendingExpressions(exprs ...*ast.Expression)</c>（classfields.go:2936-2938）。</summary>
    private void AddPendingExpressions(params Node[] exprs)
    {
        _pendingExpressions.AddRange(exprs);
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) addPrivateIdentifierPropertyDeclarationToEnvironment(node *ast.Node, name *ast.Node)</c>（classfields.go:2940-2979）。</summary>
    private void AddPrivateIdentifierPropertyDeclarationToEnvironment(Node node, Node name)
    {
        var lex = GetClassLexicalEnvironment();
        var env = GetPrivateIdentifierEnvironment();
        var isStatic = HasStaticModifier(node);
        TryGetPrivateIdentifier(env, name, out var previousInfo);
        var isValid = !IsReservedPrivateName(name) && previousInfo == null;

        if (isStatic)
        {
            var brandCheckIdentifier = lex._classThis;
            if (brandCheckIdentifier == null)
            {
                brandCheckIdentifier = lex._classConstructor;
            }
            var variableName = CreateHoistedVariableForPrivateName(name, "");
            SetPrivateIdentifier(env, name, new PrivateIdentifierInfo
            {
                _kind = PrivateIdentifierKind.Field,
                _isStatic = true,
                _brandCheckIdentifier = brandCheckIdentifier,
                _variableName = variableName,
                _isValid = isValid,
            });
        }
        else
        {
            var weakMapName = CreateHoistedVariableForPrivateName(name, "");
            SetPrivateIdentifier(env, name, new PrivateIdentifierInfo
            {
                _kind = PrivateIdentifierKind.Field,
                _isStatic = false,
                _brandCheckIdentifier = weakMapName,
                _isValid = isValid,
            });
            AddPendingExpressions(
                Factory().NewAssignmentExpression(
                    weakMapName,
                    Factory().NewNewExpression(
                        Factory().NewIdentifier("WeakMap"),
                        null, /*typeArguments*/
                        Factory().NewNodeList(null))));
        }
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) addPrivateIdentifierMethodToEnvironment(name *ast.Node, lex *classLexicalEnvironment, env *privateEnvironment, isStatic bool, isValid bool)</c>（classfields.go:2981-3000）。</summary>
    private void AddPrivateIdentifierMethodToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment env, bool isStatic, bool isValid)
    {
        var methodName = CreateHoistedVariableForPrivateName(name, "");
        Node? brandCheckIdentifier;
        if (isStatic)
        {
            brandCheckIdentifier = lex._classThis;
            if (brandCheckIdentifier == null)
            {
                brandCheckIdentifier = lex._classConstructor;
            }
            Debug.Assert(brandCheckIdentifier != null, "classConstructor should be set in private identifier environment");
        }
        else
        {
            brandCheckIdentifier = env._data._weakSetName;
        }
        SetPrivateIdentifier(env, name, new PrivateIdentifierInfo
        {
            _kind = PrivateIdentifierKind.Method,
            _methodName = methodName,
            _brandCheckIdentifier = brandCheckIdentifier,
            _isStatic = isStatic,
            _isValid = isValid,
        });
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) addPrivateIdentifierGetAccessorToEnvironment(name *ast.Node, lex *classLexicalEnvironment, env *privateEnvironment, isStatic bool, isValid bool, previousInfo *privateIdentifierInfo)</c>（classfields.go:3002-3027）。</summary>
    private void AddPrivateIdentifierGetAccessorToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment env, bool isStatic, bool isValid, PrivateIdentifierInfo? previousInfo)
    {
        var getterName = CreateHoistedVariableForPrivateName(name, "_get");
        Node? brandCheckIdentifier;
        if (isStatic)
        {
            brandCheckIdentifier = lex._classThis;
            if (brandCheckIdentifier == null)
            {
                brandCheckIdentifier = lex._classConstructor;
            }
            Debug.Assert(brandCheckIdentifier != null, "classConstructor should be set in private identifier environment");
        }
        else
        {
            brandCheckIdentifier = env._data._weakSetName;
            Debug.Assert(brandCheckIdentifier != null, "weakSetName should be set in private identifier environment");
        }

        if (previousInfo != null && IsPrivateKind(previousInfo._kind, PrivateIdentifierKind.Accessor) && previousInfo._isStatic == isStatic && previousInfo._getterName == null)
        {
            previousInfo._getterName = getterName;
        }
        else
        {
            SetPrivateIdentifier(env, name, new PrivateIdentifierInfo
            {
                _kind = PrivateIdentifierKind.Accessor,
                _getterName = getterName,
                _brandCheckIdentifier = brandCheckIdentifier,
                _isStatic = isStatic,
                _isValid = isValid,
            });
        }
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) addPrivateIdentifierSetAccessorToEnvironment(name *ast.Node, lex *classLexicalEnvironment, env *privateEnvironment, isStatic bool, isValid bool, previousInfo *privateIdentifierInfo)</c>（classfields.go:3029-3054）。</summary>
    private void AddPrivateIdentifierSetAccessorToEnvironment(Node name, ClassLexicalEnvironment lex, PrivateEnvironment env, bool isStatic, bool isValid, PrivateIdentifierInfo? previousInfo)
    {
        var setterName = CreateHoistedVariableForPrivateName(name, "_set");
        Node? brandCheckIdentifier;
        if (isStatic)
        {
            brandCheckIdentifier = lex._classThis;
            if (brandCheckIdentifier == null)
            {
                brandCheckIdentifier = lex._classConstructor;
            }
            Debug.Assert(brandCheckIdentifier != null, "classConstructor should be set in private identifier environment");
        }
        else
        {
            brandCheckIdentifier = env._data._weakSetName;
            Debug.Assert(brandCheckIdentifier != null, "weakSetName should be set in private identifier environment");
        }

        if (previousInfo != null && IsPrivateKind(previousInfo._kind, PrivateIdentifierKind.Accessor) && previousInfo._isStatic == isStatic && previousInfo._setterName == null)
        {
            previousInfo._setterName = setterName;
        }
        else
        {
            SetPrivateIdentifier(env, name, new PrivateIdentifierInfo
            {
                _kind = PrivateIdentifierKind.Accessor,
                _setterName = setterName,
                _brandCheckIdentifier = brandCheckIdentifier,
                _isStatic = isStatic,
                _isValid = isValid,
            });
        }
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) addPrivateIdentifierAutoAccessorToEnvironment(node *ast.Node, name *ast.Node, lex *classLexicalEnvironment, env *privateEnvironment, isStatic bool, isValid bool)</c>（classfields.go:3056-3079）。</summary>
    private void AddPrivateIdentifierAutoAccessorToEnvironment(Node node, Node name, ClassLexicalEnvironment lex, PrivateEnvironment env, bool isStatic, bool isValid)
    {
        var getterName = CreateHoistedVariableForPrivateName(name, "_get");
        var setterName = CreateHoistedVariableForPrivateName(name, "_set");
        Node? brandCheckIdentifier;
        if (isStatic)
        {
            brandCheckIdentifier = lex._classThis;
            if (brandCheckIdentifier == null)
            {
                brandCheckIdentifier = lex._classConstructor;
            }
            Debug.Assert(brandCheckIdentifier != null, "classConstructor should be set in private identifier environment");
        }
        else
        {
            brandCheckIdentifier = env._data._weakSetName;
            Debug.Assert(brandCheckIdentifier != null, "weakSetName should be set in private identifier environment");
        }

        SetPrivateIdentifier(env, name, new PrivateIdentifierInfo
        {
            _kind = PrivateIdentifierKind.Accessor,
            _getterName = getterName,
            _setterName = setterName,
            _brandCheckIdentifier = brandCheckIdentifier,
            _isStatic = isStatic,
            _isValid = isValid,
        });
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) addPrivateIdentifierToEnvironment(node *ast.Node)</c>（classfields.go:3081-3100）。</summary>
    private void AddPrivateIdentifierToEnvironment(Node node)
    {
        var lex = GetClassLexicalEnvironment();
        var env = GetPrivateIdentifierEnvironment();
        var name = node.Name()!;
        var isStatic = HasStaticModifier(node);
        TryGetPrivateIdentifier(env, name, out var previousInfo);
        var isValid = !IsReservedPrivateName(name) && previousInfo == null;

        if (IsAutoAccessorPropertyDeclaration(node))
        {
            AddPrivateIdentifierAutoAccessorToEnvironment(node, name, lex, env, isStatic, isValid);
        }
        else if (IsPropertyDeclaration(node))
        {
            AddPrivateIdentifierPropertyDeclarationToEnvironment(node, name);
        }
        else if (IsMethodDeclaration(node))
        {
            AddPrivateIdentifierMethodToEnvironment(name, lex, env, isStatic, isValid);
        }
        else if (IsGetAccessorDeclaration(node))
        {
            AddPrivateIdentifierGetAccessorToEnvironment(name, lex, env, isStatic, isValid, previousInfo);
        }
        else if (IsSetAccessorDeclaration(node))
        {
            AddPrivateIdentifierSetAccessorToEnvironment(name, lex, env, isStatic, isValid, previousInfo);
        }
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) setPrivateIdentifier(env *privateEnvironment, name *ast.Node, info *privateIdentifierInfo)</c>（classfields.go:3102-3111）。</summary>
    private void SetPrivateIdentifier(PrivateEnvironment env, Node name, PrivateIdentifierInfo info)
    {
        if (EmitContext().HasAutoGenerateInfo(name))
        {
            if (env._generatedIdentifiers == null)
            {
                env._generatedIdentifiers = new Dictionary<Node, PrivateIdentifierInfo>();
            }
            env._generatedIdentifiers[EmitContext().GetNodeForGeneratedName(name)] = info;
        }
        else
        {
            env._members[name.Text()] = info;
        }
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) getPrivateIdentifier(env *privateEnvironment, name *ast.Node) (*privateIdentifierInfo, bool)</c>（classfields.go:3113-3120）。</summary>
    private bool TryGetPrivateIdentifier(PrivateEnvironment env, Node name, out PrivateIdentifierInfo? info)
    {
        if (EmitContext().HasAutoGenerateInfo(name))
        {
            // Go 读 nil map 返回 (nil, false)。
            if (env._generatedIdentifiers == null)
            {
                info = null;
                return false;
            }
            return env._generatedIdentifiers.TryGetValue(EmitContext().GetNodeForGeneratedName(name), out info);
        }
        info = null;
        return env._members.TryGetValue(name.Text(), out info);
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) createHoistedVariableForClass(nameText string, node *ast.Node, suffix string) *ast.IdentifierNode</c>（classfields.go:3122-3143）。</summary>
    private Node CreateHoistedVariableForClass(string nameText, Node node, string suffix)
    {
        var env = GetPrivateIdentifierEnvironment();
        Node identifier;
        if (env._data._className != null)
        {
            var prefix = "_" + env._data._className.Text() + "_";
            identifier = Factory().NewUniqueNameEx(prefix + nameText, new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes,
                Suffix = suffix,
            });
        }
        else
        {
            identifier = Factory().NewUniqueNameEx("_" + nameText, new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes,
                Suffix = suffix,
            });
        }
        if (RequiresBlockScopedVar())
        {
            EmitContext().AddLexicalDeclaration(identifier);
        }
        else
        {
            EmitContext().AddVariableDeclaration(identifier);
        }
        return identifier;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) createHoistedVariableForClassFromNode(name *ast.Node, suffix string) *ast.IdentifierNode</c>（classfields.go:3145-3164）。</summary>
    private Node CreateHoistedVariableForClassFromNode(Node name, string suffix)
    {
        var env = GetPrivateIdentifierEnvironment();
        string prefix;
        if (env._data._className != null)
        {
            prefix = "_" + env._data._className.Text() + "_";
        }
        else
        {
            prefix = "_";
        }
        var identifier = Factory().NewGeneratedNameForNodeEx(name, new AutoGenerateOptions
        {
            Flags = GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.ReservedInNestedScopes,
            Prefix = prefix,
            Suffix = suffix,
        });
        if (RequiresBlockScopedVar())
        {
            EmitContext().AddLexicalDeclaration(identifier);
        }
        else
        {
            EmitContext().AddVariableDeclaration(identifier);
        }
        return identifier;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) createHoistedVariableForPrivateName(name *ast.Node, suffix string) *ast.IdentifierNode</c>（classfields.go:3166-3179）。</summary>
    private Node CreateHoistedVariableForPrivateName(Node name, string suffix)
    {
        // If the name is a generated identifier (e.g., auto-accessor backing field),
        // use node-based name generation so the emitter can resolve the name properly.
        if (EmitContext().HasAutoGenerateInfo(name))
        {
            return CreateHoistedVariableForClassFromNode(name, suffix);
        }
        var text = name.Text();
        if (text.Length >= 1 && text[0] == '#')
        {
            text = text.Substring(1); // strip leading '#'
        }
        return CreateHoistedVariableForClass(text, name, suffix);
    }

    // accessPrivateIdentifier accesses an already defined PrivateIdentifier in the current
    // PrivateIdentifierEnvironment.
    /// <summary>Go: <c>func (tx *classFieldsTransformer) accessPrivateIdentifier(name *ast.Node) *privateIdentifierInfo</c>（classfields.go:3181-3193）。</summary>
    private PrivateIdentifierInfo? AccessPrivateIdentifier(Node name)
    {
        for (var env = _lexicalEnvironment; env != null; env = env._previous)
        {
            if (env._privateEnv != null)
            {
                if (TryGetPrivateIdentifier(env._privateEnv, name, out var info))
                {
                    if (IsPrivateKind(info!._kind, PrivateIdentifierKind.Untransformed))
                    {
                        return null;
                    }
                    return info;
                }
            }
        }
        return null;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) wrapPrivateIdentifierForDestructuringTarget(node *ast.Node) *ast.Node</c>（classfields.go:3195-3218）。</summary>
    private Node WrapPrivateIdentifierForDestructuringTarget(Node node)
    {
        var prop = node.AsPropertyAccessExpression()!;
        var parameter = Factory().NewGeneratedNameForNode(node);
        var info = AccessPrivateIdentifier(prop.Name()!);
        if (info == null)
        {
            return Visitor().VisitEachChild(node)!;
        }
        var receiver = prop.Expression!;
        // We cannot copy `this` or `super` into the function because they will be bound
        // differently inside the function.
        var isThisOrSuperProperty = prop.Expression!.Kind == Kind.ThisKeyword || prop.Expression.Kind == Kind.SuperKeyword;
        if (isThisOrSuperProperty || !IsSimpleCopiableExpression(prop.Expression!))
        {
            receiver = Factory().NewTempVariableEx(new AutoGenerateOptions
            {
                Flags = GeneratedIdentifierFlags.ReservedInNestedScopes,
            });
            EmitContext().AddVariableDeclaration(receiver);
            _pendingExpressions.Add(
                Factory().NewAssignmentExpression(receiver, Visitor().VisitNode(prop.Expression)!));
        }
        var assignExpr = CreatePrivateIdentifierAssignment(info, receiver, parameter, Kind.EqualsToken);
        return Factory().NewAssignmentTargetWrapper(parameter, assignExpr!);
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitAssignmentElement(node *ast.Node) *ast.Node</c>（classfields.go:3220-3245）。</summary>
    private Node VisitAssignmentElement(Node node)
    {
        // 13.15.5.5 RS: IteratorDestructuringAssignmentEvaluation
        //   AssignmentElement : DestructuringAssignmentTarget Initializer?
        //     ...
        //     4. If |Initializer| is present and _value_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) and IsIdentifierRef of |DestructuringAssignmentTarget| are both *true*, then
        //           i. Let _v_ be ? NamedEvaluation of |Initializer| with argument _lref_.[[ReferencedName]].
        //     ...

        if (IsNamedEvaluationAnd(EmitContext(), node, _isAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node, false /*ignoreEmptyStringLiteral*/, "" /*assignedName*/);
        }
        if (IsAssignmentExpression(node, true /*excludeCompoundAssignment*/))
        {
            var left = VisitDestructuringAssignmentTarget(node.AsBinaryExpression()!.Left!);
            var right = Visitor().VisitNode(node.AsBinaryExpression()!.Right);
            return Factory().UpdateBinaryExpression(
                node.AsBinaryExpression()!,
                null,
                left,
                null,
                node.AsBinaryExpression()!.OperatorToken,
                right);
        }
        return VisitDestructuringAssignmentTarget(node)!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitAssignmentRestElement(node *ast.Node) *ast.Node</c>（classfields.go:3247-3254）。</summary>
    private Node VisitAssignmentRestElement(Node node)
    {
        var spread = node.AsSpreadElement()!;
        if (IsLeftHandSideExpression(spread.Expression))
        {
            var expr = VisitDestructuringAssignmentTarget(spread.Expression!);
            return Factory().UpdateSpreadElement(spread, expr);
        }
        return Visitor().VisitEachChild(node)!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitArrayAssignmentElement(node *ast.Node) *ast.Node</c>（classfields.go:3256-3266）。</summary>
    private Node VisitArrayAssignmentElement(Node node)
    {
        if (IsArrayBindingOrAssignmentElement(node))
        {
            if (IsSpreadElement(node))
            {
                return VisitAssignmentRestElement(node);
            }
            if (node.Kind != Kind.OmittedExpression)
            {
                return VisitAssignmentElement(node);
            }
        }
        return Visitor().VisitEachChild(node)!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitAssignmentProperty(node *ast.Node) *ast.Node</c>（classfields.go:3268-3292）。</summary>
    private Node VisitAssignmentProperty(Node node)
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

        var prop = node.AsPropertyAssignment()!;
        var name = Visitor().VisitNode(prop.Name());
        var init = prop.Initializer;
        if (IsAssignmentExpression(init, true /*excludeCompoundAssignment*/))
        {
            var assignElem = VisitAssignmentElement(init!);
            return Factory().UpdatePropertyAssignment(prop, null, name, null, null, assignElem);
        }
        if (IsLeftHandSideExpression(init))
        {
            var target = VisitDestructuringAssignmentTarget(init!);
            return Factory().UpdatePropertyAssignment(prop, null, name, null, null, target);
        }
        return Visitor().VisitEachChild(node)!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitShorthandAssignmentProperty(node *ast.Node) *ast.Node</c>（classfields.go:3294-3309）。</summary>
    private Node VisitShorthandAssignmentProperty(Node node)
    {
        // AssignmentProperty : IdentifierReference Initializer?

        // 13.15.5.3 RS: PropertyDestructuringAssignmentEvaluation
        //   AssignmentProperty : IdentifierReference Initializer?
        //     ...
        //     4. If |Initializer?| is present and _v_ is *undefined*, then
        //        a. If IsAnonymousFunctionDefinition(|Initializer|) is *true*, then
        //           i. Set _v_ to ? NamedEvaluation of |Initializer| with argument _P_.
        //     ...

        if (IsNamedEvaluationAnd(EmitContext(), node, _isAnonymousClassNeedingAssignedName))
        {
            node = TransformNamedEvaluation(EmitContext(), node, false /*ignoreEmptyStringLiteral*/, "" /*assignedName*/);
        }
        return Visitor().VisitEachChild(node)!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitAssignmentRestProperty(node *ast.Node) *ast.Node</c>（classfields.go:3311-3318）。</summary>
    private Node VisitAssignmentRestProperty(Node node)
    {
        var spread = node.AsSpreadAssignment()!;
        if (IsLeftHandSideExpression(spread.Expression))
        {
            var expr = VisitDestructuringAssignmentTarget(spread.Expression!);
            return Factory().UpdateSpreadAssignment(spread, expr);
        }
        return Visitor().VisitEachChild(node)!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitObjectAssignmentElement(node *ast.Node) *ast.Node</c>（classfields.go:3320-3332）。</summary>
    private Node VisitObjectAssignmentElement(Node node)
    {
        Debug.Assert(node != null && IsObjectBindingOrAssignmentElement(node));
        if (IsSpreadAssignment(node!))
        {
            return VisitAssignmentRestProperty(node!);
        }
        if (IsShorthandPropertyAssignment(node!))
        {
            return VisitShorthandAssignmentProperty(node!);
        }
        if (IsPropertyAssignment(node!))
        {
            return VisitAssignmentProperty(node!);
        }
        return Visitor().VisitEachChild(node)!;
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) visitAssignmentPattern(node *ast.Node) *ast.Node</c>（classfields.go:3334-3363）。</summary>
    private Node VisitAssignmentPattern(Node node)
    {
        if (IsArrayLiteralExpression(node))
        {
            // Transforms private names in destructuring assignment array bindings.
            // Transforms SuperProperty assignments in destructuring assignment array bindings in static initializers.
            //
            // Source:
            // ([ this.#myProp ] = [ "hello" ]);
            //
            // Transformation:
            // [ { set value(x) { this.#myProp = x; } }.value ] = [ "hello" ];
            return Factory().UpdateArrayLiteralExpression(
                node.AsArrayLiteralExpression()!,
                _arrayAssignmentElementVisitor.VisitNodes(node.AsArrayLiteralExpression()!.Elements),
                node.AsArrayLiteralExpression()!.MultiLine);
        }
        // Transforms private names in destructuring assignment object bindings.
        // Transforms SuperProperty assignments in destructuring assignment object bindings in static initializers.
        //
        // Source:
        // ({ stringProperty: this.#myProp } = { stringProperty: "hello" });
        //
        // Transformation:
        // ({ stringProperty: { set value(x) { this.#myProp = x; } }.value }) = { stringProperty: "hello" };
        return Factory().UpdateObjectLiteralExpression(
            node.AsObjectLiteralExpression()!,
            _objectAssignmentElementVisitor.VisitNodes(node.AsObjectLiteralExpression()!.Properties),
            node.AsObjectLiteralExpression()!.MultiLine);
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) isReservedPrivateName(node *ast.Node) bool</c>（classfields.go:3391-3393）。</summary>
    private bool IsReservedPrivateName(Node node)
    {
        return !(IsPrivateIdentifier(node) && EmitContext().HasAutoGenerateInfo(node)) && node.Text() == "#constructor";
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) getProperties(node *ast.Node, requireInitializer bool, isStatic bool) []*ast.Node</c>（classfields.go:3400-3410）。</summary>
    private Node[] GetProperties(Node node, bool requireInitializer, bool isStatic)
    {
        var result = new List<Node>();
        foreach (var member in node.Members()!)
        {
            if (IsPropertyDeclaration(member) &&
                (!requireInitializer || member.Initializer() != null) &&
                HasStaticModifier(member) == isStatic)
            {
                result.Add(member);
            }
        }
        return result.ToArray();
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) getStaticPropertiesAndClassStaticBlock(node *ast.Node) []*ast.Node</c>（classfields.go:3412-3421）。</summary>
    private Node[] GetStaticPropertiesAndClassStaticBlock(Node node)
    {
        var result = new List<Node>();
        foreach (var member in node.Members()!)
        {
            if (IsClassStaticBlockDeclaration(member) || (IsPropertyDeclaration(member) && HasStaticModifier(member)))
            {
                result.Add(member);
            }
        }
        return result.ToArray();
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) createCallBinding(node *ast.Node) (thisArg *ast.Expression, target *ast.Expression)</c>（classfields.go:3457-3481）。</summary>
    private (Node ThisArg, Node Target) CreateCallBinding(Node node)
    {
        if (IsSuperProperty(node))
        {
            return (Factory().NewThisExpression(), node);
        }
        if (IsPropertyAccessExpression(node))
        {
            var expr = node.AsPropertyAccessExpression()!;
            if (ShouldBeCapturedInTempVariable(expr.Expression!))
            {
                var thisArg = Factory().NewTempVariable();
                EmitContext().AddVariableDeclaration(thisArg);
                var target = Factory().NewPropertyAccessExpression(
                    Factory().NewParenthesizedExpression( // TODO: do we even need these?
                        Factory().NewAssignmentExpression(thisArg, expr.Expression!)),
                    null,
                    expr.Name(),
                    NodeFlags.None);
                return (thisArg, target);
            }
            return (expr.Expression!, node);
        }
        return (Factory().NewVoidZeroExpression(), node);
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) createAccessorPropertyGetRedirector(node *ast.PropertyDeclaration, modifiers *ast.ModifierList, name *ast.PropertyName, receiver *ast.Expression) *ast.Node</c>（classfields.go:3493-3512）。</summary>
    private Node CreateAccessorPropertyGetRedirector(Node node, ModifierList? modifiers, Node name, Node receiver)
    {
        var backingFieldName = Factory().NewGeneratedPrivateNameForNodeEx(node.Name()!, new AutoGenerateOptions { Suffix = "_accessor_storage" });
        var returnExpr = Factory().NewPropertyAccessExpression(
            receiver,
            null,
            backingFieldName,
            NodeFlags.None);
        var returnStmt = Factory().NewReturnStatement(returnExpr);
        var body = Factory().NewBlock(Factory().NewNodeList(new Node[] { returnStmt }), false);
        return Factory().NewGetAccessorDeclaration(
            modifiers,
            name,
            null, /*typeParameters*/
            Factory().NewNodeList(new Node[] { }),
            null, /*returnType*/
            null, /*fullSignature*/
            body);
    }

    /// <summary>Go: <c>func (tx *classFieldsTransformer) createAccessorPropertySetRedirector(node *ast.PropertyDeclaration, modifiers *ast.ModifierList, name *ast.PropertyName, receiver *ast.Expression) *ast.Node</c>（classfields.go:3514-3545）。</summary>
    private Node CreateAccessorPropertySetRedirector(Node node, ModifierList? modifiers, Node name, Node receiver)
    {
        var backingFieldName = Factory().NewGeneratedPrivateNameForNodeEx(node.Name()!, new AutoGenerateOptions { Suffix = "_accessor_storage" });
        var valueParam = Factory().NewParameterDeclaration(
            null, /*modifiers*/
            null, /*dotDotDotToken*/
            Factory().NewIdentifier("value"),
            null, /*questionToken*/
            null, /*typeNode*/
            null); /*initializer*/
        var assignExpr = Factory().NewAssignmentExpression(
            Factory().NewPropertyAccessExpression(
                receiver,
                null,
                backingFieldName,
                NodeFlags.None),
            Factory().NewIdentifier("value"));
        var exprStmt = Factory().NewExpressionStatement(assignExpr);
        var body = Factory().NewBlock(Factory().NewNodeList(new Node[] { exprStmt }), false);
        return Factory().NewSetAccessorDeclaration(
            modifiers,
            name,
            null, /*typeParameters*/
            Factory().NewNodeList(new Node[] { valueParam }),
            null, /*returnType*/
            null, /*fullSignature*/
            body);
    }
}

/// <summary>
/// Go: <c>internal/transformers/estransforms</c> 的包级函数（本文件承载 classfields.go:3365-3389、3395-3398、
/// 3423-3430、3432-3436、3438-3455、3483-3491、3547-3562、3564-3578、3580-3618 的无接收者 <c>func</c>）。
/// 它们被独立类 <c>ClassFieldsTransformer</c>（及 estransforms 其它分片）调用，所以是 <c>internal static</c>。
/// </summary>
public static partial class EsTransforms
{
    /// <summary>Go: <c>func createPrivateStaticFieldInitializer(factory *printer.NodeFactory, variableName *ast.IdentifierNode, initializer *ast.Expression) *ast.Expression</c>（classfields.go:3365-3378）。</summary>
    internal static Node CreatePrivateStaticFieldInitializer(NodeFactory factory, Node? variableName, Node? initializer)
    {
        if (initializer == null)
        {
            initializer = factory.NewVoidZeroExpression();
        }
        return factory.NewAssignmentExpression(
            variableName!,
            factory.NewObjectLiteralExpression(
                factory.NewNodeList(new Node[]
                {
                    factory.NewPropertyAssignment(null, factory.NewIdentifier("value"), null, null, initializer),
                }),
                false));
    }

    /// <summary>Go: <c>func createPrivateInstanceFieldInitializer(factory *printer.NodeFactory, receiver *ast.Expression, initializer *ast.Expression, weakMapName *ast.IdentifierNode) *ast.Expression</c>（classfields.go:3380-3385）。</summary>
    internal static Node CreatePrivateInstanceFieldInitializer(NodeFactory factory, Node receiver, Node? initializer, Node weakMapName)
    {
        if (initializer == null)
        {
            initializer = factory.NewVoidZeroExpression();
        }
        return factory.NewMethodCall(weakMapName, factory.NewIdentifier("set"), new Node[] { receiver, initializer });
    }

    /// <summary>Go: <c>func createPrivateInstanceMethodInitializer(factory *printer.NodeFactory, receiver *ast.Expression, weakSetName *ast.IdentifierNode) *ast.Expression</c>（classfields.go:3387-3389）。</summary>
    internal static Node CreatePrivateInstanceMethodInitializer(NodeFactory factory, Node receiver, Node weakSetName)
    {
        return factory.NewMethodCall(weakSetName, factory.NewIdentifier("add"), new Node[] { receiver });
    }

    /// <summary>Go: <c>func isStaticPropertyDeclarationOrClassStaticBlock(node *ast.Node) bool</c>（classfields.go:3395-3398）。</summary>
    internal static bool IsStaticPropertyDeclarationOrClassStaticBlock(Node node)
    {
        return IsClassStaticBlockDeclaration(node) ||
            (IsPropertyDeclaration(node) && HasStaticModifier(node));
    }

    /// <summary>Go: <c>func classHasClassThisAssignment(emitContext *printer.EmitContext, node *ast.Node) bool</c>（classfields.go:3423-3430）。</summary>
    internal static bool ClassHasClassThisAssignment(EmitContext emitContext, Node node)
    {
        foreach (var member in node.Members()!)
        {
            if (IsClassThisAssignmentBlock(emitContext, member))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Go: <c>func isNonStaticMethodOrAccessorWithPrivateName(member *ast.Node) bool</c>（classfields.go:3432-3436）。</summary>
    internal static bool IsNonStaticMethodOrAccessorWithPrivateName(Node member)
    {
        return !IsStatic(member) &&
            (IsMethodOrAccessor(member) || IsAutoAccessorPropertyDeclaration(member)) &&
            IsPrivateIdentifier(member.Name()!);
    }

    /// <summary>Go: <c>func createMemberAccessForPropertyName(factory *printer.NodeFactory, emitContext *printer.EmitContext, receiver *ast.Expression, name *ast.PropertyName, location *ast.PropertyName) *ast.Expression</c>（classfields.go:3438-3455）。</summary>
    internal static Node CreateMemberAccessForPropertyName(NodeFactory factory, EmitContext emitContext, Node receiver, Node? name, Node location)
    {
        Node expression;
        if (IsComputedPropertyName(name!))
        {
            expression = factory.NewElementAccessExpression(receiver, null, name!.Expression(), NodeFlags.None);
            expression.Loc = location.Loc;
            return expression;
        }
        if (IsIdentifier(name!) || IsPrivateIdentifier(name!))
        {
            expression = factory.NewPropertyAccessExpression(receiver, null, name, NodeFlags.None);
        }
        else
        {
            // string or numeric literal
            expression = factory.NewElementAccessExpression(receiver, null, name, NodeFlags.None);
        }
        emitContext.SetCommentRange(expression, name!.Loc);
        emitContext.SetSourceMapRange(expression, name.Loc);
        emitContext.AddEmitFlags(expression, EmitFlags.NoNestedSourceMaps);
        return expression;
    }

    /// <summary>Go: <c>func shouldBeCapturedInTempVariable(node *ast.Node) bool</c>（classfields.go:3483-3491）。</summary>
    internal static bool ShouldBeCapturedInTempVariable(Node node)
    {
        var target = SkipParentheses(node)!;
        switch (target.Kind)
        {
            case Kind.Identifier:
            case Kind.ThisKeyword:
            case Kind.NumericLiteral:
            case Kind.BigIntLiteral:
            case Kind.StringLiteral:
                return false;
            default:
                return true;
        }
    }

    // flattenCommaList decomposes a comma expression tree into a sequence of expressions.
    /// <summary>
    /// Go: <c>func flattenCommaList(node *ast.Expression) iter.Seq[*ast.Expression]</c>（classfields.go:3547-3551）。
    /// Go 的 `iter.Seq` 是 push 模型（`yield` 返回 false 即短路）；C# 迭代器是 pull 模型，消费方 `break` 时
    /// 迭代器停止产出（等价于短路）。为保持与 flattenCommaListWorker 的 1:1 对照，这里按同样的
    /// "合成括号表达式 → 逗号表达式 → 叶子" 三分支递归展开。
    /// </summary>
    internal static IEnumerable<Node> FlattenCommaList(Node node)
    {
        if (IsParenthesizedExpression(node) && NodeIsSynthesized(node))
        {
            foreach (var expression in FlattenCommaList(node.Expression()!))
            {
                yield return expression;
            }
        }
        else if (IsCommaExpression(node))
        {
            foreach (var expression in FlattenCommaList(node.AsBinaryExpression()!.Left!))
            {
                yield return expression;
            }
            foreach (var expression in FlattenCommaList(node.AsBinaryExpression()!.Right!))
            {
                yield return expression;
            }
        }
        else
        {
            yield return node;
        }
    }

    /// <summary>
    /// Go: <c>func flattenCommaListWorker(node *ast.Expression, yield func(*ast.Expression) bool) bool</c>（classfields.go:3553-3562）。
    /// 保留 Go 的 push 形态供 1:1 对照：`yield` 返回 false 表示调用方要求停止，返回值同样向上短路。
    /// </summary>
    internal static bool FlattenCommaListWorker(Node node, Func<Node, bool> yield)
    {
        if (IsParenthesizedExpression(node) && NodeIsSynthesized(node))
        {
            return FlattenCommaListWorker(node.Expression()!, yield);
        }
        else if (IsCommaExpression(node))
        {
            return FlattenCommaListWorker(node.AsBinaryExpression()!.Left!, yield) &&
                FlattenCommaListWorker(node.AsBinaryExpression()!.Right!, yield);
        }
        else
        {
            return yield(node);
        }
    }

    /// <summary>Go: <c>func findComputedPropertyNameCacheAssignment(emitContext *printer.EmitContext, name *ast.Node) *ast.BinaryExpression</c>（classfields.go:3564-3578）。</summary>
    internal static BinaryExpression? FindComputedPropertyNameCacheAssignment(EmitContext emitContext, Node name)
    {
        var node = name.Expression();
        while (true)
        {
            node = SkipOuterExpressions(node, 0);
            if (IsBinaryExpression(node!) && node!.AsBinaryExpression()!.OperatorToken!.Kind == Kind.CommaToken)
            {
                node = node.AsBinaryExpression()!.Right;
                continue;
            }
            if (IsAssignmentExpression(node, true /*excludeCompoundAssignment*/) && IsIdentifier(node!.AsBinaryExpression()!.Left!))
            {
                return node.AsBinaryExpression();
            }
            break;
        }
        return null;
    }

    /// <summary>Go: <c>func expandPreOrPostfixIncrementOrDecrementExpression(factory *printer.NodeFactory, emitContext *printer.EmitContext, node *ast.Node, expression *ast.Expression, resultVariable *ast.IdentifierNode) *ast.Expression</c>（classfields.go:3580-3618）。</summary>
    internal static Node ExpandPreOrPostfixIncrementOrDecrementExpression(NodeFactory factory, EmitContext emitContext, Node node, Node expression, Node? resultVariable)
    {
        Kind @operator;
        Node operand;
        if (IsPrefixUnaryExpression(node))
        {
            @operator = node.AsPrefixUnaryExpression()!.Operator;
            operand = node.AsPrefixUnaryExpression()!.Operand!;
        }
        else
        {
            @operator = node.AsPostfixUnaryExpression()!.Operator;
            operand = node.AsPostfixUnaryExpression()!.Operand!;
        }

        var temp = factory.NewTempVariable();
        emitContext.AddVariableDeclaration(temp);
        expression = factory.NewAssignmentExpression(temp, expression);
        expression.Loc = operand.Loc;

        Node operation;
        if (IsPrefixUnaryExpression(node))
        {
            operation = factory.NewPrefixUnaryExpression(@operator, temp);
        }
        else
        {
            operation = factory.NewPostfixUnaryExpression(temp, @operator);
        }
        operation.Loc = node.Loc;

        if (resultVariable != null)
        {
            operation = factory.NewAssignmentExpression(resultVariable, operation);
            operation.Loc = node.Loc;
        }

        expression = factory.NewCommaExpression(expression, operation);
        expression.Loc = node.Loc;

        if (IsPostfixUnaryExpression(node))
        {
            expression = factory.NewCommaExpression(expression, temp);
            expression.Loc = node.Loc;
        }

        return expression;
    }
}
