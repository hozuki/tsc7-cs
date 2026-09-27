// ported from: internal/checker/types.go:1060-1287
// （ElementFlags/TupleElementInfo/TupleType、InstantiationExpressionType、MappedType、ReverseMappedType、
//   EvolvingArrayType、UnionOrIntersectionType/UnionType/IntersectionType、TypeParameter、IndexFlags/IndexType、
//   IndexedAccessType、TemplateLiteralType、StringMappingType、SubstitutionType、ConditionalRoot/ConditionalType）
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §3.2、docs/checker-plan.md §5.1/§5.5）：
//
// 1) Go 的嵌入（`TupleType` 嵌 `InterfaceType` 等）→ C# 基类继承；`TypeData`/`TypeBase` 不出现（见 Types.Type.cs 文件头）。
// 2) `AsX()` 一律 `override ... => this`（Go 里是 `t.data.(*X)` 直接断言）。
// 3) Go 里与 `Type.Target()`/`Type.Types()` 同名的"遮蔽"方法（`IndexType.Target`、`StringMappingType.Target`、
//    `UnionOrIntersectionType.Types`、`TemplateLiteralType.Types`）→ C# 的 `override`（基类那两个是 virtual；
//    行为与 Go 的遮蔽一致：对这些类型返回各自字段，其余类型仍走基类的 DebugFailureException）。
// 4) `TupleElementInfo` 是 Go 的小结构体（按值存在 `[]TupleElementInfo` 里、`elementInfos[i].flags = …` 就地改）
//    → 落成 C# **struct**（值语义与 Go 一致；就地改数组元素字段仍合法，但 List<T> 索引器上不行）。
// 5) `[]T` 字段 → `T[]?`（null = Go 的 nil slice）；`*T` → `T?`；`map[K]V` → `Dictionary<K,V>?`。
// 6) `slices.Clip`/子切片视图 → 数组区间（一次拷贝，见 Types.Type.cs 文件头第 4 条）。
// 7) `ConditionalRoot` 在 Go 里始终以 `*ConditionalRoot` 传递（`&ConditionalRoot{…}`）→ C# sealed class。
// 8) Go 未导出字段 → `internal _camelCase`（docs/checker-plan.md §5.5）。
namespace TypeScript.Checker;

using TypeScript.Ast;

// TupleType

/// <summary>Go 的 <c>type ElementFlags uint32</c>。</summary>
[Flags]
public enum ElementFlags : uint
{
    None = 0,
    Required = 1u << 0,   // T
    Optional = 1u << 1,   // T?
    Rest = 1u << 2,       // ...T[]
    Variadic = 1u << 3,   // ...T
    Fixed = Required | Optional,
    Variable = Rest | Variadic,
    NonRequired = Optional | Rest | Variadic,
    NonRest = Required | Optional | Variadic,
}

/// <summary>Go 的 <c>type TupleElementInfo struct</c>（值语义，见文件头第 4 条）。</summary>
public struct TupleElementInfo
{
    internal ElementFlags _flags;
    internal Node? _labeledDeclaration; // NamedTupleMember | ParameterDeclaration | nil

    public ElementFlags TupleElementFlags()
    {
        return _flags;
    }

    public Node? LabeledDeclaration()
    {
        return _labeledDeclaration;
    }
}

/// <summary>Go 的 <c>type TupleType struct</c>（嵌 InterfaceType）。</summary>
public class TupleType : InterfaceType
{
    internal TupleElementInfo[]? _elementInfos;
    internal int _minLength;               // Number of required or variadic elements
    internal int _fixedLength;             // Number of initial required or optional elements
    internal ElementFlags _combinedFlags;
    internal bool _readonly;

    public override TupleType? AsTupleType() => this;

    public int FixedLength()
    {
        return _fixedLength;
    }

    public bool IsReadonly()
    {
        return _readonly;
    }

    public ElementFlags[] ElementFlags()
    {
        var elementInfos = _elementInfos;
        if (elementInfos is null)
        {
            return [];
        }
        var elementFlags = new ElementFlags[elementInfos.Length];
        for (var i = 0; i < elementInfos.Length; i++)
        {
            elementFlags[i] = elementInfos[i]._flags;
        }
        return elementFlags;
    }

    public TupleElementInfo[] ElementInfos()
    {
        return _elementInfos ?? [];
    }
}

// InstantiationExpressionType

