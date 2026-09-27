// ported from: internal/ls/lsutil/symbol_display.go
//
// **本文件补齐 `internal/ls/lsutil` 的缺口**（此前只移植了 Asi/Children/CompletedNode/FormatCodeOptions）。
// 与 Go 的偏离（C# 语言特性；依据 docs/architecture.md §2/§3）：
// - `type ScriptElementKind int`（iota）→ C# `enum ScriptElementKind : int`（成员名去 `ScriptElementKind`
//   前缀，数值照抄；§3.3）。Go 的零值 0 = `ScriptElementKindUnknown`，C# `default` 同样是 Unknown。
// - `type ScriptElementKindModifier uint32` → C# `[Flags] enum ScriptElementKindModifier : uint`。
//   **数值必须照抄**：Go 的 const 块里 `None` 显式为 0、`Public` 是 `1 << iota`（iota=1）⇒ Public 是 **2**
//   而不是 1（1 被跳过了）。这里也照写 `1u << n`，不要"顺手"改成 1。
// - Go 的 `func (m ScriptElementKindModifier) Strings() collections.Set[string]` → C# 的扩展方法
//   `LsUtil.Strings(this ScriptElementKindModifier)`（§3.3 的 stringer 手法：C# 的 enum 不能有方法）。
// - Go 的 `var scriptElementKindModifierNames = []struct{flag; name}` → C# 的元组数组（未导出 → `_camelCase`）。
// - Go 的 `var FileExtensionKindModifiers = ...`（导出变量）→ C# 静态类的 `public static readonly` 字段。
// - Go 的 `rootSymbol.Flags&(ast.SymbolFlagsPropertyOrAccessor|ast.SymbolFlagsVariable) != 0` → C# 的同一表达式
//   （`SymbolFlags` 是 `[Flags] enum`，`|`/`&` 语义一致）。
// - `checker.Checker` → 别名 `CheckerType`（命名空间 `TypeScript.Checker` 与外层命名空间 `TypeScript` 的成员同名，
//   裸写 `Checker` 会被解析成命名空间 ⇒ CS0118；而"别名与命名空间同名"在这个位置**同样无效**，所以别名必须取异名）。
//   参数可为 null（Go 的 `*checker.Checker` 可以是 nil，extract.go 传的就是 `checkerLease.TryChecker()`）。
// - `core.Some(symbol.Declarations, ast.IsLet)` → 同一个 `Some`（`Node[]?` 入参）。
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Core;
using CheckerType = TypeScript.Checker.Checker;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

namespace TypeScript.LsUtil;

/// <summary>Go 的 <c>type ScriptElementKind int</c>（每个成员上方保留 Go 的注释）。</summary>
public enum ScriptElementKind : int
{
    Unknown = 0,
    Warning = 1,
    // predefined type (void) or keyword (class)
    Keyword = 2,
    // top level script node
    ScriptElement = 3,
    // module foo {}
    ModuleElement = 4,
    // class X {}
    ClassElement = 5,
    // var x = class X {}
    LocalClassElement = 6,
    // interface Y {}
    InterfaceElement = 7,
    // type T = ...
    TypeElement = 8,
    // enum E {}
    EnumElement = 9,
    EnumMemberElement = 10,
    // Inside module and script only.
    // const v = ...
    VariableElement = 11,
    // Inside function.
    LocalVariableElement = 12,
    // using foo = ...
    VariableUsingElement = 13,
    // await using foo = ...
    VariableAwaitUsingElement = 14,
    // Inside module and script only.
    // function f() {}
    FunctionElement = 15,
    // Inside function.
    LocalFunctionElement = 16,
    // class X { [public|private]* foo() {} }
    MemberFunctionElement = 17,
    // class X { [public|private]* [get|set] foo:number; }
    MemberGetAccessorElement = 18,
    MemberSetAccessorElement = 19,
    // class X { [public|private]* foo:number; }
    // interface Y { foo:number; }
    MemberVariableElement = 20,
    // class X { [public|private]* accessor foo: number; }
    MemberAccessorVariableElement = 21,
    // class X { constructor() { } }
    // class X { static { } }
    ConstructorImplementationElement = 22,
    // interface Y { ():number; }
    CallSignatureElement = 23,
    // interface Y { []:number; }
    IndexSignatureElement = 24,
    // interface Y { new():Y; }
    ConstructSignatureElement = 25,
    // function foo(*Y*: string)
    ParameterElement = 26,
    TypeParameterElement = 27,
    PrimitiveType = 28,
    Label = 29,
    Alias = 30,
    ConstElement = 31,
    LetElement = 32,
    Directory = 33,
    ExternalModuleName = 34,
    // String literal
    String = 35,
    // Jsdoc @link: in `{@link C link text}`, the before and after text "{@link " and "}"
    Link = 36,
    // Jsdoc @link: in `{@link C link text}`, the entity name "C"
    LinkName = 37,
    // Jsdoc @link: in `{@link C link text}`, the link text "link text"
    LinkText = 38,
}

