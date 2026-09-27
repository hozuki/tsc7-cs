# tsc-cs 架构与移植规范

本文是移植工作的**契约**：任何新增代码都必须能在这里找到依据；与本文冲突的写法要么改代码，要么先改本文。

## 1. 目标与验收

- 把 TSGO（`D:\Source\GitHub\TypeScript-Go`，Go）1:1 移植为 C#。TSGO 是 TypeScript 编译器/language server 的 Go 移植。
- **验收**：最终通过 TSGO 仓库里的测试（`baselines`、`tests`）。在此之前，逐个包地复刻 TSGO 的单元测试。
- **API 对齐**：本项目会作为包发布，C# API 要尽量贴近 TSGO 的 Go API：同样的类型名、函数名、参数顺序、返回值形状。允许偏离的地方只有"Go 语言特性在 C# 里没有对应物"或"C# 有更好的对应物"，且必须在文件头注释里写明。
- 性能/内存是硬指标：热点用 struct、避免装箱与多余分配；多线程（`Program`/`Checker` 池、文件解析、LSP）要用线程安全结构，目标是不差于 Go 版本。

## 2. 目录 / 命名空间映射

一个 Go package = 一个同名文件夹 + 一个同名 C# 命名空间。这样任意 `.cs` 都能和 Go 源文件并排阅读。

| TSGO 路径 | C# 文件夹 | C# 命名空间 |
| --- | --- | --- |
| `tsc/internal/core` | `src/TypeScript.Compiler/Core/` | `TypeScript.Core` |
| `tsc/internal/ast` | `src/TypeScript.Compiler/Ast/` | `TypeScript.Ast` |
| `tsc/internal/stringutil` | `src/TypeScript.Compiler/StringUtil/` | `TypeScript.StringUtil` |
| `tsc/internal/tspath` | `src/TypeScript.Compiler/TSPath/` | `TypeScript.TSPath` |
| `tsc/internal/collections` | `src/TypeScript.Compiler/Collections/` | `TypeScript.Collections` |
| `tsc/internal/json` | `src/TypeScript.Compiler/Json/` | `TypeScript.Json` |
| `tsc/internal/jsnum` | `src/TypeScript.Compiler/JsNum/` | `TypeScript.JsNum` |
| `tsc/internal/debug` | `src/TypeScript.Compiler/Debug/` | `TypeScript`（根，见下） |

命名规则：

- 文件名 = Go 文件名去扩展名后转 PascalCase：`js_case.go` → `JsCase.cs`，`identifier_parts_generated.go` → `IdentifierParts.Generated.cs`。
- **唯一例外**：`internal/debug` 的类型放在根命名空间 `TypeScript`。若照搬成 `TypeScript.Debug`，则在任意 `TypeScript.*` 命名空间里 `Debug.Fail(...)` 会先解析到命名空间而不是静态类（编译器只做命名空间查找不落到成员），调用点被迫写 `Debug.Debug.Fail(...)`。放根命名空间后调用点与 Go 的 `debug.Fail(...)` 一一对应。
- **包级函数**：C# 没有自由函数，每个 package 的包级函数放在**与该 package 同名的静态类**里，类放在该 package 的命名空间内，于是全名是 `TypeScript.StringUtil.StringUtil`。文件里这样引用：
  ```csharp
  using TypeScript.StringUtil;                   // 类型：Comparison、UnicodeESNextIdentifierStart…
  using static TypeScript.StringUtil.StringUtil; // 函数：HasPrefix、CompareStringsCaseInsensitive…
  ```
  之后按 Go 的裸函数名调用（`HasPrefix(s, p, true)`），类型则可以用 `StringUtil.Comparison` 这样与 Go 逐字对应的限定名（命名空间限定对类型永远有效）。
  两个包出现同名包级函数时（整个 TSGO 只有 12 处，如 `Parse`、`FromString`、`NewProgram`），在该文件用别名消歧：`using JsNum2 = TypeScript.JsNum.JsNum;`。
  不要用项目级 `global using static`：可读性和消歧都更差。
  注意：命名空间限定只对**该命名空间里声明的类型**有效（`StringUtil.Comparison`），拿不到静态类的成员；静态类成员只有两种写法——`using static` 后的裸名，或全名 `TypeScript.StringUtil.StringUtil.X`。这是 C# 名字解析规则决定的（外层命名空间里同名的命名空间会先被找到），不是风格问题。
