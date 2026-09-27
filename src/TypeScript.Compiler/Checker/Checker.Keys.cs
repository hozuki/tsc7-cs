// ported from: internal/checker/checker.go:1-552
//   （CheckMode…VarianceStackEntry + maxSerializationLevel：检查模式、类型系统查询标志、缓存键类型、
//     Inference 上下文/优先级、IntrinsicTypeKind/TypeFacts/IterationUse 等枚举、迭代与加宽辅助结构）
//
// 与 Go 的偏离（逐条；依据 docs/architecture.md §2/§3、docs/checker-plan.md §5）：
// 1) Go 里当 map key / Set 元素用的"可比较结构体"（EnumLiteralKey/EnumRelationKey/CachedTypeKey/NarrowedTypeKey/
//    UnionOfUnionKey/CachedSignatureKey/StringMappingKey/AssignmentReducedKey/DiscriminatedContextualTypeKey/
//    InstantiationExpressionKey/SubstitutionTypeKey/ReverseMappedTypeKey/IterationTypesKey/PropertiesTypesKey/
//    NonExistentPropertyKey/FlowLoopKey）在 C# 里是 class + IEquatable<T>：Equals/GetHashCode **逐字段**复刻 Go 的
//    结构体比较语义（引用字段按引用比较、值字段按其 Equals），否则 Dictionary/Set 每次都是新键。
//    Go 侧这些结构体从不"复制后再改写"（只整体构造），所以 class 的引用语义与原语义等价。
// 2) `iota` → 显式写数值的 enum（数值与 Go 一致；成员名 = Go 常量名去类型前缀）。
// 3) `core.Arena[T]` 不移植（§3.2）；本文件不出现 arena。
// 4) Go 的 `any`（TypeSystemEntity）→ `object?`（checker 只往 target 里放指针类型，见 pushTypeResolution）。
// 5) `func(...)` 字段 → C# 委托/Func；`map[K]V` → Dictionary<K,V>；Go 里被 append/截断的 `[]T` → List<T>。
// 6) 字段命名：Go 未导出字段 → `_camelCase`（docs/checker-plan.md §5.5）；Go 导出字段保持原名。
// 7) `maxSerializationLevel` → `MaxSerializationLevel`（const 任何可见性 PascalCase）。
namespace TypeScript.Checker;

using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using TypeScript.JsNum;
using static TypeScript.Xxh3.Xxh3;

// CheckMode

/// <summary>Go: <c>type CheckMode uint32</c>（checker.go:36）。</summary>
public enum CheckMode : uint
{
    /// <summary>Normal type checking</summary>
    Normal = 0,
    /// <summary>Explicitly assigned contextual type, therefore not cacheable</summary>
    Contextual = 1 << 0,
    /// <summary>Inferential typing</summary>
    Inferential = 1 << 1,
    /// <summary>Skip context sensitive function expressions</summary>
    SkipContextSensitive = 1 << 2,
    /// <summary>Skip single signature generic functions</summary>
    SkipGenericFunctions = 1 << 3,
    /// <summary>Call resolution for purposes of signature help</summary>
    IsForSignatureHelp = 1 << 4,
    /// <summary>
    /// Checking a type that is going to be used to determine the type of a rest binding element
    /// e.g. in `const { a, ...rest } = foo`, when checking the type of `foo` to determine the type of `rest`,
    /// we need to preserve generic types instead of substituting them for constraints
    /// </summary>
    RestBindingElement = 1 << 5,
    /// <summary>Called from getTypeOfExpression, diagnostics may be omitted</summary>
    TypeOnly = 1 << 6,
    ForceTuple = 1 << 7,
}

// Go 的 `type TypeSystemEntity any` 在 C# 里就是 `object?`（不生成类型；docs §3.2 的 "Go 类型别名 → 直接类型"）。

/// <summary>Go: <c>type TypeSystemPropertyName int32</c>（checker.go:54）。</summary>
public enum TypeSystemPropertyName
{
    Type = 0,
    ResolvedBaseConstructorType = 1,
    DeclaredType = 2,
    ResolvedReturnType = 3,
    ResolvedBaseConstraint = 4,
    ResolvedTypeArguments = 5,
    ResolvedBaseTypes = 6,
    WriteType = 7,
    InitializerIsUndefined = 8,
    AliasTarget = 9,
}

/// <summary>Go: <c>type TypeResolution struct</c>（checker.go:69）。</summary>
public sealed class TypeResolution
{
    internal object? _target; // Go: target TypeSystemEntity
    internal TypeSystemPropertyName _propertyName;
    internal bool _result;

    public TypeResolution()
    {
    }

    public TypeResolution(object? target, TypeSystemPropertyName propertyName, bool result)
    {
        _target = target;
        _propertyName = propertyName;
        _result = result;
    }
}

// ContextualInfo