/// <summary>Go 的 <c>type ScriptElementKindModifier uint32</c>（数值见文件头：Public 是 2）。</summary>
[Flags]
public enum ScriptElementKindModifier : uint
{
    None = 0,
    Public = 1u << 1,
    Private = 1u << 2,
    Protected = 1u << 3,
    Exported = 1u << 4,
    Ambient = 1u << 5,
    Static = 1u << 6,
    Abstract = 1u << 7,
    Optional = 1u << 8,
    Deprecated = 1u << 9,
    Dts = 1u << 10,
    Ts = 1u << 11,
    Tsx = 1u << 12,
    Js = 1u << 13,
    Jsx = 1u << 14,
    Json = 1u << 15,
    Dmts = 1u << 16,
    Mts = 1u << 17,
    Mjs = 1u << 18,
    Dcts = 1u << 19,
    Cts = 1u << 20,
    Cjs = 1u << 21,
}

public static partial class LsUtil
{
    /// <summary>Go 的未导出 <c>scriptElementKindModifierNames</c>（顺序即输出顺序）。</summary>
    private static readonly (ScriptElementKindModifier Flag, string Name)[] _scriptElementKindModifierNames =
    [
        (ScriptElementKindModifier.Public, "public"),
        (ScriptElementKindModifier.Private, "private"),
        (ScriptElementKindModifier.Protected, "protected"),
        (ScriptElementKindModifier.Exported, "export"),
        (ScriptElementKindModifier.Ambient, "declare"),
        (ScriptElementKindModifier.Static, "static"),
        (ScriptElementKindModifier.Abstract, "abstract"),
        (ScriptElementKindModifier.Optional, "optional"),
        (ScriptElementKindModifier.Deprecated, "deprecated"),
        (ScriptElementKindModifier.Dts, ".d.ts"),
        (ScriptElementKindModifier.Ts, ".ts"),
        (ScriptElementKindModifier.Tsx, ".tsx"),
        (ScriptElementKindModifier.Js, ".js"),
        (ScriptElementKindModifier.Jsx, ".jsx"),
        (ScriptElementKindModifier.Json, ".json"),
        (ScriptElementKindModifier.Dmts, ".d.mts"),
        (ScriptElementKindModifier.Mts, ".mts"),
        (ScriptElementKindModifier.Mjs, ".mjs"),
        (ScriptElementKindModifier.Dcts, ".d.cts"),
        (ScriptElementKindModifier.Cts, ".cts"),
        (ScriptElementKindModifier.Cjs, ".cjs"),
    ];

    /// <summary>Go 的 <c>func (m ScriptElementKindModifier) Strings() collections.Set[string]</c>。</summary>
    public static Set<string> Strings(this ScriptElementKindModifier m)
    {
        var result = new Set<string>();
        foreach (var entry in _scriptElementKindModifierNames)
        {
            if ((m & entry.Flag) != 0)
            {
                result.Add(entry.Name);
            }
        }

        return result;
    }

    /// <summary>Go 的 <c>var FileExtensionKindModifiers ScriptElementKindModifier</c>。</summary>
    public static readonly ScriptElementKindModifier FileExtensionKindModifiers =
        ScriptElementKindModifier.Dts |
        ScriptElementKindModifier.Ts |
        ScriptElementKindModifier.Tsx |
        ScriptElementKindModifier.Js |
        ScriptElementKindModifier.Jsx |
        ScriptElementKindModifier.Json |
        ScriptElementKindModifier.Dmts |
        ScriptElementKindModifier.Mts |
        ScriptElementKindModifier.Mjs |
        ScriptElementKindModifier.Dcts |
        ScriptElementKindModifier.Cts |
        ScriptElementKindModifier.Cjs;