- 每个文件第一行是来源标注（生成文件另外标注生成器）：
  ```csharp
  // ported from: internal/stringutil/js_case.go
  ```
- 公开 API 用 PascalCase；**私有**字段/局部变量用 camelCase；Go 的导出名首字母大写语义在 C# 里就是 `public`。
- 命名空间必须显式声明为 file-scoped（`.editorconfig` 已把它设为 warning 级）。

## 3. 类型映射

### 3.1 字符串与索引

Go 源码里字符串是 UTF-8 字节序列，所以 TSGO 有一套"孤代理编码"机制：`EncodeJSStringRune`/`DecodeJSStringRune`/`CombineSurrogatePairs`（`internal/stringutil/util.go`）把孤立代理项编成 UTF-8 哨兵字节，以免 Go 把它替换成 U+FFFD。

C# 的 `string` 本身就是 UTF-16 代码单元序列，**与 JavaScript 字符串同构**，孤立代理项是合法值。因此：

- **不移植**哨兵机制：`EncodeJSStringRune`/`DecodeJSStringRune`/`CombineSurrogatePairs`/`surrogateUTF8Lead` 一律不出现。需要"原样保留孤立代理项"的地方，直接 `builder.Append(char)` 即可。
- 字符串索引一律是 **UTF-16 代码单元下标**（= TS/TSGO 语义上的 `Pos`）。Go 里按字节下标走的循环（`for i := 0; i < len(s); i++`）在 C# 里必须逐个核对是"按代码单元"还是"按码点"：前者用 `char` 下标，后者用 `Rune` 解码后自行推进下标。
- Go 的 `rune` → 字符判定函数用 **`int`**（码点/代理项值），需要真正的码点对象时才用 `System.Text.Rune`。理由（实测）：`Rune` 无法表示孤立代理项（`new Rune(0xD800)` 抛异常），而 `char` 会把 astral 码点静默截断（`IsHighSurrogate((char)0x1D800)` 会错判为 true）。TSGO 里 `unicode.ToLower` 这类纯单码点操作对应 `Rune.ToLowerInvariant`，见 §3.4。

### 3.2 容器、指针、错误

| Go | C# |
| --- | --- |
| `[]T` | `T[]`；只读参数优先 `ReadOnlySpan<T>` |
| nil slice | 语义等同时用 `[]`；Go 区分 nil 与非 nil 的地方用 `T[]?` 并在注释里说明 |
| `map[K]V` | `Dictionary<K,V>`（顺序敏感处用 `Collections.OrderedMap`） |
| Go 的 nil 接收者安全方法 | C# 实例方法在 null 上**不可分派**（调用即 NRE）。所以 Go 里 `if s == nil { … }` 的分支在 C# 侧是死代码，直接删；**调用点**必须写成 `s?.Has(k) ?? false` / `s?.Clone()`，移植调用点时要逐个核对（`Collections` 各类型的文件头列了对应写法）。 |
| `*T` | `T?`（引用类型）或 `ref`/`out`（值类型） |
| `interface{ M() }` | `interface I { M(); }`，结构性满足改为显式实现 |
| `(T, error)` | 正常路径 `T`，错误路径抛异常；"查找失败"这类预期分支用 `bool TryX(..., out T)` |
| `panic` / `debug.Fail` | `Debug.Fail(...)`，抛 `DebugFailureException` |
| `Arena[T].New() *T` | `ref T New()`——只在 **T 是 struct** 时有意义（返回 arena 内部槽位的稳定引用）。引用类型的"稳定指针"在 C# 里就是对象引用本身。AST 节点是 class（与 Go 的 `*Node` 身份/相等语义一致），所以 NodeFactory 不使用 arena。 |
| `LinkStore[K,V]` / `PagedLinkStore[V]` 的 `*V` | `where V : class, new()` + `new V()`；`null` = Go 的 `*V == nil`（`Has`/`TryGet` 语义完全一致）。Go 里 V 常是结构体（紧凑存储），C# 移植成 class：每个槽位一次分配、页是引用数组（性能偏离，待实测）。 |
| Go 用 `reflect` 做**浅拷贝**（如 `CompilerOptions.Clone` 逐字段 `reflect.Value.Set`，Go 源码里自己标了 "could be generated code instead of reflection"） | `MemberwiseClone()`：同样是逐字段浅拷贝、无反射、AOT 安全，不需要生成 150 行赋值 |
| `iota` 枚举 | C# `enum`，**数值与 TS/Go 保持一致**（`ast.Kind` 等值会出现在 baseline 里） |
| `String()` stringer | `ToString()` 覆写 |
| `atomic.*` | `Interlocked` / `Volatile` |
| `sync.Mutex` / `RWMutex` | `lock` / `ReaderWriterLockSlim`；优先无锁或不可变快照 |
| `sync.Map` | `ConcurrentDictionary`（或 TSGO 同款的 `Collections.SyncMap`） |
| `go f()` / channel | `Task` / `Channel<T>`；结构性并发要先在本文登记 |

