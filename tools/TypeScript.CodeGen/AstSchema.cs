// ast.json 的模式模型 + 查询 API。
//
// 判定逻辑逐条对应 TSGO 的 tools/scripts/tsc/schema.ts（SchemaAPI/MemberInfo/ListType/…），
// 只是把解析结果直接落成 C# 类型串（docs/ast.md §2）：
//   - Go 里所有具体节点/节点别名/实例化别名都是 `*Node`（`type IdentifierNode = Node`）→ C# 一律 `Node?`；
//   - `list: NodeList` → `NodeList?`、`list: ModifierList` → `ModifierList?`、`list: raw` → `T[]?`；
//   - Go 未导出字段（含小写首字母）→ `_camelCase` + internal/private。
using System.Globalization;

namespace TypeScript.CodeGen;

internal enum TypeShape
{
    Primitive,
    Kind,
    Node,
    List,
}

internal sealed class ResolvedType
{
    public string CsType = "object?";
    public TypeShape Shape = TypeShape.Primitive;
    public bool IsString;
    public string? AliasName;
    public List<string> Kinds = [];
    public bool IsTypeParameter;
}

internal class FieldDef
{
    public string Name = "";
    public List<string> Types = [];
    public string? ListKind;
    public bool Private;
    public bool Optional;
    public bool GoOnly;
    public bool NoGo;
    public bool NoTS;
    public bool NoFactory;
    public string? Visit;
    public string? TypeGuard;
    public string Owner = "";
    public ResolvedType DeclaredType = new();
    public ResolvedType Type = new();
}

internal sealed class MemberDef : FieldDef
{
    public bool Inherited;
    public string? Bitmask;
    public bool? ExplicitPrivate;
    public bool? ExplicitOptional;
    public string? DeclaredAliasName => DeclaredType.AliasName;
}

internal sealed class BaseEntry
{
    public string Key = "";
    public string? Brand;
    public List<string> Extends = [];
    public List<FieldDef> Fields = [];
}

internal sealed class NodeDef
{
    public string Key = "";
    public List<string> Kinds = [];
    public string PrimaryKind = "";
    public List<string> Extends = [];
    public List<MemberDef> Members = [];
    public bool GenerateSubtreeFacts;
    public bool HandWritten;
    public bool HandWrittenVisitor;
    public Dictionary<string, string> TypeParameters = new(StringComparer.Ordinal);
    public bool KindTypeIsTypeParameter;
}

internal sealed class KindElementDef
{
    public string? Name;
    public string? Comment;
}

internal sealed class KindMarkerDef
{
    public string Name = "";
    public string Value = "";
}

internal sealed class KindAliasDef
{
    public string Name = "";
    public List<string> Members = [];
    public (string First, string Last)? Range;
}

internal sealed class ClassFieldDef
{
    public string Owner = "";
    public string Name = "";
    public string CsName = "";
    public string CsType = "";
    public string Modifier = "public";
    public bool IsString;
    public bool Optional;
}

internal sealed class InterfaceForwardDef
{
    public string Owner = "";
    public string Name = "";
    public string CsName = "";
    public string CsType = "";
}

internal sealed class DataAccessorDef
{
    public string Owner = "";
    public string Name = "";
    public string ReturnType = "";
}

internal sealed class AstSchema
{
    // Go 里 Node 上的成员名：字段与它们同名要加 `new`（Go 同样遮蔽：`id.Text` 取字段而不是 Node.Text()）。
    private static readonly HashSet<string> _nodeHiddenFieldNames = new(StringComparer.Ordinal)
    {
        "Arguments", "Attributes", "Body", "Children", "ClassName", "Elements", "Expression", "ImportClause",
        "Initializer", "IsTypeOnly", "Label", "LocalSymbol", "Locals", "Members", "ModuleSpecifier", "Parameters",
        "PostfixToken", "Properties", "PropertyName", "QuestionDotToken", "QuestionToken", "RawText", "Statement",
        "Statements", "Symbol", "TagName", "Text", "Type", "TypeArguments", "TypeExpression", "TypeParameters",
    };

