// ported from: internal/checker/checker.go:21450-22020
// （getUnionSignatures … createUnionOrIntersectionProperty：union/intersection 的签名合并、
//   intersection 成员解析、mixin 构造签名处理、union/intersection 合成属性）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (c *Checker) x()` → 实例方法（PascalCase / internal）；Go 的**包级函数**
//    （`isPrototypeProperty`、`isLateBoundName`、`isLiteralType`、`compareTypesEqual`、
//    `getDeclarationModifierFlagsFromSymbol(Ex)`）→ 同一个 `partial class Checker` 的 static 成员，
//    调用点裸名（与 Go 一致）。
// 2) 命名：Go 未导出**字段** → `_camelCase`（`sig.typeParameters` → `sig._typeParameters`）；
//    Go 导出字段 → 原名 PascalCase（`prop.Flags`/`prop.Declaration*`/`symbol.ValueDeclaration`）；
//    枚举成员名 = Go 常量名去类型前缀（`SignatureFlagsPropagatingFlags` → `SignatureFlags.PropagatingFlags`、
//    `UnionReductionLiteral` → `UnionReduction.Literal`、`ast.CheckFlagsRestParameter` → `CheckFlags.RestParameter`）。
// 3) nil slice：`getUnionSignatures`/`appendSignatures`/`appendIndexInfo` 用 `T[]?` —— Go 用 nil 表示
//    "无法合并/没有"，调用点靠 nil 判断，不能用空数组代替；局部累积一律 `Core.Concatenate`。
// 4) 多返回值：`findMixins` 的 `([]bool, int)` → C# 元组 `(bool[] MixinFlags, int MixinCount)`
//    （两个值都不是"查找成败"，所以不用 `TryX(..., out T)`）。
// 5) `writeTypes = slices.Clone(propTypes)` 后紧跟 `append`：Go 的 `Clone(nil)` 是 nil，随后 append 得到
//    单元素切片 → 等价写成 `Core.Concatenate(propTypes, new[] { writeType })`。
// 6) Go 的 `switch { case cond: … }`（无表达式）→ `if/else if` 链；`default` 无（Go 里也没有）。
// 7) `for i := range n` → `for (var i = 0; i < n; i++)`；`for _, x := range s` → `foreach`。
// 8) `resolveIntersectionTypeMembers`/`createUnionOrIntersectionProperty` 里 Go 的内层循环变量 `t`
//    遮蔽（或将要遮蔽）外层 `t` → C# 不允许同名（CS0136），内层改名 `current`（外层 `t` 仍用于
//    `setStructuredTypeMembers`）。
// 9) 局部变量 `params`（Go）→ `parameters`（`params` 是 C# 关键字）。
// 10) `ast.GetSymbolTable(&t.AsUnionOrIntersectionType().propertyCache)` → 先取本地 `var unionOrIntersectionType = t.AsUnionOrIntersectionType()!`，
//    再 `GetSymbolTable(ref unionOrIntersectionType._propertyCache)`（C# 无取地址 → `ref` 字段，语义相同：null 时建表并写回）。
// 11) Go 把实例方法当函数值传（`core.Map(types, c.isMixinConstructorType)`、`c.compareTypesIdentical`）
//    → C# 用 lambda（`t => IsMixinConstructorType(t)`、`(s, tp) => CompareTypesEqual(s, tp)`）。
// 12) `propSet` 是 Go `collections.OrderedSet[*ast.Symbol]`（**不是** checker 包私有的 `orderedSet`）→
//    全限定写 `TypeScript.Collections.OrderedSet<Symbol>`，避免与包内同名类型混淆（不引入 using）。
// 13) `createUnionOrIntersectionProperty` 里 Go 三个不同作用域的 `links` → C# 同一方法作用域内需要区分，
//    分别写 `links`（singleProp 的）/`cloneLinks`/`resultLinks`。
// 14) `Signature._typeParameters`/`_parameters` 在 C# 是 `T[]?`（对应 Go 的 nil slice）：取长度与传参
//    统一用访问器 `TypeParameters()`/`Parameters()`（返回 `[]`，与 Go 的 `len(nil) == 0` 等价）。
// 15) C# 的 `getSignaturesOfType`/`getIndexInfosOfType` 返回 `T[]?`（Go 的 nil 切片）→ 消费点统一
//    `?? []` 归一（这些点只看长度/索引，与 Go 的 `len(nil) == 0` 等价）。
// 16) C# 的 `node.Symbol` 是**属性**（docs/ast.md §2：名字与返回类型名相同 → 属性）→ Go 的 `n.Symbol()`
//    写 `n.Symbol`。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

