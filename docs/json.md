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

- `TypeScript.Json` 命名空间；包级函数放静态类 `Json`（见 architecture.md §2）。API 形状对齐 `json.go`：`Marshal`、`MarshalIndent`、`MarshalWrite`、`MarshalIndentWrite`、`Unmarshal`、`UnmarshalRead`/`UnmarshalDecode`、`NewDecoder`、`Deterministic(bool)`、`AllowDuplicateNames(bool)`、`WithIndent(string)`、`Encoder`/`Decoder`（Go 的 `jsontext.Encoder`/`Decoder` 对应物）、`IJsonMarshaler`/`IJsonUnmarshaler`（对应 `MarshalerTo`/`UnmarshalerFrom`）。
- 引擎：`System.Text.Json` 的 `Utf8JsonWriter`/`Utf8JsonReader` 做结构/解析，转义、数字、字段映射由我们控制；`Marshal` 的反射映射是我们自己的（不依赖 STJ 的 `JsonSerializer`，以便精确实现 Go 的字段名/`omitempty`/`omitzero`/内嵌结构语义，并且不受 STJ 反射序列化的 AOT/trim 分析影响）。
- Go 的 struct tag → C# attribute：`json:"name,omitempty"` → `[JsonField(Name = "name", OmitEmpty = true)]`（名字待实现时定稿，写进本节）。
- `Deterministic(true)`：Go 默认 map 顺序不稳定，开了以后按 key 排序。C# 侧同样由选项控制（默认按插入/枚举顺序，开了排序）。
- 写文件/写流：Go 的 `io.Writer` → C# `Stream`（字节语义）。

## 3. 验证

- 对拍脚本：`artifacts/json-oracle/` 的 tsconfig + `tsc --showConfig` / `--generateTrace` 期望输出来自身实现对拍（转义、缩进、浮点）。
- 单测：迁移 TSGO 侧能钉住 JSON 文本的期望值（`internal/jsnum` 的 `Number.String()` 向量等价于 `json.Marshal(float64)`）。
