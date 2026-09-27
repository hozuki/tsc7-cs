// ported from: internal/checker/checker.go:5080-5220（checkInterfaceDeclaration、InheritanceInfo、
//   checkInheritedPropertiesAreIdentical、isPropertyIdenticalTo、checkEnumDeclaration、checkEnumMember）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 未导出同名方法加 `Worker`：`getDeclaredTypeOfSymbol`/`getPropertiesOfType`。
// 2) `c.compareTypesIdentical`（Go 把实例方法当函数值传）→ lambda `(s, t) => CompareTypesIdentical(s, t)`
//    （实例方法组不能转委托，ChkC7 广播；委托类型 `TypeRelatedComparer` 归 relater.go）。
// 3) Go 的包级未导出类型 `InheritanceInfo` → C# 顶层 `internal sealed class InheritanceInfo`（未导出类型按 C# 习惯名）。
// 4) `for id, p := range m` → `foreach (var (id, p) in m)`；`seen[k]` 读缺失键 → `TryGetValue`。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

/// <summary>Go: <c>type InheritanceInfo struct { prop *ast.Symbol; containingType *Type }</c>。</summary>
internal sealed class InheritanceInfo
{
    internal Symbol? _prop;
    internal Type? _containingType;
}

public sealed partial class Checker
{
    internal void CheckInterfaceDeclaration(Node node)
    {
        if (!CheckGrammarModifiers(node))
        {
            CheckGrammarInterfaceDeclaration(node.AsInterfaceDeclaration()!);
        }

        if (!ContainerAllowsBlockScopedVariable(node.Parent!))
        {
            GrammarErrorOnNode(node, Diagnostics.X_0_declarations_can_only_be_declared_inside_a_block, "interface");
        }

        CheckTypeParameters(node.TypeParameters());
        CheckTypeNameIsReserved(node.Name()!, Diagnostics.Interface_name_cannot_be_0);
        CheckExportsOnMergedDeclarations(node);
        var symbol = GetSymbolOfDeclaration(node)!;
        CheckTypeParameterListsIdentical(symbol);

        // Only check this symbol once
        var links = _declaredTypeLinks.Get(symbol);
        if (!links._interfaceChecked)
        {
            links._interfaceChecked = true;
            var t = GetDeclaredTypeOfSymbolWorker(symbol);
            var typeWithThis = GetTypeWithThisArgument(t, null, false);

            // run subsequent checks only if first set succeeded
            if (CheckInheritedPropertiesAreIdentical(t, node.Name()))
            {
                foreach (var baseType in GetBaseTypesWorker(t) ?? [])
                {
                    CheckTypeAssignableTo(typeWithThis, GetTypeWithThisArgument(baseType, t.AsInterfaceType()!._thisType, false), node.Name(), Diagnostics.Interface_0_incorrectly_extends_interface_1);
                }

                CheckIndexConstraints(t, symbol /*isStaticIndex*/, false);
            }
        }

        CheckObjectTypeForDuplicateDeclarations(node, false /*checkPrivateNames*/);
        foreach (var heritageElement in GetExtendsHeritageClauseElements(node))
        {
            if (IsExpressionWithTypeArguments(heritageElement))
            {
                var expr = heritageElement.Expression()!;
                if (!IsEntityNameExpression(expr) || IsOptionalChain(expr))
                {
                    Error(expr, Diagnostics.An_interface_can_only_extend_an_identifier_Slashqualified_name_with_optional_type_arguments);
                }
            }

            CheckTypeReferenceNode(heritageElement);
        }

        CheckSourceElements(node.Members() ?? []);
        CheckClassOrInterfaceForDuplicateIndexSignatures(node);
        RegisterForUnusedIdentifiersCheck(node);
    }

