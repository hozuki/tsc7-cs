// ported from: internal/checker/checker.go:12221-12486
//   （getContextualThisParameterType / checkThisExpression / tryGetThisTypeAt / TryGetThisTypeAtEx /
//    tryGetThisTypeAtEx / getThisContainer / isInParameterInitializerBeforeContainingFunction /
//    checkThisInStaticClassFieldInitializerInDecoratedClass / checkThisBeforeSuper / classDeclarationExtendsNull）
//
// 本文件是 `Checker` 的 partial 分片（docs/checker-plan.md §5.2）：Go 的 `func (c *Checker) x()` → 本类的实例方法；
// Go 的包级函数 → 同一个类的 static 成员（本文件里没有包级函数）。
//
// Go→C# 改写（本文件，逐条理由）：
// 1) `tryGetThisTypeAtEx`（未导出）与 `TryGetThisTypeAtEx`（导出）PascalCase 后同名 → 未导出者按 C# 习惯改名为
//    `TryGetThisTypeAtExWorker`（docs/checker-plan.md §5.2 的 `Worker` 后缀规则；Go 的两个同名不同大小写的
//    方法在 C# 里无法并存）。
// 2) 实例方法 `getThisContainer` → `GetThisContainer`，与 `Ast.GetThisContainer` 的 C# 名相同。类内裸名
//    `GetThisContainer(a, b, c)` 会绑定到**本类实例方法**（类型成员优先于 `using static` 导入的成员），所以
//    Go 里 3 处 `ast.GetThisContainer(...)` 必须写全名 `TypeScript.Ast.Ast.GetThisContainer(...)`
//    （docs/architecture.md §2 的静态类/命名空间解析规则；Binder.cs 对 `IsSignedNumericLiteral` 同款处理）。
// 3) `panic("No parent in getThisContainer")` → `Debug.Fail(...)` + `throw new UnreachableException()`
//    （`Debug.Fail` 没有 `[DoesNotReturn]` 标注，其后必须补 throw 才满足控制流/返回值要求）。
// 4) Go 的 `node = node.Parent; if node == nil { panic }` → 先取局部 `parent` 再判空后赋值（避免 CS8600）。
// 5) `case ast.KindArrowFunction: … fallthrough` 落到 `case …: return node` 的标签组 → C# 里直接 `return node;`
//    （fallthrough 的目标 case 体就是 `return node`，语义逐字相同）。
// 6) `c.error(...)` 是 `Checker` 的实例方法（Go checker.go:14234，属 C4 的 Checker.C4.Diagnostics.cs）→ 裸名
//    `Error(...)`，注意 Go 的 `error` **有返回值**（`*ast.Diagnostic`）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) getContextualThisParameterType(fn *ast.Node) *Type</c>。</summary>
    internal Type? GetContextualThisParameterType(Node fn)
    {
        if (IsArrowFunction(fn))
        {
            return null;
        }

        if (IsContextSensitiveFunctionOrObjectLiteralMethod(fn))
        {
            var contextualSignature = GetContextualSignature(fn);
            if (contextualSignature is not null)
            {
                var thisParameter = contextualSignature._thisParameter;
                if (thisParameter is not null)
                {
                    return GetTypeOfSymbolWorker(thisParameter);
                }
            }
        }

        bool inJs = IsInJSFile(fn);
        if (_noImplicitThis || inJs)
        {
            var containingLiteral = GetContainingObjectLiteral(fn);
            if (containingLiteral is not null)
            {
                // We have an object literal method. Check if the containing object literal has a contextual type
                // that includes a ThisType<T>. If so, T is the contextual type for 'this'. We continue looking in
                // any directly enclosing object literals.
                var contextualType = GetApparentTypeOfContextualType(containingLiteral, ContextFlags.None);
                var thisType = GetThisTypeOfObjectLiteralFromContextualType(containingLiteral, contextualType);
                if (thisType is not null)
                {
                    return InstantiateType(thisType, GetMapperFromContext(GetInferenceContext(containingLiteral)));
                }

                // There was no contextual ThisType<T> for the containing object literal, so the contextual type
                // for 'this' is the non-null form of the contextual type for the containing object literal or
                // the type of the object literal itself.
                if (contextualType is not null)
                {
                    thisType = GetNonNullableType(contextualType);
                }
                else
                {
                    thisType = CheckExpressionCached(containingLiteral);
                }

                return GetWidenedTypeWorker(thisType);
            }

            // In an assignment of the form 'obj.xxx = function(...)' or 'obj[xxx] = function(...)', the
            // contextual type for 'this' is 'obj'.
            var parent = WalkUpParenthesizedExpressions(fn.Parent);
            if (IsAssignmentExpression(parent, false))
            {
                var target = parent!.AsBinaryExpression()!.Left;
                if (IsAccessExpression(target))
                {
                    var expression = target!.Expression()!;

                    // Don't contextually type `this` as `exports` in `exports.Point = function(x, y) { this.x = x; this.y = y; }`
                    if (inJs && IsIdentifier(expression))
                    {
                        var sourceFile = GetSourceFileOfNode(parent);
                        if (sourceFile!.CommonJSModuleIndicator is not null
                            && (GetResolvedSymbol(expression)!.Flags & SymbolFlags.ModuleExports) != 0)
                        {
                            return null;
                        }
                    }

                    return GetWidenedTypeWorker(CheckExpressionCached(expression));
                }
            }
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) checkThisExpression(node *ast.Node) *Type</c>。</summary>
    internal Type CheckThisExpression(Node node)
    {
        // Stop at the first arrow function so that we can
        // tell whether 'this' needs to be captured.
        var container = TypeScript.Ast.Ast.GetThisContainer(node, true /*includeArrowFunctions*/, true /*includeClassComputedPropertyName*/);
        bool capturedByArrowFunction = false;
        bool thisInComputedPropertyName = false;
        if (IsConstructorDeclaration(container))
        {
            CheckThisBeforeSuper(node, container, X_super_must_be_called_before_accessing_this_in_the_constructor_of_a_derived_class);
        }

        while (true)
        {
            // Now skip arrow functions to get the "real" owner of 'this'.
            if (IsArrowFunction(container))
            {
                container = TypeScript.Ast.Ast.GetThisContainer(container, false /*includeArrowFunctions*/, !thisInComputedPropertyName);
                capturedByArrowFunction = true;
            }

            if (IsComputedPropertyName(container))
            {
                container = TypeScript.Ast.Ast.GetThisContainer(container, !capturedByArrowFunction, false /*includeClassComputedPropertyName*/);
                thisInComputedPropertyName = true;
                continue;
            }

            break;
        }

        CheckThisInStaticClassFieldInitializerInDecoratedClass(node, container);
        if (thisInComputedPropertyName)
        {
            Error(node, X_this_cannot_be_referenced_in_a_computed_property_name);
        }
        else
        {
            switch (container.Kind)
            {
                case Kind.ModuleDeclaration:
                    Error(node, X_this_cannot_be_referenced_in_a_module_or_namespace_body);
                    // do not return here so in case if lexical this is captured - it will be reflected in flags on NodeLinks
                    break;
                case Kind.EnumDeclaration:
                    Error(node, X_this_cannot_be_referenced_in_current_location);
                    // do not return here so in case if lexical this is captured - it will be reflected in flags on NodeLinks
                    break;
            }
        }

        var t = TryGetThisTypeAtExWorker(node, true /*includeGlobalThis*/, container);
        if (_noImplicitThis)
        {
            var globalThisType = GetTypeOfSymbolWorker(_globalThisSymbol!);
            if (t == globalThisType && capturedByArrowFunction)
            {
                Error(node, The_containing_arrow_function_captures_the_global_value_of_this);
            }
            else if (t is null)
            {
                // With noImplicitThis, functions may not reference 'this' if it has type 'any'
                var diag = Error(node, X_this_implicitly_has_type_any_because_it_does_not_have_a_type_annotation);
                if (!IsSourceFile(container))
                {
                    var outsideThis = TryGetThisTypeAt(container);
                    if (outsideThis is not null && outsideThis != globalThisType)
                    {
                        diag.AddRelatedInfo(CreateDiagnosticForNode(container, An_outer_value_of_this_is_shadowed_by_this_container));
                    }
                }
            }
        }

        if (t is null)
        {
            return _anyType!;
        }

        return t;
    }

    /// <summary>Go: <c>func (c *Checker) tryGetThisTypeAt(node *ast.Node) *Type</c>。</summary>
    internal Type? TryGetThisTypeAt(Node node)
    {
        return TryGetThisTypeAtExWorker(node, true /*includeGlobalThis*/, null /*container*/);
    }

    /// <summary>Go: <c>func (c *Checker) TryGetThisTypeAtEx(node *ast.Node, includeGlobalThis bool, container *ast.Node) *Type</c>。</summary>
    public Type? TryGetThisTypeAtEx(Node node, bool includeGlobalThis, Node? container)
    {
        var reparsed = GetReparsedNodeForNode(node);
        if ((reparsed!.Flags & NodeFlags.JSDoc) != 0 && (reparsed.Flags & NodeFlags.Reparsed) == 0)
        {
            return null; // Binder doesn't process non-reparsed JSDoc nodes
        }

        return TryGetThisTypeAtExWorker(reparsed, includeGlobalThis, GetReparsedNodeForNode(container));
    }

    /// <summary>
    /// Go: <c>func (c *Checker) tryGetThisTypeAtEx(node *ast.Node, includeGlobalThis bool, container *ast.Node) *Type</c>。
    /// C# 里与导出的 <c>TryGetThisTypeAtEx</c> 同名，故加 <c>Worker</c> 后缀（见文件头 1）。
    /// </summary>
    internal Type? TryGetThisTypeAtExWorker(Node node, bool includeGlobalThis, Node? container)
    {
        if (container is null)
        {
            container = GetThisContainer(node, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
        }

        if (IsFunctionLike(container)
            && (!IsInParameterInitializerBeforeContainingFunction(node) || GetThisParameter(container) is not null))
        {
            var sig = GetSignatureOfFullSignatureType(container);
            sig ??= GetSignatureFromDeclaration(container);
            var thisType = GetThisTypeOfSignature(sig!);

            // Note: a parameter initializer should refer to class-this unless function-this is explicitly annotated.
            // If this is a function in a JS file, it might be a class method.
            if (thisType is null)
            {
                thisType = GetContextualThisParameterType(container);
            }

            if (thisType is not null)
            {
                return GetFlowTypeOfReference(node, thisType);
            }
        }

        var parent = container.Parent;
        if (parent is not null && IsClassLike(parent))
        {
            var symbol = GetSymbolOfDeclaration(parent);
            Type t;
            if (IsStatic(container))
            {
                t = GetTypeOfSymbolWorker(symbol!);
            }
            else
            {
                t = GetDeclaredTypeOfSymbolWorker(symbol!).AsInterfaceType()!._thisType!;
            }

            return GetFlowTypeOfReference(node, t);
        }

        if (IsSourceFile(container))
        {
            // look up in the source file's locals or exports
            if (container.AsSourceFile()!.ExternalModuleIndicator is not null)
            {
                // TODO: Maybe issue a better error than 'object is possibly undefined'
                return _undefinedType;
            }

            if (includeGlobalThis)
            {
                return GetTypeOfSymbolWorker(_globalThisSymbol!);
            }
        }

        return null;
    }

    /// <summary>Go: <c>func (c *Checker) getThisContainer(node *ast.Node, includeArrowFunctions bool, includeClassComputedPropertyName bool) *ast.Node</c>。</summary>
    internal Node GetThisContainer(Node node, bool includeArrowFunctions, bool includeClassComputedPropertyName)
    {
        while (true)
        {
            var parent = node.Parent;
            if (parent is null)
            {
                // If we never pass in a SourceFile, this should be unreachable, since we'll stop when we reach that.
                Debug.Fail("No parent in getThisContainer");
                throw new System.Diagnostics.UnreachableException();
            }

            node = parent;
            switch (node.Kind)
            {
                case Kind.ComputedPropertyName:
                    // If the grandparent node is an object literal (as opposed to a class),
                    // then the computed property is not a 'this' container.
                    // A computed property name in a class needs to be a this container
                    // so that we can error on it.
                    if (includeClassComputedPropertyName && IsClassLike(node.Parent!.Parent!))
                    {
                        return node;
                    }

                    // If this is a computed property, then the parent should not
                    // make it a this container. The parent might be a property
                    // in an object literal, like a method or accessor. But in order for
                    // such a parent to be a this container, the reference must be in
                    // the *body* of the container.
                    node = node.Parent!.Parent!;
                    break;
                case Kind.Decorator:
                    // Decorators are always applied outside of the body of a class or method.
                    if (node.Parent!.Kind == Kind.Parameter && IsClassElement(node.Parent.Parent!))
                    {
                        // If the decorator's parent is a Parameter, we resolve the this container from
                        // the grandparent class declaration.
                        node = node.Parent.Parent!;
                    }
                    else if (IsClassElement(node.Parent))
                    {
                        // If the decorator's parent is a class element, we resolve the 'this' container
                        // from the parent class declaration.
                        node = node.Parent;
                    }

                    break;
                case Kind.ArrowFunction:
                    if (!includeArrowFunctions)
                    {
                        continue;
                    }

                    // Go: fallthrough → 下一个 case 体就是 `return node`
                    return node;
                case Kind.FunctionDeclaration:
                case Kind.FunctionExpression:
                case Kind.ModuleDeclaration:
                case Kind.ClassStaticBlockDeclaration:
                case Kind.PropertyDeclaration:
                case Kind.PropertySignature:
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                case Kind.Constructor:
                case Kind.GetAccessor:
                case Kind.SetAccessor:
                case Kind.CallSignature:
                case Kind.ConstructSignature:
                case Kind.IndexSignature:
                case Kind.EnumDeclaration:
                case Kind.SourceFile:
                    return node;
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) isInParameterInitializerBeforeContainingFunction(node *ast.Node) bool</c>。</summary>
    internal bool IsInParameterInitializerBeforeContainingFunction(Node node)
    {
        var inBindingInitializer = false;
        while (node.Parent is not null && !IsFunctionLike(node.Parent))
        {
            if (IsParameterDeclaration(node.Parent))
            {
                if (inBindingInitializer || node.Parent.Initializer() == node)
                {
                    return true;
                }
            }

            if (IsBindingElement(node.Parent) && node.Parent.Initializer() == node)
            {
                inBindingInitializer = true;
            }

            node = node.Parent!;
        }

        return false;
    }

    /// <summary>Go: <c>func (c *Checker) checkThisInStaticClassFieldInitializerInDecoratedClass(thisExpression *ast.Node, container *ast.Node)</c>。</summary>
    internal void CheckThisInStaticClassFieldInitializerInDecoratedClass(Node thisExpression, Node container)
    {
        if (IsPropertyDeclaration(container) && HasStaticModifier(container) && _legacyDecorators)
        {
            var initializer = container.Initializer();
            if (initializer is not null
                && initializer.Loc.ContainsInclusive(thisExpression.Pos())
                && HasDecorators(container.Parent!))
            {
                Error(thisExpression, Cannot_use_this_in_a_static_property_initializer_of_a_decorated_class);
            }
        }
    }

    /// <summary>Go: <c>func (c *Checker) checkThisBeforeSuper(node *ast.Node, container *ast.Node, diagnosticMessage *diagnostics.Message)</c>。</summary>
    internal void CheckThisBeforeSuper(Node node, Node container, Message diagnosticMessage)
    {
        var containingClassDecl = container.Parent!;
        var baseTypeNode = GetClassExtendsHeritageElement(containingClassDecl);

        // If a containing class does not have extends clause or the class extends null
        // skip checking whether super statement is called before "this" accessing.
        if (baseTypeNode is not null && !ClassDeclarationExtendsNull(containingClassDecl))
        {
            if (node.FlowNodeData() is not null && !IsPostSuperFlowNode(node.FlowNodeData()!.FlowNode!, false /*noCacheCheck*/))
            {
                Error(node, diagnosticMessage);
            }
        }
    }

    /// <summary>
    /// Check if the given class-declaration extends null then return true.
    /// Otherwise, return false
    /// </summary>
    /// <param name="classDecl">a class declaration to check if it extends null</param>
    internal bool ClassDeclarationExtendsNull(Node classDecl)
    {
        var classSymbol = GetSymbolOfDeclaration(classDecl);
        var classInstanceType = GetDeclaredTypeOfSymbolWorker(classSymbol!);
        var baseConstructorType = GetBaseConstructorTypeOfClassWorker(classInstanceType);
        return baseConstructorType == _nullWideningType;
    }
}
