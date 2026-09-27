// ported from: internal/checker/types.go:665-1058
// （TypeAlias、Type 基类（含 AsX 转换）与 Distributed/Target/Mapper/Types 等公共访问器、IntrinsicType、
//   LiteralType、UniqueESSymbolType、ConstrainedType、StructuredType、ObjectType、TypeReference、InterfaceType）
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §2/§3.2.1、docs/checker-plan.md §5.1/§5.5）：
//
// 1) Go 的「数据 + 断言」三层（`Type` 结构体 / `TypeData` 接口 / `TypeBase` 结构体）合并为一个类层次：
//    `TypeData`/`TypeBase` 两个名字在 C# 里**不出现**，具体类型（IntrinsicType/LiteralType/TupleType/…）
//    直接继承 `Type`；Go 的嵌入（`StructuredType` 嵌入 `ConstrainedType` …）→ C# 基类继承。
//    Go 的 `t.data.(*X)` → `(X)t`；`x, ok := t.data.(*X)` → `t as X` + null 判断；`TypeData` 型的
//    参数/字段/返回 → `Type`；Go 的 `.AsType()`（返回 `&t.Type`）不移植，调用点直接用对象本身。
// 2) `AsX()`：基类 `Type` 上 `public virtual X? AsX() => null;`，具体类型里 `public override X? AsX() => this;`
//    （逐条对应 Go 的「TypeBase 返回 nil、具体类型返回 t」）。**偏离**：Go 里 `AsIntrinsicType` 等是
//    `t.data.(*IntrinsicType)` 直接断言（类型不符会 panic），C# 里统一走虚调用、类型不符返回 null（调用点随后 NRE）。
//    约定与 Ast 的 `Node.AsIdentifier()`（`this as Identifier`）一致；调用点按 Ast 习惯加 `!`。
// 3) `panic("Unhandled case in …")` → `throw new DebugFailureException(...)`（Ast/Node.cs 的同款处理：
//    Debug.Fail 只是它的包装，throw 让编译器接受控制流）。
// 4) `slices.Clip(t.signatures[a:b])` 是"零拷贝改 len/cap"；C# 数组没有视图 → `_signatures[a..b]`（**一次拷贝**）。
//    返回 nil slice 的地方一律给 `[]`（docs/architecture.md 约定，见 Ast/SourceFile.cs 文件头）。
// 5) `[]T` 字段 → `T[]?`（null = Go 的 nil slice）；`*T` → `T?`。
// 6) Go 里 `TypeAlias.Symbol()`/`TypeArguments()` 带显式 nil 接收者分支（`if a == nil { return nil }`）——
//    C# 实例方法不能在 null 上分派，该分支是死代码（docs/architecture.md §3.2），调用点写 `x?.Symbol()`。
// 7) Go 未导出字段 → `internal _camelCase`（docs/checker-plan.md §5.5）。
// 8) `_checker` 恒由 Checker.newType 赋值为非 null（Go 里为 nil 会 panic）→ 声明为非空并给 `null!` 默认值。
namespace TypeScript.Checker;

using TypeScript.Ast;

// TypeAlias

/// <summary>Go 的 <c>type TypeAlias struct</c>。</summary>
public sealed partial class TypeAlias
{
    internal Symbol? _symbol;
    internal Type[]? _typeArguments;

    public Symbol? Symbol()
    {
        return _symbol;
    }

    public Type[]? TypeArguments()
    {
        return _typeArguments;
    }
}

// Type

/// <summary>
/// Go 的 <c>type Type struct</c>（TypeData/TypeBase 已合并进来，见文件头第 1 条）。
/// 身份语义 = 引用相等（Go 里是 <c>*Type</c> 指针相等），**不要**覆写 Equals/GetHashCode。
/// </summary>
public abstract class Type
{
    internal TypeFlags _flags;
    internal ObjectFlags _objectFlags;
    internal TypeId _id;
    internal Symbol? _symbol;
    internal TypeAlias? _alias;
    internal Checker _checker = null!;

    public TypeId Id()
    {
        return _id;
    }

    public TypeFlags Flags()
    {
        return _flags;
    }

    public ObjectFlags ObjectFlags()
    {
        return _objectFlags;
    }

    // Casts for concrete struct types

