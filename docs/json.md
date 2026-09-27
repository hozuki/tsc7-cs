# JSON 契约（`internal/json` 的移植）

TSGO 的 `tsc/internal/json/json.go` 只是 `encoding/json/v2`（+ `jsontext`）的一层薄包装，而 Go 的 json/v2 输出会直接进入 baseline（`--showConfig`、trace、types dump、API 协议、`.tsbuildinfo`），所以**逐字节**对齐是硬要求。本文件记录事实与决定；实现见 `src/TypeScript.Compiler/Json/`。

## 1. 事实：Go json/v2 的实际输出

本机没有 Go 工具链，但有全局 `tsc`（TSGO CLI，7.0.2）——它内部就是这套 Go 代码，因此**实测**是权威取证手段。

### 1.1 取证命令

```sh
# 缩进输出（showConfig 用 "    "；写 stdout，无尾部换行）
printf '{\n  "compilerOptions": { "types": ["<tag>&amp", "a\\u2028b", "\\u00e9\\u4e2d\\ud83d\\ude00"] },\n  "files": ["a.ts"]\n}\n' > cfg/tsconfig.json
echo 'export const x = 1;' > cfg/a.ts
tsc --showConfig -p cfg | od -c     # artifacts/json-oracle/showconfig.out

# compact 输出（trace/types dump）
tsc --generateTrace trace -p cfg && head -c 600 trace/trace.json    # artifacts/json-oracle/trace/
```

### 1.2 已证实的行为（每条都有上面的实测出处）

