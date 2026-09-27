// ported from: internal/ast/symbolflags.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.3）：
// - `type SymbolFlags uint32` + 包级常量 → `enum SymbolFlags : uint`：成员名 = Go 常量名去掉 `SymbolFlags`
//   前缀，数值逐条与 Go 一致。
// - Go 的 `1<<30 - 1`（Go 的 `<<` 优先级高于 `-`）在 C# 里必须加括号：`(1u << 30) - 1`——C# 的 `<<`
//   优先级**低于** `+`/`-`，不加括号会算成 `1u << 29`。
// - Go 的 unary `^X`（按位取反）在 C# 里是 `~X`。
namespace TypeScript.Ast;

[Flags]
public enum SymbolFlags : uint
{
    None = 0,
    FunctionScopedVariable = 1u << 0,  // Variable (var) or parameter
    BlockScopedVariable = 1u << 1,     // A block-scoped variable (let or const)
    Property = 1u << 2,                // Property or enum member
    EnumMember = 1u << 3,              // Enum member
    Function = 1u << 4,                // Function
    Class = 1u << 5,                   // Class
    Interface = 1u << 6,               // Interface
    ConstEnum = 1u << 7,               // Const enum
    RegularEnum = 1u << 8,             // Enum
    ValueModule = 1u << 9,             // Instantiated module
    NamespaceModule = 1u << 10,        // Uninstantiated module
    TypeLiteral = 1u << 11,            // Type Literal or mapped type
    ObjectLiteral = 1u << 12,          // Object Literal
    Method = 1u << 13,                 // Method
    Constructor = 1u << 14,            // Constructor
    GetAccessor = 1u << 15,            // Get accessor
    SetAccessor = 1u << 16,            // Set accessor
    Signature = 1u << 17,              // Call, construct, or index signature
    TypeParameter = 1u << 18,          // Type parameter
    TypeAlias = 1u << 19,              // Type alias
    ExportValue = 1u << 20,            // Exported value marker (see comment in declareModuleMember in binder)
    Alias = 1u << 21,                  // An alias for another symbol (see comment in isAliasSymbolDeclaration in checker)
    Prototype = 1u << 22,              // Prototype property (no source representation)
    ExportStar = 1u << 23,             // Export * declaration
    Optional = 1u << 24,               // Optional property
    Transient = 1u << 25,              // Transient symbol (created during type check)
    Assignment = 1u << 26,             // Assignment to property on function acting as declaration (eg `func.prop = 1`)
    ModuleExports = 1u << 27,          // Symbol for CommonJS `module` of `module.exports`
    ConstEnumOnlyModule = 1u << 28,    // Module contains only const enums or other modules with only const enums
    ReplaceableByMethod = 1u << 29,
    GlobalLookup = 1u << 30,           // Flag to signal this is a global lookup
    All = (1u << 30) - 1,              // All flags except SymbolFlagsGlobalLookup

    Enum = RegularEnum | ConstEnum,
    Variable = FunctionScopedVariable | BlockScopedVariable,
    Value = Variable | Property | EnumMember | ObjectLiteral | Function | Class | Enum | ValueModule | Method | GetAccessor | SetAccessor,
    Type = Class | Interface | Enum | EnumMember | TypeLiteral | TypeParameter | TypeAlias,
    Namespace = ValueModule | NamespaceModule | Enum,
    Module = ValueModule | NamespaceModule,
    Accessor = GetAccessor | SetAccessor,

    // Variables can be redeclared, but can not redeclare a block-scoped declaration with the
    // same name, or any other value that is not a variable, e.g. ValueModule or Class
    FunctionScopedVariableExcludes = Value & ~FunctionScopedVariable,

    // Block-scoped declarations are not allowed to be re-declared
    // they can not merge with anything in the value space
    BlockScopedVariableExcludes = Value,

    ParameterExcludes = Value,
    PropertyExcludes = Value & ~(Property | Accessor),
    EnumMemberExcludes = Value | Type,
    FunctionExcludes = Value & ~(Function | ValueModule | Class),
    ClassExcludes = (Value | Type) & ~(ValueModule | Interface | Function), // class-interface mergability done in checker.ts
    InterfaceExcludes = Type & ~(Interface | Class),
    RegularEnumExcludes = (Value | Type) & ~(RegularEnum | ValueModule), // regular enums merge only with regular enums and modules
    ConstEnumExcludes = (Value | Type) & ~ConstEnum,                    // const enums merge only with const enums
    ValueModuleExcludes = Value & ~(Function | Class | RegularEnum | ValueModule),
    NamespaceModuleExcludes = None,
    MethodExcludes = Value & ~Method,
    GetAccessorExcludes = Value & ~(SetAccessor | Property),
    SetAccessorExcludes = Value & ~(GetAccessor | Property),
    AccessorExcludes = Value & ~Property,
    TypeParameterExcludes = Type & ~TypeParameter,
    TypeAliasExcludes = Type,
    AliasExcludes = Alias,
    ModuleMember = Variable | Function | Class | Interface | Enum | Module | TypeAlias | Alias,
    ExportHasLocal = Function | Class | Enum | ValueModule,
    BlockScoped = BlockScopedVariable | Class | Enum,
    PropertyOrAccessor = Property | Accessor,
    ClassMember = Method | Accessor | Property,
    ExportSupportsDefaultModifier = Class | Function | Interface,
    ExportDoesNotSupportDefaultModifier = ~ExportSupportsDefaultModifier,
    LateBindingContainer = Class | Interface | TypeLiteral | ObjectLiteral | Function,
}
