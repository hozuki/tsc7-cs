// ported from: internal/checker/tracer.go
//
// Tracer records types and trace events during type checking. A nil *Tracer is a valid no-op in Go, so call
// sites can use `if tr := c.tracer; tr != nil` to gate work that only matters under --generateTrace. In C#
// a null receiver cannot be dispatched (docs/architecture.md §3.2), so call sites must test the field first
// (Go 的 `tr.RecordType(x)` → C# 的 `_tracer?.RecordType(x)`）。
//
// 与 Go 的差异（逐条）：
// 1) 本包的 `Tracer` 与 `tracing.Tracer` 同名：C# 里同命名空间的类型优先于 using 导入的名称，所以
//    `Tracer` 就是本类；`tracing.Tracer` 接口另取别名 `TracingTracer`（Go 的 `tracing.Tracer`）。
// 2) `map[string]any` → `Dictionary<string, object?>`；`maps.Copy` → 逐键复制（见 CopyWithCheckerIndex）。
// 3) `func() ` 返回的闭包 → `Action`（Go 的 `Push` 返回 `func()`）。
// 4) `any` → `object?`（`map[string]any` 的键值），与 `Tracing.Push/Instant` 的 C# 签名一致。
// 5) Go 的 `tracing.TracedType` 接口里 `[]TracedType` 返回 nil 表示"没有"，C# 接口签名不可空
//    （`Tracing.Types.cs` 的消费点是 `types.Length > 0`，直接解引用），所以"没有"一律返回空数组
//    （`Array.Empty<TracedType>()`），语义与 Go 的 nil 在这些消费点等价。
// 6) `a.checker.patternForType[a.t]`（Go map 读缺失键返回 nil）→ C# 的 `Dictionary.TryGetValue`
//    （索引器读缺失键会抛异常，docs/architecture.md §3.2 的已知坑）。
// 7) `defer func() { _ = recover() }()`（吞掉 TypeToString 的 panic 并返回零值 ""）→ `try/catch` 返回 ""。
// 8) `a.t.flags`/`a.t.objectFlags`/`a.t.alias`/`a.t.symbol`/`a.t.id`/`a.t.checker` → `_t._flags` 等
//    （Go 未导出字段 → `_camelCase`）；`AsX()` 转换在 C# 里可能返回 null，故用 `!`（分支已由 flags 保证）。
using System;
using System.Collections.Generic;
using TypeScript.Ast;
using TypeScript.Tracing;
using static TypeScript.Checker.Checker;
using TracingTracer = TypeScript.Tracing.Tracer;
using TracingSession = TypeScript.Tracing.Tracing;

namespace TypeScript.Checker;

/// <summary>
/// Go: <c>type Tracer struct</c>。Records types and trace events during type checking. A nil *Tracer
/// is a valid no-op, so call sites can use `if tr := c.tracer; tr != nil` to gate work that only matters
/// under --generateTrace.
/// </summary>
public sealed class Tracer
{
    private readonly TracingSession _tracing;
    private readonly TracingTracer _recorder;
    private readonly int _checkerIndex;

    private Tracer(TracingSession tracing, TracingTracer recorder, int checkerIndex)
    {
        _tracing = tracing;
        _recorder = recorder;
        _checkerIndex = checkerIndex;
    }

    /// <summary>
    /// Go: <c>func NewTracer(tr *tracing.Tracing, checkerIndex int) *Tracer</c>。
    /// Creates a Tracer for the given checker index that records both type-creation events and trace events
    /// through the provided tracing session.
    /// </summary>
    internal static Tracer NewTracer(TracingSession tr, int checkerIndex) => new(tr, tr.NewTypeTracer(checkerIndex), checkerIndex);

    public void RecordType(Type typ)
    {
        _recorder.RecordType(WrapType(typ)!);
    }

    public Action Push(string phase, string name, Dictionary<string, object?>? args, bool separateBeginAndEnd)
    {
        if (!separateBeginAndEnd)
        {
            return _tracing.Push(phase, name, CopyWithCheckerIndex(args), separateBeginAndEnd);
        }

        (args, var restore) = TemporarilyAddCheckerIndex(args);
        var pop = _tracing.Push(phase, name, args, separateBeginAndEnd);
        restore();

        return () =>
        {
            (_, var restoreEndArgs) = TemporarilyAddCheckerIndex(args);
            try
            {
                pop();
            }
            finally
            {
                restoreEndArgs();
            }
        };
    }