| 事实 | 实测 |
| --- | --- |
| 缩进模式分隔符是 `": "`（冒号后有空格）、换行 `\n`、**无**尾部换行；缩进串由调用方给（showConfig 传 `"    "`） | `showconfig.out` 十六进制 |
| compact 模式是 `"k":v`、`,` 后无空格、`\n` 无关（单行） | `trace/trace.json` |
| `<`/`>`/`&` **不转义**；非 ASCII（`é`/`中`/emoji/U+2028/U+2029）**原样输出 UTF-8 字节** | `showconfig.out`：`303 251`、`342 200 250`、`360 237 230 200` |
| 转义表：`"`→`\"`、`\`→`\\`、`\b`(08)→`\b`、`\f`(0C)→`\f`、`\n`→`\n`、`\r`→`\r`、`\t`→`\t`，其余 `<0x20` → `\u00xx` **小写十六进制**（`\u001a`、`\u001f`、`\u000b`、`\u0000`）；0x7F 与 ≥0x80 一律原样 | `json-oracle/ctrl.out` |
| float64：`-2.109808898695963e16` → `-21098088986959630`（十进制，无指数）；`1e21` → `1e+21`；`1e-7` → `1e-7`；`5e-324` → `5e-324`；`2.2250738585072014e-308` → 原样。即 `abs != 0 && (abs < 1e-6 \|\| abs >= 1e21)` 用指数形式，指数用**小写 e**、带符号、**不留前导零**（`e-7` 而非 `e-07`） | `trace.json`（`"ts":520.2`）+ TSGO 自己的测试向量（`internal/jsnum/string_test.go`、`ryu_test.go` 里 `Number.String()` 期望值） |
| 非有限值（NaN/Inf）不能编码（Go 返回错误），调用方必须先处理 | `internal/jsnum/jsnum.go` 的 `Number.String()` 在调 `json.Marshal` 前就先分派 NaN/Infinity |
| `AllowInvalidUTF8(true)` 是 TSGO 强制注入的选项（`json.go:11`）：孤立代理项按 WTF-8 原样写出，而不是替换成 U+FFFD | 实现层证据（Go 源码），仓库无 baseline 钉住 → 归入待实测 |

### 1.3 .NET 侧的坑（已实测，见 `artifacts/stj-probe`）

- `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` **不满足**上面的表：它会把 U+2028/U+2029 转义、把 astral 字符编码成 `\uD83D\uDE00`、控制字符用**大写**十六进制（`\u001F`）。⇒ 必须自己实现 `JavaScriptEncoder` 子类，逐条照抄 §1.2 的表。
- `Utf8JsonWriter` 的 `Indented`/`IndentCharacter`/`IndentSize`/`NewLine` 能精确产生 Go 的多行形状（`": "`、LF、无尾部换行）⇒ 结构写入用它。
- 数字：STJ 写出 `2.2250738585072014E-308`、`1E+21`、`1E-07`（大写 E、指数零填充），与 Go 不同 ⇒ **不能用** `WriteNumberValue`，必须自己格式化后 `WriteRawValue`。Go 的 `strconv.FormatFloat(f, 'f'|'e', -1, 64)` 的最短往返数字与 .NET 的 `"R"` 输出同为"最短且可往返"，用后者取数字、再按 §1.2 的规则重排。

## 2. 决定：C# 侧结构

- `TypeScript.Json` 命名空间；包级函数放静态类 `Json`（见 architecture.md §2）。API 形状对齐 `json.go`：`Marshal`、`MarshalIndent`、`MarshalWrite`、`MarshalIndentWrite`、`MarshalEncode`、`Unmarshal`、`UnmarshalRead`/`UnmarshalDecode`、`NewDecoder`、`Deterministic(bool)`、`AllowDuplicateNames(bool)`、`WithIndent(string)`、`Encoder`/`Decoder`（Go 的 `jsontext.Encoder`/`Decoder` 对应物）、`IJsonMarshaler`/`IJsonUnmarshaler`（对应 `MarshalerTo`/`UnmarshalerFrom`）。
  - 调用点用 `using static TypeScript.Json.Json;` 拿到裸函数名（`Marshal(...)`、`WithIndent(...)`，与 Go 的 `json.Marshal(...)` 逐字对应）。**静态字段/token 不能写 `Json.BeginObject`**：`Json` 同时是命名空间名，C# 名字解析会先找到命名空间（architecture.md §2 的同名陷阱），所以要么 `using static` 后写裸 `BeginObject`，要么写全 `TypeScript.Json.Json.BeginObject`。token 用 `JsonToken.String/Number/Bool` 工厂构造。
- **AOT 兼容**（`Directory.Build.props` 的 `IsAotCompatible=true`，warnings-as-errors）：整个包**不用运行时反射**。`Activator.CreateInstance(Type)`、`Type.MakeGenericType`、`Array.CreateInstance(Type,…)`、`PropertyInfo`/`FieldInfo`、`JsonSerializer` 一律不用（IL3050/IL2067/IL2090 会把构建打挂）。映射全部是**静态分派**：`is` 模式匹配（写侧）+ `typeof(T)` 的元数据（`IsArray`/`IsGenericType`/`GetElementType`/`GetGenericArguments`/`IsEnum`，这些不需要动态代码也不触发 IL 警告）。
  - Go 里靠 reflect 的 struct 字段/tag 映射在 C# 侧没有对应物 → 那些类型必须自己实现 `IJsonMarshaler`/`IJsonUnmarshaler`（见 §4）。**没有 `JsonFieldAttribute`**：字段名与 `omitempty`/`omitzero` 语义由显式 mapper 承担。
  - 创建实例只能来自两处：`UnmarshalNew<T>() where T : new()`（C# 的 `new T()`，Go 侧是 `reflect.New`）或调用方给出的实例（`UnmarshalInto`/`Decoder.DecodeInto`）。C# 的 `new()` 约束**不接受数组类型**，所以数组目标走 `UnmarshalInto`（长度必须与 JSON 一致，多了少了都报错）。集合的**元素**如果要创建（`List<Foo>`、`Dictionary<string,Bar>` 里 Foo/Bar 是对象或嵌套集合）同样解不动 → 抛带类型名的异常；把这类值保持成 `object?`（形状解码）或让外层类型自己实现 `IJsonUnmarshaler` 即可（TSGO 里的协议/配置类型本来就是手写 mapper）。
- 编解码覆盖的形状（静态分派）：

  | 形状 | 写（`Marshal`） | 读 |
  | --- | --- | --- |
  | `null` / `bool` / `string` / `char` | ✓（`char` 当 1 字符串） | `Unmarshal<T>`（`char` 要求长度 1） |
  | 整数类型 / `double` / `float` / `decimal` / `Half` / `BigInteger` / `Int128`/`UInt128` | ✓ 十进制；浮点按 Go 规则（见下） | `Unmarshal<T>`（`Unmarshal<object?>` 的数字是 `double`，与 Go 的 `any` 一致） |
  | `byte[]` | base64 字符串（Go 的 `[]byte`） | `Unmarshal<byte[]>`（base64 解码） |
  | `enum` | 底层整数 | 按底层整数 `Enum.ToObject` |
  | `IJsonMarshaler` / `IJsonUnmarshaler` | 优先于一切内建形状 | `UnmarshalNew<T>` / `UnmarshalInto` |
  | `IDictionary`（`Dictionary<K,V>` 等） | 对象；键支持 string/整数/枚举，其它键类型抛异常 | `UnmarshalNew<Dictionary<K,V>>`，键从名字按 K 解析 |
  | `IEnumerable`（`T[]`、`List<T>`、`IReadOnlyList<T>`…） | 数组 | `UnmarshalNew<List<T>>`；数组用 `UnmarshalInto` |
  | `object?`（any） | 按动态类型 | 形状解码：对象 → `Dictionary<string, object?>`、数组 → `List<object?>`、数字 → `double`、其余 string/bool/null |
  | 其它类型 | `JsonException("json: type X must implement IJsonMarshaler")` | `JsonException("… type X must implement IJsonUnmarshaler, or be created by the caller …")` |

- **转义**：`Utf8JsonWriter` 配 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`，再由 `OutputFilterStream` **按字节修正**成 Go 的字节（不是 STJ 的）。为什么不直接继承 `JavaScriptEncoder`：它的两个抽象成员 `FindFirstCharacterToEncode(char*, int)`/`TryEncodeUnicodeScalar(int, char*, int, out int)` 是指针签名，实现它们必须开 `AllowUnsafeBlocks`（本包不改 csproj）；`OutputFilterStream.cs` 头注列了完整修正表（控制字符小写十六进制、其余 `\uXXXX` 解回原始 UTF-8 字节、代理对/孤立代理项、`<`/`>`/`&` 不转义）。可观察字节与 Go 一致（§3 的对拍）。
- **数字**：不用 `WriteNumberValue`（会写 `1E+21`/`1E-07`/`2.2250738585072014E-308`）。`FloatFormat` 先取 .NET `"R"` 的最短往返数字串，再按 Go 的规则重排（`abs != 0 && (abs < 1e-6 || abs >= 1e21)` → 指数形式：小写 `e`、带符号、指数不留前导零；否则十进制），最后 `WriteRawValue` 写出。**实测坑**：`Utf8JsonWriter.WriteRawValue` 不参与缩进（数组元素之间既不换行也不缩进），所以原样写的值落在数组里时由 `Encoder` 自己补 `"\n" + 缩进`；对象成员的值紧跟 `": "`，不能补。
- **缩进**：`Indented` + `IndentCharacter`/`IndentSize` + `NewLine = "\n"` 产生 Go 的多行形状（`"k": v`、LF、无尾部换行）。缩进串不是“同一种空格/制表符重复”或带 prefix 时，writer 用 `IndentSize = 1`，由 `OutputFilterStream` 按“行首空格数＝层级”重排成 `prefix + indent×层级`。`HasIndent` 记录“是否给过缩进选项”而不是缩进串非空——Go 的 `WithIndent("")` 依然是多行（`MarshalIndent(v, "", "")` 才是 compact，照抄 json.go 的空缩进特判）。
- `Deterministic(true)`：Go 默认 map 顺序不稳定，开了以后按 key 排序（string 键序数比较、其它键 IComparable）。C# 侧同样由选项控制（默认 `Dictionary` 枚举顺序）。注意 Go 只对 map 排序、结构体保持声明序；C# 侧形状解码出来的都是 `Dictionary`，所以对 `object?` 形状开 Deterministic 会连“结构体”一起排序。
- 写文件/写流：Go 的 `io.Writer` → C# `Stream`（字节语义）。`Decoder` 在构造时把输入整体读进内存再用 `Utf8JsonReader` 增量解析（`Utf8JsonReader` 是 ref struct，不能长期持有），一个 Decoder 只服务一个顶层值。
- 转义之外的几处行为差异：`AllowDuplicateNames(false)` 是默认（读侧对象/字典/形状都拒绝重复名字，开了以后最后一个胜出）；写侧不做重复名字检查（Go 的 jsontext.Encoder 会查，但 C# 侧字典/显式 mapper 不可能产生重复键）；NaN/±Inf 抛 `JsonException`（Go 返回 error，不是 `null`）；`Encoder.WriteValue`/`MarshalJSONTo` 里原样写的 JSON 也会经过 `OutputFilterStream` 的转义修正（Go 的 `WriteValue` 是纯字节透传，只有字符串里的转义形式会被改写）；`DecodeInto` 解字典时先 `Clear()`（Go 的 json/v2 是替换语义）。

