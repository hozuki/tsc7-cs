// ported from: internal/checker/mapper.go
//
// 与 Go 的差异（C# 语言特性 + docs/checker-plan.md §5.1 的同类裁决，逐条）：
// 1) Go 的三层「`TypeMapper{data TypeMapperData}` 包装 + `TypeMapperData` 接口 + `TypeMapperBase` 默认实现」
//    合并成类层次：`TypeMapper` 上的 `Map`/`Kind`/`MapsThisOnly` 就是 virtual 默认实现（等价于 Go 的
//    `TypeMapperBase` 三方法，也就是 `data` 接口的实现），具体 mapper 直接继承并 override。
//    `TypeMapperData` 这个接口名在 C# 里不出现，`data` 字段与 `m.data = m` 自引用也不需要。
//    `TypeMapperBase` 保留名字（公开 API 对等），落成 `TypeMapper` 的空派生类。
// 2) Go 的工厂返回 `&m.TypeMapper`（内嵌基类字段的指针）；C# 直接返回子类实例（引用即身份）。
// 3) `targets []func() *Type` → `Func<Type>[]`；`slices.Contains(m.sources, t)` → `Array.IndexOf(...) >= 0`
//    （`Type` 不覆写 `Equals`，两者都是引用相等，与 Go 的 `==` 一致）。
// 4) Go 的包级函数（无接收者）→ 同一个 `partial class Checker` 的 `internal static` 成员
//    （docs/checker-plan.md §5.2；Parser/Binder/Printer 先例）。`GetMappedType` 必须是 `internal static`：
//    Go 里 `NodeBuilderImpl`（另一个类型）也调用它（nodecopy.go / nodebuilderimpl.go）。
// 5) `checker-plan §5.2` 的同名冲突：Go 未导出的 `getX` 与导出的 `GetX` 同形 → 未导出者加 `Worker`
//    后缀（artifacts/checker-method-collisions.md）。本文件里 `mapper.go` 无此类冲突项。
using System;
using TypeScript.Ast;
using static TypeScript.Checker.Checker;
using static TypeScript.Core.Core;

namespace TypeScript.Checker;

/// <summary>Go: <c>type TypeMapperKind int32</c>（成员名去掉 <c>TypeMapperKind</c> 前缀，数值与 Go 一致）。</summary>
public enum TypeMapperKind
{
    /// <summary>Go: <c>TypeMapperKindUnknown</c>。</summary>
    Unknown = 0,

    /// <summary>Go: <c>TypeMapperKindSimple</c>。</summary>
    Simple = 1,

    /// <summary>Go: <c>TypeMapperKindArray</c>。</summary>
    Array = 2,

    /// <summary>Go: <c>TypeMapperKindMerged</c>。</summary>
    Merged = 3,
}

/// <summary>Go: <c>type TypeMapper struct</c>（与 <c>TypeMapperData</c> 合并，见文件头第 1 条）。</summary>
public class TypeMapper
{
    /// <summary>Go: <c>func (m *TypeMapper) Map(t *Type) *Type</c>（默认实现 = Go 的 <c>TypeMapperBase.Map</c>）。</summary>
    public virtual Type Map(Type t) => t;

    /// <summary>Go: <c>func (m *TypeMapper) Kind() TypeMapperKind</c>（默认 = <c>TypeMapperKindUnknown</c>）。</summary>
    public virtual TypeMapperKind Kind() => TypeMapperKind.Unknown;

    /// <summary>Go: <c>func (m *TypeMapper) MapsThisOnly() bool</c>（默认 false）。</summary>
    public virtual bool MapsThisOnly() => false;
}

/// <summary>Go: <c>type TypeMapperBase struct { TypeMapper }</c>（C# 里默认实现就在基类上，本类型只是保持名字，见文件头第 1 条）。</summary>
public class TypeMapperBase : TypeMapper
{
}

/// <summary>Go: <c>type SimpleTypeMapper struct</c>。</summary>
public sealed class SimpleTypeMapper : TypeMapperBase
{
    internal readonly Type _source;
    internal readonly Type _target;

    internal SimpleTypeMapper(Type source, Type target)
    {
        _source = source;
        _target = target;
    }

    public override Type Map(Type t) => t == _source ? _target : t;

    public override TypeMapperKind Kind() => TypeMapperKind.Simple;

    public override bool MapsThisOnly() => IsThisTypeParameter(_source);
}

/// <summary>Go: <c>type ArrayTypeMapper struct</c>。</summary>
public sealed class ArrayTypeMapper : TypeMapperBase
{
    internal readonly Type[] _sources;
    internal readonly Type[] _targets;

    internal ArrayTypeMapper(Type[] sources, Type[] targets)
    {
        _sources = sources;
        _targets = targets;
    }

    public override Type Map(Type t)
    {
        for (var i = 0; i < _sources.Length; i++)
        {
            if (t == _sources[i])
            {
                return _targets[i];
            }
        }

        return t;
    }

    public override TypeMapperKind Kind() => TypeMapperKind.Array;