    /// <summary>Go 的 <c>func GetSymbolKind(typeChecker *checker.Checker, symbol *ast.Symbol, location *ast.Node) ScriptElementKind</c>。</summary>
    public static ScriptElementKind GetSymbolKind(CheckerType? typeChecker, Symbol symbol, Node location)
    {
        var result = GetSymbolKindOfConstructorPropertyMethodAccessorFunctionOrVar(typeChecker, symbol, location);
        if (result != ScriptElementKind.Unknown)
        {
            return result;
        }

        var flags = symbol.CombinedLocalAndExportSymbolFlags();
        if ((flags & SymbolFlags.Class) != 0)
        {
            var decl = GetDeclarationOfKind(symbol, Kind.ClassExpression);
            if (decl is not null)
            {
                return ScriptElementKind.LocalClassElement;
            }

            return ScriptElementKind.ClassElement;
        }

        if ((flags & SymbolFlags.Enum) != 0)
        {
            return ScriptElementKind.EnumElement;
        }

        if ((flags & SymbolFlags.TypeAlias) != 0)
        {
            return ScriptElementKind.TypeElement;
        }

        if ((flags & SymbolFlags.Interface) != 0)
        {
            return ScriptElementKind.InterfaceElement;
        }

        if ((flags & SymbolFlags.TypeParameter) != 0)
        {
            return ScriptElementKind.TypeParameterElement;
        }

        if ((flags & SymbolFlags.EnumMember) != 0)
        {
            return ScriptElementKind.EnumMemberElement;
        }

        if ((flags & SymbolFlags.Alias) != 0)
        {
            return ScriptElementKind.Alias;
        }

        if ((flags & SymbolFlags.Module) != 0)
        {
            return ScriptElementKind.ModuleElement;
        }

        return ScriptElementKind.Unknown;
    }

    /// <summary>Go 的未导出 <c>getSymbolKindOfConstructorPropertyMethodAccessorFunctionOrVar</c>。</summary>
    private static ScriptElementKind GetSymbolKindOfConstructorPropertyMethodAccessorFunctionOrVar(CheckerType? typeChecker, Symbol symbol, Node location)
    {
        Symbol[] roots;
        if (typeChecker is not null)
        {
            roots = typeChecker.GetRootSymbols(symbol);
        }
        else
        {
            roots = [symbol];
        }

        // If this is a method from a mapped type, leave as a method so long as it still has a call signature, as opposed to e.g.
        // `{ [K in keyof I]: number }`.
        if (roots.Length == 1 &&
            (roots[0].Flags & SymbolFlags.Method) != 0 &&
            (typeChecker is null || typeChecker.GetCallSignatures(typeChecker.GetNonNullableType(typeChecker.GetTypeOfSymbolAtLocation(symbol, location))).Length > 0))
        {
            return ScriptElementKind.MemberFunctionElement;
        }

        if (typeChecker is not null)
        {
            if (typeChecker.IsUndefinedSymbol(symbol))
            {
                return ScriptElementKind.VariableElement;
            }

            if (typeChecker.IsArgumentsSymbol(symbol))
            {
                return ScriptElementKind.LocalVariableElement;
            }

            if (location.Kind == Kind.ThisKeyword && IsExpression(location) ||
                IsThisInTypeQuery(location))
            {
                return ScriptElementKind.ParameterElement;
            }
        }

        var flags = symbol.CombinedLocalAndExportSymbolFlags();
        if ((flags & SymbolFlags.Variable) != 0)
        {
            if (IsFirstDeclarationOfSymbolParameter(symbol))
            {
                return ScriptElementKind.ParameterElement;
            }
            else if (symbol.ValueDeclaration is not null && IsVarConst(symbol.ValueDeclaration))
            {
                return ScriptElementKind.ConstElement;
            }
            else if (symbol.ValueDeclaration is not null && IsVarUsing(symbol.ValueDeclaration))
            {
                return ScriptElementKind.VariableUsingElement;
            }
            else if (symbol.ValueDeclaration is not null && IsVarAwaitUsing(symbol.ValueDeclaration))
            {
                return ScriptElementKind.VariableAwaitUsingElement;
            }
            else if (Some(symbol.Declarations, IsLet))
            {
                return ScriptElementKind.LetElement;
            }

            if (IsLocalVariableOrFunction(symbol))
            {
                return ScriptElementKind.LocalVariableElement;
            }

            return ScriptElementKind.VariableElement;
        }

        if ((flags & SymbolFlags.Function) != 0)
        {
            if (IsLocalVariableOrFunction(symbol))
            {
                return ScriptElementKind.LocalFunctionElement;
            }

            return ScriptElementKind.FunctionElement;
        }

        // FIXME: getter and setter use the same symbol. And it is rare to use only setter without getter, so in most cases the symbol always has getter flag.
        // So, even when the location is just on the declaration of setter, this function returns getter.
        if ((flags & SymbolFlags.GetAccessor) != 0)
        {
            return ScriptElementKind.MemberGetAccessorElement;
        }

        if ((flags & SymbolFlags.SetAccessor) != 0)
        {
            return ScriptElementKind.MemberSetAccessorElement;
        }

        if ((flags & SymbolFlags.Method) != 0)
        {
            return ScriptElementKind.MemberFunctionElement;
        }

        if ((flags & SymbolFlags.Constructor) != 0)
        {
            return ScriptElementKind.ConstructorImplementationElement;
        }

        if ((flags & SymbolFlags.Signature) != 0)
        {
            return ScriptElementKind.IndexSignatureElement;
        }

        if ((flags & SymbolFlags.Property) != 0)
        {
            if (typeChecker is not null && (flags & SymbolFlags.Transient) != 0 && (symbol.CheckFlags & CheckFlags.Synthetic) != 0)
            {
                // If union property is result of union of non method (property/accessors/variables), it is labeled as property
                var unionPropertyKind = ScriptElementKind.Unknown;
                foreach (var rootSymbol in roots)
                {
                    if ((rootSymbol.Flags & (SymbolFlags.PropertyOrAccessor | SymbolFlags.Variable)) != 0)
                    {
                        unionPropertyKind = ScriptElementKind.MemberVariableElement;
                        break;
                    }
                }

                if (unionPropertyKind == ScriptElementKind.Unknown)
                {
                    // If this was union of all methods,
                    // make sure it has call signatures before we can label it as method.
                    var typeOfUnionProperty = typeChecker.GetTypeOfSymbolAtLocation(symbol, location);
                    if (typeChecker.GetCallSignatures(typeOfUnionProperty).Length > 0)
                    {
                        return ScriptElementKind.MemberFunctionElement;
                    }

                    return ScriptElementKind.MemberVariableElement;
                }

                return unionPropertyKind;
            }

            return ScriptElementKind.MemberVariableElement;
        }

        return ScriptElementKind.Unknown;
    }

