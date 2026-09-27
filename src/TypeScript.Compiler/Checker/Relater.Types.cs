// ported from: internal/checker/relater.go:1-115（SignatureCheckMode / MinArgumentCountFlags / IntersectionState /
//   RecursionFlags / ExpandingFlags / RelationComparisonResult / DiagnosticAndArguments / ErrorOutputContainer /
//   ErrorReporter / RecursionId / asRecursionId / Relation）
//   + internal/checker/relater.go:2569-2617（errorState / ErrorChain / Relater / getRelater / putRelater）
//   + internal/checker/relater.go:3212-3222（getErrorState / restoreErrorState）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) Go 的 `const` 位掩码组 → C# `[Flags] enum`：数值与 Go 一致，成员名 = Go 常量名去类型前缀
//    （docs/architecture.md §3.3；`SignatureCheckMode`/`MinArgumentCountFlags`/`IntersectionState`/`RecursionFlags`/
//    `ExpandingFlags`/`RelationComparisonResult`）。C# 允许对 enum 直接做 `& | ^ ~` 与 `&= |=`，故调用点与 Go 逐字对应。
// 2) Go 的 `type ErrorReporter func(message *diagnostics.Message, args ...any)` → C# `delegate`（同名，public）；
//    `params object?[] args` 与 Go 的 `args ...any` 逐字对应（调用点写法不变）。
// 3) `TypeRelatedComparer` 是 Go 里**没有命名**的裸函数类型 `func(source, target *Type) Ternary` 的 C# 具名化
//    （relater.go 5 处 + checker.go 的 `compareProperties`；见 docs/checker-plan §5.5）。
// 4) `asRecursionId[T *ast.Node | *ast.Symbol | *Type]` 的三个类型实参在 C# 里统一成 `object`
//    （三种实参都是引用类型，AOT 安全，无反射）。
// 5) `Relation.results` 是 Go map：**读缺失键返回零值** `RelationComparisonResultNone`，C# 用 `TryGetValue`
//    （`Dictionary` 索引器读缺失键会抛异常，docs/checker-plan §5.4）。
// 6) `Relater.maybeKeys/sourceStack/targetStack` 在 Go 里是 `append` + `[:n]` 截断的切片 → C# `List<T>`
//    （`RemoveRange`/`RemoveAt` 对应截断，`len()` → `Count`；C# 数组没有 len<cap 视图）。
// 7) `putRelater` 的 `*r = Relater{...}` 复合字面量（整体重置、保留切片容量）→ C# 逐字段重置 + `List.Clear()`
//    （保留容量），并**保留**三个缓存委托：Relater 是池化对象，缓存委托让热路径零分配（见报告"函数值参数/闭包"一节）。
// 8) `RecursionId` 的 Go 值比较（struct 内一个 `any` 字段的 `==`）→ C# `ReferenceEquals` 实现的 `operator ==`
//    （三个类型实参都是引用类型，Go 的 `==` 对指针即身份比较）。
namespace TypeScript.Checker;

using System.Runtime.CompilerServices;
using TypeScript.Ast;
using TypeScript.Collections;
using TypeScript.Diagnostics;

/// <summary>Go: <c>type SignatureCheckMode uint32</c>。</summary>
[Flags]
public enum SignatureCheckMode : uint
{
    None = 0,
    BivariantCallback = 1 << 0,
    StrictCallback = 1 << 1,
    IgnoreReturnTypes = 1 << 2,
    StrictArity = 1 << 3,
    StrictTopSignature = 1 << 4,
    Callback = BivariantCallback | StrictCallback,
}

/// <summary>Go: <c>type MinArgumentCountFlags uint32</c>。</summary>
[Flags]
public enum MinArgumentCountFlags : uint
{
    None = 0,
    StrongArityForUntypedJS = 1 << 0,
    VoidIsNonOptional = 1 << 1,
}

