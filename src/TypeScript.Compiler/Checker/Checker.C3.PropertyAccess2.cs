// ported from: internal/checker/checker.go:11728-12186（不存在属性/可访问性/成员继承检查：reportNonexistentProperty、
//   getSuggestedLibForNonExistentProperty、getSuggestedSymbolForNonexistentProperty、
//   isValidPropertyAccessForCompletions（→ IsValidPropertyAccessForCompletionsWorker）、isPropertyAccessible
//   （→ IsPropertyAccessibleWorker）、containerSeemsToBeEmptyDomElement、hasCommonDomTypeName（→ internal static）、
//   checkAndReportErrorForExtendingInterface、getEntityNameForExtendingInterface、isUncalledFunctionReference、
//   checkPropertyNotUsedBeforeDeclaration、isOptionalPropertyDeclaration、isPropertyDeclaredInAncestorClass、
//   checkPropertyAccessibility、checkPropertyAccessibilityEx、checkPropertyAccessibilityAtLocation、
//   symbolHasNonMethodDeclaration、forEachProperty、getDeclaringClass、isValidOverrideOf、isPropertyInClassDerivedFrom、
//   isNodeUsedDuringClassInitialization、isNodeWithinClass、forEachEnclosingClass、isClassDerivedFromDeclaringClasses）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Worker 改名（artifacts/checker-method-collisions.md）：`isPropertyAccessible` → `IsPropertyAccessibleWorker`（本文**定义**）、
//    `isValidPropertyAccessForCompletions` → `IsValidPropertyAccessForCompletionsWorker`（本文**定义**）、
//    `getPropertiesOfType` → `GetPropertiesOfTypeWorker`、`getPropertyOfType` → `GetPropertyOfTypeWorker`、
//    `getApparentType` → `GetApparentTypeWorker`、`getTypeOfSymbol` → `GetTypeOfSymbolWorker`、
//    `getSignaturesOfType` → `GetSignaturesOfTypeWorker`、`getDeclaredTypeOfSymbol` → `GetDeclaredTypeOfSymbolWorker`、
//    `getBaseTypes` → `GetBaseTypesWorker`、`getConstraintOfTypeParameter` → `GetConstraintOfTypeParameterWorker`、
//    `getBaseConstraintOfType` → `GetBaseConstraintOfTypeWorker`、`symbolToString` → `SymbolToStringWorker`。
// 2) 包级函数 `hasCommonDomTypeName` → `internal static HasCommonDomTypeName`（§5.5）。
// 3) `strings.HasPrefix/HasSuffix`（区分大小写）→ `HasPrefix(s, p, true)`/`HasSuffix(s, p, true)`
//    （StringUtil 的签名带 `caseSensitive` 参数，docs/architecture.md §3.4）。
// 4) `slices.Contains(x, y)` → `x.Contains(y)`；`c.compilerOptions.Lib` 是 `string[]`，用 LINQ 的 `Contains`。
// 5) `entry.props`/`entry.lib` → `entry._props`/`entry._lib`（§5.5）；`getFeatureMap()` → `GetFeatureMap()`
//    （utilities.go 的 `sync.OnceValue` 包级变量，ChkLinksUtils 提供）。
// 6) Go 里遮蔽的局部名 `class`（同一函数内两处 `class := …`）→ C# 里改名为 `cls`/`enclosingClassType` 等，
//    避免 C# 的重复声明错误；其余名字保留。
// 7) Go 的 `switch node.Kind` 无 default 且后续语句继续执行 → C# 逐一写成 if/else-if（`errorNode` 必须在
//    所有分支之前声明）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;
using static TypeScript.StringUtil.StringUtil;

