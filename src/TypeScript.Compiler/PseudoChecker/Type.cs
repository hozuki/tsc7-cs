// ported from: internal/pseudochecker/type.go
//
// 与 Go 的差异（逐条）：
// 1) **`pseudoTypeData` 那一层不移植**（对应 docs/ast.md §2 对 `nodeData`/`NodeDefault`/`NodeBase` 的决定）：
//    Go 把 `PseudoType` 内嵌进每个数据结构体，再用 `data.AsPseudoType()` 拿回内嵌的头部；
//    C# 直接用继承（`PseudoType` 是基类，`PseudoTypeDirect : PseudoType` …），每层只覆写/新增自己的字段。
//    `PseudoTypeDefault`/`PseudoTypeBase`/`pseudoTypeData`/`newPseudoType` 因此不存在（静态工厂直接 new 派生类并置 Kind）。
//    `PseudoObjectElement` 的 `pseudoObjectElementData`/`newPseudoObjectElement` 同样折叠掉。
// 2) Go 的 `func (t *PseudoType) AsPseudoTypeDirect() *PseudoTypeDirect { return t.data.(*PseudoTypeDirect) }`
//    是**硬断言**（类型不对会 panic）→ C# 用**硬转换**（类型不对抛 InvalidCastException，与 panic 对应）。
//    这与 Ast 生成的 `AsIdentifier() => this as Identifier`（kind 不匹配返回 null）不同，因为那边 Go 生成的也是 `as` 语义。
// 3) `PseudoObjectElement.Signature()` 是方法，派生类型的 `Signature` 字段与它同名 → C# 里字段加 `new`（与 docs/ast.md §2
//    的"字段遮蔽方法"处理一致）。
// 4) `iota` 枚举 → C# enum 并显式写出与 Go 一致的数值（`PseudoTypeKind` int16、`PseudoObjectElementKind` int8）。
namespace TypeScript.PseudoChecker;

using TypeScript.Ast;

// `PseudoType`s are skeletons of types - partially interpreted expressions and type nodes
// composed to represent how you *should* construct a type out of them. They can be trivially
// mapped into actual types by a real `Checker`, or into a tree of `Node`s directly, without
// needing to make any intermediate types, by a `NodeBuilder`. Unlike checker `Type`s, these are
// never normalized, and multiple pseudo-types may refer to the same underlying `Type`.

public enum PseudoTypeKind : short
{
    Direct = 0,
    Inferred = 1,
    NoResult = 2,
    MaybeConstLocation = 3,
    Union = 4,
    Undefined = 5,
    Null = 6,
    Any = 7,
    String = 8,
    Number = 9,
    BigInt = 10,
    Boolean = 11,
    False = 12,
    True = 13,
    SingleCallSignature = 14,
    Tuple = 15,
    ObjectLiteral = 16,
    StringLiteral = 17,
    NumericLiteral = 18,
    BigIntLiteral = 19,
}

public class PseudoType
{
    public PseudoTypeKind Kind;

    // Go 的 `t.data.(*PseudoTypeXxx)`（见文件头第 2 条）。
    public PseudoTypeDirect AsPseudoTypeDirect() => (PseudoTypeDirect)this;

    public PseudoTypeInferred AsPseudoTypeInferred() => (PseudoTypeInferred)this;

    public PseudoTypeNoResult AsPseudoTypeNoResult() => (PseudoTypeNoResult)this;

    public PseudoTypeMaybeConstLocation AsPseudoTypeMaybeConstLocation() => (PseudoTypeMaybeConstLocation)this;

    public PseudoTypeUnion AsPseudoTypeUnion() => (PseudoTypeUnion)this;

    public PseudoTypeSingleCallSignature AsPseudoTypeSingleCallSignature() => (PseudoTypeSingleCallSignature)this;

    public PseudoTypeTuple AsPseudoTypeTuple() => (PseudoTypeTuple)this;

    public PseudoTypeObjectLiteral AsPseudoTypeObjectLiteral() => (PseudoTypeObjectLiteral)this;

    public PseudoTypeLiteral AsPseudoTypeLiteral() => (PseudoTypeLiteral)this;

    /// <summary>PseudoTypeDirect directly encodes the type referred to by a given TypeNode.</summary>
    public static PseudoType NewPseudoTypeDirect(Node? typeNode) =>
        new PseudoTypeDirect { TypeNode = typeNode, Kind = PseudoTypeKind.Direct };

    /// <summary>
    /// PseudoTypeInferred directly encodes the type referred to by a given Expression.
    /// These represent cases where the expression was too complex for the pseudochecker.
    /// Most of the time, these locations will produce an error under ID.
    /// Specific error nodes (shorthand properties, spread assignments, etc.) are stored on the
    /// ErrorNodes field, collected during pseudochecker construction.
    /// </summary>
    public static PseudoType NewPseudoTypeInferred(Node? expr, bool isSignatureReturn) =>
        new PseudoTypeInferred { Expression = expr, IsSignatureReturn = isSignatureReturn, Kind = PseudoTypeKind.Inferred };

