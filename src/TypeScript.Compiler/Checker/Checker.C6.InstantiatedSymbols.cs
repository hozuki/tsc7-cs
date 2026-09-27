// ported from: internal/checker/checker.go:20953-21449
// （instantiateSignature … isReadonlyArraySymbol：签名/索引 info/符号表实例化、thisless 判定、
//   默认构造签名、映射类型成员解析、union 成员解析）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `func (c *Checker) x()` → 实例方法（PascalCase；Go 未导出者 `internal`）；Go 的**包级函数**
//    （无接收者：isThisless*、getBaseTypeNodeOfClass、forEachType、someType、getMappedTypeModifiers）→
//    同一个 `public sealed partial class Checker` 的 `internal static` 成员（docs/checker-plan.md §5.2）。
// 2) 命名：Go 未导出字段 → `_camelCase`（`sig.typeParameters` → `sig._typeParameters`、`links.resolvedType`
//    → `links._resolvedType`、`c.errorType` → `_errorType`、`d.target/_mapper/_indexInfos/_signatures/
//    _callSignatureCount`、`mappedLinks.keyType` → `mappedLinks._keyType`）；Go 导出字段 → 原名 PascalCase
//    （`symbol.Flags`/`symbol.CheckFlags`/`symbol.Declarations`/`symbol.Members`/`p.Name`）；枚举成员名 = Go
//    常量名去类型前缀。本窗口内无同名冲突（未加 Worker），调用别的未导出成员时用改后名：
//    GetSignaturesOfTypeWorker / GetIndexInfosOfTypeWorker / GetTypeOfSymbolWorker / GetPropertyOfTypeWorker /
//    GetPropertiesOfTypeWorker / GetBaseConstructorTypeOfClassWorker / GetMinTypeArgumentCountWorker /
//    FillMissingTypeArgumentsWorker / IsReadonlySymbolWorker / RemoveMissingOrUndefinedTypeWorker /
//    SymbolToStringWorker / GetMergedSymbolWorker / GetDeclaredTypeOfSymbolWorker / GetApparentTypeWorker /
//    GetUnionTypeWorker；`c.TypeToString` 是 Go 的**导出**方法（printer.go:43，未导出的 `typeToString` 才是 Worker）
//    → C# 的 `TypeToString(t)`。
// 3) Go 的 map 读缺失键（`members[...]`、`symbol.Members[...]`）返回零值 → C# 用 `?.GetValueOrDefault(...)`
//    （缺失 → null）；写/增仍用索引器（Go 的 SymbolTable 是 map）。
// 4) `maps.Clone(members)` → 显式浅拷贝（`SymbolTable` 无拷贝构造，`Core.CopyMapInto` 返回基类 `Dictionary<K,V>`，
//    不能直接赋给 `SymbolTable` 变量）；`slices.Collect(maps.Values(members))` → `new List<Symbol>(members.Values).ToArray()`
//    （Go 的 map 遍历顺序本就随机）。
// 5) Go 的闭包 `addMemberForKeyTypeWorker`/`addMemberForKeyType` → C# 局部函数（同名 PascalCase）；其中 Go 的 lambda
//    形参 `t` 遮蔽外层 `t`，C# 禁止局部变量遮蔽 → 形参改名 `constituent`（仅此一处改名）。局部函数可写捕获的
//    `members`/`indexInfos`，与 Go 闭包语义一致。
// 6) `nil` → `null`；`sig.flags &^= X` → `sig._flags &= ~X`；Go 的 `switch { case cond: }` → `if/else if`；
//    `core.IfElse`/`core.OrElse`/`core.Every`/`core.SameMap`/`core.Concatenate` 用 `using static` 后的裸名。
// 7) `var result []*Signature` + `append` → `List<Signature>` + `ToArray()`（空结果 Go 是 nil、C# 是空数组，
//    `len`/`Length` 语义一致）；`getArrayMemberCallSignatures` 的 `return nil` → `return []`。
// 8) `c.couldContainTypeVariables(...)` 是 Go 的 **func 类型字段**（checker.go:887，不是方法）→ C# 的
//    `_couldContainTypeVariables(...)`（`Func<Type,bool>` 字段，见 Checker.Struct.cs）。
// 9) `getTypeOfMappedSymbol` 里 Go 的 `if links.resolvedType == nil { links.resolvedType = x }` → `??=`。
// 10) `getLowerBoundOfKeyType` 的 Index 分支里 Go 用 `t := ...` 遮蔽参数 → C# 改名 `target`（C# 禁止遮蔽；
//    该分支内 Go 也不再使用参数 `t`）。
// 11) 可空性：`getUnionSignatures` 返回可空 → `len(...) == 0` 写成 `callSignatures == null || callSignatures.Length == 0`；
//    `mapTypeEx` 返回可空 → 作为本方法返回值处加 `!`；`_globalArrayType._symbol`/`_globalReadonlyArrayType._symbol`
//    在 `isArrayOrTupleSymbol`/`isReadonlyArraySymbol` 里先取到局部变量再判空（等价于 Go 的重复解引用）。
namespace TypeScript.Checker;

