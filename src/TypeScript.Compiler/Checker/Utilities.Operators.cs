// ported from: internal/checker/utilities.go:757-907（从符号取修饰符标志 + 二元运算符优先级 + orderedSet）
//
// 与 Go 的机械改写（通用规则见 Utilities.cs 文件头）：
// 1) Go 的无条件 `switch { case A: ...; case B: ... }` → C# `if/else if`（条件即 case 的布尔表达式，语义相同）。
// 2) Go 的 `flags & ^ast.ModifierFlagsAccessibilityModifier` → `flags & ~ModifierFlags.AccessibilityModifier`。
// 3) 命名：Go 的包级 `getDeclarationModifierFlagsFromSymbol` 与 exports.go 的**导出** `GetDeclarationModifierFlagsFromSymbol`
//    在 C# 里同名（CS0111）→ 未导出者加 `Worker` 后缀（docs/checker-plan.md §5.2；
//    artifacts/checker-method-collisions.md 追加表），故为 `GetDeclarationModifierFlagsFromSymbolWorker`。
// 4) Go 的 `core.Find(s.Declarations, ast.IsSetAccessorDeclaration)` → `Find(s.Declarations, IsSetAccessorDeclaration)`
//    （Ast 的静态方法组转 `Func<Node, bool>`；`Find<T>` 的 T 由 `s.Declarations` 推断）。
// 5) Go 的 `map[T]struct{}` → `HashSet<T>`；`make(map[T]struct{}, n)` → `new HashSet<T>(n)`（容量提示）；
//    `m[v] = struct{}{}` → `m.Add(v)`；`_, ok := m[v]` → `m.Contains(v)`。
// 6) Go 的包级常量 `orderedSetMapThreshold` → 本类里的 `internal const int SmallOrderedSetMapThreshold`
//    （docs/architecture.md §2：const 任何可见性都用 PascalCase；Go 的包级 var/const 放 partial 类里；
//    Main 裁决随类型名一起加 `Small` 前缀；`internal` 是因为同文件的 `SmallOrderedSet<T>` 也要读它）。
// 7) Go 的包级类型 `orderedSet[T comparable]` → 命名空间级 `SmallOrderedSet<T>`；**刻意不叫 `OrderedSet`**：
//    那会遮蔽 `TypeScript.Collections.OrderedSet`（同命名空间优先于 using），而两者 `Add` 的语义不同
//    （本类不去重，只靠调用点 `Contains`）。`comparable` 在 C# 里对应 `where T : notnull`（HashSet 键的约束）。
namespace TypeScript.Checker;

using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

/// <summary>
/// Go: <c>type orderedSet[T comparable] struct { valuesByKey map[T]struct{}; values []T }</c>（utilities.go:879-882）。
/// 有序去重集合：小集合用线性扫描，超过阈值才物化去重表。
/// </summary>
internal sealed class SmallOrderedSet<T>
    where T : notnull
{
    private HashSet<T>? _valuesByKey;

    // Go: `s.values` —— C# 侧是 `internal`（Go 包内可见；checker.go 的 `orderedTypes.values = make(...)`
    // 在调用点直接赋值，因此不能是 readonly）。
    internal List<T> _values = [];

    /// <summary>Go: <c>func (s *orderedSet[T]) contains(value T) bool</c>（utilities.go:884-890）。</summary>
    internal bool Contains(T value)
    {
        if (_valuesByKey is null)
        {
            return _values.Contains(value);
        }
        return _valuesByKey.Contains(value);
    }

    /// <summary>Go: <c>func (s *orderedSet[T]) add(value T)</c>（utilities.go:892-906）。</summary>
    internal void Add(T value)
    {
        _values.Add(value);
        // Small sets are served by a linear scan over values; only materialize the map once the set
        // grows large enough for hashing to win.
        if (_valuesByKey is null)
        {
            if (_values.Count <= Checker.SmallOrderedSetMapThreshold)
            {
                return;
            }
            _valuesByKey = new HashSet<T>(_values.Count);
            for (var i = 0; i < _values.Count - 1; i++)
            {
                _valuesByKey.Add(_values[i]);
            }
        }
        _valuesByKey.Add(value);
    }
}