泛型：C# 泛型比 Go 强，移植时**可以更激进**（Go 里为了绕开泛型而写的 `any` + type switch，在 C# 里直接上泛型或接口）。

### 3.3 枚举与 stringer

- 枚举**数值必须与 Go/TS 一致**（会出现在 baseline、JSON、诊断里）。
- 枚举成员名 = Go 常量名去掉类型前缀：`core.ModuleKindNode16` → `ModuleKind.Node16`，`core.TristateTSUnknown` → `Tristate.TSUnknown`，`core.ScriptTargetES2022` → `ScriptTarget.ES2022`。
- C# 的 enum 不能覆写 `ToString()`，所以 Go 的 stringer（`*_stringer_generated.go`）落成**静态类 `Core` 里的扩展方法** `public static string String(this X value)`：合法值时与 `value.ToString()` 相同（成员名就是 Go 的 stringer 文本），越界值（`ModuleKind(55)`）与 panic 情形（`ModuleResolutionKind` 的 0）由扩展方法复刻。**移植调用点照写 `x.String()`**。
- 包级别名常量（`ScriptTargetLatest`、`LatestStandard`）放静态类里的 `const`，不要塞进枚举（会污染 `ToString()`）。

### 3.4 大小写映射

三套 API 必须分清，别混用：

| Go | C# | 说明 |
| --- | --- | --- |
| `unicode.ToLower/ToUpper`（simple 映射） | `Rune.ToLowerInvariant/ToUpperInvariant` | 单码点映射。.NET 当前的 Unicode 数据与 TSGO 钉住的 15.1 基本一致：`Rune.ToLowerInvariant(0x212A)=0x6B`、`ToUpperInvariant(0x017F)='S'`、`ToLowerInvariant(0x1E9E)=0xDF`，而 Unicode 16 新增的 `U+1C89`/`U+A7CB` 两边都无映射（`artifacts/unicode-probe`，`dotnet run Program.cs`）。版本未来若漂移，受影响的是路径规范化这类边角字符。 |
| `strings.ToLower/ToUpper`（完整多字符映射，JS 语义） | `StringUtil.ToLowerJS/ToUpperJS` | **不能用** .NET 的 `string.ToLower`（它跟随当前区域且表不同）。JS 的 Final_Sigma、`ß→SS`、`İ→i̇` 等行为由 `stringutil` 的生成表 + `JsCase.cs` 复刻。 |
| `strings.EqualFold`（SimpleFold 等价类） | 见下 | .NET 没有等价物：`StringComparison.OrdinalIgnoreCase` 实测 `"k" != "\u212A"`、`"s" != "\u017F"`，而 Go 的 EqualFold 认为相等。`EquateStringCaseInsensitive` 采用"两边取 `Rune.ToLowerInvariant` 后序数比较"，比 OrdinalIgnoreCase 更接近 SimpleFold（`k`/`U+212A` 相等），差异只落在 `ſ`/`ẞ` 这种等价类边角。 |

### 3.5 可空性

`Nullable` 全项目开启。Go 的 nil 指针映射为 `T?`；`!` 只在**同一方法内已证明非空**时使用，不允许"我觉得它不是 null"。

### 3.6 AOT 兼容

`Directory.Build.props` 设了 `<IsAotCompatible>true</IsAotCompatible>`（用户要求），它打开 trim/AOT/单文件分析器；配合 `TreatWarningsAsErrors`，**IL2xxx/IL3xxx 就是编译错误**。已验证：现有代码（StringUtil/TSPath/Debug/测试）在该开关下 0 警告 0 错误。

禁止：

- `System.Text.Json.JsonSerializer`（反射序列化 → IL2026/IL3050）。JSON 由自写引擎 + 静态分派 + 显式 `IJsonMarshaler`/`IJsonUnmarshaler` 承担，见 `docs/json.md`。
- 运行时反射做类型映射/序列化：`Type.GetProperties`/`GetFields`、`PropertyInfo.GetValue`/`SetValue`、`Activator.CreateInstance(Type)`、`MakeGenericType`、`Enum.GetValues(Type)`、`dynamic`。
- 任何 `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]` 标注的 API。确实非用不可时，只能在该调用点加 `[UnconditionalSuppressMessage]`，并在注释里给出"裁剪后为什么仍安全"的论证。