/// <summary>Go: <c>type ContextualInfo struct</c>（checker.go:77）。</summary>
public sealed class ContextualInfo
{
    internal Node? _node;
    internal Type? _t;
    internal bool _isCache;

    public ContextualInfo()
    {
    }

    public ContextualInfo(Node? node, Type? t, bool isCache)
    {
        _node = node;
        _t = t;
        _isCache = isCache;
    }
}

// InferenceContextInfo

/// <summary>Go: <c>type InferenceContextInfo struct</c>（checker.go:85）。</summary>
public sealed class InferenceContextInfo
{
    internal Node? _node;
    internal InferenceContext? _context;

    public InferenceContextInfo()
    {
    }

    public InferenceContextInfo(Node? node, InferenceContext? context)
    {
        _node = node;
        _context = context;
    }
}

// WideningKind

/// <summary>Go: <c>type WideningKind int32</c>（checker.go:92）。</summary>
public enum WideningKind
{
    Normal = 0,
    FunctionReturn = 1,
    GeneratorNext = 2,
    GeneratorYield = 3,
}

// EnumLiteralKey

/// <summary>Go: <c>type EnumLiteralKey struct</c>（checker.go:103）。</summary>
/// <remarks>
/// Go 的 <c>value any</c> 参与结构体比较（接口相等 = 动态类型 + 值），C# 用 <c>object.Equals</c> 复刻。
/// </remarks>
public sealed class EnumLiteralKey : IEquatable<EnumLiteralKey>
{
    internal Symbol? _enumSymbol;
    internal object? _value;

    public EnumLiteralKey()
    {
    }

    public EnumLiteralKey(Symbol? enumSymbol, object? value)
    {
        _enumSymbol = enumSymbol;
        _value = value;
    }

    public bool Equals(EnumLiteralKey? other)
    {
        return other is not null && ReferenceEquals(_enumSymbol, other._enumSymbol) && Equals(_value, other._value);
    }

    public override bool Equals(object? obj)
    {
        return obj is EnumLiteralKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_enumSymbol, _value);
    }
}

// EnumRelationKey

/// <summary>Go: <c>type EnumRelationKey struct</c>（checker.go:110）。</summary>
public sealed class EnumRelationKey : IEquatable<EnumRelationKey>
{
    internal SymbolId _sourceId;
    internal SymbolId _targetId;

    public EnumRelationKey()
    {
    }

    public EnumRelationKey(SymbolId sourceId, SymbolId targetId)
    {
        _sourceId = sourceId;
        _targetId = targetId;
    }

    public bool Equals(EnumRelationKey? other)
    {
        return other is not null && _sourceId.Equals(other._sourceId) && _targetId.Equals(other._targetId);
    }

    public override bool Equals(object? obj)
    {
        return obj is EnumRelationKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_sourceId, _targetId);
    }
}

// TypeCacheKind

/// <summary>Go: <c>type CachedTypeKind int32</c>（checker.go:117）。</summary>
public enum CachedTypeKind
{
    LiteralUnionBaseType = 0,
    IndexType = 1,
    StringIndexType = 2,
    EquivalentBaseType = 3,
    ApparentType = 4,
    AwaitedType = 5,
    EvolvingArrayType = 6,
    ArrayLiteralType = 7,
    PermissiveInstantiation = 8,
    RestrictiveInstantiation = 9,
    RestrictiveTypeParameter = 10,
    IndexedAccessForReading = 11,
    IndexedAccessForWriting = 12,
    Widened = 13,
    RegularObjectLiteral = 14,
    PromisedTypeOfPromise = 15,
    DefaultOnlyType = 16,
    SyntheticType = 17,
    DecoratorContext = 18,
    DecoratorContextStatic = 19,
    DecoratorContextPrivate = 20,
    DecoratorContextPrivateStatic = 21,
}

// CachedTypeKey

/// <summary>Go: <c>type CachedTypeKey struct</c>（checker.go:146）。</summary>
public sealed class CachedTypeKey : IEquatable<CachedTypeKey>
{
    internal CachedTypeKind _kind;
    internal TypeId _typeId;

    public CachedTypeKey()
    {
    }

    public CachedTypeKey(CachedTypeKind kind, TypeId typeId)
    {
        _kind = kind;
        _typeId = typeId;
    }

    public bool Equals(CachedTypeKey? other)
    {
        return other is not null && _kind == other._kind && _typeId.Equals(other._typeId);
    }

    public override bool Equals(object? obj)
    {
        return obj is CachedTypeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_kind, _typeId);
    }
}

// NarrowedTypeKey

/// <summary>Go: <c>type NarrowedTypeKey struct</c>（checker.go:153）。</summary>
public sealed class NarrowedTypeKey : IEquatable<NarrowedTypeKey>
{
    internal Type? _t;
    internal Type? _candidate;
    internal bool _assumeTrue;
    internal bool _checkDerived;

    public NarrowedTypeKey()
    {
    }

