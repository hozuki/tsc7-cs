// ported from: internal/binder/referenceresolver.go:1-262（ReferenceResolver 接口 / ReferenceResolverHooks /
//   referenceResolver 实现 / NewReferenceResolver）+ nameresolver.go:450-506 的 5 个包级函数
//   （GetLocalSymbolForExportDefault / isExportDefaultSymbol / getIsDeferredContext /
//   isTypeParameterSymbolDeclaredInContainer / isSelfReferenceLocation）。
//   按契约 §1，包级函数一律落在 `partial class Binder`；本切片把它们放在**本文件末尾**的 Binder 分片里，这样
//   NameResolver.cs 只有 NameResolver 一个类型（NameResolver.cs 的调用点写 `Binder.Xxx(...)`）。
//
// 与 Go 的差异（语言特性导致、且必须等价）：
//  1) 类型形状：`type ReferenceResolver interface` → `public interface ReferenceResolver`（导出类型名照抄，不加 I 前缀）；
//     `type ReferenceResolverHooks struct`（导出）→ `public sealed class ReferenceResolverHooks`（导出字段名照抄，
//     public 字段）；`type referenceResolver struct`（未导出）→ 按 C# 习惯命名 `public sealed class ReferenceResolverImpl`
//     并 `: ReferenceResolver`。构造器 `internal`（Go 里该类型未导出，只能经 `NewReferenceResolver` 创建）。
//  2) Go 的类型别名 `ast.IdentifierNode`/`ast.Declaration`（`ast_generated.go:253/570`，都 `= Node`）→ C# 的 `Node`；
//     Go 里可能为 nil 的指针 → `T?`（`Node?`/`Symbol?`/`Message?`）。`GetReferencedValueDeclarations` 的返回是
//     `Node[]`：Go 的 nil 切片与空切片对调用点（`for range`）无差别，统一成空数组（契约 §3.2）。
//  3) `GetElementAccessExpressionName` hook 的 Go 形状是 `func(*ElementAccessExpression) (string, bool)`：C# 的
//     `Func<>` 表达不了 `(string, bool)`（契约：`(T, bool)` → `TryX`，但接口方法名/形状已固定），改用
//     `Func<ElementAccessExpression, string?>`，**返回 null = Go 的 ok=false；返回非 null（含空串）= Go 的 ok=true**。
//     等价性：Go 的调用点是 `if name, ok := hook(x); ok { return name }`，C# 是 `var name = hook(x);
//     if (name is not null) { return name }`——ok=false 时 Go 返回 ""（下面的 `return ""` 兜底），C# 同样落到兜底；
//     ok=true 且 name=="" 时两边都返回空串（C# 的空串不是 null，不会被误判成 ok=false）。
//  4) `ReferenceResolverHooks` 在 Go 里是结构体值（`NewReferenceResolver` 收的是副本）。C# 用 class + 引用赋值
//     （`_hooks = hooks`，不逐字段复制）：两个调用点（checker/emitresolver.go、compiler/emitter.go）都在构造点写
//     字面量、之后不再持有/修改，观察行为一致。
//  5) `options *core.CompilerOptions` → 非空 `CompilerOptions`：Go 的两个调用点都传非 nil（`host.Options()` /
//     `r.checker.compilerOptions`），而 C# 里若写成 `CompilerOptions?` 会逼着 `NameResolver.CompilerOptions`（非空）
//     的赋值点写 `!`。
//  6) 可见性：Go 导出的 6 个接口方法 → public；未导出的方法（`getResolvedSymbol`/`getMergedSymbol`/
//     `getParentOfSymbol`/`getSymbolOfDeclaration`/`getReferencedValueSymbol`/`isTypeOnlyAliasDeclaration`/
//     `getDeclarationOfAliasSymbol`/`getExportSymbolOfValueSymbolIfExported`）→ private PascalCase。
//     其中 `getSymbolOfDeclaration` 与 hooks 里的同名字段冲突（C# 字段与方法不能同名，CS0102）→ 加 `Worker` 后缀
//     （项目惯例：`EmitSourceFileWorker`/`GetEmitStateWorker`）→ `GetSymbolOfDeclarationWorker`；NameResolver.cs 的
//     `error`/`lookup`/`getSymbolOfDeclaration`/`argumentsSymbol` 同理（见那边的文件头 2）。
//  7) Go 里对可能为 nil 的指针直接解引用（nil 会 panic）的位置，C# 用 `!`（如 `getExportSymbolOfValueSymbolIfExported
//     (symbol)!.ValueDeclaration`、`GetMergedSymbol(...)!`：Go 的不变量是 ExportValue 符号必有 ExportSymbol）。
//  8) map 读取：`m[k]` → `?.GetValueOrDefault(k)`；`node.Symbol()` → `node.Symbol`（属性）；`Node` 不覆写 `==`，
//     所以 Go 的指针相等判断（`reference.Parent.Name() == reference`）在 C# 是引用相等。
//  9) `core.FindLast(symbol.Declarations, ast.IsAliasSymbolDeclaration)` → `Core.FindLast(symbol.Declarations,
//     Ast.IsAliasSymbolDeclaration)`（`Declarations` 是 `Node[]?`，`FindLast` 收 `T[]?`）。
// 10) 末尾 Binder 分片里的 5 个包级函数：`GetLocalSymbolForExportDefault` 在 Go 里导出 → `public static`（契约 §1）；
//     其余 4 个未导出 → 名字 = Go 名 PascalCase（`isExportDefaultSymbol` → `IsExportDefaultSymbol`…）。
//     其中 `IsExportDefaultSymbol` 只被 `GetLocalSymbolForExportDefault` 调用 → `private static`；另外 3 个
//     （`GetIsDeferredContext`/`IsTypeParameterSymbolDeclaredInContainer`/`IsSelfReferenceLocation`）被 **NameResolver**
//     （另一个类）调用，C# 的 `private` 跨类型不可见 → `internal static`（契约 §1 里 `internal` 的同一用途）。
//     `getIsDeferredContext`/`isTypeParameterSymbolDeclaredInContainer` 的 `location`/`container` 在 Go 里被无条件解引用
//     → 非空 `Node`；`lastLocation` → `Node?`（Go 里有 `lastLocation != nil` 判断）。
namespace TypeScript.Binder;

