// ported from: internal/checker/types.go:18-663
// （标志位与枚举：ParseFlags…ObjectFlags、包级常量 externalHelpersModuleNameText、TypeId/SignatureId、全部链接结构体、
//   TypeFlags/ObjectFlags 及类型标志名表 FormatTypeFlags/类型、UnionOrIntersectionType 之前的全部数据模型）
//
// 与 Go 的差异（C# 语言特性；依据 docs/architecture.md §3.2/§3.3 与 docs/checker-plan.md §5.1/§5.5）：
//
// 1) `type X uint32` + 包级常量组 → `[Flags] enum X : uint`（成员名 = Go 常量名去掉类型前缀，数值逐条一致）。
//    32 位标志位一律写 `1u << n`：Go 的 `1 << n` 是无类型常量，C# 里是 `int`，赋给 `uint` 底层枚举会溢出（CS0266）。
// 2) `map[K]V` → `Dictionary<K,V>`；Go 的 nil map ↔ C# `null`（读缺失键用 `TryGetValue`，索引器会抛）。
// 3) `[]T` 字段 → `T[]?`（`null` = Go 的 nil slice，与 Ast/SourceFile.cs 的约定一致）；`*T` → `T?`。
// 4) links 家族是 checker 全包共享、Go 里按指针传递并被 LinkStore/PageLinkStore 持有 → C# 一律 `sealed class`（引用语义）。
// 5) Go 未导出字段 → `internal _camelCase`（docs/checker-plan.md §5.5）；Go 导出字段保持 PascalCase 原名、不加前缀。
// 6) `ObjectFlagsObjectTypeKindMask` 在 Go 里前向引用了 `ObjectFlagsInstantiationExpressionType`/`ObjectFlagsSingleSignatureType`
//    （Go 常量可前向引用），C# 枚举成员必须先声明后用 → 这两位就地写 `(1u << 24)`/`(1u << 25)` 并加注释。
// 7) `collections.OrderedSet[T]` 的 Go 零值是可用的空集合 → `internal OrderedSet<Node> _deferredNodes = new();`
//    （`new OrderedSet<T>()` 就是 Go 的零值，见 Collections/OrderedSet.cs 文件头第 2 条）。
// 8) Go 的 `accessibleChainCacheKey` 是未导出类型（按 C# 习惯命名为 `AccessibleChainCacheKey`），且是 map 的键
//    → 落成 readonly struct + 显式 IEquatable（`location` 用引用相等，与 Go 的指针键一致）。
// 9) `ExternalEmitHelpersESDecorateAndRunInitializers` 等是 Go 里的同值常量 → C# 枚举里的同值别名成员。
// 10) Go 的 `func (f TypeFlags) String()` / `func (v VarianceFlags) String()` 是所在类型的 stringer → `Checker` 类的
//     扩展方法 `String(this TypeFlags)` / `String(this VarianceFlags)`（docs/architecture.md §3.3 的 Core 先例）。
// 11) `SignatureKind` 的 stringer 在 stringer_generated.go（另一个分片），本文件不定义。
namespace TypeScript.Checker;

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using TypeScript.Evaluator;

// ParseFlags

/// <summary>Go 的 <c>type ParseFlags uint32</c>。</summary>
[Flags]
public enum ParseFlags : uint
{
    None = 0,
    Yield = 1u << 0,
    Await = 1u << 1,
    Type = 1u << 2,
    IgnoreMissingOpenBrace = 1u << 4,
    JSDoc = 1u << 5,
}

/// <summary>Go 的 <c>type SignatureKind int32</c>。</summary>
public enum SignatureKind
{
    Call = 0,
    Construct = 1,
}

/// <summary>Go 的 <c>type IndexKind int32</c>。</summary>
public enum IndexKind
{
    String = 0,
    Number = 1,
}

/// <summary>Go 的 <c>type MemberOverrideStatus int32</c>。</summary>
public enum MemberOverrideStatus
{
    None = 0,
    NeedsOverride = 1,
    HasInvalidOverride = 2,
}

/// <summary>Go 的 <c>type ContextFlags uint32</c>。</summary>
[Flags]
public enum ContextFlags : uint
{
    None = 0,
    Signature = 1u << 0,            // Obtaining contextual signature
    NoConstraints = 1u << 1,        // Don't obtain type variable constraints
    IgnoreNodeInferences = 1u << 2, // Ignore inference to current node and parent nodes out to the containing call for, for example, completions
    SkipBindingPatterns = 1u << 3,  // Ignore contextual types applied by binding patterns
}