    public NarrowedTypeKey(Type? t, Type? candidate, bool assumeTrue, bool checkDerived)
    {
        _t = t;
        _candidate = candidate;
        _assumeTrue = assumeTrue;
        _checkDerived = checkDerived;
    }

    public bool Equals(NarrowedTypeKey? other)
    {
        return other is not null
            && ReferenceEquals(_t, other._t)
            && ReferenceEquals(_candidate, other._candidate)
            && _assumeTrue == other._assumeTrue
            && _checkDerived == other._checkDerived;
    }

    public override bool Equals(object? obj)
    {
        return obj is NarrowedTypeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_t, _candidate, _assumeTrue, _checkDerived);
    }
}

// UnionOfUnionKey

/// <summary>Go: <c>type UnionOfUnionKey struct</c>（checker.go:162）。</summary>
public sealed class UnionOfUnionKey : IEquatable<UnionOfUnionKey>
{
    internal TypeId _id1;
    internal TypeId _id2;
    internal UnionReduction _r;
    internal CacheHashKey _a;

    public UnionOfUnionKey()
    {
    }

    public UnionOfUnionKey(TypeId id1, TypeId id2, UnionReduction r, CacheHashKey a)
    {
        _id1 = id1;
        _id2 = id2;
        _r = r;
        _a = a;
    }

    public bool Equals(UnionOfUnionKey? other)
    {
        return other is not null
            && _id1.Equals(other._id1)
            && _id2.Equals(other._id2)
            && _r == other._r
            && _a.Equals(other._a);
    }

    public override bool Equals(object? obj)
    {
        return obj is UnionOfUnionKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_id1, _id2, _r, _a);
    }
}

// CachedSignatureKey

/// <summary>Go: <c>type CachedSignatureKey struct</c>（checker.go:171）。</summary>
public sealed class CachedSignatureKey : IEquatable<CachedSignatureKey>
{
    internal Signature? _sig;
    internal CacheHashKey _key; // Type list key or one of the special keys below

    public CachedSignatureKey()
    {
    }

    public CachedSignatureKey(Signature? sig, CacheHashKey key)
    {
        _sig = sig;
        _key = key;
    }

    public bool Equals(CachedSignatureKey? other)
    {
        return other is not null && ReferenceEquals(_sig, other._sig) && _key.Equals(other._key);
    }

    public override bool Equals(object? obj)
    {
        return obj is CachedSignatureKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_sig, _key);
    }
}

public sealed partial class Checker
{
    /// <summary>Go: checker.go:176-182 的五个签名缓存特殊键（Go 导出包级 var → public static 成员）。</summary>
    public static readonly CacheHashKey SignatureKeyErased = new(HashString128("-"));
    public static readonly CacheHashKey SignatureKeyCanonical = new(HashString128("*"));
    public static readonly CacheHashKey SignatureKeyBase = new(HashString128("#"));
    public static readonly CacheHashKey SignatureKeyInner = new(HashString128("<"));
    public static readonly CacheHashKey SignatureKeyOuter = new(HashString128(">"));
}

// StringMappingKey

/// <summary>Go: <c>type StringMappingKey struct</c>（checker.go:186）。</summary>
public sealed class StringMappingKey : IEquatable<StringMappingKey>
{
    internal Symbol? _s;
    internal Type? _t;

    public StringMappingKey()
    {
    }

    public StringMappingKey(Symbol? s, Type? t)
    {
        _s = s;
        _t = t;
    }

    public bool Equals(StringMappingKey? other)
    {
        return other is not null && ReferenceEquals(_s, other._s) && ReferenceEquals(_t, other._t);
    }

    public override bool Equals(object? obj)
    {
        return obj is StringMappingKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_s, _t);
    }
}

// AssignmentReducedKey

/// <summary>Go: <c>type AssignmentReducedKey struct</c>（checker.go:193）。</summary>
public sealed class AssignmentReducedKey : IEquatable<AssignmentReducedKey>
{
    internal TypeId _id1;
    internal TypeId _id2;

    public AssignmentReducedKey()
    {
    }

    public AssignmentReducedKey(TypeId id1, TypeId id2)
    {
        _id1 = id1;
        _id2 = id2;
    }

    public bool Equals(AssignmentReducedKey? other)
    {
        return other is not null && _id1.Equals(other._id1) && _id2.Equals(other._id2);
    }

    public override bool Equals(object? obj)
    {
        return obj is AssignmentReducedKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_id1, _id2);
    }
}

// DiscriminatedContextualTypeKey

/// <summary>Go: <c>type DiscriminatedContextualTypeKey struct</c>（checker.go:200）。</summary>
public sealed class DiscriminatedContextualTypeKey : IEquatable<DiscriminatedContextualTypeKey>
{
    internal NodeId _nodeId;
    internal TypeId _typeId;

    public DiscriminatedContextualTypeKey()
    {
    }

