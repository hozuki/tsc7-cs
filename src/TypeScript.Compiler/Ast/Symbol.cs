// ported from: internal/ast/symbol.go
//
// 与 Go 的差异（C# 语言特性，依据 docs/architecture.md §3.2）：
// - Go 的 `id atomic.Uint64` → C# 的 `internal ulong _id`（用 Interlocked/Volatile 的分配点在
//   utilities.go 的 GetSymbolId，属于那一轮的移植范围；本轮只保留字段与命名）。
// - Go 的 `type SymbolTable map[string]*Symbol` → C# 的 `SymbolTable : Dictionary<string, Symbol>`。
//   Go 的 nil map 对应 C# 的 `null`（调用点写 `SymbolTable?`），所以它必须是引用类型而不是结构体
//   （结构体没有"整体为 null"的表示）。
// - Go 的 nil 接收者方法（如 `symbol.ValueDeclaration != nil` 之前就解引用 symbol）在 C# 里调用即 NRE，
//   与 Go 的 panic 对应，所以不做 nil 分支（docs/architecture.md §3.2）。
// - `InternalSymbolName*` 常量在 Go 里是包级导出常量 → C# 的 `Ast` 静态类里的 `public const string`。
//   注意 `InternalSymbolNamePrefix` 在 Go 里是导出常量（binder/checker 都用 `ast.InternalSymbolNamePrefix`），
//   所以这里也是 public。
// - `SymbolName`/`EscapeAllInternalSymbolNames`/`EscapeInternalSymbolName`/`EscapeSymbolName` 在 Go 里是
//   symbol.go 里的包级函数 → C# 的 `Ast` 静态类方法。
// - `IsAmbientModuleSymbolName` 在 Go 里位于 utilities.go:1662（symbol.go 只调用它）；为了让
//   `Symbol.IsExternalModule` 本轮就可用，按 Go 原文移植到这里（另见文件末尾注释）。
namespace TypeScript.Ast;

public sealed class Symbol
{
    public SymbolFlags Flags;
    public CheckFlags CheckFlags; // Non-zero only in transient symbols created by Checker
    public string Name = "";
    public Node[]? Declarations;
    public Node? ValueDeclaration;
    public SymbolTable? Members;
    public SymbolTable? Exports;
    internal ulong _id;
    public Symbol? Parent;
    public Symbol? ExportSymbol;

    public bool IsExternalModule()
    {
        return (Flags & SymbolFlags.Module) != 0 && Ast.IsAmbientModuleSymbolName(Name);
    }

    public bool IsStatic()
    {
        if (ValueDeclaration is null)
        {
            return false;
        }

        var modifierFlags = ValueDeclaration.ModifierFlags;
        return (modifierFlags & ModifierFlags.Static) != 0;
    }

    // See comment on `declareModuleMember` in `binder.go`.
    public SymbolFlags CombinedLocalAndExportSymbolFlags()
    {
        if (ExportSymbol is not null)
        {
            return Flags | ExportSymbol.Flags;
        }

        return Flags;
    }
}

/// <summary>Go 的 <c>type SymbolTable map[string]*Symbol</c>。</summary>
/// <remarks>Go 的 nil map 对应 C# 的 <c>null</c>（`SymbolTable?`），所以这里必须是 class。</remarks>
public sealed class SymbolTable : Dictionary<string, Symbol>
{
}

public static partial class Ast
{
    public const string InternalSymbolNamePrefix = "\u00FE"; // Invalid UTF8 sequence, will never occur as IdentifierName