namespace TypeScript.Checker;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。本命名空间作用域内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

public sealed partial class Checker
{
    internal Signature[]? GetUnionSignatures(Signature[][] signatureLists)
    {
        Signature[]? result = null;
        var indexWithLengthOverOne = 0;
        var countLengthOverOne = 0;
        for (var i = 0; i < signatureLists.Length; i++)
        {
            if (signatureLists[i].Length == 0)
            {
                return null;
            }
            if (signatureLists[i].Length > 1)
            {
                indexWithLengthOverOne = i;
                countLengthOverOne++;
            }
            foreach (var signature in signatureLists[i])
            {
                // Only process signatures with parameter lists that aren't already in the result list
                if (result is null || FindMatchingSignature(result, signature, false /*partialMatch*/, false /*ignoreThisTypes*/, true /*ignoreReturnTypes*/) == null)
                {
                    var unionSignatures = FindMatchingSignatures(signatureLists, signature, i);
                    if (unionSignatures != null)
                    {
                        var s = signature;
                        // Union the result types when more than one signature matches
                        if (unionSignatures.Length > 1)
                        {
                            var thisParameter = signature._thisParameter;
                            Symbol? firstThisParameterOfUnionSignatures = FirstNonNil(unionSignatures, sig => sig._thisParameter);
                            if (firstThisParameterOfUnionSignatures != null)
                            {
                                // Go 的闭包在 `this` 参数缺失时返回 nil，由 `core.MapNonNil` 过滤掉
                                Type[] thisTypes = MapNonNil(unionSignatures, sig => sig._thisParameter != null ? GetTypeOfSymbolWorker(sig._thisParameter) : null!);
                                var thisType = GetIntersectionType(thisTypes);
                                thisParameter = CreateSymbolWithType(firstThisParameterOfUnionSignatures, thisType);
                            }
                            s = CreateUnionSignature(signature, unionSignatures);
                            s._thisParameter = thisParameter;
                        }
                        result = Concatenate(result, new[] { s });
                    }
                }
            }
        }
        if ((result is null || result.Length == 0) && countLengthOverOne <= 1)
        {
            // No sufficiently similar signature existed to subsume all the other signatures in the union - time to see if we can make a single
            // signature that handles all of them. We only do this when there are overloads in only one constituent. (Overloads are conditional in
            // nature and having overloads in multiple constituents would necessitate making a power set of signatures from the type, whose
            // ordering would be non-obvious)
            var masterList = signatureLists[indexWithLengthOverOne];
            Signature[]? results = (Signature[])masterList.Clone();
            foreach (var signatures in signatureLists)
            {
                if (!Same(signatures, masterList))
                {
                    var signature = signatures[0];
                    Debug.Assert(signature != null, "getUnionSignatures bails early on empty signature lists and should not have empty lists on second pass");
                    if (signature!.TypeParameters().Length != 0 && Some(results, s => s.TypeParameters().Length != 0 && !CompareTypeParametersIdentical(signature.TypeParameters(), s.TypeParameters())))
                    {
                        results = null;
                    }
                    else
                    {
                        results = Map(results, sig => CombineUnionOrIntersectionMemberSignatures(sig, signature, true /*isUnion*/));
                    }
                    if (results is null)
                    {
                        break;
                    }
                }
            }
            result = results;
        }
        return result;
    }