    public DiscriminatedContextualTypeKey(NodeId nodeId, TypeId typeId)
    {
        _nodeId = nodeId;
        _typeId = typeId;
    }

    public bool Equals(DiscriminatedContextualTypeKey? other)
    {
        return other is not null && _nodeId.Equals(other._nodeId) && _typeId.Equals(other._typeId);
    }

    public override bool Equals(object? obj)
    {
        return obj is DiscriminatedContextualTypeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_nodeId, _typeId);
    }
}

// InstantiationExpressionKey

/// <summary>Go: <c>type InstantiationExpressionKey struct</c>（checker.go:207）。</summary>
public sealed class InstantiationExpressionKey : IEquatable<InstantiationExpressionKey>
{
    internal NodeId _nodeId;
    internal TypeId _typeId;

    public InstantiationExpressionKey()
    {
    }

    public InstantiationExpressionKey(NodeId nodeId, TypeId typeId)
    {
        _nodeId = nodeId;
        _typeId = typeId;
    }

    public bool Equals(InstantiationExpressionKey? other)
    {
        return other is not null && _nodeId.Equals(other._nodeId) && _typeId.Equals(other._typeId);
    }

    public override bool Equals(object? obj)
    {
        return obj is InstantiationExpressionKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_nodeId, _typeId);
    }
}

// SubstitutionTypeKey

/// <summary>Go: <c>type SubstitutionTypeKey struct</c>（checker.go:214）。</summary>
public sealed class SubstitutionTypeKey : IEquatable<SubstitutionTypeKey>
{
    internal TypeId _baseId;
    internal TypeId _constraintId;

    public SubstitutionTypeKey()
    {
    }

    public SubstitutionTypeKey(TypeId baseId, TypeId constraintId)
    {
        _baseId = baseId;
        _constraintId = constraintId;
    }

    public bool Equals(SubstitutionTypeKey? other)
    {
        return other is not null && _baseId.Equals(other._baseId) && _constraintId.Equals(other._constraintId);
    }

    public override bool Equals(object? obj)
    {
        return obj is SubstitutionTypeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_baseId, _constraintId);
    }
}

// ReverseMappedTypeKey

/// <summary>Go: <c>type ReverseMappedTypeKey struct</c>（checker.go:221）。</summary>
public sealed class ReverseMappedTypeKey : IEquatable<ReverseMappedTypeKey>
{
    internal TypeId _sourceId;
    internal TypeId _targetId;
    internal TypeId _constraintId;

    public ReverseMappedTypeKey()
    {
    }

    public ReverseMappedTypeKey(TypeId sourceId, TypeId targetId, TypeId constraintId)
    {
        _sourceId = sourceId;
        _targetId = targetId;
        _constraintId = constraintId;
    }

    public bool Equals(ReverseMappedTypeKey? other)
    {
        return other is not null
            && _sourceId.Equals(other._sourceId)
            && _targetId.Equals(other._targetId)
            && _constraintId.Equals(other._constraintId);
    }

    public override bool Equals(object? obj)
    {
        return obj is ReverseMappedTypeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_sourceId, _targetId, _constraintId);
    }
}

// IterationTypesKey

/// <summary>Go: <c>type IterationTypesKey struct</c>（checker.go:229）。</summary>
public sealed class IterationTypesKey : IEquatable<IterationTypesKey>
{
    internal TypeId _typeId;
    internal IterationUse _use;

    public IterationTypesKey()
    {
    }

    public IterationTypesKey(TypeId typeId, IterationUse use)
    {
        _typeId = typeId;
        _use = use;
    }

    public bool Equals(IterationTypesKey? other)
    {
        return other is not null && _typeId.Equals(other._typeId) && _use == other._use;
    }

    public override bool Equals(object? obj)
    {
        return obj is IterationTypesKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_typeId, _use);
    }
}

// PropertiesTypesKey

/// <summary>Go: <c>type PropertiesTypesKey struct</c>（checker.go:236）。</summary>
public sealed class PropertiesTypesKey : IEquatable<PropertiesTypesKey>
{
    internal TypeId _typeId;
    internal TypeFlags _include;
    internal bool _includeOrigin;
    internal bool _unresolvedMembers;

    public PropertiesTypesKey()
    {
    }

    public PropertiesTypesKey(TypeId typeId, TypeFlags include, bool includeOrigin, bool unresolvedMembers)
    {
        _typeId = typeId;
        _include = include;
        _includeOrigin = includeOrigin;
        _unresolvedMembers = unresolvedMembers;
    }

    public bool Equals(PropertiesTypesKey? other)
    {
        return other is not null
            && _typeId.Equals(other._typeId)
            && _include == other._include
            && _includeOrigin == other._includeOrigin
            && _unresolvedMembers == other._unresolvedMembers;
    }

    public override bool Equals(object? obj)
    {
        return obj is PropertiesTypesKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_typeId, _include, _includeOrigin, _unresolvedMembers);
    }
}