    public virtual IntrinsicType? AsIntrinsicType() => null;
    public virtual LiteralType? AsLiteralType() => null;
    public virtual UniqueESSymbolType? AsUniqueESSymbolType() => null;
    public virtual TupleType? AsTupleType() => null;
    public virtual InstantiationExpressionType? AsInstantiationExpressionType() => null;
    public virtual MappedType? AsMappedType() => null;
    public virtual ReverseMappedType? AsReverseMappedType() => null;
    public virtual EvolvingArrayType? AsEvolvingArrayType() => null;
    public virtual TypeParameter? AsTypeParameter() => null;
    public virtual UnionType? AsUnionType() => null;
    public virtual IntersectionType? AsIntersectionType() => null;
    public virtual IndexType? AsIndexType() => null;
    public virtual IndexedAccessType? AsIndexedAccessType() => null;
    public virtual TemplateLiteralType? AsTemplateLiteralType() => null;
    public virtual StringMappingType? AsStringMappingType() => null;
    public virtual SubstitutionType? AsSubstitutionType() => null;
    public virtual ConditionalType? AsConditionalType() => null;

    // Casts for embedded struct types（Go 里走 `t.data.AsX()`，即 TypeBase 返回 nil、具体类型返回 t）

    public virtual ConstrainedType? AsConstrainedType() => null;
    public virtual StructuredType? AsStructuredType() => null;
    public virtual ObjectType? AsObjectType() => null;
    public virtual TypeReference? AsTypeReference() => null;
    public virtual InterfaceType? AsInterfaceType() => null;
    public virtual UnionOrIntersectionType? AsUnionOrIntersectionType() => null;

    public Type[] Distributed()
    {
        if ((_flags & TypeFlags.Union) != 0)
        {
            return AsUnionType()!._types ?? [];
        }
        if ((_flags & TypeFlags.Never) != 0)
        {
            return [];
        }
        return [this];
    }

    // Common accessors

    // Go 里 IndexType/StringMappingType 各自定义了同名的 Target()（遮蔽本方法，见 Types.Compound.cs 文件头第 3 条）
    public virtual Type? Target()
    {
        if ((_flags & TypeFlags.Object) != 0)
        {
            return AsObjectType()!._target;
        }
        if ((_flags & TypeFlags.TypeParameter) != 0)
        {
            return AsTypeParameter()!._target;
        }
        if ((_flags & TypeFlags.Index) != 0)
        {
            return AsIndexType()!._target;
        }
        if ((_flags & TypeFlags.StringMapping) != 0)
        {
            return AsStringMappingType()!._target;
        }
        // Go 的第 5 个 case（TypeFlagsObject + ObjectFlagsMapped）不可达：第 1 个 case 已覆盖所有 TypeFlagsObject。
        // 逐条保留以与 Go 对齐。
        // 注意：类内 `ObjectFlags` 这个名字被上面的方法 `ObjectFlags()` 遮蔽（方法组不能做成员访问，CS0119）
        // → 这里必须写全限定名。
        if ((_flags & TypeFlags.Object) != 0 && (_objectFlags & TypeScript.Checker.ObjectFlags.Mapped) != 0)
        {
            return AsMappedType()!._target;
        }
        throw new DebugFailureException("Unhandled case in Type.Target");
    }

    public TypeMapper? Mapper()
    {
        if ((_flags & TypeFlags.Object) != 0)
        {
            return AsObjectType()!._mapper;
        }
        if ((_flags & TypeFlags.TypeParameter) != 0)
        {
            return AsTypeParameter()!._mapper;
        }
        if ((_flags & TypeFlags.Conditional) != 0)
        {
            return AsConditionalType()!._mapper;
        }
        throw new DebugFailureException("Unhandled case in Type.Mapper");
    }

    // Go 里 UnionOrIntersectionType/TemplateLiteralType 各自定义了同名的 Types()（遮蔽本方法，见 Types.Compound.cs 文件头第 3 条）
    public virtual Type[] Types()
    {
        if ((_flags & TypeFlags.UnionOrIntersection) != 0)
        {
            return AsUnionOrIntersectionType()!._types ?? [];
        }
        if ((_flags & TypeFlags.TemplateLiteral) != 0)
        {
            return AsTemplateLiteralType()!._types ?? [];
        }
        throw new DebugFailureException("Unhandled case in Type.Types");
    }

    public InterfaceType? TargetInterfaceType()
    {
        return AsTypeReference()!._target!.AsInterfaceType();
    }

    public TupleType? TargetTupleType()
    {
        return AsTypeReference()!._target!.AsTupleType();
    }

