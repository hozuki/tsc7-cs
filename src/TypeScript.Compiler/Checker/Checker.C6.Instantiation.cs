// ported from: internal/checker/checker.go:22459-23157
// （instantiateType … instantiateList：类型实例化入口、活动 mapper 栈与缓存、类型变量包含性判定、
//   匿名/映射/条件/反转映射类型的实例化、映射类型各分量访问器、alias/list 实例化辅助）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (c *Checker) x()` → 实例方法（PascalCase / 未导出为 internal）；Go 的**包级函数**
//    （`getModifiedReadonlyState`）→ 同一个 `partial class Checker` 的 `internal static` 成员
//    （Parser/Binder/Printer 先例，docs/checker-plan.md §5.2）。
// 2) 命名：Go 未导出**字段** → `_camelCase`（`t.flags`→`t._flags`、`t.alias`→`t._alias`、`t.objectFlags`→
//    `t._objectFlags`、`m.typeParameter`→`m._typeParameter`、`m.declaration`→`m._declaration`、
//    `d.instantiations`→`d._instantiations`、`links.outerTypeParameters`→`links._outerTypeParameters`、
//    `root.outerTypeParameters`→`root._outerTypeParameters`）；Go 未导出**方法** → `internal` + PascalCase；
//    枚举成员名 = Go 常量名去类型前缀（`ObjectFlagsInstantiated`→`ObjectFlags.Instantiated`、
//    `MappedTypeModifiersIncludeOptional`→`MappedTypeModifiers.IncludeOptional`）。
// 3) `x.Symbol()`/`x.TypeArguments()` 这类 Go 的 **nil 接收者安全方法**（types.go:670/677 的 `if a == nil`）
//    在 C# 里是普通实例方法（Types.Type.cs 文件头第 6 条：不能在 null 上分派）→ 调用点写 `x?._symbol`。
// 4) Go 的 **func 字段** `c.couldContainTypeVariables`（checker.go:887）→ `_couldContainTypeVariables`（委托字段，
//    C1 在 Checker.Struct.cs:333 落地）；`c.tracer` → `_tracer`。
// 5) `caching`：`cache[key]` / `data.instantiations[key]` / `root.instantiations[key]` 的读缺失键 → `TryGetValue`
//    （Go 的 map 读返回零值 nil，C# 索引器会抛）。
// 6) `var b keyBuilder` → `new KeyBuilder()`（C# 无"结构体零值"语义；`KeyBuilder` 由 C5 片落地，见报告）。
// 7) `c.activeTypeMappersCaches` 的 len/cap 复用技巧：C# `List<T>` 不暴露 len/cap 双视图 →
//    `PushActiveMapper` 里 Go 的 `cap > lastIndex` 分支与 else 分支合并成一句 `Add(new())`（那个槽里的 map
//    已被 `PopActiveMapper` 清空，追加空字典行为等价）；`PopActiveMapper` 用 `Clear()` + `RemoveAt` 复刻
//    Go 的 `clear(...)` + `[:lastIndex]`；`activeMappers[len-1] = nil` 由 `List.RemoveAt` 一并完成（清槽 + 缩短）。
// 8) `core.FindLastIndex(c.activeMappers, ...)` → `List<TypeMapper>.FindLastIndex`（两者都是"最后一个匹配项的
//    下标，没有则 -1"）。
// 9) Go 的 `map[string]any{"typeId": ...}` → `new Dictionary<string, object?>{ ... }`；`tracing.PhaseCheckTypes`
//    → `Phase.CheckTypes`（`using TypeScript.Tracing;`）。
// 10) Go 里闭包参数遮蔽外层同名参数（`containsReference` 的 `node`、`getConditionalTypeInstantiation` 里 lambda 的
//     `t`）→ C# 禁止 shadow，分别改名 `node2`/`t2`。
// 11) `instantiateList[T comparable]` → `InstantiateList<T>`：Go 的 `comparable` 约束在 C# 没有对应写法（按简报
//     不加 `where T : notnull`）。Go 的 `mapped != value` 用 `EqualityComparer<T>.Default.Equals` 复刻——四个调用点
//     （Type/Symbol/Signature/IndexInfo）全是引用类型，语义都是引用相等。`values == null`（Go 的 nil slice）与空
//     slice 一样都当空处理，直接返回空数组（Go 返回 nil slice；调用点只做长度/`Core.Same` 判断，等价）。
// 12) Go 的递归闭包 `var instantiateConstituent func(*Type) *Type` → C# 局部函数 `InstantiateConstituent`。
// 13) `InstantiateType`/`InstantiateTypeWithAlias` 的首参声明为**非空** `Type`：已落地的调用点
//     （Mapper.cs:199/276 要求非空返回值、Checker.C6.InstantiatedSymbols.cs:82 对可空实参加 `!`）都按非空使用；
//     因此 Go 的 `t == nil` 分支删除，`m == nil` 分支保留（m 仍可空）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.Tracing;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。命名空间声明体内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

public sealed partial class Checker
{
    // Go: checker.go:22459
    internal Type InstantiateType(Type t, TypeMapper? m)
    {
        return InstantiateTypeWithAlias(t, m, null /*alias*/);
    }

