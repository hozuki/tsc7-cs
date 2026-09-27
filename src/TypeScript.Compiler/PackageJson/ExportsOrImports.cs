// ported from: internal/packagejson/exportsorimports.go
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
//
// 1) Go 的 `ExportsOrImports` 内嵌 `JSONValue`（字段提升）。C# 用**继承**：`ExportsOrImports : JSONValue`
//    （docs/architecture.md §2 的"内嵌 → 继承"落法）。基类 `JSONValue.AsObject/AsArray` 因此被隐藏，
//    这里用 `new` 覆盖成 ExportsOrImports 的元素类型（Go 的 `AsObject() *OrderedMap[string, ExportsOrImports]`）。
//    同时把 `IJsonUnmarshaler` **重新列在基类列表里**：C# 的接口实现映射只有重新列出接口时才会改用派生类的
//    `new` 成员（否则 `((IJsonUnmarshaler)e).UnmarshalJSONFrom` 仍会调到基类那份，元素类型就错了）。
//
// 2) `objectKind`（未导出 int8 + 常量组）→ `internal enum ObjectKind : sbyte`，成员名按 docs §3.3 去掉类型前缀。
//
// 3) **`initObjectKind` 的缓存**：Go 里 `IsSubpaths/IsImports/IsConditions` 是**值接收者**，所以
//    `e.initObjectKind()`（指针接收者）改的是**副本**，缓存其实每次都被丢掉、下次重新算一遍（可观察结果相同）。
//    C# 的实例方法都在同一个对象上，缓存真的生效 —— 只是少算几次，返回值不变。
//
// 4) `panic("expected object"/"expected array")` → `throw new DebugFailureException(...)`。
using TypeScript.Collections;
using TypeScript.Json;

namespace TypeScript.PackageJson;

/// <summary>Go: <c>type objectKind int8</c> + <c>objectKindUnknown/…</c> 常量组（未导出 → internal PascalCase）。</summary>
internal enum ObjectKind : sbyte
{
    Unknown = 0,
    Subpaths = 1,
    Conditions = 2,
    Imports = 3,
    Invalid = 4,
}

/// <summary>
/// Go: <c>type ExportsOrImports struct { JSONValue; objectKind objectKind }</c>：
/// package.json 的 `exports`/`imports` 字段（子路径映射 / 条件解析 / 导入条目三种形态之一）。
/// </summary>
public sealed class ExportsOrImports : JSONValue, IJsonUnmarshaler
{
    private ObjectKind _objectKind;

    // Go: var _ json.UnmarshalerFrom = (*ExportsOrImports)(nil)
    /// <summary>Go 的 <c>UnmarshalJSONFrom</c>：以 T = ExportsOrImports 复用 JSONValue 的解码（见 JsonValue.cs 文件头第 4 条）。</summary>
    public new void UnmarshalJSONFrom(Decoder dec)
    {
        UnmarshalJSONValueFrom<ExportsOrImports>(dec);
    }

    public new OrderedMap<string, ExportsOrImports>? AsObject()
    {
        if (Type != JSONValueType.Object)
        {
            throw new DebugFailureException("expected object");
        }

        return (OrderedMap<string, ExportsOrImports>?)Value;
    }

    public new ExportsOrImports[] AsArray()
    {
        if (Type != JSONValueType.Array)
        {
            throw new DebugFailureException("expected array");
        }

        return (ExportsOrImports[])Value!;
    }

    public bool IsSubpaths()
    {
        InitObjectKind();
        return _objectKind == ObjectKind.Subpaths;
    }

    public bool IsImports()
    {
        InitObjectKind();
        return _objectKind == ObjectKind.Imports;
    }

    public bool IsConditions()
    {
        InitObjectKind();
        return _objectKind == ObjectKind.Conditions;
    }

    // Go: func (e *ExportsOrImports) initObjectKind()
    private void InitObjectKind()
    {
        if (_objectKind == ObjectKind.Unknown && Type == JSONValueType.Object)
        {
            var obj = AsObject()!;
            if (obj.Size() > 0)
            {
                var seenDot = false;
                var seenHash = false;
                var seenOther = false;
                foreach (var key in obj.Keys())
                {
                    if (key.Length > 0)
                    {
                        // Go 的 k[0] 是 UTF-8 首字节，C# 是 UTF-16 首单元；被比较的只有 ASCII 的 '.' 与 '#'，等价。
                        seenDot = seenDot || key[0] == '.';
                        seenHash = seenHash || key[0] == '#';
                        seenOther = seenOther || (key[0] != '.' && key[0] != '#');
                        if (seenOther && (seenDot || seenHash))
                        {
                            _objectKind = ObjectKind.Invalid;
                            return;
                        }
                    }
                }

                if (seenDot)
                {
                    _objectKind = ObjectKind.Subpaths;
                    return;
                }

                if (seenHash)
                {
                    _objectKind = ObjectKind.Imports;
                    return;
                }
            }

            _objectKind = ObjectKind.Conditions;
        }
    }
}