using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;
using static TypeScript.Diagnostics.Diagnostics;

// 消歧（探针实测）：usings 在 file-scoped namespace 之前时，`Core.X` 会先在外层命名空间链里命中
// **命名空间** TypeScript.Core（CS0234）。本命名空间作用域内的 using 别名优先于外层命名空间 →
// `Core.Map/OrElse/IfElse/...` 绑到静态类；`using static TypeScript.Core.Core;` 的裸名调用不受影响。
using Core = TypeScript.Core.Core;

public sealed partial class Checker
{
    // Go: checker.go:20953
    internal Signature InstantiateSignature(Signature sig, TypeMapper m)
    {
        return InstantiateSignatureEx(sig, m, m == _permissiveMapper /*eraseTypeParameters*/);
    }

    // Go: checker.go:20957
    internal Signature InstantiateSignatureEx(Signature sig, TypeMapper m, bool eraseTypeParameters)
    {
        Type[]? freshTypeParameters = null;
        if (sig._typeParameters is { Length: > 0 } && !eraseTypeParameters)
        {
            // First create a fresh set of type parameters, then include a mapping from the old to the
            // new type parameters in the mapper function. Finally store this mapper in the new type
            // parameters such that we can use it when instantiating constraints.
            freshTypeParameters = Core.Map(sig._typeParameters, CloneTypeParameter);
            m = CombineTypeMappers(NewTypeMapper(sig._typeParameters, freshTypeParameters!), m);
            foreach (var tp in freshTypeParameters)
            {
                tp.AsTypeParameter()!._mapper = m;
            }
        }
        // Don't compute resolvedReturnType and resolvedTypePredicate now,
        // because using `mapper` now could trigger inferences to become fixed. (See `createInferenceContext`.)
        // See GH#17600.
        var result = NewSignature(sig._flags & SignatureFlags.PropagatingFlags, sig._declaration!, freshTypeParameters!, InstantiateSymbol(sig._thisParameter, m), InstantiateSymbols(sig._parameters!, m)!, null /*resolvedReturnType*/, null /*resolvedTypePredicate*/, sig._minArgumentCount);
        result._target = sig;
        result._mapper = m;
        return result;
    }

    // Go: checker.go:20977
    internal IndexInfo InstantiateIndexInfo(IndexInfo info, TypeMapper m)
    {
        var newValueType = InstantiateType(info._valueType!, m);
        if (newValueType == info._valueType)
        {
            return info;
        }
        return NewIndexInfo(info._keyType!, newValueType, info._isReadonly, info._declaration, info._components);
    }

