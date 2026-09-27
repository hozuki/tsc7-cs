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
- Go 的 `rune` → `System.Text.Rune`；只可能是 BMP 的内部字符判断（如 `IsWhiteSpaceSingleLine`）参数用 `char` 也可以，但扫描器/解析器对外一律 `Rune`。

### 3.2 容器、指针、错误

| Go | C# |
| --- | --- |
| `[]T` | `T[]`；只读参数优先 `ReadOnlySpan<T>` |
| nil slice | 语义等同时用 `[]`；Go 区分 nil 与非 nil 的地方用 `T[]?` 并在注释里说明 |
| `map[K]V` | `Dictionary<K,V>`（顺序敏感处用 `Collections.OrderedMap`） |
| `*T` | `T?`（引用类型）或 `ref`/`out`（值类型） |
| `interface{ M() }` | `interface I { M(); }`，结构性满足改为显式实现 |
| `(T, error)` | 正常路径 `T`，错误路径抛异常；"查找失败"这类预期分支用 `bool TryX(..., out T)` |
| `panic` / `debug.Fail` | `Debug.Fail(...)`，抛 `DebugFailureException` |
| `iota` 枚举 | C# `enum`，**数值与 TS/Go 保持一致**（`ast.Kind` 等值会出现在 baseline 里） |
| `String()` stringer | `ToString()` 覆写 |
| `atomic.*` | `Interlocked` / `Volatile` |
| `sync.Mutex` / `RWMutex` | `lock` / `ReaderWriterLockSlim`；优先无锁或不可变快照 |
| `sync.Map` | `ConcurrentDictionary`（或 TSGO 同款的 `Collections.SyncMap`） |
| `go f()` / channel | `Task` / `Channel<T>`；结构性并发要先在本文登记 |

泛型：C# 泛型比 Go 强，移植时**可以更激进**（Go 里为了绕开泛型而写的 `any` + type switch，在 C# 里直接上泛型或接口）。

### 3.3 可空性

`Nullable` 全项目开启。Go 的 nil 指针映射为 `T?`；`!` 只在**同一方法内已证明非空**时使用，不允许"我觉得它不是 null"。

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
| M0 | 工程骨架：slnx、编译器库、测试工程、CodeGen 工具；移植规范定稿 | 进行中 |
| M1 | 叶子包：`Debug`、`StringUtil`（含生成表）、`Json`、`Collections`、`TSPath`、`JsNum` | |
| M2 | `Core`（`Text`、枚举、`CompilerOptions`…） | |
| M3 | `Ast`（由 `ast.json` 生成 + 手写工具函数）、`Diagnostics`（由 `diagnosticMessages.json` 生成）、`Locale` | |
| M4 | `Scanner`、`Parser` → 能 parse 出 AST，跑 parser 基线 | |
| M5 | `Binder`、`Checker` → 语义错误基线 | |
| M6 | `Printer` + transformers → emit 基线（`tests/cases/compiler` 通过） | |
| M7 | `Compiler`/`Program`/`TSPath` 命令行 → `tsc` 可编译真实工程 | |
| M8 | `tsoptions`、`execute`、`watch`、`project`、`ls`/`lsp` | |
