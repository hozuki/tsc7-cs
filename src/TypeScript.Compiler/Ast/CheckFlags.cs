// ported from: internal/ast/checkflags.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.3）：
// - `type CheckFlags uint32` + 包级常量 → `enum CheckFlags : uint`：成员名 = Go 常量名去掉 `CheckFlags`
//   前缀，数值逐条与 Go 一致。
// - Go 里最后三个常量（CheckFlagsSynthetic/CheckFlagsNonUniformAndLiteral/CheckFlagsPartial）没有写类型，
//   是**无类型常量**（可赋给任何整数类型）；C# 里直接作为枚举成员。
namespace TypeScript.Ast;

[Flags]
public enum CheckFlags : uint
{
    None = 0,
    Instantiated = 1u << 0,              // Instantiated symbol
    SyntheticProperty = 1u << 1,         // Property in union or intersection type
    SyntheticMethod = 1u << 2,           // Method in union or intersection type
    Readonly = 1u << 3,                  // Readonly transient symbol
    ReadPartial = 1u << 4,               // Synthetic property present in some but not all constituents
    WritePartial = 1u << 5,              // Synthetic property present in some but only satisfied by an index signature in others
    HasNonUniformType = 1u << 6,         // Synthetic property with non-uniform type in constituents
    HasLiteralType = 1u << 7,            // Synthetic property with at least one literal type in constituents
    ContainsPublic = 1u << 8,            // Synthetic property with public constituent(s)
    ContainsProtected = 1u << 9,         // Synthetic property with protected constituent(s)
    ContainsPrivate = 1u << 10,          // Synthetic property with private constituent(s)
    ContainsWritePublic = 1u << 11,      // Synthetic property with public set accessors(s)
    ContainsWriteProtected = 1u << 12,   // Synthetic property with protected set accessors(s)
    ContainsWritePrivate = 1u << 13,     // Synthetic property with private set accessors(s)
    ContainsStatic = 1u << 14,           // Synthetic property with static constituent(s)
    Late = 1u << 15,                     // Late-bound symbol for a computed property with a dynamic name
    ReverseMapped = 1u << 16,            // Property of reverse-inferred homomorphic mapped type
    OptionalParameter = 1u << 17,        // Optional parameter
    RestParameter = 1u << 18,            // Rest parameter
    DeferredType = 1u << 19,             // Calculation of the type of this symbol is deferred due to processing costs, should be fetched with `getTypeOfSymbolWithDeferredType`
    HasNeverType = 1u << 20,             // Synthetic property with at least one never type in constituents
    Mapped = 1u << 21,                   // Property of mapped type
    StripOptional = 1u << 22,            // Strip optionality in mapped property
    Unresolved = 1u << 23,               // Unresolved type alias symbol
    IsDiscriminantComputed = 1u << 24,   // IsDiscriminant flags has been computed
    IsDiscriminant = 1u << 25,           // Discriminant property
    IndexSymbol = 1u << 26,              // Synthetic property created from index signature
    Synthetic = SyntheticProperty | SyntheticMethod,
    NonUniformAndLiteral = HasNonUniformType | HasLiteralType,
    Partial = ReadPartial | WritePartial,
}
