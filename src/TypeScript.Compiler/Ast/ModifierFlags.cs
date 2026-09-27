// ported from: internal/ast/modifierflags.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.3）：
// - `type ModifierFlags uint32` + 包级常量 → `enum ModifierFlags : uint`：成员名 = Go 常量名去掉
//   `ModifierFlags` 前缀，数值逐条与 Go 一致。
// - 文件末尾的 5 个包级函数（ModifierToFlag/ModifiersToFlags/HasSyntacticModifier/HasAccessorModifier/
//   HasStaticModifier）在 Go 里位于 `internal/ast/utilities.go:990-1046`（不属于 modifierflags.go）；
//   为了让本轮就可用，这里按 Go 原文一并移植，放在同一个文件的 `Ast` 静态类里。
//   `ModifiersToFlags` 的 Go 签名是 `modifiers []*Node`，C# 侧是 `ReadOnlySpan<Node?>`（docs/architecture.md §3.8）。
// - Go 的 `^X`（按位取反，unary complement）在 C# 里写作 `~X`。
// - `Node.ModifierFlags` 在 C# 里是**属性**（不是方法）：本枚举的类型名与它同名，C# 里方法会遮蔽类型名
//   （`ModifierFlags.None` 会被解析成方法组），属性则不遮蔽（Color Color 规则），所以调用点写
//   `node.ModifierFlags`、`ModifierFlags.None` 两处都能成立。
namespace TypeScript.Ast;

[Flags]
public enum ModifierFlags : uint
{
    None = 0,
    // Syntactic/JSDoc modifiers
    Public = 1u << 0,     // Property/Method
    Private = 1u << 1,    // Property/Method
    Protected = 1u << 2,  // Property/Method
    Readonly = 1u << 3,   // Property/Method
    Override = 1u << 4,   // Override method
    // Syntactic-only modifiers
    Export = 1u << 5,     // Declarations
    Abstract = 1u << 6,   // Class/Method/ConstructSignature
    Ambient = 1u << 7,    // Declarations (declare keyword)
    Static = 1u << 8,     // Property/Method
    Accessor = 1u << 9,   // Property
    Async = 1u << 10,     // Property/Method/Function
    Default = 1u << 11,   // Function/Class (export default declaration)
    Const = 1u << 12,     // Const enum
    In = 1u << 13,        // Contravariance modifier
    Out = 1u << 14,       // Covariance modifier
    Decorator = 1u << 15, // Contains a decorator
    // JSDoc-only modifiers
    Deprecated = 1u << 16, // Deprecated tag
    // Cache-only JSDoc-modifiers. Should match order of Syntactic/JSDoc modifiers, above.
    JSDocPublic = 1u << 23,               // if this value changes, `selectEffectiveModifierFlags` must change accordingly
    JSDocPrivate = 1u << 24,
    JSDocProtected = 1u << 25,
    JSDocReadonly = 1u << 26,
    JSDocOverride = 1u << 27,
    HasComputedJSDocModifiers = 1u << 28, // Indicates the computed modifier flags include modifiers from JSDoc.
    HasComputedFlags = 1u << 29,          // Modifier flags have been computed

    SyntacticOrJSDocModifiers = Public | Private | Protected | Readonly | Override,
    SyntacticOnlyModifiers = Export | Ambient | Abstract | Static | Accessor | Async | Default | Const | In | Out | Decorator,
    SyntacticModifiers = SyntacticOrJSDocModifiers | SyntacticOnlyModifiers,
    JSDocCacheOnlyModifiers = JSDocPublic | JSDocPrivate | JSDocProtected | JSDocReadonly | JSDocOverride,
    JSDocOnlyModifiers = Deprecated,
    NonCacheOnlyModifiers = SyntacticOrJSDocModifiers | SyntacticOnlyModifiers | JSDocOnlyModifiers,

    AccessibilityModifier = Public | Private | Protected,
    // Accessibility modifiers and 'readonly' can be attached to a parameter in a constructor to make it a property.
    ParameterPropertyModifier = AccessibilityModifier | Readonly | Override,
    NonPublicAccessibilityModifier = Private | Protected,

    TypeScriptModifier = Ambient | Public | Private | Protected | Readonly | Abstract | Const | Override | In | Out,
    ExportDefault = Export | Default,
    All = Export | Ambient | Public | Private | Protected | Static | Readonly | Abstract | Accessor | Async | Default | Const | Deprecated | Override | In | Out | Decorator,
    Modifier = All & ~Decorator,
    JavaScript = Export | Static | Accessor | Async | Default,
}

// ported from: internal/ast/utilities.go (ModifierToFlag)
public static partial class Ast
{
    public static ModifierFlags ModifierToFlag(Kind token)
    {
        switch (token)
        {
            case Kind.StaticKeyword:
                return ModifierFlags.Static;
            case Kind.PublicKeyword:
                return ModifierFlags.Public;
            case Kind.ProtectedKeyword:
                return ModifierFlags.Protected;
            case Kind.PrivateKeyword:
                return ModifierFlags.Private;
            case Kind.AbstractKeyword:
                return ModifierFlags.Abstract;
            case Kind.AccessorKeyword:
                return ModifierFlags.Accessor;
            case Kind.ExportKeyword:
                return ModifierFlags.Export;
            case Kind.DeclareKeyword:
                return ModifierFlags.Ambient;
            case Kind.ConstKeyword:
                return ModifierFlags.Const;
            case Kind.DefaultKeyword:
                return ModifierFlags.Default;
            case Kind.AsyncKeyword:
                return ModifierFlags.Async;
            case Kind.ReadonlyKeyword:
                return ModifierFlags.Readonly;
            case Kind.OverrideKeyword:
                return ModifierFlags.Override;
            case Kind.InKeyword:
                return ModifierFlags.In;
            case Kind.OutKeyword:
                return ModifierFlags.Out;
            case Kind.Decorator:
                return ModifierFlags.Decorator;
        }

        return ModifierFlags.None;
    }

    // ported from: internal/ast/utilities.go (ModifiersToFlags)
    //
    // Go 的 `modifiers []*Node`（nil slice = 不迭代）→ C# 的 `ReadOnlySpan<Node?>`（null 数组经隐式转换
    // 得到空 span，同样不迭代，所以不需要 nil 分支；见 docs/architecture.md §3.8 的 span 优先要求）。
    public static ModifierFlags ModifiersToFlags(ReadOnlySpan<Node> modifiers)
    {
        var flags = ModifierFlags.None;
        foreach (var modifier in modifiers)
        {
            // Go: modifier.Kind（元素为 nil 时会 panic，此处同样不检查）。
            flags |= ModifierToFlag(modifier!.Kind);
        }

        return flags;
    }

    // ported from: internal/ast/utilities.go (HasSyntacticModifier)
    public static bool HasSyntacticModifier(Node node, ModifierFlags flags)
    {
        // Node.ModifierFlags 是**属性**（与类型同名时 C# 不允许方法遮蔽类型名，属性不遮蔽，见 docs/ast.md）。
        return (node.ModifierFlags & flags) != 0;
    }

    // ported from: internal/ast/utilities.go (HasAccessorModifier)
    public static bool HasAccessorModifier(Node node)
    {
        return HasSyntacticModifier(node, ModifierFlags.Accessor);
    }

    // ported from: internal/ast/utilities.go (HasStaticModifier)
    public static bool HasStaticModifier(Node node)
    {
        return HasSyntacticModifier(node, ModifierFlags.Static);
    }
}
