// ported from: internal/checker/checker.go:4586-4874（checkKindsOfPropertyMemberOverrides、arePropertiesAbstractOrInterface、
//   isPropertyAbstractOrInterface、checkMembersForOverrideModifier、checkMemberForOverrideModifier、
//   getMemberOverrideModifierStatus→Worker、checkMemberForOverrideModifierWorker、getSuggestedSymbolForNonexistentClassMember）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的函数内局部类型 `type MemberInfo struct{...}` → 本文件里的**嵌套私有类** `MemberInfo`（C# 没有函数内类型）。
//    其 `missedProperties []string` + `append`（C# 数组没有 len<cap 视图，docs/architecture.md §3.2）→ `List<string>`；
//    因此 `len(missedProperties)` → `.Count`，`missedProperties[:4]` → 手写取前 4 个的循环（不用 LINQ）。
// 2) Go 的 `notImplementedInfo map[*ast.Node]MemberInfo` 读缺失键返回零值 → `TryGetValue`（`Dictionary` 索引器抛异常）。
// 3) `getMemberOverrideModifierStatus` → `GetMemberOverrideModifierStatusWorker`（artifacts/checker-method-collisions.md：
//    导出的 `GetMemberOverrideModifierStatus` 在 exports.go:326）。
// 4) 标识符 `base` 是 C# 关键字 → 重命名为 `baseSym`（Go 的局部变量名，语义不变）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: <c>checkKindsOfPropertyMemberOverrides</c> 内的函数局部 <c>type MemberInfo struct</c>。</summary>
    internal sealed class MemberInfo
    {
        internal List<string> _missedProperties = new();
        internal string _baseTypeName = "";
        internal string _typeName = "";
    }

    internal void CheckKindsOfPropertyMemberOverrides(Type t, Type baseType)
    {
        // TypeScript 1.0 spec (April 2014): 8.2.3
        // A derived class inherits all members from its base class it doesn't override.
        // Inheritance means that a derived class implicitly contains all non - overridden members of the base class.
        // Both public and private property members are inherited, but only public property members can be overridden.
        // A property member in a derived class is said to override a property member in a base class
        // when the derived class property member has the same name and kind(instance or static)
        // as the base class property member.
        // The type of an overriding property member must be assignable(section 3.8.4)
        // to the type of the overridden property member, or otherwise a compile - time error occurs.
        // Base class instance member functions can be overridden by derived class instance member functions,
        // but not by other kinds of members.
        // Base class instance member variables and accessors can be overridden by
        // derived class instance member variables and accessors, but not by other kinds of members.
        // NOTE: assignability is checked in checkClassDeclaration
        var notImplementedInfo = new Dictionary<Node, MemberInfo>();
        foreach (var baseProperty in GetPropertiesOfTypeWorker(baseType) ?? [])
        {
            // Go: `base`
            var baseSym = GetTargetSymbolWorker(baseProperty);
            if ((baseSym.Flags & SymbolFlags.Prototype) != 0)
            {
                continue;
            }

            var baseSymbol = GetPropertyOfObjectType(t, baseSym.Name);
            if (baseSymbol == null)
            {
                continue;
            }

            var derived = GetTargetSymbolWorker(baseSymbol);
            var baseDeclarationFlags = GetDeclarationModifierFlagsFromSymbol(baseSym);

            // In order to resolve whether the inherited method was overridden in the base class or not,
            // we compare the Symbols obtained. Since getTargetSymbol returns the symbol on the *uninstantiated*
            // type declaration, derived and base resolve to the same symbol even in the case of generic classes.
            if (derived == baseSym)
            {
                // derived class inherits base without override/redeclaration.
                if ((baseDeclarationFlags & ModifierFlags.Abstract) != 0)
                {
                    // It is an error to inherit an abstract member without implementing it or being declared abstract.
                    // If there is no declaration for the derived class (as in the case of class expressions),
                    // then the class cannot be declared abstract.
                    var derivedClassDecl = GetClassLikeDeclarationOfSymbol(t._symbol!);
                    if (derivedClassDecl == null || !HasSyntacticModifier(derivedClassDecl, ModifierFlags.Abstract))
                    {
                        // Searches other base types for a declaration that would satisfy the inherited abstract member.
                        // (The class may have more than one base type via declaration merging with an interface with the
                        // same name.)
                        var derivedPropertyExistsElsewhere = false;
                        foreach (var otherBaseType in GetBaseTypesWorker(t) ?? [])
                        {
                            if (otherBaseType == baseType)
                            {
                                continue;
                            }

                            var otherBaseSymbol = GetPropertyOfObjectType(otherBaseType, baseSym.Name);
                            if (otherBaseSymbol != null && baseSym != GetTargetSymbolWorker(otherBaseSymbol))
                            {
                                // Derived property exists elsewhere.
                                derivedPropertyExistsElsewhere = true;
                                break;
                            }
                        }

                        if (derivedPropertyExistsElsewhere)
                        {
                            // Go: `continue basePropertyCheck`
                            continue;
                        }

                        var baseTypeName = TypeToString(baseType);
                        var typeName = TypeToString(t);
                        if (!notImplementedInfo.TryGetValue(derivedClassDecl!, out var memberInfo))
                        {
                            memberInfo = new MemberInfo { _baseTypeName = baseTypeName, _typeName = typeName };
                            notImplementedInfo[derivedClassDecl!] = memberInfo;
                        }

                        memberInfo._missedProperties.Add(SymbolToStringWorker(baseProperty));
                    }
                }
            }
            else
            {
                // derived overrides base.
                var derivedDeclarationFlags = GetDeclarationModifierFlagsFromSymbol(derived);
                if ((baseDeclarationFlags & ModifierFlags.Private) != 0 || (derivedDeclarationFlags & ModifierFlags.Private) != 0)
                {
                    // either base or derived property is private - not override, skip it
                    continue;
                }

                Message? errorMessage = null;
                var basePropertyFlags = baseSym.Flags & SymbolFlags.PropertyOrAccessor;
                var derivedPropertyFlags = derived.Flags & SymbolFlags.PropertyOrAccessor;
                if (basePropertyFlags != 0 && derivedPropertyFlags != 0)
                {
                    // property/accessor is overridden with property/accessor
                    if ((baseSym.CheckFlags & CheckFlags.Mapped) != 0 ||
                        (derived.ValueDeclaration != null && IsBinaryExpression(derived.ValueDeclaration)) ||
                        ArePropertiesAbstractOrInterface(baseSym, baseDeclarationFlags))
                    {
                        // when the base property is abstract or from an interface, base/derived flags don't need to match
                        // for intersection properties, this must be true of *any* of the declarations, for others it must be true of *all*
                        // same when the derived property is from an assignment
                        continue;
                    }

                    var overriddenInstanceProperty = basePropertyFlags != SymbolFlags.Property && derivedPropertyFlags == SymbolFlags.Property;
                    var overriddenInstanceAccessor = basePropertyFlags == SymbolFlags.Property && derivedPropertyFlags != SymbolFlags.Property;
                    if (overriddenInstanceProperty || overriddenInstanceAccessor)
                    {
                        errorMessage = IfElse(overriddenInstanceProperty,
                            Diagnostics.X_0_is_defined_as_an_accessor_in_class_1_but_is_overridden_here_in_2_as_an_instance_property,
                            Diagnostics.X_0_is_defined_as_a_property_in_class_1_but_is_overridden_here_in_2_as_an_accessor);
                        Error(OrElse(GetNameOfDeclaration(derived.ValueDeclaration), derived.ValueDeclaration), errorMessage, SymbolToStringWorker(baseSym), TypeToString(baseType), TypeToString(t));
                    }
                    else if (_compilerOptions.GetUseDefineForClassFields())
                    {
                        var uninitialized = Find(derived.Declarations, d => IsPropertyDeclaration(d) && d.Initializer() == null);
                        if (uninitialized != null && (derived.Flags & SymbolFlags.Transient) == 0 && (baseDeclarationFlags & ModifierFlags.Abstract) == 0 && (derivedDeclarationFlags & ModifierFlags.Abstract) == 0 && !Some(derived.Declarations, d => (d.Flags & NodeFlags.Ambient) != 0))
                        {
                            var constructor = FindConstructorDeclaration(GetClassLikeDeclarationOfSymbol(t._symbol!));
                            var propName = uninitialized.Name();
                            if (IsExclamationToken(uninitialized.PostfixToken()) || constructor == null || !IsIdentifier(propName!) || !_strictNullChecks || !IsPropertyInitializedInConstructor(propName!, t, constructor))
                            {
                                var overwriteMessage = Diagnostics.Property_0_will_overwrite_the_base_property_in_1_If_this_is_intentional_add_an_initializer_Otherwise_add_a_declare_modifier_or_remove_the_redundant_declaration;
                                Error(OrElse(GetNameOfDeclaration(derived.ValueDeclaration), derived.ValueDeclaration), overwriteMessage, SymbolToStringWorker(baseSym), TypeToString(baseType));
                            }
                        }
                    }

                    // correct case
                    continue;
                }
                else if (IsPrototypeProperty(baseSym))
                {
                    if (IsPrototypeProperty(derived) || (derived.Flags & SymbolFlags.Property) != 0)
                    {
                        // method is overridden with method or property -- correct case
                        continue;
                    }
                    else
                    {
                        errorMessage = Diagnostics.Class_0_defines_instance_member_function_1_but_extended_class_2_defines_it_as_instance_member_accessor;
                    }
                }
                else if ((baseSym.Flags & SymbolFlags.Accessor) != 0)
                {
                    errorMessage = Diagnostics.Class_0_defines_instance_member_accessor_1_but_extended_class_2_defines_it_as_instance_member_function;
                }
                else
                {
                    errorMessage = Diagnostics.Class_0_defines_instance_member_property_1_but_extended_class_2_defines_it_as_instance_member_function;
                }

                Error(OrElse(GetNameOfDeclaration(derived.ValueDeclaration), derived.ValueDeclaration), errorMessage!, TypeToString(baseType), SymbolToStringWorker(baseSym), TypeToString(t));
            }
        }

        foreach (var (errorNode, memberInfo) in notImplementedInfo)
        {
            if (memberInfo._missedProperties.Count == 1)
            {
                var missedProperty = memberInfo._missedProperties[0];
                if (IsClassExpression(errorNode))
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_does_not_implement_inherited_abstract_member_0_from_class_1, missedProperty, memberInfo._baseTypeName);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_does_not_implement_inherited_abstract_member_1_from_class_2, memberInfo._typeName, missedProperty, memberInfo._baseTypeName);
                }
            }
            else if (memberInfo._missedProperties.Count > 5)
            {
                // Go: `core.Map(missedProperties[:4], ...)`
                var firstFour = new string[4];
                for (var i = 0; i < 4; i++)
                {
                    firstFour[i] = "'" + memberInfo._missedProperties[i] + "'";
                }

                var missedProperties = string.Join(", ", firstFour);
                var remainingMissedProperties = memberInfo._missedProperties.Count - 4;
                if (IsClassExpression(errorNode))
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_is_missing_implementations_for_the_following_members_of_0_Colon_1_and_2_more, memberInfo._baseTypeName, missedProperties, remainingMissedProperties);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_is_missing_implementations_for_the_following_members_of_1_Colon_2_and_3_more, memberInfo._typeName, memberInfo._baseTypeName, missedProperties, remainingMissedProperties);
                }
            }
            else
            {
                var missedProperties = string.Join(", ", Map(memberInfo._missedProperties.ToArray(), prop => "'" + prop + "'"));
                if (IsClassExpression(errorNode))
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_expression_is_missing_implementations_for_the_following_members_of_0_Colon_1, memberInfo._baseTypeName, missedProperties);
                }
                else
                {
                    Error(errorNode, Diagnostics.Non_abstract_class_0_is_missing_implementations_for_the_following_members_of_1_Colon_2, memberInfo._typeName, memberInfo._baseTypeName, missedProperties);
                }
            }
        }
    }

    internal bool ArePropertiesAbstractOrInterface(Symbol baseSym, ModifierFlags baseDeclarationFlags)
    {
        if ((baseSym.CheckFlags & CheckFlags.Synthetic) != 0)
        {
            return Some(baseSym.Declarations, d => IsPropertyAbstractOrInterface(d, baseDeclarationFlags));
        }

        return Every(baseSym.Declarations, d => IsPropertyAbstractOrInterface(d, baseDeclarationFlags));
    }

    internal bool IsPropertyAbstractOrInterface(Node declaration, ModifierFlags baseDeclarationFlags)
    {
        return IsInterfaceDeclaration(declaration.Parent!) ||
            ((baseDeclarationFlags & ModifierFlags.Abstract) != 0 && (!IsPropertyDeclaration(declaration) || declaration.Initializer() == null));
    }

    internal void CheckMembersForOverrideModifier(Node node, Type t, Type typeWithThis, Type staticType)
    {
        Type? baseWithThis = null;
        var baseTypeNode = GetClassExtendsHeritageElement(node);
        if (baseTypeNode != null)
        {
            var baseTypes = GetBaseTypesWorker(t) ?? [];
            if (baseTypes.Length > 0)
            {
                baseWithThis = GetTypeWithThisArgument(FirstOrNil(baseTypes)!, t.AsInterfaceType()!._thisType, false);
            }
        }

        var baseStaticType = GetBaseConstructorTypeOfClassWorker(t);
        foreach (var member in node.Members() ?? [])
        {
            if (!HasAmbientModifier(member))
            {
                if (IsConstructorDeclaration(member))
                {
                    foreach (var param in member.Parameters() ?? [])
                    {
                        if (IsParameterPropertyDeclaration(param, member))
                        {
                            CheckMemberForOverrideModifier(node, staticType, baseStaticType, baseWithThis, t, typeWithThis, param);
                        }
                    }
                }
                else
                {
                    CheckMemberForOverrideModifier(node, staticType, baseStaticType, baseWithThis, t, typeWithThis, member);
                }
            }
        }
    }

    internal void CheckMemberForOverrideModifier(Node node, Type staticType, Type baseStaticType, Type? baseWithThis, Type t, Type typeWithThis, Node member)
    {
        var symbol = GetSymbolOfDeclaration(member);
        if (symbol == null)
        {
            return;
        }

        CheckMemberForOverrideModifierWorker(node, staticType, baseStaticType, baseWithThis, t, typeWithThis, HasOverrideModifier(member), HasAbstractModifier(member), IsStatic(member), IsParameterDeclaration(member), symbol, member);
    }

    internal MemberOverrideStatus GetMemberOverrideModifierStatusWorker(Node node, Node member, Symbol? memberSymbol)
    {
        if (member.Name() == null || memberSymbol == null)
        {
            return MemberOverrideStatus.None;
        }

        var classSymbol = GetSymbolOfDeclaration(node);
        if (classSymbol == null)
        {
            return MemberOverrideStatus.None;
        }

        var t = GetDeclaredTypeOfSymbolWorker(classSymbol);
        var typeWithThis = GetTypeWithThisArgument(t, null, false);
        var staticType = GetTypeOfSymbolWorker(classSymbol);

        Type? baseWithThis = null;
        if (GetClassExtendsHeritageElement(node) != null)
        {
            var baseTypes = GetBaseTypesWorker(t) ?? [];
            if (baseTypes.Length > 0)
            {
                baseWithThis = GetTypeWithThisArgument(baseTypes[0], t.AsInterfaceType()!._thisType, false);
            }
        }

        return CheckMemberForOverrideModifierWorker(node, staticType, GetBaseConstructorTypeOfClassWorker(t), baseWithThis, t, typeWithThis, HasSyntacticModifier(member, ModifierFlags.Override), HasAbstractModifier(member), IsStatic(member), false /*memberIsParameterProperty*/, memberSymbol, null /*errorNode*/);
    }

    internal MemberOverrideStatus CheckMemberForOverrideModifierWorker(Node node, Type staticType, Type baseStaticType, Type? baseWithThis, Type t, Type typeWithThis, bool memberHasOverrideModifier, bool memberHasAbstractModifier, bool memberIsStatic, bool memberIsParameterProperty, Symbol member, Node? errorNode)
    {
        var isJs = IsInJSFile(node);
        if (memberHasOverrideModifier && member.ValueDeclaration != null && IsClassElement(member.ValueDeclaration) && member.ValueDeclaration.Name() != null && IsNonBindableDynamicName(member.ValueDeclaration.Name()!))
        {
            if (errorNode != null)
            {
                Error(errorNode, IfElse(isJs, Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_its_name_is_dynamic, Diagnostics.This_member_cannot_have_an_override_modifier_because_its_name_is_dynamic));
            }

            return MemberOverrideStatus.HasInvalidOverride;
        }

        if (baseWithThis != null && (memberHasOverrideModifier || _compilerOptions.NoImplicitOverride.IsTrue()))
        {
            var thisType = IfElse(memberIsStatic, staticType, typeWithThis);
            var baseType = IfElse(memberIsStatic, baseStaticType, baseWithThis);
            var prop = GetPropertyOfTypeWorker(thisType, member.Name);
            var baseProp = GetPropertyOfTypeWorker(baseType, member.Name);

            if (prop != null && baseProp == null && memberHasOverrideModifier)
            {
                if (errorNode != null)
                {
                    var suggestion = GetSuggestedSymbolForNonexistentClassMember(SymbolName(member), baseType);
                    if (suggestion != null)
                    {
                        Error(errorNode, IfElse(isJs, Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_it_is_not_declared_in_the_base_class_0_Did_you_mean_1, Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0_Did_you_mean_1), TypeToString(baseWithThis), SymbolToStringWorker(suggestion));
                    }
                    else
                    {
                        Error(errorNode, IfElse(isJs, Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_it_is_not_declared_in_the_base_class_0, Diagnostics.This_member_cannot_have_an_override_modifier_because_it_is_not_declared_in_the_base_class_0), TypeToString(baseWithThis));
                    }
                }

                return MemberOverrideStatus.HasInvalidOverride;
            }

            if (prop != null && baseProp != null && baseProp.Declarations!.Length > 0 && _compilerOptions.NoImplicitOverride.IsTrue() && (node.Flags & NodeFlags.Ambient) == 0)
            {
                var baseHasAbstract = Some(baseProp.Declarations, d => HasAbstractModifier(d));
                if (memberHasOverrideModifier)
                {
                    return MemberOverrideStatus.None;
                }

                if (!baseHasAbstract)
                {
                    if (errorNode != null)
                    {
                        var message = IfElse(memberIsParameterProperty,
                            IfElse(isJs, Diagnostics.This_parameter_property_must_have_a_JSDoc_comment_with_an_override_tag_because_it_overrides_a_member_in_the_base_class_0, Diagnostics.This_parameter_property_must_have_an_override_modifier_because_it_overrides_a_member_in_base_class_0),
                            IfElse(isJs, Diagnostics.This_member_must_have_a_JSDoc_comment_with_an_override_tag_because_it_overrides_a_member_in_the_base_class_0, Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_a_member_in_the_base_class_0));
                        Error(errorNode, message, TypeToString(baseWithThis));
                    }

                    return MemberOverrideStatus.NeedsOverride;
                }

                if (memberHasAbstractModifier)
                {
                    if (errorNode != null)
                    {
                        Error(errorNode, Diagnostics.This_member_must_have_an_override_modifier_because_it_overrides_an_abstract_method_that_is_declared_in_the_base_class_0, TypeToString(baseWithThis));
                    }

                    return MemberOverrideStatus.NeedsOverride;
                }
            }
        }
        else if (memberHasOverrideModifier)
        {
            if (errorNode != null)
            {
                Error(errorNode, IfElse(isJs, Diagnostics.This_member_cannot_have_a_JSDoc_comment_with_an_override_tag_because_its_containing_class_0_does_not_extend_another_class, Diagnostics.This_member_cannot_have_an_override_modifier_because_its_containing_class_0_does_not_extend_another_class), TypeToString(t));
            }

            return MemberOverrideStatus.HasInvalidOverride;
        }

        return MemberOverrideStatus.None;
    }

    internal Symbol? GetSuggestedSymbolForNonexistentClassMember(string name, Type baseType)
    {
        return GetSpellingSuggestionForName(name, GetPropertiesOfTypeWorker(baseType) ?? [], SymbolFlags.ClassMember);
    }
}