    // Go: checker.go:22463
    internal Type InstantiateTypeWithAlias(Type t, TypeMapper? m, TypeAlias? alias)
    {
        // Check for type variables in the alias, so things like `type Brand<T> = number & {}` can potentially be copied with new alias type args, despite them being unreferenced.
        // This is the behavior most people using aliases expect, and prevents the cache from leaking type parameters outside their scope of validity.
        // tests/cases/compiler/declarationEmitArrowFunctionNoRenaming.ts contains an example of this, which previously only worked in strada via some input node reuse logic instead.
        if (m == null || !(_couldContainTypeVariables(t) || (t._alias != null && (t._alias._typeArguments?.Length ?? 0) > 0 && Core.Some(t._alias._typeArguments, _couldContainTypeVariables))))
        {
            return t;
        }
        if (_instantiationDepth == 100 || _instantiationCount >= 5_000_000)
        {
            // We have reached 100 recursive type instantiations, or 5M type instantiations caused by the same statement
            // or expression. There is a very high likelihood we're dealing with a combination of infinite generic types
            // that perpetually generate new type identities, so we stop the recursion here by yielding the error type.
            if (_tracer is { } tr)
            {
                tr.Instant(Phase.CheckTypes, "instantiateType_DepthLimit", new Dictionary<string, object?> { ["typeId"] = t._id, ["instantiationDepth"] = _instantiationDepth, ["instantiationCount"] = _instantiationCount });
            }
            Error(_currentNode, Type_instantiation_is_excessively_deep_and_possibly_infinite);
            return _errorType;
        }
        var index = FindActiveMapper(m);
        if (index == -1)
        {
            PushActiveMapper(m);
        }
        var b = new KeyBuilder();
        b.WriteType(t);
        b.WriteAlias(alias);
        var key = b.Hash();
        var cache = _activeTypeMappersCaches[IfElse(index != -1, index, _activeTypeMappersCaches.Count - 1)];
        if (cache.TryGetValue(key, out var cachedType))
        {
            return cachedType;
        }
        TotalInstantiationCount++;
        _instantiationCount++;
        _instantiationDepth++;
        var result = InstantiateTypeWorker(t, m, alias);
        if (index == -1)
        {
            PopActiveMapper();
        }
        else
        {
            cache[key] = result;
        }
        _instantiationDepth--;
        return result;
    }

    // Go: checker.go:22505
    internal void PushActiveMapper(TypeMapper mapper)
    {
        _activeMappers.Add(mapper);

        // Go: if cap(c.activeTypeMappersCaches) > lastIndex { 复用 cap 里的空 map } else { append 新 map }。
        // C# 的 List<T> 没有 len/cap 双视图，两个分支等价于"追加一个空字典"（见文件头第 7 条）。
        _activeTypeMappersCaches.Add(new());
    }

    // Go: checker.go:22520
    internal void PopActiveMapper()
    {
        // Go: c.activeMappers[len(c.activeMappers)-1] = nil; c.activeMappers = c.activeMappers[:len(c.activeMappers)-1]
        // C# 的 RemoveAt 同时完成"清槽 + 缩短"。
        _activeMappers.RemoveAt(_activeMappers.Count - 1);

        // Clear the map, but leave it in the list for later reuse.
        var lastIndex = _activeTypeMappersCaches.Count - 1;
        _activeTypeMappersCaches[lastIndex].Clear();
        _activeTypeMappersCaches.RemoveAt(lastIndex);
    }

    // Go: checker.go:22530
    internal int FindActiveMapper(TypeMapper mapper)
    {
        // Go: core.FindLastIndex(c.activeMappers, func(m *TypeMapper) bool { return m == mapper })
        return _activeMappers.FindLastIndex(m => m == mapper);
    }

    // Go: checker.go:22534
    internal void ClearActiveMapperCaches()
    {
        foreach (var cache in _activeTypeMappersCaches)
        {
            cache.Clear();
        }
    }