// NonExistentPropertyKey

/// <summary>Go: <c>type NonExistentPropertyKey struct</c>（checker.go:245）。</summary>
public sealed class NonExistentPropertyKey : IEquatable<NonExistentPropertyKey>
{
    internal Node? _propNode;
    internal Type? _containingType;
    internal bool _isUncheckedJS;

    public NonExistentPropertyKey()
    {
    }

    public NonExistentPropertyKey(Node? propNode, Type? containingType, bool isUncheckedJS)
    {
        _propNode = propNode;
        _containingType = containingType;
        _isUncheckedJS = isUncheckedJS;
    }

    public bool Equals(NonExistentPropertyKey? other)
    {
        return other is not null
            && ReferenceEquals(_propNode, other._propNode)
            && ReferenceEquals(_containingType, other._containingType)
            && _isUncheckedJS == other._isUncheckedJS;
    }

    public override bool Equals(object? obj)
    {
        return obj is NonExistentPropertyKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_propNode, _containingType, _isUncheckedJS);
    }
}

// FlowLoopKey

/// <summary>Go: <c>type FlowLoopKey struct</c>（checker.go:253）。</summary>
public sealed class FlowLoopKey : IEquatable<FlowLoopKey>
{
    internal FlowNode? _flowNode;
    internal CacheHashKey _refKey;

    public FlowLoopKey()
    {
    }

    public FlowLoopKey(FlowNode? flowNode, CacheHashKey refKey)
    {
        _flowNode = flowNode;
        _refKey = refKey;
    }

    public bool Equals(FlowLoopKey? other)
    {
        return other is not null && ReferenceEquals(_flowNode, other._flowNode) && _refKey.Equals(other._refKey);
    }

    public override bool Equals(object? obj)
    {
        return obj is FlowLoopKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_flowNode, _refKey);
    }
}

/// <summary>Go: <c>type FlowLoopInfo struct</c>（checker.go:258）。</summary>
public sealed class FlowLoopInfo
{
    internal FlowLoopKey? _key;
    internal Type[]? _types;

    public FlowLoopInfo()
    {
    }

    public FlowLoopInfo(FlowLoopKey? key, Type[]? types)
    {
        _key = key;
        _types = types;
    }
}

// InferenceFlags

/// <summary>Go: <c>type InferenceFlags uint32</c>（checker.go:265）。</summary>
public enum InferenceFlags : uint
{
    /// <summary>No special inference behaviors</summary>
    None = 0,
    /// <summary>Infer silentNeverType for no inferences (otherwise anyType or unknownType)</summary>
    NoDefault = 1 << 0,
    /// <summary>Infer anyType (in JS files) for no inferences (otherwise unknownType)</summary>
    AnyDefault = 1 << 1,
    /// <summary>A generic function was skipped during inference</summary>
    SkippedGenericFunction = 1 << 2,
    NoConstraintChecks = 1 << 3,
}

// InferenceContext

/// <summary>Go: <c>type InferenceContext struct</c>（checker.go:277）。</summary>
public sealed class InferenceContext
{
    internal List<InferenceInfo> _inferences = []; // Inferences made for each type parameter
    internal Signature? _signature; // Generic signature for which inferences are made (if any)
    internal InferenceFlags _flags; // Inference flags
    internal TypeComparer? _compareTypes; // Type comparer function
    internal TypeMapper? _mapper; // Mapper that fixes inferences
    internal TypeMapper? _nonFixingMapper; // Mapper that doesn't fix inferences
    internal TypeMapper? _returnMapper; // Type mapper for inferences from return types (if any)
    internal TypeMapper? _outerReturnMapper; // Type mapper for inferences from return types of outer function (if any)
    internal List<Type> _inferredTypeParameters = []; // Inferred type parameters for function result
    internal List<IntraExpressionInferenceSite> _intraExpressionInferenceSites = [];
}

/// <summary>Go: <c>type InferenceInfo struct</c>（checker.go:290）。</summary>
public sealed class InferenceInfo
{
    internal Type? _typeParameter; // Type parameter for which inferences are being made
    internal List<Type> _candidates = []; // Candidates in covariant positions in decreasing depth order
    internal List<Type> _contraCandidates = []; // Candidates in contravariant positions
    internal Type? _inferredType; // Cache for resolved inferred type
    internal InferencePriority _priority; // Priority of current inference set
    internal bool _topLevel; // True if all inferences are to top level occurrences
    internal bool _isFixed; // True if inferences are fixed
    internal int _impliedArity; // Implied arity (or -1)
}