/// <summary>Go: <c>type IntersectionState uint32</c>。</summary>
[Flags]
public enum IntersectionState : uint
{
    None = 0,
    /// <summary>Source type is a constituent of an outer intersection.</summary>
    Source = 1 << 0,
    /// <summary>Target type is a constituent of an outer intersection.</summary>
    Target = 1 << 1,
}

/// <summary>Go: <c>type RecursionFlags uint32</c>。</summary>
[Flags]
public enum RecursionFlags : uint
{
    None = 0,
    Source = 1 << 0,
    Target = 1 << 1,
    Both = Source | Target,
}

/// <summary>Go: <c>type ExpandingFlags uint8</c>。</summary>
[Flags]
public enum ExpandingFlags : byte
{
    None = 0,
    Source = 1 << 0,
    Target = 1 << 1,
    Both = Source | Target,
}

/// <summary>Go: <c>type RelationComparisonResult uint32</c>。</summary>
[Flags]
public enum RelationComparisonResult : uint
{
    None = 0,
    Succeeded = 1 << 0,
    Failed = 1 << 1,
    ReportsUnmeasurable = 1 << 3,
    ReportsUnreliable = 1 << 4,
    ComplexityOverflow = 1 << 5,
    ReportsMask = ReportsUnmeasurable | ReportsUnreliable,
    Overflow = ComplexityOverflow,
}

/// <summary>Go: <c>type DiagnosticAndArguments struct</c>（当前 Go 侧无使用点，按声明复刻）。</summary>
public sealed class DiagnosticAndArguments
{
    internal Message _message = null!;
    internal object?[] _arguments = [];
}

/// <summary>Go: <c>type ErrorOutputContainer struct</c>（当前 Go 侧无使用点，按声明复刻）。</summary>
public sealed class ErrorOutputContainer
{
    internal Diagnostic[] _errors = [];
    internal bool _skipLogging;
}

/// <summary>Go: <c>type ErrorReporter func(message *diagnostics.Message, args ...any)</c>（relater.go:87）。</summary>
public delegate void ErrorReporter(Message message, params object?[] args);

/// <summary>
/// Go 里没有命名的裸函数类型 <c>func(source *Type, target *Type) Ternary</c>
/// （relater.go 的 <c>getBestMatchingType</c>/<c>findMatchingDiscriminantType</c>/<c>compareSignaturesIdentical</c>/
/// <c>compareTypePredicatesIdentical</c> + checker.go 的 <c>compareProperties</c>）。
/// </summary>
internal delegate Ternary TypeRelatedComparer(Type source, Type target);