    public static PseudoType NewPseudoTypeInferredWithErrors(Node? expr, bool isSignatureReturn, Node[] errorNodes) =>
        new PseudoTypeInferred { Expression = expr, ErrorNodes = errorNodes, IsSignatureReturn = isSignatureReturn, Kind = PseudoTypeKind.Inferred };

    /// <summary>
    /// PseudoTypeNoResult is analogous to PseudoTypeInferred in that it references a case where the type was too
    /// complex for the pseudochecker. Rather than an expression, however, it is referring to the return type of a
    /// signature or declaration.
    /// </summary>
    public static PseudoType NewPseudoTypeNoResult(Node? decl) =>
        new PseudoTypeNoResult { Declaration = decl, Kind = PseudoTypeKind.NoResult };

    /// <summary>
    /// PseudoTypeMaybeConstLocation encodes the const/regular types of a location so the builder can later select the
    /// appropriate pseudotype based on the location's context (见 Go 原文的长注释：strada 里靠 `isConstContext` 掩盖，
    /// 这里显式建模）。
    /// </summary>
    public static PseudoType NewPseudoTypeMaybeConstLocation(Node? loc, PseudoType? ct, PseudoType? reg) =>
        new PseudoTypeMaybeConstLocation { Node = loc, ConstType = ct, RegularType = reg, Kind = PseudoTypeKind.MaybeConstLocation };

    /// <summary>PseudoTypeUnion is a collection of pseudotypes joined into a union.</summary>
    public static PseudoType NewPseudoTypeUnion(PseudoType[]? types) =>
        new PseudoTypeUnion { Types = types, Kind = PseudoTypeKind.Union };

    /// <summary>PseudoTypeSingleCallSignature represents an object type with a single call signature, like an arrow or function expression.</summary>
    public static PseudoType NewPseudoTypeSingleCallSignature(
        Node? signature,
        PseudoParameter[]? parameters,
        TypeParameterDeclaration[]? typeParameters,
        PseudoType? returnType) =>
        new PseudoTypeSingleCallSignature
        {
            Signature = signature,
            Parameters = parameters,
            TypeParameters = typeParameters,
            ReturnType = returnType,
            Kind = PseudoTypeKind.SingleCallSignature,
        };

    /// <summary>PseudoTypeTuple represents a tuple originating from an `as const` array literal.</summary>
    public static PseudoType NewPseudoTypeTuple(PseudoType[]? elements) =>
        new PseudoTypeTuple { Elements = elements, Kind = PseudoTypeKind.Tuple };

    /// <summary>PseudoTypeObjectLiteral represents an object type originating from an object literal.</summary>
    public static PseudoType NewPseudoTypeObjectLiteral(PseudoObjectElement[]? elements) =>
        new PseudoTypeObjectLiteral { Elements = elements, Kind = PseudoTypeKind.ObjectLiteral };

    /// <summary>PseudoTypeLiteral represents a literal type.</summary>
    public static PseudoType NewPseudoTypeStringLiteral(Node? node) =>
        new PseudoTypeLiteral { Node = node, Kind = PseudoTypeKind.StringLiteral };

    public static PseudoType NewPseudoTypeNumericLiteral(Node? node) =>
        new PseudoTypeLiteral { Node = node, Kind = PseudoTypeKind.NumericLiteral };

    public static PseudoType NewPseudoTypeBigIntLiteral(Node? node) =>
        new PseudoTypeLiteral { Node = node, Kind = PseudoTypeKind.BigIntLiteral };
}

/// <summary>PseudoTypeDirect directly encodes the type referred to by a given TypeNode.</summary>
public sealed class PseudoTypeDirect : PseudoType
{
    public Node? TypeNode;
}

public sealed class PseudoTypeInferred : PseudoType
{
    public Node? Expression;
    public Node[] ErrorNodes = [];
    public bool IsSignatureReturn;
}

public sealed class PseudoTypeNoResult : PseudoType
{
    public Node? Declaration;
}

public sealed class PseudoTypeMaybeConstLocation : PseudoType
{
    public Node? Node;
    public PseudoType? ConstType;
    public PseudoType? RegularType;
}

/// <summary>PseudoTypeUnion is a collection of pseudotypes joined into a union.</summary>
public sealed class PseudoTypeUnion : PseudoType
{
    public PseudoType[]? Types;
}

public sealed class PseudoParameter
{
    public bool Rest;
    public Node? Name;
    public bool Optional;
    public PseudoType? Type;

