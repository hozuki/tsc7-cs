// ported from: internal/tracing/tracing.go:534-763（TypeDescriptor / Location / LineAndChar 的 JSON 形状，
//   typeTracer.buildTypeDescriptor / mapTypeIds / getLocation）
//
// 与 Go 的差异（与 Tracing.cs 文件头同源，另加）：
// - Go 的结构体 tag（`json:"…,omitzero"`）在 C# 侧必须显式实现 `IJsonMarshaler`（docs/json.md §2）：本文件按 Go
//   的字段声明顺序写，`omitzero` 用"值非零才写"复刻（*T → `T?`，null 不写；string 空不写；slice 为 nil 不写）。
//   `flags` 在 Go 里没有 omitzero（永远写），nil 切片在这里也照写 `null`。
// - Go 的 `*uint32` / `*int32` → `uint?` / `int?`（`conditionalTrueType`/`conditionalFalseType` 用 -1 表示未解析的
//   分支，所以必须是有符号的可空类型）。
// - `recursionIdentityMap map[any]int` → `Dictionary<object, int>`（Go 的接口键按动态值的相等性，C# 的默认比较器
//   对未覆写 Equals 的引用类型是引用相等，语义一致）。
namespace TypeScript.Tracing;

using System.Globalization;
using TypeScript.Ast;
using TypeScript.Json;
using TypeScript.TSPath;
using static TypeScript.Ast.Ast;
using static TypeScript.Json.Json;
using static TypeScript.TSPath.TSPath;

/// <summary>Represents a type in the output JSON.</summary>
public sealed class TypeDescriptor : IJsonMarshaler
{
    public uint ID;
    public string IntrinsicName = "";
    public string SymbolName = "";
    public int? RecursionID;
    public bool IsTuple;
    public uint[]? UnionTypes;
    public uint[]? IntersectionTypes;
    public uint[]? AliasTypeArguments;
    public uint? KeyofType;
    public uint? IndexedAccessObjectType;
    public uint? IndexedAccessIndexType;
    public uint? ConditionalCheckType;
    public uint? ConditionalExtendsType;

    // ConditionalTrueType and ConditionalFalseType are int? (not uint?) because
    // unresolved conditional branches are serialized as -1, matching TypeScript's behavior.
    public int? ConditionalTrueType;
    public int? ConditionalFalseType;
    public uint? SubstitutionBaseType;
    public uint? ConstraintType;
    public uint? InstantiatedType;
    public uint[]? TypeArguments;
    public Location? ReferenceLocation;
    public uint? ReverseMappedSourceType;
    public uint? ReverseMappedMappedType;
    public uint? ReverseMappedConstraintType;
    public uint? EvolvingArrayElementType;
    public uint? EvolvingArrayFinalType;
    public Location? DestructuringPattern;
    public Location? FirstDeclaration;
    public string[]? Flags;
    public string Display = "";

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        WriteNumber(encoder, "id", ID);
        WriteString(encoder, "intrinsicName", IntrinsicName);
        WriteString(encoder, "symbolName", SymbolName);
        WriteNullableNumber(encoder, "recursionId", RecursionID);
        if (IsTuple)
        {
            WriteName(encoder, "isTuple");
            MarshalEncode(encoder, true);
        }

        WriteNumbers(encoder, "unionTypes", UnionTypes);
        WriteNumbers(encoder, "intersectionTypes", IntersectionTypes);
        WriteNumbers(encoder, "aliasTypeArguments", AliasTypeArguments);
        WriteNullableNumber(encoder, "keyofType", KeyofType);
        WriteNullableNumber(encoder, "indexedAccessObjectType", IndexedAccessObjectType);
        WriteNullableNumber(encoder, "indexedAccessIndexType", IndexedAccessIndexType);
        WriteNullableNumber(encoder, "conditionalCheckType", ConditionalCheckType);
        WriteNullableNumber(encoder, "conditionalExtendsType", ConditionalExtendsType);
        WriteNullableNumber(encoder, "conditionalTrueType", ConditionalTrueType);
        WriteNullableNumber(encoder, "conditionalFalseType", ConditionalFalseType);
        WriteNullableNumber(encoder, "substitutionBaseType", SubstitutionBaseType);
        WriteNullableNumber(encoder, "constraintType", ConstraintType);
        WriteNullableNumber(encoder, "instantiatedType", InstantiatedType);
        WriteNumbers(encoder, "typeArguments", TypeArguments);
        WriteLocation(encoder, "referenceLocation", ReferenceLocation);
        WriteNullableNumber(encoder, "reverseMappedSourceType", ReverseMappedSourceType);
        WriteNullableNumber(encoder, "reverseMappedMappedType", ReverseMappedMappedType);
        WriteNullableNumber(encoder, "reverseMappedConstraintType", ReverseMappedConstraintType);
        WriteNullableNumber(encoder, "evolvingArrayElementType", EvolvingArrayElementType);
        WriteNullableNumber(encoder, "evolvingArrayFinalType", EvolvingArrayFinalType);
        WriteLocation(encoder, "destructuringPattern", DestructuringPattern);
        WriteLocation(encoder, "firstDeclaration", FirstDeclaration);