## 3. 验证

- 对拍脚本：`artifacts/json-oracle/` 的 tsconfig + `tsc --showConfig` / `--generateTrace` 期望输出来自身实现对拍（转义、缩进、浮点）。
- 单测：迁移 TSGO 侧能钉住 JSON 文本的期望值（`internal/jsnum` 的 `Number.String()` 向量等价于 `json.Marshal(float64)`）。

## 4. 如何为某个类型提供 JSON

AOT 下没有反射映射（§2），所以 Go 里靠 reflect 自动映射的结构体，在 C# 侧必须显式实现 mapper。
TSGO 本来就有大量类型自己实现 `MarshalerTo`/`UnmarshalerFrom`（`internal/lsp/lsproto`、`internal/api`、
`internal/collections/ordered_map.go`…），移植时按同样方式写即可。

1. 类型实现 `IJsonMarshaler`/`IJsonUnmarshaler`（= Go 的 `MarshalerTo`/`UnmarshalerFrom`）：

```csharp
using TypeScript.Json;
using static TypeScript.Json.Json;   // Marshal/Unmarshal 这些裸函数名

public sealed class Foo : IJsonMarshaler, IJsonUnmarshaler
{
    public string Name { get; set; } = "";
    public int Count { get; set; }

    public void MarshalJSONTo(Encoder encoder)
    {
        encoder.WriteToken(BeginObject);                              // Json.BeginObject（using static 后可裸写）
        encoder.WriteToken(JsonToken.String("name"));                 // 对象里处于“等属性名”位置 → 写成属性名
        encoder.WriteToken(JsonToken.String(Name));
        encoder.WriteToken(JsonToken.String("count"));
        encoder.WriteToken(JsonToken.Number(Count.ToString(CultureInfo.InvariantCulture)));
        encoder.WriteToken(EndObject);
    }

    public void UnmarshalJSONFrom(Decoder decoder)
    {
        if (decoder.ReadToken().Kind != JsonKind.BeginObject) throw new JsonException("expected object");
        while (decoder.PeekKind() != JsonKind.EndObject)
        {
            var name = decoder.ReadToken().StringValue;               // 名字也是 token（Kind == String）
            switch (name)
            {
                case "name": Name = decoder.Decode<string>() ?? ""; break;
                case "count": Count = decoder.Decode<int>(); break;
                default: decoder.SkipValue(); break;                  // 未知字段跳过
            }
        }
        decoder.ReadToken();                                          // }
    }
}
```