    // Go: checker.go:20985
    internal void ResolveAnonymousTypeMembers(Type t)
    {
        var d = t.AsObjectType()!;
        if (d._target != null)
        {
            SetStructuredTypeMembers(t, null, null, null, null);
            var members = CreateInstantiatedSymbolTable(GetPropertiesOfObjectType(d._target), d._mapper!);
            var callSignatures = InstantiateSignatures(GetSignaturesOfTypeWorker(d._target, SignatureKind.Call)!, d._mapper!);
            var constructSignatures = InstantiateSignatures(GetSignaturesOfTypeWorker(d._target, SignatureKind.Construct)!, d._mapper!);
            var indexInfos = InstantiateIndexInfos(GetIndexInfosOfTypeWorker(d._target), d._mapper!);
            SetStructuredTypeMembers(t, members, callSignatures, constructSignatures, indexInfos);
            return;
        }
        var symbol = GetMergedSymbolWorker(t._symbol!)!;
        if ((symbol.Flags & SymbolFlags.TypeLiteral) != 0)
        {
            SetStructuredTypeMembers(t, null, null, null, null);
            var members = GetMembersOfSymbol(symbol);
            var callSignatures = GetSignaturesOfSymbol(members?.GetValueOrDefault(InternalSymbolNameCall));
            var constructSignatures = GetSignaturesOfSymbol(members?.GetValueOrDefault(InternalSymbolNameNew));
            var indexInfos = GetIndexInfosOfSymbol(symbol);
            SetStructuredTypeMembers(t, members, callSignatures, constructSignatures, indexInfos);
            return;
        }
        // Combinations of function, class, enum and module
        var membersOfSymbol = GetExportsOfSymbol(symbol);
        IndexInfo[]? indexInfos2 = null;
        if (symbol == _globalThisSymbol)
        {
            var varsOnly = new SymbolTable();
            if (membersOfSymbol != null)
            {
                foreach (var p in membersOfSymbol.Values)
                {
                    if ((p.Flags & SymbolFlags.BlockScoped) == 0 && !((p.Flags & SymbolFlags.ValueModule) != 0 && p.Declarations is { Length: > 0 } && Every(p.Declarations, IsAmbientModule)))
                    {
                        varsOnly[p.Name] = p;
                    }
                }
            }
            membersOfSymbol = varsOnly;
        }
        IndexInfo? baseConstructorIndexInfo = null;
        SetStructuredTypeMembers(t, membersOfSymbol, null, null, null);
        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var classType = GetDeclaredTypeOfClassOrInterface(symbol);
            var baseConstructorType = GetBaseConstructorTypeOfClassWorker(classType);
            if ((baseConstructorType._flags & (TypeFlags.Object | TypeFlags.Intersection | TypeFlags.TypeVariable)) != 0)
            {
                // Go: members = maps.Clone(members)（SymbolTable 无拷贝构造；`Core.CopyMapInto` 返回基类 Dictionary<K,V>）
                if (membersOfSymbol != null)
                {
                    var clonedMembers = new SymbolTable();
                    foreach (var kv in membersOfSymbol)
                    {
                        clonedMembers[kv.Key] = kv.Value;
                    }
                    membersOfSymbol = clonedMembers;
                }
                AddInheritedMembers(membersOfSymbol, GetPropertiesOfTypeWorker(baseConstructorType));
                SetStructuredTypeMembers(t, membersOfSymbol, null, null, null);
            }
            else if (baseConstructorType == _anyType)
            {
                baseConstructorIndexInfo = _anyBaseTypeIndexInfo;
            }
        }
        var indexSymbol = membersOfSymbol?.GetValueOrDefault(InternalSymbolNameIndex);
        if (indexSymbol != null)
        {
            indexInfos2 = GetIndexInfosOfIndexSymbol(indexSymbol, new List<Symbol>(membersOfSymbol!.Values).ToArray());
        }
        else
        {
            if (baseConstructorIndexInfo != null)
            {
                indexInfos2 = Concatenate(indexInfos2, new[] { baseConstructorIndexInfo });
            }
            if ((symbol.Flags & SymbolFlags.Enum) != 0 && ((GetDeclaredTypeOfSymbolWorker(symbol)._flags & TypeFlags.Enum) != 0 || Some(d.Properties(), prop => (GetTypeOfSymbolWorker(prop)._flags & TypeFlags.NumberLike) != 0)))
            {
                indexInfos2 = Concatenate(indexInfos2, new[] { _enumNumberIndexInfo });
            }
        }
        d._indexInfos = indexInfos2;
        // We resolve the members before computing the signatures because a signature may use
        // typeof with a qualified name expression that circularly references the type we are
        // in the process of resolving (see issue #6072). The temporarily empty signature list
        // will never be observed because a qualified name can't reference signatures.
        if ((symbol.Flags & (SymbolFlags.Function | SymbolFlags.Method)) != 0)
        {
            d._signatures = GetSignaturesOfSymbol(symbol);
            d._callSignatureCount = d._signatures.Length;
        }
        // And likewise for construct signatures for classes
        if ((symbol.Flags & SymbolFlags.Class) != 0)
        {
            var classType2 = GetDeclaredTypeOfClassOrInterface(symbol);
            var constructSignatures = GetSignaturesOfSymbol(symbol.Members?.GetValueOrDefault(InternalSymbolNameConstructor));
            if (constructSignatures.Length == 0)
            {
                constructSignatures = GetDefaultConstructSignatures(classType2);
            }
            d._signatures = Concatenate(d._signatures, constructSignatures);
        }
    }

    // Go: checker.go:21050
    internal SymbolTable? CreateInstantiatedSymbolTable(Symbol[]? symbols, TypeMapper m)
    {
        if (symbols == null || symbols.Length == 0)
        {
            return null;
        }
        var result = new SymbolTable();
        foreach (var symbol in symbols)
        {
            result[symbol.Name] = InstantiateSymbol(symbol, m)!;
        }
        return result;
    }

    // Go: checker.go:21061
    internal SymbolTable? InstantiateSymbolTable(SymbolTable? symbols, TypeMapper m)
    {
        if (symbols == null || symbols.Count == 0)
        {
            return null;
        }
        var result = new SymbolTable();
        foreach (var kv in symbols)
        {
            if (IsNamedMember(kv.Value, kv.Key))
            {
                result[kv.Key] = InstantiateSymbol(kv.Value, m)!;
            }
        }
        return result;
    }

    // Go: checker.go:21074
    internal Symbol? InstantiateSymbol(Symbol? symbol, TypeMapper? m)
    {
        if (symbol == null)
        {
            return null;
        }
        var links = _valueSymbolLinks.Get(symbol);
        if (m != null && m.MapsThisOnly() && IsThisless(symbol))
        {
            return symbol;
        }
        // If the type of the symbol is already resolved, and if that type could not possibly
        // be affected by instantiation, simply return the symbol itself.
        if (links._resolvedType != null && !_couldContainTypeVariables(links._resolvedType))
        {
            if ((symbol.Flags & SymbolFlags.SetAccessor) == 0)
            {
                return symbol;
            }
            // If we're a setter, check writeType.
            if (links._writeType != null && !_couldContainTypeVariables(links._writeType))
            {
                return symbol;
            }
        }
        if ((symbol.CheckFlags & CheckFlags.Instantiated) != 0)
        {
            // If symbol being instantiated is itself a instantiation, fetch the original target and combine the
            // type mappers. This ensures that original type identities are properly preserved and that aliases
            // always reference a non-aliases.
            symbol = links._target!;
            m = CombineTypeMappers(links._mapper, m!);
        }
        // Keep the flags from the symbol we're instantiating.  Mark that is instantiated, and
        // also transient so that we can just store data on it directly.
        var result = NewSymbol(symbol.Flags, symbol.Name);
        result.CheckFlags = CheckFlags.Instantiated | (symbol.CheckFlags & (CheckFlags.Readonly | CheckFlags.Late | CheckFlags.OptionalParameter | CheckFlags.RestParameter));
        result.Declarations = symbol.Declarations;
        result.Parent = symbol.Parent;
        result.ValueDeclaration = symbol.ValueDeclaration;
        var resultLinks = _valueSymbolLinks.Get(result);
        resultLinks._target = symbol;
        resultLinks._mapper = m;
        resultLinks._nameType = links._nameType;
        return result;
    }

    // Returns true if the parameter or class/interface member given by the symbol is free of "this" references. The
    // function may return false for symbols that are actually free of "this" references because it is not
    // feasible to perform a complete analysis in all cases. In particular, property members with types
    // inferred from their initializers and function members with inferred return types are conservatively
    // assumed not to be free of "this" references.
    //
    // Go: checker.go:21116（包级函数 → static 成员）
    internal static bool IsThisless(Symbol symbol)
    {
        if (symbol.Declarations is { Length: 1 })
        {
            var declaration = symbol.Declarations[0];
            if (declaration != null)
            {
                switch (declaration.Kind)
                {
                    case Kind.Parameter:
                        return IsThislessVariableLikeDeclaration(declaration);
                    case Kind.PropertyDeclaration:
                    case Kind.PropertySignature:
                        return IsThislessVariableLikeDeclaration(declaration);
                    case Kind.MethodDeclaration:
                    case Kind.MethodSignature:
                    case Kind.Constructor:
                    case Kind.GetAccessor:
                    case Kind.SetAccessor:
                        return IsThislessFunctionLikeDeclaration(declaration);
                }
            }
        }
        return false;
    }

    // A variable-like declaration is free of this references if it has a type annotation
    // that is thisless, or if it has no type annotation and no initializer (and is thus of type any).
    //
    // Go: checker.go:21134（包级函数 → static 成员）
    internal static bool IsThislessVariableLikeDeclaration(Node node)
    {
        var typeNode = node.Type();
        if (typeNode != null)
        {
            return IsThislessType(typeNode);
        }
        return node.Initializer() == null;
    }

    // A type is free of this references if it's the any, string, number, boolean, symbol, or void keyword, a string
    // literal type, an array with an element type that is free of this references, or a type reference that is
    // free of this references.
    //
    // Go: checker.go:21142（包级函数 → static 成员）
    internal static bool IsThislessType(Node node)
    {
        switch (node.Kind)
        {
            case Kind.AnyKeyword:
            case Kind.UnknownKeyword:
            case Kind.StringKeyword:
            case Kind.NumberKeyword:
            case Kind.BigIntKeyword:
            case Kind.BooleanKeyword:
            case Kind.SymbolKeyword:
            case Kind.ObjectKeyword:
            case Kind.VoidKeyword:
            case Kind.UndefinedKeyword:
            case Kind.NeverKeyword:
            case Kind.LiteralType:
                return true;
            case Kind.ArrayType:
                return IsThislessType(node.AsArrayTypeNode()!.ElementType!);
            case Kind.TypeReference:
                return Every(node.TypeArguments(), IsThislessType);
        }
        return false;
    }

    // A function-like declaration is considered free of `this` references if it has a return type
    // annotation that is free of this references and if each parameter is thisless and if
    // each type parameter (if present) is thisless.
    //
    // Go: checker.go:21160（包级函数 → static 成员）
    internal static bool IsThislessFunctionLikeDeclaration(Node node)
    {
        var returnType = node.Type();
        return (IsConstructorDeclaration(node) || (returnType != null && IsThislessType(returnType))) &&
            Every(node.Parameters(), IsThislessVariableLikeDeclaration) &&
            Every(node.TypeParameters(), IsThislessTypeParameter);
    }

    // A type parameter is thisless if its constraint is thisless, or if it has no constraint. */
    //
    // Go: checker.go:21168（包级函数 → static 成员）
    internal static bool IsThislessTypeParameter(Node node)
    {
        var constraint = node.AsTypeParameterDeclaration()!.Constraint;
        return constraint == null || IsThislessType(constraint);
    }

    // Go: checker.go:21173
    internal Signature[] GetDefaultConstructSignatures(Type classType)
    {
        var baseConstructorType = GetBaseConstructorTypeOfClassWorker(classType);
        var baseSignatures = GetSignaturesOfTypeWorker(baseConstructorType, SignatureKind.Construct) ?? [];
        var declaration = GetClassLikeDeclarationOfSymbol(classType._symbol!);
        var isAbstract = declaration != null && HasSyntacticModifier(declaration, ModifierFlags.Abstract);
        if (baseSignatures.Length == 0)
        {
            var flags = IfElse(isAbstract, SignatureFlags.Construct | SignatureFlags.Abstract, SignatureFlags.Construct);
            return new[] { NewSignature(flags, null!, classType.AsInterfaceType()!.LocalTypeParameters(), null, null!, classType, null, 0) };
        }
        var baseTypeNode = GetBaseTypeNodeOfClass(classType);
        var isJavaScript = declaration != null && IsInJSFile(declaration);
        var typeArguments = GetTypeArgumentsFromNode(baseTypeNode!);
        var typeArgCount = typeArguments.Length;
        var result = new List<Signature>();
        foreach (var baseSig in baseSignatures)
        {
            var minTypeArgumentCount = GetMinTypeArgumentCountWorker(baseSig._typeParameters ?? []);
            var typeParamCount = baseSig._typeParameters?.Length ?? 0;
            if (isJavaScript || (typeArgCount >= minTypeArgumentCount && typeArgCount <= typeParamCount))
            {
                Signature sig;
                if (typeParamCount != 0)
                {
                    sig = CreateSignatureInstantiation(baseSig, FillMissingTypeArgumentsWorker(typeArguments, baseSig._typeParameters!, minTypeArgumentCount, isJavaScript));
                }
                else
                {
                    sig = CloneSignature(baseSig);
                }
                sig._typeParameters = classType.AsInterfaceType()!.LocalTypeParameters();
                sig._resolvedReturnType = classType;
                if (isAbstract)
                {
                    sig._flags |= SignatureFlags.Abstract;
                }
                else
                {
                    sig._flags &= ~SignatureFlags.Abstract;
                }
                result.Add(sig);
            }
        }
        return result.ToArray();
    }

    // Go: checker.go:21213
    internal void ResolveMappedTypeMembers(Type t)
    {
        var members = new SymbolTable();
        IndexInfo[]? indexInfos = null;
        // Resolve upfront such that recursive references see an empty object type.
        SetStructuredTypeMembers(t, null, null, null, null);
        // In { [P in K]: T }, we refer to P as the type parameter type, K as the constraint type,
        // and T as the template type.
        var typeParameter = GetTypeParameterFromMappedType(t)!;
        var constraintType = GetConstraintTypeFromMappedType(t)!;
        var mappedType = OrElse(t.AsMappedType()!._target, t)!;
        var nameType = GetNameTypeFromMappedType(mappedType!);
        var shouldLinkPropDeclarations = GetMappedTypeNameTypeKind(mappedType!) != MappedTypeNameTypeKind.Remapping;
        var templateType = GetTemplateTypeFromMappedType(mappedType)!;
        var modifiersType = GetApparentTypeWorker(GetModifiersTypeFromMappedType(t));
        // The 'T' in 'keyof T'
        var templateModifiers = GetMappedTypeModifiers(t);
        var include = TypeFlags.StringOrNumberLiteralOrUnique;
        void AddMemberForKeyTypeWorker(Type keyType, Type propNameType)
        {
            // If the current iteration type constituent is a string literal type, create a property.
            // Otherwise, for type string create a string index signature.
            if (IsTypeUsableAsPropertyName(propNameType))
            {
                var propName = GetPropertyNameFromType(propNameType);
                // String enum members from separate enums with identical values
                // are distinct types with the same property name. Make the resulting
                // property symbol's name type be the union of those enum member types.
                var existingProp = members.GetValueOrDefault(propName);
                if (existingProp != null)
                {
                    var valueLinks = _valueSymbolLinks.Get(existingProp);
                    valueLinks._nameType = GetUnionTypeWorker(new Type[] { valueLinks._nameType!, propNameType });
                    var mappedLinks = _mappedSymbolLinks.Get(existingProp);
                    mappedLinks._keyType = GetUnionTypeWorker(new Type[] { mappedLinks._keyType!, keyType });
                }
                else
                {
                    Symbol? modifiersProp = null;
                    if (IsTypeUsableAsPropertyName(keyType))
                    {
                        modifiersProp = GetPropertyOfTypeWorker(modifiersType, GetPropertyNameFromType(keyType));
                    }
                    var isOptional = (templateModifiers & MappedTypeModifiers.IncludeOptional) != 0 || ((templateModifiers & MappedTypeModifiers.ExcludeOptional) == 0 && modifiersProp != null && (modifiersProp.Flags & SymbolFlags.Optional) != 0);
                    var isReadonly = (templateModifiers & MappedTypeModifiers.IncludeReadonly) != 0 || ((templateModifiers & MappedTypeModifiers.ExcludeReadonly) == 0 && modifiersProp != null && IsReadonlySymbolWorker(modifiersProp));
                    var stripOptional = _strictNullChecks && !isOptional && modifiersProp != null && (modifiersProp.Flags & SymbolFlags.Optional) != 0;
                    var lateFlag = modifiersProp != null ? modifiersProp.CheckFlags & CheckFlags.Late : CheckFlags.None;
                    var prop = NewSymbol(SymbolFlags.Property | IfElse(isOptional, SymbolFlags.Optional, SymbolFlags.None), propName);
                    prop.CheckFlags = lateFlag | CheckFlags.Mapped | IfElse(isReadonly, CheckFlags.Readonly, CheckFlags.None) | IfElse(stripOptional, CheckFlags.StripOptional, CheckFlags.None);
                    var valueLinks = _valueSymbolLinks.Get(prop);
                    valueLinks._containingType = t;
                    valueLinks._nameType = propNameType;
                    var mappedLinks = _mappedSymbolLinks.Get(prop);
                    mappedLinks._keyType = keyType;
                    if (modifiersProp != null)
                    {
                        mappedLinks._syntheticOrigin = modifiersProp;
                        if (shouldLinkPropDeclarations)
                        {
                            prop.Declarations = modifiersProp.Declarations;
                        }
                    }
                    members[propName] = prop;
                }
            }
            else if (IsValidIndexKeyType(propNameType) || (propNameType._flags & (TypeFlags.Any | TypeFlags.Enum)) != 0)
            {
                var indexKeyType = propNameType;
                if ((propNameType._flags & (TypeFlags.Any | TypeFlags.String)) != 0)
                {
                    indexKeyType = _stringType;
                }
                else if ((propNameType._flags & (TypeFlags.Number | TypeFlags.Enum)) != 0)
                {
                    indexKeyType = _numberType;
                }
                var propType = InstantiateType(templateType, AppendTypeMapping(t.AsMappedType()!._mapper, typeParameter, keyType));
                var modifiersIndexInfo = GetApplicableIndexInfo(modifiersType, propNameType);
                var isReadonly2 = (templateModifiers & MappedTypeModifiers.IncludeReadonly) != 0 || ((templateModifiers & MappedTypeModifiers.ExcludeReadonly) == 0 && modifiersIndexInfo != null && modifiersIndexInfo._isReadonly);
                var indexInfo = NewIndexInfo(indexKeyType, propType, isReadonly2, null, null);
                indexInfos = AppendIndexInfo(indexInfos, indexInfo, true /*union*/);
            }
        }
        void AddMemberForKeyType(Type keyType)
        {
            var propNameType = keyType;
            if (nameType != null)
            {
                propNameType = InstantiateType(nameType, AppendTypeMapping(t.AsMappedType()!._mapper, typeParameter, keyType));
            }
            ForEachType(propNameType, constituent => AddMemberForKeyTypeWorker(keyType, constituent));
        }
        if (IsMappedTypeWithKeyofConstraintDeclaration(t))
        {
            // We have a { [P in keyof T]: X }
            ForEachMappedTypePropertyKeyTypeAndIndexSignatureKeyType(modifiersType, include, false /*stringsOnly*/, AddMemberForKeyType);
        }
        else
        {
            ForEachType(GetLowerBoundOfKeyType(constraintType), AddMemberForKeyType);
        }
        SetStructuredTypeMembers(t, members, null, null, indexInfos);
    }

    // Go: checker.go:21288
    internal Type GetTypeOfMappedSymbol(Symbol symbol)
    {
        var links = _valueSymbolLinks.Get(symbol);
        if (links._resolvedType == null)
        {
            var mappedType = links._containingType!;
            if (!PushTypeResolution(symbol, TypeSystemPropertyName.Type))
            {
                mappedType.AsMappedType()!._containsError = true;
                return _errorType;
            }
            var templateType = GetTemplateTypeFromMappedType(OrElse(mappedType.AsMappedType()!._target, mappedType)!)!;
            var mapper = AppendTypeMapping(mappedType.AsMappedType()!._mapper, GetTypeParameterFromMappedType(mappedType)!, _mappedSymbolLinks.Get(symbol)._keyType!);
            var propType = InstantiateType(templateType, mapper);
            // When creating an optional property in strictNullChecks mode, if 'undefined' isn't assignable to the
            // type, we include 'undefined' in the type. Similarly, when creating a non-optional property in strictNullChecks
            // mode, if the underlying property is optional we remove 'undefined' from the type.
            if (_strictNullChecks && (symbol.Flags & SymbolFlags.Optional) != 0 && !MaybeTypeOfKind(propType, TypeFlags.Undefined | TypeFlags.Void))
            {
                propType = GetOptionalType(propType, true /*isProperty*/);
            }
            else if ((symbol.CheckFlags & CheckFlags.StripOptional) != 0)
            {
                propType = RemoveMissingOrUndefinedTypeWorker(propType);
            }
            if (PopTypeResolution())
            {
                links._resolvedType ??= propType;
            }
            else
            {
                links._resolvedType ??= _errorType;
                Error(_currentNode, Type_of_property_0_circularly_references_itself_in_mapped_type_1, SymbolToStringWorker(symbol), TypeToString(mappedType));
            }
        }
        return links._resolvedType!;
    }

    // Return the lower bound of the key type in a mapped type. Intuitively, the lower
    // bound includes those keys that are known to always be present, for example because
    // because of constraints on type parameters (e.g. 'keyof T' for a constrained T).
    //
    // Go: checker.go:21313
    internal Type GetLowerBoundOfKeyType(Type t)
    {
        if ((t._flags & TypeFlags.Index) != 0)
        {
            var target = GetApparentTypeWorker(t.AsIndexType()!._target!);
            if (IsGenericTupleType(target))
            {
                return GetKnownKeysOfTupleType(target);
            }
            return GetIndexType(target);
        }
        if ((t._flags & TypeFlags.Conditional) != 0)
        {
            var conditional = t.AsConditionalType()!;
            if (conditional._root!._isDistributive)
            {
                var checkType = conditional._checkType!;
                var constraint = GetLowerBoundOfKeyType(checkType);
                if (constraint != checkType)
                {
                    return GetConditionalTypeInstantiation(t, PrependTypeMapping(conditional._root!._checkType!, constraint, conditional._mapper), false /*forConstraint*/, null);
                }
            }
            return t;
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            return MapTypeEx(t, GetLowerBoundOfKeyType, true /*noReductions*/)!;
        }
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            // Similarly to getTypeFromIntersectionTypeNode, we preserve the special string & {}, number & {},
            // and bigint & {} intersections that are used to prevent subtype reduction in union types.
            var types = t.Types();
            if (types.Length == 2 && (types[0]._flags & (TypeFlags.String | TypeFlags.Number | TypeFlags.BigInt)) != 0 && types[1] == _emptyTypeLiteralType)
            {
                return t;
            }
            return GetIntersectionType(SameMap(t.Types(), GetLowerBoundOfKeyType));
        }
        return t;
    }

    // Go: checker.go:21343
    internal void ResolveUnionTypeMembers(Type t)
    {
        // The members and properties collections are empty for union types. To get all properties of a union
        // type use getPropertiesOfType (only the language service uses this).
        var callSignatures = GetUnionSignatures(Core.Map(t.Types(), memberType =>
        {
            if (memberType == _globalFunctionType)
            {
                return new[] { _unknownSignature };
            }
            return GetSignaturesOfTypeWorker(memberType, SignatureKind.Call)!;
        }));
        if (callSignatures == null || callSignatures.Length == 0)
        {
            callSignatures = GetArrayMemberCallSignatures(t);
        }
        var constructSignatures = GetUnionSignatures(Core.Map(t.Types(), memberType => GetSignaturesOfTypeWorker(memberType, SignatureKind.Construct)!));
        var indexInfos = GetUnionIndexInfos(t.Types());
        SetStructuredTypeMembers(t, null, callSignatures, constructSignatures, indexInfos);
    }

    // Go: checker.go:21363
    internal Signature[] GetArrayMemberCallSignatures(Type t)
    {
        // Check if union is exclusively instantiations of a member of the global Array or ReadonlyArray type.
        var memberName = "";
        var types = t.Types();
        for (var i = 0; i < types.Length; i++)
        {
            var constituent = types[i];
            if ((constituent._objectFlags & ObjectFlags.Instantiated) == 0 || constituent._symbol == null || constituent._symbol.Parent == null || !IsArrayOrTupleSymbol(constituent._symbol.Parent))
            {
                return [];
            }
            if (i == 0)
            {
                memberName = constituent._symbol.Name;
            }
            else if (memberName != constituent._symbol.Name)
            {
                return [];
            }
        }
        // Transform the type from `(A[] | B[])["member"]` to `(A | B)[]["member"]` (since we pretend array is covariant anyway).
        var arrayArg = MapType(t, constituent => GetMappedType(
            (IsReadonlyArraySymbol(constituent._symbol!.Parent!) ? _globalReadonlyArrayType : _globalArrayType).AsInterfaceType()!.TypeParameters()[0],
            constituent.Mapper()!));
        var arrayType = CreateArrayTypeEx(arrayArg, SomeType(t, constituent => IsReadonlyArraySymbol(constituent._symbol!.Parent!)));
        return GetSignaturesOfTypeWorker(GetTypeOfPropertyOfTypeWorker(arrayType, memberName)!, SignatureKind.Call) ?? [];
    }

    // Go: checker.go:21388
    internal bool IsArrayOrTupleSymbol(Symbol? symbol)
    {
        var globalArraySymbol = _globalArrayType._symbol;
        var globalReadonlyArraySymbol = _globalReadonlyArrayType._symbol;
        if (symbol == null || globalArraySymbol == null || globalReadonlyArraySymbol == null)
        {
            return false;
        }
        return GetSymbolIfSameReference(symbol, globalArraySymbol) != null || GetSymbolIfSameReference(symbol, globalReadonlyArraySymbol) != null;
    }

    // Go: checker.go:21395
    internal bool IsReadonlyArraySymbol(Symbol? symbol)
    {
        var globalReadonlyArraySymbol = _globalReadonlyArrayType._symbol;
        if (symbol == null || globalReadonlyArraySymbol == null)
        {
            return false;
        }
        return GetSymbolIfSameReference(symbol, globalReadonlyArraySymbol) != null;
    }
}