        // Go 的 `Flags []string \`json:"flags"\`` 没有 omitzero：永远写，nil 切片写 null。
        WriteName(encoder, "flags");
        MarshalEncode(encoder, Flags);

        WriteString(encoder, "display", Display);
        encoder.WriteToken(EndObject);
    }

    private static void WriteName(Encoder encoder, string name) => encoder.WriteToken(JsonToken.String(name));

    private static void WriteNumber(Encoder encoder, string name, uint value)
    {
        WriteName(encoder, name);
        MarshalEncode(encoder, value);
    }

    private static void WriteNumbers(Encoder encoder, string name, uint[]? values)
    {
        if (values is null)
        {
            return;
        }

        WriteName(encoder, name);
        MarshalEncode(encoder, values);
    }

    private static void WriteNullableNumber(Encoder encoder, string name, int? value)
    {
        if (value is null)
        {
            return;
        }

        WriteName(encoder, name);
        MarshalEncode(encoder, value.Value);
    }

    private static void WriteNullableNumber(Encoder encoder, string name, uint? value)
    {
        if (value is null)
        {
            return;
        }

        WriteName(encoder, name);
        MarshalEncode(encoder, value.Value);
    }

    private static void WriteString(Encoder encoder, string name, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        WriteName(encoder, name);
        MarshalEncode(encoder, value);
    }

    private static void WriteLocation(Encoder encoder, string name, Location? value)
    {
        if (value is null)
        {
            return;
        }

        WriteName(encoder, name);
        MarshalEncode(encoder, value);
    }
}

/// <summary>Represents a source code location.</summary>
public sealed class Location : IJsonMarshaler
{
    public string Path = "";
    public LineAndChar? Start;
    public LineAndChar? End;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("path"));
        MarshalEncode(encoder, Path);
        if (Start is not null)
        {
            encoder.WriteToken(JsonToken.String("start"));
            MarshalEncode(encoder, Start);
        }

        if (End is not null)
        {
            encoder.WriteToken(JsonToken.String("end"));
            MarshalEncode(encoder, End);
        }

        encoder.WriteToken(EndObject);
    }
}

/// <summary>Represents a line and character position (1-indexed).</summary>
public sealed class LineAndChar : IJsonMarshaler
{
    public int Line;
    public int Character;

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);
        encoder.WriteToken(JsonToken.String("line"));
        MarshalEncode(encoder, Line);
        encoder.WriteToken(JsonToken.String("character"));
        MarshalEncode(encoder, Character);
        encoder.WriteToken(EndObject);
    }
}

