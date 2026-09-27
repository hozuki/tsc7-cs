// ported from: internal/packagejson/packagejson.go
//
// 与 Go 的偏离（逐条；其余与 Go 逐行对应）：
//
// 1) **内嵌结构体 → 同名同类型字段 / 继承**（用户钉死的落法）：
//    * `Fields` 内嵌 `HeaderFields`/`PathFields`/`DependencyFields`，C# 没有字段提升，故写成**同名字段**：
//      `public HeaderFields HeaderFields = new();` 等（字段名 = 类型名，C# 的 "Color Color" 规则允许）。
//      调用点的差别：Go 的 `root.Types`（提升访问）在 C# 是 `root.PathFields.Types`。
//    * `PackageJson` 内嵌 `Fields` → C# 用**继承** `PackageJson : Fields`，这样 `contents.Name` 这类提升访问
//      在 C# 里仍是 `contents.HeaderFields.Name`（Go 的提升同样要写成 `contents.HeaderFields.Name`）。
//
// 2) **JSON 解码**：Go 靠字段标签 + reflect；C# 无反射（docs §3.6）→ 手写 `UnmarshalJSONFrom`。
//    每个 group 提供 `TryUnmarshalField(name, dec)`（按成员名解一个字段），`Fields.UnmarshalJSONFrom` 读对象、
//    逐成员分派给三个 group，"typescript" 由 Fields 自己处理（对应 Go 的 Parse 里那个带
//    `TypeScript Expected[typeScriptFields]` 的局部匿名结构体），其余成员 `SkipValue()`（Go 的 reflect 会忽略
//    未知字段）。写进 `Fields` 而不是另建类型，是因为 Go 的匿名结构体没有名字。
//
// 3) `Fields.ContentMapper`：Go 标了 `json:"-"`（不参与 reflect 解码，由 Parse 从 `typescript` 字段搬进来）；
//    C# 侧同样不参与 member 分派，只由 "typescript" 分支写。
//
// 4) **包级函数放哪里**（用户钉死）：Go 的 `Parse`/`NewInfoCache`/`ExpectedOf` 是包级函数，按 docs §2 应放
//    "与 package 同名的静态类"，但本包已经有一个同名类型 `PackageJson`（CS0101）。与 Scanner 的先例
//    （`scanner.GetIdentifierToken` → `Scanner.GetIdentifierToken`，"包级函数进同名静态类在本题下只能与实例类合并"）
//    一样，这 3 个包级函数落成 `PackageJson` 类型上的 **public static 成员**（partial 按 Go 源文件拆：
//    本文件放 `Parse`/`TryParse`，Expected.cs 放 `ExpectedOf`，Cache.cs 放 `NewInfoCache`）。
//    **注意第二个同名陷阱**：`PackageJson` 同时是命名空间名，所以在别的命名空间里写 `PackageJson.Parse(...)`
//    会先解析成**命名空间**（CS0234，实测；与 `Json.BeginObject` 同一个坑）。消费方的三种可行写法：
//      `using static TypeScript.PackageJson.PackageJson;` 后写裸名 `Parse(...)`／
//      `using PackageJsonClass = TypeScript.PackageJson.PackageJson;` 后写 `PackageJsonClass.Parse(...)`／
//      全名 `TypeScript.PackageJson.PackageJson.Parse(...)`。
//
// 4b) `Parse` 是 Go 的 `(Fields, error)` 形状：按 docs §3.2 落成"正常返回 + 抛 JsonException"，同时给出用户要求的
//    `TryParse(data, out Fields)`（`if err != nil` 的调用点形状），失败时 `fields` 是**全零的 Fields**（Go 的
//    `return Fields{}, err`，前面解出来的部分丢掉）、调用方据此得到 `Parseable=false`。
//    （`Parse`/`TryParse` 入口处还额外把 STJ 的语法错误异常收成 `JsonException`——原因见方法里的注释。）
//
// 5) `Parse` 的 `(Fields, error)` → `Fields` + 抛 `JsonException`（docs §3.2）；`json.AllowDuplicateNames(true)`
//    照传（手写解码器本来就是"后者覆盖前者"，见 Fields 的分派循环）。
//
// 6) `Expected[map[string]string]` → `Expected<Dictionary<string, string>>`（Go 的 map 无序，C# 的 Dictionary
//    插入序稳定，这一处 C# 反而更可预测）；`Expected[[]string]` → `Expected<string[]>`。
using TypeScript.Collections;
using TypeScript.Json;
using static TypeScript.Collections.Collections;
using static TypeScript.Json.Json;

