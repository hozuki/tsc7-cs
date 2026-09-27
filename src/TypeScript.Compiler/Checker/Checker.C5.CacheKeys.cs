// ported from: internal/checker/checker.go:17695-18000（CacheHashKey、keyBuilder、12 个包级 key 函数、
//   isTypeReferenceWithGenericArguments / isNonDeferredTypeReference / isUnconstrainedTypeParameter）
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
// 1) `type CacheHashKey xxh3.Uint128` → `public readonly struct CacheHashKey`（全仓共用形状，逐字保持）；
//    `xxh3.Uint128` → `System.UInt128`；`xxh3.Hash128`/`HashString128` → `Xxh3.Xxh3.Hash128`/`HashString128`
//    （命名空间 `TypeScript.Xxh3`，先例 `ContentMapper/ContentMapper.cs`、`ContentMapper/HostImpl.cs`）。
// 2) 未导出类型 `keyBuilder` → `internal sealed class KeyBuilder`；未导出字段 → `_camelCase`（§5.5）。
//    该类型**跨片共享**：flow.go:1658/1665（`getFlowReferenceKey`/`writeFlowCacheKey`）与 checker.go:22484
//    （`instantiateTypeWithAlias`）也用它，成员名/签名按 Go 的 PascalCase 固定（Hash/Spill/Write*）。
// 3) Go 的 `overflowBuffer []byte` + `append` → `byte[]? _overflowBuffer`（容量数组）+ `internal int _overflowLength;`
//    （逻辑长度，即 Go 的 `len(overflowBuffer)`）。Go 的 append 语义是「逻辑长度增长、容量不足时倍增重分配」，
//    这里用 AppendOverflow/EnsureOverflowCapacity 复刻（摊销 O(1)、不改写已有字节、不做临时分配）。
//    hash() 里的 `append(b.overflowBuffer, b.inlineBuffer[:b.inlineLength]...)` 只把 inline 字节写进**容量区**、
//    不改逻辑长度（Go 也没把 append 结果赋回 b.overflowBuffer），这里照抄（见 Hash）。
// 4) Go 的 string 是 UTF-8 字节序列：`len(s)` → `Encoding.UTF8.GetByteCount(s)`、`copy(dst, s)` →
//    `Encoding.UTF8.GetBytes(s, dst)`（docs/architecture.md §3.1/§3.4）。
// 5) `binary.LittleEndian.PutUint32/PutUint64` → `BinaryPrimitives.WriteUInt32LittleEndian/WriteUInt64LittleEndian`；
//    `uint64(可能为负的 int)` → `unchecked((ulong)value)`（与 Go 的补码回绕一致）。
// 6) 包级函数 → `partial class Checker` 的 `internal static` 成员、PascalCase（§5.2/§5.5）；`getRelationKey` 返回
//    `(CacheHashKey, bool)` → `TryGetRelationKey(..., out CacheHashKey key)`（§5.4）。这些包级名与实例方法无冲突
//    （已核 artifacts/checker-method-collisions.md），故不加 `Worker` 后缀。
// 7) Go 参数名 `readonly` 是 C# 关键字 → `isReadonly`；闭包参数 `ref` → `@ref`；
//    `isTypeReferenceWithGenericArguments` 里 Go 用同名 `t` 遮蔽外层参数（C# 会 CS0136）→ 内层改名 `typeArg`。
// 8) `panic("Unhandled case in getUnionKey")` → `Debug.Fail(...)`（`TypeScript.Debug.Fail`，会抛异常；本文件
//    **不用** `using System.Diagnostics;`）。
// 9) `writeByte(c byte)` 的参数就是 `byte`：调用点要写 `WriteByte((byte)':')`（char 字面量不能隐式转 byte）。
namespace TypeScript.Checker;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using TypeScript.Ast;
using static TypeScript.Ast.Ast;
using static TypeScript.Core.Core;

/// <summary>Go: <c>type CacheHashKey xxh3.Uint128</c>（checker.go:17695）。</summary>
public readonly struct CacheHashKey : IEquatable<CacheHashKey>
{
    internal readonly UInt128 _value;                 // Go: type CacheHashKey xxh3.Uint128（xxh3.Uint128 → System.UInt128）

    public CacheHashKey(UInt128 value) => _value = value;

    public bool IsZero() => _value == default;        // Go: (k CacheHashKey) IsZero()

    public bool Equals(CacheHashKey other) => _value == other._value;

    public override bool Equals(object? obj) => obj is CacheHashKey k && Equals(k);

    public override int GetHashCode() => _value.GetHashCode();

    public static implicit operator UInt128(CacheHashKey key) => key._value;    // Go: xxh3.Uint128(k)

    public static explicit operator CacheHashKey(UInt128 value) => new(value);  // Go: CacheHashKey(x)
}