public sealed partial class Checker
{
    internal void ReportNonexistentProperty(Node propNode, Type containingType, bool isUncheckedJS)
    {
        var key = new NonExistentPropertyKey(propNode, containingType, isUncheckedJS);
        if (_nonExistentProperties.Has(key))
        {
            return;
        }
        _nonExistentProperties.Add(key);
        var links = _nodeLinks.Get(propNode);
        if ((links._flags & NodeCheckFlags.TypeChecked) != 0)
        {
            return; // error already made/in progress
        }
        links._flags |= NodeCheckFlags.TypeChecked;
        if (IsJSDocNameReferenceContext(propNode))
        {
            return;
        }
        Diagnostic? diagnostic = null;
        if (!IsPrivateIdentifier(propNode) && (containingType._flags & TypeFlags.Union) != 0 && (containingType._flags & TypeFlags.Primitive) == 0)
        {
            foreach (var subtype in containingType.Types())
            {
                if (GetPropertyOfTypeWorker(subtype, propNode.Text()) == null && GetApplicableIndexInfoForName(subtype, propNode.Text()) == null)
                {
                    diagnostic = NewDiagnosticChainForNode(diagnostic, propNode, Diagnostics.Property_0_does_not_exist_on_type_1, DeclarationNameToString(propNode), TypeToString(subtype));
                    break;
                }
            }
        }
        if (TypeHasStaticProperty(propNode.Text(), containingType))
        {
            var propName = DeclarationNameToString(propNode);
            var typeName = TypeToString(containingType);
            diagnostic = NewDiagnosticChainForNode(diagnostic, propNode, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_to_access_the_static_member_2_instead, propName, typeName, typeName + "." + propName);
        }
        else
        {
            var promisedType = GetPromisedTypeOfPromise(containingType);
            if (promisedType != null && GetPropertyOfTypeWorker(promisedType, propNode.Text()) != null)
            {
                diagnostic = NewDiagnosticChainForNode(diagnostic, propNode, Diagnostics.Property_0_does_not_exist_on_type_1, DeclarationNameToString(propNode), TypeToString(containingType));
                diagnostic.AddRelatedInfo(NewDiagnosticForNode(propNode, Diagnostics.Did_you_forget_to_use_await));
            }
            else
            {
                var missingProperty = DeclarationNameToString(propNode);
                var container = TypeToString(containingType);
                var libSuggestion = GetSuggestedLibForNonExistentProperty(missingProperty, containingType);
                if (libSuggestion != "")
                {
                    diagnostic = NewDiagnosticChainForNode(diagnostic, propNode, Diagnostics.Property_0_does_not_exist_on_type_1_Do_you_need_to_change_your_target_library_Try_changing_the_lib_compiler_option_to_2_or_later, missingProperty, container, libSuggestion);
                }
                else
                {
                    var suggestion = GetSuggestedSymbolForNonexistentProperty(propNode, containingType);
                    if (suggestion != null)
                    {
                        var suggestedName = SymbolName(suggestion);
                        var message = IfElse(isUncheckedJS, Diagnostics.Property_0_may_not_exist_on_type_1_Did_you_mean_2, Diagnostics.Property_0_does_not_exist_on_type_1_Did_you_mean_2);
                        diagnostic = NewDiagnosticChainForNode(diagnostic, propNode, message, missingProperty, container, suggestedName);
                        if (suggestion.ValueDeclaration != null)
                        {
                            diagnostic.AddRelatedInfo(NewDiagnosticForNode(suggestion.ValueDeclaration, Diagnostics.X_0_is_declared_here, suggestedName));
                        }
                    }
                    else
                    {
                        diagnostic = ElaborateNeverIntersection(diagnostic, propNode, containingType);
                        Message message2;
                        if (ContainerSeemsToBeEmptyDomElement(containingType))
                        {
                            message2 = Diagnostics.Property_0_does_not_exist_on_type_1_Try_changing_the_lib_compiler_option_to_include_dom;
                        }
                        else
                        {
                            message2 = Diagnostics.Property_0_does_not_exist_on_type_1;
                        }
                        diagnostic = NewDiagnosticChainForNode(diagnostic, propNode, message2, missingProperty, container);
                    }
                }
            }
        }
        AddErrorOrSuggestion(!isUncheckedJS || diagnostic!.Code() != Diagnostics.Property_0_may_not_exist_on_type_1_Did_you_mean_2.Code(), diagnostic);
    }