    // Go 的原生类型 → C#（goOnly 字段的 rawType 也走这里）。
    private static readonly Dictionary<string, string> _primitiveCsTypes = new(StringComparer.Ordinal)
    {
        ["string"] = "string",
        ["bool"] = "bool",
        ["boolean"] = "bool",
        ["int"] = "int",
        ["any"] = "object?",
        ["NodeFlags"] = "NodeFlags",
        ["ModifierFlags"] = "ModifierFlags",
        ["TokenFlags"] = "TokenFlags",
        ["SymbolTable"] = "SymbolTable?",
        ["*Symbol"] = "Symbol?",
        ["*Node"] = "Node?",
        ["*FlowNode"] = "FlowNode?",
        ["*FlowList"] = "FlowList?",
        ["atomic.Uint32"] = "int",
    };

    private static readonly HashSet<string> _flagTypeNames = new(StringComparer.Ordinal)
    {
        "NodeFlags", "ModifierFlags", "TokenFlags", "SymbolFlags", "CheckFlags", "FunctionFlags",
    };

    // C# 保留关键字（参数名撞上时用 @ 转义）。
    private static readonly HashSet<string> _csharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    private readonly Dictionary<string, BaseEntry> _baseByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeDef> _nodeByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KindAliasDef> _kindAliasByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _listAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _nodeAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _instantiationAliasToNode = new(StringComparer.Ordinal);
    private readonly List<string> _kindElementNames = [];
    private readonly List<KindMarkerDef> _kindMarkers = [];

    public List<BaseEntry> Bases { get; } = [];

    public List<NodeDef> Nodes { get; } = [];

    public List<KindElementDef> KindElements { get; } = [];

    public List<KindMarkerDef> KindMarkers => _kindMarkers;

    public List<KindAliasDef> KindAliases { get; } = [];

