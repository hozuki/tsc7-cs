// ported from: internal/checker/types.go:1289-1496
// （SignatureFlags、Signature、CompositeSignature、TypePredicateKind/TypePredicate、IndexInfo、Ternary、
//   TypeComparer、LanguageFeatureMinimumTargetMap 与其包级变量）
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §3.2/§3.3、docs/checker-plan.md §5.1/§5.5）：
//
// 1) `Signature`/`IndexInfo`/`CompositeSignature`/`TypePredicate` 在 Go 里都是**按指针**传递、被
//    LinkStore/PageLinkStore（`where V : class, new()`）持有 → C# 一律 `sealed class`（引用语义）。
// 2) Go 的 int32 字段（`Signature.minArgumentCount`/`resolvedMinArgumentCount`、`TypePredicate.parameterIndex`）
//    → C# `int`（docs/architecture.md §3.2）；Go 的 `int` → C# `int`。
// 3) `type Ternary int8` + `0/1/3/-1` → `enum Ternary : sbyte`（数值一致）。C# 对枚举内置了 `&`/`|`/`^`/`~`
//    运算符，所以 Go 的 `x & y` / `x |= y` / `x &= y` 原样可用；但 `<`/`>` 不可用（需要时转 sbyte）。
// 4) `type TypeComparer func(...)` → `public delegate Ternary TypeComparer(Type s, Type t, bool reportErrors);`
//    （Go 导出类型 → C# 公开委托）。
// 5) `type StringLiteralType = Type` 是 Go 的**类型别名**：C# 没有类型别名 → 不生成声明，凡出现处一律写 `Type`
//    （与 Ast 的 `EntityName = Node`、`FlowLabel = FlowNode` 同款处理）。
// 6) 包级变量 `LanguageFeatureMinimumTarget` → `Checker` 的 `public static readonly` 字段（Go 导出名保持原样）；
//    其类型是 Go 的导出结构体 → C# class（字段为 Go 导出字段 → `public` PascalCase 原名）。
// 7) Go 未导出字段 → `internal _camelCase`（docs/checker-plan.md §5.5）；`[]T` 字段 → `T[]?`（null = nil slice）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using TypeScript.Core;

// SignatureFlags

/// <summary>Go 的 <c>type SignatureFlags uint32</c>。</summary>
[Flags]
public enum SignatureFlags : uint
{
    None = 0,
    // Propagating flags
    HasRestParameter = 1u << 0,  // Indicates last parameter is rest parameter
    HasLiteralTypes = 1u << 1,   // Indicates signature is specialized
    Construct = 1u << 2,         // Indicates signature is a construct signature
    Abstract = 1u << 3,          // Indicates signature comes from an abstract class, abstract construct signature, or abstract constructor type
    // Non-propagating flags
    IsInnerCallChain = 1u << 4,                        // Indicates signature comes from a CallChain nested in an outer OptionalChain
    IsOuterCallChain = 1u << 5,                        // Indicates signature comes from a CallChain that is the outermost chain of an optional expression
    IsUntypedSignatureInJSFile = 1u << 6,              // Indicates signature is from a js file and has no types
    IsNonInferrable = 1u << 7,                         // Indicates signature comes from a non-inferrable type
    IsSignatureCandidateForOverloadFailure = 1u << 8,
    // We do not propagate `IsInnerCallChain` or `IsOuterCallChain` to instantiated signatures, as that would result in us
    // attempting to add `| undefined` on each recursive call to `getReturnTypeOfSignature` when
    // instantiating the return type.
    PropagatingFlags = HasRestParameter | HasLiteralTypes | Construct | Abstract | IsUntypedSignatureInJSFile | IsSignatureCandidateForOverloadFailure,
    CallChainFlags = IsInnerCallChain | IsOuterCallChain,
}

// Signature

/// <summary>Go 的 <c>type Signature struct</c>。</summary>
public sealed class Signature
{
    internal SignatureId _id;
    internal SignatureFlags _flags;
    internal int _minArgumentCount;
    internal int _resolvedMinArgumentCount;
    internal Node? _declaration;
    internal Type[]? _typeParameters;
    internal Symbol[]? _parameters;
    internal Symbol? _thisParameter;
    internal Type? _resolvedReturnType;
    internal TypePredicate? _resolvedTypePredicate;
    internal Signature? _target;
    internal TypeMapper? _mapper;
    internal Type? _isolatedSignatureType;
    internal CompositeSignature? _composite;

    public SignatureId Id()
    {
        return _id;
    }

    public SignatureFlags Flags()
    {
        return _flags;
    }

    public Type[] TypeParameters()
    {
        return _typeParameters ?? [];
    }

    public Node? Declaration()
    {
        return _declaration;
    }

    public Signature? Target()
    {
        return _target;
    }

    public Symbol? ThisParameter()
    {
        return _thisParameter;
    }

    public Symbol[] Parameters()
    {
        return _parameters ?? [];
    }

    public bool HasRestParameter()
    {
        return (_flags & SignatureFlags.HasRestParameter) != 0;
    }

    public int MinArgumentCount()
    {
        return _minArgumentCount;
    }
}

/// <summary>Go 的 <c>type CompositeSignature struct</c>。</summary>
public sealed class CompositeSignature
{
    internal bool _isUnion;            // True for union, false for intersection
    internal Signature[]? _signatures; // Individual signatures
}