    internal bool CheckInheritedPropertiesAreIdentical(Type t, Node? typeNode)
    {
        var baseTypes = GetBaseTypesWorker(t) ?? [];
        if (baseTypes.Length < 2)
        {
            return true;
        }

        var seen = new Dictionary<string, InheritanceInfo>();
        foreach (var (id, p) in ResolveDeclaredMembers(t)._declaredMembers!)
        {
            if (IsNamedMember(p, id))
            {
                seen[p.Name] = new InheritanceInfo { _prop = p, _containingType = t };
            }
        }

        var identical = true;
        foreach (var baseType in baseTypes)
        {
            var properties = GetPropertiesOfTypeWorker(GetTypeWithThisArgument(baseType, t.AsInterfaceType()!._thisType, false)) ?? [];
            foreach (var prop in properties)
            {
                if (!seen.TryGetValue(prop.Name, out var existing))
                {
                    seen[prop.Name] = new InheritanceInfo { _prop = prop, _containingType = baseType };
                }
                else
                {
                    var isInheritedProperty = existing._containingType != t;
                    if (isInheritedProperty && !IsPropertyIdenticalTo(existing._prop!, prop))
                    {
                        identical = false;
                        var typeName1 = TypeToString(existing._containingType!);
                        var typeName2 = TypeToString(baseType);
                        var errorInfo = NewDiagnosticForNode(typeNode!, Diagnostics.Named_property_0_of_types_1_and_2_are_not_identical, SymbolToStringWorker(prop), typeName1, typeName2);
                        AddDiagnostic(NewDiagnosticChain(errorInfo, Diagnostics.Interface_0_cannot_simultaneously_extend_types_1_and_2, TypeToString(t), typeName1, typeName2));
                    }
                }
            }
        }

        return identical;
    }

    internal bool IsPropertyIdenticalTo(Symbol sourceProp, Symbol targetProp)
    {
        return CompareProperties(sourceProp, targetProp, (s, t) => CompareTypesIdentical(s, t)) != Ternary.False;
    }

    internal void CheckEnumDeclaration(Node node)
    {
        CheckGrammarModifiers(node);
        CheckCollisionsForDeclarationName(node, node.Name());
        CheckExportsOnMergedDeclarations(node);
        CheckSourceElements(node.Members() ?? []);

        if (ShouldCheckErasableSyntax(node) && (node.Flags & NodeFlags.Ambient) == 0)
        {
            Error(node, Diagnostics.This_syntax_is_not_allowed_when_erasableSyntaxOnly_is_enabled);
        }

        ComputeEnumMemberValues(node);

        // Spec 2014 - Section 9.3:
        // It isn't possible for one enum declaration to continue the automatic numbering sequence of another,
        // and when an enum type has multiple declarations, only one declaration is permitted to omit a value
        // for the first member.
        //
        // Only perform this check once per symbol
        var enumSymbol = GetSymbolOfDeclaration(node)!;
        var links = _declaredTypeLinks.Get(enumSymbol);
        if (!links._enumChecked)
        {
            links._enumChecked = true;
            if (enumSymbol.Declarations!.Length > 1)
            {
                var enumIsConst = IsEnumConst(node);

                // check that const is placed\omitted on all enum declarations
                foreach (var decl in enumSymbol.Declarations)
                {
                    if (IsEnumDeclaration(decl) && IsEnumConst(decl) != enumIsConst)
                    {
                        Error(GetNameOfDeclaration(decl), Diagnostics.Enum_declarations_must_all_be_const_or_non_const);
                    }
                }
            }

            var seenEnumMissingInitialInitializer = false;
            foreach (var declaration in enumSymbol.Declarations)
            {
                // return true if we hit a violation of the rule, false otherwise
                if (declaration.Kind != Kind.EnumDeclaration)
                {
                    continue;
                }

                var members = declaration.Members();
                if (members == null || members.Length == 0)
                {
                    continue;
                }

                var firstEnumMember = members[0];
                if (firstEnumMember.Initializer() == null)
                {
                    if (seenEnumMissingInitialInitializer)
                    {
                        Error(firstEnumMember.Name(), Diagnostics.In_an_enum_with_multiple_declarations_only_one_declaration_can_omit_an_initializer_for_its_first_enum_element);
                    }
                    else
                    {
                        seenEnumMissingInitialInitializer = true;
                    }
                }
            }
        }
    }

    internal void CheckEnumMember(Node node)
    {
        if (IsPrivateIdentifier(node.Name()!))
        {
            Error(node, Diagnostics.An_enum_member_cannot_be_named_with_a_private_identifier);
        }

        if (node.Initializer() != null)
        {
            CheckExpression(node.Initializer()!);
        }
    }
}