    public static AstSchema Load(string json)
    {
        var root = MiniJson.ParseObject(json);
        var schema = new AstSchema();
        var kinds = root["kinds"] as JsonObject;
        if (kinds is not null)
        {
            foreach (var element in AsList(kinds["elements"]))
            {
                if (element is JsonObject obj)
                {
                    schema.KindElements.Add(new KindElementDef
                    {
                        Name = AsString(obj["name"]),
                        Comment = AsString(obj["comment"]),
                    });
                }
                else
                {
                    schema.KindElements.Add(new KindElementDef { Name = AsString(element) });
                }
            }

            foreach (var element in AsList(kinds["elements"]))
            {
                var name = element is JsonObject obj ? AsString(obj["name"]) : AsString(element);
                if (name is not null)
                {
                    schema._kindElementNames.Add(name);
                }
            }

            foreach (var marker in AsList(kinds["markers"]))
            {
                var obj = (JsonObject)marker!;
                schema._kindMarkers.Add(new KindMarkerDef
                {
                    Name = AsString(obj["name"]) ?? "",
                    Value = AsString(obj["value"]) ?? "",
                });
            }

            if (kinds["aliases"] is JsonObject aliases)
            {
                foreach (var (name, value) in aliases.Items)
                {
                    var alias = new KindAliasDef { Name = name };
                    if (value is JsonObject rangeObj && rangeObj["range"] is not null)
                    {
                        var range = AsList(rangeObj["range"]);
                        var first = AsString(range[0]) ?? "";
                        var last = AsString(range[1]) ?? "";
                        alias.Range = (first, last);
                        var firstIndex = schema._kindElementNames.IndexOf(schema.ResolveKindMarkerValue(first));
                        var lastIndex = schema._kindElementNames.IndexOf(schema.ResolveKindMarkerValue(last));
                        if (firstIndex < 0 || lastIndex < 0)
                        {
                            throw new InvalidDataException($"Kind 别名 {name} 的 range [{first}, {last}] 无法解析。");
                        }

                        alias.Members.AddRange(schema._kindElementNames.GetRange(firstIndex, lastIndex - firstIndex + 1));
                    }
                    else
                    {
                        // 成员名可能引用尚未解析的别名，所以先记录原始成员名，加载完所有别名后再统一展开。
                        alias.Members.AddRange(AsList(value).Select(m => AsString(m) ?? ""));
                    }

                    schema._kindAliasByName[name] = alias;
                    schema.KindAliases.Add(alias);
                }
            }
        }

        var nodes = (JsonObject)root["nodes"]!;
        if (nodes["listAliases"] is JsonObject listAliases)
        {
            foreach (var (name, value) in listAliases.Items)
            {
                schema._listAliases[name] = AsString(value) ?? "";
            }
        }

        if (nodes["aliases"] is JsonObject nodeAliases)
        {
            foreach (var (name, value) in nodeAliases.Items)
            {
                schema._nodeAliases[name] = value;
            }
        }

        var bases = (JsonObject)root["bases"]!;
        foreach (var (key, value) in bases.Items)
        {
            var obj = (JsonObject)value!;
            var entry = new BaseEntry
            {
                Key = key,
                Brand = AsString(obj["brand"]),
            };
            foreach (var extend in AsList(obj["extends"]))
            {
                entry.Extends.Add(AsString(extend) ?? "");
            }

            if (obj["fields"] is JsonObject fields)
            {
                foreach (var (fieldName, fieldValue) in fields.Items)
                {
                    var field = (FieldDef)schema.LoadField(fieldName, (JsonObject)fieldValue!, isMember: false);
                    field.Owner = key;
                    entry.Fields.Add(field);
                }
            }

            schema._baseByKey[key] = entry;
            schema.Bases.Add(entry);
        }

        var definitions = (JsonObject)nodes["definitions"]!;
        foreach (var (key, value) in definitions.Items)
        {
            var obj = (JsonObject)value!;
            var node = new NodeDef { Key = key };
            var kind = obj["kind"];
            if (kind is List<object?> kindList)
            {
                node.Kinds.AddRange(kindList.Select(k => AsString(k) ?? ""));
            }
            else if (AsString(kind) is { } kindName)
            {
                node.Kinds.Add(kindName);
            }

            node.PrimaryKind = node.Kinds.Count > 0 ? node.Kinds[0] : key;
            foreach (var extend in AsList(obj["extends"]))
            {
                node.Extends.Add(AsString(extend) ?? "");
            }

            foreach (var parameter in AsList(obj["typeParameters"]))
            {
                var parameterObj = (JsonObject)parameter!;
                node.TypeParameters[AsString(parameterObj["name"]) ?? ""] = AsString(parameterObj["constraint"]) ?? "";
            }

            if (obj["instantiationAliases"] is JsonObject instantiationAliases)
            {
                foreach (var (aliasName, _) in instantiationAliases.Items)
                {
                    schema._instantiationAliasToNode[aliasName] = key;
                }
            }

            node.GenerateSubtreeFacts = AsBool(obj["generateSubtreeFacts"]);
            node.HandWritten = AsBool(obj["handWritten"]);
            node.HandWrittenVisitor = AsBool(obj["handWrittenVisitor"]);

            foreach (var member in AsList(obj["members"]))
            {
                var memberObj = (JsonObject)member!;
                var memberName = AsString(memberObj["name"]) ?? "";
                var def = (MemberDef)schema.LoadField(memberName, memberObj, isMember: true);
                def.Owner = key;
                def.Inherited = AsBool(memberObj["inherited"]);
                def.Bitmask = AsString(memberObj["bitmask"]);
                def.ExplicitPrivate = memberObj.Has("private") ? AsBool(memberObj["private"]) : null;
                def.ExplicitOptional = memberObj.Has("optional") ? AsBool(memberObj["optional"]) : null;
                node.Members.Add(def);
            }

            schema._nodeByKey[key] = node;
            schema.Nodes.Add(node);
        }

        schema.ResolveTypes();
        schema.ExpandKindAliases();
        return schema;
    }