using TypeScript.Ast;
using TypeScript.Core;
using TypeScript.Diagnostics;
using static TypeScript.Ast.Ast;

/// <summary>Go: <c>type ReferenceResolver interface</c>（binder/referenceresolver.go:9）。</summary>
public interface ReferenceResolver
{
    Node? GetReferencedExportContainer(Node node, bool prefixLocals);
    Node? GetReferencedImportDeclaration(Node node);
    Node? GetReferencedValueDeclaration(Node node);
    Node[] GetReferencedValueDeclarations(Node node);
    string GetElementAccessExpressionName(ElementAccessExpression? expression);
    Node? GetReferencedMemberValueDeclaration(Node node);
}

/// <summary>Go: <c>type ReferenceResolverHooks struct</c>（binder/referenceresolver.go:18）。</summary>
public sealed class ReferenceResolverHooks
{
    public Func<Node?, string, SymbolFlags, Message?, bool, bool, Symbol?>? ResolveName;
    public Func<Node, Symbol?>? GetResolvedSymbol;
    public Func<Symbol, Symbol?>? GetMergedSymbol;
    public Func<Symbol, Symbol?>? GetParentOfSymbol;
    public Func<Node, Symbol?>? GetSymbolOfDeclaration;
    public Func<Symbol, SymbolFlags, Node?>? GetTypeOnlyAliasDeclaration;
    public Func<Symbol, Symbol?>? GetExportSymbolOfValueSymbolIfExported;
    public Func<ElementAccessExpression, string?>? GetElementAccessExpressionName;
}

/// <summary>Go: <c>type referenceResolver struct</c>（binder/referenceresolver.go:29，未导出 → 按 C# 命名）。</summary>
public sealed class ReferenceResolverImpl : ReferenceResolver
{
    private NameResolver? _resolver;
    private readonly CompilerOptions _options;
    private readonly ReferenceResolverHooks _hooks;

    internal ReferenceResolverImpl(CompilerOptions options, ReferenceResolverHooks hooks)
    {
        _options = options;
        _hooks = hooks;
    }

    private Symbol? GetResolvedSymbol(Node? node)
    {
        if (node is not null)
        {
            if (_hooks.GetResolvedSymbol is not null)
            {
                return _hooks.GetResolvedSymbol(node);
            }
        }
        return null;
    }