    /// <summary>Go 的未导出 <c>isFirstDeclarationOfSymbolParameter</c>。</summary>
    private static bool IsFirstDeclarationOfSymbolParameter(Symbol symbol)
    {
        Node? declaration = null;
        if (symbol.Declarations is { Length: > 0 } declarations)
        {
            declaration = declarations[0];
        }

        var result = FindAncestorOrQuit(declaration, n =>
        {
            if (IsParameterDeclaration(n))
            {
                return FindAncestorResult.True;
            }

            if (IsBindingElement(n) || IsObjectBindingPattern(n) || IsArrayBindingPattern(n))
            {
                return FindAncestorResult.False;
            }

            return FindAncestorResult.Quit;
        });

        return result is not null;
    }

    /// <summary>Go 的未导出 <c>isLocalVariableOrFunction</c>。</summary>
    private static bool IsLocalVariableOrFunction(Symbol symbol)
    {
        if (symbol.Parent is not null)
        {
            return false; // This is exported symbol
        }

        foreach (var decl in symbol.Declarations ?? [])
        {
            // Function expressions are local
            if (decl.Kind == Kind.FunctionExpression)
            {
                return true;
            }

            if (decl.Kind != Kind.VariableDeclaration && decl.Kind != Kind.FunctionDeclaration)
            {
                continue;
            }

            // If the parent is not source file or module block, it is a local variable.
            var parent = decl.Parent;
            for (; !IsFunctionBlock(parent); parent = parent!.Parent)
            {
                // Reached source file or module block
                if (parent!.Kind == Kind.SourceFile || parent.Kind == Kind.ModuleBlock)
                {
                    break;
                }
            }

            if (IsFunctionBlock(parent))
            {
                // Parent is in function block.
                return true;
            }
        }

        return false;
    }