    internal string GetSuggestedLibForNonExistentProperty(string missingProperty, Type containingType)
    {
        var container = GetApparentTypeWorker(containingType)._symbol;
        if (container != null)
        {
            var featureMap = GetFeatureMap();
            if (featureMap.TryGetValue(container.Name, out var typeFeatures))
            {
                foreach (var entry in typeFeatures)
                {
                    if (entry._props.Contains(missingProperty))
                    {
                        return entry._lib;
                    }
                }
            }
        }
        return "";
    }

    internal Symbol? GetSuggestedSymbolForNonexistentProperty(Node name, Type containingType)
    {
        var props = GetPropertiesOfTypeWorker(containingType);
        var parent = name.Parent;
        if (IsPropertyAccessExpression(parent!))
        {
            props = Filter(props, prop => IsValidPropertyAccessForCompletionsWorker(parent!, containingType, prop));
        }
        // Go 直接传 nil 切片；`GetSpellingSuggestionForName` 的形参已是 `IEnumerable<Symbol>?`（null 安全）。
        return GetSpellingSuggestionForName(name.Text(), props, SymbolFlags.Value);
    }

    // Checks if an existing property access is valid for completions purposes.
    // @param node a property access-like node where we want to check if we can access a property.
    // This node does not need to be an access of the property we are checking.
    // e.g. in completions, this node will often be an incomplete property access node, as in `foo.`.
    // Besides providing a location (i.e. scope) used to check property accessibility, we use this node for
    // computing whether this is a `super` property access.
    // @param type the type whose property we are checking.
    // @param property the accessed property's symbol.
    internal bool IsValidPropertyAccessForCompletionsWorker(Node node, Type t, Symbol property)
    {
        return IsPropertyAccessibleWorker(node, IsPropertyAccessExpression(node) && node.Expression()!.Kind == Kind.SuperKeyword, false /*isWrite*/, t, property);
        // Previously we validated the 'this' type of methods but this adversely affected performance. See #31377 for more context.
    }

    // Checks if a property can be accessed in a location.
    // The location is given by the `node` parameter.
    // The node does not need to be a property access.
    // @param node location where to check property accessibility
    // @param isSuper whether to consider this a `super` property access, e.g. `super.foo`.
    // @param isWrite whether this is a write access, e.g. `++foo.x`.
    // @param containingType type where the property comes from.
    // @param property property symbol.
    internal bool IsPropertyAccessibleWorker(Node node, bool isSuper, bool isWrite, Type containingType, Symbol property)
    {
        // Short-circuiting for improved performance.
        if (IsTypeAny(containingType))
        {
            return true;
        }
        // A #private property access in an optional chain is an error dealt with by the parser.
        // The checker does not check for it, so we need to do our own check here.
        if (property.ValueDeclaration != null && IsPrivateIdentifierClassElementDeclaration(property.ValueDeclaration))
        {
            var declClass = GetContainingClass(property.ValueDeclaration);
            return !IsOptionalChain(node) && IsNodeDescendantOf(node, declClass);
        }
        return CheckPropertyAccessibilityAtLocation(node, isSuper, isWrite, containingType, property, null);
    }

    internal bool ContainerSeemsToBeEmptyDomElement(Type containingType)
    {
        return !_compilerOptions.Lib.Contains("lib.dom.d.ts") && EveryContainedType(containingType, HasCommonDomTypeName) && IsEmptyObjectType(containingType);
    }

    internal static bool HasCommonDomTypeName(Type t)
    {
        if (t._symbol == null)
        {
            return false;
        }
        var name = t._symbol.Name;
        return name == "EventTarget" || name == "Node" || name == "Element" || HasPrefix(name, "HTML", true) && HasSuffix(name, "Element", true);
    }