/// <summary>Go: <c>type keyBuilder struct</c>（checker.go:17701）。跨片共享（flow.go/checker.go 也用）。</summary>
internal sealed class KeyBuilder
{
    internal int _inlineLength;                     // Go: inlineLength
    internal byte[]? _overflowBuffer;               // Go: overflowBuffer（容量数组，逻辑长度为 _overflowLength）
    internal int _overflowLength;                   // Go: len(overflowBuffer)
    internal byte[] _inlineBuffer = new byte[192];  // Go: inlineBuffer [192]byte

    /// <summary>Go: <c>func (b *keyBuilder) hash() CacheHashKey</c>。</summary>
    internal CacheHashKey Hash()
    {
        if (_overflowBuffer == null)
        {
            return new CacheHashKey(Xxh3.Xxh3.Hash128(_inlineBuffer.AsSpan(0, _inlineLength)));
        }

        // Go: xxh3.Hash128(append(b.overflowBuffer, b.inlineBuffer[:b.inlineLength]...))
        // 照抄 append 的语义：只把 inline 字节写进容量区尾部，**不**改 overflowBuffer 的逻辑长度。
        EnsureOverflowCapacity(_overflowLength + _inlineLength);
        var buffer = _overflowBuffer!;
        _inlineBuffer.AsSpan(0, _inlineLength).CopyTo(buffer.AsSpan(_overflowLength));
        return new CacheHashKey(Xxh3.Xxh3.Hash128(buffer.AsSpan(0, _overflowLength + _inlineLength)));
    }

