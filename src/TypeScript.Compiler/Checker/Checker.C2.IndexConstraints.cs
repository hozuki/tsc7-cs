// ported from: internal/checker/checker.go:4875-5079（checkIndexConstraints、checkIndexConstraintForProperty、
//   checkIndexConstraintForIndexSignature、checkClassOrInterfaceForDuplicateIndexSignatures、checkTypeForDuplicateIndexSignatures、
//   checkPropertyInitialization、isPropertyWithoutInitializer、isPropertyInitializedInStaticBlocks、isPropertyInitializedInConstructor）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) 未导出同名方法加 `Worker`（artifacts/checker-method-collisions.md）：`getIndexInfosOfType`/`getIndexInfoOfType`/
//    `getIndexTypeOfType`/`getNonMissingTypeOfSymbol`/`isTypeAssignableTo`。
// 2) `indexSignatureMap map[*Type][]*ast.Node` + `append` → `Dictionary<Type, List<Node>>`（C# 数组没有 len<cap 视图）；
//    读缺失键用 `TryGetValue`（Go 返回零值）。
// 3) `Node.ModifierFlags()` 在 C# 是**属性** `ModifierFlags`（`Ast/Node.cs:732`，名字与类型同名时用属性，避免遮蔽类型名）。
// 4) `c.factory`（Go 未导出字段）→ `_factory`（docs/checker-plan §5.5）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal void CheckIndexConstraints(Type t, Symbol symbol, bool isStaticIndex)
    {
        var indexInfos = GetIndexInfosOfTypeWorker(t) ?? [];
        if (indexInfos.Length == 0)
        {
            return;
        }

        foreach (var prop in GetPropertiesOfObjectType(t) ?? [])
        {
            if (!(isStaticIndex && (prop.Flags & SymbolFlags.Prototype) != 0))
            {
                CheckIndexConstraintForProperty(t, prop, GetLiteralTypeFromProperty(prop, TypeFlags.StringOrNumberLiteralOrUnique, true /*includeNonPublic*/), GetNonMissingTypeOfSymbolWorker(prop));
            }
        }

        var typeDeclaration = symbol.ValueDeclaration;
        if (typeDeclaration != null && IsClassLike(typeDeclaration))
        {
            foreach (var member in typeDeclaration.Members() ?? [])
            {
                // Only process instance properties against instance index signatures and static properties against static index signatures
                if (IsStatic(member) == isStaticIndex && !HasBindableName(member))
                {
                    var memberSymbol = GetSymbolOfDeclaration(member)!;
                    CheckIndexConstraintForProperty(t, memberSymbol, GetTypeOfExpression(member.Name()!.Expression()!), GetNonMissingTypeOfSymbolWorker(memberSymbol));
                }
            }
        }

        if (indexInfos.Length > 1)
        {
            foreach (var info in indexInfos)
            {
                CheckIndexConstraintForIndexSignature(t, info);
            }
        }
    }

    internal void CheckIndexConstraintForProperty(Type t, Symbol prop, Type propNameType, Type propType)
    {
        var declaration = prop.ValueDeclaration;
        var name = GetNameOfDeclaration(declaration);
        if (name != null && IsPrivateIdentifier(name))
        {
            return;
        }

        var indexInfos = GetApplicableIndexInfos(t, propNameType);
        if (indexInfos.Length == 0)
        {
            return;
        }

        Node? interfaceDeclaration = null;
        if ((t._objectFlags & ObjectFlags.Interface) != 0)
        {
            interfaceDeclaration = GetDeclarationOfKind(t._symbol, Kind.InterfaceDeclaration);
        }

        Node? propDeclaration = null;
        if ((declaration != null && IsBinaryExpression(declaration)) || (name != null && IsComputedPropertyName(name)))
        {
            propDeclaration = declaration;
        }

        Node? localPropDeclaration = null;
        if (GetParentOfSymbol(prop) == t._symbol)
        {
            localPropDeclaration = declaration;
        }

        foreach (var info in indexInfos)
        {
            Node? localIndexDeclaration = null;
            if (info._declaration != null && GetParentOfSymbol(GetSymbolOfDeclaration(info._declaration)!) == t._symbol)
            {
                localIndexDeclaration = info._declaration;
            }

            // We check only when (a) the property is declared in the containing type, or (b) the applicable index signature is declared
            // in the containing type, or (c) the containing type is an interface and no base interface contains both the property and
            // the index signature (i.e. property and index signature are declared in separate inherited interfaces).
            var errorNode = OrElse(localPropDeclaration, localIndexDeclaration);
            if (errorNode == null && interfaceDeclaration != null && !Some(GetBaseTypesWorker(t), baseType => GetPropertyOfObjectType(baseType, prop.Name) != null && GetIndexTypeOfTypeWorker(baseType, info._keyType!) != null))
            {
                errorNode = interfaceDeclaration;
            }

            if (errorNode != null && !IsTypeAssignableToWorker(propType, info._valueType!))
            {
                var diagnostic = NewDiagnosticForNode(errorNode, Diagnostics.Property_0_of_type_1_is_not_assignable_to_2_index_type_3, SymbolToStringWorker(prop), TypeToString(propType), TypeToString(info._keyType!), TypeToString(info._valueType!));
                if (propDeclaration != null && errorNode != propDeclaration)
                {
                    diagnostic.AddRelatedInfo(NewDiagnosticForNode(propDeclaration, Diagnostics.X_0_is_declared_here, SymbolToStringWorker(prop)));
                }

                AddDiagnostic(diagnostic);
            }
        }
    }

    internal void CheckIndexConstraintForIndexSignature(Type t, IndexInfo checkInfo)
    {
        var declaration = checkInfo._declaration;
        var indexInfos = GetApplicableIndexInfos(t, checkInfo._keyType!);
        if (indexInfos.Length == 0)
        {
            return;
        }

        Node? interfaceDeclaration = null;
        if ((t._objectFlags & ObjectFlags.Interface) != 0)
        {
            interfaceDeclaration = GetDeclarationOfKind(t._symbol, Kind.InterfaceDeclaration);
        }

        Node? localCheckDeclaration = null;
        if (declaration != null && GetParentOfSymbol(GetSymbolOfDeclaration(declaration)!) == t._symbol)
        {
            localCheckDeclaration = declaration;
        }

        foreach (var info in indexInfos)
        {
            if (info == checkInfo)
            {
                continue;
            }

            Node? localIndexDeclaration = null;
            if (info._declaration != null && GetParentOfSymbol(GetSymbolOfDeclaration(info._declaration)!) == t._symbol)
            {
                localIndexDeclaration = info._declaration;
            }

            // We check only when (a) the check index signature is declared in the containing type, or (b) the applicable index
            // signature is declared in the containing type, or (c) the containing type is an interface and no base interface contains
            // both index signatures (i.e. the index signatures are declared in separate inherited interfaces).
            var errorNode = OrElse(localCheckDeclaration, localIndexDeclaration);
            if (errorNode == null && interfaceDeclaration != null && !Some(GetBaseTypesWorker(t), baseType => GetIndexInfoOfTypeWorker(baseType, checkInfo._keyType!) != null && GetIndexTypeOfTypeWorker(baseType, info._keyType!) != null))
            {
                errorNode = interfaceDeclaration;
            }

            if (errorNode != null && !IsTypeAssignableToWorker(checkInfo._valueType!, info._valueType!))
            {
                Error(errorNode, Diagnostics.X_0_index_type_1_is_not_assignable_to_2_index_type_3, TypeToString(checkInfo._keyType!), TypeToString(checkInfo._valueType!), TypeToString(info._keyType!), TypeToString(info._valueType!));
            }
        }
    }

    internal void CheckClassOrInterfaceForDuplicateIndexSignatures(Node node)
    {
        // Only check the type once
        var links = _declaredTypeLinks.Get(GetSymbolOfDeclaration(node)!);
        if (!links._indexSignaturesChecked)
        {
            links._indexSignaturesChecked = true;
            CheckTypeForDuplicateIndexSignatures(node);
        }
    }

    internal void CheckTypeForDuplicateIndexSignatures(Node node)
    {
        // TypeScript 1.0 spec (April 2014)
        // 3.7.4: An object type can contain at most one string index signature and one numeric index signature.
        // 8.5: A class declaration can have at most one string index member declaration and one numeric index member declaration
        var indexSymbol = GetIndexSymbol(GetSymbolOfDeclaration(node)!);
        // Go: `len(indexSymbol.Declarations) <= 1`（nil 切片长度为 0）
        var indexSymbolDeclarations = indexSymbol?.Declarations ?? [];
        if (indexSymbolDeclarations.Length <= 1)
        {
            return;
        }

        var indexSignatureMap = new Dictionary<Type, List<Node>>();
        foreach (var declaration in indexSymbolDeclarations)
        {
            if (IsIndexSignatureDeclaration(declaration))
            {
                var parameters = declaration.Parameters() ?? [];
                if (parameters.Length == 1 && parameters[0].Type() != null)
                {
                    foreach (var t in GetTypeFromTypeNodeWorker(parameters[0].Type()!).Distributed())
                    {
                        if (!indexSignatureMap.TryGetValue(t, out var list))
                        {
                            list = new List<Node>();
                            indexSignatureMap[t] = list;
                        }

                        list.Add(declaration);
                    }
                }
            }

            // Do nothing for late-bound index signatures: allow these to duplicate one another and explicit indexes
        }

        foreach (var (t, declarations) in indexSignatureMap)
        {
            if (declarations.Count > 1)
            {
                foreach (var declaration in declarations)
                {
                    Error(declaration, Diagnostics.Duplicate_index_signature_for_type_0, TypeToString(t));
                }
            }
        }
    }

    internal void CheckPropertyInitialization(Node node)
    {
        if (!_strictNullChecks || !_strictPropertyInitialization || (node.Flags & NodeFlags.Ambient) != 0)
        {
            return;
        }

        var constructor = FindConstructorDeclaration(node);
        foreach (var member in node.Members() ?? [])
        {
            if ((member.ModifierFlags & ModifierFlags.Ambient) != 0)
            {
                continue;
            }

            if (!IsStatic(member) && IsPropertyWithoutInitializer(member))
            {
                var propName = member.Name();
                if (IsIdentifier(propName!) || IsPrivateIdentifier(propName!) || IsComputedPropertyName(propName!))
                {
                    var t = GetTypeOfSymbolWorker(GetSymbolOfDeclaration(member)!);
                    if (!((t._flags & TypeFlags.AnyOrUnknown) != 0 || ContainsUndefinedType(t)))
                    {
                        if (constructor == null || !IsPropertyInitializedInConstructor(propName!, t, constructor))
                        {
                            Error(member.Name(), Diagnostics.Property_0_has_no_initializer_and_is_not_definitely_assigned_in_the_constructor, DeclarationNameToString(propName));
                        }
                    }
                }
            }
        }
    }

    internal bool IsPropertyWithoutInitializer(Node node)
    {
        return IsPropertyDeclaration(node) && !HasAbstractModifier(node) && !IsExclamationToken(node.PostfixToken()) && node.Initializer() == null;
    }

    internal bool IsPropertyInitializedInStaticBlocks(Node propName, Type propType, Node[] staticBlocks, int startPos, int endPos)
    {
        foreach (var staticBlock in staticBlocks)
        {
            // static block must be within the provided range as they are evaluated in document order (unlike constructors)
            if (staticBlock.Pos() >= startPos && staticBlock.Pos() <= endPos)
            {
                var reference = _factory.NewPropertyAccessExpression(_factory.NewKeywordExpression(Kind.ThisKeyword), null, propName, NodeFlags.None);
                reference.Expression()!.Parent = reference;
                reference.Parent = staticBlock;
                reference.FlowNodeData()!.FlowNode = staticBlock.AsClassStaticBlockDeclaration()!.ReturnFlowNode;
                var flowType = GetFlowTypeOfReferenceEx(reference, propType, GetOptionalType(propType, false), null, null);
                if (!ContainsUndefinedType(flowType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal bool IsPropertyInitializedInConstructor(Node propName, Type propType, Node constructor)
    {
        Node reference;
        if (IsComputedPropertyName(propName))
        {
            reference = _factory.NewElementAccessExpression(_factory.NewKeywordExpression(Kind.ThisKeyword), null, propName.Expression(), NodeFlags.None);
        }
        else
        {
            reference = _factory.NewPropertyAccessExpression(_factory.NewKeywordExpression(Kind.ThisKeyword), null, propName, NodeFlags.None);
        }

        reference.Expression()!.Parent = reference;
        reference.Parent = constructor;
        reference.FlowNodeData()!.FlowNode = constructor.AsConstructorDeclaration()!.ReturnFlowNode;
        var flowType = GetFlowTypeOfReferenceEx(reference, propType, GetOptionalType(propType, false), null, null);
        return !ContainsUndefinedType(flowType);
    }
}