    public override bool MapsThisOnly() => _sources.Length == 1 && IsThisTypeParameter(_sources[0]);
}

/// <summary>Go: <c>type ArrayToSingleTypeMapper struct</c>。</summary>
public sealed class ArrayToSingleTypeMapper : TypeMapperBase
{
    internal readonly Type[] _sources;
    internal readonly Type _target;

    internal ArrayToSingleTypeMapper(Type[] sources, Type target)
    {
        _sources = sources;
        _target = target;
    }

    public override Type Map(Type t) => Array.IndexOf(_sources, t) >= 0 ? _target : t;

    public override bool MapsThisOnly() => _sources.Length == 1 && IsThisTypeParameter(_sources[0]);
}

/// <summary>Go: <c>type DeferredTypeMapper struct</c>。</summary>
public sealed class DeferredTypeMapper : TypeMapperBase
{
    internal readonly Type[] _sources;
    internal readonly Func<Type>[] _targets;

    internal DeferredTypeMapper(Type[] sources, Func<Type>[] targets)
    {
        _sources = sources;
        _targets = targets;
    }

    public override Type Map(Type t)
    {
        for (var i = 0; i < _sources.Length; i++)
        {
            if (t == _sources[i])
            {
                return _targets[i]();
            }
        }

        return t;
    }

    public override bool MapsThisOnly() => _sources.Length == 1 && IsThisTypeParameter(_sources[0]);
}

/// <summary>Go: <c>type FunctionTypeMapper struct</c>。</summary>
public sealed class FunctionTypeMapper : TypeMapperBase
{
    internal readonly Func<Type, Type> _fn;

    internal FunctionTypeMapper(Func<Type, Type> fn)
    {
        _fn = fn;
    }

    public override Type Map(Type t) => _fn(t);
}

/// <summary>Go: <c>type MergedTypeMapper struct</c>。</summary>
public sealed class MergedTypeMapper : TypeMapperBase
{
    internal readonly TypeMapper _m1;
    internal readonly TypeMapper _m2;

    internal MergedTypeMapper(TypeMapper m1, TypeMapper m2)
    {
        _m1 = m1;
        _m2 = m2;
    }

    public override Type Map(Type t) => _m2.Map(_m1.Map(t));

    public override TypeMapperKind Kind() => TypeMapperKind.Merged;
}

/// <summary>Go: <c>type CompositeTypeMapper struct</c>。</summary>
public sealed class CompositeTypeMapper : TypeMapperBase
{
    internal readonly Checker _c;
    internal readonly TypeMapper _m1;
    internal readonly TypeMapper _m2;

    internal CompositeTypeMapper(Checker c, TypeMapper m1, TypeMapper m2)
    {
        _c = c;
        _m1 = m1;
        _m2 = m2;
    }

    public override Type Map(Type t)
    {
        var t1 = _m1.Map(t);
        return t1 != t ? _c.InstantiateType(t1, _m2) : _m2.Map(t);
    }
}

/// <summary>Go: <c>type InferenceTypeMapper struct</c>。</summary>
public sealed class InferenceTypeMapper : TypeMapperBase
{
    internal readonly Checker _c;
    internal readonly InferenceContext _n;
    internal readonly bool _fixing;

    internal InferenceTypeMapper(Checker c, InferenceContext n, bool fixing)
    {
        _c = c;
        _n = n;
        _fixing = fixing;
    }