/// <summary>Go 的 <c>type TypeFormatFlags uint32</c>。</summary>
[Flags]
public enum TypeFormatFlags : uint
{
    None = 0,
    NoTruncation = 1u << 0,                       // Don't truncate typeToString result
    WriteArrayAsGenericType = 1u << 1,            // Write Array<T> instead T[]
    GenerateNamesForShadowedTypeParams = 1u << 2, // When a type parameter T is shadowing another T, generate a name for it so it can still be referenced
    UseStructuralFallback = 1u << 3,              // When an alias cannot be named by its symbol, rather than report an error, fallback to a structural printout if possible
    // hole because there's a hole in node builder flags
    WriteTypeArgumentsOfSignature = 1u << 5,   // Write the type arguments instead of type parameters of the signature
    UseFullyQualifiedType = 1u << 6,           // Write out the fully qualified type name (eg. Module.Type, instead of Type)
    // hole because `UseOnlyExternalAliasing` is here in node builder flags, but functions which take old flags use `SymbolFormatFlags` instead
    SuppressAnyReturnType = 1u << 8,           // If the return type is any-like, don't offer a return type.
    // hole because `WriteTypeParametersInQualifiedName` is here in node builder flags, but functions which take old flags use `SymbolFormatFlags` for this instead
    MultilineObjectLiterals = 1u << 10,        // Always print object literals across multiple lines (only used to map into node builder flags)
    WriteClassExpressionAsTypeLiteral = 1u << 11, // Write a type literal instead of (Anonymous class)
    UseTypeOfFunction = 1u << 12,              // Write typeof instead of function type literal
    OmitParameterModifiers = 1u << 13,         // Omit modifiers on parameters
    UseAliasDefinedOutsideCurrentScope = 1u << 14, // For a `type T = ... ` defined in a different file, write `T` instead of its value, even though `T` can't be accessed in the current scope.
    UseSingleQuotesForStringLiteralType = 1u << 28, // Use single quotes for string literal type
    NoTypeReduction = 1u << 29,                // Don't call getReducedType
    UseInstantiationExpressions = 1u << 30,    // Use instantiation expressions for qualified instantiated names like Foo<string>.Bar
    OmitThisParameter = 1u << 25,
    WriteCallStyleSignature = 1u << 27,        // Write construct signatures as call style signatures
    // Error Handling
    AllowUniqueESSymbolType = 1u << 20,        // This is bit 20 to align with the same bit in `NodeBuilderFlags`
    // TypeFormatFlags exclusive
    AddUndefined = 1u << 17,                   // Add undefined to types of initialized, non-optional parameters
    WriteArrowStyleSignature = 1u << 18,       // Write arrow style signature
    // State
    InArrayType = 1u << 19,                    // Writing an array element type
    InElementType = 1u << 21,                  // Writing an array or union element type
    InFirstTypeArgument = 1u << 22,            // Writing first type argument of the instantiated type
    InTypeAlias = 1u << 23,                    // Writing type in type alias declaration

    NodeBuilderFlagsMask = NoTruncation | WriteArrayAsGenericType | GenerateNamesForShadowedTypeParams | UseStructuralFallback | WriteTypeArgumentsOfSignature |
        UseFullyQualifiedType | SuppressAnyReturnType | MultilineObjectLiterals | WriteClassExpressionAsTypeLiteral |
        UseTypeOfFunction | OmitParameterModifiers | UseAliasDefinedOutsideCurrentScope | AllowUniqueESSymbolType | InTypeAlias |
        UseInstantiationExpressions | UseSingleQuotesForStringLiteralType | NoTypeReduction | OmitThisParameter,
}

/// <summary>Go 的 <c>type SymbolFormatFlags uint32</c>。</summary>
[Flags]
public enum SymbolFormatFlags : uint
{
    None = 0,
    // Write symbols's type argument if it is instantiated symbol
    // eg. class C<T> { p: T }   <-- Show p as C<T>.p here
    //     var a: C<number>;
    //     var p = a.p; <--- Here p is property of C<number> so show it as C<number>.p instead of just C.p
    WriteTypeParametersOrArguments = 1u << 0,
    // Use only external alias information to get the symbol name in the given context
    // eg.  module m { export class c { } } import x = m.c;
    // When this flag is specified m.c will be used to refer to the class instead of alias symbol x
    UseOnlyExternalAliasing = 1u << 1,
    // Build symbol name using any nodes needed, instead of just components of an entity name
    AllowAnyNodeKind = 1u << 2,
    // Prefer aliases which are not directly visible
    UseAliasDefinedOutsideCurrentScope = 1u << 3,
    // { [E.A]: 1 }
    WriteComputedProps = 1u << 4,
    // Skip building an accessible symbol chain
    DoNotIncludeSymbolChain = 1u << 5,
}

/// <summary>Go 的 <c>type ExternalEmitHelpers uint32</c>（Go 里用 <c>1 &lt;&lt; iota</c>，此处逐条展开）。</summary>
[Flags]
public enum ExternalEmitHelpers : uint
{
    Rest = 1u << 0,                                             // __rest (used by ESNext object rest transformation)
    Decorate = 1u << 1,                                         // __decorate (used by TypeScript decorators transformation)
    Metadata = 1u << 2,                                         // __metadata (used by TypeScript decorators transformation)
    Param = 1u << 3,                                            // __param (used by TypeScript decorators transformation)
    Awaiter = 1u << 4,                                          // __awaiter (used by ES2017 async functions transformation)
    Await = 1u << 5,                                            // __await (used by ES2017 async generator transformation)
    AsyncGenerator = 1u << 6,                                   // __asyncGenerator (used by ES2017 async generator transformation)
    AsyncDelegator = 1u << 7,                                   // __asyncDelegator (used by ES2017 async generator yield* transformation)
    AsyncValues = 1u << 8,                                      // __asyncValues (used by ES2017 for..await..of transformation)
    ExportStar = 1u << 9,                                       // __exportStar (used by CommonJS/AMD/UMD module transformation)
    ImportStar = 1u << 10,                                      // __importStar (used by CommonJS/AMD/UMD module transformation)
    ImportDefault = 1u << 11,                                   // __importDefault (used by CommonJS/AMD/UMD module transformation)
    MakeTemplateObject = 1u << 12,                              // __makeTemplateObject (used for constructing template string array objects)
    ClassPrivateFieldGet = 1u << 13,                            // __classPrivateFieldGet (used by the class private field transformation)
    ClassPrivateFieldSet = 1u << 14,                            // __classPrivateFieldSet (used by the class private field transformation)
    ClassPrivateFieldIn = 1u << 15,                             // __classPrivateFieldIn (used by the class private field transformation)
    SetFunctionName = 1u << 16,                                 // __setFunctionName (used by class fields and ECMAScript decorators)
    PropKey = 1u << 17,                                         // __propKey (used by class fields and ECMAScript decorators)
    AddDisposableResourceAndDisposeResources = 1u << 18,        // __addDisposableResource and __disposeResources (used by ESNext transformations)
    RewriteRelativeImportExtension = 1u << 19,                  // __rewriteRelativeImportExtension (used by --rewriteRelativeImportExtensions)
    ESDecorateAndRunInitializers = Decorate,                    // __esDecorate and __runInitializers (used by ECMAScript decorators transformation)