允许：泛型实例化（编译期静态化）、`EqualityComparer<T>.Default`、`Utf8JsonWriter`/`Utf8JsonReader`、`JavaScriptEncoder` 子类、非反射的 `Regex`、`Interlocked`/`Volatile`/`ConcurrentDictionary`。

结论：类型映射必须**静态可分析**。这也是 TSGO 的 Go 代码到处自己实现 `MarshalerTo`/`UnmarshalerFrom` 的原因——移植时保持同样的显式风格，不要用反射"省事"。

### 3.7 版本库卫生

`.gitignore` 用的是 VS 模板，规则按**目录名**匹配，会和移植过来的 Go package 目录名撞车：`internal/debug` → `src/TypeScript.Compiler/Debug/` 曾被 `[Dd]ebug/` 吞掉（文件一直没进版本库）。新增 package 目录后必须：

```sh
git check-ignore -v src/TypeScript.Compiler/<Folder>/x.cs   # 退出码 1 = 没被忽略
```

被忽略时在 `.gitignore` 末尾放行（该文件已有 `Debug/` 的放行条目）。

## 4. 生成代码

TSGO 里 `*_generated.go` 的输入是语言中立的（`tools/scripts/tsc/ast.json`、`internal/stringutil/_scripts` 的 Unicode 数据、`diagnostics/diagnosticMessages.json`…）。C# 侧由 `tools/TypeScript.CodeGen` 消费同一份输入生成 `*.Generated.cs`：

- **Unicode 表**（`identifier_parts_generated.go`、`js_case_generated.go`）：生成器读 TSGO 已提交的生成文件（本机没有 npm 依赖，且这样能保证与 TSGO 出货行为逐位一致），输出等价的 range table + `Rune` 查找。生成器幂等，重跑不产生 diff。
- 生成文件头写清生成器与输入，**不允许手工编辑**。
- 未生成的 Go 文件一律手工移植，保持与 Go 逐行对应，不要"顺便重构"。

## 5. 测试

- 单元测试：`tests/TypeScript.Compiler.Tests`，xunit，按包分目录，1:1 复刻 Go 的 `*_test.go`。Go 的表驱动测试用 `[Theory]`/`[MemberData]`。
- 集成测试（后续）：`tests/TypeScript.Harness` 跑 TSGO `tsc/testdata`（`tests/cases/**` + `baselines/reference/**`）。TSGO 仓库根目录由环境变量 `TSGO_ROOT` 指定，默认探测 `D:\Source\GitHub\TypeScript-Go`。
- 差分验证：本机有全局 `tsc`（TSGO 的 CLI），可用它产生期望输出交叉验证。
- 本机没有 Go 工具链，也没有 `D:\Source\GitHub\TypeScript-Go\node_modules`：任何"必须跑 Go 才能得到"的期望值，改为从 TSGO 已提交的 baseline / 测试数据里取。

## 6. 里程碑

| 里程碑 | 内容 | 状态 |
| --- | --- | --- |
| M0 | 工程骨架：slnx、编译器库、测试工程、CodeGen 工具；移植规范定稿 | done |
| M1a | `Debug`、`StringUtil`（含 Unicode 生成表 + CodeGen 生成器）、`TSPath` | done（200 测试通过；表数据与 Go 源对拍一致） |
| M1b | `Collections`（含 OrderedMap 的 JSON 钩子）、`Json`、`JsNum` | Collections+Json 完成；JsNum 进行中 |
| M2 | `Core`（`Text`、枚举、`CompilerOptions`…） | |
| M3 | `Ast`（由 `ast.json` 生成 + 手写工具函数）、`Diagnostics`（由 `diagnosticMessages.json` 生成）、`Locale` | |
| M4 | `Scanner`、`Parser` → 能 parse 出 AST，跑 parser 基线 | |
| M5 | `Binder`、`Checker` → 语义错误基线 | |
| M6 | `Printer` + transformers → emit 基线（`tests/cases/compiler` 通过） | |
| M7 | `Compiler`/`Program` → `tsc` 可编译真实工程 | |
| M8 | `tsoptions`、`execute`、`watch`、`project`、`ls`/`lsp` | |