    private Symbol? GetMergedSymbol(Symbol? symbol)
    {
        if (symbol is not null)
        {
            if (_hooks.GetMergedSymbol is not null)
            {
                return _hooks.GetMergedSymbol(symbol);
            }
            return symbol;
        }
        return null;
    }

    private Symbol? GetParentOfSymbol(Symbol? symbol)
    {
        if (symbol is not null)
        {
            if (_hooks.GetParentOfSymbol is not null)
            {
                return _hooks.GetParentOfSymbol(symbol);
            }
            return symbol.Parent;
        }
        return null;
    }

    private Symbol? GetSymbolOfDeclarationWorker(Node? declaration)
    {
        if (declaration is not null)
        {
            if (_hooks.GetSymbolOfDeclaration is not null)
            {
                return _hooks.GetSymbolOfDeclaration(declaration);
            }
            return declaration.Symbol;
        }
        return null;
    }

    private Symbol? GetReferencedValueSymbol(Node reference, bool startInDeclarationContainer)
    {
        var resolvedSymbol = GetResolvedSymbol(reference);
        if (resolvedSymbol is not null)
        {
            return resolvedSymbol;
        }

        Node? location = reference;
        if (startInDeclarationContainer && reference.Parent is not null && IsDeclaration(reference.Parent) && reference.Parent.Name() == reference)
        {
            location = GetDeclarationContainer(reference.Parent);
        }

        if (_hooks.ResolveName is not null)
        {
            return _hooks.ResolveName(location, reference.Text(), SymbolFlags.ExportValue | SymbolFlags.Value | SymbolFlags.Alias, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/);
        }

        _resolver ??= new NameResolver
        {
            CompilerOptions = _options,
        };

        return _resolver.Resolve(location, reference.Text(), SymbolFlags.ExportValue | SymbolFlags.Value | SymbolFlags.Alias, null /*nameNotFoundMessage*/, false /*isUse*/, false /*excludeGlobals*/);
    }

    private bool IsTypeOnlyAliasDeclaration(Symbol? symbol)
    {
        if (symbol is not null)
        {
            if (_hooks.GetTypeOnlyAliasDeclaration is not null)
            {
                return _hooks.GetTypeOnlyAliasDeclaration(symbol, SymbolFlags.Value) is not null;
            }

            var node = GetDeclarationOfAliasSymbol(symbol);
            while (node is not null)
            {
                switch (node.Kind)
                {
                    case Kind.ImportEqualsDeclaration:
                    case Kind.ExportDeclaration:
                        return node.IsTypeOnly();
                    case Kind.ImportClause:
                    case Kind.ImportSpecifier:
                    case Kind.ExportSpecifier:
                        if (node.IsTypeOnly())
                        {
                            return true;
                        }
                        node = node.Parent;
                        continue;
                    case Kind.NamedImports:
                    case Kind.NamedExports:
                        node = node.Parent;
                        continue;
                }
                break;
            }
        }
        return false;
    }

    private Node? GetDeclarationOfAliasSymbol(Symbol symbol)
    {
        return Core.FindLast(symbol.Declarations, Ast.IsAliasSymbolDeclaration);
    }

    private Symbol? GetExportSymbolOfValueSymbolIfExported(Symbol? symbol)
    {
        if (symbol is not null)
        {
            if (_hooks.GetExportSymbolOfValueSymbolIfExported is not null)
            {
                return _hooks.GetExportSymbolOfValueSymbolIfExported(symbol);
            }
            if ((symbol.Flags & SymbolFlags.ExportValue) != 0 && symbol.ExportSymbol is not null)
            {
                symbol = symbol.ExportSymbol;
            }
            return GetMergedSymbol(symbol);
        }
        return null;
    }