    /// <summary>Go 的 <c>func GetSymbolModifiers(typeChecker *checker.Checker, symbol *ast.Symbol) ScriptElementKindModifier</c>。</summary>
    public static ScriptElementKindModifier GetSymbolModifiers(CheckerType? typeChecker, Symbol? symbol)
    {
        if (symbol is null)
        {
            return ScriptElementKindModifier.None;
        }

        var modifiers = GetNormalizedSymbolModifiers(typeChecker, symbol);
        if ((symbol.Flags & SymbolFlags.Alias) != 0 && typeChecker is not null)
        {
            var resolvedSymbol = typeChecker.GetAliasedSymbol(symbol);
            if (resolvedSymbol != symbol)
            {
                modifiers |= GetNormalizedSymbolModifiers(typeChecker, resolvedSymbol);
            }
        }

        if ((symbol.Flags & SymbolFlags.Optional) != 0)
        {
            modifiers |= ScriptElementKindModifier.Optional;
        }

        return modifiers;
    }

    /// <summary>Go 的未导出 <c>getNormalizedSymbolModifiers</c>。</summary>
    private static ScriptElementKindModifier GetNormalizedSymbolModifiers(CheckerType? typeChecker, Symbol symbol)
    {
        var modifierSet = ScriptElementKindModifier.None;
        if (symbol.Declarations is { Length: > 0 } declarations)
        {
            var declaration = declarations[0];
            var rest = declarations[1..];
            // omit deprecated flag if some declarations are not deprecated
            ModifierFlags excludeFlags;
            if (rest.Length > 0 &&
                IsDeprecatedDeclaration(typeChecker, declaration) && // !!! include jsdoc node flags
                Some(rest, d => !IsDeprecatedDeclaration(typeChecker, d)))
            {
                excludeFlags = ModifierFlags.Deprecated;
            }
            else
            {
                excludeFlags = ModifierFlags.None;
            }

            modifierSet = GetNodeModifiers(typeChecker, declaration, excludeFlags);
        }

        return modifierSet;
    }

    /// <summary>
    /// Go 的未导出 <c>isDeprecatedDeclaration(typeChecker, declaration)</c>。
    /// 注意：C# 里类内同名方法会**遮蔽** `using static TypeScript.Ast.Ast;` 导入的
    /// `Ast.IsDeprecatedDeclaration(Node?)`（1 参版本），所以本文件里对 AST 版本的调用一律写
    /// `global::TypeScript.Ast.Ast.…` 全名（docs/architecture.md §2 的"命名空间遮蔽"问题）。
    /// </summary>
    private static bool IsDeprecatedDeclaration(CheckerType? typeChecker, Node declaration)
    {
        if (typeChecker is not null)
        {
            return typeChecker.IsDeprecatedDeclaration(declaration);
        }

        return global::TypeScript.Ast.Ast.IsDeprecatedDeclaration(declaration);
    }

    /// <summary>Go 的未导出 <c>getNodeModifiers</c>。</summary>
    private static ScriptElementKindModifier GetNodeModifiers(CheckerType? typeChecker, Node node, ModifierFlags excludeFlags)
    {
        var result = ScriptElementKindModifier.None;
        var flags = ModifierFlags.None;
        if (IsDeclaration(node))
        {
            flags = GetCombinedModifierFlags(node);
            if (IsDeprecatedDeclaration(typeChecker, node))
            {
                flags |= ModifierFlags.Deprecated;
            }

            flags &= ~excludeFlags;
        }

        if ((flags & ModifierFlags.Private) != 0)
        {
            result |= ScriptElementKindModifier.Private;
        }

        if ((flags & ModifierFlags.Protected) != 0)
        {
            result |= ScriptElementKindModifier.Protected;
        }

        if ((flags & ModifierFlags.Public) != 0)
        {
            result |= ScriptElementKindModifier.Public;
        }

        if ((flags & ModifierFlags.Static) != 0)
        {
            result |= ScriptElementKindModifier.Static;
        }

        if ((flags & ModifierFlags.Abstract) != 0)
        {
            result |= ScriptElementKindModifier.Abstract;
        }

        if ((flags & ModifierFlags.Export) != 0)
        {
            result |= ScriptElementKindModifier.Exported;
        }

        if ((flags & ModifierFlags.Deprecated) != 0)
        {
            result |= ScriptElementKindModifier.Deprecated;
        }

        if ((flags & ModifierFlags.Ambient) != 0)
        {
            result |= ScriptElementKindModifier.Ambient;
        }

        if ((node.Flags & NodeFlags.Ambient) != 0)
        {
            result |= ScriptElementKindModifier.Ambient;
        }

        if (node.Kind == Kind.ExportAssignment)
        {
            result |= ScriptElementKindModifier.Exported;
        }

        return result;
    }
}