    internal bool CheckAndReportErrorForExtendingInterface(Node errorLocation)
    {
        var expression = GetEntityNameForExtendingInterface(errorLocation);
        if (expression != null && ResolveEntityName(expression, SymbolFlags.Interface, true /*ignoreErrors*/, false, null) != null)
        {
            Error(errorLocation, Diagnostics.Cannot_extend_an_interface_0_Did_you_mean_implements, GetTextOfNode(expression));
            return true;
        }
        return false;
    }

    /**
     * Climbs up parents to a heritage clause element and returns its entity name.
     */
    internal Node? GetEntityNameForExtendingInterface(Node node)
    {
        switch (node.Kind)
        {
            case Kind.Identifier:
            case Kind.QualifiedName:
            case Kind.PropertyAccessExpression:
                if (node.Parent != null)
                {
                    return GetEntityNameForExtendingInterface(node.Parent);
                }
                break;
            case Kind.TypeReference:
                return node.AsTypeReferenceNode()!.TypeName;
            case Kind.ExpressionWithTypeArguments:
                if (IsEntityNameExpression(node.Expression()!))
                {
                    return node.Expression();
                }
                break;
        }
        return null;
    }

    internal bool IsUncalledFunctionReference(Node node, Symbol symbol)
    {
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0)
        {
            var parent = FindAncestor(node.Parent, n => !IsAccessExpression(n));
            if (parent == null)
            {
                parent = node.Parent;
            }
            if (IsCallLikeExpression(parent!))
            {
                return IsCallOrNewExpression(parent!) && IsIdentifier(node) && HasMatchingArgument(parent!, node);
            }
            return Every(symbol.Declarations, d => !IsFunctionLike(d) || IsDeprecatedDeclaration(d));
        }
        return true;
    }

    internal void CheckPropertyNotUsedBeforeDeclaration(Symbol prop, Node node, Node right)
    {
        var valueDeclaration = prop.ValueDeclaration;
        if (valueDeclaration == null || GetSourceFileOfNode(node)!.IsDeclarationFile)
        {
            return;
        }
        Diagnostic? diagnostic = null;
        var declarationName = right.Text();
        if (IsInPropertyInitializerOrClassStaticBlock(node, false /*ignoreArrowFunctions*/) &&
            !IsOptionalPropertyDeclaration(valueDeclaration) &&
            !(IsAccessExpression(node) && IsAccessExpression(node.Expression()!)) &&
            !IsBlockScopedNameDeclaredBeforeUse(valueDeclaration, right) &&
            !(IsMethodDeclaration(valueDeclaration) && (GetCombinedModifierFlagsCached(valueDeclaration) & ModifierFlags.Static) != 0) &&
            (_compilerOptions.GetUseDefineForClassFields() || !IsPropertyDeclaredInAncestorClass(prop)))
        {
            diagnostic = Error(right, Diagnostics.Property_0_is_used_before_its_initialization, declarationName);
        }
        else if (IsClassDeclaration(valueDeclaration) && !IsTypeReferenceNode(node.Parent!) && (valueDeclaration.Flags & NodeFlags.Ambient) == 0 && !IsBlockScopedNameDeclaredBeforeUse(valueDeclaration, right))
        {
            diagnostic = Error(right, Diagnostics.Class_0_used_before_its_declaration, declarationName);
        }
        if (diagnostic != null)
        {
            diagnostic.AddRelatedInfo(NewDiagnosticForNode(valueDeclaration, Diagnostics.X_0_is_declared_here, declarationName));
        }
    }

    internal bool IsOptionalPropertyDeclaration(Node node)
    {
        return IsPropertyDeclaration(node) && !HasAccessorModifier(node) && IsQuestionToken(node.PostfixToken());
    }

    internal bool IsPropertyDeclaredInAncestorClass(Symbol prop)
    {
        if ((prop.Parent!.Flags & SymbolFlags.Class) != 0)
        {
            var baseTypes = GetBaseTypesWorker(GetDeclaredTypeOfSymbolWorker(prop.Parent)) ?? [];
            if (baseTypes.Length != 0)
            {
                var superProperty = GetPropertyOfTypeWorker(baseTypes[0], prop.Name);
                return superProperty != null && superProperty.ValueDeclaration != null;
            }
        }
        return false;
    }

    /**
     * Check whether the requested property access is valid.
     * Returns true if node is a valid property access, and false otherwise.
     * @param node The node to be checked.
     * @param isSuper True if the access is from `super.`.
     * @param type The type of the object whose property is being accessed. (Not the type of the property.)
     * @param prop The symbol for the property being accessed.
     */
    internal bool CheckPropertyAccessibility(Node node, bool isSuper, bool writing, Type t, Symbol prop)
    {
        return CheckPropertyAccessibilityEx(node, isSuper, writing, t, prop, true /*reportError*/);
    }

    internal bool CheckPropertyAccessibilityEx(Node node, bool isSuper, bool writing, Type t, Symbol prop, bool reportError /*  = true */)
    {
        Node? errorNode = null;
        if (reportError)
        {
            switch (node.Kind)
            {
                case Kind.PropertyAccessExpression:
                    errorNode = node.AsPropertyAccessExpression()!.Name();
                    break;
                case Kind.QualifiedName:
                    errorNode = node.AsQualifiedName()!.Right;
                    break;
                case Kind.ImportType:
                    errorNode = node;
                    break;
                case Kind.BindingElement:
                    errorNode = GetBindingElementPropertyName(node);
                    break;
                default:
                    errorNode = node.Name();
                    break;
            }
        }
        return CheckPropertyAccessibilityAtLocation(node, isSuper, writing, t, prop, errorNode);
    }

    /**
     * Check whether the requested property can be accessed at the requested location.
     * Returns true if node is a valid property access, and false otherwise.
     * @param location The location node where we want to check if the property is accessible.
     * @param isSuper True if the access is from `super.`.
     * @param writing True if this is a write property access, false if it is a read property access.
     * @param containingType The type of the object whose property is being accessed. (Not the type of the property.)
     * @param prop The symbol for the property being accessed.
     * @param errorNode The node where we should report an invalid property access error, or undefined if we should not report errors.
     */
    internal bool CheckPropertyAccessibilityAtLocation(Node location, bool isSuper, bool writing, Type containingType, Symbol prop, Node? errorNode)
    {
        var flags = GetDeclarationModifierFlagsFromSymbolEx(prop, writing);
        if (isSuper)
        {
            // TS 1.0 spec (April 2014): 4.8.2
            // - In a constructor, instance member function, instance member accessor, or
            //   instance member variable initializer where this references a derived class instance,
            //   a super property access is permitted and must specify a public instance member function of the base class.
            // - In a static member function or static member accessor
            //   where this references the constructor function object of a derived class,
            //   a super property access is permitted and must specify a public static member function of the base class.
            if ((flags & ModifierFlags.Abstract) != 0)
            {
                // A method cannot be accessed in a super property access if the method is abstract.
                // This error could mask a private property access error. But, a member
                // cannot simultaneously be private and abstract, so this will trigger an
                // additional error elsewhere.
                if (errorNode != null)
                {
                    Error(errorNode, Diagnostics.Abstract_method_0_in_class_1_cannot_be_accessed_via_super_expression, SymbolToStringWorker(prop), TypeToString(GetDeclaringClass(prop)!));
                }
                return false;
            }
            // A class field cannot be accessed via super.* from a derived class.
            // This is true for both [[Set]] (old) and [[Define]] (ES spec) semantics.
            if ((flags & ModifierFlags.Static) == 0 && Some(prop.Declarations, IsClassInstanceProperty))
            {
                if (errorNode != null)
                {
                    Error(errorNode, Diagnostics.Class_field_0_defined_by_the_parent_class_is_not_accessible_in_the_child_class_via_super, SymbolToStringWorker(prop));
                }
                return false;
            }
        }
        // Referencing abstract properties within their own constructors is not allowed
        if ((flags & ModifierFlags.Abstract) != 0 && SymbolHasNonMethodDeclaration(prop) && (IsThisProperty(location) ||
            IsThisInitializedObjectBindingExpression(location) ||
            IsObjectBindingPattern(location.Parent!) && IsThisInitializedDeclaration(location.Parent!.Parent!)))
        {
            var parentSymbol = GetParentOfSymbol(prop);
            if (parentSymbol != null && (parentSymbol.Flags & SymbolFlags.Class) != 0 && IsNodeUsedDuringClassInitialization(location))
            {
                if (errorNode != null)
                {
                    Error(errorNode, Diagnostics.Abstract_property_0_in_class_1_cannot_be_accessed_in_the_constructor, SymbolToStringWorker(prop), SymbolToStringWorker(parentSymbol));
                }
                return false;
            }
        }
        // Public properties are otherwise accessible.
        if ((flags & ModifierFlags.NonPublicAccessibilityModifier) == 0)
        {
            return true;
        }
        // Property is known to be private or protected at this point
        // Private property is accessible if the property is within the declaring class
        if ((flags & ModifierFlags.Private) != 0)
        {
            Node? declaringClassDeclaration = null;
            var parent = GetParentOfSymbol(prop);
            if (parent != null)
            {
                declaringClassDeclaration = GetClassLikeDeclarationOfSymbol(parent);
            }
            if (declaringClassDeclaration == null || !IsNodeWithinClass(location, declaringClassDeclaration))
            {
                if (errorNode != null)
                {
                    var cls = OrElse(GetDeclaringClass(prop), containingType);
                    Error(errorNode, Diagnostics.Property_0_is_private_and_only_accessible_within_class_1, SymbolToStringWorker(prop), TypeToString(cls!));
                }
                return false;
            }
            return true;
        }
        // Property is known to be protected at this point
        // All protected properties of a supertype are accessible in a super access
        if (isSuper)
        {
            return true;
        }
        // Find the first enclosing class that has the declaring classes of the protected constituents
        // of the property as base classes
        Type? enclosingClass = null;
        var container = GetContainingClass(location);
        while (container != null)
        {
            var cls = GetDeclaredTypeOfSymbolWorker(GetSymbolOfDeclaration(container)!);
            if (IsClassDerivedFromDeclaringClasses(cls, prop, writing))
            {
                enclosingClass = cls;
                break;
            }
            container = GetContainingClass(container);
        }
        // A protected property is accessible if the property is within the declaring class or classes derived from it
        if (enclosingClass == null)
        {
            // allow PropertyAccessibility if context is in function with this parameter
            // static member access is disallowed
            var cls = GetEnclosingClassFromThisParameter(location);
            if (cls != null && IsClassDerivedFromDeclaringClasses(cls, prop, writing))
            {
                enclosingClass = cls;
            }
            if ((flags & ModifierFlags.Static) != 0 || enclosingClass == null)
            {
                if (errorNode != null)
                {
                    var declaringClass = OrElse(GetDeclaringClass(prop), containingType);
                    Error(errorNode, Diagnostics.Property_0_is_protected_and_only_accessible_within_class_1_and_its_subclasses, SymbolToStringWorker(prop), TypeToString(declaringClass!));
                }
                return false;
            }
        }
        // No further restrictions for static properties
        if ((flags & ModifierFlags.Static) != 0)
        {
            return true;
        }
        if ((containingType._flags & TypeFlags.TypeParameter) != 0)
        {
            // get the original type -- represented as the type constraint of the 'this' type
            // Go 允许这里得到 nil（checker.go 的 `if containingType == nil` 分支），断言只为满足 C# 的非空形参类型。
            if (containingType.AsTypeParameter()!._isThisType)
            {
                containingType = GetConstraintOfTypeParameterWorker(containingType)!;
            }
            else
            {
                containingType = GetBaseConstraintOfTypeWorker(containingType)!;
            }
        }
        if (containingType == null || !HasBaseType(containingType, enclosingClass))
        {
            if (errorNode != null && containingType != null)
            {
                Error(errorNode, Diagnostics.Property_0_is_protected_and_only_accessible_through_an_instance_of_class_1_This_is_an_instance_of_class_2, SymbolToStringWorker(prop), TypeToString(enclosingClass), TypeToString(containingType));
            }
            return false;
        }
        return true;
    }

    internal bool SymbolHasNonMethodDeclaration(Symbol symbol)
    {
        return ForEachProperty(symbol, prop => (prop.Flags & SymbolFlags.Method) == 0);
    }

    // Invoke the callback for each underlying property symbol of the given symbol and return the first
    // value that isn't undefined.
    internal bool ForEachProperty(Symbol prop, Func<Symbol, bool> callback)
    {
        if ((prop.CheckFlags & CheckFlags.Synthetic) == 0)
        {
            return callback(prop);
        }
        foreach (var t in _valueSymbolLinks.Get(prop)._containingType!.Types())
        {
            var p = GetPropertyOfTypeWorker(t, prop.Name);
            if (p != null && ForEachProperty(p, callback))
            {
                return true;
            }
        }
        return false;
    }

    // Return the declaring class type of a property or undefined if property not declared in class
    internal Type? GetDeclaringClass(Symbol prop)
    {
        if (prop.Parent != null && (prop.Parent.Flags & SymbolFlags.Class) != 0)
        {
            return GetDeclaredTypeOfSymbolWorker(GetParentOfSymbol(prop)!);
        }
        return null;
    }

    // Return true if source property is a valid override of protected parts of target property.
    internal bool IsValidOverrideOf(Symbol sourceProp, Symbol targetProp)
    {
        return !ForEachProperty(targetProp, tp =>
        {
            if ((GetDeclarationModifierFlagsFromSymbol(tp) & ModifierFlags.Protected) != 0)
            {
                return !IsPropertyInClassDerivedFrom(sourceProp, GetDeclaringClass(tp)!);
            }
            return false;
        });
    }

    // Return true if some underlying source property is declared in a class that derives
    // from the given base class.
    internal bool IsPropertyInClassDerivedFrom(Symbol prop, Type baseClass)
    {
        return ForEachProperty(prop, sp =>
        {
            var sourceClass = GetDeclaringClass(sp);
            if (sourceClass != null)
            {
                return HasBaseType(sourceClass, baseClass);
            }
            return false;
        });
    }

    internal bool IsNodeUsedDuringClassInitialization(Node node)
    {
        return FindAncestorOrQuit(node, element =>
        {
            if (IsConstructorDeclaration(element) && NodeIsPresent(element.Body()) || IsPropertyDeclaration(element))
            {
                return FindAncestorResult.True;
            }
            else if (IsClassLike(element) || IsFunctionLikeDeclaration(element))
            {
                return FindAncestorResult.Quit;
            }
            return FindAncestorResult.False;
        }) != null;
    }

    internal bool IsNodeWithinClass(Node node, Node? classDeclaration)
    {
        return ForEachEnclosingClass(node, n => n == classDeclaration);
    }

    internal bool ForEachEnclosingClass(Node node, Func<Node, bool> callback)
    {
        var containingClass = GetContainingClass(node);
        while (containingClass != null)
        {
            var result = callback(containingClass);
            if (result)
            {
                return true;
            }
            containingClass = GetContainingClass(containingClass);
        }
        return false;
    }

    // Return true if the given class derives from each of the declaring classes of the protected
    // constituents of the given property.
    internal bool IsClassDerivedFromDeclaringClasses(Type checkClass, Symbol prop, bool writing)
    {
        return !ForEachProperty(prop, p =>
        {
            if ((GetDeclarationModifierFlagsFromSymbolEx(p, writing) & ModifierFlags.Protected) != 0)
            {
                return !HasBaseType(checkClass, GetDeclaringClass(p)!);
            }
            return false;
        });
    }
}