    public Node? GetReferencedExportContainer(Node node, bool prefixLocals) /*SourceFile|ModuleDeclaration|EnumDeclaration*/
    {
        // When resolving the export for the name of a module or enum
        // declaration, we need to start resolution at the declaration's container.
        // Otherwise, we could incorrectly resolve the export as the
        // declaration if it contains an exported member with the same name.
        var startInDeclarationContainer = node.Parent is not null && (node.Parent.Kind == Kind.ModuleDeclaration || node.Parent.Kind == Kind.EnumDeclaration) && node == node.Parent.Name();
        var symbol = GetReferencedValueSymbol(node, startInDeclarationContainer);
        if (symbol is not null)
        {
            if ((symbol.Flags & SymbolFlags.ExportValue) != 0)
            {
                // If we reference an exported entity within the same module declaration, then whether
                // we prefix depends on the kind of entity. SymbolFlags.ExportHasLocal encompasses all the
                // kinds that we do NOT prefix.
                var exportSymbol = GetMergedSymbol(symbol.ExportSymbol)!;
                if (!prefixLocals && (exportSymbol.Flags & SymbolFlags.ExportHasLocal) != 0 && (exportSymbol.Flags & SymbolFlags.Variable) == 0)
                {
                    return null;
                }
                symbol = exportSymbol;
            }
            var parentSymbol = GetParentOfSymbol(symbol);
            if (parentSymbol is not null)
            {
                if ((parentSymbol.Flags & SymbolFlags.ValueModule) != 0 && parentSymbol.ValueDeclaration is not null && parentSymbol.ValueDeclaration.Kind == Kind.SourceFile)
                {
                    var symbolFile = parentSymbol.ValueDeclaration.AsSourceFile()!;
                    var referenceFile = GetSourceFileOfNode(node);
                    // If `node` accesses an export and that export isn't in the same file, then symbol is a namespace export, so return nil.
                    var symbolIsUmdExport = symbolFile != referenceFile;
                    if (symbolIsUmdExport)
                    {
                        return null;
                    }
                    return symbolFile;
                }
                bool IsMatchingContainer(Node n)
                {
                    return (n.Kind == Kind.ModuleDeclaration || n.Kind == Kind.EnumDeclaration) && GetSymbolOfDeclarationWorker(n) == parentSymbol;
                }

                return FindAncestor(node.Parent, IsMatchingContainer);
            }
        }

        return null;
    }

    public Node? GetReferencedImportDeclaration(Node node)
    {
        var symbol = GetReferencedValueSymbol(node, false /*startInDeclarationContainer*/);
        if (symbol is not null)
        {
            // We should only get the declaration of an alias if there isn't a local value
            // declaration for the symbol
            if (IsNonLocalAlias(symbol, SymbolFlags.Value /*excludes*/) && !IsTypeOnlyAliasDeclaration(symbol))
            {
                return GetDeclarationOfAliasSymbol(symbol);
            }
        }

        return null;
    }

    public Node? GetReferencedValueDeclaration(Node node)
    {
        var symbol = GetReferencedValueSymbol(node, false /*startInDeclarationContainer*/);
        if (symbol is not null)
        {
            return GetExportSymbolOfValueSymbolIfExported(symbol)!.ValueDeclaration;
        }
        return null;
    }

    public Node[] GetReferencedValueDeclarations(Node node)
    {
        List<Node>? declarations = null;
        var symbol = GetReferencedValueSymbol(node, false /*startInDeclarationContainer*/);
        if (symbol is not null)
        {
            symbol = GetExportSymbolOfValueSymbolIfExported(symbol)!;
            var symbolDeclarations = symbol.Declarations;
            if (symbolDeclarations is not null)
            {
                for (var i = 0; i < symbolDeclarations.Length; i++)
                {
                    var declaration = symbolDeclarations[i];
                    switch (declaration.Kind)
                    {
                        case Kind.VariableDeclaration:
                        case Kind.Parameter:
                        case Kind.BindingElement:
                        case Kind.PropertyDeclaration:
                        case Kind.PropertyAssignment:
                        case Kind.ShorthandPropertyAssignment:
                        case Kind.EnumMember:
                        case Kind.ObjectLiteralExpression:
                        case Kind.FunctionDeclaration:
                        case Kind.FunctionExpression:
                        case Kind.ArrowFunction:
                        case Kind.ClassDeclaration:
                        case Kind.ClassExpression:
                        case Kind.EnumDeclaration:
                        case Kind.MethodDeclaration:
                        case Kind.GetAccessor:
                        case Kind.SetAccessor:
                        case Kind.ModuleDeclaration:
                            declarations ??= [];
                            declarations.Add(declaration);
                            break;
                    }
                }
            }
        }
        return declarations is null ? [] : declarations.ToArray();
    }

    public string GetElementAccessExpressionName(ElementAccessExpression? expression)
    {
        if (expression is not null)
        {
            if (_hooks.GetElementAccessExpressionName is not null)
            {
                // Go 的 `(string, bool)` → `string?`：null = ok=false，非 null（含空串）= ok=true（见文件头 3）。
                var name = _hooks.GetElementAccessExpressionName(expression);
                if (name is not null)
                {
                    return name;
                }
            }
        }
        return "";
    }