    public override Type Map(Type t)
    {
        for (var i = 0; i < _n._inferences.Count; i++)
        {
            var inference = _n._inferences[i];
            if (t == inference._typeParameter)
            {
                if (_fixing && !inference._isFixed)
                {
                    // Before we commit to a particular inference (and thus lock out any further inferences),
                    // we infer from any intra-expression inference sites we have collected.
                    _c.InferFromIntraExpressionSites(_n);
                    ClearCachedInferences(_n._inferences.ToArray());
                    inference._isFixed = true;
                }

                return _c.GetInferredType(_n, i);
            }
        }

        return t;
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func getMappedType(t *Type, mapper *TypeMapper) *Type</c>。</summary>
    internal static Type GetMappedType(Type t, TypeMapper mapper) => mapper.Map(GetNonDistributedTypeParameter(t));

    /// <summary>Go: <c>func newTypeMapper(sources []*Type, targets []*Type) *TypeMapper</c>。</summary>
    internal static TypeMapper NewTypeMapper(Type[] sources, Type[] targets) => sources.Length == 1
        ? NewSimpleTypeMapper(sources[0], targets[0])
        : NewArrayTypeMapper(sources, targets);

    /// <summary>Go: <c>func newSimpleTypeMapper(source *Type, target *Type) *TypeMapper</c>。</summary>
    internal static TypeMapper NewSimpleTypeMapper(Type source, Type target) => new SimpleTypeMapper(source, target);

    /// <summary>Go: <c>func newArrayTypeMapper(sources []*Type, targets []*Type) *TypeMapper</c>。</summary>
    internal static TypeMapper NewArrayTypeMapper(Type[] sources, Type[] targets) => new ArrayTypeMapper(sources, targets);

    /// <summary>Go: <c>func newArrayToSingleTypeMapper(sources []*Type, target *Type) *TypeMapper</c>。</summary>
    internal static TypeMapper NewArrayToSingleTypeMapper(Type[] sources, Type target) => new ArrayToSingleTypeMapper(sources, target);

    /// <summary>Go: <c>func newDeferredTypeMapper(sources []*Type, targets []func() *Type) *TypeMapper</c>。</summary>
    internal static TypeMapper NewDeferredTypeMapper(Type[] sources, Func<Type>[] targets) => new DeferredTypeMapper(sources, targets);

    /// <summary>Go: <c>func newFunctionTypeMapper(fn func(*Type) *Type) *TypeMapper</c>。</summary>
    internal static TypeMapper NewFunctionTypeMapper(Func<Type, Type> fn) => new FunctionTypeMapper(fn);

    /// <summary>Go: <c>func newMergedTypeMapper(m1 *TypeMapper, m2 *TypeMapper) *TypeMapper</c>。</summary>
    internal static TypeMapper NewMergedTypeMapper(TypeMapper m1, TypeMapper m2) => new MergedTypeMapper(m1, m2);

    /// <summary>Go: <c>func newCompositeTypeMapper(c *Checker, m1 *TypeMapper, m2 *TypeMapper) *TypeMapper</c>。</summary>
    internal static TypeMapper NewCompositeTypeMapper(Checker c, TypeMapper m1, TypeMapper m2) => new CompositeTypeMapper(c, m1, m2);

    /// <summary>Go: <c>func mergeTypeMappers(m1 *TypeMapper, m2 *TypeMapper) *TypeMapper</c>。</summary>
    internal static TypeMapper? MergeTypeMappers(TypeMapper? m1, TypeMapper? m2) => m1 is not null ? NewMergedTypeMapper(m1, m2!) : m2;

    /// <summary>Go: <c>func prependTypeMapping(source *Type, target *Type, mapper *TypeMapper) *TypeMapper</c>。</summary>
    internal static TypeMapper PrependTypeMapping(Type source, Type target, TypeMapper? mapper) => mapper is null
        ? NewSimpleTypeMapper(GetNonDistributedTypeParameter(source), target)
        : NewMergedTypeMapper(NewSimpleTypeMapper(GetNonDistributedTypeParameter(source), target), mapper);

    /// <summary>Go: <c>func appendTypeMapping(mapper *TypeMapper, source *Type, target *Type) *TypeMapper</c>。</summary>
    internal static TypeMapper AppendTypeMapping(TypeMapper? mapper, Type source, Type target) => mapper is null
        ? NewSimpleTypeMapper(GetNonDistributedTypeParameter(source), target)
        : NewMergedTypeMapper(mapper, NewSimpleTypeMapper(GetNonDistributedTypeParameter(source), target));

    /// <summary>Go: <c>func (c *Checker) combineTypeMappers(m1 *TypeMapper, m2 *TypeMapper) *TypeMapper</c>。</summary>
    internal TypeMapper CombineTypeMappers(TypeMapper? m1, TypeMapper m2) => m1 is not null ? NewCompositeTypeMapper(this, m1, m2) : m2;

    /// <summary>Go: <c>func (c *Checker) mapTypeWithCompositeMapper(t *Type, m1 *TypeMapper, m2 *TypeMapper) *Type</c>。</summary>
    internal Type MapTypeWithCompositeMapper(Type t, TypeMapper? m1, TypeMapper m2)
    {
        if (m1 is null)
        {
            return GetMappedType(t, m2);
        }

        var t1 = GetMappedType(t, m1);
        return t1 != t ? InstantiateType(t1, m2) : GetMappedType(t, m2);
    }

    /// <summary>
    /// Go: <c>func (c *Checker) newBackreferenceMapper(context *InferenceContext, index int) *TypeMapper</c>。
    /// Maps forward-references to later types parameters to the empty object type.
    /// This is used during inference when instantiating type parameter defaults.
    /// </summary>
    internal TypeMapper NewBackreferenceMapper(InferenceContext context, int index)
    {
        // Go 的 `context.inferences[index:]` 是切片视图，但紧随其后的 core.Map 会新建数组，所以 C# 用
        // `GetRange(...).ToArray()`（复制一份）在语义上等价（`_inferences` 在 C# 里是 List）。
        var forwardInferences = context._inferences.GetRange(index, context._inferences.Count - index).ToArray();
        var typeParameters = Map(forwardInferences, i => i._typeParameter!);
        return NewArrayToSingleTypeMapper(typeParameters, _unknownType!);
    }

    /// <summary>Go: <c>func (c *Checker) newInferenceTypeMapper(n *InferenceContext, fixing bool) *TypeMapper</c>。</summary>
    internal TypeMapper NewInferenceTypeMapper(InferenceContext n, bool fixing) => new InferenceTypeMapper(this, n, fixing);
}