    private FieldDef LoadField(string name, JsonObject obj, bool isMember)
    {
        var def = isMember ? new MemberDef() : new FieldDef();
        def.Name = name;
        var type = obj["type"];
        if (type is List<object?> list)
        {
            def.Types.AddRange(list.Select(t => AsString(t) ?? ""));
        }
        else if (AsString(type) is { } single)
        {
            def.Types.Add(single);
        }

        def.ListKind = AsString(obj["list"]);
        def.Private = AsBool(obj["private"]);
        def.Optional = AsBool(obj["optional"]);
        def.GoOnly = AsBool(obj["goOnly"]);
        def.NoGo = AsBool(obj["noGo"]);
        def.NoTS = AsBool(obj["noTS"]);
        def.NoFactory = AsBool(obj["noFactory"]);
        def.Visit = AsString(obj["visit"]);
        def.TypeGuard = AsString(obj["typeGuard"]);
        return def;
    }

    private void ResolveTypes()
    {
        foreach (var baseEntry in Bases)
        {
            foreach (var field in baseEntry.Fields)
            {
                // Go 的字段可见性由首字母决定：小写 = 包内可见（C# internal + _camelCase）。
                field.Private = field.Private || char.IsLower(field.Name[0]);
                field.DeclaredType = ResolveNames(field.Types, node: null);
                field.Type = field.ListKind is null
                    ? field.DeclaredType
                    : ResolveList(field.DeclaredType, field.ListKind);
            }
        }

        foreach (var node in Nodes)
        {
            foreach (var member in node.Members)
            {
                var inherited = member.Inherited ? FindInheritedField(node, member.Name) : null;
                member.Private = (member.ExplicitPrivate ?? inherited?.Private ?? false) || char.IsLower(member.Name[0]);
                member.Optional = member.ExplicitOptional ?? inherited?.Optional ?? false;
                member.Visit ??= inherited?.Visit;
                var types = member.Types.Count > 0 ? member.Types : inherited?.Types ?? [];
                member.DeclaredType = ResolveNames(types, node);
                var listKind = member.Inherited ? inherited?.ListKind : member.ListKind;
                member.Type = listKind is null ? member.DeclaredType : ResolveList(member.DeclaredType, listKind);
            }

            var kindMember = node.Members.FirstOrDefault(m => m.Name is "Kind" or "kind");
            node.KindTypeIsTypeParameter = kindMember?.DeclaredType.IsTypeParameter ?? false;
        }
    }

    private FieldDef? FindInheritedField(NodeDef node, string name) => FindInheritedField(node.Extends, name);

