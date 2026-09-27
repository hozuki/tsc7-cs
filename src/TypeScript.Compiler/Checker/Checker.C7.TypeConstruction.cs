// ported from: internal/checker/checker.go:25407-25890（类型对象构造 + 字面量类型/单元类型）
//
// 与 Go 的偏离（逐条）：
// 1) `TypeData` 层已折叠（docs/checker-plan.md §5.1）：`newType(flags, objectFlags, data TypeData)` 的参数/返回
//    由 `TypeData` 改成 `Type`；Go 的 `data.AsType()` → 该对象本身、`t.data = data`（Go 里只是把 data 存回 Type，
//    数据层合并后无对应字段）不再存在；其余逐行对应。各 `newXxxType` 里 `&X{}` → `new X { … }`。
// 2) `core.Arena[Signature]/[IndexInfo]` 不移植（docs/architecture.md §3.2）：`c.signatureArena.New()` → `new Signature()`、
//    `c.indexInfoArena.New()` → `new IndexInfo()`（调用点 checker.go:25653 / 25668）。
// 3) Go 未导出**字段** → `_camelCase`（`t.flags`→`t._flags`、`c.stringLiteralTypes`→`_stringLiteralTypes`…）；
//    Go 导出字段 → 原名（`TypeCount`/`SignatureCount`）。
// 4) `map[K]V` 读缺失键（Go 返回零值）→ `GetValueOrDefault`/`TryGetValue`（`Dictionary` 索引器会抛，§3.2）。
// 5) `slices.Clip(s)` → 直接返回 `s`（§3.2：C# 数组没有 len/cap 视图）；`len(xs)` → `(xs?.Length ?? 0)`（nil slice）。
// 6) `panic("Unhandled case in …")` → `Debug.Fail(...)` + `throw new System.Diagnostics.UnreachableException()`
//    （`Debug.Fail` 无 `[DoesNotReturn]`，先例 Checker.C3.Calls.cs）。
// 7) Go 的 package 级函数（无接收者）`isFreshLiteralType`/`getStringLiteralValue`/`getNumberLiteralValue`/
//    `getBigIntLiteralValue`/`getBooleanLiteralValue`/`isLiteralType`/`isNeitherUnitTypeNorNever`/`isUnitType`
//    → 本类 `internal static`（docs/checker-plan.md §5.2）。`getBaseTypeOfLiteralType` 与 exports.go 的导出同名
//    → 未导出者加 `Worker` 后缀（§2.1 权威清单 artifacts/checker-method-collisions.md）。
// 8) Go 的 `*T` 参数 → `T?`（§3.5）；Go 切片参数 → `T[]?`（null = Go 的 nil slice）。
// 9) Go 的 `int32(minArgumentCount)` 窄化 → C# 直接赋 `int`（`Signature._minArgumentCount` 是 `int`，Go 是 int32，
//    仅字段宽度不同，取值域一致）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.JsNum;
using static TypeScript.Core.Core;