与 Go 的 `structcodec.go`/`ordered_map.go` 同形的另一种写法：`PeekKind()` + `ReadValue()`（名字连引号一起返回，
且只消费名字本身）+ `Decoder.Decode<T>()`（相当于 `json.UnmarshalDecode`）；`Json.MarshalEncode(encoder, value)`
在对象里写字符串时也会写成属性名（Go 的 `json.MarshalEncode(enc, key)` 就是这样用的）。

2. 读写入口：

   | 场景 | 写法 |
   | --- | --- |
   | 写 | `Marshal(foo)`（不需要额外注册，`IJsonMarshaler` 优先于一切内建形状） |
   | 读、自己 new | `UnmarshalNew<Foo>(bytes)`（要求 public 无参构造；Go 的 `reflect.New`） |
   | 读、调用方给实例 | `UnmarshalInto(bytes, ref foo)` / `decoder.DecodeInto(foo)`（Go 的 `json.Unmarshal(data, &foo)`） |
   | 不实现接口 | `JsonException("json: type X must implement IJsonMarshaler")` / `… must implement IJsonUnmarshaler, or be created by the caller …` |

3. 字段名与 `omitempty`/`omitzero` 由 mapper 自己承担（没有 `JsonFieldAttribute`）：名字就是
   `JsonToken.String("name")` 里的字面量；想省略就自己判断（`if (Count != 0)`）；Go 里 `json:"-"` 的字段
   在 C# 侧就是不写。`Deterministic(true)` 只影响 `IDictionary` 的键顺序。

4. 内嵌（匿名）字段：在 mapper 里把内层类型写进**同一个**对象（`inner.MarshalJSONTo(encoder)`），
   读侧按名字把字段分发给内层（`inner.UnmarshalJSONFrom` 期待自己处理整个对象，需要时拆成逐字段方法）。