    public Symbol? Symbol()
    {
        return _symbol;
    }

    public TypeAlias? Alias()
    {
        return _alias;
    }

    public bool IsUnion()
    {
        return (_flags & TypeFlags.Union) != 0;
    }

    public bool IsString()
    {
        return (_flags & TypeFlags.String) != 0;
    }

    public bool IsIntersection()
    {
        return (_flags & TypeFlags.Intersection) != 0;
    }

    public bool IsStringLiteral()
    {
        return (_flags & TypeFlags.StringLiteral) != 0;
    }

    public bool IsNumberLiteral()
    {
        return (_flags & TypeFlags.NumberLiteral) != 0;
    }

    public bool IsBigIntLiteral()
    {
        return (_flags & TypeFlags.BigIntLiteral) != 0;
    }

    public bool IsEnumLiteral()
    {
        return (_flags & TypeFlags.EnumLiteral) != 0;
    }

    public bool IsBooleanLike()
    {
        return (_flags & TypeFlags.BooleanLike) != 0;
    }

    public bool IsStringLike()
    {
        return (_flags & TypeFlags.StringLike) != 0;
    }

    public bool IsClass()
    {
        // 同 Target()：类内 `ObjectFlags` 被方法 `ObjectFlags()` 遮蔽 → 用全限定名。
        return (_objectFlags & TypeScript.Checker.ObjectFlags.Class) != 0;
    }

    public bool IsTypeParameter()
    {
        return (_flags & TypeFlags.TypeParameter) != 0;
    }

    public bool IsIndex()
    {
        return (_flags & TypeFlags.Index) != 0;
    }

    public bool IsTupleType()
    {
        // Go: isTupleType(t)（checker.go:23884，C6 分片的包级函数 → Checker 的静态成员）
        return Checker.IsTupleType(this);
    }
}

// IntrinsicTypeData

/// <summary>Go 的 <c>type IntrinsicType struct</c>（嵌 TypeBase）。</summary>
public class IntrinsicType : Type
{
    internal string _intrinsicName = "";

    public override IntrinsicType? AsIntrinsicType() => this;

    public string IntrinsicName()
    {
        return _intrinsicName;
    }
}

// LiteralTypeData

/// <summary>Go 的 <c>type LiteralType struct</c>（嵌 TypeBase）。</summary>
public class LiteralType : Type
{
    internal object? _value;      // string | jsnum.Number | bool | PseudoBigInt | nil (computed enum)
    internal Type? _freshType;    // Fresh version of type
    internal Type? _regularType;  // Regular version of type

    public override LiteralType? AsLiteralType() => this;

    public object? Value()
    {
        return _value;
    }

    public Type? FreshType()
    {
        return _freshType;
    }

    public Type? RegularType()
    {
        return _regularType;
    }

    public override string ToString()
    {
        // Go: ValueToString（utilities.go:1738，ChkLinksUtils 分片的包级函数 → Checker 的静态成员）
        return Checker.ValueToString(_value);
    }
}

// UniqueESSymbolTypeData

/// <summary>Go 的 <c>type UniqueESSymbolType struct</c>（嵌 TypeBase）。</summary>
public class UniqueESSymbolType : Type
{
    internal string _name = "";

    public override UniqueESSymbolType? AsUniqueESSymbolType() => this;
}

// ConstrainedType (type with computed base constraint)

/// <summary>Go 的 <c>type ConstrainedType struct</c>（嵌 TypeBase）。</summary>
public class ConstrainedType : Type
{
    internal Type? _resolvedBaseConstraint;

    public override ConstrainedType? AsConstrainedType() => this;
}

// StructuredType (base of all types with members)

/// <summary>Go 的 <c>type StructuredType struct</c>（嵌 ConstrainedType）。</summary>
public class StructuredType : ConstrainedType
{
    internal SymbolTable? _members;
    internal Symbol[]? _properties;
    internal Signature[]? _signatures;  // Signatures (call + construct)
    internal int _callSignatureCount;   // Count of call signatures
    internal IndexInfo[]? _indexInfos;

    internal Type? _objectTypeWithoutAbstractConstructSignatures;

    public override StructuredType? AsStructuredType() => this;

    public Signature[] CallSignatures()
    {
        // Go: slices.Clip(t.signatures[:t.callSignatureCount])（Clip 是零拷贝视图；C# 里是一次拷贝，见文件头第 4 条）
        return _signatures is null ? [] : _signatures[.._callSignatureCount];
    }