    public void Instant(string phase, string name, Dictionary<string, object?>? args)
    {
        _tracing.Instant(phase, name, CopyWithCheckerIndex(args));
    }

    private Dictionary<string, object?> CopyWithCheckerIndex(Dictionary<string, object?>? args)
    {
        var withCheckerIndex = new Dictionary<string, object?>(args is null ? 1 : args.Count + 1);
        if (args is not null)
        {
            foreach (var pair in args)
            {
                withCheckerIndex[pair.Key] = pair.Value;
            }
        }

        withCheckerIndex["checkerId"] = _checkerIndex;
        return withCheckerIndex;
    }

    private (Dictionary<string, object?> Args, Action Restore) TemporarilyAddCheckerIndex(Dictionary<string, object?>? args)
    {
        args ??= new Dictionary<string, object?>();

        var hadPrevious = args.TryGetValue("checkerId", out var previous);
        args["checkerId"] = _checkerIndex;

        return (args, () =>
        {
            if (hadPrevious)
            {
                args["checkerId"] = previous;
            }
            else
            {
                args.Remove("checkerId");
            }
        });
    }
}

/// <summary>Go: <c>type tracedTypeAdapter struct</c>。adapts a Type to the tracing.TracedType interface.</summary>
internal sealed class TracedTypeAdapter : TracedType
{
    private readonly Type _t;
    private readonly Checker? _checker;

    internal TracedTypeAdapter(Type t, Checker? checker)
    {
        _t = t;
        _checker = checker;
    }

    public uint Id() => _t._id.Value;

    public string[] FormatFlags() => FormatTypeFlags(_t._flags);

    public bool IsConditional() => (_t._flags & TypeFlags.Conditional) != 0;

    public Symbol? Symbol() => _t._symbol;

    public Symbol? AliasSymbol()
    {
        if (_t._alias is null)
        {
            return null;
        }

        return _t._alias.Symbol();
    }

    public TracedType[] AliasTypeArguments()
    {
        if (_t._alias is null)
        {
            return Array.Empty<TracedType>();
        }

        return WrapTypes(_t._alias.TypeArguments() ?? []) ?? Array.Empty<TracedType>();
    }

    public string IntrinsicName()
    {
        if ((_t._flags & TypeFlags.Intrinsic) == 0)
        {
            return "";
        }

        var data = _t.AsIntrinsicType();
        if (data is null)
        {
            return "";
        }

        return data.IntrinsicName();
    }

    public TracedType[] UnionTypes()
    {
        if ((_t._flags & TypeFlags.Union) == 0)
        {
            return Array.Empty<TracedType>();
        }

        return WrapTypes(_t.AsUnionType()!._types ?? []) ?? Array.Empty<TracedType>();
    }

    public TracedType[] IntersectionTypes()
    {
        if ((_t._flags & TypeFlags.Intersection) == 0)
        {
            return Array.Empty<TracedType>();
        }

        return WrapTypes(_t.AsIntersectionType()!._types ?? []) ?? Array.Empty<TracedType>();
    }

    public TracedType? IndexType()
    {
        if ((_t._flags & TypeFlags.Index) == 0)
        {
            return null;
        }

        var t = _t.AsIndexType()!._target;
        return t is null ? null : WrapType(t);
    }