    /// <summary>
    /// spill moves the buffered bytes onto the end of overflowBuffer, so the key's byte
    /// stream stays overflowBuffer followed by inlineBuffer.
    /// Go: <c>func (b *keyBuilder) spill()</c>。
    /// </summary>
    internal void Spill()
    {
        AppendOverflow(_inlineBuffer.AsSpan(0, _inlineLength));
        _inlineLength = 0;
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeByte(c byte)</c>。</summary>
    internal void WriteByte(byte c)
    {
        if (_inlineLength == _inlineBuffer.Length)
        {
            Spill();
        }
        _inlineBuffer[_inlineLength] = c;
        _inlineLength++;
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeString(s string)</c>。</summary>
    internal void WriteString(string s)
    {
        // Go 的 len(s) 是 UTF-8 字节数（Go 的 string 是字节序列）。
        var byteCount = Encoding.UTF8.GetByteCount(s);
        if (_inlineLength + byteCount > _inlineBuffer.Length)
        {
            Spill();
            if (byteCount > _inlineBuffer.Length)
            {
                // Go: b.overflowBuffer = append(b.overflowBuffer, s...)
                AppendOverflowString(s, byteCount);
                return;
            }
        }
        // Go: b.inlineLength += copy(b.inlineBuffer[b.inlineLength:], s)
        _inlineLength += Encoding.UTF8.GetBytes(s, _inlineBuffer.AsSpan(_inlineLength));
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeUint32(v uint32)</c>。</summary>
    internal void WriteUint32(uint v)
    {
        if (_inlineLength + 4 > _inlineBuffer.Length)
        {
            Spill();
        }
        BinaryPrimitives.WriteUInt32LittleEndian(_inlineBuffer.AsSpan(_inlineLength), v);
        _inlineLength += 4;
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeUint64(v uint64)</c>。</summary>
    internal void WriteUint64(ulong v)
    {
        if (_inlineLength + 8 > _inlineBuffer.Length)
        {
            Spill();
        }
        BinaryPrimitives.WriteUInt64LittleEndian(_inlineBuffer.AsSpan(_inlineLength), v);
        _inlineLength += 8;
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeInt(value int)</c>。</summary>
    internal void WriteInt(int value)
    {
        WriteUint64(unchecked((ulong)value));
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeSymbol(s *ast.Symbol)</c>。</summary>
    internal void WriteSymbol(Symbol s)
    {
        WriteUint64((ulong)GetSymbolId(s));
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeType(t *Type)</c>。</summary>
    internal void WriteType(Type t)
    {
        WriteUint32((uint)t._id);
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeTypes(types []*Type)</c>。</summary>
    internal void WriteTypes(Type[]? types)
    {
        WriteInt(types?.Length ?? 0);
        if (types != null)
        {
            foreach (var t in types)
            {
                WriteType(t);
            }
        }
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeAlias(alias *TypeAlias)</c>。</summary>
    internal void WriteAlias(TypeAlias? alias)
    {
        if (alias != null)
        {
            WriteByte(1);
            WriteSymbol(alias._symbol!);
            WriteTypes(alias._typeArguments);
        }
        else
        {
            WriteByte(0);
        }
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeGenericTypeReferences(...) bool</c>。</summary>
    internal bool WriteGenericTypeReferences(Type source, Type target, bool ignoreConstraints)
    {
        var constrained = false;
        var typeParameters = new List<Type>(8); // Go: make([]*Type, 0, 8)
        void WriteTypeReference(Type @ref, int depth)
        {
            WriteType(@ref.Target()!);
            var typeArguments = @ref.AsTypeReference()!._resolvedTypeArguments;
            if (typeArguments != null)
            {
                foreach (var t in typeArguments)
                {
                    if ((t._flags & TypeFlags.TypeParameter) != 0)
                    {
                        if (ignoreConstraints || t._checker!.GetConstraintOfTypeParameterWorker(t) == null)
                        {
                            var index = typeParameters.FindIndex(x => ReferenceEquals(x, t));
                            if (index < 0)
                            {
                                index = typeParameters.Count;
                                typeParameters.Add(t);
                            }
                            WriteByte((byte)'=');
                            WriteInt(index);
                            continue;
                        }
                        constrained = true;
                    }
                    else if (depth < 4 && Checker.IsTypeReferenceWithGenericArguments(t))
                    {
                        WriteByte((byte)'<');
                        WriteTypeReference(t, depth + 1);
                        WriteByte((byte)'>');
                        continue;
                    }
                    WriteByte((byte)'-');
                    WriteType(t);
                }
            }
        }
        WriteTypeReference(source, 0);
        WriteByte((byte)',');
        WriteTypeReference(target, 0);
        return constrained;
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeNodeId(id ast.NodeId)</c>。</summary>
    internal void WriteNodeId(NodeId id)
    {
        WriteUint64((ulong)id);
    }

    /// <summary>Go: <c>func (b *keyBuilder) writeNode(node *ast.Node)</c>。</summary>
    internal void WriteNode(Node? node)
    {
        if (node != null)
        {
            WriteNodeId(GetNodeId(node));
        }
    }

    /// <summary>Go 的 <c>append(b.overflowBuffer, bytes...)</c>：把 bytes 追加到 overflowBuffer 的逻辑末尾。</summary>
    private void AppendOverflow(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return; // Go: append(s, nothing...) 返回原 slice（nil 仍是 nil）
        }
        EnsureOverflowCapacity(_overflowLength + bytes.Length);
        var buffer = _overflowBuffer!;
        bytes.CopyTo(buffer.AsSpan(_overflowLength));
        _overflowLength += bytes.Length;
    }

    /// <summary>Go 的 <c>append(s, s2...)</c>：把字符串按 UTF-8 字节追加到 overflowBuffer。</summary>
    private void AppendOverflowString(string s, int byteCount)
    {
        byte[]? rented = byteCount > 256 ? ArrayPool<byte>.Shared.Rent(byteCount) : null;
        Span<byte> buffer = rented != null ? rented.AsSpan(0, byteCount) : stackalloc byte[256];
        var written = Encoding.UTF8.GetBytes(s, buffer);
        AppendOverflow(buffer[..written]);
        if (rented != null)
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Go 的 <c>append</c> 扩容：容量不足时倍增重分配（只搬 _overflowLength 个有效字节）。
    /// </summary>
    private void EnsureOverflowCapacity(int needed)
    {
        var capacity = _overflowBuffer?.Length ?? 0;
        if (capacity >= needed)
        {
            return;
        }
        var newCapacity = Math.Max(needed, Math.Max(16, capacity * 2));
        var grown = new byte[newCapacity];
        if (_overflowLength > 0)
        {
            _overflowBuffer.AsSpan(0, _overflowLength).CopyTo(grown);
        }
        _overflowBuffer = grown;
    }
}

public sealed partial class Checker
{
    /// <summary>Go: <c>func getTypeListKey(types []*Type) CacheHashKey</c>（checker.go:17830）。</summary>
    internal static CacheHashKey GetTypeListKey(Type[]? types)
    {
        var b = new KeyBuilder();
        b.WriteTypes(types);
        return b.Hash();
    }

    /// <summary>Go: <c>func getAliasKey(alias *TypeAlias) CacheHashKey</c>（checker.go:17836）。</summary>
    internal static CacheHashKey GetAliasKey(TypeAlias? alias)
    {
        var b = new KeyBuilder();
        b.WriteAlias(alias);
        return b.Hash();
    }

    /// <summary>Go: <c>func getUnionKey(types []*Type, origin *Type, alias *TypeAlias) CacheHashKey</c>（checker.go:17842）。</summary>
    internal static CacheHashKey GetUnionKey(Type[]? types, Type? origin, TypeAlias? alias)
    {
        var b = new KeyBuilder();
        if (origin == null)
        {
            b.WriteTypes(types);
        }
        else if ((origin._flags & TypeFlags.Union) != 0)
        {
            b.WriteByte((byte)'|');
            b.WriteTypes(origin.Types());
        }
        else if ((origin._flags & TypeFlags.Intersection) != 0)
        {
            b.WriteByte((byte)'&');
            b.WriteTypes(origin.Types());
        }
        else if ((origin._flags & TypeFlags.Index) != 0)
        {
            // origin type id alone is insufficient, as `keyof x` may resolve to multiple WIP values while `x` is still resolving
            b.WriteByte((byte)'#');
            b.WriteType(origin);
            b.WriteByte((byte)'|');
            b.WriteTypes(types);
        }
        else
        {
            Debug.Fail("Unhandled case in getUnionKey");
        }
        b.WriteAlias(alias);
        return b.Hash();
    }

    /// <summary>Go: <c>func getIntersectionKey(types []*Type, flags IntersectionFlags, alias *TypeAlias) CacheHashKey</c>（checker.go:17866）。</summary>
    internal static CacheHashKey GetIntersectionKey(Type[]? types, IntersectionFlags flags, TypeAlias? alias)
    {
        var b = new KeyBuilder();
        b.WriteTypes(types);
        if ((flags & IntersectionFlags.NoConstraintReduction) == 0)
        {
            b.WriteAlias(alias);
        }
        else
        {
            b.WriteByte((byte)'*');
        }
        return b.Hash();
    }

    /// <summary>Go: <c>func getTupleKey(elementInfos []TupleElementInfo, readonly bool) CacheHashKey</c>（checker.go:17877）。</summary>
    internal static CacheHashKey GetTupleKey(TupleElementInfo[]? elementInfos, bool isReadonly)
    {
        var b = new KeyBuilder();
        if (elementInfos != null)
        {
            foreach (var e in elementInfos)
            {
                if ((e._flags & ElementFlags.Required) != 0)
                {
                    b.WriteByte((byte)'#');
                }
                else if ((e._flags & ElementFlags.Optional) != 0)
                {
                    b.WriteByte((byte)'?');
                }
                else if ((e._flags & ElementFlags.Rest) != 0)
                {
                    b.WriteByte((byte)'.');
                }
                else
                {
                    b.WriteByte((byte)'*');
                }
                if (e._labeledDeclaration != null)
                {
                    b.WriteNode(e._labeledDeclaration);
                }
            }
        }
        if (isReadonly)
        {
            b.WriteByte((byte)'!');
        }
        return b.Hash();
    }

    /// <summary>Go: <c>func getTypeAliasInstantiationKey(typeArguments []*Type, alias *TypeAlias) CacheHashKey</c>（checker.go:17900）。</summary>
    internal static CacheHashKey GetTypeAliasInstantiationKey(Type[]? typeArguments, TypeAlias? alias)
    {
        return GetTypeInstantiationKey(typeArguments, alias, false);
    }

    /// <summary>Go: <c>func getTypeInstantiationKey(typeArguments []*Type, alias *TypeAlias, singleSignature bool) CacheHashKey</c>（checker.go:17904）。</summary>
    internal static CacheHashKey GetTypeInstantiationKey(Type[]? typeArguments, TypeAlias? alias, bool singleSignature)
    {
        var b = new KeyBuilder();
        b.WriteTypes(typeArguments);
        b.WriteAlias(alias);
        if (singleSignature)
        {
            b.WriteByte((byte)'!');
        }
        return b.Hash();
    }

    /// <summary>Go: <c>func getIndexedAccessKey(objectType *Type, indexType *Type, accessFlags AccessFlags, alias *TypeAlias) CacheHashKey</c>（checker.go:17914）。</summary>
    internal static CacheHashKey GetIndexedAccessKey(Type objectType, Type indexType, AccessFlags accessFlags, TypeAlias? alias)
    {
        var b = new KeyBuilder();
        b.WriteType(objectType);
        b.WriteType(indexType);
        b.WriteUint32((uint)accessFlags);
        b.WriteAlias(alias);
        return b.Hash();
    }

    /// <summary>Go: <c>func getTemplateTypeKey(texts []string, types []*Type) CacheHashKey</c>（checker.go:17923）。</summary>
    internal static CacheHashKey GetTemplateTypeKey(string[]? texts, Type[]? types)
    {
        var b = new KeyBuilder();
        b.WriteTypes(types);
        b.WriteByte((byte)'|');
        if (texts != null)
        {
            foreach (var s in texts)
            {
                // Go 的 len(s) 是 UTF-8 字节数
                b.WriteInt(Encoding.UTF8.GetByteCount(s));
            }
        }
        b.WriteByte((byte)'|');
        if (texts != null)
        {
            foreach (var s in texts)
            {
                b.WriteString(s);
            }
        }
        return b.Hash();
    }

    /// <summary>Go: <c>func getConditionalTypeKey(typeArguments []*Type, alias *TypeAlias, forConstraint bool) CacheHashKey</c>（checker.go:17937）。</summary>
    internal static CacheHashKey GetConditionalTypeKey(Type[]? typeArguments, TypeAlias? alias, bool forConstraint)
    {
        var b = new KeyBuilder();
        b.WriteTypes(typeArguments);
        b.WriteAlias(alias);
        if (forConstraint)
        {
            b.WriteByte((byte)'!');
        }
        return b.Hash();
    }

    /// <summary>Go: <c>func getRelationKey(source *Type, target *Type, intersectionState IntersectionState, isIdentity bool, ignoreConstraints bool) (CacheHashKey, bool)</c>（checker.go:17947）。</summary>
    internal static bool TryGetRelationKey(Type source, Type target, IntersectionState intersectionState, bool isIdentity, bool ignoreConstraints, out CacheHashKey key)
    {
        if (isIdentity && (uint)source._id > (uint)target._id)
        {
            (source, target) = (target, source);
        }
        var b = new KeyBuilder();
        var constrained = false;
        if (IsTypeReferenceWithGenericArguments(source) && IsTypeReferenceWithGenericArguments(target))
        {
            b.WriteByte((byte)'g');
            constrained = b.WriteGenericTypeReferences(source, target, ignoreConstraints);
        }
        else
        {
            b.WriteByte((byte)'s');
            b.WriteType(source);
            b.WriteType(target);
        }
        b.WriteUint32((uint)intersectionState);
        key = b.Hash();
        return constrained;
    }

    /// <summary>Go: <c>func getNodeListKey(nodes []*ast.Node) CacheHashKey</c>（checker.go:17965）。</summary>
    internal static CacheHashKey GetNodeListKey(Node[]? nodes)
    {
        var b = new KeyBuilder();
        b.WriteInt(nodes?.Length ?? 0);
        if (nodes != null)
        {
            foreach (var n in nodes)
            {
                b.WriteNode(n);
            }
        }
        return b.Hash();
    }

    /// <summary>Go: <c>func isTypeReferenceWithGenericArguments(t *Type) bool</c>（checker.go:17974）。</summary>
    internal static bool IsTypeReferenceWithGenericArguments(Type t)
    {
        return IsNonDeferredTypeReference(t) && Some(t._checker!.GetTypeArgumentsWorker(t), typeArg =>
            (typeArg._flags & TypeFlags.TypeParameter) != 0 || IsTypeReferenceWithGenericArguments(typeArg));
    }

    /// <summary>Go: <c>func isNonDeferredTypeReference(t *Type) bool</c>（checker.go:17980）。</summary>
    internal static bool IsNonDeferredTypeReference(Type t)
    {
        return (t._objectFlags & ObjectFlags.Reference) != 0 && t.AsTypeReference()!._node == null;
    }

    /// <summary>
    /// Return true if type parameter originates in an unconstrained declaration in a type parameter list
    /// Go: <c>func isUnconstrainedTypeParameter(tp *Type) bool</c>（checker.go:17985）。
    /// </summary>
    internal static bool IsUnconstrainedTypeParameter(Type tp)
    {
        var target = tp.Target();
        if (target == null)
        {
            target = tp;
        }
        if (target._symbol == null)
        {
            return false;
        }
        var declarations = target._symbol.Declarations;
        if (declarations != null)
        {
            foreach (var d in declarations)
            {
                if (IsTypeParameterDeclaration(d) && (d.AsTypeParameterDeclaration()!.Constraint != null || IsMappedTypeNode(d.Parent!) || IsInferTypeNode(d.Parent!)))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