    FirstEmitHelper = Rest,
    LastEmitHelper = RewriteRelativeImportExtension,

    // Helpers included by ES2017 for..await..of
    ForAwaitOfIncludes = AsyncValues,

    // Helpers included by ES2017 async generators
    AsyncGeneratorIncludes = Await | AsyncGenerator,

    // Helpers included by yield* in ES2017 async generators
    AsyncDelegatorIncludes = Await | AsyncDelegator | AsyncValues,
}

// Ids

/// <summary>Go 的 <c>type TypeId uint32</c>：类型的唯一标识（照 Ast/Ids.cs 的 NodeId 先例，落成 readonly struct）。</summary>
public readonly struct TypeId : IEquatable<TypeId>, IComparable<TypeId>
{
    public readonly uint Value;

    public TypeId(uint value)
    {
        Value = value;
    }

    public TypeId(int value)
    {
        Value = (uint)value;
    }

    public static implicit operator uint(TypeId id) => id.Value;

    public static implicit operator TypeId(uint value) => new(value);

    public bool Equals(TypeId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is TypeId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    // Go 的 TypeId 就是 uint32：`%d` 打印数字本身（见 Ast/Ids.cs 的 NodeId 注释）。
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(TypeId other) => Value.CompareTo(other.Value);
}

/// <summary>Go 的 <c>type SignatureId uint32</c>。</summary>
public readonly struct SignatureId : IEquatable<SignatureId>, IComparable<SignatureId>
{
    public readonly uint Value;

    public SignatureId(uint value)
    {
        Value = value;
    }

    public SignatureId(int value)
    {
        Value = (uint)value;
    }

    public static implicit operator uint(SignatureId id) => id.Value;

    public static implicit operator SignatureId(uint value) => new(value);

    public bool Equals(SignatureId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is SignatureId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    public int CompareTo(SignatureId other) => Value.CompareTo(other.Value);
}

// Links for referenced symbols

/// <summary>Go 的 <c>type SymbolReferenceLinks struct</c>。</summary>
public sealed class SymbolReferenceLinks
{
    /// <summary>Flags for the meanings of the symbol that were referenced.</summary>
    internal SymbolFlags _referenceKinds;
}

// Links for value symbols

/// <summary>Go 的 <c>type ValueSymbolLinks struct</c>。</summary>
public sealed class ValueSymbolLinks
{
    internal Type? _resolvedType;      // Type of value symbol
    internal Type? _writeType;
    internal Symbol? _target;
    internal TypeMapper? _mapper;
    internal Type? _nameType;
    internal Type? _containingType;    // Mapped type for mapped type property, containing union or intersection type for synthetic property
    internal bool _functionOrConstructorChecked;
}

// Additional links for mapped symbols

/// <summary>Go 的 <c>type MappedSymbolLinks struct</c>。</summary>
public sealed class MappedSymbolLinks
{
    internal Type? _keyType;         // Key type for mapped type member
    internal Symbol? _syntheticOrigin; // For a property on a mapped or spread type, points back to the original property
}

// Additional links for deferred type symbols

/// <summary>Go 的 <c>type DeferredSymbolLinks struct</c>。</summary>
public sealed class DeferredSymbolLinks
{
    internal Type? _parent;                  // Source union/intersection of a deferred type
    internal Type[]? _constituents;          // Calculated list of constituents for a deferred type
    internal Type[]? _writeConstituents;     // Constituents of a deferred `writeType`
}

// Links for alias symbols

/// <summary>Go 的 <c>type AliasSymbolLinks struct</c>。</summary>
public sealed class AliasSymbolLinks
{
    internal Symbol? _immediateTarget;  // Immediate target of an alias. May be another alias. Do not access directly, use `checker.getImmediateAliasedSymbol` instead.
    internal Symbol? _aliasTarget;      // Resolved (non-alias) target of an alias
    internal bool _referenced;          // True if alias symbol has been referenced as a value that can be emitted
    internal Node? _typeOnlyDeclaration; // First resolved alias declaration that makes the symbol only usable in type constructs
}

// Links for module symbols

/// <summary>Go 的 <c>type ModuleSymbolLinks struct</c>。</summary>
public sealed class ModuleSymbolLinks
{
    internal SymbolTable? _resolvedExports;                  // Resolved exports of module or combined early- and late-bound static members of a class.
    internal Dictionary<string, Node>? _typeOnlyExportStarMap; // Set on a module symbol when some of its exports were resolved through a 'export type * from "mod"' declaration
    internal bool _exportsChecked;
}

/// <summary>Go 的 <c>type ReverseMappedSymbolLinks struct</c>。</summary>
public sealed class ReverseMappedSymbolLinks
{
    internal Type? _propertyType;
    internal Type? _mappedType;     // References a mapped type
    internal Type? _constraintType; // References an index type
}

// Links for late-bound symbols

/// <summary>Go 的 <c>type LateBoundLinks struct</c>。</summary>
public sealed class LateBoundLinks
{
    internal Symbol? _lateSymbol;
}

// Links for export type symbols

/// <summary>Go 的 <c>type ExportTypeLinks struct</c>。</summary>
public sealed class ExportTypeLinks
{
    internal Symbol? _target;           // Target symbol
    internal Node? _originatingImport;  // Import declaration which produced the symbol, present if the symbol is marked as uncallable but had call signatures in `resolveESModuleSymbol`
}

// Links for type aliases

/// <summary>Go 的 <c>type TypeAliasLinks struct</c>。</summary>
public sealed class TypeAliasLinks
{
    internal Type? _declaredType;
    internal Type[]? _typeParameters;                          // Type parameters of type alias (undefined if non-generic)
    internal Dictionary<CacheHashKey, Type>? _instantiations;  // Instantiations of generic type alias (undefined if non-generic)
    internal bool _isConstructorDeclaredProperty;
}

// Links for declared types (type parameters, class types, interface types, enums)

/// <summary>Go 的 <c>type DeclaredTypeLinks struct</c>。</summary>
public sealed class DeclaredTypeLinks
{
    internal Type? _declaredType;
    internal bool _interfaceChecked;
    internal bool _indexSignaturesChecked;
    internal bool _typeParametersChecked;
    internal bool _enumChecked;
}

// Links for switch clauses

/// <summary>Go 的 <c>type ExhaustiveState byte</c>。</summary>
public enum ExhaustiveState : byte
{
    Unknown = 0,   // Exhaustive state not computed
    Computing = 1, // Exhaustive state computation in progress
    False = 2,     // Switch statement is not exhaustive
    True = 3,      // Switch statement is exhaustive
}

/// <summary>Go 的 <c>type SwitchStatementLinks struct</c>。</summary>
public sealed class SwitchStatementLinks
{
    internal ExhaustiveState _exhaustiveState; // Switch statement exhaustiveness
    internal bool _switchTypesComputed;
    internal bool _witnessesComputed;
    internal Type[]? _switchTypes;
    internal string[]? _witnesses;
}

/// <summary>Go 的 <c>type ArrayLiteralLinks struct</c>。</summary>
public sealed class ArrayLiteralLinks
{
    internal bool _indicesComputed;
    internal int _firstSpreadIndex; // Index of first spread expression (or -1 if none)
    internal int _lastSpreadIndex;  // Index of last spread expression (or -1 if none)
}

// Links for late-binding containers

/// <summary>Go 的 <c>type MembersOrExportsResolutionKind int</c>。</summary>
public enum MembersOrExportsResolutionKind
{
    ResolvedExports = 0,
    ResolvedMembers = 1,
}

/// <summary>Go 的 <c>type MembersAndExportsLinks [2]ast.SymbolTable</c>（按 MembersOrExportsResolutionKind 索引）。</summary>
public sealed class MembersAndExportsLinks
{
    private readonly SymbolTable?[] _items = new SymbolTable?[2];

    public SymbolTable? this[MembersOrExportsResolutionKind kind]
    {
        get => _items[(int)kind];
        set => _items[(int)kind] = value;
    }
}

// Links for synthetic spread properties

/// <summary>Go 的 <c>type SpreadLinks struct</c>。</summary>
public sealed class SpreadLinks
{
    internal Symbol? _leftSpread;  // Left source for synthetic spread property
    internal Symbol? _rightSpread; // Right source for synthetic spread property
}

// Links for variances of type aliases and interface types

/// <summary>Go 的 <c>type VarianceLinks struct</c>。</summary>
public sealed class VarianceLinks
{
    internal VarianceFlags[]? _variances;
}

/// <summary>Go 的 <c>type VarianceFlags uint32</c>。</summary>
[Flags]
public enum VarianceFlags : uint
{
    Invariant = 0,                                                             // Neither covariant nor contravariant
    Covariant = 1u << 0,                                                       // Covariant
    Contravariant = 1u << 1,                                                   // Contravariant
    Bivariant = Covariant | Contravariant,                                     // Both covariant and contravariant
    Independent = 1u << 2,                                                     // Unwitnessed type parameter
    VarianceMask = Invariant | Covariant | Contravariant | Independent,        // Mask containing all measured variances without the unmeasurable flag
    Unmeasurable = 1u << 3,                                                    // Variance result is unusable - relationship relies on structural comparisons which are not reflected in generic relationships
    Unreliable = 1u << 4,                                                      // Variance result is unreliable - checking may produce false negatives, but not false positives
    AllowsStructuralFallback = Unmeasurable | Unreliable,
}

/// <summary>Go 的 <c>type MarkedAssignmentSymbolLinks struct</c>。</summary>
public sealed class MarkedAssignmentSymbolLinks
{
    internal int _lastAssignmentPos;
    internal bool _hasDefiniteAssignment; // Symbol is definitely assigned somewhere
}

/// <summary>
/// Go 的 <c>type accessibleChainCacheKey struct</c>（未导出类型 → C# 按习惯命名为 PascalCase）。
/// 是 <c>map[accessibleChainCacheKey][]*ast.Symbol</c> 的键 → 必须按字段值相等（<c>location</c> 按引用相等，与 Go 的指针键一致）。
/// </summary>
public readonly struct AccessibleChainCacheKey : IEquatable<AccessibleChainCacheKey>
{
    internal readonly bool _useOnlyExternalAliasing;
    internal readonly Node? _location;
    internal readonly SymbolFlags _meaning;

    public AccessibleChainCacheKey(bool useOnlyExternalAliasing, Node? location, SymbolFlags meaning)
    {
        _useOnlyExternalAliasing = useOnlyExternalAliasing;
        _location = location;
        _meaning = meaning;
    }

    public bool Equals(AccessibleChainCacheKey other) =>
        _useOnlyExternalAliasing == other._useOnlyExternalAliasing &&
        ReferenceEquals(_location, other._location) &&
        _meaning == other._meaning;

    public override bool Equals(object? obj) => obj is AccessibleChainCacheKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_useOnlyExternalAliasing, _location is null ? 0 : RuntimeHelpers.GetHashCode(_location), _meaning);
}

/// <summary>Go 的 <c>type ContainingSymbolLinks struct</c>。</summary>
public sealed class ContainingSymbolLinks
{
    internal Dictionary<NodeId, Symbol[]>? _extendedContainersByFile;       // Symbols of nodes which which logically contain this one, cached by file the request is made within
    internal Symbol[]? _extendedContainers;                                 // Containers (other than the parent) which this symbol is aliased in
    internal Dictionary<AccessibleChainCacheKey, Symbol[]>? _accessibleChainCache;
}

/// <summary>Go 的 <c>type AccessFlags uint32</c>。</summary>
[Flags]
public enum AccessFlags : uint
{
    None = 0,
    IncludeUndefined = 1u << 0,
    NoIndexSignatures = 1u << 1,
    Writing = 1u << 2,
    CacheSymbol = 1u << 3,
    AllowMissing = 1u << 4,
    ExpressionPosition = 1u << 5,
    ReportDeprecated = 1u << 6,
    SuppressNoImplicitAnyError = 1u << 7,
    Contextual = 1u << 8,
    Persistent = IncludeUndefined,
}

/// <summary>Go 的 <c>type NodeCheckFlags uint32</c>。</summary>
[Flags]
public enum NodeCheckFlags : uint
{
    None = 0,
    TypeChecked = 1u << 0,                            // Node has been type checked
    ContextChecked = 1u << 6,                         // Contextual types have been assigned
    EnumValuesComputed = 1u << 10,                    // Values for enum members have been computed, and any errors have been reported for them.
    AssignmentsMarked = 1u << 17,                     // Parameter assignments have been marked
    ContainsClassWithPrivateIdentifiers = 1u << 20,   // Marked on all block-scoped containers containing a class with private identifiers.
    ContainsSuperPropertyInStaticInitializer = 1u << 21, // Marked on all block-scoped containers containing a static initializer with 'super.x' or 'super[x]'.
    InCheckIdentifier = 1u << 22,
    InitializerIsUndefined = 1u << 24,
    InitializerIsUndefinedComputed = 1u << 25,
}

// Common links

/// <summary>Go 的 <c>type NodeLinks struct</c>。</summary>
public sealed class NodeLinks
{
    internal NodeCheckFlags _flags;                                  // Set of flags specific to Node
    internal Tristate _declarationRequiresScopeChange;               // Set by `useOuterVariableScopeInParameter` in checker when downlevel emit would change the name resolution scope inside of a parameter.
    internal bool _hasReportedStatementInAmbientContext;             // Cache boolean if we report statements in ambient context
}

/// <summary>Go 的 <c>type SymbolNodeLinks struct</c>。</summary>
public sealed class SymbolNodeLinks
{
    internal Symbol? _resolvedSymbol; // Resolved symbol associated with node
}

/// <summary>Go 的 <c>type TypeNodeLinks struct</c>。</summary>
public sealed class TypeNodeLinks
{
    internal Type? _resolvedType;          // Resolved type associated with node
    internal Type[]? _outerTypeParameters; // Outer type parameters of anonymous object type
}

/// <summary>Go 的 <c>type ComputedNameNodeLinks struct</c>。</summary>
public sealed class ComputedNameNodeLinks
{
    internal bool? _hasName;  // If the node has a computable name
    internal string _name = ""; // Resolved name associated with the type of the node
}

// Links for enum members

/// <summary>Go 的 <c>type EnumMemberLinks struct</c>。</summary>
public sealed class EnumMemberLinks
{
    internal Result _value; // Constant value of enum member
}

// Links for assertion expressions

/// <summary>Go 的 <c>type AssertionLinks struct</c>。</summary>
public sealed class AssertionLinks
{
    internal Type? _exprType; // Assertion expression type
}

// SourceFile links

/// <summary>Go 的 <c>type SourceFileLinks struct</c>。</summary>
public sealed class SourceFileLinks
{
    internal bool _typeChecked;
    internal bool _unusedChecked;
    internal Symbol? _externalHelpersModule;
    internal ExternalEmitHelpers _requestedExternalEmitHelpers;
    internal OrderedSet<Node> _deferredNodes = new();
    // Go: identifierCheckNodes []*ast.Node —— 只追加、最后整体读取一次（`append` 的摊销 O(1) 语义）
    // → C# 用 `List<Node>?`（Go 的 nil 切片 ≡ null/空表）。
    internal List<Node>? _identifierCheckNodes;
    internal string _localJsxNamespace = "";
    internal string _localJsxFragmentNamespace = "";
    internal Node? _localJsxFactory;            // Go: *ast.EntityName（Go 的 `EntityName = Node` 别名 → C# 用 Node）
    internal Node? _localJsxFragmentFactory;    // Go: *ast.EntityName
    internal Type? _jsxFragmentType;
}

// Signature specific links

/// <summary>Go 的 <c>type SignatureLinks struct</c>。</summary>
public sealed class SignatureLinks
{
    internal Signature? _resolvedSignature;  // Cached signature of signature node or call expression
    internal Signature? _effectsSignature;   // Signature with possible control flow effects
    internal Signature? _decoratorSignature; // Signature for decorator as if invoked by the runtime
}

// TypeFlags
//
// Note that for types of different kinds, the numeric values of TypeFlags determine the order
// computed by the CompareTypes function and therefore the order of constituent types in union types.
// Since union type processing often bails out early when a result is known, it is important to order
// TypeFlags in increasing order of potential type complexity. In particular, indexed access and
// conditional types should sort last as those types are potentially recursive and possibly infinite.

/// <summary>Go 的 <c>type TypeFlags uint32</c>。</summary>
[Flags]
public enum TypeFlags : uint
{
    None = 0,
    Any = 1u << 0,
    Unknown = 1u << 1,
    Undefined = 1u << 2,
    Null = 1u << 3,
    Void = 1u << 4,
    String = 1u << 5,
    Number = 1u << 6,
    BigInt = 1u << 7,
    Boolean = 1u << 8,
    ESSymbol = 1u << 9,          // Type of symbol primitive introduced in ES6
    StringLiteral = 1u << 10,
    NumberLiteral = 1u << 11,
    BigIntLiteral = 1u << 12,
    BooleanLiteral = 1u << 13,
    UniqueESSymbol = 1u << 14,   // unique symbol
    EnumLiteral = 1u << 15,      // Always combined with StringLiteral, NumberLiteral, or Union
    Enum = 1u << 16,             // Numeric computed enum member value (must be right after EnumLiteral, see getSortOrderFlags)
    NonPrimitive = 1u << 17,     // intrinsic object type
    Never = 1u << 18,            // Never type
    TypeParameter = 1u << 19,    // Type parameter
    Object = 1u << 20,           // Object type
    Index = 1u << 21,            // keyof T
    TemplateLiteral = 1u << 22,  // Template literal type
    StringMapping = 1u << 23,    // Uppercase/Lowercase type
    Substitution = 1u << 24,     // Type parameter substitution
    IndexedAccess = 1u << 25,    // T[K]
    Conditional = 1u << 26,      // T extends U ? X : Y
    Union = 1u << 27,            // Union (T | U)
    Intersection = 1u << 28,     // Intersection (T & U)
    Reserved1 = 1u << 29,        // Used by union/intersection type construction
    Reserved2 = 1u << 30,        // Used by union/intersection type construction
    Reserved3 = 1u << 31,

    AnyOrUnknown = Any | Unknown,
    Nullable = Undefined | Null,
    Literal = StringLiteral | NumberLiteral | BigIntLiteral | BooleanLiteral,
    Unit = Enum | Literal | UniqueESSymbol | Nullable,
    Freshable = Enum | Literal,
    StringOrNumberLiteral = StringLiteral | NumberLiteral,
    StringOrNumberLiteralOrUnique = StringLiteral | NumberLiteral | UniqueESSymbol,
    DefinitelyFalsy = StringLiteral | NumberLiteral | BigIntLiteral | BooleanLiteral | Void | Undefined | Null,
    PossiblyFalsy = DefinitelyFalsy | String | Number | BigInt | Boolean,
    Intrinsic = Any | Unknown | String | Number | BigInt | ESSymbol | Void | Undefined | Null | Never | NonPrimitive,
    StringLike = String | StringLiteral | TemplateLiteral | StringMapping,
    NumberLike = Number | NumberLiteral | Enum,
    BigIntLike = BigInt | BigIntLiteral,
    BooleanLike = Boolean | BooleanLiteral,
    EnumLike = Enum | EnumLiteral,
    ESSymbolLike = ESSymbol | UniqueESSymbol,
    VoidLike = Void | Undefined,
    Primitive = StringLike | NumberLike | BigIntLike | BooleanLike | EnumLike | ESSymbolLike | VoidLike | Null,
    DefinitelyNonNullable = StringLike | NumberLike | BigIntLike | BooleanLike | EnumLike | ESSymbolLike | Object | NonPrimitive,
    DisjointDomains = NonPrimitive | StringLike | NumberLike | BigIntLike | BooleanLike | ESSymbolLike | VoidLike | Null,
    UnionOrIntersection = Union | Intersection,
    StructuredType = Object | Union | Intersection,
    TypeVariable = TypeParameter | IndexedAccess,
    InstantiableNonPrimitive = TypeVariable | Conditional | Substitution,
    InstantiablePrimitive = Index | TemplateLiteral | StringMapping,
    Instantiable = InstantiableNonPrimitive | InstantiablePrimitive,
    StructuredOrInstantiable = StructuredType | Instantiable,
    ObjectFlagsType = Any | Nullable | Never | Object | Union | Intersection,
    Simplifiable = IndexedAccess | Conditional | Index,
    Singleton = Any | Unknown | String | Number | Boolean | BigInt | ESSymbol | Void | Undefined | Null | Never | NonPrimitive,
    // 'TypeFlagsNarrowable' types are types where narrowing actually narrows.
    // This *should* be every type other than null, undefined, void, and never
    Narrowable = Any | Unknown | StructuredOrInstantiable | StringLike | NumberLike | BigIntLike | BooleanLike | ESSymbol | UniqueESSymbol | NonPrimitive,
    // The following flags are aggregated during union and intersection type construction
    IncludesMask = Any | Unknown | Primitive | Never | Object | Union | Intersection | NonPrimitive | TemplateLiteral | StringMapping,
    // The following flags are used for different purposes during union and intersection type construction
    IncludesMissingType = TypeParameter,
    IncludesNonWideningType = Index,
    IncludesWildcard = IndexedAccess,
    IncludesEmptyObject = Conditional,
    IncludesInstantiable = Substitution,
    IncludesConstrainedTypeVariable = Reserved1,
    IncludesError = Reserved2,
    NotPrimitiveUnion = Any | Unknown | Void | Never | Object | Intersection | IncludesInstantiable,
}

// ObjectFlags
//
// Types included in TypeFlags.ObjectFlagsType have an objectFlags property. Some ObjectFlags
// are specific to certain types and reuse the same bit position. Those ObjectFlags require a check
// for a certain TypeFlags value to determine their meaning.

/// <summary>Go 的 <c>type ObjectFlags uint32</c>。</summary>
[Flags]
public enum ObjectFlags : uint
{
    None = 0,
    Class = 1u << 0,       // Class
    Interface = 1u << 1,   // Interface
    Reference = 1u << 2,   // Generic type reference
    Tuple = 1u << 3,       // Synthesized generic tuple type
    Anonymous = 1u << 4,   // Anonymous
    Mapped = 1u << 5,      // Mapped
    Instantiated = 1u << 6, // Instantiated anonymous or mapped type
    ObjectLiteral = 1u << 7, // Originates in an object literal
    EvolvingArray = 1u << 8, // Evolving array type
    ObjectLiteralPatternWithComputedProperties = 1u << 9, // Object literal pattern with computed properties
    ReverseMapped = 1u << 10, // Object contains a property from a reverse-mapped type
    JsxAttributes = 1u << 11, // Jsx attributes type
    JSLiteral = 1u << 12,     // Object type declared in JS - disables errors on read/write of nonexisting members
    FreshLiteral = 1u << 13,  // Fresh object literal
    ArrayLiteral = 1u << 14,  // Originates in an array literal
    PrimitiveUnion = 1u << 15, // Union of only primitive types
    ContainsWideningType = 1u << 16, // Type is or contains undefined or null widening type
    ContainsObjectOrArrayLiteral = 1u << 17, // Type is or contains object literal type
    NonInferrableType = 1u << 18, // Type is or contains anyFunctionType or silentNeverType
    CouldContainTypeVariablesComputed = 1u << 19, // CouldContainTypeVariables flag has been computed
    CouldContainTypeVariables = 1u << 20, // Type could contain a type variable
    MembersResolved = 1u << 21, // Members have been resolved

    ClassOrInterface = Class | Interface,
    RequiresWidening = ContainsWideningType | ContainsObjectOrArrayLiteral,
    PropagatingFlags = ContainsWideningType | ContainsObjectOrArrayLiteral | NonInferrableType,
    InstantiatedMapped = Mapped | Instantiated,
    // Object flags that uniquely identify the kind of ObjectType
    // （Go 里前向引用了下面的 InstantiationExpressionType/SingleSignatureType，C# 枚举不能前向引用 → 就地写位移）
    ObjectTypeKindMask = ClassOrInterface | Reference | Tuple | Anonymous | Mapped | ReverseMapped | EvolvingArray | (1u << 24) /* InstantiationExpressionType */ | (1u << 25) /* SingleSignatureType */,
    // Flags that require TypeFlags.Object
    ContainsSpread = 1u << 22,              // Object literal contains spread operation
    ObjectRestType = 1u << 23,              // Originates in object rest declaration
    InstantiationExpressionType = 1u << 24, // Originates in instantiation expression
    SingleSignatureType = 1u << 25,         // A single signature type extracted from a potentially broader type
    IsClassInstanceClone = 1u << 26,        // Type is a clone of a class instance type
    // Flags that require TypeFlags.Object and ObjectFlags.Reference
    IdenticalBaseTypeCalculated = 1u << 27, // has had `getSingleBaseForNonAugmentingSubtype` invoked on it already
    IdenticalBaseTypeExists = 1u << 28,     // has a defined cachedEquivalentBaseType member
    UnresolvedMembers = 1u << 29,           // Member resolution in process
    FromTypeNode = 1u << 30,                // Originates in resolution of AST type node
    // Flags that require TypeFlags.UnionOrIntersection or TypeFlags.Substitution
    IsGenericTypeComputed = 1u << 22, // IsGenericObjectType flag has been computed
    IsGenericObjectType = 1u << 23,   // Union or intersection contains generic object type
    IsGenericIndexType = 1u << 24,    // Union or intersection contains generic index type
    IsGenericType = IsGenericObjectType | IsGenericIndexType,
    // Flags that require TypeFlags.Union
    ContainsIntersections = 1u << 25,      // Union contains intersections
    IsUnknownLikeUnionComputed = 1u << 26, // IsUnknownLikeUnion flag has been computed
    IsUnknownLikeUnion = 1u << 27,         // Union of null, undefined, and empty object type
    IsUniformEnumComputed = 1u << 28,      // IsUniformEnum flag has been computed
    IsUniformEnum = 1u << 29,              // Union contains uniform literal types
    // Flags that require TypeFlags.Intersection
    IsNeverIntersectionComputed = 1u << 25, // IsNeverLike flag has been computed
    IsNeverIntersection = 1u << 26,         // Intersection reduces to never
    IsConstrainedTypeVariable = 1u << 27,   // T & C, where T's constraint and C are primitives, object, or {}
}

// 包级声明（Go 的 `const externalHelpersModuleNameText`、`var typeFlagNames`、`func FormatTypeFlags`、
// `func (TypeFlags) String`、`func (VarianceFlags) String`）→ 同一个 `Checker` partial 类的静态成员。

public sealed partial class Checker
{
    /// <summary>Go 的 <c>const externalHelpersModuleNameText = "tslib"</c>（types.go:165）。</summary>
    internal const string ExternalHelpersModuleNameText = "tslib";

    /// <summary>Go 的 <c>var typeFlagNames</c>（types.go:520-554）。</summary>
    private static readonly (TypeFlags Flag, string Name)[] _typeFlagNames =
    [
        (TypeFlags.Any, "Any"),
        (TypeFlags.Unknown, "Unknown"),
        (TypeFlags.Undefined, "Undefined"),
        (TypeFlags.Null, "Null"),
        (TypeFlags.Void, "Void"),
        (TypeFlags.String, "String"),
        (TypeFlags.Number, "Number"),
        (TypeFlags.BigInt, "BigInt"),
        (TypeFlags.Boolean, "Boolean"),
        (TypeFlags.ESSymbol, "ESSymbol"),
        (TypeFlags.StringLiteral, "StringLiteral"),
        (TypeFlags.NumberLiteral, "NumberLiteral"),
        (TypeFlags.BigIntLiteral, "BigIntLiteral"),
        (TypeFlags.BooleanLiteral, "BooleanLiteral"),
        (TypeFlags.UniqueESSymbol, "UniqueESSymbol"),
        (TypeFlags.EnumLiteral, "EnumLiteral"),
        (TypeFlags.Enum, "Enum"),
        (TypeFlags.NonPrimitive, "NonPrimitive"),
        (TypeFlags.Never, "Never"),
        (TypeFlags.TypeParameter, "TypeParameter"),
        (TypeFlags.Object, "Object"),
        (TypeFlags.Index, "Index"),
        (TypeFlags.TemplateLiteral, "TemplateLiteral"),
        (TypeFlags.StringMapping, "StringMapping"),
        (TypeFlags.Substitution, "Substitution"),
        (TypeFlags.IndexedAccess, "IndexedAccess"),
        (TypeFlags.Conditional, "Conditional"),
        (TypeFlags.Union, "Union"),
        (TypeFlags.Intersection, "Intersection"),
    ];

    /// <summary>Go 的 <c>func FormatTypeFlags(flags TypeFlags) []string</c>（types.go:556）：返回各标志位名。</summary>
    public static string[] FormatTypeFlags(TypeFlags flags)
    {
        var result = new string[BitOperations.PopCount((uint)flags)];
        var count = 0;
        foreach (var fn in _typeFlagNames)
        {
            if ((flags & fn.Flag) != 0)
            {
                result[count++] = fn.Name;
            }
        }
        if (count == 0)
        {
            return ["None"];
        }
        return count == result.Length ? result : result[..count];
    }
}

/// <summary>
/// Go 的 stringer 扩展方法宿主：<c>func (f TypeFlags) String() string</c>（types.go:570）与
/// <c>func (v VarianceFlags) String() string</c>（types.go:574）。
/// C# 的 enum 不能声明实例方法，而包级函数的宿主 <c>Checker</c> 是非 static 类（扩展方法必须定义在
/// 非泛型 static 类里，CS1106）→ 单独放这个 static 类；调用点仍写 <c>flags.String()</c>（扩展方法在
/// 声明它的命名空间内可见）。checker 里已有的另一个宿主是 Stringer.Generated.cs 的
/// <c>CheckerExtensions</c>（SignatureKind 的 stringer），两者互不冲突。
/// </summary>
public static class CheckerTypeStringer
{
    /// <summary>Go 的 <c>func (f TypeFlags) String() string</c>（types.go:570）：`|` 分隔的标志位名。</summary>
    public static string String(this TypeFlags f)
    {
        return string.Join("|", Checker.FormatTypeFlags(f));
    }

    /// <summary>Go 的 <c>func (v VarianceFlags) String() string</c>（types.go:574）。</summary>
    public static string String(this VarianceFlags v)
    {
        var variance = v & VarianceFlags.VarianceMask;
        var result = variance switch
        {
            VarianceFlags.Invariant => "in out",
            VarianceFlags.Bivariant => "[bivariant]",
            VarianceFlags.Contravariant => "in",
            VarianceFlags.Covariant => "out",
            VarianceFlags.Independent => "[independent]",
            _ => "",
        };
        if ((v & VarianceFlags.Unmeasurable) != 0)
        {
            result += " (unmeasurable)";
        }
        else if ((v & VarianceFlags.Unreliable) != 0)
        {
            result += " (unreliable)";
        }
        return result;
    }
}
