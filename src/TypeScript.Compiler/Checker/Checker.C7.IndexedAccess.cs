// ported from: internal/checker/checker.go:27405-27996（索引访问求值 + 只读/赋值检查 + 替换类型（Substitution）+ 基约束）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 未导出 `getBaseConstraintOfType` → 按 artifacts/checker-method-collisions.md 改名 `GetBaseConstraintOfTypeWorker`
//    （定义点；Go 导出 `GetBaseConstraintOfType` 在 exports.go:211，由别片提供）。
// 2) Go 的包级函数 `getIndexNodeForAccessExpression`/`indexTypeLessThan` → 同一 partial class 的 `internal static` 成员。
// 3) Go 的 `switch { case ... }`（每个 case 都 return）→ 顺序 `if` + `return`，保持 Go 的分支顺序。
// 4) Go 把实例方法/包级函数当函数值传（`isTupleType`/`getStringLiteralValue`/`CompareTypes`/`c.isNoInferTargetType`）
//    → 直接传方法组（同类内实例方法组可转委托）。
// 5) `[]RecursionId` → `List<RecursionId>`；Go 的 `append(stack, identity)` → 复制出新 List：
//    Go 里被扩展的切片只在本调用内使用（调用方不复用其长度），复制语义等价。
// 6) Go 的 func 字段 `c.containsMissingType`/`c.isStringIndexSignatureOnlyType` → C# 字段
//    `_containsMissingType`/`_isStringIndexSignatureOnlyType`（Checker.Struct.cs:332/334，initializeClosures 里赋值），
//    照 Go 的字段读法调用（不走 Worker 名）。
// 7) Go 的 map 读 `c.globalThisSymbol.Exports[propName]` → `Exports is { } e && e.TryGetValue(...)`（Go 缺失键返回 nil，C# 索引器会抛）。
// 8) `jsnum.FromString` → `FromString`；`index < 0`/`index < jsnum.Number(limit)` 依赖 `Number` 的隐式转换与比较运算符
//    （JsNum/NumericInterfaces.cs:39 `implicit operator Number(int)`，JsNum/JsNum.cs:101-107）。
// 9) `ast.InternalSymbolNameMissing` → `InternalSymbolNameMissing`（Ast/Symbol.cs:77 的常量）。

namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.JsNum.JsNum;
using static TypeScript.Scanner.Scanner;