/// <summary>Go: <c>type InferencePriority int32</c>（checker.go:301）。</summary>
public enum InferencePriority
{
    None = 0,
    /// <summary>Naked type variable in union or intersection type</summary>
    NakedTypeVariable = 1 << 0,
    /// <summary>Speculative tuple inference</summary>
    SpeculativeTuple = 1 << 1,
    /// <summary>Source of inference originated within a substitution type's substitute</summary>
    SubstituteSource = 1 << 2,
    /// <summary>Reverse inference for homomorphic mapped type</summary>
    HomomorphicMappedType = 1 << 3,
    /// <summary>Partial reverse inference for homomorphic mapped type</summary>
    PartialHomomorphicMappedType = 1 << 4,
    /// <summary>Reverse inference for mapped type</summary>
    MappedTypeConstraint = 1 << 5,
    /// <summary>Conditional type in contravariant position</summary>
    ContravariantConditional = 1 << 6,
    /// <summary>Inference made from return type of generic function</summary>
    ReturnType = 1 << 7,
    /// <summary>Inference made from a string literal to a keyof T</summary>
    LiteralKeyof = 1 << 8,
    /// <summary>Don't infer from constraints of instantiable types</summary>
    NoConstraints = 1 << 9,
    /// <summary>Always use strict rules for contravariant inferences</summary>
    AlwaysStrict = 1 << 10,
    /// <summary>Seed for inference priority tracking</summary>
    MaxValue = 1 << 11,
    /// <summary>Inference circularity (value less than all other priorities)</summary>
    Circularity = -1,
    /// <summary>These priorities imply that the resulting type should be a combination of all candidates</summary>
    PriorityImpliesCombination = ReturnType | MappedTypeConstraint | LiteralKeyof,
}

/// <summary>Go: <c>type IntraExpressionInferenceSite struct</c>（checker.go:322）。</summary>
public sealed class IntraExpressionInferenceSite
{
    internal Node? _node;
    internal Type? _t;

    public IntraExpressionInferenceSite()
    {
    }

    public IntraExpressionInferenceSite(Node? node, Type? t)
    {
        _node = node;
        _t = t;
    }
}

/// <summary>Go: <c>type DeclarationMeaning uint32</c>（checker.go:327）。</summary>
public enum DeclarationMeaning : uint
{
    GetAccessor = 1 << 0,
    SetAccessor = 1 << 1,
    PropertyAssignment = 1 << 2,
    Method = 1 << 3,
    PrivateStatic = 1 << 4,
    GetOrSetAccessor = GetAccessor | SetAccessor,
    PropertyAssignmentOrMethod = PropertyAssignment | Method,
}

/// <summary>Go: <c>type DeclarationSpaces int32</c>（checker.go:339）。</summary>
public enum DeclarationSpaces
{
    None = 0,
    ExportValue = 1 << 0,
    ExportType = 1 << 1,
    ExportNamespace = 1 << 2,
}

// IntrinsicTypeKind

/// <summary>Go: <c>type IntrinsicTypeKind int32</c>（checker.go:350）。</summary>
public enum IntrinsicTypeKind
{
    Unknown = 0,
    Uppercase = 1,
    Lowercase = 2,
    Capitalize = 3,
    Uncapitalize = 4,
    NoInfer = 5,
}

public sealed partial class Checker
{
    /// <summary>Go: checker.go:361 的包级 var <c>intrinsicTypeKinds</c>（Go 未导出 → <c>_camelCase</c> static 字段）。</summary>
    internal static readonly Dictionary<string, IntrinsicTypeKind> IntrinsicTypeKinds = new()
    {
        ["Uppercase"] = IntrinsicTypeKind.Uppercase,
        ["Lowercase"] = IntrinsicTypeKind.Lowercase,
        ["Capitalize"] = IntrinsicTypeKind.Capitalize,
        ["Uncapitalize"] = IntrinsicTypeKind.Uncapitalize,
        ["NoInfer"] = IntrinsicTypeKind.NoInfer,
    };
}

/// <summary>Go: <c>type MappedTypeModifiers uint32</c>（checker.go:369）。</summary>
public enum MappedTypeModifiers : uint
{
    IncludeReadonly = 1 << 0,
    ExcludeReadonly = 1 << 1,
    IncludeOptional = 1 << 2,
    ExcludeOptional = 1 << 3,
}

/// <summary>Go: <c>type MappedTypeNameTypeKind int32</c>（checker.go:378）。</summary>
public enum MappedTypeNameTypeKind
{
    None = 0,
    Filtering = 1,
    Remapping = 2,
}

/// <summary>Go: <c>type ReferenceHint int32</c>（checker.go:386）。</summary>
public enum ReferenceHint
{
    Unspecified = 0,
    Identifier = 1,
    Property = 2,
    ExportAssignment = 3,
    Jsx = 4,
    ExportImportEquals = 5,
    ExportSpecifier = 6,
    Decorator = 7,
}