public sealed partial class Checker
{
    // orderedSetMapThreshold is the size at which an orderedSet materializes its dedup map.
    // Below this, contains() scans the values slice.
    // Go 的包级常量 → C# 侧 `internal const`（同文件的 `SmallOrderedSet<T>` 也要读它）。
    internal const int SmallOrderedSetMapThreshold = 16;

    /// <summary>Go: <c>func getDeclarationModifierFlagsFromSymbol(s *ast.Symbol) ast.ModifierFlags</c>（utilities.go:757-759）。</summary>
    internal static ModifierFlags GetDeclarationModifierFlagsFromSymbolWorker(Symbol s)
    {
        return GetDeclarationModifierFlagsFromSymbolEx(s, false /*isWrite*/);
    }

    /// <summary>Go: <c>func getDeclarationModifierFlagsFromSymbolEx(s *ast.Symbol, isWrite bool) ast.ModifierFlags</c>（utilities.go:761-798）。</summary>
    internal static ModifierFlags GetDeclarationModifierFlagsFromSymbolEx(Symbol s, bool isWrite)
    {
        if ((s.CheckFlags & CheckFlags.Synthetic) != 0)
        {
            var accessModifier = ModifierFlags.None;
            if (!isWrite && (s.CheckFlags & CheckFlags.ContainsPublic) != 0 || isWrite && (s.CheckFlags & CheckFlags.ContainsWritePublic) != 0)
            {
                accessModifier = ModifierFlags.Public;
            }
            else if (!isWrite && (s.CheckFlags & CheckFlags.ContainsProtected) != 0 || isWrite && (s.CheckFlags & CheckFlags.ContainsWriteProtected) != 0)
            {
                accessModifier = ModifierFlags.Protected;
            }
            else if (!isWrite && (s.CheckFlags & CheckFlags.ContainsPrivate) != 0 || isWrite && (s.CheckFlags & CheckFlags.ContainsWritePrivate) != 0)
            {
                accessModifier = ModifierFlags.Private;
            }
            if ((s.CheckFlags & CheckFlags.ContainsStatic) != 0)
            {
                return accessModifier | ModifierFlags.Static;
            }
            return accessModifier;
        }
        if (s.ValueDeclaration is not null)
        {
            Node? declaration = null;
            if (isWrite)
            {
                declaration = Find(s.Declarations, IsSetAccessorDeclaration);
            }
            if (declaration is null && (s.Flags & SymbolFlags.GetAccessor) != 0)
            {
                declaration = Find(s.Declarations, IsGetAccessorDeclaration);
            }
            declaration ??= s.ValueDeclaration;
            var flags = GetCombinedModifierFlags(declaration!);
            if (s.Parent is not null && (s.Parent.Flags & SymbolFlags.Class) != 0)
            {
                return flags;
            }
            return flags & ~ModifierFlags.AccessibilityModifier;
        }
        if ((s.Flags & SymbolFlags.Prototype) != 0)
        {
            return ModifierFlags.Public | ModifierFlags.Static;
        }
        return ModifierFlags.None;
    }

    /// <summary>Go: <c>func isExponentiationOperator(kind ast.Kind) bool</c>（utilities.go:800-802）。</summary>
    internal static bool IsExponentiationOperator(Kind kind)
    {
        return kind == Kind.AsteriskAsteriskToken;
    }

    /// <summary>Go: <c>func isMultiplicativeOperator(kind ast.Kind) bool</c>（utilities.go:804-806）。</summary>
    internal static bool IsMultiplicativeOperator(Kind kind)
    {
        return kind == Kind.AsteriskToken || kind == Kind.SlashToken || kind == Kind.PercentToken;
    }

    /// <summary>Go: <c>func isMultiplicativeOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:808-810）。</summary>
    internal static bool IsMultiplicativeOperatorOrHigher(Kind kind)
    {
        return IsExponentiationOperator(kind) || IsMultiplicativeOperator(kind);
    }

    /// <summary>Go: <c>func isAdditiveOperator(kind ast.Kind) bool</c>（utilities.go:812-814）。</summary>
    internal static bool IsAdditiveOperator(Kind kind)
    {
        return kind == Kind.PlusToken || kind == Kind.MinusToken;
    }

    /// <summary>Go: <c>func isAdditiveOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:816-818）。</summary>
    internal static bool IsAdditiveOperatorOrHigher(Kind kind)
    {
        return IsAdditiveOperator(kind) || IsMultiplicativeOperatorOrHigher(kind);
    }