    internal Signature CombineUnionOrIntersectionMemberSignatures(Signature left, Signature right, bool isUnion)
    {
        var typeParams = left.TypeParameters();
        if (typeParams.Length == 0)
        {
            typeParams = right.TypeParameters();
        }
        TypeMapper? paramMapper = null;
        if (left.TypeParameters().Length != 0 && right.TypeParameters().Length != 0)
        {
            // We just use the type parameter defaults from the first signature
            paramMapper = NewTypeMapper(right.TypeParameters(), left.TypeParameters());
        }
        var flags = (left._flags | right._flags) & (SignatureFlags.PropagatingFlags & ~SignatureFlags.HasRestParameter);
        var declaration = left._declaration;
        var parameters = CombineUnionOrIntersectionParameters(left, right, paramMapper, isUnion);
        var lastParam = LastOrNil(parameters);
        if (lastParam != null && (lastParam.CheckFlags & CheckFlags.RestParameter) != 0)
        {
            flags |= SignatureFlags.HasRestParameter;
        }
        var thisParam = CombineUnionOrIntersectionThisParam(left._thisParameter, right._thisParameter, paramMapper, isUnion);
        var minArgCount = Math.Max(left._minArgumentCount, right._minArgumentCount);
        var result = NewSignature(flags, declaration!, typeParams, thisParam, parameters, null, null, minArgCount);
        Signature[]? leftSignatures;
        if (left._composite != null && left._composite._isUnion)
        {
            leftSignatures = left._composite._signatures;
        }
        else
        {
            leftSignatures = new[] { left };
        }
        result._composite = new CompositeSignature { _isUnion = isUnion, _signatures = Concatenate(leftSignatures, new[] { right }) };
        if (paramMapper != null)
        {
            if (left._composite != null && left._composite._isUnion == isUnion && left._mapper != null)
            {
                result._mapper = CombineTypeMappers(left._mapper, paramMapper);
            }
            else
            {
                result._mapper = paramMapper;
            }
        }
        else if (left._composite != null && left._composite._isUnion == isUnion)
        {
            result._mapper = left._mapper;
        }
        return result;
    }

    internal Symbol[] CombineUnionOrIntersectionParameters(Signature left, Signature right, TypeMapper? mapper, bool isUnion)
    {
        var leftCount = GetParameterCount(left);
        var rightCount = GetParameterCount(right);
        int longestCount;
        Signature longest;
        Signature shorter;
        if (leftCount >= rightCount)
        {
            longestCount = leftCount;
            longest = left;
            shorter = right;
        }
        else
        {
            longestCount = rightCount;
            longest = right;
            shorter = left;
        }
        var eitherHasEffectiveRest = HasEffectiveRestParameterWorker(left) || HasEffectiveRestParameterWorker(right);
        var needsExtraRestElement = eitherHasEffectiveRest && !HasEffectiveRestParameterWorker(longest);
        var parameters = new Symbol[longestCount + IfElse(needsExtraRestElement, 1, 0)];
        for (var i = 0; i < longestCount; i++)
        {
            var longestParamType = TryGetTypeAtPosition(longest, i);
            if (longest == right)
            {
                longestParamType = InstantiateType(longestParamType!, mapper);
            }
            var shorterParamType = OrElse(TryGetTypeAtPosition(shorter, i)!, _unknownType);
            if (shorter == right)
            {
                shorterParamType = InstantiateType(shorterParamType, mapper);
            }
            var combinedParamType = GetUnionOrIntersectionType(new[] { longestParamType!, shorterParamType }, !isUnion, UnionReduction.Literal);
            var isRestParam = eitherHasEffectiveRest && !needsExtraRestElement && i == longestCount - 1;
            var isOptional = i >= GetMinArgumentCount(longest) && i >= GetMinArgumentCount(shorter);
            var leftName = "";
            var rightName = "";
            if (i < leftCount)
            {
                leftName = GetParameterNameAtPosition(left, i);
            }
            if (i < rightCount)
            {
                rightName = GetParameterNameAtPosition(right, i);
            }
            var paramName = "";
            if (leftName == rightName)
            {
                paramName = leftName;
            }
            else if (leftName == "")
            {
                paramName = rightName;
            }
            else if (rightName == "")
            {
                paramName = leftName;
            }
            if (paramName == "")
            {
                paramName = "arg" + i.ToString();
            }
            var paramSymbol = NewSymbolEx(SymbolFlags.FunctionScopedVariable | IfElse(isOptional && !isRestParam, SymbolFlags.Optional, SymbolFlags.None), paramName,
                IfElse(isRestParam, CheckFlags.RestParameter, IfElse(isOptional, CheckFlags.OptionalParameter, CheckFlags.None)));
            var links = _valueSymbolLinks.Get(paramSymbol);
            if (isRestParam)
            {
                links._resolvedType = CreateArrayType(combinedParamType);
            }
            else
            {
                links._resolvedType = combinedParamType;
            }
            parameters[i] = paramSymbol;
        }
        if (needsExtraRestElement)
        {
            var restParamSymbol = NewSymbolEx(SymbolFlags.FunctionScopedVariable, "args", CheckFlags.RestParameter);
            var links = _valueSymbolLinks.Get(restParamSymbol);
            links._resolvedType = CreateArrayType(GetTypeAtPositionWorker(shorter, longestCount));
            if (shorter == right)
            {
                links._resolvedType = InstantiateType(links._resolvedType!, mapper);
            }
            parameters[longestCount] = restParamSymbol;
        }
        return parameters;
    }

