// ported from: internal/checker/checker.go:23158-23749
// （tryGetTypeFromTypeNode … createNormalizedTupleType：AST 类型节点 → 类型的所有入口
//   （关键字/字面量/引用/this/索引访问/type operator/分布式类型参数/JSDoc 内置类型解析））
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) **Worker 改名**（artifacts/checker-method-collisions.md）：Go 未导出的 `getTypeFromTypeNode`（23166）
//    与 exports.go:131 的导出 `GetTypeFromTypeNode` 冲突 → 落成 `GetTypeFromTypeNodeWorker`
//    （Exports.cs:87 的包装已按此调用）；于是 Go 自己的未导出实现 `getTypeFromTypeNodeWorker`（23170）
//    的常规 `Worker` 名被占用 → 落成 **`GetTypeFromTypeNodeWorkerImpl`**（全仓统一裁决：Go 未导出门面 `getX`
//    → `GetXWorker`，Go 自带的 `getXWorker` → `GetXWorkerImpl`，见 artifacts/checker-method-collisions.md；
//    本窗口内除 `GetTypeFromTypeNodeWorker` 外无人调用它）。
// 2) 命名：Go 未导出**字段** → `_camelCase`（`links.resolvedType` → `links._resolvedType`、
//    `d.elementInfos` → `d._elementInfos`、`d.combinedFlags` → `d._combinedFlags`、
//    `d.readonly` → `d._readonly`、`t.flags` → `t._flags`、`tp.distributedType` → `tp._distributedType`）；
//    Go **导出**字段 → 原名 PascalCase（`CheckType`/`ExtendsType`/`TrueType`/`FalseType`/`Operator`/`Right`/
//    `Left`/`Types`/`Literal`/`ObjectType`/`IndexType`/`TypeName`/`ElementType`/`Elements`）；
//    枚举成员名 = Go 常量名去类型前缀（`TypeFlags.Null`、`ElementFlags.NonRequired`/`Variadic`、
//    `ObjectFlags.Anonymous`/`Tuple`/`FromTypeNode`/`None`、`SymbolFlags.Type`/`TypeAlias`/`Class`/`Interface`、
//    `CheckFlags.Unresolved`、`NodeFlags.JSDoc`、`AccessFlags.None`）。
// 3) Go 的**包级函数**（`getNonDistributedTypeParameter`、`isSimpleIdentifierTypeReference`、`getSymbolPath`）
//    → 同一个 `partial class Checker` 的 `internal static` 成员（Parser/Binder/Printer 先例），调用点裸名。
// 4) `Dictionary` 读缺失键（`c.uniqueESSymbolTypes[k]`、`c.unresolvedSymbols[path]`）→ `GetValueOrDefault`
//    （C# 索引器会抛 KeyNotFoundException；Go 的 map 读返回零值 nil）。写仍是 `[k] = v`。
// 5) `panic("Unhandled case in …")` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`
//    （`Debug.Fail` 无 `[DoesNotReturn]`，其后必须补 throw 才满足返回值控制流；禁 `using System.Diagnostics;`）。
// 6) `ast.GetThisContainer(...)` → 全限定 `TypeScript.Ast.Ast.GetThisContainer(...)`：C4.This.cs 已把 Go 的
//    `c.getThisContainer` 落成同类的实例方法 `GetThisContainer`，裸名会绑定到实例方法（同 C4.This.cs 的先例）。
// 7) `TupleNormalizer`（Go checker.go:23750）在本窗口之外、由别的分片落地：Go 的 `n.normalize(c, …)` 显式传
//    接收者参数 → C# `n.Normalize(this, …)`；其字段按未导出字段约定写作 `n._types`/`n._infos`。
// 8) `elementTypes[:len(d.elementInfos)]`（Go 零拷贝改 len 的 slice）→ `elementTypes[..d._elementInfos!.Length]`
//    （C# 数组没有视图，一次拷贝；长度语义与 Go 完全相同）。
// 9) `append(n.types, x)` → `Core.Concatenate(n._types, Core.SingleElementSlice(x))`（C# 数组不可变长）。
// 10) `core.Map(node.TypeArguments(), c.getTypeFromTypeNode)` → `Core.Map(node.TypeArguments(), GetTypeFromTypeNodeWorker)`；
//     C# 的 `Core.Map` 对 null 输入返回空数组（Go 返回 nil slice），下游只做 len/遍历，语义等价。
// 11) unique symbol 名的构造（Go 用 `strings.Builder` + `strconv.FormatUint`）→ 单个字符串拼接表达式
//     `InternalSymbolNamePrefix + "@" + symbol.Name + "@" + GetSymbolId(symbol)`（与 Binder.cs:591 的既有先例一致）。
// 12) `getIntendedTypeFromJSDocTypeReference` 的 Go `switch typeName.Text()`：`case "array"/"promise"` 里的 `if`
//     不成立时 Go 跳出 switch 落到末尾 `return nil` → C# 写成 `break;`（跳出 switch），控制流逐字相同。
// 13) Go 的 `if sym := node.Symbol(); sym == nil || …` → C# 先取局部 `var sym = node.Symbol;` 再判空
//     （C# 的可空流分析不跨字段跟踪，直接照抄会 CS8604）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;
using static TypeScript.Scanner.Scanner;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。命名空间声明体内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Type? TryGetTypeFromTypeNode(Node node)
    {
        var typeNode = node.Type();
        if (typeNode != null)
        {
            return GetTypeFromTypeNodeWorker(typeNode);
        }
        return null;
    }

    internal Type GetTypeFromTypeNodeWorker(Node node)
    {
        return GetConditionalFlowTypeOfType(GetTypeFromTypeNodeWorkerImpl(node), node);
    }

    internal Type GetTypeFromTypeNodeWorkerImpl(Node node)
    {
        switch (node.Kind)
        {
            case Kind.AnyKeyword:
            case Kind.JSDocAllType:
                return _anyType;
            case Kind.JSDocNonNullableType:
                return GetTypeFromTypeNodeWorker(node.Type()!);
            case Kind.JSDocNullableType:
                {
                    var t = GetTypeFromTypeNodeWorker(node.Type()!);
                    if (_strictNullChecks)
                    {
                        return GetNullableType(t, TypeFlags.Null);
                    }
                    return t;
                }
            case Kind.JSDocVariadicType:
                return CreateArrayType(GetTypeFromTypeNodeWorker(node.AsJSDocVariadicType()!.Type!));
            case Kind.JSDocOptionalType:
                return AddOptionality(GetTypeFromTypeNodeWorker(node.Type()!));
            case Kind.UnknownKeyword:
                return _unknownType;
            case Kind.StringKeyword:
                return _stringType;
            case Kind.NumberKeyword:
                return _numberType;
            case Kind.BigIntKeyword:
                return _bigintType;
            case Kind.BooleanKeyword:
                return _booleanType;
            case Kind.SymbolKeyword:
                return _esSymbolType;
            case Kind.VoidKeyword:
                return _voidType;
            case Kind.UndefinedKeyword:
                return _undefinedType;
            case Kind.NullKeyword:
                return _nullType;
            case Kind.NeverKeyword:
                return _neverType;
            case Kind.ObjectKeyword:
                return _nonPrimitiveType;
            case Kind.IntrinsicKeyword:
                return _intrinsicMarkerType;
            case Kind.ThisType:
            case Kind.ThisKeyword:
                return GetTypeFromThisTypeNode(node);
            case Kind.LiteralType:
                return GetTypeFromLiteralTypeNode(node);
            case Kind.TypeReference:
            case Kind.ExpressionWithTypeArguments:
                return GetTypeFromTypeReference(node);
            case Kind.TypePredicate:
                if (node.AsTypePredicateNode()!.AssertsModifier != null)
                {
                    return _voidType;
                }
                return _booleanType;
            case Kind.TypeQuery:
                return GetTypeFromTypeQueryNode(node);
            case Kind.ArrayType:
            case Kind.TupleType:
                return GetTypeFromArrayOrTupleTypeNode(node);
            case Kind.OptionalType:
                return GetTypeFromOptionalTypeNode(node);
            case Kind.UnionType:
                return GetTypeFromUnionTypeNode(node);
            case Kind.IntersectionType:
                return GetTypeFromIntersectionTypeNode(node);
            case Kind.NamedTupleMember:
                return GetTypeFromNamedTupleTypeNode(node);
            case Kind.ParenthesizedType:
                return GetTypeFromTypeNodeWorker(node.Type()!);
            case Kind.RestType:
                return GetTypeFromRestTypeNode(node);
            case Kind.FunctionType:
            case Kind.ConstructorType:
            case Kind.TypeLiteral:
                return GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(node);
            case Kind.TypeOperator:
                return GetTypeFromTypeOperatorNode(node);
            case Kind.IndexedAccessType:
                return GetTypeFromIndexedAccessTypeNode(node);
            case Kind.TemplateLiteralType:
                return GetTypeFromTemplateTypeNode(node);
            case Kind.MappedType:
                return GetTypeFromMappedTypeNode(node);
            case Kind.ConditionalType:
                return GetTypeFromConditionalTypeNode(node);
            case Kind.InferType:
                return GetTypeFromInferTypeNode(node);
            case Kind.ImportType:
                return GetTypeFromImportTypeNode(node);
            default:
                return _errorType;
        }
    }

    internal Type GetTypeFromThisTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            links._resolvedType = GetThisType(node);
        }
        return links._resolvedType;
    }

    internal Type GetThisType(Node node)
    {
        var container = TypeScript.Ast.Ast.GetThisContainer(node, false /*includeArrowFunctions*/, false /*includeClassComputedPropertyName*/);
        if (container != null)
        {
            var parent = container.Parent;
            if (parent != null && (IsClassLike(parent) || IsInterfaceDeclaration(parent)))
            {
                if (!IsStatic(container) && (!IsConstructorDeclaration(container) || IsNodeDescendantOf(node, container.Body())))
                {
                    return Core.Coalesce(GetDeclaredTypeOfClassOrInterface(GetSymbolOfDeclaration(parent)!).AsInterfaceType()!._thisType, _errorType)!;
                }
            }
        }
        Error(node, A_this_type_is_available_only_in_a_non_static_member_of_a_class_or_interface);
        return _errorType;
    }

    internal Type GetTypeFromLiteralTypeNode(Node node)
    {
        if (node.AsLiteralTypeNode()!.Literal!.Kind == Kind.NullKeyword)
        {
            return _nullType;
        }
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            links._resolvedType = GetRegularTypeOfLiteralType(CheckExpression(node.AsLiteralTypeNode()!.Literal!));
        }
        return links._resolvedType;
    }

    internal Type GetTypeFromTypeLiteralOrFunctionOrConstructorTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            // Deferred resolution of members is handled by resolveObjectTypeMembers
            var alias = GetAliasForTypeNode(node);
            var sym = node.Symbol;
            if (sym == null || (GetMembersOfSymbol(sym)?.Count ?? 0) == 0 && alias == null)
            {
                links._resolvedType = _emptyTypeLiteralType;
            }
            else
            {
                var t = NewObjectType(ObjectFlags.Anonymous, node.Symbol);
                t._alias = alias;
                links._resolvedType = t;
            }
        }
        return links._resolvedType;
    }

    internal Type GetTypeFromIndexedAccessTypeNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var objectType = GetTypeFromTypeNodeWorker(node.AsIndexedAccessTypeNode()!.ObjectType!);
            var indexType = GetTypeFromTypeNodeWorker(node.AsIndexedAccessTypeNode()!.IndexType!);
            var potentialAlias = GetAliasForTypeNode(node);
            links._resolvedType = GetIndexedAccessTypeEx(objectType, indexType, AccessFlags.None, node, potentialAlias);
        }
        return links._resolvedType;
    }

    internal Type GetTypeFromTypeOperatorNode(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            var argType = node.Type()!;
            switch (node.AsTypeOperatorNode()!.Operator)
            {
                case Kind.KeyOfKeyword:
                    links._resolvedType = GetIndexType(GetTypeFromTypeNodeWorker(argType));
                    break;
                case Kind.UniqueKeyword:
                    if (argType.Kind == Kind.SymbolKeyword)
                    {
                        links._resolvedType = GetESSymbolLikeTypeForNode(WalkUpParenthesizedTypes(node.Parent)!);
                    }
                    else
                    {
                        links._resolvedType = _errorType;
                    }
                    break;
                case Kind.ReadonlyKeyword:
                    links._resolvedType = GetTypeFromTypeNodeWorker(argType);
                    break;
                default:
                    Debug.Fail("Unhandled case in getTypeFromTypeOperatorNode");
                    throw new System.Diagnostics.UnreachableException();
            }
        }
        return links._resolvedType;
    }

    internal Type GetESSymbolLikeTypeForNode(Node node)
    {
        if (IsValidESSymbolDeclaration(node))
        {
            var symbol = GetSymbolOfNode(node);
            if (symbol != null)
            {
                var uniqueType = _uniqueESSymbolTypes.GetValueOrDefault(symbol);
                if (uniqueType == null)
                {
                    uniqueType = NewUniqueESSymbolType(symbol, InternalSymbolNamePrefix + "@" + symbol.Name + "@" + GetSymbolId(symbol));
                    _uniqueESSymbolTypes[symbol] = uniqueType;
                }
                return uniqueType;
            }
        }
        return _esSymbolType;
    }

    internal Type GetTypeFromTypeReference(Node node)
    {
        var links = _typeNodeLinks.Get(node);
        if (links._resolvedType == null)
        {
            // Cache both the resolved symbol and the resolved type. The resolved symbol is needed when we check the
            // type reference in checkTypeReferenceNode.
            // handle LS queries on the `const` in `x as const` by resolving to the type of `x`
            if (IsConstTypeReference(node) && IsAssertionExpression(node.Parent!))
            {
                links._resolvedType = CheckExpressionCached(node.Parent!.Expression()!);
            }
            else
            {
                var t = GetIntendedTypeFromJSDocTypeReference(node);
                if (t != null)
                {
                    links._resolvedType = t;
                }
                else
                {
                    links._resolvedType = GetDistributedTypeParameter(node, GetTypeReferenceType(node, GetSymbolFromTypeReference(node)));
                }
            }
        }
        return links._resolvedType;
    }

    internal Type GetDistributedTypeParameter(Node node, Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0 && !t.AsTypeParameter()!._isDistributed)
        {
            for (var n = node.Parent; n != null && !IsStatement(n); n = n.Parent)
            {
                if (IsConditionalTypeNode(n))
                {
                    var checkTypeNode = n.AsConditionalTypeNode()!.CheckType!;
                    if (IsSimpleIdentifierTypeReference(checkTypeNode) && GetSymbolFromTypeReference(checkTypeNode) == t._symbol)
                    {
                        // If node is contained in a distributive conditional type for the given type parameter,
                        // return the distributed form of the type parameter.
                        return GetDistributedTypeFromTypeParameter(t);
                    }
                }
            }
        }
        return t;
    }

    internal Type GetDistributedTypeFromTypeParameter(Type t)
    {
        var tp = t.AsTypeParameter()!;
        if (tp._distributedType == null)
        {
            tp._distributedType = NewTypeParameter(t._symbol!);
            tp._distributedType.AsTypeParameter()!._isDistributed = true;
            tp._distributedType.AsTypeParameter()!._constraint = t;
        }
        return tp._distributedType;
    }

    internal static Type GetNonDistributedTypeParameter(Type t)
    {
        if ((t._flags & TypeFlags.TypeParameter) != 0 && t.AsTypeParameter()!._isDistributed)
        {
            return t.AsTypeParameter()!._constraint!;
        }
        return t;
    }

    internal static bool IsSimpleIdentifierTypeReference(Node node)
    {
        return IsTypeReferenceNode(node) && IsIdentifier(node.AsTypeReferenceNode()!.TypeName!) && node.TypeArgumentList() == null;
    }

    internal Type? GetIntendedTypeFromJSDocTypeReference(Node node)
    {
        if ((node.Flags & NodeFlags.JSDoc) != 0 && IsTypeReferenceNode(node))
        {
            var typeName = node.AsTypeReferenceNode()!.TypeName;
            if (IsIdentifier(typeName!))
            {
                var typeArgs = node.TypeArguments();
                switch (typeName!.Text())
                {
                    case "String":
                        CheckNoTypeArguments(node, null);
                        return _stringType;
                    case "Number":
                        CheckNoTypeArguments(node, null);
                        return _numberType;
                    case "BigInt":
                        CheckNoTypeArguments(node, null);
                        return _bigintType;
                    case "Boolean":
                        CheckNoTypeArguments(node, null);
                        return _booleanType;
                    case "Void":
                        CheckNoTypeArguments(node, null);
                        return _voidType;
                    case "Undefined":
                        CheckNoTypeArguments(node, null);
                        return _undefinedType;
                    case "Null":
                        CheckNoTypeArguments(node, null);
                        return _nullType;
                    case "Function":
                    case "function":
                        CheckNoTypeArguments(node, null);
                        return _globalFunctionType;
                    case "array":
                        if ((typeArgs?.Length ?? 0) == 0 && !_noImplicitAny)
                        {
                            return _anyArrayType;
                        }
                        break;
                    case "promise":
                        if ((typeArgs?.Length ?? 0) == 0 && !_noImplicitAny)
                        {
                            return CreatePromiseType(_anyType);
                        }
                        break;
                    case "Object":
                        if ((typeArgs?.Length ?? 0) == 2)
                        {
                            var recordSymbol = _getGlobalRecordSymbol();
                            if (recordSymbol != null)
                            {
                                var indexType = GetTypeFromTypeNodeWorker(typeArgs![0]);
                                if (IsValidIndexKeyType(indexType))
                                {
                                    return GetTypeAliasInstantiation(recordSymbol, new Type[] { indexType, GetTypeFromTypeNodeWorker(typeArgs[1]) }, null);
                                }
                            }
                            return _anyType;
                        }
                        if (!_noImplicitAny)
                        {
                            CheckNoTypeArguments(node, null);
                            return _anyType;
                        }
                        break;
                }
            }
        }
        return null;
    }

    internal Symbol GetSymbolFromTypeReference(Node node)
    {
        var links = _symbolNodeLinks.Get(node);
        if (links._resolvedSymbol == null)
        {
            // The `const` in a `const` assertion resolves to nothing; resolveName knows not to
            // report an error for it, so no special-casing is needed here.
            links._resolvedSymbol = ResolveTypeReferenceName(node, SymbolFlags.Type, false /*ignoreErrors*/);
        }
        return links._resolvedSymbol!;
    }

    internal Symbol ResolveTypeReferenceName(Node typeReference, SymbolFlags meaning, bool ignoreErrors)
    {
        var name = GetTypeReferenceName(typeReference);
        if (name == null)
        {
            return _unknownSymbol!;
        }
        var symbol = ResolveEntityName(name, meaning, ignoreErrors, false /*dontResolveAlias*/, null /*location*/);
        if (symbol != null && symbol != _unknownSymbol)
        {
            return symbol;
        }
        if (ignoreErrors)
        {
            return _unknownSymbol!;
        }
        return GetUnresolvedSymbolForEntityName(name);
    }

    internal Symbol GetUnresolvedSymbolForEntityName(Node name)
    {
        Node identifier;
        switch (name.Kind)
        {
            case Kind.QualifiedName:
                identifier = name.AsQualifiedName()!.Right!;
                break;
            case Kind.PropertyAccessExpression:
                identifier = name.Name()!;
                break;
            default:
                identifier = name;
                break;
        }
        var text = identifier.Text();
        if (text != "")
        {
            Symbol? parentSymbol;
            switch (name.Kind)
            {
                case Kind.QualifiedName:
                    parentSymbol = GetUnresolvedSymbolForEntityName(name.AsQualifiedName()!.Left!);
                    break;
                case Kind.PropertyAccessExpression:
                    parentSymbol = GetUnresolvedSymbolForEntityName(name.Expression()!);
                    break;
                default:
                    parentSymbol = null;
                    break;
            }
            string path;
            if (parentSymbol != null)
            {
                path = GetSymbolPath(parentSymbol) + "." + text;
            }
            else
            {
                path = text;
            }
            var result = _unresolvedSymbols.GetValueOrDefault(path);
            if (result == null)
            {
                result = NewSymbolEx(SymbolFlags.TypeAlias, text, CheckFlags.Unresolved);
                _unresolvedSymbols[path] = result;
                result.Parent = parentSymbol;
                _typeAliasLinks.Get(result)._declaredType = _unresolvedType;
            }
            return result;
        }
        return _unknownSymbol!;
    }

    internal static string GetSymbolPath(Symbol symbol)
    {
        if (symbol.Parent != null)
        {
            return GetSymbolPath(symbol.Parent) + "." + symbol.Name;
        }
        return symbol.Name;
    }

    internal Type GetTypeReferenceType(Node node, Symbol symbol)
    {
        if (symbol == _unknownSymbol)
        {
            return _errorType;
        }
        if ((symbol.Flags & (SymbolFlags.Class | SymbolFlags.Interface)) != 0)
        {
            return GetTypeFromClassOrInterfaceReference(node, symbol);
        }
        if ((symbol.Flags & SymbolFlags.TypeAlias) != 0)
        {
            return GetTypeFromTypeAliasReference(node, symbol);
        }
        // Get type from reference to named type that cannot be generic (enum or type parameter)
        var res = TryGetDeclaredTypeOfSymbol(symbol);
        if (res != null && CheckNoTypeArguments(node, symbol))
        {
            return GetRegularTypeOfLiteralType(res);
        }

        // !!! Resolving values as types for JS
        return _errorType;
    }

    /// <summary>
    /// Get type from type-reference that reference to class or interface
    /// </summary>
    internal Type GetTypeFromClassOrInterfaceReference(Node node, Symbol symbol)
    {
        var t = GetDeclaredTypeOfClassOrInterface(GetMergedSymbolWorker(symbol)!);
        var d = t.AsInterfaceType()!;
        var typeParameters = d.LocalTypeParameters();
        if (typeParameters.Length != 0)
        {
            var numTypeArguments = node.TypeArguments()?.Length ?? 0;
            var minTypeArgumentCount = GetMinTypeArgumentCountWorker(typeParameters);
            var isJs = IsInJSFile(node);
            var isJsImplicitAny = !_noImplicitAny && isJs;
            if (!isJsImplicitAny && (numTypeArguments < minTypeArgumentCount || numTypeArguments > typeParameters.Length))
            {
                Message message;

                var missingAugmentsTag = isJs && IsExpressionWithTypeArguments(node) && !IsJSDocAugmentsTag(node.Parent!);
                if (missingAugmentsTag)
                {
                    message = Expected_0_type_arguments_provide_these_with_an_extends_tag;
                    if (minTypeArgumentCount < typeParameters.Length)
                    {
                        message = Expected_0_1_type_arguments_provide_these_with_an_extends_tag;
                    }
                }
                else
                {
                    message = Generic_type_0_requires_1_type_argument_s;
                    if (minTypeArgumentCount < typeParameters.Length)
                    {
                        message = Generic_type_0_requires_between_1_and_2_type_arguments;
                    }
                }
                var typeStr = TypeToStringEx(t, null /*enclosingDeclaration*/, TypeFormatFlags.WriteArrayAsGenericType, null);
                Error(node, message, typeStr, minTypeArgumentCount, typeParameters.Length);
                if (!isJs)
                {
                    // TODO: Adopt same permissive behavior in TS as in JS to reduce follow-on editing experience failures (requires editing fillMissingTypeArguments)
                    return _errorType;
                }
            }
            if (node.Kind == Kind.TypeReference && IsDeferredTypeReferenceNode(node, numTypeArguments != typeParameters.Length))
            {
                return CreateDeferredTypeReference(t, node, null /*mapper*/, null /*alias*/);
            }
            // In a type reference, the outer type parameters of the referenced class or interface are automatically
            // supplied as type arguments and the type reference only specifies arguments for the local type parameters
            // of the class or interface.
            var localTypeArguments = FillMissingTypeArgumentsWorker(GetTypeArgumentsFromNode(node), typeParameters, minTypeArgumentCount, isJs);
            var typeArguments = Core.Concatenate(d.OuterTypeParameters(), localTypeArguments);
            return CreateTypeReferenceEx(t, typeArguments, ObjectFlags.FromTypeNode);
        }
        if (CheckNoTypeArguments(node, symbol))
        {
            return t;
        }
        return _errorType;
    }

    internal Type[] GetTypeArgumentsFromNode(Node node)
    {
        return Core.Map(node.TypeArguments(), GetTypeFromTypeNodeWorker);
    }

    internal bool CheckNoTypeArguments(Node node, Symbol? symbol)
    {
        if ((node.TypeArguments()?.Length ?? 0) != 0)
        {
            string typeName;
            if (symbol != null)
            {
                typeName = SymbolToStringWorker(symbol);
            }
            else
            {
                typeName = DeclarationNameToString(node.AsTypeReferenceNode()!.TypeName);
            }
            Error(node, Type_0_is_not_generic, typeName);
            return false;
        }
        return true;
    }

    // Return true if the given type reference node is directly aliased or if it needs to be deferred
    // because it is possibly contained in a circular chain of eagerly resolved types.
    internal bool IsDeferredTypeReferenceNode(Node node, bool hasDefaultTypeArguments)
    {
        if (GetAliasSymbolForTypeNode(node) != null)
        {
            return true;
        }
        if (IsResolvedByTypeAlias(node))
        {
            switch (node.Kind)
            {
                case Kind.ArrayType:
                    return MayResolveTypeAlias(node.AsArrayTypeNode()!.ElementType!);
                case Kind.TupleType:
                    return Core.Some(node.Elements(), MayResolveTypeAlias);
                case Kind.TypeReference:
                    return hasDefaultTypeArguments || Core.Some(node.TypeArguments(), MayResolveTypeAlias);
            }
            Debug.Fail("Unhandled case in isDeferredTypeReferenceNode");
            throw new System.Diagnostics.UnreachableException();
        }
        return false;
    }

    // Return true when the given node is transitively contained in type constructs that eagerly
    // resolve their constituent types. We include SyntaxKind.TypeReference because type arguments
    // of type aliases are eagerly resolved.
    internal bool IsResolvedByTypeAlias(Node node)
    {
        var parent = node.Parent!;
        switch (parent.Kind)
        {
            case Kind.ParenthesizedType:
            case Kind.NamedTupleMember:
            case Kind.TypeReference:
            case Kind.UnionType:
            case Kind.IntersectionType:
            case Kind.IndexedAccessType:
            case Kind.ConditionalType:
            case Kind.TypeOperator:
            case Kind.ArrayType:
            case Kind.TupleType:
                return IsResolvedByTypeAlias(parent);
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
                return true;
        }
        return false;
    }

    // Return true if resolving the given node (i.e. getTypeFromTypeNode) possibly causes resolution
    // of a type alias.
    internal bool MayResolveTypeAlias(Node node)
    {
        switch (node.Kind)
        {
            case Kind.TypeReference:
                return (ResolveTypeReferenceName(node, SymbolFlags.Type, false).Flags & SymbolFlags.TypeAlias) != 0;
            case Kind.TypeQuery:
                return true;
            case Kind.TypeOperator:
                return node.AsTypeOperatorNode()!.Operator != Kind.UniqueKeyword && MayResolveTypeAlias(node.Type()!);
            case Kind.ParenthesizedType:
            case Kind.OptionalType:
            case Kind.NamedTupleMember:
                return MayResolveTypeAlias(node.Type()!);
            case Kind.RestType:
                return node.Type()!.Kind != Kind.ArrayType || MayResolveTypeAlias(node.Type()!.AsArrayTypeNode()!.ElementType!);
            case Kind.UnionType:
                return Core.Some(node.AsUnionTypeNode()!.Types!.Nodes, MayResolveTypeAlias);
            case Kind.IntersectionType:
                return Core.Some(node.AsIntersectionTypeNode()!.Types!.Nodes, MayResolveTypeAlias);
            case Kind.IndexedAccessType:
                return MayResolveTypeAlias(node.AsIndexedAccessTypeNode()!.ObjectType!) || MayResolveTypeAlias(node.AsIndexedAccessTypeNode()!.IndexType!);
            case Kind.ConditionalType:
                return MayResolveTypeAlias(node.AsConditionalTypeNode()!.CheckType!) || MayResolveTypeAlias(node.AsConditionalTypeNode()!.ExtendsType!) ||
                    MayResolveTypeAlias(node.AsConditionalTypeNode()!.TrueType!) || MayResolveTypeAlias(node.AsConditionalTypeNode()!.FalseType!);
        }
        return false;
    }

    internal Type CreateNormalizedTypeReference(Type target, Type[] typeArguments)
    {
        if ((target._objectFlags & ObjectFlags.Tuple) != 0)
        {
            return CreateNormalizedTupleType(target, typeArguments);
        }
        return CreateTypeReference(target, typeArguments);
    }

    internal Type CreateNormalizedTupleTypeEx(Type target, Type[] elementTypes, ObjectFlags objectFlags)
    {
        var d = target.AsTupleType()!;
        if ((d._combinedFlags & ElementFlags.NonRequired) == 0)
        {
            // No need to normalize when we only have regular required elements
            return CreateTypeReferenceEx(target, elementTypes, objectFlags);
        }
        if ((d._combinedFlags & ElementFlags.Variadic) != 0)
        {
            for (var i = 0; i < elementTypes.Length; i++)
            {
                var e = elementTypes[i];
                if (i < d._elementInfos!.Length && (d._elementInfos[i]._flags & ElementFlags.Variadic) != 0 && (e._flags & (TypeFlags.Never | TypeFlags.Union)) != 0)
                {
                    // Transform [A, ...(X | Y | Z)] into [A, ...X] | [A, ...Y] | [A, ...Z]
                    var checkTypes = Core.MapIndex(elementTypes, (t, index) =>
                    {
                        if (index < d._elementInfos!.Length && (d._elementInfos[index]._flags & ElementFlags.Variadic) != 0)
                        {
                            return t;
                        }
                        return _unknownType;
                    });
                    if (CheckCrossProductUnion(checkTypes))
                    {
                        return MapType(e, t => CreateNormalizedTupleTypeEx(target, Core.ReplaceElement(elementTypes, i, t), objectFlags));
                    }
                }
            }
        }
        // We have optional, rest, or variadic elements that may need normalizing. Normalization ensures that all variadic
        // elements are generic and that the tuple type has one of the following layouts, disregarding variadic elements:
        // (1) Zero or more required elements, followed by zero or more optional elements, followed by zero or one rest element.
        // (2) Zero or more required elements, followed by a rest element, followed by zero or more required elements.
        // In either layout, zero or more generic variadic elements may be present at any location.
        // Note that the element types may contain an extra 'this' type argument that we want to ignore during normalization
        // and then just append to the normalized element types.
        var n = new TupleNormalizer();
        if (!n.Normalize(this, elementTypes[..d._elementInfos!.Length], d._elementInfos))
        {
            return _errorType;
        }
        if (elementTypes.Length > d._elementInfos.Length)
        {
            n._types = Core.Concatenate(n._types, Core.SingleElementSlice(elementTypes[d._elementInfos.Length]));
        }
        var tupleTarget = GetTupleTargetType(n._infos!, d._readonly);
        if (tupleTarget == _emptyGenericType)
        {
            return _emptyObjectType;
        }
        if (n._types!.Length != 0)
        {
            return CreateTypeReferenceEx(tupleTarget, n._types, objectFlags);
        }
        return tupleTarget;
    }

    internal Type CreateNormalizedTupleType(Type target, Type[] elementTypes)
    {
        return CreateNormalizedTupleTypeEx(target, elementTypes, ObjectFlags.None);
    }
}