public sealed partial class Checker
{
    /// <summary>Go: <c>func (c *Checker) newType(flags TypeFlags, objectFlags ObjectFlags, data TypeData) *Type</c>（checker.go:25407-25419）。</summary>
    internal Type NewType(TypeFlags flags, ObjectFlags objectFlags, Type data)
    {
        TypeCount++;
        var t = data;
        t._flags = flags;
        t._objectFlags = objectFlags & ~(ObjectFlags.CouldContainTypeVariablesComputed | ObjectFlags.CouldContainTypeVariables | ObjectFlags.MembersResolved);
        t._id = (TypeId)TypeCount;
        t._checker = this;
        if (_tracer != null)
        {
            _tracer.RecordType(t);
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) newIntrinsicType(flags TypeFlags, intrinsicName string) *Type</c>（checker.go:25421-25423）。</summary>
    internal Type NewIntrinsicType(TypeFlags flags, string intrinsicName)
    {
        return NewIntrinsicTypeEx(flags, intrinsicName, ObjectFlags.None);
    }

    /// <summary>Go: <c>func (c *Checker) newIntrinsicTypeEx(flags TypeFlags, intrinsicName string, objectFlags ObjectFlags) *Type</c>（checker.go:25425-25429）。</summary>
    internal Type NewIntrinsicTypeEx(TypeFlags flags, string intrinsicName, ObjectFlags objectFlags)
    {
        var data = new IntrinsicType();
        data._intrinsicName = intrinsicName;
        return NewType(flags, objectFlags, data);
    }

    /// <summary>Go: <c>func (c *Checker) createWideningType(nonWideningType *Type) *Type</c>（checker.go:25431-25438）。</summary>
    internal Type CreateWideningType(Type nonWideningType)
    {
        if (_strictNullChecks)
        {
            return nonWideningType;
        }
        var t = NewIntrinsicType(nonWideningType._flags, nonWideningType.AsIntrinsicType()!._intrinsicName);
        t._objectFlags |= ObjectFlags.ContainsWideningType;
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) createUnknownUnionType() *Type</c>（checker.go:25440-25445）。</summary>
    internal Type CreateUnknownUnionType()
    {
        if (_strictNullChecks)
        {
            return GetUnionTypeWorker(new Type[] { _undefinedType, _nullType, _unknownEmptyObjectType });
        }
        return _unknownType;
    }

    /// <summary>Go: <c>func (c *Checker) newLiteralType(flags TypeFlags, value any, regularType *Type) *Type</c>（checker.go:25447-25457）。</summary>
    internal Type NewLiteralType(TypeFlags flags, object? value, Type? regularType)
    {
        var data = new LiteralType();
        data._value = value;
        var t = NewType(flags, ObjectFlags.None, data);
        if (regularType != null)
        {
            data._regularType = regularType;
        }
        else
        {
            data._regularType = t;
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) newUniqueESSymbolType(symbol *ast.Symbol, name string) *Type</c>（checker.go:25459-25465）。</summary>
    internal Type NewUniqueESSymbolType(Symbol? symbol, string name)
    {
        var data = new UniqueESSymbolType();
        data._name = name;
        var t = NewType(TypeFlags.UniqueESSymbol, ObjectFlags.None, data);
        t._symbol = symbol;
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) newObjectType(objectFlags ObjectFlags, symbol *ast.Symbol) *Type</c>（checker.go:25467-25492）。</summary>
    internal Type NewObjectType(ObjectFlags objectFlags, Symbol? symbol)
    {
        Type data;
        if ((objectFlags & ObjectFlags.ClassOrInterface) != 0)
        {
            data = new InterfaceType();
        }
        else if ((objectFlags & ObjectFlags.Tuple) != 0)
        {
            data = new TupleType();
        }
        else if ((objectFlags & ObjectFlags.Reference) != 0)
        {
            data = new TypeReference();
        }
        else if ((objectFlags & ObjectFlags.Mapped) != 0)
        {
            data = new MappedType();
        }
        else if ((objectFlags & ObjectFlags.ReverseMapped) != 0)
        {
            data = new ReverseMappedType();
        }
        else if ((objectFlags & ObjectFlags.EvolvingArray) != 0)
        {
            data = new EvolvingArrayType();
        }
        else if ((objectFlags & ObjectFlags.InstantiationExpressionType) != 0)
        {
            data = new InstantiationExpressionType();
        }
        else if ((objectFlags & ObjectFlags.Anonymous) != 0)
        {
            data = new ObjectType();
        }
        else
        {
            Debug.Fail("Unhandled case in newObjectType");
            throw new System.Diagnostics.UnreachableException();
        }
        var t = NewType(TypeFlags.Object, objectFlags, data);
        t._symbol = symbol;
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) newAnonymousType(symbol *ast.Symbol, members ast.SymbolTable, callSignatures []*Signature, constructSignatures []*Signature, indexInfos []*IndexInfo) *Type</c>（checker.go:25494-25498）。</summary>
    internal Type NewAnonymousType(Symbol? symbol, SymbolTable? members, Signature[]? callSignatures, Signature[]? constructSignatures, IndexInfo[]? indexInfos)
    {
        var t = NewObjectType(ObjectFlags.Anonymous, symbol);
        SetStructuredTypeMembers(t, members, callSignatures, constructSignatures, indexInfos);
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) tryCreateTypeReference(target *Type, typeArguments []*Type) *Type</c>（checker.go:25500-25505）。</summary>
    internal Type TryCreateTypeReference(Type target, Type[]? typeArguments)
    {
        if ((typeArguments?.Length ?? 0) != 0 && target == _emptyGenericType)
        {
            return _unknownType;
        }
        return CreateTypeReference(target, typeArguments);
    }

    /// <summary>Go: <c>func (c *Checker) createTypeReference(target *Type, typeArguments []*Type) *Type</c>（checker.go:25507-25509）。</summary>
    internal Type CreateTypeReference(Type target, Type[]? typeArguments)
    {
        return CreateTypeReferenceEx(target, typeArguments, ObjectFlags.None);
    }

    /// <summary>Go: <c>func (c *Checker) createTypeReferenceEx(target *Type, typeArguments []*Type, objectFlags ObjectFlags) *Type</c>（checker.go:25511-25523）。</summary>
    internal Type CreateTypeReferenceEx(Type target, Type[]? typeArguments, ObjectFlags objectFlags)
    {
        var id = GetTypeListKey(typeArguments);
        var intf = target.AsInterfaceType()!;
        if (intf._instantiations!.TryGetValue(id, out var existing))
        {
            return existing;
        }
        var t = NewObjectType(ObjectFlags.Reference | objectFlags | GetPropagatingFlagsOfTypes(typeArguments, TypeFlags.None), target._symbol);
        var d = t.AsTypeReference()!;
        d._target = target;
        d._resolvedTypeArguments = typeArguments;
        intf._instantiations![id] = t;
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) createDeferredTypeReference(target *Type, node *ast.Node, mapper *TypeMapper, alias *TypeAlias) *Type</c>（checker.go:25525-25539）。</summary>
    internal Type CreateDeferredTypeReference(Type target, Node? node, TypeMapper? mapper, TypeAlias? alias)
    {
        if (alias == null)
        {
            alias = GetAliasForTypeNode(node!);
            if (alias != null && mapper != null)
            {
                alias._typeArguments = InstantiateTypes(alias._typeArguments, mapper);
            }
        }
        var t = NewObjectType(ObjectFlags.Reference, target._symbol);
        t._alias = alias;
        var d = t.AsTypeReference()!;
        d._target = target;
        d._mapper = mapper;
        d._node = node;
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) cloneTypeReference(source *Type) *Type</c>（checker.go:25541-25547）。</summary>
    internal Type CloneTypeReference(Type source)
    {
        var t = NewObjectType(ObjectFlags.Reference, source._symbol);
        t._objectFlags = source._objectFlags & ~ObjectFlags.MembersResolved;
        t.AsTypeReference()!._target = source.AsTypeReference()!._target;
        t.AsTypeReference()!._resolvedTypeArguments = source.AsTypeReference()!._resolvedTypeArguments;
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) setStructuredTypeMembers(t *Type, members ast.SymbolTable, callSignatures []*Signature, constructSignatures []*Signature, indexInfos []*IndexInfo)</c>（checker.go:25549-25570）。</summary>
    internal void SetStructuredTypeMembers(Type t, SymbolTable? members, Signature[]? callSignatures, Signature[]? constructSignatures, IndexInfo[]? indexInfos)
    {
        t._objectFlags |= ObjectFlags.MembersResolved;
        var data = t.AsStructuredType()!;
        data._members = members;
        data._properties = GetNamedMembers(members, t._symbol);
        if ((callSignatures?.Length ?? 0) != 0)
        {
            if ((constructSignatures?.Length ?? 0) != 0)
            {
                data._signatures = Concatenate(callSignatures, constructSignatures);
            }
            else
            {
                // Go: slices.Clip(callSignatures)（零拷贝视图；C# 直接返回原数组）
                data._signatures = callSignatures;
            }
            data._callSignatureCount = callSignatures!.Length;
        }
        else
        {
            if ((constructSignatures?.Length ?? 0) != 0)
            {
                data._signatures = constructSignatures;
            }
            else
            {
                data._signatures = null;
            }
            data._callSignatureCount = 0;
        }
        data._indexInfos = indexInfos;
    }

    /// <summary>Go: <c>func (c *Checker) newTypeParameter(symbol *ast.Symbol) *Type</c>（checker.go:25572-25576）。</summary>
    internal Type NewTypeParameter(Symbol? symbol)
    {
        var t = NewType(TypeFlags.TypeParameter, ObjectFlags.None, new TypeParameter());
        t._symbol = symbol;
        return t;
    }

    // This function is used to propagate certain flags when creating new object type references and union types.
    // It is only necessary to do so if a constituent type might be the undefined type, the null type, the type
    // of an object literal or a non-inferrable type. This is because there are operations in the type checker
    // that care about the presence of such types at arbitrary depth in a containing type.
    /// <summary>Go: <c>func (c *Checker) getPropagatingFlagsOfTypes(types []*Type, excludeKinds TypeFlags) ObjectFlags</c>（checker.go:25582-25590）。</summary>
    internal ObjectFlags GetPropagatingFlagsOfTypes(Type[]? types, TypeFlags excludeKinds)
    {
        var result = ObjectFlags.None;
        foreach (var t in types ?? [])
        {
            if ((t._flags & excludeKinds) == 0)
            {
                result |= t._objectFlags;
            }
        }
        return result & ObjectFlags.PropagatingFlags;
    }

    /// <summary>Go: <c>func (c *Checker) newUnionType(objectFlags ObjectFlags, types []*Type) *Type</c>（checker.go:25592-25596）。</summary>
    internal Type NewUnionType(ObjectFlags objectFlags, Type[]? types)
    {
        var data = new UnionType();
        data._types = types;
        return NewType(TypeFlags.Union, objectFlags, data);
    }

    /// <summary>Go: <c>func (c *Checker) newIntersectionType(objectFlags ObjectFlags, types []*Type) *Type</c>（checker.go:25598-25602）。</summary>
    internal Type NewIntersectionType(ObjectFlags objectFlags, Type[]? types)
    {
        var data = new IntersectionType();
        data._types = types;
        return NewType(TypeFlags.Intersection, objectFlags, data);
    }

    /// <summary>Go: <c>func (c *Checker) newIndexedAccessType(objectType *Type, indexType *Type, accessFlags AccessFlags) *Type</c>（checker.go:25604-25610）。</summary>
    internal Type NewIndexedAccessType(Type? objectType, Type? indexType, AccessFlags accessFlags)
    {
        var data = new IndexedAccessType();
        data._objectType = objectType;
        data._indexType = indexType;
        data._accessFlags = accessFlags;
        return NewType(TypeFlags.IndexedAccess, ObjectFlags.None, data);
    }

    /// <summary>Go: <c>func (c *Checker) newIndexType(target *Type, indexFlags IndexFlags) *Type</c>（checker.go:25612-25617）。</summary>
    internal Type NewIndexType(Type? target, IndexFlags indexFlags)
    {
        var data = new IndexType();
        data._target = target;
        data._indexFlags = indexFlags;
        return NewType(TypeFlags.Index, ObjectFlags.None, data);
    }

    /// <summary>Go: <c>func (c *Checker) newTemplateLiteralType(texts []string, types []*Type) *Type</c>（checker.go:25619-25624）。</summary>
    internal Type NewTemplateLiteralType(string[]? texts, Type[]? types)
    {
        var data = new TemplateLiteralType();
        data._texts = texts;
        data._types = types;
        return NewType(TypeFlags.TemplateLiteral, ObjectFlags.None, data);
    }

    /// <summary>Go: <c>func (c *Checker) newStringMappingType(symbol *ast.Symbol, target *Type) *Type</c>（checker.go:25626-25632）。</summary>
    internal Type NewStringMappingType(Symbol? symbol, Type? target)
    {
        var data = new StringMappingType();
        data._target = target;
        var t = NewType(TypeFlags.StringMapping, ObjectFlags.None, data);
        t._symbol = symbol;
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) newConditionalType(root *ConditionalRoot, mapper *TypeMapper, combinedMapper *TypeMapper) *Type</c>（checker.go:25634-25642）。</summary>
    internal Type NewConditionalType(ConditionalRoot? root, TypeMapper? mapper, TypeMapper? combinedMapper)
    {
        var data = new ConditionalType();
        data._root = root;
        data._checkType = InstantiateType(root!._checkType!, mapper);
        data._extendsType = InstantiateType(root!._extendsType!, mapper);
        data._mapper = mapper;
        data._combinedMapper = combinedMapper;
        return NewType(TypeFlags.Conditional, ObjectFlags.None, data);
    }

    /// <summary>Go: <c>func (c *Checker) newSubstitutionType(baseType *Type, constraint *Type) *Type</c>（checker.go:25644-25649）。</summary>
    internal Type NewSubstitutionType(Type? baseType, Type? constraint)
    {
        var data = new SubstitutionType();
        data._baseType = baseType;
        data._constraint = constraint;
        return NewType(TypeFlags.Substitution, ObjectFlags.None, data);
    }

    /// <summary>Go: <c>func (c *Checker) newSignature(flags SignatureFlags, declaration *ast.Node, typeParameters []*Type, thisParameter *ast.Symbol, parameters []*ast.Symbol, resolvedReturnType *Type, resolvedTypePredicate *TypePredicate, minArgumentCount int) *Signature</c>（checker.go:25651-25665）。</summary>
    internal Signature NewSignature(SignatureFlags flags, Node? declaration, Type[]? typeParameters, Symbol? thisParameter, Symbol[]? parameters, Type? resolvedReturnType, TypePredicate? resolvedTypePredicate, int minArgumentCount)
    {
        SignatureCount++;
        var sig = new Signature();
        sig._id = (SignatureId)SignatureCount;
        sig._flags = flags;
        sig._declaration = declaration;
        sig._typeParameters = typeParameters;
        sig._parameters = parameters;
        sig._thisParameter = thisParameter;
        sig._resolvedReturnType = resolvedReturnType;
        sig._resolvedTypePredicate = resolvedTypePredicate;
        sig._minArgumentCount = minArgumentCount;
        sig._resolvedMinArgumentCount = -1;
        return sig;
    }

    /// <summary>Go: <c>func (c *Checker) newIndexInfo(keyType *Type, valueType *Type, isReadonly bool, declaration *ast.Node, components []*ast.Node) *IndexInfo</c>（checker.go:25667-25675）。</summary>
    internal IndexInfo NewIndexInfo(Type? keyType, Type? valueType, bool isReadonly, Node? declaration, Node[]? components)
    {
        var info = new IndexInfo();
        info._keyType = keyType;
        info._valueType = valueType;
        info._isReadonly = isReadonly;
        info._declaration = declaration;
        info._components = components;
        return info;
    }

    /// <summary>Go: <c>func (c *Checker) getRegularTypeOfLiteralType(t *Type) *Type</c>（checker.go:25677-25689）。</summary>
    internal Type GetRegularTypeOfLiteralType(Type t)
    {
        if ((t._flags & TypeFlags.Freshable) != 0)
        {
            return t.AsLiteralType()!._regularType!;
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            var u = t.AsUnionType()!;
            if (u._regularType == null)
            {
                u._regularType = MapType(t, GetRegularTypeOfLiteralType);
            }
            return u._regularType!;
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getFreshTypeOfLiteralType(t *Type) *Type</c>（checker.go:25691-25703）。</summary>
    internal Type GetFreshTypeOfLiteralType(Type t)
    {
        if ((t._flags & TypeFlags.Freshable) != 0)
        {
            var d = t.AsLiteralType()!;
            if (d._freshType == null)
            {
                var f = NewLiteralType(t._flags, d._value, t);
                f._symbol = t._symbol;
                f.AsLiteralType()!._freshType = f;
                d._freshType = f;
            }
            return d._freshType!;
        }
        return t;
    }

    /// <summary>Go: <c>func isFreshLiteralType(t *Type) bool</c>（checker.go:25705-25707，package 级）。</summary>
    internal static bool IsFreshLiteralType(Type t)
    {
        return (t._flags & TypeFlags.Freshable) != 0 && t.AsLiteralType()!._freshType == t;
    }

    /// <summary>Go: <c>func (c *Checker) getStringLiteralType(value string) *Type</c>（checker.go:25709-25716）。</summary>
    internal Type GetStringLiteralType(string value)
    {
        var t = _stringLiteralTypes.GetValueOrDefault(value);
        if (t == null)
        {
            t = NewLiteralType(TypeFlags.StringLiteral, value, null);
            _stringLiteralTypes[value] = t;
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getNumberLiteralType(value jsnum.Number) *Type</c>（checker.go:25718-25733）。</summary>
    internal Type GetNumberLiteralType(Number value)
    {
        // NaN cannot be used as a Go map key because NaN != NaN in IEEE 754,
        // so Go map lookups for NaN always miss. Cache NaN type separately.
        if (value.IsNaN())
        {
            if (_nanType == null)
            {
                _nanType = NewLiteralType(TypeFlags.NumberLiteral, value, null);
            }
            return _nanType!;
        }
        var t = _numberLiteralTypes.GetValueOrDefault(value);
        if (t == null)
        {
            t = NewLiteralType(TypeFlags.NumberLiteral, value, null);
            _numberLiteralTypes[value] = t;
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getBigIntLiteralType(value jsnum.PseudoBigInt) *Type</c>（checker.go:25735-25744）。</summary>
    internal Type GetBigIntLiteralType(PseudoBigInt value)
    {
        var t = _bigintLiteralTypes.GetValueOrDefault(value);
        if (t == null)
        {
            t = NewLiteralType(TypeFlags.BigIntLiteral, value, null);
            _bigintLiteralTypes[value] = t;
        }
        return t;
    }

    // text is a valid bigint string excluding a trailing `n`, but including a possible prefix `-`.
    // Use `isValidBigIntString(text, roundTripOnly)` before calling this function.
    /// <summary>Go: <c>func (c *Checker) parseBigIntLiteralType(text string) *Type</c>（checker.go:25746-25748）。</summary>
    internal Type ParseBigIntLiteralType(string text)
    {
        return GetBigIntLiteralType(global::TypeScript.JsNum.JsNum.ParseValidBigInt(text));
    }

    /// <summary>Go: <c>func getStringLiteralValue(t *Type) string</c>（checker.go:25750-25752，package 级）。</summary>
    internal static string GetStringLiteralValue(Type t)
    {
        return (string)t.AsLiteralType()!._value!;
    }

    /// <summary>Go: <c>func getNumberLiteralValue(t *Type) jsnum.Number</c>（checker.go:25754-25756，package 级）。</summary>
    internal static Number GetNumberLiteralValue(Type t)
    {
        return (Number)t.AsLiteralType()!._value!;
    }

    /// <summary>Go: <c>func getBigIntLiteralValue(t *Type) jsnum.PseudoBigInt</c>（checker.go:25758-25760，package 级）。</summary>
    internal static PseudoBigInt GetBigIntLiteralValue(Type t)
    {
        return (PseudoBigInt)t.AsLiteralType()!._value!;
    }

    /// <summary>Go: <c>func getBooleanLiteralValue(t *Type) bool</c>（checker.go:25762-25764，package 级）。</summary>
    internal static bool GetBooleanLiteralValue(Type t)
    {
        return (bool)t.AsLiteralType()!._value!;
    }

    /// <summary>Go: <c>func (c *Checker) getEnumLiteralType(value any, enumSymbol *ast.Symbol, symbol *ast.Symbol) *Type</c>（checker.go:25766-25795）。</summary>
    internal Type GetEnumLiteralType(object? value, Symbol? enumSymbol, Symbol? symbol)
    {
        TypeFlags flags;
        switch (value)
        {
            case string:
                flags = TypeFlags.EnumLiteral | TypeFlags.StringLiteral;
                break;
            case Number v:
                flags = TypeFlags.EnumLiteral | TypeFlags.NumberLiteral;
                // NaN cannot be used as a Go map key because NaN != NaN in IEEE 754,
                // so Go map lookups for NaN always miss. Cache NaN enum types separately by enum symbol.
                if (v.IsNaN())
                {
                    var nanType = _enumNaNLiteralTypes.GetValueOrDefault(enumSymbol!);
                    if (nanType == null)
                    {
                        nanType = NewLiteralType(flags, value, null);
                        nanType._symbol = symbol;
                        _enumNaNLiteralTypes[enumSymbol!] = nanType;
                    }
                    return nanType;
                }
                break;
            default:
                Debug.Fail("Unhandled case in getEnumLiteralType");
                throw new System.Diagnostics.UnreachableException();
        }
        var key = new EnumLiteralKey(enumSymbol, value);
        var t = _enumLiteralTypes.GetValueOrDefault(key);
        if (t == null)
        {
            t = NewLiteralType(flags, value, null);
            t._symbol = symbol;
            _enumLiteralTypes[key] = t;
        }
        return t;
    }

    /// <summary>Go: <c>func isLiteralType(t *Type) bool</c>（checker.go:25797-25808，package 级）。</summary>
    internal static bool IsLiteralType(Type t)
    {
        if ((t._flags & TypeFlags.Boolean) != 0)
        {
            return true;
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            if ((t._flags & TypeFlags.EnumLiteral) != 0)
            {
                return true;
            }
            return Every(t.Types(), IsUnitType);
        }
        return IsUnitType(t);
    }

    /// <summary>Go: <c>func isNeitherUnitTypeNorNever(t *Type) bool</c>（checker.go:25810-25812，package 级）。</summary>
    internal static bool IsNeitherUnitTypeNorNever(Type t)
    {
        return (t._flags & (TypeFlags.Unit | TypeFlags.Never)) == 0;
    }

    /// <summary>Go: <c>func isUnitType(t *Type) bool</c>（checker.go:25814-25816，package 级）。</summary>
    internal static bool IsUnitType(Type t)
    {
        return (t._flags & TypeFlags.Unit) != 0;
    }

    /// <summary>Go: <c>func (c *Checker) isUnitLikeType(t *Type) bool</c>（checker.go:25818-25826）。</summary>
    internal bool IsUnitLikeType(Type t)
    {
        // Intersections that reduce to 'never' (e.g. 'T & null' where 'T extends {}') are not unit types.
        t = GetBaseConstraintOrType(t);
        // Scan intersections such that tagged literal types are considered unit types.
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            return Some(t.AsIntersectionType()!._types, IsUnitType);
        }
        return IsUnitType(t);
    }

    /// <summary>Go: <c>func (c *Checker) extractUnitType(t *Type) *Type</c>（checker.go:25828-25836）。</summary>
    internal Type ExtractUnitType(Type t)
    {
        if ((t._flags & TypeFlags.Intersection) != 0)
        {
            var u = Find(t.AsIntersectionType()!._types, IsUnitType);
            if (u != null)
            {
                return u;
            }
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getBaseTypeOfLiteralType(t *Type) *Type</c>（checker.go:25838-25856）。未导出者加 Worker（§2.1）。</summary>
    internal Type GetBaseTypeOfLiteralTypeWorker(Type t)
    {
        if ((t._flags & TypeFlags.EnumLike) != 0)
        {
            return GetBaseTypeOfEnumLikeType(t);
        }
        if ((t._flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0)
        {
            return _stringType;
        }
        if ((t._flags & TypeFlags.NumberLiteral) != 0)
        {
            return _numberType;
        }
        if ((t._flags & TypeFlags.BigIntLiteral) != 0)
        {
            return _bigintType;
        }
        if ((t._flags & TypeFlags.BooleanLiteral) != 0)
        {
            return _booleanType;
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            return GetBaseTypeOfLiteralTypeUnion(t);
        }
        return t;
    }

    // This like getBaseTypeOfLiteralType, but instead treats enum literals as strings/numbers instead
    // of returning their enum base type (which depends on the types of other literals in the enum).
    /// <summary>Go: <c>func (c *Checker) getBaseTypeOfLiteralTypeForComparison(t *Type) *Type</c>（checker.go:25858-25872）。</summary>
    internal Type GetBaseTypeOfLiteralTypeForComparison(Type t)
    {
        if ((t._flags & (TypeFlags.StringLiteral | TypeFlags.TemplateLiteral | TypeFlags.StringMapping)) != 0)
        {
            return _stringType;
        }
        if ((t._flags & (TypeFlags.NumberLiteral | TypeFlags.Enum)) != 0)
        {
            return _numberType;
        }
        if ((t._flags & TypeFlags.BigIntLiteral) != 0)
        {
            return _bigintType;
        }
        if ((t._flags & TypeFlags.BooleanLiteral) != 0)
        {
            return _booleanType;
        }
        if ((t._flags & TypeFlags.Union) != 0)
        {
            return MapType(t, GetBaseTypeOfLiteralTypeForComparison);
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getBaseTypeOfEnumLikeType(t *Type) *Type</c>（checker.go:25874-25879）。</summary>
    internal Type GetBaseTypeOfEnumLikeType(Type t)
    {
        if ((t._flags & TypeFlags.EnumLike) != 0 && (t._symbol!.Flags & SymbolFlags.EnumMember) != 0)
        {
            return GetDeclaredTypeOfSymbolWorker(GetParentOfSymbol(t._symbol!)!);
        }
        return t;
    }

    /// <summary>Go: <c>func (c *Checker) getBaseTypeOfLiteralTypeUnion(t *Type) *Type</c>（checker.go:25881-25889）。</summary>
    internal Type GetBaseTypeOfLiteralTypeUnion(Type t)
    {
        var key = new CachedTypeKey(CachedTypeKind.LiteralUnionBaseType, t._id);
        if (_cachedTypes.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var result = MapType(t, GetBaseTypeOfLiteralTypeWorker);
        _cachedTypes[key] = result;
        return result;
    }
}