    internal Symbol? CombineUnionOrIntersectionThisParam(Symbol? left, Symbol? right, TypeMapper? mapper, bool isUnion)
    {
        if (left == null)
        {
            return right;
        }
        if (right == null)
        {
            return left;
        }
        // A signature `this` type might be a read or a write position... It's very possible that it should be invariant
        // and we should refuse to merge signatures if there are `this` types and they do not match. However, so as to be
        // permissive when calling, for now, we'll intersect the `this` types just like we do for param types in union signatures.
        var thisType = GetUnionOrIntersectionType(new[] { GetTypeOfSymbolWorker(left), InstantiateType(GetTypeOfSymbolWorker(right), mapper) }, !isUnion, UnionReduction.Literal);
        return CreateSymbolWithType(left, thisType);
    }

    internal void ResolveIntersectionTypeMembers(Type t)
    {
        // The members and properties collections are empty for intersection types. To get all properties of an
        // intersection type use getPropertiesOfType (only the language service uses this).
        Signature[]? callSignatures = null;
        Signature[]? constructSignatures = null;
        IndexInfo[]? indexInfos = null;
        var types = t.Types();
        var (mixinFlags, mixinCount) = FindMixins(types);
        for (var i = 0; i < types.Length; i++)
        {
            var current = types[i];
            // When an intersection type contains mixin constructor types, the construct signatures from
            // those types are discarded and their return types are mixed into the return types of all
            // other construct signatures in the intersection type. For example, the intersection type
            // '{ new(...args: any[]) => A } & { new(s: string) => B }' has a single construct signature
            // 'new(s: string) => A & B'.
            if (!mixinFlags[i])
            {
                // Go 的 getSignaturesOfType 可能返回 nil 切片，这里统一归一成空数组（所有消费点只看长度/索引）
                var signatures = GetSignaturesOfTypeWorker(current, SignatureKind.Construct) ?? [];
                if (signatures.Length != 0 && mixinCount > 0)
                {
                    signatures = Map(signatures, s =>
                    {
                        var clone = CloneSignature(s);
                        clone._resolvedReturnType = IncludeMixinType(GetReturnTypeOfSignatureWorker(s), types, mixinFlags, i);
                        return clone;
                    });
                }
                constructSignatures = AppendSignatures(constructSignatures, signatures);
            }
            callSignatures = AppendSignatures(callSignatures, GetSignaturesOfTypeWorker(current, SignatureKind.Call) ?? []);
            foreach (var info in GetIndexInfosOfTypeWorker(current) ?? [])
            {
                indexInfos = AppendIndexInfo(indexInfos, info, false /*union*/);
            }
        }
        SetStructuredTypeMembers(t, null, callSignatures, constructSignatures, indexInfos);
    }

    internal Signature[]? AppendSignatures(Signature[]? signatures, Signature[] newSignatures)
    {
        foreach (var sig in newSignatures)
        {
            if (signatures is null || signatures.Length == 0 || Every(signatures, s => CompareSignaturesIdentical(s, sig, false /*partialMatch*/, false /*ignoreThisTypes*/, false /*ignoreReturnTypes*/, (s2, t2) => CompareTypesIdentical(s2, t2)) == Ternary.False))
            {
                signatures = Concatenate(signatures, new[] { sig });
            }
        }
        return signatures;
    }

    internal IndexInfo[]? AppendIndexInfo(IndexInfo[]? indexInfos, IndexInfo newInfo, bool union)
    {
        if (indexInfos != null)
        {
            for (var i = 0; i < indexInfos.Length; i++)
            {
                var info = indexInfos[i];
                if (info._keyType == newInfo._keyType)
                {
                    Type valueType;
                    bool isReadonly;
                    if (union)
                    {
                        valueType = GetUnionTypeWorker(new[] { info._valueType!, newInfo._valueType! });
                        isReadonly = info._isReadonly || newInfo._isReadonly;
                    }
                    else
                    {
                        valueType = GetIntersectionType(new[] { info._valueType!, newInfo._valueType! });
                        isReadonly = info._isReadonly && newInfo._isReadonly;
                    }
                    indexInfos[i] = NewIndexInfo(info._keyType!, valueType, isReadonly, null, null);
                    return indexInfos;
                }
            }
        }
        return Concatenate(indexInfos, new[] { newInfo });
    }