/// <summary>Go: <c>type TypeFacts uint32</c>（checker.go:399）。</summary>
public enum TypeFacts : uint
{
    None = 0,
    TypeofEQString = 1 << 0,
    TypeofEQNumber = 1 << 1,
    TypeofEQBigInt = 1 << 2,
    TypeofEQBoolean = 1 << 3,
    TypeofEQSymbol = 1 << 4,
    TypeofEQObject = 1 << 5,
    TypeofEQFunction = 1 << 6,
    TypeofEQHostObject = 1 << 7,
    TypeofNEString = 1 << 8,
    TypeofNENumber = 1 << 9,
    TypeofNEBigInt = 1 << 10,
    TypeofNEBoolean = 1 << 11,
    TypeofNESymbol = 1 << 12,
    TypeofNEObject = 1 << 13,
    TypeofNEFunction = 1 << 14,
    TypeofNEHostObject = 1 << 15,
    EQUndefined = 1 << 16,
    EQNull = 1 << 17,
    EQUndefinedOrNull = 1 << 18,
    NEUndefined = 1 << 19,
    NENull = 1 << 20,
    NEUndefinedOrNull = 1 << 21,
    Truthy = 1 << 22,
    Falsy = 1 << 23,
    IsUndefined = 1 << 24,
    IsNull = 1 << 25,
    IsUndefinedOrNull = IsUndefined | IsNull,
    All = (1 << 27) - 1,
    // The following members encode facts about particular kinds of types for use in the getTypeFacts function.
    // The presence of a particular fact means that the given test is true for some (and possibly all) values
    // of that kind of type.
    BaseStringStrictFacts = TypeofEQString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseStringFacts = BaseStringStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    StringStrictFacts = BaseStringStrictFacts | Truthy | Falsy,
    StringFacts = BaseStringFacts | Truthy,
    EmptyStringStrictFacts = BaseStringStrictFacts | Falsy,
    EmptyStringFacts = BaseStringFacts,
    NonEmptyStringStrictFacts = BaseStringStrictFacts | Truthy,
    NonEmptyStringFacts = BaseStringFacts | Truthy,
    BaseNumberStrictFacts = TypeofEQNumber | TypeofNEString | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseNumberFacts = BaseNumberStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    NumberStrictFacts = BaseNumberStrictFacts | Truthy | Falsy,
    NumberFacts = BaseNumberFacts | Truthy,
    ZeroNumberStrictFacts = BaseNumberStrictFacts | Falsy,
    ZeroNumberFacts = BaseNumberFacts,
    NonZeroNumberStrictFacts = BaseNumberStrictFacts | Truthy,
    NonZeroNumberFacts = BaseNumberFacts | Truthy,
    BaseBigIntStrictFacts = TypeofEQBigInt | TypeofNEString | TypeofNENumber | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseBigIntFacts = BaseBigIntStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    BigIntStrictFacts = BaseBigIntStrictFacts | Truthy | Falsy,
    BigIntFacts = BaseBigIntFacts | Truthy,
    ZeroBigIntStrictFacts = BaseBigIntStrictFacts | Falsy,
    ZeroBigIntFacts = BaseBigIntFacts,
    NonZeroBigIntStrictFacts = BaseBigIntStrictFacts | Truthy,
    NonZeroBigIntFacts = BaseBigIntFacts | Truthy,
    BaseBooleanStrictFacts = TypeofEQBoolean | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull,
    BaseBooleanFacts = BaseBooleanStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    BooleanStrictFacts = BaseBooleanStrictFacts | Truthy | Falsy,
    BooleanFacts = BaseBooleanFacts | Truthy,
    FalseStrictFacts = BaseBooleanStrictFacts | Falsy,
    FalseFacts = BaseBooleanFacts,
    TrueStrictFacts = BaseBooleanStrictFacts | Truthy,
    TrueFacts = BaseBooleanFacts | Truthy,
    SymbolStrictFacts = TypeofEQSymbol | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    SymbolFacts = SymbolStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    ObjectStrictFacts = TypeofEQObject | TypeofEQHostObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEFunction | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    ObjectFacts = ObjectStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    FunctionStrictFacts = TypeofEQFunction | TypeofEQHostObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | NEUndefined | NENull | NEUndefinedOrNull | Truthy,
    FunctionFacts = FunctionStrictFacts | EQUndefined | EQNull | EQUndefinedOrNull | Falsy,
    VoidFacts = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | EQUndefined | EQUndefinedOrNull | NENull | Falsy,
    UndefinedFacts = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | TypeofNEHostObject | EQUndefined | EQUndefinedOrNull | NENull | Falsy | IsUndefined,
    NullFacts = TypeofEQObject | TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEFunction | TypeofNEHostObject | EQNull | EQUndefinedOrNull | NEUndefined | Falsy | IsNull,
    EmptyObjectStrictFacts = All & ~(EQUndefined | EQNull | EQUndefinedOrNull | IsUndefinedOrNull),
    EmptyObjectFacts = All & ~IsUndefinedOrNull,
    UnknownFacts = All & ~IsUndefinedOrNull,
    AllTypeofNE = TypeofNEString | TypeofNENumber | TypeofNEBigInt | TypeofNEBoolean | TypeofNESymbol | TypeofNEObject | TypeofNEFunction | NEUndefined,
    // Masks
    OrFactsMask = TypeofEQFunction | TypeofNEObject,
    AndFactsMask = All & ~(TypeofEQFunction | TypeofNEObject),
}