    public Signature[] ConstructSignatures()
    {
        return _signatures is null ? [] : _signatures[_callSignatureCount..];
    }

    public Symbol[] Properties()
    {
        return _properties ?? [];
    }
}

// Except for tuple type references and reverse mapped types, all object types have an associated symbol.
// Possible object type instances are listed in the following.

// InterfaceType:
// ObjectFlagsClass: Originating non-generic class type
// ObjectFlagsClass|ObjectFlagsReference: Originating generic class type
// ObjectFlagsInterface: Originating non-generic interface type
// ObjectFlagsInterface|ObjectFlagsReference: Originating generic interface type

// TupleType:
// ObjectFlagsReference|ObjectFlagsTuple: Originating generic tuple type (synthesized)

// TypeReference
// ObjectFlagsReference: Instantiated generic class, interface, or tuple type

// ObjectType:
// ObjectFlagsAnonymous: Originating anonymous object type
// ObjectFlagsAnonymous|ObjectFlagsInstantiated: Instantiated anonymous object type

// MappedType:
// ObjectFlagsMapped: Originating mapped type
// ObjectFlagsMapped|ObjectFlagsInstantiated: Instantiated mapped type

// InstantiationExpressionType:
// ObjectFlagsAnonymous|ObjectFlagsInstantiationExpression: Originating instantiation expression type
// ObjectFlagsAnonymous|ObjectFlagsInstantiated|ObjectFlagsInstantiationExpression: Instantiated instantiation expression type

// ReverseMappedType:
// ObjectFlagsAnonymous|ObjectFlagsReverseMapped: Reverse mapped type

// EvolvingArrayType:
// ObjectFlagsEvolvingArray: Evolving array type

/// <summary>Go 的 <c>type ObjectType struct</c>（嵌 StructuredType）。</summary>
public class ObjectType : StructuredType
{
    internal Type? _target;                                   // Target of instantiated type
    internal TypeMapper? _mapper;                             // Type mapper for instantiated type
    internal Dictionary<CacheHashKey, Type>? _instantiations; // Map of type instantiations

    public override ObjectType? AsObjectType() => this;
}

// TypeReference (instantiation of an InterfaceType)

/// <summary>Go 的 <c>type TypeReference struct</c>（嵌 ObjectType）。</summary>
public class TypeReference : ObjectType
{
    internal Node? _node;                     // TypeReferenceNode | ArrayTypeNode | TupleTypeNode when deferred, else nil
    internal Type[]? _resolvedTypeArguments;

    public override TypeReference? AsTypeReference() => this;
}

// InterfaceType (when generic, serves as reference to instantiation of itself)

/// <summary>Go 的 <c>type InterfaceType struct</c>（嵌 TypeReference）。</summary>
public class InterfaceType : TypeReference
{
    internal Type[]? _allTypeParameters;        // Type parameters (outer + local + thisType)
    internal int _outerTypeParameterCount;      // Count of outer type parameters
    internal Type? _thisType;                   // The "this" type (nil if none)
    internal bool _baseTypesResolved;
    internal bool _declaredMembersResolved;
    internal Type? _resolvedBaseConstructorType;
    internal Type[]? _resolvedBaseTypes;
    internal SymbolTable? _declaredMembers;     // Declared members
    internal Signature[]? _declaredCallSignatures;       // Declared call signatures
    internal Signature[]? _declaredConstructSignatures;  // Declared construct signatures
    internal IndexInfo[]? _declaredIndexInfos;           // Declared index signatures

    public override InterfaceType? AsInterfaceType() => this;

    public Type[] OuterTypeParameters()
    {
        if (_allTypeParameters is null || _allTypeParameters.Length == 0)
        {
            return [];
        }
        return _allTypeParameters[.._outerTypeParameterCount];
    }

    public Type[] LocalTypeParameters()
    {
        if (_allTypeParameters is null || _allTypeParameters.Length == 0)
        {
            return [];
        }
        return _allTypeParameters[_outerTypeParameterCount..^1];
    }

    public Type[] TypeParameters()
    {
        if (_allTypeParameters is null || _allTypeParameters.Length == 0)
        {
            return [];
        }
        return _allTypeParameters[..^1];
    }

    public Type? ThisType()
    {
        return _thisType;
    }
}