    /// <summary>Go 的包级工厂 <c>NewPseudoParameter</c>（放在产物类型上，见 Type.cs 文件头第 1 条）。</summary>
    public static PseudoParameter NewPseudoParameter(bool isRest, Node? name, bool isOptional, PseudoType? t) =>
        new() { Rest = isRest, Name = name, Optional = isOptional, Type = t };
}

/// <summary>PseudoTypeSingleCallSignature represents an object type with a single call signature, like an arrow or function expression.</summary>
public sealed class PseudoTypeSingleCallSignature : PseudoType
{
    public Node? Signature;
    public PseudoParameter[]? Parameters;
    public TypeParameterDeclaration[]? TypeParameters;
    public PseudoType? ReturnType;
}

/// <summary>PseudoTypeTuple represents a tuple originating from an `as const` array literal.</summary>
public sealed class PseudoTypeTuple : PseudoType
{
    public PseudoType[]? Elements;
}

public enum PseudoObjectElementKind : sbyte
{
    Method = 0,
    PropertyAssignment = 1,
    SetAccessor = 2,
    GetAccessor = 3,
}

public class PseudoObjectElement
{
    public Node? Name;
    public bool Optional;
    public PseudoObjectElementKind Kind;

    public PseudoObjectElement AsPseudoObjectElement() => this;

    public Node? Signature()
    {
        switch (Kind)
        {
            case PseudoObjectElementKind.Method:
                return AsPseudoObjectMethod().Signature;
            case PseudoObjectElementKind.SetAccessor:
                return AsPseudoSetAccessor().Signature;
            case PseudoObjectElementKind.GetAccessor:
                return AsPseudoGetAccessor().Signature;
            default:
                return null;
        }
    }

    public PseudoObjectMethod AsPseudoObjectMethod() => (PseudoObjectMethod)this;

    public PseudoPropertyAssignment AsPseudoPropertyAssignment() => (PseudoPropertyAssignment)this;

    public PseudoSetAccessor AsPseudoSetAccessor() => (PseudoSetAccessor)this;

    public PseudoGetAccessor AsPseudoGetAccessor() => (PseudoGetAccessor)this;

    public static PseudoObjectElement NewPseudoObjectMethod(
        Node? signature,
        Node? name,
        bool optional,
        TypeParameterDeclaration[]? typeParameters,
        PseudoParameter[]? parameters,
        PseudoType? returnType) =>
        new PseudoObjectMethod
        {
            Signature = signature,
            TypeParameters = typeParameters,
            Parameters = parameters,
            ReturnType = returnType,
            Kind = PseudoObjectElementKind.Method,
            Name = name,
            Optional = optional,
        };

    public static PseudoObjectElement NewPseudoPropertyAssignment(bool @readonly, Node? name, bool optional, PseudoType? t) =>
        new PseudoPropertyAssignment
        {
            Readonly = @readonly,
            Type = t,
            Kind = PseudoObjectElementKind.PropertyAssignment,
            Name = name,
            Optional = optional,
        };

    public static PseudoObjectElement NewPseudoSetAccessor(Node? signature, Node? name, bool optional, PseudoParameter? p) =>
        new PseudoSetAccessor
        {
            Signature = signature,
            Parameter = p,
            Kind = PseudoObjectElementKind.SetAccessor,
            Name = name,
            Optional = optional,
        };

    public static PseudoObjectElement NewPseudoGetAccessor(Node? signature, Node? name, bool optional, PseudoType? t) =>
        new PseudoGetAccessor
        {
            Signature = signature,
            Type = t,
            Kind = PseudoObjectElementKind.GetAccessor,
            Name = name,
            Optional = optional,
        };
}

public sealed class PseudoObjectMethod : PseudoObjectElement
{
    public new Node? Signature;
    public TypeParameterDeclaration[]? TypeParameters;
    public PseudoParameter[]? Parameters;
    public PseudoType? ReturnType;
}

public sealed class PseudoPropertyAssignment : PseudoObjectElement
{
    public bool Readonly;
    public PseudoType? Type;
}

public sealed class PseudoSetAccessor : PseudoObjectElement
{
    public new Node? Signature;
    public PseudoParameter? Parameter;
}

public sealed class PseudoGetAccessor : PseudoObjectElement
{
    public new Node? Signature;
    public PseudoType? Type;
}

/// <summary>PseudoTypeObjectLiteral represents an object type originating from an object literal.</summary>
public sealed class PseudoTypeObjectLiteral : PseudoType
{
    public PseudoObjectElement[]? Elements;
}

/// <summary>PseudoTypeLiteral represents a literal type.</summary>
public sealed class PseudoTypeLiteral : PseudoType
{
    public Node? Node;
}