    public TracedType? IndexedAccessObjectType()
    {
        if ((_t._flags & TypeFlags.IndexedAccess) == 0)
        {
            return null;
        }

        var t = _t.AsIndexedAccessType()!._objectType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? IndexedAccessIndexType()
    {
        if ((_t._flags & TypeFlags.IndexedAccess) == 0)
        {
            return null;
        }

        var t = _t.AsIndexedAccessType()!._indexType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? ConditionalCheckType()
    {
        if ((_t._flags & TypeFlags.Conditional) == 0)
        {
            return null;
        }

        var t = _t.AsConditionalType()!._checkType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? ConditionalExtendsType()
    {
        if ((_t._flags & TypeFlags.Conditional) == 0)
        {
            return null;
        }

        var t = _t.AsConditionalType()!._extendsType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? ConditionalTrueType()
    {
        if ((_t._flags & TypeFlags.Conditional) == 0)
        {
            return null;
        }

        var t = _t.AsConditionalType()!._resolvedTrueType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? ConditionalFalseType()
    {
        if ((_t._flags & TypeFlags.Conditional) == 0)
        {
            return null;
        }

        var t = _t.AsConditionalType()!._resolvedFalseType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? SubstitutionBaseType()
    {
        if ((_t._flags & TypeFlags.Substitution) == 0)
        {
            return null;
        }

        var t = _t.AsSubstitutionType()!._baseType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? SubstitutionConstraintType()
    {
        if ((_t._flags & TypeFlags.Substitution) == 0)
        {
            return null;
        }

        var t = _t.AsSubstitutionType()!._constraint;
        return t is null ? null : WrapType(t);
    }

    public TracedType? ReferenceTarget()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.Reference) == 0)
        {
            return null;
        }

        var t = _t.AsTypeReference()!._target;
        return t is null ? null : WrapType(t);
    }

    public TracedType[] ReferenceTypeArguments()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.Reference) == 0)
        {
            return Array.Empty<TracedType>();
        }

        return WrapTypes(_t.AsTypeReference()!._resolvedTypeArguments ?? []) ?? Array.Empty<TracedType>();
    }

    public Node? ReferenceNode()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.Reference) == 0)
        {
            return null;
        }

        return _t.AsTypeReference()!._node;
    }

    public TracedType? ReverseMappedSourceType()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.ReverseMapped) == 0)
        {
            return null;
        }

        var t = _t.AsReverseMappedType()!._source;
        return t is null ? null : WrapType(t);
    }

    public TracedType? ReverseMappedMappedType()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.ReverseMapped) == 0)
        {
            return null;
        }

        var t = _t.AsReverseMappedType()!._mappedType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? ReverseMappedConstraintType()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.ReverseMapped) == 0)
        {
            return null;
        }

        var t = _t.AsReverseMappedType()!._constraintType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? EvolvingArrayElementType()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.EvolvingArray) == 0)
        {
            return null;
        }

        var t = _t.AsEvolvingArrayType()!._elementType;
        return t is null ? null : WrapType(t);
    }

    public TracedType? EvolvingArrayFinalType()
    {
        if ((_t._flags & TypeFlags.Object) == 0 || (_t._objectFlags & ObjectFlags.EvolvingArray) == 0)
        {
            return null;
        }

        var t = _t.AsEvolvingArrayType()!._finalArrayType;
        return t is null ? null : WrapType(t);
    }

    public bool IsTuple() => (_t._objectFlags & ObjectFlags.Tuple) != 0;

    public Node? Pattern()
    {
        if (_checker is null)
        {
            return null;
        }

        return _checker._patternForType.TryGetValue(_t, out var pattern) ? pattern : null;
    }

    public object? RecursionIdentity() => GetRecursionIdentity(_t)._value;

    public string Display()
    {
        // Compute display text for types where it's valuable for trace analysis.
        // TypeScript only does this for Anonymous|Literal types, but we extend to
        // unions, intersections, and template literals since they often lack
        // firstDeclaration and the display text helps identify them.
        // Incomplete types during tracing can cause panics, which we intentionally
        // suppress (returning ""), matching TypeScript's try/catch around typeToString.
        if (_checker is null)
        {
            return "";
        }

        if ((_t._objectFlags & ObjectFlags.Anonymous) != 0 ||
            (_t._flags & (TypeFlags.Literal | TypeFlags.TemplateLiteral | TypeFlags.Union | TypeFlags.Intersection)) != 0)
        {
            try
            {
                return _checker.TypeToString(_t);
            }
            catch (Exception)
            {
                return "";
            }
        }

        return "";
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func wrapType(t *Type) tracing.TracedType</c>。</summary>
    internal static TracedType? WrapType(Type? t) => t is null ? null : new TracedTypeAdapter(t, t._checker);

    /// <summary>Go: <c>func wrapTypes(types []*Type) []tracing.TracedType</c>。</summary>
    internal static TracedType[]? WrapTypes(Type[] types)
    {
        if (types.Length == 0)
        {
            return null;
        }

        var result = new TracedType[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            result[i] = WrapType(types[i])!;
        }

        return result;
    }
}