/// <summary>
/// Go: <c>type RecursionId struct { value any }</c>。Go 的 `==` 对 `any` 里的指针就是身份比较，
/// C# 用 <see cref="ReferenceEquals(object?, object?)"/> 复刻。
/// </summary>
public readonly struct RecursionId : IEquatable<RecursionId>
{
    internal readonly object? _value;

    internal RecursionId(object? value)
    {
        _value = value;
    }

    public bool Equals(RecursionId other)
    {
        return ReferenceEquals(_value, other._value);
    }

    public override bool Equals(object? obj)
    {
        return obj is RecursionId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(_value);
    }

    public static bool operator ==(RecursionId left, RecursionId right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(RecursionId left, RecursionId right)
    {
        return !left.Equals(right);
    }
}

/// <summary>Go: <c>type Relation struct { results map[CacheHashKey]RelationComparisonResult }</c>。</summary>
public sealed class Relation
{
    internal Dictionary<CacheHashKey, RelationComparisonResult>? _results;

    // Go 的 map 读缺失键返回零值（RelationComparisonResultNone）。
    internal RelationComparisonResult Get(CacheHashKey key)
    {
        return _results != null && _results.TryGetValue(key, out var result) ? result : RelationComparisonResult.None;
    }

    internal void Set(CacheHashKey key, RelationComparisonResult result)
    {
        _results ??= new Dictionary<CacheHashKey, RelationComparisonResult>();
        _results[key] = result;
    }

    internal int Size()
    {
        return _results?.Count ?? 0;
    }
}

/// <summary>Go: <c>type errorState struct</c>（值语义，按值保存/恢复）。</summary>
internal readonly struct ErrorState
{
    internal readonly ErrorChain? _errorChain;
    internal readonly Diagnostic[] _relatedInfo;

    internal ErrorState(ErrorChain? errorChain, Diagnostic[] relatedInfo)
    {
        _errorChain = errorChain;
        _relatedInfo = relatedInfo;
    }
}

/// <summary>Go: <c>type ErrorChain struct</c>。</summary>
public sealed class ErrorChain
{
    internal ErrorChain? _next;
    internal Message _message = null!;
    internal object?[] _args = [];
}

/// <summary>
/// Go: <c>type Relater struct</c>。关系判定用的可变状态；Go 用 `freeRelater` 链表池化，C# 同样池化
/// （<see cref="Checker.GetRelater"/>/<see cref="Checker.PutRelater"/>），因此缓存的三个委托只需分配一次。
/// </summary>
public sealed partial class Relater
{
    internal Checker _c = null!;
    internal Relation _relation = null!;
    internal Node? _errorNode;
    internal ErrorChain? _errorChain;
    internal Diagnostic[] _relatedInfo = [];
    internal List<CacheHashKey> _maybeKeys = new();
    internal Set<CacheHashKey> _maybeKeysSet = new();
    internal List<Type> _sourceStack = new();
    internal List<Type> _targetStack = new();
    internal int _maybeCount;
    internal int _sourceDepth;
    internal int _targetDepth;
    internal ExpandingFlags _expandingFlags;
    internal bool _overflow;
    internal int _relationCount;
    internal Relater? _next;

    // Go 的函数值参数（r.reportError / r.isRelatedToSimple / r.isRelatedToWorker）在 C# 里是委托：
    // 逐次创建委托会让最热的路径每次比较都多一次分配，故按实例缓存（Relater 池化 → 全程只分配一次）。
    internal readonly ErrorReporter _reportErrorDelegate;
    internal TypeRelatedComparer _isRelatedToSimpleDelegate;
    internal TypeComparer _isRelatedToWorkerDelegate;

    internal Relater()
    {
        _reportErrorDelegate = ReportError;
        _isRelatedToSimpleDelegate = IsRelatedToSimple;
        _isRelatedToWorkerDelegate = IsRelatedToWorker;
    }

    internal ErrorState GetErrorState()
    {
        return new ErrorState(_errorChain, _relatedInfo);
    }

    internal void RestoreErrorState(ErrorState e)
    {
        _errorChain = e._errorChain;
        _relatedInfo = e._relatedInfo;
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>type RecursionId</c> 的构造（<c>asRecursionId[T *ast.Node | *ast.Symbol | *Type]</c>）。</summary>
    internal static RecursionId AsRecursionId(object? value)
    {
        return new RecursionId(value);
    }

    /// <summary>Go: <c>func (c *Checker) getRelater() *Relater</c>。</summary>
    internal Relater GetRelater()
    {
        var r = _freeRelater;
        if (r == null)
        {
            r = new Relater { _c = this };
        }
        _freeRelater = r._next;
        return r;
    }

    /// <summary>Go: <c>func (c *Checker) putRelater(r *Relater)</c>（`*r = Relater{...}` 的整体重置）。</summary>
    internal void PutRelater(Relater r)
    {
        r._maybeKeysSet.Clear();
        r._c = this;
        r._relation = null!;
        r._errorNode = null;
        r._errorChain = null;
        r._relatedInfo = [];
        r._maybeKeys.Clear();
        r._sourceStack.Clear();
        r._targetStack.Clear();
        r._maybeCount = 0;
        r._sourceDepth = 0;
        r._targetDepth = 0;
        r._expandingFlags = ExpandingFlags.None;
        r._overflow = false;
        r._relationCount = 0;
        r._next = _freeRelater;
        _freeRelater = r;
    }
}