    /// <summary>Go: <c>func isShiftOperator(kind ast.Kind) bool</c>（utilities.go:820-823）。</summary>
    internal static bool IsShiftOperator(Kind kind)
    {
        return kind == Kind.LessThanLessThanToken || kind == Kind.GreaterThanGreaterThanToken ||
            kind == Kind.GreaterThanGreaterThanGreaterThanToken;
    }

    /// <summary>Go: <c>func isShiftOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:825-827）。</summary>
    internal static bool IsShiftOperatorOrHigher(Kind kind)
    {
        return IsShiftOperator(kind) || IsAdditiveOperatorOrHigher(kind);
    }

    /// <summary>Go: <c>func isRelationalOperator(kind ast.Kind) bool</c>（utilities.go:829-832）。</summary>
    internal static bool IsRelationalOperator(Kind kind)
    {
        return kind == Kind.LessThanToken || kind == Kind.LessThanEqualsToken || kind == Kind.GreaterThanToken ||
            kind == Kind.GreaterThanEqualsToken || kind == Kind.InstanceOfKeyword || kind == Kind.InKeyword;
    }

    /// <summary>Go: <c>func isRelationalOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:834-836）。</summary>
    internal static bool IsRelationalOperatorOrHigher(Kind kind)
    {
        return IsRelationalOperator(kind) || IsShiftOperatorOrHigher(kind);
    }

    /// <summary>Go: <c>func isEqualityOperator(kind ast.Kind) bool</c>（utilities.go:838-841）。</summary>
    internal static bool IsEqualityOperator(Kind kind)
    {
        return kind == Kind.EqualsEqualsToken || kind == Kind.EqualsEqualsEqualsToken ||
            kind == Kind.ExclamationEqualsToken || kind == Kind.ExclamationEqualsEqualsToken;
    }

    /// <summary>Go: <c>func isEqualityOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:843-845）。</summary>
    internal static bool IsEqualityOperatorOrHigher(Kind kind)
    {
        return IsEqualityOperator(kind) || IsRelationalOperatorOrHigher(kind);
    }

    /// <summary>Go: <c>func isBitwiseOperator(kind ast.Kind) bool</c>（utilities.go:847-849）。</summary>
    internal static bool IsBitwiseOperator(Kind kind)
    {
        return kind == Kind.AmpersandToken || kind == Kind.BarToken || kind == Kind.CaretToken;
    }

    /// <summary>Go: <c>func isBitwiseOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:851-853）。</summary>
    internal static bool IsBitwiseOperatorOrHigher(Kind kind)
    {
        return IsBitwiseOperator(kind) || IsEqualityOperatorOrHigher(kind);
    }

    /// <summary>Go: <c>func isLogicalOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:855-857）。</summary>
    internal static bool IsLogicalOperatorOrHigher(Kind kind)
    {
        return IsLogicalBinaryOperator(kind) || IsBitwiseOperatorOrHigher(kind);
    }

    /// <summary>Go: <c>func isAssignmentOperatorOrHigher(kind ast.Kind) bool</c>（utilities.go:859-861）。</summary>
    internal static bool IsAssignmentOperatorOrHigher(Kind kind)
    {
        return kind == Kind.QuestionQuestionToken || IsLogicalOperatorOrHigher(kind) || IsAssignmentOperator(kind);
    }

    /// <summary>Go: <c>func isBinaryOperator(kind ast.Kind) bool</c>（utilities.go:863-865）。</summary>
    internal static bool IsBinaryOperator(Kind kind)
    {
        return IsAssignmentOperatorOrHigher(kind) || kind == Kind.CommaToken;
    }

    /// <summary>Go: <c>func isObjectLiteralType(t *Type) bool</c>（utilities.go:867-869）。</summary>
    internal static bool IsObjectLiteralType(Type t)
    {
        return (t._objectFlags & ObjectFlags.ObjectLiteral) != 0;
    }

    /// <summary>Go: <c>func isDeclarationReadonly(declaration *ast.Node) bool</c>（utilities.go:871-873）。</summary>
    internal static bool IsDeclarationReadonly(Node declaration)
    {
        return (GetCombinedModifierFlags(declaration) & ModifierFlags.Readonly) != 0 && !IsParameterPropertyDeclaration(declaration, declaration.Parent);
    }
}
