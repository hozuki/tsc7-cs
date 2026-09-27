// ported from: internal/checker/checker.go:4319-4585（checkClassLikeDeclaration、checkJSDocAugmentsTagMatchesExtends、
//   checkClassForStaticPropertyNameConflicts、checkTypeParameterListsIdentical、getClassOrInterfaceDeclarationsOfSymbol、
//   areTypeParametersIdentical、checkBaseTypeAccessibility、issueMemberSpecificError、getTypeWithoutSignatures）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 未导出同名方法按 artifacts/checker-method-collisions.md 加 `Worker` 后缀（`getDeclaredTypeOfSymbol` →
//    `GetDeclaredTypeOfSymbolWorker` 等），导出者保持 Go 名（`TypeToString`）。
// 2) `areTypeParametersIdentical` 的函数参数 `func(*ast.Node) []*ast.Node` → `Func<Node, Node[]?>`；Go 传
//    `(*ast.Node).TypeParameters`，C# 实例方法组不能转委托 → 调用点写 `n => n.TypeParameters()`。
// 3) `c.getEffectivePropertyNameForPropertyNameNode(...)` 的 `(string, bool)` → `TryGetEffectivePropertyNameForPropertyNameNode(..., out string)`（docs/checker-plan §5.4）。
// 4) `core.Find`/`core.Some`/`core.Filter` 收 `T[]?`（Go 的 nil slice 语义一致）；`node.Members()` 为 nil 时
//    `foreach` 用 `?? []`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    internal void CheckClassLikeDeclaration(Node node)
    {
        CheckGrammarClassLikeDeclaration(node);
        CheckDecorators(node);
        CheckCollisionsForDeclarationName(node, node.Name());
        CheckTypeParameters(node.TypeParameters());
        CheckExportsOnMergedDeclarations(node);
        var symbol = GetSymbolOfDeclaration(node)!;
        var classType = GetDeclaredTypeOfSymbolWorker(symbol);
        var classTypeData = classType.AsInterfaceType()!;
        var typeWithThis = GetTypeWithThisArgument(classType, null, false);
        var staticType = GetTypeOfSymbolWorker(symbol);
        CheckTypeParameterListsIdentical(symbol);
        CheckFunctionOrConstructorSymbol(symbol);
        CheckObjectTypeForDuplicateDeclarations(node, true /*checkPrivateNames*/);

        // Only check for reserved static identifiers on non-ambient context.
        var nodeInAmbientContext = (node.Flags & NodeFlags.Ambient) != 0;
        if (!nodeInAmbientContext)
        {
            CheckClassForStaticPropertyNameConflicts(node);
        }

        var baseTypeNode = GetClassExtendsHeritageElement(node);
        if (baseTypeNode != null)
        {
            CheckSourceElements(baseTypeNode.TypeArguments() ?? []);
            var baseTypes = GetBaseTypesWorker(classType) ?? [];
            if (baseTypes.Length != 0)
            {
                var baseType = baseTypes[0];
                CheckJSDocAugmentsTagMatchesExtends(node, baseTypeNode, baseType);
                var baseConstructorType = GetBaseConstructorTypeOfClassWorker(classType);
                var staticBaseType = GetApparentTypeWorker(baseConstructorType);
                CheckBaseTypeAccessibility(staticBaseType, baseTypeNode);
                CheckSourceElement(baseTypeNode.Expression()!);
                if (baseTypeNode.TypeArguments()!.Length != 0)
                {
                    CheckSourceElements(baseTypeNode.TypeArguments() ?? []);
                    foreach (var constructor in GetConstructorsForTypeArguments(staticBaseType, baseTypeNode.TypeArguments()!, baseTypeNode))
                    {
                        if (!CheckTypeArgumentConstraints(baseTypeNode, constructor._typeParameters))
                        {
                            break;
                        }
                    }
                }
                var baseWithThis = GetTypeWithThisArgument(baseType, classTypeData._thisType, false);
                if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, null, null))
                {
                    IssueMemberSpecificError(node, typeWithThis, baseWithThis, Diagnostics.Class_0_incorrectly_extends_base_class_1);
                }
                else
                {
                    // Report static side error only when instance type is assignable
                    CheckTypeAssignableTo(staticType, GetTypeWithoutSignatures(staticBaseType), OrElse(node.Name(), node), Diagnostics.Class_static_side_0_incorrectly_extends_base_class_static_side_1);
                }
                if ((baseConstructorType._flags & TypeFlags.TypeVariable) != 0)
                {
                    if (!IsMixinConstructorType(staticType))
                    {
                        Error(OrElse(node.Name(), node), Diagnostics.A_mixin_class_must_have_a_constructor_with_a_single_rest_parameter_of_type_any);
                    }
                    else
                    {
                        var constructSignatures = GetSignaturesOfTypeWorker(baseConstructorType, SignatureKind.Construct);
                        if (Some(constructSignatures, signature => (signature._flags & SignatureFlags.Abstract) != 0) && !HasSyntacticModifier(node, ModifierFlags.Abstract))
                        {
                            Error(OrElse(node.Name(), node), Diagnostics.A_mixin_class_that_extends_from_a_type_variable_containing_an_abstract_construct_signature_must_also_be_declared_abstract);
                        }
                    }
                }
                if (!(staticBaseType._symbol != null && (staticBaseType._symbol.Flags & SymbolFlags.Class) != 0) && (baseConstructorType._flags & TypeFlags.TypeVariable) == 0)
                {
                    // When the static base type is a "class-like" constructor function (but not actually a class), we verify
                    // that all instantiated base constructor signatures return the same type.
                    var constructors = GetInstantiatedConstructorsForTypeArguments(staticBaseType, baseTypeNode.TypeArguments(), baseTypeNode);
                    if (!Every(constructors, sig => IsTypeIdenticalTo(GetReturnTypeOfSignatureWorker(sig), baseType)))
                    {
                        Error(baseTypeNode.Expression()!, Diagnostics.Base_constructors_must_all_have_the_same_return_type);
                    }
                }
                CheckKindsOfPropertyMemberOverrides(classType, baseType);
            }
        }
        CheckMembersForOverrideModifier(node, classType, typeWithThis, staticType);
        var implementedTypeNodes = GetImplementsHeritageClauseElements(node);
        foreach (var typeRefNode in implementedTypeNodes)
        {
            if (IsExpressionWithTypeArguments(typeRefNode))
            {
                var expr = typeRefNode.Expression()!;
                if (!IsEntityNameExpression(expr) || IsOptionalChain(expr))
                {
                    Error(expr, Diagnostics.A_class_can_only_implement_an_identifier_Slashqualified_name_with_optional_type_arguments);
                }
            }
            CheckTypeReferenceNode(typeRefNode);
            var t = GetReducedTypeWorker(GetTypeFromTypeNodeWorker(typeRefNode));
            if (!IsErrorType(t))
            {
                if (IsValidBaseType(t))
                {
                    var genericDiag = IfElse(t._symbol != null && (t._symbol.Flags & SymbolFlags.Class) != 0,
                        Diagnostics.Class_0_incorrectly_implements_class_1_Did_you_mean_to_extend_1_and_inherit_its_members_as_a_subclass,
                        Diagnostics.Class_0_incorrectly_implements_interface_1);
                    var baseWithThis = GetTypeWithThisArgument(t, classType.AsInterfaceType()!._thisType, false);
                    if (!CheckTypeAssignableTo(typeWithThis, baseWithThis, null, null))
                    {
                        IssueMemberSpecificError(node, typeWithThis, baseWithThis, genericDiag);
                    }
                }
                else
                {
                    Error(typeRefNode, Diagnostics.A_class_can_only_implement_an_object_type_or_intersection_of_object_types_with_statically_known_members);
                }
            }
        }
        CheckIndexConstraints(classType, symbol, false /*isStaticIndex*/);
        CheckIndexConstraints(staticType, symbol, true /*isStaticIndex*/);
        CheckClassOrInterfaceForDuplicateIndexSignatures(node);
        CheckPropertyInitialization(node);
    }

    internal void CheckJSDocAugmentsTagMatchesExtends(Node node, Node baseTypeNode, Type baseType)
    {
        if (!IsInJSFile(node))
        {
            return;
        }

        var file = GetSourceFileOfNode(node);
        foreach (var j in node.EagerJSDoc(file) ?? [])
        {
            if (j.AsJSDoc()!.Tags == null)
            {
                continue;
            }

            foreach (var tag in j.AsJSDoc()!.Tags!.Nodes!)
            {
                if (tag.Kind != Kind.JSDocAugmentsTag)
                {
                    continue;
                }

                var sourceTypeNode = tag.ClassName()!;
                if (IsTypeIdenticalTo(GetTypeFromTypeNodeWorker(sourceTypeNode), baseType))
                {
                    continue;
                }

                var targetName = GetIdentifierFromEntityNameExpression(baseTypeNode.Expression()!);
                var sourceName = GetIdentifierFromEntityNameExpression(sourceTypeNode.Expression()!);
                if (targetName != null && sourceName != null)
                {
                    Error(sourceName, Diagnostics.JSDoc_0_1_does_not_match_the_extends_2_clause, tag.TagName()!.Text(), sourceName.Text(), targetName.Text());
                }
            }
        }
    }

    internal void CheckClassForStaticPropertyNameConflicts(Node node)
    {
        if (_compilerOptions.GetUseDefineForClassFields())
        {
            return;
        }

        foreach (var member in node.Members() ?? [])
        {
            var memberNameNode = member.Name();
            var isStaticMember = IsStatic(member);
            if (isStaticMember && memberNameNode != null)
            {
                TryGetEffectivePropertyNameForPropertyNameNode(memberNameNode, out var memberName);
                switch (memberName)
                {
                    case "name":
                    case "length":
                    case "caller":
                    case "arguments":
                        Error(
                            memberNameNode,
                            Diagnostics.Static_property_0_conflicts_with_built_in_property_Function_0_of_constructor_function_1,
                            memberName,
                            SymbolToStringWorker(GetSymbolOfDeclaration(node)!));
                        break;
                }
            }
        }
    }

    // Check that type parameter lists are identical across multiple declarations
    internal void CheckTypeParameterListsIdentical(Symbol symbol)
    {
        if (symbol.Declarations?.Length == 1)
        {
            return;
        }

        var links = _declaredTypeLinks.Get(symbol);
        if (!links._typeParametersChecked)
        {
            links._typeParametersChecked = true;
            var declarations = GetClassOrInterfaceDeclarationsOfSymbol(symbol);
            if (declarations.Length <= 1)
            {
                return;
            }

            var t = GetDeclaredTypeOfSymbolWorker(symbol);
            if (!AreTypeParametersIdentical(declarations, t.AsInterfaceType()!.LocalTypeParameters(), n => n.TypeParameters()))
            {
                // Report an error on every conflicting declaration.
                var name = SymbolToStringWorker(symbol);
                foreach (var declaration in declarations)
                {
                    Error(declaration.Name(), Diagnostics.All_declarations_of_0_must_have_identical_type_parameters, name);
                }
            }
        }
    }

    internal Node[] GetClassOrInterfaceDeclarationsOfSymbol(Symbol symbol)
    {
        return Filter(symbol.Declarations, d => IsClassDeclaration(d) || IsInterfaceDeclaration(d));
    }

    internal bool AreTypeParametersIdentical(Node[] declarations, Type[] targetParameters, Func<Node, Node[]?> getTypeParameterDeclarations)
    {
        var maxTypeArgumentCount = targetParameters.Length;
        var minTypeArgumentCount = GetMinTypeArgumentCountWorker(targetParameters);
        foreach (var declaration in declarations)
        {
            // If this declaration has too few or too many type parameters, we report an error
            // Go: `len(getTypeParameterDeclarations(declaration))` —— 无类型参数时是 nil 切片，len 为 0；
            // C# 的 `TypeParameters()` 返回 null，等价于空数组（原实现把 null 直接当"不合格"误报 TS2428）。
            var sourceParameters = getTypeParameterDeclarations(declaration) ?? [];
            if (sourceParameters.Length < minTypeArgumentCount || sourceParameters.Length > maxTypeArgumentCount)
            {
                return false;
            }

            for (var i = 0; i < sourceParameters.Length; i++)
            {
                var source = sourceParameters[i];
                var target = targetParameters[i];

                // If the type parameter node does not have the same name as the resolved type
                // parameter at this position, we report an error.
                if (source.Name()!.Text() != target._symbol!.Name)
                {
                    return false;
                }

                // If the type parameter node does not have an identical constraintNode as the resolved
                // type parameter at this position, we report an error.
                var constraintNode = source.AsTypeParameterDeclaration()!.Constraint;
                var targetConstraint = GetConstraintOfTypeParameterWorker(target);

                // relax check if later interface augmentation has no constraint, it's more broad and is OK to merge with
                // a more constrained interface (this could be generalized to a full hierarchy check, but that's maybe overkill)
                if (constraintNode != null && targetConstraint != null && !IsTypeIdenticalTo(GetTypeFromTypeNodeWorker(constraintNode), targetConstraint))
                {
                    return false;
                }

                // If the type parameter node has a default and it is not identical to the default
                // for the type parameter at this position, we report an error.
                var defaultNode = source.AsTypeParameterDeclaration()!.DefaultType;
                var targetDefault = GetDefaultFromTypeParameterWorker(target);
                if (defaultNode != null && targetDefault != null && !IsTypeIdenticalTo(GetTypeFromTypeNodeWorker(defaultNode), targetDefault))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal void CheckBaseTypeAccessibility(Type t, Node node)
    {
        var signatures = GetSignaturesOfTypeWorker(t, SignatureKind.Construct);
        var accessibilityError = GetConstructorAccessibilityError(node, signatures ?? [], ModifierFlags.Private);
        if (accessibilityError != null)
        {
            Error(node, Diagnostics.Cannot_extend_a_class_0_Class_constructor_is_marked_as_private, GetFullyQualifiedNameWorker(accessibilityError._declaringClass!._symbol!, null));
        }
    }

    internal void IssueMemberSpecificError(Node node, Type typeWithThis, Type baseWithThis, Message broadDiag)
    {
        // iterate over all implemented properties and issue errors on each one which isn't compatible, rather than the class as a whole, if possible
        var issuedMemberError = false;
        foreach (var member in node.Members() ?? [])
        {
            if (IsStatic(member))
            {
                continue;
            }

            if (GetSymbolOfDeclaration(member) is { } declaredProp && declaredProp.Name != InternalSymbolNameComputed)
            {
                var prop = GetPropertyOfTypeWorker(typeWithThis, declaredProp.Name);
                var baseProp = GetPropertyOfTypeWorker(baseWithThis, declaredProp.Name);
                if (prop != null && baseProp != null)
                {
                    if (!CheckTypeAssignableToEx(GetTypeOfSymbolWorker(prop), GetTypeOfSymbolWorker(baseProp), OrElse(member.Name(), member), null /*headMessage*/, out var diags))
                    {
                        AddDiagnostic(NewDiagnosticChain(diags![0], Diagnostics.Property_0_in_type_1_is_not_assignable_to_the_same_property_in_base_type_2, SymbolToStringWorker(declaredProp), TypeToString(typeWithThis), TypeToString(baseWithThis)));
                        issuedMemberError = true;
                    }
                }
            }
        }

        if (!issuedMemberError)
        {
            // check again with diagnostics to generate a less-specific error
            CheckTypeAssignableTo(typeWithThis, baseWithThis, OrElse(node.Name(), node), broadDiag);
        }
    }

    internal Type GetTypeWithoutSignatures(Type t)
    {
        // Go 是 `if/else if`（checker.go getTypeWithoutSignatures）；这里刻意不用 `switch (t) { case { } when ... }`：
        // Roslyn 会把「未匹配的 fall-through 路径」里的形参 t 当成 maybe-null，从而在 `return t;` 报 CS8603（实测）。
        if ((t._flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(t);
            if ((resolved._signatures?.Length ?? 0) != 0)
            {
                var result = NewObjectType(ObjectFlags.Anonymous, t._symbol);
                result._objectFlags |= ObjectFlags.MembersResolved;
                result.AsObjectType()!._members = resolved._members;
                result.AsObjectType()!._properties = resolved._properties;
                return result;
            }
        }
        else if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return GetIntersectionType(Map(t.AsIntersectionType()!._types, x => GetTypeWithoutSignatures(x)));
        }

        return t;
    }
}