    private FieldDef? FindInheritedField(List<string> baseKeys, string name)
    {
        foreach (var key in baseKeys)
        {
            if (!_baseByKey.TryGetValue(key, out var baseEntry))
            {
                continue;
            }

            var direct = baseEntry.Fields.FirstOrDefault(f => f.Name == name);
            if (direct is not null)
            {
                return direct;
            }

            var inherited = FindInheritedField(baseEntry.Extends, name);
            if (inherited is not null)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>把所有 kind 别名的成员递归展开成具体 kind 名（别名可以引用别名，且顺序任意）。</summary>
    private void ExpandKindAliases()
    {
        foreach (var alias in KindAliases)
        {
            var expanded = new List<string>();
            foreach (var member in alias.Members)
            {
                ExpandKindAliasMember(member, expanded);
            }

            alias.Members.Clear();
            alias.Members.AddRange(expanded);
        }
    }

    private void ExpandKindAliasMember(string name, List<string> result)
    {
        if (_kindAliasByName.TryGetValue(name, out var alias) && !_kindElementNames.Contains(name))
        {
            foreach (var member in alias.Members)
            {
                ExpandKindAliasMember(member, result);
            }

            return;
        }

        result.Add(name);
    }

    private string ResolveKindMarkerValue(string name)
    {
        foreach (var marker in _kindMarkers)
        {
            if (marker.Name == name)
            {
                return ResolveKindMarkerValue(marker.Value);
            }
        }

        return name;
    }

    // ── 类型解析（对应 schema.ts 的 SchemaAPI.resolveType + formatGoReference）──

    private ResolvedType ResolveNames(List<string> types, NodeDef? node)
    {
        if (types.Count == 0)
        {
            return new ResolvedType { CsType = "object?", Shape = TypeShape.Primitive };
        }

        if (types.Count == 1)
        {
            return ResolveName(types[0], node);
        }

        return ResolveUnion(types.Select(t => ResolveName(t, node)).ToList());
    }

    private ResolvedType ResolveName(string typeName, NodeDef? node)
    {
        if (node is not null && node.TypeParameters.TryGetValue(typeName, out var constraint))
        {
            var resolved = constraint == typeName
                ? new ResolvedType { CsType = "object?", Shape = TypeShape.Primitive }
                : ResolveName(constraint, node);
            return new ResolvedType
            {
                CsType = resolved.CsType,
                Shape = resolved.Shape,
                IsString = resolved.IsString,
                AliasName = resolved.AliasName,
                Kinds = resolved.Kinds,
                IsTypeParameter = true,
            };
        }

        if (_kindAliasByName.TryGetValue(typeName, out var kindAlias))
        {
            return new ResolvedType
            {
                CsType = "Kind",
                Shape = TypeShape.Kind,
                AliasName = typeName,
                Kinds = [.. kindAlias.Members],
                IsTypeParameter = false,
            };
        }

        if (typeName == "Kind" || typeName.StartsWith("SyntaxKind.", StringComparison.Ordinal))
        {
            return new ResolvedType
            {
                CsType = "Kind",
                Shape = TypeShape.Kind,
                Kinds = typeName == "Kind" ? [] : [typeName["SyntaxKind.".Length..]],
            };
        }

        if (typeName == "Node")
        {
            return NodeType();
        }

        if (_primitiveCsTypes.TryGetValue(typeName, out var csType))
        {
            return new ResolvedType
            {
                CsType = csType,
                Shape = TypeShape.Primitive,
                IsString = typeName == "string",
            };
        }

        if (_listAliases.ContainsKey(typeName))
        {
            return new ResolvedType { CsType = "NodeList?", Shape = TypeShape.List, AliasName = typeName };
        }

        if (_instantiationAliasToNode.ContainsKey(typeName))
        {
            return NodeType(typeName);
        }

        if (_nodeAliases.TryGetValue(typeName, out var alias))
        {
            // 节点别名（联合或 `{base}`）在 Go 里都是 `*Alias`（= *Node）。
            _ = alias;
            return NodeType(typeName);
        }

        if (_nodeByKey.ContainsKey(typeName) || _baseByKey.ContainsKey(typeName))
        {
            return NodeType(typeName);
        }

        throw new InvalidDataException($"ast.json 里的类型 {typeName} 没有对应的 C# 类型映射。");
    }

    private static ResolvedType NodeType(string? aliasName = null) => new()
    {
        CsType = "Node?",
        Shape = TypeShape.Node,
        AliasName = aliasName,
    };

    private static ResolvedType ResolveUnion(List<ResolvedType> parts)
    {
        if (parts.Any(p => p.Shape is TypeShape.Node or TypeShape.List))
        {
            return new ResolvedType { CsType = "Node?", Shape = TypeShape.Node };
        }

        if (parts.All(p => p.Shape == TypeShape.Kind))
        {
            var kinds = new List<string>();
            foreach (var part in parts)
            {
                kinds.AddRange(part.Kinds);
            }

            return new ResolvedType { CsType = "Kind", Shape = TypeShape.Kind, Kinds = kinds };
        }

        var first = parts[0];
        if (parts.All(p => p.CsType == first.CsType))
        {
            return new ResolvedType { CsType = first.CsType, Shape = first.Shape, IsString = first.IsString };
        }

        throw new InvalidDataException(
            $"无法解析联合类型：{string.Join(" | ", parts.Select(p => p.CsType))}");
    }

    private static ResolvedType ResolveList(ResolvedType element, string listKind)
    {
        if (listKind == "ModifierList")
        {
            return new ResolvedType { CsType = "ModifierList?", Shape = TypeShape.List };
        }

        if (listKind != "raw")
        {
            return new ResolvedType { CsType = "NodeList?", Shape = TypeShape.List };
        }

        if (element.Shape == TypeShape.Node)
        {
            return new ResolvedType { CsType = "Node[]?", Shape = TypeShape.List };
        }

        if (element.IsString)
        {
            return new ResolvedType { CsType = "string[]?", Shape = TypeShape.Primitive };
        }

        throw new InvalidDataException($"raw 列表的元素类型 {element.CsType} 没有 C# 映射。");
    }

    // ── 生成器用到的查询 ──

    public List<MemberDef> SchemaMembers(NodeDef node) =>
        node.Members.Where(m => !m.GoOnly && !m.NoFactory).ToList();

    public bool IsKindMember(MemberDef member, NodeDef node) =>
        member.Name is "Kind" or "kind" && member.DeclaredType.Shape == TypeShape.Kind;

    public bool IsNodeFlagsMember(MemberDef member) =>
        member.DeclaredType.CsType == "NodeFlags";

    public bool IsChild(MemberDef member, NodeDef node) =>
        member.Type.Shape is TypeShape.Node or TypeShape.List;

    public bool IsRawList(MemberDef member, NodeDef node) =>
        member.Type.CsType.EndsWith("[]?", StringComparison.Ordinal);

    public bool HasTextContent(NodeDef node)
    {
        if (node.Members.Count == 0)
        {
            return false;
        }

        return SchemaMembers(node).Any(m => m.DeclaredType.IsString || m.Type.IsString || m.Type.CsType == "string[]?");
    }

    public string CsType(MemberDef member, NodeDef node) => member.Type.CsType;

    public string CsFieldType(FieldDef field) => field.Type.CsType;

    public string CsFieldName(FieldDef field) => field.Private ? "_" + field.Name : field.Name;

    public string CsFieldName(string name, bool unexported) => unexported ? "_" + name : name;

    /// <summary>Go 的 MemberInfo.goParamName（含 Go 关键字规避）+ C# 关键字转义。</summary>
    public string CsParamName(string name)
    {
        var param = GeneratorLowerFirst(name);
        param = param switch
        {
            "type" => "typeNode",
            "default" => "defaultNode",
            "case" => "caseNode",
            _ => param,
        };
        return _csharpKeywords.Contains(param) ? "@" + param : param;
    }

    public string CsBitmask(string bitmask)
    {
        foreach (var flagType in _flagTypeNames)
        {
            if (bitmask.StartsWith(flagType, StringComparison.Ordinal))
            {
                return flagType + "." + bitmask[flagType.Length..];
            }
        }

        return bitmask;
    }

    public List<FieldDef> ExportedInterfaceFields(BaseEntry baseEntry) =>
        baseEntry.Fields.Where(f => !f.Private && !f.NoGo).ToList();

    public List<string> KindTypes(NodeDef node)
    {
        var kindMember = node.Members.FirstOrDefault(m => m.Name is "Kind" or "kind");
        if (kindMember is not null && kindMember.DeclaredType.Kinds.Count > 0)
        {
            return kindMember.DeclaredType.Kinds;
        }

        return [node.PrimaryKind];
    }

    public List<string> ExpandKindAliasMembers(string name) =>
        _kindAliasByName.TryGetValue(name, out var alias) ? alias.Members : [name];

    /// <summary>节点类要声明的字段：自己的成员（非继承）+ 平铺进来的 mixin 基础字段。</summary>
    public List<ClassFieldDef> ClassFields(NodeDef node)
    {
        var result = new List<ClassFieldDef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in node.Members)
        {
            if (member.Inherited || IsKindMember(member, node) || member.NoGo)
            {
                continue;
            }

            Add(member.Owner, member.Name, member.Type.CsType, member.Private, member.Type.CsType == "string", member.Optional);
        }

        foreach (var baseKey in BaseFieldOrder(node))
        {
            if (baseKey == "NodeBase")
            {
                continue;
            }

            foreach (var field in _baseByKey[baseKey].Fields)
            {
                if (field.NoGo)
                {
                    continue;
                }

                Add(baseKey, field.Name, field.Type.CsType, field.Private, field.Type.CsType == "string", field.Optional);
            }
        }

        return result;

        void Add(string owner, string name, string csType, bool unexported, bool isString, bool optional)
        {
            if (!seen.Add(name))
            {
                var existing = result.First(f => f.Name == name);
                if (existing.CsType != csType)
                {
                    throw new InvalidDataException(
                        $"{node.Key}.{name} 在 {existing.Owner} 与 {owner} 里的 C# 类型不一致：{existing.CsType} vs {csType}");
                }

                return;
            }

            var csName = unexported ? "_" + name : name;
            var modifier = unexported ? "internal" : "public";
            if (!unexported && _nodeHiddenFieldNames.Contains(name))
            {
                // Go 里字段同名会遮蔽 Node 的方法（`id.Text` 取字段）；C# 需要 `new` 才不报 CS0108。
                modifier += " new";
            }

            result.Add(new ClassFieldDef
            {
                Owner = owner,
                Name = name,
                CsName = csName,
                CsType = csType,
                Modifier = modifier,
                IsString = isString,
                Optional = optional,
            });
        }
    }

    /// <summary>mixin 基础字段的声明顺序：节点自己的成员之后，按 DFS（自己的字段然后 extends）顺序。</summary>
    private IEnumerable<string> BaseFieldOrder(NodeDef node)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        Visit(node.Extends);
        return order;

        void Visit(List<string> keys)
        {
            foreach (var key in keys)
            {
                if (!visited.Add(key) || !_baseByKey.ContainsKey(key))
                {
                    continue;
                }

                order.Add(key);
                Visit(_baseByKey[key].Extends);
            }
        }
    }