namespace TypeScript.PackageJson;

/// <summary>Go: <c>type HeaderFields struct</c>（package.json 的 name/version/type）。</summary>
public sealed class HeaderFields
{
    public Expected<string> Name = new();

    public Expected<string> Version = new();

    public Expected<string> Type = new();

    /// <summary>按成员名解一个字段（C# 侧新增；对应 Go 的 reflect 按标签分派）。返回 false 表示本组不认识这个名字。</summary>
    internal bool TryUnmarshalField(string name, Decoder dec)
    {
        switch (name)
        {
            case "name":
                Name.UnmarshalJSONFrom(dec);
                return true;
            case "version":
                Version.UnmarshalJSONFrom(dec);
                return true;
            case "type":
                Type.UnmarshalJSONFrom(dec);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>Go: <c>type PathFields struct</c>（package.json 的解析相关字段）。</summary>
public sealed class PathFields
{
    public Expected<string> TSConfig = new();

    public Expected<string> Main = new();

    public Expected<string> Types = new();

    public Expected<string> Typings = new();

    public JSONValue TypesVersions = new();

    public ExportsOrImports Imports = new();

    public ExportsOrImports Exports = new();

    /// <summary>按成员名解一个字段（C# 侧新增；对应 Go 的 reflect 按标签分派）。</summary>
    internal bool TryUnmarshalField(string name, Decoder dec)
    {
        switch (name)
        {
            case "tsconfig":
                TSConfig.UnmarshalJSONFrom(dec);
                return true;
            case "main":
                Main.UnmarshalJSONFrom(dec);
                return true;
            case "types":
                Types.UnmarshalJSONFrom(dec);
                return true;
            case "typings":
                Typings.UnmarshalJSONFrom(dec);
                return true;
            case "typesVersions":
                TypesVersions.UnmarshalJSONFrom(dec);
                return true;
            case "imports":
                Imports.UnmarshalJSONFrom(dec);
                return true;
            case "exports":
                Exports.UnmarshalJSONFrom(dec);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>Go: <c>type DependencyFields struct</c>（四类依赖表）。</summary>
public sealed class DependencyFields
{
    public Expected<Dictionary<string, string>> Dependencies = new();

    public Expected<Dictionary<string, string>> DevDependencies = new();

    public Expected<Dictionary<string, string>> PeerDependencies = new();

    public Expected<Dictionary<string, string>> OptionalDependencies = new();

    /// <summary>按成员名解一个字段（C# 侧新增；对应 Go 的 reflect 按标签分派）。</summary>
    internal bool TryUnmarshalField(string name, Decoder dec)
    {
        switch (name)
        {
            case "dependencies":
                Dependencies.UnmarshalJSONFrom(dec);
                return true;
            case "devDependencies":
                DevDependencies.UnmarshalJSONFrom(dec);
                return true;
            case "peerDependencies":
                PeerDependencies.UnmarshalJSONFrom(dec);
                return true;
            case "optionalDependencies":
                OptionalDependencies.UnmarshalJSONFrom(dec);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// HasDependency returns true if the package.json has a dependency with the given name
    /// under any of the dependency fields (dependencies, devDependencies, peerDependencies,
    /// optionalDependencies).
    /// </summary>
    public bool HasDependency(string name)
    {
        // Go: `if deps, ok := df.Dependencies.GetValue(); ok { if _, ok := deps[name]; ok { return true } }`。
        // TryGetValue 的 out 参数即使失败也会被赋值（Valid 为 false 时 Value 是 null），故下面逐段判空。
        if (Dependencies.TryGetValue(out var deps) && deps is not null && deps.ContainsKey(name))
        {
            return true;
        }

        if (DevDependencies.TryGetValue(out var devDeps) && devDeps is not null && devDeps.ContainsKey(name))
        {
            return true;
        }

        if (PeerDependencies.TryGetValue(out var peerDeps) && peerDeps is not null && peerDeps.ContainsKey(name))
        {
            return true;
        }

        if (OptionalDependencies.TryGetValue(out var optDeps) && optDeps is not null && optDeps.ContainsKey(name))
        {
            return true;
        }

        return false;
    }

    /// <summary>Go: <c>RangeDependencies(f func(name, version, dependencyField string) bool)</c>（回调返回 false 即停止）。</summary>
    public void RangeDependencies(Func<string, string, string, bool> f)
    {
        if (Dependencies.TryGetValue(out var deps) && deps is not null)
        {
            foreach (var pair in deps)
            {
                if (!f(pair.Key, pair.Value, "dependencies"))
                {
                    return;
                }
            }
        }

        if (DevDependencies.TryGetValue(out var devDeps) && devDeps is not null)
        {
            foreach (var pair in devDeps)
            {
                if (!f(pair.Key, pair.Value, "devDependencies"))
                {
                    return;
                }
            }
        }

        if (PeerDependencies.TryGetValue(out var peerDeps) && peerDeps is not null)
        {
            foreach (var pair in peerDeps)
            {
                if (!f(pair.Key, pair.Value, "peerDependencies"))
                {
                    return;
                }
            }
        }

        if (OptionalDependencies.TryGetValue(out var optDeps) && optDeps is not null)
        {
            foreach (var pair in optDeps)
            {
                if (!f(pair.Key, pair.Value, "optionalDependencies"))
                {
                    return;
                }
            }
        }
    }

    public Set<string> GetRuntimeDependencyNames()
    {
        // Go: deps, _ := df.Dependencies.GetValue() —— 解不出来时是 nil map（len 0、遍历 0 次）。
        Dependencies.TryGetValue(out var deps);
        PeerDependencies.TryGetValue(out var peerDeps);
        OptionalDependencies.TryGetValue(out var optDeps);

        var count = (deps?.Count ?? 0) + (peerDeps?.Count ?? 0) + (optDeps?.Count ?? 0);
        var names = NewSetWithSizeHint<string>(count);
        if (deps is not null)
        {
            foreach (var name in deps.Keys)
            {
                names.Add(name);
            }
        }

        if (peerDeps is not null)
        {
            foreach (var name in peerDeps.Keys)
            {
                names.Add(name);
            }
        }

        if (optDeps is not null)
        {
            foreach (var name in optDeps.Keys)
            {
                names.Add(name);
            }
        }

        return names;
    }
}

/// <summary>Go: <c>type Fields struct</c>（package.json 里除 typescript 外的全部字段；见文件头第 1 条）。</summary>
public class Fields : IJsonUnmarshaler
{
    public HeaderFields HeaderFields = new();

    public PathFields PathFields = new();

    public DependencyFields DependencyFields = new();

    /// <summary>Go 的 <c>ContentMapper Expected[ContentMapperFields] \`json:"-"\`</c>（由 typescript 字段搬入，见文件头第 3 条）。</summary>
    public Expected<ContentMapperFields> ContentMapper = new();

    // Go: json.Unmarshal(data, &parsed)（parsed = 三个内嵌 group + `TypeScript Expected[typeScriptFields]`）
    public void UnmarshalJSONFrom(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("package.json: expected object");
        }

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (HeaderFields.TryUnmarshalField(name, dec))
            {
                continue;
            }

            if (PathFields.TryUnmarshalField(name, dec))
            {
                continue;
            }

            if (DependencyFields.TryUnmarshalField(name, dec))
            {
                continue;
            }

            if (name == "typescript")
            {
                UnmarshalTypeScriptField(dec);
                continue;
            }

            // Go 的 reflect 解码忽略未知字段。
            dec.SkipValue();
        }

        dec.ReadToken();
    }

    // Go 的 Parse 用局部匿名结构体里的 `TypeScript Expected[typeScriptFields] \`json:"typescript"\`` 承接本字段：
    // Expected 的 UnmarshalJSON 拿的是**整个字段的原始字节**且永不报错，所以字段类型不合法（比如字符串/数组）只是
    // “解不出来”，`typeScript` 保持零值 —— 这里同样先 ReadValue() 再在临时 Decoder 上解，失败就保持零值。
    private void UnmarshalTypeScriptField(Decoder dec)
    {
        var raw = dec.ReadValue();
        var typeScript = new TypeScriptFields();
        try
        {
            typeScript.UnmarshalJSONFrom(new Decoder(raw));
        }
        catch (JsonException)
        {
            // Go: json.Unmarshal 失败 → typeScript 是零值 → ContentMapper 也是零值
        }

        ContentMapper = typeScript.ContentMapper;
    }
}

/// <summary>Go: <c>type ContentMapperFields struct</c>（package.json 的 <c>typescript.contentMapper</c>）。</summary>
public sealed class ContentMapperFields : IJsonUnmarshaler
{
    public Expected<string[]> Exec = new();

    public Expected<string[]> CompilerOptions = new();

    public Expected<bool> DynamicConfig = new();

    public void UnmarshalJSONFrom(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("contentMapper: expected object");
        }

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            switch (name)
            {
                case "exec":
                    Exec.UnmarshalJSONFrom(dec);
                    break;
                case "compilerOptions":
                    CompilerOptions.UnmarshalJSONFrom(dec);
                    break;
                case "dynamicConfig":
                    DynamicConfig.UnmarshalJSONFrom(dec);
                    break;
                default:
                    dec.SkipValue();
                    break;
            }
        }

        dec.ReadToken();
    }
}

/// <summary>
/// Go: <c>type typeScriptFields struct</c>（未导出 → C# internal + PascalCase，docs §2）：
/// package.json 的 <c>typescript</c> 字段的形状。
/// </summary>
internal sealed class TypeScriptFields : IJsonUnmarshaler
{
    public Expected<ContentMapperFields> ContentMapper = new();

    public void UnmarshalJSONFrom(Decoder dec)
    {
        if (dec.ReadToken().Kind != JsonKind.BeginObject)
        {
            throw new JsonException("typescript: expected object");
        }

        while (dec.PeekKind() != JsonKind.EndObject)
        {
            var name = dec.ReadName();
            if (name == "contentMapper")
            {
                ContentMapper.UnmarshalJSONFrom(dec);
                continue;
            }

            dec.SkipValue();
        }

        dec.ReadToken();
    }
}

/// <summary>
/// Go: <c>type PackageJson struct { Fields; Parseable bool; versionPaths VersionPaths; … }</c> +
/// 包级函数 <c>Parse</c>（见文件头第 1、4 条：内嵌 Fields → 继承；包级函数 → 类型上的 public static 成员）。
/// 本文件放 <c>Parseable</c> 与 <c>Parse</c>/<c>TryParse</c>；状态与 `GetVersionPaths`/`NewInfoCache` 在 Cache.cs。
/// </summary>
public sealed partial class PackageJson : Fields
{
    /// <summary>Go 的导出字段 <c>Parseable bool</c>。</summary>
    public bool Parseable;

    /// <summary>
    /// Go: <c>func Parse(data []byte) (Fields, error)</c>（见文件头第 4、5 条：包级函数落在类型上，
    /// 错误路径抛 <see cref="JsonException"/>）。
    /// </summary>
    public static Fields Parse(byte[] data)
    {
        var fields = new Fields();
        try
        {
            UnmarshalInto(data, ref fields, AllowDuplicateNames(true));
        }
        catch (System.Text.Json.JsonException e)
        {
            // Json 包的 Decoder（ReadToken/PeekKind/ReadValue）目前让 STJ 的语法错误异常（JsonException/JsonReaderException）直接漏出来，而
            // docs/json.md 的读侧契约是"错误路径抛 JsonException"。Go 的 Parse 也是把 json 的 error 原样返回，
            // 所以这里统一成 JsonException（已上报 Json 包；修好后这个 catch 变成死代码）。
            throw new JsonException("package.json: invalid JSON", e);
        }

        return fields;
    }

    /// <summary>
    /// Go 的 <c>Parse</c> 里 <c>if err != nil</c> 那一支的 C# 形状（docs/architecture.md §3.2）：
    /// 失败返回 false 且 <paramref name="fields"/> 是**全零的 Fields**（Go 的 <c>return Fields{}, err</c>）。
    /// </summary>
    public static bool TryParse(byte[] data, out Fields fields)
    {
        var parsed = new Fields();
        try
        {
            UnmarshalInto(data, ref parsed, AllowDuplicateNames(true));
        }
        catch (Exception e) when (e is JsonException or System.Text.Json.JsonException)
        {
            // STJ 的语法错误异常见 Parse 的注释（Json 包让它漏出）。
            // 部分解码结果按 Go 丢弃（Go 返回的是零值 Fields）。
            fields = new Fields();
            return false;
        }

        fields = parsed;
        return true;
    }
}