/// <summary>Go 的 <c>type TypePredicateKind int32</c>。</summary>
public enum TypePredicateKind
{
    This = 0,
    Identifier = 1,
    AssertsThis = 2,
    AssertsIdentifier = 3,
}

/// <summary>Go 的 <c>type TypePredicate struct</c>。</summary>
public sealed class TypePredicate
{
    internal TypePredicateKind _kind;
    internal int _parameterIndex;
    internal string _parameterName = "";
    internal Type? _t;

    public Type? Type()
    {
        return _t;
    }

    public TypePredicateKind Kind()
    {
        return _kind;
    }

    public int ParameterIndex()
    {
        return _parameterIndex;
    }

    public string ParameterName()
    {
        return _parameterName;
    }
}

// IndexInfo

/// <summary>Go 的 <c>type IndexInfo struct</c>。</summary>
public sealed class IndexInfo
{
    internal Type? _keyType;
    internal Type? _valueType;
    internal bool _isReadonly;
    internal Node? _declaration;     // IndexSignatureDeclaration
    internal Symbol? _indexSymbol;   // Synthetic property symbol for this index signature
    internal Node[]? _components;    // ElementWithComputedPropertyName

    public Type? KeyType()
    {
        return _keyType;
    }

    public Type? ValueType()
    {
        return _valueType;
    }

    public bool IsReadonly()
    {
        return _isReadonly;
    }

    public Node? Declaration()
    {
        return _declaration;
    }
}

/**
 * Ternary values are defined such that
 * x & y picks the lesser in the order False < Unknown < Maybe < True, and
 * x | y picks the greater in the order False < Unknown < Maybe < True.
 * Generally, Ternary.Maybe is used as the result of a relation that depends on itself, and
 * Ternary.Unknown is used as the result of a variance check that depends on itself. We make
 * a distinction because we don't want to cache circular variance check results.
 */

/// <summary>Go 的 <c>type Ternary int8</c>（数值一致；C# 枚举自带 `&amp;`/`|`/`^`/`~`，无 `&lt;`/`&gt;`）。</summary>
public enum Ternary : sbyte
{
    False = 0,
    Unknown = 1,
    Maybe = 3,
    True = -1,
}

/// <summary>Go 的 <c>type TypeComparer func(s *Type, t *Type, reportErrors bool) Ternary</c>。</summary>
public delegate Ternary TypeComparer(Type s, Type t, bool reportErrors);

/// <summary>Go 的 <c>type LanguageFeatureMinimumTargetMap struct</c>（Go 导出字段 → C# `public` 原名）。</summary>
public sealed class LanguageFeatureMinimumTargetMap
{
    public ScriptTarget Exponentiation;
    public ScriptTarget AsyncFunctions;
    public ScriptTarget ForAwaitOf;
    public ScriptTarget AsyncGenerators;
    public ScriptTarget AsyncIteration;
    public ScriptTarget ObjectSpreadRest;
    public ScriptTarget RegularExpressionFlagsDotAll;
    public ScriptTarget BindinglessCatch;
    public ScriptTarget BigInt;
    public ScriptTarget NullishCoalesce;
    public ScriptTarget OptionalChaining;
    public ScriptTarget LogicalAssignment;
    public ScriptTarget TopLevelAwait;
    public ScriptTarget ClassFields;
    public ScriptTarget PrivateNamesAndClassStaticBlocks;
    public ScriptTarget RegularExpressionFlagsHasIndices;
    public ScriptTarget ShebangComments;
    public ScriptTarget UsingAndAwaitUsing;
    public ScriptTarget ClassAndClassElementDecorators;
    public ScriptTarget RegularExpressionFlagsUnicodeSets;
}

public sealed partial class Checker
{
    /// <summary>Go 的 <c>var LanguageFeatureMinimumTarget = LanguageFeatureMinimumTargetMap{…}</c>（types.go:1472）。</summary>
    public static readonly LanguageFeatureMinimumTargetMap LanguageFeatureMinimumTarget = new()
    {
        Exponentiation = ScriptTarget.ES2016,
        AsyncFunctions = ScriptTarget.ES2017,
        ForAwaitOf = ScriptTarget.ES2018,
        AsyncGenerators = ScriptTarget.ES2018,
        AsyncIteration = ScriptTarget.ES2018,
        ObjectSpreadRest = ScriptTarget.ES2018,
        RegularExpressionFlagsDotAll = ScriptTarget.ES2018,
        BindinglessCatch = ScriptTarget.ES2019,
        BigInt = ScriptTarget.ES2020,
        NullishCoalesce = ScriptTarget.ES2020,
        OptionalChaining = ScriptTarget.ES2020,
        LogicalAssignment = ScriptTarget.ES2021,
        TopLevelAwait = ScriptTarget.ES2022,
        ClassFields = ScriptTarget.ES2022,
        PrivateNamesAndClassStaticBlocks = ScriptTarget.ES2022,
        RegularExpressionFlagsHasIndices = ScriptTarget.ES2022,
        ShebangComments = ScriptTarget.ESNext,
        UsingAndAwaitUsing = ScriptTarget.ESNext,
        ClassAndClassElementDecorators = ScriptTarget.ESNext,
        RegularExpressionFlagsUnicodeSets = ScriptTarget.ESNext,
    };
}

// Aliases for types
// Go: `type StringLiteralType = Type` 是类型别名 → C# 不生成声明，凡出现处写 Type（见文件头第 5 条）。