/// <summary>Go: <c>type IterationUse uint32</c>（checker.go:484）。</summary>
public enum IterationUse : uint
{
    AllowsSyncIterablesFlag = 1 << 0,
    AllowsAsyncIterablesFlag = 1 << 1,
    AllowsStringInputFlag = 1 << 2,
    ForOfFlag = 1 << 3,
    YieldStarFlag = 1 << 4,
    SpreadFlag = 1 << 5,
    DestructuringFlag = 1 << 6,
    PossiblyOutOfBounds = 1 << 7,
    // Spread, Destructuring, Array element assignment
    Element = AllowsSyncIterablesFlag,
    Spread = AllowsSyncIterablesFlag | SpreadFlag,
    Destructuring = AllowsSyncIterablesFlag | DestructuringFlag,
    ForOf = AllowsSyncIterablesFlag | AllowsStringInputFlag | ForOfFlag,
    ForAwaitOf = AllowsSyncIterablesFlag | AllowsAsyncIterablesFlag | AllowsStringInputFlag | ForOfFlag,
    YieldStar = AllowsSyncIterablesFlag | YieldStarFlag,
    AsyncYieldStar = AllowsSyncIterablesFlag | AllowsAsyncIterablesFlag | YieldStarFlag,
    GeneratorReturnType = AllowsSyncIterablesFlag,
    AsyncGeneratorReturnType = AllowsAsyncIterablesFlag,
    CacheFlags = AllowsSyncIterablesFlag | AllowsAsyncIterablesFlag | ForOfFlag,
}

/// <summary>Go: <c>type IterationTypes struct</c>（checker.go:508；C2 在其它文件里挂 partial 成员）。</summary>
public sealed partial class IterationTypes
{
    internal Type? _yieldType;
    internal Type? _returnType;
    internal Type? _nextType;

    public IterationTypes()
    {
    }

    public IterationTypes(Type? yieldType, Type? returnType, Type? nextType)
    {
        _yieldType = yieldType;
        _returnType = returnType;
        _nextType = nextType;
    }
}

/// <summary>Go: <c>type IterationTypeKind int32</c>（checker.go:514）。</summary>
public enum IterationTypeKind
{
    Yield = 0,
    Return = 1,
    Next = 2,
}

/// <summary>Go: <c>type IterationTypesResolver struct</c>（checker.go:522；C2 在其它文件里挂 partial 成员）。</summary>
public sealed partial class IterationTypesResolver
{
    internal string _iteratorSymbolName = "";
    internal Func<Type>? _getGlobalIteratorType;
    internal Func<Type>? _getGlobalIterableType;
    internal Func<Type>? _getGlobalIterableTypeChecked;
    internal Func<Type>? _getGlobalIterableIteratorType;
    internal Func<Type>? _getGlobalIterableIteratorTypeChecked;
    internal Func<Type>? _getGlobalIteratorObjectType;
    internal Func<Type>? _getGlobalGeneratorType;
    internal Func<Type[]>? _getGlobalBuiltinIteratorTypes;
    internal Func<Type, Node?, Type?>? _resolveIterationType; // Go: func(t *Type, errorNode *ast.Node) *Type（nil 时调用方 OrElse 回退）
    internal Message? _mustHaveANextMethodDiagnostic;
    internal Message? _mustBeAMethodDiagnostic;
    internal Message? _mustHaveAValueDiagnostic;
}

/// <summary>Go: <c>type WideningContext struct</c>（checker.go:538）。</summary>
/// <remarks>C5 分片（checker.go:18752 的 <c>getChildContext</c>）在本类上再挂一个 partial 文件。</remarks>
public sealed partial class WideningContext
{
    internal WideningContext? _parent; // Parent context
    internal string _propertyName = ""; // Name of property in parent
    internal List<Type>? _siblings; // Types of siblings
    internal List<Symbol>? _resolvedProperties; // Properties occurring in sibling object literals
    internal Dictionary<string, WideningContext>? _childContexts;
    internal Dictionary<Type, Type>? _widenedTypes;
}

/// <summary>Go: <c>type VarianceStackEntry struct</c>（checker.go:547）。</summary>
public sealed class VarianceStackEntry
{
    internal Symbol? _symbol;
    internal Type[]? _typeParameters;

    public VarianceStackEntry()
    {
    }

    public VarianceStackEntry(Symbol? symbol, Type[]? typeParameters)
    {
        _symbol = symbol;
        _typeParameters = typeParameters;
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>const maxSerializationLevel = 2</c>（checker.go:552）。</summary>
    internal const int MaxSerializationLevel = 2;
}