    /// <summary>基础（mixin）导出字段要显式实现对应接口的属性转发。</summary>
    public List<InterfaceForwardDef> InterfaceForwards(NodeDef node)
    {
        var result = new List<InterfaceForwardDef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var baseKey in BaseFieldOrder(node))
        {
            if (baseKey == "NodeBase")
            {
                continue;
            }

            foreach (var field in _baseByKey[baseKey].Fields)
            {
                if (field.Private || field.NoGo || !seen.Add(baseKey + "." + field.Name))
                {
                    continue;
                }

                result.Add(new InterfaceForwardDef
                {
                    Owner = baseKey,
                    Name = field.Name,
                    CsName = field.Name,
                    CsType = field.Type.CsType,
                });
            }
        }

        return result;
    }

    /// <summary>Go 里由内嵌基础提供的 `*Data()` 访问器；C# 要在节点类里覆写成 `=> this`。</summary>
    public List<DataAccessorDef> DataAccessors(NodeDef node)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        Collect(node.Extends);
        var result = new List<DataAccessorDef>();
        Add("DeclarationBase", "DeclarationData", "IDeclarationBase?");
        Add("ExportableBase", "ExportableData", "IExportableBase?");
        Add("FlowNodeBase", "FlowNodeData", "IFlowNodeBase?");
        Add("ClassLikeBase", "ClassLikeData", "IClassLikeBase?");
        Add("BodyBase", "BodyData", "IBodyBase?");
        Add("LiteralLikeNodeBase", "LiteralLikeData", "ILiteralLikeNodeBase?");
        Add("TemplateLiteralLikeNodeBase", "TemplateLiteralLikeData", "ITemplateLiteralLikeNodeBase?");
        Add("LocalsContainerBase", "LocalsContainerData", "ILocalsContainerBase?");
        Add("FunctionLikeBase", "FunctionLikeData", "IFunctionLikeBase?");

        if (closure.Contains("FunctionLikeWithBodyBase"))
        {
            Add("FunctionLikeBase", "FunctionLikeData", "IFunctionLikeBase?");
            Add("BodyBase", "BodyData", "IBodyBase?");
        }

        if (closure.Contains("TemplateLiteralLikeNodeBase"))
        {
            Add("LiteralLikeNodeBase", "LiteralLikeData", "ILiteralLikeNodeBase?");
        }

        return result;

        void Collect(List<string> keys)
        {
            foreach (var key in keys)
            {
                if (!closure.Add(key) || !_baseByKey.TryGetValue(key, out var baseEntry))
                {
                    continue;
                }

                Collect(baseEntry.Extends);
            }
        }

        void Add(string owner, string name, string returnType)
        {
            if (closure.Contains(owner) && result.All(r => r.Name != name))
            {
                result.Add(new DataAccessorDef { Owner = owner, Name = name, ReturnType = returnType });
            }
        }
    }

    public bool HasField(NodeDef node, string csName) =>
        ClassFields(node).Any(f => f.CsName == csName);

    /// <summary>node 的内嵌基础闭包里是否包含某个基础（对应 Go 的嵌入关系）。</summary>
    public bool HasBase(NodeDef node, string baseKey)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        return Visit(node.Extends);

        bool Visit(List<string> keys)
        {
            foreach (var key in keys)
            {
                if (!visited.Add(key) || !_baseByKey.TryGetValue(key, out var baseEntry))
                {
                    continue;
                }

                if (key == baseKey || Visit(baseEntry.Extends))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public string ForEachChildPart(MemberDef member, NodeDef node)
    {
        var access = CsFieldName(member);
        if (member.Type.CsType.EndsWith("[]?", StringComparison.Ordinal))
        {
            return $"Ast.VisitNodes(v, {access}.AsSpan())";
        }

        if (member.Type.CsType == "ModifierList?")
        {
            return $"Ast.VisitModifiers(v, {access})";
        }

        if (member.Type.CsType == "NodeList?")
        {
            return $"Ast.VisitNodeList(v, {access})";
        }

        return $"Ast.Visit(v, {access})";
    }

    /// <summary>Go 生成器里 VisitEachChild 用的 `v.visitXxx(...)`（会走 hooks）。</summary>
    public string VisitMethod(MemberDef member)
    {
        if (member.Visit is { } visit)
        {
            return visit switch
            {
                "embeddedStatement" => "v.VisitEmbeddedStatementWithHooks",
                "iterationBody" => "v.VisitIterationBodyWithHooks",
                "parameters" => "v.VisitParametersWithHooks",
                "functionBody" => "v.VisitFunctionBodyWithHooks",
                "topLevelStatements" => "v.VisitTopLevelStatementsWithHooks",
                "modifiers" => "v.VisitModifiersWithHooks",
                _ => $"v.Visit{GeneratorUpperFirst(visit)}WithHooks",
            };
        }

        if (member.Type.CsType == "ModifierList?")
        {
            return "v.VisitModifiersWithHooks";
        }

        if (member.Type.CsType == "NodeList?")
        {
            return "v.VisitNodesWithHooks";
        }

        if (member.Types.Contains("Token"))
        {
            return "v.VisitTokenWithHooks";
        }

        return "v.VisitNodeWithHooks";
    }

    public string SubtreeFactsTerm(MemberDef member, NodeDef node)
    {
        var access = CsFieldName(member);
        if (member.Type.CsType == "ModifierList?")
        {
            return $"Ast.PropagateModifierListSubtreeFacts({access})";
        }

        if (member.Type.Shape == TypeShape.List)
        {
            return $"Ast.PropagateNodeListSubtreeFacts({access}, Ast.PropagateSubtreeFacts)";
        }

        return $"Ast.PropagateSubtreeFacts({access})";
    }

    /// <summary>Go 的 isTextContentType：成员类型是 string 或 raw 的 string 列表（触发工厂里的 textCount++）。</summary>
    internal static bool IsStringField(ResolvedType type) =>
        type.IsString || type.CsType == "string[]?";

    internal static string GeneratorLowerFirst(string name)
    {
        if (name.StartsWith("JSDoc", StringComparison.Ordinal))
        {
            return "jsdoc" + name[5..];
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    internal static string GeneratorUpperFirst(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    internal static List<object?> AsList(object? value) => value as List<object?> ?? [];

    internal static string? AsString(object? value) => value as string;

    internal static bool AsBool(object? value) => value is true;
}