/// <summary>Go 的 <c>type InstantiationExpressionType struct</c>（嵌 ObjectType）。</summary>
public class InstantiationExpressionType : ObjectType
{
    internal Node? _node;

    public override InstantiationExpressionType? AsInstantiationExpressionType() => this;
}

// MappedType

/// <summary>Go 的 <c>type MappedType struct</c>（嵌 ObjectType）。</summary>
public class MappedType : ObjectType
{
    internal MappedTypeNode? _declaration;
    internal Type? _typeParameter;
    internal Type? _constraintType;
    internal Type? _nameType;
    internal Type? _templateType;
    internal Type? _modifiersType;
    internal Type? _resolvedApparentType;
    internal bool _containsError;

    public override MappedType? AsMappedType() => this;

    public Type? TypeParameter()
    {
        return _typeParameter;
    }

    public Type? ConstraintType()
    {
        return _constraintType;
    }

    public Type? NameType()
    {
        return _nameType;
    }

    public Type? TemplateType()
    {
        return _templateType;
    }

    public void ResolveComponents(Checker c, Type typ)
    {
        // Go: c.getTypeParameterFromMappedType / getConstraintTypeFromMappedType / getNameTypeFromMappedType /
        //     getTemplateTypeFromMappedType（均为 Checker 的实例方法，属其它分片）
        c.GetTypeParameterFromMappedType(typ);
        c.GetConstraintTypeFromMappedType(typ);
        c.GetNameTypeFromMappedType(typ);
        c.GetTemplateTypeFromMappedType(typ);
    }
}

// ReverseMappedType

/// <summary>Go 的 <c>type ReverseMappedType struct</c>（嵌 ObjectType）。</summary>
public class ReverseMappedType : ObjectType
{
    internal Type? _source;
    internal Type? _mappedType;
    internal Type? _constraintType;

    public override ReverseMappedType? AsReverseMappedType() => this;
}

// EvolvingArrayType

/// <summary>Go 的 <c>type EvolvingArrayType struct</c>（嵌 ObjectType）。</summary>
public class EvolvingArrayType : ObjectType
{
    internal Type? _elementType;
    internal Type? _finalArrayType;

    public override EvolvingArrayType? AsEvolvingArrayType() => this;
}

// UnionOrIntersectionTypeData

/// <summary>Go 的 <c>type UnionOrIntersectionType struct</c>（嵌 StructuredType）。</summary>
public class UnionOrIntersectionType : StructuredType
{
    internal Type[]? _types;
    internal SymbolTable? _propertyCache;
    internal SymbolTable? _propertyCacheWithoutFunctionPropertyAugment;
    internal Symbol[]? _resolvedProperties;

    public override UnionOrIntersectionType? AsUnionOrIntersectionType() => this;

    // Go 里这里遮蔽了 Type.Types()（见文件头第 3 条）
    public override Type[] Types()
    {
        return _types ?? [];
    }
}

// UnionType

/// <summary>Go 的 <c>type UnionType struct</c>（嵌 UnionOrIntersectionType）。</summary>
public class UnionType : UnionOrIntersectionType
{
    internal Type? _resolvedReducedType;
    internal Type? _regularType;
    internal Type? _origin;                                  // Denormalized union, intersection, or index type in which union originates
    internal string _keyPropertyName = "";                   // Property with unique unit type that exists in every object/intersection in union type
    internal Dictionary<Type, Type>? _constituentMap;        // Constituents keyed by unit type discriminants

    public override UnionType? AsUnionType() => this;
}

// IntersectionType

/// <summary>Go 的 <c>type IntersectionType struct</c>（嵌 UnionOrIntersectionType）。</summary>
public class IntersectionType : UnionOrIntersectionType
{
    internal Type? _resolvedApparentType;
    internal Type? _uniqueLiteralFilledInstantiation; // Instantiation with type parameters mapped to never type

    public override IntersectionType? AsIntersectionType() => this;
}

// TypeParameter

/// <summary>Go 的 <c>type TypeParameter struct</c>（嵌 ConstrainedType）。</summary>
public class TypeParameter : ConstrainedType
{
    internal Type? _constraint;
    internal Type? _target;
    internal TypeMapper? _mapper;
    internal bool _isThisType;
    internal bool _isDistributed;
    internal Type? _resolvedDefaultType;
    internal Type? _distributedType;

    public override TypeParameter? AsTypeParameter() => this;