    internal (bool[] MixinFlags, int MixinCount) FindMixins(Type[] types)
    {
        var mixinFlags = Map(types, t => IsMixinConstructorType(t));
        var constructorTypeCount = 0;
        var mixinCount = 0;
        var firstMixinIndex = -1;
        for (var i = 0; i < types.Length; i++)
        {
            var constructSignatures = GetSignaturesOfTypeWorker(types[i], SignatureKind.Construct) ?? [];
            if (constructSignatures.Length > 0)
            {
                constructorTypeCount++;
            }
            if (mixinFlags[i])
            {
                if (firstMixinIndex < 0)
                {
                    firstMixinIndex = i;
                }
                mixinCount++;
            }
        }
        if (constructorTypeCount > 0 && constructorTypeCount == mixinCount)
        {
            mixinFlags[firstMixinIndex] = false;
            mixinCount--;
        }
        return (mixinFlags, mixinCount);
    }

    internal Type IncludeMixinType(Type t, Type[] types, bool[] mixinFlags, int index)
    {
        Type[]? mixedTypes = null;
        for (var i = 0; i < types.Length; i++)
        {
            if (i == index)
            {
                mixedTypes = Concatenate(mixedTypes, new[] { t });
            }
            else if (mixinFlags[i])
            {
                mixedTypes = Concatenate(mixedTypes, new[] { GetReturnTypeOfSignatureWorker(GetSignaturesOfTypeWorker(types[i], SignatureKind.Construct)![0]) });
            }
        }
        return GetIntersectionType(mixedTypes!);
    }

    /// <summary>
    /// If the given type is an object type and that type has a property by the given name,
    /// return the symbol for that property. Otherwise return undefined.
    /// </summary>
    internal Symbol? GetPropertyOfObjectType(Type t, string name)
    {
        if ((t._flags & TypeFlags.Object) != 0)
        {
            var resolved = ResolveStructuredTypeMembers(t);
            var symbol = resolved._members?.GetValueOrDefault(name);
            if (symbol != null && SymbolIsValue(symbol))
            {
                return symbol;
            }
        }
        return null;
    }

    internal Symbol? GetPropertyOfUnionOrIntersectionType(Type t, string name, bool skipObjectFunctionPropertyAugment)
    {
        var prop = GetUnionOrIntersectionProperty(t, name, skipObjectFunctionPropertyAugment);
        // We need to filter out partial properties in union types
        if (prop != null && (prop.CheckFlags & CheckFlags.ReadPartial) != 0)
        {
            return null;
        }
        return prop;
    }

    // Return the symbol for a given property in a union or intersection type, or undefined if the property
    // does not exist in any constituent type. Note that the returned property may only be present in some
    // constituents, in which case the isPartial flag is set when the containing type is union type. We need
    // these partial properties when identifying discriminant properties, but otherwise they are filtered out
    // and do not appear to be present in the union type.
    internal Symbol? GetUnionOrIntersectionProperty(Type t, string name, bool skipObjectFunctionPropertyAugment)
    {
        var unionOrIntersectionType = t.AsUnionOrIntersectionType()!;
        // Go: ast.GetSymbolTable(&t.AsUnionOrIntersectionType().propertyCache)（同义：null 时建表并写回字段）
        SymbolTable cache;
        if (skipObjectFunctionPropertyAugment)
        {
            cache = GetSymbolTable(ref unionOrIntersectionType._propertyCacheWithoutFunctionPropertyAugment);
        }
        else
        {
            cache = GetSymbolTable(ref unionOrIntersectionType._propertyCache);
        }
        if (cache.TryGetValue(name, out var cachedProp) && cachedProp != null)
        {
            return cachedProp;
        }
        var prop = CreateUnionOrIntersectionProperty(t, name, skipObjectFunctionPropertyAugment);
        if (prop != null)
        {
            cache[name] = prop;
            // Propagate an entry from the non-augmented cache to the augmented cache unless the property is partial.
            if (skipObjectFunctionPropertyAugment && (prop.CheckFlags & CheckFlags.Partial) == 0)
            {
                var augmentedCache = GetSymbolTable(ref unionOrIntersectionType._propertyCache);
                if (!augmentedCache.TryGetValue(name, out var existing) || existing == null)
                {
                    augmentedCache[name] = prop;
                }
            }
        }
        return prop;
    }