internal sealed partial class TypeTracer
{
    private TypeDescriptor BuildTypeDescriptor(TracedType typ, Dictionary<object, int> recursionIdentityMap)
    {
        var symbol = typ.Symbol();
        var aliasSymbol = typ.AliasSymbol();

        var desc = new TypeDescriptor
        {
            ID = typ.Id(),
            Flags = typ.FormatFlags(),
        };

        // Assign a unique integer token per recursion identity, matching TypeScript's behavior.
        // This lets trace analysis tools detect which types share the same recursion identity.
        var identity = typ.RecursionIdentity();
        if (identity is not null)
        {
            if (!recursionIdentityMap.TryGetValue(identity, out var token))
            {
                token = recursionIdentityMap.Count;
                recursionIdentityMap[identity] = token;
            }

            desc.RecursionID = token;
        }

        // Intrinsic name
        var intrinsicName = typ.IntrinsicName();
        if (intrinsicName.Length != 0)
        {
            desc.IntrinsicName = intrinsicName;
        }

        // Symbol name - escape the internal symbol name prefix for valid JSON
        if (aliasSymbol is not null)
        {
            desc.SymbolName = EscapeAllInternalSymbolNames(aliasSymbol.Name);
        }
        else if (symbol is not null)
        {
            desc.SymbolName = EscapeAllInternalSymbolNames(symbol.Name);
        }

        // Tuple flag
        if (typ.IsTuple())
        {
            desc.IsTuple = true;
        }

        // Union types
        var unionTypes = typ.UnionTypes();
        if (unionTypes.Length > 0)
        {
            desc.UnionTypes = MapTypeIds(unionTypes);
        }

        // Intersection types
        var intersectionTypes = typ.IntersectionTypes();
        if (intersectionTypes.Length > 0)
        {
            desc.IntersectionTypes = MapTypeIds(intersectionTypes);
        }

        // Alias type arguments
        var aliasTypeArguments = typ.AliasTypeArguments();
        if (aliasTypeArguments.Length > 0)
        {
            desc.AliasTypeArguments = MapTypeIds(aliasTypeArguments);
        }

        // Index type (keyof)
        var indexType = typ.IndexType();
        if (indexType is not null)
        {
            desc.KeyofType = indexType.Id();
        }

        // Indexed access type
        var objectType = typ.IndexedAccessObjectType();
        if (objectType is not null)
        {
            desc.IndexedAccessObjectType = objectType.Id();
        }

        var indexedAccessIndexType = typ.IndexedAccessIndexType();
        if (indexedAccessIndexType is not null)
        {
            desc.IndexedAccessIndexType = indexedAccessIndexType.Id();
        }

        // Conditional type
        if (typ.IsConditional())
        {
            var checkType = typ.ConditionalCheckType();
            if (checkType is not null)
            {
                desc.ConditionalCheckType = checkType.Id();
            }

            var extendsType = typ.ConditionalExtendsType();
            if (extendsType is not null)
            {
                desc.ConditionalExtendsType = extendsType.Id();
            }

            var trueType = typ.ConditionalTrueType();
            desc.ConditionalTrueType = trueType is not null ? (int)trueType.Id() : -1;
            var falseType = typ.ConditionalFalseType();
            desc.ConditionalFalseType = falseType is not null ? (int)falseType.Id() : -1;
        }

        // Substitution type
        var baseType = typ.SubstitutionBaseType();
        if (baseType is not null)
        {
            desc.SubstitutionBaseType = baseType.Id();
        }

        var constraint = typ.SubstitutionConstraintType();
        if (constraint is not null)
        {
            desc.ConstraintType = constraint.Id();
        }

        // Reference type
        var target = typ.ReferenceTarget();
        if (target is not null)
        {
            desc.InstantiatedType = target.Id();
        }

        var referenceTypeArguments = typ.ReferenceTypeArguments();
        if (referenceTypeArguments.Length > 0)
        {
            desc.TypeArguments = MapTypeIds(referenceTypeArguments);
        }

        var referenceNode = typ.ReferenceNode();
        if (referenceNode is not null)
        {
            desc.ReferenceLocation = GetLocation(referenceNode);
        }

        // Reverse mapped type
        var reverseMappedSourceType = typ.ReverseMappedSourceType();
        if (reverseMappedSourceType is not null)
        {
            desc.ReverseMappedSourceType = reverseMappedSourceType.Id();
        }

        var reverseMappedMappedType = typ.ReverseMappedMappedType();
        if (reverseMappedMappedType is not null)
        {
            desc.ReverseMappedMappedType = reverseMappedMappedType.Id();
        }

        var reverseMappedConstraintType = typ.ReverseMappedConstraintType();
        if (reverseMappedConstraintType is not null)
        {
            desc.ReverseMappedConstraintType = reverseMappedConstraintType.Id();
        }

        // Evolving array type
        var elementType = typ.EvolvingArrayElementType();
        if (elementType is not null)
        {
            desc.EvolvingArrayElementType = elementType.Id();
        }

        var finalType = typ.EvolvingArrayFinalType();
        if (finalType is not null)
        {
            desc.EvolvingArrayFinalType = finalType.Id();
        }

        // Pattern (destructuring)
        var pattern = typ.Pattern();
        if (pattern is not null)
        {
            desc.DestructuringPattern = GetLocation(pattern);
        }

        // First declaration - prefer aliasSymbol, matching TypeScript's `aliasSymbol ?? symbol`
        var firstDeclSymbol = aliasSymbol ?? symbol;
        if (firstDeclSymbol is not null && firstDeclSymbol.Declarations is { Length: > 0 })
        {
            desc.FirstDeclaration = GetLocation(firstDeclSymbol.Declarations[0]);
        }

        // Display text
        var display = typ.Display();
        if (display.Length != 0)
        {
            desc.Display = display;
        }

        return desc;
    }

    private static uint[]? MapTypeIds(TracedType[] types)
    {
        if (types.Length == 0)
        {
            return null;
        }

        var ids = new uint[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is not null)
            {
                ids[i] = types[i].Id();
            }
        }

        return ids;
    }

    private static Location? GetLocation(Node? node)
    {
        if (node is null)
        {
            return null;
        }

        var file = GetSourceFileOfNode(node);
        if (file is null)
        {
            return null;
        }

        var startPos = Scanner.Scanner.GetTokenPosOfNode(node, file, false);
        var (startLine, startChar) = Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(file, startPos);
        var (endLine, endChar) = Scanner.Scanner.GetECMALineAndUTF16CharacterOfPosition(file, node.End());

        return new Location
        {
            Path = ToPath(file.FileName(), "", false).ToString(),
            Start = new LineAndChar
            {
                Line = startLine + 1,
                Character = (int)startChar + 1,
            },
            End = new LineAndChar
            {
                Line = endLine + 1,
                Character = (int)endChar + 1,
            },
        };
    }
}