    public const string InternalSymbolNameCall = InternalSymbolNamePrefix + "call";                                       // Call signatures
    public const string InternalSymbolNameConstructor = InternalSymbolNamePrefix + "constructor";                         // Constructor implementations
    public const string InternalSymbolNameNew = InternalSymbolNamePrefix + "new";                                         // Constructor signatures
    public const string InternalSymbolNameIndex = InternalSymbolNamePrefix + "index";                                     // Index signatures
    public const string InternalSymbolNameExportStar = InternalSymbolNamePrefix + "export";                               // Module export * declarations
    public const string InternalSymbolNameGlobal = InternalSymbolNamePrefix + "global";                                   // Global self-reference
    public const string InternalSymbolNameMissing = InternalSymbolNamePrefix + "missing";                                 // Indicates missing symbol
    public const string InternalSymbolNameType = InternalSymbolNamePrefix + "type";                                       // Anonymous type literal symbol
    public const string InternalSymbolNameObject = InternalSymbolNamePrefix + "object";                                   // Anonymous object literal declaration
    public const string InternalSymbolNameJSXAttributes = InternalSymbolNamePrefix + "jsxAttributes";                     // Anonymous JSX attributes object literal declaration
    public const string InternalSymbolNameClass = InternalSymbolNamePrefix + "class";                                     // Unnamed class expression
    public const string InternalSymbolNameFunction = InternalSymbolNamePrefix + "function";                               // Unnamed function expression
    public const string InternalSymbolNameComputed = InternalSymbolNamePrefix + "computed";                               // Computed property name declaration with dynamic name
    public const string InternalSymbolNameAssignmentDeclaration = InternalSymbolNamePrefix + "assignment";                // Assignment declarations
    public const string InternalSymbolNameInstantiationExpression = InternalSymbolNamePrefix + "instantiationExpression"; // Instantiation expressions
    public const string InternalSymbolNameImportAttributes = InternalSymbolNamePrefix + "importAttributes";
    public const string InternalSymbolNameExportEquals = "export=";          // Export assignment symbol
    public const string InternalSymbolNameDefault = "default";               // Default export symbol (technically not wholly internal, but included here for usability)
    public const string InternalSymbolNameThis = "this";
    public const string InternalSymbolNameModuleExports = "module.exports";

    public static string SymbolName(Symbol? symbol)
    {
        // Go 在这里不做 nil 检查（symbol 为 nil 时解引用会 panic）；C# 里 `symbol!` 之后的访问就是 NRE。
        var s = symbol!;
        var valueDeclaration = s.ValueDeclaration;
        if (valueDeclaration is not null && IsPrivateIdentifierClassElementDeclaration(valueDeclaration))
        {
            // Go: symbol.ValueDeclaration.Name().Text()。PrivateIdentifier 类元素的 name 节点恒非 nil
            // （isPrivateIdentifierClassElementDeclaration 的判定），Go 同样直接解引用。
            return valueDeclaration.Name()!.Text();
        }

        return s.Name;
    }

    /// <summary>Go 的 <c>EscapeAllInternalSymbolNames</c>：把内部符号名标记（"\xFE"）替换成 "__"。</summary>
    public static string EscapeAllInternalSymbolNames(string name)
    {
        return name.Replace(InternalSymbolNamePrefix, "__", StringComparison.Ordinal);
    }

    public static string EscapeInternalSymbolName(string name)
    {
        if (name.StartsWith(InternalSymbolNamePrefix, StringComparison.Ordinal))
        {
            return "__" + name[InternalSymbolNamePrefix.Length..];
        }

        return name;
    }

    /// <summary>
    /// Go 的 <c>EscapeSymbolName</c>：把 binder 的符号名转成它的 "__String" 形式。内部名（以 "\xFE"
    /// 哨兵开头）变成 "__" 前缀；本身就以 "__" 开头的用户名字前面再加一个下划线，以便与内部名区分。
    /// </summary>
    public static string EscapeSymbolName(string name)
    {
        if (name.StartsWith(InternalSymbolNamePrefix, StringComparison.Ordinal))
        {
            return "__" + name[InternalSymbolNamePrefix.Length..];
        }

        if (name.Length >= 2 && name[0] == '_' && name[1] == '_')
        {
            return "_" + name;
        }

        return name;
    }

    // ported from: internal/ast/utilities.go (IsAmbientModuleSymbolName)。
    // Go 的实现委托给 TryGetAmbientModuleNameFromSymbolName 并丢弃取出的名字；这里只保留布尔结果，
    // 因为 TryGetAmbientModuleNameFromSymbolName 本身属于 utilities.go 的移植范围（下一轮），
    // 那个函数移植时必须**不要**重复声明本函数。
    // Ambient module 符号有两种形式：`"modulename"` 或 `InternalSymbolNamePrefix + "\"modulename\"pattern@nodeId"`；
    // 见 `getDeclarationName`。
    public static bool IsAmbientModuleSymbolName(string s)
    {
        if (s.StartsWith('"') && s.EndsWith('"'))
        {
            return true;
        }

        var patternPrefix = InternalSymbolNamePrefix + "\"";
        if (!s.StartsWith(patternPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = s[patternPrefix.Length..];
        return rest.LastIndexOf("\"pattern@", StringComparison.Ordinal) >= 1;
    }
}