    internal Symbol? CreateUnionOrIntersectionProperty(Type containingType, string name, bool skipObjectFunctionPropertyAugment)
    {
        var propFlags = SymbolFlags.None;
        Symbol? singleProp = null;
        var propSet = new TypeScript.Collections.OrderedSet<Symbol>();
        Type[]? indexTypes = null;
        var isUnion = (containingType._flags & TypeFlags.Union) != 0;
        // Flags we want to propagate to the result if they exist in all source symbols
        var checkFlags = CheckFlags.None;
        var optionalFlag = SymbolFlags.None;
        if (!isUnion)
        {
            checkFlags = CheckFlags.Readonly;
            optionalFlag = SymbolFlags.Optional;
        }
        var syntheticFlag = CheckFlags.SyntheticMethod;
        var mergedInstantiations = false;
        foreach (var current in containingType.Types())
        {
            var t = GetApparentTypeWorker(current);
            if (!IsErrorType(t) && (t._flags & TypeFlags.Never) == 0)
            {
                var prop = GetPropertyOfTypeEx(t, name, skipObjectFunctionPropertyAugment, false /*includeTypeOnlyMembers*/);
                var modifiers = ModifierFlags.None;
                if (prop != null)
                {
                    modifiers = GetDeclarationModifierFlagsFromSymbol(prop);
                    var writeModifiers = GetDeclarationModifierFlagsFromSymbolEx(prop, true /*isWrite*/);
                    if ((prop.Flags & SymbolFlags.ClassMember) != 0)
                    {
                        if (isUnion)
                        {
                            optionalFlag |= prop.Flags & SymbolFlags.Optional;
                        }
                        else
                        {
                            optionalFlag &= prop.Flags;
                        }
                    }
                    if (singleProp == null)
                    {
                        singleProp = prop;
                        propFlags = OrElse(prop.Flags & SymbolFlags.Accessor, SymbolFlags.Property);
                    }
                    else if (prop != singleProp)
                    {
                        var isInstantiation = GetTargetSymbolWorker(prop) == GetTargetSymbolWorker(singleProp);
                        // If the symbols are instances of one another with identical types - consider the symbols
                        // equivalent and just use the first one, which thus allows us to avoid eliding private
                        // members when intersecting a (this-)instantiations of a class with its raw base or another instance
                        if (isInstantiation && CompareProperties(singleProp, prop, (s, tp) => CompareTypesEqual(s, tp)) == Ternary.True)
                        {
                            // If we merged instantiations of a generic type, we replicate the symbol parent resetting behavior we used
                            // to do when we recorded multiple distinct symbols so that we still get, eg, `Array<T>.length` printed
                            // back and not `Array<string>.length` when we're looking at a `.length` access on a `string[] | number[]`
                            mergedInstantiations = singleProp.Parent != null && GetLocalTypeParametersOfClassOrInterfaceOrTypeAliasWorker(singleProp.Parent).Length != 0;
                        }
                        else
                        {
                            if (propSet.Size() == 0)
                            {
                                propSet.Add(singleProp);
                            }
                            propSet.Add(prop);
                        }
                        // classes created by mixins are represented as intersections
                        // and overriding a property in a derived class redefines it completely at runtime
                        // so a get accessor can't be merged with a set accessor in a base class,
                        // for that reason the accessor flags are only used when they are the same in all constituents
                        if ((propFlags & SymbolFlags.Accessor) != 0 && (prop.Flags & SymbolFlags.Accessor) != (propFlags & SymbolFlags.Accessor))
                        {
                            propFlags = propFlags & ~SymbolFlags.Accessor | SymbolFlags.Property;
                        }
                    }
                    if (isUnion && IsReadonlySymbolWorker(prop))
                    {
                        checkFlags |= CheckFlags.Readonly;
                    }
                    else if (!isUnion && !IsReadonlySymbolWorker(prop))
                    {
                        checkFlags &= ~CheckFlags.Readonly;
                    }
                    if ((modifiers & ModifierFlags.Protected) != 0 && (modifiers & ModifierFlags.Public) == 0)
                    {
                        checkFlags |= CheckFlags.ContainsProtected;
                    }
                    else if ((modifiers & ModifierFlags.Private) != 0 && (modifiers & ModifierFlags.Public) == 0)
                    {
                        checkFlags |= CheckFlags.ContainsPrivate;
                    }
                    else
                    {
                        checkFlags |= CheckFlags.ContainsPublic;
                    }
                    if ((writeModifiers & ModifierFlags.Protected) != 0 && (writeModifiers & ModifierFlags.Public) == 0)
                    {
                        checkFlags |= CheckFlags.ContainsWriteProtected;
                    }
                    else if ((writeModifiers & ModifierFlags.Private) != 0 && (writeModifiers & ModifierFlags.Public) == 0)
                    {
                        checkFlags |= CheckFlags.ContainsWritePrivate;
                    }
                    else
                    {
                        checkFlags |= CheckFlags.ContainsWritePublic;
                    }
                    if ((modifiers & ModifierFlags.Static) != 0)
                    {
                        checkFlags |= CheckFlags.ContainsStatic;
                    }
                    if (!IsPrototypeProperty(prop))
                    {
                        syntheticFlag = CheckFlags.SyntheticProperty;
                    }
                }
                else if (isUnion)
                {
                    IndexInfo? indexInfo = null;
                    if (!IsLateBoundName(name))
                    {
                        indexInfo = GetApplicableIndexInfoForName(t, name);
                    }
                    if (indexInfo != null)
                    {
                        propFlags = propFlags & ~SymbolFlags.Accessor | SymbolFlags.Property;
                        checkFlags |= CheckFlags.WritePartial | IfElse(indexInfo._isReadonly, CheckFlags.Readonly, CheckFlags.None);
                        if (IsTupleType(t))
                        {
                            var indexType = GetRestTypeOfTupleType(t);
                            if (indexType == null)
                            {
                                indexType = _undefinedType;
                            }
                            indexTypes = Concatenate(indexTypes, new[] { indexType });
                        }
                        else
                        {
                            indexTypes = Concatenate(indexTypes, new[] { indexInfo._valueType! });
                        }
                    }
                    else if (IsObjectLiteralType(t) && (t._objectFlags & ObjectFlags.ContainsSpread) == 0)
                    {
                        checkFlags |= CheckFlags.WritePartial;
                        indexTypes = Concatenate(indexTypes, new[] { _undefinedType });
                    }
                    else
                    {
                        checkFlags |= CheckFlags.ReadPartial;
                    }
                }
            }
        }
        if (singleProp == null)
        {
            // No property was found
            return null;
        }
        if (isUnion &&
            (propSet.Size() != 0 || (checkFlags & CheckFlags.Partial) != 0) &&
            (checkFlags & (CheckFlags.ContainsPrivate | CheckFlags.ContainsProtected | CheckFlags.ContainsWritePrivate | CheckFlags.ContainsWriteProtected)) != 0 &&
            !(propSet.Size() != 0 && HasCommonDeclaration(propSet)))
        {
            // A property in a union has a private or protected declaration in one constituent, but is missing
            // or has a different declaration in another constituent. If the private or protected declaration is
            // for reading, we don't create a property.
            if ((checkFlags & (CheckFlags.ContainsPrivate | CheckFlags.ContainsProtected)) != 0)
            {
                return null;
            }
            // Otherwise, if the private or protected declaration is for writing, reduce accessibility to that of
            // the most restricted constituent.
            if ((checkFlags & CheckFlags.ContainsWritePrivate) != 0)
            {
                checkFlags &= ~(CheckFlags.ContainsWritePublic | CheckFlags.ContainsWriteProtected);
            }
            else if ((checkFlags & CheckFlags.ContainsWriteProtected) != 0)
            {
                checkFlags &= ~CheckFlags.ContainsWritePublic;
            }
        }
        if (propSet.Size() == 0 && (checkFlags & CheckFlags.ReadPartial) == 0 && (indexTypes?.Length ?? 0) == 0)
        {
            if (!mergedInstantiations)
            {
                return singleProp;
            }
            // No symbol from a union/intersection should have a `.parent` set (since unions/intersections don't act as symbol parents)
            // Unless that parent is "reconstituted" from the "first value declaration" on the symbol (which is likely different than its instantiated parent!)
            // They also have a `.containingType` set, which affects some services endpoints behavior, like `getRootSymbol`
            Type? singlePropType = null;
            TypeMapper? singlePropMapper = null;
            if ((singleProp.Flags & SymbolFlags.Transient) != 0)
            {
                var links = _valueSymbolLinks.Get(singleProp);
                singlePropType = links._resolvedType;
                singlePropMapper = links._mapper;
            }
            var clone = CreateSymbolWithType(singleProp, singlePropType!);
            if (singleProp.ValueDeclaration != null)
            {
                clone.Parent = singleProp.ValueDeclaration.Symbol!.Parent;
            }
            var cloneLinks = _valueSymbolLinks.Get(clone);
            cloneLinks._containingType = containingType;
            cloneLinks._mapper = singlePropMapper;
            cloneLinks._writeType = GetWriteTypeOfSymbol(singleProp);
            return clone;
        }
        if (propSet.Size() == 0)
        {
            propSet.Add(singleProp);
        }
        Node[]? declarations = null;
        Type? firstType = null;
        Type? nameType = null;
        Type[]? propTypes = null;
        Type[]? writeTypes = null;
        Node? firstValueDeclaration = null;
        var hasNonUniformValueDeclaration = false;
        foreach (var prop in propSet.Values())
        {
            if (firstValueDeclaration == null)
            {
                firstValueDeclaration = prop.ValueDeclaration;
            }
            else if (prop.ValueDeclaration != null && prop.ValueDeclaration != firstValueDeclaration)
            {
                hasNonUniformValueDeclaration = true;
            }
            foreach (var declaration in prop.Declarations ?? [])
            {
                declarations = AppendIfUnique(declarations, declaration);
            }
            var t = GetTypeOfSymbolWorker(prop);
            if (firstType == null)
            {
                firstType = t;
                nameType = _valueSymbolLinks.Get(prop)._nameType;
            }
            var writeType = GetWriteTypeOfSymbol(prop);
            if (writeTypes != null || writeType != t)
            {
                if (writeTypes == null)
                {
                    writeTypes = Concatenate(propTypes, new[] { writeType });
                }
                else
                {
                    writeTypes = Concatenate(writeTypes, new[] { writeType });
                }
            }
            if (t != firstType)
            {
                checkFlags |= CheckFlags.HasNonUniformType;
            }
            if (IsLiteralType(t) || IsPatternLiteralType(t))
            {
                checkFlags |= CheckFlags.HasLiteralType;
            }
            if ((t._flags & TypeFlags.Never) != 0 && t != _uniqueLiteralType)
            {
                checkFlags |= CheckFlags.HasNeverType;
            }
            propTypes = Concatenate(propTypes, new[] { t });
        }
        propTypes = Concatenate(propTypes, indexTypes);
        var result = NewSymbolEx(propFlags | optionalFlag, name, checkFlags | syntheticFlag);
        result.Declarations = declarations;
        if (!hasNonUniformValueDeclaration && firstValueDeclaration != null)
        {
            result.ValueDeclaration = firstValueDeclaration;
            // Inherit information about parent type.
            result.Parent = firstValueDeclaration.Symbol!.Parent;
        }
        var resultLinks = _valueSymbolLinks.Get(result);
        resultLinks._containingType = containingType;
        resultLinks._nameType = nameType;
        if (propTypes!.Length > 2)
        {
            // When `propTypes` has the potential to explode in size when normalized, defer normalization until absolutely needed
            result.CheckFlags |= CheckFlags.DeferredType;
            var deferred = _deferredSymbolLinks.Get(result);
            deferred._parent = containingType;
            deferred._constituents = propTypes;
            deferred._writeConstituents = writeTypes;
            return result;
        }
        if (isUnion)
        {
            resultLinks._resolvedType = GetUnionTypeWorker(propTypes!);
        }
        else
        {
            resultLinks._resolvedType = GetIntersectionType(propTypes!);
        }
        if (writeTypes != null)
        {
            if (isUnion)
            {
                resultLinks._writeType = GetUnionTypeWorker(writeTypes);
            }
            else
            {
                resultLinks._writeType = GetIntersectionType(writeTypes);
            }
        }
        return result;
    }
}