    /// <summary>
    /// Return true if the given type could possibly reference a type parameter for which
    /// we perform type inference (i.e. a type parameter of a generic function). We cache
    /// results for union and intersection types for performance reasons.
    /// </summary>
    // Go: checker.go:22543
    internal bool CouldContainTypeVariablesWorker(Type t)
    {
        if ((t._flags & TypeFlags.StructuredOrInstantiable) == 0)
        {
            return false;
        }
        var objectFlags = t._objectFlags;
        if ((objectFlags & ObjectFlags.CouldContainTypeVariablesComputed) != 0)
        {
            return (objectFlags & ObjectFlags.CouldContainTypeVariables) != 0;
        }
        var result = (t._flags & TypeFlags.Instantiable) != 0 ||
            (t._flags & TypeFlags.Object) != 0 && !IsNonGenericTopLevelType(t) && ((objectFlags & ObjectFlags.Reference) != 0 && (t.AsTypeReference()!._node != null || Core.Some(GetTypeArgumentsWorker(t), _couldContainTypeVariables)) ||
                (objectFlags & ObjectFlags.Anonymous) != 0 && t._symbol != null && (t._symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method | SymbolFlags.Class | SymbolFlags.TypeLiteral | SymbolFlags.ObjectLiteral)) != 0 && t._symbol.Declarations != null ||
                (objectFlags & (ObjectFlags.Mapped | ObjectFlags.ReverseMapped | ObjectFlags.ObjectRestType | ObjectFlags.InstantiationExpressionType)) != 0) ||
            (t._flags & TypeFlags.UnionOrIntersection) != 0 && (t._flags & TypeFlags.EnumLiteral) == 0 && !IsNonGenericTopLevelType(t) && Core.Some(t.Types(), _couldContainTypeVariables);
        t._objectFlags |= ObjectFlags.CouldContainTypeVariablesComputed | IfElse(result, ObjectFlags.CouldContainTypeVariables, ObjectFlags.None);
        return result;
    }

    // Go: checker.go:22560
    internal bool IsNonGenericTopLevelType(Type t)
    {
        if (t._alias != null && (t._alias._typeArguments?.Length ?? 0) == 0)
        {
            var declaration = GetDeclarationOfKind(t._alias._symbol, Kind.TypeAliasDeclaration);
            if (declaration == null)
            {
                declaration = GetDeclarationOfKind(t._alias._symbol, Kind.JSTypeAliasDeclaration);
            }
            return declaration != null && FindAncestorOrQuit(declaration.Parent, n =>
            {
                switch (n.Kind)
                {
                    case Kind.SourceFile:
                        return FindAncestorResult.True;
                    case Kind.ModuleDeclaration:
                        return FindAncestorResult.False;
                }
                return FindAncestorResult.Quit;
            }) != null;
        }
        return false;
    }

    // Go: checker.go:22579
    internal Type InstantiateTypeWorker(Type t, TypeMapper m, TypeAlias? alias)
    {
        var flags = t._flags;
        if ((flags & TypeFlags.TypeParameter) != 0)
        {
            return GetMappedType(t, m);
        }
        if ((flags & TypeFlags.Object) != 0)
        {
            var objectFlags = t._objectFlags;
            if ((objectFlags & (ObjectFlags.Reference | ObjectFlags.Anonymous | ObjectFlags.Mapped)) != 0)
            {
                if ((objectFlags & ObjectFlags.Reference) != 0 && t.AsTypeReference()!._node == null)
                {
                    var resolvedTypeArguments = t.AsTypeReference()!._resolvedTypeArguments;
                    var newTypeArguments = InstantiateTypes(resolvedTypeArguments, m);
                    if (Core.Same(newTypeArguments, resolvedTypeArguments))
                    {
                        return t;
                    }
                    return CreateNormalizedTypeReference(t.Target()!, newTypeArguments);
                }
                if ((objectFlags & ObjectFlags.ReverseMapped) != 0)
                {
                    return InstantiateReverseMappedType(t, m);
                }
                return GetObjectTypeInstantiation(t, m, alias);
            }
            return t;
        }
        if ((flags & TypeFlags.UnionOrIntersection) != 0)
        {
            var source = t;
            if ((t._flags & TypeFlags.Union) != 0)
            {
                var origin = t.AsUnionType()!._origin;
                if (origin != null && (origin._flags & TypeFlags.UnionOrIntersection) != 0)
                {
                    source = origin;
                }
            }
            var types = source.Types();
            var newTypes = InstantiateTypes(types, m);
            if (Core.Same(newTypes, types) && alias?._symbol == t._alias?._symbol)
            {
                return t;
            }
            if (alias == null)
            {
                alias = InstantiateTypeAlias(t._alias, m);
            }
            if ((source._flags & TypeFlags.Intersection) != 0)
            {
                return GetIntersectionTypeEx(newTypes, IntersectionFlags.None, alias);
            }
            return GetUnionTypeExWorker(newTypes, UnionReduction.Literal, alias, null /*origin*/);
        }
        if ((flags & TypeFlags.Index) != 0)
        {
            return GetIndexType(InstantiateType(t.Target()!, m));
        }
        if ((flags & TypeFlags.IndexedAccess) != 0)
        {
            if (alias == null)
            {
                alias = InstantiateTypeAlias(t._alias, m);
            }
            var d = t.AsIndexedAccessType()!;
            return GetIndexedAccessTypeEx(InstantiateType(d._objectType!, m), InstantiateType(d._indexType!, m), d._accessFlags, null /*accessNode*/, alias);
        }
        if ((flags & TypeFlags.TemplateLiteral) != 0)
        {
            return GetTemplateLiteralType(t.AsTemplateLiteralType()!._texts!, InstantiateTypes(t.AsTemplateLiteralType()!._types!, m));
        }
        if ((flags & TypeFlags.StringMapping) != 0)
        {
            return GetStringMappingType(t._symbol!, InstantiateType(t.AsStringMappingType()!._target!, m));
        }
        if ((flags & TypeFlags.Conditional) != 0)
        {
            return GetConditionalTypeInstantiation(t, CombineTypeMappers(t.AsConditionalType()!._mapper, m), false /*forConstraint*/, alias);
        }
        if ((flags & TypeFlags.Substitution) != 0)
        {
            var newBaseType = InstantiateType(t.AsSubstitutionType()!._baseType!, m);
            if (IsNoInferType(t))
            {
                return GetNoInferType(newBaseType);
            }
            var newConstraint = InstantiateType(t.AsSubstitutionType()!._constraint!, m);
            // A substitution type originates in the true branch of a conditional type and can be resolved
            // to just the base type in the same cases as the conditional type resolves to its true branch
            // (because the base type is then known to satisfy the constraint).
            if ((newBaseType._flags & TypeFlags.TypeVariable) != 0 && IsGenericType(newConstraint))
            {
                return GetSubstitutionType(newBaseType, newConstraint);
            }
            if ((newConstraint._flags & TypeFlags.AnyOrUnknown) != 0 || IsTypeAssignableToWorker(GetRestrictiveInstantiation(newBaseType), GetRestrictiveInstantiation(newConstraint)))
            {
                return newBaseType;
            }
            if ((newBaseType._flags & TypeFlags.TypeVariable) != 0)
            {
                return GetSubstitutionType(newBaseType, newConstraint);
            }
            return GetIntersectionType(new Type[] { newConstraint, newBaseType });
        }
        return t;
    }

    /// <summary>
    /// Handles instantiation of the following object types:
    /// AnonymousType (ObjectFlagsAnonymous|ObjectFlagsSingleSignatureType),
    /// TypeReference with node != nil (ObjectFlagsReference),
    /// InstantiationExpressionType (ObjectFlagsInstantiationExpressionType),
    /// MappedType (ObjectFlagsMapped).
    /// </summary>
    // Go: checker.go:22663
    internal Type GetObjectTypeInstantiation(Type t, TypeMapper m, TypeAlias? alias)
    {
        Node? declaration;
        Type target;
        Type[]? typeParameters;
        if ((t._objectFlags & ObjectFlags.Reference) != 0) // Deferred type reference
        {
            declaration = t.AsTypeReference()!._node;
        }
        else if ((t._objectFlags & ObjectFlags.InstantiationExpressionType) != 0)
        {
            declaration = t.AsInstantiationExpressionType()!._node;
        }
        else
        {
            declaration = t._symbol!.Declarations![0];
        }
        var links = _typeNodeLinks.Get(declaration!);
        if ((t._objectFlags & ObjectFlags.Reference) != 0) // Deferred type reference
        {
            target = links._resolvedType!;
        }
        else if ((t._objectFlags & ObjectFlags.Instantiated) != 0)
        {
            target = t.Target()!;
        }
        else
        {
            target = t;
        }
        typeParameters = links._outerTypeParameters;
        if (typeParameters == null)
        {
            // The first time an anonymous type is instantiated we compute and store a list of the type
            // parameters that are in scope (and therefore potentially referenced). For type literals that
            // aren't the right hand side of a generic type alias declaration we optimize by reducing the
            // set of type parameters to those that are possibly referenced in the literal.
            typeParameters = GetOuterTypeParameters(declaration!, true /*includeThisTypes*/);
            if ((target._alias?._typeArguments?.Length ?? 0) == 0)
            {
                if ((t._objectFlags & (ObjectFlags.Reference | ObjectFlags.InstantiationExpressionType)) != 0)
                {
                    typeParameters = Core.Filter(typeParameters, tp => IsTypeParameterPossiblyReferenced(tp, declaration!));
                }
                else if ((target._symbol!.Flags & (SymbolFlags.Method | SymbolFlags.TypeLiteral)) != 0)
                {
                    typeParameters = Core.Filter(typeParameters, tp => Core.Some(t._symbol!.Declarations, d => IsTypeParameterPossiblyReferenced(tp, d)));
                }
            }
            if (typeParameters == null)
            {
                typeParameters = [];
            }
            links._outerTypeParameters = typeParameters;
        }
        if (typeParameters.Length == 0)
        {
            return t;
        }
        // We are instantiating an anonymous type that has one or more type parameters in scope. Apply the
        // mapper to the type parameters to produce the effective list of type arguments, and compute the
        // instantiation cache key from the type IDs of the type arguments.
        var typeArguments = new Type[typeParameters.Length];
        for (var i = 0; i < typeParameters.Length; i++)
        {
            typeArguments[i] = MapTypeWithCompositeMapper(typeParameters[i], t.Mapper(), m);
        }
        var newAlias = alias;
        newAlias ??= InstantiateTypeAlias(t._alias, m);
        var data = target.AsObjectType()!;
        var key = GetTypeInstantiationKey(typeArguments, newAlias, (t._objectFlags & ObjectFlags.SingleSignatureType) != 0);
        if (data._instantiations == null)
        {
            data._instantiations = new Dictionary<CacheHashKey, Type>();
            data._instantiations[GetTypeInstantiationKey(typeParameters, target._alias, false)] = target;
        }
        data._instantiations.TryGetValue(key, out var result);
        if (result == null)
        {
            var newMapper = NewTypeMapper(typeParameters, typeArguments);
            if ((target._objectFlags & ObjectFlags.SingleSignatureType) != 0 && m != null)
            {
                newMapper = CombineTypeMappers(newMapper, m);
            }
            if ((target._objectFlags & ObjectFlags.Reference) != 0)
            {
                result = CreateDeferredTypeReference(t.Target()!, t.AsTypeReference()!._node!, newMapper, newAlias);
            }
            else if ((target._objectFlags & ObjectFlags.Mapped) != 0)
            {
                result = InstantiateMappedType(target, newMapper, newAlias);
            }
            else
            {
                result = InstantiateAnonymousType(target, newMapper, newAlias);
            }
            data._instantiations[key] = result;
            if ((result._flags & TypeFlags.ObjectFlagsType) != 0 && (result._objectFlags & ObjectFlags.CouldContainTypeVariablesComputed) == 0)
            {
                // if `result` is one of the object types we tried to make (it may not be, due to how `instantiateMappedType` works), we can carry forward the type variable containment check from the input type arguments
                var resultCouldContainObjectFlags = Core.Some(typeArguments, _couldContainTypeVariables);
                if ((result._objectFlags & ObjectFlags.CouldContainTypeVariablesComputed) == 0)
                {
                    if ((result._objectFlags & (ObjectFlags.Mapped | ObjectFlags.Anonymous | ObjectFlags.Reference)) != 0)
                    {
                        result._objectFlags |= ObjectFlags.CouldContainTypeVariablesComputed | IfElse(resultCouldContainObjectFlags, ObjectFlags.CouldContainTypeVariables, ObjectFlags.None);
                    }
                    else
                    {
                        // If none of the type arguments for the outer type parameters contain type variables, it follows
                        // that the instantiated type doesn't reference type variables.
                        // Intrinsics have `CouldContainTypeVariablesComputed` pre-set, so this should only cover unions and intersections resulting from `instantiateMappedType`
                        result._objectFlags |= IfElse(!resultCouldContainObjectFlags, ObjectFlags.CouldContainTypeVariablesComputed, ObjectFlags.None);
                    }
                }
            }
        }
        return result;
    }

    // Go: checker.go:22762
    internal bool IsTypeParameterPossiblyReferenced(Type tp, Node node)
    {
        bool ContainsReference(Node node2)
        {
            switch (node2.Kind)
            {
                case Kind.ThisType:
                    return tp.AsTypeParameter()!._isThisType;
                case Kind.TypeReference:
                    // use worker because we're looking for === equality
                    if (!tp.AsTypeParameter()!._isThisType && (node2.TypeArguments()?.Length ?? 0) == 0 && GetSymbolFromTypeReference(node2) == tp._symbol)
                    {
                        return true;
                    }
                    break;
                case Kind.TypeQuery:
                    {
                        var entityName = node2.AsTypeQueryNode()!.ExprName;
                        var firstIdentifier = GetFirstIdentifier(entityName);
                        if (!IsThisIdentifier(firstIdentifier!))
                        {
                            var firstIdentifierSymbol = GetResolvedSymbolWorker(firstIdentifier!);
                            var tpDeclaration = tp._symbol!.Declarations![0]; // There is exactly one declaration, otherwise `containsReference` is not called
                            Node? tpScope = null;
                            if (IsTypeParameterDeclaration(tpDeclaration))
                            {
                                tpScope = tpDeclaration.Parent; // Type parameter is a regular type parameter, e.g. foo<T>
                            }
                            else if (tp.AsTypeParameter()!._isThisType)
                            {
                                tpScope = tpDeclaration; // Type parameter is the this type, and its declaration is the class declaration.
                            }
                            if (tpScope != null)
                            {
                                return Core.Some(firstIdentifierSymbol.Declarations, d => IsNodeDescendantOf(d, tpScope)) ||
                                    Core.Some(node2.TypeArguments(), ContainsReference);
                            }
                        }
                        return true;
                    }
                case Kind.MethodDeclaration:
                case Kind.MethodSignature:
                    {
                        var returnType = node2.Type();
                        return returnType == null && node2.Body() != null ||
                            Core.Some(node2.TypeParameters(), ContainsReference) ||
                            Core.Some(node2.Parameters(), ContainsReference) ||
                            returnType != null && ContainsReference(returnType);
                    }
            }
            return node2.ForEachChild(ContainsReference);
        }

        // If the type parameter doesn't have exactly one declaration, if there are intervening statement blocks
        // between the node and the type parameter declaration, if the node contains actual references to the
        // type parameter, or if the node contains type queries that we can't prove couldn't contain references to the type parameter,
        // we consider the type parameter possibly referenced.
        if (tp._symbol != null && tp._symbol.Declarations?.Length == 1)
        {
            var container = tp._symbol.Declarations![0].Parent;
            for (Node? n = node; n != container; n = n.Parent)
            {
                if (n == null || IsBlock(n) || IsConditionalTypeNode(n) && ContainsReference(n.AsConditionalTypeNode()!.ExtendsType!))
                {
                    return true;
                }
            }
            return ContainsReference(node);
        }
        return true;
    }

    // Go: checker.go:22817
    internal Type InstantiateAnonymousType(Type t, TypeMapper m, TypeAlias? alias)
    {
        var result = NewObjectType(t._objectFlags & ~(ObjectFlags.CouldContainTypeVariablesComputed | ObjectFlags.CouldContainTypeVariables) | ObjectFlags.Instantiated, t._symbol);
        if ((t._objectFlags & ObjectFlags.Mapped) != 0)
        {
            result.AsMappedType()!._declaration = t.AsMappedType()!._declaration;
            // C.f. instantiateSignature
            var origTypeParameter = GetTypeParameterFromMappedType(t);
            var freshTypeParameter = CloneTypeParameter(origTypeParameter);
            result.AsMappedType()!._typeParameter = freshTypeParameter;
            m = CombineTypeMappers(new SimpleTypeMapper(origTypeParameter, freshTypeParameter), m);
            freshTypeParameter.AsTypeParameter()!._mapper = m;
        }
        else if ((t._objectFlags & ObjectFlags.InstantiationExpressionType) != 0)
        {
            result.AsInstantiationExpressionType()!._node = t.AsInstantiationExpressionType()!._node;
        }
        if (alias == null)
        {
            alias = InstantiateTypeAlias(t._alias, m);
        }
        result._alias = alias;
        if (alias != null && (alias._typeArguments?.Length ?? 0) != 0)
        {
            result._objectFlags |= GetPropagatingFlagsOfTypes(alias._typeArguments!, TypeFlags.None);
        }
        var d = result.AsObjectType()!;
        d._target = t;
        d._mapper = m;
        return result;
    }

    // Go: checker.go:22844
    internal Type GetConditionalTypeInstantiation(Type t, TypeMapper mapper, bool forConstraint, TypeAlias? alias)
    {
        var root = t.AsConditionalType()!._root!;
        if ((root._outerTypeParameters?.Length ?? 0) != 0)
        {
            // We are instantiating a conditional type that has one or more type parameters in scope. Apply the
            // mapper to the type parameters to produce the effective list of type arguments, and compute the
            // instantiation cache key from the type IDs of the type arguments.
            var typeArguments = Core.Map(root._outerTypeParameters, mapper.Map);
            var key = GetConditionalTypeKey(typeArguments, alias, forConstraint);
            // Go 假定 root.instantiations 非 nil（往 nil map 写会 panic）；C# 字段可空 → 首用处初始化。
            root._instantiations ??= new Dictionary<CacheHashKey, Type>();
            root._instantiations.TryGetValue(key, out var result);
            if (result == null)
            {
                var newMapper = NewTypeMapper(root._outerTypeParameters!, typeArguments);
                var checkType = root._checkType!;
                Type? distributionType = null;
                if (root._isDistributive)
                {
                    distributionType = GetReducedTypeWorker(GetMappedType(checkType, newMapper));
                }
                // Distributive conditional types are distributed over union types. For example, when the
                // distributive conditional type T extends U ? X : Y is instantiated with A | B for T, the
                // result is (A extends U ? X : Y) | (B extends U ? X : Y).
                if (distributionType != null && checkType != distributionType && (distributionType._flags & (TypeFlags.Union | TypeFlags.Never)) != 0)
                {
                    result = MapTypeWithAlias(distributionType, t2 => GetConditionalType(root, PrependTypeMapping(checkType, t2, newMapper), forConstraint, null /*alias*/), alias);
                }
                else
                {
                    result = GetConditionalType(root, newMapper, forConstraint, alias);
                }
                root._instantiations[key] = result;
            }
            return result;
        }
        return t;
    }

    // Go: checker.go:22877
    internal Type CloneTypeParameter(Type tp)
    {
        var result = NewTypeParameter(tp._symbol);
        result.AsTypeParameter()!._target = tp;
        return result;
    }

    // Go: checker.go:22883
    internal Type? GetHomomorphicTypeVariable(Type t)
    {
        var constraintType = GetConstraintTypeFromMappedType(t);
        if ((constraintType._flags & TypeFlags.Index) != 0)
        {
            var typeVariable = GetActualTypeVariable(constraintType.AsIndexType()!._target!);
            if ((typeVariable._flags & TypeFlags.TypeParameter) != 0)
            {
                return typeVariable;
            }
        }
        return null;
    }

    // Go: checker.go:22894
    internal Type InstantiateMappedType(Type t, TypeMapper m, TypeAlias? alias)
    {
        // For a homomorphic mapped type { [P in keyof T]: X }, where T is some type variable, the mapping
        // operation depends on T as follows:
        // * If T is a primitive type no mapping is performed and the result is simply T.
        // * If T is a union type we distribute the mapped type over the union.
        // * If T is an array we map to an array where the element type has been transformed.
        // * If T is a tuple we map to a tuple where the element types have been transformed.
        // * If T is an intersection of array or tuple types we map to an intersection of transformed array or tuple types.
        // * Otherwise we map to an object type where the type of each property has been transformed.
        // For example, when T is instantiated to a union type A | B, we produce { [P in keyof A]: X } |
        // { [P in keyof B]: X }, and when when T is instantiated to a union type A | undefined, we produce
        // { [P in keyof A]: X } | undefined.
        var d = t.AsMappedType()!;
        var typeVariable = GetHomomorphicTypeVariable(t);

        Type InstantiateConstituent(Type s)
        {
            if ((s._flags & (TypeFlags.AnyOrUnknown | TypeFlags.InstantiableNonPrimitive | TypeFlags.Object | TypeFlags.Intersection)) == 0 || s == _wildcardType || IsErrorType(s))
            {
                return s;
            }
            if (d._declaration!.NameType == null)
            {
                if (IsArrayTypeWorker(s) || (s._flags & TypeFlags.Any) != 0 && FindResolutionCycleStartIndex(typeVariable!, TypeSystemPropertyName.ResolvedBaseConstraint) < 0 && HasArrayOrTypeTypeConstraint(typeVariable!))
                {
                    return InstantiateMappedArrayType(s, t, PrependTypeMapping(typeVariable!, s, m));
                }
                if (IsTupleTypeWorker(s))
                {
                    return InstantiateMappedTupleType(s, t, typeVariable!, m);
                }
                if (IsArrayOrTupleOrIntersection(s))
                {
                    return GetIntersectionType(Core.Map(s.Types(), InstantiateConstituent));
                }
            }
            return InstantiateAnonymousType(t, PrependTypeMapping(typeVariable!, s, m), null);
        }

        if (typeVariable != null)
        {
            var mappedTypeVariable = InstantiateType(typeVariable, m);
            if (typeVariable != mappedTypeVariable)
            {
                return MapTypeWithAlias(GetReducedTypeWorker(mappedTypeVariable), InstantiateConstituent, alias);
            }
        }
        // If the constraint type of the instantiation is the wildcard type, return the wildcard type.
        if (InstantiateType(GetConstraintTypeFromMappedType(t), m) == _wildcardType)
        {
            return _wildcardType;
        }
        return InstantiateAnonymousType(t, m, alias);
    }

    // Go: checker.go:22939
    internal bool HasArrayOrTypeTypeConstraint(Type typeVariable)
    {
        var constraint = GetConstraintOfTypeParameterWorker(typeVariable);
        return constraint != null && EveryType(constraint, IsArrayOrTupleType);
    }

    // Go: checker.go:22944
    internal Type InstantiateMappedArrayType(Type arrayType, Type mappedType, TypeMapper m)
    {
        var elementType = InstantiateMappedTypeTemplate(mappedType, _numberType, true /*isOptional*/, m);
        if (IsErrorType(elementType))
        {
            return _errorType;
        }
        return CreateArrayTypeEx(elementType, GetModifiedReadonlyState(IsReadonlyArrayType(arrayType), GetMappedTypeModifiers(mappedType)));
    }

    // Go: checker.go:22952
    internal Type InstantiateMappedTupleType(Type tupleType, Type mappedType, Type typeVariable, TypeMapper m)
    {
        // We apply the mapped type's template type to each of the fixed part elements. For variadic elements, we
        // apply the mapped type itself to the variadic element type. For other elements in the variable part of the
        // tuple, we surround the element type with an array type and apply the mapped type to that. This ensures
        // that we get sequential property key types for the fixed part of the tuple, and property key type number
        // for the remaining elements. For example
        //
        //   type Keys<T> = { [K in keyof T]: K };
        //   type Foo<T extends any[]> = Keys<[string, string, ...T, string]>; // ["0", "1", ...Keys<T>, number]
        //
        var elementInfos = tupleType.TargetTupleType()!._elementInfos!;
        var fixedLength = tupleType.TargetTupleType()!._fixedLength;
        var fixedMapper = m;
        if (fixedLength != 0)
        {
            fixedMapper = PrependTypeMapping(typeVariable, tupleType, m);
        }
        var modifiers = GetMappedTypeModifiers(mappedType);
        var elementTypes = GetElementTypes(tupleType);
        var newElementTypes = new Type[elementTypes.Length];
        var newElementInfos = (TupleElementInfo[])elementInfos.Clone();
        for (var i = 0; i < elementTypes.Length; i++)
        {
            var e = elementTypes[i];
            var flags = elementInfos[i]._flags;
            Type? mapped;
            if (i < fixedLength)
            {
                mapped = InstantiateMappedTypeTemplate(mappedType, GetStringLiteralType(i.ToString()), (flags & ElementFlags.Optional) != 0, fixedMapper);
            }
            else if ((flags & ElementFlags.Variadic) != 0)
            {
                mapped = InstantiateType(mappedType, PrependTypeMapping(typeVariable, e, m));
            }
            else
            {
                mapped = GetElementTypeOfArrayTypeWorker(InstantiateType(mappedType, PrependTypeMapping(typeVariable, CreateArrayType(e), m)));
                if (mapped == null)
                {
                    mapped = _unknownType;
                }
            }
            if ((modifiers & MappedTypeModifiers.IncludeOptional) != 0)
            {
                if ((flags & ElementFlags.Required) != 0)
                {
                    newElementInfos[i]._flags = ElementFlags.Optional;
                }
            }
            else if ((modifiers & MappedTypeModifiers.ExcludeOptional) != 0)
            {
                if ((flags & ElementFlags.Optional) != 0)
                {
                    newElementInfos[i]._flags = ElementFlags.Required;
                }
            }
            newElementTypes[i] = mapped;
        }
        var newReadonly = GetModifiedReadonlyState(tupleType.TargetTupleType()!._readonly, GetMappedTypeModifiers(mappedType));
        if (Array.IndexOf(newElementTypes, _errorType) >= 0)
        {
            return _errorType;
        }
        return CreateTupleTypeEx(newElementTypes, newElementInfos, newReadonly);
    }

    // Go: checker.go:23005
    internal Type InstantiateMappedTypeTemplate(Type t, Type key, bool isOptional, TypeMapper m)
    {
        var templateMapper = AppendTypeMapping(m, GetTypeParameterFromMappedType(t), key);
        var propType = InstantiateType(GetTemplateTypeFromMappedType(OrElse(t.AsMappedType()!._target, t)!), templateMapper);
        var modifiers = GetMappedTypeModifiers(t);
        if (_strictNullChecks && (modifiers & MappedTypeModifiers.IncludeOptional) != 0 && !MaybeTypeOfKind(propType, TypeFlags.Undefined | TypeFlags.Void))
        {
            return GetOptionalType(propType, true /*isProperty*/);
        }
        if (_strictNullChecks && (modifiers & MappedTypeModifiers.ExcludeOptional) != 0 && isOptional)
        {
            return RemoveMissingOrUndefinedTypeWorker(propType);
        }
        return propType;
    }

    // Go: checker.go:23019
    internal static bool GetModifiedReadonlyState(bool state, MappedTypeModifiers modifiers)
    {
        if ((modifiers & MappedTypeModifiers.IncludeReadonly) != 0)
        {
            return true;
        }
        if ((modifiers & MappedTypeModifiers.ExcludeReadonly) != 0)
        {
            return false;
        }
        return state;
    }

    // Go: checker.go:23029
    internal Type GetTypeParameterFromMappedType(Type t)
    {
        var m = t.AsMappedType()!;
        if (m._typeParameter == null)
        {
            m._typeParameter = GetDeclaredTypeOfTypeParameter(GetSymbolOfDeclaration(m._declaration!.TypeParameter!)!);
        }
        return m._typeParameter!;
    }

    // Go: checker.go:23037
    internal Type GetConstraintTypeFromMappedType(Type t)
    {
        var m = t.AsMappedType()!;
        if (m._constraintType == null)
        {
            m._constraintType = OrElse(GetConstraintOfTypeParameterWorker(GetTypeParameterFromMappedType(t)), _errorType)!;
        }
        return m._constraintType!;
    }

    // Go: checker.go:23045
    internal Type? GetNameTypeFromMappedType(Type t)
    {
        var m = t.AsMappedType()!;
        if (m._declaration!.NameType == null)
        {
            return null;
        }
        if (m._nameType == null)
        {
            m._nameType = InstantiateType(GetTypeFromTypeNodeWorker(m._declaration!.NameType), m._mapper);
        }
        return m._nameType;
    }

    // Go: checker.go:23056
    internal Type GetTemplateTypeFromMappedType(Type t)
    {
        var m = t.AsMappedType()!;
        if (m._templateType == null)
        {
            if (m._declaration!.Type != null)
            {
                m._templateType = InstantiateType(AddOptionalityEx(GetTypeFromTypeNodeWorker(m._declaration!.Type!) /*isProperty*/, true, (GetMappedTypeModifiers(t) & MappedTypeModifiers.IncludeOptional) != 0), m._mapper);
            }
            else
            {
                m._templateType = _errorType;
            }
        }
        return m._templateType!;
    }

    // Go: checker.go:23068
    internal bool IsMappedTypeWithKeyofConstraintDeclaration(Type t)
    {
        var constraintDeclaration = GetConstraintDeclarationForMappedType(t);
        return IsTypeOperatorNode(constraintDeclaration!) && constraintDeclaration!.AsTypeOperatorNode()!.Operator == Kind.KeyOfKeyword;
    }

    // Go: checker.go:23073
    internal Node? GetConstraintDeclarationForMappedType(Type t)
    {
        return t.AsMappedType()!._declaration!.TypeParameter!.AsTypeParameterDeclaration()!.Constraint;
    }

    // Go: checker.go:23077
    internal Type GetApparentMappedTypeKeys(Type nameType, Type targetType)
    {
        var modifiersType = GetApparentTypeWorker(GetModifiersTypeFromMappedType(targetType));
        var mappedKeys = new List<Type>();
        ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, TypeFlags.StringOrNumberLiteralOrUnique, false, t =>
        {
            mappedKeys.Add(InstantiateType(nameType, AppendTypeMapping(targetType.Mapper(), GetTypeParameterFromMappedType(targetType), t)));
        });
        return GetUnionTypeWorker(mappedKeys.ToArray());
    }

    // Go: checker.go:23086
    internal void ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(Type t, TypeFlags include, bool stringsOnly, Action<Type> cb)
    {
        foreach (var prop in GetPropertiesOfTypeWorker(t)!)
        {
            cb(GetLiteralTypeFromProperty(prop, include, false));
        }
        if ((t._flags & TypeFlags.Any) != 0)
        {
            cb(_stringType);
        }
        else
        {
            foreach (var info in GetIndexInfosOfTypeWorker(t))
            {
                if (!stringsOnly || (info._keyType!._flags & (TypeFlags.String | TypeFlags.TemplateLiteral)) != 0)
                {
                    cb(info._keyType!);
                }
            }
        }
    }

    // Go: checker.go:23101
    internal Type InstantiateReverseMappedType(Type t, TypeMapper m)
    {
        var r = t.AsReverseMappedType()!;
        var innerMappedType = InstantiateType(r._mappedType!, m);
        if ((innerMappedType._objectFlags & ObjectFlags.Mapped) == 0)
        {
            return t;
        }
        var innerIndexType = InstantiateType(r._constraintType!, m);
        if ((innerIndexType._flags & TypeFlags.Index) == 0)
        {
            return t;
        }
        var instantiated = InferTypeForHomomorphicMappedType(InstantiateType(r._source!, m), innerMappedType, innerIndexType);
        if (instantiated != null)
        {
            return instantiated;
        }
        return t;
        // Nested invocation of `inferTypeForHomomorphicMappedType` or the `source` instantiated into something unmappable
    }

    // Go: checker.go:23119
    internal TypeAlias? InstantiateTypeAlias(TypeAlias? alias, TypeMapper m)
    {
        if (alias == null)
        {
            return null;
        }
        return new TypeAlias { _symbol = alias._symbol, _typeArguments = InstantiateTypes(alias._typeArguments, m) };
    }

    // Go: checker.go:23126
    internal Type[] InstantiateTypes(Type[]? types, TypeMapper m)
    {
        // Go: c.instantiateList(types, m, (*Checker).instantiateType)
        return InstantiateList(types, m, (checker, value, mapper) => checker.InstantiateType(value, mapper));
    }

    // Go: checker.go:23130
    internal Symbol[] InstantiateSymbols(Symbol[]? symbols, TypeMapper m)
    {
        // Go: c.instantiateList(symbols, m, (*Checker).instantiateSymbol)
        return InstantiateList(symbols, m, (checker, value, mapper) => checker.InstantiateSymbol(value, mapper)!);
    }

    // Go: checker.go:23134
    internal Signature[] InstantiateSignatures(Signature[]? signatures, TypeMapper m)
    {
        // Go: c.instantiateList(signatures, m, (*Checker).instantiateSignature)
        return InstantiateList(signatures, m, (checker, value, mapper) => checker.InstantiateSignature(value, mapper));
    }

    // Go: checker.go:23138
    internal IndexInfo[] InstantiateIndexInfos(IndexInfo[]? indexInfos, TypeMapper m)
    {
        // Go: c.instantiateList(indexInfos, m, (*Checker).instantiateIndexInfo)
        return InstantiateList(indexInfos, m, (checker, value, mapper) => checker.InstantiateIndexInfo(value, mapper));
    }

    // Go: checker.go:23142
    internal T[] InstantiateList<T>(T[]? values, TypeMapper m, Func<Checker, T, TypeMapper, T> instantiator)
    {
        if (values == null)
        {
            // Go 的 nil slice：range 不执行并原样返回 nil；调用点只按长度与 `Core.Same` 使用 → 返回空数组（见文件头第 11 条）。
            return [];
        }
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var mapped = instantiator(this, value, m);
            if (!EqualityComparer<T>.Default.Equals(mapped, value))
            {
                var result = new T[values.Length];
                Array.Copy(values, result, i);
                result[i] = mapped;
                for (var j = i + 1; j < values.Length; j++)
                {
                    result[j] = instantiator(this, values[j], m);
                }
                return result;
            }
        }
        return values;
    }
}