    public bool IsThisType()
    {
        return _isThisType;
    }
}

// IndexFlags

/// <summary>Go 的 <c>type IndexFlags uint32</c>。</summary>
[Flags]
public enum IndexFlags : uint
{
    None = 0,
    StringsOnly = 1u << 0,
    NoIndexSignatures = 1u << 1,
    NoReducibleCheck = 1u << 2,
}

// IndexType

/// <summary>Go 的 <c>type IndexType struct</c>（嵌 ConstrainedType）。</summary>
public class IndexType : ConstrainedType
{
    internal Type? _target;
    internal IndexFlags _indexFlags;

    public override IndexType? AsIndexType() => this;

    // Go 里这里遮蔽了 Type.Target()（见文件头第 3 条）
    public override Type? Target()
    {
        return _target;
    }
}

// IndexedAccessType

/// <summary>Go 的 <c>type IndexedAccessType struct</c>（嵌 ConstrainedType）。</summary>
public class IndexedAccessType : ConstrainedType
{
    internal Type? _objectType;
    internal Type? _indexType;
    internal AccessFlags _accessFlags; // Only includes AccessFlags.Persistent

    public override IndexedAccessType? AsIndexedAccessType() => this;

    public Type? ObjectType()
    {
        return _objectType;
    }

    public Type? IndexType()
    {
        return _indexType;
    }
}

/// <summary>Go 的 <c>type TemplateLiteralType struct</c>（嵌 ConstrainedType）。</summary>
public class TemplateLiteralType : ConstrainedType
{
    internal string[]? _texts; // Always one element longer than types
    internal Type[]? _types;   // Always at least one element

    public override TemplateLiteralType? AsTemplateLiteralType() => this;

    public string[] Texts()
    {
        return _texts ?? [];
    }

    // Go 里这里遮蔽了 Type.Types()（见文件头第 3 条）
    public override Type[] Types()
    {
        return _types ?? [];
    }
}

/// <summary>Go 的 <c>type StringMappingType struct</c>（嵌 ConstrainedType）。</summary>
public class StringMappingType : ConstrainedType
{
    internal Type? _target;

    public override StringMappingType? AsStringMappingType() => this;

    // Go 里这里遮蔽了 Type.Target()（见文件头第 3 条）
    public override Type? Target()
    {
        return _target;
    }
}

/// <summary>Go 的 <c>type SubstitutionType struct</c>（嵌 ConstrainedType）。</summary>
public class SubstitutionType : ConstrainedType
{
    internal Type? _baseType;   // Target type
    internal Type? _constraint; // Constraint that target type is known to satisfy

    public override SubstitutionType? AsSubstitutionType() => this;

    public Type? BaseType()
    {
        return _baseType;
    }

    public Type? SubstConstraint()
    {
        return _constraint;
    }
}

/// <summary>Go 的 <c>type ConditionalRoot struct</c>（Go 里始终以指针传递 → C# class）。</summary>
public sealed class ConditionalRoot
{
    internal ConditionalTypeNode? _node;
    internal Type? _checkType;
    internal Type? _extendsType;
    internal bool _isDistributive;
    // Go: `inferTypeParameters []*TypeParameter` —— nil 切片与空切片在 len/range/slices.Index 下等价，
    // 归一成空数组（`getInferTypeParameters` 返回 nil 时）。
    internal Type[] _inferTypeParameters = [];
    internal Type[]? _outerTypeParameters;
    internal Dictionary<CacheHashKey, Type>? _instantiations;
    internal TypeAlias? _alias;
}

/// <summary>Go 的 <c>type ConditionalType struct</c>（嵌 ConstrainedType）。</summary>
public class ConditionalType : ConstrainedType
{
    internal ConditionalRoot? _root;
    internal Type? _checkType;
    internal Type? _extendsType;
    internal Type? _resolvedTrueType;
    internal Type? _resolvedFalseType;
    internal Type? _resolvedInferredTrueType; // The `trueType` instantiated with the `combinedMapper`, if present
    internal Type? _resolvedDefaultConstraint;
    internal Type? _resolvedConstraintOfDistributive;
    internal TypeMapper? _mapper;
    internal TypeMapper? _combinedMapper;

    public override ConditionalType? AsConditionalType() => this;

    public Type? CheckType()
    {
        return _checkType;
    }

    public Type? ExtendsType()
    {
        return _extendsType;
    }
}