public sealed partial class Checker
{
    internal Type? GetPropertyTypeForIndexType(Type originalObjectType, Type objectType, Type indexType, Type fullIndexType, Node? accessNode, AccessFlags accessFlags)
    {
        Node? accessExpression = null;
        if (accessNode != null && IsElementAccessExpression(accessNode))
        {
            accessExpression = accessNode;
        }
        var propName = "";
        var hasPropName = false;
        if (!(accessNode != null && IsPrivateIdentifier(accessNode)))
        {
            propName = GetPropertyNameFromIndex(indexType, accessNode);
            hasPropName = propName != InternalSymbolNameMissing;
        }
        if (hasPropName)
        {
            if ((accessFlags & AccessFlags.Contextual) != 0)
            {
                var t = GetTypeOfPropertyOfContextualTypeWorker(objectType, propName);
                if (t == null)
                {
                    t = _anyType;
                }
                return t;
            }
            var prop = GetPropertyOfTypeWorker(objectType, propName);
            if (prop != null)
            {
                if ((accessFlags & AccessFlags.ReportDeprecated) != 0 && accessNode != null && (prop.Declarations?.Length ?? 0) != 0 && IsDeprecatedSymbol(prop) && IsUncalledFunctionReference(accessNode, prop))
                {
                    Node? deprecatedNode;
                    if (accessExpression != null)
                    {
                        deprecatedNode = accessExpression.AsElementAccessExpression()!.ArgumentExpression;
                    }
                    else if (IsIndexedAccessTypeNode(accessNode))
                    {
                        deprecatedNode = accessNode.AsIndexedAccessTypeNode()!.IndexType;
                    }
                    else
                    {
                        deprecatedNode = accessNode;
                    }
                    AddDeprecatedSuggestion(deprecatedNode!, prop.Declarations, propName);
                }
                if (accessExpression != null)
                {
                    MarkPropertyAsReferenced(prop, accessExpression, IsSelfTypeAccess(accessExpression.Expression()!, objectType._symbol));
                    if (IsAssignmentToReadonlyEntity(accessExpression, prop, GetAssignmentTargetKind(accessExpression)))
                    {
                        Error(accessExpression.AsElementAccessExpression()!.ArgumentExpression, Cannot_assign_to_0_because_it_is_a_read_only_property, SymbolToStringWorker(prop));
                        return null;
                    }
                    if ((accessFlags & AccessFlags.CacheSymbol) != 0)
                    {
                        _symbolNodeLinks.Get(accessNode!)._resolvedSymbol = prop;
                    }
                    if (IsThisPropertyAccessInConstructor(accessExpression, prop))
                    {
                        return _autoType;
                    }
                }
                Type propType;
                if ((accessFlags & AccessFlags.Writing) != 0)
                {
                    propType = GetWriteTypeOfSymbol(prop);
                }
                else
                {
                    propType = GetTypeOfSymbolWorker(prop);
                }
                if (accessExpression != null && GetAssignmentTargetKind(accessExpression) != AssignmentKind.Definite)
                {
                    return GetFlowTypeOfReference(accessExpression, propType);
                }
                if (accessNode != null && IsIndexedAccessTypeNode(accessNode) && _containsMissingType(propType))
                {
                    return GetUnionTypeWorker(new Type[] { propType, _undefinedType });
                }
                return propType;
            }
            if (EveryType(objectType, IsTupleType) && IsNumericLiteralName(propName))
            {
                var index = FromString(propName);
                if (accessNode != null && EveryType(objectType, t => (t.TargetTupleType()!._combinedFlags & ElementFlags.Variable) == 0) && (accessFlags & AccessFlags.AllowMissing) == 0)
                {
                    var indexNode = GetIndexNodeForAccessExpression(accessNode);
                    if (IsTupleType(objectType))
                    {
                        if (index < 0)
                        {
                            Error(indexNode, A_tuple_type_cannot_be_indexed_with_a_negative_value);
                            return _undefinedType;
                        }
                        Error(indexNode, Tuple_type_0_of_length_1_has_no_element_at_index_2, TypeToString(objectType), GetTypeReferenceArity(objectType), propName);
                    }
                    else
                    {
                        Error(indexNode, Property_0_does_not_exist_on_type_1, propName, TypeToString(objectType));
                    }
                }
                if (index >= 0)
                {
                    ErrorIfWritingToReadonlyIndex(GetIndexInfoOfTypeWorker(objectType, _numberType), objectType, accessExpression);
                    return GetTupleElementTypeOutOfStartCount(objectType, index, IfElse<Type?>((accessFlags & AccessFlags.IncludeUndefined) != 0, _missingType, null));
                }
            }
        }
        if ((indexType._flags & TypeFlags.Nullable) == 0 && IsTypeAssignableToKind(indexType, TypeFlags.StringLike | TypeFlags.NumberLike | TypeFlags.ESSymbolLike))
        {
            if ((objectType._flags & (TypeFlags.Any | TypeFlags.Never)) != 0)
            {
                return objectType;
            }
            // If no index signature is applicable, we default to the string index signature. In effect, this means the string
            // index signature applies even when accessing with a symbol-like type.
            var indexInfo = GetApplicableIndexInfo(objectType, indexType);
            if (indexInfo == null)
            {
                indexInfo = GetIndexInfoOfTypeWorker(objectType, _stringType);
            }
            if (indexInfo != null)
            {
                if ((accessFlags & AccessFlags.NoIndexSignatures) != 0 && indexInfo._keyType != _numberType)
                {
                    if (accessExpression != null)
                    {
                        if ((accessFlags & AccessFlags.Writing) != 0)
                        {
                            Error(accessExpression, Type_0_is_generic_and_can_only_be_indexed_for_reading, TypeToString(originalObjectType));
                        }
                        else
                        {
                            Error(accessExpression, Type_0_cannot_be_used_to_index_type_1, TypeToString(indexType), TypeToString(originalObjectType));
                        }
                    }
                    return null;
                }
                if (accessNode != null && indexInfo._keyType == _stringType && !IsTypeAssignableToKind(indexType, TypeFlags.String | TypeFlags.Number))
                {
                    var indexNode = GetIndexNodeForAccessExpression(accessNode);
                    Error(indexNode, Type_0_cannot_be_used_as_an_index_type, TypeToString(indexType));
                    if ((accessFlags & AccessFlags.IncludeUndefined) != 0)
                    {
                        return GetUnionTypeWorker(new Type[] { indexInfo._valueType!, _missingType });
                    }
                    else
                    {
                        return indexInfo._valueType;
                    }
                }
                ErrorIfWritingToReadonlyIndex(indexInfo, objectType, accessExpression);
                // When accessing an enum object with its own type,
                // e.g. E[E.A] for enum E { A }, undefined shouldn't
                // be included in the result type
                if ((accessFlags & AccessFlags.IncludeUndefined) != 0 &&
                    !(objectType._symbol != null &&
                      (objectType._symbol.Flags & (SymbolFlags.RegularEnum | SymbolFlags.ConstEnum)) != 0 &&
                      (indexType._symbol != null &&
                       (indexType._flags & TypeFlags.EnumLiteral) != 0 &&
                       GetParentOfSymbol(indexType._symbol) == objectType._symbol)))
                {
                    return GetUnionTypeWorker(new Type[] { indexInfo._valueType!, _missingType });
                }
                return indexInfo._valueType;
            }
            if ((indexType._flags & TypeFlags.Never) != 0)
            {
                return _neverType;
            }
            if (IsJSLiteralType(objectType))
            {
                return _anyType;
            }
            if (accessExpression != null && !IsConstEnumObjectType(objectType))
            {
                if (IsObjectLiteralType(objectType))
                {
                    if (_noImplicitAny && (indexType._flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
                    {
                        AddDiagnostic(CreateDiagnosticForNode(accessExpression, Property_0_does_not_exist_on_type_1, indexType.AsLiteralType()!._value, TypeToString(objectType)));
                        return _undefinedType;
                    }
                    else if ((indexType._flags & (TypeFlags.Number | TypeFlags.String)) != 0)
                    {
                        var types = Map(objectType.AsStructuredType()!._properties, prop => GetTypeOfSymbolWorker(prop));
                        return GetUnionTypeWorker([.. types, _undefinedType]);
                    }
                }
                if (objectType._symbol == _globalThisSymbol && hasPropName && _globalThisSymbol!.Exports is { } globalThisExports && globalThisExports.TryGetValue(propName, out var globalThisProp) && globalThisProp != null && (globalThisProp.Flags & SymbolFlags.BlockScoped) != 0)
                {
                    Error(accessExpression, Property_0_does_not_exist_on_type_1, propName, TypeToString(objectType));
                }
                else if (_noImplicitAny && (accessFlags & AccessFlags.SuppressNoImplicitAnyError) == 0)
                {
                    if (hasPropName && TypeHasStaticProperty(propName, objectType))
                    {
                        var typeName = TypeToString(objectType);
                        Error(accessExpression, Property_0_does_not_exist_on_type_1_Did_you_mean_to_access_the_static_member_2_instead, propName /* as string */, typeName, typeName + "[" + GetTextOfNode(accessExpression.AsElementAccessExpression()!.ArgumentExpression!) + "]");
                    }
                    else if (GetIndexTypeOfTypeWorker(objectType, _numberType) != null)
                    {
                        Error(accessExpression.AsElementAccessExpression()!.ArgumentExpression, Element_implicitly_has_an_any_type_because_index_expression_is_not_of_type_number);
                    }
                    else
                    {
                        var suggestion = "";
                        if (hasPropName)
                        {
                            suggestion = GetSuggestionForNonexistentProperty(propName, objectType);
                        }
                        if (suggestion != "")
                        {
                            Error(accessExpression.AsElementAccessExpression()!.ArgumentExpression, Property_0_does_not_exist_on_type_1_Did_you_mean_2, propName /* as string */, TypeToString(objectType), suggestion);
                        }
                        else
                        {
                            suggestion = GetSuggestionForNonexistentIndexSignature(objectType, accessExpression, indexType);
                            if (suggestion != "")
                            {
                                Error(accessExpression, Element_implicitly_has_an_any_type_because_type_0_has_no_index_signature_Did_you_mean_to_call_1, TypeToString(objectType), suggestion);
                            }
                            else
                            {
                                Diagnostic? diagnostic = null;
                                if ((indexType._flags & TypeFlags.EnumLiteral) != 0)
                                {
                                    diagnostic = NewDiagnosticForNode(accessExpression, Property_0_does_not_exist_on_type_1, "[" + TypeToString(indexType) + "]", TypeToString(objectType));
                                }
                                else if ((indexType._flags & TypeFlags.UniqueESSymbol) != 0)
                                {
                                    var symbolName = GetFullyQualifiedNameWorker(indexType._symbol!, accessExpression);
                                    diagnostic = NewDiagnosticForNode(accessExpression, Property_0_does_not_exist_on_type_1, "[" + symbolName + "]", TypeToString(objectType));
                                }
                                else if ((indexType._flags & TypeFlags.StringLiteral) != 0)
                                {
                                    diagnostic = NewDiagnosticForNode(accessExpression, Property_0_does_not_exist_on_type_1, indexType.AsLiteralType()!._value, TypeToString(objectType));
                                }
                                else if ((indexType._flags & TypeFlags.NumberLiteral) != 0)
                                {
                                    diagnostic = NewDiagnosticForNode(accessExpression, Property_0_does_not_exist_on_type_1, indexType.AsLiteralType()!._value, TypeToString(objectType));
                                }
                                else if ((indexType._flags & (TypeFlags.Number | TypeFlags.String)) != 0)
                                {
                                    diagnostic = NewDiagnosticForNode(accessExpression, No_index_signature_with_a_parameter_of_type_0_was_found_on_type_1, TypeToString(indexType), TypeToString(objectType));
                                }
                                AddDiagnostic(NewDiagnosticChainForNode(diagnostic, accessExpression, Element_implicitly_has_an_any_type_because_expression_of_type_0_can_t_be_used_to_index_type_1, TypeToString(fullIndexType), TypeToString(objectType)));
                            }
                        }
                    }
                }
                return null;
            }
        }
        if ((accessFlags & AccessFlags.AllowMissing) != 0 && IsObjectLiteralType(objectType))
        {
            return _undefinedType;
        }
        if (IsJSLiteralType(objectType))
        {
            return _anyType;
        }
        if (accessNode != null)
        {
            var indexNode = GetIndexNodeForAccessExpression(accessNode);
            if (indexNode.Kind != Kind.BigIntLiteral && (indexType._flags & (TypeFlags.StringLiteral | TypeFlags.NumberLiteral)) != 0)
            {
                Error(indexNode, Property_0_does_not_exist_on_type_1, indexType.AsLiteralType()!._value, TypeToString(objectType));
            }
            else if ((indexType._flags & (TypeFlags.String | TypeFlags.Number)) != 0)
            {
                Error(indexNode, Type_0_has_no_matching_index_signature_for_type_1, TypeToString(objectType), TypeToString(indexType));
            }
            else
            {
                string typeString;
                if (indexNode.Kind == Kind.BigIntLiteral)
                {
                    typeString = "bigint";
                }
                else
                {
                    typeString = TypeToString(indexType);
                }
                Error(indexNode, Type_0_cannot_be_used_as_an_index_type, typeString);
            }
        }
        if (IsTypeAny(indexType))
        {
            return indexType;
        }
        return null;
    }

    internal bool TypeHasStaticProperty(string propName, Type containingType)
    {
        if (containingType._symbol != null)
        {
            var prop = GetPropertyOfTypeWorker(GetTypeOfSymbolWorker(containingType._symbol), propName);
            return prop != null && prop.ValueDeclaration != null && IsStatic(prop.ValueDeclaration);
        }
        return false;
    }

    internal string GetSuggestionForNonexistentProperty(string name, Type containingType)
    {
        var symbol = GetSpellingSuggestionForName(name, GetPropertiesOfTypeWorker(containingType), SymbolFlags.Value);
        if (symbol != null)
        {
            return symbol.Name;
        }
        return "";
    }

    internal string GetSuggestionForNonexistentIndexSignature(Type objectType, Node expr, Type keyedType)
    {
        // check if object type has setter or getter
        bool HasProp(string name)
        {
            var prop = GetPropertyOfObjectType(objectType, name);
            if (prop != null)
            {
                var s = GetSingleCallSignature(GetTypeOfSymbolWorker(prop));
                return s != null && GetMinArgumentCount(s) >= 1 && IsTypeAssignableToWorker(keyedType, GetTypeAtPositionWorker(s, 0));
            }
            return false;
        }
        var suggestedMethod = IsAssignmentTarget(expr) ? "set" : "get";
        if (!HasProp(suggestedMethod))
        {
            return "";
        }
        var suggestion = TryGetPropertyAccessOrIdentifierToString(expr.Expression()!);
        if (suggestion == "")
        {
            return suggestedMethod;
        }
        return suggestion + "." + suggestedMethod;
    }

    internal Type? GetSuggestedTypeForNonexistentStringLiteralType(Type source, Type target)
    {
        var candidates = FilterSeq(target.Types(), t => (t._flags & TypeFlags.StringLiteral) != 0);
        return GetSpellingSuggestionWithMaxCandidateCount(GetStringLiteralValue(source), candidates, GetStringLiteralValue, CompareTypes, 1000);
    }

    internal static Node GetIndexNodeForAccessExpression(Node accessNode)
    {
        switch (accessNode.Kind)
        {
            case Kind.ElementAccessExpression:
                return accessNode.AsElementAccessExpression()!.ArgumentExpression!;
            case Kind.IndexedAccessType:
                return accessNode.AsIndexedAccessTypeNode()!.IndexType!;
            case Kind.ComputedPropertyName:
                return accessNode.Expression()!;
        }
        return accessNode;
    }

    internal void ErrorIfWritingToReadonlyIndex(IndexInfo? indexInfo, Type objectType, Node? accessExpression)
    {
        if (indexInfo != null && indexInfo._isReadonly && accessExpression != null && (IsAssignmentTarget(accessExpression) || IsDeleteTarget(accessExpression)))
        {
            Error(accessExpression, Index_signature_in_type_0_only_permits_reading, TypeToString(objectType));
        }
    }

    internal bool IsSelfTypeAccess(Node name, Symbol? parent)
    {
        // Go（checker.go:27680）`getResolvedSymbol(ast.GetFirstIdentifier(name))`：GetFirstIdentifier 只会返回 Identifier/QualifiedName
        // 的左端，永不返回 nil（C# 侧异常分支是 DebugFailureException）⇒ `!` 与 Go 不变量一致。
        return name.Kind == Kind.ThisKeyword || parent != null && IsEntityNameExpression(name) && parent == GetResolvedSymbolWorker(GetFirstIdentifier(name)!);
    }

    internal bool IsAssignmentToReadonlyEntity(Node expr, Symbol symbol, AssignmentKind assignmentKind)
    {
        if (assignmentKind == AssignmentKind.None)
        {
            // no assignment means it doesn't matter whether the entity is readonly
            return false;
        }
        if (IsAccessExpression(expr))
        {
            // Go（checker.go:27683 / 27710）对 SkipParentheses(expr.Expression()) 的结果直接 IsIdentifier/getResolvedSymbol，
            // 二者在 Go 里对 nil 会 panic ⇒ 该处必非 nil，`!` 与 Go 不变量一致。
            var node = SkipParentheses(expr.Expression()!)!;
            if (IsIdentifier(node))
            {
                var expressionSymbol = GetResolvedSymbolWorker(node);
                // CommonJS module.exports is never readonly
                if ((expressionSymbol.Flags & SymbolFlags.ModuleExports) != 0)
                {
                    return false;
                }
            }
        }
        if (IsReadonlySymbolWorker(symbol))
        {
            // Allow assignments to readonly properties within constructors of the same class declaration.
            if ((symbol.Flags & SymbolFlags.Property) != 0 && IsAccessExpression(expr) && expr.Expression()!.Kind == Kind.ThisKeyword)
            {
                // Look for if this is the constructor for the class that `symbol` is a property of.
                var ctor = GetControlFlowContainer(expr);
                if (ctor == null || !IsConstructorDeclaration(ctor))
                {
                    return true;
                }
                if (symbol.ValueDeclaration != null)
                {
                    var isAssignmentDeclaration = IsBinaryExpression(symbol.ValueDeclaration);
                    var isLocalPropertyDeclaration = ctor.Parent == symbol.ValueDeclaration.Parent;
                    var isLocalParameterProperty = ctor == symbol.ValueDeclaration.Parent;
                    var isLocalThisPropertyAssignment = isAssignmentDeclaration && symbol.Parent!.ValueDeclaration == ctor.Parent;
                    var isLocalThisPropertyAssignmentConstructorFunction = isAssignmentDeclaration && symbol.Parent!.ValueDeclaration == ctor;
                    var isWriteableSymbol = isLocalPropertyDeclaration || isLocalParameterProperty || isLocalThisPropertyAssignment || isLocalThisPropertyAssignmentConstructorFunction;
                    return !isWriteableSymbol;
                }
            }
            return true;
        }
        if (IsAccessExpression(expr))
        {
            // references through namespace import should be readonly
            // Go（checker.go:27683 / 27710）对 SkipParentheses(expr.Expression()) 的结果直接 IsIdentifier/getResolvedSymbol，
            // 二者在 Go 里对 nil 会 panic ⇒ 该处必非 nil，`!` 与 Go 不变量一致。
            var node = SkipParentheses(expr.Expression()!)!;
            if (IsIdentifier(node))
            {
                var expressionSymbol = GetResolvedSymbolWorker(node);
                if ((expressionSymbol.Flags & SymbolFlags.Alias) != 0)
                {
                    var declaration = GetDeclarationOfAliasSymbol(expressionSymbol);
                    return declaration != null && IsNamespaceImport(declaration);
                }
            }
        }
        return false;
    }

    internal bool IsThisPropertyAccessInConstructor(Node node, Symbol prop)
    {
        Node? constructor = null;
        var (kind, location) = IsConstructorDeclaredThisProperty(prop);
        if (kind == ThisAssignmentDeclarationKind.Constructor)
        {
            constructor = location;
        }
        else if (IsThisProperty(node) && IsAutoTypedProperty(prop))
        {
            constructor = GetDeclaringConstructor(prop);
        }
        return GetThisContainer(node, true /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/) == constructor;
    }

    internal bool IsAutoTypedProperty(Symbol symbol)
    {
        // A property is auto-typed when its declaration has no type annotation or initializer and we're in
        // noImplicitAny mode or a .js file.
        var declaration = symbol.ValueDeclaration;
        return declaration != null && IsPropertyDeclaration(declaration) && declaration.Type() == null && declaration.Initializer() == null && _noImplicitAny;
    }

    internal Node? GetDeclaringConstructor(Symbol symbol)
    {
        foreach (var declaration in symbol.Declarations ?? [])
        {
            var container = GetThisContainer(declaration, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
            if (container != null && IsConstructorDeclaration(container))
            {
                return container;
            }
        }
        return null;
    }

    internal string GetPropertyNameFromIndex(Type indexType, Node? accessNode)
    {
        if (IsTypeUsableAsPropertyName(indexType))
        {
            return GetPropertyNameFromType(indexType);
        }
        if (accessNode != null && IsPropertyName(accessNode))
        {
            return GetPropertyNameForPropertyNameNode(accessNode);
        }
        return InternalSymbolNameMissing;
    }

    internal bool IsStringIndexSignatureOnlyTypeWorker(Type t)
    {
        return (t._flags & TypeFlags.Object) != 0 && !IsGenericMappedType(t) && (GetPropertiesOfTypeWorker(t)?.Length ?? 0) == 0 && (GetIndexInfosOfTypeWorker(t)?.Length ?? 0) == 1 && GetIndexInfoOfTypeWorker(t, _stringType) != null ||
            (t._flags & TypeFlags.UnionOrIntersection) != 0 && Every(t.Types(), _isStringIndexSignatureOnlyType);
    }

    internal bool ShouldDeferIndexedAccessType(Type objectType, Type indexType, Node? accessNode)
    {
        if (IsGenericIndexType(indexType))
        {
            return true;
        }
        if (accessNode != null && !IsIndexedAccessTypeNode(accessNode))
        {
            return IsGenericTupleType(objectType) && !IndexTypeLessThan(indexType, GetTotalFixedElementCount(objectType.TargetTupleType()!));
        }
        return IsGenericObjectType(objectType) && !(IsTupleType(objectType) && IndexTypeLessThan(indexType, GetTotalFixedElementCount(objectType.TargetTupleType()!))) ||
            IsGenericReducibleType(objectType);
    }

    internal static bool IndexTypeLessThan(Type indexType, int limit)
    {
        return EveryType(indexType, t =>
        {
            if ((t._flags & TypeFlags.StringOrNumberLiteral) != 0)
            {
                var propName = GetPropertyNameFromType(t);
                if (IsNumericLiteralName(propName))
                {
                    var index = FromString(propName);
                    return index >= 0 && index < (Number)limit;
                }
            }
            return false;
        });
    }

    internal Type GetNoInferType(Type t)
    {
        if (IsNoInferTargetType(t))
        {
            return GetOrCreateSubstitutionType(t, _unknownType);
        }
        return t;
    }

    internal bool IsNoInferTargetType(Type t)
    {
        // This is effectively a more conservative and predictable form of couldContainTypeVariables. We want to
        // preserve NoInfer<T> only for types that could contain type variables, but we don't want to exhaustively
        // examine all object type members.
        return (t._flags & TypeFlags.UnionOrIntersection) != 0 && Some(t.AsUnionOrIntersectionType()!._types, IsNoInferTargetType) ||
            (t._flags & TypeFlags.Substitution) != 0 && !IsNoInferType(t) && IsNoInferTargetType(t.AsSubstitutionType()!._baseType!) ||
            (t._flags & TypeFlags.Object) != 0 && !IsEmptyAnonymousObjectType(t) ||
            (t._flags & (TypeFlags.Instantiable & ~TypeFlags.Substitution)) != 0 && !IsPatternLiteralType(t);
    }

    internal Type GetSubstitutionType(Type baseType, Type constraint)
    {
        if ((constraint._flags & TypeFlags.AnyOrUnknown) != 0 || constraint == baseType || (baseType._flags & TypeFlags.Any) != 0)
        {
            return baseType;
        }
        return GetOrCreateSubstitutionType(baseType, constraint);
    }

    internal Type GetOrCreateSubstitutionType(Type baseType, Type constraint)
    {
        var key = new SubstitutionTypeKey(baseType._id, constraint._id);
        if (_substitutionTypes.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }
        var result = NewSubstitutionType(baseType, constraint);
        _substitutionTypes[key] = result;
        return result;
    }

    internal Type GetBaseConstraintOrType(Type t)
    {
        var constraint = GetBaseConstraintOfTypeWorker(t);
        if (constraint != null)
        {
            return constraint;
        }
        return t;
    }

    internal Type? GetBaseConstraintOfTypeWorker(Type t)
    {
        if ((t._flags & (TypeFlags.InstantiableNonPrimitive | TypeFlags.UnionOrIntersection | TypeFlags.TemplateLiteral | TypeFlags.StringMapping | TypeFlags.Index)) != 0 || IsGenericTupleType(t))
        {
            var constraint = GetResolvedBaseConstraint(t, null);
            if (constraint != _noConstraintType && constraint != _circularConstraintType)
            {
                return constraint;
            }
            return null;
        }
        return null;
    }

    internal Type GetResolvedBaseConstraint(Type t, List<RecursionId>? stack)
    {
        var constrained = t.AsConstrainedType();
        if (constrained == null)
        {
            return t;
        }
        if (constrained._resolvedBaseConstraint != null)
        {
            return constrained._resolvedBaseConstraint;
        }
        if (!PushTypeResolution(t, TypeSystemPropertyName.ResolvedBaseConstraint))
        {
            return _circularConstraintType;
        }
        Type? constraint = null;
        // We always explore at least 10 levels of nested constraints. Thereafter, we continue to explore
        // up to 50 levels of nested constraints provided there are no "deeply nested" types on the stack
        // (i.e. no types for which five instantiations have been recorded on the stack). If we reach 50
        // levels of nesting, we are presumably exploring a repeating pattern with a long cycle that hasn't
        // yet triggered the deeply nested limiter. We have no test cases that actually get to 50 levels of
        // nesting, so it is effectively just a safety stop.
        var identity = GetRecursionIdentity(t);
        var stackCount = stack?.Count ?? 0;
        if (stackCount < 10 || stackCount < 50 && !stack!.Contains(identity))
        {
            var nextStack = stack == null ? new List<RecursionId> { identity } : new List<RecursionId>(stack) { identity };
            constraint = ComputeBaseConstraint(GetSimplifiedType(t /*writing*/, false), nextStack);
        }
        if (!PopTypeResolution())
        {
            if ((t._flags & TypeFlags.TypeParameter) != 0)
            {
                var errorNode = GetConstraintDeclaration(t);
                if (errorNode != null)
                {
                    var diagnostic = Error(errorNode, Type_parameter_0_has_a_circular_constraint, TypeToString(t));
                    if (_currentNode != null && !IsNodeDescendantOf(errorNode, _currentNode) && !IsNodeDescendantOf(_currentNode, errorNode))
                    {
                        diagnostic.AddRelatedInfo(NewDiagnosticForNode(_currentNode, Circularity_originates_in_type_at_this_location));
                    }
                }
            }
            constraint = _circularConstraintType;
        }
        if (constraint == null)
        {
            constraint = _noConstraintType;
        }
        if (constrained._resolvedBaseConstraint == null)
        {
            constrained._resolvedBaseConstraint = constraint;
        }
        return constraint;
    }

    internal Type? ComputeBaseConstraint(Type t, List<RecursionId> stack)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0)
        {
            var constraint = GetConstraintFromTypeParameter(t);
            if (t.AsTypeParameter()!._isThisType)
            {
                return constraint;
            }
            return GetNextBaseConstraint(constraint, stack);
        }
        if ((t._flags & TypeFlags.UnionOrIntersection) != 0)
        {
            var types = t.Types();
            var constraints = new List<Type>();
            var different = false;
            foreach (var s in types)
            {
                var constraint = GetNextBaseConstraint(s, stack);
                if (constraint != null)
                {
                    if (constraint != s)
                    {
                        different = true;
                    }
                    constraints.Add(constraint);
                }
                else
                {
                    different = true;
                }
            }
            if (!different)
            {
                return t;
            }
            if ((t._flags & TypeFlags.Union) != 0 && constraints.Count == types.Length)
            {
                return GetUnionTypeWorker(constraints.ToArray());
            }
            if ((t._flags & TypeFlags.Intersection) != 0 && constraints.Count != 0)
            {
                return GetIntersectionType(constraints.ToArray());
            }
            return null;
        }
        if ((t._flags & TypeFlags.Index) != 0)
        {
            if (IsGenericMappedType(t.AsIndexType()!._target!))
            {
                var mappedType = t.AsIndexType()!._target!;
                if (GetNameTypeFromMappedType(mappedType) != null && !IsMappedTypeWithKeyofConstraintDeclaration(mappedType))
                {
                    return GetNextBaseConstraint(GetIndexTypeForMappedType(mappedType, IndexFlags.None), stack);
                }
            }
            return _stringNumberSymbolType;
        }
        if ((t._flags & TypeFlags.TemplateLiteral) != 0)
        {
            var types = t.Types();
            var constraints = new List<Type>();
            foreach (var s in types)
            {
                var constraint = GetNextBaseConstraint(s, stack);
                if (constraint != null)
                {
                    constraints.Add(constraint);
                }
            }
            if (constraints.Count == types.Length)
            {
                return GetTemplateLiteralType(t.AsTemplateLiteralType()!._texts!, constraints.ToArray());
            }
            return _stringType;
        }
        if ((t._flags & TypeFlags.StringMapping) != 0)
        {
            var constraint = GetNextBaseConstraint(t.Target(), stack);
            if (constraint != null && constraint != t.Target())
            {
                return GetStringMappingType(t._symbol!, constraint);
            }
            return _stringType;
        }
        if ((t._flags & TypeFlags.IndexedAccess) != 0)
        {
            if (IsMappedTypeGenericIndexedAccess(t))
            {
                // For indexed access types of the form { [P in K]: E }[X], where K is non-generic and X is generic,
                // we substitute an instantiation of E where P is replaced with X.
                return GetNextBaseConstraint(SubstituteIndexedMappedType(t.AsIndexedAccessType()!._objectType!, t.AsIndexedAccessType()!._indexType!), stack);
            }
            var baseObjectType = GetNextBaseConstraint(t.AsIndexedAccessType()!._objectType, stack);
            var baseIndexType = GetNextBaseConstraint(t.AsIndexedAccessType()!._indexType, stack);
            if (baseObjectType == null || baseIndexType == null)
            {
                return null;
            }
            return GetNextBaseConstraint(GetIndexedAccessTypeOrUndefined(baseObjectType, baseIndexType, t.AsIndexedAccessType()!._accessFlags, null, null), stack);
        }
        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            if (_conditionalConstraintDepth >= 100)
            {
                return null;
            }
            _conditionalConstraintDepth++;
            var constraint = GetConstraintFromConditionalType(t);
            _conditionalConstraintDepth--;
            return GetNextBaseConstraint(constraint, stack);
        }
        if ((t._flags & TypeFlags.Substitution) != 0)
        {
            return GetNextBaseConstraint(GetSubstitutionIntersection(t), stack);
        }
        if (IsGenericTupleType(t))
        {
            // We substitute constraints for variadic elements only when the constraints are array types or
            // non-variadic tuple types as we want to avoid further (possibly unbounded) recursion.
            var elementTypes = GetElementTypes(t);
            var elementInfos = t.TargetTupleType()!._elementInfos!;
            var newElements = new List<Type>();
            for (var i = 0; i < elementTypes.Length; i++)
            {
                var v = elementTypes[i];
                var newElement = v;
                if ((v._flags & TypeFlags.TypeParameter) != 0 && (elementInfos[i]._flags & ElementFlags.Variadic) != 0)
                {
                    var constraint = GetNextBaseConstraint(v, stack);
                    if (constraint != null && constraint != v && EveryType(constraint, n => IsArrayOrTupleType(n) && !IsGenericTupleType(n)))
                    {
                        newElement = constraint;
                    }
                }
                newElements.Add(newElement);
            }
            return CreateTupleTypeEx(newElements.ToArray(), elementInfos, t.TargetTupleType()!._readonly);
        }
        return t;
    }
}