    public Node? GetReferencedMemberValueDeclaration(Node node)
    {
        // member references are `this.something` or `this[something]`, so should always simply have a resolved symbol
        var s = GetResolvedSymbol(node);
        if (s is null && node.Symbol is not null)
        {
            // might be a declaration instead of a ref, get the merged declaration symbol
            s = GetMergedSymbol(node.Symbol);
        }
        if (s is null)
        {
            return null;
        }
        return GetExportSymbolOfValueSymbolIfExported(s)!.ValueDeclaration;
    }
}

public sealed partial class Binder
{
    public static ReferenceResolver NewReferenceResolver(CompilerOptions options, ReferenceResolverHooks hooks)
    {
        return new ReferenceResolverImpl(options, hooks);
    }

    // ported from: internal/binder/nameresolver.go:450-465（GetLocalSymbolForExportDefault / isExportDefaultSymbol）
    public static Symbol? GetLocalSymbolForExportDefault(Symbol? symbol)
    {
        if (!IsExportDefaultSymbol(symbol))
        {
            return null;
        }
        // Go: `len(symbol.Declarations) == 0`（isExportDefaultSymbol 已排除 nil 与空声明，保留此判断与 Go 逐行对应）
        var declarations = symbol!.Declarations!;
        if (declarations.Length == 0)
        {
            return null;
        }
        for (var i = 0; i < declarations.Length; i++)
        {
            var localSymbol = declarations[i].LocalSymbol();
            if (localSymbol is not null)
            {
                return localSymbol;
            }
        }
        return null;
    }

    private static bool IsExportDefaultSymbol(Symbol? symbol)
    {
        return symbol is not null && symbol.Declarations is not null && symbol.Declarations.Length > 0 && HasSyntacticModifier(symbol.Declarations[0], ModifierFlags.Default);
    }

    // ported from: internal/binder/nameresolver.go:467-483（getIsDeferredContext）
    // internal（不是 private）：NameResolver 是另一个类，C# 的 private 跨类型不可见（契约 §1 的 internal 例外）。
    internal static bool GetIsDeferredContext(Node location, Node? lastLocation)
    {
        if (location.Kind != Kind.ArrowFunction && location.Kind != Kind.FunctionExpression)
        {
            // initializers in instance property declaration of class like entities are executed in constructor and thus deferred
            // A name is evaluated within the enclosing scope - so it shouldn't count as deferred
            return IsTypeQueryNode(location) ||
                (IsFunctionLikeDeclaration(location) || location.Kind == Kind.PropertyDeclaration && !IsStatic(location)) &&
                    (lastLocation is null || lastLocation != location.Name());
        }
        if (lastLocation is not null && lastLocation == location.Name())
        {
            return false;
        }
        // generator functions and async functions are not inlined in control flow when immediately invoked
        if (location.BodyData()!.AsteriskToken is not null || HasSyntacticModifier(location, ModifierFlags.Async))
        {
            return true;
        }
        return GetImmediatelyInvokedFunctionExpression(location) is null;
    }

    // ported from: internal/binder/nameresolver.go:485-495（isTypeParameterSymbolDeclaredInContainer）
    internal static bool IsTypeParameterSymbolDeclaredInContainer(Symbol symbol, Node container)
    {
        var declarations = symbol.Declarations;
        if (declarations is not null)
        {
            for (var i = 0; i < declarations.Length; i++)
            {
                var decl = declarations[i];
                if (decl.Kind == Kind.TypeParameter)
                {
                    var parent = decl.Parent;
                    if (parent == container)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // ported from: internal/binder/nameresolver.go:497-506（isSelfReferenceLocation）
    internal static bool IsSelfReferenceLocation(Node node, Node? lastLocation)
    {
        switch (node.Kind)
        {
            case Kind.Parameter:
                return lastLocation is not null && lastLocation == node.Name();
            case Kind.FunctionDeclaration:
            case Kind.ClassDeclaration:
            case Kind.InterfaceDeclaration:
            case Kind.EnumDeclaration:
            case Kind.TypeAliasDeclaration:
            case Kind.JSTypeAliasDeclaration:
            case Kind.ModuleDeclaration: // For `namespace N { N; }`
                return true;
        }
        return false;
    }
}
